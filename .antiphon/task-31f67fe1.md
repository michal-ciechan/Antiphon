# CARD-1149/1150 S2 repair 5 (F11, F12): Code report, task 31f67fe1

Outcome: F11 fixed and F12 corrected. Pre-input delivery refusals now report no input, so a later resume bookkeeping failure kills and fails the launch as it did before S2. Possible input now needs positive evidence. The new 6-case row CP-76 went red on b81e2672 (5 of 6) and under three method-scoped mutants. All 45 rows are green at 5b730d54: 867 executed, 867 passed, `CHECKPOINT SOURCE VALID`.

- Branch `feat/card-task-31f67fe1`, runner mirror worktree `/work/worktrees/task-31f67fe1` (desktop `C:\Antiphon\worktrees\card-task-31f67fe1`). Base b81e2672109497637dc594259fe17452aded2f75; pre-S2 baseline 31632adc03b78956c7dc2d056280d7309681a7ff. Not rebased.
- Original Code landing owner: ea5ef98c-75dd-4918-9513-f13a66b1680d. This repair: task 31f67fe1.
- Commits: acab4c82ec3f04163ec565533f53905294f0692f (fix and tests), 5b730d549e2f8021eb0d431d52deea83bcda7fbf (test design and report qualifications; this is the tested SHA). This report is committed after them and contains docs only.
- Plan: `docs/superpowers/plans/2026-10-07-card-1149-1150-dispatch-recovery-plan.md`. Test design: `docs/superpowers/plans/2026-10-07-card-1149-1150-test-design.md`, new section "Repair 7 evidence (S2 F11-F12)", which holds the full classification table. The test design numbers rounds differently: its "Repair 6" is repair 4 (6da8a413).

## F11 fix (fail-closed)

- `SessionMessageQueueService.cs`: a private `InputAttempt` (`MayHaveTyped`) is threaded as an optional argument through `DeliverNextLockedAsync`, `RecoverDeliveryRunLockedAsync`, `EnterOnlyConfirmLockedAsync` and `DeliverAsync`.
  - It is marked when a terminal write begins: the body write (:3666), a local command (:3564) or an Enter-only press (:3040). It is also marked when a truncated UserPrompt of the run is found during recovery (:2947).
  - It is cleared on the two proven pre-input refusals of the body write: `RunnerSpillWriteException` (:3675) and the courier's `RemoteSpillUndeliverableException` (:3691).
  - `InputMayHaveStarted(result, input)` is `Delivered`, or `Failed` with the mark (:1701).
- `SessionMessageQueueService.DispatchBrief.cs` (ensure) and `FlushSessionReportingInputAsync` (boot flush) each pass their own `InputAttempt`. Every other caller passes none and its behaviour is unchanged.
- `AgentSessionService.cs`: comment only (F12).
- Unchanged: the CARD-0340 order, the legacy `deliverIfIdle:false` branch, the catch, the boot-stall tail, the dispatcher's ensure and the F10 event-save and flush handling. There is no migration.

### Classification of `Failed` returns

Lines are `SessionMessageQueueService.cs` at acab4c82. "Pre-S2" (31632adc) and "b81": what a later resume bookkeeping failure did at each source.

| Line | Return | Class | Pre-S2 | b81 | Now |
|---|---|---|---|---|---|
| 2413 | `HoldSpillBodyForDeliveryAsync` refusal: missing spill, row canceled, 0 attempts | pre-input | kill | kept | kill |
| 2524 | exception before the body write | pre-input | kill | kept | kill |
| 2524 | exception after the body write began | may have typed | kill | kept | kept |
| 2535 | runner spill-write refusal | pre-input | kill | kept | kill |
| 2551 | courier missing spill on the first write | pre-input | kill | kept | kill |
| 2551 | missing spill on the overlay retype, after the first write | may have typed | kill | kept | kept |
| 2580 | `ForbiddenBody` | pre-input | kill | kept | kill |
| 2589 | `ModalBlocked` before a byte | pre-input | kill | kept | kill |
| 2585/2589 | `Truncated` and other verdicts after the write; a local command not accepted | may have typed | kill | kept | kept |
| 2948 | recovery finds a truncated UserPrompt of the run | may have typed | kill | kept | kept |
| 3098/3122 | Enter-only `Truncated` or failure after the Enter | may have typed | kill | kept | kept |

The pre-input rows now match pre-S2. The may-have-typed rows keep repair 4's protection, which CP-75 needs. No result is classified by its value alone, so no case is unknown: a `Failed` without the mark is pre-input. `Delivered`, `Nothing` and `LateConfirmed` map as before (true, false, false).

## F12 (docs and comments)

These now say that only the resume's own bookkeeping after the ensure or flush has returned possible input is non-destructive: the two event saves and, when the ensure typed, the follow-up flush. A throw inside a delivery after typing still kills and fails the recipient, on 31632adc and on S2 alike. Examples are the ensure's Delivered-verdict save after one complete UserPrompt and, when the flush is the first input, the flush's delivery and post-delivery `GetQueueAsync`. That inherited gap is stated as not fixed, and nothing is filed here. The updated places:

- `.antiphon/task-d30ad28b.md:246`
- `.antiphon/task-44fc0e5a.md:206`
- `.antiphon/task-6da8a413.md`, the repair 4 summary, lines 3, 13, 22, 31 and 94
- the test design's Repair 6 "Fix", step table and Known limits
- the `AgentSessionService` comment

A grep of every file changed on the branch finds no remaining unqualified "every step after input" claim.

## Tests

`tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.PreInputRefusal.cs`, `C1150_Pre_input_refusal_keeps_the_resume_failure_path`, 6 cases. Each case runs the real `AgentSessionService` and queue on an isolated schema with the fake adapter, plus one one-shot event-save fault.

- Cases: flush-missing-spill, flush-courier-missing-spill, flush-spill-write-refusal, ensure-courier-missing-spill, ensure-spill-write-refusal, and the companion may-have-typed-transport-failure.
- The pre-input cases assert:
  - the fault was reached once
  - 0 terminal inputs, 0 submitted bodies and 0 UserPrompts
  - the task stays Dispatched and there is one brief row
  - the error propagates, the adapter is killed and the session is Failed
  - flush-missing-spill only: the row is Canceled with 0 attempts
- The companion asserts no error, no kill, Running and 1 attempt.
- CP-75 is untouched at 8. CP-68's floor goes from 199 to 205. No assertion was weakened or deleted.

Red proofs (method-scoped `--treenode-filter` on the new method, `bin-c1150r6m/`, UseAppHost=false, serial). They discharge no PC.

| Proof | Red | Failing assertion |
|---|---|---|
| b81e2672 `server/Application/Services/` (= mutant M-a, the b81 expression) | 5/6: all pre-input cases | `a pre-input refusal suppressed the launch failure` |
| batch A: M-b (:3675 leaves the mark set) + M-c (:3691 leaves the mark set) | 4/6: the spill-write cases (M-b) and the courier cases (M-c), exactly as predicted | same |
| M-d (:3666 does not mark) | 1/6: the companion | `a possibly typed delivery fails the resumed launch` |

Source was restored after each proof, and git status was clean before the checkpoint run.

Pending SourceLanding Mutation (none discharged): PC-F11/flag (M-a), PC-F11/spill-write (M-b), PC-F11/courier (M-c) and PC-F11/write-mark (M-d). Missing controls with no ordinary test: the marks at :3564, :3040 and :2947, and the ForbiddenBody/ModalBlocked pre-input returns. Every PC-F10 and earlier PC is unchanged.

## Ordinary scope: 45 rows, one build

The scope is the Review's 44 rows with filters unchanged, plus CP-76, with CP-68's floor at 205. After/Build are regrouped to `S2-R7` and one build, `bin-c1150-r7/` (the tool adds UseAppHost=false). Every row was serial with `TUNIT_MAX_PARALLEL_TESTS=1` on Postgres testcontainer schemas. The selection is in the scratchpad (`selection.md`); its 45 rows' filters, Min and estimates match the branch test-design table. The whole Unit lane was not run: the brief's ordinary scope overrides the generic verification footer, and AGENTS.md forbids whole-Unit runs here.

- Run 20261008-163300-65d1, 16:33-17:17 UTC, wall 44m12s, expected SHA 5b730d54.
- 45 green, 0 red, 0 skipped; 867 executed, 867 passed. Of the 867, 855 are the Review's rows; CP-76 adds 6 and CP-68 rises 6.
- `unlisted: none`, `builds: 1`, `max-concurrent-builds: 1`.
- Build lease fdbaca48, 173 s. Every row slot was granted. All waited 0 s except CP-40, which waited 195 s.
- `validate --evidence report.json --expected-source-sha 5b730d54...` gives `CHECKPOINT SOURCE VALID source=5b730d549e2f8021eb0d431d52deea83bcda7fbf rows=45`.
- The tool deleted `bin-c1150-r7/`.

Unedited CHECKPOINT lines:

```
CHECKPOINT CP-6 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=ok filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Concurrent_producers_ensure_one_brief* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Existing_brief_and_spill_are_byte_identical* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Uncertain_evidence_cannot_create_a_replacement* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Old_attempt_evidence_does_not_suppress_current_brief* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-60 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Working_probe_failure_keeps_the_committed_brief* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-60/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-61 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Local_spill_must_be_present_and_intact* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-61/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-62 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Spill_pointer_forms_fail_closed* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-62/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-63 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Real_producers_race_to_one_brief_and_keep_the_followup* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-63/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-69 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Brief_decision_table_flips_one_condition* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-69/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-70 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Brief_evidence_whitelist_flips_one_condition* executed=73 passed=73 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-70/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-71 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Inline_brief_mentioning_spill_paths_is_not_held* executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-71/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-72 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Late_ensure_after_remote_receipt_and_payload_release* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-72/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-73 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Brief_envelope_corpus_matches_the_expected_matrix* executed=48 passed=48 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-73/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-74 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Outer_envelope_and_receipt_floor_through_the_queue* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-74/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-75 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Resumed_* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-75/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-76 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Pre_input_refusal_keeps_the_resume_failure_path* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-76/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Absent_launch_is_blocked_with_original_input* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Hold_is_once_and_automatic_relaunch_bound_is_zero* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Listed_or_unknown_runner_is_never_absence* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Changed_or_working_attempt_is_untouched* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Different_reason_or_attempted_brief_still_uses_failure_policy* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/BootStallWorkingTickCharacterizationTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-39 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Native_attempt_keeps_the_failure_path* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-39/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Failed_hold_does_not_persist_on_a_later_save* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-40/run.trx slot=granted waited=195s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-41 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Caller_note_has_one_complete_user_prompt* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-41/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-58 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/AbsentLaunchPolicyTests/* executed=77 passed=77 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-58/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-67 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_* executed=49 passed=49 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-67/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-68 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_* executed=205 passed=205 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-68/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-43 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DelegationBriefRecoveryTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-44 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/AgentTaskDeadSessionReconciliationTests/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-44/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-45 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/AgentTaskDeliveryWatchdogTests/* executed=85 passed=85 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-45/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-46 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/AgentSessionInterruptedLaunchResumeTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-46/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-47 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/SessionReconciliationServiceTests/* executed=67 passed=67 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-47/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-48 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/AgentSessionLaunchQueueOwnershipTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-48/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-49 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/AgentTaskDispatchFailureTests/* executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-49/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-50 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/AgentTaskConcurrencyLimitTests/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-50/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-51 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/AgentTaskDispatcherPredicateTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-51/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-52 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/PhoneHomeRollingRunnerTests/C1125_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-52/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-53 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/DispatcherSweepLifetimeRegistrationTests/Program_wires_both_sync_debt_sweeps_into_the_dispatcher* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-53/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-54 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-54/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-55 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/SessionTerminationSourcePersistenceTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-55/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-56 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/AgentTaskLivenessTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-56/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-64 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/RepairSourceDispatchTests/C1115_* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-64/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-65 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/BlockedTaskParkReplyAdmissionTests/C1144_* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-65/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
CHECKPOINT CP-66 commit=5b730d549e2f8021eb0d431d52deea83bcda7fbf build=reused filter=/*/*/BlockedTaskParkReplyAdmissionTests/C1146_* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-31f67fe1/.antiphon/checkpoints/20261008-163300-65d1/rows/CP-66/run.trx slot=granted waited=0s dirty=0 source=5b730d549e2f8021eb0d431d52deea83bcda7fbf sourceState=clean buildSource=verified
```

## Every build and test run

| # | Driver | Purpose | Outcome | Duration | Lease |
|---|---|---|---|---|---|
| 1 | build-slot: dotnet build tests/Antiphon.Tests -> bin-c1150r6m/ | candidate at acab4c82 content | 0 errors | 164 s held, 15 s wait | 6916b1c8 |
| 2 | build-slot: dotnet bin-c1150r6m/Antiphon.Tests.dll, CP-76 filter | candidate green | 6/6 passed | 49 s held, 60 s wait | bf4bc7fc |
| 3 | build-slot: build, server at b81e2672 | red proof build | 0 errors | 129 s | a4b89649 |
| 4 | build-slot: test, CP-76 filter | red on b81 | 5 failed / 1 passed (intended) | 51 s | 61105c4a |
| 5 | build-slot: build, mutants M-b+M-c | mutant build | 0 errors | 107 s | 955f8c5c |
| 6 | build-slot: test, CP-76 filter | mutant batch A | 4 failed / 2 passed (intended) | 52 s | da172c11 |
| 7 | build-slot: build, mutant M-d | mutant build | 0 errors | 99 s | 261f41a4 |
| 8 | build-slot: test, CP-76 filter | mutant D | 1 failed / 5 passed (intended) | 50 s | e922072e |
| 9 | build-slot: dotnet build tools/Antiphon.Checkpoints -> bin-c1150r7drv/ | checkpoint tool bootstrap | 0 warnings / 0 errors | 4 s | 983109e2 |
| 10 | checkpoint tool start/wait, 45 rows | ordinary Final scope | 45/45 green, 867/867 | 44m12s | build fdbaca48, one per row |

Runs 1-8 are the explained unlisted drivers for the red and mutant proofs. All were slot-gated, serial and method-scoped. Their `bin-c1150r6m` (28 dirs) and `bin-c1150r7drv` outputs were deleted with a root-confined loop.

## Publication, guard and activation

- `git merge-tree --write-tree origin/master 5b730d54` exits 0 against origin/master 6e7bdd59090e0cb1c847df3d19968047a99da0ac, which advanced from 2a0f8407 by CARD-1137 test/docs only. There is no overlap with this diff.
- `scripts/check-evidence-diff.ps1 -BaseRef 31632adc -HeadRef <final HEAD>`: zero violations (final figures in the closing message).
- GET /api/runner-defaults and /api/session-runners returned 200. No Runner or Platform pin was used.
- Restart after land: server/AppHost (owner: the landing orchestrator). No runner restart, no migration.
- Working sessions: nothing stops or fails a Working session beyond the pre-S2 code. The pre-input cases return to pre-S2 behaviour. The may-have-typed cases keep repair 4's non-kill. The inherited in-delivery exception gap and the CARD-1151 boot-stall tail (CP-37 3/3) are unchanged and disclosed. CARD-0079 remains the only intended automatic stop.
