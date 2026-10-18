# Concrete class sealing review (CR1400)

CR1400 is a Readability warning implementing [ticket 13 / CodingRules #32](https://github.com/AntonC9018/CodingRules/issues/32)
and [ScheduleLib A16](https://github.com/AntonC9018/ScheduleLib/issues/229).
It requests sealing review for hand-written non-abstract, non-static, unsealed
classes, including record classes, exceptions, public types, virtual members
and types with existing inheritors. An inherited test seam is not an exemption.

## Upstream reuse and the actual gap

The implementation adapts MIT-licensed [Meziantou MA0053 source](https://github.com/meziantou/Meziantou.Analyzer/blob/decb1e62ae7205db62d8b6225220e108d05f52d4/src/Meziantou.Analyzer/Rules/ClassMustBeSealedAnalyzer.cs)
at `decb1e62ae7205db62d8b6225220e108d05f52d4`, following the
[reuse research](formatting-reuse-research.md#other-229-reuse-leads).
It reuses concrete-type candidate selection and the COM-import/benchmark
exclusions. Source adaptation avoids shipping an entire second analyzer suite
and its unrelated rules. The [provenance and change record](../third-party/meziantou/PROVENANCE.md)
and upstream MIT notice are included in the consumer package.

MA0053 deliberately removes classes with current-compilation inheritors. Its
public/exception/virtual options do not close that gap. Consequently enabling
MA0053 alone would miss A16 and could not satisfy the policy. The adapted rule
includes those classes, adds the attribute exception below, uses repository
warning/configuration conventions and excludes generated code. It does not
enable or advertise MA0053 as a second diagnostic.

## The attribute exception

A non-generic class deriving from resolved framework `System.Attribute` is
accepted when its same-owner, same-name one-type-parameter variant directly
inherits it and only forwards `typeof(T)` to its `System.Type` constructor:

```csharp
public class MergerAttribute : Attribute
{
    public MergerAttribute(Type type) { Type = type; }
    public Type Type { get; }
}
public sealed class MergerAttribute<T> : MergerAttribute
{
    public MergerAttribute() : base(typeof(T)) { }
}
```

The proof supports empty ordinary constructors and empty-parameter primary
constructors, named constructor arguments, partial declarations and generated
generic wrappers. The wrapper must have no extra declared members, no constructor
parameters, and no constructor work beyond the forwarding initializer. The base
may store and expose its type normally. The variant itself must still be sealed
or abstract to avoid its own finding. Arbitrary inheritance, a differently named
generic child, forwarding a concrete type or `typeof(T[])`, and constructor/member
work do not establish this exception. Other forwarding designs require manual
review rather than a general attribute or inheritance exemption.

## Findings and actions

The original [CalendarCommands declaration at 12e601e, lines 23–33](https://github.com/AntonC9018/ScheduleLib/blob/12e601e7b62511bdecc44e280407fee12147b230/src/MainCli/Commands.Calendar.cs#L23)
has virtual configuration/connection hooks used by derived tests. CR1400 reports
its class identifier even with those tests present. Replacing that seam is a
separate architectural decision; blindly adding `sealed` would break it.

**No automatic code fix or Fix All is offered**, including for leaf classes.
Absence of inheritors within one compilation does not prove that callers in
other projects, runtime proxies or reflection-based frameworks permit sealing.
Manual review decides how to satisfy the default: seal a safe class, redesign
an owned inheritance seam, make an appropriate base abstract, or document a
concrete imposed external constraint. This follows [ADR 0001](adr/0001-separate-findings-from-fix-availability.md)
without changing the original CR0001 safe-fix requirement.

The adapted upstream exclusions retain resolved framework `ComImportAttribute`
classes and methods carrying an externally defined
`BenchmarkDotNet.Attributes.BenchmarkAttribute` (also through inherited methods).
These frameworks impose concrete constraints; an external interface implementation,
an external base class, a virtual method or a test subclass alone does not.
Source-defined lookalike attribute names do not establish those external constraints.
Uncatalogued framework requirements can use a justified local suppression:

```csharp
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Readability", "CR1400", Justification = "The reviewed framework contract requires runtime subclassing.")]
public class FrameworkEntryPoint { }
```

Abstract/static/sealed classes, interfaces, structs, delegates, implicit top-level
Program, malformed declarations, unresolved base types and generated code are
not reported. A partial class receives one warning on its declaration identifier.

## Configuration and regression evidence

Ordinary Roslyn severity, pragma and SuppressMessage configuration applies:

```ini
[*.cs]
dotnet_diagnostic.CR1400.severity = error
```

Use `none` to disable it. Analyzer tests compile shared positive/accepted fixtures,
check exact locations, test concrete/generic/record/public/exception/virtual and
inherited cases, verify the forwarding proof, generated code and partial types,
and exercise severity and scoped suppressions. Metadata-backed benchmark tests
distinguish an external framework contract from a source-name spoof.

`CodeFixTests/Consumers/SealedClasses/Findings.cs` reduces A16's original virtual
configuration and connection hooks with their parameter shapes preserved; local
dependency stubs make it compilable, and a sealed derived test exercises both
overrides. Nine exact positive locations share the same analyzer/package fixture.
`Accepted.cs` supplies the attribute wrappers, abstract/static/sealed/value cases,
COM-import constraint and justified suppressions.

The isolated regression is registered for the final stable candidate only:

```sh
python3 build/verify-sealed-classes-consumer.py artifacts/release
```

It checks exact warnings, accepted/generated cases, unchanged source in the CLI
fixer host, warnings-as-errors, and real editorconfig none/error transport using
the supplied archive. Package execution is deferred until all tickets are done;
no pack, consumer gate execution or CI enablement is included in ticket 13.
