# CARD-1830: Antiphon.GitInProcess.Tests mirror project

Umbrella CARD-1729 (mirrored test projects). Census and order: the CARD-1546 plan
(`docs/superpowers/plans/2026-11-09-card-1546-server-git-project.md`, missing-mirror order row 2).
Pattern: CARD-1829 (`Antiphon.Resilience.Tests`): `Compile Include` links, a local assembly hook
file, no `DefineConstants`, and the sanctioned land-graph refresh as its own commit.

## Scope

- Move `InProcessGitCommandsTests` (5), `LibGit2VsGitDifferentialTests` (11),
  `LibGit2VsGitJunctionTests` (1), `LibGit2VsGitWorkingTreeVerdictTests` (8) and
  `LibGit2VsGitWorktreeListTests` (6), 31 `[Test]`, from `tests/Antiphon.Tests/Infrastructure` to
  `tests/Antiphon.GitInProcess.Tests`. Namespaces stay (`Antiphon.Tests.Infrastructure`).
- `ServerGitProcessTests` (12) and `LandingGitBatchingTests` (4) also import GitInProcess but their
  system under test is server git. They stay in `Antiphon.Tests` and become the members of the
  `git.inprocess` selection set.
- The mirror links its git fixtures (`LandingGitFixture`, `ScratchGitRepo`, `GitTreeTemplate`,
  `LandingEvidence`, both spawn limits, `LibGit2ConfigIsolation`) and the runner/pty guards from
  `Antiphon.Tests`, plus `tests/Shared/TestTiming.cs` and `TestTrace.cs`.
- `AssemblyHooks.cs` in the mirror (CARD-1729 decision 4): ProductionRunnerGuard,
  PtyBackendEnvGuard, CheckpointTempRootSweep and `LibGit2ConfigIsolation.IsolateLibGit2Config`.
  No test in the mirror touches the database, so there is no TestDbFixture lifecycle.
- `LibGit2ConfigIsolation` loses its `[Before(Assembly)]`; `LibGit2ConfigIsolationAssemblyHook.cs`
  keeps the attribute in `Antiphon.Tests`, which still reads through libgit2.
- `Antiphon.GitInProcess` and the server grant the mirror `InternalsVisibleTo` (the moved tests use
  `LandingGit.ParseRegistrations`, `LandingGit.PathsEqual` and `GitTreeVerdict.IsVerdictOnly`).
- Registration: solution, `git-in-process` suite (default, nightly, rc), project disposition,
  release-authority rc requirement, the suite lists in the nightly/rc scripts, the C1313 literals,
  `P-GITINPROCESS` rule and `git.inprocess` set, `gitinprocess-tests` impacted-classes kind, and
  `git-operations` rc category. The policyHash, live-lane pin and catalog hash move together.

## Slices

| Slice | Commit content |
|---|---|
| S1 | `GitInProcessAssemblyTests` in `Antiphon.Tests` (red) |
| S2 | the move, mirror project, hooks, registration and pins |
| S3 | derived impact index regeneration; the land-graph refresh script's own commit |

## Tests

| Id | When | Assertion |
|---|---|---|
| V-1 | red at S1, green from S2 | `Mirror_holds_the_gitinprocess_tests` |
| V-2 | red at S1, green from S2 | `No_GitInProcess_test_remains_in_Antiphon_Tests` |
| V-3 | red at S1, green from S2 | `Assembly_hook_is_local_to_the_mirror` |
| V-4 | green from S2 | the 31 moved methods run in the mirror |
| V-5 | green from S2 | the consumers that stayed (`ServerGitProcessTests`, `LandingGitBatchingTests`, `LandingSourceOptionalLockTests`) |
| R-1 | green from S2 | the guard sweep and the impacted-classes rows of this diff |
| R-2 | green from S2 | the policy-script cases whose literals moved |
| R-3 | S3 | `audit-land-verification-graph.ps1 -Check` exit 0 |

`LibGit2VsGitWorkingTreeVerdictTests.Status_and_ls_files_verdicts_match_git` has three Linux cases
red at base 3db1a43b0 in `Antiphon.Tests` (`long-path-untracked`,
`same-size-edit-mtime-restored-settled`, `same-size-edit-mtime-restored-inside-the-index-second`):
MAX_PATH and NTFS stat semantics. CP-4 runs that method alone.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests/Antiphon.Tests.csproj -> bin-c1830-s1/ | witness-red | `/*/*/GitInProcessAssemblyTests/*` | V-1, V-2, V-3 | red: all three methods failed, executed at least 3. A build error or zero tests is not this witness | 3 | 12 |
| CP-2 | S2 | tests/Antiphon.GitInProcess.Tests/Antiphon.GitInProcess.Tests.csproj -> bin-c1830-s2/ | mirror | `/*/*/(GitInProcessAssemblyTests*)\|(InProcessGitCommandsTests*)\|(LibGit2VsGitDifferentialTests*)\|(LibGit2VsGitJunctionTests*)\|(LibGit2VsGitWorktreeListTests*)/*` | V-1, V-2, V-3, V-4 | 0 failed | 26 | 8 |
| CP-3 | S2 | CP-2 | mirror-verdict | `/*/*/LibGit2VsGitWorkingTreeVerdictTests/(Index_and_tree*)\|(The_runner*)\|(A_verdict*)\|(A_runner*)\|(A_status*)` | V-4 | 0 failed; seven methods, argument rows included | 25 | 6 |
| CP-4 | S2 | CP-2 | mirror-verdict-inherited | `/*/*/LibGit2VsGitWorkingTreeVerdictTests/Status_and_ls_files_verdicts_match_git` | V-4 | Linux: exactly the three inherited cases red, 42 green; Windows: 0 failed. Own run | 45 | 6 |
| CP-5 | S2 | tests/Antiphon.Tests/Antiphon.Tests.csproj -> bin-c1830-s2b/ | consumers | `/*/*/(ServerGitProcessTests*)\|(LandingGitBatchingTests*)\|(LandingSourceOptionalLockTests*)/*` | V-5 | 0 failed | 29 | 10 |
| CP-6 | S2 | CP-5 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/*` | R-1 | 0 failed | 20 | 8 |
| CP-7 | S2 | CP-5 | impacted | `/*/*/(TestLaneCategoryGuardTests*)\|(ImpactedClassesSelectorTests*)\|(LiveLaneRunnerTests*)\|(NightlyCoreTests*)\|(LandGraphAuditProjectInstanceTests*)\|(LandGraphLandRefreshTests*)\|(LandNonCodeSkipTests*)\|(DotnetToolManifestContractTests*)\|(PostLandEarlyWarningTests*)\|(TimeoutAuditTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(DerivedImpactIndexTests*)/*` | R-1 | 0 failed | 50 | 25 |
| CP-8 | S2 | CP-5 | impacted-dispatch | `/*/*/(AgentTaskDispatchBaseGuardTests*)\|(DispatchConcurrencyAdmissionTests*)/*` | R-1 | 0 failed | 20 | 15 |
| CP-9 | S2 | CP-5 | impacted-land-graph | `/*/*/LandGraphEvaluationTests/*` | R-1 | 0 failed | 20 | 20 |
| CP-10 | S2 | CP-5 | policy-scripts | `/*/*/(ReleaseBranchModelGateTests*)\|(ReleaseGatePlatformTests*)\|(TestSelectionCatalogTests*)\|(TestSelectionTests*)\|(ChangeAwareReleaseTests*)\|(RcFailedFirstExecutionTests*)\|(RcFailureHistoryTests*)/(C1311_LongLivedRcRef*)\|(C1323_PlatformPolicy*)\|(C1313_ProjectAndCatalogUniverse*)\|(C1313_PinnedPolicy*)\|(C1313_ModeCredit*)\|(ImportedAuthorityFixtureInitializesTiming*)\|(EvidenceUnion*)` | R-2 | 0 failed | 7 | 15 |
| CP-11 | S3 | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-3 | exit 0 | n/a | 6 |

CP-1 is reported red by design and is not rerun. CP-4 runs alone because a red row fails the whole
receipt. CP-3, CP-4 reuse CP-2; CP-6 to CP-10 reuse CP-5. Builds use `UseAppHost=false` on Linux.

## Positive controls (pending for Mutation)

| PC | Mutant | Expected red |
|---|---|---|
| PC-1 | drop `<InternalsVisibleTo Include="Antiphon.GitInProcess.Tests" />` from `Antiphon.GitInProcess.csproj` | V-1 (or the mirror build, which is not a red) |
| PC-2 | copy one moved class back into `tests/Antiphon.Tests/Infrastructure` | V-1 and V-2 |
| PC-3 | add `using Antiphon.GitInProcess;` to another `Antiphon.Tests` file | V-2 |
| PC-4 | delete the `LibGit2ConfigIsolation.IsolateLibGit2Config` call from the mirror's `AssemblyHooks.cs` | V-3 |
| PC-5 | put `[Before(Assembly)]` back on `LibGit2ConfigIsolation.IsolateLibGit2Config` | V-3 |
| PC-6 | remove `LibGit2ConfigIsolationAssemblyHook.cs` | V-3 |
