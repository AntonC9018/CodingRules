using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class ReturnDecisionCallerSafetyTests
{
    [Theory]
    [InlineData("Ready")]
    [InlineData("stop ? true : Ready")]
    public async Task LaterCallerLineObservationKeepsReturnsUnchanged(string decision)
    {
        var source = """
            using System.Runtime.CompilerServices;
            class C
            {
                static bool Ready => true;
                static bool Pick(bool stop)
                {
                    if (stop) { return false; }
                    return DECISION;
                }
                static int Later() => Line();
                static int Line([CallerLineNumber] int line = 0) => line;
            }
            """.Replace("DECISION", decision);
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document, includeReturns: true));
    }

    [Fact]
    public async Task ImplicitIndexerCallerInformationKeepsTheReturnUnchanged()
    {
        const string source = """
            using System.Runtime.CompilerServices;
            class C
            {
                bool this[string key, [CallerLineNumber] int line = 0] => true;
                bool Pick(bool stop)
                {
                    if (stop) { return false; }
                    return this["ready"];
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document, includeReturns: true));
    }

    [Fact]
    public async Task ExplicitCallerLineValuesDoNotHideSafePropertyDecisions()
    {
        const string source = """
            using System.Runtime.CompilerServices;
            class C
            {
                static bool Ready => true;
                static bool Pick(bool stop)
                {
                    if (stop) { return false; }
                    return Ready;
                }
                static int Later() => Line(41);
                static int Line([CallerLineNumber] int line = 0) => line;
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document, includeReturns: true));
        Assert.Equal("CR0001", diagnostic.Id);
    }
}
