# Epics — Kanban + Agent PTY Orchestration

> **Source plan:** `plan.md` in this directory.
> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
>
> **Note:** This doc was generated before `/pts-vibe-init` was run for this feature.
> Requirements (`FR-…` / `NFR-…`) are inlined here under §0 as a stand-in for
> `01-requirements.md`. When init runs, migrate them out.

---

## §0 Inline Requirements (stand-in for 01-requirements.md)

### Functional

- **FR-01** User can create boards (one per repo / workspace).
- **FR-02** User can create cards on a board with title, description, priority, labels.
- **FR-03** User can drag cards across columns (Backlog / In Progress / Review / Done — configurable).
- **FR-04** Moving a card to In Progress (or explicit `/spawn`) creates a git worktree on a fresh branch and starts an `AgentSession` with `cwd = worktree`.
- **FR-05** User picks `AgentKind` (ClaudeCode / Codex / Gemini / Cursor / Aider / Custom) from a registry when spawning.
- **FR-06** Live PTY output streams to xterm.js in browser via SignalR; late joiners get full ANSI replay via REST.
- **FR-07** User can send keystrokes and resize PTY from the browser.
- **FR-08** User can fork a session (clone history, new worktree).
- **FR-09** User can kill a session (SIGTERM → SIGKILL).
- **FR-10** Card moved to Review surfaces a diff view; inline comments route back to the agent as channel messages.
- **FR-11** Card moved to Done can open a GitHub PR from the worktree branch.
- **FR-12** Orchestrator polls a tracker (Linear / GitHub Issues / Jira / Internal) and auto-spawns sessions for eligible issues (Symphony mode).
- **FR-13** Orchestrator enforces global + per-column concurrency caps and exponential-backoff retry on failure.
- **FR-14** Each board has a `WORKFLOW.md` (YAML front matter + Markdown prompt) with hot-reload on edit; running attempts pin to the version captured at launch.
- **FR-15** Workspace hooks run `after_create` / `before_run` / `after_run` / `before_remove`; pre-hook failure aborts the attempt.
- **FR-16** Agents discover peers and `@mention` to delegate; messages inject into target session stdin (amux channels).
- **FR-17** Atomic claim — at most one active session per card.
- **FR-18** Watchdog detects "Press Enter / (Y/n)" prompts and auto-responds per registry rules.
- **FR-19** Snapshot API exposes orchestrator state: running sessions, retry queue, token totals, runtime seconds.
- **FR-20** Stale worktrees auto-prune after N days.

### Non-functional

- **NFR-01** Onion architecture preserved — Pty.Net / libgit2sharp confined to `Infrastructure/`.
- **NFR-02** Domain layer has zero external package dependencies.
- **NFR-03** Workspace path stays under configured root; directory names sanitized to `[A-Za-z0-9._-]+`.
- **NFR-04** PTY child cwd = workspace path only (never project root).
- **NFR-05** Per-session memory cap enforced via Windows `JobObject`.
- **NFR-06** ANSI output buffer: ring buffer in memory + disk spill under `C:\MavLog\Antiphon\sessions\`.
- **NFR-07** Tracker rate limits respected — single fetch per tick, in-tick cache.
- **NFR-08** Stalled session detected within `stall_timeout_ms` (default 5m) of last event.
- **NFR-09** Restart recovers running attempts via tracker repoll + filesystem worktree scan; retry queue persists in DB (Antiphon deviation from Symphony).
- **NFR-10** SignalR delta payload size capped; large outputs chunked.
- **NFR-11** All API routes follow `/api/...` kebab-case per `docs/project-context.md`.
- **NFR-12** All new entities migrate via EF Core code-first (`.\stop-server.ps1` first).

---

## §1 Overview

Three modes — **tracker-driven** (Symphony), **board-driven** (vibe-kanban),
**chat-driven** (amux) — feed one shared `RunAttempt` + `AgentSession`
machinery. Cards spawn worktrees; agents run as PTY children via vs-pty.net;
output streams via existing SignalR `AgentTextDelta`. Reuses Antiphon's
existing Workflow / Stage / Gate / Artifact / CostLedger primitives where
possible (Card may wrap Workflow rather than duplicate).

13 epics, sliced so each delivers a usable increment. Spike → infra → domain
→ vertical slices → external integrations → ops surface.

---

## §2 Epic Index

| ID | Epic | Requirements | Status |
|----|------|-------------|--------|
| E01 | Pty.Net spike + Windows headed-agent proof | FR-04, NFR-01 | `[ ]` |
| E02 | Agent abstraction (`IPtyAgentRunner` + `IAgentProtocolAdapter`) | FR-04, FR-05, FR-06, NFR-01 | `[ ]` |
| E03 | Worktree manager + safety invariants | FR-04, FR-08, NFR-03, NFR-04, FR-20 | `[ ]` |
| E04 | Domain model + EF migration (Board / Card / AgentSession / RunAttempt / Worktree / WorkflowDefinition) | FR-01, FR-02, NFR-02, NFR-12 | `[ ]` |
| E05 | AgentSession lifecycle + RunAttempt phase machine + SignalR streaming | FR-06, FR-09, NFR-06, NFR-08, NFR-10 | `[ ]` |
| E06 | Workspace hook runner | FR-15 | `[ ]` |
| E07 | Orchestrator (tick loop, eligibility, dispatch, reconcile, retry) — internal tracker only | FR-12, FR-13, FR-17, NFR-09 | `[ ]` |
| E08 | xterm.js terminal pane + Board UI + drag-drop spawn (board-driven mode) | FR-01, FR-02, FR-03, FR-04, FR-06, FR-07 | `[ ]` |
| E09 | WorkflowDefinition loader + hot reload + Monaco editor | FR-14 | `[ ]` |
| E10 | External tracker adapters (Linear, GitHub Issues, Jira) | FR-12, NFR-07 | `[ ]` |
| E11 | amux channels + atomic claim + watchdog | FR-16, FR-17, FR-18 | `[ ]` |
| E12 | DiffReview + GitHub PR open (Review → Done flow) | FR-10, FR-11 | `[ ]` |
| E13 | Snapshot / observability API + ops dashboard | FR-19, NFR-05 | `[ ]` |

---

## §3 Dependencies Between Epics

```
E01 ─► E02 ─┬─► E05 ─┬─► E07 ─┬─► E08 ──► E12
            │        │        │
E03 ────────┘        │        ├─► E10
                     │        │
E04 ─────────────────┘        ├─► E11
                              │
E06 ─────────────────────────►┘

E09 attaches after E07 (workflow drives orchestrator + agents).
E13 attaches after E07 (needs orchestrator + session state to snapshot).
```

- **Critical path:** E01 → E02 → E04 → E05 → E07 → E08 (first usable end-to-end).
- **Parallelisable after E04:** E03, E06 can land independently.
- **Post-MVP:** E09–E13 in any order once E07 + E08 land.

---

## §4 When To Add Per-Epic Architecture Docs

Defaults — refine if a later epic deviates:

| Trigger | Add per-epic arch doc? |
|---------|------------------------|
| Epic introduces a new external dependency (Pty.Net, libgit2sharp, Linear API) | **Yes** |
| Epic crosses ≥3 layers (Domain + Application + Infrastructure + Api + Client) | **Yes** |
| Epic adds a new state machine or background-loop service | **Yes** |
| Epic ≥6 stories | **Yes** |
| Epic is pure CRUD or pure UI on existing domain | No |

**Recommended now:**

- `/pts-vibe-tech-architecture epics E01` — Pty.Net Windows behaviour, JobObject lifecycle.
- `/pts-vibe-tech-architecture epics E02` — protocol adapter interface; how Codex app-server / Claude JSON-stream / RawPty share one seam.
- `/pts-vibe-tech-architecture epics E05` — RunAttempt phase machine + SignalR backpressure.
- `/pts-vibe-tech-architecture epics E07` — Orchestrator tick semantics, retry persistence, restart recovery.
- `/pts-vibe-test-architecture epics E01` — how to test PTY without flake.
- `/pts-vibe-test-architecture epics E07` — deterministic time / tracker stub for orchestrator loop.

---

## §5 Stories

> Every work item has a **TDD pre-condition** — the failing test to write
> first. If the pre-condition reads "smoke test" or "manual", reject the work
> item and rewrite.

---

### E01 — Pty.Net spike + Windows headed-agent proof

**Goal:** prove vs-pty.net can spawn `claude` / `codex` headed on Windows, stream stdout/stderr, accept stdin, kill cleanly.

**Stories:**

- **E01-S01** Console spike `tools/PtySpike/` launches `claude --version` via Pty.Net, captures output, exits 0.
  - Work items:
    - Add `Pty.Net` NuGet (latest from microsoft/vs-pty.net). *TDD:* xUnit test `PtyAgentRunner_can_spawn_and_capture_known_exit_code` runs `cmd.exe /c exit 42` and asserts exit code + captured stdout.
    - Spike capturing stdout/stderr to in-memory ring buffer. *TDD:* test `RingBuffer_overwrites_oldest_when_full`.
    - Send Ctrl-C / kill. *TDD:* test `PtyAgentRunner_kill_terminates_within_2s`.
- **E01-S02** Document Windows-specific findings (winpty vs conhost path, JobObject for memory cap) in `epics-E01-pty-spike.md`.
  - Work items:
    - Write findings doc. *TDD:* exception — doc-only story; mark per `06-test-strategy.md §2`.

---

### E02 — Agent abstraction

**Goal:** clean seam between domain and PTY/protocol details so Codex app-server, Claude JSON-stream, RawPty all plug in.

**Stories:**

- **E02-S01** `IPtyAgentRunner` interface in `Application/Interfaces/`.
  - Work items:
    - Define interface (`StartAsync`, `WriteInputAsync`, `ResizeAsync`, `KillAsync`, `OutputStream`). *TDD:* compile-time: contract test `IPtyAgentRunner_contract` (mocked impl asserts each method invoked).
- **E02-S02** `IAgentProtocolAdapter` interface + `RawPtyAdapter` impl.
  - Work items:
    - Adapter interface with `OnTextDelta`, `OnTurnComplete`, `OnError`. *TDD:* test `RawPtyAdapter_emits_text_delta_per_chunk` with fake stream.
    - Token usage extraction stub (returns null for raw PTY). *TDD:* test `RawPtyAdapter_token_usage_is_null`.
- **E02-S03** `AgentRegistry` JSON config (`agents.json`): name, exe, args template, auto-prompt rules.
  - Work items:
    - DTO + loader. *TDD:* test `AgentRegistry_loads_known_agents_from_json` parses sample with claude+codex+gemini.
    - Validation: missing exe = error. *TDD:* test `AgentRegistry_rejects_missing_exe_path`.
- **E02-S04** Pty.Net implementation in `Infrastructure/Agents/PtyAgentRunner.cs`.
  - Work items:
    - Wire Pty.Net to interface. *TDD:* integration test `PtyAgentRunner_echo_stdin_round_trips` writes "hello\n" to `cmd.exe`, asserts "hello" appears in output.
    - Resize handling. *TDD:* test `PtyAgentRunner_resize_does_not_crash_running_process`.

---

### E03 — Worktree manager + safety invariants

**Goal:** create / list / remove git worktrees with hard safety rules; auto-prune stale.

**Stories:**

- **E03-S01** `IWorktreeManager` interface + `WorktreeManager` impl (shell-out to `git worktree`).
  - Work items:
    - `CreateAsync(cardId, baseRef)` → returns `Worktree { path, branch }`. *TDD:* test `WorktreeManager_create_produces_worktree_under_root` against tmp git repo.
    - `RemoveAsync(path)`. *TDD:* test `WorktreeManager_remove_deletes_worktree_and_branch`.
    - `ListAsync()`. *TDD:* test `WorktreeManager_list_returns_only_worktrees_for_repo`.
- **E03-S02** Safety invariants: path under root; sanitised branch name; reject path traversal.
  - Work items:
    - Sanitiser. *TDD:* test `Sanitise_rejects_path_traversal_and_special_chars` (`../`, `;`, `\0`, etc.).
    - Root-confinement check. *TDD:* test `WorktreeManager_create_throws_when_resolved_path_escapes_root`.
- **E03-S03** Stale worktree auto-prune background service.
  - Work items:
    - `WorktreeJanitorHostedService` runs daily, removes worktrees with `Status = Stale && lastTouched > N days`. *TDD:* test `WorktreeJanitor_prunes_stale_worktrees` with fake clock.

---

### E04 — Domain model + EF migration

**Goal:** persist board / card / session / attempt / worktree / workflow-definition.

**Stories:**

- **E04-S01** Domain entities in `server/Domain/Entities/`.
  - Work items:
    - `Board`, `Column`, `Card`, `AgentSession`, `RunAttempt`, `Worktree`, `WorkflowDefinition`, `ExternalIssueRef`, `RetrySchedule`, `TokenUsage`. *TDD:* test `Card_state_machine_rejects_invalid_transition` (`Backlog → Done` direct = throw).
    - Enums: `AgentKind`, `SessionStatus`, `RunPhase`, `CardStatus`, `TrackerKind`. *TDD:* test `RunPhase_terminal_phases_are_immutable`.
    - State machine `CardStateMachine` mirroring `WorkflowStateMachine`. *TDD:* test `CardStateMachine_legal_transitions_match_spec`.
- **E04-S02** EF Core configuration + migration.
  - Work items:
    - `AppDbContext` entries + Fluent API. *TDD:* test `AppDbContext_round_trip_persists_board_card_session` against in-memory PostgreSQL via Testcontainers.
    - `dotnet ef migrations add KanbanInitial` (stop server first per AGENTS.md). *TDD:* test `Migration_KanbanInitial_creates_expected_tables` queries `information_schema`.
- **E04-S03** Repository interfaces + impls.
  - Work items:
    - `IBoardRepository`, `ICardRepository`, `ISessionRepository`, `IRunAttemptRepository`, `IWorktreeRepository`. *TDD:* test `CardRepository_save_then_load_round_trips_all_fields`.

---

### E05 — AgentSession lifecycle + RunAttempt phase machine + SignalR streaming

**Goal:** start a session for a card, stream output, transition phases, persist attempts.

**Stories:**

- **E05-S01** `RunAttempt` phase machine.
  - Work items:
    - States: `PreparingWorkspace → BuildingPrompt → LaunchingAgent → InitializingSession → StreamingTurn → Finishing → {Succeeded|Failed|TimedOut|Stalled|Canceled}`. *TDD:* test `RunAttemptPhaseMachine_illegal_transitions_throw`.
    - Per-phase timing capture. *TDD:* test `RunAttemptPhaseMachine_records_phase_durations`.
- **E05-S02** `AgentSessionService` lifecycle.
  - Work items:
    - `StartAsync(cardId, agentKind, prompt)` orchestrates Worktree → Hooks → PtyRunner → ProtocolAdapter → DB writes. *TDD:* integration test `AgentSessionService_start_to_first_text_delta` with `RawPtyAdapter` running `echo hello`.
    - `KillAsync` SIGTERM → 5s grace → SIGKILL. *TDD:* test `AgentSessionService_kill_force_kills_after_grace_period`.
    - `ForkAsync` clones session history → new worktree from same base. *TDD:* test `AgentSessionService_fork_creates_new_worktree_and_session`.
- **E05-S03** SignalR streaming via existing `AgentTextDelta` event.
  - Work items:
    - Filter by `sessionId` group; chunk large output (`NFR-10`). *TDD:* test `SignalR_AgentTextDelta_routes_to_session_group_only`.
    - Replay endpoint `GET /api/sessions/{id}/buffer`. *TDD:* test `SessionsApi_buffer_returns_full_ansi_replay`.
- **E05-S04** Stall detector.
  - Work items:
    - Track last-event timestamp; emit `RunAttemptStalled` after `stall_timeout_ms`. *TDD:* test `StallDetector_fires_after_configured_idle` with fake clock.

---

### E06 — Workspace hook runner

**Goal:** run shell hooks at workspace lifecycle points with timeouts + abort semantics.

**Stories:**

- **E06-S01** `IWorkspaceHookRunner` + impl.
  - Work items:
    - Run `after_create`, `before_run`, `after_run`, `before_remove` with cwd = workspace, timeout per hook. *TDD:* test `HookRunner_runs_script_in_workspace_cwd`.
    - Pre-hook failure aborts attempt; `after_run` failure logged-only. *TDD:* test `HookRunner_pre_hook_nonzero_aborts` and `HookRunner_post_hook_nonzero_does_not_abort`.
    - Timeout kills hook process. *TDD:* test `HookRunner_timeout_kills_hung_hook`.
- **E06-S02** Hook config in `WorkflowDefinition`.
  - Work items:
    - YAML schema accepts `hooks: { after_create, before_run, after_run, before_remove }`. *TDD:* test `WorkflowDefinition_parses_hooks_block`.

---

### E07 — Orchestrator (internal tracker only)

**Goal:** background tick loop dispatches eligible cards to sessions, enforces concurrency, retries with backoff.

**Stories:**

- **E07-S01** `IOrchestrator` + `OrchestratorTickHostedService`.
  - Work items:
    - `PollTick()` runs every `PollIntervalSeconds`. *TDD:* test `OrchestratorTick_invokes_dispatch_at_configured_interval` with fake `IHostApplicationLifetime` + fake clock.
    - Eligibility: active state, not running, not claimed, slot available. *TDD:* test `Orchestrator_skips_card_when_global_concurrency_full`.
    - Per-column concurrency override. *TDD:* test `Orchestrator_respects_max_concurrent_by_column`.
- **E07-S02** Atomic claim with optimistic concurrency token.
  - Work items:
    - `Card.OwnerSessionId` + `RowVersion`. *TDD:* test `Orchestrator_two_parallel_dispatches_only_one_wins` (DbUpdateConcurrencyException path).
- **E07-S03** Retry scheduler.
  - Work items:
    - Continuation 1s, failure `10s × 2^(n-1)` capped at 5m. *TDD:* test `RetryScheduler_backoff_matches_spec` for n=1..10.
    - Persist `RetrySchedule` (NFR-09 deviation from Symphony — Antiphon persists). *TDD:* test `RetryScheduler_survives_restart` (drop service, reload, due retries fire).
- **E07-S04** Reconcile loop.
  - Work items:
    - On tick: refresh state for running attempts; cancel stalled. *TDD:* test `Reconcile_cancels_attempt_when_card_externally_terminal`.
- **E07-S05** Pause / resume API.
  - Work items:
    - `POST /api/orchestrator/pause` / `resume`. *TDD:* test `Orchestrator_pause_skips_dispatch_until_resume`.

---

### E08 — xterm.js + Board UI + drag-drop spawn

**Goal:** board-driven mode end-to-end usable in browser.

**Stories:**

- **E08-S01** `BoardPage.tsx` + dnd-kit columns.
  - Work items:
    - Column grid; drag card between columns calls `PATCH /api/cards/{id}`. *TDD:* RTL test `BoardPage_drag_card_between_columns_invokes_patch`.
    - Optimistic update + invalidate `['boards', id]` on success. *TDD:* RTL test `BoardPage_optimistic_move_reverts_on_api_error`.
- **E08-S02** `CardModal.tsx` + `AgentPicker.tsx`.
  - Work items:
    - Modal shows card detail; spawn button posts `/api/cards/{id}/spawn`. *TDD:* RTL test `CardModal_spawn_calls_api_with_selected_agent`.
    - Picker pulls registry from `GET /api/agents`. *TDD:* RTL test `AgentPicker_renders_options_from_registry`.
- **E08-S03** `SessionTerminal.tsx` (xterm.js).
  - Work items:
    - Mount xterm; pull `/buffer` for backlog; subscribe SignalR `AgentTextDelta` filtered by sessionId. *TDD:* RTL test `SessionTerminal_renders_buffer_then_appends_live_deltas` with mocked hub.
    - Keystroke → `POST /api/sessions/{id}/input`. *TDD:* RTL test `SessionTerminal_sends_keystrokes_to_input_endpoint`.
    - Resize → `POST /api/sessions/{id}/resize` on viewport change. *TDD:* RTL test `SessionTerminal_resize_posts_new_dimensions`.
- **E08-S04** `SessionTabs.tsx` (multi-session per card).
  - Work items:
    - Tab strip; fork button creates new session. *TDD:* RTL test `SessionTabs_fork_button_calls_fork_endpoint_and_adds_tab`.

---

### E09 — WorkflowDefinition loader + hot reload + Monaco editor

**Goal:** edit per-board `WORKFLOW.md`; live reload; running attempts pinned to launch-time version.

**Stories:**

- **E09-S01** YAML+Markdown parser.
  - Work items:
    - `WorkflowDefinitionLoader` parses front matter (YAML) + body (Markdown). *TDD:* test `Loader_parses_front_matter_and_body_separately`.
    - Strict template renderer (unknown var → render fail). *TDD:* test `PromptRenderer_unknown_variable_throws`.
- **E09-S02** Hot reload via `IFileSystemWatcher`.
  - Work items:
    - On change → re-parse → publish; bad parse keeps last-good + emits `WorkflowReloaded { ok: false, error }`. *TDD:* test `Loader_invalid_reload_keeps_last_good`.
    - Pin version to attempt at launch. *TDD:* test `RunAttempt_uses_definition_version_at_launch_not_current`.
- **E09-S03** Monaco editor in `WorkflowEditor.tsx`.
  - Work items:
    - YAML+Markdown highlighting; PUT `/api/boards/{id}/workflow`. *TDD:* RTL test `WorkflowEditor_save_button_puts_content`.

---

### E10 — External tracker adapters

**Goal:** Linear / GitHub Issues / Jira poll → cards in board.

**Stories:**

- **E10-S01** `IIssueTracker` abstraction.
  - Work items:
    - `FetchCandidates`, `FetchByStates`, `FetchByIds`. *TDD:* contract test `IIssueTracker_contract` runs against fake tracker.
- **E10-S02** `LinearTracker` (GraphQL).
  - Work items:
    - GraphQL query for active states + project filter. *TDD:* test `LinearTracker_fetch_candidates_normalises_response` against recorded fixture.
    - Blocker derivation from inverse `blocks`. *TDD:* test `LinearTracker_blockers_derived_from_inverse_blocks`.
- **E10-S03** `GitHubIssuesTracker`.
  - Work items:
    - REST fetch; map labels lowercase; priority from label convention. *TDD:* test `GitHubIssuesTracker_normalises_priority_from_label_convention`.
- **E10-S04** `JiraTracker` (reuse existing Jira MCP integration as inspiration).
  - Work items:
    - JQL fetch; map status. *TDD:* test `JiraTracker_jql_filters_to_active_states`.
- **E10-S05** Single-fetch-per-tick + in-tick cache (NFR-07).
  - Work items:
    - `TrackerCache` scoped to tick. *TDD:* test `TrackerCache_dedupes_same_id_lookup_within_tick`.

---

### E11 — amux channels + atomic claim + watchdog

**Goal:** agents coordinate; @mentions delegate; stuck prompts auto-respond.

**Stories:**

- **E11-S01** `AgentChannelHub` (SignalR sub-hub).
  - Work items:
    - `card:{id}` group; `SendAsync(targetSessionId, msg)` injects into target PTY stdin. *TDD:* test `ChannelHub_mention_routes_to_target_session_stdin`.
- **E11-S02** Output filter detects `@sessionName` mentions.
  - Work items:
    - Regex scanner over text deltas. *TDD:* test `MentionScanner_extracts_at_mentions_from_ansi_stripped_text`.
- **E11-S03** Atomic claim already in E07-S02 — re-export to chat trigger path.
  - Work items:
    - Channel-driven claim path uses same DB primitive. *TDD:* test `Channel_delegate_claims_via_optimistic_concurrency`.
- **E11-S04** `WatchdogHostedService`.
  - Work items:
    - Pattern registry (`Press Enter`, `(Y/n)`, `[y/N]`, prompt-specific). *TDD:* test `Watchdog_matches_known_prompt_patterns`.
    - Auto-respond per rule (configurable: yes / no / enter / skip). *TDD:* test `Watchdog_auto_responds_with_configured_input`.
    - Cooldown to prevent loops. *TDD:* test `Watchdog_does_not_respond_twice_within_cooldown`.

---

### E12 — DiffReview + GitHub PR open

**Goal:** Review column shows diff; inline comments → channel; Done opens PR.

**Stories:**

- **E12-S01** `DiffReview.tsx` reusing artifact viewer.
  - Work items:
    - Fetch diff `worktree vs base`; render unified. *TDD:* RTL test `DiffReview_renders_added_removed_lines`.
    - Inline comment input. *TDD:* RTL test `DiffReview_post_comment_calls_api`.
- **E12-S02** Comment → channel injection.
  - Work items:
    - `POST /api/cards/{id}/comments` → `AgentChannelHub.SendAsync(activeSessionId, formatted)`. *TDD:* test `CommentApi_post_routes_to_active_session_stdin`.
- **E12-S03** GitHub PR open.
  - Work items:
    - `POST /api/cards/{id}/pr` pushes branch + opens PR via existing GitHub MCP. *TDD:* integration test `CardPrApi_open_pushes_branch_and_creates_pr` with stub GitHub.
    - PR description templated from card + last attempt summary. *TDD:* test `PrDescription_includes_card_title_and_attempt_summary`.

---

### E13 — Snapshot / observability API + ops dashboard

**Goal:** see what orchestrator is doing right now.

**Stories:**

- **E13-S01** `GET /api/orchestrator/state`.
  - Work items:
    - Snapshot DTO: running sessions (turn count, tokens, last event), retry queue, aggregate tokens, runtime seconds, rate limits. *TDD:* test `OrchestratorStateApi_snapshot_includes_running_and_retry`.
- **E13-S02** `OrchestratorPanel.tsx`.
  - Work items:
    - Polls `/state` every 5s; renders running + retry queue. *TDD:* RTL test `OrchestratorPanel_renders_running_sessions_from_state_endpoint`.
- **E13-S03** Per-session memory cap via Windows `JobObject` (NFR-05).
  - Work items:
    - Wrap each PTY child in JobObject with memory limit. *TDD:* integration test `JobObject_kills_session_when_memory_limit_exceeded` (allocate-until-OOM child).
    - Surface `MemoryKilled` exit reason. *TDD:* test `RunAttempt_records_memory_killed_exit_reason`.

---

## Open questions (parked in TODO.md candidates)

- Should `Card` wrap existing `Workflow` or be sibling? (plan.md risk #5)
- Auth/secrets: shared user creds vs per-agent secret store? (plan.md risk #3)
- Hook sandboxing — full trust or restricted? (plan.md risk #9)
- vs-pty.net Windows behaviour on Server Core / older Win10 — may need fallback to `Process` + redirect (no PTY). Decide in E01.
