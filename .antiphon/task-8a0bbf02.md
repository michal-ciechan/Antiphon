# CARD-1463 Code task 8a0bbf02: the task worktree is residue after the verification window (option b)

Outcome: implemented and green. 21 inherited reds plus 6 same-mechanism guard reds fixed; one CARD-1388 test
re-pinned to the operator's decision. Tested SHA `6559f74007d31a52c4435b6720ee46e291e6bb89` on
`feat/card-task-8a0bbf02` (worktree `/work/worktrees/task-8a0bbf02`, server2). Branch base
`bb43ec3529b9d8ad2a799f81080b9e34d89fa9c9`. Plan:
`docs/superpowers/plans/2026-10-22-card-1463-source-worktree-residue-plan.md`.

## Commits (all pushed)

| SHA | Slice |
|---|---|
| `1802b56ee64322a579213ccc2dc502e1f574d296` | plan document (S1) |
| `5d8e0cf2168d181d31296264a068d92eecbf0bc9` | test: re-pin `C1388_SourceTreeChangeRefusesResume` as `C1388_SourceTreeChangeLandsWithResidue` (S1, red before the fix) |
| `6559f74007d31a52c4435b6720ee46e291e6bb89` | fix: protocol + docs + C688 comment (S2) |
| (this report commit) | `.antiphon/task-8a0bbf02.md` only |

## What changed (`server/Application/Services/AgentTaskLandingProtocol.cs`)

- `ValidateTicketAsync`: the land worktree keeps its refusals (index lock codes, `verification_sequencer_changed`,
  `verification_tracked_tree_changed`, `land_worktree_changed`). The task worktree, when `ticket.SourceCanonical`
  is non-null, is probed by `ObserveSourceResidueAsync` with the same three commands in the same order and argv
  (the CARD-1449 C688 inventory), read-only, and a finding (`git_index_lock_held`/`_stale`, `active_sequencer`,
  `source_dirty`, `status_error`) is logged and never refuses; guarded cleanup records the durable
  `Refused`/reason. Every `LandingGit` child already runs with `GIT_OPTIONAL_LOCKS=0`
  (`LandingGit.ExecuteProcessAsync`), which is what `--no-optional-locks` sets, so the argv was not changed and the
  C688 inventory test stays byte-identical (CP-6 green). The CARD-1449 caveat was about the argv, not the effect.
- `RunCoreAsync`: a ticketed re-entry judges the owner row against the ticket's operation first
  (`RequireTaskIdentityAsync`, the DB half factored out of `RecheckSourceIdentityAndPinsAsync`), before the lease
  check and the pending-operation identity check, so a task coordinate, active-operation pointer or status moved
  during the window refuses as `task_coordinates_changed` (the pre-CARD-1379 reason). When the owner no longer
  points at the ticket's operation, that operation is loaded by the ticket's `OperationId` so the refusal is
  recorded on it (the `active_operation` guard case had `LastReason` null before).
- `docs/orchestration-loop.md`: CARD-1379 post-window paragraph extended with the CARD-1463 rule.
- `tests/.../AgentTaskLandPublicationTests.cs`: comment only on `C688_LandNeverRunsGitInTheSourceWorktree`.

## Test that contradicted the decision (re-pinned, not left)

`LandVerificationLeaseWindowTests.C1388_SourceTreeChangeRefusesResume` (CARD-1388 witness commit `024c3533f`,
master only) asserted that tracked dirt in the task worktree during the window refuses with
`verification_tracked_tree_changed`: the exact behaviour option (b) removes, so it could not be reconciled and was
re-pinned (D-12: renamed, not dropped) as `C1388_SourceTreeChangeLandsWithResidue` (publishes, remote advances,
source bytes kept, cleanup `Refused`/`source_dirty`). The four sibling C1388 land-worktree refusals and C1379 are
unchanged and green (CP-4). Review should confirm this re-pin is within the operator's decision.

## Red witness (CP-1, own run at 5d8e0cf21, documented non-receipt row)

CHECKPOINT CP-1 commit=5d8e0cf2168d181d31296264a068d92eecbf0bc9 build=ok filter=/*/*/(AgentTaskLandSourceFreshnessTests*)|(AgentTaskLandBoundaryControlledTests*)|(LandingProtocolGuardTests*)|(LandVerificationLeaseWindowTests*)/(C488_DirtySourceFfRefuses*)|(C488_SourceSequencerFfRefuses*)|(C448_V10_VerificationCannotFreezeOldTaskCoordinates*)|(C475_Task_*)|(C1388_SourceTreeChangeLandsWithResidue*) executed=14 passed=1 failed=13 skipped=0 trx=/work/worktrees/task-8a0bbf02/.antiphon/checkpoints/20261022-083431-6daf/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=5d8e0cf2168d181d31296264a068d92eecbf0bc9 sourceState=clean buildSource=verified

Red reasons: C488_DirtySourceFfRefuses `verification_tracked_tree_changed`; C488_SourceSequencerFfRefuses
`verification_sequencer_changed`; C1388 re-pin `verification_tracked_tree_changed`;
C448_V10_VerificationCannotFreezeOldTaskCoordinates source-ref/source-path/target
`pending_operation_coordinates_changed`, repository `repository_lease_required`; C475_Task_source_ref/path/target_ref
`pending_operation_coordinates_changed`, C475_Task_repository `repository_lease_required`, C475_Task_task_status
`verification_ticket_changed`, C475_Task_active_operation `LastReason` null. C475_Task_verification_filter passed.
The six red `LandingProtocolGuardTests.C475_Task_*` cases are inherited reds of the same mechanism, beyond the
card's 21 (same harness, same barrier); the fix covers them.

## Green run (one run, one build, at 6559f7400; receipt validated)

CHECKPOINT CP-2 commit=6559f74007d31a52c4435b6720ee46e291e6bb89 build=ok filter=/*/*/AgentTaskLandBoundaryControlledTests/(C448_V10_EachAcknowledgedBoundaryRechecksSource*)|(C448_V10_VerificationCannotFreezeOldTaskCoordinates*) executed=94 passed=94 failed=0 skipped=0 trx=/work/worktrees/task-8a0bbf02/.antiphon/checkpoints/20261022-084403-dd62/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=6559f74007d31a52c4435b6720ee46e291e6bb89 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=6559f74007d31a52c4435b6720ee46e291e6bb89 build=reused filter=/*/*/AgentTaskLandSourceFreshnessTests/(C488_DirtySourceFfRefuses*)|(C488_SourceIdentityFfRefuses*)|(C488_SourceSequencerFfRefuses*)|(C488_PostFfDirtyRefuses*)|(C488_PostFfIdentityRefuses*)|(C488_RebaseCannotAdoptLaterHead*)|(C488_TargetCheckpointStillGuarded*)|(C488_FastForwardCannotAdoptLaterHead*)|(C488_LocalIdentityBoundaryMatrix*) executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-8a0bbf02/.antiphon/checkpoints/20261022-084403-dd62/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=6559f74007d31a52c4435b6720ee46e291e6bb89 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=6559f74007d31a52c4435b6720ee46e291e6bb89 build=reused filter=/*/*/LandVerificationLeaseWindowTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-8a0bbf02/.antiphon/checkpoints/20261022-084403-dd62/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=6559f74007d31a52c4435b6720ee46e291e6bb89 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=6559f74007d31a52c4435b6720ee46e291e6bb89 build=reused filter=/*/*/LandingProtocolGuardTests/(C475_Task_*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-8a0bbf02/.antiphon/checkpoints/20261022-084403-dd62/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=6559f74007d31a52c4435b6720ee46e291e6bb89 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=6559f74007d31a52c4435b6720ee46e291e6bb89 build=reused filter=/*/*/(AgentTaskLandPublicationTests*)|(AgentTaskLandRefusedRetryTests*)/(C688_LandNeverRunsGitInTheSourceWorktree*)|(C688_DirtyCanonicalCheckoutLandsWithResidue*)|(C688_TargetCheckedOutElsewhereIsResidueNotRefusal*)|(C688_RealLandProfile*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-8a0bbf02/.antiphon/checkpoints/20261022-084403-dd62/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=6559f74007d31a52c4435b6720ee46e291e6bb89 sourceState=clean buildSource=verified

Run `20261022-084403-dd62`, verdict GREEN, `unlisted: none`, outputs `bin-c1463/` deleted by the tool.
`scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261022-084403-dd62/report.json
-ExpectedSourceSha 6559f7400...`: `CHECKPOINT SOURCE VALID source=6559f74007d31a52c4435b6720ee46e291e6bb89 rows=5`,
exit 0. Fresh TRX inspected: CP-2 holds all 12 formerly red cells (dirty/staged/untracked x
Prepared/RebaseStarted/remote/BeforeRebaseIntent) and the 4 CannotFreeze cases, all Passed; CP-4 holds the
re-pinned case plus the four land-worktree refusals and C1379; CP-6 holds the C688 inventory, the two canonical
residue cases and the profile budget.

Invariants: V-1 CP-2/3/4 passed; V-2 CP-3 passed; V-3 CP-2/5 passed; R-1 CP-4 passed; R-2 CP-6 passed. No
manual acceptance. Unit lane not run: the change is confined to the land protocol; no unit class references
`ValidateTicketAsync`/`RunCoreAsync` directly (the plan states this scope).

Unlisted runs, with reasons: (1) `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1463-tool/`
through `build-slot.ps1` (label c1463-tool, held 9 s): the tool bootstrap. (2) `dotnet build server
--property:OutputPath=bin-c1463-compile/` through `build-slot.ps1` (label c1463-compile, held 95 s): a compile check
of the fix before committing it, 0 errors; output deleted before the green run. No other build or test driver ran.

Repeat proof: none (no flake; one normal run per selection).

## Policy and landability

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef bb43ec352... -HeadRef 6559f7400...`: commits=3
  entries=0 violations=0, exit 0 (re-run after the report commit is the Review's to repeat; the report is Markdown
  under `.antiphon/`, outside any checkpoints directory).
- `git merge-tree --write-tree origin/master HEAD` at origin/master `a38e1e1bd0f515d6ae7a6682f537a5fe5e4cdd49`
  (bb43ec352 has moved on): clean, exit 0. Not merged.

## RC port (`origin/rc/2026-10-20` = `ceb6e3d6b78471db6831258699c493b8eb2d619b`)

Simulated with `git merge-tree --write-tree --merge-base <c>^ <rc> <c>`:

- `6559f7400` (fix) alone onto rc: the `AgentTaskLandingProtocol.cs` and `docs/orchestration-loop.md` hunks apply
  cleanly (0 conflict markers); only the `AgentTaskLandPublicationTests.cs` hunk conflicts, and it is a comment on the
  CARD-1449 re-budgeted C688 test, which rc does not have (`6ee09ad07` is not on rc). Port: pick `6559f7400` and
  drop that test-file hunk.
- `5d8e0cf21` (test re-pin) does not apply to rc: rc carries none of the CARD-1388 `C1388_*` tests
  (`024c3533f`/`173050670` are master only), so there is nothing to re-pin there; skip it.
- `1802b56ee` (plan): rc carries no `docs/superpowers/plans`; drop it per the rc convention.
- Dependencies: the protocol/docs hunks do not depend on the CARD-1427/1428/1432 chain or on `6ee09ad07`; rc's
  `ValidateTicketAsync` has the same shape at the edited lines (rc `CaptureTicketAsync` returns a single ticket,
  which the hunk does not touch). rc's pre-CARD-1449 `C688_LandNeverRunsGitInTheSourceWorktree` (two-command
  inventory) stays red on rc after the port, exactly as it is red on rc today for the CARD-1379 reason; it goes
  green only with `6ee09ad07`'s re-budget. The 21 behavioural tests and the six C475 guard cases are expected green on
  rc after the port (same files on both branches per the card).
- The orchestrator pushes; nothing was pushed to rc here.

## Pending for method-scoped SourceLanding Mutation (none discharged here)

PC-1 remove the ticketed `RequireTaskIdentityAsync` pre-check ->
`/*/*/AgentTaskLandBoundaryControlledTests/C448_V10_VerificationCannotFreezeOldTaskCoordinates` (repository) red.
PC-2 source `status` finding back to `Require` -> `/*/*/AgentTaskLandSourceFreshnessTests/C488_DirtySourceFfRefuses`.
PC-3 source sequencer finding back to `Require` -> `/*/*/AgentTaskLandSourceFreshnessTests/C488_SourceSequencerFfRefuses`.
PC-4 drop the source probes -> `/*/*/AgentTaskLandPublicationTests/C688_LandNeverRunsGitInTheSourceWorktree`.
PC-5 drop the land-worktree status refusal -> `/*/*/LandVerificationLeaseWindowTests/C1388_TrackedTreeChangeRefusesResume`.

## Housekeeping

`bin-c1463-red/` (kept by the red run), `bin-c1463-compile/` and `tools/Antiphon.Checkpoints/bin-c1463-tool/` deleted;
`bin-c1463/` deleted by the tool. Checkpoint evidence stays ignored under `.antiphon/checkpoints/`.
