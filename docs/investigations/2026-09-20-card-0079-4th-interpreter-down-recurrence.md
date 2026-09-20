# CARD-0079 — fourth interpreter-down recurrence (2026-09-19/20)

Investigated 2026-09-20 (task e78ba0b0, Investigate stage). Evidence: live
`GET /api/model-availability`, `GET /api/agents` (slug `antiphon-check-interpreter`),
`GET :17204/sessions/cea73d57-…/snapshot`, and direct Postgres on `antiphon`
(`AgentTasks`, `AgentTaskEvents`, `AgentIncidents`, `ModelAvailabilityHolds`,
`TranscriptEntries`, `SessionQueuedMessages`). Code citations are this worktree
HEAD `3c7a4057` (same SHA as `GET /api/version` on the live server). All
timestamps UTC.

## Verdict

**Confirmed, and it is not either previously-fixed CARD-0079 cause.** Overnight
2026-09-19 17:00Z–2026-09-20 15:00Z the check interpreter produced **zero**
readings. 93 Role=`Check` rows: 0 Succeeded, 89 Canceled
("The caller that asked for it stopped waiting."), 3 Failed on the 240-minute
Check ceiling, 1 still Dispatched occupying the specialist. Zero
`haiku is held` events. `GET /api/model-availability` returns `holds: []` with
`haiku` in `available`. Last haiku AutoDetected hold ended 2026-09-13.

The standing session `cea73d57-3072-4cc2-8cb0-ab859e7d3415` has been
**Working since 2026-09-14 23:15** because auto-compaction fired mid-check
(seq 1255 UserPrompt `42ff7aae`, seq 1256 `Context compacted (auto)`, seq 1257
continuation UserPrompt, **no TurnEnd after 1254**). `IsWorkingAsync` treats
that auto boundary as housekeeping, not an end, and treats the unanswered
`42ff7aae` prompt as activity after the last TurnEnd — so WhenIdle check
briefs never flush. CARD-0117 D8 then **defers** the 10-minute delivery
watchdog while the brief is still Pending and the session is working, so each
occupant sits Dispatched for the full 240-minute Check ceiling without a
kill. Everyone else is Held as "standing agent busy" and canceled at the 60 s
interpreter wait.

This is not CARD-0079's zombie session-migration (live `AgentSessionId` equals
the specialist snapshot). It is not CARD-0335 haiku-hold exhaustion (no hold,
no `haiku is held` events, CARD-0335 `9f9e9ee1` is an ancestor of the running
SHA). It is not CARD-0501's Enter-only `HeadFragmentIsVisible` loop (that head
seq 535 is Canceled; the 50 remaining Pending briefs all have
`DeliveryAttempts = 0` and were never typed). CARD-0501's **ignition**
(auto-compact mid-check on this same session) did recur at 23:15 on 09-14
after the 22:50 resume; the stuck state since then is the Working/WhenIdle
deadlock plus 4 h occupancy, which CARD-0501's generation-gate / whole-head /
attempt-charging fix does not touch.

## What was ruled out first

### 1. Original CARD-0079 zombie (session migration, task `1d407857`)

Occupying Check rows in the window have `AgentSessionId` =
`SpecialistSessionId` = live session `cea73d57`,
`SpecialistSessionStartedAt` = `2026-09-14 22:50:07.718176Z` (the live
`startedAt`). Settlement is looking at the live session. The occupant is not
a two-day-old Dispatched row whose message moved to another session.

### 2. CARD-0335 AutoDetected haiku hold (third-reopen cause)

`scripts/model-availability.ps1 get` at investigation time:

```json
{ "holds": [], "available": ["fable","opus","sonnet","haiku","grok-4.6", ...] }
```

`ModelAvailabilityHolds` rows with `ModelAlias = haiku`:

| Id prefix | Source | HitAt | DisabledUntil | ClearedAt | Reason |
|---|---|---|---|---|---|
| 9342a783 | AutoDetected | 2026-09-13 00:14 | 2026-09-13 01:52 | 2026-09-13 01:52 | session-limit resets 02:50 Europe/London |
| 1c2eba7a | AutoDetected | 2026-09-06 07:35 | HitAt+6h | 2026-09-06 08:08 | Haiku per-model cap (no reset stated) |
| 4db774bc | AutoDetected | 2026-09-05 15:16 | HitAt+6h | 2026-09-05 21:16 | Haiku per-model cap (no reset stated) |
| d83acc1a | AutoDetected | 2026-09-01 20:54 | (null, pre-0335) | 2026-09-03 04:35 | Haiku per-model cap (no reset stated) |

No haiku row is active. None overlaps 2026-09-19/20. Third-reopen shape was
53/76 undispatched cancellations with Held `"haiku is held; dispatch paused
for that model."` Window count of `AgentTaskEvents.Detail ILIKE '%haiku is
held%'` = **0**. The 93 Created events that mention `haiku` are the specialist
pin line `Execution: ClaudeCode/Low/haiku`, not a hold.

CARD-0335 (`9f9e9ee1`, 2026-09-03) is an ancestor of running `3c7a4057`. The
6-hour fallback is not the overnight outage.

## Overnight down-rate (Role=Check, the interpreter's own tasks)

Window: `CreatedAt >= 2026-09-19 17:00Z` and `< 2026-09-20 15:00Z`.

| Status | n | Dispatched? | FailureReason head |
|---|---:|---|---|
| Canceled (6) | 89 | no | `The caller that asked for it stopped waiting.` |
| Failed (5) | 3 | yes | `Ran 4h00m against the 240-minute ceiling for role Check. Last transcript entry: CompactBoundary, {119,123,133}h ago. … session was NOT killed` |
| Dispatched (1) | 1 | yes | (null; still occupying) |
| Succeeded (4) | **0** | | |

Readings: **0 / 93 = 0%**. Fallback rate: **100%** of settled interpreter
rows. Compare the 2026-09-03 post-0335 re-verify: 24/25 readings (96%).

Event types on those 93 rows: Created 0, Held 18 (occupancy, not haiku),
Dispatched 1, Failed 9, HeldAged 37 (CARD-0535, 2 rows on `ff599b51` while it
queued behind `a38dbd0a`).

Held detail buckets (window):

| n | Detail |
|---:|---|
| 33 | busy with `ff599b51` (Dispatched) |
| 17 | busy with `2ade1336` (Dispatched) — occupant started 15:00, before the 17:00 window |
| 14 | busy with `28f2b526` (Dispatched) |
| 13 | busy with `c8243890` (Dispatched; live occupant) |
| 11 | busy with `a38dbd0a` (Dispatched) |

Text is `DispatchHoldDetails.StandingAgentBusy`
(`server/Application/Services/DispatchHoldDetails.cs:53-55`).

`AgentIncidents` Kind=26 `CheckInterpreterUnavailable` on agent
`be5d4502` (`antiphon-check-interpreter`): **91 in the window**, 510 since
the 09-14 22:50 resume, last at 2026-09-20 14:49:32. Sample message:
`check interpreter 'antiphon-check-interpreter' could not complete a run (no reading within 60s).`

Parent-task `AgentTaskEventType.Check` (13) rows in the window: 118, of which
8 contain the literal `INTERPRETER DOWN`. That under-counts the down-rate.
`BuildNote` puts the marker on the **header** and the
`(unverified digest — interpreter unavailable: no reading within 60s)` line
on the body (`AgentTaskCheckService.cs:707-733`); the Check event stores the
digest head (CARD-0047, truncated ~900 chars), so most events start
`CAPTURED 2026-09-19T…` with no marker. Role=Check outcomes + Kind=26
incidents are the rate.

Hourly Role=Check creates (first census; later two Canceled rows arrived in
the 14:00 hour):

| Hour (UTC) | n | Canceled | Failed | Dispatched |
|---|---:|---:|---:|---:|
| 09-19 17 | 9 | 9 | 0 | 0 |
| 09-19 18 | 8 | 8 | 0 | 0 |
| 09-19 19 | 12 | 10 | 2 | 0 |
| 09-19 23 | 18 | 18 | 0 | 0 |
| 09-20 00 | 11 | 11 | 0 | 0 |
| 09-20 01–02 | 4 | 4 | 0 | 0 |
| 09-20 08 | 8 | 7 | 1 | 0 |
| 09-20 09–12 | 7 | 7 | 0 | 0 |
| 09-20 13 | 4 | 3 | 0 | 1 |
| 09-20 14 | 12 | 11 | 0 | 0 |

Quiet hours (03–07, 03:05–08:29) are the gap after occupant `ff599b51`
failed at 03:05:07 until `28f2b526` dispatched at 08:29:04 — no Check
traffic, not a recovery.

Last **Succeeded** Role=Check anywhere: `6c837773` completed 2026-09-12
16:48:22Z. That is CARD-0501's last healthy reading (investigation
`docs/investigations/2026-09-14-card-0501-check-interpreter-repeatedly-down.md`).

Since the 09-14 22:50 resume, every day's Check census is the same shape: a
handful of 4 h Failed occupants + tens to hundreds of 60 s Canceled waiters,
zero Succeeded (09-15: 3/88, 09-16: 2/7, 09-17: 5/122, 09-18: 6/136, 09-19:
6/177, 09-20 partial: 1 Failed + 1 live Dispatched + 42+ Canceled).

## Mechanism

### A. Session is Working because auto-compact never ended the turn

`TranscriptEntries` for `cea73d57`, last rows (max seq 1257, last
`CreatedAt` 2026-09-14 23:15:20.750Z):

| Seq | Kind | Timestamp | Text head |
|---:|---|---|---|
| 1254 | TurnEnd | 23:14:31 | (empty) |
| 1255 | UserPrompt | 23:14:47 | `[antiphon-task:42ff7aae] role=Check …` (Check #1 on `db04414b`, captured 08:49:34) |
| 1256 | CompactBoundary | 23:15:19 | `Context compacted (auto)` |
| 1257 | UserPrompt | 23:15:19 | `This session is being continued from a previous conversation that ran out of context.` |

No AssistantText / TurnEnd after 1255. Live agent: `status=Running`,
`working=true`, `effectiveModelId=haiku`, `contextFullnessState=Compacted`,
`startedAt=2026-09-14T22:50:07.718176Z`, `lastSeenAt=2026-09-20T14:42:05Z`,
supervision `consecutiveFailures=0`. Runner snapshot
`acceptedStartedAt` matches; rendered screen still shows the `42ff7aae`
brief / report-token instructions (`lastSequence=10443`).

`IsWorkingAsync` (`SessionMessageQueueService.cs:4308-4405`):

- Auto `CompactBoundary` is **not** a turn end (only text containing
  `(manual)` is; `TranscriptKinds` comment at
  `SessionRunnerContracts.cs:460-463`: treating auto as an end would inject
  WhenIdle into a working composer).
- Continuation UserPrompt matching
  `CompactionContinuationPromptPrefix` (`This session is being continued
  from a previous conversation`, `:486-493`) is **excluded from activity**
  (CARD-0041).
- Seq 1255 `[antiphon-task:42ff7aae]` is ordinary UserPrompt activity
  **after** last TurnEnd 1254 → `workingAfterEnd` = true.

So the session has read Working for ~5.6 days. Enqueue path
(`SessionMessageQueueService.cs:474-479`) only flushes WhenIdle when
`!working`. DeliverNext never runs.

### B. Watchdog defers; occupancy lasts 240 minutes; session is not killed

`AgentTaskDispatcher.cs:1487-1497` (CARD-0117 D8): if the brief is still
Pending **and** `IsWorkingAsync` is true, the delivery watchdog logs
`delivery watchdog deferring — the brief is still queued Pending and
session … is working; the clock passes to TaskDeadlinePolicy` and
`continue`s. No "Boot prompt was never delivered" fail, no always-on kill.

TaskDeadlinePolicy then fails the Check at the 240-minute role ceiling. The
three window Failed rows and the 21 prior occupants since 09-14 23:36 all
last **240.0 minutes** and say `The session was NOT killed`. Occupancy
chain (DispatchedAt → CompletedAt), continuous from 21 minutes after the
compact freeze:

`d52f2405` 23:36 → … → `2ade1336` 15:00–19:00 → `a38dbd0a` 19:04–23:04 →
`ff599b51` 23:05–03:05 → (quiet) → `28f2b526` 08:29–12:29 → `c8243890`
13:04–(still Dispatched).

### C. 60 s waiters never leave Queued

`AgentTaskCheckService.InterpretAsync` waits
`CheckInterpreterWaitSeconds` (default 60)
(`AgentTaskCheckService.cs:462-488`). Timeout →
`interpreter unavailable: no reading within 60s` → INTERPRETER DOWN header
+ unverified digest. `SpecialistTaskRunner.CancelIfStillQueuedAsync`
(`:404-414`) stamps Canceled / `The caller that asked for it stopped
waiting.` only while `Status == Queued`. The occupant is already
Dispatched, so it is not canceled; the waiters are.

### D. Queue is a 50-deep untyped backlog; CARD-0501's Enter-only head is gone

`SessionQueuedMessages` for `cea73d57`:

| Status | n | DeliveryAttempts | Sequences | CreatedAt span |
|---|---:|---|---|---|
| Pending (0) | 50 | **all 0** | 585–634 | 2026-09-14 09:04 – 2026-09-20 13:04 |
| Sent (1) | 312 | all >0 | 272–584 | last SentAt 2026-09-14 23:14:46 (`42ff7aae`, seq 584) |
| Canceled (2) | 1 | 1 | **535** | 2026-09-12 17:11:45 — CARD-0501's wedged head |

Seq 584 (`42ff7aae`) SentAt 23:14:46 with DeliveryVerdict Delivered — the
last typed check, then auto-compact. Seq 585 (`ff1a20f0`) is the FIFO head
of the Pending backlog; that **task** Failed at 09-14 09:14:48 via the
10-minute watchdog (session was not yet stuck Working).
`CancelDeadBriefsAsync` (`SessionMessageQueueService.cs:1435-1464`) would
cancel untyped briefs for settled tasks, but it only runs inside
`DeliverNextLockedAsync`, which does not run while Working. So 50 dead
Pending rows remain, including the three overnight Failed occupants and
the live occupant `c8243890` (seq 634).

CARD-0501 land `9b914298` is an ancestor of the running SHA. Its Enter-only
generation gate / `HeadFragmentIsVisibleWhole` / attempt charging is not
the path in play: nothing is being typed (`DeliveryAttempts = 0`).

## Live agent snapshot (investigation time)

- Agent `be5d4502` slug `antiphon-check-interpreter`, AlwaysOn, model
  `haiku` / Low, queueLength 0 (agent-row queue; the **session** queue is
  the 50 Pending briefs above).
- Session `cea73d57` Running, working=true, Compacted, bound.
- No `ModelAvailability` hold; attention filter for hold/interpreter
  items empty (Kind=26 incidents exist but are the 60 s unavailability
  pages, not a model hold).

## Remaining uncertainties

1. Why the auto-compaction continuation (seq 1257) never produced
   AssistantText — provider stall, deny-all PreToolUse, TUI composer
   wedge, or compacted context too large — is not reconstructable from
   stored transcript (no further rows). Not required to confirm the
   Working/WhenIdle deadlock.
2. Whether a **fresh** (non-resume) relaunch would restore readings was
   not tested (Investigate forbids the experiment). Resume at 22:50 on
   09-14 recovered for ~25 minutes, then auto-compacted again.
3. Parent Check-event 8/118 `INTERPRETER DOWN` vs 100% Role=Check failure
   is a storage shape, not a second healthy path.

## Not done, noted

Unblock WhenIdle on unanswered auto-compact (fresh relaunch or treat that
state as idle) and stop 4 h occupancy from hiding a dead interpreter.

--- next stage ---
next: plan
handoff: Fourth CARD-0079 recurrence is not haiku-hold (0 holds, 0 "haiku is held" events, 0/93 Check readings overnight). Standing session cea73d57 has been Working since auto-compact 2026-09-14 23:15 with no TurnEnd, so WhenIdle briefs never flush; CARD-0117 D8 defers the 10-min watchdog and each occupant sits Dispatched 240 min. Plan recovery that clears Working/WhenIdle and stops 4h occupancy.
artifact: docs/investigations/2026-09-20-card-0079-4th-interpreter-down-recurrence.md
