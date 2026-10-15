# CodingRules contributor guidance

Follow the coding standards in [docs/spec.md](docs/spec.md) for hand-written
code in this repository. Keep analyzer diagnostics conservative. Every pattern
reported by the first implementation of CR0001 must have a behavior-preserving
code fix and a test for that fix.

Use [CONTEXT.md](CONTEXT.md) for project terminology and
[docs/schedulelib-follow-up.md](docs/schedulelib-follow-up.md) for the current
design decisions and open questions. Update these records as the discussion
settles decisions; concise architectural records live in `docs/adr/`.

Follow [docs/agent-coding-rules.md](docs/agent-coding-rules.md) when acting on
duplication findings. Review shared meaning before extracting a common helper;
similar expressions may represent intentionally separate domain concepts.
