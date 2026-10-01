using System.Linq;
using System.Threading.Tasks;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

internal sealed class StatementOperationFixAllProvider : FixAllProvider
{
    public override Task<CodeAction?> GetFixAsync(FixAllContext context)
    {
        var action = CodeAction.Create("Fix statement operations", token => ApplyAsync(context), context.CodeActionEquivalenceKey);
        return Task.FromResult<CodeAction?>(action);
    }

    private static async Task<Solution> ApplyAsync(FixAllContext context)
    {
        var solution = context.Solution;
        var documents = context.Scope switch
        {
            FixAllScope.Document => new[] { context.Document! },
            FixAllScope.Project => context.Project.Documents.ToArray(),
            _ => context.Solution.Projects.SelectMany(project => project.Documents).ToArray(),
        };
        foreach (var original in documents)
        {
            var requested = await context.GetDocumentDiagnosticsAsync(original).ConfigureAwait(false);
            if (requested.Length == 0)
            {
                continue;
            }

            while (true)
            {
                var document = solution.GetDocument(original.Id)!;
                var compilation = await document.Project.GetCompilationAsync(context.CancellationToken).ConfigureAwait(false);
                if (compilation is null)
                {
                    break;
                }

                var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new StatementOperationAnalyzer()),
                    document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync(context.CancellationToken).ConfigureAwait(false);
                var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
                var model = await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
                var extract = context.CodeActionEquivalenceKey == StatementOperationCodeFixProvider.ExtractKey;
                var candidates = diagnostics.Where(diagnostic => diagnostic.Location.SourceTree == model?.SyntaxTree
                        && context.DiagnosticIds.Contains(diagnostic.Id))
                    .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
                    .Select(diagnostic => root!.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) as ExpressionSyntax)
                    .Where(expression => expression is not null)
                    .Select(expression => OperationPlan.Create(expression!, model!, context.CancellationToken))
                    .Where(candidate => candidate is not null && (extract ? candidate.CanExtract : candidate.CanExpand));
                OperationPlan? plan = null;
                foreach (var candidate in candidates)
                {
                    if (await StatementOperationFixes.IsValidAsync(document, candidate!, extract, context.CancellationToken).ConfigureAwait(false))
                    {
                        plan = candidate;
                        break;
                    }
                }
                if (plan is null)
                {
                    break;
                }

                var changed = await StatementOperationFixes.ApplyAsync(document, plan, extract, context.CancellationToken).ConfigureAwait(false);
                var before = await document.GetTextAsync(context.CancellationToken).ConfigureAwait(false);
                var after = await changed.GetTextAsync(context.CancellationToken).ConfigureAwait(false);
                if (before.ContentEquals(after))
                {
                    break;
                }

                solution = changed.Project.Solution;
            }
        }

        return solution;
    }
}
