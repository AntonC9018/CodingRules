# Cohesive inputs for owned contracts (CR1000)

CR1000 is a Readability warning for locally owned contracts with **more than
three configuration parameters**. Four triggers the rule; the earlier five-parameter
proposal was rejected. A trailing framework CancellationToken passed by value is
execution control and does not count. Other slots count, including optional/default
parameters, ref/in/out parameters and a params array (one slot).
Generic type parameters and implicit receivers are not parameter slots.

This implements [CodingRules #24](https://github.com/AntonC9018/CodingRules/issues/24)
and the accepted [Q12/Q17/Q18 decisions](schedulelib-follow-up.md#tuple-and-parameter-refinements-q15q18).
It is a **manual finding**. Choosing cohesive inputs, a builder, their ownership
and lifetime, and migrating callers are architectural decisions. There is no
automatic code fix or Fix All action and no claim that an arbitrary argument bag
would preserve the API's meaning.

## Detection and ownership

The analyzer reports the declaration's identifier once for:

- Locally declared interface method contracts, including default implementations
  and static abstract/virtual contracts. Private nonvirtual interface helpers
  and concrete static helpers are outside this contract boundary.
- Virtual and abstract method declarations that introduce a contract.
- Delegate type declarations, at the delegate identifier rather than each
  lambda, anonymous method, local function or method used as an implementation.
- Explicit instance constructors of abstract classes, including private
  constructors and primary constructors of abstract classes and abstract records.

Interface implementations (implicit or explicit), overrides (including sealed
and reabstracted overrides), and partial implementation parts do not repeat the
finding. Generic interface substitution and inherited interfaces are resolved
through symbols. A local interface declaration still reports even when its
implementations must conform to it. An inherited interface with no redeclaration
adds no finding; a `new` declaration introduces a separately owned contract.

Referenced contracts are externally imposed and exempt at their implementations.
For example, even a virtual implementation of the framework's four-parameter
ISpanFormattable.TryFormat is exempt. The exemption follows the actual resolved
contract, not a method-name allowlist. An unrelated locally introduced virtual
TryFormat still reports. A new owned abstract-class constructor is checked even
when its base type comes from a dependency: constructor signatures are not
inherited contracts.

Ownership is bounded to the current compilation's declarations. Contracts in
referenced assemblies are not diagnosed by inspecting their metadata. Suppressed
or generated contract declarations do not move a finding to their implementations.
Independently introduced declarations can each be owned contracts, such as a
base virtual method later used to implement an interface by a derived class.

Ordinary concrete constructors, nonvirtual instance methods, concrete static
functions, local functions, lambdas, anonymous methods, indexers, operators and
accessors receive no independent count finding. Synthesized constructors are
not analyzed. Invalid syntax and unresolved parameter types are conservative
skips, including unresolved arguments in containing generic types and unresolved
function-pointer return or parameter types. The analyzer runs concurrently and excludes generated code through the
standard Roslyn driver.

## Reviewed boundaries

`CodeFixTests/Consumers/ContractParameters/Findings.cs` and `Accepted.cs` compile
together and are reused by the analyzer tests and the package gate:

- Four configuration parameters report on owned contracts. A signature with
  three configuration parameters plus a trailing CancellationToken is accepted,
  including interface methods, abstract constructors and primary constructors.
  The abstract Merge method and PDF/Word-style import delegate retain five
  configuration parameters and report. Static abstract Create and virtual Visit
  retain four configuration parameters and report.
- Ordinary concrete implementations, virtual implementations of local
  interfaces, explicit implementations, and overrides receive no duplicate.
- The reviewed static `Finish<T>(context, exit, errors, data, verb)` and ten-slot
  `Edit(arguments, scope, key, value, unset, cancellationToken,
  invocationDirectory, userFile, operation, item)` remain accepted by count.
  The static serializer signature also receives no count finding.
- Both implicit virtual and explicit ISpanFormattable.TryFormat implementations
  remain accepted, as do three-slot contracts, ordinary nonvirtual import
  methods, concrete constructors and signatures taking a cohesive input record.

The static exception does not establish compliance with the independent Q19
overload/builder policy. Repeated plumbing still requires semantic review under
[agent guidance](agent-coding-rules.md#review-repeated-argument-plumbing).
Q20's concrete shared import record decision does not establish a blanket
parameter-count rule for ordinary nonvirtual instance methods.

`AnalyzerTests/ContractParameterAnalyzerTests.cs` additionally covers generic
and inherited interface ownership, new declarations, external metadata, optional
and params slots, ref/in/out parameters, static virtual interface contracts,
generated code, invalid signatures and scoped suppressions. All normal fixtures
must compile before analyzer expectations are checked.

## Manual migration and configuration

Review which values share a domain role and lifetime before grouping them. Use
the application's existing cohesive input or design a meaningful named value;
review overload alternatives for a builder separately. Update the contract,
implementations and callers together. Preserve argument evaluation, ref semantics,
defaults, cancellation behavior and any compatibility requirements. The rejected
schedule-plus-hash record is not a recommended automatic grouping.

Ordinary Roslyn configuration applies independently of other diagnostics:

```ini
[*.cs]
dotnet_diagnostic.CR1000.severity = error
```

Use `none` to disable the rule. Intentional exceptions can use scoped
`#pragma warning disable CR1000` / restore or
`SuppressMessage("Readability", "CR1000", Justification = "...")` on the contract.
Suppression does not turn a violation into an accepted regression case.

## Packaged verification

```sh
dotnet pack Package/CodingRules.Package.csproj -c Release --output artifacts/contract-parameters
python3 build/verify-contract-consumer.py artifacts/contract-parameters
```

The gate restores the exact archive using an empty package cache and local-only
feed. It checks four exact positive locations, no extra findings in accepted or
generated sources, unchanged source after a CR1000 fixing pass, warnings-as-errors
failure, and actual editorconfig error/disabled behavior. A separate failing
compilation verifies only the expected CS0246 errors for containing generic
types and function-pointer parameter/return types; Roslyn skips analyzers when
declaration errors exist. `ContractParameterAnalyzerTests.SkipsInvalidSignatures`
covers the analyzer-side skip behavior. CI runs it against the
same archive used for the other package gates. The shared manual-diagnostic
consumer harness also runs the existing CR0900 gate with its original fixtures.
No downstream API migration or NuGet publication is implied.
