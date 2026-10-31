using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RepeatedFormattingAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.RepeatedFormattingCandidate,
        "Review repeated timestamp formatting for shared meaning",
        "Repeated timestamp formatting is a duplication candidate; review shared meaning before extracting a helper",
        "Readability", DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Matching formatting operations may express distinct domain concepts. Review their meaning and reasons to change; preserve intentionally separate computations with a justified scoped suppression. No automatic extraction is offered.",
        helpLinkUri: "https://github.com/AntonC9018/CodingRules/blob/main/docs/agent-coding-rules.md#review-duplication-by-meaning");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var firsts = new List<Candidate>();
        // Nested callables never contribute matches to their containing method.
        var nodes = context.Node.DescendantNodes(node => node == context.Node
            || node is not LocalFunctionStatementSyntax && node is not AnonymousFunctionExpressionSyntax);
        foreach (var syntax in nodes.OfType<InvocationExpressionSyntax>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var candidate = Create(syntax, context);
            if (candidate is null) continue;
            var first = firsts.FirstOrDefault(previous => Matches(previous, candidate));
            if (first is null)
            {
                firsts.Add(candidate);
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(Rule, syntax.GetLocation(),
                additionalLocations: new[] { first.Syntax.GetLocation() }));
        }
    }

    private static Candidate? Create(InvocationExpressionSyntax syntax, SyntaxNodeAnalysisContext context)
    {
        if (syntax.ContainsDiagnostics) return null;
        if (context.SemanticModel.GetOperation(syntax, context.CancellationToken) is not IInvocationOperation call) return null;
        var method = call.TargetMethod;
        if (!IsTimestampFormat(method, context.Compilation)) return null;
        if (call.Arguments.Length != 2) return null;
        var format = call.Arguments.SingleOrDefault(argument => argument.Parameter?.Ordinal == 0);
        var provider = call.Arguments.SingleOrDefault(argument => argument.Parameter?.Ordinal == 1);
        if (format is null || provider is null) return null;
        if (format.IsImplicit || provider.IsImplicit) return null;
        if (!format.Value.ConstantValue.HasValue) return null;
        if (format.Value.ConstantValue.Value is not null and not string) return null;
        if (CompilerDiagnostics.HasError(context.SemanticModel, syntax, context.CancellationToken)) return null;
        if (!SameProvider(provider.Value, provider.Value)) return null;

        var gates = ImmutableArray.CreateBuilder<ITypeSymbol>();
        SyntaxNode current = syntax;
        while (true)
        {
            if (current.Parent is ParenthesizedExpressionSyntax parentheses)
            {
                current = parentheses;
                continue;
            }
            if (current.Parent is not ConditionalAccessExpressionSyntax conditional) break;
            if (conditional.WhenNotNull != current) break;
            var type = context.SemanticModel.GetTypeInfo(conditional.Expression, context.CancellationToken).Type;
            if (type is null || type.TypeKind == TypeKind.Error) return null;
            gates.Add(type);
            current = conditional;
        }

        return new Candidate(syntax, method, (string?)format.Value.ConstantValue.Value, provider.Value, gates.ToImmutable());
    }

    private static bool IsTimestampFormat(IMethodSymbol method, Compilation compilation)
    {
        if (method.Name != "ToString" || method.IsStatic || method.Arity != 0) return false;
        if (method.ReturnType.SpecialType != SpecialType.System_String) return false;
        if (method.Parameters.Length != 2) return false;
        if (method.Parameters[0].Type.SpecialType != SpecialType.System_String) return false;
        var date = compilation.GetSpecialType(SpecialType.System_DateTime);
        var offset = compilation.GetTypeByMetadataName("System.DateTimeOffset");
        if (date.Locations.Any(location => location.IsInSource)) return false;
        if (!Same(method.ContainingType, date) && !Same(method.ContainingType, offset)) return false;
        if (!Same(method.ContainingAssembly, date.ContainingAssembly)) return false;
        return Same(method.Parameters[1].Type, compilation.GetTypeByMetadataName("System.IFormatProvider"));
    }

    private static bool Matches(Candidate first, Candidate other)
    {
        if (!Same(first.Method, other.Method)) return false;
        if (first.Format != other.Format) return false;
        if (first.Gates.Length != other.Gates.Length) return false;
        if (!first.Gates.Zip(other.Gates, Same).All(equal => equal)) return false;
        return SameProvider(first.Provider, other.Provider);
    }

    private static bool SameProvider(IOperation first, IOperation other)
    {
        // Only transparent conversions and named provider sources participate.
        // Calls, indexers, arbitrary properties and user conversions stay unknown.
        if (first is IConversionOperation left && left.OperatorMethod is null && left.IsImplicit && left.Conversion.Exists)
            return SameProvider(left.Operand, other);
        if (other is IConversionOperation right && right.OperatorMethod is null && right.IsImplicit && right.Conversion.Exists)
            return SameProvider(first, right.Operand);
        if (first.ConstantValue.HasValue && other.ConstantValue.HasValue)
            return first.ConstantValue.Value is null && other.ConstantValue.Value is null;
        if (first is ILocalReferenceOperation local && other is ILocalReferenceOperation otherLocal)
            return Same(local.Local, otherLocal.Local);
        if (first is IParameterReferenceOperation parameter && other is IParameterReferenceOperation otherParameter)
            return Same(parameter.Parameter, otherParameter.Parameter);
        if (first is IInstanceReferenceOperation instance && other is IInstanceReferenceOperation otherInstance)
            return instance.ReferenceKind == otherInstance.ReferenceKind && Same(instance.Type, otherInstance.Type);
        if (first is IFieldReferenceOperation field && other is IFieldReferenceOperation otherField)
        {
            if (!Same(field.Field, otherField.Field)) return false;
            if (field.Instance is null || otherField.Instance is null) return field.Instance is null && otherField.Instance is null;
            return SameProvider(field.Instance, otherField.Instance);
        }
        if (first is IPropertyReferenceOperation property && other is IPropertyReferenceOperation otherProperty)
        {
            if (!Same(property.Property, otherProperty.Property)) return false;
            if (!property.Property.IsStatic || property.Arguments.Length != 0) return false;
            var type = property.Property.ContainingType;
            if (type.ToDisplayString() != "System.Globalization.CultureInfo") return false;
            if (type.Locations.Any(location => location.IsInSource)) return false;
            return property.Property.Name is "InvariantCulture" or "CurrentCulture" or "CurrentUICulture";
        }
        return false;
    }

    private static bool Same(ISymbol? first, ISymbol? other) => SymbolEqualityComparer.Default.Equals(first, other);

    private sealed class Candidate(InvocationExpressionSyntax syntax, IMethodSymbol method, string? format,
        IOperation provider, ImmutableArray<ITypeSymbol> gates)
    {
        public InvocationExpressionSyntax Syntax { get; } = syntax;
        public IMethodSymbol Method { get; } = method;
        public string? Format { get; } = format;
        public IOperation Provider { get; } = provider;
        public ImmutableArray<ITypeSymbol> Gates { get; } = gates;
    }
}
