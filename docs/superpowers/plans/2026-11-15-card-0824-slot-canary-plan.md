# CARD-0824 wave 1 Plan slot canary

Scope: the bounded documentation canary commissioned as task `870a2044-4aa6-4611-9723-2bb745fd7755`. The eight-line [canary note](2026-11-15-slot-canary-note.md) is its requested deliverable. This companion supplies the required Plan-stage structure; neither artifact changes rollout settings or implements the broader [enablement plan](2026-11-14-card-0824-all-roles-enablement-plan.md#first-task-release-and-reuse).

## Decisions

- D-1: Use the already assigned branch and leased worktree because the caller authorized the first ordinary Plan canary. Reject a new worktree or branch: either would abandon the lease under observation.
- D-2: Record lease identity now and hand release/reuse observation to the caller after settlement. Reject claiming release from inside the active leased session or invoking release manually: the live session still owns the lease.
- D-3: Run no builds or tests because this slice changes only narrative documentation and the brief explicitly excludes them. Reject adding a Code/TestDesign stage or broadening rollout scope for this canary.
- D-4: Follow the documented soft rollback with roles `none` and support kept enabled until drain. Reject disabling support first or deleting slot state because release depends on that support and custody must survive.

## Ground truth

| Brief assumption | Code or observed behavior |
|---|---|
| This Plan uses a reusable slot. | Task API reports role `Plan` and `WorkspaceSlotDecision` = slot, reason `listed`; its preparation matches the ledger. |
| Lease identity must match exact source and branch. | Ledger: slot `ws-01`, directory/lease generation `1`/`1`, task `870a2044-4aa6-4611-9723-2bb745fd7755`, preparation `1c33a514-b484-404c-997a-20b769c43cf7`, branch `feat/card-task-870a2044`, base `a71c45ab4d5703d480f4eee4c4e30adab45bc4f9`; initial Git HEAD matches. |
| A free directory can serve the next task. | `src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs`: replay is task/preparation-scoped; selection orders Free slots by number, increments generation, and verifies exact source. |
| Completion releases the slot. | `server/Application/Services/WorkspaceSlotReleaseService.cs` also requires no runner seat, Pending/Held sync debt or live follow-up. `RunnerWorkspaceSlotPool.cs` checks custody before cleanup; release is not established by this active task. |
| Roles `none` safely rolls back new admission. | `server/Application/Services/WorkspaceSlotRoleGate.cs` reads the role monitor per decision; the Docker owner documents its restart requirement and keeping support until drain. |

Owner: [runner workspace slots](../../docker-stack.md#runner-workspace-slots-on-the-volume-card-0824-s1). Read-only placement discovery completed through `GET /api/runner-defaults` and `GET /api/session-runners`; no host pin or platform override is added.

## Slices

| Slice | Files | Verification and tests |
|---|---|---|
| S1: bounded canary documentation | `docs/superpowers/plans/2026-11-15-slot-canary-note.md`; this companion plan | Inspect note length/content, exact branch/base and ledger identity; commit/push only the assigned task ref. Tests: none. |

Existing tests affected: **none**. No migrations, public DTOs/routes, operation labels, remote functions, statement budgets or executable selections change; no [CARD-1782 impacted-classes](../../testing-and-build.md#impacted-classes-card-1782) row is implicated.

## Verification design

The closed verification scope is manual documentation and identity inspection. Compare the note against the Docker owner, verify its maximum eight lines, inspect only the two Markdown additions with `git diff --check`, and confirm origin's assigned branch tip after push. Retain the first 40 ledger lines and runtime identity in the final report; no secret is printed.

### Checkpoints

Lane: `portable-docs`. No executable checkpoint rows, class/method filters, suffix-collision roster, build or test driver apply. The caller owns subsequent stopped-session, confirmed-sync, Free/detached published-SHA and generation-increment reuse observations under the existing enablement plan; these remain outstanding operational observations, not results of this slice.

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards). No tests/builds are scheduled: per-test/row/batch/run durations, measured p95/sample provenance and approximately 2x-p95 test timeouts are not applicable. No test timeout changes or performance claims are made. Container/readiness, data preparation, host startup, migrations, snapshot/clone, test body, teardown and build-slot admission phases do not run. Shared fixture setup and per-test isolation are unnecessary; no project graph/downstream test selection changes. Record manual identity and Git outcomes in the final report. Read-only discovery HTTP calls use a finite 15-second diagnostic bound; this is not a measured test budget. The broader canary's release/reuse timing remains the caller's separate observation, with no guaranteed release deadline.

This slice adds no session that waits for input. It ends normally; no parking or input-release deadline is introduced.
