using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

internal sealed class NamedArgumentFixAllProvider : FixAllProvider
{
    public override Task<CodeAction?> GetFixAsync(FixAllContext context) => Task.FromResult<CodeAction?>(CodeAction.Create("Add parameter names", token => Apply(context, token), NamedArgumentCodeFixProvider.Key));
    private static async Task<Solution> Apply(FixAllContext context, CancellationToken executionToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, executionToken);
        var token = cancellation.Token;
        context = context.WithCancellationToken(token);
        token.ThrowIfCancellationRequested();
        var solution = context.Solution;
        var documents = context.Scope == FixAllScope.Document ? new[] { context.Document! }
            : context.Scope == FixAllScope.Project ? context.Project.Documents.ToArray() : solution.Projects.SelectMany(project => project.Documents).ToArray();
        foreach (var original in documents)
        {
            token.ThrowIfCancellationRequested();
            var initialDiagnostics = await context.GetDocumentDiagnosticsAsync(original).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (initialDiagnostics.Length == 0) continue;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var document = solution.GetDocument(original.Id)!;
                var model = await document.GetSemanticModelAsync(token).ConfigureAwait(false);
                if (model is null) break;
                var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
                var diagnostics = await model.Compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new NamedArgumentAnalyzer()), document.Project.AnalyzerOptions)
                    .GetAnalyzerSemanticDiagnosticsAsync(model, null, token).ConfigureAwait(false);
                Document? changed = null;
                foreach (var diagnostic in diagnostics.Where(item => item.Location.SourceTree == model.SyntaxTree && context.DiagnosticIds.Contains(item.Id)).OrderBy(item => item.Location.SourceSpan.Start))
                {
                    token.ThrowIfCancellationRequested();
                    var owner = root!.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).Parent;
                    if (owner is null) continue;
                    changed = await NamedArgumentFixes.ChangeAsync(document, owner, model, token).ConfigureAwait(false);
                    if (changed is not null) break;
                }
                if (changed is null) break;
                solution = changed.Project.Solution;
            }
        }
        return solution;
    }
}
