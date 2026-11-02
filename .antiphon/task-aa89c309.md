# CARD-0824 S1 review round 2 fix (D-1, D-2) — task aa89c309

Outcome: review f4c293bc D-1 and D-2 fixed on `feat/card-task-aa89c309`. The witnesses were red at the tip, are green after the fix, and each fix's mutant goes red. Checkpoint rows CP-1, CP-8, CP-9, CP-4 and CP-21 all pass and the receipt validates. Both flags stay default off. Master-only.

Base `009759428ccd73994b3241345dcf970858edefed`. Commits:
- `d951e891ecf72fd1f9f2177e506912aa56fcbe27` test: red witnesses
- `292ab1394a1a4a984d3c22640752a927ccaa909e` fix
- `2f73fbf52be4bb1c7bb1e377f20f466c1897263d` checkpoint amendment `docs/superpowers/plans/2026-11-02-card-0824-S1-review-fix2-checkpoints.md` (tested source)
- this report commit

## D-1 design: (b), bounded with an operator release

I chose (b). (a) would treat `slot_unknown` from the old runner as released, and that has three problems:
- It contradicts the reconciler's stated rule for the same answer: never a guessed release stamp.
- The reviewer's fix also said not to guess.
- A runner that has lost its ledger record answers `slot_unknown` while its directory may still hold an unpushed lease, and the server cannot tell those two cases apart.

(b) uses the existing three-strike count and adds one small operator route. The changes:

- `WorkspaceSlotReleaseService` now has shared helpers: `IsUnknownSlot`, `UnknownAttemptsAsync` and `RecordUnknown`. The reconciler and the drain rebind count the same `workspace_slot_release_unknown: slot S generation G` events, so a slot gets 3 asks in total, not 3 per path.
- `AgentTaskDispatcher.ReleaseSlotBeforeRebindAsync` now returns Released, Pending or Stuck:
  - A 409 `slot_unknown` records an attempt. The third attempt also writes the `workspace_slot_release_stuck` event.
  - Once the count is spent, the old runner is not asked again.
  - The hold becomes `DispatchHoldDetails.WorkspaceSlotReleaseStuck`, which names the route. It is a stable text, so it dedupes like the pending hold.
  - Every other refusal or lost reply still gives the unbounded `workspace_slot_release_pending` hold. Generation mismatch keeps holding, as the review asked.
- New operator route `POST /api/session-runners/{runnerId}/workspace-slots/{slotId}/record-release`, body `{taskId, generation, reason}`:
  - It needs the operator token (403 without it) and a reason of 1 to 200 characters (400 otherwise).
  - It refuses 409 unless the task is bound to that runner, slot and generation, has no recorded release yet, and already has the stuck record.
  - It stamps `RemoteWorkspaceSlotReleasedAt`, clears the path and writes `workspace_slot_release_recorded: …`.
  - It sends nothing to any runner and touches no directory. The next tick rebinds.
  - It also resolves the reconciler's terminal stuck case: the stuck sentence now names the route instead of saying "record the release by hand".
- Docs updated: `docs/session-runtime-invariants.md` (the D-3 rule) and `docs/ops-http.md` (the route).

## D-2

`CreateAgentTaskRequest.WorkspaceColdReset` now carries `[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]`. The new witness `C0824_DefaultHandoffDigestIsThePreSlotDigest` (CP-4, next to V-12) asserts three things:
- A default request omits the property.
- `HandoffRequestDigest(new CreateAgentTaskRequest("goal"))` equals the pinned pre-change digest `5DCDE154371E04DE1AD48AB39D1A7214CAF4E1D9677A6672B56CB45A85460023`.
- A request with `WorkspaceColdReset = true` emits the property and its digest differs.

How the pinned digest was checked: I called the real `AgentTaskService.HandoffRequestDigest` through reflection in a master build's `Antiphon.Server.dll`. That build is worktree task-296074ab at 143449423. Its record has 41 parameters ending at `FreshWorktree`, and `AgentTaskDtos.cs` there has no diff from ead93feb0, the pre-S1 base. The probe console was built under build-slot lease a25bf91a (held 5s) in my scratchpad.

The review's `0866E7AC…` digest comes from its one-property model record, not from the real 41-parameter record, so it is not the right pin for this DTO.

## Red, green, mutants (outside the checkpoint tool, gated, native output)

| Run | Lease | Result |
|---|---|---|
| red build at d951e891 | 24a9f513 | build waited 75s |
| red run | 03f012f7 | 2/2 failed. D-2: `TryGetProperty(WorkspaceColdReset)` was True. D-1: record-release was 404 (no route) |
| green build at 292ab139 | ea1fffd3 | build ok |
| green run | 2f383a49 | 2/2 passed |
| mutants, one build and run | 5062c0e6 | 2/2 failed |

The two mutants were batched because they touch different files and methods:
- M-R2-D1: the dispatcher's `slot_unknown` catch disabled (`when (false && …)`), so the refusal is a plain failure again. Red: release asks `should be 3 but was 5`.
- M-R2-D2: the JsonIgnore attribute removed. Red: "the default is omitted", True.

Both files were restored with `git checkout --`. The tree was clean before the checkpoint run, which then ran green at the restored commit.

Unlisted builds and runs, with reasons: the red and green witness proofs and the mutant cycle above; the digest probe; the `tools/Antiphon.LandGraphAudit` Release build (lease de9ba461) that `-Check` needs as its evaluator, with its `bin-land-audit` directories deleted afterwards. No other test runs.

## Checkpoints

Tool run `20261102-111533-49d8`, plan `docs/superpowers/plans/2026-11-02-card-0824-S1-review-fix2-checkpoints.md`, `--expected-source-sha 2f73fbf52be4bb1c7bb1e377f20f466c1897263d --total-timeout 70m --hard-wall-timeout 75m`.
- Exit 0, wall 22m10s, 5 rows green, 0 red, 0 skipped. Unlisted: none.
- `validate-checkpoint-receipt.ps1`: `CHECKPOINT SOURCE VALID source=2f73fbf52be4bb1c7bb1e377f20f466c1897263d rows=5`.
- V-12r is inside CP-1 and V-12 is inside CP-4; both green.

```
CHECKPOINT CP-1 commit=2f73fbf52be4bb1c7bb1e377f20f466c1897263d build=ok filter=/*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotResetTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-aa89c309/.antiphon/checkpoints/20261102-111533-49d8/rows/CP-1/run.trx slot=granted waited=150s dirty=0 source=2f73fbf52be4bb1c7bb1e377f20f466c1897263d sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=2f73fbf52be4bb1c7bb1e377f20f466c1897263d build=reused filter=/*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotRehomeTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-aa89c309/.antiphon/checkpoints/20261102-111533-49d8/rows/CP-8/run.trx slot=granted waited=180s dirty=0 source=2f73fbf52be4bb1c7bb1e377f20f466c1897263d sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=2f73fbf52be4bb1c7bb1e377f20f466c1897263d build=reused filter=/*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotColdResetTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-aa89c309/.antiphon/checkpoints/20261102-111533-49d8/rows/CP-9/run.trx slot=granted waited=30s dirty=0 source=2f73fbf52be4bb1c7bb1e377f20f466c1897263d sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=2f73fbf52be4bb1c7bb1e377f20f466c1897263d build=ok filter=/*/Antiphon.Tests.Application/C0824WorkspaceSlotServerTests/* executed=24 passed=24 failed=0 skipped=0 trx=/work/worktrees/task-aa89c309/.antiphon/checkpoints/20261102-111533-49d8/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=2f73fbf52be4bb1c7bb1e377f20f466c1897263d sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=2f73fbf52be4bb1c7bb1e377f20f466c1897263d build=reused filter=/*/Antiphon.Tests.Application/PhoneHomeRollingRunnerTests/* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-aa89c309/.antiphon/checkpoints/20261102-111533-49d8/rows/CP-21/run.trx slot=granted waited=45s dirty=0 source=2f73fbf52be4bb1c7bb1e377f20f466c1897263d sourceState=clean buildSource=verified
```

## Gates

- `scripts/audit-land-verification-graph.ps1 -Check` (no `-Update`): `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69`, `approved`. The digest is unchanged.
- `check-evidence-diff.ps1 -BaseRef 009759428ccd73994b3241345dcf970858edefed -HeadRef 2f73fbf52`: commits=3 entries=0 violations=0.
- `origin/master` `3a49b60133327f4441f06698ef3369b1d277d049`: merge-tree exit 0, tree `c90f6ec269b9c96528a5d4d6858c6ffcfc1cb9ba`.
- `origin/rc/2026-11-01` `e114313d36ef0892676c4e0f4be2c956808c7978`: merge-tree exit 0, tree `f5bde48acd653d399ede4a62184a3f9e4274032a`. The feature stays master-only.

## Pending for SourceLanding Mutation

All plan positive controls stay pending for method-scoped SourceLanding Mutation; review f4c293bc counts 74. The two new mutants are candidates for PCs:
- M-R2-D1: `/*/*/PhoneHomeRollingRunnerTests/C0824_DrainSlotUnknownIsBoundedAndAnOperatorRecordsTheRelease`
- M-R2-D2: `/*/*/C0824WorkspaceSlotServerTests/C0824_DefaultHandoffDigestIsThePreSlotDigest`

Not run this round: the whole Unit lane, V-42 and V-56, as in the earlier rounds of this card.

## Follow-up note (advisories, not fixed)

- Two ownership-proof components stay green when mutated: the primary-`.git` and registration-parent check, and the back-pointer `PathsEqual`. Each needs an isolating witness. This is a card, not a blocker.
- `ReleaseQuarantineAsync` removes the recorded quarantine path without `ProveOwnership`.
- `git clean -ffdx` in a cold reset is wider than the keep-list, so there is a window between preflight and clean.
- Open question 3 from the review: `RunnerId` rewrites for a slot-bound task outside the drain re-arm are still not audited.
- The witness's D-1 red at the tip was the missing route (404), reached before the bound assertion. The bound itself is shown red by M-R2-D1.
