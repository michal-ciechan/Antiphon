# CARD-0353 — the "too long to type" brief is delivered whole; the strand is a hung first model call, and the fix is a boot-turn deadline with an automatic retry and an honest check reading

**Date:** 2026-09-03

**Plan task:** 983bfbdb (Frontier, plan only; no production code changed)

**Card:** CARD-0353 — "A too-long-to-type brief's promised follow-up delivery can silently never arrive, stranding the session forever"

**Evidence base:** `AgentTaskDispatcher.FitBriefForTyping` / `FailNeverStartedAsync` / `TryFailOverdueAsync`, `DelegationReportFormatter.BuildBriefPointer`, `TypedBodySpill`, `PtyDeliveryCeilings`, `DelegationSettings` (`BriefInlineMaxBytes`, `ModernPtyBriefInlineMaxBytes`, `ModelWaitDeadlineMinutes`), `TaskDeadlinePolicy`, `TaskProgressPolicy`, `DelegateCheckProbe`, `server/Bundles/check-interpreter.md`, `AgentTaskService.RetryAsync` / `RequeueAsync` / `FindLaunchFailureRepeatAsync`, `ModelAvailability.UpsertAutoDetectedAsync`, `ApiErrorRecoveryService`; the server log `antiphon-20260903.log` for tasks `9763bfae`, `3318cc48`, `6dc88d90`, `a429ddf2`; the server transcript API for session `f08c827e`; the raw pty log `C:\logs\antiphon\session-runner\f08c827e….ansi.log`; Grok's own records under `~/.grok/sessions/…/{events,updates,chat_history}.jsonl` and `~/.grok/logs/unified.jsonl` (157 sessions since 09-01, 707 inference calls today).

## Decision

**There is no queued follow-up and there never was.** A brief over the inline ceiling is written to `{WorkingDirectory}\.antiphon\task-<short>-brief.md` and the typed message is a *pointer* that tells the delegate to read that file ("so it was written out instead. Read it in full before you do anything else: '<path>'"). The pointer for task `3318cc48` arrived intact — transcript-confirmed `UserPrompt` at 13:47:21Z carrying the full spill path — and the spill file exists with all 2,638 bytes. The card's premise ("the real brief will be delivered as a follow-up queued message … `lastDelivery: null`") is the check interpreter's misreading, adopted verbatim into the card. Nothing in the spill/pointer path is changed by this plan.

**The strand is a hung first model call, on Grok, during an xAI capacity incident.** Grok's own event log for the session ends at `phase_changed: waiting_for_model` with no `first_token`, no retry, no error, for the full 16 minutes until cancel. The unified log shows `shell.turn.inference_start` and then nothing — the HTTP request to xAI was accepted and never answered, and Grok Build 1.0.13 has no first-token timeout. Between 14:25Z and 15:39Z the same log records 289 HTTP 500 responses ("The model is currently at capacity due to high demand", "Service temporarily unavailable. The model did not respond"). The three hung requests today (13:22Z, 13:47Z, 14:03Z) are the leading edge of that incident; there are none outside it.

**Antiphon already has the right clock and used it too slowly and too quietly.** `TaskDeadlinePolicy` classifies "last entry `UserPrompt`, session working" as `ModelWait` and `TryFailOverdueAsync` fails the task at `ModelWaitDeadlineMinutes` = 20 with "not retried … the session was NOT killed". Attention previews it at 16 minutes. The orchestrator cancelled at 16 minutes, one minute before the preview and four before the automatic failure. So "stranding the session forever" is really "twenty minutes, then Failed with no retry, with the check interpreter calling it a delivery failure at minute 10 and minute 15". Three things follow, and they are the three slices:

| Slice | What | Why here |
|---|---|---|
| S1 | A **boot-turn** model-wait deadline: tighter than the general 20 minutes, applied only while the task's post-dispatch transcript is its own prompt(s) and nothing else. | The general deadline is conservative because a mid-task session may hold real work (CARD-0056). A boot turn has produced nothing — no assistant row, no tool call, no file — so there is nothing to protect and no reason to wait 20 minutes. |
| S2 | On a boot-turn breach: fail with a new `ProviderUnresponsive` code, kill the session, and **retry once automatically** at the same kind and tier. Optional hold on the model alias after a repeat. | A bare retry is the measured cure (every stalled task tonight recovered on retry once the provider did). Manual cancel-and-retry is what the orchestrator did by hand three times. |
| S3 | An honest check reading: the digest names the phase deadline and the boot-turn condition, and the interpreter bundle states the pointer contract so it stops reading a pointer as "queued" or "delivery failed". Docs. | The misdiagnosis cost more than the stall: it produced a wrong workaround (shorten goals), a wrong card, and a Plan pass. |
| S4 | FakeGrok hang mode and the tests that drive S1–S3 end to end. | The repo's rule: a ceiling nobody has watched fire is a number in a comment. |

## Ground truth, as verified

**The real inline threshold.** `FitBriefForTyping` measures the *whole typed brief* (goal + reporting contract; the contract alone is ~1 KB) in UTF-8 bytes against `PtyDeliveryCeilings.BriefInlineMaxBytes`, which is:

| Receiver | Ceiling | Effect |
|---|---|---|
| ClaudeCode on the modern ConPTY (production) | 43,200 B | briefs up to ~40 KB type inline |
| ClaudeCode on the inbox conhost fallback | 900 B | effectively every brief spills |
| **Grok, Codex, every non-Claude kind** (`ForAgentKind`, CARD-0084/CARD-0099 default-deny) | **0 B** | **every brief spills, always** |

The card's "threshold between 75 and 1,824 characters" is an artefact: all four CARD-0351 attempts were Grok, and all four — including the 75-character goal that "worked" — took the pointer path (server log: `brief is 2,442 / 2,638 / 1,828 UTF-8 bytes (> 0 — … Grok joins typed lines … so briefs and refinements always spill)`; the fourth session's Grok summary title is the pointer text). Goal length did not discriminate anything. The 75-character attempt worked because it was dispatched at 14:41Z, when the provider had begun answering again (its own session logged eight 500-retries and recovered).

**What each CARD-0351 attempt actually did** (all Grok, all pointer-delivered):

| Task | Session | Brief | Dispatched (Z) | Grok's own record | Outcome |
|---|---|---|---|---|---|
| `9763bfae` | `22f9c248` | 2,442 B | 13:21 | six loops of work, then `inference_start` loop 7 at 13:22:30 — hung | cancelled ("unrelated first-attempt stall" — same cause) |
| `3318cc48` | `f08c827e` | 2,638 B | 13:47 | `inference_start` loop 1 at 13:47:21 — hung; screen timer reached 15m10s on "Waiting for response…" | cancelled 14:03 |
| `6dc88d90` | `25f73f8f` | 1,828 B | 14:03 | `inference_start` loop 1 at 14:03:32 — hung | cancelled |
| `a429ddf2` | `68e156a3` | ~1 KB | 14:41 | eight 500 retries 14:32–14:45, recovered | succeeded |

**Failure rate, measured.** Grok sessions since 09-01: 157; first-turn stalls: 2 (both above, 1.3%). Inference calls today: 707; hung with no completion, retry or error: 3 (0.4%), all inside 13:22–15:40Z; one further `inference_start` at 16:05Z belongs to a session still running when the log was read. The pointer path itself has zero attributable failures in the corpus. First-turn time-to-first-token today (an incident day): p50 2.5 s, p90 32 s, max 94 s over 24 turns; all turns p99 96 s, max 116 s. The corpus figure the 20-minute deadline was set from is "after a real prompt, p99 163 s, max 217 s".

**What the existing watchdogs saw.**

- `FailNeverStartedAsync` (CARD-0340's clock): `started == true` (a real `UserPrompt` since dispatch), brief row `Sent`, no uncorrelated report → `continue`. Correct: the brief *was* delivered.
- `TaskProgressPolicy`: needs `MinRowsInWindow` rows to call a session looping; one row → null. Correct: this is the "nothing landed" case it explicitly hands to the deadline policy.
- `TaskDeadlinePolicy`: `ModelWait`, limit 20 min, preview at 16 min. Would have fired at 20 min: "Mid-turn and waiting on the model for 20m … The task is Failed, not escalated and not retried … The session was NOT killed".
- `ApiErrorRecoveryService` (CARD-0072): keyed on a dead API-error `TurnEnd` row. A hung request never ends its turn, so it is invisible here; and the auto-hold it writes (`UpsertAutoDetectedAsync`) is keyed on a parsed usage-limit wall, which a hang does not paint.
- The check interpreter, twice: "Ambiguous, task booted but brief not delivered; message says 2,634-char brief is queued" and "Looks stuck … only initial prompt received (brief delivery failed)". The bundle contains no description of the pointer contract and the digest carries no phase-deadline fact, so a WORKING session whose only row is a pointer reads as a delivery failure.

**Grok's signals, for the record.** Next to `updates.jsonl` (which the runner tails) Grok writes `events.jsonl` (`turn_started` → `phase_changed: waiting_for_model` → `first_token` → …) and `~/.grok/logs/unified.jsonl` (`shell.turn.inference_start` / `inference_done` with `ttft_ms` / `inference_retry` with `kind` and `reason` / `inference_failed` with `status_code`). Neither is consumed by Antiphon, and this plan does not make either a *verdict* — the transcript stays the verdict (`docs/session-runtime-invariants.md`). They are named in S3's docs as the first place to look when a Grok session sits on "Waiting for response…".

## Relationship to CARD-0340 and CARD-0348

Not a shared cause, and not a shared mechanism. CARD-0340 is "dispatched, and no prompt was ever typed" — its watchdog waved this case through precisely because the prompt was typed. CARD-0348 is a stale status snapshot after a reply. What *is* shared with CARD-0340 is the shape of the fix: a watchdog on "the next expected transcript event never came", with its window measured from the right clock. S1 is that watchdog for the event after the boot prompt — the first assistant, thinking or tool row — and it reuses CARD-0340's `max(DispatchedAt, LaunchResumedAt)` clock unchanged, since a resumed launch's boot turn starts at the resume.

## Alternatives considered and rejected

- **Lower `ModelWaitDeadlineMinutes` globally.** It is measured at ~3x the corpus maximum for *any* prompt, including huge-context turns on warm sessions, and the sweep it drives fails without killing because a mid-task session may hold work. Both properties are right for the general case and wrong for the boot turn. A separate, narrower clock is the honest change.
- **Read Grok's `events.jsonl` in the runner and raise on `waiting_for_model` without `first_token`.** Precise, but provider-specific, and it makes a sidecar file the verdict — the invariant the repo has paid for repeatedly says the transcript is. The transcript already carries the same fact as "last row is the task's own prompt and the session is working". Keep `events.jsonl` as diagnostics (S3 docs), not as a gate. Revisit only if a Claude or Codex boot stall turns out to need a signal the transcript lacks.
- **Press Escape and resubmit the prompt in place.** Grok's Esc does cancel a turn ("Turn cancelled by user", `RunnerGrokAdapter.DonePattern`), and the pointer is idempotent. But a resubmit types into a TUI whose provider connection is in an unknown state, and it adds a fourth delivery path with its own confirm/timeout contract. A fresh session on a bare retry is the path that has been measured to work, four times tonight. Keep it.
- **Auto-hold the model on the first boot stall.** One hung request is not evidence about the provider; today's second stall came 16 minutes after the first and the fourth dispatch at 14:41Z succeeded during the same incident. A hold on the first stall would have queued work that a retry would have finished. Hold on a *repeat* only (S2 step 5), and keep it short.
- **Tell orchestrators to keep goals short.** The card's workaround. It changes nothing (every non-Claude brief spills at any length) and it fights the "orchestrator delegates the reading, hands over full context" rule. S3 says so in the docs.

## S1 — boot-turn model-wait deadline

**Files**

- `server/Application/Settings/DelegationSettings.cs` — `BootModelWaitDeadlineMinutes`
- `server/Application/Services/TaskDeadlinePolicy.cs` — `DeadlineKind.BootModelWait`, boot-turn classification
- `server/Application/Services/AttentionService.cs` — evidence text for the new kind
- Tests: `tests/Antiphon.Tests/Application/TaskDeadlinePolicyTests.cs`, `AttentionServiceTests.cs`

1. **Measure before setting.** Over `TranscriptEntries` joined to `AgentTasks` since 2026-08-20, per task: the gap from the task's brief `UserPrompt` (the first non-housekeeping prompt at or after `DispatchedAt`, the same predicate as `TranscriptPromptSpan`) to the first `Thinking` / `AssistantText` / `ToolCall` row on that session. Report p99 and max by `AgentKind`. Record the numbers in the setting's doc comment the way `ModelWaitDeadlineMinutes` does. Expected from today's Grok data: max ≈ 94 s. **Default = ~3x the measured maximum, floored at 5 minutes;** the placeholder in this plan is **6 minutes**. `<= 0` disables the boot arm and leaves the general 20-minute arm in place.

2. **Classification.** In `TaskDeadlinePolicy.EvaluateAsync`, after `IsWorkingAsync` is true and the last-entry kind is `UserPrompt`, ask one more question: are *all* rows on this session with `At >= clock` (where `clock = max(DispatchedAt, LaunchResumedAt)`, CARD-0340 S2) of kind `UserPrompt` or `QueuedUserPrompt` (housekeeping excluded exactly as `TranscriptPromptSpan` excludes it)? If so the phase is `BootModelWait` with limit `BootModelWaitDeadlineMinutes`, elapsed measured from the *latest* such prompt's `At` (a refinement typed into a still-silent session restarts the wait, which is right: it is a new request). A warm-pool session's inherited rows predate `clock` and do not count — nothing is failed for a stall it inherited. Any assistant, thinking, tool or turn-end row after `clock` means "not a boot turn"; the existing `ModelWait` classification applies unchanged.

3. **Preview.** `WorthSurfacing` at 80% as today; `AttentionService` renders the new kind with evidence text saying the session has produced nothing since its prompt and that crossing the deadline retries automatically (S2), so the human's options are "wait, cancel, or retry now".

4. **Tests** (`TaskDeadlinePolicyTests`): boot turn classified at the tighter limit; a `Thinking` row after the prompt drops back to `ModelWait`; inherited rows before `LaunchResumedAt` are ignored; a refinement prompt restarts the boot clock; `BootModelWaitDeadlineMinutes <= 0` falls back to `ModelWait`.

## S2 — fail loudly, kill, retry once

**Files**

- `server/Domain/Enums/AgentTaskEnums.cs` — `AgentTaskFailureCode.ProviderUnresponsive`
- `server/Domain/Enums/AgentIncidentKind.cs` — `ProviderUnresponsive` (session incident; the failure reason is the task's record, the incident is the agent's)
- `server/Application/Services/AgentTaskDispatcher.cs` — `TryFailOverdueAsync` boot arm
- `server/Application/Services/AgentTaskService.cs` — `RetryAsync` reuse; `FindLaunchFailureRepeatAsync` **must not** learn the new code
- `server/Application/Services/ModelAvailability.cs` (call only), `server/Application/Settings/DelegationSettings.cs` — `BootStallRepeatHoldMinutes`
- Tests: `tests/Antiphon.Tests/Application/AgentTaskOverdueDeadlineTests.cs` (new arm), `ModelAvailabilityDispatcherTests.cs`

1. **Guard, then act.** In `TryFailOverdueAsync`, when the (post-pull, gate 2) verdict is `BootModelWait` and `Breached`: re-check the boot predicate on the freshly pulled transcript and, if the `WorkspaceProgressArm` is available, require no file change or commit since dispatch. Any evidence of work → fall through to today's non-killing `ModelWait` failure. This is the CARD-0056 line: kill only what provably did nothing.

2. **Fail with the code.** `FailAndNotifyAsync(task, reason, "boot-turn provider stall", ct, AgentTaskFailureCode.ProviderUnresponsive)` with a reason that says what was observed and what was done: "Provider never answered the boot prompt: the brief was delivered at HH:MM:SSZ (pointer to <path>, or inline) and the session produced no response in N minutes. The session was killed (it had produced nothing) and the task is being retried once at the same tier." Raise `AgentIncidentKind.ProviderUnresponsive` on the session with the same text so the agent's incident list explains the kill. The completion note to the parent session goes out as today (`BuildCompletionNote`), so the orchestrator sees the failure *and* the retry in one line.

3. **Kill.** The session is killed through the existing stopper (`IDelegateSessionStopper`), not left alive as the general deadline does — leaving a Grok process on a hung request costs a pool slot and re-adopts nothing. `RequeueAsync` already calls `StopDelegateAsync`; make sure the kill lands before the retry's dispatch claims a pool slot.

4. **Retry once.** Call `_tasks.RetryAsync(task.Id, ct)` when `task.Attempt < task.MaxAttempts` (default 2, so exactly one automatic attempt; a human retry still outranks the cap as today). On the second boot stall of the same task, fail with the same code and **no** retry — the reason says so and names the model alias. `FindLaunchFailureRepeatAsync` lists the codes that block a same-goal re-run; `ProviderUnresponsive` is deliberately **not** added, or the automatic retry would block itself. `RerouteAsync` (CARD-0090) stays available to a human who wants a different kind.

5. **Hold on repeat.** When a boot stall fires for the same `(AgentKind, model alias)` twice within `BootStallRepeatHoldMinutes` (placeholder 30) — across tasks, read from `AgentTasks` where `FailureCode == ProviderUnresponsive` — write `ModelAvailability.UpsertAutoDetectedAsync(kind, alias, now + BootStallRepeatHoldMinutes, "provider unresponsive: N boot turns hung in the last M minutes", rawText: the failure reason, sourceSessionId, sourceTaskId)`. CARD-0022's queue-until-clear then holds new dispatches for that alias; Manual holds outrank as today (CARD-0309). Alias resolution is the task's `ModelLevel` through `ModelLevelAliases.For(kind, level)` — never a stub. `0` disables the hold.

6. **Tests** (`AgentTaskOverdueDeadlineTests`, same hermetic-limit discipline as the file's header describes): boot breach → `Failed`/`ProviderUnresponsive`, session stopped, `Attempt` 2, status `Queued`; a boot breach on a session with one `Thinking` row → today's non-killing failure; a second boot breach → `Failed`, not requeued, reason names the alias; two boot stalls on different tasks within the window → one `AutoDetected` hold for the alias; a Manual hold is not shortened.

## S3 — an honest check reading, and the docs

**Files**

- `server/Application/Services/DelegateCheckProbe.cs` — `CheckFacts.Deadline`, `CheckSessionFacts.BootTurn`, `RenderDigest`
- `server/Bundles/check-interpreter.md` (contract v5 — the bundle version is the behaviour change, per its own header)
- `docs/orchestration-loop.md` §3 and §4, `docs/agent-kinds.md` (Grok), `docs/session-runtime-invariants.md`
- Tests: `tests/Antiphon.Tests/Application/DelegateCheckProbeTests.cs`

1. **Digest facts.** `DelegateCheckProbe` evaluates `TaskDeadlinePolicy` (the same call `AttentionService` makes; it is read-only) and renders a `DEADLINE:` line with the verdict's `Summary` when one is worth surfacing, or `DEADLINE: none near` otherwise. When the boot predicate from S1 holds, the `SESSION` line gains `BOOT TURN — prompt confirmed HH:MM:SSZ, no model response since`. This is the fact the interpreter lacked.

2. **Pointer contract in the bundle.** One paragraph under the readings: a transcript prompt beginning `YOUR BRIEF IS NOT IN THIS MESSAGE` (or `YOUR MESSAGE IS NOT IN THIS MESSAGE`) is *complete* delivery — the brief was written to the named file and the delegate reads it; nothing further is queued for it, and "brief not delivered" is never the right reading of it. A WORKING session whose only row since dispatch is that prompt is waiting on its model; with the `BOOT TURN` fact present, the reading is `Needs attention — provider has not answered the boot prompt for N minutes` (or, past 80%, that the harness will retry it). Keep the bundle's one-line output rule; this adds knowledge, not output.

3. **Docs.**
   - `docs/orchestration-loop.md` §3: goal length never affects delivery. Claude delegates type inline up to ~40 KB; every non-Claude delegate receives a pointer and reads the brief from `.antiphon/task-<short>-brief.md` regardless of length. Shortening a goal to "avoid the spill" avoids nothing and drops context the delegate needed. §4: a session showing only the pointer prompt and WORKING is a provider stall; the harness retries it (S2) after `BootModelWaitDeadlineMinutes`; cancel-and-retry by hand only if you cannot wait that long.
   - `docs/agent-kinds.md` (Grok): `events.jsonl` and `unified.jsonl` as the diagnostic sources, with the field names above; Grok Build 1.0.13 has no first-token timeout and retries HTTP 500 up to 15 times per request but never abandons a request that simply does not answer; the 2026-09-03 13:22–15:40Z incident as the measured example.
   - `docs/session-runtime-invariants.md`: one bullet — a delivered boot prompt with no assistant row is a provider stall and is the boot-turn deadline's business, never a delivery re-attempt.

4. **Tests** (`DelegateCheckProbeTests`): digest carries `DEADLINE:` and `BOOT TURN` when seeded; neither on a session with an assistant row; the pointer-only transcript tail renders the pointer prompt unchanged (the interpreter reads it, the bundle now explains it).

## S4 — FakeGrok hang mode and the end-to-end pass

**Files**

- `src/Antiphon.FakeGrok/Program.cs` — `ANTIPHON_FAKE_HANG_AFTER_PROMPT=1`
- `tests/Antiphon.Tests/Application/GrokDelegateEndToEndTests.cs` (new test in the existing harness) or a sibling `GrokBootStallEndToEndTests.cs`

1. **Hang mode.** On the first submit, write the `user_message_chunk` to `updates.jsonl` as today, paint `- Waiting for response… 0.0s` and the spinner, and then write nothing else and end no turn — matching the measured screen and file state of `f08c827e`. Honour `ANTIPHON_FAKE_HANG_AFTER_PROMPT_TURNS=N` so a retry on a fresh process (which does not inherit the env of the killed one only if the test clears it; document that) answers normally.

2. **The pass.** In the `GrokDelegateEndToEndTests` shape (real `delegate.ps1` over HTTP, real dispatcher, real launch onto a real ConPTY, real tailer): dispatch a Grok task; assert the brief spilled and the pointer landed (transcript `UserPrompt` contains the spill path); advance the test clock past `BootModelWaitDeadlineMinutes` (armed at a test value); run the overdue sweep; assert `ProviderUnresponsive`, the session killed, `Attempt == 2`, `Queued`; tick again with hang mode cleared; assert the retry's session answers and the task settles with the report. Then the digest test: probe the stalled task before the sweep and assert the `BOOT TURN` and `DEADLINE` lines.

3. **Unit coverage of the ceiling itself stays where it is** (`DelegationBriefCeilingPtyTests`, `PtyDeliveryCeilingsTests`, `GrokDeliveryShapeTests`). Nothing in the spill gate changes, so nothing there is touched.

## Order, sizing, and what to run

S1 → S2 → S3 → S4 is the dependency order; S3's doc edits can land with S1. S1 and S2 are one Code dispatch (they share `TaskDeadlinePolicy` and the overdue sweep, and S2's tests need S1's classification). S3 is a second, small dispatch. S4 is a third, Grok- or Codex-sized, and is the one that should be watched on a real pty. Verification: `dotnet run --project tests/Antiphon.Tests` chunked by namespace per `docs/testing-and-build.md`, then `TaskDeadlinePolicyTests`, `AgentTaskOverdueDeadlineTests`, `DelegateCheckProbeTests`, `GrokDelegateEndToEndTests` targeted. Pre-existing red per the CARD-0336 triage is to be verified by stashing, not re-fixed here.

## What this plan does not do

- It does not change `FitBriefForTyping`, `BuildBriefPointer`, `TypedBodySpill` or any ceiling. They worked.
- It does not make Grok's sidecar files a verdict. The transcript remains the verdict; the sidecars are named for humans.
- It does not add a detector for the explicit HTTP 500 burst. Grok's own client retried those to completion in every session that received them today; the hang is the shape that needs Antiphon.
- It does not retry a boot stall more than once, or hold a model on a single stall. Both are deliberate; the numbers above are why.
