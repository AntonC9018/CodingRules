using System;
using System.Collections.Generic;

namespace TupleConsumer;

// Reduced E1/E2/E4/E5 from docs/tuple-examples-research.md at ScheduleLib 4c26748e.
public static class CalendarSync
{
    public static void Run()
    {
        var plan = new List<(string Id, string Action, string? CalendarId, int? DesiredEventIndex)>(); // CR0900: plan
        plan.Add(("event", "delete", null, null));
        foreach (var step in plan)
        {
            ReportPlanned(step);
        }

        void ReportPlanned((string Id, string Action, string? CalendarId, int? DesiredEventIndex) step) // CR0900: (string Id
        {
            Console.WriteLine(step.Id);
        }
    }

    public static IEnumerable<(string Id, string Action, string? CalendarId, int? DesiredEventIndex)> Plan() // CR0900: IEnumerable<
    {
        yield return ("event", "delete", null, null);
    }
}

public static class Config
{
    public static ReadOnlySpan<(string From, string To)> CourseNameUnificationConfig => new[] // CR0900: ReadOnlySpan<
    {
        (From: "Dezv. apl. server-side cu Node.js", To: "Node.js"),
        (From: "HTML", To: "HTML și CSS"),
        (From: "Modele design soft", To: "Design Soft"),
    };
}

public enum QualificationType { Bachelor }

public static class PathParser
{
    private static (int? cycle, QualificationType? qualificationType) ParseQualificationSegment(string segment) // CR0900: (int?
    {
        if (segment.Length == 0)
        {
            return (null, null);
        }
        return (1, QualificationType.Bachelor);
    }

    public static QualificationType? Parse(string segment)
    {
        var (_, qualificationType) = ParseQualificationSegment(segment);
        return qualificationType;
    }
}

public sealed class Schedule
{
    public FilteredSchedule Filter(int filter) { return new FilteredSchedule(); }
}
public sealed class FilteredSchedule { }

public sealed class FilteredItems<T>(IEnumerable<T> items, Func<T, int, int> producer)
{
    public IEnumerable<(T Item, FilteredSchedule Schedule)> Filter(Schedule schedule) // CR0900: IEnumerable<
    {
        foreach (var it in items)
        {
            var filter = producer(it, 0);
            var ret = schedule.Filter(filter);
            yield return (it, ret);
        }
    }
}
