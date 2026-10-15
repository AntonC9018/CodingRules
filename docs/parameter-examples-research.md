# Parameter-count examples for discussion

Scope: public `AntonC9018/ScheduleLib` at [`feat/registry-history-analysis` / `4c26748e448fe7ccbdffc3f8f6c4b05ffddce144`](https://github.com/AntonC9018/ScheduleLib/tree/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144). This is source evidence for design discussion, not a claim about analyzer diagnostics or fixes. Counts below include context such as `CancellationToken`; constructors are excluded.

## Observed shapes

1. **Three-parameter domain operation; options have a distinct role.** `CourseNameParsing.Parse(this CourseNameParserConfig config, ReadOnlyMemory<char> course, CourseNameParseOptions options = default)` ([definition](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Parsing/CourseNameParser.cs#L117-L123)). Representative call: `CourseNameParsing.Parse(new(new()), group.Key.Name.Value.AsMemory(), new() { IgnorePunctuation = false })` ([call](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/CurriculumShared/CurriculumNameParser.cs#L677-L680)). The receiver is parser configuration, the input is the course text, and options govern punctuation interpretation. The three roles remain legible at the call site. Blame traces the declaration/configuration receiver to owner-attributed commits `8be2dc1e` / `2b18c1c4` (2025-01-22), options to `3415444f` (2025-02-01), and the current input type to `911572a7` (2026-01-04). These lines predate June 2026; this does not prove human authorship.

2. **Four parameters including cancellation, with separate schedule payload and output mechanics.** `ScheduleSerializer.Serialize(Schedule schedule, Stream outputFile, string hash, CancellationToken cancellationToken)` ([definition](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Model/ImmutableModels/Serializer.cs#L14-L24)). Representative call: `await ScheduleSerializer.Serialize(schedule, json, "", cancellationToken)` ([call](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/FmiScheduleImport/FmiPdfImport.cs#L81-L81)). These express the schedule, destination, serialization identity/hash, and async cancellation. The signature and body trace to owner-attributed commit `5bc1ea53` (2025-10-05), before the June 2026 boundary. Under the accepted threshold it needs redesign; the remaining decision is whether a proposed grouping has meaningful domain cohesion.

3. **Five-parameter domain helper.** `ScheduleImportContext.AddOrMergeLesson(in ParsedLesson lesson, DayOfWeek day, TimeSlot timeSlot, IReadOnlyList<GroupId> groups, Alternative alternative = default)` ([definition](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Parsing/ScheduleImportContext.cs#L303-L310)). Representative calls use the common four-value shape or pass an explicit alternative ([parser call](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Parsing/WordScheduleParser.cs#L463-L470), [alternative call](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/Integration/Tasks/Initialization/ElectiveCohorts.cs#L81-L86)). The lesson, day/time, group set, and optional partition are distinct import facts. The signature has no `CancellationToken`. Blame traces these declaration lines to owner-attributed commit `517c1d83` (2026-09-26, no Generated-by trailer). This falls within the maintainer's mostly AI-generated period; do not treat it as an established older convention.

4. **Five parameters in a current CLI result adapter.** `CliConfigExecution.Finish<T>(CliCommandContext context, int exit, string[] errors, T data, string verb)` ([definition](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/MainCli/CliConfigExecution.cs#L45-L51)). Call: `CliConfigExecution.Finish(context, CliExit.Success, [], inspect(resolved), verb)` ([call](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/MainCli/Commands.Config.cs#L127-L133)). Context, outcome, errors, payload, and verb are meaningful separately, although they form one cohesive completion. File history attributes the scoped config work to a 2026-10-08 commit with `Generated-by: GPT-6.1 Sol`; this is recent generated work, not evidence of an older convention.

5. **Ten parameters in the current settings editor.** `CliSettings.Edit(SettingsArguments arguments, SettingsScope scope, string key, string? value, bool unset, CancellationToken cancellationToken, string? invocationDirectory = null, string? userFile = null, string? operation = null, string? item = null)` ([definition](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/MainCli/CliSettings.Writes.cs#L22-L36)). Representative call passes the six required values and names optional operation/item ([call](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/MainCli/Commands.Config.Writes.cs#L98-L106)). The first six specify settings context and requested mutation; the rest configure file resolution or collection operation. Git history dates this writer to 2026-10-08 and records `Generated-by: GPT-6.1 Sol`. This makes a strong discussion example for separating a cohesive edit request from optional execution context, but is recent generated code.

6. **Four parameters imposed by a framework contract.** `SequenceSegment.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)` ([implementation](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Helper/Parser/SequenceReader.cs#L68-L88)) implements `ISpanFormattable` on the enclosing type ([declaration](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Helper/Parser/SequenceReader.cs#L66-L68)). These roles are fixed by the .NET interface. An imposed-contract exemption is proposed, not accepted yet. The signature traces to owner-attributed commit `e50d9173` (2026-01-27), following the earlier source path through the move. Changing its shape would stop implementing the interface.

## Discussion implications

- With the accepted threshold of **more than three** parameters, four or more is the candidate set, including context parameters. A count can identify review candidates, but the examples show both well-separated domain facts and a cohesive request with optional execution details.
- The current `Finish` and `Edit` APIs provide concrete recent examples, with explicit generated-by provenance. The parser, serializer, and TryFormat signatures have verified pre-June introduction history. Owner attribution alone still does not establish human authorship.
- A possible *new illustrative proposal*, not an existing or tested API, is a small `SettingsEditRequest` grouping `SettingsArguments`, `SettingsScope`, key/value/unset intent, and optional operation/item. Keep cancellation and directory/file dependencies explicit at the execution boundary if they serve separate lifecycle/configuration roles. Do not collapse unrelated dependencies into a general-purpose context bag merely to lower the count.
- Open question: exempt interface and override signatures whose shape is imposed by an external contract? Recommend yes. Do not extend this automatically to locally owned interfaces that can be redesigned. Constructors were outside this scan's scope.

## Candidate redesigns for review

These are proposals, not source excerpts or implemented fixes. They retain the
accepted maximum of three parameters, including execution context.

The serializer currently receives a schedule and its hash separately. A named
serialization input could carry those related values, leaving the output stream
and cancellation explicit:

```csharp
readonly record struct ScheduleSerializationInput(Schedule Schedule, string Hash);

// Proposed signature:
Task Serialize(ScheduleSerializationInput input, Stream outputFile,
    CancellationToken cancellationToken);
// Proposed call replacing Serialize(schedule, json, "", cancellationToken):
await ScheduleSerializer.Serialize(new(schedule, ""), json, cancellationToken);
```

The [cache loader](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/Integration/Tasks/Initialization/ScheduleLoader.cs#L39-L88)
computes the hash from configuration and loader inputs, compares it with the
stored model's Hash, and passes it when writing the built schedule. Thus it is
cache validity metadata, not simply a hash of the schedule's serialized bytes.
FmiPdfImport passes an empty hash. Grouping these as serialization payload plus
metadata is a proposal; a writer object owning the stream is another possible
design if it matches actual lifecycle and reuse.

For AddOrMergeLesson, a candidate is a named lesson occurrence containing the
parsed lesson, day, time slot, groups, and alternative, passed as one value. Check
existing lesson/occurrence models before adding another. The current five values
should not merely be hidden in an unstructured context bag.

For the newer CLI examples, Finish should first be checked against the existing
CliCompletion<T> rather than inventing another completion type. Edit needs a
cohesive edit request, with cancellation and genuine execution dependencies kept
explicit. These are implementation investigations, not additional policy choices.
