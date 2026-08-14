# Card workflow — decisions taken outside the cards

**Why this file exists:** cards used to be write-once with no edit endpoint (CARD-0019), so a
decision that refined an already-filed card had nowhere to live. Chat is not a record.

**Since 2026-08-14** cards *can* be edited — `PATCH /api/cards/{id}/content`, with a mandatory
reason and an immutable revision history — so the entries below should be folded back into their
cards, leaving this file only for decisions that genuinely live outside any card. That fold is
CARD-0019 slice 3 and has not happened yet.

---

## 2026-08-13 — Backlog → Done is allowed, and a close carries a reason

**Shipped.** `CardStateMachine` previously refused `Backlog → Done`, pinned by a test. The rule
assumed every Done card was done *by that card's own work*. Two ordinary closes do not fit it:

- a card that is **no longer wanted**, and
- a card **already fixed as part of another card's work**.

Routing those through `InProgress → Review → Done` invents a history that never happened — six
cards were walked that way by hand on 2026-08-13 — and `Canceled` is wrong for the second, because
the work genuinely happened.

What separates a close from a completion is therefore **the reason, not the path**.
`MoveCardRequest` now carries an optional `Reason`, and a supplied reason overwrites an existing one
so a card re-closed with a better explanation ("fixed by CARD-0041") does not keep the generic note
it got first.

It is called `Reason`, not `TerminalReason`, deliberately. Closing is what motivated it, but "moved
back because the spec changed" and "started early to unblock CARD-nnnn" are the same kind of fact,
and a field named for one use is how a second one ends up as a second field.

**Known gap — CLOSED 2026-08-14 by CARD-0019 slice 1.** The reason used to persist only on a move
into a terminal column, where it becomes `Card.TerminalReason`; on every other move it was accepted
and dropped, because there was no per-card history to hold it. `CardRevision` is now that home: every
column transition writes a `Move` revision carrying the reason, from/to column and from/to status,
including the spawn-, session- and tracker-driven moves. A terminal move still stamps
`TerminalReason` as well — the two are not alternatives. (`AuditRecord` was never the right home; it
is workflow and LLM-cost oriented.)

**Not changed, deliberately:** `InProgress → Done` and `Blocked → Done` remain forbidden. The same
argument applies to both and they are probably worth allowing, but widening a considered state
machine further than asked is a decision for the operator, not an inference.

---

## 2026-08-13 — Moving a card into In Progress SPAWNS AN AGENT

**Discovered by walking into it.** `CardService.ApplyColumnMove` ends with:

```csharp
if (targetColumn.IsActive && card.OwnerSessionId is null)
    await SpawnAsync(card.Id, new SpawnCardRequest(), ct);
```

Six already-completed cards were moved Backlog → Done to make the board honest. The state machine
routes that through In Progress, so the move **launched six agent sessions** (`229f3bf9`,
`6c19f8fd`, `7ee35818`, `775447a9`, `2f8ad7c0`, `1759d0b9`, all 05:23:5x), created six git
worktrees, and six branches. All six sessions ended `Failed`, so nothing ran away — but on a
healthy system that is six Claude agents starting work on cards that were already finished, from
an operation whose entire intent was bookkeeping.

**This is a hard constraint on CARD-0040.** Automatic Backlog → In Progress does not just change a
column; it starts real, billable work. Any auto-transition design must either separate "the column
says active" from "start an agent", or be explicit that the trigger is the work starting rather
than the reverse. The current coupling means a reconciler that tidies stale columns would spawn an
agent per card it tidied.

It also argues that a **bookkeeping move needs a way to say "do not spawn"** — the same shape as
`MoveCardRequest.Reason`: the caller knows why it is moving the card, and "because it was already
done" should not launch anything. Note the `card.OwnerSessionId is null` guard is not enough,
because a completed card whose session has been cleaned up has no owner.

---

## 2026-08-13 — Auto-transition stops at Review, conditionally

**Not implemented — this is a constraint on CARD-0040.**

Work should **go through Review first** rather than auto-advancing to Done. Skipping Review would
make Done mean "an agent said it finished", which is not the same claim and would quietly bury
unreviewed work.

The exception is conditional, and the condition has two inputs:

1. **Priority** — a low-priority card may not be worth a human read. This depends on
   **CARD-0039** (urgency and importance as separate axes); with today's single `priority` integer
   the condition cannot be expressed usefully, because it conflates "matters" with "soon".
2. **Confidence** — how sure the system is that the work is actually complete and correct.

**Confidence does not exist as a signal today.** Anyone implementing this must define it before
using it, and it must be built from evidence rather than an agent's self-assessment — a delegate
reporting success is exactly the claim under question. Candidate inputs, all already recorded:
whether the delegate's report correlated at all, whether tests were run and passed, whether the
work landed on a branch, whether any incident was raised against the session, and whether the task
settled normally rather than via the watchdog.

The failure mode to design against is specific and has already happened: on 2026-08-11 a task read
`Dispatched` for nine hours while its session had finished and its work was committed. A confidence
score built on task status would have been wrong in both directions. See CARD-0021, CARD-0029 and
CARD-0035.

**Ordering:** CARD-0039 and a defined confidence signal are both prerequisites for the conditional
arm. CARD-0040's unconditional part — Backlog → InProgress → Review driven off signals that already
exist — does not depend on either and can ship first.
