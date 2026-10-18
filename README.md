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
their helper invocation at the original guard site. The reviewed ScheduleLib
config, JSON, cancellation/authentication and numeric status examples reuse these
families; the [compound-condition boundary](docs/compound-conditions.md) records
the exact six-status enum predicate, safe flow rewrites and package gate.
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

`CR0600` requires a return type that represents proven index-search absence on
every declared non-private member; API migration is manual. `CR0601` adds an
explicit sentinel guard when a primitive caller forwards a certified private
or local helper. The initial evidence set, dependency exemptions and safe-fix
boundaries are documented in [primitive sentinels](docs/sentinels.md).

`CR0700` replaces a parameterless framework string trim immediately inspected
for Length or equality with a non-null string constant. Its synchronous scalar
helper avoids the temporary trimmed string while preserving null exceptions,
ordinal UTF-16 comparison and receiver evaluation. The [span assessment](docs/spans.md)
records the finite catalogue and manual ownership, API and lifetime boundaries.

The same package now supplies Roslynator `RCS0042` for compact auto-accessors,
`RCS1016` for multiline-expression block bodies, `CR0800` for leading
wrapped `&&`/`||`, and `CR0801`/`CR0802` for multiline initializer members and
adjacent sole constructor arguments. Other upstream diagnostics remain disabled. See
[packaged formatting](docs/formatting.md) for the required CLI `.editorconfig`
snippet, formatter interaction, fixer commands, licenses and consumer checks.

`CR0900` reports tuple method results, method inputs and materialized collection
data that need a named record struct. API migration is manual. Local-function
results and deconstruction remain allowed; scalar local tuples and swaps remain
outside the settled scope. The [tuple data contract](docs/tuple-data.md) records
the finite collection catalogue, Q15/Q16 coverage and isolated package gate.

`CR1000` reports locally owned interface, virtual/abstract, delegate and
abstract-class constructor contracts with four or more parameters, including
cancellation. Architectural grouping is manual; implementations and overrides
receive no duplicate. The [contract parameter boundary](docs/contract-parameters.md)
records the static-function and external-contract exceptions, accepted
Finish/Edit fixtures, configuration and isolated package gate.

`CR1001` requires a configuration builder for multi-parameter overload alternatives,
including static operations. The approved pair has the same declaring type, name,
staticness, generic arity and parameter count, exactly one differing resolved
parameter type, and matching ref/params/default presence. One-parameter Parse and
builder Output overloads remain allowed. The [overload configuration boundary](docs/overload-configuration.md)
records manual API migration, required-step review and isolated package evidence.

`CR1100` prefers interpolation for runtime built-in string-only concatenations,
including ScheduleLib's invariant desired identifier and lock path. It preserves
explicit formatting calls, string overload selection and handler gating. The
[interpolation contract](docs/string-concatenation.md) records accepted cases,
reuse evidence and the shared regression reserved for final package validation.

`CR1200` replaces proven unchanged assignment branches with explicit conditional
updates, including property/accessor arrow hosts. The [assignment contract](docs/unchanged-assignments.md)
records storage/null proofs, setter and receiver exclusions, returning behavior,
and the shared regression reserved for final package validation.

`CR1300` identifies repeated timestamp formatting within a method as a duplication
candidate. Review [shared meaning](docs/agent-coding-rules.md#review-duplication-by-meaning)
before extracting a helper; independent domain policies may remain separate with
a justified scoped suppression. The [recognition contract](docs/repeated-formatting.md)
records exact A14 coverage, accepted distinctions and the deferred package gate.

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
