# Astra fixes B checkpoints

Code task `7e8fa6a4`, assigned base `d86d0f39422b152faf8c39e362a58b745eb95267`, branch `feat/card-task-7e8fa6a4`. Three inherited nightly-harness defects from Astra audit `a5e3ce5c` (audit commit `0a22441a8cf71b5243d24602803624ac625cc712`): discarded flush failures, journal acknowledgement before publication, and an older loaded core. Audit items F3 (cache mtime) and F5 (discovery JSON) are the other task.

The runner branch is already published at the assigned base. The brief's last-step rebase onto origin/master is refused: this ref is fast-forward-only. Land rebases. `git merge-tree --write-tree origin/master HEAD` is reported and is not a merge.

No PC-n is named. Mutation execution is waived (operator 2026-11-12) and is not discharged. SHORT tests only (operator 2026-11-18). Windows file-sharing denial, Windows DLL load, PowerShell 5.1, and Ctrl+C are UNOBSERVED. The Linux host runs the managed bounded read and the loader directly.

The full `Rc*` / qualification script catalogue is omitted: those classes exceed the ten-minute row cap and belong to rc qualification. `NightlyScriptsTests` is the Linux nightly-script row in this closed list.

## Verification design

- V-1. A failed deferred write is not a complete bounded read. `TestPhaseFiles.BoundedRead` refuses with kind `deferred-write-failed` and leaves the failure in place. `Flush(path)` reports that path only. A later parameterless `Flush()` still reports the other path. `Get-NightlyDeferredEvidenceWriteVerdict` returns `coverageComplete` false, `testsPassed` false, and a `deferred-evidence-write-failed:` reason. `Invoke-AntiphonNightlyTests` calls that verdict before it builds the summary. `Get-TestPhasePublicationQuality` can add the token `deferred-evidence-write-failed`.
- V-2. `Add-TestPhaseExecutedChunk` returns only after the record is on disk. A writer paused inside the deferred publish sees `LastCommitted` still 0. A writer that throws does not acknowledge, does not advance `LastCommitted`, leaves no disk record, and the failure stays visible. `Close-TestPhaseExecutedContext` does not release `writer.lock` when publish fails. Killing the writer after it has entered the publish and before the record exists does not leave an acknowledgement.
- V-3. A process that already has `Antiphon.Nightly.TestPhaseJson` from a DLL that is not the required cache identity throws `nightly-core-identity-mismatch` naming `NightlyCoverageParts` before any chunk helper runs.
- R-1. `NightlyCoreTests` / `scripts/test-nightly-core.ps1` case `CoreMatchesFallback` (38 rows). `WriteBehindFileSystemTests.FailedWriteIsRaisedByTheNextFlushOnly` stays green.
- R-2. Guard union once. Report the executed count. `C1729LayeringGuardTests` stays at 0 violations and 162 listed. `C1210_Windows_linux_only_outcomes_are_exact` skips on Linux.
- R-3. `DerivedImpactIndexTests` alone, after the regenerate driver is built and the script is run twice (changed, then unchanged) when `tests/derived-impact-index.json` changes.
- R-4. `scripts/audit-land-verification-graph.ps1 -Check`. No `-Update`.
- R-5. Recompute nightly pins. `scripts/lib/nightly-core.ps1` is a protected input, so a digest change is resealed in its own commit. `Read-NightlyExecutionPolicy` must agree with the policy file. `scripts/test-live-lane-run.ps1 -PrintInputDigest` must equal `PinnedInputDigest`.
- R-6. `scripts/check-evidence-diff.ps1` over the assigned base..HEAD, and `git merge-tree --write-tree origin/master HEAD` after `git fetch origin master`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-astra-b/ | astra-witnesses | `/*/*/AstraFixesBTests/*` | V-1, V-2, V-3 | 3 executed, 0 failed | 3 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | all | CP-1 | write-behind | `/*/*/WriteBehindFileSystemTests/*` | R-1 | 8 executed, 0 failed | 8 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | all | CP-1 | nightly-core | `/*/*/NightlyCoreTests/*` | R-1 | 1 executed, 0 failed | 1 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | all | CP-1 | nightly-scripts | `/*/*/NightlyScriptsTests/*` | R-1 | 7 executed, 0 failed | 7 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | all | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(DerivedImpactIndexInvariantTests*)\|(WindowsUnitPlatformAdmissionInvariantTests*)/*` | R-2 | executed, 0 failed; C1210 skips on Linux; report the count | 80 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | all | n/a | derived-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-3 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | all | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | R-3 | 27 executed, 0 failed | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | all | n/a | land-audit-build | `dotnet build tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj -c Release -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-4 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | all | n/a | land-graph-check | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Release/net10.0/Antiphon.LandGraphAudit.dll` | R-4 | exit 0, approved | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | all | n/a | nightly-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; $pin = $m.Groups[1].Value; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); if ($d -cne $pin) { throw ("input digest drift computed=" + $d) }'` | R-5 | exit 0, printed digest equals the script pin | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-11 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef d86d0f39422b152faf8c39e362a58b745eb95267 -HeadRef HEAD` | R-6 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-12 | all | n/a | merge-tree | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-6 | exit 0, tree oid printed, no rebase | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 79 minutes (the `EstimatedMinutes` sum). CP-1 is the only `Antiphon.Tests` build. CP-5 includes `DerivedImpactIndexTests` because the guard union names it; CP-7 checks the committed index again after regeneration. Windows behaviour is unobserved and is not a row.
