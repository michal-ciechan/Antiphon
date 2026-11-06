# CARD-1708 round 1 repair — Code task 935ca9be (Review 9222cb00 B-1..B-4)

Branch feat/card-task-935ca9be, worktree /work/worktrees/task-935ca9be (runner mirror), stacked on c9d9d7a631d923aa904a4daf1dde5ebd70ed73fb. Original Code task / landing owner f822e84d-ef5c-44d0-ba24-05580ee5fadf. Review report /work/evidence/task-9222cb00/docs/investigations/2026-11-05-review-card-1708.md.

## Commits

| SHA | Role |
|---|---|
| 5c0ed9f1057a4fcbb43fd1ccedc84989d4a6a434 | witnesses (red; did not compile, CS1503 at line 307) |
| bf9159aa1fd0fa7df03b4c0251406ec7deb74680 | red SHA: witness compile fix (production = c9d9d7a63) |
| 585edc1d9a9803f7e23626fd3896b2fc3f3a91bc | fix SHA (B-1..B-4) |
| a694ad9874ccbd1488aa47e1e12aa0dd91675113 | tested SHA: docs + test class summary (no product change after the fix) |

## Fixes (server/Infrastructure/Git/WorktreeReflogSalvage.cs, server/Application/Services/DesktopLegacyReleasePolicyService.cs)

- B-1: for the HEAD and branch reflogs, the candidates now include every non-null old and new id from the reflog file Git keeps (`rev-parse --path-format=absolute --git-path logs/<ref>`), as well as the `reflog show` ids. Any doubt fails closed with `reflog_salvage_failed`: an unreadable or malformed line, or a missing file when `reflog show` listed entries (for example reftable).
- B-4: the write reads the scope namespace first (`%(symref)`). An existing direct ref holding the exact commit is kept and not rewritten. A symbolic ref, or a ref naming another object, refuses with `reflog_salvage_collision`. Writes are `update-ref --no-deref ... <sha> <null-id>`, so they can only create. Verification requires a direct ref (empty symref). Prune skips any symbolic line, deletes only a direct ref named after its own object, and deletes with `--no-deref -d <name> <oid>`.
- B-2: prune takes the repository mutation lease. If the lease is busy, every ref in that repository is kept. Under the lease it reads the DB: a task scope can be pruned only when every retirement of that task is Complete and its RetirementCompletedAt is at least the retention ago. The ref's own reflog-age check stays as well. So an unfinished, refused or retried release keeps its refs, and the retention counts from the completed release.
- B-3: prune now receives the tick's live switch (StopReason). It is read before each repository, before each delete, and at the delete child's start through LandingGit.GateDestructiveStep. A refusal deletes nothing more and becomes the tick's `Stopped`.

## Witnesses (C1708ReflogSalvageTests, 8 new), red at bf9159aa1 then green at 585edc1d9

Red run at bf9159aa1 (native build, build-slot): 13 total, 8 failed, 5 passed. Each witness failed at its intended assertion:
- B-1 Commit_kept_only_as_a_reflog_old_id_gets_a_salvage_ref (:179 no salvage ref)
- B-2 Aged_retry_keeps_its_salvage_ref_for_a_retention_after_completion (:208 pruned in the retry's own tick)
- B-2 Unfinished_release_keeps_its_salvage_ref_past_the_retention (:248 unfinished release's ref pruned)
- B-2 Prune_needs_the_repository_lease (:267 pruned while the lease was held)
- B-3 Late_switch_at_the_prune_delete_keeps_every_salvage_ref (:306 fired 2, expected 1)
- B-4 Symbolic_salvage_collision_refuses_and_never_moves_another_branch (:333 refs/heads/precious moved)
- B-4 Direct_salvage_collision_refuses_and_keeps_the_existing_ref (:357 overwritten)
- B-4 Prune_never_deletes_through_a_symbolic_salvage_ref (:383: at base the prune deleted refs/heads/precious through the symref, a further instance of B-4)

Green at 585edc1d9: 13/13.

Mutants (fix pieces reverted, two batched native builds, each witness red only for its own mutant, source restored with `git checkout --` afterwards):
- M-A (M1 drop logged old ids; M3 prune gate returns null and pre-delete live check removed; M2L lease-null ignored): 3 failed (B-1 :179, lease :267, prune switch :306), 10 passed.
- M-B (M2C every scope prunable; M4 collision check, --no-deref and null old-id dropped from the write; M4P symbolic lines admitted and --no-deref dropped from the delete): 5 failed (aged retry :208, unfinished :248, symbolic :333, direct :357, prune symref :383), 8 passed.
These are Code-stage mutants and give no PC credit. Every PC remains pending for method-scoped SourceLanding Mutation.

## Checkpoint run 20261106-000210-51a6 (plan .antiphon/c1708r1/checkpoints.md = the Code plan's three rows, output bin-c1708r1/)

```
run: 20261106-000210-51a6   manifest: /work/worktrees/task-935ca9be/.antiphon/checkpoints/20261106-000210-51a6/manifest.resolved.yaml
commit: a694ad9874ccbd1488aa47e1e12aa0dd91675113  branch: feat/card-task-935ca9be  worktree: /work/worktrees/task-935ca9be  host: Debian GNU/Linux 12 (bookworm) cores=24
source: a694ad9874ccbd1488aa47e1e12aa0dd91675113 state=clean buildSource=verified
CHECKPOINT CP-1 commit=a694ad9874ccbd1488aa47e1e12aa0dd91675113 build=ok filter=/*/*/(C1708ReflogSalvageTests*)|(C0824DesktopLegacyReleaseSwitchTests*)|(C0824DesktopLegacyReleaseNativeTests*)|(C0824DesktopLegacyReleaseSettingsTests*)|(C0824DesktopLegacyReleasePolicyTests*)/* executed=199 passed=199 failed=0 skipped=8 trx=/work/worktrees/task-935ca9be/.antiphon/checkpoints/20261106-000210-51a6/rows/CP-1/run.trx slot=granted waited=120s dirty=0 source=a694ad9874ccbd1488aa47e1e12aa0dd91675113 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=120s executed=135.727s wall=255.978s
PHASES CP-1 slotWait=300s build=513.0412846s startup=10.8022076s testsWall=118.9582295s teardown=5.1049895s hostWall=134.8656375s
CHECKPOINT CP-2 commit=a694ad9874ccbd1488aa47e1e12aa0dd91675113 build=reused filter=/*/*/(TaskWorktreeRetirementTests*)|(WorktreeResidueSweepTests*)|(WorktreeGuardedCleanupTests*)|(SettledWorktreeRemovalTests*)|(LandingGitTests*)|(LandingRemovalPolicyControlTests*)|(WorktreeCleanupJournalTests*)|(WorktreeCleanupPresentationTests*)|(WorktreeCleanupSettingsValidatorTests*)|(CardDoneWorktreeCleanupTests*)|(CardWorktreeCleanupRecoveryTests*)|(CompletedCardWorktreeRemovalTests*)|(AgentTaskLandPublicationTests*)/* executed=570 passed=570 failed=0 skipped=6 trx=/work/worktrees/task-935ca9be/.antiphon/checkpoints/20261106-000210-51a6/rows/CP-2/run.trx slot=granted waited=270s dirty=0 source=a694ad9874ccbd1488aa47e1e12aa0dd91675113 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=270s executed=305.041s wall=575.366s
PHASES CP-2 slotWait=270s build=0s startup=10.384067s testsWall=279.9839491s teardown=12.4747533s hostWall=302.8427694s
CHECKPOINT CP-3 commit=a694ad9874ccbd1488aa47e1e12aa0dd91675113 build=reused filter=/*/*/(C1677RetentionServerTests*)|(C1677RetentionSettingsTests*)|(PhoneHomeConnectionTests*)|(C0824WorkspaceSlotServerTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(MechanicalPcContractTests*)/* executed=186 passed=104 failed=82 skipped=0 trx=/work/worktrees/task-935ca9be/.antiphon/checkpoints/20261106-000210-51a6/rows/CP-3/run.trx slot=granted waited=330s dirty=0 source=a694ad9874ccbd1488aa47e1e12aa0dd91675113 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=330s executed=242.993s wall=573.110s
PHASES CP-3 slotWait=330s build=0s startup=13.6862063s testsWall=226.0633636s teardown=2.3995121s hostWall=242.1490821s
BUDGET wall=2155.544s execution=1255.302s admissionOnly=900.242s
wall: 35m55s  sequential-equivalent: 23m24s  builds: 1  max-concurrent-builds: 1  rows: 2 green 1 red 0 skipped
outputs: refused/kept bin-c1708r1/
verdict: RED exit=1
```

`validate --evidence .antiphon/checkpoints/20261106-000210-51a6/report.json --expected-source-sha a694ad98...`: `CHECKPOINT SOURCE INVALID reason=report_failed` (because CP-3 is red; the source is clean).

## CP-3 timeouts (existing timeouts repair, Code d69fe301)

My CP-3 hit them: 82 failed. All are TimeoutException, 61 in C1677RetentionServerTests and 21 in C0824WorkspaceSlotServerTests. The 101 tests outside those two classes all pass. The per-identity outcome matches the Review's c9d9d7a63 tip run 85/85: the same 82 failures, 17 also red at base 88e9fc746 and 65 green at base.

Classification run (one build-slot lease, the kept clean tip output bin-c1708r1, the two classes only): 85 executed, 66 passed, 19 failed (all TimeoutException).
- C1677RetentionServerTests: 61/61 pass alone at the tip (the Review's base run, with the two classes alone, was also 61/61).
- C0824WorkspaceSlotServerTests: 5 passed and 19 timed out. 17 of those are exactly the base reds. The other 2 are tip-only: C0824_ReleasedTaskCoordinatesNotAmbiguous and C0824_ReleaseReconcilerRequiresAllConditions(not-terminal). They are a single sample in the same 35 s timing family and stay unclassified.
- Conclusion: 63 of the 65 depend on composition, not on the SHA. They time out only when these two classes share a row with the other CP-3 classes (TestClassificationPolicy, PhoneHomeConnection and the rest), at both c9d9d7a63 and a694ad987. The salvage diff does not touch RunnerWorktreeRetention or RemoteWorkspace code. There was no separate base build or run: the base datapoint is the Review's 20261105-222858-a6fa, so the brief's "base and tip in one lease" was done only for the tip.

## Merge-tree vs origin/master 1527c29fe

`git merge-tree --write-tree origin/master HEAD` gives content conflicts in DesktopLegacyReleasePolicyService.cs, RemoteWorkspaceService.cs and TaskWorktreeRetirementService.cs, one hunk each. The same three conflicts occur at the S4a base 88e9fc746 and at c9d9d7a63, and no conflict hunk contains a CARD-1708 line. All three are inherent to the S4a base, and CARD-1708 (this repair included) introduces none. They were not resolved, as the brief instructed.

## Other

- check-evidence-diff c9d9d7a63..a694ad987: commits=4 entries=0 violations=0.
- Unlisted runs, all build-slot leased: 4 native builds (one red build failed to compile, the red build, the fix build, and a re-launch after my own 590 s wrapper timeout cut the first fix build during slot admission), 2 mutant builds, red/fix/M-A/M-B class runs, and the classification run. No green row was rerun.
- Not run: the whole Unit lane (cost; Debug 5ae98d31/CARD-1720) and native Windows (8 + 6 skips pending).
- Advisories A-1 (180 s class timeout) and A-2 (no prune cursor) were not changed. Prune now filters to prunable scopes before the 200 cap, which reduces A-2 starvation by other tasks' refs.
- Build outputs: every native bin/obj created here and bin-c1708r1 were deleted with literal paths. Checkpoint receipts and .antiphon/c1708r1 logs/TRX remain, ignored.
