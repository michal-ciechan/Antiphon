# CARD-1836 S8b shadow sample checkpoints

Original Code/landing owner: ad11c378-16c7-4e0d-bffd-04e440b33d63.
Base: 9bcd61113f631ec58f158ff3fb637736fb786fd4.
Authority: activation plan D-8 and S8b. Shadow mode only.
Owned files: scripts/lib/rc-shadow-sample.ps1, scripts/test-rc-shadow-sample.ps1, tests/Antiphon.Tests/Scripts/RcIncrementalShadowTests.cs.
Red commit ec01d5fc27af0dadfd8ed5f6bebe6d28c1065043 failed the three V-11 outcome assertions on the default-deny adapter. That adapter is not a deliverable.
Green library e34da3630e05c6099f32504e110677d3ecbb1ce5 persists a proposal and does not grant trust.

## Verification design

V-11: SampleSizeStrataAndReplay, MismatchInvalidatesCarries, NoCarryAndOnePilotDoNotGrantTrust.
The witnesses pin the fixed-seed roster, stratum increase, replay, write-once proposal, Cchunk=0 sample=not-applicable, contract mismatch, failed-sample preservation, and refusal of trust from zero carries or one pilot.
effectiveMode stays Full, gateCredit stays false, IncrementalValidation stays Shadow, and no trust record is written.
The off switch is the unchanged Full/Shadow/ChangeAware ValidateSet lines plus Get-ReleaseGateCreditVerdict refusing Incremental.
R-1 through R-7 belong to other slices. S8b changes none of their surfaces.
No PC is defined for S8b. Mutation execution is operator-waived (2026-11-12). Every PC stays pending for caller-commissioned SourceLanding Mutation.
Windows-only behavior is UNOBSERVED. WindowsUnitPlatformAdmissionTests is not in a zero-skip row because C1210_Windows_linux_only_outcomes_are_exact skips on Linux.
Restart: none. No publish, promote, tag, or IncrementalValidation flip.

## Performance budget

Dispatch sets the first row EstimatedMinutes to 15. CP-90, CP-91, and CP-92 keep the activation plan's provisional minutes.
Each harness child has a 45s deadline, 10s cleanup, and a 60s TUnit budget. No measured p95 yet.
The derived checkpoint total (2 * sum + 10) exceeds 30. CARD-1836 treats that derived allowance as an importer exception. Row timeouts are not widened.
Rows run serially. CP-91 and CP-92 do not overlap Antiphon.Tests.
Impacted classes for base..HEAD select spawn-limit-rosters and test-lane-category.
nightly-live-lane does not match. CP-91 and CP-92 are the other assemblies that spawn-limit-rosters names.
CP-90 adds DerivedImpactIndexTests to the activation-plan guard union, matching S8a. Min 79 remains the executed-test floor.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-11 | S8b | `tests/Antiphon.Tests -> bin-c1836-s8b/` | linux-sample | `/*/*/RcIncrementalShadowTests/*` | V-11 | all 3 named methods, 0 failed/skipped | 3 | 15 |
| CP-90 | S8b | `CP-11` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)\|(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | policy, categories, spawn census, index | all listed, 0 failed/skipped | 79 | 4 |
| CP-91 | S8b | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s8b-pty/` | linux-pty-spawn | `/*/*/(ProcessSpawnLimitTests*)\|(ProcessSpawnLimitInvariantTests*)/*` | spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 3 |
| CP-92 | S8b | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s8b-runner/` | linux-runner-spawn | `/*/*/ProcessSpawnLimitTests/*` | spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 3 |
