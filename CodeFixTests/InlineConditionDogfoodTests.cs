using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class InlineConditionDogfoodTests
{
    public static IEnumerable<object[]> ReferenceCases()
    {
        yield return new object[] { "class C { void M(string status) { if (status.StartsWith(\"rejected\", System.StringComparison.Ordinal) || status.StartsWith(\"declined\", System.StringComparison.Ordinal) || status.StartsWith(\"hired\", System.StringComparison.Ordinal) || status.StartsWith(\"done\", System.StringComparison.Ordinal)) { } } }", "CR0204", true };
        yield return new object[] { "class C { void M(byte[] bytes) { var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF; } }", "CR0201", true };
        yield return new object[] { "class C { bool M(string? rowPath, string key) { return rowPath is not null && string.Equals(rowPath.Replace('\\\\', '/'), key.Replace('\\\\', '/'), System.StringComparison.OrdinalIgnoreCase); } }", "CR0203", true };
        yield return new object[] { "class C { void M(bool wasQuoted, string value) { var needsQuotes = wasQuoted || value.Contains(',') || value.Contains('\"') || value.Contains('\\r') || value.Contains('\\n'); } }", "CR0201", true };
        yield return new object[] { "class C { void M(System.Text.Json.JsonElement root) { if (root.TryGetProperty(\"field\", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String) { System.Console.WriteLine(value); } } }", "CR0203", false };
        yield return new object[] { "class C { enum Mode { Flowing } class P { public Mode LayoutMode; public object? PageLayout; } void M(P p) { if (p.LayoutMode == Mode.Flowing && p.PageLayout is not null) { } } }", "CR0202", true };
        yield return new object[] { "class C { void M() { System.Func<string, bool> filter = static name => !name.EndsWith(\".doc\", System.StringComparison.OrdinalIgnoreCase) && !name.EndsWith(\".docx\", System.StringComparison.OrdinalIgnoreCase) && !name.EndsWith(\".pdf\", System.StringComparison.OrdinalIgnoreCase) && !name.EndsWith(\".rtf\", System.StringComparison.OrdinalIgnoreCase) && !name.EndsWith(\".txt\", System.StringComparison.OrdinalIgnoreCase) && !name.EndsWith(\".odt\", System.StringComparison.OrdinalIgnoreCase) && !name.EndsWith(\".html\", System.StringComparison.OrdinalIgnoreCase) && !name.EndsWith(\".htm\", System.StringComparison.OrdinalIgnoreCase); } }", "CR0204", true };
    }

    [Theory]
    [MemberData(nameof(ReferenceCases))]
    public async Task ReferenceShapeDiagnosesAndFixes(string source, string id, bool both)
    {
        var document = InlineConditionTestFixture.Document(source);
        Assert.Equal(id, Assert.Single(await InlineConditionTestFixture.Diagnostics(document)).Id);
        foreach (var key in both ? new[] { InlineConditionCodeFixProvider.ExtractKey, InlineConditionCodeFixProvider.ExpandKey }
            : new[] { InlineConditionCodeFixProvider.ExpandKey })
        {
            var changed = await InlineConditionTestFixture.Fix(document, key);
            await InlineConditionTestFixture.Compiles(changed);
            Assert.Empty(await InlineConditionTestFixture.Diagnostics(changed));
        }
    }

    [Theory]
    [InlineData("class C { bool M(int index, string[] fields) => index < 0 || index >= fields.Length; }")]
    [InlineData("class C { bool M(double value) => !double.IsFinite(value) || value <= 0; }")]
    [InlineData("class C { bool M(string? path) => path is null || !System.IO.Directory.Exists(path); }")]
    [InlineData("class C { bool M(string status) => status.StartsWith(\"a\", System.StringComparison.Ordinal) || status.StartsWith(\"b\", System.StringComparison.Ordinal); }")]
    public async Task ReferenceBoundariesStayAllowed(string source)
    {
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(InlineConditionTestFixture.Document(source)));
    }

    [Theory]
    [InlineData("System.Collections.Generic.Dictionary<string, int>")]
    [InlineData("System.Collections.Generic.IDictionary<string, int>")]
    [InlineData("System.Collections.Generic.IReadOnlyDictionary<string, int>")]
    public async Task DictionaryCatalogueMatchesEachSupportedDeclaration(string type)
    {
        var document = InlineConditionTestFixture.Document("class C { void M(" + type + " dict) { if (dict.TryGetValue(\"key\", out var value) && value > 0) System.Console.WriteLine(value); } }");
        Assert.Equal("CR0203", Assert.Single(await InlineConditionTestFixture.Diagnostics(document)).Id);
        var changed = await InlineConditionTestFixture.Fix(document, InlineConditionCodeFixProvider.ExpandKey);
        await InlineConditionTestFixture.Compiles(changed);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(changed));
    }

    [Theory]
    [InlineData("text.Trim().StartsWith(\"a\")")]
    [InlineData("System.Convert.ToInt32(text) > 0")]
    [InlineData("System.Guid.Parse(text) != System.Guid.Empty")]
    [InlineData("(int)value > 0")]
    public async Task ProducingCatalogueLeavesTerminateAfterEitherFix(string expression)
    {
        var document = InlineConditionTestFixture.Document("class C { bool M(string text, double value) => " + expression + "; }");
        Assert.Equal("CR0203", Assert.Single(await InlineConditionTestFixture.Diagnostics(document)).Id);
        foreach (var key in new[] { InlineConditionCodeFixProvider.ExtractKey, InlineConditionCodeFixProvider.ExpandKey })
        {
            var changed = await InlineConditionTestFixture.Fix(document, key);
            await InlineConditionTestFixture.Compiles(changed);
            Assert.Empty(await InlineConditionTestFixture.Diagnostics(changed));
        }
    }
}
