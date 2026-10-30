# C1379 land restart replay Debug

Task 472dd1cb; base `335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f`; Linux runner mirror.

## Initial evidence

Windows qualification d47848cc, candidate cde8def41, group 79: 288/289 passed,
C1379_OldHeldReplaysBeforeConcurrentNewRequest canceled at original test line 92
(the 20 ms database-observation polling delay), with cleanup at line 102.
The test shares a fixed 30-second CancellationTokenSource across startup, first
consumption, terminal-state polling and second consumption. The token is supplied
to BackgroundService.StartAsync, which also links it to execution lifetime.
Classification remains under investigation; cancellation alone does not prove
an ordering error or a Windows-only cause.

The real hosted reader requires reconciliation and calls ReconcileQueueAsync
before each selection; the queue sorts complete durable requests by RequestedAt
then request identity. A fresh provider clears volatile queue state. The test
uses real fixture-owned Git repositories and an isolated PostgreSQL database.
CARD-0331, CARD-1379 and CARD-1364 public card descriptions were read with
scripts/card.ps1; no private notes were read.

## Verification plan and evidence

Pending: unchanged exact case at least 50 alone and 30 with recorded concurrent
build load through the host slot gate. TEMP=/tmp, UseAppHost=false, isolated
producer-owned bin-c1379-debug/ outputs. Preserve per-invocation logs/TRX and
timing under .antiphon/c1379-debug (gitignored). No native run, live qualification
mutation, tags, publication, definition update, rebase or rc branch mutation.

The task's exact narrow selections override ordinary broad-suite defaults: this
Debug is for one qualification failure. Additional runs will be named and justified.

## Owed native verification

Do not run until Windows qualification d47848cc releases the native lock.
Windows: LandRestartReplayTests class once; then the exact case 20 times alone
and 10 times under recorded load, retaining per-test timing and failure text.
Native coverage remains owed; Linux receipts cannot discharge it.

## Measured outcome (unchanged replay code)

**Classification: observed test-owned deadline cancellation; underlying Windows cause
unconfirmed.** No Linux ordering failure or replay hang reproduced. Calling the Windows
incident a confirmed product race or a confirmed load-only flake would exceed this evidence.
The test, queue, hosted reader, request service, source resolver and fixture files are
byte-identical between qualification candidate `cde8def41` and assigned base `335b4fbb`.
All measurements below used doc-only source `e2d43d6f9847d350e497d690820eab05ec96fe0f`.

| Linux selection | Executed | Passed | Failed/skipped | Case p50 / p95 / max seconds |
|---|---:|---:|---:|---|
| Exact case, initial single host | 1 | 1 | 0 / 0 | 14.55 / 14.55 / 14.55 |
| Exact case, 50 serial native repetitions, no owned load | 50 | 50 | 0 / 0 | 4.45 / 8.57 / 10.98 |
| Same binary, 50 serial repetitions with owned build load | 50 | 50 | 0 / 0 | 3.15 / 8.67 / 12.10 |

No Linux test failed, so there are no Linux failure first lines. The Windows error remains
`TaskCanceledException: A task was canceled.` at original lines 92 and 102.
The 50 loaded trials exceed the requested 30 so the same native-repeat binary can be
reused without another compilation. The brief explicitly commissions these repetitions;
they are diagnosis of the reported qualification failure, not routine repeat-proof churn.
"Alone" means the exact single method, Git lane limit 1, no owned load; ambient host work
continues. It does not mean an idle host.

The loaded driver held one granted six-node slot, used one MSBuild node for each of five
sequential `dotnet build server/Antiphon.Server.csproj --no-restore --target:Rebuild`
commands, and ran the exact test filter concurrently. All five builds exited 0. All 50
case intervals overlap a recorded build interval. The host reports 24 CPUs; sampled
one-minute load ranged 25.35–48.00. Admission wait was 31 s; held time 283 s. Every owned
child was awaited before source changes. The test DLL SHA-256 stayed
`bd6aa1f5567648ec454835806b003f7d647d5e4fbbc8e12fe901204abb8fadc5`, with clean tracked
source and unchanged HEAD before/after. Retained evidence: `loaded-timeline.jsonl`,
`loaded-summary.json`, five `loaded-build-*.log`, `loaded-tests.log`, loaded TRX,
`loaded-50-repeat-analysis.json`, `loaded-50-phases/` and fixture Git JSONL.

The initial build passed (150 s, slot wait 0 s). The initial test host held a slot 107 s
(wait 135 s); opt-in phase evidence attributes 14.31 s to container startup and 64.61 s
to shared migrations, outside the test's deadline. The 50-normal checkpoint reports
346 s admission, 267.74 s build, 65.67 s host startup, 238.94 s test span and 4.56 s
teardown. Its container/migration/clone breakdown is unknown: `-TimingRoot` records
wrapper phases but does not itself enable TUnit phase recording. Per-case durations
and fixture Git call boundaries are retained. Recorded process/DB spans are partial;
uninstrumented EF/filesystem/other time remains unattributed, never counted as zero.
The loaded host has opt-in TUnit per-case, clone and process phase evidence.

### Receipt configuration mistakes (retained, not hidden)

The first repeat command supplied an incorrectly transcribed expected SHA. It refused
`source_mismatch`, exit 2, before build or tests (zero executions). The corrected command
built and ran 50/50 green but requested `MinExecuted=50`; the repeat reducer applies that
minimum to **each ordinal**, which contains one case. Its preserved wrapper outcome is
exit 2, `repeat_min_executed`; it is not a green checkpoint certificate. The source
receipt still records clean, unchanged source and verified build binding. Read-only
analysis of the original TRX with minimum 1 per ordinal verifies 50 distinct native IDs,
50 complete marker pairs and 50 passed ordinals in one host. This append-only analysis
lives in `baseline-50-repeat-analysis.json`; neither original TRX nor receipt was rewritten.
Loaded repeat validation uses its independently retained nonce and verifies the same
50 native ordinals. Final changed-source verification must use the correct minimum.

### Diagnosis and proposed test repair

The 30-second source is created by the test, not a production land-request timeout.
It covers reconciliation, selection, publication and second selection; it is also
passed to hosted-service startup, coupling observation expiry to worker cancellation.
A pending first request at expiry therefore yields the reported opaque polling-delay
exception. Production Git commands have their own five-minute bound; the hosted reader
and durable reconciliation have no equivalent fixed 30-second land timeout.

The test's ordering assertion currently occurs only after first-request completion;
a stalled/held first request can hide the ordering evidence behind a deadline failure.
Use the existing production `terminal-committed` delivery boundary, then independently
read the durable request and operation. Assert first-selection order immediately,
retain attempt=1 and RemoteConfirmedAt assertions, and keep the newer-second assertion.
No production behavior change or deterministic product-race witness is justified by the
101 passing Linux executions. A Windows filesystem/timer-specific cause is not established.

### Real land graph

`TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1379-land-graph-check -- pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin-c1379-debug/Antiphon.LandGraphAudit.dll`

Exit 0: real MSBuild passes and canary approved, 68 definitions, digest
`7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`.
Slot wait 256 s, held 51 s. No definition file changed; `-Update` was not run.
