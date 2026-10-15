# ScheduleLib follow-up implementation ticket drafts

Status: draft breakdown awaiting the to-tickets review step. No new GitHub issues have been published.

The configured tracker is GitHub. Publish CodingRules diagnostics/package work in CodingRules, shared SDK dependency repair in SourceGenerators, and the concrete downstream changes in ScheduleLib. Use the skill's default ready-for-agent label on approved implementation tickets; create it if absent.

Reuse [CodingRules #19](https://github.com/AntonC9018/CodingRules/issues/19), which already describes the initializer false positive. Do not reopen implemented #1–#9 or recreate unrelated existing ScheduleLib #230 tasks. Do not change or close parent issues.

Each draft is a complete diagnostic/fix/consumer or downstream behavior slice. SDK repair and the consumer rollout are separate from introducing new rule policy. Draft 18 is the package adoption/coverage gate, not a blocker for independent rule development or the three downstream feature refactors.

| Draft | Repository | Delivers | Blocked by |
| --- | --- | --- | --- |
| [01: Repair the shared SDK testing dependency graph](01-repair-shared-testing-dependencies.md) | SourceGenerators | A fresh SDK-based CodingRules test consumer restores, builds, and runs its tests with normal audit and warnings-as-errors settings, without per-repository advisory exceptions. | None |
| [02: Deliver selected upstream formatting through Anton.CodingRules](02-reuse-upstream-formatting.md) | CodingRules | One consumer entry-point package supplies working diagnostics, fixes, and configuration for compact auto-properties, leading wrapped Boolean operators, and multiline-expression block bodies (A2/A3/A12). | None |
| [03: Close initializer and sole-constructor-argument wrapping gaps](03-initializer-call-wrapping.md) | CodingRules | A consumer can apply/check the exact reviewed A1 layout through the package-supported formatting pipeline, including cases existing formatter settings leave unchanged. | 02 |
| [04: Report tuple data that needs a named record struct](04-tuple-api-records.md) | CodingRules | The accepted tuple API/data cases receive a diagnostic directing the author to a named record struct, while local-function tuple results remain allowed (A4/Q15/Q16). | None |
| [05: Require argument groups on oversized locally owned contracts](05-contract-parameter-groups.md) | CodingRules | Locally owned interface, virtual/abstract, delegate, and abstract-class-constructor contracts with more than three parameters receive a grouping finding (A5/Q17/Q18). | None |
| [06: Diagnose multi-parameter overload families needing a builder](06-overloaded-configuration-builders.md) | CodingRules | Methods with more than one parameter and alternative overload forms for at least one parameter receive a builder-required finding under Q19, including static methods. | None |
| [07: Cover reviewed nested computations and named-step extraction](07-computed-arguments-and-lookups.md) | CodingRules | The non-message A6, A7, and A8 computations are found through existing or extended CR operation rules, with useful behavior-preserving refactorings where possible. | #19 (existing) |
| [08: Make reviewed compound guards and status filters explicit](08-compound-checks-and-status-filters.md) | CodingRules | The config-result, JSON lookup, cancellation/authentication, and numeric HTTP-status examples receive precise guard/helper or enum findings with safe fixes where possible (A3/A11/A15). | None |
| [09: Prove named argument roles for target-typed return construction](09-target-typed-named-roles.md) | CodingRules | The A9 return-new case gets the same named-role behavior as ordinary construction through the packaged CR0500 rule. | None |
| [10: Prefer interpolation for the reviewed string concatenations](10-interpolation-over-concatenation.md) | CodingRules | The desired-identifier and lock-file concatenations receive interpolation findings and behavior-preserving fixes (A10). | None |
| [11: Replace proven unchanged assignment branches with conditional updates](11-unchanged-assignment-branches.md) | CodingRules | Assignments with a branch that provably preserves the current value become explicit conditional updates, including expression-bodied property/accessor hosts (A13/Q13). | None |
| [12: Report repeated formatting candidates with semantic review](12-repeated-formatting-candidates.md) | CodingRules | The repeated nullable calendar timestamp formatting is identified as a duplication candidate with guidance to review shared meaning (A14/Q14). | None |
| [13: Report non-abstract classes that should be sealed](13-seal-concrete-classes.md) | CodingRules | Non-abstract classes are reviewed for sealing, including CalendarCommands even though its current tests subclass it (A16). | None |
| [14: Ship agent rules for semantic reuse, messages, and builder review](14-consumer-agent-guidance.md) | CodingRules | A consumer can obtain and explicitly import the package-provided agent guidance, and agents receive the judgments that analyzers cannot establish. | None |
| [15: Render calendar reports from typed message and action values](15-structured-calendar-reporting.md) | ScheduleLib | Calendar planning/reporting passes typed record-struct data to a reporting boundary and renders human text where needed, preserving machine output and operation semantics. | None |
| [16: Share one PDF/Word lesson-input record across delegate and method](16-shared-import-record.md) | ScheduleLib | PDF/Word import, ImportedLessonHandler, the cohort handler, and AddOrMergeLesson use one record struct for the repeated lesson/day/time/groups/alternative input. | None |
| [17: Configure serialization with a reusable validated builder](17-serializer-builder.md) | ScheduleLib | Serialization can configure input, stream/path output, and optional hash through a reusable builder, then execute asynchronously with cancellation. | None |
| [18: Adopt the package and replace formatter workarounds with proved coverage](18-adopt-package-formatting.md) | ScheduleLib | ScheduleLib consumes the resulting single package and runs a repeatable formatting/style-check workflow backed by exact A1–A16 coverage evidence. | 01–14; existing CodingRules #19 |

## Scope boundaries

- Broader inline interpolation and ordinary nonvirtual-instance parameter rules remain manual/open; do not create implementable diagnostics for undecided policy.
- Structured exception context is included in agent guidance; a universal lazy-Message analyzer is not approved.
- RequiredStep annotation/inferred-helper analysis stays recorded as a deferred idea, with no ready-for-agent implementation ticket.
- The import record is shared by the PDF/Word delegate/handler/method; Excel retains its own existing aggregate.
- Automatic fixes remain behavior preserving. Confident architectural findings can be manual; the original CR0001 contract remains intact.
- Cross-ticket links will become native blocking relationships on publication; draft IDs are stable for breakdown review.

The proposed breakdown must be approved before issue creation, as required by the invoked to-tickets skill.
