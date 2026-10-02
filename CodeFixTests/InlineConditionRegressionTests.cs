using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Xunit;

namespace CodingRules;

public sealed class InlineConditionRegressionTests
{
    [Theory]
    [InlineData("(A() && int.Parse(\"bad\") > 0) == true", false)]
    [InlineData("(A() && int.Parse(\"1\") > 0) == false", false)]
    [InlineData("(A() || int.Parse(\"bad\") > 0) == true", true)]
    [InlineData("(A() && int.Parse(\"1\") > 0) == true", true)]
    [InlineData("(A() ? int.Parse(\"bad\") : 0) > 0", false)]
    [InlineData("(A() ? int.Parse(\"1\") : 0) > 0", true)]
    public async Task NestedLazyProducerPreservesRuntimeAfterSaving(string condition, bool a)
    {
        var source = "public class C { static string trace = \"\"; static bool A() { trace += \"A\"; return "
            + a.ToString().ToLowerInvariant() + "; } public static string Run() { bool result = " + condition
            + "; return trace + result; } }";
        await AssertSavedFixes(source, both: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedCheckedMutationRemainsOnReachedPath(bool reached)
    {
        var source = "public class C { public static string Run() { int n=int.MaxValue; bool a="
            + reached.ToString().ToLowerInvariant()
            + "; try { checked { bool result = (a && ++n > 0) == true; return result.ToString() + n; } } catch(System.OverflowException) { return \"overflow\"; } } }";
        var document = InlineConditionTestFixture.Document(source);
        var diagnostic = Assert.Single(await InlineConditionTestFixture.Diagnostics(document));
        var action = Assert.Single(await InlineConditionTestFixture.Actions(document, diagnostic));
        Assert.Equal(InlineConditionCodeFixProvider.ExpandKey, action.EquivalenceKey);
        await AssertSavedFixes(source, both: false, key: action.EquivalenceKey!);
    }

    [Theory]
    [InlineData("items[int.Parse(Read())] > 0")]
    [InlineData("items[0, int.Parse(Read())] > 0")]
    public async Task ElementProducerIsLinearAndKeepsReceiverBeforeIndices(string condition)
    {
        var array = condition.Contains("[0,") ? "new int[,] {{1}}" : "new [] {1}";
        var type = condition.Contains("[0,") ? "int[,]" : "int[]";
        var source = "public class C { static string trace = \"\"; static " + type + " Items() { trace += \"receiver\"; return "
            + array + "; } static string Read() { trace += \"index\"; return \"0\"; } public static string Run() { bool result = "
            + condition.Replace("items", "Items()") + "; return trace + result; } }";
        await AssertSavedFixes(source, both: true);
    }

    [Theory]
    [InlineData("int.TryParse(\"1\", out var n) && int.Parse(\"2\") > n", "n.ToString()")]
    [InlineData("text is not null && (a || b)", "text")]
    public async Task FlowExpansionLowersTheWholeSelectedRoot(string condition, string outcome)
    {
        var source = "public class C { public static string Run() { string? text=\"x\"; bool a=true,b=false; if ("
            + condition + ") return " + outcome + "; return \"none\"; } }";
        var document = InlineConditionTestFixture.Document(source);
        var action = Assert.Single(await InlineConditionTestFixture.Actions(document,
            Assert.Single(await InlineConditionTestFixture.Diagnostics(document))));
        Assert.Equal(InlineConditionCodeFixProvider.ExpandKey, action.EquivalenceKey);
        await AssertSavedFixes(source, both: false, key: action.EquivalenceKey!);
    }

    [Fact]
    public async Task NestedArgumentCannotUseAnOuterNullableFlowPlan()
    {
        var source = "class C { static bool Use(bool value)=>value; string M(string? text) { if (text is not null && Use(text.Length > 0 && true && true)) return text; return \"none\"; } }";
        var document = InlineConditionTestFixture.Document(source);
        await InlineConditionTestFixture.Compiles(document);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("static bool Flag = Use(A() && B() && D());", "Flag")]
    [InlineData("static bool P { get; } = Use(A() && B() && D());", "P")]
    [InlineData("bool Flag = Use(A() && B() && D());", "new C().Flag")]
    [InlineData("bool P { get; } = Use(A() && B() && D());", "new C().P")]
    public async Task NestedMemberInitializationOffersWorkingHelper(string member, string read)
    {
        var source = "public class C { static string trace=\"\"; static bool A() { trace+=\"A\"; return true; } static bool B() { trace+=\"B\"; return false; } static bool D() { trace+=\"D\"; return true; } static bool Use(bool value) { trace+=\"Use\"; return value; } "
            + member + " public static string Run() { bool result=" + read + "; return trace + result; } }";
        var document = InlineConditionTestFixture.Document(source);
        var action = Assert.Single(await InlineConditionTestFixture.Actions(document,
            Assert.Single(await InlineConditionTestFixture.Diagnostics(document))));
        Assert.Equal(InlineConditionCodeFixProvider.ExtractKey, action.EquivalenceKey);
        await AssertSavedFixes(source, both: false);
    }

    [Theory]
    [InlineData("C() => Use(A() && B() && D());")]
    [InlineData("~C() => Use(A() && B() && D());")]
    public async Task ConstructorAndDestructorArrowsBecomeBlocks(string member)
    {
        var source = "public class C { static bool A()=>true; static bool B()=>true; static bool D()=>true; static void Use(bool value) { } "
            + member + " public static string Run() { var value=new C(); return \"ok\"; } }";
        await AssertSavedFixes(source, both: false);
        var changed = await InlineConditionTestFixture.Fix(InlineConditionTestFixture.Document(source), InlineConditionCodeFixProvider.ExtractKey);
        Assert.DoesNotContain(member, (await changed.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData("items[int.Parse(\"0\")] > 0", InlineConditionCodeFixProvider.ExtractKey)]
    [InlineData("items[int.Parse(\"0\")] > 0", InlineConditionCodeFixProvider.ExpandKey)]
    [InlineData("(a ? int.Parse(\"1\") : 0) > 0", InlineConditionCodeFixProvider.ExtractKey)]
    [InlineData("(a ? int.Parse(\"1\") : 0) > 0", InlineConditionCodeFixProvider.ExpandKey)]
    [InlineData("A() && B() && D()", InlineConditionCodeFixProvider.ExtractKey)]
    public async Task FixAllMakesBoundedProgressAndIsIdempotent(string condition, string key)
    {
        var source = key == InlineConditionCodeFixProvider.ExtractKey && condition.StartsWith("A()")
            ? "class C { static bool A()=>true; static bool B()=>true; static bool D()=>true; static void Use(bool value) { } C()=>Use(" + condition + "); }"
            : "class C { void M() { var items=new[]{1}; bool a=true; bool result=" + condition + "; } }";
        var document = InlineConditionTestFixture.Document(source);
        Assert.Single(await InlineConditionTestFixture.Diagnostics(document));
        using var timeout = new CancellationTokenSource(System.TimeSpan.FromSeconds(15));
        var changed = await FixAll(document, key, timeout.Token);
        await InlineConditionTestFixture.Compiles(changed);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(await InlineConditionTestFixture.Reparse(changed)));
        var repeated = await FixAll(changed, key, timeout.Token);
        Assert.Equal((await changed.GetTextAsync()).ToString(), (await repeated.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task FixingProducerRetainsAnUnrelatedRootAndReturnWarning()
    {
        var document = InlineConditionTestFixture.Document("class C { int M(bool a,bool b,bool c) { bool parsed=int.Parse(\"1\")>0; if(a && b && c) return 1; return a ? 2 : 3; } }");
        var before = await InlineConditionTestFixture.Diagnostics(document, includeReturns: true);
        Assert.Equal(new[] { "CR0203", "CR0201", "CR0001" }, before.Select(diagnostic => diagnostic.Id));
        foreach (var key in new[] { InlineConditionCodeFixProvider.ExtractKey, InlineConditionCodeFixProvider.ExpandKey })
        {
            var changed = await InlineConditionTestFixture.Fix(document, key, DiagnosticIds.CombinedConditionOperations);
            var reparsed = await InlineConditionTestFixture.Reparse(changed);
            Assert.Equal(new[] { "CR0201", "CR0001" }, (await InlineConditionTestFixture.Diagnostics(reparsed, true)).Select(diagnostic => diagnostic.Id));
        }
    }

    [Theory]
    [InlineData("(text ?? int.Parse(\"1\").ToString()) == \"1\"")]
    [InlineData("(text?.Replace(\"x\", \"y\")) == \"y\"")]
    public async Task UnsupportedLazyProducerShapeIsOmittedIndividually(string condition)
    {
        var document = InlineConditionTestFixture.Document("class C { bool M(string? text)=>" + condition + "; }");
        await InlineConditionTestFixture.Compiles(document);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(document));
    }

    private static async Task AssertSavedFixes(string source, bool both, string key = InlineConditionCodeFixProvider.ExtractKey)
    {
        var document = InlineConditionTestFixture.Document(source);
        var before = await InlineConditionTestFixture.Run(document);
        var diagnostic = Assert.Single(await InlineConditionTestFixture.Diagnostics(document));
        var actions = await InlineConditionTestFixture.Actions(document, diagnostic);
        Assert.Equal(both ? 2 : 1, actions.Count);
        foreach (var action in actions)
        {
            var changed = await InlineConditionTestFixture.Fix(document, action.EquivalenceKey!);
            Assert.Equal(before, await InlineConditionTestFixture.Run(changed));
            var reparsed = await InlineConditionTestFixture.Reparse(changed);
            Assert.Equal(before, await InlineConditionTestFixture.Run(reparsed));
            Assert.Empty(await InlineConditionTestFixture.Diagnostics(reparsed));
        }

        Assert.Contains(actions, action => action.EquivalenceKey == key);
    }

    private static async Task<Document> FixAll(Document document, string key, CancellationToken token)
    {
        var provider = new InlineConditionCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Document, key, provider.FixableDiagnosticIds,
            new Diagnostics(), token);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        var operation = Assert.Single((await action!.GetOperationsAsync(token)).OfType<ApplyChangesOperation>());
        return operation.ChangedSolution.GetDocument(document.Id)!;
    }

    private sealed class Diagnostics : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            await InlineConditionTestFixture.Diagnostics(document);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            var diagnostics = new List<Diagnostic>();
            foreach (var document in project.Documents)
            {
                diagnostics.AddRange(await InlineConditionTestFixture.Diagnostics(document));
            }

            return diagnostics;
        }
    }
}
