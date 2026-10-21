# CARD-1384: real-land worktree-list budget

Plan date: 2026-10-21. Target: **master only**. Stage: Plan. TestDesign is the next stage.
Source inspected and measured at `326c91c7759522751213bf2c1bd24a1a9a1cb66e` (`origin/master`).
No product code is changed by this Plan task.

## Outcome

`C688_RealLandProfile` still fails on current master. A successful schema-3 land now spends ten `git worktree list` processes, and the test fails on the older rule that none of those lists may precede cleanup. The 2026-09-25 ceiling of four is stale. One of the ten lists is an immediate duplicate and should be removed. The other nine are required samples. Raise the test ceilings to that inventory, with the reason in the test. Do not cache mutation-authorizing identity reads to chase the old number.

## Ground truth

Card text assumed a sibling walk (`EvaluateCardSiblingBaseAsync`, CARD-1349) or the land path lists too often, and that CARD-1379 / CARD-1380 might have changed the count since baseline `480407504eae586e18281665f2808c3f77281dd6` (seven lists). That baseline is not an ancestor of this master tip. One narrow run of the existing test was executed here; it was not a checkpoint-tool receipt.

Measured profile, one case, failed:

`C688_PROFILE: Land git profile task=aa9681b9-e29e-4b73-91c8-73ca9e9239e6 request=9543b8d7-e833-445f-a926-de8bbcf4106b outcome=Complete/Landed wallSeconds=4.17 processes=261 worktreeList=10 registrationHits=0 canonicalHits=18 inspections=2 remote=20 gitSeconds=0.91 reset=0.07 rebase=0.12 verify=0.29 push=0.14 canonical=0.06 cleanup=1`

The land completed. The failure is the budget, at `AgentTaskLandRefusedRetryTests.cs:359` (`ShouldNotContain` a `worktree list` before `CleanupStarted`). The `<= 4` assertion on line 360 was not reached. `inspections=2` still meets its ceiling. `processes=261` and `remote=20` exceed 250 and 19, which the same test asserts after the list check.

| Premise | What this master tip does | Consequence |
|---|---|---|
| CARD-1349's sibling walk lists worktrees during this test. | `C688_RealLandProfile` calls `AgentTaskLandService.RunAsync`. `AgentTaskLandSourceResolver` has no worktree list. `EvaluateCardSiblingBaseAsync` is not on this path. | Do not change dispatch. |
| The approved budget is still four lists, all during cleanup. | The comment at `AgentTaskLandRefusedRetryTests.cs:355-360` still says cleanup lists three times and the first Full inspection fills the cache once. `IdentityAsync` (`LandingGit.cs:751`) is `live: true` since CARD-0975. Each Full inspection calls it twice (`LandingGit.cs:390` and `:408`). Cleanup runs two Full inspections (`GuardedWorktreeRemoval.cs:109` and `:128`) plus three uncached `worktree list` commands (`:57`, `:264`, `:284` via `:410-412`). That is 4 + 3 = 7 during cleanup. | Those seven stay. Caching them would contradict `C975_InspectAsyncAlwaysReadsCurrentRegistrations` (four lists, zero registration hits). |
| CARD-1379/1380 may have added lists. | `RepositoryMutationLease.SupportsVerificationWindows` is true, so `AgentTaskLandService.cs:650-652` uses `PrepareAsync`. The fixture source is `feature.txt`, so `LandVerificationPolicy` does not skip. `CaptureTicketAsync` lists live (`AgentTaskLandingProtocol.cs:585`). After the window, `ValidateTicketAsync` calls `CaptureTicketAsync` again (`:602`) and then `LiveRegistrationsAsync` again (`:612`) with no registration mutation between them. Resume also calls `RecheckRemoteSourceAsync` (`:751`), one `ls-remote`. | Three lists precede cleanup. Ten total matches the profile. The third list is the duplicate. The extra `ls-remote` is the twentieth remote. |
| Raising only the list ceiling makes the test pass. | The same method still requires processes `<= 250`, pre-cleanup remote commands `<= 13`, and total remote `<= 19`. Measured processes are 261 and total remote is 20. | The green edit states new ceilings for those counters too. Inspections stay `<= 2`. |

Registration hits were 0 and canonical hits were 18. This plan adds no assertion for either. `C975` already owns the zero-hit identity contract.

## Decisions

- **D-1 — Reuse the post-reacquire listing for the land-worktree proof.** In `ValidateTicketAsync`, the locked-detached-head check must use the registration rows just parsed for the ticket comparison. Do not call `LiveRegistrationsAsync` again in that method. Keep the predicate: one row for the land path, `Branch` null, `Locked`, not `Prunable`, `Head` equal to the candidate SHA. The pre-release `CaptureTicketAsync` list and the post-reacquire `CaptureTicketAsync` list stay live, because the repository lease is released between them.
- **D-2 — Do not cache identity samples.** Reject restoring `live: false` in `IdentityAsync`, dropping either Full inspection, or routing the three cleanup lists through the stamp cache to recreate a budget of four. CARD-0975 D-1 and D-5 require one fresh list per mutation-authorizing identity sample. The three direct cleanup lists are the approved guarded-cleanup reads (initial rows, post-unregister confirmation, branch-delete checkout check).
- **D-3 — State the new ceilings in `C688_RealLandProfile`.** After D-1, a successful run of this fixture is:
  - exactly two `worktree list` commands before `CleanupStarted` (the two lease-boundary samples);
  - `worktreeList <= 9` (those two, plus seven cleanup lists);
  - pre-cleanup `ls-remote` / `fetch` / `push` count `<= 14` (the old 13, plus resume's one source `ls-remote`);
  - `remote <= 20`;
  - `processes <= 260` (measured 261, minus the one removed list);
  - `inspections <= 2`.
  Replace the 2026-09-25 comment with this inventory. Do not add headroom. If the green run misses one of these figures, stop. Do not widen the assertion in that same change.
- **D-4 — Master only.** `origin/rc/2026-10-20` at `5acbd708d640c9d457c272ec4fb3801122e806fe` still has the bound of four and live identity, and does not contain `CaptureTicketAsync`. A cherry-pick of D-3's "exactly two lists before cleanup" would fail there. This card does not backport. An rc repair, if that line still runs this test, is a separate patch for the cleanup seven, not this commit.
- **D-5 — Portable lane.** `GET /api/runner-defaults` revision 2 and `GET /api/session-runners` were read on 2026-10-21. Eligible Linux and Windows lanes were visible. Omit `-Runner`. Omit `-Platform`. Do not record a fleet path.

Rejected: leave all ten lists and only raise the number; treat CARD-1349 as the producer; pad processes above 260; edit `LandingGit` or `GuardedWorktreeRemoval`.

## Slice

One slice. Authoring budget about 40 minutes. Any OS.

| Slice | Files | Change | Witness |
|---|---|---|---|
| S1 | `server/Application/Services/AgentTaskLandingProtocol.cs`; `tests/Antiphon.Tests/Application/AgentTaskLandRefusedRetryTests.cs` | D-1 and D-3 only. No other production edit. | Red is the Plan run below. Green is one run of the same method after the commit. |

Do not edit historical plan or investigation files. Do not add a second test that already passes: the land-worktree predicate remains the existing check, applied to the reused rows. `C975_InspectAsyncAlwaysReadsCurrentRegistrations` is unchanged and is not part of this run.

### Witnesses

Behaviour: the real-land profile matches D-3, which is true only after the D-1 reuse.

Red, already run on this plan at `326c91c7759522751213bf2c1bd24a1a9a1cb66e`, build slot, `UseAppHost=false`, output `bin-c1384/` (deleted after the run):

`/*/*/AgentTaskLandRefusedRetryTests/C688_RealLandProfile*`

1 executed, 1 failed, 0 skipped. Failure: a `worktree list` precedes cleanup. Profile `worktreeList=10`, `processes=261`, `inspections=2`, `remote=20`. Code does not repeat this red run.

Green, after S1 is committed: the same filter, one narrow run, through the checkpoint tool once TestDesign has written the row. Expect 1 passed. No whole class, no whole Unit. Alternate output `bin-c1384/`, then delete those directories. If pre-cleanup remote is not 14, or processes are not within 260, stop and report the profile line.

## Shipment

Commit and push S1 to the assigned branch. Fetch `origin/master` and run `git merge-tree --write-tree origin/master HEAD`. Do not merge or rebase the task branch. Land master only, through the normal land path. No rc activation and no server restart.

### Checkpoints

TestDesign was skipped. This is the one Code run: C688 after S1, plus the four CARD-1379 lease-window witnesses, sharing one build. No Unit lane.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1384/` | real-land-profile | `/*/*/AgentTaskLandRefusedRetryTests/C688_RealLandProfile*` | D-3 | 1 passed, 0 failed | 1 | 15 | true |
| CP-2 | S1 | CP-1 | lease-window | `/*/*/(LandVerificationLeaseWindowTests*)\|(LandDispatchHandoffTests*)\|(LandRestartReplayTests*)\|(LandVerificationWindowTests*)/*` | CARD-1379 witnesses | 4 passed, 0 failed | 4 | 12 | true |
