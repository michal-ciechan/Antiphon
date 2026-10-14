# CARD-1032 S8 Code evidence: durable outcome and retry composition

Code task f2630af7-f74a-4e6a-b7ad-ec577763f123 (landing owner), branch
feat/card-task-f2630af7, base e8bbdba9513b4fb72463a108d7695d978f1d4fa1 (origin/master
at start). Round 2 (Review 5d4a0d1c findings F1-F3): Code task 567fbd60, branch
feat/card-task-567fbd60, continued from febe8bf8dc9062ce7ccb054769d460aacc75ed8f
without rebase; test, fixture and docs files only. Execution stays inert:
`C1032_RecoveryInertServerComposition`. Restart: none.

## Master census at start

| Item | On master at e8bbdba9 |
|---|---|
| Plan CP-13/CP-14 (labelled S8 in the plan) | The plan's S8 recovery/sync rows. The test design renumbered them: its CP-13/CP-14 are the S7 admission/receipt rows (7 + 7, run as S7 verification); its S8 rows are CP-15 (recovery) and CP-16 (`RemoteWorktreeMirrorTests`). This slice uses the test design's numbering. |
| CP-15 methods `C1017_MirrorCompletesIndependently`, `C1017_LocalProgressSurvivesOffline`, `C1017_LostAckReusesOperation`, `C1032_RecoveryQueuesBusyAndReady`, `C1032_RecoveryCrashHandoffs`, `C1032_RecoveryInertServerComposition` | Absent. |
| CP-16 `RemoteWorktreeMirrorTests` | Present (8 existing methods, unchanged here). |
| `TestHelpers/CompletedCardMirrorFixture.cs` | Present (S7). Extended here. |
| Coordinator retry metadata (`Attempts`, `Retryable`, `NextAttemptAt` columns) | Columns present; the mirror coordinator did not write them. A transport exception escaped `TryAsync`. |

## Production change (round 1; round 2 changes no production file)

`server/Application/Services/CardWorktreeMirrorCleanupService.cs` only:

- A `PhoneHomeTransportException` or `ServiceUnavailableException` from client
  resolution, inspection or removal is saved as retry debt: `Retryable`,
  `NextAttemptAt = now + 5 min`, `Reason` = the problem code. State, operation id
  and request bytes are not changed. Other exceptions propagate.
- A call before a persisted `NextAttemptAt` returns `retry_not_due` and makes
  no client call.
- Each removal request increments `Attempts`. A receipt import and a terminal
  refusal clear `Retryable` and `NextAttemptAt`.

No Program, sweep, route, runner or Execute change. The coordinator's one removal
is the command it sends (`SendAsync` -> `RemoveCompletedCardWorkspaceAsync`), gated
by destructive policy, `NextAttemptAt`, the binding/live-store check, and first
admission or digest-verified replay of the persisted request. The fixture adds no
deletion; `CompletedCardCleanupFixture.DisposeAsync` keeps its ownership-checked
root delete.

## Fixture additions

`CompletedCardMirrorFixture`: per-call client resolution; `BeforeRemoval` and
`AfterRemoval` cut seams; `AttemptedRequests` (request bytes at seam entry, before
`BeforeRemoval`), `SentOperationIds` and `SentRequests` (commands that reached the
inner client); `DisconnectAsync` and `ReconnectAsync` (same boot identity);
`ReplaceStoreAsync` (the `RemoteWorkspacePreparerTests` rig recipe: lease expiry on
a fake host clock, retire/clear authorization, new boot and store);
`HoldMutationTurnAsync` (an earlier unbound WorkspaceMirror blocks inside
`RunnerWorkspaceService.MirrorAsync` under the dispatcher `MutationLock`; the
fixture installs its workspace instance into the dispatcher by reflection);
`RunnerHandlerProbe` (a `DispatchProxy` over the dispatcher's runtime surface that
records inspect/remove handler entries and the removal OperationId);
`ReleaseWhenQueuedAsync` (see F2 below); `Order` (runner-side event sequence);
`MirrorPreparation.Ready/Busy` (the fixture's mirror is prepared through
`PhoneHomeRunnerClient.MirrorWorkspaceAsync` over the socket, not through
`RemoteWorkspacePreparer`).

## Round 2 repairs

F1. D1 and D11-frozen: at the pre-send fault the seam reads the committed endpoint
from a fresh context (`frozen`), and `AttemptedRequests[0]` holds the first
attempt's bytes. After the retry-debt save the test reads OperationId,
RequestDigest and ImmutableRequest again and expects `frozen`'s values, with
`Attempts` 1 and `Retryable` true. After the due retry, `SentRequests` holds one
entry (the retry) and `replayedRequestBytes.ShouldBe(firstRequestBytes)` compares
it with `AttemptedRequests[0]`; the canonical digest of the retry bytes, the sent
and runner-entered OperationIds are checked against `frozen`; the final row has
`Attempts` 2, `Retryable` false, `NextAttemptAt` null. D11-frozen also checks that
the changed report has a different digest from the frozen command's. D9 expects
two sends with equal bytes and the original OperationId twice. LostAck expects two
sends.

F2. `ReleaseWhenQueuedAsync(holder, tested, operation)` loops (yield, no fixed
delay, 30 s Stopwatch bound) until the dispatcher's `MutationLock` has one queued
async waiter and the server connection's outstanding requests are exactly the
holder's WorkspaceMirror plus `operation`; it then checks that no handler for
`operation` has entered, records `queued:<operation>`, records
`released:waiters=<n>` (waiters observed at release) and releases. The holder
records `holder-resumed` after its own release. `C1032_RecoveryQueuesBusyAndReady`
asserts a hand-written runner order per vector, for example removal-busy:
`entered:WorkspaceMirror > entered:CompletedCardWorkspaceInspect > holder-entered >
queued:CompletedCardWorkspaceRemove > released:waiters=1 > holder-resumed >
entered:CompletedCardWorkspaceRemove`, and the runner-entered removal OperationId.
Capabilities bypasses `MutationLock`, so it adds no waiter.

Limits: the waiter count and outstanding set are read by reflection
(`SemaphoreSlim.m_asyncHead`, `PhoneHomeLiveConnection._waiters`); the waiter does
not carry a request id, so the tested request is identified by the outstanding set
and the later handler entry.

## Round 3 repair (F4 of Review 1aefbc9b; CARD-1239)

Code task c734da58, branch feat/card-task-c734da58, continued from
e07168db417fbfe7ac85c98bb1fb7d0b52c26924 without rebase. Fixture file only:
`tests/Antiphon.Tests/TestHelpers/CompletedCardMirrorFixture.cs`. No production,
test-method, queue, transport or outcome assertion changes.

Cause found at source. The fixture's last setup step called
`AgentReportStore.StoreAsync` and asserted `saved.Succeeded` without the reason.
`StoreAsync` links its own `CancelAfter(TimeSpan.FromSeconds(2))` (a constant, not a
parameter or clock seam) around Git custody checks (`GetWorkspaceInfoAsync`,
`rev-parse`, `ls-files`, two `check-ignore`) and a `WriteThrough` write, publish and
readback. It does not touch Postgres, so database readiness is not part of this
race. The Review's heap dump of the timed-out CP-F2 host showed a thread in
`FSync`. The setup therefore depended on Git process starts and fsync finishing
within 2 s.

Repair: the fixture writes the same UTF-8 bytes at the path `StoreAsync` publishes
(`<RepoPath>/.antiphon/reports/<task id>/<sha256>.md`; the seed `.gitignore` ignores
`.antiphon/`). It then checks `git check-ignore` and the store's own
`IsUsableAsync(path, Result)`, which has no wall-clock budget. A false result fails
with the path. The path is the one `StoreAsync` returns for this task, so a later
production `CardDoneArtifactPreservation.EnsureAsync` in the same test finds the
same usable file and keeps `ResultFilePath` unchanged. There is no retry and no
widened deadline. The 2 s budget remains in production calls that tests in these
classes make through `CardWorktreeCleanupExecutor` (`EnsureAsync -> StoreAsync`).
Those are production behaviour under test, and a refusal there surfaces as
`artifact_unpreserved` in the test's own result reason.

Sweep of `tests/Antiphon.Tests/TestHelpers` for a fixture save under a bounded
budget: `StoreAsync` is called only in this fixture. Other `Succeeded.ShouldBeTrue()`
sites (`LandingGitFixture` observer Git reads, `ControlledLandingGitTests`) are Git
reads and tests, not budgeted saves. `DirectoryLinkFixtureWindowsTests`' 2 s
`WaitUntilAsync` is a Windows-only test wait, not a save. No other site changed.

Red-first, forced (scratch, not committed; one build `bin-c1239scratch/` through
build-slot, deleted afterwards). Environment switches selected the old save or the
new write, and `C1239_DELAY` set `AgentReportStore.BeforeIoAsync` to wait 2.5 s at
phase `write` on the budget token (a slow write-through). Filter
`/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryQueuesBusyAndReady`:

| Run | Fixture | Stall | Result |
|---|---|---|---|
| A | old (`StoreAsync`, reason in message) | 2.5 s at write | 1 executed, 0 passed, 1 failed: `C1239 old fixture save refused: io-budget-expired` |
| B | new (this commit) | 2.5 s at write | 1 executed, 1 passed |

The mirror cleanup path calls `VerifyMirrorAsync` and never `StoreAsync`, so B
exercises the same six vectors with the stall installed.

Load, not forced (same scratch build, old and new run side by side; each driver
pinned with `taskset`): round 1 used CPUs 0-3 with 8 pinned spinners (host load
about 16-23). Round 2 used CPUs 0-1 with 8 spinners and two `dd oflag=dsync`
writers on the worktree's filesystem (host load about 35). Old passed 2/2 and new
passed 2/2. Load alone did not reproduce the refusal in two rounds. The brief asked
for five rounds of the three classes; two rounds of the one method were run within
the time budget, and they do not discriminate. The forced stall is the evidence for
the removed race.

Drivers outside the checkpoint table (build-slot, Linux): 1 scratch build, 2 forced
runs and 4 load runs above, plus one `dotnet run --project tools/Antiphon.Checkpoints
-- --help` outside a slot to read the tool's verbs.

## Witnesses

| Method | Case -> mutation -> red | Pre-slice (e8bbdba9 production, tests at da5b99df) | Scratch red (reverted) |
|---|---|---|---|
| `C1017_MirrorCompletesIndependently` | local `Complete` + mirror Pending -> skip target with local `Complete` -> `mirrorDirectoryGone` | passed (S7 behaviour) | round 1 batch A: `mirrorDirectoryGone` false |
| `C1017_LocalProgressSurvivesOffline` | runner disconnected, local eligible -> refuse local while a mirror is pending -> `localDirectoryGone` | failed: `ServiceUnavailableException` escaped the mirror call | round 1 batch A: `localDirectoryGone` false |
| `C1017_LostAckReusesOperation` | real removal, reply discarded at the client seam, reconnect, due retry -> new OperationId on retry -> `distinctRemovalOperationIds` | failed: `PhoneHomeTransportException` escaped | round 1 batch B: `distinctRemovalOperationIds` 2 |
| `C1032_RecoveryQueuesBusyAndReady` | six vectors -> drop work arriving while the turn is busy -> `busyRecipientDirectoryGone` | passed (S6/S7 transport) | round 1 batch A: stopped at a 409 from the mutant, before the oracle |
| `C1032_RecoveryQueuesBusyAndReady` (F2) | busy vectors -> fixture releases the holder at barrier entry -> runner order | n/a | round 2: red at the order assertion for preparation-busy, inspection-busy and removal-busy (`holder-resumed` before `queued:<operation>`) |
| `C1032_RecoveryQueuesBusyAndReady` (F2) | busy vectors -> fixture uses the reviewed barrier (server in-flight >= 2) -> runner order | n/a | round 2: red for inspection-busy and removal-busy (`released:waiters=0`); preparation-busy passed (no capability request precedes a WorkspaceMirror) |
| `C1032_RecoveryCrashHandoffs` | cuts in the table below -> ignore `NextAttemptAt` -> `callsBeforeDue` | failed at I0: `PhoneHomeTransportException` escaped | round 1 batch B: `callsBeforeDue` nonzero at I0 |
| `C1032_RecoveryCrashHandoffs` (F1) | D1, D11-frozen -> replay with a new OperationId -> `replayedRequestBytes` | n/a | round 2: red at `replayedRequestBytes.ShouldBe(firstRequestBytes)` for D1 and for D11-frozen |
| `C1032_RecoveryCrashHandoffs` (F1) | D11-frozen -> replay with the report digest recomputed from the current task -> `replayedRequestBytes` | n/a | round 2: red for D11-frozen (reportDigest differs); D1 passed (its task does not change) |
| `C1032_RecoveryInertServerComposition` | Program Execute=true and the residue sweep -> register the coordinator in Program -> `productionCoordinatorResolved` | passed (inert on master) | round 1 batch A: stopped at Program DI validation, before the oracle |

Round 1 scratch: `/work/evidence/task-f2630af7/scratchA.diff`, `scratchB.diff`.
Round 2 scratch (environment-selected mutants and vector lists in one dirty build
per batch; `git checkout` restored the files): `/work/evidence/task-567fbd60/`
`scratch.diff`, `scratchF2.diff` and one log per run.

## Cuts

| Cut | Tested here | Test |
|---|---|---|
| P0 | no | not covered |
| P1 | no | not covered |
| P2 | no | not covered |
| P3 | no | not covered |
| P4 | no | not covered |
| P5 | no | not covered |
| I0 (inspection request timeout at the client seam) | yes | `C1032_RecoveryCrashHandoffs` |
| D0 | no | not covered |
| D1 (request-limit before send) | yes | `C1032_RecoveryCrashHandoffs` |
| D1 (loss after admission) | no | not covered |
| D2 | no | not covered |
| D3 | no | not covered |
| D4 | no | not covered |
| D5 | no | not covered |
| D6 | no | not covered |
| D7 | no | not covered |
| D8 | no | not covered |
| D9 (SaveChanges fault after a real receipt; fresh coordinator) | yes | `C1032_RecoveryCrashHandoffs` |
| D9 (process death) | no | not covered |
| D10 | no | not covered |
| D11-frozen (task path and report changed after intent) | yes | `C1032_RecoveryCrashHandoffs` |
| D11-store (replacement store on reconnect) | yes | `C1032_RecoveryCrashHandoffs` |
| D11 (policy or proof change) | no | not covered |
| D11 (wall-clock jump, elapsed budget) | no | not covered |
| runner-process restart at any cut | no | not covered |

PC status per variant: test design, section "S8 implementation status (amendment
2026-10-14)".

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-15 | S8 | `tests/Antiphon.Tests -> bin-c1032-s8-server/` | mirror-recovery | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorCompletesIndependently*)\|(C1017_LocalProgressSurvivesOffline*)\|(C1017_LostAckReusesOperation*)\|(C1032_Recovery*)` | V-1, V-2, V-5, V-6 (cut table subset), R-2, R-3 | 6 listed methods; 0 failed/skipped | 6 | 6 | true |
| CP-16 | S8 | CP-15 | legacy-sync | `/*/*/RemoteWorktreeMirrorTests/*` | R-1 | 8 methods; 0 failed/skipped | 8 | 2 | true |
| CP-F2 | S8 | CP-15 | local-adjacent | `/*/*/(CardWorktreeCleanupRecoveryTests*)\|(CompletedCardWorktreeRemovalTests*)\|(CardWorktreeMirrorCleanupTests*)/*` | V-1..6, R-1..3 | three full classes, 69 results; 0 failed/skipped | 69 | 10 | true |
| CP-RG1 | S8 | CP-15 | classification-guard | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata*` | metadata | exact guard; 0 failed/skipped | 1 | 1 | true |

No Unit lane: the brief excludes it. Native Windows and S4/S5 activation stay
deferred.

## Pending Mutation

PCs stay pending for method-scoped SourceLanding Mutation: PC-71, PC-72, PC-82,
PC-285..292 and PC-511; PC-284 and PC-512 have no target in this slice. Retry
guards for Mutation to bind:

| Deliberate defect | Detecting method | Expected red |
|---|---|---|
| retry ignores persisted `NextAttemptAt` | `C1032_RecoveryCrashHandoffs` | `callsBeforeDue` (I0, D1, D11 vectors) |
| transport loss propagates or clears intent instead of retry debt | `C1032_RecoveryCrashHandoffs`, `C1017_LostAckReusesOperation` | exception escapes; `RetryDebtAsync` fields |
| retry sends a new OperationId | `C1017_LostAckReusesOperation`, `C1032_RecoveryCrashHandoffs` | `distinctRemovalOperationIds`; `replayedRequestBytes.ShouldBe(firstRequestBytes)` (D1, D11-frozen) |
| replay rebuilds the request from the current task | `C1032_RecoveryCrashHandoffs` | `replayedRequestBytes.ShouldBe(firstRequestBytes)` (D11-frozen) |
| busy holder released before the tested request queues | `C1032_RecoveryQueuesBusyAndReady` | runner order string (fixture mutant, not production) |

## Checkpoint provenance

Round 1: run 20261013-224931-5b7c, bound, source
6c250d8a9970a0f0461c743e6d7bcb3d60652e0e, state=clean, buildSource=verified,
verdict GREEN exit=0, one build, unlisted: none.

```
CHECKPOINT CP-15 commit=6c250d8a9970a0f0461c743e6d7bcb3d60652e0e build=ok filter=/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorCompletesIndependently*)|(C1017_LocalProgressSurvivesOffline*)|(C1017_LostAckReusesOperation*)|(C1032_Recovery*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-f2630af7/.antiphon/checkpoints/20261013-224931-5b7c/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=6c250d8a9970a0f0461c743e6d7bcb3d60652e0e sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=6c250d8a9970a0f0461c743e6d7bcb3d60652e0e build=reused filter=/*/*/RemoteWorktreeMirrorTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-f2630af7/.antiphon/checkpoints/20261013-224931-5b7c/rows/CP-16/run.trx slot=granted waited=45s dirty=0 source=6c250d8a9970a0f0461c743e6d7bcb3d60652e0e sourceState=clean buildSource=verified
CHECKPOINT CP-F2 commit=6c250d8a9970a0f0461c743e6d7bcb3d60652e0e build=reused filter=/*/*/(CardWorktreeCleanupRecoveryTests*)|(CompletedCardWorktreeRemovalTests*)|(CardWorktreeMirrorCleanupTests*)/* executed=69 passed=69 failed=0 skipped=0 trx=/work/worktrees/task-f2630af7/.antiphon/checkpoints/20261013-224931-5b7c/rows/CP-F2/run.trx slot=granted waited=90s dirty=0 source=6c250d8a9970a0f0461c743e6d7bcb3d60652e0e sourceState=clean buildSource=verified
CHECKPOINT CP-RG1 commit=6c250d8a9970a0f0461c743e6d7bcb3d60652e0e build=reused filter=/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f2630af7/.antiphon/checkpoints/20261013-224931-5b7c/rows/CP-RG1/run.trx slot=granted waited=0s dirty=0 source=6c250d8a9970a0f0461c743e6d7bcb3d60652e0e sourceState=clean buildSource=verified
```

Round 1 drivers outside the table, through build-slot on Linux: 8 builds of
`tests/Antiphon.Tests` to `bin-c1032s8/` and the pre-slice red, fix, diagnostic and
scratch runs listed in that round's commits; outputs `bin-c1032s8/` were deleted.

Round 2: the checkpoint run tests the commit that adds this text; its unedited
CHECKPOINT lines are in the task 567fbd60 Code report. Round 2 drivers outside the
table, through build-slot on Linux, output `bin-c1032s8r/` (deleted at the end):
three compile builds (CS1503, then an MSB3027 apphost collision without
`UseAppHost=false`, then 0 errors), one development run of the two changed methods
at 0c6b2d6d (2/2 passed), and the scratch batches above (F1 build with five runs,
the batch ended by its 598 s shell timeout during a sixth; its fifth run, an earlier
early-release variant that released after the barrier's checks, passed
preparation-busy, which led to the `holder-resumed` observation in 6ee751ec; F2 build with three
early-release runs; three reviewed-barrier runs).

Round 3: the checkpoint run tests the commit that adds this text, all four rows in
one run from a manifest imported from this table with `timeouts.rowMinutes: 40` (the
tool's build deadline); row deadlines stay derived from the table. Its unedited
CHECKPOINT lines are in the task c734da58 Code report.
