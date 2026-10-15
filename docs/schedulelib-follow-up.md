# ScheduleLib analyzer follow-up

The acceptance backlog is [ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229),
based on the [reviewed implementation at 12e601e](https://github.com/AntonC9018/ScheduleLib/tree/12e601e7b62511bdecc44e280407fee12147b230).
This document records the maintainer's 2026-10-15 clarifications. It describes
pending work, not coverage already provided by the published package.

## Discussion record

Continue the design interview by updating this document after each substantive
answer. Keep settled meanings in the root [glossary](../CONTEXT.md), record
architectural trade-offs in concise ADRs, and leave unanswered questions marked
open. Research recommendations remain proposals until chosen; update an earlier
decision explicitly if a later answer changes it. Do not recreate the interview
from scratch at a handoff.

The accepted architectural decisions are
[confident findings independent of fix availability](adr/0001-separate-findings-from-fix-availability.md)
and [structured messages rendered at reporting boundaries](adr/0002-render-structured-messages-at-reporting-boundaries.md).
Custom errors should also
[preserve structured error context](adr/0003-preserve-structured-error-context.md).

## Scope and completion

CodingRules owns reusable diagnostics and fixes. SourceGenerators supplies the
project SDK and reusable infrastructure. ScheduleLib is an acceptance target;
its structural refactors remain separate in
[ScheduleLib #230](https://github.com/AntonC9018/ScheduleLib/issues/230).
Keep one consumer entry-point package, `Anton.CodingRules`, supplying the chosen
analyzers, fixes, and configuration. A metapackage with dependencies, bundled
assemblies, or adapted upstream source are all acceptable: choose the simplest
reliable implementation rather than requiring one physical archive. Survey
existing implementations before writing formatting rules. MIT and Apache-2.0
reuse are acceptable; inspect the actual version's license, retain required
notices, and clearly identify any copied code and its provenance.

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

## Open design questions

- **Q6 — Resolved.** Apache-2.0 is acceptable. Choose the simplest suitable
  distribution approach, including a configured metapackage. If code is copied,
  make that reuse clear and preserve its license and attribution. No upstream
  code or binaries have been incorporated yet.
- **Q7 — Open; needs real examples.** The maintainer regards the illustrative
  `Report($"Saved {path}")` and `File.WriteAllText(path, $"{heading}\n{body}")`
  calls as quick debugging shortcuts rather than a maintainable design; the
  file-writing example needs an abstraction too. This does not yet define a
  general diagnostic or establish which abstraction to generate. Collect actual
  cases from maintainer-authored and agent-authored code, with provenance and
  candidate solutions, before asking for the policy decision. Distinguish actual
  current analyzer output from a hypothetical future finding.
- **Literal/builder boundaries.** Multiline string literals are explicitly
  allowed. The existing ListStringBuilder interpolation in the lexer is now an
  accepted case (Q10). Do not infer an exemption for all string construction,
  all handlers, or complex computation nested inside holes.

No universal rule requiring message records for short messages or arbitrary
string-taking APIs has been accepted. The next interview round should compare
real examples rather than repeat abstract scope questions.

The [real string-formatting cases](string-formatting-examples-research.md)
record source provenance, reduced-harness CR03 observations, and alternatives.
The following answers refine the policy:

- **Q8 — Resolved.** Name the complete file pattern as a const when the document
  extension is constant. When the extension varies, use a named local function
  for the construction. A message record is not required for this pattern.
- **Q9 — Structured context preferred; lazy Message recommendation.**
  Prefer custom exceptions where possible and keep associated context as
  accessible structured data, not solely in prose. The maintainer questions
  eagerly formatting that context into base(message), especially if handling
  never inspects the message, and asks whether overriding Message is the usual
  pattern. Research confirms that base(message) is the usual documented custom
  exception pattern, while overriding Message to render stored context is also
  used by framework exceptions. Prefer typed read-only context and consider an
  overridden Message for deferred rendering; keep the general diagnostic/fix
  boundary open. Do not treat the previous base-interpolation example as an
  approved final design merely because current CR03 skips it.
  The [exception research](structured-exception-research.md) documents the
  framework precedents and distinguishes structured context from its automatic
  inclusion in any particular logging provider's output.
- **Q10 — Resolved.** The existing lexer call
  `list.Append($"{type} - {t.Value.Span}")` is acceptable. Preserve this real
  ListStringBuilder/custom-handler case; it does not settle every builder or
  handler expression.

The specific reporting interface, analyzer composition mechanics, and ordinary
engineering choices are delegated implementation work. Investigate concrete
handler and evaluation cases before offering a fix; ask the maintainer only
where those cases expose uncertain intended behavior.
