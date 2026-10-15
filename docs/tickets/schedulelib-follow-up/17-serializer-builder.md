# 17: Configure serialization with a reusable validated builder

**Target repository:** [ScheduleLib](https://github.com/AntonC9018/ScheduleLib)

**Status:** ready-for-agent

**Published issue:** https://github.com/AntonC9018/ScheduleLib/issues/243

## Implementation branch

**Base branch and PR target:** `feat/registry-history-analysis` in AntonC9018/ScheduleLib.
Create an implementation branch from its current tip and target the PR back to
that branch. Preserve any existing checkout changes. The verified review
snapshot is `4c26748e448fe7ccbdffc3f8f6c4b05ffddce144`; use the current branch tip
when work starts rather than assuming the repository default branch is correct.

## What to build

Serialization can configure input, stream/path output, and optional hash through a reusable builder, then execute asynchronously with cancellation.

## Acceptance criteria

- [ ] Input and Output are required; Hash defaults to empty when omitted. Provide the accepted alternative output forms without proliferating positional configuration overloads.
- [ ] Choose the exact builder API, including a state/static-callback form if suitable; do not merely group schedule and hash into the rejected partial input record.
- [ ] Agents manually trace required configuration through overloads/helpers. Keep runtime checks and document repeated/conflicting-setter behavior; do not implement RequiredStep analysis or adopt staged generators.
- [ ] Preserve ownership/disposal of caller streams versus builder-opened files, cancellation, cache-validity hash meaning, serialized schema, and existing atomic-write/error behavior.
- [ ] Migrate serializer callers and add focused tests for both output forms, omitted hash, missing required configuration, cancellation, and round-trip/cache behavior.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
