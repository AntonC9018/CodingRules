using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

/// <summary>
/// Expression trees are translated by a provider and must stay inline, so the
/// statement-operation, condition, chain-lambda and UTF-8 rules ignore them.
/// The same bodies as delegates prove that each rule would otherwise report.
/// </summary>
public sealed class ExpressionTreeExclusionTests
{
    private static readonly ImmutableArray<MetadataReference> References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    [Fact]
    public async Task DelegateBodiesAreReported()
    {
        var ids = (await Analyze(Source(tree: false))).Select(diagnostic => diagnostic.Id).Order();
        Assert.Equal(
            new[]
            {
                DiagnosticIds.MixedConditionOperators, DiagnosticIds.NestedArgumentOperation,
                DiagnosticIds.NestedArgumentOperation, DiagnosticIds.NestedChainLambda, DiagnosticIds.Utf8StringLiteral,
            }.Order(),
            ids);
    }

    [Fact]
    public async Task ExpressionTreeLambdasAndQueriesAreIgnored()
    {
        Assert.Empty(await Analyze(Source(tree: true)));
    }

    private static string Source(bool tree)
    {
        string Wrap(string type) => tree ? $"Expression<{type}>" : type;
        var query = tree ? "items" : "items.AsEnumerable()";
        return $$"""
            using System;
            using System.Linq;
            using System.Linq.Expressions;
            using System.Text;
            class Item { public byte[] Bytes = new byte[0]; public int A; public int B; public int C; }
            static class C
            {
                static int Get(int value) => value;
                static int Use(int value) => value;
                static object M(IQueryable<Item> items)
                {
                    {{Wrap("Func<Item, bool>")}} condition = item => item.A > 0 && item.B > 0 || item.C > 0 && item.A < 9;
                    {{Wrap("Func<Item, int>")}} nested = item => Use(Get(Get(item.A)));
                    {{Wrap("Func<int[][], object>")}} chain = rows => rows.Select(row => row.Select(cell => cell.ToString()));
                    {{Wrap("Func<int>")}} bytes = () => Encoding.UTF8.GetBytes("abc").Length;
                    var selected = from item in {{query}} select Use(Get(Get(item.B)));
                    return new object[] { condition, nested, chain, bytes, selected };
                }
            }
            """;
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), path: "Test.cs");
        var compilation = CSharpCompilation.Create("ExpressionTreeTests", [tree], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error));
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new StatementOperationAnalyzer(),
            new InlineConditionAnalyzer(), new NestedChainLambdaAnalyzer(), new Utf8LiteralAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }
}
