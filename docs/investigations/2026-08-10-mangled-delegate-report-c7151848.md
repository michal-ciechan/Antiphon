# A delegate report reached its caller spliced — and looked complete

**Task:** c7151848 (Debug) · **Date:** 2026-08-10 · **Status:** diagnosed and fixed

## Root cause, in one sentence

Nothing excerpted the report: a body of a few KB written to an agent's pty is silently
truncated by the terminal input path — it discards what does not fit and reports success — and the
one check that could have caught it (`ComposerDeliveryEvidence.IsVisible`) matches on the body's
**head OR tail**, which this failure mode always preserves.

## The evidence contradicts the stated hypothesis

The brief for this task proposed that `DelegationReportFormatter.FitReport` produced a deliberate
head+tail excerpt whose marker got lost. **That is not what happened, and `FitReport` never ran.**

`FitReport` only excerpts above `DelegationSettings.ReplyInlineMaxChars`, which was **20 000**. The
report was 5 368 characters, so `FitReport` returned it untouched with `Excerpted = false` — the
flag was correct. For the same reason `ResolveSpillFileAsync` returned `null` on its first line
(`if (report.Length <= ReplyInlineMaxChars) return null;`) without touching the filesystem: it did
not silently no-op or throw, it was never supposed to run at that size. That is why
`ResultFilePath` was empty.

So all three sub-questions have the same answer: **the ceiling was set so high that the excerpt
path, the marker and the spill file were all unreachable**, and a 5.4 KB body went straight to the
terminal.

Batching did not cause this cut either: the note was queued alone (`SessionQueuedMessages` row
`4cf3bd3a`, 5 471 chars, a run of one).

## What actually happened, measured

Aligning what was queued against what the receiving Claude recorded in its own JSONL transcript,
byte for byte:

**The report (task 0b0f558c → parent session `da374342`)** — 5 471 chars queued, **379 delivered**:

| kept | payload bytes | note |
|---|---|---|
| `src[0..246]` | `[6 .. 256]` | the opening |
| — | `[256 .. 5376]` | **5 120 bytes dropped = 5 × 1024** |
| `src[5339..5470]` | `[5376 .. 5514]` | the conclusion |

**This task's own brief (→ session `6f8a56ba`)** — 5 203 chars queued, **1 091 delivered**:

| kept | payload bytes | note |
|---|---|---|
| `src[0..1017]` | `[0 .. 1024]` | exactly the first 1024-byte chunk |
| — | `[1024 .. 5120]` | **4 096 bytes dropped = 4 × 1024** |
| `src[5112..5184]` | `[5120 .. 5199]` | the final partial chunk |

In both cases the dropped span is an **exact multiple of 1024 bytes**. Whole chunks are discarded;
the head and the tail always survive. That is why the result reads as coherent prose rather than as
obvious damage — and why the caller only noticed because the seam happened to fall mid-word.

### The size cliff

Every delegation delivery with a stored body, compared against what its recipient recorded:

| sent | delivered | |
|---|---|---|
| 1 004 | 1 004 | ok |
| 3 175 | 3 175 | ok |
| 4 097 | 4 097 | ok |
| 4 117 | 4 117 | ok |
| 4 262 | 4 262 | ok |
| **5 185** | **1 091** | lost 4 094 |
| **5 471** | **379** | lost 5 092 |

The cliff sits between **4 262 (intact)** and **5 185 (mangled)**.

### Confirmed independently, and it is not ours

A CI-runnable probe (pwsh child, `[Console]::In.ReadLine()`, one write through `PtyAgentRunner`):

| wrote | received |
|---|---|
| 1 000 / 2 000 / 3 000 / 4 000 | intact |
| 5 000 | 4 094 |
| 8 000 | 4 094 |
| 16 000 | 4 094 |
| 65 536 | 4 094 |

A hard ~4 KB console input buffer; everything past it is thrown away with no error, no short write
and no exception. Note the repo **already had a red test asserting this** —
`PtyAgentRunnerTests.Stdin_large_write_64KB_does_not_truncate` fails on a clean `master`, and has
been a standing, unnoticed reproduction of this exact mechanism.

Two things were ruled out by measurement, not by argument:

- **The loss is not in our code.** The chain server → HTTP → session-runner → length-framed pipe →
  pty-host → `WriteCoreAsync` is lossless: the fake Claude receives 43 KB in a single write without
  losing a byte, at any drain rate. `PtyLargeWriteTests` now pins that.
- **Pacing the write does not help.** I implemented chunked, paced writes in `WriteCoreAsync`
  (1024-byte chunks, 2 ms apart) and re-ran the probe: **byte-identical results** — 4 094 at every
  size. It is a fixed buffer cap, not a drain-rate race, so the change bought nothing and was
  reverted rather than shipped as an unvalidated timing change to the input path.

**Conclusion: a body that large cannot be made to survive the terminal. The only fix is not to send
one.**

## What changed

| file | change |
|---|---|
| `DelegationSettings.cs` | `ReplyInlineMaxChars` 20 000 → **3 000**; excerpt head/tail 6 000/6 000 → 1 800/900; new **`PtyInlineSafeChars` = 4 000**, documented with the measurement |
| `DelegationReportFormatter.cs` | `FitReport` snaps both cuts to a whitespace boundary (**never mid-word**); the banner now says outright that this is an excerpt, how much is missing, and where the rest is. New `BuildBriefPointer` |
| `AgentTaskDispatcher.cs` | `FitBriefForTyping` — a brief over the ceiling is written to `.antiphon/task-<id>-brief.md` and replaced by a short pointer that keeps the correlation marker; falls back to the API URL if the file cannot be written |
| `SessionMessageQueueService.cs` | any body over `PtyInlineSafeChars` logs an error and raises an incident before delivery — oversize typing is never invisible again |
| `AgentIncidentKind.cs` | new `OversizedTerminalDelivery = 14` |
| `Antiphon.FakeClaude/Program.cs` | `ANTIPHON_FAKE_STDIN_READ_DELAY_MS` models a TUI that renders between reads |
| `.gitignore`, `scripts/cleanup-build-junk.ps1` | cover `bin-report*/` |

Because the ceiling is now reachable, `ResolveSpillFileAsync` actually fires: a 5 KB report gets a
real spill file and the excerpt points at it. 3 000 was chosen so an ordinary report still arrives
whole — the report *is* the deliverable — while the complete note stays inside the largest body
measured to arrive intact.

### Tests

- `PtyLargeWriteTests` (new, 4 tests) — our stack delivers 5.4 KB and 21 KB whole, at slow drain
  too, with an explicit assertion on the **middle** (the check that head-or-tail matching cannot
  make).
- `DelegationReportFormatterTests` (+6) — the shipped ceiling stays under the measured cliff; an
  excerpt never cuts mid-word; an excerpt says plainly it is not the whole report; an oversized
  brief becomes a pointer that keeps the task marker and is itself small enough to type.
- `AgentTaskReplyIntegrationTests` (+1) — the live miss at its exact size through the **shipped**
  settings: spill file written, note under the pty-safe size, marked as an excerpt, opening and
  conclusion both present. All assertions scoped to rows the test created.

`Antiphon.Tests`: 712 tests, all green except two pre-existing failures unrelated to this change,
both verified red on a clean tree —
`CodexAdapterLocalShellTests.Question_detection_ignores_question_mark_in_prompt_echo` and
`PtyAgentRunnerTests.Stdin_large_write_64KB_does_not_truncate`.

## Still open

1. **`Stdin_large_write_64KB_does_not_truncate` asserts something the platform does not provide.**
   Left untouched — it is pre-existing and rewriting another test's meaning is the user's call —
   but the measurement above says it should be restated as "documents the ~4 KB cap" or removed.
2. **Only the delegation paths have a file fallback.** Channel and UI messages over 4 000
   characters now raise an incident but are still typed. A generic "spill to `.antiphon/inbox/` and
   type a pointer" helper would close that.
3. **Verification still cannot detect truncation.** `ComposerDeliveryEvidence` matches head or
   tail by necessity (a large paste renders as `[Pasted text #N +X lines]`, so the middle is not on
   screen). The ground truth is the recipient's own transcript — comparing what was recorded
   against what was sent would make any future shortfall detectable rather than certifiable.
