using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CodingRules;

public sealed class InlineConditionAnalyzerTests
{
    [Theory]
    [InlineData("(a > 0 && b > 0) || ready", "CR0200")]
    [InlineData("a > 0 && b > 0 && ready", "CR0201")]
    [InlineData("a > 0 || b > 0", "CR0202")]
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
            TestCode = "class C { void M(bool a, bool b, bool c) { System.Func<bool> f = () => {|CR0201:a && b && c|}; bool Local() { return {|CR0201:a && b && c|}; } } }",
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task UserLogicalOperatorsAreExcluded()
    {
        var test = new CSharpAnalyzerTest<InlineConditionAnalyzer, DefaultVerifier>
        {
            TestCode = "class C { public static C operator &(C a, C b) => a; public static C operator |(C a, C b) => a; public static bool operator true(C a) => true; public static bool operator false(C a) => false; void M(C a, C b, C c) { if (a && b && c) { } } }",
        };
        await test.RunAsync();
    }
}
