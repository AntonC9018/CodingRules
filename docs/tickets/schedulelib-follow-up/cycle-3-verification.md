# Review Cycle 3 verification — 2026-10-18

Cycle 3 is closed for tickets 05, 06 and 08: the three accepted review findings
are fixed and the required focused and broad checks passed. No additional
implementation changes or maintainer decisions were needed in this completion
pass. Package validation remains deferred until all tickets are complete.

Verified source: `0f5c2da82faf31cec8f0e47972ecc292a59b89e3` on
`feat/schedulelib-follow-up`, using .NET SDK 10.0.112. The preceding CRLF fixture
anchor fix remains covered by the LF/CRLF compound-condition analyzer tests.

## Accepted fixes and regression evidence

- `ebda226` — CR1000 skips unresolved types recursively through containing
  generic types and function-pointer parameter/return types.
  `ContractParameterAnalyzerTests.SkipsInvalidSignatures` covers the unresolved
  cases; `CountsDeclaredSlotsAtFourWithinAcceptedCallableCategories` supplies
  resolved controls.
- `9296116` — CR1001 ignores tuple element names recursively while comparing
  overload parameter types. The 15 cases in
  `OverloadConfigurationAnalyzerTests.ComparesTupleTypesWithoutElementNamesRecursively`
  cover tuple/ValueTuple spelling, nesting, generic arguments, containing types,
  arrays, pointers and function pointers, including negative signature controls.
- `49f3dcb` — terminal-guard expansion declines changes that could alter later
  implicit caller-line defaults. Both cases in
  `ReviewedCompoundRuntimeTests.JsonGuardDeclinesExpansionThatMovesLaterCallerLineNumbers`
  verify same-member and later-member calls, including saved/reparsed runtime
  behavior. The shared accepted consumer fixture also stays unreported.

## Commands and results

These commands ran sequentially, with builds enabled so the checks used the
current source:

```sh
dotnet test AnalyzerTests/CodingRules.Analyzers.Tests.csproj -c Release \
  -p:WarningsNotAsErrors=NU1902%3BNU1903 \
  --filter 'FullyQualifiedName~ContractParameterAnalyzerTests|FullyQualifiedName~OverloadConfigurationAnalyzerTests' \
  --logger 'trx;LogFileName=cycle3-analyzers.trx' \
  --results-directory /tmp/codingrules-cycle3-verification
# Passed: 38 CR1000 + 57 CR1001 tests; 0 failed or skipped.

dotnet test CodingRules.slnx -c Release \
  -p:WarningsNotAsErrors=NU1902%3BNU1903 \
  --filter 'FullyQualifiedName~ReviewedCompound' \
  --logger 'trx;LogFilePrefix=cycle3-compound' \
  --results-directory /tmp/codingrules-cycle3-verification
# Passed: 4 analyzer + 30 code-fix/runtime tests; 0 failed or skipped.

dotnet test CodingRules.slnx -c Release \
  -p:WarningsNotAsErrors=NU1902%3BNU1903 \
  --filter 'FullyQualifiedName!~PackageConsumptionTests' \
  --logger 'trx;LogFilePrefix=cycle3-broad' \
  --results-directory /tmp/codingrules-cycle3-verification
# Passed: 163 analyzer + 946 code-fix tests (1,109 total); 0 failed or skipped.

git diff --check
# Passed.
```

The existing NU1902/NU1903 advisories for transitive
`System.Security.Cryptography.Pkcs` 9.0.6 remained visible under the documented
command-scoped warning overrides. No audit configuration changed.

No `dotnet pack`, `PackageConsumptionTests`, or `build/verify-*-consumer.py`
gate ran in this pass, following AGENTS.md and the test-latency report. The
package-fixture and gate changes in the accepted fixes still require the single
final candidate run after all tickets are done. PR #34 remains open and draft;
CI remains disabled. Ticket 09 was not started.
