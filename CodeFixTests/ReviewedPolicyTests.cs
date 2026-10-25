using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class ReviewedPolicyTests
{
    [Theory]
    [InlineData("record C(int State) { C M(C frame) => frame with { State = 1 }; }", 0)]
    [InlineData("record C(int State) { C M(C frame) => frame with { State = 1 + 2 + 3 }; }", 1)]
    [InlineData("class C { object Get(int x) => x; string M() { var value = (string)Get(1); return value; } }", 0)]
    [InlineData("class C { object Get(int x) => x; string M() { var value = (string)Get(1+2+3); return value; } }", 1)]
    public async Task RecordUpdatesAndLookupCastsRetainInnerOperationChecks(string source, int count)
    {
        var document = StatementOperationTestFixture.Document(source);
        Assert.Equal(count, (await StatementOperationTestFixture.Diagnostics(document)).Length);
    }

    [Fact]
    public async Task OrdinaryComputationPrefersLocalAndSimplifiesTypes()
    {
        var document = StatementOperationTestFixture.Document("using System.Text; class C { static string Use(StringBuilder value) => value.Capacity.ToString(); public static string Run() => Use(new StringBuilder(4)); }");
        var before = await StatementOperationTestFixture.Run(document);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.Equal(StatementOperationCodeFixProvider.ExpandKey, actions[0].EquivalenceKey);
        var operation = Assert.Single((await actions[0].GetOperationsAsync(default)).OfType<ApplyChangesOperation>());
        var changed = operation.ChangedSolution.GetDocument(document.Id)!;
        var source = (await changed.GetTextAsync()).ToString();
        Assert.DoesNotContain("global::", source);
        Assert.DoesNotContain("Calculate", source);
        Assert.Equal(before, await StatementOperationTestFixture.Run(await StatementOperationTestFixture.Reparse(changed)));
    }

    [Fact]
    public async Task ConditionalHelperUsesReturnsInsteadOfStagingTheDecision()
    {
        var document = StatementOperationTestFixture.Document("class C { static int Use(int value) => value; static int M(int month,int year) => Use(month >= 8 ? year : year - 1); }");
        var changed = await StatementOperationTestFixture.Fix(document, StatementOperationCodeFixProvider.ExtractKey);
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("return year;", text);
        Assert.Contains("return year - 1;", text);
        await StatementOperationTestFixture.Compiles(await StatementOperationTestFixture.Reparse(changed));
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
    }

    [Fact]
    public async Task SimplifiedTypeStillDisambiguatesAConflictingShortName()
    {
        var document = StatementOperationTestFixture.Document("class StringBuilder {} class C { static string Use(System.Text.StringBuilder value) => value.Capacity.ToString(); public static string Run() => Use(new System.Text.StringBuilder(4)); }");
        var changed = await StatementOperationTestFixture.Fix(document, StatementOperationCodeFixProvider.ExpandKey);
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("System.Text.StringBuilder", text);
        Assert.DoesNotContain("global::", text);
        Assert.Equal(await StatementOperationTestFixture.Run(document), await StatementOperationTestFixture.Run(await StatementOperationTestFixture.Reparse(changed)));
    }

    [Theory]
    [InlineData("bool value; if (flag) { value = true; } else { value = false; } return value;", 1)]
    [InlineData("bool value; if (flag) { value = true; } else { if (flag) { value = false; } else { value = flag; } } return value;", 1)]
    [InlineData("bool value = false; if (flag) { value = true; } return value;", 0)]
    [InlineData("bool value; if (flag) { value = true; } else { return false; } return value;", 0)]
    public async Task BranchingBooleanValuesHaveTheirOwnManualFinding(string body, int count)
    {
        var document = StatementOperationTestFixture.Document("class C { bool M(bool flag) { " + body + " } }");
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DecisionValueAnalyzer())).GetAnalyzerDiagnosticsAsync();
        Assert.Equal(count, diagnostics.Count(item => item.Id == "CR0303"));
        Assert.DoesNotContain(diagnostics, item => item.Id == "AD0001");
    }

    [Fact]
    public async Task WhitespaceAndAssessmentChecksRequireControlFlow()
    {
        var document = StatementOperationTestFixture.Document("class C { static bool IsAssessment(string text) => text.Length > 2; bool M(string text) => !string.IsNullOrWhiteSpace(text) && !IsAssessment(text); }");
        Assert.Contains(await InlineConditionTestFixture.Diagnostics(document), item => item.Id == "CR0203");
    }
    [Fact]
    public async Task StableParameterIsReusedButCapturedParameterIsFrozen()
    {
        var stable = StatementOperationTestFixture.Document("class C { static int Get(int value) => value + 1; static string Use(string text,int value) => text + value; public static string Run() { string text = \"before\"; return Use(text, Get(1)); } }");
        var changed = await StatementOperationTestFixture.Fix(stable, StatementOperationCodeFixProvider.ExpandKey);
        Assert.DoesNotContain("text2 = text", (await changed.GetTextAsync()).ToString());
        Assert.Equal(await StatementOperationTestFixture.Run(stable), await StatementOperationTestFixture.Run(await StatementOperationTestFixture.Reparse(changed)));
        var captured = StatementOperationTestFixture.Document("class C { static string Use(string text,int value) => text + value; public static string Run() { string text = \"before\"; int Get(int value) { text = \"after\"; return value; } return Use(text, Get(1)); } }");
        changed = await StatementOperationTestFixture.Fix(captured, StatementOperationCodeFixProvider.ExpandKey);
        Assert.Equal("before1", await StatementOperationTestFixture.Run(await StatementOperationTestFixture.Reparse(changed)));
    }

    [Fact]
    public async Task ComparerRepresentationOverloadsAreAllowedWithoutExemptingOtherEqualsMethods()
    {
        const string source = "using System; using System.Collections.Generic; class C : IEqualityComparer<string> { public bool Equals(string? x,string? y)=>x==y; public bool Equals(ReadOnlySpan<char> x,ReadOnlySpan<char> y)=>x.SequenceEqual(y); public int GetHashCode(string value)=>value.GetHashCode(); } class D { public bool Equals(int left,int right)=>left==right; public bool Equals(int left,string right)=>false; }";
        var document = StatementOperationTestFixture.Document(source);
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new OverloadConfigurationAnalyzer())).GetAnalyzerDiagnosticsAsync();
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("CR1001", diagnostic.Id));
    }

    [Fact]
    public async Task SingleMemberRecordUpdatesHaveNewlinesAndTrailingComma()
    {
        var document = StatementOperationTestFixture.Document("record C(int State) { C M(C frame) => frame with { State = 1 }; }");
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new InitializerCallLayoutAnalyzer())).GetAnalyzerDiagnosticsAsync();
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("CR0801", diagnostic.Id);
        var actions = new List<Microsoft.CodeAnalysis.CodeActions.CodeAction>();
        await new InitializerCallLayoutCodeFixProvider().RegisterCodeFixesAsync(new Microsoft.CodeAnalysis.CodeFixes.CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), default));
        var operation = Assert.Single((await Assert.Single(actions).GetOperationsAsync(default)).OfType<ApplyChangesOperation>());
        var changed = operation.ChangedSolution.GetDocument(document.Id)!;
        var source = (await changed.GetTextAsync()).ToString();
        Assert.Contains("with\n", source);
        Assert.Contains("State = 1,", source);
        await StatementOperationTestFixture.Compiles(await StatementOperationTestFixture.Reparse(changed));
    }
}
