using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NestedReturnDecisionCodeFixProvider))]
[Shared]
public sealed class NestedReturnDecisionCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            DiagnosticIds.NestedTernaryReturn,
            DiagnosticIds.NestedCoalesceReturn,
            DiagnosticIds.NestedNullableCallReturn,
            DiagnosticIds.NestedBooleanReturn);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var nestedReturn = node.FirstAncestorOrSelf<ReturnStatementSyntax>();
        if (nestedReturn is null)
        {
            return;
        }

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return;
        }

        if (!ReturnDecisionAnalysis.TryGetNestedKind(
                nestedReturn,
                semanticModel,
                context.CancellationToken,
                out var kind))
        {
            return;
        }

        var replacement = ReturnDecisionRewrites.CreateReplacement(nestedReturn, kind, semanticModel);
        if (replacement.Count == 0)
        {
            return;
        }

        var action = CodeAction.Create(
            "Make nested return outcomes explicit",
            cancellationToken => ReturnDecisionRewrites.ApplyFormattedAsync(
                context.Document,
                nestedReturn,
                replacement,
                cancellationToken),
            nameof(NestedReturnDecisionCodeFixProvider));
        context.RegisterCodeFix(action, diagnostic);
    }
}
