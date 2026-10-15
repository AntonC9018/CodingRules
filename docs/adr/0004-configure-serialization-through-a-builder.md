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

Required input/output configuration must be checked. Investigate existing
analyzers and compiler-enforced builder designs before adding another analyzer;
do not claim an unchecked fluent API proves required calls occurred. No checking
mechanism has been selected yet. The scope of the general overload rule remains
open in the [discussion record](../schedulelib-follow-up.md#next-frontier).
