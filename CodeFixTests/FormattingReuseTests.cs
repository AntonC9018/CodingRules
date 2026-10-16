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
using Roslynator.Formatting.CodeFixes.CSharp;
using Roslynator.Formatting.CSharp;
using Xunit;

namespace CodingRules;

public sealed class FormattingReuseTests
{
    private static readonly ImmutableArray<DiagnosticAnalyzer> Selected = ImmutableArray.Create<DiagnosticAnalyzer>(
        new AccessorListAnalyzer(), new UseBlockBodyOrExpressionBodyAnalyzer(), new LeadingBooleanOperatorAnalyzer(),
        new UnsafeExpressionBodySuppressor());

    private static Document Document(string source, string? config = null)
    {
        var document = StatementOperationTestFixture.Document(source, config);
        var globalConfig = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CodingRules.globalconfig"));
        return document.Project.AddAnalyzerConfigDocument("CodingRules.globalconfig", SourceText.From(globalConfig),
            filePath: "/CodingRules.globalconfig").Project.GetDocument(document.Id)!;
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document, bool includeSafetySuppressor = true)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var analyzers = includeSafetySuppressor ? Selected
            : Selected.Where(analyzer => analyzer is not DiagnosticSuppressor).ToImmutableArray();
        var diagnostics = await compilation!.WithAnalyzers(analyzers, document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0001");
        return diagnostics.Where(diagnostic => !diagnostic.IsSuppressed)
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToImmutableArray();
    }

    private static async Task<Document> Fix(Document document, Diagnostic diagnostic)
    {
        CodeFixProvider provider = diagnostic.Id switch
        {
            "RCS0042" => new AccessorListCodeFixProvider(),
            "RCS1016" => new ExpressionBodyCodeFixProvider(),
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

    private static async Task NoExpressionBodyFix(Document document, IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            var actions = new List<CodeAction>();
            await new ExpressionBodyCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic,
                (action, _) => actions.Add(action), CancellationToken.None));
            Assert.Empty(actions);
        }
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

    [Theory]
    [InlineData("method", "/* preserve this explanation */ ")]
    [InlineData("method", "// preserve this explanation\n        ")]
    [InlineData("property", "/* preserve this explanation */ ")]
    [InlineData("property", "// preserve this explanation\n        ")]
    [InlineData("accessor", "/* preserve this explanation */ ")]
    [InlineData("accessor", "// preserve this explanation\n        ")]
    public async Task LeavesArrowCommentsExactlyIntact(string host, string comment)
    {
        var expression = $"=> {comment}System.Math.Abs(\n        -1);";
        var member = host switch
        {
            "method" => $"int M() {expression}",
            "property" => $"int P {expression}",
            "accessor" => $"int P {{ get {expression} }}",
            _ => throw new InvalidOperationException(host),
        };
        var source = $"class C {{ {member} }}";
        var document = Document(source);
        await StatementOperationTestFixture.Compiles(document);
        var upstreamDiagnostics = await Diagnostics(document, includeSafetySuppressor: false);
        Assert.Equal("RCS1016", Assert.Single(upstreamDiagnostics).Id);
        await NoExpressionBodyFix(document, upstreamDiagnostics);
        // The bundled fixer loses arrow trivia: these bodies must stay excluded.
        Assert.Empty(await Diagnostics(document));
        var changed = await FixAll(document);
        Assert.Equal(source, (await changed.GetTextAsync()).ToString());
        Assert.Equal(source, (await (await FixAll(changed)).GetTextAsync()).ToString());
        await StatementOperationTestFixture.Compiles(changed);
    }

    [Fact]
    public async Task LeavesCustomAsyncMethodAndLocalFunctionCompilableAndIdempotent()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TaskLike.cs.txt"));
        var document = Document(source);
        await StatementOperationTestFixture.Compiles(document);
        var upstreamDiagnostics = await Diagnostics(document, includeSafetySuppressor: false);
        Assert.Equal(2, upstreamDiagnostics.Length);
        Assert.All(upstreamDiagnostics, diagnostic => Assert.Equal("RCS1016", diagnostic.Id));
        await NoExpressionBodyFix(document, upstreamDiagnostics);
        Assert.Empty(await Diagnostics(document));
        var changed = await FixAll(document);
        Assert.Equal(source, (await changed.GetTextAsync()).ToString());
        await StatementOperationTestFixture.Compiles(changed);
        var repeated = await FixAll(changed);
        Assert.Equal(source, (await repeated.GetTextAsync()).ToString());
        await StatementOperationTestFixture.Compiles(repeated);
    }

    [Theory]
    [InlineData("System.Threading.Tasks.Task<int>", "FromResult")]
    [InlineData("System.Threading.Tasks.ValueTask<int>", "FromResult")]
    [InlineData("System.Threading.Tasks.Task", "Delay")]
    [InlineData("System.Threading.Tasks.ValueTask", "Delay")]
    [InlineData("void", "Delay")]
    public async Task StillConvertsSupportedAsyncMethodsAndLocalFunctions(string returnType, string operation)
    {
        var expression = $"await System.Threading.Tasks.Task.{operation}(\n        1)";
        var document = Document($$"""
            class C
            {
                async {{returnType}} M() => {{expression}};
                void Host()
                {
                    async {{returnType}} Local() => {{expression}};
                }
            }
            """);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Equal(2, (await Diagnostics(document)).Length);
        var changed = await FixAll(document);
        Assert.Empty((await changed.GetSyntaxRootAsync())!.DescendantNodes().OfType<ArrowExpressionClauseSyntax>());
        Assert.Empty(await Diagnostics(changed));
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Equal(text, (await (await FixAll(changed)).GetTextAsync()).ToString());
    }

    [Fact]
    public async Task ConvertsOtherArrowHostsRefReturnsAndThrowsWithoutChangingCompilation()
    {
        var document = Document("""
            class C
            {
                int storage;
                public C() => System.Console.WriteLine(
                    1);
                ~C() => System.Console.WriteLine(
                    2);
                public int this[int index] => System.Math.Abs(
                    index);
                public static C operator +(C left, C right) => new C(
                    );
                public static implicit operator int(C value) => System.Math.Abs(
                    -1);
                public int Value
                {
                    get { return storage; }
                    init => System.Console.WriteLine(
                        value);
                }
                public event System.Action Changed
                {
                    add => System.Console.WriteLine(
                        value);
                    remove => System.Console.WriteLine(
                        value);
                }
                public ref int Reference() => ref (true
                    ? ref storage : ref storage);
                public int Throw() => throw new System.InvalidOperationException(
                    "message");
            }
            """);
        await StatementOperationTestFixture.Compiles(document);
        var diagnostics = await Diagnostics(document);
        Assert.Equal(10, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("RCS1016", diagnostic.Id));
        var changed = await FixAll(document);
        Assert.Empty((await changed.GetSyntaxRootAsync())!.DescendantNodes().OfType<ArrowExpressionClauseSyntax>());
        await StatementOperationTestFixture.Compiles(await StatementOperationTestFixture.Reparse(changed));
        Assert.Equal((await changed.GetTextAsync()).ToString(), (await (await FixAll(changed)).GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("RCS1016.UseBlockBody")]
    public async Task ExportedFixAllSkipsUnsafeDiagnosticsAndStillFixesSafeBodies(string? equivalenceKey)
    {
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TaskLike.cs.txt"));
        var document = Document(fixture + "\n" + """
            class C
            {
                int Unsafe() => /* preserve */ System.Math.Abs(
                    -1);
                int Safe() => System.Math.Abs(
                    -2);
            }
            """);
        await StatementOperationTestFixture.Compiles(document);
        var upstreamDiagnostics = await Diagnostics(document, includeSafetySuppressor: false);
        Assert.Equal(4, upstreamDiagnostics.Length);
        Assert.Single(await Diagnostics(document));
        var changed = await ExportedExpressionFixAll(document, equivalenceKey);
        var text = (await changed.GetTextAsync()).ToString();
        Assert.StartsWith(fixture, text);
        Assert.Contains("Unsafe() => /* preserve */", text);
        Assert.DoesNotContain("Safe() =>", text);
        Assert.Empty(await Diagnostics(changed));
        await StatementOperationTestFixture.Compiles(await StatementOperationTestFixture.Reparse(changed));
        var repeated = await ExportedExpressionFixAll(changed, equivalenceKey);
        Assert.Equal(text, (await repeated.GetTextAsync()).ToString());
    }

    private static async Task<Document> ExportedExpressionFixAll(Document document, string? equivalenceKey)
    {
        var provider = new ExpressionBodyCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Document, equivalenceKey,
            provider.FixableDiagnosticIds, new UnsuppressedExpressionDiagnostics(), CancellationToken.None);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        Assert.NotNull(action);
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var operation = Assert.Single(operations.OfType<ApplyChangesOperation>());
        return operation.ChangedSolution.GetDocument(document.Id)!;
    }

    private sealed class UnsuppressedExpressionDiagnostics : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document,
            CancellationToken cancellationToken) =>
            (await Diagnostics(document, includeSafetySuppressor: false)).Where(diagnostic => diagnostic.Id == "RCS1016");

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project,
            CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project,
            CancellationToken cancellationToken)
        {
            var diagnostics = new List<Diagnostic>();
            foreach (var document in project.Documents)
                diagnostics.AddRange(await GetDocumentDiagnosticsAsync(document, cancellationToken));
            return diagnostics;
        }
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
