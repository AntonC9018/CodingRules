using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class ReviewedRuntimeTypeAssertionTests
{
    [Theory]
    [InlineData("value.GetType().IsAssignableTo(typeof(string))")]
    [InlineData("value is null || value.GetType().IsAssignableTo(typeof(string))")]
    public async Task AcceptedRuntimeTypeAssertionsRemainInline(string condition)
    {
        var source = "using System.Diagnostics; class C { static void M(object value) { Debug.Assert("
            + condition + ", \"The value must support the contract.\"); } }";
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("Check(value.GetType().IsAssignableTo(typeof(string)))")]
    [InlineData("System.Diagnostics.Debug.Assert(GetValue().GetType().IsAssignableTo(typeof(string)), \"message\")")]
    [InlineData("System.Diagnostics.Debug.Assert(value.GetType().IsAssignableTo(GetContract(1)), \"message\")")]
    public async Task OtherComputedAssertionsRetainFindings(string call)
    {
        var document = StatementOperationTestFixture.Document("class C { static object GetValue() => new(); "
            + "static System.Type GetContract(int index) => typeof(string); static void Check(bool value) {} "
            + "static void M(object value) { " + call + "; } }");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Contains(await StatementOperationTestFixture.Diagnostics(document), diagnostic => diagnostic.Id == "CR0300");
    }
}
