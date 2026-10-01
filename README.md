# CodingRules

Roslyn analyzers and code fixes for the C# coding standards in
[FindJobHelper's `AGENTS.md`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/AGENTS.md).

The [spec](docs/spec.md) records the complete set of rules. Implementation
is split into [GitHub issues](https://github.com/AntonC9018/CodingRules/issues).
`CR0001` checks explicit return decisions after a top-level guard and offers
code fixes for every form it reports. `CR0003` through `CR0006` check the same
decision forms in returns nested deeper than the function body's top level.
`CR0200` through `CR0204` check mixed Boolean operators, more than two checks,
independent subjects, combined operations, and three or more repeated alternatives.
They offer local-function extraction first and statement expansion when safe.
Member initializers use a private static helper; filters and switch guards keep
their helper invocation at the original guard site.
`CR0300` names nested argument operations, `CR0301` names embedded conditional
values, and `CR0302` separates arithmetic or other mechanically proven combined
values. Calls and constructors with no explicitly supplied arguments remain
allowed: `Use(GetValue())` is compliant; `Save(CreateRequest(id))` needs a named
step. Direct values and one supported calculation remain inline. Linear LINQ
pipelines, whole named conditional values and lone conditional returns retain
their existing policy. Both safe actions preserve evaluation order and branch
timing; extraction is offered first, then statement expansion.

`CR0400` names composed delegate projections; `CR0401` extracts whole stages
with several result-feeding temporaries or nested decisions. Simple lambdas,
independent simple aggregate components and linear pipelines remain allowed.
`CR0402` names dictionary/lookup selector arguments and makes a key-only
overload's identity mapping explicit. The [pipeline catalogue](docs/pipelines.md)
records supported framework signatures, equivalent overload pairs and safety
boundaries. CR02, CR03 and return diagnostics remain independently enabled.

`CR0500` names every positional argument in a supplied repeated-type parameter
group. Calls, constructors, base/this/primary base lists and attribute constructors
are supported. Exact framework exceptions, the shipped callee annotation,
external registrations and semantic safety boundaries are documented in
[named argument roles](docs/named-arguments.md). The fix preserves expression
text, evaluation order and the selected overload.

For a mathematical formula whose shape is useful to retain, use normal scoped
suppression with a justification. This leaves neighboring methods enabled:

```csharp
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Readability", "CR0302", Justification = "Keep the formula intact for comparison with its derivation.")]
static double Formula(double a, double b, double c) => a + b + c;

static double Neighbor(double a, double b, double c) => a + b + c; // CR0302
```

Alternatively, place `#pragma warning disable CR0302` immediately before the
intended function and `#pragma warning restore CR0302` immediately after it.
Use the exact rule ID to keep return and condition rules enabled.
Diagnostics are warnings by default. A consumer can change severity through
`.editorconfig`:

```ini
dotnet_diagnostic.CR0001.severity = error
```

## Local build

The SourceGeneration SDK has not been published yet. Pack it and its helper
libraries from a local checkout, then build and test CodingRules:

```sh
./build/prepare-local-sdk.sh /path/to/SourceGenerators
dotnet test CodingRules.slnx -c Release
dotnet pack Package/CodingRules.Package.csproj -c Release --output artifacts/local-feed
```

The versioned project SDK restores from `artifacts/local-feed` through
`NuGet.Config`, which also uses a repository-local package cache. The prepare
script refreshes that cache after packing the SDK and package-testing helper
so a previous local build of the same version cannot be reused. The normal
analyzer package is `Anton.CodingRules`; it contains the analyzer and code fix
assemblies. The code-fix test project uses `Anton.SourceGeneration.PackageTesting`
to pack `Anton.CodingRules` into a temporary feed, check `CR0001` in a fresh
consumer, apply its packaged fix, and build the consumer. See the
[dependency pass](docs/sdk-dependencies.md) and [WebUI dogfood run](docs/dogfood.md).

This repository does not have CI yet.
