using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Xunit;

namespace CodingRules;

public sealed class InlineConditionInteractionTests
{
    [Theory]
    [InlineData(InlineConditionCodeFixProvider.ExtractKey)]
    [InlineData(InlineConditionCodeFixProvider.ExpandKey)]
    public async Task ExistingUnrelatedFinalDecisionRemainsVisible(string key)
    {
        var document = InlineConditionTestFixture.Document("class C { int M(bool a, bool b, bool flag) { if (a && b) return 0; return flag ? 1 : 2; } }");
        Assert.Equal(new[] { "CR0202", "CR0001" }, (await InlineConditionTestFixture.Diagnostics(document, true)).Select(diagnostic => diagnostic.Id));
        var changed = await InlineConditionTestFixture.Fix(document, key);
        Assert.Equal("CR0001", Assert.Single(await InlineConditionTestFixture.Diagnostics(changed, true)).Id);
        await InlineConditionTestFixture.Compiles(changed);
    }

    [Theory]
    [InlineData(false, InlineConditionCodeFixProvider.ExtractKey)]
    [InlineData(false, InlineConditionCodeFixProvider.ExpandKey)]
    [InlineData(true, InlineConditionCodeFixProvider.ExtractKey)]
    [InlineData(true, InlineConditionCodeFixProvider.ExpandKey)]
    public async Task BothOrdersOfOverlappingReturnFixesTerminate(bool nested, string key)
    {
        var source = nested
            ? "class C { bool M(bool a, bool b, bool c) { lock (new object()) { return a && b && c; } } }"
            : "class C { bool M(bool a, bool b, bool c) { if (!a) return false; return a && b && c; } }";
        var document = InlineConditionTestFixture.Document(source);
        var returnId = nested ? "CR0006" : "CR0001";
        var conditionFirst = await InlineConditionTestFixture.Fix(document, key);
        var remaining = await InlineConditionTestFixture.Diagnostics(conditionFirst, true);
        if (key == InlineConditionCodeFixProvider.ExtractKey)
        {
            Assert.Equal(returnId, Assert.Single(remaining).Id);
            conditionFirst = await FixReturn(conditionFirst, nested);
        }
        else
        {
            Assert.Empty(remaining);
        }

        Assert.Empty(await InlineConditionTestFixture.Diagnostics(conditionFirst, true));
        await InlineConditionTestFixture.Compiles(conditionFirst);

        var returnFirst = await FixReturn(document, nested);
        Assert.Equal("CR0201", Assert.Single(await InlineConditionTestFixture.Diagnostics(returnFirst, true)).Id);
        var changed = await InlineConditionTestFixture.Fix(returnFirst, key);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(changed, true));
        await InlineConditionTestFixture.Compiles(changed);
    }

    private static async Task<Document> FixReturn(Document document, bool nested)
    {
        var diagnostic = (await InlineConditionTestFixture.Diagnostics(document, true)).First(diagnostic => diagnostic.Id == (nested ? "CR0006" : "CR0001"));
        var actions = new List<CodeAction>();
        CodeFixProvider provider = nested ? new NestedReturnDecisionCodeFixProvider() : new ExplicitReturnDecisionCodeFixProvider();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        var operation = Assert.Single((await Assert.Single(actions).GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        return operation.ChangedSolution.GetDocument(document.Id)!;
    }

    [Theory]
    [InlineData(InlineConditionCodeFixProvider.ExtractKey)]
    [InlineData(InlineConditionCodeFixProvider.ExpandKey)]
    public async Task FixAllReplansNestedSitesSectionsAndNames(string key)
    {
        var source = """
            class C
            {
                static bool Use(bool x) => x;
                void M(bool a, bool b, bool c, int mode)
                {
                    bool CheckCondition = false;
                    var first = a && b && c;
                    if (Use(a && b && c) && b && c) { }
                    switch (mode)
                    {
                        case 1: if (a && b && c) { } break;
                        case 2: if (a && b && c) { } break;
                    }
                    bool conditionResult = CheckCondition;
                }
            }
            """;
        var document = InlineConditionTestFixture.Document(source);
        Assert.Equal(5, (await InlineConditionTestFixture.Diagnostics(document)).Length);
        var provider = new InlineConditionCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Document, key,
            provider.FixableDiagnosticIds, new DiagnosticProvider(), CancellationToken.None);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        var operation = Assert.Single((await action!.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        var changed = operation.ChangedSolution.GetDocument(document.Id)!;
        await InlineConditionTestFixture.Compiles(changed);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(changed, true));
        var fixedText = (await changed.GetTextAsync()).ToString();
        Assert.DoesNotContain("a && b && c", fixedText);
        Assert.Contains(key == InlineConditionCodeFixProvider.ExtractKey ? "CheckCondition2" : "conditionResult2", fixedText);
    }

    [Theory]
    [InlineData(FixAllScope.Document, InlineConditionCodeFixProvider.ExtractKey)]
    [InlineData(FixAllScope.Project, InlineConditionCodeFixProvider.ExtractKey)]
    [InlineData(FixAllScope.Solution, InlineConditionCodeFixProvider.ExtractKey)]
    [InlineData(FixAllScope.Document, InlineConditionCodeFixProvider.ExpandKey)]
    [InlineData(FixAllScope.Project, InlineConditionCodeFixProvider.ExpandKey)]
    [InlineData(FixAllScope.Solution, InlineConditionCodeFixProvider.ExpandKey)]
    public async Task FixAllRespectsScopeAndCrossFileBindings(FixAllScope scope, string key)
    {
        var target = InlineConditionTestFixture.Document("class C { static bool M(bool a, bool b, bool c) { if (a && b && Helper.Check(c)) { return true; } return false; } }");
        const string otherSource = "class D { static bool M(bool a, bool b, bool c) { if (a && b && Helper.Check(c)) { return true; } return false; } }";
        var other = target.Project.AddDocument("Other.cs", otherSource);
        var helpers = other.Project.AddDocument("Helper.cs", "static class Helper { public static bool Check(bool value) => value; }");
        target = helpers.Project.GetDocument(target.Id)!;
        var provider = new InlineConditionCodeFixProvider();
        using var timeout = new CancellationTokenSource(System.TimeSpan.FromSeconds(20));
        var context = new FixAllContext(target, provider, scope, key, provider.FixableDiagnosticIds,
            new DiagnosticProvider(), timeout.Token);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        var operation = Assert.Single((await action!.GetOperationsAsync(timeout.Token)).OfType<ApplyChangesOperation>());
        var changed = await InlineConditionTestFixture.Reparse(operation.ChangedSolution.GetDocument(target.Id)!);
        await InlineConditionTestFixture.Compiles(changed);
        Assert.Empty(await DocumentDiagnostics(changed));
        var changedOther = operation.ChangedSolution.GetDocument(other.Id)!;
        if (scope == FixAllScope.Document)
        {
            Assert.Equal(otherSource, (await changedOther.GetTextAsync()).ToString());
        }
        else
        {
            Assert.Empty(await DocumentDiagnostics(await InlineConditionTestFixture.Reparse(changedOther)));
        }
        Assert.Equal((await helpers.GetTextAsync()).ToString(), (await operation.ChangedSolution.GetDocument(helpers.Id)!.GetTextAsync()).ToString());
    }

    private static async Task<IEnumerable<Diagnostic>> DocumentDiagnostics(Document document)
    {
        var tree = await document.GetSyntaxTreeAsync();
        return (await InlineConditionTestFixture.Diagnostics(document))
            .Where(diagnostic => diagnostic.Location.SourceTree == tree);
    }

    private sealed class DiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            await DocumentDiagnostics(document);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            var diagnostics = new List<Diagnostic>();
            foreach (var document in project.Documents)
            {
                diagnostics.AddRange(await DocumentDiagnostics(document));
            }

            return diagnostics;
        }
    }
}
