# Card → repo task file sync

**Status:** planned, not started
**Date:** 2026-08-09
**Tracking:** [TODO.md](../../../TODO.md) → "Card → repo task file sync"

Sync board Cards into the git repository as human-readable task files that stay up to date and get
checked in, so outstanding work has a durable in-repo home: visible in the file tree, visible in
code review, and travelling with the branch.

## Two facts that shape the whole design

Both were found by reading the code, and both rule out the obvious approach.

**1. There is no card-edit API at all.** `CardEndpoints.cs:13-55` exposes only move/spawn/diff/
comments/pr; creation lives in `BoardEndpoints.cs:47-55`. The client agrees
(`client/src/api/boards.ts:265-328`). Title and description are effectively write-once today. So
file→card sync is not "harder", it is *blocked* on first building card editing, validation, and
rules for what happens when a file edit sets `status: done` and bypasses `CardStateMachine`
(`server/Domain/Enums/CardStatus.cs:3-11`, `CardService.cs:218-251`).

**2. Cards mutate in six services, and there is no in-proc event bus.** Mutations happen in
`CardService.cs:75,113,190`, `AgentSessionLaunchQueue.cs:251-309`, `OrchestratorService.cs:373-449`,
`AgentService.cs:389-538`, `AgentSessionService.cs:162,446,530` and `AgentSessionRuntime.cs:991`.
They all announce via `IEventBus.PublishToAllAsync("CardChanged", …)`, but `IEventBus` is
SignalR-**outbound only** (`server/Application/Interfaces/IEventBus.cs:7-18`) — nothing in-process
can subscribe. A per-mutation hook therefore means touching ~10 call sites; a reconcile loop needs
none.

## Decisions

### Direction: card → file, one-way. The database is the source of truth.

A card is one row, but its file exists at a different content on every branch: cards spawn worktrees
on their own branches and delegation worktrees rebase back
(`DelegationWorktreeService.cs:229-251`). "The file changed" is ill-posed — which branch's copy
wins? Bidirectional sync is unsound in this architecture, not merely awkward.

The `WORKFLOW.md` precedent does **not** transfer. That is one config file per board where
last-writer-wins with version history is acceptable (`WorkflowFileStore.cs:15-20`,
`WorkflowDefinitionLoader.cs:285-327`). Task status is orchestrator-owned state.

Escape hatch for later, deliberately not v1: an explicit `POST /api/boards/{id}/tasks/import` that
diffs files against the DB and applies title/description only. Explicit, never a watcher.

### Location and format

`<repo>/.antiphon/tasks/<board-slug>/` — a sibling of the existing `.antiphon/boards/` convention
but human-named, because visibility is the entire point. One file per card, plus a generated
`INDEX.md` grouping cards by status for at-a-glance review.

Markdown with YAML frontmatter (matching WORKFLOW.md and the specs in this directory): `id` (the
canonical key), `identifier`, `title`, `status`, `priority`, `labels`, `agent`, `created`,
`completed`. Body is `Description` verbatim. Everything renders from `CardDto` — no new columns.

Filename `CARD-0007-add-sibling-agent-skill.md`. **Matching is always by frontmatter `id`, never by
filename.** The reconciler owns the directory: a title change is delete-old + write-new in one
commit, which git shows as a rename. No slug column, no `git mv` bookkeeping. This is only safe
because the sync is one-way — regeneration cannot destroy a human edit that was never allowed.

### Target repo: the project repo only, never agent worktrees

`Project.LocalRepositoryPath` (`Project.cs:12`), matching `WorkflowFileStore.cs:12-20`. Writing into
worktrees would dirty agent workspaces, pollute the `AgentFilesService` union listing
(`AgentFilesService.cs:101-124`), and let the delegation merge-back's commit-all
(`DelegationWorktreeService.cs:209`) sweep task files onto task branches. Single-writer means
worktree branches never carry divergent copies, so rebases stay conflict-free, and worktrees branched
after a sync commit inherit the files anyway — "travels with the branch" for free.

Project with no `LocalRepositoryPath`: silently skip, as `GetWorkflowFilePath` does
(`WorkflowFileStore.cs:12-13`), and surface `taskFilesPath: null` so the UI can explain why.

### When it fires

A debounced reconciler with three triggers feeding one channel, copying
`WorkflowFileWatcherHostedService.cs:16,39-48`:

1. Enqueue from `CardService` at its three mutation points — immediate feel for user-driven changes.
2. A ~60s tick — the only practical way to catch the other five mutating services given fact 2.
3. `POST /api/boards/{id}/tasks/sync` for "sync now".

Idempotence is the defence against surprise dirty state: render desired content, hash-compare, write
only real diffs. A tick with no card changes touches nothing, so the working tree and the
files-review baseline see zero churn.

### Committing: yes, path-scoped, never push

`git commit -m "antiphon: sync task files" -- .antiphon/tasks` — a pathspec commit leaves the user's
staged index for other files untouched. Include the `antiphon: true` trailer so
`ChangeDetectionService.cs:35` classifies these correctly.

Checked before committing; on any of these, still write the files but skip the commit and retry next
tick, logging once: rebase in progress (`.git/rebase-merge`, `rebase-apply`), `MERGE_HEAD` or
`CHERRY_PICK_HEAD`, detached HEAD, or conflicted task paths.

No push code in the feature at all. Config: `CardFileSync:Enabled` and `CardFileSync:AutoCommit`,
both defaulting true when a repo path is set. Debounce commits ~30s so a burst of card moves lands
as one commit.

## Testing

TUnit, `[Category("Integration")]`, real temp repos via `tests/Antiphon.Tests/TestHelpers/
ScratchGitRepo.cs` as `DelegationWorktreeTests` and `GitIgnorePreviewTests` do. DB assertions scoped
to the test's own rows (see the shared-Postgres rule in CLAUDE.md). Cases:

- Render: frontmatter + body round-trip; index groups by status.
- Idempotence: a second sync writes nothing and commits nothing (assert HEAD unchanged).
- Rename: title change leaves one file, one commit.
- Terminal cards: file kept with `status: done`, not deleted — the durable record is the point.
- Dirty tree elsewhere: an unrelated staged file stays staged and uncommitted.
- Mid-rebase: files written, commit skipped, committed on the next pass.
- Project with no repo: no-op, no throw.
- Two cards sharing an `Identifier`: two distinct files.

## Risks

- **`NextIdentifierAsync` is count-based** (`CardService.cs:253-257`), so deleting a card lets the
  next one reuse an identifier. Matching by `id` survives it (collisions get a filename suffix), but
  the generator should be fixed to max+1 separately. Tracked in TODO.md as its own item.
- Sync commits will appear in "changes since checkpoint" for agents whose workspace *is* the project
  repo (`AgentFilesService.cs:91-95`). Acceptable in v1; if noisy, filter `.antiphon/tasks/` out of
  the listing.
- Multi-board projects need per-board subdirectories or `INDEX.md` collides.
- CRLF/LF must be pinned (write LF) or hash-compare false-positives on Windows every pass.
- The existing `.antiphon/boards/` uses GUID directory names. Deviating to slugs here is deliberate
  — GUID paths are hostile to the humans this feature exists for.

## First slice

`CardTaskFileService` (render + reconcile + path-scoped commit with guards),
`CardTaskFileSyncHostedService` (tick + channel), the manual sync endpoint, and the integration
tests above. One-way, project repo only, no UI, no import, no `AgentFilesService` filter.

That alone delivers the motivating case: every card durably visible as
`.antiphon/tasks/<board>/CARD-XXXX-*.md`, committed, and travelling with the branch.

---

*Plan generated by a Fable planning agent against this repo on 2026-08-09; file/line citations were
produced by that pass and should be re-checked if the code has moved before implementation starts.*
