using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using SourceGeneration.Testing;
using Xunit;

namespace CodingRules;

public sealed class ExplicitReturnDecisionAnalyzerTests
{
    [Fact]
    public async Task ReportsFinalNullableTernaryAfterGuard()
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

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task ReportsBooleanCallAfterGuards()
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

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task TreatsStraightLineWorkBeforeReturnAsGuard()
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

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task ReportsCoalesceAndNullableCall()
    {
        var source = TestCode.Create($$"""
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

                    return {{InterpolateDiagnostic("Read() ?? \"fallback\"", ExplicitReturnDecisionAnalyzer.Rule)}};
                }

                string? G(bool stop)
                {
                    if (stop)
                    {
                        return null;
                    }

                    return {{InterpolateDiagnostic("Read()", ExplicitReturnDecisionAnalyzer.Rule)}};
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task AnalyzesLocalFunctionsAccessorsAndBlockLambdasSeparately()
    {
        var source = TestCode.Create($$"""
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

                        return {{InterpolateDiagnostic("Check()", ExplicitReturnDecisionAnalyzer.Rule)}};
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

                        return {{InterpolateDiagnostic("Check()", ExplicitReturnDecisionAnalyzer.Rule)}};
                    }

                    Func<bool> callback = () =>
                    {
                        if (false)
                        {
                            return false;
                        }

                        return {{InterpolateDiagnostic("Check()", ExplicitReturnDecisionAnalyzer.Rule)}};
                    };

                    _ = Local();
                    _ = callback();
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task AllowsLoneTernaryPlainValueAndNestedGuard()
    {
        var source = TestCode.Create("""
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
                    if (flag)
                    {
                        return false;
                    }

                    return flag == false;
                }
            }
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task SkipsUnsupportedDecisionForms()
    {
        var source = TestCode.Create("""
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
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task SkipsReturnsWhoseIntermediateConversionWouldChange()
    {
        var source = TestCode.Create("""
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
            """);

        await Builder()
            .WithSource(source)
            .RunAsync();
    }

    private static AnalyzerTestBuilder<ExplicitReturnDecisionAnalyzer, DefaultVerifier> Builder()
    {
        return AnalyzerTestBuilder.For<ExplicitReturnDecisionAnalyzer, DefaultVerifier>();
    }
}
