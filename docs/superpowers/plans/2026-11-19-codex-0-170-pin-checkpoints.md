# Codex CLI 0.170.0 pin and gpt-6.2-sol ladder

Code owner: task `464ba2aa`. Branch `feat/card-task-464ba2aa`. Worktree `/work/worktrees/ws-09`.
Base: `c83d14d4858a66e41e2c51c4631869db99d0a2c7`.

High and Medium Codex resolve to `gpt-6.2-sol` with floor `0.169.1`. Frontier stays `gpt-6-astra` (documented floor `0.153.4`, null in code). Low stays `gpt-5.6-luna`. `gpt-6.2-astra` is catalogued at the same `0.169.1` floor and is not a rung. `gpt-6.1-sol` stays recognizable at `0.159.1` and leaves the delegatable hold vocabulary. The server2 runner image ARG is `0.170.0` with the registry SHA-512. No image build, deploy, or desktop install is in this slice.

## Verification design

V-1: `CodexGpt62LadderTests` is the ladder, floor, bare-name, and Dockerfile witness. It was red at `820d46aa4fdf311d1b78995489459eb227ee0561`: 4 executed, 4 failed, 0 passed, before the production edit.
V-2: `ModelAliasTests`, `ModelLevelAliasDisplayTests`, `LaunchModelArgumentAppenderTests`, and `RolePolicyReadServiceTests` follow the new rung. `DelegationRetryEventKindTests` shares the display file and stays out of this filter.
V-3: `CodexRunnerImageContractTests` checks the version, the npm integrity, and the install order. The captured `0.160.0` trust-screen byte string stays.
V-4: `CodexCliObservationTests.C959_Ladder_and_exact_models_share_floor` only. The rest of that class keeps its `0.159.1` and `0.160.0` sample banners.
V-5: `CodexDelegateDispatchTests` launched slugs and escalation text.
R-1: The nine-class guard union once. `WindowsUnitPlatformAdmissionTests` is not a row: its Linux skip is not a pass on this runner. C1729 stays VIOLATIONS 0 / LISTED 162. The named classes execute 81. `TaskPlatformGuidanceTests` is 5 cases. `RunnerDefaultGuidanceTests`, 4 cases in the same file, is outside this union.
R-2: `DerivedImpactIndexTests` alone, after the derived-impact driver is built and the index commit is the HEAD under test.
R-3: The touched client ladder files through `scripts/test-client.ps1`. This worktree has no vitest, so that script installs client dependencies once.
R-4: Recompute nightly pins and report them. `scripts/audit-land-verification-graph.ps1 -Check`. `scripts/check-evidence-diff.ps1` over base..HEAD. `git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`. No `-Update` and no merge. Re-pin only if a guard fails.

Updated and not given their own rows, so a later nightly run sees the new slug: `NamedCodexAgentLaunchTests`, `AgentTuiLaunchResolverTests`, the curated array in `AgentTuiProfileServiceTests`, `ComplexityRoutingWalkTests`, the tier-default arm of `PinnedProfileLaunchSpecTests`, the hold literals in `ProviderQuotaRefusalAcceptanceTests`, `ModelAvailabilityTests`, `ModelAvailabilityCreateTests`, the default arm of `CodexCliRemoteDeliveryFixture`, and the launch assertions inside `CodexCliObservationTests`. Left as historical data: probe `FromSpec` strings, the exact High `gpt-6.1-sol` pin, `MigrateStagePinsScriptTests`, `AgentTaskEffectiveModelChipTests`, and the `0.160.0` sample banners.

Manual: the registry tarball was hashed on this runner before the pin. SHA-512 matches the Dockerfile. SHA-1 is `f509396252bb6a7f9cbc795f9b5b77cf0b08ccd8`. `verify-codex-image.sh` counts 45 regular files in that tarball. No docker build, deploy, restart, or desktop npm. Windows is unobserved.

No new positive control. Operator waived mutation execution (2026-11-12). Existing CARD-0959 controls stay pending for SourceLanding Mutation. Restart: none.

## Performance budget

CP-1 EstimatedMinutes 15 is the cold-build allowance. Later TUnit rows reuse that build. Estimates are provisional, not a measured p95. No green repetition. `ANTIPHON_LINUX_COMPILER_POLICY=0`. Admit a row only when the 1-minute load is below 16. The derived-impact driver build is the index commit's own leased build, before CP-7, because `regenerate_script_reports_changed_then_unchanged` passes `-NoBuild`.

The red witness is unlisted: it confirmed the new tests failed on the pre-fix commit.

### Checkpoints

Run through the checkpoint tool with `--expected-source-sha` set to committed HEAD, `--serial`, and no extra build-slot wrap. The tool leases its own slot. Wait until the exit is not 75.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-codex170/ | ladder | `/*/*/CodexGpt62LadderTests/*` | V-1 | 4 pass, 0 failed/skipped | 4 | 15 | true |
| CP-2 | all | CP-1 | alias-display | `/*/*/(ModelAliasTests*)\|(ModelLevelAliasDisplayTests*)\|(LaunchModelArgumentAppenderTests*)\|(RolePolicyReadServiceTests*)/*` | V-2 | all listed, 0 failed/skipped | 115 | 6 | true |
| CP-3 | all | CP-1 | image-pin | `/*/*/CodexRunnerImageContractTests/*` | V-3 | all listed, 0 failed/skipped | 21 | 6 | true |
| CP-4 | all | CP-1 | sol-floor | `/*/*/CodexCliObservationTests/C959_Ladder_and_exact_models_share_floor*` | V-4 | 1 pass, 0 failed/skipped | 1 | 4 | true |
| CP-5 | all | CP-1 | codex-dispatch | `/*/*/CodexDelegateDispatchTests/*` | V-5 | all listed, 0 failed/skipped | 23 | 8 | true |
| CP-6 | all | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-1 | 81 pass, 0 failed/skipped | 81 | 8 | true |
| CP-7 | all | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | R-2 | all listed, 0 failed/skipped | 27 | 8 | true |
| CP-8 | all | n/a | client-ladder | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/delegations/taskVisuals.test.ts src/features/orchestrator/pipelineStageModel.test.ts src/features/delegations/TaskChip.test.tsx src/features/delegations/DelegationsBoard.test.tsx src/features/delegations/DelegationsHistory.test.tsx src/features/orchestrator/ModelAvailabilityPanel.test.tsx` | R-3 | exit 0 | n/a | 8 | true |
