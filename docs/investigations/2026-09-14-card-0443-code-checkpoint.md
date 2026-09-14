# CARD-0443 Code checkpoint: implementation and ordinary verification incomplete

Original Code task and landing owner: `6cb9c0d9`. Branch: `feat/card-task-6cb9c0d9`. Worktree: `C:\Antiphon\worktrees\card-task-6cb9c0d9`. Base: `1282f178d8763c4a92eacce553c5eda7d24169fa`. Latest compiled/tested source: `c80a8b8ab6a589490dfa1a1ee9a84617a50531f2`.

Plan: [2026-09-14 receipt-backed cleanup plan](../superpowers/plans/2026-09-14-card-0443-receipt-backed-cleanup-plan.md). Its 218 PC rows and all declared variants remain pending deliberate post-land SourceLanding Mutation. No deliberate mutant was executed. This checkpoint is not ready for ordinary Review, landing or deployment. Next stage remains Code.

The durable journal/migration, bounded Windows observation providers, receipt-backed two-slot coordinator, terminal Outcome projection, service registrations and lifecycle observations are implemented. Ordinary acceptance is incomplete. A source-name census finds targets for 115 PC rows and no target for 103 rows; names are not proof of complete assertions or variants. The eight-method Windows qualification class currently contains only the two real native-probe methods.

## Remaining implementation and coverage

- 37 authority targets: 36 Initial/Retry methods and the fresh tracked-state reader guard.
- 24 guarded-cleanup targets, including confinement/fresh authority, remaining-budget boundaries and actual child custody.
- Eight journal targets, ten physical JC worker-death cuts and the CLI upgrade/idempotence/legacy-row test.
- 25 Outcome targets and all 36 DC delivery crash cases, including recovery, immutable destination/payload, prior provenance and complete receipt predicates.
- Seven lifecycle/verifier targets; the planned lifecycle test class is absent.
- Two diagnostic targets for joined diagnostic child/readers and observation-only owners; real pinned Handle CSV qualification remains absent.
- Six additional Windows Git/Handle qualification methods, including intact transient retry versus partial removal.
- Audit authored methods against every specified argument and decisive assertion. Exact 262143/262144/262145 stream bytes, 31/32/33 owners, 32767/32768/32769 UTF8 JSON, 599/600/601 summary, 949/950/951 detail, 399/400/401 reason, 63/64/65 native entries and 1.999/2/2.001-second boundaries are not all implemented.

Additional inspection concerns to resolve with tests: terminal evidence fresh-read failure currently propagates; budget coverage needs to establish the boundary around post-retry branch cleanup; the owned-holder startup path can fail before its disposal guard and direct graph construction retains a provider. These are noticed gaps/risks, not reproduced production failures.

## Every PC remains pending

Pending means every declared mutation and argument/variant in the linked plan. In particular PC-147..182 require both Initial and Retry; PC-58/59 require OwnersObserved and InsufficientPrivileges; byte/count/time/display boundaries above remain separate variants. The table records only whether the target method name exists. PC-18 and PC-47 deliberately share one existing method.

| PC | Exact target | Target name present | Mutation and all plan variants |
|---|---|---|---|
| PC-1 | `WorktreeLockDiagnosticsTests.C443_UnsupportedPlatform` | Yes; coverage still requires audit | Pending |
| PC-2 | `WorktreeLockDiagnosticsTests.C443_RejectUntrustedToolPath` | Yes; coverage still requires audit | Pending |
| PC-3 | `WorktreeLockDiagnosticsTests.C443_InsufficientPrivileges` | Yes; coverage still requires audit | Pending |
| PC-4 | `WorktreeLockDiagnosticsTests.C443_ReadOnlyArgumentVector` | Yes; coverage still requires audit | Pending |
| PC-5 | `WorktreeLockDiagnosticsTests.C443_DiagnosticLocationsOutsideTarget` | Yes; coverage still requires audit | Pending |
| PC-6 | `WorktreeLockDiagnosticsTests.C443_TargetQueryPrecedesControls` | Yes; coverage still requires audit | Pending |
| PC-7 | `WorktreeLockDiagnosticsTests.C443_MissingFileControl` | Yes; coverage still requires audit | Pending |
| PC-8 | `WorktreeLockDiagnosticsTests.C443_MissingDirectoryControl` | Yes; coverage still requires audit | Pending |
| PC-9 | `WorktreeLockDiagnosticsTests.C443_ControlResourcesDisposed` | Yes; coverage still requires audit | Pending |
| PC-10 | `WorktreeGuardedCleanupTests.C443_CaptureCommittedBeforeRetry` | Yes; coverage still requires audit | Pending |
| PC-11 | `WorktreeGuardedCleanupTests.C443_OneCapturePerInvocation` | Yes; coverage still requires audit | Pending |
| PC-12 | `WorktreeGuardedCleanupTests.C443_InitialGitLockRefuses` | Yes; coverage still requires audit | Pending |
| PC-13 | `WorktreeGuardedCleanupTests.C443_NewGitLockStopsRetry` | Yes; coverage still requires audit | Pending |
| PC-14 | `WorktreeGuardedCleanupTests.C443_UnknownRegistrationStopsRetry` | Yes; coverage still requires audit | Pending |
| PC-15 | `WorktreeGuardedCleanupTests.C443_RecheckConfinement` | No; Code required | Pending |
| PC-16 | `WorktreeGuardedCleanupTests.C443_RetryStartsWithFreshAuthority` | No; Code required | Pending |
| PC-17 | `WorktreeManagerTests.WorktreeManager_try_remove_reports_directory_residue_when_a_file_is_held` | Yes; coverage still requires audit | Pending |
| PC-18 | `AgentTaskLandRemovalMatrixTests.C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent` | Yes; coverage still requires audit | Pending |
| PC-19 | `WorktreeGuardedCleanupTests.C443_RetryOnlyNativeSharingCodes` | Yes; coverage still requires audit | Pending |
| PC-20 | `WorktreeGuardedCleanupTests.C443_TwoGitSlotsMaximum` | Yes; coverage still requires audit | Pending |
| PC-21 | `WorktreeGuardedCleanupTests.C443_SingleBackoff` | Yes; coverage still requires audit | Pending |
| PC-22 | `WorktreeGuardedCleanupTests.C443_SharedAdditionalWorkDeadline` | No; Code required | Pending |
| PC-23 | `WorktreeGuardedCleanupTests.C443_RegistrationUsesRemainingBudget` | No; Code required | Pending |
| PC-24 | `WorktreeGuardedCleanupTests.C443_NoAbandonedGitChild` | No; Code required | Pending |
| PC-25 | `WorktreeLockDiagnosticsTests.C443_PropagateOuterCancellation` | Yes; coverage still requires audit | Pending |
| PC-26 | `WorktreeLockDiagnosticsTests.C443_SharedDiagnosticBudget` | Yes; coverage still requires audit | Pending |
| PC-27 | `WorktreeLockDiagnosticsTests.C443_CombinedOutputBound` | Yes; coverage still requires audit | Pending |
| PC-28 | `WorktreeLockDiagnosticsTests.C443_OwnerCountBound` | Yes; coverage still requires audit | Pending |
| PC-29 | `WorktreeLockDiagnosticsTests.C443_StructuredEvidenceBound` | Yes; coverage still requires audit | Pending |
| PC-30 | `WorktreeLockDiagnosticsTests.C443_DiagnosticChildReaped` | No; Code required | Pending |
| PC-31 | `WorktreeLockDiagnosticsTests.C443_OwnersAreObservationOnly` | No; Code required | Pending |
| PC-32 | `WorktreeLockDiagnosticsTests.C443_ValidateCsvSchema` | Yes; coverage still requires audit | Pending |
| PC-33 | `WorktreeLockDiagnosticsTests.C443_ExactRootAndDescendants` | Yes; coverage still requires audit | Pending |
| PC-34 | `WorktreeLockDiagnosticsTests.C443_UnresolvedAliasIsPartial` | Yes; coverage still requires audit | Pending |
| PC-35 | `WorktreeLockDiagnosticsTests.C443_EmptyRequiresCompleteCoverage` | Yes; coverage still requires audit | Pending |
| PC-36 | `WorktreeLockDiagnosticsTests.C443_PartialRetainsOwners` | Yes; coverage still requires audit | Pending |
| PC-37 | `WorktreeLockDiagnosticsTests.C443_ReusedPidDoesNotEnrichOldOwner` | Yes; coverage still requires audit | Pending |
| PC-38 | `WorktreeLockDiagnosticsTests.C443_UnknownAncestryStaysUnknown` | Yes; coverage still requires audit | Pending |
| PC-39 | `WorktreeLockDiagnosticsTests.C443_PathGone` | Yes; coverage still requires audit | Pending |
| PC-40 | `WorktreeGuardedCleanupTests.C443_PreserveFirstAndLastGitOutcomes` | Yes; coverage still requires audit | Pending |
| PC-41 | `AgentTaskWorktreeLockOutcomeTests.C443_SummaryPreservesEvidenceAtLimit` | No; Code required | Pending |
| PC-42 | `WorktreeLockDiagnosticsTests.C443_SanitizedStructuredEvidence` | Yes; coverage still requires audit | Pending |
| PC-43 | `AgentTaskWorktreeLockOutcomeTests.C443_RecoveredCleanupKeepsCapture` | No; Code required | Pending |
| PC-44 | `AgentTaskWorktreeLockOutcomeTests.C443_DiagnosticFailureDoesNotRefuseClean` | No; Code required | Pending |
| PC-45 | `AgentTaskWorktreeLockOutcomeTests.C443_ResidueKeepsPublication` | No; Code required | Pending |
| PC-46 | `AgentTaskLandStageOutcomeTests.reland_of_an_already_landed_task_runs_cleanup_only` | Yes; coverage still requires audit | Pending |
| PC-47 | `AgentTaskLandRemovalMatrixTests.C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent` | Yes; coverage still requires audit | Pending |
| PC-48 | `AgentTaskWorktreeLockLifecycleTests.C443_PersistDeliverBeforeStop` | No; Code required | Pending |
| PC-49 | `AgentTaskWorktreeLockLifecycleTests.C443_StopFailureIsNotExit` | No; Code required | Pending |
| PC-50 | `AgentTaskWorktreeLockLifecycleTests.C443_SkippedVerifyHasNoChild` | No; Code required | Pending |
| PC-51 | `AgentTaskWorktreeLockLifecycleTests.C443_ObserverOrderingUnchanged` | No; Code required | Pending |
| PC-52 | `AgentTaskWorktreeLockLifecycleTests.C443_WorktreeOnlyObservations` | No; Code required | Pending |
| PC-53 | `AgentTaskWorktreeLockLifecycleTests.C443_NoAdditionalStopperCalls` | No; Code required | Pending |
| PC-54 | `DelegationTestServicesTests.C443_DiagnosticRegistrationPreservesOverride` | Yes; coverage still requires audit | Pending |
| PC-55 | `AgentTaskWorktreeLockOutcomeTests.C443_OutcomeTransactionRecovery` | No; Code required | Pending |
| PC-56 | `AgentTaskWorktreeLockOutcomeTests.C443_EnqueueAcknowledgementRecovery` | No; Code required | Pending |
| PC-57 | `AgentTaskWorktreeLockOutcomeTests.C443_LostWakeupRecoversReceipt` | No; Code required | Pending |
| PC-58 | `AgentTaskWorktreeLockOutcomeTests.C443_BusyRecipientGetsOutcomeWhenIdle` | Yes; coverage still requires audit | Pending |
| PC-59 | `AgentTaskWorktreeLockOutcomeTests.C443_EligibleRecipientGetsOutcome` | Yes; coverage still requires audit | Pending |
| PC-60 | `AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsUserPrompt` | Yes; coverage still requires audit | Pending |
| PC-61 | `AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsDestination` | Yes; coverage still requires audit | Pending |
| PC-62 | `AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsIdentity` | Yes; coverage still requires audit | Pending |
| PC-63 | `AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsCompleteBody` | Yes; coverage still requires audit | Pending |
| PC-64 | `AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsSequenceFloor` | Yes; coverage still requires audit | Pending |
| PC-65 | `AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsTimeFloor` | Yes; coverage still requires audit | Pending |
| PC-66 | `AgentTaskWorktreeLockOutcomeTests.C443_ReceiptCommitRecoveryDoesNotRetype` | No; Code required | Pending |
| PC-67 | `AgentTaskWorktreeLockLifecycleTests.C443_ObservationFailureDoesNotChangeOutcome` | No; Code required | Pending |
| PC-68 | `WorktreeGuardedCleanupTests.C443_CreationDoesNotEnterCleanupCoordinator` | No; Code required | Pending |
| PC-69 | `WorktreeLockDiagnosticsTests.C443_UnknownCsvSchema` | Yes; coverage still requires audit | Pending |
| PC-70 | `WorktreeLockDiagnosticsTests.C443_ControlPidMustMatch` | Yes; coverage still requires audit | Pending |
| PC-71 | `WorktreeLockDiagnosticsTests.C443_ControlDirectoryOutsideWorktrees` | Yes; coverage still requires audit | Pending |
| PC-72 | `WorktreeLockDiagnosticsTests.C443_EvidencePathsOutsideTarget` | Yes; coverage still requires audit | Pending |
| PC-73 | `WorktreeDeleteAccessProbeTests.C443_ProbeRequestsDeleteAccess` | Yes; coverage still requires audit | Pending |
| PC-74 | `WorktreeDeleteAccessProbeTests.C443_ProbeSharesReadWriteDelete` | Yes; coverage still requires audit | Pending |
| PC-75 | `WorktreeDeleteAccessProbeTests.C443_ProbeNeverCreates` | Yes; coverage still requires audit | Pending |
| PC-76 | `WorktreeDeleteAccessProbeTests.C443_ProbeOpensDirectories` | Yes; coverage still requires audit | Pending |
| PC-77 | `WorktreeDeleteAccessProbeTests.C443_ProbeOpensReparsePointItself` | Yes; coverage still requires audit | Pending |
| PC-78 | `WorktreeDeleteAccessProbeTests.C443_ProbeCannotDeleteOrAlter` | Yes; coverage still requires audit | Pending |
| PC-79 | `WorktreeDeleteAccessProbeTests.C443_ProbeClosesEachHandle` | Yes; coverage still requires audit | Pending |
| PC-80 | `WorktreeDeleteAccessProbeTests.C443_ProbeHandlesNotInherited` | Yes; coverage still requires audit | Pending |
| PC-81 | `WorktreeDeleteAccessProbeTests.C443_ProbeCapturesLastErrorImmediately` | Yes; coverage still requires audit | Pending |
| PC-82 | `WorktreeDeleteAccessProbeTests.C443_ProbeRootFirst` | Yes; coverage still requires audit | Pending |
| PC-83 | `WorktreeDeleteAccessProbeTests.C443_ProbeCandidateCap` | Yes; coverage still requires audit | Pending |
| PC-84 | `WorktreeDeleteAccessProbeTests.C443_ProbeEnumerationCap` | Yes; coverage still requires audit | Pending |
| PC-85 | `WorktreeDeleteAccessProbeTests.C443_ProbeTwoSecondDeadline` | Yes; coverage still requires audit | Pending |
| PC-86 | `WorktreeDeleteAccessProbeTests.C443_ProbeUsesRemainingBudget` | Yes; coverage still requires audit | Pending |
| PC-87 | `WorktreeDeleteAccessProbeTests.C443_ProbeDoesNotReadContents` | Yes; coverage still requires audit | Pending |
| PC-88 | `WorktreeDeleteAccessProbeTests.C443_ProbeDoesNotTraverseReparse` | Yes; coverage still requires audit | Pending |
| PC-89 | `WorktreeDeleteAccessProbeTests.C443_ProbeRootIdentityRequired` | Yes; coverage still requires audit | Pending |
| PC-90 | `WorktreeDeleteAccessProbeTests.C443_ProbeValidatesBeforeOpen` | Yes; coverage still requires audit | Pending |
| PC-91 | `WorktreeDeleteAccessProbeTests.C443_ProbeValidatesAfterFailure` | Yes; coverage still requires audit | Pending |
| PC-92 | `WorktreeDeleteAccessProbeTests.C443_ProbeValidatesFinalHandlePath` | Yes; coverage still requires audit | Pending |
| PC-93 | `WorktreeDeleteAccessProbeTests.C443_ProbeSkipsGitAdmin` | Yes; coverage still requires audit | Pending |
| PC-94 | `WorktreeDeleteAccessProbeTests.C443_ProbeValidatesOwnerCandidates` | Yes; coverage still requires audit | Pending |
| PC-95 | `WorktreeDeleteAccessProbeTests.C443_PartialProbeKeepsPositive` | Yes; coverage still requires audit | Pending |
| PC-96 | `WorktreeDeleteAccessProbeTests.C443_NativeUnsupportedPlatform` | Yes; coverage still requires audit | Pending |
| PC-97 | `WorktreeDeleteAccessProbeTests.C443_NativeLimitationsArePartial` | Yes; coverage still requires audit | Pending |
| PC-98 | `WorktreeGuardedCleanupTests.C443_UnavailableHandleStillProbes` | Yes; coverage still requires audit | Pending |
| PC-99 | `WorktreeGuardedCleanupTests.C443_GitAndOwnersCannotNominate` | Yes; coverage still requires audit | Pending |
| PC-100 | `WorktreeGuardedCleanupTests.C443_IncompleteSuccessDoesNotRetry` | Yes; coverage still requires audit | Pending |
| PC-101 | `WorktreeGuardedCleanupTests.C443_TimeoutDoesNotRetry` | Yes; coverage still requires audit | Pending |
| PC-102 | `WorktreeGuardedCleanupTests.C443_ContextFreePublicationIsOnePass` | Yes; coverage still requires audit | Pending |
| PC-103 | `WorktreeGuardedCleanupTests.C443_LocalMergeIsOnePass` | No; Code required | Pending |
| PC-104 | `WorktreeGuardedCleanupTests.C443_VerificationIsOnePass` | No; Code required | Pending |
| PC-105 | `WorktreeCleanupJournalTests.C443_AttemptRequestUnique` | Yes; coverage still requires audit | Pending |
| PC-106 | `WorktreeCleanupJournalTests.C443_AttemptCoordinatesImmutable` | Yes; coverage still requires audit | Pending |
| PC-107 | `WorktreeCleanupJournalTests.C443_UnknownAttemptCommitReloaded` | Yes; coverage still requires audit | Pending |
| PC-108 | `WorktreeCleanupJournalTests.C443_InitialSlotBeforeLaunch` | No; Code required | Pending |
| PC-109 | `WorktreeCleanupJournalTests.C443_InitialSlotSpentAcrossRestart` | Yes; coverage still requires audit | Pending |
| PC-110 | `WorktreeCleanupJournalTests.C443_RetrySlotBeforeLaunch` | No; Code required | Pending |
| PC-111 | `WorktreeCleanupJournalTests.C443_RetrySlotSpentAcrossRestart` | Yes; coverage still requires audit | Pending |
| PC-112 | `WorktreeCleanupJournalTests.C443_FailureCommittedBeforeCollection` | No; Code required | Pending |
| PC-113 | `WorktreeCleanupJournalTests.C443_FirstFailureWriteOnce` | Yes; coverage still requires audit | Pending |
| PC-114 | `WorktreeCleanupJournalTests.C443_CaptureWriteOnce` | Yes; coverage still requires audit | Pending |
| PC-115 | `WorktreeCleanupJournalTests.C443_JournalUtf8Bound` | Yes; coverage still requires audit | Pending |
| PC-116 | `AgentTaskWorktreeLockOutcomeTests.C443_LastReasonIsNotCaptureStorage` | No; Code required | Pending |
| PC-117 | `AgentTaskWorktreeLockOutcomeTests.C443_CleanResidueRemainsNull` | No; Code required | Pending |
| PC-118 | `AgentTaskWorktreeLockOutcomeTests.C443_CurrentRequestOwnsAttempt` | No; Code required | Pending |
| PC-119 | `WorktreeCleanupJournalTests.C443_ContextCannotBorrowAttempt` | Yes; coverage still requires audit | Pending |
| PC-120 | `WorktreeCleanupJournalTests.C443_JournalDoesNotUseTrackedEvidence` | Yes; coverage still requires audit | Pending |
| PC-121 | `WorktreeCleanupJournalTests.C443_CaptureSaveFailureStopsRetry` | No; Code required | Pending |
| PC-122 | `WorktreeCleanupJournalTests.C443_PendingCaptureInterrupted` | Yes; coverage still requires audit | Pending |
| PC-123 | `WorktreeCleanupJournalTests.C443_InterruptedCaptureNotReobserved` | No; Code required | Pending |
| PC-124 | `WorktreeCleanupJournalTests.C443_CapturedRestartClosesAllowance` | No; Code required | Pending |
| PC-125 | `AgentTaskWorktreeLockOutcomeTests.C443_RemovalRecoveryUsesComponents` | No; Code required | Pending |
| PC-126 | `WorktreeCleanupJournalTests.C443_FinalDispositionAtomic` | No; Code required | Pending |
| PC-127 | `WorktreeCleanupJournalTests.C443_CaptureAcknowledgementReloaded` | Yes; coverage still requires audit | Pending |
| PC-128 | `WorktreeCleanupJournalTests.C443_LostResultIsNotInvented` | Yes; coverage still requires audit | Pending |
| PC-129 | `AgentTaskWorktreeLockOutcomeTests.C443_PriorCaptureStaysPrior` | No; Code required | Pending |
| PC-130 | `AgentTaskWorktreeLockOutcomeTests.C443_TaskDetailProjectsCapture` | No; Code required | Pending |
| PC-131 | `WorktreeCleanupJournalTests.C443_JournalHistoryOwnership` | No; Code required | Pending |
| PC-132 | `WorktreeCleanupJournalTests.C443_JournalConcurrencyRejectsLostUpdate` | Yes; coverage still requires audit | Pending |
| PC-133 | `WorktreeCleanupJournalTests.C443_UnknownAttemptSchemaRefuses` | Yes; coverage still requires audit | Pending |
| PC-134 | `AgentTaskWorktreeLockOutcomeTests.C443_LogFailureKeepsCommittedCapture` | No; Code required | Pending |
| PC-135 | `WorktreeGuardedCleanupTests.C443_InitialFirstInspectionRequired` | Yes; coverage still requires audit | Pending |
| PC-136 | `WorktreeGuardedCleanupTests.C443_InitialSecondInspectionRequired` | Yes; coverage still requires audit | Pending |
| PC-137 | `WorktreeGuardedCleanupTests.C443_InitialFirstIgnoredBoundary` | Yes; coverage still requires audit | Pending |
| PC-138 | `WorktreeGuardedCleanupTests.C443_InitialSecondIgnoredBoundary` | Yes; coverage still requires audit | Pending |
| PC-139 | `WorktreeGuardedCleanupTests.C443_InitialAuthorityRefreshRequired` | No; Code required | Pending |
| PC-140 | `WorktreeGuardedCleanupTests.C443_InitialFinalAuthorityAfterSlot` | No; Code required | Pending |
| PC-141 | `WorktreeGuardedCleanupTests.C443_RetryFirstInspectionRequired` | Yes; coverage still requires audit | Pending |
| PC-142 | `WorktreeGuardedCleanupTests.C443_RetrySecondInspectionRequired` | Yes; coverage still requires audit | Pending |
| PC-143 | `WorktreeGuardedCleanupTests.C443_RetryFirstIgnoredBoundary` | Yes; coverage still requires audit | Pending |
| PC-144 | `WorktreeGuardedCleanupTests.C443_RetrySecondIgnoredBoundary` | Yes; coverage still requires audit | Pending |
| PC-145 | `WorktreeGuardedCleanupTests.C443_RetryAuthorityRefreshRequired` | No; Code required | Pending |
| PC-146 | `WorktreeGuardedCleanupTests.C443_RetryFinalAuthorityAfterSlot` | No; Code required | Pending |
| PC-147 | `AgentTaskLandRemovalMatrixTests.C443_Authority_GenuineLease` | No; Code required | Pending |
| PC-148 | `AgentTaskLandRemovalMatrixTests.C443_Authority_CommonDirectory` | No; Code required | Pending |
| PC-149 | `AgentTaskLandRemovalMatrixTests.C443_Authority_ValidOids` | No; Code required | Pending |
| PC-150 | `AgentTaskLandRemovalMatrixTests.C443_Authority_DistinctRefs` | No; Code required | Pending |
| PC-151 | `AgentTaskLandRemovalMatrixTests.C443_Authority_ReceiptExists` | No; Code required | Pending |
| PC-152 | `AgentTaskLandRemovalMatrixTests.C443_Authority_ReceiptActive` | No; Code required | Pending |
| PC-153 | `AgentTaskLandRemovalMatrixTests.C443_Authority_PublishedReceipt` | No; Code required | Pending |
| PC-154 | `AgentTaskLandRemovalMatrixTests.C443_Authority_ReceiptTask` | No; Code required | Pending |
| PC-155 | `AgentTaskLandRemovalMatrixTests.C443_Authority_ReceiptSourceRef` | No; Code required | Pending |
| PC-156 | `AgentTaskLandRemovalMatrixTests.C443_Authority_ReceiptTargetRef` | No; Code required | Pending |
| PC-157 | `AgentTaskLandRemovalMatrixTests.C443_Authority_ReceiptTargetSha` | No; Code required | Pending |
| PC-158 | `AgentTaskLandRemovalMatrixTests.C443_Authority_ReceiptDeletionSha` | No; Code required | Pending |
| PC-159 | `AgentTaskLandRemovalMatrixTests.C443_Authority_ReceiptVerifiedSha` | No; Code required | Pending |
| PC-160 | `AgentTaskLandRemovalMatrixTests.C443_Authority_CleanupIntent` | No; Code required | Pending |
| PC-161 | `AgentTaskLandRemovalMatrixTests.C443_Authority_CleanupPhase` | No; Code required | Pending |
| PC-162 | `AgentTaskLandRemovalMatrixTests.C443_Authority_ReceiptRepository` | No; Code required | Pending |
| PC-163 | `AgentTaskLandRemovalMatrixTests.C443_Authority_ReceiptWorktree` | No; Code required | Pending |
| PC-164 | `AgentTaskLandRemovalMatrixTests.C443_Authority_ReceiptGitDirectory` | No; Code required | Pending |
| PC-165 | `AgentTaskLandRemovalMatrixTests.C443_Authority_ReceiptCommonDirectory` | No; Code required | Pending |
| PC-166 | `AgentTaskLandRemovalMatrixTests.C443_Authority_CurrentTask` | No; Code required | Pending |
| PC-167 | `AgentTaskLandRemovalMatrixTests.C443_Authority_TaskSucceeded` | No; Code required | Pending |
| PC-168 | `AgentTaskLandRemovalMatrixTests.C443_Authority_TaskCoordinates` | No; Code required | Pending |
| PC-169 | `AgentTaskLandRemovalMatrixTests.C443_Authority_TaskTarget` | No; Code required | Pending |
| PC-170 | `AgentTaskLandRemovalMatrixTests.C443_Authority_TaskRepository` | No; Code required | Pending |
| PC-171 | `AgentTaskLandRemovalMatrixTests.C443_Authority_TaskPath` | No; Code required | Pending |
| PC-172 | `AgentTaskLandRemovalMatrixTests.C443_Authority_TaskNotSourced` | No; Code required | Pending |
| PC-173 | `AgentTaskLandRemovalMatrixTests.C443_Authority_TaskNotMutation` | No; Code required | Pending |
| PC-174 | `AgentTaskLandRemovalMatrixTests.C443_Authority_OriginalPin` | No; Code required | Pending |
| PC-175 | `AgentTaskLandRemovalMatrixTests.C443_Authority_TargetPin` | No; Code required | Pending |
| PC-176 | `AgentTaskLandRemovalMatrixTests.C443_Authority_PreparedPin` | No; Code required | Pending |
| PC-177 | `AgentTaskLandRemovalMatrixTests.C443_Authority_RemoteContainment` | No; Code required | Pending |
| PC-178 | `AgentTaskLandRemovalMatrixTests.C443_Authority_RemoteRead` | No; Code required | Pending |
| PC-179 | `AgentTaskLandRemovalMatrixTests.C443_Authority_InspectionHead` | No; Code required | Pending |
| PC-180 | `AgentTaskLandRemovalMatrixTests.C443_Authority_InspectionCommon` | No; Code required | Pending |
| PC-181 | `AgentTaskLandRemovalMatrixTests.C443_Authority_InspectionAdmin` | No; Code required | Pending |
| PC-182 | `AgentTaskLandRemovalMatrixTests.C443_Authority_AcceptedInspection` | No; Code required | Pending |
| PC-183 | `WorktreeGuardedCleanupTests.C443_UnregisteredRootPreserved` | Yes; coverage still requires audit | Pending |
| PC-184 | `WorktreeGuardedCleanupTests.C443_MissingRegisteredRootPreserved` | No; Code required | Pending |
| PC-185 | `WorktreeGuardedCleanupTests.C443_DirectoryPostcondition` | Yes; coverage still requires audit | Pending |
| PC-186 | `WorktreeGuardedCleanupTests.C443_RegistrationPostcondition` | Yes; coverage still requires audit | Pending |
| PC-187 | `WorktreeGuardedCleanupTests.C443_BranchAuthorityFresh` | No; Code required | Pending |
| PC-188 | `WorktreeGuardedCleanupTests.C443_RecreatedSourceStopsBranch` | No; Code required | Pending |
| PC-189 | `WorktreeGuardedCleanupTests.C443_CheckedOutBranchPreserved` | No; Code required | Pending |
| PC-190 | `WorktreeGuardedCleanupTests.C443_BranchDeleteUsesOldSha` | Yes; coverage still requires audit | Pending |
| PC-191 | `WorktreeGuardedCleanupTests.C443_BranchDeleteNoDeref` | Yes; coverage still requires audit | Pending |
| PC-192 | `WorktreeGuardedCleanupTests.C443_BranchAbsentConfirmed` | No; Code required | Pending |
| PC-193 | `WorktreeGuardedCleanupTests.C443_BranchNotRetried` | Yes; coverage still requires audit | Pending |
| PC-194 | `WorktreeGuardedCleanupTests.C443_NoCleanupRescue` | Yes; coverage still requires audit | Pending |
| PC-195 | `WorktreeGuardedCleanupTests.C443_CheckpointUsesRemainingBudget` | No; Code required | Pending |
| PC-196 | `WorktreeGuardedCleanupTests.C443_CaptureSaveUsesRemainingBudget` | No; Code required | Pending |
| PC-197 | `WorktreeGuardedCleanupTests.C443_AuthorityUsesRemainingBudget` | No; Code required | Pending |
| PC-198 | `WorktreeGuardedCleanupTests.C443_InspectionUsesRemainingBudget` | No; Code required | Pending |
| PC-199 | `WorktreeGuardedCleanupTests.C443_RetryGitUsesRemainingBudget` | No; Code required | Pending |
| PC-200 | `AgentTaskWorktreeLockOutcomeTests.C443_BudgetExpiryStillSettles` | No; Code required | Pending |
| PC-201 | `WorktreeGuardedCleanupTests.C443_BothGitChildrenJournaled` | No; Code required | Pending |
| PC-202 | `WorktreeGuardedCleanupTests.C443_UncertainChildJournalFencesRecovery` | No; Code required | Pending |
| PC-203 | `WorktreeCleanupJournalTests.C443_JournalScopeLifetime` | Yes; coverage still requires audit | Pending |
| PC-204 | `AgentTaskWorktreeLockOutcomeTests.C443_OutcomeDestinationImmutable` | No; Code required | Pending |
| PC-205 | `AgentTaskWorktreeLockOutcomeTests.C443_OutcomeQueuePayloadImmutable` | No; Code required | Pending |
| PC-206 | `AgentTaskWorktreeLockOutcomeTests.C443_ReceiptNeedsAttempt` | No; Code required | Pending |
| PC-207 | `AgentTaskWorktreeLockOutcomeTests.C443_ReceiptRejectsQueuedKinds` | No; Code required | Pending |
| PC-208 | `AgentTaskWorktreeLockOutcomeTests.C443_ReceiptNeedsKnownFloor` | No; Code required | Pending |
| PC-209 | `AgentTaskWorktreeLockOutcomeTests.C443_ConfirmedReceiptNoReplay` | No; Code required | Pending |
| PC-210 | `AgentTaskWorktreeLockOutcomeTests.C443_SummarySixHundredLimit` | No; Code required | Pending |
| PC-211 | `AgentTaskWorktreeLockOutcomeTests.C443_StageNineFiftyLimit` | No; Code required | Pending |
| PC-212 | `AgentTaskWorktreeLockOutcomeTests.C443_ProtocolWiresCurrentCleanupContext` | No; Code required | Pending |
| PC-213 | `WorktreeGuardedCleanupTests.C443_InitialCleanHasNoCapture` | Yes; coverage still requires audit | Pending |
| PC-214 | `AgentTaskLandRemovalMatrixTests.C443_AuthorityReaderIgnoresTrackedState` | No; Code required | Pending |
| PC-215 | `WorktreeGuardedCleanupTests.C443_CoordinatorOuterCancellation` | Yes; coverage still requires audit | Pending |
| PC-216 | `WorktreeDeleteAccessProbeTests.C443_NativeOuterCancellation` | Yes; coverage still requires audit | Pending |
| PC-217 | `WorktreeLockDiagnosticsWindowsTests.C443_NativeFileSharing32` | Yes; coverage still requires audit | Pending |
| PC-218 | `WorktreeLockDiagnosticsTests.C443_DiagnosticsNeverUseShell` | Yes; coverage still requires audit | Pending |

