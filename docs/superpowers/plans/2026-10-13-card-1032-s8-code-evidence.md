# CARD-1032 S8 Code evidence: durable outcome and retry composition

Code task f2630af7-f74a-4e6a-b7ad-ec577763f123, branch feat/card-task-f2630af7,
base e8bbdba9513b4fb72463a108d7695d978f1d4fa1 (origin/master at start). Commits
added without rebase. Execution stays inert: `C1032_RecoveryInertServerComposition`.
Restart: none.

## Master census at start

| Item | On master at e8bbdba9 |
|---|---|
| Plan CP-13/CP-14 (labelled S8 in the plan) | The plan's S8 recovery/sync rows. The test design renumbered them: its CP-13/CP-14 are the S7 admission/receipt rows (7 + 7, run as S7 verification); its S8 rows are CP-15 (recovery) and CP-16 (`RemoteWorktreeMirrorTests`). This slice uses the test design's numbering. |
| CP-15 methods `C1017_MirrorCompletesIndependently`, `C1017_LocalProgressSurvivesOffline`, `C1017_LostAckReusesOperation`, `C1032_RecoveryQueuesBusyAndReady`, `C1032_RecoveryCrashHandoffs`, `C1032_RecoveryInertServerComposition` | Absent. |
| CP-16 `RemoteWorktreeMirrorTests` | Present (8 existing methods, unchanged here). |
| `TestHelpers/CompletedCardMirrorFixture.cs` | Present (S7). Extended here. |
| Coordinator retry metadata (`Attempts`, `Retryable`, `NextAttemptAt` columns) | Columns present; the mirror coordinator did not write them. A transport exception escaped `TryAsync`. |

## Production change

`server/Application/Services/CardWorktreeMirrorCleanupService.cs` only:

- A `PhoneHomeTransportException` or `ServiceUnavailableException` from client
  resolution, inspection or removal is saved as retry debt: `Retryable`,
  `NextAttemptAt = now + 5 min`, `Reason` = the problem code. State, operation id
  and request bytes are not changed. Other exceptions still propagate.
- A call before a persisted `NextAttemptAt` returns `retry_not_due` and makes
  no client call.
- Each removal request increments `Attempts`. Complete and terminal refusal
  clear `Retryable` and `NextAttemptAt`.

No Program, sweep, route, runner or Execute change.

Removal paths in touched code, with their gates: the coordinator's only removal is
the command it sends (`SendAsync` -> `RemoveCompletedCardWorkspaceAsync`). That
send is gated by destructive policy, `NextAttemptAt`, the binding/live-store check,
first admission (eligibility, inspection, proof, artifacts, intent transaction)
or digest-verified replay of the persisted request. The fixture adds no deletion.
`CompletedCardCleanupFixture.DisposeAsync` keeps its existing ownership-checked
root delete.

## Fixture additions

`CompletedCardMirrorFixture`: per-call client resolution; `BeforeRemoval` and
`AfterRemoval` cut seams; captured sent operation ids and request JSON;
`DisconnectAsync` and `ReconnectAsync` (same boot identity);
`ReplaceStoreAsync` (the `RemoteWorkspacePreparerTests` rig recipe: lease
expiry on a fake host clock, retire/clear authorization, new boot and store);
`HoldMutationTurnAsync` (an earlier actual unbound WorkspaceMirror blocks inside
`RunnerWorkspaceService.MirrorAsync` under the dispatcher `MutationLock`; the
fixture installs its workspace instance into the dispatcher by reflection);
`ReleaseWhenQueuedAsync` (releases once server in-flight is 2); and
`MirrorPreparation.Ready/Busy` (the fixture's own mirror is prepared through
`PhoneHomeRunnerClient.MirrorWorkspaceAsync` over the socket rather than
`RemoteWorkspacePreparer`, whose capture stays CP-4/CP-21).

## Witnesses

| Method | Case -> mutation -> red | Pre-slice (e8bbdba9 production, tests at da5b99df) | Scratch red (reverted) |
|---|---|---|---|
| `C1017_MirrorCompletesIndependently` | local Complete + mirror Pending -> skip target with Complete local -> `mirrorDirectoryGone` | passed (S7 behaviour) | batch A: `mirrorDirectoryGone` false |
| `C1017_LocalProgressSurvivesOffline` | runner disconnected, local eligible -> refuse local while a mirror is not Complete -> `localDirectoryGone` | failed: `ServiceUnavailableException` escaped the mirror call | batch A: `localDirectoryGone` false |
| `C1017_LostAckReusesOperation` | real removal, reply lost at the client seam, reconnect, due retry -> new OperationId on retry -> `distinctRemovalOperationIds` | failed: `PhoneHomeTransportException` escaped | batch B: `distinctRemovalOperationIds` 2 |
| `C1032_RecoveryQueuesBusyAndReady` | six vectors (preparation/inspection/removal x busy/ready) -> drop work arriving while the turn is busy -> `busyRecipientDirectoryGone` | passed (S6/S7 transport) | batch A: red at the first vector's busy preparation (409 conflict from the mutant), before the oracle |
| `C1032_RecoveryCrashHandoffs` | cuts I0, D1, D9, D11-frozen, D11-store -> ignore `NextAttemptAt` -> `callsBeforeDue` | failed at I0: `PhoneHomeTransportException` escaped | batch B: `callsBeforeDue` nonzero at I0 |
| `C1032_RecoveryInertServerComposition` | Program Execute=true and the residue sweep over an eligible mirror -> register the coordinator in Program -> `productionCoordinatorResolved` | passed (inert on master) | batch A: red at Program DI validation (coordinator dependencies unregistered), before the oracle |

Scratch batches ran on the dirty tree at 379ba5b0 through
build-slot. Diffs and logs: `/work/evidence/task-f2630af7/scratchA.diff` and
`scratchB.diff`. The production files were restored with `git checkout`.

Crash cuts at the mirror level, with their expected outcomes:

| Cut | Fault | Expected and asserted |
|---|---|---|
| I0 | inspection request timeout | Pending retry debt, no intent, bytes kept; early call sends nothing; due retry removes |
| D1 | removal request-limit before send | IntentRecorded retry debt, same request; early call sends nothing; due retry removes with one operation and identical request bytes |
| D9 | receipt import save fails after a real receipt | exception propagates; IntentRecorded, no receipt; fresh coordinator replays the same operation to Complete |
| D11-frozen | task path and report changed after intent | replayed request bytes equal the first |
| D11-store | runner reconnects with a replacement store | `runner_store_mismatch`; old endpoint not Complete; no removal request; mirror, admin and sentinel kept |

Not reproduced at the mirror level in this slice (recorded, not passed): P0-P5
(preparer cuts; CP-4/CP-21 own preparer capture), D0 (CP-13
`C1032_AdmissionIntentOrdering`), D2 (queued request at runner disconnect),
D3-D7 (runner component cuts; CP-10 `C1032_RemovalComponentRecovery`), D8
(the parent `C1017_LostAckReusesOperation` above), D10 (no invalidation is
emitted on this path, so PC-284 has no emitter to mutate; fresh-context
readback is asserted in each removal), and the PC-512 monotonic elapsed budget
(no coordinator budget exists; the transport timer is the phone-home
connection's).

## Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-15 | S8 | `tests/Antiphon.Tests -> bin-c1032-s8-server/` | mirror-recovery | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorCompletesIndependently*)\|(C1017_LocalProgressSurvivesOffline*)\|(C1017_LostAckReusesOperation*)\|(C1032_Recovery*)` | V-1, V-2, V-5, V-6, R-2, R-3 | all 6 listed; 0 failed/skipped | 6 | 6 | true |
| CP-16 | S8 | CP-15 | legacy-sync | `/*/*/RemoteWorktreeMirrorTests/*` | R-1 | all 8 listed; 0 failed/skipped | 8 | 2 | true |
| CP-F2 | S8 | CP-15 | local-adjacent | `/*/*/(CardWorktreeCleanupRecoveryTests*)\|(CompletedCardWorktreeRemovalTests*)\|(CardWorktreeMirrorCleanupTests*)/*` | V-1..6, R-1..3 | all three complete classes, 69 results; 0 failed/skipped | 69 | 10 | true |
| CP-RG1 | S8 | CP-15 | classification-guard | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata*` | metadata | exact guard; 0 failed/skipped | 1 | 1 | true |

No Unit lane: the brief excludes it. Native Windows and S4/S5 activation stay
deferred.

## Pending Mutation

Every PC stays pending for method-scoped SourceLanding Mutation. S8 rows:
PC-71, PC-72, PC-82 and PC-285..292, PC-511, plus PC-284 and PC-512, which have
no target in this slice (above). New retry guards for Mutation to bind:

| Deliberate defect | Detecting method | Expected red |
|---|---|---|
| retry ignores persisted `NextAttemptAt` | `C1032_RecoveryCrashHandoffs` | `callsBeforeDue` (I0, D1, D11 vectors) |
| transport loss propagates or clears intent instead of retry debt | `C1032_RecoveryCrashHandoffs`, `C1017_LostAckReusesOperation` | exception escapes; `RetryDebtAsync` fields |
| retry sends a new OperationId | `C1017_LostAckReusesOperation` | `distinctRemovalOperationIds` |
