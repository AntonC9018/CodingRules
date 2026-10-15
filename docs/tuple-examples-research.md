# Tuple examples for a design interview

These are public ScheduleLib examples at `4c26748e448fe7ccbdffc3f8f6c4b05ffddce144`,
with subsequent decisions recorded in the [follow-up](schedulelib-follow-up.md#tuple-and-parameter-refinements-q15q18).
Older owner-attributed cases predate June 2026 but lack provenance trailers.

## E1: domain mapping data stored as a collection
[Config.cs, lines 24–30](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleDefaults/Config.cs#L24-L30).
Owner commit [`fad0b552`](https://github.com/AntonC9018/ScheduleLib/commit/fad0b5528c01e116b49db5b0d0da21d8139909d3), 2025-12-13 (`stuff`; no trailer).

```csharp
public static ReadOnlySpan<(string From, string To)> CourseNameUnificationConfig => new[]
{
    (From: "Dezv. apl. server-side cu Node.js", To: "Node.js"),
    (From: "HTML", To: "HTML și CSS"),
    (From: "Modele design soft", To: "Design Soft"),
    // Further mappings omitted from this excerpt.
};
```

This feeds course-name unification and represents a stable source/normalized-name mapping.
A possible new alternative is `readonly record struct CourseNameMapping(string From, string To)`;
it gives the pair a domain name. **Q15 decision:** use the named record struct.

## E2: a parser method returns two related results
[PathParser.cs, lines 158–170](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/MoodleInterop/PathParser.cs#L158-L170), consumed by [lines 124–132](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/MoodleInterop/PathParser.cs#L124-L132).
Owner commit [`094123a6`](https://github.com/AntonC9018/ScheduleLib/commit/094123a622f31bbfb047d07417485ac583d40c2a), 2025-11-03 (`Program works`; no trailer).

```csharp
private static (int? cycle, QualificationType? qualificationType)
    ParseQualificationSegment(string segment)
{
    // ...
    if (!scope.TryConsume(MoodlePathTokenType.Ciclul))
        return (null, null);
```

The caller deconstructs both values, discards `cycle`, and validates the qualification.
One possible new API shape is `readonly record struct QualificationSegment(int? Cycle, QualificationType? Type)`;
that names partial parse state while adding a type for a private helper result.
**Q16 decision:** replace this private-method tuple result; private scope is not an exemption.

## E3: a local iterator groups source text with its cell

[CommissionParser.cs, lines 142–150](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/ThesesParsing/ThesesListParser/CommissionParser.cs#L142-L150), consumed at [lines 177–189](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/ThesesParsing/ThesesListParser/CommissionParser.cs#L177-L189).
Owner commit [`e13d6759`](https://github.com/AntonC9018/ScheduleLib/commit/e13d6759f30edb49f277a761b71523a944d66a51), 2026-02-15 (`Fix deps`; no trailer).

```csharp
IEnumerable<(string Text, CellInfo Cell)> NonEmptyCells()
{
    foreach (var cell in row.SizedCells(widths))
    {
        var text = stringTable.GetStringValue(cell.Cell);
        if (text is not null)
            yield return (text, cell);
    }
}
```

Callers use both members to build student columns and process names. A possible
new `record struct NonEmptyCell(string Text, CellInfo Cell)` names this relationship;
the result crosses a local-function boundary. Whether the small scope justifies
retaining the tuple was initially a policy question. **Q16 decision:** allow this
local-function tuple result; this exception does not extend to ordinary private methods.

## E4: the user-identified calendar plan tuple

[CalendarSync.cs, lines 123–142](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/MainCli/CalendarSync.cs#L123-L142).
The plan declaration and `ReportPlanned` signature came from owner commit
[`cd70ba8b`](https://github.com/AntonC9018/ScheduleLib/commit/cd70ba8be905cbea76bb74629f41aee5e82e1dcc), 2026-10-11 (no trailer).
Agent commit [`12e601e7`](https://github.com/AntonC9018/ScheduleLib/commit/12e601e7b62511bdecc44e280407fee12147b230),
2026-10-13 (`Generated-by: GPT 6.1 Sol`), edited later construction/use lines;
the declaration's authorship is not established by the display name.

```csharp
var plan = new List<(string Id, string Action, string? CalendarId,
    int? DesiredEventIndex)>();
// Delete/create-calendar/create-event actions append plan steps.
void ReportPlanned((string Id, string Action, string? CalendarId,
    int? DesiredEventIndex) step) { /* ... */ }
```

This action carries identity and optional references through preview, apply, and
terminal reporting. The user decided this case needs a named record struct; a new
`readonly record struct CalendarPlanStep` with these fields captures that boundary.
It does not imply a blanket local-tuple exception or universal tuple ban.

## E5: a tuple returned by a domain abstraction

[FilteredSchedule.cs, lines 648–673](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Model/FilteredSchedule.cs#L648-L673).
Owner commit [`b2d1a95a`](https://github.com/AntonC9018/ScheduleLib/commit/b2d1a95a573f734c4001990ec197ebd83a396b40), 2025-11-02 (`Try adding an abstraction`; no trailer).

```csharp
public IEnumerable<(T Item, FilteredSchedule Schedule)> Filter(Schedule schedule)
{
    foreach (var it in _items)
    {
        var filter = _producer(it, _default);
        var ret = schedule.Filter(filter);
        yield return (it, ret);
    }
}
```

Each result pairs an input item with its filtered schedule. A possible new
`record struct FilteredItem<T>(T Item, FilteredSchedule Schedule)` provides a
named return shape. A nearby comment questions this abstraction, so the tuple
does not show that the abstraction is settled. **Q16 decision:** use a named
record struct for this ordinary-method result.

## Coverage limits

Found collection data, method returns (including a local iterator), and a multi-stage plan;
no compelling purely local tuple value, swap, or LINQ temporary grouping. Older examples lack positive
human-authorship evidence. Accepted decisions are annotated above; purely local
tuple values and swaps are not settled by this scan.
