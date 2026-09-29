using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using SourceGeneration.Testing;
using Xunit;

namespace CodingRules;

public sealed class NestedReturnDecisionCodeFixTests
{
    [Fact]
    public async Task RewritesNestedTernaryInsideLock()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                private readonly object _gate = new object();

                string? Find(System.Collections.Generic.Dictionary<string, string> jobs, string id)
                {
                    lock (_gate)
                    {
                        return {{InterpolateDiagnostic("jobs.TryGetValue(id, out var job) ? job : null", NestedReturnDecisionAnalyzer.TernaryRule)}};
                    }
                }
            }
            """);
        const string fixedSource = """
            #nullable enable
            class C
            {
                private readonly object _gate = new object();

                string? Find(System.Collections.Generic.Dictionary<string, string> jobs, string id)
                {
                    lock (_gate)
                    {
                        if (jobs.TryGetValue(id, out var job))
                        {
                            return job;
                        }

                        return null;
                    }
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task RewritesThrowTernaryInsideLockToGuard()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                private readonly object _gate = new object();

                string Report(System.Collections.Generic.Dictionary<string, string> steps, string step)
                {
                    lock (_gate)
                    {
                        return {{InterpolateDiagnostic("steps.TryGetValue(step, out var progress)\n                ? progress\n                : throw new System.InvalidOperationException($\"Unknown step '{step}'.\")", NestedReturnDecisionAnalyzer.TernaryRule)}};
                    }
                }
            }
            """);
        const string fixedSource = """
            #nullable enable
            class C
            {
                private readonly object _gate = new object();

                string Report(System.Collections.Generic.Dictionary<string, string> steps, string step)
                {
                    lock (_gate)
                    {
                        if (!steps.TryGetValue(step, out var progress))
                        {
                            throw new System.InvalidOperationException($"Unknown step '{step}'.");
                        }

                        return progress;
                    }
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task RewritesTrueArmThrowTernaryWithoutNegation()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                private readonly object _gate = new object();

                string Start(string? step)
                {
                    lock (_gate)
                    {
                        return {{InterpolateDiagnostic("step is null ? throw new System.InvalidOperationException(\"Unknown step.\") : \"started\"", NestedReturnDecisionAnalyzer.TernaryRule)}};
                    }
                }
            }
            """);
        const string fixedSource = """
            #nullable enable
            class C
            {
                private readonly object _gate = new object();

                string Start(string? step)
                {
                    lock (_gate)
                    {
                        if (step is null)
                        {
                            throw new System.InvalidOperationException("Unknown step.");
                        }

                        return "started";
                    }
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task RewritesNestedCoalesceInsideIfBlock()
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
        const string fixedSource = """
            #nullable enable
            class C
            {
                const string FallbackValue = "fallback";

                string? Read(string key) => null;

                string Load(string key, bool valid)
                {
                    if (!valid)
                    {
                        var result = Read(key);
                        if (result is not null)
                        {
                            return result;
                        }

                        return FallbackValue;
                    }

                    return FallbackValue;
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task RewritesCoalesceThrowInsideLockToGuard()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                string? Read() => null;

                string Require(bool flag)
                {
                    lock (new object())
                    {
                        return {{InterpolateDiagnostic("Read() ?? throw new System.InvalidOperationException(\"Missing value.\")", NestedReturnDecisionAnalyzer.CoalesceRule)}};
                    }
                }
            }
            """);
        const string fixedSource = """
            #nullable enable
            class C
            {
                string? Read() => null;

                string Require(bool flag)
                {
                    lock (new object())
                    {
                        var result = Read();
                        if (result is null)
                        {
                            throw new System.InvalidOperationException("Missing value.");
                        }

                        return result;
                    }
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task RewritesNullableValueCoalesceThrowToGuard()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                int Count(bool flag)
                {
                    int? stored = null;

                    if (flag)
                    {
                        return {{InterpolateDiagnostic("stored ?? throw new System.InvalidOperationException(\"Missing count.\")", NestedReturnDecisionAnalyzer.CoalesceRule)}};
                    }

                    return 0;
                }
            }
            """);
        const string fixedSource = """
            #nullable enable
            class C
            {
                int Count(bool flag)
                {
                    int? stored = null;

                    if (flag)
                    {
                        var result = stored;
                        if (!result.HasValue)
                        {
                            throw new System.InvalidOperationException("Missing count.");
                        }

                        return result.Value;
                    }

                    return 0;
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task RewritesNullableCallInsideCaseArm()
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
        const string fixedSource = """
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
                            {
                                var result = cache.Get(key);
                                if (result is not null)
                                {
                                    return result;
                                }

                                return null;
                            }
                    }

                    return null;
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task RewritesBooleanCallInsideCatch()
    {
        var source = TestCode.Create($$"""
            class C
            {
                bool ShouldRetry(int attempt) => attempt < 3;

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

                void Work() { }
            }
            """);
        const string fixedSource = """
            class C
            {
                bool ShouldRetry(int attempt) => attempt < 3;

                bool Retry(int attempt)
                {
                    try
                    {
                        Work();
                    }
                    catch (System.IO.IOException)
                    {
                        if (ShouldRetry(attempt))
                        {
                            return true;
                        }

                        return false;
                    }

                    return false;
                }

                void Work() { }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task WrapsEmbeddedBracelessReturnInBlock()
    {
        var source = TestCode.Create($$"""
            class C
            {
                bool CheckReady(System.Collections.Generic.List<int> batch, System.Collections.Generic.List<int> items, bool allValid)
                {
                    if (batch.Count == 0)
                        return {{InterpolateDiagnostic("items.Count > 0 && allValid", NestedReturnDecisionAnalyzer.BooleanRule)}};
                    return false;
                }
            }
            """);
        const string fixedSource = """
            class C
            {
                bool CheckReady(System.Collections.Generic.List<int> batch, System.Collections.Generic.List<int> items, bool allValid)
                {
                    if (batch.Count == 0)
                    {
                        if (items.Count > 0 && allValid)
                        {
                            return true;
                        }

                        return false;
                    }

                    return false;
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task RewritesAllNestedDecisionsInOneBody()
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
        const string fixedSource = """
            #nullable enable
            class C
            {
                string? Read() => null;

                string? Two(bool flag, bool choice)
                {
                    if (flag)
                    {
                        if (choice)
                        {
                            return "found";
                        }

                        return null;
                    }

                    lock (new object())
                    {
                        var result = Read();
                        if (result is not null)
                        {
                            return result;
                        }

                        return "fallback";
                    }
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task KeepsCommentsAroundNestedReturn()
    {
        var source = TestCode.Create($$"""
            class C
            {
                bool Check() => true;

                bool M(bool flag)
                {
                    lock (new object())
                    {
                        // Check the remaining case.
                        return {{InterpolateDiagnostic("Check()", NestedReturnDecisionAnalyzer.BooleanRule)}}; // End of decision.
                    }
                }
            }
            """);
        const string fixedSource = """
            class C
            {
                bool Check() => true;

                bool M(bool flag)
                {
                    lock (new object())
                    {
                        // Check the remaining case.
                        if (Check())
                        {
                            return true;
                        }

                        return false; // End of decision.
                    }
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task IntroducesLocalPerIterationInsideLoop()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                string? Read(string key) => null;

                string ReadFirst(System.Collections.Generic.List<string> keys)
                {
                    foreach (var key in keys)
                    {
                        return {{InterpolateDiagnostic("Read(key) ?? \"fallback\"", NestedReturnDecisionAnalyzer.CoalesceRule)}};
                    }

                    return "fallback";
                }
            }
            """);
        const string fixedSource = """
            #nullable enable
            class C
            {
                string? Read(string key) => null;

                string ReadFirst(System.Collections.Generic.List<string> keys)
                {
                    foreach (var key in keys)
                    {
                        var result = Read(key);
                        if (result is not null)
                        {
                            return result;
                        }

                        return "fallback";
                    }

                    return "fallback";
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task AvoidsNameClashWithLaterOuterDeclaration()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                string? Read() => null;

                string Load(bool flag)
                {
                    if (flag)
                    {
                        return {{InterpolateDiagnostic("Read() ?? \"fallback\"", NestedReturnDecisionAnalyzer.CoalesceRule)}};
                    }

                    var result = "saved";
                    return result;
                }
            }
            """);
        const string fixedSource = """
            #nullable enable
            class C
            {
                string? Read() => null;

                string Load(bool flag)
                {
                    if (flag)
                    {
                        var result1 = Read();
                        if (result1 is not null)
                        {
                            return result1;
                        }

                        return "fallback";
                    }

                    var result = "saved";
                    return result;
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task KeepsCommentAfterReturnKeyword()
    {
        var source = TestCode.Create($$"""
            class C
            {
                bool Check() => true;

                bool M(bool flag)
                {
                    lock (new object())
                    {
                        return /* decision */ {{InterpolateDiagnostic("Check()", NestedReturnDecisionAnalyzer.BooleanRule)}}; // End of decision.
                    }
                }
            }
            """);
        const string fixedSource = """
            class C
            {
                bool Check() => true;

                bool M(bool flag)
                {
                    lock (new object())
                    {
                        /* decision */
                        if (Check())
                        {
                            return true;
                        }

                        return false; // End of decision.
                    }
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task ScopesGeneratedLocalAwayFromSiblingSectionLocal()
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
                        case Mode.Live:
                            var result = cache.Get(key);
                            return result;
                    }

                    return null;
                }
            }
            """);
        const string fixedSource = """
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
                            {
                                var result1 = cache.Get(key);
                                if (result1 is not null)
                                {
                                    return result1;
                                }

                                return null;
                            }

                        case Mode.Live:
                            var result = cache.Get(key);
                            return result;
                    }

                    return null;
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task FixAllWrapsGeneratedLocalsInSiblingSections()
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
                Entry? Read(Cache cache, Mode mode, string key, string otherKey)
                {
                    switch (mode)
                    {
                        case Mode.Cached:
                            return {{InterpolateDiagnostic("cache.Get(key)", NestedReturnDecisionAnalyzer.NullableCallRule)}};
                        case Mode.Live:
                            return {{InterpolateDiagnostic("cache.Get(otherKey)", NestedReturnDecisionAnalyzer.NullableCallRule)}};
                    }

                    return null;
                }
            }
            """);
        const string fixedSource = """
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
                Entry? Read(Cache cache, Mode mode, string key, string otherKey)
                {
                    switch (mode)
                    {
                        case Mode.Cached:
                            {
                                var result = cache.Get(key);
                                if (result is not null)
                                {
                                    return result;
                                }

                                return null;
                            }

                        case Mode.Live:
                            {
                                var result = cache.Get(otherKey);
                                if (result is not null)
                                {
                                    return result;
                                }

                                return null;
                            }
                    }

                    return null;
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    private static CodeFixTestBuilder<
        NestedReturnDecisionAnalyzer,
        NestedReturnDecisionCodeFixProvider,
        DefaultVerifier> Builder()
    {
        return CodeFixTestBuilder.For<
            NestedReturnDecisionAnalyzer,
            NestedReturnDecisionCodeFixProvider,
            DefaultVerifier>();
    }
}
