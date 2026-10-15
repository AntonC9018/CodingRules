# 08: Make reviewed compound guards and status filters explicit

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** ready-for-agent

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/27

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

The config-result, JSON lookup, cancellation/authentication, and numeric HTTP-status examples receive precise guard/helper or enum findings with safe fixes where possible (A3/A11/A15).

## Acceptance criteria

- [ ] Exercise the actual success/verb/type-pattern and JSON/TryGetValue examples through existing CR condition families before adding new rules.
- [ ] Cover complex cancellation/authentication filters with named local-function or explicit-control-flow actions where safe; moving || to another line does not satisfy this semantic refactor.
- [ ] Prefer named HttpStatusCode members and an explanatory predicate in the reviewed numeric status set, preserving all matched statuses and exception-filter behavior.
- [ ] Keep accepted same-value simple invariants, preserve short-circuiting, out-variable scope/assignment, pattern matching, and exception-filter timing.
- [ ] Document mechanically recognizable boundaries and leave unresolved semantic cases for agent review. Include positive/negative fixtures and verify every offered fix.

- [ ] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
