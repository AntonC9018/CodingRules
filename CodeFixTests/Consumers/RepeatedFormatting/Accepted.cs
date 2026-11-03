using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace RepeatedFormatting;

public static class Accepted
{
    public static void DifferentFormats(DateTime start, DateTime end)
    {
        _ = start.ToString("o", CultureInfo.InvariantCulture);
        _ = end.ToString("d", CultureInfo.InvariantCulture);
    }

    public static void DifferentProviders(DateTime start, DateTime end)
    {
        _ = start.ToString("o", CultureInfo.InvariantCulture);
        _ = end.ToString("o", CultureInfo.CurrentCulture);
    }

    public static void DifferentShapes(DateTime start, DateTime? end, DateTimeOffset offset)
    {
        _ = start.ToString("o", CultureInfo.InvariantCulture);
        _ = end?.ToString("o", CultureInfo.InvariantCulture);
        _ = offset.ToString("o", CultureInfo.InvariantCulture);
    }

    // These are independent product policies, despite today's matching format.
    public static string FormatAuditTimestamp(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);
    public static string FormatExportTimestamp(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);

    [SuppressMessage("Readability", "CR1300", Justification = "Audit and export timestamps have independent versioning policies.")]
    public static void SeparatePolicies(DateTime audit, DateTime export)
    {
        _ = audit.ToString("o", CultureInfo.InvariantCulture);
        _ = export.ToString("o", CultureInfo.InvariantCulture);
    }

    public static void ScopedPolicies(DateTime audit, DateTime export)
    {
        _ = audit.ToString("o", CultureInfo.InvariantCulture);
#pragma warning disable CR1300 // Export policy is versioned independently of audit policy.
        _ = export.ToString("o", CultureInfo.InvariantCulture);
#pragma warning restore CR1300
    }

    public static void SeparateCallables(DateTime start, DateTime end)
    {
        _ = start.ToString("o", CultureInfo.InvariantCulture);
        string Local() => end.ToString("o", CultureInfo.InvariantCulture);
        Func<string> deferred = () => end.ToString("o", CultureInfo.InvariantCulture);
        _ = Local();
        _ = deferred;
    }
}
