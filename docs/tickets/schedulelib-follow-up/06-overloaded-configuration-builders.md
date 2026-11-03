# 06: Diagnose multi-parameter overload families needing a builder

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** implemented (CR1001; publication and downstream migration are separate)

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/25

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

Methods with more than one parameter and alternative overload forms for at least one parameter receive a builder-required finding under Q19, including static methods.

## Acceptance criteria

- [x] A multi-parameter operation varying its output form is reported; one-parameter Parse string/span alternatives and one-parameter builder Output overloads remain allowed.
- [x] The trigger is more than one parameter, not more than three; do not treat the static count-rule exemption as an overload-rule exemption.
- [x] Resolve symbols and document a conservative supported definition of an overload alternative; do not confuse same names on unrelated types or unresolved/dynamic calls with an overload family.
- [x] The diagnostic explains the configuration abstraction required without automatically rewriting public APIs or claiming the deferred RequiredStep analyzer exists.
- [x] Keep required-step checks in manual agent guidance and runtime validation. Broader repeated plumbing is reviewed by agents, not guessed from overload count alone.

- [x] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Implementation evidence

[CR1001 documentation](../../overload-configuration.md) records the approved
same-type/name/staticness/generic-arity, equal-count (>1), exactly-one-type-difference
boundary, with matching ref/params/optional/default presence and unconstrained
parameter names. Analyzer tests share compiled positive and accepted sources with
`build/verify-overload-consumer.py`; CI verifies the exact release candidate using
the existing manual-consumer harness. API migration is a documented manual finding.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20).
Later Q15–Q20 clarifications and the
[2026-10-18 Q19 boundary approval](../../schedulelib-follow-up.md#overload-scope-and-usage-context-q19q20)
supersede older interpretations in the original acceptance issue.
