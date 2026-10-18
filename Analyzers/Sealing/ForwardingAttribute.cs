using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

internal static class ForwardingAttribute
{
    public static bool IsBase(INamedTypeSymbol symbol, Compilation compilation, CancellationToken cancellationToken)
    {
        if (symbol.IsGenericType) return false;
        var attribute = compilation.GetTypeByMetadataName("System.Attribute");
        if (attribute is null || attribute.Locations.Any(location => location.IsInSource)) return false;
        var derivesFromAttribute = false;
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (Same(current, attribute)) derivesFromAttribute = true;
        }
        if (!derivesFromAttribute) return false;

        // A generic variant has the same name and owner; arbitrary inheritors
        // (including generic ones) do not make an attribute base acceptable.
        var variants = symbol.ContainingType is { } owner
            ? owner.GetTypeMembers(symbol.Name, arity: 1)
            : symbol.ContainingNamespace.GetTypeMembers(symbol.Name, arity: 1);
        return variants.Any(variant => IsWrapper(variant, symbol, compilation, cancellationToken));
    }

    private static bool IsWrapper(INamedTypeSymbol variant, INamedTypeSymbol baseType,
        Compilation compilation, CancellationToken cancellationToken)
    {
        if (!Same(variant.BaseType, baseType)) return false;
        if (variant.GetMembers().Any(member => !member.IsImplicitlyDeclared
            && member is not IMethodSymbol { MethodKind: MethodKind.Constructor })) return false;

        var constructors = variant.InstanceConstructors;
        if (constructors.IsEmpty) return false;
        foreach (var constructor in constructors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (constructor.IsImplicitlyDeclared) return false;
            if (constructor.Parameters.Length != 0) return false;
            var syntax = constructor.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax(cancellationToken);
            SyntaxNode? call;
            ArgumentListSyntax? arguments;
            if (syntax is ConstructorDeclarationSyntax declaration)
            {
                if (declaration.Body is not { Statements.Count: 0 } || declaration.ExpressionBody is not null) return false;
                call = declaration.Initializer;
                arguments = declaration.Initializer?.ArgumentList;
            }
            else if (syntax is TypeDeclarationSyntax typeDeclaration)
            {
                var primaryBase = typeDeclaration.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().SingleOrDefault();
                call = primaryBase;
                arguments = primaryBase?.ArgumentList;
            }
            else return false;

            if (call is null || arguments?.Arguments.Count != 1 || call.ContainsDiagnostics) return false;
            if (arguments.Arguments[0].Expression is not TypeOfExpressionSyntax typeOf) return false;
            var model = compilation.GetSemanticModel(call.SyntaxTree);
            if (model.GetSymbolInfo(call, cancellationToken).Symbol is not IMethodSymbol target) return false;
            if (!Same(target.ContainingType, baseType)) return false;
            if (target.Parameters.Length != 1) return false;
            if (!Same(target.Parameters[0].Type, compilation.GetTypeByMetadataName("System.Type"))) return false;
            if (!Same(model.GetTypeInfo(typeOf.Type, cancellationToken).Type, variant.TypeParameters[0])) return false;
            if (model.GetDiagnostics(call.Span, cancellationToken).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return false;
        }
        return true;
    }

    private static bool Same(ISymbol? first, ISymbol? second) => SymbolEqualityComparer.Default.Equals(first, second);
}
