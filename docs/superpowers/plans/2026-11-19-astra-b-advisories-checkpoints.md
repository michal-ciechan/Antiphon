# Astra B advisory checkpoints

Code task `265eefc9`, assigned base `3f24857e8251ba0eb735f4b6dd1f4c963f3cc05d`, branch `feat/card-task-265eefc9`. Advisories A-1 through A-6 from the clean Astra B review `f8a67ac27ea89411b8e3ca5496af2632203375ff`.

The runner branch is fast-forward-only. The brief's last-step rebase onto origin/master is refused. Land rebases. `git merge-tree --write-tree origin/master HEAD` is reported and is not a merge.

No PC-n is named. Mutation execution is waived (operator 2026-11-12) and is not discharged. SHORT tests only (operator 2026-11-18). Windows file-sharing denial, Windows DLL load, PowerShell 5.1, and Ctrl+C during `Flush(FinalPath)` are UNOBSERVED. No OS-selected production branch changed. The Linux host runs the managed bounded read, the lock scope, and the loader directly.

`WindowsUnitPlatformAdmissionTests` is named by the guard union and is not a row: on Linux `C1210_Windows_linux_only_outcomes_are_exact` skips, and a skipped row is not a pass. `DerivedImpactIndexTests` is its own row after the driver build.

## Verification design

- V-1. A complete core (all four required types) loaded from a hash folder other than the build identity throws `nightly-core-identity-mismatch` with `loaded=` that folder's DLL and without `missing=`. The message tells the operator to start a fresh `pwsh`. The incomplete stub still names `NightlyCoverageParts`.
- V-2. `Close-TestPhaseExecutedContext` keeps `writer.lock` when a failed deferred write is under the journal directory. A failure on any other path is retained, the verdict stays failed (`coverageComplete` false, `testsPassed` false, a `deferred-evidence-write-failed:` reason), and the lock is released so a later open in the same process is not `writer-lock-held`.
- V-3. `Get-NightlyDeferredEvidenceWriteVerdict` flushes before it peeks. A write that fails only after the call starts is not reported complete.
- V-4. After `Flush(path)` reports a failure, `PathFailed` stays true and `BoundedRead` still refuses with `deferred-write-failed`. A later successful write of that path does not clear the failed verdict.
- V-5. `Merge-NightlyDeferredEvidenceVerdict` keeps an earlier reason, adds `deferred-evidence-write-failed:`, and returns both flags false. `Get-TestPhasePublicationQuality` adds the token `deferred-evidence-write-failed` when a failure is retained. `Invoke-AntiphonNightlyTests` calls the merge before `summary.json`.
- V-6. Once the core flag is true, a changed build hash re-checks the loaded assembly and throws `nightly-core-identity-mismatch`. The same hash returns true without reloading.
- R-1. `NightlyCoreTests` and `NightlyScriptsTests` stay green. `WriteBehindFileSystemTests` stays green. `FailedWriteIsRaisedByTheNextFlushOnly` is unchanged.
- R-2. Guard union once, without `WindowsUnitPlatformAdmissionTests` and without `DerivedImpactIndexTests`. Report the executed count. `C1729LayeringGuardTests` stays at 0 violations and 162 listed.
- R-3. `DerivedImpactIndexTests` alone, after the regenerate driver is built. When `tests/derived-impact-index.json` changes, regenerate it in its own last commit: driver first, then the script twice (changed, then unchanged).
- R-4. `scripts/audit-land-verification-graph.ps1 -Check`. No `-Update`.
- R-5. Recompute nightly pins. `scripts/lib/nightly-core.ps1` is a protected input, so a digest change is resealed in its own commit. `Read-NightlyExecutionPolicy` must agree with the policy file. `scripts/test-live-lane-run.ps1 -PrintInputDigest` must equal `PinnedInputDigest`.
- R-6. `scripts/check-evidence-diff.ps1` over the assigned base..HEAD, and `git merge-tree --write-tree origin/master HEAD` after `git fetch origin master`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-astra-adv/ | astra-witnesses | `/*/*/AstraFixesBTests/*` | V-1, V-2, V-3, V-4, V-5, V-6 | 5 executed, 0 failed | 5 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | all | CP-1 | write-behind | `/*/*/WriteBehindFileSystemTests/*` | R-1 | 8 executed, 0 failed | 8 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | all | CP-1 | nightly-core | `/*/*/NightlyCoreTests/*` | R-1 | 1 executed, 0 failed | 1 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | all | CP-1 | nightly-scripts | `/*/*/NightlyScriptsTests/*` | R-1 | 7 executed, 0 failed | 7 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | all | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2 | executed, 0 failed; report the count; C1729 violations 0 listed 162 | 80 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | all | n/a | derived-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-3 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | all | CP-1 | derived-index | `/*/*/DerivedImpactIndexTests/*` | R-3 | 27 executed, 0 failed | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | all | n/a | land-audit-build | `dotnet build tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj -c Release -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-4 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | all | n/a | land-graph-check | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Release/net10.0/Antiphon.LandGraphAudit.dll` | R-4 | exit 0, approved | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | all | n/a | nightly-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; $pin = $m.Groups[1].Value; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); if ($d -cne $pin) { throw ("input digest drift computed=" + $d) }'` | R-5 | exit 0, printed digest equals the script pin | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-11 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 3f24857e8251ba0eb735f4b6dd1f4c963f3cc05d -HeadRef HEAD` | R-6 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-12 | all | n/a | merge-tree | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-6 | exit 0, tree oid printed, no rebase | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 79 minutes (the `EstimatedMinutes` sum). CP-1 is the only `Antiphon.Tests` build. Windows behaviour is unobserved and is not a row.
