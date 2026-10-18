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
            if (SymbolEqualityComparer.Default.Equals(parameter.Type, other.Type)) continue;
            differences++;
            if (differences > 1) return false;
        }

        return differences == 1;
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
