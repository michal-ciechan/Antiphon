# CARD-2049 Astra test gaps

## Verification design

- V-1. Apply the actual requested-alias migration to its predecessor in a fixture-owned PostgreSQL database. Verify column absence before, nullable varchar(80) afterward, the migration history, and the exact Down refusal with preserved data and history.
- V-2. Start a separate historical fixture database at the same predecessor. Save an alias through the fixture interceptor, then read it through a plain context and check that migration history stayed unchanged. Retain the existing transaction-lock regression.
- V-3. Create a Codex High task with the explicit Astra alias, reload it from PostgreSQL, and check the production dispatcher composes one model option with the requested slug and one high-effort override. This path is portable and uses no real CLI.
- V-4. Reproduce the six inherited named Codex failures before gating. On Linux the final run must record six skips with the Windows cmd.exe reason. The class-level Windows platform declaration and runtime gate select the existing AgentControlService → AgentSessionLaunchQueue → AgentSessionService assertions on Windows. Native Windows execution of those six methods remains pending under the operator's Linux-only task placement. No production OS branch, startup setting, or environment input changes.
- R-1. The same-source migration metadata and AppDbContext model classes remain green.
- R-2. The Linux guard union verifies test policy, Slow registration/tripwire, native platform policy, the mechanical positive-control contract, task platform guidance, layering, and lane categorization. The migration timing call selects TestTimingOperationLabelTests and the changed skip roster selects C1210 source closure through the impacted-classes table.
- R-3. DerivedImpactIndexTests verifies the generated class selection. Regenerate after source changes and commit the index last if it changes.
- R-4. The land graph check runs against the final tip. No build definition is changed.
- R-5. Verify the live-lane input digest and recompute the nightly execution policy hash. No protected inputs are in scope.
- R-6. Run check-evidence-diff across the whole task range and check merge-tree against freshly fetched master.

No new migration ships. The task exercises the exact predecessor/upgrade/downgrade path and model checks in CP-2. The full Unit lane remains nightly/rc work. All selections here are bounded by class. The real CLI rollout check belongs to CARD-2046.

Positive-control candidates for method-scoped Mutation are PC-1 omit the shipped alias column in Up, PC-2 skip the interceptor's async column preparation, and PC-3 ignore the explicit alias during dispatch. The Linux named-agent inherited failure is the relevant red-first witness. The migration, fixture interceptor and argv production behavior exist at baseline. No deliberate mutants are run in Code.

## Performance budget

Estimates are provisional for CARD-2049. The prior Astra investigation measured 4m31 for the test assembly build, 12s for migration metadata and 28–43s for the named/dispatch class hosts. The initial and final groups budget 6 and 15 minutes respectively, excluding admission. Existing guard/index estimates use prior Astra checkpoint selections as their reference. Each historical test creates its own predecessor database on the assembly's shared PostgreSQL fixture because it needs to prove the column is absent before its respective operation. Checkpoint receipts retain slot wait, source identity, build and row wall time, and TRX test timing. Test phase timing is enabled for the fixture and host.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | red | `tests/Antiphon.Tests -> bin-card2049-red/` | inherited-named-linux | `/*/*/NamedCodexAgentLaunchTests/*` | V-4 | six inherited cmd.exe failures before the gate | 6 | 6 | true |
| CP-2 | final | `tests/Antiphon.Tests -> bin-card2049-final/` | migration-and-model | `/*/*/(RequestedModelAliasMigrationTests*)\|(RequestedModelAliasDatabaseMigrationTests*)\|(RequestedModelAliasSchemaTests*)\|(AppDbContextModelTests*)/*` | V-1, V-2, R-1 | all listed, 0 failed | 7 | 6 | true |
| CP-3 | final | CP-2 | codex-launch | `/*/*/(NamedCodexAgentLaunchTests*)\|(CodexDelegateDispatchTests*)/*` | V-3, V-4 | all 30 dispatch cases pass and six named cases skip on Linux | 30 | 1 | true |
| CP-4 | final | CP-2 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestTimingOperationLabelTests*)/*` | R-2 | all listed, 0 failed | 83 | 2 | true |
| CP-5 | final | CP-2 | derived-index | `/*/*/DerivedImpactIndexTests/*` | R-3 | all listed, 0 failed | 27 | 2 | true |
| CP-6 | final | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tests/Antiphon.Tests/bin-card2049-final/land-graph-audit/Antiphon.LandGraphAudit.dll` | R-4 | approved, exit 0 | n/a | 1 | true |
| CP-7 | final | n/a | pins | `pwsh -NoProfile -Command '$ErrorActionPreference = "Stop"; . ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-core.ps1; Assert-NightlyCoreAvailable "CARD-2049 pins"; $p = [Antiphon.Nightly.NightlyPolicy]::Read((Get-Location).Path, [string]::Empty); Write-Output ("policyHash=" + $p.Hash); $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), "PinnedInputDigest = .([0-9a-f]{64})."); Write-Output ("liveLaneDigest=" + $d); if (-not $m.Success -or $d -cne $m.Groups[1].Value) { exit 1 }'` | R-5 | recomputed hashes match the checked-in pins | n/a | 1 | true |
| CP-8 | final | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 35a23c90a7ad92cf08f769982459ebe8e87bc9d1 -HeadRef HEAD` | R-6 | exit 0 | n/a | 1 | true |
| CP-9 | final | CP-2 | roster-closure | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates` | R-2 | exact method executes, 0 failed | 1 | 1 | true |
