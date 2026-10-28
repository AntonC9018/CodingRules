using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Formatting;
using Xunit;

namespace CodingRules;

public sealed partial class FormattingReuseTests
{
    [Fact]
    public async Task ChoppedInvocationAndConstructorArgumentsMoveTheFirstArgument()
    {
        var document = Document("""
            class Value { public Value(int first, int second) { } }
            class C
            {
                void Use(int first, int second) { }
                void Use(Value value) { }
                void Run()
                {
                    Use(1,
                        2);
                    _ = new Value(1,
                        2);
                    Value implicitValue = new(1,
                        2);
                    Use(first: 1,
                        second: 2);
                }
            }
            """);

        var diagnostics = (await Diagnostics(document)).Where(item => item.Id == "CR0806").ToArray();
        Assert.Equal(4, diagnostics.Length);
        var changed = await Formatter.FormatAsync(await FixAll(document));
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("Use(\n            1,\n            2);", text);
        Assert.Contains("new Value(\n            1,\n            2);", text);
        Assert.Contains("new(\n            1,\n            2);", text);
        Assert.Contains("Use(\n            first: 1,\n            second: 2);", text);
        await AssertStableLayout(document, changed);
    }

    [Fact]
    public async Task LambdaAndSoleConstructedInvocationKeepTheirExistingExceptions()
    {
        var document = Document("""
            using System;
            class Value { public Value(int first, int second) { } }
            class C
            {
                void Use(Func<int, int> transform, int value) { }
                void Use(Value value) { }
                void Run()
                {
                    Use(value =>
                    {
                        return value;
                    },
                    2);
                    Use(
                        new(
                            1,
                            2));
                }
            }
            """);

        var diagnostics = await Diagnostics(document);
        Assert.DoesNotContain(diagnostics, item => item.Id == "CR0806");
        Assert.Contains(diagnostics, item => item.Id == "CR0802");
        var changed = await Formatter.FormatAsync(await FixAll(document));
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("Use(new(\n            1,\n            2));", text);
        Assert.Contains("value =>", text);
        await AssertStableLayout(document, changed);
    }

    [Fact]
    public async Task ChoppedArgumentsPreserveCommentsAndMultilineStringTokens()
    {
        var document = Document(""""
            class C
            {
                void Use(string first, string second, int third) { }
                void Run()
                {
                    Use("first", /* keep this */
                        """
                        line one
                        line two
                        """,
                        3);
                    Use(/* keep beside opener */ "first",
                        "second",
                        3);
                }
            }
            """");

        var diagnostics = await Diagnostics(document);
        Assert.Single(diagnostics.Where(item => item.Id == "CR0806"));
        var changed = await Formatter.FormatAsync(await FixAll(document));
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("Use(\n            \"first\", /* keep this */", text);
        Assert.Contains("line one\nline two", text);
        Assert.Contains("/* keep beside opener */", text);
        Assert.Contains("Use(/* keep beside opener */ \"first\",\n", text);
        await AssertStableLayout(document, changed);
    }

    [Fact]
    public async Task DirectivesAndCallerSensitiveChopsDoNotOfferFixes()
    {
        var directiveDocument = Document("""
            class C
            {
                void Use(int first, int second, int third) { }
                void Run()
                {
                    Use(1,
            #if true
                        2,
            #else
                        3,
            #endif
                        4);
                }
            }
            """);
        Assert.DoesNotContain(await Diagnostics(directiveDocument), item => item.Id == "CR0806");

        var callerDocument = Document("""
            using System.Runtime.CompilerServices;
            class C
            {
                static int Line([CallerLineNumber] int line = 0) { return line; }
                static string Capture(int value, [CallerArgumentExpression("value")] string expression = "") => expression;
                static int Use(int first, int second) { return first + second; }
                static string Run()
                {
                    var captured = Capture(Use(1,
                        2));
                    var line = Use(Line(),
                        2);
                    return captured + line;
                }
            }
            """);
        var diagnostics = (await Diagnostics(callerDocument)).Where(item => item.Id == "CR0806").ToArray();
        Assert.Equal(2, diagnostics.Length);
        foreach (var diagnostic in diagnostics)
        {
            var actions = new List<CodeAction>();
            await new InitializerCallLayoutCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(callerDocument,
                diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
            Assert.Empty(actions);
        }
    }
}
