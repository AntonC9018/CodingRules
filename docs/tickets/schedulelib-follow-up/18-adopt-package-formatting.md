# 18: Adopt the package and replace formatter workarounds with proved coverage

**Target repository:** [ScheduleLib](https://github.com/AntonC9018/ScheduleLib)

**Status:** Draft for breakdown approval; not published to GitHub Issues.

## What to build

ScheduleLib consumes the resulting single package and runs a repeatable formatting/style-check workflow backed by exact A1–A16 coverage evidence.

## Acceptance criteria

- [ ] Use freshly packed local artifacts or the normal reviewed release path; verify SDK/upstream analyzer dependencies, fixes, configuration, and imported agent guidance actually reach the consumer.
- [ ] Replace the fake project/solution CleanupCode workaround where the new supported pipeline proves equivalent braces, commas, statement layout, wrapping, and block-body behavior. Keep any still-needed tool until the replacement is demonstrated.
- [ ] Retain scoped apply/check, source/comment preservation, concurrent-edit protection, and a stable second pass; do not silently apply unrelated semantic refactors in a whitespace-formatting command.
- [ ] Maintain an A1–A16 coverage ledger for original cases and accepted cases, distinguishing diagnostic, fix, configuration, manual review, and unresolved policy. Do not mark open policy complete merely to obtain a clean run.
- [ ] Regression-check accepted service chaining, direct/simple factory initializers, local-function tuple results, external contracts, and the lexer handler case without disabling whole rule families.
- [ ] Record relevant tests/build/formatter results and existing unrelated baseline failures separately. New feature implementation, consumer adoption, and package publication have separate completion evidence.

## Blocked by

- Draft 01: Repair the shared SDK testing dependency graph.
- Draft 02: Deliver selected upstream formatting through Anton.CodingRules.
- Draft 03: Close initializer and sole-constructor-argument wrapping gaps.
- Draft 04: Report tuple data that needs a named record struct.
- Draft 05: Require argument groups on oversized locally owned contracts.
- Draft 06: Diagnose multi-parameter overload families needing a builder.
- Draft 07: Cover reviewed nested computations and named-step extraction.
- Draft 08: Make reviewed compound guards and status filters explicit.
- Draft 09: Prove named argument roles for target-typed return construction.
- Draft 10: Prefer interpolation for the reviewed string concatenations.
- Draft 11: Replace proven unchanged assignment branches with conditional updates.
- Draft 12: Report repeated formatting candidates with semantic review.
- Draft 13: Report non-abstract classes that should be sealed.
- Draft 14: Ship agent rules for semantic reuse, messages, and builder review.
- [Existing CodingRules #19](https://github.com/AntonC9018/CodingRules/issues/19): ordinary initializer values remain accepted.

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
