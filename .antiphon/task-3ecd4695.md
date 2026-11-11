# CARD-1842 S1 review

Verdict: CLEAN. Round 1, Final, ordinary scope Full. No product blocker.

Reviewed source task: 74e1f474-ef49-4c50-806b-6d8cfb8c2a7f.
Reviewed tip: 8446dd7f8b97e58b7c2762ee6e0c56510a602ffd on feat/card-task-74e1f474.
Range: ef7afd44927206f6c03547670a5add84130bdce9..8446dd7f8b97e58b7c2762ee6e0c56510a602ffd.
Commits: 9f4542bcf plan, 49c9805d5 tests only, 8446dd7f8 fix. Refresh script numstat +82/-7.

## Behaviour

`scripts/refresh-nightly-class-costs.ps1` requires `-HostId`, `-Os` (`windows` or `linux`), and `-MeasuredRunId`. Blank host or run throws `class-cost-host` or `class-cost-run`. Any other `-Os` throws `class-cost-os`. The script does not read the machine name.

When `-Os` equals the file platform, a refreshed row updates `nodes`, `seconds`, and `basis=trx`, copies that row's `source`, and stores `hostId`, `os`, and `measuredRunId`. A class absent from the TRX is copied whole. When `-Os` differs, primary `seconds`, `nodes`, `basis`, `source`, and file `platform` stay, and the sample is `otherSample`. A class that is not already in the file is not added.

`Get-NightlyClassCostTable` in `scripts/lib/nightly-tests-impl.ps1` still prices `seconds / nodes` only. `NightlyLaunchPlan.ClassCost` receives that reduced table. `plannedSeconds` is the packed chunk's `seconds` from `Get-NightlyLaunchClassCost`. `otherSample` is not read by the packer. `ReadCosts` uses `basis` and `seconds` only, so an extra property does not throw and a missing `hostId` does not.

The committed cost file was not rewritten. Probe at this tip: 772 classes, format `antiphon-nightly-class-costs-v1`, platform `windows`, every row has `nodes`, `seconds`, and `basis`, zero `hostId`, zero `os`, zero `measuredRunId`, zero `otherSample`. The table loaded 772 prices. `Antiphon.E2E.AgentE2ETests` is 600/2 = 300 per node. Existing rows without the new fields are tolerated.

Callers: the only executable invocation added in this range is `scripts/test-class-cost-measurement.ps1`. No match under `.claude/`. Windmill `u/lndcobra/antiphon_nightly_tests` ssh's `scripts/nightly-run.ps1`, and that script does not call the refresh. The committed file's `refresh` string still omits the three required parameters. That string is not executed. Copying it fails closed on missing `HostId`. The script writes the updated hint on the next refresh. S1's plan says not to rewrite the JSON.

`-SetPlatform` is implemented. It sets file platform only when the file has at least one row and every stored primary `os` equals `-Os`. The plan states S1 passes it in no witness. On the committed file no row has `os`, so the switch cannot change `platform`.

## Checks

CP-1, checkpoint tool run 20261111-001023-d2e9, receipt validated:

CHECKPOINT CP-1 commit=8446dd7f8b97e58b7c2762ee6e0c56510a602ffd build=ok filter=/*/*/ClassCostMeasurementTests/(RefreshKeepsRowSource*)|(RefreshRequiresHostOsAndRun*)|(ForeignOsDoesNotReplaceSeconds*)|(PackerStillReadsPrimarySeconds*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-3ecd4695/.antiphon/checkpoints/20261111-001023-d2e9/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=8446dd7f8b97e58b7c2762ee6e0c56510a602ffd sourceState=clean buildSource=verified

CHECKPOINT-TIMING CP-1 waited=0s executed=53.264s wall=54.074s. Build 581s, tests wall 34s, run wall 12m18s. `validate-checkpoint-receipt.ps1` exit 0, CHECKPOINT SOURCE VALID. The filter matches the plan's CP-1 row (V-1 through V-4). Min 4. No unlisted tool command.

Guard and impacted batch, one leased `dotnet run --no-build` after one leased native build (`UseAppHost=false`, no custom output). Filter is the nine-class Code/Review recipe plus `ProcessSpawnLimitTests` (impacted `spawn-limit-rosters` and `test-lane-category`):

`/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(ProcessSpawnLimitTests*)/*`

TRX `.antiphon/c1842-review-guards/guards.trx`: executed=135 passed=135 failed=0 skipped=0. Slot granted, waited 0s. By class: TestClassificationPolicyTests 41, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, MechanicalPcContractTests 11, TaskPlatformGuidanceTests 5, C1729LayeringGuardTests 9, TestLaneCategoryGuardTests 1, ProcessSpawnLimitTests 57. The nine-class union is 78. The five-class policy sweep is 52.

`scripts/check-evidence-diff.ps1` over the range: commits=3 entries=0 violations=0, exit 0.
`git merge-tree --write-tree origin/master HEAD`: exit 0, tree 4113a01ab5767438e83a30c0fd84c7b43ef4a263. origin/master is 584a169521a763d682da8313a9bc84f3c24583fb.
Impacted kinds: spawn-limit-rosters, test-lane-category. Migrations kind did not match, so the downgrade selector selected 0 callers.
No session input wait changed. The script is not on the AppHost startup path.
OS: no `OperatingSystem` or `RuntimeInformation` branch. `-Os windows` and `-Os linux` both go through the real script in CP-1. Runner defaults and session-runner routes were read (HTTP 200). No host pin.

Red commit 49c9805d5 adds only the witnesses, against a script that still dropped row `source` and had no identity parameters. The four plan mutants were not re-run. They stay pending for method-scoped SourceLanding Mutation:

- V-1 delete the source copy (`RefreshKeepsRowSource`)
- V-2 default `-HostId` from the machine name (`RefreshRequiresHostOsAndRun`)
- V-3 let a linux refresh overwrite `seconds` (`ForeignOsDoesNotReplaceSeconds`)
- V-4 price from `otherSample` (`PackerStillReadsPrimarySeconds`)

## Advisories

These do not hold the land.

- `-SetPlatform` has no S1 witness, as the plan states. A later witness should show agreement changing platform and one disagreeing row leaving it.
- The committed `scripts/lib/nightly-class-costs.json` `refresh` hint still omits `-HostId`, `-Os`, and `-MeasuredRunId`. S1 does not rewrite that file. No live caller uses the hint. The next refresh rewrites it, and a copied old command fails closed.

S2 through S7, and the Windows read of run 6591e378, are later slices.
