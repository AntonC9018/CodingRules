using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FluentChainLayoutCodeFixProvider)), Shared]
public sealed class FluentChainLayoutCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.FluentChainLayout);
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var parent = root?.FindToken(context.Span.Start).Parent;
        var call = parent?.AncestorsAndSelf().Concat(parent.DescendantNodes()).OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(candidate => FluentChainLayoutAnalyzer.TryGetBoundary(candidate, out _, out var separator)
                && separator.SpanStart == context.Span.Start);
        if (call is null || model is null || call.Ancestors().OfType<InterpolatedStringExpressionSyntax>().Any()
            || CallerInformation.AffectedDefaults(call, call, model)) return;
        var text = await context.Document.GetTextAsync(context.CancellationToken).ConfigureAwait(false);
        if (!FluentChainLayoutAnalyzer.TryGetChange(call, text, out var change)) return;
        context.RegisterCodeFix(CodeAction.Create("Put the chained call on a new line",
            _ => Task.FromResult(context.Document.WithText(text.WithChanges(change))), DiagnosticIds.FluentChainLayout), context.Diagnostics);
    }
}
