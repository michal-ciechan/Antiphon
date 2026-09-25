# CARD-0714 — Grok report judged on a later system-reminder turn

Confirmed. Task `e9516943` (`e9516943-9bc8-460f-ad90-6f1b1849f8a7`, CARD-0697, Grok, server2) wrote a real done report whose prompt carried `[antiphon-task:e9516943]`. Settlement never took that turn. A Grok `<system-reminder>` background-task completion was stored as the next `UserPrompt`, that prompt has no task marker, and the delivery watchdog then Failed the still-Dispatched task. The sibling `b5e6521b` used the same spill pointer and settled, because it never received that extra user turn.

The lines below were read on this checkout (`813ac551`, the desktop `/api/version` SHA). `git diff origin/master` is empty for the cited files; `origin/master` is `39444f91`.

## Outcome

| | e9516943 (failed) | b5e6521b (succeeded) |
|---|---|---|
| Task | `e9516943-9bc8-460f-ad90-6f1b1849f8a7` | `b5e6521b-a59a-421a-a2b9-5d550607fee9` |
| Session | `df9109a5-08d5-4832-bd71-996a4233090a` | `891a840f-53d7-492d-9bc6-ef00a485c7eb` |
| Dispatched | 2026-09-25T14:58:59.845699Z | 2026-09-25T14:58:36.658061Z |
| Terminal | Failed 2026-09-25T15:40:24.546231Z | Succeeded 2026-09-25T15:36:34.459877Z |
| `result` | null | 2,767-character final message, verdict done |
| User prompts after dispatch | 3 | 2 |
| `<system-reminder>` user chunks in `updates.jsonl` | 1 | 0 |

Failure reason, verbatim:

> Delegate reported but the result could not be attributed: 10 minutes after dispatch the session has ended a turn with a report whose prompt carries no task marker (most likely the brief was mangled in delivery). The work may be real — read session df9109a5-08d5-4832-bd71-996a4233090a before re-running this task.

That sentence is the watchdog template in `AgentTaskDispatcher.cs:2012-2016`. The "10 minutes" is `DeliveryFailTimeoutMinutes` (`DelegationSettings.cs:389`, default 10), cast to int at `:2013`. It is not the elapsed time. Dispatch to Failed is 41 minutes 25 seconds. The watchdog only loads tasks still `Dispatched` whose `DispatchedAt` is older than that timeout (`AgentTaskDispatcher.cs:1814-1818`), then fails only when a `DelegateReportUncorrelated` incident is evidence for this task (`:1997-2004`, `UncorrelatedReportEvidence.cs:27-29`). It had been eligible since 15:08:59Z and did not fail until that incident existed.

## What was delivered

Both briefs spilled. The on-disk briefs still have their newlines and a marker at each end:

- Failed spill: `/work/worktrees/task-e9516943/.antiphon/inbox/5fa8a82a-a7ca-41c9-a471-fa8a2687ea10.md` — 4,781 characters, 58 newlines, `[antiphon-task:e9516943]` twice.
- Succeeded spill: `/work/worktrees/task-b5e6521b/.antiphon/inbox/ae78503c-d23e-4706-8627-3461b0b45587.md` — 4,274 characters, 53 newlines, `[antiphon-task:b5e6521b]` twice.

The typed text is the join-safe pointer from `DelegationReportFormatter.BuildBriefPointer` (`:786-873`) passed through `FlattenForJoiningComposer` (`:893-907`), which replaces newlines with single spaces before typing. Grok then records that already-flat line (`GrokTranscriptNormalizer` documents the composer dropping newlines; the pointer did not depend on Grok to keep them).

Recorded `UserPrompt` for the failed task, transcript sequence 11, 2026-09-25T14:59:29.281Z, 762 characters, 0 newlines. It contains `[antiphon-task:e9516943]` three times (opening header, "YOUR BRIEF IS NOT IN THIS MESSAGE", and the closing token) and the spill path `5fa8a82a-a7ca-41c9-a471-fa8a2687ea10.md`. Sequence 9 of the succeeded session is the same shape for `b5e6521b` (771 characters, 0 newlines, marker three times).

The marker was not lost in the spill pointer, in bracketed paste, or in Grok's newline join.

The first prompt on both sessions is the rules refresh (`[antiphon-grok-rules:…]`, sequence 1). `GrokRulesRefreshService.IsRefreshPromptAsync` returns true only when the text starts with that header (`GrokRulesRefreshService.cs:92-94`). Those turns end at sequence 10 (14:59:27.375Z) and sequence 8 (14:58:59.089Z) and are not reports.

## Which turn was judged

`ExtractMarkedTurnAsync` loads the newest `TurnEnd`, then the last non-housekeeping prompt with a smaller sequence (`AgentTaskReplyService.cs:2497-2516`). The marker gate is a separate, strict match (`:2558`):

```csharp
if (!promptText.Contains(DelegationReportFormatter.TaskMarker(taskId), StringComparison.Ordinal))
    return new TurnOutcome(null, joined.Length > 0);
```

`TaskMarker` is the exact `[antiphon-task:{8 hex}]` (`DelegationReportFormatter.cs:18`). A non-empty assistant join on a prompt that fails this check is `UncorrelatedReport` (`:2559`, recorded at `:143-154`).

Failed session, desktop `GET /api/sessions/df9109a5-08d5-4832-bd71-996a4233090a/transcript?since=0` (276 entries):

| Seq | Timestamp (record) | Kind | What it is |
|---|---|---|---|
| 11 | 14:59:29.281Z | UserPrompt | Flattened pointer. Marker present three times. |
| 271 | 15:39:46.28Z | AssistantText | The report, 2,822 characters. ApiCallId `b3df71fa-7880-4310-a941-4a80990bd8d0:161`. Closes with `--- next stage ---`, `next: review`, and `[antiphon-report:e9516943 done]`. |
| 272 | 15:39:46.465Z | TurnEnd | `stop_reason=end_turn`, same ApiCallId. This is the report boundary. Its prompt is sequence 11. |
| 273 | 15:39:46.493Z | UserPrompt | `<system-reminder>` background-task completion. 297 characters, 4 newlines, no `[antiphon-task:]`. |
| 275 | 15:39:59.789Z | AssistantText | 235 characters: the model saying that completion was already counted. ApiCallId `task-completed-01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7:0`. |
| 276 | 15:40:00.089Z | TurnEnd | `end_turn`, that ApiCallId. Newest boundary. Its prompt is sequence 273. |

Sequence 273, verbatim:

```text
<system-reminder>
Background task "01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7" completed (exit code: 1).
Description: Green-run the three affected integration classes | Duration: 241.6s
Use get_command_or_subagent_output("01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7") to see the full output.
</system-reminder>
```

The report text is still in the transcript at sequence 271. The task row's `result` is null because that turn was never the one settlement read. Once sequence 276 is the newest `TurnEnd`, the gate tests sequence 273, the join of sequence 275 is non-empty, and the outcome is uncorrelated. The task stayed `Dispatched` (events: Created, Warning, Held, Dispatched, Held, Dispatched, Failed — no Working, no Completed) until the watchdog wrote Failed at 15:40:24.546Z. Runner log: `Phone-home ReleaseSlot` for this session at 15:40:24.599Z, outcome `Exited`.

`lastTranscriptAt` on the task detail is `Max(TranscriptEntries.CreatedAt)` (`AgentTaskService.cs:2150-2151`): 15:40:04.516114Z, 18 seconds after the report boundary's own timestamp and 4 seconds after the reminder boundary. Per-row `CreatedAt` is not on `TranscriptEntryDto`, so this max does not say whether sequence 272 was stored at 15:39:46 or in the same write as sequence 276. What it does establish: at 15:40:24 the marked turn had still not settled.

## Why this prompt is a turn

Grok's `updates.jsonl` for the failed session (`/state/grok/sessions/%2Fwork%2Fworktrees%2Ftask-e9516943/df9109a5-08d5-4832-bd71-996a4233090a/updates.jsonl`, 787 lines):

- Line 671, unix 1790349822 = 15:23:42Z: `task_backgrounded` for `01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7` (the class-filter checkpoint).
- Line 682, unix 1790350064 = 15:27:44Z: `task_completed` for that same id (242 seconds later; the reminder's "Duration: 241.6s" matches). The normalizer skips `task_backgrounded` / `task_completed` (`GrokTranscriptNormalizer.cs:56-57`).
- The model had already polled that id (`get_command_or_subagent_output` at lines 676 and 680) and folded the 46/47 result into the report.
- Line 782, unix 1790350786 = 15:39:46Z: `turn_completed` of the report (`prompt_id` `b3df71fa-7880-4310-a941-4a80990bd8d0`).
- Line 783, same unix second: `user_message_chunk` whose text is the system-reminder. `FromUserChunk` stores every non-blank user chunk as `UserPrompt` (`GrokTranscriptNormalizer.cs:164-169`).
- Line 786, unix 1790350800 = 15:40:00Z: `turn_completed` with `prompt_id` `task-completed-01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7`.

The reminder is a new Grok turn, injected as the next file line after the report boundary, twelve minutes after `task_completed` was written. It is not a mangled copy of the brief.

Housekeeping that settlement walks past is Claude's `<task-notification>` prefix only (`SessionRunnerContracts.cs:661-667`, `TranscriptPromptSpan.cs:124-128`). A `<system-reminder>` does not match, so it stays in `TurnPrompts`.

The succeeded session's `updates.jsonl` has 6 `task_backgrounded` and 6 `task_completed` rows and zero system-reminder user chunks. Its newest `TurnEnd` (sequence 180, 15:36:24.502Z) still belongs to the brief prompt (sequence 9), which contains the marker, so the 2,799-character assistant text at sequence 179 settled.

## CARD-0584 does not cover this gate

`4c657379` is `4c6573795bee4afc0d7d00055ea596d588e25c14` (2026-09-25, "attribute quoted channel bodies to their machine delivery attempt"). It changes `ChannelPromptCorrelation.Matches` and adds `MatchesMachineDelivery`. The whitespace-free arm there is `PromptSubmissionMatch.Normalize` plus stripping spaces, and it decides whether a channel quote belongs to a machine delivery attempt. It is not called from `ExtractMarkedTurnAsync`.

`PromptSubmissionMatch.IsConfirmedBy` / `IsCompleteIn` (`PromptSubmissionMatch.cs:163-226`) are the delivery-confirmation matchers that tolerate Grok newline loss. Report attribution does not use them. On this session that would not have mattered: the judged prompt is a different prompt, and the brief prompt still contains the literal marker.

## Timing of the watchdog, and what is not pinned

Phone-home catch-up persists a transcript snapshot and does not settle (`PhoneHomeRecoveryPump.cs:304-305`). The watchdog's own pull is the same persist-only catch-up (`AgentSessionRuntime.CatchUpTranscriptAsync`, `:700-705`, called at `AgentTaskDispatcher.cs:1890`) and then reads incidents that already exist. The caller that records the incident is `OnTurnEndAsync`. Live `TurnEnd` events call it (`AgentSessionRuntime.cs:388-389`). CARD-0288's sweep also calls it when any assistant row already contains this task's `[antiphon-report:]` token (`AgentTaskDispatcher.cs:3355-3424`), and that call still runs `ExtractMarkedTurnAsync` on the newest boundary only.

Desktop Serilog was not retrieved from this runner (`docs/logs.md` places it on the Windows server content root). The ephemeral agent `2ddb3d40-ffea-4744-a011-a3202ffbc1b3` is gone (`GET /api/agents/2ddb3d40-ffea-4744-a011-a3202ffbc1b3/incidents` is 404), so the incident row's `CreatedAt` was not read back. Remaining uncertainty: which of those `OnTurnEndAsync` callers ran first after sequence 276 existed. It does not change the mechanism. In-order live handling of sequence 272, while it was still the newest boundary, would have settled the marked report and left sequence 276 with no open task (`OnTurnEndLockedAsync` returns when status is not Dispatched or Working, `:107-110`). The task was still Dispatched at 15:40:24Z, so that settle did not happen. The reminder line sits immediately after the report `turn_completed` in `updates.jsonl`, and any judgment that sees sequence 276 as newest classifies the task as uncorrelated instead of reading sequence 271.

## Not done, noted

Skip a Grok `<system-reminder>` background-task turn as a settlement boundary and settle the previous marked `TurnEnd` (sequence 272's final message); treating the reminder as housekeeping alone is not enough, because `FinalMessageOf` would then report sequence 275's 235-character ack under the newest ApiCallId. CARD-0584's channel matcher is the wrong seam. A regression should replay this shape: rules prompt, flattened pointer containing `[antiphon-task:e9516943]` three times, assistant report with `[antiphon-report:e9516943 done]` plus its `TurnEnd`, then the sequence 273 reminder, a short assistant reply, and a second `TurnEnd`, and expect Succeeded on the long report with no uncorrelated failure. Attributing every turn on a bound session would also settle a human's unmarked turn, which this transcript does not justify.
