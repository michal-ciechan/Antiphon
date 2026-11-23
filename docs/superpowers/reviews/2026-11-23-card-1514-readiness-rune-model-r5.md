# CARD-1514 readiness prefilter model, Final Review

**Hold landing.** The supplementary-plane repair passes. CP-4 failed before its launch assertion, and the candidate conflicts with current master.

Reviewed `2b22d32e1465a4920d9de82b7958b2285693a247..0ad676b98811edc463ee251fe6a1f4fae595ce3a`. The range changes two test files. The source task is `c20eeb6a-8a18-4bb6-9df5-150cb9983f11`. The original Code landing owner is `6e82eb65-4a0a-42fa-b5ed-390b2719e801`.

## Blockers

1. **CP-4, shared model hold.** `AgentSystemPromptLaunchTests.Interactive_launch_maps_frontier_level_to_fable` threw `ModelDisabledException` before checking the adapter arguments. At 04:34:33 UTC it overlapped `AgentControlServiceIntegrationTests.Start_returns_409_model_disabled_when_the_agent_alias_is_held`. The latter inserts a one-hour session-limit hold for `fable` in the same fixture database. The exception reported an expiry of 05:34:33 UTC. The reader, writer and harness are unchanged across the reviewed range. At the previous SHA the reader passed alone (1/1), and one run with the writer passed (2/2). The base diagnostics did not reproduce the failure. Give the hold-writing fixture and its service harness an isolated database, then verify the two methods together and rerun CP-4. [Hold writer](../../../tests/Antiphon.Tests/Application/AgentControlServiceIntegrationTests.cs) · [Launch assertion](../../../tests/Antiphon.Tests/Application/AgentSystemPromptLaunchTests.cs)
2. **Master integration.** The first merge-tree check passed against `9d2f1cc6d2c8a306514f06ce7b9eb533f1351a10`. At fetched master `e298e6fbb5c9125e3144e02b6f9a8432a229a3bc`, both the previous SHA and the reviewed tip conflict at the end of the rc investigation. Preserve both appended findings, “Resilience linked fixture” and “Project readiness path-matched agent”, on a fresh integration candidate and verify its merge tree. [Conflicting investigation](../../investigations/2026-11-22-rc-run-79d9e6f9-failures.md)

## Verification

The model decodes both strings into scalar values, consumes one stored rune per `_`, and requires full consumption. No other model helper indexes the pattern by UTF-16 unit. Both PostgreSQL fixtures execute the three exact-ID probes. The expected sets contain two Windows U+20000 rows, one Windows U+20001 sibling and two POSIX U+20000 rows.

Code’s original TRXs contain the same 186 named cases. At `47d71784d93fe4a2e106cbbe2ad4b0b68cc07c24`, the model failed four assertions and both database parity assertions failed. All 186 passed at the reviewed SHA. Review CP-2 independently passed the identical 186 cases, including all six new parameter rows. All five Final rows ran in 21m57s, with 804 passes, one failure and 11 roster-listed skips. The SHA-pinned validator returned exit 2, `report_failed`. Code’s earlier green checkpoint was validated when its TRXs were present. Its retained report alone cannot revalidate the skips after those TRXs were retired.

The impacted selector returned zero downgrade cases. Code ran the evidence-diff launcher outside a build slot. Review reran that exact gate under a lease and found two commits, zero entries and zero violations. The checkpoint tool bootstrap and both base diagnostics also used leases. All positive controls remain pending for SourceLanding Mutation.

<details>
<summary>Checkpoint receipts, run 20261123-041744-d303</summary>

```text
source: 0ad676b98811edc463ee251fe6a1f4fae595ce3a state=clean buildSource=verified
CHECKPOINT CP-1 commit=0ad676b98811edc463ee251fe6a1f4fae595ce3a build=ok filter=/*/*/ProjectReadinessTests/agent_counts_a_path_matched_agent_on_another_board executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-041744-d303/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=0ad676b98811edc463ee251fe6a1f4fae595ce3a sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=0ad676b98811edc463ee251fe6a1f4fae595ce3a build=reused filter=/*/*/(ProjectReadinessBatchTests*)|(ProjectReadinessCollationTests*)|(ProjectReadinessTests*)|(ProjectSetupServiceTests*)|(WorkingDirectoryIdentityTests*)/* executed=199 passed=199 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-041744-d303/rows/CP-2/run.trx slot=granted waited=30s dirty=0 source=0ad676b98811edc463ee251fe6a1f4fae595ce3a sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=0ad676b98811edc463ee251fe6a1f4fae595ce3a build=reused filter=/*/*/(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(SlowTestRegistryMergeTests*)|(TestFixtureCleanupGuardTests*)|(SlowTestTripwireTests*)|(TestClassificationPolicyTests*)|(TestLaneCategoryGuardTests*)|(NativeLinuxPolicyWitnessTests*)|(TestClassificationGuardTests*)/* executed=85 passed=85 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-041744-d303/rows/CP-3/run.trx slot=granted waited=135s dirty=0 source=0ad676b98811edc463ee251fe6a1f4fae595ce3a sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=0ad676b98811edc463ee251fe6a1f4fae595ce3a build=reused filter=/*/*/(AgentAttachHerdrTests*)|(AgentControlServiceIntegrationTests*)|(AgentCreateRaceTests*)|(AgentCreateSupervisionTests*)|(AgentRemoteControlGateTests*)|(AgentServiceIntegrationTests*)|(AgentSessionBackendTests*)|(AgentSessionLaunchFailureTests*)|(AgentSessionServiceIntegrationTests*)|(AgentStartRecoveryTests*)|(AgentSupervisionTests*)|(AgentSystemPromptLaunchTests*)|(AgentTaskPoolTests*)|(BoardServiceIntegrationTests*)|(ContextCompactionAgentTests*)|(DiagnosticsBundleServiceTests*)|(HerdrAlwaysOnChannelParityTests*)|(HerdrLabelFollowConcurrencyTests*)|(HerdrPlacementSettingsTests*)|(HerdrSupervisionBackoffTests*)|(PhoneHomeStandingLaunchTests*)|(ProjectCascadeForeignKeyTests*)|(RemoteExecutableLaunchTests*)|(SessionHealthTests*)/* executed=514 passed=513 failed=1 skipped=10 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-041744-d303/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=0ad676b98811edc463ee251fe6a1f4fae595ce3a sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=0ad676b98811edc463ee251fe6a1f4fae595ce3a build=reused filter=/*/*/WindowsUnitPlatformAdmissionTests/* executed=6 passed=6 failed=0 skipped=1 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-041744-d303/rows/CP-5/run.trx slot=granted waited=150s dirty=0 source=0ad676b98811edc463ee251fe6a1f4fae595ce3a sourceState=clean buildSource=verified
BUDGET wall=1317.178s execution=1002.073s admissionOnly=315.105s
verdict: RED exit=1
```

</details>
