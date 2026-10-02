using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class SentinelBoundaryTests
{
    private static async Task<Document> Metadata(string source)
    {
        var producer = SentinelTestFixture.Document("""
            public interface I<T> { int Find(T value); }
            public interface Inherited : I<string> { }
            public abstract class Base { public abstract int Find(string value); }
            public static class External {
                public static void Fixed(System.Func<int> callback) { }
                public static void Generic<T>(System.Func<T> callback) { }
            }
            """);
        var compilation = await producer.Project.GetCompilationAsync();
        using var stream = new MemoryStream();
        var emitted = compilation!.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var consumer = SentinelTestFixture.Document(source);
        return consumer.Project.AddMetadataReference(MetadataReference.CreateFromImage(stream.ToArray())).GetDocument(consumer.Id)!;
    }

    [Theory]
    [InlineData("class C : I<string> { public int Find(string value) => value.IndexOf('x'); }")]
    [InlineData("class C : Inherited { int I<string>.Find(string value) => value.IndexOf('x'); }")]
    [InlineData("class C : Base { public override int Find(string value) => value.IndexOf('x'); }")]
    [InlineData("abstract class Owned : Base { public abstract override int Find(string value); } class C : Owned { public override int Find(string value) => value.IndexOf('x'); }")]
    [InlineData("interface Owned { int Find(string value); } class C : I<string>, Owned { public int Find(string value) => value.IndexOf('x'); }")]
    public async Task ActualMetadataContractsImposeSignatures(string source)
    {
        var document = await Metadata(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await SentinelTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("interface I { int Find(string value); } class C : I { public int Find(string value) => value.IndexOf('x'); }")]
    [InlineData("interface I { int Find(string value); } class C : I { int I.Find(string value) => value.IndexOf('x'); }")]
    [InlineData("abstract class Base { public abstract int Find(string value); } class C : Base { public override int Find(string value) => value.IndexOf('x'); }")]
    public async Task SourceOwnedContractsRemainOwned(string source)
        => Assert.Equal(DiagnosticIds.PrimitiveSentinelReturn, Assert.Single(await SentinelTestFixture.Diagnostics(SentinelTestFixture.Document(source))).Id);

    [Fact]
    public async Task FixedExternalCallbackAndApplicationSelectedGenericResultDiffer()
    {
        var document = await Metadata("class C { private static int Search() => \"abc\".IndexOf('x'); static void Accept(System.Func<int> callback) {} static void Call() { External.Fixed(() => Search()); External.Generic(() => Search()); Accept(() => Search()); System.Func<int> local = () => Search(); } }");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostics = await SentinelTestFixture.Diagnostics(document);
        Assert.Equal(3, diagnostics.Length);
        foreach (var diagnostic in diagnostics) Assert.Single(await SentinelTestFixture.Actions(document, diagnostic));
    }

    [Fact]
    public async Task NestedInitializersAndCallbacksUseTheirOwnContext()
    {
        var document = await Metadata("""
            class C {
                private static int Search() => "abc".IndexOf('x');
                static void Accept(System.Func<int> callback) { }
                static void Call() {
                    External.Fixed((() => {
                        System.Func<int> local = () => Search();
                        System.Func<int> cast = (System.Func<int>)(() => Search());
                        Accept(() => Search());
                        External.Fixed(() => Search());
                        External.Generic(() => Search());
                        return Search();
                    }));
                    System.Func<int> control = () => Search();
                }
            }
            """);
        await StatementOperationTestFixture.Compiles(document);
        var diagnostics = await SentinelTestFixture.Diagnostics(document);
        Assert.Equal(5, diagnostics.Length);
        foreach (var diagnostic in diagnostics) Assert.Single(await SentinelTestFixture.Actions(document, diagnostic));
    }

    [Fact]
    public async Task SharedHelperGraphHasBoundedWorkAndKeepsEveryForwardingSite()
    {
        var source = new System.Text.StringBuilder("class C { private static int F0(string text, bool branch) => text.IndexOf('x'); ");
        for (var index = 1; index < 20; index++)
            source.Append($"private static int F{index}(string text, bool branch) {{ if (branch) return F{index - 1}(text, branch); return F{index - 1}(text, branch); }} ");
        source.Append('}');
        var document = SentinelTestFixture.Document(source.ToString());
        var model = (await document.GetSemanticModelAsync())!;
        var root = (await document.GetSyntaxRootAsync())!;
        // Inspect the existing work budget, rather than asserting elapsed time
        // or adding an analyzer counter solely for this regression.
        var assembly = typeof(PrimitiveSentinelAnalyzer).Assembly;
        var hostType = assembly.GetType("CodingRules.SentinelHost")!;
        var summariesType = assembly.GetType("CodingRules.SentinelSummaries")!;
        var contextType = summariesType.GetNestedType("WalkContext", System.Reflection.BindingFlags.NonPublic)!;
        var context = System.Activator.CreateInstance(contextType, true)!;
        var method = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Last();
        var host = hostType.GetMethod("Create")!.Invoke(null, new object[] { method, model, CancellationToken.None });
        var summaries = System.Activator.CreateInstance(summariesType, new object[] { model.Compilation })!;
        summariesType.GetMethod("Walk", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(summaries, new[] { host, model, context, 0, (object)CancellationToken.None });
        var work = (int)contextType.GetField("Work")!.GetValue(context)!;
        Assert.InRange(work, 1, root.DescendantNodesAndSelf().Count());
        Assert.Equal(0, contextType.GetField("Truncations")!.GetValue(context));
        Assert.Equal(20, ((System.Collections.IDictionary)contextType.GetField("Completed")!.GetValue(context)!).Count);
        Assert.Equal(38, (await SentinelTestFixture.Diagnostics(document)).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DepthAndCycleUnknownsDoNotDependOnRootOrder(bool reverse)
    {
        var members = new System.Collections.Generic.List<string> { "private static int F0(string text) => text.IndexOf('x');" };
        for (var index = 1; index <= 33; index++) members.Add($"private static int F{index}(string text) => F{index - 1}(text);");
        members.Add("private static int CachedFirst(string text, bool branch) { if (branch) return F0(text); return F33(text); }");
        members.Add("private static int CycleA(string text) => CycleB(text); private static int CycleB(string text) => CycleA(text);");
        members.Add("public static int Shallow(string text) => F0(text);");
        if (reverse) members.Reverse();
        var document = SentinelTestFixture.Document("class C { " + string.Join(" ", members) + " }");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostics = await SentinelTestFixture.Diagnostics(document);
        Assert.Equal(32, diagnostics.Length);
        Assert.Single(diagnostics.Where(item => item.Id == "CR0600"));
        var root = (await document.GetSyntaxRootAsync())!;
        foreach (var method in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText is "F32" or "F33" or "CachedFirst" or "CycleA" or "CycleB"))
            Assert.DoesNotContain(diagnostics, item => method.Span.Contains(item.Location.SourceSpan));
    }

    [Fact]
    public async Task NodeLimitAppliesAcrossCalleeBodies()
    {
        var source = new System.Text.StringBuilder("class C { private static int F0(string text) => text.IndexOf('x'); ");
        for (var index = 1; index <= 10; index++)
        {
            source.Append($"private static int F{index}(string text) {{ ");
            for (var local = 0; local < 100; local++) source.Append($"int local{local} = {local}; System.Console.WriteLine(local{local}); ");
            source.Append($"return F{index - 1}(text); }} ");
        }
        source.Append("public static int Deep(string text) => F10(text); }");
        var document = SentinelTestFixture.Document(source.ToString());
        await StatementOperationTestFixture.Compiles(document);
        var diagnostics = await SentinelTestFixture.Diagnostics(document);
        Assert.NotEmpty(diagnostics);
        Assert.DoesNotContain(diagnostics, item => item.Id == "CR0600");
    }

    [Theory]
    [InlineData("public int Search(string[] values, string target) { for (var i = 0; i < values.Length; i++) if (values[i] == target) return i; return -1; }", true)]
    [InlineData("public int Search(string target) { string[] values = new[] { target }; for (var i = 0; i < values.Length; i++) if (values[i] == target) return i; return -1; }", true)]
    [InlineData("string[] values = new string[1]; public int Search(string target) { for (var i = 0; i < values.Length; i++) if (values[i] == target) return i; return -1; }", false)]
    [InlineData("public int Search(string[] values, string target) { for (var i = 0; i < values.Length; i++) if ((values = new string[1])[i] == target) return i; return -1; }", false)]
    [InlineData("static bool Predicate(ref int i) => true; public int Search(string[] values, string target) { for (var i = 0; i < values.Length; i++) if (values[i] == target && Predicate(ref i)) return i; return -1; }", false)]
    [InlineData("class Values { public int Length => 1; public string this[int i] => \"x\"; } public int Search(string target) { var values = new Values(); for (var i = 0; i < values.Length; i++) if (values[i] == target) return i; return -1; }", false)]
    [InlineData("public int Search(string[] values, string target) { for (var i = 0; i < values.Length; i += 1) if (values[i] == target) return i; return -1; }", false)]
    public async Task SourceLoopRequiresStableBuiltInArrayAndCounter(string member, bool report)
    {
        var diagnostics = await SentinelTestFixture.Diagnostics(SentinelTestFixture.Document("class C { " + member + " }"));
        Assert.Equal(report ? 1 : 0, diagnostics.Length);
    }

    [Theory]
    [InlineData("#pragma warning disable CR0600\npublic int First(string text) => text.IndexOf('x');\n#pragma warning restore CR0600\npublic int Next(string text) => text.IndexOf('x');", "CR0600")]
    [InlineData("[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Readability\", \"CR0600\")] public int First(string text) => text.IndexOf('x'); public int Next(string text) => text.IndexOf('x');", "CR0600")]
    [InlineData("#pragma warning disable CR0601\nprivate int First() => Helper();\n#pragma warning restore CR0601\nprivate int Next() => Helper();", "CR0601")]
    [InlineData("[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Readability\", \"CR0601\")] private int First() => Helper(); private int Next() => Helper();", "CR0601")]
    public async Task SuppressionKeepsNeighborsEnabled(string members, string id)
    {
        var document = SentinelTestFixture.Document("class C { private int Helper() => \"abc\".IndexOf('x');\n" + members + "\n}");
        Assert.Equal(id, Assert.Single(await SentinelTestFixture.Diagnostics(document)).Id);
    }

    [Theory]
    [InlineData("// <auto-generated/>\nclass C { public int Find() => \"abc\".IndexOf('x'); }")]
    [InlineData("[System.CodeDom.Compiler.GeneratedCode(\"test\",\"1\")] class C { public int Find() => \"abc\".IndexOf('x'); }")]
    [InlineData("class C { [System.CodeDom.Compiler.GeneratedCode(\"test\",\"1\")] private int Helper() => \"abc\".IndexOf('x'); private int Forward() => Helper(); }")]
    public async Task GeneratedContractsAreNotInferred(string source)
        => Assert.Empty(await SentinelTestFixture.Diagnostics(SentinelTestFixture.Document(source)));

    [Fact]
    public async Task CancellationAndConcurrentWalksStayDeterministic()
    {
        var document = SentinelTestFixture.Document("class C { private int Helper() => \"abc\".IndexOf('x'); private int Forward() => Helper(); public int Find() => Helper(); }");
        var compilation = (await document.Project.GetCompilationAsync())!;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(() => compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PrimitiveSentinelAnalyzer()), document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync(cancellation.Token));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => SentinelTestFixture.Diagnostics(document)));
        Assert.All(results, diagnostics => Assert.Equal(new[] { "CR0601", "CR0600" }, diagnostics.Select(item => item.Id)));
    }
}
