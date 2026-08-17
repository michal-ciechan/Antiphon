Working the Antiphon board over its HTTP API (default `http://localhost:17202`). There is no card
CLI yet, so these are the shapes that bite:

- READ CARDS THROUGH THEIR BOARD. `GET /api/boards` lists boards; `GET /api/boards/{boardId}`
  returns the board with its columns and every card in them, including each card's `identifier`
  (`CARD-0058`), its `concurrencyToken`, and its column's `isActive` / `isTerminal` flags. There is
  NO `GET /api/cards/{id}` — asking for one returns 404 and that is not evidence the card is gone.

- EVERY WRITE CARRIES THE CARD'S CURRENT `concurrencyToken`, AND EVERY SUCCESSFUL WRITE ROTATES IT.
  Re-read the board between two writes to the same card; replaying a token you already spent is
  rejected as a conflict, which is the guard working, not a transient error to retry.

- VALIDATION FAILURES ARE 422, NOT 400, with a problem-details `errors` dictionary keyed by
  PascalCase field name (`"Description"`, `"Reason"`) whose messages name the limit and the actual
  length. Read the message rather than guessing from the status code.

- SEND WRITE BODIES FROM A FILE, never inline on a shell command line: write the JSON with your file
  tools, then `curl -s -X PATCH ... -H "Content-Type: application/json" --data @body.json`. Card text
  is long and full of quotes, newlines, backticks and dollar signs, and both shells here mangle it
  silently — the corruption lands in the record, not in an error.

- MOVE AND REWRITE ARE DIFFERENT ENDPOINTS. `PATCH /api/cards/{id}` is move-only (`boardColumnId`,
  `concurrencyToken`, optional `reason`). `PATCH /api/cards/{id}/content` corrects the text
  (`concurrencyToken` plus a REQUIRED `reason`, then any of `title`, `description`, `priority`,
  `labels`, `editedBy`); it records a revision carrying the values it superseded, readable at
  `GET /api/cards/{id}/revisions`. A card that is simply wrong gets corrected in place — filing a
  second card to describe the mistake is what this endpoint exists to stop.

- NEVER MOVE A CARD INTO AN ACTIVE COLUMN FOR BOOKKEEPING: that move SPAWNS AN AGENT. One such PATCH
  left two dead sessions and a stray worktree behind. Check the column's `isActive` before moving.

- A TERMINAL MOVE'S `reason` IS THE VERDICT and persists as the card's `terminalReason`. Say what
  shipped, what was corrected, what is still open, with commit hashes — it is the last thing anyone
  reads about the card.

- ARCHIVE IS WHAT DELETE MEANS HERE (`POST /api/cards/{id}/archive` with token and reason). The row
  stays so references never dangle and the identifier is never handed out again.
