using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class OverloadConfigurationAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    [Fact]
    public async Task ReportsReviewedOutputAlternativesAndAcceptsOneParameterAndUnrelatedShapes()
    {
        var sources = new[] { Fixture("Findings.cs"), Fixture("Accepted.cs") };
        var diagnostics = await Analyze(sources);
        Assert.Equal(14, diagnostics.Length);
        var expected = sources[0].Split('\n').Select((line, index) => (line, index))
            .Where(item => item.line.Contains("// CR1001: "))
            .Select(item => (item.index, item.line.IndexOf(item.line.Split("// CR1001: ")[1].Trim(), StringComparison.Ordinal)));
        Assert.Equal(expected, diagnostics.Select(item =>
        {
            var position = item.Location.GetLineSpan().StartLinePosition;
            return (position.Line, position.Character);
        }));
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("Test0.cs", diagnostic.Location.GetLineSpan().Path);
            Assert.Contains("configuration builder", diagnostic.GetMessage());
        });
        Assert.Equal(new[] { "Serialize", "Serialize", "Export", "Export", "Render", "Render", "Write", "Write", "Save", "Save", "Store", "Store", "Write", "Write" },
            diagnostics.Select(Text));
    }

    [Theory]
    [InlineData("void M(int input, System.IO.Stream output); void M(int source, string path);", 2)]
    [InlineData("void M(int input); void M(string input);", 0)]
    [InlineData("void M(); void M(string input);", 0)]
    [InlineData("void M(int input, int output); void M(int input, string output, int extra);", 0)]
    [InlineData("void M(int input, int output); void M(long input, string output);", 0)]
    [InlineData("static void M(int input, int output) { } void M(int input, string output);", 0)]
    [InlineData("void M<T>(int input, int output); void M<T, U>(int input, string output);", 0)]
    [InlineData("void M<T>(T input, int output); void M<U>(U input, string output);", 2)]
    [InlineData("void M<T, U>(T input, U output); void M<X, Y>(Y input, string output);", 0)]
    [InlineData("void M<T>(System.Collections.Generic.List<T[]> input, int output); void M<U>(System.Collections.Generic.List<U[]> input, string output);", 2)]
    [InlineData("void M(int input, ref int output); void M(int input, string output);", 0)]
    [InlineData("void M(in int input, int output); void M(int input, string output);", 0)]
    [InlineData("void M(in int input, ref int output); void M(in int input, ref string output);", 2)]
    [InlineData("void M(int input, out int output); void M(int input, out string output);", 2)]
    [InlineData("void M(int input, int[] output); void M(int input, params string[] output);", 0)]
    [InlineData("void M(int input, params int[] output); void M(int input, params string[] output);", 2)]
    [InlineData("void M(int input, int output = 0); void M(int input, string output);", 0)]
    [InlineData("void M(int input, int output = 0); void M(int input, string output = null!);", 2)]
    [InlineData("void M(int input = 0, int output = 0); void M(int input = 1, string output = null!);", 2)]
    [InlineData("void M(int input, [System.Runtime.InteropServices.Optional] int output); void M(int input, string output);", 0)]
    [InlineData("void M(int input, [System.Runtime.InteropServices.Optional] int output); void M(int input, string output = null!);", 0)]
    [InlineData("void M(int input, System.IO.Stream output); void M(System.Int32 input, string path);", 2)]
    [InlineData("void M(object? input, int output); void M(object input, string path);", 2)]
    public async Task EnforcesExactlyTheApprovedPairBoundary(string members, int count)
    {
        Assert.Equal(count, (await Analyze(["interface I { " + members + " }"])).Length);
    }

    [Theory]
    [InlineData("(int a, int b)", "(int x, int y)", 2)]
    [InlineData("(int a, int b)", "System.ValueTuple<int, int>", 2)]
    [InlineData("System.ValueTuple<int, int>", "(int x, int y)", 2)]
    [InlineData("((int a, int b) pair, int c)", "((int x, int y) other, int z)", 2)]
    [InlineData("(int a, (int b, int c) pair)", "System.ValueTuple<int, System.ValueTuple<int, int>>", 2)]
    [InlineData("System.Collections.Generic.List<(T a, T b)[]>", "System.Collections.Generic.List<System.ValueTuple<U, U>[]>", 2)]
    [InlineData("Outer<(T a, T b)>.Nested", "Outer<System.ValueTuple<U, U>>.Nested", 2)]
    [InlineData("(int a, int b)*", "System.ValueTuple<int, int>*", 2)]
    [InlineData("delegate*<(T a, T b), void>", "delegate*<System.ValueTuple<U, U>, void>", 2)]
    [InlineData("delegate*<int, (T a, T b)>", "delegate*<int, System.ValueTuple<U, U>>", 2)]
    [InlineData("delegate*<ref (T a, T b), void>", "delegate*<System.ValueTuple<U, U>, void>", 0)]
    [InlineData("delegate* unmanaged[Cdecl]<(int a, int b), void>", "delegate* unmanaged[Stdcall]<System.ValueTuple<int, int>, void>", 0)]
    [InlineData("(T a, T b)[]", "System.ValueTuple<U, U>[,]", 0)]
    [InlineData("(int a, long b)", "(int x, int y)", 0)]
    [InlineData("(T a, T b)", "(U x, int y)", 0)]
    public async Task ComparesTupleTypesWithoutElementNamesRecursively(string input, string alternative, int count)
    {
        var source = "class Outer<T> { public class Nested { } } unsafe interface I { void Save<T>("
            + input + " input, System.IO.Stream output); void Save<U>(" + alternative + " input, string path); }";
        Assert.Equal(count, (await Analyze([source])).Length);
    }

    [Fact]
    public async Task ReportsEachMemberOnceEvenWithMultipleAlternativePeers()
    {
        var diagnostics = await Analyze(["""
            class C
            {
                public void Save(object input, int output) { }
                public void Save(object input, string path) { }
                public void Save(object input, System.IO.Stream output) { }
            }
            """]);
        Assert.Equal(3, diagnostics.Length);
        Assert.Equal(3, diagnostics.Select(item => item.Location.SourceSpan).Distinct().Count());
    }

    [Fact]
    public async Task ResolvesPartialTypesAndReportsPartialMethodDefinitionsOnly()
    {
        var diagnostics = await Analyze(["""
            partial class C
            {
                public partial void Save<T>(T input, int output);
                public partial void Save<T>(T input, string path);
            }
            """, """
            partial class C
            {
                public partial void Save<T>(T input, int output) { }
                public partial void Save<T>(T input, string path) { }
            }
            """]);
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, item => Assert.Equal("Test0.cs", item.Location.GetLineSpan().Path));
    }

    [Fact]
    public async Task ResolvesAlternativesAcrossPartialTypeFiles()
    {
        var diagnostics = await Analyze([
            "partial class C { void Save(object input, int output) { } }",
            "partial class C { void Save(object input, string path) { } }"]);
        Assert.Equal(new[] { "Test0.cs", "Test1.cs" }, diagnostics.Select(item => item.Location.GetLineSpan().Path));
    }

    [Fact]
    public async Task GenericContainingTypesDoNotMergeUnrelatedNestedTypes()
    {
        Assert.Empty(await Analyze(["""
            class Outer<T>
            {
                public class A { public void Save(T input, int output) { } }
                public class B { public void Save(T input, string path) { } }
                public class Derived : A { public void Save(T input, string path) { } }
            }
            """]));
    }

    [Fact]
    public async Task AppliesThePairBoundaryToInterfacesAndImplementationsIndependently()
    {
        var diagnostics = await Analyze(["""
            interface I
            {
                void Save(int input, int output);
                void Save(int input, string path);
            }
            class C : I
            {
                public void Save(int input, int output) { }
                public void Save(int input, string path) { }
            }
            class E : I
            {
                void I.Save(int input, int output) { }
                void I.Save(int input, string path) { }
            }
            """]);
        Assert.Equal(6, diagnostics.Length);
        Assert.All(diagnostics, item => Assert.Equal("Save", Text(item)));
    }

    [Fact]
    public async Task DoesNotInferFamiliesFromResolvedDynamicOrUnresolvedCalls()
    {
        Assert.Empty(await Analyze(["""
            class C
            {
                public void Save(int input, int output) { }
                void Use(dynamic target)
                {
                    Save(1, 2);
                    Save(1, "unresolved");
                    target.Save(1, 2);
                    target.Save(1, "dynamic");
                    System.Console.WriteLine("{0}", 1);
                    System.Console.WriteLine("{0}", new object());
                }
            }
            """], requireCompilation: false));
    }

    [Fact]
    public async Task CountRuleConfigurationDoesNotDisableStaticOverloadRule()
    {
        var diagnostics = await Analyze([Fixture("Findings.cs"), Fixture("Accepted.cs")], includeCountAnalyzer: true);
        Assert.Equal(14, diagnostics.Length);
        Assert.All(diagnostics, item => Assert.Equal(DiagnosticIds.OverloadConfigurationBuilder, item.Id));
    }

    [Theory]
    [InlineData("Test.g.cs", "")]
    [InlineData("Test.cs", "// <auto-generated/>\n")]
    [InlineData("Test.cs", "[System.CodeDom.Compiler.GeneratedCode(\"test\", \"1\")]\n")]
    public async Task SkipsGeneratedDeclarations(string path, string prefix)
    {
        Assert.Empty(await Analyze([prefix + "class C { void M(int a, int b) { } void M(int a, string b) { } }"], path: path));
    }

    [Theory]
    [InlineData(ReportDiagnostic.Suppress, 0)]
    [InlineData(ReportDiagnostic.Error, 2)]
    public async Task HonorsSeverityConfiguration(ReportDiagnostic severity, int count)
    {
        var diagnostics = await Analyze(["interface I { void M(int a, int b); void M(int a, string b); }"], severity: severity);
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, item => Assert.Equal(DiagnosticSeverity.Error, item.Severity));
    }

    [Fact]
    public async Task HonorsScopedSuppressionsWithoutHidingNeighbors()
    {
        var diagnostics = await Analyze(["""
            interface I
            {
            #pragma warning disable CR1001
                void M(int a, int b);
            #pragma warning restore CR1001
                [System.Diagnostics.CodeAnalysis.SuppressMessage("Readability", "CR1001", Justification = "Migration deferred")]
                void M(int a, string b);
                void M(int a, System.IO.Stream b);
            }
            """]);
        Assert.Equal("M", Text(Assert.Single(diagnostics)));
        Assert.Equal(7, diagnostics[0].Location.GetLineSpan().StartLinePosition.Line);
    }

    [Theory]
    [InlineData("void M(Missing a, int b); void M(Missing a, string b);")]
    [InlineData("void M(System.Collections.Generic.List<Missing[]> a, int b); void M(System.Collections.Generic.List<Missing[]> a, string b);")]
    [InlineData("void M(int a, Missing[] b); void M(int a, string b);")]
    [InlineData("class Outer<T> { public class Nested { } } void M(Outer<Missing>.Nested a, int b); void M(Outer<Missing>.Nested a, string b);")]
    [InlineData("void M(int a, int b); void M(int a, string b, );")]
    public async Task SkipsUnresolvedOrMalformedSignatures(string members)
    {
        Assert.Empty(await Analyze(["interface I { " + members + " }"], requireCompilation: false));
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "OverloadConfiguration", name));

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string[] sources, string? path = null,
        ReportDiagnostic severity = ReportDiagnostic.Default, bool requireCompilation = true, bool includeCountAnalyzer = false)
    {
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12), path: path ?? $"Test{index}.cs"));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true,
            specificDiagnosticOptions: ImmutableDictionary<string, ReportDiagnostic>.Empty
                .Add(DiagnosticIds.OverloadConfigurationBuilder, severity)
                .Add(DiagnosticIds.ContractParameterGroup, ReportDiagnostic.Suppress));
        var compilation = CSharpCompilation.Create("OverloadTests", trees, References, options);
        if (requireCompilation) Assert.Empty(compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error));
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new OverloadConfigurationAnalyzer());
        if (includeCountAnalyzer) analyzers = analyzers.Add(new ContractParameterAnalyzer());
        var diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        Assert.All(diagnostics, item => Assert.Equal(DiagnosticIds.OverloadConfigurationBuilder, item.Id));
        return diagnostics.OrderBy(item => item.Location.SourceTree!.FilePath).ThenBy(item => item.Location.SourceSpan.Start).ToImmutableArray();
    }
}
