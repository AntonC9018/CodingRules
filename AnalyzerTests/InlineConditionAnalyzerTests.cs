using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CodingRules;

public sealed class InlineConditionAnalyzerTests
{
    [Theory]
    [InlineData("(a > 0 && b > 0) || (ready && text.Length > 0)", "CR0200")]
    [InlineData("a > 0 && b > 0 && ready && text.Length > 0", "CR0201")]
    [InlineData("a > 0 || text.EndsWith(\"x\")", "CR0202")]
    [InlineData("int.Parse(text) > 0", "CR0203")]
    [InlineData("text.StartsWith(\"a\") || text.StartsWith(\"b\") || text.StartsWith(\"c\")", "CR0204")]
    public async Task MarksWholeHostRoot(string expression, string id)
    {
        var test = new CSharpAnalyzerTest<InlineConditionAnalyzer, DefaultVerifier>
        {
            TestCode = "class C { void M(int a, int b, bool ready, string text) { if ({|" + id + ":" + expression + "|}) { } } }",
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task NestedFunctionOwnsItsConditionIndependently()
    {
        var test = new CSharpAnalyzerTest<InlineConditionAnalyzer, DefaultVerifier>
        {
            TestCode = "class C { void M(bool a, bool b, bool c, bool d) { System.Func<bool> f = () => {|CR0201:a && b && c && d|}; bool Local() { return {|CR0201:a && b && c && d|}; } } }",
        };
        await test.RunAsync();
    }

    [Theory]
    [InlineData("!enabled || !parser.IsEmpty")]
    [InlineData("!parser.IsEmpty || name is null")]
    [InlineData("name is not null && other is null")]
    [InlineData("exit == 0 && name == \"profiles\" && data is System.Collections.Generic.IReadOnlyList<string> profiles")]
    [InlineData("count > 0 && count - 1 < limit && enabled")]
    public async Task SimpleGuardsOfUpToThreeOperandsAreAccepted(string expression)
    {
        var test = new CSharpAnalyzerTest<InlineConditionAnalyzer, DefaultVerifier>
        {
            TestCode = "class P { public bool IsEmpty; } class C { void M(bool enabled, P parser, string? name, string? other, int exit, object data, int count, int limit) { if ("
                + expression + ") { } } }",
        };
        await test.RunAsync();
    }

    [Theory]
    [InlineData("{|CR0201:a && b && c && d|}")]
    [InlineData("{|CR0201:a && b && Read()|}")]
    [InlineData("{|CR0203:a && b && int.Parse(\"1\") > 0|}")]
    [InlineData("{|CR0203:a && (count = 1) > 0|}")]
    public async Task FourOperandsOrNonSimpleOperandsAreReported(string expression)
    {
        var test = new CSharpAnalyzerTest<InlineConditionAnalyzer, DefaultVerifier>
        {
            TestCode = "class C { static bool Read() => System.Console.ReadLine() is null; bool M(bool a, bool b, bool c, bool d, int count) { return "
                + expression + "; } }",
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task UserLogicalOperatorsAreExcluded()
    {
        var test = new CSharpAnalyzerTest<InlineConditionAnalyzer, DefaultVerifier>
        {
            TestCode = "class C { public static C operator &(C a, C b) => a; public static C operator |(C a, C b) => a; public static bool operator true(C a) => true; public static bool operator false(C a) => false; void M(C a, C b, C c, C d) { if (a && b && c && d) { } } }",
        };
        await test.RunAsync();
    }
}
