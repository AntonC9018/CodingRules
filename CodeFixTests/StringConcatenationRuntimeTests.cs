using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodingRules;

public sealed partial class StringConcatenationTests
{
    [Theory]
    [InlineData("Next(\"a\") + Next(null)", "a,null|a")]
    [InlineData("Next(null) + Next(\"b\")", "null,b|b")]
    [InlineData("Next(null) + Next(null)", "null,null|")]
    [InlineData("Next(\"a\") + (Next(\"b\") + Next(\"c\"))", "a,b,c|abc")]
    [InlineData("Next(\"a\") + Fail() + Next(\"skipped\")", "a,throw|failed")]
    public async Task PreservesLeftToRightEvaluationNullsAndExceptions(string expression, string expected)
    {
        var document = StatementOperationTestFixture.Document("""
            using System;
            using System.Collections.Generic;
            public static class C
            {
                static readonly List<string> trace = new();
                static string? Next(string? value) { trace.Add(value ?? "null"); return value; }
                static string Fail() { trace.Add("throw"); throw new InvalidOperationException(); }
                public static string Run()
                {
                    trace.Clear();
                    string result;
                    try { result = EXPRESSION; }
                    catch (InvalidOperationException) { result = "failed"; }
                    return string.Join("|", string.Join(",", trace), result);
                }
            }
            """.Replace("EXPRESSION", expression));
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        Assert.Single(await Diagnostics(document));
        document = await FixAll(document);
        Assert.Empty(await Diagnostics(document));
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
    }

    [Theory]
    [InlineData("\"{\\\"quoted\\\"}\\n\\t\\\\\"")]
    [InlineData("@\"{\"\"quoted\"\"}\n\\\"")]
    [InlineData("\"\"\"{\"raw\"}\\\"\"\"")]
    [InlineData("\"\\0\\u2028\\u2029\"")]
    public async Task PreservesRegularVerbatimAndRawLiteralValues(string literal)
    {
        var document = StatementOperationTestFixture.Document("""
            public static class C
            {
                public static string Run()
                {
                    string? text = null;
                    return LITERAL + text + LITERAL;
                }
            }
            """.Replace("LITERAL", literal));
        var expected = await StatementOperationTestFixture.Run(document);
        Assert.Single(await Diagnostics(document));
        document = await FixAll(document);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        Assert.Empty(await Diagnostics(document));
    }

    [Fact]
    public async Task PreservesSelectedStringOverloadAgainstHandlerAndFormattableAlternatives()
    {
        var document = StatementOperationTestFixture.Document("""
            using System;
            using System.Runtime.CompilerServices;
            [InterpolatedStringHandler]
            public ref struct Handler
            {
                public Handler(int literalLength, int formattedCount) { }
                public void AppendLiteral(string value) { }
                public void AppendFormatted<T>(T value) { }
            }
            public static class C
            {
                static string Choose(string value) => value;
                static string Choose(Handler value) => "handler";
                static string Choose(FormattableString value) => "formattable";
                static string Choose(object value) => "object";
                static string Next() => "file";
                public static string Run() => Choose(Next() + ".lock");
            }
            """);
        Assert.Equal("file.lock", await StatementOperationTestFixture.Run(document));
        Assert.Single(await Diagnostics(document));
        var bindings = await Bindings(document);
        document = await FixAll(document);
        Assert.Empty(await Diagnostics(document));
        Assert.Equal(bindings, await Bindings(document));
        Assert.Equal("file.lock", await StatementOperationTestFixture.Run(document));
    }

    [Theory]
    [InlineData(false, "gate:False")]
    [InlineData(true, "gate:True,next,append:file.lock")]
    public async Task KeepsConcatenationInsideGatedAndShortCircuitingHandler(bool enabled, string expected)
    {
        var document = StatementOperationTestFixture.Document("""
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            [InterpolatedStringHandler]
            public ref struct Handler
            {
                public Handler(int literalLength, int formattedCount, bool enabled, out bool shouldAppend)
                { C.Trace.Add($"gate:{enabled}"); shouldAppend = enabled; }
                public bool AppendFormatted(string value) { C.Trace.Add($"append:{value}"); return false; }
            }
            public static class C
            {
                public static readonly List<string> Trace = new();
                static string Next() { Trace.Add("next"); return "file"; }
                static string Later() { Trace.Add("later"); return "wrong"; }
                static void Use(bool enabled, [InterpolatedStringHandlerArgument("enabled")] ref Handler handler) { }
                public static string Run()
                {
                    Trace.Clear();
                    Use(ENABLED, $"{Next() + ".lock"}{Later()}");
                    return string.Join(",", Trace);
                }
            }
            """.Replace("ENABLED", enabled ? "true" : "false"));
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        Assert.Single(await Diagnostics(document));
        var bindings = await Bindings(document);
        document = await FixAll(document);
        Assert.Empty(await Diagnostics(document));
        Assert.Equal(bindings, await Bindings(document));
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
    }

    [Theory]
    [InlineData("(file + \".lock\").Length.ToString()", "9")]
    [InlineData("(file + \".lock\")?[0].ToString() ?? \"missing\"", "f")]
    [InlineData("(true ? file : null) + \".lock\"", "file.lock")]
    [InlineData("file + (file = \"changed\")", "filechanged")]
    public async Task PreservesReceiverGroupingAndComplexStringOperands(string expression, string expected)
    {
        var document = StatementOperationTestFixture.Document("""
            public static class C
            {
                public static string Run()
                {
                    string file = "file";
                    return EXPRESSION;
                }
            }
            """.Replace("EXPRESSION", expression));
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        Assert.Single(await Diagnostics(document));
        document = await FixAll(document);
        Assert.Empty(await Diagnostics(document));
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
    }

    [Fact]
    public async Task SingleActionIsExportedAndIdempotentAfterSaving()
    {
        var document = StatementOperationTestFixture.Document("class C { string M(string file) => file + \".lock\"; }");
        var diagnostic = Assert.Single(await Diagnostics(document));
        var actions = new System.Collections.Generic.List<CodeAction>();
        await new StringConcatenationCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action), CancellationToken.None));
        var operation = Assert.Single((await Assert.Single(actions).GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        document = await StatementOperationTestFixture.Reparse(operation.ChangedSolution.GetDocument(document.Id)!);
        await StatementOperationTestFixture.Compiles(document);
        var root = await document.GetSyntaxRootAsync();
        Assert.Single(root!.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>());
        Assert.Empty(await Diagnostics(document));
    }
}
