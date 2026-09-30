using System.Linq;
using System.Threading.Tasks;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

internal sealed class InlineConditionFixAllProvider : FixAllProvider
{
    public override Task<CodeAction?> GetFixAsync(FixAllContext context)
    {
        var action = CodeAction.Create("Fix inline conditions", token => ApplyAsync(context), context.CodeActionEquivalenceKey);
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

            for (var pass = 0; pass < 1000; pass++)
            {
                var document = solution.GetDocument(original.Id)!;
                var compilation = await document.Project.GetCompilationAsync(context.CancellationToken).ConfigureAwait(false);
                if (compilation is null)
                {
                    break;
                }

                var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new InlineConditionAnalyzer()),
                    document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync(context.CancellationToken).ConfigureAwait(false);
                var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
                var model = await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
                var extract = context.CodeActionEquivalenceKey == InlineConditionCodeFixProvider.ExtractKey;
                var plan = diagnostics.Where(diagnostic => diagnostic.Location.SourceTree == model?.SyntaxTree)
                    .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
                    .Select(diagnostic => root!.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) as ExpressionSyntax)
                    .Where(expression => expression is not null)
                    .Select(expression => ConditionRewritePlan.Create(expression!, model!, context.CancellationToken))
                    .FirstOrDefault(candidate => candidate is not null && (extract ? candidate.CanExtract : candidate.CanExpand));
                if (plan is null)
                {
                    break;
                }

                var changed = await InlineConditionFixes.ApplyAsync(document, plan, extract, context.CancellationToken).ConfigureAwait(false);
                solution = changed.Project.Solution;
            }
        }

        return solution;
    }
}
