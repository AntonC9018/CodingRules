using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExceptionContextCodeFixProvider)), Shared]
public sealed class ExceptionContextCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.ExceptionContext);
    // This action introduces an exception API and changes exact runtime type.
    // Require individual review instead of propagating that decision through Fix All.
    public override FixAllProvider? GetFixAllProvider() => null;
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var message = root?.FindNode(context.Span, getInnermostNodeForTie: true) as InterpolatedStringExpressionSyntax;
        if (model is null || message is null || ExceptionContextPlan.Create(message, model) is not { } plan) return;
        context.RegisterCodeFix(CodeAction.Create("Generate exception with typed context (changes exact exception type)",
            async token =>
            {
                var changed = root!.ReplaceNode(plan.Container, plan.Container.ReplaceNode(plan.Creation, plan.Replacement)
                    .AddMembers(plan.Declaration).WithAdditionalAnnotations(Formatter.Annotation));
                return await Formatter.FormatAsync(context.Document.WithSyntaxRoot(changed), Formatter.Annotation, cancellationToken: token).ConfigureAwait(false);
            }, nameof(ExceptionContextCodeFixProvider)), context.Diagnostics);
    }
}
