using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

/// <summary>
/// Exact output of generated steps: TODO_ placeholders, no result variables or
/// aliases of unchanged values, and no blank lines added around generated code.
/// The pilot sources are the reviewed shapes that motivated each rule.
/// </summary>
public sealed class GeneratedStepShapeTests
{
    [Fact]
    public async Task ConditionExtractionIsACompactGuardClauseFunctionDefinedBeforeUse()
    {
        const string source = """
            public static class C
            {
                public static int M(bool a, bool b, bool c, bool d, int x)
                {
                    var y = x;

                    if (a && b && c && d)
                    {
                        return y;
                    }
                    return 0;
                }
            }
            """;
        const string expected = """
            public static class C
            {
                public static int M(bool a, bool b, bool c, bool d, int x)
                {
                    var y = x;

                    bool TODO_CONDITION()
                    {
                        if (!a)
                        {
                            return false;
                        }
                        if (!b)
                        {
                            return false;
                        }
                        if (!c)
                        {
                            return false;
                        }
                        if (!d)
                        {
                            return false;
                        }
                        return true;
                    }
                    if (TODO_CONDITION())
                    {
                        return y;
                    }
                    return 0;
                }
            }
            """;
        var document = InlineConditionTestFixture.Document(source);
        var changed = await InlineConditionTestFixture.Fix(document, InlineConditionCodeFixProvider.ExtractKey);
        Assert.Equal(expected, (await changed.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(StatementOperationCodeFixProvider.ExtractKey, """
                int TODO_CALCULATION()
                {
                    return Get(Get(x));
                }
                var y = Use(TODO_CALCULATION());
                return y;
        """)]
    [InlineData(StatementOperationCodeFixProvider.ExpandKey, """
                int TODO_VARIABLE = Get(Get(x));
                var y = Use(TODO_VARIABLE);
                return y;
        """)]
    public async Task ValueExtractionStagesOnlyTheNestedWork(string key, string body)
    {
        const string prefix = """
            public static class C
            {
                static int Get(int value) => value;
                static int Use(int value) => value;
                public static int M(int x)
                {

            """;
        const string suffix = "\n    }\n}";
        var document = StatementOperationTestFixture.Document(prefix + "        var y = Use(Get(Get(x)));\n        return y;" + suffix);
        var changed = await StatementOperationTestFixture.Fix(document, key);
        Assert.Equal(prefix + body + suffix, (await changed.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task AssignmentInAConditionIsHoistedIntoItsDeclaration()
    {
        const string source = """
            using System;
            public static class C
            {
                public sealed class S { public int? Year; public int? Grade; public string? Name; }
                public static int CompareScopes(S x, S y)
                {
                    int c;
                    if ((c = Nullable.Compare(x.Year, y.Year)) != 0)
                    {
                        return c;
                    }
                    if ((c = string.Compare(x.Name, y.Name, StringComparison.Ordinal)) != 0)
                    {
                        return c;
                    }
                    return Nullable.Compare(x.Grade, y.Grade);
                }
            }
            """;
        var document = InlineConditionTestFixture.Document(source);
        var diagnostics = await InlineConditionTestFixture.Diagnostics(document);
        Assert.Equal(2, diagnostics.Length);
        var first = await InlineConditionTestFixture.Fix(document, InlineConditionCodeFixProvider.ExpandKey);
        var second = await InlineConditionTestFixture.Fix(await StatementOperationTestFixture.Reparse(first), InlineConditionCodeFixProvider.ExpandKey);
        Assert.Equal(source
            .Replace("int c;\n        if ((c = Nullable.Compare(x.Year, y.Year)) != 0)",
                "int c = Nullable.Compare(x.Year, y.Year);\n        if (c != 0)")
            .Replace("if ((c = string.Compare(x.Name, y.Name, StringComparison.Ordinal)) != 0)",
                "c = string.Compare(x.Name, y.Name, StringComparison.Ordinal);\n        if (c != 0)"),
            (await second.GetTextAsync()).ToString());
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(second));
    }

    [Fact]
    public async Task DecisionValueExtractionLeavesNoBlankLine()
    {
        const string source = """
            public static class C
            {
                public static bool M(bool a, bool b, int c)
                {
                    bool found;
                    if (a)
                    {
                        found = b;
                    }
                    else
                    {
                        found = c > 0;
                    }
                    return found;
                }
            }
            """;
        const string expected = """
            public static class C
            {
                public static bool M(bool a, bool b, int c)
                {
                    bool TODO_CONDITION()
                    {
                        if (!a)
                        {
                            return c > 0;
                        }
                        return b;
                    }
                    bool found = TODO_CONDITION();
                    return found;
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostic = Assert.Single(await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DecisionValueAnalyzer()))
            .GetAnalyzerDiagnosticsAsync());
        var actions = new List<CodeAction>();
        await new DecisionValueCodeFixProvider().RegisterCodeFixesAsync(
            new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        var operation = Assert.Single((await Assert.Single(actions).GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        Assert.Equal(expected, (await operation.ChangedSolution.GetDocument(document.Id)!.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task LevenshteinInnerLoopKeepsIndexOffsetsInline()
    {
        const string source = """
            using System;
            public static class C
            {
                public static string Run() => Distance("kitten", "sitting").ToString();
                static int Distance(string first, string second)
                {
                    var previous = new int[second.Length + 1];
                    var current = new int[second.Length + 1];
                    for (var j = 0; j <= second.Length; j++) previous[j] = j;
                    for (var i = 1; i <= first.Length; i++)
                    {
                        current[0] = i;
                        for (var j = 1; j <= second.Length; j++)
                        {
                            var cost = first[i - 1] == second[j - 1] ? 0 : 1;
                            current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                        }
                        (previous, current) = (current, previous);
                    }
                    return previous[second.Length];
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        // The nested Min and its offsets form one finding; a[j - 1] alone is no calculation.
        Assert.Single(await StatementOperationTestFixture.Diagnostics(document, includeReturns: true));
        foreach (var key in new[] { StatementOperationCodeFixProvider.ExtractKey, StatementOperationCodeFixProvider.ExpandKey })
        {
            var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, key));
            var text = (await changed.GetTextAsync()).ToString();
            Assert.DoesNotContain("= j - 1;", text);
            Assert.Equal("3", await StatementOperationTestFixture.Run(changed));
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed, includeReturns: true));
        }
    }

    [Theory]
    [InlineData("sb.Append('\\'').Append(lesson.Name).Append('\\'').Append(\" slot \").Append(lesson.Index).Append(\", subgroup: \").Append(sub ?? \"all\");")]
    [InlineData("Assert(Directory.GetFiles(path));")]
    [InlineData("notices.Add(new(\"omitted\", $\"Expelled registry student: {registryName}\"));")]
    [InlineData("var bytes = Encoding.UTF8.GetBytes(text);")]
    [InlineData("var copy = registryName;")]
    [InlineData("var values = notices.Where(notice => notice.Kind == \"omitted\").Select(notice => notice.Detail).ToList();")]
    public async Task ReviewedPilotShapesAreAccepted(string statement)
    {
        var source = """
            using System.Collections.Generic;
            using System.IO;
            using System.Linq;
            using System.Text;
            public static class C
            {
                public sealed record Notice(string Kind, string Detail);
                public sealed class Lesson { public string Name = ""; public int Index; }
                static void Assert(System.Collections.IEnumerable items) { }
                public static void M(StringBuilder sb, Lesson lesson, string? sub, string path, List<Notice> notices, string registryName, string text)
                {
                    STATEMENT
                }
            }
            """.Replace("STATEMENT", statement);
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document, includeReturns: true));
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("!parser.IsEmpty || name is null")]
    [InlineData("parser.IsEmpty && name is not null && name.Length > 0")]
    public async Task ShortSimpleGuardsAreAccepted(string condition)
    {
        var source = """
            public static class C
            {
                public sealed class Parser { public bool IsEmpty => true; }
                public static bool M(Parser parser, string? name)
                {
                    if (CONDITION)
                    {
                        return false;
                    }
                    return true;
                }
            }
            """.Replace("CONDITION", condition);
        var document = InlineConditionTestFixture.Document(source);
        await InlineConditionTestFixture.Compiles(document);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(document));
    }
}
