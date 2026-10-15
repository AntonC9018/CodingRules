# Preserve structured context on custom domain exceptions

Prefer meaningful custom domain exceptions with associated context available
through typed read-only members, so handlers can inspect the failure without
parsing its human-readable message. The message remains a rendering of that
context rather than its only representation; replacing a framework exception
with a new domain type is a deliberate API decision, not an automatic safe fix.

The maintainer also prefers avoiding unnecessary eager message formatting.
Overriding Message is an established .NET mechanism for deferred rendering,
but caching, context snapshotting, localization, and automatic enforcement
remain implementation and coverage questions to establish with concrete cases.

Microsoft's [exception guidance](https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions)
describes base(message) as typical and recommends additional properties when
they are useful to programmatic handling. .NET's
[ArgumentOutOfRangeException](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/ArgumentOutOfRangeException.cs#L62-L78)
exposes ActualValue and formats it from an overridden Message getter.
[Exception.ToString](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Exception.cs#L112-L116)
reads the virtual Message property.

A .NET 10 probe with a counting Message getter observed zero formatting calls
after construction and throw/catch without reading the message, one after a
Message read, and another after ToString. An uncached getter formats on every
read; this is deferred formatting evidence, not an allocation benchmark.
