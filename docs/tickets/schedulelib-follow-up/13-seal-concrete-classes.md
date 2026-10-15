# 13: Report non-abstract classes that should be sealed

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** ready-for-agent

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/32

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

Non-abstract classes are reviewed for sealing, including CalendarCommands even though its current tests subclass it (A16).

## Acceptance criteria

- [ ] Reuse an upstream analyzer where it covers the intended policy and fill actual gaps rather than duplicating its implementation wholesale.
- [ ] Honor the stated non-generic attribute-base exception where the generic wrapper forwards its type parameter; do not invent a general inheritance/test-seam exception.
- [ ] The current inherited test seam may require a manual architectural finding. Do not silently skip the original example because a safe one-click seal fix is unavailable.
- [ ] Only offer sealing when it preserves compilation and intended behavior; distinguish a diagnostic from choosing a replacement test seam.
- [ ] Cover positive/negative examples and conservative boundaries, including abstract/static classes and actual external constraints.

- [ ] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
