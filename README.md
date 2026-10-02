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
