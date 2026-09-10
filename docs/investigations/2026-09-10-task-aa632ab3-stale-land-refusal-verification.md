# Stale land-refusal retry verification

R-2/R-3 and PC-1 through PC-3 are complete and green after two test-fixture corrections.
The branch is ready for Review; this pass did not land or deploy it.

## Scope and commits

- Worktree: `C:\Antiphon\worktrees\card-task-51407c07`.
- Branch: `feat/card-task-51407c07`.
- Continued implementation `74ada9a3` and recovery-fixture correction `fc165e3d`.
- `7bfcab08`: corrected the changed-verification-filter preparation fixture.
- `e8b6baa4`: corrected remote-error redaction assertions.
- All six PC executions used `e8b6baa4` plus only the named temporary mutation for red.
- Prior evidence: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\task-51407c07.md`.
- Raw evidence root: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07`.

No production-code change was retained in this continuation. Changed files are
`tests/Antiphon.Tests/Application/AgentTaskLandPreparationIdentityTests.cs`,
`tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs`, and this report.

## Findings and corrections

The first R-2 run executed 16 cases: 15 passed; the `verification-filter` row of
`C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation` failed because the
operation ID was unchanged. The fixture reposted an unfinished CARD-0467 request,
which correctly preserves its original timestamp. It now calls the existing failure
handler to settle the injected failure while retaining the Verified checkpoint, then
submits the selected filter through `RequestAsync`. Assertions require a strictly newer
request marker and matching task, durable-request, and replacement-operation filters.
The full corrected class passed 16/16.

The first publication run executed 31 cases: 29 passed; the `timeout` and `canceled`
rows of `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment` threw inside Shouldly's
string assertion on a null `LastReason`. Drain-side failure settlement can retain a
null operation reason. The assertion is now null-safe and also checks the persisted
LandRefused event's redaction and operation identity. The full corrected class passed 31/31.

Cleanup safety passed 27/27 without changes. This pass has 74 final passing regression
executions and three passing restored controls, with three expected mutation failures.
Across all continuation attempts: 127 executed, 121 passed, six failed (three initial
fixture failures, three expected PC failures), zero skipped. There are no unresolved
failures. No baseline comparison was run; the report does not classify these as flakes.

Prior checkpoint failures remain historical: its initial integration run had two V-5
durable-request fixture failures, each corrected and verified by an exact-method rerun
before this continuation. Its initial compilation errors were corrected before execution.

## Complete verification mapping

Rows marked prior retain the preceding task's evidence; they were not rerun merely to
duplicate that checkpoint. Detailed TRX records and actual method/argument rows follow.

| Item | Passing executions | Source |
|---|---:|---|
| V-1 | 1 | Prior V1-V6; also PC-1 restored green |
| V-2 | 1 | Prior V1-V6; also PC-3 restored green |
| V-3 | 1 | Prior V1-V6 |
| V-4 | 4 | Prior V1-V6 |
| V-5 | 3 | Prior settled-row case plus V5a/V5b restored reruns; also PC-2 restored green |
| V-6 | 4 | Prior V1-V6 |
| V-7 | 32 new; 51 total policy cases | Prior V7 |
| R-1 recovery | 17 | Prior AgentTaskLandRecoveryTests |
| R-2 preparation identity | 16 | aa632ab3-R2-fixed |
| R-3 publication | 31 | aa632ab3-R3-publication-fixed |
| R-3 cleanup safety | 27 | aa632ab3-R3-cleanup |
| R-4 request / sweep | 15 / 7 | Prior request / sweep classes |
| R-5 concurrency / persistence | 28 / 2 | Prior concurrency / persistence classes |
| R-6 verification evidence / verifier | 9 / 10 | Prior evidence / verifier classes |
| PC-1 | 1 expected red, 1 restored pass | aa632ab3-PC-1 |
| PC-2 | 1 expected red, 1 restored pass | aa632ab3-PC-2 |
| PC-3 | 1 expected red, 1 restored pass | aa632ab3-PC-3 |

All 14 required integration rows and 32 new policy rows have passing evidence. The
combined distinct regression/policy/integration coverage is 227 executed rows with final
passing evidence; control green runs repeat three of those rows.

## Positive controls

| Control | Exact method in AgentTaskLandRefusedRetryTests | Mutation and observed red |
|---|---|---|
| PC-1 | RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation | Restored only the original final reason/source-difference disjunction in CanReplaceRefused. Failed `b.Id.ShouldNotBe(s.A.Id)` after unchanged-identity preconditions. |
| PC-2 | RR_V5_OriginalPendingRequestCannotReplaceRefusal | Removed only `explicitRequest &&` in CanReplaceRefused. Failed the single-operation assertion: two operations existed after recovery of the older pending request. |
| PC-3 | RR_V2_StillDirtyTargetCreatesFreshRefusal | Removed only `status.Output.Length == 0` from CheckTargetAsync. Failed the B phase assertion: actual Complete, expected Refused. A's failed-status setup had passed. |

Each red ran only its exact method once, exited 2, and failed at the intended behavioral
assertion. Each restored-green run executed the same method once and exited 0. Sources
were backed up immediately before each control and restored in `finally`; their SHA-256
hashes matched the fixed backups. Restored timestamps were refreshed before compilation.
All restored server DLLs have the same SHA-256:
`F70889F9B3EE3E99039BB9137C3B01221F7FB7E2C60450CC7431C7831DCEC69D`.
Each red DLL differed, and each green DLL timestamp is newer than its restored source.
The copied test-output DLL also matches that final fixed hash. Source diff against the
fixed production files is empty, and `git diff --check` passes. All test runs have exited.

Build evidence: `.antiphon/refused-retry-51407c07/aa632ab3-builds.jsonl`.
Temporary patches remain at `PC-1.patch`, `PC-2.patch`, `PC-3.patch` in the evidence root;
their presence is evidence storage, not an applied mutation. Alternate build outputs and
raw evidence are retained; the preceding task's `output-inventory.txt` remains applicable.

## Reproduction

From the worktree above, run one class per invocation:

```powershell
pwsh -NoProfile -File .antiphon/run-refused-retry.ps1 -Id R2 -Class AgentTaskLandPreparationIdentityTests
pwsh -NoProfile -File .antiphon/run-refused-retry.ps1 -Id R3-publication -Class AgentTaskLandPublicationTests
pwsh -NoProfile -File .antiphon/run-refused-retry.ps1 -Id R3-cleanup -Class AgentTaskLandCleanupSafetyTests
```

The helper calls `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-refused-retry/`
with `--treenode-filter "/*/*/<Class>/<Method>"`, fresh `--report-trx` output and a unique
results directory. It rejects empty or unexpected selection and checks exit codes and
per-result outcomes. For controls pass `-Method <exact method above> -Expect red` while
only its specified mutation is present, then restore in `finally`, refresh the source's
LastWriteTimeUtc, and invoke the same method with `-Expect green` (no `--no-build`).
The plan owns the exact patch procedure:
`docs/superpowers/plans/2026-09-09-task-cb2477f8-stale-land-refusal-retry-plan.md`.

## TRX inventory and executed rows

The following inventory is generated from the retained fresh TRXs. Every run includes
`run.log` and, where applicable, an `evidence` directory beside its TRX. Prior and
continuation runs are labelled by their IDs; initial failing runs are preserved explicitly.

### V7 (green)

Executed: 51; passed: 51; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\V7-green-deaceb061ee145a7af82e7831447d109\V7-green-deaceb061ee145a7af82e7831447d109.trx`

- Passed: `C448_V17_ResolvedInterruptedRebaseCanOpenNewOperation(False, False)`
- Passed: `C448_V17_ResolvedInterruptedRebaseCanOpenNewOperation(False, True)`
- Passed: `C448_V17_ResolvedInterruptedRebaseCanOpenNewOperation(True, False)`
- Passed: `C448_V17_ResolvedInterruptedRebaseCanOpenNewOperation(True, True)`
- Passed: `C448_V22_StageDurationsUseTheirOwnAcknowledgedIntervals`
- Passed: `C448_V31_TransitionsRequireEvidence`
- Passed: `C448_V31_VerificationEvidenceMustDescribeTheExactCommit(base-changed, False)`
- Passed: `C448_V31_VerificationEvidenceMustDescribeTheExactCommit(containment-after-rebase, False)`
- Passed: `C448_V31_VerificationEvidenceMustDescribeTheExactCommit(no-verification, False)`
- Passed: `C448_V31_VerificationEvidenceMustDescribeTheExactCommit(no-verified-time, False)`
- Passed: `C448_V31_VerificationEvidenceMustDescribeTheExactCommit(prepared-unpinned, False)`
- Passed: `C448_V31_VerificationEvidenceMustDescribeTheExactCommit(selected-filter-skipped, False)`
- Passed: `C448_V31_VerificationEvidenceMustDescribeTheExactCommit(source-unpinned, False)`
- Passed: `C448_V31_VerificationEvidenceMustDescribeTheExactCommit(target-unpinned, False)`
- Passed: `C448_V31_VerificationEvidenceMustDescribeTheExactCommit(unknown-skip, False)`
- Passed: `C448_V31_VerificationEvidenceMustDescribeTheExactCommit(valid-base-unchanged, True)`
- Passed: `C448_V31_VerificationEvidenceMustDescribeTheExactCommit(valid-exact-containment, True)`
- Passed: `C448_V31_VerificationEvidenceMustDescribeTheExactCommit(valid-verified-rebase, True)`
- Passed: `C448_V31_VerificationEvidenceMustDescribeTheExactCommit(wrong-verified-sha, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(automatic-changed, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(automatic, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(common, True)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(inspection, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(no-lease-changed, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(no-lease, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(path, True)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(phase:CleanupStarted, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(phase:Complete, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(phase:Inspected, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(phase:LocalTargetAdvanced, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(phase:Prepared, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(phase:PublicationConfirmed, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(phase:PushStarted, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(phase:RebaseStarted, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(phase:RecoveryPinned, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(phase:TargetAdvanceStarted, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(phase:Verified, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(reason:interrupted_rebase_requires_inspection, True)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(reason:null, True)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(reason:remote_read_failed, True)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(reason:target_dirty_or_unknown, True)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(reason:unknown, True)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(reason:verification_failed, True)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(receipt:AlreadyPresent, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(receipt:Landed, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(refused-status, True)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(schema, False)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(source-ref, True)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(source-sha, True)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(target-ref, True)`
- Passed: `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(wrong-task, False)`

### V1-V6 (green)

Executed: 14; passed: 12; failed: 2; exit: 2.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\V1-V6-green-4fe21e034a8043479f4f9838ec739d79\V1-V6-green-4fe21e034a8043479f4f9838ec739d79.trx`

- Passed: `RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation`
- Passed: `RR_V2_StillDirtyTargetCreatesFreshRefusal`
- Passed: `RR_V3_TargetOnlyAdvanceRequiresFreshSelectedVerification`
- Passed: `RR_V4_RemoteContainmentAfterRefusalKeepsAlreadyPresentShortcut`
- Passed: `RR_V4_RemoteReadFailureRetriesUnchangedSource`
- Passed: `RR_V4_TargetStatusFailureRetriesFromFreshEvidence(False)`
- Passed: `RR_V4_TargetStatusFailureRetriesFromFreshEvidence(True)`
- Failed: `RR_V5_EqualPendingTimestampCannotReplaceRefusal`
- Failed: `RR_V5_OriginalPendingRequestCannotReplaceRefusal`
- Passed: `RR_V5_SettledRefusalIsNotAutomaticallyQueued`
- Passed: `RR_V6_PreparationFailurePreservesActiveRefusal(destination)`
- Passed: `RR_V6_PreparationFailurePreservesActiveRefusal(target-commit)`
- Passed: `RR_V6_ReplacementSaveFailureRollsBackOldDeactivation(False)`
- Passed: `RR_V6_ReplacementSaveFailureRollsBackOldDeactivation(True)`

Failure: DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.

Failure: DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.

### V5a-restored (green)

Executed: 1; passed: 1; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\V5a-restored-green-093c015af48747cbbb1682ea737d6236\V5a-restored-green-093c015af48747cbbb1682ea737d6236.trx`

- Passed: `RR_V5_OriginalPendingRequestCannotReplaceRefusal`

### V5b-restored (green)

Executed: 1; passed: 1; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\V5b-restored-green-e1b028b05b794aa2b164761150897ce0\V5b-restored-green-e1b028b05b794aa2b164761150897ce0.trx`

- Passed: `RR_V5_EqualPendingTimestampCannotReplaceRefusal`

### AgentTaskLandRecoveryTests (green)

Executed: 17; passed: 17; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\AgentTaskLandRecoveryTests-green-41b1a2afa39748438ed1f20bc1d4b2c0\AgentTaskLandRecoveryTests-green-41b1a2afa39748438ed1f20bc1d4b2c0.trx`

- Passed: `C448_V15_RealWorkerDeathRecoversDurableBoundaries(C03)`
- Passed: `C448_V15_RealWorkerDeathRecoversDurableBoundaries(C05)`
- Passed: `C448_V15_RealWorkerDeathRecoversDurableBoundaries(C09)`
- Passed: `C448_V15_RealWorkerDeathRecoversDurableBoundaries(C12)`
- Passed: `C448_V15_RealWorkerDeathRecoversDurableBoundaries(C14)`
- Passed: `C448_V15_RealWorkerDeathRecoversDurableBoundaries(C16)`
- Passed: `C448_V15_RealWorkerDeathRecoversDurableBoundaries(C17)`
- Passed: `C448_V16_RestartReconcilesAcknowledgementGap(directory-remove)`
- Passed: `C448_V16_RestartReconcilesAcknowledgementGap(local-advance)`
- Passed: `C448_V16_RestartReconcilesAcknowledgementGap(push)`
- Passed: `C448_V17_InterruptedRebaseRequiresInspectionAndExplicitRepost`
- Passed: `C448_V17_UnsafeOrAutomaticRepostCannotDiscardInterruptedEvidence(active-sequencer)`
- Passed: `C448_V17_UnsafeOrAutomaticRepostCannotDiscardInterruptedEvidence(automatic)`
- Passed: `C448_V17_UnsafeOrAutomaticRepostCannotDiscardInterruptedEvidence(changed-symbolic-branch)`
- Passed: `C448_V17_UnsafeOrAutomaticRepostCannotDiscardInterruptedEvidence(dirty)`
- Passed: `C448_V31_FailedReplacementKeepsThePreviousOperationRetryable`
- Passed: `C448_V31_UnknownSchemaCannotResumeMutation`

### AgentTaskLandRequestTests (green)

Executed: 15; passed: 15; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\AgentTaskLandRequestTests-green-61cfa914c86142ca9ecd7c1b7e04aac0\AgentTaskLandRequestTests-green-61cfa914c86142ca9ecd7c1b7e04aac0.trx`

- Passed: `a_request_after_landed_queues_again_and_resets_the_attempt`
- Passed: `a_second_request_while_active_is_409_naming_running_and_the_requested_time`
- Passed: `C467_V01_AcceptRequeueSerializeRequest`
- Passed: `C467_V01_ConcurrentAcceptanceKeepsOneRequest`
- Passed: `C467_V01_ExplicitPostResumesResolvedConflictWithoutResettingAge`
- Passed: `C467_V01_TerminalRetryPreservesSnapshotAndCancellationDebt`
- Passed: `C467_V02_RejectStaleWorkAndExposeMirrorDrift(canceled)`
- Passed: `C467_V02_RejectStaleWorkAndExposeMirrorDrift(mirror)`
- Passed: `C467_V02_RejectStaleWorkAndExposeMirrorDrift(needs-resolution)`
- Passed: `C467_V02_RejectStaleWorkAndExposeMirrorDrift(stale)`
- Passed: `C467_V02_RejectStaleWorkAndExposeMirrorDrift(superseded)`
- Passed: `fail_async_writes_land_failed_and_clears_the_pending_row`
- Passed: `request_sets_columns_writes_the_event_and_enqueues`
- Passed: `run_async_with_a_null_column_is_a_noop`
- Passed: `try_enqueue_twice_returns_false_the_second_time`

### AgentTaskLandSweepTests (green)

Executed: 7; passed: 7; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\AgentTaskLandSweepTests-green-8884a7c21bdf4f97b2b0e73af7aa101a\AgentTaskLandSweepTests-green-8884a7c21bdf4f97b2b0e73af7aa101a.trx`

- Passed: `a_blocked_pending_row_is_skipped`
- Passed: `a_canceled_row_with_the_column_set_is_cleared_and_not_enqueued`
- Passed: `a_pending_row_that_is_already_active_is_skipped`
- Passed: `a_pending_row_that_is_not_active_is_enqueued_with_no_event`
- Passed: `an_interrupted_attempt_warns_once_nulls_started_at_and_enqueues_once_across_two_passes`
- Passed: `drain_releases_the_id_when_the_scoped_service_throws`
- Passed: `three_interrupted_attempts_refuse_and_do_not_enqueue`

### AgentTaskLandingPersistenceTests (green)

Executed: 2; passed: 2; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\AgentTaskLandingPersistenceTests-green-b643ea55b1de4515bbfeb44e176e5459\AgentTaskLandingPersistenceTests-green-b643ea55b1de4515bbfeb44e176e5459.trx`

- Passed: `C448_V31_ConcurrentOperationsAreFenced`
- Passed: `C448_V31_MigrationAndConcurrentOperations`

### AgentTaskLandVerificationEvidenceTests (green)

Executed: 9; passed: 9; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\AgentTaskLandVerificationEvidenceTests-green-5857044d9ec84660b2c82e1adf4c8e98\AgentTaskLandVerificationEvidenceTests-green-5857044d9ec84660b2c82e1adf4c8e98.trx`

- Passed: `C448_V34_CountersNeedExecutedPassingTests(0, 0, 0, False)`
- Passed: `C448_V34_CountersNeedExecutedPassingTests(1, 1, 0, True)`
- Passed: `C448_V34_CountersNeedExecutedPassingTests(1, 1, null, False)`
- Passed: `C448_V34_CountersNeedExecutedPassingTests(1, null, 0, False)`
- Passed: `C448_V34_CountersNeedExecutedPassingTests(2, 1, 0, False)`
- Passed: `C448_V34_CountersNeedExecutedPassingTests(2, 2, 1, False)`
- Passed: `C448_V34_CountersNeedExecutedPassingTests(7, 7, 0, True)`
- Passed: `C448_V34_CountersNeedExecutedPassingTests(invalid, 1, 0, False)`
- Passed: `C448_V34_CountersNeedExecutedPassingTests(null, 1, 0, False)`

### AgentTaskLandVerifierTests (green)

Executed: 10; passed: 10; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\AgentTaskLandVerifierTests-green-cce8cc39fafb4153b0778c5039c78533\AgentTaskLandVerifierTests-green-cce8cc39fafb4153b0778c5039c78533.trx`

- Passed: `C448_V32_VerifierOutcomeNeverMakesPrivateOutputDisposable(cancel)`
- Passed: `C448_V32_VerifierOutcomeNeverMakesPrivateOutputDisposable(fail)`
- Passed: `C448_V32_VerifierOutcomeNeverMakesPrivateOutputDisposable(pass)`
- Passed: `C448_V32_VerifierOutcomeNeverMakesPrivateOutputDisposable(throw)`
- Passed: `C448_V34_RealTUnitSelectionRequiresExecutedPassingTests(DoesNotExist, False)`
- Passed: `C448_V34_RealTUnitSelectionRequiresExecutedPassingTests(SelectedFailure, False)`
- Passed: `C448_V34_RealTUnitSelectionRequiresExecutedPassingTests(SelectedPass, True)`
- Passed: `C448_V35_RealVerifierPreservesPreExistingOutput(cancel)`
- Passed: `C448_V35_RealVerifierPreservesPreExistingOutput(fail)`
- Passed: `C448_V35_RealVerifierPreservesPreExistingOutput(pass)`

### AgentTaskLandConcurrencyTests (green)

Executed: 28; passed: 28; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\AgentTaskLandConcurrencyTests-green-68586e5ebc104f488b97d120cf1923d9\AgentTaskLandConcurrencyTests-green-68586e5ebc104f488b97d120cf1923d9.trx`

- Passed: `C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget(source-dirty)`
- Passed: `C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget(source-registration)`
- Passed: `C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget(source-staged)`
- Passed: `C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget(source-switch)`
- Passed: `C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget(source-untracked)`
- Passed: `C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget(source)`
- Passed: `C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget(target-dirty)`
- Passed: `C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget(target-staged)`
- Passed: `C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget(target-switch)`
- Passed: `C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget(target)`
- Passed: `C448_V14_EveryModeHonoursWriterAndLeaseHolds(follow-up, AlreadyPresent)`
- Passed: `C448_V14_EveryModeHonoursWriterAndLeaseHolds(follow-up, CleanupRetry)`
- Passed: `C448_V14_EveryModeHonoursWriterAndLeaseHolds(follow-up, Fresh)`
- Passed: `C448_V14_EveryModeHonoursWriterAndLeaseHolds(follow-up, ResumePublication)`
- Passed: `C448_V14_EveryModeHonoursWriterAndLeaseHolds(lease, AlreadyPresent)`
- Passed: `C448_V14_EveryModeHonoursWriterAndLeaseHolds(lease, CleanupRetry)`
- Passed: `C448_V14_EveryModeHonoursWriterAndLeaseHolds(lease, Fresh)`
- Passed: `C448_V14_EveryModeHonoursWriterAndLeaseHolds(lease, ResumePublication)`
- Passed: `C448_V14_EveryModeHonoursWriterAndLeaseHolds(shared, AlreadyPresent)`
- Passed: `C448_V14_EveryModeHonoursWriterAndLeaseHolds(shared, CleanupRetry)`
- Passed: `C448_V14_EveryModeHonoursWriterAndLeaseHolds(shared, Fresh)`
- Passed: `C448_V14_EveryModeHonoursWriterAndLeaseHolds(shared, ResumePublication)`
- Passed: `C448_V36_SettlementAndChildMergeExcludeLandingInBothOrders(False, False)`
- Passed: `C448_V36_SettlementAndChildMergeExcludeLandingInBothOrders(False, True)`
- Passed: `C448_V36_SettlementAndChildMergeExcludeLandingInBothOrders(True, False)`
- Passed: `C448_V36_SettlementAndChildMergeExcludeLandingInBothOrders(True, True)`
- Passed: `C448_V36_SettlementLeasePrecedesItsFirstMutation(False)`
- Passed: `C448_V36_SettlementLeasePrecedesItsFirstMutation(True)`

### aa632ab3-R2 (green)

Executed: 16; passed: 15; failed: 1; exit: 2.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\aa632ab3-R2-green-30bf7150289943dfbb6bf9c7fa4b60a0\aa632ab3-R2-green-30bf7150289943dfbb6bf9c7fa4b60a0.trx`

- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(advance)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(dirty)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(metadata-path)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(metadata-repository)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(metadata-target)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(metadata)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(same-sha-switch)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(staged)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(untracked)`
- Passed: `C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation(source)`
- Passed: `C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation(target-checkout)`
- Passed: `C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation(target)`
- Failed: `C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation(verification-filter)`
- Passed: `C448_V15_ExplicitRepostCannotReplaceTargetAdvanceIntent`
- Passed: `C448_V17_MissingRebaseResultRequiresFreshExplicitInspection`
- Passed: `C448_V25_LocalMergeCannotAdoptACommitAfterItsRebase`

Failure: ShouldAssertException: completed.Id should not be a7121741-f3a6-4502-aa65-b0388a0a5cc7 but was Additional Info: an explicit fresh request must not strand changed work behind an unadvanced Verified checkpoint

### aa632ab3-R2-fixed (green)

Executed: 16; passed: 16; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\aa632ab3-R2-fixed-green-c46cf33ff66a4bb48ddb4707020f8660\aa632ab3-R2-fixed-green-c46cf33ff66a4bb48ddb4707020f8660.trx`

- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(advance)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(dirty)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(metadata-path)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(metadata-repository)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(metadata-target)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(metadata)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(same-sha-switch)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(staged)`
- Passed: `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(untracked)`
- Passed: `C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation(source)`
- Passed: `C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation(target-checkout)`
- Passed: `C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation(target)`
- Passed: `C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation(verification-filter)`
- Passed: `C448_V15_ExplicitRepostCannotReplaceTargetAdvanceIntent`
- Passed: `C448_V17_MissingRebaseResultRequiresFreshExplicitInspection`
- Passed: `C448_V25_LocalMergeCannotAdoptACommitAfterItsRebase`

### aa632ab3-R3-publication (green)

Executed: 31; passed: 29; failed: 2; exit: 2.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\aa632ab3-R3-publication-green-967178a93dc849ceb9cb7f59de2d48d8\aa632ab3-R3-publication-green-967178a93dc849ceb9cb7f59de2d48d8.trx`

- Passed: `C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(detached)`
- Passed: `C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(dirty)`
- Passed: `C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(switched)`
- Passed: `C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(untracked)`
- Passed: `C448_V04_IgnoredFilesSurviveConfirmedPublication(·antiphon/report·md)`
- Passed: `C448_V04_IgnoredFilesSurviveConfirmedPublication(·claude/settings·local·json)`
- Passed: `C448_V04_IgnoredFilesSurviveConfirmedPublication(bin-private/data·txt)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(ancestry-error)`
- Failed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(canceled)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(empty)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(fetch-error)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(malformed)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(missing)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(read-error)`
- Failed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(timeout)`
- Passed: `C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained(False, False)`
- Passed: `C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained(False, True)`
- Passed: `C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained(True, False)`
- Passed: `C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained(True, True)`
- Passed: `C448_V08_RejectedPushWithIndependentContainmentIsAlreadyPresent`
- Passed: `C448_V09_PushAndConfirmationPreserveCompetingRemoteState(destination-after-push)`
- Passed: `C448_V09_PushAndConfirmationPreserveCompetingRemoteState(destination-before-push)`
- Passed: `C448_V09_PushAndConfirmationPreserveCompetingRemoteState(non-ff-before-push)`
- Passed: `C448_V09_PushAndConfirmationPreserveCompetingRemoteState(rewrite-after-push)`
- Passed: `C448_V09_PushExitCannotReplaceRemoteConfirmation(False)`
- Passed: `C448_V09_PushExitCannotReplaceRemoteConfirmation(True)`
- Passed: `C448_V22_ConfirmedPublicationUsesSavedReceipt(False)`
- Passed: `C448_V22_ConfirmedPublicationUsesSavedReceipt(True)`
- Passed: `C448_V26_OriginalAndPreparedPinsSurviveCleanupAndGarbageCollection`
- Passed: `C448_V33_CleanupRetriesDoNotEmitAnotherPublication(False)`
- Passed: `C448_V33_CleanupRetriesDoNotEmitAnotherPublication(True)`

Failure: NullReferenceException: Object reference not set to an instance of an object.

Failure: NullReferenceException: Object reference not set to an instance of an object.

### aa632ab3-R3-publication-fixed (green)

Executed: 31; passed: 31; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\aa632ab3-R3-publication-fixed-green-08ed8c3177d84118952ea65cc64ee992\aa632ab3-R3-publication-fixed-green-08ed8c3177d84118952ea65cc64ee992.trx`

- Passed: `C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(detached)`
- Passed: `C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(dirty)`
- Passed: `C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(switched)`
- Passed: `C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(untracked)`
- Passed: `C448_V04_IgnoredFilesSurviveConfirmedPublication(·antiphon/report·md)`
- Passed: `C448_V04_IgnoredFilesSurviveConfirmedPublication(·claude/settings·local·json)`
- Passed: `C448_V04_IgnoredFilesSurviveConfirmedPublication(bin-private/data·txt)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(ancestry-error)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(canceled)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(empty)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(fetch-error)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(malformed)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(missing)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(read-error)`
- Passed: `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(timeout)`
- Passed: `C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained(False, False)`
- Passed: `C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained(False, True)`
- Passed: `C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained(True, False)`
- Passed: `C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained(True, True)`
- Passed: `C448_V08_RejectedPushWithIndependentContainmentIsAlreadyPresent`
- Passed: `C448_V09_PushAndConfirmationPreserveCompetingRemoteState(destination-after-push)`
- Passed: `C448_V09_PushAndConfirmationPreserveCompetingRemoteState(destination-before-push)`
- Passed: `C448_V09_PushAndConfirmationPreserveCompetingRemoteState(non-ff-before-push)`
- Passed: `C448_V09_PushAndConfirmationPreserveCompetingRemoteState(rewrite-after-push)`
- Passed: `C448_V09_PushExitCannotReplaceRemoteConfirmation(False)`
- Passed: `C448_V09_PushExitCannotReplaceRemoteConfirmation(True)`
- Passed: `C448_V22_ConfirmedPublicationUsesSavedReceipt(False)`
- Passed: `C448_V22_ConfirmedPublicationUsesSavedReceipt(True)`
- Passed: `C448_V26_OriginalAndPreparedPinsSurviveCleanupAndGarbageCollection`
- Passed: `C448_V33_CleanupRetriesDoNotEmitAnotherPublication(False)`
- Passed: `C448_V33_CleanupRetriesDoNotEmitAnotherPublication(True)`

### aa632ab3-R3-cleanup (green)

Executed: 27; passed: 27; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\aa632ab3-R3-cleanup-green-bdf9a684854e45edb42d3c50e148f3c5\aa632ab3-R3-cleanup-green-bdf9a684854e45edb42d3c50e148f3c5.trx`

- Passed: `C448_V18_CleanupRetryPreservesChangedWork(advance)`
- Passed: `C448_V18_CleanupRetryPreservesChangedWork(detach)`
- Passed: `C448_V18_CleanupRetryPreservesChangedWork(ignored)`
- Passed: `C448_V18_CleanupRetryPreservesChangedWork(other-checkout)`
- Passed: `C448_V18_CleanupRetryPreservesChangedWork(pin-missing)`
- Passed: `C448_V18_CleanupRetryPreservesChangedWork(remote-delete)`
- Passed: `C448_V18_CleanupRetryPreservesChangedWork(remote-error)`
- Passed: `C448_V18_CleanupRetryPreservesChangedWork(remote-rewrite)`
- Passed: `C448_V18_CleanupRetryPreservesChangedWork(switch)`
- Passed: `C448_V18_CleanupRetryPreservesChangedWork(tracked)`
- Passed: `C448_V18_CleanupRetryPreservesChangedWork(unregistered)`
- Passed: `C448_V18_CleanupRetryPreservesChangedWork(untracked)`
- Passed: `C448_V19_AbsentComponentsRequireReceipt(False)`
- Passed: `C448_V19_AbsentComponentsRequireReceipt(True)`
- Passed: `C448_V27_EveryDeletionRefreshesRemoteProof(1, containing-descendant)`
- Passed: `C448_V27_EveryDeletionRefreshesRemoteProof(1, delete)`
- Passed: `C448_V27_EveryDeletionRefreshesRemoteProof(1, rewrite)`
- Passed: `C448_V27_EveryDeletionRefreshesRemoteProof(1, unavailable)`
- Passed: `C448_V27_EveryDeletionRefreshesRemoteProof(2, containing-descendant)`
- Passed: `C448_V27_EveryDeletionRefreshesRemoteProof(2, delete)`
- Passed: `C448_V27_EveryDeletionRefreshesRemoteProof(2, rewrite)`
- Passed: `C448_V27_EveryDeletionRefreshesRemoteProof(2, unavailable)`
- Passed: `C448_V27_EveryDeletionRefreshesRemoteProof(3, containing-descendant)`
- Passed: `C448_V27_EveryDeletionRefreshesRemoteProof(3, delete)`
- Passed: `C448_V27_EveryDeletionRefreshesRemoteProof(3, rewrite)`
- Passed: `C448_V27_EveryDeletionRefreshesRemoteProof(3, unavailable)`
- Passed: `C448_V36_UnknownPurposeCannotBorrowPublicationReceipt`

### aa632ab3-PC-1 (red)

Executed: 1; passed: 0; failed: 1; exit: 2.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\aa632ab3-PC-1-red-ef933c68f5984537a445d98378a80a97\aa632ab3-PC-1-red-ef933c68f5984537a445d98378a80a97.trx`

- Failed: `RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation`

Failure: ShouldAssertException: b.Id should not be 40f5874c-2ef8-4dd0-977c-3f4b917cb73c but was Additional Info: explicit retry must allocate B before any cleanup assertions

### aa632ab3-PC-1 (green)

Executed: 1; passed: 1; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\aa632ab3-PC-1-green-6df002cd9ff645d99d88cf751faba185\aa632ab3-PC-1-green-6df002cd9ff645d99d88cf751faba185.trx`

- Passed: `RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation`

### aa632ab3-PC-2 (red)

Executed: 1; passed: 0; failed: 1; exit: 2.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\aa632ab3-PC-2-red-fb8f5da0ada24ad289c73873efb954f7\aa632ab3-PC-2-red-fb8f5da0ada24ad289c73873efb954f7.trx`

- Failed: `RR_V5_OriginalPendingRequestCannotReplaceRefusal`

Failure: ShouldAssertException: await s.OperationsAsync() should have single item but had 2 items and was [Antiphon.Server.Domain.Entities.AgentTaskLanding (55121068), Antiphon.Server.Domain.Entities.AgentTaskLanding (55007105)]

### aa632ab3-PC-2 (green)

Executed: 1; passed: 1; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\aa632ab3-PC-2-green-1eb921a60b564aad821019d870c8972b\aa632ab3-PC-2-green-1eb921a60b564aad821019d870c8972b.trx`

- Passed: `RR_V5_OriginalPendingRequestCannotReplaceRefusal`

### aa632ab3-PC-3 (red)

Executed: 1; passed: 0; failed: 1; exit: 2.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\aa632ab3-PC-3-red-a504343812ca4ae8b0fd08172c9a025d\aa632ab3-PC-3-red-a504343812ca4ae8b0fd08172c9a025d.trx`

- Failed: `RR_V2_StillDirtyTargetCreatesFreshRefusal`

Failure: ShouldAssertException: b.Phase should be LandPhase.Refused but was LandPhase.Complete Additional Info: B must refuse the real dirty target before mutation

### aa632ab3-PC-3 (green)

Executed: 1; passed: 1; failed: 0; exit: 0.

TRX: `C:\Antiphon\worktrees\card-task-51407c07\.antiphon\refused-retry-51407c07\aa632ab3-PC-3-green-3eb7727dac754193888b290432c73101\aa632ab3-PC-3-green-3eb7727dac754193888b290432c73101.trx`

- Passed: `RR_V2_StillDirtyTargetCreatesFreshRefusal`
