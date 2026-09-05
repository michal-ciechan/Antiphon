# CARD-0387 — completion-awareness latency

**Date:** 2026-09-05
**Card:** CARD-0387 (`942e9a36-5385-4b6c-9a34-e37b6301f1bc`)
**Status:** investigation complete. No app code was changed.
**Verified against:** this worktree's server, session-runner, and settings. Sibling CARD-0386 confirmed auto-delivery exists; this card asks where (if anywhere) a real interval sits between the event and the orchestrator hearing it.

---

## Verdict, in one sentence

The land drain and the `[task done]` / land-outcome note are **event-driven on the happy path**; the delay an operator feels is **WhenIdle's designed wait for the orchestrator's own idle turn boundary**, not a timer that "notices" completion. Named intervals exist, but they are backstops (5 s land sweep, 5 s deferred-settlement tick, 60 s stranded-queue flush, 300 ms transcript-file poll) — none of them is the primary path from "the work finished" to "the parent session is told."

If the orchestrator is already idle and Running when the note is enqueued, delivery is **inline** (no wait for a later boundary). If it is mid-turn, the note sits until that turn actually ends. That is not a bug.

---

## 1. Land-pipeline processing latency

**Answer: genuinely event-driven. No sleep before git starts.**

A `-Land` request does three things in `AgentTaskLandService.RequestAsync`:

1. Persist `AgentTasks.LandRequestedAt` (the durable queue, CARD-0331 / session-runtime Gotcha #87).
2. `AgentTaskLandQueue.TryEnqueue` into an **unbounded in-process `Channel<LandRequest>`** (`SingleReader = true`).
3. Return. The HTTP/CLI caller does not wait for git.

`AgentTaskLandHostedService.ExecuteAsync` is `await foreach` on `ReadAllAsync`. The class comment is literal: **"this reader never sleeps."** There is no `PeriodicTimer`, `Task.Delay`, or poll in that service. The moment a request is written to the channel, a waiting reader picks it up.

`RunAsync` itself has no delay either: it stamps `LandStartedAt`, then immediately `PrepareLandAsync` (fetch/rebase) → `VerifyAsync` (optional `dotnet build` / `dotnet test`) → `FinalizeLandAsync` (push). Git/build/test duration is real processing time, not an artificial wait before the pipeline starts.

### What *is* on a clock (backstop only)

| Interval | Config | Default | Role |
|---|---|---|---|
| `Delegation:LandSweepSeconds` | `DelegationSettings.LandSweepSeconds`; `server/appsettings.json` `"LandSweepSeconds": 5` | **5 s** (clamp 1–60) | `AgentTaskLandSweepHostedService` re-enqueues a pending `LandRequestedAt` row that **this process does not already hold**. Boot sweep is immediate; then every 5 s. |
| Held retry | same 5 s sweep | **5 s** | If a Shared-writer task is still running in the same repo, `RunAsync` returns `Held` and releases the channel slot. The sweep re-queues it. That is wait-for-the-other-writer, not wait-to-start-this-land. |
| `Delegation:LandMaxAttempts` | same settings; appsettings `"LandMaxAttempts": 3` | **3** | Interrupted git attempts before the sweep refuses. Not a latency knob. |

The sweep is the CARD-0331 recovery path for a server restart / crash mid-land. On a live server that accepted `-Land`, the channel write is the start signal. A second land request while one is already `IsActive` in this process is a **409**, not a queued wait behind a timer.

Serial drain: the hosted service awaits one `RunAsync` before reading the next channel item. If land A is still in `dotnet build`, land B (already in the channel) waits for A to finish. That is queueing behind real work, not a poll.

After the land settles, `DeliverAsync` calls `SessionMessageQueueService.EnqueueAsync(..., MessageSendMode.WhenIdle, ..., origin: Delegation)`. From here the latency question is Q2.

---

## 2. WhenIdle delivery latency

**Answer: idle → immediate; working → wait for a real turn boundary. Not a timer.**

`EnqueueAsync` (default `deliverIfIdle: true`) persists the Pending row, then:

```
if deliverIfIdle
   && session is live in the runner
   && DB status is Running (not Starting)
   && !IsWorkingAsync(transcript)
   → DeliverNextLockedAsync immediately
else
   → leave Pending; the next flush trigger delivers it
```

`IsWorkingAsync` is transcript-derived: activity outranks the last turn-end / interrupt / manual-compact / restart boundary. It is a DB query, not a poll.

### Flush triggers (none of these is "every N seconds, try to deliver")

| Trigger | When | What it does |
|---|---|---|
| Inline in `EnqueueAsync` | Session already idle + live + Running | Types now. |
| `OnTurnEndAsync` | A **real** turn boundary arrives on **this** session (TurnEnd `end_turn` / `cancelled` / `error`, or the interrupt marker) | Deliver next Pending, or publish `SessionFinished` if the queue is empty. |
| `FlushIfIdleAsync` | Manual `/compact` boundary, or herdr leaving `blocked` | Narrow deliver-if-idle only. Not a settlement path. |
| `FlushSessionAsync` (launch) | Boot completes, session becomes Running | Starting-session rows that were held back. |
| `FlushStrandedQueuesAsync` | Session-health tick, **only** idle always-on sessions (or stranded Delegation/Supervision briefs) whose Pending row is older than `StrandedAgeSeconds` | Backstop for a missed turn-end after a kill/resume. **Not** the happy path. |

`SessionHealthHostedService` runs `FlushStrandedQueuesAsync` on `Supervision:RcWatch:ProbeIntervalSeconds` (default **60 s**, floor 10). Combined with `DeliveryVerification:StrandedAgeSeconds` (default **60 s**), a stranded always-on note can wait up to ~60–120 s **only if** the turn-end flush never ran. An orchestrator that is AlwaysOn and sitting idle with a fresh Pending row does **not** wait for this: inline delivery already fired.

### Designed semantics (the operator-visible wait)

`AgentTaskReplyService.DeliverToParentAsync` documents it:

> Deliver the completion note into the parent's session — WhenIdle, so it lands between the parent's turns rather than interrupting one.

The same `WhenIdle` is used for land outcomes (`AgentTaskLandService.DeliverAsync`) and check notes (`AgentTaskCheckService`). There is **no** path that interrupts a mid-turn orchestrator to inject `[task X done]` or a land result. Mode.Now / `EnqueueDeliveringNowAsync` exist for other cases (overlay answers); completion notes do not use them.

So:

- Orchestrator **idle at a prompt** when the note is queued → typed immediately (plus delivery-verification, below).
- Orchestrator **mid-turn** (including a long think, a tool loop, or reading another delegate) → the note waits until **that** session writes a turn boundary. That wait can be minutes. It is not `LandSweepSeconds`, not `PollIntervalSeconds`, and not `StrandedAgeSeconds`.

### Delivery verification (after typing starts — not "awareness")

Once a flush decides to type, `WaitForTranscriptConfirmAsync` polls the stored transcript every `DeliveryVerification:PollIntervalMs` (**500 ms** default) up to `TranscriptConfirmTimeoutSeconds` (**30 s**), re-pressing Enter every `ReEnterIntervalSeconds` (**7 s**) for `SubmitAttempts` (**3**). That is CARD-0055 confirm-the-bytes-landed, not "did the delegate finish." An operator watching the orchestrator would already see the note being typed.

---

## 3. Delegate-task completion detection

**Answer: event-driven off the transcript TurnEnd (file poll 300 ms, then SSE push). Check-interpreter cadence is a different machine and cannot settle a task.**

### Happy path (ordinary delegate)

```
delegate writes JSONL TurnEnd
  → TranscriptTailer / GrokTranscriptTailer / CodexTranscriptTailer reads the file
  → publishes SessionTranscript on the runner event hub
  → SSE `/events` to the server (SessionRunnerEventPump)
  → AgentSessionRuntime.ObserveTranscriptAsync
  → FlushQueueOnIdleAsync → AgentTaskReplyService.OnTurnEndAsync
  → settle the task, then EnqueueAsync(WhenIdle) to the parent (Q2)
```

There is **no FileSystemWatcher** on transcript files. The tailers poll:

| Tailer | Interval | Where |
|---|---|---|
| Claude `TranscriptTailer` | **300 ms** hard-coded (`PollInterval`) | `src/Antiphon.SessionRunner/TranscriptTailer.cs` |
| Grok `GrokTranscriptTailer` | **300 ms** (`DefaultPollInterval`) | same project |
| Codex `CodexTranscriptTailer` | **300 ms** (`DefaultPollInterval`) | same project |
| Locate / exact-id discovery | **250 ms** (`LocatePollInterval`) | until the file appears (boot, not completion) |
| `/clear` fork scan | **10 s** (`DefaultForkScanInterval`) | only to follow a forked conversation file |

No PTY-host "output stopped" signal is used as completion. Screen redraws are explicitly **not** reply evidence (Gotcha #50). The PTY host pushes output frames for the live terminal; settlement keys on **transcript kinds** (`TurnEnd` with a report-boundary stop reason, plus `AssistantText` re-trigger because Claude can write the stop marker before the report text).

The runner → server hop is SSE, not a poll. Keepalives every `Events:KeepAliveSeconds` (**15 s** default) reset `SessionRunner:EventStreamIdleTimeoutSeconds` (**90 s**). That idle timeout is a half-open-TCP reconnect watchdog (`EventReconnectDelayMs` **1000 ms**). It does not delay a TurnEnd that actually arrived: the tailer publishes, the hub pushes, the pump observes.

`AssistantText` arrival re-calls `OnTurnEndAsync` so a stop-marker-first Claude response does not wait for a sweep. Catch-up (`SyncTranscriptAsync` / `CatchUpTranscriptAsync`) re-invokes settlement if the live event was missed (CARD-0288).

### What is on a clock (settlement backstops, not detection)

`AgentTaskDispatcherHostedService` ticks every `Delegation:PollIntervalSeconds` (**5 s** default; not in appsettings, so the C# default applies). `TickAsync` includes `SettleDeferredReportsAsync`. That sweep:

- **Arm 0:** a `[antiphon-report:` marker is already in the transcript but the live observer missed settle (server-down catch-up). Re-hands immediately on this 5 s tick.
- **Arm 1:** TurnEnd arrived, the turn-ending response never wrote text. Waits `FinalMessageGraceSeconds` (**120 s**) then settles on whatever the turn produced. Happy path does **not** wait this out: the text's own arrival re-triggers.
- **Subagent arm:** background `Agent` tool launches. Backstop `SubagentGraceMinutes` (**30**). Closed by identity when notifications return, not by the clock.
- Unchanged-boundary re-hand is rate-limited by `ReportSweepRehandSeconds` (**60 s**) so the 5 s tick does not re-enter settlement forever.

These are recovery for a missed or incomplete live event. They are the closest thing in this stack to "we didn't realise until a timeout," and they only fire when the live observer did not settle.

### Check interpreter is **not** completion detection

`AgentTaskCheckService` is CARD-0047 scheduled look-ins. Its own comment:

> **A check cannot settle anything, structurally.**

Cadence:

| Knob | Default | Role |
|---|---|---|
| `Delegation:PollIntervalSeconds` | **5 s** | Dispatcher tick claims due checks (minute-granularity due times). |
| `Delegation:CheckMinIntervalMinutes` | **5 min** | Floor between checks on one task. |
| `Delegation:CheckInterpreterWaitSeconds` | **60 s** | How long one check waits for the specialist reading before delivering the digest degraded. |
| `Delegation:CompletionNoteGraceSeconds` | **5 s** | If the task settled while the check was interpreting, wait this long for the completion note to appear so the check can suppress itself. 100 ms inner poll. Advisory only. |

A check note is also `WhenIdle` into the **parent**. It is a progress digest, not "the delegate finished."

---

## End-to-end: where time actually goes

### Land finishes → orchestrator hears it

```
-Land RequestAsync
  → Channel write (immediate)
  → LandHostedService RunAsync (git/build/push: real work)
  → EnqueueAsync(WhenIdle) to parent
      → if parent idle: type now (~300 ms tailer + 500 ms confirm poll, usually sub-second)
      → if parent working: wait for parent's next real turn-end
```

No 5 s land-sweep wait on the happy path. The 5 s sweep is only "this process dropped the request."

### Delegate finishes its report → orchestrator hears it

```
JSONL TurnEnd (+ AssistantText if the stop marker came first)
  → ≤300 ms file poll
  → SSE push (event-driven)
  → OnTurnEndAsync settle
  → EnqueueAsync(WhenIdle) to parent
      → same idle/working split as land
```

If the live observer misses the boundary, `SettleDeferredReportsAsync` on the **5 s** dispatcher tick is the backstop (or 120 s if the turn-ending text never arrives).

---

## Named intervals (complete list for this question)

Happy-path latency contributors are marked. The rest are backstops or other machines.

| Name | Default | Config location | On happy path? |
|---|---|---|---|
| Transcript file poll | **300 ms** | hard-coded in Claude/Grok/Codex tailers | Yes — ceiling on "file written → runner event" |
| Locate poll | **250 ms** | hard-coded | Boot/discovery only |
| Fork scan | **10 s** | hard-coded `DefaultForkScanInterval` | Only after `/clear` |
| SSE keepalive | **15 s** | `Events:KeepAliveSeconds` | Keeps the stream alive; not a detect interval |
| Event-stream idle reconnect | **90 s** | `SessionRunner:EventStreamIdleTimeoutSeconds` | Half-open TCP only |
| Event reconnect delay | **1000 ms** | `SessionRunner:EventReconnectDelayMs` | After a dropped stream |
| Land channel drain | none | `AgentTaskLandQueue` unbounded Channel | Event-driven |
| Land sweep | **5 s** | `Delegation:LandSweepSeconds` / appsettings | Backstop / Held retry |
| Dispatcher tick | **5 s** | `Delegation:PollIntervalSeconds` (C# default; not in appsettings) | Settlement **backstop**; check claim |
| Final-message grace | **120 s** | `Delegation:FinalMessageGraceSeconds` | Only if TurnEnd has no text |
| Subagent grace | **30 min** | `Delegation:SubagentGraceMinutes` | Only if background Agent tools launched |
| Report re-hand | **60 s** | `Delegation:ReportSweepRehandSeconds` | Load bound on the 5 s sweep |
| Stranded-queue age | **60 s** | `DeliveryVerification:StrandedAgeSeconds` | Missed turn-end on always-on |
| Health / stranded tick | **60 s** | `Supervision:RcWatch:ProbeIntervalSeconds` | Drives the stranded flush |
| Transcript confirm poll | **500 ms** | `DeliveryVerification:PollIntervalMs` | After typing, not detection |
| Transcript confirm timeout | **30 s** | `DeliveryVerification:TranscriptConfirmTimeoutSeconds` | After typing |
| Re-Enter | **7 s** | `DeliveryVerification:ReEnterIntervalSeconds` | After typing |
| Check-interpreter wait | **60 s** | `Delegation:CheckInterpreterWaitSeconds` | Checks only; cannot settle |
| Check min interval | **5 min** | `Delegation:CheckMinIntervalMinutes` | Checks only |
| Completion-note grace | **5 s** | `Delegation:CompletionNoteGraceSeconds` | Check self-suppression |

---

## Operator implication

The "it feels like we don't realise a turn has finished until some timeout hits" report matches **WhenIdle waiting for the orchestrator's own turn to end**, not a hidden 5/30/60-second detect loop.

- **Not a code bug** if the orchestrator was mid-turn (investigating, writing, waiting on another delegate). The note is already queued; it will type at the next real idle boundary. Procedural: finish or interrupt the current orchestrator turn, or read the task/board/queue directly (`scripts/card.ps1`, task row, `SessionQueuedMessages`) instead of waiting for the session to be told.
- **Would be a bug** only if an **idle, Running** orchestrator still sat for tens of seconds with a Pending Delegation note. That shape is `IsWorkingAsync` stuck true (Gotchas #33/#34/#47 — local commands, compaction, interrupt marker) or a missed turn-end falling through to the 60 s stranded flush. Those are already known, pinned failure classes, not a missing completion detector.
- **Do not** "fix" this by sending completion notes as Mode.Now into a working orchestrator. That types into a live composer (CARD-0055 / overlay-focus history). WhenIdle is the load-bearing contract.

**Recommendation for decide:** no code change for the designed wait. Treat a long-running orchestrator turn as the expected delay. Investigate further only if a concrete idle-orchestrator + Pending-note timestamp pair shows a gap larger than the 300 ms tailer + inline delivery.
