Working the Antiphon board. Use `scripts/card.ps1` — its header comment is the reference (verbs
`get`, `history`, `new`, `edit`, `move`, `close`, `archive`, `unarchive`, `-Limits`); AGENTS.md
"Working cards from a shell" has the human-facing synopsis. Do not hand-compose the HTTP yourself
unless the script genuinely can't do it. These are the shapes that bite either way:

- An **external** ChatGPT/Codex caller dispatches with `delegate.ps1 -Capability <name>` (the name,
  never a secret). While Claude is held, pass `-Kind Codex` on Worker/stage work. Do not edit
  `Delegation:AllowedRoots`. Operator issue is `capability.ps1`.

- A CARD IS ADDRESSED THE WAY IT'S NAMED. `CARD-0051`, `card-51`, `#51`, `51`, or its guid — every
  card route (`GET /api/cards/{id}`, both PATCHes, `/revisions`, `/archive`, `/unarchive`, `/reopen`, `/spawn`)
  resolves whichever form you give it. There is no look-up-the-id-first step.

- `GET /api/cards/limits` returns the current title/description/reason/actor length ceilings
  plus `importanceValues` / `urgencyValues` (`-Limits` on the script). Today: title 300,
  description 20 000, reason 4 000, actor 200. There is no `priority` field; a write that still
  sends one is 400.

- EVERY WRITE CARRIES THE CARD'S CURRENT `concurrencyToken`, AND EVERY SUCCESSFUL WRITE ROTATES IT.
  Re-read the card between two writes to it; replaying a token you already spent is rejected as a
  conflict, which is the guard working, not a transient error to retry. `card.ps1` re-reads for you
  by default — see its header for the tradeoff and `-Token` for strict compare-and-swap.

- VALIDATION FAILURES ARE 422, NOT 400, with a problem-details `errors` dictionary keyed by
  PascalCase field name (`"Description"`, `"Reason"`) whose messages name the limit and the actual
  length. Read the message rather than guessing from the status code.

- SEND WRITE BODIES FROM A FILE, never inline on a shell command line: `-DescriptionFile` /
  `-ReasonFile` (or, hand-composing HTTP, a `--data @body.json` you wrote with your file tools).
  Card text is long and full of quotes, newlines, backticks and dollar signs, and both shells here
  mangle it silently — the corruption lands in the record, not in an error.

- MOVE AND REWRITE ARE DIFFERENT ENDPOINTS. `PATCH /api/cards/{id}` is move-only (`boardColumnId`,
  `concurrencyToken`, optional `reason`, optional `spawn`). `PATCH /api/cards/{id}/content` corrects
  the text (`concurrencyToken` plus a REQUIRED `reason`, then any of `title`, `description`,
  `importance`, `urgency`, `dueAt`, `clearDueAt`, `labels`, `editedBy`); it records a revision carrying the values it superseded,
  readable at `GET /api/cards/{id}/revisions`. A card that is simply wrong gets corrected in place —
  filing a second card to describe the mistake is what this endpoint exists to stop.

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
