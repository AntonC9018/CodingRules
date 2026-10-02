using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CodingRules;

public sealed class InlineConditionFormattingTests
{
    [Fact]
    public async Task PreferredFunctionHasPinnedLinearGuardOutput()
    {
        var source = """
            class C
            {
                bool M(bool a, bool b, bool c) => {|CR0201:a && b && c|};
            }
            """;
        var expected = """
            class C
            {
                bool M(bool a, bool b, bool c)
                {
                    bool CheckCondition()
                    {
                        if (!(a))
                        {
                            return false;
                        }

                        if (!(b))
                        {
                            return false;
                        }

                        if (!(c))
                        {
                            return false;
                        }

                        return true;
                    }

                    return CheckCondition();
                }
            }
            """;
        var test = new CSharpCodeFixTest<InlineConditionAnalyzer, InlineConditionCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = expected,
            CodeActionEquivalenceKey = InlineConditionCodeFixProvider.ExtractKey,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task DirectExpansionHasPinnedLiteralReturns()
    {
        var source = """
            class C
            {
                bool M(bool a, bool b, bool c) => {|CR0201:a && b && c|};
            }
            """;
        var expected = """
            class C
            {
                bool M(bool a, bool b, bool c)
                {
                    if (!(a))
                    {
                        return false;
                    }

                    if (!(b))
                    {
                        return false;
                    }

                    if (!(c))
                    {
                        return false;
                    }

                    return true;
                }
            }
            """;
        var test = new CSharpCodeFixTest<InlineConditionAnalyzer, InlineConditionCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = expected,
            CodeActionEquivalenceKey = InlineConditionCodeFixProvider.ExpandKey,
        };
        await test.RunAsync();
    }

    [Theory]
    [InlineData(InlineConditionCodeFixProvider.ExtractKey)]
    [InlineData(InlineConditionCodeFixProvider.ExpandKey)]
    public async Task GeneratedCodeUsesConfiguredTabsBracesAndLf(string key)
    {
        var source = "class C\r\n{\r\n    bool M(bool a, bool b, bool c) => a && b && c;\r\n}\r\n";
        var config = "root = true\n[*]\nindent_style = tab\nindent_size = 4\nend_of_line = lf\ncsharp_new_line_before_open_brace = none\n";
        var document = InlineConditionTestFixture.Document(source, config);
        var changed = await InlineConditionTestFixture.Fix(document, key);
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("\t\tif (!(a)) {\n", text);
        await InlineConditionTestFixture.Compiles(changed);
        var method = (await changed.GetSyntaxRootAsync())!.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First();
        Assert.DoesNotContain("\r\n", method.ToFullString());
    }
}
