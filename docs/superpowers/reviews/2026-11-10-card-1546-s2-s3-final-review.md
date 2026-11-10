# CARD-1546 S2+S3 final review

Verdict: clean. Round 1, Final. The commissioned selection ran with fresh identities and nonzero counts. `ordinaryScopeCompleted: Full`.

Reviewed source is Code task `3d1b7734-7506-4f9a-80c7-2233deb4365e` (Succeeded), ref `feat/card-task-3d1b7734`, tip `f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce`. Range `09d80f11a71cffa03edebc2866b561a8d0962fcb..f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce`. The source tree at that SHA was clean, and the checkpoint receipts record `buildSource=verified`. This review adds only this document on `feat/card-task-da37e430`.

Plan: `docs/superpowers/plans/2026-11-09-card-1546-server-git-project.md` at `b355a5fccc8c71f5026d73a45a952a9b62e9366e`.

Commits on the reviewed tip:

- `ebe86d30a952d0d6af625e544270fe7421dea2f7` S1 red witness
- `ba0b675faedb6041ad1f21615a01072e2d55076b` S2 move
- `19d4fb3feb00a051b5b9baf64b51404d6a008382` S2 land-graph refresh
- `e0a9d5d03bb95e26170c0f896b488076a9a5b2c8` S3 red witness
- `0762f63857b10499a5e1e701376e1a897b9f948b` S3 mirror and registration
- `f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce` S3 land-graph refresh

## Dependency direction

`src/Antiphon.Server.Git/Antiphon.Server.Git.csproj` has one `ProjectReference`, to `Antiphon.Observability`. `Antiphon.Observability.csproj` has no `ProjectReference`. `dotnet list reference` of the Git project in this session returned only Observability. The server csproj references the Git project. The only other direct reference is `tests/Antiphon.Server.Git.Tests`. `InternalsVisibleTo` names `Antiphon.Server`, `Antiphon.Server.Git.Tests`, and `Antiphon.Tests`. Assembly name is `Antiphon.Server.Git`.

No tracked `*.slnf` or `*.slnx`. `layers.txt` is unchanged. `tests/Antiphon.Tests/Layering/allowlist.txt` drops the six moved-file rows. The land-graph manifest digest moves from `3ED4A38C…` / 75 to `F799258C…` / 77. CP-5 and CP-10 both ran `scripts/audit-land-verification-graph.ps1 -Check` at the reviewed SHA and exited 0.

Images that publish the server copy the new project:

- Root `Dockerfile` has `COPY src/ src/` and publishes `server/Antiphon.Server.csproj`.
- `docker/session-runner-grok/Dockerfile` has `COPY . .` and publishes SessionRunner, PtyHost, and FakeGrok.
- `docker/tests/Dockerfile` has `COPY . .` and publishes `Antiphon.DockerStack.Fixture`.
- `src/Antiphon.Messaging.Service/Dockerfile` copies the messaging projects and publishes the messaging service.

No Dockerfile publishes the server from an explicit project list that omits `src/Antiphon.Server.Git`. A server `dotnet publish` and a server2 image build were left unrun; the COPY lines above are the docker evidence.

The plan slice table says the S2 csproj has no `ProjectReference`. The shipped project references Observability because `ServerGitProcess.Span` calls `AntiphonTrace`, which is the reference this review was told to accept. That sentence is advisory. It does not hold the land.

## Behaviour

`git diff -M --numstat` from the merge base to the tip is `0 0` for both `GitIndexLock.cs` and `ServerGitProcess.cs` (pure renames into `src/Antiphon.Server.Git`). Namespaces stay `Antiphon.Server.Application.Services` and `Antiphon.Server.Application.Dtos`. `GitIndexLock.PathsEqual` still uses `StringComparison.OrdinalIgnoreCase` on Windows and `StringComparison.Ordinal` otherwise. The Windows side is the pre-existing branch. This Linux run did not execute it, and it is not marked passed.

`LandingIndexLockObservation` moved out of `LandingDtos.cs` into the Git project with the same eight-line record and the same namespace. `GitIndexLockPathTests` stayed in `Antiphon.Tests`. The lock-test move deleted an unused `using`. `ServerGitAssemblyTests` gained `Test_project_does_not_reference_the_server`. No test assertion was weakened.

## Tests

Census at the tip: 7 methods moved with the lock tests, 2 path methods stayed in `Antiphon.Tests`, 12 `ServerGitProcessTests` methods stayed. The plan's "446 tests" figure is the spec placement estimate. S3's closed scope is the mirrored test project, the registration, and the hash recompute. `LandingGit` and the rest of `server/Infrastructure/Git` stay for the later slices the plan already names.

Checkpoint run `20261110-100925-7f01` on Debian 12, source `f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce`, `sourceState=clean`, `buildSource=verified`, fingerprint `65a547279571910eb82637f9572fbc811811167ef8a04201ef780d571ce140b0`. Validator: `CHECKPOINT SOURCE VALID` for the seven rows. Wall 12m49s. Four builds, one at a time. `unlisted: none` inside the tool.

```
CHECKPOINT CP-10 commit=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce build=n/a filter=pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-10 waited=0s executed=30.393s wall=30.439s
CHECKPOINT CP-5 commit=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce build=n/a filter=pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-5 waited=0s executed=30.588s wall=30.649s
CHECKPOINT CP-3 commit=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce build=ok filter=/*/*/ServerGitProcessTests/* executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-da37e430/.antiphon/checkpoints/20261110-100925-7f01/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=11.493s wall=11.528s
PHASES CP-3 slotWait=0s build=320.9631133s startup=7.9674025s testsWall=1.8056547s teardown=0.6910418s hostWall=10.4640388s
CHECKPOINT CP-6 commit=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce build=ok filter=/*/*/(ServerGitAssemblyTests*)|(GitIndexLockTests*)/(Declaring_assembly_is_Antiphon_Server_Git*)|(Project_does_not_reference_the_server*)|(Test_project_does_not_reference_the_server*)|(Classify_matrix*)|(C1436_census_drops_git_that_exits_mid_census*)|(C1436_census_keeps_live_holders*)|(Evaluate_path_error_is_held*)|(TryReclaimAfterKill_is_gone*) executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-da37e430/.antiphon/checkpoints/20261110-100925-7f01/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=0s executed=5.262s wall=5.311s
PHASES CP-6 slotWait=0s build=30.5143947s startup=1.9202772s testsWall=2.2223428s teardown=0.4074606s hostWall=4.5500806s
CHECKPOINT CP-8 commit=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce build=reused filter=/*/*/ImpactedClassesSelectorTests/seed_kinds_globs_and_owner_sentences_stay_present* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-da37e430/.antiphon/checkpoints/20261110-100925-7f01/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=0s executed=19.016s wall=19.101s
PHASES CP-8 slotWait=0s build=318.0458918s startup=7.7612735s testsWall=9.6650121s teardown=0.4442951s hostWall=17.8705808s
CHECKPOINT CP-9 commit=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce build=reused filter=/*/*/GitIndexLockPathTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-da37e430/.antiphon/checkpoints/20261110-100925-7f01/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=0s executed=8.553s wall=8.579s
PHASES CP-9 slotWait=0s build=0s startup=6.7431173s testsWall=0.4995045s teardown=0.7291429s hostWall=7.9717652s
CHECKPOINT CP-7 commit=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce build=ok filter=/*/*/(TestSelectionCatalogTests*)|(TestSelectionTests*)/(C1313_ProjectAndCatalogUniverse*)|(C1313_PinnedPolicy*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-da37e430/.antiphon/checkpoints/20261110-100925-7f01/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=32.682s wall=32.776s
PHASES CP-7 slotWait=0s build=0s startup=8.0413394s testsWall=23.2562806s teardown=0.4254244s hostWall=31.7230447s
```

CP-6 is 14 results because `Classify_matrix` has 7 argument rows. The plan Min of 8 is the method count. CP-3 is 23 results from the 12 stayed methods.

CP-1 stays the S1 red witness at `ebe86d30a952d0d6af625e544270fe7421dea2f7` (2 executed, 2 failed by design). CP-2 and CP-4 name classes that now live in the mirror; at this tip those filters would miss their mins. CP-6, CP-9, and `C1729LayeringGuardTests` are the successors.

Guard batch, one lease, `--no-build` on the kept CP-7 output `tests/Antiphon.Tests/bin-c1546-s3b`: 135 passed, 0 failed, 0 skipped. Reason: the plan's ten-class Review batch is not a checkpoint row.

| Class | Passed |
|---|---:|
| MechanicalPcContractTests | 11 |
| TaskPlatformGuidanceTests | 5 |
| C1729LayeringGuardTests | 9 |
| SlowTestRegistryMergeTests | 5 |
| ProcessSpawnLimitTests | 57 |
| SlowTestTripwireTests | 2 |
| TestClassificationPolicyTests | 41 |
| TestLaneCategoryGuardTests | 1 |
| NativeLinuxPolicyWitnessTests | 3 |
| TestClassificationGuardTests | 1 |

Hashes recomputed and matched: `policyHash` `568aae96ca07d5fb5cb5c54e62a5a4a02b1159ae6fd287de18ca9e8f2c0f2a52`, selection catalog `b0dfba6df314cf57bc89631806a29dbc1fb366def500fb77662710349c54742b`. `scripts/check-evidence-diff.ps1` over the range: commits=7, entries=0, violations=0.

`impacted-classes.ps1 -MergeSubset` on the same range: `migrations=false`, so the downgrade selector count is 0. Kinds: `contracts`, `land-graph-audit-project`, `linux-only-policy`, `nightly-live-lane`, `server-git-start-sites`, `spawn-limit-rosters`, `test-lane-category`. The Antiphon.Tests classes from those kinds that were outside the checkpoint rows were rerun on the same kept build, one lease, `--no-build`: `LandGraphAuditProjectInstanceTests`, `ReleaseGatePlatformTests`, `NightlyCoreTests`, `LiveLaneRunnerTests` — 12 passed, 0 failed. `server-git-start-sites` is CP-3 and CP-6. `contracts` selects E2E `ContractSnapshotTests` because `LandingDtos.cs` lost the moved record. The record's namespace is unchanged and the plan records no public HTTP route. That E2E class was outside the commissioned selection and was not run.

PC-1, PC-2, and PC-3 stay pending for method-scoped Mutation.

## Merge

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`. Current `origin/master` is `ac6d00022e940ee3b90584c7d313567b4491c498`. The brief pin `b067f912ae01234d744c5862cc90285b5859dc1a` is an ancestor, and the protected inputs are identical between that pin and this master. The merge-tree exits 1 with one content conflict: `scripts/test-live-lane-run.ps1`, the `PinnedInputDigest` line.

Protected inputs the branch changed: `tests/test-execution-policy.json` only. Since the merge base, master changed `scripts/lib/nightly-coverage.ps1`. The other four protected files are the same on both sides. Recomputing the script's digest over master's coverage file plus this branch's policy file plus the four shared files yields `08fb1f1eb83e4e8ef3a17a8ea98246745c79e318d4c4cdd48b7f79cea1ce5c4a`. The branch pin `a4a0e8d6fe4aae96b472ce0f56882586c4eead6455196b0525a3834b58c178ce` matches `-PrintInputDigest` on this tip. The master pin is `4bc09f3840a39436be6ca4035ea0946c01f96ae1883d0a7b4e5513132dbf74ec`. The land sets the pin to the recomputed merged digest.

## OS witness

`scripts/check-os-witnesses.ps1` over the range exited 0 with `NATIVE pending restartImpact`. The startup census is `scripts/lib/nightly-failure-history.ps1`, `scripts/lib/nightly-stopped-run-seed.ps1`, and `scripts/lib/release-authority.ps1` (suite-id allowlist edits). Windows host execution of that census stays pending. This land is not activation. The pending restart is inventory, and it is not marked passed.

## Land

Land the Code owner `3d1b7734-7506-4f9a-80c7-2233deb4365e` at `-ExpectedSourceSha f10db184a6b7c91ed4ab4c1a2e4b4c45c06555ce`. Resolve the pin by recomputing, as above. Leave PC-1..PC-3 for Mutation.
