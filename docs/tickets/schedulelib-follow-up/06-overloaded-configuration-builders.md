# 06: Diagnose multi-parameter overload families needing a builder

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** Draft for breakdown approval; not published to GitHub Issues.

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

Methods with more than one parameter and alternative overload forms for at least one parameter receive a builder-required finding under Q19, including static methods.

## Acceptance criteria

- [ ] A multi-parameter operation varying its output form is reported; one-parameter Parse string/span alternatives and one-parameter builder Output overloads remain allowed.
- [ ] The trigger is more than one parameter, not more than three; do not treat the static count-rule exemption as an overload-rule exemption.
- [ ] Resolve symbols and document a conservative supported definition of an overload alternative; do not confuse same names on unrelated types or unresolved/dynamic calls with an overload family.
- [ ] The diagnostic explains the configuration abstraction required without automatically rewriting public APIs or claiming the deferred RequiredStep analyzer exists.
- [ ] Keep required-step checks in manual agent guidance and runtime validation. Broader repeated plumbing is reviewed by agents, not guessed from overload count alone.

- [ ] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Blocked by

None (can start immediately once the breakdown is approved).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
