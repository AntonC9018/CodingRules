using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class SealConcreteClassAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    [Fact]
    public void AcceptedFixtureCompilesWithWarningsAsErrors()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SealedClasses", "Accepted.cs"));
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            generalDiagnosticOption: ReportDiagnostic.Error, nullableContextOptions: NullableContextOptions.Enable);
        var compilation = CSharpCompilation.Create("AcceptedSealingFixture", [tree], References, options);
        Assert.Empty(compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task ReportsA16DespiteInheritedTestSeamAndAcceptsSharedBoundaries()
    {
        var findings = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SealedClasses", "Findings.cs"));
        var accepted = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SealedClasses", "Accepted.cs"));
        var diagnostics = await Analyze([findings, accepted]);
        var expected = findings.Split('\n').Select((line, index) => (line, index))
            .Where(item => item.line.Contains("// CR1400: "))
            .Select(item => (item.index, item.line.Split("// CR1400: ")[0]
                .IndexOf(item.line.Split("// CR1400: ")[1].Trim(), StringComparison.Ordinal)));
        Assert.Equal(9, diagnostics.Length);
        Assert.Equal(expected, diagnostics.Select(item =>
        {
            var position = item.Location.GetLineSpan().StartLinePosition;
            return (position.Line, position.Character);
        }));
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal("Test0.cs", diagnostic.Location.GetLineSpan().Path);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Contains("manually", diagnostic.GetMessage());
        });
    }

    [Theory]
    [InlineData("class C { }", 1)]
    [InlineData("public class C { }", 1)]
    [InlineData("class C<T> { } sealed class D : C<int> { }", 1)]
    [InlineData("class C { public virtual void M() { } }", 1)]
    [InlineData("class C : System.Exception { }", 1)]
    [InlineData("class C : System.Collections.Generic.List<int> { }", 1)]
    [InlineData("record C;", 1)]
    [InlineData("record C; sealed record D : C;", 1)]
    [InlineData("class C { public sealed class D { } }", 1)]
    [InlineData("abstract class C { }", 0)]
    [InlineData("static class C { }", 0)]
    [InlineData("sealed class C { }", 0)]
    [InlineData("abstract record C;", 0)]
    [InlineData("record struct C;", 0)]
    [InlineData("interface C { }", 0)]
    [InlineData("delegate void C();", 0)]
    [InlineData("file class C { }", 1)]
    [InlineData("class C(int value);", 1)]
    public async Task CoversConcreteClassesWithoutGeneralInheritanceOrVisibilityExceptions(string source, int count)
        => Assert.Equal(count, (await Analyze([source])).Length);

    [Theory]
    [InlineData("public AAttribute() : base(typeof(T)) { }", 0)]
    [InlineData("public AAttribute() : base(type: typeof(T)) { }", 0)]
    [InlineData("public AAttribute() : base(typeof(int)) { }", 1)]
    [InlineData("public AAttribute() : base(typeof(T[])) { }", 1)]
    [InlineData("public AAttribute() : base(typeof(T)) { System.Console.WriteLine(); }", 1)]
    [InlineData("public AAttribute() : base(typeof(T)) { } public int Value;", 1)]
    [InlineData("public AAttribute() : base(typeof(T)) { } public static void Run() { }", 1)]
    [InlineData("public AAttribute(System.Type type) : base(type) { }", 1)]
    public async Task OnlyAcceptsProvenTypeForwardingWrappers(string member, int count)
    {
        var source = "class AAttribute : System.Attribute { public AAttribute(System.Type type) { } } "
            + "sealed class AAttribute<T> : AAttribute { " + member + " }";
        Assert.Equal(count, (await Analyze([source])).Length);
    }

    [Fact]
    public async Task DoesNotExemptGenericVariantItselfOrUnrelatedGenericInheritance()
    {
        var diagnostics = await Analyze(["""
            class AAttribute : System.Attribute { public AAttribute(System.Type type) { } }
            class AAttribute<T> : AAttribute { public AAttribute() : base(typeof(T)) { } }
            class BAttribute : System.Attribute { public BAttribute(System.Type type) { } }
            sealed class Other<T> : BAttribute { public Other() : base(typeof(T)) { } }
            """]);
        Assert.Equal(new[] { "AAttribute", "BAttribute" }, diagnostics.Select(Text));
        Assert.Equal(1, diagnostics[0].Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public async Task RecognizesPartialAndGeneratedForwardingVariants()
    {
        var diagnostics = await Analyze([
            "class AAttribute : System.Attribute { public AAttribute(System.Type type) { } }",
            "// <auto-generated/>\nsealed class AAttribute<T>() : AAttribute(typeof(T));"]);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsPartialTypeOnceAndDoesNotTreatGeneratedInheritorsAsAnExemption()
    {
        var diagnostics = await Analyze([
            "partial class C { }",
            "partial class C { }",
            "// <auto-generated/>\nsealed class D : C { }"]);
        Assert.Equal("C", Text(Assert.Single(diagnostics)));
    }

    [Theory]
    [InlineData(true, "class")]
    [InlineData(false, "class")]
    [InlineData(true, "record")]
    [InlineData(false, "record")]
    public async Task MixedPartialDeclarationsKeepTheHandWrittenFinding(bool generatedFirst, string kind)
    {
        var generated = $"// <auto-generated/>\npartial {kind} C {{ }}";
        var handwritten = $"partial {kind} C {{ }}";
        var sources = generatedFirst ? new[] { generated, handwritten } : new[] { handwritten, generated };
        var diagnostic = Assert.Single(await Analyze(sources));
        Assert.Equal(generatedFirst ? "Test1.cs" : "Test0.cs", diagnostic.Location.GetLineSpan().Path);
    }

    [Fact]
    public async Task SupportsNestedAndPartialAttributeVariants()
    {
        Assert.Empty(await Analyze(["""
            sealed partial class Owner
            {
                public class AAttribute : System.Attribute { public AAttribute(System.Type type) { } }
                public sealed partial class AAttribute<T> : AAttribute { }
            }
            """, """
            sealed partial class OutsideAttribute<T> : OutsideAttribute
            {
                public OutsideAttribute() : base(typeof(T)) { }
            }
            class OutsideAttribute : System.Attribute { public OutsideAttribute(System.Type type) { } }
            sealed partial class OutsideAttribute<T> { }
            """, """
            sealed partial class Owner
            {
                public sealed partial class AAttribute<T> { public AAttribute() : base(typeof(T)) { } }
            }
            """]));
    }

    [Theory]
    [InlineData("Generated.g.cs", "")]
    [InlineData("Test.cs", "// <auto-generated/>\n")]
    [InlineData("Test.cs", "[System.CodeDom.Compiler.GeneratedCode(\"test\", \"1\")]\n")]
    public async Task SkipsGeneratedClasses(string path, string prefix)
        => Assert.Empty(await Analyze([prefix + "class C { }"], path: path));

    [Theory]
    [InlineData(ReportDiagnostic.Suppress, 0)]
    [InlineData(ReportDiagnostic.Error, 1)]
    public async Task HonorsSeverity(ReportDiagnostic severity, int count)
    {
        var diagnostics = await Analyze(["class C { }"], severity: severity);
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public async Task HonorsPragmaAndAttributeSuppressionWithoutHidingNeighbors()
    {
        var diagnostics = await Analyze(["""
            #pragma warning disable CR1400
            class Suppressed { }
            #pragma warning restore CR1400
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Readability", "CR1400", Justification = "External constraint")]
            class Reviewed { }
            class Neighbor { }
            """]);
        Assert.Equal("Neighbor", Text(Assert.Single(diagnostics)));
    }

    [Fact]
    public async Task SkipsMalformedAndUnresolvedBaseDeclarations()
        => Assert.Empty(await Analyze(["class C : Missing { }"], requireCompilation: false));

    [Fact]
    public async Task SkipsMalformedDeclaration()
        => Assert.Empty(await Analyze(["class C {"], requireCompilation: false));

    [Fact]
    public async Task DoesNotReportImplicitTopLevelProgram()
        => Assert.Empty(await Analyze(["System.Console.WriteLine();"], outputKind: OutputKind.ConsoleApplication));

    [Fact]
    public async Task RetainsUpstreamBenchmarkExternalConstraintButRejectsSourceNameSpoofs()
    {
        const string definition = """
            namespace BenchmarkDotNet.Attributes
            {
                public sealed class BenchmarkAttribute : System.Attribute { }
            }
            """;
        using var image = new MemoryStream();
        var external = CSharpCompilation.Create("BenchmarkDotNet.Annotations", [CSharpSyntaxTree.ParseText(definition)], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.True(external.Emit(image).Success);
        var reference = MetadataReference.CreateFromImage(image.ToArray());
        const string direct = "class C { [BenchmarkDotNet.Attributes.Benchmark] public void Run() { } }";
        const string inherited = "abstract class B { [BenchmarkDotNet.Attributes.Benchmark] public void Run() { } } class C : B { }";
        foreach (var benchmark in new[] { direct, inherited })
        {
            Assert.Empty(await Analyze([benchmark], extraReference: reference));
            Assert.Equal("C", Text(Assert.Single(await Analyze([definition, benchmark]))));
        }
    }

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string[] sources, string? path = null,
        ReportDiagnostic severity = ReportDiagnostic.Default, bool requireCompilation = true,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary, MetadataReference? extraReference = null)
    {
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12), path: path ?? $"Test{index}.cs"));
        var options = new CSharpCompilationOptions(outputKind,
            specificDiagnosticOptions: ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(DiagnosticIds.SealConcreteClass, severity));
        var references = extraReference is null ? References : References.Add(extraReference);
        var compilation = CSharpCompilation.Create("SealingTests", trees, references, options);
        if (requireCompilation) Assert.Empty(compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error));
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SealConcreteClassAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        Assert.All(diagnostics, diagnostic => Assert.Equal(DiagnosticIds.SealConcreteClass, diagnostic.Id));
        return diagnostics.OrderBy(item => item.Location.SourceTree!.FilePath).ThenBy(item => item.Location.SourceSpan.Start).ToImmutableArray();
    }
}
