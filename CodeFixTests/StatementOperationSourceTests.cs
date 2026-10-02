using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class StatementOperationSourceTests
{
    [Fact]
    public async Task ImmutableArrayJoinPipelineAndDeferredFormattableReturnRemainAllowed()
    {
        const string source = """
            using System;
            using System.Collections.Immutable;
            using System.Collections.Generic;
            using System.Linq;
            class C
            {
                readonly record struct Category(string Name);
                readonly record struct Info(Category Category, ImmutableArray<string> Values);
                static FormattableString CategoryValue(Category category, string value) => $"{category.Name}:{value}";
                static string RenderList(Info list)
                {
                    var values = string.Join(", ", list.Values.Select(value => CategoryValue(list.Category, value)));
                    return values;
                }
                static FormattableString? RenderLinks(List<FormattableString> links)
                {
                    return links.Count == 0 ? null : (FormattableString)$"{string.Join(" · ", links)}";
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData(StatementOperationCodeFixProvider.ExtractKey)]
    [InlineData(StatementOperationCodeFixProvider.ExpandKey)]
    public async Task BomOffsetsKeepDistinctReadsAndLengthOrdering(string key)
    {
        const string source = """
            using System.Text;
            public class C
            {
                public static string Run()
                {
                    Encoding encoding = Encoding.UTF8;
                    byte[] bytes = { 239, 187, 191, 65 };
                    bool hasBom = true;
                    var text = encoding.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
                    return text;
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        Assert.Equal(new[] { "CR0301", "CR0301" }, (await StatementOperationTestFixture.Diagnostics(document)).Select(diagnostic => diagnostic.Id));
        var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, key));
        Assert.Equal("A", await StatementOperationTestFixture.Run(changed));
        if (key == StatementOperationCodeFixProvider.ExtractKey)
            changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(changed, key));
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        Assert.Equal(2, (await changed.GetSyntaxRootAsync())!.DescendantTokens().Count(token => token.ValueText == "hasBom") - 1);
    }

    [Theory]
    [InlineData(StatementOperationCodeFixProvider.ExtractKey)]
    [InlineData(StatementOperationCodeFixProvider.ExpandKey)]
    public async Task ReadonlyCsvFieldConstructionRemainsOneOccurrence(string key)
    {
        const string source = """
            using System.Collections.Generic;
            using System.Text;
            public class C
            {
                readonly record struct CsvField(string Value, bool Quoted);
                public static string Run()
                {
                    var current = new List<CsvField>();
                    var currentField = new StringBuilder("field");
                    var quotedFields = new List<int> { 1 };
                    current.Add(new CsvField(currentField.ToString(), quotedFields.Count > 0));
                    currentField.Clear();
                    return current[0].Value;
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        Assert.Equal("CR0300", Assert.Single(await StatementOperationTestFixture.Diagnostics(document)).Id);
        var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, key));
        Assert.Equal("field", await StatementOperationTestFixture.Run(changed));
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
    }

    [Fact]
    public async Task ThirdCollectionElementRetainsTheOriginalPosition()
    {
        const string source = """
            using System;
            using System.IO;
            using System.Collections.Generic;
            public class C
            {
                static List<string> events = new();
                static string First() { events.Add("first"); return "a"; }
                static string Second() { events.Add("second"); return "b"; }
                public static string Run()
                {
                    events.Clear();
                    string[] paths = [First(), Second(), Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"))];
                    return string.Join(",", events) + ":" + paths.Length;
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var actions = await StatementOperationTestFixture.Actions(document, (await StatementOperationTestFixture.Diagnostics(document))[0]);
        Assert.Equal(StatementOperationCodeFixProvider.ExtractKey, Assert.Single(actions).EquivalenceKey);
        var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, StatementOperationCodeFixProvider.ExtractKey));
        Assert.Equal("first,second:3", await StatementOperationTestFixture.Run(changed));
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
    }
}
