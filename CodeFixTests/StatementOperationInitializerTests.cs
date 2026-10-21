using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class StatementOperationInitializerTests
{
    private const string Prelude = """
        using System;
        class C
        {
            public int Value { get; set; }
            public int[] WeeklyLessons { get; set; } = [];
            public int[] Groups { get; set; } = [];
            public TimeSpan LessonDuration { get; set; }
            public int[] TimeSlotStarts { get; set; } = [];
            public C Child { get; set; } = null!;
            static int[] CreateDefaultTimeSlots() => [];
            static int Get(int value) => value;
            static int Use(int value) => value;
        """;

    [Theory]
    [InlineData("new C { WeeklyLessons = [], Groups = [] }")]
    [InlineData("new C { Value = 42 }")]
    [InlineData("new C { TimeSlotStarts = CreateDefaultTimeSlots() }")]
    [InlineData("new C { LessonDuration = TimeSpan.FromMinutes(90), TimeSlotStarts = CreateDefaultTimeSlots() }")]
    [InlineData("new C { Child = new C { Value = 42, Groups = [] } }")]
    [InlineData("new C { Child = { Value = 42, Groups = [] } }")]
    [InlineData("new() { WeeklyLessons = [], Groups = [] }")]
    public async Task DirectObjectInitializerValuesRemainAllowed(string expression)
    {
        var document = StatementOperationTestFixture.Document(Prelude
            + "static C M() { C result = " + expression + "; return result; } }");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("static C M() => new C { Value = 42 };")]
    [InlineData("static C M() { return new C { Value = 42 }; }")]
    [InlineData("static C instance = new C { Value = 42 };")]
    [InlineData("static C Instance { get; } = new C { Value = 42 };")]
    public async Task DirectObjectInitializersRemainAllowedAcrossHosts(string member)
    {
        var document = StatementOperationTestFixture.Document(Prelude + member + " }");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("new C { Value = Get(1) + 2 }", "CR0302")]
    [InlineData("new C { Value = 1 + 2 + 3 }", "CR0302")]
    [InlineData("new C { Value = Use(Get(1)) }", "CR0300")]
    [InlineData("new C { Child = new C { Value = Get(1) + 2 } }", "CR0302")]
    [InlineData("new C { Child = { Value = Get(1) + 2 } }", "CR0302")]
    public async Task CombinedInitializerRightHandSidesStillReport(string expression, string expectedId)
    {
        var document = StatementOperationTestFixture.Document(Prelude
            + "static C M(int value) { var result = " + expression + "; return result; } }");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        Assert.Equal(expectedId, diagnostic.Id);
    }

    [Theory]
    [InlineData("(value = 2) + 1")]
    [InlineData("value++ + 1")]
    public async Task ConsumedMutationsOutsideInitializersStillReportAndFix(string expression)
    {
        var document = StatementOperationTestFixture.Document(Prelude
            + "static int M(int value) => " + expression + "; }");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        Assert.Equal("CR0302", diagnostic.Id);
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var changed = await StatementOperationTestFixture.Reparse(
                await StatementOperationTestFixture.Fix(document, action.EquivalenceKey!));
            await StatementOperationTestFixture.Compiles(changed);
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        }
    }
}
