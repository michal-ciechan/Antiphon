# CARD-0478 Code continuation: named G methods and crash matrix

Date: 2026-09-11. Task `27871064`. Branch `feat/card-task-48888860` from `d3dff839`.
Worktree `C:\Antiphon\worktrees\card-task-27871064`.

Ordinary Code for the remaining named G methods plus V-4, V-9 crash, compiled V-12,
and V-15/V-16. **Do not land or deploy.** All **230 PCs remain pending**.

## What ran

Windows 10.0.19045 x64, .NET 9.0.16. Isolated `OutputPath=bin-c478-2787*/` with
forward slash. No production runner 17204. No paid provider.

| Filter | Result | Duration | TRX |
|---|---|---|---|
| PostLandMutationContractTests + Publication + ReceiptPolicy | 88/88 | 37s | `.antiphon/c478-2787-unit/unit.trx` |
| PostLandMutationDeliveryTests (V-9 crash matrix) | 53/53 | 47s | `.antiphon/c478-2787-delivery3/delivery.trx` |
| PostLandMutationAdmissionTests (after G-8/17/18 fix) | 27/27 with G-17/G-18 re-run green | 8m + 37s + 38s | `.antiphon/c478-2787-admit2/admit.trx`, `c478-2787-g017`, `c478-2787-g018` |
| PostLandMutationWorkflowTests | included in first admit run 43/46 then remaining green with admission | 8m | `.antiphon/c478-2787-admit/admit.trx` |
| PostLandMutationWorktreeTests (V-4) | 32/32 | 10m55s | `.antiphon/c478-2787-worktree/worktree.trx` |
| PostLandMutationCleanupTests | 44/44 | 17m05s | `.antiphon/c478-2787-cleanup/cleanup.trx` |
| PostLandMutationCustodyTests | 51/52 then G-223 restored 1/1 | 19m57s + 43s | `.antiphon/c478-2787-custody/custody.trx`, `c478-2787-g223` |
| HostCustodyTests (V-15 named) | 17/17 | 5s | `.antiphon/c478-2787-host/host.trx` |
| RunnerCustodyTests V-15/V-16 after G-208 fix | V-15 1/1, V-16 1/1, G-208 1/1 | 7s + 2s + 2s | `.antiphon/c478-2787-v15`, `v16`, `g208b` |
| PtyCustodyTests | 30/30 | 9s | `.antiphon/c478-2787-pty/pty.trx` |

Workflow: the first combined admission+workflow run was 43 passed / 3 failed (G-8/17/18). After those three were fixed, workflow was not re-run as a class; those methods were already green in that 43.

## V-12

`PostLandMutationContractTests.C478_V12_CompiledSelectorAndBundleHashes` checks compiled Code/Review/Mutation bundle versions and client `sourceLanding*` field names. Loaded server/runner/host capability probes after canonical main-checkout deploy remain caller-owned. Health alone is insufficient. `restart: none` from this worktree.

## PCs

PC-1..PC-230 pending for post-land Mutation at confirmed O.VerifiedSourceSha.
