# runner_sync_identity_mismatch on a1f403eb (Debug 8e5fd2d6, 2026-10-22)

Read-only investigation. Nothing on the desktop, the database or the task was changed. Live server
rc `0714a9a89`; code read at master `adc32643f`. `WorktreeManager.cs` is byte-identical on the rc, and
the `Locked` refusal in `RemoteWorkspaceService.cs` is the same there.

## Outcome

The sync refused because **the desktop registration of `C:\Antiphon\worktrees\card-task-a1f403eb` is
`locked`, and the checkout is only partly populated**. An earlier `git worktree add` for this
task was killed while it was checking files out. Git leaves its `locked initializing` marker
when that happens. `WorktreeManager.RollbackFailedAddAsync` refuses to roll back a locked
registration. The dispatcher's next tick then **adopted** the half-built directory as the task's
worktree. The task ran normally on the server2 mirror, which is built from the pushed branch.
At settlement, `ValidateCheckoutAsync` saw the locked registration and returned
`runner_sync_identity_mismatch`.

Ruled out:
- the 15:54Z restart and the rc bundle: the code dates from 2026-09-08 (`cf59f1083`);
- CARD-1479's lease waiter: the refusal comes before any lease wait;
- the re-cut, which happened on the server2 mirror.

The existing Backlog card **CARD-1448** (the same failure on 7c30c896) is this bug.

## 1. What the sync compares, and what differed

`RemoteWorkspaceService.SyncEligibleAsync` / `ValidateCheckoutAsync` return
`runner_sync_identity_mismatch` in these cases:

| Check | Where | a1f403eb |
|---|---|---|
| baseline `ownerTaskId` = task id | pre-lease (`:455-459`) | equal (`a1f403eb-4895-…`) |
| `task.WorktreePath` = baseline `registeredCheckout` | pre-lease | equal (`C:\Antiphon\worktrees\card-task-a1f403eb`) |
| repo and worktree `--git-common-dir` = baseline `canonicalCommonDirectory`; IOException | `:853-863` | not established (desktop git not reachable) |
| exactly one registration at the path, **not Locked**, not Prunable | `:865-867` | **Locked**: fails here |

Evidence:
- `GET /api/filesystem/worktrees?path=C:\src\Antiphon` (222 registrations) shows exactly three
  `isLocked: true`: `card-task-a1f403eb`, `card-task-7c30c896`, and `land\0f9784a67773`. The land
  workspace locks itself on purpose (`LandWorkspace.cs:107`).
- `progressEvidence.remoteSync` is `{attempt 1, state Refused, observedSha null}`. A refusal under
  the lease would carry `RemoteSha`, so this came from the first `ValidateCheckoutAsync`, before
  any observation, fetch or lease wait.
- The checkout is partial. `GET /api/filesystem/browse` shows these top-level directories
  (base `6a2fe6ae3` tracks all of them):

  | Checkout | `src` | `tests` | `tools` | `server`, `scripts`, `client`, `docs` |
  |---|---|---|---|---|
  | a1f403eb | missing | missing | missing | present |
  | 7c30c896 | present | present | missing | present |
  | 8e5fd2d6 (control) | present | present | present | present |

- The task row was adopted rather than created. It shows `worktreeBaseRef/worktreeBaseSha/commitBaselineSha = null` and
  `worktreeBaseSource = Unset`. The Dispatched event reads `Worktree created at … (no merge target …)` with **no
  `from <sha>`**. That is the shape of the adopt branch in `DelegationWorktreeService` (`ListAsync`
  match + `Directory.Exists` returns early without recording a base). Every fresh creation in the
  same windows says `from <sha>`. Since 2026-10-15 only three dispatches have this shape:
  a6d66bd8 (10-15, Failed), 7c30c896 and a1f403eb.
- Baseline (`ProgressBaselineJson`, 15:36:38Z): repo `C:\src\Antiphon`, common `C:\src\Antiphon\.git`,
  ref `refs/heads/feat/card-task-a1f403eb`, localSha `6a2fe6ae3…`, remote Missing. The branch ref existed
  because `worktree add -b` writes it before the checkout. That is why the remote preparation could push it,
  and why the mirror and the agent worked.

The mechanism, in code:
1. `WorktreeManager.CreateAsync` saves intent metadata (`CreationComplete=false`), then runs
   `git worktree add -b <branch> <path> <base>` with a 180 s timeout (`Git:WorktreeAddTimeoutSeconds`).
2. If git is killed mid-checkout (timeout, tick cancellation, or the process tree dying in a restart),
   the registration keeps `locked initializing` and the directory is partial.
3. `RollbackFailedAddAsync` returns silently when `matching[0].Locked`, so nothing is undone and nothing is logged.
   After a process kill it never runs at all.
4. On the next tick, `WorktreeManager.ListAsync` returns the metadata entry **without checking
   `CreationComplete`**. `DelegationWorktreeService` adopts it because the directory exists. If it did not,
   `CreateAsync` would throw "path already exists" and the catch would adopt the same entry.
5. Settlement: the registration is Locked, so the sync refuses with identity mismatch, and no sync debt is
   created (`syncDebt: null`; debt exists only for `runner_sync_lease_busy`).

## 2. Cause: restart straddle, rc bundle, or the re-cut

- **Not the 15:54Z restart.** The lock predates the dispatch at 15:36:38Z. a1f403eb has no events
  between 15:30:17Z (Held: base inspection) and 15:35:11Z (HeldAged). The fleet event stream has a
  gap from 15:30:42Z to 15:34:33Z, which contains the **15:33Z restart**. The most likely interrupter is
  that first restart killing an in-flight `worktree add`. The 180 s timeout is the other candidate.
  Desktop server logs, which would show which, are not reachable from server2. 7c30c896 had no
  restart in its window. It had a 5-minute event gap from 01:39:02Z to 01:44:28Z while a land held the
  repository, so a timeout or cancel is the likely trigger there.
- **Not the rc bundle.** The adopt path and the rollback's `Locked` early return come from `cf59f1083`
  (2026-09-08), are on rc `0714a9a89`, and are unchanged. The earlier occurrences predate the bundle.
- **Not CARD-1479.** The refusal is before the lease, and `WhileLeaseBusyAsync` is never reached.
- **Not the re-cut.** The desktop checkout is cut before the agent starts. The cherry-picks happened on
  `/work/worktrees/task-a1f403eb`.

## 3. Other occurrences

Since 15:33Z: **none besides a1f403eb**. All time, from `CompletionProgressEvidenceJson` and
AgentTaskEvents (12 events, 5 tasks):

| Task | Settled | Status now | Note |
|---|---|---|---|
| 273b3a0d | 09-25 12:44Z | Canceled | registration `locked initializing`, no directory (pruned by CARD-1432 on 10-21) |
| 6fea402b | 09-30 13:24Z | Canceled | same |
| 4c2309e1 | 10-01 00:40Z | Canceled | same |
| 7c30c896 | 10-22 02:04Z, 02:06Z | Canceled | still locked, partial (no `tools/`); CARD-1448 |
| a1f403eb | 10-22 16:02Z | Blocked | locked, partial |

So this is not new today. It is rare, about 5 in 4 weeks.

## 4. Report and branch intact; what "repair" means

- Branch `feat/card-task-a1f403eb` on origin is at `5d1623b88f708774194c02e6a5fb38986117825b`
  (`git ls-remote`). There are 11 commits over `6a2fe6ae3`, and the tip commit adds
  `.antiphon/task-a1f403eb.md` and the plan addendum.
- The retained result is 4,868 characters, `reportEvidence: Marked`, with handoff and artifact lines. The
  `blocked.question` holds the Review handoff.
- The desktop checkout should hold no unique work: its branch was at `6a2fe6ae3` at baseline and the sync never moved it. The first repair command below confirms this.

Repair, for the operator on the desktop from `C:\src\Antiphon`. Not run. Do it while no land is running
on the repository:

```powershell
git worktree list --porcelain | Select-String -Context 0,5 'card-task-a1f403eb'   # expect HEAD 6a2fe6ae3..., branch refs/heads/feat/card-task-a1f403eb, locked [initializing]
Get-Process git -ErrorAction SilentlyContinue                                     # no git for that path
Test-Path C:\src\Antiphon\.git\worktrees\card-task-a1f403eb\index.lock             # if True and no git is running: Remove-Item it
git worktree unlock C:\Antiphon\worktrees\card-task-a1f403eb
git -C C:\Antiphon\worktrees\card-task-a1f403eb rev-parse HEAD                     # must be 6a2fe6ae390400aff141bf99633d427f78f47346
git -C C:\Antiphon\worktrees\card-task-a1f403eb reset --hard HEAD                  # repopulates src/ tests/ tools/
git -C C:\Antiphon\worktrees\card-task-a1f403eb status --porcelain=v1 --untracked-files=all   # must print nothing
```

The same steps apply to 7c30c896's checkout. That task is Canceled, so cleanup is optional there.

## 5. Is `-Reply a1f403eb` safe?

- **Before the repair: no.** A Reply only reopens the turn. The next completion re-runs the same sync,
  which hits the same Locked registration and blocks again. A short answer ("Accepted… stop") would
  also replace the retained 4,868-character report (CARD-1472; 7c30c896 is exactly that case:
  "Acknowledged. Stopping." → Canceled, report still sync-blocked).
- **Unlike `runner_sync_lease_busy`, nothing clears this block by itself.** There is no debt row
  and no sweep. Standing rule 6's premise, "the settlement sync sweep clears the block", does not hold
  for this code. The rule's conclusion still holds: do not Reply "stop".
- Two safe routes:
  - **(a) Repair first (section 4), then Reply** asking for the complete final report again, verbatim, with
    its `--- next stage ---` block and closing `[antiphon-report:a1f403eb done]` line. Never ask it to
    "stop". Settlement then fast-forwards the desktop from `6a2fe6ae3` to `5d1623b88` and Succeeds with the Review handoff.
  - **(b) No Reply.** Review the pushed `5d1623b88` in a Worktree Review with `-StartRef`, then
    `delegate.ps1 -Land a1f403eb -ExpectedSourceSha 5d1623b88f708774194c02e6a5fb38986117825b
    -ReviewEvidenceId <clean review> -RecoverReviewedSource`. Recovery accepts a Blocked owner. Expect
    the land's desktop worktree cleanup to leave residue while the registration is locked. Repair
    first to avoid that.

## 6. Recommended fix (CARD-1448, Code, about 45-60 min)

Fix under the existing card CARD-1448. Do not file a new one.

- **D-1:** `DelegationWorktreeService` must never adopt an entry whose schema-2 metadata has
  `CreationComplete == false`. This covers both the pre-create `ListAsync` adopt and the `ConflictException` fallback.
  Either expose `CreationComplete` on `WorktreeInfo`, or add `WorktreeManager.ListAdoptableAsync`.
- **D-2:** in `WorktreeManager.CreateAsync`, under the creation file lock and the repository lease, before
  the path and branch checks, recover an interrupted creation. Every one of these must hold:
  - the saved intent is schema 2, `CreationComplete=false`, with a `CreationId`;
  - exactly one registration exists at the path and it is `locked` with reason `initializing`;
  - the branch tip equals `InitialSha`.

  Then run `worktree remove --force --force <path>`, `worktree prune`, and
  `update-ref -d refs/heads/<branch> <InitialSha>` (old-value checked), delete the metadata, and
  fall through to a fresh add. Any mismatch, such as a different lock reason or a moved branch, is a
  ConflictException that removes nothing.
- **D-3:** `RollbackFailedAddAsync` logs a Warning when it refuses because of `Locked`. Today it is silent.

### Checkpoints

The checkpoints are closed, Linux-runnable git integration rows. Fixtures simulate an interrupted add with a real
`worktree add`: delete `src/`, write `<admin>/locked` = `initializing`, and save intent metadata with
`CreationComplete=false`.

| CP | Filter | Red before fix |
|---|---|---|
| CP-1 | `/*/*/WorktreeManagerGitIntegrationTests/C1448_*` (new: interrupted creation is recreated full, unlocked, `CreationComplete=true`, same branch at base; locked with another reason, or branch moved, removes nothing) | first case red (adopt or "path already exists") |
| CP-2 | `/*/*/DelegationWorktreeTests/C1448_*` (new: incomplete metadata is not adopted; a complete one still is) | red |
| CP-3 | `/*/*/(WorktreeManager*)\|(DelegationWorktreeTests*)/*` (all `WorktreeManager*` classes and `DelegationWorktreeTests`; see the TUnit OR grammar note) | regression |

**rc:** the same code is on rc `0714a9a89`, and the files are identical, so the picks will be clean. The
fix is low urgency: about 5 hits in 4 weeks, no data loss, and the branch stays landable through
recovery. Port it with the next rc bundle rather than as a hotfix.

## Side note

`observedScope` on a1f403eb lists 20 master-tracked `.antiphon/c1008-*` and `c959-*` files that its branch
never touched. This is consistent with a scope read taken from the partial desktop checkout. It was not
investigated further.
