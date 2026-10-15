# C# builder protocol enforcement research

Research snapshot: 2026-10-15. The question is whether existing C# tooling can prove that `Input` and `Output` are called before a serializer's `ExecuteAsync`, where the proposed API is:

```csharp
ScheduleSerializer.New(state, static (s, b) =>
{
    b.Input(s.Schedule);
    b.Output(s.OutputFile); // or b.Output(path)
    b.Hash(s.Hash);         // optional; default is empty
}).ExecuteAsync(cancellationToken);
```

The `ScheduleLib` checkout currently has `Serialize(schedule, outputFile, hash, cancellationToken)` and separate deserialize/add-to-builder APIs; this `New` callback API is a proposal, not a shipped API ([current source](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Model/ImmutableModels/Serializer.cs)).

## Finding

A compiler-enforced design option is to change the public shape so required values flow through a staged/type-state API, or to take them as required arguments at construction. The surveyed generators encode each step in the returned type, so `Build` is unavailable until required states are present. That model cannot directly express the shown callback: `b.Input(...)` and `b.Output(...)` are void/mutating effects on one ordinary builder instance, and C# does not update the static type of `b` after those statements.

For preserving this callback shape, a dedicated Roslyn analyzer is the practical route. No general, established, annotation-driven C# analyzer was found that accepts annotations declaring “these methods must have been called on this receiver before this terminal method” and verifies invocation history through arbitrary caller code. A small method-specific analyzer can reliably cover an explicitly constrained subset, especially straight-line callback bodies. This is a feasibility assessment, not a tested analyzer design. It should conservatively decline cases whose call history it cannot prove and retain runtime validation in `ExecuteAsync`.

## Reusable candidates

### TypedStateBuilder.Generator

[TypedStateBuilder.Generator 1.2.0 on NuGet](https://www.nuget.org/packages/TypedStateBuilder.Generator/1.2.0) is a published Roslyn incremental source-generator package whose NuGet metadata declares MIT licensing. Its declared feature is required-step checking in the generated API: required steps may be taken in flexible order, and the terminal build API exists only when all required states have transitioned to set. The package page gives a concrete valid chain and an invalid example where `Build()` is unavailable after omitting `SetEmail`; it also describes duplicate steps as unavailable through the typed wrapper. The repository README describes the generated API and its limits ([source repository](https://github.com/Georgiy-Petrov/TypedStateBuilder.Generator)). No package compilation probe or upstream test execution was performed in this survey; these are documented capabilities, not independently demonstrated acceptance coverage. Inspect the exact package source/license before reuse.

Applicability: good design precedent if the serializer API can become a generated chain such as `New(state).Input(...).Output(...).ExecuteAsync(...)`. It does not match `New(state, (s,b) => { b.Input(...); ... })`, where each setter call discards any new builder type. Source-generator setup also entails adopting its annotated builder model and API conventions. The README explicitly says it has no async build pipeline and blocks on async validators. Returning a Task from a terminal method may be a separate option, but was not verified; this limits direct suitability for ExecuteAsync. It also documents wrapper allocation per step and a shared mutable underlying builder.

### StepwiseBuilderGenerator

[StepwiseBuilderGenerator 4.0.0 on NuGet](https://www.nuget.org/packages/StepwiseBuilderGenerator/4.0.0) is also MIT-licensed and published as a source generator. It generates step interfaces / partial builders with explicitly ordered steps, allows default-value factories and branch paths, and its examples place `.Build(...)` after the final required chain step ([package README and version/license metadata](https://www.nuget.org/packages/StepwiseBuilderGenerator/4.0.0)). Its ordered API is a worse fit when Input and Output should be supplied in either order. It likewise does not analyze statement callbacks, and no exact analyzer diagnostic is involved.

### Annotation-driven invocation analysis

The closest explicit general-purpose prior art found is the Java [Checker Framework Called Methods Checker](https://checkerframework.org/manual/#called-methods-checker): it supports `@CalledMethods` receiver types and `@RequiresCalledMethods` preconditions and documents builder required-setter checking. This is not a C# analyzer or a reusable .NET dependency; it establishes that annotation-driven typestate analysis is a known approach, not that the needed analyzer already exists for Roslyn.

The published C# [MustUseRetVal 0.0.2](https://www.nuget.org/packages/MustUseRetVal/0.0.2) analyzer uses `[MustUseReturnValue]` to report discarded return values. Its own example applies the attribute to fluent step methods to catch a chain whose terminal `Write()` is omitted. It does **not** prove that `Input` or `Output` was invoked before a later call: the proposed setters can return `void`, and an analyzed callback that invokes them is not the discarded-return-value case. Package metadata calls out its MIT license and no package dependencies ([NuGet description, examples and metadata](https://www.nuget.org/packages/MustUseRetVal/0.0.2); [upstream source](https://github.com/mykolav/must-use-ret-val-fs)). It can catch a discarded fluent expression missing its terminal call. Assigning the value to a local is enough to satisfy that check; it does not ensure eventual execution.

The MustUseRetVal documentation also accepts JetBrains `[MustUseReturnValue]`; the same semantic boundary applies: it can request that a returned value be consumed, not that particular setters ran. `System.Diagnostics.CodeAnalysis.MemberNotNull` and related nullable-flow attributes describe member null-state after a method returns; they do not record a history of calls. C# `required` members enforce initialization at object creation sites, not setter-method invocation history ([C# required proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-11.0/required-members.md); [nullable analysis attributes](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis)). Required properties on an options/configuration object could still encode the data requirement more directly.

## What a callback analyzer can and cannot prove

The analyzer can identify `ScheduleSerializer.New` and `ExecuteAsync` by symbol identity and inspect the callback argument's operation tree / control-flow graph. An analyzer attached through the consumer package can do this even when the serializer implementation is only available as a referenced assembly, because the call and lambda syntax are at the consumer's call site. The analyzer needs the API's symbols (or an explicit annotation/configuration contract) to recognize the factory, builder receiver, required setter methods, output overloads, and terminal method.

| Case | Safe boundary for a first analyzer |
|---|---|
| Straight-line callback | Track calls to required methods on the callback's builder parameter through the block; report at the terminal execution if `Input` or any `Output` overload is absent. |
| Branching callback | A control-flow graph can combine facts by intersection at joins: a requirement is satisfied only if it was invoked along every normal path that reaches callback completion. For example, `if (condition) b.Input(...);` does not satisfy Input. A branch where both sides call Input does. Analyze normal fallthrough; a thrown path does not reach ExecuteAsync. |
| Aliases | A limited points-to rule can recognize direct local aliases such as `var x = b; x.Input(...)`. It should not assume arbitrary fields, returned references, captured variables, or alias mutation identify the same builder without analysis. |
| Static/state lambda | `static` limits capture, and the first state argument determines values; neither changes the need to prove operations on the builder parameter. A local function or nested callback should count only if the analyzer explicitly follows it. |
| Helper calls | A call like `Configure(b)` cannot be assumed to perform required setters from its name or body. Supporting helper methods needs interprocedural summaries/contracts and handling recursion, virtual dispatch, and source unavailable in referenced assemblies. The safe initial rule should require setters in the callback itself or treat unknown helper calls as “cannot prove,” not as satisfied. |
| Repeated/conflicting setters | Invocation-history analysis can count a setter as invoked, but it does not establish intended value semantics. Repeated `Output` calls may overwrite each other or conflict. Decide whether duplicates are accepted, diagnosed separately, or given last-call-wins behavior; do not infer a duplicate prohibition from “required.” |
| Output overloads | Treat `Output(path)` and `Output(file)` as alternatives satisfying the one Output requirement, using resolved method symbols rather than text names. |
| Hidden source / external caller | The analyzer can inspect a call-site callback in the current compilation even if serializer code is in a package. It cannot prove callback behavior hidden in a referenced helper, reflection, dynamically dispatched code, or code compiled without the analyzer. |

Important limitation: reaching `ExecuteAsync` after the callback returns is only a static claim under the analyzer's recognized language subset. Users can suppress diagnostics, omit the analyzer package, or route configuration through unanalyzed helpers. Runtime checks remain necessary for robustness and for values that can still be invalid despite the required calls. Unknown calls that reset or otherwise mutate the builder must invalidate assumptions. A setter that throws and is caught does not establish successful configuration. Struct builders, ref parameters, and copies also require distinct handling: a call on a copy need not configure the instance later executed. Whether the callback runs synchronously exactly once is part of the API contract, not something inferred merely from its signature.

## Recommendation

If the API can change, make required data structurally explicit: for example, have `New` take schedule and output as required parameters / a required configuration value, with hash optional, or expose staged methods whose returned types encode completed steps. This obtains compiler-enforced completeness and avoids analyzing callback effects. The existing generator candidates are evidence for this pattern, but neither is a drop-in for the proposed callback, and neither generator has been validated against this callback/async API in a compilation probe.

If retaining the exact callback DSL is the priority, implement a small opt-in analyzer for this specific serializer protocol rather than adding a general contract framework. Start with direct calls and a Roslyn CFG over callback block lambdas; handle both Output overloads and branch intersection. Specify duplicate-setter semantics. Make the contract reusable through explicit opt-in symbols/annotations or configuration; ScheduleLib should supply acceptance examples, not hard-coded project semantics. Report only cases proven incomplete within the supported syntax and avoid claims about unknown helpers. Keep an execution-time `Input`/`Output` guard as the authority for invalid runtime states. A broad annotation-driven interprocedural analyzer would be substantially more work than this API-specific rule and no reusable C# implementation with shipped tests was verified in this survey.

## Sources

- [TypedStateBuilder.Generator NuGet package, usage, limits, license and current version](https://www.nuget.org/packages/TypedStateBuilder.Generator/1.2.0)
- [TypedStateBuilder.Generator source repository and README](https://github.com/Georgiy-Petrov/TypedStateBuilder.Generator)
- [StepwiseBuilderGenerator NuGet package, examples, license and versions](https://www.nuget.org/packages/StepwiseBuilderGenerator/4.0.0)
- [MustUseRetVal NuGet package and fluent-chain example](https://www.nuget.org/packages/MustUseRetVal/0.0.2)
- [MustUseRetVal source repository](https://github.com/mykolav/must-use-ret-val-fs)
- [Checker Framework Called Methods Checker](https://checkerframework.org/manual/#called-methods-checker)
- [C# `required` proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-11.0/required-members.md)
- [Microsoft nullable analysis attributes reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis)
- [ScheduleLib current serializer source](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Core/Model/ImmutableModels/Serializer.cs)

## Integration boundary

No package was added. An analyzer or generator can be delivered through the
consumer entry point only after checking its actual analyzer assets, Roslyn
compatibility, transitive delivery, and required annotation/runtime references.
The SDK dependency work already showed that a plain project/package reference
is not proof that third-party analyzer DLLs reach consumers. A source generator
would generate the staged API in the library that defines the builder; an
analyzer would instead inspect consumer call sites. These roles should not be
confused when deciding what to put in Anton.CodingRules.
