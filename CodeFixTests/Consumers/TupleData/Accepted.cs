using System;
using System.Collections.Generic;

namespace TupleConsumer;

public readonly record struct CellInfo(string? Text);

public static class CommissionParser
{
    // E3's local iterator result is explicitly allowed by Q16, including its deconstruction.
    public static void Read(IEnumerable<CellInfo> row)
    {
        IEnumerable<(string Text, CellInfo Cell)> NonEmptyCells()
        {
            foreach (var cell in row)
            {
                var text = cell.Text;
                if (text is not null)
                {
                    yield return (text, cell);
                }
            }
        }

        var cells = NonEmptyCells();
        foreach (var (text, cell) in cells)
        {
            Console.WriteLine(text);
            Console.WriteLine(cell);
        }
    }

    // Purely local tuples/swaps and LINQ temporaries remain outside the settled scope.
    public static int LocalValues(int left, int right)
    {
        var pair = (left, right);
        (left, right) = (right, left);
        (int Left, int Right) Local() { return pair; }
        var (first, second) = Local();
        return first + second;
    }
}

public readonly record struct CourseNameMapping(string From, string To);
public readonly record struct QualificationSegment(int? Cycle, QualificationType? Type);
public readonly record struct CalendarPlanStep(string Id, string Action, string? CalendarId, int? DesiredEventIndex);
public readonly record struct FilteredItem<T>(T Item, FilteredSchedule Schedule);

// Representative manually migrated shapes are accepted, including record deconstruction.
public static class NamedData
{
    public static ReadOnlySpan<CourseNameMapping> Mappings => new CourseNameMapping[] { new("HTML", "HTML și CSS") };

    private static QualificationSegment ParseQualificationSegment(string segment) { return new(1, QualificationType.Bachelor); }

    public static IEnumerable<FilteredItem<T>> Filter<T>(T item)
    {
        yield return new(item, new FilteredSchedule());
    }

    public static void Run()
    {
        var plan = new List<CalendarPlanStep>();
        plan.Add(new("event", "delete", null, null));
        foreach (var step in plan)
        {
            ReportPlanned(step);
        }
        var (_, qualificationType) = ParseQualificationSegment("1");
        Console.WriteLine(qualificationType);

        void ReportPlanned(CalendarPlanStep step) { Console.WriteLine(step.Id); }
    }
}
