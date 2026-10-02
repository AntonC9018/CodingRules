using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CodingRules;

public sealed class SpanRegressionTests
{
    public static IEnumerable<object[]> LongConditionHosts()
    {
        string[] hosts = {
            "static bool F() { if (EXPR) return true; return false; }",
            "static bool F() { var result = EXPR; return result; }",
            "static bool F() { return EXPR; }",
            "static bool F() => EXPR;",
            "static bool Property => EXPR;",
            "static bool Property { get { return EXPR; } }",
            "static bool F() { bool Local() => EXPR; return Local(); }",
            "static System.Func<bool> F() => () => EXPR;",
            "static bool F() { bool result; result = EXPR; return result; }",
            "static int F() => EXPR ? 1 : 0;",
            "static bool F() { while (EXPR) return true; return false; }",
            "static bool F() { for (; EXPR;) return true; return false; }",
            "static bool F() { do { a = false; } while (EXPR); return a; }",
            "static bool F() { try { return false; } catch (System.Exception) when (EXPR) { return true; } }",
            "static bool F(int value) { switch (value) { case 0 when EXPR: return true; default: return false; } }",
            "static bool field = EXPR;",
            "static bool Property { get; } = EXPR;",
        };
        foreach (var host in hosts)
            foreach (var inspection in new[] { "text.Trim() == \"x\"", "text.Trim().Length > 0" })
                yield return new object[] { host.Replace("EXPR", inspection + " && a && b") };
    }

    [Theory]
    [MemberData(nameof(LongConditionHosts))]
    public async Task LongConditionWhoseRemovedTrimExposesNewOlderReasonOmitsOnlyItsRoot(string host)
    {
        var document = SpanTestFixture.Document("class C { static string text = \" x \"; static bool a = true, b = true; "
            + host + " static int Safe(string value) => value.Trim().Length; }");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await SpanTestFixture.Diagnostics(document));
        Assert.Contains("value.Trim()", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
        Assert.Contains(await SpanTestFixture.Diagnostics(document, all: true), item => item.Id == "CR0203");
        var changed = await SpanTestFixture.Fix(document, diagnostic);
        Assert.Empty(await SpanTestFixture.Diagnostics(changed));
    }

    [Theory]
    [InlineData("text.Trim() == \"x\"")]
    [InlineData("text.Trim() == \"x\" && a")]
    [InlineData("text.Trim().Length > 0 && a")]
    [InlineData("text.Trim() == \"x\" && a || b")]
    [InlineData("text.Trim() == \"x\" && other.Trim() == \"y\" && a")]
    public async Task ConditionsWithNoNewOlderReasonKeepTheirAction(string condition)
    {
        var document = SpanTestFixture.Document("class C { static bool F(string text, string other, bool a, bool b) => " + condition + "; }");
        var diagnostics = await SpanTestFixture.Diagnostics(document);
        Assert.NotEmpty(diagnostics);
        var changed = await SpanTestFixture.Fix(document, diagnostics[0]);
        var before = (await SpanTestFixture.Diagnostics(document, all: true)).Where(item => item.Id != "CR0700").Select(item => item.Id).ToArray();
        Assert.All((await SpanTestFixture.Diagnostics(changed, all: true)).Where(item => item.Id != "CR0700"), item => Assert.Contains(item.Id, before));
    }

    [Theory]
    [InlineData("CR0201")]
    [InlineData("CR0203")]
    public async Task DisabledConditionReasonUsesActualDriverPrecedence(string disabled)
    {
        var document = SpanTestFixture.Document("class C { bool F(string text, bool a, bool b) => text.Trim() == \"x\" && a && b; }",
            "root=true\n[*.cs]\ndotnet_diagnostic." + disabled + ".severity=none\n");
        Assert.Single(await SpanTestFixture.Diagnostics(document));
        await SpanTestFixture.Fix(document);
    }

    [Fact]
    public async Task DisabledMixedReasonDoesNotHideExposedLongConditionConflict()
    {
        var document = SpanTestFixture.Document("class C { bool F(string text, bool a, bool b) => text.Trim() == \"x\" && a || b; }",
            "root=true\n[*.cs]\ndotnet_diagnostic.CR0200.severity=none\n");
        Assert.Empty(await SpanTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData(LanguageVersion.CSharp7)]
    [InlineData(LanguageVersion.CSharp12)]
    public async Task MemberHelperPreservesOtherPartialStaticImportCallNameofAndCallerDefaults(LanguageVersion language)
    {
        var document = SpanTestFixture.Document("public partial class C { static int P = \" a \".Trim().Length; }", language: language);
        document = document.Project.WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).GetDocument(document.Id)!;
        var other = document.Project.AddDocument("Other.cs", """
            using static Utility;
            public static class Utility {
                public static int InspectTextLength(string text) => 7;
                public static string Observe(string text = "", [System.Runtime.CompilerServices.CallerMemberName] string member = "",
                    [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => text + ":" + member + ":" + line;
            }
            public partial class C {
                public static string Run() => InspectTextLength(" z ") + ":" + nameof(InspectTextLength) + ":" + Observe();
            }
            public class Derived : C { public static int Other() => InspectTextLength(" z "); }
            """, filePath: "/Other.cs");
        document = other.Project.GetDocument(document.Id)!;
        var expected = await StatementOperationTestFixture.Run(document);
        Assert.StartsWith("7:InspectTextLength::Run:", expected);
        var changed = await SpanTestFixture.Fix(document);
        Assert.Contains("InspectTextLength2", (await changed.GetTextAsync()).ToString());
        Assert.Equal((await other.GetTextAsync()).ToString(), (await changed.Project.GetDocument(other.Id)!.GetTextAsync()).ToString());
        Assert.Equal(expected, await StatementOperationTestFixture.Run(changed));
        Assert.Empty((await changed.Project.GetCompilationAsync())!.GetDiagnostics().Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
    }

    [Theory]
    [InlineData("class Base { protected static int InspectTextLength(string text) => 7; }", "Base", "static int P = \" a \".Trim().Length;", "InspectTextLength2")]
    [InlineData("class Base<T> { protected static int InspectTextLength(string text) => 7; }", "Base<int>", "static int P { get; } = \" a \".Trim().Length;", "InspectTextLength2")]
    [InlineData("class Base { protected static bool InspectTextEquals(string text) => false; }", "Base", "static bool P = \" a \".Trim() == \"a\";", "InspectTextEquals2")]
    public async Task InheritedNamesDoNotLeaveInitializerWarningWithoutAction(string prefix, string baseType, string member, string name)
    {
        var document = SpanTestFixture.Document(prefix + " class C : " + baseType + " { " + member + " }");
        Assert.Single(await SpanTestFixture.Diagnostics(document));
        var changed = await SpanTestFixture.Fix(document);
        Assert.Contains(name, (await changed.GetTextAsync()).ToString());
        Assert.Empty(await SpanTestFixture.Diagnostics(changed));
        Assert.Empty((await changed.Project.GetCompilationAsync())!.GetDiagnostics().Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
    }
}
