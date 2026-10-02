using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class StatementOperationClassificationTests
{
    private const string Prelude = "using System; using System.Linq; class C { static int Use(int value) => value; static string Use(string value) => value; static int Get(int value = 1) => value; static int Collect(params int[] values) => 1; ";

    [Theory]
    [InlineData("string.Join(\",\", values.Select(x => x))", "Enumerable", "")]
    [InlineData("string.Join(\",\", values.AsQueryable().Select(x => x))", "Queryable", "")]
    [InlineData("string.Join(\",\", System.Linq.Enumerable.Select(values, x => x))", "Enumerable", "")]
    [InlineData("string.Join(\",\", System.Linq.Enumerable.Select(values, x => x))", "Enumerable", "namespace System.Linq { static class Enumerable { public static System.Collections.Generic.IEnumerable<int> Select(int[] values, System.Func<int, int> selector) => values; } }")]
    [InlineData("string.Join(\",\", System.Linq.Queryable.Select(values, x => x))", "Queryable", "namespace System.Linq { static class Queryable { public static System.Collections.Generic.IEnumerable<int> Select(int[] values, System.Func<int, int> selector) => values; } }")]
    public async Task NetstandardFrameworkPipelinesAreAllowedButSourceImitationsStillWarn(string expression, string type, string imitation)
    {
        var source = "using System.Linq; class C { static string M(int[] values) => " + expression + "; } " + imitation;
        var tree = CSharpSyntaxTree.ParseText(source);
        var reference = MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "References", "netstandard.dll"));
        var compilation = CSharpCompilation.Create("PipelineTest", new[] { tree }, new[] { reference },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = compilation.GetSemanticModel(tree);
        var select = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single(call => call.Expression.ToString().EndsWith("Select", StringComparison.Ordinal));
        var symbol = Assert.IsAssignableFrom<IMethodSymbol>(model.GetSymbolInfo(select).Symbol);
        Assert.Equal(type, symbol.ContainingType.Name);
        Assert.Equal(imitation.Length == 0 ? "netstandard" : "PipelineTest", symbol.ContainingAssembly.Name);
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new StatementOperationAnalyzer())).GetAnalyzerDiagnosticsAsync();
        if (imitation.Length == 0) Assert.Empty(diagnostics);
        else Assert.Equal("CR0300", Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData("const int Value = 1 + 2 + 3;", "return Value;")]
    [InlineData("", "const int constant = (1 + 2) + 3; return constant;")]
    [InlineData("", "switch (value) { case (1 + 2) + 3: return 1; default: return 0; }")]
    [InlineData("", "return value switch { 1 + 2 + 3 => 1, _ => 0 };")]
    [InlineData("", "return value is (1 + 2 + 3) ? 1 : 0;")]
    [InlineData("", "return value is not (1 + 2 + 3) ? 1 : 0;")]
    [InlineData("", "return value is >= (1 + 2 + 3) and < (4 + 5 + 6) ? 1 : 0;")]
    public async Task ConstantRequiredOperandsAreSilentAndRuntimeNeighborsStillFix(string member, string body)
    {
        var source = "class C { " + member + " static int Constant(int value) { " + body
            + " } static int Runtime() { var value = 1 + 2 + 3; return value; } }";
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        Assert.Equal("CR0302", diagnostic.Id);
        var root = await document.GetSyntaxRootAsync();
        Assert.Equal("Runtime", root!.FindNode(diagnostic.Location.SourceSpan).Ancestors()
            .OfType<MethodDeclarationSyntax>().First().Identifier.Text);
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.Equal(2, actions.Count);
        foreach (var action in actions)
        {
            var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, action.EquivalenceKey!));
            await StatementOperationTestFixture.Compiles(changed);
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        }
    }

    [Fact]
    public async Task RuntimePatternGuardRemainsFixable()
    {
        var document = StatementOperationTestFixture.Document(Prelude
            + "static int M(object value) => value switch { int x when Use(Get(x)) > 0 => x, _ => 0 }; }");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        Assert.Equal("CR0300", diagnostic.Id);
        var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document,
            StatementOperationCodeFixProvider.ExtractKey));
        await StatementOperationTestFixture.Compiles(changed);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
    }

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
