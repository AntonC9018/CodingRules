# Configure serialization through a builder

For the ScheduleLib serializer discussed in Q17, use a reusable builder to
configure the operation: input schedule, output stream or path, and optional
hash defaulting to empty. Supply cancellation at ExecuteAsync. The exact
construction API, including state passed to a static configuration callback,
is an implementation choice.

Grouping only the schedule and hash into a record was rejected as an unhelpful
abstraction. The builder should accommodate alternate output forms and defaults,
rather than proliferating positional overloads. Repeated argument plumbing
elsewhere warrants a builder or cohesive context, with semantic review by agents.

Required input/output configuration must be checked manually by agents for now,
with appropriate runtime validation. Staged-generator alternatives do not match
the maintainer's intended API. The [RequiredStep analyzer experiment](../required-builder-steps-proposal.md)
is recorded for later, including inferred coverage through overloads and
higher-level helpers; no implementation is requested now.

The general builder rule applies when an operation has more than one parameter
and at least one parameter has alternative overload forms (Q19), including static
operations. Single-parameter overloads remain allowed. Ordinary instance-method
parameter grouping remains open in the [discussion record](../schedulelib-follow-up.md#overload-scope-and-usage-context-q19q20).
