using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class InlineConditionHostTests
{
    public static IEnumerable<object[]> Hosts()
    {
        var declarations = "static bool A() => true; static bool B() => true; static bool D() => true; static void Work() { }";
        var bodies = new[]
        {
            "void M() { if (A() && B() && D()) Work(); }",
            "void M() { var ok = A() && B() && D(); }",
            "void M() { bool ok = A() && B() && D(); }",
            "bool M() { return A() && B() && D(); }",
            "bool M() { lock (new object()) { return A() && B() && D(); } }",
            "bool M() => A() && B() && D();",
            "bool P => A() && B() && D();",
            "bool P { get => A() && B() && D(); }",
            "void M() { bool Local() => A() && B() && D(); Work(); }",
            "void M() { System.Func<bool> f = () => A() && B() && D(); }",
            "void M() { System.Func<bool> f = static () => A() && B() && D(); }",
            "void M() { bool ok = false; ok = A() && B() && D(); }",
            "static void Use(bool b) { } void M() { Use(A() && B() && D()); }",
            "public C(bool b) { } void M() { var c = new C(A() && B() && D()); }",
            "void M() { var value = (A() && B() && D()) ? 1 : 2; }",
            "void M() { while (A() && B() && D()) { break; } }",
            "void M(int limit) { for (int i = 0; i < limit && B() && D(); i++) { Work(); } }",
            "void M() { do { Work(); } while (A() && B() && D()); }",
        };
        foreach (var body in bodies)
        {
            yield return new object[] { "class C { " + declarations + " " + body + " }", true };
        }

        var extractionOnly = new[]
        {
            "void M() { try { Work(); } catch (System.Exception e) when (e.Message.Length > 0 && B() && D()) { } }",
            "void M() { try { Work(); } catch (System.Exception) when (A() && B() && D()) { } }",
            "void M(object value) { switch(value) { case int n when n > 0 && B() && D(): Work(); break; } }",
            "int M(object value) => value switch { int n when n > 0 && B() && D() => n, _ => 0 };",
            "static bool Flag = A() && B() && D();",
            "static bool P { get; } = A() && B() && D();",
            "bool Flag = A() && B() && D();",
            "bool P { get; } = A() && B() && D();",
            "static bool[] Target() => new bool[1]; static int Index() => 0; void M() { Target()[Index()] = A() && B() && D(); }",
            "void M() { do { if (A()) continue; Work(); } while (A() && B() && D()); }",
            "void M(bool outer) { var value = outer ? ((A() && B() && D()) ? 1 : 2) : 0; }",
        };
        foreach (var body in extractionOnly)
        {
            yield return new object[] { "class C { " + declarations + " " + body + " }", false };
        }
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task EveryAcceptedHostHasCompilingLinearFix(string source, bool both)
    {
        var document = InlineConditionTestFixture.Document(source);
        var diagnostic = Assert.Single(await InlineConditionTestFixture.Diagnostics(document));
        Assert.Equal(DiagnosticIds.ExcessConditionChecks, diagnostic.Id);
        Assert.Contains("&&", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        var negative = source.Remove(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)
            .Insert(diagnostic.Location.SourceSpan.Start, "true");
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(InlineConditionTestFixture.Document(negative)));
        var actions = await InlineConditionTestFixture.Actions(document, diagnostic);
        Assert.Equal(both ? 2 : 1, actions.Count);
        Assert.Equal(InlineConditionCodeFixProvider.ExtractKey, actions[0].EquivalenceKey);
        foreach (var action in actions)
        {
            var changed = await InlineConditionTestFixture.Fix(document, action.EquivalenceKey!);
            await InlineConditionTestFixture.Compiles(changed);
            Assert.Empty(await InlineConditionTestFixture.Diagnostics(changed));
            var text = (await changed.GetTextAsync()).ToString();
            Assert.DoesNotContain("A() && B() && D()", text);
            Assert.DoesNotContain("var ok;", text);
            Assert.Contains("if (", text);
            if (source.Contains("static ()"))
            {
                Assert.Contains("static ()", text);
            }
        }
    }
}
