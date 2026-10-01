using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class NamedArgumentSafetyTests
{
    [Theory]
    [InlineData("static void M(int x,int y,[System.Runtime.CompilerServices.CallerLineNumber] int line=0) {} static void F() { M(1,2); }")]
    [InlineData("static void Observe(int x,[System.Runtime.CompilerServices.CallerArgumentExpression(\"x\")] string? text=null) {} static int M(int x,int y) => x; static void F() { Observe(M(1,2)); }")]
    [InlineData("static void Observe([System.Runtime.CompilerServices.CallerLineNumber] int line=0) {} static void M(int x,int y) {} static void F() { M(1,2); } static void G() { Observe(); }")]
    [InlineData("class B { public B(int x,[System.Runtime.CompilerServices.CallerArgumentExpression(\"x\")] string? text=null) {} } class D : B { public D() : base(M(1,2)) {} } static int M(int x,int y) => x;")]
    [InlineData("public int this[int x,[System.Runtime.CompilerServices.CallerArgumentExpression(\"x\")] string? text=null] => x; static int M(int x,int y) => x; static int F(C c) => c[M(1,2)];")]
    [InlineData("public int this[int x,[System.Runtime.CompilerServices.CallerLineNumber] int line=0] => x; static void M(int x,int y) {} static void F() { M(1,2); } static int G(C c) => c[1];")]
    public async Task ActualImplicitObserversExcludeAffectedAndLaterSites(string members)
    {
        var document = NamedArgumentTestFixture.Document("class C { " + members + " }");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await NamedArgumentTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task AttributeDefaultsAreObservers()
    {
        var document = NamedArgumentTestFixture.Document("class A : System.Attribute { public A(int x,int y,[System.Runtime.CompilerServices.CallerLineNumber] int line=0) {} } [A(1,2)] class C {}");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await NamedArgumentTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task EarlierObserverAndExplicitDefaultsRemainSupported()
    {
        var document = NamedArgumentTestFixture.Document("class C { static void Observe([System.Runtime.CompilerServices.CallerLineNumber] int line=0) {} static void M(int x,int y,[System.Runtime.CompilerServices.CallerLineNumber] int line=0) {} static void F() { Observe(); M(1,2,line:42); } }");
        await NamedArgumentTestFixture.Fix(document, Assert.Single(await NamedArgumentTestFixture.Diagnostics(document)));
    }

    [Fact]
    public async Task RuntimeRetainsSideEffectsConversionsRefMutationAndConstructorOrder()
    {
        var document = NamedArgumentTestFixture.Document("""
            using System;
            class V {
                public int Value;
                public static implicit operator V(int x) { C.Log += "convert" + x + ";"; return new V { Value=x }; }
            }
            class B { public B(V first,V second) { C.Log += "base;"; } }
            class C : B {
                public static string Log = "";
                public C() : base(Get(1),Get(2)) { Log += "ctor;"; }
                static int Get(int n) { Log += "get" + n + ";"; return n; }
                static void M(ref int first,out int second) { first++; second=first; Log += "mutate;"; }
                public static string Run() { new C(); int value=3; M(ref value,out var output); return Log + value + ":" + output; }
            }
            """);
        var original = await StatementOperationTestFixture.Run(document);
        while (!(await NamedArgumentTestFixture.Diagnostics(document)).IsEmpty) document = await NamedArgumentTestFixture.Fix(document);
        Assert.Equal(original, await StatementOperationTestFixture.Run(document));
        Assert.Equal("get1;convert1;get2;convert2;base;ctor;mutate;4:4", original);
    }
}
