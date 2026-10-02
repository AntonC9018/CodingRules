# FindJobHelper WebUI dogfood run

The linked FindJobHelper branch `anton/cv-skills-long-lists` was checked out
locally at commit `0f579ef650d0f06c7bb04ff7c32b8daf97b65986`. The
analyzer DLL was added to a WebUI build through a temporary MSBuild import.
The checkout remained clean.

`CR0001` reported six diagnostics. WebUI treats warnings as errors, so its
build displayed these as errors:

| File | Line | Final return |
| --- | ---: | --- |
| `ApplicationIndexStore.cs` | 209 | nullable ternary |
| `ApplicationIndexStore.cs` | 346 | boolean predicate |
| `ApplicationIndexStore.cs` | 358 | nullable ternary |
| `ApplicationIngestion.cs` | 571 | nullable call after guard |
| `FolderOpener.cs` | 202 | nullable ternary |
| `Program.cs` | 431 | boolean predicate |

The lone ternary in `Program.cs:316` was not reported, matching the documented
exception. The package consumption test separately restores `Anton.CodingRules`
from a fresh local NuGet cache, verifies that `CR0001` appears, and applies the
packaged code fix.

## Inline conditions, 2026-09-30

The final `0.1.0-issue003final` package was restored into a fresh temporary
consumer copy produced by `git archive` at the same pinned FindJobHelper commit.
It was added through a temporary MSBuild import to WebUI and its project
references. The original reference checkout remained clean. The Release build
succeeded with **66 total warnings and zero errors**, including the unchanged
return families and ten distinct CR02xx sites. Warnings-as-errors and locked
restore were disabled only in that temporary copy. The earlier development
package reported 68 warnings; the final conservative subject classifier no
longer identifies calculated index expressions as independent scalar subjects.

All seven expected inline-condition roots appeared:

| File and line | Diagnostic | Root |
| --- | --- | --- |
| ApplicationIndexStore.cs:103 | CR0204 | Four StartsWith alternatives |
| ApplicationIndexStore.cs:319 | CR0201 | BOM length and three bytes |
| ApplicationIndexStore.cs:331 | CR0203 | Guarded Equals with two Replace calls |
| ApplicationIndexStore.cs:543 | CR0201 | wasQuoted and four Contains checks |
| WorkspaceConfig.cs:67 | CR0203 | TryGetProperty and ValueKind |
| CvGeneration.cs:299 | CR0202 | LayoutMode and PageLayout |
| ApplicationCatalog.cs:415 | CR0204 | Static delegate with eight negated suffix checks |

The index ranges (ApplicationIndexStore.cs:203/352), IsFinite plus bound
(ProgressReporting.cs:52), null plus Directory.Exists (ApplicationCatalog.cs:408),
and two StartsWith alternatives (ApplicationIndexStore.cs:98) produced no CR02xx
warning. Reduced fixtures retaining the original operations and literal values
verify compiling fixes; out-variable body use gets expansion alone.

Three additional sites were inspected: ApplicationIndexStore.cs:223 recognizes
Trim followed by equality without assigning FirstOrDefault a category;
ApplicationIndexStore.cs:502 identifies distinct Count and element-Length
subjects; ApplicationMetadata.cs:16 identifies two distinct properties.
The Count/element-Length case is a conservative catalogue boundary rather than
proof about the author's conceptual invariant. No broader semantic intent is
claimed. Filter/when, expression-tree, nullable-flow and other individually
unsafe rewrites remain covered by specific safety tests.

The fresh-package test now also applies the preferred CR0201 fix and builds
its consumer. Execution tests compare original/fixed traces for all accepted
host groups, short circuit, mutation, exceptions, argument conversion order,
loops/continue, filter search before finally, switch guards and initialization.
The final solution run passed 41 analyzer and 177 code-fix/package tests,
including the two comment-preservation regression cases and both fresh-package
consumer tests.

### Review corrections, 2026-09-30

After the consolidated review fixes, the Release solution passed **245 tests**:
41 analyzer tests and 204 code-fix/package tests, including both fresh-package
consumers and 27 new regression cases. The regressions compile and execute
saved/reparsed fixes, check reached producer evaluation, preserve unrelated
diagnostics, and bound Fix All completion and idempotence.

The new `0.1.0-issue003fixes` package was restored into a separate cache for the
pinned temporary reference copy. A stalled third-party dependency download was
replaced by seeding those unchanged dependencies from the earlier cache; the
CodingRules package itself was freshly restored from the new package feed.
The reference Release build again succeeded with **66 warnings and zero errors**.
All seven expected positive roots, ten distinct CR02xx sites, and the four
negative boundary groups above remained unchanged. The original FindJobHelper
checkout stayed clean at the pinned commit.

The nested nullable-argument case that previously rewrote its outer if now
produces no diagnostic when neither action can safely rewrite the selected
argument. Flow-preserving expansion remains supported for the actual whole-if
root, including mixed trees and producing leaves. Nested logical/conditional
producer operands and element indices now retain laziness and grouping; nested
member initializers and constructor/destructor arrows have working extraction.
