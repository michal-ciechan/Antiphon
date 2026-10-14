Working the Antiphon board. Use `scripts/card.ps1` — its header comment is the reference (verbs
`list`, `search`, `get`, `history`, `block`, `unblock`, `new`, `edit`, `move`, `close`, `reopen`, `archive`,
`unarchive`, `diagnose`, `reorder`, `order`, `-Limits`); AGENTS.md
"Working cards from a shell" has the human-facing synopsis. Do not hand-compose the HTTP yourself
unless the script genuinely can't do it. These are the shapes that bite either way:

- Before filing a possible duplicate, run `card.ps1 search '<distinctive phrase>' -Board <name> -All`,
  then `card.ps1 get <hit> -Board <name>` to read the full description and verdict. Search checks
  stored full text and follows every page. A capped list or summary preview cannot prove absence;
  a failed or changed enumeration cannot establish "no duplicate".

- An **external** ChatGPT/Codex caller dispatches with `delegate.ps1 -Capability <name>` (the name,
  never a secret). While Claude is held, pass `-Kind Codex` on Worker/stage work. Do not edit
  `Delegation:AllowedRoots`. Operator issue is `capability.ps1`.

- Address cards as `CARD-0051`, `card-51`, `#51`, `51`, or a guid; use `-Board` to scope identifiers.

- `GET /api/cards/limits` returns the current title/description/reason/actor length ceilings
  plus `importanceValues` / `urgencyValues` (`-Limits` on the script). Today: title 300,
  description 20 000, reason 4 000, actor 200. There is no `priority` field; a write that still
  sends one is 400.

- Re-read the card's `concurrencyToken` between writes. `card.ps1` fetches it by default;
  `-Token` selects strict compare-and-swap. Inspect a conflict before retrying.

- VALIDATION FAILURES ARE 422, NOT 400, with a problem-details `errors` dictionary using existing
  PascalCase field names (`"Description"`, `"Reason"`, `"ConcurrencyToken"`) alongside new
  camelCase privacy fields (`"privateNotes"`, `"cardFileVisibility"`) whose messages name the limit and the actual
  length. Read the message rather than guessing from the status code.

- SEND WRITE BODIES FROM A FILE, never inline on a shell command line: `-DescriptionFile` /
  `-ReasonFile` (or, hand-composing HTTP, a `--data @body.json` you wrote with your file tools).
  Card text is long and full of quotes, newlines, backticks and dollar signs, and both shells here
  mangle it silently — the corruption lands in the record, not in an error.

- Move with `PATCH /api/cards/{id}` (`boardColumnId`, `concurrencyToken`, `reason?`, `spawn?`).
  Correct text with `PATCH /api/cards/{id}/content` (`concurrencyToken`, required `reason`,
  optional `title`, `description`, `importance`, `urgency`, `dueAt`, `clearDueAt`, `labels`,
  `editedBy`). Read revisions at `GET /api/cards/{id}/revisions`.

- Relationships: `block CARD-0051 -BlockedBy CARD-0049`; remove with `unblock`.
  Optional: `-ReasonFile`, `-By`; `block -RelatedBoard` scopes the blocker.
  HTTP-stub coverage (`CardRelationshipScriptTests`; details in `docs/ops-http.md`):

| Behaviour | Named test | What it asserts |
|---|---|---|
| Add | `Block_reads_the_card_then_posts_the_relationship_body_with_the_fetched_token` | Body, tokens, file, board, JSON |
| Remove | `Unblock_resolves_the_row_by_relatedIdentifier_and_posts_remove` | Identifier, route, token |
| Read | `Get_and_history_print_blocked_by_blocks_and_relationship_rows` | Status, inverse, history, JSON |
| No match | `Unblock_without_a_matching_blocker_exits_non_zero_and_names_the_current_blockers` | Failure, identifiers, no POST |

- A MOVE INTO AN ACTIVE COLUMN NO LONGER SPAWNS AN AGENT UNLESS YOU ASK. `spawn`/`-Spawn` defaults
  to false; the response's `spawnedSessionId` / `spawnSuppressed` say what happened. Before
  CARD-0051 a bookkeeping move always spawned silently and cost two dead sessions and a stray
  worktree — if you have muscle memory from before that, assume nothing starts unless you pass it.

- A TERMINAL MOVE'S `reason` IS THE VERDICT and persists as the card's `terminalReason`. Say what
  shipped, what was corrected, what is still open, with commit hashes — it is the last thing anyone
  reads about the card.

- ARCHIVE IS WHAT DELETE MEANS HERE (`POST /api/cards/{id}/archive` with token and reason). The row
  stays so references never dangle and the identifier is never handed out again.

- A CLOSED CARD IS REOPENED VIA `POST /api/cards/{id}/reopen`, NOT A MOVE. `Done`/`Canceled` stay
  unreachable through `PATCH /api/cards/{id}`. Body: `concurrencyToken`, required `reason`, optional
  `boardColumnId` (defaults to the board's Backlog, then the lowest-order live column), optional
  `reopenedBy`. The Reopen revision keeps the superseded `terminalReason`/`completedAt`; the card
  surface is live again. Reopen never spawns — want an agent on the reopened card, `POST /spawn`.

- CARD FILES ARE OPT-IN PUBLICATIONS: boards default off; locally configured Unknown
  repository visibility blocks sync. Private card overrides suppress the whole card;
  private notes never export. Title, description, outcome and archive reasons are public
  on eligible cards. New/edit accept `-PrivateNotesFile` and `-CardFileVisibility`;
  edit accepts `-ClearPrivateNotes`. Never echo notes. A saved card is not proof of publication.
  Status is at `/api/boards/{id}/card-files/status`; settings require both syncCardFiles
  and expectedSyncCardFiles. See docs/card-file-privacy.md for refusal/cleanup semantics.

- PRIVATE NOTES READ PROHIBITION: agents must not read the notes route
  (`GET /api/cards/{id}/private-notes`) unless the card's own brief explicitly instructs,
  and must never quote note text into a report, card body, commit message, or chat message.
