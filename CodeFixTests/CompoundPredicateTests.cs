using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodingRules;

public sealed class CompoundPredicateTests
{
    [Theory]
    [InlineData("ids.Any(id => IsLab(id) && parents.Any(parent => parent == id))")]
    [InlineData("ids.All(id => IsLab(id) || parents.All(parent => parent == id))")]
    [InlineData("ids.Where(id => IsLab(id) && parents.Where(parent => parent == id).Any()).Any()")]
    [InlineData("ids.Where((id, index) => index > 0 && parents.Any(parent => parent == id)).Any()")]
    [InlineData("Enumerable.Any(source: ids, predicate: id => IsLab(id) && Enumerable.Any(parents, parent => parent == id))")]
    [InlineData("ids.Any((int id) => IsLab(id) && parents.Any(parent => parent == id))")]
    [InlineData("ids.Any(id => IsLab(id) && parents.Any(parent => { return parent == id; }))")]
    public async Task FrameworkCompoundPredicatesUseGuards(string query)
    {
        var document = CompoundPredicateTestFixture.Document(Prelude
            + "static bool M(int[] ids, int[] parents) => " + query + ";}");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await CompoundPredicateTestFixture.Diagnostics(document));
        Assert.Equal("CR0403", diagnostic.Id);
        var changed = await CompoundPredicateTestFixture.Fix(document);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(changed));
        var root = (await changed.GetSyntaxRootAsync())!;
        var outer = root.DescendantNodes().OfType<LambdaExpressionSyntax>().First();
        var body = Assert.IsType<BlockSyntax>(outer.Body);
        Assert.True(body.Statements.Count >= 3);
        Assert.DoesNotContain(body.DescendantNodes(), node => node is LocalFunctionStatementSyntax
            or LocalDeclarationStatementSyntax or ElseClauseSyntax);
        Assert.Equal(query.Contains("||") ? "return false;" : "return true;",
            body.Statements.Last().ToString());
    }

    [Theory]
    [InlineData("ids.Any(id => parents.Any(parent => parent == id))")]
    [InlineData("ids.Any(id => IsLab(id) && id > 0)")]
    [InlineData("ids.Any(id => IsLab(id) && parents.Any())")]
    [InlineData("ids.Any(id => IsLab(id) && parents.Any(IsLab))")]
    [InlineData("ids.Any(id => { if (!IsLab(id)) { return false; } return parents.Any(parent => parent == id); })")]
    [InlineData("ids.AsQueryable().Any(id => IsLab(id) && parents.Any(parent => parent == id))")]
    public async Task SimpleNestingAndOtherPredicateShapesRemainAccepted(string query)
    {
        var document = CompoundPredicateTestFixture.Document(Prelude
            + "static bool M(int[] ids, int[] parents) => " + query + ";}");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task DescendantLambdaOwnsItsCompoundDecision()
    {
        const string source = Prelude + """
            static bool M(int[] ids, int[] parents) => ids.Any(id =>
                parents.Any(parent => IsLab(parent) && ids.Any(child => child == id)));
            }
            """;
        var document = CompoundPredicateTestFixture.Document(source);
        var diagnostics = await CompoundPredicateTestFixture.Diagnostics(document);
        Assert.Equal(2, diagnostics.Length);
        Assert.StartsWith("id =>", source.Substring(diagnostics[0].Location.SourceSpan.Start));
        Assert.StartsWith("parent =>", source.Substring(diagnostics[1].Location.SourceSpan.Start));
        var changed = await CompoundPredicateTestFixture.Fix(document);
        changed = await CompoundPredicateTestFixture.Fix(changed);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(changed));
    }

    [Theory]
    [InlineData("ids.Any(id => parents.Any(parent => ids.Any(child => child == parent)))")]
    [InlineData("ids.All(id => parents.All(parent => ids.All(child => child == parent)))")]
    [InlineData("ids.Where((id, index) => parents.Where(parent => ids.Any(child => child == parent)).Any()).Any()")]
    [InlineData("Enumerable.Any(ids, predicate: (int id) => Enumerable.Any(parents, parent => ids.Any(child => child == parent)))")]
    public async Task ThirdCompactLevelReportsOuterLambdaAndRetainsExactDecision(string query)
    {
        var document = CompoundPredicateTestFixture.Document(Prelude
            + "static bool M(int[] ids, int[] parents) => " + query + ";}");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await CompoundPredicateTestFixture.Diagnostics(document));
        var root = (await document.GetSyntaxRootAsync())!;
        var lambda = root.DescendantNodes().OfType<LambdaExpressionSyntax>().First();
        Assert.Equal(lambda.Span, diagnostic.Location.SourceSpan);
        var expression = Assert.IsAssignableFrom<ExpressionSyntax>(lambda.Body);
        var changed = await CompoundPredicateTestFixture.Fix(document);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(changed));
        var block = Assert.IsType<BlockSyntax>((await changed.GetSyntaxRootAsync())!.DescendantNodes()
            .OfType<LambdaExpressionSyntax>().First().Body);
        var decision = Assert.IsType<IfStatementSyntax>(block.Statements[0]);
        Assert.Equal(expression.WithoutTrivia().ToString(), decision.Condition.ToString());
        Assert.Equal("return true;", Assert.Single(Assert.IsType<BlockSyntax>(decision.Statement).Statements).ToString());
        Assert.Equal("return false;", block.Statements[1].ToString());
    }

    [Fact]
    public async Task DeeperChainReportsOneDepthDiagnosticPerPass()
    {
        var document = CompoundPredicateTestFixture.Document(Prelude + """
            static bool M(int[] ids) => ids.Any(a => ids.Any(b => ids.Any(c => ids.Any(d => d == a))));
            }
            """);
        Assert.Single(await CompoundPredicateTestFixture.Diagnostics(document));
        document = await CompoundPredicateTestFixture.Fix(document);
        Assert.Single(await CompoundPredicateTestFixture.Diagnostics(document));
        document = await CompoundPredicateTestFixture.Fix(document);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("ids.Any(a => ids.Any(b => { return ids.Any(c => ids.Any(d => d == c)); }))")]
    [InlineData("ids.Any(a => ids.Any(b => Match(ids, b)))")]
    [InlineData("ids.Any(a => ids.Any(b => Fake.Any(ids, c => ids.Any(d => d == c))))")]
    [InlineData("ids.Where(a => ids.Any(b => b == a)).Any(a => ids.All(b => b == a))")]
    public async Task BlocksNamedStagesAndUncataloguedLambdasBreakCompactDepth(string query)
    {
        var source = Prelude + "static bool M(int[] ids) => " + query + ";"
            + "static bool Match(int[] ids, int value) => ids.Any(c => ids.Any(d => d == value));}"
            + "static class Fake { public static bool Any(int[] source, System.Func<int, bool> predicate) => true; }";
        var document = CompoundPredicateTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task ReviewedConjunctionProducesFlatGuardsAndExplicitOutcomes()
    {
        var document = CompoundPredicateTestFixture.Document(Prelude + """
            static bool M(int[] ids, int[] parents) => ids.Any(id => IsLab(id)
                && id > 0 && parents.Any(parent => parent == id));
            }
            """);
        var changed = await CompoundPredicateTestFixture.Fix(document);
        var lambda = (await changed.GetSyntaxRootAsync())!.DescendantNodes().OfType<LambdaExpressionSyntax>().First();
        var block = Assert.IsType<BlockSyntax>(lambda.Body);
        Assert.Equal(4, block.Statements.Count);
        foreach (var guard in block.Statements.OfType<IfStatementSyntax>())
        {
            var exit = Assert.Single(Assert.IsType<BlockSyntax>(guard.Statement).Statements);
            Assert.Equal("return false;", exit.ToString());
        }

        Assert.Equal("return true;", block.Statements.Last().ToString());
    }

    [Theory]
    [InlineData("Fake.Any(ids, id => IsLab(id) && parents.Any(parent => parent == id))")]
    [InlineData("ids.Any(id => IsLab(id) && Fake.Any(parents, parent => parent == id))")]
    public async Task UserMethodsWithFrameworkNamesAreAccepted(string query)
    {
        var source = Prelude + "static bool M(int[] ids, int[] parents) => " + query + ";}"
            + "static class Fake { public static bool Any(int[] source, System.Func<int, bool> predicate) => true; }";
        var document = CompoundPredicateTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task SourceDefinedEnumerableIsNotFrameworkMetadata()
    {
        const string source = """
            using System;
            using System.Linq;
            namespace System.Linq {
                public static class Enumerable {
                    public static bool Any<T>(this T[] source, Func<T, bool> predicate) => true;
                }
            }
            class C {
                static bool M(int[] ids, int[] parents) => ids.Any(id => id > 0 && parents.Any(parent => parent == id));
            }
            """;
        var document = CompoundPredicateTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task OriginalRegistryShapeIsReported()
    {
        const string source = """
            using System.Linq;
            class C {
                sealed record Lesson(int Id, bool IsLab);
                sealed record Group(int[] Parents, Lesson[] Lessons);
                static bool M(int[] ids, Group[] groups) => ids.Any(id => IsLab(id)
                    && groups.Any(group => group.Parents.Any(parent => parent == id)
                        && group.Lessons.Where(lesson => lesson.Id == id).Any(lesson => lesson.IsLab)));
                static bool IsLab(int id) => id > 0;
            }
            """;
        var document = CompoundPredicateTestFixture.Document(source);
        Assert.Equal(2, (await CompoundPredicateTestFixture.Diagnostics(document)).Length);
        var changed = await CompoundPredicateTestFixture.Fix(document);
        changed = await CompoundPredicateTestFixture.Fix(changed);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(changed));
    }

    [Fact]
    public async Task NullablePatternFlowSurvivesSavedSource()
    {
        const string source = """
            using System.Linq;
            class C {
                static bool M(string?[] values, int[] lengths) => values.Any(value => value is not null
                    && lengths.Any(length => length == value.Length));
            }
            """;
        var changed = await CompoundPredicateTestFixture.Fix(CompoundPredicateTestFixture.Document(source));
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(changed));
    }

    [Fact]
    public async Task PragmaAndGeneratedCodeAreRespected()
    {
        var body = Prelude + "static bool M(int[] ids) => ids.Any(id => IsLab(id) && ids.Any(p => p == id));}";
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(
            CompoundPredicateTestFixture.Document("#pragma warning disable CR0403\n" + body)));
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(
            CompoundPredicateTestFixture.Document("// <auto-generated/>\n" + body)));
    }

    private const string Prelude = "using System.Linq; class C { static bool IsLab(int id) => id > 0; ";
}
