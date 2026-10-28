using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class ReviewedCompoundArgumentTests
{
    [Theory]
    [InlineData("Make()")]
    [InlineData("Current")]
    [InlineData("new Counter(10)")]
    public async Task StructTemporaryRetainsReceiverAndArgumentEvaluationOrder(string receiver)
    {
        var source = """
            struct Counter(int value)
            {
                public int Read(int delta) { value += delta; return value; }
            }
            class C
            {
                static string trace = "";
                static Counter Make() { trace += "receiver;"; return new(10); }
                static Counter Current { get { trace += "property;"; return new(10); } }
                static int Input(int value) { trace += "argument;"; return value; }
                static int Use(int value) => value;
                public static string Run()
                {
                    trace = "";
                    var result = Use(RECEIVER.Read(Input(2)));
                    return result + ":" + trace;
                }
            }
            """.Replace("RECEIVER", receiver);
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var before = await StatementOperationTestFixture.Run(document);
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(
                document, action.EquivalenceKey!, diagnostic.Id));
            await StatementOperationTestFixture.Compiles(changed);
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
            Assert.Equal(before, await StatementOperationTestFixture.Run(changed));
        }
    }

    [Theory]
    [InlineData("local")]
    [InlineData("stored")]
    [InlineData("GetReference()")]
    [InlineData("GetReadOnlyReference()")]
    public async Task AliasedStructReceiversKeepTheirStorageDuringArgumentMutation(string receiver)
    {
        var source = """
            struct Counter(int value)
            {
                public int Value = value;
                public int Read(int delta) { Value += delta; return Value; }
            }
            class C
            {
                static Counter stored;
                static ref Counter GetReference() => ref stored;
                static ref readonly Counter GetReadOnlyReference() => ref stored;
                static int Use(int value) => value;
                public static string Run()
                {
                    stored = new(10);
                    var local = new Counter(10);
                    int Mutate(int delta)
                    {
                        stored.Value = 50;
                        local.Value = 50;
                        return delta;
                    }
                    var result = Use(RECEIVER.Read(Mutate(2)));
                    return result + ":" + stored.Value + ":" + local.Value;
                }
            }
            """.Replace("RECEIVER", receiver);
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var before = await StatementOperationTestFixture.Run(document);
        var initialActions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.Equal(StatementOperationCodeFixProvider.ExtractKey, Assert.Single(initialActions).EquivalenceKey);
        for (var pass = 0; pass < 8; pass++)
        {
            var diagnostics = await StatementOperationTestFixture.Diagnostics(document);
            if (diagnostics.IsEmpty) return;
            diagnostic = diagnostics[0];
            var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
            Assert.NotEmpty(actions);
            foreach (var action in actions)
            {
                var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(
                    document, action.EquivalenceKey!, diagnostic.Id));
                await StatementOperationTestFixture.Compiles(changed);
                Assert.Equal(before, await StatementOperationTestFixture.Run(changed));
            }
            document = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(
                document, actions[0].EquivalenceKey!, diagnostic.Id));
        }
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData(false, "done:0")]
    [InlineData(true, "missing:1")]
    public async Task ConditionalAssertionKeepsLookupInsideTheConditionalCall(bool debug, string expected)
    {
        var source = (debug ? "#define DEBUG\n" : "") + """
            using System.Collections.Generic;
            using System.Diagnostics;
            using System.Text.Json;
            class C
            {
                public static string Run()
                {
                    using var data = JsonDocument.Parse("{}");
                    var reads = 0;
                    JsonElement ReadRoot() { reads++; return data.RootElement; }
                    try
                    {
                        Debug.Assert(ReadRoot().GetProperty("fileCount").GetInt32() >= 1, "message");
                        return "done:" + reads;
                    }
                    catch (KeyNotFoundException)
                    {
                        return "missing:" + reads;
                    }
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.Equal(StatementOperationCodeFixProvider.ExtractKey, Assert.Single(actions).EquivalenceKey);
        var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(
            document, actions[0].EquivalenceKey!, diagnostic.Id));
        await StatementOperationTestFixture.Compiles(changed);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        Assert.Equal(expected, await StatementOperationTestFixture.Run(changed));
    }

    [Fact]
    public async Task JsonLookupComparisonHasNamedStepsAndPreservesItsResult()
    {
        const string source = """
            using System.Text.Json;
            class C
            {
                static string Check(bool value) => value.ToString();
                public static string Run()
                {
                    using var data = JsonDocument.Parse("{\"fileCount\":2}");
                    return Check(data.RootElement.GetProperty("fileCount").GetInt32() >= 1);
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        var diagnostics = await StatementOperationTestFixture.Diagnostics(document);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("CR0300", diagnostic.Id);
        var before = await StatementOperationTestFixture.Run(document);
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(
                document, action.EquivalenceKey!, diagnostic.Id));
            await StatementOperationTestFixture.Compiles(changed);
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
            Assert.Equal(before, await StatementOperationTestFixture.Run(changed));
        }
    }
}
