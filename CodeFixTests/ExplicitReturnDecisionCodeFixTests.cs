using System.Threading.Tasks;
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
                    return {|CR0001:value.Length == 0 ? null : value|};
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

        await RunAsync(source, fixedSource);
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

                    return {|CR0001:name.Contains("..")|};
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

        await RunAsync(source, fixedSource);
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

                    return {|CR0001:Read() ?? "fallback"|};
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

        await RunAsync(source, fixedSource);
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

                    return {|CR0001:Read()|};
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

        await RunAsync(source, fixedSource);
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

                    return {|CR0001:Read() ?? 42|};
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

        await RunAsync(source, fixedSource);
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

                    return {|CR0001:number > 0 ? null : "found"|};
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

        await RunAsync(source, fixedSource);
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
                    return {|CR0001:Check()|}; // End of decision.
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

        await RunAsync(source, fixedSource);
    }

    private static async Task RunAsync(string source, string fixedSource)
    {
        var test = new CSharpCodeFixTest<
            ExplicitReturnDecisionAnalyzer,
            ExplicitReturnDecisionCodeFixProvider,
            DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
        };

        await test.RunAsync();
    }
}
