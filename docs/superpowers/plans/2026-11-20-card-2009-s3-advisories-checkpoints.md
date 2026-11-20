# CARD-2009 S3 owner-map advisories (task f0a2e975)

Base `ba9ffc8ccc5496ef849af656e1d2906b7d308e43`. Master already contains the S3 test-only class, the E2E star walker and `runtime-reader-scan`, so this branch stays on that tip rather than `feat/card-task-c412d01f`.

A-1: the star walker for `tests/Antiphon.E2E/**/*.cs` compares the multiset of `class|method|Arguments` identities, the c1584 roster key, per changed file. A net-zero rename, a same-identity move between files and an added `[Arguments]` row select every suite. A later body edit still defers. `gateCredit` stays false and `activation` stays `shadow-only`. `creditBlockedBy` stays `unowned-paths` only when an unowned path is present. `scripts/release-selection.ps1` is unchanged.

A-2: `scripts/rc-input-owner-path-census.ps1` fails closed when a literal root, the canary file, a `testOnly.exceptPaths` prefix, `suiteProjects`, or an owner-glob directory prefix is missing. The message is `missing-literal-path: <prefix> <= <pattern>`. A glob that starts at `**` has no directory prefix. Root `except` carve-outs are not existence-gated: `docs/reviews/**` has no directory, and a missing carve-out over-selects.

A-3: `runtime-reader-scan` is also on `tests/fixtures/card0490-linux/card0490-live.example.json`, `tests/fixtures/card1336-release-gate-followups.ps1` and `tests/fixtures/incremental-selection-soundness.json`. Those edits select Antiphon.Tests. That is the safe direction. The corrected sentence is in `docs/superpowers/plans/2026-11-20-incremental-s3-testonly-fix-checkpoints.md`.

`WindowsUnitPlatformAdmissionTests` stays off this Linux selection. A skip is not a pass. Windows is unobserved. The driver project is built once, before this table, into the worktree's native output: `DerivedImpactIndexTests` runs the regenerate script with `-NoBuild`, and the checkpoint build of `tests/Antiphon.Tests` does not produce that driver. No nightly input changes, so the pin row recomputes and does not reseal. Mutation is waived and is not discharged. `scripts/rc-input-reader-census.ps1` is outside this table: Code runs it only when it finishes within 500 seconds.

## Verification design

- V-1. `RcInputOwnersMapTests`, 30 methods, 0 failed. Includes `E2EIdentityChangeSelectsSuites` and `OwnerLiteralPathsFailClosed`, both red at `02ca16cf277dc9cd329e8bf15e786f9f44802968` before the fix.
- V-2. Nine-class guard union, floor 84. `TaskPlatformGuidanceTests` is five methods. The four tests on `RunnerDefaultGuidanceTests` are outside that filter. `C1729LayeringGuardTests` stays at VIOLATIONS 0 LISTED 162.
- V-3. `DerivedImpactIndexTests` alone, floor 27, after the driver build.
- V-4. `scripts/rc-input-owner-path-census.ps1`. Exit 0 and `missing-literal-path=0`.
- V-5. Nightly policy hash and pinned input digest match the committed pins.
- V-6. `scripts/check-evidence-diff.ps1` over `ba9ffc8ccc5496ef849af656e1d2906b7d308e43..HEAD`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | fix | `tests/Antiphon.Tests -> bin-c2009s3/` | c2009-owners | `/*/*/RcInputOwnersMapTests/*` | V-1 | 0 failed, executed at least 30 | 30 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | fix | CP-1 | c2009-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-2 | 0 failed, executed at least 84 | 84 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | fix | CP-1 | c2009-index | `/*/*/DerivedImpactIndexTests/*` | V-3 | 0 failed, executed at least 27 | 27 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | fix | n/a | c2009-census | `pwsh -NoProfile -File scripts/rc-input-owner-path-census.ps1` | V-4 | exit 0 and missing-literal-path=0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | fix | n/a | c2009-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; $pin = $m.Groups[1].Value; $quote = [char]34; $legacyPattern = "LegacyExclusionDigest = ${quote}([0-9a-f]{64})${quote}"; $legacy = [regex]::Match((Get-Content -Raw ./tests/Shared/NativeLinuxPolicyWitness.cs), $legacyPattern).Groups[1].Value; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); Write-Output ("legacyExclusionDigest=" + $legacy); if (-not $legacy) { throw "legacy pin line missing" }; if ($d -cne $pin) { throw ("input digest drift computed=" + $d) }'` | V-5 | exit 0, printed digest equals the script pin | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | fix | n/a | c2009-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef ba9ffc8ccc5496ef849af656e1d2906b7d308e43 -HeadRef HEAD` | V-6 | exit 0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
