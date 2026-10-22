# CARD-1519 / CARD-1457: admit queued work independently of maintenance

Date: 2026-10-22. Plan owner: `7a85bd21`. Source inspected:
`182eac62b24c1c55b76045f167edd421695275c4` (also fetched `origin/master`
at the start of this task). This is a design, not an implemented or tested fix.

Give ordinary queued admission its own serial polling loop, independent of the
maintenance sweep loop. Reconcile sibling inspections with the complete queued
snapshot before selecting a partial ready batch. Preserve claim-time authority,
fresh occupancy reads, repository leases and owned service scopes.

## Evidence and ground truth

Read both cards in full using `scripts/card.ps1 get CARD-1519 -Board Antiphon`
and `get CARD-1457 -Board Antiphon`. Read the complete Debug report at
`docs/investigations/2026-10-22-dispatch-queue-stuck-debug.md`, initially from
`origin/feat/card-task-679d6ab5` commit
`9bd3b62ed932184fcbc4792419bd5c2b3d10ffa6`. It was absent from the initial
master fetch, then arrived on master during this Plan as
`55b4ddcef63736c4fa3a412a2c1eabf75eb94133`. Verified both report blobs equal
`b9327622a8220c08f75202d2fb3e9b1178b1ff11`; the dispatcher, hosted service
and inspection-results source files have no diff between this task's base and
that fetched master. There is no remaining Debug-report publication gap.

### Measured maintenance evidence

Followed `docs/logs.md`: the read-only `scripts/logs.ps1 -Source desktop-server
-Tail 4000` succeeded, and the incident was read directly from the same source,
`C:\src\Antiphon\server\logs\antiphon-20261022_002.log`. Raw log payloads are
not committed. The following are UTC conversions of the file's `+01:00` times.

| Source line / UTC | Named operation | What the log proves about duration |
|---|---|---|
| 96723 / 19:52:19.090 | `compaction continuation`, `CheckCompactionContinuationService.SweepAsync` | Finished **over 60 seconds**: the message says it overran its 60-second budget. No paired start is present at the retained log level, so an exact elapsed duration is unavailable. |
| 96830 / 19:53:49.244 | `delivery watchdog`, `AgentTaskDispatcher.FailNeverStartedAsync` | The wrapper reached its **60 + 30 = 90-second** budget/grace threshold and reported abandonment on its owned scope. This is the observed timeout threshold, not a precise stopwatch measurement of the underlying operation. |
| 96844 / 19:53:48.008 | Same delivery watchdog | The operation also logged cancellation at its 60-second budget. File order differs from timestamp order; do not add this as a second 90-second delay. |
| 96990 / 19:54:39.401 | Hosted tick, degraded | **140.311 seconds** elapsed from the compaction overrun line to this tick-completion line; **50.157 seconds** from the abandonment line. These are measured timestamp differences, not a claim that every second was maintenance. |
| 85024 / 19:06:39.624; 85098 / 19:06:46.467 | `settlement sync debt`, `RecoverSettlementSyncAsync` | A separate tick reached the **60-second cancellation threshold**; its degraded completion followed **6.843 seconds** later. |

Reproduce the incident selection with:

```powershell
rg -n "2026-10-22 20:(5[1-4]|06):.*(Delegation sweep|Delegation tick ran)" 'C:\src\Antiphon\server\logs\antiphon-20261022_002.log'
```

The Debug report's occupancy samples fell from 10 at 19:57:57 to 8 at
19:58:58 and 7 at 19:59:57; the first subsequent checkout was logged at
20:00:10 and launch at 20:01:54. Five of seven tasks eventually launched.
These are attributed Debug observations, not a new occupancy audit by this
Plan. The operator's roughly 50-minute queue cost includes genuine capacity,
inspection, lease, checkout and mirror delays; it is not a measured single
maintenance stall. Neither card requires a permanently wedged dispatcher.

| Card assumption / desired property | Current code at the inspected source | Consequence for this change |
|---|---|---|
| Five-second polling rechecks a freed seat promptly. | `AgentTaskDispatcherHostedService.ExecuteAsync` waits for timer/lease readiness, then awaits `DispatchReadyWaitersAsync` and the entire `TickAsync`. Its next timer is not armed until that work finishes. `DelegationSettings.PollIntervalSeconds` defaults to 5. | A timer interval is not a bound while a full tick awaits maintenance. Separate ownership of the two loops. |
| Slow serial maintenance precedes ordinary admission. | `TickCoreAsync(null)` runs 17 named `RunSweepAsync` calls with compaction present, before loading host budgets and queued work. `RunSweepAsync` has 60-second budget and 30-second grace defaults. | Extract this existing block as maintenance; keep its order and scope rules. The hosted log's hard-coded “9 sweeps” is already inaccurate. |
| A capacity-held task benefits from between-sweep wakes. | `RunSweepAsync` first calls `DispatchReadyWaitersAsync`; `RepositoryLeaseWaiters.TryTakeReady` selects at most eight compatible dispatch waiters. Only repository lease release and land admission call `SignalReady`. End-of-pass reconciliation removes a waiter held on runner capacity. | Lease wakes are not a runner-capacity backstop. Poll all queued work independently; do not pretend capacity holds are repository waiters. |
| “runner at capacity 10/10” is a permanently cached capacity verdict. | `RemoteHoldForAsync` calls `CountRunnerOccupancyAsync`: live DB sessions plus prepared queued launches plus in-flight preparations, with the candidate excluded where appropriate. | Reuse this fresh read; do not clear a hold by trusting a wake, the UI, or physical slot count alone. |
| Ready passes only affect their selected tasks' inspection state. | `TickCoreAsync` loads all queued rows, then narrows to `ready`/`Take(8)`, then calls `_siblingInspections.Reconcile` on that narrowed list at line 640. | Move reconciliation immediately after the full queued read and before narrowing or the empty-queue return. No extra DB query is needed. |
| A missing inspection result is harmless cache churn. | `SiblingInspectionResults.Reconcile` cancels omitted in-flight entries and removes omitted completed entries; the queue also drops `Attempted` markers. | Preserve unrelated queued results, running workers and attempted markers across partial passes. |
| Retention grants authority to launch. | `Peek` checks intent and ten-minute age; dispatch revalidates task/attempt, sibling metadata and frozen refs under the repository lease. | Keep all invalidation, expiry, bounded store sizes and claim checks. |
| Another asynchronous tick can safely use the existing scope. | Dispatcher carries an EF `AppDbContext`, `_hostLimits` and other per-pass fields. `RunSweepAsync` only abandons work on an independently owned scope. | Never overlap calls on one dispatcher/context; maintenance does not invoke admission. |

## Decisions

- **D-1 — Two owned loops in the existing hosted service.** Extract
  `RunMaintenanceAsync(ct)` and `DispatchQueuedAsync(ct)` from the existing
  dispatcher; the latter calls the admission core with no ready restriction.
  `DispatchReadyWaitersAsync` calls that same admission core with its selected
  IDs. `AgentTaskDispatcherHostedService` owns one admission loop and one
  maintenance loop, each serial and each opening its own async scope per pass.
  Await both lifetimes. Remove admission calls from `RunSweepAsync`; all
  production admission belongs to the admission loop. A full admission pass
  may retain the existing awaited ready checks between candidates on fresh
  scopes; these are nested serial work, never a competing worker. Keep
  `TickAsync` as a sequential maintenance-then-admission compatibility wrapper
  for existing direct harness callers; production hosting does not call it.
  Rejected: merely moving admission before sweeps, which still delays the next
  check when a seat frees during a sweep; checking between sweeps, which still
  admits a 90-second delay; overlapping full ticks, which races EF state and
  maintenance; reducing sweep budgets, which changes recovery policy.
- **D-2 — Existing poll interval is the admission scheduling bound.** Use an
  injected `TimeProvider` for hosted timers, defaulting to the existing system
  provider. Maintain a full-admission due time independent of lease signals.
  When full admission is due it takes precedence over a ready-only batch;
  frequent lease signals cannot postpone it. A lease wake may accelerate a
  ready pass, never reset the full-pass deadline. If an admission pass is
  still executing, retain/coalesce the wake and service it after that pass;
  do not accumulate concurrent passes. With no admission already executing,
  the next full pass starts within `max(1, PollIntervalSeconds)` (default five
  seconds) plus scheduler jitter, irrespective of maintenance duration. If
  admission is already executing, the bound includes its remaining duration.
  Fresh reads, Git/lease/inspection/preparation and launch execution remain
  additional time. The acceptance witness prepares those prerequisites so it
  proves a freed seat is actually admitted while maintenance is still held.
  Rejected: adding runner-release events across every terminal writer or a
  separate capacity waiter registry; the independent poll covers missed
  signals and costs fewer changes. This is not an unconditional five-second
  end-to-end launch SLA.
- **D-3 — Reconcile from the full snapshot on every admission pass.** Call
  `_siblingInspections.Reconcile(allQueuedIds)` immediately after the existing
  all-queued query, before `ready` filtering and before returning for zero
  selected rows. Remove the later subset reconciliation. Rejected: changing
  the inspection store's global reconciliation contract; it is correct for a
  complete set. Full-ticks-only reconciliation also works, but wastes the
  complete set already read by partial passes and delays cleanup unnecessarily.
  Keep lease waiters' existing `considered=ready` contract separate.
  Accept CARD-1457's existing brief extension of a prerequisite-pending
  waiter's lifetime until its inspection finishes; tightening it is outside
  these two fixes.
- **D-4 — Preserve all admission and maintenance authority.** Reuse current
  capacity, retained-capacity-return priority, concurrency, platform, provider,
  scope, drain, inspection and transactional claim/lease gates. A wake only
  requests a fresh attempt. Maintenance can update durable state concurrently
  on its own context, as abandoned owned sweeps already can; no new status or
  lock bypass is permitted. The existing per-sweep in-flight guard prevents a
  second instance of a still-running sweep. Keep sweep budgets unchanged.
- **D-5 — Own cancellation through disposal.** On stop, cancel both loops,
  observe/join their tasks, then wait for any registered abandoned sweep's
  owned completion/disposal before declaring the execution lifetime finished.
  Add a small all-released wait to `SweepInFlightState` if needed; collect it
  after the maintenance loop can no longer start a sweep. An ignored token
  does not authorize disposing that sweep's context or restarting it. Do not
  introduce a new forced-stop policy. An unreleased external operation can
  still outlive the host's shutdown deadline; report that as incomplete
  shutdown, never a completed join. Catch/log pass failures independently so
  neither loop silently terminates its peer; cancellation is not a failure.
- **D-6 — Keep diagnostics useful and bounded.** Distinguish maintenance from
  admission in the existing runtime phase/log records, report maintenance
  failures without the false fixed sweep count, and retain admission counters.
  Do not add metrics storage, an API, UI or a general scheduler framework.
- **D-7 — Portable verification lane.** Read `GET /api/runner-defaults` and
  `GET /api/session-runners` on 2026-10-22 at approximately 20:26:56 UTC.
  Defaults revision was 2; eligible Windows and Linux runners were present,
  alongside an unavailable draining row. These are observations, not a
  placement contract. All checkpoint rows use the **Integration** lane;
  inspection rows additionally retain their **Slow** category and
  `ProcessSpawnLimit`. No Windows-only behavior is required. Resolve current
  defaults at dispatch, omit `-Runner` and `-Platform`; do not pin a fleet host.
- **D-8 — Separate TestDesign handoff.** The brief requests witnesses and a
  closed checkpoint table but does not explicitly fold the TestDesign stage
  into this dispatch. The design below is concrete input for that stage;
  settle `next: test-design`. No product decision or permission is pending.

This plan does not add or change a session waiting for input. Existing Blocked
seat retention remains governed by CARD-1083: with parking disabled there is
no automatic release deadline. A capacity poll does not release any seat.

## Implementation slices

Commit and push each slice on the assigned Code branch, with each red witness
committed and executed before its fix. No implementation is part of this Plan.

| Slice | Files | Deliverable / test |
|---|---|---|
| S1 | New `tests/Antiphon.Tests/Application/DispatcherAdmissionSchedulingTests.cs`; new `tests/Antiphon.Tests/TestHelpers/DispatcherAdmissionFixture.cs`; `server/Infrastructure/Orchestration/AgentTaskDispatcherHostedService.cs` only for the behavior-preserving injected clock seam | V-1 red on the existing scheduling order. Use real hosted service and dispatcher, isolated DB, fake runner and recording launch sink. Clock injection must retain the old serial order at this commit. CP-1. |
| S2 | `server/Application/Services/AgentTaskDispatcher.cs`; `server/Infrastructure/Orchestration/AgentTaskDispatcherHostedService.cs`; `server/Application/Services/SweepInFlightState.cs`; S1 test/fixture files; `docs/orchestration-loop.md` dispatch scheduling paragraph | Extract maintenance/admission, implement two owned loops, remove between-sweep admission, preserve one admission owner, drain owned work, correct phase/failure wording. Add V-2/V-3 controls. CP-2. No policy/settings change or new registration is expected. |
| S3 | New partial `tests/Antiphon.Tests/Application/DispatcherSiblingInspectionSchedulingTests.PartialPass.cs`; `tests/Antiphon.Tests/TestHelpers/SiblingInspectionFixture.cs` only if a reusable ready-pass helper is needed | V-4 two-argument red witness with ten queued tasks; invokes the production `SignalReady` -> `DispatchReadyWaitersAsync` route. CP-3. |
| S4 | `server/Application/Services/AgentTaskDispatcher.cs`; S3 test file | Move full-snapshot reconciliation ahead of partial selection/empty return. Finish V-4 cleanup checks; retain R-1/R-2 without rewriting existing tests. CP-4. |

Keep `CardSiblingInspectionQueue.cs`, `SiblingInspectionResults.cs`,
`RepositoryLeaseWaiters.cs`, the capacity formula and claim implementation
unchanged unless a demonstrated failure requires an explicit plan amendment.
Check the extracted maintenance result still carries routing-resume counts
and sweep failures; the compatibility wrapper must compose those counts.

## Verification design

This is the proposed closed Code selection for TestDesign to validate. All
new tests assert durable task/session/launch outcomes or owned lifecycle
outcomes, not source text or helper-to-itself comparisons. No whole-suite,
native runner, provider login, live session release or production restart is
part of this scope. One initial red and one narrow green run per behavior.

### Admission witness and controls

Use a fresh `IsolatedTestSchema`, the existing `PhoneHomeTestHost`/recording
`IAgentTaskLaunchSink` patterns from `PhoneHomeTaskDispatchProjectionTests`,
and the production `AgentTaskDispatcherHostedService`. Use a prepared remote
candidate with no checkout/mirror prerequisite, a runner capacity of one, and
one counted occupying session. No real provider process is needed. The runner
ID comes from the fake host. The fixture uses `AddDelegationWorktreeGraph` and
never launches real `Program` against the production runner.

Configure the scoped real dispatcher using its existing `CatchUpOverride`:
for one seeded overdue Dispatched watchdog sentinel only, signal entry and
wait on a test-owned barrier. This holds `FailNeverStartedAsync` through its
real sweep path, rather than substituting the scheduler or its result. Keep
the sentinel on a separate fixture runner so it does not consume the
candidate's seat. Maintain its required session/brief evidence. Scope identity
is observable through the existing internal `Db` property. If this fixture
cannot reach the watchdog hook, fix the fixture; a never-entered barrier is
not evidence of the scheduling defect.

- **V-1**, `DispatcherAdmissionSchedulingTests.C1519_freed_seat_is_admitted_while_maintenance_waits`:
  first let the hosted service persist the candidate's capacity Held result.
  Confirm no lease waiter is registered. Arm the sentinel and await sweep
  entry; then transition the occupying session to Stopped in a separate DB
  scope, exactly the durable fact read by production capacity admission.
  Keep the sweep barrier closed. Advance the injected clock through one
  five-second full-admission interval and observe the real admission outcome
  within a bounded fixture window. At the end of that window, query and
  assert candidate Dispatched, exactly one
  candidate session and launch-sink entry, and sweep still blocked. Use clock
  timer-registration/phase barriers before advancing; do not rely on sleeps
  to arm the timer. At the S1 source, the next poll cannot execute while the
  sweep is held: the task stays Queued / launch count remains zero, causing
  the explicit outcome assertion to fail. The observation timeout must not
  throw first and replace that assertion with a generic timeout. Always release and join the held
  sweep in `finally`, including on the intended red.
- **V-2**, `DispatcherAdmissionSchedulingTests.C1519_full_capacity_and_repeated_wakes_preserve_single_admission`:
  hold maintenance and keep occupancy full; advance two poll intervals and
  send repeated real `RepositoryLeaseWaiters.SignalReady(force: true)` wakes.
  Assert candidate remains Queued, zero candidate sessions/launches and a
  runner-capacity hold. Free the seat while repeating signals across the next
  full deadline; await one launch, advance another interval and assert exactly
  one task transition/session/launch. Include a timer+ready simultaneous wake
  and record maximum concurrent admission invocations as one. This proves
  full-pass deadline priority and capacity rechecks; signals confer no claim.
- **V-3**, `DispatcherAdmissionSchedulingTests.C1519_shutdown_joins_loops_and_owned_sweep_scopes`:
  while a sweep is held, advance beyond budget+grace so it remains registered
  on its owned scope. Cancel hosting and assert the execution lifetime has
  not reported a completed join while that scope still runs. Release the
  barrier; await service execution and sweep release, assert scope disposal,
  no later polls/launches, and maintenance/admission `Db` identities differ.
  Include the cooperative cancellation path in this method. Do not equate
  an expired external StopAsync timeout with successful execution completion.

Use `TimeProvider` time for scheduling; wall-clock watchdogs only prevent a
broken test from hanging. The witness must assert the admission result before
opening maintenance, not merely assert a timer fired or that admission happens
after cleanup. S1 is allowed the minimal system-clock-equivalent timer seam,
but cannot include the scheduling fix. Build/fixture errors and zero selected
tests are not the required red.

### Partial inspection witness and narrow regressions

- **V-4**, `DispatcherSiblingInspectionSchedulingTests.C1457_partial_ready_pass_preserves_unrelated_inspections`,
  two `[Arguments]`: `completed`, `inflight`. Extend `SiblingInspectionFixture`
  (real scratch Git, isolated schema, fake clock and actual queue workers).
  Seed ten queued Worktree tasks. Register eight compatible dispatch lease
  waiters selected deterministically by their `Since` values. An unrelated
  ninth task has either a completed retained guard or an inspection held in
  `InspectionGit`'s cancellable barrier; the tenth task ensures the fixture
  cannot accidentally cover the whole queue. Keep ready tasks held by a
  refusing repository lease to avoid unrelated launches. Signal readiness
  and invoke real `DispatchReadyWaitersAsync`, without an intervening full
  tick. Assert the ninth task's completed guard is still reusable or its
  worker token is not canceled, its attempted marker survives, and no
  additional worker inspection was requested. Current subset reconciliation
  removes/cancels it, so both cases fail at that outcome assertion.
  Then make the ninth task terminal, signal a ready pass again and assert
  real cleanup; also remove all queued tasks and exercise a full admission
  pass to assert empty-snapshot cleanup. Release/join every worker in `finally`.
  Observe worker invocation/cancellation directly; do not rely only on
  aggregate Outstanding, which deliberately includes canceled work until join.
- **R-1**, existing
  `DispatcherSiblingInspectionSchedulingTests.C1428_lease_refusal_reuses_the_inspection_on_the_next_attempt`:
  one observation survives lease refusal and produces a single cut/dispatch.
- **R-2**, existing
  `DispatcherSiblingInspectionSchedulingTests.C1428_stale_observation_at_claim_reinspects_and_keeps_the_waiter`:
  a moved ref cannot launch from a retained stale result; reinspection and
  waiter age survive. Together these protect retention without weakening
  claim-time authority.

### Checkpoint execution and cost

Run through the checkpoint tool, one committed slice group at a time. Its
build/test drivers take the host build-slot gate. Use the checkpoint driver
built from the Code checkout; if it needs bootstrapping, build that tool alone
under `scripts/build-slot.ps1` with an alternate `bin-c1519-driver/` output,
record this tooling-only bootstrap separately, and release that slot before
starting the driver. Do not hold an outer build slot while the checkpoint
executor waits for its own row slots. With `$checkpointDriver` set to that
build's emitted `Antiphon.Checkpoints.dll`, run (replace S1 with S2/S3/S4):

```powershell
dotnet $checkpointDriver run --plan docs/superpowers/plans/2026-10-22-card-1519-admission-delay-plan.md --after S1
```

Rows acquire their own slots. If the command returns 75, keep the run ID and
call the tool's `wait` until terminal; never settle with an executor running.
Exit 4 is not run, not a reason to bypass the slot. CP-1/CP-3 intentionally
return failed-test results; require the named assertion failure, record it
unchanged, then implement the corresponding fix. They are red witnesses,
not green gates. Each later green is a fresh committed source/build identity.
No same-source repetition after green. Preserve complete CHECKPOINT lines,
counts, full SHAs and provenance in ignored evidence and the Code report.
Clean up only the run-owned alternate outputs through the documented safe
cleanup path; no generated logs/TRX/JSON are committed. Code/Review also run
`scripts/check-evidence-diff.ps1` over base..HEAD.

The **Integration** lane covers all four rows. CP-3/CP-4 also use the existing
**Slow** category and process-spawn limiter for scratch Git. Method filters
below are the full selection; category-wide execution is not authorized.
Estimated ordinary checkpoint floor is **16 minutes** (Windows **22 minutes**),
plus approximately 45-60 minutes authoring. Estimates include a separate build
at each red/fix commit; no output is reused across different source groups.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows |
|---|---|---|---|---|---|---|---:|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1519-admission-red/` | integration-admission-red | `/*/Antiphon.Tests.Application/DispatcherAdmissionSchedulingTests/C1519_freed_seat_is_admitted_while_maintenance_waits` | V-1 | 1 executed, 1 expected assertion failure, 0 skipped | 1 | 4 | 5 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1519-admission-green/` | integration-admission-green | `/*/Antiphon.Tests.Application/DispatcherAdmissionSchedulingTests/(C1519_freed_seat_is_admitted_while_maintenance_waits*)\|(C1519_full_capacity_and_repeated_wakes_preserve_single_admission*)\|(C1519_shutdown_joins_loops_and_owned_sweep_scopes*)` | V-1, V-2, V-3 | all 3 listed methods, 0 failed/skipped | 3 | 4 | 6 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1457-partial-red/` | integration-slow-partial-red | `/*/Antiphon.Tests.Application/DispatcherSiblingInspectionSchedulingTests/C1457_partial_ready_pass_preserves_unrelated_inspections` | V-4 | 2 executed argument cases, 2 expected assertion failures, 0 skipped | 2 | 4 | 5 |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1457-partial-green/` | integration-slow-partial-green | `/*/Antiphon.Tests.Application/DispatcherSiblingInspectionSchedulingTests/(C1457_partial_ready_pass_preserves_unrelated_inspections*)\|(C1428_lease_refusal_reuses_the_inspection_on_the_next_attempt*)\|(C1428_stale_observation_at_claim_reinspects_and_keeps_the_waiter*)` | V-4, R-1, R-2 | all 4 executions (2 argument cases + 2 methods), 0 failed/skipped | 4 | 4 | 6 |

## Delivery and limits

Plan verification is source/log inspection and Markdown/diff checks only; no
test or build has run in this task. TestDesign should validate fixture reach,
timer arming and manifest import before handing this closed selection to Code.
It must not replace the hosted production-path witness with a pure scheduler
test. If a red cannot reach the stated assertion, revise the fixture/plan
before claiming a fix.

After Code's green rows, separate ordinary Review checks the extracted sweep
roster, single admission owner, claim gates and scope shutdown. The plan's own
documentation publication does not deploy the server. The caller/owner lands
through the normal task landing operation; no direct shared-target push.
Product activation follows the release owner, then read the actual server
version. A live observation may correlate a capacity drop with a new admission
attempt while maintenance runs, but must separate scheduling delay from other
holds and must not force-release a session to manufacture evidence.

--- next stage ---
next: test-design
handoff: Validate CARD-1519/CARD-1457's hosted-path red witnesses, scope/shutdown ownership and four-row closed manifest, preserving the five-second scheduling-bound limits and full-snapshot reconciliation. Publish this plan through its owner; then hand the verified design to Code.
artifact: docs/superpowers/plans/2026-10-22-card-1519-admission-delay-plan.md
