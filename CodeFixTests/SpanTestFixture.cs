using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

internal static class SpanTestFixture
{
    public static Document Document(string source, string? config = null, LanguageVersion language = LanguageVersion.CSharp12) =>
        StatementOperationTestFixture.Document(source, config).Project.WithParseOptions(new CSharpParseOptions(language)).Documents.Single();
    public static async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document, bool all = false)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var analyzers = all ? ImmutableArray.Create<DiagnosticAnalyzer>(new SpanTextAnalyzer(), new ExplicitReturnDecisionAnalyzer(),
            new NestedReturnDecisionAnalyzer(), new InlineConditionAnalyzer(), new StatementOperationAnalyzer(), new PipelineAnalyzer(),
            new NamedArgumentAnalyzer(), new PrimitiveSentinelAnalyzer()) : ImmutableArray.Create<DiagnosticAnalyzer>(new SpanTextAnalyzer());
        var diagnostics = await compilation!.WithAnalyzers(analyzers, document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, item => item.Id == "AD0001");
        return diagnostics.OrderBy(item => item.Location.SourceSpan.Start).ThenBy(item => item.Id).ToImmutableArray();
    }
    public static async Task<List<CodeAction>> Actions(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        await new SpanTextCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        return actions;
    }
    public static async Task<Document> Fix(Document document, Diagnostic? diagnostic = null)
    {
        diagnostic ??= (await Diagnostics(document)).First();
        var actions = await Actions(document, diagnostic);
        Assert.True(actions.Count == 1, "No span action for: " + (await document.GetTextAsync()).ToString());
        var operation = Assert.Single((await actions[0].GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        var changed = await StatementOperationTestFixture.Reparse(operation.ChangedSolution.GetDocument(document.Id)!);
        await StatementOperationTestFixture.Compiles(changed);
        return changed;
    }
}
