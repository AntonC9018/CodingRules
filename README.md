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

After SourceGenerators PR #10 publishes the SDK and helper packages, build
and test using .NET 10:

```sh
mkdir -p artifacts/local-feed
dotnet test CodingRules.slnx -c Release
dotnet pack Package/CodingRules.Package.csproj -c Release --output artifacts/local-feed
```

The versioned project SDK is `Anton.SourceGeneration.Sdk` 1.1.0.
For local SDK development, run `./build/prepare-local-sdk.sh /path/to/SourceGenerators`
first. `NuGet.Config` includes this optional feed and a repository-local cache;
the prepare script refreshes cached development dependencies. CI uses only
NuGet.org and a fresh cache. The normal
analyzer package is `Anton.CodingRules`; it contains the analyzer and code fix
assemblies. The code-fix test project uses `Anton.SourceGeneration.PackageTesting`
to pack `Anton.CodingRules` into a temporary feed, check `CR0001` in a fresh
consumer, apply its packaged fix, and build the consumer. See the
[dependency pass](docs/sdk-dependencies.md) and [WebUI dogfood run](docs/dogfood.md).

CI tests every PR and main push. NuGet publishing runs only when a maintainer
publishes a GitHub release, using the exact package retained by successful
main CI. See [CI and release setup](docs/releases.md).
