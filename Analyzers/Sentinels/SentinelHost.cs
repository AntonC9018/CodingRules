using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal sealed class SentinelHost
{
    private SentinelHost(SyntaxNode declaration, IMethodSymbol symbol, SyntaxNode body, SyntaxNode type)
    { Declaration = declaration; Symbol = symbol; Body = body; Type = type; }
    public SyntaxNode Declaration { get; }
    public IMethodSymbol Symbol { get; }
    public SyntaxNode Body { get; }
    public SyntaxNode Type { get; }
    public bool IsApi => Symbol.MethodKind is not (MethodKind.LocalFunction or MethodKind.AnonymousFunction)
        && (Symbol.DeclaredAccessibility != Accessibility.Private || Symbol.ExplicitInterfaceImplementations.Length != 0
            || Symbol.AssociatedSymbol is IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 });

    public static SentinelHost? Create(SyntaxNode node, SemanticModel model, CancellationToken token)
    {
        var symbol = node is AnonymousFunctionExpressionSyntax
            ? (model.GetOperation(node, token) as IAnonymousFunctionOperation)?.Symbol
            : model.GetDeclaredSymbol(node, token) as IMethodSymbol;
        SyntaxNode? body = null, type = null;
        switch (node)
        {
            case MethodDeclarationSyntax method: body = (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression; type = method.ReturnType; break;
            case LocalFunctionStatementSyntax local: body = (SyntaxNode?)local.Body ?? local.ExpressionBody?.Expression; type = local.ReturnType; break;
            case OperatorDeclarationSyntax op: body = (SyntaxNode?)op.Body ?? op.ExpressionBody?.Expression; type = op.ReturnType; break;
            case ConversionOperatorDeclarationSyntax op: body = (SyntaxNode?)op.Body ?? op.ExpressionBody?.Expression; type = op.Type; break;
            case AccessorDeclarationSyntax accessor when accessor.Parent?.Parent is BasePropertyDeclarationSyntax property:
                body = (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression; type = property.Type; break;
            case PropertyDeclarationSyntax property:
                symbol = (model.GetDeclaredSymbol(property, token) as IPropertySymbol)?.GetMethod;
                body = property.ExpressionBody?.Expression; type = property.Type; break;
            case IndexerDeclarationSyntax indexer:
                symbol = (model.GetDeclaredSymbol(indexer, token) as IPropertySymbol)?.GetMethod;
                body = indexer.ExpressionBody?.Expression; type = indexer.Type; break;
            case AnonymousFunctionExpressionSyntax anonymous: body = anonymous.Body; type = anonymous; break;
        }
        if (symbol is null || body is null || type is null || symbol.ReturnType.SpecialType != SpecialType.System_Int32
            || symbol.ReturnsByRef || symbol.ReturnsByRefReadonly || symbol.IsAsync || symbol.IsExtern
            || body.ContainsDiagnostics || node.AncestorsAndSelf().Any(parent => parent is UnsafeStatementSyntax)
            || node.DescendantTokens().Any(t => t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.UnsafeKeyword))) return null;
        // The implementation owns the proof; report a partial member at its shared declaration.
        var definition = symbol.PartialDefinitionPart;
        if (definition?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(token) is MethodDeclarationSyntax partial) type = partial.ReturnType;
        return new SentinelHost(node, symbol, body, type);
    }

    public bool Imposed(SemanticModel model, CancellationToken token)
    {
        if (Symbol.MethodKind == MethodKind.AnonymousFunction)
        {
            SyntaxNode context = Declaration;
            while (context.Parent is ParenthesizedExpressionSyntax parentheses) context = parentheses;
            var argument = context.Parent as ArgumentSyntax;
            if (argument is null) return false;
            if (model.GetOperation(argument, token) is not IArgumentOperation bound || bound.Parent is not IInvocationOperation call) return true;
            if (!External(call.TargetMethod)) return false;
            var original = call.TargetMethod.OriginalDefinition.Parameters.ElementAtOrDefault(bound.Parameter!.Ordinal)?.Type as INamedTypeSymbol;
            // A generic result selected/inferred by the application is not a fixed-int obligation.
            return original?.DelegateInvokeMethod?.ReturnType.SpecialType == SpecialType.System_Int32;
        }
        for (var current = Symbol.OverriddenMethod; current is not null; current = current.OverriddenMethod)
            if (External(current)) return true;
        if (Symbol.ExplicitInterfaceImplementations.Any(External)) return true;
        foreach (var contract in Symbol.ContainingType.AllInterfaces)
        foreach (var member in contract.GetMembers())
        {
            token.ThrowIfCancellationRequested();
            var implementation = Symbol.ContainingType.FindImplementationForInterfaceMember(member);
            if (!SymbolEqualityComparer.Default.Equals(implementation, Symbol)
                && !SymbolEqualityComparer.Default.Equals(implementation, Symbol.AssociatedSymbol)) continue;
            if (External(member)) return true;
        }
        return false;
    }

    internal static bool External(ISymbol symbol) => symbol.DeclaringSyntaxReferences.Length == 0
        || symbol.ContainingType is not null && Generated(symbol.ContainingType)
        || Generated(symbol);

    private static bool Generated(ISymbol symbol) =>
        symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() is
            "System.CodeDom.Compiler.GeneratedCodeAttribute" or "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
        || symbol.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree.FilePath.EndsWith(".g.cs", System.StringComparison.OrdinalIgnoreCase)
            || reference.SyntaxTree.GetRoot().GetLeadingTrivia().ToFullString().Contains("<auto-generated"));

    public static bool Nested(SyntaxNode node) => node is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax;
}
