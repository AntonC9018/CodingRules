using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CodingRules;

/// <summary>
/// Validates a fix against the text a user saves rather than the generated
/// syntax tree, so precedence, token boundaries and trivia are compiled as written.
/// </summary>
internal static class SavedSource
{
    /// <summary>Compiles <paramref name="text"/> in place of the tree <paramref name="original"/> describes.</summary>
    public static SemanticModel Compile(SemanticModel original, SourceText text, CancellationToken token)
    {
        var originalTree = original.SyntaxTree;
        var savedTree = CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)originalTree.Options, originalTree.FilePath, token);
        return original.Compilation.ReplaceSyntaxTree(originalTree, savedTree).GetSemanticModel(savedTree);
    }

    /// <summary>
    /// Whether <paramref name="after"/> reports an error or warning more often than
    /// <paramref name="before"/>. Messages are matched by ID and text, not location,
    /// because a rewrite moves the code that existing diagnostics point at.
    /// </summary>
    public static bool AddsCompilerMessages(SemanticModel before, SemanticModel after, CancellationToken token)
    {
        var existing = CompilerMessages(before, token)
            .GroupBy(message => message)
            .ToDictionary(group => group.Key, group => group.Count());
        return CompilerMessages(after, token)
            .GroupBy(message => message)
            .Any(group => !existing.TryGetValue(group.Key, out var count) || group.Count() > count);
    }

    private static IEnumerable<string> CompilerMessages(SemanticModel model, CancellationToken token) => model
        .GetDiagnostics(cancellationToken: token)
        .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
        .Select(diagnostic => diagnostic.Id + ":" + diagnostic.GetMessage());
}
