using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BracesAndCastSpacingCodeFixProvider)), Shared]
public sealed class BracesAndCastSpacingCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.RequiredBraces, DiagnosticIds.CastSpacing);
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null) return;
        var diagnostic = context.Diagnostics[0];
        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        if (diagnostic.Id == DiagnosticIds.CastSpacing)
        {
            var cast = token.Parent?.FirstAncestorOrSelf<CastExpressionSyntax>();
            if (cast is null || CallerInformation.AffectedDefaults(cast, cast, model)) return;
            context.RegisterCodeFix(CodeAction.Create("Insert space after cast", async cancellation =>
            {
                var source = await context.Document.GetTextAsync(cancellation).ConfigureAwait(false);
                return context.Document.WithText(source.WithChanges(new Microsoft.CodeAnalysis.Text.TextChange(
                    new Microsoft.CodeAnalysis.Text.TextSpan(cast.CloseParenToken.Span.End, 0), " ")));
            }, DiagnosticIds.CastSpacing), diagnostic);
            return;
        }
        var statement = token.Parent?.AncestorsAndSelf().OfType<StatementSyntax>()
            .FirstOrDefault(candidate => BracesAndCastSpacingAnalyzer.Embedded(candidate.Parent!) == candidate);
        if (statement is null || statement.ContainsDirectives || CallerInformation.AffectedDefaults(statement, statement, model)) return;
        var block = SyntaxFactory.Block(statement.WithoutLeadingTrivia()).WithLeadingTrivia(statement.GetLeadingTrivia())
            .WithAdditionalAnnotations(Formatter.Annotation);
        var changed = context.Document.WithSyntaxRoot(root.ReplaceNode(statement, block));
        context.RegisterCodeFix(CodeAction.Create("Add braces",
            cancellation => Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: cancellation),
            DiagnosticIds.RequiredBraces), diagnostic);
    }
}
