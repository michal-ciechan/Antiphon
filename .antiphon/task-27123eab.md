# CARD-0824 S4a round 4 — Code report (task 27123eab)

Outcome: B-1 and B-2 from Review 1999cb95 (round 3, /tmp/1999cb95-evidence/review-report.md) are fixed and nothing else was changed. Each blocker has a witness that is red at the pre-fix commit and green at the fix commit. Reverting each guard (a mutant) turns its own witness red while the other witnesses stay green as controls. The final checkpoint run is GREEN at the fix SHA.

Stacked on 45f1aaee8 (origin/feat/card-task-0c839994), branch feat/card-task-27123eab, worktree /work/worktrees/task-27123eab. Combined Code source owner: 0c839994-dbc8-4f03-8b6e-4385df959e17. Original slice landing owners: A 73c94e0b-ad0b-4917-b782-e989e262a1f9 and B 362f7434-af30-4737-a4d5-fd7a732c77ff. This task: 27123eab-fefd-40b0-b509-d36e212d69b9.

## What each blocker was, and the fix

- **B-1 (local destructive helpers).** The switch was re-read in the callers, but each step still awaited inside its callee before acting:
  - `LandingGit.UnregisterWorktreeAsync` awaits the common-directory read and `ReadAllTextAsync(gitdir)`, then moves Git's administrative entry.
  - The local `update-ref -d` awaits `RepositoryChildJournal.BeginAsync` before `Process.Start`.

  Execute turned off during those awaits still unregistered the tree, or deleted the branch.

  **Fix:** `LandingGit.GateDestructiveStep` adds an ambient (AsyncLocal) gate scoped to one call. It is ambient so every fixture or override that calls `base` still reaches it. `LandingGit` reads the gate:
  - in `ExecuteProcessAsync`, after the journal is written and before the child starts (only for mutating commands). A refusal starts nothing and clears the unstarted journal with `journal.NotStarted()`.
  - in `UnregisterWorktreeAsync`, after its reads and before the entry's move. Only synchronous checks run between that read and the move.

  `GuardedWorktreeRemoval` scopes `LiveAuthorizationAsync(request)` around unregister and around the branch delete:
  - A refusal at the move restores the registered tree, records no git outcome and sets no failedAt.
  - A refusal at the branch start returns the live reason as residue.
  - The existing outer reads are unchanged.
- **B-2 (queued mirror removal).** `PhoneHomeLiveConnection.RequestAsync` awaited `_send` after the caller's last switch read. A WorkspaceRemove that was queued, and then disabled while it waited, was still sent, and the stored mirror path was cleared.

  **Fix:** a `RequestAsync(..., beforeWrite, ct)` overload reads the gate once the send gate is held, last before the frame is written. A refusal writes nothing and throws `PhoneHomeRequestRefusedException`. `PhoneHomeRunnerClient.RemoveWorkspaceAsync(..., beforeSend)` passes the gate through. `RemoteWorkspaceService.RemoveMirrorAsync` passes `liveAuthorization` and maps the refusal to `path (reason)`, so the mirror path is kept. A frame that was already written is out of scope, as the Review stated.

The default switch is unchanged in code, and no host settings were touched.

## Commits

| SHA | What |
|---|---|
| 14d6cbd1eada6dc8949d801425c44d712dbd83a5 | three witnesses (did not compile: missing using) |
| a920fe383498c8da4d018fc804e885a5dd96aa9d | using fix; first red run (3/3 red; the branch witness was red on a hook race, fired=2) |
| 80e5686e7d616ecfdbbcded1328f5d42365d6c31 | **red SHA**: branch witness fires once, on this world's own journal; 3/3 red on the blocker outcome |
| 2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 | **fix SHA = tested SHA** |

Witnesses (`C0824DesktopLegacyReleaseSwitchTests`):

| Witness | Blocker | Red at 80e5686e7 | Green at 2f66c1bbe |
|---|---|---|---|
| `Late_switch_inside_unregister_keeps_the_registration` | B-1 unregister | `RegisteredAsync` False | registered, tree back, no drop, then resumes to retired |
| `Late_switch_before_branch_delete_start_keeps_the_branch` | B-1 branch | `BranchExistsAsync` False | branch kept, reason `legacy_release_execute_off`, then resumes to retired |
| `Late_switch_while_mirror_remove_waits_to_send_keeps_the_mirror` | B-2 | removes 1 | 0 removes, path kept |

Mutants (each built with its own build-slot lease, then restored with `git checkout --`):

| Mutant | Change | Result |
|---|---|---|
| M1 | remove the gate read in `UnregisterWorktreeAsync` | unregister witness red (`RegisteredAsync` False) |
| M3 | `beforeWrite` check disabled in `RequestAsync` | mirror witness red (removes 1) |
| M2 | gate read in `ExecuteProcessAsync` replaced by null | branch witness red (`BranchExistsAsync` False) |

M1 and M3 ran in one build: different files, and in that run the branch witness stayed green as a control. M2 ran in a separate build: the other two witnesses stayed green.

## Checkpoints (run 20261105-171951-8e41, plan .antiphon/c0824-r4-checkpoints.md, ignored)

--- checkpoint report ---
run: 20261105-171951-8e41   manifest: /work/worktrees/task-27123eab/.antiphon/checkpoints/20261105-171951-8e41/manifest.resolved.yaml
commit: 2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77  branch: feat/card-task-27123eab  worktree: /work/worktrees/task-27123eab  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 state=clean buildSource=verified
CHECKPOINT CP-1 commit=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 build=ok filter=/*/*/(C0824DesktopLegacyReleaseSwitchTests*)|(C0824DesktopLegacyReleaseNativeTests*)/* executed=29 passed=29 failed=0 skipped=8 trx=/work/worktrees/task-27123eab/.antiphon/checkpoints/20261105-171951-8e41/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=34.885s wall=34.932s
PHASES CP-1 slotWait=0s build=250.0434284s startup=10.5770009s testsWall=20.0079888s teardown=3.4088915s hostWall=33.9938835s
SLOW CLASS Antiphon.Tests.Application.C0824DesktopLegacyReleaseSwitchTests 78s tests=29 (CP-1)
CHECKPOINT CP-2 commit=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 build=reused filter=/*/*/(C0824DesktopLegacyReleaseSettingsTests*)|(C0824DesktopLegacyReleasePolicyTests*)/* executed=155 passed=155 failed=0 skipped=0 trx=/work/worktrees/task-27123eab/.antiphon/checkpoints/20261105-171951-8e41/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=110.565s wall=110.608s
PHASES CP-2 slotWait=0s build=0s startup=10.270802s testsWall=94.9728859s teardown=4.5434314s hostWall=109.7871191s
SLOW CLASS Antiphon.Tests.Application.C0824DesktopLegacyReleasePolicyTests 304s tests=123 (CP-2)
CHECKPOINT CP-3 commit=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 build=reused filter=/*/*/(TaskWorktreeRetirementTests*)|(WorktreeResidueSweepTests*)/* executed=55 passed=55 failed=0 skipped=0 trx=/work/worktrees/task-27123eab/.antiphon/checkpoints/20261105-171951-8e41/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=30.345s wall=30.391s
PHASES CP-3 slotWait=0s build=0s startup=10.2912423s testsWall=14.2643982s teardown=5.0185122s hostWall=29.5741523s
CHECKPOINT CP-4 commit=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 build=reused filter=/*/*/(WorktreeGuardedCleanupTests*)|(SettledWorktreeRemovalTests*)|(LandingGitTests*)|(LandingRemovalPolicyControlTests*)/* executed=337 passed=337 failed=0 skipped=5 trx=/work/worktrees/task-27123eab/.antiphon/checkpoints/20261105-171951-8e41/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=137.240s wall=137.285s
PHASES CP-4 slotWait=0s build=0s startup=9.5879805s testsWall=122.9213493s teardown=4.0104026s hostWall=136.5197321s
SLOW CLASS Antiphon.Tests.Infrastructure.LandingGitTests 86s tests=75 (CP-4)
SLOW CLASS Antiphon.Tests.Infrastructure.WorktreeGuardedCleanupTests 75s tests=50 (CP-4)
CHECKPOINT CP-5 commit=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 build=reused filter=/*/*/(WorktreeCleanupJournalTests*)|(WorktreeCleanupPresentationTests*)|(WorktreeCleanupSettingsValidatorTests*)|(CardDoneWorktreeCleanupTests*)|(CardWorktreeCleanupRecoveryTests*)|(CompletedCardWorktreeRemovalTests*)/* executed=115 passed=115 failed=0 skipped=0 trx=/work/worktrees/task-27123eab/.antiphon/checkpoints/20261105-171951-8e41/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=65.256s wall=65.301s
PHASES CP-5 slotWait=0s build=0s startup=9.3917087s testsWall=51.0828978s teardown=4.0026486s hostWall=64.4772546s
SLOW CLASS Antiphon.Tests.Application.WorktreeCleanupJournalTests 254s tests=16 (CP-5)
SLOW CLASS Antiphon.Tests.Application.CardDoneWorktreeCleanupTests 91s tests=20 (CP-5)
SLOW CLASS Antiphon.Tests.Infrastructure.CompletedCardWorktreeRemovalTests 65s tests=37 (CP-5)
CHECKPOINT CP-6 commit=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 build=reused filter=/*/*/(C1677RetentionServerTests*)|(C1677RetentionSettingsTests*)|(PhoneHomeConnectionTests*)|(C0824WorkspaceSlotServerTests*)/* executed=128 passed=128 failed=0 skipped=0 trx=/work/worktrees/task-27123eab/.antiphon/checkpoints/20261105-171951-8e41/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=0s executed=49.038s wall=49.085s
PHASES CP-6 slotWait=0s build=0s startup=10.4931899s testsWall=32.7306522s teardown=5.035866s hostWall=48.259708s
SLOW CLASS Antiphon.Tests.Application.C1677RetentionServerTests 1685s tests=61 (CP-6)
SLOW CLASS Antiphon.Tests.Application.C0824WorkspaceSlotServerTests 605s tests=24 (CP-6)
SLOW CLASS Antiphon.Tests.Agents.PhoneHomeConnectionTests 71s tests=29 (CP-6)
CHECKPOINT CP-7 commit=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 build=reused filter=/*/*/AgentTaskLandPublicationTests*/* executed=63 passed=63 failed=0 skipped=1 trx=/work/worktrees/task-27123eab/.antiphon/checkpoints/20261105-171951-8e41/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=127.460s wall=127.509s
PHASES CP-7 slotWait=0s build=0s startup=10.3009634s testsWall=111.9728474s teardown=4.4234969s hostWall=126.6973078s
SLOW CLASS Antiphon.Tests.Application.AgentTaskLandPublicationTests 111s tests=63 (CP-7)
CHECKPOINT CP-8 commit=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(MechanicalPcContractTests*)/* executed=58 passed=58 failed=0 skipped=0 trx=/work/worktrees/task-27123eab/.antiphon/checkpoints/20261105-171951-8e41/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=0s executed=97.469s wall=97.516s
PHASES CP-8 slotWait=0s build=0s startup=5.202954s testsWall=90.9301198s teardown=0.5722334s hostWall=96.7053073s
SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 90s tests=39 (CP-8)
BUDGET wall=1003.779s execution=1003.770s admissionOnly=0.009s
unlisted: none (the tool ran no other build or test command)
wall: 16m43s  sequential-equivalent: 10m52s  builds: 1  max-concurrent-builds: 1  rows: 8 green 0 red 0 skipped
outputs: deleted bin-c0824r4/
evidence: /work/worktrees/task-27123eab/.antiphon/checkpoints/20261105-171951-8e41/report.md
verdict: GREEN exit=0

- **Totals:** 940 passed, 0 failed, 14 skipped. Every skip is a Windows-native case (CP-1 8, CP-4 5, CP-7 1), the same set the Review lists as pending.
- **Validation:** `validate --rows CP-2,CP-3,CP-5,CP-6,CP-8` gives `CHECKPOINT SOURCE VALID source=2f66c1bbe5355ac28ea2d26f0fc3623b77aa9b77 rows=5`. Full validation gives `INVALID reason=row_failed`, because of the native skips only.
- **Unlisted drivers:** each had its own build-slot lease, with UseAppHost=false and TEMP=/tmp:
  - 3 red builds/runs (the first build failed with CS0246)
  - the fix build and its green run
  - 2 mutant builds/runs
  - one un-slotted `dotnet run --project tools/Antiphon.Checkpoints -- import` that built the checkpoint tool, to validate the table. This was an oversight; later tool calls used the built dll.
- **Repository checks:**
  - `check-evidence-diff.ps1 -BaseRef 45f1aaee8 -HeadRef 2f66c1bbe`: commits=4, entries=0, violations=0.
  - `git merge-tree --write-tree origin/master(388ff4820) HEAD`: clean, tree 7584b630f.

## Pending (not discharged here)

- **PCs:** every PC is still pending for method-scoped SourceLanding Mutation. That includes the three new guards: the gate in `UnregisterWorktreeAsync`, the gate at the mutating child start, and `beforeWrite` after `_send`. The Code-stage M1–M3 results are historical only.
- **Windows-native receipts (B-6)** are still pending, along with the Review's A-1..A-3 advisories. The A-1 Windows assertion was not touched (out of scope).
- **Out of scope:** the whole Unit lane (CARD-1720 cap) and real producer-to-recipient acceptance.
- **Residual window:** the ambient gate is not seen by non-`LandingGit` `ILandingGit` fakes; for those the outer reads still apply.
