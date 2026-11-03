using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using SourceGeneration.Testing;
using Xunit;

namespace CodingRules;

public sealed class ReviewedPropertyReturnTests
{
    [Fact]
    public async Task FinalPropertyAfterGuardUsesExplicitOutcomes()
    {
        var source = TestCode.Create($$"""
            class C
            {
                bool IsSingleGroup { get; set; }
                bool ShouldSkip(bool wildcard)
                {
                    if (wildcard)
                    {
                        return false;
                    }

                    return {{InterpolateDiagnostic("IsSingleGroup", ExplicitReturnDecisionAnalyzer.Rule)}};
                }
            }
            """);
        const string expected = """
            class C
            {
                bool IsSingleGroup { get; set; }
                bool ShouldSkip(bool wildcard)
                {
                    if (wildcard)
                    {
                        return false;
                    }

                    if (IsSingleGroup)
                    {
                        return true;
                    }

                    return false;
                }
            }
            """;
        await CodeFixTestBuilder.For<ExplicitReturnDecisionAnalyzer,
                ExplicitReturnDecisionCodeFixProvider, DefaultVerifier>()
            .WithSource(source).WithFixedCode(expected).RunAsync();
    }
}
