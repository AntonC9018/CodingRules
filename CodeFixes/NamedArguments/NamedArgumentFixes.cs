using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodingRules;

internal static class NamedArgumentFixes
{
    public static async Task<Document?> ChangeAsync(Document document, SyntaxNode owner, SemanticModel model, CancellationToken token)
    {
        var plan = NamedArgumentPlan.Create(owner, model, document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider, token);
        if (plan is null) return null;
        var changes = plan.Indices.Select(index =>
        {
            var parameter = plan.Site.BoundArguments[index].Parameter!;
            var name = SyntaxFacts.GetKeywordKind(parameter.Name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(parameter.Name) != SyntaxKind.None
                ? "@" + parameter.Name : parameter.Name;
            return new TextChange(new TextSpan(plan.Site.Arguments[index].SpanStart, 0), name + ": ");
        }).ToArray();
        var original = await document.GetTextAsync(token).ConfigureAwait(false);
        var text = original.WithChanges(changes);
        var tree = CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)owner.SyntaxTree.Options, owner.SyntaxTree.FilePath, token);
        var root = tree.GetRoot(token);
        var after = model.Compilation.ReplaceSyntaxTree(owner.SyntaxTree, tree).GetSemanticModel(tree);
        // Exact occurrence mapping includes diagnostics outside the selected root.
        // No histogram can distinguish a moved warning from a new same-message one.
        var beforeDiagnostics = model.GetDiagnostics(cancellationToken: token).Where(CompilerDiagnostic)
            .Select(diagnostic => DiagnosticKey(diagnostic, changes)).OrderBy(value => value).ToArray();
        var afterDiagnostics = after.GetDiagnostics(cancellationToken: token).Where(CompilerDiagnostic)
            .Select(diagnostic => DiagnosticKey(diagnostic, null)).OrderBy(value => value).ToArray();
        if (!beforeDiagnostics.SequenceEqual(afterDiagnostics)) return null;
        var oldRoot = owner.SyntaxTree.GetRoot(token);
        foreach (var occurrence in oldRoot.DescendantNodes().Where(node => NamedArgumentSite.IsHost(node)
                     || node is ElementAccessExpressionSyntax or ElementBindingExpressionSyntax))
        {
            token.ThrowIfCancellationRequested();
            var span = Map(occurrence.Span, changes);
            var corresponding = root.FindNode(span, getInnermostNodeForTie: true).AncestorsAndSelf()
                .FirstOrDefault(node => node.Span == span && node.RawKind == occurrence.RawKind);
            if (corresponding is null
                || NamedArgumentIdentity.Operation(model.GetOperation(occurrence, token)) != NamedArgumentIdentity.Operation(after.GetOperation(corresponding, token))) return null;
            if (occurrence is ExpressionSyntax beforeExpression && corresponding is ExpressionSyntax afterExpression)
            {
                var oldType = model.GetTypeInfo(beforeExpression, token);
                var newType = after.GetTypeInfo(afterExpression, token);
                if (NamedArgumentIdentity.Type(oldType.Type) != NamedArgumentIdentity.Type(newType.Type)
                    || NamedArgumentIdentity.Type(oldType.ConvertedType) != NamedArgumentIdentity.Type(newType.ConvertedType)) return null;
            }
        }
        var selected = root.FindNode(Map(owner.Span, changes), getInnermostNodeForTie: true);
        if (NamedArgumentPlan.Create(selected, after, document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider, token) is not null) return null;
        return document.WithText(text);
    }

    private static bool CompilerDiagnostic(Diagnostic diagnostic) => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning;
    private static string DiagnosticKey(Diagnostic diagnostic, TextChange[]? changes)
    {
        var span = diagnostic.Location.SourceSpan;
        if (changes is not null && diagnostic.Location.IsInSource) span = Map(span, changes);
        return diagnostic.Id + ":" + diagnostic.Severity + ":" + diagnostic.GetMessage() + ":" + diagnostic.Location.Kind
            + ":" + diagnostic.Location.SourceTree?.FilePath + ":" + span.Start + ":" + span.Length;
    }
    private static TextSpan Map(TextSpan span, TextChange[] changes)
    {
        var start = span.Start + changes.Where(change => change.Span.Start <= span.Start).Sum(change => change.NewText!.Length);
        var end = span.End + changes.Where(change => change.Span.Start < span.End).Sum(change => change.NewText!.Length);
        return TextSpan.FromBounds(start, end);
    }
}
