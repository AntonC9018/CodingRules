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
