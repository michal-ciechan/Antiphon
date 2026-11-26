# Incremental rc S2 selector soundness checkpoints (Code task f1b04f65)

Replay of twelve historical rc failures from runs `6591e378`, `d88de419`,
`0b292fa4`, `97150a0b`, `34d41b7e`, `f2de5b30`, and `a6278259`. The report test
is `IncrementalSelectionSoundnessTests`. It reads
`scripts/lib/rc-input-owners.json` and
`tests/fixtures/incremental-selection-soundness.json`, then computes two
statuses from the stored path lists. The current owner map is whatever the
committed file says. The proposed project closure (plan M1, not built here)
selects a suite when a changed path is under the suite csproj or a project
reached by `ProjectReference` (reflexive, transitive, every `Include`), or the
path's file name is a build-graph root (`Directory.Build.*`,
`Directory.Packages.props`, `global.json`, `NuGet.config`, `packages.lock.json`,
`*.sln`, `*.slnx`). Linked `Compile` items are not edges. The test passes when
each recorded status matches that computation and the computed misses equal
`knownUnsound`.

The first committed fixture records `f2de5b30-resilience-mirror` as selected
and `knownUnsound` as empty, so CP-1 on that commit is red. The correction is
the following commit. This table is the closed list for the corrected source.

`WindowsUnitPlatformAdmissionTests` stays off this Linux selection.
`C1210_Windows_linux_only_outcomes_are_exact` skips on Linux, and a skip is
not a pass. CP-2 is the one `TestClassificationPolicyTests` run: the nine-class
guard union, floor 81. CP-3 runs only after `tests/derived-impact-index.json`
includes the new class. No migration. No positive-control row. Mutation
execution is waived (operator 2026-11-12) and is not discharged.

The checkpoint tool's own row deadlines govern these rows. A test-host
`--timeout 570s` is not passed to the tool: that cap would kill the 15-minute
first row. `bin-s2-sound/` contains no `ref` substring.

## Verification design

- V-1. `IncrementalSelectionSoundnessTests`: owner-map status and project-closure known-unsound set. 2 methods.
- V-2. Nine-class guard union, without `WindowsUnitPlatformAdmissionTests`, floor 81. This is the single `TestClassificationPolicyTests` run. `C1729LayeringGuardTests` stays at VIOLATIONS 0 LISTED 162.
- V-3. `DerivedImpactIndexTests` alone, floor 27.
- V-4. `scripts/audit-land-verification-graph.ps1 -Check`.
- V-5. Nightly policy hash and pinned input digest recomputed against the committed pins. No re-pin in this change.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | s2 | `tests/Antiphon.Tests -> bin-s2-sound/` | s2-sound | `/*/*/(IncrementalSelectionSoundnessTests*)|(IncrementalSelectionSoundnessInvariantTests*)/*` | V-1 | 0 failed, executed 2 | 2 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | s2 | CP-1 | s2-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-2 | 0 failed, executed at least 81 | 81 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | s2 | CP-1 | s2-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | V-3 | 0 failed, executed at least 27 | 27 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | s2 | n/a | s2-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | V-4 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | s2 | n/a | s2-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; $pin = $m.Groups[1].Value; $quote = [char]34; $legacyPattern = "LegacyExclusionDigest = ${quote}([0-9a-f]{64})${quote}"; $legacy = [regex]::Match((Get-Content -Raw ./tests/Shared/NativeLinuxPolicyWitness.cs), $legacyPattern).Groups[1].Value; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); Write-Output ("legacyExclusionDigest=" + $legacy); if (-not $legacy) { throw "legacy pin line missing" }; if ($d -cne $pin) { throw ("input digest drift computed=" + $d) }'` | V-5 | exit 0, printed digest equals the script pin | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
