# 03: Close initializer and sole-constructor-argument wrapping gaps

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** implemented and verified on 2026-10-16

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/22

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

A consumer can apply/check the exact reviewed A1 layout through the package-supported formatting pipeline, including cases existing formatter settings leave unchanged.

## Acceptance criteria

- [x] Use the pinned A1 cases: initializer members on separate lines and a sole targeted or explicit constructor argument adjacent to the opening call parenthesis.
- [x] Prove both expected layouts and accepted layouts, including already adjacent constructors, comments, nested initializers, and formatting stability on a second pass.
- [x] Reuse supported upstream formatting/configuration when it provides exact coverage; supply only missing behavior and expose a documented apply/check path.
- [x] Formatting preserves syntax and meaning and does not turn accepted initializer values into named-step extraction requirements.

## Delivered coverage

CR0801 expands multi-member object initializers; CR0802 joins the sole positional
explicit/target-typed constructor argument when its opening gap is whitespace.
The standard formatter supplies indentation and expanded-initializer formatting.
Tests demonstrate that formatter configuration and Roslynator RCS0053 leave the
pinned gaps, so only those missing behaviors are implemented here. No additional
upstream diagnostic is enabled and no third-party source or binary is modified.

[`InitializerCallFormattingTests.cs`](../../../CodeFixTests/InitializerCallFormattingTests.cs)
covers reduced cases from all four pinned sites at ScheduleLib
`12e601e7b62511bdecc44e280407fee12147b230`: `AppTasks.cs:53/73/127` and
`CliSettings.Removals.cs:269`. It checks actual diagnostic/fix output, accepted
layouts, comments, nested initializers, CRLF/tabs, string token preservation,
runtime order, compilation, unchanged operation findings and second-pass
stability. Exported Fix All tests also reproduce and prevent the SDK CLI failure
when a provider's action key differs between its diagnostic IDs.

The existing slice 02
[`verify-formatting-consumer.py`](../../../build/verify-formatting-consumer.py)
gate now verifies CR0801/CR0802 through the packed package, exact expected source,
accepted constructor/comment layouts, unchanged runtime output, warnings-as-errors
compilation and a byte-identical second pass. It also exercises the documented
A1-only `--verify-no-changes` check. See [packaged formatting](../../formatting.md)
for apply/check commands, validation results, scope and provenance.

Existing initializer operation false positives remain tracked in #19. A1 does
not add extraction requirements or change those diagnostics. Single-member
initializers remain accepted; named/ref/multiple arguments, non-constructor
values and constructor argument lists retain their existing policies. Comments
in the opening argument gap, directives, invalid syntax and generated code are
conservative skips. No new rule-policy decision was needed.

## Blocked by

- https://github.com/AntonC9018/CodingRules/issues/21

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. The coverage above verifies this ticket's implementation; publishing and downstream adoption remain separate.
