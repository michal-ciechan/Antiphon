# CARD-1514 per-test observations

2026-10-22; executable source `ffa98a8f76588c12df6b0437d24b541762b95f09`.
One build, one bounded invocation per class. Raw files: `/work/test-evidence/card-1514-85ca6a8c/<class>/`.
See [interpretation and limitations](2026-10-22-card-1514-slow-test-profile.md).
Durations are seconds from TRX or completed diagnostic records; p95 is nearest-rank.
A timed-out sample is censored and does not establish the unseen tail. Native Git counts include optional capture reads; only serial landing selections receive per-test attribution.

## AgentTaskLandPersistenceFailureTests

UTC 2026-10-22T22:09:29.106Z to 2026-10-22T22:13:38.317Z; driver wall 249.211 s; exit 0; final diagnostic states {"Passed":52}.

- `run.trx` SHA-256 `f2384b5825f317271633fd4e58f9fc68e7d0d88215d7f3b33d1b72841ab50d3a`.
- `diag/log_261022220934027.diag` SHA-256 `d6e8df695faac87c80834dee96e0efec4efed96c37235f16351fffbbcffb27ca`.
- `git-trace.jsonl` SHA-256 `8cbef10d06d605e19f970e1a9716eb029a7a0e608cad9e812b5d53bcb22a6c52`.
- `console.log` SHA-256 `0c50cdb48579619a4863c0d50ecda513dca464e64ae2a29c2e8b6b47378d7a53`.
- `run.json` SHA-256 `f9445b24e4e26086fdfaab9cb3c252b086b7c907f4ccaa1b56400d6de7ce63b3`.

| Test / argument variant | Outcome | Seconds | Evidence line | Native Git processes | Native Git seconds |
|---|---|---:|---|---:|---:|
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(PublicationConfirmed, True) | Passed | 22.317612 | run.trx:8 | 330 | 1.637258 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(Prepared, True) | Passed | 8.162603 | run.trx:43 | 333 | 1.794734 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(PublicationConfirmed, False) | Passed | 5.107683 | run.trx:44 | 344 | 1.266170 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(PushStarted, True) | Passed | 2.650091 | run.trx:45 | 364 | 0.755464 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(Verified, False) | Passed | 2.269676 | run.trx:46 | 360 | 0.707544 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(RebaseStarted, False) | Passed | 2.858487 | run.trx:47 | 348 | 0.837892 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(PushStarted, False) | Passed | 3.132780 | run.trx:48 | 350 | 0.885109 |
| C448_F07_RefusalEvidenceAndTerminalEventCommitTogether(False) | Passed | 1.754273 | run.trx:49 | 156 | 0.403285 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(Prepared, False) | Passed | 1.522275 | run.trx:50 | 167 | 0.397453 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(Inspected, True) | Passed | 5.519871 | run.trx:51 | 327 | 0.653805 |
| C448_V23_DeliveryFailureKeepsTheCommittedPublicationAndEvent | Passed | 3.536351 | run.trx:52 | 298 | 0.830015 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(RecoveryPinned, True) | Passed | 3.825229 | run.trx:53 | 333 | 0.977958 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(Verified, True) | Passed | 9.191564 | run.trx:54 | 334 | 1.850041 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(RecoveryPinned, False) | Passed | 6.094415 | run.trx:55 | 344 | 1.408508 |
| C467_V06_AtomicSettlementFaultMatrix(success, before-save) | Passed | 1.901474 | run.trx:56 | 355 | 0.525238 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(RebaseStarted, True) | Passed | 1.552638 | run.trx:57 | 157 | 0.346952 |
| C448_V23_NotificationFailureCannotReplaceConfirmedPublication | Passed | 2.322749 | run.trx:58 | 295 | 0.609584 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(TargetAdvanceStarted, False) | Passed | 2.277939 | run.trx:59 | 360 | 0.620887 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(Inspected, False) | Passed | 3.165367 | run.trx:60 | 346 | 1.039736 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(CleanupStarted, False) | Passed | 1.817486 | run.trx:61 | 330 | 0.484546 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(TargetAdvanceStarted, True) | Passed | 3.387490 | run.trx:62 | 334 | 0.809361 |
| C448_F07_RefusalEvidenceAndTerminalEventCommitTogether(True) | Passed | 1.690921 | run.trx:63 | 156 | 0.343177 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(LocalTargetAdvanced, True) | Passed | 2.858382 | run.trx:64 | 364 | 0.823710 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(CleanupStarted, True) | Passed | 2.435561 | run.trx:65 | 330 | 0.695073 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(LocalTargetAdvanced, False) | Passed | 2.757513 | run.trx:66 | 350 | 0.795060 |
| C448_F04_PushResultAcknowledgementCannotAuthorizeCleanup(True) | Passed | 2.623075 | run.trx:67 | 331 | 0.738726 |
| C448_F04_PushResultAcknowledgementCannotAuthorizeCleanup(False) | Passed | 2.503379 | run.trx:68 | 331 | 0.710027 |
| C467_V06_AtomicSettlementFaultMatrix(success, after-save) | Passed | 2.679907 | run.trx:69 | 355 | 0.770847 |
| C467_V06_AtomicSettlementFaultMatrix(success, after-commit) | Passed | 2.722136 | run.trx:70 | 295 | 0.679793 |
| C467_V06_AtomicSettlementFaultMatrix(refusal, before-save) | Passed | 1.169116 | run.trx:71 | 88 | 0.256793 |
| C467_V06_AtomicSettlementFaultMatrix(refusal, after-save) | Passed | 1.026329 | run.trx:72 | 88 | 0.233985 |
| C467_V06_AtomicSettlementFaultMatrix(success, commit) | Passed | 3.010346 | run.trx:73 | 355 | 0.863964 |
| C467_V06_AtomicSettlementFaultMatrix(refusal, after-commit) | Passed | 0.793691 | run.trx:74 | 60 | 0.173234 |
| C467_V06_AtomicSettlementFaultMatrix(refusal, commit) | Passed | 0.919080 | run.trx:75 | 88 | 0.234958 |
| C467_V06_AtomicSettlementFaultMatrix(cleanup, before-save) | Passed | 3.471253 | run.trx:76 | 425 | 0.920503 |
| C467_V06_AtomicSettlementFaultMatrix(cleanup, commit) | Passed | 3.073850 | run.trx:77 | 425 | 0.850330 |
| C467_V06_AtomicSettlementFaultMatrix(cleanup, after-save) | Passed | 3.058391 | run.trx:78 | 425 | 0.827255 |
| C448_F07_PublicationSettlementIsAtomic(False) | Passed | 2.058477 | run.trx:79 | 356 | 0.613111 |
| C467_V06_AtomicSettlementFaultMatrix(cleanup, after-commit) | Passed | 2.151775 | run.trx:80 | 365 | 0.555378 |
| C448_F07_PublicationSettlementIsAtomic(True) | Passed | 2.111423 | run.trx:81 | 310 | 0.627169 |
| C448_F03_TargetIntentAcknowledgement(False) | Passed | 2.818780 | run.trx:82 | 360 | 0.813749 |
| C448_F03_TargetIntentAcknowledgement(True) | Passed | 2.309988 | run.trx:83 | 334 | 0.687492 |
| C448_F04_PushIntentAcknowledgement(False) | Passed | 2.420863 | run.trx:84 | 350 | 0.725311 |
| C448_F05_PublicationReceiptAcknowledgement(False) | Passed | 2.627409 | run.trx:85 | 344 | 0.779340 |
| C448_F04_PushIntentAcknowledgement(True) | Passed | 2.649689 | run.trx:86 | 364 | 0.811648 |
| C448_F05_PublicationReceiptAcknowledgement(True) | Passed | 2.451764 | run.trx:87 | 330 | 0.710450 |
| C448_F06_CleanupIntentAcknowledgement(False) | Passed | 2.352486 | run.trx:88 | 330 | 0.659097 |
| C448_F06_CleanupIntentAcknowledgement(True) | Passed | 2.359815 | run.trx:89 | 330 | 0.681784 |
| C448_F01_SourcePinResultMustBeAcknowledgedBeforeTheNextPin(False) | Passed | 3.089066 | run.trx:90 | 339 | 0.875100 |
| C448_F01_SourcePinResultMustBeAcknowledgedBeforeTheNextPin(True) | Passed | 2.708371 | run.trx:91 | 339 | 0.740461 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(Complete, False) | Passed | 3.040438 | run.trx:92 | 356 | 0.911370 |
| C448_V16_AcknowledgedCheckpointsGateDependentMutations(Complete, True) | Passed | 3.620074 | run.trx:93 | 310 | 1.063748 |

## AgentTaskLandCheckpointMatrixTests

UTC 2026-10-22T22:13:45.955Z to 2026-10-22T22:17:57.171Z; driver wall 251.216 s; exit 0; final diagnostic states {"Passed":43}.

- `run.trx` SHA-256 `c6311e02f5fe328829da25acf28fa115275e4e843d9f1151eec5a646122a4983`.
- `diag/log_261022221353244.diag` SHA-256 `2d9b6789a85e5b3093581a8f63f8754de4c5e079bea6c3eb4d10ffd4fb4d3d2b`.
- `git-trace.jsonl` SHA-256 `706c13153067d0d4216ecb08badd6ad6385106e52ddc1446407060f6e39e05d6`.
- `console.log` SHA-256 `61fb6d5217dc7ee67171c00ffea6e0ead6799f94a75e33d9875905ec9c5b51ff`.
- `run.json` SHA-256 `783e9cc38bc70c34ca9c263158e8204a7f0be3c6a23a487e4f37e3c3f8668b0d`.

| Test / argument variant | Outcome | Seconds | Evidence line | Native Git processes | Native Git seconds |
|---|---|---:|---|---:|---:|
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C15, source) | Passed | 7.306967 | run.trx:8 | 237 | 0.434654 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C16, source) | Passed | 4.473278 | run.trx:42 | 296 | 0.745072 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C11, destination) | Passed | 2.269858 | run.trx:43 | 211 | 0.410490 |
| C448_V15_ResumeUsesOnlyAcknowledgedCheckpoints(C21, Complete, False) | Passed | 2.382955 | run.trx:44 | 362 | 0.579195 |
| C448_V15_ResumeUsesOnlyAcknowledgedCheckpoints(C11, PushStarted, True) | Passed | 3.065163 | run.trx:45 | 370 | 0.909231 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C13, destination) | Passed | 1.985248 | run.trx:46 | 231 | 0.492472 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C12, destination) | Passed | 1.935964 | run.trx:47 | 218 | 0.471280 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C17, source) | Passed | 2.194028 | run.trx:48 | 316 | 0.591380 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C11, source) | Passed | 3.679940 | run.trx:49 | 212 | 0.649842 |
| C448_V15_ResumeUsesOnlyAcknowledgedCheckpoints(C15, CleanupStarted, True) | Passed | 6.123619 | run.trx:50 | 336 | 1.721351 |
| C448_C09_ResumedTargetCannotAdoptAnEarlierAncestor | Passed | 6.993706 | run.trx:51 | 341 | 1.907643 |
| C448_V15_ChangedRecoveryPreparationNeverAdoptsNewWork(C02, source) | Passed | 3.524835 | run.trx:52 | 139 | 0.787063 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C09, destination) | Passed | 5.281162 | run.trx:53 | 194 | 0.999960 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C10, source) | Passed | 4.027998 | run.trx:54 | 212 | 1.034950 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C13, source) | Passed | 2.798200 | run.trx:55 | 232 | 0.721094 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C10, destination) | Passed | 2.880568 | run.trx:56 | 211 | 0.699807 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C15, destination) | Passed | 3.162760 | run.trx:57 | 236 | 0.891618 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C14, destination) | Passed | 2.317246 | run.trx:58 | 228 | 0.600395 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C16, destination) | Passed | 6.613660 | run.trx:59 | 295 | 1.624437 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C14, source) | Passed | 2.592723 | run.trx:60 | 229 | 0.575129 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C12, source) | Passed | 3.233455 | run.trx:61 | 219 | 0.730809 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C09, source) | Passed | 2.782060 | run.trx:62 | 195 | 0.595260 |
| C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(C17, destination) | Passed | 3.710742 | run.trx:63 | 315 | 0.747064 |
| C665_WorkerDeathKeepsRecordedTreeAcrossActualRegistrationDrop(False) | Passed | 28.986465 | run.trx:64 | 510 | 1.158769 |
| C448_V15_ChangedRecoveryPreparationNeverAdoptsNewWork(C02, target) | Passed | 2.774305 | run.trx:65 | 329 | 0.781863 |
| C448_V15_ChangedRecoveryPreparationNeverAdoptsNewWork(C03, source) | Passed | 2.968349 | run.trx:66 | 150 | 0.669086 |
| C448_V15_ChangedRecoveryPreparationNeverAdoptsNewWork(C03, pin) | Passed | 4.439180 | run.trx:67 | 157 | 0.761310 |
| C448_V15_ChangedRecoveryPreparationNeverAdoptsNewWork(C03, target) | Passed | 3.879466 | run.trx:68 | 341 | 1.114468 |
| C448_V15_ChangedRecoveryPreparationNeverAdoptsNewWork(C07, source) | Passed | 3.474399 | run.trx:69 | 177 | 0.729075 |
| C448_V15_ChangedRecoveryPreparationNeverAdoptsNewWork(C07, target) | Passed | 3.955365 | run.trx:70 | 335 | 0.944121 |
| C448_V15_ChangedRecoveryPreparationNeverAdoptsNewWork(C08, source) | Passed | 5.274170 | run.trx:71 | 204 | 1.370806 |
| C448_V15_ResumeUsesOnlyAcknowledgedCheckpoints(C13, PublicationConfirmed, False) | Passed | 3.230718 | run.trx:72 | 350 | 0.886666 |
| C448_V15_ChangedRecoveryPreparationNeverAdoptsNewWork(C08, target) | Passed | 2.598189 | run.trx:73 | 336 | 0.745474 |
| C448_V15_ChangedRecoveryPreparationNeverAdoptsNewWork(C09, target) | Passed | 1.893826 | run.trx:74 | 336 | 0.579625 |
| C448_V15_ResumeUsesOnlyAcknowledgedCheckpoints(C02, Inspected, True) | Passed | 2.445699 | run.trx:75 | 332 | 0.717723 |
| C448_V15_ResumeUsesOnlyAcknowledgedCheckpoints(C04, RecoveryPinned, True) | Passed | 4.400101 | run.trx:76 | 338 | 1.218107 |
| C448_V15_ResumeUsesOnlyAcknowledgedCheckpoints(C07, Prepared, True) | Passed | 6.800799 | run.trx:77 | 338 | 1.565972 |
| C448_V15_ResumeUsesOnlyAcknowledgedCheckpoints(C06, Prepared, False) | Passed | 3.982073 | run.trx:78 | 170 | 1.045308 |
| C448_V15_ResumeUsesOnlyAcknowledgedCheckpoints(C08, Verified, True) | Passed | 5.911607 | run.trx:79 | 339 | 1.470605 |
| C448_V15_ResumeUsesOnlyAcknowledgedCheckpoints(C09, TargetAdvanceStarted, True) | Passed | 6.602638 | run.trx:80 | 339 | 1.729803 |
| C448_V15_ResumeUsesOnlyAcknowledgedCheckpoints(C10, LocalTargetAdvanced, True) | Passed | 4.170800 | run.trx:81 | 370 | 1.483894 |
| C448_V15_ChangedRecoveryPreparationNeverAdoptsNewWork(C09, source) | Passed | 3.429890 | run.trx:82 | 204 | 0.826390 |
| C665_WorkerDeathKeepsRecordedTreeAcrossActualRegistrationDrop(True) | Passed | 29.214773 | run.trx:83 | 331 | 1.097728 |

## AgentTaskLandPublicationTests

UTC 2026-10-22T22:18:10.998Z to 2026-10-22T22:23:02.479Z; driver wall 291.481 s; exit 2; final diagnostic states {"Passed":61,"Skipped":1,"Error":1}.

- `run.trx` SHA-256 `ea1ab47170578f421b4baaa94be9504a724ee1a206596592306fdb98ee1cbe19`.
- `diag/log_261022221834354.diag` SHA-256 `6ade503c14604c0684c2a9c3cf297272ce300f3eaed0a998a9fdc6a48aad75de`.
- `git-trace.jsonl` SHA-256 `5113d33772a6078332497c703ab05de88625967284f6d8dfcfd70c11f08efe63`.
- `console.log` SHA-256 `729d313fa53f083829c842fbe0a46eb0e1bbf14b3f582aad8d801f362ceceb8d`.
- `run.json` SHA-256 `0777d17e0ce4316c8b784ad527739eacb0f02971f9d6aee2953ca8f9aa58f6bd`.

| Test / argument variant | Outcome | Seconds | Evidence line | Native Git processes | Native Git seconds |
|---|---|---:|---|---:|---:|
| C448_V09_PushExitCannotReplaceRemoteConfirmation(True) | Passed | 13.012123 | run.trx:8 | 195 | 0.640695 |
| C494_LostPushAckReconcilesRealTargetBeforeSourceChecks | Passed | 7.115037 | run.trx:42 | 287 | 1.460194 |
| C688_LandNeverRunsGitInTheSourceWorktree | Passed | 10.098767 | run.trx:43 | 298 | 2.105376 |
| C688_RebaseAndVerifyRunInTheLandWorktree | Passed | 8.113852 | run.trx:44 | 301 | 1.948707 |
| C688_PushPrecedesCanonicalAdvance | Passed | 4.132544 | run.trx:45 | 296 | 1.167300 |
| C688_DirtyCanonicalCheckoutLandsWithResidue | Passed | 3.123256 | run.trx:46 | 291 | 0.696211 |
| C688_TargetCheckedOutElsewhereIsResidueNotRefusal | Passed | 3.465425 | run.trx:47 | 289 | 0.953810 |
| C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(missing, False) | Passed | 3.128222 | run.trx:48 | 290 | 0.857811 |
| C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(malformed, False) | Passed | 1.434299 | run.trx:49 | 290 | 0.408836 |
| C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(empty-ref, False) | Passed | 2.500865 | run.trx:50 | 290 | 0.668862 |
| C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(directory, False) | Passed | 2.747598 | run.trx:51 | 290 | 0.627828 |
| C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(missing, True) | Passed | 3.384016 | run.trx:52 | 295 | 0.917580 |
| C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(malformed, True) | Passed | 1.381008 | run.trx:53 | 295 | 0.380871 |
| C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(locked, False) | NotExecuted | — | run.trx:54 | 0 | 0.000000 |
| C448_V09_PushExitCannotReplaceRemoteConfirmation(False) | Passed | 1.399186 | run.trx:59 | 193 | 0.338685 |
| C688_Repair_AlreadyCanonicalRecordsLocalIdentity(False) | Passed | 2.020502 | run.trx:60 | 222 | 0.500623 |
| C688_LocalMasterBehindOriginRebasesOntoOrigin | Passed | 2.159683 | run.trx:61 | 305 | 0.626980 |
| C688_LocalMasterAheadOfOriginRefuses | Passed | 2.274917 | run.trx:62 | 105 | 0.493979 |
| C642_ProtocolInspectionsAreIdentityAndStatus | Passed | 0.956701 | run.trx:63 | 0 | 0.000000 |
| C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt(protected, False, before-enqueue) | Passed | 18.379855 | run.trx:64 | 354 | 1.265702 |
| C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt(protected, True, before-enqueue) | Passed | 8.037347 | run.trx:65 | 354 | 1.682548 |
| C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt(protected, False, queue-inserted) | Passed | 5.927923 | run.trx:66 | 354 | 1.430736 |
| C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt(protected, True, queue-inserted) | Passed | 6.777049 | run.trx:67 | 354 | 1.582746 |
| C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt(retained, False, before-enqueue) | Passed | 5.189561 | run.trx:68 | 407 | 1.132927 |
| C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt(retained, True, before-enqueue) | Passed | 6.567833 | run.trx:69 | 407 | 1.203958 |
| C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt(retained, False, queue-inserted) | Passed | 8.991900 | run.trx:70 | 407 | 1.504285 |
| C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt(retained, True, queue-inserted) | Passed | 7.237972 | run.trx:71 | 407 | 1.529004 |
| C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt(protected, False, before-enqueue, canonical_checkout_unknown) | Passed | 7.151220 | run.trx:72 | 354 | 1.651957 |
| C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt(protected, True, queue-inserted, canonical_checkout_changed) | Passed | 3.391799 | run.trx:73 | 354 | 0.666569 |
| C688_Repair_AlreadyCanonicalRecordsLocalIdentity(True) | Passed | 1.624259 | run.trx:74 | 220 | 0.416549 |
| C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt(retained, True, before-enqueue, canonical_checkout_unknown) | Passed | 4.203392 | run.trx:75 | 407 | 0.801632 |
| C665_ProtectedIgnoredRefusalNamesPaths | Passed | 5.733544 | run.trx:76 | 245 | 1.137228 |
| C448_V04_IgnoredFilesSurviveConfirmedPublication(·antiphon/inbox/photo·png) | Failed | 2.568694 | run.trx:77 | 161 | 0.523979 |
| C448_V09_PushAndConfirmationPreserveCompetingRemoteState(non-ff-before-push) | Passed | 2.536789 | run.trx:117 | 210 | 0.693845 |
| C448_V09_PushAndConfirmationPreserveCompetingRemoteState(rewrite-after-push) | Passed | 3.037501 | run.trx:118 | 210 | 0.680975 |
| C448_V09_PushAndConfirmationPreserveCompetingRemoteState(destination-before-push) | Passed | 2.986016 | run.trx:119 | 197 | 0.688632 |
| C448_V09_PushAndConfirmationPreserveCompetingRemoteState(destination-after-push) | Passed | 3.116339 | run.trx:120 | 200 | 0.982497 |
| C448_V26_OriginalAndPreparedPinsSurviveCleanupAndGarbageCollection | Passed | 4.957167 | run.trx:121 | 315 | 1.406404 |
| C448_V08_RejectedPushWithIndependentContainmentIsAlreadyPresent | Passed | 4.502582 | run.trx:122 | 298 | 1.259136 |
| C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(read-error) | Passed | 1.193226 | run.trx:123 | 96 | 0.277969 |
| C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(empty) | Passed | 1.363462 | run.trx:124 | 96 | 0.334922 |
| C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(malformed) | Passed | 1.638794 | run.trx:125 | 96 | 0.523296 |
| C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(missing) | Passed | 2.806257 | run.trx:126 | 96 | 0.649272 |
| C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(fetch-error) | Passed | 1.223148 | run.trx:127 | 97 | 0.306224 |
| C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(ancestry-error) | Passed | 3.761983 | run.trx:128 | 103 | 0.682082 |
| C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(timeout) | Passed | 2.457693 | run.trx:129 | 93 | 0.613076 |
| C665_DisposableOnlyIgnoredContentIsRemovedOnLand | Passed | 3.064176 | run.trx:130 | 298 | 0.760670 |
| C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(canceled) | Passed | 1.121031 | run.trx:131 | 93 | 0.287158 |
| C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained(True, False) | Passed | 2.841093 | run.trx:132 | 221 | 0.759352 |
| C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained(False, True) | Passed | 0.781497 | run.trx:133 | 107 | 0.221024 |
| C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained(True, True) | Passed | 2.333844 | run.trx:134 | 100 | 0.530668 |
| C448_V33_CleanupRetriesDoNotEmitAnotherPublication(False) | Passed | 9.162601 | run.trx:135 | 364 | 1.848966 |
| C448_V33_CleanupRetriesDoNotEmitAnotherPublication(True) | Passed | 7.976424 | run.trx:136 | 279 | 1.926554 |
| C448_V22_ConfirmedPublicationUsesSavedReceipt(False) | Passed | 3.433137 | run.trx:137 | 300 | 0.967783 |
| C448_V22_ConfirmedPublicationUsesSavedReceipt(True) | Passed | 1.552531 | run.trx:138 | 217 | 0.392365 |
| C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(detached) | Passed | 1.623255 | run.trx:139 | 153 | 0.381012 |
| C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(switched) | Passed | 1.490741 | run.trx:140 | 153 | 0.364689 |
| C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(dirty) | Passed | 1.334265 | run.trx:141 | 165 | 0.327055 |
| C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(untracked) | Passed | 1.330731 | run.trx:142 | 165 | 0.358714 |
| C448_V04_IgnoredFilesSurviveConfirmedPublication(·antiphon/report·md) | Passed | 1.745474 | run.trx:143 | 166 | 0.425837 |
| C448_V04_IgnoredFilesSurviveConfirmedPublication(·claude/settings·local·json) | Passed | 1.683937 | run.trx:144 | 166 | 0.432613 |
| C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained(False, False) | Passed | 2.376862 | run.trx:145 | 304 | 0.706770 |
| C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt(retained, False, queue-inserted, canonical_checkout_changed) | Passed | 3.684121 | run.trx:146 | 407 | 0.794247 |

## DelegationDispatchRecoveryBoundaryTests

UTC 2026-10-22T22:23:19.844Z to 2026-10-22T22:24:28.242Z; driver wall 68.398 s; exit 2; final diagnostic states {"Passed":374,"Error":7}.

- `run.trx` SHA-256 `cac2b329d636593571586f2ce2422f02ec93dafc96116b8f0dd811a79ce1e4ec`.
- `diag/log_261022222323909.diag` SHA-256 `cf4e22b3e968780d5e303070327330233fc078d75ed403040037f03148909fed`.
- `git-trace.jsonl` SHA-256 `583a9c4457ddbfdf5f5b2d5d9e53210d4510a6bf3a599911255b0d9917f436a8`.
- `console.log` SHA-256 `c32812e4b2821b6cd18ad8214343c6b21017938cd5ca5a8bfa81f4bf56aa94ec`.
- `run.json` SHA-256 `57b3af6f0ed79d08a2532318a9e8ef50115a883ec5146b74aa63e4deb2dbdb84`.

| Test / argument variant | Outcome | Seconds | Evidence line | Native Git processes | Native Git seconds |
|---|---|---:|---|---:|---:|
| C1150_Brief_decision_table_flips_one_condition(canceled-after-attempt, Uncertain, True) | Passed | 0.036161 | run.trx:8 | — | — |
| C1150_Spill_pointer_forms_fail_closed(windows-quoted, unreadable, Unavailable) | Passed | 0.064993 | run.trx:9 | — | — |
| C1150_Brief_decision_table_flips_one_condition(pristine-unattempted, Reuse, False) | Passed | 0.036008 | run.trx:10 | — | — |
| C1150_Brief_decision_table_flips_one_condition(task-closed, Uncertain, False) | Passed | 0.036128 | run.trx:11 | — | — |
| C1150_Spill_pointer_forms_fail_closed(posix, missing, Unavailable) | Passed | 0.065024 | run.trx:12 | — | — |
| C1150_Spill_pointer_forms_fail_closed(windows-quoted, intact, Reuse) | Passed | 0.065076 | run.trx:13 | — | — |
| C1150_Spill_pointer_forms_fail_closed(windows-quoted, missing, Unavailable) | Passed | 0.064463 | run.trx:14 | — | — |
| C1150_Spill_pointer_forms_fail_closed(windows, intact, Reuse) | Passed | 0.064485 | run.trx:15 | — | — |
| C1150_Spill_pointer_forms_fail_closed(windows-quoted, corrupt, Unavailable) | Passed | 0.064402 | run.trx:16 | — | — |
| C1150_Spill_pointer_forms_fail_closed(posix, unreadable, Unavailable) | Passed | 0.064719 | run.trx:17 | — | — |
| C1150_Brief_decision_table_flips_one_condition(neither-attempted-nor-unattempted, Uncertain, True) | Passed | 0.029741 | run.trx:18 | — | — |
| C1150_Spill_pointer_forms_fail_closed(windows, corrupt, Unavailable) | Passed | 0.064527 | run.trx:19 | — | — |
| C1150_Brief_decision_table_flips_one_condition(received-payload-released, Received, False) | Passed | 0.031796 | run.trx:20 | — | — |
| C1150_Brief_decision_table_flips_one_condition(brief-and-other-evidence, Reuse, False) | Passed | 0.032821 | run.trx:21 | — | — |
| C1150_Brief_decision_table_flips_one_condition(no-brief-other-evidence, Uncertain, True) | Passed | 0.032849 | run.trx:22 | — | — |
| C1150_Brief_decision_table_flips_one_condition(received, Received, False) | Passed | 0.032486 | run.trx:23 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(legacy-pointer-corrupt, Unavailable, True) | Passed | 0.029805 | run.trx:24 | — | — |
| C1150_Spill_pointer_forms_fail_closed(posix, corrupt, Unavailable) | Passed | 0.065186 | run.trx:25 | — | — |
| C1150_Spill_pointer_forms_fail_closed(windows, missing, Unavailable) | Passed | 0.064672 | run.trx:26 | — | — |
| C1150_Brief_decision_table_flips_one_condition(spill-unavailable, Unavailable, True) | Passed | 0.036033 | run.trx:27 | — | — |
| C1150_Brief_decision_table_flips_one_condition(two-briefs, Uncertain, True) | Passed | 0.017117 | run.trx:28 | — | — |
| C1150_Brief_decision_table_flips_one_condition(attempted, AttemptOwned, False) | Passed | 0.036310 | run.trx:29 | — | — |
| C1150_Spill_pointer_forms_fail_closed(windows, unreadable, Unavailable) | Passed | 0.065053 | run.trx:30 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(legacy-unc-pointer-missing, Unavailable, True) | Passed | 0.033961 | run.trx:31 | — | — |
| C1150_Spill_pointer_forms_fail_closed(posix, intact, Reuse) | Passed | 0.064522 | run.trx:32 | — | — |
| C1150_Brief_decision_table_flips_one_condition(spill-intact, Reuse, False) | Passed | 0.000967 | run.trx:64 | — | — |
| C1150_Brief_decision_table_flips_one_condition(no-brief, Absent, False) | Passed | 0.000947 | run.trx:65 | — | — |
| C1150_Brief_decision_table_flips_one_condition(inline-attempted-without-payload, AttemptOwned, False) | Passed | 0.001050 | run.trx:66 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(lookalike-pointer-without-length, Reuse, False) | Passed | 0.000968 | run.trx:67 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(genuine-relative-pointer-missing, Unavailable, True) | Passed | 0.000918 | run.trx:68 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(legacy-windows-pointer-missing, Unavailable, True) | Passed | 0.000889 | run.trx:69 | — | — |
| C1150_Brief_decision_table_flips_one_condition(received-canceled, Received, False) | Passed | 0.001060 | run.trx:70 | — | — |
| C1150_Brief_decision_table_flips_one_condition(spill-released-unreceived, Unavailable, True) | Passed | 0.001305 | run.trx:71 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(lookalike-other-marker-headline, Reuse, False) | Passed | 0.000724 | run.trx:72 | — | — |
| C1150_Brief_decision_table_flips_one_condition(attempt-changed, Superseded, False) | Passed | 0.001431 | run.trx:73 | — | — |
| C1150_Brief_decision_table_flips_one_condition(inline-without-goal, Reuse, False) | Passed | 0.000076 | run.trx:74 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(inline-absolute-windows-mention, Reuse, False) | Passed | 0.001091 | run.trx:75 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(legacy-pointer-intact, Reuse, False) | Passed | 0.000204 | run.trx:76 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(lookalike-pointer-without-report-tail, Reuse, False) | Passed | 0.000220 | run.trx:77 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(api-fallback-pointer, Reuse, False) | Passed | 0.001701 | run.trx:78 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(legacy-pointer-attempted-missing, Unavailable, True) | Passed | 0.000205 | run.trx:79 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(lookalike-bare-path-lines, Reuse, False) | Passed | 0.000178 | run.trx:80 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(inline-quoted-path, Reuse, False) | Passed | 0.000191 | run.trx:81 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(lookalike-other-task-pointer, Reuse, False) | Passed | 0.000218 | run.trx:82 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(inline-mention-attempted, AttemptOwned, False) | Passed | 0.000198 | run.trx:83 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(delivery-time-spill-released, AttemptOwned, False) | Passed | 0.002330 | run.trx:84 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(lookalike-joined-other-marker-headline, Reuse, False) | Passed | 0.001767 | run.trx:85 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(legacy-joined-pointer-missing, Unavailable, True) | Passed | 0.003683 | run.trx:86 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(legacy-pointer-missing, Unavailable, True) | Passed | 0.003874 | run.trx:87 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(genuine-joined-pointer-missing, Unavailable, True) | Passed | 0.003423 | run.trx:88 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(dated-receipt-before-dispatch, Unavailable, True) | Passed | 0.002772 | run.trx:89 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(stale-undated-receipt-under-floor, Unavailable, True) | Passed | 0.002230 | run.trx:90 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(dated-receipt-under-floor, Unavailable, True) | Passed | 0.002183 | run.trx:91 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(undated-receipt-proven-sequence, Received, False) | Passed | 0.003884 | run.trx:92 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(inline-mention-received, Received, False) | Passed | 0.003952 | run.trx:93 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(dated-receipt-above-floor, Received, False) | Passed | 0.003569 | run.trx:94 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(lookalike-message-pointer-midbody, Reuse, False) | Passed | 0.024519 | run.trx:95 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(genuine-message-pointer-missing, Unavailable, True) | Passed | 0.026430 | run.trx:96 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(genuine-compact-pointer-missing, Unavailable, True) | Passed | 0.025305 | run.trx:97 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(retry-handoff-own-pointer, Reuse, False) | Passed | 0.031496 | run.trx:98 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(retry-outer-pointer-missing, Unavailable, True) | Passed | 0.031880 | run.trx:99 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(stale-undated-receipt-retry, Unavailable, True) | Passed | 0.032627 | run.trx:100 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(goal-quotes-own-pointer, Reuse, False) | Passed | 0.032437 | run.trx:101 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(retry-handoff-nested-quotes, Reuse, False) | Passed | 0.032437 | run.trx:102 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(retry-handoff-own-pointer-attempted, AttemptOwned, False) | Passed | 0.032895 | run.trx:103 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(retry-handoff-fenced-headline, Reuse, False) | Passed | 0.032466 | run.trx:104 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(inline-absolute-posix-mention, Reuse, False) | Passed | 0.000413 | run.trx:105 | — | — |
| C1150_Resume_input_rule_classifies_every_delivery_result | Passed | 0.068847 | run.trx:106 | — | — |
| C1149_C1150_Statement_budgets(inside-grace-absent-scan, 4) | Passed | 6.163625 | run.trx:107 | — | — |
| C1149_C1150_Statement_budgets(runner-unknown-close-scan, 5) | Passed | 6.702424 | run.trx:108 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(relative-backslash, pending) | Passed | 13.650538 | run.trx:109 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(relative-forward, pending) | Passed | 13.724762 | run.trx:110 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(absolute-native-existing, pending) | Passed | 13.722725 | run.trx:111 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(windows-quoted, pending) | Passed | 13.816454 | run.trx:112 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(other-task-pointer, pending) | Passed | 13.912483 | run.trx:113 | — | — |
| C1150_Local_spill_must_be_present_and_intact(native, unreadable) | Passed | 13.939106 | run.trx:114 | — | — |
| C1150_Local_spill_must_be_present_and_intact(posix, missing) | Passed | 13.990260 | run.trx:115 | — | — |
| C1150_Local_spill_must_be_present_and_intact(native, corrupt) | Passed | 13.990141 | run.trx:116 | — | — |
| C1150_Local_spill_must_be_present_and_intact(windows, missing) | Passed | 14.180773 | run.trx:117 | — | — |
| C1150_Local_spill_must_be_present_and_intact(native, missing) | Passed | 14.292127 | run.trx:118 | — | — |
| C1149_C1150_Statement_budgets(first-dead-session-observation, 18) | Failed | 14.675040 | run.trx:119 | — | — |
| C1149_C1150_Statement_budgets(held-dispatched-tick, 18) | Failed | 14.817714 | run.trx:165 | — | — |
| C1149_C1150_Statement_budgets(working-live-tick, 18) | Failed | 14.799085 | run.trx:211 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(flush-before-send, False) | Passed | 15.202446 | run.trx:257 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(local-request-limit, False) | Passed | 15.311917 | run.trx:258 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(flush-unavailable, False) | Passed | 15.331198 | run.trx:259 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(ensure-before-send, False) | Passed | 15.357666 | run.trx:260 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(ensure-before-send, True) | Passed | 15.347631 | run.trx:261 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(ensure-unavailable, False) | Passed | 15.378019 | run.trx:262 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(flush-request-limit, False) | Passed | 15.358800 | run.trx:263 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(ensure-request-limit, True) | Passed | 15.379464 | run.trx:264 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(local-unavailable, True) | Passed | 15.348844 | run.trx:265 | — | — |
| C1161_Owner_names_the_monotonic_grace | Passed | 0.007706 | run.trx:266 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(flush-before-send, True) | Passed | 15.366753 | run.trx:267 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(flush-unavailable, True) | Passed | 15.368215 | run.trx:268 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(ensure-request-limit, False) | Passed | 15.389379 | run.trx:269 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(inline-relative-mentions, Reuse, False) | Passed | 0.000278 | run.trx:270 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(windows-spill-intact, Reuse, False) | Passed | 0.000251 | run.trx:271 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(flush-request-limit, True) | Passed | 15.378274 | run.trx:272 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(ensure-unavailable, True) | Passed | 15.399495 | run.trx:273 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(local-request-limit, True) | Passed | 15.410395 | run.trx:274 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(windows-quoted, received) | Passed | 15.784031 | run.trx:275 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(relative-forward, working) | Passed | 15.695711 | run.trx:276 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(relative-backslash, received) | Passed | 15.696301 | run.trx:277 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(relative-forward, received) | Passed | 15.748489 | run.trx:278 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(other-task-pointer, working) | Passed | 16.029877 | run.trx:279 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(absolute-native-existing, working) | Passed | 16.001125 | run.trx:280 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(windows-quoted, working) | Passed | 15.991278 | run.trx:281 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(other-task-pointer, received) | Passed | 16.041862 | run.trx:282 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(absolute-native-existing, received) | Passed | 16.290796 | run.trx:283 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(local-unavailable, False) | Passed | 16.315111 | run.trx:284 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(local-before-send, False) | Passed | 16.316069 | run.trx:285 | — | — |
| C1150_Resumed_launch_post_input_bookkeeping_flips_one_step(pre-input-legacy-requeued-event-save) | Passed | 16.356016 | run.trx:286 | — | — |
| C1150_Resumed_launch_post_input_bookkeeping_flips_one_step(healthy) | Passed | 16.357394 | run.trx:287 | — | — |
| C1150_Resumed_launch_post_input_bookkeeping_flips_one_step(flush-input-then-resumed-event-save) | Passed | 16.364359 | run.trx:293 | — | — |
| C1150_Resumed_launch_post_input_bookkeeping_flips_one_step(pre-input-resumed-event-save) | Passed | 16.331102 | run.trx:294 | — | — |
| C1150_Resumed_launch_post_input_bookkeeping_flips_one_step(resumed-event-save) | Passed | 16.365400 | run.trx:295 | — | — |
| C1150_Late_ensure_after_remote_receipt_and_payload_release(clipped) | Passed | 16.492189 | run.trx:296 | — | — |
| C1150_Resumed_backfill_event_save_failure_keeps_the_working_recipient | Passed | 16.367368 | run.trx:297 | — | — |
| C1150_Resumed_launch_post_input_bookkeeping_flips_one_step(requeued-event-save) | Passed | 16.368237 | run.trx:298 | — | — |
| C1150_Late_ensure_after_remote_receipt_and_payload_release(complete) | Passed | 16.498397 | run.trx:299 | — | — |
| C1150_Resumed_launch_post_input_bookkeeping_flips_one_step(flush-after-ensure-input) | Passed | 16.374727 | run.trx:300 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(full-current, local-posix) | Passed | 0.093415 | run.trx:301 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(full-current, local-windows) | Passed | 0.099016 | run.trx:307 | — | — |
| C1150_Inline_brief_mentioning_spill_paths_is_not_held(relative-backslash, working) | Passed | 16.661329 | run.trx:313 | — | — |
| C1149_C1150_Statement_budgets(initial-refused-dispatch, 42) | Failed | 17.586665 | run.trx:314 | — | — |
| C1150_Real_producers_race_to_one_brief_and_keep_the_followup | Passed | 18.884192 | run.trx:385 | — | — |
| C1150_Watchdog_first_recovers_before_failure | Passed | 18.896473 | run.trx:386 | — | — |
| C1149_C1150_Statement_budgets(empty-healthy-scan, 5) | Passed | 19.227351 | run.trx:387 | — | — |
| C1150_Insert_before_flush_cut_reuses_pending_row | Passed | 19.926017 | run.trx:388 | — | — |
| C1149_C1150_Statement_budgets(repeated-blocked-tick, 16) | Failed | 20.110236 | run.trx:389 | — | — |
| C1149_C1150_Statement_budgets(watchdog-first-recovery, 60) | Failed | 21.195558 | run.trx:433 | — | — |
| C1149_C1150_Statement_budgets(running-recovery-missing-brief, 60) | Failed | 21.287695 | run.trx:521 | — | — |
| C1149_C1150_Statement_budgets(repeated-discovery-after-delivery, 6) | Passed | 21.338165 | run.trx:609 | — | — |
| C1149_C1150_Statement_budgets(running-recovery-existing-brief, 65) | Passed | 21.400684 | run.trx:610 | — | — |
| C1150_Discovery_gates_withhold_recovery(owned) | Passed | 21.655384 | run.trx:611 | — | — |
| C1150_Discovery_gates_withhold_recovery(pending-ui) | Passed | 21.846548 | run.trx:612 | — | — |
| C1150_Discovery_gates_withhold_recovery(wrong-generation) | Passed | 22.538815 | run.trx:613 | — | — |
| C1150_Discovery_gates_withhold_recovery(unavailable) | Passed | 22.753729 | run.trx:614 | — | — |
| C1150_Discovery_gates_withhold_recovery(working) | Passed | 23.240710 | run.trx:615 | — | — |
| C1150_Running_cut_is_discovered_and_delivered_once | Passed | 23.321356 | run.trx:616 | — | — |
| C1161_Dead_session_grace_is_monotonic(forward-jump-working, working, 1, 300, 2, Working) | Passed | 23.311632 | run.trx:617 | — | — |
| C1161_Dead_session_grace_is_monotonic(forward-jump-hold, hold, 1, 300, 2, Dispatched) | Passed | 23.312245 | run.trx:618 | — | — |
| C1161_Dead_session_grace_is_monotonic(forward-jump-ordinary, ordinary, 1, 300, 2, Dispatched) | Passed | 23.311535 | run.trx:619 | — | — |
| C1182_Gone_parent_stamps_the_lost_note_once | Passed | 23.694047 | run.trx:620 | — | — |
| C1149_C1150_Statement_budgets(running-recovery-readiness-hold, 12) | Passed | 24.081786 | run.trx:621 | — | — |
| C1161_Dead_session_grace_is_monotonic(rollback-hold, hold, 300, 60, 2, Blocked) | Passed | 24.140660 | run.trx:622 | — | — |
| C1150_Recovery_exhaustion_is_visible_without_replacement | Passed | 24.158578 | run.trx:623 | — | — |
| C1150_Discovery_gates_withhold_recovery(compaction) | Passed | 24.226018 | run.trx:624 | — | — |
| C1150_Followup_survives_both_queue_orders(ui-before-missing-backfill) | Passed | 24.338713 | run.trx:625 | — | — |
| C1150_Followup_survives_both_queue_orders(brief-before-ui) | Passed | 24.338974 | run.trx:626 | — | — |
| C1161_Dead_session_grace_is_monotonic(rollback-ordinary, ordinary, 300, 60, 2, Failed) | Passed | 24.514097 | run.trx:627 | — | — |
| C1150_Readiness_or_persistence_fault_preserves_custody(attach-refused) | Passed | 24.558942 | run.trx:628 | — | — |
| C1149_Watchdog_first_holds_absent_launch | Passed | 24.793655 | run.trx:629 | — | — |
| C1153_Real_client_certificate_holds_original_input(local-http) | Passed | 25.012141 | run.trx:630 | — | — |
| C1150_Readiness_or_persistence_fault_preserves_custody(save-fault) | Passed | 25.103601 | run.trx:631 | — | — |
| C1150_Readiness_or_persistence_fault_preserves_custody(cancel-after-insert) | Passed | 25.317555 | run.trx:632 | — | — |
| C1161_Dead_session_grace_is_monotonic(negative-ordinary, ordinary, -1, 300, 2, Dispatched) | Passed | 11.972684 | run.trx:633 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal(local-before-send, True) | Passed | 19.575414 | run.trx:634 | — | — |
| C1186_Recovered_failure_preserves_obligation_guidance(historical-decoys) | Passed | 26.280911 | run.trx:635 | — | — |
| C1186_Recovered_failure_preserves_obligation_guidance(multiple) | Passed | 26.280401 | run.trx:636 | — | — |
| C1186_Recovered_failure_preserves_obligation_guidance(busy) | Passed | 26.281440 | run.trx:637 | — | — |
| C1161_Dead_session_grace_is_monotonic(inside-ordinary, ordinary, 60, 60, 2, Dispatched) | Passed | 12.585030 | run.trx:638 | — | — |
| C1161_Failed_caller_note_has_one_complete_user_prompt(crash) | Passed | 26.282311 | run.trx:639 | — | — |
| C1161_Failed_caller_note_has_one_complete_user_prompt(busy) | Passed | 20.176091 | run.trx:640 | — | — |
| C1161_Failed_caller_note_has_one_complete_user_prompt(eligible) | Passed | 26.289706 | run.trx:641 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(full-current, local-unc) | Passed | 0.025661 | run.trx:642 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_local_command(local-write-failure, True) | Passed | 12.721035 | run.trx:648 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_local_command(local-write-failure, False) | Passed | 13.096475 | run.trx:649 | — | — |
| C1186_Recovered_failure_preserves_obligation_guidance(one) | Passed | 26.831769 | run.trx:650 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_local_command(local-pre-write-exception, True) | Passed | 13.218444 | run.trx:651 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_local_command(local-pre-write-exception, False) | Passed | 13.304848 | run.trx:652 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_local_command(local-no-snapshot, True) | Passed | 13.779556 | run.trx:653 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_local_command(local-modal, True) | Passed | 14.009101 | run.trx:654 | — | — |
| C1161_Dead_session_grace_is_monotonic(negative-hold, hold, -1, 300, 2, Dispatched) | Passed | 13.138551 | run.trx:655 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_local_command(local-no-snapshot, False) | Passed | 13.720773 | run.trx:656 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_local_command(local-modal, False) | Passed | 14.022964 | run.trx:657 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path(ensure-spill-write-refusal) | Passed | 13.550956 | run.trx:658 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path(may-have-typed-transport-failure) | Passed | 13.657073 | run.trx:659 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path(flush-spill-write-refusal) | Passed | 13.331435 | run.trx:660 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path(flush-courier-missing-spill) | Passed | 13.328897 | run.trx:661 | — | — |
| C1161_Dead_session_grace_is_monotonic(exact-ordinary, ordinary, 180, 180, 2, Failed) | Passed | 13.388300 | run.trx:662 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path(flush-missing-spill) | Passed | 13.519457 | run.trx:663 | — | — |
| C1161_Dead_session_grace_is_monotonic(restart-ordinary, ordinary, 600, 600, 1, Dispatched) | Passed | 13.549520 | run.trx:664 | — | — |
| C1161_Dead_session_grace_is_monotonic(aligned-working, working, 300, 300, 2, Failed) | Passed | 13.772609 | run.trx:665 | — | — |
| C1161_Dead_session_grace_is_monotonic(aligned-hold, hold, 300, 300, 2, Blocked) | Passed | 13.996045 | run.trx:666 | — | — |
| C1161_Dead_session_grace_is_monotonic(aligned-ordinary, ordinary, 300, 300, 2, Failed) | Passed | 14.080254 | run.trx:667 | — | — |
| C1150_Pre_input_refusal_keeps_the_resume_failure_path(ensure-courier-missing-spill) | Passed | 14.603869 | run.trx:668 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(F2-pending-ui) | Passed | 14.667298 | run.trx:669 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(full-current, remote-bound) | Passed | 0.019882 | run.trx:670 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(full-current, remote-staged) | Passed | 0.024819 | run.trx:676 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(full-legacy, local-posix) | Passed | 0.018313 | run.trx:682 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(rules-source-task, Reuse, False) | Passed | 0.000530 | run.trx:688 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(marker-without-identity, Uncertain, True) | Passed | 0.000114 | run.trx:689 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(marker-removed, Uncertain, True) | Passed | 0.000090 | run.trx:690 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(delivery-started, AttemptOwned, False) | Passed | 0.000119 | run.trx:691 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(delivery-verdict, AttemptOwned, False) | Passed | 0.000095 | run.trx:692 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(canceled, Uncertain, True) | Passed | 0.000269 | run.trx:693 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(status-sent, AttemptOwned, False) | Passed | 0.000127 | run.trx:694 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(delivery-attempts, AttemptOwned, False) | Passed | 0.000113 | run.trx:695 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(healthy, Reuse, False) | Passed | 0.000097 | run.trx:696 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(other-session, Absent, False) | Passed | 0.000226 | run.trx:697 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(F2-prior-working) | Passed | 14.872456 | run.trx:698 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(F2-queued-prompt) | Passed | 14.906960 | run.trx:699 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(F1-custody-on-other-session) | Passed | 15.029384 | run.trx:700 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(F1-source-task-delivered) | Passed | 15.271045 | run.trx:701 | — | — |
| C1149_C1150_Working_full_tick_safety(active-working) | Passed | 15.337650 | run.trx:702 | — | — |
| C1149_Unknown_native_evidence_is_not_unattempted(wrong-generation) | Passed | 15.088644 | run.trx:703 | — | — |
| C1149_Unknown_native_evidence_is_not_unattempted(wrong-session) | Passed | 15.342009 | run.trx:704 | — | — |
| C1149_Unknown_native_evidence_is_not_unattempted(sidecar-entry) | Passed | 15.534524 | run.trx:705 | — | — |
| C1149_Unknown_native_evidence_is_not_unattempted(incomplete) | Passed | 15.609970 | run.trx:706 | — | — |
| C1149_Unknown_native_evidence_is_not_unattempted(null) | Passed | 15.538218 | run.trx:707 | — | — |
| C1149_Unknown_native_evidence_is_not_unattempted(unreadable) | Passed | 15.653928 | run.trx:708 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(F2-followup) | Passed | 15.950435 | run.trx:709 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(before-dispatch, Absent, False) | Passed | 0.000507 | run.trx:710 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(marker-only-prompt, Uncertain, True) | Passed | 0.000136 | run.trx:711 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(windows-spill-missing, Unavailable, True) | Passed | 0.000167 | run.trx:712 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(canceled-received, Received, False) | Passed | 0.000162 | run.trx:713 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(local-spill-deleted-received, Received, False) | Passed | 0.000257 | run.trx:714 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(remote-released-clipped-prompt, Unavailable, True) | Passed | 0.000332 | run.trx:715 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(remote-released-unreceived, Unavailable, True) | Passed | 0.000151 | run.trx:716 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(remote-released-received, Received, False) | Passed | 0.000235 | run.trx:717 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(two-unattempted, Uncertain, True) | Passed | 0.000248 | run.trx:718 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(queued-user-prompt, Reuse, False) | Passed | 0.000102 | run.trx:719 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(failed-status, Uncertain, False) | Passed | 0.000235 | run.trx:720 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(complete-user-prompt, Received, False) | Passed | 0.000135 | run.trx:721 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(working-healthy, Reuse, False) | Passed | 0.000208 | run.trx:722 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(correlation-only, Uncertain, True) | Passed | 0.000316 | run.trx:723 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(custody-only, Uncertain, True) | Passed | 0.000272 | run.trx:724 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(started-at-mismatch, Superseded, False) | Passed | 0.000271 | run.trx:725 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(dispatched-at-mismatch, Superseded, False) | Passed | 0.000219 | run.trx:726 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(session-mismatch, Superseded, False) | Passed | 0.000206 | run.trx:727 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(attempt-mismatch, Superseded, False) | Passed | 0.000084 | run.trx:728 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(remote-spill-conflict, Unavailable, True) | Passed | 0.000141 | run.trx:729 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(remote-spill-missing, Unavailable, True) | Passed | 0.000096 | run.trx:730 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(old-session-prompt, Absent, False) | Passed | 0.000092 | run.trx:731 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(origin-ui, Uncertain, True) | Passed | 0.000205 | run.trx:732 | — | — |
| C1150_Brief_evidence_whitelist_flips_one_condition(retried-intact-spill, AttemptOwned, False) | Passed | 0.000123 | run.trx:733 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(F2-control) | Passed | 16.204822 | run.trx:734 | — | — |
| C1149_Native_attempt_keeps_the_failure_path(queued-command) | Passed | 16.241675 | run.trx:735 | — | — |
| C1149_Caller_note_has_one_complete_user_prompt(crash) | Passed | 16.706210 | run.trx:736 | — | — |
| C1149_Native_attempt_keeps_the_failure_path(legacy-file) | Passed | 16.263854 | run.trx:737 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(readonly-current, remote-bound) | Passed | 0.011253 | run.trx:738 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(check-current, local-posix) | Passed | 0.032794 | run.trx:744 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(readonly-legacy, local-windows) | Passed | 0.017604 | run.trx:750 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(compact-joined, remote-bound) | Passed | 0.010978 | run.trx:756 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(compact-joined, remote-staged) | Passed | 0.018152 | run.trx:762 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(readonly-current, local-posix) | Passed | 0.023478 | run.trx:768 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(compact, remote-staged) | Passed | 0.008964 | run.trx:774 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(joined-legacy, local-unc) | Passed | 0.019595 | run.trx:780 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(compact, remote-bound) | Passed | 0.020393 | run.trx:786 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(check-current, remote-bound) | Passed | 0.018776 | run.trx:792 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(joined-legacy, local-windows) | Passed | 0.013963 | run.trx:798 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(joined-current, local-unc) | Passed | 0.012887 | run.trx:804 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(joined-current, remote-bound) | Passed | 0.023984 | run.trx:810 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(joined-current, local-posix) | Passed | 0.017389 | run.trx:816 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(joined-current, local-windows) | Passed | 0.020776 | run.trx:822 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(full-legacy-contract, local-windows) | Passed | 0.013071 | run.trx:828 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(full-legacy-contract, local-posix) | Passed | 0.020335 | run.trx:834 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(full-legacy-contract, local-unc) | Passed | 0.024164 | run.trx:840 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(full-legacy, local-unc) | Passed | 0.017857 | run.trx:846 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(joined-legacy, local-posix) | Passed | 0.016962 | run.trx:852 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(full-legacy, local-windows) | Passed | 0.018717 | run.trx:858 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(header-current, local-posix) | Passed | 0.010413 | run.trx:864 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(message-current, remote-bound) | Passed | 0.011484 | run.trx:870 | — | — |
| C1149_Native_attempt_keeps_the_failure_path(user) | Passed | 16.421551 | run.trx:876 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(lookalike-joined-other-marker-headline, n/a) | Passed | 0.005152 | run.trx:877 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(lookalike-other-marker-headline, n/a) | Passed | 0.001917 | run.trx:883 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(lookalike-without-report-tail, n/a) | Passed | 0.001760 | run.trx:889 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(inline-other-task-pointer, n/a) | Passed | 0.004650 | run.trx:895 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(inline-api-fallback-pointer, n/a) | Passed | 0.004567 | run.trx:901 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(inline-retry-fenced-headline, n/a) | Passed | 0.003441 | run.trx:907 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(inline-retry-nested-handoff, n/a) | Passed | 0.004670 | run.trx:913 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(header-current, remote-bound) | Passed | 0.010102 | run.trx:919 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(inline-retry-quotes-own-legacy-pointer, n/a) | Passed | 0.003819 | run.trx:925 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(inline-goal-quotes-own-legacy-pointer, n/a) | Passed | 0.006664 | run.trx:931 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(inline-goal-is-own-pointer, n/a) | Passed | 0.004142 | run.trx:937 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(inline-goal-quotes-own-pointer, n/a) | Passed | 0.004137 | run.trx:943 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(inline-mentions, n/a) | Passed | 0.006067 | run.trx:949 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(inline-plain, n/a) | Passed | 0.003301 | run.trx:955 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(message-legacy, local-relative) | Passed | 0.007296 | run.trx:961 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(message-legacy, remote-bound) | Passed | 0.004223 | run.trx:967 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(message-joined, remote-bound) | Passed | 0.009425 | run.trx:973 | — | — |
| C1149_Caller_note_has_one_complete_user_prompt(busy) | Passed | 16.961777 | run.trx:979 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(message-current, local-relative) | Passed | 0.009583 | run.trx:980 | — | — |
| C1150_Brief_envelope_corpus_matches_the_expected_matrix(inline-retry-quotes-own-pointer, n/a) | Passed | 0.004028 | run.trx:986 | — | — |
| C1149_Caller_note_has_one_complete_user_prompt(eligible) | Passed | 16.614955 | run.trx:992 | — | — |
| C1149_Native_attempt_keeps_the_failure_path(pending-brief) | Passed | 16.592964 | run.trx:993 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(F2-reply) | Passed | 16.690490 | run.trx:994 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(prior-replied-task) | Passed | 16.767465 | run.trx:995 | — | — |
| C1149_Failed_hold_does_not_persist_on_a_later_save | Passed | 17.405562 | run.trx:996 | — | — |
| C1213_Recovered_brief_keeps_branch_protections_without_git_common(card, feat/card-1213-code-7f3a2b91) | Passed | 17.730136 | run.trx:997 | — | — |
| C1213_Recovered_brief_keeps_branch_protections_without_git_common(legacy, feat/card-task-7f3a2b91) | Passed | 17.801656 | run.trx:998 | — | — |
| C1149_Different_reason_or_attempted_brief_still_uses_failure_policy(attempted-turn) | Passed | 15.012747 | run.trx:999 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(missing-dispatch-time) | Passed | 14.352112 | run.trx:1000 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(note-header-only) | Passed | 13.308922 | run.trx:1001 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(correlation-only) | Passed | 13.369591 | run.trx:1002 | — | — |
| C1151_Ceiling_failure_note_has_one_complete_user_prompt(busy) | Passed | 18.073369 | run.trx:1003 | — | — |
| C1151_Ceiling_failure_note_has_one_complete_user_prompt(crash) | Passed | 18.269750 | run.trx:1004 | — | — |
| C1151_Ceiling_failure_note_has_one_complete_user_prompt(eligible) | Passed | 17.186613 | run.trx:1005 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(submission-start-only) | Passed | 13.378126 | run.trx:1006 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(rules-prompt-only) | Passed | 13.409308 | run.trx:1007 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(verdict-time-only) | Passed | 13.280520 | run.trx:1008 | — | — |
| C1151_Brief_and_spill_remain_byte_identical(pending-ui-followup) | Passed | 16.231633 | run.trx:1009 | — | — |
| C1151_Brief_and_spill_remain_byte_identical(inline) | Passed | 15.025206 | run.trx:1010 | — | — |
| C1151_Brief_and_spill_remain_byte_identical(spilled) | Passed | 16.101966 | run.trx:1011 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(missing-brief) | Passed | 13.250292 | run.trx:1012 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(unknown-transcript-kind) | Passed | 12.642341 | run.trx:1013 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(old-transcript) | Passed | 12.565676 | run.trx:1014 | — | — |
| C1149_Whitelist_gap_keeps_previous_failure(conflicting-task-identity) | Passed | 12.475656 | run.trx:1015 | — | — |
| C1150_Watchdog_cleanup_requires_failure_and_fresh_safe_evidence(working-after-catch-up) | Passed | 35.775620 | run.trx:1016 | — | — |
| C1150_Watchdog_cleanup_requires_failure_and_fresh_safe_evidence(working-before) | Passed | 35.819781 | run.trx:1017 | — | — |
| C1149_Different_reason_or_attempted_brief_still_uses_failure_policy(other-reason) | Passed | 12.530669 | run.trx:1018 | — | — |
| C1149_Different_reason_or_attempted_brief_still_uses_failure_policy(delivery-evidence) | Passed | 12.758907 | run.trx:1019 | — | — |
| C1149_Changed_or_working_attempt_is_untouched(attempt-rebind) | Passed | 12.825707 | run.trx:1020 | — | — |
| C1153_Certificate_age_is_monotonic(ordinary-expiry) | Passed | 13.009791 | run.trx:1021 | — | — |
| C1153_Certificate_age_is_monotonic(fresh) | Passed | 12.743475 | run.trx:1022 | — | — |
| C1150_Watchdog_cleanup_requires_failure_and_fresh_safe_evidence(failure-write-loses) | Passed | 36.477521 | run.trx:1023 | — | — |
| C1153_Certificate_age_is_monotonic(exact-five-seconds) | Passed | 12.556538 | run.trx:1024 | — | — |
| C1153_Certificate_age_is_monotonic(wall-rollback-fresh) | Passed | 12.596224 | run.trx:1025 | — | — |
| C1153_Certificate_age_is_monotonic(forward-jump-fresh) | Passed | 12.683797 | run.trx:1026 | — | — |
| C1153_Certificate_age_is_monotonic(negative-age) | Passed | 12.766794 | run.trx:1027 | — | — |
| C1153_Certificate_age_is_monotonic(rollback-before-request) | Passed | 12.688489 | run.trx:1028 | — | — |
| C1153_Certificate_age_is_monotonic(review-rollback) | Passed | 12.808305 | run.trx:1029 | — | — |
| C1150_Watchdog_cleanup_requires_failure_and_fresh_safe_evidence(unavailable-inventory) | Passed | 37.195489 | run.trx:1030 | — | — |
| C1150_Watchdog_cleanup_requires_failure_and_fresh_safe_evidence(listed-session) | Passed | 37.266267 | run.trx:1031 | — | — |
| C1153_Final_certificate_is_revalidated_under_lock(expired-proof) | Passed | 12.971854 | run.trx:1032 | — | — |
| C1153_Working_or_unknown_inventory_withholds(working-task) | Passed | 12.956518 | run.trx:1033 | — | — |
| C1153_Real_client_bad_evidence_keeps_failure(empty-transcript) | Passed | 12.901261 | run.trx:1034 | — | — |
| C1153_Real_client_bad_evidence_keeps_failure(incomplete) | Passed | 12.737949 | run.trx:1035 | — | — |
| C1153_Real_client_bad_evidence_keeps_failure(mismatched-generation) | Passed | 12.618316 | run.trx:1036 | — | — |
| C1153_Real_client_bad_evidence_keeps_failure(stale-nonce) | Passed | 12.395324 | run.trx:1037 | — | — |
| C1153_Final_certificate_is_revalidated_under_lock(store-or-epoch-change) | Passed | 13.243011 | run.trx:1038 | — | — |
| C1153_Real_client_bad_evidence_keeps_failure(timeout) | Passed | 11.855883 | run.trx:1039 | — | — |
| C1153_Real_client_bad_evidence_keeps_failure(evidence-unreachable) | Passed | 11.887918 | run.trx:1040 | — | — |
| C1153_Real_client_bad_evidence_keeps_failure(old-runner) | Passed | 12.025560 | run.trx:1041 | — | — |
| C1153_Real_client_bad_evidence_keeps_failure(plain404) | Passed | 12.350447 | run.trx:1042 | — | — |
| C1153_Final_certificate_is_revalidated_under_lock(changed-generation) | Passed | 12.738634 | run.trx:1043 | — | — |
| C1153_Real_client_certificate_holds_original_input(remote-phone-home) | Passed | 12.773760 | run.trx:1044 | — | — |
| C1153_Working_or_unknown_inventory_withholds(working-transcript) | Passed | 12.866096 | run.trx:1045 | — | — |
| C1149_Changed_or_working_attempt_is_untouched(working-transcript) | Passed | 12.877806 | run.trx:1046 | — | — |
| C1149_Changed_or_working_attempt_is_untouched(working-task) | Passed | 12.663279 | run.trx:1047 | — | — |
| C1153_Only_new_cold_dispatch_prepares_evidence(cold-fresh) | Passed | 13.283614 | run.trx:1048 | — | — |
| C1149_Listed_or_unknown_runner_is_never_absence(unavailable-inventory) | Passed | 12.826359 | run.trx:1049 | — | — |
| C1149_Listed_or_unknown_runner_is_never_absence(wrong-generation) | Passed | 12.885369 | run.trx:1050 | — | — |
| C1149_Listed_or_unknown_runner_is_never_absence(listed-exited) | Passed | 12.878620 | run.trx:1051 | — | — |
| C1149_Listed_or_unknown_runner_is_never_absence(listed-starting) | Passed | 12.373988 | run.trx:1052 | — | — |
| C1149_Listed_or_unknown_runner_is_never_absence(listed-running) | Passed | 12.426747 | run.trx:1053 | — | — |
| C1153_Working_or_unknown_inventory_withholds(unavailable-owning-inventory) | Passed | 12.798195 | run.trx:1054 | — | — |
| C1149_Hold_is_once_and_automatic_relaunch_bound_is_zero | Passed | 12.956033 | run.trx:1055 | — | — |
| C1153_Due_hold_statement_ceiling(parent, 48) | Passed | 13.016927 | run.trx:1056 | — | — |
| C1149_Absent_launch_is_blocked_with_original_input | Passed | 13.491329 | run.trx:1083 | — | — |
| C1153_Nonpristine_brief_never_closes_identity(native-attempt) | Passed | 13.064035 | run.trx:1084 | — | — |
| C1153_Nonpristine_brief_never_closes_identity(extra-related-row) | Passed | 13.042537 | run.trx:1085 | — | — |
| C1153_Certificate_failure_does_not_leak_a_hold_on_later_save | Passed | 13.425141 | run.trx:1086 | — | — |
| C1153_Nonpristine_brief_never_closes_identity(attempted-delivery) | Passed | 13.130922 | run.trx:1087 | — | — |
| C1153_Only_new_cold_dispatch_prepares_evidence(remote-old-runner) | Passed | 13.102398 | run.trx:1088 | — | — |
| C1153_Only_new_cold_dispatch_prepares_evidence(prepare-faulted) | Passed | 13.697987 | run.trx:1089 | — | — |
| C1153_Only_new_cold_dispatch_prepares_evidence(warm-reuse) | Passed | 13.386072 | run.trx:1090 | — | — |
| C1153_Only_new_cold_dispatch_prepares_evidence(recovery) | Passed | 13.512007 | run.trx:1091 | — | — |
| C1153_Due_hold_statement_ceiling(no-parent, 40) | Passed | 13.058902 | run.trx:1092 | — | — |
| C1150_Old_attempt_evidence_does_not_suppress_current_brief | Passed | 13.005127 | run.trx:1117 | — | — |
| C1150_Uncertain_evidence_cannot_create_a_replacement(marker-only) | Passed | 13.056195 | run.trx:1118 | — | — |
| C1150_Uncertain_evidence_cannot_create_a_replacement(attempted) | Passed | 13.169938 | run.trx:1119 | — | — |
| C1150_Uncertain_evidence_cannot_create_a_replacement(canceled) | Passed | 13.186556 | run.trx:1120 | — | — |
| C1150_Existing_brief_and_spill_are_byte_identical(remote-spill) | Passed | 13.062734 | run.trx:1121 | — | — |
| C1150_Existing_brief_and_spill_are_byte_identical(local-spill) | Passed | 13.173023 | run.trx:1122 | — | — |
| C1150_Existing_brief_and_spill_are_byte_identical(inline) | Passed | 13.263908 | run.trx:1123 | — | — |
| C1150_Outer_envelope_and_receipt_floor_through_the_queue(stale-dated-receipt, Unavailable) | Passed | 13.240230 | run.trx:1124 | — | — |
| C1150_Outer_envelope_and_receipt_floor_through_the_queue(proven-undated-receipt, Received) | Passed | 13.278920 | run.trx:1125 | — | — |
| C1150_Uncertain_evidence_cannot_create_a_replacement(damaged-spill) | Passed | 13.392762 | run.trx:1126 | — | — |
| C1150_Outer_envelope_and_receipt_floor_through_the_queue(stale-undated-receipt, Unavailable) | Passed | 13.415480 | run.trx:1127 | — | — |
| C1150_Concurrent_producers_ensure_one_brief | Passed | 13.983155 | run.trx:1128 | — | — |
| C1150_Outer_envelope_and_receipt_floor_through_the_queue(legacy-intact-payload, Reuse) | Passed | 12.746763 | run.trx:1129 | — | — |
| C1150_Outer_envelope_and_receipt_floor_through_the_queue(retry-handoff-working, AttemptOwned) | Passed | 12.711803 | run.trx:1130 | — | — |
| C1150_Outer_envelope_and_receipt_floor_through_the_queue(legacy-missing-payload, Unavailable) | Passed | 12.656783 | run.trx:1131 | — | — |
| C1150_Outer_envelope_and_receipt_floor_through_the_queue(retry-handoff-pending, Reuse) | Passed | 12.830840 | run.trx:1132 | — | — |
| C1149_C1150_Working_full_tick_safety(aged-prompt-only) | Passed | 13.020272 | run.trx:1133 | — | — |
| C1149_C1150_Statement_budgets(due-absent-hold, 19) | Passed | 45.842566 | run.trx:1134 | — | — |
| C1153_Only_new_cold_dispatch_prepares_evidence(resume) | Passed | 16.983842 | run.trx:1135 | — | — |
| C1150_Working_probe_failure_keeps_the_committed_brief | Passed | 13.516303 | run.trx:1136 | — | — |

## RemoteScriptContractTests

UTC 2026-10-22T22:24:54.666Z to 2026-10-22T22:34:41.859Z; driver wall 587.193 s; exit 124; final diagnostic states {"Passed":29,"Error":18}.

- `diag/log_261022222458256.diag` SHA-256 `e2d0e57ee31946cf153199159a1512680ee05eff89c8f6f0510f8fb4b0a39061`.
- `console.log` SHA-256 `c2d5214c49f6980886e05b566d8ad973aa14b66517587ba7824086e8f7255996`.
- `run.json` SHA-256 `b744ca347436250bb918403b6764b7e6e4e8ccf9271a00716c983b646e85fb89`.

Censored run: 29 passed before the deadline. All 18 Error updates are cancellation outcomes at the deadline; the 457.604062-second active test was interrupted, and the other 17 errors have no timing. None supplies an independent assertion-failure verdict. No TRX was produced.

| Test / argument variant | Outcome | Seconds | Evidence line | Native Git processes | Native Git seconds |
|---|---|---:|---|---:|---:|
| C1105_Jq_file_forms_are_present(--rawfile audit "$CASE_DIR/audit·txt", 1) | Passed | 0.056264 | diag/log_261022222458256.diag:112 | — | — |
| C1105_Jq_file_forms_are_present($saved[0]·stopIntents, 1) | Passed | 0.056148 | diag/log_261022222458256.diag:100 | — | — |
| C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle | Passed | 0.073543 | diag/log_261022222458256.diag:124 | — | — |
| C1105_Jq_file_forms_are_present(( umask 077; : > "$tmp" ), 1) | Passed | 0.056034 | diag/log_261022222458256.diag:106 | — | — |
| C1105_Jq_file_forms_are_present(--slurpfile saved "$CASE_DIR/saved-record·json", 3) | Passed | 0.055932 | diag/log_261022222458256.diag:109 | — | — |
| C1008_Recycle_preserves_tmp_copyup | Passed | 23.228518 | diag/log_261022222458256.diag:133 | — | — |
| C1105_Jq_argv_forms_are_absent(--argjson saved "$C1008_RECORD") | Passed | 0.041239 | diag/log_261022222458256.diag:115 | — | — |
| C1105_Jq_file_forms_are_present($saved[0]·owned, 3) | Passed | 0.055697 | diag/log_261022222458256.diag:121 | — | — |
| C1105_Jq_file_forms_are_present(# CARD-1105-AUDIT-STORE-END, 1) | Passed | 0.056350 | diag/log_261022222458256.diag:103 | — | — |
| C1105_Jq_file_forms_are_present(# CARD-1105-AUDIT-STORE-BEGIN, 1) | Passed | 0.055717 | diag/log_261022222458256.diag:94 | — | — |
| C1105_Jq_file_forms_are_present(c1008_spill_record \|\| c1008_refuse RecycleReceiptUnavailable, 3) | Passed | 0.056086 | diag/log_261022222458256.diag:97 | — | — |
| C1105_Jq_argv_forms_are_absent(--arg audit "$audit") | Passed | 0.052751 | diag/log_261022222458256.diag:118 | — | — |
| C1105_Verify_tmp_accepts_an_absent_pty_host(unreadable, 2, RecycleTmpAssetsMissing) | Passed | 1.680434 | diag/log_261022222458256.diag:139 | — | — |
| C1105_Verify_tmp_accepts_an_absent_pty_host(absent, 0, ) | Passed | 1.075657 | diag/log_261022222458256.diag:145 | — | — |
| C1105_Verify_tmp_accepts_an_absent_pty_host(error, 2, RecycleTmpAssetsMissing) | Passed | 1.970185 | diag/log_261022222458256.diag:151 | — | — |
| C1105_Jq_argv_limit(rawfile, EXIT=0) | Passed | 0.126541 | diag/log_261022222458256.diag:157 | — | — |
| C1105_Verify_tmp_accepts_an_absent_pty_host(dangling, 2, RecycleTmpAssetsMissing) | Passed | 2.448065 | diag/log_261022222458256.diag:167 | — | — |
| C1105_Jq_argv_limit(arg, EXIT=126) | Passed | 0.038878 | diag/log_261022222458256.diag:173 | — | — |
| C1105_Verify_tmp_accepts_an_absent_pty_host(ok, 0, ) | Passed | 2.422730 | diag/log_261022222458256.diag:187 | — | — |
| C1105_Verify_tmp_accepts_an_absent_pty_host(other-uid, 2, RecycleTmpAssetsMissing) | Passed | 2.613349 | diag/log_261022222458256.diag:193 | — | — |
| C1105_Verify_tmp_accepts_an_absent_pty_host(mode, 2, RecycleTmpModeInvalid) | Passed | 2.141923 | diag/log_261022222458256.diag:199 | — | — |
| C1105_Verify_tmp_accepts_an_absent_pty_host(empty, 2, RecycleTmpAssetsMissing) | Passed | 2.745026 | diag/log_261022222458256.diag:205 | — | — |
| C1105_Jq_argv_limit(store, PASS store) | Passed | 0.461147 | diag/log_261022222458256.diag:211 | — | — |
| C1374_Host_census_recovers_transient_reads | Passed | 71.850624 | diag/log_261022222458256.diag:221 | — | — |
| C1105_Verify_tmp_accepts_an_absent_pty_host(file, 2, RecycleTmpAssetsMissing) | Passed | 2.354403 | diag/log_261022222458256.diag:293 | — | — |
| C1105_Verify_tmp_accepts_an_absent_pty_host(symlink, 2, RecycleTmpAssetsMissing) | Passed | 2.769848 | diag/log_261022222458256.diag:299 | — | — |
| C1335_Mirror_owner_stamp_in_linked_admin(review-evidence-linked) | Passed | 1.897619 | diag/log_261022222458256.diag:305 | — | — |
| C1335_Mirror_owner_stamp_in_linked_admin(antiphon-linked) | Passed | 1.420979 | diag/log_261022222458256.diag:315 | — | — |
| C1335_Mirror_owner_stamp_in_linked_admin(unknown-entry) | Passed | 1.478997 | diag/log_261022222458256.diag:325 | — | — |
| C1008_Recycle_resume_requires_matching_receipt | Error | 457.604062 | diag/log_261022222458256.diag:407 | — | — |
| C1335_Mirror_owner_stamp_in_linked_admin(mirror-owner-common) | Error | — | diag/log_261022222458256.diag:422 | — | — |
| C1008_Recycle_receipt_records_disk_and_partial_failure | Error | — | diag/log_261022222458256.diag:433 | — | — |
| C1008_Retire_temp_reclaims_below_cache_disk_gate | Error | — | diag/log_261022222458256.diag:444 | — | — |
| C1105_Resume_post_removal_census_tracks_open_work(verified, 0, ) | Error | — | diag/log_261022222458256.diag:455 | — | — |
| C1105_Resume_post_removal_census_tracks_open_work(missing-phase, 2, RecycleTaskCensusUnknown cause=Unstable) | Error | — | diag/log_261022222458256.diag:466 | — | — |
| C1105_Resume_post_removal_census_tracks_open_work(bad-tasks, 2, RecycleTaskCensusUnknown cause=Unstable) | Error | — | diag/log_261022222458256.diag:477 | — | — |
| C1105_Resume_post_removal_census_tracks_open_work(preflight, 2, RecycleTaskCensusUnknown cause=Unstable) | Error | — | diag/log_261022222458256.diag:488 | — | — |
| C1105_Resume_post_removal_census_tracks_open_work(rebound, 2, RecycleTaskCensusUnknown cause=Unstable) | Error | — | diag/log_261022222458256.diag:499 | — | — |
| C1105_Resume_post_removal_census_tracks_open_work(new-server2, 2, RecycleBoundTasks 44444444-4444-4444-4444-444444444444 status=Blocked runner=server2) | Error | — | diag/log_261022222458256.diag:510 | — | — |
| C1105_Resume_post_removal_census_tracks_open_work(new-temp, 0, ) | Error | — | diag/log_261022222458256.diag:521 | — | — |
| C1105_Resume_post_removal_census_tracks_open_work(new-desktop, 0, ) | Error | — | diag/log_261022222458256.diag:536 | — | — |
| C1105_Resume_post_removal_census_tracks_open_work(terminal, 0, ) | Error | — | diag/log_261022222458256.diag:555 | — | — |
| C1105_Resume_post_removal_census_tracks_open_work(matching, 0, ) | Error | — | diag/log_261022222458256.diag:566 | — | — |
| C1105_Resume_digest_pins_previous_sha12(short-previous, 2) | Error | — | diag/log_261022222458256.diag:577 | — | — |
| C1105_Resume_digest_pins_previous_sha12(missing-previous, 2) | Error | — | diag/log_261022222458256.diag:588 | — | — |
| C1105_Resume_digest_pins_previous_sha12(edited-digest, 2) | Error | — | diag/log_261022222458256.diag:599 | — | — |
| C1105_Resume_state_init_image_follows_the_journal | Error | — | diag/log_261022222458256.diag:798 | — | — |

