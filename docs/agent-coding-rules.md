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

For the serializer configuration discussed in the
[ScheduleLib follow-up](schedulelib-follow-up.md#tuple-and-parameter-refinements-q15q18),
use a reusable builder supporting alternate output sources and an optional hash,
with cancellation supplied at execution. Review which configuration is required,
which has defaults, and what happens when required configuration is omitted.
Existing analyzer support is under investigation; do not claim that a builder
API by itself proves required methods were called. The general overload rule's
scope remains open in the follow-up, so do not turn every overload set into a
builder without checking that decision.
