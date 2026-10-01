using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class StatementOperationHostTests
{
    private const string Prelude = "using System; using System.Collections.Generic; class C { static int Get(int x) => x; static int Use(int x) => x; static int[] Items(int x) => new[] { x }; ";

    [Theory]
    [InlineData("var x = Use(Get(1)); return x;", true)]
    [InlineData("return Use(Get(1));", true)]
    [InlineData("int x = 0; x = Use(Get(1)); return x;", true)]
    [InlineData("var c = new C(); c.Value = Use(Get(1)); return c.Value;", true)]
    [InlineData("var array = new int[1]; array[0] = Use(Get(1)); return array[0];", true)]
    [InlineData("Func<int, int> f = x => Use(Get(x)); return f(1);", true)]
    [InlineData("int Local(int x) => Use(Get(x)); return Local(1);", true)]
    [InlineData("return flag && Use(Get(1)) > 0 ? 1 : 0;", true)]
    [InlineData("return flag ? Use(Get(1)) : 0;", true)]
    [InlineData("while (Use(Get(1)) > 0) break; return 0;", false)]
    [InlineData("do { break; } while (Use(Get(1)) > 0); return 0;", false)]
    [InlineData("for (var x = Use(Get(1)); x < 2; x++) break; return 0;", false)]
    [InlineData("for (var x = 0; Use(Get(x)) > 0; x++) break; return 0;", false)]
    [InlineData("for (var x = 0; x < 1; Use(Get(x))) break; return 0;", false)]
    [InlineData("foreach (var x in Items(Get(1))) return x; return 0;", false)]
    [InlineData("try { return 0; } catch (Exception ex) when (Use(Get(ex.Message.Length)) > 0) { return 1; }", false)]
    [InlineData("switch (value) { case int x when Use(Get(x)) > 0: return x; default: return 0; }", false)]
    [InlineData("lock (new object()) { var x = Use(Get(1)); return x; }", true)]
    [InlineData("try { return Use(Get(1)); } finally { }", true)]
    public async Task AcceptedStatementHostsHaveACompilingSavedFix(string body, bool both)
    {
        var source = Prelude + "int Value { get; set; } static int M(bool flag, object value) { " + body + " } }";
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.Contains(actions, action => action.EquivalenceKey == StatementOperationCodeFixProvider.ExtractKey);
        if (both) Assert.Contains(actions, action => action.EquivalenceKey == StatementOperationCodeFixProvider.ExpandKey);
        foreach (var action in actions)
        {
            var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, action.EquivalenceKey!));
            await StatementOperationTestFixture.Compiles(changed);
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        }
    }

    [Theory]
    [InlineData("static int Value = Use(Get(1));")]
    [InlineData("int Value = Use(Get(1));")]
    [InlineData("static int Value { get; } = Use(Get(1));")]
    [InlineData("int Value { get; } = Use(Get(1));")]
    [InlineData("static int Value => Use(Get(1));")]
    public async Task MemberInitializersAndBodiesKeepTheirEvaluationSite(string member)
    {
        var document = StatementOperationTestFixture.Document(Prelude + member + "}");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, StatementOperationCodeFixProvider.ExtractKey));
        await StatementOperationTestFixture.Compiles(changed);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
    }

    [Theory]
    [InlineData("using System.Linq.Expressions;", "Expression<Func<int, int>> f = x => Use(Get(x));")]
    [InlineData("", "Func<int, int> f = x => Use(x + 1);")]
    [InlineData("", "var x = Use(Get());")]
    [InlineData("", "var x = Use(Get(/* boundary */ 1));")]
    public async Task HostBoundariesAndDirectCounterpartsAreSilent(string imports, string body)
    {
        var source = imports + Prelude + "static int Get() => 1; static void M() { " + body + " } }";
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }
}
