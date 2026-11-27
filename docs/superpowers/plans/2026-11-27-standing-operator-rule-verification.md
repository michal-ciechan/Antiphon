# Standing operator rule verification

Full task base: 561591c562f31dd4fe0fc8f940f2ea7e947ce965. The Code brief specifies implementation and verification. This manifest binds the red and Final selections.

- V-1: A source-contract witness requires the orchestrator prompt to tell agents to act and record decisions and point to the standing rule.
- V-2: Manually trace the canonical rule, its review-cap and delegate handoffs, the verified single-session stop and runner-wide hold, the checked-in Claude import, and the configuration writer and readback route.
- R-1: Keep affected prompt contracts and size budgets green, and run the instruction-file contracts.
- R-2: Run the standing Final guards and live-gate companion.
- R-3: Resolve the impacted-class rows, compare evidence to the full task base, and check mergeability.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-2f23-red/` | witness | `/*/*/OrchestratorInstructionsGuidanceTests/The_bundle_tells_orchestrators_to_act_and_record_the_decision` | V-1 | One assertion fails at the missing standing rule | 1 | 4 | true |
| CP-2 | S3 | `tests/Antiphon.Tests -> bin-43a985de-candidate/` | prompt-contracts | `/*/*/(AgentContextCheckTests*)\|(AgentContextContractTests*)\|(AgentPresetsTests*)\|(BlockedTaskParkProjectionTests*)\|(ChannelContractsTests*)\|(CheckpointManifestDocumentationTests*)\|(CheckpointRepeatDocumentationTests*)\|(CommitOnSettleDocumentationTests*)\|(D2AdvisoryDocumentationTests*)\|(InstructionBundleTests*)\|(InstructionFileStampTests*)\|(InstructionFragmentTests*)\|(KeepSixDocumentationTests*)\|(OrchestratorBundleOwnershipTests*)\|(OrchestratorInstructionsGuidanceTests*)\|(PerTaskFinalUnitLaneWitnessTests*)\|(PostLandMutationContractTests*)\|(PostLandRetrospectiveContractTests*)\|(RepairSourceDocumentationTests*)\|(ReviewRoundsPolicyTests*)\|(ReviewTestQualityChecklistWitnessTests*)\|(RunnerBranchContractDocumentationTests*)\|(RunnerDefaultGuidanceTests*)\|(SequentialOverrideWordingTests*)\|(StableBuildOutputDocumentationTests*)\|(StandingPipelinePolicyDocumentationTests*)/*` | V-1, R-1 | All listed classes execute and pass | 26 | 5 | true |
| CP-3 | S3 | CP-2 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)\|(ProcessSpawnLimitTests*)/*` | R-2, R-3 | All listed classes execute and pass | 55 | 3 | true |
| CP-4 | S3 | CP-2 | live-gates | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | R-2 | Both methods pass | 2 | 1 | true |
| CP-5 | S3 | `tests/Antiphon.Agents.Pty.Tests -> bin-43a985de-candidate-pty/` | pty-spawn-census | `/*/*/ProcessSpawnLimitTests/*` | R-3 | Both methods pass | 2 | 2 | true |
| CP-6 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-43a985de-candidate-runner/` | runner-spawn-census | `/*/*/ProcessSpawnLimitTests/*` | R-3 | Both methods pass | 2 | 2 | true |
| CP-7 | S3 | CP-2 | land-graph-pointer | `/*/*/LandGraphLandRefreshTests/instructions_say_the_land_path_refreshes_and_bare_update_stays_forbidden` | R-1 | The named method passes | 1 | 1 | true |
| CP-8 | S3 | CP-2 | migration-prompt-pins | `/*/*/AdditiveMigrationRuleWitnessTests/(stage_code_runs_the_downgrade_selector*)\|(stage_review_runs_the_downgrade_selector*)\|(stage_test_design_requires_existing_tests_affected*)\|(orchestrator_skill_requires_the_migration_blast_radius*)` | R-1 | All four methods pass | 4 | 1 | true |
| CP-9 | S3 | CP-2 | report-prompt-contract | `/*/*/DelegationReportFormatterTests/(a_blocked_question_note_puts_reason_asks_authority_and_next_outside_the_excerpt*)\|(a_blocked_note_without_authority_names_reply_not_continue*)\|(the_stage_handoff_contract_is_at_most_700_characters*)\|(the_handoff_block_paragraph_appears_for_every_stage_role*)` | R-1 | All four methods represented and pass | 8 | 1 | true |
| CP-10 | S4 | `tests/Antiphon.Tests -> bin-2ae5-note-red/` | blocked-note-witness | `/*/*/AgentTaskReplyIntegrationTests/Structured_blocked_note_reaches_the_caller_once_with_parking_disabled` | V-3 | Missing durable Blocked obligation | 6 | 3 | true |

### Final rerun

The S2 selection at `2d9ec1c39fec0019bdc41e78624a9de3d77c4fbf` reached CP-2 before the host ran out of disk space. The TRX recorded 409 passes, two failures and a Windows-only skip. S3 restores the overnight Blocked-seat instruction that CP-2 caught. CP-8 and CP-9 now select the migration instructions and report-formatting methods that read the changed prompts. The migration source census and Windows drive-letter path check in those classes cover independent behavior.

The first replay with the session-stop update had five documentation-contract failures in CP-2. The repair locates the single-session procedure with operational autonomy and preserves the pinned restart and report-recovery instructions. The repaired tip runs the same Final selectors in fresh output directories.

The exact `PlanCheckpointClassSelectionGuardTests.Plan_checkpoint_rows_select_declared_test_classes_or_are_retired` method also failed at the full task base. It reported three historical operands for classes deleted by shell port units 65 and 66. The retirement ledger records those exact operands. The prompt repair preserves the existing Review handoff instruction and the shared Mutation recipe.

### Cost

CP-1 and CP-2 each take one cold `Antiphon.Tests` build. CP-3, CP-4 and CP-7 through CP-9 reuse the Final build. The checkpoint hash pin edit matches `spawn-limit-rosters`, which selects a source census in three projects. CP-5 and CP-6 each build their own project and run only the two census methods. The expected cold-build cost is about 14 minutes for `Antiphon.Tests` and a few minutes for each census project. The table estimates test execution on those builds separately from slot waits. The changes affect standing instructions and generated guidance text. They select bounded contract and guard tests with no full-assembly or native Windows run. The curated `test-lane-category` and `test-fixture-cleanup` matches are covered by CP-3.

No positive controls were bound for this task. SourceLanding Mutation owns discovery.
