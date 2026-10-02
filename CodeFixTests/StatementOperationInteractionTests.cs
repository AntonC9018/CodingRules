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

public sealed class StatementOperationInteractionTests
{
    [Theory]
    [InlineData(StatementOperationCodeFixProvider.ExtractKey)]
    [InlineData(StatementOperationCodeFixProvider.ExpandKey)]
    public async Task ConditionOverlapTerminatesInEitherOrder(string key)
    {
        const string source = "class C { static bool Use(bool x) => x; static bool M(string text) => Use(string.IsNullOrEmpty(text.Trim())); }";
        var document = StatementOperationTestFixture.Document(source);
        Assert.Equal(new[] { "CR0203", "CR0300" }, (await All(document)).Select(diagnostic => diagnostic.Id).OrderBy(id => id));
        var operationsFirst = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, key));
        await StatementOperationTestFixture.Compiles(operationsFirst);
        Assert.Empty(await All(operationsFirst));
        var conditionDiagnostic = Assert.Single(await InlineConditionTestFixture.Diagnostics(document));
        var conditionActions = await InlineConditionTestFixture.Actions(document, conditionDiagnostic);
        Assert.NotEmpty(conditionActions);
        foreach (var conditionKey in conditionActions.Select(action => action.EquivalenceKey!))
        {
            var conditionFirst = await StatementOperationTestFixture.Reparse(await InlineConditionTestFixture.Fix(document, conditionKey));
            for (var pass = 0; pass < 5 && (await StatementOperationTestFixture.Diagnostics(conditionFirst)).Length != 0; pass++)
                conditionFirst = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(conditionFirst, key));
            await StatementOperationTestFixture.Compiles(conditionFirst);
            Assert.Empty(await All(conditionFirst));
        }
    }

    [Theory]
    [InlineData(StatementOperationCodeFixProvider.ExtractKey)]
    [InlineData(StatementOperationCodeFixProvider.ExpandKey)]
    public async Task UnrelatedFrozenWarningsRemainVisible(string key)
    {
        const string source = "class C { static int Get(int x)=>x; static int Use(int x)=>x; static int M(bool a,bool b,bool c) { var value=Use(Get(1)); if(a&&b&&c) return value; return a ? 2 : 3; } }";
        var document = StatementOperationTestFixture.Document(source);
        var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, key));
        Assert.Equal(new[] { "CR0201", "CR0001" }, (await All(changed)).Select(diagnostic => diagnostic.Id));
    }

    [Theory]
    [InlineData(StatementOperationCodeFixProvider.ExtractKey)]
    [InlineData(StatementOperationCodeFixProvider.ExpandKey)]
    public async Task FixAllReplansNamesSectionsAndSavedSourceAndIsIdempotent(string key)
    {
        const string source = """
            class C
            {
                static int Get(int x) => x;
                static int Use(int x) => x;
                static void M(int mode)
                {
                    int operationValue = 1;
                    int CalculateValue = 2;
                    for (int i=0; i<1; i++) Use(Get(i));
                    var first = Use(Get(CalculateValue));
                    switch (mode)
                    {
                        case 1: var one = Use(Get(1)); break;
                        case 2: var two = Use(Get(2)); break;
                    }
                    Use(operationValue);
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        using var timeout = new CancellationTokenSource(System.TimeSpan.FromSeconds(20));
        var changed = await FixAll(document, key, timeout.Token);
        changed = await StatementOperationTestFixture.Reparse(changed);
        await StatementOperationTestFixture.Compiles(changed);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        var repeated = await FixAll(changed, key, timeout.Token);
        Assert.Equal((await changed.GetTextAsync()).ToString(), (await repeated.GetTextAsync()).ToString());
    }

    internal static async Task<ImmutableArray<Diagnostic>> All(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        return (await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new StatementOperationAnalyzer(), new InlineConditionAnalyzer(),
            new ExplicitReturnDecisionAnalyzer(), new NestedReturnDecisionAnalyzer()), document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync())
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ThenBy(diagnostic => diagnostic.Id).ToImmutableArray();
    }

    private static async Task<Document> FixAll(Document document, string key, CancellationToken token)
    {
        var provider = new StatementOperationCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Document, key, provider.FixableDiagnosticIds, new Diagnostics(), token);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        var operation = Assert.Single((await action!.GetOperationsAsync(token)).OfType<ApplyChangesOperation>());
        return operation.ChangedSolution.GetDocument(document.Id)!;
    }

    private sealed class Diagnostics : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) => await StatementOperationTestFixture.Diagnostics(document);
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Diagnostic>());
        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            var diagnostics = new List<Diagnostic>();
            foreach (var document in project.Documents) diagnostics.AddRange(await StatementOperationTestFixture.Diagnostics(document));
            return diagnostics;
        }
    }
}
