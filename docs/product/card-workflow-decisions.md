# Card workflow — decisions taken outside the cards

**Why this file exists:** cards are write-once and there is no edit endpoint (CARD-0019), so a
decision that refines an already-filed card has nowhere to live. Chat is not a record. Until
CARD-0019 ships, refinements land here and should be folded into the card once it can be edited.

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

**Known gap:** the reason only persists on a move into a terminal column, where it becomes
`Card.TerminalReason`. On every other move it is accepted and dropped, because there is no per-card
history to hold it. `AuditRecord` is not that home — it is workflow and LLM-cost oriented. The right
home is CARD-0019's `CardRevision`, which makes this the second caller waiting on that card. The API
shape is deliberately correct ahead of the storage: callers should pass a reason now rather than
learn not to.

**Not changed, deliberately:** `InProgress → Done` and `Blocked → Done` remain forbidden. The same
argument applies to both and they are probably worth allowing, but widening a considered state
machine further than asked is a decision for the operator, not an inference.

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
