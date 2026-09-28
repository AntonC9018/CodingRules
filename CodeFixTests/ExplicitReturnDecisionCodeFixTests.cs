using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using SourceGeneration.Testing;
using Xunit;

namespace CodingRules;

public sealed class ExplicitReturnDecisionCodeFixTests
{
    [Fact]
    public async Task ReturnsFoundValueBeforeNullableFallback()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                string? Field(int index, string[] fields)
                {
                    if (index < 0)
                    {
                        return null;
                    }

                    var value = fields[index].Trim();
                    return {{InterpolateDiagnostic("value.Length == 0 ? null : value", ExplicitReturnDecisionAnalyzer.Rule)}};
                }
            }
            """);
        const string fixedSource = """
            #nullable enable
            class C
            {
                string? Field(int index, string[] fields)
                {
                    if (index < 0)
                    {
                        return null;
                    }

                    var value = fields[index].Trim();
                    if (value.Length != 0)
                    {
                        return value;
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
    public async Task SplitsBooleanCallIntoTrueAndFalseReturns()
    {
        var source = TestCode.Create($$"""
            class C
            {
                bool IsUnsafe(string name)
                {
                    if (name.Contains('/'))
                    {
                        return true;
                    }

                    return {{InterpolateDiagnostic("name.Contains(\"..\")", ExplicitReturnDecisionAnalyzer.Rule)}};
                }
            }
            """);
        const string fixedSource = """
            class C
            {
                bool IsUnsafe(string name)
                {
                    if (name.Contains('/'))
                    {
                        return true;
                    }

                    if (name.Contains(".."))
                    {
                        return true;
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
    public async Task FixesReturnAfterGuardWithSideEffect()
    {
        var source = TestCode.Create($$"""
            class C
            {
                bool Check() => true;
                void Log() { }

                bool M(bool stop)
                {
                    if (stop)
                    {
                        Log();
                        return false;
                    }

                    return {{InterpolateDiagnostic("Check()", ExplicitReturnDecisionAnalyzer.Rule)}};
                }
            }
            """);
        const string fixedSource = """
            class C
            {
                bool Check() => true;
                void Log() { }

                bool M(bool stop)
                {
                    if (stop)
                    {
                        Log();
                        return false;
                    }

                    if (Check())
                    {
                        return true;
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
    public async Task EvaluatesCoalesceOperandOnlyOnce()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                string? Read() => null;

                string M(bool stop)
                {
                    if (stop)
                    {
                        return "stop";
                    }

                    return {{InterpolateDiagnostic("Read() ?? \"fallback\"", ExplicitReturnDecisionAnalyzer.Rule)}};
                }
            }
            """);
        const string fixedSource = """
            #nullable enable
            class C
            {
                string? Read() => null;

                string M(bool stop)
                {
                    if (stop)
                    {
                        return "stop";
                    }

                    var result = Read();
                    if (result is not null)
                    {
                        return result;
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
    public async Task ChoosesUnusedNameForNullableCallResult()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                string? Read() => null;

                string? M(bool stop)
                {
                    var result = "previous";
                    if (stop)
                    {
                        return result;
                    }

                    return {{InterpolateDiagnostic("Read()", ExplicitReturnDecisionAnalyzer.Rule)}};
                }
            }
            """);
        const string fixedSource = """
            #nullable enable
            class C
            {
                string? Read() => null;

                string? M(bool stop)
                {
                    var result = "previous";
                    if (stop)
                    {
                        return result;
                    }

                    var result1 = Read();
                    if (result1 is not null)
                    {
                        return result1;
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
    public async Task HandlesNullableValueCoalesce()
    {
        var source = TestCode.Create($$"""
            class C
            {
                int? Read() => null;

                int M(bool stop)
                {
                    if (stop)
                    {
                        return 0;
                    }

                    return {{InterpolateDiagnostic("Read() ?? 42", ExplicitReturnDecisionAnalyzer.Rule)}};
                }
            }
            """);
        const string fixedSource = """
            class C
            {
                int? Read() => null;

                int M(bool stop)
                {
                    if (stop)
                    {
                        return 0;
                    }

                    var result = Read();
                    if (result.HasValue)
                    {
                        return result.Value;
                    }

                    return 42;
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    [Fact]
    public async Task PreservesFloatingPointComparisonSemantics()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class C
            {
                string? M(double number, bool stop)
                {
                    if (stop)
                    {
                        return null;
                    }

                    return {{InterpolateDiagnostic("number > 0 ? null : \"found\"", ExplicitReturnDecisionAnalyzer.Rule)}};
                }
            }
            """);
        const string fixedSource = """
            #nullable enable
            class C
            {
                string? M(double number, bool stop)
                {
                    if (stop)
                    {
                        return null;
                    }

                    if (!(number > 0))
                    {
                        return "found";
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
    public async Task DoesNotReplaceUserDefinedEqualityWithInequality()
    {
        var source = TestCode.Create($$"""
            #nullable enable
            class Odd
            {
                public static bool operator ==(Odd? left, Odd? right) => true;
                public static bool operator !=(Odd? left, Odd? right) => true;
                public override bool Equals(object? value) => base.Equals(value);
                public override int GetHashCode() => 0;
            }

            class C
            {
                string? M(Odd left, Odd right, bool stop)
                {
                    if (stop)
                    {
                        return null;
                    }

                    return {{InterpolateDiagnostic("left == right ? null : \"found\"", ExplicitReturnDecisionAnalyzer.Rule)}};
                }
            }
            """);
        const string fixedSource = """
            #nullable enable
            class Odd
            {
                public static bool operator ==(Odd? left, Odd? right) => true;
                public static bool operator !=(Odd? left, Odd? right) => true;
                public override bool Equals(object? value) => base.Equals(value);
                public override int GetHashCode() => 0;
            }

            class C
            {
                string? M(Odd left, Odd right, bool stop)
                {
                    if (stop)
                    {
                        return null;
                    }

                    if (!(left == right))
                    {
                        return "found";
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
    public async Task KeepsCommentsAroundReturn()
    {
        var source = TestCode.Create($$"""
            class C
            {
                bool Check() => true;

                bool M(bool stop)
                {
                    if (stop)
                    {
                        return false;
                    }

                    // Check the remaining case.
                    return {{InterpolateDiagnostic("Check()", ExplicitReturnDecisionAnalyzer.Rule)}}; // End of decision.
                }
            }
            """);
        const string fixedSource = """
            class C
            {
                bool Check() => true;

                bool M(bool stop)
                {
                    if (stop)
                    {
                        return false;
                    }

                    // Check the remaining case.
                    if (Check())
                    {
                        return true;
                    }

                    return false; // End of decision.
                }
            }
            """;

        await Builder()
            .WithSource(source)
            .WithFixedCode(fixedSource)
            .RunAsync();
    }

    private static CodeFixTestBuilder<
        ExplicitReturnDecisionAnalyzer,
        ExplicitReturnDecisionCodeFixProvider,
        DefaultVerifier> Builder()
    {
        return CodeFixTestBuilder.For<
            ExplicitReturnDecisionAnalyzer,
            ExplicitReturnDecisionCodeFixProvider,
            DefaultVerifier>();
    }
}
