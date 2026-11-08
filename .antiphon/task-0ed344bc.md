# CARD-1790 Code repair (task 0ed344bc), Review 6a7c2ca6 D-1

Branch feat/card-task-0ed344bc, runner mirror /work/worktrees/task-0ed344bc, starts at f0e9ea9d3128d44f3bd09a00433de563a8790b46 (original Code task d6eca70e, landing owner).
Plan: docs/superpowers/plans/2026-11-08-card-1790-pwsh-private-cache-plan.md.

## D-1 fix

Commit 2a1c997b74fcfb02898c6cd431e84c94141f1583 changes only the plan. It replaces the three-column `### Checkpoints` table with the validated table from Review 6a7c2ca6 (CP / After / Build / Group / Filter / Covers / Expect / Min / EstimatedMinutes) and keeps the row descriptions as prose under the table. No production or test source changed. There is no red commit because this fixes a manifest. The witnesses' red commit is still c8c5be8e8b0de4ae7d59f32e9124179b046b69dc.

## Checkpoint tool (driven for real)

- `import --plan <plan>` (lease 2e63307e, waited=0s): imported 2 rows.
- `start --plan <plan>`, then `wait`: run 20261108-180654-a830, verdict GREEN exit=0, wall 8m49s, one build (bin-c1790-a, 337 s, deleted by the tool).
- `validate --evidence .antiphon/checkpoints/20261108-180654-a830/report.json --expected-source-sha 2a1c997b74fcfb02898c6cd431e84c94141f1583`: `CHECKPOINT SOURCE VALID source=2a1c997b74fcfb02898c6cd431e84c94141f1583 rows=2`, exit 0.

Unedited receipt lines:

```
source: 2a1c997b74fcfb02898c6cd431e84c94141f1583 state=clean buildSource=verified
CHECKPOINT CP-1 commit=2a1c997b74fcfb02898c6cd431e84c94141f1583 build=ok filter=/*/Antiphon.Tests.Scripts/PwshPrivateCacheTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-0ed344bc/.antiphon/checkpoints/20261108-180654-a830/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=2a1c997b74fcfb02898c6cd431e84c94141f1583 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=2a1c997b74fcfb02898c6cd431e84c94141f1583 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=75 passed=75 failed=0 skipped=0 trx=/work/worktrees/task-0ed344bc/.antiphon/checkpoints/20261108-180654-a830/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=2a1c997b74fcfb02898c6cd431e84c94141f1583 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
verdict: GREEN exit=0
```

## Release solution build

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1790-release-sln -- dotnet build Antiphon.sln -c Release -p:UseAppHost=false`, using native output at 2a1c997b74 (source identical to f0e9ea9d31). Lease 6c392993, waited=0s, maxcpucount=6, held=181s. Result: 0 Error(s), 2079 Warning(s), elapsed 00:02:59.79, exit 0. All 39 solution projects plus their fixture executables built. No CS0436 or duplicate-type error for `PwshPrivateCache`. The three new tests raise TUnit0015 ("Missing TimeoutAttribute cancellation token parameter"). Files in 179 other test files raise the same warning, so it is not a regression.

## Unlisted runs

- The tool `import` ran as a leased `dotnet run` of tools/Antiphon.Checkpoints. It is the tool build itself, not a test.
- The Release solution build was requested by the brief.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef f54741ef3bc08baa0493f9e0bebd2425acab6fb5 -HeadRef HEAD`: commits=4 entries=0 violations=0, exit 0.

## Master merge check

`git merge-tree --write-tree origin/master HEAD` against origin/master 5fef2c23b8 is clean (exit 0, no conflicts). Since the base, master changed Directory.Build.targets (be16e6c35b), src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj (fdaf40558f, 360f2a92a6) and tests/Antiphon.ScriptHarnessHost/Program.cs, and all of them auto-merge. The plan file is not on master, so no add/add conflict.

## V/R and PCs

The plan declares no V-n/R-n rows. The three witnesses (CP-1) pass. Their PCs (unset variable, shared name, skip delete) stay pending for method-scoped SourceLanding Mutation. No mutant was run in this round. Advisories A-1 to A-5 belong to CARD-1793 and are not this task's work.
