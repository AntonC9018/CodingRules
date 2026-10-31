using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;

namespace CodingRules;

/// <summary>
/// Formats syntax a fix generated (nodes carrying <see cref="Formatter.Annotation"/>)
/// with the line endings the edited file already uses.
/// </summary>
internal static class GeneratedSyntaxFormatter
{
    public static async Task<Document> FormatAsync(Document changed, Document original, CancellationToken token)
    {
        var options = await changed.GetOptionsAsync(token).ConfigureAwait(false);
        var newline = await NewLineAsync(original, token).ConfigureAwait(false);
        var formattingOptions = options.WithChangedOption(FormattingOptions.NewLine, changed.Project.Language, newline);
        return await Formatter.FormatAsync(changed, Formatter.Annotation, formattingOptions, token).ConfigureAwait(false);
    }

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
