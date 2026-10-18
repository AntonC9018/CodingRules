# 05: Require argument groups on oversized locally owned contracts

**Target repository:** [CodingRules](https://github.com/AntonC9018/CodingRules)

**Status:** implemented as CR1000; verified in analyzer tests and the isolated package consumer

**Published issue:** https://github.com/AntonC9018/CodingRules/issues/24

## Parent

[ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229). Reuse existing CodingRules #19 for initializer false positives; do not duplicate or alter the parent.

## What to build

Locally owned interface, virtual/abstract, delegate, and abstract-class-constructor contracts with more than three parameters receive a grouping finding (A5/Q17/Q18).

## Acceptance criteria

- [x] Count context parameters including CancellationToken; distinguish the accepted threshold of four from the rejected earlier five-parameter proposal.
- [x] Cover each accepted callable category and report at the owned contract without redundant findings on every implementation.
- [x] Externally imposed signatures such as ISpanFormattable.TryFormat are exempt. Locally owned interfaces are not exempt merely because implementations must follow them.
- [x] A single static function is allowed by this count rule. Ordinary concrete constructors remain outside the count rule; ordinary nonvirtual instance methods have no newly invented blanket rule.
- [x] Use the reviewed static Finish/Edit shapes as count-rule boundary fixtures rather than blindly carrying forward the old A5 interpretation. Overload/builder and repeated-plumbing policies remain independent.
- [x] Do not offer an arbitrary argument bag as an automatic fix for an architectural choice. Document manual findings and test positive/negative cases.

- [x] Follow the repository warning/configuration, generated-code, release-metadata, and diagnostic documentation conventions; include positive and accepted-case regression evidence in the packaged offering.

## Blocked by

None (can start immediately).

## Decision record

[Accepted discussion and research, PR #20](https://github.com/AntonC9018/CodingRules/pull/20). Later Q15–Q20 clarifications supersede older interpretations in the original acceptance issue. Implementation coverage is recorded below; NuGet publication remains separate.

## Implementation evidence

[CR1000 documentation](../../contract-parameters.md) records the exact count,
callable/ownership boundary, manual migration and configuration. Shared compiled
fixtures in `CodeFixTests/Consumers/ContractParameters` include positive owned
contracts and accepted static Finish/Edit, concrete constructor, ordinary
nonvirtual instance, implementation/override and external TryFormat cases.
The analyzer regression suite also covers generic/inherited interfaces,
external metadata, suppression and generated-code ownership.

`build/verify-contract-consumer.py` checks nine exact package findings, no extra
accepted/generated findings, unchanged source after a CLI fixing pass, and
warning/error/disabled configuration. CI runs it on the release candidate;
the shared manual-consumer harness retains the original six-finding CR0900 gate.

## Verification (2026-10-18)

All commands completed successfully in the current worktree:

```sh
dotnet test CodingRules.slnx -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903
dotnet test AnalyzerTests/CodingRules.Analyzers.Tests.csproj -c Release --filter FullyQualifiedName~ContractParameterAnalyzerTests -p:WarningsNotAsErrors=NU1902%3BNU1903
python3 -m unittest discover -s build/tests -v
dotnet pack Package/CodingRules.Package.csproj -c Release --output artifacts/contract-parameters
python3 build/verify-contract-consumer.py artifacts/contract-parameters
python3 build/verify-tuple-consumer.py artifacts/contract-parameters
git diff --check
```

The full suite passed 96 analyzer and 922 code-fix tests (1,018 total); the
focused suite passed all 32 CR1000 tests. Python passed all 15 checks. Pack
produced `Anton.CodingRules.0.1.0.nupkg`; the consumer gates passed nine CR1000
and six CR0900 exact findings with their accepted/generated and configuration
checks. The final focused rerun also verifies the fixture anchor parsing after
making it insensitive to CRLF line endings. Whitespace checks passed.

`%3B` is MSBuild's escaped semicolon. The existing transitive
System.Security.Cryptography.Pkcs 9.0.6 NU1902/NU1903 advisories remained visible
under the requested command-scoped warning exception; no repository-wide audit
suppression was introduced. No rule-policy decision remains blocked.
