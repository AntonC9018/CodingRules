using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

internal static class RuleSeverity
{
    /// <summary>
    /// Whether <paramref name="id"/> can report in <paramref name="tree"/>. Analyzers
    /// use it to skip planning for a disabled rule and to let a lower-priority rule
    /// report when a higher-priority one is disabled.
    /// </summary>
    public static bool IsEnabled(
        string id,
        SyntaxTree tree,
        Compilation compilation,
        AnalyzerConfigOptionsProvider options,
        CancellationToken token)
    {
        var treeSeverity = ReportDiagnostic.Default;
        var treeConfigured = compilation.Options.SyntaxTreeOptionsProvider?.TryGetDiagnosticValue(
            tree, id, token, out treeSeverity) == true;
        var severity = treeConfigured ? treeSeverity
            : compilation.Options.SpecificDiagnosticOptions.TryGetValue(id, out var configured) ? configured
            : ReportDiagnostic.Default;
        if (severity == ReportDiagnostic.Suppress)
        {
            return false;
        }

        var disabledInConfig = options.GetOptions(tree).TryGetValue("dotnet_diagnostic." + id + ".severity", out var value)
            && value == "none";
        return !disabledInConfig;
    }
}
