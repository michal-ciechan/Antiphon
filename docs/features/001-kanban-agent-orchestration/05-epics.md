# Epics — Kanban + Agent PTY Orchestration

> **Source plan:** `plan.md` in this directory.
> **Requirements:** [`01-requirements.md`](01-requirements.md).
> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Stories live per-epic** in `E_NN_stories.md` files — iterate there.

---

## 1. Epic Index

| ID  | Epic | Requirements | Stories | Status |
|-----|------|--------------|---------|--------|
| E01 | PTY substrate + Windows headed-agent proof (`Antiphon.Agents.Pty` library) | FR-04, FR-06, FR-07, FR-09, NFR-01, NFR-06, NFR-08 | [E_01-stories.md](epics/E_01-stories.md) | `[x]` |
| E02 | Agent abstraction (`IAgentProtocolAdapter` + registry) | FR-04, FR-05, FR-06, NFR-01 | [E_02-stories.md](epics/E_02-stories.md) | `[x]` |
| E03 | Worktree manager + safety invariants | FR-04, FR-08, FR-20, NFR-03, NFR-04 | [E_03-stories.md](epics/E_03-stories.md) | `[x]` |
| E04 | Domain model + EF migration (Board / Card / AgentSession / RunAttempt / Worktree / WorkflowDefinition) | FR-01, FR-02, NFR-02, NFR-12 | [E_04-stories.md](epics/E_04-stories.md) | `[x]` |
| E05 | AgentSession lifecycle + RunAttempt phase machine + SignalR streaming | FR-06, FR-09, NFR-06, NFR-08, NFR-10 | [E_05-stories.md](epics/E_05-stories.md) | `[x]` |
| E06 | Workspace hook runner | FR-15 | [E_06-stories.md](epics/E_06-stories.md) | `[x]` |
| E07 | Orchestrator (tick loop, eligibility, dispatch, reconcile, retry) — internal tracker only | FR-12, FR-13, FR-17, NFR-09 | [E_07-stories.md](epics/E_07-stories.md) | `[x]` |
| E08 | xterm.js terminal pane + Board UI + drag-drop spawn (board-driven mode) | FR-01, FR-02, FR-03, FR-04, FR-06, FR-07 | [E_08-stories.md](epics/E_08-stories.md) | `[x]` |
| E09 | WorkflowDefinition loader + hot reload + Monaco editor | FR-14 | [E_09-stories.md](epics/E_09-stories.md) | `[x]` |
| E10 | External tracker adapters (Linear, GitHub Issues, Jira) | FR-12, NFR-07 | [E_10-stories.md](epics/E_10-stories.md) | `[x]` |
| E11 | amux channels + atomic claim + watchdog | FR-16, FR-17, FR-18 | [E_11-stories.md](epics/E_11-stories.md) | `[ ]` |
| E12 | DiffReview + GitHub PR open (Review → Done flow) | FR-10, FR-11 | [E_12-stories.md](epics/E_12-stories.md) | `[ ]` |
| E13 | Snapshot / observability API + ops dashboard | FR-19, NFR-05 | [E_13-stories.md](epics/E_13-stories.md) | `[ ]` |
| E14 | Migrate test suites from xUnit → TUnit + FluentAssertions → Shouldly (run **early** — after E01-S01 lands, before E02 substantial work) | NFR-* (testability) | [E_14-stories.md](epics/E_14-stories.md) | `[x]` |

---

## 2. Dependencies Between Epics

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
- **E14 (TUnit migration):** sequence between E01-S01 (proof shipped on xUnit) and the bulk of E02. Cheap to migrate while suite is small; expensive once E02–E07 add hundreds of tests.
