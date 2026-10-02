using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class PipelineRuntimeTests
{
    [Theory]
    [InlineData(PipelineCodeFixProvider.ExtractKey)]
    [InlineData(PipelineCodeFixProvider.BlockKey)]
    public async Task LazyIndexedCapturesRepeatedEnumerationAndDisposal(string key)
    {
        const string source = """
            using System; using System.Linq; using System.Collections.Generic;
            class C {
                static List<string> trace=new(); static int capture;
                sealed record Item(int Value,int Index,int Later);
                static int F(int x){trace.Add("F"+x+":"+capture);capture++;return x+capture;}
                static int Later {get{trace.Add("later"+capture);return capture;}}
                static IEnumerable<int> Source(){try{trace.Add("start");yield return 3;yield return 7;}finally{trace.Add("dispose");}}
                static IEnumerable<int> Get(){trace.Add("source");return Source();}
                public static string Run(){
                    trace.Clear();capture=1;
                    var sequence=Get().Select((x,index)=>new Item(F(x),index,Later));
                    trace.Add("constructed");capture=10;
                    using(var iterator=sequence.GetEnumerator()){trace.Add("iterator");iterator.MoveNext();trace.Add(iterator.Current.ToString());}
                    capture=20; foreach(var value in sequence)trace.Add(value.ToString());
                    return string.Join("|",trace);
                }
            }
            """;
        await Same(source, key, "CR0400");
    }
    [Theory]
    [InlineData(PipelineCodeFixProvider.ExtractKey)]
    [InlineData(PipelineCodeFixProvider.BlockKey)]
    public async Task ReachedBranchesConversionsAndCheckedOverflow(string key)
    {
        const string source = """
            using System; using System.Linq; using System.Collections.Generic;
            class C {
                static List<string> trace=new(); static int F(int x){trace.Add("F"+x);return x;}
                static int Throw(int x){trace.Add("throw");throw new InvalidOperationException();}
                sealed class Item{public Item(long value){trace.Add("item"+value);}}
                public static string Run(){trace.Clear();checked {
                    var sequence=new[]{0,1,int.MaxValue}.Select(x=>new Item(x==0 ? F(x) : F(x)+1));
                    try{foreach(var item in sequence)trace.Add("next");}catch(OverflowException){trace.Add("overflow");}
                    var lazy=new[]{0}.Select(x=>new Item(x==0 ? F(x) : Throw(x)));
                    foreach(var item in lazy)trace.Add("lazy");
                } return string.Join("|",trace);}
            }
            """;
        var document = PipelineTestFixture.Document(source);
        var before = await StatementOperationTestFixture.Run(document);
        for (var pass = 0; pass < 3 && (await PipelineTestFixture.Diagnostics(document)).Length != 0; pass++) document = await PipelineTestFixture.Fix(document, key);
        Assert.Empty(await PipelineTestFixture.Diagnostics(document));
        Assert.Equal(before, await StatementOperationTestFixture.Run(document));
        Assert.Contains("overflow", before);
        Assert.DoesNotContain("throw", before);
    }
    [Fact]
    public async Task BlockExtractionRetainsCheckedAndMutableCaptures()
    {
        const string source = """
            using System; using System.Linq;
            class C { static int F(int x)=>x;
              public static string Run(){int capture=2; checked {
                var sequence=new[]{int.MaxValue}.Select(x=>{var a=F(x);var b=a+capture;return b;});
                capture=3;try{return sequence.Single().ToString();}catch(OverflowException){return "overflow";}
              }}
            }
            """;
        await Same(source, PipelineCodeFixProvider.ExtractKey, "CR0401");
    }
    [Theory]
    [InlineData("ToDictionary")]
    [InlineData("ToLookup")]
    [InlineData("ToImmutableDictionary")]
    public async Task MaterializerKeysComparersDuplicateExceptionsAndDisposal(string method)
    {
        var source = """
            using System; using System.Linq; using System.Collections.Generic; using System.Collections.Immutable;
            class C { static List<string> trace=new();
              sealed class Comparer:IEqualityComparer<int>{public bool Equals(int a,int b){trace.Add("eq"+a+":"+b);return a==b;}public int GetHashCode(int a){trace.Add("hash"+a);return a;}}
              static IEnumerable<int> Source(){try{trace.Add("start");yield return 1;yield return 1;}finally{trace.Add("dispose");}}
              static int Key(int x){trace.Add("key"+x);return x;}
              static IEqualityComparer<int> GetComparer(){trace.Add("comparer");return new Comparer();}
              public static string Run(){trace.Clear();try{var result=Source().METHOD(x=>Key(x),GetComparer());trace.Add("done");}catch(ArgumentException){trace.Add("duplicate");}return string.Join("|",trace);}
            }
            """;
        await Same(source.Replace("METHOD", method), PipelineCodeFixProvider.SelectorsKey, "CR0402");
    }
    [Theory]
    [InlineData(PipelineCodeFixProvider.ExtractKey)]
    [InlineData(PipelineCodeFixProvider.BlockKey)]
    public async Task ImmutableArrayProjectionTiming(string key)
    {
        const string source = "using System;using System.Linq;using System.Collections.Generic;using System.Collections.Immutable;class C{static List<string> log=new();static int F(int x){log.Add(\"f\"+x);return x;}public static string Run(){log.Clear();var p=ImmutableArray.Create(1,2).Select(x=>F(x)+1);log.Add(\"constructed\");foreach(var x in p)log.Add(x.ToString());return string.Join(\"|\",log);}}";
        await Same(source,key,"CR0400");
    }
    private static async Task Same(string source,string key,string id)
    {
        var document=PipelineTestFixture.Document(source);
        var before=await StatementOperationTestFixture.Run(document);
        Assert.Contains(await PipelineTestFixture.Diagnostics(document), diagnostic=>diagnostic.Id==id);
        var changed=await PipelineTestFixture.Fix(document,key,id);
        Assert.Empty(await PipelineTestFixture.Diagnostics(changed));
        Assert.Equal(before,await StatementOperationTestFixture.Run(changed));
    }
}
