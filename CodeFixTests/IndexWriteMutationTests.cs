using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class IndexWriteMutationTests
{
    [Theory]
    [InlineData("values[index++] = item;", "")]
    [InlineData("buffer.Values[index++] = item;", "var buffer = new Buffer { Values = values };")]
    public async Task IncrementConsumedByAnIndexWriteIsReported(string statement, string setup)
    {
        var source = "class Buffer { public int[] Values = new int[1]; } class C { "
            + "static void Write(int[] values, int item) { int index = 0; "
            + setup + statement + " } }";
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);

        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));

        Assert.Equal("CR0302", diagnostic.Id);
    }

    [Theory]
    [InlineData("values[index] = item; index++;", "")]
    [InlineData("buffer.Values[index] = item; index++;", "var buffer = new Buffer { Values = values };")]
    public async Task SeparateIndexWriteAndIncrementAreAccepted(string statement, string setup)
    {
        var source = "class Buffer { public int[] Values = new int[1]; } class C { "
            + "static void Write(int[] values, int item) { int index = 0; "
            + setup + statement + " } }";
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);

        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }
}
