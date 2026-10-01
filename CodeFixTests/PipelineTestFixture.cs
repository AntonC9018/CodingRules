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

internal static class PipelineTestFixture
{
    public static Document Document(string source, string? config = null) => StatementOperationTestFixture.Document(source, config);
    public static async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document, bool all = false)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var analyzers = all ? ImmutableArray.Create<DiagnosticAnalyzer>(new PipelineAnalyzer(), new StatementOperationAnalyzer(),
            new InlineConditionAnalyzer(), new ExplicitReturnDecisionAnalyzer(), new NestedReturnDecisionAnalyzer())
            : ImmutableArray.Create<DiagnosticAnalyzer>(new PipelineAnalyzer());
        var diagnostics = await compilation!.WithAnalyzers(analyzers, document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0001");
        return diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ThenBy(diagnostic => diagnostic.Id).ToImmutableArray();
    }
    public static async Task<List<CodeAction>> Actions(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        await new PipelineCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        return actions;
    }
    public static async Task<Document> Fix(Document document, string key, string? id = null)
    {
        var diagnostic = (await Diagnostics(document)).First(item => id is null || item.Id == id);
        var action = Assert.Single((await Actions(document, diagnostic)).Where(item => item.EquivalenceKey == key));
        var operation = Assert.Single((await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        var changed = await StatementOperationTestFixture.Reparse(operation.ChangedSolution.GetDocument(document.Id)!);
        await StatementOperationTestFixture.Compiles(changed);
        return changed;
    }
}
