using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class SentinelClassificationTests
{
    [Theory]
    [InlineData("public", "text.IndexOf('x')")]
    [InlineData("internal", "text.LastIndexOf('x')")]
    [InlineData("protected", "new System.Collections.Generic.List<string>().IndexOf(text)")]
    [InlineData("private protected", "new System.Collections.Generic.List<string>().LastIndexOf(text)")]
    public async Task OwnedApiUsesDeclaredVisibilityAndActualSearchContracts(string visibility, string result)
    {
        var document = SentinelTestFixture.Document($"class Outer {{ private class C {{ {visibility} int Find(string text) => {result}; }} }}");
        var diagnostic = Assert.Single(await SentinelTestFixture.Diagnostics(document));
        Assert.Equal(DiagnosticIds.PrimitiveSentinelReturn, diagnostic.Id);
        Assert.Equal("int", (await document.GetTextAsync()).ToString(diagnostic.Location.SourceSpan));
        Assert.Empty(await SentinelTestFixture.Actions(document, diagnostic));
    }

    [Theory]
    [InlineData("public int Value => \"abc\".IndexOf('x');")]
    [InlineData("public int Value { get { return \"abc\".IndexOf('x'); } }")]
    [InlineData("public int this[string text] => text.IndexOf('x');")]
    [InlineData("public static int operator +(C a, C b) => \"abc\".IndexOf('x');")]
    [InlineData("public static implicit operator int(C a) => \"abc\".IndexOf('x');")]
    public async Task ApiHosts(string member)
    {
        var diagnostic = Assert.Single(await SentinelTestFixture.Diagnostics(SentinelTestFixture.Document("class C { " + member + " }")));
        Assert.Equal(DiagnosticIds.PrimitiveSentinelReturn, diagnostic.Id);
    }

    [Theory]
    [InlineData("public int Delta(bool down) => down ? -1 : 1;")]
    [InlineData("public int Missing() => -1;")]
    [InlineData("public int Count() => 0;")]
    [InlineData("public bool Try(out int value) { value = default; return false; }")]
    [InlineData("public int? Find(string text) => text.IndexOf('x');")]
    [InlineData("public bool Has(string text) => text.IndexOf('x') >= 0;")]
    [InlineData("public int Find(string text) => text.IndexOf('x') + 1;")]
    [InlineData("public int Find(string text) => System.Array.IndexOf(new[] { text }, text);")]
    [InlineData("public int Find(string text) => new System.Collections.Generic.List<string>().BinarySearch(text);")]
    [InlineData("public int Find(string text) => IndexOf(text); private int IndexOf(string text) => -1;")]
    [InlineData("private int A() => B(); private int B() => A(); public int Find() => A();")]
    [InlineData("public async System.Threading.Tasks.Task<int> Find(string text) { await System.Threading.Tasks.Task.Yield(); return text.IndexOf('x'); }")]
    public async Task NoLiteralNameOrTransformedResultHeuristics(string member)
        => Assert.Empty(await SentinelTestFixture.Diagnostics(SentinelTestFixture.Document("class C { " + member + " }")));

    [Theory]
    [InlineData("return Search(values, target);")]
    [InlineData("return (Search(values, target));")]
    [InlineData("var value = Search(values, target); return value;")]
    [InlineData("var value = Search(values, target); int alias = value; return alias;")]
    [InlineData("if (target.Length > 0) { return Search(values, target); } throw new System.Exception();")]
    [InlineData("var value = Search(values, target); if (value < 0) { System.Console.WriteLine(\"missing\"); } return value;")]
    [InlineData("var value = Search(values, target); if (Search(values, target) < 0) return -1; return value;")]
    [InlineData("var value = Search(values, target); if (value == 1) throw new System.Exception(); return value;")]
    public async Task ProvenHelpersNeedAnExplicitSentinelReturn(string body)
    {
        var document = SentinelTestFixture.Document("class C { " + SentinelTestFixture.Loop + " private int Forward(string[] values, string target) { " + body + " } }");
        var diagnostic = Assert.Single(await SentinelTestFixture.Diagnostics(document));
        Assert.Equal(DiagnosticIds.UncheckedHelperSentinel, diagnostic.Id);
        var changed = await SentinelTestFixture.Fix(document, diagnostic);
        Assert.Empty(await SentinelTestFixture.Diagnostics(changed));
    }

    [Theory]
    [InlineData("if (value < 0) return -1; return value;")]
    [InlineData("if (value >= 0) return value; return -1;")]
    [InlineData("if (-1 == value) return -1; return value;")]
    [InlineData("if (!(0 <= value)) return -1; return value;")]
    [InlineData("const int missing = -1; if (value == missing) return -1; return value;")]
    [InlineData("if (value < 0) throw new System.Exception(); return value;")]
    public async Task FlowRefinesBothDirectionsAndThrowingAbsence(string body)
    {
        var document = SentinelTestFixture.Document("class C { " + SentinelTestFixture.Loop + " private int Forward(string[] values, string target) { var value = Search(values, target); " + body + " } }");
        Assert.Empty(await SentinelTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task SuccessOnlySummaryDoesNotPropagateAbsence()
    {
        var document = SentinelTestFixture.Document("class C { " + SentinelTestFixture.Loop + " private int Found(string[] values, string target) { var value = Search(values, target); if (value < 0) throw new System.Exception(); return value; } public int Find(string[] values, string target) => Found(values, target); private int Forward(string[] values, string target) => Found(values, target); }");
        Assert.Empty(await SentinelTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task ApiOwnerDominatesAndSeverityNoneExposesForwarding()
    {
        var source = "class C { " + SentinelTestFixture.Loop + " public int Find(string[] values, string target) => Search(values, target); }";
        Assert.Equal(DiagnosticIds.PrimitiveSentinelReturn, Assert.Single(await SentinelTestFixture.Diagnostics(SentinelTestFixture.Document(source))).Id);
        var document = SentinelTestFixture.Document(source, "root = true\n[*.cs]\ndotnet_diagnostic.CR0600.severity = none\n");
        Assert.Equal(DiagnosticIds.UncheckedHelperSentinel, Assert.Single(await SentinelTestFixture.Diagnostics(document)).Id);
    }

    [Fact]
    public async Task PartialDeclarationReportsOnceAtSharedReturnType()
    {
        var document = SentinelTestFixture.Document("partial class C { public partial int Find(string text); public partial int Find(string text) { var result = text.IndexOf('x'); if (result < 0) return -1; return result; } }");
        var diagnostic = Assert.Single(await SentinelTestFixture.Diagnostics(document));
        Assert.Equal((await document.GetTextAsync()).ToString().IndexOf("int Find"), diagnostic.Location.SourceSpan.Start);
    }

    [Theory]
    [InlineData("var value = Search(values, target); value++; return value;")]
    [InlineData("var value = Search(values, target); Mutate(ref value); return value;")]
    [InlineData("var value = Search(values, target); System.Func<int> captured = () => value; return value;")]
    [InlineData("var value = Search(values, target); if (target.Length > 0) value = 7; return value;")]
    [InlineData("var value = Search(values, target); while (value < 0) return -1; return value;")]
    public async Task UnknownProvenanceIsSkipped(string body)
    {
        var source = "class C { " + SentinelTestFixture.Loop + " static void Mutate(ref int value) { value++; } private int Forward(string[] values, string target) { " + body + " } }";
        Assert.Empty(await SentinelTestFixture.Diagnostics(SentinelTestFixture.Document(source)));
    }
}
