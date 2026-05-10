# E07 — Orchestrator (tick loop, eligibility, dispatch, reconcile, retry) — internal tracker only

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** background tick loop dispatches eligible cards to sessions, enforces concurrency, retries with backoff.

**Covers:** FR-12, FR-13, FR-17, NFR-09

---

## Stories

- **E07-S01** `[ ]` `IOrchestrator` + `OrchestratorTickHostedService`.
  - Work items:
    - `PollTick()` runs every `PollIntervalSeconds`. *TDD:* test `OrchestratorTick_invokes_dispatch_at_configured_interval` with fake `IHostApplicationLifetime` + fake clock.
    - Eligibility: active state, not running, not claimed, slot available. *TDD:* test `Orchestrator_skips_card_when_global_concurrency_full`.
    - Per-column concurrency override. *TDD:* test `Orchestrator_respects_max_concurrent_by_column`.
- **E07-S02** `[ ]` Atomic claim with optimistic concurrency token.
  - Work items:
    - `Card.OwnerSessionId` + `RowVersion`. *TDD:* test `Orchestrator_two_parallel_dispatches_only_one_wins` (DbUpdateConcurrencyException path).
- **E07-S03** `[ ]` Retry scheduler.
  - Work items:
    - Continuation 1s, failure `10s × 2^(n-1)` capped at 5m. *TDD:* test `RetryScheduler_backoff_matches_spec` for n=1..10.
    - Persist `RetrySchedule` (NFR-09 deviation from Symphony — Antiphon persists). *TDD:* test `RetryScheduler_survives_restart` (drop service, reload, due retries fire).
- **E07-S04** `[ ]` Reconcile loop.
  - Work items:
    - On tick: refresh state for running attempts; cancel stalled. *TDD:* test `Reconcile_cancels_attempt_when_card_externally_terminal`.
- **E07-S05** `[ ]` Pause / resume API.
  - Work items:
    - `POST /api/orchestrator/pause` / `resume`. *TDD:* test `Orchestrator_pause_skips_dispatch_until_resume`.
