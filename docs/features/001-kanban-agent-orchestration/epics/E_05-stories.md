# E05 — AgentSession lifecycle + RunAttempt phase machine + SignalR streaming

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** start a session for a card, stream output, transition phases, persist attempts.

**Covers:** FR-06, FR-09, NFR-06, NFR-08, NFR-10

---

## Stories

- **E05-S01** `[x]` `RunAttempt` phase machine.
  - Work items:
    - States: `PreparingWorkspace → BuildingPrompt → LaunchingAgent → InitializingSession → StreamingTurn → Finishing → {Succeeded|Failed|TimedOut|Stalled|Canceled}`. *TDD:* test `RunAttemptPhaseMachine_illegal_transitions_throw`.
    - Per-phase timing capture. *TDD:* test `RunAttemptPhaseMachine_records_phase_durations`.
- **E05-S02** `[x]` `AgentSessionService` lifecycle.
  - Work items:
    - `StartAsync(cardId, agentKind, prompt)` orchestrates Worktree → Hooks → PtyRunner → ProtocolAdapter → DB writes. *TDD:* integration test `AgentSessionService_start_to_first_text_delta` with `RawPtyAdapter` running `echo hello`.
    - `KillAsync` SIGTERM → 5s grace → SIGKILL. *TDD:* test `AgentSessionService_kill_force_kills_after_grace_period`.
    - `ForkAsync` deferred to the session-history/amux slice: current worktree naming is deterministic per card, and there is not yet a persisted transcript model to clone safely.
- **E05-S03** `[x]` SignalR streaming via existing `AgentTextDelta` event.
  - Work items:
    - Filter by `sessionId` group; chunk large output (`NFR-10`). *TDD:* test `SignalR_AgentTextDelta_routes_to_session_group_only`.
    - Replay endpoint `GET /api/sessions/{id}/buffer`. *TDD:* test `SessionsApi_buffer_returns_full_ansi_replay`.
- **E05-S04** `[x]` Stall detector.
  - Work items:
    - Track last-event timestamp; emit `RunAttemptStalled` after `stall_timeout_ms`. *TDD:* test `StallDetector_fires_after_configured_idle` with fake clock.

## Implementation notes

- `AgentSessionRuntime` owns live adapters plus bounded ANSI replay buffers; `AgentTextDelta` payloads are chunked and sequenced per `session-{id}` group.
- `AgentSessionService` persists `RunAttempt` before worktree/hook/PTY work starts so failures leave an audit row.
- `RunAttemptStallDetector` only stalls idle `StreamingTurn` attempts and kills/disposes the live adapter when present.
