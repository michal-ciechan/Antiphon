# Incremental rc S3 owner-map checkpoints (Code task 1f7120d3)

Replace the blanket `**` owner with an M1 map. `scripts/lib/rc-input-manifest.ps1`
applies every root except `**` first, then the existing project walk (project
directory, ProjectReference, Import, and Compile/Content/None/EmbeddedResource/
AdditionalFiles/Analyzer includes), then applies `**` only to paths no suite
owns. Unknown paths still broaden every suite when no fallback claims them.

`RcInputOwnersMapTests` reads the committed map. A changed Compile link selects
the including project. An unlinked sibling selects the project that contains
the file. `release-selection.ps1` is unchanged and still emits Full.

`WindowsUnitPlatformAdmissionTests` stays off this Linux selection.
`C1210_Windows_linux_only_outcomes_are_exact` skips on Linux, and a skip is
not a pass. CP-2 is the nine-class guard union, floor 81, and the single
`TestClassificationPolicyTests` run. CP-3 runs only after
`tests/derived-impact-index.json` includes the new class. No migration.
No positive-control row. Mutation execution is waived (operator 2026-11-12)
and is not discharged.

The nightly `scripts` suite has no csproj, so it is not an owner-map consumer.
`scripts/**` is assigned to the two repository-reader projects, and
`scripts/lib/**` is forced full.

The checkpoint tool's own row deadlines govern these rows. A test-host
`--timeout 570s` is not passed to the tool: that cap would kill the 15-minute
first row. `bin-s3-owners/` contains no `ref` substring.

## Verification design

- V-1. `RcInputOwnersMapTests`, `RcInputManifestTests`, and `RcChunkInputDigestTests`. 20 methods.
- V-2. Nine-class guard union, without `WindowsUnitPlatformAdmissionTests`, floor 81. This is the single `TestClassificationPolicyTests` run. `C1729LayeringGuardTests` stays at VIOLATIONS 0 LISTED 162.
- V-3. `DerivedImpactIndexTests` alone, floor 27.
- V-4. `scripts/audit-land-verification-graph.ps1 -Check`.
- V-5. Nightly policy hash and pinned input digest recomputed against the committed pins. No re-pin unless a guard fails.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | s3 | `tests/Antiphon.Tests -> bin-s3-owners/` | s3-owners | `/*/*/(RcInputOwnersMapTests*)\|(RcInputManifestTests*)\|(RcChunkInputDigestTests*)/*` | V-1 | 0 failed, executed at least 20 | 20 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | s3 | CP-1 | s3-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-2 | 0 failed, executed at least 81 | 81 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | s3 | CP-1 | s3-index | `/*/*/DerivedImpactIndexTests/*` | V-3 | 0 failed, executed at least 27 | 27 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | s3 | n/a | s3-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | V-4 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | s3 | n/a | s3-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; $pin = $m.Groups[1].Value; $quote = [char]34; $legacyPattern = "LegacyExclusionDigest = ${quote}([0-9a-f]{64})${quote}"; $legacy = [regex]::Match((Get-Content -Raw ./tests/Shared/NativeLinuxPolicyWitness.cs), $legacyPattern).Groups[1].Value; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); Write-Output ("legacyExclusionDigest=" + $legacy); if (-not $legacy) { throw "legacy pin line missing" }; if ($d -cne $pin) { throw ("input digest drift computed=" + $d) }'` | V-5 | exit 0, printed digest equals the script pin | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
