# Codex 6.2 ladder checkpoints

Code and landing owner `ae8b6243-c56b-4cea-bb8a-383059354890`. Task base `2afaf4bebfb763c12ace57849ed7dd17e5bfe0ca`.
Branch `feat/card-task-ae8b6243`. Final verification on Linux.

- V-1. Codex Frontier resolves to GPT-6.2 Astra at `xhigh` with CLI floor `0.169.1`. High and Medium use GPT-6.2 Sol at their own efforts. Low uses GPT-5.6 Luna. CP-1 first runs with the new assertion against the base implementation.
- V-2. Both Astra slugs remain canonical hold aliases and explicit dispatch choices. A hold on GPT-6.2 Astra refuses ladder Frontier and explicit GPT-6.2 Astra, while ladder High and explicit GPT-6 Astra can still launch.
- V-3. A new Codex Frontier pool launch records its model. Warm reuse admits a session with the current launch slug and fresh-launches a stale or unstamped session. An explicit alias remains recorded and takes the fresh path even when it names the ladder model.
- V-4. Complexity chains, pins, role policy and escalation use the resolved Frontier alias.
- V-5. Section 7 of generated orchestrator instructions lists the four Codex tiers, including GPT-6.2 Sol at High and Medium.
- V-6. Task badges and pipeline rows show the new Frontier label. A recorded GPT-6 Astra retains its historical label.
- R-1. The nine-class Linux guard union passes, including the layering guard. Record conditional skips.
- R-2. Build the derived index driver in its native output through the build-slot gate before the closed run. Run the regenerator twice to check stability and commit an index change last. The separate driver build is necessary because DerivedImpactIndexTests invokes the regenerator with `-NoBuild`.
- R-3. The dispatcher diff selects the `dispatcher-ref-only-composition` and `land-notification` rows from `tests/impacted-classes.json`.
- R-4. Check evidence policy over the complete task base to committed HEAD.
- R-5. Audit the land graph. Compare any mismatch with the base graph and refresh only through the land-refresh script when a changed input requires it.
- R-6. Recompute the nightly policy hash and live-lane input digest. Reseal only a changed protected input.
- R-7. Fetch master and check `git merge-tree --write-tree origin/master HEAD`. This is a Git check outside the checkpoint build and test rows.
- R-8. Run the OS witness inventory lint for the committed diff. New launch decisions depend on provider, tier and recorded slug.

Use one cold Antiphon.Tests build for the exact class and method filters. The invariant is that the same pinned slug is observed by dispatch, holds, reuse, routing and display. Estimated serial execution is 30 minutes or less. Test Database fixtures use disposable containers and clones. The whole Unit lane belongs to nightly and rc. No OS-selected production branch changed. Keep source at committed HEAD and use that SHA with `--expected-source-sha`. Windows qualification and production activation are separate.

PC-1 and PC-2 are listed in the [unbound mutation candidate](../mutations/2026-11-21-codex-ladder-62-candidate.md).
They remain pending for SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-codex62-ladder/` | ladder-witness | `/*/*/CodexGpt62LadderTests/Codex_tiers_resolve_to_the_pinned_model_slugs` | V-1 | 0 failed and every named class or method executed | 1 | 4 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | all | CP-1 | aliases-and-launch-arguments | `/*/*/(CodexGpt62LadderTests)\|(ModelAliasTests)\|(RequestedModelAliasMigrationTests)\|(ModelAvailabilityTests)\|(CodexLaunchArgsTests)\|(LaunchModelArgumentAppenderTests)\|(ModelLevelAliasDisplayTests)\|(DelegationRetryEventKindTests)/*` | V-1, V-2 | 0 failed and every named class or method executed | 8 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | all | CP-1 | dispatch | `/*/*/(CodexDelegateDispatchTests)\|(NamedCodexAgentLaunchTests)\|(AgentTaskPoolTests)\|(CodexCliObservationTests)/*` | V-2, V-3 | 0 failed and every named class or method executed | 4 | 4 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | all | CP-1 | routing | `/*/*/(ComplexityChainRoleTests)\|(ComplexityEffectiveComposeTests)\|(ComplexityEffectiveHttpTests)\|(ComplexityRoutingComposeTests)\|(ComplexityRoutingWalkTests)\|(DefaultRunnerRerouteTests)\|(RoutingPinCandidateCreateTests)\|(RoutingPinCandidateTests)\|(RequiredSingleCandidateRefusalTests)\|(RolePolicyEndpointTests)\|(RolePolicyReadServiceTests)/*` | V-4 | 0 failed and every named class or method executed | 11 | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | all | CP-1 | orchestrator-instructions | `/*/*/(OrchestratorInstructionsRendererTests)\|(OrchestratorInstructionsRefreshTests)/*` | V-5 | 0 failed and every named class or method executed | 2 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | all | CP-1 | linux-guards | `/*/*/(TestClassificationPolicyTests)\|(TestClassificationGuardTests)\|(SlowTestRegistryMergeTests)\|(SlowTestTripwireTests)\|(NativeLinuxPolicyWitnessTests)\|(MechanicalPcContractTests)\|(TaskPlatformGuidanceTests)\|(C1729LayeringGuardTests)\|(TestLaneCategoryGuardTests)/*` | R-1 | 0 failed and every named class or method executed | 9 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-7 | all | CP-1 | derived-index | `/*/*/DerivedImpactIndexTests/*` | R-2 | 0 failed and every named class or method executed | 27 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-8 | all | n/a | client-ladder-labels | `pwsh -NoProfile -File scripts/test-client.ps1 taskVisuals.test.ts pipelineStageModel.test.ts -JsonResultPath ../.antiphon/evidence/codex-ladder-62/client.json` | V-6 | 2 test files green | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-9 | all | CP-1 | dispatcher-ref-only-composition | `/*/*/(PhoneHomeRollingRunnerTests)\|(RefOnlyProductionCompositionTests)\|(DelegationTestServicesTests)\|(DelegationHarnessCensusTests)/*` | R-3 | 0 failed and every named class or method executed | 4 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-10 | all | CP-1 | land-notification | `/*/*/(AgentTaskLandHeldNotificationTests)\|(AgentTaskLandNotificationParkFollowUpTests)\|(AgentTaskLandNotificationPersistenceTests)\|(AgentTaskLandNotificationReconcileLoadTests)\|(AgentTaskLandNotificationRecoveryTests)\|(AgentTaskLandNotificationRescanTests)\|(AgentTaskLandReceiptTests)\|(AgentTaskLandReceiptWatermarkSafetyTests)\|(AgentTaskLandReceiptWatermarkTests)\|(ExpectationDebtTests)\|(LandReceiptScanCacheTests)\|(PreDispatchFailureDeliveryTests)\|(ReceiptScanDestinationIntegrationTests)/*` | R-3 | 0 failed and every named class or method executed | 13 | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-11 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . -BaseRef 2afaf4bebfb763c12ace57849ed7dd17e5bfe0ca -HeadRef HEAD` | R-4 | exit 0 | n/a | 1 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-12 | all | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-5 | exit 0 or investigate inherited graph mismatch | n/a | 1 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-13 | all | n/a | nightly-pins | `pwsh -NoProfile -Command '$ErrorActionPreference = "Stop"; $p = Get-Content -Raw tests/test-execution-policy.json \| ConvertFrom-Json; $h = & pwsh -NoProfile -File scripts/test-live-lane-manifest.ps1 -Case EmitPolicyHash -PolicyRoot (Get-Location).Path; if ($LASTEXITCODE -ne 0 -or $h -cne $p.policyHash) { throw "policyHash expected $($p.policyHash), computed $h" }; $d = & pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest; $pin = (Select-String -Path scripts/test-live-lane-run.ps1 -Pattern "PinnedInputDigest =").Line.Split([char]39)[1]; if ($LASTEXITCODE -ne 0 -or $d -cne $pin) { throw "PinnedInputDigest expected $pin, computed $d" }; Write-Output "policyHash=$h PinnedInputDigest=$d"'` | R-6 | exit 0 and both hashes equal their pins | n/a | 1 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-14 | all | CP-1 | frontier-pool-witness | `/*/*/AgentTaskPoolTests/Frontier_Codex_reuse_requires_the_current_launch_model` | V-3 | 7 passed | 7 | 1 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
