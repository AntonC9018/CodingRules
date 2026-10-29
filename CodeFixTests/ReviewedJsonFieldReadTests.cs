using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class ReviewedJsonFieldReadTests
{
    [Theory]
    [InlineData("Read(data.RootElement.GetProperty(\"command\").GetString())")]
    [InlineData("ReadInt(data.RootElement.GetProperty(\"fileCount\").GetInt32())")]
    [InlineData("ReadInt(data.RootElement.GetProperty(\"items\").GetArrayLength())")]
    public async Task SimpleFieldReadsRemainAccepted(string call)
    {
        var document = StatementOperationTestFixture.Document(Source(call));
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("ReadBool(data.RootElement.GetProperty(\"fileCount\").GetInt32() >= 1)")]
    [InlineData("Read(data.RootElement.GetProperty(Name(1)).GetString())")]
    public async Task ComparisonsAndComputedSelectorsStillNeedNamedSteps(string call)
    {
        var document = StatementOperationTestFixture.Document(Source(call));
        await StatementOperationTestFixture.Compiles(document);
        Assert.Contains(await StatementOperationTestFixture.Diagnostics(document), diagnostic => diagnostic.Id == "CR0300");
    }

    private static string Source(string call) => """
        using System.Text.Json;
        class C
        {
            static void Read(string? value) { }
            static void ReadInt(int value) { }
            static void ReadBool(bool value) { }
            static string Name(int value) => "command";
            static void Run()
            {
                using var data = JsonDocument.Parse("{}");
                CALL;
            }
        }
        """.Replace("CALL", call);
}
