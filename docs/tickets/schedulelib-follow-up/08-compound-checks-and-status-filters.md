# 08: Make reviewed compound guards and status filters explicit

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** implemented

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/27

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

The config-result, JSON lookup, cancellation/authentication, and numeric HTTP-status examples receive precise guard/helper or enum findings with safe fixes where possible (A3/A11/A15).

## Acceptance criteria

- [x] Exercise the actual success/verb/type-pattern and JSON/TryGetValue examples through existing CR condition families before adding new rules.
- [x] Cover complex cancellation/authentication filters with named local-function or explicit-control-flow actions where safe; moving || to another line does not satisfy this semantic refactor.
- [x] Prefer named HttpStatusCode members and an explanatory predicate in the reviewed numeric status set, preserving all matched statuses and exception-filter behavior.
- [x] Keep accepted same-value simple invariants, preserve short-circuiting, out-variable scope/assignment, pattern matching, and exception-filter timing.
- [x] Document mechanically recognizable boundaries and leave unresolved semantic cases for agent review. Include positive/negative fixtures and verify every offered fix.

- [x] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.

## Implementation evidence

The [compound-condition boundary](../../compound-conditions.md) records the
original CR02 probes, implemented generalization, safe actions and manual review
boundaries. CR0201/CR0202/CR0203 are reused; no new diagnostic is introduced.
`ReviewedCompoundAnalyzerTests` shares six positive and accepted fixtures with
`build/verify-condition-consumer.py`. Saved/reparsed runtime tests verify every
offered action, JSON out-variable assignments, pattern scope, short-circuiting,
all statuses from −1 through 600, filter exceptions and finally ordering.
