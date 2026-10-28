# CARD file ignore duplicate project name

Commit `4d11133d55299d6215bcb336604b1e25f0f856c0` on `feat/card-task-efcb77ea`.
Base `68565db92359eadea14180c745c0a28711a36872` (`origin/rc/2026-10-27`, unchanged).

## Diagnosis

`CardFileIgnoreAcceptanceTests.Failed_ignore_install_preserves_created_project_and_original_ignore_bytes` is a two-row theory (`denied` false and true). Both rows called `ProjectService.CreateAsync` with the literal name `c408-install` (line 33). `CardFilePrivacyWorld.Db()` uses the shared `antiphon_test` database (`TestDbFixture.CreateDbContextOptions()` with no isolated schema). `Projects.Name` is unique (`IX_Projects_Name`). `GitProcessSpawnLimit` is 2-4, so the two rows run together. The neighbouring helper `CardFilePrivacyWorld.InitializeAsync` already uses `$"c408-{ProjectId:N}"`.

The losing row throws `DbUpdateException` / Postgres `23505` from `ProjectService.CreateAsync` (`ProjectService.cs` line 136, `AppDbContext.SaveChangesAsync` line 158). Either theory argument can lose. Run 1 at the base failed `denied: False`.

Same class, same defect, also fixed:

- line 45 inserted `c408-other`
- line 63 renamed a project to `c408-target` through `UpdateAsync`

`ScratchGitRepo("c408-target")` is a directory label, not a project name, and was left as-is.

## Counts

Base, class alone, 10 runs: each `11` total, `10` passed, `1` failed, exit 2. `23505` on `IX_Projects_Name` in `10/10` runs.

Base, `/*/Antiphon.Tests.Application/CardFile*/*`: `233` total, `223` passed, `10` failed, `0` skipped. No `23505`. The ignore-install rows passed under that load because the git lane was shared. The 10 failures are not this defect:

- `Git_failures_are_sanitized_preserve_HEAD_and_retry_after_owned_fault_is_removed` (`cancel`, `timeout`, `stderr`)
- `Worktree_change_from_real_post_index_hook_is_detected_after_staging_before_commit`
- `Junction_ancestors_refuse_root_and_managed_file_aliases` (`board`, `cards`, `root`)
- `Reparse_alias_refuses_sync_and_content_mutations_before_pinning_or_touching_outside_bytes` (`board`, `cards`, `root`)

After the fix, class alone, 10 runs: each `12` total, `12` passed, `0` failed, exit 0. `23505` in `0/10`. The extra test is `Parallel_same_base_name_creates_both_preserve_the_project_and_ignore_bytes`.

After the fix, the same CardFile application filter: `234` total, `224` passed, `10` failed, `0` skipped. No `23505`. The same 10 failures as the base run.

`RcRosterRepairTests`: `3` passed, `0` failed.

`C1404_Group10MeasuredBudget`, `C1577_Group71MeasuredBudget`, `C1584_E2EExpandedBudget`, and `C1410_MeasuredCostGroups` pass when `TEMP` is set (`/tmp` on this host). A first attempt with `TEMP` unset failed in `New-C487Root` before any census assertion.

`git merge-tree --write-tree origin/rc/2026-10-27 HEAD` exits 0. The same command against `origin/master` `86d21b50b6b32bc50a1b20f2003e0351a2520710` conflicts in `.antiphon/task-af2b78c8.md` and `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`. Those two add/add conflicts are already present at the rc base.

## Follow-up literal project names

Not changed. `ProjectService.CreateAsync` / `UpdateAsync` with a literal name:

- `tests/Antiphon.Tests/Application/CardFileIgnoreTests.cs:27` `C408 fresh` — shared store, same defect, different class
- `tests/Antiphon.Tests/Application/CardFilePrivacyConcurrencyTests.cs:37` `C408 project` — shared store, one theory row
- `tests/Antiphon.Tests/Application/CardFilePrivacyPathTests.cs:46` `C408_NEW_PROJECT` — shared store; the test expects `unsafe_card_file_path` before the row is saved
- `tests/Antiphon.Tests/Application/CardFileBoardLookupEventTests.cs:77` `New` and `:80` `Renamed` — isolated schema per row

Other `new Project` initializers whose `Name` is a pure literal (some are saved, some are in-memory only):

- `tests/Antiphon.E2E/ContractSnapshotTests.cs:248` Antiphon
- `tests/Antiphon.E2E/ContractSnapshotTests.cs:253` gym-stat
- `tests/Antiphon.E2E/ContractSnapshotTests.cs:663` Home tasks contract
- `tests/Antiphon.E2E/ContractSnapshotTests.cs:766` Pipeline contract
- `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Dispatch.cs:24` C540 owned
- `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Dispatch.cs:80` C508 mismatch
- `tests/Antiphon.E2E/PipelineGlanceE2ETests.cs:106` C557 glance
- `tests/Antiphon.Tests/Application/AgentTaskDecisionQuestionIntegrationTests.cs:189` Decision fixture
- `tests/Antiphon.Tests/Application/AgentTaskLandAdoptionTests.cs:374` reviewed recovery fixture
- `tests/Antiphon.Tests/Application/AgentTaskLandAdoptionTests.cs:501` C788 adoption
- `tests/Antiphon.Tests/Application/AgentTaskLandApprovalRequestTests.cs:385` land approval
- `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusC1329Tests.cs:43` pipeline-1329-foreign
- `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs:1072` pipeline endpoint
- `tests/Antiphon.Tests/Application/AgentTaskWorktreeLockOutcomeTests.cs:170` C443 sibling warning
- `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:125` c0188
- `tests/Antiphon.Tests/Application/BlockedTaskParkReclaimTests.Unloadable.cs:526` c1143-moved
- `tests/Antiphon.Tests/Application/BlockedTaskParkReleaseTests.cs:309` park owner
- `tests/Antiphon.Tests/Application/CardDiagnosisSweepTests.cs:376` Sweep project
- `tests/Antiphon.Tests/Application/CardFilePrivacyMigrationTests.cs:26` C408 synthetic (isolated schema)
- `tests/Antiphon.Tests/Application/CardFilePrivacyPersistenceTests.cs:61` C408 fresh (isolated schema)
- `tests/Antiphon.Tests/Application/CardRankingOrderTests.cs:35` P (in-memory unit)
- `tests/Antiphon.Tests/Application/CardRankingOrderTests.cs:83` P (in-memory unit)
- `tests/Antiphon.Tests/Application/CardWorkTransitionServiceTests.cs:384` Transition project
- `tests/Antiphon.Tests/Application/ChannelOutboundCaptureTests.cs:360` capture-project
- `tests/Antiphon.Tests/Application/ChannelOutboundDiscoveryTests.cs:669` discovery
- `tests/Antiphon.Tests/Application/ChannelOutboundMaterializationTests.cs:332` materialization
- `tests/Antiphon.Tests/Application/ChannelOutboundRetryPolicyTests.cs:402` other
- `tests/Antiphon.Tests/Application/ChannelOutboundTrailingRecoveryTests.cs:312` tails
- `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs:314` c835 recovery
- `tests/Antiphon.Tests/Application/DecisionCardNotifierTests.cs:225` decision-notifier
- `tests/Antiphon.Tests/Application/ExpectationLedgerTests.cs:233` c650
- `tests/Antiphon.Tests/Application/ExpectationSnapshotTests.cs:47` other
- `tests/Antiphon.Tests/Application/ExpectationSnapshotTests.cs:774` c650
- `tests/Antiphon.Tests/Application/ExpectationTestWorld.cs:82` c650
- `tests/Antiphon.Tests/Application/HerdrLabelFollowTests.cs:45` card
- `tests/Antiphon.Tests/Application/LandEvidenceWarningDeliveryTests.cs:126` C788 delivery
- `tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerDrainTests.cs:893` rolling
- `tests/Antiphon.Tests/Application/PipelineHandoffPilotTests.cs:286` foreign project
- `tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.Correction.cs:81` correction scope
- `tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.Correction.cs:101` foreign
- `tests/Antiphon.Tests/Application/PostLandMutationAdmissionTests.cs:198` other-project
- `tests/Antiphon.Tests/Application/PostLandMutationAdmissionTests.cs:284` unrelated
- `tests/Antiphon.Tests/Application/PostLandMutationCustodyTests.cs:806` custody fixture
- `tests/Antiphon.Tests/Application/PostLandMutationWorktreeTests.cs:519` c478-v04
- `tests/Antiphon.Tests/Application/PublishedCardCompletionTests.cs:304` completion
- `tests/Antiphon.Tests/Application/ReviewEvidenceRebindingTests.cs:350` C1043 binding
- `tests/Antiphon.Tests/Application/ReviewEvidenceResettlementTests.cs:179` C1043
- `tests/Antiphon.Tests/Application/RoutingExhaustedPlacementTests.cs:589` C1021
- `tests/Antiphon.Tests/Application/RunnerAlarmCoordinatorTests.cs:350` one (isolated schema; `:355` two, `:360` gone, `:412` one)
- `tests/Antiphon.Tests/Application/ScheduleCardActionTests.cs:370` Schedule card project
- `tests/Antiphon.Tests/Application/SeatDesktopJoinTests.cs:54` c1079-join
- `tests/Antiphon.Tests/Application/SeatDesktopJoinTests.cs:304` c1124-join
- `tests/Antiphon.Tests/Application/SeatDesktopJoinTests.cs:412` c1138-join
- `tests/Antiphon.Tests/Application/SeatOccupancySamplerTests.cs:395` c1138-sampler
- `tests/Antiphon.Tests/Application/StartRefRepairAdoptionTests.cs:163` C675
- `tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs:329` release project
- `tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs:1060` seat ownership project
- `tests/Antiphon.Tests/Application/WorkflowEngineTests.cs:109` Test Project
- `tests/Antiphon.Tests/Application/WorkflowEngineTests.cs:237` P
- `tests/Antiphon.Tests/Application/WorktreeBaseSelectionTests.cs:133` c508
- `tests/Antiphon.Tests/Application/WorktreeBaseSelectionTests.cs:172` c508-missing
- `tests/Antiphon.Tests/Application/WorktreeDefaultCallerTests.cs:476` c644-export
- `tests/Antiphon.Tests/Application/WorktreeDefaultContinuationTests.cs:345` CARD-0644
- `tests/Antiphon.Tests/Application/WorktreeRetirementRaceTests.cs:1267` c459
- `tests/Antiphon.Tests/TestHelpers/BlockedTaskParkFixture.cs:43` park-state
- `tests/Antiphon.Tests/TestHelpers/LandHalfResetFixture.cs:92` C939 fixture
