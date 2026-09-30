using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class InlineConditionRuntimeTests
{
    public static IEnumerable<object[]> EvaluationSites()
    {
        var common = "static string trace = \"\"; static int count; static bool A() { trace += \"A\"; return true; } static bool B() { trace += \"B\"; return true; } static bool D() { trace += \"D\"; return true; }";
        var samples = new[]
        {
            "public static string Run() { if (A() && B() && D()) trace += \"work\"; return trace; }",
            "public static string Run() { var ok = A() && B() && D(); return trace + ok; }",
            "static bool Test() { return A() && B() && D(); } public static string Run() { return Test() + trace; }",
            "static bool Test() { lock(new object()) { return A() && B() && D(); } } public static string Run() { return Test() + trace; }",
            "static bool Test() => A() && B() && D(); public static string Run() { return Test() + trace; }",
            "static bool P => A() && B() && D(); public static string Run() { return P + trace; }",
            "static bool P { get => A() && B() && D(); } public static string Run() { return P + trace; }",
            "public static string Run() { bool Local() => A() && B() && D(); trace += \"created\"; return Local() + trace; }",
            "public static string Run() { System.Func<bool> f = static () => A() && B() && D(); trace += \"created\"; trace += f(); trace += f(); return trace; }",
            "public static string Run() { bool ok = false; ok = A() && B() && D(); return trace + ok; }",
            "static void Use(bool value) { trace += \"use\" + value; } public static string Run() { Use(A() && B() && D()); return trace; }",
            "C(bool value) { trace += \"new\" + value; } public static string Run() { var c = new C(A() && B() && D()); return trace; }",
            "static int Left() { trace += \"left\"; return 1; } static int Right() { trace += \"right\"; return 2; } public static string Run() { var value = (A() && B() && D()) ? Left() : Right(); return trace + value; }",
            "public static string Run() { while (count++ < 2 && B() && D()) { trace += \"body\"; continue; } return trace + count; }",
            "public static string Run() { for (int i = 0; i < 3 && B() && D(); i++) { trace += i; continue; } return trace; }",
            "public static string Run() { do { trace += \"body\"; } while (count++ < 2 && B() && D()); return trace + count; }",
        };
        foreach (var sample in samples)
        {
            yield return new object[] { "public class C { " + common + sample + " }", true };
        }

        var extraction = new[]
        {
            "static bool[] data = new bool[1]; static bool[] Target() { trace += \"target\"; return data; } static int Index() { trace += \"index\"; return 0; } public static string Run() { Target()[Index()] = A() && B() && D(); return trace + data[0]; }",
            "static void ThrowInner() { try { trace += \"throw\"; throw new System.Exception(\"value\"); } finally { trace += \"finally\"; } } public static string Run() { try { ThrowInner(); } catch(System.Exception e) when(e.Message.Length > 0 && B() && D()) { trace += \"caught\"; } catch { trace += \"fallback\"; } return trace; }",
            "public static string Run() { object value = 2; switch(value) { case int n when n > 0 && B() && D(): trace += n; break; default: trace += \"fallback\"; break; } return trace; }",
            "static int Test(object value) => value switch { int n when n > 0 && B() && D() => n, _ => 0 }; public static string Run() { trace += Test(2); trace += Test(\"none\"); return trace; }",
            "public static string Run() { do { trace += \"body\"; continue; } while (count++ < 2 && B() && D()); return trace + count; }",
            "public static string Run() { bool outer = false; var value = outer ? ((A() && B() && D()) ? 1 : 2) : 0; return trace + value; }",
        };
        foreach (var sample in extraction)
        {
            yield return new object[] { "public class C { " + common + sample + " }", false };
        }
    }

    [Theory]
    [MemberData(nameof(EvaluationSites))]
    public async Task FixRetainsObservableHostTrace(string source, bool both)
    {
        var document = InlineConditionTestFixture.Document(source);
        var before = await InlineConditionTestFixture.Run(document);
        foreach (var key in both ? new[] { InlineConditionCodeFixProvider.ExtractKey, InlineConditionCodeFixProvider.ExpandKey }
            : new[] { InlineConditionCodeFixProvider.ExtractKey })
        {
            var changed = await InlineConditionTestFixture.Fix(document, key);
            Assert.Equal(before, await InlineConditionTestFixture.Run(changed));
            Assert.Empty(await InlineConditionTestFixture.Diagnostics(changed));
        }
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, false, true)]
    public async Task MixedTreePreservesGroupingShortCircuitAndExceptions(bool a, bool b, bool c, bool throwing)
    {
        var source = $$"""
            public class C
            {
                static string trace = "";
                static bool A() { trace += "A"; return {{a.ToString().ToLowerInvariant()}}; }
                static bool B() { trace += "B"; {{(throwing ? "throw new System.Exception();" : "return " + b.ToString().ToLowerInvariant() + ";")}} }
                static bool D() { trace += "D"; return {{c.ToString().ToLowerInvariant()}}; }
                public static string Run()
                {
                    try { if ((A() && B()) || D()) trace += "true"; else trace += "false"; }
                    catch { trace += "throw"; }
                    return trace;
                }
            }
            """;
        await FixRetainsObservableHostTrace(source, both: true);
    }

    [Fact]
    public async Task MutationIsReachedOnceAndUsesExpansionOnly()
    {
        var source = "public class C { public static string Run() { int count = 0; bool ready = true; if (++count > 0 && ready) count += 10; return count.ToString(); } }";
        var document = InlineConditionTestFixture.Document(source);
        var before = await InlineConditionTestFixture.Run(document);
        var changed = await InlineConditionTestFixture.Fix(document, InlineConditionCodeFixProvider.ExpandKey);
        Assert.Equal(before, await InlineConditionTestFixture.Run(changed));
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(changed));
        Assert.Contains("conditionValue = ++count", (await changed.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData("new byte[0]")]
    [InlineData("new byte[] { 0xEF }")]
    [InlineData("new byte[] { 0xEF, 0xBB, 0xBF }")]
    public async Task BomReadsOnlyAvailableBytes(string bytes)
    {
        var source = "public class C { public static string Run() { var bytes = " + bytes + "; var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF; return hasBom.ToString(); } }";
        await FixRetainsObservableHostTrace(source, both: true);
    }

    [Theory]
    [InlineData("static bool Flag = A() && B() && D();", "Flag")]
    [InlineData("static bool P { get; } = A() && B() && D();", "P")]
    [InlineData("bool Flag = A() && B() && D();", "new C().Flag")]
    [InlineData("bool P { get; } = A() && B() && D();", "new C().P")]
    public async Task MemberInitializationRetainsDeclarationOrder(string member, string read)
    {
        var source = "public class C { static string trace = \"\"; static bool Log(string name) { trace += name; return true; } static bool A()=>Log(\"A\"); static bool B()=>Log(\"B\"); static bool D()=>Log(\"D\"); static bool Before = Log(\"before\"); " + member + " static bool After = Log(\"after\"); public static string Run() { var value = " + read + "; return trace + value; } }";
        await FixRetainsObservableHostTrace(source, both: false);
    }

    [Fact]
    public async Task ArgumentExpansionRetainsReceiverConversionAndOperandOrder()
    {
        var source = """
            public class C
            {
                static string trace = "";
                class Box { }
                struct Token { public static implicit operator Box(Token token) { trace += "convert"; return new Box(); } }
                static C Receiver() { trace += "receiver"; return new C(); }
                static Token Earlier() { trace += "earlier"; return new Token(); }
                static int Later() { trace += "later"; return 1; }
                static bool A() { trace += "A"; return true; }
                static bool B() { trace += "B"; return false; }
                static bool D() { trace += "D"; return true; }
                void Use(Box earlier, bool flag, int later) { trace += "use"; }
                public static string Run() { Receiver().Use(Earlier(), A() && B() && D(), Later()); return trace; }
            }
            """;
        await FixRetainsObservableHostTrace(source, both: true);
    }
}
