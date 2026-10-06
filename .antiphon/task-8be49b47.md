# CARD-1076 S2 Final Review report (review task 8be49b47)

Outcome: **clean**. CARD-1076 S2 (D-2 push budget and tag, D-4 per-repository push gate with `Progress`) at `678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4` is correct against the plan; the commissioned selection was rerun once at that SHA in one checkpoint-tool run and is green (163 executed, 0 failed, 2 pre-existing Windows-only skips). One disclosure filed as CARD-1093 (Backlog). No source changed by this review.

- subjectTaskId (Code owner): `c533b8de-cb18-4dda-8b63-9c6b3fa7d9b9`, branch `refs/heads/feat/card-task-c533b8de`
- reviewedSourceSha: `678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4` (worktree clean; `origin/feat/card-task-c533b8de` and `origin/feat/card-task-8be49b47` both at this SHA at review time; master `99ac3b31886c897ca85f61c74288a7be47a47e64`)
- Plan: `docs/superpowers/plans/2026-10-06-card-1076-remote-prep-push-fence-plan.md`
- Evidence guard: `scripts/check-evidence-diff.ps1 -BaseRef 99ac3b318... -HeadRef 678f246fa...` exit 0, commits=1 entries=0 violations=0.

## Verification run (checkpoint tool, run 20261006-081015-5713)

Manifest: five filter rows sharing one build (`tests/Antiphon.Tests -> bin-c1076-rv8be4/`), all `serial: true`, `--expected-source-sha 678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4 --row-timeout 20m --total-timeout 60m`. The plan has no After=S2 row and CP-3..CP-7 expect S3-S5 counts, so the plan table could not be run as-is; the rows below carry the brief's named classes with the brief's recorded counts as floors. `start` ran under `scripts/build-slot.ps1` (lease 505aa8e3, held 9s; the tool bootstrap build is the one explained unlisted build); every build and row shows `slot=granted`. `wait` ran the already-built tool DLL with no build. Host Debian 12, 24 cores. Unedited lines:

commit: 678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4  branch: feat/card-task-8be49b47  worktree: /work/worktrees/task-8be49b47  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4 state=clean buildSource=verified
CHECKPOINT RV-1 commit=678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4 build=ok filter=/*/*/(RemoteWorkspacePreparerTests*)|(DispatcherRemotePrepStarvationTests*)|(PhoneHomeTaskDispatchProjectionTests*)|(RemoteWorktreeMirrorTests*)/* executed=41 passed=41 failed=0 skipped=0 trx=/work/worktrees/task-8be49b47/.antiphon/checkpoints/20261006-081015-5713/rows/RV-1/run.trx slot=granted waited=0s dirty=0 source=678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4 sourceState=clean buildSource=verified
PHASES RV-1 slotWait=0s build=112.8806206s startup=41.6818582s testsWall=16.2252561s teardown=2.5616608s hostWall=60.4687873s
SLOW CLASS Antiphon.Tests.Application.RemoteWorkspacePreparerTests 288s tests=21 (RV-1)
SLOW CLASS Antiphon.Tests.Application.PhoneHomeTaskDispatchProjectionTests 114s tests=8 (RV-1)
SLOW CLASS Antiphon.Tests.Application.DispatcherRemotePrepStarvationTests 63s tests=4 (RV-1)
CHECKPOINT RV-2 commit=678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4 build=reused filter=/*/*/(DelegationLeaseSettingsTests*)|(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-8be49b47/.antiphon/checkpoints/20261006-081015-5713/rows/RV-2/run.trx slot=granted waited=0s dirty=0 source=678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4 sourceState=clean buildSource=verified
PHASES RV-2 slotWait=0s build=0s startup=4.6634053s testsWall=1.8783397s teardown=0.6453027s hostWall=7.1870476s
CHECKPOINT RV-3 commit=678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4 build=reused filter=/*/*/LandingGitTests/* executed=57 passed=57 failed=0 skipped=0 trx=/work/worktrees/task-8be49b47/.antiphon/checkpoints/20261006-081015-5713/rows/RV-3/run.trx slot=granted waited=0s dirty=0 source=678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4 sourceState=clean buildSource=verified
PHASES RV-3 slotWait=0s build=0s startup=2.8986503s testsWall=20.4075919s teardown=0.5977778s hostWall=23.90402s
CHECKPOINT RV-4 commit=678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4 build=reused filter=/*/*/(RepositoryMutationLeaseTests*)|(RepositoryMutationLeaseDescribeTests*)|(RepositoryChildJournalInspectorTests*)|(RepositoryFenceObserverTests*)|(RepositoryMutationLeaseOwnerTests*)/* executed=37 passed=37 failed=0 skipped=2 trx=/work/worktrees/task-8be49b47/.antiphon/checkpoints/20261006-081015-5713/rows/RV-4/run.trx slot=granted waited=0s dirty=0 source=678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4 sourceState=clean buildSource=verified
PHASES RV-4 slotWait=0s build=0s startup=41.8911247s testsWall=19.4462872s teardown=1.2093114s hostWall=62.546723s
CHECKPOINT RV-5 commit=678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4 build=reused filter=/*/*/RepositoryChildRecoveryTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-8be49b47/.antiphon/checkpoints/20261006-081015-5713/rows/RV-5/run.trx slot=granted waited=0s dirty=0 source=678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4 sourceState=clean buildSource=verified
PHASES RV-5 slotWait=0s build=0s startup=3.4299908s testsWall=57.8358657s teardown=0.78656s hostWall=62.0524162s
unlisted: none (the tool ran no other build or test command)
wall: 5m32s  sequential-equivalent: 3m38s  builds: 1  max-concurrent-builds: 1  rows: 5 green 0 red 0 skipped
outputs: deleted review-s2/
verdict: GREEN exit=0

TRX per class (all Passed unless stated): RV-1 RemoteWorkspacePreparerTests 21 (19 existing + `C1076_prep_push_carries_the_task_tag_and_the_configured_budget` + `C1076_pushes_on_one_repository_run_one_at_a_time_and_the_waiter_names_the_pusher`), DispatcherRemotePrepStarvationTests 4, PhoneHomeTaskDispatchProjectionTests 8, RemoteWorktreeMirrorTests 8 (incl. the re-pinned `Push_reports_the_head_it_pushed`). RV-2 DelegationLeaseSettingsTests 11 (8 + the 3 `C1076_RemotePrepPushBudgetMinutes_defaults_to_20_and_rejects_below_1` arms), TestClassificationGuardTests 1, SlowTestTripwireTests 2. RV-3 LandingGitTests 57. RV-4 RepositoryMutationLeaseTests 22 (20 executed; NotExecuted = `C448_V13_WindowsJunctionAndOtherProcessShareTheLease`, `C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput`, the two pre-existing Windows-only skips), RepositoryChildJournalInspectorTests 7, RepositoryMutationLeaseDescribeTests 1, RepositoryFenceObserverTests 1, RepositoryMutationLeaseOwnerTests 8. RV-5 RepositoryChildRecoveryTests 14. Totals match the Code report's recorded counts exactly.

V/R for this round: V-5, V-6, V-13 passed; R-1 (19 existing preparer results), R-3 (55 + V-1, V-2), R-2/R-8 (lease, inspector, describe, fence), R-7 (4), R-10 (8), R-11 (8 existing settings) passed. Not run here, by the brief: the whole-Unit lane (caller-owned). Not runnable yet: V-7..V-12, V-14, V-15, R-4..R-6, R-9, R-12 and CP-3..CP-7 belong to S3-S5, which are not implemented at this SHA. PCs: PC-4, PC-5, PC-11 (and PC-1..PC-3 from S1) stay pending for method-scoped SourceLanding Mutation; nothing here discharges them.

## Code review of the S2 diff (99ac3b318..678f246fa)

(a) Per-repository gate, `RemoteWorkspacePreparer.cs:69-74, 158-187, 269-313`. Keyed by `Path.TrimEndingDirectorySeparator(Path.GetFullPath(RepoPath))` with the OS comparer (same shape as `RepositoryMutationLease.cs:102`); a null/blank RepoPath takes no gate. `SemaphoreSlim(1,1)` wait is on `_stopping.Token`, outside the push budget; the only `Release()` is in the `finally` around `PushBranchAsync`, entered only after `EnterPushTurnAsync` returned holding the turn, so a cancelled or faulted wait never releases a turn it did not take, and a push exception, timeout or cancel always releases. Async waiters are FIFO; the holder's hold is bounded by its push budget (20 min) plus two 5-minute reads, so a waiter cannot starve. A waiting task stays in `_inFlight` (counts for `InFlightCount`, `IsInFlight`, `WhenIdleAsync`), so the dispatcher's `RemoteHoldForAsync` (`AgentTaskDispatcher.cs:1110`) keeps it `HeldForRemotePrep` and `TryBegin`'s `TryAdd` refuses a second launch; shutdown cancels the wait, writes nothing, and the next process re-arms from the row. `TryBegin` never awaits; the wait runs inside `Task.Run`, off the tick. `Release()` runs as the last statement under `gate.Sync` and the waiter's TCS resumes asynchronously, so no lock-order or re-entrancy hazard. `_pushGates` is never pruned (one entry per repository path; bounded by repositories, acceptable).

(b) Dropping `-u`. Repo-wide search for `@{u}`, `@{upstream}`, `%(upstream)`, `set-upstream`, `--track`, `pull`, `--porcelain=v2`, `status -sb`: the only production readers are `GitWorkspaceService.InspectUpstreamAsync`/`UpstreamShaAsync` (`GitWorkspaceService.cs:1012-1030`), used by the Commit-child upstream audit (`AgentTaskService.cs:3723`, `AgentTaskReplyService.cs:4375`) on `task.RepoPath`'s own HEAD branch (the main checkout), never on the task branch in the desktop worktree. The mirror (`MirrorAsync` sends branch + sha + repository), the settlement sync (`SyncCoreAsync` pins `refs/heads/<OwnedBranch>` and fetches explicitly), the land (`AgentTaskLandingProtocol` rebases onto `op.TargetBeforeSha`), and the worktree cut (`WorktreeManager.cs:109-113`, `worktree add [-b]`) read no tracking config. `scripts/checkpoint-task.ps1:192-197` probes `@{u}` and sets it itself when missing. Verdict: safe; no fail-open. Behavioural note for the S5 docs: the desktop worktree's task branch no longer tracks `origin/<task-branch>`, so a human running bare `git push`/`git pull` there is asked for a tracking branch.

(c) Budget. `LandingGit.cs:72` applies `options.Budget ?? DefaultBudget` (5 min, `:16`); the three-argument `RunAsync` (`:32-33`), `RunOwnedAsync` (`:41`), the head verify (`:132`) and the byte batch (`:681`) pass no budget. The only `LandingGitRunOptions` with a budget in `server/` is `RemoteWorkspaceService.PushBranchAsync` (`:114-120`), for the push only; the two reads before it keep the default. Verified.

(d) Settings. `DelegationSettings.RemotePrepPushBudgetMinutes = 20` (`:632`), validator `< 1` failure (`:1197-1198`); registered as `IValidateOptions<DelegationSettings>` with `AddOptions<DelegationSettings>().Bind(...).ValidateOnStart()` (`server/Program.cs:165-168`), so a 0 or negative value stops the host before `RemoteWorkspaceService` reads `settings.Value` (`:58-60`); a null `IOptions` (tests) falls back to `new DelegationSettings()`. V-13 covers 0/1/20. Verified.

Tests. Both new preparer tests run the real preparer, dispatcher tick, isolated schema and phone-home host; each can go red (budget/tag/argument pins; `MaxConcurrentPushes == 1` with a held first push, `waiting.ShouldNotBeNull()` on timeout). `PushOnlyGit`'s three-argument push throws, so the production call site cannot regress to the budget-less overload silently (PC-4 witness). `RemoteWorktreeMirrorTests.cs` was not in the plan's S2 file list but had to change (its `push -u` pin would otherwise fail); the new pin asserts the exact arguments, the 20-minute default and the tag. Not a defect.

## Disclosures (no defect; verdict policy = regression or reachable fail-open only)

1. **CARD-1093 (Backlog, filed by this review).** `Progress().BehindTaskId` is an enqueue-time snapshot: with three tasks on one repository the second waiter keeps naming the finished first pusher during the second push; a newcomer that lands between `Release()` and the woken waiter's `lock` records null. Only the S4 `heldBy`/S5 client text is affected; occupancy and holds are not. Fix sketch and a V-6 three-task arm are on the card. (`BehindTaskId` is also written lock-free at the `Mirroring` transition; a torn `Guid?` read is theoretical and the same fix removes it.)
2. `-u` removal leaves the desktop worktree's task branch untracked (see (b)); worth one sentence in the S5 docs slice.
3. Tooling, out of scope: `scripts/card.ps1 new` on Linux creates the card and then fails in its card-file step (`Join-Path` on a `C:` path, line 523, `board_not_opted_in`); the create itself succeeded.

## Housekeeping

The tool deleted `review-s2/` (`bin-c1076-rv8be4`) on green; the 29 `bin-c1076-*` directories (tool bootstrap included) were removed under the worktree root afterwards; `git status` clean. Checkpoint outputs stay ignored under `.antiphon/checkpoints/20261006-081015-5713/`.

--- review evidence ---
subjectTaskId: c533b8de-cb18-4dda-8b63-9c6b3fa7d9b9
reviewedSourceSha: 678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4
reviewedSourceClean: true
ordinaryScopeCompleted: Full
