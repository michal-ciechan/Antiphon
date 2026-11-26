# CARD-2087 operator status verification

The Code brief specifies the implementation and tests. This manifest binds those selections to checkpoint rows. The full base is e29c634eee5d972daa76461c85465a1ad9e2c51e.

- V-1 verifies the sequential ETA using the exact matched-chunk ratio, scheduled remaining chunks, unknown work, and failure names.
- V-2 verifies missing history and invalid heartbeat remain partial while independent facts survive. The portable Windows-layout fixture uses the real filesystem.
- V-3 verifies typed task handoff, blocked reasons, land and merge, runner seats, read-only Git and HTTP. Exercise the shipped `status` entry point against readable fixtures or the live read endpoints and inspect JSON and text.
- R-1 preserves the existing snapshot and registered caller contract.
- R-2 runs the six-class census and PlanCheckpointClassSelectionGuardTests in the standing guard union.
- R-3 covers the matched `nightly-live-lane` impact row, evidence-diff and merge-tree. The shared invariant is that adding the observer preserves nightly and live-lane execution. CP-2 through CP-4 provisionally budget 2 minutes of execution each apart from the shared cold build.
- R-4 covers the derived instruction-reader classes selected by the `AGENTS.md` pointer edit. The invariant is that existing instruction imports and workspace guidance still resolve. The selector caps the `AGENTS.md` edge, leaving 204 further classes to nightly and rc. The provider workspace canary requires explicit headed or offline-oracle opt-in and stays with its managed gate. CP-5 provisionally budgets 6 minutes of execution and shares the same cold build. No full assembly is in scope.

Mutation candidates are unbound and pending for SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c2087-redwitness/` | red | `/*/*/OperatorStatusTests/Exact_eta_matches_finished_chunks_and_lists_failures_and_unknowns` | V-1 | 1 fails at the ETA assertion before the fix | 1 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c2087-final/` | status | `/*/*/(OperatorStatusTests*)\|(StatusSnapshotScriptTests*)\|(RegisteredCallerScriptTests*)/*` | V-1, V-2, V-3, R-1 | all listed pass | 9 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | S2 | CP-2 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | R-2 | all listed pass | 53 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | S2 | CP-2 | impact | `/*/*/(LiveLaneRunnerTests*)\|(NightlyCoreGoldenTests*)/*` | R-3 | all listed pass | 2 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | S2 | CP-2 | instruction-readers | `/*/*/(AdvisoryChangePolicyTests*)\|(AgentBundleAttachmentTests*)\|(AgentContextCheckTests*)\|(AgentContextContractTests*)\|(AgentSystemPromptLaunchTests*)\|(AgentWorkspaceProvisionerTests*)\|(BootstrapCheckTests*)\|(CheckpointManifestDocumentationTests*)\|(InstructionFileStampTests*)\|(LiveLaneIsolationTests*)\|(OrchestratorInstructionsGuidanceTests*)\|(OrchestratorWorkspaceCommandTests*)\|(OrchestratorWorkspaceLayoutTests*)\|(OrchestratorWorkspaceWarningTests*)\|(PolicyRefreshServiceTests*)\|(PostLandMutationContractTests*)\|(ProjectReadinessTests*)\|(ProjectScriptWorkspaceTests*)\|(ProjectWorkerWorkspaceDefaultTests*)\|(ReleaseImpactScriptTests*)\|(StableBuildOutputDocumentationTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(StandingSessionSelectionTests*)\|(StandingSpecialistSeatTests*)\|(WorkerWorkspaceDefaultMigrationTests*)/*` | R-4 | all listed pass | 25 | 6 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
