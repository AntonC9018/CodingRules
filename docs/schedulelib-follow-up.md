# ScheduleLib analyzer follow-up

The acceptance backlog is [ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229),
based on the [reviewed implementation at 12e601e](https://github.com/AntonC9018/ScheduleLib/tree/12e601e7b62511bdecc44e280407fee12147b230).
This document records the maintainer's 2026-10-15 clarifications. It describes
pending work, not coverage already provided by the published package.

## Scope and completion

CodingRules owns reusable diagnostics and fixes. SourceGenerators supplies the
project SDK and reusable infrastructure. ScheduleLib is an acceptance target;
its structural refactors remain separate in
[ScheduleLib #230](https://github.com/AntonC9018/ScheduleLib/issues/230).
Keep one consumer package, `Anton.CodingRules`, including any adopted analyzers
and required fixes. Survey existing implementations before writing formatting
rules. Configuring existing tools or adapting MIT source with retained credit
are both possible; inspect the actual version's license and package contents.

Interpret the review comments and choose implementation details without asking
the maintainer about routine engineering decisions. Ask when the intended code
style or behavior is uncertain, using concrete examples and counterexamples.

A referenced requirement is complete when tests demonstrate coverage of its
cited cases and the accepted cases that must remain unreported. A configuration
key, a clean formatter run, or a successful build alone does not prove that
coverage. State the implemented generalization and its remaining boundaries.
Publishing and downstream adoption are separate from this coverage criterion.

Confident diagnostics may report even when a safe automatic refactoring is not
available. Multiple alternative fixes are allowed, such as local-function
extraction and statement expansion. Every offered fix must preserve behavior;
uncertainty about a fix must not be confused with uncertainty about the rule.
The original CR0001 contract still requires a safe fix for every reported case.
Existing families' documented safe-fix-only detection remains their current
implementation, not an automatic restriction on all new rules.

## A6: separate message data from rendering

The maintainer refined A6 beyond extracting a completed string into a local.
For a compound human message, the intended design is a named record struct
containing the message data, passed to a generic reporting boundary which
renders it where needed. This keeps the call site focused on data and makes
localization possible at the rendering boundary.

The reviewed `Report` takes `string human`; the existing interpolations therefore
produce strings before reporting. The preference is not fully satisfied merely
by naming that string. Two representative original shapes are:

```csharp
Report(..., $"Delete event {identity.Id}: {identity.Summary}");

Report(..., $"Create event [{index}]: {ev.Summary}; {ev.Start?.DateTimeDateTimeOffset}–{ev.End?.DateTimeDateTimeOffset}; {string.Join(", ", ev.Recurrence ?? [])}");
```

These are schematic call excerpts; retain the exact complete original calls
and declarations in acceptance fixtures. The direct constructed exception
passed to `StreamingFailure` is a separate, non-message A6 example.

`ISpanFormattable` is a candidate reporting constraint, not a settled universal
requirement or a license to introduce a message type mechanically. Its formatting
contract does not itself define resource lookup, message identity, or translation.
Choose the smallest contract that supports the reporting and localization needs.

An automatic rewrite must account for:

- The actual selected parameter and overload: string, deferred
  `FormattableString`, or a custom interpolated-string handler.
- Handler constructor gating and append-method short circuiting. Moving data
  construction or hole computation outside a disabled handler can make formerly
  skipped work run.
- The order of hole evaluation and formatting, including property reads,
  mutations, exceptions, culture, format strings, and alignment.
- The handler's actual `AppendFormatted` overloads and generic constraints.
  A handler supporting the original hole types need not accept a new message
  type or treat it as the same sequence of holes.
- Stable versus deferred message data: capturing a mutable object or collection
  does not snapshot the values that an earlier interpolation would have observed.
- Loss of structured values, escaping, or provider-specific handling when
  several original holes become one formatted object.
- The distinction between accepting `ISpanFormattable` and actually invoking
  `TryFormat`. A custom handler may instead use `ToString` and allocate.

Keep handler-aware interpolation at its original site unless the rewrite proves
the relevant behavior is preserved. Leave architectural changes without an
automatic fix when that proof requires a human choice.

## Coverage work to establish

Use the individual A1–A16 items in #229 as the source of acceptance fixtures.
Do not mark a rule covered merely because an existing diagnostic appears related.
Record both current detection and available fixes for each original example,
with negative fixtures for the explicitly accepted chained service call, direct
initializers, and simple factory values. Initializer false positives remain
tracked in [CodingRules #19](https://github.com/AntonC9018/CodingRules/issues/19).

For A1–A3 and A12, first establish what existing formatters/analyzers can enforce
and fix with their supported configuration. In particular, a formatter applying
whitespace need not apply expression-body-to-block code-style fixes. The research
report distinguishes configuration, package composition, licensed source
reuse, and any genuinely missing behavior. The
[formatting reuse research](formatting-reuse-research.md) records the tested
upstream candidates, gaps, package composition, and version-specific licenses.
