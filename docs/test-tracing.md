# Trace a slow test chunk

Use the test host's OpenTelemetry activities and the Antiphon JSONL exporter. Start with the original evidence and measure a baseline before changing code. Use [the test performance standards](test-performance-standards.md) for long runs and [the testing guide](testing-and-build.md#build-slots-card-0589) for host leases and process safety.

## 1. Recover the selection and baseline

Record the source SHA, run ID, attempt, suite, chunk ID, platform, host and relevant load. In the run's log root, use `summary.json` to find `timingRoot` and `timingChunks`. Use the suite's `groups-planned-<suite>.json` under that timing root for the planned classes, test IDs, filter and planned seconds. Cross-check with the executed manifest (the terminal `groups-executed.json` or its published journal) and the chunk's TRX and execution receipt. Use TRX results, including skips and durations, to establish which tests ran. Record a missing or partial result explicitly. Keep queue, build and launch time from the run receipt separate from test-host wall time.

From the repository root in PowerShell 7, set `$runRoot`, `$suite` and `$chunkId` from the run evidence, then inspect the matching records.

```powershell
$summary = Get-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Raw | ConvertFrom-Json
$timing = $summary.timingChunks | Where-Object { $_.suite -eq $suite -and $_.chunk -eq $chunkId }
$timing | Format-List
$plan = Get-Content -LiteralPath (Join-Path $summary.timingRoot "groups-planned-$suite.json") -Raw | ConvertFrom-Json
$group = $plan.groups | Where-Object chunk -eq $chunkId
$group | Format-List chunk, classes, filter, plannedTestIds, plannedSeconds
$policy = Get-Content -LiteralPath 'tests/test-execution-policy.json' -Raw | ConvertFrom-Json
$project = $policy.suites.$suite.project
$trxPath = if ($timing.trx) { $timing.trx } else { Join-Path $runRoot "$suite-$chunkId.trx" }
[xml]$trx = Get-Content -LiteralPath $trxPath -Raw
$trx.TestRun.Results.UnitTestResult |
    Sort-Object { [TimeSpan]$_.duration } -Descending |
    Select-Object -First 20 testName, duration, outcome
```

Use the policy and manifest at the recorded SHA. Confirm the TRX test definitions and assembly match the suite. For a class without run evidence, locate its declaration with `rg -n 'class HostStatsEndpointTests\b' tests` and confirm the owning project, including any linked sources.

Preserve the exact launch filter from the receipt, logged arguments or plan. A blank manifest filter can mean a single-class selection. When reproducing a method or group slice, recover its exact filter and compare the new TRX with the original roster.

Look for `test-trace-*.jsonl` in the chunk's `<suite>-<chunk>-execution` directory beside its TRX. Summarise existing evidence first. Use a fresh run when the trace is missing or you need a comparable measurement.

## 2. Capture a trace

From `pwsh`, build one project at a time with `-m:1`, node reuse and shared compilation disabled. Use the worktree's default build output. Run one heavy job at a time and let its test host and owned children exit before starting the next job. The normal commands take a repository build slot. If the operator forbids access to the slot endpoint and supplies local lock scripts, use those scripts for each build or test invocation instead. Retry a failed acquisition and release an acquired lock in `finally`. Apply the task's exclusions and service restrictions before picking a selection.

This example uses a small SessionRunner class, a fresh evidence directory and an up-to-date CLI launcher. For a chunk, use the project, built executable and filter for the recovered selection.

```powershell
$project = 'tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj'
$testHost = './tests/Antiphon.SessionRunner.Tests/bin/Debug/net9.0/Antiphon.SessionRunner.Tests.exe'
$filter = '/*/*/HostStatsEndpointTests/*'
$traceDir = Join-Path $env:TEMP ("antiphon-test-trace-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $traceDir | Out-Null

scripts/antiphon build-slot --label trace-build -- dotnet build $project '-m:1' '-nodeReuse:false' '-p:UseSharedCompilation=false'
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }

$oldTraceDir = $env:ANTIPHON_TEST_TRACE_DIR
$oldOtlp = $env:ANTIPHON_TEST_TRACE_OTLP
try {
    $env:ANTIPHON_TEST_TRACE_DIR = $traceDir
    $env:ANTIPHON_TEST_TRACE_OTLP = $null
    scripts/antiphon build-slot --label trace-run -- $testHost --treenode-filter $filter --report-trx --report-trx-filename run.trx --results-directory $traceDir
    $testExit = $LASTEXITCODE
} finally {
    $env:ANTIPHON_TEST_TRACE_DIR = $oldTraceDir
    $env:ANTIPHON_TEST_TRACE_OTLP = $oldOtlp
}
"Test exit $testExit. Evidence $traceDir"
scripts\antiphon.cmd test-trace-summary "$traceDir"
```

For OTLP export, set `ANTIPHON_TEST_TRACE_OTLP=1` and point `OTEL_EXPORTER_OTLP_ENDPOINT` at an authorised collector. Native chunks write local JSONL by default. A background writer drains a bounded queue and reports dropped spans on orderly exit if the queue filled. Verify the executed count and identity in the fresh TRX even when the host exits successfully. If an OR filter contains `|`, pass it through the PowerShell `scripts/antiphon.exe` launcher or directly to the built host under the authorised lock. The `.cmd` launcher parses the pipe.

For the operator-supplied lock, set `$acquireLock` and `$releaseLock` to the supplied script paths and `$tag` to the assigned tag. Substitute this pattern for each `build-slot` invocation above. For the test invocation, call `& $testHost` inside the lock, pass the same test and TRX arguments, and capture its exit code before releasing the lock.

```powershell
do {
    pwsh -NoProfile -File $acquireLock -Tag $tag
} while ($LASTEXITCODE -ne 0)
try {
    dotnet build $project '-m:1' '-nodeReuse:false' '-p:UseSharedCompilation=false'
    $buildExit = $LASTEXITCODE
} finally {
    pwsh -NoProfile -File $releaseLock -Tag $tag
}
if ($buildExit -ne 0) { throw "Build failed ($buildExit)" }
```

If the script launcher is cold or stale on a laptop capped at `-m:1`, build `scripts/lib/Antiphon.Scripts/Antiphon.Scripts.csproj` explicitly with the authorised lock and flags. Use `./scripts/lib/Antiphon.Scripts/bin/Debug/net9.0/Antiphon.Scripts.exe` in place of the launchers for the permitted commands, including `test-trace-summary "$traceDir"`.

## 3. Read the summary

Each file describes one process. If a chunk has several files, compare their lifetimes against the receipt. Overlapping processes or spans can sum to more than elapsed wall time.

| Output | Interpretation |
|---|---|
| Wall, startup | Wall uses OS process start through the process-exit callback. Startup ends at the first observed TUnit activity. A missing lifetime gives an observed lower bound. |
| Discovery | Observed TUnit discovery work. |
| One-time setup and teardown | Observed session, assembly, class and shared-fixture work. |
| Per-test setup, body and teardown | Observed test lifecycle and body. Setup includes the interval from test-case start to first body when available. Child helpers identify work inside those intervals. |
| Limiter wait | TUnit 1.44 leaves this unobserved. Look for limiter attributes and serial body starts as clues. Queueing cannot be calculated from gaps alone. |
| Process exit/teardown | Tail after the last observed test and teardown work through the exit callback. Retirement and land-delivery workers flush completed local spans before publishing a crash barrier. A forced exit can still lose the exit interval and spans in progress. |
| Idle/unaccounted | Wall minus the union of observed phase intervals. It can contain scheduler gaps and work without spans. |
| Top 20 spans and top 10 classes | Inclusive span durations and summed test-case durations by class. Use them to find cost centres and check nesting before estimating savings. |

A dash means timing was unavailable. Use the JSONL `TraceId`, `SpanId` and `ParentId` to follow helper spans to their owners. Locate each actionable operation using its span label, for example `rg -n -F 'HostStatsTestHost build' tests`. Resolve line numbers against the measured SHA.

## 4. Report and re-measure

Start with the run identity, selection, result counts, observed wall and evidence paths. Add a breakdown table with one row per phase and for each top actionable span or class.

| Phase or operation | Observed ms | Calls | Source file:line | Interpretation |
|---|---:|---:|---|---|
| Copy the phase or span name | From summary or trace | From trace | Exact helper, hook or test line | Inclusive, overlapping, inferred or unavailable |

For top items, cite the code location even when several spans share the same helper. Mark process-level intervals with their instrumentation location. Follow the table with a ranked list of optimisations. Give each an estimated **wall-time** saving or range, the assumption behind it, and the risk to isolation, determinism or coverage.

- Check for per-test `WebApplication`, `WebApplicationFactory`, `SharedApp`, database clones or migrations, process starts and Git repo creation. See whether a per-class or per-chunk fixture can reuse the work and preserve isolation. Count repeat starts and distinguish setup already shared from work worth moving.
- Look for fixed sleeps, poll intervals and real-time deadlines. Consider a signal or fake clock where the test's semantics allow it.
- Find serial work under `ParallelLimiter` and estimate potential savings from shortening work that holds a slot. Treat actual queue wait as unknown.
- Treat deliberate measurement windows, such as those in `C1514HostLoadNativeOverheadTests`, as part of the measurement contract. Bring any proposal to change them to the owner.

Get the owner's decision before changing chunking, lanes, test semantics or removing coverage. For each approved optimisation, change one thing, re-run the same selection under comparable conditions with fresh evidence, and compare wall time, affected spans and TRX membership before proposing the next change.
