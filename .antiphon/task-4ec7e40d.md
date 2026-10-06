# CARD-1076 S1 Final Review (task 4ec7e40d)

Outcome: clean. No regression of existing behaviour and no new reachable fail-open in S1 at `0a998a5c728093e5cdcb5ff4976dc03820623706`. Three disclosures for Backlog cards; none blocks landing.

subjectTaskId `c7d2a835-8c77-4374-b0e9-91ab185b0caf` (Code owner). Reviewed ref `refs/heads/feat/card-task-c7d2a835` = `0a998a5c728093e5cdcb5ff4976dc03820623706` (tested source `c4eee157b38f9a5ee07ae621b860e1613bae07ab`; the tip adds only `.antiphon/task-c7d2a835.md`), base master `1d3508b9cfb31240b11ac101e788c6893804dd5e`. The review branch `feat/card-task-4ec7e40d` started at the same SHA; this report is its only commit. Plan `docs/superpowers/plans/2026-10-06-card-1076-remote-prep-push-fence-plan.md`.

## Reruns at the reviewed SHA (Linux runner mirror, one build)

Checkpoint tool run `20261006-070947-7596` through `scripts/build-slot.ps1` (outer lease `e184b900-9729-4bda-aa89-5a3787f3b579` waited=0s held=325s maxcpucount=6; the tool's implicit bootstrap build is the one explained unlisted build). Arguments `run --plan <plan> --rows CP-1,CP-2 --expected-source-sha 0a998a5c7… --row-timeout 15m --total-timeout 40m --keep-outputs`. Verdict GREEN exit=0, wall 5m12s, `unlisted: none (the tool ran no other build or test command)`.

```
CHECKPOINT CP-1 commit=0a998a5c728093e5cdcb5ff4976dc03820623706 build=ok filter=/*/*/(RepositoryMutationLeaseTests*)|(RepositoryMutationLeaseDescribeTests*)|(RepositoryChildJournalInspectorTests*)|(RepositoryFenceObserverTests*)/* executed=29 passed=29 failed=0 skipped=2 trx=/work/worktrees/task-4ec7e40d/.antiphon/checkpoints/20261006-070947-7596/rows/CP-1/run.trx slot=granted waited=45s dirty=0 source=0a998a5c728093e5cdcb5ff4976dc03820623706 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=105s build=121.2740377s startup=41.6571288s testsWall=16.5011702s teardown=1.1622665s hostWall=59.3205739s
CHECKPOINT CP-2 commit=0a998a5c728093e5cdcb5ff4976dc03820623706 build=reused filter=/*/*/LandingGitTests/* executed=57 passed=57 failed=0 skipped=0 trx=/work/worktrees/task-4ec7e40d/.antiphon/checkpoints/20261006-070947-7596/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=0a998a5c728093e5cdcb5ff4976dc03820623706 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=0s startup=3.1298305s testsWall=20.269075s teardown=0.6154321s hostWall=24.0143373s
```

CP-1 TRX counters: total=31 executed=29 passed=29 failed=0 notExecuted=2. The two NotExecuted rows are `C448_V13_WindowsJunctionAndOtherProcessShareTheLease` and `C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput`. Class split: `RepositoryMutationLeaseTests` 22 (18 existing + 4 V-3 arms alive/exited/reused/unknown, all Passed), `RepositoryChildJournalInspectorTests` 7 (6 + V-4), `RepositoryMutationLeaseDescribeTests` 1, `RepositoryFenceObserverTests` 1. CP-2: total=57 executed=57 passed=57; V-1 `C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal` 2.42 s, V-2 `C1076_TaggedPushJournalNamesTaskAndPurposeWhileAlive` 8.08 s.

Additional runs, each with a stated reason (the brief's "existing landing/journal/lease classes the diff touches and the registry guard"; none is a checkpoint row). Each was `dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1076-a/ --property:UseAppHost=false -- --treenode-filter … --report-trx` against the kept CP-1 build, via `build-slot.ps1`, so no second build:

| Run | Filter | Result | Lease |
|---|---|---|---|
| registry guard + lease owner class (CP-1's `RepositoryMutationLeaseTests*` wildcard does not match `RepositoryMutationLeaseOwnerTests`) | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)\|(RepositoryMutationLeaseOwnerTests*)/*` | total 11, passed 11, failed 0, skipped 0 (guard 3: `Registry_matches_compiled_metadata`, `unlisted_slow_test_is_a_hit_and_listed_fast_or_allowed_slow_is_not`, `allowlist_file_exists_and_has_spawn_lane_entries`; owner 8) | `68476a23-e64f-4848-a802-9b8ab1dbd466` waited=0s held=49s |
| recovery-script class (the diff edits `scripts/recover-repository-children.ps1`) | `/*/*/RepositoryChildRecoveryTests/*` | total 14, passed 14, failed 0, skipped 0 (includes the `alive` arm) | `a109cf07-2ef7-4d41-8983-e68d505728ee` waited=0s held=61s |

Manual script check (no automated test covers the new line): scratch repository, three planted records against one sleeper pid. Output `retained (reused): …/a-tagged.json purpose=remote-prep-push task=66086125-1a04-425d-b92e-85599371dc13`; the untagged record and a legacy record without the two properties printed their lines unchanged; exit 3. The `reused` label is my harness's start-tick mismatch, not the script: the recovery class's `alive` arm passed in the same session on the same build.

Evidence guard: `scripts/check-evidence-diff.ps1 -BaseRef 1d3508b9c… -HeadRef 0a998a5c7…` printed `commits=3 entries=1 violations=0`, exit 0 (the one entry is the Code report Markdown).

Cleanup: all 28 `bin-c1076-*` directories (27 `bin-c1076-a` plus the tool's `bin-c1076-tool`) deleted from the worktree root; `git status` clean; the scratch repository removed.

## Brief checks

**(a) Fence relaxation.** Three production readers consult the journal: `RepositoryMutationLease.AcquireAsync` and `GuardedVerificationRemoval.AuthorityAsync` through `HasUnfinishedAsync`, and `DescribeUnavailableAsync` through `FirstFencingAsync`; all three share one traversal. The only record skipped is schema 1, pid and start ticks present, common directory equal, `IsProcessAliveAsync == true`, and `Purpose` in the reader-side set `{ "remote-prep-push" }`. Dead (`false`), reused (`false` by ticks), unknown (`null`), `Completed` (no start ticks, so the pattern does not match), torn, malformed, non-json, unreadable (catch returns `FencingRecord.Untagged`), wrong common directory and live untagged records all fence exactly as before; R-2 `C448_C24_StandingJournalFencesAdmissionByStartIdentity(alive)` and the four V-3 arms passed. Crash visibility is kept: the record is deleted only by `Exited` after both streams drain, a server death leaves it, and the recovery script retains an alive record and now names its tag.

What a live push can touch while another lease holder runs: the upload itself (remote side), the local `refs/remotes/origin/<task-branch>` update (ref locks with git's `core.filesRefLockTimeout` retry; a concurrent land fetch of the same ref converges on the same value), and with `-u` the `branch.<name>.remote/merge` keys in the common `.git/config`. Git does not retry `config.lock`; a collision fails immediately with `could not lock config file`. Lease holders: the land (`worktree add --detach`, fetch, rebase, push of master, `gc --auto` behind fetch, which packs refs under `packed-refs.lock` with retry and prunes only unreachable objects while the pushed branch is a ref), the dispatcher's worktree cut (`WorktreeManager.cs:113` `worktree add -b <branch> <path> <base>`; git writes tracking config only when the base is a remote-tracking ref, and the default base is the local `master` from `MergeTargetRef`/`GitSettings.DefaultBranch`, while the delegation path at `DelegationWorktreeService.cs:321` uses a verified SHA), and verification removal (`worktree remove` plus `update-ref -d` of a landed task's branch, never the Queued task being prepared). So the only cross-write is `.git/config`, only when an operator requests a remote-tracking base ref, and only in the milliseconds of the push's upstream write. Worst case: the cut fails and `WorktreeManager.RollbackFailedAddAsync` undoes it for a later tick, or the push's upstream write fails while the push itself still exits 0 (git does not propagate that error) and the desktop upstream, which the runner mirror never reads, stays unset. The same window already existed in the other direction, since a push that starts after a lease was taken was never fenced. Judgement: not a fail-open; disclosure 1.

**(b) Skip floor.** At the base commit the lease test file throws `SkipTestException` off Windows at lines 20 (`C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput`) and 500 (`C448_V13_WindowsJunctionAndOtherProcessShareTheLease`); HEAD keeps them at lines 21 and 501, and the file's other two `IsWindows()` sites (132, 384) only gate `File.SetUnixFileMode`. My CP-1 TRX NotExecuted rows are exactly those two. 29 executed = 31 total − 2, and 31 = the plan's 18 + 4 + 1 + 6 + 1 + 1, so no newly skipped test hides behind the floor. The plan edit in `c4eee157b` touched only the CP-1 row and one risk note.

**(c) Budgets and defaults.** `RunAsync(repository, arguments, ct)` and `RunOwnedAsync` pass `new LandingGitRunOptions()`, so `options.Budget ?? DefaultBudget` is 5 minutes; the rebase HEAD follow-up uses default options; the read-only byte batch at `LandingGit.cs:681` keeps its literal 5 minutes (`grep FromMinutes` shows only that literal and `DefaultBudget`). V-1 pins `DefaultBudget == 5 min`. No production caller passes options yet (S2). `LandingGit` is the only production `ILandingGit` (`Program.cs:421` singleton, no decorator), so the default interface method cannot silently drop options on the production path. One shape change: untagged records now serialize `"Purpose":null,"TaskId":null` under schema 1; the PowerShell reader (`PSObject.Properties`), the System.Text.Json inspector and the recovery script accept both shapes (legacy record checked above). `JournalRecordFinding` gains two trailing optional fields; its consumers (`RunnerAlarmCoordinator`, `ExpectationObservationAdapter`) are unaffected. `DispatchHoldDetails.ClassOf` anchors on the `Held: repository mutation lease` prefix, so the tagged fence sentence still classes as `Lease`.

## Code report against the plan

CP-1 and CP-2 lines match the `### Checkpoints` table at HEAD (CP-1 Min 29 linux / 31 windows, CP-2 57) with nonzero counts and executed identity `source=c4eee157b… sourceState=clean buildSource=verified`; the tool's bootstrap build is explained; the registry guard is an unlisted run with the brief's requirement as its reason; the earlier pre-runs are disclosed and superseded; every driver carried a lease id. R-2, R-3 and R-8 ran inside the rows; V-5..V-15 and the later R rows are correctly not marked passed. The four new tests assert outcomes that a mutation flips (admission result and describe text, `git_timeout` inside the bound, record fields and deletion, inspector fields), so none is a stub. No red TRX was captured; PC-1..PC-3 remain the red witnesses for SourceLanding Mutation.

## Disclosures (Backlog cards, not defects)

1. **Config-lock window.** Plan D-3 says the push's ref and config writes sit "under git's own ref/config locks with retry"; `.git/config` has no retry. Reachable only with a remote-tracking `WorktreeBaseRequestedRef`, consequences above. Suggested fix for S2, which edits `PushBranchAsync` anyway: drop `-u` from the prep push (the runner branch contract pushes explicit refspecs and the desktop upstream is unused), or pass `--no-track` on the dispatcher's `worktree add -b`; and correct the plan sentence.
2. **Recovery script tag line untested.** The plan's S1 row describes the ` purpose=<p> task=<id>` suffix but names no test, so only my manual check covers it. The script prints the stored `D`-format guid while `DescribeUnavailableAsync` prints `N`. Cosmetic; a tagged arm in `RepositoryChildRecoveryTests` would pin both.
3. **Tracing seam and the shared reader.** `LandingGit.RunAsync` (four arguments) is virtual, but `FixtureGit` and `ControlledLandingGit` override only the three-argument seam, so a four-argument call bypasses fixture tracing; harmless in S1, relevant to S2's fakes (the plan already calls the override optional). `GuardedVerificationRemoval` is relaxed by the same reader change; safe because it removes only a landed task's verification worktree and branch.

## Not run or pending

- Whole-Unit lane: caller-owned per the brief.
- CP-3..CP-7, V-5..V-15, R-1, R-4..R-7, R-9..R-12: S2–S5, not started.
- `DispatchHoldVisibilityTests` and `RunnerAlarmAttentionTests` pin fence or recovery text at the Application layer; outside the brief's class selection, and the untagged sentence is byte-identical.
- PC-1..PC-3 pending for method-scoped SourceLanding Mutation; PC-4..PC-12 belong to later slices.
- Producer/recipient, queue and UserPrompt transcript audit: not applicable; library change with no session delivery and no activation (`restart: none`).

--- review evidence ---
subjectTaskId: c7d2a835-8c77-4374-b0e9-91ab185b0caf
reviewedSourceSha: 0a998a5c728093e5cdcb5ff4976dc03820623706
reviewedSourceClean: true
ordinaryScopeCompleted: Full
