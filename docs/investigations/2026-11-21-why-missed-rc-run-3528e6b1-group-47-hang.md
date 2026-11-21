# Why rc run 3528e6b1 missed the group 47 hang

The Windows Full run used `rc/2026-11-30` at
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`. The incident brief records
`antiphon-all-group-47` starting at 15:27:48Z, its last TUnit diagnostic
at 15:31:11Z, and the nightly chunk timeout at 16:27:50Z. The timing
sidecar reported 3,601 seconds of run wall time, 493 seconds of build
time, zero test results and no TRX. The timeout capture reported
`active=2` and roughly 90 Git or console descendants, with one `conhost`
still present. `NativeProcessOwner.WriteHangCapture`
gets `active` from the Windows Job Object process count, so this is
consistent with one TUnit root plus one live console process. The last diagnostic named
`ReviewEvidenceResettlementTests`. The Windows artifacts were unavailable
from the Linux worktree. Those observations identify the area to
investigate. They leave the active method IDs, the wait stacks,
handle ownership and the heartbeat inputs at each sample unresolved.

## Introducing commits and cards

- CARD-0657, `80dee2547d21bc0679b179e4c1e8973af484fb4d`, introduced the
  unjournaled setup Git helper in `RunnerSettlementSyncTests.SyncWorld`.
  It arms a 60-second root-exit timeout and drains both redirected
  streams concurrently. The normal EOF wait, the post-kill root wait
  and the post-kill EOF wait have no deadline. A descendant that retains
  a writer can therefore hold the test open after the root exits.
- CARD-1043, `53ad18ec71ae27b6ec473642fcf545d1f8d00e6d`, introduced the
  resettlement class on master. It uses this fixture for setup, subject
  publication and runner clones. Real lease and settlement calls also
  reach `LandingGit`. CARD-1076 follow-up
  `f9de5cfd7ef7a43f6dd6a432ac5efc683e52d450` explicitly preserved its
  normal-exit drain through EOF to retain success and repository child
  journal custody after a completed mutation. That behavior is exercised
  by `C1076_NormalExitWaitsForADetachedPipeHolderPastTheBudget`.
- CARD-1667, `65631396a44904ed4a90559d4d3041316ae2b667`, wired the
  supervisor heartbeat to launch-tree CPU and whole-directory file totals.
  The change reached the seeded rc with the same inputs.

## Approving reviews and selection

CARD-1043's [S2 CP-4 plan](../superpowers/plans/2026-10-05-card-1043-unbound-review-evidence-plan.md#checkpoints)
selected `/*/*/ReviewEvidenceResettlementTests/*` with nine expected
results. The approving Review task, verification scope and executed
receipt for that slice could not be established from the checkout.
CARD-1076 Interim Review `6cd85b51` ran
`/*/*/(LandingGit*)|(RcRosterRepairTests)|(C1514LandingSplitBudgetTests)/*`
on Linux (99 passed) and asked to preserve the normal-exit drain contract.
CARD-1667 Interim Review `b26811ff` ran the offline supervisor harness
(18/18), orchestration watcher harness (39/39) and
`/*/*/RcQualificationWatcherScriptTests*/*` (2/2) on Linux. It found a
run-ID custody defect. Follow-up Review `9f29e134` is recorded as Clean with next Land at
`639f78a890f35093fe8366a6d2881b1dc649f005`. Its follow-up addressed
run-ID custody and credit after a newer run replaces `last-run.json`.
The qualifying selection for that approval could not be established.

## Outside the selection

The resettlement class was in the historical Integration/Slow selection.
The slow allowlist added it in CARD-1062,
`395ef99063fe4f7f239252f12124f2632dcbf6dd`. The prior Windows measurement
used by the [nightly cost roster](../../scripts/lib/nightly-class-costs.json)
is rc run `79b742de`, group `all-group-33`, 10 of 10 nodes complete and
358 serial test-seconds. The [expected-duration roster](../../tests/Shared/expected-duration-roster.json)
carries the same cost. This is one aggregate prior sample. The history
establishes a known slow class and gives no evidence of an earlier
hour-long hang or a quarantine for this class.

The planned class regression exercises Git commands with normal process
exit and EOF. The stuck-pipe ordering crosses neither its expected
settlement states nor the separate CARD-1076 normal-success assertion.
The 20-minute supervisor budget also had no isolated-chunk progress
witness for an idle test host beside unrelated progress.

## What each test can wait on

| Test | Waits |
|---|---|
| `C1043_ConfirmedNoPushBinds` | Two incident setups, runner push/fetch for the changed tip, Reply, real settlement and DB reads. |
| `C1043_FinalReportWins` | Eight separate setups and settlements, plus the changed-tip Git and DB work in the first variant. |
| `C1043_AppendPreservesHistory` | Setup, Reply, settlement, outcome/obligation/event reads. |
| `C1043_HeaderSnapshotAndGetAgree` | Two setups, foreign-branch publication and push for the mismatch, settlement, DB and service detail reads. |
| `C1043_ContinuingSyncFailure` | Setup and a second controlled lease-busy settlement, with DB state changes in between. |
| `C1043_AutomaticEligibility` | Seven independent repositories and DB clones, Reply/settlement, and eligibility state writes and reads. |
| `C1043_SettlementsSerialize` | Two settlement calls, a two-party rendezvous and an explicit hold before save. The probe uses `FOR UPDATE NOWAIT`. The service's competing row lock uses ordinary PostgreSQL command timeout and its caller token. The test releases the hold in `finally`. |
| `C1043_PreparationBeforeLock` | Setup, Reply, service restart, and real Git observation through the transaction-recording seam. |
| `C1043_SettlementRollback` | Two failure boundaries, service restart, and another real settlement for each. |
| `C1082_PendingContinuationRefusesStrictRebind` | Fixture setup, refused dirty checkout, Reply, then a deliberately held repository lease while advancing the fake clock. |

All setups use task-private repos and DB clones. The common lease-busy
fixture arms its retry timer before notifying the test and advances the
fake clock only after that notification. Its observation and completion
joins already had 30-second guards, while normal setup, settlement and
cleanup had no test-wide timeout. Git uses `CreateNoWindow` with terminal
prompts disabled, and both output streams already start draining before
waiting for exit. The indefinite wait occurs if EOF never arrives after both reads start.
A lone console host and ended Git roots are consistent with that hole.
The capture leaves pipe ownership, credential prompting, Git index
contention and the identities of concurrent tests unresolved.

## Why the idle signal missed the chunk

At the rc SHA, `supervise-rc.ps1` recursively sums file lengths and TRX
counts over both `LogRoot` and `StateRoot`. It passes the **launch
wrapper PID** as `ChunkRootPid`, `PlannedSeconds = $null` and no
`DiagnosticProgress`. Thus `default-unknown-group` at 20 minutes is
selected by construction, even when the nightly plan has a cost for the
active chunk. Any net change in total bytes or TRX count across those trees, including
an unrelated write or deletion, clears `idleSinceUtc`. The supervisor
does not bind these inputs to the active chunk or the host that owns its
TUnit diagnostic.

`hostCpu` holds the cumulative CPU of the wrapper and every descendant
visible in the current Win32 snapshot. `cpuSeconds` is their cumulative
sum at that instant. The hang predicate uses the sum of deltas for PIDs
present in both samples, divided by elapsed minutes. Five CPU seconds
per minute or more clears the quiet interval. An exited child contributes
nothing to later samples. An incoming PID contributes to the cumulative
display, then to the predicate only if it survives into another sample.
A static survivor contributes zero delta. `suspectPids` is populated
only after the continuous quiet interval reaches the budget. Empty
suspects therefore supply no evidence about whether the host was alive.

A replay of the current predicate at one-minute intervals with a
constant zero host-CPU delta and no TRX change flagged at minute 20.
An unrelated one-byte increment every ten minutes suppressed the flag
through minute 55. So did a surviving sibling gaining six CPU seconds
per minute. Replacing a child PID on every sample, with constant CPU
for surviving PIDs, still flagged at minute 20. This demonstrates the
scope error. The exact reset signal in 3528e6b1 requires the retained
heartbeat series and file observations. The timeout snapshot of exited
Git descendants cannot establish historical CPU deltas. A poll error
can also skip a sample and is logged as `HEARTBEAT-ERROR`.

The 20-minute flag is observation-only. Even a timely flag would have
left the chunk running until the independent nightly timeout. TRX is
written at test-host completion. The reported zero is the number of
retained TRX results for this group.

## Follow-up card text

**Detect idle qualification chunks and preserve partial test evidence.**
Bind the observation to the active run, attempt, suite, chunk and actual
test-host PID plus start identity. Watch that host and its owned child
tree and attribute fresh CPU deltas to both separately. Track the active
chunk's diagnostic/phase completion, logs and TRX only. Record the quiet
interval and which signal reset it. When a verified host remains idle
for 20 minutes without attributable completion or I/O progress, emit an
observation containing the current test UIDs, host and child samples,
and output paths. Capture the process tree and bounded wait-stack
diagnostics while it is alive. Preserve the append-only per-case phase
journal even if no TRX is finalized. Report those cases as partial
diagnostics and leave the coverage gap explicit.

If later authorized to stop a stalled chunk, put that action in nightly's
owned-process controller. Confirm identity again, capture evidence, end
that chunk's owned job, record the failed/incomplete attempt, and leave
the rest of the qualification eligible to continue. The observer only
reports. Cover idle host plus unrelated byte churn, idle host plus active
owned child, PID reuse, and missing host identity in the supervisor
harness. Confirm the same behavior on the native Windows launch path.

## Cheapest pre-land check

| Path glob | TUnit filter |
|---|---|
| `tests/Antiphon.Tests/Application/RunnerSettlementSyncTests.cs` | `/*/*/RunnerSettlementGitDeadlineTests/*` |
| `tests/Antiphon.Tests/Application/ReviewEvidenceResettlementTests.cs` | `/*/*/ReviewEvidenceResettlementTests/C1043_SettlementsSerialize` |
| `server/Infrastructure/Git/LandingGit.cs` | `/*/*/LandingGitTests/C1076_NormalExitWaitsForADetachedPipeHolderPastTheBudget` |
| `scripts/qualification/supervise-rc.ps1` and `scripts/lib/qualification-supervisor-heartbeat.ps1` | `/*/*/RcQualificationWatcherScriptTests/*` |

Windows qualification owes the complete `ReviewEvidenceResettlementTests`
class at the integrated source SHA after the native owner releases its
lock, plus the three cases in `RunnerSettlementGitDeadlineTests`. Retain
test UIDs and per-test durations.

```powershell
$results = Join-Path $env:TEMP ('group47-' + [guid]::NewGuid().ToString('N'))
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group47-windows-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group47-windows-tests -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/(ReviewEvidenceResettlementTests*)|(RunnerSettlementGitDeadlineTests*)/*' --timeout 570s --report-trx --report-trx-filename group47.trx --results-directory $results
```

## Linux baseline

At `72565797a7702f4af64f43f51d854ea08071b1d1`, the exact class filter
completed 10/10 with zero failures and skips on Linux. TUnit wall time
was 108.371 seconds. The recovered build took 189.30 seconds after
admission at 13.97 load. The first build with `--no-restore` found
missing/stale assets and ran no tests. The recovered invocation restored
packages, then used the existing isolated Docker PostgreSQL fixture with
`ANTIPHON_LINUX_COMPILER_POLICY=0` and TUnit `--timeout 570s`.

| Test | TRX seconds |
|---|---:|
| `C1043_AppendPreservesHistory` | 0.890 |
| `C1043_AutomaticEligibility` | 8.978 |
| `C1043_ConfirmedNoPushBinds` | 29.515 |
| `C1043_ContinuingSyncFailure` | 0.677 |
| `C1043_FinalReportWins` | 10.937 |
| `C1043_HeaderSnapshotAndGetAgree` | 1.781 |
| `C1043_PreparationBeforeLock` | 1.130 |
| `C1043_SettlementRollback` | 2.434 |
| `C1043_SettlementsSerialize` | 2.221 |
| `C1082_PendingContinuationRefusesStrictRebind` | 1.093 |

This is a single Linux sample. The fixture retains its 60-second
per-command budget and adds a two-second cleanup grace. The
three-minute per-test ceiling is provisional because the prior Windows
class cost lacks a per-test maximum and the longest methods create up
to eight separate fixtures. The named rendezvous guards stay at
30 seconds. Record fresh Windows per-case times to check these budgets.

## Repair

The setup command now shares a real deadline across root exit and both
concurrent reads. On timeout the fixture attempts a tree kill, gives
cleanup two seconds, closes its readers and reports the Git command.
Its normal Git children receive immediate stdin EOF. The settlement
fixture also opts into a bounded drain for the real Git calls through
`LandingGit`. Production callers continue to wait for EOF after a
successful Git root exit under the CARD-1076 journal contract.

The resettlement cases pass TUnit's timeout token through settlement,
repository lease acquisition, fixture Git and the relevant DB calls.
The concurrent settlement and lease-busy helpers name the wait that
overran its guard and cancel and join the operation before releasing
fixtures. The shared test DB clone queue now reports a named timeout
after 60 seconds. The assembly continues to supply the fixture database
and its existing per-query deadlines.

The synthetic witness starts a fake Git child. It covers a root that
stays alive and a child that holds the redirected pipes after its root
exits, through both the setup and settlement runners. Its separate
descendant scope tears the fake processes down even on a red result.
At the pre-fix witness commit `d0aceb96a4ce659f0238b9083c7541c1c93211a4`,
checkpoint `20261121-165133-1a8e` confirmed the root exited while a
descendant remained alive. Both EOF cases hit the outer witness guard
and failed their named-error assertions. The still-live root control
passed. The native Windows process behavior remains to be verified.

## Exemption

None.
