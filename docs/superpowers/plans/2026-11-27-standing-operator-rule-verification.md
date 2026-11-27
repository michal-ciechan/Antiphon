# Standing operator rule verification

Full task base: 561591c562f31dd4fe0fc8f940f2ea7e947ce965. The Code brief specifies implementation and verification. This manifest binds the red and Final selections.

- V-1: A source-contract witness requires the orchestrator prompt to tell agents to act and record decisions and point to the standing rule.
- V-2: Manually trace the canonical rule, its review-cap and delegate handoffs, the checked-in Claude import, and the configuration writer and readback route.
- R-1: Keep affected prompt contracts and size budgets green, and run the instruction-file contracts.
- R-2: Run the standing Final guards and live-gate companion.
- R-3: Resolve the impacted-class rows, compare evidence to the full task base, and check mergeability.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-2f23-red/` | witness | `/*/*/OrchestratorInstructionsGuidanceTests/The_bundle_tells_orchestrators_to_act_and_record_the_decision` | V-1 | One assertion fails at the missing standing rule | 1 | 4 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-2f23-final/` | prompt-contracts | `/*/*/(AdditiveMigrationRuleWitnessTests*)\|(AgentContextCheckTests*)\|(AgentContextContractTests*)\|(BlockedTaskParkProjectionTests*)\|(ChannelContractsTests*)\|(CheckpointManifestDocumentationTests*)\|(CheckpointRepeatDocumentationTests*)\|(CommitOnSettleDocumentationTests*)\|(D2AdvisoryDocumentationTests*)\|(DelegationReportFormatterTests*)\|(InstructionBundleTests*)\|(InstructionFileStampTests*)\|(InstructionFragmentTests*)\|(KeepSixDocumentationTests*)\|(OrchestratorBundleOwnershipTests*)\|(OrchestratorInstructionsGuidanceTests*)\|(PerTaskFinalUnitLaneWitnessTests*)\|(PostLandMutationContractTests*)\|(PostLandRetrospectiveContractTests*)\|(RepairSourceDocumentationTests*)\|(ReviewRoundsPolicyTests*)\|(ReviewTestQualityChecklistWitnessTests*)\|(RunnerBranchContractDocumentationTests*)\|(RunnerDefaultGuidanceTests*)\|(SequentialOverrideWordingTests*)\|(StableBuildOutputDocumentationTests*)\|(StandingPipelinePolicyDocumentationTests*)/*` | V-1, R-1 | All listed classes execute and pass | 27 | 5 | true |
| CP-3 | S2 | CP-2 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)\|(ProcessSpawnLimitTests*)/*` | R-2, R-3 | All listed classes execute and pass | 55 | 3 | true |
| CP-4 | S2 | CP-2 | live-gates | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | R-2 | Both methods pass | 2 | 1 | true |
| CP-5 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-2f23-pty/` | pty-spawn-census | `/*/*/ProcessSpawnLimitTests/*` | R-3 | Both methods pass | 2 | 2 | true |
| CP-6 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-2f23-runner/` | runner-spawn-census | `/*/*/ProcessSpawnLimitTests/*` | R-3 | Both methods pass | 2 | 2 | true |
| CP-7 | S2 | CP-2 | land-graph-pointer | `/*/*/LandGraphLandRefreshTests/instructions_say_the_land_path_refreshes_and_bare_update_stays_forbidden` | R-1 | The named method passes | 1 | 1 | true |

### Cost

CP-1 and CP-2 each take one cold `Antiphon.Tests` build. CP-3, CP-4 and CP-7 reuse the Final build. The checkpoint hash pin edit matches `spawn-limit-rosters`, which selects a source census in three projects. CP-5 and CP-6 each build their own project and run only the two census methods. The expected cold-build cost is about 14 minutes for `Antiphon.Tests` and a few minutes for each census project. The table estimates test execution on those builds separately from slot waits. The changes affect standing instructions and generated guidance text. They select bounded contract and guard tests with no full-assembly or native Windows run. The curated `test-lane-category` and `test-fixture-cleanup` matches are covered by CP-3.

No positive controls were bound for this task. SourceLanding Mutation owns discovery.
