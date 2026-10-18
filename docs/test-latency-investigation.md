# Implementation latency investigation — 2026-10-18

Baseline: `5de49ce2fe8b76f0b2dcc9bccf6ff7ba972c8c7e` on
`feat/schedulelib-follow-up`. Experiments run in `/tmp/codingrules-test-latency`,
branch `investigate/test-latency`. Production code and the implementers’ worktree
were left unchanged. Only the existing NuGet cache and local feed were shared;
experimental `bin`/`obj` directories were independent.

## Finding

Tests directly account for most of the worst observed implementation delay.
The CRLF review fix changed one test file (9 insertions, 6 deletions). Its task
ran from 07:55:24.901Z to 08:16:31.558Z: 21 minutes 6.657 seconds. The code-fix
suite alone took **19 minutes 32 seconds**, approximately **92.5%** of that task.
The focused LF/CRLF regression passed before the full-suite wait.

The slow run ended with 142 analyzer tests passing, and 948 code-fix tests
passing plus two package-integration timeouts:

- `AppliesPackagedStatementOperationFixes`: 11 minutes 54 seconds overall;
  its final CR0302 consumer build reached the five-minute command timeout.
- `AppliesPackagedNamedArgumentFix`: 5 minutes 7 seconds overall;
  its package build reached the five-minute command timeout.

These were subprocess timeouts, not assertion failures in the changed test.
The timeout budget applies **per command**, not per test or suite, so a sequence
of slow commands can consume considerably more than five minutes.

## Normal overhead and intermittent slowdown

A subsequent review run at the same commit passed all 142 analyzer and 950
code-fix tests. Its code-fix testhost took 158.43 seconds (TRX session start to
finish). The two previously failing tests also passed in an isolated reviewer
run: 40.71 and 15.65 seconds respectively. This rules out a deterministic hang
in these examples; it does not identify the exact blocked operation in the old
processes, which had already exited.

During the timeout run, the implementer captured a load average of **35.04**
on this **eight-CPU**, roughly **12 GB RAM** machine, with **4,096/4,096 MB swap**
occupied. During this investigation, `/proc/pressure/memory` and
`/proc/pressure/io` also recorded substantial recent pressure; those counters
are machine-wide and include other projects. Later measurements under lighter
load completed normally. Resource contention is therefore the leading
explanation for the extreme outlier, but full swap alone is not proof of
active swapping, and no historical scheduler/I/O trace was available to
separate memory pressure, CPU contention, and build/restore waits precisely.

## Why six tests cost so much

`CodeFixTests/PackageConsumptionTests.cs` contains six facts covering eight
consumer projects. All six independently create a package tester and add the
same package project. In the current package-testing implementation:

1. Each fact repacks `Package/CodingRules.Package.csproj`.
2. A fresh consumer workspace, package feed, and global package directory are
   created for that fact.
3. Every code-fix consumer performs a build expected to fail on its diagnostic,
   a separate `dotnet format analyzers` process, and a final build.
4. The build commands use `--no-cache` and allow implicit restore; the formatter
   uses `--no-restore`.

A full run therefore requests **6 packs + 16 builds + 8 formatter processes**
inside just six tests, in addition to the outer solution build/test processes.
The facts share a test class, so xUnit’s normal collection scheduling serializes
these package tests. Increasing test parallelism would multiply heavyweight
build/formatter processes and needs separate resource measurements.

Source inspected:
`/home/anton/temp/SourceGenerators/source/SourceGeneration.PackageTesting/LocalNuGet/`
(`LocalNuGetPackageTester.cs`, `LocalNuGetModels.cs`, `DotNetCli.cs`,
`PackageTestWorkspace.cs`). This is the local source corresponding to the
installed `Anton.SourceGeneration.PackageTesting` 1.0.0 dependency.

## Repeated validation multiplies the cost

Ticket 08 ran the full solution suite three times. The logs report code-fix
suite durations of 1:58, 2:12, and 2:31: **6 minutes 41 seconds** of suite time
across the three runs, plus focused tests, builds and separate package gates.
Some work overlapped, so this sum must not be treated as additional task wall
time. The initial analyzer run failed two expectations; later runs also
included additional tests, so these runs were not all redundant.

The task itself lasted about 20 minutes. Test/gate cost explains a substantial
part of it, but this investigation does not attribute all implementation and
review time to testing. The one-file CRLF task provides much stronger causal
evidence because 92.5% of its elapsed time is visible in one test run.

## Measured experiments

All experiments used SDK 10.0.112 and the same source commit. The machine was
shared with ongoing review gates and another project’s tests, so these are
observed timings, not controlled microbenchmarks.

| Run | Result | Elapsed |
| --- | --- | --- |
| Focused ReviewedCompound analyzer tests, existing build | 4 pass | 4.94 s command; 2 s tests |
| Historical CRLF full code-fix suite | 948 pass, 2 timeouts | 19:32 suite |
| Later reviewer full code-fix suite | 950 pass | 158.43 s testhost |
| Fresh-worktree solution excluding package tests | 142 + 944 pass | 66.13 s command; code-fix suite 47 s |
| Package tests alone, existing build | 6 pass | 102.61 s command; approximately 100 s tests |

The package-only tests took 11.94–30.96 seconds each. A partial process-tree
sample captured complete formatter commands taking 5.76–6.42 seconds,
consumer builds taking 1.54–2.06 seconds, and packs taking 3.95–4.28 seconds.
The sampler attached after the run began, so these are samples, not complete
phase totals. Formatter startup/work is material even for small fixtures.
A PATH-based wrapper did not intercept the child CLI commands; timings above
come from TRX, the outer time command, and process observation instead.

Measured focused check (an existing build is required; rebuild after source changes):

```sh
dotnet test AnalyzerTests/CodingRules.Analyzers.Tests.csproj -c Release \
  --no-build --no-restore --filter 'FullyQualifiedName~ReviewedCompound'
# 4 tests passed. WALL=4.94
```

Measured broad local check, from the experimental worktree:

```sh
/usr/bin/time -f 'WALL=%e CPU=%U+%S MAXRSS_KB=%M' \
  dotnet test CodingRules.slnx -c Release \
  -p:WarningsNotAsErrors=NU1902%3BNU1903 \
  --filter 'FullyQualifiedName!~PackageConsumptionTests' \
  --logger 'trx;LogFilePrefix=fast' \
  --results-directory /tmp/codingrules-perf-results
# 142 analyzer + 944 code-fix tests passed. WALL=66.13
```

Package-only measurement:

```sh
/usr/bin/time -f 'WALL=%e CPU=%U+%S MAXRSS_KB=%M' \
  dotnet test CodeFixTests/CodingRules.CodeFixes.Tests.csproj -c Release \
  --no-build --no-restore --filter 'FullyQualifiedName~PackageConsumptionTests' \
  --logger 'trx;LogFileName=packages.trx' \
  --results-directory /tmp/codingrules-perf-results
# 6 tests passed. WALL=102.61
```

The package-only command originally had a PATH wrapper, which forwards to
`/usr/bin/dotnet` unchanged. It added a small outer-process startup cost but
failed to intercept the nested commands; the command above omits that wrapper.
Raw TRX/log files are preserved alongside the workspace report under
`artifacts/test-latency/`. The report is committed only on the investigation
branch; the implementers’ branch was not changed.

## Actions supported by the evidence

- During edits, run the affected tests first. For broad local checks, exclude
  `PackageConsumptionTests`; keep the complete suite as a final gate and in CI.
  This preserves the package tests rather than removing their coverage.
- Coordinate heavyweight validation across implementers/reviewers. Avoid
  simultaneous solution tests and several package-consumer gates on this VM.
  Separate worktrees prevent build-output collisions but do not isolate CPU,
  memory, swap, or disk bandwidth.
- Reuse one exact candidate package for the Python consumer gates; their
  existing package-path argument supports this. CI already passes
  `artifacts/release`. Run the relevant gate while editing and the complete set
  once the candidate is stable.
- For a follow-up in the shared PackageTesting library, investigate a
  suite-scoped packed artifact, explicit restore followed by no-restore builds,
  and per-command timings/progress. Preserve fresh consumer isolation and
  verification of the actual archive. Savings from these changes are not yet
  measured.
- Capture process-tree resource usage and partial command output on future
  timeouts. `ExecuteBufferedAsync` currently reports a timeout command and
  directory but loses the accumulated build/restore output in the exception,
  making a long wait harder to diagnose.

No analyzer optimization or blanket timeout increase is justified by the
measurements collected here.
