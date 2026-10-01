using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class StatementOperationClassificationTests
{
    private const string Prelude = "using System; using System.Linq; class C { static int Use(int value) => value; static string Use(string value) => value; static int Get(int value = 1) => value; static int Collect(params int[] values) => 1; ";

    [Theory]
    [InlineData("Use(Get())")]
    [InlineData("Use(Get() + 1)")]
    [InlineData("Use(Collect())")]
    [InlineData("Use(1 + 2)")]
    [InlineData("Use(-1)")]
    [InlineData("Use(nameof(Get))")]
    [InlineData("Use(text.Trim())")]
    [InlineData("Use(text.ToString())")]
    [InlineData("Use(text.Length)")]
    [InlineData("Use(values[a + 1])")]
    [InlineData("string.Join(\",\", values.Select(x => Get(x)))")]
    public async Task DirectAndSimpleValuesRemainAllowed(string expression)
    {
        var document = StatementOperationTestFixture.Document(Prelude + "static void M(string text, int a, int[] values) { " + expression + "; } }");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("Use(Get(value: a))", "CR0300")]
    [InlineData("Use(Collect(a))", "CR0300")]
    [InlineData("Use(Collect(Array.Empty<int>()))", "CR0300")]
    [InlineData("Use(1 + 2 + 3)", "CR0300")]
    [InlineData("Use((int)number)", "CR0300")]
    [InlineData("Use($\"value:{a}\")", "CR0300")]
    [InlineData("Use(flag ? a : b)", "CR0301")]
    [InlineData("Use(a++)", "CR0300")]
    [InlineData("Use((a = b))", "CR0300")]
    public async Task SourceArgumentBoundaryIsMechanical(string expression, string id)
    {
        var source = Prelude + "static void M(int a, int b, double number, bool flag) { " + expression + "; } }";
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Equal(id, Assert.Single(await StatementOperationTestFixture.Diagnostics(document)).Id);
        var actions = await StatementOperationTestFixture.Actions(document, (await StatementOperationTestFixture.Diagnostics(document))[0]);
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, action.EquivalenceKey!));
            await StatementOperationTestFixture.Compiles(changed);
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        }
    }

    [Theory]
    [InlineData("var value = flag ? a : b;")]
    [InlineData("a = flag ? a : b;")]
    [InlineData("return flag ? a : b;")]
    public async Task WholeNamedOrDirectReturnedConditionalIsAllowed(string statement)
    {
        var source = "class C { static int M(bool flag, int a, int b) { " + statement + (statement.StartsWith("return") ? "" : "return a;") + " } }";
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(StatementOperationTestFixture.Document(source)));
    }

    [Theory]
    [InlineData("dotnet_diagnostic.CR0301.severity = none", "CR0300")]
    [InlineData("dotnet_diagnostic.CR0300.severity = none", "CR0301")]
    public async Task DisabledHigherReasonExposesEnabledReason(string options, string expected)
    {
        var document = StatementOperationTestFixture.Document(Prelude + "static int M(bool flag) => Use(flag ? 1 : 2); }",
            "root = true\n[*.cs]\n" + options);
        Assert.Equal(expected, Assert.Single(await StatementOperationTestFixture.Diagnostics(document)).Id);
    }

    [Theory]
    [InlineData("#pragma warning disable CR0302\n", "#pragma warning restore CR0302\n")]
    [InlineData("[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Readability\", \"CR0302\", Justification = \"Formula\")]\n", "")]
    public async Task StandardDriverSuppressionIsScopedToTheFormula(string before, string after)
    {
        var methods = "static int Formula(int a, int b, int c) => a + b + c;\n";
        var neighbor = "static int Neighbor(int a, int b, int c) => a + b + c;\n";
        var baseline = StatementOperationTestFixture.Document("class C {\n" + methods + neighbor + "}");
        Assert.Equal(2, (await StatementOperationTestFixture.Diagnostics(baseline)).Length);
        var document = StatementOperationTestFixture.Document("class C {\n" + before + methods + after + neighbor + "}");
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        Assert.Equal("CR0302", diagnostic.Id);
        Assert.Contains("Neighbor", (await document.GetTextAsync()).Lines[diagnostic.Location.GetLineSpan().StartLinePosition.Line].ToString());
        var compilation = await document.Project.GetCompilationAsync();
        var options = new CompilationWithAnalyzersOptions(document.Project.AnalyzerOptions, null, true, false, reportSuppressedDiagnostics: true);
        var all = await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new StatementOperationAnalyzer()), options).GetAnalyzerDiagnosticsAsync();
        Assert.Equal(2, all.Length);
        Assert.Single(all.Where(item => item.IsSuppressed));
        Assert.Single(all.Where(item => !item.IsSuppressed));
    }
}
