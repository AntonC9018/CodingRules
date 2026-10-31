using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class CompoundPredicateIntegrationTests
{
    [Fact]
    public async Task SharedPackageFixtureHasExactFindingsAndRetainsBehaviorAfterBothFixes()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "CompoundPredicates");
        var findings = File.ReadAllText(Path.Combine(directory, "Findings.cs"));
        var document = CompoundPredicateTestFixture.Document(findings);
        var accepted = File.ReadAllText(Path.Combine(directory, "Accepted.cs"));
        var project = document.Project.AddDocument("Accepted.cs", accepted).Project
            .AddDocument("Runtime.cs", File.ReadAllText(Path.Combine(directory, "Runtime.cs"))).Project;
        document = project.GetDocument(document.Id)!;
        var diagnostics = await CompoundPredicateTestFixture.Diagnostics(document);
        Assert.Equal(2, diagnostics.Length);
        var expected = findings.Split('\n').Select((line, index) => (line, index))
            .Where(item => item.line.Contains("// CR0403: "))
            .Select(item => (item.index, item.line.Split("// CR0403: ")[0]
                .IndexOf(item.line.Split("// CR0403: ")[1].Trim(), StringComparison.Ordinal)));
        Assert.Equal(expected, diagnostics.Select(diagnostic =>
        {
            var position = diagnostic.Location.GetLineSpan().StartLinePosition;
            return (position.Line, position.Character);
        }));
        var behavior = await StatementOperationTestFixture.Run(document);
        Assert.Equal("lab0|lab1|lab2|True|True|True|False|True|True|True|True", behavior);
        document = await CompoundPredicateTestFixture.Fix(document);
        document = await CompoundPredicateTestFixture.Fix(document);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(document));
        Assert.Equal(behavior, await StatementOperationTestFixture.Run(document));
        var acceptedDocument = document.Project.Documents.Single(value => value.Name == "Accepted.cs");
        Assert.Equal(accepted, (await acceptedDocument.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData("warning", DiagnosticSeverity.Warning)]
    [InlineData("error", DiagnosticSeverity.Error)]
    [InlineData("none", null)]
    public async Task SeverityIsIndependentOfExistingPipelineRules(string severity, DiagnosticSeverity? expected)
    {
        const string source = """
            using System.Linq;
            class C {
                sealed record Item(int Value);
                static int Read(int value) => value;
                static object Projection(int[] ids) => ids.Select(id => new Item(Read(id)));
                static bool Predicate(int[] ids) => ids.Any(id => id > 0 && ids.Any(p => p == id));
            }
            """;
        var document = CompoundPredicateTestFixture.Document(source, "root = true\n[*.cs]\n"
            + "dotnet_diagnostic.CR0403.severity = " + severity + "\n");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostics = await CompoundPredicateTestFixture.Diagnostics(document, all: true);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CR0400");
        var findings = diagnostics.Where(diagnostic => diagnostic.Id == "CR0403").ToArray();
        if (expected is null)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal(expected, Assert.Single(findings).Severity);
        }
    }

    [Theory]
    [InlineData("netstandard.dll", false)]
    [InlineData("netstandard.dll", true)]
    [InlineData("netstandard2.1.dll", false)]
    [InlineData("netstandard2.1.dll", true)]
    public async Task ActualReferenceFrameworkDefinitionsAndSourceLookalikes(string reference, bool shadow)
    {
        var source = "using System.Linq; class C { static bool M(int[] ids) => "
            + "ids.Any(id => id > 0 && ids.Any(p => p == id)); }"
            + (shadow ? "namespace System.Linq { static class Enumerable { "
                + "public static bool Any<T>(this T[] source, System.Func<T, bool> predicate) => true; }}" : "");
        var compilation = CSharpCompilation.Create("PredicateReference", [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "References", reference))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var diagnostics = await compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new CompoundPredicateAnalyzer())).GetAnalyzerDiagnosticsAsync();
        if (shadow)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            Assert.Equal("CR0403", Assert.Single(diagnostics).Id);
        }
    }
}
