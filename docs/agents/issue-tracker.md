# Issue tracker

CodingRules tickets live in [GitHub Issues](https://github.com/AntonC9018/CodingRules/issues).
Use `gh issue view NUMBER --repo AntonC9018/CodingRules` to read a ticket.
The accepted rule definitions and first-ticket acceptance cases are in
[`docs/spec.md`](../spec.md). Issues #1–#9 cover the original implemented rule
families and assessments. Their completion evidence is recorded in GitHub;
manual boundaries remain documented in the spec and family-specific notes.

The initializer false positive from
[#19](https://github.com/AntonC9018/CodingRules/issues/19) is fixed in source, with
regression coverage for accepted assignments and combined right-hand sides.
Additional acceptance cases come from
[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229);
the [follow-up decisions](../schedulelib-follow-up.md) record its scope and
maintainer clarifications. Check live issue state before starting work.

## ScheduleLib follow-up tickets

The [implementation ticket index](../tickets/schedulelib-follow-up/README.md)
links the 18 approved and published slices with explicit blocking edges across
CodingRules, SourceGenerators, and ScheduleLib. Reuse existing initializer issue
#19. Consult the live GitHub dependency state before starting implementation.

**ScheduleLib base branch and PR target: `feat/registry-history-analysis`.**
This branch is specified in every published ScheduleLib implementation ticket.
Create feature branches from its current tip and target their PRs back to it.

Published implementation issues have the ready-for-agent label. Work only when
their blockers are complete. Unresolved policy and the deferred RequiredStep
experiment have no implementation tickets.
