# E13 — Snapshot / observability API + ops dashboard

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** see what orchestrator is doing right now.

**Covers:** FR-19, NFR-05

---

## Stories

- **E13-S01** `[ ]` `GET /api/orchestrator/state`.
  - Work items:
    - Snapshot DTO: running sessions (turn count, tokens, last event), retry queue, aggregate tokens, runtime seconds, rate limits. *TDD:* test `OrchestratorStateApi_snapshot_includes_running_and_retry`.
- **E13-S02** `[ ]` `OrchestratorPanel.tsx`.
  - Work items:
    - Polls `/state` every 5s; renders running + retry queue. *TDD:* RTL test `OrchestratorPanel_renders_running_sessions_from_state_endpoint`.
- **E13-S03** `[ ]` Per-session memory cap via Windows `JobObject` (NFR-05).
  - Work items:
    - Wrap each PTY child in JobObject with memory limit. *TDD:* integration test `JobObject_kills_session_when_memory_limit_exceeded` (allocate-until-OOM child).
    - Surface `MemoryKilled` exit reason. *TDD:* test `RunAttempt_records_memory_killed_exit_reason`.
