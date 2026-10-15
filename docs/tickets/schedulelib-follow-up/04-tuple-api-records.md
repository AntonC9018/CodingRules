# 04: Report tuple data that needs a named record struct

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** Draft for breakdown approval; not published to GitHub Issues.

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

The accepted tuple API/data cases receive a diagnostic directing the author to a named record struct, while local-function tuple results remain allowed (A4/Q15/Q16).

## Acceptance criteria

- [ ] Cover the calendar plan collection/API flow, course-name mapping table, private qualification-parser return, and ordinary filtered-item method result.
- [ ] Keep the nonempty-cell local iterator tuple result unreported. Private ordinary methods do not inherit that exception, and deconstruction syntax is not itself a violation.
- [ ] Do not infer a universal ban on purely local tuple temporaries/swaps or an exemption for every tuple used somewhere by a local function; constrain detection to settled cases and a documented conservative generalization.
- [ ] Confident architectural findings may be manual findings. Offer a fix only when naming, API/caller changes, semantics, and compilation can be preserved; verify every offered fix.

- [ ] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Blocked by

None (can start immediately once the breakdown is approved).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
