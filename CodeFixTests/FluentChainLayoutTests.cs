using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class FluentChainLayoutTests
{
    [Fact]
    public async Task AcceptsTwoCallsOnlyWithinNinetyColumns()
    {
        var shortLine = StatementOperationTestFixture.Document("class C { C Next()=>this; C M(C x)=>x.Next().Next(); }");
        Assert.Empty(await Diagnostics(shortLine));
        var longLine = StatementOperationTestFixture.Document("class C { C Next()=>this; C M(C x)=>" + new string(' ', 65) + "x.Next().Next(); }");
        Assert.Single(await Diagnostics(longLine));
        var threeCalls = StatementOperationTestFixture.Document("class C { C Next()=>this; C M(C x)=>x.Next().Next().Next(); }");
        Assert.Equal(2, (await Diagnostics(threeCalls)).Length);
    }

    [Theory]
    [InlineData("using System.Linq; class C { int[] M(int[] values) => values.Where(x => x > 0).ToArray(); }", 1)]
    [InlineData("using System.Linq; class C { int[] M(int[] values) => values.Where(x => x > 0).Select(x => x + 1).ToArray(); }", 2)]
    [InlineData("using System.Linq; class C { int[] M(int[] values) => values.Where(x => x > 0)\n    .ToArray(); }", 0)]
    [InlineData("using System.Linq; class C { int[] M(int[] values) => values.ToArray(); }", 0)]
    [InlineData("class C { C Next()=>this; C M(C c) => c.Next()?.Next(); }", 0)]
    [InlineData("class C { C Next()=>this; C M(C c) => c?.Next().Next(); }", 0)]
    [InlineData("class C { static object Get()=>new C(); C Next()=>this; C M() => ((C)Get()).Next(); }", 0)]
    [InlineData("class C { static C[] Get()=>new C[1]; C Next()=>this; C M() => Get()[0].Next(); }", 0)]
    public async Task CountsOnlyCallsAfterTheFirst(string source, int count)
    {
        var document = StatementOperationTestFixture.Document(source);
        var diagnostics = await Diagnostics(document);
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("CR0805", diagnostic.Id));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task SavedChainsPreserveRuntimeAndConverge(string newline)
    {
        var source = "using System.Linq;\nclass C { public static string Run() => new[] {1,2,3}.Where(x => x > 1).Select(x => x * 2).Sum().ToString(); }".Replace("\n", newline);
        var document = StatementOperationTestFixture.Document(source);
        var before = await StatementOperationTestFixture.Run(document);
        for (var pass = 0; pass < 4; pass++)
        {
            var diagnostics = await Diagnostics(document);
            if (diagnostics.IsEmpty) break;
            var actions = await Actions(document, diagnostics[0]);
            var operation = Assert.Single((await Assert.Single(actions).GetOperationsAsync(default)).OfType<ApplyChangesOperation>());
            document = await StatementOperationTestFixture.Reparse(operation.ChangedSolution.GetDocument(document.Id)!);
        }
        Assert.Empty(await Diagnostics(document));
        Assert.Equal(before, await StatementOperationTestFixture.Run(document));
        var text = (await document.GetTextAsync()).ToString();
        Assert.Contains(newline + "    .Select", text);
        Assert.Contains(newline + "    .Sum", text);
        if (newline == "\n")
        {
            Assert.DoesNotContain("\r", text);
        }
        else
        {
            Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        }
    }

    [Fact]
    public async Task CallerArgumentTextRemainsObservableAndHasNoAutomaticFix()
    {
        const string source = "using System.Runtime.CompilerServices; class C { static C Next()=>new(); static string Observe(C value,[CallerArgumentExpression(\"value\")] string text=\"\")=>text; public static string Run()=>Observe(Next().ToString().Trim() is null ? new() : new()); }";
        var document = StatementOperationTestFixture.Document(source);
        var diagnostics = await Diagnostics(document);
        Assert.Equal(2, diagnostics.Length);
        foreach (var diagnostic in diagnostics)
        {
            Assert.Empty(await Actions(document, diagnostic));
        }
    }

    [Fact]
    public async Task CommentsKeepTheirAttachment()
    {
        var document = StatementOperationTestFixture.Document("class C { C Next()=>this; C M(C c)=>c.Next() /* keep */ .Next().Next(); }");
        var diagnostic = (await Diagnostics(document)).OrderBy(d => d.Location.SourceSpan.Start).First();
        Assert.Empty(await Actions(document, diagnostic));
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new FluentChainLayoutAnalyzer())).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0001");
        return diagnostics;
    }

    private static async Task<List<CodeAction>> Actions(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        await new FluentChainLayoutCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), default));
        return actions;
    }
}
