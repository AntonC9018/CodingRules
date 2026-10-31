using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class NestedChainLambdaAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    [Theory]
    [InlineData("rows.Select(row => row.Where(cell => cell > 0).Count())", "row =>")]
    [InlineData("rows.Select(row => Apply(row, cell => cell + 1))", "row =>")]
    [InlineData("rows.Where(row => row.Length > 0).Select(row => row.Select(cell => cell.ToString()))", "row => row.Select")]
    [InlineData("rows.Select(row => rows.Select(other => other.Select(cell => cell)))", "row =>")]
    [InlineData("rows.Select<int[], Func<int, int>>(row => cell => cell + row.Length)", "row =>")]
    public async Task ChainLambdaWithAnotherLambdaInItsExpressionBodyIsReported(string expression, string anchor)
    {
        var source = Source(expression);
        var diagnostic = Assert.Single(await Analyze(source));
        Assert.Equal(DiagnosticIds.NestedChainLambda, diagnostic.Id);
        Assert.Equal(source.IndexOf(anchor, StringComparison.Ordinal), diagnostic.Location.SourceSpan.Start);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Theory]
    [InlineData("rows.Where(row => row.Length > 0).Select(row => row.Length).ToList()")]
    [InlineData("rows.Select(row => { var positive = row.Where(cell => cell > 0); return positive.Count(); })")]
    [InlineData("rows.Any(row => row.Any(cell => cell > 0))")]
    [InlineData("Apply(rows, row => row.Select(cell => cell).ToArray())")]
    [InlineData("rows.Select(row => Count(row))")]
    public async Task FlatChainsBlocksPredicateNestingAndPlainCallsAreAccepted(string expression)
    {
        Assert.Empty(await Analyze(Source(expression)));
    }

    private static string Source(string expression) => """
        using System;
        using System.Linq;
        static class C
        {
            static T Apply<T>(T value, Func<T, T> change) => value;
            static int[] Apply(int[] value, Func<int, int> change) => value;
            static int Count(int[] row) => row.Length;
            static object M(int[][] rows) => EXPRESSION;
        }
        """.Replace("EXPRESSION", expression);

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), path: "Test.cs");
        var compilation = CSharpCompilation.Create("NestedChainLambdaTests", [tree], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Assert.Empty(compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error));
        return await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new NestedChainLambdaAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}
