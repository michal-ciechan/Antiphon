# CARD-1829 Final Review (task d85d2e34)

Verdict: CLEAN. Round 1, Final, ordinary scope Full. No blocker. Positive controls stay pending.

Subject is the Code task `84e7e4ab-d4ad-4fa1-8cf9-8a3c7cab48f8` on `feat/card-task-84e7e4ab`, tip `5681114763f6ac7a26e154d9a5888ad89aa343ee`. Reviewed range `584a169521a763d682da8313a9bc84f3c24583fb..5681114763f6ac7a26e154d9a5888ad89aa343ee`. The review checkout was that tip with a clean tree. The repository is not shallow. Code tested `15a9844d0e3dedaea8d37a5831aae0c8e15e2c91`; the report commit is the reviewed tip and does not change the pins.

There is no `### Checkpoints` table for this card, so the checkpoint tool was not the vehicle. The commissioned selection is the inventory, the nightly/rc registration, the EF pin, the three hashes, merge-tree, evidence-diff, the resilience suite, and the guard batch. That selection ran.

## No test lost

Base `tests/Antiphon.Tests/Infrastructure/Resilience` has 55 `[Test]` methods. `Pump_stops_at_the_owner_deadline_when_the_continuation_is_gated` has `[Arguments(200)]` and `[Arguments(700)]`, so the base executes 56 rows. The tip mirror keeps every one of those rows. The old directory has no `.cs` files. The only additions are `ResilienceAssemblyTests.Mirror_holds_the_resilience_tests` and `ResilienceAssemblyTests.Assembly_hook_is_local_to_the_mirror`. That is 57 `[Test]` lines and 58 executed rows. The witness counts `[Test]` lines and expects 57. Moved files are renames. No moved class declaration remains under `tests/Antiphon.Tests`.

## The suite is registered to run

`resilience` is a primary unattended `native-dotnet` suite whose project is `tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj`. It is in `defaultSuites`, `profiles.nightly.requiredSuites`, and `profiles.rc.requiredSuites`. With observability present the live counts are primary 10, nightly required 10, rc required 12, and the release-authority required list is the same 12, including `resilience`. `Antiphon.sln` nests the project beside `Antiphon.Server.Git.Tests`. `tests/rc-project-categories.json` has a `shared-runtime` row. `src/Antiphon.Resilience` grants `InternalsVisibleTo` the mirror. The suite id is on the same script allowlists that already name `server-git`. `Get-NightlyFallbackSuiteUniverse` still returns the original seven suites and also omits `server-git` and `observability`; that path is the fallback, not the profile path.

`AssemblyHooks` calls the four CARD-1729 decision-4 hooks: `ProductionRunnerGuard`, `PtyBackendEnvGuard`, `CheckpointTempRootSweep.SweepOnce` unless `C804_ORPHAN_SWEEP_ROOT` is set, then `TestDbFixture`. The mirror defines `ANTIPHON_RESILIENCE_MIRROR`, and the linked helpers strip their own assembly attributes under that define. It does not call `RecordSelectedRoster` or `TestWorkerModes`. Those stay with `Antiphon.Tests`. That omission is an advisory.

## EF Relational 9.0.21

The mirror pins `Microsoft.EntityFrameworkCore.Relational` 9.0.21, the same pin `Antiphon.Tests` and the E2E project already used. The server project pins `Microsoft.EntityFrameworkCore` 9.0.21. After the leased mirror build, the mirror output `Microsoft.EntityFrameworkCore.Relational.dll` is 9.0.21.0 and the server output `Microsoft.EntityFrameworkCore.dll` is 9.0.21.0. The server output Relational assembly is 9.0.20.0 because the server does not reference Relational directly; 9.0.20 arrives transitively from Design 9.0.20. The pre-pin failure was FileNotFound for 9.0.20 while the unpinned mirror had copied 9.0.1. The 9.0.21 pin continues the previous test host. `Antiphon.Tests` still warns MSB3277 inside `Antiphon.DockerStack.Fixture` (Relational 9.0.1 chosen versus 9.0.20 from `Antiphon.Server`). That warning is pre-existing and is not this pin.

## Pins

Recomputed at the reviewed tip, and accepted by the readers:

- policyHash `4ab004cf02e2d8adb80f4744030bd23fae3e2fd9b2cafff94822ba43b7bc5719`
- catalogHash `f4aa926ae5d4ba930718c71b097c04316e1855431c35ba5d22de072749df68ec`
- PinnedInputDigest `d54af40756ac84a442b0c65b8b515c6933a2f4be024d4c94c32be8a979e33c27`

Commits after the registration commit `dab412a0c3f185197152aeabf2b5f00bbb036089` do not touch the hashed inputs. Fetched `origin/master` is `188ac942166884f060fdcc7cbf3b15f670a27b43`. That master still carries the pre-change pins (`7a7d65408ed9956188efb31dd7da7ad62082e864456404266c93c96aec5d650c`, `b0dfba6df314cf57bc89631806a29dbc1fb366def500fb77662710349c54742b`, `ad242a47536a59cb00a5eb83c7cf4580a4981b08f813814f31f02fa24a6dedaa`). The protected inputs are unchanged between the branch base and that master, so the branch pins are already the combined-tree values. Recompute all three only if master changes a protected input before land.

## Merge-tree and evidence

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` exits 1. The only conflict is `tests/derived-impact-index.json`. `ProductionRunnerGuard.cs` auto-merges. The worktree was not merged. Merge regenerates the index with `DerivedImpactIndexBuilder.Build/Write`. This branch does not edit `server/Application/Services/LandVerificationBuildGraph.manifest.txt`. A new csproj makes that manifest stale; `scripts/land-graph-land-refresh.ps1` refreshes it at land. `-Update` was not run.

`scripts/check-evidence-diff.ps1 -BaseRef 584a169521a763d682da8313a9bc84f3c24583fb -HeadRef HEAD` exits 0: commits=5, entries=1, violations=0. The entry is `.antiphon/task-84e7e4ab.md`.

## Runs at 5681114763f6ac7a26e154d9a5888ad89aa343ee

All four commands used `scripts/build-slot.ps1`, `UseAppHost=false`, `TEMP=/tmp`, and the worktree's default output. The tree was clean at the SHA.

| Run | Lease | Result |
|---|---|---|
| `dotnet build tests/Antiphon.Resilience.Tests` | `42eafebd-9b5b-47b9-bb12-8eab2021b9a0` held 102s | 0 errors. Elapsed 00:01:41.69 |
| `dotnet run --no-build` resilience project | `eb0b4582-e032-4e07-8459-9452e584a70e` held 38s | total 58, failed 0, succeeded 58, skipped 0, 33s 841ms. TRX `/tmp/card-1829-resilience-trx/_6f03ea7dd3f9_2026-11-11_01_26_46.7414616.trx` |
| `dotnet build tests/Antiphon.Tests` | `d17ae471-7928-4721-a356-85542500c2c6` held 240s | 0 errors, 2012 warnings. Elapsed 00:03:58.57 |
| Guard `dotnet run --no-build` | `32aa8663-28e0-490b-ac9c-5a942cfe0315` waited 0s, held 749s | total 107, failed 0, succeeded 107, skipped 0, 12m 24s 682ms. TRX `/tmp/card-1829-guards-trx/_6f03ea7dd3f9_2026-11-11_01_45_22.0385879.trx` |

Guard filter, the documented nine-class batch plus the touched index and the class Code ran:

`/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(DerivedImpactIndexTests*)|(C1514LandingSplitBudgetTests*)/*`

TRX class counts, all passed: TestClassificationPolicyTests 41, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, MechanicalPcContractTests 11, TaskPlatformGuidanceTests 5, C1729LayeringGuardTests 9, TestLaneCategoryGuardTests 1, DerivedImpactIndexTests 24, C1514LandingSplitBudgetTests 5.

## Advisories (do not hold the land)

- `Get-Project` in `scripts/impacted-classes.ps1` still returns `tests/Antiphon.Tests` for `tests/Antiphon.Resilience.Tests/`. The same gap exists for `Antiphon.Server.Git.Tests` on master. A kind hit would emit the filter against the wrong project. Full nightly and rc runs use `requiredSuites`.
- `remainderProjects` is copied into the selection result and no executor reads it. CARD-1546 uses the same shape. Narrow selection runs the named members; the full profiles run the suite because it is required.
- The card text asked for checkpoint rows. The plan has no closed table, so none were added. This round ran the named selection instead.
- PCs stay pending. PC-1: move one class back or delete the mirror project so `Mirror_holds_the_resilience_tests` fails (`/*/*/ResilienceAssemblyTests/Mirror_holds_the_resilience_tests*`). PC-2: remove `[Before(Assembly)]` so `Assembly_hook_is_local_to_the_mirror` fails (`/*/*/ResilienceAssemblyTests/Assembly_hook_is_local_to_the_mirror*`).

## Merge handoff

Regenerate `tests/derived-impact-index.json`. That is the only merge-tree conflict against `origin/master` `188ac942166884f060fdcc7cbf3b15f670a27b43`. Recompute policyHash, catalogHash, and PinnedInputDigest only if master changes a protected input before land. At that master the branch pins are already the combined values. Land refreshes the land-graph manifest.
