# Explicit conditional updates (CR1200)

ScheduleLib slice 11 implements A13/Q13 as a Readability warning, enabled by
default. Normal `.editorconfig` severity, `#pragma warning` and `SuppressMessage`
configuration applies; generated code is excluded. The diagnostic covers an
entire simple assignment whose conditional right side has a proven unchanged
branch. Other diagnostic families remain independent.

## Source and proof boundary

The pinned [A13 source](https://github.com/AntonC9018/ScheduleLib/blob/12e601e7b62511bdecc44e280407fee12147b230/src/MainCli/CliSettings.Removals.cs#L271-L275)
contains this reflection traversal:

```csharp
object? current = value;
foreach (var member in members)
{
    current = current is null ? null : member.GetValue(current);
}
```

The null branch is already the stored value. The fix makes the update explicit:

```csharp
if (!(current is null))
{
    current = member.GetValue(current);
}
```

[Q13](schedulelib-follow-up.md#data-shapes-parameter-groups-and-reuse-q11q14)
also approves `current = condition ? current : next` and its reversed orientation.
Storage identity is resolved by symbol, including parentheses and implicit or
explicit `this` field access. A null literal is proved unchanged only by a
built-in reference null test of that same storage: `is null`, `is not null`,
`== null`, or `!= null` (either equality operand orientation). Overloaded equality,
semantic guesses about helper results, arbitrary equal-value expressions and
unproved null branches remain unreported.

Supported storage is a non-ref local, by-value parameter, or non-readonly,
non-volatile instance field on the current reference-type instance. Property,
indexer, array-element, static-field, other/effectful-receiver, struct-instance
and ref-alias targets remain unreported. In particular, being inside a property
host does not establish that assigning to a property is harmless. No setter,
receiver or index evaluation is removed or repeated. Synchronization through
volatile stores is outside the rule's boundary.

The condition must have actual Boolean type. The conditional's type must match
the assignment type; a conversion of a whole conditional can otherwise make even
its null branch observably different. Dynamic, error, pointer, anonymous and
ref-like assignment types are excluded. Changed operands may mutate storage or
throw: their original reached-branch timing is retained. The unchanged direct
storage read occurs after condition evaluation, including when that evaluation
mutates the stored value.

## Hosts and automatic action

The action replaces an assignment statement with an `if` and the reached
update. Embedded statements receive a block to retain their surrounding `else`.
A direct assignment return or supported arrow host becomes a block with an
explicit conditional update/return and an unchanged-value return. For example:

```csharp
object? Property => current = keep ? current : Next();
```

becomes a getter with:

```csharp
if (!(keep))
{
    return current = Next();
}
return current;
```

Returning the reached assignment directly preserves its result without reading
a mutable field again. The other return keeps the original unchanged branch, with a cast to the
assignment's static type when needed, so a null literal cannot bypass an original
return conversion. Methods, local functions, property/indexer getters and get/set/init
accessors are supported. Void hosts do not acquire a return value. Assignment
results consumed by arguments, initializers or other expressions, delegate
lambda arrows and unsupported arrow declarations remain outside initial coverage.

Internal comments, including comments on removed `?:` and arrow tokens, are
retained once at the beginning of the replacement block. Exterior comments and
documentation stay on the host. Directives, expression trees and invalid source
are conservative exclusions. Implicit caller-information arguments anywhere in
the document, including collection `Add` and indexer calls, exclude the rewrite
because blocks can change source lines or property getter member names.

Before offering an action the fixer formats, saves and reparses the candidate,
rejects compiler errors/nullable warnings, and compares every original call,
construction and indexer binding/argument conversion in the document. A confident
finding may have no automatic action if scope or nullable-flow changes fail this
validation; this follows [ADR 0001](adr/0001-separate-findings-from-fix-availability.md).
Document/project/solution Fix All tracks sites and replans/validates each edit in
sequence. A saved second pass is unchanged.

## Regression and package evidence

`UnchangedAssignmentTests` compiles and runs the shared consumer fixture before
and after fixing. It checks the exact saved source and six findings: the pinned
reflection expression, generalized branches in both orientations, an arrow
property, an arrow getter and an arrow setter. Accepted property/indexer setters
still run, and accepted array/equality-unknown cases remain unchanged. Runtime
checks include null/non-null traversal, branch selection, setter counts,
condition/update mutations, exceptions and a user-defined return conversion.
Focused cases cover comments, host forms, configuration/suppression, generated
code, ref/effectful targets, overloaded equality, source observers and Fix All
across documents/projects.

The shared fixture lives in `CodeFixTests/Consumers/UnchangedAssignment`, with its
exact saved result in `CodeFixTests/Expected/UnchangedAssignment.Fixed.cs.txt`.
`PackageConsumptionTests.AppliesPackagedUnchangedAssignmentFix` registers the same
positive and accepted cases against `Anton.CodingRules`. Package execution is
reserved for the single stable candidate after all follow-up tickets; it is not
claimed as validation performed during slice 11.
