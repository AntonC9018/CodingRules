using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class StatementOperationTests
{
    [Theory]
    [InlineData("Use(int.Parse(text.Trim()))", "CR0300")]
    [InlineData("Use(flag ? 1 : 2)", "CR0301")]
    [InlineData("Use(System.Math.Max(a, b) * c)", "CR0300")]
    public async Task BothActionsLinearizeSavedSource(string expression, string id)
    {
        var source = "class C { static int Use(int x) => x; static int M(string text, bool flag, int a, int b, int c) { return " + expression + "; } }";
        foreach (var key in new[] { StatementOperationCodeFixProvider.ExtractKey, StatementOperationCodeFixProvider.ExpandKey })
        {
            var document = StatementOperationTestFixture.Document(source);
            Assert.Equal(id, Assert.Single(await StatementOperationTestFixture.Diagnostics(document)).Id);
            var changed = await StatementOperationTestFixture.Fix(document, key);
            changed = await StatementOperationTestFixture.Reparse(changed);
            await StatementOperationTestFixture.Compiles(changed);
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        }
    }

    [Theory]
    [InlineData("a + b + c", "6")]
    [InlineData("System.Math.Max(a, b) * c", "6")]
    [InlineData("Use((a + b) * c)", "9")]
    public async Task ArithmeticRemainsIntactAndRunsWithoutAStagingDiagnostic(string expression, string expected)
    {
        var source = "public class C { static int Use(int value) => value; public static string Run() { int a=1,b=2,c=3; return ("
            + expression + ").ToString(); } }";
        var document = StatementOperationTestFixture.Document(source);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        Assert.Equal(source, (await document.GetTextAsync()).ToString());
    }
}
