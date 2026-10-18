# 11: Replace proven unchanged assignment branches with conditional updates

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** implemented; final stable-candidate package execution deferred

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/30

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

Assignments with a branch that provably preserves the current value become explicit conditional updates, including expression-bodied property/accessor hosts (A13/Q13).

## Acceptance criteria

- [x] Cover both the null-preserving reflection example and the generalized current-value ternary branch.
- [x] Only omit an assignment when the unchanged-value proof and target behavior permit it; do not drop property/indexer setter effects or repeat receiver/index evaluation.
- [x] Convert an expression-bodied property/accessor host to a block with the conditional update and necessary return, preserving public behavior.
- [x] Exercise both branch orientations, comments, mutable/effectful operands, and negative cases where equality or stability cannot be established.
- [x] Verify every offered code fix with compilation and relevant behavior checks.

- [x] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. These criteria describe implementation to do, not coverage already shipped.

## Implementation evidence

CR1200 and its explicit conditional update action implement the A13/Q13 proof
boundary. See the [assignment contract](../../unchanged-assignments.md) for exact
storage/host exclusions and saved-source validation. Focused tests compile all
offered actions and compare runtime behavior, including the shared pinned
reflection fixture, both generalized orientations, arrow property/get/set hosts,
condition/update mutation, exceptions, comments and user-defined return conversion.
The same positive/accepted fixture and exact saved source are registered in the
package tests. Packaged execution is intentionally deferred until one final stable
candidate after all tickets; no pack or consumer gate ran for this slice.

## Validation

Focused: 43 passed. Broad: 161 analyzer tests and 1,032 code-fix tests passed,
with zero failures. `%3B` passes the existing semicolon-separated NU1902/NU1903
warning overrides through MSBuild's command-line parser. The broad exclusion
also omits package/consumer-named fixture tests, beyond the heavyweight class.

```sh
dotnet test CodeFixTests/CodingRules.CodeFixes.Tests.csproj -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903 --filter 'FullyQualifiedName~UnchangedAssignmentTests&FullyQualifiedName!~PackageConsumptionTests&FullyQualifiedName!~Packaged&FullyQualifiedName!~Consumer' --no-restore
dotnet test CodingRules.slnx -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903 --filter 'FullyQualifiedName!~PackageConsumptionTests&FullyQualifiedName!~Packaged&FullyQualifiedName!~Package&FullyQualifiedName!~Consumer'
```
