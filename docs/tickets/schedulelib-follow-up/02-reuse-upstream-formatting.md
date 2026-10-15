# 02: Deliver selected upstream formatting through Anton.CodingRules

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** ready-for-agent

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/21

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

One consumer entry-point package supplies working diagnostics, fixes, and configuration for compact auto-properties, leading wrapped Boolean operators, and multiline-expression block bodies (A2/A3/A12).

## Acceptance criteria

- [ ] Use the completed reuse research and choose the simplest reliable metapackage, binary delivery, or attributed source reuse. MIT and Apache-2.0 are accepted.
- [ ] A clean consumer referencing only Anton.CodingRules observes each selected diagnostic and can apply its exported code fix; ordinary builds diagnose and the documented fixer command applies changes.
- [ ] Verify compact get/init accessors, leading &&/|| on existing continuation lines, and multiline methods/local functions/properties/accessors converted to appropriate block bodies. Preserve accepted simple single-line bodies.
- [ ] Test comments, directives, host compatibility, formatter interaction, and a second idempotent pass. Whitespace formatting or setting names alone are not coverage evidence.
- [ ] Do not silently enable unrelated upstream rules or broader operator policies. Implement only actual remaining policy gaps and retain required license/NOTICE/credit and modified-source provenance.
- [ ] If reusable SDK packaging support is needed, implement and exercise that minimal support with the consumer; do not assume arbitrary analyzer references are automatically bundled.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
