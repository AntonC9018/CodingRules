# 03: Close initializer and sole-constructor-argument wrapping gaps

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** Draft for breakdown approval; not published to GitHub Issues.

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

A consumer can apply/check the exact reviewed A1 layout through the package-supported formatting pipeline, including cases existing formatter settings leave unchanged.

## Acceptance criteria

- [ ] Use the pinned A1 cases: initializer members on separate lines and a sole targeted or explicit constructor argument adjacent to the opening call parenthesis.
- [ ] Prove both expected layouts and accepted layouts, including already adjacent constructors, comments, nested initializers, and formatting stability on a second pass.
- [ ] Reuse supported upstream formatting/configuration when it provides exact coverage; supply only missing behavior and expose a documented apply/check path.
- [ ] Formatting preserves syntax and meaning and does not turn accepted initializer values into named-step extraction requirements.

## Blocked by

- Draft 02: Deliver selected upstream formatting through Anton.CodingRules.

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
