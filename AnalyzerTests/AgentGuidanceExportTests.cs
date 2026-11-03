using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace CodingRules;

public sealed class AgentGuidanceExportTests
{
    [Fact]
    public async Task OrdinaryBuildDoesNotExportEvenWhenDestinationIsConfigured()
    {
        using var fixture = new ExportFixture();
        var result = await fixture.Run("Build", ".codingrules");

        Assert.Equal(0, result.ExitCode);
        var exportDirectory = Path.Combine(fixture.ProjectDirectory, ".codingrules");
        var exportExists = Directory.Exists(exportDirectory);
        Assert.False(exportExists);
        fixture.AssertInstructionsUnchanged();
    }

    [Fact]
    public async Task ExportRequiresAnExplicitDestination()
    {
        using var fixture = new ExportFixture();
        var result = await fixture.Run("ExportCodingRulesAgentGuidance");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Set CodingRulesAgentGuidanceDirectory", result.Output);
        var exportedFile = Path.Combine(fixture.ProjectDirectory, "agent-coding-rules.md");
        var exportExists = File.Exists(exportedFile);
        Assert.False(exportExists);
        fixture.AssertInstructionsUnchanged();
    }

    [Theory]
    [InlineData(".")]
    [InlineData("guidance with spaces")]
    public async Task ExportPreservesExistingInstructionsAndRefreshesOnlyItsOwnedFile(string destination)
    {
        using var fixture = new ExportFixture();
        var directory = Path.Combine(fixture.ProjectDirectory, destination);
        Directory.CreateDirectory(directory);
        var otherInstructions = Path.Combine(directory, "OTHER.md");
        await File.WriteAllTextAsync(otherInstructions, "Preserve these other instructions.");
        var result = await fixture.Run("ExportCodingRulesAgentGuidance", directory);

        Assert.True(result.ExitCode == 0, result.Output);
        var exportedFile = Path.Combine(directory, "agent-coding-rules.md");
        fixture.AssertExportMatchesGuidance(exportedFile);
        fixture.AssertInstructionsUnchanged();

        // Simulate selecting a newer package version, then re-exporting into the same directory.
        await File.AppendAllTextAsync(fixture.SourceGuidance, "\nUpdated guidance from the selected package.\n");
        result = await fixture.Run("ExportCodingRulesAgentGuidance", directory);

        Assert.True(result.ExitCode == 0, result.Output);
        fixture.AssertExportMatchesGuidance(exportedFile);
        var otherContent = File.ReadAllText(otherInstructions);
        Assert.Equal("Preserve these other instructions.", otherContent);
        fixture.AssertInstructionsUnchanged();
    }

    [Fact]
    public async Task SampleReadDirectiveResolvesToTheExportedGuidance()
    {
        using var fixture = new ExportFixture();
        var instructionsFile = Path.Combine(fixture.ProjectDirectory, "AGENTS.md");
        var instructions = File.ReadAllText(instructionsFile);
        var relativePath = instructions.Split('`')[1];
        var destination = Path.GetDirectoryName(relativePath)!;
        var result = await fixture.Run("ExportCodingRulesAgentGuidance", destination);

        Assert.True(result.ExitCode == 0, result.Output);
        var importedFile = Path.Combine(fixture.ProjectDirectory, relativePath);
        fixture.AssertExportMatchesGuidance(importedFile);
        fixture.AssertInstructionsUnchanged();
    }

    private sealed class ExportFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "codingrules-guidance-" + Guid.NewGuid());
        private readonly byte[] _instructions;
        private readonly string _project;

        public string ProjectDirectory { get; }
        public string SourceGuidance { get; }

        public ExportFixture()
        {
            var data = Path.Combine(AppContext.BaseDirectory, "AgentGuidance");
            var packageProject = Path.Combine(data, "Package", "CodingRules.Package.csproj");
            var packageDirectory = Path.Combine(_root, "installed package");
            StageDeclaredAssets(packageProject, packageDirectory);
            SourceGuidance = Path.Combine(packageDirectory, "agent-guidance", "agent-coding-rules.md");
            ProjectDirectory = Path.Combine(_root, "sample repository");
            Directory.CreateDirectory(ProjectDirectory);
            var instructionsFile = Path.Combine(ProjectDirectory, "AGENTS.md");
            var sampleInstructions = Path.Combine(data, "Sample", "AGENTS.md");
            File.Copy(sampleInstructions, instructionsFile);
            _instructions = File.ReadAllBytes(instructionsFile);

            // NuGet imports buildTransitive targets after restore. Reproduce that import
            // without restoring/building a consumer or creating a package per test.
            var targets = Path.Combine(packageDirectory, "buildTransitive", "Anton.CodingRules.targets");
            var importPath = new XAttribute("Project", targets);
            var import = new XElement("Import", importPath);
            var buildName = new XAttribute("Name", "Build");
            var build = new XElement("Target", buildName);
            var project = new XElement("Project", import, build);
            _project = Path.Combine(ProjectDirectory, "Export.proj");
            project.Save(_project);
        }

        public async Task<BuildResult> Run(string target, string? destination = null)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = ProjectDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("msbuild");
            start.ArgumentList.Add(_project);
            start.ArgumentList.Add("-nologo");
            start.ArgumentList.Add("-t:" + target);
            if (destination is not null)
            {
                start.ArgumentList.Add("-p:CodingRulesAgentGuidanceDirectory=" + destination);
            }

            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            var timeoutDuration = TimeSpan.FromSeconds(30);
            using var timeout = new CancellationTokenSource(timeoutDuration);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            var standardOutput = await output;
            var standardError = await error;
            var combinedOutput = standardOutput + standardError;
            return new BuildResult(process.ExitCode, combinedOutput);
        }

        public void AssertExportMatchesGuidance(string exportedFile)
        {
            var source = File.ReadAllBytes(SourceGuidance);
            var exported = File.ReadAllBytes(exportedFile);
            Assert.Equal(source, exported);
        }

        public void AssertInstructionsUnchanged()
        {
            var instructionsFile = Path.Combine(ProjectDirectory, "AGENTS.md");
            var actual = File.ReadAllBytes(instructionsFile);
            Assert.Equal(_instructions, actual);
        }

        private static void StageDeclaredAssets(string projectPath, string destination)
        {
            var project = XDocument.Load(projectPath);
            var directory = Path.GetDirectoryName(projectPath)!;
            foreach (var item in project.Descendants("None"))
            {
                var packagePath = (string?)item.Attribute("PackagePath");
                if (packagePath is not ("agent-guidance" or "buildTransitive"))
                {
                    continue;
                }

                var pack = (string?)item.Attribute("Pack");
                Assert.Equal("true", pack);
                var include = (string)item.Attribute("Include")!;
                var source = Path.Combine(directory, include);
                var sourceDirectory = Path.GetDirectoryName(source)!;
                var pattern = Path.GetFileName(source);
                var files = Directory.GetFiles(sourceDirectory, pattern);
                Assert.NotEmpty(files);
                var outputDirectory = Path.Combine(destination, packagePath);
                Directory.CreateDirectory(outputDirectory);
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var outputFile = Path.Combine(outputDirectory, fileName);
                    File.Copy(file, outputFile);
                }
            }
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private readonly record struct BuildResult(int ExitCode, string Output);
}
