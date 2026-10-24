using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class StatementOperationNullableSuppressionTests
{
    [Theory]
    [InlineData("\"directory/file\"")]
    [InlineData("null")]
    public async Task SuppressedNullableCallHasSavedBehaviorPreservingActions(string input)
    {
        var document = StatementOperationTestFixture.Document("""
            class C
            {
                static string Use(string first, string second) => first + second;
                public static string Run()
                {
                    return Use("prefix:", System.IO.Path.GetDirectoryName(INPUT)!);
                }
            }
            """.Replace("INPUT", input));
        var original = await StatementOperationTestFixture.Run(document);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.Contains(actions, action => action.EquivalenceKey == StatementOperationCodeFixProvider.ExtractKey);
        foreach (var action in actions)
        {
            var changed = await StatementOperationTestFixture.Reparse(
                await StatementOperationTestFixture.Fix(document, action.EquivalenceKey!));
            Assert.Equal(original, await StatementOperationTestFixture.Run(changed));
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        }
    }
}
