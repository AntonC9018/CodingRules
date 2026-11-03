using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EarlyReturnLayoutCodeFixProvider)), Shared]
public sealed class EarlyReturnLayoutCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.ElseAfterExit);
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var decision = root?.FindToken(context.Span.Start).Parent?.FirstAncestorOrSelf<IfStatementSyntax>();
        if (decision?.Else is null || decision.Parent is not BlockSyntax block
            || decision.ContainsDirectives || !EarlyReturnLayoutAnalyzer.Terminates(decision.Statement)) return;
        if (decision.Else.DescendantTrivia().Any(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))) return;
        var alternative = decision.Else.Statement;
        // Retain a scope if moving local declarations could affect binding outside this branch.
        var statements = alternative is BlockSyntax branch
            && !branch.DescendantNodes().Any(node => node is VariableDeclaratorSyntax or SingleVariableDesignationSyntax or LocalFunctionStatementSyntax)
            ? branch.Statements.ToArray() : new[] { alternative };
        if (decision.Else.GetLeadingTrivia().Any(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))) return;
        var index = block.Statements.IndexOf(decision);
        var changedBlock = block.WithStatements(block.Statements.Replace(decision, decision.WithElse(null))
            .InsertRange(index + 1, statements)).WithAdditionalAnnotations(Formatter.Annotation);
        var changed = context.Document.WithSyntaxRoot(root!.ReplaceNode(block, changedBlock));
        changed = await Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: context.CancellationToken).ConfigureAwait(false);
        // A layout rewrite must not alter implicit CallerArgumentExpression text.
        var originalModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (originalModel is null || CallerInformation.AffectedDefaults(decision, decision, originalModel)) return;
        var text = await changed.GetTextAsync(context.CancellationToken).ConfigureAwait(false);
        var tree = CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)originalModel.SyntaxTree.Options);
        var saved = originalModel.Compilation.ReplaceSyntaxTree(originalModel.SyntaxTree, tree);
        var before = new HashSet<string>(originalModel.Compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id + d.GetMessage()));
        if (saved.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error && !before.Contains(d.Id + d.GetMessage()))) return;
        var result = changed;
        context.RegisterCodeFix(CodeAction.Create("Remove the else after the terminating branch",
            _ => Task.FromResult(result), DiagnosticIds.ElseAfterExit), context.Diagnostics);
    }
}
