# CARD-1610: Hold a live task at its role ceiling

Date: 2026-10-30. Round 2 Plan, replacing the round-1 design in place.
Source base: `93a21a1fac2ab3965cce5a48503d54fb71132c07`.
Status: early design skeleton; detailed verification and source census follow in
this same task. No product changes are authorized by this Plan dispatch.

## Decisions

- **D-1 — Adopt a non-failing ceiling hold for all roles, including Deploy.**
  A Working session at its role ceiling remains alive; its task becomes Blocked
  with a typed ceiling question. Cancel, continue/extend via Reply, and ordinary
  report settlement remain available. A hold must remain in phase supervision:
  model-wait and tool deadlines still fail real stalls. CARD-1151 is a precedent
  for detection-only handling, not an existing Blocked transition (its code
  leaves task status unchanged). Separate phase and ceiling decisions so a larger
  ceiling fraction cannot mask an overdue phase.
- **D-2 — Keep failure history and tell remaining live failed sessions.**
  Persist one attempt-bound note obligation for non-destructive failures; deliver
  through the existing queue at a safe turn boundary. No timer grants stop authority.
- **D-3 — Correct the control-verb premise.** Retry already calls StopDelegateAsync
  unless release is confirmed; Cancel stops an open task. Delete the new operator
  token rule and task-oriented release protocol from round 1.
- **D-4 — Cover Deploy explicitly.** Its default ceiling is 240 minutes while the
  rollout defaults to a 480-minute drain wait. All-role hold semantics apply.
  Evaluate bounded declared ceiling extensions and bounded drain polling without
  weakening the 90-minute tool deadline or the drain safety gates.
- **D-5 — Delete automatic late Failed-to-Succeeded correction.** A held task
  settles normally. Historical/remaining Failed reports require explicit triage,
  fenced by card and role, with next=decide; never start a second chain.
- **D-6 — Drop the broker wait ledger.** No broker migrations, interval accounting,
  runner protocol or lock-order convergence are needed to prevent ceiling failure.
  Existing wait metrics remain diagnostic. Target master plus separately reviewed
  rc ports after CARD-1604; coordinate Deploy integration with CARD-1615.
- **D-7 — Related work matters.** Preserve CARD-1604 token-count semantics and
  CARD-1606's exclusion of monitor self-progress; retain CARD-0153 loop detection.
  CARD-1573 owns sharded Unit speedups, CARD-1151 boot detection, CARD-1615 the
  Deploy/landing deadlock. Incidents: Review 7ee3b07a, the Astra plans, Deploy
  2e4f7375. Source and live evidence will be distinguished below.
- **D-8 — Verification classes need a declared lane, Slow status and limiter.**
  Add classification/limiter guard checkpoint coverage and exact roster floors.
- **D-9 — Delivery proof is a complete UserPrompt.** Hold notes to the parent and
  failure notes to the delegate require eligible, busy and post-commit crash arms.
- **D-10 — A warm Working session past TTL is detected, never killed.** Remove
  the janitor's time-based kill and add durable attention for the taskless case.

Rejected: terminal ceiling failure plus compensating release/ledger/recovery
subsystems; resetting ceilings on chatter; an automatic hard-stop timer;
changing global phase deadlines to hide long test or drain commands.

## Ground-truth table

| Assumption | Source truth | Consequence |
|---|---|---|
| A role ceiling proves a hang | TryFailOverdueAsync fails even with a recent ToolCall | Hold Working sessions, preserve phase failure |
| Boot detection already blocks tasks | AgentTaskDispatcher.BootStall.cs explicitly preserves status | Reuse detection principle, implement hold transitions explicitly |
| Retry cannot stop Failed+live | RequeueCoreAsync calls StopDelegateAsync | Reuse existing authority and controls |
| Blocked automatically keeps phase/report processing | Deadline/reply selectors currently accept Dispatched/Working | Explicitly admit typed ceiling holds; do not widen all Blocked tasks |
| Deploy fits its ceiling | Default drain wait 480 exceeds ceiling 240 | All-role hold plus declared budget and bounded command waits |

## Slices

1. Deadline hold, typed question, continue/extend, settlement eligibility and durable
   caller receipt. Files: TaskDeadlinePolicy, AgentTaskDispatcher, AgentTaskService,
   AgentTaskReplyService and their existing runtime-action consumers; tests to follow.
2. Failed+live notice and visibility, janitor detection-only handling. Files:
   failure notification/queue, AttentionService, pool janitor; tests to follow.
3. Deploy budget/drain integration and operator documentation. Files:
   delegate.ps1, deploy-server2.ps1, DTO/model/configuration, relevant owner docs.
4. Exact checkpoint manifest and delivery/custody regressions; guards and counts.

## Performance budget

Apply docs/testing-and-build.md#performance-standards and
`docs/test-performance-standards.md`. No product tests run in this Plan stage;
checkpoint import is the requested tooling validation. Detailed provisional costs
and exact filters follow. Reuse one producer build per unchanged committed slice;
share database/container/migrations once per run and isolate tests with schemas.
Record admission, build, startup, migration, body, teardown and external-call time
separately. Missing samples are not measured p95; TestDesign must collect bounded
samples and set timeouts near 2x measured p95. Any run expected over 30 minutes
requires Debug before repetition. Import defaults are 45/15/90 minutes for
slot/row/total, not the round-1 plan's claimed limits.

## Sessions waiting for input

A ceiling hold keeps the Working session and its seat. No automatic release
exists while parking is disabled (CARD-1083). Explicit Cancel stops an open task;
Retry stops the old attempt; a normal exit or qualified existing release can
release it. Parking must never treat a Working ceiling hold as an idle question.

## Handoff

This round returns to independent design Review; TestDesign remains separate
before Code. The next revisions in this task replace this skeleton with the
source-backed contract, per-slice tests and imported checkpoint table.
