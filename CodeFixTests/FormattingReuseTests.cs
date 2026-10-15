using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Roslynator.CSharp.Analysis;
using Roslynator.CSharp.CodeFixes;
using Roslynator.Formatting.CodeFixes.CSharp;
using Roslynator.Formatting.CSharp;
using Xunit;

namespace CodingRules;

public sealed class FormattingReuseTests
{
    private static readonly ImmutableArray<DiagnosticAnalyzer> Selected = ImmutableArray.Create<DiagnosticAnalyzer>(
        new AccessorListAnalyzer(), new UseBlockBodyOrExpressionBodyAnalyzer(), new LeadingBooleanOperatorAnalyzer());

    private static Document Document(string source, string? config = null)
    {
        var document = StatementOperationTestFixture.Document(source, config);
        var globalConfig = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CodingRules.globalconfig"));
        return document.Project.AddAnalyzerConfigDocument("CodingRules.globalconfig", SourceText.From(globalConfig),
            filePath: "/CodingRules.globalconfig").Project.GetDocument(document.Id)!;
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!.WithAnalyzers(Selected, document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0001");
        return diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToImmutableArray();
    }

    private static async Task<Document> Fix(Document document, Diagnostic diagnostic)
    {
        CodeFixProvider provider = diagnostic.Id switch
        {
            "RCS0042" => new AccessorListCodeFixProvider(),
            "RCS1016" => new UseBlockBodyOrExpressionBodyCodeFixProvider(),
            "CR0800" => new LeadingBooleanOperatorCodeFixProvider(),
            _ => throw new InvalidOperationException(diagnostic.Id),
        };
        Assert.NotNull(provider.GetType().GetCustomAttribute<ExportCodeFixProviderAttribute>());
        Assert.NotNull(provider.GetFixAllProvider());
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action), CancellationToken.None));
        var action = Assert.Single(actions);
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var operation = Assert.Single(operations.OfType<ApplyChangesOperation>());
        var changed = operation.ChangedSolution.GetDocument(document.Id)!;
        await StatementOperationTestFixture.Compiles(changed);
        return changed;
    }

    private static async Task<Document> FixAll(Document document)
    {
        for (var pass = 0; pass < 30; pass++)
        {
            var diagnostics = await Diagnostics(document);
            if (diagnostics.IsEmpty) return document;
            document = await Fix(document, diagnostics[0]);
        }
        throw new InvalidOperationException("Formatting fixes failed to converge.");
    }

    [Fact]
    public void EveryBundledConfigurableRuleIsExplicitlyConfigured()
    {
        Assert.Equal(new Version(4, 10, 0, 0), typeof(Compilation).Assembly.GetName().Version);
        var config = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CodingRules.globalconfig"));
        var assemblies = Selected.Take(2).Select(analyzer => analyzer.GetType().Assembly);
        foreach (var assembly in assemblies)
        {
            var analyzers = assembly.GetTypes().Where(type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type));
            foreach (var type in analyzers)
            {
                var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;
                foreach (var descriptor in analyzer.SupportedDiagnostics)
                {
                    if (descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable)) continue;
                    var severity = descriptor.Id is "RCS0042" or "RCS1016" ? "warning" : "none";
                    Assert.Contains($"dotnet_diagnostic.{descriptor.Id}.severity = {severity}\n", config);
                }
            }
        }
    }

    [Theory]
    [InlineData("&&")]
    [InlineData("||")]
    public async Task MovesOnlyExistingBooleanContinuationAndIsIdempotent(string op)
    {
        var source = $"class C {{ bool M(bool a, bool b) {{ return a {op}\n    b; }} }}";
        var document = Document(source);
        var diagnostic = Assert.Single(await Diagnostics(document));
        Assert.Equal("CR0800", diagnostic.Id);
        document = await Fix(document, diagnostic);
        Assert.Contains($"a\n    {op} b", (await document.GetTextAsync()).ToString());
        Assert.Empty(await Diagnostics(document));
        Assert.Equal((await document.GetTextAsync()).ToString(), (await (await FixAll(document)).GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData("&&", "\n", "    ")]
    [InlineData("||", "\n", "    ")]
    [InlineData("&&", "\r\n", "\t")]
    [InlineData("||", "\r\n", "\t")]
    public async Task BooleanFixRemovesWhitespaceBeforeLineBreak(string op, string lineBreak, string indentation)
    {
        var source = $"class C {{ bool M(bool a, bool b) {{ return a \t{op} \t{lineBreak}{indentation}b; }} }}";
        var document = Document(source);
        var diagnostic = Assert.Single(await Diagnostics(document));
        Assert.Equal("CR0800", diagnostic.Id);
        var changed = await Fix(document, diagnostic);
        var expected = $"class C {{ bool M(bool a, bool b) {{ return a{lineBreak}{indentation}{op} b; }} }}";
        Assert.Equal(expected, (await changed.GetTextAsync()).ToString());
        Assert.Empty(await Diagnostics(changed));
    }

    [Theory]
    [InlineData("a && b")]
    [InlineData("a\n    && b")]
    [InlineData("a && // keep comment\n    b")]
    [InlineData("a /* keep comment */ &&\n    b")]
    [InlineData("a &&\n#if DEBUG\n    b\n#else\n    b\n#endif\n")]
    public async Task LeavesAcceptedOrUnsafeBooleanTrivia(string expression)
    {
        var document = Document($"class C {{ bool M(bool a, bool b) => {expression}; }}");
        var diagnostics = await Diagnostics(document);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CR0800");
    }

    [Fact]
    public async Task LeavesArithmeticAndSingleLineBodiesAlone()
    {
        var document = Document("""
            class C
            {
                int M(int a, int b) => a +
                    b;
                int Simple() => 3;
                int P => 3;
                int A { get => 3; }
                int Local() { int Value() => 3; return Value(); }
                int Block() { return 3; }
            }
            """);
        var diagnostics = await Diagnostics(document);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CR0800");
        Assert.Single(diagnostics); // Only the multiline arrow, not the '+' placement.
        Assert.Equal("RCS1016", diagnostics[0].Id);
        var changed = await Fix(document, diagnostics[0]);
        Assert.Contains("a +\n", (await changed.GetTextAsync()).ToString());
        Assert.Empty(await Diagnostics(changed));
    }

    [Fact]
    public async Task CompactsGetInitAndRespectsCommentsDirectivesAndAttributes()
    {
        var document = Document("""
            class C
            {
                public int Value
                {
                    get;
                    init;
                }
                public int Comment
                {
                    get; // retain
                    init;
                }
                public int Directive
                {
            #if DEBUG
                    get;
            #else
                    get;
            #endif
                    init;
                }
                public int Attribute
                {
                    [System.Obsolete] get;
                    init;
                }
            }
            """);
        var diagnostic = Assert.Single(await Diagnostics(document));
        Assert.Equal("RCS0042", diagnostic.Id);
        var changed = await Fix(document, diagnostic);
        Assert.Contains("Value { get; init; }", (await changed.GetTextAsync()).ToString());
        Assert.Contains("get; // retain", (await changed.GetTextAsync()).ToString());
        Assert.Empty(await Diagnostics(changed));
    }

    [Fact]
    public async Task ConvertsMultilineMethodLocalPropertyAndAccessorWithComments()
    {
        var document = Document("""
            class C
            {
                int M() => System.Math.Abs(
                    -1); // method
                void V() => System.Console.WriteLine(
                    1);
                int P => System.Math.Abs(
                    -2);
                int A
                {
                    get => System.Math.Abs(
                        -3);
                    set => System.Console.WriteLine(
                        value);
                }
                int L()
                {
                    int Local() => System.Math.Abs(
                        /* argument */ -4);
                    return Local();
                }
            }
            """);
        var diagnostics = await Diagnostics(document);
        Assert.Equal(6, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("RCS1016", diagnostic.Id));
        var changed = await FixAll(document);
        var root = (await changed.GetSyntaxRootAsync())!;
        Assert.Empty(root.DescendantNodes().OfType<ArrowExpressionClauseSyntax>());
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("// method", text);
        Assert.Contains("/* argument */", text);
        Assert.Contains("return System.Math.Abs(", text);
        Assert.DoesNotContain("return System.Console.WriteLine", text);
        Assert.Empty(await Diagnostics(changed));
    }

    [Fact]
    public async Task LeavesDirectiveContainingArrowAndGeneratedCode()
    {
        var document = Document("""
            class C
            {
                int P => System.Math.Abs(
            #if DEBUG
                    -1
            #else
                    -2
            #endif
                );
            }
            """);
        Assert.Empty(await Diagnostics(document));
        var generated = Document("// <auto-generated/>\nclass C { bool M(bool a, bool b) => a &&\n    b; }");
        Assert.Empty(await Diagnostics(generated));
    }

    [Fact]
    public async Task BooleanFixPreservesCrLfTabsAndShortCircuitEffects()
    {
        var source = """
            public class C
            {
                public static string Run()
                {
                    string trace = "";
                    bool Check(bool value)
                    {
                        trace += value ? "T" : "F";
                        return value;
                    }
                    bool first = Check(false) &&
                        Check(true);
                    bool second = Check(true) ||
                        Check(false);
                    return trace;
                }
            }
            """;
        source = source.Replace("\n", "\r\n").Replace("    ", "\t");
        var original = Document(source);
        Assert.Equal(2, (await Diagnostics(original)).Length);
        var changed = await FixAll(original);
        Assert.Equal("FT", await StatementOperationTestFixture.Run(original));
        Assert.Equal("FT", await StatementOperationTestFixture.Run(changed));
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("Check(false)\r\n\t\t\t&& Check(true)", text);
        Assert.Contains("Check(true)\r\n\t\t\t|| Check(false)", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
    }

    [Fact]
    public async Task FormatterAndSecondFixPassConverge()
    {
        var document = Document("""
            class C
            {
                public int Value
                {
                    get;
                    init;
                }
                bool M(bool a, bool b) => a &&
                    b;
            }
            """);
        document = await FixAll(document);
        document = await Formatter.FormatAsync(document);
        Assert.Empty(await Diagnostics(document));
        var text = (await document.GetTextAsync()).ToString();
        var repeated = await Formatter.FormatAsync(await FixAll(document));
        Assert.Equal(text, (await repeated.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task ConsumerCanOverrideSeverityAndMustResolveConflictingFormatterOption()
    {
        const string source = "class C\n{\n    public int Value\n    {\n        get;\n        init;\n    }\n}";
        var conflicting = Document(source, "root = true\n[*]\ncsharp_preserve_single_line_blocks = false\n");
        var changed = await Fix(conflicting, Assert.Single(await Diagnostics(conflicting)));
        Assert.Equal("RCS0042", Assert.Single(await Diagnostics(changed)).Id);
        var supported = await FixAll(Document(source, "root = true\n[*]\ncsharp_preserve_single_line_blocks = true\n"));
        Assert.Empty(await Diagnostics(await Formatter.FormatAsync(supported)));
        var disabled = Document(source, "root = true\n[*]\ndotnet_diagnostic.RCS0042.severity = none\n");
        Assert.Empty(await Diagnostics(disabled));
    }
}
