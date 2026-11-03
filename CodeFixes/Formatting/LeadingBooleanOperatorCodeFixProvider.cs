using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LeadingBooleanOperatorCodeFixProvider)), Shared]
public sealed class LeadingBooleanOperatorCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.LeadingBooleanOperator);
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        context.RegisterCodeFix(CodeAction.Create(
            "Move Boolean operator to the continuation line",
            token => ApplyAsync(context.Document, diagnostic, token),
            DiagnosticIds.LeadingBooleanOperator), diagnostic);
        return Task.CompletedTask;
    }

    private static async Task<Document> ApplyAsync(Document document, Diagnostic diagnostic, CancellationToken token)
    {
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        var binary = root?.FindToken(diagnostic.Location.SourceSpan.Start).Parent as BinaryExpressionSyntax;
        if (binary is null) return document;
        var text = await document.GetTextAsync(token).ConfigureAwait(false);
        if (!LeadingBooleanOperatorAnalyzer.TryGetChange(binary, text, out var change)) return document;
        return document.WithText(text.WithChanges(change));
    }
}
