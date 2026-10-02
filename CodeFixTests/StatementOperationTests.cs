using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class StatementOperationTests
{
    [Theory]
    [InlineData("Use(int.Parse(text))", "CR0300")]
    [InlineData("Use(flag ? 1 : 2)", "CR0301")]
    [InlineData("Use((a + b) * c)", "CR0300")]
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
    [InlineData("a + b + c")]
    [InlineData("System.Math.Max(a, b) * c")]
    public async Task StandaloneArithmeticHasBothActions(string expression)
    {
        var source = "class C { static int M(int a, int b, int c) => " + expression + "; }";
        foreach (var key in new[] { StatementOperationCodeFixProvider.ExtractKey, StatementOperationCodeFixProvider.ExpandKey })
        {
            var document = StatementOperationTestFixture.Document(source);
            Assert.Equal("CR0302", Assert.Single(await StatementOperationTestFixture.Diagnostics(document)).Id);
            var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, key));
            await StatementOperationTestFixture.Compiles(changed);
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        }
    }
}
