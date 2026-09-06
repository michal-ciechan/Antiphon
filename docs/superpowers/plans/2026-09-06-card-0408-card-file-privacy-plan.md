# CARD-0408 - Card-file privacy and explicit publication

Date: 2026-09-06. Stage: Plan. Base: `fc9cdf5c`. Task: `42d4421c`.

Decision: implement board opt-in, a dedicated private-notes field, and a per-card
override. Enforce the policy before rendering and before committing, through the
same service for manual sync and the 60-second tick. This document specifies the
implementation; it does not enable publication or change any live cards/repos.

This is a security-sensitive, multi-slice change. Next stage is **TestDesign**;
Code must finish with a separate Review before landing the mechanism.

## Ground truth

The full live CARD-0408 and CARD-0088 were read through `scripts/card.ps1 get`
with `-Board Antiphon`. CARD-0409's boundary below is taken from CARD-0408 and the
dispatch brief, not a new census of files or GitHub visibility.

| Assumption / requirement | Code today | Consequence |
|---|---|---|
| Every card is written | `CardTaskFileService.SyncBoardAsync` loads every card, including archived cards, then passes whole `Card` entities to `RenderCard` and `RenderIndex` | Filtering must precede filenames, rendering, index grouping and counts |
| Only the description can leak | `CardTaskFileRenderer` also emits title, alias, labels, external author/URL, archive actor/reason and terminal reason; titles occur in filenames and index rows | Define an explicit public projection; hiding one body field alone is insufficient for a private card |
| AutoCommit is the gate | `CardFileSyncSettings`: Enabled=true, AutoCommit=false, IntervalSeconds=60; false AutoCommit still writes files | Policy must gate disk writes independently of commits |
| Manual and scheduled sync differ | Both call `CardTaskFileService`; `CardTaskFileSyncHostedService` has no CardService enqueue | Keep one policy implementation and the existing cadence |
| A path-scoped commit only contains generated content | `CommitAsync` uses `git add -A -- <board-directory>` and `git commit --only ... -- <board-directory>` | Stray `.txt`, nested files and staged content under that directory can enter a commit; replace the directory pathspec with exact generated paths |
| Skipping a private board removes its files | Early returns for archived boards/projects and `no_cards` leave old files alone | Privacy revocation needs an empty desired set and removal, not an early return |
| Existing file locations are stable | `UniqueBoardSlugAsync` recomputes from names; CARD-0004 explicitly leaves rename orphans open | Pin the directory used by this mechanism; prevent target changes from abandoning old exports |
| Content edits have history and concurrency | `CardService.UpdateContentAsync` snapshots via `CardRevisionLog.AppendContentEdit`, rotates the token, and emits ID-only `CardChanged` | Private notes/visibility must participate in the same atomic edit and history contract |
| There is a board settings PATCH | `BoardEndpoints` has create/archive/unarchive/delete/workflow/order, no generic update | Add a narrowly named card-files settings route |
| Write-time UX knows the output destination | `card.ps1 new` prints the card summary and ID only; `CardDto` has no export status | Add a server-computed status and print it without running sync |
| A clone is a backup | CARD-0088 is Done; its plan and `docs/bootstrap.md` distinguish a fresh DB from `dev-backup.ps1`/`dev-restore.ps1` migration | Card markdown is a deliberate public projection, never a backup of private notes or revisions |
| CARD-0004 prescribed AutoCommit=true | Its original normative table did; its execution notes and current settings explicitly supersede that with false | Keep false in defaults, examples and this deployment; enabling it is a later owner action |

Primary sources: `server/Application/Services/CardTaskFileService.cs`,
`CardTaskFileRenderer.cs`, `CardTaskFileSyncGate.cs`, `CardService.cs`,
`CardRevisionLog.cs`, `BoardService.cs`, `ProjectService.cs`, `ProjectSetupService.cs`;
`server/Application/Settings/CardFileSyncSettings.cs`;
`server/Infrastructure/Orchestration/CardTaskFileSyncHostedService.cs`;
`server/Api/Endpoints/{CardFileSync,Card,Board,Project}Endpoints.cs`;
`server/Application/Dtos/{BoardDtos,CardFileSyncDtos,CreateProjectRequest,UpdateProjectRequest}.cs`;
`server/Domain/Entities/{Card,CardRevision,Board,Project}.cs`;
`scripts/card.ps1`; `client/src/api/{boards,projects}.ts`;
`client/src/features/board/{BoardPage,CardModal,CardEditModal}.tsx`;
the existing card-file renderer/service/endpoint tests;
[CARD-0004 plan](2026-09-02-card-0004-card-task-file-sync-plan.md) and
[CARD-0088 plan](2026-08-19-card-0088-bootstrap-plan.md).

## Decisions

### D-1. Publication permission is explicit and independent of repo visibility

Schema (Domain enums, EF string conversions consistent with neighboring enums,
camelCase JSON with enum names; migration created with the EF CLI):

| Entity | Property | Initial/backfilled value | Meaning |
|---|---|---|---|
| Board | `bool SyncCardFiles` | false | Board-wide permission to export its public surface, including future cards |
| Card | `CardFileVisibility CardFileVisibility` | Inherit | Enum `Inherit`, `Private`, `Public`; default follows the board; Private always suppresses the entire card |
| Card | `string PrivateNotes` | empty string | At most 20,000 characters, stored as text, independent of Description |
| CardRevision | `string? PrivateNotes`, `CardFileVisibility? CardFileVisibility` | null for old rows | Superseded values on ContentEdit; no invented historical classification |
| Project | `RepositoryVisibility RepositoryVisibility` | Unknown | Enum `Unknown`, `Private`, `Public`; operator assertion, not inferred from GitHub integration being enabled |
| Board | `string? CardFilesDirectorySlug` | null | Internal, stable directory name, max 60 chars, using the current collision allocator on first reconciliation |
| Board | `string? CardFilesRepositoryPath` | null | Internal normalized project-root path used for that directory; retained while removal is outstanding |

Existing boards are **not grandfathered**. No migration copies Description into
PrivateNotes, reviews a card, enables a board, or touches a filesystem. New card
creation from UI, CLI, tracker import, seed or automation defaults to Inherit.
New boards created by ProjectSetup also default to off.

Policy, in order: feature Enabled, usable target, board/project active,
SyncCardFiles, known RepositoryVisibility, card override. `Public` does not bypass
a disabled board, feature switch or unknown target. It records explicit card
intent; the useful escape hatch on an opted-in board is `Private`.

| Board SyncCardFiles | Repo visibility | Card visibility | Export |
|---|---|---|---|
| false | any | any | No: `board_not_opted_in` |
| true | Unknown | any | No: `repository_visibility_unknown` |
| true | Private or Public | Private | No: `card_private` |
| true | Private or Public | Inherit or Public | Yes, subject to target/ignore/git guards |

On a public repo, an *unmarked board* cannot write an unmarked card. An Inherit
card on an explicitly opted-in board is marked by that board policy and does
write. This is how B avoids per-card review: the owner declares the board a place
for publishable work, with sensitive working detail in PrivateNotes. Board UI
must state that the opt-in covers existing and future cards. No mechanism can
classify arbitrary confidential prose placed in the public fields of such a
board; do not describe opt-in as a content scanner.

Rejected: a Public/Private card enum with no inheritance (per-card busywork);
RepositoryIsPublic as permission (repo classification is not consent); Public
overriding a disabled board (no reliable board stop switch); auto-enabling old
boards based on current files (perpetuates the near miss).

### D-2. PrivateNotes is a storage field with a separate read surface

Add `privateNotes` and `cardFileVisibility` to CreateCardRequest and
UpdateCardContentRequest. On edit, omitted/null means unchanged; empty notes
means clear. Preserve notes text, including leading/trailing whitespace and LF;
enforce the 20,000-character ceiling before saving, with a normal 422 field error.
Add `maxPrivateNotesLength` and `cardFileVisibilityValues` to `/api/cards/limits`.
Invalid numeric or string enum values must be rejected, never treated as Inherit.

An edit can atomically remove a contact from Description and put it in
PrivateNotes. Snapshot both new fields with the existing ContentEdit revision,
reason, author, revision number and concurrency token. Existing revisions of a
description remain in the DB; neither old revisions nor notes become file input.

Do **not** put private text on the widely reused CardDto. Add only
`cardFileVisibility`, `hasPrivateNotes`, and optional `cardFileStatus` there.
Add `GET /api/cards/{id}/private-notes[?revisionNumber=N]`, using the normal card
identifier resolver and scope rules. Return
`{ cardId, privateNotes, concurrencyToken, revisionNumber }`; revisionNumber is
null for current notes. A historical read selects that ContentEdit snapshot;
missing/non-content revision is 404; old snapshots with no field return null,
meaning unknown history, not an empty note. Set `Cache-Control: no-store`.
CardRevisionDto exposes the superseded visibility and a presence indicator, not
the note text. No request/response-body logging of this route or note writes.

The UI fetches notes only when their separate panel/edit surface is opened.
Do not include them in board/list/summary/thread DTOs, exports, tracker payloads,
SignalR payloads, diagnostics, launch prompts or delegation briefs. No generic
entity serialization on those boundaries. Existing explicit tracker body maps
continue using Description only. This does not redesign tracker publication or
make card visibility a tracker ACL; the UI calls it **Card-file visibility**.

Private means Antiphon database and its backups, accessible through the existing
local application access model. It is not encryption, a new authorization role,
or permission to store credentials outside the established credential system.
The guarantee is that the mechanism never copies notes into repository artifacts.
Manually copying a note into public text, a chat, a plan or another file remains
an explicit disclosure by the caller. Do not auto-copy notes for agent convenience.

Rejected: fenced body markers (typos silently publish); regex secret detection
(cannot prove absence); returning notes in every CardDto (unnecessary accidental
export and prompt surface); suppressing revision history (breaks CARD-0019).

### D-3. Give the renderer only publishable data

Introduce an internal immutable `CardFilePublicCard` projection, constructed only
for eligible cards. Both card rendering and INDEX use it; neither accepts Card
or CardRevision. Do not select PrivateNotes from the DB for file sync. Keep an
explicit whitelist matching the existing public file format: ID, identifier,
title, alias, description, status, importance/provenance, urgency, due and
lifecycle timestamps, labels, external tracker/key/URL/author and review flag,
archive timestamp/actor/reason, and TerminalReason. All of these are public fields
when a card is eligible. The UI/help must name outcome/archive reasons as public
too; sensitive reasons should be summarized and their detail put in PrivateNotes.

Private cards contribute **nothing** to filenames, markdown, INDEX rows, group
counts, total/archived counts, labels, generated summaries or commit subjects.
No private-card stub, filename, redaction marker, note length or hash goes into
the repository. An opted-in board with zero eligible cards has no INDEX.md.
Private-note-only edits must leave all generated bytes and git HEAD unchanged.

Keep existing ordering, escaping, UTF-8/no-BOM, LF format and archive-card
behavior for eligible cards. Runtime fields/revisions stay excluded as today.
The projection removes the temptation to serialize a newly added entity field.

### D-4. Configured visibility; Unknown fails closed

Use `Project.RepositoryVisibility` in this slice; do not add a gh dependency or
network call to creation, sync, or the 60-second tick. The project create request
accepts an optional value, default Unknown. Project PUT uses a nullable value:
omitted means preserve, so an older client cannot silently reset it. A change to
GitRepositoryUrl or LocalRepositoryPath resets it to Unknown unless the same
request explicitly supplies a new value. Validation is 422 for invalid enum.

All status/UI text says **configured Public/Private** or **Unknown (not checked)**.
A configured Private value is not proof that a provider has not since made the
repo public. This cannot grant export by itself: board opt-in authorizes the
public fields equally for Private and Public targets. Public + off is a hard
write refusal; Public + on is permitted with warning `public_repository`.
Unknown + on is refused. Public/Unknown notices name the configured target and
remediation, with no raw remote URL that might embed credentials.

A future visibility detector can strengthen diagnostics without becoming a new
publication permission path. Do not invent authentication setup or test GitHub
availability in CARD-0408.

### D-5. Reconcile removals and constrain git to generated paths

Add concrete `CardFilePolicyService` for decisions/status and keep filesystem/git
I/O behind Application interfaces implemented in Infrastructure where new seams
are needed: `Application/Interfaces/ICardFileRepository.cs` and
`Infrastructure/Git/CardFileRepository.cs` own target inspection, literal git
operations, contained file reconciliation and ignore inspection/installation.
Extract this feature's existing private git runner there; do not refactor other
git services. Register the seam and concrete policy service in `server/Program.cs`.
Reuse existing GitSettings timeout, GitProcessGate and cancellation
discipline; avoid a general CardService interface or repository wrapper.

Normative sync sequence:

1. Resolve the target and acquire CardTaskFileSyncGate before loading the
   authoritative board/project/card snapshot. Normalize lock ownership to the
   real git working-tree root (`GetRepoToplevelAsync`), so two subdirectory
   projects sharing an index cannot race. Keep their distinct project-relative
   `docs/cards` targets. Reject overlapping resolved board directories with
   `card_file_directory_conflict`; no two boards may own one directory.
2. Compute/pin CardFilesDirectorySlug with today's UniqueBoardSlugAsync rules on
   the first real pass. Store the target path before the first file mutation, so
   a crash can be retried against the same location. A rename never changes the
   stored slug. Dry run computes this without saving it. For legacy directories,
   the first pass resolves the current CARD-0004 slug; old, already-orphaned
   directories remain the owner's cleanup responsibility.
3. Evaluate policy and project only eligible cards. A disabled, archived,
   unknown-visibility or empty board has an **empty desired set**. Reconcile any
   existing owned top-level `*.md` to that empty set; do not create its directory
   or a stub/index. Revocation applies to archived cards too. Board/project
   archive now removes its owned exports instead of leaving them forever;
   individual card archive still retains an eligible card.
4. Reconcile the reserved board directory as CARD-0004 already does: write
   desired markdown, delete obsolete top-level markdown, leave other files and
   sibling directories alone. Deletion uses names, not old bodies. Validate
   normalized containment; refuse symlink/reparse-point traversal at root,
   docs/cards, board directory or a managed file (`unsafe_card_file_path`).
   No recursive deletion. Never write private text to a temporary file.
5. If privacy blocks new writes, removal is still allowed with Enabled=true.
   `Written=0` can coexist with `Deleted>0` and a privacy WriteSkipReason.
   If Enabled=false, neither the endpoint nor tick mutates anything (existing
   contract); status must say that disabling the feature does not erase files.
   SyncAll must include cleanup candidates from archived/off/empty boards instead
   of losing them in its current active-only query. Each failure still isolates
   one board and is visible as a result/log, not a silent missing result.
6. Build the git allowlist from the current desired generated paths plus obsolete
   top-level markdown paths being removed, including tracked/index-only stale
   paths found in git status. This last case matters after a partial failed run
   or a previously staged private card whose working file is already absent.
   Use NUL-delimited, literal pathspec input (`--pathspec-from-file=-`,
   `--pathspec-file-nul`, with literal-path handling) for add and commit, avoiding
   Windows command-line length and wildcard interpretation. An empty set runs
   no add/commit. Never substitute the board directory as a fallback pathspec.
   After staging, rebuild the commit list from changes against HEAD: a private
   file that was only a staged addition is now removed from the index and needs
   no commit path; do not pass a nonexistent, never-HEAD-tracked path to commit.
   Include stale generated markdown reported by git even if the whole working
   directory is absent. Tracked deletion paths remain in the commit list.
7. For every included existing file, current working-tree bytes must equal the
   current public projection (LF-normalized), and every removal must be absent.
   Stage those paths from that state. Existing staged versions on those exact
   generated paths are superseded; unrelated staged paths remain staged. A stray
   `.txt`, nested file or another board's file must never enter the commit.
   Recheck managed files before commit; on mismatch return
   `generated_files_changed`, retry next sync, do not commit stale bytes.
8. Keep merge/rebase/cherry-pick/detached/conflict/index-lock/timeout guards,
   AutoCommit=false default, commit trailer and no push. Commit messages use
   the opted-in board name, never card titles/identifiers or note content. A
   removal-only commit for a disabled/archived board uses a neutral subject
   `antiphon: remove unpublished card files` (no private board name).

Privacy-affecting saves (`SyncCardFiles`, RepositoryVisibility/target changes,
and card visibility/content edits) use the same repository gate, held through
SaveChanges, with the usual card token check **after** acquiring it. This orders
a revocation against a sync: a queued sync cannot publish a snapshot loaded
before a completed save. No reentrant SyncBoardAsync call under that gate.
Create also takes the gate when its board can publish; note-only edits may use
the same path for simplicity. Polling lifecycle/tracker writers must not set
the new policy fields; their existing non-policy updates remain a later tick.

Revocation removes the current working files and stages/commits their removal on
the next successful reconciliation (up to the next tick, or manual now). The
save's status reports `removal_pending` when old exports remain; it does not
claim immediate erasure. An I/O/git failure keeps that status and blocks adding
new generated content for the affected board until reconcile succeeds. Git
history, already pushed clones and already copied text are not erased.

LocalRepositoryPath changes, or hard board/project deletion, must first drain
the recorded export directory (turn sync off and reconcile while the old target
still exists). Refuse an operation that would abandon markdown or staged
generated paths with 409 `card_file_cleanup_required`, giving only the affected
target paths and board IDs. The guard also probes the current computed legacy
directory when the bookkeeping fields are null. Unreachable targets require
owner repair/removal before retry; no force path in v1. This is ownership cleanup,
not a new historical-content investigation. Preserve unrelated project deletion
rules and don't silently delete somebody else's workspace.

Rejected: skip-only revocation (stale body remains available to git add);
directory-wide staging (includes unreviewed content); repository-wide reset,
stash or git rm --cached (changes other work); restoring private content from
old revisions during retry. This feature serializes its own publishers; it does
not lock out arbitrary human git commands or editors racing the filesystem.

### D-6. API status and refusal shapes

Add `GET /api/boards/{id:guid}/card-files/status`, read-only even with Enabled=false.
Add `PUT /api/boards/{id:guid}/card-files/settings` with the complete body
`{ syncCardFiles: bool, expectedSyncCardFiles: bool }`. A missing field is 422;
stale expected value is 409 `card_file_policy_changed`. Boards currently have no
concurrency token; compare expected value under the shared gate. PUT persists
only settings, never runs a sync or starts an agent. Enabling while visibility
is Unknown or no usable target exists is 409 `card_file_policy_refused`, before
saving. Public visibility is allowed and returned with its warning. Disabling
must remain possible when a target has disappeared.

Expose the flag on BoardSummaryDto/BoardDetailDto. Keep CreateBoardRequest's
optional flag default false; an explicit true goes through identical validation.
Add RepositoryVisibility to project DTOs and nullable create/update requests.

Status shape (board response; per-card result adds effective card eligibility):

```json
{
  "boardId": "<guid>",
  "syncCardFiles": false,
  "repositoryVisibility": "Public",
  "visibilitySource": "Configured",
  "repositoryPath": "C:\\src\\example",
  "directory": "docs/cards/example",
  "eligible": false,
  "reason": "board_not_opted_in",
  "warnings": ["public_repository"],
  "ignored": true,
  "removalPending": false,
  "autoCommit": false,
  "intervalSeconds": 60
}
```

Unknown/no-path values use null paths and Unknown visibility, never a guessed
destination. The full create/content-edit response remains CardDto (201 on
create, 200 on edit); add `cardFileStatus` with this status plus `relativeFile`
and `cardFileVisibility`. For a private/suppressed card, relativeFile is null;
directory still says where an eligible card would go. A successful DB create
does **not** fail because export is blocked. Status I/O failure returns
`reason: status_unavailable`, eligible=false and a warning, preserving the
successful create so clients do not accidentally duplicate the card by retrying.
Single-card GET can attach status; bulk reads omit it to avoid N git probes.

Keep `POST /api/boards/{id}/card-files/sync?dryRun=`. Add `eligibleCards`,
`excludedCards`, `policy`, and string-array `warnings` to CardFileSyncBoardResult.
These counts are Antiphon-only diagnostics, never INDEX content. Existing fields
remain. Real sync returns 200 for a completed reconciliation, including refusal
to write new content: use WriteSkipReason `board_not_opted_in`,
`repository_visibility_unknown`, `no_publishable_cards`, or existing structural
reasons. This is a hard *filesystem write refusal*, not necessarily an HTTP error;
removals and their counts must be representable. `directory` now names a resolved
target even when writes were refused, null only without a safe resolved target.

Preserve 404 unknown board and 409 `card_file_sync_disabled` /
`card_file_sync_running`. Unsafe paths/overlapping ownership return 409 with
their named code before mutations. Use the HttpException hierarchy and Problem
Details middleware. Operational git failures remain 200 with CommitSkipReason
and sanitized Error, plus warning; never echo notes, card bodies or remote stderr
containing credentials. Consumers must inspect the result, not only HTTP 200.

Dry run is 200 with the same policy and prospective counts, even when blocked.
No DB changes, files, ignores, staging, commits or persistent warning-state
updates. If Enabled=false the existing sync endpoint still returns 409; use GET
status to inspect policy. A dry run cannot flip flags, bypass policy, or preview
private text.

The tick calls this same policy/reconcile core. Log the refusal at Warning once
per `(boardId, target, reason)` change; repeated ticks are Debug. The current
repo-only skip map would let two boards alternate and spam, so split policy
deduplication by board. Board settings show the current reason/status without
requiring log access. Reuse IEventBus ID-only BoardChanged/CardChanged events;
project policy changes can use BoardChanged with projectId, as project archive
already does. Extend `useSignalRInvalidation` so those events invalidate the
affected card-file status and project queries as well as the existing board
queries; CardChanged also invalidates that card's explicit notes query. Do not
send notices to Slack, Telegram or alert sinks.

### D-7. .gitignore defaults are installed during project setup, never on the tick

For a project with **no opted-in boards**, install at its LocalRepositoryPath's
`.gitignore` (including a subdirectory project, not its parent repo):

```gitignore
# BEGIN ANTIPHON CARD FILES
/docs/cards/
# END ANTIPHON CARD FILES
```

Use the Infrastructure file-I/O helper called by ProjectService's creation
and repository-path assignment paths, so API creation and ProjectSetup share it.
Run once a valid existing local git path is available, before allowing any board
opt-in. Do not create a missing checkout or initialize a repo. Preserve other
bytes/newline style, make the append idempotent, and accept an existing effective
ignore rule instead of duplicating it. A malformed owned block or unwritable file
does not lose a successfully created project: return a setup warning and expose
`card_files_ignore_missing` in project readiness/status. Sync stays off by default
and Unknown still blocks enabling it. Bootstrap-check remains read-only and names
the same remediation. No content bodies are needed for any ignore check.

Existing project rows are not silently edited during migration or a tick. Add
the managed block to Antiphon's root `.gitignore` in Code's documentation/setup
slice as the checked-in default for fresh clones. For other existing projects,
the readiness warning and bootstrap instructions require the owner to add it.
CARD-0409 owns any live interim ignore changes in the existing Antiphon checkout.
No action here removes or rewrites existing card files during this Plan stage.

Opt-in **never automatically removes a user's ignore rule**. The owner can keep
the protection while reviewing a dry run. Before actual publication, edit the
ignore policy explicitly. For a project with one or several opted-in boards, the
documented replacement is:

```gitignore
# BEGIN ANTIPHON CARD FILES
/docs/cards/*
!/docs/cards/publishable-board/
# END ANTIPHON CARD FILES
```

Use the exact stable slug from status, adding one exception per opted-in board;
off-board directories remain ignored. The writer still enforces policy for every
file independently of ignores. If an eligible generated file is ignored by any
effective rule, a real sync refuses new writes/commit with
`card_file_path_ignored`; dry run reports the same refusal (`Written=0`), eligible
card count and ignore status, plus any prospective removals. No private content
is rendered to explain a refusal. Re-run dry-run after changing ignore rules to
see prospective writes. Inspect ignores with git's no-index mode too, so a
tracked file does not hide an effective ignore rule. Never use
`git add -f`. Removals of previously generated files remain allowed. No automatic
staging/commit of `.gitignore` by CardTaskFileService.

Gitignore does not protect tracked or already-staged files. Readiness must report
that condition for a non-opted-in board as `card_file_cleanup_required`, not claim
the rule hides them. Turning a board off reports removal pending and the optional
owner action of deleting its allow exception; the policy's empty desired set is
the actual protection. Absence of an ignore file never grants publication.

### D-8. CLI and UI must say what saving means

`card.ps1 new` and `edit` gain `-CardFileVisibility Inherit|Private|Public`,
`-PrivateNotesFile <path>` and `-ClearPrivateNotes` (edit only). A file and clear
together are a local error. Do not add an inline private-notes argument. Use the
existing UTF-8 bytes/JSON file-argument path, send an empty file as an explicit
clear, and never echo its body. A field over limit fails locally with length and
ceiling only. `new` still defaults to Inherit and does not sync.

After the existing created summary/ID, print these ASCII-only lines, derived
from returned server status (example, not a current repo observation):

```text
card files  NOT WRITTEN: board_not_opted_in
target      C:\src\example\docs\cards\example (configured Public repository)
policy      Inherit; board sync off; AutoCommit off
private     notes stored in Antiphon; excluded from card files
```

For eligible creation, first line is `ELIGIBLE: next sync (60s)` (or `manual sync`
when IntervalSeconds=0), followed by the full predicted file path. Say
`PUBLIC REPOSITORY: public card fields will be written on sync` for configured
Public, and `UNKNOWN REPOSITORY VISIBILITY: sync blocked` for Unknown. Never say
"written" solely because create succeeded. Edit also prints removal pending if
applicable. A missing status from an older server prints
`status unavailable; export safety not confirmed`, not "private".

Make `-Json` on new/edit return the single response object without human lines;
it currently has no new/edit branch. Notes are absent from that DTO. Ordinary
get/history must not print note text; explicit private-note retrieval uses the
dedicated API. Script errors retain current nonzero behavior; a card successfully
created with export blocked exits 0 and explains why. Existing fresh token reads
and `-Token` behavior remain.

UI changes:

- Board settings: off-by-default **Publish card files** switch, configured repo
  visibility, absolute destination, policy reason and dry-run/sync action results.
  Enabling clearly covers current and future Inherit cards. The initial action
  is the user's explicit opt-in; do not add another approval dialogue.
- ProjectConfig/ProjectSetup: Repository visibility select, Unknown default;
  show the ignore readiness warning and remediation. No automatic inference.
- BoardPage create, CardEditModal and CardModal: distinct **Private notes (kept
  in Antiphon; excluded from card files)** field/panel plus Card-file visibility
  selection. Description/help shows whether public fields are eligible and warns
  that outcome/archive reasons also export. Never mingle private notes with the
  public Markdown preview; show private text without fetching embedded resources.
- CardHistory: show visibility changes and an explicit action to inspect a
  private-note snapshot; never insert that snapshot into public body/history
  previews. Load notes separately and avoid persisting them to localStorage or
  a persisted TanStack cache. A note read failure must not prefill empty notes and
  accidentally clear them on a later unrelated edit.

## Code slices

Each slice is a scoped commit with its verification results; the whole mechanism
must be complete before deployment. Do not restart the shared stack from this
worktree. No live opt-in or AutoCommit change is part of Code.

| Slice | Changes and files | Required evidence for TestDesign to specify |
|---|---|---|
| S1 - Safe defaults and public projection | Domain entities/enums above; AppDbContext; CLI-generated migration/designer/snapshot; BoardDtos, project DTOs, CardFileSyncDtos; CardFilePolicyService and public projection; CardTaskFileRenderer/Service first gate; CardFileSyncSettings comments | Migration old/new rows private by default; policy table; renderer/index absence; retain eligible-card formatting tests |
| S2 - Notes and policy APIs | CardService, CardRevisionLog, BoardService, ProjectService; CardEndpoints, BoardEndpoints, CardFileSyncEndpoints; new private-notes read DTO/route; limits; settings validation/status; repository gate ordering | Atomic public-to-private-note edit; token collision/history semantics; GET privacy boundaries; old-client omission; API codes and create success despite refusal |
| S3 - Reconciliation and commits | CardTaskFileService, CardTaskFileSyncGate; new Infrastructure I/O seam(s); CardTaskFileSyncHostedService cleanup candidates/results; stable directory pinning; ProjectService/BoardService target/delete guards | Revocation, zero-visible set, archive, rename stability, old path refusal, stale index, exact pathspec, safe paths, partial failures, concurrency and dry-run non-mutation |
| S4 - Setup and ignore behavior | ProjectService, ProjectSetupService/readiness DTOs; ignore helper; scripts/bootstrap-check.ps1 (read-only); ProjectReadinessPanel | All creation paths, missing repo, custom ignore bytes, malformed block, effective ignored/tracked/staged distinctions, multi-board/subdirectory cases |
| S5 - card.ps1 UX | scripts/card.ps1 and board-api bundle help | Actual script against a stub API: exact text/exit/JSON; UTF-8 multiline notes; limit/clear/token behavior; no body echo |
| S6 - Client | client/src/api/boards.ts, projects.ts; BoardPage/CardModal/CardEditModal/CardHistory; new board publishing settings component; ProjectConfig/ProjectSetupModal/ProjectReadinessPanel; SignalR invalidation hook | Off/unknown/public states, create/edit persistence, private/public separation, pending removal, 409/422 recovery, failed notes read, no cached private text in bulk views |
| S7 - Owner docs and default ignore | docs/orchestration-loop.md, agent-card-lifecycle.md, ops-http.md, antiphon-api.md, bootstrap.md; server/Bundles/board-api.md; root .gitignore; CARD-0004 plan addendum pointing here | Corrected opt-in/skip/staging contracts, bootstrap fresh-vs-backup distinction, operational rollout instructions; no generated docs/cards edits |

S1 must install the safe gate along with schema/projection so an intermediate
restart cannot publish newly stored fields. S3 completes removals, not historical
redaction. Avoid widening `antiphon.areas.json` for a hypothetical collision.

## Verification brief for TestDesign

Append the executable `## Verification design` to this landed plan; do not
re-decide the schema. Pin each safety guard with a PC-n positive control. Code
must run each break/red/revert/green control and report all V/R/PC items.

Use unique synthetic sentinels, never any real email/contact or current card body.
Check output **bytes**, filenames, INDEX counts/rows, captured log/API output,
`git diff --cached` and `git show HEAD:<path>`, not just eligible/written counts.
Use two distinct private sentinels for old/current notes to catch history leaks.

Mandatory behavioral groups:

1. Exhaustive board x repo x card policy table, defaults on migration and every
   create path, invalid enum rejection, no auto-grandfathering. Include off board
   + Public card, opted-in public board + Inherit, and Unknown + explicit opt-in.
2. A mixed public card keeps Description visible and both old/current note
   sentinels absent from all generated bytes and commit blobs. Note-only edits
   yield no file diff or commit. A private card with sentinels in *all* public
   fields is absent from filenames, INDEX counts and metadata. All-private means
   no INDEX. Test the projection boundary, not a regex marker happy path.
3. API create/edit persists notes, null/empty/limit/Unicode behavior and history;
   stale-token conflicts lose no text. Explicit note read is the only DTO with
   note text. Board/list/thread/revision/SignalR/tracker/launch boundaries remain
   note-free. Verify a non-ContentEdit revision cannot be read as a private snapshot.
4. Existing exported card made Private, last public card removed, board off,
   project visibility Unknown, board/project archive: next reconcile removes
   owned exports/index and leaves unrelated files alone. Include a paused sync
   racing a privacy edit, blocked removal/retry, index-only private additions,
   and restart between state pinning, write, delete and commit.
5. AutoCommit=true in ScratchGitRepo is safe on the **first** sync after migration
   with off/unknown boards; only opted-in public projections commit. An unrelated
   staged source file, nested file, `.txt` and another board remain uncommitted.
   An old staged generated version is replaced by current public bytes or removed.
   Include literal metacharacters, a large path list, CRLF and the current git
   refusal/retry guards. No git push at any point.
6. Same source snapshot yields policy/count parity for manual, dry-run and tick.
   Dry-run hashes/checks DB, files, index, HEAD and ignore file before/after.
   Check per-board warning deduplication across repeated ticks/two boards.
7. Stable slug across board rename and colliding names; two project subdirectories
   share a repo lock without sharing output; overlapping roots and reparse points
   refuse before writes. Target/deletion guard preserves recoverability.
8. Ignore setup preserves user content, is idempotent, covers fresh project APIs
   and ProjectSetup, never runs on tick; .gitignore does not mask tracked/staged
   residue. Publication respects existing ignores, never force-adds. Multiple
   board exceptions do not expose a suppressed card through the writer.
9. CLI and UI acceptance in D-8: output reason/path/configured visibility, clear
   distinction between saved/eligible/written, no private text in stdout/JSON,
   explicit note-read errors cannot destroy notes, token and validation behavior.
10. A fresh DB restored from a backup retains notes and privacy settings; a fresh
    clone/default DB never reconstructs private notes from docs/cards. Test a
    migration/DB round-trip, not a live operator backup with real data.

Existing test anchors: `CardTaskFileRendererTests`, `CardTaskFileServiceTests`,
`CardFileSyncEndpointTests` and `CardFileSyncDisabledEndpointTests`,
`ProjectServiceTests`, `ProjectSetupServiceTests`, `ProjectReadinessTests`,
`ProjectDeletionTests`, `BoardProjectArchiveApiTests`; client CardEditModal,
CardModal, CardHistory, BoardPage, ProjectConfig and ProjectSetupModal tests.
Add focused `CardFilePolicyTests`, `CardPrivateNotesApiTests`,
`CardFilePrivacySyncTests`, `CardFileIgnoreTests`, `CardFilePolicyApiTests` and
`Scripts/CardFilePrivacyScriptTests` rather than hiding privacy cases in unrelated
large classes. Names can be adjusted by TestDesign to the actual test ownership.

Verification constraints: use `ScratchGitRepo` and owned test DB rows, never
production project paths or runner 17204. Add the assembly's
`ParallelLimiter<ProcessSpawnLimit>` to new process-spawning classes; a global
SyncAll test also needs unkeyed NotInParallel. WebApplicationFactory uses
AntiphonWebAppFactory/ProductionRunnerGuard. This supersedes CARD-0004's outdated
"git children need no limiter" and grouped-sweep advice.

Run named classes with, for example:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c408/ -- --treenode-filter "/*/*/CardFilePrivacySyncTests/*"
pwsh -File scripts/test-client.ps1 CardEditModal.test
```

TestDesign must name the remaining exact class/file filters, controls and expected
outcomes; no namespace-wide default. Run client tests via the wrapper; compile
client after UI changes; rebuild dist before any scoped E2E. No live git/gh/API
publication probe is necessary to prove a configured policy. Keep all checks
foreground and clean only verified worktree-contained bin-c408 directories.

## Rollout and related-card boundaries

1. Finish Code, TestDesign controls and separate Review. Land/deploy through the
   normal Antiphon pipeline. Keep AutoCommit=false. On deployment, existing boards
   backfill to off and projects to Unknown; tell the owner exports are now blocked
   and owned legacy directories may be removed by reconciliation. This is an
   intentional behavior change, not data loss: the database/revisions remain.
2. CARD-0409 owns inspecting/categorizing/redacting the already-on-disk Antiphon
   material, applying corrections to source cards, old/orphan directories, and
   deciding the live repo's interim ignore disposition. CARD-0408 supplies the
   mechanism for removal and future private detail; it does **not** read/review the
   409 files, infer they are safe, rewrite git history, or duplicate that sweep.
3. After CARD-0409's sweep is complete, the owner assigns configured repository
   visibility and opts in only the intended board(s). Existing mixed cards can
   be corrected atomically with Description + PrivateNotes; whole cards can be
   Private. Review board-level public-surface conventions once, including
   outcome/archive reasons, rather than reviewing every future card.
4. Run status and dry-run while the ignore protection remains. It reports eligible,
   excluded, removed and ignored paths/counts without private body previews. Make
   the intentional `.gitignore` board exceptions and commit that config through
   the owner's normal workflow; then run an actual manual sync with AutoCommit
   still false. Resolve cleanup/unknown/ignore/git warnings and check the public
   projection. This is initial setup after the separate historical sweep.
5. The owner can now set AutoCommit=true in the intended configuration and restart
   from the main checkout only. Verify loaded configuration directly and inspect
   the next generated commit's paths/content; health alone is not evidence. Other
   off/Unknown boards remain unable to publish even though AutoCommit is global.
   No per-card privacy review or explicit Public marking is needed for future
   Inherit cards on the deliberately publishable board. The writer never pushes;
   its commits become public on a later normal push.
6. Disable board sync to revoke future export, then reconcile removal. Feature
   Enabled=false freezes all I/O and must not be presented as erasure. Keep a
   private database backup: CARD-0088's existing backup/restore flow is how notes,
   revisions and publication policy survive a machine move. A fresh clone gets
   the safe ignore default and only intentionally published files, not those
   private records. Do not add a backup service, memory store, scheduled task or
   provisioning script under this card.

No unresolved product decision blocks TestDesign. The configured-visibility
choice and the inherited-public meaning of board opt-in are deliberate defaults,
not work deferred to Code. Future live visibility detection, tracker-wide privacy,
credential storage, history erasure and arbitrary third-party writer races are
outside this mechanism's guarantee.

## Verification design

Date: 2026-09-06. Stage: TestDesign. Task: `8c839a5e`. Inspected code and plan:
`bf5a4219a9450597506de7cf9938e6346731f89e`. This is a verification specification,
not implemented tests or a claim that the mechanism already passes. The original
plan above is unchanged. Names below are the required new test names; parameter
rows count as distinct cases. V items are acceptance evidence, R items explain
the regressions they catch, and PC items are deliberately broken production guards.

**Stage verdict: return to Plan for A-1 through A-7 before Code.** The safety
oracles are concrete, but those gaps prevent exact API/CLI tests from being
implemented without choosing a contract. Each gap includes a proposed resolution;
these proposals are not silently substituted for the plan. No live-data review or
new product preference is needed to complete this TestDesign deliverable.

### Proves it works now

#### Grounding and existing tests to adapt

Paths in this subsection are repository-relative. Test classes are under
`tests/Antiphon.Tests/Application/` unless another directory is named.

| Code inspected | Current behavior the tests must distinguish from the new contract |
|---|---|
| `server/Application/Services/CardTaskFileService.cs`: `SyncBoardAsync` | Loads board and all cards before `TryEnterAsync`; early returns for archive/no cards; constructs filenames and both render calls from whole `Card` entities. A successful policy mock alone cannot establish privacy. |
| Same file: `SyncAllAsync`, `UniqueBoardSlugAsync` | Sweep excludes archived board/project rows; exceptions are logged but produce no result; slug is recalculated from names. Test actual cleanup candidates, failure visibility and persisted ownership. |
| Same file: `CommitAsync`, `RunGitAsync` | Both add and `commit --only` receive the board directory, and raw stderr becomes Error. Existing `Unrelated_staged_file_stays_staged_and_is_absent_from_the_sync_commit` only covers a source file outside that directory. It does not catch the flagged commit-path guard issue. |
| `server/Application/Services/CardTaskFileRenderer.cs`: `RenderCard`, `RenderIndex` | Both accept entity data. Description, outcome, archive actor/reason and tracker metadata are public output. INDEX computes its counts from its input collection. Tests must check projection, names and counts as well as body bytes. |
| `server/Application/Settings/CardFileSyncSettings.cs` | Enabled=true, AutoCommit=false, IntervalSeconds=60; IntervalSeconds=0 means manual only. False AutoCommit currently still writes. Keep these feature defaults separate from board opt-in=false. |
| `server/Infrastructure/Orchestration/CardTaskFileSyncHostedService.cs` | Uses real `PeriodicTimer`, floor five seconds, fresh scope and `SyncAllAsync`; disabled/manual-only exits. No save-triggered sync. Exercise this driver as well as the service. |
| `server/Api/Endpoints/CardFileSyncEndpoints.cs` | One POST route; checks Enabled before board lookup, then returns 200 with the service result. Disabled unknown-board request is already pinned as 409. Status/settings are new routes, not an existing board PATCH. |
| `CardTaskFileSyncGate.cs` | Case-insensitive normalized local-path lock, zero-wait acquisition, repo-only warning cache. It does not yet canonicalize two project subdirectories to one Git index. |
| `CardService.cs`, `CardRevisionLog.cs`, `BoardDtos.cs`, `CardEndpoints.cs` | Content PATCH validates, loads, checks token, snapshots OLD values, trims public Description, saves and emits IDs. New notes must preserve whitespace and join that atomic write without entering ordinary DTOs. Identifier reads use the existing resolver. |
| `ProjectService.cs`, `ProjectSetupService.cs`, `ProjectSetupDtos.cs` | Setup delegates creation to ProjectService/BoardService; readiness uses typed checks; setup has `notes`, plain ProjectDto has no warning collection. Neither path currently installs this ignore block. |
| `scripts/card.ps1`, `Scripts/CardDiagnoseScriptTests.cs` | Script has a UTF-8 JSON byte path, file text reader, fresh-token lookup and a real-process/loopback-stub test pattern. `new`/`edit` currently lack their own JSON-only response branch. |
| `client/src/api/{boards,projects}.ts`, board/settings components, `hooks/useSignalRInvalidation.ts` | Separate public description/edit/history surfaces already exist. BoardChanged does not currently invalidate project/status queries; CardChanged does not explicitly invalidate a notes query. |

Retain the renderer's escaping, LF, author/review, archive and ordering tests.
Change their input fixtures to the public projection; do not reintroduce a Card
renderer overload for test convenience. In particular,
`Index_orders_a_later_placed_card_ahead_of_an_older_same_rank_card` must remain green.
The S3 replacement for `Archived_board_and_archived_project_skip_without_touching_disk`
must seed old exports and expect removal. Replace the archived exclusion in
`SyncAllAsync_syncs_the_pathed_project_reports_pathless_and_skips_archived` with
cleanup assertions. Existing publication tests must explicitly opt in, configure
visibility and use an allowed ignore policy; do not make safe defaults permissive
in a shared fixture merely to rescue old tests.

#### Contract gaps for Plan, with proposed resolutions

| ID | Exact ambiguity / conflict | Proposed resolution for Plan to adopt or replace; affected evidence |
|---|---|---|
| A-1 | D-6 says "Unknown/no-path values use null paths" but also says directory is null only without a safe resolved target; D-4/D-8 require Unknown notices to identify the configured target. Visibility Unknown does not make a known local path unknown. | Keep a safely resolved `repositoryPath` and `directory` for Unknown visibility, with `relativeFile=null` and eligibility=false; use null paths only for unavailable/unsafe targets. Keep configured visibility separate from path usability. Pin exact Unknown CLI target text after this decision. V-3, V-15, V-32. |
| A-2 | D-6 does not specify PUT-settings success body/status, the type/fields of sync-result `policy`, or board/card `reason` when eligible, empty or ignored. D-2 leaves revision presence-field name/nullability open. Project create must return a setup warning, but ProjectDto has no warning slot; readiness check key/level are unnamed. | Recommend PUT 200 returning board status; `policy` uses that same status DTO; `reason=null` when permitted, `card_private` for a suppressed single card, `no_publishable_cards` for an opted-in board with zero eligible cards, and `card_file_path_ignored` for effective ignore refusal. Recommend `CardRevisionDto.hasPrivateNotes: bool?` (null for unknown snapshots, false for known empty), `ProjectDto.cardFileWarnings: string[]`, readiness key `card-files` with Recommended/Warning for missing protection or residue, leaving `canDispatch` unchanged. Specify `ignored` for an empty desired set and whether a no-card board is `eligible`; neither is inferable from counts. V-6, V-9, V-15, V-28, V-35. |
| A-3 | D-2 only says invalid card enums are rejected; D-4 explicitly promises project-enum 422. `Program.cs` uses `JsonStringEnumConverter(... allowIntegerValues:false)`, so invalid JSON enum tokens can fail binding before service validation (400). ValidationException uses caller-provided error-key casing; notes error casing is unspecified. | Recommend 422 `validation_failed` with camelCase `errors.privateNotes`, `errors.cardFileVisibility`, `errors.repositoryVisibility`, `errors.syncCardFiles` and `errors.expectedSyncCardFiles` for semantically invalid supplied fields. Keep malformed JSON/wrong JSON primitive types 400. Explicitly settle numeric enum tokens (including 0), numeric strings, empty string and unknown names, with no fallback to Inherit/Unknown. Do not change all enums globally just to satisfy these routes. V-7, V-12, V-14, V-33. |
| A-4 | D-5 specifies operational git failure as a 200 result, but not filesystem write/delete failure. It says a failed board is visible "as a result/log", whereas D-6 requires consumers to inspect results. Lock behavior for privacy saves and cancellation is not specified; only concurrent sync has a defined 409. | Recommend wait-with-cancellation for privacy saves, while manual/tick sync retains try-enter and 409 `card_file_sync_running`; check card tokens after waiting. Recommend a failed reconcile returns HTTP 200/result with `error`, warning `card_file_io_error`, removalPending=true when applicable, and no new writes/commit until retry. Decide the exact WriteSkipReason/CommitSkipReason for I/O failure and a sweep's per-board failure entry. Never return success-looking empty results. V-16, V-19, V-20, V-24. |
| A-5 | D-5 says removal stages/commits on the next successful reconcile, but AutoCommit=false normally performs neither. `removalPending` is described by "old exports remain" without defining worktree versus index/HEAD state. Cleanup guards mention staged paths but do not distinguish a staged addition from a safe staged deletion. | Recommend AutoCommit=false still deletes working files but never stages or commits. Keep removalPending true while HEAD/index still contains an unpublished export that needs owner action; report it separately from write refusal. With AutoCommit=true it clears only after successful removal/unstaging; a canceled staged-only addition needs no commit. Explicitly decide when path reassignment/hard deletion becomes allowed with unstaged or staged deletions and how a fresh service reconstructs failure/pending state. V-17, V-18, V-20, V-23, V-26, V-32. |
| A-6 | D-7 says ignore installation occurs "before allowing any board opt-in", while D-6's named enable refusals cover only Unknown/no usable target. Missing protection on an existing configured project has no specified settings response. Enabling an already effectively ignored board is permitted for dry-run review. | Recommend missing/malformed protection gives 409 `card_file_policy_refused` before enabling, with `card_files_ignore_missing` in warnings; effective existing user protection satisfies setup. A protected board may opt in without altering ignores; sync still refuses new writes until owner adds a board exception. Specify whether this recommendation also applies to explicit true on CreateBoardRequest and how intentional removal of an ignore is distinguished from failed setup. V-13, V-27, V-28. |
| A-7 | D-3 calls its projection a whitelist but omits `Position`; the actual renderer delegates ranking to `CardRanking.OrderKey(Card, now)`, which reads Position. A whitelist without that field cannot retain the existing pinned ordering. | Include `Position` (and any needed precomputed review/sort values) as internal projection inputs only, not new frontmatter. Reuse the scalar ranking overload; never pass Card/ExternalIssueRef entity references through the projection. V-4, V-5. |

Plan should record explicit answers here or in an addendum, then hand this spec
to Code. These are not reasons to weaken the already specified assertions below.
In particular Unknown BLOCKS, Public cannot bypass board-off, PrivateNotes never
enter generated data, and directory-wide commits are forbidden regardless of
which response-shape clarification is chosen.

#### Fixtures and evidence rules

- Use synthetic `C408_PUBLIC_BODY`, `C408_NOTE_OLD`, `C408_NOTE_CURRENT`, and
  `C408_PRIVATE_CARD_*` sentinels with a per-test suffix. Keep old/current note
  sentinels exclusively in PrivateNotes/snapshot fields. A separate contact-like
  synthetic marker may move from Description to notes for the atomic-edit case;
  that marker's old public Description legitimately remains in database history.
- Service integration uses owned TestDbFixture rows and `ScratchGitRepo`, with
  a seed HEAD created BEFORE staging any contaminants. `ScratchGitRepo.CommitFileAsync`
  calls `git add .`; do not use it once unrelated/staged test data exists. Use
  explicit fixture Git paths thereafter. Never use the checked-out repository
  or live project roots as a sync target. Configure only `.invalid` remotes.
- New process-spawning classes, including real Git/PowerShell classes, carry
  `[ParallelLimiter<ProcessSpawnLimit>]` from this assembly. Add it to the existing
  CardTaskFileServiceTests when adapting that class. Give a whole-repo sweep test
  unkeyed `[NotInParallel]`; every shared-database assertion also filters the
  test's project/board/card IDs. Web tests extend AntiphonWebAppFactory and retain
  ProductionRunnerGuard/RefusingSessionRunnerClient. Set tick interval 0 except
  in the isolated hosted-driver test. Never point at runner 17204.
- Use the planned `ICardFileRepository` seam for deterministic file failures,
  command recording and barriers. Inspect the EF command/projection for sync
  only: it must not select PrivateNotes or revision bodies. Test native Git
  index/commit behavior with real Git as well; a mock command list is insufficient.
- Capture generated file **bytes and names**, INDEX rows/counts/groups, current
  Git tree/index and new commit messages/blobs, response JSON and structured
  logs. Search raw and decoded JSON, and use exact public expected bytes where
  possible so escaping is not a way to hide a sentinel leak. Assert no synthetic
  note marker at these boundaries. No filename/stub/hash/count contribution from
  a Private card. API-only excluded counts and hasPrivateNotes are permitted.
- Capture HEAD, `git status --porcelain=v1 -z`, `git ls-files --stage -z`, and
  per-file hashes; `git diff --cached --binary` and `git diff-tree --no-commit-id
  --name-status -r -z --no-renames HEAD` prove the actual changed set. Use
  `git show HEAD:<path>` for public bytes and `git show :<path>` for index bytes.
  Existing history may contain seeded formerly public text; assert absence in
  the NEW tree/commits, not retroactive erasure from all Git objects.
- Byte-idempotence cases hold public inputs, rank/urgency time and ignore policy
  fixed. Use no near-threshold due dates. If the hosted driver gains a TimeProvider
  seam, use an auto-advancing timer-capable provider confined to this driver;
  never freeze the whole server graph. For races use awaited barriers, separate
  DbContexts and a shared gate, not sleeps or probabilistic repetition.
- A baseline pass cannot pass on the strength of zero selected tests. Report
  named tests and case counts for each command and PC run. Compile errors do not
  count as a successful positive control.

#### Defaults, projection and private storage (S1/S2)

| ID | Layer / concrete test name(s) | Setup, action and exact oracle |
|---|---|---|
| V-1 | unit: `CardFilePolicyTests.Defaults_are_board_off_card_Inherit_notes_empty_repository_Unknown`; integration: `CardFilePrivacyMigrationTests.Upgrade_does_not_grandfather_existing_boards_or_copy_descriptions` | Pin entity/request/config defaults: false/Inherit/empty/Unknown, internal ownership fields null, Enabled=true/AutoCommit=false/60. In a NEW disposable test database migrate to the predecessor migration, insert old board/card/ContentEdit rows, migrate forward, and assert backfills plus null historical notes/visibility. Existing synthetic markdown and `.gitignore` hashes do not change from migration alone. Never downgrade the shared database or its template. |
| V-2 | unit: `CardFilePolicyTests.Board_repository_card_policy_matrix`; integration: `CardFilePrivacySyncTests.Board_off_never_writes_even_for_explicit_Public_card` | Enumerate 2 board flags x 3 repository values x 3 card overrides = 18 cases with Enabled=true, active board/project and usable allowed target. Board=false always denies `board_not_opted_in`; board=true + Unknown always denies `repository_visibility_unknown`; known + Private denies card; known + Inherit/Public permits. Exactly four permitted rows (two known repositories x two eligible card values). For board-off repeat AutoCommit false/true and manual/dry-run/sweep: Written=0, no newly created docs/cards tree, unchanged HEAD/index. |
| V-3 | integration: `CardFilePrivacySyncTests.Unknown_repository_visibility_with_remote_present_BLOCKS_publication` | Seed an opted-in board with both Inherit and Public cards, usable scratch Git repo, allowed ignore policy and remote `https://example.invalid/c408.git`; configured visibility remains Unknown. For AutoCommit=false and true, manual/dry-run/SyncAll must return **WriteSkipReason == "repository_visibility_unknown", Written == 0, CommitSha == null, eligibleCards == 0, excludedCards == 2**; no directory, filenames or INDEX, HEAD/index unchanged. A remote, GitHubIntegrationEnabled=true or unavailable provider credentials never turns this into a publish path. D-4 uses configured visibility: assert no gh/provider request occurs. There is no gh-failure branch to mock into production in this slice; a failed future detector must leave this same Unknown input. |
| V-4 | unit: `CardTaskFileRendererTests.Public_projection_has_no_entity_or_private_note_input`; integration: `CardFilePrivacySyncTests.PrivateNotes_with_template_markers_and_frontmatter_delimiters_never_render` | Assert renderer/index signatures accept only the immutable public projection, with no Card/CardRevision/object/entity navigation/private-note field reachable through it. Create eligible public text containing literal `{{PrivateNotes}}`, `${PrivateNotes}`, frontmatter delimiters, fenced sections, quotes, pipes and CRLF; notes contain distinct old/current sentinels and `---\nprivateNotes:`/fence-closing text. Sync with AutoCommit=true and decode every new blob/index/log. Public markers remain literal; both note sentinels are absent in bytes, names, metadata and commit subject/body. A sync SELECT interceptor rejects any PrivateNotes/revision-body selection. Check exact expected public output, not merely that a regex redacts a marker. |
| V-5 | unit: existing `CardTaskFileRendererTests` formatting/order tests; integration: `CardFilePrivacySyncTests.Private_card_contributes_no_filename_index_count_or_metadata`; `Private_note_only_edit_keeps_all_generated_bytes_and_HEAD` | Mixed board has one public card and one Private card with markers in title, alias, description, labels, external fields, archive fields and outcome. INDEX reports only the public card and its archive/group totals. Change override Public -> Private -> Inherit on an opted-in known repo and verify removal/reappearance; on an off board all remain absent. Edit only notes twice through CardService, retaining old/current history; no file hash, index blob or HEAD change. Public eligible archived cards still render archive/outcome fields; runtime fields remain excluded. Preserve Position-based ordering per A-7. |
| V-6 | integration: `CardPrivateNotesApiTests.Create_and_atomic_public_to_private_edit_preserve_notes_and_history` | POST /api/boards/{id}/cards returns 201 CardDto with hasPrivateNotes/visibility/status but no note text. PATCH /api/cards/{id}/content returns 200. A single correction replaces Description and stores notes, rotates token once, adds one ContentEdit snapshot of superseded notes/visibility/public fields with the existing reason/author/sequence. Current explicit read returns current text/revisionNumber=null; historical read at that new revision returns OLD text. Inject save failure: neither field nor token/revision persists; no event. |
| V-7 | integration: `CardPrivateNotesApiTests.Omitted_null_empty_and_limit_inputs_have_distinct_semantics`; `Invalid_card_file_visibility_never_falls_back_to_Inherit` | On edit omitted/null notes and visibility preserve; empty notes clears; whitespace-only notes stay whitespace. On create omitted/null notes become empty and visibility Inherit. Preserve leading/trailing spaces, LF, Unicode and shell metacharacters exactly. 20,000 .NET UTF-16 code units accepted, 20,001 rejected without echoing content; include supplementary Unicode to pin code-unit counting consistently with existing string limits. Test all valid enum strings and invalid integer 0/99, numeric string, unknown name, empty string; mutation/readback must not occur on rejection. Final status/error keys require A-3. Limits route returns maxPrivateNotesLength=20,000 and the three named visibility values. |
| V-8 | integration: `CardPrivateNotesApiTests.Stale_token_loses_no_notes_and_allocates_no_revision`; `Concurrent_note_edits_have_one_winner_after_gate_acquisition` | Two scoped contexts edit using one token. Exactly one 200, one existing 409 conflict, one new revision and one CardChanged; winner text/token survive. Empty Guid remains 422. Force the loser to wait before gate/token check so pre-gate cached entity state would fail this test. Include public-to-private atomic content correction. |
| V-9 | integration: `CardPrivateNotesApiTests.Explicit_note_read_uses_identifier_scope_no_store_and_snapshot_kind` | Dedicated GET works with GUID and normal CARD identifier, board/cwd/task scope; existing collision is 409 and wrong board/missing card 404. Current response has only cardId/privateNotes/concurrencyToken/revisionNumber, Cache-Control:no-store. Missing revision and every non-ContentEdit kind return 404. Old ContentEdit with null snapshot returns privateNotes=null; known empty returns empty. Cross-board same revision number cannot fetch another card. Malformed revisionNumber handling must follow the clarified validation contract. |
| V-10 | integration: `CardPrivateNotesApiTests.Ordinary_read_write_and_event_boundaries_never_serialize_note_text`; `CardPrivateNotesBoundaryTests.Tracker_diagnostics_and_launch_composition_exclude_current_and_historical_notes` | Through real public DTO mapping inspect card GET, board summary/detail, GET /api/cards?boardId=..., thread, revisions, create/edit/move responses and ID-only BoardChanged/CardChanged. Only explicit note-read DTO contains notes. Capture actual tracker adapter payloads using the patterns in CardServiceTrackerPushTests/TrackerCardStatePushServiceTests, and task/card launch composition at a fake runner/event bus using AgentTaskCardBindingTests/AgentSystemPromptLaunchTests harness conventions. Assert notes do not enter requests/prompts/briefs/diagnostics/logs even when notes ask for inclusion. No real agent/tracker launch. Public Description/outcome behavior is unchanged; this is not a tracker ACL test. |
| V-11 | integration: `CardFilePrivacyDefaultsTests.All_card_and_board_creation_paths_keep_safe_defaults`; `CardFilePrivacyMigrationTests.Database_round_trip_retains_private_fields_but_fresh_database_does_not_import_markdown` | Exercise ordinary BoardService/CardService creation, ProjectSetup board creation, tracker import (`ExternalTrackerSyncService` path), database seeder and scheduled/automated creation paths that construct Card/Board. Each omitted policy field stays false/Inherit/empty/Unknown; cover fresh UI/CLI payloads in V-31/V-34. Round-trip synthetic notes, snapshots and settings through PostgreSQL in fresh contexts, and a disposable DB copy/restore fixture; confirm explicit notes endpoint after round-trip. A separate fresh migrated/seeded DB with synthetic docs/cards does not reconstruct cards/notes/policies from markdown. This adds persistence coverage to CARD-0088, not a backup implementation. |

#### API policy and status (S2)

Use raw HTTP JSON for enum/missing-property cases; serializing a typed valid
request cannot prove invalid binding. Existing exact refusal contracts below
are mandatory; A items only qualify the additional unspecified fields.

| ID | Layer / concrete test name(s) | Setup, action and exact oracle |
|---|---|---|
| V-12 | integration: `CardFilePolicyApiTests.Settings_require_both_fields_and_compare_expected_value_under_gate` | PUT /api/boards/{id}/card-files/settings missing either syncCardFiles or expectedSyncCardFiles returns 422, no save/event. A stale expected boolean returns 409 code=card_file_policy_changed; matching expected state changes only that board flag and emits IDs. A repeated current-value request is idempotent. No renderer, sync, agent, file, ignore, stage or commit call occurs. Success body/status and field-error shape require A-2/A-3. |
| V-13 | integration: `CardFilePolicyApiTests.Enabling_Unknown_or_unusable_target_is_409_before_save`; `Public_opt_in_warns_and_disabling_missing_target_remains_possible` | Enabling Unknown, missing path, missing directory or non-Git target gives 409 card_file_policy_refused, flag remains false. Valid configured Public enables with public_repository warning, no extra confirmation action. Equivalent explicit true on board creation uses identical validation and leaves no half-created board on failure. When a previously enabled target disappears, disabling still succeeds and reports pending/unavailable status. Ignore prerequisites require A-6. |
| V-14 | integration: `CardFilePolicyApiTests.Project_visibility_defaults_preserves_omission_and_resets_on_target_change` | Create project omitting visibility -> Unknown. PUT omitting/null preserves the current value when URL/path unchanged. Changing either URL or LocalRepositoryPath resets Unknown unless request explicitly sets new valid visibility; normalize equivalent path spelling so a non-change is not a reset. Invalid values reject atomically, expected 422 for project enum per D-4/A-3. Target cleanup refusal (V-26) must roll back both path and visibility. Old client cannot reset permission accidentally. |
| V-15 | integration: `CardFilePolicyApiTests.Status_and_create_response_describe_policy_without_publishing`; `Create_status_failure_preserves_201_and_returns_status_unavailable` | GET board status 200 is read-only with Enabled either value; nonexistent board 404. Verify every D-6 property, enum casing, absolute safe root, project-relative directory, autoCommit/interval, warnings and nullable relativeFile for suppressed cards; no remote URL/notes. Create and content PATCH return 201/200 even for board-off/Unknown/ignored targets. Inject status I/O failure AFTER save: eligible=false/reason=status_unavailable/warning, no duplicate create or rollback claim. Bulk reads omit cardFileStatus and make zero per-card repository probes. A-1/A-2 settle Unknown paths/eligible reason/presence shapes. |
| V-16 | integration: `CardFileSyncEndpointTests.Policy_refusal_is_200_with_zero_writes_and_allows_cleanup_counts`; `Concurrent_sync_returns_409_without_mutation`; existing disabled/unknown tests | POST sync: unknown existing-enabled-feature board ID -> 404; feature-disabled real AND dry-run requests -> 409 card_file_sync_disabled before lookup, including unknown IDs. Busy repo -> 409 card_file_sync_running. Board-off/Unknown/all-private -> 200 with the respective WriteSkipReason (board_not_opted_in/repository_visibility_unknown/no_publishable_cards), Written=0; Deleted may be positive on real/dry cleanup. Safe resolved directory is returned on refusal per A-1. Unsafe path -> 409 unsafe_card_file_path; overlapping ownership -> 409 card_file_directory_conflict, both before mutation. Operational git failure -> 200, CommitSha=null, CommitSkipReason=git_error, sanitized Error/warning. A-4 settles filesystem failures. |

#### Reconciliation, concurrency and Git (S3)

| ID | Layer / concrete test name(s) | Setup, action and exact oracle |
|---|---|---|
| V-17 | integration: `CardFilePrivacySyncTests.Revocation_removes_previously_exported_files_and_last_public_index`; `First_post_migration_sync_with_AutoCommit_true_only_removes_unpublished_exports` | Start with actual successful public sync (card+INDEX), then separately card Private, board off, project Unknown, board archive, project archive, hard removal of last card in fixture, or all cards Private. Reconcile each through manual and sweep: old top-level managed markdown absent, Written=0 for whole-board suppression, obsolete INDEX absent, unrelated `.txt`/nested/sibling files byte-identical. Individual eligible card archive keeps its file. Repeat AutoCommit=false/true; true commits tracked deletions with no new private/public additions for blocked board. Seed legacy files with unpinned old rows to model the first migrated pass; no automatic grandfathering. |
| V-18 | integration: `CardFilePrivacySyncTests.Enabled_false_freezes_IO_and_does_not_claim_erasure` | Seed exported files and set board off then feature Enabled=false. Both direct service path and tick perform zero mutations (DB ownership, files, ignore, staging, commits); endpoint refusal remains V-16. GET status still reports exports/pending and explicitly says disabled feature does not erase files. Re-enable feature while board stays off; cleanup then succeeds. A-5 fixes pending fields after successful disk-only cleanup. |
| V-19 | integration: `CardFilePrivacyConcurrencyTests.Sync_after_completed_revocation_never_uses_pre_gate_snapshot`; `Revocation_waits_for_inflight_sync_then_next_sync_removes_export`; `Concurrent_creates_and_content_edits_serialize_with_publication` | Use barriers at gate acquisition and snapshot load, two DbContexts and one canonical gate. Order 1: hold the gate for a privacy save, persist card Private/board off/project Unknown, release, then admit sync; it must load NEW state and never write/commit that card. If concurrent sync uses try-enter it first gets 409 and retry after save has the same oracle. Order 2: sync owns gate and pauses before commit; privacy save cannot complete until it exits, then reports removal pending and next sync deletes. Parameterize policy edits, content correction and eligible create. Test cancellation releases leases and subsequent work completes. No claim of immediate erasure on a save or protection against arbitrary external writers. A-4 fixes save contention response. |
| V-20 | integration: `CardFilePrivacyRecoveryTests.Delete_failure_keeps_removal_pending_and_blocks_new_content_until_retry`; `Restart_after_pin_write_delete_or_stage_reconciles_from_current_DB` | Throw deterministically before delete, after ownership SaveChanges, after first public write, after delete, and after staging/before commit. Dispose service/context, instantiate fresh ones; retries use pinned old target and latest policy, never old revision text. For failed removal, add a new eligible sibling card: it must not be published while stale unpublished residue remains. Once fault is removed, delete stale files/index, publish only current eligible cards and recover commit/pending state. Preserve unrelated bytes/index throughout. Cancellation likewise releases gate. A-4/A-5 must define durable failure detection, error result and pending semantics. |
| V-21 | integration: `CardFilePrivacyGitTests.AutoCommit_stages_and_commits_only_exact_generated_paths_never_directory` | Seed HEAD; stage modifications to an unrelated source file, `.gitignore`, `docs/cards/board/notes.txt`, `docs/cards/board/nested/keep.md`, another board's markdown, and `docs/cards/board-other/keep.md`; also leave untracked contaminants under the board. Run opted-in allowed sync with AutoCommit=true. Actual new commit path set equals desired generated files plus obsolete top-level managed markdown deletions, EXACTLY. All excluded index entries/blob IDs and working bytes remain as before; untracked files stay untracked. No bare commit, `git add .`, directory pathspec, `-f`, push, stash or reset. Assert both recorded add/commit path lists and `git diff-tree`/HEAD blobs. This specifically closes the Plan-stage commit-path guard issue. |
| V-22 | integration: `CardFilePrivacyGitTests.Staged_generated_body_is_replaced_with_current_public_projection`; `Changed_generated_bytes_before_commit_refuse_with_generated_files_changed` | Stage a generated version containing an old synthetic disclosable body; update DB public text and place sensitive detail only in PrivateNotes. New index/HEAD body must equal current public projection, not old staged bytes. Inject a byte change after reconcile/stage but before commit recheck: CommitSha=null, CommitSkipReason=generated_files_changed, HEAD unchanged, next clean reconcile commits current public bytes. Test missing expected file and reappearing deleted file too. Unrelated staged data remains intact. |
| V-23 | integration: `CardFilePrivacyGitTests.Index_only_private_addition_is_unstaged_without_nonexistent_commit_path`; `Tracked_stale_file_is_deleted_when_entire_working_directory_is_absent` | Seed an unpublished top-level file as an index-only addition (never in HEAD), remove its working file/directory, revoke and sync AutoCommit=true. Index entry disappears; no invalid commit path is passed; no commit is required when this was the only change. Separately seed a HEAD-tracked stale file whose directory is absent: commit deletion, new HEAD has no path. Include both cases together with a desired public file and unrelated staged source edit. A-5 specifies AutoCommit=false status/cleanup guard behavior. |
| V-24 | integration: `CardFilePrivacyHostedServiceTests.Tick_manual_and_dry_run_share_policy_and_cleanup_results`; `Sweep_reports_failed_board_and_continues_to_other_owned_board`; unit: `CardFilePolicyTests.Warning_dedup_is_per_board_target_reason_and_dry_run_does_not_change_it` | Use equivalent cloned fixture states for dry/manual/driver; compare board-specific eligible/excluded/policy reasons and prospective Written/Deleted/Unchanged, allowing commit-specific fields to differ. Global SyncAll includes off/archived/empty cleanup candidates. One board's failure does not suppress another's success. Driver Enabled=false/interval=0 resolves no sync; enabled driver produces a real sweep via controlled timer or one bounded floor-5s tick. Alternate two boards with different reasons in one repo: one Warning per board/reason transition, repeated Debug, target/reason change warns again, success resets refusal transition. Dry-run must not consume the next warning. Failure-result shape requires A-4. |
| V-25 | integration: `CardFilePrivacySyncTests.Dry_run_changes_no_database_file_ignore_index_HEAD_or_warning_state` | Snapshot own DB rows including tokens/timestamps/pinned fields, directory inventory and file hashes, ignore bytes, index entries and HEAD plus gate warning-state observations. Run dry=true for eligible fresh board, off/Unknown cleanup, ignored files, archived board and stale index. All snapshots unchanged; prospective counts reflect the same policy, with Written=0 for privacy/ignore refusal and cleanup Deleted>0 as applicable. No directory creation, slug persistence, temporary body file, add, commit or implicit ignore installation. Then real run must still emit its first warning. |
| V-26 | integration: `CardFilePrivacyOwnershipTests.Rename_pins_slug_and_collisions_keep_distinct_owners`; `Subdirectory_projects_share_git_gate_but_not_generated_targets`; `Unsafe_or_overlapping_paths_refuse_before_any_mutation`; `Path_change_and_hard_delete_refuse_until_old_exports_drained` | Rename after first write: recorded slug unchanged, no orphan; title rename changes only that card's path. Colliding names/case variations stay distinct and a new collision cannot claim another board's stored directory. Two project subdirectories of one repo share gate/index but write their own docs/cards targets. Test `..`/prefix-sibling containment and reparse points/junctions at project root, docs/cards, board directory and managed file, including alias spellings: 409 named guard, no outside-target touch or DB ownership mutation. Test equal resolved board directories across projects. Old path reassignment/board or project hard delete with working or index-only residue -> 409 card_file_cleanup_required and unchanged ownership/rows; include null bookkeeping legacy probe and unreachable target. After approved cleanup semantics from A-5, retry succeeds; unrelated ProjectDeletionTests rules still apply. |
| V-27 | integration: `CardFilePrivacyGitTests.Literal_NUL_pathspec_handles_metacharacters_and_large_generated_sets`; `Git_refusal_guards_retry_without_committing_unrelated_state` | Real Git case with spaces/Unicode/brackets in allowed roots and stale top-level markdown names, plus >32,767 bytes of combined pathspec input; no wildcard/prefix overmatch, exact add AND commit lists, NUL/literal mode, no argument-length failure. Slug sanitizer prevents some metacharacters in NEW filenames; put them in legacy stale names/repository path instead of weakening sanitizer. Existing CRLF/autocrlf cases still produce LF-equivalent public blobs and no repeated commit. Parameterize rebase-merge/rebase-apply, MERGE_HEAD, CHERRY_PICK_HEAD, detached HEAD, unmerged managed paths, index.lock, timeout and cancellation: expected existing CommitSkipReason, no new commit; removal/writes obey policy; remove only fixture-owned marker and retry. Empty allowed set invokes neither add nor commit. Pure removal of disabled/archived board has neutral subject `antiphon: remove unpublished card files`, trailer antiphon=true, no board/card/note marker; eligible sync uses permitted board name only. Capture sanitized error/logs using synthetic credential-like stderr, never real secrets. |

#### Ignore, CLI, UI and owner documentation (S4-S7)

| ID | Layer / concrete test name(s) | Setup, action and exact oracle |
|---|---|---|
| V-28 | integration: `CardFileIgnoreTests.Project_create_setup_and_path_assignment_install_ignore_once`; `Existing_user_ignore_bytes_are_preserved_and_failures_are_warnings` | Fresh ProjectService create, ProjectSetup, and later path assignment on project with no opted-in boards install the exact D-7 block at the project local path (including a subdirectory project), before any publish. With a missing checkout/non-Git/pathless target the ignore helper creates neither directory nor Git repo; leave ProjectSetup's existing explicit CreateDirectory option unchanged and set it false for this case. LF/CRLF, BOM/no-BOM, no trailing newline, comments and custom patterns preserve preexisting bytes; repeated install creates no duplicate. Effective user rule satisfies protection without owned block. Malformed/duplicate owned markers and denied write preserve project creation and original ignore bytes; expose card_files_ignore_missing via A-2/A-6 warning/readiness contracts. |
| V-29 | integration: `CardFileIgnoreTests.Effective_ignore_blocks_new_writes_including_tracked_files_but_allows_removals`; `Board_exceptions_do_not_publish_private_cards_or_other_boards` | Board opted in and repo known, entire tree ignored -> 200 sync result, WriteSkipReason=card_file_path_ignored, Written=0, no new generated bytes/commit; eligibleCards still reports policy-eligible cards. Repeat dry-run. Check both untracked and already tracked generated path with git check-ignore --no-index semantics. Owner-created stable-slug exception permits eligible board only; Private card still absent, other board stays ignored/off. Effective global/info-exclude patterns also block. Never force-add or automatically remove/edit/stage an ignore. Removal of revoked files remains allowed while ignore active. |
| V-30 | integration: `CardFileIgnoreTests.Migration_tick_status_and_bootstrap_check_never_install_ignore`; `Ignore_does_not_hide_tracked_or_staged_cleanup_required` | Existing project without block: migration, repeated tick, status, dry-run and read-only bootstrap-check leave ignore bytes absent/unchanged and show remediation on diagnostic surfaces (migration itself emits no status). Non-opted-in board with tracked or staged generated file reports card_file_cleanup_required despite matching ignore; no false "safe because ignored" claim. Test using a loopback stub for bootstrap script if endpoint-backed, never the shared stack. Reassigning a clean project's path when it already has opted-in boards does not append blanket ignore or override exceptions. |
| V-31 | integration: `Scripts/CardFilePrivacyScriptTests.New_and_edit_print_server_target_policy_and_configured_visibility`; `Json_returns_one_note_free_response_object` | Run actual pwsh -NoProfile -NonInteractive -File scripts/card.ps1 against loopback stub, isolated task-token env. New board-off returns exit 0, existing summary/ID followed by exact D-8 lines: `card files  NOT WRITTEN: board_not_opted_in`, target full directory plus `(configured Public repository)`, `policy      Inherit; board sync off; AutoCommit off`, and private-notes storage/exclusion line. Configured Public prints its warning even when blocked. Eligible Inherit/Public uses `ELIGIBLE: next sync (60s)` and full predicted file path, interval 0 uses `manual sync`; configured Private is never phrased as provider-verified. -Json new/edit stdout parses as exactly one response object with no human prefix/suffix or notes; stderr contains no notes. No POST sync and no gh invocation from creation. |
| V-32 | integration: `Scripts/CardFilePrivacyScriptTests.Unknown_missing_status_and_pending_removal_never_claim_written` | Unknown prints `UNKNOWN REPOSITORY VISIBILITY: sync blocked` and refusal; target/path behavior follows A-1. Older server omitting status prints exactly `status unavailable; export safety not confirmed`. Edit with removalPending=true says removal pending, not erased. Failed status probe on successful create stays exit 0; API rejection nonzero. Assert token/id remain usable, one create call only, no hidden sync. A-2/A-5 settle exact status/reason and pending wording needed beyond D-8's sample. |
| V-33 | integration: `Scripts/CardFilePrivacyScriptTests.Private_notes_file_preserves_UTF8_without_echo_and_clear_is_explicit`; `Notes_validation_and_tokens_preserve_current_edit_contract` | Temp notes file contains leading/trailing whitespace, LF, Unicode, quotes, backticks and literal `$()`; recorded UTF-8 JSON field exactly matches it, all process output omits it. Empty file and edit -ClearPrivateNotes each send empty string; omitted field sends no change. File+clear, clear on new, unsupported inline -PrivateNotes, missing file, invalid visibility and over-limit notes fail locally/nonzero before mutation request; error reports length/limit only. Use /limits=20,000 and boundary inputs. Existing get/history responses contain no note text. Ordinary edit uses freshly fetched token; -Token sends literal supplied token and a stub 409 stays nonzero without retrying a changed write. |
| V-34 | unit (Vitest): `BoardPage.test.tsx` / `CardModal.test.tsx`: `create keeps Inherit and stores private notes separately`; `CardEditModal.test.tsx`: `atomic correction preserves note whitespace and token` | All create entry points show Inherit by default and send new notes only in privateNotes, public Description separately. Edited notes/override persist and empty notes explicitly clear; title-only edit omits untouched notes. Existing reason/token/limits flow remains. Public preview has no note text. Help names title/body/outcome/archive reasons as public fields on eligible cards. Private panel label uses D-8 wording. |
| V-35 | unit (Vitest): new `BoardCardFileSettings.test.tsx`: `publishing starts off and Unknown refusal preserves state`; `public opt in covers current and future cards without an extra dialog`; existing `ProjectConfig.test.tsx`, `ProjectSetupModal.test.tsx`, `ProjectReadinessPanel.test.tsx` | Default switch off and repository select Unknown; no save/sync on mount. Explicit enable sends syncCardFiles + expectedSyncCardFiles, displays 409 stale/refused and 422 field errors while preserving editable input and refreshing current policy. Public warning, absolute target, ignore remediation, private override and removal pending are visible. Dry-run/sync 200 with WriteSkipReason is shown as refused, never success-written; counts distinguish eligible from written. Project setting omission preserves old state. A-1/A-2/A-3/A-6 fix exact DTO/labels. |
| V-36 | unit (Vitest): `CardModal.test.tsx`: `notes load only on explicit open and cannot fetch embedded resources`; `CardEditModal.test.tsx`: `failed note read never clears saved notes on unrelated edit`; `CardHistory.test.tsx`: `private snapshot is explicit and separate from public history` | Opening board/list/modal public view does not fetch notes. Opening note panel uses dedicated request; synthetic `<img src=https://example.invalid/...>`/Markdown image/script remains inert text, no embedded DOM resource load. Explicit historical snapshot is keyed by card AND revision, shows null as unknown history versus empty as empty, never inserts into public description/history preview. Network/read error leaves notes unknown, and later title edit omits privateNotes instead of sending empty. No notes in localStorage/sessionStorage/persisted query dehydration, board caches or other card's panel; switching cards cannot display stale prior notes. |
| V-37 | unit (Vitest): new `client/src/hooks/useSignalRInvalidation.test.ts`: `ID events refresh policy projects and explicit notes without carrying text` | BoardChanged with boardId and projectId variants invalidates affected board/status/project queries; CardChanged invalidates that card's explicit notes and status plus existing board/list/thread keys. Mount/refetch acceptance proves visible stale policy and note data refresh after ID events. Inspect event payloads for IDs only. Do not populate bulk caches with dedicated notes response. |
| V-38 | integration: `CardFilePrivacyDocumentationTests.Fresh_clone_ignore_and_owner_contracts_match_publication_policy`; document review | Inspect checked-in root `.gitignore` for exact managed default, and S7 owner docs for false board default, Unknown block, explicit local configured visibility, dedicated notes route, settings/sync statuses, revocation, pending/frozen semantics, exact-path commits/no push, manual ignore exceptions and public outcome/archive fields. Existing CARD-0004 guidance is superseded by a link, not left normative. CARD-0088/bootstrap retains fresh-DB versus backup/restore distinction; rollout waits for CARD-0409 cleanup before any live opt-in. Changed paths exclude generated docs/cards files and no live data is copied into fixtures. Prefer one small artifact contract test plus focused human review; do not encode prose paragraphs as brittle snapshots. |

The mandatory API status table, to avoid an HTTP-success/privacy-success mix-up:

| Operation and condition | Existing/fixed status and code | Mutation expectation |
|---|---|---|
| Create card with off/Unknown/ignored publication | 201, CardDto.cardFileStatus explains refusal | DB card saved; no sync |
| Edit content under same refusal | 200, CardDto.cardFileStatus explains refusal/pending | Atomic DB edit; no sync |
| POST real or dry sync when feature disabled | 409 `card_file_sync_disabled` | Nothing, even for unknown board ID |
| POST sync unknown board, feature enabled | 404 | Nothing |
| POST sync busy canonical repo | 409 `card_file_sync_running` | Nothing |
| POST sync policy refusal, feature enabled | 200 with WriteSkipReason | No new writes; owned removal allowed |
| POST sync unsafe/overlapping target | 409 `unsafe_card_file_path` / `card_file_directory_conflict` | No mutation |
| POST sync operational Git error | 200 with CommitSkipReason and sanitized Error/warning | Never commit; preserve retryable state |
| PUT settings missing either boolean | 422 | No save |
| PUT settings stale expected flag | 409 `card_file_policy_changed` | No save |
| Enable board with Unknown/unusable target | 409 `card_file_policy_refused` | No save |
| Change target/delete owner with outstanding exports | 409 `card_file_cleanup_required` | Old ownership/rows preserved |
| GET explicit notes missing/non-content revision | 404 | Nothing |
| Stale card content token | 409, existing conflict shape | No note loss/revision/event |

### Guards the regression

- R-1: New entity/migration/import/setup defaults accidentally opt in old or new
  work | caught by V-1/V-2/V-11 because both raw defaults and real filesystem
  absence are asserted; Public-on-off is a dedicated row.
- R-2: Treating Unknown, remote presence, provider failure or integration enabled
  as permission | caught by the EXACT V-3 BLOCKS test because it requires
  `repository_visibility_unknown`, zero writes and unchanged index/HEAD, with
  an explicit Public card and board opt-in already present.
- R-3: Passing entities to the renderer, broad serialization, template expansion
  or selecting notes for sync | caught by V-4/V-5/V-10 because both projection
  boundary and hostile-content blob/output absence are pinned.
- R-4: Filtering only card bodies, after filenames/index grouping, or letting
  Public bypass board-off | caught by V-2/V-5/V-17 because private identifiers,
  titles, group/count changes, stubs and last INDEX are all observable failures.
- R-5: Trimming notes, treating null as clear, snapshotting new values or checking
  token before acquiring the gate | caught by V-6/V-7/V-8/V-19: exact old/current
  text, one revision/event, one concurrent winner and post-save snapshot ordering.
- R-6: Returning notes through DTOs/logs/tracker/prompts or eagerly fetching/caching
  them in UI | caught by V-9/V-10/V-31/V-36/V-37, including response and cache
  contents rather than just hidden DOM text.
- R-7: Early-return revocation/archive/no-card handling or active-only sweep
  selection | caught by V-17/V-18/V-24 because seeded prior files must disappear
  while disabled-feature mode must leave them alone.
- R-8: Lost ownership/failure state on restart, slug renames, or abandoned old
  targets | caught by V-20/V-23/V-26 through fresh service instances and actual
  worktree/index residue, including null bookkeeping legacy rows.
- R-9: Reintroducing directory-wide add OR commit, dropping --only, force-add or
  a repo-wide cleanup fallback | caught by V-21/V-27 because contaminants inside
  the same directory and unrelated staged entries must remain outside HEAD.
- R-10: Reusing staged secret-bearing blobs, dropping generated-byte recheck or
  omitting index-only removals | caught by V-22/V-23 because exact index/HEAD bytes,
  absent-file cases and no-commit-path cases are checked independently.
- R-11: Locking by project subdirectory/cached snapshot, or clearing the lease
  before SaveChanges/commit | caught by V-19/V-26 barrier order and shared-index
  assertions; retries do not authorize publication from a revoked snapshot.
- R-12: Lexical-prefix containment, following junctions, broad recursive deletion
  or overlapping ownership | caught by V-26 because all outside-target bytes and
  pre-mutation ownership state are captured and must survive named 409 refusal.
- R-13: Dry-run writes bookkeeping/ignore/index/log-dedup state, or per-repo skip
  dedup hides/spams another board | caught by V-24/V-25 complete state snapshots
  and alternating board warnings.
- R-14: Mistaking ignore for protection of tracked/staged content, removing user
  rules, or treating absence of ignore as consent | caught by V-28/V-29/V-30:
  effective no-index check, residue status and preservation of original bytes.
- R-15: Treating 200 sync or 201 create as proof of exported files, stale settings
  overwrites, or status failure rolling back/retrying successful creation | caught
  by V-12/V-15/V-16/V-31/V-32/V-35 actual request counts and status messages.
- R-16: New private fields become a markdown-based backup/import, an agent memory
  feature, or an excuse to omit database preservation | caught by V-11/V-38 DB
  round-trip/fresh-DB evidence and owner-document boundary review.
- R-17: Projection drops Position, public outcome/archive text, escaping or LF
  normalization | caught by preserved renderer tests and V-4/V-5/V-27 exact bytes
  and placed-card order; privacy must not corrupt eligible public output.

### Positive controls

Code runs each control on its completed implementation: establish the named
baseline green, make the single production mutation below, build and observe
the specified assertion red, revert ONLY that mutation, rebuild and observe
the same test green. Record PC ID, actual file/line changed, selected case count,
failing assertion and restored result. Do not commit mutated guards or alter a
test expectation. A compiler error, unavailable Git/DB, timeout or zero-test run
is not the required red. Where a new seam has not yet been named, the mutation
names its exact behavioral statement, not a demand for a second implementation.

| Control | One-line production mutation | Required failing test / assertion |
|---|---|---|
| PC-1 | Flip the new Board.SyncCardFiles initializer from false to true. | V-1 `Defaults_are_board_off_card_Inherit_notes_empty_repository_Unknown`: board flag false. Separately mutate the new migration's board backfill/default to true and run V-1 upgrade case as PC-1b; entity default alone does not prove migration safety. |
| PC-2 | Remove the board-opt-in denial predicate from policy evaluation. | V-2 `Board_off_never_writes_even_for_explicit_Public_card`: zero writes and board_not_opted_in. |
| PC-3 | Change Unknown policy denial to the known/allowed branch. | V-3 `Unknown_repository_visibility_with_remote_present_BLOCKS_publication`: exact reason/zero files fails, with the repo/ignore already usable. |
| PC-4 | Make the Private-card eligibility predicate return true. | V-5 `Private_card_contributes_no_filename_index_count_or_metadata`: private filename and extra INDEX row/count fail. |
| PC-5 | At sync public-projection construction, append the synthetic card's PrivateNotes to Description. | V-4 hostile-note test: new file/HEAD bytes contain forbidden note sentinel. This mutation may require selecting notes; do not weaken the projection test to compile it. If projection is SQL-only, make this one projection expression read and append notes; the SELECT guard must also fail. |
| PC-6 | Replace the ordinary card GET return expression with the dedicated private-notes read result. | V-10 ordinary-boundaries test: raw/decoded response contains a privateNotes field/text; its API is not the explicit notes route. |
| PC-7 | Make AppendContentEdit set snapshot PrivateNotes to null instead of the superseded value. | V-6 history assertion expects C408_NOTE_OLD. |
| PC-8 | Change the private-notes update guard so a null/omitted value sets empty string. | V-7 omitted/null preservation case loses exact existing notes. |
| PC-9 | Bypass the post-gate card concurrency-token inequality rejection. | V-8 stale-token case must fail on 200, extra revision/event or lost winner text; no DB concurrency collision is needed for the sequential stale-token arm. |
| PC-10 | Remove the 20,000-code-unit notes length validator. | V-7 over-limit API case accepts 20,001 or saves it instead of rejecting; keep UI/CLI out of this control so their local validation cannot mask it. |
| PC-11 | Remove the revision-kind predicate on explicit historical notes selection. | V-9 non-ContentEdit case must return 404 instead of a 200 null snapshot. |
| PC-12 | Replace the private-revocation reconciliation path with an early skip return. | V-17 card/board revocation variant leaves old markdown/INDEX on disk. |
| PC-13 | Restore SyncAll's active-only archived board/project filter. | V-24 cleanup candidate case fails because seeded archived exports survive and the owned board result is absent. |
| PC-14 | Remove the Enabled guard in the shared reconcile core. | V-18 direct-service invocation mutates files/ownership despite Enabled=false; endpoint guard must not mask the test. |
| PC-15 | Replace the authoritative post-gate board/card load with the already-loaded pre-gate snapshot (single load assignment; use a fixture barrier that has populated that snapshot). | V-19 completed-revocation case sees stale public bytes after save. Also exercise Public->Private, not only board off. |
| PC-16 | Change gate key from canonical Git toplevel to configured project directory. | V-26 subdirectory-project case admits simultaneous owners of one index. |
| PC-17 | Ignore deletion failure and continue into new-file publication. | V-20 delete-failure case finds the newly eligible sibling's file/commit despite unresolved private residue. |
| PC-18a | Feed add the board-directory pathspec in place of the exact allowlist. | V-21 sees excluded index entries staged/altered or recorded broad pathspec; even a still-safe --only commit cannot mask unsafe staging. |
| PC-18b | Feed commit the board-directory pathspec in place of the exact commit list. | V-21 actual commit contains staged notes.txt/nested/other unapproved same-directory content. Controls a and b are separate red/revert/green runs. |
| PC-19 | Disable the working-file/projection equality check immediately before commit. | V-22 `Changed_generated_bytes_before_commit_refuse_with_generated_files_changed`: unexpected commit/blob instead of refusal. |
| PC-20a | Drop index/status-only stale paths from the generated removal allowlist. | V-23 index-only addition or directory-absent tracked case leaves unpublished index/HEAD entry. |
| PC-20b | Reuse the pre-add allowlist as the commit list without rebuilding changes against HEAD. | V-23 never-HEAD-tracked addition case gets nonexistent-path Git error instead of successful unstage/no-commit. |
| PC-21 | Remove the reparse/containment refusal before reconciling a managed path. | V-26 unsafe-path case misses named 409 or touches outside fixture target. Repeat root and managed-file parameter rows. |
| PC-22 | Bypass the outstanding-export guard on project path assignment/hard deletion. | V-26 path-change/delete case succeeds while old working/index residue and ownership must be retained. |
| PC-23 | Unconditionally save the first-pass slug during dry run. | V-25 changes CardFilesDirectorySlug/repository path or row state. Also run PC-23b removing the dry-run check before deletion: the same test loses seeded private legacy file. |
| PC-24 | Change effective ignore check to disregard ignored paths (or remove --no-index). | V-29 blocks new writes assertion fails; use tracked-path parameter for --no-index mutation. The no-force-add assertion remains independent in V-21/V-27. |
| PC-25 | Skip project-creation ignore installation. | V-28 fresh-create case lacks exact managed block/effective protection. |
| PC-26 | Suppress tracked/index residue detection when an ignore rule matches. | V-30 cleanup-required case reports safe/ready despite known tracked/staged export. |
| PC-27 | Remove the board expectedSyncCardFiles comparison. | V-12 stale expected-state test overwrites policy instead of 409 card_file_policy_changed. |
| PC-28 | Let status-probe failure escape the successfully saved card create path. | V-15 status-failure case loses 201/status_unavailable and invites retry; verify exactly one saved card. |
| PC-29 | In script status output, replace the eligible/not-written distinction with unconditional "WRITTEN". | V-31/V-32 board-off/Unknown/eligible case fails exact text; no filesystem test alone would catch this false assurance. |
| PC-30 | On private-note query error, initialize/save privateNotes as empty rather than unknown/omitted. | V-36 CardEditModal failed-read/unrelated-edit test sees privateNotes="" in the PATCH. |
| PC-31 | Render fetched private notes through a resource-loading Markdown/HTML component instead of inert text. | V-36 CardModal notes test sees image/iframe/script DOM or external resource request; sentinel in private panel is allowed but resource fetching is not. |
| PC-32 | Include the explicit notes query in persisted/dehydrated/bulk cache data. | V-36 cache-boundary test finds sentinel in serialized cache/storage or another card panel. Use the actual cache persistence boundary; do not introduce persistence only to mutate it. If there is no persistence layer, mutate the bulk-cache mapping instead. |
| PC-33 | Change policy warning-cache key from (board,target) to target only. | V-24 alternating-board case gets duplicate Warnings or suppresses the second board's first warning. |
| PC-34 | Drop literal-path handling from add/commit pathspec input. | V-27 bracketed legacy path case overmatches or misses a required literal path. Keep NUL serialization separately asserted, including its large-input case. |
| PC-35 | Flip CardFileSyncSettings.AutoCommit's default to true. | V-1 defaults assertion and existing renderer `AutoCommit_defaults_false_so_the_first_restart_cannot_commit_unreviewed` fail. |
| PC-36 | Append PrivateNotes to the content-write log message instead of logging only IDs. | V-10 captured-log assertion finds the note sentinel; a clean HTTP response cannot mask logging it. |
| PC-37 | Remove reset-to-Unknown when project URL/path changes without explicit visibility. | V-14 persists the old known value instead of Unknown. |
| PC-38 | Replace the neutral removal-only subject with the disabled board's name. | V-27 removal commit subject fails exact string and private-board-marker absence. |
| PC-39 | Bypass the specific in-progress/conflict guard in CommitAsync/repository seam, one guard at a time: rebase, merge, cherry-pick, detached HEAD and unmerged managed paths. | V-27 corresponding parameter must lose its named CommitSkipReason or make an unexpected commit. Run five separate mutations; Git itself refusing later with generic git_error is still a meaningful assertion failure, not the intended guarded result. |
| PC-40 | Remove Cache-Control:no-store from explicit notes GET. | V-9 current/history header assertion fails. |

Each PC qualifies the corresponding R guard only after the actual assertion
fails for the intended reason. A control may expose more than one assertion;
report the privacy-relevant one. Controls with a/b variants require BOTH runs.
Following A-1 through A-7 resolution, Plan must update any affected expected
response assertions before Code runs these controls; do not retrospectively
bless an observed implementation as the expected contract.

### Out of scope

- CARD-0409 owns the live historical cleanup: classify/redact already-written
  material, correct source cards, find old orphan directories and decide interim
  ignore disposition. CARD-0408 tests use synthetic owned legacy directories to
  prove future removal. They do not read or certify the real generated corpus,
  rewrite Git history, remove copied/pushed data or authorize board opt-in.
  Historical cleanup completion remains a rollout prerequisite, not a unit-test
  assertion this work can satisfy.
- CARD-0088 already owns bootstrap fresh-install versus backup/restore. V-11 is
  a synthetic migration/persistence/DB round-trip check for the new columns; it
  does not run dev-backup/dev-restore on live data or create another backup service,
  scheduler, memory store or provisioning script.
- No live GitHub/gh visibility probe: D-4 explicitly chooses operator-configured
  visibility. V-3 models an unclassified repository with a remote and fails closed.
  A future detector requires its own failure-path tests; configured Private is
  not proof of actual current provider visibility.
- No content scanner, encryption, tracker-wide ACL or new note authorization
  role. Public fields remain public on opted-in boards, even if a caller manually
  copies confidential text there. Existing card resolver/scope behavior is tested.
- No arbitrary human/editor/Git writer race guarantee, external push, shared-stack
  restart, production runner or live database access. New writes and retries
  serialize publishers and saves belonging to this mechanism only.
- No full E2E or full backend assembly is forced by this document-only stage.
  Code uses focused backend/API/process and component tests below. A scoped E2E
  may be added if a real browser-only failure cannot be covered by the existing
  component harness; first rebuild client/dist and use the isolated E2E runner.

### Cost

**TestDesign execution:** zero tests/builds run; source/fixture review and document
integrity checks only. Code must implement these names, run all V/R/PC evidence
and retain results for separate Review. The full ~25.5-minute assembly and Pty
suite are not a default verify step for this card; the current testing/build
owner supersedes older generic bundle timing/full-suite advice.

**Required backend classes/filters after implementation.** The command pattern
for EACH named class below is:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c408/ -- --treenode-filter "/*/*/CardFilePolicyTests/*"
```

Replace only `CardFilePolicyTests` with each class in this complete list, running
foreground and sequentially. This lists exact class filters, not a namespace:

| Slice / evidence | Classes |
|---|---|
| S1 defaults/projection | CardFilePolicyTests, CardFilePrivacyMigrationTests, CardFilePrivacyDefaultsTests, CardTaskFileRendererTests |
| S2 APIs/notes | CardPrivateNotesApiTests, CardPrivateNotesBoundaryTests, CardFilePolicyApiTests, CardFileSyncEndpointTests, CardFileSyncDisabledEndpointTests, CardCorrectionApiTests, CardIdentifierResolutionTests |
| S3 writer/recovery | CardFilePrivacySyncTests, CardFilePrivacyConcurrencyTests, CardFilePrivacyRecoveryTests, CardFilePrivacyGitTests, CardFilePrivacyOwnershipTests, CardFilePrivacyHostedServiceTests, CardTaskFileServiceTests, BoardProjectArchiveApiTests, ProjectDeletionTests |
| S4 setup/readiness | CardFileIgnoreTests, ProjectServiceTests, ProjectSetupServiceTests, ProjectReadinessTests |
| S5 CLI | CardFilePrivacyScriptTests (namespace Antiphon.Tests.Scripts), CardDiagnoseScriptTests |
| S7 artifact guard | CardFilePrivacyDocumentationTests |

For a PC, narrow to the exact test method, for example:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c408/ -- --treenode-filter "/*/*/CardFilePrivacySyncTests/Unknown_repository_visibility_with_remote_present_BLOCKS_publication"
```

New classes that use only the policy/renderer/fakes are Unit; native file/Git,
database/API and child-process cases are Integration. If splitting a proposed
class is necessary to keep one lane per class, record the replacement exact filter
against its V/PC IDs in the Code evidence, rather than broadening to a namespace.

**Required client commands** (wrapper propagates actual exit status):

```powershell
pwsh -File scripts/test-client.ps1 BoardPage.test
pwsh -File scripts/test-client.ps1 CardModal.test
pwsh -File scripts/test-client.ps1 CardEditModal.test
pwsh -File scripts/test-client.ps1 CardHistory.test
pwsh -File scripts/test-client.ps1 BoardCardFileSettings.test
pwsh -File scripts/test-client.ps1 ProjectConfig.test
pwsh -File scripts/test-client.ps1 ProjectSetupModal.test
pwsh -File scripts/test-client.ps1 ProjectReadinessPanel.test
pwsh -File scripts/test-client.ps1 useSignalRInvalidation.test
```

Run `npm run build` with working directory `client/` after client edits. No extra
browser bundle build is needed if that completed build is current. After controls,
run `git diff --check`, inspect changed-path list for unintended docs/cards or
mutation residue, and verify the new migration/designer/snapshot were CLI-generated.
Delete only verified worktree-contained bin-c408 directories after all foreground
processes finish; do not touch shared main-checkout outputs.

**Planning estimate, not measured timing:** verification floor about 60 minutes
once tests exist (isolated migration/DB/API setup, real Git scenarios, process
script tests, component tests and build); allow roughly 90-150 additional minutes
for all mutation/revert/rebuild controls and evidence capture. Test implementation
is extra. The large-input Git case, migration upgrade and barrier/failure harnesses
are the expensive parts. Do not trade those safety assertions for a namespace-wide
green run, widen timeouts or call a missed control covered by inspection.

Review receives a V-1..V-38 / R-1..R-17 / PC-1..PC-40 checklist with variant
counts, selected filters, pass/fail and restored-green evidence; all A answers,
any pre-existing failures reproduced at base, and the commit holding the tests.
The next stage for THIS artifact is Plan to settle A-1..A-7, then Code to implement
S1-S7 plus this verification, followed by a separate Review before landing.

## Plan resolution pass - A-1 through A-7

Date: 2026-09-06. Stage: Plan. Task: `83018df7`. Reviewed the complete original
plan and TestDesign specification at `3843a0bb5608141857fbbb24b586cb02015899b4`.
That branch was fetched and fast-forwarded into this task's assigned branch;
the source branch remains in its own worktree.

**Verdict: all seven gaps are resolved; next stage is Code.** This addendum is
normative wherever earlier D/V text left a choice or conflicts with these answers.
The original plan and TestDesign's 38 V groups, 17 R guards and 40 PC groups
(including their variants) remain intact. The assertion amendments below are
part of that verification contract, not optional suggestions to Code. Separate
Review remains required after S1-S7. No implementation, tests, live publication,
ignore installation or historical-data review occurred in this resolution pass.

### A-1 resolved - visibility and path knowledge are independent

**Accept the proposal.** A known safe local destination remains useful diagnostic
information when repository visibility is Unknown. D-6's sentence that combines
"Unknown/no-path" is superseded: Unknown visibility alone never nulls a path.

`repositoryVisibility` is always the stored project enum; lack of a checkout
does not rewrite configured Public/Private to Unknown. `visibilitySource` is
`Configured` for Public/Private and `Unknown` for Unknown. D-4 still adds no gh,
provider or network call. Remote present plus no configured classification, a
failed external gh check, or no clear signal are all **Unknown (not checked)**;
none supplies consent. A failed outside check does not silently change an existing
operator assertion either. No gh stderr, credential detail or raw remote URL is
reported by this feature.

For a safely resolved existing Git target, `repositoryPath` is the normalized
absolute **project root**, and `directory` is the project-relative forward-slash
`docs/cards/<stable-slug>` path. The canonical Git toplevel is only a locking/git
implementation detail; a subdirectory project must not display its parent's
output path. Status may compute an unpinned slug read-only. For a missing path,
missing checkout, non-Git target, unsafe traversal or conflicting ownership,
both destination fields are null; do not present an unchecked configuration
string as a resolved export destination. Keep the visibility value independently.
Outstanding ownership at an unavailable target is reported by A-5, never as
successful cleanup. A successful safe resolution is not invalidated just because
later visibility, ignore or publication policy refuses writes.

With an otherwise usable target, opted-in board and Unknown visibility, status
is `eligible=false`, `reason=repository_visibility_unknown`, and a per-card
`relativeFile=null`; sync has zero eligible cards and zero writes. Board-off
takes precedence when it also applies. The Unknown warning is emitted in both
cases. Exact ASCII CLI target/warning lines for V-32 are:

```text
target      C:\src\example\docs\cards\example (Unknown repository visibility; not checked)
UNKNOWN REPOSITORY VISIBILITY: sync blocked
```

When no safe target can be resolved, replace the target line with:

```text
target      unavailable (no safe resolved card-file target)
```

The first line remains `card files  NOT WRITTEN: <reason>`; Unknown does not hide
that a board is also off. Configured Private/Public target text remains D-8's
`(configured Private repository)` / `(configured Public repository)`.

### A-2 resolved - closed response contracts and the explicit notes exception

**Accept the proposed DTO reuse, nullable revision presence and readiness shape,
with the following complete definitions.** There is an intentional exception to
any shorthand claim that "PrivateNotes is never in a response": D-2's explicitly
opened notes endpoint must return the notes to support reading/editing them.
**No publication/status/sync/settings response or ordinary card, revision,
board, project, thread, create/edit or event response contains a
`privateNotes` property, even as null, or its current/historical text.** Only
`GET /api/cards/{id}/private-notes` returns that property. Removing this dedicated
read would break the accepted private-notes UI; broadening its DTO reuse would
break privacy. `hasPrivateNotes` is a presence flag, not the note text.
The sole use of that name in an error response is A-3's `errors.privateNotes`
field-validation key with an array of safe error messages; it never contains
the submitted value or any stored note. No error echoes note text.

Use `CardFileStatusDto` for board status and sync `policy`. Its complete wire
field set is below. All fields are present, including explicit nullable values;
arrays are never null. Implement null inclusion on these DTOs if necessary,
without changing unrelated endpoint serialization.

| Field | Wire type and exact meaning |
|---|---|
| `boardId` | GUID string |
| `enabled` | bool; effective CardFileSync.Enabled, independent of board permission |
| `syncCardFiles` | bool; stored board opt-in |
| `repositoryVisibility` | `Unknown`, `Private` or `Public` |
| `visibilitySource` | `Unknown` or `Configured`, as A-1 |
| `repositoryPath` | string or null; A-1 absolute project root |
| `directory` | string or null; A-1 project-relative directory |
| `eligible` | bool; at least one card can be newly published under the evaluated policy and repository guards |
| `reason` | string or null; first applicable write refusal below; null only when eligible |
| `warnings` | distinct string codes, sorted ordinally for deterministic output; never prose containing card data |
| `ignored` | bool or null; true if any policy-eligible card file or prospective INDEX is effectively ignored; false if that nonempty set was checked and none is ignored; null if the set is empty or inspection unavailable |
| `workingTreeRemovalPending` | bool or null; A-5 working-file cleanup outstanding, null when inspection cannot establish it |
| `gitRemovalPending` | bool or null; A-5 index/current-HEAD cleanup outstanding, null when inspection cannot establish it |
| `removalPending` | bool; true if either pending field is true or unknown; false only when both are false |
| `autoCommit` | bool; effective setting, default false |
| `intervalSeconds` | integer; effective setting, 0 means manual only |

Use `CardFileCardStatusDto` with exactly the above fields plus
`cardFileVisibility` (Inherit/Private/Public) and `relativeFile` (project-relative
`docs/cards/<slug>/<filename>.md`, or null). Its eligibility is for that card,
not for a sibling; its cleanup fields deliberately describe the owning board,
including a stale INDEX. `relativeFile` is non-null only when that card is
eligible. Never construct a suppressed card's filename for a response.

Reason precedence for a successful inspection is: `card_file_sync_disabled`,
`no_repository_path`, `not_a_git_repository` (also a missing checkout),
`unsafe_card_file_path`, `card_file_directory_conflict`, `board_archived`,
`project_archived`, `board_not_opted_in`, `repository_visibility_unknown`, then
`card_private` for a suppressed single card or `no_publishable_cards` for a
board with zero policy-eligible cards, then `card_files_ignore_missing`,
`card_file_path_ignored`, and finally `card_file_cleanup_required` if A-5
prevents additions. No-card and all-Private boards are `eligible=false`,
`reason=no_publishable_cards` only after the preceding board gates pass,
`ignored=null`. An eligible card/board has `reason=null`; an opted-in board may
be saved successfully while its current status is ineligible or ignored.
An unexpected status probe failure overrides eligibility/reason to
false/`status_unavailable`, adds that warning and uses null for unobserved
inspection fields. Preserve already known DB fields and safely resolved paths.

`eligibleCards` counts cards passing Enabled, safe target, active board/project,
board opt-in, known visibility and card override **before** ignore/cleanup
readiness; `excludedCards` is total board cards minus that count, including
archived cards in the total. Therefore an ignored/pending board can have
`eligibleCards>0` and `policy.eligible=false`. Counts expose no private card
identity or content and remain Antiphon diagnostics only.

| Surface | Success status and exact additions/shape |
|---|---|
| GET board card-files/status | 200 `CardFileStatusDto`, read-only, including when feature disabled |
| PUT board card-files/settings | 200 `CardFileStatusDto` evaluated after the saved value; matching same-value request has no save/token/event side effect; no sync or ignore edit |
| POST board card-files/sync | 200 `CardFileSyncBoardResult`: existing `boardId`, `boardName`, `directory`, `written`, `deleted`, `unchanged`, `commitSha`, `writeSkipReason`, `commitSkipReason`, `error`, `dryRun`, plus integer `eligibleCards`, integer `excludedCards`, non-null `policy: CardFileStatusDto`, and `warnings: string[]` |
| Sync result details | Top-level directory equals policy.directory; outer warnings are the sorted union of policy and operation warnings. Counts/skip/error describe this attempt. Policy describes observed end state; dry-run policy describes the unmodified current state, with prospective counts only. Nullable skip/error/SHA fields are explicitly null when absent. |
| Card create/content PATCH | Existing 201/200 CardDto plus required `cardFileVisibility`, bool `hasPrivateNotes` (nonempty string, including whitespace), and non-null `cardFileStatus: CardFileCardStatusDto`; post-save status failure keeps 201/200 and the saved card |
| Single-card GET | Includes the same cardFileStatus; ordinary bulk/list/board/thread embedded cards omit cardFileStatus and perform no per-card repository probe |
| Ordinary CardRevisionDto | Add nullable `cardFileVisibility` and nullable bool `hasPrivateNotes`; null for legacy/missing or non-ContentEdit snapshots, false for known empty notes, true for any nonempty superseded notes; never the snapshot text |
| Explicit notes GET | 200 with exactly `cardId`, `privateNotes`, `concurrencyToken`, `revisionNumber`; current notes are string, current revisionNumber=null; historical notes string/null and requested revision number; token is the current card token even on historical reads. Cache-Control:no-store on all outcomes from this route. |
| /api/cards/limits | Existing fields plus `maxPrivateNotesLength: 20000` and `cardFileVisibilityValues: ["Inherit", "Private", "Public"]` |
| Board summary/detail and creation | Add `syncCardFiles: bool`; retain their existing success bodies/statuses; no notes or internal ownership fields |
| ProjectDto (create/update/get/list and nested setup project) | Add `repositoryVisibility` and non-null `cardFileWarnings: string[]`; successful project creation survives ignore-install failure |
| ProjectSetupResultDto | Existing shape remains; nested project carries warnings, readiness carries the card-files check; existing notes may include safe remediation but never card text |

Status warnings add `public_repository` whenever configured Public and
`repository_visibility_unknown` whenever Unknown, even if a preceding reason
already blocks writes. Add `card_files_ignore_missing` when A-6 fails,
`card_file_path_ignored` on an effective ignored eligible path,
`card_file_cleanup_required` when removalPending, and
`card_file_cleanup_status_unknown` when either cleanup inspection is unknown.
When working cleanup is false and Git cleanup true also add
`card_file_git_cleanup_pending`. When disabled add `card_file_sync_disabled`.
Operation warnings are the codes in A-3/A-4. No duplicate codes.

Project `cardFileWarnings` aggregates only ignore/cleanup/status-unavailable
diagnostics from its targets; it is not a new private-card summary. Readiness adds
key `card-files`, level `Recommended`: status `Warning` for missing/malformed
protection, residual exports or unavailable inspection, `NotApplicable` if no
local target has ever been assigned, otherwise `Ok`. A missing assigned checkout
is Warning, not Ok. Safe detail names target/board IDs and remediation only;
fix links to project configuration. This check never changes `canDispatch`.

### A-3 resolved - validation and failure codes

**Accept semantic 422 with camelCase field keys; make enum token behavior
explicit instead of inheriting the global enum converter's 400 behavior.** Use
feature-scoped request parsing/conversion for these new fields, retain Domain
enums, and validate internal service callers too. Do not change all existing
enum endpoints. Recognize the listed enum names case-insensitively; emit canonical
names. Do not trim enum tokens. Unknown names, whitespace/empty strings, numeric
strings and every JSON number (including 0, 99 and fractional values) are invalid
enum values with 422, never a default. Booleans, arrays and objects are wrong
shapes with 400. Omitted/null optional enums retain D-1/D-2/D-4 semantics.

All errors use the HttpException hierarchy and Problem Details middleware:
`type`, `title`, `status`, sanitized `detail`, `traceId`, and the `code` below.
422 has `errors` mapping exact camelCase keys to nonempty string arrays. Error
messages name the field, allowed values or length/limit only, never the supplied
notes, body, malformed JSON excerpt or raw exception/remote stderr. Feature-route
binding must sanitize before logging as well as before responding; do not retain
a body-containing inner exception just to return a sanitized outer message.

| Condition | HTTP status / code / field or result |
|---|---|
| privateNotes string longer than 20,000 UTF-16 code units | 422 `validation_failed`, `errors.privateNotes` |
| Invalid card-file/repository enum as defined above | 422 `validation_failed`, `errors.cardFileVisibility` / `errors.repositoryVisibility` |
| Settings syncCardFiles or expectedSyncCardFiles missing or null | 422 `validation_failed`, corresponding `errors.syncCardFiles` / `errors.expectedSyncCardFiles`; report both if both absent |
| Malformed JSON, non-object request body, wrong JSON shape for any new field (including a string/number for a boolean or non-string notes) | 400 `bad_request`, no errors collection; feature routes use a sanitized BadRequestException rather than leaking binder detail |
| CreateBoard optional syncCardFiles omitted/null | Valid false; actual true uses A-6, false never requires enable prerequisites |
| revisionNumber present but not one base-10 integer fitting Int32 (including empty, repeated query values, overflow) | 400 `bad_request`; omitted means current notes |
| revisionNumber parsed but <=0 | 422 `validation_failed`, `errors.revisionNumber` |
| Missing card/board/project or missing/non-ContentEdit historical revision | 404 `not_found`; preserve existing identifier ambiguity/scope errors |
| Stale card content token | Existing 409 `conflict`; empty token retains existing 422 `validation_failed` and `errors.ConcurrencyToken` (do not rename preexisting fields globally) |
| Settings expected flag differs under gate | 409 `card_file_policy_changed`, no persistence or event |
| Enable with Unknown, absent/missing/non-Git target, or missing/malformed A-6 protection | 409 `card_file_policy_refused`; extensions `reason`, `warnings`, `repositoryPath`, `directory` using the status definitions and proposed target policy |
| Unsafe traversal / overlapping generated-directory ownership on a mutating route | 409 `unsafe_card_file_path` / `card_file_directory_conflict`, before any mutation, including ownership pinning; these guards precede the general enable refusal |
| Target reassignment / hard board or project deletion before A-5 drain | 409 `card_file_cleanup_required`; `targets` extension is an array of `{boardId, repositoryPath, directory}` for affected owned targets, with safe nullable paths; no individual card filenames/bodies |
| Sync feature disabled / busy gate | 409 `card_file_sync_disabled` / `card_file_sync_running`; disabled check precedes board lookup for real and dry requests |
| Sync completed with policy/ignore/cleanup refusal | 200; writeSkipReason is the applicable A-2 reason, written=0; owned deletions allowed |
| Sync filesystem operation fails | 200; writeSkipReason and commitSkipReason both `card_file_io_error`, error non-null, warning `card_file_io_error`; A-4 defines partial counts |
| Sync Git command fails or times out | 200; commitSkipReason=`git_error`, error non-null, warning `git_error`; writeSkipReason=`git_error` if inspection/cleanup failure prevents new writes, otherwise retain prior write reason/null |
| Generated-byte recheck differs before commit | 200; commitSkipReason=`generated_files_changed`, warning `generated_files_changed`; no commit of the mismatched files, SHA null unless an earlier cleanup commit already completed (A-5) |
| Post-save status inspection fails / GET status inspection fails | Successful mutation retains its 201/200; GET status 200; status reason/warning=`status_unavailable`, eligible=false; not a mutation failure |
| Ignore setup fails after project persistence | Successful create/update/setup status preserved; warning `card_files_ignore_missing`, no partial overwrite of .gitignore |

Keep existing commit skips `autocommit_disabled`, `dry_run`, `nothing_to_commit`,
`rebase_in_progress`, `merge_in_progress`, `cherry_pick_in_progress`,
`detached_head`, and `conflicted_paths`. index.lock and Git command timeouts use
`git_error` as today. The first failed operation ends that attempt; a later
policy/autocommit skip must not overwrite its error. On a non-error real attempt,
AutoCommit=false gives `autocommit_disabled` even after successful disk cleanup;
AutoCommit=true and no commit needed gives `nothing_to_commit`. Dry-run uses
`dry_run` unless inspection itself fails. Ordinary DB save failure stays an
atomic failed save with existing sanitized 500 behavior; it is never converted
to a successful reconciliation or a successful settings save.

### A-4 resolved - serialized saves, partial I/O and recovery

**Accept waiting saves and observable per-board failure results.** Privacy/content
saves wait for the shared gate with the request cancellation token. They reload
authoritative state and check the card token / expected board flag after waiting.
Two edits with the same card token have one winner, one 409 conflict, one revision
and one event; never silently retry the losing content. Manual, dry and tick sync
use try-enter; a busy manual request is 409 card_file_sync_running. No reentrant
sync from a save, and no file I/O performed merely because a setting was saved.

Extend the gate with a project-ID lease acquired before its canonical Git-root
lease by every feature publisher and privacy save. This orders path reassignment
and saves when a checkout is unavailable, so disabling remains possible. Re-read
target/policy after entering; never reuse a tracked pre-gate entity snapshot.
Where a target change needs old and new Git roots, acquire the distinct roots
in normalized ordinal order after the project lease. Subdirectory projects
still share the Git lease. Failed try-enter releases every already-held lease;
finally/disposal releases all leases on save/I/O exceptions and cancellation.

Cancellation while waiting causes no mutation. Cancellation during reconciliation
stops further operations and leaves already completed effects for the next pass;
it propagates cancellation, not a fake 200 or a newly invented 499 response.
Cancellation after SaveChanges does not undo an acknowledged DB commit. An
aborted request is not promised a response; subsequent GET gives current state.

Reconciliation must remove obsolete/unpublished files **before** adding new
content. A delete failure stops the board: no new public sibling, refreshed INDEX
containing that sibling, staging or commit later in that attempt. Successful
earlier deletions remain deleted. A write failure stops remaining writes and
all later staging/commit. Filesystem reconciliation is not one atomic transaction;
do not claim rollback of already completed file operations.

Each generated-file replacement is atomic at the individual-file boundary: write
only the allowed projection to a uniquely named same-directory temporary file,
close/flush it, then atomically replace/move into the validated final path. An
interrupted write leaves the old complete file or no final file, never a truncated
final markdown. Reserve `.antiphon-card-files-<boardGuidN>-<nonce>.tmp` for those
temporary files; remove this board's contained, non-reparse temporary residue on
retry, before additions, without staging it. Never fall back to truncating the
destination if atomic replacement fails. Dry-run creates no temporary file.

On a handled filesystem failure, return the A-3 200 failure result with actual
completed `written`/`deleted` counts (failed operations do not increment them),
`unchanged` for verified unchanged desired files, and commitSha=null if no commit
completed. Error is a fixed safe phase description such as
`Card-file delete failed; retry reconciliation.`; no body, private filename or
native error dump. `policy` uses the observed end-state cleanup flags; unknown
inspection remains unknown/pending. A partial result is never an empty success.
Written/deleted count completed final markdown writes/working-file deletions,
not temporary cleanup, index-only unstaging or a HEAD-only removal. Those Git-only
effects are observable through pending flags, commitSha and commitSkipReason.

SyncAll returns one result for every attempted board and continues after a
board failure. Convert a caught preflight HttpException to that board's ordinary
result with both skip reasons equal to its code, error a sanitized description,
zero mutation counts and warnings containing the code; its status is ineligible.
Unexpected per-board failures use `card_file_sync_error` in both skips/warnings
and a sanitized non-null error. A scoped manual request keeps the A-3 HTTP
preflight errors. Sweep cancellation stops the sweep rather than inventing
success results for unattempted boards. Warning dedup remains board/target/reason;
dry-run cannot consume warning transitions.

Recovery does not depend on an in-memory failed-board flag or old snapshots.
Persist D-1's ownership path/slug before the first mutation. Each new instance
enumerates owned top-level markdown plus exact index and current-HEAD entries,
compares with the latest permitted projection, and discovers temporary residue.
Inspect Git read-only even when AutoCommit=false. Do not select notes or revision
bodies to recover. An absent directory is not proof of an empty index or HEAD.
A pin-save failure leaves files untouched. Failed writes are retried from current
DB values; failed cleanup blocks additions as A-5 specifies until it is drained.

### A-5 resolved - disk revocation works with AutoCommit=false

**Accept the proposal, including keeping Git cleanup pending after disk removal.**
This is the core safety property: with Enabled=true, turning a previously
published board off, making a card Private, making visibility Unknown or archiving
the board/project causes the next real reconciliation to delete its revoked
working files even when AutoCommit=false. It must not wait for auto-commit to be
enabled. That setting controls staging/committing only. Enabled=false continues
to freeze all mutation and cannot claim erasure.

An unpublished path is an owned generated path absent from the current permitted
set, including obsolete filenames and INDEX when no cards remain. Removal also
covers a stale INDEX that still describes a revoked card: treat it as cleanup,
not as a harmless still-desired filename. On a mixed board, remove that stale
INDEX before adding new card information and rebuild it only after cleanup can
complete. A conservative byte comparison with the current public-only INDEX
is allowed to identify a stale INDEX when cards are suppressed; no private-card
body or old revision is needed. Same-path public content corrections may replace
their existing files with the current permitted projection; they never restore
an old body from Git. A corrected file is still subject to the precommit recheck.

For each removed path, distinguish its presence in the working tree, index and
**current HEAD tree**; historical objects are explicitly outside these flags.
For stale INDEX cleanup, apply the same rule until its removal/replacement is
completed from the current public projection. Pending is not a persisted success
flag: re-inspect under the gate after the operation and on later status/restart.

| Observed state of revoked owned exports | workingTreeRemovalPending | gitRemovalPending | removalPending / target-delete guard |
|---|---|---|---|
| Untracked working export only | true | false | true / refuse |
| Working absent, index-only addition still staged, never in HEAD | false | true | true / refuse |
| Working absent, HEAD-tracked export with an unstaged deletion | false | true | true / refuse |
| Working absent, deletion staged but old export still in HEAD | false | true | true / refuse |
| Working/index/current HEAD drained, even if old commits contain exports | false | false | false / allow subject to other existing deletion rules |
| Target/index inspection unavailable with possible recorded or legacy exports | null for uninspectable side | null for uninspectable side | true / refuse; warning card_file_cleanup_status_unknown |
| Never-assigned pathless target with no recorded export ownership | false | false | false; there is no export destination to drain |

Aggregate by board; true on either side wins, otherwise an uninspectable side
remains null. Do not assume null bookkeeping means no exports: probe the legacy
computed target. A reachable, inspected empty legacy target is clean. Even a
safe staged deletion does **not** release ownership while HEAD still contains
the export. This conservative choice avoids forgetting the cleanup responsibility
across a path change or hard delete; a normal owner commit resolves it without
requiring global AutoCommit=true. Changing only GitRepositoryUrl keeps the same
directory ownership and still applies D-4's visibility reset.

For target reassignment or hard deletion, evaluate the old target against an
**empty** desired set regardless of its current publication permission: even
currently eligible exports must be drained before their owner/path can disappear.
Do not infer that removalPending=false under an enabled publication policy permits
abandoning its current public files. The operation only checks/refuses; the owner
turns publication off and reconciles first. All old managed working/index/HEAD
entries must then be absent, with the normal project deletion rules also passing.

With AutoCommit=false, delete working files and stale INDEX, report actual
deleted counts and `commitSkipReason=autocommit_disabled`; make **zero** changes
to index/HEAD, including no unstage of an index-only addition. If Git residue
remains, preserve ownership and block adding new generated files for this board
(`card_file_cleanup_required` if no earlier policy reason applies). Other boards
remain independent. The owner can stage/commit the exact deletions, or remove
the canceled index-only addition through their normal workflow, then retry.
An untracked-only removal can finish completely without any Git mutation.

With AutoCommit=true, repair the exact stale paths in the index and commit
HEAD-tracked removals. Removing a never-HEAD-tracked staged-only addition merely
unstages it; if this was the sole change, return nothing_to_commit and null SHA.
Rebuild the commit path list after staging. If cleanup and new publication are
both needed, drain cleanup first (a separate removal commit is allowed) and only
then add new generated content. Git refusal/failure during drain blocks additions
until retry. If an earlier cleanup commit succeeded but later publication failed,
return its SHA as the last successful commit, actual counts and the later error;
never claim that a successful commit was rolled back. With multiple commits,
verification inspects every commit since the seeded HEAD, not just the final tip.
Dry-run simulates this ordered plan without changing any state: with AutoCommit
false and Git residue it predicts zero additions; with true it may predict
additions after successful prospective drain. Its policy still reports current
pending flags, while writeSkipReason/counts describe that prospective attempt.

Exact additional CLI lines for V-32, after ordinary policy output:

```text
removal     pending: reconcile previously exported working files
removal     pending: working files removed; Git index/HEAD cleanup required
removal     pending: cleanup state unavailable; erasure not confirmed
```

Choose the unknown line if either cleanup field is null; otherwise the first
for workingTreeRemovalPending=true and the second for false/true. Print none
when both false. When Enabled=false also print
`card-file sync disabled; existing exports are not erased`. Git history is never
claimed erased, regardless of whether removalPending has cleared.

### A-6 resolved - ignore protection is a prerequisite, not implied consent

**Accept the enable-time refusal proposal and apply it to explicit true on
CreateBoardRequest too.** There is no new project permission flag or hidden
"was initialized" exemption. The prerequisites for installing D-7's default are:
a project with no opted-in boards, a configured existing safe local Git checkout,
and a usable `.gitignore` (or permission to create it). A preexisting ignore entry
is not required: setup creates the owned block when protection is absent. An
effective existing user-owned blanket rule satisfies protection without adding
or rewriting a block. Configured repository visibility can still be Unknown
during setup; installing ignores does not classify or opt in the repository.

Distinguish **default protection** from **an allowed publish destination**.
Default protection must keep the docs/cards namespace closed except for named
board exceptions. The supported owned forms are exactly D-7's blanket block or
its `/docs/cards/*` replacement followed by literal stable-board-slug exceptions.
No wildcard-negation opening all boards, path traversal or unknown broad exception
counts as default protection. Accept equivalent existing user catch-all rules
(including effective repository/info/global exclusions) only when their evaluated
rule structure establishes the same deny-default namespace; Git checks with
--no-index confirm known off-board paths and a prospective unused board directory
remain ignored. A probe alone is not proof of a general default. If equivalence
cannot be established, report card_files_ignore_missing and recommend the simple
documented managed form instead of guessing. Preserve the user's file bytes.

A well-formed owned block still needs effective-rule checks; a later negation
can override it. Missing/duplicate/unbalanced owned markers are malformed and
require owner repair, even if a coincidental pattern ignores today's one file.
An exception for the board being explicitly enabled is permitted for that
enable evaluation; a leftover exception for an off board makes protection
incomplete until removed or that board is deliberately enabled. Rejecting an
enable is transactional, including board creation: no half-created board,
policy event, implicit sync or ignore mutation. Missing protection yields
409 card_file_policy_refused with reason/warning card_files_ignore_missing.
Unknown/unusable target still wins by the A-2 precedence. Disabling always
remains possible and reports any newly exposed off-board exception as a warning.

An effectively ignored but protected board **may opt in** for review. Its status
is card_file_path_ignored and a dry/real sync writes zero until the owner makes
a supported explicit board exception. The writer never removes an ignore,
force-adds, or stages .gitignore. Recheck protection before every new publication:
an already opted-in project that loses its default protection refuses new writes
with card_files_ignore_missing, while cleanup stays allowed. No install/repair
runs on tick, status, dry-run or settings PUT.

Intentional removal cannot be inferred from a missing file or block. The
supported expression of intent is the documented blanket-plus-board-exceptions
form (or provably equivalent user rules), not deleting all protection. Thus a
fresh process can distinguish permission using current DB policy and ignore
configuration without a new persisted acknowledgement bit. Existing projects
without protection need an explicit owner edit before enabling; migration
does not mutate their files. New path assignment with already opted-in boards
does not append a blanket ignore; status warns/refuses publication until the
new target is deliberately protected and its visibility configured. None of
these rules mistakes ignore matching for erasure of tracked/staged content.

### A-7 resolved - Position is an internal public-projection input

**Accept the proposal.** Add `int? Position` to CardFilePublicCard's immutable
scalar whitelist. The existing scalar call is
`CardRanking.OrderKey(importance, urgency, dueAt, position, createdAt, now)`;
use it for INDEX ordering and retain the final ordinal Identifier tie-break.
Keep one `now` value for the render, and preserve null Position sorting after
placed cards in a rank cell. Precompute the existing review boolean from scalar
inputs; flatten tracker metadata/labels into immutable scalar values/collections.
Do not carry Card, CardRevision or ExternalIssueRef references or an object bag.

Position affects ordering only; no new `position` frontmatter or INDEX metadata
is introduced. Dropping it would regress the current pinned ordering and
CARD-0098 for eligible cards, with no privacy benefit. V-4's projection boundary
and V-5's existing placed-card test must both pass, including null/equal-position
and final identifier tie cases.

### Reconfirmed seven Code slices and verification amendments

Keep seven slices in their original order. The resolutions expand specific
contracts inside them, not the product scope. S2 depends on the S3 gate/I/O seam
and S4 read-only ignore evaluator: introduce those minimal shared primitives
when S2 needs them, then complete reconciliation/setup in their assigned slices.
Do not stub enable validation to allow unsafe intermediate behavior. No partial
S1-S7 deployment; no live opt-in, gh probe, cleanup sweep or AutoCommit change.

| Slice | Confirmed scope / additions from this pass | Existing verification groups receiving exact amendments |
|---|---|---|
| S1 | Existing entity/default/migration/public projection work; add internal Position and fixed DTO shapes (including cleanup fields). No new database column for ignore acknowledgement or transient failure state. | V-1..V-5/V-11: retain safe defaults and projection SELECT exclusion; add Position/null/tie cases to renderer tests. |
| S2 | Card/board/project APIs, note history/read, limits and status; feature-scoped enum/binding validation, safe error logging, waiting saves and gate primitives, ignore-read prerequisite on all explicit enable paths. | V-6..V-16/V-19: assert A-2 exact field sets/nulls, sole explicit-notes exception, PUT 200/status, no-card/private/ignored reasons; raw invalid-token/status matrix in A-3; one winner after wait, cancellation and status failure after save. |
| S3 | Reconciler/repository seam, exact-path Git, driver, ownership guards; deletion-first order, atomic file replacement/temp recovery, project+repo gates, disk-only versus index/HEAD pending, observable partial sweep results. | V-17..V-27: parameterize A-5 state table for both AutoCommit values and fresh service; disk deletion still occurs with false and index/HEAD bytes stay unchanged; staged deletions still block reassignment; retry after index-only unstage/HEAD commit clears pending. V-20 injects partial temp/write/delete/commit failures with exact A-3 result/counts. |
| S4 | Existing setup/ignore/readiness work; fixed ProjectDto warnings/card-files check, safe equivalent-rule evaluation shared with S2/S3, no implicit initialization exemption. | V-13/V-28..V-30: explicit true on create/settings refuses missing/malformed protection; protected-but-ignored enable succeeds; full ignore removal versus supported exception; late override/off-board exception; fresh process repeats the same verdict; readiness leaves canDispatch unchanged. |
| S5 | Existing card.ps1/help changes; A-1 exact Unknown/unavailable target text, A-5 three pending states and feature-disabled line, JSON-only note-free new/edit output. | V-31..V-33: pin exact lines, null paths, cleanup flags and 400/422/nonzero rejection; successful blocked/status-unavailable save still exits 0 and calls create once. |
| S6 | Existing client API/types/components/invalidation; explicit null inspection states, field errors, board-level cleanup status in card UX, Project warnings and readonly remediation. | V-34..V-37: render A-2/A-5 statuses without claiming written/erased, distinguish Unknown from unavailable path, retain failed-read notes protection and no extra opt-in dialog. |
| S7 | Existing owner docs/bundle/default ignore only; document all seven final answers, especially disk cleanup with AutoCommit=false, current HEAD versus history, prerequisite rules and exclusive notes read. | V-38/document review: exact API/code and rollout references, default-ignore equivalence, cleanup instructions; no generated docs/cards edits or live-data claims. |

These are normative expectation changes to the existing V groups, preserving
their IDs and named classes/filters; Code records any actual class split against
those IDs. Keep every R and PC assertion, with these additional variants within
their existing groups: PC-12 must also catch an erroneous AutoCommit gate around
working deletion; PC-17 must catch additions while Git cleanup remains pending;
PC-22 must include a staged-deletion/current-HEAD residue case; PC-24 must catch
bypassed default-protection validation as well as effective path ignores;
PC-29 must cover Unknown and both known pending states. Each added mutation is
its own red/revert/green run. Existing positive-control counts are groups, not
the number of mutation executions; report the expanded variant counts honestly.

No A item remains for Code to decide. Implement these answers with the preserved
verification specification, report every V/R/PC item, and hand the completed
mechanism to separate Review. This pass ran no tests or builds; its checks are
document preservation, whitespace, scope and the consistency of the contracts.
