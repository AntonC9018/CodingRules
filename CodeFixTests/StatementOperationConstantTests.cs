using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodingRules;

public sealed class StatementOperationConstantTests
{
    [Theory]
    [InlineData(StatementOperationCodeFixProvider.ExpandKey)]
    [InlineData(StatementOperationCodeFixProvider.ExtractKey)]
    public async Task IdentityConvertedConstantsStayInline(string key)
    {
        const string source = """
            public class C
            {
                enum Mode { Create }
                const int Limit = 4;
                static int Read(string text) => int.Parse(text);
                static int Twice(int value) => value * 2;
                static string Report(string kind, int value, string detail, Mode mode, int limit, string name)
                {
                    if (kind != "kind" || detail != "detail" || mode != Mode.Create || limit != Limit || name != "C")
                        throw new System.Exception();
                    return value.ToString();
                }
                public static string Run()
                {
                    return Report("kind", Twice(Read("3")), "detail", Mode.Create, Limit, nameof(C));
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        Assert.Equal("6", await StatementOperationTestFixture.Run(document));
        Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var changed = await StatementOperationTestFixture.Reparse(
            await StatementOperationTestFixture.Fix(document, key));
        Assert.Equal("6", await StatementOperationTestFixture.Run(changed));
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        var model = (await changed.GetSemanticModelAsync())!;
        var root = (await changed.GetSyntaxRootAsync())!;
        var initializers = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()
            .Where(statement => statement.Modifiers.All(token => token.RawKind != (int)SyntaxKind.ConstKeyword))
            .SelectMany(statement => statement.Declaration.Variables)
            .Select(variable => variable.Initializer!.Value);
        Assert.DoesNotContain(initializers, value => model.GetConstantValue(value).HasValue);
    }

    [Theory]
    [InlineData("object", "produce,use")]
    [InlineData("Tag", "convert,produce,use")]
    public async Task BoxingAndUserConversionsKeepTheirEarlierArgumentSnapshot(string parameterType, string expected)
    {
        var source = $$"""
            public class C
            {
                static string events = "";
                struct Tag
                {
                    public static implicit operator Tag(int value)
                    {
                        events += "convert,";
                        return new Tag();
                    }
                }
                static int Produce(int value) { events += "produce,"; return value; }
                static void Use({{parameterType}} first, int second) { events += "use"; }
                public static string Run()
                {
                    events = "";
                    Use(1, Produce(2));
                    return events;
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var changed = await StatementOperationTestFixture.Reparse(
            await StatementOperationTestFixture.Fix(document, StatementOperationCodeFixProvider.ExpandKey));
        Assert.Equal(expected, await StatementOperationTestFixture.Run(changed));
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        var declarations = (await changed.GetSyntaxRootAsync())!.DescendantNodes()
            .OfType<LocalDeclarationStatementSyntax>();
        Assert.Contains(declarations, statement => statement.Declaration.Type.ToString() == parameterType
            && statement.Declaration.Variables.Any(variable => variable.Initializer?.Value.ToString() == "1"));
    }
}
