# C1379 land restart replay Debug

Task 472dd1cb; base `335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f`; Linux runner mirror.

Outcome: 104/104 Linux executions passed, including 50 normal and 50 loaded unchanged
trials and 3 changed-source executions. Test observation is hardened; the Windows
incident’s underlying cause and native qualification remain unconfirmed.

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
one-minute load ranged 23.85–52.99. Admission wait was 31 s; held time 283 s. Every owned
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

## Implemented test-side repair and performance budget

Only the replay test and this investigation change. Production source is untouched:
ExpectedSourceSha refusals, held episodes, durable reconciliation and ls-remote publication
confirmation remain the same paths exercised by the test. The repair waits for the existing
`terminal-committed` production boundary, then independently asserts nonpending state,
attempt 1 and RemoteConfirmedAt. It checks old-first as soon as selection is observed and
still checks newer-second. A terminal result for a different request fails immediately.
There is no polling sleep or replacement sleep. Observation expiry cannot cancel the worker
via its startup token; the finally block stops and joins that worker. Deadline failures name
the awaited phase and captured real boundary sequence, rather than only a canceled delay.

The observation deadline is provisionally 60 seconds, doubling the Windows incident's
exhausted 30-second allowance. **This is a recorded CARD-1379 native-budget exception,
not a claimed measured 2x Windows p95.** Linux loaded p95 is 8.67 s and does not itself
justify 60 s. Windows p95 is unavailable while qualification d47848cc holds the native
lock. The bounded collection below (class once, 20 alone, 10 loaded) is owed before
claiming a native fix; use it to replace the provisional deadline with about 2x measured
native loaded p95. A product stall remains an open possibility until that evidence exists.
The added early ordering/terminal identity assertions prevent a longer observation budget
from accepting a reordered or missing publication.

Changed-source ordinary verification is the exact class, three serial repetitions in one
host. Shared database startup/migrations happen once per host; database clones and isolated
fixture Git roots remain per case because fresh durable/volatile state is the subject.
Retain opt-in phase recording, fixture Git evidence and native-repeat markers. Measured
normal-build wall is 268 s plus preflight/seal overhead; startup 66 s; three cases are
provisionally budgeted 45 s from the measured maximum 14.55 s. Allow 12 minutes execution
and at most 25 minutes slot admission. This is a narrow Debug verification profile, not a
full assembly/Unit-lane qualification claim. No native invocation is authorized now.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Repeat |
|---|---|---|---|---|---|---|---:|---:|---:|
| CP-2 | test repair | `tests/Antiphon.Tests -> bin-c1379-fixed/` | fixed-replay | `/*/*/LandRestartReplayTests/*` | old-first, committed attempt/publication, newer-second, worker teardown | one case in each of 3 native ordinals; 0 failed/skipped | 1 | 12 | 3 |

The script's repeat minimum is per ordinal: Min=1 here yields three actual executed cases.
The baseline single build/run and real graph check diagnose the reported failure; five
additional builds are the explicitly requested controlled load, not acceptance selections.
No broad unlisted suite or native test was run.

### Owed NATIVE row (deferred by explicit brief)

After qualification d47848cc releases the native lock, on Windows at the reviewed repair:

| Row | Selection | Executions owed | Evidence |
|---|---|---:|---|
| NATIVE-1 | `/*/*/LandRestartReplayTests/*` | class once (currently 1 case) | clean-source TRX, per-case/fixture/process phase timing |
| NATIVE-2 | `/*/*/LandRestartReplayTests/C1379_OldHeldReplaysBeforeConcurrentNewRequest` | 20 alone | native ordinals, loaded-budget comparison, boundary/hold diagnostics on failure |
| NATIVE-3 | same exact case | 10 under recorded build/ambient load | actual overlap, host load, loaded p95, each failure's first lines |

Use the host build-slot gate, isolated outputs, UseAppHost=false, native lock ownership and
fresh results; the checkpoint repeat minimum is 1 per ordinal. Set ANTIPHON_C448_EVIDENCE
and all ANTIPHON_TEST_PHASE_* metadata for the test host only. Do not touch the live
qualification or anything under C:\\Antiphon from this Linux task. Native result: **not run**.

## Final changed-source verification

Repair commit `008d32c80ef99f08e31ca0fdc215f692b2617e65` passed CP-2: 3 executed,
3 passed, 0 failed, 0 skipped; all three native repeat ordinals verified in one host.
`validate-checkpoint-receipt.ps1 -ExpectedSourceSha 008d32c80ef99f08e31ca0fdc215f692b2617e65 -ExpectedRepeat 3`
returned exit 0 (`CHECKPOINT SOURCE VALID`, one row). Source stayed clean and build
binding verified. The exact class contains the same one original test; no test was
removed, skipped or relaxed. All original assertions remain, with extra durable-terminal
and request-identity assertions. No production fix or product red witness is claimed.

CP-2 had zero admission wait, 213.41 s build, 64.79 s startup, 19.45 s test span and
3.97 s teardown (88.20 s test-host wall; slot held 473 s including preparation/sealing).
The three case durations have median 5.97 s and maximum 9.24 s; this is a small sample,
not a Windows p95 estimate. Opt-in fixture phases measured 10.30 s container startup
and 47.84 s migration, with uninstrumented time retained as remainder.

The following CHECKPOINT lines are preserved verbatim, including historical refusals.
The later report-only commit records outcomes; the changed test was verified at the
repair SHA above. Raw generated payloads remain gitignored under `.antiphon/c1379-debug/`.

Initial source mismatch (no execution):

```text
CHECKPOINT CP-1 commit=e2d43d6f9847d350e497d690820eab05ec96fe0f build=n/a filter=/*/*/LandRestartReplayTests/C1379_OldHeldReplaysBeforeConcurrentNewRequest executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=skipped waited=0s dirty=0 source=e2d43d6f9847d350e497d690820eab05ec96fe0f sourceState=unknown buildSource=unknown repeat=50 repetitions=0/50 hostInvocations=1 reason=source_mismatch
CHECKPOINT CP-1 EXIT CODE: 2
```

Baseline diagnostic receipt (50 tests passed; wrapper exit 2):

```text
CHECKPOINT CP-1 commit=e2d43d6f9847d350e497d690820eab05ec96fe0f build=ok filter=/*/*/LandRestartReplayTests/C1379_OldHeldReplaysBeforeConcurrentNewRequest executed=50 passed=50 failed=0 skipped=0 trx=/work/worktrees/task-472dd1cb/.antiphon/c1379-debug/baseline-50-corrected/CP-1-20261030-184257-71a8/run.trx slot=granted waited=346s dirty=0 source=e2d43d6f9847d350e497d690820eab05ec96fe0f sourceState=clean buildSource=verified repeat=50 repetitions=0/50 hostInvocations=1
CHECKPOINT CP-1 EXIT CODE: 2
```

Changed-source acceptance receipt (exit 0):

```text
CHECKPOINT CP-2 commit=008d32c80ef99f08e31ca0fdc215f692b2617e65 build=ok filter=/*/*/LandRestartReplayTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-472dd1cb/.antiphon/c1379-debug/fixed/CP-2-20261030-190835-9363/run.trx slot=granted waited=0s dirty=0 source=008d32c80ef99f08e31ca0fdc215f692b2617e65 sourceState=clean buildSource=verified repeat=3 repetitions=3/3 hostInvocations=1
CHECKPOINT CP-2 EXIT CODE: 0
```

## Restoration and handoff

All build/test/load children exited and every build slot was released before cleanup.
After inspecting producer build logs and exact paths, all 44 task-owned alternate output
roots were removed with literal, non-forced commands: `bin-c1379-debug`,
`bin-c1379-base50`, `bin-c1379-load` and `bin-c1379-fixed` in this assigned worktree only.
No remaining root from that inventory exists. Build-source stamps were copied to the
evidence root before deletion; TRX, phase/Git JSONL, logs and the output inventory remain.
The forceful command form was rejected before execution; non-forced removal succeeded.
No source mutation, native test, live qualification action, tag, publish, rc rebase or
change under C:\\Antiphon occurred. No definition file changed; the real graph Check passed.

Pushed slices before this final evidence amendment:

| Outcome | Full commit |
|---|---|
| Initial Debug scope | `e2d43d6f9847d350e497d690820eab05ec96fe0f` |
| Unchanged trials and classification limits | `c4179ec1c292145877b4c6689364411e97ea1168` |
| Test observation repair, verified CP-2 | `008d32c80ef99f08e31ca0fdc215f692b2617e65` |

Caller handoff: quick Opus check, then adopt in both rc and master through the normal
landing path. Preserve the owed NATIVE-1/2/3 obligation after qualification d47848cc
releases its native lock. Windows load-only flake versus a product stall remains an open
diagnostic question; this report does not certify the native failure fixed.
