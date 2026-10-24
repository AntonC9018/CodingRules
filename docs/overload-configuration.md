# Configuration builders for overload alternatives (CR1001)

CR1001 is a Readability warning for a method with **more than one effective
parameter** and a resolved overload alternative. This implements
[CodingRules #25](https://github.com/AntonC9018/CodingRules/issues/25) and
[Q19](schedulelib-follow-up.md#overload-scope-and-usage-context-q19q20).
Two parameters already trigger it, including on static methods. The separate
CR1000 parameter-count rule and its contract ownership exceptions do not filter
this rule.

This is a **manual finding**. The diagnostic asks for a configuration builder;
choosing its API, preserving behavior, and migrating callers require architectural
review. There is no automatic code fix or Fix All action.

## Approved overload-alternative boundary

Two methods form an alternative pair only when all of these hold:

- They have the same declaring type, method name, staticness and generic arity.
- They have equal effective parameter counts, greater than one. For extension
  methods the leading `this` receiver is excluded from both counts and from
  pairwise comparison. Ordinary method parameters are all counted.
- Exactly one resolved parameter type differs. Every other parameter type matches.
- All corresponding parameter ref-kinds (`ref`/`out`/`in`, including ref readonly),
  `params` flags, optional flags and explicit-default presence match, including
  at the position with the alternative type.

Parameter names may differ. Default **values** may differ; their presence must
match. Generic method type parameters are compared by ordinal after substitution,
so `Save<T>(List<T[]> input, Stream output)` and
`Save<U>(List<U[]> values, string path)` qualify. Type comparison uses resolved
Roslyn symbol equality, ignoring nullable annotations and spelling aliases. Tuple
element labels are ignored recursively, including tuples inside arrays, generic
arguments and containing types; tuple syntax and its underlying `ValueTuple`
form compare equally. Generic method parameters still compare by ordinal.

The test is pairwise. Each qualifying method identifier receives one diagnostic,
even if several peers qualify. A pair with differing arity or with more than one
type difference stays outside initial coverage. Membership in some other valid
pair can still produce a finding on either declaration.

```csharp
static void Save(object input, Stream output) { } // CR1001
static void Save(object source, string path) { }  // CR1001
```

The analyzer examines method declarations in the current compilation, including
private, interface and implementation methods. Explicit interface implementations
are skipped. Partial types share their resolved declaring type; partial method
implementation parts do not repeat their definition's finding. A base/derived pair
or same names on unrelated types never qualifies. Inherited and referenced methods
are not added to a type's declared overloads.
Constructors, operators, indexers, accessors, local functions and lambdas are outside
this method-declaration boundary.

Calls do not create findings: resolved framework calls, unresolved overload
candidates and dynamic calls are not used to infer families. Malformed declarations
and unresolved parameter types (including nested type arguments) are conservative
skips. The analyzer runs concurrently and uses Roslyn's generated-code exclusion.
Generated declarations receive no findings; their signatures can still be peers
of a hand-written declaration, just as a suppressed peer remains part of its type.

## Accepted cases and independent manual review

One-effective-parameter `Parse(string)` / `Parse(ReadOnlySpan<char>)` alternatives
and builder `Output(Stream)` / `Output(string)` alternatives remain allowed. A
method with no alternative receives no CR1001 finding regardless of its parameter
count. Different effective counts, staticness, generic arities, two differing
types, or mismatched ref/params/default presence remain outside this initial
detection boundary.

A method is treated as already using a configuration builder when any effective
parameter has a recognized builder type. By default, names ending in `Builder`,
`Descriptor` or `Configurator` match using ordinal, case-sensitive suffix
comparison. Generic type arity is ignored, so
`NodeDataBuilder<LessonTopicsConfig>` matches `Builder`. The package exposes the
exact suffix list through the compiler-visible MSBuild property
`CodingRulesBuilderSuffixes`, separated by semicolons; setting it replaces the
default list. For example:

```xml
<PropertyGroup>
  <CodingRulesBuilderSuffixes>Builder;Descriptor;Configurator;Options</CodingRulesBuilderSuffixes>
</PropertyGroup>
```

Types can instead be marked with `[CodingRules.ConfigurationBuilder]` from
`CodingRules.Shared`. A same-named source-defined attribute is not accepted as a
marker. Extension receiver parameters are excluded from effective counts and
comparisons, including generic receivers such as
`this NodeDataBuilder<LessonTopicsConfig>`; this does not exclude ordinary
parameters on instance methods.

The reviewed serializer should use a reusable builder with Input and alternate
Output forms, optional Hash defaulting to empty, and cancellation passed to
execution. Preserve argument evaluation, defaults, ref semantics, cancellation,
resource ownership and any requested compatibility when migrating the API.
Keep required-configuration checks in runtime validation and
[manual agent review](agent-coding-rules.md#check-builder-completeness-manually).
The [RequiredStep analyzer proposal](required-builder-steps-proposal.md) is deferred;
CR1001 supplies neither required-call analysis nor annotations.

Broader repeated argument plumbing remains subject to
[semantic agent review](agent-coding-rules.md#review-repeated-argument-plumbing).
Overload count alone cannot identify the appropriate shared payload or context.

## Configuration

Normal Roslyn configuration applies independently of CR1000:

```ini
[*.cs]
dotnet_diagnostic.CR1001.severity = error
```

Use `none` to disable the rule. Intentional exceptions can use scoped
`#pragma warning disable CR1001` / restore or
`SuppressMessage("Readability", "CR1001", Justification = "...")` on the method.
Each declaration is configured and suppressed independently; suppression does
not make the underlying signature an accepted case.

## Regression and package evidence

`CodeFixTests/Consumers/OverloadConfiguration/Findings.cs` and `Accepted.cs` compile
together and are shared by analyzer tests and the isolated package gate. They
cover two-parameter static stream/path alternatives, the four-parameter serializer
shape, instance and nested-generic alternatives, one-parameter Parse and builder
Output methods, runtime builder validation, unrelated/base/derived types, dynamic
and framework calls, and the accepted signature mismatches listed above.

`AnalyzerTests/OverloadConfigurationAnalyzerTests.cs` adds exact locations,
generic ordinal substitution, partial declarations, interface/implementation
boundaries and explicit-implementation skips, ref/out/in/params and
default-presence comparisons, builder suffix configuration and marker validation,
extension receiver counting, generated code, unresolved signatures, severity,
scoped suppressions and independent CR1000 configuration.
All normal fixtures must compile before analyzer expectations are checked.

```sh
dotnet pack Package/CodingRules.Package.csproj -c Release --output artifacts/ticket06
python3 build/verify-overload-consumer.py artifacts/ticket06
```

The shared manual-consumer harness restores the exact archive from a local-only
feed into an empty cache. It verifies fourteen exact positive locations, accepted
and generated sources with no findings, unchanged source after a CR1001 fixer
pass, warnings-as-errors failure, and actual editorconfig error/disabled behavior.
CI runs this gate against the same archive as all existing consumer gates.
Publishing and downstream API migration are separate work.
