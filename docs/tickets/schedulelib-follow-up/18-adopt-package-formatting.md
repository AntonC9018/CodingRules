# 18: Adopt the package and replace formatter workarounds with proved coverage

**Target repository:** [ScheduleLib](https://github.com/AntonC9018/ScheduleLib)

**Status:** ready-for-agent

**Published issue:** https://github.com/AntonC9018/ScheduleLib/issues/244

## Implementation branch

**Base branch and PR target:** `feat/registry-history-analysis` in AntonC9018/ScheduleLib.
Create an implementation branch from its current tip and target the PR back to
that branch. Preserve any existing checkout changes. The verified review
snapshot is `4c26748e448fe7ccbdffc3f8f6c4b05ffddce144`; use the current branch tip
when work starts rather than assuming the repository default branch is correct.

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

- https://github.com/AntonC9018/CodingRules/issues/19
- https://github.com/AntonC9018/SourceGenerators/issues/12
- https://github.com/AntonC9018/CodingRules/issues/21
- https://github.com/AntonC9018/CodingRules/issues/22
- https://github.com/AntonC9018/CodingRules/issues/23
- https://github.com/AntonC9018/CodingRules/issues/24
- https://github.com/AntonC9018/CodingRules/issues/25
- https://github.com/AntonC9018/CodingRules/issues/26
- https://github.com/AntonC9018/CodingRules/issues/27
- https://github.com/AntonC9018/CodingRules/issues/28
- https://github.com/AntonC9018/CodingRules/issues/29
- https://github.com/AntonC9018/CodingRules/issues/30
- https://github.com/AntonC9018/CodingRules/issues/31
- https://github.com/AntonC9018/CodingRules/issues/32
- https://github.com/AntonC9018/CodingRules/issues/33

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
