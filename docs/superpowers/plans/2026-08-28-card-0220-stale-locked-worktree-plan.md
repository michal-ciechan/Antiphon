# CARD-0220 — plan: a locked worktree with a missing directory must never fail a dispatch (2026-08-28)

Plan only; nothing here is built. One slice, three parts (S1 source fix, S2 self-heal, S3
visibility), plus one optional side fix (S4) that the investigation turned up.

## Root cause — established, not inferred

The card's diagnosis is right about the *state* (registered + locked + directory gone) and this
investigation found **how the state is made**. It is our own code, every time, in a
deterministic five-step sequence:

1. `WorktreeManager.RunGitAsync` (`server/Infrastructure/Git/WorktreeManager.cs:539`) runs every
   git command under a hard 30 s `GitTimeout`; on expiry it `process.Kill(entireProcessTree: true)`
   and rethrows the `TaskCanceledException`.
2. `git worktree add` writes `.git/worktrees/<name>/locked` = `initializing` *before* it checks
   the tree out and unlinks it only on success. A killed process runs no atexit cleanup, so the
   registration, the lock **and the freshly created `feat/card-task-<id>` branch** all survive.
   Reproduced tonight on git 2.50.1.windows.1 in a scratch clone of this repo: kill the add
   ~120 ms in → `git worktree list --porcelain` shows `locked initializing`.
3. `CreateAsync`'s catch (`WorktreeManager.cs:66-81`) then `Directory.Delete(worktreePath,
   recursive: true)`. That is the whole shape: **registered + locked + directory gone + branch
   exists**. `git worktree prune` skips locked entries, so nothing automatic ever clears it.
4. The `TaskCanceledException` is an `OperationCanceledException`, and the per-task catch in
   `AgentTaskDispatcher.TickAsync` (`AgentTaskDispatcher.cs:401`) is
   `when (ex is not OperationCanceledException)` with **no** `|| !ct.IsCancellationRequested`
   arm — the same defect class as the AGENTS.md HttpClient-timeout rule. So attempt 1 escapes the
   per-task catch, kills the **whole tick** ("Delegation dispatch tick failed"), and leaves the
   task `Queued` with no failure recorded.
5. The next tick (5 s poll) calls `CreateAsync` again: `BranchExistsAsync` → `ConflictException`
   → `DelegationWorktreeService.CreateForTaskAsync`'s adopt arm (`DelegationWorktreeService.cs:86-97`)
   finds the registration with `Directory.Exists == false` → throws the card's "directory is gone"
   message → `FailAsync` → `Failed`, `DispatchedAt` null, nobody told.

**Log evidence — 3 of 3 occurrences in the retained window match exactly:**

| task | created | tick died (`TaskCanceledException` ← `RunGitAsync` ← `CreateAsync:64`) | "directory is gone" |
|---|---|---|---|
| `5519184e` | 08-24 23:07:12 | 23:07:49 | 23:08:04 |
| `7af4be46` (the card) | 08-27 06:12:24 | 06:13:06 | 06:13:18 |
| `1cfe0576` | 08-28 10:00:11 | 10:00:49 | 10:00:53 |

`grep -A3 "Delegation dispatch tick failed" server/logs/antiphon-2026082*.log` shows the stack
each time. The "54 seconds" in the card is: ~5-12 s until a tick claimed the task, 30 s timeout,
one more tick.

**Why a `git worktree add` takes over 30 s here:** a quiet-machine add of this repo (3 498 tracked
files) measured **5.4 s** tonight, so 30 s is only a 6× margin, and the 08-27 log shows
`git status --porcelain` timing out in `C:\src\Antiphon` eleven minutes later (06:24:15) — the
machine was IO-starved (overnight builds; CARD-0222's 228 MB ledger reads were live then). The
timeout is not wrong to exist; 30 s is the wrong number for a full checkout.

**The "remove --force sometimes fails, harmless" pattern is NOT the precursor.** I could not find
that gotcha's text in AGENTS.md; its live manifestation is the janitor: `PruneStaleAsync` →
`RemoveAsync` → `git worktree remove --force` → `fatal: '<path>' is not a working tree` (exit 128)
for ~20 metadata files on every janitor run (08-28 19:35 and 21:35, same 20 paths). That is the
*opposite* shape — directory present, registration gone — left when a `git worktree remove`
deleted the admin dir but could not delete every file (Windows file locks). It never touches a
lock and cannot produce CARD-0220's state. Its fix is S4, optional.

## What exists today (the pieces the fix reuses)

- `WorktreeManager.ParseWorktreeList` (`:284`) reads only `worktree ` and `branch ` lines; the
  `locked <reason>` and `prunable <reason>` lines git prints are dropped, so nothing above git can
  see a lock. Verified vocabulary on 2.50.1: `locked initializing`, `locked <custom reason>`.
- `DelegationWorktreeService.CreateForTaskAsync` adopts a leftover worktree of the **same task**
  when its directory exists (a retry keeps the last attempt's commits). The missing-directory arm
  is the only dead end.
- `AgentTaskDispatcher.FailAndNotifyAsync` (`:1312`) is the existing **non-destructive failure
  tail**: `FailAsync` + retire ephemeral agent + a `[task xxxx failed] …` completion note into the
  parent session (`ReplyTo == Session`, WhenIdle, Delegation origin) + `AgentTaskChanged`. Two
  sweeps use it; the dispatch catch at `:401` uses bare `FailAsync` and tells nobody.
- `AttentionKind.RecentFailure` already lists a Failed task for 24 h — it *was* in the feed on
  08-27; the caller was an orchestrator session that never reads the feed.
- `AgentIncident.AgentId` is non-nullable and every incident is keyed to an agent/session. A
  pre-dispatch failure has neither.

## Verified git behaviour the self-heal relies on (scratch clone, git 2.50.1.windows.1)

| state | command | result |
|---|---|---|
| locked + directory fully gone | `git worktree remove --force --force <path>` | **exit 0**, registration removed (lock overridden) |
| locked + directory fully gone | `git worktree prune` | skipped (still registered) |
| locked + directory *partially* present (no `.git` file) | `git worktree remove --force --force` | exit 128 `validation failed … /.git does not exist` |
| directory present, registration gone (janitor shape) | `git worktree remove --force` | exit 128 `is not a working tree` |
| any | `git worktree unlock <path>` then `remove --force` | the operator's hand fix; also works |

So the heal must delete any partial directory **first** (it is under `Git:WorktreeBasePath` and on
our own `feat/card-` branch — nothing else is ever there), then `remove --force --force`, then
`prune` as belt-and-braces, and only then touch the branch.

## Design

### S1 — stop manufacturing the state (`WorktreeManager.CreateAsync`, `RunGitAsync`)

1. **A per-command budget that fits the command.** New `GitSettings.WorktreeAddTimeoutSeconds`
   (default **180**) used for `worktree add` only; every other command keeps the 30 s constant.
   A full checkout under IO load is legitimately slow; a `show-ref` is not.
2. **A timeout is a `TimeoutException`, never an `OperationCanceledException`.** In
   `RunGitAsync`, `catch (OperationCanceledException) when (!ct.IsCancellationRequested)` after
   the kill → throw `TimeoutException($"git {args} timed out after {budget}s in {dir}")`. Real
   shutdown (`ct` cancelled) still propagates as OCE. This fixes the type at the source so every
   `when (ex is not OperationCanceledException)` above it behaves.
3. **Full rollback of a failed add, on a fresh token.** Replace the catch's `Directory.Delete`
   with `RollbackFailedAddAsync(repo, worktreePath, branch, branchExistedBefore: false)`:
   delete the directory if present → if still registered, `worktree remove --force --force` →
   `worktree prune` → `branch -D <branch>` (safe: `BranchExistsAsync` proved it did not exist
   before this call). Runs under `CancellationToken.None` with the 30 s per-command budget so a
   cancelled `ct` cannot abort the cleanup halfway. Each step logged; a rollback failure is logged
   at Warning and the **original** exception is what propagates.
4. **Defence in depth at the dispatcher:** `AgentTaskDispatcher.cs:401` becomes
   `when (ex is not OperationCanceledException || !ct.IsCancellationRequested)` so a timeout of
   any type fails ONE task with its real reason rather than the tick.

### S2 — self-heal a stale registration at create (`WorktreeManager.CreateAsync`)

Placed in `WorktreeManager`, **not** `DelegationWorktreeService`, so `IWorktreeManager` is
unchanged — it has 17 test fakes and an interface change would touch all of them for nothing.

1. Extend `WorktreePorcelainEntry` with `Locked` / `LockReason` / `Prunable` parsed from the
   `locked` and `prunable` lines (pure parsing, unit-tested).
2. Before the two existing conflict checks (`Directory.Exists`, `BranchExistsAsync`): list
   worktrees; if an entry's path equals `worktreePath` **and** the directory does not exist, this
   is the stale shape (locked or not — an unlocked one is the same dead end for the dispatcher,
   because only the janitor ever prunes, and only for stale metadata). Heal exactly as the
   verified table says: `remove --force --force` → `prune`; log at **Warning** naming the path,
   the lock reason (`initializing` = "a killed add", the smoking gun) and what was run.
3. **The branch is re-attached, never deleted.** If `feat/card-<id>` still exists after the heal,
   create the worktree *on* it (`git worktree add <path> <branch>`, no `-b`) — the same rule as
   the adopt arm: whatever a previous attempt committed is preserved. For the killed-add case the
   branch sits at the base ref, so this is equivalent to a fresh create. If the branch is gone,
   the normal `-b` path runs.
4. If the heal fails, throw `ConflictException` carrying the original diagnosis **plus** each
   command tried and git's stderr, and `DelegationWorktreeService.CreateForTaskAsync` must
   include that inner message instead of replacing it (today it swallows the inner
   `ConflictException` and prints its own sentence).
5. Metadata: `SaveMetadataAsync` after a healed create as for a fresh one; delete any stale
   metadata file for the path first.

### S3 — a pre-dispatch failure reaches the caller

Change the dispatch catch (`AgentTaskDispatcher.cs:401-406`) from `FailAsync` to
**`FailAndNotifyAsync(task, reason, "dispatch", ct)`** — the existing tail. The caller gets the
same `[task xxxx failed] <title> · <model>` completion note every other failure already produces,
WhenIdle, batched under `task:<root>`; the drawer and `RecentFailure` rows are unchanged. The
reason text should name the stage: `Dispatch failed before a session existed: <ex.Message>`.
`RemoveEphemeralAgentAsync` is null-safe on `AgentId`, so the tail needs no change.

**Why not an `AgentIncident`:** it needs an `AgentId` (non-nullable) and a session; a task that
failed before either exists would need a schema change or a placeholder agent, and the alert
router would then digest it to channel sinks by severity — a Telegram line for a shape that, after
S1/S2, should never recur. The party actually waiting is the orchestrator session, and the
completion note is the channel it already listens on. The tradeoff, stated plainly: a caller that
delegated with `ReplyTo = None` (no parent session) still gets only the feed row and the task row.
That is the same coverage every other failure has today; it is not widened here.

### S4 — optional, separate card recommended: the janitor's "is not a working tree" loop

`RemoveAsync` (`:162`): when the directory exists but `git worktree remove --force` says
`is not a working tree`, the registration is already gone — delete the directory and the metadata
ourselves instead of throwing, so the same 20 paths stop failing on every janitor run. Ten
lines; not this card's failure, so not required to close it.

## Tests

`tests/Antiphon.Tests/Application/DelegationWorktreeTests.cs` (real git via `ScratchGitRepo`,
the file's existing pattern):

- `a_locked_registration_whose_directory_is_gone_is_healed_and_the_task_dispatches` —
  `git worktree add --lock -b feat/card-task-<id> <path>`, delete the directory,
  `CreateForTaskAsync` succeeds, directory exists, `worktree list` shows the entry unlocked, one
  entry only, task fields set. **Red today** with the card's exact message.
- `an_unlocked_registration_whose_directory_is_gone_is_healed_too` — same without `--lock`.
- `healing_re_attaches_the_task_branch_and_keeps_its_commits` — commit on the branch before
  deleting the directory; after the heal `rev-list --count base..HEAD` is unchanged.
- `a_failed_worktree_add_leaves_no_registration_branch_or_directory` — make the add fail
  deterministically with a `core.hooksPath` `post-checkout` hook that exits 1 (git reports the
  hook's status as the add's); assert nothing is left. For the **timeout** arm, the same hook
  with `sleep 30` and `WorktreeAddTimeoutSeconds = 1`: assert `TimeoutException` (not OCE) and the
  same clean post-state. If the hook proves awkward on the CI shell, the rollback helper can be
  driven directly against a hand-made `--lock` registration; the parsing and heal tests above
  already cover the state.
- `WorktreeManagerTests` (new, unit): `ParseWorktreeList` captures `locked <reason>` and
  `prunable <reason>`, and a bare `locked` line.

`AgentTaskPoolTests`-style harness (real `WorktreeManager` through DI):

- `a_dispatch_that_throws_before_a_session_exists_tells_the_caller` — seed a Worktree task with
  `ParentSessionId` + `ReplyTo = Session` whose `RepoPath` is a plain directory (not a git repo)
  so `CreateAsync` throws `ValidationException`; after `TickAsync`: task `Failed`, `DispatchedAt`
  null, one `SessionQueuedMessages` row for the parent with `Origin = Delegation` and a header
  starting `[task <id> failed]`. **Red today** (no row).
- `a_git_timeout_fails_one_task_not_the_tick` — two queued tasks, the first with a
  `WorktreeAddTimeoutSeconds` it cannot meet (hook sleep), the second healthy: the tick returns
  `Failures = 1, Dispatched = 1`. Red today (the OCE aborts the tick before the second task).

Run: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0220/ --treenode-filter
"/*/Antiphon.Tests.Application/DelegationWorktreeTests/*"` (and the dispatcher class), then delete
the `bin-0220` directories.

## AGENTS.md gotcha to add when this lands

> **A killed `git worktree add` leaves git's own `locked initializing` behind, and a locked
> registration with no directory failed every future dispatch of that task id** (CARD-0220):
> `WorktreeManager`'s 30 s per-command timeout killed the checkout under IO load (a quiet add of
> this repo is 5.4 s), the catch deleted the directory, and the timeout's `TaskCanceledException`
> escaped the dispatcher's per-task catch and killed the tick. `worktree add` now has its own
> budget (`Git:WorktreeAddTimeoutSeconds`, 180), a timeout is a `TimeoutException`, a failed add
> rolls back fully (directory → `remove --force --force` → `prune` → branch), `CreateAsync`
> heals a registered-but-missing worktree (re-attaching the branch, never deleting it), and a
> dispatch failure reaches the caller as a `[task … failed]` note through `FailAndNotifyAsync`.
> `git worktree remove --force --force` clears a locked+missing entry in one command; it does NOT
> clear one whose directory is partially present — delete the directory first.

## Operator decisions

1. **`WorktreeAddTimeoutSeconds` default.** Recommend **180**. 30 s failed three times in five
   days on a 5.4 s-quiet operation; 180 still bounds a genuinely wedged git.
2. **Heal re-attaches the branch rather than deleting it.** Recommend re-attach (never destroys a
   previous attempt's commits; matches the adopt arm). Say if you would rather a healed create
   always start fresh from the merge target.
3. **Alert in addition to the completion note?** Recommend **no**: the note reaches the waiting
   orchestrator; the feed already shows `RecentFailure`; after S1/S2 the remaining dispatch
   failures are real configuration gaps that the note names. If you want a human-facing line too,
   `IAlertService.RaiseAsync(Warning, Source: "delegation")` from the same catch is five lines —
   but it will go to every Warning sink.
4. **S4 in this card or its own?** Recommend its own small card; it is a different shape with
   its own log signature and should not be verified under this one's tests.

## Not planned

- Any change to `git worktree prune` semantics, the janitor cadence, or `WorktreeStaleAfterDays`.
- Retrying a failed dispatch automatically. With S1 the retry would be clean, but the caller
  hearing about it (S3) and re-dispatching is the existing contract; an automatic retry is a
  different card with its own attempt accounting.
- Making `AgentIncident.AgentId` nullable.
