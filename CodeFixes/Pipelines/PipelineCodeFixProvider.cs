using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PipelineCodeFixProvider)), Shared]
public sealed class PipelineCodeFixProvider : CodeFixProvider
{
    public const string ExtractKey = "Pipelines.ExtractProjection";
    public const string BlockKey = "Pipelines.BlockProjection";
    public const string SelectorsKey = "Pipelines.ExplicitSelectors";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.ComposedProjection, DiagnosticIds.StageImplementation, DiagnosticIds.ExplicitCollectionSelectors);
    public override FixAllProvider GetFixAllProvider() => new PipelineFixAllProvider();
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null) return;
        var owner = root.FindNode(context.Diagnostics[0].Location.SourceSpan, getInnermostNodeForTie: true);
        var keys = context.Diagnostics[0].Id == DiagnosticIds.ExplicitCollectionSelectors ? new[] { SelectorsKey }
            : context.Diagnostics[0].Id == DiagnosticIds.StageImplementation ? new[] { ExtractKey } : new[] { ExtractKey, BlockKey };
        foreach (var key in keys)
        {
            var changed = await PipelineFixes.ChangeAsync(context.Document, owner, model, key, context.CancellationToken).ConfigureAwait(false);
            if (changed is null) continue;
            var title = key == SelectorsKey ? "Make key and element selectors explicit" : key == ExtractKey ? "Extract projection to a function" : "Use a block projection with named stages";
            context.RegisterCodeFix(CodeAction.Create(title, _ => Task.FromResult(changed), key), context.Diagnostics);
        }
    }
}
