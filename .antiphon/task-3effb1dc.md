# CARD-1065 S8 Final Review (task 3effb1dc)

Reviewed: Code owner `2a4c9a0b-770d-4393-92a1-d48348c31985`, ref `refs/heads/feat/card-task-2a4c9a0b`,
source `1bed35a353efbbffe77ea4a104057853abf8b8b6`, base `4eb9241807ca89f8ca9ec3b4593a8c08db0e339b`.
Verdict: clean. No regression of existing behaviour and no reachable fail-open in the S8 diff.
Three inherited test failures (red at the base commit too) and three precision gaps are disclosed below
with Backlog cards CARD-1102, CARD-1103, CARD-1104.

## Checkpoint run (one checkpoint-tool run, serial, source-qualified)

Run `.antiphon/checkpoints/20261006-122809-b2cf` (local evidence, ignored), host server2 Linux, one build
`tests/Antiphon.Tests -> bin-c1065-rv8/`, every row `slot=granted waited=0s dirty=0
source=1bed35a353efbbffe77ea4a104057853abf8b8b6 sourceState=clean buildSource=verified`.
`validate --rows CP-8,RV-PARK,RV-SERVICE,RV-GUARD` printed `CHECKPOINT SOURCE VALID source=1bed35a35… rows=4`;
the whole-run validate is `INVALID reason=row_failed` because of the two inherited-red rows.

| Row | Classes | executed | passed | failed | skipped |
|---|---|---:|---:|---:|---:|
| CP-8 | BlockedTaskParkDeliveryTests C1065_* (V-20, V-21, V-22, V-27) | 4 | 4 | 0 | 0 |
| RV-PARK | TerminalRunnerSeatReleaseTests 39, BlockedTaskParkReleaseTests 3, BlockedTaskSyncRecoveryTests 2, TaskParkPublicationTests 3, TaskParkRunnerIdentityTests 2, BlockedTaskParkResumeTests 3 | 52 | 52 | 0 | 0 |
| RV-SERVICE | AgentTaskDetailBlockedContextTests 10, RemotePoolFollowUpAdmissionTests 3, AgentTaskAnswerTests 6, AgentTaskServiceIntegrationTests 120 (whole class) | 139 | 139 | 0 | 0 |
| RV-EVIDENCE | ReviewEvidenceResettlementTests 9, ReviewEvidenceRebindingTests 15, ReviewEvidenceSettlementTests 11, ReviewEvidenceDeliveryTests 17, ReviewEvidenceConsistencyTests 31, ReviewEvidenceParserTests 21, ReviewEvidenceRecoveryTests 21, ReviewEvidenceRecoveryEndpointTests 6, AgentTaskReviewEvidenceTests 16, VerificationRoundSettlementTests 10 | 157 | 156 | 1 | 0 |
| RV-NOTIFY | AgentTaskLandNotificationPersistenceTests, AgentTaskLandNotificationRecoveryTests | 55 | 55 | 0 | 0 |
| RV-LAND-A | AgentTaskLandApprovalPersistenceTests, AgentTaskLandApprovalRecoveryTests, AgentTaskLandApprovalRequestTests, AgentTaskLandContractEndpointTests | 140 | 140 | 0 | 0 |
| RV-LAND-B | AgentTaskLandRequestTests, AgentTaskLandSourceFreshnessTests, AgentTaskLandSourcePersistenceTests, AgentTaskLandTargetRaceTests, InterimVerificationLandGitTests, InterimVerificationLandGuardTests, RepairSourceLandRefusalTests, RepairSourceRecoveryLandingTests | 161 | 159 | 2 | 0 |
| RV-BRIEF | DelegationBriefRecoveryTests, DelegationBriefCeilingPtyTests, VerificationRoundBriefTests, DurableRunnerSpillReceiptTests, InstructionBundleTests, PtyDeliveryCeilingsTests | 97 | 97 | 0 | 6 |
| RV-GUARD | TestClassificationGuardTests, SlowTestTripwireTests (registry guard) | 3 | 3 | 0 | 0 |

The six RV-BRIEF skips are Windows-ConPTY-only methods (DelegationBriefCeilingPtyTests 2, PtyDeliveryCeilingsTests 4),
unrelated to S8. The Unit lane was not run, per the brief ("no whole-Unit").

Failures (all three reproduce unchanged at base `4eb92418`, method-scoped, built through `scripts/build-slot.ps1`
in a detached worktree, so they are inherited, not introduced; CARD-1102):

- `VerificationRoundSettlementTests.C544_SettlementAtomic`: after-commit cut expects 1 StageOutcome, got 0.
- `RepairSourceRecoveryLandingTests.C603_FailedOwnerLandsReviewedRepairedTip`: `RequestAsync(... RecoverReviewedSource: true)`
  throws `ConflictException "Review evidence does not assert a verified clean source."` (CARD-0835 land gate).
- `RepairSourceRecoveryLandingTests.C603_RepairSuccessDoesNotAuthorizeUnreviewedOwnerLand(repair-subject)`: expected
  `review_evidence_subject_mismatch`, got `review_evidence_source_not_clean`.

Unlisted runs, with reasons: checkpoint-tool bootstrap build (plan-sanctioned); `import --plan` (no build/test);
base-commit build plus one method-scoped run of the three failing methods (inherited-red classification).
Evidence guard `check-evidence-diff.ps1 4eb92418..1bed35a3`: commits=6 entries=0 violations=0 (matches Code).
Build outputs `bin-c1065-rv8/`, `bin-c1065-rv8-driver/` and the base worktree were removed.

## Code report against the plan Checkpoints table

CP-8 is the only S8 row; the Code report's line matches the plan's filter and Min (4/4) and I reproduced it.
Code's extra class runs and its one method run carry stated reasons. Code skipped
`ReviewEvidenceResettlementTests` with a reason; it is now run (9/9) together with the other review-evidence,
completion-batch and land-eligibility classes above. S9-S11 rows are out of scope. PCs stay pending.

## Code review (production diff: AgentTaskDispatcher, AgentTaskService, BlockedContextBuilder, DelegationReportFormatter)

- (a) Parking disabled: the dispatcher only adds the prior-transcript link when `ReleasedSeatAnswerReleaseId` names a
  receipted park; `HasConfirmedPublishedParkAsync` requires a publication receipt and a Confirmed `RunnerSeatRelease`;
  the detail builder's new context and `CanAnswer` override are behind that flag; the brief gains lines only with a
  parked SHA/ref. One pre-existing path changes regardless of parking: a follow-up onto a remote pool agent that is
  parked on a Blocked task now hits CARD-1037's 422 `follow_up_remote_pool_unsupported` before the 409
  `follow_up_agent_blocked`. Both refuse before insert; TestDesign PC-170 expects the 422, so this is by design,
  but the message no longer names Reply (CARD-1103).
- (b) Evidence binding for a parked Review goes through the unchanged `ReviewEvidenceBindingService.PrepareRepairAsync`
  (sync Synchronized, `DesktopAfterSha` equal to the claimed SHA, `MirrorDirty == false`, exact-ref observation equal
  to the claim). V-27 drives the missing, wrong-subject and dirty shapes: all outcomes stay unbound, `NextStage` is
  Decide, the caller header has no `reviewed-sha=`/`next=land`. V-21 G-167 shows the old session's TurnEnd cannot
  settle attempt 2.
- (c) V-22: local confirmed park refuses with Reply guidance and no `/cancel`; remote refuses independently; the
  same-task Reply resumes the same AgentId at attempt 2 with the task count unchanged.
- (d) Nothing in the diff touches stop, kill or release paths.
- (e) No assertion or timeout was loosened; changes are additive (fixture parameter, `SeatMirrorPublisher`,
  Slow allowlist entry for the new class; registry guard green).
- The "inbox ceiling"/"delegation setup" note: `RunnerSeatReleaseFixture.CreateAsync(completionSingleWriteBytes)`
  sets `BridgeQueueHarness.Options.Delegation = new DelegationSettings { PtySingleChunkBytes = n }`; ReviewWorld passes
  86,400. Test fixture only. The production default stays 1,024 (`DelegationSettings.cs` untouched). Consequence:
  V-27's caller receipt is proven on the inline path only (CARD-1104).
- `HasConfirmedPublishedParkAsync` is not attempt-scoped and accepts `Resumed`; a stale earlier park can label a later
  re-blocked attempt as released in guidance text and `CanAnswer`. The answer path is independently gated per attempt
  by `FindAttemptReleaseAsync`, so no fail-open (CARD-1103).
- New tests can go red: every scenario asserts concrete counts, codes, SHAs or UserPrompt text with G-labels.
- The brief's `Prior transcript: GET /api/sessions/{id}/transcript?since=0` matches the mapped route.
- `docs/orchestration-loop.md` still documents only the 409 variant; S10 owns the docs.

## Platform

`GET /api/runner-defaults`: globalRunnerId server2. `GET /api/session-runners`: Linux runners available; no `-Runner`
or `-Platform` pin is needed for the land.
