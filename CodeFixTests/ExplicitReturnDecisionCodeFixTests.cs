using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CodingRules;

public sealed class ExplicitReturnDecisionCodeFixTests
{
    [Fact]
    public async Task ReturnsFoundValueBeforeNullableFallback()
    {
        const string source = """
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
                    return {|#0:value.Length == 0 ? null : value|};
                }
            }
            """;
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

        var expected = Expect(0);
        await RunAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task SplitsBooleanCallIntoTrueAndFalseReturns()
    {
        const string source = """
            class C
            {
                bool IsUnsafe(string name)
                {
                    if (name.Contains('/'))
                    {
                        return true;
                    }

                    return {|#0:name.Contains("..")|};
                }
            }
            """;
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

        var expected = Expect(0);
        await RunAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task FixesReturnAfterGuardWithSideEffect()
    {
        const string source = """
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

                    return {|#0:Check()|};
                }
            }
            """;
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

        var expected = Expect(0);
        await RunAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task EvaluatesCoalesceOperandOnlyOnce()
    {
        const string source = """
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

                    return {|#0:Read() ?? "fallback"|};
                }
            }
            """;
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

        var expected = Expect(0);
        await RunAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task ChoosesUnusedNameForNullableCallResult()
    {
        const string source = """
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

                    return {|#0:Read()|};
                }
            }
            """;
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

        var expected = Expect(0);
        await RunAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task HandlesNullableValueCoalesce()
    {
        const string source = """
            class C
            {
                int? Read() => null;

                int M(bool stop)
                {
                    if (stop)
                    {
                        return 0;
                    }

                    return {|#0:Read() ?? 42|};
                }
            }
            """;
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

        var expected = Expect(0);
        await RunAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task PreservesFloatingPointComparisonSemantics()
    {
        const string source = """
            #nullable enable
            class C
            {
                string? M(double number, bool stop)
                {
                    if (stop)
                    {
                        return null;
                    }

                    return {|#0:number > 0 ? null : "found"|};
                }
            }
            """;
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

        var expected = Expect(0);
        await RunAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task DoesNotReplaceUserDefinedEqualityWithInequality()
    {
        const string source = """
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

                    return {|#0:left == right ? null : "found"|};
                }
            }
            """;
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

        var expected = Expect(0);
        await RunAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task KeepsCommentsAroundReturn()
    {
        const string source = """
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
                    return {|#0:Check()|}; // End of decision.
                }
            }
            """;
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

        var expected = Expect(0);
        await RunAsync(source, fixedSource, expected);
    }

    private static async Task RunAsync(
        string source,
        string fixedSource,
        DiagnosticResult expected)
    {
        var test = new CSharpCodeFixTest<
            ExplicitReturnDecisionAnalyzer,
            ExplicitReturnDecisionCodeFixProvider,
            DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
        };

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }

    private static DiagnosticResult Expect(int location)
    {
        var expected = new DiagnosticResult(
            DiagnosticIds.ExplicitReturnDecision,
            DiagnosticSeverity.Warning);
        return expected.WithLocation(location);
    }
}
