# CARD-1519 / CARD-1457 Code evidence

Code / landing owner: `5e66dc2e-37a9-438e-a07f-4a8a5b117f3e`.
Branch: `feat/card-task-5e66dc2e`.
Worktree: `/work/worktrees/task-5e66dc2e` (runner mirror; desktop checkout not accessed).
Task base: `8f595e260649b00adc600e6531629ae7abe3adbb`.
Plan: `docs/superpowers/plans/2026-10-22-card-1519-admission-delay-plan.md`.

Implementation separates serial admission and maintenance, preserves the compatibility
TickAsync result composition and all 17 maintenance sweep calls, and joins registered
owned sweep scopes after both hosted loops stop. Full admission has a monotonic deadline
which lease wakes cannot reset. There is no nested admission in maintenance or in a full
pass. Partial admission reconciles inspections immediately after the complete queued read,
before narrowing or returning empty. Capacity, claim/lease, inspection expiry and frozen-ref
checks are unchanged. The inspection store, queue and lease waiter implementation are unchanged.

The explicit brief overrides the generic Final profile: only the four closed checkpoint
rows are selected. Unit, whole affected classes, whole-suite execution and live operational
acceptance are deferred, not passed. No product restart or session release was performed.
Activation requires the server/AppHost on the release branch; runner restart is unnecessary.

## Ordinary verification and historical outcomes

- V-1: CP-1 at `28b01587944fd0c7d70c8f9882453830043736f8` reached the real delivery
  watchdog barrier, freed durable occupancy, and failed on Queued versus Dispatched while
  maintenance remained held. One execution, one expected assertion failure, zero skips.
- V-2: CP-2 at `60b4ea7bcf9acd550efee668d12dcbbb51e3dc9b` passed capacity holds,
  repeated signals, a due full pass ahead of a pending signal, and one admission owner/launch.
- V-3: the same CP-2 initially failed its cancellation-observation timeout. The fixture now
  records cancellation in the cooperative operation's catch as well as its callback; disposing
  the older callback can race the WaitAsync continuation. It also gives an erroneously completed
  execution lifetime the existing five-second observation window before asserting it stays owned.
- V-1 in that CP-2 initially observed the committed task before the launch sink. The fixture now
  waits for both inside the existing five-second window, retaining every durable outcome assertion.
- V-4: CP-3 at the same source executed both `completed` and `inflight` cases. Both failed at
  the intended assertion: the unrelated guard was null / worker cancellation was true.
  Each used ten queued tasks, eight real compatible lease waiters and actual inspection workers.
- V-4 cleanup and R-1/R-2 are pending the final CP-4 execution at this document's commit.
- Final CP-2 and CP-4 are pending when this document is committed. Their exact SHA, fresh TRX
  roster/counts, unedited lines and receipt-validation results will be recorded in
  `/work/worktrees/task-5e66dc2e/.antiphon/task-5e66dc2e.md` and the final caller report.
  This document does not predeclare those results green.

Initial CP-1 attempts are retained: `0673692b40deadfa960941e23c223f09bd050986` failed
compilation because the new fixture used CreatedAt instead of event At; zero tests ran.
`cb74d7186b1f4c58f2d7939908d88ed2e53c1bec` built and executed one test but failed seeding
an incomplete runner binding (missing RunnerCwd). Neither is a defect witness.

No timeout or assertion was loosened. Admission tests are classified Slow and registered
because the first hosted controls measured 17–22 seconds. The final rerun is failure-driven,
also covers the changed admission core, and supplies a wholly green receipt after the mixed
expected-red group. It repeats only CP-2 and CP-4's exact selections, with one isolated build
per row; no new broad selection is introduced.

## Provenance, cost and pending work

Runner defaults and session-runner catalogue were read live at approximately 20:49 UTC:
default revision 2, eligible Linux and Windows rows, one unavailable draining row. No runner
location is embedded in implementation or tests. Checkpoints run on the assigned Linux mirror.

The tooling-only bootstrap was authorized by the plan: build-slot.ps1 wrapped dotnet build
of tools/Antiphon.Checkpoints with OutputPath=bin-c1519-driver/. It succeeded in 5 seconds,
slot=granted, waited=0s (four existing warnings). All subsequent build/test drivers are from
the checkpoint tool. Historical build and test slots were granted with waited=0s.
Actual checkpoint wall costs: 2m49s (compile failure), 5m12s (seed failure), 5m50s (V-1 red),
12m13s (CP-2/CP-3 group). No repeated green proof was requested.

The red receipts have clean source and verified build provenance where builds succeeded;
validate-checkpoint-receipt.ps1 rejects their retained failed-run output as cleanup_refused.
They are red evidence, not green source certificates. Raw TRX, JSON, logs and checkpoint
outputs stay ignored. Terminal red output directories are manually removed only after
checking containment, links and tracked files. Final green receipts must validate separately.

The plan enumerates zero PC-n rows. All SourceLanding Mutation work, including missing-control
discovery, remains pending; ordinary tests discharge no positive control. No deliberate mutant
was applied. After ordinary Review the caller lands this original Code owner and commissions
SourceLanding Mutation. Restart requirement: server; no restart performed here.

Final audit must run check-evidence-diff.ps1 over the complete task base..HEAD, fetch current
master and rc/2026-10-20, check master with merge-tree, simulate this task's commits onto rc
without changing branches, verify clean status and confirm the pushed ref with ls-remote.

## Unedited historical CHECKPOINT lines

~~~text
CHECKPOINT CP-1 commit=0673692b40deadfa960941e23c223f09bd050986 build=failed filter=/*/Antiphon.Tests.Application/DispatcherAdmissionSchedulingTests/C1519_freed_seat_is_admitted_while_maintenance_waits executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=0673692b40deadfa960941e23c223f09bd050986 sourceState=clean buildSource=unknown
~~~

~~~text
CHECKPOINT CP-1 commit=cb74d7186b1f4c58f2d7939908d88ed2e53c1bec build=ok filter=/*/Antiphon.Tests.Application/DispatcherAdmissionSchedulingTests/C1519_freed_seat_is_admitted_while_maintenance_waits executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-5e66dc2e/.antiphon/checkpoints/20261022-205451-9125/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=cb74d7186b1f4c58f2d7939908d88ed2e53c1bec sourceState=clean buildSource=verified
~~~

~~~text
CHECKPOINT CP-1 commit=28b01587944fd0c7d70c8f9882453830043736f8 build=ok filter=/*/Antiphon.Tests.Application/DispatcherAdmissionSchedulingTests/C1519_freed_seat_is_admitted_while_maintenance_waits executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-5e66dc2e/.antiphon/checkpoints/20261022-210048-3c10/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=28b01587944fd0c7d70c8f9882453830043736f8 sourceState=clean buildSource=verified
~~~

~~~text
CHECKPOINT CP-2 commit=60b4ea7bcf9acd550efee668d12dcbbb51e3dc9b build=ok filter=/*/Antiphon.Tests.Application/DispatcherAdmissionSchedulingTests/(C1519_freed_seat_is_admitted_while_maintenance_waits*)|(C1519_full_capacity_and_repeated_wakes_preserve_single_admission*)|(C1519_shutdown_joins_loops_and_owned_sweep_scopes*) executed=3 passed=1 failed=2 skipped=0 trx=/work/worktrees/task-5e66dc2e/.antiphon/checkpoints/20261022-211026-5688/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=60b4ea7bcf9acd550efee668d12dcbbb51e3dc9b sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=60b4ea7bcf9acd550efee668d12dcbbb51e3dc9b build=ok filter=/*/Antiphon.Tests.Application/DispatcherSiblingInspectionSchedulingTests/C1457_partial_ready_pass_preserves_unrelated_inspections executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-5e66dc2e/.antiphon/checkpoints/20261022-211026-5688/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=60b4ea7bcf9acd550efee668d12dcbbb51e3dc9b sourceState=clean buildSource=verified
~~~

