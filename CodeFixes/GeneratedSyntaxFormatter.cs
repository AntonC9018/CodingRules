using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace CodingRules;

/// <summary>
/// Formats syntax a fix generated (nodes carrying <see cref="Formatter.Annotation"/>)
/// with the line endings the edited file already uses. Generated code stays compact:
/// no blank line separates it from its neighbours, and a declared type stays on the
/// line of its name.
/// </summary>
internal static class GeneratedSyntaxFormatter
{
    public static async Task<Document> FormatAsync(Document changed, Document original, CancellationToken token)
    {
        var options = await changed.GetOptionsAsync(token).ConfigureAwait(false);
        var newline = await NewLineAsync(original, token).ConfigureAwait(false);
        var formattingOptions = options.WithChangedOption(FormattingOptions.NewLine, changed.Project.Language, newline);
        var root = await changed.GetSyntaxRootAsync(token).ConfigureAwait(false);
        if (root is not null)
        {
            changed = changed.WithSyntaxRoot(Compact(root, newline));
        }

        return await Formatter.FormatAsync(changed, Formatter.Annotation, formattingOptions, token).ConfigureAwait(false);
    }

    /// <summary>
    /// The formatter separates a block from the next statement with a blank line
    /// when the trivia between them is elastic, as it is on synthesized tokens.
    /// A concrete line break after each generated closing brace keeps one line.
    /// </summary>
    internal static SyntaxNode Compact(SyntaxNode root, string newline)
    {
        var generated = root.GetAnnotatedNodes(Formatter.Annotation).SelectMany(node => node.DescendantTokens()).ToArray();
        var braces = generated.Where(token => token.IsKind(SyntaxKind.CloseBraceToken) && StartsStatement(token.GetNextToken()));
        var followers = braces.Select(token => token.GetNextToken());
        var typeEnds = generated.Where(IsDeclaredTypeEnd);
        var changes = new Dictionary<SyntaxToken, Change>();
        foreach (var brace in braces) changes[brace] = Change.BreakAfter;
        foreach (var follower in followers) changes[follower] = changes.TryGetValue(follower, out var existing) ? existing | Change.Inelastic : Change.Inelastic;
        foreach (var typeEnd in typeEnds) changes[typeEnd] = changes.TryGetValue(typeEnd, out var existing) ? existing | Change.SpaceAfter : Change.SpaceAfter;
        return root.ReplaceTokens(changes.Keys, (original, rewritten) => Apply(rewritten, changes[original], newline));
    }

    [System.Flags]
    private enum Change
    {
        BreakAfter = 1,
        Inelastic = 2,
        SpaceAfter = 4,
    }

    private static SyntaxToken Apply(SyntaxToken token, Change change, string newline)
    {
        if ((change & Change.Inelastic) != 0)
        {
            token = token.WithLeadingTrivia(Concrete(token.LeadingTrivia));
        }

        if ((change & Change.BreakAfter) != 0)
        {
            var trailing = Concrete(token.TrailingTrivia);
            token = token.WithTrailingTrivia(trailing.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                ? trailing : trailing.Add(SyntaxFactory.EndOfLine(newline)));
        }

        if ((change & Change.SpaceAfter) != 0 && token.TrailingTrivia.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
        {
            token = token.WithTrailingTrivia(SyntaxFactory.Space);
        }

        return token;
    }

    private static SyntaxTriviaList Concrete(SyntaxTriviaList trivia) =>
        SyntaxFactory.TriviaList(trivia.Where(item => !item.HasAnnotation(SyntaxAnnotation.ElasticAnnotation)));

    // The first token of a statement, other than a continuation such as else or catch.
    private static bool StartsStatement(SyntaxToken token) =>
        token.Parent?.FirstAncestorOrSelf<StatementSyntax>() is { } statement && statement.GetFirstToken() == token
        && statement.Parent is BlockSyntax or SwitchSectionSyntax;

    // The last token of the type in a declaration, which must stay on its name's line.
    private static bool IsDeclaredTypeEnd(SyntaxToken token) => token.Parent?.AncestorsAndSelf().OfType<TypeSyntax>().LastOrDefault() is { } type
        && type.GetLastToken() == token
        && type.Parent is VariableDeclarationSyntax or LocalFunctionStatementSyntax or MethodDeclarationSyntax or ParameterSyntax;

    // A recognised end_of_line setting wins; otherwise the file's first line break decides.
    private static async Task<string> NewLineAsync(Document document, CancellationToken token)
    {
        var tree = await document.GetSyntaxTreeAsync(token).ConfigureAwait(false);
        if (tree is not null
            && document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(tree)
                .TryGetValue("end_of_line", out var configured))
        {
            switch (configured)
            {
                case "lf":
                    return "\n";
                case "crlf":
                    return "\r\n";
                case "cr":
                    return "\r";
            }
        }

        var text = (await document.GetTextAsync(token).ConfigureAwait(false)).ToString();
        var firstNewline = text.IndexOf('\n');
        return firstNewline > 0 && text[firstNewline - 1] == '\r' ? "\r\n" : "\n";
    }
}
