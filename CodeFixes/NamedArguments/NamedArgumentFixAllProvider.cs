using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

internal sealed class NamedArgumentFixAllProvider : FixAllProvider
{
    public override Task<CodeAction?> GetFixAsync(FixAllContext context) => Task.FromResult<CodeAction?>(CodeAction.Create("Add parameter names", _ => Apply(context), NamedArgumentCodeFixProvider.Key));
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
                var diagnostics = await model.Compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new NamedArgumentAnalyzer()), document.Project.AnalyzerOptions)
                    .GetAnalyzerSemanticDiagnosticsAsync(model, null, context.CancellationToken).ConfigureAwait(false);
                Document? changed = null;
                foreach (var diagnostic in diagnostics.Where(item => item.Location.SourceTree == model.SyntaxTree && context.DiagnosticIds.Contains(item.Id)).OrderBy(item => item.Location.SourceSpan.Start))
                {
                    var owner = root!.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).Parent;
                    if (owner is null) continue;
                    changed = await NamedArgumentFixes.ChangeAsync(document, owner, model, context.CancellationToken).ConfigureAwait(false);
                    if (changed is not null) break;
                }
                if (changed is null) break;
                solution = changed.Project.Solution;
            }
        }
        return solution;
    }
}
