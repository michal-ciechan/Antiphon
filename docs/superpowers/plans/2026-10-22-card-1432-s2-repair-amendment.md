# CARD-1432 S2 repair amendment (Review 9e126982)

Amends `2026-10-22-card-1432-repository-maintenance-plan.md` (the plan) for the S2 repair on
`feat/card-task-1919d384` (Code task 1919d384, descendant of Code task 0a8f49c2 at
`746447cb2a486f0f742e7b19a6ae79ed3ae9babe`). The plan file itself is left as reviewed: master
received it through its own commit, so a branch-side edit of that file is an add/add conflict for
`git merge-tree`. This file carries the amended text and the repair checkpoint rows.

## Decisions

- **D-3 amended (Review 9e126982 D1).** The `update-ref --stdin` batch is a mutating child and runs under the
  `RepositoryChildJournal` like every other `update-ref` (begin before `Process.Start`, started with the
  identity read, exited after both streams drain, not-started when no child was created; D2).
  - **Task not protected** (the §2 rule, evaluated for this one task, after the commit): delete the
    `progress/<task>/` subtree, every observation pin, and each `land/<task>/<operation>/` subtree whose
    operation is superseded. The recovery pins (`source`, `target-before`, `prepared`, `adopt/*`) of the task's
    latest landing operation (its active landing, and the newest `AgentTaskLandings` row) and of every Refused
    landing stay: CARD-0448 V-26 says a land's pins survive cleanup and gc, and the refused-retry suite resumes
    a retry from the Refused operation's pins (`LandRefRetention.RetainedOperationsAsync` and `IsDeletable`).
  - **Task protected**: unchanged, only the observation pins under it go.
  - `AntiphonRefProtection` (D-5) is unchanged: the retention is the retained-operation rule, not a wider
    protected set, so the `landing-refused` protection row stays `false`.
- **S2 tests added or changed.**
  - `LandRefRetentionTests.C1432_completed_land_deletes_task_land_and_progress_refs`: an unprotected task. Its
    request-id, mirror and superseded-operation subtrees and its `progress/<task>/` refs are gone; the landed
    operation's `source`, `target-before` and `prepared` pins stay. Another task's refs and `refs/heads/*` are
    unchanged.
  - `LandRefRetentionTests.C1432_unprotected_task_keeps_latest_and_refused_operation_pins`: rows for a Refused,
    a superseded and a latest landing. The latest and Refused operations keep their recovery pins; the superseded
    operation, a settled request's `adopt/*`, every observation and the progress pins go.
  - `LandingGitTests.C1432_UpdateRefsBatchRunsUnderTheChildJournal` (D2): a `reference-transaction` hook holds
    the batch while a journal record with its process id exists; the record is gone after exit.

## Verification design

| V | Claim | Test |
|---|---|---|
| V-3 (amended) | A completed land leaves an unprotected task only the landed operation's recovery pins; its observation, superseded-operation and `progress/<task>/` refs are gone | `LandRefRetentionTests.C1432_completed_land_deletes_task_land_and_progress_refs` |
| V-9 | An unprotected task keeps the recovery pins of its latest and of every Refused landing operation (CARD-0448 V-26, refused-retry pin rule) | `LandRefRetentionTests.C1432_unprotected_task_keeps_latest_and_refused_operation_pins`, `AgentTaskLandPublicationTests.C448_V26_OriginalAndPreparedPinsSurviveCleanupAndGarbageCollection`, `AgentTaskLandRefusedRetryTests.RR_V5_SettledRefusalIsNotAutomaticallyQueued` |
| V-10 | The `update-ref --stdin` batch runs under a child journal record while it runs and leaves none after | `LandingGitTests.C1432_UpdateRefsBatchRunsUnderTheChildJournal` |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-12 | S2 repair | `tests/Antiphon.Tests -> bin-c1432-r2/` | c1432-r2 | `/*/Antiphon.Tests.Application/LandRefRetentionTests/*` | V-3, V-4, V-5, V-9 | 37 passed, 0 failed/skipped | 37 | 6 |
| CP-13 | S2 repair | CP-12 (-NoBuild) | c1432-r2 | `/*/Antiphon.Tests.Infrastructure/LandingGitTests/*` | V-10, land git regression | 58 passed, 0 failed/skipped | 58 | 8 |
| CP-14 | S2 repair | CP-12 (-NoBuild) | c1432-r2 | `/*/Antiphon.Tests.Application/LandingProtocolHarnessTests/(C475_CoverageAllocationMatchesTheLegacyTupleManifest*)\|(C475_AllServicesUseTheRegisteredGit*)\|(C475_AcknowledgedHookReadsCommittedPhase*)\|(C475_SourceAliasContendsOnRepositoryLease*)\|(C475_CleanupUsesGuardedRemoval*)\|(C475_RecreationPreservesCommittedModeAndAttempts*)\|(C475_ModesAreReachedThroughProtocol*)\|(C475_CleanupRetryRequiresFreshRemoteContainment*)` | land regression; `C475_LandingKeepsLeaseThroughProtocol` (4 cases) is red at base 55b70676 and stays a documented non-receipt exclusion | 14 passed, 0 failed/skipped | 14 | 8 |
| CP-15 | S2 repair | CP-12 (-NoBuild) | c1432-r2 | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C494_*)\|(C448_*)\|(C665_*)\|(C642_*)\|(C688_RebaseAndVerifyRunInTheLandWorktree*)\|(C688_PushPrecedesCanonicalAdvance*)\|(C688_DirtyCanonicalCheckoutLandsWithResidue*)\|(C688_TargetCheckedOutElsewhereIsResidueNotRefusal*)\|(C688_Repair_AlreadyCanonicalRecordsLocalIdentity*)\|(C688_LocalMasterBehindOriginRebasesOntoOrigin*)\|(C688_LocalMasterAheadOfOriginRefuses*)` | V-9 (`C448_V26`, `C448_V09`, `C448_V22`) and the land regression; excluded: `C688_LandNeverRunsGitInTheSourceWorktree` (red at base 55b70676) and `C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance` (its `locked` row self-skips off Windows) | 43 passed, 0 failed/skipped | 40 | 12 |
| CP-16 | S2 repair | CP-12 (-NoBuild) | c1432-r2 | `/*/Antiphon.Tests.Application/AgentTaskLandRefusedRetryTests/(RR_V2_StillDirtyTargetCreatesFreshRefusal*)\|(RR_V4_RemoteContainmentAfterRefusalKeepsAlreadyPresentShortcut*)\|(RR_V5_OriginalPendingRequestCannotReplaceRefusal*)\|(RR_V5_EqualPendingTimestampCannotReplaceRefusal*)\|(RR_V5_SettledRefusalIsNotAutomaticallyQueued*)\|(C688_RealLandProfile*)` | V-9 (the refused-retry pin rule); excluded as red at base 55b70676: `RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation`, `RR_V3_TargetOnlyAdvanceRequiresFreshSelectedVerification`, `RR_V4_RemoteReadFailureRetriesUnchangedSource`, `RR_V4_TargetStatusFailureRetriesFromFreshEvidence` (2), `RR_V6_PreparationFailurePreservesActiveRefusal` (2), `RR_V6_ReplacementSaveFailureRollsBackOldDeactivation` (2) | 6 passed, 0 failed/skipped | 6 | 8 |
| CP-17 | S2 repair | CP-12 (-NoBuild) | c1432-r2 | `/*/Antiphon.Tests.Infrastructure/ServerGitProcessTests/C1432_*` | CP-2's filter (V-1, V-2) on the repair tip | 6 passed, 0 failed/skipped | 4 | 4 |
| CP-18 | S2 repair | CP-12 (-NoBuild) | c1432-r2 | `/*/Antiphon.Tests.Infrastructure/LandingGitProfileTests/*` | CP-8's filter (V-7) on the repair tip | 26 passed, 0 failed/skipped | 9 | 4 |

The seven rows are one checkpoint-tool run with one build, `--rows CP-12,CP-13,CP-14,CP-15,CP-16,CP-17,CP-18`.
The nine cases red at base 55b70676 in the two land classes (one in `AgentTaskLandPublicationTests`, eight in
`AgentTaskLandRefusedRetryTests`, named in the Covers cells) and the four `C475_LandingKeepsLeaseThroughProtocol`
cases are excluded by method so the receipt stays valid. `Min` is a floor; confirm `executed=` in each CHECKPOINT line.

## Follow-ups (file as cards)

- **F-5:** S3's ref sweep applies `LandRefRetention.IsDeletable` with `RetainedOperationsAsync` to each terminal
  task, so the sweep never deletes the latest or a Refused operation's recovery pins (Review 9e126982 D1).
- **F-6:** fold this amendment into the plan file once the plan's own commit and master's agree on one ancestor.
