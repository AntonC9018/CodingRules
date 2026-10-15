# 09: Prove named argument roles for target-typed return construction

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** Draft for breakdown approval; not published to GitHub Issues.

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

The A9 return-new case gets the same named-role behavior as ordinary construction through the packaged CR0500 rule.

## Acceptance criteria

- [ ] Reproduce the pinned constructor call and determine whether its apparent gap is detection, binding, configuration, or consumer delivery; extend the existing rule only if necessary.
- [ ] Name the bound repeated-type constructor roles without changing argument expressions, evaluation order, or overload selection.
- [ ] Keep existing explicit positional opt-outs and framework exceptions intact; test both ordinary and target-typed construction in a return expression.
- [ ] Treat extraction of the computed scope expression as the separate operation-rule concern; a green CR0500 test alone does not prove that refactor.
- [ ] Include a clean consumer regression so a rule already covered in unit tests is not mistaken for working package adoption.

- [ ] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Blocked by

None (can start immediately once the breakdown is approved).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
