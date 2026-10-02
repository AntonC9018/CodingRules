using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class StatementOperationSafetyTests
{
    [Theory]
    [InlineData("Use(Get(1, line: 7))", "using System.Runtime.CompilerServices;", "static int Get(int x, [CallerLineNumber] int line = 0) => x;")]
    [InlineData("Use(callback(1))", "using System;", "static Func<int,int> callback = x => x;")]
    [InlineData("Use(new Item(1))", "", "record Item(int Value); static int Use(Item x) => x.Value;")]
    [InlineData("UseItem(new(1))", "", "record Item(int Value); static int UseItem(Item x) => x.Value;")]
    [InlineData("Use(System.Convert.ToInt32(\"1\"))", "", "")]
    [InlineData("Use(Extensions.Get(1))", "", "")]
    public async Task ResolvedBindingsAndExplicitCallerConstantsHaveSavedFixes(string expression, string imports, string declarations)
    {
        var source = imports + "static class Extensions { public static int Get(this int x, int optional=0) => x; } class C { static int Use(int x)=>x; "
            + declarations + " static int M() { return " + expression + "; } }";
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var actions = await StatementOperationTestFixture.Actions(document, (await StatementOperationTestFixture.Diagnostics(document))[0]);
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, action.EquivalenceKey!));
            await StatementOperationTestFixture.Compiles(changed);
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        }
    }

    [Theory]
    [InlineData("Use(1.Get())")]
    [InlineData("Use(Get())")]
    public async Task ReducedReceiverAndOmittedCallerDefaultsDoNotCountAsArguments(string expression)
    {
        var source = "using System.Runtime.CompilerServices; static class Extensions { public static int Get(this int x, int optional=0)=>x; } class C { static int Use(int x)=>x; static int Get([CallerLineNumber] int line=0)=>line; static int M()=>" + expression + "; }";
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("Use(Get(1))", "static int Get(int x, [System.Runtime.CompilerServices.CallerArgumentExpression(\"x\")] string? text=null)=>x;")]
    [InlineData("Use(Get(1))", "static int Get(int x, [System.Runtime.CompilerServices.CallerMemberName] string member=\"\")=>x;")]
    [InlineData("Use((a ?? Get(1).ToString()).Length)", "static int Get(int x)=>x;")]
    public async Task AffectedCallerAndUnknownValueShapesAreIndividualSkips(string expression, string declarations)
    {
        var document = StatementOperationTestFixture.Document("class C { static int Use(int x)=>x; " + declarations + " static int M(string? a)=>" + expression + "; }");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task OutVariableUsedLaterKeepsItsOriginalScopeThroughExpansion()
    {
        const string source = "class C { static int Use(int x)=>x; static int M(string text) { int result = Use(int.TryParse(text, out var value) ? value : 0); return result + value; } }";
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.Equal(StatementOperationCodeFixProvider.ExpandKey, Assert.Single(actions).EquivalenceKey);
        var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, StatementOperationCodeFixProvider.ExpandKey));
        await StatementOperationTestFixture.Compiles(changed);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
    }

    [Fact]
    public async Task MutableStructReceiverStaysAtTheOriginalCallLocation()
    {
        const string source = "struct S { public void Use(int x) { } } class C { static int Get(int x)=>x; static void M(ref S value) { value.Use(Get(1)); } }";
        var document = StatementOperationTestFixture.Document(source);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.Equal(StatementOperationCodeFixProvider.ExtractKey, Assert.Single(actions).EquivalenceKey);
        var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, StatementOperationCodeFixProvider.ExtractKey));
        await StatementOperationTestFixture.Compiles(changed);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
    }

    [Fact]
    public async Task BrokenSourceDoesNotCrashTheAnalyzer()
    {
        var document = StatementOperationTestFixture.Document("class C { int M() => Missing(Get(,)); }");
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData(StatementOperationCodeFixProvider.ExtractKey)]
    [InlineData(StatementOperationCodeFixProvider.ExpandKey)]
    public async Task FormattingHonorsEditorconfigAndSavedNewlines(string key)
    {
        const string source = "class C\r\n{\r\n    static int Use(int x) => x;\r\n    static int M(int a,int b,int c)\r\n    {\r\n        return Use((a+b)*c);\r\n    }\r\n}\r\n";
        var document = StatementOperationTestFixture.Document(source, "root = true\n[*.cs]\nindent_style = tab\nindent_size = 4\nend_of_line = lf\ncsharp_new_line_before_open_brace = all\n");
        var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, key));
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("\n\t", text);
        await StatementOperationTestFixture.Compiles(changed);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
    }
}
