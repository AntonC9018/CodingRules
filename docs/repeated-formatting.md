# Repeated timestamp formatting candidates (CR1300)

CR1300 is a Readability warning implementing [ticket 12 / CodingRules #31](https://github.com/AntonC9018/CodingRules/issues/31)
and [A14/Q14](schedulelib-follow-up.md#data-shapes-parameter-groups-and-reuse-q11q14).
It identifies a **duplication candidate**, not a requirement to share a helper.
Review [the meaning and reasons to change](agent-coding-rules.md#review-duplication-by-meaning)
before deciding whether the operations belong together.

## Initial recognition boundary

Within one ordinary method or local function, match resolved framework
`DateTime.ToString(string, IFormatProvider)` or
`DateTimeOffset.ToString(string, IFormatProvider)` calls. Both arguments must
be explicitly supplied. A match requires the same resolved overload, constant
format value (including null), provider source, and conditional-access shape.
Receiver values may differ. Constant aliases, named arguments and different
spellings of the same provider symbol do not prevent a match.

Provider sources are null, the same local/parameter, the same field reached
through matching local/parameter/current-instance/field paths, or the same
framework `CultureInfo.InvariantCulture`, `CurrentCulture` or `CurrentUICulture`
property. Different symbols, arbitrary property getters, calls, indexers and
user conversions do not establish a match. A matching source does not prove
that its runtime value stays unchanged; no results are cached or moved.

Conditional-access shape means the same sequence of receiver types at the
directly enclosing null-propagation gates. Direct formatting differs from
nullable formatting; one gate differs from two. This includes the pinned
[CalendarSync.cs at 12e601e, lines 239–244](https://github.com/AntonC9018/ScheduleLib/blob/12e601e7b62511bdecc44e280407fee12147b230/src/MainCli/CalendarSync.cs#L239):

```csharp
start = ev.Start?.DateTimeDateTimeOffset?.ToString(
    "o", System.Globalization.CultureInfo.InvariantCulture),
end = ev.End?.DateTimeDateTimeOffset?.ToString(
    "o", System.Globalization.CultureInfo.InvariantCulture),
```

Each later match receives one diagnostic on its formatting invocation, with the
first occurrence as an additional location. Three matches produce two warnings.
Branches and loops within a method share its candidate set; matching operations
in separate methods never do. Local functions are analyzed independently;
lambdas and anonymous methods do not contribute to their enclosing method and
are outside initial detection. Constructors, accessors, member initializers, other
formatting APIs, nonconstant formats, invalid calls, and generated code remain
outside this boundary. Existing operation/return rules remain independent.

## Semantic review and intentional separation

The reviewed start/end values may share one calendar timestamp representation.
Audit timestamps and exported timestamps may instead have independent contracts
and reasons to change, even while both currently use invariant `"o"` formatting.
Keep independently meaningful named helpers separate. When matching operations
must remain in one method, justify a scoped suppression:

```csharp
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Readability", "CR1300",
    Justification = "Audit and export timestamps have independent versioning policies.")]
```

A narrow `#pragma warning disable CR1300` / `#pragma warning restore CR1300`
around an intentional match also works. Neighboring candidates and other IDs
remain enabled. The analyzer cannot infer the domain distinction from syntax;
a name alone does not opt out a matching operation in the same method.

No automatic code fix or Fix All is offered. Manual extraction must preserve
receiver evaluation, branch timing, null results, formatting, culture observations
and exceptions. Sharing the operation does not justify eager evaluation or
caching results. Consumer agent-guidance distribution/import remains ticket 14.

## Configuration and regression evidence

Normal Roslyn configuration applies:

```ini
[*.cs]
dotnet_diagnostic.CR1300.severity = error
```

Use `none` to disable it. Analyzer tests check default warnings, severity changes,
generated files/comments/attributes, scoped pragma and SuppressMessage behavior,
exact locations, additional locations, argument binding, constant aliases,
provider identity, null shapes, nested callable boundaries and invalid/custom APIs.

The compiled shared `CodeFixTests/Consumers/RepeatedFormatting/Findings.cs` and
`Accepted.cs` fixtures supply seven positive locations, the exact A14 chains,
matching/mismatching formats/providers and justified independent policies.
`build/verify-repeated-formatting-consumer.py` registers the isolated package
regression using the existing manual-consumer harness. For the final stable
candidate only, run:

```sh
python3 build/verify-repeated-formatting-consumer.py artifacts/release
```

The gate checks exact warning locations, accepted/generated cases, unchanged
source in a fixer host, warnings-as-errors and actual editorconfig disabled/error
behavior against the supplied archive. Package execution is deferred until all
tickets are complete; no package build, consumer gate, or CI enablement is part
of this ticket's validation.
