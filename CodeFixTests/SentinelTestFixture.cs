using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

internal static class SentinelTestFixture
{
    public static Document Document(string source, string? config = null) => StatementOperationTestFixture.Document(source, config);
    public static async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PrimitiveSentinelAnalyzer()), document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, item => item.Id == "AD0001");
        return diagnostics.OrderBy(item => item.Location.SourceSpan.Start).ToImmutableArray();
    }
    public static async Task<List<CodeAction>> Actions(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        await new PrimitiveSentinelCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        return actions;
    }
    public static async Task<Document> Fix(Document document, Diagnostic? diagnostic = null)
    {
        diagnostic ??= (await Diagnostics(document)).First(item => item.Id == DiagnosticIds.UncheckedHelperSentinel);
        var action = Assert.Single(await Actions(document, diagnostic));
        var applied = Assert.Single((await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        var changed = await StatementOperationTestFixture.Reparse(applied.ChangedSolution.GetDocument(document.Id)!);
        await StatementOperationTestFixture.Compiles(changed);
        return changed;
    }
    public const string Loop = "private static int Search(string[] values, string target) { for (int i = 0; i < values.Length; i++) { if (values[i] == target) return i; } return -1; }";
}
