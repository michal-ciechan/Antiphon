# CARD-2049 replay on master

Base `2f3caabcc83d77b043806ab80a8d3d6ef1d95055`. Source `42900510d3cd040204f82fa12e088dc5fc125e2f`.

## Verification design

- V-1. In a fixture-owned PostgreSQL database, start at the shipped alias migration's predecessor and verify the column is absent. Apply Up and verify a nullable varchar(80) and the exact migration history. Store an Astra alias, request Down, and verify the refusal leaves the stored alias and migration history intact.
- V-2. In a separate historical fixture database, save an alias through the fixture interceptor. Read it through a plain context and verify migration history stays at the predecessor. Retain the existing transaction-lock regression.
- V-3. Keep master's explicit High and Frontier cases in the production dispatcher harness. Reload each created task from PostgreSQL. Require exactly one `--model gpt-6.2-astra` and exactly one effort override with `high` for High and `xhigh` for Frontier. The separate tier-default test still verifies that High defaults to Sol.
- V-4. Run all six named Codex launch tests on Linux using master's existing portable executable path and fake adapter. The cases exercise the queue and service launch wiring without starting a real Codex CLI. This replay changes no OS-selected production behavior.
- R-1. Run the migration metadata and AppDbContext model classes with V-1 and V-2. This replay adds test coverage for an existing migration and does not change the migration or model.
- R-2. Run the bounded policy, mechanical, task platform, layering, lane and timing-label guard union. Compare failures to the assigned master base and rerun red rows. Review the changed-path selection for additional impacted classes.
- R-3. Build the index driver in its native output, then run it twice to verify changed bytes followed by unchanged bytes. Commit the regenerated index last. Run the full DerivedImpactIndexTests class.
- R-4. Run the land graph audit with `-Check`. The base has a known refusal for `SatelliteResourceLanguages` in `Antiphon.ChannelOutbound.Probe.csproj`. Verify the build definitions and audit inputs across the task range.
- R-5. Recompute the live-lane digest and nightly policy hash. Reseal only if a pinned input is changed by this replay.
- R-6. Check the evidence diff across the entire task range and check merge-tree against freshly fetched master.

The alias migration, historical fixture interceptor and explicit dispatcher route already ship at the replay base. The new tests start with a genuinely pre-alias schema for V-1 and V-2. Positive-control candidates stay pending for Mutation: PC-1 omit the shipped column from Up, PC-2 bypass async interceptor column preparation, PC-3 disregard the explicit dispatch alias. PC-3's High case distinguishes the explicit alias from its tier default. The old source's Linux named-launch red predated master's portability fix.

All selections are bounded by class. The Unit lane belongs to nightly and rc qualification. CP-1 builds the test assembly once for the four test rows. Budget about 6 minutes to build and 7 minutes total for the bounded tests, excluding slot and host-load admission, based on the source run. Keep per-test TRX and phase timings. Historical migration tests use separate predecessor databases inside the assembly-owned fixture because each needs to observe column absence.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | final | `tests/Antiphon.Tests -> bin-c2049-replay/` | migration-and-model | `/*/*/(RequestedModelAliasMigrationTests*)\|(RequestedModelAliasDatabaseMigrationTests*)\|(RequestedModelAliasSchemaTests*)\|(AppDbContextModelTests*)/*` | V-1, V-2, R-1 | all listed, zero failed | 7 | 6 | true |
| CP-2 | final | CP-1 | codex-launch | `/*/*/(NamedCodexAgentLaunchTests*)\|(CodexDelegateDispatchTests*)/*` | V-3, V-4 | all listed, including six named cases, zero failed or skipped | 36 | 1 | true |
| CP-3 | final | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestTimingOperationLabelTests*)/*` | R-2 | all listed, zero failed | 83 | 2 | true |
| CP-4 | final | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | R-3 | all listed, zero failed | 27 | 4 | true |
| CP-5 | final | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll` | R-4 | exit 0 or record exact inherited refusal | n/a | 1 | true |
| CP-6 | final | n/a | pins | `pwsh -NoProfile -Command '$ErrorActionPreference = "Stop"; . ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-core.ps1; Assert-NightlyCoreAvailable "CARD-2049 replay pins"; $p = [Antiphon.Nightly.NightlyPolicy]::Read((Get-Location).Path, [string]::Empty); Write-Output ("policyHash=" + $p.Hash); $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), "PinnedInputDigest = .([0-9a-f]{64})."); Write-Output ("liveLaneDigest=" + $d); if (-not $m.Success -or $d -cne $m.Groups[1].Value) { exit 1 }'` | R-5 | both recomputed hashes match pins | n/a | 1 | true |
| CP-7 | final | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 2f3caabcc83d77b043806ab80a8d3d6ef1d95055 -HeadRef HEAD` | R-6 | exit 0 | n/a | 1 | true |
