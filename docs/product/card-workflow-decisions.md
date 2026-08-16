# Card workflow — decisions taken outside the cards

**Why this file exists:** cards used to be write-once with no edit endpoint, so a decision that
refined an already-filed card had nowhere to live. Chat is not a record.

**CARD-0019 has shipped.** Cards can now be corrected in place — `PATCH /api/cards/{id}/content`
(mandatory reason, immutable `CardRevision` history), `POST /api/cards/{id}/archive`/`/unarchive`,
and `GET /api/cards/{id}/revisions`. That closes the gap this file existed to patch: a decision
that refines one specific card now belongs on that card, not here.

**What happened to the rest of this file:** this file has been trimmed to the entries that
genuinely don't have a single card to attach to — cross-cutting workflow/policy calls made while
doing card work, not a correction to one card's own record. Two entries that *were* here —
"Moving a card into In Progress SPAWNS AN AGENT" and "Auto-transition stops at Review,
conditionally" — were both scoped as hard constraints on CARD-0040 specifically. They have been
removed from this file; that content belongs in CARD-0040's own description/history now that a
card can carry corrections directly, not in a side file.

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

*(Two entries formerly here, both hard constraints on CARD-0040 — "Moving a card into In Progress
SPAWNS AN AGENT" and "Auto-transition stops at Review, conditionally" — were trimmed out; see the
note at the top of this file.)*
