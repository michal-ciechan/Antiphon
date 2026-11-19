# Nightly core port 1: discovery UID index

Code task `aef78bf4`, base `8cca875d8c26b8590eeda6769c5db67fa8cf1a02`, branch `feat/card-task-aef78bf4`. The port reads per-chunk required UIDs from the discovery index instead of walking every discovery node. Mutation is waived (operator 2026-11-12). Windows behaviour of this path is UNOBSERVED; the orchestrator runs the desktop dry replay.

## Measurement

Replay: `scripts/fixtures/nightly/c1410-antiphon-listing.txt` expanded to 19,012 nodes and 150 chunks (`Resolve-NightlySuiteChunks`). Warm figures drop the first two chunks. Drive-letter path checks are overridden with `File.Exists` / `Directory.Exists` so the Linux `Test-Path` penalty is not in the profile. Deferred evidence writes are off. 26 of 150 chunks stay red: oversized `all-split-*` rows whose `RequiredClasses` is the whole class. Those reds are the same before and after.

Windows-comparable warm milliseconds per chunk, before commit `b6e49221e`:

| Step | ms/chunk | Where |
|---|---:|---|
| suite verdict | 26.66 | `ConvertTo-NightlyNativeSuiteVerdict` |
| executed manifest | 105.9 | Linux `Test-Path` on the journal directory; see skipped |
| timing terminal | 10.52 | per-chunk timing line |
| active identity | 4.42 | active-chunk identity |
| active identity idle | 4.02 | idle identity |
| timing start | 2.92 | timing start |

Warm per chunk 154.45 ms. Times 150 chunks is 23.17 s. Times 1,274 Windows Full chunks is 196.8 s. End of that replay, once: run summary 3148.7 ms, timing report 666.7 ms (150 rows), TRX `[xml]` export 174.3 ms, `Read-NightlyTrxIdentities` 577.5 ms.

The same replay after `b6e49221e`: suite verdict 18.49 ms (delta −8.17 ms/chunk; times 1,274 is about 10.4 s). Warm per chunk 134.48 ms; times 150 is 20.17 s; times 1,274 is 171.3 s. Timing terminal 10.05, active identity 4.36, idle 3.94, timing start 2.42. Executed manifest stayed 95.22 ms. Run summary 2675.2 ms and timing report 900.3 ms are the same writers and are treated as Linux noise, not a result of this port. Export XML 172.8 ms, identities 502.8 ms.

Inside the 26.66 ms verdict, the UID walk was the largest pure piece (about 17 ms on a green 73-row chunk) because `IndexRequiredUids` enumerated all 19,012 nodes. The `@()` copy of `Document.Nodes` was about 2.5–5 ms. The 8.17 ms drop is those two changes together.

## What was ported

`DiscoveryUidIndex` is built once in `NightlyDiscovery.IndexView` and `Get-NightlyDiscoveryIndexRequiredUids` calls `RequiredUidsFor` when the view carries it. `NightlyVerdicts.IndexRequiredUids` stays for a hand-built view with no index. A missing core still throws `nightly-core-required`. `ConvertTo-NightlyNativeSuiteVerdict` keeps an array node list as-is, and `nightly-tests-impl.ps1` copies discovery nodes into the suite only on the first chunk. The loader path in `nightly-core.ps1` is unchanged. The desktop DLL is still built into the temp cache by `Install-NightlyCoreBuild`.

## Skipped

- Linux `Test-Path` and `Test-NightlyDrive*` / `Test-TestPhasePathExists`. Windows drive-letter checks already landed in `55f52506c` and `f115f2b19`. The ~165 ms `Test-Path -PathType Leaf` on a directory is Linux-only. The 95 ms manifest figure is that cost and is not a port target.
- `Read-NightlyTrxIdentities` `[xml]` load. It stays in PowerShell for Windows PowerShell 5.1. A 4,707-byte TRX was 7.5 ms in PowerShell and 9.8 ms with `XmlDocument.Load`.
- Path checks, class-cost, suite completion, diag parse, summary json/csv, active-chunk JSON, deferred writes, planned manifest, census, launch packer, prior-failure index, expected-skip scans, and journal read. Those landed in the operator series through `55f52506c`.
- Journal envelope write and `Write-TestPhaseCommitFile`: filesystem mutation. The JSON body is about 2 ms.
- `scripts/nightly-report.ps1`: HTTP.
- `Export-NightlyPriorFailures`: side effects. End-of-run XML plus identities was about 0.75 s on 150 files.
- `Test-NightlyExpandedRowsPresent` node-array bind: 0.37 ms and the parameter is unused by the compiled check.

## Pins

Protected inputs include `NightlyCore.cs` and `nightly-coverage.ps1`. Reseal commit `0e1e06c8e98d8d431b95a0c2aa79e9d4685adbe2` sets `PinnedInputDigest` to `9f57e5650d9e79c660989c466d97bd5693523ddd69338105d4e8cbde82c65cb4`. `policyHash` recomputed equal to the file: `0396d6a543917cd112d2f28dd0adfe8bd80ae549f2803df2bb866a751b261542`. `LegacyExclusionDigest` stays `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a` (asserted by `NativeLinuxPolicyWitnessTests`). The derived-impact builder rewrote 14 lines (`115216c03cd8c11ace2a3a307f980b5a59a87c9a`); the second run printed unchanged.

## Inherited Linux reds

`scripts/test-nightly-core.ps1 -Case CoreMatchesFallback` is 33 passed, 2 failed, 35 rows after the port, the same pair as at the base script: `diagnostics` (logs golden length) and `coverageParts` (`diagTie` / `hidden` directory order). `verdicts`, `indexView`, and `coverageVerdicts` passed. Those two rows are not rewritten. `NightlyCoreTests.CoreMatchesFallback` therefore stays red on Linux and is outside this closed list. `WindowsUnitPlatformAdmissionTests` is omitted on Linux: its Windows skip does not make a valid receipt. It is UNOBSERVED here.

## Verification design

- V-1. `NightlyCoreTests.UidIndex` runs `test-nightly-core.ps1 -Case UidIndex` and requires `NC UidIndex.matchesLegacy`. The index result equals the node walk and `IndexRequiredUids`, and a decoy node list is not read.
- V-2. `LiveLaneRunnerTests` (impacted `nightly-live-lane`), 4 methods.
- V-3. `ReleaseImpactScriptTests` (derived closure of the nightly scripts), 10 methods.
- R-1. `TestClassificationPolicyTests` once, floor 42 executed.
- R-2. Guard union, one parenthesized filter, without `DerivedImpactIndexTests` and without `WindowsUnitPlatformAdmissionTests`: classification guard, slow-registry merge, slow tripwire, native Linux policy witness, mechanical PC contract, task platform guidance, C1729 layering, lane category. Floor 39 executed, 0 failed. C1729 stays VIOLATIONS 0 LISTED 162.
- R-3. Build `Antiphon.DerivedImpactIndex`, then `DerivedImpactIndexTests` alone, floor 27. Then the regenerate script twice; both runs print `derived-impact-index unchanged`.
- R-4. Release-build `Antiphon.LandGraphAudit`, then `scripts/audit-land-verification-graph.ps1 -Check`.
- R-5. `Read-NightlyExecutionPolicy` and `scripts/test-live-lane-run.ps1 -PrintInputDigest` match the committed pin.
- R-6. `scripts/check-evidence-diff.ps1` over base `8cca875d8c26b8590eeda6769c5db67fa8cf1a02`..HEAD.
- R-7. `git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`. At the time of writing, `origin/master` is `0822e8bbc60f2c8f333504d60cf3484cc7b6f2b8` and the only conflict is the `PinnedInputDigest` line in `scripts/test-live-lane-run.ps1` (this branch `9f57e5650d9e79c660989c466d97bd5693523ddd69338105d4e8cbde82c65cb4`, master `a08d61c11698379cd0b1e464c3032681d9cd10ea8eb78fb0f40255838e32050c` after CARD-2005 changed `nightly-core.ps1`). `nightly-tests-impl.ps1` and `test-nightly-core.ps1` auto-merge and keep the UID copy and the `UidIndex` case. This branch does not rebase. Land reseals the pin on the merged tree. The row passes when the merge is clean or that single file is the only content conflict.

No positive control. SourceLanding owns any later method-scoped mutation. No server or runner restart.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-aef78bf4-uid/ | uid-index | `/*/*/NightlyCoreTests/UidIndex` | V-1 | 1 executed, 0 failed | 1 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | all | CP-1 | live-lane | `/*/*/LiveLaneRunnerTests/*` | V-2 | >= 4 executed, 0 failed | 4 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | all | CP-1 | release-impact | `/*/*/ReleaseImpactScriptTests/*` | V-3 | >= 10 executed, 0 failed | 10 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | all | CP-1 | policy | `/*/*/TestClassificationPolicyTests/*` | R-1 | >= 42 executed, 0 failed | 42 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | all | CP-1 | guard-union | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2 | >= 39 executed, 0 failed | 39 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | all | n/a | derived-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-3 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | all | CP-1 | derived-index | `/*/*/DerivedImpactIndexTests/*` | R-3 | >= 27 executed, 0 failed | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | all | n/a | index-regen | `pwsh -NoProfile -Command '$a = & pwsh -NoProfile -File ./scripts/regenerate-derived-impact-index.ps1 -NoBuild \| Out-String; $b = & pwsh -NoProfile -File ./scripts/regenerate-derived-impact-index.ps1 -NoBuild \| Out-String; Write-Output $a; Write-Output $b; if ($a -notmatch "derived-impact-index unchanged" -or $b -notmatch "derived-impact-index unchanged" -or $a -match "derived-impact-index changed" -or $b -match "derived-impact-index changed") { throw "index drift" }'` | R-3 | exit 0, both runs unchanged | n/a | 4 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | all | n/a | land-audit-build | `dotnet build tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj -c Release -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-4 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | all | n/a | land-graph-check | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Release/net10.0/Antiphon.LandGraphAudit.dll` | R-4 | exit 0, approved | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-11 | all | n/a | nightly-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $pin = "9f57e5650d9e79c660989c466d97bd5693523ddd69338105d4e8cbde82c65cb4"; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); if ($d -ne $pin) { throw ("input digest drift computed=" + $d) }'` | R-5 | exit 0, printed digest matches the committed pin | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-12 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 8cca875d8c26b8590eeda6769c5db67fa8cf1a02 -HeadRef HEAD` | R-6 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-13 | all | n/a | merge-tree | `bash -c 'git fetch origin master && out=$(git merge-tree --write-tree origin/master HEAD 2>&1); ec=$?; printf "%s\n" "$out"; if [ "$ec" -eq 0 ]; then exit 0; fi; printf "%s\n" "$out" \| grep -q "CONFLICT (content): Merge conflict in scripts/test-live-lane-run.ps1" \|\| exit 1; n=$(printf "%s\n" "$out" \| grep -c "^CONFLICT"); [ "$n" -eq 1 ] \|\| exit 1; exit 0'` | R-7 | exit 0; clean tree, or the only content conflict is scripts/test-live-lane-run.ps1 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 86 minutes (the `EstimatedMinutes` sum). The green run is one isolated `Antiphon.Tests` build plus the rows above. CP-1 `EstimatedMinutes` is 15 so the row deadline stays at the 45-minute advisory.
