# Span-based text inspection

The text-handling policy prefers spans for inspection, slicing and trimming
when ownership and lifetime permit them. Ticket 9 assesses that broad policy
and implements one mechanically proved subset. `CR0700` is a Readability warning
with one action, **Inspect text with a span**. Generated code is excluded;
normal editorconfig severity, scoped pragma and SuppressMessage apply.

## Automatic catalogue

A resolved framework `string.Trim()`, `TrimStart()` or `TrimEnd()` with an
actual zero-parameter signature must be immediately consumed by `string.Length`
or built-in string `==`/`!=` against a non-null compile-time string constant,
in either operand order. Parentheses are transparent. Literal, const and nameof
constants are reconstructed as escaped literals from their semantic values.
The fix removes this temporary trim's potential materialization; an unchanged
input need not have allocated in the original implementation.

```csharp
static int Inspect(string text)
{
    static int InspectTextLength(string? sourceText)
    {
        var textLength = sourceText!.Length;
        var textSpan = global::System.MemoryExtensions.AsSpan(
            text: sourceText, start: 0, length: textLength);
        var trimmedText = global::System.MemoryExtensions.Trim(span: textSpan);
        return trimmedText.Length;
    }

    return InspectTextLength(text);
}
```

The helper receives the original receiver once at the original reached scalar
site. Its actual Length dereference preserves a null receiver's
NullReferenceException before subsequent work. `!` only suppresses nullable
analysis; plain AsSpan would incorrectly convert null to an empty span. Full
immutable-string bounds cannot fail. Parameter nullability follows the consumer
context; C# 7 uses a proven noncapturing local function without C# 8 syntax.
Eligible field/property initializers use a private static helper.

The corresponding nongeneric char-span trim uses Unicode `char.IsWhiteSpace`
with the same directional boundaries as string trimming. Equality stages a
constant span and calls exactly `SequenceEqual<char>(span: ..., other: ...)`;
inequality negates that result. Both compare ordinal UTF-16 code units, including
embedded NUL and surrogate units. No culture-sensitive operation is replaced.
String identity and allocation cannot be observed by these scalar consumers.
No string, array, delegate or closure is introduced by the helper.

All spans stay in the synchronous helper. Async/iterator callers retain their
original suspension points and heap-safe string/scalar boundary. Awaited
receiver evaluation remains at the call site. Receiver getters, indexes,
short-circuit/lazy branches, loop frequency and filter timing remain in place;
there is no receiver capture or mutable-struct copy.

## Availability and safety

The compilation-scoped catalogue resolves the consumer's actual string,
ReadOnlySpan and MemoryExtensions definitions and verifies signature, arity,
static/extension status, ref kinds and type identity. Framework provenance is
checked against the normal signed core/netstandard/System.Memory assemblies;
source impostors and ambiguous definitions are rejected. Equality-only missing
APIs do not disable proved Length sites. No dependency or language update is
added to consumers. Older TrimStart/TrimEnd calls bound to empty params arrays
are excluded even when syntax supplies no arguments.

Expression trees, nameof ancestors, dynamic/error/custom equality, conditional
access, explicit trim characters, chained trims, internal comments/directives,
unsafe/fixed contexts, unsupported declaration spaces and observer-sensitive
implicit caller constants are excluded. Existing nullable warnings at a selected
site are conservatively omitted. Exterior comments and formatting are retained.
Member-helper name generation reserves source identifiers across the compilation,
including other partial declarations and derived types, plus inherited and
enclosing type members. The source-name facts are weakly cached per compilation.

An outer argument, supported arithmetic/conversion/formatting consumer or
producing receiver can turn the new parameterized helper call into a new CR03
operation. Such individual sites are omitted; the older rules retain their
policy and have no generated-helper exemption. Existing warnings at the selected
replaced decision can disappear when its producing trim is removed. Unrelated
older roots must survive.

Boolean hosts are also omitted when removing the trim's producing category would
expose a different older condition warning, such as CR0201 after CR0203 in a
three-check condition. Planning follows the existing category and enabled-rule
precedence without replacing the compilation for each candidate.

Before registering an action the fixer formats, saves and reparses the proposal,
compares compiler warning/error messages, surviving original call/index/property
bindings, argument mappings/conversions/default constants, scalar conversion and
selected progress, then audits every older diagnostic family. The saved proposal
must preserve untouched source documents' call/property/default bindings and
nameof values, as well as compiler diagnostics across the compilation. Helpers
must add no old-family warning. A stale or invalidated plan fails closed. Fix All
supports document/project/solution, reparsing and replanning each edit with fresh
names, and is idempotent. Analysis supports concurrency and cancellation.

## Manual assessment

| Case | Ownership or proof boundary |
| --- | --- |
| Returned/stored trim or slice, dictionary/DTO/deferred selector/async state | Retain the owned string or redesign its owning boundary deliberately. |
| String parameters/results used only for inspection | Review all callers, external signatures, overloads, method groups, delegates, reflection and lifetimes before an API change. |
| Substring/range immediately inspected | Potential opportunity, but bounds, exception types/parameter names, Index/Range lowering and evaluation order need separate proof. |
| Culture-sensitive StartsWith/IndexOf/Equals/Compare or parsing | Require an actual equivalent span overload with matching culture/options/null behavior. |
| Span.ToString at an owned boundary | Materialization is legitimate; changing identity/allocation need not be an improvement. |
| Ref-struct capture, escape, storage, boxing or suspension | Follow actual consumer-language compiler restrictions; local immutable string slices do not authorize stack-backed escapes. |

Pinned FindJobHelper `ReadRowsAsync`'s header `Value.Trim() == "nr"` is the
intended positive. Row.Field/GetField's named trims return owned strings;
SelectErrorOutput returns owned text/suffixes; LatexMeasurementProtocol stores
dictionary keys/values; LatexResults yields owned fields. These remain manual.
LatexProgressMarkerProtocol, RichText and ApplicationIndexStore's existing span
inspection/slicing remain compliant. No warning requests wholesale signature or
dictionary redesign.

Tests compile saved fixes against real framework references and language options,
compare Unicode/culture/null/evaluation behavior, check all old families and Fix
All, and measure a warmed padded-string helper outside assertions. The bounded
allocation result is for the selected Trim, with no universal receiver-allocation
or throughput claim. Package/reference results are recorded in dogfood notes.
