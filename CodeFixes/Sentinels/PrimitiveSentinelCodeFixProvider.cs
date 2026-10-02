using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PrimitiveSentinelCodeFixProvider)), Shared]
public sealed class PrimitiveSentinelCodeFixProvider : CodeFixProvider
{
    public const string Key = "Sentinels.CheckAndReturn";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.UncheckedHelperSentinel);
    public override FixAllProvider GetFixAllProvider() => new SentinelFixAllProvider();
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        if (context.Diagnostics[0].Id != DiagnosticIds.UncheckedHelperSentinel) return;
        var changed = await Change(context.Document, context.Diagnostics[0], context.CancellationToken).ConfigureAwait(false);
        if (changed is not null) context.RegisterCodeFix(CodeAction.Create("Check and return the sentinel explicitly",
            token => Apply(context.Document, context.Diagnostics[0], token), Key), context.Diagnostics);
    }
    internal static async Task<Document?> Change(Document document, Diagnostic diagnostic, CancellationToken token)
    {
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(token).ConfigureAwait(false);
        if (root is null || model is null || diagnostic.Location.SourceSpan.End > root.FullSpan.End) return null;
        var expression = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) as ExpressionSyntax;
        return expression is null ? null : await SentinelFixes.ChangeAsync(document, expression, model, token).ConfigureAwait(false);
    }
    private static async Task<Document> Apply(Document original, Diagnostic diagnostic, CancellationToken token)
    {
        var current = original.Project.Solution.Workspace.CurrentSolution.GetDocument(original.Id) ?? original;
        if (!(await original.GetTextAsync(token).ConfigureAwait(false)).ContentEquals(await current.GetTextAsync(token).ConfigureAwait(false))) return current;
        return await Change(current, diagnostic, token).ConfigureAwait(false) ?? current;
    }
}
