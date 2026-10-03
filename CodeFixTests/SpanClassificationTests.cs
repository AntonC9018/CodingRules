using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CodingRules;

public sealed class SpanClassificationTests
{
    [Theory]
    [InlineData("text.Trim().Length", "int")]
    [InlineData("(text.TrimStart()).Length", "int")]
    [InlineData("((text.TrimEnd())).Length", "int")]
    [InlineData("text.Trim() == \"a\"", "bool")]
    [InlineData("\"a\" == text.TrimStart()", "bool")]
    [InlineData("text.TrimEnd() != \"\"", "bool")]
    [InlineData("\"a\" != (text.Trim())", "bool")]
    [InlineData("text.Trim() == nameof(C)", "bool")]
    [InlineData("text.TrimStart() == Expected", "bool")]
    [InlineData("\"\\\"\\n\\0\" == text.TrimEnd()", "bool")]
    public async Task ExactImmediateCatalogueHasSavedCompliantAction(string expression, string type)
    {
        var document = SpanTestFixture.Document("class C { const string Expected = \"a\"; static " + type + " F(string text) => " + expression + "; }");
        var diagnostic = Assert.Single(await SpanTestFixture.Diagnostics(document));
        Assert.Equal("CR0700", diagnostic.Id);
        Assert.Contains(".Trim", (await document.GetTextAsync()).ToString().Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        var changed = await SpanTestFixture.Fix(document, diagnostic);
        Assert.Empty(await SpanTestFixture.Diagnostics(changed, all: true));
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("global::System.MemoryExtensions.AsSpan(text:", text);
        Assert.DoesNotContain(".ToString(", text);
        Assert.DoesNotContain("text.Trim", text);
    }

    [Theory]
    [InlineData("text.Trim('x').Length")]
    [InlineData("text.Trim(new char[0]).Length")]
    [InlineData("text.Trim().Trim().Length")]
    [InlineData("text.Substring(1).Length")]
    [InlineData("text[1..].Length")]
    [InlineData("text.Trim().StartsWith(\"a\") ? 1 : 0")]
    [InlineData("text.Trim() == null ? 1 : 0")]
    [InlineData("text.Trim() == other ? 1 : 0")]
    [InlineData("(object)text.Trim() == (object)\"a\" ? 1 : 0")]
    [InlineData("text?.Trim().Length ?? 0")]
    public async Task OutsideFiniteCatalogueIsManual(string expression) => Assert.Empty(await SpanTestFixture.Diagnostics(
        SpanTestFixture.Document("class C { static int F(string text, string other) => " + expression + "; }")));

    [Theory]
    [InlineData("class Text { public Text Trim() => this; public int Length => 0; } class C { int F(Text text) => text.Trim().Length; }")]
    [InlineData("class C { int F(dynamic text) => text.Trim().Length; }")]
    [InlineData("class C { string F(string text) => text.Trim(); string field = \" a \".Trim(); }")]
    [InlineData("class C { string F(string text) { var owned = text.Trim(); return owned.Length == 0 ? null! : owned; } }")]
    [InlineData("class C { static System.Linq.Expressions.Expression<System.Func<string,int>> E = text => text.Trim().Length; }")]
    [InlineData("class C { int F(string text) => text.Trim(/* keep */).Length; }")]
    [InlineData("class C { unsafe int F(string text) => text.Trim().Length; }")]
    [InlineData("class C { int F(string text) { label: return text.Trim().Length; } }")]
    [InlineData("class C { static int Use(int value) => value; int F(string text) => Use(text.Trim().Length); }")]
    [InlineData("class C { static string Get(int value) => \" a \"; int F() => Get(1).Trim().Length; }")]
    [InlineData("namespace System { public static class MemoryExtensions {} } class C { int F(string text) => text.Trim().Length; }")]
    [InlineData("namespace System { public ref struct ReadOnlySpan<T> {} } class C { int F(string text) => text.Trim().Length; }")]
    [InlineData("class C { System.Collections.Generic.Dictionary<string,string> F(string text) => new System.Collections.Generic.Dictionary<string,string> { [text.Trim()] = text.Trim() }; }")]
    [InlineData("class C { System.Collections.Generic.IEnumerable<string> F(string text) { yield return text.Trim(); } }")]
    [InlineData("class C { System.Collections.Generic.IEnumerable<string> F(string[] texts) => System.Linq.Enumerable.Select(texts, text => text.Trim()); }")]
    [InlineData("class C { int F(string text) => System.MemoryExtensions.Trim(System.MemoryExtensions.AsSpan(text)).Length; }")]
    [InlineData("class C { string F(string text) => System.MemoryExtensions.Trim(System.MemoryExtensions.AsSpan(text)).ToString(); }")]
    [InlineData("class C {\n#nullable enable\nstatic int field = \" a \".Trim().Length;\n#nullable disable\n}")]
    public async Task UnsafeAndLookalikeRootsNeverPromiseAnAction(string source) => Assert.Empty(await SpanTestFixture.Diagnostics(SpanTestFixture.Document(source)));

    [Theory]
    [InlineData("2.0", 0)]
    [InlineData("2.1", 3)]
    public async Task ActualNetstandardSurfaceControlsAvailability(string version, int expected)
    {
        var packages = NuGetTestPaths.GetPackagesDirectory();
        var folder = version == "2.0" ? Path.Combine(packages, "netstandard.library/2.0.3/build/netstandard2.0/ref")
            : Path.Combine(Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "../../..")), "packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1");
        var document = SpanTestFixture.Document("class C { int A(string text) => text.Trim().Length; bool B(string text) => text.TrimStart() == \"a\"; int D(string text) => text.TrimEnd().Length; }");
        document = document.Project.WithMetadataReferences(Directory.GetFiles(folder, "*.dll").Select(path => MetadataReference.CreateFromFile(path))).GetDocument(document.Id)!;
        await StatementOperationTestFixture.Compiles(document);
        Assert.Equal(expected, (await SpanTestFixture.Diagnostics(document)).Length);
        while (!(await SpanTestFixture.Diagnostics(document)).IsEmpty) document = await SpanTestFixture.Fix(document);
        Assert.Empty(await SpanTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task RealSystemMemoryBackedNetstandard20SupportsOnlyAvailableSignatures()
    {
        var packages = NuGetTestPaths.GetPackagesDirectory();
        var folder = Path.Combine(packages, "netstandard.library/2.0.3/build/netstandard2.0/ref");
        var memory = Path.Combine(packages, "system.memory/4.5.5/lib/netstandard2.0/System.Memory.dll");
        Assert.True(File.Exists(memory), memory);
        var document = SpanTestFixture.Document("class C { int A(string text) => text.Trim().Length; bool B(string text) => text.Trim() == \"a\"; int D(string text) => text.TrimStart().Length; int E(string text) => text.TrimEnd().Length; }");
        var references = Directory.GetFiles(folder, "*.dll").Concat(new[] { memory }).Select(path => MetadataReference.CreateFromFile(path));
        document = document.Project.WithMetadataReferences(references).GetDocument(document.Id)!;
        await StatementOperationTestFixture.Compiles(document);
        Assert.Equal(2, (await SpanTestFixture.Diagnostics(document)).Length);
        var model = (await document.GetSemanticModelAsync())!;
        foreach (var invocation in (await document.GetSyntaxRootAsync())!.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                     .Where(node => node.Expression.ToString().EndsWith("TrimStart") || node.Expression.ToString().EndsWith("TrimEnd")))
            Assert.Single(((Microsoft.CodeAnalysis.Operations.IInvocationOperation)model.GetOperation(invocation)!).TargetMethod.Parameters);
        while (!(await SpanTestFixture.Diagnostics(document)).IsEmpty) document = await SpanTestFixture.Fix(document);
        Assert.Empty(await SpanTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData(LanguageVersion.CSharp7)]
    [InlineData(LanguageVersion.CSharp8)]
    [InlineData(LanguageVersion.CSharp12)]
    public async Task LanguageOptionsDoNotMoveSpansIntoAsyncOrIteratorState(LanguageVersion language)
    {
        var document = SpanTestFixture.Document("#nullable disable\nclass C { static async System.Threading.Tasks.Task<int> F(string text) { await System.Threading.Tasks.Task.Yield(); return text.Trim().Length; } static System.Collections.Generic.IEnumerable<int> G(string text) { yield return text.Trim().Length; } }", language: language);
        if (language < LanguageVersion.CSharp8) document = document.WithText(Microsoft.CodeAnalysis.Text.SourceText.From((await document.GetTextAsync()).ToString().Replace("#nullable disable\n", "")))
            .Project.WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Disable)).Documents.Single();
        Assert.Equal(2, (await SpanTestFixture.Diagnostics(document)).Length);
        while (!(await SpanTestFixture.Diagnostics(document)).IsEmpty) document = await SpanTestFixture.Fix(document);
        Assert.Empty(await SpanTestFixture.Diagnostics(document, all: true));
    }
}
