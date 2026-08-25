# CARD-0163 — herdr S4b: Antiphon → herdr status push, and live herdr-status badges — plan

**Date:** 2026-08-26 · **Card:** CARD-0163 (`3c305b27-07c7-4100-984b-63ecb3a55a60`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `feat/card-task-fa71cb77` @ `0d126bc` (= master after CARD-0162 S4, CARD-0186,
CARD-0187, CARD-0205). Every file:line below was re-read out of the code on that commit. Every herdr
behavior marked **measured** was measured LIVE this pass — herdr 0.8.2, protocol 20, this machine,
2026-08-26, through the same named-pipe NDJSON framing `HerdrClient` uses, on a throwaway
workspace/pane the probe created and closed. Raw transcript: `.antiphon/card-0163-report-agent-probe.md`
(gitignored); the load-bearing results are inlined in § Live measurements.

**Established facts, not re-derived here:**
- CARD-0162 §8 (`docs/superpowers/plans/2026-08-24-card-0162-herdr-s4-state-mirror-plan.md`) — why
  `pane.report_agent` and the badges were split out: zero safety return, a WRONG Antiphon state in
  the operator's most-trusted surface whenever our working rule is mid-catch-up (CARD-0055's 45 s),
  and one-directionality as S4's central pin. S4's invariant is unchanged by this card: herdr data
  never feeds a kill, retype, Enter, delivery verdict, park, or working-state computation.
- What S3/S4 already wired for the badge: `RunnerSessionDto.AgentStatus` / `AgentStatusSinceUtc`
  (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:92-95`), the runner's cache
  `RunnerSession._herdrAgentStatus` (`src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:794-795`)
  updated by the pump (`ApplyHerdrAgentStatus`, `:876-900`) and by the single-session GET refresh
  (`RefreshHerdrSurfaceAsync`, `:850-873`), the SSE event `SessionAgentStatus` (`:895-896`), the
  server parse arm (`server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs:87`) →
  `AgentSessionRuntime.ObserveAgentStatusAsync` (`server/Application/Services/AgentSessionRuntime.cs:309-323`),
  `AgentSessionLiveMetadata.AgentStatus` (`:1282-1288`, filled at `:775-799` from the runner GET),
  and the sweep that grades disagreement (`server/Application/Services/HerdrStatusCorroborationService.cs`,
  matrix at `:175-181`).
- The runner already holds a THIRD lockstep working/idle implementation over the tailer's own
  file-ordered mirror: `TranscriptWorkingState.IsProvenIdle`
  (`src/Antiphon.SessionRunner/TranscriptWorkingState.cs:17-60`), consumed by the CPU watchdog
  (`SessionCpuWatchdogService.cs:107`). Pinned by `tests/Antiphon.SessionRunner.Tests/TranscriptWorkingStateTests.cs`.
- Client badge idiom: `AgentActivityBadge.tsx` (reads `agent.working`, never `agent.status`),
  `SessionWorkingBadge.tsx` (the server's `IsWorkingAsync` read, shown next to the terminal
  precisely so a human can cross-check it), `SessionContextBadge.tsx` (per-session live field
  overlaid on `AgentSessionSummaryDto`). Overlay precedent for a runner-sourced per-session field:
  `AgentService.AttachTranscriptBindingAsync` (`server/Application/Services/AgentService.cs:242-274`)
  → `AgentSessionSummaryDto.TranscriptBinding` (`server/Application/Dtos/BoardDtos.cs:99-101`) →
  `client/src/api/boards.ts:171-175`.

**Related:** CARD-0162 (S4), CARD-0161 (S3 blocked gate), CARD-0160 (S2 metadata/binding calls),
CARD-0055 (transcript lag), CARD-0180/0190 (`transcriptBinding` overlay — the badge's shape),
CARD-0047 (the blocked-and-invisible class the badge makes visible).

---

## Verdict up front — the ten decisions

1. **`pane.report_agent` is NOT the sidebar push. Measured: it is a lifecycle-authority
   takeover, not a display hint.** A report from `antiphon` becomes the pane's effective
   `agent_status` verbatim (R1/R4), relabels the pane's agent (R1), is last-writer-wins across
   sources (R5), survives `release_agent` (R7), and — the disqualifier — would sit ABOVE herdr's own
   screen detection, which is exactly the signal S3's `blocked` delivery gate and S4's
   `HerdrStatusDisagreement` read. Pushing our transcript verdict through it would (a) turn the
   corroboration sweep into a tautology (herdr status ≡ our status), and (b) let a stale
   `working`/`idle` push mask a live permission modal so a WhenIdle delivery types into it — the
   CARD-0047 shape, re-created by our own hand. §2.
2. **The push ships as display-only `pane.report_metadata`: `state_labels` + two tokens, with a
   TTL.** Measured R6: it leaves `agent_status` untouched and is rendered in the sidebar. The label
   for EVERY herdr state carries our verdict ("blocked · antiphon: working"), so the sidebar shows
   both sides at once instead of one side overwriting the other. This is the card's intent (our
   transcript-derived state visible in herdr) without the authority takeover. §3.
3. **The verdict is computed RUNNER-side, tri-state, from the tailer's file-ordered mirror.** New
   `TranscriptWorkingState.Classify(entries) → Working | Idle | Unknown` beside `IsProvenIdle`
   (which becomes `Classify == Idle`, pinned). The runner is the only process with a `HerdrClient`
   (S2 decision 1, unchanged), the pane id, and a transcript mirror that is NOT arrival-reordered —
   the 45 s CARD-0055 lag lives on the runner→server hop and never enters this path. §4.
4. **Safe return = three things, not one.** (i) `Unknown` whenever there is no positive evidence
   (no tailer, unbound, claim revoked, empty mirror, `HerdrPending`); (ii) every push carries
   `antiphon-as-of` = timestamp of the newest mirrored record, so the sidebar states its own age
   rather than implying "now"; (iii) `ttl_ms` on every report so a runner that dies or stops
   pushing leaves NO label behind — the runner's death is precisely when a confident stale label
   would otherwise stand for hours. Exit clears the labels explicitly. §5.
5. **Push on change only, coalesced, strictly increasing `seq`.** Measured R3: a stale `seq` is
   answered `ok` and silently ignored — so a per-session counter that restarts at 1 after a runner
   restart would have every push ignored forever with no error anywhere. `seq` = UTC unix ms. §6.
6. **B0 first: the S4 pump drops every real status event.** Measured R9: the live wire name is
   DOTTED `pane.agent_status_changed` (7/7 events); `HerdrEventTypes.PaneAgentStatusChangedWire`
   is the schema `type` `pane_agent_status_changed`, which no live event has ever carried (S4's E3
   saw zero status events, so the constant was never checked). Today the status arm only advances
   through GET refreshes. Fix: accept both spellings; make `FakeHerdrServer` emit the measured one.
   Independent, tiny, ships alone. §7.
7. **The metadata report itself fires a status event with the status unchanged (R6).**
   `ApplyHerdrAgentStatus` is idempotent on an equal value (`SessionRunnerRuntime.cs:880-884`) so
   `since` does not move and nothing is published — pinned so a future edit cannot turn our own
   pushes into a disagreement-hysteresis reset. §7.
8. **Badge: one new client component, one new DTO field pair, one overlay branch, one
   invalidation publish.** `AgentSessionSummaryDto.HerdrAgentStatus`/`HerdrAgentStatusSinceUtc`
   overlaid in `AgentService` from the runner `ListAsync` the binding overlay already makes (no new
   herdr calls, no extra runner round trip); `HerdrStatusBadge.tsx` rendered beside
   `AgentActivityBadge` on cards/detail and beside `SessionWorkingBadge` in the terminal modal;
   `ObserveAgentStatusAsync` publishes `AgentChanged` so the existing SignalR invalidation
   (`client/src/hooks/useSignalRInvalidation.ts:86-94`) keeps it live. Null for pty sessions ⇒
   renders nothing. §8.
9. **The badge shows disagreement as a HINT, never a verdict.** A client mirror of
   `HerdrStatusCorroborationService.IsDisagreement` (no hysteresis) sets `data-disagree` and an
   outline; the tooltip names both sides. The graded, hysteresis-backed row stays kind 34's job. §8.
10. **Tests pin the two things that must never happen:** zero `pane.report_agent` calls from
    production code (the fake THROWS on that method; a structural grep-pin over `src/`), and zero
    effect of any push on `agent_status`, delivery, or session state. §9.

---

## Live measurements (this pass, 2026-08-26)

| # | Measurement | Result |
|---|---|---|
| R1 | `pane.report_agent {source:antiphon, agent:claude, state:working, seq:1}` on a plain-shell pane | `ok`; `pane.get.agent_status = working`, `pane.get.agent = claude` (the report labels the pane's agent). Wire: `pane_agent_detected` then `pane.agent_status_changed {agent_status:working}`. |
| R2 | report `idle` | effective status **`done`** — herdr renders a reported idle as done ("idle and not yet seen"); S3's `done ≠ idle` holds for reported states too. |
| R3 | report with a STALE `seq` (1 after 2) | **`ok`, silently ignored**; state unchanged. |
| R4 | report `blocked` / `unknown` | verbatim. `agent` stays `claude`. |
| R5 | second source reports `working`; antiphon then `idle`; other source released | `working → done → done`: **last writer wins across sources, no precedence, release does not revert.** |
| R6 | `pane.report_metadata {state_labels:{idle:…,working:…}, tokens:{antiphon-state:idle}}` | `ok`; **`agent_status` unchanged**; `pane.get` returns `state_labels` + `tokens`; wire: `pane.agent_status_changed {agent_status:done, state_labels:{…}}` — **the metadata report fires the status event with the status unchanged.** |
| R7 | `release_agent {source:antiphon}` | status stays at the last reported value; `agent explain` reports the screen classifier's own opinion (`state:idle`, `fallback_reason:default_known_agent_idle_fallback`, `screen_detection_skipped:false`) — and reported the same `idle` while the effective status was the reported `working` (R1). The report is the effective status; the classifier keeps evaluating underneath it. |
| R8 | report `working` with `seq:1` after release | ignored — the seq watermark survives release, or a released source is ignored (indistinguishable; a strictly increasing seq sidesteps both). |
| R9 | **Wire name of the status event** | **`"event":"pane.agent_status_changed"` — dotted**, every time. `pane_closed` / `pane_agent_detected` are underscored. Status `data` carries no `type` field. |
| R10 | Replay | `pane_agent_detected` replays historically to every new subscriber alongside `pane_closed` (E5 generalizes; a `released:true, final_status:idle` shape was seen). |
| R11 | Schema vs CLI | `PaneAgentState` (report vocabulary) is `idle|working|blocked|unknown` — no `done`; `state_labels` keys must be one of the five effective states; `tokens` ≤ 16 per report / 32 per pane, names `^[A-Za-z0-9_-]{1,32}$`; `ttl_ms` 1..86 400 000. `herdr pane release-agent` exists ("Release pane agent lifecycle authority"). |
| R12 | Cleanup | `pane.close` on the last pane left the workspace standing; `workspace.close` removed it. |

**Not measured, and the design does not depend on it:** whether an `antiphon` report displaces
herdr's OWN screen-detected `blocked` on a pane whose manifest is actively matching. R1/R7 show the
report is the effective status while the classifier's opinion differs, which is the same mechanism,
and herdr's docs say it outright ("the integration is authoritative when it is installed and
actively reporting … It does not also run screen manifest fallback for that same lifecycle
authority"). Probe R-C (§10) settles it as evidence for the record; the metadata design is correct
either way.

## 1. What exists on `0d126bc`, restated precisely (so the diff stays small)

The runner: launches a herdr session through `HerdrPaneChild.LaunchAsync`
(`src/Antiphon.SessionRunner/HerdrPaneChild.cs:76-130`), which already calls
`PaneReportMetadataAsync` once at launch with the `antiphon-session` token and the pane title
(`:100-104`); keeps the pane id (`:51`) and sidecar (`:54`); refreshes status + revision via
`RefreshStatusAsync` (`:158-168`). `HerdrClient.PaneReportMetadataAsync`
(`src/Antiphon.SessionRunner/HerdrClient.cs:192-203`) sends `tokens` + `title` only —
`HerdrPaneReportMetadataParams` (`HerdrApiModels.cs:152-158`) has no `state_labels`,
`clear_state_labels`, or `applies_to_source`. `PaneReportAgentSessionAsync` (`HerdrClient.cs:205-219`)
exists with **zero production callers** (tests only). `HerdrPaneInfo` (`HerdrApiModels.cs:33-46`)
does not deserialize `state_labels`. The tailer appends each entry to its mirror and publishes
`SessionTranscript` on the hub (`TranscriptTailer.cs:340-351`); `Snapshot()` (`:186-190`) is the
mirror; `BoundTranscriptPath`/`BindHow`/`UnboundReason` (`:149-152`) say whether it is bound.
`SessionRunnerEventHub.Subscribe(ct)` (`SessionRunnerRuntime.cs:2166-2185`) hands any in-process
consumer the same channel the SSE endpoint reads. `RunnerSession.ToDto()` (`:1662-1687`) exposes
`AgentStatus`, `AgentStatusSinceUtc`, `Backend`, `Pending`, `TranscriptBound`.
`HerdrSettings` (`HerdrSettings.cs`, 39 lines) has `Enabled`, reconnect knobs, `LaunchDetectTimeoutMs`.

The server: `ObserveAgentStatusAsync` (`AgentSessionRuntime.cs:309-323`) nudges
`FlushIfIdleAsync` on blocked-exit and does nothing else — in particular it publishes NO client
event; `AgentService.LoadLiveSessionsAsync` (`AgentService.cs:186-236`) builds
`AgentSessionSummaryDto` with `ContextFullness` overlaid, then `AttachTranscriptBindingAsync`
(`:242-274`) overlays `TranscriptBinding` from one `_runnerClient.ListAsync` — that list carries
`AgentStatus`/`AgentStatusSinceUtc`/`Backend` already (`SessionRunnerDtos.cs:18-31`) and nobody reads
them there. `IsSessionWorkingAsync` (`:115-117`) is what `agent.working` means.

The client: `AgentSessionSummaryDto` (`client/src/api/boards.ts:149-176`) has no herdr field;
`AgentsPage.tsx` renders `AgentActivityBadge` at `:151` (card) and `:247` (detail header) with
`SessionContextBadge` beside it (`:144-150`, `:248-252`); `AgentCliModal.tsx:55` renders
`SessionWorkingBadge`; `AgentRail.tsx:50-62` composes the terminal-icon tooltip from
`transcriptBinding`.

## 2. Decision 1 — why `pane.report_agent` is rejected for the push

Three measured facts and one already-shipped dependency:

- **Authority, not annotation (R1, R4, R5, R7).** The reported state IS `pane.get.agent_status`.
  Nothing in the response, the event, or `agent.explain` distinguishes "reported by antiphon" from
  "detected on screen" for a consumer of `agent_status` — which is every consumer we have.
- **S3's blocked gate reads `agent_status`** (`SessionMessageQueueService.cs:840-856`, via
  `AgentSessionLiveMetadata.AgentStatus`) and defers delivery on the literal `blocked`. If our push
  has replaced `blocked` with `idle` — which it would the moment the transcript's last turn ended
  and the modal appeared AFTER that end, exactly the trust-dialog shape — the gate opens and the
  queue types into the modal. That is a regression of CARD-0047/CARD-0161, caused by us.
- **S4's corroboration sweep compares `agent_status` with `IsWorkingAsync`**
  (`HerdrStatusCorroborationService.cs:110-136`). With our verdict in `agent_status` the two sides
  are the same number read through two paths; kind 34 would never fire again on a pushed session,
  and the sweep would be silently blind rather than visibly off.
- **The vocabulary does not fit (R2, R11).** We can report `idle|working|blocked|unknown`; we can
  never truthfully say `blocked` (the transcript cannot see a modal), and a reported `idle`
  displays as `done` until the operator focuses the pane.

The rejection is structural, not a caution: any use of `report_agent` that carries our
working/idle verdict re-creates the two problems above regardless of guards, because the guard
would have to read the very signal the push overwrites. `pane.report_agent` therefore stays at
zero production callers, and that zero is pinned (§9). If a future card wants herdr to treat
Antiphon as the lifecycle authority (e.g. for Codex, where herdr's manifest is weakest), it needs
its own design in which the S3 gate reads a source that herdr does not let us overwrite —
`pane.agent_status_changed`'s own `agent` field, or a `state_labels`-free `agent.explain` — and
that is out of scope here.

## 3. Decision 2 — the display-only push: `state_labels` + tokens + TTL

`pane.report_metadata` (R6) is the sanctioned display surface: "Metadata reports are display-only.
Valid metadata can override the pane title, displayed agent name, visible state labels, and
arbitrary named tokens" (herdr docs, socket-api). One report per Antiphon verdict change:

```jsonc
{
  "pane_id": "<PaneId>", "source": "antiphon",
  "state_labels": {                                   // one entry per EFFECTIVE herdr state (R11)
    "idle":    "idle · antiphon: working",
    "working": "working · antiphon: working",
    "blocked": "blocked · antiphon: working",         // both sides visible at once — the operator
    "done":    "done · antiphon: working",            // sees the CARD-0047 shape as text
    "unknown": "unknown · antiphon: working"
  },
  "tokens": { "antiphon-state": "working", "antiphon-as-of": "2026-08-26T00:23:16Z" },
  "ttl_ms": 900000,                                   // §5 (iii)
  "seq": 1787631796253                                // §6
}
```

- The label keeps herdr's own word FIRST so the operator's existing reading of the sidebar is
  unchanged; our verdict is appended. `Unknown` renders as `"<state> · antiphon: unknown"` with the
  reason in `antiphon-state` (`unknown:unbound`, `unknown:no-transcript`, `unknown:pending-herdr`)
  — a stated absence, never a blank that looks like agreement.
- Exit (any `Exited`, including `HerdrPaneClosed`/`RestartPresumedDead`) sends one final report
  with `clear_state_labels: true` and the two tokens set to `null` (token null clears — docs), on
  `CancellationToken.None` with a short timeout, best-effort: the pane is usually already gone and
  `pane_not_found` is expected and logged at Debug.
- `title` is NOT touched (S2 owns it; CARD-0187 keeps the pane label as the operator's handle).
- Client/model changes needed: `HerdrPaneReportMetadataParams` gains `state_labels`
  (`IReadOnlyDictionary<string,string>?`), `clear_state_labels` (bool, default false, omit when
  false), `applies_to_source` (unused, omitted); `HerdrClient.PaneReportMetadataAsync` grows an
  overload taking the full params record (the S2 call site at `HerdrPaneChild.cs:100` is
  unchanged); `HerdrPaneInfo` gains `[JsonPropertyName("state_labels")] IReadOnlyDictionary<string,string>? StateLabels`
  so tests and the baseline sweep can read back what was pushed.

**Rejected:** `pane.report_agent` (§2); `pane.rename` label suffixing (the label is the operator's
handle — CARD-0187 K5 — and a rename is not TTL-guarded); title suffixing (same objection, weaker);
`display_agent` (it renames the agent, not the state).

## 4. Decision 3 — the verdict is runner-side, tri-state, over the file-ordered mirror

**New `TranscriptWorkingState.Classify(IReadOnlyList<RunnerTranscriptEvent>) → WorkingVerdict`**
(`Working | Idle | Unknown`) in `src/Antiphon.SessionRunner/TranscriptWorkingState.cs`, the same
loop as `IsProvenIdle` (`:26-60`) with its three outcomes made explicit:

| mirror state | `Classify` | today's `IsProvenIdle` |
|---|---|---|
| empty / no entry counts as activity or end | `Unknown` | false |
| activity, no end yet (first turn in flight) | `Working` | false |
| activity after the last end | `Working` | false |
| an end, nothing counting as activity after it | `Idle` | true |

`IsProvenIdle` is reimplemented as `Classify(entries) == Idle` — the CPU watchdog's semantics are
unchanged and `TranscriptWorkingStateTests` prove it. The kinds treated as ends and as housekeeping
are exactly the existing lists (`:32-35`, `:45-51`); the lockstep comment at `:5-15` gains
"S4b's herdr label reads `Classify`".

**Why the runner, not the server:** (1) S2 decision 1 — the server has no herdr transport, and a
server-side push would need a new runner endpoint + `ISessionRunnerClient` method + a server pump
arm for something the runner can do in-process; (2) the runner's mirror is in transcript-file
order — the arrival-order reordering that motivates the server's timestamp override (and the
45 s CARD-0055 store lag) is a property of the runner→server hop, so the runner's read is the
FRESHEST transcript-derived read that exists; (3) the runner already makes exactly this judgement
for the CPU watchdog, so this adds a consumer, not a fourth implementation. The remaining honest
divergence — the server/client carry a timestamp override, the runner does not — is the one the
lockstep comment already states, and it cannot make the label wrong, only (rarely) later.

**What can still lag, stated:** the JSONL itself can trail the composer (CARD-0055's 0.9 s write,
and Claude's own buffering under some exits). The label is therefore always presented as "as of
<record timestamp>" (§5), never as "now".

**New runner component `HerdrStatusPushService`** (`BackgroundService`, registered always,
inert unless `HerdrSettings.Enabled && StatusPush.Enabled`) in `src/Antiphon.SessionRunner/`:

- Subscribes to `SessionRunnerEventHub.Subscribe(stoppingToken)` and reacts to
  `SessionTranscript`, `SessionTranscriptBound`, `SessionExited`, `SessionStarted`/`SessionAdopted`
  (for herdr sessions only — `LiveHerdrPanes()` `SessionRunnerRuntime.cs:79-90` is the set); an
  event for a non-herdr session is dropped before any work.
- Per session it keeps `(lastVerdict, lastReason, lastAsOf, lastPushUtc)`; on each trigger it
  re-reads `runtime.GetTranscript(sessionId)` (`:255`, the tailer `Snapshot()`), classifies, and
  pushes only when the verdict or reason CHANGED, or when `HeartbeatSeconds` have elapsed (TTL
  renewal, §5). Coalescing: a turn writes many records in a burst — one timer per session,
  `DebounceMs` (default 500) after the last trigger.
- Adoption/restart: on `SessionAdopted` for a herdr session the first classification pushes
  unconditionally (the previous runner's label may be seconds from expiring, or already wrong).
- Herdr unreachable → log at Debug, keep state, retry on the next trigger/heartbeat. Never a
  verdict about the session (S2 §6A rule, unchanged).

**Rejected:** driving the push from the server's `IsWorkingAsync` (the lagging hop, a new wire
surface, and the S3 constructor-cycle shape to reach `SessionMessageQueueService` from a pump);
a per-tailer callback (`onEntry`) instead of the hub — the hub already fans out every entry and
needs no tailer signature change across the three tailers (`SessionRunnerRuntime.cs:1059/1076/1103`
and the adoption trio `:1579/1588/1609`).

## 5. Decision 4 — the safe return, in three parts

(i) **`Unknown` on absent evidence** — the reasons, each a `antiphon-state` token value so the
sidebar says which:

| condition | how the runner knows | token |
|---|---|---|
| no tailer on the session (Raw) | `GetTranscript()` returns the empty DTO (`:1746-1747`) | `unknown:no-transcript` |
| tailer present, not bound | `RunnerSessionDto.TranscriptBound == false` (`:1675-1677`) | `unknown:unbound` (or `unknown:awaiting-input` from `TranscriptUnboundReason`, CARD-0190) |
| bound, mirror empty | `Snapshot().Entries.Count == 0` | `unknown:empty` |
| claim revoked (CARD-0181) | `SessionTranscriptBound` with `Bound:false`, mirror dropped | `unknown:unbound` |
| session pending herdr (CARD-0186 S3) | `RunnerSessionDto.Pending != null` | no push at all — the pane is not reachable |

(ii) **Age on every push** — `antiphon-as-of` = the newest mirrored record's own `Timestamp`
(the transcript's clock, which survives reordering and is what CARD-0055 taught us to trust), ISO
seconds. A label that reads "working · antiphon: working" with an `as-of` fifteen minutes old is
self-evidently a stale read, and the operator can see it without a second surface.

(iii) **TTL** — `ttl_ms = StatusPush.TtlSeconds * 1000` (default 900 s) on every report, renewed by
the heartbeat (`HeartbeatSeconds`, default 300, so a live label is renewed three times per TTL).
A dead runner, a dead pump, or a herdr that stopped receiving leaves a label that expires by
itself. Probe R-T (§10) confirms `ttl_ms` clears `state_labels` on 0.8.2 — if it turns out to clear
only tokens, the labels are ALSO renewed by the heartbeat and the exit-clear, and the plan notes the
gap rather than hiding it.

**Why this is enough:** the card's failure mode is "a confidently wrong status in the surface the
operator trusts most". After this design the surface never shows a bare verdict: it shows herdr's
own word, then ours, then how old ours is; ours degrades to a stated `unknown` on any missing
evidence; and it cannot outlive the process that produced it.

## 6. Decision 5 — cadence, idempotence, `seq`

- **Change-only + heartbeat** (§4). Expected volume: 2–4 reports per turn (working at the first
  record, idle at the end; `unknown` transitions only around bind/unbind), plus one every
  5 minutes. herdr's per-report cost is the same 2–5 ms as any request (S3 M5).
- **`seq` = `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`** per report. Measured R3/R8: a
  `seq` at or below the last accepted one is answered `ok` and IGNORED — silently — and the
  watermark survives release. A counter that restarts on runner restart would put every push
  after the restart below the previous runner's watermark and nothing would ever say so. Wall-clock
  ms is strictly increasing across restarts and across the debounce window (two pushes in the same
  ms are coalesced by the debounce anyway). Pinned in the fake (§9).
- **Idempotence against the S4 pump (R6, decision 7):** every metadata report makes herdr emit
  `pane.agent_status_changed` with the CURRENT status and the new `state_labels`. The pump maps it
  through `ApplyHerdrAgentStatus`, whose equal-value early return (`SessionRunnerRuntime.cs:880-884`)
  means no `since` movement and no `SessionAgentStatus` publish — the corroboration hysteresis is
  untouched by our own pushes. This only holds once B0 (§7) lands; before it the pump ignores the
  event entirely, which is also harmless.

## 7. Decision 6 & 7 — B0: the pump's status arm has never received a live event

`HerdrEventPumpService.HandleEventAsync` (`src/Antiphon.SessionRunner/HerdrEventPumpService.cs:165`)
matches `HerdrEventTypes.PaneAgentStatusChangedWire` = `"pane_agent_status_changed"`
(`HerdrClient.cs:537-538`). Measured R9: herdr 0.8.2 emits `"event":"pane.agent_status_changed"`.
So every live status event falls through to `"Ignoring unhandled herdr event"` at Debug (`:178`),
and `AgentStatus`/`AgentStatusSinceUtc` move only through `RefreshHerdrSurfaceAsync` on
single-session GETs (`SessionRunnerRuntime.cs:850-873`) and the baseline sweep
(`HerdrEventPumpService.cs:181-203`). The S4 plan's E7 took the wire name from the schema's
`type` field; E3's "zero events in 30 s" meant the constant was never exercised.

**Fix (B0):** `HerdrEventTypes` gains `PaneAgentStatusChangedWireDotted = "pane.agent_status_changed"`
(measured) and keeps the underscored name (schema) — `HandleEventAsync` accepts either; the XML doc
records which was measured and when. `FakeHerdrServer.EnqueueEvent` callers in tests use the
dotted name; a new `HerdrClientSurfaceTests`/`HerdrEventPumpTests` case drives one dotted event
through the pump against a live `RunnerSession` (the seam `HerdrRunnerSessionTests` already
constructs) and asserts `ToDto().AgentStatus` changed and `SessionAgentStatus` was published — red
on `0d126bc`. `docs/herdr-sessions.md` §7 and the AGENTS.md CARD-0162 bullet get one clause each.
No behavior change for pty sessions; for herdr sessions the only change is that the S4 status arm
starts doing what it was built to do.

**Scope note:** S4's blocked-exit `FlushIfIdleAsync` nudge (`AgentSessionRuntime.cs:309-323`) has
therefore also never fired from a live event; it fires now. Its safety argument (re-checks
`IsWorkingAsync` and the S3 gate before typing) is unchanged and already pinned server-side.

## 8. Decisions 8 & 9 — the badge

**Server.** `AgentSessionSummaryDto` (`server/Application/Dtos/BoardDtos.cs:79-101`) gains two
additive fields after `TranscriptBinding`: `string? HerdrAgentStatus = null` (herdr's effective
status verbatim: `idle|working|blocked|done|unknown`; null = not a herdr session / runner did not
answer / older runner) and `DateTime? HerdrAgentStatusSinceUtc = null`. `AgentService.AttachTranscriptBindingAsync`
(`AgentService.cs:242-274`) is renamed `AttachRunnerLiveStateAsync` and, in the same loop over the
one `ListAsync` result, ALSO overlays the pair when `runner.Backend == SessionBackends.Herdr`
(`SessionRunnerDtos.cs:27`) — `Backend` null (older runner) ⇒ leave null, never guessed. No new
runner call: `List()` (`SessionRunnerRuntime.cs:232-233`) reports the cached status, which B0 keeps
live. `AgentSessionRuntime.ObserveAgentStatusAsync` (`:309`) additionally publishes
`AgentChanged { AgentId }` for the owning agent (looked up in a scope by
`Agents.PersistentSessionId == sessionId.ToString("D")`, the shape `HerdrStatusCorroborationService.cs:64-67`
uses) on EVERY status change — before the blocked-exit early return, which stays as it is.
Unclaimed session ⇒ no publish (nothing renders it). Volume: a handful per turn, the same order as
the transcript-driven `AgentChanged` publishes already made.

**Client.** `client/src/api/boards.ts:149-176` gains `herdrAgentStatus?: HerdrAgentStatus | null`
(`export type HerdrAgentStatus = 'idle' | 'working' | 'blocked' | 'done' | 'unknown'`) and
`herdrAgentStatusSinceUtc?: string | null`. New `client/src/features/agents/HerdrStatusBadge.tsx`:

- Props `{ session: AgentSessionSummaryDto; working: boolean; size? }`. Renders nothing when
  `session.herdrAgentStatus` is null/undefined (pty sessions, older servers) — the same
  absent-means-nothing rule as `SessionContextBadge`'s `Suppressed`.
- `Badge variant="light"` in the shared palette: `blocked` → orange (the `Review` tone — a human is
  needed at the pane), `working` → yellow with the dots `Loader` (matches `AgentActivityBadge`),
  `idle`/`done` → green, `unknown` → gray. Text `herdr · blocked` (the prefix is the point: this is
  herdr's screen read, not ours). `data-testid="herdr-status-badge"`, `data-status`, `data-disagree`.
- Disagreement hint: `isHerdrDisagreement(status, working)` in `transcriptModel.ts` mirrors
  `HerdrStatusCorroborationService.IsDisagreement` (`:175-181`) exactly — `working|blocked` × `!working`,
  `idle|done` × `working`, else false — with NO hysteresis (it is a hint the incident timeline
  grades; the badge must not pretend to be the sweep). When true: `data-disagree="true"`, an
  orange outline, and the tooltip reads "herdr sees blocked · transcript says idle — corroboration
  only; see the agent's incidents". Otherwise the tooltip reads "herdr's screen detection for the
  pane, since HH:MM — cross-check against Working (transcript)".
- Placement: `AgentsPage.tsx` after `AgentActivityBadge` at `:151` (card) and `:247` (detail
  header): `<HerdrStatusBadge session={agent.liveSession} working={agent.working} />` guarded by
  `agent.liveSession &&`; `AgentCliModal.tsx:55` after `SessionWorkingBadge` (this is the surface
  where the two reads sit side by side, which is the whole reason `SessionWorkingBadge` exists);
  `AgentRail.tsx:50-62` appends ` · herdr: blocked` to the terminal tooltip when
  `herdrAgentStatus === 'blocked'` only (the rail is deliberately minimal — CARD-0180 put one dot
  there, not a badge).
- Storybook: one story file beside `SessionTranscriptPanel.stories.tsx` showing the five states and
  the disagreeing pair; no new addon.

**Rejected:** a session-group SignalR event consumed by the modal directly (the modal reads
`liveSession` off the agent detail query; `AgentChanged` invalidation already covers it and adds
no second path); polling `getSessionQueue` for herdr status (that endpoint is the server's working
read — mixing herdr into it would blur the two badges the design keeps apart); folding herdr
status INTO `AgentActivityBadge` (that badge's contract is "one meaning everywhere" for
`agent.working`; a second signal belongs in a second badge).

## 9. Decision 10 — verification / test design

Runner tests (`Antiphon.SessionRunner.Tests`, TUnit, in-process fake herdr — no process-spawn
rules apply); server tests (`Antiphon.Tests`, shared-Postgres rules: assert only on rows the test
made); client via `pwsh -File scripts/test-client.ps1` (never a Bash pipeline's exit code).

- **`FakeHerdrServer` extensions** (`tests/Antiphon.SessionRunner.Tests/FakeHerdrServer.cs`):
  `ReportPaneMetadata` (`:423-436`) records `state_labels` on `PaneState`, honors
  `clear_state_labels`, treats a `null` token as delete, keeps a per-(pane,source) `seq` watermark
  and — R3's exact shape — answers `ok` and IGNORES a stale report; on every metadata report it
  enqueues the measured DOTTED `pane.agent_status_changed` with the pane's current status and the
  labels (R6). `PaneGetJson` includes `state_labels`. **The switch at `:306-325` gets a
  `"pane.report_agent"` arm that THROWS** (`FakeHerdrApiException("forbidden_in_tests")`) so any
  production path that ever reaches it fails a test loudly. `ReplayBuffer` gains
  `AddReplayPaneAgentDetected` (R10) so the pump's "untracked/replayed events are dropped" pin
  covers the second replaying event.
- **`TranscriptWorkingStateTests`:** `Classify` table (empty → Unknown; activity-no-end → Working;
  end-then-quiet → Idle; end-then-activity → Working; every housekeeping kind neither);
  `IsProvenIdle == (Classify == Idle)` over the existing cases.
- **`HerdrStatusPushTests` (new, runner):**
  1. A herdr session whose tailer mirror gains `UserPrompt` → one `pane.report_metadata` with all
     five `state_labels` ending `antiphon: working`, `antiphon-state=working`, `antiphon-as-of` =
     that record's timestamp, `ttl_ms` = setting, `seq` strictly greater than the previous push.
  2. `TurnEnd` → one push `idle`; a burst of ten records inside `DebounceMs` → ONE push.
  3. Same verdict again → NO push until `HeartbeatSeconds`; heartbeat pushes with a larger `seq`.
  4. Unbound / empty / no-tailer / claim-revoked → `unknown:<reason>`; `Pending` → zero requests.
  5. Exit → one report with `clear_state_labels:true` and null tokens; `pane_not_found` on that
     report is swallowed at Debug.
  6. Herdr unreachable → no exception, no state change; next trigger retries.
  7. **Never-act pin:** across all of the above the fake saw ZERO `pane.report_agent`,
     `pane.release_agent`, `pane.send_text`, `pane.send_keys`, `pane.close` from the push service;
     `ToDto().AgentStatus` never changed because of a push (the fake's status is whatever the test
     set); plus a structural grep-pin: `src/**/*.cs` contains no string `"pane.report_agent"`
     outside `HerdrClient` documentation comments (the wrapper is not added at all).
  8. Disabled (`StatusPush.Enabled=false` or `Herdr.Enabled=false`) → zero pipe connections.
- **`HerdrEventPumpTests` (B0):** the dotted event updates the DTO and publishes
  `SessionAgentStatus`; the underscored spelling still works; a metadata-induced event with an
  unchanged status leaves `AgentStatusSinceUtc` untouched and publishes nothing (decision 7).
- **Server:** `AgentServiceIntegrationTests` (`tests/Antiphon.Tests/Application/AgentServiceIntegrationTests.cs`)
  gains the overlay cases — herdr runner row ⇒ both fields set; pty row ⇒ null; `Backend` null ⇒
  null; runner unreachable ⇒ null (never guessed). `AgentSessionRuntime` test: a `SessionAgentStatus`
  for a claimed session publishes `AgentChanged` with the owner's id; unclaimed ⇒ none; the
  blocked-exit nudge behavior is byte-for-byte the existing test.
- **Client:** `HerdrStatusBadge.test.tsx` (five states + colours, null renders nothing, disagree
  pairs set `data-disagree` and the tooltip copy, agree pairs do not); `AgentsPage.test.tsx` gains
  one card with `herdrAgentStatus:'blocked'` asserting the badge appears beside `agent-working-*`;
  `AgentRail` tooltip case. `isHerdrDisagreement` unit table identical to
  `HerdrStatusDisagreementTests.Disagreement_matrix_matches_plan_section_5`
  (`tests/Antiphon.Tests/Application/HerdrStatusDisagreementTests.cs:31`) so the two stay in
  lockstep by shared test data.
- **Headed probes (`[Explicit]`, `Category("Herdr")`, real herdr):** R-C and R-T (§10).

## 10. Probes the build must run (cheap, spend-nothing)

| Probe | Question | When | Decides |
|---|---|---|---|
| **R-T** | Does `ttl_ms` on `pane.report_metadata` expire `state_labels` as well as `tokens`? (set `ttl_ms: 2000`, `pane.get` at +3 s) | B1, before the heartbeat default is chosen | Whether the TTL alone is the dead-runner safety net or the exit-clear + heartbeat carry it; the plan note in §5 (iii) is updated with the answer |
| **R-C** | On a REAL Claude pane parked on the trust dialog (herdr detects `blocked` — CARD-0047's shape, zero model spend), does an `antiphon` `pane.report_agent {state:idle}` displace `blocked` in `pane.get.agent_status`? Then `release_agent` and re-check. | B4, once, for the record | Nothing in this design — it documents WHY `report_agent` is forbidden with a measurement instead of an inference. If it does NOT displace, the §2 rejection still stands on the tautology argument alone, and the doc says so. |
| **R-L** | Where does the sidebar actually render `state_labels` vs `tokens` (screenshot of the operator's herdr with a pushed label)? | B1 | Label wording/length; whether `antiphon-as-of` needs to be in the label text rather than a token to be visible |

## 11. Settings

`HerdrSettings.StatusPush` (new nested class in `src/Antiphon.SessionRunner/HerdrSettings.cs`):
`Enabled` (default **true** — inert anyway unless `Herdr.Enabled`), `DebounceMs` 500,
`HeartbeatSeconds` 300, `TtlSeconds` 900, `ExitClearTimeoutMs` 2000. Bound from
`SessionRunner:Herdr:StatusPush:*`; `src/Antiphon.SessionRunner/appsettings.json` gets the block
with defaults so the operator can see it. No server setting: the badge has no switch (null hides
it), and the corroboration sweep is unchanged.

## 12. Out of scope

`pane.report_agent` / `pane.release_agent` / any lifecycle-authority claim (rejected §2, pinned
§9); `pane.report_agent_session` (still zero callers — a session-identity report is a different
feature, and S2 already binds through the `antiphon-session` token); herdr `display_agent`, title
or `pane.rename` changes; any consumption of `state_labels` BY Antiphon (the labels are for the
operator's eyes; reading our own label back would be the tautology in a hat); any change to the
S3 blocked gate, S4's matrix/hysteresis/severity, delivery verdicts, or `PtyBackend`; Grok/Codex
label wording differences (the verdict rule is transcript-kind-agnostic — the three tailers all
emit `TurnEnd`/interrupt shapes the classifier already understands, CARD-0187 decision 7).

## 13. Build order

1. **B0 — pump wire-name fix (runner, ~30 lines + tests).** `HerdrEventTypes` dotted constant,
   `HandleEventAsync` accepts both, fake emits dotted, pump test red→green, docs clause. Ship alone;
   this is a defect fix in CARD-0162's slice, not S4b.
2. **B1 — runner push (dark unless `Herdr:Enabled`).** `TranscriptWorkingState.Classify`,
   `HerdrPaneReportMetadataParams` + `HerdrPaneInfo` fields, `PaneReportMetadataAsync` overload,
   `HerdrStatusPushService` + `StatusPush` settings + registration, fake extensions incl. the
   `report_agent` throw, `HerdrStatusPushTests`, probes R-T/R-L, §5 note updated.
3. **B2 — server overlay + invalidation.** `AgentSessionSummaryDto` fields, `AttachRunnerLiveStateAsync`,
   `ObserveAgentStatusAsync` `AgentChanged` publish, tests.
4. **B3 — client badge.** DTO fields, `HerdrStatusBadge`, `isHerdrDisagreement`, placements,
   story, vitest (`scripts/test-client.ps1`), `npm run build` (E2E serves `client/dist`).
5. **B4 — evidence + docs + close.** Probe R-C on a real Claude trust-dialog pane; `docs/herdr-sessions.md`
   §7 (events: measured dotted name; our metadata push and what it never does) and §9 (S4b no
   longer deferred; `report_agent` rejected with the R1–R7 line); AGENTS.md CARD-0162 bullet gains
   "wire name is `pane.agent_status_changed` (dotted, measured 2026-08-26); S4b pushes
   `state_labels` only, never `report_agent`"; card closed with the measured results.

Slices are independently shippable. Nothing observable changes for pty sessions at any point; for
herdr sessions B0 makes S4's status arm live, B1 adds a label in the operator's sidebar, B2/B3 add a
badge that reads what S4 already carried.

## 14. Open questions for the operator

1. **The push is `report_metadata`, not `report_agent`.** The card names `report_agent`; §2 is
   why the design refuses it. If the intent was specifically "make herdr's sidebar STATE be
   Antiphon's state", that is the authority takeover, and it needs the separate design §2 sketches
   (the S3 gate must first stop reading `agent_status`). Say so and this plan's §3–§6 become that
   card's prerequisite instead.
2. **Label wording** — `"<herdr> · antiphon: <ours>"` is the proposal; R-L may show the sidebar
   truncates, in which case the build shortens to `"<herdr> · A:<ours>"` and says so.
3. **Badge on the home rail** — proposed as tooltip text only for `blocked`; a dot like
   CARD-0180's unbound dot is a one-line change if the rail should show it at a glance.
