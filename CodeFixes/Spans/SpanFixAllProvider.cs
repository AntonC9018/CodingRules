using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

internal sealed class SpanFixAllProvider : FixAllProvider
{
    public override Task<CodeAction?> GetFixAsync(FixAllContext context) => Task.FromResult<CodeAction?>(CodeAction.Create(
        "Inspect text with a span", token => Apply(context, token), SpanTextCodeFixProvider.Key));
    private static async Task<Solution> Apply(FixAllContext context, CancellationToken executionToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, executionToken);
        var token = cancellation.Token;
        context = context.WithCancellationToken(token);
        var solution = context.Solution;
        var documents = context.Scope == FixAllScope.Document ? new[] { context.Document! }
            : context.Scope == FixAllScope.Project ? context.Project.Documents.ToArray() : solution.Projects.SelectMany(project => project.Documents).ToArray();
        foreach (var original in documents)
        {
            token.ThrowIfCancellationRequested();
            if (!(await context.GetDocumentDiagnosticsAsync(original).ConfigureAwait(false)).Any(item => item.Id == DiagnosticIds.TransientTextInspection)) continue;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var document = solution.GetDocument(original.Id)!;
                var model = await document.GetSemanticModelAsync(token).ConfigureAwait(false);
                if (model is null) break;
                var diagnostics = await model.Compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SpanTextAnalyzer()), document.Project.AnalyzerOptions)
                    .GetAnalyzerSemanticDiagnosticsAsync(model, null, token).ConfigureAwait(false);
                Document? changed = null;
                foreach (var diagnostic in diagnostics.Where(item => item.Location.SourceTree == model.SyntaxTree && item.Id == DiagnosticIds.TransientTextInspection).OrderBy(item => item.Location.SourceSpan.Start))
                {
                    changed = await SpanTextCodeFixProvider.Change(document, diagnostic, token).ConfigureAwait(false);
                    if (changed is not null) break;
                }
                if (changed is null) break;
                solution = changed.Project.Solution;
            }
        }
        return solution;
    }
}
