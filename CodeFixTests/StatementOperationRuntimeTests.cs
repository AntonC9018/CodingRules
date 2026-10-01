using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class StatementOperationRuntimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReceiverAndEarlierConversionPrecedeTheSelectedProducer(bool receiverThrows, bool conversionThrows)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            public class C
            {
                static readonly List<string> Events = new();
                class Input
                {
                    public static implicit operator int(Input value)
                    {
                        Events.Add("convert");
                        if ({{conversionThrows.ToString().ToLowerInvariant()}}) throw new Exception();
                        return 1;
                    }
                }
                static C Receiver()
                {
                    Events.Add("receiver");
                    if ({{receiverThrows.ToString().ToLowerInvariant()}}) throw new Exception();
                    return new C();
                }
                static int Produce(int value) { Events.Add("produce"); return value; }
                static int Later() { Events.Add("later"); return 3; }
                void Use(int first, int second, int third) { Events.Add("use"); }
                public static string Run()
                {
                    Events.Clear();
                    try { Receiver().Use(new Input(), Produce(2), Later()); }
                    catch { Events.Add("throw"); }
                    return string.Join(",", Events);
                }
            }
            """;
        await AssertRuntime(source, receiverThrows ? "receiver,throw" : conversionThrows ? "receiver,convert,throw" : "receiver,convert,produce,later,use");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LazyAndConditionalArmsEvaluateOnlyWhenReached(bool flag)
    {
        var source = $$"""
            using System.Collections.Generic;
            public class C
            {
                static List<string> Events = new();
                static int Get(int value) { Events.Add("get" + value); return value; }
                static int Use(int value) { Events.Add("use" + value); return value; }
                public static string Run()
                {
                    Events.Clear();
                    bool flag = {{flag.ToString().ToLowerInvariant()}};
                    int result = flag ? Use(Get(1)) : 0;
                    return result + ":" + string.Join(",", Events);
                }
            }
            """;
        await AssertRuntime(source, flag ? "1:get1,use1" : "0:");
    }

    [Theory]
    [InlineData("Target().Value = Use(Get(2));", "receiver,get2,use2,set2")]
    [InlineData("Target().array[Index()] = Use(Get(2));", "receiver,index,get2,use2")]
    public async Task AssignmentLocationsAreEvaluatedBeforeTheRhs(string statement, string expected)
    {
        var source = $$"""
            using System.Collections.Generic;
            public class C
            {
                static List<string> Events = new();
                public int[] array = new int[1];
                public int Value { set { Events.Add("set" + value); } }
                static C Target() { Events.Add("receiver"); return new C(); }
                static int Index() { Events.Add("index"); return 0; }
                static int Get(int value) { Events.Add("get" + value); return value; }
                static int Use(int value) { Events.Add("use" + value); return value; }
                public static string Run()
                {
                    Events.Clear();
                    {{statement}}
                    return string.Join(",", Events);
                }
            }
            """;
        await AssertRuntime(source, expected);
    }

    [Fact]
    public async Task EarlierValuesAreSnapshotsEvenWhenTheProducerChangesThem()
    {
        const string source = """
            public class C
            {
                static int current;
                static int Change(int value) { current = value; return value; }
                static int Use(int first, int second, int third)
                {
                    int firstPart = first * 100;
                    int secondPart = second * 10;
                    int combined = firstPart + secondPart;
                    return combined + third;
                }
                public static string Run()
                {
                    current = 1;
                    int result = Use(current, Change(2), current);
                    return result.ToString();
                }
            }
            """;
        await AssertRuntime(source, "122");
    }

    [Fact]
    public async Task SourceGroupingSurvivesSavingTheArithmeticFix()
    {
        const string source = """
            public class C
            {
                static int Use(int value) => value;
                public static string Run()
                {
                    int a = 1, b = 2, c = 3;
                    int result = Use((a + b) * c);
                    return result.ToString();
                }
            }
            """;
        await AssertRuntime(source, "9");
    }

    private static async Task AssertRuntime(string source, string expected)
    {
        foreach (var key in new[] { StatementOperationCodeFixProvider.ExtractKey, StatementOperationCodeFixProvider.ExpandKey })
        {
            var document = StatementOperationTestFixture.Document(source);
            Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
            var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
            var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
            Assert.Contains(actions, action => action.EquivalenceKey == key);
            var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, key));
            Assert.Equal(expected, await StatementOperationTestFixture.Run(changed));
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        }
    }
}
