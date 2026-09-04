# CARD-0360 + CARD-0361 — Grok transport-error death on a channel-bound session: carry the error, tell the human now, keep the boundary invariant unchanged

**Cards:** CARD-0360 "Grok retry_state / agent_result are dropped, so proxy connection failures never become transcript or Slack text" (In Progress, #37) and CARD-0361 "Grok turn_completed stop_reason=error is not a turn boundary, so Slack waits 30 minutes then ChannelReplyLost" (Backlog, #36). Filed together as two halves of one failure; they ship together.
**Task:** ef3aed70 (Plan, Frontier, Worktree) · **Date:** 2026-09-04 · **Code re-read on:** `48a6ecbf`
**Related:** CARD-0281 (Grok agent_result carriage, `stop_reason=error` boundary, capacity notice — shipped `4e11756d`, 2026-09-03 00:43Z), CARD-0071/0072 (API-error stub withhold, classifier, recovery ladder), CARD-0067/0233 (channel reply durability, TTL loss classes, targeted lost-reply notice), CARD-0338 (machine-turn plain text, ChannelReplyLost paging), CARD-0353/0312 (boot-turn provider stall), CARD-0083 S1 (Grok `retry_state` survey), CARD-0358 (stale canonical checkout after an out-of-band push).

This is a design document. No production code was written for it. Nothing was built or run beyond read-only queries against the local Postgres and local Grok session files.

---

## Verdict up front

1. **Both cards were diagnosed against pre-CARD-0281 code. Half of what they ask for is already on `master`.** `AgentSessionRuntime.IsTurnBoundary` already has the `stop_reason=error` arm (`server/Application/Services/AgentSessionRuntime.cs:360-364`, pinned by `AgentSessionRuntimeTests.Error_stop_reason_is_an_idle_boundary_stop_sequence_is_not`, `:435-446`), and `GrokTranscriptNormalizer.FromTurnCompleted` already parses `agent_result` on an error `turn_completed` into `IsApiError`/`ApiErrorClass`/`ApiErrorStatus`/`Text` on the TurnEnd (`src/Antiphon.SessionRunner/GrokTranscriptNormalizer.cs:371-380`, `:399-413`). The GitHub issues were opened 2026-09-04 09:20Z, after that commit, from a deployment this box does not run (a Slack-bound Grok agent behind an llm-key-proxy on port 10746 — the same origin as #25). This box's database has zero transport-error TurnEnd rows, and its 402 rows from 2026-09-03 22:14Z carry `IsApiError=true, payment_required, 402`, which proves the deployed server here already runs CARD-0281. **Do not re-add the boundary arm and do not re-parse `agent_result`.** The build slices below assume CARD-0281 is present; if the other deployment is behind, its first fix is `git pull` (CARD-0358).

2. **The residual failure is real and is one classification hop past CARD-0281.** Trace the measured shape through HEAD: 15–45 `retry_state` rows are skipped (`:133`), the error `turn_completed` becomes a TurnEnd with `IsApiError=true`, `ApiErrorClass=null`, `ApiErrorStatus=null` and the connection text as `Text` (the "no grammar match" branch, `:405-407`), the boundary arm fires, `ChannelReplyDispatcher.DispatchAsync` finds the stub (`ChannelReplyDispatcher.cs:1368-1379`) and enters `HandleApiErrorWithholdAsync` (`:438`), which adopts it through `ApiErrorClassifier.Classify(null, null, text)` → **`Unknown`** (`ApiErrorClassifier.cs:48`) → a retry-ladder row with `ResolvedAt == null` → the withhold logs "correlations stay owed, TTL as backstop" and returns (`:459-467`). **Nothing is sent to the chat.** Thirty minutes later `ClassifyTtlLossAsync` (`:640-674`) sees a TurnEnd and zero assistant chars and reports `TurnIncomplete`: "a matching prompt was recorded but no turn completed" (`:699-700`) — the sentence the card calls a lie. It is: the turn completed, at the transport layer.

3. **The provider error must NOT become AssistantText.** The cards suggest it because "there is text, dispatch it" looks like the shortest path. It is the wrong path, for five reasons verified in code: (a) an AssistantText row without `IsApiError` is published as the reply *and settles the correlation before the produce* (`:283-289` comment, CARD-0067 idempotency), which cancels any genuine later answer; (b) with `IsApiError` stamped it is withheld anyway (`TranscriptKinds.IsApiErrorStub`, `SessionRunnerContracts.cs:516-518`), so it buys nothing; (c) `AgentTaskReplyService` reads the last AssistantText as a delegate's final message/report candidate (CARD-0046); (d) `DispatchMachineTurnFollowUpAsync` would deliver it into chat for Scheduled/Check turns (`:1043-1120`); (e) `ClassifyTtlLossAsync` would count its chars and downgrade `TurnIncomplete` to `TurnUnmatched`. The diagnostic already lives on the TurnEnd (the Codex shape CARD-0281 chose); this plan keeps it there and makes the **withhold path speak**.

4. **Design: a transport-class death is terminal for the owed channel correlation, and every terminal withhold sends one class-appropriate notice.** The Grok normalizer stamps a structural class `transport` when `agent_result` (or, if blank, the last `retry_state` message) fails the `API error (status …)` grammar and matches transport vocabulary ("error sending request", "connection refused", "tcp connect", "dns error", "timed out", …). The classifier maps `transport` → `Transient` for the session's own ladder. The channel-side withhold gains two terminal outcomes beside CARD-0281's `ProviderCapacity`: **`ProviderTransport`** (immediate notice with the scrubbed error text, settle, Critical `ChannelReplyLost`, no TTL contradiction) and **`ProviderError`** (the same treatment when a Transient/Unknown recovery has already resolved terminally — parked or exhausted). Non-terminal withholds stay owed exactly as CARD-0071 designed, but the TTL sweep learns to say "the turn died on a provider error (…)" instead of "no turn completed".

5. **Why terminal, not "keep owed and let the resume answer".** A resumed turn's answer cannot reach the channel today for any provider: the resume is a `Supervision`-origin prompt (`SupervisionSettings.cs:205`), `DispatchAsync` attributes the resumed turn to *that* prompt (`TranscriptTurnWindow.FindOwningPromptAsync`, `:248`), `PromptsMatch` against the owed chat body fails (`:1420-1430`), and `DispatchMachineTurnFollowUpAsync` excludes `Supervision` by construction (`:1090-1096`, `ChannelBridgeSettings.cs:61-66` and its validator). So "stay owed for a resumed turn" is a 30-minute silence in every case, not a recovery. Fixing routing for resumed turns is a real capability with CARD-0233 blast radius; it is named as a follow-up card (§8) rather than smuggled in here. For the transport shape specifically, terminal is also *right*: Grok has already exhausted its own 15-attempt ladder against an unreachable endpoint, and a human restarting the proxy is the only recovery. The chat user is told that immediately and asked to resend.

6. **The boundary invariant is untouched.** This plan changes nothing in `IsTurnBoundary`, nothing in delivery confirmation (`PromptSubmissionMatch`, `VerifiedPromptSubmitter`, queue `Sent` stamping), and nothing in what counts as working/idle. §3 records, for the record the task asked for, why the existing `Error` arm is safe for Claude and Codex.

7. **One bounded addition the plan's own choice makes necessary.** Classifying `transport` as `Transient` exposes it to a loop that already exists on this box: each resume dies, the newer stub gets adopted as a fresh row, the old row resolves `Replaced`, and the fresh row fires again one minute later — measured overnight on Codex session `f07371c9` (12 rows, `usage_limit_exceeded`, classification Unknown, 00:58–01:24Z 2026-09-04). Against a dead proxy that is a resume every ~12 minutes forever, each burning Grok's 15-attempt ladder. S4 adds `TransientDeathCap` (3 consecutive Transient/Unknown deaths with no healthy turn between → `TransientParked`, Critical incident, no further resumes), the exact mirror of `WallDeathCap` (`ApiErrorRetrySchedule.cs:59-60`).

Estimated build: **S0–S3 ~7–9 h** (the incident fix), **S4 ~2 h**, **S5 ~1.5 h** (FakeGrok mode + contract pin), **S6 ~1 h** (docs). Sequential, one Worktree.

---

## 0. Scope note — where the incident is and is not

- **Not on this deployment.** `TranscriptEntries` has no `StopReason='error'` row in the last four days other than the 402 pair (sessions `571412d6`, `82e6845e`, 2026-09-03 22:14–22:15Z). No local `~/.grok/sessions` file contains "error sending request", "connection refused" or "tcp connect"; the 68-file corpus CARD-0083 surveyed has `retry_state` only in the 500-at-capacity (`retrying`, `max_retries: 15`) and 400/402 (`failed`) shapes. The exact `agent_result` text for a connection failure is therefore **card-quoted, not locally measured** — the fixture in S0 carries the card's `retry_state.reason` string and must say so, the same way CARD-0281 pinned the 403 wording.
- **The deployed server here runs CARD-0281.** Evidence: the 402 TurnEnd rows above are stamped, and two `ChannelReplyLost/ProviderCapacity` incidents (session `9558d35f`, telegram, 2026-09-03 13:40Z and 13:45Z) show the withhold-time notice path working. Nothing in this plan depends on a restart to start being true.
- **What the cards got right at HEAD:** `retry_state` is still dropped (both `retrying` and `failed`, `:40-43`, `:133`), the retry count is nowhere, and the connection text reaches nobody in the chat. What they got wrong at HEAD: the boundary arm exists; `agent_result` is carried; an `ApiErrorTurnDied` incident *is* raised at withhold time (`ApiErrorRecoveryService.RaiseAdoptIncidentAsync`, `:441`, Critical when channel-bound — but deduped to one per session forever by `:451-453`, so the second DM's death raises nothing).

---

## 1. What the code does today (verified at `48a6ecbf`)

### 1.1 Runner: `GrokTranscriptNormalizer`

| Row | Arm | Result |
|---|---|---|
| `retry_state` (`type: retrying`, `attempt`, `max_retries: 15`, `reason`) | `_ => []` (`:133`; rationale `:40-43`) | dropped; the storm is invisible in the transcript |
| `retry_state` (`type: failed`, `message`) | same | dropped (duplicates `agent_result` — true for 402/400, unverified for transport) |
| `turn_completed` `stop_reason: error` + `agent_result` matching `^API error \(status (\d{3})\s*([^)]*)\):\s*(.*)$` (`:70-72`) | `ReadApiError` (`:399-413`) | TurnEnd `IsApiError=true`, `ApiErrorStatus`, `ApiErrorClass` = snake-cased reason phrase, `Text` = bounded detail (600 chars, `:68`) |
| same, `agent_result` not matching the grammar | `:405-407` | TurnEnd `IsApiError=true`, class `null`, status `null`, `Text` = bounded raw text |
| same, `agent_result` blank/missing | `:402-403` | TurnEnd `IsApiError=true`, class/status/Text all `null` — nothing to show anyone |

`GrokTranscriptTailer.Publish` forwards all four fields (`GrokTranscriptTailer.cs:327`). Contract tests: `GrokTranscriptTailerTests` `:603-697` (402, 403, unparseable, retrying → nothing, failed → nothing, 600-char bound).

### 1.2 Server: boundary and dispatch

- `AgentSessionRuntime.ObserveTranscriptAsync` (`:248-296`): `actOnTurnBoundary = IsTurnBoundary(entry) && IsUnseenTurnBoundaryAsync(...)` (`:258`), then `FlushQueueOnIdleAsync` (`:268` → `:465`) which runs `ChannelReplyDispatcher.OnTurnEndAsync`, `ReviewReplyDispatcher`, `AgentTaskReplyService.OnTurnEndAsync`, then the queue flush — in that order, on purpose.
- `IsTurnBoundary` (`:351-365`): `TurnEnd ∧ end_turn` ∨ `TurnEnd ∧ cancelled` ∨ **`TurnEnd ∧ error`** ∨ interrupt marker. Replay/split dedup is `IsUnseenTurnBoundaryAsync` (`:384`), pinned for the error arm by `AgentSessionRuntimeTests` `:455-505`.
- `ChannelReplyDispatcher.DispatchAsync` (`:219`): returns before any withhold when no correlation is open (`:230-231`); owning prompt is CARD-0233's rule; `ExtractTurnResponseAsync` (`:1361-1385`) returns `containsApiErrorStub` from an AssistantText stub *or* an `IsApiError` TurnEnd in the window (`:1368-1379`); on a stub the whole turn is withheld (`:283-289`).
- `HandleApiErrorWithholdAsync` (`:438-489`): `FindApiErrorStubAsync` (`:547`) → `ApiErrorRecoveryService.EnsureAdoptedAsync(..., raiseIncident: true)` → if the recovery row is unresolved: log + return, rows stay owed (`:459-467`); if resolved (terminal): `ProviderCapacityNotice.Format` (`ProviderCapacityNotice.cs:13-35`: "the {provider} provider refused the request … Someone needs to restore capacity or clear the hold"), `NotifyCapacityAsync` (`:491`), `SettleAsync`, `ReportLostAsync(LossReason.ProviderCapacity)`.
- TTL sweep (`SweepStaleCorrelationsAsync`, `:204`; `ClassifyTtlLossAsync`, `:640-674`): `StaleTtl` (no prompt) / `TurnIncomplete` (no TurnEnd, **or** TurnEnd with zero assistant chars, `:670-671`) / `TurnUnmatched`; `ReportLostAsync` (`:686`) writes the Critical incident and, for every reason except `Unroutable` and `ProviderCapacity`, sends `LostReplyNoticePrefix + why` to the originating conversation (`:774-777`, `:790`). Note the `TurnUnmatched` arm counts a Claude AssistantText stub's chars as an answer, so a Claude API death that ages out is reported as "a turn completed (N chars) but the dispatcher did not route it" — the same lie in a different hat.
- `IncidentPageNotifier` pages DigestEnabled channels once per Critical kind in `DigestSettings.WakeOnIncidentKinds` (default `[ChannelReplyLost]`, `DigestSettings.cs:19`), skipping `FailureReason == "ProviderCapacity"` (`IncidentPageNotifier.cs:60`).

### 1.3 Server: classification and the ladder

- `ApiErrorClassifier.Classify` (`:24-50`): structural class first (`rate_limit`, `server_error`, `authentication_failed`, `model_not_found`), then status (429/402 Wall, 403 vocabulary-gated, 400/401 NeedsHuman, ≥500 Transient), else **Unknown**. No arm recognises a transport/connection error that carries no status. (Claude's connection drop arrives as `server_error`/no-status → Transient; Codex's `ReadError` stamps its own class. Only Grok lands here classless.)
- `ApiErrorRecoveryService.EnsureAdoptedAsync` (`:71`) / `BuildNewRowAsync` (`:284`): Transient and Unknown get `NextAttemptAt = now + 1 min` (`ApiErrorRetrySchedule.cs:17-18, :32-47`). `FireOneAsync` (`:202`) enqueues `TransientPrompt` `WhenIdle` with `Origin = Supervision`; a newer stub resolves the row `Replaced` (`:231-241`); `UnknownAttemptCap` (3) is per row (`:257-263`) and never trips when every row is replaced after one fire. `MaybeRaiseDeadTimeIncidentAsync` (`:528`) measures from the *current* row's `DetectedAt`, which the replacement resets, so it never fires in the loop either. `AdoptAsync` (`:125`) re-adopts any stamped TurnEnd inside `AdoptWindowMinutes` (180), so the loop does not need the dispatcher to keep going.
- `SessionMessageQueueService.ApplyCapacityHoldAsync` / `HasTerminalCapacityHoldAsync` (`:3384`, `:3420`): Channel/Scheduled rows are held only behind `WallModelPaused` / `WallParked` / `NeedsHuman` / `UnknownExhausted` / `WallUnparsed`.
- Delegate-task path (`AgentTaskReplyService.HandleApiErrorTurnAsync`, `:991`; `DeferApiErrorTurnAsync`, `:1081`): defers while the recovery row is open, fails the task with the class in the reason when it resolves. Untouched by this plan except that a `transport` class now reads as `Transient (transport)` instead of `Unknown (no error class)` in the failure text.

### 1.4 What is *not* a model reply, and what is

`BootReplyWatch.IsModelReply` (`:57-62`) counts AssistantText, Thinking, ToolCall, ToolResult **and TurnEnd** as the model having replied — deliberately, because two Codex sessions answered their boot prompt with an API-error TurnEnd in ~1 s and then sat correctly in the ladder. `TranscriptWorkingState.Classify` (`:35-70`) treats every non-housekeeping kind as activity. Any new mid-turn row kind Grok emits therefore has three lockstep working rules, the boot watch and the client renderer as blast radius; see §2.3 for why this plan emits none.

---

## 2. Decisions

### 2.1 Where the provider error text surfaces — on the TurnEnd, never as AssistantText

Already the case for the text itself (§1.1). What this plan adds is *carriage of the shape*, not a new row:

- **`ApiErrorClass = "transport"`** stamped by the Grok normalizer when the grammar does not match and the text matches the transport vocabulary. Constant lives beside `StopReasons` as `TranscriptKinds.ApiErrorClasses.Transport` (`SessionRunnerContracts.cs`, shared by runner and server). Fits the 60-char column (`AppDbContext.cs:1146`, `:1175`).
- **Vocabulary** (case-insensitive substring, applied only when the `API error (status …)` grammar did *not* match, so a "504 Gateway Timeout" stays a status-class error): `error sending request`, `connection refused`, `tcp connect error`, `connect error`, `failed to connect`, `dns error`, `failed to lookup address`, `connection reset`, `broken pipe`, `timed out`, `network unreachable`, `no route to host`, `os error`. Text that matches neither the grammar nor the vocabulary keeps class `null` → `Unknown`, the designed fallback.
- **Blank `agent_result` fallback:** if `agent_result` is missing or whitespace, the diagnostic falls back to the last `retry_state` row of the turn (`failed.message`, else the last `retrying.reason`), bounded the same 600 chars. This is CARD-0360's "persist last retry/error payload". The normalizer already keeps per-prompt state; add `LastRetryReason` and `RetryCount` to it, cleared on `turn_completed`.
- **Retry count** rides on the TurnEnd as a suffix to `Text`: `" [after N retries]"`, only when N > 0 (the 402 pair has no `retrying` rows, so its measured `Text` is byte-identical to today). Suffix, not prefix, so anything that anchors on `API error (status` still parses; S0 pins that `UsageLimitWallParser` ignores a trailing suffix.

### 2.2 The classifier and the ladder

- `ApiErrorClassifier.Classify`: `case "transport": return Transient;` (structural, before the status fallback). Unknown stays Unknown.
- **Why Transient and not NeedsHuman:** NeedsHuman would put the session behind `HasTerminalCapacityHoldAsync`, so every later chat message would sit `Pending` with `NoteHeader = "Held"` and *no notice to the human* until a StaleTtl loss at 30 minutes — the failure class this plan exists to remove. With Transient, each later message is typed, dies after Grok's ladder, and the human gets the transport notice again; when the proxy is back the next message simply works. The cost is the resume loop, which S4 bounds.

### 2.3 `retry_state` live visibility — out of scope, with the design named

Not emitted as Thinking (or any row) in this pass. Reasons: every emitted kind is activity to three lockstep working rules and a "model reply" to `BootReplyWatch` (§1.4); one row per retry is 15–45 rows of noise per turn and feeds CARD-0153's looping-rows stall detector; and the operator already has the live pane. What the operator gains from this plan post hoc is the retry count and the last reason on the TurnEnd, in the transcript, the incident text and the chat notice. **Follow-up card (§8):** emit exactly one `Thinking` row per prompt on the *first* `retrying` row ("Provider retry 1/15 — <reason>"), uuid = that row's `eventId`; measure the boot-watch and stall-detector interaction before shipping.

### 2.4 Channel-side: what a withhold sends, and when

`HandleApiErrorWithholdAsync` becomes class-aware. After `EnsureAdoptedAsync`:

| Recovery state after adoption | Class | Action |
|---|---|---|
| resolved (terminal) | Wall / NeedsHuman (`WallModelPaused`, `WallParked`, `WallUnparsed`, `NeedsHuman`) | unchanged: capacity notice, settle, `ProviderCapacity` |
| resolved (terminal) | `transport` — **new:** the adopt step resolves a transport row immediately when the channel correlation is the caller (see below) | **transport notice**, settle, `ProviderTransport` |
| resolved (terminal) | Transient/Unknown parked or exhausted (`TransientParked` from S4, `UnknownExhausted`) | **provider-error notice**, settle, `ProviderError` |
| unresolved (ladder scheduled) | Transient/Unknown, non-transport | unchanged: stay owed, log; the TTL now tells the truth (S3) |

*Transport is terminal for the correlation, not for the session.* The recovery row for a transport death still schedules the ladder (Transient) — the session keeps its own chance to recover for a later prompt — but the *correlation* is settled at withhold time. Implement this as: in the withhold, `if (stub.ApiErrorClass == Transport)` take the terminal branch regardless of `recovery.ResolvedAt`. No new column: settlement is `ChannelReplySettledAt` (CARD-0067), idempotent by construction; the notice is sent once because `open` is empty on every later re-trigger (`:230-231`).

**Notice wording** (new `ProviderCapacityNotice.FormatTransport(kind, alias, retryCount, detail)`), same scrub (`:62-75`: URLs, bearer tokens, key-shaped tokens → "…", 80-char clip), same `ReplyHandle` routing as CARD-0281's `NotifyCapacityAsync`:

> ⚠️ I can't answer right now: I couldn't reach the Grok model endpoint (connection error after 15 attempts — error sending request for url (…)). Your message was not answered — please send it again once the connection is restored.

The `[after N retries]` suffix is consumed into "after N attempts" and stripped from the detail. `FormatProviderError(kind, alias, status, phrase, detail)` for the parked/exhausted arm: "…the Grok provider kept failing (HTTP 500 Internal Server Error — …) and automatic retries are parked. Please send it again later."

**Incidents:** `ReportLostAsync` gains `LossReason.ProviderTransport` ("the model endpoint was unreachable (connection error)") and `LossReason.ProviderError` ("the turn died on a provider error ({class}{, HTTP n}) and no automatic resume answered"). Both are Critical `ChannelReplyLost` with `FailureReason` = the reason name, both skip the TTL follow-up notice (`:774-777` extended), and both **page** via `IncidentPageNotifier` — unlike `ProviderCapacity`, there is no hold incident telling the operator anything, and a dead local proxy is an operator problem. One config flip (`Digest:WakeOnIncidentKinds`) turns paging off if the user disagrees.

### 2.5 TTL truth for non-terminal deaths (all providers)

`ClassifyTtlLossAsync`: compute assistant chars **excluding stub rows** (`IsApiErrorStub`), and check the window for an API-error TurnEnd or AssistantText stub (the same query `ExtractTurnResponseAsync` runs, bounded to `(prompt.Sequence, nextPromptSeq)`). If present → `LossReason.ProviderError` carrying the stub's class/status/scrubbed text. This fixes both today's lies: Grok/Codex `TurnIncomplete` ("no turn completed") and Claude `TurnUnmatched` ("N chars but the dispatcher did not route it"). Settlement timing is unchanged — this is wording and `FailureReason` only.

### 2.6 `TransientDeathCap` (S4)

New `ApiErrorRecoverySettings.TransientDeathCap` (default 3) beside `WallDeathCap` (`SupervisionSettings.cs:189`). In `BuildNewRowAsync`, for a Transient or Unknown classification: `consecutive = count(ApiErrorRecoveries for the session with Classification ∈ {Transient, Unknown} ∧ StubSequence > lastHealthyEndSeq ∧ ResolvedReason ≠ Superseded)`, where `lastHealthyEndSeq = max(Sequence of TurnEnd rows with IsApiError ≠ true)` (a `cancelled` end counts as healthy — the session answered a keypress). If `consecutive + 1 ≥ cap` → `Resolve(row, now, ApiErrorRecoveryReasons.TransientParked)`, `NextAttemptAt = null`. `RaiseAdoptIncidentAsync` gets a `TransientParked` arm (Critical, `FailureReason = "TransientParked"`, "N consecutive Transient/Unknown API deaths with no healthy turn between; resumes are parked; the next healthy turn resets the streak"). **Not** added to `HasTerminalCapacityHoldAsync` — holding chat rows silently is the failure class §2.2 rejects; each later death sends its own notice.

The overnight Codex loop (§Verdict 7) is a second, separate defect this cap merely bounds: `usage_limit_exceeded` is a Codex structural class the classifier does not know, so a genuine usage wall runs the Transient ladder instead of writing a `ModelAvailabilityHold`. Follow-up card (§8), not this plan.

### 2.7 Interaction with #25 (CARD-0281), #30 (CARD-0338), CARD-0072

- **#25 / CARD-0281:** consumed, not changed. The 402/403 → Wall path, the capacity notice, `ProviderCapacity` and the queue hold keep their tests byte-for-byte. This plan adds parallel arms; the only shared edit is the `switch` in `ReportLostAsync` and the terminal-branch dispatch in `HandleApiErrorWithholdAsync`. CARD-0281 S4–S7 (declared fallback, identity, schedules, docs) remain unshipped; S6 here writes the invariants entry CARD-0281 S7 never landed.
- **#30 / CARD-0338:** disjoint. Machine-turn plain text is `Delegation/Check/Scheduled`-origin turns with real AssistantText; every path in that dispatcher returns on `containsApiErrorStub` (`:1072`) and stays that way. No API-error text ever rides the machine-turn path.
- **CARD-0072 / `ApiErrorTurnDied`:** the card's claim that these turns "cannot be adopted" is pre-CARD-0281; at HEAD they are adopted as Unknown. This plan changes the classification (`transport` → Transient), adds `TransientParked`, and leaves the ladder, the prompts and the incident dedup as they are. It does not change the delegate-task defer/fail arm.

---

## 3. The boundary invariant — why nothing here widens it

`IsTurnBoundary` is shared by every provider's turn ending. The `Error` arm CARD-0281 added is safe for the other two, and this plan relies on that reasoning rather than re-deriving a new set:

- **Claude** copies `stop_reason` verbatim from the API record (`TranscriptNormalizer.cs:98`). The Anthropic API's vocabulary is `end_turn`, `max_tokens`, `stop_sequence`, `tool_use` (plus `pause_turn`/`refusal`); Claude Code writes API deaths as an AssistantText stub with `error` as a *separate* field (`:108`) and a `stop_sequence` TurnEnd. All 23 stubs in the CARD-0072 sweep have that shape; `"stop_sequence"` is pinned as NOT a boundary (`AgentSessionRuntimeTests:445-446`). The arm cannot fire on a Claude row.
- **Codex** has no `stop_reason`; its normalizer synthesizes `end_turn` on every `task_complete`, error or not (`CodexTranscriptNormalizer.cs:44`, `:336`). The arm cannot fire on a Codex row.
- **Grok** emits `error` only on an API death (0 occurrences on `end_turn`/`cancelled` across the CARD-0281 corpus), where the turn is provably over: no further chunk follows, and Grok's own retry ladder has already exhausted. Treating it as idle is the *same* judgement `BootReplyWatch` and `TranscriptWorkingState` already make (any TurnEnd ends the turn); without the arm the two disagree and WhenIdle rows strand.
- `IsReportBoundary` (`SessionRunnerContracts.cs:336-337`) is a different predicate (everything but `cancelled`) and is not touched.
- **Delivery proof is untouched.** No change to `PromptSubmissionMatch`, `VerifiedPromptSubmitter`, `LastDeliveryBaselineSequence`, `DeliveryVerdict`, or the LF + bracketed paste + Enter contract. The transport notice goes *out* on the channel producer, never *into* a composer.

---

## 4. Slices

Sequential, one Worktree, each slice green before the next. Build with `--property:OutputPath=bin-t/` (forward slash) while daemons hold `bin/`; delete `bin-t` directories before finishing.

### S0 — Pin the measured shape (tests first, red)

- Fixture `tests/Antiphon.SessionRunner.Tests/Fixtures/grok-transport-error.jsonl`: `user_message_chunk`; 15 `retry_state` `type: retrying` rows `attempt: 1..15, max_retries: 15, reason: "error sending request for url (http://localhost:10746/v1/chat/completions)"` (card-quoted; header comment says so); one `retry_state` `type: failed` with the same text as `message`; `turn_completed` `stop_reason: error`, `agent_result` = the same text, `prompt_id`, `usage` absent. A second variant with `agent_result` omitted.
- `GrokTranscriptTailerTests`: `Transport_fixture_stamps_transport_class_no_status_and_retry_count` (exactly UserPrompt + TurnEnd; `IsApiError=true`; `ApiErrorClass="transport"`; `ApiErrorStatus=null`; `Text` contains the quoted reason and ends `[after 15 retries]`; **no** AssistantText, **no** Thinking); `Blank_agent_result_falls_back_to_the_last_retry_state_message`; `Status_bearing_agent_result_never_gets_the_transport_class` (a `504 Gateway Timeout` grammar row → class `gateway_timeout`, status 504); `Error_402_fixture_text_is_unchanged_by_the_retry_suffix` (no `retrying` rows → no suffix; existing 402 assertions untouched).
- `ApiErrorClassifierTests`: `Transport_class_is_Transient`; `Classless_statusless_text_stays_Unknown`.
- `UsageLimitWallParserTests`: a trailing ` [after 3 retries]` on a 402 text leaves status/phrase/detail parsing unchanged.

### S1 — Runner carriage + classifier (turns S0 green)

- `SessionRunnerContracts.cs`: `TranscriptKinds.ApiErrorClasses.Transport = "transport"` + the vocabulary list as `TransportVocabulary` next to it (runner and server share one list).
- `GrokTranscriptNormalizer`: per-prompt `RetryCount` / `LastRetryReason` accumulated from `retry_state` rows (still emitting `[]`); `ReadApiError` gains the transport arm and the blank-result fallback; `FromTurnCompleted` appends the suffix and clears the counters. Update the class remarks (`:33-43`).
- `ApiErrorClassifier`: the `transport` case.
- Verify: `dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-t/ -- --treenode-filter "/*/*/GrokTranscriptTailerTests/*"`; `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-t/ -- --treenode-filter "/*/*/ApiErrorClassifierTests/*"`.

### S2 — Channel withhold speaks (the required positive control)

- `ProviderCapacityNotice.FormatTransport` / `FormatProviderError`; `LossReason.ProviderTransport` / `ProviderError`; `ReportLostAsync` `why` arms and the TTL-notice skip; `IncidentPageNotifier` unchanged (they page).
- `HandleApiErrorWithholdAsync`: class-aware terminal dispatch per §2.4, including the transport-is-terminal-for-the-correlation rule.
- Tests in `ChannelReplyDurabilityTests` (harness: `BridgeQueueHarness`, `InsertTranscriptEntryAsync(kind, text, stopReason, isApiError, apiErrorClass, apiErrorStatus)`, `h.Messaging.SentReplies`):
  - **`A_grok_transport_death_sends_the_error_now_settles_and_skips_ttl`** — Grok session, bound channel, seeded correlation, UserPrompt + TurnEnd(`error`, `IsApiError`, `transport`, the quoted text + suffix) → `OnTurnEndAsync` → exactly one `SentReply` to the conversation containing "couldn't reach", "Grok", "after 15 attempts", no `http://`, `ReplyHandle == chatId`; `ChannelReplySettledAt` set; `PendingCountAsync == 0`; one `ChannelReplyLost` incident `FailureReason == "ProviderTransport"` Critical; an `ApiErrorRecovery` row `Classification == Transient` with `NextAttemptAt` set; the TTL sweep afterwards sends nothing.
  - **`The_measured_transport_fixture_dispatches_the_error_through_the_runtime`** — the end-to-end control the task asked for: normalize `grok-transport-error.jsonl` with the real `GrokTranscriptNormalizer`, map the parts to `SessionRunnerTranscriptEvent`s (the mapping `GrokTranscriptTailer.Publish` does, `:318-327`), feed them as one runtime batch the way `A_runtime_batch_redispatches_a_late_confirmed_channel_reply_once` does, and assert the same notice. This exercises normalizer → `IsTurnBoundary` → `FlushQueueOnIdleAsync` → withhold → producer with no seeded rows.
  - **`A_second_transport_death_on_the_same_session_notifies_again`** — the second DM in the card. Also asserts the `ApiErrorTurnDied` dedup (`:451-453`) does not suppress the *channel* notice.
  - **`A_grok_transient_5xx_death_stays_owed_and_sends_nothing_at_withhold`** — negative control that non-transport Transient keeps CARD-0071 behaviour.
  - Existing CARD-0071/0281 tests (`:403`, `:430`, `:452`, `:477`, `:593`, `:658`) must pass unchanged.
- Verify: `--treenode-filter "/*/*/ChannelReplyDurabilityTests/*"` and `"/*/*/ProviderCapacityNoticeTests/*"`.

### S3 — TTL truth

- `ClassifyTtlLossAsync` per §2.5. Tests: `Ttl_after_a_grok_transient_death_says_provider_error_not_no_turn_completed`; `Ttl_after_a_claude_stub_says_provider_error_not_unmatched` (AssistantText stub + `stop_sequence` TurnEnd); existing `Ttl_with_*` tests (`:509-590`) unchanged.

### S4 — `TransientDeathCap`

- Setting, count, `TransientParked` reason, incident arm, withhold `ProviderError` path. Tests in `ApiErrorRecoveryServiceTests`: three consecutive Transient adoptions → third row resolves `TransientParked`, no `EnqueueAsync`; a healthy `end_turn` between deaths resets the streak; Wall rows are not counted; `ChannelReplyDurabilityTests.A_parked_transient_death_sends_the_provider_error_notice_and_settles`.

### S5 — FakeGrok mode + contract pin

- `ANTIPHON_FAKE_API_ERROR=transport` (`Program.cs:209`, `ApiErrorMessage` `:996`) with `ANTIPHON_FAKE_API_ERROR_RETRIES` (default 15) making `AppendApiErrorTurn` write the `retrying` rows before `failed` + `turn_completed`. `FakeGrokContractTests.An_armed_transport_turn_writes_the_retry_storm_then_the_error_pair`. Optional capstone (headed, pty lane): a `SessionMessageQueueGrokPtyIntegrationTests`-style test that binds a channel, arms transport, sends one message, and asserts the notice — run once; not required for the slice to be green.

### S6 — Docs and cards

- `docs/session-runtime-invariants.md`: one entry covering CARD-0281 + 0360 + 0361: `stop_reason=error` is an idle boundary and why it is provider-safe; the diagnostic lives on the TurnEnd and is never AssistantText (with the five reasons); the withhold outcomes table; resumed-turn answers do not route to the channel (the follow-up card's name); the three lockstep rules and the boot watch are why no retry row is emitted.
- `docs/agent-kinds.md` §5 Grok: the error carriage paragraph (402/403/transport, retry count suffix, `retry_state` dropped by design).
- `ProviderContractCatalog` Grok `UsageLimitSignal` text (`:122-125`): add the transport shape sentence.
- Cards: note on CARD-0361 that item 1 shipped in CARD-0281 (`4e11756d`) and item 2 is deliberately the TurnEnd diagnostic, not AssistantText; both cards close on the S2 commit.

---

## 5. Verification design

**Positive controls** (each must be red before its slice and green after; "red" here means the assertion fails on `48a6ecbf`, verified by running it at the base commit, not assumed):

| # | Control | Proves | Slice |
|---|---|---|---|
| V1 | `Transport_fixture_stamps_transport_class_no_status_and_retry_count` | the measured shape (15 `retrying` + `failed` + error `turn_completed`) yields one TurnEnd with `transport`, the text and the count, and no assistant row | S0/S1 |
| V2 | `Classify("transport", null, text) == Transient` and `Classify(null, null, "error sending request…") == Unknown` | vocabulary lives in the normalizer, the classifier keys on structure | S0/S1 |
| V3 | `A_grok_transport_death_sends_the_error_now_settles_and_skips_ttl` | the chat gets the error text at turn end, not a TTL notice | S2 |
| V4 | `The_measured_transport_fixture_dispatches_the_error_through_the_runtime` | the whole chain from raw `updates.jsonl` rows to the producer, with the *existing* boundary arm | S2 |
| V5 | `Ttl_after_a_grok_transient_death_says_provider_error_not_no_turn_completed` | the 30-minute sentence is true for the shapes that still age out | S3 |
| V6 | `Three_consecutive_transient_deaths_park_the_ladder` | the resume loop is bounded | S4 |
| V7 | `An_armed_transport_turn_writes_the_retry_storm_then_the_error_pair` | FakeGrok mirrors the shape for future E2E | S5 |

**Negative controls / invariants that must not move:** `AgentSessionRuntimeTests.Error_stop_reason_is_an_idle_boundary_stop_sequence_is_not` and `:455-505` (boundary set unchanged, dedup unchanged); `ChannelReplyDurabilityTests` `:403/:430/:452/:477` (CARD-0071 withhold + stays-owed for non-terminal), `:593/:658` (CARD-0281 402 notice, scrub); `GrokTranscriptTailerTests` `:603-697`; `SessionMessageQueueServiceTests` capacity-hold tests (S4 must not add `TransientParked` to the hold list); `IncidentPageNotifierTests.ProviderCapacity_is_not_pinged`; `FakeGrokContractTests.An_armed_payment_required_turn_writes_the_measured_error_pair`.

**Run plan:** full `Antiphon.SessionRunner.Tests` once after S1; `Antiphon.Tests` chunked by namespace once after S4 (`--treenode-filter "/*/Antiphon.Tests.Application/*/*"`); targeted classes after each slice. Any pre-existing red is confirmed at the base commit by re-running the named tests there, not the assembly.

**Manual smoke (optional, other deployment):** the card's own repro — bind a Grok agent through the local proxy, stop the proxy, send one message. Expected within seconds of Grok's ladder ending: the transport notice in the chat, a Critical `ChannelReplyLost/ProviderTransport` incident, an `ApiErrorTurnDied` incident, a Transient recovery row, and **no** notice at 30 minutes.

---

## 6. Risks and limits

- **The transport text is card-quoted.** If the real `agent_result` differs from the `retry_state.reason` wording (e.g. reqwest's longer `error sending request for url (…): error trying to connect: tcp connect error: …`), the vocabulary still matches on `error sending request`/`tcp connect`; if it is something else entirely, the row stays `Unknown` and today's behaviour (plus S3's truthful TTL) applies. Ask the other deployment for one raw row and add it to the fixture when it arrives.
- **Two notices per chat message worst case:** the transport notice at withhold, then nothing (settled). A *later* message that dies gets its own notice. No path sends both a transport notice and a TTL notice for one correlation.
- **Paging** on `ProviderTransport`/`ProviderError` is a deliberate difference from `ProviderCapacity`; if it is noisy on a flapping proxy, the cap in S4 limits it to one Critical `ApiErrorTurnDied` per session plus one `ChannelReplyLost` per chat message.
- **Resumed answers still do not reach the chat** for non-terminal Transient/Unknown deaths on every provider (§Verdict 5). This plan makes that period *truthfully reported*, not shorter.

---

## 7. Out of scope (explicit)

- Emitting `retry_state` rows live (§2.3) — follow-up.
- Routing a resumed turn's answer to the correlation its dead turn owed — follow-up.
- Codex `usage_limit_exceeded` classification → Wall/hold — follow-up (evidence: session `f07371c9`, 12 `Replaced` rows, 2026-09-04 00:58–01:24Z).
- `ApiErrorTurnDied` dedup that hides every death after the first on a session (`:451-453`) — note on the CARD-0072 card; S4's parked arm uses its own `FailureReason` so it is not swallowed by the dedup.
- CARD-0281 S4–S6 (declared fallback, identity, schedules).

## 8. Follow-up cards to file at settlement

1. **Grok `retry_state` live visibility** — one Thinking row per prompt on the first `retrying` row; measure `BootReplyWatch`/`TaskProgressStalled` interaction first.
2. **Resumed-turn answers never route to the owed channel correlation** — `Supervision`-origin resume opens a new turn; `DispatchAsync` matches it to nothing; CARD-0071's "a resumed turn's real answer routes by the same stored prompt match" is false at HEAD for all providers. Design: a resume row carries the dead turn's owning prompt sequence and the dispatcher matches the owed correlation through it.
3. **Codex `usage_limit_exceeded` is Unknown, not Wall** — the overnight loop on `f07371c9`; should write a `ModelAvailabilityHold` like CARD-0022/0281.
