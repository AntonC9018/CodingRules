# 16: Share one PDF/Word lesson-input record across delegate and method

**Target repository:** [ScheduleLib](https://github.com/AntonC9018/ScheduleLib)

**Status:** Draft for breakdown approval; not published to GitHub Issues.

## What to build

PDF/Word import, ImportedLessonHandler, the cohort handler, and AddOrMergeLesson use one record struct for the repeated lesson/day/time/groups/alternative input.

## Acceptance criteria

- [ ] Group all five payload values in the shared record struct; the delegate and method use the same type rather than independently grouping subsets.
- [ ] Preserve default and explicit alternatives, per-group cohort expansion, course routing, group/time overrides, merge behavior, and error propagation.
- [ ] Keep Excel AddLessonParams as a separate aggregate. It is an existing pattern to learn from, not a requirement to unify Excel date/ref/span semantics with this record.
- [ ] Migrate the relevant callers together and validate the existing merge/time-override/different-cohort cases plus the optional callback path. The change is small enough to land as one green refactor without a compatibility facade.
- [ ] Preserve structured values instead of converting them to text early. Record that the reviewed source is Astra-generated rather than assuming it is a human-style precedent.

## Blocked by

None (can start immediately once the breakdown is approved).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
