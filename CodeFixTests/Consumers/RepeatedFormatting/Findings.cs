using System;
using System.Globalization;
using static System.Globalization.CultureInfo;

namespace RepeatedFormatting;

public sealed class CalendarEvent
{
    public EventDateTime? Start { get; set; }
    public EventDateTime? End { get; set; }
}

public sealed class EventDateTime
{
    public DateTimeOffset? DateTimeDateTimeOffset { get; set; }
}

public static class Findings
{
    // ScheduleLib CalendarSync.cs at 12e601e, A14: preserve the actual chains,
    // format/provider and anonymous-object context from the desired-event report.
    public static object Calendar(CalendarEvent ev)
    {
        return new
        {
            start = ev.Start?.DateTimeDateTimeOffset?.ToString(
                "o",
                System.Globalization.CultureInfo.InvariantCulture),
            end = ev.End?.DateTimeDateTimeOffset?.ToString( // CR1300: .ToString
                "o",
                System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    public static void Direct(DateTime start, DateTime end)
    {
        _ = start.ToString("o", InvariantCulture);
        _ = end.ToString("o", CultureInfo.InvariantCulture); // CR1300: end.ToString
    }

    public static void Nullable(DateTimeOffset? start, DateTimeOffset? end, DateTimeOffset? other)
    {
        _ = start?.ToString("o", InvariantCulture);
        _ = end?.ToString("o", InvariantCulture); // CR1300: .ToString
        _ = other?.ToString("o", InvariantCulture); // CR1300: .ToString
    }

    public static void SharedProvider(DateTimeOffset start, DateTimeOffset end, IFormatProvider provider)
    {
        _ = start.ToString("o", provider);
        _ = end.ToString("o", provider); // CR1300: end.ToString
    }

    public static void DefaultProvider(DateTime start, DateTime end)
    {
        _ = start.ToString(null, null);
        _ = end.ToString(null, null); // CR1300: end.ToString
    }

    public static void Branches(bool first, DateTime start, DateTime end)
    {
        if (first) _ = start.ToString("o", InvariantCulture);
        else _ = end.ToString("o", InvariantCulture); // CR1300: end.ToString
    }
}
