# Agent Card Lifecycle

Antiphon tracks two related but separate states:

- **Card status** is the board workflow location: `Backlog`, `InProgress`, `Review`, `Done`, `Blocked`, or `Canceled`.
- **Agent session status** is the runtime process state: `Starting`, `Running`, `Stopping`, `Stopped`, or `Failed`.

## Status Rules

Moving or spawning a card into the board's active column moves the card to `InProgress` and claims it for an agent session.

When a queued agent attempt completes successfully, the card moves from `InProgress` to the board's `Review` column. This applies to both interactive card launches and orchestrated launches.

Orchestrator reconciliation also applies the same transition for already-claimed active cards whose latest run attempt is `Succeeded`. This repairs cards that completed before this lifecycle rule existed.

Interactive launches keep the successful session attached and running after the card enters `Review`. This lets a reviewer send follow-up comments to the same agent session from the diff review UI.

Orchestrated launches stop and release the successful session after moving the card to `Review`. They do not schedule a continuation after success because the next step is human review.

Failed launches release the claim and leave the card in its current workflow column. Retry scheduling remains an orchestrator concern.

Moving a card to a terminal column while a session is active stops the session and clears the claim.

## Diff Review Comments

Review comments from the card diff include the card identifier, file path, side, and selected line or line range before the comment text. The UI selects a single line from its comment action and extends same-file, same-side selections with Shift-click. If a matching agent session is running, the comment is sent to that session as channel input. If no agent session is running, Antiphon starts a new interactive agent session on the card with the review comment as the launch prompt.

## Review Column

The success transition targets the first board column whose `CardStatus` is `Review`, ordered by `ColumnOrder`. If a board has no review column, or if the card has already reached a terminal column, completion does not move the card.
