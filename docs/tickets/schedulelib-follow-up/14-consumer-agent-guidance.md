# 14: Ship agent rules for semantic reuse, messages, and builder review

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** Draft for breakdown approval; not published to GitHub Issues.

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

A consumer can obtain and explicitly import the package-provided agent guidance, and agents receive the judgments that analyzers cannot establish.

## Acceptance criteria

- [ ] Deliver versioned guidance with the single consumer entry point and document/test an explicit import mechanism; a NuGet reference alone must not be described as making agents read it.
- [ ] Cover semantic distinction in duplication, repeated argument plumbing, and builder configuration completeness including helper/overload coverage on successful execution paths.
- [ ] Record structured message values rendered at reporting boundaries, structured accessible exception context, and lazy Message rendering as a considered design choice rather than a universal automatic rewrite.
- [ ] Preserve accepted multiline literals, the concrete lexer/custom-handler case, and the constant-glob versus variable-extension local-function decision.
- [ ] For the PDF/Word lesson flow, require its own complete record struct shared across delegate/handler/method; Excel keeps its existing aggregate as a precedent.
- [ ] Keep universal interpolation rules, blanket ordinary-instance-method grouping, and RequiredStep analysis explicitly undecided/deferred. Do not add RequiredStep annotations or a staged-builder generator now.
- [ ] A sample consuming repository demonstrates how the imported rules are found/read without overwriting unrelated instructions.

## Blocked by

None (can start immediately once the breakdown is approved).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
