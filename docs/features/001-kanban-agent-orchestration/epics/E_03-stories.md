# E03 — Worktree manager + safety invariants

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** create / list / remove git worktrees with hard safety rules; auto-prune stale.

**Covers:** FR-04, FR-08, FR-20, NFR-03, NFR-04

---

## Stories

- **E03-S01** `[ ]` `IWorktreeManager` interface + `WorktreeManager` impl (shell-out to `git worktree`).
  - Work items:
    - `CreateAsync(cardId, baseRef)` → returns `Worktree { path, branch }`. *TDD:* test `WorktreeManager_create_produces_worktree_under_root` against tmp git repo.
    - `RemoveAsync(path)`. *TDD:* test `WorktreeManager_remove_deletes_worktree_and_branch`.
    - `ListAsync()`. *TDD:* test `WorktreeManager_list_returns_only_worktrees_for_repo`.
- **E03-S02** `[ ]` Safety invariants: path under root; sanitised branch name; reject path traversal.
  - Work items:
    - Sanitiser. *TDD:* test `Sanitise_rejects_path_traversal_and_special_chars` (`../`, `;`, `\0`, etc.).
    - Root-confinement check. *TDD:* test `WorktreeManager_create_throws_when_resolved_path_escapes_root`.
- **E03-S03** `[ ]` Stale worktree auto-prune background service.
  - Work items:
    - `WorktreeJanitorHostedService` runs daily, removes worktrees with `Status = Stale && lastTouched > N days`. *TDD:* test `WorktreeJanitor_prunes_stale_worktrees` with fake clock.
