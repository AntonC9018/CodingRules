# Reviewed computed arguments and lookups

ScheduleLib slice 07 implements the non-message A6/A7/A8 cases with existing
Readability warnings CR0300, CR0301 and CR0203. CR0302 retains its composition
policy and also recognizes the new metadata producer when consumed by a calculation. No public diagnostic ID or package
configuration default changes. Generated code, ordinary Roslyn severity options,
EditorConfig, pragma and SuppressMessage conventions remain in force. CR0302's
initializer correction at `7b9a4b2` and CR0001's safe-fix contract remain intact.

## Original-source evidence

All source references below pin ScheduleLib `12e601e7b62511bdecc44e280407fee12147b230`,
the reviewed source in [ScheduleLib #229](https://github.com/AntonC9018/ScheduleLib/issues/229).
The consumer fixture retains each evaluated call shape with framework-only domain
doubles; it does not reference or modify the downstream repository.

| Case | Original source | Finding and resulting boundary |
| --- | --- | --- |
| Constructed streaming exception | [CalendarSync.cs:161](https://github.com/AntonC9018/ScheduleLib/blob/12e601e7b62511bdecc44e280407fee12147b230/src/MainCli/CalendarSync.cs#L161) | Existing CR0300 names the exception; its original message and inner-exception identity remain intact. |
| Any/ternary inside result/exception construction | [CalendarSync.cs:165](https://github.com/AntonC9018/ScheduleLib/blob/12e601e7b62511bdecc44e280407fee12147b230/src/MainCli/CalendarSync.cs#L165) | Existing CR0301 names the selected exit value without eagerly evaluating either branch. |
| Directory selection | [CliRuntime.cs:44](https://github.com/AntonC9018/ScheduleLib/blob/12e601e7b62511bdecc44e280407fee12147b230/src/MainCli/CliRuntime.cs#L44) | CR0300 names GetFolderPath inside the fallback branch. Its helper remains inside the original Path.Combine argument evaluation. |
| Split/key/lookup construction | [CliSettings.Removals.cs:260](https://github.com/AntonC9018/ScheduleLib/blob/12e601e7b62511bdecc44e280407fee12147b230/src/MainCli/CliSettings.Removals.cs#L260) | CR0300 stages split/index/key construction in order. Target-typed new keeps its construction conversion; later local copies validate their own conversion. |
| Concat/ToArray ternary branches | [CliSettings.Writes.cs:209](https://github.com/AntonC9018/ScheduleLib/blob/12e601e7b62511bdecc44e280407fee12147b230/src/MainCli/CliSettings.Writes.cs#L209) | CR0301 additionally reports a named/direct ternary when either branch contains more than one bound catalogued pipeline call. The fix makes the branch decision explicit and keeps each linear pipeline together. |
| Reflection/service condition | [CliSettings.Removals.cs:242](https://github.com/AntonC9018/ScheduleLib/blob/12e601e7b62511bdecc44e280407fee12147b230/src/MainCli/CliSettings.Removals.cs#L242) | CR0300 additionally counts resolved System.Type.GetGenericArguments as metadata computation despite having no source arguments. CR0203 counts framework IServiceProvider.GetService as a lookup producer and supports its constant-null pattern, naming the nullable service result before testing it. |

The service catalogue checks the resolved System.IServiceProvider member and
framework identity (including System.ComponentModel on .NET and netstandard
facades). Application-owned lookalike methods and source imitations remain
outside this addition. System.Type.GetGenericArguments likewise requires a resolved framework
member, with a source-imitation regression; there is no general parameterless-call or receiver-chain ban.

## Evaluation and fix availability

Local-function extraction remains the first action. Statement expansion is
also offered when the whole reached evaluation site can be safely staged.
Helpers retain their original invocation position, so they preserve receiver
and earlier argument evaluation, short-circuiting, exceptions and handler gating.
All actions still validate formatted/reparsed saved source, compiler messages,
call occurrences/binding, staged conversions and actual diagnostic progress.

The reviewed MakeGenericType and Path.Combine calls use expanded params arrays.
The existing conservative boundary prevents hoisting their element computations
ahead of the implicit array allocation. Independent same-site helpers name those
computations without moving them across that allocation. The reflection example
therefore needs successive CR0300 passes to name the generic arguments/element
and constructed service type; CR0203 names the service result within the guarded
branch. Both condition actions preserve a skipped lookup, one reached type query,
one reached service call, and index/provider exception timing. A single expansion
of the entire original params chain is deliberately unavailable.

Pattern input lowering retains constant-required pattern operands. Ref-like,
await, expression-tree, caller-information, comment/directive, unsupported
conversion and escaping-variable boundaries remain conservative safety omissions.
No new manual-only diagnostic is introduced. In particular, comments and span
lifetimes are preserved by leaving unproved sites unchanged; silence at such a
site does not claim style approval.

The downstream rollout also exposed repeated proof annotations on a call inside
a null-forgiving expression, such as `Use(Path.GetDirectoryName(file)!)`.
The validator treats identical annotation data as one proof instead of throwing
on its cardinality. Conflicting data still declines the action, and call
occurrence counts, binding, conversion and saved-source progress checks remain
required. Runtime regressions verify extraction with both null and non-null
results; other actions remain conditional on their existing safety proofs.

## Accepted cases and message scope

The shared accepted fixture preserves direct/nested initializer assignments,
empty collections, simple factories (TimeSpan.FromMinutes and default time slots),
ordinary simple conditional values, standalone linear LINQ pipelines, and the
reviewed GetRequiredService<ISlugProvider>().SlugMap receiver chain from
[AppTasks.cs:157](https://github.com/AntonC9018/ScheduleLib/blob/12e601e7b62511bdecc44e280407fee12147b230/src/MainCli/AppTasks.cs#L157).
The accepted lexer ListStringBuilder Append interpolation retains its two holes
and ReadOnlySpan input. Unit regressions retain the separate initializer tests
introduced for #19, including genuine mutations and combined initializer RHSs.

This slice does not add a general interpolation rule, extract the reviewed
reporting messages into string locals, or redesign structured-message/custom
exception APIs. A computation inside a handler hole can get its own safe helper
at that hole; it is never moved ahead of handler construction or earlier appends.
Runtime tests cover disabled constructors, stopping AppendLiteral, and a fully
reached nested computation. Structured reporting remains slice 15 and the
existing agent-guidance/ADR policy.

## Verification and package offering

`ReviewedComputationTests` reads the same Findings.cs and Accepted.cs shipped in
`CodeFixTests/Consumers/Computations`. It verifies six exact CR03 locations, the
CR0203 location, compiled saved actions at every finding, runtime equivalence,
fix convergence and accepted-case silence. Runtime tests separately check lazy
pipeline enumeration/failure, guarded reflection queries and provider failures,
nullable service-result naming, nested lookup receiver/constructor order and JSON
failures, and handler constructor/append gating.

CI registers `build/verify-computation-consumer.py artifacts/release`. The gate
requires an existing candidate package and never repacks it. It checks all seven
exact warning locations, severity-none/error and warnings-as-errors transport,
generated/accepted source stability, packaged fixing convergence, identical runtime
output, and a stable second formatting pass. Its execution is intentionally
reserved for the single final candidate after all tickets; this slice validates
the shared fixture in process without running that gate.

## Downstream usability follow-up

The 2026-10-24 ScheduleLib rollout deliberately holds the bulk CR0300/CR0301/
CR0302 rewrite. A saved-source probe of `CliSettings.Removals.cs` verifies that
both packaged actions run, but exposes a readability problem: extraction makes
many generic `CalculateValue` helpers, while statement expansion can copy a
fresh `operationValue` into another local and substantially expand short-circuit
conditions. These are distinct from the fixed proof-annotation crash. Preserve
the diagnostics while improving naming and eliminating unnecessary captures;
warning convergence alone is insufficient evidence of a good downstream change.
