# ScheduleLib analyzer follow-up

The acceptance backlog is [ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229),
based on the [reviewed implementation at 12e601e](https://github.com/AntonC9018/ScheduleLib/tree/12e601e7b62511bdecc44e280407fee12147b230).
This document records the maintainer's 2026-10-15 clarifications and subsequent
implementation decisions, including the 2026-10-18 overload boundary approval.
Implementation coverage below is distinct from publication of the package.

## Discussion record

The high-level interview has reached the implementation handoff. Update this
document when subsequent answers settle remaining policy; leave unresolved
generalizations under manual review rather than reopening the whole interview. Keep settled meanings in the root [glossary](../CONTEXT.md), record
architectural trade-offs in concise ADRs, and leave unanswered questions marked
open. Research recommendations remain proposals until chosen; update an earlier
decision explicitly if a later answer changes it. Do not recreate the interview
from scratch at a handoff. The [implementation ticket index](tickets/schedulelib-follow-up/README.md)
links the approved, published slices and their blocking edges. ScheduleLib
implementation branches and PRs target `feat/registry-history-analysis`, as
specified in its four tickets.

The accepted architectural decisions are
[confident findings independent of fix availability](adr/0001-separate-findings-from-fix-availability.md)
and [structured messages rendered at reporting boundaries](adr/0002-render-structured-messages-at-reporting-boundaries.md).
Custom errors should also
[preserve structured error context](adr/0003-preserve-structured-error-context.md).
The serializer should [configure its operation through a builder](adr/0004-configure-serialization-through-a-builder.md).

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
initializers, and simple factory values. The
[CodingRules #19](https://github.com/AntonC9018/CodingRules/issues/19) fix excludes
object-initializer assignments from consumed mutations while preserving analysis
of combined right-hand-side operations. Regression tests cover the original
empty collections and lesson-time factory values, nested initializers, and
genuine consumed mutations outside initializers.

Slice 07 now covers the non-message A6/A7/A8 computations through CR0300,
CR0301 and the exact framework service-lookup addition to CR0203. The
[computed-operation contract](computed-operations.md) records the pinned source,
parameterless metadata exception, lazy composed-pipeline branches, same-site
params boundaries and service-result naming. Shared unit/package fixtures retain
direct initializers, factories, the allowed service receiver chain and lexer
handler interpolation. Runtime tests verify guarded lookups, exception timing,
lazy enumeration and handler constructor/append gating. The final candidate
package/consumer gate remains deferred; this does not implement structured
message or exception API redesign.

For A1–A3 and A12, first establish what existing formatters/analyzers can enforce
and fix with their supported configuration. In particular, a formatter applying
whitespace need not apply expression-body-to-block code-style fixes. The research
report distinguishes configuration, package composition, licensed source
reuse, and any genuinely missing behavior. The
[formatting reuse research](formatting-reuse-research.md) records the tested
upstream candidates, gaps, package composition, and version-specific licenses.

Slice 02 now implements A2/A3/A12 through the consumer package. The
[formatting implementation](formatting.md) records the chosen Roslynator 5.0.1
binary analyzer delivery with a CodingRules RCS1016 fixer, selective
configuration, Boolean-only CR0800 gap, conservative RCS1016 exclusions for
arrow-token trivia and custom async return contracts, tested Roslyn
4.10/SDK 10.0.112 hosts, and required CLI configuration. Slice 03 adds
CR0801/CR0802 for A1's multi-member initializer expansion and adjacent sole
constructor arguments. The same consumer gate verifies exact output, accepted
layouts, comments, nesting, unchanged runtime evaluation and a byte-identical
second pass. Focused tests cover all four pinned A1 sites and preserve existing
operation findings; #19's separate fix now accepts direct initializer assignments.
This implementation does not establish coverage for other A items.

Slice 04 implements A4/Q15/Q16 as CR0900 manual findings for tuple method
results, method inputs and catalogued stored collection data. The
[tuple data contract](tuple-data.md) records the conservative generalization,
the local-function result exception, and positive/accepted regression evidence
in analyzer tests and the isolated package gate. No universal scalar-local tuple
policy or automatic API migration is introduced.

Slice 05 implements the revised A5/Q12/Q17/Q18 count boundary as CR1000 manual
findings on owned interface, virtual/abstract, delegate and abstract-class
constructor declarations with four or more parameters, including cancellation.
The [contract parameter boundary](contract-parameters.md) records ownership and
external exemptions, accepted static Finish/Edit fixtures, and the shared compiled
positive/accepted cases checked by analyzer tests and the isolated package gate.
Overload/builder and repeated-plumbing decisions remain independent; ordinary
nonvirtual instance methods receive no new blanket count rule.

Slice 06 implements Q19 as CR1001 manual configuration-builder findings on
multi-parameter method declarations, including static operations. The approved
pairwise boundary requires the same declaring type, name, staticness, generic
arity and parameter count, exactly one differing resolved parameter type, and
matching ref-kinds, params, optional and default presence. Parameter names may
differ. The [overload configuration boundary](overload-configuration.md) records
the conservative symbol comparison, one-parameter Parse/Output exceptions, and
shared analyzer/package regression evidence. Required-step checks remain manual
with runtime validation; the RequiredStep analyzer remains deferred.

Slice 08 reuses CR0201/CR0202/CR0203 for the actual A3/A11/A15 config-result,
JSON lookup, cancellation/authentication and rejected-action status conditions.
The [compound-condition boundary](compound-conditions.md) records baseline
probes, scope-safe throw guards, nested property subjects, the exact six-status
named enum predicate kept inside the exception filter, accepted simple
invariants and shared compiled/package evidence. Other status sets and semantic
policy generalizations remain for agent review; no new rule ID is added.

Slice 09 proves the existing CR0500 behavior for A9's pinned SettingsEdit return
and ordinary construction. No detection or binding extension was needed. The
[named argument contract](named-arguments.md#schedulelib-a9-return-construction)
records the source/configuration investigation and shared unit/package consumer
regression: bound string-role names, unchanged Boolean, preserved expressions
and overload/evaluation behavior, exact opt-outs and framework exceptions.
Scope-value extraction remains separate; the exact parameterless chain receives
no current CR03 finding. Final common-package validation and downstream adoption
remain separate from this coverage work.

Slice 10 adds CR1100 for A10's desired identifier and lock path using a
conservative runtime string-only concatenation boundary. The
[interpolation contract](string-concatenation.md) records the upstream reuse
probe, explicit invariant conversion, binding and handler safety, accepted
cases, and shared saved-source/runtime regression. Its packaged consumer test
is registered for the final stable candidate; no package validation runs during
this ticket. Universal inline-string/message-record policy remains open.

Slice 11 implements A13/Q13 as CR1200 for proven direct current-value and
reference-null branches on stable storage. The [assignment contract](unchanged-assignments.md)
records conditional updates in statement/return/property/accessor hosts,
conservative setter/receiver/ref/volatile boundaries and preserved assignment
result conversions. Shared compiled/runtime and saved-source cases cover the
pinned reflection traversal, generalized branches and accepted effectful targets.
Its package regression is registered for the final stable candidate; package
validation and downstream adoption remain separate.

Slice 12 implements A14/Q14 as CR1300, a manual duplication-candidate warning
for matching framework timestamp formatting in one method/local function.
The [recognition contract](repeated-formatting.md) records constant format,
provider-source and null-propagation comparisons, the exact start/end chains,
independent semantic policies and scoped suppressions. Diagnostics link directly
to semantic-review guidance; no automatic helper extraction is offered. Shared
positive/accepted consumer fixtures and an isolated package gate are ready for
the final stable candidate. Package execution and guidance import remain separate.

Slice 13 implements A16 as CR1400, a manual sealing-review warning adapted from
MIT-licensed MA0053 source. Public/virtual/inherited classes remain findings,
including CalendarCommands and its derived test seam. The [sealing contract](sealed-classes.md)
records the narrow type-forwarding generic attribute exception, concrete external
framework constraints and shared positive/accepted fixtures. No automatic sealing
or replacement test architecture is chosen. The isolated package regression is
registered for the final stable candidate; package execution remains deferred.

## Open design questions

- **Q6 — Resolved.** Apache-2.0 is acceptable. Choose the simplest suitable
  distribution approach, including a configured metapackage. If code is copied,
  make that reuse clear and preserve its license and attribution. Slice 02
  incorporates unmodified Apache-2.0 Roslynator binaries with license, NOTICE
  and provenance, omitting the unsafe common fixer assembly in favor of an
  independent MIT CodingRules RCS1016 fixer. The compiler suppressor and the
  fixer both exclude unsafe shapes because the tested CLI bypasses suppressors.
  No upstream source has been copied or modified.
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

## Data shapes, parameter groups, and reuse: Q11–Q14

These answers supersede the earlier recommendations for A4, A5, A13, and A14.
They are policy decisions and research boundaries, not claims of implemented
diagnostic coverage.

- **Q11 — More tuple examples required.** The calendar plan example needs a
  named record struct. The suggested general exemption for short-lived local
  tuples was not accepted. Mine more real examples before deciding which tuple
  uses may remain; neither a universal tuple ban nor a local-tuple exemption
  had been established at this point. Q15–Q16 below resolve the collected
  examples, including an exception for local-function results. Distinguish tuple
  data from deconstruction syntax.
- **Q12 — Four or more parameters.** More than three parameters is excessive;
  count context parameters, including CancellationToken. The earlier proposed
  five-parameter trigger is rejected. This initial broad scope is refined by
  Q17–Q18 below: static functions may keep separate arguments; interface,
  virtual/abstract, delegate, and abstract-class constructor contracts require
  grouping above three. Ordinary constructors were originally excluded;
  abstract-class constructors are now explicitly included. Do not invent an
  arbitrary context bag just to reduce the count.
- **Q13 — Generalize proven unchanged branches.** Flag the null-preserving
  assignment and other branches that provably retain the current value, such
  as `current = condition ? current : next`. If the affected operation appears
  in an expression-bodied property/accessor, the refactoring must introduce
  a block body with the explicit conditional update and any required return.
  A property/accessor host is distinct from an assignment to an effectful
  property/indexer target; do not remove setter effects without proof.
- **Q14 — Duplication candidates require semantic review.** Begin with the
  repeated formatting shape previously proposed (same method and matching
  formatting operation/format/provider, possibly different receivers). A
  matching expression may represent a different domain concept and should not
  then be factored into a common helper. The analyzer cannot settle that
  distinction. Agent guidance imported by consuming repositories must tell
  agents to examine the semantic roles before applying a duplication rewrite.
  Preserve separate implementations when their meaning or reasons to change
  differ; do not treat structural similarity as mandatory abstraction reuse.

## Provenance and the next examples

The maintainer clarified that most code from June 2026 onward is AI-generated
and not thoroughly reviewed. Prefer pre-June examples when investigating the
maintainer's own style; use introduction history and Generated-by trailers,
not merely the current author's account or the last formatting edit. Even
older source is evidence to discuss, not automatic approval of every pattern.
The restarted scans are complete. Reviewed source and introduction history are
recorded in [tuple examples](tuple-examples-research.md) and
[parameter examples](parameter-examples-research.md). Pre-June examples include
a mapping table, private parser result, local iterator result, four-parameter
serializer, and ISpanFormattable implementation. Recent CLI examples have explicit
Generated-by provenance. The maintainer subsequently identified the shown
lesson-import/plumbing code as AI-generated by Astra (Q20); owner attribution
in its commit history must not be treated as human authorship.

## Tuple and parameter refinements: Q15–Q18

- **Q15 — Resolved: named mapping value.** Replace the course-name mapping
  table's `(string From, string To)` elements with a named record struct.
- **Q16 — Resolved: local-function result exception.** The nonempty-cell local
  iterator may retain its tuple result. The private qualification parser and
  ordinary generic filtered-item method should return named record structs.
  Private methods do not inherit the local-function exception. The calendar plan
  collection still needs a record struct; its use by a local function does not
  make the collection a local-function result. Purely local swaps were not found
  in this scan and remain undecided. Deconstruction syntax is not itself banned.
- **Q17 — Revised: builders and contracts.** The proposed schedule-plus-hash
  record was rejected as an unhelpful grouping. For the serializer, prefer a
  reusable builder with Input, Output (stream/path alternatives), optional Hash
  (default empty), and ExecuteAsync(cancellationToken). The exact API is delegated;
  the maintainer's state-plus-static-callback sketch is illustrative. A builder
  must address missing required configuration rather than merely moving the
  failure away from the call site. Research existing required-call analyzers
  before implementing another one.

  The parameter rule now distinguishes static helpers from contracts imposed on
  callers/implementers. Static functions may retain separate arguments. Locally
  owned interfaces, virtual/abstract methods, constructors of abstract classes,
  and delegate types must group arguments when there are more than three,
  including context. An imposed external signature remains exempt (Q18).

  Overloaded APIs require a builder when the operation has more than one
  parameter and at least one parameter has alternative overload forms (Q19).
  One-parameter overloads remain allowed. Repeated
  argument plumbing requires a builder or context object and should be reviewed
  through agent guidance: counting parameters alone cannot establish that
  architectural need. Do not assume the new static exception exempts repeated
  plumbing or the Q19 overload rule. Q20 settles the shown import instance
  methods as a shared-record case; the broader category of ordinary nonvirtual
  instance methods has not been classified from parameter count alone.
- **Q18 — Resolved: external signature exemption.** The four-parameter
  ISpanFormattable.TryFormat implementation is allowed because its signature is
  imposed by an external contract. The maintainer still regards that original
  interface design as poor: external compatibility is the reason for the
  exemption, not approval of this shape in new locally owned interfaces.

## Overload scope and usage context: Q19–Q20

- **Q19 — Resolved: more than one parameter plus overload alternatives.** Use a
  builder when an operation has more than one parameter and at least one of
  those parameters has alternative overload forms. The earlier recommendation
  to restrict this to signatures exceeding three parameters was not accepted.
  A one-parameter Parse(string)/Parse(ReadOnlySpan<char>) pair is allowed. This
  overload rule is independent of the more-than-three grouping requirement for
  the specified contract kinds; static functions are not exempt from it.

  **Approved initial overload-alternative boundary (2026-10-18).** A pair must
  have the same declaring type, method name, staticness and generic arity;
  equal parameter counts greater than one; and exactly one differing resolved
  parameter type. All other parameter types and all corresponding ref-kinds,
  params flags, optional flags and default presence must match. Parameter names
  may differ. Differing-arity pairs and pairs with more than one parameter type
  difference remain outside initial coverage. CR1001 implements this exact
  pairwise boundary as a manual finding, without an automatic API rewrite or
  required-step analysis. [Rule documentation](overload-configuration.md) records
  generic parameter normalization, locations, configuration and regression evidence.
- **Q20 — Resolved for the shown import flow: one shared record struct.**
  The lesson, day, time slot, groups, and alternative should travel together in
  one record struct through the importer, delegate, cohort handler, and
  AddOrMergeLesson. Group all the arguments of that shared payload, not an
  arbitrary subset. Preserve the per-cohort changes to groups/alternative and
  the import context's existing merge policy. The maintainer identifies this
  code as AI-generated by Astra; it is not a precedent for their preferred
  hand-written style. The [parameter report](parameter-examples-research.md#addormergelesson-usage-for-q20)
  records Word/PDF import, elective cohort routing, and test usage. The broader
  rule for ordinary instance methods remains separate from this concrete
  repeated-plumbing decision. The [Luna Excel scan](excel-import-input-research.md)
  confirms the existing AddLessonParams readonly ref struct and one-argument
  IScheduleAddLessonHandler.AddLesson interface as a precedent. The maintainer's
  final clarification is that PDF/Word should have their own record struct,
  shared across their delegate, handler, and method; Excel keeps its existing
  aggregate. The repeated forwarding usage establishes this sharing boundary.
  The previous interpretation requiring a unified model across Excel and
  PDF/Word is superseded. No reconciliation of Excel's date/ref/span contract
  with the PDF/Word payload is required by this decision. The existing Excel
  declaration dates to April 2026; that history does not itself establish human
  authorship.

The [Luna builder research](builder-protocol-analyzers-research.md) is complete.
It found published MIT-licensed staged-builder generators, but did not verify a
general C# analyzer that proves a set of required method calls in the proposed
statement callback. MustUseReturnValue checks discarded results, not setter
completion. Staged generators require a different returned-chain API; their
advertised guarantees have not been checked in a compilation probe. A small,
explicitly opted-in reusable callback protocol rule is a possible implementation
path if that callback shape is retained. Runtime configuration validation remains
necessary. The maintainer rejected the staged-generator alternatives as the
intended solution. Record the [RequiredStep analyzer idea](required-builder-steps-proposal.md)
for later: annotate core methods and infer coverage transitively through
overloads/higher-level methods, including helpers covering multiple steps.
Probably discover requirements only on the builder itself at first, with
convenience overloads/helpers in extensions. No analyzer implementation or
annotation integration is requested now; agents must check completeness manually.
No package or generator was selected; the survey is not proof that no suitable
analyzer exists anywhere.

## Consumer agent guidance

[Agent coding rules](agent-coding-rules.md) records semantic reuse, repeated
plumbing, builder completeness, structured reporting, exception context and the
accepted string-construction boundaries. Slice 14 includes this self-contained
file in each version of `Anton.CodingRules`. Its explicit
`ExportCodingRulesAgentGuidance` target copies the rules to a consumer-selected
directory; [consumer import instructions](consumer-agent-guidance.md) and
`samples/AgentGuidance` show how an existing AGENTS.md directs agents to read it
without overwriting unrelated instructions. No export runs on normal build or
restore, and a NuGet reference alone does not make agents read the file. Focused
tests exercise export and sample discovery from the declared package layout;
final nupkg verification remains deferred until the stable candidate after all
tickets. This repository continues to import the source guidance through AGENTS.md.
Universal interpolation, blanket ordinary-instance-method grouping and RequiredStep
analysis remain undecided/deferred; no new analyzer or downstream refactor is added.

## ScheduleLib rollout refinements — 2026-10-24

The reviewed legacy collection overloads should be explicit interface members;
CR1500 diagnoses public non-generic-only collection APIs. Shared generic members
remain public. CR1001 excludes explicit implementations, allows one effective
parameter and recognizes builder types/extension receivers. Default suffixes
Builder, Descriptor and Configurator can be replaced with the comma-separated
`CodingRulesBuilderSuffixes` property, or a type can use `[ConfigurationBuilder]`.
See [overload configuration](overload-configuration.md).

Boolean Equals/EqualsN calls allow positional arguments; exact external-method
registrations use assembly attributes or MSBuild. CR0803 requires braces for
single statements; CR0804 requires a space after casts. A cast itself is an
accepted CR03 argument, while `TimeSpan.FromSeconds` remains an operation.
Computation names prefer the destination parameter, then operation/type.
Exception interpolation is excluded from CR03 extraction and uses CR1600's
[opt-in context-preserving subtype action](exception-context.md).

ScheduleLib's two exception bases become abstract with sealed general concrete
subtypes. CalendarCommands retains a local suppression pending
[audit #247](https://github.com/AntonC9018/ScheduleLib/issues/247). The existing
test failures are tracked in [audit #248](https://github.com/AntonC9018/ScheduleLib/issues/248).
The measured [Fix All performance correction](fix-all-performance.md) retains
sequential semantic validation while avoiding repeated scans of unrelated files.
