# Issue tracker

CodingRules tickets live in [GitHub Issues](https://github.com/AntonC9018/CodingRules/issues).
Use `gh issue view NUMBER --repo AntonC9018/CodingRules` to read a ticket.
The accepted rule definitions and first-ticket acceptance cases are in
[`docs/spec.md`](../spec.md). Issues #1–#9 cover the original implemented rule
families and assessments. Their completion evidence is recorded in GitHub;
manual boundaries remain documented in the spec and family-specific notes.

The current initializer false positive is tracked in
[#19](https://github.com/AntonC9018/CodingRules/issues/19). Additional acceptance
cases come from [ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229);
the [follow-up decisions](../schedulelib-follow-up.md) record its scope and
maintainer clarifications. Check live issue state before starting work.

## ScheduleLib follow-up drafts

The [implementation ticket drafts](../tickets/schedulelib-follow-up/README.md)
turn the accepted discussion decisions into individual slices with explicit
blocking edges. They are awaiting breakdown approval under the invoked
to-tickets workflow; no new GitHub issues have been published yet. Reuse the
existing initializer issue #19. The index identifies the appropriate target
repository for SDK and downstream implementation slices.

Approved implementation issues use the to-tickets default ready-for-agent
label. Create that label on the target repository if absent. Unresolved policy
and the deferred RequiredStep experiment do not receive that label.
