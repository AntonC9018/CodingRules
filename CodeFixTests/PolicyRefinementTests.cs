using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class PolicyRefinementTests
{
    [Theory]
    [InlineData("Equals", 0)]
    [InlineData("Equals1", 0)]
    [InlineData("Equals42", 0)]
    [InlineData("EqualsIgnoreCase", 1)]
    [InlineData("Equals1Extra", 1)]
    public async Task EqualityNamesAreExact(string name, int count)
    {
        var document = NamedArgumentTestFixture.Document($"class C {{ static bool {name}(string left,string right) => left==right; static bool M() => {name}(\"a\",\"b\"); }}");
        Assert.Equal(count, (await NamedArgumentTestFixture.Diagnostics(document)).Length);
    }

    [Fact]
    public async Task AssemblyRegistrationUsesExactDeclarationId()
    {
        var source = """
            [assembly: CodingRules.AllowExternalPositionalArguments(assemblyName: "ASSEMBLY", declarationId: "M:C.Compare(System.Int32,System.Int32)~System.Boolean")]
            class C {
                static bool Compare(int left,int right) => left==right;
                static bool Other(int left,int right) => left==right;
                static void M() { Compare(1,2); Other(1,2); }
            }
            """;
        var prototype = NamedArgumentTestFixture.Document(source);
        var compilation = await prototype.Project.GetCompilationAsync();
        source = source.Replace("ASSEMBLY", compilation!.AssemblyName);
        var diagnostics = await NamedArgumentTestFixture.Diagnostics(NamedArgumentTestFixture.Document(source));
        Assert.Single(diagnostics);
        Assert.Equal(source.IndexOf("Other(1,2)", StringComparison.Ordinal) + "Other".Length, diagnostics[0].Location.SourceSpan.Start);
    }

    [Theory]
    [InlineData("(int)pid", 0)]
    [InlineData("System.TimeSpan.FromSeconds(2)", 1)]
    public async Task CastsAreAcceptedButDurationFactoriesStillNeedNaming(string value, int count)
    {
        var document = StatementOperationTestFixture.Document("class C { static void Use(object value) {} static void M(long pid) { Use("+value+"); } }");
        Assert.Equal(count, (await StatementOperationTestFixture.Diagnostics(document)).Length);
    }

    [Fact]
    public async Task ArgumentNamesDriveHelpersAndCopiesAreNotRepeated()
    {
        var document = StatementOperationTestFixture.Document("""
            class C {
                static string Use(string directory) => directory;
                public static string Run() => Use(System.IO.Path.GetDirectoryName("folder/file")!);
            }
            """);
        var before = await StatementOperationTestFixture.Run(document);
        var changed = await StatementOperationTestFixture.Fix(document, StatementOperationCodeFixProvider.ExtractKey);
        var text = (await changed.GetTextAsync()).ToString();
        Assert.True(text.Contains("CalculateDirectory"), text);
        Assert.DoesNotContain("operationValue", text);
        Assert.Equal(before, await StatementOperationTestFixture.Run(await StatementOperationTestFixture.Reparse(changed)));
    }

    [Fact]
    public async Task BracesAndCastSpaceSurviveSavedSource()
    {
        var document = StatementOperationTestFixture.Document("""
            class C {
                public static string Run() {
                    long pid = 2;
                    if (pid > 0)
                        return ((int)pid).ToString();
                    return "zero";
                }
            }
            """);
        var analyzer = new BracesAndCastSpacingAnalyzer();
        var diagnostics = await Analyze(document, analyzer);
        Assert.Equal(new[] { "CR0803", "CR0804" }, diagnostics.Select(item => item.Id));
        var before = await StatementOperationTestFixture.Run(document);
        for (var attempt = 0; attempt < 4 && (await Analyze(document, analyzer)).FirstOrDefault() is { } diagnostic; attempt++)
            document = await Apply(document, diagnostic, new BracesAndCastSpacingCodeFixProvider());
        var saved = await StatementOperationTestFixture.Reparse(document);
        Assert.Empty(await Analyze(saved, analyzer));
        Assert.Contains("(int) pid", (await saved.GetTextAsync()).ToString());
        Assert.Equal(before, await StatementOperationTestFixture.Run(saved));
    }

    [Fact]
    public async Task ExplicitCollectionRuleKeepsSharedGenericMembers()
    {
        var document = StatementOperationTestFixture.Document("""
            using System.Collections;
            using System.Collections.Generic;
            class C : IEnumerable<int>, ICollection {
                public int Count => 0;
                public bool IsSynchronized => false;
                public object SyncRoot => this;
                public void CopyTo(System.Array a,int i) { }
                public IEnumerator<int> GetEnumerator() => throw new System.NotSupportedException();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            """);
        var diagnostics = await Analyze(document, new ExplicitCollectionImplementationAnalyzer());
        Assert.Equal(4, diagnostics.Length);
        var explicitSource = (await document.GetTextAsync()).ToString()
            .Replace("public int Count", "int ICollection.Count")
            .Replace("public bool IsSynchronized", "bool ICollection.IsSynchronized")
            .Replace("public object SyncRoot", "object ICollection.SyncRoot")
            .Replace("public void CopyTo", "void ICollection.CopyTo");
        Assert.Empty(await Analyze(StatementOperationTestFixture.Document(explicitSource), new ExplicitCollectionImplementationAnalyzer()));
    }

    [Fact]
    public async Task FormattingFixesDoNotChangeObservedCallerInformation()
    {
        var document = StatementOperationTestFixture.Document("""
            class C {
                static void Log(int value, [System.Runtime.CompilerServices.CallerArgumentExpression("value")] string expression = "") { }
                static void M(long pid) { if (pid > 0) Log((int)pid); }
            }
            """);
        var diagnostics = await Analyze(document, new BracesAndCastSpacingAnalyzer());
        Assert.Equal(2, diagnostics.Length);
        foreach (var diagnostic in diagnostics)
        {
            var actions = new List<CodeAction>();
            await new BracesAndCastSpacingCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), default));
            Assert.Empty(actions);
        }
    }

    [Fact]
    public async Task ExceptionFixPreservesMessageAndExposesCapturedValues()
    {
        var document = StatementOperationTestFixture.Document("""
            class C {
                public static string Run() {
                    string field = "teacher";
                    int count = 3;
                    try { throw new System.NotSupportedException($"Unknown preliminary field {field}: {count:D2}"); }
                    catch (System.NotSupportedException error) {
                        var property = error.GetType().GetProperty("Field");
                        return error.Message + ":" + (property?.GetValue(error) ?? field);
                    }
                }
            }
            """);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
        var diagnostic = Assert.Single(await Analyze(document, new ExceptionContextAnalyzer()));
        var changed = await Apply(document, diagnostic, new ExceptionContextCodeFixProvider());
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("UnknownPreliminaryFieldException", text);
        Assert.Contains("public string Field", text);
        Assert.Contains("public int Count", text);
        Assert.Equal(await StatementOperationTestFixture.Run(document), await StatementOperationTestFixture.Run(await StatementOperationTestFixture.Reparse(changed)));
        Assert.Empty(await Analyze(changed, new ExceptionContextAnalyzer()));
    }

    [Fact]
    public async Task NullableAndRepeatedExceptionContextKeepsFormattingAndCatchContract()
    {
        var document = StatementOperationTestFixture.Document("""
            class C {
                public static string Run() {
                    string? value = null;
                    try { throw new System.InvalidOperationException($"Missing {value,5}: '{value}'"); }
                    catch (System.InvalidOperationException error) { return error.Message; }
                }
            }
            """);
        var diagnostic = Assert.Single(await Analyze(document, new ExceptionContextAnalyzer()));
        var changed = await Apply(document, diagnostic, new ExceptionContextCodeFixProvider());
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("public string? Value", text);
        Assert.DoesNotContain("context2", text);
        Assert.Equal(await StatementOperationTestFixture.Run(document), await StatementOperationTestFixture.Run(await StatementOperationTestFixture.Reparse(changed)));
    }

    [Fact]
    public async Task SideEffectingExceptionHoleRemainsManual()
    {
        var document = StatementOperationTestFixture.Document("class C { static int Next() => 1; static void M() { throw new System.Exception($\"Value {Next()}\"); } }");
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
        var diagnostic = Assert.Single(await Analyze(document, new ExceptionContextAnalyzer()));
        var actions = new List<CodeAction>();
        await new ExceptionContextCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), default));
        Assert.Empty(actions);
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(Document document, DiagnosticAnalyzer analyzer)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!.WithAnalyzers(ImmutableArray.Create(analyzer), document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, item => item.Id == "AD0001");
        return diagnostics.OrderBy(item => item.Location.SourceSpan.Start).ToImmutableArray();
    }

    private static async Task<Document> Apply(Document document, Diagnostic diagnostic, CodeFixProvider provider)
    {
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), default));
        var action = Assert.Single(actions);
        var operation = Assert.Single((await action.GetOperationsAsync(default)).OfType<ApplyChangesOperation>());
        return operation.ChangedSolution.GetDocument(document.Id)!;
    }
}
