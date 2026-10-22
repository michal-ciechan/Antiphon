# CARD-1463: the task worktree is residue after the verification window (option b)

Operator decision 2026-10-22: option (b) of CARD-1463. Brief-scoped Code task 8a0bbf02, no separate
Plan stage. Evidence: `docs/investigations/2026-10-22-rc-windows-qualification-interim-reds.md`
section 3.A (commit 7d47ae6ca) and `docs/investigations/2026-10-22-card-1449-land-protocol-base-reds.md`
(C688 caveat).

## Decision

Since CARD-1379 (`f612cbe60`, rc pick `20c38faa8`) `AgentTaskLandingProtocol.ValidateTicketAsync`
validates every checkout the ticket names after the verification window, including the task (source)
worktree when `ticket.SourceCanonical` is non-null, and refuses the land on an index lock, an active
sequencer (`verification_sequencer_changed`) or tracked dirt (`verification_tracked_tree_changed`)
found there. CARD-0688 D-2/D-12 made the task worktree's state guarded-cleanup residue, and 21
behavioural tests still assert that contract.

Option (b), as implemented here:

- The land worktree keeps its three post-window refusals unchanged: index lock
  (`git_index_lock_held`/`git_index_lock_stale`), `verification_sequencer_changed`,
  `verification_tracked_tree_changed`, then `land_worktree_changed`.
- The task worktree keeps the same three probes in the same order and argv (the CARD-1449 C688
  inventory: `rev-parse --path-format=absolute --git-path index.lock`, `rev-parse --absolute-git-dir`,
  `status --porcelain=v1 -z --untracked-files=no`), but they are observations, never refusals. A
  finding (a held or stale `index.lock`, an active sequencer, tracked dirt, a failed status) is
  logged as residue and the land proceeds; guarded cleanup re-inspects the tree and records the
  durable outcome (`Cleanup = Refused`, `LastReason = source_dirty | source_branch_mismatch |
  active_sequencer | <index-lock code>`), exactly as before CARD-1379.
- Read-only: every `LandingGit` child already runs with `GIT_OPTIONAL_LOCKS=0`
  (`server/Infrastructure/Git/LandingGit.cs`, `ExecuteProcessAsync`), which is what
  `--no-optional-locks` sets, so the source `status` refreshes no index and takes no lock. The argv
  is deliberately not changed, so the C688 inventory test stays byte-for-byte. The CARD-1449
  caveat ("runs without `--no-optional-locks`") was about the argv, not the effect.
- Task coordinates: the four `C448_V10_VerificationCannotFreezeOldTaskCoordinates` cases and the
  `LandingProtocolGuardTests.C475_Task_*` cases expect `task_coordinates_changed` when a task
  coordinate, its active operation or its status moves during verification. The earlier ordering is
  restored as a small change: the ticketed re-entry (`RunCoreAsync` with `ticket != null`) judges
  the task row against the ticket's operation first (the DB half of
  `RecheckSourceIdentityAndPinsAsync`, factored into one helper), before the lease is judged
  against possibly moved coordinates (`repository_lease_required`) and before the
  pending-operation identity check (`pending_operation_coordinates_changed`). When the task no
  longer points at the ticket's operation, that operation is still loaded (by the ticket's
  `OperationId`) so the refusal is recorded on it, as the single-pass protocol did.

### Test that pinned the superseded rule

`LandVerificationLeaseWindowTests.C1388_SourceTreeChangeRefusesResume` (CARD-1388 witness of the
CARD-1379 rule) asserted that tracked dirt in the task worktree during the window refuses with
`verification_tracked_tree_changed`. That is the exact behaviour option (b) removes, so it cannot be
reconciled with the decision and is re-pinned (D-12: renamed, not dropped) as
`C1388_SourceTreeChangeLandsWithResidue`: the land publishes, the remote target advances, the
source file keeps its bytes, and cleanup ends `Refused`/`source_dirty`. The four sibling C1388
refusals (ticket field, land-worktree identity, land-worktree tracked tree, land-worktree HEAD) and
C1379 stay as they are. The re-pinned test is committed before the fix and is part of the red
witness (CP-1).

## Files

- `server/Application/Services/AgentTaskLandingProtocol.cs`: `RunCoreAsync` ticket pre-check,
  `ValidateTicketAsync` split into land-worktree refusals and a source observation
  (`ObserveSourceResidueAsync`), `RequireTaskIdentityAsync` factored out of
  `RecheckSourceIdentityAndPinsAsync`.
- `tests/Antiphon.Tests/Application/LandVerificationLeaseWindowTests.cs`: re-pin above.
- `tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs`: comment only on
  `C688_LandNeverRunsGitInTheSourceWorktree` (the inventory is unchanged).
- `docs/orchestration-loop.md`: the CARD-1379 post-window paragraph.

## Invariants

- V-1 (CP-2, CP-3, CP-4): tracked dirt, a staged change, an untracked file or a switched branch in
  the task worktree after the window does not refuse; the land publishes and cleanup records
  `Refused` with `source_dirty` / `source_branch_mismatch`.
- V-2 (CP-3): an active sequencer in the task worktree after the window does not refuse; cleanup
  records `Refused` / `active_sequencer`.
- V-3 (CP-2, CP-5): a task coordinate, active-operation pointer or status moved during the window
  refuses as `task_coordinates_changed` before any push.
- R-1 (CP-4): the land-worktree refusals of CARD-1379/1388 are unchanged.
- R-2 (CP-6): the C688 source-worktree inventory (CARD-1449) and the C688 profile budget are
  unchanged; canonical-checkout residue cases are unchanged.
- No manual acceptance.

## Red witness

CP-1 runs once at the S1 commit (plan plus the re-pinned test, before the fix) in its own run, as a
documented non-receipt row: C488_DirtySourceFfRefuses, C488_SourceSequencerFfRefuses, the four
C448_V10_VerificationCannotFreezeOldTaskCoordinates cases, the seven C475_Task_* guard cases and the
re-pinned C1388 case. The 12 C448_V10_EachAcknowledgedBoundaryRechecksSource cells are covered by
CP-2 after the fix (the investigation records the untracked cells' refusal reason as unknown; CP-2
is where it is read).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1463-red/` | c1463-red | `/*/*/(AgentTaskLandSourceFreshnessTests*)\|(AgentTaskLandBoundaryControlledTests*)\|(LandingProtocolGuardTests*)\|(LandVerificationLeaseWindowTests*)/(C488_DirtySourceFfRefuses*)\|(C488_SourceSequencerFfRefuses*)\|(C448_V10_VerificationCannotFreezeOldTaskCoordinates*)\|(C475_Task_*)\|(C1388_SourceTreeChangeLandsWithResidue*)` | red witness at S1 | 14 executed; red expected: C488 x2, C448_V10 x4, C1388 x1, plus the C475_Task_* cases the window reaches; documented non-receipt row | 14 | 6 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1463/` | c1463-boundary | `/*/*/AgentTaskLandBoundaryControlledTests/(C448_V10_EachAcknowledgedBoundaryRechecksSource*)\|(C448_V10_VerificationCannotFreezeOldTaskCoordinates*)` | V-1, V-3 | 94 executed (90 + 4), 0 failed/skipped | 94 | 14 |
| CP-3 | S2 | CP-2 | c1463-freshness | `/*/*/AgentTaskLandSourceFreshnessTests/(C488_DirtySourceFfRefuses*)\|(C488_SourceIdentityFfRefuses*)\|(C488_SourceSequencerFfRefuses*)\|(C488_PostFfDirtyRefuses*)\|(C488_PostFfIdentityRefuses*)\|(C488_RebaseCannotAdoptLaterHead*)\|(C488_TargetCheckpointStillGuarded*)\|(C488_FastForwardCannotAdoptLaterHead*)\|(C488_LocalIdentityBoundaryMatrix*)` | V-1, V-2 | 9 executed, 0 failed/skipped | 9 | 6 |
| CP-4 | S2 | CP-2 | c1463-window | `/*/*/LandVerificationLeaseWindowTests/*` | V-1 (re-pin), R-1 | 6 executed, 0 failed/skipped | 6 | 8 |
| CP-5 | S2 | CP-2 | c1463-guard | `/*/*/LandingProtocolGuardTests/(C475_Task_*)` | V-3 | 7 executed, 0 failed/skipped | 7 | 4 |
| CP-6 | S2 | CP-2 | c1463-inventory | `/*/*/(AgentTaskLandPublicationTests*)\|(AgentTaskLandRefusedRetryTests*)/(C688_LandNeverRunsGitInTheSourceWorktree*)\|(C688_DirtyCanonicalCheckoutLandsWithResidue*)\|(C688_TargetCheckedOutElsewhereIsResidueNotRefusal*)\|(C688_RealLandProfile*)` | R-2 | 4 executed, 0 failed/skipped | 4 | 6 |

CP-1 is one run at S1. CP-2 to CP-6 are one run with one build at S2 (the fix). `Min` is a floor;
confirm `executed=` in each CHECKPOINT line. No whole-suite run; the Unit lane is not affected (the
change is confined to the land protocol and its integration classes).

## Positive controls pending for method-scoped SourceLanding Mutation

- PC-1: remove the ticketed task-identity pre-check in `RunCoreAsync` ->
  `/*/*/AgentTaskLandBoundaryControlledTests/C448_V10_VerificationCannotFreezeOldTaskCoordinates`
  (`repository` case) red with `repository_lease_required`.
- PC-2: make the source `status` finding a `Require(...)` again ->
  `/*/*/AgentTaskLandSourceFreshnessTests/C488_DirtySourceFfRefuses` red.
- PC-3: make the source sequencer finding a `Require(...)` again ->
  `/*/*/AgentTaskLandSourceFreshnessTests/C488_SourceSequencerFfRefuses` red.
- PC-4: drop the source probes entirely ->
  `/*/*/AgentTaskLandPublicationTests/C688_LandNeverRunsGitInTheSourceWorktree` red (inventory).
- PC-5: drop the land-worktree status refusal ->
  `/*/*/LandVerificationLeaseWindowTests/C1388_TrackedTreeChangeRefusesResume` red.

## RC port

CARD-1379 is on `rc/2026-10-20` as `20c38faa8`; the Windows qualification runs the rc, so this fix
must reach it. The report records whether the commits cherry-pick cleanly onto
`origin/rc/2026-10-20` (merge-tree per commit) and whether the port depends on the
CARD-1427/1428/1432 chain or the CARD-1449 fix `6ee09ad07`, which rc lacks.
