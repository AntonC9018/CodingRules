using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class PipelineHostSafetyTests
{
    private const string Prelude="using System;using System.Linq;using System.Collections.Generic;class C{static int F(int x)=>x;";
    [Theory]
    [InlineData("static object M(int[] xs)=>xs.Select(x=>F(x)+1);")]
    [InlineData("object P=>new[]{1}.Select(x=>F(x)+1);")]
    [InlineData("object P{get{return new[]{1}.Select(x=>F(x)+1);}}")]
    [InlineData("static object M(int[] xs){object Local()=>xs.Select(x=>F(x)+1);return Local();}")]
    [InlineData("static object M(int[] xs){Func<object> outer=()=>xs.Select(x=>F(x)+1);return outer();}")]
    [InlineData("static object Value=new[]{1}.Select(x=>F(x)+1);")]
    [InlineData("object Value=new[]{1}.Select(x=>F(x)+1);")]
    [InlineData("object Value{get;}=new[]{1}.Select(x=>F(x)+1);")]
    [InlineData("static object M(int[] xs){foreach(var item in xs.Select(x=>F(x)+1)){}return xs;}")]
    [InlineData("static object M(int[] xs){for(int i=0;xs.Select(x=>F(x)+i).Any();i++){break;}return xs;}")]
    [InlineData("static object M(int[] xs){try{}catch(Exception ex) when(xs.Select(x=>F(x)+ex.HResult).Any()){}return xs;}")]
    [InlineData("static object M(object value)=>value switch{int[] xs when xs.Select(x=>F(x)+1).Any()=>xs,_=>value};")]
    public async Task AllHostsRetainTheOriginalStageInvocation(string member)
    {
        var document=PipelineTestFixture.Document(Prelude+member+"}");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic=Assert.Single(await PipelineTestFixture.Diagnostics(document));
        var actions=await PipelineTestFixture.Actions(document,diagnostic);
        Assert.NotEmpty(actions);
        foreach(var action in actions){var changed=await PipelineTestFixture.Fix(document,action.EquivalenceKey!);Assert.Empty(await PipelineTestFixture.Diagnostics(changed));}
    }
    [Theory]
    [InlineData("using System.Linq.Expressions;", "Expression<Func<int[],object>> tree=xs=>xs.Select(x=>F(x)+1);", "")]
    [InlineData("", "var p=new[]{1}.AsQueryable().Select(x=>F(x)+1);", "")]
    [InlineData("", "var p=new[]{1}.Select(x=>F(x)+ /*internal*/ 1);", "")]
    [InlineData("", "var p=new[]{1}.Select(x=>F(Line(x))+1);", "static int Line(int x,[System.Runtime.CompilerServices.CallerLineNumber]int line=0)=>x+line;")]
    [InlineData("", "var p=new[]{1}.Select(async x=>{var a=F(x);var b=F(a);await System.Threading.Tasks.Task.Yield();return b;});", "")]
    [InlineData("", "var p=new[]{1}.Select(x=>new Box{A=F(F(x))});", "class Box{public int A{get;init;}}")]
    [InlineData("", "S value=new();var p=new[]{1}.Select(x=>value.Update(F(x)));", "struct S{int state;public int Update(int x)=>state+=x;}")]
    [InlineData("", "var p=Use(new[]{1}.Select(x=>{var a=F(x);var b=F(a);return b;}));", "static object Use(object value,[System.Runtime.CompilerServices.CallerLineNumber]int line=0)=>value;")]
    [InlineData("", "var p=new[]{1}.Select(x=>{Func<int> nested=()=>Line(x);var a=F(x);var b=F(a);return b+nested();});", "static int Line(int x,[System.Runtime.CompilerServices.CallerLineNumber]int line=0)=>x+line;")]
    public async Task IndividualUnsafeMovesAreSilent(string imports,string body,string members)
    {
        var document=PipelineTestFixture.Document(imports+Prelude+members+"static void M(){"+body+"}}");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await PipelineTestFixture.Diagnostics(document));
    }
    [Fact]
    public async Task NestedCallsOutsideLambdasUseFrozenCR0300AndExpandedChainIsCompliant()
    {
        const string members="class C{static int Read(int x)=>x;static int Normalize(int x)=>x;static int Parse(int x)=>x;static void Consume(int x){}";
        var nested=PipelineTestFixture.Document(members+"static void M(int input){Consume(Parse(Normalize(Read(input))));}}");
        Assert.Empty(await PipelineTestFixture.Diagnostics(nested));
        Assert.Equal("CR0300",Assert.Single(await StatementOperationTestFixture.Diagnostics(nested)).Id);
        foreach(var key in new[]{StatementOperationCodeFixProvider.ExtractKey,StatementOperationCodeFixProvider.ExpandKey}){
            var saved=await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(nested,key));
            await StatementOperationTestFixture.Compiles(saved);Assert.Empty(await PipelineTestFixture.Diagnostics(saved,true));
        }
        var expanded=PipelineTestFixture.Document(members+"static void M(int input){var raw=Read(input);var normalized=Normalize(raw);var result=Parse(normalized);Consume(result);}}");
        Assert.Empty(await PipelineTestFixture.Diagnostics(expanded,true));
    }
    [Fact]
    public async Task IndexedWholeParameterBridgeIsSimpleAndNestedBridgeKeepsCR0300()
    {
        const string source="using System.Linq;class C{static int Project(int x,int index)=>x+index;sealed record Row(int Value);static object Simple(int[] xs)=>xs.Select((x,index)=>Project(x,index));static object Nested(int[] xs)=>xs.Select((x,index)=>new Row(Project(x,index)));}";
        var document=PipelineTestFixture.Document(source);
        Assert.Equal(new[]{"CR0400","CR0300"},(await PipelineTestFixture.Diagnostics(document,true)).Select(diagnostic=>diagnostic.Id));
        var changed=await PipelineTestFixture.Fix(document,PipelineCodeFixProvider.ExtractKey);
        Assert.Empty(await PipelineTestFixture.Diagnostics(changed,true));
    }
    [Fact]
    public async Task NarrowedNullableCapturePreservesItsFlowForBothActions()
    {
        const string source="using System.Linq;class C{static int F(int x)=>x;static object M(int[] xs,string? text){if(text is null)return xs;return xs.Select(x=>F(text.Length)+1);}}";
        var document=PipelineTestFixture.Document(source);await StatementOperationTestFixture.Compiles(document);
        var diagnostic=Assert.Single(await PipelineTestFixture.Diagnostics(document));
        var actions=await PipelineTestFixture.Actions(document,diagnostic);
        Assert.Equal(2,actions.Count);
        foreach(var action in actions){var changed=await PipelineTestFixture.Fix(document,action.EquivalenceKey!);Assert.Empty(await PipelineTestFixture.Diagnostics(changed));}
    }
}
