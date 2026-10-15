# CodingRules

CodingRules expresses the maintainer's C# readability preferences as findings
and refactoring actions. Referenced examples establish the intended boundaries
between code that needs changing and code deliberately accepted as written.

## Language

**CodingRules package**:
The consumer entry point supplying the intended coding rules, refactoring
actions, and configuration, regardless of how upstream components are delivered.
_Avoid_: Single physical archive

**Coverage case**:
A concrete code example with an expected finding or an expected absence of
findings, used to establish a coding requirement's intended scope.
_Avoid_: Rule-name match

**Accepted case**:
A coverage case that deliberately complies with the coding preference and
must remain unreported by the relevant rule.
_Avoid_: Suppressed violation

**Manual finding**:
A confidently identified violation whose correction is left to a person
because no safe automatic action is available.
_Avoid_: Uncertain finding

**Message value**:
Structured data representing a human-facing message before its text is
rendered.
_Avoid_: Rendered message, message string

**Reporting boundary**:
The place where an application accepts message data and chooses when and how
to render human-facing text.
_Avoid_: Message construction
