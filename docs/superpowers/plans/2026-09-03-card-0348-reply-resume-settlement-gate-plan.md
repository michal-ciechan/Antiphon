# CARD-0348 — a reply starts a new turn: settlement refuses the pre-reply boundary, and elapsed counts from the reply

**Date:** 2026-09-03

**Plan task:** 1dcf13b9 (Frontier, plan only; no production code changed)

**Card:** CARD-0348 — "Task status/report can stay stuck on a stale pre-reply snapshot after -Reply, and elapsed-time display may be misleading"

**Evidence base:** the Debug pass b6ee66c5 (verdict on the task row; live gym-stat DB rows and transcripts for `a571d6c1`, `42bb5ffb`, `fbd37f59`, `fdcae70a`), re-verified against this tree at `1311251e`: `AgentTaskReplyService` (`OnTurnEndAsync` / `OnTurnEndLockedAsync`, `ExtractMarkedTurnAsync`, `AnswerAsync`, `ClassifyReportAsync`, `SettleAsync`, `BlockUnmarkedWaitingAsync`), `AgentTaskDispatcher.SettleDeferredReportsAsync` (arm 0) and `FailNeverStartedAsync` (the CARD-0340 S2 clock), `DeferredReportSweepMarks`, `TranscriptPromptSpan`, `AgentTaskService.RequeueAsync` / `GetSummaryAsync`, `AgentTaskLandService` (the Conflicted block), `DelegateCheckProbe.GatherAsync`, `AgentTaskCheckService.BuildNote`, `DelegationReportFormatter.BuildCompletionNote`, the CARD-0248 / CARD-0288 / CARD-0320 / CARD-0340 plans, `AgentTaskSettlementRaceTests`, `AgentTaskDeliveryWatchdogTests`, `AgentTaskAnswerTests`, `DelegateCheckProbeTests`, `DelegationUnitTests`.

## Decision

Two fixes, one migration, one card.

**Settlement.** A reply to a Blocked task starts a **new turn**. Until that turn ends, the session's newest `TurnEnd` is still the boundary the block was settled from, and every settlement entry point walks back from the newest `TurnEnd`. The fix is a per-task **reply watermark**: `AnswerAsync` records the transcript high-water mark (`MAX(TranscriptEntries.Sequence)` for the delegate's session) on the row when it flips `Blocked → Working`, and `ExtractMarkedTurnAsync` refuses any turn whose **prompt** is at or below it. Arm 0 of the deferred sweep short-circuits on the same watermark before it hands off, so it stops re-invoking settlement (and logging a Warning) every 60 s while the delegate is answering. The gate is on the prompt, not the boundary, for a reason given below. `OnTurnEndAsync` stays closed to `Blocked` tasks: with the gate, the row is `Working` when the real report arrives, which is the whole fix, and opening settlement to `Blocked` would let a human typing in a stuck delegate's terminal close a task nobody answered.

**Elapsed.** The check header (`AgentTaskCheckService.BuildNote` via `DelegateCheckProbe.GatherAsync`) and the completion note (`DelegationReportFormatter.BuildCompletionNote`) measure from `max(DispatchedAt, RepliedAt)`, where `RepliedAt` is a new column stamped by every reply. When a reply reset the clock, both surfaces say so and keep the since-dispatch figure beside it, so a reader sees both numbers and which one "elapsed" is. This is **not** `AgentSession.LaunchResumedAt`: that is CARD-0340's session-level interrupted-launch clock, stamped by the reconciler's pass 1c, read only by `FailNeverStartedAsync`, and never touched by a reply. Different field, different path, different event — the investigation was explicit and this plan keeps them apart.

## Ground truth, as verified

**The stuck status is a real DB write.** `a571d6c1` and `42bb5ffb` are `Blocked` in Postgres with `Result` equal to their first blocked report, while their transcripts carry a later `[antiphon-report:<id> done]`. `GET /api/agent-tasks/{id}` and `delegate.ps1 -Status` read that row; there is no cache to invalidate.

**The mechanism, step by step, in today's code.**

1. `AnswerAsync` (Blocked branch, `AgentTaskReplyService.cs:291-328`): `Status = Working`, a `Replied` event, the answer enqueued `WhenIdle` with the task marker. Nothing about the transcript changes.
2. `SettleDeferredReportsAsync` arm 0 (`AgentTaskDispatcher.cs:1904-1968`) selects `Dispatched|Working` tasks, finds the session's newest `TurnEnd`, and looks for `[antiphon-report:<id>]` anywhere in the session's `AssistantText`. The old blocked report still carries that token. The CARD-0320 gates (`stillOpen`, `IsSettleInFlight`) both pass — the task *is* open again and no settle is in flight. `DeferredReportSweepMarks.Prune` dropped the session's 60 s watermark when the task left the open set at the original block, so arm 0 hands off on the very next 5 s tick. Measured re-block lag: 0.4–4 s after every reply.
3. `OnTurnEndLockedAsync` → `ExtractMarkedTurnAsync` (`:1723`) walks back from that same newest `TurnEnd` to the last prompt before it — the original marked brief — collects the old blocked report, and `SettleAsync` writes `Blocked` again with the same `Result` and a second `Blocked` event. The parent-note digest skip (CARD-0320) hides the duplicate note from chat, so the caller sees nothing wrong.
4. The delegate's answer turn then ends with a real `done`. `OnTurnEndLockedAsync` loads only `Dispatched|Working` (`:105-107`); the row is `Blocked`; the report is dropped on the floor forever.

**CARD-0320 is not the fix and its tests do not cover this.** CARD-0320 serialised *concurrent* `OnTurnEndAsync` calls and de-duplicated the parent note. This is a *sequential* re-settle after a legal state change. `AgentTaskSettlementRaceTests` has three tests: concurrent settle, digest skip, kill-vs-retire. None replies.

**The same shape exists for a conflict reply.** `AgentTaskLandService.cs:153-158` flips a `Succeeded` Worktree task to `Blocked` (`Conflicted` event) on a rebase conflict. The session's newest boundary is the *done* turn. A `-Reply` on that task (the Blocked branch accepts any Blocked task with a session) goes `Working`, arm 0 sees the done token on the old boundary, and the task re-`Succeeds` on the stale done report before the delegate has resolved anything. Same gate, same fix; the plan tests it.

**CARD-0248's nudge gate is the precedent, and it is not reusable as-is.** `ClassifyReportAsync` (`:2256-2280`) already refuses a boundary that is the same as, or predates, the nudge (`boundary.Sequence <= ReportNudgedSequence || boundary.CreatedAt <= sentAt`). It runs only on the unmarked settle-anyway path and only after the report has been extracted. The reply problem is upstream of it: the marked path never reaches `ClassifyReportAsync`'s gates, and the stale report *is* marked.

**Elapsed is a separate clock with two consumers.** `DelegateCheckProbe.GatherAsync` (`:226-227`) sets `Age = now - DispatchedAt`; `BuildNote` renders it as `"{age} elapsed (expected {n}m)"` and the facts digest prints `elapsed=`. `BuildCompletionNote` (`:373-374`) renders `FormatDuration(CompletedAt - DispatchedAt)`. The `fbd37f59` note at 10:21:08 literally reads `· 2h24m ·` for a delegate whose reply had landed 34 s earlier. The `last activity … ago` bit on the same header line was honest; `elapsed` was the misleading number. Neither consumer has any resume-aware input today, and `RequeueAsync` (`AgentTaskService.cs:1409-1445`) is the only place attempt-scoped clocks are reset.

## Why the gate is on the prompt, not the boundary

The natural first cut is "ignore a `TurnEnd` at or below the watermark". It is necessary but not sufficient. A boundary can land *after* the reply is recorded with **no new prompt in front of it** — a synthetic `SessionRestartBoundary` written by a relaunch, or an interrupted-turn marker — and its sequence is above the watermark. `ExtractMarkedTurnAsync` would walk back from it to the last prompt before it, which is the *old marked brief*, collect the old report, and re-settle exactly as before. Requiring `prompt.Sequence > RepliedAtSequence` closes both cases with one comparison: the reply's own `UserPrompt` is always persisted after the reply is recorded (arrival-ordered, append-only sequences), so the genuine answer turn passes, and any turn that walks back to a pre-reply prompt is refused. Because `prompt.Sequence < end.Sequence`, the prompt gate implies the boundary gate; the sweep, which has only the boundary in hand, uses the boundary form as a cheap pre-filter.

**Why a sequence and not a time.** `ReportNudgedSequence` (CARD-0248) established the identity idiom: per-session, monotonic, always present, totally ordered, and the comparison is "strictly later", which is what this predicate needs. `TranscriptPromptSpan.PromptRow` already carries `Sequence` and no `CreatedAt`; a time gate would need a new projection field and a cross-table clock comparison of the kind CARD-0046 spent a slice un-doing. The one thing the sequence gate cannot see is backfill re-ordering — but backfill re-persists rows that were *missed*, and the blocked boundary was, by construction, persisted (the block was settled from it), so it cannot be missed and rebased above the watermark.

**Why the overlay reply does not stamp the watermark.** The CARD-0241 in-turn path (`AnswerAsync` Dispatched/Working branch, `:330-359`) answers an `ask_user_question` popup *inside the brief's turn*. That turn's prompt is the brief, which legitimately predates the reply. Stamping the watermark there would refuse the brief's own settlement. The overlay path stamps `RepliedAt` (the delegate was waiting; elapsed should reset) and leaves `RepliedAtSequence` null. This is also why there is **no backfill** from `Replied` events: it could not tell the two shapes apart, and any task replied to before deploy is already past the 0.4–4 s window in which the bug fires.

## Alternatives considered and rejected

- **Open `OnTurnEndLockedAsync` to `Blocked` tasks so the later done report settles.** Without the gate the stale re-block still happens and the caller still reads a lie for the whole answer turn. With the gate it is unnecessary. On its own it also lets a human typing into a blocked delegate's terminal, or a stray boundary, close a task nobody answered. Rejected; the investigation said the same.
- **Time gate on `Replied.At` vs `TurnEnd.CreatedAt` (no schema change).** Fails the restart-boundary case above (boundary-only), needs a new `PromptRow` field to become a prompt gate, and puts a clock comparison where the codebase already chose sequence identity. Rejected.
- **Strip or neutralise the old `[antiphon-report:]` token so arm 0 stops matching.** Transcript rows are evidence; rewriting them is off the table. Arm 0 also is not the only entry point (live observer, dead-session reconciler, arms 1 and 2 all reach `ExtractMarkedTurnAsync`). Rejected.
- **Re-stamp `DispatchedAt` on reply for the elapsed fix.** `FailNeverStartedAsync`, `DelegationUsageRollup.ForSessionAsync`, `TranscriptPromptSpan.LoadAsync` and the stall clock all key `DispatchedAt`; CARD-0340 rejected the same move for the same reason. Rejected.
- **Reuse `AgentSession.LaunchResumedAt` for elapsed.** Session-level, reconciler-owned, watchdog-only; a reply does not resume a launch. Rejected, per the investigation.

## S1 — the reply watermark: settlement refuses a pre-reply turn

**Files**

- `server/Domain/Entities/AgentTask.cs`
- `server/Infrastructure/Data/AppDbContext.cs`, `server/Migrations/AppDbContextModelSnapshot.cs`, `server/Migrations/20260903230000_AddAgentTaskRepliedAt.cs` (new)
- `server/Application/Services/AgentTaskReplyService.cs`
- `server/Application/Services/AgentTaskDispatcher.cs`
- `server/Application/Services/AgentTaskService.cs`
- Tests: `tests/Antiphon.Tests/Application/AgentTaskSettlementRaceTests.cs` (extend), `AgentTaskDeliveryWatchdogTests.cs`, `AgentTaskAnswerTests.cs`, `AgentTaskReplyOverlayTests.cs`, `AgentTaskServiceIntegrationTests.cs`

1. **Schema.** Two nullable columns on `AgentTask`, placed with the CARD-0248 nudge columns:

   ```csharp
   /// <summary>
   /// When the caller last answered this task (CARD-0348) — the At of the newest Replied event,
   /// on the row so the elapsed clocks (check header, completion note, status DTO) read it without
   /// a timeline query. Stamped by both reply paths. Null until the first reply; cleared by
   /// RequeueAsync because it describes this attempt only.
   /// </summary>
   public DateTime? RepliedAt { get; set; }

   /// <summary>
   /// Transcript high-water mark (max TranscriptEntries.Sequence on the delegate's session) at the
   /// moment a Blocked task was answered (CARD-0348). The answer starts a NEW turn; until it ends
   /// the session's newest TurnEnd is the boundary the block was settled from, and settlement
   /// walking back from it re-Blocked the row on the stale report within one 5 s tick. Settlement
   /// refuses any turn whose PROMPT is at or below this — the prompt, not the boundary, so a
   /// promptless boundary (restart marker) cannot walk back to the old brief either. Stamped only
   /// by the Blocked → Working reply; the in-turn question-tool reply leaves it null because that
   /// turn's prompt is legitimately older than the reply. Cleared by RequeueAsync: sequences are
   /// per session.
   /// </summary>
   public long? RepliedAtSequence { get; set; }
   ```

   `AppDbContext`: `entity.Property(t => t.RepliedAt).IsRequired(false); entity.Property(t => t.RepliedAtSequence).IsRequired(false);` next to the CARD-0248 lines. Snapshot: both properties in the `AgentTask` block, alphabetical (`timestamp with time zone`, `bigint`). Migration `20260903230000_AddAgentTaskRepliedAt`: generate with `dotnet ef migrations add AddAgentTaskRepliedAt --project server` when `bin/` is free; otherwise hand-write it exactly as `20260903220000_AddAgentTaskStandingAuthority.cs` does (attributes inline, snapshot edited by hand, comment says why). Two `AddColumn`s, both nullable. **No backfill** (see above).

2. **`AnswerAsync`, Blocked branch.** Before the status flip:

   ```csharp
   var now = UtcNow();
   var watermark = await db.TranscriptEntries.AsNoTracking()
       .Where(t => t.AgentSessionId == blockedSessionId)
       .MaxAsync(t => (long?)t.Sequence, ct) ?? 0;
   task.Status = AgentTaskStatus.Working;
   task.RepliedAt = now;
   task.RepliedAtSequence = watermark;
   ```

   `?? 0` is correct for a session with no rows: nothing can be stale, every prompt passes. Use the same `now` for the `Replied` event (it already is a local). No `CatchUpTranscriptAsync` here: the block was settled from persisted rows, the overlay branch's catch-up exists for a different reason (it must see an open tool call), and a catch-up against a stopped session is not a risk this path should take. **Overlay branch** (`Dispatched/Working` with an open question tool): `task.RepliedAt = now` only, sharing the event's `now`. `ContinueWithAuthorityAsync` goes through the Blocked branch and needs nothing.

3. **`ExtractMarkedTurnAsync`.** Immediately after `prompt` resolves (`:1750`) and before the `AssistantText` query:

   ```csharp
   // CARD-0348: the answer to a Blocked task starts a NEW turn. Until it ends, the newest
   // boundary is the one the block was settled from, and arm 0 re-handed it within one tick —
   // re-Blocking the row on the stale report, after which the real done report could never
   // settle (a571d6c1, 42bb5ffb, fbd37f59). On the PROMPT, not the boundary: a boundary with no
   // new prompt in front of it (a restart marker) walks back to the old brief and would re-settle
   // it too.
   if (task.RepliedAtSequence is long replyWatermark && prompt.Sequence <= replyWatermark)
       return new TurnOutcome(null, false, PreReplyBoundary: true);
   ```

   `TurnOutcome` gains `bool PreReplyBoundary = false` with a doc line ("the newest boundary belongs to a turn that predates the caller's reply; not a verdict, wait for the answer turn"). `Boundary` stays null on it, so `BlockUnmarkedWaitingAsync`'s existing `turn.Report is null && turn.Boundary is null` return already covers that path — add `|| turn.PreReplyBoundary` to its explicit early-return list anyway so the intent is visible. The `Interrupted` check stays ahead of the gate (a cancelled newest boundary is a fact about the session regardless), and `RecordInterruptedTurnAsync` is already deduped on sequence.

4. **`OnTurnEndLockedAsync`.** In the `turn.Report is not string report` block, first branch:

   ```csharp
   if (turn.PreReplyBoundary)
   {
       _logger.LogDebug(
           "Session {SessionId}: newest boundary predates task {ShortId}'s reply watermark #{Watermark}; waiting for the answer turn",
           sessionId, DelegationReportFormatter.Short(task.Id), task.RepliedAtSequence);
       return;
   }
   ```

   Debug, not Warning: this is the expected state for the whole of every answer turn.

5. **`SettleDeferredReportsAsync`, arm 0.** Add `t.RepliedAtSequence` to the `openTasks` projection. After `markedTaskId` resolves and before `_sweepMarks.ShouldHandOff`:

   ```csharp
   var marked = sessionTasks.First(t => t.Id == tid);
   if (marked.RepliedAtSequence is long replyWatermark && end.Sequence <= replyWatermark)
   {
       _logger.LogDebug(
           "Task {ShortId}: marked report at boundary {Sequence} predates the reply watermark #{Watermark}; not re-handing",
           DelegationReportFormatter.Short(tid), end.Sequence, replyWatermark);
       continue;
   }
   ```

   No watermark recorded, nothing counted in `swept`. The existing Warning ("marked report already in transcript … re-invoking settlement") therefore no longer fires every 60 s during an answer turn. Arms 1 and 2 are not gated separately: arm 1 cannot fire on the stale boundary (its report text landed), arm 2 needs `SubagentGraceMinutes` of total silence, and both end in `ExtractMarkedTurnAsync`, which is inert. The dead-session reconciler's CARD-0288 hand-off (`:1365`) is covered the same way.

6. **`RequeueAsync`.** Next to `task.RecoveredAt = null;`: `task.RepliedAt = null; task.RepliedAtSequence = null;` with the same one-line reason (attempt-scoped; sequences are per session — a stale watermark from the old session would refuse the new session's first hundred rows). The three dispatcher claim sites take Queued rows, which are either fresh (null) or requeued (cleared), so they need nothing. `RelaunchWedgedAsync` relaunches a `Dispatched` task that has never been Blocked-replied; nothing to clear.

   *Observation for a separate card, not this one:* `RequeueAsync` does not clear `ReportNudgedAt` / `ReportNudgedSequence` / `ReportNudgeMessageId` either. A retried task that was nudged on its previous attempt skips the nudge on the new one and compares old-session sequences against new-session boundaries in `ClassifyReportAsync`. Same class of defect; out of scope here.

7. **Tests — `AgentTaskSettlementRaceTests`, extended, not replaced.** The harness gains nothing new (`AgentTaskDispatcher` is already registered scoped; `DeferredReportSweepMarks` stays unregistered, which means "hand off every tick"). Helpers: `SeedMarkedTurnAsync` takes a `verdict` argument (today it hard-codes `done`); new `SeedBlockedTaskAsync(verdict, resultText)` = the shared-task seed with `Status = Blocked`, `Result` set, a `Blocked` (or `Conflicted`) event, and one stale marked turn (sequences 1–3); new `SeedAnswerTurnAsync(sessionId, taskId, answer, report)` = `UserPrompt` of `TaskMarker + "\n\n" + answer`, `AssistantText` of report + done token, `TurnEnd end_turn`.

   - `an_answered_blocked_task_is_not_re_blocked_by_the_stale_boundary`: seed Blocked with a stale `blocked` turn; `AnswerAsync("proceed")`; assert `Working`, `RepliedAtSequence == 3`, `RepliedAt` set. Run `SettleDeferredReportsAsync` twice from a fresh scope, then `OnTurnEndAsync` directly. Assert: still `Working`; exactly one `Blocked` event; `Result` unchanged; zero Delegation notes on the parent session; the reply's queue row still `Pending`.
   - `the_answer_turn_settles_the_task_and_delivers_one_done_note`: same start, then `SeedAnswerTurnAsync` (sequences 5–7 — the reply's own prompt is above the watermark); one sweep. Assert `Succeeded`, `Result` is the new report, `ReportEvidence.Marked`, one `Completed` event, exactly one parent note containing `[task <id> done]`.
   - `a_stale_done_boundary_after_a_conflict_reply_does_not_re_succeed`: Blocked with a `Conflicted` event over a stale `done` turn (the land-conflict shape, without land machinery); reply; sweep + direct `OnTurnEndAsync`. Assert `Working`, no `Completed` event.

   **`AgentTaskDeliveryWatchdogTests`** (sweep-level pin, `ListLogger`): `arm_0_skips_a_boundary_at_or_below_the_reply_watermark` — `SeedDispatchedTaskAsync` + `SeedMarkedReportTurnAsync`, set `RepliedAtSequence` to the boundary's sequence directly, `ReportSweepRehandSeconds = 0`; sweep; still `Dispatched`, logs do **not** contain "re-invoking settlement".

   **`AgentTaskAnswerTests.answers_a_blocked_task_and_enqueues_marker_plus_text_WhenIdle`**: add `stored.RepliedAt.ShouldBe(replied.At)` and `stored.RepliedAtSequence.ShouldBe(<MAX sequence the harness inserted>)`. **`AgentTaskReplyOverlayTests`** (in-turn path): `RepliedAt` set, `RepliedAtSequence` null. **`AgentTaskServiceIntegrationTests`** retry test: seed both non-null, retry, both null.

## S2 — elapsed counts from the reply, and says so

**Files**

- `server/Application/Services/AgentTaskResumeClock.cs` (new, ~15 lines)
- `server/Application/Services/DelegateCheckProbe.cs`, `AgentTaskCheckService.cs`, `DelegationReportFormatter.cs`
- `server/Application/Dtos/AgentTaskDtos.cs`, `server/Application/Services/AgentTaskService.cs` (`GetSummaryAsync`)
- `client/src/api/agentTasks.ts`
- Tests: `DelegateCheckProbeTests.cs`, `AgentTaskCheckInterpreterTests.cs` (or a direct `BuildNote` unit test beside it), `DelegationUnitTests.cs`

1. **The rule, in one place.** `internal static class AgentTaskResumeClock` with `static DateTime? ActiveSince(AgentTask task)` = `RepliedAt > DispatchedAt ? RepliedAt : DispatchedAt` (null when both are null) and `static bool WasResumed(AgentTask task)`. Its doc names CARD-0340's `LaunchResumedAt` as the thing it deliberately is not.

2. **Check probe.** `GatherAsync`: `Age = ActiveSince(task) is { } from ? now - from : null`. `CheckTaskFacts` gains `DateTime? RepliedAt` after `DispatchedAt`; the facts digest line that prints `dispatched=… elapsed=…` adds ` replied=<u>` when non-null, so the interpreter's reading is grounded in the same fact.

3. **Check header.** `BuildNote` keeps the `"{age} elapsed (expected {n}m)"` bit byte-for-byte (tests and readers key on it) and, when `facts.Task.RepliedAt > facts.Task.DispatchedAt`, appends one bit right after it: `after reply (dispatched {FormatAge(facts.At - DispatchedAt)} ago)`. For the card's instance the header reads `34s elapsed (expected 60m) · after reply (dispatched 2h24m ago)`.

4. **Completion note.** In `BuildCompletionNote`, the duration bit becomes: unchanged `FormatDuration(finished - started)` when there was no reply; otherwise two bits, `{FormatDuration(finished - replied)} since reply` and `{FormatDuration(finished - started)} since dispatch`. Static method, reads the task row, so none of its five callers change.

5. **Status DTO.** `AgentTaskSummaryDto` gains a trailing `DateTime? RepliedAt = null` (trailing and defaulted so no positional constructor call moves); `GetSummaryAsync` passes `task.RepliedAt`; `client/src/api/agentTasks.ts` adds `repliedAt: string | null` to the summary type. No UI rendering is required by this card; `delegate.ps1 -Status` prints the summary line and result only and is untouched.

6. **Deliberately untouched clocks**, each measuring the whole attempt on purpose: `DelegationUsageRollup.ForSessionAsync(… task.DispatchedAt …)` (spend must include pre-block work), `FailNeverStartedAsync` (CARD-0340's clock), `AutoEscalateStalledAsync` (already keys last transcript progress), `TranscriptPromptSpan.LoadAsync(dispatchedAt)`.

7. **Tests.** `DelegationUnitTests.the_completion_note_measures_duration_from_the_latest_reply`: `NewTask()` with `RepliedAt = CompletedAt - 34 s` → note contains `34s since reply` and `4m12 since dispatch`; the existing `4m12` test is unaffected. `DelegateCheckProbeTests.age_counts_from_the_latest_reply_not_dispatch`: `DispatchedAt` 144 min ago, `RepliedAt` 34 s ago → `Age < 1 min`, `facts.Task.RepliedAt` set. A `BuildNote` unit test with a hand-built `CheckFacts`: header contains `elapsed (expected ` and `after reply (dispatched 2h24m ago)`; without a reply it contains no `after reply`.

## Order, tiers, estimate

S1 then S2; S2 reads the column S1 ships. **Dispatch both as one Worktree task at Frontier** (the card asks for CARD-0320-grade care and this is the settlement path; the memory's Grok/Codex routing is for simple builds, which this is not). One migration, one land.

| Slice | Verification floor | Authoring | Band |
|---|---|---|---|
| S1 | ~10 min (five test classes, targeted) | 40–60 min | 50–75 min |
| S2 | ~5 min | 25–35 min | 30–45 min |

## Verification

Build to an alternate output path (daemons hold `bin/`), forward slash, and delete the `bin-0348` directories afterwards:

```powershell
dotnet build server --property:OutputPath=bin-0348/
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0348/ -- --treenode-filter "/*/*/AgentTaskSettlementRaceTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0348/ -- --treenode-filter "/*/*/AgentTaskDeliveryWatchdogTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0348/ -- --treenode-filter "/*/*/AgentTaskAnswerTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0348/ -- --treenode-filter "/*/*/AgentTaskReplyOverlayTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0348/ -- --treenode-filter "/*/*/AgentTaskServiceIntegrationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0348/ -- --treenode-filter "/*/*/DelegationUnitTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0348/ -- --treenode-filter "/*/*/DelegateCheckProbeTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0348/ -- --treenode-filter "/*/*/AgentTaskCheckInterpreterTests/*"
pwsh -File scripts/test-client.ps1
Get-ChildItem . -Recurse -Depth 2 -Directory -Filter bin-0348 | Remove-Item -Recurse -Force
```

Any red in those classes must be re-run at the base commit before it is called pre-existing.

**Live, after deploy** (the shape that failed three times on 2026-09-02/03): dispatch a delegate whose brief ends in a question, wait for `Blocked`, `-Reply`, then watch `-Status` across three 5 s ticks — it must stay `Working` with the first `Result` unchanged and exactly one `Blocked` event; the done report must settle `Succeeded` with the new `Result`; the completion note must carry `… since reply · … since dispatch`; a check fired during the answer turn must carry `after reply (dispatched … ago)`. Confirm the server log has no "re-invoking settlement" line for that task during the answer turn.

## Invariants and non-goals

- **A reply is a new turn, and only a turn asked after the reply can settle a replied task.** The watermark is written in the same `SaveChanges` as the `Replied` event and the status flip; nothing may flip `Blocked → Working` without it.
- **`Blocked` stays closed to `OnTurnEndAsync`.** A Blocked task changes state through a reply, a cancel, or a land/merge outcome, never through a boundary.
- **`RepliedAt` and `RepliedAtSequence` are attempt-scoped.** Cleared on requeue; never inherited across sessions.
- **`AgentSession.LaunchResumedAt` is not touched, read, or reused.** CARD-0340's clock stays the watchdog's.
- **Not in scope:** the two live residue rows on gym-stat (`a571d6c1`, `42bb5ffb`) — owned by their orchestrator, not to be retried or recovered here. `fbd37f59`'s closure by `RecoverFromBindRefusalAsync` on a session that demonstrably had transcript rows is a third shape the investigation did not resolve; it needs its own Debug pass and card, and this plan neither explains nor fixes it. The `ReportNudged*` requeue observation in S1.6 is likewise a separate card.

## Record in docs

- `docs/session-runtime-invariants.md`, new REQUIREMENT bullet beside the settlement entries: a reply to a Blocked task records `AgentTask.RepliedAtSequence`; `ExtractMarkedTurnAsync` refuses any turn whose prompt is at or below it and arm 0 skips a boundary at or below it; the live miss (three tasks, 2026-09-02/03), why the gate is on the prompt, why the overlay reply does not stamp it, and the pinning tests.
- `docs/orchestration-loop.md` §4 (Checking on a delegate): `elapsed` on a check header and the duration on a completion note count from the latest reply; when they do, the header says `after reply (dispatched … ago)` and the note carries both `since reply` and `since dispatch`. An orchestrator judging a stall reads `elapsed` together with `last activity`.
