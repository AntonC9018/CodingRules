using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class StatementOperationNullableSuppressionTests
{
    [Theory]
    [InlineData("null", "\"target\"")]
    [InlineData("\"source\"", "null")]
    public async Task ConditionalNameofExceptionArgumentsKeepBindingAndRuntime(string source, string target)
    {
        var document = StatementOperationTestFixture.Document("""
            class C {
                public static string Run() {
                    string? from = SOURCE;
                    string? to = TARGET;
                    try { throw new System.ArgumentNullException(from == null ? nameof(from) : nameof(to)); }
                    catch (System.ArgumentNullException error) { return error.ParamName!; }
                }
            }
            """.Replace("SOURCE", source).Replace("TARGET", target));
        var before = await StatementOperationTestFixture.Run(document);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, action.EquivalenceKey!));
            Assert.Equal(before, await StatementOperationTestFixture.Run(changed));
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        }
    }

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
