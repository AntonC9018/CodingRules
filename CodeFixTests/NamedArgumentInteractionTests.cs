using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace CodingRules;

public sealed class NamedArgumentInteractionTests
{
    [Fact]
    public async Task CachedActionDoesNotRedirectToNewCallAtOldSpan()
    {
        var document = NamedArgumentTestFixture.Document("class C { static int M(int x,int y) => x; static int F() => M(1,2); }");
        var workspace = document.Project.Solution.Workspace;
        Assert.True(workspace.TryApplyChanges(document.Project.Solution));
        document = workspace.CurrentSolution.GetDocument(document.Id)!;
        var action = Assert.Single(await NamedArgumentTestFixture.Actions(document, Assert.Single(await NamedArgumentTestFixture.Diagnostics(document))));
        var inserted = (await document.GetTextAsync()).ToString().Replace("static int F() => M(1,2);", "static int G() => M(3,4); static int F() => M(1,2);");
        Assert.True(workspace.TryApplyChanges(document.WithText(SourceText.From(inserted)).Project.Solution));
        var operation = Assert.Single((await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        Assert.Equal(inserted, (await operation.ChangedSolution.GetDocument(document.Id)!.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task CachedActionRechecksCurrentPolicyWithUnchangedSource()
    {
        var document = NamedArgumentTestFixture.Document("class C { static int M(int x,int y) => x; static int F() => M(1,2); }");
        var workspace = document.Project.Solution.Workspace;
        Assert.True(workspace.TryApplyChanges(document.Project.Solution));
        document = workspace.CurrentSolution.GetDocument(document.Id)!;
        var action = Assert.Single(await NamedArgumentTestFixture.Actions(document, Assert.Single(await NamedArgumentTestFixture.Diagnostics(document))));
        var compilation = (await document.Project.GetCompilationAsync())!;
        var id = DocumentationCommentId.CreateDeclarationId(compilation.GetTypeByMetadataName("C")!.GetMembers("M").Single());
        var updated = document.Project.AddAnalyzerConfigDocument(".editorconfig", SourceText.From("root=true\n[*.cs]\ndotnet_code_quality.CR0500.allow_positional_arguments = "
            + compilation.Assembly.Name + "::" + id), filePath: "/.editorconfig").Project.GetDocument(document.Id)!;
        Assert.True(workspace.TryApplyChanges(updated.Project.Solution));
        var operation = Assert.Single((await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        var changed = operation.ChangedSolution.GetDocument(document.Id)!;
        Assert.Equal((await updated.GetTextAsync()).ToString(), (await changed.GetTextAsync()).ToString());
        Assert.Empty(await NamedArgumentTestFixture.Diagnostics(changed));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FixAllPropagatesCancellationDuringDiagnosticRetrieval(bool cancelExecution)
    {
        var source = "class C { static int M(int x,int y) => x; static void F() {"
            + string.Concat(Enumerable.Range(0, 30).Select(index => "M(" + index + "," + (index + 1) + ");")) + "} }";
        var document = NamedArgumentTestFixture.Document(source);
        var diagnostics = await NamedArgumentTestFixture.Diagnostics(document);
        Assert.Equal(30, diagnostics.Length);
        using var execution = new CancellationTokenSource();
        using var contextCancellation = new CancellationTokenSource();
        var diagnosticProvider = new CancelDuringRetrieval(diagnostics, cancelExecution ? execution : contextCancellation);
        var provider = new NamedArgumentCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Document, NamedArgumentCodeFixProvider.Key,
            provider.FixableDiagnosticIds, diagnosticProvider, contextCancellation.Token);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action!.GetOperationsAsync(execution.Token));
        Assert.True(diagnosticProvider.ReceivedCanceledToken);
    }

    private sealed class CancelDuringRetrieval(ImmutableArray<Diagnostic> diagnostics, CancellationTokenSource cancellation) : FixAllContext.DiagnosticProvider
    {
        public bool ReceivedCanceledToken { get; private set; }
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken token)
        {
            Assert.False(token.IsCancellationRequested);
            cancellation.Cancel();
            ReceivedCanceledToken = token.IsCancellationRequested;
            return Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
        }
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken token) => Task.FromResult(Enumerable.Empty<Diagnostic>());
        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken token) => GetDocumentDiagnosticsAsync(project.Documents.First(), token);
    }

    [Theory]
    [InlineData("#pragma warning disable CR0500\n", "#pragma warning restore CR0500\n")]
    [InlineData("[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Readability\",\"CR0500\")]\n", "")]
    public async Task SuppressionRestoresNeighborAndKeepsOtherFamily(string before, string after)
    {
        var document = NamedArgumentTestFixture.Document("using System.Linq; class C {\n" + before
            + "static object Kept(int[] xs) => xs.ToDictionary(x=>x,x=>x);\n" + after
            + "static object Neighbor(int[] xs) => xs.ToDictionary(x=>x,x=>x); }");
        Assert.Single(await NamedArgumentTestFixture.Diagnostics(document));
        Assert.Equal(2, (await PipelineTestFixture.Diagnostics(document)).Length);
        var compilation = await document.Project.GetCompilationAsync();
        var all = await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new NamedArgumentAnalyzer()),
            new CompilationWithAnalyzersOptions(document.Project.AnalyzerOptions, null, true, false, reportSuppressedDiagnostics: true)).GetAnalyzerDiagnosticsAsync();
        // The SuppressMessage attribute is itself an accepted string/string call
        // and inherits the containing member's suppression.
        Assert.Equal(before.StartsWith("#", StringComparison.Ordinal) ? 2 : 3, all.Length);
        Assert.Equal(all.Length - 1, all.Count(diagnostic => diagnostic.IsSuppressed));
    }

    [Fact]
    public async Task SelectorOverlapSupportsEitherFixOrder()
    {
        var document = NamedArgumentTestFixture.Document("using System.Linq; class C { static object F(int[] xs) => xs.ToDictionary(x=>x,x=>x); }");
        Assert.Single(await NamedArgumentTestFixture.Diagnostics(document));
        Assert.Single(await PipelineTestFixture.Diagnostics(document));
        var namesFirst = await NamedArgumentTestFixture.Fix(document);
        Assert.Empty(await NamedArgumentTestFixture.Diagnostics(namesFirst));
        Assert.Empty(await PipelineTestFixture.Diagnostics(namesFirst));
        var selectorsFirst = await PipelineTestFixture.Fix(document, PipelineCodeFixProvider.SelectorsKey);
        Assert.Empty(await NamedArgumentTestFixture.Diagnostics(selectorsFirst));
        Assert.Empty(await PipelineTestFixture.Diagnostics(selectorsFirst));
    }

    [Fact]
    public async Task StaleDiagnosticReadsUpdatedPolicy()
    {
        var document = NamedArgumentTestFixture.Document("class C { static int M(int x,int y) => x; static int F() => M(1,2); }");
        var diagnostic = Assert.Single(await NamedArgumentTestFixture.Diagnostics(document));
        var compilation = await document.Project.GetCompilationAsync();
        var id = DocumentationCommentId.CreateDeclarationId(compilation!.GetTypeByMetadataName("C")!.GetMembers("M").Single());
        var updated = document.Project.AddAnalyzerConfigDocument(".editorconfig", SourceText.From("root=true\n[*.cs]\ndotnet_code_quality.CR0500.allow_positional_arguments = "
            + compilation.Assembly.Name + "::" + id), filePath: "/.editorconfig").Project.GetDocument(document.Id)!;
        Assert.Empty(await NamedArgumentTestFixture.Diagnostics(updated));
        Assert.Empty(await NamedArgumentTestFixture.Actions(updated, diagnostic));
        var cleared = updated.Project.RemoveAnalyzerConfigDocument(updated.Project.AnalyzerConfigDocuments.Single().Id).GetDocument(document.Id)!;
        Assert.Single(await NamedArgumentTestFixture.Diagnostics(cleared));
        Assert.Single(await NamedArgumentTestFixture.Actions(cleared, diagnostic));
    }

    [Theory]
    [InlineData(FixAllScope.Document)]
    [InlineData(FixAllScope.Project)]
    [InlineData(FixAllScope.Solution)]
    public async Task FixAllRediscoversNestedSitesPerScopeAndIsIdempotent(FixAllScope scope)
    {
        const string source = "class C { static int M(int x,int y) => x; static int F() => M(M(1,2),M(3,4)); }";
        var document = NamedArgumentTestFixture.Document(source);
        var second = document.Project.AddDocument("Second.cs", source.Replace("class C", "class D"), filePath: "/Second.cs");
        var otherProject = ProjectId.CreateNewId();
        var otherDocument = DocumentId.CreateNewId(otherProject);
        var solution = second.Project.Solution.AddProject(otherProject, "Other", "Other", LanguageNames.CSharp)
            .WithProjectMetadataReferences(otherProject, document.Project.MetadataReferences)
            .WithProjectParseOptions(otherProject, document.Project.ParseOptions!)
            .WithProjectCompilationOptions(otherProject, document.Project.CompilationOptions!)
            .AddDocument(otherDocument, "Other.cs", SourceText.From(source.Replace("class C", "class E")), filePath: "/Other.cs");
        document = solution.GetDocument(document.Id)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var provider = new NamedArgumentCodeFixProvider();
        var context = new FixAllContext(document, provider, scope, NamedArgumentCodeFixProvider.Key, provider.FixableDiagnosticIds, new Diagnostics(), timeout.Token);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        var operation = Assert.Single((await action!.GetOperationsAsync(timeout.Token)).OfType<ApplyChangesOperation>());
        foreach (var changed in operation.ChangedSolution.Projects.SelectMany(project => project.Documents))
        {
            var included = scope == FixAllScope.Solution || changed.Project.Id == document.Project.Id && (scope != FixAllScope.Document || changed.Id == document.Id);
            if (!included) { Assert.Equal((await solution.GetDocument(changed.Id)!.GetTextAsync()).ToString(), (await changed.GetTextAsync()).ToString()); continue; }
            await StatementOperationTestFixture.Compiles(changed);
            var tree = await changed.GetSyntaxTreeAsync();
            Assert.DoesNotContain(await NamedArgumentTestFixture.Diagnostics(changed), diagnostic => diagnostic.Location.SourceTree == tree);
            var again = new FixAllContext(changed, provider, FixAllScope.Document, NamedArgumentCodeFixProvider.Key, provider.FixableDiagnosticIds, new Diagnostics(), timeout.Token);
            var repeated = await provider.GetFixAllProvider().GetFixAsync(again);
            var repeatOperation = Assert.Single((await repeated!.GetOperationsAsync(timeout.Token)).OfType<ApplyChangesOperation>());
            Assert.Equal((await changed.GetTextAsync()).ToString(), (await repeatOperation.ChangedSolution.GetDocument(changed.Id)!.GetTextAsync()).ToString());
        }
    }

    [Fact]
    public async Task FixAllHonorsCancellation()
    {
        var document = NamedArgumentTestFixture.Document("class C { static int M(int x,int y) => x; static int F() => M(1,2); }");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = new NamedArgumentCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Document, NamedArgumentCodeFixProvider.Key, provider.FixableDiagnosticIds, new Diagnostics(), cancellation.Token);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action!.GetOperationsAsync(cancellation.Token));
    }

    private sealed class Diagnostics : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken token)
        {
            var tree = await document.GetSyntaxTreeAsync(token);
            return (await NamedArgumentTestFixture.Diagnostics(document)).Where(diagnostic => diagnostic.Location.SourceTree == tree);
        }
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken token) => Task.FromResult(Enumerable.Empty<Diagnostic>());
        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken token)
        {
            var result = new List<Diagnostic>();
            foreach (var document in project.Documents) result.AddRange(await GetDocumentDiagnosticsAsync(document, token));
            return result;
        }
    }
}
