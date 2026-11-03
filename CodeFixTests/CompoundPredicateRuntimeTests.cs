using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class CompoundPredicateRuntimeTests
{
    [Theory]
    [InlineData("A() && B() && Nested()")]
    [InlineData("A() || B() || Nested()")]
    [InlineData("A() && (B() || Nested())")]
    [InlineData("(A() || B()) && Nested()")]
    [InlineData("(A() && B()) || Nested()")]
    [InlineData("(A() && B()) || (CValue() && Nested())")]
    [InlineData("!(A() && (B() || Nested()))")]
    public async Task EveryTruthCombinationRetainsExactShortCircuitTrace(string expression)
    {
        var source = """
            using System;
            using System.Linq;
            using System.Collections.Generic;
            class C {
                static List<string> trace = new();
                static int bits;
                static bool A() { trace.Add("a"); return (bits & 1) != 0; }
                static bool B() { trace.Add("b"); return (bits & 2) != 0; }
                static bool CValue() { trace.Add("c"); return (bits & 4) != 0; }
                static bool Value(int value) { trace.Add("value" + value); return (bits & 8) != 0; }
                public static string Run() {
                    trace.Clear();
                    for (bits = 0; bits < 16; bits++) {
                        trace.Add("case" + bits);
                        var result = new[] { 1 }.Any(id => EXPRESSION);
                        trace.Add(result.ToString());
                    }
                    return string.Join("|", trace);
                }
            }
            """;
        // Keep a real nested predicate at the tested site instead of hiding it
        // behind a test helper, which would change the diagnostic's contract.
        source = source.Replace("EXPRESSION", expression.Replace("Nested()", "new[] { 2 }.Any(p => Value(p))"));
        await Same(source);
    }

    [Fact]
    public async Task DeferredIndexedEnumerationCaptureExceptionsAndDisposalArePreserved()
    {
        const string source = """
            using System;
            using System.Linq;
            using System.Collections.Generic;
            class C {
                static List<string> trace = new();
                static int capture;
                static IEnumerable<int> Source(string name) {
                    try { trace.Add("start:" + name); yield return 0; yield return 1; yield return 2; }
                    finally { trace.Add("dispose:" + name); }
                }
                static bool Check(int value) {
                    trace.Add("check:" + value + ":" + capture);
                    if (value == 2) { throw new InvalidOperationException(); }
                    return value == capture;
                }
                public static string Run() {
                    trace.Clear(); capture = 0;
                    var query = Source("outer").Where((id, index) => index > 0 && Source("inner").Any(p => Check(p)));
                    trace.Add("constructed"); capture = 1;
                    using (var iterator = query.GetEnumerator()) {
                        trace.Add("iterator"); iterator.MoveNext(); trace.Add(iterator.Current.ToString());
                    }
                    capture = 3;
                    try { foreach (var value in query) { trace.Add(value.ToString()); } }
                    catch (InvalidOperationException) { trace.Add("threw"); }
                    return string.Join("|", trace);
                }
            }
            """;
        await Same(source);
    }

    [Fact]
    public async Task CustomNegationEqualityAndFloatingPointNaNAreNotInverted()
    {
        const string source = """
            using System;
            using System.Linq;
            using System.Collections.Generic;
            class C {
                static List<string> trace = new();
                readonly struct Flag {
                    public static bool operator !(Flag value) { trace.Add("custom!"); return true; }
                    public static bool operator ==(Flag left, Flag right) { trace.Add("custom=="); return true; }
                    public static bool operator !=(Flag left, Flag right) { trace.Add("custom!="); return true; }
                    public override bool Equals(object? other) => false;
                    public override int GetHashCode() => 0;
                }
                public static string Run() {
                    trace.Clear();
                    var first = new[] { 0 }.Any(id => !new Flag() && new Flag() == new Flag()
                        && new[] { 0 }.Any(p => p == id));
                    var second = new[] { double.NaN, 1.0 }.Any(value => value < 0
                        && new[] { 0 }.Any(p => p == value));
                    trace.Add(first + ":" + second);
                    return string.Join("|", trace);
                }
            }
            """;
        var document = CompoundPredicateTestFixture.Document(source);
        var before = await StatementOperationTestFixture.Run(document);
        Assert.Contains("custom!|custom==", before);
        Assert.DoesNotContain("custom!=", before);
        document = await CompoundPredicateTestFixture.Fix(document);
        document = await CompoundPredicateTestFixture.Fix(document);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(document));
        Assert.Equal(before, await StatementOperationTestFixture.Run(document));
    }

    [Fact]
    public async Task DepthOnlyRewriteRetainsEvaluationAndEmptySequenceOutcomes()
    {
        const string source = """
            using System;
            using System.Linq;
            using System.Collections.Generic;
            class C {
                static List<string> trace = new();
                static IEnumerable<int> Source(int count) {
                    try { trace.Add("start" + count); for (var index = 0; index < count; index++) { yield return index; } }
                    finally { trace.Add("dispose" + count); }
                }
                public static string Run() {
                    trace.Clear();
                    for (var size = 0; size < 3; size++) {
                        var result = Source(size).Any(a => Source(size).All(b => Source(size).Any(c => c == a + b)));
                        trace.Add(result.ToString());
                    }
                    return string.Join("|", trace);
                }
            }
            """;
        await Same(source);
    }

    private static async Task Same(string source)
    {
        var document = CompoundPredicateTestFixture.Document(source);
        var before = await StatementOperationTestFixture.Run(document);
        Assert.Single(await CompoundPredicateTestFixture.Diagnostics(document));
        var changed = await CompoundPredicateTestFixture.Fix(document);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(changed));
        Assert.Equal(before, await StatementOperationTestFixture.Run(changed));
    }
}
