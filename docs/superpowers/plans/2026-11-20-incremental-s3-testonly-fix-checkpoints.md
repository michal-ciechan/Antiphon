# CARD-1820 S3 test-only fix checkpoints (task c412d01f)

Review r1 blocker D-1: a `project-tree:<P>` reason whose project is not the suite's own is a product edge. A body edit under a referenced helper selects every suite that references it. A body edit inside the suite's own project still defers.

A-1: `tests/Antiphon.E2E/**/*.cs` has `walker: true`. An added file selects every suite. An added `[Test]` on that star walker selects every suite. A later body edit still defers. A body edit of `ClaudeDialogWatchdogCanaryTests.cs` selects `Antiphon.Agents.Pty.Tests` because `runtime-reader-scan` is a product edge. That reason is not limited to the canary. Three `tests/fixtures/**` files also carry it: `tests/fixtures/card0490-linux/card0490-live.example.json`, `tests/fixtures/card1336-release-gate-followups.ps1` and `tests/fixtures/incremental-selection-soundness.json`. Their reader is Antiphon.Tests, so an edit selects Antiphon.Tests. That is the safe direction.

A-3: `enabled: false` selects the same suites as the enabled closure before deferral. Selection parity, not manifest bytes. `gateCredit` stays false and `activation` stays `shadow-only`.

`WindowsUnitPlatformAdmissionTests` stays off this Linux selection. A skip is not a pass. The driver project is built once, before this table, into the worktree's native output: `DerivedImpactIndexTests` runs the regenerate script with `-NoBuild`, and the checkpoint build of `tests/Antiphon.Tests` does not produce that driver. No nightly input changes, so the pin row recomputes and does not reseal. Mutation is waived and is not discharged.

## Verification design

- V-1. `RcInputOwnersMapTests`, 28 methods, 0 failed.
- V-2. Nine-class guard union, floor 81. `C1729LayeringGuardTests` stays at VIOLATIONS 0 LISTED 162.
- V-3. `DerivedImpactIndexTests` alone, floor 27, after the driver build.
- V-4. `scripts/rc-input-reader-census.ps1`. Exit 0, `under-selected=0`, `noop-vs-owner=0`.
- V-5. Nightly policy hash and pinned input digest match the committed pins.
- V-6. `scripts/check-evidence-diff.ps1` over `3cc3852c4073a54bdef9e427a810fdbc4520e441..HEAD`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | fix | `tests/Antiphon.Tests -> bin-s3-tofix/` | s3fix-owners | `/*/*/RcInputOwnersMapTests/*` | V-1 | 0 failed, executed at least 28 | 28 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | fix | CP-1 | s3fix-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-2 | 0 failed, executed at least 81 | 81 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | fix | CP-1 | s3fix-index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | V-3 | 0 failed, executed at least 27 | 27 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | fix | n/a | s3fix-census | `pwsh -NoProfile -File scripts/rc-input-reader-census.ps1` | V-4 | exit 0 and under-selected=0 | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | fix | n/a | s3fix-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; $pin = $m.Groups[1].Value; $quote = [char]34; $legacyPattern = "LegacyExclusionDigest = ${quote}([0-9a-f]{64})${quote}"; $legacy = [regex]::Match((Get-Content -Raw ./tests/Shared/NativeLinuxPolicyWitness.cs), $legacyPattern).Groups[1].Value; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); Write-Output ("legacyExclusionDigest=" + $legacy); if (-not $legacy) { throw "legacy pin line missing" }; if ($d -cne $pin) { throw ("input digest drift computed=" + $d) }'` | V-5 | exit 0, printed digest equals the script pin | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | fix | n/a | s3fix-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 3cc3852c4073a54bdef9e427a810fdbc4520e441 -HeadRef HEAD` | V-6 | exit 0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
