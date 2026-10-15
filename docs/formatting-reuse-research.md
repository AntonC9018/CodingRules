# Reusing formatting rules in Anton.CodingRules

Research date: 2026-10-15. Recommendation: reuse existing formatters and analyzers, then implement only the proven gaps. Keep **one consumer-facing `Anton.CodingRules` PackageReference**. That can mean bundled upstream analyzer assemblies or a package with dependencies; these have different deployment properties. No implementation or upstream issue changes were made for this research.

## Scope and evidence

Subsequent maintainer decision: MIT and Apache-2.0 reuse are acceptable, and
the implementation may choose a configured metapackage, bundled assemblies,
or source adaptation according to simplicity. Copied code must be identified
and retain required licenses and notices. This resolves the approval questions
in the historical recommendations below; see the
[discussion record](schedulelib-follow-up.md#scope-and-completion).

The target is [ScheduleLib #229], especially A1/A2/A3/A12, plus braces, statement layout, parentheses and trailing commas. This repository's [spec](spec.md) requires conservative diagnostics and behavior-preserving fixes; upstream rule names alone do not prove that contract.

Public ScheduleLib branch `feat/registry-history-analysis` was read at **`4c26748e448fe7ccbdffc3f8f6c4b05ffddce144`**: [.editorconfig][schedule-config], [cleanup profile][schedule-profile], [wrapper][schedule-wrapper], [formatting documentation][schedule-doc], [accepted initializer examples][schedule-review]. No local ScheduleLib checkout or private configuration was inspected.

Source inspections use these snapshots, which are development snapshots unless a release is explicitly named:

| Project | Source snapshot | Downloaded NuGet version |
|---|---|---|
| Roslyn | `036431aef582b93345a8f54b7a452926d2abd459` | SDK formatter tested with .NET SDK **10.0.112** |
| Roslynator | `167f43468118e620c1a4637d83d227d0d638444d` | `Roslynator.Formatting.Analyzers` and `Roslynator.Analyzers` **5.0.1** |
| StyleCop | current `ea51bca2a9e8fa42a61920e3980398a0e3d87f82`; release `aeb5e0951d9449fe67d0da726691f70caba3bab2` | stable **1.1.118**, prerelease **1.3.79-beta** |
| Meziantou | `decb1e62ae7205db62d8b6225220e108d05f52d4` | `Meziantou.Analyzer` **3.0.303**, same repository commit in nuspec |
| CSharpier | source inspected at `f0d3353eaa6b2cc185de1728c2c275675ca23f1a` | `CSharpier` / `CSharpier.Core` **1.3.0**, nuspec commit `c3fe3f22a4f091eaf759e0b5aa8f3b9d3565e51b` |
| SourceGenerators SDK | `a0fbc38df472cdbebf694c3aff2c7c7562d0de40` | `Anton.SourceGeneration.Sdk` **1.1.0**, same repository commit in nuspec |

Versions and layouts were checked through the [NuGet flat-container API][nuget-api] and actual `.nupkg` archives, not inferred from repository defaults. Later versions may change these findings.

## What the built-in stack actually covers

- **IDE0055** and `dotnet format whitespace` implement standard spacing, indentation and newline options. `csharp_new_line_before_members_in_object_initializers=true` addresses initializer members; `csharp_preserve_single_line_blocks=false` prevents preservation of compact blocks. Neither option specifies that a sole argument must join the invocation's `(`. There is no standard maximum-line-length argument wrapping engine in that options catalogue. [Formatting options][ms-format]
- **A2 is not proved by `csharp_preserve_single_line_blocks=true`**: preserving an already compact property does not require collapsing an expanded property. Setting it false, as ScheduleLib does, can conflict with a separate compact-property fixer. Test the combined pipeline, including the second formatting pass. [Formatting options][ms-format]
- **A3:** `dotnet_style_operator_placement_when_wrapping=beginning_of_line` is documented as a **refactoring preference**, without a diagnostic severity. It does not establish a build warning or bulk repair of existing trailing operators. [Microsoft refactoring options][ms-wrap]
- **A12 has a significant reverse-direction gap.** IDE0022's `when_on_single_line` limits conversion *from blocks to expressions*. Current Roslyn's `CanOfferUseBlockBody` considers the analyzer preference a block preference only when it is `Never`; it does not test whether an existing arrow expression spans lines. Consequently, the setting does not generally report existing multiline arrow bodies. [Exact helper][roslyn-helper], [IDE0022 documentation][ms-expression]
- Related built-in IDs are **IDE0021** constructors, **IDE0022** methods, **IDE0023** conversion operators, **IDE0024** operators, **IDE0025** properties, **IDE0026** indexers, **IDE0027** accessors, **IDE0053** lambdas and **IDE0061** local functions. These are separate scopes, not one universal A12 switch. [Rule catalogue][ms-rules]
- **IDE0011** with `csharp_prefer_braces=true` supplies a braces diagnostic/fix; **IDE0047/IDE0048** supply unnecessary/clarifying parentheses rules, with the `dotnet_style_parentheses_in_*` options. Reuse those before introducing CR equivalents. Semantic parentheses preferences are distinct from spaces/newlines around call parentheses. [Rule catalogue][ms-rules]
- IDE0055's preservation settings address statement layout, not “one conceptual operation per statement.” Neither the formatter nor brace insertion replaces CR operation analysis. Build enforcement of supported SDK style rules requires `EnforceCodeStyleInBuild`; severity and SDK version also matter. [Build configuration][ms-build]

`dotnet format style --diagnostics ...` runs built-in style fixes; `dotnet format analyzers --diagnostics ...` runs third-party fixes; `whitespace` does layout only. The CLI defaults to warning-or-higher fixes and can verify without writing. Shipping a NuGet package does not ship this external SDK command. [CLI contract][ms-cli]

## Roslynator: the strongest reusable analyzer match

The formatting package contains concrete analyzers **and exported code-fix providers**. Its formatting diagnostics are disabled by default, so enable selected IDs explicitly. Source boundaries and runtime checks matter:

| Requirement | Candidate | Proven coverage / boundary |
|---|---|---|
| A2 compact auto-property | **RCS0042** | `AccessorListAnalyzer` compacts properties/indexers without accessor bodies. It has **no accessor-count or maximum-line-length threshold**. It skips accessor attributes and non-whitespace trivia between the property name and closing brace. `init` follows the same bodyless-accessor path; runtime fix verified. It is broader than only concrete auto-properties, including interface/indexer declarations. [Analyzer][rcs-accessor], [fix][rcs-accessor-fix], [tests][rcs-accessor-tests] |
| A3 leading wrapped operator | **RCS0027** | `roslynator_binary_operator_new_line=before`; existing `&&`/`||` breaks are moved, with a fix. Also applies to arithmetic, comparisons and other registered binary operators, so its scope is broader than A3. Leaves single-line expressions alone; does not extract complex filters. [Analyzer][rcs-operator], [tests][rcs-operator-tests] |
| Whole wrapped binary chain | **RCS0055** | Adds separate-expression lines and indentation in already multiline chains. This is stronger than merely moving operators; do not enable by default to solve A3. [Tests][rcs-chain-tests] |
| A1 argument placement | **RCS0053** | Checks list indentation and consistency; contains special handling for one argument with a multiline brace block. **It does not require joining a correctly indented sole argument back to `(`.** Both `Call(new() …)` and `Call(\n    new() …)` survived the package experiment. Multi-element lists may instead be expanded. [Analyzer][rcs-list], [tests][rcs-list-tests] |
| A1 multiline initializer members | **RCS0053**, standard formatter | RCS0053 formats a list that is already multiline; it does not force every compact multi-member initializer to become multiline. **RCS0048 goes in the opposite direction** (“Put initializer on a single line”). Prefer explicit formatter policy. [List source][rcs-list] |
| Separate statements | **RCS0033** | Puts statements on separate lines; has a dedicated code fix and diagnostic/fix tests. It is layout, not conceptual-operation detection. [Tests][rcs-statements] |
| Braces | **RCS1007**, common package | Adds braces. Built-in IDE0011 avoids taking the large common package just for this. [Catalogue][rcs-catalogue] |
| Trailing comma | **RCS1260**, common package | Has analyzer/fix and `roslynator_trailing_comma_style=include` / `omit` / `omit_when_single_line`; not contained in the formatting-only package. Exact supported syntax must be tested against consumer language versions. [Tests][rcs-commas] |
| A12 reverse fix | **RCS1016**, common package | `roslynator_use_block_body_when_expression_spans_over_multiple_lines=true` diagnoses multiline arrow expressions and fixes them to blocks. Optional `...when_declaration_spans_over_multiple_lines=true` additionally covers multiline headers. With no `roslynator_body_style`, these options can enforce the restriction without forcing every simple block to become an arrow. [Analyzer][rcs-body], [option interpretation][rcs-body-options] |

RCS1016 covers methods, constructors, destructors, operators/conversions, local functions, properties/indexers and accessors; lambdas are a separate rule. It skips directive-containing bodies. Its header/expression tests are independent: a newline only between `=>` and an otherwise single-line expression is not automatically the same as a multiline expression. “Simple” semantic complexity is outside this formatting rule; keep existing operation rules and accepted chaining/initializer exceptions.

**License:** current Roslynator source and inspected 5.0.1 packages say **Apache-2.0**, not MIT. Adopting/configuring or redistributing its package is a distinct choice from the user's offered MIT-source vendoring path. Do not silently copy its source under an MIT-only assumption. [License][rcs-license], [NOTICE][rcs-notice]

## MIT-source alternatives and less suitable packages

**Meziantou.Analyzer 3.0.303:** **MA0007** is a good selective MIT-source candidate for trailing commas. It includes `CommaAnalyzer` and exported `CommaFixer`; current source handles object/array/collection and anonymous initializers, enums, `with`, switch expressions, property patterns and collection expressions. Collection expressions whose `]` shares the last item's line are explicitly exempt. `MA0007.ignore_catch_all_arm=true` exempts catch-all switch arms. It does not solve A1/A2/A3/A12. Whole-package adoption brings many unrelated diagnostics and configuration behavior; prefer selected source if only commas are needed. [Rule/options][ma-comma-doc], [analyzer][ma-comma], [fix][ma-comma-fix], [license][ma-license]

**StyleCop needs version-specific licensing:** stable **1.1.118**'s nuspec/license are Apache-2.0 and include `THIRD-PARTY-NOTICES.txt`; **1.3.79-beta**'s nuspec and pinned tag LICENSE are **MIT**. Do not infer the license of another version from either result. The beta source is a possible MIT-source reference for braces/commas; check copied files and their dependencies individually. [Pinned beta license][sa-license], [release][sa-release], [stable archive][sa-stable-package]

Relevant StyleCop rules have narrower purposes than the requested policy:

- **SA1503** adds mandatory braces (fix exists); **SA1500** lays out braces; **SA1502** expands braced elements but explicitly allows auto-properties. It **permits** compact accessors rather than requiring their collapse. **SA1107** separates statements; **SA1516** separates adjacent elements with blank lines, a different policy. [Rule source/docs][sa-rules]
- **SA1116** requires the first of multiple wrapped arguments/parameters after the opener; source only performs this comparison when count > 1. A sole multiline constructor argument is exempt, but no rule here joins it to `(`. **SA1118** exempts the first argument, lambdas, invocation/object/array creation and other listed constructs. Thus these do not implement A1, and applying all StyleCop wrapping rules would impose additional policy. [SA1116 source][sa-1116], [SA1118 docs][sa-1118]
- **SA1413** has a fix for missing trailing commas. Current docs include newer syntax than the old stable release; use the exact beta/source tests if choosing it for C# 12 collection expressions. The catalogue contains no demonstrated exact match for required A2 collapse, A3 operator relocation or A12 multiline-arrow reversal. [SA1413 docs][sa-1413]

**CSharpier 1.3.0:** MIT and available both as an external .NET tool and `CSharpier.Core` library, but not a Roslyn diagnostic/code-fix package. Its small configuration surface does not expose all these house-style choices. The actual tool compacted `Call(new() { Value = 1, Other = 2 });`, contradicting A1's multi-member layout, and retained a long arrow body split over several lines, contradicting A12. Plain `get; init;` compacted correctly. Source confirms a whole-document printer rather than targeted fixes. [Configuration][csharpier-config], [argument printer][csharpier-args], [property printer][csharpier-property]

Although Core has `lib/netstandard2.0/CSharpier.Core.dll`, its **1.3.0 nuspec requires Microsoft.CodeAnalysis 5.3.0** plus XML and filesystem dependencies. `netstandard2.0` alone does not imply compatibility with this repository's Roslyn 4.10 host floor. Vendoring the printer would be a substantial formatter fork, not a small fix reuse. The tool package also carries runtime/compiler dependencies under `tools/net8.0|net9.0|net10.0/any`; those are not analyzer assets. [Core archive][csharpier-core-package], [license][csharpier-license]

## Other #229 reuse leads

These are catalogue/source leads, **not exact-fixture coverage or package-integration proofs**:

- **A16 sealing:** Meziantou **MA0053** includes an analyzer and fixer for classes/records without known inheritors. Public types require `MA0053.public_class_should_be_sealed=true`; exceptions and types with virtual members have separate options. Its current-project inheritance model differs from “seal every non-abstract class except the specific attribute-wrapper pattern,” so verify that exception and existing inheritance before adoption. [Pinned rule/options][ma-seal]
- **A10 interpolation:** Roslyn has the **Convert concatenation to interpolated string refactoring**, which is useful fix infrastructure but does not itself supply an enforcement diagnostic. Roslynator **RCS1267** covers `string.Concat`, not proof of the requested `+` cases. Any reuse experiment must preserve explicit invariant conversion, overload binding, null behavior and custom interpolation-handler behavior; IDE0071 only simplifies an existing interpolation. [Microsoft refactoring][ms-interpolation], [RCS1267][rcs-concat], [IDE0071][ms-interpolation-simplify]
- **A5 parameter count:** Sonar **S107** is an existing parameter-count diagnostic worth comparing before writing detection. It does not define the requested threshold/exclusion policy or provide a proven behavior-preserving “introduce request record/struct” fix. Treat it as a detector/reference lead; its current source header specifies the Sonar Source-Available License rather than MIT; no Sonar redistribution is recommended here. [Official rule source][sonar-parameters]

## Existing JetBrains stack: retain it while proving gaps

ScheduleLib already pins `JetBrains.ReSharper.GlobalTools` **2026.2.3.1**. Its wrapper copies selected compiled files and EditorConfigs into a synthetic `netstandard2.1`, preview-language solution with no application dependencies/analyzers. The cleanup profile enables only reformatting plus braces/trailing commas; **it does not enable expression-body syntax cleanup**. This distinction means A12 is not enforced merely because its Roslyn keys exist in `.editorconfig`. [Wrapper][schedule-wrapper], [profile][schedule-profile]

JetBrains documents solution mode as necessary for syntax cleanup; file-only mode reformats regardless of profile. Consequently, replacing the wrapper with a file-only command would lose braces/commas behavior. No replacement syntax rewriter is justified by these findings. [CleanupCode contract][jb-cleanup]

Existing settings already request `wrap_after_invocation_lpar=false`, `wrap_arguments_style=chop_if_long`, object/collection initializer `chop_always`, a maximum of one initializer element on a line, and `wrap_before_binary_opsign=true`. This is promising A1/A3 formatter coverage, **not independently verified exact-fixture output in this research**. The `false` preference is not a diagnostic specific to sole `new()` arguments and applies more broadly. A1's single-member compact exception currently exists in ScheduleLib configuration and needs a maintained fixture. [Config][schedule-config], [JetBrains option reference][jb-lines]

For A2, the specific documented candidate is `resharper_csharp_place_abstract_accessorholder_on_single_line=true`, with `...keep_existing_declaration_block_arrangement=false` if preservation interferes. This is preferable to globally collapsing all simple blocks; actual interaction with standard `csharp_preserve_single_line_blocks=false` must be tested. No A2 accessor-count threshold is documented here. The current config does not set these keys. [JetBrains declaration block options][jb-lines]

CleanupCode is a separately installed external tool, not a Roslyn analyzer assembly that can simply be placed in `analyzers/dotnet/cs`. Its use can coexist with one Anton NuGet package. Redistributing JetBrains binaries inside Anton is **not** proposed; the free CLI's availability does not establish MIT redistribution rights. [Tool documentation][jb-cleanup]

## Practical checks performed

Disposable `/tmp` fixtures only; source checkouts were not modified:

1. Downloaded/extracted all package versions listed above and inspected nuspecs, licenses, analyzer host directories, helper DLLs and code-fix DLLs.
2. Created a small `net10.0`, C# 12 project referencing the actual Roslynator formatting package. Enabled RCS0042/RCS0027/RCS0053 and `roslynator_binary_operator_new_line=before`; ran `dotnet format analyzers --diagnostics RCS0042 RCS0027 RCS0053 --no-restore`. It compacted `get; init;`, moved trailing `&&` (and `+`) to continuation starts, and preserved both sole-`new()` placements. This proves CLI discovery of these packaged fixes on that SDK, **not Visual Studio/Rider or Roslyn 4.10 compatibility**.
3. Ran `dotnet format style --diagnostics IDE0022 --no-restore` with `when_on_single_line:warning`. It converted `SimpleBlock` to `=> 3` and left existing multiline arrow expressions unchanged, confirming the source-level A12 gap.
4. Added Roslynator common package, enabled RCS1016 with both multiline options above, and ran `dotnet format analyzers --diagnostics RCS1016 --no-restore`. Both multiline arrows became return-bearing blocks; an existing simple arrow stayed intact.
5. Ran actual CSharpier 1.3.0 tool against multi-member initializers and a long arrow expression. It compacted the initializer and kept the multiline arrow, demonstrating policy mismatch rather than an untested assumption.

No JetBrains fixture run, IDE light-bulb run, Fix All stress test, packaged Anton integration or minimum-host run was performed. Source-level fix availability is not a claim that every consumer host discovers it.

## Keeping a single Anton package

The checked-in package uses SDK **1.1.0**. Its published `Sdk.props` sets `SuppressDependenciesWhenPacking=true` for `.Package`. Its pack target gathers project-reference DLLs and only the two named Anton helper packages; it does **not** automatically repackage arbitrary external analyzer PackageReferences. Its default Roslyn is **4.10.0**, analyzer/fix target **netstandard2.0**. [Exact published SDK source][sdk-props], [pack target][sdk-targets]

| Approach | Actual package implications | Assessment |
|---|---|---|
| Bundle upstream binaries | Explicitly include analyzer, code-fix and every needed helper DLL under `analyzers/dotnet/.../cs`, plus licenses/notices. Ship optional `.globalconfig` and `buildTransitive/Anton.CodingRules.props` to register it using `GlobalAnalyzerConfigFiles`. Validate IDE and CLI discovery separately. | One physical artifact and one consumer reference. Best Roslynator experiment; carries third-party IDs and licensing. |
| Dependency/meta-package | Override dependency suppression and author the nuspec dependency/asset flow correctly. `PrivateAssets=all` on the package's own external ref prevents the intended dependency from flowing. NuGet's default private assets include analyzers/build; do not assume indirect analyzer flow. Preserve/forward required assets explicitly and inspect restore results. | One consumer reference, multiple restored package IDs/artifacts. Good if “one package” means one installation entry. |
| Selectively vendor MIT source | Compile reviewed analyzer+fix+required helper code into existing Anton assemblies; retain upstream notices and record pinned provenance/modifications. Adapt to Roslyn 4.10 and conservative generated-code/trivia boundaries. | Strong option for MA0007 or selected MIT StyleCop code; no extra analyzer DLLs or unrelated diagnostics. Creates maintenance/test ownership. |
| Package config only | Add `.globalconfig` through a build/buildTransitive import, possibly SDK style-build properties; document formatter `.editorconfig` settings separately. A loose `.editorconfig` inside a NuGet does not automatically govern consumer source paths. | Reuse built-ins; cannot fill missing A1/A2/A3/A12 diagnostics by configuration alone. |

NuGet distinguishes `analyzers`, `build` and `buildTransitive` assets. `.globalconfig` controls compiler/analyzer configuration; it is not proof that JetBrains or every whitespace formatter reads the same packaged preferences. Verify formatter options in the actual consumer pipeline. [NuGet conventions][nuget-analyzers], [asset-flow documentation][nuget-assets]

Actual Roslynator 5.0.1 layout repeats **eight DLLs per `roslyn3.8`, `roslyn4.7`, `roslyn5.0` band**: analyzer, code-fix, and six `Roslynator_Formatting_Analyzers_Roslynator.*` helpers (the common package uses a separate helper prefix). Preserve the supported directory/identity scheme, or deliberately choose/test one compatible band; do not flatten all three bands into one folder or copy only the analyzer. Host compatibility depends on Roslyn API versions, not the consumer's target framework. A 4.10 host can only consider compatible bands; this was not runtime-tested. [Formatting archive][rcs-package]

Meziantou 3.0.303 supplies analyzer/fixer pairs under `roslyn4.8`, `4.14`, `5.0`, `5.6`, `5.9`, plus build/buildTransitive props and configuration files. Copying DLLs alone omits that configuration behavior. StyleCop stable ships analyzer+fix DLLs and localized resources under `analyzers/dotnet/cs`. Neither whole package is a selective-rule assembly: disabling unrelated diagnostics requires an explicit policy, and disabling an ID does not necessarily remove all analyzer initialization overhead. [Meziantou archive][ma-package], [StyleCop archive][sa-stable-package]

Do not bundle `Microsoft.CodeAnalysis*.dll` beside analyzers as a workaround for a newer API requirement; hosts own those assemblies. Test the minimum supported compiler/IDE, modern SDK, restore asset list and exported fixes. Ship fixes alongside analyzers for IDE use; command-line builds themselves never apply them.

## License handling and recommendation

This records license text, not legal advice. MIT sources ([Roslyn][roslyn-license], [Meziantou][ma-license], [CSharpier][csharpier-license], [StyleCop beta][sa-license]) require: “The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.” Include the full applicable license/notice in source and package third-party notices; a README thank-you alone does not reproduce that notice. Audit the files actually copied and third-party notices, not just the root license.

Roslynator's Apache license requires a license copy on redistribution; modified files need prominent change notices; relevant copyright/attribution notices are retained; applicable NOTICE content must remain readable. Its repository NOTICE identifies Orang code. The downloaded formatting package did not contain a standalone LICENSE/NOTICE file, so bundle the applicable upstream texts deliberately if redistributing DLLs. This is **not** an MIT-source recommendation or assumed authorization to vendor Apache code. [Apache section 4][apache], [Roslynator notices][rcs-notice]

Ranked next steps:

1. **Retain existing CleanupCode and standard Roslyn formatting first.** Add exact A1/A2/A3 positive/negative fixtures to prove configuration, including sole typed/target-typed `new`, one vs multiple initializer members, compact `get/init`, comments and a second pass. Do not rewrite parentheses/newline infrastructure.
2. **Prototype bundling selected Roslynator package functionality in Anton**, if Apache binary redistribution is acceptable: formatting RCS0042/RCS0027/RCS0033 and common RCS1016 provide the strongest demonstrated diagnostic+fix reuse. Enable only chosen IDs. Assess the cost of the common package for A12 against a small custom rule. Preserve existing CR initializer/chaining exceptions. This is an integration experiment, not authorization to vendor Apache source.
3. **For an MIT-source-only path**, reuse standard built-ins and selectively port MA0007 or the exact MIT StyleCop beta code only where needed. Keep a small Anton remainder for sole-new argument adjacency, compact-property enforcement, Boolean-only line placement and multiline-arrow reversal if no approved upstream binary is used. Roslyn's MIT conversion code can inform fixes, but its internal helper dependency graph is not a drop-in public API; prefer public SyntaxFactory/Document/Formatter APIs.
4. Before implementation, verify the packed Anton artifact in a clean consumer, both build diagnostics and `dotnet format analyzers`, actual IDE code actions/Fix All, Roslyn 4.10 hosting and modern hosting. Test formatter/fixer idempotence and attribution contents. Do not declare #229 complete from setting names or package installation alone.

[ScheduleLib #229]: https://github.com/AntonC9018/ScheduleLib/issues/229
[schedule-config]: https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/.editorconfig
[schedule-profile]: https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/All.sln.DotSettings
[schedule-wrapper]: https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/build/format.py
[schedule-doc]: https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/docs/formatting.md
[schedule-review]: https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/docs/analyzer-review.md
[nuget-api]: https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource
[ms-format]: https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/csharp-formatting-options
[ms-wrap]: https://learn.microsoft.com/en-us/visualstudio/ide/reference/code-styles-refactoring-options?view=visualstudio
[ms-expression]: https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0022
[ms-rules]: https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/language-rules
[ms-build]: https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview#code-style-analysis
[ms-cli]: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format
[roslyn-helper]: https://github.com/dotnet/roslyn/blob/036431aef582b93345a8f54b7a452926d2abd459/src/Analyzers/CSharp/Analyzers/UseExpressionBody/Helpers/UseExpressionBodyHelper%601.cs#L226
[roslyn-license]: https://github.com/dotnet/roslyn/blob/036431aef582b93345a8f54b7a452926d2abd459/License.txt
[rcs-accessor]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/src/Formatting.Analyzers/CSharp/AccessorListAnalyzer.cs
[rcs-accessor-fix]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/src/Formatting.Analyzers.CodeFixes/CSharp/AccessorListCodeFixProvider.cs
[rcs-accessor-tests]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/src/Tests/Formatting.Analyzers.Tests/RCS0042RemoveNewLinesFromAccessorListOfAutoPropertyTests.cs
[rcs-operator]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/src/Formatting.Analyzers/CSharp/PlaceNewLineAfterOrBeforeBinaryOperatorAnalyzer.cs
[rcs-operator-tests]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/src/Tests/Formatting.Analyzers.Tests/RCS0027PlaceNewLineAfterOrBeforeBinaryOperatorTests.cs
[rcs-chain-tests]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/src/Tests/Formatting.Analyzers.Tests/RCS0055FixFormattingOfBinaryExpressionChainTests.cs
[rcs-list]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/src/Formatting.Analyzers/CSharp/FixFormattingOfListAnalyzer.cs
[rcs-list-tests]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/src/Tests/Formatting.Analyzers.Tests/RCS0053FixFormattingOfListTests.cs
[rcs-statements]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/src/Tests/Formatting.Analyzers.Tests/RCS0033PutStatementOnItsOwnLineTests.cs
[rcs-catalogue]: https://josefpihrt.github.io/docs/roslynator/analyzers/
[rcs-commas]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/src/Tests/Analyzers.Tests/RCS1260AddOrRemoveTrailingCommaTests.cs
[rcs-body]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/src/Analyzers/CSharp/Analysis/UseBlockBodyOrExpressionBodyAnalyzer.cs
[rcs-body-options]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/src/Common/CSharp/CodeStyle/BodyStyle.cs
[rcs-license]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/LICENSE.txt
[rcs-notice]: https://github.com/dotnet/roslynator/blob/167f43468118e620c1a4637d83d227d0d638444d/NOTICE.md
[rcs-package]: https://api.nuget.org/v3-flatcontainer/roslynator.formatting.analyzers/5.0.1/roslynator.formatting.analyzers.5.0.1.nupkg
[ma-comma-doc]: https://github.com/meziantou/Meziantou.Analyzer/blob/decb1e62ae7205db62d8b6225220e108d05f52d4/docs/Rules/MA0007.md
[ma-comma]: https://github.com/meziantou/Meziantou.Analyzer/blob/decb1e62ae7205db62d8b6225220e108d05f52d4/src/Meziantou.Analyzer/Rules/CommaAnalyzer.cs
[ma-comma-fix]: https://github.com/meziantou/Meziantou.Analyzer/blob/decb1e62ae7205db62d8b6225220e108d05f52d4/src/Meziantou.Analyzer.CodeFixers/Rules/CommaFixer.cs
[ma-license]: https://github.com/meziantou/Meziantou.Analyzer/blob/decb1e62ae7205db62d8b6225220e108d05f52d4/LICENSE.txt
[ma-package]: https://api.nuget.org/v3-flatcontainer/meziantou.analyzer/3.0.303/meziantou.analyzer.3.0.303.nupkg
[sa-license]: https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/aeb5e0951d9449fe67d0da726691f70caba3bab2/LICENSE
[sa-release]: https://github.com/DotNetAnalyzers/StyleCopAnalyzers/releases/tag/1.3.79-beta
[sa-stable-package]: https://api.nuget.org/v3-flatcontainer/stylecop.analyzers/1.1.118/stylecop.analyzers.1.1.118.nupkg
[sa-rules]: https://github.com/DotNetAnalyzers/StyleCopAnalyzers/tree/ea51bca2a9e8fa42a61920e3980398a0e3d87f82/documentation
[sa-1116]: https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/ea51bca2a9e8fa42a61920e3980398a0e3d87f82/src/StyleCop.Analyzers/ReadabilityRules/SA1116SplitParametersMustStartOnLineAfterDeclaration.cs
[sa-1118]: https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/ea51bca2a9e8fa42a61920e3980398a0e3d87f82/documentation/SA1118.md
[sa-1413]: https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/ea51bca2a9e8fa42a61920e3980398a0e3d87f82/documentation/SA1413.md
[csharpier-config]: https://csharpier.com/docs/Configuration
[csharpier-args]: https://github.com/belav/csharpier/blob/f0d3353eaa6b2cc185de1728c2c275675ca23f1a/Src/CSharpier.Core/CSharp/SyntaxPrinter/ArgumentListLikeSyntax.cs
[csharpier-property]: https://github.com/belav/csharpier/blob/f0d3353eaa6b2cc185de1728c2c275675ca23f1a/Src/CSharpier.Core/CSharp/SyntaxPrinter/SyntaxNodePrinters/BasePropertyDeclaration.cs
[csharpier-core-package]: https://api.nuget.org/v3-flatcontainer/csharpier.core/1.3.0/csharpier.core.1.3.0.nupkg
[csharpier-license]: https://github.com/belav/csharpier/blob/f0d3353eaa6b2cc185de1728c2c275675ca23f1a/LICENSE
[jb-cleanup]: https://www.jetbrains.com/help/resharper/CleanupCode.html
[jb-lines]: https://www.jetbrains.com/help/resharper/EditorConfig_CSHARP_LineBreaksPageSchema.html
[sdk-props]: https://github.com/AntonC9018/SourceGenerators/blob/a0fbc38df472cdbebf694c3aff2c7c7562d0de40/source/SourceGeneration.Sdk/Sdk/Sdk.props
[sdk-targets]: https://github.com/AntonC9018/SourceGenerators/blob/a0fbc38df472cdbebf694c3aff2c7c7562d0de40/source/SourceGeneration.Sdk/Sdk/Sdk.targets
[nuget-analyzers]: https://learn.microsoft.com/en-us/nuget/guides/analyzers-conventions
[nuget-assets]: https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#controlling-dependency-assets
[apache]: https://www.apache.org/licenses/LICENSE-2.0#redistribution

[ma-seal]: https://github.com/meziantou/Meziantou.Analyzer/blob/decb1e62ae7205db62d8b6225220e108d05f52d4/docs/Rules/MA0053.md
[ms-interpolation]: https://github.com/dotnet/roslyn/blob/036431aef582b93345a8f54b7a452926d2abd459/src/Features/CSharp/Portable/ConvertToInterpolatedString/CSharpConvertConcatenationToInterpolatedStringRefactoringProvider.cs
[rcs-concat]: https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1267/
[ms-interpolation-simplify]: https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0071
[sonar-parameters]: https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/src/SonarAnalyzer.CSharp/Rules/TooManyParameters.cs
