# CARD-2009 fix: E2E identity parser and owner-path census wiring

Base `30e42ec5f4947e9dac92159dcbc83b7b2cc03686`. Review `0c9dacf9` round 1 blockers only.

D-1: `Get-RcTestIdentities` / `Test-RcTestIdentityChange` in `scripts/lib/rc-input-manifest.ps1` match a multi-line `[Arguments(...)]` row, an attribute that contains `]`, a generic method, and a qualified `Task` or `ValueTask`. The method's class is the open brace scope. Either side with more non-comment `[Test` attributes than matched methods, or more `[Arguments(` openers than captured rows, selects. A later body edit of a parsed method still defers.

D-2: `RealOwnerMapLiteralPathsExist` runs `Get-RcMissingOwnerLiteralPaths` and `scripts/rc-input-owner-path-census.ps1` on this repository and the committed map, and requires zero rows. The same case materializes those literal prefixes in a scratch directory and requires `missing-literal-path` after the canary file is renamed.

Red witness `2e4a0adc0f72ec254f8265e4068d58303d8e2758` (parser still the base): `E2EIdentityChangeSelectsSuites` failed `e2e-multiline-arguments-selects-all`, `e2e-array-arguments-rename-selects-all`, and `e2e-generic-rename-selects-all`. Fix `ca89510a40a0255ded783e475cc7a5f3cc6380d9` passes both harness cases. No pinned input changed, so CP-5 recomputes and does not reseal. `WindowsUnitPlatformAdmissionTests` stays off this Linux selection. Windows is unobserved. Mutation is waived and is not discharged. CP-3 needs the regenerate driver in the native output first (`dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false`); the Antiphon.Tests build does not produce it.

## Verification design

- V-1. `RcInputOwnersMapTests`, 31 methods, 0 failed. Includes the three D-1 labels and `RealOwnerMapLiteralPathsExist`.
- V-2. Nine-class guard union, floor 84. `TaskPlatformGuidanceTests` is five methods. `C1729LayeringGuardTests` stays at VIOLATIONS 0 LISTED 162.
- V-3. `DerivedImpactIndexTests` alone, floor 27, after the driver build named above.
- V-4. `scripts/rc-input-owner-path-census.ps1`. Exit 0 and `missing-literal-path=0`.
- V-5. Nightly pinned input digest matches the committed pin `99297dde12befbe23fd15dff6f60b3c99c0df82bca5a2817f2c6bef5e4fef6df`.
- V-6. `scripts/check-evidence-diff.ps1` over `30e42ec5f4947e9dac92159dcbc83b7b2cc03686..HEAD`.
- V-7. `git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` has no conflicts.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | fix | `tests/Antiphon.Tests -> bin-c2009fix/` | c2009fix-owners | `/*/*/RcInputOwnersMapTests/*` | V-1 | 0 failed, executed at least 31 | 31 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | fix | CP-1 | c2009fix-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-2 | 0 failed, executed at least 84 | 84 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | fix | CP-1 | c2009fix-index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | V-3 | 0 failed, executed at least 27 | 27 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | fix | n/a | c2009fix-census | `pwsh -NoProfile -File scripts/rc-input-owner-path-census.ps1` | V-4 | exit 0 and missing-literal-path=0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | fix | n/a | c2009fix-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; $pin = $m.Groups[1].Value; $quote = [char]34; $legacyPattern = "LegacyExclusionDigest = ${quote}([0-9a-f]{64})${quote}"; $legacy = [regex]::Match((Get-Content -Raw ./tests/Shared/NativeLinuxPolicyWitness.cs), $legacyPattern).Groups[1].Value; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); Write-Output ("legacyExclusionDigest=" + $legacy); if (-not $legacy) { throw "legacy pin line missing" }; if ($d -cne $pin) { throw ("input digest drift computed=" + $d) }'` | V-5 | exit 0, printed digest equals the script pin | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | fix | n/a | c2009fix-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 30e42ec5f4947e9dac92159dcbc83b7b2cc03686 -HeadRef HEAD` | V-6 | exit 0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | fix | n/a | c2009fix-merge | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | V-7 | exit 0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
