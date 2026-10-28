using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class SingleConstructionArgumentTests
{
    [Theory]
    [InlineData("Use(new Item(1, [2, 3]))")]
    [InlineData("UseMany(new Item(1, [2, 3]))")]
    [InlineData("Use(item: new(1, [2, 3]))")]
    [InlineData("Use(item: new Item(1, [2, 3]), extra: 4)")]
    [InlineData("Use(item: new(1, [2, 3]), extra: 4)")]
    public async Task OneDirectConstructionArgumentAllowsOtherSimpleAndCollectionArguments(string expression)
    {
        var document = StatementOperationTestFixture.Document(Source(expression));
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("Use(new Item(1, [2]), new Item(2, [3]))", "CR0300,CR0300")]
    [InlineData("UseMany(new Item(1, [2]), new Item(2, [3]))", "CR0300,CR0300")]
    [InlineData("Use(first: new(1, [2]), second: new(2, [3]))", "CR0300,CR0300")]
    [InlineData("Use(new Pair(new Item(1, [2]), new Item(2, [3])))", "CR0300")]
    [InlineData("Use(new Item(Get(1), [2]))", "CR0300")]
    [InlineData("Use(new Item(flag ? 1 : 2, [3]))", "CR0301")]
    [InlineData("Use(new Item(1 + 2, [3]))", "CR0300")]
    [InlineData("Use(new Item(1, [2]), Get(3))", "CR0300")]
    public async Task RemainingComputationsHaveBehaviorPreservingSavedFixes(string expression, string expectedIds)
    {
        var document = StatementOperationTestFixture.Document(Source(expression));
        await StatementOperationTestFixture.Compiles(document);
        var expected = expectedIds.Split(',');
        Assert.Equal(expected, (await StatementOperationTestFixture.Diagnostics(document)).Select(diagnostic => diagnostic.Id));

        var behavior = await StatementOperationTestFixture.Run(document);
        for (var pass = 0; pass < 12; pass++)
        {
            var diagnostics = await StatementOperationTestFixture.Diagnostics(document);
            if (diagnostics.IsEmpty)
            {
                Assert.Equal(behavior, await StatementOperationTestFixture.Run(document));
                return;
            }

            var diagnostic = diagnostics[0];
            var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
            Assert.NotEmpty(actions);
            foreach (var action in actions)
                await FixAndConverge(document, action.EquivalenceKey!, diagnostic.Id, behavior);

            document = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(
                document, actions[0].EquivalenceKey!, diagnostic.Id));
        }

        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
        Assert.Equal(behavior, await StatementOperationTestFixture.Run(document));
    }

    private static async Task FixAndConverge(
        Microsoft.CodeAnalysis.Document document, string key, string initialId, string behavior)
    {
        for (var pass = 0; pass < 12; pass++)
        {
            var diagnostics = await StatementOperationTestFixture.Diagnostics(document);
            if (diagnostics.IsEmpty)
            {
                Assert.Equal(behavior, await StatementOperationTestFixture.Run(document));
                return;
            }

            var diagnostic = diagnostics[0];
            var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
            Assert.NotEmpty(actions);
            var actionKey = pass == 0 ? key : actions[0].EquivalenceKey!;
            document = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(
                document, actionKey, pass == 0 ? initialId : diagnostic.Id));
            await StatementOperationTestFixture.Compiles(document);
            Assert.Equal(behavior, await StatementOperationTestFixture.Run(document));
        }

        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
        Assert.Equal(behavior, await StatementOperationTestFixture.Run(document));
    }

    private static string Source(string expression) => """
        record Item(int Value, int[] Values);
        record Pair(Item Left, Item Right);
        class C
        {
            static int calls;
            static bool flag = true;
            static int Get(int value) { calls++; return value; }
            static string Use(Item item, int extra = 0) => $"{item.Value}:{item.Values.Length}:{extra}:{calls}";
            static string Use(Item first, Item second) => $"{first.Value}:{second.Value}:{calls}";
            static string Use(Pair pair) => $"{pair.Left.Value}:{pair.Right.Value}:{calls}";
            static string UseMany(params Item[] items) => items.Length.ToString();
            public static string Run()
            {
                calls = 0;
                return EXPRESSION;
            }
        }
        """.Replace("EXPRESSION", expression);
}
