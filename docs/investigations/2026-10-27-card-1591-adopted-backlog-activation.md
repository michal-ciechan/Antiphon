# CARD-1591 adopted-notice backlog activation

This is a plan only. It does not flush, cancel, reorder, or send anything.
Session `b2f1a869-9bc5-4974-b8ae-53686448fd20` and its pending notes stay untouched.

The historical count was about 340 Pending notes, almost all at zero attempts,
with two already parked. That count is not a live reading. Re-read the queue
before any activation and keep the producer and queue identities that already exist.

## What must stay true

A note is delivered only when the adopted session shows an empty idle composer,
or Enter when that composer holds the body this attempt typed, and the bound
transcript is caught up on TurnEnd `end_turn`. A working spinner, a dialog, or an
operator draft refuses the write. The receipt is one complete UserPrompt above
that attempt's floor. Queue disappearance, Sent, an ack, or a healthy runner is
not delivery.

The runner that will receive the notes must already be the build that rechecks
the screen and the transcript at the write. An older runner omits the live
composer flags and fails closed, which charges a still-withheld row once and can
disable the zero-attempt shrink of a polled completion note. Do not point this
backlog at an old runner.

## Release shape

1. Reconcile receipts first. Any Pending or Sent row whose complete UserPrompt is
   already above its floor is confirmed and left alone. Parked rows are included
   in that reconciliation and are not retyped.
2. Cancel or supersede notes whose obligation is gone: a newer note for the same
   producer replaces the older one, and a note older than the session's current
   work is expired or folded into one digest. Keep notes that are still the
   current obligation.
3. Release the remainder in the queue's existing order (rules priority, then
   sequence), one head run at a time, and only while the screen is an empty idle
   composer. Bound the rate to a few notes per idle turn and stop when the screen
   leaves that state. Do not batch unrelated notes into one submit.
4. After each submit, wait for that note's complete UserPrompt before the next
   body. A withhold stays Pending at one attempt, with no typing stamp, and is
   not parked. A real dialog stays at zero attempts.

No expiry, digest, or rate limit is implemented here. Activation is a separate
operator step after the guard is on the runner that owns the session.
