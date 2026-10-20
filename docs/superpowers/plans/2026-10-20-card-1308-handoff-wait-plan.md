# CARD-1308: wake deferred review handoffs and finish temporary-block coverage

## Outcome and inspected source

A-1 still holds; A-2 is intended behavior; A-3 is partly covered already. Keep
this card open for the small coordinator fix and the missing delivery tests.
This is a Plan artifact; TestDesign remains a separate stage.

Inspected on 2026-10-20 after fetching the refs:

| Ref | Full tip |
|---|---|
| Assigned base and current `origin/master` | `2d49f44111f533d9d242ae529304ae6bd95e5ea0` |
| Card-labelled `origin/rc/2026-10-15` | `61117dd1663d2f47ce4708c18b609b8936491986` |
| Also inspected `origin/rc/2026-10-18` | `81cfb267a0af5c0ddb65e8b238af355067bee9cc` |

The coordinator is blob `a89d98b9ee05855dd0de163f761518b7989c3908`
on all three refs. `SessionRuntimeActionDeferralTests.cs` and
`SessionRuntimeActionRoutingTests.cs` also match across these refs. Therefore
the handoff wait and the existing Herdr/rules delivery coverage are on both rc
lines too. Other runtime/queue code differs, including later async-read work;
this is not a claim that the complete release and master trees match.
No rc pick, activation or release decision is part of this master-only task.

Evidence is source inspection, including the current card text, not a test pass.
No build or test was needed or run during Plan. The optional single narrow
investigative run was unused.

## Ground truth

Paths below are relative to the repository; line numbers describe the inspected
master tip.

| Card assumption | Current code / test evidence | Disposition |
|---|---|---|
| A-1: a deferred handoff can hold later delivery/finished notification for up to 30 seconds. | `server/Application/Services/SessionRuntimeActionCoordinator.cs:157-172` requires a boundary/manual/status head before `superseded` can bypass `DueAt`. A `review:<queue-id>` head is excluded. `RetryAt` caps deferral at 30 seconds. Routing newer transcript actions first does not remove this queue-phase head. | Still holds. Fix early execution separately from replacement. |
| The wait generates repeat worker logs. | `DiscoverAsync` finds the due successor once per one-second scan; `DrainPageAsync` logs start/end through `ObserveDebtAsync` even when the head returns before execution. | Consequence of the same wait. Drain eligible work promptly; no separate logging project. |
| Later CARD-1259 work may already have fixed it. | Master history includes `e282b0e5252e1d4000da33efd2714dd68d94111b`, ordering repair `a8a7fc5b59f132bba5b4f0f87a89ba3b4dfb5344`, and handoff identity repair `6e23e0b4c8d6b2ae12218c2c0058593dbf773530`. The last intentionally excluded typed handoffs from coalescing; no later coordinator change appears at this master tip. | Keep that identity repair; it explains why merely widening `superseded` is wrong. |
| A-2: an older replaced boundary can publish `SessionFinished` before a successor delivers. | `SessionMessageQueueService.cs:1592-1602` explicitly publishes for a superseded turn boundary. `docs/session-runtime-invariants.md` documents this, and `C1259_new_requests_replace_backed_off_queue_work` asserts boundary counts. | Intended UI behavior. Preserve; no product change. |
| A-3: Herdr-blocked and rules-held lack ordinary unblock-to-delivery tests. | `SessionRuntimeActionRoutingTests.C1259_new_working_or_blocked_state_defers_old_idle_action`, arguments `herdr-blocked` and `rules-held`, asserts zero input/attempts while held, clears the gate, then asserts one complete stored UserPrompt, `Delivered`, one attempt and one submitted body. | Already represented in ordinary source tests. Reuse; do not add duplicates or claim fresh execution/Mutation qualification. |
| A-3: transport, modal and the remaining Herdr temporary barriers have equivalent coverage. | `SessionMessageQueueService.cs:2210-2257,2729-2731` has transport-unavailable, Herdr pending/unreachable, modal and post-send `BackendUnreachable` temporary returns. Existing `Herdr_unreachable_*` and `C514_WhenIdle_flush_waits_behind_open_modal` tests cover blocking, but do not retain a runtime action and drive that action through unblock to confirmed delivery. | Add four narrowly selected cases: transport-unavailable, Herdr pending/unreachable, Herdr send-unreachable, modal. |

## Decisions

- **D-1 — Master-only, advisory-sized scope.** Change the coordinator and tests,
  plus the owner sentence describing early handoff execution. Keep CARD-1304's
  broader pump verification separate. Reject a new scheduler, timeout change,
  whole-Unit run or rc backport in this card.
- **D-2 — Separate eligibility to run early from eligibility to coalesce.**
  Extract/reuse the existing newer queue-request predicate, including its
  ordering/tie rules. A queue-phase head with `FailureReason == "Deferred"`
  and a typed `review:` trigger may execute before `DueAt` when a newer routed
  queue wakeup sits behind it. Call its normal queue path with
  `superseded: false`. Existing boundary/status coalescing remains as written.
  Reject removing the head-type condition from `superseded`: that acknowledges
  a review handoff without doing its own work.
- **D-3 — Bound an unsuccessful early recheck.** If that execution is still
  Deferred, end the drain page immediately after persisting its normal retry.
  Return an explicit execution disposition if needed. Otherwise the unchanged
  successor predicate could repeatedly bypass the new due time within all 128
  page iterations and continually re-hint the session. With no newer wake,
  retain the existing 1/2/4/8/16/30-second backoff. A still-present newer wake
  may cause another recheck on later discovery; this change promises prompt
  handling of newly eligible work, not suppression of every blocked-state log.
- **D-4 — Preserve all delivery authority.** Early execution still passes
  generation, routing, Working, transport, Herdr, rules, modal and attempt-cap
  gates. Preserve the handoff's ID, trigger, queue/thread association and
  late-confirm obligation through acknowledgement. A wakeup or cleared modal
  is never a delivery receipt.
- **D-5 — Reuse covered behaviors and add only missing cases.** Ordinary new
  temporary-block cases should already pass production behavior. Do not invent
  a product regression to obtain red. TestDesign must bind one method-scoped
  positive control per covered barrier; execution belongs to the separate
  post-land SourceLanding Mutation stage. Existing CARD-1259 verification debts
  remain pending, not discharged by this plan.

These are implementation decisions within the brief, not unresolved product
defaults requiring a decision dispatch.

## Implementation slices

### S1 — Commit the handoff regression before its fix

Add to `tests/Antiphon.Tests/Application/SessionRuntimeActionDeferralTests.cs`:
`C1308_new_wakeup_rechecks_review_handoff_before_due`, with three reported
arguments: `boundary-delivery`, `status-delivery`, `boundary-finished`.

Use the real review producer pattern from
`SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once`
(`ReviewThreadService.DispatchAsync` creates the typed queue/action identity).
Place that durable head in the real Deferred state with a future capped retry.
Admit the newer boundary/status through the runtime/coordinator entry point.
The first drain, before the original due time, must deliver the original pending
review row exactly once, or publish the later boundary's single finished event
when no eligible input remains. A typed handoff/status itself publishes none.
Assert original handoff identity and queue association while retained, complete
UserPrompt confirmation on delivery, then acknowledgement with no duplicate send.
Do not wait thirty seconds or invoke `FlushSessionAsync` to bypass the coordinator.

Commit/push these failing assertions first. The current head due gate is the
ordinary red witness, not a fixture, build or timeout failure.

### S2 — Implement bounded early execution and its negative guards

Edit `server/Application/Services/SessionRuntimeActionCoordinator.cs` at
`DrainCoreAsync`/`ExecuteAsync` only as needed for D-2/D-3. No schema change.
Update the deferred-effects paragraph in `docs/session-runtime-invariants.md`
to distinguish executing a typed handoff early from coalescing it.

In the same test file add
`C1308_still_blocked_handoff_recheck_yields_and_preserves_debt`. Prove one
early queue attempt per drain, retained identity/debt, zero submitted bodies and
zero charged delivery attempts while the gate remains closed; show a no-newer
hint before `DueAt` does not retry. Check generation/order guards through the
normal path. Assert the persisted action attempt count increases by exactly
one and the page returns without requesting a continuation; removing the
new deferral yield must fail those assertions, not merely time out.

Commit/push; batch S3 before CP-2 through CP-5 so the exact handoff methods and
existing guards share one green build. No production queue-gate rewrite is
anticipated.

### S3 — Complete only the missing temporary-block delivery tests

Add `tests/Antiphon.Tests/Application/SessionRuntimeActionTemporaryBlockTests.cs`,
using `BridgeQueueHarness`, an isolated PostgreSQL schema and the existing
Integration/Slow categories and `ParallelLimiter<ProcessSpawnLimit>`.
Planned method:
`C1308_temporary_block_retains_action_then_delivers`, four reported arguments:

| Argument | Set and clear the actual boundary |
|---|---|
| `transport-unavailable` | Test `ISessionRunnerDirectory.Resolve` raises `PhoneHomeProblemTypes.Unavailable` while inventory stays live/unknown, then resolves. Reuse the directory pattern in `SessionMessageQueueDeliveredSpillRecoveryTests`; do not fake a dead session. |
| `herdr-pending` | Set/clear `HerdrPendingReasons.Unreachable` via the runtime test metadata seam. |
| `herdr-send-unreachable` | Adapter raises `HerdrProblemTypes.Unreachable` before accepting input; clear it. Assert the temporary `BackendUnreachable` path refunds the attempt and clears attempt state. |
| `modal` | Use `RemoteControlRecoveryHarness` with an isolated schema to detect the real menu episode, then resolve it through recovery. Clearing a boolean or inserting a receipt must not stand in for modal resolution. |

For each, begin with a Pending eligible row and a routed runtime action.
While blocked assert zero accepted input, no delivery verdict, zero charged
attempts, no kill, retained Deferred action and no finished event. Clear only
the barrier, make the retained retry due, and drain through the coordinator
without creating a replacement action. Emit the complete matching UserPrompt
through runtime ingestion/event-pump machinery after submission. Assert
`Delivered`, one attempt, one body/Enter submission, original row identity,
and action acknowledgement. A later drain must not send again.

Use existing harness clocks with running timers or controlled advancement;
never freeze the whole queue graph's timers. No new provider process or live
runner/broker is required. Keep helper additions local unless an existing
harness needs a small, optional seam.

## Test-design handoff

Bind the above methods, exact argument counts, barriers and one red witness plus
one narrow green per behavior. A-1 uses ordinary pre-fix red then post-fix green.
For already-correct barriers, bind separate method-scoped Mutation controls
that remove only that barrier's temporary-debt retention or refund: the blocked
state/acknowledgement assertion must fail, then the exact method must pass after
restoration. Include the existing Herdr-blocked/rules-held methods in that
mapping without duplicating them. Select one fault per behavior, not a broad
matrix of pump variants. Compile/fixture failures and zero tests are not red.
Keep all historical outcomes and pending controls explicit.

### Platform and checkpoints

Read `GET /api/runner-defaults` and `GET /api/session-runners` at approximately
17:41 UTC on 2026-10-20: defaults revision 2 had a global preference and no
kind overrides; the catalogue offered eligible Linux and Windows runners.
These are observations, not host pins. Re-read at dispatch. Omit `-Runner` and
`-Platform`; all rows use the **Integration/Slow lane, Linux-capable**, with
isolated PostgreSQL and scripted input adapters. There is no native Windows need.

The proposed ordinary list below is for TestDesign to bind before Code. It is
not a completed verification design or authority to skip that stage. Estimated
total is 12 minutes, including two fresh builds; build-slot waits are additional.
No whole Unit, namespace, assembly or unrelated CARD-1259 checkpoint selection.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1308-red/ | integration-handoff-red | `/*/Antiphon.Tests.Application/SessionRuntimeActionDeferralTests/C1308_new_wakeup_rechecks_review_handoff_before_due` | A-1 witness | 3 executed, expected early-progress assertion failures | 3 | 4 |
| CP-2 | S2-S3 | tests/Antiphon.Tests -> bin-c1308-green/ | integration-handoff-green | `/*/Antiphon.Tests.Application/SessionRuntimeActionDeferralTests/(C1308_new_wakeup_rechecks_review_handoff_before_due)\|(C1308_still_blocked_handoff_recheck_yields_and_preserves_debt)` | A-1, bounded recheck | 4 passed, 0 failed/skipped | 4 | 4 |
| CP-3 | S2-S3 | CP-2 | integration-temporary-green | `/*/Antiphon.Tests.Application/SessionRuntimeActionTemporaryBlockTests/C1308_temporary_block_retains_action_then_delivers` | A-3 missing paths | 4 passed, 0 failed/skipped | 4 | 2 |
| CP-4 | S2-S3 | CP-2 | integration-existing-delivery | `/*/Antiphon.Tests.Application/SessionRuntimeActionRoutingTests/C1259_new_working_or_blocked_state_defers_old_idle_action` | existing Herdr/rules delivery and current-state guards | 6 passed, 0 failed/skipped | 6 | 1 |
| CP-5 | S2-S3 | CP-2 | integration-coalescing-backoff | `/*/Antiphon.Tests.Application/SessionRuntimeActionDeferralTests/(C1259_new_requests_replace_backed_off_queue_work)\|(C1259_transient_deferrals_back_off_without_charging_input)` | A-2, existing replacement/backoff | 3 passed, 0 failed/skipped | 3 | 1 |

Batch S2/S3 edits and commits before the single green build. Run through the
checkpoint tool per `docs/testing-and-build.md`, once per committed source group,
and wait for every executor to finish. Every build/test driver takes the host
build-slot gate. Record exact source SHA, counts and unedited CHECKPOINT lines;
keep receipts ignored and remove owned alternate build outputs after completion.
TestDesign must add its `## Verification design` and bind the positive controls
before handing this to Code.

### Waiting-session release checklist

This changes a queue action's wait, not session ownership. Transport recovery,
Herdr becoming available/unblocked, rules readiness or resolution of the modal
allows normal input delivery after all other gates pass. A person must still
answer any approval UI that requires their input; no timeout supplies an answer.
Deferred actions release workers between attempts; they do not release the
session's seat. With `BlockedTaskParking:Enabled=false`, there is no automatic
seat-release deadline (CARD-1083). No park/stop/kill policy is added here.
