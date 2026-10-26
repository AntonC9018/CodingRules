using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class DecisionValueCodeFixTests
{
    [Fact]
    public async Task SavedExtractionPreservesLazyCallsAndCapturedState()
    {
        var document = StatementOperationTestFixture.Document("""
            public class C
            {
                public static string Run()
                {
                    string trace = "";
                    bool Check() { trace += "check;"; return false; }
                    bool First() { trace += "first;"; return true; }
                    bool Second() { trace += "second;"; return false; }
                    bool result;
                    if (Check()) { result = First(); }
                    else { result = Second(); }
                    return trace + result;
                }
            }
            """);
        var before = await StatementOperationTestFixture.Run(document);
        var diagnostic = Assert.Single(await Diagnostics(document));
        var actions = await Actions(document, diagnostic);
        var operation = Assert.Single((await Assert.Single(actions).GetOperationsAsync(default)).OfType<ApplyChangesOperation>());
        var changed = await StatementOperationTestFixture.Reparse(operation.ChangedSolution.GetDocument(document.Id)!);
        Assert.Equal("check;second;False", before);
        Assert.Equal(before, await StatementOperationTestFixture.Run(changed));
        Assert.Empty(await Diagnostics(changed));
        Assert.Contains("bool CalculateResult()", (await changed.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task DecisionExtractionUsesEarlyReturnsWithoutElse()
    {
        var document = StatementOperationTestFixture.Document("class C { static bool M(bool a, bool b) { bool result; if(a) { result=true; } else { if(b) { result=true; } else { result=false; } } return result; } }");
        var action = Assert.Single(await Actions(document, Assert.Single(await Diagnostics(document))));
        var operation = Assert.Single((await action.GetOperationsAsync(default)).OfType<ApplyChangesOperation>());
        var changed = operation.ChangedSolution.GetDocument(document.Id)!;
        await StatementOperationTestFixture.Compiles(changed);
        Assert.DoesNotContain("else", (await changed.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task NestedConjunctionExtractionUsesFlatEarlyReturns()
    {
        var document = StatementOperationTestFixture.Document("class C { static bool M(bool a,bool b) { bool result; if(a) { if(b) {result=true;} else {result=false;} } else {result=false;} return result; } }");
        var action = Assert.Single(await Actions(document, Assert.Single(await Diagnostics(document))));
        var operation = Assert.Single((await action.GetOperationsAsync(default)).OfType<ApplyChangesOperation>());
        var changed = operation.ChangedSolution.GetDocument(document.Id)!;
        await StatementOperationTestFixture.Compiles(changed);
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("if (!a)", text);
        Assert.DoesNotContain("else", text);
        var root = (await changed.GetSyntaxRootAsync())!;
        Assert.All(root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax>(),
            decision => Assert.DoesNotContain(decision.Statement.DescendantNodes(),
                child => child is Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax));
    }

    [Theory]
    [InlineData("using System.Runtime.CompilerServices; class C { static bool Check([CallerMemberName] string name=\"\") => true; bool M() { bool value; if(Check()) { value=true; } else { value=false; } return value; } }")]
    [InlineData("struct C { bool flag; bool M() { bool value; if(flag) { value=true; } else { value=false; } return value; } }")]
    [InlineData("class C { static bool Check(out int found) { found=1; return true; } int M() { bool value; if(Check(out var found)) { value=true; } else { value=false; } return value ? found : 0; } }")]
    public async Task UnsafeCaptureAndObservableCallerCasesRemainManual(string source)
    {
        var document = StatementOperationTestFixture.Document(source);
        var diagnostic = Assert.Single(await Diagnostics(document));
        Assert.Empty(await Actions(document, diagnostic));
    }

    [Fact]
    public async Task FixAllReplansCollidingNamesInSavedSource()
    {
        var document = StatementOperationTestFixture.Document("class C { static bool M(bool input) { bool v; if(input) { v=true; } else { v=false; } bool V; if(v) { V=false; } else { V=true; } return V; } }");
        var provider = new DecisionValueCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Document, DiagnosticIds.DecisionValueFunction,
            provider.FixableDiagnosticIds, new Provider(), default);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        var operation = Assert.Single((await action!.GetOperationsAsync(default)).OfType<ApplyChangesOperation>());
        var changed = await StatementOperationTestFixture.Reparse(operation.ChangedSolution.GetDocument(document.Id)!);
        await StatementOperationTestFixture.Compiles(changed);
        Assert.Empty(await Diagnostics(changed));
        Assert.Contains("CalculateV2", (await changed.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task ReportsElseAfterExitAndPreservesBranchRuntime()
    {
        var document = StatementOperationTestFixture.Document("class C { public static string Run() { if(false) { return \"bad\"; } else { return \"good\"; } } }");
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new EarlyReturnLayoutAnalyzer())).GetAnalyzerDiagnosticsAsync();
        var actions = new List<CodeAction>();
        await new EarlyReturnLayoutCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document,
            Assert.Single(diagnostics), (action, _) => actions.Add(action), default));
        var operation = Assert.Single((await Assert.Single(actions).GetOperationsAsync(default)).OfType<ApplyChangesOperation>());
        var changed = operation.ChangedSolution.GetDocument(document.Id)!;
        Assert.DoesNotContain("else", (await changed.GetTextAsync()).ToString());
        Assert.Equal("good", await StatementOperationTestFixture.Run(changed));
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DecisionValueAnalyzer())).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0001");
        return diagnostics;
    }

    private static async Task<List<CodeAction>> Actions(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        await new DecisionValueCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), default));
        return actions;
    }

    private sealed class Provider : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) => await Diagnostics(document);
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Diagnostic>());
        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            var results = new List<Diagnostic>();
            foreach (var document in project.Documents)
            {
                results.AddRange(await Diagnostics(document));
            }
            return results;
        }
    }
}
