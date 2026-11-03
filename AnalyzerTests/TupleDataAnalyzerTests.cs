using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class TupleDataAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    [Fact]
    public async Task ReportsAllSettledCasesAndAcceptsIteratorLocalsAndNamedRecords()
    {
        var sources = new[] { Fixture("Findings.cs"), Fixture("Accepted.cs") };
        var diagnostics = await Analyze(sources);
        Assert.Equal(6, diagnostics.Length);
        var expectedLines = sources[0].Split('\n').Select((line, index) => (line, index))
            .Where(item => item.line.Contains("// CR0900")).Select(item => item.index).ToArray();
        Assert.Equal(expectedLines, diagnostics.Select(item => item.Location.GetLineSpan().StartLinePosition.Line));
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticIds.NamedTupleData, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("Test0.cs", diagnostic.Location.GetLineSpan().Path);
            Assert.Contains("named record struct", diagnostic.GetMessage());
        });
        Assert.Equal(new[]
        {
            "plan",
            "(string Id, string Action, string? CalendarId, int? DesiredEventIndex)",
            "IEnumerable<(string Id, string Action, string? CalendarId, int? DesiredEventIndex)>",
            "ReadOnlySpan<(string From, string To)>",
            "(int? cycle, QualificationType? qualificationType)",
            "IEnumerable<(T Item, FilteredSchedule Schedule)>",
        }, diagnostics.Select(item => item.Location.SourceTree!.GetText().ToString(item.Location.SourceSpan)));
    }

    [Theory]
    [InlineData("private (int, int) M() { return (1, 2); }", "(int, int)")]
    [InlineData("public System.Collections.Generic.IEnumerable<(T, T)> M<T>() { yield return default; }", "System.Collections.Generic.IEnumerable<(T, T)>")]
    [InlineData("public (int, int)[] M() { return []; }", "(int, int)[]")]
    [InlineData("public void M((int, int) input) { }", "(int, int)")]
    [InlineData("public void M() { void Local((int, int) input) { } Local((1, 2)); }", "(int, int)")]
    [InlineData("public void M(System.Collections.Generic.List<(int, int)> input) { }", "System.Collections.Generic.List<(int, int)>")]
    [InlineData("private (int, int)[] table = [];", "table")]
    [InlineData("private System.Collections.Generic.List<(int, int)> table = new();", "table")]
    [InlineData("public (int, int)[] Table { get; } = [];", "(int, int)[]")]
    [InlineData("public void M() { var table = new[] { (1, 2) }; }", "table")]
    [InlineData("public void M() { (int, int)[] first = [], second = []; }", "first", "second")]
    [InlineData("public void M() { System.ReadOnlySpan<(int, int)> table = new (int, int)[] { (1, 2) }; }", "table")]
    public async Task GeneralizesOnlyDeclaredResultsInputsAndCataloguedTupleElements(string member, params string[] expected)
    {
        var diagnostics = await Analyze(["class C { " + member + " }"]);
        Assert.Equal(expected, diagnostics.Select(item => item.Location.SourceTree!.GetText().ToString(item.Location.SourceSpan)));
    }

    [Fact]
    public async Task ResolvesAliasesAndIgnoresLookalikeCollectionNames()
    {
        var diagnostics = await Analyze(["""
            using Table = System.Collections.Generic.List<(int, int)>;
            class List<T> { }
            class C
            {
                public void M()
                {
                    var table = new Table();
                    var other = new List<(int, int)>();
                }
            }
            """]);
        Assert.Equal("table", Assert.Single(diagnostics).Location.SourceTree!.GetText().ToString(diagnostics[0].Location.SourceSpan));
    }

    [Fact]
    public async Task DoesNotBroadenToUncataloguedWrappersOrUnsettledLocalData()
    {
        Assert.Empty(await Analyze(["""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            class C
            {
                public Task<(int, int)> AsyncResult() { return Task.FromResult((1, 2)); }
                public void M()
                {
                    (int Left, int Right) pair = (1, 2);
                    (pair.Left, pair.Right) = (pair.Right, pair.Left);
                    var (left, right) = pair;
                    var query = Enumerable.Range(0, 2).Select(value => (value, value));
                    IEnumerable<(int, int)> Local() { yield return (1, 2); }
                    var cells = Local();
                    Func<(int, int), int> selector = input => input.Item1;
                    var nested = new List<List<(int, int)>>();
                }
            }
            """]));
    }

    [Fact]
    public async Task CollectionInsideLocalFunctionIsIndependentOfItsResultException()
    {
        var diagnostics = await Analyze(["""
            using System.Collections.Generic;
            class C
            {
                public void M()
                {
                    (int, int) Local()
                    {
                        var plan = new List<(int, int)>();
                        return (1, 2);
                    }
                    _ = Local();
                }
            }
            """]);
        Assert.Equal("plan", Assert.Single(diagnostics).Location.SourceTree!.GetText().ToString(diagnostics[0].Location.SourceSpan));
    }

    [Theory]
    [InlineData("Test.g.cs", "")]
    [InlineData("Test.cs", "// <auto-generated/>\n")]
    [InlineData("Test.cs", "[System.CodeDom.Compiler.GeneratedCode(\"test\", \"1\")]\n")]
    public async Task SkipsGeneratedCode(string path, string prefix)
    {
        Assert.Empty(await Analyze([prefix + "class C { public (int, int) M() { return (1, 2); } }"], path: path));
    }

    [Theory]
    [InlineData(ReportDiagnostic.Suppress, 0)]
    [InlineData(ReportDiagnostic.Error, 1)]
    public async Task HonorsSeverityConfiguration(ReportDiagnostic severity, int count)
    {
        var diagnostics = await Analyze(["class C { public (int, int) M() { return (1, 2); } }"], severity: severity);
        Assert.Equal(count, diagnostics.Length);
        if (count > 0) Assert.Equal(DiagnosticSeverity.Error, diagnostics[0].Severity);
    }

    [Fact]
    public async Task HonorsScopedPragmaAndSuppressMessageWithoutHidingNeighbors()
    {
        var diagnostics = await Analyze(["""
            class C
            {
            #pragma warning disable CR0900
                public (int, int) First() { return (1, 2); }
            #pragma warning restore CR0900
                [System.Diagnostics.CodeAnalysis.SuppressMessage("Readability", "CR0900", Justification = "External consumer migration pending")]
                public (int, int) Second() { return (1, 2); }
                public (int, int) Neighbor() { return (1, 2); }
            }
            """]);
        Assert.Equal(7, Assert.Single(diagnostics).Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public async Task IgnoresUnresolvedTupleElementTypes()
    {
        Assert.Empty(await Analyze(["class C { private (Missing, int) M() { return default; } }"], requireCompilation: false));
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TupleData", name));

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string[] sources, string? path = null,
        ReportDiagnostic severity = ReportDiagnostic.Default, bool requireCompilation = true)
    {
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12), path: path ?? $"Test{index}.cs"));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable,
            specificDiagnosticOptions: ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(DiagnosticIds.NamedTupleData, severity));
        var compilation = CSharpCompilation.Create("TupleTests", trees, References, options);
        if (requireCompilation) Assert.Empty(compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error));
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TupleDataAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        Assert.All(diagnostics, item => Assert.Equal(DiagnosticIds.NamedTupleData, item.Id));
        return diagnostics.OrderBy(item => item.Location.SourceTree!.FilePath).ThenBy(item => item.Location.SourceSpan.Start).ToImmutableArray();
    }
}
