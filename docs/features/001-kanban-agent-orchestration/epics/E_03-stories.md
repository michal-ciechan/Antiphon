# E03 — Worktree manager + safety invariants

> **Status:** `[x]` **Closed 2026-05-15.**
> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** create / list / remove git worktrees with hard safety rules; auto-prune stale.

**Covers:** FR-04, FR-08, FR-20, NFR-03, NFR-04

---

## Stories

- **E03-S01** `[x]` `IWorktreeManager` interface + `WorktreeManager` impl (shell-out to `git worktree`).
  - Work items:
    - `CreateAsync(repoPath, cardId, baseRef)` → returns `WorktreeInfo { path, branch }`. *TDD:* test `WorktreeManager_create_produces_worktree_under_root` against tmp git repo.
    - `RemoveAsync(repoPath, path)`. *TDD:* test `WorktreeManager_remove_deletes_worktree_and_branch`.
    - `ListAsync(repoPath)`. *TDD:* test `WorktreeManager_list_returns_only_worktrees_under_root`.
- **E03-S02** `[x]` Safety invariants: path under root; sanitised branch name; reject path traversal.
  - Work items:
    - Sanitiser. *TDD:* test `Sanitise_rejects_path_traversal_and_special_chars` (`../`, `;`, `\0`, etc.).
    - Root-confinement check. *TDD:* test `Worktree_path_confinement_rejects_resolved_escape`.
- **E03-S03** `[x]` Stale worktree auto-prune background service.
  - Work items:
    - `WorktreeJanitorHostedService` runs daily, removes worktrees whose sidecar `LastTouchedAt` is older than `Git:WorktreeStaleAfterDays`. *TDD:* test `WorktreeJanitor_prunes_stale_worktrees` with fake clock.

---

## Notes

- E03 is filesystem/git-only because the persisted `Worktree` domain entity lands in E04. Until then, stale pruning uses sidecar metadata under `Git:WorktreeBasePath/.antiphon/worktrees/`.
- Worktree branches use the PR-facing `feat/card-{cardId}` convention; worktree directories use `card-{cardId}`.

---

## Acceptance

- ✅ `dotnet build server/Antiphon.Server.csproj -p:UseAppHost=false -p:OutputPath=...` green.
- ✅ `dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter "/*/*/*/*[Category=GitIntegration]"` — 6 pass.
- ✅ `dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter "/*/*/*/*[Category=Unit]"` — 22 pass.
- ✅ `dotnet run --project tests/Antiphon.Tests --no-build` — 144 pass, 1 skipped.
