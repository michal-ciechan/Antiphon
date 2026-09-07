# Card-file publication and private notes (CARD-0408)

The database is authoritative. Card markdown is an optional, one-way publication
of public fields; never edit generated files under `docs/cards/`. A fresh database
does not import markdown. Backup/restore carries cards, private notes, revisions
and policy settings; a Git clone does not. Dumps contain private notes: retain
the root `backups/` ignore and use the existing backup custody procedures.

## Publication policy

Boards default to `syncCardFiles=false`. Projects default to locally configured
`repositoryVisibility=Unknown`; Unknown blocks publication even with a remote or
GitHub integration. Private/Public are operator classifications, not provider
checks. A card defaults to `cardFileVisibility=Inherit`; `Private` excludes the
whole card and its INDEX entry. `Public` does not bypass the board, project,
archive, path or ignore gates. Archived cards can publish; archived boards and
projects are reconciled to no exports.

Private notes are always excluded. Eligible public fields include title,
description, labels, identifiers, outcome and archive reasons. Do not put private
material into those fields. Ordinary card/history/list/thread/agent DTOs contain
only note presence and visibility, never note bodies. Content edits preserve the
superseded notes in a private snapshot. Null means unchanged; empty explicitly
clears; whitespace is preserved. Notes have a 20000 UTF-16 character ceiling.

`scripts/card.ps1 new/edit` accepts `-PrivateNotesFile` and
`-CardFileVisibility Inherit|Private|Public`; edit also accepts
`-ClearPrivateNotes` (mutually exclusive with the file). There is no inline notes
argument. `-Json` returns one ordinary response without human output. Saving never
syncs: the returned status says eligible or not written, safe absolute target,
configured visibility and cleanup state. An unavailable status confirms nothing.

## API and explicit reads

- `GET /api/boards/{id}/card-files/status`: read-only policy and current cleanup
  inspection, with explicit nulls for unavailable target/inspection values.
- `PUT /api/boards/{id}/card-files/settings`: both `syncCardFiles` and
  `expectedSyncCardFiles` are required; 200 returns status. Stale expectations
  give 409 `card_file_policy_changed`; an unusable path, Unknown visibility or
  missing ignore protection refuses opt-in as `card_file_policy_refused`.
  Unsafe/reparse paths and overlapping owners have their own refusal codes.
  Explicit true on board creation has the same prerequisites. Disabling remains
  possible when the target is unavailable.
- `POST /api/boards/{id}/card-files/sync?dryRun=true|false`: counts, policy,
  warnings and write/commit skip reasons. A 200 may describe refused writes or
  partial reconciliation. `eligibleCards` is not the number written. Dry run
  changes no files, index, ownership or warning-cache state.
- `GET /api/cards/{id}/private-notes` is an explicit operator-only UI action,
  with optional positive int32 `revisionNumber` for a ContentEdit snapshot.
  The response is exactly cardId, privateNotes, concurrencyToken and revisionNumber,
  with `Cache-Control: no-store` on all outcomes. Historical null is unknown;
  empty is a known empty snapshot. Agents must not read the notes route unless
  the card's own brief explicitly instructs, and must never quote note text into
  a report, card body, commit message, or chat message.

Semantic validation uses 422 `validation_failed`; new fields use camelCase error
keys alongside existing PascalCase keys (e.g. `privateNotes` and
`ConcurrencyToken`). Numeric enums, numeric strings and unknown enum names are
invalid. Wrong primitive types and malformed JSON give sanitized 400 responses.

## Reconciliation and ownership

The 60-second tick and manual endpoint share project and canonical Git-root
leases with policy/content edits and ownership changes. Ownership pins the safe
target and stable board slug before filesystem effects. Renames retain the slug;
conflicting directory owners are refused. Drain the old working tree, index and HEAD before project
path reassignment or hard deletion; unavailable inspection keeps the guard closed.

Reconciliation removes revoked generated markdown and stale INDEX content before
publishing anything. AutoCommit defaults false: working files are removed, and
never-in-HEAD staged additions are automatically unstaged on exact owned paths.
They have a distinct `card_file_staged_private_residue` warning. Already-in-HEAD
files require owner Git cleanup when AutoCommit is false, including when a deletion
is already staged. `card_file_cleanup_required` remains pending until HEAD/index
are clear. With AutoCommit true, a removal-only commit uses exactly
`antiphon: remove unpublished card files`, then permitted additions may proceed.
The previous commit still contains previously published data; history is not
rewritten or claimed erased.

Nullable working-tree/Git pending flags describe unknown inspection, not success.
`removalPending` is false only when both are false. `Enabled=false` freezes all
mutation, including cleanup; the manual endpoint returns 409
`card_file_sync_disabled`. `IntervalSeconds=0` is manual-only. An occupied sync
lease returns 409 `card_file_sync_running`.

Git add and commit use the exact owned top-level markdown paths, NUL-delimited
literal pathspecs. Source files, ignore rules, nested files and sibling boards
remain untouched. There is no automatic push or force-add. Rebase, merge,
cherry-pick, detached HEAD, managed conflicts, external byte changes and Git/IO
failures are surfaced; successful earlier changes remain observable for retry.
Filesystem paths and ancestors must be contained and free of reparse aliases.

## Fresh-project ignore protection and rollout

For other projects, setup appends this managed block only to an existing safe Git
checkout with no opted-in boards, preserving existing bytes/newlines. Setup
failures return warnings and do not erase a successfully saved project. Status,
readiness and sync only inspect protection; they do not repair it. The recommended
`card-files` readiness warning does not change `canDispatch`.

```gitignore
# BEGIN ANTIPHON CARD FILES
/docs/cards/
# END ANTIPHON CARD FILES
```

Equivalent effective user/Git ignore protection is accepted; malformed blocks and
broad negations fail closed. Opt-in does not edit ignore rules. To publish a named
board, the owner may deliberately replace the blanket rule with the following,
using the safe slug returned by status:

```gitignore
# BEGIN ANTIPHON CARD FILES
/docs/cards/*
!/docs/cards/example/
# END ANTIPHON CARD FILES
```

All other boards remain protected, including future boards. Review preview and
configured repository visibility before sync. An ignored target remains blocked
even if files were previously tracked.

CARD-0408 does not modify Antiphon's own root `.gitignore`. CARD-0409 owns its
existing exported files and interim ignore disposition. Complete that cleanup
before any live opt-in. Do not change live AutoCommit, enable boards, run a cleanup
sweep or infer production migration from a healthy server as part of this card.
