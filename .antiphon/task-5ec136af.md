# CARD-0824 S4a integration onto master: Code report (task 5ec136af)

Branch feat/card-task-5ec136af, worktree /work/worktrees/task-5ec136af (runner mirror), original Code task 5ec136af.
Base master 1a22e0d2ca7b32e7f6ac346a658e1c7824252a46. Tested tip 0ac54954def9d4ee1753a489ed6e42c9369c929c.
Resolution record: docs/investigations/2026-11-06-s4a-integration-onto-master.md (replay table, every hunk).
Plan: docs/superpowers/plans/2026-11-06-card-0824-s4a-integration-checkpoints.md.

## Commits (on top of the 41 replayed picks, post-replay 8c519a759cfe)

- 22bb9eb8dbd290c4bf90f0599ea2a6bda156569c test: S4a-off system release on a ref-only task sends no mirror remove (red)
- a4aff79c0bcbf6fa39592033be9abb2ca7536748 fix: S5b-1 no-claim mirror call compiles against the S4a gated signature (no gate = master behaviour)
- f28571c8e216a6cff7e8c2789308636ae3423f29 fix: that branch reads the S4a switch and passes the live gate (green)
- 0ac54954def9d4ee1753a489ed6e42c9369c929c docs: investigation + checkpoint plan

Replay: 48 `+` from git cherry; 41 picked; skipped 6 .antiphon report commits, 2291dbfca (byte-identical to
ae99c06ae, empty pick) and the merge e893ad24c (its seam resolution reproduced exactly in the 35bb34ad7 pick).
Conflicts: 96d7cde36 (DesktopLegacyReleasePolicyService), 35bb34ad7 (GuardedWorktreeRemoval A+B seam, the
reviewed merge's resolution), b3873bd12 (RemoteWorkspaceService + TaskWorktreeRetirementService). 3-way
equivalence check vs (master, merge-base, stack tip): identical for every stack file except those conflict regions.

## Checkpoints (Linux server2, UseAppHost=false, TEMP=/tmp)

Run A 20261106-032227-d547 (rows CP-1..CP-3), verdict RED exit=1 (CP-3 inherited red only):
    CHECKPOINT CP-1 commit=0ac54954def9d4ee1753a489ed6e42c9369c929c build=ok filter=/*/*/(C1708ReflogSalvageTests*)|(C0824DesktopLegacyReleaseSwitchTests*)|(C0824DesktopLegacyReleaseNativeTests*)|(C0824DesktopLegacyReleaseSettingsTests*)|(C0824DesktopLegacyReleasePolicyTests*)/* executed=201 passed=201 failed=0 skipped=8 trx=/work/worktrees/task-5ec136af/.antiphon/checkpoints/20261106-032227-d547/rows/CP-1/run.trx slot=granted waited=120s dirty=0 source=0ac54954def9d4ee1753a489ed6e42c9369c929c sourceState=clean buildSource=verified
    CHECKPOINT-TIMING CP-1 waited=120s executed=112.042s wall=232.094s
    PHASES CP-1 slotWait=465s build=504.9257375s startup=16.9853023s testsWall=91.7952083s teardown=2.4878524s hostWall=111.2683627s
    CHECKPOINT CP-2 commit=0ac54954def9d4ee1753a489ed6e42c9369c929c build=reused filter=/*/*/(TaskWorktreeRetirementTests*)|(WorktreeResidueSweepTests*)|(WorktreeGuardedCleanupTests*)|(SettledWorktreeRemovalTests*)|(LandingGitTests*)|(LandingRemovalPolicyControlTests*)|(WorktreeCleanupJournalTests*)|(WorktreeCleanupPresentationTests*)|(WorktreeCleanupSettingsValidatorTests*)|(CardDoneWorktreeCleanupTests*)|(CardWorktreeCleanupRecoveryTests*)|(CompletedCardWorktreeRemovalTests*)|(AgentTaskLandPublicationTests*)|(WorktreeNoFollowDeleteTests*)/* executed=583 passed=583 failed=0 skipped=7 trx=/work/worktrees/task-5ec136af/.antiphon/checkpoints/20261106-032227-d547/rows/CP-2/run.trx slot=granted waited=15s dirty=0 source=0ac54954def9d4ee1753a489ed6e42c9369c929c sourceState=clean buildSource=verified
    CHECKPOINT-TIMING CP-2 waited=15s executed=282.433s wall=297.480s
    PHASES CP-2 slotWait=15s build=0s startup=18.3654627s testsWall=256.2442604s teardown=6.8215502s hostWall=281.4312733s
    CHECKPOINT CP-3 commit=0ac54954def9d4ee1753a489ed6e42c9369c929c build=reused filter=/*/*/(C1677RetentionServerTests*)|(C1677RetentionSettingsTests*)|(PhoneHomeConnectionTests*)|(C0824WorkspaceSlotServerTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(MechanicalPcContractTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=199 passed=197 failed=2 skipped=0 trx=/work/worktrees/task-5ec136af/.antiphon/checkpoints/20261106-032227-d547/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=0ac54954def9d4ee1753a489ed6e42c9369c929c sourceState=clean buildSource=verified
    CHECKPOINT-TIMING CP-3 waited=0s executed=233.334s wall=233.406s
    PHASES CP-3 slotWait=0s build=0s startup=27.2775523s testsWall=201.3795196s teardown=3.4516234s hostWall=232.1086951s
    FAILED Antiphon.TestSupport.NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute (CP-3) ShouldAssertException: report.Success     should be True     but was False  Additional Info:     reverse-coverage suite=antiphon row=C0824TransportGitTests concrete=Antiphon.Tests.Application.C0824TransportGitTests declaring=Antiphon.Tests.Application.C0824TransportGitTests signature=Scratch_git_deadline_includes_inherited_output_drains() -> rows/CP-3/failures.md
    FAILED Antiphon.TestSupport.NativeLinuxPolicyWitnessTests.Markers_do_not_expand_policy_exclusions (CP-3) ShouldAssertException: report.Diagnostics.Any(item => item.Code == "reverse-coverage")     should be False     but was True  Additional Info:     reverse-coverage suite=antiphon row=C0824TransportGitTests concrete=Antiphon.Tests.Application.C0824TransportGitTests declaring=Antiphon.Tests.Application.C0824TransportGitTests signature=Scratch_git_deadline_includes_inherited_output_drains() -> rows/CP-3/failures.md
    BUDGET wall=1655.189s execution=1175.104s admissionOnly=480.085s

Run B 20261106-035035-7463 (rows CP-4, CP-5), verdict GREEN exit=0, `validate` -> `CHECKPOINT SOURCE VALID source=0ac54954def9d4ee1753a489ed6e42c9369c929c rows=2`:
    CHECKPOINT CP-4 commit=0ac54954def9d4ee1753a489ed6e42c9369c929c build=ok filter=/*/Antiphon.Tests.Application/(C0824RefOnlyIdentityTests*)|(C0824RefOnlyGateTests*)|(C0824RefOnlyLandingTests*)|(C0824RefOnlyCleanupTests*)/* executed=20 passed=20 failed=0 skipped=0 trx=/work/worktrees/task-5ec136af/.antiphon/checkpoints/20261106-035035-7463/rows/CP-4/run.trx slot=granted waited=240s dirty=0 source=0ac54954def9d4ee1753a489ed6e42c9369c929c sourceState=clean buildSource=verified
    CHECKPOINT-TIMING CP-4 waited=240s executed=107.394s wall=347.577s
    PHASES CP-4 slotWait=690s build=729.611022s startup=20.9783665s testsWall=82.0945592s teardown=3.6091901s hostWall=106.6823105s
    CHECKPOINT CP-5 commit=0ac54954def9d4ee1753a489ed6e42c9369c929c build=reused filter=/*/Antiphon.Tests.Application/DelegationWorktreeTests/(C508_ReuseKeepsRecordedBase*)|(C508_BaseResolutionRequiresOwnedLease*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-5ec136af/.antiphon/checkpoints/20261106-035035-7463/rows/CP-5/run.trx slot=granted waited=120s dirty=0 source=0ac54954def9d4ee1753a489ed6e42c9369c929c sourceState=clean buildSource=verified
    CHECKPOINT-TIMING CP-5 waited=120s executed=43.429s wall=163.669s
    PHASES CP-5 slotWait=120s build=0s startup=35.0251775s testsWall=1.1263805s teardown=6.4368945s hostWall=42.5884525s
    BUDGET wall=1867.820s execution=1057.531s admissionOnly=810.289s

- CP-1 201/201 (8 skipped = Windows-native C0824DesktopLegacyReleaseNativeTests et al.), CP-2 583/583 (7 Windows-native
  skips), CP-3 197/199, CP-4 20/20, CP-5 2/2. Run A `validate`: `CHECKPOINT SOURCE INVALID reason=report_failed`
  (CP-3 red; CP-1/CP-2 platform skips cannot validate on Linux either).
- The old-base CP-3 timeout family (82 TimeoutException in C1677RetentionServerTests/C0824WorkspaceSlotServerTests)
  is gone: both classes fully green with master's caches.
- CP-3 red = NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute and
  Markers_do_not_expand_policy_exclusions, `reverse-coverage row=C0824TransportGitTests
  signature=Scratch_git_deadline_includes_inherited_output_drains()`. INHERITED: the method came with master's
  77b4e1d77 (batch-87 timeout fix) without a native-Linux policy row; red 2/2 with the identical diagnostic at base
  1a22e0d2c (detached worktree, build-slot leases build waited=301s+incremental, run 487a25cb waited=361s). Not fixed on
  origin/master b37fc7bbd either (no policy or class change since). Needs a master repair (policy row + reseal).

Unlisted builds/runs (all build-slot, native default output, UseAppHost=false): build at a4aff79c0 (lease
dd4f9b64, waited 150 s, 203 s); red method run (daf7956d, 1 failed on s4a-switch-off-sends-no-mirror-remove);
fix build (d9ac6ac5, waited 120 s, 135 s); green method run (66e555e4, 1/1); base red proof above. One earlier
build at 22bb9eb8 was stopped while still queued for a slot (it could not compile: the a4aff79c0 seam).
Repetitions: none beyond the above.

## Policy, migrations, digests

No EF migration or model change in the stack (master's S5b-1 and CARD-1591 chain untouched). No csproj, no
tests/test-execution-policy.json change: policyHash and PinnedInputDigest inputs unchanged, not recomputed;
build-graph digest untouched.

## Other gates

- scripts/check-evidence-diff.ps1 -Repository . -BaseRef 1a22e0d2c -HeadRef HEAD: commits=45 entries=0 violations=0.
- git merge-tree --write-tree origin/master HEAD: clean at origin/master b37fc7bbd4d2357a7391675c45254a2c34c89b04.
- S4a not activated anywhere; no host/config edits.

## Owed (not done here; never marked passed)

- Windows-native: C0824DesktopLegacyReleaseNativeTests and the other 15 Windows-only skips in CP-1/CP-2 (desktop lane).
- Whole Unit lane at the integrated tip (not run: budget; the closed list above is the affected-class scope).
- NativeLinuxPolicyWitness inherited red: master repair.
- Mutation: every S4a/CARD-1717/CARD-1708 PC stays pending for SourceLanding Mutation, plus the new seam witness
  (mutant: drop `&& await StopLegacyContinuationAsync(systemRelease, ...) is null` or pass `null` as the gate in
  the TryRetireCoreAsync NotRequired branch).
- Review advisory S5b-1 A-1 (filing with the re-read revision token) unchanged by this integration.
