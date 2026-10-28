using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class ReviewedAliasExpansionTests
{
    [Fact]
    public async Task ReadOnlyCapturedClosureKeepsUnchangedArgumentsAsOriginalIdentifiers()
    {
        const string source = """
            using System;
            class C
            {
                static string Use(string reporter, string course, string[] groups, Func<int> inspect, int value) =>
                    reporter + ":" + course + ":" + groups[0] + ":" + inspect() + ":" + value;
                public static string Run()
                {
                    string reporter = "reporter";
                    string course = "course";
                    string[] groups = ["group"];
                    Func<int> inspect = () => reporter.Length + course.Length + groups.Length;
                    return Use(reporter, course, groups, inspect, int.Parse("7"));
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        var before = await StatementOperationTestFixture.Run(document);
        var changed = await StatementOperationTestFixture.Fix(document, StatementOperationCodeFixProvider.ExpandKey);
        var changedText = (await changed.GetTextAsync()).ToString();

        Assert.Contains("Use(reporter, course, groups, inspect,", changedText);
        Assert.Equal(before, await StatementOperationTestFixture.Run(await StatementOperationTestFixture.Reparse(changed)));
    }

    [Fact]
    public async Task ExpansionSnapshotsLocalWrittenByCallableBetweenArguments()
    {
        const string source = """
            class C
            {
                static string Use(string first, string second) => first + ":" + second;
                public static string Run()
                {
                    string value = "before";
                    string Mutate() { value = "after"; return value; }
                    return Use(value, Mutate());
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        var before = await StatementOperationTestFixture.Run(document);
        var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(
            document, StatementOperationCodeFixProvider.ExpandKey));

        Assert.Equal("before:after", before);
        Assert.Equal(before, await StatementOperationTestFixture.Run(changed));
    }

    [Fact]
    public async Task ExpansionSnapshotsStructReceiverWrittenByCallableBetweenArguments()
    {
        const string source = """
            struct Counter
            {
                public int Value;
                public Counter(int value) => Value = value;
                public int Read(int delta) { Value += delta; return Value; }
            }
            class C
            {
                static int Use(int value) => value;
                public static string Run()
                {
                    var counter = new Counter(10);
                    int Mutate() { counter.Value = 50; return 2; }
                    var result = Use(counter.Read(Mutate()));
                    return result + ":" + counter.Value;
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        var before = await StatementOperationTestFixture.Run(document);
        var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(
            document, StatementOperationCodeFixProvider.ExpandKey));

        Assert.Equal("12:50", before);
        Assert.Equal(before, await StatementOperationTestFixture.Run(changed));
    }
}
