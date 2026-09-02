# CARD-0281 — Provider-capacity failures: tell the channel user in seconds, hold the provider, launch the declared fallback, keep schedules and identity durable

**Card:** CARD-0281 "Surface provider-capacity failures and provide a durable channel-agent fallback" (In Progress, High)
**Task:** dd9b8487 (Plan, Frontier, Shared) · **Date:** 2026-09-02 · **Code re-read on:** `582da043`
**Related:** CARD-0071/0072 (API-error carriage, classifier, recovery ladder), CARD-0022/0309 (`ModelAvailabilityHold`), CARD-0067/0233 (channel reply durability, targeted lost-reply notice), CARD-0057 (`Schedule`/`ScheduleFire`), CARD-0090/0322 (delegation fallback chains — consumed, not extended), CARD-0080/0157/0159/0241 (Grok normalizer), CARD-0136 (quota gate), CARD-0245 (ingress detector).

This is a design document. No production code was written for it. Nothing was built or run beyond read-only queries.

---

## Verdict up front

1. **The root cause is a carriage gap in the Grok normalizer, not a channel-side gap.** Grok writes the error structurally — a `retry_state` row (`type: failed`, `error_type: api`, `message: "API error (status 402 Payment Required): Grok Build usage balance exhausted"`) followed by `turn_completed` with `stop_reason: "error"` and the same text in `agent_result`. `GrokTranscriptNormalizer.FromTurnCompleted` (`src/Antiphon.SessionRunner/GrokTranscriptNormalizer.cs:314-365`) keeps `stop_reason` and drops `agent_result`; `retry_state` falls to the `_ => []` arm. So `IsApiError` is null on every Grok error TurnEnd (10 stored rows, 6 sessions, `IsApiError` null on all), and every consumer built for CARD-0071/0072/0022 — the channel withhold, the recovery ladder, the hold writer, the task fail arm, the channel-bound Critical incident — is blind. Codex already solves the same problem the right way (`CodexTranscriptNormalizer.ReadError`, `:346-390`, diagnostic on the TurnEnd itself); Grok gets the same shape.
2. **Today the waiting user hears nothing for 30 minutes, then a wrong sentence.** With no stamp, `ChannelReplyDispatcher` sees a turn with no text and leaves the correlation owed; the TTL sweep (`:414-460`) classifies it `TurnIncomplete` and the CARD-0233 notice says "a matching prompt was recorded but no turn completed within 30 minutes" (`:522-523`). Fix: at the moment the stub is withheld (`:243`), adopt it, and when the class is terminal for automation send one capacity-specific, channel-safe notice to the originating conversation on the same `ChatChannelService.SendAsync` path CARD-0233 chose, settle those correlations against it, and stop typing further channel prompts into that session.
3. **The circuit breaker is `ModelAvailabilityHold`. Do not build a second one.** Once stamped, the stub flows through `ApiErrorRecoveryService.ApplyWallAsync` (`:330-391`) which already writes an AutoDetected hold and never nudges a ModelCap. What is missing is classification: `ApiErrorClassifier` maps 402/403 to `Unknown` (`:26-46`), which would enter the Transient ladder and type "resume" prompts into a session whose account has no credit. New arms: 402 → `Wall`; 403 → `Wall` when the text matches the provider's own capacity vocabulary (measured in the Grok binary: `spending limit`, `out of credits`, `usage balance exhausted`, `usage limit reached`), otherwise `NeedsHuman`. Both are terminal; both write the hold for `(Grok, grok-4.6)` with `DisabledUntil = null`. Dispatcher skip, create/start 409, attention kind 24, `GET /api/model-availability`, CARD-0090 chain exclusion all come for free.
4. **The fallback is declared on the agent by a human, in advance; it is never chosen by Antiphon.** `Agent.FallbackTuiProfileId` / `FallbackModelId` / `FallbackModelLevel` (null = today's behaviour: refuse and notify). `AgentControlService.StartAsync` consults it exactly where `RequireAsync` throws today (`:135`); a channel-bound session that dies on a terminal class with a fallback declared is stopped and relaunched on it, the owed prompts are re-enqueued as Pending so the existing relaunch carry-over (`:333-345`) moves them, and the reply routes normally. Every activation is an incident, an attention row and an agent event, and the channel gets one sentence saying who is answering. This satisfies "route only the scoped workload": the fallback is per agent, the hold is what pauses fleet dispatch, and CARD-0090's chain keeps owning delegate tasks.
5. **Schedules: Grok's scheduler is in-process and dies with the process; the durable replacement already ships.** The Grok binary carries an `x.ai/scheduler/*` RPC family and a "Maximum 50 scheduled tasks can be active at once" tool description; `~/.grok` holds no scheduler state file to migrate. CARD-0057's `Schedule` (Kind `Prompt`, Repeat `Interval`, `EveryMinutes 30`, `WhenTargetDown = Queue`) targets the **agent**, resolves `PersistentSessionId` at fire time (`ScheduleService.cs:246-262`), and therefore survives a crash, a fresh relaunch and a fallback launch. This slice is a runbook, a bundle sentence, and a detection incident — no scheduler code.
6. **Identity: the caller sees the requested alias only, the channel sees nothing.** `Dispatched` events say `(grok-4.6)` with no kind (`AgentTaskDispatcher.cs:2376`), `BuildCompletionNote` adds `ModelLevelAliases.For(kind, level)` (`DelegationReportFormatter.cs:362`) — the requested alias, never the measured model — and `EffectiveModelId` is null on 271 of 277 Grok sessions. Add `{kind}/{alias}` plus the measured model and a `fallback` marker to dispatch events, completion notes, API-error and hold incidents, and stamp `EffectiveModelId` on every launch. No per-reply provider stamp in chat; the one-time notice and the attention row are the audit surface a human wants.

Estimated build: **S0–S3 ~9–12 h** (the incident fix), **S4 ~8–10 h** (declared fallback), **S5–S7 ~4–5 h**. Sequential, one Worktree per slice group.

---

## 0. Scope note — the incident is not on this deployment

This box has no agent named PredictionMarkets, no Slack channel bound to a Grok agent (the two Slack rows bind to `Slack Test`, a Claude agent), an empty `Schedules` table, and zero HTTP 403 rows in `~/.grok/sessions` (the three `permission-denied` hits are tool-result text about a worktree delete). CARD-0281 came in as GitHub issue #25 from another Antiphon deployment. The design below is grounded in the **same failure on this box**: xAI **402 Payment Required — "Grok Build usage balance exhausted"** on 2026-08-23 and 2026-08-26 (§2.1), plus the Family agent's 2026-09-01 Telegram loss that shows what the user-facing 30-minute notice looks like today (§2.2). The 403 wording from the card is pinned as a fixture with the card's text; its exact provider wording is unverified here and is called out as such.

---

## 1. What the code does today (verified)

### 1.1 Grok carriage — two rows, neither ingested

Measured shape (22 occurrences across local Grok files, 15 of them the 402; the 400/404 ones are the stub-proxy canaries):

```
{"method":"_x.ai/session/update","params":{"update":{"sessionUpdate":"retry_state","type":"failed","error_type":"api",
  "message":"API error (status 402 Payment Required): Grok Build usage balance exhausted"}, "_meta":{"eventId":"…-4580"}}}
{"method":"_x.ai/session/update","params":{"update":{"sessionUpdate":"turn_completed","prompt_id":"ab64d0fc-…",
  "stop_reason":"error","agent_result":"API error (status 402 Payment Required): Grok Build usage balance exhausted",
  "usage":{…"modelUsage":{"grok-4.6-build":{…}}}}}}
```

- The `retry_state` row arrives 0–500 ms before the `turn_completed` and carries the identical message. Grok's own transient retry is a separate `retry_state` `type: retrying` row (`attempt`, `max_retries: 15`, `reason: "API error (status 500 …): The model is currently at capacity…"`) — Grok retries 5xx itself up to 15 times before it ever writes `failed`, so a `failed` row is terminal by construction.
- `agent_result` appears only on `stop_reason: "error"` rows (0 occurrences on `end_turn`/`cancelled`). Message grammar is stable across 402/400/404: `API error (status <code> <reason phrase>): <detail>`.
- Every background-task completion Grok injects (`<system-reminder>Background task … completed`, `promptId` `task-completed-call-…`) opens a new turn that dies the same way, so one exhausted account produces a burst: session `09bca313` wrote four error TurnEnds in 17 minutes, three of them for prompts Antiphon typed after the first death.
- `GrokTranscriptNormalizer` keeps `stop_reason` verbatim (`:359`), ignores `agent_result`, and skips `retry_state`. `GrokTranscriptTailer.Publish` (`:318-327`) already forwards `IsApiError`/`ApiErrorClass`/`ApiErrorStatus` from the part, so the transport needs nothing.
- `ProviderContractCatalog.Grok.UsageLimitSignal` is `Unknown` / "pending CARD-0083 S1 survey" (`:118-122`). This pass is that survey for the capacity axis.

### 1.2 What the server does with an unstamped error TurnEnd

| Consumer | Behaviour today | Consequence |
|---|---|---|
| `AgentSessionRuntime.IsTurnBoundary` (`:351-360`) | only `end_turn` / `cancelled` / interrupt marker | an error TurnEnd flushes no queue and triggers no dispatcher; settlement waits for the next sweep |
| `TranscriptWorkingState.Classify` (`:41`) and the server working rule | any `TurnEnd` is an end | the session reads **idle**, so `SessionMessageQueueService` keeps delivering `WhenIdle` rows into a dead session (measured: refinement prompts at 20:20:36 and 20:21:53 on `09bca313`, each dead in <1 s) |
| `TranscriptKinds.IsReportBoundary` (`:332`) | anything but `cancelled` | `ExtractMarkedTurnAsync` sees a finished turn with no text → CARD-0046 grace → task Failed "ended with no report at all… 120s grace" (tasks `356cdd89`, `9a92bb0c`, `b72da3bb`). Right status, wrong reason, no `ApiErrorTurnDied` incident, no hold |
| `ChannelReplyDispatcher.DispatchAsync` (`:231-252`) | `containsApiErrorStub` false, `responseText` null → "no assistant text yet; correlations stay pending" | nothing is sent; the row stays owed |
| TTL sweep `ClassifyTtlLossAsync` (`:463-497`) | prompt matched, TurnEnd exists, 0 assistant chars → `TurnIncomplete` | Critical `ChannelReplyLost` + targeted notice "**a matching prompt was recorded but no turn completed within 30 minutes**" — 30 minutes late and false (the turn completed, at the API) |
| `ApiErrorRecoveryService.AdoptAsync` (`:126-166`) | selects `IsApiError == true` TurnEnds only | never adopts; no hold, no incident |
| `ApiErrorClassifier.Classify` (`:26-46`) | 402/403 → `Unknown` | once stamped, without S2 this enters the 1/3/5-minute Transient ladder and types `TransientPrompt` into the dead session up to `UnknownAttemptCap` (3) |

### 1.3 The hold machinery is complete; two doors do not know about it

- `AgentControlService.StartAsync` gates on `RequireAsync` (`:131-136`) → `ModelDisabledException` 409. `ChannelBridgeService.EnsureAgentSessionAsync` (`:314-370`) calls `StartAsync(IgnoreSubscriptionQuota: true)` (`:356`) and catches nothing; the exception propagates to the consume loop's `catch (Exception)` (`:76-82`), which backs off 15 s and re-enters `ConsumeAsync`. The Kafka consumer auto-commits (`KafkaAntiphonMessagingConsumer.cs:29`), and on redelivery `UpsertFromInboundAsync` reports a duplicate, which `HandleInboundAsync` skips (`:110-115`). **Inferred from code, not reproduced:** a channel message that arrives while its agent's model is held is dropped with a Warning log line and no incident, no notice. That is the exact shape this card is about, one door earlier.
- `AgentSupervisorService` AlwaysOn restart (`:195-230`) wraps `StartAsync` in `catch (Exception)` and records `StartFailure` ("Start attempt N failed: fable is disabled until …") on the backoff ladder — visible, but it says "start failed" every rung instead of "held; fallback not declared".

### 1.4 Channel notice plumbing that already exists

`ChannelReplyDispatcher.NotifyOriginatingConversationsAsync` (`:606-652`) resolves the catalog row from `ConversationKey` and calls `ChatChannelService.SendAsync(channel.Id, text)` (`ChatChannelService.cs:95-127`), which builds a `ChannelReply` with `ConversationId` only. The main reply path attaches the catalog `ReplyHandle` (`:300`), which on Slack is `C123|thread_ts` and keeps the reply in the thread (`SlackChannelAdapter.cs:42`, `:729`). A notice sent without it lands top-level in the Slack channel. `ChannelSendOptions` (`:187`) has `Silent` and `ReplyToMessageId` and no handle.

### 1.5 Schedules

- Grok binary: RPC ids `x.ai/scheduler/delete` (and a truncated sibling), tool text "Create a scheduled task… List all active scheduled tasks with their IDs, prompts, intervals, and next fire times… Maximum 50 scheduled tasks can be active at once", changelog 1.0.13 "Recurring scheduled tasks now include a reminder to stop the monitor". No `scheduler`/`tasks` state file under `~/.grok` or any session directory. The tool's `_meta["x.ai/tool"].name` is **unmeasured** (no local transcript ever called it).
- Antiphon: `Schedule` + `ScheduleFire` (`server/Domain/Entities/Schedule.cs`, `ScheduleFire.cs`), `ScheduleService.FireCoreAsync` resolves `Agent.PersistentSessionId` at fire time, queues `WhenIdle` with `Origin = Scheduled`, and when the agent is down with `WhenTargetDown = Queue` records `QueuedForRelaunch` — the relaunch carry-over delivers it (`:262-330`). `scripts/schedule.ps1` (`list/get/preview/new/enable/disable/remove/fire`) and `GET/POST/PATCH/DELETE /api/schedules` (`docs/antiphon-api.md:295-302`) exist. `Schedules` table is empty on this box.

### 1.6 Identity today

- Brief header `[antiphon-task:id] role= tier= workspace=` (`DelegationReportFormatter.BuildBrief:158-162`, pointer `:490`) — no kind, no alias.
- `Dispatched` event: `Dispatched to agent 'task-x' (grok-4.6) in <dir>` (`AgentTaskDispatcher.cs:2376`).
- Completion note bits: `ModelLevelAliases.For(task.AgentKind, task.ModelLevel)` (`:362`) — the request, not the run.
- `AgentSession.EffectiveModelId`: set only when a model argument was applied (`AgentTuiLaunchResolver.ApplyModelArgument:469-480`); null on 271/277 Grok and 119/139 Codex sessions. The measured model is on `TurnEnd.Model` (`grok-4.6-build`) for normal turns only.
- `TokenUsages.ModelName` / `CostLedgerEntries.ModelName` exist for the workflow ledger; nothing on the delegation side names the kind.

---

## 2. Evidence

### 2.1 Grok 402 on this box (the local analogue)

| Session | Cwd | Error TurnEnds (seq) | Prompts typed after the first death | Task outcome |
|---|---|---|---|---|
| `235d92a2` | card-task-f0541477 | 216, 218, 220 (2026-08-23 10:37) | 2 background-task reminders | Failed "session is Stopped (KilledByRequest)" 11:21 |
| `cadfab16` | card-task-33e15f59 | 240 (10:38) | — | **Succeeded** 11:08 on mid-turn narration ("I'll start by reading the full brief…") |
| `09bca313` | card-task-356cdd89 | 298, 300, 302, 304 (08-26 20:04–20:21) | 1 reminder + 2 refinements from Antiphon | Failed "no report… 120s grace" 20:24 |
| `f20dc550` | card-task-9a92bb0c | 2 (20:26) | — | Failed same reason 20:28 |
| `4f354406` | card-task-b72da3bb | 2 (20:29) | — | Failed same reason 20:32 |

Zero `ApiErrorTurnDied` incidents, zero `ApiErrorRecoveries` rows, zero holds for Grok in the whole record (`ApiErrorRecoveries` by kind: Claude 24 rows, Codex 150, Grok 0). Every one of these sessions was a pool delegate with no channel binding, so nobody was waiting on a chat — the same stub on a channel-bound agent is the card's incident.

### 2.2 What the channel user sees today (Family, Telegram, 2026-09-01)

Ola's 19:22 message (`SessionQueuedMessages` seq 90, `SentAt` 19:23:36) got TurnEnds at 19:23:14/19:23:15 that dispatch did not route (a CARD-0233-class mismatch, not a wall); at 19:54:29 the TTL sweep wrote Critical `ChannelReplyLost` / `StaleTtl` and sent `[Antiphon] A reply this chat was owed was never delivered: no turn matching the message completed within 30 minutes.` Thirteen minutes later the same session hit the Claude session-limit stub (seq 1684-1685, `rate_limit`/429), which **was** stamped, adopted (`WallModelPaused`), incident 22 written, and a hold created — the whole CARD-0022 path working for Claude. The delta between the two providers is the stamp.

### 2.3 Provider vocabulary (from `~/.grok/bin/grok.exe`, 1.0.13)

- Hook error classes: `rate_limit`, `authentication_failed`, `invalid_request`, `server_error` — Claude's vocabulary, so `ApiErrorClassifier`'s class switch applies unchanged when Grok's reason phrase is mapped onto it.
- Capacity phrases Grok itself matches for its "credit limit upsell": `spending-limit`, `spending limit`, `out of credits`, `usage balance exhausted`, `usage limit reached`; compaction text "out of credits or over your spending limit. Add credits and retry."
- Scheduler: `x.ai/scheduler/delete`, "Maximum 50 scheduled tasks can be active at once".

---

## 3. Design

### D1. Carriage — stamp the Grok error TurnEnd the Codex way (runner)

In `GrokTranscriptNormalizer.FromTurnCompleted`, when `stop_reason == "error"`:

- Parse `agent_result` with one regex: `^API error \(status (\d{3})\s*([^)]*)\):\s*(.*)$`. `ApiErrorStatus` = group 1. `ApiErrorClass` = the reason phrase snake_cased (`payment_required`, `permission_denied`, `too_many_requests`, `internal_server_error`, `unauthorized`, `bad_request`, `not_found`) — the raw provider fact, not a classification. `Text` = group 3 bounded to 600 chars (Codex's `MaxApiErrorDiagnosticChars`), never the whole payload. `IsApiError = true`. No match → `IsApiError = true`, class null, status null, `Text` = bounded `agent_result` (the classifier's `Unknown` arm is the designed fallback).
- No synthetic `AssistantText` stub. `AgentTaskReplyService.ExtractMarkedTurnAsync` already falls back to `end.Text` when no stub text row exists (`:1696-1703`, "Codex stamps the diagnostic on the TurnEnd itself"); `ChannelReplyDispatcher.ExtractTurnResponseAsync` excludes any `IsApiErrorStub` row (`:1083-1095`) and only joins `AssistantText` (`:1100-1120`), so the diagnostic can never reach a chat.
- `retry_state` rows stay skipped. The `failed` one duplicates `agent_result`; the `retrying` ones are Grok's internal ladder and must not become stubs (a stub per retry would park the session at `WallDeathCap` on a transient 500 that Grok then recovers from). Document both in the class remarks.
- `TranscriptKinds.StopReasons.Error = "error"`; `AgentSessionRuntime.IsTurnBoundary` accepts it. Claude never emits it (its stubs are `stop_sequence`), Codex synthesizes `end_turn`, so the arm is Grok-only, like the `cancelled` arm above it. Effect: the dispatchers and `AgentTaskReplyService.OnTurnEndAsync` run on the tailer poll, not on the next 60 s sweep, so the notice in D2 goes out in seconds. `IsReportBoundary` is unchanged (`error` stays a report boundary; the stub arm is checked first).
- `ProviderContractCatalog.Grok.UsageLimitSignal` → `Supported`, `StructuralField`, `StatesResetTime: false`, reason naming `agent_result` and the 402 measurement.

### D2. The channel notice — at withhold time, once, channel-safe, then stop typing

`ChannelReplyDispatcher.DispatchAsync`, inside the existing `if (containsApiErrorStub)` branch (`:243`):

1. Adopt: `ApiErrorRecoveryService.EnsureAdoptedAsync(sessionId, stub.Sequence, …, raiseIncident: true)` — the same idempotent marker the task path uses (`AgentTaskReplyService.cs:913`). The row's `Classification` + `ResolvedReason` decide the arm.
2. **Retryable** (`Transient`, `Unknown` not exhausted, `Wall`/SessionLimit with a resume scheduled): unchanged — withhold, keep owed, the resumed turn answers, TTL is the backstop. Optional, off by default: a `⏳` Progress-kind "provider hiccup, retrying" note (`ChannelBridge:NotifyTransientErrors`). Not in v1.
3. **Terminal** (`WallModelPaused`, `WallParked`, `NeedsHuman`, `UnknownExhausted`, `WallUnparsed`): 
   - Send one notice per distinct `ConversationKey` among the open correlations, through `ChatChannelService.SendAsync` with a new `ChannelSendOptions.ReplyHandle` so Slack keeps it in the thread (parity with `:300`). Text, built by a pure `ProviderCapacityNotice.Format(kind, alias, status, reasonPhrase, fallbackDeclared)`:
     `⚠️ I can't answer right now: the Grok provider refused the request (HTTP 402 Payment Required — usage balance exhausted). Your message is kept.` followed by either `A fallback (Codex gpt-5.6-terra) is taking over; the next reply comes from it.` or `Someone needs to restore capacity or clear the hold before I can continue.` Channel-safe means: provider kind + alias + status + reason phrase + the bounded provider detail after a `SecretScrubber`-style pass (drop anything shaped like a key, bearer token or URL); never the raw `agent_result` verbatim, never file paths.
   - Settle those rows (`SettleAsync`, `:386`) so the TTL sweep cannot send the contradictory "no turn completed" notice 30 minutes later. New `LossReason.ProviderCapacity` is recorded on the Critical `ChannelReplyLost` incident raised **now** (not at TTL) with the classification and hold in the message — the "one of two states" invariant from CARD-0067 holds: every correlation still ends in a published reply or a Critical incident.
   - When a fallback is declared (D4): re-enqueue a **Pending** copy of each settled row's `Body` with the same `ConversationKey` and `Origin = Channel` (`SessionMessageQueueService.EnqueueAsync`, the bridge's own call shape at `ChannelBridgeService.cs:183-186`) so the relaunch carry-over moves it and the fallback session answers it by the normal prompt match. Dedupe by `ContentDigest`.
4. **Stop typing into the dead session.** `SessionMessageQueueService` flush (`:343`, `:2752`, the `IsWorkingAsync` gates) gains a capacity gate: if the session has an unresolved-terminal `ApiErrorRecoveries` row newer than its last `UserPrompt` (reasons above) and no fallback relaunch is in progress, `Channel`- and `Scheduled`-origin rows stay Pending (log once, `Held` note on the row's `NoteHeader`); `Supervision`/`Ui` rows still deliver (an operator's typed `/login` or a human resume is exactly what un-sticks it). Pending rows carry over on the next launch (`AgentControlService.cs:333-345`).
5. The bridge door (§1.3): `EnsureAgentSessionAsync` catches `ModelDisabledException` — when the agent has a declared fallback the exception cannot occur (D4 makes `StartAsync` succeed); when it has none, raise a deduped Critical `ChannelReplyLost`/`ProviderCapacity` incident on the agent and send the same notice to the message's conversation, then return null (today's drop path with `RaiseBridgeDropAlertAsync`). A held agent is not a consume-loop crash.
6. Supervisor: `StartFailure` text for a `ModelDisabledException` says `held: grok-4.6 is disabled (per-model cap); no fallback declared` — one incident per hold episode, not one per backoff rung (dedupe on `(agent, hold.Id)`).

### D3. Classification and the hold — extend `ApiErrorClassifier`, reuse `ApplyWallAsync`

- `ApiErrorClassifier.Classify`: after the class switch, status arms `402 → Wall`; `403 → Wall` **iff** `UsageLimitWallParser.LooksLikeCapacity(text)` (the §2.3 vocabulary plus the card's "exhausted credits" / "monthly spending limit"), else `NeedsHuman`; `400 → NeedsHuman` (invalid_request — measured on Codex as an unsupported model; retrying cannot fix it). 401 and ≥500 unchanged. The text parameter finally has a use; the doc comment's "unused today" line is updated. Classes stay four — a fifth `Capacity` class would need a parallel recovery arm for behaviour that is byte-for-byte ModelCap.
- `UsageLimitWallParser.Parse`: no reset in Grok text → `ModelCap` on `fallbackAlias`, which `ResolveFallbackAliasAsync` (`:393-435`) already resolves to `grok-4.6` from `session.EffectiveModelId` → `agent.ModelId` → task level → `ModelLevelAliases.ForGrok`. `FormatReason` gains a capacity form: `grok-4.6 provider capacity (HTTP 402 Payment Required: usage balance exhausted; no reset stated)` so the hold row, the attention row and the 409 sentence say why.
- `ApplyWallAsync` writes the hold and resolves `WallModelPaused`; `WallDeathCap` (3) still parks after the third death, which the Grok burst (§1.1) reaches within seconds — acceptable, both are terminal and Critical when channel-bound. The `RaiseAdoptIncidentAsync` message for `WallModelPaused` names kind + alias + status (D5).
- Manual recovery is CARD-0309's: `model-availability.ps1 clear -Kind Grok -Model grok-4.6` after topping up. No probe, no polling of `/usage` (Grok's is Degraded, `GrokUsageOverlayCanaryTests`).

### D4. Declared fallback — one agent, two launch profiles, the hold decides

**Entity:** `Agent.FallbackTuiProfileId` (Guid?), `Agent.FallbackModelId` (string?), `Agent.FallbackModelLevel` (AgentModelLevel?). `AgentSession.LaunchedOnFallback` (bool, default false) and `AgentSession.LaunchTuiProfileId` (Guid?) record what actually ran. `Agent.Kind` keeps its invariant against the **primary** profile (`Agent.cs:100-125`); the session's `AgentKind` is the launched kind.

**PATCH:** `UpdateAgentRequest` (`AgentDtos.cs:231`) gains the three fields; `AgentService.UpdateAsync` validates them with the same rules the primary passes through `ApplyTuiSelectionAsync` (`:489`, `:608`): `ValidateSessionBackendPairing(agent.SessionBackend, fallbackProfile.Kind)` and `RemoteControlPolicy.Require(fallbackProfile.Kind, agent.RemoteControlEnabled, …)` — a Herdr or RC-enabled agent cannot declare a fallback its lane refuses (422). `fallbackModelId` must normalize to an alias `ModelLevelAliases` knows for that kind. Exposed on `AgentDetailDto`; `scripts/agent.ps1`-style verb if one exists, otherwise documented as a PATCH.

**Launch selection:** a `LaunchProfileSelection(profileId, modelId, level, isFallback)` threaded into `AgentSessionLaunchComposer.ComposeForAgentAsync` / `PeekProfileKindAsync` (`:52`, `:103`) and `AgentLaunchResolution.ResolveForAgentAsync` (`AgentTuiLaunchResolver.cs:32`) instead of each reading `agent.TuiProfileId`/`ModelId`/`ModelLevel` directly. `AgentControlService.StartAsync` (`:118-136`): peek the primary kind, resolve the primary alias, `GetActiveHoldAsync`; if held **and** a fallback is declared **and** the fallback alias is not held, select the fallback and continue; if held and no fallback (or fallback also held) throw `ModelDisabledException` as today. A fallback launch is always **fresh** (a Grok `--resume` id is meaningless to Codex); `FindResumableSessionAsync` is skipped and the Pending carry-over (`:333-345`) runs. Bootstrap/restart notes follow the launched kind (`isClaudeCode` gate at `:232`).

**Live reroute for a channel-bound agent:** in D2 step 3, when the agent is channel-bound or AlwaysOn and a fallback is declared and not held: `StopAsync` the dead session and `StartAsync(Fresh: true)`. The kill is justified by the CARD-0072 D4 standard — a structural stub plus a hold is proof the session cannot complete a turn — not by silence; without a declared fallback nothing is killed (a human may top up credits and the same session resumes on the next prompt). Delegate sessions are **not** rerouted here: CARD-0071 fails the task, CARD-0090's chain re-walk owns the next attempt.

**Visibility (never silent):** `AgentIncidentKind.ProviderFallbackActivated = 45` (Warning; Critical never — the human chose this) with primary and fallback kind/alias and the hold reason; `AttentionKind.RunningOnFallback = 27` (Warning) projected while `session.LaunchedOnFallback` on the agent's live session, action `OpenAgent`; agent event / `AgentChanged` publish. The channel notice's second sentence names the fallback.

**Return to primary:** by construction on the next launch — `StartAsync` selects the primary whenever it is not held. Auto-return while the fallback session is alive is an idle restart (the `RcRestart` precedent, kind 9): `ProviderFallback:AutoReturn` (default **false** in v1) with `AutoReturnIdleMinutes` (10); when on, the supervisor's minute pass restarts an idle fallback session once the primary hold has cleared, recording `ProviderFallbackReturned` (same kind 45, message says returned). Off by default because a mid-conversation provider swap changes voice; the operator turns it on per deployment.

**What it does not do:** pick a kind Antiphon likes (no fallback declared = refuse + notify, exactly CARD-0309's rule); fleet-wide fallback; per-channel fallback (the unit of identity — cwd, preamble, memory, bindings — is the agent); delegate reroute (CARD-0090).

### D5. Identity in dispatch and completion summaries

- `Dispatched` event detail: `Dispatched to agent 'task-x' (Grok grok-4.6) in <dir>`; on a fallback launch of a standing agent the agent event says `(Codex gpt-5.6-terra, fallback for Grok grok-4.6)`.
- `BuildCompletionNote` bit `:362` becomes `Grok/grok-4.6` and appends `→ grok-4.6-build` when the session's measured model (last `TurnEnd.Model`, else `EffectiveModelId`) differs from the alias, and `fallback` when `LaunchedOnFallback`. `DelegationUnitTests` pin the new bit; the note width budget (`NoteShrunk`) is unaffected by ~20 chars.
- Brief header: **unchanged** — the delegate knows its own kind, the header is parsed by marker tests, and the pointer form duplicates it. The kind lands on the task DTO and events, which is where an auditor reads.
- `ApiErrorTurnDied`, `WallModelPaused`, `ChannelReplyLost/ProviderCapacity` and hold `Reason` all lead with `{Kind} {alias}`.
- `AgentTuiLaunchResolver.ApplyModelArgument` (`:469-480`): stamp `EffectiveModelId` with `ModelLevelAliases.ForLaunch(kind, level)` whenever no explicit model was applied, so no session is null again; this is what makes "which provider answered" answerable from `AgentSessions` alone.
- Chat replies carry **no** per-message provider suffix (rejected, §5).

### D6. Schedules — runbook, one bundle sentence, one detector

- **Runbook** (docs, `docs/telegram-bot-ops.md` "Standing up a bot + agent" and `docs/orchestration-loop.md` §Scheduled prompts): a channel agent's recurring work is an Antiphon schedule, never the TUI's own scheduler. For the card's case: `pwsh -File scripts/schedule.ps1 new -Name "PredictionMarkets status" -Agent <agent> -Repeat Interval -EveryMinutes 30 -PromptFile <status-prompt.md>` (`WhenTargetDown` defaults to Queue for an AlwaysOn agent per CARD-0057 D3). The fire path already survives crash, fresh relaunch and D4's fallback launch because it targets the agent.
- **Bundle sentence** in `server/Bundles/delegate-basics.md` and the channel preamble presets: "Anything that must happen on a clock goes through `schedule.ps1`; a scheduled task created inside the TUI (Grok `/tasks`, any provider-owned reminder) dies with the process and is invisible to Antiphon."
- **Detector:** in `GrokTranscriptNormalizer.FromToolCall`, a `_meta["x.ai/tool"]` whose `namespace`/`name` contains `schedul` sets a new `TranscriptPart` flag → server raises `AgentIncidentKind.ProviderOwnedSchedule = 46` (Warning, deduped per session) with the tool input, and `AttentionKind.ProviderOwnedSchedule = 28` pointing at `schedule.ps1`. The tool name is unmeasured; an `[Explicit]` headed `GrokSchedulerCanaryTests` measures it (type "schedule a reminder in 2 minutes to say hi", read the `tool_call` row, kill the session). Until measured the substring match stands and the plan says so.
- No migration code: Grok keeps no readable scheduler state to migrate from.

---

## 4. Slices

| # | Slice | Files (owners to read first) | Tests | Est. |
|---|---|---|---|---|
| **S0** | Fixtures + fakegrok knob. `tests/Antiphon.SessionRunner.Tests/Fixtures/grok-api-error-402.jsonl` (the measured `retry_state` + `turn_completed` pair from `09bca313`, usage block intact) and `grok-api-error-403.jsonl` (card wording: `API error (status 403 Forbidden): permission-denied: team has exhausted its credits or reached its monthly spending limit` — **wording unverified**). `ANTIPHON_FAKE_API_ERROR=payment_required|permission_denied|server_error` (+ `_AFTER_TURNS`, FakeClaude's shape at `src/Antiphon.FakeClaude/Program.cs:56-62`) in `src/Antiphon.FakeGrok/Program.cs` writing both rows. | `FakeGrokContractTests` pins both modes | 1.5 h |
| **S1** | Carriage (D1). `GrokTranscriptNormalizer`, `SessionRunnerContracts.StopReasons`, `AgentSessionRuntime.IsTurnBoundary`, `ProviderContractCatalog.Grok`. Owners: `docs/session-runtime-invariants.md`, `docs/agent-kinds.md` §5. | `GrokTranscriptTailerTests`: 402 fixture → TurnEnd `IsApiError=true`, status 402, class `payment_required`, Text bounded, usage/model still carried; 403 fixture; unparseable `agent_result` → `IsApiError=true`, null class/status; `retrying` rows emit nothing; `end_turn` rows unchanged (`IsApiError` null). `AgentSessionRuntimeTests`: `error` is an idle boundary once (replay dedup), never for Claude. | 2 h |
| **S2** | Classifier + parser (D3). `ApiErrorClassifier`, `UsageLimitWallParser` (+`LooksLikeCapacity`, `FormatReason`), `ApiErrorRecoveryService.RaiseAdoptIncidentAsync` wording. | `ApiErrorClassifierTests`: 402 → Wall; 403+vocabulary → Wall; 403 plain → NeedsHuman; 400 → NeedsHuman; 429/5xx/401 unchanged. `ApiErrorRecoveryServiceTests`: `Grok_402_stub_writes_an_open_ended_hold_for_grok_4_6_and_never_enqueues` (positive control: Claude session-limit still schedules one resume). `ModelAvailabilityCreateTests`: Grok create 409 after the hold; Claude create 200. | 2 h |
| **S3** | Channel notice + stop typing + bridge/supervisor doors (D2). `ChannelReplyDispatcher` (withhold branch, `LossReason.ProviderCapacity`, `ProviderCapacityNotice`), `ChatChannelService`/`ChannelSendOptions.ReplyHandle`, `SessionMessageQueueService` capacity gate, `ChannelBridgeService.EnsureAgentSessionAsync`, `AgentSupervisorService` start-failure text. Owner: `docs/session-runtime-invariants.md` (CARD-0067/0071/0233 paragraphs). | `ChannelReplyDurabilityTests`: terminal stub → one notice per conversation with the reply handle, rows settled, Critical incident with `ProviderCapacity`, TTL sweep sends nothing later (positive control: `A_turn_killed_by_an_api_error_publishes_nothing_and_stays_owed` still holds for a Transient stub); notice text carries kind/alias/status and no raw detail beyond the bounded phrase. `SessionMessageQueue*Tests`: Channel/Scheduled rows stay Pending behind a terminal recovery row, Supervision rows deliver, Pending rows carry over on relaunch. `ChannelBridgeTests`: held agent, no fallback → notice + deduped incident + null session, consume loop does not throw (positive control: unheld agent routes). | 4–5 h |
| **S4** | Declared fallback (D4). Migration (3 Agent + 2 AgentSession columns), `UpdateAgentRequest`/`AgentService`, `LaunchProfileSelection` through `AgentSessionLaunchComposer`/`AgentTuiLaunchResolver`/`AgentControlService.StartAsync`, reroute call from S3, incident 45, attention 27, `ProviderFallback` settings, client: fallback fields on the agent form + attention visual. Owners: `docs/agent-kinds.md`, `docs/antiphon-api.md`, `docs/ai-agent-tui-configuration.md`. | `AgentControlServiceIntegrationTests`: primary held + fallback declared → session launched with fallback kind/profile, `LaunchedOnFallback=true`, incident 45, attention 27; primary held + no fallback → 409 unchanged; both held → 409; primary clear → primary (positive control); Herdr/RC pairing 422 on PATCH. `ChannelReplyDurabilityTests` end-to-end with fakegrok `payment_required` + a declared fakeclaude fallback: notice sent, Pending copy re-enqueued, fallback session answers, reply routed, old rows settled once. `attentionVisuals.test.ts` totality. | 8–10 h |
| **S5** | Identity (D5). `AgentTaskDispatcher` event detail, `DelegationReportFormatter.BuildCompletionNote`, incident/hold wording, `ApplyModelArgument` stamping, task DTO `agentKind` if absent. | `DelegationUnitTests` note bit; `AgentTuiLaunchResolver` tests: Grok/Codex sessions get `EffectiveModelId` without an explicit model; Claude with explicit model unchanged. | 2 h |
| **S6** | Schedules (D6). Docs + bundle sentence + detector flag in normalizer + incident 46 / attention 28 + `[Explicit]` canary. | `GrokTranscriptTailerTests` detector flag; `AttentionServiceTests` kind 28; canary opt-in. | 2 h |
| **S7** | Docs. `docs/agent-kinds.md` §5 Grok (error carriage, capacity vocabulary, fallback fields) and the CARD-0022 gotcha (`:407`, add Grok); `docs/session-runtime-invariants.md` (error TurnEnd is an idle boundary; terminal-class channel notice; queue capacity gate); `docs/antiphon-api.md` (agent fallback fields, incident kinds 45/46, attention 27/28, `ChannelSendOptions`); `docs/messaging/build-your-own-gateway.md:274` (a second server-composed notice); `docs/testing-and-build.md` (fakegrok knob); this plan's execution notes. | — | 1 h |

Order: **S0 → S1 → S2 → S3** (ships the incident fix: a user hears within seconds, nothing is typed into a dead session, the hold pauses the fleet) → **S4** → **S5** → **S6** → **S7**. S5 and S6 can land in either order after S4. Worktree per group; run the Pty tests sequentially with `Antiphon.Tests`.

---

## 5. Rejected alternatives

- **A `ProviderCapacityState` table or a fleet pause.** The hold is keyed `(Kind, Alias)` for exactly this (CARD-0022 D7 struck the singleton); the Grok 402 pauses `grok-4.6` and nothing else.
- **A fifth `ApiErrorClassification.Capacity`.** Its recovery would be `ApplyWallAsync`'s ModelCap arm verbatim; the class is a label, not a behaviour.
- **Routing the notice through `ChannelAlertRouter`.** Every `AlertMinSeverity` is null and filling one dumps every Critical into a family chat (CARD-0233 decision 2). Targeted `SendAsync` it is.
- **Letting CARD-0090's chain pick a fallback kind for a standing channel agent.** That is the silent reroute AGENTS.md and CARD-0309 forbid, and chains are delegate-task routing (pool rows, Worktree, Role policy); a standing agent's fallback is a launch-profile fact on that agent.
- **Swapping the Agent row's primary profile (what the live mitigation did).** It works, but it rewrites the operator's declaration, hides that a fallback is active, and return-to-primary becomes another hand edit. A session-level `LaunchedOnFallback` keeps the declaration and the audit intact.
- **Classifying from `retry_state` rows.** Grok's internal retry (`retrying`, `max_retries 15`) means `failed` is already terminal; a stub per attempt would park healthy sessions.
- **Text-matching the assistant stream for "permission-denied".** CARD-0072 D1: detection is structural; `agent_result` is a field. The vocabulary check applies only to a stub that already carries `IsApiError = true` and a 403.
- **A per-reply provider suffix in chat.** Humans in a family or Slack thread do not want a stamp on every line; incidents, events, `EffectiveModelId` and the one-time notice are the audit trail.
- **Auto-migrating Grok scheduled tasks.** No readable state file exists; the operator re-creates the one known 30-minute status schedule with `schedule.ps1`.
- **Killing the dead session when no fallback is declared.** A stall is never a kill; a stub plus a hold proves the *provider* is dead, and the same session resumes when a human tops up. Kill only to hand the work to a declared fallback.

---

## 6. Risks and open decisions

| Risk / decision | Disposition |
|---|---|
| xAI 403 is also a real permission error | Vocabulary gate → Wall; unmatched 403 → NeedsHuman. Both terminal, both hold-free? **No** — NeedsHuman writes no hold today (`BuildNewRowAsync` resolves before `ApplyWallAsync`). Decision for the caller: leave NeedsHuman hold-free (403 auth misconfig should not pause a healthy model) — recommended — or write an open-ended hold for any 403. |
| The 403 fixture wording is unverified | Pinned with the card's text and marked; the parser keys on `status 403` + vocabulary, not the sentence. Replace the fixture when the other deployment's `updates.jsonl` row is copied in. |
| `WallDeathCap` parks the Grok burst at the third death | Both `WallModelPaused` and `WallParked` are terminal for D2; the incident text names the cap. Acceptable. |
| Idle boundary on `error` flushes the next WhenIdle row into a dead session | Ordering in `FlushQueueOnIdleAsync`: channel dispatch (which adopts the stub) runs **before** the queue flush (`AgentSessionRuntime.cs:456-484`), so the capacity gate sees the recovery row on the same pass. Pin it. |
| Fallback kind ≠ agent kind breaks a consumer of `agent.Kind` | Audit list for S4: `RemoteControlPolicy`, `ValidateSessionBackendPairing`, `SubscriptionUsageKey.For(agent, k)`, pool claim (`IsPoolDelegate` only — standing agents are not pool rows), transcript tailer format (from `session.AgentKind` via `DefinitionName` — correct by construction), `ChannelPreamble` notes (`isClaudeCode` from the launched kind), `RunnerBuildIdentity` staleness check. Each gets a test or a one-line note. |
| Two Antiphon deployments; this plan cites this box's rows | Same code, same normalizer; the 402 and 403 differ only in status and phrase. |
| Slack thread targeting of the notice | `ChannelSendOptions.ReplyHandle` from the catalog row, parity with the main path. Telegram ignores it (handle == conversation id). |
| Secret leakage in the notice | Bounded phrase only; `SecretScrubber` pass; pinned by a test feeding a fake `agent_result` containing `Bearer xai-…` and a URL. |
| Return-to-primary voice change mid-conversation | `AutoReturn` default off; on by explicit setting. |

**Decisions that are the caller's:** (a) accept the declared-fallback shape (agent-level, human-declared, session-marked) over the profile-swap shape; (b) 403 without capacity wording = NeedsHuman with no hold (recommended) or hold anyway; (c) whether the Transient-class `⏳` progress note is wanted in v1 (recommended: no); (d) `AutoReturn` default (recommended: off).

---

## 7. Verification (for the Code passes)

```powershell
# runner-side carriage (S0/S1/S6)
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-card0281/ -- --treenode-filter "/*/*/GrokTranscriptTailerTests/*"
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0281/ -- --treenode-filter "/*/*/FakeGrokContractTests/*"
# server (S2–S5)
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0281/ -- --treenode-filter "/*/*/ApiErrorClassifierTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0281/ --no-build -- --treenode-filter "/*/*/ApiErrorRecoveryServiceTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0281/ --no-build -- --treenode-filter "/*/*/ChannelReplyDurabilityTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0281/ --no-build -- --treenode-filter "/*/*/ChannelBridgeTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0281/ --no-build -- --treenode-filter "/*/*/AgentControlServiceIntegrationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0281/ --no-build -- --treenode-filter "/*/*/ModelAvailability*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0281/ --no-build -- --treenode-filter "/*/*/DelegationUnitTests/*"
pwsh -File scripts/test-client.ps1
```

Forward slash on `OutputPath`; Pty tests never concurrent with `Antiphon.Tests`; delete every `bin-card0281` directory afterwards. Live proof after deploy: run fakegrok is not enough for the provider itself — the first real Grok 402/403 on a channel-bound agent should produce, in order: TurnEnd stamped (`IsApiError=true`, status), `ApiErrorRecoveries` row `WallModelPaused`, hold `(Grok, grok-4.6)`, the notice in the chat within one tailer poll, no further Channel rows typed, and (with a fallback declared) a fresh session on the fallback kind answering the re-enqueued prompt.

---

## 8. Execution notes and environment

- Nothing was built or run for this plan; all evidence is from the repository at `582da043`, read-only `psql` against the live database, `~/.grok/sessions/*/updates.jsonl`, and string extraction from `~/.grok/bin/grok.exe` 1.0.13.
- `docs/cards/` is untracked and generated; left alone. `bin-hangfirefix/` and `bin-card0208/` directories from other tasks still exist under several projects; not touched.
- The operator's live mitigation (PredictionMarkets on a Codex `gpt-5.6-terra` wrapper) is the manual form of D4 and stays in place until S4 ships; after S4 the intended state is primary Grok + declared Codex fallback, with `model-availability.ps1 clear -Kind Grok -Model grok-4.6` once credits are restored.
