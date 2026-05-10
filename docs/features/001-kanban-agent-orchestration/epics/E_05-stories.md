# E05 — AgentSession lifecycle + RunAttempt phase machine + SignalR streaming

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** start a session for a card, stream output, transition phases, persist attempts.

**Covers:** FR-06, FR-09, NFR-06, NFR-08, NFR-10

---

## Stories

- **E05-S01** `[ ]` `RunAttempt` phase machine.
  - Work items:
    - States: `PreparingWorkspace → BuildingPrompt → LaunchingAgent → InitializingSession → StreamingTurn → Finishing → {Succeeded|Failed|TimedOut|Stalled|Canceled}`. *TDD:* test `RunAttemptPhaseMachine_illegal_transitions_throw`.
    - Per-phase timing capture. *TDD:* test `RunAttemptPhaseMachine_records_phase_durations`.
- **E05-S02** `[ ]` `AgentSessionService` lifecycle.
  - Work items:
    - `StartAsync(cardId, agentKind, prompt)` orchestrates Worktree → Hooks → PtyRunner → ProtocolAdapter → DB writes. *TDD:* integration test `AgentSessionService_start_to_first_text_delta` with `RawPtyAdapter` running `echo hello`.
    - `KillAsync` SIGTERM → 5s grace → SIGKILL. *TDD:* test `AgentSessionService_kill_force_kills_after_grace_period`.
    - `ForkAsync` clones session history → new worktree from same base. *TDD:* test `AgentSessionService_fork_creates_new_worktree_and_session`.
- **E05-S03** `[ ]` SignalR streaming via existing `AgentTextDelta` event.
  - Work items:
    - Filter by `sessionId` group; chunk large output (`NFR-10`). *TDD:* test `SignalR_AgentTextDelta_routes_to_session_group_only`.
    - Replay endpoint `GET /api/sessions/{id}/buffer`. *TDD:* test `SessionsApi_buffer_returns_full_ansi_replay`.
- **E05-S04** `[ ]` Stall detector.
  - Work items:
    - Track last-event timestamp; emit `RunAttemptStalled` after `stall_timeout_ms`. *TDD:* test `StallDetector_fires_after_configured_idle` with fake clock.
