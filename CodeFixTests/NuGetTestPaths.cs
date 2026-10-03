using System;
using System.IO;

namespace CodingRules;

internal static class NuGetTestPaths
{
    public static string GetPackagesDirectory()
    {
        var configuredPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(configuredPackages))
        {
            return configuredPackages;
        }

        var repositoryPath = Path.Combine(AppContext.BaseDirectory, "../../../..");
        var repository = Path.GetFullPath(repositoryPath);
        return Path.Combine(repository, "artifacts/nuget-packages");
    }
}
