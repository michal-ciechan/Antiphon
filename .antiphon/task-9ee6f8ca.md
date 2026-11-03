# CARD-0824 S2 Code report (task 9ee6f8ca)

Outcome: S2 is implemented and ships OFF. CP-12, CP-15, CP-16, CP-19, CP-20, CP-21 and CP-22 are green at
`8affc7e646e304c5e9e4468d7df2d74b38cb2731` (branch `feat/card-task-9ee6f8ca`, runner mirror
`/work/worktrees/task-9ee6f8ca`, landing owner: this Code task 9ee6f8ca). Not done: CP-17 (V-43, the recycle audit)
and CP-18 (V-44, phone-home delivery) are not written, and the whole Unit lane was not run.

## Commits (base c2f1f83a30b324af81149918e8a6360565b95344)
- 18792d4434 stubs: settings, landing fields, migration `20261103210620_Card0824BranchDeletion`, refusing seams; carries the S2 TD (e59ca744c) byte-for-byte
- 0e0a11a8d4 tests (red against the stubs)
- a7197a742e checkpoint amendment `docs/superpowers/plans/2026-11-03-card-0824-S2-code-checkpoints.md`
- 8affc7e646 implementation, wiring, docs, test fixes
(Full SHAs: `git log c2f1f83a3..8affc7e64`.)

## Checkpoints (closed list: the amendment table)
RED run `20261103-212525-cc41` at a7197a742 (`--rows CP-12,CP-16`, verdict RED exit=1):
- CP-12: executed=42, passed=4, failed=38. The 4 that pass on the stub are safe by construction: V-15 target-hook-reject, V-15 unconfirmed-cut, V-19 flag-off, V-17 terminal-dependent control.
- CP-16: executed=11, passed=0, failed=11.
- Three of the reds were test bugs, fixed in 8affc7e64:
  - divergent-remote: no landing op is ever created;
  - dry-run: the runner was not allow-listed;
  - a Deleted-list ordering assertion.

GREEN run `20261103-215529-9d29` at 8affc7e64: all seven rows. Verdict GREEN exit=0, wall 20m25s, 2 builds, unlisted: none.
```
CHECKPOINT CP-12 ... executed=43 passed=43 failed=0 skipped=0 ... slot=granted waited=90s ... sourceState=clean buildSource=verified
CHECKPOINT CP-15 ... executed=32 passed=32 failed=0 skipped=0 ... slot=granted waited=0s ... buildSource=verified
CHECKPOINT CP-19 ... executed=63 passed=63 failed=0 skipped=1 ... slot=granted waited=0s ... buildSource=verified
CHECKPOINT CP-21 ... executed=1 passed=1 failed=0 skipped=0 ... slot=granted waited=0s
CHECKPOINT CP-22 ... executed=2 passed=2 failed=0 skipped=0 ... slot=granted waited=0s ... buildSource=verified
CHECKPOINT CP-16 ... executed=11 passed=11 failed=0 skipped=0 ... slot=granted waited=0s ... buildSource=verified
CHECKPOINT CP-20 ... executed=20 passed=20 failed=0 skipped=0 ... slot=granted waited=0s ... buildSource=verified
```
`validate --rows CP-12,CP-15,CP-16,CP-20,CP-21,CP-22` gives `CHECKPOINT SOURCE VALID source=8affc7e64 rows=6`.

A whole-run validate gives `row_failed`, because of CP-19's one skip. That skip is
`C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(locked, False)`. It calls `Skip.Test` off Windows
("Windows sharing denial is required"), so it skips on Linux by design and has nothing to do with S2.

`scripts/check-evidence-diff.ps1 -BaseRef c2f1f83a3 -HeadRef HEAD` gives: commits=4, violations=0.
`git merge-tree --write-tree origin/master HEAD` is clean against master 97e77f9f1.

Unlisted runs, all through build-slot:
- one `dotnet ef migrations add`;
- native compile builds of Antiphon.Tests and SessionRunner.Tests;
- native iteration runs of the CP-12, CP-21/22 and CP-16/20 classes. These were dev-loop checks before the cold checkpoint builds; they are not evidence.

## Not run / deferred to the next Code round (never marked passed)
- **CP-17 / V-43** `C0824BranchDeletionRecycleAuditTests`: linux-native, uses `C1008HostFixture` and the real recycle audit. Not written.
- **CP-18 / V-44** `C0824BranchRefsDeliveryTests`: linux-native, real phone-home socket, built on the private `PhoneHomeMirrorJoinTests.JoinWorld`. Not written. The TD asks for a `C1032_JoinIdleRecipient` base check first; it was not run.
  - The transport half is covered only by CP-21: the dispatcher handles every operation, including the new op 48.
- **Whole Unit lane** `[Category=Unit]` (Final profile): not run. It takes about 70 min wall on server2 with 3 lanes.
  - Surfaces that could move a Unit pin: the new `AgentTaskEventType.LandingBranchDeletion = 41`, `PhoneHomeOperation.WorkspaceBranchRefsDelete = 48`, `workspaceBranchRefsV1` now in every runner's `Capabilities()` (not in `StaticFeatures`), and the new `LandingSettings` members.
- Native Windows CP-12/CP-15 (enable gate E-2): not run. Nothing runs on the desktop.

## Seams and defaults (TD Q-1..Q-8 defaults, amendment A-D-n untouched)
- Q-1: the lease is on `SourceRemoteSha`, which equals `ReviewedSourceSha` and `OriginalSourceSha` (B-1).
- Q-2: an open dependent gives `Deferred dependent_open`, re-evaluated every sweep.
- Q-3: a null `SourceBranchDeletionAfterUtc` makes nothing eligible; the bound is 25 actions per run.
- Q-4: recovery and adoption lands are `Kept recovery_land`. There is no revert detection.
- Q-5: runner volumes are enabled through `SourceBranchDeletionRunnerIds`.
- Q-6: Hangfire job `antiphon:published-branch-deletion`, every 15 minutes. It is registered only while the flag is on at startup.
- Q-7: runner refs are deleted before origin.
- Q-8: a residue-confirmed land is still eligible.

Code additions, each from the brief's safety list:
1. **Fresh containment at deletion time.** `ILandingGit.ConfirmTargetContainsAsync` runs `ls-remote`, then a fetch that writes no ref and no FETCH_HEAD, then `merge-base --is-ancestor`. It never reads a cached state. If origin's target no longer contains `VerifiedSourceSha`, the branch is `Kept publication_not_contained` (V-49).
2. **Card not Done.** A card on the branch whose status is not `Done` defers the deletion (`card_not_done`). The brief says "card branch binding that is not Done".
3. **Owner reopened.** If the owner task is open again, the deletion defers (`owner_open`).
4. **DryRun** writes nothing: no runner call, no push, no row write.
5. **Kill switch.** `IOptionsMonitor` re-reads the flag before every row.
6. **Repository lease** per deletion; if it is busy, the result is `Deferred repository_busy`.
7. **Runner set.** It covers every runner of the owner and of the other tasks on the branch. `RunnerId` null or `local` counts as the desktop.
8. **Capability.** `workspaceBranchRefsV1` is advertised by every runner, because the git work is portable. The dispatcher runs the op under the mutation lock.
9. **Op number.** 48 is the next free value. S4b must take 49 or later.

Seams:
- `IRunnerBranchRefs` (production: `PhoneHomeRunnerBranchRefs`);
- `ILandingGit.DeleteSourceBranchAsync`, `ConfirmTargetContainsAsync` and `DeleteTrackingRefAsync`. Their default interface members fail closed;
- `RunnerWorkspaceService.BeforeBranchRefDelete`;
- `LandingGitFixture` and `LandingSafetyHarness` take an optional `sourceBranch`. The default keeps the historical full-id shape, which the delegate-name guard refuses.

## Mutation (all pending; Mutation owns them)
- **PC-1..PC-30:** pending, as mapped in the TD. PC-5 is the single `DelegateBranchName.TryParse` guard, on the server and on the runner.
- **New guards for Mutation:**
  - G-31 fresh containment: `C0824_target_rewritten_after_land_keeps_branch`;
  - G-32 card not Done: `C0824_dependent_rows(owner-card-not-done)`;
  - G-34 DryRun: `C0824_dry_run_never_deletes`.
- **Guards with no witness yet (missing controls):**
  - G-33 `owner_open`;
  - G-35 the kill switch flipped mid-run;
  - the `repository_busy` deferral;
  - the multi-runner set (a second runner on the same branch).
- No deliberate mutants were run in this task (the profile says every PC stays pending). Behavioural red evidence is the stub run above.
