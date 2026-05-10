# E04 — Domain model + EF migration

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** persist board / card / session / attempt / worktree / workflow-definition.

**Covers:** FR-01, FR-02, NFR-02, NFR-12

---

## Stories

- **E04-S01** `[ ]` Domain entities in `server/Domain/Entities/`.
  - Work items:
    - `Board`, `Column`, `Card`, `AgentSession`, `RunAttempt`, `Worktree`, `WorkflowDefinition`, `ExternalIssueRef`, `RetrySchedule`, `TokenUsage`. *TDD:* test `Card_state_machine_rejects_invalid_transition` (`Backlog → Done` direct = throw).
    - Enums: `AgentKind`, `SessionStatus`, `RunPhase`, `CardStatus`, `TrackerKind`. *TDD:* test `RunPhase_terminal_phases_are_immutable`.
    - State machine `CardStateMachine` mirroring `WorkflowStateMachine`. *TDD:* test `CardStateMachine_legal_transitions_match_spec`.
- **E04-S02** `[ ]` EF Core configuration + migration.
  - Work items:
    - `AppDbContext` entries + Fluent API. *TDD:* test `AppDbContext_round_trip_persists_board_card_session` against in-memory PostgreSQL via Testcontainers.
    - `dotnet ef migrations add KanbanInitial` (stop server first per AGENTS.md). *TDD:* test `Migration_KanbanInitial_creates_expected_tables` queries `information_schema`.
- **E04-S03** `[ ]` Repository interfaces + impls.
  - Work items:
    - `IBoardRepository`, `ICardRepository`, `ISessionRepository`, `IRunAttemptRepository`, `IWorktreeRepository`. *TDD:* test `CardRepository_save_then_load_round_trips_all_fields`.
