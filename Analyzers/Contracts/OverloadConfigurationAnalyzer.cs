using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OverloadConfigurationAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.OverloadConfigurationBuilder,
        "Use a builder for multi-parameter overload alternatives",
        "Use a configuration builder for '{0}': this multi-parameter operation has alternative parameter forms",
        "Readability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Multi-parameter methods with a same-type, same-name overload differing in exactly one parameter type need a configuration builder. API migration and required-configuration checks require manual review and runtime validation.",
        helpLinkUri: "https://github.com/AntonC9018/CodingRules/blob/main/docs/overload-configuration.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        if (declaration.ContainsDiagnostics) return;
        var method = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
        if (method is not { Parameters.Length: > 1 }) return;
        // A partial implementation is the same method, not another overload site.
        if (method.PartialDefinitionPart is not null) return;
        if (method.Parameters.Any(parameter => HasErrorType(parameter.Type))) return;

        foreach (var alternative in method.ContainingType.GetMembers(method.Name).OfType<IMethodSymbol>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (SymbolEqualityComparer.Default.Equals(method, alternative)) continue;
            if (alternative.PartialDefinitionPart is not null) continue;
            if (alternative.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(context.CancellationToken).ContainsDiagnostics)) continue;
            if (!IsAlternative(method, alternative)) continue;
            context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), method.Name));
            return;
        }
    }

    private static bool IsAlternative(IMethodSymbol method, IMethodSymbol alternative)
    {
        if (method.IsStatic != alternative.IsStatic) return false;
        if (method.Arity != alternative.Arity) return false;
        if (method.Parameters.Length != alternative.Parameters.Length) return false;
        if (alternative.Parameters.Any(parameter => HasErrorType(parameter.Type))) return false;

        // Type parameters from distinct methods have distinct symbol identities.
        // Substitute by ordinal so M<T>(T, Stream) and M<U>(U, string) compare
        // their shared generic input as equal, including nested generic types.
        if (method.Arity > 0)
        {
            alternative = alternative.Construct(method.TypeParameters.Cast<ITypeSymbol>().ToArray());
        }

        var differences = 0;
        for (var index = 0; index < method.Parameters.Length; index++)
        {
            var parameter = method.Parameters[index];
            var other = alternative.Parameters[index];
            if (parameter.RefKind != other.RefKind) return false;
            if (parameter.IsParams != other.IsParams) return false;
            if (parameter.IsOptional != other.IsOptional) return false;
            if (parameter.HasExplicitDefaultValue != other.HasExplicitDefaultValue) return false;
            if (HaveEquivalentTypes(parameter.Type, other.Type)) continue;
            differences++;
            if (differences > 1) return false;
        }

        return differences == 1;
    }

    private static bool HaveEquivalentTypes(ITypeSymbol type, ITypeSymbol other)
    {
        if (SymbolEqualityComparer.Default.Equals(type, other)) return true;
        // Tuple labels are source names, not overload-signature differences.
        // Unwrap at every level, including inside constructed containing types.
        if (type is INamedTypeSymbol { TupleUnderlyingType: { } underlying }) type = underlying;
        if (other is INamedTypeSymbol { TupleUnderlyingType: { } otherUnderlying }) other = otherUnderlying;
        if (type is INamedTypeSymbol named && other is INamedTypeSymbol otherNamed)
        {
            if (!SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, otherNamed.OriginalDefinition)) return false;
            if (named.ContainingType is { } containing && otherNamed.ContainingType is { } otherContaining
                && !HaveEquivalentTypes(containing, otherContaining)) return false;
            return HaveEquivalentTypeArguments(named.TypeArguments, otherNamed.TypeArguments);
        }
        if (type is IArrayTypeSymbol array && other is IArrayTypeSymbol otherArray)
        {
            return array.Rank == otherArray.Rank && array.IsSZArray == otherArray.IsSZArray
                && HaveEquivalentTypes(array.ElementType, otherArray.ElementType);
        }
        if (type is IPointerTypeSymbol pointer && other is IPointerTypeSymbol otherPointer)
        {
            return HaveEquivalentTypes(pointer.PointedAtType, otherPointer.PointedAtType);
        }
        if (type is IFunctionPointerTypeSymbol function && other is IFunctionPointerTypeSymbol otherFunction)
        {
            var signature = function.Signature;
            var otherSignature = otherFunction.Signature;
            if (signature.CallingConvention != otherSignature.CallingConvention) return false;
            if (signature.RefKind != otherSignature.RefKind) return false;
            if (!HaveEquivalentTypeArguments(signature.UnmanagedCallingConventionTypes, otherSignature.UnmanagedCallingConventionTypes)) return false;
            if (!HaveEquivalentTypes(signature.ReturnType, otherSignature.ReturnType)) return false;
            if (signature.Parameters.Length != otherSignature.Parameters.Length) return false;
            return signature.Parameters.Zip(otherSignature.Parameters, (parameter, otherParameter) =>
                parameter.RefKind == otherParameter.RefKind && HaveEquivalentTypes(parameter.Type, otherParameter.Type)).All(equal => equal);
        }
        return false;
    }

    private static bool HaveEquivalentTypeArguments<T>(ImmutableArray<T> types, ImmutableArray<T> others) where T : ITypeSymbol
    {
        if (types.Length != others.Length) return false;
        return types.Zip(others, (type, other) => HaveEquivalentTypes(type, other)).All(equal => equal);
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
