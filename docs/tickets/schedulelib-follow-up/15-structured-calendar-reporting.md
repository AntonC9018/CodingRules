# 15: Render calendar reports from typed message and action values

**Target repository:** [ScheduleLib](https://github.com/AntonC9018/ScheduleLib)

**Status:** Draft for breakdown approval; not published to GitHub Issues.

## Parent

[ScheduleLib #230](https://github.com/AntonC9018/ScheduleLib/issues/230), specifically the structured calendar action/reporting work.

## What to build

Calendar planning/reporting passes typed record-struct data to a reporting boundary and renders human text where needed, preserving machine output and operation semantics.

## Acceptance criteria

- [ ] Cover the reviewed delete/create-event compound reports and planned-action data flow, coordinating with existing #230 B7 rather than re-creating unrelated CLI refactors.
- [ ] The reporting boundary can accept the structured message values generically; choose the smallest useful formatting contract. ISpanFormattable is a candidate, not a mandated universal interface.
- [ ] Do not satisfy the decision by merely extracting a completed interpolated string. Keep associated values available until the boundary chooses rendering, supporting future localization without requiring a localization system now.
- [ ] Preserve JSON/JSONL schemas, plan/apply behavior, partial outcomes, cancellation, identity/recurrence/culture semantics, and user-visible text unless an intentional change is documented.
- [ ] Check selected interpolation-handler overloads, gating, evaluation order, and mutable-data snapshots before moving construction/formatting; do not supply a purportedly safe automatic redesign without that proof.
- [ ] Add focused plan/reporting regressions. Generic exception-type redesign and a universal string-taking-API diagnostic are not part of this slice.

## Blocked by

None (can start immediately once the breakdown is approved).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
