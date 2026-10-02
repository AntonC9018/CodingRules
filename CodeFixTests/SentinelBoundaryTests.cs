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
