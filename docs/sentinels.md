# Primitive sentinel returns

`CR0600` warns once on an application-owned, declared non-private `int` return
type that exposes a proven absent-index sentinel. Internal and protected members,
and public members inside private types, are included. Methods, getter properties,
indexers and operators are supported. Change the return contract and its callers
manually to an appropriate nullable or result type; CR0600 has no code action.

`CR0601` warns on a feasible unchecked return of a certified private method or
local function result. Private helpers may return a sentinel. Their primitive
callers must return the sentinel explicitly on the failure path:

```csharp
int index = IndexFor(name);
if (index == -1)
{
    return -1;
}

return index;
```

The canonical action evaluates the helper once at the original return site and
adds this guard. An existing stable local is reused; a return already known to
be the sentinel becomes `return -1;`. Arrow bodies become block bodies. Fix All
supports document, project and solution scopes and replans after each change.
When CR0600 owns a host, CR0601 is hidden; setting CR0600 severity to `none`
exposes feasible forwarding sites. Ordinary pragma/SuppressMessage suppression
uses Roslyn's standard behavior. Previous diagnostic families stay independent.

The first version proves only zero-based index or `int -1` outcomes from actual
framework String/List<T> IndexOf/LastIndexOf definitions, certified private/local
forwarding chains and one bounded source array-search recipe: a counter from
zero to the actual SZ-array Length, incremented once, an element predicate that
returns that counter, then -1. A stable array local may be initialized before
the loop. Array/counter capture, reference escape or mutation invalidates this
recipe. Element/predicate side effects alone do not invalidate the index shape.
This source form is a policy proxy for selection, not general business-intent
inference. Recursive chains and unsupported control flow remain unknown.

Flow distinguishes success from sentinel: a wrapper that throws on absence and
returns only success needs neither diagnostic. Identity aliases and built-in
comparisons to -1/0 (including constants, reversed operands and negation) support
explicit guards. A comparison that logs and falls through does not check the
returned sentinel. Fields, captured locals, ref/out escapes, transformed results
and ambiguous reaching definitions do not establish provenance.

Arbitrary -1 literals, ordinary negative data, counts/zero/default, bool/out Try
functions, nullable types, enums, empty strings, BinarySearch, Array.IndexOf,
custom lookalikes, async/iterator signatures and nonidentity conversions are
outside this initial evidence set. Names and comparisons alone never prove
absence. Nullable return types are allowed without validating their migration.

Actual external interface maps and override chains impose exempt signatures,
including implicit, inherited and generic contracts. A source-owned contract
does not. Project references are external to the current compilation even when
another editable project exists in the solution. Generated contracts are also
imposed. A directly bound external callback with a fixed int delegate return is
exempt; an application-selected generic result or a local Func<int> is not.
Private method-group escape does not change declared ownership.

Generated code is excluded. Local functions and lambdas have independent facts.
CR0601 omits unsafe/ref/expression-tree/invalid/directive shapes and affected
implicit caller-information constants, including neighboring calls, attributes,
base initializers and indexers. Explicit original constants remain supported.
The fix verifies formatted, reparsed source, compiler messages, all original call
bindings, argument conversions and implicit constants. It changes no signature.
Compilation-scoped immutable summaries use independent recursion walks with
32-callee depth and 4000-node body limits; exhausted/cyclic facts stay unknown,
and cancellation is never cached.
