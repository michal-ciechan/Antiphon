# CARD-1032 S2 repair 3 (task f59ffbd2)

D2 is fixed, committed, and pushed. D1, the real phone-home mutation-queue join, was not built.

## Identity

- Branch: `feat/card-task-f59ffbd2`
- Worktree: `/work/worktrees/task-f59ffbd2`
- Code SHA (checkpoint source): `53d7bb91a605e31c9293de89744398c1b308fb3b`
- Parent: `d60d5ca2824eab648f250f5f6f840bd26749ccb7`
- Landing owner: `cac7e685-f97a-401e-8e47-2f1f8babc731`
- This Code task: `f59ffbd2`
- Card: CARD-1032, InProgress. The card body still describes parent S3 removal. This dispatch stayed on S2. Cleanup execution was not enabled.

## D2

A bound mirror now refuses `runner_store_mismatch` before any directory, ledger entry, or completed binding when the request runner or store is not this recipient, or when the recipient has not been bound. `PhoneHomeCommandDispatcher` binds the settings runner id and the runtime store, then refuses before `MirrorAsync`. `BeginMirrorPreparationAsync` reuses a row only when its store is the live store. An old-store row is left incomplete. `CaptureMirrorBindingAsync` does not rewrite an existing row onto another store.

Red on `d60d5ca28`, production unchanged:

- `C1032_StoreMismatchRefused`: `wrongStoreCompleted` was True at `RunnerWorkspaceBindingTests.cs:346`. Slot lease `355c77c5-2a42-4b03-92f5-9a8be03cc79b`, waited=0s, held=40s. Output `tests/Antiphon.SessionRunner.Tests/bin-c1032-store-red/` (deleted after the green run).
- `C1032_StoreReplacementKeepsDebt`: `reusedOldStore` was True at `RemoteWorkspacePreparerTests.cs:529`. The replacement reached a live new store and a second mirror request. Slot lease `96b56e79-a40b-4ce0-b50f-898a49efcb96`, waited=0s, held=232s. Output `tests/Antiphon.Tests/bin-c1032-store-replace-red/` (deleted).

Green after the fix, before the checkpoint, same filters:

- Store mismatch: 1 passed, 0 failed, 774ms. Slot `8f707567-cbef-4ee6-8502-40b43c7d7d65`, waited=0s, held=21s.
- Store replacement: 1 passed, 0 failed, 56s 983ms. Slot `76a783f4-ff1c-45fa-b8e5-c856b46a5942`, waited=0s, held=175s.

## Checkpoint

Run `20261012-185007-8c2d`, plan `docs/superpowers/plans/2026-10-12-card-1032-test-design.md`, rows CP-3, CP-4, CP-17, CP-18, CP-19, CP-20. Exit 0. Source `53d7bb91a605e31c9293de89744398c1b308fb3b`, state=clean, buildSource=verified. Evidence: `.antiphon/checkpoints/20261012-185007-8c2d/report.md`. Every row slot=granted, waited=0s, dirty=0.

| CP | executed | passed | failed | skipped |
|---|---:|---:|---:|---:|
| CP-3 | 5 | 5 | 0 | 0 |
| CP-4 | 4 | 4 | 0 | 0 |
| CP-17 | 1 | 1 | 0 | 0 |
| CP-18 | 1 | 1 | 0 | 0 |
| CP-19 | 1 | 1 | 0 | 0 |
| CP-20 | 1 | 1 | 0 | 0 |

Unlisted, stated reason: the dispatcher mirror admission changed, and the brief names `/*/*/PhoneHomeCommandDispatcherTests/Workspace_*` outside the closed S2 rows. 3 passed, 0 failed. Slot `38da08f7-1bd6-4742-838c-2ddb61e664e6`, waited=0s, held=20s. Those three requests carry no binding, so the new refusal does not apply to them.

## Ordinary V/R for this S2 selection

- V-1: passed on CP-3, CP-4, CP-17, CP-18, CP-19, CP-20.
- R-2: the S2 part passed on CP-3 (`C1032_BindingLegacyCompatibility`). CP-11 and CP-15 are later slices and were not run.
- V-2, V-3, V-4, V-5, V-6, R-1, and R-3 are outside this S2 selection and were not run.

## Publication

- `git merge-tree --write-tree` against fetched `origin/master` `f6d468e719350a34e758768cf51ba31c21c8bb9b` exited 0. Tree `7191b8792ebb11077ed1c92e9280116cc2559963`.
- Evidence diff `d60d5ca28..53d7bb91a`: commits=1, violations=0.
- Restart: none.

## D1 still needed

No test joins S2 preparation through the real phone-home mutation queue to runner storage and a fresh server readback.

Needed path: server preparation (`BeginMirrorPreparationAsync` / `RemoteWorkspacePreparer`) → `PhoneHomeLiveConnection.RequestAsync` → real socket → `PhoneHomeConnectionService.ReceiveLoopAsync` → `PhoneHomeCommandDispatcher.MutationLock` → `RunnerWorkspaceService.MirrorAsync` on a scratch git repository → reply → a new `AppDbContext` read of `TaskWorktreeMirrorBindings`.

Cases still unproven: a busy recipient, an idle recipient, enqueue-then-crash recovery with durable identities, and the refusals `foreign_mirror_owner`, `stale_mirror_binding`, and `runner_store_mismatch`, each asserting `antiphon-mirror-owner.json` and the fresh server binding.

What exists and does not prove that join:

- `PhoneHomeTestHost` plus a scripted peer answers the mirror without running the recipient.
- A direct `RunnerWorkspaceService` call is not the queue.
- `PhoneHomeEpochAgreementTests` opens a real socket to `PhoneHomeConnectionService` and checks heartbeats. Its cwd is `/work`. It does not prepare a binding or read one back.
- `SessionMessageQueuePtyIntegrationTests` is the session input queue, not `MutationLock`.

No CP-21 row was added. The harness is larger than the remaining budget after D2 was pushed.

## Mutation

Not run. Pending: PC-209..PC-222, PC-520..PC-533, including new PC-532 (`wrongStoreCompleted.ShouldBeFalse()`) and PC-533 (`reusedOldStore.ShouldBeFalse()`).
