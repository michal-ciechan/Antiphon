# Telegram bot agents - implementation plan

Status: Implemented (2026-07-22) — PRs 0–12 landed; full round-trip verified live
Date: 2026-07-21
Spec: [2026-07-21-telegram-bot-agents.md](../specs/2026-07-21-telegram-bot-agents.md)
Ops + known issues: [telegram-bot-ops.md](../../telegram-bot-ops.md)

## Implementation status (2026-07-22)

All 11 PRs landed, CI-green, with headed real-Claude canaries proving the risky assumptions
(`--append-system-prompt` survives `/compact` and `--resume`; compact-boundary JSONL shape;
batched-marker body verifies on the real composer). PR 11 (Claude nesting-marker env scrub) was
added from live verification.

**Full round-trip verified live this session:** migrations applied at boot; bridge consumes inbound
from the local broker (stable consumer group); same-sender debounce merges rapid messages; verified
delivery into the real Claude composer; bootstrap note end-to-end (agent ran its CLAUDE.md ritual,
deleted BOOTSTRAP.md, wrote its memory log); restart-resume note end-to-end; and — after PR 12 —
**the agent's PONG reply routed all the way back to `channels.outbound` and was recorded by the
gateway** (inbound → agent → reply → outbound).

**PR 12 (transcript tailer resilience):** interactive Claude does not reliably honour `--session-id`
— it forks the conversation to a self-chosen `<uuid>.jsonl`. Ruled out as arg/quoting (command line
confirmed correct), `--append-system-prompt` (headed diagnostic honoured it), and nesting-marker env
(live PEB read: no `CLAUDE_CODE_SESSION_ID`). `TranscriptTailer` now discovers the real transcript by
its `cwd` field when `<session-id>.jsonl` never appears. See the ops doc for details.

Verified against the working tree at `8687e20`. Corrections to the spec's "what exists" table, confirmed in code:

- `AgentLaunchOptions.ExtraArgs` **already exists** and `AgentRegistry.Resolve` appends it (`server/Application/Dtos/AgentLaunchSpec.cs:13`, `AgentRegistry.cs:48-50`). No registry change needed — only `AgentControlService.StartInteractiveSessionAsync` must pass it.
- `AgentSessionService.BuildClaudeSessionArgs` (AgentSessionService.cs:652-699) strips only `--session-id/--resume/-r/--continue/-c`; `--append-system-prompt` passes through on **both fresh and resume** launches, and the one resolved `spec` (AgentControlService.cs:124) feeds fresh, resume, and the not-found fallback alike.
- JSONL parsing is `src/Antiphon.SessionRunner/TranscriptNormalizer.cs` (pure static, fed by `TranscriptTailer.cs`); kinds are plain strings in `SessionRunnerContracts.cs` (`TranscriptKinds`), so a new kind flows runner→server with no wire change. **The existing normalizer tests live at `tests/Antiphon.Tests/Agents/TranscriptNormalizerTests.cs`** — not in `Antiphon.SessionRunner.Tests` (that project holds only PtyHost/SessionLiveness tests). There are **no existing tailer tests** anywhere; any tailer coverage is new work.
- Next free incident value: `ContextCompacted = 12` (`server/Domain/Enums/AgentIncidentKind.cs` ends at `DeliveryVerificationFailed = 11`).
- `tests/Antiphon.E2E` is an **in-proc `WebApplicationFactory` + Testcontainers Postgres** fixture — no Kafka, no fake gateway on :17208, no session-runner. The spec's dev-stack smoke is new harness work (see PR 10), not an extension of existing E2E patterns.
- Two facts that shape the whole design (verified):
  - `SessionMessageQueueService.IsWorkingAsync` (SessionMessageQueueService.cs:495-507) treats **every** transcript kind except `TurnEnd`/`TurnTitle` as activity; the client mirrors this (`client/src/features/agents/SessionTranscriptPanel.tsx:~76-81`). Both the `EnqueueAsync` idle fast-path (line 104) and `FlushStrandedQueuesAsync` (line 280) gate on it, and the watchdog is **always-on-agents-only** (line 259). Any new persisted kind that isn't excluded turns an idle session into a phantom "working" one and strands WhenIdle messages.
  - `AgentSupervisorService.RecordIncidentAsync` (AgentSupervisorService.cs:272-307) raises an alert **unconditionally** — incidents ARE alerts, 1:1, no severity gate — and does **not** `SaveChanges` (callers do; see `HandleDeliveryFailureAsync`, SessionMessageQueueService.cs:463-473). The service is **scoped** (Program.cs:152).

---

## Risk register → canary map

Formal decision point: **if the append-system-prompt canaries (rows 1–4) fail, STOP — pivot to a CLAUDE.md-based contract plus queued re-injection on the recovery path, before any migration or launch plumbing exists.** That pivot changes PR 5's content but not its seams, which is why the canaries are their own PR, first.

| # | Risk / assumption | Canary that pins it | Pin artifact | Kill-switch / fallback |
|---|---|---|---|---|
| 1 | `--append-system-prompt` accepted on an interactive TUI launch | `ClaudeAppendSystemPromptCanaryTests.Append_system_prompt_flag_is_accepted_interactively` | codeword-marker echo | Pivot: CLAUDE.md contract + queued injection (decision point above) |
| 2 | Append survives `/compact` (the design keystone) | `...Marker_still_echoes_after_compact` | marker echo post-compact | Same pivot; recovery note (PR 7) carries the full contract text instead |
| 3 | Re-append on `--resume` re-arms the contract (spec risk c) | `...Marker_survives_resume_relaunch` | marker echo post-resume | Queued re-injection note on every resume |
| 4 | Multi-line arg survives runner→pty-host CreateProcess quoting on Windows | `...Multi_line_append_arg_survives_windows_arg_quoting` | multi-line marker echo | Single-line preamble (paragraphs joined with spaces) — renderer switch, decided by the canary |
| 5 | Compact-boundary JSONL record shape (spec risk a) | `ClaudeCompactionCanaryTests.Compact_writes_a_boundary_entry_to_the_session_jsonl` | committed fixture `compact-boundary.jsonl` — single source of truth for normalizer AND fakeclaude | CLI version bump breaks one unit test, not prod; normalizer branch is additive |
| 6 | `Compacted (ctrl+o…)` screen-line text | `...Compact_renders_the_compacted_screen_line` | `ClaudeCompactedDetector` regex + fakeclaude constant (`// PINNED-BY:` comments) | Detector is fallback-only in this epic; transcript path is primary |
| 7 | Multi-line batch body passes composer delivery evidence on real Claude (paste-placeholder path) | `ClaudeVerifiedDeliveryTests.Batched_body_with_markers_verifies_against_real_composer` | `ComposerDeliveryEvidence` head/tail contract extended to batch shape | `ChannelBridge:BatchingEnabled=false` restores 1:1 delivery |
| 8 | Historical-event replay: `TranscriptTailer` restarts at offset 0 on every runner restart/adoption and republishes all events through `SessionRunnerEventPump` → `ObserveTranscriptAsync` | (structural, no canary) covered by `Duplicate_boundary_events_are_deduped_after_simulated_replay` | persisted watermark on `AgentSession` (PR 7) | Note is WhenIdle + NO_REPLY-able; worst case is one redundant note |
| 9 | Debounce moves routing off the awaited consume loop | `Flush_failure_raises_drop_alert_and_does_not_fault_the_loop` | try/catch + `RaiseBridgeDropAlertAsync` around the flush callback | `ChannelBridge:DebounceWindowMs=0` = passthrough (today's behavior) |
| 10 | Low-context screen line `Context left until auto-compact: N%` shape (stretch WI) | captured **opportunistically during the first headed canary run** (row 1) — note the exact rendering in test output even though the feature ships last | recorded line shape in canary output | Feature is last and independently droppable |

All headed canaries: `[Category("Headed")]`, `[NotInParallel("Headed")]`, skipped unless `ANTIPHON_HEADED_TESTS=1`, modeled on `ClaudeComposerRenderCanaryTests` / `ClaudeVerifiedDeliveryTests`. Run: `dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-ptyhost\ --treenode-filter ...` (daemons lock `bin/`).

---

## PR 0 — Contract freeze (no behavior change)

Single authoritative static classes pinning every string the epic depends on, landed before anything consumes them. Nothing is wired to production paths in this PR; existing behavior is unchanged.

**Files**
- `server/Application/Services/ChannelPromptFormat.cs` (new, pure static): the envelope grammar.
  ```csharp
  public static class ChannelPromptFormat
  {
      public static string Format(ChatChannel channel, string author, string? username,
          DateTimeOffset timestamp, string text, TimeZoneInfo tz);
      // → [Telegram "Family" — Mike (@mciechan) 14:32] text   (DM: [Telegram direct message — ...])
      public static string FormatBatch(IReadOnlyList<string> contextBodies, string currentBody);
      // → [Chat messages since your last reply - for context]\n...\n\n[Current message - respond to this]\n...
      public const string BatchContextMarker = "[Chat messages since your last reply - for context]";
      public const string BatchCurrentMarker = "[Current message - respond to this]";
  }
  ```
- `server/Application/Services/ChannelContracts.cs` (new): reply-contract semantics.
  ```csharp
  public static class ChannelContracts
  {
      public static bool IsNoReply(string turnResponse); // whole-turn: Trim() == "NO_REPLY", OrdinalIgnoreCase
  }
  ```
- `server/Application/Services/ChannelPreamble.cs` (new): the preamble template and every system-note body.
  ```csharp
  public static class ChannelPreamble
  {
      public static string Render(string template, string agentName,
          IReadOnlyList<(string Provider, string Title)> boundChannels); // {agentName}, {channels}
      public static string TelegramPresetTemplate { get; }  // 4-part contract from the spec:
          // identity hook; inbound envelope + batch markers + untrusted-metadata warning;
          // reply contract (4000 chars, phone-sized, plain Markdown, NO_REPLY); compaction note
      public static string BootstrapBody { get; }        // "New session started. Follow your CLAUDE.md
          // session-start ritual now (and BOOTSTRAP.md if present), then reply READY."
      public static string RestartResumeBody { get; }    // "Your session was resumed after a restart.
          // Skim today's memory log before acting; do not re-execute completed work. Reply NO_REPLY
          // unless you have something for the user."
      public static string RecoveryNoteBody { get; }     // the [System note from Antiphon: ...] text from the spec
  }
  ```
- `server/Application/Services/AgentSupervisorService.cs`: add `bool raiseAlert = true` parameter to `RecordIncidentAsync` (AgentSupervisorService.cs:272) — **required work, not a hedge**: alerts are raised unconditionally today and PR 7's Info incident must not alert. Default `true` = zero behavior change; PR 7 consumes it.

**Tests (pure unit, `tests/Antiphon.Tests/Application/ChannelContractsTests.cs` + golden strings)**
- `Envelope_contains_title_author_username_and_local_time`; `Direct_message_envelope_omits_title`
- `Batch_format_places_all_but_newest_under_context_marker`; `Batch_of_one_uses_plain_envelope`
- `Preset_contains_envelope_reply_contract_no_reply_and_compaction_note`
- `Preamble_placeholders_render_agent_name_and_bound_channel_titles`
- `IsNoReply_matches_whole_turn_only` (leading/trailing prose defeats it)
- `Existing_incident_paths_still_alert` (raiseAlert default unchanged)

**De-risks:** every later PR references frozen strings instead of re-deciding them; envelope/marker drift between bridge, queue, dispatcher, fakeclaude, and docs becomes impossible.

## PR 1 — fakeclaude v2: compaction + batched/multi-line submit modelling

**Files:** `src/Antiphon.FakeClaude/Program.cs`; `tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs` (extend).

**Changes**
1. **Newline-safe submit marker.** `SubmitTurn` emits `SUBMITTED:{text.Replace("\n", "\\n")}` on one line. The batched body arrives as ONE paste burst → accumulates in the composer with literal newlines (composer echo at Program.cs:144 already handles this) → lone-CR burst submits the whole body; only the output marker needs escaping.
2. **Response-echo truncation is a NEW change, not "keep":** today `FAKE response to:` echoes the full submitted text (Program.cs:156). Truncate to the first 60 chars **and audit existing `FakeClaudeContractTests` / PTY-integration assertions that match on that line** before merging.
3. **`/compact` command:** submitted text exactly `/compact` → `\r\n`, `SUBMITTED:/compact\r\n`, then the pinned screen line `Compacted (ctrl+o to see full summary)\r\n` (`// PINNED-BY: ClaudeCompactionCanaryTests`), then idle title. No `Crunched for Ns` — compaction is not a turn.
4. **Spontaneous compaction:** env `ANTIPHON_FAKE_COMPACT_AFTER_TURNS=N` — emit the `Compacted (...)` line after the Nth turn's normal output.
5. **JSONL transcript emission (opt-in):** env `ANTIPHON_FAKE_TRANSCRIPT_PATH=<file>` — `user` line on submit, `assistant` (+`stop_reason:"end_turn"`) on turn end, and on compaction the boundary line **copied verbatim from the PR 3 canary fixture** (fakeclaude owns no schema).

**Tests (gate):** `Multi_line_paste_then_lone_cr_submits_whole_body_with_escaped_marker`; `Slash_compact_emits_compacted_screen_line_and_no_turn_end`; `Compact_after_turns_env_emits_compacted_after_nth_turn`; `Transcript_path_env_appends_user_assistant_and_boundary_lines`; plus the truncation-audit updates.

**De-risks:** every later PTY-tier test. Ships alone; no product code.

## PR 2 — FakeAgentProtocolAdapter + shared DI harness

**Files:** `tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs`; extract the `CreateHarnessAsync` setup used by `SessionMessageQueueDeliveryVerificationTests` into a shared `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs`.

**Changes**
1. `FakeAgentProtocolAdapter.SubmittedBodies` (`List<string>`): on the lone-`"\r"` branch of `SendInputAsync`, capture `_composer.ToString()` before clearing — batching tests assert the submitted body shape directly.
2. Shared harness exposes: `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing — `ChannelBridgeService` already takes `TimeProvider`), `FakeAntiphonMessagingClient` (`Antiphon.Messaging.Client.Testing`), **`MockEventBus`** (`tests/Antiphon.Tests/TestHelpers/MockEventBus.cs` — the repo's existing fake; there is no `TestEventBus`), and `InsertTranscriptEntryAsync(kind, seq, ...)` able to insert `CompactBoundary` rows.
3. NO_REPLY tests drive assistant turns via the existing **DB-based `InsertTurnAsync`** pattern (ChannelBridgeTests.cs:299) — `ChannelReplyDispatcher.DispatchAsync` reads exclusively from `TranscriptEntries` (ChannelReplyDispatcher.cs:94-151), so no adapter-output hook is added (a raw-output emitter would be dead weight).

**Tests (gate):** existing suites stay green (`SessionMessageQueueDeliveryVerificationTests`, `ChannelBridgeTests`, `SessionMessageQueueServiceTests`) via `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-ptyhost\`.

## PR 3 — Headed canaries (go/no-go)

**Files:** `tests/Antiphon.Agents.Pty.Tests/ClaudeAppendSystemPromptCanaryTests.cs`, `ClaudeCompactionCanaryTests.cs` (new); `tests/Antiphon.Tests/Agents/Fixtures/compact-boundary.jsonl` (new — committed **next to its consumer**, the normalizer tests in `Antiphon.Tests`).

**Canary A — `ClaudeAppendSystemPromptCanaryTests`** (risk rows 1–4): launch real `claude --append-system-prompt "When asked for the codeword reply exactly ZEBRA-QUARTZ-19"` + `--session-id` via `PtyAgentRunner`; assert the marker via `ClaudeResponseAnalyzer.ExtractResponse` — then post-`/compact`, post-kill-and-`--resume`-relaunch, and with a multi-line marker text (Windows arg-quoting pin). While this session is live, **record the low-context indicator line's exact shape** (`Context left until auto-compact: N%`) in test output for the stretch WI — free data, no extra run.

**Canary B — `ClaudeCompactionCanaryTests`** (risk rows 5–6): `/compact` after filler turns; locate `~/.claude/projects/<encoded-cwd>/<session-id>.jsonl`; write the boundary record's raw line to test output AND assert `compact-boundary.jsonl` matches its shape (first run captures, thereafter pins). Assert the rendered `Compacted (` screen line and pin its text.

**Decision point executes here** (see risk table). Merging PR 3 with green canaries is the approval to build PRs 5–7 as designed.

## PR 4 — Slice 1: workspace content + docs (parallel with anything)

**`C:\src\ClaudeBot` (separate repo):** `agents/family/USER.md`, `MEMORY.md`, `memory/` (+ seed `memory/2026-07-21.md`), `BOOTSTRAP.md`; update `agents/family/CLAUDE.md` with the session-start ritual (read SOUL/USER/MEMORY, skim today+yesterday memory logs) and the compaction-recovery paragraph. Mirror `openclaw/workspace` conventions.

**Antiphon repo:** `docs/agent-workspaces.md` — the convention (`Agent.WorkingDirectory = C:\src\ClaudeBot\agents\<name>`, file roles, BOOTSTRAP deletion ritual).

**Tests:** none (content). Gate = review.

## PR 5 — Slice 2: `Agent.SystemPromptAppend` + launch plumbing + bootstrap/restart notes

**Schema / migration**
- `server/Domain/Entities/Agent.cs`: `public string? SystemPromptAppend { get; set; }`
- Migration via `dotnet ef migrations add AddAgentSystemPromptAppend` (produces `yyyyMMddHHmmss_AddAgentSystemPromptAppend.cs` + `.Designer.cs` + snapshot update). Nullable text, no backfill.

**Launch plumbing (`AgentControlService.StartInteractiveSessionAsync`)**
- **Kind gate before Resolve** (the naive `spec.Kind == AgentKind.ClaudeCode` check is circular — `Kind` is produced by the same `Resolve` call that must receive the args): look up the definition first via `AgentRegistry.LookupByName(...)` (returns `AgentDefinition` with **string** `Kind`; compare via `Enum.TryParse`/`nameof(AgentKind.ClaudeCode)`), build `ExtraArgs`, then call `Resolve` once:
  ```csharp
  string[]? extraArgs = isClaudeCode && !string.IsNullOrWhiteSpace(agent.SystemPromptAppend)
      ? ["--append-system-prompt", ChannelPreamble.Render(agent.SystemPromptAppend, agent.Name, boundChannels)]
      : null;
  var spec = _agentRegistry.Resolve(..., new AgentLaunchOptions(Cols: 120, Rows: 30, ExtraArgs: extraArgs));
  ```
  Bound channels: `_db.ChatChannels.Where(c => c.AgentId == agent.Id && c.Enabled)`. Rendered at launch time — note honestly: bindings added later flow in **as of the next launch**, not live. Fresh, resume, and the not-found fallback all carry the args because they share the one resolved `spec`.

**Bootstrap + restart notes — threaded on ALL launches, branch decided where the truth lives**
- New record `server/Application/Dtos/LaunchNotes.cs`: `public sealed record LaunchNotes(string FreshBody, string? ResumeBody);` — **two bodies, not one string**: the resume→fresh fallback must deliver the *fresh* bootstrap, a successful resume the *restart* note.
- `AgentSessionLaunchQueue.EnqueueInteractiveSession(...)` gains `LaunchNotes? notes = null`, threaded through `AgentSessionService.LaunchInteractiveAsync` → `LaunchInteractiveProcessAsync`. `AgentControlService` passes `new LaunchNotes(ChannelPreamble.BootstrapBody, ChannelPreamble.RestartResumeBody)` on **every** launch of an agent with `SystemPromptAppend` configured — including the resume branch — so the fallback path always has the right body in hand.
- In `LaunchInteractiveProcessAsync`, after `SendRemoteControlCommandsAsync`, the code knows which branch actually ran: genuinely-fresh row → `FreshBody`; `ClaudeSessionNotFoundException` fallback (**effective fresh** — same session row, old `TranscriptEntries`) → `FreshBody`; successful resume → `ResumeBody` (skip if null).
- **Delivery mode — `MessageSendMode.Now`, not WhenIdle** (fixes the stranding caveat): at this point the session just reached Running and is idle *by construction*, but `IsWorkingAsync` can read **true** on the fallback and resume paths (reused row whose prior conversation died mid-turn: `lastActivity > lastEnd`), so a WhenIdle enqueue would skip the idle fast-path, have no upcoming turn-end, and the watchdog is always-on-only — it would strand indefinitely for non-always-on agents. `Now` goes through the same verified-delivery path (`DeliverAsync` + verdict, SessionMessageQueueService.cs:63-76). Wrap in try/catch: on `ConflictException`/failure, log + fall back to a WhenIdle enqueue (watchdog/next-turn-end recovery) — a note-delivery failure must never fail the launch. Resolve the singleton `SessionMessageQueueService` into the scoped `AgentSessionService` (safe: queue depends only on runtime/scope-factory; no cycle).
- No `ChannelReplyDispatcher.Track` for either note → nothing routes to chat.

**DTO / API / client**
- `AgentDtos.cs`: `string? SystemPromptAppend` on `AgentDetailDto` + `UpdateAgentRequest` (null = unchanged, empty = clear — existing convention); `AgentService.UpdateAsync` maps it.
- `GET /api/agents/preamble-preset?provider=telegram` in `server/Api/Endpoints/AgentEndpoints.cs` (note path: `server/Api/Endpoints/`, there is no `server/Endpoints/`) returning `ChannelPreamble.TelegramPresetTemplate`.
- `client/src/features/agents/AgentSettingsModal.tsx`: "System prompt (appended)" textarea + "Use Telegram preset" button; `client/src/api` types + mutation.

**Tests**

| Tier | Names |
|---|---|
| Pure unit | (PR 0 already covers renderer/preset) `LaunchNotes_fallback_selects_fresh_body` (if extracted as a pure helper) |
| Server integration (`tests/Antiphon.Tests/Application/AgentSystemPromptLaunchTests.cs`, harness + `FakeAgentProtocolAdapter.StartedArgs`/`SubmittedBodies`) | `Start_with_system_prompt_append_passes_flag_on_fresh_launch`; `Resume_launch_also_carries_append_system_prompt`; `Agent_without_preamble_launches_with_unchanged_args`; `Fresh_start_delivers_bootstrap_exactly_once_verified`; `Resume_start_delivers_restart_note_not_bootstrap`; `Resume_not_found_fallback_delivers_fresh_bootstrap`; `Fallback_with_stale_mid_turn_transcript_still_delivers_bootstrap` (insert activity-after-TurnEnd rows first — pins the Now-mode rationale); `Note_delivery_failure_falls_back_to_queue_and_does_not_fail_launch`; `Bootstrap_produces_no_channel_reply` |
| PTY integration (fakeclaude) | `Launch_args_reach_the_child_process` — **new fakeclaude capability in this PR**: `--echo-args` makes the banner print joined argv (does not exist today); asserts `--append-system-prompt` + multi-line text survived runner→pty-host intact |
| Headed | Canary A (PR 3) gates this PR's merge |

**De-risks:** the Windows arg-quoting and resume-re-append assumptions land immediately after their canaries; all three note-delivery paths (fresh / effective-fresh / resume) are pinned before compaction work builds on them.

## PR 6 — Slice 3a: compaction detection (normalizer + kind + **idle-detection exclusions**, one PR)

The `CompactBoundary` kind and the `IsWorking` exclusions **must ship together** — a persisted boundary row after the last `TurnEnd` otherwise flips `IsWorkingAsync` (and the client mirror) to permanently "working": every `/compact` would strand the PR 7 recovery note (both the `EnqueueAsync` idle fast-path and `FlushStrandedQueuesAsync` gate on it) and show a phantom working agent in the UI.

**Files**
- `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`: `TranscriptKinds.CompactBoundary = "CompactBoundary"`.
- `src/Antiphon.SessionRunner/TranscriptNormalizer.cs`: new branch parsing the boundary record exactly as pinned by the PR 3 fixture (`type`/`subtype` per capture); emits `TranscriptPart(TranscriptKinds.CompactBoundary, uuid, parent, ts, ..., StopReason: null)`. Not a turn end.
- `server/Application/Services/SessionMessageQueueService.cs` (IsWorkingAsync, lines 503-504): add `&& t.Kind != TranscriptKinds.CompactBoundary` to the activity filter.
- `client/src/api/sessions.ts`: add `'CompactBoundary'` to the `TranscriptKind` union (lines 11-18).
- `client/src/features/agents/SessionTranscriptPanel.tsx`: exclude `CompactBoundary` from the `isWorking` mirror (~lines 76-81) and render it deliberately — a subtle "context compacted" divider in the transcript (the render switch handles only known kinds today; an unhandled decision here is silent UI drift).
- `src/Antiphon.Agents.Pty/ClaudeDetectors.cs`: `ClaudeCompactedDetector` — regex on the pinned `Compacted (` line, `WaitAsync` shaped like `ClaudeCrunchedDetector`. **Fallback/canary surface only in this epic** — deliberately not wired to product code; documented as the session-health hook if the transcript path ever goes quiet (spec Slice 3.2).

**Tests**

| Tier | Names |
|---|---|
| Pure unit (`tests/Antiphon.Tests/Agents/TranscriptNormalizerTests.cs` — the real location; fixture lives beside it) | `Compact_boundary_line_normalizes_to_CompactBoundary_kind`; `Compact_boundary_is_not_a_turn_end`; existing tests unchanged |
| Pure unit (`tests/Antiphon.Agents.Pty.Tests/ClaudeDetectorsTests.cs`) | `CompactedDetector_matches_pinned_screen_line`; `CompactedDetector_ignores_ordinary_output` |
| Server integration | `Session_with_boundary_after_last_turn_end_reads_idle` (harness inserts `CompactBoundary` after `TurnEnd`; asserts `IsWorkingAsync` false and the WhenIdle fast-path fires) |
| Client (Vitest) | `isWorking_ignores_compact_boundary_entries`; `transcript_renders_compact_divider` |
| PTY integration (fakeclaude, PR 1) | `Compacted_screen_line_from_fakeclaude_trips_the_detector` |
| Runner integration (`tests/Antiphon.SessionRunner.Tests/TranscriptTailerCompactionTests.cs` — **new file; no existing tailer tests exist to extend**) | `Tailer_emits_CompactBoundary_event_for_boundary_line` (file-driven: append fixture line, assert emitted part) |

**De-risks:** the undocumented JSONL shape lives in exactly one fixture; the idle-detection regression can never ship separately from the kind that causes it.

## PR 7 — Slice 3b: recovery note + incident + replay-proof dedupe

**Schema / migration:** `AddCompactionRecoveryWatermark` — `AgentSessions.CompactionRecoveryWatermark bigint null`. This is the dedupe anchor. **Why not an incident-row check:** the real replay vector is `TranscriptTailer` restarting at **offset 0** on every runner restart/adoption, republishing every historical event through `SessionRunnerEventPump` → `ObserveTranscriptAsync` — and a durable check against `AgentIncidents` is defeated by `AgentSupervisorService.PruneIncidentsAsync` (30 days / 500-per-agent cap) for long-lived sessions. A persisted per-session high-water mark survives both.

**Files**
- `server/Domain/Enums/AgentIncidentKind.cs`: `ContextCompacted = 12` (doc: Info, no alert — normal operation).
- `server/Application/Services/CompactionRecoveryService.cs` (new, singleton):
  ```csharp
  public sealed class CompactionRecoveryService
  {
      // ctor deps: IServiceScopeFactory, SessionMessageQueueService, TimeProvider, ILogger
      public async Task OnCompactBoundaryAsync(Guid sessionId, long sequence, string? uuid, CancellationToken ct);
  }
  ```
  Behavior per call: in-memory `(sessionId, sequence)` latch for cheap same-process dedupe; then create a scope → `AppDbContext` + **`AgentSupervisorService` (scoped — must be scope-resolved, and `RecordIncidentAsync` does not save; this scope's `SaveChangesAsync` commits both)**; skip if `sequence <= session.CompactionRecoveryWatermark`; resolve agent by `PersistentSessionId`; `RecordIncidentAsync(agentId, sessionId, ContextCompacted, AlertSeverity.Info, "...boundary seq <n>...", raiseAlert: false)` (PR 0's parameter); set watermark = `sequence`; `SaveChangesAsync`. Then, **only if `agent.SystemPromptAppend is not null`** (a plain dev agent compacting gets the incident but no bot-flavored note): `EnqueueAsync(sessionId, ChannelPreamble.RecoveryNoteBody, WhenIdle, Origin: System once PR 8 lands)` — safe now that PR 6 excluded the boundary from `IsWorkingAsync`.
- `server/Application/Services/AgentSessionRuntime.cs` `ObserveTranscriptAsync`: on `entry.Kind == TranscriptKinds.CompactBoundary`, dispatch to the recovery service — **resolved lazily** (`GetRequiredService` at dispatch time), mirroring the existing queue-service comment at AgentSessionRuntime.cs:203. Direct ctor injection is NOT safe: Runtime→Recovery→Queue→Runtime is a constructor cycle (`SessionMessageQueueService` ctor-injects `AgentSessionRuntime`, both singletons).
- `server/Program.cs`: `builder.Services.AddSingleton<CompactionRecoveryService>();`
- **Q2 restart-resume note:** already shipped in PR 5 (`ChannelPreamble.RestartResumeBody`, Now-mode). No incident row for resumes — a supervised restart already records incidents.

**Tests (`tests/Antiphon.Tests/Application/CompactionRecoveryTests.cs`, DI harness)**

| Tier | Names |
|---|---|
| Server integration | `Compact_boundary_records_info_incident_and_enqueues_one_recovery_note`; `Duplicate_boundary_events_are_deduped_after_simulated_replay` (clear the in-memory latch, re-fire same + lower sequences — watermark holds; also prune incidents first to prove independence from `PruneIncidentsAsync`); `Recovery_note_is_delivered_verified_when_session_idle` (`Adapter.SubmittedBodies`); `Compaction_incident_does_not_raise_alert` (fake alert sink empty — fails without PR 0's `raiseAlert`); `Agent_without_preamble_gets_incident_but_no_note` |
| Pure unit | (PR 0) `Recovery_note_text_names_workspace_files_and_no_reply` |
| Headed (optional, extend Canary B) | `Forced_compact_produces_incident_and_recovery_note_end_to_end` |

**Sequencing:** lands before batching so the recovery note first runs in prod through the simple one-message-per-turn path.

## PR 8 — Slice 4a: envelope wiring + queue origin metadata (behavior-neutral schema PR)

Deliberately split from PR 9: migration + envelope switch ship with **no** batching behavior, so the risky delivery-semantics change is a pure-logic diff.

**Schema / migration:** `AddQueuedMessageOrigin` — `SessionQueuedMessage.Origin` (`QueuedMessageOrigin` enum, new file `server/Domain/Enums/QueuedMessageOrigin.cs`: `Ui=0, Channel=1, System=2`; int column default 0) + `ConversationKey text null`. No new index (queues are tiny).

**Server**
- `SessionMessageQueueService.EnqueueAsync` gains optional `(QueuedMessageOrigin origin = Ui, string? conversationKey = null)`; UI endpoints unchanged; `CompactionRecoveryService` and PR 5's WhenIdle-fallback note pass `System`.
- `ChannelBridgeService.HandleInboundAsync`: replace the private `BuildPrompt` (ChannelBridgeService.cs:171) with `ChannelPromptFormat.Format` (author display name + `@username` when `Participant` carries one, `HH:mm` server-local from `ChannelMessage.Timestamp`); enqueue with `Origin=Channel, ConversationKey=$"{channel.Provider}:{message.Conversation.Id}"`.
- **Config:** add the new keys (used by PR 9 but documented now, in one place) to **both** `server/appsettings.json` (existing `ChannelBridge` section at line 89) **and** `server/appsettings.json.example`: `DebounceWindowMs`, `DebounceMaxMs`, `BatchingEnabled` (+ `Compaction:MemoryFlushThresholdPercent` for the stretch WI).

**Client:** extend the closed `AgentIncidentKind` union in `client/src/api/agents.ts` (lines 54-65) with `'DeliveryVerificationFailed'` (**already missing today**) and `'ContextCompacted'`.

**Tests**

| Tier | Names |
|---|---|
| Pure unit | (PR 0 golden tests already pin the format) |
| Server integration (extend `ChannelBridgeTests`) | `Bridge_enqueues_with_channel_origin_and_conversation_key`; `Ui_enqueue_keeps_ui_origin`; existing reply-correlation tests green (Track and Enqueue both use the new envelope, so `PromptsMatch` still holds) |

## PR 9 — Slice 4b: bridge debounce + batched delivery + NO_REPLY

**Settings** (`server/Application/Settings/ChannelBridgeSettings.cs`): `DebounceWindowMs = 500` (sliding), `DebounceMaxMs = 2000` (hard cap from first buffered message), `BatchingEnabled = true`. `DebounceWindowMs=0` = passthrough.

**Debounce** — new `server/Application/Services/ChannelInboundDebouncer.cs` (singleton, `TimeProvider`-driven, timers via `Task.Delay(_timeProvider)` so `FakeTimeProvider` drives tests). **Registered in `server/Program.cs`** (`AddSingleton<ChannelInboundDebouncer>()` — the bridge is host-constructed; an unregistered dependency fails at startup).
- **Debounce key = `(conversationKey, authorId)` — NOT per-conversation.** This diverges deliberately from the spec's per-conversation wording (spec line 91): merging across authors would make the single merged envelope header lie about who spoke. Same-sender merge is the Hermes rule; the merged flush uses ONE envelope header (truthful, first buffered message's metadata) + one line per message text. Different authors/conversations flush independently.
- `HandleInboundAsync` keeps catalog upsert / dup-check / `EnsureAgentSessionAsync` inline per message (still under the consume loop's catch/backoff), then hands `(channel, message, sessionId)` to the debouncer. Flush callback does `Track` + `EnqueueAsync(Channel, key)`.
- **Flush failures must not become unobserved-task faults** (routing has left the awaited consume loop): wrap the flush callback in try/catch → log + `RaiseBridgeDropAlertAsync` (ChannelBridgeService.cs:147). This preserves the documented "broker outage degrades to late messages, never silent loss without an alert" property.
- `FlushAllAsync` on shutdown drain; `internal int PendingConversations` test surface.

**Batched delivery** — `SessionMessageQueueService.DeliverNextLockedAsync`:
- If `BatchingEnabled && head.Origin == Channel`: take the **contiguous run** from the head of pending with the same `ConversationKey` (stop at any different key or non-Channel origin — preserves cross-origin FIFO; UI/System messages always deliver 1:1). Run of 1 = exactly today's behavior.
- Run >1: body = `ChannelPromptFormat.FormatBatch(allButNewest, newest)`; mark ALL run rows `Sent` in one `SaveChanges`; one `DeliverAsync`; on failure revert ALL run rows to `Pending`.
- `HandleDeliveryFailureAsync` signature becomes `(Guid sessionId, IReadOnlyList<Guid>? messageIds, DeliveryVerdict verdict, CancellationToken ct)` — **must keep accepting null**, because the Now-mode call site passes null today (SessionMessageQueueService.cs:71); single-message sites pass a one-element list.
- Tail-fragment composer evidence covers the newest message naturally (it is the body's tail; `ComposerDeliveryEvidence` already models head/tail + paste placeholder).

**Reply correlation** — `ChannelReplyDispatcher`:
- `TakeMatching` → `TakeAllMatching`: keep the fast 120-char-prefix `StartsWith` for the non-batch path, add `turnPrompt.Contains(probe)` containment for batch bodies; **consume every matching correlation** for the turn.
- **Group matched correlations by `ConversationId`; send ONE `ChannelReply` per distinct conversation, using the newest match's `ReplyHandle`.** With same-conversation batching this is degenerate (always exactly one) — it is a deliberate latent safety net in case batching scope ever widens; cheap now, expensive to retrofit.
- **NO_REPLY:** after `ExtractTurnResponseAsync`, `if (ChannelContracts.IsNoReply(responseText))` → log, consume correlations, send nothing. Whole-turn exact match only (PR 0's frozen semantics).

**Tests**

| Tier | Names |
|---|---|
| Pure unit | `ChannelInboundDebouncerTests`: `Same_sender_messages_within_window_merge_into_one_flush`; `Different_senders_same_conversation_do_not_merge`; `Quiet_window_expiry_flushes`; `Hard_cap_flushes_under_continuous_typing`; `Different_conversations_never_merge`; `Flush_failure_raises_drop_alert_and_does_not_fault_the_loop`. `ChannelReplyDispatcherTests`: `No_reply_turn_is_swallowed`; `Batch_body_matches_all_constituent_correlations_by_containment`; `Human_typed_turn_still_matches_nothing`; `Matches_group_to_one_reply_per_conversation_with_newest_handle` |
| Server integration (new `tests/Antiphon.Tests/Application/ChannelBatchingTests.cs`) | `Multiple_pending_channel_messages_coalesce_into_one_batched_delivery_all_rows_sent` (ONE `SubmittedBodies` entry with both markers); `Batched_reply_fans_out_once_to_the_conversation`; `Ui_enqueued_messages_stay_one_per_turn_even_with_batching_on`; `Mixed_origin_queue_preserves_order_ui_message_breaks_the_run`; `Cross_conversation_pending_messages_deliver_one_per_turn`; `Failed_batch_delivery_reverts_all_rows_and_records_one_incident`; `Now_mode_failure_still_works_with_null_message_ids`; `No_reply_turn_produces_no_channel_reply` (the spec's server-integration-tier NO_REPLY row — not unit-only); `Batching_disabled_setting_restores_single_message_delivery` |
| Bridge integration (extend `ChannelBridgeTests`, `FakeTimeProvider`) | `Rapid_fire_same_sender_inbound_merges_within_window`; `Merged_prompt_has_single_envelope_header_one_line_per_message`; `Correlation_tracked_once_per_flush_not_per_message` |
| PTY integration (fakeclaude) | `Batched_multiline_body_passes_composer_delivery_verification_and_submits_once` (PR 1's escaped `SUBMITTED:` marker contains both batch markers) |
| Headed | Risk row 7 canary (`Batched_body_with_markers_verifies_against_real_composer`) |

**Sequencing:** last behavioral PR — it touches the sacred submit contract; by now envelope, origin metadata, harness, and evidence contract are all pinned, so the diff reviews as pure delivery logic.

## PR 10 — Slice 5: ops + dev-stack smoke

**No new product code.**
- `docs/telegram-bot-ops.md`: second `Antiphon.Messaging.Service` deployment on server2 (same `ghcr.io/.../antiphon-messaging-telegram` image), `@antiphon_assistant_bot` token, `AllowedChatIds` fail-closed, same Kafka; bind the first-inbound-created channel to `family` on ChannelsPage (binding lives in `ChatChannelService.UpdateAsync` — there is no `BindAgentAsync`); agent row config (`AlwaysOn`, WorkingDirectory `C:\src\ClaudeBot\agents\family`, `SystemPromptAppend` = Telegram preset); optional admin-group `AlertMinSeverity`.
- **Dev-stack smoke — new work, stated plainly:** `tests/Antiphon.E2E`'s `AntiphonAppFixture` is in-proc WebApplicationFactory + Testcontainers Postgres with no Kafka, no fake gateway on :17208, and no session-runner — existing E2E patterns do **not** cover this flow. Two acceptable shapes, choose at build time:
  1. New `tests/Antiphon.DevStackSmoke` harness that targets a running `dev-aspire.ps1` stack (skipped unless `ANTIPHON_DEVSTACK_TESTS=1`): `Three_rapid_inbound_messages_produce_one_batched_turn_and_one_delivery` (fake gateway `POST :17208/inbound` ×3 within 300 ms → poll `GET /deliveries` for exactly one reply) and `Forced_compact_produces_incident_and_no_chat_spam` (queued `/compact` → incident via API, no extra delivery — NO_REPLY honored).
  2. Fallback hedge: the same two scenarios as a scripted manual checklist in `docs/telegram-bot-ops.md`, executed and checked off before first prod bind.
- Optional client nicety: workdir display / "open workspace folder" on the agent page (Slice 1.3).

## Stretch WI (ships last, independently droppable) — pre-compaction memory flush

Design captured now; built only after the basic path has run in prod:
- During the existing session-health cadence, screen-scrape the low-context indicator (`Context left until auto-compact: N%` — **exact shape recorded during the PR 3 Canary A run**, no dedicated headed run needed).
- Setting `ChannelBridge:Compaction:MemoryFlushThresholdPercent` (default 15). Below threshold → enqueue one System-origin nudge ("context is nearly full — flush durable state to today's memory log now"), guarded by a per-session latch.
- **Latch resets on the next `CompactBoundary`** (PR 6's kind), so exactly one nudge per compaction cycle.
- Tests: detector unit test on the pinned line; server-integration latch test; fakeclaude gains an env to render the indicator line.

---

## Build order recap

```
PR 0  contract freeze + raiseAlert param   — no behavior change
PR 1  fakeclaude v2                        — harness, unblocks PTY tiers
PR 2  fake adapter + shared harness        — harness, unblocks server tiers
PR 3  headed canaries                      — GO/NO-GO decision point; pins fixture PR 6 consumes
PR 4  workspace content                    — parallel, no code
PR 5  SystemPromptAppend + launch notes    (needs 0,2,3)
PR 6  compaction kind + IsWorking exclusions (needs 1,3-fixture) — inseparable pair
PR 7  recovery note + watermark dedupe     (needs 0,2,6)
PR 8  envelope wiring + origin schema      (needs 0,2; behavior-neutral)
PR 9  debounce + batching + NO_REPLY       (needs 1,2,8; riskiest diff on a fully pinned base)
PR 10 ops + dev-stack smoke                (needs all)
```

Each PR is individually green and shippable: 0–4 change no product behavior; 5–7 are inert for agents without `SystemPromptAppend`; 8 is a no-op migration + string-source swap; 9 is gated by `ChannelBridge:BatchingEnabled` and degrades to passthrough at `DebounceWindowMs=0`. Test command throughout: `dotnet run --project tests/<X> --property:OutputPath=bin-ptyhost\` (daemons lock `bin/`); PTY-flaky failures rerun in isolation before blaming a change.

---

## Recommended answers to open questions

1. **Per-agent bots** — one `Antiphon.Messaging.Service` instance per bot token. Bot name = persona is better Telegram UX; the gateway is already one-deployable-per-bot; `AllowedChatIds` stays a small fail-closed list per persona; a multiplexing bot would force sender-based agent routing into the bridge that the catalog doesn't need. **Known limitation to document:** `ChatChannel` is uniquely keyed `(Provider, ExternalId)`, so two bots joined to the *same* Telegram group collide on one channel row — both bots' traffic routes to whichever agent that row is bound to. Acceptable now (one bot per group by policy); the future fix is a `BotId` discriminator column in the channel key, which the catalog schema can absorb without redesign.
2. **Yes, fire a note on restart-resume — but a distinct, cheaper one.** Shipped in PR 5 as `ChannelPreamble.RestartResumeBody` ("session resumed after a restart; skim today's memory log; do not re-execute completed work; reply NO_REPLY unless…") — a separate body from the fresh bootstrap via the `LaunchNotes` record, because the resume→fresh fallback must get the *bootstrap*, not the resume note. Delivered Now-mode (verified) precisely because resumed sessions often carry a stale mid-turn transcript that makes `IsWorkingAsync` lie. No incident row — supervised restarts already record incidents.
3. **Strictly same-conversation batching — yes** (and same-sender for the debounce merge, a deliberate divergence from the spec's per-conversation wording so the merged envelope header stays truthful). Cross-conversation coalescing would force multi-reply fan-out with ambiguous attribution for one assistant text. The contiguous same-key run + containment matching keeps correlation exact; cross-chat messages one-per-turn is fine at family-chat volume; the per-conversation reply fan-out in the dispatcher is the safety net if scope ever widens.

## Kill-switches & fallbacks

- **Preamble design fails canaries (rows 1–4)** → formal STOP before any migration exists; pivot to CLAUDE.md-based contract + queued re-injection (recovery-note path carries the full contract text). Multi-line-arg failure alone → single-line preamble render, no pivot.
- **Batching**: `ChannelBridge:BatchingEnabled=false` → exact pre-epic one-message-per-turn delivery (run-of-1 code path is literally today's behavior).
- **Debounce**: `ChannelBridge:DebounceWindowMs=0` → passthrough, routing back to fully inline semantics. Flush failures alert via `RaiseBridgeDropAlertAsync` — degradation is *late or dropped-with-alert*, never silent.
- **Compaction recovery**: notes only fire for agents with `SystemPromptAppend` set — clearing the field disables the feature per agent with no deploy. Watermark dedupe caps the blast radius of any replay bug at one redundant NO_REPLY-able note.
- **CLI drift**: JSONL boundary shape and both pinned screen lines each break exactly one unit test (fixture / detector regex / fakeclaude constant), flagged by `// PINNED-BY:` comments pointing at the canary that re-pins them.
- **Whole feature per bot**: stop the per-persona `Antiphon.Messaging.Service` deployment — the bridge sees no inbound; agent keeps running unaffected.