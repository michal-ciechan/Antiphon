# CARD-1843 re-cut Code report (task 3b0e3e9e)

Outcome: done. The five commits of feat/card-task-3e96732b were cherry-picked (-x, clean, no conflicts)
onto master b02f110fb. The C1801 why-missed tuple now expects InProcessGitCommandsTests on its own
tests/Antiphon.GitInProcess.Tests row. All touched, impacted and guard rows are green at 944fff9b.

- Branch feat/card-task-3b0e3e9e, runner mirror /work/worktrees/task-3b0e3e9e. Original Code task 3e96732b, Review f9fbf3bd (clean).
- Picks: 572797752 (<- 04c94f179), 1a11a04e2 (<- 41e408523), 984ded3e5 (<- 7bc9cf12f), f0d09b5e1 (<- 0f109f8b6), 4d1b25be3 (<- 8d426d265, the 3e96732b report).
- Red SHA 4d1b25be3e3ed2e57fa65a4a5c4bf6c62e3a1d25 (the combined pick). Fix SHA 944fff9b35b504a12dab6a1e43621bfa801cbd08. Tested SHA 944fff9b35b504a12dab6a1e43621bfa801cbd08. The report commit that follows touches only .antiphon markdown.

## Fix
`tests/Antiphon.Tests/Scripts/ImpactedClassesSelectorTests.cs`: `WhyMissedTestsFilter` (the Antiphon.Tests row) drops
`(InProcessGitCommandsTests*)`. The new `WhyMissedGitInProcessFilter = "/*/*/(InProcessGitCommandsTests*)/*"` is asserted
exactly against `tests/Antiphon.GitInProcess.Tests`. No assertion was loosened, and no script or data changed.
`tests/test-selection-policy.json` already lists `tests/Antiphon.GitInProcess.Tests/Antiphon.GitInProcess.Tests.csproj` in
`remainderProjects`, from CARD-1830. `scripts/regenerate-derived-impact-index.ps1` reported the index unchanged at 944fff9b.
`tests/test-execution-policy.json` is untouched, so the policyHash and PinnedInputDigest were not refreshed. There is no migration.

## Red at the combined base (4d1b25be3, native build, leased)
The filter `/*/*/(ImpactedClassesSelectorTests*)|(DerivedImpactIndexTests*)/*` gave 68 total, 66 passed and 2 failed:
- `C1801_why_missed_paths_select_their_classes_and_windows_filters` failed for the intended reason. The selector emitted
  `(tests/Antiphon.GitInProcess.Tests, /*/*/(InProcessGitCommandsTests*)/*)`, but the constant expected that class on the
  Antiphon.Tests row.
- `regenerate_script_reports_changed_then_unchanged` failed for an environmental reason: the regen driver dll was not
  prebuilt. It was green in CP-1 after the regenerate script built the driver.

## Mutant (witness C1801_why_missed_paths..., built at 944fff9b, slot granted waited=0s)
M1: drop the GitInProcess csproj from `remainderProjects` in tests/test-selection-policy.json. The result was 1/1 failed: the
class moves back onto the Antiphon.Tests filter (ShouldContain at line 658). The file was restored with git checkout.

## Checkpoints
Plans are .antiphon/card-1843-checkpoints.md (CP-1 to CP-4) and .antiphon/card-1843-checkpoints-r2.md (CP-3b).
Run 20261112-020429-7aee ended with verdict RED exit=3, but only because of the CP-3 roster: the CP-3 filter named
SolutionShapeTests, which is only a fixture string literal in DerivedImpactIndexTests, so its class executed 0 tests. CP-3
still ran 253/253 green. This was a planning error in my filter, not a test failure. CP-1, CP-2 and CP-4 were green.
`validate` refuses that whole run (report_failed), so it has no receipt.
CP-3b in run 20261112-023944-06b0 is the same filter without the phantom class: GREEN. `validate` printed
`CHECKPOINT SOURCE VALID source=944fff9b35b504a12dab6a1e43621bfa801cbd08 rows=1`. CP-1, CP-2 and CP-4 were not rerun, so
they have no validated receipt. Their unedited lines are below.

CHECKPOINT CP-2 commit=944fff9b35b504a12dab6a1e43621bfa801cbd08 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=77 passed=77 failed=0 skipped=0 trx=/work/worktrees/task-3b0e3e9e/.antiphon/checkpoints/20261112-020429-7aee/rows/CP-2/run.trx slot=granted waited=225s dirty=0 source=944fff9b35b504a12dab6a1e43621bfa801cbd08 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=944fff9b35b504a12dab6a1e43621bfa801cbd08 build=ok filter=/*/*/C1677TempFamilyRegistryTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-3b0e3e9e/.antiphon/checkpoints/20261112-020429-7aee/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=944fff9b35b504a12dab6a1e43621bfa801cbd08 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=944fff9b35b504a12dab6a1e43621bfa801cbd08 build=ok filter=/*/*/(ImpactedClassesSelectorTests*)|(DerivedImpactIndexTests*)|(MergeHighRadiusSubsetTests*)/* executed=74 passed=74 failed=0 skipped=0 trx=/work/worktrees/task-3b0e3e9e/.antiphon/checkpoints/20261112-020429-7aee/rows/CP-1/run.trx slot=granted waited=165s dirty=0 source=944fff9b35b504a12dab6a1e43621bfa801cbd08 sourceState=clean buildSource=verified
CHECKPOINT CP-3b commit=944fff9b35b504a12dab6a1e43621bfa801cbd08 build=ok filter=/*/*/(AdditiveMigrationRuleWitnessTests*)|(AdvisoryChangePolicyTests*)|(CheckpointManifestDocumentationTests*)|(DockerStackDocumentationTests*)|(ExpectedDurationSyncTests*)|(GrokRunnerImageContractTests*)|(PerTaskFinalUnitLaneWitnessTests*)|(PipelineSpeedIntegrationTests*)|(ProviderEndpointProjectionTests*)|(RepositoryMutationLeaseTests*)|(ReviewTestQualityChecklistWitnessTests*)|(RunnerPushCredentialDocsTests*)|(Server2StagingServerContractTests*)|(StableBuildOutputDocumentationTests*)|(TestLaneCategoryGuardTests*)/* executed=253 passed=253 failed=0 skipped=0 trx=/work/worktrees/task-3b0e3e9e/.antiphon/checkpoints/20261112-023944-06b0/rows/CP-3b/run.trx slot=granted waited=0s dirty=0 source=944fff9b35b504a12dab6a1e43621bfa801cbd08 sourceState=clean buildSource=verified

Counts: CP-1 has 74 tests (ImpactedClassesSelector 42, DerivedImpactIndex 26, MergeHighRadiusSubset 6). CP-2 is the guard
union with 77 tests (TestClassificationPolicy 41, TestClassificationGuard 1, SlowTestRegistryMerge 5, SlowTestTripwire 2,
NativeLinuxPolicyWitness 3, MechanicalPcContract 11, TaskPlatformGuidance 5, C1729LayeringGuard 9). CP-3/CP-3b has 253
tests in the impacted Antiphon.Tests rows for base..HEAD. CP-4 runs the SessionRunner C1677TempFamilyRegistryTests, 3 tests.

## Other runs and checks
Unlisted runs:
- The red run and the native build at 4d1b25be3.
- The M1 mutant run.
- `scripts/regenerate-derived-impact-index.ps1`.
- One `dotnet run --project tools/Antiphon.Checkpoints -- import`. I ran it without a lease by mistake; the tool dll
  already existed. Later tool calls used the dll directly.

Checks:
- `check-evidence-diff.ps1 -Repository . -BaseRef b02f110fb -HeadRef 944fff9b`: commits=6, entries=1, violations=0.
- `git merge-tree --write-tree origin/master HEAD`: clean, exit 0. origin/master is b02f110fb.

The impacted selector for this diff also names phantom classes that are only fixture literals: AllLinuxOnly, ClassLinuxOnly,
Probe, WindowsSpecifics, OverrideTarget, UndeclaredOsToken, SolutionShape, and C1677TempFamilyRegistry on Antiphon.Tests.
This existed before this task (derived from string literals). It is advisory only and not changed here.

## Owed / pending for Mutation
- The 3e96732b PCs are still pending (method-scoped SourceLanding Mutation): PC-1 to PC-4 (M1 to M4 on
  ImpactedClassesSelectorTests/C1843_*) and PC-5 (the merge oracle AssertOnlyProjectsMoved).
- PC-6 (new): the exact GitInProcess.Tests ShouldContain at line 662 is not reached by the M1 mutant, which fails earlier at
  line 658. It needs a mutant that leaves the Antiphon.Tests row intact but corrupts the GitInProcess row, for example by
  adding a class to that row.
- Nothing ran on Windows. This diff has no windowsFilters.
