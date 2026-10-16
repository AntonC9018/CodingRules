using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InitializerCallLayoutCodeFixProvider)), Shared]
public sealed class InitializerCallLayoutCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
        DiagnosticIds.InitializerMemberLayout, DiagnosticIds.SoleConstructorArgumentLayout);
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindToken(context.Span.Start).Parent;
        if (node is not (InitializerExpressionSyntax or ArgumentListSyntax)) return;
        var text = await context.Document.GetTextAsync(context.CancellationToken).ConfigureAwait(false);
        if (InitializerCallLayoutAnalyzer.GetChanges(node, text).IsEmpty) return;
        var diagnostic = context.Diagnostics[0];
        context.RegisterCodeFix(CodeAction.Create("Apply initializer call layout",
            token => ApplyAsync(context.Document, node, token), nameof(InitializerCallLayoutCodeFixProvider)), diagnostic);
    }

    private static async Task<Document> ApplyAsync(Document document, SyntaxNode node, CancellationToken token)
    {
        var text = await document.GetTextAsync(token).ConfigureAwait(false);
        var changes = InitializerCallLayoutAnalyzer.GetChanges(node, text);
        if (changes.IsEmpty) return document;
        var changed = document.WithText(text.WithChanges(changes));
        if (node is not InitializerExpressionSyntax) return changed;
        var length = node.Span.Length + changes.Sum(change => (change.NewText?.Length ?? 0) - change.Span.Length);
        return await Formatter.FormatAsync(changed, new TextSpan(node.SpanStart, length), cancellationToken: token).ConfigureAwait(false);
    }
}
