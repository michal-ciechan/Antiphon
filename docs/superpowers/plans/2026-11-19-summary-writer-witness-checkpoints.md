# CARD-2005 summary writer witness, checkpoints

Code task `77ec2b4f`, assigned base `d57edfc2c54007d293ec3096941ebbb91fc6c9da`, branch `feat/card-task-77ec2b4f`. The production summary writer is already on master (`Write-NightlyJsonFile` in `scripts/lib/nightly-core.ps1`, streamed by `Antiphon.Nightly.NightlyJson.WriteFile`). This task adds one durable witness. It does not add a second writer.

`Write-NightlyRunSummary` in `scripts/lib/nightly-tests-impl.ps1` is the run's existing tail (D-5, `dispatchProgress` with `suitesFromSummary=$true`, the phase-summary try, and `Write-NightlyJsonFile -Depth 8`), called from `Invoke-AntiphonNightlyTests`. The witness calls that same function.

The writer runs under pwsh 7. `scripts/qualification/launch-rc.ps1` re-executes the current pwsh image for `build-slot.ps1` and `nightly-run.ps1`. Windows PowerShell 5.1 does not run this path.

`scripts/lib/nightly-tests-impl.ps1` is not a protected nightly input. Policy hash and `PinnedInputDigest` are recomputed and reported. They are not resealed.

No PC-n is named. Mutation execution is waived (operator 2026-11-12) and is not discharged. The brief's local red proof (duplicate the exclusion lists, watch the witness fail, restore the writer) is unlisted: a checkpoint row cannot keep a reverted mutation. SHORT tests only (operator 2026-11-18).

## Verification design

- V-1. `C2005_SummaryBuildDoesNotConvertTheChunkList`: inside `Write-NightlyRunSummary`, `ConvertTo-Json -InputObject` is only `$firstList` or `$list`, the function writes through `Write-NightlyJsonFile`, and `dispatchProgress` sets `suitesFromSummary=$true` without a second `suites` array. The message on a whole-list convert is `summary build path hands the whole chunk list to ConvertTo-Json`.
- V-2. `C2005_SummaryWriterStreamsWithinABoundedHeap`: a child pwsh 7, with `DOTNET_GCHeapHardLimit=0x30000000` (768 MiB, hexadecimal), calls `Write-NightlyRunSummary` on 1,274 synthetic rows. The file parses, `suites` is 1274, the reader rebuilds 1274 `dispatchProgress` rows, the on-disk progress object has no `suites` property, each exclusion uid appears once, and `coverageComplete`, `runId`, and `liveness` are present. `nightly-report.ps1 -PassThru -DryRun -HttpShim` accepts the file (exit 0, action `none`, title `Nightly green`).
- V-3. Unlisted local mutation, then revert. Not a checkpoint row.
- R-1. `NightlyCoreTests` whole class. Two Linux reds are owned by task `e5626886`. Report them if they are still red. Do not fix them.
- R-2. `TestClassificationPolicyTests` once, inside the guard batch. `C1729LayeringGuardTests` stays at 0 violations and 162 listed.
- R-3. `DerivedImpactIndexTests` alone, after the regenerate driver is built. A new test file that changes `tests/derived-impact-index.json` is regenerated twice (second run unchanged) and committed before this row.
- R-4. `scripts/audit-land-verification-graph.ps1 -Check` approves. No `-Update`.
- R-5. Recompute nightly pins. `Read-NightlyExecutionPolicy` throws when the stated policy hash disagrees. `scripts/test-live-lane-run.ps1 -PrintInputDigest` must equal the `PinnedInputDigest` string in that script.
- R-6. `scripts/check-evidence-diff.ps1` over the assigned base..HEAD, and `git merge-tree --write-tree origin/master HEAD` after `git fetch origin master`, exit 0. The runner branch contract forbids rebasing this branch.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c2005-witness/ | summary-witness | `/*/*/NightlySummaryWitnessTests/*` | V-1, V-2 | 2 executed, 0 failed | 2 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | all | CP-1 | nightly-core | `/*/*/NightlyCoreTests/*` | R-1 | 1 executed; inherited Linux reds from task e5626886 are reported and not fixed | 1 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | all | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(WindowsUnitPlatformAdmissionInvariantTests*)/*` | R-2 | 86 executed; C1210_Windows_linux_only_outcomes_are_exact skips on Linux | 86 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | all | n/a | derived-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-3 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | all | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | R-3 | 27 executed, 0 failed | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | all | n/a | land-audit-build | `dotnet build tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj -c Release -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-4 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | all | n/a | land-graph-check | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Release/net10.0/Antiphon.LandGraphAudit.dll` | R-4 | exit 0, approved | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | all | n/a | nightly-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; $pin = $m.Groups[1].Value; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); if ($d -cne $pin) { throw ("input digest drift computed=" + $d) }'` | R-5 | exit 0, printed digest equals the script pin | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef d57edfc2c54007d293ec3096941ebbb91fc6c9da -HeadRef HEAD` | R-6 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | all | n/a | merge-tree | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-6 | exit 0, tree oid printed | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 66 minutes (the `EstimatedMinutes` sum). CP-1 is the only `Antiphon.Tests` build. CP-2 may be red because of the inherited `NightlyCoreTests` failures; that row is still required and is not a license to edit the core.
