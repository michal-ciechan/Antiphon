# Tasks stuck Dispatched forever (2026-08-11)

Three delegated tasks — `6e82003d` (CARD-0006 implement), `eb629fba` (CARD-0019 plan),
`d69ac19d` (CARD-0020 plan) — sat `Dispatched` from 22:43 on 2026-08-10 with events `Created`
then `Dispatched` and nothing after. Their sessions ran, did real work, and the server logged
`SessionFinished` for all three between 00:16 and 00:48.

## Root cause

**The correlation marker was emitted once, at the head of the brief, and the pty dropped the
head.**

`AgentTaskReplyService.ExtractMarkedTurnAsync` settles a task only if the last `UserPrompt`
before the turn's `TurnEnd` contains `[antiphon-task:<short8>]`. `BuildBrief` put that marker in
the first token of the brief and nowhere else. When the head of the brief did not survive
delivery, every turn-end failed the gate, `OnTurnEndAsync` logged at **Debug** — under a Serilog
file sink set to Information — and returned. The delegates finished, reported, and their tasks
sat Dispatched with no record anywhere of why.

## Evidence

Queued body (`SessionQueuedMessages.Body`) aligned against what the delegate actually recorded
(`TranscriptEntries`, first `UserPrompt`), offset measured with `strpos`:

| session  | queued | arrived | dropped head | shape |
|----------|--------|---------|--------------|-------|
| 61023d29 | 4262   | 4262    | 0            | intact |
| 6f8a56ba | 5185   | 1091    | 0            | head kept, middle dropped |
| da438fff | 4728   | 634     | 0            | head kept, middle dropped |
| 83f42759 | 1366   | 380     | 986          | **only the final chunk** |
| 1ba90779 | 1402   | 380     | 1022         | **only the final chunk** |
| ab425cfb | 1431   | 409     | 1022         | **only the final chunk** |
| d681178e | 2320   | 274     | 2046         | **only the final chunk** |

The cut lands at **byte 1024n − 2**. The character offsets read 986 where the byte offset was
1022 because the briefs are full of em-dashes (3 bytes each in UTF-8).

Every task whose brief kept its head settled. Every task whose brief lost its head is stuck.
That is the whole correlation.

## What the previous investigation got wrong

`eb24e56` (2026-08-10, `docs/investigations/2026-08-10-mangled-delegate-report-c7151848.md`)
concluded the loss takes only the MIDDLE and that "the head and the tail always survive". The
four deliveries above are the counter-example: the head is exactly what was lost. That reading
was also load-bearing in three doc comments (`DelegationSettings.PtyInlineSafeChars`,
`AgentIncidentKind.OversizedTerminalDelivery`, the size gate in `SessionMessageQueueService`),
all corrected here.

The size ceiling is likewise not a safety guarantee. All four stranded bodies were
**1 366–2 320 characters** — far below `PtyInlineSafeChars` (4 000) and below
`ReplyInlineMaxChars` (3 000) — so no size guard fired and no incident was raised. A 4 262-char
body arrived intact at 20:01 while a 1 402-char body lost its head at 22:43, so this is not a
size threshold at all.

### The AppHost restart was a red herring

The framing was "everything dispatched before the restart settled, everything after is stuck".
The restart is not causal. Mangling was already happening before it (5 185 → 1 091 at 20:33,
4 728 → 634 at 20:59); those tasks settled anyway *because the mangling kept their head*. What
changed after the restart was the SHAPE of the loss, not whether it occurred.

## The fix

1. **`ReportingContract` closes with the task marker**, so the brief carries it at both ends.
   The tail survived in all seven measured deliveries, across both mangling shapes; a marker at
   each end correlates whichever fragment lands. This is the fix for the stuck tasks.
2. **An uncorrelated report is no longer Debug-silent** (CARD-0003). When a turn ends with
   assistant text but fails the marker gate, `AgentTaskReplyService` logs at Warning and records
   a `DelegateReportUncorrelated` incident + alert — once per session, since a stranded delegate
   keeps ending turns.
3. **The delivery watchdog can now fail it.** `FailNeverStartedAsync` treated any transcript
   entry as proof of health, so a session that was busily working but never correlating was
   waved through forever. It now also fails a task past the window whose session carries that
   incident, with a reason that says the work may be real and names the session to read.

## Not fixed here

Why ConPTY drops the leading chunks of a sub-2 KB write is still unexplained, and the numbers
rule out the simple stories: not a fixed 4 KB cap (4 262 arrived whole), not a pure size
threshold (1 402 did not). The mitigation above is deliberately transport-independent — it makes
correlation survive the loss rather than betting on delivery being lossless. A transport fix
needs its own investigation; `PtyLargeWriteTests` covers our own stack and shows it lossless to
21 KB, so the loss is below that, in conhost or the TUI's paste handling.

## Regression tests

- `a_brief_that_lost_its_head_in_the_pty_still_settles_the_task` — replays the measured cut
  (byte 1024n−2, final chunk only) on a real `BuildBrief` output at the size that actually
  stranded. Red before the trailing marker: the task stays Dispatched.
- `a_report_that_cannot_be_correlated_raises_an_incident` and
  `the_uncorrelated_incident_is_raised_once_not_once_per_turn`.
- `a_task_whose_report_could_never_be_correlated_fails_instead_of_hanging` — red before the
  watchdog branch.
