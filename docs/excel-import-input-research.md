# ScheduleLib Excel lesson input research

This checks the public ScheduleLib source at pinned commit [`4c26748e448fe7ccbdffc3f8f6c4b05ffddce144`](https://github.com/AntonC9018/ScheduleLib/tree/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144). Inspection used committed source via `git show`/`git grep` and did not modify the ScheduleLib checkout.

## Finding

The schedule Excel importer already uses a named aggregate for the lesson handler input: `AddLessonParams`, a `readonly ref struct` containing `ScheduleImportContext`, a by-reference `ParsedLesson`, a `ReadOnlySpan<GroupId>`, `TimeSlot`, and `Day`. `ExcelScheduleParser` builds this aggregate at the handler call site, and both Excel handler implementations consume it. This is direct precedent for packaging one import operation’s arguments into a named type.

The maintainer clarified the sharing boundary: PDF/Word should have their own record struct, shared across their delegate, handler, and AddOrMergeLesson method. Excel AddLessonParams is a precedent for that pattern and remains a separate type. The earlier interpretation requiring one model across Excel and PDF/Word was mistaken. The existing representations differ as follows:

| Field | Excel `AddLessonParams` | Shared `AddOrMergeLesson` call |
|---|---|---|
| Parsed lesson | `ref readonly ParsedLesson` | `in ParsedLesson` |
| Groups | `ReadOnlySpan<GroupId>` | `IReadOnlyList<GroupId>` |
| Day | `Day` record (`DayOfWeek` plus a `DateOnly` field) | `DayOfWeek` only |
| Time | `TimeSlot` | `TimeSlot` |
| Alternative | absent | optional `Alternative` |
| Import context | `ScheduleImportContext` | receiver (`this`) |

The Excel handler uses the date in `FrScheduleAddLessonHandler` to create a one-time lesson; regular Excel import uses the `DayOfWeek` from `Day`. The shared add-or-merge method creates a regular lesson and applies `Alternative`. These differences belong to their respective input contracts. The accepted change is to consolidate the repeated PDF/Word arguments into one record struct shared by its delegate and method; it does not require changing Excel's date-based or ref/span representation.

The exact declaration and handler signature are:

```csharp
public readonly ref struct AddLessonParams
{
    public readonly ScheduleImportContext Context;
    public readonly ref readonly ParsedLesson ParsedLesson;
    public readonly ReadOnlySpan<GroupId> Groups;
    public readonly TimeSlot TimeSlot;
    public readonly Day Day;
    // Constructor omitted from this excerpt.
}

public interface IScheduleAddLessonHandler
{
    public AddLessonResult AddLesson(AddLessonParams p);
}
```

The parser constructs the aggregate at the call site:

```csharp
var err = config.AddLessonHandler.AddLesson(new(
    context: p.Context,
    parsedLesson: parsedLesson,
    groups: lessonGroups,
    timeSlot: rowIterationContext.TimeSlot,
    day: rowIterationContext.Day));
```

These are source excerpts; this existing type is a ref struct, not a record struct.

Accepted direction: introduce a record struct containing all five PDF/Word lesson-import arguments and share it across ImportedLessonHandler, the cohort handler, and AddOrMergeLesson. Excel keeps AddLessonParams as a separate aggregate. The repeated forwarding pattern establishes why the delegate and method should use the same input type; cross-format unification with Excel is not requested.

## Source details

At the Excel parser entry point, `ExcelScheduleParser.Params` groups workbook input, context, and a reusable `StringBuilder` ([source, lines 14–27](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Parsing/ExcelScheduleParser.cs#L14-L27)). For each parsed lesson, the parser constructs `AddLessonParams` with context, parsed lesson, the groups for the cell’s column span, the row’s `TimeSlot`, and the row’s `Day` ([call site, lines 250–260](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Parsing/ExcelScheduleParser.cs#L250-L260)).

The type is defined as a `readonly ref struct`; its `ParsedLesson` is a `ref readonly` field and its group field is a `ReadOnlySpan<GroupId>`. Its constructor assigns those five inputs, and `IScheduleAddLessonHandler.AddLesson` receives exactly one `AddLessonParams` value ([definition and interface, lines 813–849](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Parsing/ExcelScheduleParser.cs#L813-L849)). The two handlers use the same parameter object: FR uses `Day.Date`, groups, and time slot to create a one-time lesson ([lines 851–887](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Parsing/ExcelScheduleParser.cs#L851-L887)); the regular handler uses `Day.DayOfWeek`, groups, and time slot for a weekly lesson ([lines 890–919](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Parsing/ExcelScheduleParser.cs#L890-L919)). Neither handler passes an `Alternative`.

In the shared path, `ScheduleImportContext.AddOrMergeLesson` accepts lesson, `DayOfWeek`, `TimeSlot`, `IReadOnlyList<GroupId>`, and optional `Alternative`; it chooses a slot from `ParsedLesson.StartTime` when present and applies the alternative before group assignment and merging ([source, lines 303–320](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Parsing/ScheduleImportContext.cs#L303-L320)). The Word table parser gathers groups from the cell span and calls this common method with current day and time ([source, lines 525–541](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Parsing/WordScheduleParser.cs#L525-L541)).

## Excel import inventory and provenance

- **Schedule Excel import:** `ScheduleLib.Core/Parsing/ExcelScheduleParser.cs` parses semester schedule workbooks and forwards parsed lessons through `AddLessonParams`. The parameter type first appears in commit `fe684304` (“Add master, fix bugs”, 2026-04-01), which is before June 2026 ([commit](https://github.com/AntonC9018/ScheduleLib/commit/fe6843040566e528f6574d1dfb1f37a65b75393b)). This establishes repository history and authorship metadata only; it does not establish AI authorship.
- **Thesis spreadsheets:** `ThesisListParser.Parse(Stream, ThesisType)` opens a workbook and turns rows into thesis/student records, with group and teacher names and multilingual thesis titles ([entry point](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/ThesesParsing/ThesesListParser/ThesisListParser.cs#L105-L125), [result fields](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/ThesesParsing/ThesesListParser/ThesisListParser.cs#L25-L39)). It is a distinct curriculum/thesis domain and has no schedule lesson payload analogous to the arguments here.
- **Teacher-list spreadsheet import:** `ExcelTeacherListParseParams` groups only the Open XML `SpreadsheetDocument` and `ScheduleBuilder`; its parser adds teacher identities ([source](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/Integration/Tasks/Initialization/ExcelTeacherListParser.cs#L10-L20)). This shows another named parameter struct, but it is unrelated to lesson ingestion.
- **Attendance Excel:** `AttendanceListsExcelParser` imports attendance data and has its own workbook parse parameter object; it maps attendance rows to existing lessons, not schedule-import lessons. It is not the precedent to reuse ([source](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/OnlineRegistry/AttendanceExcel/AttendanceExcel.cs#L30-L45), [parameter type](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/OnlineRegistry/AttendanceExcel/AttendanceExcel.cs#L1251-L1268)).
- **Parity spreadsheet naming:** `ScheduleLib.Dates/ParityExcelParser.cs` is a filename false positive for Excel: it parses a Wordprocessing document/table to infer parity dates, not an `.xlsx` workbook ([source](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Dates/ParityExcelParser.cs#L46-L60)).
- **Excel export:** the repository also has Excel builders and generated teacher/registry workbooks; these write spreadsheets and do not define an input aggregate for schedule lesson imports.
