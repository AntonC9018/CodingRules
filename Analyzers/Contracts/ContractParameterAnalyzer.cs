using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractParameterAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ContractParameterGroup,
        "Group parameters on oversized owned contracts",
        "Group the {0} parameters of this owned contract into cohesive inputs",
        "Readability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Owned interface, virtual/abstract, delegate and abstract-class constructor contracts with more than three parameters need cohesive argument groups. Architectural migration requires manual review.",
        helpLinkUri: "https://github.com/AntonC9018/CodingRules/blob/main/docs/contract-parameters.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration,
            SyntaxKind.DelegateDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (context.Node.ContainsDiagnostics) return;
        var model = context.SemanticModel;
        var cancellation = context.CancellationToken;
        IMethodSymbol? contract;
        Location location;
        switch (context.Node)
        {
            case MethodDeclarationSyntax method:
                contract = model.GetDeclaredSymbol(method, cancellation);
                if (contract is not { Parameters.Length: > 3 }) return;
                if (!OwnsMethodContract(contract)) return;
                location = method.Identifier.GetLocation();
                break;
            case ConstructorDeclarationSyntax constructor:
                contract = model.GetDeclaredSymbol(constructor, cancellation);
                if (contract is null) return;
                if (contract.IsStatic || !contract.ContainingType.IsAbstract) return;
                location = constructor.Identifier.GetLocation();
                break;
            case DelegateDeclarationSyntax declaration:
                var delegateType = model.GetDeclaredSymbol(declaration, cancellation);
                if (HasExternalSignature(delegateType, model.Compilation)) return;
                contract = delegateType?.DelegateInvokeMethod;
                location = declaration.Identifier.GetLocation();
                break;
            case TypeDeclarationSyntax { ParameterList.Parameters.Count: > 3 } declaration:
                var type = model.GetDeclaredSymbol(declaration, cancellation);
                if (type is not { TypeKind: TypeKind.Class, IsAbstract: true }) return;
                // Primary constructors have no ConstructorDeclarationSyntax of their own.
                contract = (model.GetDeclaredSymbol(declaration.ParameterList.Parameters[0], cancellation)
                    as IParameterSymbol)?.ContainingSymbol as IMethodSymbol;
                location = declaration.Identifier.GetLocation();
                break;
            default:
                return;
        }

        if (contract is null) return;
        var count = contract.Parameters.Length;
        var cancellationType = model.Compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        if (count > 0 && cancellationType?.DeclaringSyntaxReferences.IsEmpty == true
            && contract.Parameters[count - 1].RefKind == RefKind.None
            && SymbolEqualityComparer.Default.Equals(contract.Parameters[count - 1].Type, cancellationType))
        {
            count--;
        }
        if (count <= 3) return;
        if (contract.Parameters.Any(parameter => HasErrorType(parameter.Type))) return;
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, count));
    }

    private static bool HasExternalSignature(INamedTypeSymbol? type, Compilation compilation)
    {
        var marker = PolicyAttributeIdentity.Resolve(compilation, "CodingRules.ExternalSignatureAttribute");
        if (type is null || marker is null) return false;
        return type.GetAttributes().Any(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker)
            && attribute.ConstructorArguments.Length == 2
            && attribute.ConstructorArguments[0].Value is INamedTypeSymbol externalType
            && externalType.TypeKind != TypeKind.Error
            && externalType.DeclaringSyntaxReferences.IsEmpty
            && !SymbolEqualityComparer.Default.Equals(externalType.ContainingAssembly, compilation.Assembly)
            && attribute.ConstructorArguments[1].Value is string memberName
            && !string.IsNullOrWhiteSpace(memberName));
    }

    private static bool OwnsMethodContract(IMethodSymbol method)
    {
        // The declaration that introduces the contract owns the finding. Implementations,
        // reabstracted/sealed overrides and partial implementation parts do not repeat it.
        if (method.IsOverride || method.PartialDefinitionPart is not null) return false;
        if (!method.ExplicitInterfaceImplementations.IsEmpty) return false;
        var type = method.ContainingType;
        foreach (var contract in type.AllInterfaces)
        {
            foreach (var member in contract.GetMembers(method.Name).OfType<IMethodSymbol>())
            {
                var implementation = type.FindImplementationForInterfaceMember(member);
                if (SymbolEqualityComparer.Default.Equals(method, implementation)) return false;
            }
        }

        if (method.IsAbstract || method.IsVirtual) return true;
        if (method.IsStatic) return false;
        if (type.TypeKind != TypeKind.Interface) return false;
        return method.DeclaredAccessibility != Accessibility.Private;
    }

    private static bool HasErrorType(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Error) return true;
        if (type is IArrayTypeSymbol array) return HasErrorType(array.ElementType);
        if (type is IPointerTypeSymbol pointer) return HasErrorType(pointer.PointedAtType);
        if (type is INamedTypeSymbol named)
        {
            if (named.ContainingType is { } containing && HasErrorType(containing)) return true;
            return named.TypeArguments.Any(HasErrorType);
        }
        if (type is IFunctionPointerTypeSymbol function)
        {
            if (HasErrorType(function.Signature.ReturnType)) return true;
            return function.Signature.Parameters.Any(parameter => HasErrorType(parameter.Type));
        }
        return false;
    }
}
