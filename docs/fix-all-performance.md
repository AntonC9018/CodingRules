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

The stable candidate at `9d24f23` passed all **1,444 tests** (262 analyzer and
1,182 code-fix/package tests) and all **11 packaged consumer gates** on AWS.
The package SHA-256 is
`c3291f06d0dd381455e1328f552c10d01a88c2a31191d48de09453376702eeb5`.
It includes the Boolean decision-value fixer and fluent-chain layout rule.
These checks reuse one candidate archive; they do not publish it.

```sh
dotnet run --project build/performance/FixAllBenchmark.csproj -c Release \
  -p:AnalyzerDirectory=/absolute/path/to/analyzers/dotnet/cs -- 256 12 20 conditions
```


## Real-project unchanged invocations

The stable `9d24f23` package converged on both ScheduleLib targets. An unchanged
broad invocation still took 237 seconds for the solution and 104 seconds for
the CLI test project on the 2-vCPU / 8-GiB worker. After whitespace normalization,
the unchanged invocations took 211 and 104 seconds. Source hashes, rather than
formatter messages, establish that each invocation left the C# files unchanged.
Some hosts log files as formatted even when their final bytes are unchanged.

A subsequent completion pass included CR0003–CR0006, omitted from the original
command. It removed 111 nested-return findings exposed by decision extraction.
Its final unchanged invocations, restricted to the affected files, took 196
seconds for the solution and 80 seconds for CLI tests. These runs use different
source sets and are not a controlled speedup comparison.

Document-scoped reanalysis reduces work after each accepted edit; it does not
remove workspace loading, initial analysis or the retained semantic checks.
Routine formatting should use the consumer's project/path-scoped wrapper;
the broad semantic cleanup is not intended to run on each compilation.
