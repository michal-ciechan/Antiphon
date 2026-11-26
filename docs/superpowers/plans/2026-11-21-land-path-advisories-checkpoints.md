# Land-path advisories checkpoints

The assigned base is `adce1900111e85c0e437e2caf7c2f98b8c9a203f`. The invariant is that a land can publish only with the existing recovery and verification guards satisfied. The receipt identifies the graph and manifest that authorized a docs-only skip. An index-only rebase conflict with an unavailable toolchain produces a named refusal and leaves the target unchanged.

- V-1. Exercise the production regenerator with real Git, an unavailable checkout-selected SDK, and no build-slot gate. Require the named reason in the landing status, conflict event and rebase outcome, a NeedsResolution request, and an unchanged target. Inspect the actual ASP.NET base and the final server Dockerfile stage.
- V-2. Use distinct embedded and target manifests. Assert the whole stored command receipt, including the pinned target and candidate, matched digest and manifest source. Retain the embedded and fail-closed policy witnesses.
- V-3. Check the rollback runbook against the persisted phase and verification-state guards and the operator-facing task detail. Verify that the current state machine accepts the target-manifest skip only on the supported publication path.
- R-1. Run the named land classes. The policy cases live in LandNonCodeSkipTests. DelegationWorktreeTests also covers merge-branch and receipt behavior.
- R-2. Run the nine-class guard union and the matched explicit impacted-classes rows.
- R-3. Run DerivedImpactIndexTests and regenerate the index as the final commit if the bytes differ.
- R-4. Run the graph audit and record any inherited refusal. Recompute the live-lane input pin and nightly policy hash.
- R-5. Check evidence diff over the full range and merge-tree against freshly fetched master.

PC-1 pending for Mutation. Bypass the selected SDK probe or strip the unavailable category when propagating its result. The witness must fail on the missing reason, and the target must stay unchanged. PC-2 pending for Mutation. Substitute the embedded digest or omit the trust field in the target-manifest receipt. The target-manifest receipt witness must fail. V-3 changes the operator procedure only.

## Performance budget

The test scope is bounded by class. The FT-5 Review measured 43 seconds for LandGeneratedArtifactTests. The FT-2 Review measured 2m31s for LandNonCodeSkipTests plus AgentTaskLandingStateTests. The first local red session took 8m30s including load admission, the build and two one-test rows. Use 10 minutes as the provisional cold assembly build cost and 15 minutes for the combined Final test rows before slot and load waits. Keep the existing row and test deadlines. Antiphon.Tests reuses one Final build, and C1677TempFamilyRegistryTests uses its own assembly build. The test database fixture warms once per test process, then individual landing cases clone their database and own their Git fixtures. The checkpoint receipt directory retains TRX, phase timing and build versus admission cost. The whole Unit lane is owned by nightly and rc.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | red | tests/Antiphon.Tests -> bin-cf792-red/ | missing-sdk-red | `/*/*/LandGeneratedArtifactTests/Missing_selected_sdk_names_toolchain_in_conflict_and_status` | V-1 before repair | one assertion failure on missing reason | 1 | 10 | true |
| CP-2 | red | CP-1 | matched-receipt-red | `/*/*/LandNonCodeSkipTests/C_FT2_CurrentTargetManifestSkipsWhenEmbeddedDiffers` | V-2 before repair | one assertion failure on receipt | 1 | 2 | true |
| CP-3 | final | tests/Antiphon.Tests -> bin-cf792-final/ | generated-artifact | `/*/*/LandGeneratedArtifactTests/*` | V-1 and R-1 index-only, mixed conflict, generator failure and clean rebase | all pass | 5 | 10 | true |
| CP-4 | final | CP-3 | policy-and-receipts | `/*/*/(LandNonCodeSkipTests*)|(LandNonCodeSkipInvariantTests*)/*` | V-2 and R-1 matched digest, embedded path and fail-closed policy | all pass | 1 | 5 | true |
| CP-5 | final | CP-3 | delegation-worktree | `/*/*/DelegationWorktreeTests/*` | R-1 worktree landing and recovery receipts | all pass | 1 | 3 | true |
| CP-6 | final | CP-3 | publication-state | `/*/*/AgentTaskLandingStateTests/*` | V-3 verification schema, refs and publication transitions | all pass | 1 | 2 | true |
| CP-7 | final | CP-3 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2 nine guard classes | all nine execute and pass | 81 | 2 | true |
| CP-8 | final | CP-3 | derived-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | R-3 committed index and selector | all pass | 27 | 4 | true |
| CP-9 | final | tests/Antiphon.SessionRunner.Tests -> bin-cf792-registry/ | temp-family-registry | `/*/*/C1677TempFamilyRegistryTests/*` | R-2 explicit temp-family-registry impact | all pass | 1 | 4 | true |
| CP-10 | audit | n/a | land-graph | `export PATH="${ANTIPHON_LAND_CHECKPOINT_PATH:-$PATH}"; dotnet build tools/Antiphon.LandGraphAudit -p:UseAppHost=false --nologo -maxcpucount:"$ANTIPHON_BUILD_SLOT_MAXCPUCOUNT" && pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll` | R-4 graph | exit 0 or record inherited refusal | n/a | 3 | true |
| CP-11 | final | n/a | pins | `PATH="${ANTIPHON_LAND_CHECKPOINT_PATH:-$PATH}" pwsh -NoProfile -Command '$ErrorActionPreference = "Stop"; . ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-core.ps1; Assert-NightlyCoreAvailable "land-path advisory pins"; $p = [Antiphon.Nightly.NightlyPolicy]::Read((Get-Location).Path, [string]::Empty); $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), "PinnedInputDigest = .([0-9a-f]{64})."); Write-Output ("policyHash=" + $p.Hash); Write-Output ("liveLaneDigest=" + $d); if (-not $m.Success -or $d -cne $m.Groups[1].Value) { exit 1 }'` | R-4 pins | both pins match | n/a | 2 | true |
| CP-12 | final | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef adce1900111e85c0e437e2caf7c2f98b8c9a203f -HeadRef HEAD` | R-5 evidence diff | exit 0 | n/a | 1 | true |
| CP-13 | final | n/a | merge-tree | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-5 merge-tree | exit 0 | n/a | 1 | true |
| CP-14 | index | n/a | build-index-driver | `PATH="${ANTIPHON_LAND_CHECKPOINT_PATH:-$PATH}" dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false --nologo -maxcpucount:"$ANTIPHON_BUILD_SLOT_MAXCPUCOUNT"` | R-3 native driver build before regeneration | exit 0 | n/a | 2 | true |
