# CARD-0004 — Card → repo card files: one-way, project repo only, committed by pathspec

**Date:** 2026-09-02 (Plan pass, task 9365320f — design only; no production code changed)
**Card:** CARD-0004 "Card -> repo task file sync" (`86b6542a-5f1e-4107-b04b-46d81c636225`, Backlog, p2)
**Supersedes:** `docs/superpowers/specs/2026-08-09-card-task-file-sync.md` (2026-08-09). Its three
core decisions — card → file **one-way**, the **project repo only** (never worktrees), and a
**reconcile loop** with a **path-scoped commit and no push** — all still hold and are carried
forward. Its location, its two load-bearing "facts", its frontmatter, one test case and one risk are
stale against today's code and are replaced here; the errata block at the top of that file lists
exactly what changed.

**Sources (verified this pass, 2026-09-02, master `f2ee3580`):** the card; the 2026-08-09 spec;
`CardEndpoints.cs`, `BoardDtos.cs` (`CardDto`), `Card.cs`, `Board.cs`, `Project.cs`,
`CardRevision.cs`, `CardStatus.cs`, `CardStateMachine.cs`, `IEventBus.cs`, `CardService.cs`
(`GetSummaryAsync`, `ApplyColumnMove`, `ArchiveAsync`, `NextIdentifierAsync`),
`CardIdentifierAllocator.cs`, `CardLifecycleTransitions.cs`, `CardWorkTransitionHostedService.cs`,
`CardWorkTransitionSettings.cs`, `WorkflowFileStore.cs`, `WorkflowFileWatcherHostedService.cs`,
`GitService.cs` (`BuildCommitArgs`, `CommitAllChangesAsync`), `GitWorkspaceService.cs`,
`GitProcessGate.cs`, `GitSettings.cs`, `WorktreeManager.cs` (`RunGitAsync`),
`DelegationWorktreeService.cs`, `DelegateCheckProbe.cs`, `OrchestratorInvestigationDetector.cs`,
`ChangeDetectionService.cs`, `CardThreadService.cs`, `TrackerSyncEndpoints.cs`, `TrackerSyncDtos.cs`,
`Program.cs`, `appsettings.json`, `.gitignore`, `.gitattributes`, `antiphon.areas.json`,
`ScratchGitRepo.cs`, `TestDbFixture.cs`, `AntiphonWebAppFactory.cs`, `CardThreadServiceTests.cs`,
`DelegationWorktreeTests.cs`, `docs/agent-card-lifecycle.md`, `docs/orchestration-loop.md`,
`docs/project-context.md`, `docs/testing-and-build.md`, `docs/workflow-tracker-block.md`,
`docs/antiphon-api.md`, `docs/ops-http.md`, `TODO.md`, the CARD-0005 / CARD-0019 / CARD-0166 /
CARD-0002 / CARD-0316 specs and plans, `git log --since=2026-08-09` over the card model (39
commits), and the live server at 17202 (`/api/boards`, `/api/projects`, `/api/cards?boardId=`).

---

## Decision

1. **One-way, card → file. The database stays the source of truth.** The spec's reason (a card is
   one row but its file has a different content on every branch, so "the file changed" has no
   well-defined winner) still holds. Its *other* reason — "there is no card-edit API, so file→card
   is blocked" — is gone (CARD-0019 shipped `PATCH /api/cards/{id}/content`, archive, unarchive,
   reopen and a revision log), and a stronger one has replaced it: every card write now carries a
   **reason and an actor** on an append-only `CardRevision` row, and a file edit has nowhere to put
   either. Import stays an explicit, later verb; there is no watcher in this design.

2. **Location: `docs/cards/<board-slug>/`, not `.antiphon/tasks/`.** The spec's directory is
   **gitignored**: `.gitignore:48` (`e1dc5443`, 2026-08-11, two days after the spec) ignores all of
   `.antiphon/` as regenerable agent scratch, and `docs/orchestration-loop.md:383` now documents
   it as such. A path-scoped commit there commits nothing. `docs/` is where humans look, is
   already excluded from "produced source" by `OrchestratorInvestigationDetector` (`:36`), and is
   the one area `antiphon.areas.json` weights `allow` so touching it never serialises a dispatch.

3. **Vocabulary: "cards" on disk and on the wire; the card's class names kept.** Since the spec,
   *task* in this repo means `AgentTask` — `/api/agent-tasks`, `.antiphon/task-<id>.md`, the Home
   "Tasks" rail (CARD-0002). A `tasks/` directory would read as delegations. The directory is
   `docs/cards/`, the route is `/card-files/sync`, the settings section is `CardFileSync`; the
   classes are `CardTaskFileService` and `CardTaskFileSyncHostedService` exactly as the card
   names them, plus a pure `CardTaskFileRenderer` so rendering is testable without a database.

4. **Two triggers in v1: a 60 s tick and the manual endpoint. No enqueue from `CardService`.**
   The spec's third trigger (an in-request enqueue for "immediate feel") is dropped: the
   consumer is a human reading files or `git log`, who cannot perceive a 60 s lag; it would
   touch seven publish sites in `CardService`; and with tick-only triggering a burst of moves
   already lands as one commit, so the spec's separate 30 s commit debounce is unnecessary too.
   `IEventBus` is still SignalR-outbound only (`IEventBus.cs:7-18`), so a sweep remains the only
   way to see the other seven services that mutate cards — same reasoning as CARD-0040's sweep.

5. **The reconciler owns the directory. Idempotence is judged by git, not by bytes.** Desired
   content is rendered LF-only; a file is rewritten only when its on-disk content, CRLF→LF
   normalised, differs; any `*.md` in the board directory that is not in the desired set is
   deleted (a title change is delete-old + write-new, which git reports as a rename). The
   commit gate is `git status --porcelain -- <dir>` being non-empty, never the write count —
   this machine runs `core.autocrlf=true` with `* text=auto`, so a checkout puts CRLF on disk
   and a byte-compare would rewrite every file every pass and still find nothing to commit.

6. **Commit: path-scoped, trailered, guarded, never pushed, never cites a card.**
   `git add -A -- <dir>` then `git commit --only -m "antiphon: sync card files (<board>)"
   --trailer antiphon=true -- <dir>`; an unrelated staged file stays staged. `antiphon: true` is
   the trailer `ChangeDetectionService.cs:35` and `GitService.BuildCommitArgs` (`:313`) already
   use. The message **never names a card identifier**: `CardThreadService` correlates commits by
   `git log --grep <identifier>` (`:115`) and would list every sync commit on every card's thread.
   Guards (write files, skip commit, retry next tick, log once per reason change): rebase in
   progress, `MERGE_HEAD`, `CHERRY_PICK_HEAD`, detached HEAD, conflicted paths under the
   directory, any git failure (an `index.lock` held by a delegate is the expected one).

7. **Archived cards keep their file; archived boards and projects are skipped.** Archive is what
   "delete" means here (`Card.cs` remarks, CARD-0019) precisely so citations never dangle; a file
   that vanished on archive would undo that. The renderer reads the DbContext directly (there is no
   global query filter — `CardIdentifierAllocator` remarks), so archived rows are included with
   `archived:` frontmatter and their own INDEX group. An archived board's directory is left alone.

8. **Frontmatter carries the record, not the runtime.** Fields that change on an agent claim, a
   queue shuffle or a workflow tick would churn a commit for an invisible change, so
   `OwnerSessionId`, `CurrentWorktreeId`, `AssignedAgent*`, `AgentQueuePosition`,
   `ActiveWorkflowRun*`, `ConcurrencyToken`, `UpdatedAt`, `AutoDispatchHeldAt`,
   `DecisionNotifiedAt`, `RevisionCount` and sessions are **excluded**. `ExternalIssueRef` is
   **included**: the Antiphon board is `TrackerKind.GitHubIssues` and 24 live cards carry one.

9. **`dryRun` on the endpoint.** The first real run on this repo writes 315 files in one commit on
   master in the shared checkout. A dry run renders, compares and reports without touching disk
   or git. Cheap, and the only safe way to look before that first commit.

---

## Ground truth (checked, not guessed)

Each spec claim, against the code on 2026-09-02.

### "There is no card-edit API at all" — false since 2026-08-14

`CardEndpoints.cs` now maps `PATCH /{id}/content` (`42f3c2db`), `POST /{id}/archive` and
`/unarchive` (`d2e88a21`), `POST /{id}/reopen` (CARD-0054, `61cc78af`), `GET /{id}/revisions`,
`GET|POST /{id}/discussion` (CARD-0166, `d46758c7`), `GET /{id}/thread` (CARD-0035) and a list
`GET /api/cards?updatedSince|status|boardId` (`b00eb3ba`). `scripts/card.ps1 edit` fronts the
content route. The one-way decision therefore rests on the branch-ambiguity argument and on
CARD-0019's append-only record (`CardRevision.cs`: "the card SURFACE is correctable; the card
RECORD is append-only … with a reason"), not on a missing API.

### "Cards mutate in six services, no in-proc bus" — still true in kind, stale in count

`"CardChanged"` is published from seven files: `AgentService.cs` (5 sites), `AgentSessionLaunchQueue.cs`,
`AgentSessionRuntime.cs`, `AgentSessionService.cs` (3), `CardLifecycleTransitions.cs`, `CardService.cs`
(7), `OrchestratorService.cs`. `CardWorkTransitionService` (CARD-0040) and the tracker import
(CARD-0166/0175) also write cards. `IEventBus` has no subscribe surface. Every site sets
`Card.UpdatedAt` (17 assignments verified), so a watermark would work, but the reconciler compares
rendered content instead and needs no watermark.

### The location is gitignored — blocking

`.gitignore:48` `.antiphon/` (`e1dc5443`, 2026-08-11: "Agent scratch: delegation brief/report spill
files, regenerable from the task row"). `git check-ignore -v .antiphon/tasks/x.md` → ignored. The
spec's "sibling of `.antiphon/boards/`" no longer means "checked in like WORKFLOW.md": WORKFLOW.md
is not checked in either (`.antiphon/boards/<guid>/WORKFLOW.md` exists on disk here and is ignored).

### The card model grew (39 commits on the card files since 2026-08-09)

`CardStatus` gained `NeedsDecision` (`ef914706`); `Card` gained `ArchivedAt/Reason/By`,
`RevisionCount`, `AutoDispatchHeldAt`, `DecisionNotifiedAt`, `ExternalIssueRef`, `Comments`,
`Revisions`; `CardDto` gained `TerminalReason`, `AssignedAgentName`, `WorkflowRunStatus`,
`CurrentWorkflowStageName`, `ExternalIssue`, `HasMore`. `CardStateMachine` now allows any live
state to reach any other directly (2026-08-13). None of this changes the design; it changes the
rendered fields (§Format) and the INDEX groups.

### The identifier risk is closed; the duplicate-identifier test is impossible

`CardIdentifierAllocator` is parse-max+1 (CARD-0005, `ce48f504`), archived rows still count, and
`IX_Cards_BoardId_Identifier` (`AppDbContext.cs:909`) is unique per board. The spec's "two cards
sharing an `Identifier` → two files" case cannot be set up inside one board; it becomes "two boards
in one project each holding CARD-0001 → two directories".

### Commit and merge mechanics — held

Trailer `antiphon: true` (`ChangeDetectionService.cs:35`; `GitService.BuildCommitArgs` emits
`--trailer "antiphon=true"`). Delegation merge-back's `CommitAllChangesAsync` (`git add -A`) runs in
the **worktree** only (`DelegationWorktreeService.cs:330-331`), then rebase and fast-forward
(`:350-372`); a worktree never has sync files written into it and inherits committed ones clean.

### No existing git runner fits; the shape to copy is `WorktreeManager.RunGitAsync`

`GitService.RunGitAsync` takes its arguments as **one string** (`:319-323`) — a quoting hazard for
paths and messages. `GitWorkspaceService` is read-only by contract that `DelegateCheckProbe`
documents and relies on (`:26`, `:342-344`: "a git wrapper that only ever runs log/status"); adding
writes there would erode a safety property. `WorktreeManager.RunGitAsync` (`:819-870`) is private:
`ArgumentList`, a per-command budget, kill-tree on timeout. The new service gets a private runner of
that shape, taking a `GitProcessGate` lease the way `GitWorkspaceService.RunAsync` does and
`GitSettings.ExecutableName` / `TimeoutSeconds` from `IOptions<GitSettings>`.

### The CARD-0227 hazard is closed; sync files are never "produced source"

A commit on master in the shared checkout used to be mis-credited to a running Shared task.
`DelegateCheckProbe` now omits commit and working-tree evidence for Shared/ReadOnly checkouts
(`:147-149`, `SharedWorkspaceUnattributable`); a Worktree task's evidence is `mergeTarget..branch`
(`:141-144`), which a master-side commit never enters. `OrchestratorInvestigationDetector`
excludes `docs/` from source roots (`:34-40`).

### Patterns to copy

`CardWorkTransitionHostedService` (`Infrastructure/Orchestration`): `PeriodicTimer`, one scope per
tick, `Enabled` short-circuit, `Program.cs:165` settings bind and `:523-524` registration.
`CardWorkTransitionSettings` for the settings doc-comment style. `TrackerSyncEndpoints` for a
board-scoped "sync now" POST returning a result record and 409-ing on a concurrent run.
`WorkflowFileStore.GetWorkflowFilePath` for the null-skip on a missing `LocalRepositoryPath`.
`CardThreadServiceTests.Scenario` for own-rows seeding and disposal; `ScratchGitRepo` for a real repo.

### Live facts that shape defaults

Antiphon board: 314 live cards — Backlog 67, In Progress 2, Review 31, Done 214, Needs decision 0 —
plus archived rows; longest title 154 chars; labels include comma-joined single strings
(`"bug,grok,delegation"` is one label). Ten projects have a `LocalRepositoryPath`; three boards
have cards (Antiphon 314, Gym Stat 39, school-revision 9). `check-interpreter`'s path is under
`C:\logs`, `slack-test`'s path does not exist, and `az-care` / `codeperf` / `family` are
**subdirectories of the ClaudeBot repo** — the writer must tolerate non-repos and sub-directory
roots. `git 2.50.1.windows.1`, `core.autocrlf=true`, `.gitattributes` `* text=auto`.
`TODO.md` still exists as the "interim measure" whose header points at the spec.

---

## Format (normative)

### Paths

- Root: `<Project.LocalRepositoryPath>/docs/cards/`. Git runs with that repository path as the
  working directory and the **absolute** board directory as the pathspec, so a project rooted in a
  subdirectory of a larger repo (ClaudeBot's agents) commits into that repo correctly.
- Board directory: `docs/cards/<slug>/`, `slug` = board name lower-cased, every non-alphanumeric
  run → `-`, trimmed, max 60 chars (same rule as `AgentService.Slugify`). Board names are unique
  per project (`IX_Boards_ProjectId_Name`); if two names slugify identically the later-created
  board appends `-<Id:N8>`.
- Card file: `<IDENTIFIER>-<title-slug>.md` — identifier sanitised (`[^A-Za-z0-9-]` → `-`), title
  slug as above with max 60, or the identifier alone when the slug is empty. Unique per board
  because the identifier is.
- `INDEX.md` in each board directory.
- Nothing is written for a board with zero cards, an archived board, or an archived project.

### Card file

```markdown
---
id: 86b6542a-5f1e-4107-b04b-46d81c636225
identifier: CARD-0004
title: "Card -> repo task file sync"
status: Backlog
priority: 2
labels: ["feature", "cards"]
created: 2026-08-09T15:05:43Z
started: 2026-08-20T09:12:00Z          # omitted when null
completed: 2026-08-21T17:40:03Z        # omitted when null
external_tracker: GitHubIssues         # the three external_* keys omitted when no ExternalIssueRef
external_key: "#12"
external_url: "https://github.com/…/issues/12"
archived: 2026-08-25T08:00:00Z         # archived / archived_by / archived_reason omitted when live
archived_by: "operator"
archived_reason: "duplicate of CARD-0210"
---

# CARD-0004 — Card -> repo task file sync

<Description, verbatim, LF-normalised>

## Outcome                              # only when TerminalReason is set

<TerminalReason, verbatim>
```

- Every string scalar is double-quoted with `\` and `"` escaped (YAML double-quoted form) so titles
  containing `:`, `#` or quotes never break the block. Labels render as a flow sequence of quoted
  strings, verbatim from `LabelsJson` (comma-joined labels are one label; the file does not
  reinterpret them). No YAML library on the render path — the schema is fixed and a hand-rendered
  block is byte-deterministic (YamlDotNet is referenced for *parsing* elsewhere and stays there).
- Timestamps are UTC, second precision, `Z` suffix. `status` and `external_tracker` are the enum
  names. `priority` is the raw int.
- Body: an H1 of `<identifier> — <title>`, blank line, the description verbatim (CRLF→LF), then
  `## Outcome` only when `TerminalReason` is set. UTF-8 without BOM, LF line endings, exactly one
  trailing LF.

### INDEX.md

```markdown
# Antiphon — cards

Generated by Antiphon from the board on every sync. Do not edit files in this directory — edit the
card (`scripts/card.ps1`), and the next sync overwrites them. 314 cards, 3 archived.

## Needs decision (0)      # a group is omitted when empty
## In progress (2)
- [CARD-0316](CARD-0316-never-leave-terminationsource-unknown….md) — Never leave … `p2` `reliability`
## Review (31)
## Backlog (67)
## Done (214)
## Canceled (n)
## Archived (n)
```

Group order is fixed as above (attention-first, then the work order, then the closed states),
independent of the board's column order, so two boards render the same shape. Within a group:
priority descending, then identifier ascending. The card count line is the only free text.

---

## Reconcile and commit (normative)

Per board, under the repository's lock:

1. **Load** the board with its project; return `WriteSkipReason` `board_archived` /
   `project_archived` / `no_repository_path` / `not_a_git_repository` (`git rev-parse
   --is-inside-work-tree` ≠ `true`, or the path does not exist) / `no_cards` without touching disk.
   Load every card of the board (archived included, `ExternalIssueRef` included, `AsNoTracking`).
2. **Render** the desired set `{ filename → content }` (cards + `INDEX.md`).
3. **Compare** against `*.md` files directly in the board directory (no recursion): equal after
   CRLF→LF normalisation → `Unchanged`; else → `Written`. Existing `*.md` not in the desired set →
   `Deleted`. `dryRun` stops here and reports the counts it *would* have produced.
4. **Commit** unless `CardFileSync:AutoCommit` is false (`CommitSkipReason` `autocommit_disabled`):
   - `git status --porcelain -- <dir>` empty → `nothing_to_commit` (no `add`, no lock taken —
     this is the common tick and it must not touch `index.lock`).
   - Guards, in order, each a `CommitSkipReason`: `git rev-parse --git-path rebase-merge` /
     `rebase-apply` exists → `rebase_in_progress`; `--git-path MERGE_HEAD` → `merge_in_progress`;
     `--git-path CHERRY_PICK_HEAD` → `cherry_pick_in_progress`; `git symbolic-ref -q HEAD` fails
     → `detached_head`; `git diff --name-only --diff-filter=U -- <dir>` non-empty →
     `conflicted_paths`. (`--git-path` rather than `.git/<x>` so a linked-worktree layout resolves.)
   - `git add -A -- <dir>` then `git commit --only -m "antiphon: sync card files (<board name>)"
     --trailer antiphon=true -- <dir>`; `git rev-parse HEAD` → `CommitSha`.
   - Any non-zero exit → `git_error` with the trimmed stderr in `Error`; files stay written; the
     next pass retries because step 4's status check still sees them.
   - Log at Warning only when a repository's skip reason **changes** (a rebase that lasts ten
     ticks logs once); Debug otherwise. The last-reason map lives on the singleton gate.
5. Never `git add` or `git commit` without the pathspec; never `push`; never `stash`, `checkout`
   or `reset`. The service has no code path that could touch a file outside `docs/cards/<slug>/`.

**Locking.** `CardTaskFileSyncGate` (singleton): a `SemaphoreSlim(1,1)` per full-path-normalised
repository. The tick takes it with `WaitAsync(0)` and skips a busy repo; the endpoint takes it the
same way and answers **409 `card_file_sync_running`**. Boards of one project run sequentially under
that one lock; each board is its own commit.

**Settings** (`CardFileSync` section, `CardFileSyncSettings`, bound at `Program.cs` beside
`CardTransitions`):

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Off means the feature does not exist: no tick, endpoint answers 409 `card_file_sync_disabled`. |
| `AutoCommit` | `true` | Off writes files and never commits (`CommitSkipReason` `autocommit_disabled`). |
| `IntervalSeconds` | `60` | Tick cadence, floor 5. `0` disables the tick and leaves the endpoint on (manual-only mode). |

**Endpoint.** `POST /api/boards/{id:guid}/card-files/sync?dryRun=false` → 200
`CardFileSyncBoardResult`; 404 unknown board; 409 as above. Board ids are guids with a route
constraint (ops-http rule); no identifier resolution — this is a board verb, not a card verb.
Mirrors `POST /api/boards/{id}/tracker/sync` in shape. No fleet-wide `POST /api/card-files/sync`
in v1 (the tick is that).

```csharp
public sealed record CardFileSyncBoardResult(
    Guid BoardId,
    string BoardName,
    string? Directory,          // repo-relative "docs/cards/<slug>", null when writing was skipped
    int Written,
    int Deleted,
    int Unchanged,
    string? CommitSha,          // null when no commit was made, for any reason
    string? WriteSkipReason,    // board_archived | project_archived | no_repository_path | not_a_git_repository | no_cards
    string? CommitSkipReason,   // autocommit_disabled | nothing_to_commit | dry_run | rebase_in_progress | merge_in_progress | cherry_pick_in_progress | detached_head | conflicted_paths | git_error
    string? Error,              // git stderr on git_error; otherwise null
    bool DryRun);
```

---

## Slices

Each slice is one commit on its own, green before the next starts. S1 and S2 are the render and
write core with no host wiring; S3 makes it live; S4 is the record.

### S1 — Renderer and writer (no git)

**Files:** `server/Application/Services/CardTaskFileRenderer.cs` (new, `internal static`, pure:
`BoardSlug`, `CardFileName`, `RenderCard`, `RenderIndex`, `YamlQuote`),
`server/Application/Services/CardTaskFileService.cs` (new: load, render, compare, write, delete;
the commit step lands in S2 as a stub returning `autocommit_disabled`),
`server/Application/Dtos/CardFileSyncDtos.cs` (new, the result record above),
`server/Application/Services/CardTaskFileSyncGate.cs` (new singleton: per-repo semaphore +
last-skip-reason map).

**Tests:** `tests/Antiphon.Tests/Application/CardTaskFileRendererTests.cs` (pure, no DB, no
`[Category]`): frontmatter round-trip for every field incl. a title with `: " #`, a label with
commas, a description with CRLF and a trailing `---`; nullable keys omitted; `## Outcome` present
only with a terminal reason; INDEX group order, empty-group omission, in-group ordering, count line;
slug rules and the 60-char cap; identifier sanitising.
`tests/Antiphon.Tests/Application/CardTaskFileServiceTests.cs` (`[Category("Integration")]`,
`ScratchGitRepo` per test as the project path, own Project/Board/Column/Card rows in the
`CardThreadServiceTests.Scenario` style, disposed by id; assertions only over the scratch directory
and the test's rows): first sync writes N+1 files with LF; second sync `Written == 0 &&
Deleted == 0`; title edit → old file gone, new file present, one file per card; archived card kept
with `archived:` keys and listed under Archived; project with `LocalRepositoryPath = null` →
`no_repository_path` and nothing on disk; path that is not a repo → `not_a_git_repository`; two
boards in one project (each with its own CARD-0001) → two directories; a stray `notes.md` in the
board directory is deleted and a stray `notes.txt` is not; `dryRun` reports counts and writes
nothing; a CRLF rewrite of one file on disk → rewritten LF, reported `Written == 1`.

**Verify:** `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0004/ --
--treenode-filter "/*/*/CardTaskFile*/*"`, then delete every `bin-c0004` directory.

### S2 — Path-scoped commit with guards

**Files:** `CardTaskFileService.cs` (the private git runner in `WorktreeManager.RunGitAsync`'s
shape with a `GitProcessGate` lease and `GitSettings` timeout; `CommitAsync` implementing
§Reconcile step 4), `CardTaskFileSyncGate.cs` (reason-change logging).

**Tests (same class, all `ScratchGitRepo`):** first sync commits exactly one commit whose subject is
`antiphon: sync card files (<board>)`, whose `%(trailers:key=antiphon,valueonly)` is `true`, and
whose message does **not** contain the identifier of any card; second sync leaves `HEAD`
unchanged; title edit → `git diff --name-status HEAD~1 HEAD` shows one `R` line and no other
changes; an unrelated file staged before the sync is still staged (`git diff --cached
--name-only`) and absent from the sync commit; `.git/rebase-merge` created by the test (via `git
rev-parse --git-path`) → files written, `CommitSkipReason == "rebase_in_progress"`, `HEAD`
unchanged; directory removed → next sync commits; detached HEAD (`git checkout --detach`) →
`detached_head`; `AutoCommit=false` → `autocommit_disabled` and a dirty tree; under
`core.autocrlf=true` + `* text=auto` in the scratch repo, a `git checkout -- docs/cards` that puts
CRLF on disk followed by a sync → `Written` may be > 0 but `CommitSkipReason == "nothing_to_commit"`
and `HEAD` unchanged (the idempotence-under-autocrlf pin); `git_error` when the repo's `index.lock`
exists (create the file) — files written, reason reported, next sync after removal commits.

**Verify:** as S1.

### S3 — Hosted service, endpoint, settings

**Files:** `server/Application/Settings/CardFileSyncSettings.cs` (new),
`server/Infrastructure/Orchestration/CardTaskFileSyncHostedService.cs` (new; copy
`CardWorkTransitionHostedService`, add the `IntervalSeconds == 0` manual-only arm; each tick calls
`CardTaskFileService.SyncAllAsync`, which iterates non-archived projects with a path and their
non-archived boards, one try/catch per board), `server/Api/Endpoints/CardFileSyncEndpoints.cs`
(new; `MapCardFileSyncEndpoints`, modelled on `TrackerSyncEndpoints`), `server/Program.cs`
(`Configure<CardFileSyncSettings>` beside `:165`; `AddScoped<CardTaskFileService>` beside
`CardService`; `AddSingleton<CardTaskFileSyncGate>`; `AddHostedService` beside `:523`;
`MapCardFileSyncEndpoints()` beside `MapTrackerSyncEndpoints()`), `server/appsettings.json`
(`"CardFileSync": { "Enabled": true, "AutoCommit": true, "IntervalSeconds": 60 }` beside
`CardTransitions` at `:137`).

**Tests:** `SyncAllAsync` in `CardTaskFileServiceTests`: two projects, one with a repo and one
without → the first synced, the second reported `no_repository_path`, an archived project and an
archived board skipped. HTTP in an `AntiphonWebAppFactory` subclass (`CardFileSyncEndpointTests`):
`POST /api/boards/{unknown}/card-files/sync` → 404; `?dryRun=true` on a seeded board whose project
has no path → 200 with `writeSkipReason == "no_repository_path"` and `dryRun == true`; with
`CardFileSync:Enabled=false` in the factory's settings → 409 `card_file_sync_disabled`. The hosted
service is a timer around `SyncAllAsync` and gets no test of its own, as
`CardWorkTransitionHostedService` has none.

**Verify:** the two test classes; then `pwsh -NoProfile -File scripts/restart-apphost.ps1`, wait one
tick, `git -C C:\src\Antiphon show --stat HEAD` shows one `antiphon: sync card files (Antiphon)`
commit touching only `docs/cards/antiphon/`, `git status` shows nothing else changed, and
`curl -X POST "http://localhost:17202/api/boards/8988ca03-7414-47ad-b0b6-51556c701703/card-files/sync?dryRun=true"`
answers `written: 0, deleted: 0`. Before the restart, run the dry run against a build that has S3
only if the tick is disabled in config; otherwise accept that the first tick is the first run.

### S4 — Docs and the record

**Files:** `docs/orchestration-loop.md` (a gotcha bullet: one-way, `docs/cards/<slug>/`, generated
and overwritten, path-scoped `antiphon: true` commit, never push, never an identifier in the
message, the guard list, `CardFileSync` keys, the 409 codes), `docs/antiphon-api.md` §"Work items —
cards, boards, projects" (one route line beside the tracker sync line at `:333` or in the boards
block at `:122-126`), `docs/ops-http.md` (one route row), `AGENTS.md` §"Cards and tracker" (one
line: files under `docs/cards/` are generated from the board; edit the card, not the file),
`TODO.md` (header points at `docs/cards/antiphon/INDEX.md` as the generated list; items untouched —
they are already cards), and this plan's "Execution notes" updated with the commit shas.

---

## What this card does not do

- **No file → card import**, no watcher, no `POST …/tasks/import`. The record is append-only with
  reasons; an import verb needs an actor and a reason design of its own.
- **No UI.** `taskFilesPath` on a DTO, a board button, a "sync now" in the client — none of it.
- **No `scripts/card.ps1` verb.** The endpoint is the surface; `curl`/`Invoke-RestMethod` is enough.
- **No `AgentFilesService` filter.** An agent whose workspace *is* the project repo will see
  `docs/cards/` changes in its files review; v1 accepts that (spec risk 2, still true).
- **No push**, no branch, no remote interaction of any kind.
- **No per-project or per-board configuration** of the directory; it is `docs/cards/` everywhere.
- **No orphan cleanup.** A board rename changes the slug and leaves the old directory behind
  (§Left open).
- **No CardService hook** (Decision 4).

## Left open, deliberately

- Board rename → orphaned `docs/cards/<old-slug>/`. The safe rule is "delete a sibling directory
  only when its `INDEX.md` carries the generated marker line"; not worth the code until a board is
  renamed. Delete by hand.
- One commit per repository per tick (all boards in one pathspec) instead of per board — trivial to
  switch if a multi-board project appears; the result record already lists boards.
- Filtering `docs/cards/` out of `AgentFilesService` listings and `GET /api/cards/{id}/thread`
  commits if the noise bites. The thread is already safe by the no-identifier rule.
- Retiring `TODO.md` entirely once every item on it is a card (memory says they are).

## Test matrix

| Property | Pinned by |
|---|---|
| Frontmatter and body are deterministic and YAML-safe | `CardTaskFileRendererTests` |
| INDEX groups, order, empty-group omission | `CardTaskFileRendererTests` |
| Slug and filename rules, 60-char cap, identifier sanitising | `CardTaskFileRendererTests` |
| First sync writes; second sync writes nothing and commits nothing | `CardTaskFileServiceTests` |
| Title edit = one file, one commit, git sees a rename | `CardTaskFileServiceTests` |
| Archived card kept with `archived:` keys | `CardTaskFileServiceTests` |
| Reconciler owns `*.md` only | `CardTaskFileServiceTests` |
| No repo path / not a repo / archived board / archived project → skip, no throw | `CardTaskFileServiceTests` |
| Two boards in one project → two directories | `CardTaskFileServiceTests` |
| Unrelated staged file survives, uncommitted | `CardTaskFileServiceTests` |
| Rebase / detached / index.lock → written, not committed, committed next pass | `CardTaskFileServiceTests` |
| Idempotent under `core.autocrlf=true` | `CardTaskFileServiceTests` |
| Commit message carries the trailer and no identifier | `CardTaskFileServiceTests` |
| `dryRun` touches nothing | `CardTaskFileServiceTests`, `CardFileSyncEndpointTests` |
| 404 / 409 disabled / 200 shape | `CardFileSyncEndpointTests` |

All integration tests: own rows, own scratch repo, assertions scoped to both; no `[NotInParallel]`
(nothing global is swept — `SyncAllAsync`'s test seeds its own projects and asserts only on them).
Git child processes need no `ParallelLimiter` (`DelegationWorktreeTests` carries none).

## Sequencing and risks

- **First run on this repo:** ~315 files, one commit on master in `C:\src\Antiphon`, 60 s after the
  AppHost restart that deploys S3. It will land while delegates may be running in the shared
  checkout. That is safe for verdicts (CARD-0227 closed, §Ground truth) and for files (nothing
  reads `docs/cards/`), and it is the feature; the dry run exists so the implementer can eyeball
  the output on a scratch clone first.
- **`index.lock` contention** with a delegate's own `git commit` in the shared checkout: bounded to
  ticks with real card changes (the common tick runs only `git status`); a collision is a
  `git_error` retried next tick on our side, and a one-off "index.lock exists" on the delegate's
  side. Acceptable; if it recurs, widen the tick.
- **Volume of commits on master:** roughly one per tick in which any card changed — on a busy day,
  dozens. The spec chose "committing: yes"; the tick, not a per-mutation hook, is what keeps a
  burst to one commit. If it proves noisy, `IntervalSeconds` is the knob, `AutoCommit=false` the
  off switch, and the files still exist either way.
- **Sync commits ride along on the next push** by whoever pushes master. The feature never pushes;
  that was and remains the rule.
- **Sub-directory projects** (ClaudeBot agents) commit into the parent repo under
  `<agent-dir>/docs/cards/`; all three have zero cards today, so nothing is written.
- **`slack-test`'s path does not exist**, `check-interpreter`'s is not a repo: both must report a
  `WriteSkipReason`, never throw, never create the directory.
- **Human edits are overwritten** without warning; the INDEX header and the AGENTS.md line are
  the only notice. This is the one-way contract.
- Sequence: S1 → S2 → S3 → S4, each its own commit, each verified by its own test classes before
  the next; S3's restart is the only live step.

## Execution notes

*(filled in by the executing task: commit shas per slice, test counts, the first live commit's sha
and `--stat`.)*
