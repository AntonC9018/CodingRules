# 13: Report non-abstract classes that should be sealed

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** implemented; final stable-package execution deferred

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/32

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

Non-abstract classes are reviewed for sealing, including CalendarCommands even though its current tests subclass it (A16).

## Acceptance criteria

- [x] Reuse an upstream analyzer where it covers the intended policy and fill actual gaps rather than duplicating its implementation wholesale.
- [x] Honor the stated non-generic attribute-base exception where the generic wrapper forwards its type parameter; do not invent a general inheritance/test-seam exception.
- [x] The current inherited test seam may require a manual architectural finding. Do not silently skip the original example because a safe one-click seal fix is unavailable.
- [x] Only offer sealing when it preserves compilation and intended behavior; distinguish a diagnostic from choosing a replacement test seam.
- [x] Cover positive/negative examples and conservative boundaries, including abstract/static classes and actual external constraints.

- [x] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Implementation

CR1400 adapts MIT MA0053 source and reports manual sealing review, including the
original CalendarCommands virtual seam despite a derived test. The
[sealing contract](../../sealed-classes.md) records reuse/provenance, the
attribute forwarding proof, concrete external constraints, shared compiled
positive/accepted fixtures and warning/suppression/generated-code boundaries.
There is no automatic sealing action. The isolated consumer regression is
registered for final stable-package execution after all tickets; it has not
been executed during this ticket. CI remains disabled.

## Validation (2026-10-18)

- Focused sealing tests: **45 passed**. The resumed WIP initially passed 42/43;
  selecting a generated first partial declaration hid the hand-written finding.
  Declaration actions with symbol deduplication fix that case. Class/record
  declaration order and inherited external benchmark constraints are covered.
- Broad solution tests: **242 analyzer + 1,052 code-fix tests passed**, zero
  failures. Both runs exclude Package, Packaged and Consumer test names; the
  broad run also explicitly excludes PackageConsumptionTests.
- Both runs use Release and the command-scoped
  `-p:WarningsNotAsErrors=NU1902%3BNU1903` exception for existing transitive
  dependency advisories. No repository warning policy changed.
- No pack or consumer gate ran. The final stable candidate must still run
  `build/verify-sealed-classes-consumer.py` with the shared candidate archive.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
