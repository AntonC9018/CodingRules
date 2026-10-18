# 14: Ship agent rules for semantic reuse, messages, and builder review

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** implemented; final stable-package execution deferred

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/33

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

A consumer can obtain and explicitly import the package-provided agent guidance, and agents receive the judgments that analyzers cannot establish.

## Acceptance criteria

- [x] Deliver versioned guidance with the single consumer entry point and document/test an explicit import mechanism; a NuGet reference alone must not be described as making agents read it.
- [x] Cover semantic distinction in duplication, repeated argument plumbing, and builder configuration completeness including helper/overload coverage on successful execution paths.
- [x] Record structured message values rendered at reporting boundaries, structured accessible exception context, and lazy Message rendering as a considered design choice rather than a universal automatic rewrite.
- [x] Preserve accepted multiline literals, the concrete lexer/custom-handler case, and the constant-glob versus variable-extension local-function decision.
- [x] For the PDF/Word lesson flow, require its own complete record struct shared across delegate/handler/method; Excel keeps its existing aggregate as a precedent.
- [x] Keep universal interpolation rules, blanket ordinary-instance-method grouping, and RequiredStep analysis explicitly undecided/deferred. Do not add RequiredStep annotations or a staged-builder generator now.
- [x] A sample consuming repository demonstrates how the imported rules are found/read without overwriting unrelated instructions.

## Implementation

The package includes the self-contained canonical agent rules and import
instructions. Its `ExportCodingRulesAgentGuidance` target requires an explicit
destination and writes only `agent-coding-rules.md`; ordinary restore/build never
exports instructions. `samples/AgentGuidance/AGENTS.md` demonstrates an explicit
read directive alongside an existing application-specific rule. The
[consumer import contract](../../consumer-agent-guidance.md) records export,
version updates, discovery and preservation behavior.

No analyzer, RequiredStep annotations/generator, or downstream ScheduleLib
changes are introduced. No policy question blocked this implementation.

## Validation (2026-10-18)

- Focused `AgentGuidanceExportTests`: **5 passed**. They stage the declared package
  assets and invoke the real MSBuild target without packing/restoring a consumer.
  Coverage includes normal build, missing destination, paths with spaces,
  re-export, existing instructions and sample read-directive resolution.
- Broad solution: **247 analyzer + 1,052 code-fix tests passed**, zero failures.
  Focused and broad filters exclude Package, Packaged and Consumer names; the
  broad run explicitly excludes PackageConsumptionTests as well. Both use
  Release and `-p:WarningsNotAsErrors=NU1902%3BNU1903` for existing advisories.
- `git diff --check` and Python syntax validation of the new deferred gate passed.
- No pack or consumer gate ran. Final stable-package verification must run
  `build/verify-agent-guidance-consumer.py` with the shared candidate archive.
  The gate requires an existing package, isolates the sample feed/cache, checks
  archived versus exported bytes and preserves unrelated instructions. It does
  not prove any particular agent obeys the read directive. CI remains disabled.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
