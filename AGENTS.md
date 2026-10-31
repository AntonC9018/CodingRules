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
duplication findings, reviewing repeated argument plumbing, or configuring
builders. Review shared meaning before extracting a common helper or context;
similar expressions may represent intentionally separate domain concepts.

## Testing

Scale test cost to the edit. The measurements and reasoning behind this policy
are in [artifacts/test-latency/REPORT.md](artifacts/test-latency/REPORT.md).

- Run the focused tests for the code you changed first (targeted `--filter`).
- For a broad local check, exclude the heavyweight package tests:
  `dotnet test CodingRules.slnx -c Release -p:WarningsNotAsErrors=NU1902;NU1903 --filter FullyQualifiedName!~PackageConsumptionTests`.
- Do not run `dotnet pack` or the `build/verify-*-consumer.py` gates while
  developing. `PackageConsumptionTests` and those gates each repack the package
  and spawn consumer builds/formatters, so they dominate implementation time.
  Run them once at the end, against a single stable candidate package, and in
  CI. The gates accept an explicit package path so one candidate can be reused.
- Avoid running heavy validation (solution tests, package tests, consumer
  gates) concurrently on this machine; scheduler, CPU, memory, and disk
  contention have caused multi-minute timeouts. The full suite must end green.
