using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class PlaceholderNameAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    [Fact]
    public async Task EveryDeclaredPlaceholderIsAnErrorNamingItsRole()
    {
        const string source = """
            class TODO_Type
            {
                int TODO_field;
                static int M(int TODO_parameter)
                {
                    bool TODO_CONDITION() => TODO_parameter > 0;
                    int TODO_VARIABLE_1 = TODO_parameter;
                    int TODO_CALCULATION() => TODO_VARIABLE_1 + 1;
                    if (TODO_parameter is var TODO_pattern) return TODO_pattern;
                    return TODO_CONDITION() ? TODO_CALCULATION() : 0;
                }
            }
            """;
        var diagnostics = await Analyze(source);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticIds.PlaceholderName, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        });
        Assert.Equal(
            new[]
            {
                "TODO_Type:symbol", "TODO_field:symbol", "TODO_parameter:symbol", "TODO_CONDITION:condition",
                "TODO_VARIABLE_1:variable", "TODO_CALCULATION:calculation", "TODO_pattern:symbol",
            },
            diagnostics.Select(diagnostic => source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)
                + ":" + diagnostic.GetMessage().Split(' ')[3]));
        Assert.Equal("Rename this extracted condition to a domain name", diagnostics[3].GetMessage());
    }

    [Fact]
    public async Task UsingAliasesAndNamespacesAreDeclarations()
    {
        const string source = """
            using TODO_ALIAS = System.String;
            namespace Outer.TODO_NAMESPACE
            {
                class C
                {
                    static TODO_ALIAS M() => typeof(Outer.TODO_NAMESPACE.C).Name;
                }
            }
            namespace TODO_FILE_NAMESPACE.Inner
            {
            }
            """;
        var diagnostics = await Analyze(source);
        Assert.Equal(
            new[] { "TODO_ALIAS", "TODO_NAMESPACE", "TODO_FILE_NAMESPACE" },
            diagnostics.Select(diagnostic => source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)));
    }

    [Fact]
    public async Task ReferencesAndOtherIdentifiersAreAccepted()
    {
        const string source = """
            class C
            {
                static int TODOValue;
                static int Todo_value;
                static int M() => TODOValue + Todo_value;
            }
            """;
        Assert.Empty(await Analyze(source));
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), path: "Test.cs");
        var compilation = CSharpCompilation.Create("PlaceholderNameTests", [tree], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error));
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PlaceholderNameAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        return diagnostics.OrderBy(item => item.Location.SourceSpan.Start).ToImmutableArray();
    }
}
