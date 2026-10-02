# Coding rules analyzer spec

Status: accepted for implementation.

## Source and scope

The source policy is the coding standards section of
[FindJobHelper's `AGENTS.md` at `0f579ef`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/AGENTS.md#coding-standards).
This spec records every rule in that section, including rules that need
judgment and may require a later implementation ticket. The analyzer applies
to hand-written C# in production code, tests, and code generators. It skips
generated code. Diagnostics are warnings by default and may be reconfigured
through `.editorconfig`.

## Rule catalogue

### Compatibility and API design

- Do not preserve backward compatibility by default. Breaking an API and
  updating callers is acceptable unless compatibility is requested. This is a
  decision rule for maintainers; it does not identify a syntax pattern by
  itself.
- Use named arguments when a call has multiple arguments of the same type and
  positional order does not make their roles clear. Ticket 6 mechanically
  requires names for every supplied repeated-type group except its exact
  framework catalogue and explicitly opted-out callees.
- Do not introduce constructor overloads solely to accommodate dependency
  injection or tests. Update the existing constructor and callers, or use a
  builder when several optional configurations justify one.

### Linear operations and conditions

- Keep each statement focused on one conceptual operation.
- An inline condition may have at most two simple checks when both concern the
  same value and express one invariant, such as a range.
- Split checks about different values into separate guards. Do not combine
  `&&` and `||` in one inline condition.
- Do not combine parsing, lookup, conversion, validation, or mutation inside
  one condition. Move repeated alternatives into a helper, loop, or another
  linear form.
- Do not nest one conceptual operation inside the arguments of another.
  Calculate, convert, look up, validate, format, and join before a method or
  constructor call. Direct values and expressions with at most one simple
  calculation operator may remain inline. Assign conditional expressions to
  named variables first.

### Pipelines and lambdas

- Linear LINQ and similar declarative pipelines are allowed. Prefer simple
  lambdas for their stages.
- A projection requiring several operations should use a block lambda with
  named intermediate values.
- Move logic with nested conditionals or several temporary values unused by
  the containing method to a local function. One intermediate value may stay
  in the containing method. Move reusable logic to a private method.
- Shape final collections explicitly, including named key and value
  selectors when relevant.

### Return decisions

- After a conditional early return, later decisions among return outcomes
  should use `if` or `switch` statements with explicit returns. A final
  predicate, nullable expression, or ternary that still chooses among outcomes
  violates the rule, regardless of return type.
- Returning a value already known to represent one outcome is allowed. A lone
  conditional return with no preceding conditional return is also allowed.
- For a final lookup that may be absent, test the result and return the found
  value in its own branch, then explicitly return the fallback. Match the
  direction of earlier branches that return found values.

### Sentinel returns

- Application-owned functions beyond private helpers should express absence
  or failure in the return type rather than returning a primitive sentinel
  such as `-1`.
- A private helper may use a sentinel. A primitive-returning caller must check
  the helper's result before forwarding its own sentinel explicitly.
- Signatures imposed by dependencies are exempt.

### Text handling

- Prefer spans for text inspection, slicing, and trimming. Accept and return
  spans when the string need not be owned and the lifetime permits it. Create
  a string when ownership is needed.

## Reference cases to verify

These examples come from the linked FindJobHelper branch and serve as
acceptance cases for the return rule.

- [`ApplicationIndexStore.Field`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/ApplicationIndexStore.cs#L200-L210)
  and [`GetField`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/ApplicationIndexStore.cs#L349-L359)
  return a ternary after an earlier guard. Both should warn.
- [`FolderOpener.ParseWslPathOutput`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/FolderOpener.cs#L194-L203)
  has the same shape and should warn.
- [`Program.IsUnsafeFileName`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/Program.cs#L419-L432)
  returns a predicate after two guards and should warn.
- The lone ternary in [`Program.cs`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/Program.cs#L313-L317)
  should not warn. The explicit final `if` and fallback in
  [`ExperienceDatabaseShadow`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/ExperienceDatabaseShadow.cs#L81-L105)
  should not warn either.
- The private `IndexFor` helper's `-1` fallback in
  [`ApplicationIndexStore`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/ApplicationIndexStore.cs#L363-L373)
  is allowed by the sentinel exception.

## First implementation: explicit return decisions

Diagnostic `CR0001` is a warning by default. It checks block-bodied methods,
accessors, local functions, and lambdas independently. A preceding top-level
`if` guard in the same body must have a branch that returns unconditionally.
The analyzer does not treat a return inside a nested function or deeper control
flow as a guard for the containing body. Generated code is excluded.

After such a guard, the final return is a candidate when it contains a
ternary, null-coalescing expression, boolean predicate or boolean-returning
call, or nullable-returning call. A plain `return value;` and a lone ternary
without an earlier guard are allowed. Switch expressions and more complex
control flow are deferred to later tickets. The analyzer reports a candidate
only when the code fix can rewrite it without changing evaluation order or
the number of times a subexpression runs.

The fix replaces the final decision with explicit `if` branches and returns.
It must preserve the original values, side effects, and comments. Analyzer
and code-fix tests cover positive cases, negative cases, generated code,
nested function boundaries, and fixes. The local package will also be tested
against the linked FindJobHelper branch without modifying that checkout.

Acceptance tests include:

- A final ternary returning a found value or `null` after a guard reports a
  warning and fixes to an explicit found-value branch and fallback.
- A final boolean predicate or boolean-returning call after a guard reports a
  warning and fixes to explicit `true` and `false` returns.
- A final `??` or nullable-returning call after a guard reports when its
  expression can be evaluated once and rewritten with an explicit fallback.
- The same patterns work in a block-bodied local function, accessor, and
  lambda, without a nested function's guard affecting its parent.
- A lone conditional return, a plain value return, generated code, and a
  switch expression do not report in this ticket. A conditional return inside
  deeper control flow also does not report in this ticket; it reports from
  ticket #2 onward through the nested return decision rules below.
- Fixes preserve comments and do not change the number or order of evaluations
  of calls and property accesses.

For nullable fallbacks, the fix returns the found value from an `if` branch and
leaves the fallback as the final return. For example, after an earlier guard,
`return value.Length == 0 ? null : value;` becomes
`if (value.Length != 0) return value; return null;` with normal C# block
formatting. The fix may invert the condition to keep this branch direction.

## Nested return decisions (ticket 2)

Diagnostics `CR0003` through `CR0006` report a return whose expression decides
among outcomes when it sits deeper than the function body's top level, for
example inside a `lock`, an `if` or `else` branch, a loop, `try`/`catch`/
`finally`, a `switch` statement case arm, or a nested block. Nesting alone
triggers the warning; a preceding guard is not required. `CR0003` covers
ternaries, `CR0004` null-coalescing expressions, `CR0005` nullable-returning
calls, and `CR0006` boolean predicates and boolean-returning calls. Returns
inside nested function bodies (lambdas, local functions, and anonymous
methods) are not affected by the containing body's scope, and switch-expression
returns are allowed style.

Each rule offers one canonical fix that rewrites the return in place into
explicit `if` statements and returns. The fix preserves evaluation order and
the number of times every subexpression runs, and all generated code is
formatted through the document's `.editorconfig` options. A ternary whose
branch throws, and a null-coalescing expression whose right side throws, are
reported and fix to a guard whose branch throws, followed by a return of the
remaining arm or the saved value. Generated local declarations inserted into
a switch section are wrapped in a block, because all sections of a switch
share one declaration space. Final-position decision returns stay governed by
`CR0001` alone; the nested rules never re-report them. Generated code and the
conservative skips of the first implementation (comments or directives inside
the decision, ref returns, and non-identity return conversions) still apply.

## Inline conditions (ticket 3)

`CR0200` reports mixed built-in Boolean `&&`/`||`; `CR0201` reports more than
two atomic checks; `CR0202` reports two simple checks with different proven
subjects; `CR0203` reports recognized producing/transformation/mutation combined
with validation; `CR0204` reports homogeneous chains of at least three resolved
framework predicates differing in one constant argument. Two repeated
alternatives remain allowed. Overlap selects the highest enabled reason in
this order: mixed, operations, alternatives, length, independent subjects.
Disabling one ID exposes the next enabled reason.

The analyzer covers if conditions, bool/var local initializers, Boolean returns,
expression-bodied methods/accessors/properties/local functions/delegate lambdas,
assignments, Boolean method/constructor arguments, ternary tests, while/for/do
tests, catch filters, switch when guards, and Boolean field/property initializers.
Nested function hosts are independent. Generated code is excluded.

Two actions share short-circuit evaluation and reached-leaf lowering: extract a
linear local function first, or expand into guards/statements. A private static
helper adapts member initializers. Filter and pattern variables may bridge to
helpers as stable exact-type parameters, with the call remaining in the filter
or guard. Loop helpers run at every original header/bottom test. For initializer
locals stay in a scoped replacement block. Out-variable and nullable flow may
require expansion alone. Unsafe individual rewrites are omitted. Fix All
replans sites and names sequentially. Fixes honor editorconfig formatting and
retain original call binding, conversions, grouping and evaluation order.

Recognition is conservative: bounds on one scalar, a protecting null guard,
and numeric IsFinite/IsNaN plus one bound are permitted two-check invariants.
Arbitrary same-subject predicates and two unconstrained variables do not prove
an independent invariant. Unknown atomic leaves count structurally but acquire
no operation category. Compound patterns are currently one atomic check.
The catalogue matches actual framework numeric/Guid parsing, Dictionary,
IDictionary and IReadOnlyDictionary TryGetValue, JsonElement.TryGetProperty,
scalar Convert, string trimming/replacement/predicates, and built-in casts or
mutations. It does not infer user method behavior from names.

Expression-tree hosts (including nested arguments inside trees), internal
comments/directives, unresolved/dynamic/nullable/custom logical operators,
unsafe/fixed context moves, labels/gotos, and changed scope/flow are conservative
safety boundaries. Omitted caller-information arguments prohibit affected moves;
explicit original caller constants remain supported. Params, ref/out/in and
location-sensitive argument expansion is omitted when faithful timing cannot
be established. A safe same-site helper remains available where possible.
Nested short-circuit and typed conditional operands lower producers only on
their reached branches; element-index producers keep the receiver and index
evaluation order. Original parentheses survive reinsertion, and fixes are
validated against formatted, reparsed source. Producing operations inside
unsupported lazy shapes, such as null coalescing or conditional access, are
individual conservative skips. Target-typed or throwing conditional operands
and moves that would copy a mutable value-type receiver also remain skips.
A flow-preserving whole-if expansion requires the diagnosed expression to be
that if's condition. A nested Boolean argument depending on the outer guard's
nullable flow is omitted when neither an independent helper nor a rewrite of
the selected argument is safe; it cannot borrow the outer condition's plan.
Frozen return diagnostics retain their classification and may overlap Boolean
returns: extraction leaves an existing return warning available; direct literal
return expansion resolves that same decision. Unrelated return warnings remain.

## Statement operations (ticket 4)

`CR0300` reports operations nested in resolved method, delegate or ordinary
constructor arguments. Count explicitly supplied source arguments: an inner
call/construction with at least one supplied entry is an operation, while
`Use(GetValue())` is allowed. Omitted optional/caller arguments, an implicit
reduced-extension receiver and an empty implicit params collection do not count.
Explicit named, static extension and params entries do count. Parameterless
Trim/ToString remain allowed here; inline-condition diagnostics stay independent.
Direct fields, property/index selections and built-in tests remain inline,
without a purity assumption. At most one supported built-in numeric calculation
is allowed, including original constant-folded syntax. Supported nonidentity
casts, ordinary string interpolation and consumed embedded mutation also qualify.

`CR0301` reports an embedded ternary argument or larger consumed value. Entire
named initializers and assignment right sides are compliant; lone direct
returns/arrows and switch expressions retain the frozen return policy.
`CR0302` reports more than one supported calculation in a standalone named value,
or a producer consumed by another calculation/conversion/formatting stage.
Delivery through an assignment, declaration or return is not a second operation.
One CR03 owner selects the highest enabled feasible reason in the order
CR0301, CR0300, CR0302. Distinct arguments remain distinct roots; safe inner roots
remain available when an outer rewrite is unsafe. Return and condition families
are analyzed independently.

Local-function extraction comes first; statement expansion follows when safe.
The helper contains named linear stages and its zero-argument invocation remains
at the original evaluation site. There is no hidden helper exemption after
saving the source. Typed branch-local assignments retain short-circuit and
ternary laziness. Expansion snapshots the receiver, earlier arguments and their
conversions, assignment location and later arguments in source order. Helpers
adapt loop headers, foreach collections, filters and switch guards without
changing reevaluation or continue timing. Stable unavailable filter/pattern/for
variables can bridge the whole site through exact typed parameters. Field and
auto-property initializers use a private static helper at their original site.
Constructor creation includes readonly structs and target-typed new. The pinned
BOM offsets, CsvField construction, third collection-expression Path element and
Math.Max score multiplication are acceptance cases.

Every reported site has a compiler-layer feasible strategy. The fixer verifies
formatted saved source, compiler diagnostics, original call occurrences,
parameter mapping and staged conversions, and selected/generated-root progress.
Fix All replans current roots and fresh names sequentially and is idempotent.
Analyzer feasibility never replaces a compilation per root.

Constant-required expressions (const declarations, case labels and constant or
relational pattern operands) cannot be staged and are excluded. Ordinary runtime
arithmetic still counts its original operators even when constant-folded.
Expression trees, attributes/base/this constructor initializers, invalid/dynamic
binding, ref-like/pointer/unsafe values, internal comments/directives and labels
or gotos are conservative boundaries. Individual actions are omitted for
mutable receiver copies, ref locations, escaping out-variable scope, narrowed
nullable dependencies, changed caller-information constants, unsupported lazy or
custom/lifted operations, and handler/FormattableString formatting. Expanded
params calls can stay unchanged in same-site helpers; staging a params call is
omitted where allocation timing cannot be proved. Explicit caller constants are
supported. Async operations and multi-hole interpolation containing producers
remain individual safety skips. No accepted host category is excluded wholesale.

Linear Enumerable/Queryable/ImmutableArray pipelines and string.Join over them remain
allowed. Conceptual grouping beyond these mechanical facts remains judgment:
property/index lookups, fluent business APIs, several assignments representing
one state transition, argument count, naming quality and user declarative APIs
are not diagnosed by line length, call counts or guessed method names.

## Pipelines, delegate stages and final shaping (ticket 5)

`CR0400` reports a structurally composed expression projection in a catalogued
delegate selector. Composition uses the existing operation facts: a producer
with explicitly supplied arguments inside another ordinary call, or a producer
consumed by a supported calculation/conversion/formatting stage. Direct shaping,
one simple stage and independent simple aggregate components stay allowed.
Each aggregate component is classified separately. Ordinary constructor
arguments, tuple components and supported settable object initializer values
can be linearized; unsupported individual transformations are omitted.

`CR0401` covers whole delegate stage blocks containing at least two ordinary
non-alias value declarations feeding their returns directly or transitively,
or a decision inside another decision's branch. One temporary, a lone guard,
an else-if sequence and nested callable implementation details do not qualify.
The fixer extracts a typed local function and prefers a method group when
scope and binding permit. Otherwise an invocation-local helper retains the
original lambda parameters and capture timing. A block projection action
keeps the named implementation in an invocation-local helper when placing
several temporaries directly in the lambda would introduce CR0401. Safe member
initializers use a private static helper. Neither action performs selector work
while constructing a lazy pipeline or changes materialization.

`CR0402` uses actual bound keySelector/elementSelector parameter names. Key-only
overloads receive an explicit identity element selector only through the paired
framework signatures documented in [the catalogue](pipelines.md). Comparer and
source expression order remain intact. Already named explicit selector calls,
ToArray/ToList/ToImmutableArray, query providers and unproved KVP/builder fast
paths remain allowed. Named arguments are sufficient; named selector functions
are optional for simple mappings.

All three are Readability warnings, excluded from generated code and controlled
by ordinary Roslyn severity, pragma and SuppressMessage configuration. CR0402
is independent of stage warnings; existing CR02/CR03/return reporting is unchanged.
Fixers validate formatted, saved and reparsed source, occurrence bindings,
argument mappings, conversions, original stage delegate binding and root
progress. Fix All reparses/replans each edit with fresh names and does not carry
validation annotations into a later pass. Framework definitions are resolved
once per compilation; analyzer feasibility never replaces a compilation.

Containing-method declaration chains are already compliant named linear steps.
Nested calls outside lambdas retain CR0300 ownership, including its parameterless
exemption. General method-region extraction, identifying semantic reuse and
selecting a private-method abstraction remain manual judgment. The bounded
automatic stage rule does not replace those parts of the coding standard.

For a genuine mathematical formula, use standard method/member
`System.Diagnostics.CodeAnalysis.SuppressMessage("Readability", "CR0302",
Justification = "Keep the formula intact for comparison with its derivation.")`
or a scoped `#pragma warning disable CR0302` / `#pragma warning restore CR0302`
around the intended function. Neighboring functions and other rule IDs remain
enabled. Real analyzer-driver tests verify both suppressions and restored scope;
there is no custom opt-out attribute or handwritten suppression matcher.

## Constructor overloads (ticket 7)

The constructor-overload policy remains a maintainer judgment without an
analyzer diagnostic. Review why an overload is being introduced: when its
sole purpose is dependency injection or test setup, update the existing
constructor and callers; use a builder when several optional configurations
justify one.

Roslyn exposes [current source, references and bound symbols](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-semantics#compilation),
which do not establish why an overload was added or whether it serves a genuine
production construction form. Constructor chaining, defaults, accessibility,
interface parameters and calls observed only in tests do not prove exclusive
test or injection intent. The same signatures and bodies can provide genuine
default or configurable behavior. Whether compatibility was requested also
requires maintainer judgment under the existing compatibility policy; it is
not an automatic exemption from the constructor-overload rule.
[DI constructor selection](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/overview#constructor-selection-rules)
and an [ActivatorUtilitiesConstructor attribute](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.activatorutilitiesconstructorattribute?view=net-10.0)
establish activation behavior, not the sole reason for introducing an overload.

Ticket 7 therefore adds no diagnostic or code fix. Existing rules still apply
independently to constructor calls and bodies, including named arguments.

## Delivery decisions

- Publish the source in the public `AntonC9018/CodingRules` repository.
- Package normal analyzer and code fix consumers, and test diagnostics and
  fixes. The first implementation can use direct package references.
- Implement explicit return decisions and their code fix in the first ticket.
  Track the remaining rule families as GitHub issues linked to this spec.
- After the first implementation passes its tests, identify the dependencies
  needed by a normal analyzer, add the reusable ones to the local
  `Anton.SourceGeneration.Sdk`, and consume that SDK from a local NuGet feed.
- The initial local dependency phase is complete. CI uses published
  SourceGeneration dependencies; [release setup](releases.md) describes the
  manually published GitHub release and exact tested package flow.

## Implementation tickets

[Ticket 1](https://github.com/AntonC9018/CodingRules/issues/1) implements
`CR0001` as described above. Follow-up tickets cover
[conditional returns nested beyond the top level](https://github.com/AntonC9018/CodingRules/issues/2),
[inline conditions](https://github.com/AntonC9018/CodingRules/issues/3),
[statement and argument structure](https://github.com/AntonC9018/CodingRules/issues/4),
[pipelines and lambdas](https://github.com/AntonC9018/CodingRules/issues/5),
[named arguments](https://github.com/AntonC9018/CodingRules/issues/6),
[constructor overloads](https://github.com/AntonC9018/CodingRules/issues/7),
[sentinels](https://github.com/AntonC9018/CodingRules/issues/8), and
[spans](https://github.com/AntonC9018/CodingRules/issues/9). The
compatibility policy stays documented without a diagnostic because no syntax
pattern can establish whether a change needed backward compatibility.

## Named argument roles (ticket 6)

CR0500 implements the accepted supplied repeated-type policy, all explicit call/constructor/attribute hosts, shipped exact-callee annotation and exact canonical declaration-ID registrations. The [named argument contract](named-arguments.md) records grouping, provenance, configuration transport/precedence, pre-warning correspondence proof, saved-source semantic/diagnostic validation and individual safety omissions. Every warning has an Add parameter names action; Fix All supports fresh document/project/solution replanning. Older families remain independent.
