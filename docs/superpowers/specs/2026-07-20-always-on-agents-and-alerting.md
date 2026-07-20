# Spec: Always-On Agents (Supervision) + Alert Routing to Channels

Date: 2026-07-20
Status: **Approved** — all Q-IDs resolved (see [Decisions](#decisions--resolved-2026-07-20-review)); implementation pending
Builds on: [2026-07-19-pty-host-split.md](2026-07-19-pty-host-split.md) (sessions survive runner
restarts and are re-adopted — the discovery half of "find all possible running ptys" is done).

## Problem

1. **Always-on agents.** Some agents (e.g. the Telegram-bound `school-revision`, `torquay-leander`)
   should simply *always be running*: started automatically when the server comes up (after
   accounting for sessions that survived in detached pty-hosts), restarted automatically when they
   crash, with the failure **recorded** and restart attempts **backed off** so a crash-looping
   agent can't melt the machine. Today a crashed agent just sits `Failed` until a human presses
   Start (the only exception: an inbound Telegram message wakes its bound agent —
   `ChannelBridgeService.EnsureAgentSessionAsync`, `ChannelBridgeService.cs:164-206`).
2. **Alerting.** When something goes wrong — an agent crashes, supervision gives up, the runner is
   unreachable, warnings/errors are logged — nothing tells the operator. We already run a
   messaging gateway that can deliver to Telegram; alerts should route through it, filtered by
   severity, to a designated channel.

These interlock: supervision is the first serious alert *producer* ("agent X crashed, restarting
in 40s (attempt 3)", "gave up on agent Y — circuit open").

## What already exists (leverage, don't rebuild)

| Piece | Where | Reused for |
|---|---|---|
| Idempotent, row-locked agent start (card or interactive, resume-aware) | `AgentControlService.StartAsync` (`:58`) | The supervisor's restart primitive |
| Deliberate stop → `AgentStatus.Stopped` | `AgentControlService.StopAsync` (`:190`) | The "user intent" hook for the suspend latch |
| Crash observation: exit code ≠ 0 → session+agent `Failed` (live path + 15s reconciler backstop) | `AgentSessionRuntime.CloseSessionOnExitAsync`, `SessionReconciliationService` | Supervisor's crash signal |
| Exponential backoff precedent (base doubled per failure, capped, max-attempts) | `RetryScheduler.GetFailureDelay` (`:21`) | Supervision backoff algorithm |
| Periodic-work template (`BackgroundService` + `PeriodicTimer` + scoped tick) | `OrchestratorTickHostedService`, `SessionReconciliationHostedService` | `AgentSupervisorHostedService` |
| Server→Telegram outbound: `IAntiphonMessagingProducer` → `channels.outbound` → gateway `OutboundConsumerService` → `TelegramChannelAdapter.SendAsync` | `Antiphon.Messaging.Client` / `.Service` / `.Telegram` | Alert delivery — zero new transport |
| Channel catalog (DB rows per external conversation, bound/edited in Channels UI) | `ChatChannel` entity + `ChannelsPage.tsx` | Selecting the alert sink channel |
| Dedup/cooldown store pattern | `WatchdogCooldownStore` | Alert de-noising |
| In-memory fake messaging client | `FakeAntiphonMessagingClient` | Alert pipeline tests |
| SignalR event bus + toast/browser-notification precedent | `IEventBus`, `useSessionFinishedToasts.ts` | `AlertRaised` UI surfacing |

## Part A — Always-on agent supervision

### Data model (one migration)

**`Agent` gains** (additive):
- `AlwaysOn` (bool, default false) — supervision opt-in. Always on means always on — no sub-modes.
- `RemoteControlEnabled` (bool, default true) — a **general agent setting, deliberately NOT a
  supervision flag** (user decision, 2026-07-20): remote control is part of an agent's normal
  setup, applied by *every* start path — manual UI start, channel-bridge auto-start, supervised
  restart. The per-start "Remote control" checkbox on `AgentsPage` becomes this persisted agent
  setting; `StartAgentRequest.RemoteControl` becomes nullable (null = use the agent's setting)
  so existing callers keep working.

**New `AgentSupervisionStates`** (1:1 with Agent; separated so supervisor churn never touches the
`Agents` row / its concurrency semantics):
- `AgentId` (PK, FK), `Suspended` (bool) — the **user-intent latch** (see below),
- `ConsecutiveFailures` (int), `NextRestartAt?`, `LastAttemptAt?`,
- `LastEscalationTier` (int) — highest backoff tier already alerted on (so tier-crossing alerts
  fire exactly once per escalation, not per attempt),
- `LastHealthyAt?` — for backoff reset, `UpdatedAt`.

**New `AgentIncidents`** — the "records the issue" requirement; append-only audit feeding both the
UI and the alert pipeline:
- `Id`, `AgentId`, `SessionId?`, `Kind` (enum: `Crash`, `StartFailure`, `RestartScheduled`,
  `Recovered`, `BackoffEscalated`, `SuspendedByUser`, `ResumedByUser`),
- `Severity` (`Info|Warning|Error|Critical`), `Message`, `ExitCode?`, `FailureReason?`, `CreatedAt`.
- Retention: pruned by a nightly pass (default keep 30 days, cap 500/agent).

### Supervisor loop

`AgentSupervisorService` (scoped logic) + `AgentSupervisorHostedService`
(`PeriodicTimer`, `Supervision:TickSeconds`, default 10s — same template as the reconciler).
Each tick, for every `AlwaysOn` agent:

1. **Skip if `Suspended`** (user pressed Stop — supervision must never fight the human).
2. **Healthy?** Agent has a live session (`Starting/Running/Stopping`): if
   `ConsecutiveFailures > 0` and the session has been `Running` longer than
   `HealthyUptimeResetMinutes` (default 10), reset failures and escalation tier to 0 and record a
   `Recovered` incident (Info).
3. **Crashed / not running:** if `NextRestartAt` is null, schedule it:
   `now + Backoff(ConsecutiveFailures)`, record `RestartScheduled` (Warning) with the captured
   `ExitCode`/`FailureReason` from the dead session (that's the **Crash** incident when the exit
   was non-zero, recorded once per death, not per tick).
4. **Due?** `now ≥ NextRestartAt`: call `AgentControlService.StartAsync(agentId,
   new StartAgentRequest(Fresh: fresh))` (remote control comes from the agent's
   `RemoteControlEnabled`) — idempotent and row-locked, so races with the channel-bridge
   auto-start or a human are safe.
   - `fresh = ConsecutiveFailures ≥ FreshAfterResumeFailures` (default 2): resume the previous
     Claude conversation by default; if resume itself keeps failing (e.g.
     `ClaudeSessionNotFound`), fall back to a fresh conversation rather than crash-loop.
   - Success → clear `NextRestartAt`; failure (exception) → `ConsecutiveFailures++`,
     `StartFailure` incident (Error), reschedule.

**Backoff ladder — never gives up (Q2 decision).** There is no circuit breaker that stops trying;
the retry interval simply keeps growing through minutes → hours → **daily** → multi-day, capped at
**30 days**, and stays there:

```
Backoff(n) = min(BaseSeconds · 2ⁿ, 30 days)
           = 5s, 10s, 20s, 40s, …, ~14 min (n=8), ~1.9 h (n=11),
             ~15 h (n=14), ~2.5 d (n=16), ~10 d (n=18), 30 d (n≥20, cap)
```

**Logging is a first-class requirement here**: every scheduling decision logs and records — the
attempt number, the delay chosen, and the absolute `NextRestartAt` ("attempt 12 failed
(exit -1: …); next retry 2026-07-21 06:14:03Z, backing off 15h, tier: hours"). The agent DTO
exposes `ConsecutiveFailures` / `NextRestartAt` so the UI shows a live countdown, and
`GET /incidents` gives the full history. When the ladder crosses a tier boundary
(≥ 1 hour, ≥ 1 day) a **`BackoffEscalated` incident** fires exactly once per escalation
(tracked by `LastEscalationTier`) — Warning at hourly, **Critical at daily** ("agent X has been
failing for a day; now retrying daily") — so a long-degraded agent pages once, not 20 times, and
recovery still resets everything via step 2.

**Boot auto-start** is not a special case: on server start the first tick sees "AlwaysOn, not
suspended, no live session, no backoff state" and starts immediately. Sessions that **survived in
detached pty-hosts** are already re-adopted (`adopted=true`, DB rows still `Running`) before the
runner serves requests, so the supervisor's "healthy" check sees them and does nothing — the
"find all possible running ptys first" requirement falls out of the pty-host adoption work for
free. Guard: the tick skips (logs once) while the runner is unreachable, so a runner restart
window never triggers a false restart storm — the 15s reconciler settles truth first.

### The suspend latch (deliberate stop ≠ crash)

Today the only crash-vs-deliberate signal is exit code + who called `StopAsync`. Supervision makes
the intent explicit:
- `AgentControlService.StopAsync` on an `AlwaysOn` agent sets `Suspended = true` and records
  `SuspendedByUser` (Info). The agent stays down until a human presses Start.
- `StartAsync` (any caller) clears `Suspended`, `ConsecutiveFailures`, the escalation tier, and
  records `ResumedByUser` — a human Start is always an immediate, backoff-free attempt.
- Ops kills that bypass the server (runner `-KillSessions`, task #8-style maintenance) do **not**
  suspend — the supervisor restarting agents after maintenance is exactly the desired behaviour.
- Card-owned work is untouched: supervision only ensures the *cardless interactive session*
  exists; `StartAsync` already resolves card-vs-interactive itself, and the orchestrator keeps
  owning card lifecycles (same boundary the reconciler respects).

### Session liveness probe (stale-session detection, Q4)

Distinct from RC health: a session can hold a perfectly healthy PTY and bridge yet be *stale* —
the Claude process wedged, the TUI unresponsive, the API conversation dead. Supervision gains an
explicit liveness probe with two tiers (both idle-gated, like everything else):

1. **TUI echo probe (free, default).** Type a single character into the composer via the normal
   input path, verify the rendered screen changes (the runner's `SnapshotScreen` already exposes
   this), then backspace it. No LLM turn, no tokens, proves the whole chain
   server → runner → pipe → pty-host → ConPTY → Claude TUI is alive. Default cadence: hourly
   while idle.
2. **Round-trip probe (token-costing, sparing).** Enqueue a tiny prompt via the existing
   `SessionMessageQueueService` (`WhenIdle` mode) — "healthcheck: reply with exactly `pong`" —
   and await the turn-end reply (the `SessionFinished`/turn-end machinery already detects
   completion). Verifies the full loop including the model API. Default cadence: every 6 h while
   idle (configurable; also exposed as an on-demand "Probe now" action on the agent card).

Probe failure ⇒ treated exactly like a crash: `LivenessProbeFailed` incident (Error) + supervised
restart-when-idle (resume first, ladder applies). Existing pieces reused: message queue WhenIdle
mode, turn-end detection, screen snapshots — no new transport. (Nothing equivalent exists today —
the Watchdog matches *patterns in output* but never initiates a check.)

### Remote-control health watch (re-arm, then restart-when-idle)

Remote control silently dying is a known failure mode (upstream Claude Code bugs: idle-TTL kills
after ~20 min, websocket drops ~25 min, broken auto-reconnect — see claude-code issues #32982,
#31853, #34255). A session can look perfectly healthy while its claude.ai bridge is long dead. For
always-on agents with `RemoteControlEnabled`, the supervisor also watches RC health and repairs it
— **never while the agent is mid-work**.

**Detection** (server-side port of the proven `rc-status.ps1` probe): for each supervised
`Running` ClaudeCode session, take the child pid (`RunnerSessionDto.Pid`) and check
1. `%USERPROFILE%\.claude\sessions\<pid>.json` — Claude's own per-process state file;
   `bridgeSessionId` present = RC was armed;
2. live bridge connection — the process holds established TCP connections to Anthropic
   (`160.79.0.0/16:443`) even when idle; armed-but-zero-connections while idle = bridge dropped
   (via `GetExtendedTcpTable` P/Invoke, same signal the PowerShell probe uses).

A session is **RC-degraded** when: `RemoteControlEnabled`, armed (or never armed despite the
setting), no bridge connection across `ConsecutiveFailedProbes` (see below), and the session is
**idle** — no new output sequence for the idle window and no queued messages. Idleness gates
*everything*: a working agent is never disturbed, and busy sessions legitimately churn
connections.

**Thresholds are calibrated, not guessed (Q7 decision) — CALIBRATED 2026-07-20.** Measurement on
two idle RC-armed sessions, 74 samples each at 30s cadence over ~37 min:
- `.claude/sessions/<pid>.json` `updatedAt` **never changed during idleness** (1 distinct value
  per session) — it is a status-transition stamp, NOT a heartbeat. It cannot be a liveness signal.
- Bridge TCP connections were **continuously ≥ 2 (min 2, max 3), with zero samples at 0** across
  both sessions. The connection count is the reliable, always-on liveness signal.

Verdict thresholds derived from that evidence: dead = **5 consecutive zero-connection probes at
60s cadence** (5 min of sustained absence, vs a healthy signal that never blipped once in 148
samples) — comfortably within the "miss 5–10 normal probes" rule with the measured signal being
100% stable. `ConsecutiveFailedProbesBeforeAction: 5` is therefore the shipped default.

**Repair escalation** (each step is an incident + alert):
1. **Re-arm in place** — send `/remote-control` into the live PTY (the existing
   `SendRemoteControlCommandsAsync` path), then re-probe after a settle period. Cheap,
   non-destructive, fixes the common dropped-websocket case without losing terminal state.
2. **Restart when idle** — after `RcReArmAttemptsBeforeRestart` (default 2) failed re-arms:
   kill + resume the session via the normal supervised restart (conversation continuity via
   `--resume`; a fresh Claude process reliably re-establishes the bridge). Counts toward the
   supervision backoff so an RC-flapping environment can't cause a restart storm.
3. Still degraded after a restart cycle → `RcDegraded` incident (Error) + alert, stop escalating
   until the next probe interval — a broken claude.ai backend shouldn't burn sessions.

New incident kinds: `RcDegraded`, `RcReArmed`, `RcRestart`. Settings:
`Supervision:RcWatch { Enabled: true, ProbeIntervalSeconds: 60, IdleQuietMinutes: 5,
ReArmAttemptsBeforeRestart: 2 }`.

### UI & API

- `AgentSettingsModal`: "Always on" toggle; "Remote control" moves here as a persistent agent
  setting (the per-start checkbox on the card goes away).
- Agent card (`AgentsPage.tsx`): supervision badge — shield (supervised & healthy), countdown
  ("restarting in 38s · attempt 3"), open circuit (red, "gave up — click Start to reset").
  Driven by existing `AgentChanged` invalidations plus supervision fields on the agent DTO.
- `GET /api/agents/{id}/incidents?take=50` — incident history (drawer/panel on the card).
- `PATCH /api/agents/{id}` gains the two new flags (existing update path).

### Settings

```jsonc
"Supervision": {
  "Enabled": true,
  "TickSeconds": 10,
  "BackoffBaseSeconds": 5,
  "BackoffMaxSeconds": 2592000,
  "HealthyUptimeResetMinutes": 10,
  "FreshAfterResumeFailures": 2,
  "IncidentRetentionDays": 30,
  "RcWatch": {
    "Enabled": true,
    "ProbeIntervalSeconds": 60,
    "IdleQuietMinutes": 5,
    "ConsecutiveFailedProbesBeforeAction": 5,   // pending calibration: 5-10 missed heartbeats
    "ReArmAttemptsBeforeRestart": 2
  },
  "LivenessProbe": {
    "Enabled": true,
    "TuiEchoIntervalMinutes": 60,
    "RoundTripIntervalHours": 6
  }
}
```

(`BackoffMaxSeconds` = 2,592,000 — the 30-day cap; `MaxConsecutiveFailures`/circuit settings are
gone, replaced by the unbounded ladder + tier-escalation alerts.)

## Part B — Alert routing through Antiphon messaging

### Model & service

- `Alert` (record + `Alerts` table for audit/UI): `Id`, `Severity` (`Info|Warning|Error|Critical`),
  `Source` (`supervisor|reconciler|launch|bridge|runner|log|watchdog`), `AgentId?`, `SessionId?`,
  `Title`, `Detail?`, `DedupKey`, `CreatedAt`, `RoutedAt?`, `SuppressedCount`.
- `IAlertService.RaiseAsync(Alert)` — persists, publishes SignalR `AlertRaised` (client toast for
  `Error+`, mirroring `useSessionFinishedToasts`), and hands to the router. Fire-and-forget from
  callers' perspective; the pipeline must never take down the caller.

### Producers (initial set — explicit domain alerts, not log scraping)

| Source | Event | Severity |
|---|---|---|
| supervisor | Crash observed (non-zero exit) | Warning |
| supervisor | Restart succeeded after failures (`Recovered`) | Info |
| supervisor | Start attempt failed | Error |
| supervisor | Backoff escalated to hourly tier | Warning |
| supervisor | **Backoff escalated to daily tier** (failing ≥ a day) | **Critical** |
| supervisor | Liveness probe failed / RC degraded | Error |
| reconciler | Session/agent corrected (phantom closed) | Warning |
| launch queue | Interactive/orchestrated launch failed | Error |
| bridge | Inbound message dropped (agent never became ready) | Warning |
| runner | Unreachable → reachable transitions (edge-triggered only) | Error / Info |

### Routing & delivery (reuses the proven outbound path)

- **Sink selection lives in the channel catalog**: `ChatChannel` gains `AlertMinSeverity?`
  (nullable enum; null = not an alert sink). The Channels UI gets a per-channel "Alerts: off /
  Warning+ / Error+ / Critical" select. Ops flow (Q10 decision — a **new dedicated admin/dev
  Telegram group**): create the group, add the existing bot, send one message so it appears in
  the catalog, set its alerts to Warning+ — no config files, no restarts, works for N sinks with
  different thresholds.
- `AlertRoutingService`: for each alert, find sink channels with `AlertMinSeverity ≤ severity`,
  format (`🔴/🟠/🔵 [severity] title — agent, detail, time`), and send via the **existing**
  `IAntiphonMessagingProducer.SendAsync(new ChannelReply(Channel: sink.Provider,
  ConversationId: sink.ExternalId, Text: …))` → `channels.outbound` → gateway →
  `TelegramChannelAdapter`. No new transport, and future providers (WhatsApp/Discord) get alert
  support automatically when their adapter lands.
- **Noise control (Q6 decision — hard send-window, non-negotiable before the log tap):**
  - **At most one Telegram message per sink per `MinMinutesBetweenSends` (default 5).** Every
    alert raised inside the window accumulates; when the window closes, ONE grouped message
    flushes: alerts grouped by `DedupKey` with counts, worst severity first
    ("🔴 agent X circuit… · 🟠 reconciler corrected session ×3 · 🟠 launch failed ×2").
  - Dedup inside the group: same `DedupKey` never repeats, only its `×N` counter grows.
  - `CriticalBypassWindow` (default **false**, per the hard 1-per-5-min rule) exists in config if
    paging-style immediacy is ever wanted for Critical.
  - Pattern: `WatchdogCooldownStore` generalised into an `AlertThrottle`; all counters
    `TimeProvider`-driven for deterministic tests.
- Delivery-degraded mode: producer/broker down → alerts still persist to DB + SignalR; router
  retries on its own cadence. Telegram down → gateway's existing bounded retry; alerting must
  never crash-loop the server (all sinks best-effort).

### Log tap (last slice, off by default)

Custom Serilog sink `AlertingLogSink` (server only): `Warning+` events → `Alert(Source: "log",
DedupKey: hash(message template))` into the same pipeline. Two hard rules learned from every
system that's done this: (1) the alert pipeline's own log events are tagged
(`LogContext` property) and excluded by the sink — no feedback loops; (2) the sink is
sampling/throttled *before* the router's dedup even sees it. Config:
`Alerts:LogTap { Enabled: false, MinLevel: "Warning" }`. Runner/pty-host logs are out of scope for
the tap (they surface via the server's structured events already); revisit with the
discovery-service spec if needed.

### Dependency callout: gateway stays OUT of our process — a fake gateway is the dev/test tool

Alert *delivery* (not recording) requires a gateway consuming `channels.outbound` + Redpanda.
**User decision (2026-07-20): the real `Antiphon.Messaging.Service` (which talks to real
Telegram) is NOT added to the AppHost/always-on set.** It stays a separately deployed/operated
app. What the dev stack and E2E tests get instead is a **fake gateway** — a real-Kafka-connected
stand-in that acts as a test tool:

**New `src/Antiphon.Messaging.FakeGateway`** (tiny web app, same contracts, no Telegram):
- **Records deliveries**: consumes `channels.outbound` (own consumer group), appends every
  `ChannelReply` it would have delivered to `logs/fake-gateway/outbound.jsonl` and the console —
  a local, greppable delivery log.
- **Assertable over HTTP**: `GET /deliveries?since=&channel=&conversationId=` returns recorded
  deliveries (what tests poll to assert "the alert reached the sink"); `DELETE /deliveries`
  resets between tests.
- **Pushes data through**: `POST /inbound` produces a synthetic `ChannelMessage` onto
  `channels.inbound` (templates for direct/group Telegram-shaped messages) — drives the whole
  bridge path (inbound → catalog upsert → agent prompt → reply → outbound → recorded delivery)
  without any external service.
- **Failure injection**: config/endpoint to simulate delivery failures (`SendResult.Failed`,
  delays) so retry/degraded-mode paths are testable.
- AppHost wires the **fake** gateway into the dev stack (built-exe daemon pattern — the
  `dotnet run` job lesson applies to anything that spawns children; the fake spawns none, but
  consistency is free). The real gateway's deployment is explicitly out of scope for this spec.
- Layering: `FakeAntiphonMessagingClient` (in-memory, no Kafka) stays the unit-test seam; the
  fake gateway is the *integration/E2E* seam — real producer, real Redpanda, real consumer group
  semantics, fake egress.
- **Packaged + documented (Q9 decision):** the fake gateway joins the existing messaging NuGet
  set (`src/Messaging.Pack.props` versioning, published by the `publish-nuget` workflow to the
  GitHub Packages feed — the same pipeline `Antiphon.Messaging`/`.Client`/`.Client.Testing`
  already ship through, which downstream repos like school-revision consume). Downstream
  consumers get the identical Kafka test tool for their own local dev. Documented in
  `docs/messaging-standalone.md` (run it, assert against it, inject through it). **Deployed
  environments depend purely on the deployed real gateway** — the fake never ships to prod.

## Slices (reviewable increments)

| # | Slice | Contents | Size |
|---|---|---|---|
| 1 | Supervision core | Migration (`AlwaysOn`, `RemoteControlEnabled`, `AgentSupervisionStates`, `AgentIncidents`), supervisor with the 30-day backoff ladder + tier-escalation incidents + rich scheduling logs (attempt #, delay, NextRestartAt), suspend latch wiring, `RemoteControlEnabled` honored by all start paths, settings; integration tests: crash→ladder→restart, suspend honored, boot start, adopted-session no-op, tier escalation fires once, runner-unreachable guard | L |
| 2 | Supervision surface | Agent DTO fields (incl. `NextRestartAt`/failure count for countdowns), PATCH flags, incidents endpoint, `AgentSettingsModal` toggles (Always on, Remote control), card badge + countdown + incidents drawer | M |
| 3 | Session health: RC watch + liveness probes | **Calibration first**: measure idle-session heartbeat cadence (`.claude/sessions` updatedAt + bridge TCP churn) and set thresholds at 5–10 missed heartbeats. Then: server-side bridge probe, re-arm → restart-when-idle escalation, TUI echo probe + round-trip "pong" probe (`SessionMessageQueueService` WhenIdle), `RcDegraded/RcReArmed/RcRestart/LivenessProbeFailed` incidents, "Probe now" action; tests with a fake probe | M–L |
| 4 | Alert core | `Alerts` table, `IAlertService`, SignalR `AlertRaised` + toast, producers wired (supervisor incl. RC watch, reconciler, launch, bridge, runner edge) | M |
| 5 | Routing + Telegram sink | `ChatChannel.AlertMinSeverity` + Channels UI select, `AlertRoutingService` + `AlertThrottle` (dedup/rate/digest), delivery via producer; E2E with `FakeAntiphonMessagingClient` + a real end-to-end smoke to the designated Telegram channel | M–L |
| 6 | Fake gateway + log tap + docs | `Antiphon.Messaging.FakeGateway` (outbound recorder + HTTP assert/inject API + failure injection) wired into the AppHost dev stack **and packed into the messaging NuGet set** (`Messaging.Pack.props` + publish-nuget workflow); alert E2E rewritten against it; docs in `docs/messaging-standalone.md`; `AlertingLogSink` (off by default, loop-guarded); retention/pruning pass; spec/CLAUDE.md/AGENTS.md updates | M–L |

Rough total: another multi-day epic; slices 1–2 deliver the user-visible "always on" promise on
their own, 3 delivers RC self-healing, 4–5 deliver Telegram alerts, 6 is hardening.

## Testing strategy

- Supervision: `FakeAgentProtocolAdapter` crash simulation (`ThrowOnStart`, nonzero `ExitCode`)
  through the real `AgentControlService` + supervisor tick with a controlled `TimeProvider`
  (already DI-injected) — deterministic backoff/circuit tests, no real sleeps.
- Alerts: unit-test the throttle (dedup windows, digests) with `TimeProvider`; pipeline tests via
  `FakeAntiphonMessagingClient` asserting exact `ChannelReply`s; integration/E2E through real
  Redpanda against the **fake gateway** (`GET /deliveries` assertions, `POST /inbound` to drive
  the bridge path, failure injection for retry paths). A gated headed smoke to a real Telegram
  test channel remains optional (skipped unless env var set, per repo convention).
- Guard tests: supervisor never acts on `Suspended` or card-owned agents; alert pipeline exception
  never propagates to a caller.

## User decision updates (2026-07-20)

- Remote control is **not** a supervision sub-flag: `RemoteControlEnabled` is a general agent
  setting applied by every start path ("remote control is just naturally part of the setup").
  `AlwaysOn` stays a single unqualified switch.
- Added instead: the **RC health watch** — when a supervised idle session's remote-control bridge
  looks old/disconnected, re-arm `/remote-control` in place, and if that keeps failing, restart
  the session while idle to restore remote access.
- The real messaging gateway (talks to real Telegram) is **not** added to the AppHost/dev
  process. Instead a Kafka-connected **fake gateway** joins the dev stack as a test tool:
  records would-be deliveries (JSONL + HTTP assertions), injects inbound messages, simulates
  failures. Real gateway deployment stays out of scope.

## Decisions — RESOLVED (2026-07-20 review)

| ID | Decision |
|---|---|
| Q1 | Defaults accepted: 5s base ×2, reset after 10 min healthy — tail extended per Q2. |
| Q2 | **No give-up circuit.** Backoff ladder keeps growing through hourly → daily → multi-day, capped at **30 days**, retrying forever at the cap. First-class logging: every attempt records attempt #, chosen delay, absolute next-retry time; tier-crossing alerts (Warning at hourly, Critical at daily) fire once per escalation. |
| Q3 | Yes (default): Stop suspends supervision until a manual Start. |
| Q4 | Yes: resume, fresh after 2 failed resumes. **Plus** a separate session **liveness probe**: TUI echo probe (free, hourly) and round-trip "reply with pong" probe (every 6h idle / on demand) — probe failure ⇒ supervised restart-when-idle. |
| Q5 | Default accepted: sink selection via `ChatChannel.AlertMinSeverity` + Channels UI. |
| Q6 | Warning+ **with a hard send window: max 1 Telegram message per sink per ~5 min**; everything inside the window groups into one deduped digest (`×N` counters). Critical-bypass exists in config but defaults off. |
| Q7 | 5-min idle gate stays, but dead-verdict thresholds are **calibrated**: slice 3 first measures idle-session heartbeat cadence (`.claude/sessions` updatedAt + bridge TCP churn) and sets the threshold at **5–10 missed normal heartbeats**. |
| Q8 | Default accepted: log tap ships disabled until domain alerts prove quiet. |
| Q9 | Fake gateway is the first-class Kafka test tool for local dev; deployed environments depend purely on the deployed real gateway. **Documented + published as a NuGet package** alongside the existing messaging packages (school-revision-style consumption). |
| Q10 | New dedicated Telegram **admin/dev group** with the existing bot as the first sink. |

Status: **Approved — ready to implement** (slices 1→6).
