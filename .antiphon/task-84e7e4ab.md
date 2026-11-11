# CARD-1829 Code report (task 84e7e4ab)

Outcome: the Resilience tests now live in `tests/Antiphon.Resilience.Tests` (suite id `resilience`). Namespaces stayed `Antiphon.Tests.Infrastructure.Resilience`. The witness, the touched classes, and the guard batch are green. PC-1 and PC-2 stay pending for Mutation.

Branch `feat/card-task-84e7e4ab`. Worktree `/work/worktrees/task-84e7e4ab`. Base `584a169521a763d682da8313a9bc84f3c24583fb`. No checkpoint table exists for this card, so the checkpoint tool was not run. Nothing was deployed. Restart: none.

## SHAs

- Red witness: `376e921c6df77db919da164ed1d4904187e988c1`
- Mirror and suite registration: `dab412a0c3f185197152aeabf2b5f00bbb036089`
- EF Relational 9.0.21 pin: `d59528a2f848dda7b03dfe02e9daf45dc6672db5`
- Support link (tested tip for guards): `15a9844d0e3dedaea8d37a5831aae0c8e15e2c91`

## Hashes

- policyHash `7a7d65408ed9956188efb31dd7da7ad62082e864456404266c93c96aec5d650c` -> `4ab004cf02e2d8adb80f4744030bd23fae3e2fd9b2cafff94822ba43b7bc5719`
- catalogHash `b0dfba6df314cf57bc89631806a29dbc1fb366def500fb77662710349c54742b` -> `f4aa926ae5d4ba930718c71b097c04316e1855431c35ba5d22de072749df68ec` (same value in `tests/test-selection-policy.json` catalogHash and `tests/test-execution-policy.json` selectionCatalog.hash)
- PinnedInputDigest `ad242a47536a59cb00a5eb83c7cf4580a4981b08f813814f31f02fa24a6dedaa` -> `d54af40756ac84a442b0c65b8b515c6933a2f4be024d4c94c32be8a979e33c27` in `scripts/test-live-lane-run.ps1`

The EF pin and the support link do not change those inputs.

## Tests

Red witness on `376e921c6`, filter `/*/*/ResilienceAssemblyTests*/*`, `--no-build`, UseAppHost=false, TEMP=/tmp. Slot lease=`6597c5a2-6f26-4257-8d50-c35f9c89611b` waited=0s held=15s. total 2, failed 2, succeeded 0.

Witness green against the sources committed as `dab412a0c`. Same filter. Slot lease=`012991be-91bb-4b64-8ffd-5eb90795cfd5` waited=0s held=6s. total 2, failed 0, succeeded 2. Not rerun.

Touched classes after `dab412a0c` and before the EF pin: total 58, failed 58. BeforeAssembly `FileNotFoundException` for `Microsoft.EntityFrameworkCore.Relational` 9.0.20. The mirror output had 9.0.1. Slot lease=`f11a8620-ce42-4ac2-8fd2-93510a0fcf65` waited=0s held=13s.

EF rebuild slot lease=`202cf5a5-030d-4cc3-bea8-af4d33626e7d` waited=0s held=80s, 0 errors. Output Relational assembly 9.0.21.0.

Touched classes after `d59528a2f`. Filter `/*/*/(DatabaseReadResilienceTests*)|(HttpReadResilienceTests*)|(HttpResilienceRegistrationTests*)|(ResilienceBudgetTests*)|(ResilienceCircuitTests*)|(ResilienceOptionsAndTelemetryTests*)|(ResiliencePredicateTests*)|(ResilienceAssemblyTests*)/*`. Slot lease=`0b5505c9-1bd4-4034-a3d8-32aba39cb6fb` waited=0s held=30s. total 58, failed 0, succeeded 58, skipped 0, duration 27s 862ms.

`Antiphon.Tests` then failed to build: `AgentSessionLaunchFailureTests.cs` CS0234 because `CollectingLoggerProvider` had moved with `ResilienceTestSupport.cs`. Slot lease=`87ca0e9d-f9df-4dbf-bc6a-5fd6818b384f` waited=0s held=138s, 1 error. Fix: compile-link that support file into `Antiphon.Tests`. The file stays under `tests/Antiphon.Resilience.Tests`, so the witness source census is unchanged. Rebuild slot lease=`5117a00e-9eaf-458a-a6fe-619b4a79f1a3` waited=0s held=111s, 0 errors, committed as `15a9844d0`.

Guards on `15a9844d0`, one run, UseAppHost=false, TEMP=/tmp. Filter `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(DerivedImpactIndexTests*)|(C1514LandingSplitBudgetTests*)/*`. The class wildcard is the pinned OR form. It includes `C1584_E2EExpandedBudget` and the other four methods of that class. `TestLaneCategoryGuardTests` was not added. Slot lease=`91b813f1-fd45-4b3a-bbd4-a89e43f2c269` waited=0s held=827s. total 106, failed 0, succeeded 106, skipped 0, duration 13m 42s 701ms.

TRX class counts, all passed: TestClassificationPolicyTests 41, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, MechanicalPcContractTests 11, TaskPlatformGuidanceTests 5, C1729LayeringGuardTests 9, DerivedImpactIndexTests 24, C1514LandingSplitBudgetTests 5. `C1584_E2EExpandedBudget` outcome Passed.

## Evidence and merge-tree

`scripts/check-evidence-diff.ps1 -BaseRef 584a169521a763d682da8313a9bc84f3c24583fb -HeadRef HEAD` exit 0. commits=4 entries=0 violations=0.

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` exit 1. origin/master `188ac942166884f060fdcc7cbf3b15f670a27b43`. Conflict only in `tests/derived-impact-index.json`. `ProductionRunnerGuard.cs` auto-merged. Not merged. Land rebases and should regenerate the index.

## Pending positive controls

Mutation owns the red/restore/green cycles. They were not run.

- PC-1: move one moved class declaration back into `tests/Antiphon.Tests`, or delete the mirror csproj, so `Mirror_holds_the_resilience_tests` fails. Filter `/*/*/ResilienceAssemblyTests/Mirror_holds_the_resilience_tests*`.
- PC-2: remove `[Before(Assembly)]` from `AssemblyHooks` (or one of the named calls) so `Assembly_hook_is_local_to_the_mirror` fails. Filter `/*/*/ResilienceAssemblyTests/Assembly_hook_is_local_to_the_mirror*`.
