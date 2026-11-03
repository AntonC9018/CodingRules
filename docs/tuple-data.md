# Tuple data and named record structs (CR0900)

CR0900 is a Readability warning directing authors to represent tuple data with
a named record struct. It implements ScheduleLib #229 A4 and the settled Q15/Q16
examples in [the decision record](schedulelib-follow-up.md#tuple-and-parameter-refinements-q15q18),
tracked by [CodingRules #23](https://github.com/AntonC9018/CodingRules/issues/23).
It is a **manual finding**, with no automatic code fix. A confident finding does
not establish the domain name, sharing boundary, or safe caller migration.

## Detection boundary

The initial conservative generalization recognizes these source declarations,
regardless of accessibility, staticness, or tuple element names:

- Ordinary method return types and ordinary/local method parameter types when
  the type is a tuple, an array of tuples, or one of the three exact generic
  types below with an immediate tuple element.
- Local variable and field declarations, and property types, storing arrays
  of tuples, `System.Collections.Generic.List<T>` with tuple elements, or
  `System.ReadOnlySpan<T>` with tuple elements. Inferred `var`, aliases,
  target-typed initializers, and multiple declarators are handled semantically.
- `System.Collections.Generic.IEnumerable<T>` with tuple elements is included
  for method results and inputs, covering the ordinary filtered-item iterator.
  Lazy sequence locals/fields/properties are outside this first storage rule;
  a local can retain the accepted local iterator's sequence without a finding.

The generic catalogue resolves definitions once per compilation and compares
symbol identity. User collections with matching short names are not included.
Only immediate tuple elements are inspected; nested arbitrary wrappers,
`Task<(...)>`, dictionaries, custom collections and other generic types are
outside this first catalogue. The rule does not infer shared domain meaning
from names or attempt whole-program data-flow analysis.

Locations identify the return/property/parameter type or each collection
variable's identifier. One declaration receives one finding, even when its
tuple has several elements. Using a collection and passing its tuple elements
through a method input are separate declarations and can each receive a finding.
Tuple construction expressions and deconstruction syntax are not analyzed.

Local-function **result declarations** are excluded, including iterator results.
This exclusion does not apply to their input declarations or materialized
collection declarations in their bodies. Nor does use of a collection by a
local function exempt its declaration. Private ordinary methods remain included.

Purely local scalar tuple values, swaps, and LINQ intermediate tuples are outside
this settled scope; their omission establishes no universal permission or ban.
Direct tuple fields/properties, constructor/delegate/lambda signatures, and
other declaration kinds are also outside the initial catalogue. Unresolved
immediate tuple element types and syntax-error declarations are skipped.

## Reviewed coverage

The shared, compiled consumer fixtures reduce the pinned source examples in
[tuple research](tuple-examples-research.md):

- E4: the materialized calendar `List<(Id, Action, CalendarId, DesiredEventIndex)>`,
  its local `ReportPlanned` tuple input, and an ordinary calendar API result all
  report. Using the list from a local function supplies no blanket exemption.
- E1: the course-name mapping `ReadOnlySpan<(string From, string To)>` property
  reports and calls for a named mapping value.
- E2: the private qualification-parser tuple return reports; its caller's
  deconstruction does not receive an additional finding.
- E5: the ordinary generic `IEnumerable<(T Item, FilteredSchedule Schedule)>`
  result reports, including when implemented as an iterator.
- E3: the nonempty-cell local iterator, a local holding its lazy sequence, and
  its deconstructing foreach remain unreported. Scalar tuple locals/swaps and
  local scalar results also remain unreported.
- Representative `CourseNameMapping`, `QualificationSegment`, `CalendarPlanStep`,
  and `FilteredItem<T>` record structs and their collection/result/caller shapes
  remain unreported, including record deconstruction.

`AnalyzerTests/TupleDataAnalyzerTests.cs` checks those six exact diagnostic
locations and the accepted file, plus array/List/span storage, alias binding,
collection lookalikes, multiple declarators, a collection inside a local
function, catalogue limits, warning/error/disabled severity, scoped pragma,
SuppressMessage, and generated-code exclusion. All normal fixtures must compile.

## Manual migration and configuration

Choose a record name and fields that describe the shared data's role, update
the producers and consumers together, and verify compilation and behavior.
For the calendar case, `readonly record struct CalendarPlanStep(...)` is a
candidate; the correct ownership and naming remain an application decision.
Review overload resolution, tuple conversions/equality, mutation, deconstruction,
serialization, API compatibility, and caller evaluation before migrating.
The package offers no fixer or Fix All action for CR0900, so no automatic
transformation is claimed to preserve those contracts.

The analyzer runs concurrently and skips generated code using the standard
Roslyn analyzer driver. Ordinary severity configuration and scoped suppression
apply independently of every other rule:

```ini
[*.cs]
dotnet_diagnostic.CR0900.severity = error
```

Use `none` to disable the diagnostic, or scoped `#pragma warning disable CR0900`
and restore, or `SuppressMessage("Readability", "CR0900", Justification = "...")`
for an intentional exception. A suppression is not an accepted-case fixture.

## Packaged verification

```sh
dotnet pack Package/CodingRules.Package.csproj -c Release --output artifacts/tuple-data
python3 build/verify-tuple-consumer.py artifacts/tuple-data
```

The gate restores the exact package in an isolated consumer with an empty NuGet
cache and a local-only feed. It checks six exact positive locations, zero extra
findings in the accepted/generated files, unchanged source after a CR0900 CLI
fixing pass, warnings-as-errors failure, and real `.editorconfig` error/disabled
behavior. Other diagnostic families are disabled only to isolate this rule's
evidence. CI runs this gate on the same archive as the formatting and release
checks, before retaining the tested artifact. Source-level analyzer tests and
packaged-consumer checks establish coverage; no downstream migration or NuGet
publication is implied.
