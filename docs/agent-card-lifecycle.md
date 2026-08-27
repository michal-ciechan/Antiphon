# Agent Card Lifecycle

Antiphon tracks two related but separate states:

- **Card status** is the board workflow location: `Backlog`, `InProgress`, `Review`, `Done`, `NeedsDecision`, or `Canceled`.
- **Agent session status** is the runtime process state: `Starting`, `Running`, `Stopping`, `Stopped`, or `Failed`.

## Status Rules

Moving or spawning a card into the board's active column moves the card to `InProgress` and claims it for an agent session.

When a queued agent attempt completes successfully, the card moves from `InProgress` to the board's `Review` column. This applies to both interactive card launches and orchestrated launches.

Orchestrator reconciliation also applies the same transition for already-claimed active cards whose latest run attempt is `Succeeded`. This repairs cards that completed before this lifecycle rule existed.

Interactive launches keep the successful session attached and running after the card enters `Review`. This lets a reviewer send follow-up comments to the same agent session from the diff review UI.

Orchestrated launches stop and release the successful session after moving the card to `Review`. They do not schedule a continuation after success because the next step is human review.

Failed launches release the claim and leave the card in its current workflow column. Retry scheduling remains an orchestrator concern.

Moving a card to a terminal column while a session is active stops the session and clears the claim.

## Delegated work — cards move themselves (CARD-0040)

Everything above is the **card-spawn path**: a card that owns an agent session (`Card.OwnerSessionId`),
moved by `SpawnAsync` and by `CardLifecycleTransitions.TryMoveToReview`, with `system` as the actor on
the revision. Most work on this deployment is not that shape — it is a **delegated task**, and until
CARD-0040 a task had no link to a card at all, so a card whose work ran as a delegate sat in whatever
column a human last dragged it to.

**The link.** `AgentTask.CardId`, resolved once at creation in this order:

1. an explicit `-Card` on `scripts/delegate.ps1` (`CARD-0040`, `card-40`, `#40`, `40`, or the guid) —
   a value that resolves to no card is a **422**, never a silent no-binding;
2. the parent task's card, the followed-up task's card, or the conflicted task's card for an
   auto-spawned Merge task;
3. the **first** `CARD-nnnn` in the title — which is why the house habit of leading a brief's title
   with the identifier is worth keeping. A title naming several cards binds the first and records a
   `Warning` event naming the rest.

`Role = Check` rows never bind: a check-in interpretation is about a task, not a card. Identifiers are
unique per **board**, not globally, so resolution walks the narrowest scope that answers — the boards
this caller demonstrably works, then the boards of projects whose checkout contains the task's
repository, then every board — and demands uniqueness inside it. Ambiguity binds nothing and says
which boards collided; unbound is never an error, the task simply runs and the card does not move.

**The transitions.** `CardWorkTransitionService` sweeps every 60 s (`CardTransitions:IntervalSeconds`)
over durable rows only — no session status, no transcript, no runner:

| Evidence | From | To |
|---|---|---|
| a bound task is `Dispatched` / `Working` / `Blocked` | Backlog, Review | **In Progress** |
| the newest event is a task settling `Succeeded`, and nothing is open | Backlog, In Progress | **Review** |
| the newest event is `Failed` or `Canceled` | any | — (nothing) |

A Backlog card whose only evidence is a settle goes **straight to Review** in one move; it is never
routed via In Progress. A `Blocked` task counts as open — the card is still being worked, by whoever
answers the question.

**The last word wins, and a human move is a fact.** The sweep acts only when the evidence timestamp is
NEWER than the card's latest `Move`/`Reopen` revision (`UpdatedAt` for a card with no history). Drag a
card out of Review with a reason and nothing overrides you; the next dispatch is newer and moves it
again, which is correct. The same rule is what makes the sweep idempotent — its own `Move` row becomes
the new last word.

**The two paths never touch the same card.** The sweep skips any card with `OwnerSessionId != null`,
which is exactly the population the card-spawn path above owns. It also skips archived cards and
anything in `NeedsDecision`, `Done` or `Canceled`. The actor on its revisions is **`card-transitions`**,
deliberately not `system` — that name already means the RunAttempt path.

An automated move calls `ApplyColumnMove` directly (like `ReopenAsync`, unlike `MoveAsync`), so it is
structurally incapable of spawning. It sets `AutoDispatchHeldAt` on every active landing so the
orchestrator tick cannot start a card session on top of the delegate, and dequeues on a Review landing
so a finished card never sits at an agent's queue head (the CARD-0001 respawn loop).

**Stale is detection, not repair.** A card In Progress past `CardTransitions:StaleAfterDays` (7) with
no open bound task, no live session and no owning card session becomes a Warning
`AttentionKind.CardStalled` row in `GET /api/attention`, naming the last bound task's outcome. Nothing
un-stalls it automatically — move it, or start something.

**Review → Done is not automated, in any form.** That is the card's conditional arm and it waits on
CARD-0039 and on a confidence signal nobody has defined.

## Diff Review Comments

Review comments from the card diff include the card identifier, file path, side, and selected line or line range before the comment text. The UI selects a single line from its comment action and extends same-file, same-side selections with Shift-click. If a matching agent session is running, the comment is sent to that session as channel input. If no agent session is running, Antiphon starts a new interactive agent session on the card with the review comment as the launch prompt.

## Review Column

The success transition targets the first board column whose `CardStatus` is `Review`, ordered by `ColumnOrder`. If a board has no review column, or if the card has already reached a terminal column, completion does not move the card.
