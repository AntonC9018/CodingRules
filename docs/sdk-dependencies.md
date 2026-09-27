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
