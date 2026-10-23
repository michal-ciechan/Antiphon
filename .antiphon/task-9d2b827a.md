# CARD-1518 S2-S4 implementation and verification

Code/landing owner: `9d2b827a-8368-454d-90dc-35c019db4359` (owns the whole S1-S4 chain).
Branch: `feat/card-task-9d2b827a`.
Worktree: `/work/worktrees/task-9d2b827a`.
Assigned base: `ffc1c8877be2c306eae1bdf6531c6ca2a4c3e619` (S1 tip).
Plan: `docs/superpowers/plans/2026-10-23-card-1518-permission-wait-classification-plan.md`.
Mutation candidate: `docs/superpowers/mutations/2026-10-23-card-1518-provider-wait.md`.

## Implementation

S2 adds a DI-owned read-only observation store, real CARD-0680 contract validation,
per-session serialization/cadence, completed-read expiry and binding revalidation.
The final deadline disposition refreshes after transcript catch-up, retains all
original clocks, and names provider waits without claiming that execution started.

S3 adds the bounded pre-deadline dispatcher pass, durable versioned Warning episode
transitions, and the atomic ProviderInputWait notification (ordinal 8). It uses the
existing WhenIdle keyed queue/outbox. Complete UserPrompt, an attempt floor and the
current recorded delivery generation are required. The read-only expectation debt
projection applies the same generation requirement. It never stamps completion.

S4 adds AttentionKind.ProviderInputWait (60), Check/stall diagnosis and client
visual/rail mapping. Navigation is drawer/agent/card only. Recent failure and
existing seat surfaces retain terminal truth. Runtime, HTTP and card-lifecycle
owners document explicit recovery. No schema, detector, input authority, timeout,
retry policy or task-status change is included.

## Scope and source qualification

The specific brief/plan overrides the generic Final profile: CP-2..CP-8 only;
no Unit lane, namespace or whole assembly. One isolated Antiphon.Tests build serves
CP-2..CP-7; CP-8 is the focused portable client wrapper. Method-OR filters gained
suffix wildcards exactly as S1's documented pinned-TUnit selection repair requires.
Every new slice's initial tests were committed before its main production behavior.
Additional guard/fixture repairs are separately committed. Mutation PCs are pending.

Final qualification runs after this report is committed and pushed. The actual
source SHA, every unedited CHECKPOINT line, per-method TRX roster and receipt
validation are stored in `.antiphon/evidence/c1518-s2-s4/settlement.md` and the
explicit `.antiphon/checkpoints/<run-id>/report.{md,json}`. The final response names
the actual tested/pushed SHA. This committed narrative does not predict green.

## Execution history

Checkpoint bootstrap was an explicitly reported setup build through build-slot:
`dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1518-tool/`.
It passed, four existing warnings, zero errors, 6.32s MSBuild / 7s lease,
`slot=granted waited=0s`. A first launcher invocation with a short expected SHA
was refused before any build/test; all executed runs use a full 40-hex SHA.

Run `20261023-060053-0e1e`, source
`2816290744d02e3303487b12a4682f5cedc7dd9f`, built once (249.631s) and ran the
seven rows. Wall 7m58s. All slots granted, zero wait. CP-2/3/4 each ran four tests,
three passed and one failed; CP-5 3/3, CP-6 4/4, CP-7 3/3. CP-8 passed one client
assertion method plus typecheck/lint. Total .NET: 22 executed, 19 passed, 3 failed,
0 skipped. The report's `builds: 2` includes the unused S1 declaration; only the
green output was built. No unlisted test selection ran.

The three new-fixture failures were: V-5's chosen WorktreeLockChild could not load
the TUnit assembly module from pwsh; V-7's caller adapter lacked current dialog
observation metadata and correctly held input; V-10 deliberately deleting the
server session cascaded its own queue. Repairs retain the assertions: a locally
inherited stdin/stdout readiness/release child with 30s bound; a current Clear
caller adapter and real submitted transcript; an actually absent runner-only seat
alongside the preserved task/queue, with explicit operator endpoint verification.
These are introduced fixture defects, not claims of inherited product red.

The first run's output cleanup reported `still-present`; receipt validation
returned `cleanup_refused` (exit 2), independently of its test failures. After
executor death, the task-owned output was containment/link checked and removed.
Later runs retain outputs with the documented `--keep-outputs` option, then remove
only task-owned outputs after foreground completion; receipts remain unedited.
No production deadline or assertion was widened. No deliberate mutant was run.

## Per-ID ledger

Initial run: V-1 passed; V-2 passed; V-3 passed; V-4 passed; V-5 failed fixture;
V-6 passed; V-7 failed fixture; V-8 passed; V-9 passed; V-10 failed fixture;
V-11 passed; V-12 passed; V-13 passed; R-1/R-2/R-3/R-4 passed.
The strengthened final witnesses and repaired V-5/V-7/V-10 remain pending at this
report commit. Final actual outcomes are in settlement evidence, never inferred
from this initial ledger. No required ID is marked passed without execution.

## Activation and ownership

Restart: server. Promote/restart the server on the approved rc tip to activate the
new diagnostic pass and projections, and rebuild/serve the matching client bundle.
CARD-1518 adds no runner code; compatible active CARD-0680 runners are required.
The observed fleet did not advertise `claudeDialogGuardV1`; its old binaries yield
Unknown and keep ordinary deadlines. The release prerequisite is CARD-0680 pick
`89c1cdaa4` first, then this implementation, through the caller's release workflow.
The plan's rc observation was rc/2026-10-20 at
`5a126f6fc5ae4e16d803b628716b8f0485a37174`, lacking the observation files;
master publication alone does not activate them. A runner upgrade/restart is
needed for that dependency when the selected rc runner still lacks it. Verify
loaded server/runner SHAs, capability and applicable CARD-0680 qualification.
No desktop, release directory, supervisor 209072 or native provider was touched.
The post-activation natural/disposable read-only modal acceptance belongs to that
release operation; no live modal or approval/cancellation input was commissioned.

Every PC-1..PC-17 and each variant in the mutation candidate remains pending for
SourceLanding Mutation after independent Review and the original Code owner's land.
Ordinary green does not discharge a PC. No land/deploy is requested by this task.

## Unedited initial checkpoint lines

```text
CHECKPOINT CP-8 commit=2816290744d02e3303487b12a4682f5cedc7dd9f build=n/a filter=pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1518-client -- pwsh -NoProfile -File scripts/test-client.ps1 ProviderInputWait.test.tsx -t C1518 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=2816290744d02e3303487b12a4682f5cedc7dd9f sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=2816290744d02e3303487b12a4682f5cedc7dd9f build=reused filter=/*/*/PermissionWaitTests/(C1518_CapturedDialogNeedsDecisionBeforeStall*)|(C1518_EpisodeAndNoteCommitOnce*)|(C1518_DecisionReachesBusyAndIdleCaller*)|(C1518_NoteRecoveryRetainsOneQueueRow*) executed=4 passed=3 failed=1 skipped=0 trx=/work/worktrees/task-9d2b827a/.antiphon/checkpoints/20261023-060053-0e1e/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=2816290744d02e3303487b12a4682f5cedc7dd9f sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=2816290744d02e3303487b12a4682f5cedc7dd9f build=ok filter=/*/*/PermissionWaitTests/(C1518_OptionalObservationInputs*)|(C1518_ObservationBindingRacesAndExpiry*)|(C1518_NinetyMinuteBoundaryKeepsCustody*)|(C1518_RealChildAndOtherClocksRemainBounded*) executed=4 passed=3 failed=1 skipped=0 trx=/work/worktrees/task-9d2b827a/.antiphon/checkpoints/20261023-060053-0e1e/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=2816290744d02e3303487b12a4682f5cedc7dd9f sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=2816290744d02e3303487b12a4682f5cedc7dd9f build=reused filter=/*/*/PermissionWaitTests/(C1518_CheckAttentionAndStallAgree*)|(C1518_FailedSeatRecoveryDoesNotResurrect*)|(C1518_CadenceAndReadFailuresAreBounded*)|(C1518_DetectionHasNoTerminalSideEffects*) executed=4 passed=3 failed=1 skipped=0 trx=/work/worktrees/task-9d2b827a/.antiphon/checkpoints/20261023-060053-0e1e/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=2816290744d02e3303487b12a4682f5cedc7dd9f sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=2816290744d02e3303487b12a4682f5cedc7dd9f build=reused filter=/*/*/TaskDeadlinePolicyTests/(a_running_local_tool_takes_the_much_longer_local_execution_deadline*)|(a_warm_pool_session_is_never_charged_for_a_stall_it_inherited*)|(a_compaction_continuation_prompt_is_not_a_stalled_model_call*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-9d2b827a/.antiphon/checkpoints/20261023-060053-0e1e/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=2816290744d02e3303487b12a4682f5cedc7dd9f sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=2816290744d02e3303487b12a4682f5cedc7dd9f build=reused filter=/*/*/AgentTaskOverdueDeadlineTests/(an_overdue_pool_delegate_keeps_its_row_idle_while_the_session_lives*)|(an_unresolved_api_error_recovery_stands_the_sweep_down*)|(a_working_boot_stall_warns_without_failure_stop_or_retry*)|(the_caller_is_told_rather_than_left_to_discover_it*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-9d2b827a/.antiphon/checkpoints/20261023-060053-0e1e/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=2816290744d02e3303487b12a4682f5cedc7dd9f sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=2816290744d02e3303487b12a4682f5cedc7dd9f build=reused filter=/*/*/(ClaudeDialogDeliveryTests*)|(TaskProgressStallSweepTests*)|(RunnerSlotEndpointTests*)/(C680_QueueAndExpectationKeepDialogInputPending*)|(A_stalled_task_is_never_killed_escalated_or_failed_by_the_stall_sweep*)|(Force_release_without_the_operator_token_is_forbidden_even_from_loopback*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-9d2b827a/.antiphon/checkpoints/20261023-060053-0e1e/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=2816290744d02e3303487b12a4682f5cedc7dd9f sourceState=clean buildSource=verified
```
