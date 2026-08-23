# CARD-0162 — herdr S4 state mirror: event pump, status corroboration, never an authority — plan

**Date:** 2026-08-24 · **Card:** CARD-0162 (`e78ba737-3454-4d01-ad39-e712676146fd`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `feat/card-task-6051fcdc` @ `6005831` (= master after CARD-0160 S2 + CARD-0161
S3). Every file:line below was re-read out of the code on that commit. Every herdr behavior below
marked **measured** was measured LIVE in this pass — herdr 0.8.2, protocol 20, this machine,
2026-08-24, through the same named-pipe NDJSON framing `HerdrClient` uses, plus a fresh
`herdr api schema --json` dump (protocol 20, schema_version 1). Raw probe transcripts:
`.antiphon` (gitignored) — the load-bearing results are inlined in § Live measurements.

**Established facts, not re-derived here:**
- The Investigate stage (task `e22f180b`, findings on the card 2026-08-24): the incident machinery
  map (`AgentIncidentKind` next free = 34, `TaskProgressStalled = 32` as the detection-only
  precedent, `AgentSupervisorService.RecordIncidentAsync` with `raiseAlert:false` = timeline-only,
  `AgentSupervisorHostedService` as the sweep host, `AgentTaskDispatcher.DetectStalledProgressAsync`
  as the dedupe/pull-before-raise analog); `events.subscribe` live-reachable with
  `HerdrClient.SubscribeEventsAsync` at zero callers; the `pane.agent_status_changed` payload and
  the pane_id-required subscription finding; the S4b recommendation for `pane.report_agent`.
- The S2 plan (`docs/superpowers/plans/2026-08-23-card-0160-herdr-s2-launch-path-plan.md`) §6B —
  the pump's charter (one stream, `pane.closed` → Exited, reconnect re-verification, OCE only when
  cancelled, reconnect forever) — **still unshipped on `6005831`** and owned by this card; §6A's
  adoption evidence bar (`AdoptHerdrSessionsAsync`, `SessionRunnerRuntime.cs:415`), which this
  card's verification path reuses.
- The S3 plan (`docs/superpowers/plans/2026-08-23-card-0161-herdr-s3-delivery-adapter-plan.md`)
  §6/§9: `blocked` defers and only defers (`FlushResult.Nothing`, `SessionMessageQueueService`);
  the confirm-loop Enter withhold; `done` ≠ `idle` (measured M4); the single-session GET refresh
  (`RunnerSession.RefreshHerdrSurfaceAsync`, `SessionRunnerRuntime.cs:625`) carrying
  `RunnerSessionDto.AgentStatus`; S3 explicitly did not open the subscription stream — S4 owns
  everything event-shaped, including the UI-WhenIdle-while-blocked promptness gap.
- CARD-0055/0056/0153 discipline (CLAUDE.md): verdicts come from transcript rows only; kills need
  positive evidence and are withheld on ambiguity; a detector whose evidence is a screen heuristic
  gets a row for a human, never a trigger finger. CARD-0117/0135/0149/0159 plans (same docs tree)
  carry the same never-act-on-incomplete-evidence severity conventions.

**Related:** CARD-0160 (S2), CARD-0161 (S3), CARD-0153 (stall detection-only precedent),
CARD-0055/0056 (evidence discipline), CARD-0041 (`FlushIfIdleAsync` narrow-flush precedent),
CARD-0047 (trust-dialog blocked-and-invisible shape — the disagreement class this card makes
visible), CARD-0101 (incident-storm dedupe), CARD-0067 (global sweep on a clock nobody's turn owns).

---

## Verdict up front — the twelve decisions

1. **Pump process: runner-owned.** A new `HerdrEventPumpService` in the session runner — the only
   process with a `HerdrClient`, the pane↔session map, and a lifetime coupled to the panes. Status
   reaches the server as a new additive SSE event (`SessionAgentStatus`) on the existing `/events`
   surface; an older server ignores it by construction (`ParseEvent` returns null on unknown event
   names, `SessionRunnerHttpClient.cs:466`). All incident logic is server-side. §2.
2. **`pane.closed`: full §6B lands in this card — but every event is a verification TRIGGER, never
   evidence.** Measured fact E5 forces this: herdr replays its historical `pane_closed` buffer to
   every new subscriber, so a pump that trusted the event would re-kill sessions on stale closes at
   every reconnect. On `pane_closed`/`pane_exited` for a tracked pane the pump re-runs the §6A
   evidence bar (`pane.get` + `pane.process_info` vs the sidecar's `ChildPid`); only a failed bar
   registers Exited(`HerdrPaneClosed`). The reconnect sweep is the same verification over all live
   herdr sessions. §3.
3. **Subscription model: ONE stream, recycled on pane-set change.** Entries: `pane.closed` +
   `pane.exited` (type-only, global) + one `pane.agent_status_changed {pane_id}` per live herdr
   session (pane_id is required by schema — measured E2). Launch/exit → signal → tear down and
   reopen with the new set; after EVERY (re)subscribe, baseline-sweep all tracked sessions with
   `pane.get` (change-only semantics, measured E3) — which is also §6B's reconnect verification.
   No stream at all while zero herdr sessions exist. §4.
4. **`HerdrSubscription` grows optional `PaneId`; subscribe types are dotted, wire events are
   underscored (both measured); and one client fix: the subscribe ack's error `id` comes back
   SUFFIXED (`<id>:sub:1:probe`, measured E2), so `RequireResult`'s strict match must be relaxed
   for the subscribe ack or a `pane_not_found` surfaces as a protocol exception instead of a
   retryable API error.** §4.
5. **Disagreement matrix: two pairs raise, everything else never does.** (a) herdr
   `working`/`blocked` × `IsWorkingAsync == false` — screen-active-but-transcript-idle (the
   CARD-0047 trust-dialog / bind-failure class); (b) herdr `idle`/`done` × `IsWorkingAsync == true`
   — transcript-working-but-screen-done (the CARD-0041/0055 stranded-working class). `unknown` and
   an unobserved status never raise. Hysteresis: the herdr status must have been stable ≥
   `MinSustainedMinutes` (default 10), and the sweep pulls the transcript and recomputes before
   raising (the stall detector's pull-before-raise shape). §5.
6. **Incident kind `HerdrStatusDisagreement = 34`. Warning always — no Error/Critical ladder.**
   Timeline-only (`raiseAlert:false`) for unbound agents; alert-raised (still Warning) when the
   agent is channel-bound. A corroboration hint never establishes "a human is being failed right
   now", so Critical is structurally out of reach for this kind. §6.
7. **Dedupe: the stall pattern, minus the severity step.** Newest kind-34 row on the session; not
   re-raised while `row.CreatedAt >= disagreement start`; a cleared-then-reformed disagreement is a
   new episode. DB-backed, restart-safe; the runner-side `AgentStatusSince` resets conservatively
   on runner restart (delays a Warning, never spams). §6.
8. **Unblock: every observed exit from `blocked` (to `done`/`idle`/`working`/`unknown`) fires
   `FlushIfIdleAsync` — the narrow deliver-if-idle flush, nothing else.** Safe by composition:
   the flush re-checks `IsWorkingAsync` (transcript) AND re-reads the S3 blocked gate before
   typing, so a spurious unblock event cannot type into a modal and herdr never overrides
   "working" toward "idle enough to type". §7.
9. **`pane.report_agent`: confirmed OUT — split to S4b**, with sharpened reasoning: the push adds
   zero safety, risks displaying a WRONG Antiphon state in the surface the operator trusts most
   (our working rule is transcript-derived and can be mid-catch-up), and would make the data flow
   bidirectional in the same PR whose central pin is one-directionality. §8.
10. **UI: none in S4.** The incident timeline (existing) is the surface; a status badge ships with
    S4b alongside `report_agent`. §8.
11. **Enabled gate: pump registered always, inert unless `SessionRunner:Herdr:Enabled`; holds a
    pipe only while ≥ 1 live herdr session exists.** Server sweep self-gates on "any Running
    session with `SessionBackend == Herdr`" (one cheap query). Herdr unreachable with sessions
    tracked → log + bounded backoff + reconnect forever (the Telegram-ingress rule); unreachable is
    never a death verdict (S2 §6A). §9.
12. **Tests: extend `FakeHerdrServer` with scripted subscription streams INCLUDING the replay
    buffer; `HerdrEventPumpTests` pin the no-false-kill and lifecycle behavior; server suites pin
    the matrix, hysteresis, dedupe, pull-before-raise, and — the card's headline pin — that a
    disagreement (and a status-event storm) triggers NOTHING beyond the incident row, mirroring
    how S3 pinned "no `agent.prompt` ever".** §10.

---

## Live measurements (this pass, 2026-08-24)

All through the raw named pipe (`%APPDATA%\herdr\herdr.sock`, NDJSON, `HerdrClient` framing)
against the operator's live herdr 0.8.2, plus the live schema dump. Probe furniture (one
workspace, three panes) was created and destroyed by the probe itself; herdr auto-removed the
emptied workspace (consistent with S2's P3 tab behavior).

| # | Measurement | Result |
|---|---|---|
| E1 | Multi-entry subscribe | ONE `events.subscribe` accepts an ARRAY of subscription entries (ack `subscription_started` with 4 entries: `pane.closed` + 3 × `pane.agent_status_changed`). One stream is enough. |
| E2 | Per-entry validation | `pane.agent_status_changed` requires `pane_id` (schema; optional `agent_status` value filter). A subscribe naming an unknown pane fails the WHOLE call with `pane_not_found` — and the error response's `id` is the request id **suffixed** (`<id>:sub:1:probe`), which `RequireResult`'s strict equality (`HerdrClient.cs:462`) would misreport as `HerdrProtocolException` instead of `HerdrApiException`. |
| E3 | Change-only semantics | No snapshot/initial status event on subscribe (30 s + 3 s listens, real panes, zero events). Baseline state must come from `pane.get`. |
| E4 | Stream survival | The stream stays OPEN after its per-pane subscribed pane closes (`pane_closed` delivered, then quiet; connection alive 8 s later). Per-pane entries for dead panes are inert, not fatal. |
| E5 | **Historical replay** | herdr re-delivers its buffer of past `pane_closed` events to EVERY new subscription — closes from hours earlier (this morning's investigation probe panes w2:p3/p4) arrived again on two consecutive fresh subscriptions, plus all three probe panes. **A `pane_closed` on a fresh stream is not evidence the pane closed just now — or that the close is news.** |
| E6 | Id allocation | Workspace ids are monotonic per herdr server lifetime (`w4` allocated after `w3` was closed, never reused); pane ids monotonic per workspace (`w3:p1..p3`). Within one herdr lifetime a replayed close cannot collide with a live pane's id; across a herdr restart ids DO restart from `w1` — one more reason events are triggers, not evidence. |
| E7 | Event vocabulary | The global event family (subscribe with dotted type-only entries; wire names underscored) includes `pane_closed`, `pane_exited`, and `pane_agent_detected {agent, released, final_status}` beside `pane_agent_status_changed {pane_id, workspace_id, agent_status, display_agent?, state_labels?, title?}`. `pane_closed` data carries only `pane_id` + `workspace_id`. |
| E8 | Capabilities | `ping` now reports `capabilities: {live_handoff: false, detached_server_daemon: true}` — ignored by our client (forward-compat contract), noted for the record. |

## 1. What exists on `6005831`, restated precisely (so the diff stays small)

The runner already: launches/adopts herdr sessions with a `HerdrPaneSidecar` (workspace/tab/pane
ids + `ChildPid`), registers Exited through the shared `Exited` → `SessionExited` SSE path
(`SessionRunnerRuntime.cs:659-674`), serves screen reads from `pane.read` and the single-session
GET refresh from `pane.get` (`RefreshHerdrSurfaceAsync` — revision → `LastSequence`, status →
`RunnerSessionDto.AgentStatus`), and has a liveness backstop: `MarkVanishedIfDead`
(`SessionRunnerRuntime.cs:1390`) probes `_childPid`, which IS set for herdr sessions, so a dead
claude is already caught at sweep latency with reason `ProcessVanished` — and it is idempotent
against a racing exit ("a real exit event won the race — keep its verdict"). The server already:
consumes the runner SSE stream in `SessionRunnerEventPump` (unknown event names skipped), defers
delivery on the literal `"blocked"` (S3), computes working truth in
`SessionMessageQueueService.IsWorkingAsync` (`:2192`), flushes narrowly via `FlushIfIdleAsync`
(`:710`), and records incidents via `RecordIncidentAsync` (`AgentSupervisorService.cs:275`).

**S4 is therefore NOT a new authority.** It is: (a) the long-lived subscription pump with
verified event handling, (b) one additive SSE event carrying status changes to the server,
(c) a blocked-exit promptness nudge into an existing narrow flush, (d) a Warning-only
corroboration sweep, and (e) pins. Anything in the build that finds itself writing a kill,
a retype, a delivery verdict, or a working-state override from herdr data is off the map and
must stop.

## 2. Decision 1 — the pump lives in the runner; status crosses on the existing SSE surface

**Runner-owned**, settled by three facts confirmed in code: the server has NO herdr transport
(S2 decision 1 explicitly refused a server-side `HerdrClient`; nothing on `6005831` changed that);
the pane↔session map lives in the runner (`HerdrPaneSidecar`, `RunnerSession._herdrChild`); and
the pump's §6B duties (Exited registration, adoption-bar re-verification) are runner operations.
A server-owned pump would re-litigate S2's transport decision for zero gain.

**New runner component `HerdrEventPumpService`** (`BackgroundService`, registered always):

- Holds the one subscription stream (§4), consumes events, and maps `pane_id` → live session via
  `SessionRunnerRuntime.LiveHerdrPanes()` (new internal: sessions with a `HerdrPaneChild`, their
  pane ids). Events for untracked panes (operator's own furniture, replayed history for gone
  sessions) are dropped at Debug.
- On a status change for a tracked pane: updates that `RunnerSession`'s `_herdrAgentStatus` (and a
  new `_herdrAgentStatusSinceUtc`, set only when the value CHANGES — the S3 GET refresh updates
  both the same way, silently), then publishes the new SSE event.
- On `pane_closed`/`pane_exited` for a tracked pane: verification, §3.

**New wire event** (additive, `SessionRunnerContracts.cs`):

```csharp
public sealed record RunnerAgentStatusEvent(
    Guid SessionId,
    // herdr agent_status verbatim (open vocabulary — consumers may only equality-match).
    string AgentStatus,
    // The value this one replaced, null for the first observation. Lets the server detect
    // blocked-exit without holding its own per-session history.
    string? PreviousAgentStatus,
    DateTime ObservedAtUtc);
// RunnerEventNames gains: public const string SessionAgentStatus = "SessionAgentStatus";
```

Old-server compatibility is by construction: `ParseEvent` returns null for unknown event names
(`SessionRunnerHttpClient.cs:466`) — verified, and pinned by a test in §10. The server pump gains
one arm mapping it to `AgentSessionRuntime.ObserveAgentStatusAsync` (§7).

**Rejected:** a server-side subscription (new transport dependency, second process on the pipe);
runner-side incident writing (the runner has no DB and no `IsWorkingAsync` — the disagreement is
a SERVER judgment by definition); a new dedicated HTTP push channel (the SSE surface exists,
reconnects, and backfills exactly this shape already).

## 3. Decision 2 — `pane.closed` verified, never trusted; full §6B lands here

Measured E5 is the decision-maker: **historical `pane_closed` events replay to every new
subscriber**, and every pump reconnect therefore begins with a batch of stale closes. The 2026
scar tissue this repo is built on (CARD-0056's founding false positive, CARD-0102's "unclaimed
never implies kill") says exactly what to do with an untrustworthy death signal: verify.

- **On `pane_closed` or `pane_exited` naming a tracked pane** (both are just "look now" triggers;
  `pane_exited` — pane alive, process gone — is included because it is free on the same path):
  run `RunnerSession.VerifyHerdrLivenessAsync` — the §6A evidence bar transposed to runtime:
  `pane.get(PaneId)` answers AND `pane.process_info` lists the sidecar's `ChildPid` → alive, do
  nothing (this is what neutralizes every replayed/stale event). Pane unknown, or `ChildPid`
  absent → register Exited(`HerdrPaneClosed`) through the EXISTING `Exited` handler (idempotent:
  `if (_status == "Exited") return`), delete the sidecar (`HerdrPaneSidecar.TryDelete`). Sidecar
  `ChildPid == null` (process_info failed at launch): pane existence alone decides, stated
  honestly as the weaker bar it is.
  - `HerdrRestartPresumedDead` stays adoption-time-only (§6A); the pump's reason is always
    `HerdrPaneClosed` — the reason strings tell an operator WHICH detector fired.
- **Herdr unreachable during verification** → no verdict; the session stays as-is and the
  reconnect sweep retries. Unreachable is never evidence of death (S2 §6A rule, unchanged).
- **On every (re)connect** of the stream: re-run the same verification over ALL live herdr
  sessions — §6B's reconnect sweep verbatim. The disconnect window is exactly when a herdr
  restart happens; the sweep is what converts "my panes' pids are gone" into prompt Exited
  registrations. It also doubles as the status baseline (§4).
- **Redundancy stated, not fudged:** `MarkVanishedIfDead` already catches a dead claude at
  liveness-sweep latency; the pump adds promptness and the herdr-specific reason, and both paths
  are idempotent against each other by the existing status-gate. The alternative from the
  investigation ("forward closed to the server, keep existing latency") is rejected: it would put
  an unverified, replay-prone signal on the wire for the server to mis-trust later, and saves
  almost nothing — the verification is two cheap calls (2–5 ms each, S3 M5).

## 4. Decisions 3 & 4 — one recycled stream; `HerdrSubscription` + the ack-id client fix

**One stream, recycled.** Entries at (re)subscribe time: `pane.closed`, `pane.exited` (type-only —
the schema has no pane_id filter for them; the pump filters to tracked panes on receipt) plus one
`pane.agent_status_changed {pane_id}` per live herdr session. The runtime signals the pump on any
herdr launch, adoption, or exit (`NotifyPaneSetChanged`); the pump cancels the current stream,
recomputes the set, resubscribes. Events missed in the recycle gap are covered by the
post-subscribe baseline sweep: for every tracked session, one `pane.get` (status + since
re-baseline, change-only semantics E3) and the §3 verification. So the gap is harmless by
construction, which is what makes the simple recycle correct.

- **`pane_not_found` race** (E2): a pane can close between snapshotting the set and subscribing,
  failing the whole call. The pump treats `HerdrApiException("pane_not_found")` on subscribe as:
  route that session through §3 verification, refresh the set, retry. Bounded — each retry has
  strictly fewer panes or a newly-launched set.
- **Zero sessions → zero pipe.** Not holding a permanent subscription against an operator app
  that restarts freely avoids both a pointless reconnect loop and consuming E5's replay buffer
  for nothing.
- **Rejected:** per-session streams (N pipes plus still needing a global close stream — two
  lifecycle codepaths for one job); a permanent global stream with per-pane polling for status
  (loses the promptness this slice exists to add); subscribing `pane.agent_detected` (its
  `released`/`final_status` are interesting but redundant against §3's verification).

**`HerdrSubscription`** (`HerdrClient.cs:496`) gains `[JsonPropertyName("pane_id")]
[JsonIgnore(Condition = WhenWritingNull)] string? PaneId = null`. A new `HerdrEventTypes` constants
class pairs each subscribe TYPE (dotted: `pane.closed`, `pane.exited`,
`pane.agent_status_changed`) with its WIRE event name (underscored: `pane_closed`, `pane_exited`,
`pane_agent_status_changed`) — both measured — so no consumer ever string-matches ad hoc.

**The ack-id fix** (E2): `SubscribeEventsAsync` currently funnels the ack through `RequireResult`,
whose strict id equality throws `HerdrProtocolException("mismatched request id")` on the suffixed
error id — misclassifying a routine, retryable validation failure as a protocol breach. Fix
narrowly, in the subscribe path only: accept a response id that EQUALS OR PREFIX-MATCHES
`"{requestId}:"` before surfacing result/error as today. Normal requests keep strict equality.

**Typed event payload** (additive, `HerdrApiModels.cs`): `HerdrPaneStatusEventData(PaneId,
WorkspaceId, AgentStatus)` and `HerdrPaneClosedEventData(PaneId, WorkspaceId)`, deserialized from
`HerdrEvent.Data`; unknown fields ignored (forward-compat contract, unchanged).

## 5. Decision 5 — the disagreement matrix and its hysteresis

Antiphon's side of every comparison is `SessionMessageQueueService.IsWorkingAsync` — the
transcript-derived rule and nothing else. Herdr's side is the latest observed `agent_status` with
its `AgentStatusSinceUtc`, read at sweep time from the runner's single-session GET (which S3
already made refresh live via `pane.get` — the sweep is therefore correct even if the pump is
down, degraded only in `since` granularity).

| herdr status | `IsWorkingAsync` | Verdict | Why |
|---|---|---|---|
| `working` | `true` | agree | — |
| `blocked` | `true` | expected | a permission modal is mid-turn; S3's defer already handles delivery. Not a disagreement. |
| `working` | `false` | **RAISE (A)** | screen-active-but-transcript-idle: the bind-failure / lost-tailer / stranded class. Quiet-is-not-done corroboration in the direction our probes are blind. |
| `blocked` | `false` | **RAISE (A)** | a modal parked outside any turn — the CARD-0047 trust-dialog shape, invisible to every other signal we have. |
| `idle` / `done` | `true` | **RAISE (B)** | transcript-working-but-screen-done: the CARD-0041/0055 stranded-working class (dead mid-turn, missed turn end). |
| `idle` / `done` | `false` | agree | — |
| `unknown` | any | never | absence of evidence. herdr has not detected an agent; saying nothing is the only honest reading. |
| unobserved / GET fails / pty session | any | never | no herdr side exists. |

**Hysteresis — the over-fire guard the investigation demanded:** raise only when the herdr status
has been STABLE in the disagreeing value for ≥ `MinSustainedMinutes` (default **10**;
`AgentStatusSinceUtc`). This kills the two flap shapes for free: `working → done` around every
turn end races transcript ingestion by seconds, and both flips reset `since`. A session actually
taking turns can never accumulate 10 stable minutes of disagreement. The window is deliberately
generous — this kind exists to catch conditions that persist for hours (the specialist died on
seven consecutive launches over a day in CARD-0047), not to win a race.

**Pull-before-raise** (the stall detector's shape, `AgentTaskDispatcher.cs:1088-1107`): before
raising, `CatchUpTranscriptAsync` (the no-side-effects pull) and recompute `IsWorkingAsync` — a
stale transcript stream is the single most likely false disagreement, and CARD-0055 taught that
the store lags reality by up to 45 s. Still disagreeing after the pull → raise.

**The direction rule, stated as an invariant:** no cell of this matrix — including "agree" — ever
feeds a kill, a retype, an Enter, a delivery verdict, a park, a working-state computation, or a
session status change. The ONLY two effects in all of S4 are: a Warning incident row (§6), and
the blocked-exit `FlushIfIdleAsync` nudge (§7), which itself re-derives everything from the
transcript before acting. Herdr never forces "idle enough to type" over `IsWorkingAsync == true`.

**Sweep locus:** a new `HerdrStatusCorroborationService.SweepAsync`, driven from
`AgentSupervisorHostedService` on its own period (default 60 s) beside the CARD-0067/0082 sweeps —
the precedent for "a condition no turn will ever report on needs a clock nobody's turn owns". Per
sweep: query Running sessions with `SessionBackend == Herdr` (self-gating, §9); for each, one
runner GET + one `IsWorkingAsync`; evaluate; dedupe (§6); raise via `RecordIncidentAsync`.
Sessions with no owning agent are skipped (an incident row needs an `AgentId` — the stall
detector's same guard) with a Debug log, and unclaimed herdr sessions remain covered by the
existing reconciliation surfaces.

## 6. Decisions 6 & 7 — kind 34, Warning-only, episode dedupe

**`AgentIncidentKind.HerdrStatusDisagreement = 34`** (append-only int; next free confirmed on
`6005831`). The XML doc carries the hard constraint verbatim: herdr's detection is the same
screen-heuristic class as our own probes; disagreement is corroboration for a human; this kind
never kills, retypes, escalates, or corrects — and cites E5 as the measured reason events are
never trusted directly.

**Severity: Warning, always.** No timed Error step (deliberately NOT stall parity, and the
difference is principled: a stall measures a TASK going nowhere — its cost grows with time; a
status disagreement measures two heuristics differing — its cost is constant, and every real
underlying failure has its own, better detector: stall (32), bind-stuck (27), delivery verdicts,
`ChannelReplyLost` (21)). No Critical anywhere in this kind: Critical is reserved for "a human is
being failed right now", which a corroboration hint cannot establish alone.

**Alerting:** `RecordIncidentAsync(..., raiseAlert: false)` — timeline-only — for unbound agents
(the investigation's recommendation, confirmed: this is context for a human already looking, not
a page). **Channel-bound agents flip `raiseAlert: true`** (severity still Warning): a
channel-bound agent in sustained disagreement is the "human waiting on a dead line" precursor
shape (CARD-0055/0067's severity rule applied at the hint stage), and the alert dedupe key
(`supervisor:{kind}:{agentId}`) plus episode dedupe keep the volume at one per episode.

**Episode dedupe (decision 7)** — the stall pattern minus the severity step: read the newest
kind-34 incident for the session; withhold when `latest.CreatedAt >= disagreementStart` (the
herdr `AgentStatusSinceUtc`, i.e. this same episode is already on record). A sweep that observes
agreement clears nothing in the DB (rows are append-only audit); the next disagreement has a
fresh, later `since`, outdates the old row, and raises as a new episode. Restart-safe with no
in-memory state on the server (CARD-0153's requirement); the runner's `since` resetting to
adoption time on a runner restart only DELAYS a raise (conservative direction, stated). The
incident `Message` names both sides and the evidence trail: herdr status + since + pane id,
`IsWorkingAsync` verdict + the newest boundary row's sequence, and the sentence "corroboration
only — no automatic action was or will be taken." `FailureReason` carries a machine-greppable
`herdr-status:{status}`.

## 7. Decision 8 — blocked-exit unblock, safe by composition

S3 left one promptness gap on purpose: a UI-origin WhenIdle message enqueued while `blocked`
sits Pending until the next turn-end flush or watchdog. The pump closes it:

- Server `SessionRunnerEventPump` gains the `SessionAgentStatus` arm →
  `AgentSessionRuntime.ObserveAgentStatusAsync(evt)`: when `PreviousAgentStatus == "blocked"` and
  the new status is anything else (`done`, `idle`, `working`, `unknown` — ALL exits), call
  `SessionMessageQueueService.FlushIfIdleAsync(sessionId)` via the scoped-queue shape that
  `FlushQueueAfterManualCompactionAsync` already uses (`AgentSessionRuntime.cs:244-256`). Nothing
  else: never the turn-end path, no "Agent finished", no settlement (CARD-0041's narrow-flush
  rule, unchanged).
- **Why all exits, not just `done`/`idle`:** restricting by value would re-encode trust in herdr's
  vocabulary that this design forswears. The flush is self-guarding: `FlushIfIdleAsync` re-checks
  `IsWorkingAsync` (transcript) and its delivery re-reads live metadata through the S3 blocked
  gate — so a nudge on `blocked → working` delivers nothing, and a SPURIOUS unblock event cannot
  type into a still-open modal (the gate re-reads `pane.get` at flush time). The event is a hint
  about WHEN to look; every decision about WHETHER to type stays where CARD-0055/S3 put it.
- Blocked-ENTRY does nothing (S3's poll gate already defers at flush time), and the confirm-loop
  Enter withhold stays poll-based and untouched.
- This is also why the pump forwards EVERY status change to the server rather than only
  blocked-exits: the server keeps no history (the event carries `PreviousAgentStatus`), the
  volume is trivial (herdr sessions are few; status changes a handful per turn), and a future UI
  badge (S4b) gets its feed without a wire change.

## 8. Decisions 9 & 10 — `report_agent` and UI both confirmed out, as S4b

The investigation recommended splitting `pane.report_agent`; confirmed, with the reasoning made
load-bearing: (1) the safety obligation — an Antiphon-side incident on disagreement — needs no
push; (2) pushing our transcript-derived working state into herdr's sidebar can display a WRONG
state in the surface the operator trusts most whenever our rule is mid-catch-up (the exact lag
CARD-0055 measured at 45 s), and doing it honestly needs the `seq` ordering care the schema
gestures at — real surface, zero safety return; (3) S4's central invariant is that state flows
one way, herdr → Antiphon, as corroboration; shipping the reverse direction in the same PR that
pins the invariant muddies the one thing this card must keep sharp; (4) the operator-facing gap
is small — herdr already shows its own detection plus our pane titles, metadata tokens, and
`pane.report_agent_session` binding from S2.

**UI likewise:** no new client surface in S4. The incident timeline already renders new kinds by
name, which is the corroboration surface this card owes. A live status badge on session/agent
cards belongs with `report_agent` in **S4b** (one card: "herdr status presentation, both
directions"), where the client-side cost (CLAUDE.md's client-suite discipline) is paid once. The
`SessionAgentStatus` SSE event and `AgentSessionLiveMetadata.AgentStatus` (S3) mean S4b needs no
further server wire changes.

## 9. Decision 11 — the enabled gate and the empty case

- **Runner:** `HerdrEventPumpService` is registered unconditionally, checks
  `HerdrSettings.Enabled` once at start — disabled → log Information, exit (the S2 `EnsureEnabled`
  throw never fires because the pump never touches the client). Enabled → it waits (no pipe) until
  `LiveHerdrPanes()` is non-empty, opens/recycles the stream per §4, and closes it when the count
  returns to zero. Reconnect on stream failure: OCE rethrown only `when (ct.IsCancellationRequested)`;
  anything else logs Warning + bounded backoff (`EventsReconnectMinSeconds` = 1 doubling to
  `EventsReconnectMaxSeconds` = 30, new `HerdrSettings` knobs) + reconnect forever while sessions
  remain — the CLAUDE.md Telegram-ingress rule, verbatim. Every successful reconnect runs the §3
  sweep, so an outage window can strand nothing.
- **Server:** no configuration coupling to the runner's flag. The sweep's first query (Running ∧
  `SessionBackend == Herdr`) returns empty on a pty-only deployment and the sweep is a no-op; the
  SSE arm simply never receives the event. One optional master switch
  (`SupervisionSettings.HerdrCorroboration.Enabled`, default true) for operator control, matching
  the sibling sweeps' shape.
- A runner that is herdr-enabled but whose herdr is down: launches already fail loudly (S2);
  the pump's reconnect loop is the only S4 behavior, and the §3 rule "unreachable is no verdict"
  means sessions tracked through an outage are re-verified, never presumed dead.

## 10. Decision 12 — verification / test design

Runner tests in `Antiphon.SessionRunner.Tests` (TUnit; process-spawn rules don't apply — fake
herdr is in-process named pipes); server tests in `Antiphon.Tests` (shared-Postgres rules: every
assertion scoped to rows the test made).

- **`FakeHerdrServer` extensions:** long-lived `events.subscribe` connections with (a) a
  scriptable event queue, (b) a configurable REPLAY BUFFER delivered to every new subscription
  (pinning E5's shape into the fake so no future test can forget it), (c) `pane_not_found`
  rejection with the SUFFIXED error id (E2's exact wire shape), (d) per-connection subscription
  recording so tests assert what was subscribed.
- **`HerdrClientTests` additions:** `HerdrSubscription` serializes `pane_id` (and omits it when
  null); the subscribe ack accepts the suffixed error id and surfaces
  `HerdrApiException("pane_not_found")` — not `HerdrProtocolException`; normal requests keep
  strict id matching.
- **`HerdrEventPumpTests` (runner, fake herdr) — the core suite:**
  1. One stream carries `pane.closed` + `pane.exited` + one status entry per live herdr session;
     a launch/exit recycles the stream with the updated set.
  2. **A replayed stale `pane_closed` naming a LIVE tracked pane changes nothing** — verification
     finds the child, session stays Running, no `SessionExited` published, sidecar intact. (The
     no-false-kill pin; red against a pump that trusts events.)
  3. A `pane_closed` whose verification fails (pane unknown / `ChildPid` gone) → exactly one
     `SessionExited(HerdrPaneClosed)`, sidecar deleted; a second event is a no-op (idempotence
     against `MarkVanishedIfDead` and repeats).
  4. Reconnect after a dropped stream re-subscribes, runs the baseline sweep, and converts a
     session whose pane died during the outage to Exited; herdr unreachable → no verdict, retry.
  5. `pane_not_found` on subscribe → set refreshed, retried, affected session verified — no crash,
     no unhandled exception.
  6. A status change on a tracked pane publishes `SessionAgentStatus` with the correct
     `PreviousAgentStatus` and updates the DTO's `AgentStatus`/`AgentStatusSinceUtc` (since moves
     only on value change); untracked panes' events are dropped.
  7. Disabled → zero pipe connections; enabled with zero herdr sessions → zero pipe connections;
     first launch opens, last exit closes.
- **Server pump/runtime tests (`SessionRunnerEventPump`/`AgentSessionRuntime`):** the
  `SessionAgentStatus` arm calls `ObserveAgentStatusAsync`; `blocked → done` fires
  `FlushIfIdleAsync`; **`blocked → working` fires the flush and the flush delivers NOTHING when
  the transcript reads working** (composition pin); a status event for an unknown session is
  ignored; an unknown SSE event name still parses to null (the old-server additive pin, now
  test-visible).
- **`HerdrStatusDisagreementTests` (server) — matrix, hysteresis, dedupe, and the headline pin:**
  1. Each RAISE cell raises Warning with the documented message fields; each agree/`unknown`/
     unobserved cell raises nothing.
  2. Hysteresis: disagreeing status with `since` younger than the window → nothing; crossing the
     window → one row.
  3. Pull-before-raise: a catch-up that flips `IsWorkingAsync` withholds the incident.
  4. Episode dedupe: a standing disagreement over many sweeps → one row; agreement then a new
     disagreement (later `since`) → a second row; channel-bound → `raiseAlert:true` (assert via
     the alert row), unbound → timeline-only.
  5. **The never-act pin (the card's mirror of S3's no-`agent.prompt` pin):** drive a full
     disagreement episode AND a status-event storm through a recording fake runner client and the
     real queue against a live-ish session fixture; assert ZERO `KillAsync`, ZERO input/Enter
     writes, ZERO queued-message state transitions, ZERO session status changes, and exactly the
     incident rows — the only observable effects in the whole slice are the row and (on
     blocked-exit, transcript-idle) a delivery that the EXISTING CARD-0055 machinery owns.
     Backed by a structural pin in the S3 style: `HerdrStatusCorroborationService`'s constructor
     dependency list is asserted by reflection to contain no delivery, control, or runner-client
     service — raising an incident is the only capability it is even WIRED for.
- **Headed canary (`[Explicit]`, extends the S3 M4 shape):** real herdr + real Claude on a
  tool-permission modal — one `pane_agent_status_changed` (`working → blocked`, Esc → `done`)
  captured END-TO-END through the production pump into a `SessionAgentStatus` event, pinning the
  wire name, payload mapping, and the blocked-exit flush trigger against the real thing.

## 11. Out of scope

`pane.report_agent` and any Antiphon→herdr state push (S4b); UI status badges (S4b);
`pane.output_matched`/`pane.scroll_changed` subscriptions; `pane.agent_detected` consumption;
`events.wait`; any change to delivery verdicts, ceilings, `PtyBackend`/`PtyBackendPolicy`, or
S3's blocked gate; any kill/restart/escalation driven by herdr data (forbidden, pinned); herdr
worktree API/plugins/`--remote`; changes to adoption's evidence bar (§6A is reused, not edited).

## 12. Build order

1. **B1 — client + fake (runner, dark):** `HerdrSubscription.PaneId`, `HerdrEventTypes`, the
   subscribe ack-id fix, typed event payloads; `FakeHerdrServer` subscription support incl.
   replay buffer; `HerdrClientTests` additions. No behavior change anywhere.
2. **B2 — the pump (runner):** `HerdrEventPumpService`, `LiveHerdrPanes()`/`NotifyPaneSetChanged`,
   `VerifyHerdrLivenessAsync` (sharing §6A's bar), status cache + `AgentStatusSinceUtc` on
   `RunnerSessionDto` (additive), `RunnerAgentStatusEvent` publishing. `HerdrEventPumpTests`.
   Ships dark for pty deployments; live behind `Herdr:Enabled`.
3. **B3 — server wire-through:** `SessionAgentStatus` parse arm, `ObserveAgentStatusAsync`,
   blocked-exit `FlushIfIdleAsync`. Server pump/runtime tests incl. the composition pin.
4. **B4 — corroboration sweep (server):** `AgentIncidentKind.HerdrStatusDisagreement = 34`,
   `HerdrStatusCorroborationService` + `SupervisionSettings.HerdrCorroboration`
   (`Enabled`, `SweepPeriodSeconds` = 60, `MinSustainedMinutes` = 10), hosted-service wiring.
   `HerdrStatusDisagreementTests` incl. the never-act pin.
5. **B5 — live smoke + docs:** headed canary against real herdr/Claude; CLAUDE.md gotcha line
   under the CARD-0161 entry (S4: events are triggers never evidence — herdr REPLAYS historical
   `pane_closed` to every subscriber; disagreement = Warning row only; blocked-exit may only
   nudge `FlushIfIdleAsync`); create the S4b follow-up card; close CARD-0162 with the measured
   results.

Slices are independently shippable; nothing observable changes for pty sessions at any point,
and nothing observable changes for herdr sessions until B2 is deployed with the flag on.
