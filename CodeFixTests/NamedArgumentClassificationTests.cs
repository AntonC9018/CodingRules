using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CodingRules;

public sealed class NamedArgumentClassificationTests
{
    [Theory]
    [InlineData("M(1, 2)", "M(first: 1, second: 2)")]
    [InlineData("M(first, second)", "M(first: first, second: second)")]
    [InlineData("M(first: 1, 2)", "M(first: 1, second: 2)")]
    [InlineData("M(second: 2, first: 1)", null)]
    [InlineData("M(1)", null)]
    public async Task SuppliedDistinctParameters(string call, string? expected)
    {
        var document = NamedArgumentTestFixture.Document("class C { static int M(int first, int second = 0) => first + second; static int F(int first, int second) => " + call + "; }");
        var diagnostics = await NamedArgumentTestFixture.Diagnostics(document);
        if (expected is null) { Assert.Empty(diagnostics); return; }
        var diagnostic = Assert.Single(diagnostics);
        var source = (await document.GetTextAsync()).ToString();
        Assert.Equal(call.Substring(call.IndexOf('(')), source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        var changed = await NamedArgumentTestFixture.Fix(document, diagnostic);
        Assert.Contains(expected, (await changed.GetTextAsync()).ToString());
        Assert.Empty(await NamedArgumentTestFixture.Diagnostics(changed));
    }

    [Theory]
    [InlineData("static int M(int first, long second) => 0;", "M(1, 2)", false)]
    [InlineData("static int M<T,U>(T first, U second) => 0;", "M(1, 2)", true)]
    [InlineData("static int M(string? first, string second) => 0;", "M(null, \"x\")", true)]
    [InlineData("static int M((int x,int y) first, (int a,int b) second) => 0;", "M((1,2), (3,4))", true)]
    [InlineData("static int M(System.Collections.Generic.List<(int x,int y)> first, System.Collections.Generic.List<(int a,int b)> second) => 0;", "M(new System.Collections.Generic.List<(int,int)>(), new System.Collections.Generic.List<(int,int)>())", true)]
    [InlineData("static int M((int x,int y)[] first, (int a,int b)[] second) => 0;", "M(new (int,int)[0], new (int,int)[0])", true)]
    [InlineData("static int M((int x,int y)[] first, (int a,int b)[,] second) => 0;", "M(new (int,int)[0], new (int,int)[0,0])", false)]
    [InlineData("static int M(int first, int? second) => 0;", "M(1, 2)", false)]
    [InlineData("static int M(int first, params int[] rest) => 0;", "M(1, 2, 3)", false)]
    [InlineData("static int M(int first, int second, params string[] rest) => 0;", "M(1, 2, \"a\", \"b\")", true)]
    [InlineData("static int M(int[] first, params int[] rest) => 0;", "M(new int[0], new int[0])", true)]
    public async Task ConstructedTypesAndParams(string declaration, string call, bool positive)
    {
        var document = NamedArgumentTestFixture.Document("class C { " + declaration + " static int F() => " + call + "; }");
        var diagnostics = await NamedArgumentTestFixture.Diagnostics(document);
        if (!positive) { Assert.Empty(diagnostics); return; }
        await NamedArgumentTestFixture.Fix(document, Assert.Single(diagnostics));
    }

    [Fact]
    public async Task NamesCannotEnableRemappedOverload()
    {
        var document = NamedArgumentTestFixture.Document("""
            class V { public static implicit operator V(int value) => new(); public static implicit operator V(bool value) => new(); }
            class C { static string M(V x,V y) => "original"; static string M(bool y,int x) => "swapped"; static string F() => M(1,true); }
            """);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await NamedArgumentTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("int", false)]
    [InlineData("long", true)]
    public async Task ConstructedContainingTypesRemainDistinct(string firstOuter, bool positive)
    {
        var document = NamedArgumentTestFixture.Document("class Outer<T> { public class Inner<U> {} } class C { static int M(Outer<" + firstOuter
            + ">.Inner<(int x,int y)> first, Outer<long>.Inner<(int a,int b)> second) => 0; static int F() => M(new Outer<"
            + firstOuter + ">.Inner<(int,int)>(),new Outer<long>.Inner<(int,int)>()); }");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostics = await NamedArgumentTestFixture.Diagnostics(document);
        if (!positive) { Assert.Empty(diagnostics); return; }
        await NamedArgumentTestFixture.Fix(document, Assert.Single(diagnostics));
    }

    [Theory]
    [InlineData("System.Math.Max(1,2)")]
    [InlineData("System.MathF.Min(1f,2f)")]
    [InlineData("string.Equals(\"a\",\"b\")")]
    [InlineData("string.Equals(\"a\",\"b\",System.StringComparison.Ordinal)")]
    [InlineData("\"a\".Replace('a','b')")]
    [InlineData("\"a\".Replace(\"a\",\"b\")")]
    [InlineData("\"a\".Replace(\"a\",\"b\",System.StringComparison.Ordinal)")]
    [InlineData("\"a\".Replace(\"a\",\"b\",true,System.Globalization.CultureInfo.InvariantCulture)")]
    [InlineData("System.IO.Path.Combine(\"a\",\"b\",\"c\",\"d\")")]
    public async Task ExactFrameworkExceptions(string call)
    {
        var document = NamedArgumentTestFixture.Document("class C { static object F() => " + call + "; }");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await NamedArgumentTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("System.IO.Path.GetRelativePath(\"a\",\"b\")")]
    [InlineData("System.Math.Clamp(1,2,3)")]
    [InlineData("System.Text.Encoding.UTF8.GetString(new byte[4],1,2)")]
    [InlineData("Xunit.Assert.Equal(\"a\",\"b\")")]
    [InlineData("Math.Max(1,2)")]
    [InlineData("Path.Combine(\"a\",\"b\")")]
    [InlineData("Other.Replace('a','b')")]
    [InlineData("Other.Equals(\"a\",\"b\")")]
    public async Task NoGuessedExceptions(string call)
    {
        var document = NamedArgumentTestFixture.Document("""
            static class Math { public static int Max(int a,int b) => a; }
            static class Path { public static string Combine(string a,string b) => a; }
            static class Other { public static char Replace(char a,char b) => a; public static bool Equals(string a,string b) => false; }
            class C { static void F() { CALL; } }
            """.Replace("CALL", call));
        await NamedArgumentTestFixture.Fix(document, Assert.Single(await NamedArgumentTestFixture.Diagnostics(document)));
    }

    [Fact]
    public async Task OldLanguageCompletesLaterArgumentsWithoutChangingExpressions()
    {
        var document = NamedArgumentTestFixture.Document("class C { static int M(int @event, int second, string tail) => 0; static int F() => M(/*a*/1, /*b*/2, /*c*/\"tail\"); }");
        document = document.Project.WithParseOptions(new CSharpParseOptions(LanguageVersion.CSharp7_1))
            .WithCompilationOptions(new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary)).GetDocument(document.Id)!;
        var changed = await NamedArgumentTestFixture.Fix(document);
        Assert.Contains("M(/*a*/@event: 1, /*b*/second: 2, /*c*/tail: \"tail\")", (await changed.GetTextAsync()).ToString());
    }
}
