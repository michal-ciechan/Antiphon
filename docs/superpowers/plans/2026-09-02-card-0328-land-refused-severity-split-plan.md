# CARD-0328 — LandRefused severity split: landed-with-residue vs not landed

**Date:** 2026-09-02
**Card:** CARD-0328 (spin-off of CARD-0272 §2.4 item 1)
**Verdict:** Mechanical. The card's "small enough to skip straight to Code" instinct is right for the
split and the cleanup step (S1+S2, one Code pass). The sweep (S3) is also Code-sized but carries the
only real policy choice, so it is a second pass with its rules written out below. Nothing here needs
a further Plan stage.

---

## 1. Verified current behaviour (live DB + server log + scratch git 2.50.1)

**Counts since the land op shipped (2026-09-01 12:57 → 2026-09-02 17:35):** 51 `LandRequested`,
38 `Landed`, 11 `LandRefused`. Of the 11 refusals, **10 begin "Landed and pushed, but could not
delete feat/card-task-…"** — the target had advanced and `git push origin master` had succeeded
before the refusal was written. One (gym-stat `87e7e1ae`, MSB1003) is a real refusal.

**Where the code is.** `AgentTaskLandService.RunAsync` → `DelegationWorktreeService.PrepareLandAsync`
(fetch, remote-ahead check, rebase) → `VerifyAsync` (build) → `FinalizeLandAsync` (ff target, push,
cleanup). `FinalizeLandAsync` cleans up with `RemoveQuietlyAsync` (= `WorktreeManager.RemoveAsync`:
`git worktree remove --force` → `git branch -D` → delete metadata, any exception swallowed to a
warning) and then a second, unconditional `git branch -d`. If `-d` fails and `show-ref` still finds
the branch, `FinalizeLandAsync` returns `Succeeded=false` and `RunAsync` calls `RefuseAsync`, which
writes `LandRefused` + a `Warning` event and delivers `land refused: …` to the caller. That is the
conflation: one boolean for "pushed" and "cleaned".

**The two mechanisms, matched 10/10 against `server/logs/antiphon-2026090{1,2}.log`:**

| Refusal text | Count | Log line for the same task | Why |
|---|---|---|---|
| `error: cannot delete branch '…' used by worktree at '…'` | 7 | `System.TimeoutException: git worktree remove --force … timed out after 30s` | `WorktreeManager.GitTimeout` is 30 s. Deleting a 10-20k-file worktree (`bin/`, `obj/`, `node_modules`) takes longer on this disk. `RunGitAsync` kills git before it reaches `delete_git_dir`, so the registration survives and the branch stays checked out. The half-deleted directory (2935a868 still has 18,841 files) is left behind. |
| `warning: not deleting branch '…' that is not yet merged to 'refs/remotes/origin/…', even though it is merged to HEAD` | 3 | `InvalidOperationException: git worktree remove --force failed with exit code 255: error: failed to delete '…': Invalid argument` | git hit an undeletable entry (a held handle — CARD-0308's class), but git's `remove` continues to `delete_git_dir` after a failed tree delete ("no going back"), so the worktree IS unregistered. `RemoveAsync` throws before its own `-D`; the second `branch -d` then consults the delegate's `push -u` upstream (`branch.<b>.merge` is set on every one of these) and refuses because `origin/feat/…` is behind the rebased tip. |

The same two failures hit the settlement merge-back (`TryMergeBackAsync` → `RemoveQuietlyAsync`) six
more times in the same window (57a1a335, 598eb79d, c906e265, df9c286d, 05e34c05, 29f4378a — all
"Invalid argument"). That path never refuses anything; it just leaves the directory.

**Scratch-repo confirmation (git 2.50.1.windows.1):** `branch -d` on a merged branch with an
unmerged upstream fails, `-D` succeeds; `-D` on a branch checked out in a registered worktree fails
with "used by worktree at"; `worktree remove --force` on a directory deleted out from under git exits
0 and unregisters; a locked worktree needs `--force --force` (the existing CARD-0220 heal path).

**What the residue looks like today (this machine, 2026-09-02, plan time):**

| Class | Count | Detail |
|---|---|---|
| Registered worktrees (`git worktree list`, excluding main) | 16 | 10 are settled, 0 commits ahead of master, clean → pure residue. 6 must be kept: `510b73f8` and `7e10b572` (Succeeded, 3 and 1 commits never landed), `bf31273c` (Succeeded, `LandRequested` still pending — held behind Shared writers), `fcf948b8` (Failed, 1 commit), `7016fc31` (Failed, 11 modified tracked files), `f091e3e2` (Dispatched, live). |
| Local `feat/card-task-*` branches | 17 | The 16 above + `a6437d7a` (Succeeded 08-25, merged, no worktree). 13 are `--merged master`. |
| Leftover directories under `C:\Antiphon\worktrees` with no registration | 25 | 23 belong to Succeeded tasks, 2 to Canceled. 20 hold 3k-28k files each (the failed/killed deletes above); 5 are empty; `f316fd9a` still has its `.git` file. 4 of the 41 directories have no `.antiphon/worktrees/*.json` metadata record, so the janitor cannot see them. |
| Remote `origin/feat/card-task-*` branches | 163 | Delegates push with `-u`. Not touched by any cleanup today. |

**The janitor is the existing git-worktree reap tooling**, not CARD-0298's census.
`WorktreeJanitorHostedService` runs `WorktreeManager.PruneStaleAsync` at startup and every 24 h over
the metadata records, removing anything whose `LastTouchedAt` is older than 7 days
(`Git:WorktreeStaleAfterDays`) via the same throwing `RemoveAsync`. It has no task-status context
(Infrastructure layer, no `AppDbContext`), so it cannot tell a landed worktree from a Blocked task's
worktree — which is exactly why it must stay TTL-based and why the sweep below lives in Application.
CARD-0298's `ZombieCensusJob` is OS-process shaped (WMI + runner claims), report-only, and knows
nothing about repos or branches; extending it would mean bolting git onto a process classifier.

---

## 2. Design

### 2.1 Vocabulary: one new event, no new task status

- `AgentTaskEventType.LandedWithResidue = 24` (append after `Rerouted = 23`): the target advanced
  and the push succeeded; the branch and/or directory could not be fully removed. `Landed` keeps its
  meaning (pushed AND cleaned). `LandRefused` now means only "the target did not advance" (fetch,
  remote-ahead, rebase, verify, fast-forward, or push failed).
- `AgentTaskStatus` is unchanged (Succeeded stays Succeeded — the CARD-0159 rule). The residue is a
  fact about the repo, not the task.
- Outcome line delivered to the caller keeps the `landed …` prefix so every existing reader (the
  backfill's `verify:` head parser, the orchestrator's eye) still sees a landing:
  `landed {branch} -> {target} as {sha}, pushed (origin/{target}={sha}), verify: {verify}, cleanup incomplete: {residue}`
  versus the current `…, worktree removed`. No `Warning` event for residue (it is not warning-grade);
  the `Cleanup Failed` `StageOutcome` row CARD-0272 already writes is the measurement.
- Residue text is one clause naming what is left and why, e.g.
  `directory C:\Antiphon\worktrees\card-task-x still exists (git: failed to delete '…': Invalid argument); branch deleted`
  or `branch feat/card-task-x kept (still used by worktree at …)`.

### 2.2 Cleanup step: one robust, non-throwing sequence in `WorktreeManager`

Add `Task<WorktreeRemoval> TryRemoveAsync(string repoPath, string worktreePath, string? mergedInto,
CancellationToken ct)` to `IWorktreeManager` **with a default interface implementation** that calls
`RemoveAsync` and returns clean — the ten-plus test fakes implementing the interface then compile
untouched. `RemoveAsync` becomes a wrapper over the same core that throws when the result is not
clean (existing semantics and `WorktreeManagerTests` preserved). `WorktreeRemoval` is a record
`(bool Unregistered, bool DirectoryGone, bool BranchDeleted, string? Residue)` with `IsClean =>
Residue is null`.

The core, in order, each step independent of the previous one's success:

1. **`git worktree remove --force <path>`** (single `--force`: a locked worktree stays a refusal,
   pinned by `WorktreeManager_remove_still_throws_when_the_worktree_is_locked`) with a new budget
   `Git:WorktreeRemoveTimeoutSeconds`, default **300** (add the arm to `TimeoutFor`, next to the
   180 s `worktree add` one). On "is not a working tree" fall through (CARD-0229 path).
2. **If step 1 failed or timed out:** `TryDeleteDirectory(path)` (own recursive delete, attribute
   reset, no timeout, already exists) then **`git worktree prune`**. Capture the first exception
   message from the directory delete as the residue clause. This is the operator's manual
   `remove --force` → `prune` sequence, with the directory delete retried by us when git's was cut
   short.
3. **Branch:** when `mergedInto` is given (land and merge-back callers), run
   `git merge-base --is-ancestor <branch> <mergedInto>`; if true, `git branch -D <branch>`; if false,
   keep the branch and add `branch kept: N commit(s) not on <mergedInto>` to the residue — never
   force-delete unmerged work. When `mergedInto` is null (janitor TTL path) keep today's
   unconditional `-D` (existing 7-day policy, pinned by `WorktreeJanitor_prunes_stale_worktrees`;
   not this card's question). A `-D` that fails "used by worktree" means step 2 left the registration
   in place; report it, do not retry in a loop.
4. **Metadata:** delete the `.antiphon/worktrees/*.json` record only when the result is clean;
   otherwise stamp `residueSince` (new optional field, schemaVersion stays 1) so `PruneStaleAsync`
   retries that record on every cycle instead of waiting out the TTL.

`FinalizeLandAsync` drops its second `branch -d` entirely and calls `TryRemoveAsync(repo, worktree,
mergedInto: target)`. `LandFinalization` becomes `(bool Pushed, string? Sha, string? Detail, string?
Residue)`. `TryMergeBackAsync` uses the same call and appends `; cleanup incomplete: …` to the
`Merged` detail when residue remains.

### 2.3 Land service

- `RunAsync`: `!finalized.Pushed` → `RefuseAsync` (unchanged). `finalized.Residue != null` →
  `LandedWithResidue` event with the outcome line from §2.1, `Cleanup Failed` stage row (detail =
  residue). Otherwise `Landed` as today.
- `RequestAsync`'s "already queued" query must include the new type in its latest-outcome set
  (`LandRequested | Landed | LandRefused | LandedWithResidue`); otherwise the first re-land after a
  residue landing finds the older `LandRequested` as the latest and returns 409.
- **Re-land is the cleanup retry verb.** `RequestAsync` already admits a second `-Land` after any
  non-`LandRequested` latest event. Add an already-landed short-circuit at the top of `RunAsync`:
  if the branch ref is gone, or `merge-base --is-ancestor <branch> origin/<target>` holds, skip
  prepare/verify and run only the cleanup, settling as `Landed` ("nothing left to clean" when both
  branch and directory are already gone) or `LandedWithResidue`. Without this, a re-land after the
  directory was removed would fail inside `PrepareLandAsync`'s worktree commands and be reported as
  a refusal — the same conflation in a new coat.
- `StageOutcomeBackfillService`: add `LandedWithResidue` to the event filter and a `RowsFor` arm
  (Rebase Clean, Verify from the `verify:` head as for `Landed`, Cleanup Failed). The ten historical
  "could not delete" `LandRefused` rows keep their existing classification; they are not rewritten.

### 2.4 Sweep: a small Hangfire job in Application, not a census extension

`WorktreeResidueSweepService` (Application) + `WorktreeResidueJob` (Infrastructure/Agents, the
`ZombieCensusJob` shape: `[AutomaticRetry(Attempts = 0)]`, one Information summary, one Warning per
kept row) + `WorktreeResidueSettings { Enabled = true, RecurringJobId = "antiphon:worktree-residue",
Cron = "0 10 * * *", TimeZoneId = "Europe/London", Execute = false, MinSettledMinutes = 120 }` +
`HangfireConfiguration.AddOrUpdateWorktreeResidueJob` mirroring the census registration. The
dashboard's "Trigger now" is the one-time rollout pass; `Execute` is the report-only gate the
operator asked for on CARD-0295/0298.

Candidates = registered worktrees under `Git:WorktreeBasePath` ∪ leftover `card-task-*` directories
under it ∪ local `feat/card-task-*` branches, each joined to `AgentTasks` by `WorktreePath` or
`WorktreeBranch` equality. Rules, first match wins:

| Rule | Label | Action |
|---|---|---|
| No task row | `Unknown` | report only — never act on what the board cannot explain |
| Task Queued/Dispatched/Working/Blocked | `Live` | keep |
| Task settled less than `MinSettledMinutes` ago | `Settling` | keep (a land may be queued or held) |
| Branch exists and is not an ancestor of `MergeTargetRef ?? Git:DefaultBranch` | `Unmerged (N ahead)` | keep, report |
| Directory has modifications — tracked-only for Succeeded tasks with a `Landed`/`LandedWithResidue`/`Merged` event, tracked+untracked (`git status --porcelain`) for Failed/Canceled | `Dirty` | keep, report |
| Otherwise | `Eligible` | `TryRemoveAsync(mergedInto: target)` when `Execute`, else log the row |

Applied to today's census the first execute run removes 10 registered worktrees, the `a6437d7a`
branch, and 25 leftover directories, and reports the six keeps named in §1 with their reasons.
Directories whose files are still held open (CARD-0308's holders) stay as reported residue and are
retried daily; the job does not kill anything.

---

## 3. Decisions

| # | Question | Recommendation |
|---|---|---|
| D1 | New event vs `residue=` field on `Landed` | **New event `LandedWithResidue = 24`.** Every consumer of the land outcome switches on the type (`RequestAsync`, backfill, orchestrator reading, the CARD-0272 report); a field would be invisible to all of them. |
| D2 | `--force` or `--force --force` in the cleanup step | **Single `--force`.** None of the 10 cases was a lock; the double-force override stays with CARD-0220's heal path. |
| D3 | Sweep home | **Second Hangfire recurring job, report-only by default.** Not the census (wrong shape, wrong layer), not the janitor (no task context, so it would delete a Blocked task's clean worktree). The janitor stays as the TTL backstop and gains only the `residueSince` retry. |
| D4 | Delete `origin/feat/card-task-*` when the delegate pushed with `-u` | **Not in this card.** 163 remote branches cost nothing locally; deleting them is an external side effect on GitHub. File a follow-up if wanted; `GitService.DeleteBranchAsync` already has the best-effort `push origin --delete`. |
| D5 | `Execute` default for the sweep | **false** until one report-only run has been read on the dashboard, then flip in `appsettings` — same burn-in bar as CARD-0298. |

---

## 4. Slices

| Slice | Files | Tests | Estimate |
|---|---|---|---|
| **S1** robust cleanup | `server/Infrastructure/Git/WorktreeManager.cs` (`TryRemoveAsync` core, `TimeoutFor` arm, `residueSince`, `PruneStaleAsync` retry rule), `server/Application/Interfaces/IWorktreeManager.cs` (+`WorktreeRemoval`, default impl), `server/Application/Settings/GitSettings.cs` + `server/appsettings.json` (`WorktreeRemoveTimeoutSeconds: 300`), `server/Application/Services/DelegationWorktreeService.cs` (`FinalizeLandAsync`, `TryMergeBackAsync`, `RemoveQuietlyAsync` → result) | `WorktreeManagerTests`: merged branch with an unmerged upstream is deleted (the 3× case); killed/partial delete leaves registration → step 2 unregisters and reports directory residue; unmerged branch is kept with the ahead count; locked worktree still throws via `RemoveAsync`. `DelegationWorktreeTests`: `land_happy_path…` unchanged; new `land_with_upstream_set_deletes_the_branch`. | 3–4 h |
| **S2** severity split | `server/Domain/Enums/AgentTaskEnums.cs` (+24), `server/Application/Services/AgentTaskLandService.cs` (`RunAsync` residue arm, already-landed short-circuit, outcome line), `server/Application/Services/StageOutcomeBackfillService.cs`, `client/src/api/agentTasks.ts` (`AgentTaskEventType` union — already stale past `Refined`; add the land trio while there), docs: `docs/orchestration-loop.md` §1 "Also delegated" paragraph, §5 land paragraph, §8; `docs/antiphon-api.md` + `docs/ops-http.md` (the `POST /api/agent-tasks/{id}/land` route and its three outcome events are undocumented today) | `AgentTaskLandStageOutcomeTests`: residue land writes Rebase Clean / Verify / Cleanup Failed and a `LandedWithResidue` event, no `LandRefused`; re-land of an already-landed task runs cleanup only. `StageOutcomeBackfillTests`: one `LandedWithResidue` case. | 2–3 h |
| **S3** sweep | `server/Application/Services/WorktreeResidueSweepService.cs`, `server/Application/Settings/WorktreeResidueSettings.cs` (+validator), `server/Infrastructure/Agents/WorktreeResidueJob.cs`, `HangfireConfiguration.cs`, `Program.cs` registration next to the census, `server/appsettings.json`, `docs/bootstrap.md` (Hangfire job list) | `WorktreeResidueSweepTests`: pure classification over a fixture (six labels above, one row each) and one real-git integration case (`ScratchGitRepo`) proving `Execute=false` touches nothing and `Execute=true` removes only the eligible row. | 3–4 h |

S1+S2 are one Code pass in one worktree (they touch the same two services). S3 is a second pass and
can be dispatched in parallel; it depends only on S1's `TryRemoveAsync`.

**Verify (alternate output path, delete the `bin-0328` directories afterwards):**

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0328/ -- --treenode-filter "/*/*/WorktreeManagerTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0328/ -- --treenode-filter "/*/*/DelegationWorktreeTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0328/ -- --treenode-filter "/*/*/AgentTaskLandStageOutcomeTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0328/ -- --treenode-filter "/*/*/StageOutcomeBackfillTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0328/ -- --treenode-filter "/*/*/WorktreeResidueSweepTests/*"
Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-0328 | Remove-Item -Recurse -Force
```

**Rollout:** land S1+S2, restart the AppHost, land one real Worktree task and read its `Landed`
line. Land S3, restart, open `/hangfire`, trigger `antiphon:worktree-residue` once with `Execute`
false, read the 6 kept rows against §1, set `WorktreeResidue:Execute = true`, restart, trigger again.
Expected: `git worktree list` shows main + the 6 keeps; 25 leftover directories gone except any a
live handle still holds (those name the holder in the log — CARD-0308 territory).

---

## 5. Not in this card

- The 30 s `GitTimeout` on every other git call in `WorktreeManager` is unchanged; only `worktree
  remove` gets the 300 s arm, the same shape as CARD-0220's `add` exception.
- Why `remove` takes over 30 s (Defender per-file scanning of `bin/`/`obj/` deletes) and whether the
  land verify should build somewhere other than inside the worktree — a separate question.
- `bf31273c`'s land has been `LandRequested` since 2026-09-02 12:22 with no outcome: held behind
  Shared writers by `FindSharedWriterAsync`. Correct per CARD-0258; the sweep labels it `Settling`
  or `Unmerged`, never eligible.
- CARD-0308 (process-tree kill) is what shrinks the "Invalid argument" class; this card only stops
  it from being called a failed land.
- Remote branch pruning (D4) and the janitor's unconditional `-D` after 7 days on the TTL path.
