using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class RepeatedFormattingAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    [Fact]
    public async Task ReportsPinnedCalendarShapeAndAcceptsReviewedSemanticDistinctions()
    {
        var findings = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RepeatedFormatting", "Findings.cs"));
        var accepted = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RepeatedFormatting", "Accepted.cs"));
        var diagnostics = await Analyze([findings, accepted]);
        var expected = findings.Split('\n').Select((line, index) => (line, index))
            .Where(item => item.line.Contains("// CR1300: "))
            .Select(item => (item.index, item.line.Split("// CR1300: ")[0]
                .IndexOf(item.line.Split("// CR1300: ")[1].Trim(), StringComparison.Ordinal)));
        Assert.Equal(7, diagnostics.Length);
        Assert.Equal(expected, diagnostics.Select(item =>
        {
            var position = item.Location.GetLineSpan().StartLinePosition;
            return (position.Line, position.Character);
        }));
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal("Test0.cs", diagnostic.Location.GetLineSpan().Path);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Contains("candidate", diagnostic.GetMessage());
            Assert.Contains("shared meaning", diagnostic.GetMessage());
            var first = Assert.Single(diagnostic.AdditionalLocations);
            Assert.True(first.SourceSpan.Start < diagnostic.Location.SourceSpan.Start);
        });
        Assert.EndsWith("docs/agent-coding-rules.md#review-duplication-by-meaning", RepeatedFormattingAnalyzer.Rule.HelpLinkUri);
    }

    [Theory]
    [InlineData("a.ToString(\"o\", p)", "b.ToString(\"o\", p)", 1)]
    [InlineData("a.ToString(\"o\", p)", "b.ToString(\"O\", p)", 0)]
    [InlineData("a.ToString(\"o\", p)", "b.ToString(\"o\", q)", 0)]
    [InlineData("a.ToString(\"o\", p)", "b.ToString(\"o\", null)", 0)]
    [InlineData("a.ToString(\"o\", p)", "b.ToString(format: \"o\", provider: p)", 1)]
    [InlineData("a.ToString(\"o\", p)", "b.ToString(provider: p, format: \"o\")", 1)]
    [InlineData("a.ToString(\"o\", p)", "b.ToString(Format, p)", 1)]
    [InlineData("a.ToString(\"o\", p)", "b.ToString(format, p)", 0)]
    [InlineData("a.ToString(\"o\", p)", "a.ToString(\"o\", p)", 1)]
    [InlineData("a.ToString(\"o\")", "b.ToString(\"o\")", 0)]
    [InlineData("a.ToString()", "b.ToString()", 0)]
    [InlineData("a.ToString(\"o\", InvariantCulture)", "b.ToString(\"o\", CultureInfo.InvariantCulture)", 1)]
    [InlineData("a.ToString(\"o\", CultureInfo.CurrentCulture)", "b.ToString(\"o\", CultureInfo.CurrentCulture)", 1)]
    [InlineData("a.ToString(\"o\", CultureInfo.InvariantCulture)", "b.ToString(\"o\", CultureInfo.CurrentUICulture)", 0)]
    public async Task ComparesResolvedFormatAndProvider(string first, string second, int count)
    {
        Assert.Equal(count, (await Analyze(["""
            using System;
            using System.Globalization;
            using static System.Globalization.CultureInfo;
            class C
            {
                const string Format = "o";
                void M(DateTime a, DateTime b, IFormatProvider p, IFormatProvider q, string format)
                {
            """ + $"_ = {first}; _ = {second};" + "} }"])).Length);
    }

    [Theory]
    [InlineData("p", "p", 1)]
    [InlineData("local", "local", 1)]
    [InlineData("provider", "this.provider", 1)]
    [InlineData("x.provider", "y.provider", 0)]
    [InlineData("x.provider", "x.provider", 1)]
    [InlineData("Provider", "Provider", 0)]
    [InlineData("GetProvider()", "GetProvider()", 0)]
    [InlineData("providers[0]", "providers[0]", 0)]
    public async Task OnlyMatchesKnownProviderSources(string first, string second, int count)
    {
        Assert.Equal(count, (await Analyze(["""
            using System;
            class C
            {
                IFormatProvider? provider;
                IFormatProvider? Provider => provider;
                IFormatProvider? GetProvider() => provider;
                void M(DateTime a, DateTime b, IFormatProvider p, C x, C y, IFormatProvider[] providers)
                {
                    var local = p;
            """ + $"_ = a.ToString(\"o\", {first}); _ = b.ToString(\"o\", {second});" + "} }"])).Length);
    }

    [Theory]
    [InlineData("a?.ToString(\"o\", null)", "b?.ToString(\"o\", null)", 1)]
    [InlineData("a?.ToString(\"o\", null)", "b.Value.ToString(\"o\", null)", 0)]
    [InlineData("x.Next?.Value?.ToString(\"o\", null)", "y.Next?.Value?.ToString(\"o\", null)", 1)]
    [InlineData("x.Next?.Value?.ToString(\"o\", null)", "b?.ToString(\"o\", null)", 0)]
    public async Task RequiresMatchingNullPropagationShape(string first, string second, int count)
    {
        Assert.Equal(count, (await Analyze(["""
            using System;
            class C
            {
                C? Next;
                DateTimeOffset? Value;
                void M(DateTimeOffset? a, DateTimeOffset? b, C x, C y)
                {
            """ + $"_ = {first}; _ = {second};" + "} }"])).Length);
    }

    [Fact]
    public async Task DoesNotCombineMethodsLocalFunctionsOrDeferredBodies()
    {
        Assert.Empty(await Analyze(["""
            using System;
            partial class C
            {
                string A(DateTime a) => a.ToString("o", null);
                string B(DateTime b) => b.ToString("o", null);
                void M(DateTime a)
                {
                    _ = a.ToString("o", null);
                    string Local() => a.ToString("o", null);
                    Func<string> lambda = () => a.ToString("o", null);
                    Func<string> anonymous = delegate { return a.ToString("o", null); };
                }
            }
            """, "partial class C { string Other(System.DateTime a) => a.ToString(\"o\", null); }"]));
    }

    [Fact]
    public async Task ReportsLocalFunctionMatchesWithinThatFunctionOnly()
    {
        var diagnostics = await Analyze(["""
            using System;
            class C
            {
                void M(DateTime a, DateTime b)
                {
                    _ = a.ToString("o", null);
                    void Local()
                    {
                        _ = a.ToString("o", null);
                        _ = b.ToString("o", null);
                    }
                }
            }
            """]);
        Assert.Equal(9, Assert.Single(diagnostics).Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public async Task DoesNotGuessFromCustomFormattingMethodsDynamicOrInvalidCode()
    {
        Assert.Empty(await Analyze(["""
            using System;
            class Custom { public string ToString(string format, IFormatProvider? provider) => format; }
            class C
            {
                void M(Custom a, Custom b, dynamic d, DateTime value)
                {
                    _ = a.ToString("o", null); _ = b.ToString("o", null);
                    _ = d.ToString("o", null); _ = d.ToString("o", null);
                    _ = value.ToString("o", Missing); _ = value.ToString("o", Missing);
                    _ = value.ToString("o", ); _ = value.ToString("o", );
                }
            }
            """], requireCompilation: false));
    }

    [Theory]
    [InlineData("Test.g.cs", "")]
    [InlineData("Test.cs", "// <auto-generated/>\n")]
    [InlineData("Test.cs", "[System.CodeDom.Compiler.GeneratedCode(\"test\", \"1\")]\n")]
    public async Task SkipsGeneratedCode(string path, string prefix)
    {
        Assert.Empty(await Analyze([prefix + "class C { void M(System.DateTime a) { _ = a.ToString(\"o\", null); _ = a.ToString(\"o\", null); } }"], path: path));
    }

    [Theory]
    [InlineData(ReportDiagnostic.Suppress, 0)]
    [InlineData(ReportDiagnostic.Error, 1)]
    public async Task HonorsSeverity(ReportDiagnostic severity, int count)
    {
        var diagnostics = await Analyze(["class C { void M(System.DateTime a) { _ = a.ToString(\"o\", null); _ = a.ToString(\"o\", null); } }"], severity: severity);
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public async Task SuppressionKeepsNeighboringCandidatesEnabled()
    {
        var diagnostics = await Analyze(["""
            class C
            {
                void M(System.DateTime a)
                {
                    _ = a.ToString("o", null);
            #pragma warning disable CR1300
                    _ = a.ToString("o", null);
            #pragma warning restore CR1300
                    _ = a.ToString("o", null);
                }
                [System.Diagnostics.CodeAnalysis.SuppressMessage("Readability", "CR1300", Justification = "Independent policies")]
                void Separate(System.DateTime a) { _ = a.ToString("o", null); _ = a.ToString("o", null); }
                void Neighbor(System.DateTime a) { _ = a.ToString("o", null); _ = a.ToString("o", null); }
            }
            """]);
        Assert.Equal(new[] { 8, 12 }, diagnostics.Select(item => item.Location.GetLineSpan().StartLinePosition.Line));
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string[] sources, string? path = null,
        ReportDiagnostic severity = ReportDiagnostic.Default, bool requireCompilation = true)
    {
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12), path: path ?? $"Test{index}.cs"));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable,
            specificDiagnosticOptions: ImmutableDictionary<string, ReportDiagnostic>.Empty
                .Add(DiagnosticIds.RepeatedFormattingCandidate, severity));
        var compilation = CSharpCompilation.Create("RepeatedFormattingTests", trees, References, options);
        if (requireCompilation) Assert.Empty(compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error));
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new RepeatedFormattingAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        Assert.All(diagnostics, diagnostic => Assert.Equal(DiagnosticIds.RepeatedFormattingCandidate, diagnostic.Id));
        return diagnostics.OrderBy(item => item.Location.SourceTree!.FilePath).ThenBy(item => item.Location.SourceSpan.Start).ToImmutableArray();
    }
}
