# Run e7f92f66 group 134 checkpoints

Debug task `808a934f`. Branch `feat/card-task-808a934f`.
Base `e0f381c48e840fb2ee614fd63571f8b26ca4a01d`.

Two repairs. The C1801 literals now name `LinkedUnattemptedQueueWakeTests` where the capped window kept it, and no longer name `C0747LandNoteExpireEndpointTests`. The C1336 candidate clone sets local `core.longpaths` true before any `-NoSync` run. The pre-fix witnesses are unlisted: the selector strings differed by that one class, and the fixture's omitted run returned phase `longpaths-not-enabled` exit 3 after `PASS C1336 N omitted`.

## Verification design

- V-1: `ImpactedClassesSelectorTests`, whole class. The two C1801 filters match the selector. Floor 40.
- V-2: `ReleaseGateFollowupTests`, both harness cases, including `C1336_NightlyCandidateIdentity`. Floor 2.
- R-1: `TestClassificationPolicyTests` once. Floor 42.
- R-2: `DerivedImpactIndexTests` alone. Floor 27. Build `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` through `scripts/build-slot.ps1` before this table, native output, `UseAppHost=false`. `tests/Antiphon.Tests` does not produce that driver. That build is unlisted. Run `scripts/regenerate-derived-impact-index.ps1 -NoBuild` twice. If the second print is unchanged and the file is unmodified, do not commit the index.
- R-3: the rest of the short guard union, including `WindowsUnitPlatformAdmissionTests`. C1729 stays VIOLATIONS 0 LISTED 162. The Linux skip of `C1210_Windows_linux_only_outcomes_are_exact` is rostered and is not a failure. Floor 39.
- R-4: `scripts/audit-land-verification-graph.ps1 -Check` exits 0.
- R-5: `scripts/check-evidence-diff.ps1` over `e0f381c48e840fb2ee614fd63571f8b26ca4a01d..HEAD` exits 0.
- R-6: stated pins stay the recomputed values. `PinnedInputDigest` in `scripts/test-live-lane-run.ps1` is `c0d2a723edfe8f12b63f7eafa7ed360c6a8bc132377c1705d2fa9dd9dce6cd84`. `policyHash` in `tests/test-execution-policy.json` is `8c376b03c4e012e3700de15e8b99bdbe851187bce4d3c5defe2b6b6df1f782e2`. Re-pin only if a guard fails because an input changed. This repair does not change those inputs.

Mutation execution is waived (operator 2026-11-12). Short tests only (operator 2026-11-18). No positive controls.

## Performance budget

CP-1 EstimatedMinutes 15 is the cold-build allowance. Later rows reuse that build. `ANTIPHON_LINUX_COMPILER_POLICY=0`. Admit a row only when the 1-minute load is below 16. No row is expected to exceed 10 minutes of test execution.

### Checkpoints

Run through the checkpoint tool with `--expected-source-sha` set to committed HEAD, outside a build-slot lease. The tool leases its own slot. Wait until the exit is not 75. Remove `tests/Antiphon.Tests/bin-g134` by that exact path after a green run.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-g134/ | selector | `/*/*/ImpactedClassesSelectorTests/*` | V-1 | all listed, 0 failed | 40 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | all | CP-1 | c1336 | `/*/*/ReleaseGateFollowupTests/*` | V-2 | all listed, 0 failed | 2 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | all | CP-1 | policy | `/*/*/TestClassificationPolicyTests/*` | R-1 | all listed, 0 failed | 42 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | all | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | R-2 | all listed, 0 failed | 27 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | all | CP-1 | linux-guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(WindowsUnitPlatformAdmissionInvariantTests*)/*` | R-3 | all listed, 0 failed | 39 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | all | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-4 | exit 0 | n/a | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-7 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . -BaseRef e0f381c48e840fb2ee614fd63571f8b26ca4a01d -HeadRef HEAD` | R-5 | exit 0 | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-8 | all | n/a | pin-digest | `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest` | R-6 | exit 0 | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
