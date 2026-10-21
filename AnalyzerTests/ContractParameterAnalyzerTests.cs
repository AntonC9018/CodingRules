using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class ContractParameterAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    [Fact]
    public async Task ReportsOwnedContractsOnceAndAcceptsReviewedStaticAndExternalShapes()
    {
        var sources = new[] { Fixture("Findings.cs"), Fixture("Accepted.cs") };
        var diagnostics = await Analyze(sources);
        Assert.Equal(9, diagnostics.Length);
        var expected = sources[0].Split('\n').Select((line, index) => (line, index))
            .Where(item => item.line.Contains("// CR1000: "))
            .Select(item => (item.index, item.line.IndexOf(item.line.Split("// CR1000: ")[1].Trim(), StringComparison.Ordinal)));
        Assert.Equal(expected, diagnostics.Select(item =>
        {
            var position = item.Location.GetLineSpan().StartLinePosition;
            return (position.Line, position.Character);
        }));
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticIds.ContractParameterGroup, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("Test0.cs", diagnostic.Location.GetLineSpan().Path);
            Assert.Contains("cohesive inputs", diagnostic.GetMessage());
        });
        Assert.Equal(new[] { "Execute", "Default", "Create", "ImportBase", "Visit", "Merge", "PrimaryImport", "ImportRecord", "ImportedLessonHandler" },
            diagnostics.Select(Text));
    }

    [Theory]
    [InlineData("interface C { void M(int a, int b, int c); }", 0)]
    [InlineData("interface C { void M(int a, int b, int c, System.Threading.CancellationToken cancellation); }", 1)]
    [InlineData("interface C { void M(int a, int b, int c, int d = 0); }", 1)]
    [InlineData("interface C { void M(int a, int b, int c, params int[] values); }", 1)]
    [InlineData("interface C { void M(int a, int b, params int[] values); }", 0)]
    [InlineData("interface C { void M<T>(in T a, ref T b, out T c, T d); }", 1)]
    [InlineData("abstract class C { private C(int a, int b, int c, int d) { } }", 1)]
    [InlineData("abstract record C(int A, int B, int Cc, int D);", 1)]
    [InlineData("abstract class C(int a, int b, int c);", 0)]
    [InlineData("record C(int A, int B, int Cc, int D);", 0)]
    [InlineData("record struct C(int A, int B, int Cc, int D);", 0)]
    [InlineData("class C { public virtual void M(int a, int b, int c) { } }", 0)]
    [InlineData("class C { public virtual void M(int a, int b, int c, int d, int e) { } }", 1)]
    [InlineData("abstract class C { protected abstract void M(int a, int b, int c, int d); }", 1)]
    [InlineData("delegate void C<T>(T a, T b, T c, T d);", 1)]
    [InlineData("interface C { static virtual void M(int a, int b, int c, int d) { } }", 1)]
    [InlineData("class Outer<T> { public class Nested { } } interface C { void M(Outer<int>.Nested a, int b, int c, int d); }", 1)]
    [InlineData("unsafe interface C { void M(delegate*<int, void> a, int b, int c, int d); }", 1)]
    [InlineData("unsafe interface C { void M(delegate*<int, int> a, int b, int c, int d); }", 1)]
    [InlineData("interface C { int this[int a, int b, int c, int d] { get; } }", 0)]
    public async Task CountsDeclaredSlotsAtFourWithinAcceptedCallableCategories(string source, int count)
    {
        var diagnostics = await Analyze([source]);
        Assert.Equal(count, diagnostics.Length);
        if (count == 1)
        {
            var declarationCount = source.Contains("int e", StringComparison.Ordinal) ? 5 : 4;
            Assert.Contains($"the {declarationCount} parameters", diagnostics[0].GetMessage());
        }
    }

    [Fact]
    public async Task OwnsLocalInheritedAndGenericContractsWithoutRepeatingImplementations()
    {
        var diagnostics = await Analyze(["""
            interface I<T> { void M(T a, T b, T c, T d); }
            interface IDerived : I<int> { }
            class C : IDerived { public virtual void M(int a, int b, int c, int d) { } }
            class D : C { public sealed override void M(int a, int b, int c, int d) { } }
            class E : I<string> { void I<string>.M(string a, string b, string c, string d) { } }
            """]);
        Assert.Equal("M", Text(Assert.Single(diagnostics)));
        Assert.Equal(0, diagnostics[0].Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public async Task NewContractAndUnrelatedTryFormatSpellingAreStillOwned()
    {
        var diagnostics = await Analyze(["""
            interface I { void M(int a, int b, int c, int d); }
            interface J : I { new void M(int a, int b, int c, int d); }
            class C { public virtual void TryFormat(int a, int b, int c, int d) { } }
            """]);
        Assert.Equal(new[] { "M", "M", "TryFormat" }, diagnostics.Select(Text));
    }

    [Fact]
    public async Task ExternalMetadataContractIsExemptWhileLocalContractStillReports()
    {
        var external = CSharpCompilation.Create("External", [CSharpSyntaxTree.ParseText("""
            public interface IExternal<T> { void M(T a, T b, T c, T d); }
            public abstract class ExternalBase { public abstract void N(int a, int b, int c, int d); }
            """)], References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Assert.True(external.Emit(stream).Success);
        var reference = MetadataReference.CreateFromImage(stream.ToArray());
        var diagnostics = await Analyze(["""
            interface ILocal { void Own(int a, int b, int c, int d); }
            abstract class C : ExternalBase, IExternal<int>, ILocal
            {
                public virtual void M(int a, int b, int c, int d) { }
                public abstract override void N(int a, int b, int c, int d);
                public virtual void Own(int a, int b, int c, int d) { }
            }
            """], additionalReference: reference);
        Assert.Equal("Own", Text(Assert.Single(diagnostics)));
        Assert.Equal(0, diagnostics[0].Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public async Task CallbacksAndOverloadFamiliesHaveNoIndependentCountFinding()
    {
        Assert.Empty(await Analyze(["""
            class C
            {
                public C(int a, int b, int c, int d) { }
                public static void M(int a, int b, int c, string d) { }
                public static void M(int a, int b, int c, int d) { }
                public void N(int a, int b, int c, int d)
                {
                    System.Action<int, int, int, int> lambda = (a, b, c, d) => { };
                    System.Action<int, int, int, int> anonymous = delegate(int a, int b, int c, int d) { };
                    void Local(int a, int b, int c, int d) { }
                    Local(a, b, c, d);
                }
            }
            """]));
    }

    [Theory]
    [InlineData("Test.g.cs", "")]
    [InlineData("Test.cs", "// <auto-generated/>\n")]
    [InlineData("Test.cs", "[System.CodeDom.Compiler.GeneratedCode(\"test\", \"1\")]\n")]
    public async Task SkipsGeneratedContracts(string path, string prefix)
    {
        Assert.Empty(await Analyze([prefix + "interface I { void M(int a, int b, int c, int d); }"], path: path));
    }

    [Fact]
    public async Task GeneratedOrSuppressedContractsDoNotMoveFindingsToImplementations()
    {
        Assert.Empty(await Analyze(["""
            // <auto-generated/>
            interface IGenerated { void M(int a, int b, int c, int d); }
            """, """
            interface IOwned
            {
            #pragma warning disable CR1000
                void N(int a, int b, int c, int d);
            #pragma warning restore CR1000
            }
            class C : IGenerated, IOwned
            {
                public virtual void M(int a, int b, int c, int d) { }
                public virtual void N(int a, int b, int c, int d) { }
            }
            """]));
    }

    [Theory]
    [InlineData(ReportDiagnostic.Suppress, 0)]
    [InlineData(ReportDiagnostic.Error, 1)]
    public async Task HonorsSeverityConfiguration(ReportDiagnostic severity, int count)
    {
        var diagnostics = await Analyze(["interface I { void M(int a, int b, int c, int d); }"], severity: severity);
        Assert.Equal(count, diagnostics.Length);
        if (count > 0) Assert.Equal(DiagnosticSeverity.Error, diagnostics[0].Severity);
    }

    [Fact]
    public async Task HonorsScopedSuppressionsWithoutHidingNeighbors()
    {
        var diagnostics = await Analyze(["""
            interface I
            {
            #pragma warning disable CR1000
                void First(int a, int b, int c, int d);
            #pragma warning restore CR1000
                [System.Diagnostics.CodeAnalysis.SuppressMessage("Readability", "CR1000", Justification = "Migration deferred")]
                void Second(int a, int b, int c, int d);
                void Neighbor(int a, int b, int c, int d);
            }
            """]);
        Assert.Equal("Neighbor", Text(Assert.Single(diagnostics)));
    }

    [Theory]
    [InlineData("interface I { void M(Missing a, int b, int c, int d); }")]
    [InlineData("interface I { void M(System.Collections.Generic.List<Missing> a, int b, int c, int d); }")]
    [InlineData("class Outer<T> { public class Nested { } } interface I { void M(Outer<Missing>.Nested a, int b, int c, int d); }")]
    [InlineData("unsafe interface I { void M(delegate*<Missing, void> a, int b, int c, int d); }")]
    [InlineData("unsafe interface I { void M(delegate*<int, Missing> a, int b, int c, int d); }")]
    [InlineData("interface I { void M(int a, int b, int c, ); }")]
    public async Task SkipsInvalidSignatures(string source)
    {
        Assert.Empty(await Analyze([source], requireCompilation: false));
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ContractParameters", name));

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string[] sources, string? path = null,
        ReportDiagnostic severity = ReportDiagnostic.Default, bool requireCompilation = true, MetadataReference? additionalReference = null)
    {
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12), path: path ?? $"Test{index}.cs"));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true,
            specificDiagnosticOptions: ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(DiagnosticIds.ContractParameterGroup, severity));
        var references = additionalReference is null ? References : References.Add(additionalReference);
        var compilation = CSharpCompilation.Create("ContractTests", trees, references, options);
        if (requireCompilation) Assert.Empty(compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error));
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ContractParameterAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        Assert.All(diagnostics, item => Assert.Equal(DiagnosticIds.ContractParameterGroup, item.Id));
        return diagnostics.OrderBy(item => item.Location.SourceTree!.FilePath).ThenBy(item => item.Location.SourceSpan.Start).ToImmutableArray();
    }
}
