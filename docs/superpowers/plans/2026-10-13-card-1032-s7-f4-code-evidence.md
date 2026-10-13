# CARD-1032 S7 F4 negative-witness Code evidence

All five required negative witnesses were added. Each was green on unchanged
production and turned red under its scratch-probe mutation, which was then
reverted and never committed. The full 14-row F4 selection is GREEN with 232
executions at afafe9c6604d4578db7b1ce2ef4ad3f189209a5d. Production is unchanged
and inert. Next: Review. Restart: none.

Code task: 55328335-fca8-474e-a307-7a5fc84b37ff; landing owner Code
60025819-39c5-49e6-8951-817cdeeff5fc. Branch feat/card-task-55328335, worktree
/work/worktrees/task-55328335. Base 1383709fc74516e4b79570d3bd880ed9bea23aaf
(full task base 63b2e70479c846cacd01af6b20190e845f8258e4). Commits were added
without rebase: 2497d511786343470dd2b03f6938b3160f043d27 (tests first),
afafe9c6604d4578db7b1ce2ef4ad3f189209a5d (selection/PCs, tested SHA). This
evidence commit changes Markdown only.
Selection: docs/superpowers/plans/2026-10-13-card-1032-s7-f4-verification.md.
Review finding: /work/evidence/task-75a50129/review.md (F4). Probe:
/work/worktrees/task-045e9719/docs/superpowers/mutations/2026-10-13-card-1032-s7-viability-probe.md.

## Witnesses and scratch red

Changed files: tests/Antiphon.Tests/Application/CardWorktreeMirrorCleanupTests.cs
(four witnesses), tests/Antiphon.Tests/TestHelpers/CompletedCardMirrorFixture.cs
(removal-request counter and a refusal-only interceptor that asserts it never
returns Complete), tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceContractTests.cs
(signature witness), plus the plan, test design, mutation candidate and this selection.
No existing test body, assertion or timeout changed. No production file changed.

Scratch mutations were the probe's exact patches, run on the dirty tree at
2497d511 through scripts/build-slot.ps1 (slot granted, waited=0s), then
reverted with git checkout. Diffs and logs: /work/evidence/task-55328335/.

| Family | Mutation | Witness | Scratch red |
|---|---|---|---|
| VP-08 | Mac normalizes Binding.TaskId to Guid.Empty | C1032_LandingSignatureCoversTaskId | 1/1 failed: `Verify(changed, key)` should be False but was True, TaskId 55555555-... |
| VP-09 | PublicationAsync `o.TaskId == task.Id && o.Active` -> `o.Active` | C1032_WitnessLandingOwnership | failed: `result.Complete` should be False but was True, vector foreign-task |
| VP-10 | drop `HasPublication(op)` | C1032_WitnessLandingCompleteness | failed: `result.Complete` should be False but was True, vector observedRemoteTargetSha:null |
| VP-13 | ReadCommand drops RequestDigest comparison | C1032_WitnessPersistedReplayTamper | failed: reason should be immutable_request_unreadable but was receipt_mismatch, vector reportDigest (tampered request reached the recipient) |
| VP-15 | unsupported removal reply -> `new(true, null, null)` | C1032_WitnessUnsupportedRemovalAfterInspection | failed: `result.Complete` should be False but was True, vector runtime-unsupported |

Batches: A = VP-09 + VP-13 (different methods; the replay witness never reaches
PublicationAsync, and the ownership witness never replays); B = VP-10 + VP-15
(different methods; each witness's red is on its own vector before any other
path). VP-08 ran alone on the runner project. Each red names its vector.
Only the first red vector per witness was observed; per-vector red belongs to
Mutation (PC-595..599).

Disclosed setup failure: the first batch-B run failed Completeness in
CompletedCardMirrorFixture.InitializeAsync (`saved.Succeeded` was False) before
any witness assertion. This is the same unreproduced fixture-setup failure the
repair evidence disclosed at 3cbd00d3. AgentReportStore.StoreAsync has a 2 s I/O
budget, and the run's cold Postgres start took about 66 s. It is not counted
as red. The witness was rerun alone under the unchanged batch-B mutation and
failed on its real assertion (row above). No budget, retry or assertion changed.

Pre-mutation sanity on unchanged production at 2497d511: the four server
witnesses passed 4/4 (one leased build and run). The first selection attempt
used exact names in an OR group without wildcards and selected zero tests. It
was rerun with `*` suffixes, and every checkpoint filter uses wildcards.

## Ordinary verification

Checkpoint run 20261013-174707-f33f, one run of all 14 rows, GREEN exit 0.
CP-29, 30, 31 and 34..37 were 1/1 each. CP-13 was 7/7, CP-14 7/7, CP-F2
63/63, CP-RG1 1/1, CP-32 1/1, CP-33 1/1 and CP-F3 145/145, for 232 executions
including deliberate overlap. The TRX census shows one exact method each in
CP-33..37; CP-F2 contains the four server witnesses and CP-F3 the signature
witness. validate-checkpoint-receipt.ps1 exited 0:
`CHECKPOINT SOURCE VALID source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d rows=14`.
Every row had slot=granted, dirty=0, sourceState=clean and buildSource=verified.

V-1, V-2, V-3 and V-6 PASS at the stated scope. V-4 and V-5 PASS at adjacent
scope only. R-1, R-2 and R-3 PASS. Native Windows and S8 work remain deferred
with their owners and are not reported passed. No Unit lane was run because the
brief excludes it. Manual composition inspection found no production diff, no
Program registration, residue caller or route, and no Execute/cutoff change.

Extra leased drivers outside the table, all through build-slot: one sanity
server build and run, two scratch server builds and four runs (including the
setup-failure rerun and the zero-test filter attempt), one runner build and run,
and the checkpoint-tool bootstrap. No proof selection on unchanged source exceeded three
normal executions. Outputs from those drivers (bin-c55328335-srv,
bin-c55328335-runner from scratch, bin-c55328335-tool) were deleted. The
checkpoint tool deleted its own outputs.

## Pending

Post-land SourceLanding Mutation was NOT run. PC-595..599 (F4) and every earlier
PC/variant (PC-52..PC-594 as censused in the repair evidence) remain pending.

## Closing checks

Fetched origin/master b5653d5832aac66c5ac1ff1a0012efb33b54de79; `git merge-tree
--write-tree origin/master HEAD` exit 0 at the tested code tip (tree
3b61f2f04bbf45b0163a7429aa3aaae3578ec1c3). The final documentation tip is
checked again in the caller report.

Unedited checkpoint lines:

```text
commit: afafe9c6604d4578db7b1ce2ef4ad3f189209a5d  branch: feat/card-task-55328335  worktree: /work/worktrees/task-55328335  host: Debian GNU/Linux 12 (bookworm) cores=24
source: afafe9c6604d4578db7b1ce2ef4ad3f189209a5d state=clean buildSource=verified
CHECKPOINT CP-29 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=ok filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportRecognitionCaseTable* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-29/run.trx slot=granted waited=15s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-30 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAddedAfterProducerInspection* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-30/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-31 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAbsentAllowsCleanup* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorBehindPushedTip)|(C1017_ReopenBetweenEndpoints)|(C1017_MultipleMirrorsRemainDistinct)|(C1032_Admission*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/(C1017_ReceiptMustMatchOperation)|(C1017_ReceiptMustMatchEndpoint)|(C1017_ReceiptMustMatchStore)|(C1017_ReceiptMustMatchTaskAttempt)|(C1017_AckIsNotRemoval)|(C1017_UnsupportedNeverFallsBack)|(C1032_Receipt*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-34 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessLandingOwnership* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessLandingCompleteness* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-35/run.trx slot=granted waited=15s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessPersistedReplayTamper* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-36/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessUnsupportedRemovalAfterInspection* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-F2 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/(CardWorktreeCleanupRecoveryTests*)|(CompletedCardWorktreeRemovalTests*)|(CardWorktreeMirrorCleanupTests*)/* executed=63 passed=63 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-F2/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-RG1 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-RG1/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-32 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalEligiblePhysical* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-32/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-33 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CompletedCardWorkspaceContractTests/C1032_LandingSignatureCoversTaskId* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-F3 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/(CompletedCardWorkspaceRemovalTests*)|(CompletedCardWorkspaceContractTests*)|(RunnerWorkspaceServiceTests*)|(LegacyWorkspaceRemovalGateTests*)/* executed=145 passed=145 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-F3/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 22m17s  sequential-equivalent: 18m59s  builds: 2  max-concurrent-builds: 1  rows: 14 green 0 red 0 skipped
outputs: deleted bin-c55328335-server/, bin-c55328335-runner/
verdict: GREEN exit=0
```
