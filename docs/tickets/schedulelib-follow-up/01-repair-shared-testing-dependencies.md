# 01: Repair the shared SDK testing dependency graph

**Target repository:** [SourceGenerators](https://github.com/AntonC9018/SourceGenerators)

**Status:** ready-for-agent

**Published issue:** https://github.com/AntonC9018/SourceGenerators/issues/12

## What to build

A fresh SDK-based CodingRules test consumer restores, builds, and runs its tests with normal audit and warnings-as-errors settings, without per-repository advisory exceptions.

## Acceptance criteria

- [ ] Repair the vulnerable transitive testing graph at the shared SDK/helper source rather than permanently suppressing audit warnings in consumers.
- [ ] Exercise the SDK analyzer-test and code-fix-test roles and the shared Roslyn testing helper through a fresh consumer.
- [ ] The CodingRules suite and packaged consumer checks pass using the repaired shared dependencies; distinguish normal restore success from the earlier command-scoped baseline exception.
- [ ] Document compatible dependency versions and the adoption path. Do not conflate this repair with the existing optional-reference opt-out request in SourceGenerators #9.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.
