using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

internal sealed class PipelineFixAllProvider : FixAllProvider
{
    public override Task<CodeAction?> GetFixAsync(FixAllContext context) => Task.FromResult<CodeAction?>(CodeAction.Create("Fix pipeline stages and selectors", _ => Apply(context), context.CodeActionEquivalenceKey));
    private static async Task<Solution> Apply(FixAllContext context)
    {
        var solution = context.Solution;
        var documents = context.Scope == FixAllScope.Document ? new[] { context.Document! }
            : context.Scope == FixAllScope.Project ? context.Project.Documents.ToArray() : solution.Projects.SelectMany(project => project.Documents).ToArray();
        foreach (var original in documents)
        {
            if ((await context.GetDocumentDiagnosticsAsync(original).ConfigureAwait(false)).Length == 0) continue;
            while (true)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var document = solution.GetDocument(original.Id)!;
                var model = await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
                if (model is null) break;
                var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
                var diagnostics = await model.Compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PipelineAnalyzer()), document.Project.AnalyzerOptions)
                    .GetAnalyzerDiagnosticsAsync(context.CancellationToken).ConfigureAwait(false);
                Document? changed = null;
                foreach (var diagnostic in diagnostics.Where(item => item.Location.SourceTree == model.SyntaxTree && context.DiagnosticIds.Contains(item.Id)).OrderBy(item => item.Location.SourceSpan.Start))
                {
                    changed = await PipelineFixes.ChangeAsync(document, root!.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true), model,
                        context.CodeActionEquivalenceKey!, context.CancellationToken).ConfigureAwait(false);
                    if (changed is not null) break;
                }
                if (changed is null) break;
                solution = changed.Project.Solution;
            }
        }
        return solution;
    }
}
