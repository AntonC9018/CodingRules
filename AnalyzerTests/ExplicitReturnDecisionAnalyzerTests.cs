using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CodingRules;

public sealed class ExplicitReturnDecisionAnalyzerTests
{
    [Fact]
    public async Task ReportsFinalNullableTernaryAfterGuard()
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
                    return [|value.Length == 0 ? null : value|];
                }
            }
            """;

        await RunAsync(source);
    }

    [Fact]
    public async Task ReportsBooleanCallAfterGuards()
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

                    return [|name.Contains("..")|];
                }
            }
            """;

        await RunAsync(source);
    }

    [Fact]
    public async Task TreatsStraightLineWorkBeforeReturnAsGuard()
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

                    return [|Check()|];
                }
            }
            """;

        await RunAsync(source);
    }

    [Fact]
    public async Task ReportsCoalesceAndNullableCall()
    {
        const string source = """
            #nullable enable
            class C
            {
                string? Read() => null;

                string F(bool stop)
                {
                    if (stop)
                    {
                        return "stop";
                    }

                    return [|Read() ?? "fallback"|];
                }

                string? G(bool stop)
                {
                    if (stop)
                    {
                        return null;
                    }

                    return [|Read()|];
                }
            }
            """;

        await RunAsync(source);
    }

    [Fact]
    public async Task AnalyzesLocalFunctionsAccessorsAndBlockLambdasSeparately()
    {
        const string source = """
            using System;
            class C
            {
                bool Value
                {
                    get
                    {
                        if (false)
                        {
                            return false;
                        }

                        return [|Check()|];
                    }
                }

                bool Check() => true;

                void M()
                {
                    bool Local()
                    {
                        if (false)
                        {
                            return false;
                        }

                        return [|Check()|];
                    }

                    Func<bool> callback = () =>
                    {
                        if (false)
                        {
                            return false;
                        }

                        return [|Check()|];
                    };

                    _ = Local();
                    _ = callback();
                }
            }
            """;

        await RunAsync(source);
    }

    [Fact]
    public async Task AllowsLoneTernaryPlainValueAndNestedGuard()
    {
        const string source = """
            class C
            {
                bool Check() => true;

                bool Lone(bool flag)
                {
                    return flag ? true : false;
                }

                bool Plain(bool flag)
                {
                    if (flag)
                    {
                        return false;
                    }

                    var value = Check();
                    return value;
                }

                bool Nested(bool flag)
                {
                    if (flag)
                    {
                        bool Local()
                        {
                            return true;
                        }

                        _ = Local();
                    }

                    return Check();
                }

                bool Deeper(bool outer, bool inner)
                {
                    if (outer)
                    {
                        if (inner)
                        {
                            return true;
                        }
                    }

                    return Check();
                }
            }
            """;

        await RunAsync(source);
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        const string source = """
            // <auto-generated/>
            class C
            {
                bool M(bool flag)
                {
                    if (flag)
                    {
                        return false;
                    }

                    return flag == false;
                }
            }
            """;

        await RunAsync(source);
    }

    [Fact]
    public async Task SkipsUnsupportedDecisionForms()
    {
        const string source = """
            #nullable enable
            struct Choice
            {
                public static bool operator true(Choice value) => true;
                public static bool operator false(Choice value) => false;
            }

            class C
            {
                string? CustomCondition(Choice choice, bool stop)
                {
                    if (stop)
                    {
                        return null;
                    }

                    return choice ? null : "found";
                }

                bool Switch(bool flag)
                {
                    if (flag)
                    {
                        return true;
                    }

                    return flag switch { true => true, false => false };
                }

                string? CommentInside(bool flag)
                {
                    if (flag)
                    {
                        return null;
                    }

                    return flag ? /* keep */ null : "found";
                }
            }
            """;

        await RunAsync(source);
    }

    [Fact]
    public async Task SkipsReturnsWhoseIntermediateConversionWouldChange()
    {
        const string source = """
            #nullable enable
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

                Wrapper Conditional(bool stop, bool choice)
                {
                    if (stop)
                    {
                        return new Wrapper();
                    }

                    return choice ? new Derived() : new Base();
                }

                Wrapper Coalesce(bool stop)
                {
                    if (stop)
                    {
                        return new Wrapper();
                    }

                    return Read() ?? new Derived();
                }

                Wrapper Call(bool stop)
                {
                    if (stop)
                    {
                        return new Wrapper();
                    }

                    return Read();
                }
            }
            """;

        await RunAsync(source);
    }

    private static async Task RunAsync(string source)
    {
        var test = new CSharpAnalyzerTest<ExplicitReturnDecisionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };

        await test.RunAsync();
    }
}
