# CARD-0334 — Live orchestrators pick up policy changes: a supervised relaunch-with-resume at the next idle boundary

**Date:** 2026-09-03 (Plan pass, task 2b2d0432 — design only; no production code changed, no tests run)
**Card:** CARD-0334 "Live orchestrators need a way to pick up workflow/policy changes without a fresh session (CARD-0303 extension)" (InProgress, Normal/Normal, rank 10)
**Extends:** CARD-0303 (closed: build a diagnose agent — done as CARD-0352), CARD-0058 (bundles + drift badge), CARD-0059 (CLAUDE.md floor), CARD-0340 (interrupted launches resume from durable state).
**Coordinates with:** CARD-0352 (Diagnose seat, S3 landed `9bd7e2e4`, S4 label sweep pending), CARD-0332/0333 (role × complexity matrix, Backlog).

**Sources (verified this pass):** CARD-0334, CARD-0303, CARD-0352, CARD-0332, CARD-0333; `server/Bundles/README.md`, `server/Bundles/orchestrator.md`, `server/Bundles/diagnose.md`, `server/Bundles/Presets/orchestrator-prompt.md`; `server/Application/Services/{InstructionBundleComposer,AgentService,AgentControlService,AgentSupervisorService,AgentSessionService,AgentSessionLaunchComposer,AgentWorkspaceProvisioner,SessionMessageQueueService,ContextCompactionService,CompactionRecoveryService,ApiErrorRecoveryService,ScheduleService,WorkflowDefinitionLoader,WorkflowDefinitionVersionGate,DiagnoseService,DelegationReportFormatter,OrchestratorService,OrchestratorControlState}.cs`; `server/Infrastructure/WorkflowDefinitions/{WorkflowFileStore,WorkflowFileWatcherHostedService}.cs`; `server/Infrastructure/Supervision/AgentSupervisorHostedService.cs`; `server/Application/Settings/{DelegationSettings,AgentSessionSettings}.cs`; `server/Application/Dtos/{AgentDtos,SessionQueueDtos,AgentSessionDtos,LaunchNotes}.cs`; `server/Domain/Enums/{QueuedMessageOrigin,AgentTaskEnums}.cs`; `server/Api/Endpoints/AgentEndpoints.cs`; `client/src/features/agents/AgentsPage.tsx`; `docs/orchestration-loop.md` §0, §3; `docs/session-runtime-invariants.md` (pty-host split, launch ownership, compaction strands working/idle); `docs/ops-http.md`; `tests/Antiphon.Tests/Application/AgentBundleAttachmentTests.cs`; git history of `server/Bundles/` since 2026-09-01; and the live server on 17202 (`/api/agents`, `/api/agents/{id}`) on 2026-09-03 ~18:30Z.

---

## Verdict up front

**A standing agent's instructions live in three places with three different propagation speeds, and only one of them is stuck: the `--append-system-prompt` bundles, which are fixed for the life of the process. The fix is not to type new rules into a live session (the bundle README pins that as forbidden, for good reason) but to make the server do, automatically and at a safe moment, what the operator did by hand to `ClaudeBot-Antiphon` tonight: stop and start the agent — except that for a Claude/Grok standing agent a start is a `--resume` of the same conversation with freshly composed launch args, so nothing in context is lost.** The card's premise that a restart "loses in-context state" is wrong for exactly the sessions this card is about: `AgentControlService.StartInteractiveSessionAsync` already resumes the previous conversation and re-stamps the bundle composition ("a resume is a LAUNCH — the args are rebuilt per invocation", `AgentControlService.cs:290–293`), and this board's own orchestrator seat has already been resumed this way at least once (session `fdf1dd3d` created 16 Aug, last started 1 Sep 18:49Z, conversation intact).

The four questions the card asks, answered:

| Question | Answer |
|---|---|
| 1. What counts as "workflow" here | **The standing instructions a live orchestrator process holds:** the composed bundles (`orchestrator`, `board-api`, `style-*` via `--append-system-prompt`), and the repo instruction files it read at launch (`CLAUDE.md` → `AGENTS.md` → the owner docs it links, plus the `antiphon-delegate` skill). **Not** `WorkflowDefinitionLoader` — that is the per-board `WORKFLOW.md` (card-session prompt template + tracker block + `max_concurrent`), which is already hot-reloaded, versioned, and consumed only at card spawn. **Not** the pipeline's server-side stages (`AgentTaskRole`, `DelegationSettings.RolePolicy`/`ComplexityChains`, the Diagnose/Check/Land hosted services) — those run in the server and reach every orchestrator on the next server restart, which does not kill sessions (pty-host split). **Not** Claude Code's Workflow-tool scripts — per-invocation, nothing standing to propagate. See D1. |
| 2. Mechanism | **Relaunch-with-resume at an idle boundary, driven by a supervisor sweep, using the drift signal that already exists (`BundlesOutOfDate`).** Push notes are the second lane (for agents that cannot resume, or that opt out): a WhenIdle System note naming what changed and telling the agent to re-read, exactly the shape of `CompactionRecoveryService`'s recovery note. A periodic self-check by the session is rejected: a session cannot change its own system prompt, and every self-check is a paid turn. See D2, D3. |
| 3. Scope | **Fleet-wide, every standing agent with a live PtyHost session and a recorded stamp** — today that is the three AlwaysOn ClaudeCode orchestrator seats already badged out of date on three boards (`Antiphon-Orchestrator`, `Gym Stat Orchestrator`, `school-revision`), plus channel agents when their bundles change. Relaunch is only for AlwaysOn Claude agents (supervision recovers a failed relaunch; `--resume` preserves context); everything else gets the note lane. Not pool delegates (retire at 60 min idle; briefs carry the state of today), not card sessions (per-card, short-lived), not Herdr/attached panes (no stamp = no evidence). See D4. |
| 4. Mid-decision risk | **Never mid-turn; only at an idle boundary, with the conversation preserved and a note that says what changed and when.** Because the resumed session still holds everything it told its delegates, it can reconcile deliberately (`-Refine` a running delegate) rather than being contradicted silently. Requiring "no delegates in flight" would mean this board's seat never refreshes — it always has delegates running. The ordering is also right by construction: the launch note is delivered Now, then the `[task … done]` rows that queued during the relaunch flush WhenIdle behind it. See D5. |

**Four slices, sequential, ~11–15 h.** S1 the instruction-file stamp and a fuller drift DTO; S2 the relaunch sweep; S3 the note lane, per-agent mode and the manual endpoint/button; S4 docs and live verification on this board's seat.

---

## Live evidence, and what it changes

**This session's orchestrator is the case.** `GET /api/agents/a392cbc4…` (`Antiphon-Orchestrator`, AlwaysOn, ClaudeCode, ReplyStyle Brief, cwd `C:\src\Antiphon`) on 2026-09-03 18:30Z:

| Fact | Value |
|---|---|
| Live session | `fdf1dd3d` — created 2026-08-16 16:12Z, **started 2026-09-01 18:49Z** (a resume: `createdAt ≠ startedAt`), transcript `bound`, context fullness 4.3 % |
| Launched with | `board-api v51981dbe, orchestrator v26dea68f, style-brief v664a6353` |
| `bundlesOutOfDate` | **true** |
| Bundle commits since its launch | 9 (`4b33dd06` 09-01 20:46 … `eb25e209` 09-03 18:09), including CARD-0017's delegate-the-reading rule (`dca9a5f5`), CARD-0294's `-Continue`/`authority:` paragraph (`332098ca`), CARD-0339's check reading, CARD-0352's diagnose seat |

So the seat that dispatched this Plan pass is running an `orchestrator` bundle that predates the blocked-note protocol it is supposed to follow; it knows about those changes only to the extent it was told in chat. Two other AlwaysOn orchestrators (`Gym Stat Orchestrator`, `school-revision`) show the same badge. The badge is working exactly as CARD-0058 designed it ("informational only… nothing forces a launch"); this card is the "something forces a launch" that CARD-0058 deliberately left out.

**The stop/start precedent is a resume, not a loss.** `FindResumableSessionAsync` (`AgentControlService.cs:401–421`) resumes the agent's last session when the kind is ClaudeCode or Grok, the row is Stopped/Failed, and the cwd matches. The resumed launch rebuilds `--append-system-prompt` from the repo (`AgentSessionLaunchComposer.cs:74–100`), restamps `ComposedBundleStamp`, re-arms remote control, delivers `RestartResumeBody` (Now-mode, `AgentSessionService.cs:2124–2165`), and auto-continues an interrupted turn (`EnqueueResumeContinueAsync`). Pending queue rows stay on the same session row. What a resume genuinely loses: in-process state only — background Agent-tool subagents, a running shell command, MCP connections (re-established), and the prompt cache (the first post-resume turn re-prefills the whole context; at 4 % that is nothing, at 80 % it is one uncached opus prefill).

**The seat's floor is not the lever.** The CARD-0059 `CLAUDE.md` floor is `LeftAlone` for any agent whose cwd is a real checkout (the repo's own `CLAUDE.md` → `AGENTS.md`), which is every orchestrator seat. Claude reads those at process start; the compaction recovery note already tells channel agents to re-read `CLAUDE.md` after a compaction. Repo-doc changes therefore propagate on relaunch for free; the bundle is the only thing that needs the process to be relaunched, and relaunch covers both.

**Server-side stages already propagate without the orchestrator.** CARD-0352 S3 landed while this seat was running: `DiagnoseHostedService` auto-titles tasks the seat creates, and the seat has no idea it exists — nor does it need to. The operator's motivating example ("add a diagnose step at the start for cards without a complexity label") is, in the shipped design, a server sweep (CARD-0352 S4) plus one sentence in `orchestrator.md` ("read the `complexity:`/`ui:` labels; do not judge by hand"). This card is the mechanism for that sentence.

**`WorkflowDefinitionLoader` is a different "workflow".** It watches `.antiphon/boards/<id>/WORKFLOW.md` (this board's: `name: Antiphon`, `max_concurrent: 1`, a GitHub tracker block, a 5-line prompt template), debounces file changes 250 ms, saves a new `BoardWorkflowDefinition` version behind a per-board semaphore, and publishes `WorkflowReloaded`. It is already hot. Its consumer is card spawn (`RenderPrompt` + `BuildPromptVariables`); a running card session keeps the prompt it was launched with, which is correct for a per-card session. Nothing here to change; the name is the only overlap.

---

## Decisions

### D1. Which instructions this card propagates, and which it explicitly does not

Three tiers, by where the text lives and when the process reads it:

| Tier | Where | Read when | Propagates today | This card |
|---|---|---|---|---|
| **Bundles** (`orchestrator`, `board-api`, `delegate-basics`, `style-*`, `Agent.SystemPromptAppend`) | `--append-system-prompt` (`--rules` for Grok, `-c developer_instructions` for Codex) | Process start only | Next launch | **Yes — the core gap** |
| **Repo instruction files** (`CLAUDE.md`, `AGENTS.md`, the owner docs it links, `.claude/skills/antiphon-delegate/SKILL.md`) | Files under the agent's cwd | `CLAUDE.md`/`AGENTS.md` at process start and after compaction (Claude re-injects them — S1 verifies on a live compaction); a skill body at each invocation; owner docs on demand | Next launch / next compaction / next read | **Yes — as a second drift input to the same signal** |
| **Server-side pipeline** (`AgentTaskRole`, `RolePolicy`, `ComplexityChains`, Diagnose/Check/Land/close sweeps, `WORKFLOW.md`) | Server process / DB / per-board file | Server start (settings are `IOptions` + `ValidateOnStart`), or hot (`WORKFLOW.md`) | Server restart, which does not touch sessions | **No — already reaches every orchestrator** |

The rule this produces, to be written into the bundle README and orchestration-loop §3: *a pipeline step every orchestrator must honour is built server-side; what goes to the orchestrator is the one sentence about how to behave toward it, and that sentence lives in `orchestrator.md`, which this card delivers to live seats.*

### D2. Relaunch-with-resume is the mechanism, not injection and not self-check

- **Injection is refused by design and stays refused.** `server/Bundles/README.md` and orchestration-loop §3 both pin "Nothing types bundles into a live session, deliberately". The reason holds: a rule typed into the conversation competes with the stale rule in the system prompt, decays with compaction, and is invisible to the drift check. This plan never puts bundle *text* into a queue row; the note lane carries stamps and file names only (a test pins that).
- **Self-check is the wrong shape.** A session cannot edit its own launch args, and "re-read AGENTS.md every N minutes" is N paid turns per hour on a frontier model for a file that changes a few times a day. The server already knows the exact moment drift appears (the composition is recomputed per request) and the exact moment the session is idle (`IsWorkingAsync`); it should act, not ask.
- **Relaunch is what the operator already does, made safe.** The manual precedent (`POST /stop` then `POST /start` on `ClaudeBot-Antiphon` to apply a model change) has two flaws the sweep removes: `StopAsync` suspends supervision (`AgentControlService.cs:626–648`), and a human picks the moment. The sweep uses `AgentSessionService.KillAsync` with a new `SessionTerminationSource.PolicyRefresh` (never `StopAsync`), then `StartAsync(new StartAgentRequest(Fresh: false, IgnoreSubscriptionQuota: true))` — the same call the supervisor makes on a crash restart — within one sweep pass, so the supervisor's own tick never sees the gap and never grows the backoff ladder (a test pins `ConsecutiveFailures` unchanged across a policy relaunch).

### D3. One drift signal, two inputs, two lanes

**Signal.** `AgentSession` gains `InstructionFileStamp` (≤ 2000 chars, same shape as `ComposedBundleStamp`: `AGENTS.md v1a2b3c4d, docs/orchestration-loop.md v9e8d7c6b, …`), recorded by `AgentSessionLaunchComposer` for standing launches from the files in `PolicyRefreshSettings.InstructionFiles` that exist under cwd. Default list: `CLAUDE.md`, `AGENTS.md`, `docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`, `docs/ops-http.md`, `.claude/skills/antiphon-delegate/SKILL.md`. Hash = first 8 hex of SHA-256 over LF-normalised text, exactly `InstructionBundle.Version`'s rule, so an operator reads both stamps the same way. Drift = `InstructionBundleComposer.IsOutOfDate(bundleStamp, current)` **or** the same string comparison on the file stamp. Null on either side is no evidence (the CARD-0213 attached-pane rule) and never drift.

**Why files too, when relaunch already re-reads them.** Without the file stamp the sweep would relaunch only on bundle edits and an `AGENTS.md` safety-trigger edit would still wait for the next crash. With it, the operator's actual ask ("modify workflow and all live orchestrators get the update") is one rule: *edit any standing instruction, and every idle standing agent has it within a couple of minutes.* The churn cost is bounded by the cooldown (D5) and by the list being explicit — a `docs/superpowers/plans/*.md` commit changes nothing.

**Lane A — Relaunch.** For agents in `Relaunch` mode: kill → resume → a dedicated launch note (`ChannelPreamble.PolicyRefreshResumeBody(delta)`):

> `[System note from Antiphon: your session was relaunched to pick up updated standing instructions — orchestrator v26dea68f → v3c1f0a9e; AGENTS.md changed. Your conversation is intact and the new instructions are in your system prompt now; where they differ from what you told a delegate before this note, the new instructions win — steer that delegate with -Refine rather than assuming it knows. Re-read AGENTS.md before your next dispatch. Do not re-execute completed work. Reply NO_REPLY unless you have something for the user.]`

Recorded as `AgentIncidentKind.PolicyRefreshed` (Info, no alert; message carries the stamp delta) — the timeline row is the audit that a rule change reached this seat, and when.

**Lane B — Notify.** For agents in `Notify` mode, or any agent the relaunch gates exclude (D4): one WhenIdle `System` row per distinct drift (dedupe key: the current composed stamp line + file stamp line, persisted as `AgentSession.PolicyNotifiedStamp`):

> `[System note from Antiphon: your standing instructions changed since you launched — orchestrator v26dea68f → v3c1f0a9e (in your system prompt only at your next launch); AGENTS.md, docs/orchestration-loop.md changed (re-read them now, before your next dispatch). Reply NO_REPLY unless you have something for the user.]`

Recorded as `PolicyDriftNotified` (Info). This lane is honest about its limit: it cannot apply a bundle change, and the note says so.

### D4. Scope and the eligibility gates

Population: every `Agent` row with a live PtyHost session (`PersistentSessionId` → `Status == Running`), AlwaysOn or not. Per agent the sweep resolves a lane:

| Condition | Lane |
|---|---|
| `PolicyRefreshMode == Off` | none |
| No recorded stamp (Herdr attach, pre-column sessions) | none — no evidence |
| Kind ∉ {ClaudeCode} for Relaunch (Grok resumes, but `--rules` on resume is unproven; Codex/OpenCode/Raw never resume) | Notify |
| `AlwaysOn == false` (no supervision to recover a failed relaunch) | Notify |
| Supervision `Suspended`, or `NextRestartAt` set | skip this tick |
| Model held (`IModelAvailability.RequireAsync` would throw) | skip this tick |
| Live session `transcriptBinding != bound` (a resume would fall back to fresh and lose the conversation) | Notify |
| Otherwise, AlwaysOn ClaudeCode with drift | **Relaunch** |

Defaults: AlwaysOn → `Relaunch`, others → `Notify`; `Agent.PolicyRefreshMode` (nullable enum column, `Auto` = the default above) overrides per agent, editable in the agent settings modal next to the reply style. The delegate pool and card sessions are out of population by construction (no `Agent.PersistentSessionId`).

### D5. The idle boundary, the cooldown, and why "no delegates in flight" is not a gate

A relaunch or a note fires only when, after `AgentSessionRuntime.CatchUpTranscriptAsync` (pull before acting, the `ContextCompactionService` rule — gotchas #50/#54 say a compaction or local slash-command can strand working/idle in stored rows), all of:

- `!IsWorkingAsync` (transcript-derived, the same signal WhenIdle delivery uses);
- the session's queue has no `Pending` row and no `Sent` row with a null `DeliveryVerdict` (an interrupted attempt, gotcha #84) and no Channel row still owed a reply (`ChannelReplySettledAt == null`, the CARD-0233 yield rule);
- no `Supervision`-origin `/compact` row is Pending (let the compaction land first; a resume at 4 % is cheaper than a resume at 90 %);
- idle for at least `PolicyRefreshSettings.IdleMinutes` (default 2) by transcript;
- last policy action on this agent older than `CooldownMinutes` (default 30) — in-memory stamp like `ContextCompactionService._attempts`, plus the durable `PolicyRefreshed` incident as the cross-restart check.

**Why in-flight delegates do not block.** Delegates are separate processes; the parent's relationship to them is queue rows (`[task … done]`, `[check …]`) that survive a resume on the same session row and flush WhenIdle behind the launch note. The seat that runs this board has had delegates in flight continuously for the whole session; a gate on "none in flight" is a gate on "never". The mid-decision risk the card raises is real but it is a *conversation* risk, and the conversation survives: the relaunched orchestrator sees, in order, the instruction it gave, the note saying the rules changed, and the delegate's report — which is the best position from which to reconcile the two. That is materially safer than the status quo, where the rule changes silently for the next fresh sub-orchestrator and not at all for the seat.

**What the sweep must never do:** kill a session that reads working; type bundle text; use `StopAsync` (suspends supervision); relaunch a session it cannot resume; retry inside the cooldown; act on Herdr or attached panes; act while the runner is unreachable (the supervisor's own guard, reused by running inside its tick).

### D6. Where it runs

A new `PolicyRefreshService` (singleton, per-tick scopes, mirrors `ContextCompactionService`), swept from `AgentSupervisorHostedService` with its own `_lastPolicyRefreshSweepUtc` and a 1-minute period, **before** `supervisor.TickAsync` in the same loop iteration so a relaunch's kill→start completes inside one pass. Settings: `Supervision:PolicyRefresh { Enabled=true, IdleMinutes=2, CooldownMinutes=30, InstructionFiles=[…] }` on `SupervisionSettings` (where `ApiErrorRecovery` already lives), validated on start.

### D7. Manual front door and UI

- `POST /api/agents/{id}/refresh-policy` body `{ "force": false }`. Idle-gated like the sweep. `force: true` skips only the idle-minutes floor and the cooldown; a session that reads working is always a 409 `session_working`, and an agent whose lane resolves to Notify gets the note and a 200 that says so (`refreshed: false, notified: true`). Returns the `AgentDetailDto`. Documented in `docs/ops-http.md` beside start/stop, with the sentence "this is the stop/start you did by hand, without suspending supervision".
- `BundleDriftBadge` (`AgentsPage.tsx:631`) becomes the drift badge for both inputs: tooltip lists what drifted ("orchestrator, AGENTS.md"), and — for agents in Relaunch/Notify mode — says "refreshes at its next idle window" instead of "at its next launch"; a "Refresh now" action calls the endpoint. `AgentSummaryDto`/`AgentDetailDto` gain `PolicyDrift { Bundles: string[], Files: string[], Mode, LastRefreshedAt }`; `BundlesOutOfDate` stays as-is for compatibility (it becomes `Bundles.Count > 0`).

### D8. Grok, Codex and channel agents

Grok standing agents (`Grok 4.6` is a live example) are Notify-only in this card even though `FindResumableSessionAsync` allows a Grok resume: `--rules` on a resumed Grok conversation and the CARD-0342/0324 sign-in/submit-evidence work are too fresh to bet an automatic kill on. Promote to Relaunch after one deliberate manual `refresh-policy` on a Grok seat proves the resume path. Codex cannot resume (always fresh) — Notify only, permanently, and the note says the bundle applies at the next launch. Channel-bound Claude agents (`Family`, `AZ Care`, `Slack Test`) are AlwaysOn ClaudeCode and get Relaunch by default; their launch note is the same body, and it ends in the `NO_REPLY` clause so a relaunch never produces a chat message.

---

## Slices

### S1 — Instruction-file stamp and a fuller drift DTO (≈3–4 h)

- Migration: `AgentSession.InstructionFileStamp` (string?, ≤ 2000), `AgentSession.PolicyNotifiedStamp` (string?, ≤ 4000), `Agent.PolicyRefreshMode` (int?, enum `Auto|Relaunch|Notify|Off`).
- `InstructionFileStamps.Compute(cwd, files)` — pure, LF-normalised SHA-256/8 per existing file, missing files omitted; `StampLine` in composition order. `AgentSessionLaunchComposer.ComposeAsync` returns it alongside `composedStamp`; `AgentControlService` records it on both the new-session and the resume branch (the resume branch restamps, exactly as the bundle stamp does at `:293`). Delegate launches leave it null (out of population).
- `AgentService.IsOutOfDate` → `PolicyDrift.Of(live, currentBundles, currentFiles)`; DTOs gain `PolicyDrift`; `BundlesOutOfDate` derived. `AgentsPage` tooltip lists both.
- `PolicyRefreshSettings` on `SupervisionSettings` with validator (idle ≥ 1, cooldown ≥ 5, file list relative paths only, no `..`).
- Verify on a live compaction (a test agent in a scratch dir with a marked floor): does Claude re-read `CLAUDE.md` from disk after `/compact`? Record the answer in the plan's S4 doc edit; it decides whether a file-only drift on a session that has since compacted can be cleared without relaunch (if yes, S2 clears file drift on `CompactBoundary`; if no, nothing changes).
- Tests: `InstructionFileStampTests` (hash rule matches `InstructionBundle.Version`; missing file omitted; CRLF-insensitive), `AgentBundleAttachmentTests` siblings for file drift, DTO round-trip.

### S2 — The relaunch sweep (≈5–6 h)

- `SessionTerminationSource.PolicyRefresh`; `AgentIncidentKind.PolicyRefreshed`, `PolicyRefreshFailed` (next free values — read the enum at execute time, CARD-0340/0352 have both taken numbers earlier plans assumed).
- `PolicyRefreshService.SweepAsync(ct)`: population → lane (D4) → gates (D5) → for Relaunch: stamp the attempt, `KillAsync(sessionId, PolicyRefresh)`, wait for the row to read Stopped (bounded, `DeadSessionFailGraceMinutes` is too long — use the runner's kill confirmation, 30 s cap), then `AgentControlService.StartAsync(agent.Id, new(Fresh: false, IgnoreSubscriptionQuota: true))`. Launch notes: `AgentControlService` builds `LaunchNotes` today only for preamble-configured agents (`:252–269`); add a `PolicyRefresh` variant so the resume body is `PolicyRefreshResumeBody(delta)` for this launch regardless of preamble (an orchestrator seat without a channel preamble must still be told why it was relaunched). The delta text is computed from the two stamp lines (old→new per key, added/removed keys, changed files).
- On `StartAsync` throwing: `PolicyRefreshFailed` (Warning) with the exception message; do **not** touch supervision state — the agent is now Stopped and the supervisor's next tick schedules its normal ladder restart, which also carries the new bundles. A test pins that the ladder's `ConsecutiveFailures` is untouched on the success path.
- Hook into `AgentSupervisorHostedService` (D6). Pull-before-act with `CatchUpTranscriptAsync`.
- Tests (`PolicyRefreshServiceTests`, fake clock/runtime/queue in the `ContextCompactionServiceTests` shape): drift + idle → one kill + one resume + incident; working → skip; queued Pending row → skip; Channel row owed → skip; cooldown → skip; suspended → skip; held model → skip; unbound transcript → Notify instead; Codex → Notify; Herdr null stamp → nothing; relaunch note contains stamps and file names and no bundle text (scan against every `InstructionBundles` text); supervision ladder unchanged; `AppHost`-style server restart mid-relaunch leaves a `Starting` row CARD-0340's resume adopts (integration, reuse `LaunchInterruptedByRestart` harness).

### S3 — Notify lane, per-agent mode, manual endpoint and badge action (≈2–3 h)

- Notify body + `PolicyDriftNotified` incident + `PolicyNotifiedStamp` dedupe; delivered through `SessionMessageQueueService.EnqueueAsync(WhenIdle, origin: System)` — it rides the existing idle fast-path and the stranded watchdog.
- `POST /api/agents/{id}/refresh-policy` (D7), `AgentControlService.RefreshPolicyAsync(agentId, force)`; 409 `session_working` / `not_resumable` problem types.
- `PATCH /api/agents/{id}` accepts `policyRefreshMode`; settings modal select ("Auto (relaunch when idle)", "Relaunch", "Notify only", "Off") beside reply style; badge "Refresh now".
- Tests: endpoint 409s, force path, mode round-trip, Notify dedupe across sweeps and across a server restart (persisted stamp), note text pinned.

### S4 — Docs, invariants, and live verification on this board's seat (≈1–2 h)

- `server/Bundles/README.md`: replace "Nothing types bundles into a live session" with "Nothing types bundles into a live session; instead a standing agent that is idle is relaunched with `--resume` and the new composition (CARD-0334), keeping its conversation" and describe the modes. Same paragraph in `docs/orchestration-loop.md` §3 (`:242–245`) and a line in §0 that a standing orchestrator's bundle is refreshed automatically. `docs/ops-http.md`: the endpoint. `docs/session-runtime-invariants.md`: a gotcha — "a resume is a launch: args, bundles and stamps are rebuilt; the conversation is not." `docs/agent-card-lifecycle.md`: nothing (no card state changes).
- Live: with the sweep enabled on 17202, watch `Antiphon-Orchestrator` at its next idle window: `bundlesOutOfDate` flips false, a `PolicyRefreshed` incident appears with the stamp delta, the transcript shows the note, and a `[task … done]` that queued during the relaunch still delivers after it. Then the same for `Gym Stat Orchestrator` and `school-revision` without further action. Record the three timestamps on the card.

---

## Cost, risk, and what could go wrong

- **Cost of a relaunch:** boot to ready (tens of seconds, no tokens) + one uncached prefill of the resumed context on the next turn + the note's reply (`NO_REPLY`). At this seat's 4 % fullness that is negligible; at high fullness it is one full-context prefill, which is why the sweep yields to a pending `/compact` first. Worst-case cadence is one relaunch per agent per cooldown (30 min), and only when something actually changed.
- **Kill-mid-turn misread** is the failure that matters. Mitigations are the ones the compaction sweep already relies on: transcript pull before acting, the working rule, the queue checks, and the idle-minutes floor. A relaunch that does land mid-turn is recoverable — `WriteRestartBoundaryIfInterruptedAsync` + the auto-continue prompt exist for exactly this — but it is still a defect to be logged as an incident, not a "flaky".
- **Resume-not-found fallback** would silently start a fresh conversation. Gated by `transcriptBinding == bound` (D4); if the fallback fires anyway, the launch path's `ClaudeSessionNotFoundException` branch already delivers the bootstrap body, and the sweep records `PolicyRefreshed` with `fresh: true` so the loss is visible.
- **Remote-control re-arm** on resume is the existing path (`SendRemoteControlCommandsAsync`, CARD-0292 probe) — no new behaviour, but the live verification in S4 must confirm the seat's bridge is live after the relaunch (`/rc-status`).
- **Churn on doc edits.** The file list is deliberately short and explicit; a plan or spec commit does not trigger it. If the operator finds the cadence too high, `Notify` mode per agent or a longer cooldown is the dial; nothing in the design depends on the default.
- **Two orchestrators on one board racing.** Not possible here (one seat per board), and pool sub-orchestrators are out of population.

## Not this card

- Building the complexity/UI label sweep (CARD-0352 S4) or the role × complexity matrix (CARD-0332).
- Hot-reloading `DelegationSettings` (`IOptionsMonitor`) — a server restart already reaches every orchestrator, and settings changes are rare and deliberate.
- Propagating to pool delegates mid-task, or to card sessions — briefs carry the state of today (orchestration-loop §3), and both are short-lived.
- Grok relaunch (D8) — promote after one manual proof.
- Hashing the entire docs tree, or deriving the file list from `AGENTS.md`'s link table automatically — explicit list first; widen if a real miss shows up.
