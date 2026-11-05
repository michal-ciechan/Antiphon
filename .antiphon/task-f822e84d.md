# CARD-1708 salvage refs: Code report (task f822e84d)

Outcome: done. Before an S4a system release removes a tree, each commit that only the tree's reflogs reach now gets a salvage ref. If that fails, the removal is refused, and the tick prunes salvage refs by age. All four required witnesses (five tests) were red at the red SHA, are green at the fix SHA, and each one goes red under its own mutant. CP-1 and CP-2 are green. CP-3 is red only on load timeouts in C1677RetentionServerTests and C0824WorkspaceSlotServerTests; the base run under the same load shows the same 77 failures.

Branch feat/card-task-f822e84d, worktree /work/worktrees/task-f822e84d, stacked on 88e9fc746c78503ef84c3f183bb93ea5ab713e75. Original Code task (landing owner) f822e84d-ef5c-44d0-ba24-05580ee5fadf. Operator decision 2026-11-05 option (b) with salvage refs.

## SHAs

| SHA | What |
|---|---|
| 5b181db47b56dd0e4a3faa904edf7dca8451e5e6 | **red SHA**: C1708ReflogSalvageTests, 5/5 red |
| 4f3164999f557ff07cb7dbec10354aedd87b09ad | **fix SHA = tested SHA** |

## Design

- `server/Infrastructure/Git/WorktreeReflogSalvage.cs` (new):
  - **Candidates:** the tree's HEAD reflog, its HEAD, and the branch reflog.
  - **Reachability:** a candidate needs a ref when `rev-list <candidates> --not --branches --tags --remotes` lists it (in batches of 200). The set is then reduced with `merge-base --independent`.
  - **Bound:** more than 64 refs refuses with `reflog_salvage_over_limit` (more than 256 unreachable candidates refuses before the reduction).
  - **Switch reads:** the live switch is read before the first write. The writes then run inside `LandingGit.GateDestructiveStep`, so the switch is also read as each `update-ref` child starts.
  - **Writes:** `update-ref --create-reflog refs/antiphon/salvage/<taskId D>/<sha> <sha>`. Writing the same ref again is a no-op and adds no reflog entry.
  - **Check:** the refs are verified with `for-each-ref`.
  - **Failures:** any read or write failure gives `reflog_salvage_failed`.
- `GuardedWorktreeRemoval`: when `request.SalvageScope` is set, the salvage runs after the D-2 late-untracked check and before `set_aside_unrecorded` / BindContent / the switch read / the move-aside. A refusal returns while nothing has moved. The B-1/B-2 code is unchanged.
- `TaskWorktreeRetirementService`: `SalvageScope` is set only for `system:desktop-legacy-policy` releases, including residue-sweep retries. Caller-filed, publication and land removals are unchanged.
- `DesktopLegacyReleasePolicyService`: an executing tick that the switch did not stop prunes, in each repository that has a system retirement row, salvage refs whose newest reflog entry is at least `SalvageRefRetentionDays` old. It reads at most 200 refs per pass and deletes with `update-ref -d <ref> <sha>`. A ref with no readable time is kept. Dry runs and previews prune nothing.
- `WorktreeLegacyReleaseSettings.SalvageRefRetentionDays`: default 30, validated to 1..3650. It exists in code only: appsettings.json is not changed and no host setting is changed. Docs: docs/orchestration-loop.md (automatic desktop release paragraph).

## Witnesses (C1708ReflogSalvageTests) and mutants

All five are red at 5b181db47 and green at 4f3164999. Mutants were env-gated in one uncommitted build, run with one lease per run, then restored with `git checkout --`.

| Witness | Red at base | Mutant | Mutant result |
|---|---|---|---|
| (i) `Released_tree_keeps_its_reflog_only_commit_under_a_salvage_ref` | refs `[]` | M1: salvage returns success before any write (revert) | 5/5 red |
| (ii) `Salvage_failure_refuses_the_removal` | tree removed (snapshot) | M2: GuardedWorktreeRemoval ignores the salvage result | only (ii) red (:58) |
| (iii) `Off_switch_writes_no_salvage_ref` (control half: switch on salvages) | refs `[]` at the control | M5: prune also runs when execute=false | only (iv) red, at "a dry run prunes nothing" (:150) |
| (iii) `Late_switch_at_the_salvage_write_writes_no_ref_and_keeps_the_tree` | fired=0, tree removed | M3: no live gate passed to the salvage | only the late-switch witness red (ref written, :120) |
| (iv) `Age_pruning_keeps_young_salvage_refs` | refs `[]` | M4: prune ignores age | (iv) red at :141, plus 3 other witnesses whose ref is pruned in the same tick |

The Off_switch witness's own off-switch assertions are green at base by construction, because the base never writes refs. It is red there only through its control half. Its off-switch guard is the policy's existing filing gate, and M3/M5 cover the new side-effect paths.

## Checkpoints (run 20261105-213259-b379, plan .antiphon/c1708-checkpoints.md, ignored)

--- checkpoint report ---
run: 20261105-213259-b379   manifest: /work/worktrees/task-f822e84d/.antiphon/checkpoints/20261105-213259-b379/manifest.resolved.yaml
commit: 4f3164999f557ff07cb7dbec10354aedd87b09ad  branch: feat/card-task-f822e84d  worktree: /work/worktrees/task-f822e84d  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 4f3164999f557ff07cb7dbec10354aedd87b09ad state=clean buildSource=verified
CHECKPOINT CP-1 commit=4f3164999f557ff07cb7dbec10354aedd87b09ad build=ok filter=/*/*/(C1708ReflogSalvageTests*)|(C0824DesktopLegacyReleaseSwitchTests*)|(C0824DesktopLegacyReleaseNativeTests*)|(C0824DesktopLegacyReleaseSettingsTests*)|(C0824DesktopLegacyReleasePolicyTests*)/* executed=191 passed=191 failed=0 skipped=8 trx=/work/worktrees/task-f822e84d/.antiphon/checkpoints/20261105-213259-b379/rows/CP-1/run.trx slot=granted waited=90s dirty=0 source=4f3164999f557ff07cb7dbec10354aedd87b09ad sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=90s executed=170.934s wall=261.085s
PHASES CP-1 slotWait=90s build=831.0794298s startup=13.6681789s testsWall=151.3671049s teardown=5.0889448s hostWall=170.1246075s
SLOW CLASS Antiphon.Tests.Application.C0824DesktopLegacyReleasePolicyTests 505s tests=123 (CP-1)
SLOW CLASS Antiphon.Tests.Application.C0824DesktopLegacyReleaseSwitchTests 65s tests=29 (CP-1)
CHECKPOINT CP-2 commit=4f3164999f557ff07cb7dbec10354aedd87b09ad build=reused filter=/*/*/(TaskWorktreeRetirementTests*)|(WorktreeResidueSweepTests*)|(WorktreeGuardedCleanupTests*)|(SettledWorktreeRemovalTests*)|(LandingGitTests*)|(LandingRemovalPolicyControlTests*)|(WorktreeCleanupJournalTests*)|(WorktreeCleanupPresentationTests*)|(WorktreeCleanupSettingsValidatorTests*)|(CardDoneWorktreeCleanupTests*)|(CardWorktreeCleanupRecoveryTests*)|(CompletedCardWorktreeRemovalTests*)|(AgentTaskLandPublicationTests*)/* executed=570 passed=570 failed=0 skipped=6 trx=/work/worktrees/task-f822e84d/.antiphon/checkpoints/20261105-213259-b379/rows/CP-2/run.trx slot=granted waited=90s dirty=0 source=4f3164999f557ff07cb7dbec10354aedd87b09ad sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=90s executed=343.951s wall=434.073s
PHASES CP-2 slotWait=90s build=0s startup=21.9661904s testsWall=312.7011776s teardown=7.3526726s hostWall=342.0200368s
SLOW CLASS Antiphon.Tests.Application.AgentTaskLandPublicationTests 195s tests=63 (CP-2)
SLOW CLASS Antiphon.Tests.Application.CardDoneWorktreeCleanupTests 148s tests=20 (CP-2)
SLOW CLASS Antiphon.Tests.Infrastructure.LandingGitTests 109s tests=75 (CP-2)
SLOW CLASS Antiphon.Tests.Infrastructure.CompletedCardWorktreeRemovalTests 81s tests=37 (CP-2)
SLOW CLASS Antiphon.Tests.Infrastructure.WorktreeGuardedCleanupTests 73s tests=50 (CP-2)
CHECKPOINT CP-3 commit=4f3164999f557ff07cb7dbec10354aedd87b09ad build=reused filter=/*/*/(C1677RetentionServerTests*)|(C1677RetentionSettingsTests*)|(PhoneHomeConnectionTests*)|(C0824WorkspaceSlotServerTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(MechanicalPcContractTests*)/* executed=186 passed=107 failed=79 skipped=0 trx=/work/worktrees/task-f822e84d/.antiphon/checkpoints/20261105-213259-b379/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=4f3164999f557ff07cb7dbec10354aedd87b09ad sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=176.714s wall=176.769s
PHASES CP-3 slotWait=0s build=0s startup=10.7882646s testsWall=156.6961788s teardown=7.839382s hostWall=175.3238265s
SLOW CLASS Antiphon.Tests.Application.C1677RetentionServerTests 2865s tests=61 (CP-3)
SLOW CLASS Antiphon.Tests.Application.C0824WorkspaceSlotServerTests 775s tests=24 (CP-3)
SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 153s tests=39 (CP-3)
BUDGET wall=1750.594s execution=1570.495s admissionOnly=180.099s
unlisted: none (the tool ran no other build or test command)
wall: 29m10s  sequential-equivalent: 14m31s  builds: 1  max-concurrent-builds: 1  rows: 2 green 1 red 0 skipped
outputs: refused/kept bin-c1708/
evidence: /work/worktrees/task-f822e84d/.antiphon/checkpoints/20261105-213259-b379/report.md
verdict: RED exit=1

- **CP-1:** 191 passed, 0 failed, 8 skipped. The skips are the Windows-native C0824DesktopLegacyReleaseNativeTests.
- **CP-2:** 570 passed, 0 failed, 6 skipped (native).
- **CP-3:** 79 failed, 107 passed. Every failure is a TUnit timeout: 58 in C1677RetentionServerTests (45 s) and 21 in C0824WorkspaceSlotServerTests. PhoneHome, C1677RetentionSettings, the guards and MechanicalPcContractTests are all green.
- **Validation:** `validate --rows CP-1,CP-2` returns `INVALID reason=report_failed`, because the whole run is red.

**Base comparison for the CP-3 timeouts** (direct build-slot runs of `(C1677RetentionServerTests*)|(C0824WorkspaceSlotServerTests*)`, 85 tests):

| Run | SHA | Slot wait | Failed / 85 |
|---|---|---|---|
| base r1 | 88e9fc746 | 60 s | 4 (C0824 real-runner, 35 s) |
| tip | 4f3164999 | 150 s | **77** |
| base r2, immediately after, load average 33 on 24 cores | 88e9fc746 | 90 s | **77** |

Neither class reaches the changed code: they test RunnerWorktreeRetentionService and the workspace slots. This is the 77-timeout family Debug 5ae98d31 is investigating, and it is inherited and load-dependent.

## Other build and test drivers outside the checkpoint table

Every one ran under its own build-slot lease, with UseAppHost=false and TEMP=/tmp.

| Run | Slot wait | Lease held | Result |
|---|---|---|---|
| red build + run | 90 s | 392 s | 5/5 red |
| fix build + run (C1708 + settings) | 451 s | 538 s | 39/39 green |
| mutant build | 105 s | 480 s | — |
| 5 mutant runs | 0 s each | 23–34 s each | see the mutant table |
| base build + run | 60 s | 265 s | — |
| tip CP-3 comparison build + run | 150 s | 336 s | — |
| base r2 run | 90 s | 72 s | — |

The checkpoint tool itself was run as `dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll`, the dll the Antiphon.Tests build had already produced.

## Repository checks

- `check-evidence-diff.ps1 -BaseRef 88e9fc746 -HeadRef 4f3164999`: commits=2, entries=0, violations=0. Re-run at the report tip; see the final message.
- `git merge-tree --write-tree origin/master(e6da24530) HEAD`: clean, tree a34d3cf73.

## Not run, or pending

- **Whole Unit lane:** not run. The brief named the classes to run, and the time budget was exhausted. It is not passed.
- **"Policy sweep":** no class by that name was found. TestClassificationPolicy* and MechanicalPcContractTests ran in CP-3 and are green.
- **Windows-native skips (14):** pending on a Windows runner.
- **PCs:** all pending for SourceLanding Mutation:
  - the salvage call and its refusal in GuardedWorktreeRemoval;
  - the pre-write switch read and the ambient gate at each write;
  - SalvageScope being set for system releases only;
  - the age check in the prune and the prune being for executing ticks only;
  - the post-write for-each-ref verification;
  - the `--not --branches --tags --remotes` filter;
  - the merge-base reduction;
  - the over-limit refusals;
  - a reflog read failure refusing.
- **Missing controls** (no witness yet, for Mutation's missing-control discovery):
  - over-limit refusal;
  - verification failure;
  - a detached-HEAD-only commit;
  - a branch-reflog-only commit with the HEAD reflog intact.
