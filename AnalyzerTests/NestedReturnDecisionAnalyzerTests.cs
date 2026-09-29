using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using SourceGeneration.Testing;
using Xunit;

namespace CodingRules;

public sealed class NestedReturnDecisionAnalyzerTests
{
    [Fact]
    public async Task ReportsNestedTernaryInsideLock()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            using System.Collections.Generic;

            class C
            {
                private readonly object _gate = new object();

                string? Find(Dictionary<string, string> jobs, string id)
                {
                    lock (_gate)
                    {
                        return {{InterpolateDiagnostic("jobs.TryGetValue(id, out var job) ? job : null", NestedReturnDecisionAnalyzer.TernaryRule)}};
                    }
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task ReportsTernaryWithThrowArmInsideLock()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            using System.Collections.Generic;

            class C
            {
                private readonly object _gate = new object();

                string Report(Dictionary<string, string> steps, string step)
                {
                    lock (_gate)
                    {
                        return {{InterpolateDiagnostic("steps.TryGetValue(step, out var progress)\n                ? progress\n                : throw new System.InvalidOperationException($\"Unknown step '{step}'.\")", NestedReturnDecisionAnalyzer.TernaryRule)}};
                    }
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task ReportsCoalesceInsideIfBlock()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                const string FallbackValue = "fallback";

                string? Read(string key) => null;

                string Load(string key, bool valid)
                {
                    if (!valid)
                    {
                        return {{InterpolateDiagnostic("Read(key) ?? FallbackValue", NestedReturnDecisionAnalyzer.CoalesceRule)}};
                    }

                    return FallbackValue;
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task ReportsNullableCallInsideCaseArm()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            enum Mode
            {
                Cached,
                Live,
            }

            class Entry { }

            class Cache
            {
                public Entry? Get(string key) => null;
            }

            class C
            {
                Entry? Read(Cache cache, Mode mode, string key)
                {
                    switch (mode)
                    {
                        case Mode.Cached:
                            return {{InterpolateDiagnostic("cache.Get(key)", NestedReturnDecisionAnalyzer.NullableCallRule)}};
                    }

                    return null;
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task ReportsBooleanDecisionsInCatchLoopAndNestedIf()
    {
        var source = TestCode.Create($$"""
            class C
            {
                bool Check() => true;
                bool ShouldRetry(int attempt) => attempt < 3;
                void Work() { }

                bool Retry(int attempt)
                {
                    try
                    {
                        Work();
                    }
                    catch (System.IO.IOException)
                    {
                        return {{InterpolateDiagnostic("ShouldRetry(attempt)", NestedReturnDecisionAnalyzer.BooleanRule)}};
                    }

                    return false;
                }

                bool InLoop(System.Collections.Generic.List<int> items)
                {
                    foreach (var item in items)
                    {
                        return {{InterpolateDiagnostic("item > 0 && Check()", NestedReturnDecisionAnalyzer.BooleanRule)}};
                    }

                    return false;
                }

                bool InNestedIf(bool first, bool second)
                {
                    if (first)
                    {
                        if (second)
                        {
                            return {{InterpolateDiagnostic("Check()", NestedReturnDecisionAnalyzer.BooleanRule)}};
                        }
                    }

                    return false;
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task ReportsEmbeddedBracelessReturns()
    {
        var source = TestCode.Create($$"""
            class C
            {
                bool Check() => true;

                bool EmbeddedTernary(bool flag, int first)
                {
                    if (flag)
                        return {{InterpolateDiagnostic("first > 0 ? true : false", NestedReturnDecisionAnalyzer.TernaryRule)}};
                    return false;
                }

                bool EmbeddedBoolean(System.Collections.Generic.List<int> batch, System.Collections.Generic.List<int> items, bool allValid)
                {
                    if (batch.Count == 0)
                        return {{InterpolateDiagnostic("items.Count > 0 && allValid", NestedReturnDecisionAnalyzer.BooleanRule)}};
                    return false;
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task ReportsNestedDecisionWithoutGuard()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                private readonly object _gate = new object();

                string? Choose(bool flag)
                {
                    lock (_gate)
                    {
                        return {{InterpolateDiagnostic("flag ? \"found\" : null", NestedReturnDecisionAnalyzer.TernaryRule)}};
                    }
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task ReportsDecisionInsideFirstGuardBranch()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                string? M(bool flag, bool inner)
                {
                    if (flag)
                    {
                        return {{InterpolateDiagnostic("inner ? \"found\" : null", NestedReturnDecisionAnalyzer.TernaryRule)}};
                    }

                    return null;
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task ReportsDeeplyNestedDecisions()
    {
        var source = TestCode.Create($$"""
            class C
            {
                bool Check() => true;
                private readonly object _gate = new object();

                bool InTryFinally(bool flag)
                {
                    try
                    {
                        lock (_gate)
                        {
                            return {{InterpolateDiagnostic("flag ? Check() : false", NestedReturnDecisionAnalyzer.TernaryRule)}};
                        }
                    }
                    finally
                    {
                    }
                }

                bool InNestedBlock(bool flag)
                {
                    {
                        {
                            return {{InterpolateDiagnostic("flag ? Check() : false", NestedReturnDecisionAnalyzer.TernaryRule)}};
                        }
                    }
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task ReportsTwoNestedDecisionsWithDistinctRules()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                string? Read() => null;

                string? Two(bool flag, bool choice)
                {
                    if (flag)
                    {
                        return {{InterpolateDiagnostic("choice ? \"found\" : null", NestedReturnDecisionAnalyzer.TernaryRule)}};
                    }

                    lock (new object())
                    {
                        return {{InterpolateDiagnostic("Read() ?? \"fallback\"", NestedReturnDecisionAnalyzer.CoalesceRule)}};
                    }
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task AllowsFinalPositionDecisionReturns()
    {
        var source = TestCode.Create("""
            #nullable enable
            class C
            {
                string? Read() => null;
                bool Check() => true;

                string? Ternary(int index, string[] fields)
                {
                    if (index < 0)
                    {
                        return null;
                    }

                    var value = fields[index].Trim();
                    return value.Length == 0 ? null : value;
                }

                string Coalesce(bool stop)
                {
                    if (stop)
                    {
                        return "stop";
                    }

                    return Read() ?? "fallback";
                }

                bool Boolean(string name)
                {
                    if (name.Contains('/'))
                    {
                        return true;
                    }

                    return name.Contains("..");
                }

                string? NullableCall(bool stop)
                {
                    if (stop)
                    {
                        return null;
                    }

                    return Read();
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task AllowsSwitchExpressionReturns()
    {
        var source = TestCode.Create("""
            class C
            {
                bool Switch(bool flag)
                {
                    if (flag)
                    {
                        return true;
                    }

                    return flag switch { true => true, false => false };
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task AllowsDecisionReturnsInsideNestedFunctions()
    {
        var source = TestCode.Create("""
            class C
            {
                bool Boundaries(bool flag)
                {
                    bool Local()
                    {
                        return flag ? true : false;
                    }

                    System.Func<bool> callback = () =>
                    {
                        return flag ? true : false;
                    };

                    System.Func<bool> simple = () => flag ? true : false;

                    if (flag)
                    {
                        return true;
                    }

                    _ = Local();
                    _ = callback();
                    return simple();
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        var source = TestCode.Create("""
            // <auto-generated/>
            class C
            {
                bool M(bool flag)
                {
                    lock (new object())
                    {
                        return flag ? true : false;
                    }
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task AllowsIfElseChainsOfExplicitReturns()
    {
        var source = TestCode.Create("""
            class C
            {
                bool Chain(bool first, bool second)
                {
                    if (first)
                    {
                        return true;
                    }
                    else if (second)
                    {
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task AllowsTopLevelDecisionReturn()
    {
        var source = TestCode.Create("""
            class C
            {
                bool Lone(bool flag)
                {
                    return flag ? true : false;
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task SkipsNestedDecisionWithComments()
    {
        var source = TestCode.Create("""
            class C
            {
                bool Check() => true;

                bool CommentInside(bool flag)
                {
                    lock (new object())
                    {
                        return flag ? /* keep */ true : false;
                    }
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task SkipsUnsupportedNestedDecisionForms()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            struct Choice
            {
                public static bool operator true(Choice value) => true;
                public static bool operator false(Choice value) => false;
            }

            class Base { }
            class Derived : Base { }
            class Wrapper
            {
                public static implicit operator Wrapper(Base? value) => new Wrapper();
                public static implicit operator Wrapper(Derived value) => new Wrapper();
            }

            class C
            {
                Base? Read() => null;

                Wrapper Conditional(bool choice)
                {
                    lock (new object())
                    {
                        return choice ? new Derived() : new Base();
                    }
                }

                Wrapper Coalesce()
                {
                    lock (new object())
                    {
                        return Read() ?? new Derived();
                    }
                }

                Wrapper CoalesceThrow()
                {
                    lock (new object())
                    {
                        return Read() ?? throw new System.InvalidOperationException();
                    }
                }

                string? CustomCondition(Choice choice)
                {
                    lock (new object())
                    {
                        return choice ? null : "found";
                    }
                }

                System.Collections.Generic.IEnumerable<int> Iterator(System.Collections.Generic.IEnumerable<int> items)
                {
                    foreach (var item in items)
                    {
                        yield return item;
                    }
                }

                ref int RefReturn(ref int value)
                {
                    lock (new object())
                    {
                        return ref value;
                    }
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task AllowsDecisionsInConditionsAndArguments()
    {
        var source = TestCode.Create("""
            class C
            {
                bool Check() => true;

                bool InCondition(bool a, bool b)
                {
                    if (a ? b : Check())
                    {
                        return true;
                    }

                    return false;
                }

                int Sum(int first, int second)
                {
                    return Add(first > 0 ? first : 0, second);
                }

                int Add(int left, int right) => left + right;
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    private static AnalyzerTestBuilder<NestedReturnDecisionAnalyzer, DefaultVerifier> Builder()
    {
        return AnalyzerTestBuilder.For<NestedReturnDecisionAnalyzer, DefaultVerifier>();
    }
}
