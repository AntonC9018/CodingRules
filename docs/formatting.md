# Packaged formatting rules

`Anton.CodingRules` is the only required PackageReference. It bundles the
unmodified Roslynator.Analyzers and Roslynator.Formatting.Analyzers **5.0.1**
Roslyn 4.7 assets, including both complete helper and code-fix assembly sets.
These assets work on the repository's Roslyn **4.10** host floor and the
**.NET SDK 10.0.112** compiler/CLI tested here. No IDE GUI session was tested.

The package imports a low-priority global analyzer configuration through
`buildTransitive/Anton.CodingRules.props`. Only these upstream diagnostics are
enabled, as warnings:

- **RCS0042 / A2:** compact bodyless accessors, including `get; init;`.
  It also supports interface/indexer declarations, with no accessor-count or
  line-length threshold. Comments, directives and accessor attributes are
  conservative skips.
- **RCS1016 / A12:** convert an expression spanning multiple lines to a block.
  Methods, local functions, properties and accessors are tested, including
  return-bearing getters and statement-only void methods/setters. Upstream
  also supports constructors, destructors, operators and indexers. Directive
  bodies are skipped. Simple single-line arrows and blocks remain accepted;
  a multiline declaration header alone is not a trigger. Lambdas are separate
  policy and are not enabled here.

**CR0800 / A3** supplies the actual upstream gap: move an existing trailing
`&&` or `||` to the continuation's beginning. It preserves line endings,
indentation, grouping and evaluation. Single-line expressions, already-leading
operators, arithmetic/comparison/bitwise operators and generated code are left
alone. Comments or directives in the whitespace that would move cause a skip.
Roslynator's broader RCS0027 and RCS0055 remain disabled. No Boolean expression
is wrapped or split merely to enforce placement.

All configurable upstream diagnostics are explicitly allowlisted, including
support/configuration diagnostics. Other existing CR families retain their
defaults. Consumers can override individual IDs in `.editorconfig`. The
descriptor-inventory test must pass whenever the upstream version changes.

## Consumer workflow

An ordinary `dotnet build` diagnoses all three rules from the package defaults.
For the fixer workflow, merge this into the consumer's `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.RCS0042.severity = warning
dotnet_diagnostic.RCS1016.severity = warning
csharp_preserve_single_line_blocks = true
```

The same snippet is shipped in
`config/CodingRules.Formatting.editorconfig` inside the NuGet archive.
SDK 10.0.112's `dotnet format` filter overlooks the global diagnostic severity
for these upstream rules; explicit per-file severities make it select their
exported fixes. This was reproduced with the bundled binaries and the CLI's
diagnostic log, then verified against the exact expected consumer output.
The CLI's [severity filtering implementation](https://source.dot.net/dotnet-format/Analyzers/AnalyzerOptionExtensions.cs.html)
distinguishes tree diagnostic settings from other analyzer options.

Remove or change an existing `csharp_preserve_single_line_blocks=false` override:
it conflicts with compact properties and even makes the upstream fix re-expand
its own result. The package defaults this option to `true`; an overriding
consumer choice wins. This option preserves existing compact blocks rather
than converting multiline method bodies to arrows. No `roslynator_body_style`
or multiline-declaration restriction is set by the package.

After restoring the consumer, run (substitute its solution or project path):

```sh
dotnet format analyzers Consumer.csproj --no-restore --diagnostics RCS0042 RCS1016 CR0800
dotnet format whitespace Consumer.csproj --no-restore
dotnet format analyzers Consumer.csproj --no-restore --diagnostics RCS0042 RCS1016 CR0800 --verify-no-changes
dotnet format whitespace Consumer.csproj --no-restore --verify-no-changes
```

The explicit diagnostic filter limits this command to the selected formatting
slice. Other CR diagnostics remain independently available. The standard
formatter supplies indentation; it does not replace these diagnostics/fixes.

## Delivery, licenses and verification

Bundling avoids relying on analyzer transitivity from a metapackage. SDK 1.1.0
does not gather arbitrary upstream analyzer references; the package project
explicitly restores and packs the two pinned dependency sets. This uses normal
NuGet pack items and needs no change to the shared SDK. Production code in
CodingRules implements only the Boolean-placement policy gap, without copied
upstream source. The unmodified binaries are Apache-2.0; CodingRules source
remains MIT. Required license, NOTICE and credit/provenance are shipped under
`third-party/roslynator`. The package declares `MIT AND Apache-2.0`.

Run the repeatable consumer gate:

```sh
python3 build/verify-formatting-consumer.py
# Or verify an already-packed artifact, as CI does:
python3 build/verify-formatting-consumer.py path/to/Anton.CodingRules.0.1.0.nupkg
```

It uses a fresh cache, an Anton-only local feed and a consumer referencing only
Anton.CodingRules. Before adding the shipped CLI configuration it asserts
exactly nine ordinary-build warnings: one RCS0042, six RCS1016 and two CR0800.
It then applies actual exported fixes through `dotnet format`, compares exact
source with `CodeFixTests/Expected/Formatting.Fixed.cs.txt`, runs whitespace
formatting and a second idempotent pass, and builds with warnings as errors.
Accepted simple bodies, arithmetic placement, comments and directive bodies
are part of the same fixture. CI checks the retained release artifact itself.

`FormattingReuseTests` run the actual upstream analyzers/providers on Roslyn
4.10, check the complete descriptor inventory and exported Fix All providers,
and exercise trivia skips, default configuration, consumer overrides,
formatter conflict/recovery, CRLF/tabs and short-circuit runtime effects.
Packaging tests and this consumer gate establish CLI discovery; they do not
claim GUI-host interaction or coverage of every upstream host shape.

Validation on 2026-10-15: all **900** solution tests passed (41 analyzer and
859 code-fix tests, including the 15 new formatting tests) with:

```sh
dotnet test CodingRules.slnx -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903
```

The normal command without that existing, command-scoped exception still fails
restore on the shared testing graph's Pkcs 9.0.6 NU1902/NU1903 advisories. The
dependency-repair slice owns that baseline issue; this change adds no permanent
audit exception. Normal package build/pack and the isolated consumer gate pass
without an audit exception. The 12 Python release guard tests also pass.
