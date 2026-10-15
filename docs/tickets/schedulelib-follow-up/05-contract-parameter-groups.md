# 05: Require argument groups on oversized locally owned contracts

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** ready-for-agent

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/24

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

Locally owned interface, virtual/abstract, delegate, and abstract-class-constructor contracts with more than three parameters receive a grouping finding (A5/Q17/Q18).

## Acceptance criteria

- [ ] Count context parameters including CancellationToken; distinguish the accepted threshold of four from the rejected earlier five-parameter proposal.
- [ ] Cover each accepted callable category and report at the owned contract without redundant findings on every implementation.
- [ ] Externally imposed signatures such as ISpanFormattable.TryFormat are exempt. Locally owned interfaces are not exempt merely because implementations must follow them.
- [ ] A single static function is allowed by this count rule. Ordinary concrete constructors remain outside the count rule; ordinary nonvirtual instance methods have no newly invented blanket rule.
- [ ] Use the reviewed static Finish/Edit shapes as count-rule boundary fixtures rather than blindly carrying forward the old A5 interpretation. Overload/builder and repeated-plumbing policies remain independent.
- [ ] Do not offer an arbitrary argument bag as an automatic fix for an architectural choice. Document manual findings and test positive/negative cases.

- [ ] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
