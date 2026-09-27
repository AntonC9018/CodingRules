using System.Threading.Tasks;
using SourceGeneration.Testing.LocalNuGet;
using Xunit;

namespace CodingRules;

public sealed class PackageConsumptionTests
{
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
