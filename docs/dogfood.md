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
