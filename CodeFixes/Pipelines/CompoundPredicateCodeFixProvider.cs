using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CompoundPredicateCodeFixProvider)), Shared]
public sealed class CompoundPredicateCodeFixProvider : CodeFixProvider
{
    public const string BlockKey = "Pipelines.BlockCompoundPredicate";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.CompoundNestedPredicate);

    public override FixAllProvider GetFixAllProvider() => new CompoundPredicateFixAllProvider();

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null || root?.FindNode(context.Span, getInnermostNodeForTie: true)
            is not LambdaExpressionSyntax lambda)
        {
            return;
        }

        var changed = await CompoundPredicateFixes.ChangeAsync(context.Document, lambda, model,
            context.CancellationToken).ConfigureAwait(false);
        if (changed is null)
        {
            return;
        }

        context.RegisterCodeFix(CodeAction.Create("Use a block predicate with guard clauses",
            _ => Task.FromResult(changed), BlockKey), context.Diagnostics);
    }
}
