using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class ReviewedCompoundAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    [Fact]
    public async Task SharedConsumerFixturesHaveExactFindingsAndAcceptedCases()
    {
        var findings = Fixture("Findings.cs");
        var diagnostics = await Analyze(findings, Fixture("Accepted.cs"));
        var expected = findings.Split('\n').Select((line, number) => (line, number))
            .Select(item => (match: Regex.Match(item.line, "// (CR[0-9]+): (.+)$"), item.line, item.number))
            .Where(item => item.match.Success)
            .Select(item => (item.match.Groups[1].Value, item.number,
                item.line.IndexOf(item.match.Groups[2].Value, StringComparison.Ordinal))).ToArray();
        Assert.Equal(6, expected.Length);
        Assert.Equal(expected, diagnostics.Select(d => (d.Id, d.Location.GetLineSpan().StartLinePosition.Line,
            d.Location.GetLineSpan().StartLinePosition.Character)));
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    [Theory]
    [InlineData(ReportDiagnostic.Suppress)]
    [InlineData(ReportDiagnostic.Error)]
    public async Task SharedFixturesHonorSeverity(ReportDiagnostic severity)
    {
        var diagnostics = await Analyze(Fixture("Findings.cs"), Fixture("Accepted.cs"), severity);
        Assert.Equal(severity == ReportDiagnostic.Suppress ? 0 : 6, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    private static string Fixture(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ReviewedConditions", file));

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string findings, string accepted,
        ReportDiagnostic severity = ReportDiagnostic.Default)
    {
        var trees = new[] { findings, accepted }.Select((source, index) => CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12), path: $"Test{index}.cs"));
        var rules = new[] { "CR0200", "CR0201", "CR0202", "CR0203", "CR0204" };
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable,
            specificDiagnosticOptions: rules.ToImmutableDictionary(id => id, _ => severity));
        var compilation = CSharpCompilation.Create("ReviewedTests", trees, References, options);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new InlineConditionAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        return diagnostics.OrderBy(d => d.Location.SourceSpan.Start).ToImmutableArray();
    }
}
