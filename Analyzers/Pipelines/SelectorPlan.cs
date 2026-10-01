using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal sealed class SelectorPlan
{
    public InvocationExpressionSyntax Call { get; }
    public SemanticModel Model { get; }
    public IInvocationOperation Operation { get; }
    public IMethodSymbol? IdentityPair { get; }
    public SelectorPlan(InvocationExpressionSyntax call, SemanticModel model, IInvocationOperation operation, IMethodSymbol? pair)
    {
        Call = call;
        Model = model;
        Operation = operation;
        IdentityPair = pair;
    }
    public static SelectorPlan? Create(InvocationExpressionSyntax call, SemanticModel model, PipelineCatalog catalog)
    {
        if (model.GetOperation(call) is not IInvocationOperation operation || !catalog.IsMaterializer(operation.TargetMethod)
            || ConditionHosts.IsInsideExpressionTree(call, model) || call.ContainsDirectives || call.ContainsDiagnostics
            || model.GetDiagnostics(call.Span).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return null;
        // No KVP/builder insertion: immutable overloads can preserve returned identity
        // and avoid enumeration even when their source's static type is IEnumerable.
        var pair = catalog.IdentityPair(operation.TargetMethod);
        var missing = call.ArgumentList.Arguments.Any(argument => argument.NameColon is null
            && model.GetOperation(argument) is IArgumentOperation { Parameter.Name: "keySelector" or "elementSelector" });
        return pair is null && !missing ? null : new SelectorPlan(call, model, operation, pair);
    }
}
