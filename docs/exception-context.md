# Typed exception context (CR1600)

An interpolated exception message often contains failure data that a handler
should be able to inspect without parsing text. CR1600 reports interpolation
passed directly to an exception constructor. CR0300–CR0302 do not offer a string
extraction for the same message.

The action **Generate exception with typed context** generates a named internal
sealed subtype nested in the current class, exposes the values through typed
read-only properties, and replaces construction with that subtype. Its name
comes from the literal message prefix, with collision handling. It inherits the
original framework exception so existing base catches continue to work, while
its exact runtime type changes. Review exact-type catches/assertions, serialized
type names and handler access before accepting it.

The first implementation deliberately retains eager rendering in `base(message)`.
It preserves the original template, culture, alignment and format strings.
It accepts only a one-string-argument framework exception constructor and simple
local/parameter holes of primitive, enum or string type. Repeated holes share one
captured property. Captures are immutable values or immutable strings, not live
mutable objects. A null string remains null.

Arbitrary expressions, property getters, user-defined formatting, mutable
references, custom exception hierarchies, additional constructor arguments and
other shapes remain manual findings. Moving those values ahead of formatting can
change evaluation order, thrown exceptions or failure-time observations. There
is no Fix All: generating an exception type is an API decision, not a routine
string rewrite. A later lazy Message implementation would require separate
culture, snapshotting, caching and repeated-read decisions.

`PolicyRefinementTests` verifies saved-source compilation, preserved message and
base catch behavior, read-only captured properties, repeated/nullable context,
and the absence of a string-extraction action or unsafe side-effecting rewrite.
