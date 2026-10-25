# Statement-operation Fix All performance

Measured on the temporary AWS `m7i-flex.large` runner (2 vCPU, 8 GiB),
2026-10-24. Before: `dfd239d`. After: document-scoped semantic diagnostics
in `StatementOperationFixAllProvider`. No concurrent builds or tests ran.

Fix All previously reran the statement-operation analyzer over the entire
project after every edit, then discarded diagnostics from other documents.
It now requests semantic diagnostics for the current document. Each edit still
replans against the updated compilation and retains the existing saved-source
checks for compiler diagnostics, call binding, conversions and convergence.

| Documents | Target edits | Sites in each unrelated document | Before | After |
| --- | --- | --- | --- | --- |
| 32 | 12 | 20 | 5,744 ms | 859 ms |
| 256 | 12 | 20 | 16,230 ms | 716 ms |

These are single samples after an excluded Roslyn/JIT warm-up, not timing
guarantees. The larger fixture improved about 23 times. All four runs produced
the same saved-source SHA-256:
`903A5790989FA2A3A4DAB6D361245A9C54DE90E33D9921E4E34A68204A56CDE6`.
The harness asserts compilation, diagnostic convergence and unchanged unrelated
documents. Its SDK Roslyn references match the CLI host rather than adding
another package dependency. Run it against a directory containing the built
analyzer and code-fix assemblies:

```sh
dotnet run --project build/performance/FixAllBenchmark.csproj -c Release \
  -p:AnalyzerDirectory=/absolute/path/to/analyzers/dotnet/cs -- 256 12 20
```

Six regression cases cover document/project/solution scopes with both actions
and cross-file bindings. Existing interaction tests also verify saved-source
idempotence and replanning. Timing thresholds are deliberately excluded from
unit tests because machine contention changes wall-clock results.

All 1,401 analyzer/code-fix/package tests and ten consumer gates passed on AWS
with the production change at `0f02bab`. The single stable candidate has SHA-256
`e0268e2bc377add6b2f5265fcb4fcc162c8ea4f276511a2ffb071690b978caca`.

## Condition Fix All follow-up — 2026-10-25

The broader ScheduleLib pass exposed the same project-wide scan in
`InlineConditionFixAllProvider`. It now requests semantic diagnostics for the
edited document, retaining sequential replanning, saved-text compilation,
call-binding checks and convergence checks. Six additional scope cases cover
both actions across document/project/solution scopes with cross-file bindings.
All 179 focused condition tests pass on the AWS worker.

The harness also accepts `conditions` as its fourth argument. On the same
2-vCPU / 8-GiB worker, with 12 target edits and 20 sites per unrelated document:

| Documents | Before | After |
| --- | ---: | ---: |
| 32 | 5,844 ms | 1,170 ms |
| 256 | 12,999 ms | 832 ms |

Each run excludes its Roslyn/JIT warm-up and asserts compilation, convergence
and unchanged unrelated documents. These single samples are not timing
guarantees. All four outputs have SHA-256
`3BD4EA31C4E14D99EFE0708EE769C815657D5D217ED345284565C84A070153C3`.
The 256-document sample improves about 15.6 times.

```sh
dotnet run --project build/performance/FixAllBenchmark.csproj -c Release \
  -p:AnalyzerDirectory=/absolute/path/to/analyzers/dotnet/cs -- 256 12 20 conditions
```
