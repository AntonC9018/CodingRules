using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace CodingRules;

public sealed class SpanSafetyTests
{
    [Theory]
    [InlineData("static int field = \" a \".Trim().Length;")]
    [InlineData("static bool Property { get; } = \" a \".Trim() == \"a\";")]
    [InlineData("int Property => \" a \".Trim().Length;")]
    [InlineData("int Property { get { return \" a \".Trim().Length; } }")]
    [InlineData("int this[int index] => \" a \".Trim().Length;")]
    [InlineData("int F(string text) { int Local(string value) => value.Trim().Length; return Local(text); }")]
    [InlineData("System.Func<string,int> field = text => text.Trim().Length;")]
    [InlineData("System.Func<string,int> field = delegate(string text) { return text.Trim().Length; };")]
    [InlineData("int value; void F(string text) => value = text.Trim().Length;")]
    [InlineData("int value; C() => value = \" a \".Trim().Length;")]
    [InlineData("int F(string text) { switch(text) { default: return text.Trim().Length; } }")]
    [InlineData("int F(string text) { while(text.Trim().Length > 0) return 1; return 0; }")]
    [InlineData("bool F(string text) { try { return false; } catch(System.Exception e) when(e.Message.Trim() == \"a\") { return true; } }")]
    public async Task EligibleHostInsertsNoncapturingHelperAndSavedSourceCompiles(string member)
    {
        var document = SpanTestFixture.Document("class C { " + member + " }");
        Assert.Single(await SpanTestFixture.Diagnostics(document));
        var changed = await SpanTestFixture.Fix(document);
        Assert.Empty(await SpanTestFixture.Diagnostics(changed));
    }

    [Theory]
    [InlineData("int F(string text) => text.Trim().Length + 1;")]
    [InlineData("long F(string text) => (long)text.Trim().Length;")]
    [InlineData("string F(string text) => $\"{text.Trim().Length}\";")]
    [InlineData("int F(string? text) => text.Trim().Length;")]
    [InlineData("int F(string text) => text.Trim().Length; static void Next() { Observe(); } static void Observe([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) {}")]
    [InlineData("int this[int i, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0] => line; int F(string text) => text.Trim().Length; int Next() => this[0];")]
    [InlineData("static bool Observe(bool value, [System.Runtime.CompilerServices.CallerArgumentExpression(\"value\")] string expression = \"\") => value; bool F(string text) => Observe(text.Trim() == \"a\");")]
    public async Task ObserverOrOlderPolicyConflictOmitsOnlyThatRoot(string member)
    {
        var document = SpanTestFixture.Document("class C { " + member + " int Safe(string text) => text.Trim().Length; }");
        var diagnostics = await SpanTestFixture.Diagnostics(document);
        // Later CallerLineNumber calls can also observe a preceding safe member.
        Assert.Single(diagnostics);
        Assert.Contains("Safe", (await document.GetSyntaxRootAsync())!.FindNode(diagnostics[0].Location.SourceSpan).Ancestors().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First().Identifier.ValueText);
        if (!member.Contains("string?")) await SpanTestFixture.Fix(document, diagnostics[0]);
        else Assert.Single(await SpanTestFixture.Actions(document, diagnostics[0]));
    }

    [Fact]
    public async Task AllOlderUnrelatedRootsSurviveAndGeneratedHelperAddsNone()
    {
        var document = SpanTestFixture.Document("""
            class C {
                static int Use(int x, int y) => x;
                static int Other(int x) => Use(1 + x * 2, 1);
                static bool Conditions(string first, string second) => first.Length == 0 && second.Length == 0;
                static string? Decision(string text) { if(text == "") return null; return text.Length == 1 ? text : null; }
                static int Inspect(string text) => text.Trim().Length;
            }
            """);
        var before = (await SpanTestFixture.Diagnostics(document, all: true)).Where(item => item.Id != "CR0700").Select(Key).OrderBy(item => item).ToArray();
        Assert.NotEmpty(before);
        var changed = await SpanTestFixture.Fix(document);
        Assert.Equal(before, (await SpanTestFixture.Diagnostics(changed, all: true)).Select(Key).OrderBy(item => item).ToArray());
    }

    [Fact]
    public async Task TriviaFreshNamesVisibleDiscardAndExplicitCallerConstantsAreRetained()
    {
        var document = SpanTestFixture.Document("class C { static void Observe([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) {} int F() { int _ = 4; int InspectTextLength = _; string sourceText = \"x\";\n// before\nvar result = \" a \".Trim().Length; // after\nObserve(line: 42); return result + InspectTextLength + sourceText.Length; } }", "root=true\n[*.cs]\nindent_style=space\nindent_size=2\nend_of_line=lf\n");
        var changed = await SpanTestFixture.Fix(document);
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("InspectTextLength2", text);
        Assert.Contains("sourceText2", text);
        Assert.Contains("// before", text); Assert.Contains("// after", text);
        Assert.Contains("Observe(line: 42)", text); Assert.DoesNotContain("_ = source", text);
        Assert.DoesNotContain("\r\n", text);
    }

    [Theory]
    [InlineData("#pragma warning disable CR0700\nint Suppressed(string text) => text.Trim().Length;\n#pragma warning restore CR0700\n")]
    [InlineData("[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Readability\", \"CR0700\", Justification=\"Fixture\")] int Suppressed(string text) => text.Trim().Length;\n")]
    public async Task OrdinaryScopedSuppressionsDoNotDisableNeighbors(string suppressed)
    {
        var document = SpanTestFixture.Document("class C {\n" + suppressed + "int Active(string text) => text.Trim().Length; }");
        Assert.Single(await SpanTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task SeverityAndGeneratedCodeUseNormalAnalyzerDriverConfiguration()
    {
        Assert.Empty(await SpanTestFixture.Diagnostics(SpanTestFixture.Document("class C { int F(string text) => text.Trim().Length; }", "root=true\n[*.cs]\ndotnet_diagnostic.CR0700.severity=none\n")));
        Assert.Empty(await SpanTestFixture.Diagnostics(SpanTestFixture.Document("// <auto-generated/>\nclass C { int F(string text) => text.Trim().Length; }")));
        var information = await SpanTestFixture.Diagnostics(SpanTestFixture.Document("class C { int F(string text) => text.Trim().Length; }", "root=true\n[*.cs]\ndotnet_diagnostic.CR0700.severity=suggestion\n"));
        Assert.Equal(DiagnosticSeverity.Info, Assert.Single(information).Severity);
    }

    [Fact]
    public async Task CachedActionDoesNotApplyToDifferentWorkspaceSnapshot()
    {
        var document = SpanTestFixture.Document("class C { int F(string text) => text.Trim().Length; }");
        var diagnostic = Assert.Single(await SpanTestFixture.Diagnostics(document));
        var action = Assert.Single(await SpanTestFixture.Actions(document, diagnostic));
        var current = document.WithText(SourceText.From("class C { int F(string text) => text.Length; }"));
        Assert.True(document.Project.Solution.Workspace.TryApplyChanges(current.Project.Solution));
        var operation = Assert.Single((await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        Assert.Equal((await current.GetTextAsync()).ToString(), (await operation.ChangedSolution.GetDocument(document.Id)!.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task ConcurrentCancellationDoesNotPoisonLaterCompilationAnalysis()
    {
        var document = SpanTestFixture.Document("class C { int F(string text) => text.Trim().Length; }");
        var compilation = (await document.Project.GetCompilationAsync())!;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SpanTextAnalyzer())).GetAnalyzerDiagnosticsAsync(cancellation.Token));
        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SpanTestFixture.Diagnostics(document)));
        Assert.All(runs, diagnostics => Assert.Single(diagnostics));
    }

    [Theory]
    [InlineData(FixAllScope.Document)]
    [InlineData(FixAllScope.Project)]
    [InlineData(FixAllScope.Solution)]
    public async Task FixAllReplansSavedNestedAndMultipleDocumentsAndIsIdempotent(FixAllScope scope)
    {
        const string source = "partial class C { int A(string text) { int Local(string value) => value.Trim().Length; return text.Trim().Length + Local(text); } bool B(string text) => text.Trim() == \"a\"; }";
        var document = SpanTestFixture.Document(source);
        var second = document.Project.AddDocument("Second.cs", "partial class C { int Other(string text) => text.TrimEnd().Length; }", filePath: "/Second.cs");
        var projectId = ProjectId.CreateNewId(); var documentId = DocumentId.CreateNewId(projectId);
        var solution = second.Project.Solution.AddProject(projectId, "Other", "Other", LanguageNames.CSharp)
            .WithProjectMetadataReferences(projectId, document.Project.MetadataReferences).WithProjectParseOptions(projectId, document.Project.ParseOptions!)
            .WithProjectCompilationOptions(projectId, document.Project.CompilationOptions!).AddDocument(documentId, "Other.cs", SourceText.From(source.Replace("class C", "class D")), filePath: "/Other.cs");
        document = solution.GetDocument(document.Id)!;
        var provider = new SpanTextCodeFixProvider();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var context = new FixAllContext(document, provider, scope, SpanTextCodeFixProvider.Key, provider.FixableDiagnosticIds, new Diagnostics(), timeout.Token);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        var operation = Assert.Single((await action!.GetOperationsAsync(timeout.Token)).OfType<ApplyChangesOperation>());
        foreach (var changed in operation.ChangedSolution.Projects.SelectMany(project => project.Documents))
        {
            var included = scope == FixAllScope.Solution || changed.Project.Id == document.Project.Id && (scope != FixAllScope.Document || changed.Id == document.Id);
            if (!included) { Assert.Equal((await solution.GetDocument(changed.Id)!.GetTextAsync()).ToString(), (await changed.GetTextAsync()).ToString()); continue; }
            await StatementOperationTestFixture.Compiles(changed);
            var tree = await changed.GetSyntaxTreeAsync();
            Assert.DoesNotContain(await SpanTestFixture.Diagnostics(changed), item => item.Location.SourceTree == tree);
            var again = new FixAllContext(changed, provider, FixAllScope.Document, SpanTextCodeFixProvider.Key, provider.FixableDiagnosticIds, new Diagnostics(), timeout.Token);
            var repeated = await provider.GetFixAllProvider().GetFixAsync(again);
            var repeat = Assert.Single((await repeated!.GetOperationsAsync(timeout.Token)).OfType<ApplyChangesOperation>());
            Assert.Equal((await changed.GetTextAsync()).ToString(), (await repeat.ChangedSolution.GetDocument(changed.Id)!.GetTextAsync()).ToString());
        }
    }

    private static string Key(Diagnostic item) => item.Id + ":" + item.GetMessage() + ":" + item.Location.SourceTree!.GetText().ToString(item.Location.SourceSpan);
    private sealed class Diagnostics : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken token)
        { var tree = await document.GetSyntaxTreeAsync(token); return (await SpanTestFixture.Diagnostics(document)).Where(item => item.Location.SourceTree == tree); }
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken token) => Task.FromResult(Enumerable.Empty<Diagnostic>());
        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken token)
        { var result = new List<Diagnostic>(); foreach (var document in project.Documents) result.AddRange(await GetDocumentDiagnosticsAsync(document, token)); return result; }
    }
}
