using System.Threading.Tasks;
using SourceGeneration.Testing.LocalNuGet;
using Xunit;

namespace CodingRules;

public sealed class PackageConsumptionTests
{
    [Fact]
    public async Task AppliesPackagedCodeFix()
    {
        var tester = await LocalNuGetPackageTester.CreateAsync();
        var expectedEntries = new[]
        {
            "analyzers/dotnet/cs/CodingRules.Analyzers.dll",
            "analyzers/dotnet/cs/CodingRules.CodeFixes.dll",
            "analyzers/dotnet/cs/CodingRules.Shared.dll",
            "analyzers/dotnet/cs/Anton.SourceGeneration.dll",
            "analyzers/dotnet/cs/Anton.Utils.Shared.dll",
        };
        var package = new PackageProject(
            "Package/CodingRules.Package.csproj",
            expectedEntries);
        var codeFix = new CodeFixExpectation(
            DiagnosticId: "CR0001",
            SourcePath: "Consumer.cs",
            ExpectedSourcePath: "CodeFixTests/Expected/CR0001.cs.txt");
        var consumer = ConsumerFixture.ProjectDirectory(
            path: "CodeFixTests/Consumers/CR0001",
            mainProjectPath: "Consumer.csproj",
            codeFix: codeFix);
        var packages = new[] { package };
        var consumers = new[] { consumer };
        var test = new PackageConsumptionTest(
            nameof(AppliesPackagedCodeFix),
            packages,
            consumers);

        await tester.AssertAsync(test);
    }
}
