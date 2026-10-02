using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NamedArgumentCodeFixProvider)), Shared]
public sealed class NamedArgumentCodeFixProvider : CodeFixProvider
{
    public const string Key = "NamedArguments.AddParameterNames";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.NamedArgumentRoles);
    public override FixAllProvider GetFixAllProvider() => new NamedArgumentFixAllProvider();
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null) return;
        var list = root.FindNode(context.Diagnostics[0].Location.SourceSpan, getInnermostNodeForTie: true);
        var owner = list.Parent;
        if (owner is null) return;
        var changed = await NamedArgumentFixes.ChangeAsync(context.Document, owner, model, context.CancellationToken).ConfigureAwait(false);
        if (changed is not null) context.RegisterCodeFix(CodeAction.Create("Add parameter names",
            token => ApplyAsync(context.Document, context.Diagnostics[0], token), Key), context.Diagnostics);
    }

    private static async Task<Document> ApplyAsync(Document original, Diagnostic diagnostic, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var document = original.Project.Solution.Workspace.CurrentSolution.GetDocument(original.Id) ?? original;
        // A cached diagnostic span belongs to the received source snapshot. Read
        // current policy only while that source is unchanged; otherwise an edit
        // could put an unrelated call at the old span.
        var originalText = await original.GetTextAsync(token).ConfigureAwait(false);
        if (!originalText.ContentEquals(await document.GetTextAsync(token).ConfigureAwait(false))) return document;
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(token).ConfigureAwait(false);
        if (root is null || model is null || diagnostic.Location.SourceSpan.End > root.FullSpan.End) return document;
        var owner = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).Parent;
        if (owner is null) return document;
        return await NamedArgumentFixes.ChangeAsync(document, owner, model, token).ConfigureAwait(false) ?? document;
    }
}
