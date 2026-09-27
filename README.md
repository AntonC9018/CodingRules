# CodingRules

Roslyn analyzers and code fixes for the C# coding standards in
[FindJobHelper's `AGENTS.md`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/AGENTS.md).

The [spec](docs/spec.md) records the complete set of rules. Implementation
is split into [GitHub issues](https://github.com/AntonC9018/CodingRules/issues).
`CR0001` checks explicit return decisions after a top-level guard and offers
code fixes for every form it reports. Diagnostics are warnings by default. A
consumer can change severity through `.editorconfig`:

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
to pack `Anton.CodingRules` into a temporary feed, check its assemblies and
`CR0001` in a fresh consumer, apply its packaged fix, and build the consumer. See the
[dependency pass](docs/sdk-dependencies.md) and [WebUI dogfood run](docs/dogfood.md).

This repository does not have CI yet.
