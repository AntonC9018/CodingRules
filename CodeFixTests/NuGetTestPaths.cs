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

        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        return Path.Combine(repository, "artifacts/nuget-packages");
    }
}
