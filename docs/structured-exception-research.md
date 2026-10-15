# Structured exception design — primary-source fact check

Scope: official Microsoft Learn docs and dotnet/runtime `v10.0.0` source only; no repo/home data. "Deferred" = compose in a `Message` getter; "eager" = interpolated string passed to `base(message)`.

## Verified facts

1. Passing a message string to the base constructor is Microsoft's documented typical custom-exception pattern [BP]. The guidance also describes three conventional constructors; that is background guidance, not a new constructor requirement adopted by this project.
2. `Exception.Message` is virtual: source `public virtual string Message => _message ?? SR.Format(...)` [EX61]; API `public virtual string Message { get; }`, "overridden in classes that require control over message content or format" [MSG].
3. `Exception.ToString()` reads the virtual `Message` (`string? message = Message;`) [EX127]; docs: default `ToString` uses "the message" [TST].
4. Derived types keep typed context and override `Message`: `ArgumentException.ParamName` is `virtual` [APN]; `ArgumentOutOfRangeException.ActualValue` is `virtual` [AOV]. Both override `Message` and append the typed value at read time [AEX73][AO71]. `ParamName`/`ActualValue` "returns the same value as was passed into the constructor" [APN][AOV].
5. Caching — checked in source, not assumed: the runtime does **not** cache the composed message.
   - `Exception.Message` is `_message ?? Format(default)` and never stores the default back; backing field `internal string? _message;` only holds ctor input [EX61][EXF150].
   - `ArgumentException.Message` recomposes `base.Message + paramName` on every read; it memoizes only the static default once via `SetMessageField` (`_message = SR.Arg_ArgumentException`, an unsynchronized, idempotent constant write) [AEX73][AEX89].
   - `ArgumentOutOfRangeException.Message` recomposes the value text on every read [AO71].
6. Culture timing depends on the formatter. Ordinary interpolated values use the current formatting culture when evaluated; resource lookup can use a separate UI culture. A custom getter may deliberately use a stored or invariant provider. Eager text fixes its rendering at construction, uncached deferred rendering evaluates the chosen policy on each read, and caching fixes the first successful rendering. Do not assume every custom getter is culture-sensitive [MSG][TST].
7. Logging is not automatic structured data. `ILogger` takes the `Exception` as a separate argument and "Exception logging is provider-specific" [LOG]; structured fields come from message-template arguments ("The arguments themselves are passed to the logging system ... to store the parameter values as fields") and scopes [LOG]. So custom exception properties are **not** guaranteed to appear as structured fields without explicit integration (passing values as template args, or a provider that reflects them). No stronger claim is supported here.

## Uncached getter vs lazy cached getter

- **Allocation:** an uncached getter that composes dynamic text can allocate on each `Message`/`ToString` access; the observed framework getters recompute their context-dependent additions [AEX73][AO71]. A constant-returning getter need not allocate. Lazy caching can reuse a completed message, but concurrent first reads may still compute more than once unless synchronization prevents that.
- **Culture / read-time:** uncached composes at read time (current culture) [MSG][TST]; a cache freezes culture at first read; eager `base(message)` freezes at construction.
- **Thread safety:** define the cache contract explicitly; merely publishing a value atomically does not guarantee exactly one formatting operation. ArgumentException's default-resource write is idempotent [AEX89]. An uncached getter needs no cache synchronization, though mutable context can still require its own safety measures.
- **Snapshot vs mutable graph:** readonly fields freeze the stored value/reference, not the referenced object's state. `_paramName` references an immutable string, but `_actualValue` is an object and may remain mutable. Preserve stable values or make an appropriate snapshot if the diagnostic must describe throw-time state. Eager text captures its observed rendering at construction.

## Project-policy nuance

- Microsoft prefers an existing applicable exception type and recommends introducing a custom type when existing types do not fit [BP]. The project's preference for meaningful custom domain exceptions is a project policy, not a universal requirement to replace framework exceptions.
- If the typed domain base currently exposes only `ctor(string)`, a parameterless/deferred path is a deliberate **base API change**, not a free refactor; docs' three-ctor guidance assumes you can add it [BP].

Conceptual skeleton (not compiled; the parent owns the real sample). It needs a base parameterless ctor and makes `Message` culture read-time dependent:

```csharp
public sealed class DomainException : Exception {
    public MyContext Context { get; }
    public DomainException(MyContext c) { Context = c; }   // requires base() path
    public override string Message => Format(Context);     // deferred, read-time
}
```

## Sources (v10 tag for runtime)

- [BP] Best practices for exceptions — https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions
- [MSG] Exception.Message — https://learn.microsoft.com/en-us/dotnet/api/system.exception.message?view=net-10.0
- [TST] Exception.ToString — https://learn.microsoft.com/en-us/dotnet/api/system.exception.tostring?view=net-10.0
- [APN] ArgumentException.ParamName — https://learn.microsoft.com/en-us/dotnet/api/system.argumentexception.paramname?view=net-10.0
- [AOV] ArgumentOutOfRangeException.ActualValue — https://learn.microsoft.com/en-us/dotnet/api/system.argumentoutofrangeexception.actualvalue?view=net-10.0
- [LOG] Logging in C# — https://learn.microsoft.com/en-us/dotnet/core/extensions/logging
- [EX61] Exception.cs L61 — https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Exception.cs#L61
- [EX127] Exception.cs L127 — https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Exception.cs#L127
- [EXF150] Exception.CoreCLR.cs L150 — https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs#L150
- [AEX73] ArgumentException.cs L73 — https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/ArgumentException.cs#L73
- [AEX89] ArgumentException.cs L89 — https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/ArgumentException.cs#L89
- [AO71] ArgumentOutOfRangeException.cs L71 — https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/ArgumentOutOfRangeException.cs#L71
