# Reviewed compound conditions and HTTP status filters

ScheduleLib slice 08 reuses CR0200–CR0204. No diagnostic ID or configuration
switch is added. The warnings remain enabled by default, exclude generated code,
and support normal `.editorconfig`, pragma and SuppressMessage configuration.

## Evidence and original examples

The source is the [reviewed ScheduleLib commit](https://github.com/AntonC9018/ScheduleLib/tree/12e601e7b62511bdecc44e280407fee12147b230),
[A3/A11/A15 in #229](https://github.com/AntonC9018/ScheduleLib/issues/229), and the
[accepted follow-up](schedulelib-follow-up.md). Q15–Q20 change other slices;
they do not replace these conditions or the reviewed status set.
The fixtures retain the actual condition expressions, using small host doubles
for Google exceptions and the application context. They use the real framework
JSON nodes, CancellationToken and HttpStatusCode.

Before extending the implementation, five compiled probes exercised each
original expression and every available action against the existing families:
config handling reported CR0201 and cancellation reported CR0202; the JSON throw
guard, authentication alternative and numeric status pattern had no finding.
The baseline is recorded in commit `1ce6189`.

| Pinned source | Finding now | Available semantic action |
| --- | --- | --- |
| CliConfigExecution.cs:55, success / verb / `IReadOnlyList<string> profiles` | CR0201 | Nested explicit checks retain the pattern variable's scope in the original body. |
| CliSettings.Removals.cs:119, JSON value / TryGetValue alternatives | CR0203 | Separate failure guards preserve short-circuiting and the key/operation out variables used afterward. |
| CalendarSync.cs:322, cancellation exception filter | CR0202 | A named local function is invoked inside `when`, with the catch variable passed as its exact type. |
| CliRemoteFailure.cs:69, cancellation guard | CR0202 | Local-function extraction or explicit control flow. |
| CliRemoteFailure.cs:78, token exception / Google status property pattern | CR0202 | Local-function extraction or explicit control flow, retaining the original property pattern and getter gating. |
| CalendarSync.cs:370, numeric rejected-action filter | CR0203 | Extract `IsRejectedHttpStatusCode` and replace the cast/numeric alternatives with named enum members. |

Leading `&&`/`||` layout remains CR0800's separate responsibility. Rewrapping a
condition does not discharge these semantic findings. The existing condition
priority and severity fallback remain unchanged.

## Mechanically recognizable extensions

CR0202 can identify a simple `value is Type` subject. A typed recursive pattern
with exactly one named constant property subpattern identifies the nested
property subject instead of treating it as the entire receiver. For example,
`error is TokenResponseException` checks the exception's type, whereas
`error is GoogleApiException { HttpStatusCode: HttpStatusCode.Unauthorized }`
also checks its status. The rewrite retains each complete pattern; it never
repeats a property getter to decompose the pattern. Positional, multi-property,
variable-binding, field and otherwise unresolved property patterns do not gain
this new recognition. Existing independently recognized conditions still apply.

The flow-preserving OR expansion now accepts a single terminal `throw`, as well
as the existing terminal `return`, optionally wrapped in a single-statement
block. It retains declaration scope through separate guards. The terminal
statement is duplicated in syntax but can execute only on the first failed
check, exactly once. Its call bindings are checked with that precise expected
multiplicity; unrelated calls keep their original binding counts. Comments in
the terminal body and implicit caller-information arguments prevent this
expansion. General multi-statement failure paths remain for agent review.

The status extension recognizes only a whole catch-filter expression of the
form `(int)<framework HttpStatusCode expression> is ...`, with exactly these six
integer literal alternatives, each present once (in any order, with optional
pattern parentheses):

| Value | Named member |
| --- | --- |
| 400 | HttpStatusCode.BadRequest |
| 403 | HttpStatusCode.Forbidden |
| 404 | HttpStatusCode.NotFound |
| 409 | HttpStatusCode.Conflict |
| 412 | HttpStatusCode.PreconditionFailed |
| 422 | HttpStatusCode.UnprocessableEntity |

The enum type and each member's constant are resolved against the framework.
The action removes the unnecessary int conversion and returns the named enum
pattern from a collision-free `IsRejectedHttpStatusCode` local function. The
original expression, including its receiver/getter, still runs once during
filter evaluation. Filter exceptions remain filter exceptions, and evaluation
still precedes stack unwinding. The action never moves the check into `catch`.

Other status sets, duplicated alternatives, negation, nullable/lookalike types,
constant aliases, equality chains and status checks outside catch filters do
not gain this specialized rewrite. They require agent review of their meaning;
this finite set is not a general definition of retryable or rejected HTTP
responses. Existing generic condition rules can still recognize other shapes.
Named enum patterns are accepted already. Two same-value simple invariants,
null guards, ranges and plain two-type alternatives retain their existing
accepted behavior. Comments/directives, expression trees, caller information,
unsafe captures and unresolved syntax retain the condition family's existing
conservative safety checks.

## Verification and packaged offering

`ReviewedCompoundConditionTests` verifies the pinned expressions, every offered
action, accepted cases, generated-code forms, comments, lookalikes, unreviewed
sets and caller-information boundaries. `ReviewedCompoundRuntimeTests` compiles
and executes saved/reparsed fixes, testing JSON success/failure assignments,
type-pattern scope, short-circuit getter effects, cancellation catch selection,
filter getter exceptions, and finally ordering. Its status test covers every
integer from −1 through 600 and a conflicting predicate name.

`ReviewedCompoundAnalyzerTests` shares the package consumer's positive and
accepted source files and asserts all six diagnostic IDs and locations plus
warning/error/none configuration. The consumer runs the original and fixed
program and compares the complete trace, including all six matched statuses.

```sh
dotnet test CodingRules.slnx -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903
python3 -m unittest discover -s build/tests -v
dotnet pack Package/CodingRules.Package.csproj -c Release --output /tmp/cr08-package
python3 build/verify-condition-consumer.py /tmp/cr08-package
```

`%3B` escapes the advisory-list separator for MSBuild; this keeps the existing
NU1902/NU1903 test dependency exception command-scoped. The package gate uses a
fresh cache and a local feed with no remote sources. It verifies exact findings,
accepted/generated source stability, warnings-as-errors and editorconfig
severity, exported fixes, unchanged runtime behavior and a byte-identical subsequent
fix pass. The .NET CLI chooses one action equivalence key per provider/pass;
mixed extraction-only filters and expansion-only out guards use a bounded
workflow of up to three passes, with strictly fewer findings on each pass. CI runs this gate against the exact release candidate. Shared setup also serves
the existing tuple and contract manual gates.
