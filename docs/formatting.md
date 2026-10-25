# Packaged formatting rules

`Anton.CodingRules` is the only required PackageReference. It bundles the
unmodified Roslynator.Analyzers and Roslynator.Formatting.Analyzers **5.0.1**
Roslyn 4.7 assets. The common code-fix assembly is omitted and the selected
RCS1016 conversion is provided by CodingRules; the formatting code-fix assembly
and both helper sets are retained unchanged. These assets work on the
repository's Roslyn **4.10** host floor and the
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
  bodies are skipped. A packaged CodingRules diagnostic suppressor excludes
  arrows with non-whitespace arrow-token trivia (including directly attached
  block/line comments) and async methods/local functions returning custom
  task-like types. Roslynator 5.0.1 loses the former comments and can omit a
  required return for the latter. The CodingRules fixer also declines these
  shapes directly because the tested CLI bypasses diagnostic suppressors.
  These exclusions leave the entire body unchanged; they are safety boundaries,
  not acceptance of the formatting.
  Async void and resolved framework `Task`/`Task<T>`/`ValueTask`/`ValueTask<T>` remain
  supported. Simple single-line arrows and blocks remain accepted;
  a multiline declaration header alone is not a trigger. Lambdas are separate
  policy and are not enabled here. Alternate RCS1016 block-to-expression
  configurations are outside this package's fixer contract.

**CR0800 / A3** supplies the actual upstream gap: move an existing trailing
`&&` or `||` to the continuation's beginning. It preserves line endings,
indentation, grouping and evaluation. Single-line expressions, already-leading
operators, arithmetic/comparison/bitwise operators and generated code are left
alone. Comments or directives in the whitespace that would move cause a skip.
Roslynator's broader RCS0027 and RCS0055 remain disabled. No Boolean expression
is wrapped or split merely to enforce placement.

**CR0801 / A1** expands object initializers with at least two members so each
member and the closing brace start on separate lines. It handles compact and
partly wrapped layouts, trailing commas, nested object initializers and comments.
Empty and single-member initializers remain accepted; collection, array,
anonymous-object and `with` initializers are outside this rule.

**CR0802 / A1** removes an existing line break between an invocation's opening
parenthesis and its sole positional explicit or target-typed constructor
argument. Already adjacent constructors remain accepted. Named/ref arguments,
multiple arguments, constructor argument lists and non-constructor expressions
are separate policy. A comment in the gap prevents joining. Both rules skip
syntax with errors or directives and generated code. The fixes change layout,
retain token text and leave initializer values inline. Existing operation
findings are independent. Ordinary initializer assignments are accepted by CR0302
after the #19 fix; combined right-hand-side operations remain analyzed.

Before offering an A1 fix, CodingRules compares implicit caller-information
argument constants throughout the original document and the final formatted,
reparsed source. Enclosing `CallerArgumentExpression` captures and downstream
`CallerLineNumber` arguments (including calls in later members) must keep their
values. If a value changes or cannot be established, the fix is declined and the
layout warning remains for manual review. Explicit caller arguments and edits
that leave all implicit values unchanged remain fixable. Fix All validates each
edit against the document produced by preceding edits; safe sites still format
when another site is unsafe. This follows the existing separation of confident
findings from automatic fix availability and does not inject caller constants.

The standard formatter supplies indentation and
`csharp_new_line_before_members_in_object_initializers=true` for expanded
initializers. With `csharp_preserve_single_line_blocks=true`, it leaves compact
multi-member initializers unchanged; disabling that option would conflict with
A2. Actual formatter and Roslynator RCS0053 tests demonstrate both A1 gaps, so
neither RCS0053 nor a broader block-expansion policy is enabled. The two CR rules
supply only these missing behaviors; Roslyn's existing lambda indentation is
retained when joining a delegate constructor.

All configurable upstream diagnostics are explicitly allowlisted, including
support/configuration diagnostics. Other existing CR families retain their
defaults. Consumers can override individual IDs in `.editorconfig`. The
descriptor-inventory test must pass whenever the upstream version changes.

## Consumer workflow

An ordinary `dotnet build` diagnoses all five rules from the package defaults.
For the fixer workflow, merge this into the consumer's `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.RCS0042.severity = warning
dotnet_diagnostic.RCS1016.severity = warning
csharp_preserve_single_line_blocks = true
csharp_new_line_before_members_in_object_initializers = true
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
dotnet format analyzers Consumer.csproj --no-restore --diagnostics RCS0042 RCS1016 CR0800 CR0801 CR0802
dotnet format whitespace Consumer.csproj --no-restore
dotnet format analyzers Consumer.csproj --no-restore --diagnostics RCS0042 RCS1016 CR0800 CR0801 CR0802
dotnet format whitespace Consumer.csproj --no-restore --verify-no-changes
dotnet build Consumer.csproj --no-restore
```

The explicit diagnostic filter limits this command to the selected formatting
slice. Other CR diagnostics remain independently available. The standard
formatter supplies indentation; it does not replace these diagnostics/fixes.
The repeated analyzer fixing command is safe and idempotent. In the consumer
fixture, analyzer `--verify-no-changes` returns exit 2 on both SDK 10.0.1xx and
10.0.4xx even when the fixer makes no changes. SDK 10.0.1xx reports all eleven
residual diagnostics: six arrow-comment, two custom task-like and three
caller-information diagnostics. SDK 10.0.4xx reports only the three
caller-information diagnostics, omitting manual-only diagnostics that offer no
action. Use ordinary build diagnostics for the suppression-aware check; the
consumer gate requires the caller set exactly and rejects diagnostics outside
that set and the eight expected manual RCS1016 cases. Golden source and
second-pass byte comparisons prove the manual cases stay unchanged.

For an A1-only check after applying the fixes, run:

```sh
dotnet format analyzers Consumer.csproj --no-restore --diagnostics CR0801 CR0802 --verify-no-changes
dotnet format whitespace Consumer.csproj --no-restore --verify-no-changes
```

The analyzer check returns nonzero for remaining A1 violations. Comment-separated
constructor arguments and the other conservative skips above are left alone.
Caller-information-sensitive layouts retain their warnings and make this check
return nonzero, even though the fixer leaves their source unchanged. Review
these sites manually; a successful fix pass alone does not clear their findings.

## Delivery, licenses and verification

Bundling avoids relying on analyzer transitivity from a metapackage. SDK 1.1.0
does not gather arbitrary upstream analyzer references; the package project
explicitly restores and packs the two pinned helper dependency sets. This uses
normal NuGet pack items and needs no change to the shared SDK. Production code in
CodingRules implements the Boolean-placement and A1 layout gaps, a conservative
RCS1016 safety suppressor and an independent expression-to-block fixer using
public Roslyn syntax/formatter APIs, without copied upstream source. Suppressions
CRSPR0801 (arrow-token trivia) and CRSPR0802 (custom async return contract)
exclude the unsafe shapes from build diagnostics. SDK 10.0.112's tested
`dotnet format` workflow bypasses suppressors and reproduced both unsafe fixes;
see also the [upstream suppressor issue](https://github.com/dotnet/sdk/issues/51364).
The package therefore omits `Roslynator.CSharp.Analyzers.CodeFixes.dll` and
ships exactly one RCS1016 provider, which independently refuses the same unsafe
shapes. It implements the selected expression-to-block action; other common
upstream fixes are outside the enabled policy. The formatting fixer and both
helper sets remain intact. All 15 retained upstream DLLs are unmodified.
This keeps the selected binary analyzers without maintaining an upstream fork.
The unmodified binaries are Apache-2.0; CodingRules source
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
exactly twenty-one ordinary-build warnings: one RCS0042, six RCS1016, two CR0800,
eight CR0801 and four CR0802.
It then applies actual exported fixes through `dotnet format`, compares exact
source with `CodeFixTests/Expected/Formatting.Fixed.cs.txt` and
`CodeFixTests/Expected/InitializerCalls.Fixed.cs.txt` and
`CodeFixTests/Expected/CallerInformation.Fixed.cs.txt` plus
`CodeFixTests/Expected/CallerExpressions.Fixed.cs.txt`, runs whitespace
formatting and a second idempotent pass, and builds with warnings as errors
except for the three precisely asserted residual A1 warnings.
The analyzer verification command is separately expected to return exit 2.
SDK 10.0.1xx reports all eleven diagnostics (six arrow-comment, two custom
task-like and three protected A1 caller-information diagnostics); SDK 10.0.4xx
reports only the three caller-information diagnostics because it omits
manual-only diagnostics that offer no action. The gate requires the caller set
exactly and rejects any diagnostic outside that set and the eight expected
manual RCS1016 cases; golden source and second-pass byte comparisons prove the
manual cases stay unchanged. Whitespace verification and the final compiler
build must succeed. The A1-only analyzer verification command
must return exit 2 with exactly those three A1 diagnostics. Its command-scoped
`WarningsNotAsErrors=CR0801%3BCR0802` build exception permits these deliberate
manual cases; the gate asserts their exact locations and rejects other warnings.
Accepted simple bodies, arithmetic placement, comments and directive bodies
are part of the same fixture. Exact-output fixtures include block and line
comments immediately after arrows in methods, properties and accessors. A
separate custom generic task-like fixture contains both an async method and
local function; it must remain unchanged, compile before/after fixing and
survive the second pass. CI checks the retained release artifact itself.

The A1 consumer fixture includes already adjacent constructors, nested
initializers, trailing commas, member and call comments, a simple factory value
and the sole explicit delegate-constructor argument. It compiles and runs before
and after formatting with the same evaluation trace, `123345667890123`. The
fixture locally suppresses CR0300 findings to isolate layout; CR0302 remains
enabled to guard against initializer false positives from #19. The A1 fixer
neither extracts values nor adds operation requirements. Its bytes
must remain identical on the second analyzer/formatter pass.
The consumer project suppresses unrelated CR1400 sealing-review findings.
Separate caller-information fixtures isolate enclosing expression captures
from constructor line numbers and a line-number argument in a later member. The CLI
must retain three unsafe layouts, format safe sites after the last observer,
and preserve all runtime output, including captured text and line numbers.

`FormattingReuseTests` run the actual upstream analyzers and formatting provider,
the CodingRules expression-body provider and packaged safety suppressor on
Roslyn 4.10, check the complete descriptor inventory and exported Fix All
providers, and exercise trivia skips, default configuration, consumer overrides,
formatter conflict/recovery, CRLF/tabs and short-circuit runtime effects. They
also pass unsuppressed upstream diagnostics to the replacement provider and
assert no unsafe action is offered. Constructors, destructors, indexers,
operators/conversions, init/event accessors, ref returns and throws are
compilation-checked alongside the four primary member shapes.
Packaging tests and this consumer gate establish CLI discovery; they do not
claim GUI-host interaction or coverage of every upstream host shape.

`InitializerCallFormattingTests.cs` adds reduced fixtures for all four pinned
A1 sites at ScheduleLib `12e601e7b62511bdecc44e280407fee12147b230`:
`AppTasks.cs:53/73/127` and `CliSettings.Removals.cs:269`. These prove the
context/task/services, files-provider request, groups collection request and
delegate-constructor layouts. They also check accepted layouts, comments,
nested initialization, CRLF/tabs, literal preservation, runtime order, unchanged
operation findings, consumer severity overrides and a stable second pass.
Exported Fix All uses one equivalence key for both IDs, including when a host
reuses the action selected for one ID while fixing the other. A regression
reproduces the CLI failure with separate keys; the consumer gate proves the fix.

Validation on 2026-10-16: all **951** solution tests passed (41 analyzer and
910 code-fix tests, including **66** focused formatting cases). Commands:

```sh
dotnet test CodingRules.slnx -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903
dotnet test CodeFixTests/CodingRules.CodeFixes.Tests.csproj -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903 --filter FullyQualifiedName~FormattingReuseTests
python3 -m unittest discover -s build/tests -v
dotnet pack Package/CodingRules.Package.csproj -c Release --output /tmp/codingrules-ticket03-20261016
python3 build/verify-formatting-consumer.py /tmp/codingrules-ticket03-20261016
git diff --check
```

The focused formatting run, all **15** Python build guard tests, standalone
Release pack and isolated consumer gate also passed. The archive at
`/tmp/codingrules-ticket03-20261016/Anton.CodingRules.0.1.0.nupkg` was checked
directly; all 15 retained upstream DLLs matched the pinned NuGet assets byte
for byte. The gate confirmed exact output, two unchanged custom async bodies,
all six retained arrow comments, a byte-identical second fixing pass, successful
whitespace verification and a final compiler build with zero warnings/errors.
Its expected analyzer-verification exit 2 contained only the eight protected
manual cases described above; the A1-only check returned exit 0. `git diff
--check` also passed.

The A1 cross-ID Fix All regression initially failed for both IDs because the
batch fixer returned no action with the other diagnostic's key. The shared
action key made both cases pass. The current sequential Fix All provider retains
the shared key and supports both IDs, including when the CLI supplies no key
because its first diagnostic is unsafe.

In slice 02, temporarily bypassing the suppressor with the original upstream provider made
all six arrow-comment exact-output regressions fail and reproduced CS0161 for
the custom task-like method. The temporary negative-control changes were
restored before the passing runs; both custom method/local-function diagnostics
are also tested directly against the guarded provider without a suppressor.

The normal command without that existing, command-scoped exception still fails
restore on the shared testing graph's Pkcs 9.0.6 NU1902/NU1903 advisories. The
dependency-repair slice owns that baseline issue; this change adds no permanent
audit exception. Normal package build/pack and the isolated consumer gate pass
without an audit exception. MSBuild requires the semicolon-separated warning IDs
to be escaped as `%3B`; passing a literal quoted semicolon produced MSB1006.

Validation on 2026-10-18 (A1 caller-information safety): the baseline at
`f6e2310` was reproduced in a scratch copy under
`/tmp/codingrules-a1-caller-repro`. All ten isolated runtime regressions failed
before the fix, covering individual actions and exported Fix All for enclosing
expression captures, constructor line numbers and downstream line numbers in a
later member. A freshly packed baseline also changed the constructor value from
18 to 17, changed the captured compact initializer to multiline text, and moved
the downstream value from 27 to 30. These observations used compiler-emitted
runtime values after formatting and reparsing, not token equality alone.

The corrected implementation passed **986** solution tests (64 analyzer and
922 code-fix tests), including **78** focused formatting cases, and all **15**
Python build tests. Verification commands:

```sh
dotnet test CodingRules.slnx -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903
dotnet test CodeFixTests/CodingRules.CodeFixes.Tests.csproj -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903 --filter FullyQualifiedName~FormattingReuseTests
python3 -m unittest discover -s build/tests -v
dotnet pack Package/CodingRules.Package.csproj -c Release --output /tmp/codingrules-a1-caller-package
python3 build/verify-formatting-consumer.py /tmp/codingrules-a1-caller-package
git diff --check
```

The isolated gate passed exact source, runtime output and byte-identical second
passes. The A1-only check returned the expected exit 2 for exactly three retained
manual warnings; the combined check contained those three plus the existing
eight RCS1016 manual cases. The final compiler build passed with only the three
explicitly permitted and asserted A1 warnings. All 15 bundled Roslynator DLLs
matched the pinned 5.0.1 NuGet assets byte for byte. No third-party code was copied
or modified; existing MIT/Apache-2.0 licensing and provenance remain accurate.
The command-scoped NU1902/NU1903 exception still covers the pre-existing Pkcs
9.0.6 test-dependency advisories only.

The 2026-10-24 follow-up adds CodingRules CR0803 for all embedded-statement
braces (including a single-line condition followed by a single statement) and
CR0804 for separation after casts. These are independent of Roslynator's
multiline-only RCS1001. The package sets `csharp_space_after_cast = true`;
comments and existing line breaks between the cast and operand remain intact.

## Subsequent fluent calls (CR0805)

Start each invocation after the first on a new continuation line, including
chains that fit on one line:

```csharp
historical.Where(Matches)
    .ToArray();
```

A plain receiver, namespace or property qualification is not itself a method
call. Null-conditional boundaries remain intact (?.). The whitespace-only fixer
preserves the existing newline convention and never relocates comments. It
leaves findings manual where comments, directives, interpolated-string layout
or implicit caller-information observations prevent a safe automatic edit.
