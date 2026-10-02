using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class PipelineReviewRegressionTests
{
    private const string Prelude = "using System;using System.Linq;using System.Runtime.CompilerServices;class C{static int F(int x)=>x;";

    [Theory]
    [InlineData("new Box(new[]{1}.Select(x=>F(x)+1)).Text")]
    [InlineData("new Box(new[]{1}.Select(x=>{var a=F(x);var b=F(a);return b;})).Text")]
    [InlineData("new Box(new[]{1}.ToDictionary(x=>x)).Text")]
    [InlineData("Inspect(new[]{1}.ToDictionary(x=>x,x=>x))")]
    [InlineData("Inspect(new[]{1}.ToDictionary(x=>x))")]
    [InlineData("Inspect(new[]{1}.Select(x=>F(x)+1))")]
    public async Task EnclosingConstructorAndMethodCallerTextSitesAreOmitted(string expression)
    {
        var source = Prelude + "sealed class Box{public string Text;public Box(object value,[CallerArgumentExpression(\"value\")]string text=\"\"){Text=text;}}"
            + "static string Inspect(object value,[CallerArgumentExpression(\"value\")]string text=\"\")=>text;public static string Run()=>" + expression + ";}";
        var document = PipelineTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await PipelineTestFixture.Diagnostics(document));
        Assert.Equal(await StatementOperationTestFixture.Run(document), await StatementOperationTestFixture.Run(await StatementOperationTestFixture.Reparse(document)));
    }

    [Theory]
    [InlineData("return Line().ToString();")]
    [InlineData("return new Box().Line.ToString();")]
    public async Task LaterCallerLinesBeyondTheSelectedRootAreOmitted(string completion)
    {
        var source = Prelude + "static int Line([CallerLineNumber]int line=0)=>line;sealed class Box{public int Line;public Box([CallerLineNumber]int line=0){Line=line;}}"
            + "public static string Run(){\nvar p=new[]{1}.Select(x=>F(x)+1);\n" + completion + "\n}}";
        var document = PipelineTestFixture.Document(source);
        Assert.Equal("3", await StatementOperationTestFixture.Run(document));
        Assert.Empty(await PipelineTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("new Box{Value=F(F(x))}", "sealed class Box{public required int Value{get;set;}}")]
    [InlineData("new Box{Value=F(F(x))}", "class Base{public required int Value{get;set;}}sealed class Box:Base{}")]
    public async Task RequiredMemberAggregateIsAnIndividualSilentOmission(string expression, string members)
    {
        var document = PipelineTestFixture.Document(Prelude + members + "public static string Run()=>new[]{1}.Select(x=>" + expression + ").Single().Value.ToString();}");
        Assert.Equal("1", await StatementOperationTestFixture.Run(document));
        Assert.Empty(await PipelineTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("return Use(new[]{1}.ToDictionary(x=>x)).ToString();", "static int Use(object value)=>1;")]
    [InlineData("var p=new[]{new{Id=1}}.ToDictionary(x=>x.Id);return p.Single().Value.Id.ToString();", "")]
    [InlineData("var p=new[]{new{Id=1}}.ToLookup(x=>x.Id);return p.Single().Single().Id.ToString();", "")]
    public async Task NestedAndAnonymousMaterializersHaveSavedSafeActions(string body, string members)
    {
        var document = PipelineTestFixture.Document(Prelude + members + "public static string Run(){" + body + "}}");
        await SavedSame(document, PipelineCodeFixProvider.SelectorsKey, "CR0402");
    }

    [Theory]
    [InlineData("var p=new[]{1}.Select(@event=>{var a=F(@event);var b=F(a);return b;});return p.Single().ToString();", "")]
    [InlineData("Action outer=()=>new[]{1}.Select(x=>{var a=F(x);var b=F(a);return b;});outer();return \"ok\";", "")]
    [InlineData("new C();return \"ok\";", "C()=>new[]{1}.Select(x=>{var a=F(x);var b=F(a);return b;});")]
    public async Task EscapedParametersAndVoidEnclosingCompletionRemainBound(string body, string members)
    {
        var document = PipelineTestFixture.Document(Prelude + members + "public static string Run(){" + body + "}}");
        await SavedSame(document, PipelineCodeFixProvider.ExtractKey, "CR0401");
    }

    [Theory]
    [InlineData(PipelineCodeFixProvider.ExtractKey)]
    [InlineData(PipelineCodeFixProvider.BlockKey)]
    public async Task InitializerFixDoesNotFormatUnrelatedCallerText(string key)
    {
        var document = PipelineTestFixture.Document(Prelude + "static object p=new[]{1}.Select(x=>F(x)+1);static string Inspect(int value,[CallerArgumentExpression(\"value\")]string text=\"\")=>text;public static string Run()=>Inspect(1+2);}");
        Assert.Equal("1+2", await StatementOperationTestFixture.Run(document));
        var changed = await SavedSame(document, key, "CR0400");
        Assert.Contains("Run()=>Inspect(1+2)", (await changed.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(PipelineCodeFixProvider.ExtractKey)]
    [InlineData(PipelineCodeFixProvider.BlockKey)]
    public async Task ExplicitOriginalCallerDefaultsPermitStageActions(string key)
    {
        var document = PipelineTestFixture.Document(Prelude + "sealed class Box{public string Text;public Box(object value,[CallerArgumentExpression(\"value\")]string text=\"\"){Text=text;}}static int Line([CallerLineNumber]int line=0)=>line;public static string Run(){\nvar box=new Box(new[]{1}.Select(x=>F(x)+1),text:\"original\");\nreturn box.Text+Line(line:3);\n}}");
        await SavedSame(document, key, "CR0400");
    }

    [Fact]
    public async Task EarlierCallerLineDoesNotExcludeAnUnaffectedStage()
    {
        var document = PipelineTestFixture.Document(Prelude + "static int Line([CallerLineNumber]int line=0)=>line;public static string Run(){\nvar line=Line();\nvar p=new[]{1}.Select(x=>F(x)+1);\nreturn line.ToString();\n}}");
        await SavedSame(document, PipelineCodeFixProvider.ExtractKey, "CR0400");
    }

    private static async Task<Microsoft.CodeAnalysis.Document> SavedSame(Microsoft.CodeAnalysis.Document document, string key, string id)
    {
        var before = await StatementOperationTestFixture.Run(document);
        var changed = await PipelineTestFixture.Fix(document, key, id);
        Assert.Empty(await PipelineTestFixture.Diagnostics(changed));
        Assert.Equal(before, await StatementOperationTestFixture.Run(changed));
        return changed;
    }
}
