using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

/// <summary>
/// Fixes one diagnostic at a time, re-analyzing the document after each change,
/// so every fix sees the names and positions the previous one produced.
/// </summary>
internal sealed class SequentialFixAllProvider(
    string title,
    string key,
    string diagnosticId,
    Func<DiagnosticAnalyzer> analyzer,
    Func<Document, Diagnostic, CancellationToken, Task<Document?>> change) : FixAllProvider
{
    public override Task<CodeAction?> GetFixAsync(FixAllContext context) =>
        Task.FromResult<CodeAction?>(CodeAction.Create(title, token => Apply(context, token), key));

    private async Task<Solution> Apply(FixAllContext context, CancellationToken executionToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, executionToken);
        var token = cancellation.Token;
        context = context.WithCancellationToken(token);
        var solution = context.Solution;
        var documents = context.Scope switch
        {
            FixAllScope.Document => new[] { context.Document! },
            FixAllScope.Project => context.Project.Documents.ToArray(),
            _ => solution.Projects.SelectMany(project => project.Documents).ToArray(),
        };
        foreach (var original in documents)
        {
            token.ThrowIfCancellationRequested();
            var reported = await context.GetDocumentDiagnosticsAsync(original).ConfigureAwait(false);
            if (!reported.Any(item => item.Id == diagnosticId)) continue;
            while (await Next(solution.GetDocument(original.Id)!, token).ConfigureAwait(false) is { } changed)
            {
                solution = changed.Project.Solution;
            }
        }

        return solution;
    }

    private async Task<Document?> Next(Document document, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var model = await document.GetSemanticModelAsync(token).ConfigureAwait(false);
        if (model is null) return null;
        var diagnostics = await model.Compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer()), document.Project.AnalyzerOptions)
            .GetAnalyzerSemanticDiagnosticsAsync(model, null, token).ConfigureAwait(false);
        foreach (var diagnostic in diagnostics
                     .Where(item => item.Location.SourceTree == model.SyntaxTree && item.Id == diagnosticId)
                     .OrderBy(item => item.Location.SourceSpan.Start))
        {
            if (await change(document, diagnostic, token).ConfigureAwait(false) is { } changed) return changed;
        }

        return null;
    }
}
