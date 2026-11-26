# Wrapper-only census identity checkpoints

Debug task `0cb46743`. Base `c83d14d4858a66e41e2c51c4631869db99d0a2c7`. Fix
`0ecf8ba031b0652f471002b975cecf159bcacedd` on `feat/card-task-0cb46743`.

The runner branch is fast-forward-only. No rebase. Land rebases.
`git merge-tree --write-tree origin/master HEAD` is reported and is not a merge.

No PC-n is named. Mutation execution is waived (operator 2026-11-12). SHORT tests only
(operator 2026-11-18). `WindowsUnitPlatformAdmissionTests` is named by the standing guard
union and is not a row: on Linux `C1210_Windows_linux_only_outcomes_are_exact` skips, and
a skip is not a pass.

## Verification design

- V-1. `LiveLaneRunnerTests.WrapperOnlyPaidManifest` completes every `C1545` census row.
  The isolated reader of a protected-input copy does not throw `required-identity-unavailable`.
  The other three methods in the class pass.
- V-2. A stale or partial core is still refused, and a complete core loaded from the wrong
  cache folder is still refused, with `nightly-core-identity-mismatch` and `start a fresh pwsh`.
  `AstraFixesBTests` covers that. `NightlyCoreTests` and `NightlyScriptsTests` stay green.
- R-1. Guard union once, without `WindowsUnitPlatformAdmissionTests` and without
  `DerivedImpactIndexTests`. `C1729LayeringGuardTests` stays at 0 violations and 162 listed.
- R-2. `DerivedImpactIndexTests` alone, after the regenerate driver is built. The index is
  not regenerated for this fix: `scripts/test-live-lane-run.ps1` is not a protected input
  and is not an index input.
- R-3. `scripts/audit-land-verification-graph.ps1 -Check`. No `-Update`.
- R-4. Recompute nightly pins. `policyHash` from `Read-NightlyExecutionPolicy` and
  `scripts/test-live-lane-run.ps1 -PrintInputDigest` equal the committed pin. No reseal
  when they already match.
- R-5. `scripts/check-evidence-diff.ps1` over `c83d14d4858a66e41e2c51c4631869db99d0a2c7..HEAD`,
  and `git merge-tree --write-tree origin/master HEAD` after `git fetch origin master`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-wrapper-identity/ | live-lane | `/*/*/LiveLaneRunnerTests/*` | V-1 | 4 executed, 0 failed | 4 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | all | CP-1 | nightly-core | `/*/*/NightlyCoreTests/*` | V-2 | 1 executed, 0 failed | 1 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | all | CP-1 | nightly-scripts | `/*/*/NightlyScriptsTests/*` | V-2 | 7 executed, 0 failed | 7 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | all | CP-1 | astra-b | `/*/*/AstraFixesBTests/*` | V-2 | 5 executed, 0 failed | 5 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | all | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-1 | executed, 0 failed; C1729 violations 0 listed 162 | 81 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | all | n/a | derived-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-2 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | all | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | R-2 | 27 executed, 0 failed | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | all | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-3 | exit 0, approved | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | all | n/a | nightly-pins | `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest` | R-4 | exit 0; printed digest equals PinnedInputDigest | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef c83d14d4858a66e41e2c51c4631869db99d0a2c7 -HeadRef HEAD` | R-5 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-11 | all | n/a | merge-tree | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-5 | exit 0, tree oid printed, no rebase | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 79 minutes (the `EstimatedMinutes` sum). CP-1 is the only `Antiphon.Tests` build. CP-6 is the regenerate driver. Windows behaviour is unobserved.
