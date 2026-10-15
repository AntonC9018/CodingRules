# CodingRules guidance for agents

This file complements analyzer diagnostics with judgments that syntax analysis
cannot establish. It is source guidance for inclusion in the CodingRules
consumer offering; a package reference alone does not make an agent read it.

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
[Excel scan](excel-import-input-research.md) is a precedent for the pattern,
not a requirement to unify these import contracts. Infer the sharing boundary
from values repeatedly forwarded together. Partial arbitrary groupings do not
satisfy this concrete decision.

For the serializer configuration discussed in the
[ScheduleLib follow-up](schedulelib-follow-up.md#tuple-and-parameter-refinements-q15q18),
use a reusable builder supporting alternate output sources and an optional hash,
with cancellation supplied at execution. Review which configuration is required,
which has defaults, and what happens when required configuration is omitted.
Use a builder when an operation has more than one parameter and at least one
parameter has alternative overload forms. One-parameter overloads remain allowed.
This rule also applies to static operations; it is independent of the separate
parameter-count requirement for extensible contracts.

## Check builder completeness manually

The [RequiredStep analyzer idea](required-builder-steps-proposal.md) is deferred.
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
