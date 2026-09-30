using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class InlineConditionSafetyTests
{
    [Theory]
    [InlineData("string? text = \"x\";", "text")]
    [InlineData("string? Text { get; set; } = \"x\";", "Text")]
    public async Task NullableMemberNarrowingUsesFlowPreservingExpansion(string declaration, string name)
    {
        var source = "class C { " + declaration + " void M(bool a, bool b) { if (" + name + " is not null && a && b) { System.Console.WriteLine(" + name + ".Length); } } }";
        var document = InlineConditionTestFixture.Document(source);
        await InlineConditionTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await InlineConditionTestFixture.Diagnostics(document));
        var action = Assert.Single(await InlineConditionTestFixture.Actions(document, diagnostic));
        Assert.Equal(InlineConditionCodeFixProvider.ExpandKey, action.EquivalenceKey);
        var changed = await InlineConditionTestFixture.Fix(document, action.EquivalenceKey!);
        await InlineConditionTestFixture.Compiles(changed);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(changed));
    }

    [Theory]
    [InlineData("false && A() && B()")]
    [InlineData("true || A() || B()")]
    public async Task ConstantShortCircuitDoesNotAddUnreachableCodeWarnings(string condition)
    {
        var document = InlineConditionTestFixture.Document("class C { static bool A()=>true; static bool B()=>true; static bool M()=>" + condition + "; }");
        var before = (await document.Project.GetCompilationAsync())!.GetDiagnostics().Select(diagnostic => diagnostic.Id).ToArray();
        foreach (var key in new[] { InlineConditionCodeFixProvider.ExtractKey, InlineConditionCodeFixProvider.ExpandKey })
        {
            var changed = await InlineConditionTestFixture.Fix(document, key);
            Assert.Equal(before, (await changed.Project.GetCompilationAsync())!.GetDiagnostics().Select(diagnostic => diagnostic.Id));
        }
    }

    [Fact]
    public async Task LeftOperandReadRemainsBeforeLaterProducerMutation()
    {
        var source = "public class C { public static string Run() { int x = 2; string Read() { x = 0; return \"1\"; } bool result = x > int.Parse(Read()); return result.ToString() + x; } }";
        var document = InlineConditionTestFixture.Document(source);
        var before = await InlineConditionTestFixture.Run(document);
        Assert.Equal("True0", before);
        foreach (var key in new[] { InlineConditionCodeFixProvider.ExtractKey, InlineConditionCodeFixProvider.ExpandKey })
        {
            var changed = await InlineConditionTestFixture.Fix(document, key);
            Assert.Equal(before, await InlineConditionTestFixture.Run(changed));
        }
    }

    [Fact]
    public async Task PartialMemberHelperNameCannotShadowExistingMember()
    {
        var source = "partial class C { static bool A()=>true; static bool B()=>true; static bool D()=>true; static bool Flag = A() && B() && D(); } partial class C { static bool CheckCondition()=>false; }";
        var document = InlineConditionTestFixture.Document(source);
        var changed = await InlineConditionTestFixture.Fix(document, InlineConditionCodeFixProvider.ExtractKey);
        await InlineConditionTestFixture.Compiles(changed);
        Assert.Contains("private static bool CheckCondition2()", (await changed.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task SwitchSectionInitializerRetainsFollowingLocalReferences()
    {
        var source = "class C { void M(int mode, bool a, bool b, bool c) { switch(mode) { case 1: var ok = a && b && c; System.Console.WriteLine(ok); break; case 2: break; } } }";
        var document = InlineConditionTestFixture.Document(source);
        foreach (var key in new[] { InlineConditionCodeFixProvider.ExtractKey, InlineConditionCodeFixProvider.ExpandKey })
        {
            var changed = await InlineConditionTestFixture.Fix(document, key);
            await InlineConditionTestFixture.Compiles(changed);
            Assert.Empty(await InlineConditionTestFixture.Diagnostics(changed));
        }
    }

    [Fact]
    public async Task ExplicitCallerArgumentsRemainSupported()
    {
        var source = "class C { static bool A([System.Runtime.CompilerServices.CallerLineNumber] int line = 0)=>true; bool M()=>A(42) && A(43) && A(44); }";
        var document = InlineConditionTestFixture.Document(source);
        Assert.Single(await InlineConditionTestFixture.Diagnostics(document));
        foreach (var key in new[] { InlineConditionCodeFixProvider.ExtractKey, InlineConditionCodeFixProvider.ExpandKey })
        {
            await InlineConditionTestFixture.Compiles(await InlineConditionTestFixture.Fix(document, key));
        }
    }

    [Fact]
    public async Task ParamsExpansionIsOmittedWhileSameSiteHelperRemainsSafe()
    {
        var source = "class C { static bool A()=>true; static bool B()=>true; static bool D()=>true; static void Use(params bool[] values) { } void M() { Use(A() && B() && D(), A()); } }";
        var document = InlineConditionTestFixture.Document(source);
        var action = Assert.Single(await InlineConditionTestFixture.Actions(document, Assert.Single(await InlineConditionTestFixture.Diagnostics(document))));
        Assert.Equal(InlineConditionCodeFixProvider.ExtractKey, action.EquivalenceKey);
        await InlineConditionTestFixture.Compiles(await InlineConditionTestFixture.Fix(document, action.EquivalenceKey!));
    }

    [Fact]
    public async Task VoidArrowArgumentConvertsToBlockWithoutChangingCall()
    {
        var source = "class C { static bool A()=>true; static bool B()=>true; static bool D()=>true; static void Use(bool x) { } void M()=>Use(A() && B() && D()); }";
        var changed = await InlineConditionTestFixture.Fix(InlineConditionTestFixture.Document(source), InlineConditionCodeFixProvider.ExtractKey);
        await InlineConditionTestFixture.Compiles(changed);
        Assert.DoesNotContain("return Use", (await changed.GetTextAsync()).ToString());
    }
}
