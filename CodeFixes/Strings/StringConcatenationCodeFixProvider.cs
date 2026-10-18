using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StringConcatenationCodeFixProvider)), Shared]
public sealed class StringConcatenationCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.StringConcatenation);
    public override FixAllProvider GetFixAllProvider() => FixAllProvider.Create(async (context, document, diagnostics) =>
    {
        var token = context.CancellationToken;
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        if (root is null) return document;
        var nodes = diagnostics.Select(diagnostic => root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true))
            .OfType<BinaryExpressionSyntax>().OrderBy(node => node.Span.Length).ToArray();
        document = document.WithSyntaxRoot(root.TrackNodes(nodes));
        // Nested argument concatenations can overlap an outer concatenation.
        // Track syntax rather than merging independent edits over stale spans.
        foreach (var original in nodes)
        {
            token.ThrowIfCancellationRequested();
            root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
            var model = await document.GetSemanticModelAsync(token).ConfigureAwait(false);
            var binary = root!.GetCurrentNode(original);
            if (binary is not null && model is not null && StringConcatenationRewrite.Create(binary, model, token) is { } replacement)
                document = document.WithSyntaxRoot(root!.ReplaceNode(binary, replacement));
        }
        return document;
    });

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics[0];
        var binary = root?.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) as BinaryExpressionSyntax;
        if (binary is null || model is null || StringConcatenationRewrite.Create(binary, model, context.CancellationToken) is null) return;
        context.RegisterCodeFix(CodeAction.Create("Convert to string interpolation",
            token => ApplyAsync(context.Document, diagnostic, token), DiagnosticIds.StringConcatenation), diagnostic);
    }

    private static async Task<Document> ApplyAsync(Document document, Diagnostic diagnostic, CancellationToken token)
    {
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(token).ConfigureAwait(false);
        var binary = root?.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) as BinaryExpressionSyntax;
        if (binary is null || model is null || StringConcatenationRewrite.Create(binary, model, token) is not { } replacement) return document;
        return document.WithSyntaxRoot(root!.ReplaceNode(binary, replacement));
    }
}
