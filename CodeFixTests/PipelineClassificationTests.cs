using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class PipelineClassificationTests
{
    private const string Prelude = "using System; using System.Linq; using System.Collections.Generic; using System.Collections.Immutable; class C { static int F(int x)=>x; sealed record Item(int Id, int Name); ";
    [Theory]
    [InlineData("xs.Select(x=>x)")]
    [InlineData("xs.Select(x=>x+1)")]
    [InlineData("xs.Select(x=>F(x))")]
    [InlineData("xs.Select(x=>new Item(x,x))")]
    [InlineData("xs.Select(x=>(x,x))")]
    [InlineData("xs.Select(x=>(x+1,x+2))")]
    [InlineData("xs.Select(x=>new { A=F(x), B=F(x) })")]
    [InlineData("xs.Select(x=>xs.Where(y=>y>0).Select(y=>F(y)).ToArray())")]
    [InlineData("xs.Select(x=>F(Get()))")]
    [InlineData("xs.Where(x=>x>0).OrderBy(x=>x).Select(x=>x+1).Where(x=>x>0).Select(x=>F(x)).ToArray()")]
    [InlineData("from x in xs select new Item(F(x),x)")]
    [InlineData("xs.AsQueryable().Select(x=>new Item(F(x),x))")]
    [InlineData("xs.Select(x=>{var a=F(x); return new Item(a,x);})")]
    [InlineData("xs.Select(x=>{var a=x;var b=a;return b;})")]
    [InlineData("xs.Select(x=>{if(x<0)throw new Exception();return x;})")]
    [InlineData("xs.Select(x=>{if(x<0)return 1;else if(x>0)return 2;return x;})")]
    [InlineData("xs.Select(x=>{Func<int,int> nested=y=>{var a=F(y);var b=F(a);return b;};return nested(x);})")]
    public async Task SimpleLinearAndIndependentStagesAreAllowed(string expression)
    {
        var document = PipelineTestFixture.Document(Prelude + "static int Get()=>1; static object M(int[] xs)=>" + expression + "; }");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await PipelineTestFixture.Diagnostics(document));
    }
    [Theory]
    [InlineData("xs.Select(x=>new Item(F(x),x))", "CR0400")]
    [InlineData("xs.Select((x,i)=>new Item(F(x),i))", "CR0400")]
    [InlineData("Enumerable.Select(xs,static x=>new Item(F(x),x))", "CR0400")]
    [InlineData("xs.ToImmutableArray().Select(x=>new Item(F(x),x))", "CR0400")]
    [InlineData("xs.Select(x=>F(x)+1)", "CR0400")]
    [InlineData("xs.Select(x=>(F(F(x)),x))", "CR0400")]
    [InlineData("xs.Select(x=>new Box { A=F(F(x)), B=x })", "CR0400")]
    [InlineData("xs.Select(x=>{var a=F(x);var b=F(a);return new Item(b,x);})", "CR0401")]
    [InlineData("xs.Select(x=>{if(x>0){if(x<10)return 1;}return 0;})", "CR0401")]
    public async Task CompositionsAndClosedStageImplementationsHaveSavedSourceFixes(string expression, string id)
    {
        var document = PipelineTestFixture.Document(Prelude + "sealed class Box { public int A { get; set; } public int B { get; set; } } static object M(int[] xs)=>" + expression + "; }");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await PipelineTestFixture.Diagnostics(document));
        Assert.Equal(id, diagnostic.Id);
        var actions = await PipelineTestFixture.Actions(document, diagnostic);
        Assert.Equal(id == "CR0400" ? 2 : 1, actions.Count);
        foreach (var action in actions)
        {
            var changed = await PipelineTestFixture.Fix(document, action.EquivalenceKey!);
            Assert.Empty(await PipelineTestFixture.Diagnostics(changed));
            if (id == "CR0400") Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        }
    }
    [Theory]
    [InlineData("xs.ToDictionary(x=>x)")]
    [InlineData("xs.ToDictionary(x=>x, EqualityComparer<int>.Default)")]
    [InlineData("xs.ToImmutableArray().ToDictionary(x=>x)")]
    [InlineData("xs.ToImmutableArray().ToDictionary(x=>x, EqualityComparer<int>.Default)")]
    [InlineData("xs.ToImmutableArray().ToDictionary(x=>x,x=>x+1)")]
    [InlineData("xs.ToImmutableArray().ToDictionary(x=>x,x=>x+1,EqualityComparer<int>.Default)")]
    [InlineData("xs.ToDictionary(x=>x,x=>x+1)")]
    [InlineData("xs.ToDictionary(x=>x,x=>x+1,EqualityComparer<int>.Default)")]
    [InlineData("Enumerable.ToDictionary(xs,x=>x)")]
    [InlineData("Enumerable.ToDictionary<int,int>(xs,x=>x,EqualityComparer<int>.Default)")]
    [InlineData("xs.ToLookup(x=>x)")]
    [InlineData("xs.ToLookup(x=>x, EqualityComparer<int>.Default)")]
    [InlineData("xs.ToLookup(x=>x,x=>x+1)")]
    [InlineData("xs.ToLookup(x=>x,x=>x+1,EqualityComparer<int>.Default)")]
    [InlineData("xs.ToImmutableDictionary(x=>x)")]
    [InlineData("xs.ToImmutableDictionary(x=>x,EqualityComparer<int>.Default)")]
    [InlineData("xs.ToImmutableDictionary(x=>x,x=>x+1)")]
    [InlineData("xs.ToImmutableDictionary(x=>x,x=>x+1,EqualityComparer<int>.Default)")]
    [InlineData("xs.ToImmutableDictionary(x=>x,x=>x+1,EqualityComparer<int>.Default,EqualityComparer<int>.Default)")]
    [InlineData("xs.ToImmutableSortedDictionary(x=>x,x=>x+1)")]
    [InlineData("xs.ToImmutableSortedDictionary(x=>x,x=>x+1,Comparer<int>.Default)")]
    [InlineData("xs.ToImmutableSortedDictionary(x=>x,x=>x+1,Comparer<int>.Default,EqualityComparer<int>.Default)")]
    public async Task ActualMaterializerOverloadMatrix(string expression)
    {
        var document = PipelineTestFixture.Document(Prelude + "static object M(int[] xs)=>" + expression + "; }");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Equal("CR0402", Assert.Single(await PipelineTestFixture.Diagnostics(document)).Id);
        var changed = await PipelineTestFixture.Fix(document, PipelineCodeFixProvider.SelectorsKey);
        Assert.Empty(await PipelineTestFixture.Diagnostics(changed));
        Assert.Contains("keySelector:", (await changed.GetTextAsync()).ToString());
        Assert.Contains("elementSelector:", (await changed.GetTextAsync()).ToString());
    }
    [Theory]
    [InlineData("xs.ToDictionary(keySelector:x=>x,elementSelector:x=>x)")]
    [InlineData("xs.ToImmutableDictionary(keySelector:x=>x,elementSelector:x=>x)")]
    [InlineData("pairs.ToImmutableDictionary()")]
    [InlineData("pairs.ToImmutableDictionary(EqualityComparer<int>.Default)")]
    [InlineData("pairs.ToImmutableSortedDictionary()")]
    [InlineData("pairs.ToDictionary()")]
    [InlineData("ImmutableDictionary<int,int>.Empty.ToBuilder().ToImmutableDictionary()")]
    public async Task ExplicitSelectorsAndUnprovedIdentityFastPathsAreAllowed(string expression)
    {
        var document = PipelineTestFixture.Document(Prelude + "static object M(int[] xs,IEnumerable<KeyValuePair<int,int>> pairs)=>" + expression + "; }");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await PipelineTestFixture.Diagnostics(document));
    }
    [Theory]
    [InlineData(false,"netstandard.dll")]
    [InlineData(true,"netstandard.dll")]
    [InlineData(false,"netstandard2.1.dll")]
    [InlineData(true,"netstandard2.1.dll")]
    public async Task RealNetstandardDefinitionsAndSourceLookalikes(bool shadow,string reference)
    {
        var source = "using System.Linq; class C { static int F(int x)=>x; static object M(int[] xs)=>xs.Select(x=>F(x)+1); }"
            + (shadow ? "namespace System.Linq {static class Enumerable{public static System.Collections.Generic.IEnumerable<int> Select(this int[] xs,System.Func<int,int> selector)=>xs;}}" : "");
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Test", new[] { tree }, new[] { MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "References",reference)) }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PipelineAnalyzer())).GetAnalyzerDiagnosticsAsync();
        if (shadow) Assert.Empty(diagnostics); else Assert.Equal("CR0400", Assert.Single(diagnostics).Id);
    }
}
