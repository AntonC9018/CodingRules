# 11: Replace proven unchanged assignment branches with conditional updates

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** Draft for breakdown approval; not published to GitHub Issues.

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

Assignments with a branch that provably preserves the current value become explicit conditional updates, including expression-bodied property/accessor hosts (A13/Q13).

## Acceptance criteria

- [ ] Cover both the null-preserving reflection example and the generalized current-value ternary branch.
- [ ] Only omit an assignment when the unchanged-value proof and target behavior permit it; do not drop property/indexer setter effects or repeat receiver/index evaluation.
- [ ] Convert an expression-bodied property/accessor host to a block with the conditional update and necessary return, preserving public behavior.
- [ ] Exercise both branch orientations, comments, mutable/effectful operands, and negative cases where equality or stability cannot be established.
- [ ] Verify every offered code fix with compilation and relevant behavior checks.

- [ ] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Blocked by

None (can start immediately once the breakdown is approved).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
