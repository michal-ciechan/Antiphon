# CARD-1105 recycle audit: full replay on the server2 OLD runner volume (2026-10-08)

Task 96b558c0 (Debug, read-only). The real `redeploy-old` of master `beeaa1902b92bd9a9bf63aa4ad590a23a088bec9`
stopped at the first refusal:
`DIAGNOSIS=RecycleWorktreeDirty audit check=status status=0 repo=worktrees/task-0eafbbee`.
This replay runs the same audit over every entry on the OLD volume without stopping, so the full set of
refusals is known.

## Method (read-only)

- Volume `antiphon-runner_work` (project `antiphon-runner`, the OLD runner, not `antiphon-runner-temp`),
  mounted `type=volume,...,target=/work,readonly` in throwaway `docker run --rm` containers. A write probe
  failed (`Read-only file system`).
- Image: `antiphon-server2/session-testing:4358939ecd85`
  (`sha256:743279186cff…`), the image the running old runner uses. Same git (2.47.3). `--user 1654:1654`.
- Program: the `c1008_git_program` audit body taken verbatim from `scripts/c590-remote.sh` at `beeaa1902`
  (same env: `GIT_OPTIONAL_LOCKS=0 GIT_NO_LAZY_FETCH=1 GIT_CONFIG_SYSTEM=/dev/null GIT_CONFIG_GLOBAL=/dev/null`,
  `core.commitGraph=false`, the same checks in the same order and the same 8-way parallel content checks).
  The only changes were reporting changes: a refusal is recorded and the run moves on (per worktree, per
  Git-directory entry, per linked admin directory, per symlink); per-tip provenance went to a side file; and
  a refused worktree got a names-only diagnostic (status codes and paths, never file contents).
- Follow-up attribution passes, all on the same `:ro` mount: per-tip unpublished commits; and, on the desktop
  clone after `git fetch`, whether each commit's patch-id is already on `origin/master` (patch-ids for
  master commits since 2026-08-15). Owning tasks came from read-only `GET /api/agent-tasks/<id>` calls and
  `card.ps1 get`.
- No git command changed any state (no fetch/gc/prune/checkout/reset/clean/stash on the volume), nothing
  was stopped or removed, and no deploy phase ran.
- The full replay took 23.4 min (12:33:57Z to 12:57:19Z, host load average about 17). The content checks
  alone took about 20 min.

## Aggregate

| Item | Count |
|---|---|
| Repository entries (`.git` plus Git-shaped `HEAD`) | 390 (2 main checkouts: `repos/antiphon`, `repos/markdown-package`; 388 checkouts, including nested `worktrees/task-b288ec96/.antiphon/c1005-baseline`) |
| Entries marked partial (promisor) | 390 |
| Entry-level refusals (layout, locks, shallow, dot-git) | 0 |
| Content audit (`consider_dirty`) PASS | 389 |
| Content audit REFUSE | 1: `RecycleWorktreeDirty audit check=status status=0 repo=worktrees/task-0eafbbee` |
| Common pass `repos/antiphon/.git` | 3 × `RecycleGitAuditUnknown audit check=worktree-confine`, then 1 × `RecycleUnpublishedWork audit check=rev-list status=0 repo=repos/antiphon` (728 commits, 428 distinct tips) |
| Common pass `repos/markdown-package/.git` | PASS (12 commit tips, 18 comparisons, 0 unpublished) |
| Symlink refusals | 0 |
| Final line | `repositories=390 partial=390` |

So a recycle needs three separate decisions. Fixing the dirty worktree alone would only move the
stop to `worktree-confine`, and after that to `RecycleUnpublishedWork`.

## 1. RecycleWorktreeDirty: `worktrees/task-0eafbbee` (LEGITIMATE, but superseded)

- Task `0eafbbee` "CARD-0835 checkpoint receipt dirty tree", Code/Codex, **Failed** (2026-10-01T05:38Z),
  runner server2. Card **CARD-0835 is Done**.
- Checkout: branch `feat/card-task-0eafbbee`, HEAD `8331a9cf1`, which is already an ancestor of
  `origin/master`. The index is present and `core.autocrlf`/`core.eol` are unset. No index-flag entries.
- 30 status entries: 23 ` M` and 7 `??`, under `tools/Antiphon.Checkpoints/` (13), `server/Application/` (8),
  `server/Migrations/` (3), `scripts/` (3), `scripts/lib/`, `server/Domain/`, `tests/Antiphon.Tests/`. Examples:
  ` M scripts/run-checkpoint.ps1`, ` M tools/Antiphon.Checkpoints/Report/ReportWriter.cs`,
  ` M server/Application/Services/StageOutcomeService.cs`, `?? scripts/validate-checkpoint-receipt.ps1`,
  `?? scripts/lib/checkpoint-source.ps1`.
- Classification: **LEGITIMATE**. This is real uncommitted source, not build output or line-ending noise.
  It is the first attempt's working state. The same card's work was later committed to origin
  `feat/card-task-0eafbbee` at `1d994aac9` (2026-10-01T16:33Z), which touches the same files (including both
  untracked scripts). It then landed on master (`4cdd8809b`, `7ada82cb9`, `17cf31446`, `aedf87bb7`), and
  both untracked scripts exist on `origin/master`. The diff was not compared byte for byte, so whether
  every uncommitted hunk is superseded is very likely but not proven.

## 2. RecycleGitAuditUnknown `worktree-confine` (FALSE REFUSAL: checkouts live in the sibling tmp volume)

| Admin dir (`repos/antiphon/.git/worktrees/…`) | Recorded checkout | HEAD | Status of checkout |
|---|---|---|---|
| `c1005-master-261500e2` | `/tmp/c1005-master-261500e2` | `261500e2f` (detached) | clean (0 entries) |
| `c1005-rebase-rX0auR` | `/tmp/c1005-rebase-rX0auR` | `6018e6aae` (detached, 18 commits not on origin, all patch-equivalent on master) | clean |
| `c983-base-608ed47c` | `/tmp/c983-base-608ed47c` | `15a2601b8` (detached) | clean |

These are CARD-1005 and CARD-0983 verification worktrees created under the runner's `/tmp`. In the runner,
`/tmp` is the `antiphon-runner_runner-tmp` volume, not `/work`. All three checkouts still exist there
(66 entries each, with `.git` files pointing back at the admin dirs), and status run read-only with both
volumes mounted is clean. The audit helper mounts only `/work`, so it cannot see them and refuses.
Nothing is uncommitted, and their HEADs hold no work that is missing from master.

**Fix shape:** have `c1008_audit` also mount `${project}_runner-tmp` read-only at `/tmp`, matching the
runner's own mount table (`c1008_owned_mounts` already proves it). Then `worktree-confine` accepts a
recorded checkout under `/tmp/` (resolved inside that mount) and inspects it with `orphan-status` or
`worktree-status` like any other. A recorded `/tmp` checkout that is absent from the tmp volume keeps the
current stale-index rule. The helper's own scratch must then move off `/tmp` (for example `mktemp -d` under a
tmpfs at `/audit-scratch`), because `/tmp` would be read-only. Separately, the CARD-1005/0983 verification
harness should `git worktree remove` its `/tmp` checkouts when it finishes.

## 3. RecycleUnpublishedWork in `repos/antiphon` (728 commits, 428 tips)

`ls-remote` advertises 1473 heads. 1180 are present locally as commits and **293 are missing locally**
(the drained clone stopped fetching). Tip sources: 142 tips are named by a current ref or worktree HEAD, and
286 only by reflog entries (80 `logs/refs/heads/…`, 39 `logs/refs/remotes/…`, 2 `logs/HEAD` files).

All 196 owning tasks are **terminal** (154 Succeeded, 22 Canceled, 20 Failed). None is Working or Queued.

Per-commit content classification (patch-id against `origin/master`):

| Class | Commits |
|---|---|
| Patch already on master (rebased land) | 651 |
| Empty commit (tree equals parent; message only) | 45 |
| Merge | 1 |
| Patch NOT on master | 31 (1 also present on the desktop clone, 30 exist only on this volume) |

Classes:

### 3a. FALSE REFUSAL: stale `origin/master` and `origin/HEAD` (5 commits)
`refs/remotes/origin/master` and `refs/remotes/origin/HEAD` = `af4d4c386e` (2026-10-05). Origin still
advertises `master`, but at a commit this clone never fetched, so the audit has no comparison for it. On a
freshly fetched desktop, `af4d4c386e` is reachable from origin's current heads. It is published. The same
applies to any tip that an advertised but locally absent head reaches.
**Fix shape:** prove publication against origin's real heads without writing to the volume. Create a scratch
bare repo in the helper's scratch with `objects/info/alternates` pointing at the audited repo's object store,
then `git fetch --filter=blob:none --no-tags origin '+refs/heads/*:refs/c1008/origin/*'` into the scratch
repo only. Use the fetched heads as comparisons. Keep "present only" as the fallback when the fetch fails,
which can only refuse more.

### 3b. Branches deleted on origin after land or terminal settle, content already on master (289 current refs)
- 162 `refs/remotes/origin/feat/card-task-*` remote-tracking refs and 126 local `refs/heads/feat/card-task-*`
  branches whose name origin no longer advertises (the land deletes the branch), plus
  `refs/heads/feat/card-task-3895bec1`. That branch's same-name origin branch diverged by 1 commit whose
  patch is on master (task Succeeded, CARD-0804, Landed). 123 of the local branches have a same-oid
  remote-tracking twin, which means they were pushed.
- Examples: `refs/heads/feat/card-task-fcb2e819` (Investigate, CARD-0772, Succeeded),
  `refs/heads/feat/card-task-08084c5f` (Plan, CARD-0738, Succeeded/Landed),
  `refs/remotes/origin/feat/card-task-365d4862` (Investigate, CARD-0464, Succeeded). Each holds 1 commit
  whose patch is on master.
- Task status across all 129 unpublished local branches (3b and 3c together): 57 Succeeded with no land record, 42 Succeeded/Landed,
  2 Succeeded/Unconfirmed, 13 Failed, 15 Canceled.
- Classification: by the audit's contract (graph reachability) these commits **are** unpublished,
  because land rebases and origin deleted the branches. By content, none of these refs holds a patch
  that is missing from master. Whether this counts as a false refusal is a **policy decision**:
  - (i) Treat a `refs/remotes/origin/X` tip whose name origin no longer advertises as upstream-deleted, not
    work (the state `git fetch --prune` would reach).
  - (ii) Accept a tip whose every unpublished commit is patch-id equivalent to a commit on origin `master` (or
    is empty). This needs blobs on both sides. The partial clone plus `GIT_NO_LAZY_FETCH` can prevent that,
    in which case fetch blobs into the scratch repo from 3a.
  - (iii) Keep refusing, and rescue-push or delete the refs first.

### 3c. Patch not on master: 31 commits (LEGITIMATE unpublished; a human must decide)
Almost all are **reflog-only** old tips: pre-amend or pre-rebase states of task branches. The one
current-ref case is below. Owning tasks (all terminal):

| Task | Status / role | Card | Unique commits | Where |
|---|---|---|---|---|
| `fa0190b4` | Canceled Code | CARD-0688 (Done) | 1 (`70076885`, land services; also in the desktop clone's object store, unreachable there) | **current refs** `refs/heads/feat/card-task-fa0190b4`, `…-fa0190b4-first-verified` |
| `3b559ad9` | Succeeded Code | none | 5 | reflog `logs/refs/heads/feat/card-task-3b559ad9`, `logs/refs/remotes/origin/feat/card-task-3b559ad9` |
| `1300c9b8` | Canceled Code | CARD-0665 | 3 | reflog |
| `303a8c1f` | Succeeded Code, Landed | CARD-0657 | 2 | reflog |
| `3d00cfc8`, `5f6b3388` | Succeeded Code | CARD-0807 | 2 + 2 | reflog |
| `7a978aa8` | Succeeded Review | CARD-0665 | 2 | reflog |
| `8a662a98` | Failed Code | CARD-0688 | 2 | reflog `…-8a662a98-integrated` |
| `170e6f0d` | Failed Code, Landed | CARD-0519 | 1 | reflog plus `worktrees/task-170e6f0d/logs/HEAD` |
| `16178635`, `2a293dd6`, `47f087f7`, `6b457b41`, `7ebf8aa2`, `84de0093`, `a8685a72`, `bb485c68`, `d806b77b`, `e09045ba`, `e839b406` | terminal | CARD-0584/0407/0452/0665/0589/0701/0664/0767/0657 | 1 each | reflog |

Caveat: a land rebase that resolved conflicts changes the patch-id, so some of these may already be on master
under a different patch. They were not diffed by hand.

## Next stage

`next: decide`. The legitimately dirty or unpublished items need a human decision, or a delegated
rescue-push-then-clean. They must never be cleaned automatically:

1. `worktrees/task-0eafbbee`: 30 uncommitted entries from the Failed first attempt at CARD-0835. The card is Done
   and the same files landed later. Options: rescue-push to `rescue/task-0eafbbee` from inside the old runner,
   or approve the loss.
2. The 31 patch-not-on-master commits (3c), above all the live `feat/card-task-fa0190b4` refs. Options: one
   rescue bundle or push of the listed tips, or approve the loss.
3. A policy for 3b (289 refs whose content is already on master): (i), (ii) or (iii).

Code follow-ups for the false refusals: mount `runner-tmp` read-only in the audit helper (section 2), and
add the scratch-repo fetch for publication proof (3a).

Timing risk: the content checks alone took about 20 min of the 1800 s budget on a loaded host. With every
gate passing, the real audit would come close to `audit-timeout`.
