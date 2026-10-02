using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CodingRules;

public sealed class SpanRuntimeTests
{
    [Theory]
    [InlineData("Trim", "Length")]
    [InlineData("TrimStart", "Length")]
    [InlineData("TrimEnd", "Length")]
    [InlineData("Trim", "Equal")]
    [InlineData("TrimStart", "Equal")]
    [InlineData("TrimEnd", "Equal")]
    [InlineData("Trim", "NotEqual")]
    [InlineData("TrimStart", "Reversed")]
    [InlineData("TrimEnd", "ReversedNotEqual")]
    public async Task UnicodeUtf16AndCultureResultsMatchOriginal(string trim, string inspection)
    {
        var expression = inspection switch
        {
            "Length" => "text." + trim + "().Length",
            "Equal" => "text." + trim + "() == \"I\\0\\ud800\"",
            "NotEqual" => "text." + trim + "() != \"I\\0\\ud800\"",
            "Reversed" => "\"I\\0\\ud800\" == text." + trim + "()",
            _ => "\"I\\0\\ud800\" != text." + trim + "()",
        };
        var document = SpanTestFixture.Document($$"""
            using System;
            using System.Globalization;
            public class C {
                static {{(inspection == "Length" ? "int" : "bool")}} Inspect(string text) => {{expression}};
                public static string Run() {
                    var original = CultureInfo.CurrentCulture;
                    try {
                        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                        string[] inputs = { "", "I\0\ud800", " I\0\ud800", "I\0\ud800 ", " I\0\ud800 ", " \t\r\n", "\u00a0I\0\ud800\u2003\u2028", "\u200bI\0\ud800\u200b", "\udfff", "İ" };
                        var result = "";
                        foreach (var value in inputs) result += Inspect(value) + ";";
                        return result;
                    } finally { CultureInfo.CurrentCulture = original; }
                }
            }
            """);
        var expected = await StatementOperationTestFixture.Run(document);
        var changed = await SpanTestFixture.Fix(document);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(changed));
        Assert.Empty(await SpanTestFixture.Diagnostics(changed));
    }

    [Theory]
    [InlineData("null!", "receiver;NullReferenceException")]
    [InlineData("\" a \"", "receiver;later;True")]
    [InlineData("throw new InvalidOperationException()", "receiver;InvalidOperationException")]
    public async Task ReceiverRunsOnceAndNullThrowsBeforeLaterOperand(string value, string expected)
    {
        var document = SpanTestFixture.Document($$"""
            using System;
            public class C {
                static string log = "";
                static string Receiver() { log += "receiver;"; {{(value.StartsWith("throw") ? value : "return " + value)}}; }
                static int Later() { log += "later;"; return 1; }
                static bool Inspect() => Receiver().Trim().Length == Later();
                public static string Run() { log = ""; try { var value = Inspect(); return log + value; } catch (Exception e) { return log + e.GetType().Name; } }
            }
            """);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        var changed = await SpanTestFixture.Fix(document);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(changed));
    }

    [Fact]
    public async Task LazyBranchesLoopReevaluationGetterAndIndexRemainAtSelectedSite()
    {
        var document = SpanTestFixture.Document("""
            public class C {
                static int calls;
                static string Value { get { calls++; return " a "; } }
                static int Index { get { calls++; return 0; } }
                static string[] values = { " a " };
                public static string Run() {
                    calls = 0;
                    bool prefix = false;
                    if (prefix && Value.Trim() == "a") calls += 100;
                    var unused = prefix ? Value.Trim().Length : 0;
                    var total = 0;
                    for (int i = 0; i < 3; i++) total += values[Index].Trim().Length;
                    return calls + ":" + total + ":" + unused;
                }
            }
            """);
        Assert.Equal("3:3:0", await StatementOperationTestFixture.Run(document));
        Assert.Equal(3, (await SpanTestFixture.Diagnostics(document)).Length);
        while (!(await SpanTestFixture.Diagnostics(document)).IsEmpty) document = await SpanTestFixture.Fix(document);
        Assert.Equal("3:3:0", await StatementOperationTestFixture.Run(document));
    }

    [Fact]
    public async Task AwaitedReceiverRemainsInCallerAndSpanStaysInSynchronousHelper()
    {
        var document = SpanTestFixture.Document("""
            public class C {
                static async System.Threading.Tasks.Task<string> Read() { await System.Threading.Tasks.Task.Yield(); return " a "; }
                static async System.Threading.Tasks.Task<int> Inspect() { var result = (await Read()).Trim().Length; await System.Threading.Tasks.Task.Yield(); return result; }
                public static string Run() => Inspect().GetAwaiter().GetResult().ToString();
            }
            """);
        var changed = await SpanTestFixture.Fix(document);
        Assert.Equal("1", await StatementOperationTestFixture.Run(changed));
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("InspectTextLength((await Read()))", text);
        Assert.Empty(await SpanTestFixture.Diagnostics(changed, all: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NullableAnnotationsAndRuntimeNullArePreserved(bool nullable)
    {
        var document = SpanTestFixture.Document("public class C { static int Inspect(string" + (nullable ? "?" : "") + " text) { if (text == null) text = null!; return text!.Trim().Length; } public static string Run() { try { var result = Inspect(null!); return result.ToString(); } catch (System.NullReferenceException) { return \"NRE\"; } } }");
        if (!nullable) document = document.Project.WithCompilationOptions(new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Disable)).GetDocument(document.Id)!;
        var changed = await SpanTestFixture.Fix(document);
        Assert.Equal("NRE", await StatementOperationTestFixture.Run(changed));
    }

    [Fact]
    public async Task WarmedPaddedStringInspectionEliminatesSelectedTrimAllocation()
    {
        var document = SpanTestFixture.Document("""
            using System;
            public class C {
                static int Inspect(string text) => text.Trim().Length;
                public static string Run() {
                    var text = "  a padded string for allocation measurement  ";
                    long checksum = 0;
                    for (int i = 0; i < 20000; i++) checksum += Inspect(text);
                    var start = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 20000; i++) checksum += Inspect(text);
                    var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
                    return allocated + ":" + checksum;
                }
            }
            """);
        var original = (await StatementOperationTestFixture.Run(document)).Split(':');
        var changed = await SpanTestFixture.Fix(document);
        var fixedResult = (await StatementOperationTestFixture.Run(changed)).Split(':');
        Assert.Equal(original[1], fixedResult[1]);
        Assert.True(long.Parse(original[0]) > 20000);
        Assert.Equal(0, long.Parse(fixedResult[0]));
        var text = (await changed.GetTextAsync()).ToString();
        Assert.DoesNotContain("Trim()", text);
        Assert.DoesNotContain("ToString", text);
        Assert.DoesNotContain("=>", text);
    }
}
