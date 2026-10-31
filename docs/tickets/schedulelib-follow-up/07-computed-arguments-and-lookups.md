# 07: Cover reviewed nested computations and named-step extraction

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** implemented; final stable-candidate package/consumer execution deferred

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/26

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

The non-message A6, A7, and A8 computations are found through existing or extended CR operation rules, with useful behavior-preserving refactorings where possible.

## Acceptance criteria

- [x] Cover constructed exceptions passed to StreamingFailure, nested Any/ternary construction, directory selection, nested split/key/lookup construction, and complex Concat/ToArray ternary branches.
- [x] Cover reflection/service lookup chains including intermediate type calculations and the service result used by a condition; a known allowed service-call chain remains unreported.
- [x] Preserve direct initializers, simple factory values, and the accepted lexer ListStringBuilder interpolation. General interpolation and structured-message API redesign are not silently reduced to string-local extraction.
- [x] Where safe, offer named-local and named-local-function alternatives; preserve evaluation count/order, side effects, short-circuiting, exceptions, comments, and ref-like lifetimes.
- [x] Use handler-aware evaluation boundaries and retain the original CR0001 safe-fix contract. Confident new manual findings are allowed where no safe fix can be proved.
- [x] Verify analyzer behavior, fixed code, and representative runtime-sensitive cases; do not add a diagnostic merely because a new rule name sounds related.

- [x] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Validation and boundaries

[Computed-operation documentation](../../computed-operations.md) records exact
coverage, accepted cases, safety boundaries and the shared package consumer.
The initializer blocker was resolved by `7b9a4b2` (#19). The final candidate
package execution remains part of the combined end gate, not this slice.

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
