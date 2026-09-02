# CARD-0057 — Nothing happens on a clock: a schedule row, one sweep, two actions (prompt, then card)

**Date:** 2026-09-02 (Plan pass, task 371a3574 — design only; no production code changed)
**Card:** CARD-0057 "Nothing happens on a clock - schedule a prompt to an agent, then schedule a
card into its workflow" (In Progress, priority 1)
**Supersedes:** two lines of the card's own premise — "nothing anywhere fires on a wall clock" (one
thing does, see Ground truth §G1) and "CARD-0040 ... worth reading together before either is built"
(CARD-0040 is Done; its executor is what this card calls, see Decision D6). Nothing else.

**Sources (verified this pass):** CARD-0057, CARD-0040 (Done 2026-08-27, `2a48af7`), CARD-0047
(Done, `4c22529` / `ede9b75` / `d187c62` / `99089ba` / 4A-4D), CARD-0055 (Done, `0134964` /
`8410d9a`), CARD-0051 / CARD-0087 (the spawn opt-in and the hold),
`server/Application/Services/{AgentTaskDispatcher,AgentTaskCheckQueue,AgentTaskCheckService,
CheckSchedule,SessionMessageQueueService,CardService,CardWorkTransitionService,CardLifecycleTransitions,
OrchestratorService,AgentService,AgentControlService,AgentSupervisorService,AgentTaskService,
AwayDigestNotifier,AttentionService,CardWorkflowRunFactory}.cs`,
`server/Infrastructure/Orchestration/{AgentTaskDispatcherHostedService,AgentTaskCheckHostedService,
CardWorkTransitionHostedService}.cs`, `server/Application/Settings/{DelegationSettings,
CardWorkTransitionSettings,DigestSettings,OrchestratorSettings}.cs`, `server/Domain/Entities/{Agent,
AgentTask,Card,RetrySchedule,SessionQueuedMessage}.cs`, `server/Domain/Enums/{QueuedMessageOrigin,
QueuedMessageStatus,SessionStatus,AgentStatus}.cs`, `server/Application/Dtos/{BoardDtos,
SessionQueueDtos,AgentTaskDtos,AttentionDtos}.cs`, `server/Migrations/20260816142301_AddAgentTaskCheckSchedule.cs`,
`server/Program.cs` (hosted-service and settings registration), `scripts/{card,delegate}.ps1`,
`tests/Antiphon.Tests/Application/{AgentTaskCheckSweepTests,CardWorkTransitionServiceTests}.cs`,
`tests/Antiphon.E2E/CardCliE2ETests.cs`, `client/src/features/agents/{AgentCliModal,
SessionMessageQueue}.tsx`, `client/src/api/{sessions,attention}.ts`,
`client/src/features/attention/attentionVisuals.ts`, `docs/{orchestration-loop,agent-card-lifecycle,
antiphon-api,ops-http,testing-and-build}.md`, and the live server at 17202
(`/api/orchestrator/state`, `/api/agents`, `/api/boards`) on 2026-09-02.

---

## Verdict up front

**Build it as the card says: one `Schedule` row, one 5 s claim-and-hand-off sweep shaped exactly
like CARD-0047's check sweep, and two action kinds behind it — a prompt to a standing agent's
session (phase 1) and a card move with an explicit, acknowledged spend mode (phase 2).** Everything
the fire path needs already exists and is proven: `SessionMessageQueueService.EnqueueAsync` with
`WhenIdle` handles busy, starting and idle agents; `AgentControlService.StartAsync` already carries
pending queue rows across a relaunch, which answers "dead agent" for always-on agents without a
line of new delivery code; and CARD-0040 shipped `CardService.ApplyAutomatedMoveAsync`, the
no-spawn transition executor both triggers share.

**The five open questions, answered (details in Decision):**

| Question | Answer |
|---|---|
| Where the schedule lives | One global `Schedules` table; each row names its target (agent or card) by FK. Listed and filtered per agent / card / board. Not a column on `Agent`, not per board. |
| Repeat expression | Three typed shapes, no cron: `Once` (an instant), `Interval` (every N minutes, drift-free from an anchor), `Daily` (HH:mm on a day-of-week mask in a named zone). Covers all three examples the card gives. |
| Target dead / busy / mid-turn | Always `WhenIdle`, never `Now`. Busy and starting are the queue's problem already. Dead: `Queue` (enqueue onto the agent's persistent session; the relaunch carry-over delivers it) for always-on agents, `Skip` with an attention row otherwise; per-schedule override. |
| Catch-up after downtime | Re-arm computes the next occurrence from *now*, so at most one late fire per restart is structurally possible. A per-schedule `MissedGraceMinutes` decides whether that one fires: `Once` = unlimited (fires late, stamped late), recurring = 60 (skips, records the skip). |
| UI visibility / cancel | Yes, minimal: a Schedules tab beside the Messages tab in the agent modal; the enqueued row itself is already visible and cancellable in the queue panel and gets an origin badge; misfires are attention rows. Card schedules show read-only on the card. |

**The spend trap is designed in, not around.** A card action carries a `Start` mode — `None`
(bookkeeping, structurally cannot spawn), `Release` (lift the auto-dispatch hold so the orchestrator
starts it under its caps), `Spawn` (start a session at fire time). `Release` and `Spawn` refuse to
be created without `acceptSpend: true`, the preview endpoint says in words what will happen and
what it will cost, and the CLI prints that preview and refuses without `-AcceptSpend`.

**Six slices, sequential, Shared workspace, ~16–24 h.** Phase 1 (S1–S3, ~9–13 h) is shippable and
useful on its own.

---

## Decision

### D1. One row, two kinds, one sweep

`Schedule` (table `Schedules`) with `Kind ∈ {Prompt, Card}`. Both kinds share: `Name`, the
recurrence (D2), `TimeZoneId`, `NextFireAt` (UTC), `Enabled`, `MissedGraceMinutes`, `FireCount`,
`LastFiredAt`, `LastOutcome`, `LastOutcomeDetail`, `CreatedBy`, `CreatedAt`, `UpdatedAt`,
`ConcurrencyToken`. Prompt adds `AgentId` (FK, cascade), `PromptText`, `WhenTargetDown ∈ {Queue,
Skip}`. Card adds `CardId` (FK, cascade), `TargetStatus ∈ {Backlog, InProgress, Review}`,
`Start ∈ {None, Release, Spawn}`, `SpendAcceptedAt`, `SpendAcceptedBy`.

A second table `ScheduleFires` (`ScheduleId`, `FireNumber`, `DueAt`, `ClaimedAt`, `CompletedAt`,
`Outcome`, `Detail`, `QueuedMessageId?`, `SpawnedSessionId?`, `Manual`) is the fire history. It is
what makes "did 09:00 fire yesterday, and what happened?" answerable from rows — the same reason
CARD-0040 chose durable rows over hooks. Kept to the last `Schedules:FireHistoryKeep` (50) per
schedule, pruned by the sweep.

Why one global table and not a column on `Agent` (the CARD-0047 shape): a check is 1:1 with its
task and dies with it, so `NextCheckAt` belongs on `AgentTask`. A schedule is N:1 with its target,
outlives any one session, and has its own lifecycle (enable, edit, history). CARD-0047's *pattern*
— a due column, one conditional `UPDATE` as the claim, re-arm before run, an unbounded channel to
a worker — is reused verbatim; its *location* is not.

### D2. Recurrence: three typed shapes, one pure function, no cron

```
Repeat = Once     → FireAt (UTC, given as local wall time + zone at create)
Repeat = Interval → EveryMinutes (1..10080), AnchorAt (UTC); next = anchor + k·every, first > now
Repeat = Daily    → AtLocal "HH:mm", DaysOfWeek bitmask (Mon..Sun, default all), TimeZoneId
```

`ScheduleRecurrence.NextAfter(schedule, nowUtc) → DateTime?` is the whole calendar — a static,
side-effect-free function like `CheckSchedule.NextInterval`, table-tested. Daily walks forward up
to eight local days, builds the local wall time, converts with `TimeZoneInfo`: an invalid time
(spring-forward gap) rolls to the first valid minute after it; an ambiguous time (fall-back) takes
the first occurrence. `Interval` is anchored so it never drifts under a slow tick (the AwayDigest
"last sent + N" shape drifts; this does not).

Zone: `TimeZoneId` is validated at create with `TimeZoneInfo.FindSystemTimeZoneById` (IANA or
Windows ids both resolve on .NET 9 with ICU). Default when omitted: `Schedules:DefaultTimeZone`,
else `Digest:TimeZone` (already "Europe/London", `DigestSettings.cs:10`), else `TimeZoneInfo.Local`.
Every timestamp on the wire is UTC; the DTO also carries the local rendering so a human reads
"09:00 Europe/London" and never has to convert.

Why not cron: it adds a parser (a dependency or hand-rolled), a second notation the operator has
to learn, and the failure mode of a silently-wrong expression that fires at 3 am. The card's own
three examples ("09:00 daily", "every Monday", "Thursday") are all `Daily`; "every 30 min while I
am away" is `Interval`; "Thursday at 09:00, once" is `Once`. If a real need for cron appears, the
`Repeat` enum gets a fourth value and `NextAfter` gets a fourth arm; nothing else changes.

### D3. Fire = enqueue `WhenIdle`; the queue decides busy, the carry-over decides dead

At fire, the prompt kind resolves `Agent.PersistentSessionId` (`Agent.cs:95`) and calls
`EnqueueAsync(sessionId, body, MessageSendMode.WhenIdle, origin: QueuedMessageOrigin.Scheduled,
noteHeader: "Scheduled · <name>")`. Everything after that is existing, tested behaviour:

- **Idle** → the WhenIdle branch delivers immediately (`SessionMessageQueueService.cs` ~:317–323)
  with CARD-0055's transcript confirmation. Outcome `Delivered`.
- **Busy (mid-turn)** → the row waits; `OnTurnEndAsync` (`:743`) flushes it. Outcome `Enqueued`.
- **Starting** → the row stays Pending by design (the 2026-08-09 kill), flushed on boot. `Enqueued`.
- **No live session, agent `AlwaysOn`** → enqueue anyway onto the persistent session row.
  `AgentControlService.StartAsync` moves every Pending row from the previous session id to the
  new one (`AgentControlService.cs:333–345`), and the supervisor (`AgentSupervisorService.cs:82`)
  is what restarts an always-on agent. Outcome `QueuedForRelaunch`. This is the `Queue` policy and
  the default for always-on agents.
- **No live session, not always-on** → nobody will start it, and a 09:00 triage prompt delivered
  next Tuesday when a human happens to start the agent is worse than a skip. Outcome
  `SkippedNoSession` plus an attention row. This is the `Skip` policy and the default otherwise.
  `WhenTargetDown` overrides either way per schedule.
- **Never launched (`PersistentSessionId` null)** → `SkippedNoSession` regardless of policy;
  `EnqueueAsync` would throw `NotFound` on the session anyway (`:3042`).

**One outstanding copy per recurring schedule.** Before enqueueing, the worker cancels any still-
Pending row with `SourceScheduleId == this` (new nullable column on `SessionQueuedMessage`, the
`SourceTaskId` shape). A daily prompt to an agent that was down for three days delivers once on
boot, not three times. The superseded row's id goes in the fire's `Detail`.

**The body is unmistakably a schedule and carries no task marker** — the same rule the check note
keeps (`AgentTaskCheckSweepTests.the_note_is_unmistakably_a_check_and_carries_no_task_marker`):

```
[scheduled: Morning triage · daily 09:00 Europe/London · fire #12 · due 09:00, fired 09:00:04]
<prompt text>
```

Per-kind body rules (`TryGetForbiddenReason`) are applied at *create* against the target agent's
kind, so a body Grok refuses is a 422 today, not a silent `Refused` at 09:00. Multi-line bodies
spill through `TypedBodySpill` exactly as any queued message does.

`Now` mode is never used: the whole point of the card is that a clock must not interrupt a turn.

### D4. Sweep: claim, re-arm, hand off — in its own hosted service

`ScheduleSweepHostedService` (`PeriodicTimer`, `Schedules:SweepSeconds` = 5) calls
`ScheduleService.ClaimDueAsync`:

```sql
SELECT … FROM Schedules WHERE Enabled AND NextFireAt IS NOT NULL AND NextFireAt <= @now ORDER BY NextFireAt
-- per row, one conditional update = the claim (ClaimCheckAsync, AgentTaskDispatcher.cs:1973):
UPDATE Schedules SET NextFireAt = @next, FireCount = @seenCount + 1, LastFiredAt = @now
  WHERE Id = @id AND NextFireAt = @seenNext AND FireCount = @seenCount
INSERT ScheduleFires (…, Outcome = Claimed)       -- same transaction
```

`@next = NextAfter(schedule, now)` — computed from *now*, not from `@seenNext`, which is what
collapses N missed occurrences into one (D5). A `Once` row re-arms to null. Rows>0 → `ScheduleFireQueue.TryEnqueue(new FireClaim(id, dueAt: seenNext, fireNumber))`;
zero rows means another tick or instance won. `ScheduleFireHostedService` drains the channel on its
own thread and runs `ScheduleService.FireAsync(claim)`, which updates the `ScheduleFires` row with
the outcome. A fire that throws is logged, its row marked `Failed`, and never retried — the
schedule has already moved on, exactly the `AgentTaskCheckHostedService` contract.

The fire row written *inside the claim* is a small improvement over CARD-0047: a crash between
claim and run leaves a row stuck at `Claimed`, which the attention feed can show, instead of a
silent gap.

Why a separate hosted service and not sweep #12 in `AgentTaskDispatcher.TickAsync`: the tick is
gated on `Delegation:Enabled` and lives in a 3,600-line class with twenty-odd constructor
dependencies whose test harness needs the whole `DelegationTestServices` git graph. A schedule is
not delegation; its sweep is forty lines with three dependencies, and its tests need a database, a
clock and a fake queue. The card asks for the *pattern* to be reused, not the host. Both timers run
at 5 s, both hand off through an unbounded `Channel<T>`, both re-arm before run.

Disabling keeps `NextFireAt` but the query filters `Enabled`; re-enabling and every edit recompute
`NextFireAt` from now, so a daily schedule paused for a week does not fire the moment it is
resumed as "late".

### D5. Missed occurrences: at most one, and a knob for whether that one fires

Because the claim re-arms from now, downtime can produce at most one overdue claim per schedule.
`MissedGraceMinutes` says whether that claim fires: the worker compares `now − dueAt` and either
fires or writes a `SkippedLate` fire row (visible, not silent). Defaults: `Once` → null (always
fires, stamped "fired 2h14m late" in the header); `Daily` → 60; `Interval` → min(every, 60). The
card's own split — skip for "every morning", fire for "run once at T" — falls out of the defaults;
an operator who wants a Monday sweep to fire whenever the server comes back sets the grace to null.

`POST /api/schedules/{id}/fire-now` bypasses the grace and does not advance the recurrence; it
goes through the same worker and the same spend acknowledgement stored on the row.

### D6. Card actions share CARD-0040's executor; spend is a mode, acknowledged, previewed

CARD-0040 is **Done** (`2a48af7`, 2026-08-27). Its executor is `CardService.ApplyAutomatedMoveAsync`
(`CardService.cs:611`): calls `ApplyColumnMove` directly so it is structurally incapable of
spawning, refuses archived / owned / NeedsDecision / Done / Canceled cards, sets
`AutoDispatchHeldAt` on every active landing so the orchestrator tick cannot start a session on
top of it, and writes the `Move` revision with the automation's actor. The time trigger's three
`Start` modes are:

| `Start` | What fires | Spend | Actor on the revision |
|---|---|---|---|
| `None` | `ApplyAutomatedMoveAsync(cardId, TargetStatus, reason, movedBy: "scheduler")` | none, ever | `scheduler` |
| `Release` | the same move, then `ReleaseAutoDispatchHoldAsync` (new, ~15 lines beside the hold logic): `AutoDispatchHeldAt = null` under the same guards | the orchestrator starts a card session on its next 30 s tick **under the board and column caps** (`OrchestratorService.cs:~105–115`), with its assigned agent or the default definition (`:610`) | `scheduler` |
| `Spawn` | `SpawnAsync(cardId, new SpawnCardRequest())` (`:727`) — moves Backlog→active itself, clears the hold, launches now | immediate session, **bypassing caps** exactly as `card.ps1 move -Spawn` does today (`TryClaimCardAsync` `:338` checks no cap) | `system` (SpawnAsync's own) |

Neither trigger grows its own copy: CARD-0040's sweep and this one both end in the same two
methods, and `Release` is fifteen lines *in* `CardService` next to the hold it lifts, not a third
transition path. The "last word" rule (`CardWorkTransitionService.cs` header) stays coherent: a
scheduler move is a newer `Move` row, so the evidence sweep will not undo it, and a later dispatch
against the card is newer still and moves it again — correct in both directions.

**`Release` is the recommended spend mode.** It is what "start this card's workflow on Thursday"
means on this deployment: the orchestrator is enabled and not paused (live state, poll 30 s), every
board's cap is 1, and a released card is picked up within a tick if the cap allows and waits
honestly if not. `Spawn` exists for an immediate session regardless of caps, or when the
orchestrator is deliberately paused.

**Acknowledgement.** `POST /api/schedules` with `Start ∈ {Release, Spawn}` and `acceptSpend != true`
is **422 `spend_unacknowledged`**, with the preview (below) embedded in the problem-details
extension so the refusal itself says what would have been started. Accepting stamps
`SpendAcceptedAt/By`. `TargetStatus` for a card action is one of Backlog / InProgress / Review —
closing a card because it is Thursday is not a thing, and NeedsDecision needs a human's reason.

**Preview.** `POST /api/schedules/preview` (same body, no write) and `GET /api/schedules/{id}/preview`
return `SchedulePreviewDto`: the next three occurrences (UTC + local), the resolved target (agent:
live?, always-on?, session status; card: identifier, status, column, owner, archived?), the
effect in words (`willMove: Backlog → In Progress`, `willStartSession: true`, `spend:
none | orchestrator-under-cap | immediate-session`), the environment (`orchestratorPaused`, board
active count vs cap, any active `ModelAvailabilityHold` for the resolved kind, assigned agent or
default definition), and warnings. The CLI prints this before every create and refuses a spend
mode without `-AcceptSpend`.

**At fire time the card is re-checked, never trusted from the row.** Archived, owned, terminal or
NeedsDecision → `SkippedTargetGone` (a `Once` row is disabled; a recurring one keeps going and the
attention row says why). A `Spawn` that hits the subscription-quota or model-hold 409 records
`Refused` with the server's code and **never reroutes** (AGENTS.md); the attention row carries the
refusal.

### D7. Visibility

- **Agent modal → new "Schedules" tab** beside Terminal / Messages / Transcript
  (`AgentCliModal.tsx:73–95`): list (name, repeat in words, next fire local, last outcome), toggle,
  delete, fire-now, and a small create form (name, prompt, Once/Interval/Daily, time, weekday
  chips, zone select defaulting to the server default).
- **Queue panel** (`SessionMessageQueue.tsx`): a row with `origin == 'Scheduled'` shows its
  `noteHeader` as the badge. Cancelling it there is the existing action.
- **Card drawer**: read-only list of schedules targeting the card (`GET /api/schedules?cardId=`),
  each stating its spend mode in words ("will start a session on Thu 09:00").
- **Attention feed**: `AttentionKind.ScheduleMisfired` (Warning) while a schedule's last outcome is
  `SkippedNoSession`, `SkippedTargetGone`, `Refused`, `Failed`, or a fire row is stuck at
  `Claimed` for more than `Schedules:SweepSeconds × 12`; cleared by the next good fire or by
  disabling. `SkippedLate` is a fire row, not an attention row.
- SignalR `ScheduleChanged { scheduleId, agentId?, cardId? }` → query invalidation per
  `docs/project-context.md` §SignalR.

### D8. Creation surfaces and identity

`POST /api/schedules` and `scripts/schedule.ps1` (`new | list | get | preview | enable | disable |
remove | fire`). The script is `delegate.ps1`-shaped: ASCII-only, identity from the `ANTIPHON_*`
environment, long text via `-PromptFile`, agent reference resolved server-side exactly as
CARD-0291's `ResolveStandingAgentAsync` (`AgentTaskService.cs:1248`: guid, exact slug,
case-insensitive name; ambiguous refused naming candidates; pool delegates refused). That method
becomes a shared internal helper rather than a copy. `-Agent` defaults to `$env:ANTIPHON_AGENT_ID`
(`AgentSessionLaunchComposer.cs:44`), so a standing agent can schedule itself in one line; a pool
delegate is refused as a target and must name its caller. `CreatedBy` is self-reported free text
like `EditedBy` on cards.

---

## Ground truth

**G1. The one wall clock that exists.** `AwayDigestNotifier.IsDue` (`:77–88`) fires channel
digests at `Digest:SendTimesLocal` in `Digest:TimeZone` off a 60 s sweep (`DigestSettings.cs:9–13`).
It is channel-specific and last-sent-based (it drifts and cannot express weekdays), so it is a
precedent for zone-aware due computation, not code to reuse. The card's "nothing anywhere fires on
a wall clock" is true for prompts and cards.

**G2. The check sweep, exactly.** `RunScheduledChecksAsync` (`AgentTaskDispatcher.cs:1701`)
selects due rows, `ClaimCheckAsync` (`:1973`) is one conditional `ExecuteUpdateAsync` keyed on the
values read, the id goes to `AgentTaskCheckQueue` (unbounded channel, `SingleReader`), and
`AgentTaskCheckHostedService` drains it, logging and dropping a throwing check. `RunSweepAsync`
(`:531`) isolates each sweep since `99089ba`. Tick cadence `Delegation:PollIntervalSeconds` = 5
(`DelegationSettings.cs:15`). Tests: `AgentTaskCheckSweepTests` (`[NotInParallel]`, shared
fixture DB, assertions scoped to rows it created).

**G3. Enqueue semantics.** `EnqueueAsync(sessionId, body, mode, ct, origin, conversationKey,
sourceTaskId, contentDigest, noteHeader, onCreated)` (`SessionMessageQueueService.cs:155`). `Now`
requires a live, input-accepting session (`ConflictException` otherwise). `WhenIdle` writes the
row, then delivers immediately only if live ∧ accepting ∧ not working; a Starting session's rows
stay Pending until the launch path flushes. Origins today: Ui, Channel, System, Delegation, Check,
Supervision — `Scheduled` is new. The row has `NoteHeader` and `SourceTaskId`; `SourceScheduleId`
is new.

**G4. Relaunch carry-over.** `AgentControlService.StartAsync` (`:333–345`): when the new session id
differs from `PersistentSessionId`, every Pending row on the old session is moved to the new one.
The supervisor restarts `AlwaysOn` agents (`AgentSupervisorService.cs:82`; live statuses `:23`)
**through that same `StartAsync`** (`AgentSupervisorService.cs:202`), so the carry-over is on the
restart path — verified, not assumed. Live today: 7 always-on standing agents
(antiphon-check-interpreter, AZ Care, Family, Gym Stat Orchestrator, school-revision, Slack Test,
Torquay Leander); `Antiphon-Orchestrator` is live but **not** always-on.

**G5. The move paths.** `MoveAsync` (`CardService.cs:373`): spawn opt-in via `MoveCardRequest.Spawn`,
hold set on an unowned active landing without spawn (`:404–408`), result reports
`SpawnedSessionId` / `SpawnSuppressed`. `ReopenAsync` (`:556`) and `ApplyAutomatedMoveAsync`
(`:611`) call `ApplyColumnMove` (`:993`) directly and cannot spawn. `SpawnAsync` (`:727`) clears the
hold, moves a Backlog card into the first active column with actor `system`, then launches with the
board's active workflow definition prompt. The orchestrator's candidate query (`OrchestratorService.cs:507`)
requires `AutoDispatchHeldAt == null` (`:521`), no owner, no active session, and the retry gate;
board and column caps are checked in the dispatch loop, not in `TryClaimCardAsync`.

**G6. CARD-0040 shipped.** `CardWorkTransitionService` (60 s, `CardTransitions:IntervalSeconds`)
moves cards off bound-task evidence with the "last word" edge trigger; actor `card-transitions`;
Review → Done not automated (waits on CARD-0039 and a confidence signal). Documented in
`docs/agent-card-lifecycle.md:24–83`.

**G7. The other "workflow".** `POST /api/agents/{id}/queue` → `AgentService.AssignCardAsync`
(`:742`) queues a card on a standing agent and creates a `CardWorkflowRun` from the agent's
default template — the agent-queue workflow, distinct from the board's workflow definition that
`SpawnAsync` uses for its prompt. Whether queueing a card on a *running* agent starts work was not
traced this pass; see Left open (1).

**G8. Test-clock rule.** `docs/testing-and-build.md:54`: a frozen `TimeProvider` handed to the
queue service hangs the process. Recurrence is a pure function over an explicit `now`; sweep tests
use an offset clock (`AgentTaskPoolTests.OffsetTimeProvider` shape).

**G9. Attention rows are computed on read** (`AttentionService.cs:813` for `ChecksSpent`), with
`AttentionKind` in `AttentionDtos.cs` and a client mirror in `client/src/api/attention.ts:36` /
`attentionVisuals.ts:82`. Actions available: Reply, Retry, Cancel, Escalate — a misfire row uses
none of them (open the agent instead), so `AttentionAction` may need `Open`; S5 decides.

**G10. Migration precedent.** `20260816142301_AddAgentTaskCheckSchedule.cs`; latest is
`20260901200000_AddRoutingPins`. Procedure in `docs/bootstrap.md` §Creating EF Migrations.

---

## Slices

### S1 — Domain, recurrence, migration (2–3 h)

- `server/Domain/Entities/Schedule.cs`, `ScheduleFire.cs`; enums `ScheduleKind`, `ScheduleRepeat`,
  `ScheduleStart`, `ScheduleWhenTargetDown`, `ScheduleFireOutcome {Claimed, Delivered, Enqueued,
  QueuedForRelaunch, SkippedNoSession, SkippedLate, SkippedTargetGone, Moved, Released, Spawned,
  Refused, Failed}`; `QueuedMessageOrigin.Scheduled = 6`; `SessionQueuedMessage.SourceScheduleId`.
- `AppDbContext` config: indexes on `(Enabled, NextFireAt)`, `AgentId`, `CardId`; FKs cascade;
  unique `(ScheduleId, FireNumber)` on fires.
- `ScheduleRecurrence.NextAfter` (static, pure) + `Describe(schedule)` ("daily 09:00 Europe/London,
  Mon–Fri").
- Migration `AddSchedules` (three changes: two tables, one column).
- `Schedules` settings section: `Enabled`, `SweepSeconds` (5), `DefaultTimeZone`, `FireHistoryKeep`
  (50), `MaxPromptLength` (16 000).
- Tests: `ScheduleRecurrenceTests` (unit, table-driven, see Test matrix); migration applies on the
  shared Postgres fixture.

### S2 — Sweep, worker, prompt fire (4–6 h)

- `ScheduleService` (scoped): `ClaimDueAsync`, `FireAsync(FireClaim)`, `PruneFiresAsync`; the
  prompt arm per D3; `ScheduleFireQueue` (singleton channel); `ScheduleSweepHostedService`,
  `ScheduleFireHostedService` (`server/Infrastructure/Orchestration/`); registration in
  `Program.cs` beside `AgentTaskCheckQueue` (`:302`) and the hosted list (`:528–543`).
- Body/header builder; forbidden-body check helper exposed from the queue service; dedupe of the
  previous Pending row.
- Tests: `ScheduleSweepTests` modelled on `AgentTaskCheckSweepTests` (see matrix).
- The supervisor's restart path is `AgentControlService.StartAsync` (`AgentSupervisorService.cs:202`),
  so the carry-over in D3 applies to a supervised relaunch; the test
  `a_dead_always_on_agent_gets_a_row_on_its_persistent_session_that_the_relaunch_carries_over`
  drives that path end to end.

### S3 — API, CLI, docs (phase 1 complete) (3–4 h)

- `ScheduleEndpoints.cs`: `GET /api/schedules?agentId=&cardId=&boardId=&enabled=`,
  `GET /api/schedules/{id}` (with last 10 fires), `POST /api/schedules`, `POST /api/schedules/preview`,
  `GET /api/schedules/{id}/preview`, `PATCH /api/schedules/{id}` (concurrency token, recompute
  `NextFireAt`), `DELETE /api/schedules/{id}`, `POST /api/schedules/{id}/fire-now`. DTOs in
  `ScheduleDtos.cs`; enums as strings per `Program.cs:252`. Validation: zone, prompt length,
  per-kind body rule, target resolution (shared `ResolveStandingAgentAsync`).
- `scripts/schedule.ps1` (ASCII-only, `-PromptFile`, preview-before-create, `-AcceptSpend` in S4).
- Docs: `docs/antiphon-api.md` new "Schedules" block under Orchestration and workflows;
  `docs/ops-http.md` one row; `docs/orchestration-loop.md` §9 gains "scheduled prompts have
  shipped" and the unmerged-branch sweep becomes "a `Daily` schedule to the orchestrator"; AGENTS.md
  Cards-and-tracker bullet: one line — "A scheduled card action with `Release`/`Spawn` is a
  scheduled spend; it needs `acceptSpend` and is previewed first."
- Tests: `ScheduleEndpointsTests` (integration, WebApplicationFactory as `CardCorrectionIntegrationTests`);
  `ScheduleCliE2ETests` on the `CardCliE2ETests` precedent (script as a real process; every
  schedule it creates is a prompt or a `Start=None` card action, so the fixture never spawns).

### S4 — Card actions (phase 2) (3–5 h)

- `CardService.ReleaseAutoDispatchHoldAsync(cardId, reason, actor)` (internal, same guards as
  `ApplyAutomatedMoveAsync`, records the reason on the card's newest Move revision text or a
  `Kind.Move` row with unchanged columns — S4 picks the one `CardRevisionLog` supports without a
  new kind); the card arm of `FireAsync` per D6; `acceptSpend` gate (422 `spend_unacknowledged`);
  preview effect/environment fields; `schedule.ps1 new -Card … -To … -Start … -AcceptSpend`.
- Tests: see matrix (bookkeeping move, release visible to the orchestrator query, spawn refused
  without ack, spawn invoked with ack against a fake launch, every skip reason, last-word
  interplay with `CardWorkTransitionService`, quota 409 → `Refused` and no reroute).

### S5 — Attention and client (3–4 h)

- `AttentionKind.ScheduleMisfired` + `AttentionService` rule + client mirror and visual.
- Agent modal Schedules tab, queue-row badge, card-drawer list, `client/src/api/schedules.ts`,
  SignalR invalidation mapping. Vitest for the tab (render, toggle, delete confirm) and the badge.
- `ContractSnapshotTests` gains one scenario only if a story is added for the tab; otherwise no
  snapshot churn.

### S6 — Rollout and canary (1–2 h)

- Migration on the dev stack per `docs/bootstrap.md` (stop, `dotnet ef migrations add`, restart,
  check the log for `[ERR]`/`[FTL]`); `verify-dev-stack.ps1 -SkipBrowser`; client rebuild.
- Canary: a `Daily` prompt to a standing always-on agent at a time a few minutes out, then the
  delivery verdict by the AGENTS.md rule — a `UserPrompt` in `GET /api/sessions/{id}/transcript`
  carrying the header line, not the queue row's status. Then a `Start=None` card action on a
  scratch card, confirmed by `GET /api/cards/{id}/revisions` showing actor `scheduler` and
  `autoDispatchHeldAt` set. No `Release`/`Spawn` canary on a real board without the operator
  choosing the card.
- Browser check of the Schedules tab on 17203 after the watcher rebuild.

---

## What this card does not do

- No cron expressions (D2). No per-fire model call: a fire is a database write and a queue write.
- No schedule that creates a delegation (`AgentTask`) — Left open (2).
- No Review → Done automation and no touching of CARD-0040's conditional arm.
- No catch-up replay of N missed occurrences; at most one late fire, by construction (D5).
- No move of Windmill's jobs into Antiphon: the ones that exist are shell cleanups, not prompts.
- No global calendar page; visibility is per agent, per card, and on the attention feed (D7).
- No `Now`-mode delivery, ever.

## Left open, deliberately

1. **`Start = Assign` (queue the card on a standing agent, `AssignCardAsync`).** It is the third
   thing "into its workflow" could mean (G7). Whether it is a spend depends on whether a running
   agent picks up a newly queued card — not traced this pass. Decide after phase 1, with that
   traced.
2. **Schedule → delegation.** A pinned `AgentTask` per fire (`-Agent`, CARD-0291) would give a
   report, settlement, cost and incidents for free, at the price of a task row per fire — the exact
   trade-off CARD-0047 weighed for checks. A prompt to a standing orchestrator that then delegates
   covers today's examples; revisit if a schedule's *work* rather than its *delivery* needs a record.
3. **Should `Antiphon-Orchestrator` become always-on?** Today it is not, so a morning triage prompt
   to it defaults to `Skip` when it is down. Operator's call; `WhenTargetDown = Queue` on the
   schedule is the per-row alternative.
4. **`AttentionAction.Open`** for misfire rows — S5 decides whether the existing four actions are
   enough or the feed needs a neutral "open the agent" action.

## Test matrix

| Area | Test (name is the claim) | Slice |
|---|---|---|
| Recurrence | `once_in_the_past_is_due_now_and_never_again`; `interval_is_anchored_and_does_not_drift_after_a_slow_tick`; `daily_skips_days_not_in_the_mask_and_wraps_the_week`; `daily_in_the_spring_forward_gap_rolls_to_the_first_valid_minute` (Europe/London 2026-03-29 01:30 → 02:00 BST); `daily_in_the_fall_back_overlap_takes_the_first_occurrence` (2026-10-25 01:30); `an_unknown_zone_is_refused_at_create` | S1 |
| Claim | `a_due_schedule_is_claimed_once_when_two_ticks_race`; `the_recurrence_is_advanced_and_the_fire_row_written_before_the_hand_off`; `a_disabled_schedule_is_never_selected`; `a_throwing_fire_marks_its_row_failed_and_is_not_reclaimed`; `re_enabling_recomputes_next_from_now_instead_of_firing_late` | S2 |
| Missed | `a_once_schedule_overdue_by_hours_still_fires_and_says_how_late`; `a_daily_schedule_past_its_grace_writes_skipped_late_and_re_arms`; `three_days_of_downtime_produce_exactly_one_claim` | S2 |
| Prompt delivery | `an_idle_agent_gets_the_prompt_delivered_and_transcript_confirmed`; `a_working_agent_gets_a_pending_when_idle_row`; `a_starting_session_leaves_the_row_pending`; `a_dead_always_on_agent_gets_a_row_on_its_persistent_session_that_the_relaunch_carries_over`; `a_dead_standing_agent_is_skipped_with_no_session`; `a_never_launched_agent_is_skipped_regardless_of_policy`; `a_recurring_fire_cancels_the_previous_pending_copy`; `the_body_names_the_schedule_and_carries_no_task_marker`; `a_body_the_target_kind_forbids_is_refused_at_create` | S2 |
| API/CLI | `create_validates_target_zone_and_length`; `patch_requires_the_concurrency_token`; `preview_writes_nothing`; `fire_now_ignores_grace_and_does_not_advance_the_recurrence`; CLI round trip new → list → preview → disable → remove as a real process | S3 |
| Card actions | `start_none_moves_through_apply_automated_move_with_actor_scheduler_and_sets_the_hold`; `start_release_clears_the_hold_and_the_orchestrator_candidate_query_sees_the_card`; `start_spawn_without_accept_spend_is_422_with_the_preview`; `start_spawn_with_accept_spend_calls_spawn_async`; `an_archived_owned_terminal_or_needs_decision_card_is_skipped_target_gone`; `a_once_card_action_that_skips_is_disabled`; `the_evidence_sweep_does_not_undo_a_scheduler_move`; `a_quota_409_at_spawn_records_refused_and_never_reroutes` | S4 |
| Attention/client | `a_misfire_is_a_warning_row_until_the_next_good_fire`; Vitest: schedules tab renders and toggles; queued row shows the scheduled badge; card drawer states the spend mode in words | S5 |

## Sequencing and risks

- **Order:** S1 → S2 → S3 (phase 1 ships, ~9–13 h) → S4 → S5 → S6. One Shared dispatch per slice;
  S4 is the only one touching `CardService` (scope `cards`), S2 the only one touching the queue
  service (one enum value, one column, one exposed helper). Nothing here collides with CARD-0039's
  plan (importance/urgency) or CARD-0031's rail work.
- **Frozen clock (G8).** Recurrence tests pass `now` explicitly; sweep/queue tests use an offset
  provider. A `MutableTimeProvider` with a frozen instant handed to the queue service will hang.
- **Carry-over (D3).** Phase 1's dead-agent answer for always-on agents rests on
  `AgentControlService.cs:333–345`, which is on the supervisor's restart path
  (`AgentSupervisorService.cs:202`). If a future launch path bypasses `StartAsync`, add the same
  three-line carry-over there rather than changing the policy.
- **Spend at fire time.** `Spawn` bypasses caps like the manual move does; the preview says so.
  A 409 at fire is `Refused` + attention, never a reroute, never a retry — the next occurrence is
  the retry.
- **Two instances / two ticks.** The conditional `UPDATE` is the whole guarantee, as for checks;
  `a_due_schedule_is_claimed_once_when_two_ticks_race` is the test that must exist before S2
  lands.
- **Stale sweep count text.** `AgentTaskDispatcherHostedService` says "9 sweeps"; this plan adds
  none there, so it is not touched.

## Execution notes

- Build to an alternate output path (`--property:OutputPath=bin-<name>/`, forward slash) and
  delete the `bin-<name>` directories before finishing; the daemons hold `bin/`.
- Run `Antiphon.Tests` chunked by namespace; the new classes are `[NotInParallel]` with no group
  key like `AgentTaskCheckSweepTests`, because the sweep is global over the shared fixture DB.
- Migration procedure per `docs/bootstrap.md` §Creating EF Migrations; migrations auto-apply on
  restart — S6 restarts once.
- `scripts/schedule.ps1` must stay ASCII-only (Windows PowerShell 5.1 fallback).
- Never print a session's transcript body in a commit message or the report when it might carry
  a secret; cite sequence numbers.
