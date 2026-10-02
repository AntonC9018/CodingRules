using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal static class SentinelPlan
{
    public static bool Feasible(SentinelReturn site, SentinelHost host, SemanticModel model, CancellationToken token)
    {
        var expression = site.Expression;
        if (site.Value is not { Helper: true } value || (value.Outcomes & SearchOutcomes.Sentinel) == 0
            || expression.ContainsDiagnostics || expression.DescendantTrivia().Any(trivia => trivia.IsDirective
                || trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            || expression.AncestorsAndSelf().Any(node => node is RefExpressionSyntax or UnsafeStatementSyntax)
            || host.Body.DescendantNodes(node => !SentinelHost.Nested(node)).Any(node => node is GotoStatementSyntax or LabeledStatementSyntax)
            || host.Body.DescendantTrivia().Any(trivia => trivia.IsDirective)
            || model.GetDiagnostics(host.Body.Span, token).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return false;
        foreach (var anonymous in expression.Ancestors().OfType<AnonymousFunctionExpressionSyntax>())
            if (model.GetTypeInfo(anonymous, token).ConvertedType is INamedTypeSymbol type
                && type.OriginalDefinition.ToDisplayString() == "System.Linq.Expressions.Expression<TDelegate>") return false;
        var operation = SentinelSearch.Operation(expression, model, token);
        if (operation is not (ILocalReferenceOperation or IInvocationOperation)
            || model.GetTypeInfo(expression, token).ConvertedType?.SpecialType != SpecialType.System_Int32) return false;
        var insertion = expression.Parent is ReturnStatementSyntax returned ? (SyntaxNode)returned : host.Declaration;
        return !CallerInformation.AffectedDefaults(expression, insertion, model)
            && !NamedArgumentObservers.Affected(expression, model, token);
    }
}
