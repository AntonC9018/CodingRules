using System.Threading.Tasks;
using SourceGeneration.Testing.LocalNuGet;
using Xunit;

namespace CodingRules;

public sealed class PackageConsumptionTests
{
    [Fact]
    public async Task AppliesPackagedNamedArgumentFix()
    {
        var tester = await LocalNuGetPackageTester.CreateAsync();
        var test = new PackageConsumptionTestBuilder(nameof(AppliesPackagedNamedArgumentFix))
            .AddPackage("Package/CodingRules.Package.csproj")
            .AddProjectConsumer("CodeFixTests/Consumers/NamedArguments", consumer => consumer.ExpectCodeFix(
                DiagnosticIds.NamedArgumentRoles, "Consumer.cs", "CodeFixTests/Expected/NamedArguments.Fixed.cs.txt"))
            .Build();
        await tester.AssertAsync(test);
    }

    [Fact]
    public async Task AppliesPackagedStatementOperationFixes()
    {
        var tester = await LocalNuGetPackageTester.CreateAsync();
        var test = new PackageConsumptionTestBuilder(nameof(AppliesPackagedStatementOperationFixes))
            .AddPackage("Package/CodingRules.Package.csproj")
            .AddProjectConsumer("CodeFixTests/Consumers/StatementOperation0300", consumer => consumer.ExpectCodeFix(
                DiagnosticIds.NestedArgumentOperation, "Consumer.cs", "CodeFixTests/Expected/StatementOperation0300.Fixed.cs.txt"))
            .AddProjectConsumer("CodeFixTests/Consumers/StatementOperation0301", consumer => consumer.ExpectCodeFix(
                DiagnosticIds.UnnamedConditionalValue, "Consumer.cs", "CodeFixTests/Expected/StatementOperation0301.Fixed.cs.txt"))
            .AddProjectConsumer("CodeFixTests/Consumers/StatementOperation0302", consumer => consumer.ExpectCodeFix(
                DiagnosticIds.CombinedStatementOperations, "Consumer.cs", "CodeFixTests/Expected/StatementOperation0302.Fixed.cs.txt"))
            .Build();
        await tester.AssertAsync(test);
    }

    [Fact]
    public async Task AppliesPackagedInlineConditionFix()
    {
        var tester = await LocalNuGetPackageTester.CreateAsync();
        var test = new PackageConsumptionTestBuilder(nameof(AppliesPackagedInlineConditionFix))
            .AddPackage("Package/CodingRules.Package.csproj")
            .AddProjectConsumer("CodeFixTests/Consumers/InlineCondition",
                consumer => consumer.ExpectCodeFix(
                    diagnosticId: DiagnosticIds.ExcessConditionChecks,
                    sourcePath: "Consumer.cs",
                    expectedSourcePath: "CodeFixTests/Expected/InlineCondition.Fixed.cs.txt"))
            .Build();
        await tester.AssertAsync(test);
    }

    [Fact]
    public async Task AppliesPackagedExplicitReturnDecisionFix()
    {
        var tester = await LocalNuGetPackageTester.CreateAsync();
        var test = new PackageConsumptionTestBuilder(
            nameof(AppliesPackagedExplicitReturnDecisionFix))
            .AddPackage("Package/CodingRules.Package.csproj")
            .AddProjectConsumer(
                "CodeFixTests/Consumers/ExplicitReturnDecision",
                consumer => consumer.ExpectCodeFix(
                    diagnosticId: DiagnosticIds.ExplicitReturnDecision,
                    sourcePath: "Consumer.cs",
                    expectedSourcePath: "CodeFixTests/Expected/ExplicitReturnDecision.Fixed.cs.txt"))
            .Build();

        await tester.AssertAsync(test);
    }
}
