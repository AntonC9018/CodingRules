# CodingRules guidance for agents

This file complements analyzer diagnostics with judgments that syntax analysis
cannot establish. It ships as `agent-guidance/agent-coding-rules.md` in each
version of `Anton.CodingRules`. A package reference alone does not make an agent
read it. Export the file explicitly, then instruct the consuming agent to read
the exported file through the repository's existing instructions. See the
[import workflow](https://github.com/AntonC9018/CodingRules/blob/main/docs/consumer-agent-guidance.md).
The guidance below is self-contained; linked documents supply background.

## Review duplication by meaning

A duplication finding identifies a candidate for reuse, not proof that the
matched expressions represent the same domain concept. Before extracting a
common helper, compare what each expression means, which rules govern it,
and why each is likely to change.

- Share a helper when the sites express the same operation and policy, such
  as a deliberately common timestamp representation.
- Keep sites separate when they express semantically distinct operations,
  even if their current implementations happen to match. Independent naming
  or helpers may make that distinction clear without coupling their policies.
- Do not merge separately meaningful helpers merely to silence a structural
  duplication diagnostic. Use a narrowly scoped suppression with an explanation
  when needed to preserve an intentional distinction.
- Preserve receiver evaluation, formatting, culture, exceptions, and branch
  timing. Sharing an operation does not mean eagerly computing all of its
  results before their original use sites.

Apply this review to each candidate before a batch refactor. Infer the domain
meaning from the code and its documented model; ask the maintainer only when
that meaning remains uncertain. An analyzer's inability to prove a semantic
distinction does not make the distinction unimportant.

CR1300 identifies repeated timestamp formatting in one
method. Start/end values sharing a calendar timestamp representation may warrant
one helper. Audit timestamps and exported timestamps may instead have independent
versioning policies despite both currently using invariant `"o"` formatting.
Keep those policies separate, using independently named helpers or a narrow
CR1300 pragma/SuppressMessage with that reason. A diagnostic is a request for
this review; it does not require a shared abstraction. The shared accepted-case
fixture in the CodingRules source at
`CodeFixTests/Consumers/RepeatedFormatting/Accepted.cs` demonstrates
both intentional separation and justified suppression.

## Review repeated argument plumbing

When the same related values are carried repeatedly through several calls or
layers, use a builder or a cohesive context object. Establish their shared role
and lifetime before choosing the abstraction; do not hide unrelated dependencies
in a generic argument bag solely to satisfy a parameter count. A static helper's
parameter-count exception does not remove this architectural review.

For the lesson-import flow reviewed in Q20, use one shared record struct carrying
all the lesson/day/time/groups/alternative arguments across the importer,
delegate, cohort handler, and merge operation. Preserve the merge policy and
per-cohort changes to groups/alternative. This record struct belongs to the
PDF/Word flow and must be the same input type used by its delegate and method.
Excel retains its existing AddLessonParams aggregate; the
[Excel scan](https://github.com/AntonC9018/CodingRules/blob/main/docs/excel-import-input-research.md) is a precedent for the pattern,
not a requirement to unify these import contracts. Infer the sharing boundary
from values repeatedly forwarded together. Partial arbitrary groupings do not
satisfy this concrete decision.

For the serializer configuration discussed in the
[ScheduleLib follow-up](https://github.com/AntonC9018/CodingRules/blob/main/docs/schedulelib-follow-up.md#tuple-and-parameter-refinements-q15q18),
use a reusable builder supporting alternate output sources and an optional hash,
with cancellation supplied at execution. Review which configuration is required,
which has defaults, and what happens when required configuration is omitted.
Use a builder when an operation has more than one parameter and at least one
parameter has alternative overload forms. One-parameter overloads remain allowed.
This rule also applies to static operations; it is independent of the separate
parameter-count requirement for extensible contracts.

CR1001 identifies the approved overload pairs: same
declaring type, name, staticness, generic arity and parameter count greater than
one, with exactly one differing resolved parameter type and matching ref-kinds,
params, optional and default presence. Parameter names may differ. Its finding
requires manual builder design and caller migration. Differing counts or multiple
type differences remain outside initial analyzer coverage; review broader repeated
plumbing by shared meaning and usage rather than inferring it from overload count.

## Check builder completeness manually

The [RequiredStep analyzer idea](https://github.com/AntonC9018/CodingRules/blob/main/docs/required-builder-steps-proposal.md) is deferred.
Do not add an analyzer or annotations as part of recording that idea. Until it
is implemented, agents must trace the builder's configuration before execution:

- Identify required configuration and distinguish it from optional defaults.
- Follow overloads and higher-level helpers: a helper can satisfy several core
  steps, but only when those steps run on the same builder on every successful
  path reaching execution.
- Inspect conditional paths and the relevant helper bodies; do not assume a
  method name or fluent API guarantees complete configuration.
- Preserve appropriate runtime checks for incomplete or invalid configuration.

For the proposed serializer builder, Input and Output must be supplied; Hash is
optional and defaults to empty. A helper supplying Input and Output satisfies
both requirements. Do not replace the intended mutable builder API with staged
generators merely to obtain their different form of compile-time checking.

## Render structured message values at reporting boundaries

For the reviewed compound calendar reporting messages, construct a named record
struct containing the message data and pass it to a generic reporting boundary.
Render human-facing text there, where localization and presentation belong.
Merely extracting an already-rendered string into a variable does not establish
this separation. Preserve machine-readable output and the operation's behavior.
Choose the smallest reporting contract that supports the application;
`ISpanFormattable` is a candidate, not a required interface for every message.

Review the selected parameter and overload before changing interpolation:
`string`, deferred `FormattableString`, and custom interpolated-string handlers
have different timing and formatting contracts. Preserve handler constructor
gating, append-method short circuiting, hole evaluation and formatting order,
culture, alignment, format strings, overloads and exceptions. A handler accepting
the original hole types need not accept a new message type or render it the same
way. Accepting `ISpanFormattable` does not prove the handler invokes `TryFormat`.
Keep handler-aware interpolation at its original site unless preservation is
proved. Capturing mutable objects or collections does not snapshot their earlier
values; decide explicitly which observations belong to the reporting operation.
Leave architectural changes manual when they require that choice.

## Preserve accessible exception context

Prefer meaningful custom domain exceptions where appropriate, exposing useful
failure context through typed read-only members. Handlers should inspect that
context without parsing the message. Read-only members holding mutable objects
do not freeze failure-time state; preserve stable context where needed.
Changing a framework exception to a custom type is a deliberate API decision.

Consider overriding `Message` to render stored context lazily when handling may
never read the text. Decide caching, culture/localization, snapshotting and
repeated-read behavior explicitly. An uncached getter can format on each read,
including reads through `ToString`. Eager `base(message)` is the usual documented
framework pattern; lazy rendering is a considered design choice, not a universal
automatic rewrite. Context properties are accessible to handlers but are not
automatically included by every logging provider.

## Preserve accepted string construction

Multiline string literals are allowed. Preserve the reviewed lexer rendering:

```csharp
var type = TokenTypeLabels.Get(t.Type);
list.Append($"{type} - {t.Value.Span}");
```

Here `list` is the existing `ListStringBuilder` with its custom handler and
separator behavior. Do not add a message wrapper or split its append operation
merely because it uses interpolation. This concrete acceptance does not exempt
all builders/handlers or arbitrary nested computations inside holes.

For a machine file glob, name the complete pattern as a `const` when the document
extension is constant, for example
`const string CurriculumDocumentSearchPattern = $"*{CurriculumDocumentExtension}";`.
When the extension varies, construct the pattern in a named local function.
Neither case requires a message record.

## Keep unresolved policies explicit

No universal interpolation rule or requirement for message records on arbitrary
short messages, file-content construction or string-taking APIs is settled.
Use real cases to resolve those questions; analyzer silence alone is not style
approval. Blanket grouping by parameter count for ordinary nonvirtual instance
methods remains undecided; the concrete PDF/Word sharing decision above is
settled independently. Static parameter-count exemptions do not exempt overload
configuration or repeated plumbing. RequiredStep analysis remains deferred:
do not introduce its annotations, an analyzer or a staged-builder generator.

## Keep generated steps readable

- Prefer a local variable for a one-off computed value, and a local function with
  guard clauses and explicit returns for a branching decision. Name helpers after
  their domain decision, not generically (`CheckCondition`, `CalculateArgs`).
- Do not copy an unchanged local just to pass it on, but keep aliases that clarify
  a role. Single use alone does not make a local useless.
- Do not move a computation into a narrower scope when that delays, repeats or
  skips observable work.
- An automatic refactoring's output is not evidence of readability; review it.
- Do not add shared API members solely for tests. Keep fixture construction in
  tests.

## Keep predicates and branches simple

- Use a block lambda with guard clauses when a predicate mixes `&&` or `||` with
  another LINQ predicate. Do not replace it with mutable Boolean scratch state.
- Compact `Any`/`All`/`Where` predicates may nest two levels; use a block or a
  named stage at the third.
- Do not keep an `else` after a branch that returns or throws. Do not invert
  user-defined operators or floating-point comparisons without proving
  equivalence.
- Give debug assertions an explicit, meaningful message.
