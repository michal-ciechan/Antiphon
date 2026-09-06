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
