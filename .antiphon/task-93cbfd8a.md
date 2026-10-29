# Windows rc exclusions for Linux-only skips

Outcome: Windows Full rc qualification no longer has to treat POSIX-by-construction skips as coverage failures. 45 `platform: windows` rows were added to `profiles.rc.exclusions`. Linux still requires those cases. Junction, case-sensitivity, CARD-1199 unverified, and opt-in skips stay required, so a real Windows Full run can still end `coverageComplete=false` until those gaps are decided.

Branch `feat/card-task-93cbfd8a` from `91b45006b4d76f144c2651180039aece5a9d7938`.

| Commit | Subject |
|---|---|
| `1c35c653123d1ad5be0d5d86d4facd6383aeebbc` | test: witness Windows rc exclusions for Linux-only skips |
| `3180cd87d3947fce7c469ec5851d3512a9fb5c13` | fix: exclude Linux-only skips from the Windows rc census |
| `65862ecfdf961619fdac8e1f473d6805fad5a3ab` | test: retarget the C1410 census after Linux-only Windows exclusions |

`policyHash` moved from `67fb49932cf43763c6dd0bafd61ccac4d2945e72f8ceeed97c9d1910de35f38d` to `a7c88a0ab7b92daa0e9a72a8b2f377ca4a8678eaa0d469d22d1d97551c676382`. That string was the only digest pin. The C1410 fixed-listing pin moved with the exclusions: 155 launches / `106516.3397645542` s became 154 launches / `106343.8397645542` s (164 listing nodes, 172.5 s, one listing launch).

## What landed

Each new row has `platform: windows`, owner from the skip or roster (CARD-0590 when no card is named; C1364 methods use CARD-1050), and a reason that starts `Linux-only by construction:` and ends `The case remains required on Linux.` 25 rows are whole classes. 20 are method lists. CARD-1415 (2), CARD-1323 (3), CARD-1561 (27), and the three session-runner class rows are unchanged. `scripts/fixtures/c599-policy-v2.json` and `WindowsUnitSkipRoster` are unchanged.

`docs/release-gates.md` states that the prefix is a Windows-only POSIX exclusion and that junction, case-sensitivity, CARD-1199 unverified, and opt-in skips stay required.

`scripts/test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusions` (7 rows, also `ReleaseGatePlatformTests.LinuxOnlyWindowsExclusions`) checks:

- every new row's class or method is gated by a Linux/POSIX skip (`IsLinux` skip, `IsWindows` skip of a Unix proof, `NativeLinuxGate` / `RequireNativeLinux` / `RequireLinux` / `RequireUnix`, or one same-file helper that is)
- `AttentionFeedNoiseTests` and a synthetic junction skip are refused
- every class in qualification sections 2b and 2d is an exclusion or in the in-test decision set
- decision classes and the four decision methods have no covering exclusion
- filtering the excluded identities out of the terminal set leaves `coverageComplete` with no `unexecuted-outcome`
- the decision-gap identities stay `NotExecuted` and incomplete
- `platform: linux` does not apply the new rows

`RollingVolumeRecycleScriptTests`: the qualification chunk counted 6 NotExecuted rows. Source has 10 Linux-gated methods, and all 10 are excluded.

`RemoteScriptContractTests` is method-scoped. `C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` stays required.

## Needs decision

The schema has no argument-level exclusion. A method row would drop every `[Arguments]` row of that method, including ones that run on Windows. These stay required.

| Class / method | Why Windows skipped | What verifying on Windows would take | Recommended owner action |
|---|---|---|---|
| `ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs` (1 row) | "Native watchdog qualification is explicitly opt-in." | An explicit opt-in run of the native watchdog canary. | Add an opt-in row only if it should match the CARD-0599 manual-obligation shape. Otherwise run it opted in. |
| `OutputCleanupTests.parent_symlink_does_not_delete_a_tracked_stamp` (1 of 11) | "Windows junction variant is unverified". The other 10 methods are excluded. | A junction fixture for the parent-link case. | Keep this method required. Implement the junction variant. Owner CARD-0590. |
| `CheckpointOwnedOutputTests.changed_inventory_is_refused` argument `link` (1 row) | "Linux symlink proof; Windows junction remains R2". Other arguments run. | A Windows junction argument for `link`. | Keep the class required. CARD-1199 owns the R2 disclosure. |
| `CheckpointScriptReaderDeadlineTests` ledger arguments `unreadable` and `tampered-beside-unreadable` (2 rows) | "Linux hard-link ledger qualification; the reader cases run on every platform". Gated by `ledgerCase && !IsLinux()`. | A Windows hard-link or unreadable-ledger fixture for those two arguments. | Do not class-exclude. Owner CARD-0590. An argument exclusion would need a schema change. |
| `CheckpointShapeConflictTests` (17 rows once group 105 completes) | "CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure." | Junction shape-conflict proofs. | CARD-1199. No exclusion until Windows is verified or the R2 disclosure is explicitly accepted. |
| `CheckpointTrackedBaselineTests` (13 rows) | "CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified." | Run or port the 13 proofs on Windows. | CARD-1199. No exclusion. |
| `CheckpointOutputConfinementTests` (5 rows) | "CARD-1199 output confinement real-build proof runs on Linux; Windows is unverified." `windows_confined_build_leaves_use_app_host_unset` runs on Windows. | Verify the five real-build proofs on Windows. | CARD-1199. Do not class-exclude. |
| `CompletedCardWorkspaceRemovalTests.C1032_InspectionHoldShapes` argument `case-twin` (1 of 4) | "Case-twin inspection requires a case-sensitive scratch filesystem". The exec-bit and FIFO methods are excluded. | A case-sensitive Windows scratch, or an accepted case-folding disclosure. | CARD-1032. Do not method-exclude; the other shapes run. |
| `RunnerWorkspaceBindingTests.C1032_StoreIdentityRefused` argument `unreadable` (1 row) | "Mode-bit denial of the registration file is Linux-only." The other nine arguments run. | A Windows ACL denial of the registration file for `unreadable`. | CARD-1032. Do not method-exclude. |
| `PlanCoverageCommandTests.existing_outside_project_is_refused_by_root_boundary` | Latent `UnauthorizedAccessException` / `PlatformNotSupportedException` / `IOException` catch: "coverage project symlink creation unavailable". Not in the documented 1-row skip. The FIFO method is excluded. | Confirm whether Windows hits that catch. If it does, it is a missing-capability gap. | Leave required. Owner CARD-0590. |

Section 2b leftover after the exclusions is those argument and obligation rows (watchdog 1, OutputCleanup junction 1, owned-output link 1, script-reader ledger 2, case-twin 1, store-identity unreadable 1). Group 105's 65 future NotExecuted rows: 30 are now excluded (recovery 10, stamp safety 10, fixture routing 4, inherited file 4, temp-root sweep 1, plan-coverage FIFO 1). The other 35 are the three CARD-1199 classes above.

## Verification

All through `scripts/build-slot.ps1`. Counts are passed/failed rows.

- Red witness, before the policy edit, lease `2c50ef05`: 4 passed, 3 failed. Failures were `every-new-row-has-linux-gate` (0 shipped rows, 45 expected), `document-classes-are-excluded-or-decided`, and `linux-only-rows-leave-census-complete`. Source-gate, ungated-class, decision-class, and decision-gap rows passed.
- Green witness, lease `c82f4bcc`: 7 passed, 0 failed.
- Guard slot lease `c8af4fe2` (held 422s), commit `65862ecfd`: C1404 1/0, C1577 1/0, C1584 1/0, C1410 1/0, G2865 1/0, roster Mirror 2/0, Explicit 4/0, Costs 4/0, C1415 10/0, LinuxSessionRunnerAdmission 13/0. `dotnet build tests/Antiphon.Tests` `UseAppHost=false` `OutputPath=bin-linuxonly93/` : 0 errors, 1357 warnings. TUnit: `LinuxOnlyWindowsExclusions` 1 passed, `C1210_Roster_closure_matches_source_gates` 1 passed, `RcRosterRepairTests` 3 passed. The `bin-linuxonly93` directories were removed.

The first C1410 run, before the pin commit, failed `launch-count,suite-hours` (154 launches, 29.54 h). That was the pin, and the census, split, and watchdog checks were already clean.

## Land

`git merge-tree --write-tree origin/rc/2026-10-27 HEAD` is clean (tree `0f8832ab0670b4319e0ee52dd8b915c6ac213f1d`). `git merge-tree --write-tree origin/master HEAD` exits 1. `origin/master` blobs for `tests/test-execution-policy.json`, `scripts/test-rc-platform.ps1`, and `scripts/test-nightly-tests.ps1` are identical to rc parent `91b45006`, and both histories changed those files since merge-base `a97194ee7a4adf215454fd82c453ba46362596ee`, so the merge reports content conflicts there. Also add/add on `.antiphon/task-af2b78c8.md` and `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`, which this task did not touch. This task does not land.
