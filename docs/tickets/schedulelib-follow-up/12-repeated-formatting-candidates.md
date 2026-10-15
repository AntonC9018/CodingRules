# 12: Report repeated formatting candidates with semantic review

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** Draft for breakdown approval; not published to GitHub Issues.

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

The repeated nullable calendar timestamp formatting is identified as a duplication candidate with guidance to review shared meaning (A14/Q14).

## Acceptance criteria

- [ ] Start with matching operations in one method with the same format/provider and operation shape, possibly different receivers, including the exact start/end timestamp example.
- [ ] Treat the result as a candidate for review: identical syntax may represent intentionally distinct domain concepts and does not mandate extraction.
- [ ] Link the semantic-review agent guidance; preserve narrowly justified separate computations/suppressions rather than mechanically merging every match.
- [ ] A helper fix is optional and only offered when receiver evaluation, branch timing, null behavior, formatting, culture, and exceptions are preserved.
- [ ] Test matching/mismatching format/provider cases, accepted semantic distinctions in guidance, and any offered fixes.

- [ ] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Blocked by

None (can start immediately once the breakdown is approved).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
