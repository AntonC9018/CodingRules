# Analyzer dependencies and SDK migration

The first CodingRules implementation used direct package references. The
dependency pass compared that working graph with the SourceGeneration SDK.
Microsoft's [Roslyn analyzer tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix)
uses separate analyzer, code fix, package, and test projects and recommends
`netstandard2.0` for the analyzer host. NuGet's
[analyzer package convention](https://learn.microsoft.com/en-us/nuget/guides/analyzers-conventions)
places C# analyzer DLLs under `analyzers/dotnet/cs`.

| Role | Direct dependency needed by CodingRules | SDK status |
| --- | --- | --- |
| Analyzer | `Microsoft.CodeAnalysis.CSharp` 4.10.0 | Already supplied |
| Analyzer | `Microsoft.CodeAnalysis.Analyzers` 3.3.4 for analyzer rules and release tracking | Already supplied |
| Code fix | `Microsoft.CodeAnalysis.CSharp.Workspaces` 4.10.0 for `Document`, actions, and syntax formatting | Already supplied |
| Code fix | `System.Composition.AttributedModel` 8.0.0 for `[Shared]` | Added explicitly to the CodeFixes role |
| Tests | xUnit, test SDK, analyzer/code fix testing packages, and modern Roslyn packages | Already supplied by the matching test roles |
| Tests | `Anton.SourceGeneration.RoslynTesting` for typed diagnostic markers and fluent test builders | Added to the analyzer and code fix test roles |
| Package test | `Anton.SourceGeneration.PackageTesting` 1.0.0 | Explicit reference from the code-fix tests |

The direct-reference test projects initially restored Roslyn 1.0.1 transitively
through the testing packages and failed with `NU1701` on `net10.0`. Adding
direct Roslyn 4.10.0 references fixed that; the SDK already has them. The
working code fix project also had an explicit `System.Composition.AttributedModel`
reference. The SDK used to obtain it transitively from Workspaces, so this pass
adds it as an intentional CodeFixes dependency at the matching 8.0.0 version.

The SDK already targets `netstandard2.0` for production roles and packages the
analyzer and code fix assemblies under `analyzers/dotnet/cs`. CodingRules uses
the same project-name roles and a local feed built by
[`build/prepare-local-sdk.sh`](../build/prepare-local-sdk.sh). Its `NuGet.Config`
keeps restored packages in this checkout, and the prepare script refreshes
cached SDK and helper packages after rebuilding the feed. The package
consumption test restores `Anton.CodingRules` in a fresh consumer, reports
`CR0001`, applies its code fix, and then builds without warnings.

## Validation on 2026-10-15

Create the optional `artifacts/local-feed` directory before restoring a fresh
checkout, as shown in the README. NuGet treats a configured but missing local
source as a restore error even when the needed SDK packages are published.

The original test dependency graph currently resolves
`System.Security.Cryptography.Pkcs` 9.0.6 through
`Microsoft.CodeAnalysis.Analyzer.Testing` 1.1.4 and `NuGet.Packaging` 7.0.3.
Both the SDK's testing references and `Anton.SourceGeneration.RoslynTesting`
1.0.0 bring in that graph. Inspect it with:

```sh
dotnet nuget why CodeFixTests/CodingRules.CodeFixes.Tests.csproj System.Security.Cryptography.Pkcs --framework net10.0
```

A default restore fails with NU1902/NU1903 under warnings-as-errors. The existing
baseline was checked without changing repository policy using a command-scoped
exception that kept the advisory warnings visible:

```sh
dotnet test CodingRules.slnx -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903
```

All 885 tests passed: 41 analyzer and 844 code-fix tests, including packaged
consumer checks. This is test-coverage evidence, not a successful default
restore. Dependency repair belongs in the shared testing graph rather than
adding a permanent audit exception to each consumer.

Analyzer and code fix unit tests now create source with
`TestCode.Create(...)`. `InterpolateDiagnostic` takes the analyzer's
`DiagnosticDescriptor` and marks the expected span. The SDK imports the marker
method for both test roles. Each test uses `AnalyzerTestBuilder` or
`CodeFixTestBuilder` to run the corresponding Roslyn test.

## Selected formatting delivery (slice 02)

The SDK's existing pack target intentionally does not infer arbitrary analyzer
package assets. CodingRules now explicitly packs the complete Roslyn 4.7
assembly sets from Roslynator.Analyzers/Formatting.Analyzers 5.0.1, using
restore-only PackageReferences with generated path properties and standard
NuGet pack items. No shared SDK extension is needed for this pinned delivery.
The [formatting gate](formatting.md) restores the resulting Anton package in an
empty cache from an Anton-only feed, proves default build diagnostics, applies
exported CLI fixes, and checks exact output and a second formatter pass.
Tests also load the same upstream assets on the Roslyn 4.10 floor. The existing
test dependency audit issue above remains separate and unchanged.

## Test dependency repair on 2026-10-21

The analyzer and code fix test projects now explicitly pin
`System.Security.Cryptography.Pkcs` 9.0.21, the first patched 9.x version, to
repair the test-only Roslyn testing/NuGet.Packaging dependency graph. Ordinary
restore succeeds with warnings-as-errors and no audit exception. This matches
SourceGenerators' shared repair; remove these pins when SDK 1.1.1 is published
and adopted. The 2026-10-15 validation above records the original baseline.
