# E04 — Domain model + EF migration

> **Status:** `[x]` **Closed 2026-05-15.**
> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** persist board / card / session / attempt / worktree / workflow-definition.

**Covers:** FR-01, FR-02, NFR-02, NFR-12

---

## Stories

- **E04-S01** `[x]` Domain entities in `server/Domain/Entities/`.
  - Work items:
    - `Board`, `BoardColumn`, `Card`, `AgentSession`, `RunAttempt`, `Worktree`, `BoardWorkflowDefinition`, `ExternalIssueRef`, `RetrySchedule`, `TokenUsage`. *TDD:* test `Card_state_machine_rejects_invalid_transition` (`Backlog → Done` direct = throw).
    - Enums: `SessionStatus`, `RunPhase`, `CardStatus`, `TrackerKind`, `WorktreeStatus`. `AgentKind` already landed in E02.
    - State machine `CardStateMachine` mirroring `WorkflowStateMachine`. *TDD:* test `CardStateMachine_legal_transitions_match_spec`.
- **E04-S02** `[x]` EF Core configuration + migration.
  - Work items:
    - `AppDbContext` entries + Fluent API. *TDD:* test `AppDbContext_round_trip_persists_board_card_session_worktree_attempt` against PostgreSQL via Testcontainers.
    - `dotnet ef migrations add KanbanInitial` (stop server first per AGENTS.md). *TDD:* test `Migration_KanbanInitial_creates_expected_tables` queries `information_schema`.
- **E04-S03** `[-]` ~~Repository interfaces + impls.~~ Dropped per project rule: use `AppDbContext` directly; no repository wrappers.

---

## Notes

- E04 deliberately uses **Card** terminology for the internal kanban domain. External tracker issues map through `ExternalIssueRef`; `Issue` naming does not leak into the persisted domain model.
- `BoardWorkflowDefinition` avoids colliding with the existing `Domain/ValueObjects/WorkflowDefinition`.
- `Card.ConcurrencyToken` is an explicit EF concurrency token for PostgreSQL; application code must rotate it when claiming/updating cards.
- `BoardColumn.StateKey`, `IsActive`, `IsTerminal`, and `MaxConcurrentSessions` preserve configurable column/state behavior for E07/E08.

---

## Acceptance

- ✅ `dotnet ef migrations add KanbanInitial --project server` after `.\stop-server.ps1`.
- ✅ `dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter "/*/*/*/*[Category=Integration]"` — 3 pass.
- ✅ `dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter "/*/*/*/*[Category=Unit]"` — 25 pass.
- ✅ `dotnet run --project tests/Antiphon.Tests --no-build` — 150 pass, 1 skipped.
