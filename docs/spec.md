# Coding rules analyzer spec

Status: accepted for implementation.

The [ScheduleLib follow-up](schedulelib-follow-up.md) records additional reviewed
examples, the coverage-based completion criterion, and pending policy changes,
including separating message data from rendering. Those changes do not imply
that the currently published package already implements them.

## Selected formatting implementation (ScheduleLib slices 02/03)

The consumer package enables upstream RCS0042 for compact bodyless accessors
and RCS1016 for multiline expressions converted to blocks. Simple single-line
arrows and blocks remain accepted. CR0800 moves only already-wrapped trailing
`&&`/`||` to the continuation's beginning, skipping comments/directives that
would move. RCS1016 conservatively excludes arrow-token comments and custom async
return contracts because the upstream fixer is unsafe for those shapes. The
package replaces the common upstream fixer assembly with a CodingRules RCS1016
provider that independently declines these shapes, including in CLI hosts that
bypass diagnostic suppressors.
Other upstream rules and broader binary-operator policies remain disabled.
CR0801 requires every nonempty object initializer, including a single-member
initializer, to place its members on separate lines. CR0802 places a sole
positional constructor argument adjacent to an invocation's opening parenthesis.
Record `with` initializers also require a new-line block and trailing comma,
including a single member. Empty object initializers and already adjacent
constructors remain accepted. Comment-separated argument
gaps, directives, invalid syntax and other argument policies are conservative
boundaries. These layout fixes preserve initializer values and implicit
caller-information constants across the final formatted document; affected
layouts remain diagnosed without an automatic fix. Fix All validates edits
sequentially. Operation diagnostics remain independent; ordinary initializer
assignments are accepted as specified below for #19.
[Packaged formatting](formatting.md) defines the precise boundaries,
formatter and CLI configuration, license provenance and verification workflow.

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

### Duplication and semantic distinction

- Review matching computations as candidates for reuse. Matching syntax does
  not establish that the computations represent the same domain concept.
- Factor a genuinely shared operation into a named helper; keep semantically
  distinct operations separate when their meaning or reasons to change differ.
- Agents must make that judgment before acting on a duplication diagnostic.
  The [agent guidance](agent-coding-rules.md) records the review expected in
  this repository and in the intended consumer integration. CR1300 identifies
  the reviewed repeated timestamp formatting as a manual duplication candidate;
  the [recognition boundary](repeated-formatting.md) preserves semantic review.

### Return decisions

- After a conditional early return, later decisions among return outcomes
  should use `if` or `switch` statements with explicit returns. A final
  predicate, nullable expression, or ternary that still chooses among outcomes
  violates the rule, regardless of return type.
- Returning a value already known to represent one outcome is allowed. A return
  ternary requires explicit branches even without a preceding conditional return.
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

Ticket 9 implements `CR0700` for immediate Length and non-null constant ordinal
equality inspection of genuine parameterless framework string trims. The
[span assessment](spans.md) records the exact available-API catalogue, safe
synchronous helper rewrite and manual ownership/signature/slicing boundaries.

## Tuple data implementation (ScheduleLib slice 04)

CR0900 implements the settled Q15/Q16 cases as a manual Readability warning:
ordinary tuple method results, ordinary/local method tuple inputs, and stored
tuple elements in arrays, List and ReadOnlySpan. Method results and inputs also
include IEnumerable of tuples. The [tuple data contract](tuple-data.md) records
the exact catalogue and regression evidence. Private ordinary methods remain
included; local-function result declarations are excluded. A collection used by
a local function is not thereby exempt. Deconstruction is not a violation.
Purely local scalar tuples/swaps and other uncatalogued shapes remain outside
the settled detection boundary. Domain naming and API/caller migration require
manual review; CR0900 provides no automatic fix.

## Contract parameter implementation (ScheduleLib slice 05)

CR1000 is a manual Readability warning on locally owned interface methods,
virtual/abstract methods, delegate types and abstract-class constructors with
more than three configuration parameters, counting context but excluding a
trailing framework CancellationToken passed by value. It reports at the
owned declaration; implementations and overrides do not repeat the finding.
Abstract primary constructors are included. Externally imposed signatures are
exempt; ordinary concrete constructors and nonvirtual instance methods remain
outside the count rule. Static functions such as the reviewed Finish/Edit shapes
remain allowed by count. Static abstract/virtual interface members still define
owned contracts. The [contract parameter boundary](contract-parameters.md)
records exact ownership, regression evidence and the package gate. Grouping is
an architectural decision with no automatic fix. Overload/builder and repeated
plumbing policies remain independent.

## Overload configuration implementation (ScheduleLib slice 06)

CR1001 is a manual Readability warning requiring a configuration builder for
methods with more than one parameter and an overload alternative, including
static methods. The approved pair has the same declaring type, name, staticness,
generic arity and parameter count, exactly one differing resolved parameter type,
and matching ref-kinds, params, optional and default presence throughout.
Parameter names may differ. Differing counts and multiple type differences stay
outside initial coverage; one-parameter Parse and builder Output overloads remain
allowed. The [overload configuration boundary](overload-configuration.md) records
symbol comparison, declaration locations, accepted cases and package evidence.
API migration has no automatic fix. Required builder steps remain a manual agent
check with runtime validation; the RequiredStep proposal stays deferred. Broader
repeated plumbing still requires semantic review independent of overload count.

## Reviewed computations (ScheduleLib slice 07)

CR0300/CR0301 cover the non-message A6/A7/A8 constructions and lookup inputs;
CR0203 additionally recognizes the exact framework IServiceProvider.GetService
producer, including its null constant pattern. It names the nullable service
result before testing it, preserving the original short-circuited branch.
Parameterless System.Type.GetGenericArguments is the reviewed metadata exception
to CR0300's ordinary parameterless exemption. CR0301 also names ternaries whose
branches compose catalogued pipeline calls. CR0302 retains its composition
policy and also uses the shared metadata-producer fact. No diagnostic ID is
added; the original CR0001 contract stays unchanged. The
[computed-operation boundary](computed-operations.md) records provenance,
same-site params helpers, safety omissions, accepted cases and shared package
fixtures. Final candidate package execution is deferred until all tickets.

## Reviewed compound guards (ScheduleLib slice 08)

CR0201/CR0202/CR0203 cover the reviewed config-result, JSON lookup,
cancellation/authentication and numeric HTTP-status examples. CR0202 resolves
simple type tests and single-constant typed property-pattern subjects. CR0203's
flow expansion supports terminal throw guards; its exact reviewed six-status
catch filter extracts a named predicate using HttpStatusCode members while
keeping evaluation inside `when`. No new diagnostic is introduced. The
[compound-condition boundary](compound-conditions.md) records original-source
provenance, exact recognition, accepted invariants, manual review cases and
packaged positive/accepted/runtime regression evidence.

## Primitive sentinel implementation (ticket 8)

The accepted first version proves exactly `int -1` absence from resolved
String/List<T> IndexOf/LastIndexOf, bounded stable SZ-array searches and
private/local identity-forwarding chains. `CR0600` reports one declared
non-private member's return type when a normal sentinel outcome remains
possible; API redesign is manual. `CR0601` reports a locally fixable unchecked
private/local helper result and adds an explicit guard at its original return.
Success-only paths, including throwing on absence, remain allowed. CR0600 owns
an exposed host while enabled; severity-none exposes feasible CR0601 sites.
Actual external interface/override/fixed callback contracts are exempt. All
earlier diagnostic contracts remain independent. [Sentinel documentation](sentinels.md)
records the full evidence, visibility, flow, safety and compilation boundaries.

## String concatenation implementation (ScheduleLib slice 10)

ScheduleLib slice 10 implements `CR1100` for runtime built-in string-only
concatenations. The [interpolation contract](string-concatenation.md) records
the reuse probe, original A10 calls, explicit invariant conversion, static
string binding, handler timing and conservative accepted cases. The shared
unit/consumer fixture and final package test are registered; package execution
is deferred until the stable candidate after all tickets. Broader inline-string
and structured-message policy remains separate and unresolved.

## Unchanged assignment implementation (ScheduleLib slice 11)

CR1200 covers A13/Q13's reflection null preservation and direct current-value
branches for stable local/parameter/current-instance-field storage. Supported
statement, return and property/accessor arrow hosts become explicit conditional
updates with required returns. The [assignment contract](unchanged-assignments.md)
records exact proofs, setter/receiver/alias exclusions, saved-source validation,
and shared positive/accepted/runtime evidence. Package execution remains reserved
for the final stable candidate.

## Concrete class sealing implementation (ScheduleLib slice 13)

CR1400 reviews non-abstract classes for sealing, including public types,
exceptions, virtual members and existing inherited test seams. Adapted MIT
MA0053 source supplies candidate/framework selection; its general inheritor
exclusion is removed to cover A16. The [sealing contract](sealed-classes.md)
records the narrow non-generic attribute/type-forwarding generic variant
exception, concrete external constraints, warning/configuration conventions,
and shared positive/accepted fixtures. Sealing and replacement seam design are
manual; no automatic modifier insertion is offered. Package regression execution
is reserved for the final stable candidate after all tickets.

## Consumer agent guidance (ScheduleLib slice 14)

Each `Anton.CodingRules` version includes the self-contained agent rules, with an
explicit `ExportCodingRulesAgentGuidance` target and a sample repository read
directive. The [consumer import contract](consumer-agent-guidance.md) explains
discovery, updates and preservation of unrelated instructions. A package reference
alone does not cause an agent to read them. The rules record semantic reuse,
repeated plumbing, manual builder completeness, structured reporting and exception
context, plus accepted multiline literals, lexer interpolation and file-glob
construction. Universal interpolation, blanket ordinary-instance grouping and
RequiredStep analysis remain undecided/deferred. No diagnostic, annotation,
generator or downstream ScheduleLib refactor is introduced by this slice.

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
  should warn even without an earlier guard. The explicit final `if` and fallback in
  [`ExperienceDatabaseShadow`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/ExperienceDatabaseShadow.cs#L81-L105)
  should not warn either.
- The private `IndexFor` helper's `-1` fallback in
  [`ApplicationIndexStore`](https://github.com/AntonC9018/FindJobHelper/blob/0f579ef650d0f06c7bb04ff7c32b8daf97b65986/src/FindJobHelper.WebUi/ApplicationIndexStore.cs#L363-L373)
  is allowed by the sentinel exception.

## First implementation: explicit return decisions

Diagnostic `CR0001` is a warning by default. It checks block-bodied methods,
accessors, local functions, and lambdas independently. A final top-level return
ternary is a candidate even without an earlier guard. For other compound final
returns, a preceding top-level `if` guard in the same body must have a branch
that returns unconditionally. The analyzer does not treat a return inside a
nested function or deeper control flow as a guard for the containing body.
Generated code is excluded.

After such a guard, the final return is also a candidate when it contains a
null-coalescing expression, boolean predicate or boolean-returning call, or
nullable-returning call. A plain `return value;` is allowed. Switch expressions
and more complex control flow are deferred to later tickets. The analyzer reports
a candidate only when the code fix can rewrite it without changing evaluation order or
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
- A lone return ternary reports and fixes to explicit branches without requiring
  an earlier guard. A plain value return, generated code, and a switch expression
  do not report in this ticket. A conditional return inside
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
ScheduleLib slice 07 additionally treats resolved System.Type.GetGenericArguments
as a metadata calculation even without source arguments; unrelated parameterless
factories and service receivers keep their exemption.
One simple directly constructed argument may remain inline per containing call,
including explicit or target-typed `new(...)` alongside other nonconstructed
arguments. More than one direct construction retains CR0300. The allowance does
not hide nested constructors, producer calls, conditional values or numeric
calculations inside the constructor. Existing parameterless and initializer
boundaries remain unchanged.
Direct fields, property/index selections and built-in tests remain inline,
without a purity assumption. At most one supported built-in numeric calculation
is allowed, including original constant-folded syntax. Supported nonidentity
casts, ordinary string interpolation and consumed embedded mutation also qualify.

`CR0301` reports an embedded ternary argument or larger consumed value. Entire
named initializers and assignment right sides are compliant except when a
branch composes more than one catalogued pipeline call (for example
Concat/ToArray). Those branch computations receive CR0301 and become explicit
lazy branches; ordinary linear pipelines remain allowed. Simple direct
returns/arrows and switch expressions retain the frozen return policy.
`CR0302` reports more than one supported calculation in a standalone named value,
or a producer consumed by another calculation/conversion/formatting stage.
Delivery through an assignment, declaration or return is not a second operation.
Object-initializer member assignments likewise do not consume their assignment
results. Direct values, empty collections, parameterless factories and calls
such as `TimeSpan.FromMinutes(90)` remain allowed, including nested initializers.
Combined operations on initializer right-hand sides are still analyzed; there
is no initializer-specific EditorConfig option.
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
ScheduleLib slice 09 adds shared unit/consumer evidence for the pinned A9
target-typed return and ordinary construction, using the existing rule unchanged.
The four string roles receive their bound names; the unique Boolean remains
positional. Scope-expression extraction remains a separate operation concern.

## Follow-up API and formatting decisions (2026-10-24)

- CR1500 requires explicit implementation of non-generic IEnumerable/ICollection/
  IList members when a type implements generic IEnumerable<T>. Members shared
  with a generic interface (such as Count or Clear) retain their generic API.
  Caller migration is manual; merely changing accessibility can break callers.
- CR0803 requires braces for embedded if/else/loop/using/lock/fixed statements,
  preserving else-if chains. CR0804 requires separation after a cast; package
  formatting specifies `csharp_space_after_cast = true`.
- CR1001 accepts builder declaring types and extension receivers, recognizes
  configurable suffixes and `[ConfigurationBuilder]`, excludes the extension
  receiver from the count, and permits one effective parameter. See
  [builder configuration](overload-configuration.md).
- CR0500 accepts Boolean Equals/EqualsN operations and supports exact external
  registrations via assembly attributes or MSBuild. See [named arguments](named-arguments.md).
- CR1600 requests typed read-only exception context. Its opt-in fixer generates
  an internal sealed subtype of the original framework exception, captures simple
  immutable local/parameter values and eagerly renders the original template
  (including alignment/format strings). It changes exact runtime exception type;
  individual API review is required. No Fix All is offered. Custom exception
  types, side-effecting holes and mutable formatting objects remain manual.


### Explicit external reflection signatures

A delegate whose parameter list is imposed by an external member can use
`[ExternalSignature(typeof(ExternalType), "MemberName")]` from CodingRules.Shared.
CR1000 retains ordinary owned-contract findings and accepts only a named
external metadata owner through this marker. Reflection compatibility and the
reason for retaining the signature remain a manual review; the attribute does
not provide a blanket exemption for delegates or multi-parameter adapters.

## Review refinements — 2026-10-26

- All nonempty object initializers expand, including a single member.
- A fluent chain of at most two calls may remain on one line when the full line
  is at most 90 columns. Longer chains wrap. Chopped ordinary argument lists put
  every argument below the opening parenthesis, including the first. Preserve
  the lambda exception and keep a sole positional constructor adjacent to its
  outer call, chopping the constructor's own arguments below `new(` when needed.
  The remaining analyzer/fixer coverage review is tracked in
  [CodingRules #36](https://github.com/AntonC9018/CodingRules/issues/36).
- Return ternaries require explicit if statements even without an earlier guard.
- CR0205 reports else after a terminating return/throw branch. Fixes preserve
  declaration scopes and decline caller-expression-sensitive edits.
- Boolean predicate extraction emits early returns, retaining short-circuit
  evaluation and built-in operator meaning; it does not introduce a mutable
  scratch-variable tree. CR0303 extraction removes redundant else branches.
