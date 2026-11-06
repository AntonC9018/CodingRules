using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class CalculationPolicyTests
{
    [Theory]
    [InlineData("(a + b) / 2", "3")]
    [InlineData("a * a + b * b", "20")]
    [InlineData("b - a + 1", "3")]
    [InlineData("a * (b - 1)", "6")]
    [InlineData("a & ~b", "2")]
    [InlineData("(a << 2) | b", "12")]
    [InlineData("-a + b % 3", "-1")]
    [InlineData("(a >>> 1) ^ b", "5")]
    [InlineData("(int) ((double) a / b * 10)", "5")]
    public async Task FormulasRemainIntactAcrossValueHosts(string expression, string expected)
    {
        var source = $$"""
            public class C
            {
                record Value(int Number);
                static int Use(int number) => number;
                static int Formula(int a, int b) => {{expression}};
                static int Property { get { int a = 2, b = 4; return {{expression}}; } }
                public static string Run()
                {
                    int a = 2, b = 4;
                    var local = {{expression}};
                    var argument = Use({{expression}});
                    var constructed = new Value({{expression}});
                    return local + ":" + argument + ":" + constructed.Number + ":" + Formula(a, b) + ":" + Property;
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
        Assert.Equal(string.Join(":", Enumerable.Repeat(expected, 5)), await StatementOperationTestFixture.Run(document));
        Assert.Equal(source, (await document.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(0, "values", "failed:1")]
    [InlineData(0, "buffer.Values", "failed:1")]
    [InlineData(1, "values", "stored:1")]
    [InlineData(1, "buffer.Values", "stored:1")]
    public async Task IndexWriteFixPreservesIncrementTimingWhenTheStoreFails(int length, string target, string expected)
    {
        var source = $$"""
            public class C
            {
                class Buffer { public int[] Values = []; }
                public static string Run()
                {
                    int[] values = new int[{{length}}];
                    var buffer = new Buffer { Values = values };
                    int index = 0;
                    try { {{target}}[index++] = 7; }
                    catch (System.IndexOutOfRangeException) { return "failed:" + index; }
                    return "stored:" + index;
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        Assert.Equal("CR0302", diagnostic.Id);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var changed = await StatementOperationTestFixture.Reparse(
                await StatementOperationTestFixture.Fix(document, action.EquivalenceKey!));
            Assert.Equal(expected, await StatementOperationTestFixture.Run(changed));
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        }
    }
    [Fact]
    public async Task PipelineFormulasKeepLazyEvaluationAndRuntimeGrouping()
    {
        const string source = """
            using System.Linq;
            public class C
            {
                static int calls;
                static int Read(int value) { calls++; return value; }
                public static string Run()
                {
                    calls = 0;
                    var query = new[] { 1, 2 }.Select(x => Read(x) * 2 + 1);
                    if (calls != 0) return "eager";
                    var values = string.Join(",", query);
                    return values + ":" + calls;
                }
            }
            """;
        var document = PipelineTestFixture.Document(source);
        Assert.Empty(await PipelineTestFixture.Diagnostics(document, true));
        Assert.Equal("3,5:2", await StatementOperationTestFixture.Run(document));
        Assert.Equal(source, (await document.GetTextAsync()).ToString());
    }
}
