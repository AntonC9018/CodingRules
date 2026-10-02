using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class SentinelInteractionTests
{
    [Fact]
    public async Task EarlierFamiliesRemainIndependentAfterTheSentinelFix()
    {
        var document = SentinelTestFixture.Document("""
            using System.Linq;
            class C {
                static int Search(string text, int start, int count) => text.IndexOf('x', start, count);
                static int Forward(string text) => Search(text, 0, 1);
                static bool ReturnRule(bool a, bool b) { if (a) return true; return b && a; }
                static int Nested(bool a) { if (a) { return a ? 1 : 2; } return 0; }
                static bool Condition(bool a, bool b, bool c) => a && b && c;
                static int Get(int a) => a;
                static void Use(int a) {}
                static void Operations() => Use(Get(1));
                static object Pipeline(int[] values) => values.Select(x => (x + 1) * 2).ToArray();
                static void SameType(int first, int second) {}
                static void Names() => SameType(1, 2);
            }
            """);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new PrimitiveSentinelAnalyzer(), new ExplicitReturnDecisionAnalyzer(),
            new NestedReturnDecisionAnalyzer(), new InlineConditionAnalyzer(), new StatementOperationAnalyzer(), new PipelineAnalyzer(), new NamedArgumentAnalyzer());
        async Task<ImmutableArray<Diagnostic>> Analyze(Document current)
        {
            var compilation = (await current.Project.GetCompilationAsync())!;
            var diagnostics = await compilation.WithAnalyzers(analyzers, current.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
            Assert.DoesNotContain(diagnostics, item => item.Id == "AD0001"); return diagnostics;
        }
        var before = await Analyze(document);
        Assert.Contains(before, item => item.Id == "CR0001"); Assert.Contains(before, item => item.Id == "CR0003");
        Assert.Contains(before, item => item.Id.StartsWith("CR02")); Assert.Contains(before, item => item.Id.StartsWith("CR03"));
        Assert.Contains(before, item => item.Id.StartsWith("CR04")); Assert.Contains(before, item => item.Id == "CR0500");
        var changed = await SentinelTestFixture.Fix(document, Assert.Single(before.Where(item => item.Id == "CR0601")));
        var after = await Analyze(changed);
        Assert.DoesNotContain(after, item => item.Id.StartsWith("CR06"));
        Assert.Equal(before.Where(item => !item.Id.StartsWith("CR06")).Select(item => item.Id + ":" + item.GetMessage()).OrderBy(item => item),
            after.Select(item => item.Id + ":" + item.GetMessage()).OrderBy(item => item));
    }

    [Fact]
    public async Task CatalogueDoesNotTrustSourceFrameworkLookalikes()
    {
        var document = SentinelTestFixture.Document("namespace System.Collections.Generic { class List<T> { public int IndexOf(T value) => -1; } } class C { public int Find() => new System.Collections.Generic.List<string>().IndexOf(\"x\"); }");
        Assert.Empty(await SentinelTestFixture.Diagnostics(document));
    }
}
