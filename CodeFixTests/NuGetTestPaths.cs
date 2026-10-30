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

    /// <summary>
    /// Returns the restored reference-assembly folder for a netstandard version.
    /// </summary>
    public static string GetNetstandardReferenceDirectory(string version)
    {
        var relativePath = version switch
        {
            "2.0" => "netstandard.library/2.0.3/build/netstandard2.0/ref",
            "2.1" => "netstandard.library.ref/2.1.0/ref/netstandard2.1",
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null),
        };
        return Path.Combine(GetPackagesDirectory(), relativePath);
    }
}
