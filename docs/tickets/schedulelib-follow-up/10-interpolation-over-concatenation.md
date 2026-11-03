# 10: Prefer interpolation for the reviewed string concatenations

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** ready-for-agent

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/29

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

The desired-identifier and lock-file concatenations receive interpolation findings and behavior-preserving fixes (A10).

## Acceptance criteria

- [ ] Reuse a suitable existing analyzer/fixer before adding a custom rule; prove both original A10 examples through a consumer.
- [ ] Retain explicit invariant identifier conversion and test under a non-invariant current culture.
- [ ] Preserve operand evaluation, null handling, formatting, and overload selection; do not rewrite numeric addition or unsupported overloaded/dynamic operators as if they were plain string concatenation.
- [ ] Keep this rule distinct from the unresolved universal inline-string/message-record policy, and do not move work outside a gated interpolation handler.

- [ ] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
