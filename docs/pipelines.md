# Framework pipeline catalogue

The compiler catalogue resolves metadata definitions once per compilation and
normalizes reduced extension methods and generic constructions. It excludes
source-defined lookalikes and unsigned/foreign assemblies. Enumerable metadata
is accepted from System.Linq, System.Core and real netstandard definitions;
immutable definitions come from System.Collections.Immutable. Method names alone
are insufficient: source, selector arity/types, generic parameter relationships,
parameter names and supported tail shape are checked. Queryable/expression trees
and nested expressions inside an enclosing expression tree cannot be rewritten.

| Declaring type | Projection slots | Materializers |
| --- | --- | --- |
| System.Linq.Enumerable | Select element and indexed selectors; catalogued terminal selectors | ToDictionary and ToLookup: key; key/comparer; key/element; key/element/comparer |
| System.Linq.ImmutableArrayExtensions | Select element selector; catalogued terminal selectors | ToDictionary: key; key/comparer; key/element; key/element/comparer |
| System.Collections.Immutable.ImmutableDictionary | Catalogued terminal selectors | ToImmutableDictionary: key; key/keyComparer; key/element; key/element/keyComparer; key/element/keyComparer/valueComparer |
| System.Collections.Immutable.ImmutableSortedDictionary | Catalogued terminal selectors | ToImmutableSortedDictionary: key/element; key/element/keyComparer; key/element/keyComparer/valueComparer |

Actual immutable dictionary parameters are **elementSelector**, including sorted
dictionaries. ImmutableArray dictionary generic order is TKey,TElement,T;
Enumerable and immutable dictionary order is TSource,TKey,TElement. The paired
identity transition substitutes the source type for the element type and retains
the same key/source/result types and comparer tail. Explicit type arguments avoid
accidental generic inference changes; the fixer still verifies the bound pair
and each original argument conversion after saving/reparsing.

The key-only identity pairs are Enumerable ToDictionary/ToLookup,
ImmutableArrayExtensions.ToDictionary and ImmutableDictionary.ToImmutableDictionary,
each with and without its supported comparer. Naming-only transformations retain
the original overload. The added pure selector is static on C# 9 or later and an
ordinary identity lambda on earlier versions. Every original argument remains
in source order; the newly added selector is appended as a named argument.

Primary-source proof: the .NET runtime [Enumerable collection implementations](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Linq/src/System/Linq/ToCollection.cs),
[lookup implementations](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Linq/src/System/Linq/Lookup.cs),
[ImmutableArray extensions](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Collections.Immutable/src/System/Linq/ImmutableArrayExtensions.cs),
[immutable dictionary definitions](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Collections.Immutable/src/System/Collections/Immutable/ImmutableDictionary.cs)
and [sorted dictionary definitions](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Collections.Immutable/src/System/Collections/Immutable/ImmutableSortedDictionary.cs)
establish the overload relationships. Key-only paths add the original element;
their paired paths add the element selector result at the same place. Identity
therefore preserves original selector/comparer calls, duplicate-key behavior,
enumeration and disposal. ImmutableDictionary key-only overloads already forward
through an identity selector. ImmutableArray Select checks initialization at
construction and delegates lazy projection to Enumerable; the fixer retains both
timings. Runtime tests compare original and saved/reparsed selector/comparer logs.

Sequence-of-KeyValuePair dictionary materializers are deliberately not paired.
Immutable instance/builder paths may preserve object identity or avoid enumeration
even through an IEnumerable static type. A selector reconstruction does not prove
those properties. Enumerable's newer KVP/tuple overloads also remain outside the
selector catalogue. No materializer is replaced with another collection kind.

Query syntax, SelectMany, Aggregate/grouping and arbitrary user delegate APIs are
outside this first finite stage catalogue. No warning is inferred from pipeline
length, property access, method naming or independent simple DTO transformations.
Automatic semantic reuse/private-method deduplication and arbitrary method-region
extraction remain judgment rather than implemented diagnostics.

Individual safety omissions include async/yield, unrenderable helper results,
ref-like/pointer/unsafe values, expression trees, internal directives/comments,
labels/gotos and omitted caller-information defaults. Whole block extraction can
retain FormattableString without lowering its formatting; expression linearization
does not support FormattableString/handler lowering. Mutable receiver/location,
ref/params allocation, lazy custom operations and narrowed nullable dependencies
follow the existing operation planner's boundaries. Init-only/required/nested or
collection initializers and constructor arguments combined with object initializers
are not staged by the new aggregate path. Settable reference-type object
components allocate the object first, then retain setter/value order; tuple
components retain their original evaluation order. Compiler-only success is not
used as proof of a paired materializer transition.
