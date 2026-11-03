# Required builder steps: deferred analyzer experiment

Status: recorded for later exploration, not approved implementation work.
For now, agents review builder completeness manually through the
[agent coding rules](agent-coding-rules.md).

The maintainer wants an annotation-based analyzer over ordinary mutable builders,
not a generated staged API. Mark core configuration methods with RequiredStep.
Before a builder is executed, check whether all required steps have been covered
on that builder instance. Optional configuration, such as Hash with its default
empty value, is not a required step.

Coverage should be compositional: when an overload or higher-level method calls
two required methods, calling that higher-level method covers both. Follow the
resolved method bodies rather than assuming that matching names or parameter
types imply equivalent configuration. Extensions may provide convenience
overloads and higher-level operations while the core required methods remain
on the builder itself.

The initial experiment should probably discover annotated requirements only on
the builder's own methods. Discovering new required methods declared by arbitrary
extensions is not necessary for that initial scope. This is a preferred starting
point, not a completed design. Following extension helper bodies to establish
coverage is a separate concern from discovering required-step declarations.

For example, a helper that always calls Input and Output should satisfy both
requirements. A helper that calls Output only in one conditional branch must not
establish unconditional Output coverage. Requirements must refer to the same
builder instance, rather than calls on another builder or an unrelated copy.

Future exploration should decide how execution points are identified, how
overload/helper summaries are represented, and what to do with unavailable
bodies, recursion, aliases, exceptions, resets, and conflicting configuration.
Recognized call coverage is not proof that all supplied values are valid.
Keep runtime validation for incomplete/invalid configuration. These are future
design boundaries; do not implement the analyzer or add annotations now.

The earlier [tooling survey](builder-protocol-analyzers-research.md) did not verify
a matching reusable C# analyzer. Its staged-builder candidates were explicitly
rejected as the intended solution. That result does not establish that the
annotation-based idea is impossible or that no relevant tool exists anywhere.
