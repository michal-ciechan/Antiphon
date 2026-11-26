# CARD-2009 round 3: C# verbatim strings in the identity parser

Base `adc423c49c149843648a95ebc27f5b5c94bf8eef`. Review round 2 `0eda4073694b1cfceb56bdebe881a4fb84316c42` left D-1 open.

D-1: `Skip-RcNonCode` treats `@"`, `$@"` and `@$"` as C# verbatim strings. A doubled `""` is one quote. `{{` and `}}` are braces in an interpolated verbatim string. A raw literal, including `$"""` with one or more `$`, ends on the same quote run that opened it. `[Test` and `Arguments(` counts, including `[Test, Arguments(1)]`, come from a regex over `Get-RcCommentStrippedText`, not from the attribute walk. A later body edit of a fully parsed method still defers.

Red witnesses at `1157b78d02ed45141b28409cbadeb8fa14a2413c`, parser still the base: `VerbatimStringRenameSelectsSuites` failed `e2e-verbatim-rename-selects-all` and `e2e-comma-arguments-selects-all`. `RealE2EParseMatchesRegex` failed `BoardE2ETests.cs` (matched 3, regex 10) and `ContractSnapshotTests.cs` (matched 2, regex 5). `FailClosedOnlySelectsSuites` passed on the base parser and failed when `Test-RcTestIdentityIncomplete` returned `$false`. Fix `77fc1366fb4b5d5019c7e0741a67fb27cc3e0eca` passes all three. The same mutant fails `FailClosedOnlySelectsSuites` again on the fix; the function was restored before this plan.

No pinned input changed, so CP-6 recomputes and does not reseal. On this branch the reader is `Read-NightlyExecutionPolicy` because `NightlyPolicy` is not in the core yet. CP-6 calls `NightlyPolicy.Read` when that type is loaded, which is the reader on master after `47dd2f06c` deleted `scripts/lib/nightly-policy.ps1`. `WindowsUnitPlatformAdmissionTests` stays off this Linux selection. Windows is unobserved. Mutation is waived and is not discharged. CP-4 needs the regenerate driver in the native output first. The Antiphon.Tests build does not produce it.

The independent regex also counts `[Test` and `Arguments(` text that is not a method attribute. That flags 30 of 2748 `tests/**/*.cs` files incomplete, so a body edit of those files selects. None of them are under `tests/Antiphon.E2E`. E2E matched counts equal the regex and no E2E file is incomplete.

## Verification design

- V-1. `RcInputOwnersMapTests`, 34 methods, 0 failed. Includes the verbatim rename, the fail-closed body edit, and the real E2E count.
- V-2. Nine-class guard union, floor 84. `TaskPlatformGuidanceTests` is in the union. `C1729LayeringGuardTests` stays at VIOLATIONS 0 LISTED 162.
- V-3. Build `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` with no custom output.
- V-4. `DerivedImpactIndexTests` alone, floor 27, after V-3.
- V-5. `scripts/rc-input-owner-path-census.ps1`. Exit 0 and `missing-literal-path=0`.
- V-6. Branch input digest equals the pin in `scripts/test-live-lane-run.ps1` (`99297dde12befbe23fd15dff6f60b3c99c0df82bca5a2817f2c6bef5e4fef6df`). Merged-tree digest against `origin/master` is reported and is not a reseal.
- V-7. `scripts/check-evidence-diff.ps1` over `adc423c49c149843648a95ebc27f5b5c94bf8eef..HEAD`.
- V-8. `git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` has no conflicts.
- V-9. `scripts/audit-land-verification-graph.ps1 -Check` exits 0.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | fix | `tests/Antiphon.Tests -> bin-c2009r3/` | c2009r3-owners | `/*/*/RcInputOwnersMapTests/*` | V-1 | 0 failed, executed at least 34 | 34 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | fix | CP-1 | c2009r3-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-2 | 0 failed, executed at least 84 | 84 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | fix | n/a | c2009r3-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false --verbosity minimal` | V-3 | exit 0 | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | fix | CP-1 | c2009r3-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | V-4 | 0 failed, executed at least 27 | 27 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | fix | n/a | c2009r3-census | `pwsh -NoProfile -File scripts/rc-input-owner-path-census.ps1` | V-5 | exit 0 and missing-literal-path=0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | fix | n/a | c2009r3-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-core.ps1; $root = (Get-Location).Path; $loaded = Test-NightlyCoreAvailable; $policyType = "Antiphon.Nightly.NightlyPolicy" -as [type]; if ($policyType) { $policy = $policyType.GetMethod("Read").Invoke($null, @($root, "")); $hash = [string]$policy.Hash } elseif (Test-Path -LiteralPath "./scripts/lib/nightly-policy.ps1") { . ./scripts/lib/nightly-policy.ps1; $hash = [string](Read-NightlyExecutionPolicy -RepoRoot $root).Hash } else { throw "no policy reader" }; $lines = @(pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest); $digest = $lines[$lines.Length - 1].Trim(); $q = [char]39; $match = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $match.Success) { throw "pin line missing" }; $pin = $match.Groups[1].Value; Write-Output ("policyHash=" + $hash); Write-Output ("inputDigest=" + $digest); Write-Output ("pinnedInputDigest=" + $pin); if ($digest -cne $pin) { throw ("input digest drift computed=" + $digest) }'` | V-6 | exit 0, printed digest equals the script pin | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | fix | n/a | c2009r3-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef adc423c49c149843648a95ebc27f5b5c94bf8eef -HeadRef HEAD` | V-7 | exit 0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | fix | n/a | c2009r3-merge | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | V-8 | exit 0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | fix | n/a | c2009r3-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | V-9 | exit 0 | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
