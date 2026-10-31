# 12: Report repeated formatting candidates with semantic review

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** implemented; final stable-candidate package execution deferred

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/31

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

The repeated nullable calendar timestamp formatting is identified as a duplication candidate with guidance to review shared meaning (A14/Q14).

## Acceptance criteria

- [x] Start with matching operations in one method with the same format/provider and operation shape, possibly different receivers, including the exact start/end timestamp example.
- [x] Treat the result as a candidate for review: identical syntax may represent intentionally distinct domain concepts and does not mandate extraction.
- [x] Link the semantic-review agent guidance; preserve narrowly justified separate computations/suppressions rather than mechanically merging every match.
- [x] A helper fix is optional and only offered when receiver evaluation, branch timing, null behavior, formatting, culture, and exceptions are preserved. No automatic fix is offered.
- [x] Test matching/mismatching format/provider cases, accepted semantic distinctions in guidance, and any offered fixes.

- [x] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.

## Implementation evidence

[CR1300 recognition and semantic-review contract](../../repeated-formatting.md)
records the exact initial boundary and manual-only behavior. Analyzer tests share
the positive/accepted consumer sources, including the pinned A14 chains and
independent policies. `build/verify-repeated-formatting-consumer.py` supplies the
isolated package regression for the final stable archive; it is intentionally
not executed during ticket development. CI remains disabled.

## Validation

Focused: 36 passed. Broad: 197 analyzer tests and 1,052 code-fix tests passed,
with zero failures. `%3B` transports the existing NU1902/NU1903 warning overrides
through MSBuild's property parser. All Package/Packaged/Consumer-named tests are
excluded. The new Python gate passed a syntax-only parse; no pack or consumer
gate was executed.

```sh
dotnet test AnalyzerTests/CodingRules.Analyzers.Tests.csproj -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903 --filter 'FullyQualifiedName~RepeatedFormattingAnalyzerTests&FullyQualifiedName!~Package&FullyQualifiedName!~Packaged&FullyQualifiedName!~Consumer'
dotnet test CodingRules.slnx -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903 --filter 'FullyQualifiedName!~PackageConsumptionTests&FullyQualifiedName!~Package&FullyQualifiedName!~Packaged&FullyQualifiedName!~Consumer'
```
