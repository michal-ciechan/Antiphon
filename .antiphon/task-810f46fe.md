# CARD-1769 review repair (task 810f46fe)

Round 1 to round 2. Branch `feat/card-task-810f46fe`. Worktree `/work/worktrees/task-810f46fe`. Base for this repair `956736c959ca969ccee04c56c4fbe6c4c168e6cf`.

## Outcome

B-1 and B-2 are fixed. The hosted park stamp is the pre-reconcile page read (session status, StartedAt, prompt sequence, plus the note's queue Status, DeliveryAttempts, DeliveryVerdict, and CanceledAt). A change that lands during reconcile is visible on the next pass. A parked note wakes when that queue row changes. Delivery semantics otherwise are unchanged. Advisories A-1 through A-6 were left as they were. No migration. Restart: none.

Red `12f433bd93ce8f6a694ab612144ed128fe5b1bca`.
Fix and tested code `ce17e53dec7dd8f03b30a8074f053e07b62b5185`.
Diff since `956736c959ca969ccee04c56c4fbe6c4c168e6cf`: those two commits, two files, +298/−45.

- `server/Infrastructure/Orchestration/AgentTaskLandNotificationHostedService.cs`
- `tests/Antiphon.Tests/Application/AgentTaskLandNotificationRescanTests.cs`

`tests/test-execution-policy.json` is unchanged.

## Measures

Red (pre-fix behavior, witnesses committed): `beforeCommands=6047 beforeUpdates=1508 afterCommands=14 afterUpdates=0`. Failures were exactly: queue delivery did not reconcile the parked note; queue cancel did not reconcile the parked note; queue cancel left the note AwaitingReceipt; status change landed during the pass was absorbed and the next pass skipped the note.

Green, including the official touched-class rerun of the restored tip: `beforeCommands=6093 beforeUpdates=1508 afterCommands=15 afterUpdates=0`. The quiet pass is one extra statement (the parked queue-id read) and 0 updates. The historical quiet pass at `956736c95` was 14 commands and 0 updates.

## Verification

Touched classes, one lease: `AgentTaskLandNotificationRescanTests`, `AgentTaskLandNotificationReconcileLoadTests`, `AgentTaskLandNotificationRecoveryTests`, `AgentTaskLandNotificationPersistenceTests`. Lease `8a5afb1d-da26-4686-bdee-6c8296209653` waited 75s held 240s. 65 passed, 1 failed. The failure is inherited `C467_V17_UpgradeAndLegacyEvidence` (`HostLoadRetentionCatchup.Down` throws on downgrade). Present at the repair base. Rescan passed on the 15/0 measure.

Guards, one lease: `TestClassificationPolicy*`, `TestClassificationGuard*`, `SlowTestRegistryMerge*`, `SlowTestTripwire*`, `NativeLinuxPolicyWitnessTests*`, `MechanicalPcContractTests*`, `TaskPlatformGuidanceTests*`, `C1729LayeringGuardTests*`. Lease `607e3f54-128e-4b73-ae4e-bd57b188af86` waited 0s held 139s. 75 passed, 0 failed.

Official rebuild of the restored tip: lease `7f3388d9-80a5-45de-b4d6-73b9d37c6bea` waited 30s held 268s, 0 errors. Native default output, `UseAppHost=false`, `TEMP=/tmp`.

## Slots

| Label | Lease | Waited | Held |
|---|---|---|---|
| c1769-red-build | 17ae2d91-8c6f-44fa-869d-e4416f71331a | 105s | 271s |
| c1769-red-rescan | 795e6e93-cb8c-4237-9311-b1d36c23d76d | 0s | 60s |
| c1769-fix-build | 95292f27-b0eb-4ba8-8ad5-a4d673b26cf1 | 0s | 173s |
| c1769-green-rescan | c73438e9-9d43-40c0-a27f-e2b79275a6d1 | 0s | 73s |
| c1769-mutant-queue-build | 9bf6a097-5401-4f31-b09e-ef0382e5a01d | 0s | 396s |
| c1769-mutant-queue | f95ab0ed-3fd8-4906-9821-8a6d396a9724 | 0s | 121s |
| c1769-mutant-stamp-build | 59b4ad66-8e20-4ffd-87ee-23a549cc5224 | 166s | 537s |
| c1769-mutant-stamp | c7e34aaf-54a8-4d94-a205-dddf831866f6 | 421s | 85s |
| c1769-official-build | 7f3388d9-80a5-45de-b4d6-73b9d37c6bea | 30s | 268s |
| c1769-touched | 8a5afb1d-da26-4686-bdee-6c8296209653 | 75s | 240s |
| c1769-guards | 607e3f54-128e-4b73-ae4e-bd57b188af86 | 0s | 139s |

## Local mutants (restored, not committed)

M-queue-ignored: `Unchanged` returned before the queue compare. Delivery and cancel went red; mid-pass status stayed green. Failures: queue delivery did not reconcile the parked note; queue cancel did not reconcile the parked note; queue cancel left the note AwaitingReceipt.

M-stamp-after: after the reconcile boundary, the pass re-read session and queue stamps and remembered those. The mid-pass follow-up went red ("status change landed during the pass was absorbed and the next pass skipped the note"). Delivery and cancel stayed green.

Both mutants were removed with `git checkout -- server/Infrastructure/Orchestration/AgentTaskLandNotificationHostedService.cs`. The official suite ran against a rebuild of the clean tip.

## Staleness

Database-visible session status, StartedAt, UserPrompt/QueuedUserPrompt sequence, and queue Status, DeliveryAttempts, DeliveryVerdict, and CanceledAt are reconciled on the next hosted pass (the 5s period, plus the rest of an in-flight pass, plus that pass's stamp read). The remembered stamp is the pre-reconcile page read, so a change during reconcile is visible on the next pass. A runner-only receipt that changes none of those fields still waits the per-note backoff, at most 5 minutes. The park is process-local. A restart reconciles once, then parks. The quiet pass adds one queue-id read and writes no updates.

## Evidence and merge

`scripts/check-evidence-diff.ps1` over `956736c959ca969ccee04c56c4fbe6c4c168e6cf..HEAD` at the tested code: commits=2 entries=0 violations=0.

`git merge-tree --write-tree origin/master HEAD` exit 0, tree `b619925d5d9750187a750051bfabf0447127dd8c`. `origin/master` at that fetch was `6ccf8896e61a121bb2b34f1d19bcdf344dc51dfa`. Not merged.

## Pending for method-scoped SourceLanding Mutation

The card names no PC-n. These witnesses stay pending: park consulted, status wake, prompt-sequence wake, StartedAt wake, backoff expiry, Canceled excluded from the page, compiled tag on NoteById, mid-pass status absorption, delivery-wake, cancel-wake.
