# CARD-0248 — root cause: the nudge is eaten by the next 5-second sweep

**Task d6f02cd5, Debug, 2026-08-31.** Diagnosis only; no fix built.

## Verdict in one line

`IsSessionLiveAsync` returned **true**, both times it was asked. The gate that actually failed was
`task.ReportNudgedAt is null` — the task **had** already been nudged, 5.08 seconds earlier, by the
*previous tick of the same sweep looking at the same unchanged turn boundary*. The card's premise
(a wrong session-liveness check) is disproved by the production row.

## The disproof, straight from the production database

```sql
SELECT "Status","DispatchedAt","CompletedAt","ReportNudgedAt","ReportEvidence","CheckCount"
FROM "AgentTasks" WHERE "Id"::text LIKE '8aaa9bd1%';
```
```
Status         | 4 (Succeeded)
DispatchedAt   | 2026-08-30 13:52:14.109+00
ReportNudgedAt | 2026-08-30 13:54:54.406248+00   <-- NOT NULL
CompletedAt    | 2026-08-30 13:54:59.487210+00   <-- 5.080962 s later
ReportEvidence | 4 (FinalMessageMissing)
CheckCount     | 0
Result         | I'll read the card first, then research hooks, precedent, provider equivalents, ...
```

The task's own event list says the same thing in prose:

```
13:52:09.027 | Created   | Worker/Plan at Frontier (explicit override) in C:\src\Antiphon
13:52:14.110 | Dispatched| Dispatched to agent 'task-8aaa9bd1' (fable)
13:54:54.406 | Warning   | Turn ended without the closing report line — asked once for
             |             `[antiphon-report:8aaa9bd1 done|blocked|failed]`. The task stays Working.
13:54:59.487 | Completed | Delegate reported 150 characters.
13:54:59.487 | Warning   | The response that ended the turn never wrote its own text within 120s...
```

The card inferred `ReportNudgedAt == null` from `checkCount: 0`. Those are unrelated counters —
`NudgeForClosingLineAsync` never touches `CheckCount` (that one belongs to the check-in sweep).
The nudge was visible all along, both as `reportNudgedAt` on the task and as the `Warning` event.

`5.080962 s` is not a coincidence: `DelegationSettings.PollIntervalSeconds = 5`
(`server/Application/Settings/DelegationSettings.cs:14`), the period of
`AgentTaskDispatcherHostedService`'s `PeriodicTimer`.

## The full causal chain, with citations

### 1. `claude-fable-5` emits `stop_reason: "end_turn"` on records that carry `tool_use`

`TranscriptNormalizer.FromAssistant`
(`src/Antiphon.SessionRunner/TranscriptNormalizer.cs:141-147`):

```csharp
// A finished turn: stop_reason present and not "tool_use" (tool_use means the turn continues
// after the tool runs). end_turn / stop_sequence / max_tokens all mean the agent is idle.
if (!string.IsNullOrEmpty(stopReason) && stopReason != "tool_use")
    parts.Add(new TranscriptPart(TranscriptKinds.TurnEnd, ...));
```

The same JSONL record therefore yields **both** a `ToolCall` part and a `TurnEnd` part. Session
`ea371170`'s transcript shows exactly that — one `Uuid`, two rows:

```
 Seq | Kind          | Uuid                                 | Model          | StopReason | Tool
   6 | ToolCall      | 34caa40e-787a-48ab-87d1-38b14e48492e | claude-fable-5 |            | ToolSearch
   7 | TurnEnd       | 34caa40e-787a-48ab-87d1-38b14e48492e | claude-fable-5 | end_turn   |
   9 | ToolCall      | e471d826-e157-4f8b-977c-dd1fd54999d0 | claude-fable-5 |            | Bash
  10 | TurnEnd       | e471d826-e157-4f8b-977c-dd1fd54999d0 | claude-fable-5 | end_turn   |
  ... (seq 11/12, 14/15, 16/17, 20/21 identical shape)
```

Blast radius, measured over the whole DB (TurnEnd rows sharing a `Uuid` with a `ToolCall` row):

```
     Model      | phantom_turnends | sessions
----------------+------------------+----------
 claude-fable-5 |               18 |        3
```

Zero on every other model. This is the *trigger*, and it is model-specific.

### 2. The phantom boundary parks settlement in the CARD-0046 grace

The last phantom `TurnEnd` is seq 21, `ApiCallId msg_011CeZ3VPvBbeuz1bhmFqSYi`, stored
13:52:52.388. `ExtractMarkedTurnAsync` (`AgentTaskReplyService.cs:1389-1394`) takes the highest-
sequence `TurnEnd` as *the* boundary; `ResolveFinalMessageState` finds no `AssistantText` row with
that `ApiCallId` (the record was a tool call, not text) and returns `Pending` →
`TurnOutcome.Deferred`. Correct at that moment.

### 3. The grace expires and the sweep starts firing — every 5 seconds, forever

`AgentTaskDispatcher.SettleDeferredReportsAsync` (`AgentTaskDispatcher.cs:1370-1447`) runs once per
tick. Its predicate is **monotonic**: "the session's latest `TurnEnd` has an `ApiCallId`, was stored
more than `FinalMessageGraceSeconds` (120) ago, and no `AssistantText` carries that `ApiCallId`."
Once true it stays true until the task leaves Dispatched/Working. Nothing records that the sweep has
already handed this boundary to `OnTurnEndAsync`.

- 13:54:52.39 — cutoff crosses seq 21's `CreatedAt`.
- **13:54:54.406, tick N** → `OnTurnEndAsync` → `FinalMessageMissing = true`, report = the only
  `AssistantText` in the prompt span, the 150-char preamble → `ClassifyReportAsync`:
  `ReportNudgedAt is null` ✓ and `sessionLive` ✓ → **nudge**, task stays Working.
- **13:54:59.487, tick N+1** → identical sweep, identical boundary, identical `TurnOutcome`. Now
  `ReportNudgedAt is not null` → settle-anyway branch
  (`AgentTaskReplyService.cs:1808-1818`) → `Succeeded`, `FinalMessageMissing`, `Result` = preamble.

### 4. The nudge could not possibly have been answered — it had not even been typed

`NudgeForClosingLineAsync` enqueues with `MessageSendMode.WhenIdle`. The session was mid-turn:

```
SELECT "CreatedAt","SentAt" FROM "SessionQueuedMessages" WHERE "AgentSessionId"='ea371170-...';
 13:52:14.205 | sent 13:52:25.478   <- the brief
 13:54:54.421 | sent 14:07:39.911   <- the nudge
```

**The nudge was delivered 12 minutes 45 seconds after it was queued, and 12 m 40 s after the task it
belonged to had already been settled Succeeded.** 87 more transcript entries were written by that
session after the settle, up to 14:07:39.

So the settle-anyway branch's contract — "we asked once and it ended another turn without the line"
— is not merely raced here; it is structurally unachievable on any sweep-driven re-entry, because
the sweep re-asks 5 s later and the answer channel is `WhenIdle`.

## What was ruled out, with evidence

| Card hypothesis | Status |
|---|---|
| 1. Race on `AgentSessions.Status`/`EndedAt` in a session's first minutes | **Ruled out.** `sessionLive` was `true` at 13:54:54 (the nudge branch requires it). Nothing wrote a non-live status in the window. |
| 2. `ClassifyReportAsync` reading a stale session snapshot | **Ruled out.** `IsSessionLiveAsync` issues its own `AsNoTracking` query per call; and it never mattered — `ReportNudgedAt is null` fails first in the `&&`. |
| 3. Something transiently set `EndedAt` (CARD-0056/CARD-0126 shared root cause) | **Ruled out.** `SELECT * FROM "AgentIncidents" WHERE "SessionId"='ea371170-...'` returns **0 rows** — no re-adoption, no false-Failed, no reconciliation correction. The session row ended at 14:55:04 with `ExitCode -1`, an hour after the settle. |
| "Louder for Plan-role dispatches" | **No.** Defect A (below) is role-, kind- and model-agnostic. Defect B is `claude-fable-5`-specific. It looked Plan-flavoured only because Plan dispatches route to fable. |

## Reproduction — already in the repo, and green

`tests/Antiphon.Tests/Application/AgentTaskDeliveryWatchdogTests.cs:577-611`,
`a_deferred_settlement_is_swept_after_the_grace_window`, **encodes the bug as the expected
behaviour**: two back-to-back `SettleDeferredReportsAsync` calls with no time between them, asserting
nudge-then-`Succeeded`/`FinalMessageMissing` with `Result` = `"I'll start by reading the spec."` —
a preamble, exactly the production shape. Its seeded session row is live throughout, which is the
controlled-harness proof that `IsSessionLiveAsync` is irrelevant to reaching that branch.

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-<name>/ \
  --treenode-filter "/*/*/AgentTaskDeliveryWatchdogTests/a_deferred_settlement_is_swept_after_the_grace_window"
-> total: 1, succeeded: 1  (14.9 s)
```

`a_task_waiting_on_a_dead_subagent_is_swept_after_the_subagent_grace`
(`:697-722`) has the same two-call shape on the *subagent* arm, whose predicate
("session silent past `SubagentGraceMinutes`") is monotonic in the same way. Both tests must change
with the fix.

Prevalence so far: the CARD-0159 nudge has fired **exactly once** in the entire production database,
and that one time it was consumed by the next tick. Sample size 1, failure rate 1/1.

```
 ReportEvidence      |  n  | settled within 30 s of its own nudge
 Legacy(0)           | 728 | 0
 Marked(1)           |  39 | 0
 FinalMessageMissing | 1   | 1
```

## Proposed fix

Two independent defects. **A is load-bearing** — it fires on any sweep-driven settlement, including
the genuine "1 in 180" case CARD-0046 was built for. B is what pulled the trigger this time.

### Defect A — the nudge has no clock and the sweep has no memory

Needs design; do not drive-by. Three complementary changes, in priority order:

1. **Settle-anyway must require a *different* boundary, not just a flag.** Persist what was nudged
   alongside `ReportNudgedAt` — the `TurnEnd` row's `Sequence` (or its `ApiCallId`) — and take the
   settle-anyway branch only when the current boundary differs. That restores the stated contract:
   "asked once, and it ended *another* turn unmarked." This alone fixes the incident.
2. **Do not settle before the nudge has been delivered.** The nudge rides `WhenIdle` and here took
   12 m 45 s to be typed. Gate the settle-anyway branch on the nudge's
   `SessionQueuedMessages.SentAt` being non-null plus a response window, so a second boundary
   arriving seconds later (another phantom fable `end_turn`, say) still cannot settle it.
3. **Give `SettleDeferredReportsAsync` a watermark.** Its predicates are monotonic, so it re-enters
   settlement every 5 s for the whole life of an affected task. Record the swept boundary
   (e.g. `AgentTask.DeferredSweptSequence`) and skip a session whose latest `TurnEnd` has already
   been handed over. Hardening rather than the fix, but it removes the whole class.

Open design question for the owner: should the nudge instead be delivered `Immediate` (interrupting
the delegate) rather than `WhenIdle`? Answering with a nudge requires the delegate to be idle, which
is precisely the state the sweep has *failed* to establish.

### Defect B — the normalizer trusts a stop_reason over the record's own content

`src/Antiphon.SessionRunner/TranscriptNormalizer.cs:141-147`: a record carrying a `tool_use` block
is not a turn end whatever its `stop_reason` claims — the tool result is by construction still to
come. The guard is ~3 lines, but `TranscriptKinds.TurnEnd` is read by seven consumers
(`SessionMessageQueueService` idle-flush at `:2621`, `ChannelReplyDispatcher`,
`ReviewReplyDispatcher`, `ApiErrorRecoveryService`, `TranscriptTurnWindow`,
`AgentSessionRuntime:344`'s idle boundary, and the delegation sweeps), and every one of them is
currently seeing these phantom boundaries too — `AgentSessionRuntime:344` treating a phantom
`end_turn` as idle is what let the queue believe a mid-turn session was flushable. So it wants its
own slice with normalizer tests, not a one-liner.

Scope note: restrict the guard to `end_turn` if you want to keep `max_tokens` mid-tool-call
behaving as a real (truncated) end.

## Note for the card

Add to CARD-0248: `checkCount` is not evidence about `reportNudgedAt`. The two counters are
unrelated, and the API already returns both plus the nudge `Warning` event.
