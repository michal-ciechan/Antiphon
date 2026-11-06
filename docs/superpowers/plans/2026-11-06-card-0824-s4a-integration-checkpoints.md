# CARD-0824 S4a integration onto master (task 5ec136af): checkpoints

Source: feat/card-task-5ec136af, the S4a stack (origin/feat/card-task-bb0a6ce8 tip f45832647) replayed onto master
1a22e0d2c by cherry-pick -x, plus the S4a x S5b-1 seam fix. Resolution record:
`docs/investigations/2026-11-06-s4a-integration-onto-master.md`.

Rows CP-1..CP-3 are the S4a closed list of the CARD-1708 Code/Review manifest (Review 39f7bfe1
`code-checkpoints.md`), with `WorktreeNoFollowDeleteTests` (an S4a class) added to CP-2 and
`NativeLinuxPolicyWitnessTests` to CP-3. Rows CP-4 and CP-5 are the S5b-1 RefOnly rows of
`docs/superpowers/plans/2026-11-05-card-0824-s5b-ref-only-plan.md` (CP-1..CP-3 there), as whole classes.
At most three rows per run (Debug 5ae98d31): run A = CP-1..CP-3, run B = CP-4, CP-5.

## Performance budget

One cold build per run, serial rows; UseAppHost=false, TEMP=/tmp. Estimates from run 20261106-014505-8730
(CP-1 95 s, CP-2 326 s, CP-3 177 s execution) and the S5b-1 runs (CP-1 35 s, CP-2+CP-3 83 s, C508 33 s).
Loaded-host ceilings, not p95.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | I1 | tests/Antiphon.Tests -> bin-c0824int-a/ | s4a-policy-switch-salvage | /*/*/(C1708ReflogSalvageTests*)\|(C0824DesktopLegacyReleaseSwitchTests*)\|(C0824DesktopLegacyReleaseNativeTests*)\|(C0824DesktopLegacyReleaseSettingsTests*)\|(C0824DesktopLegacyReleasePolicyTests*)/* | S4a policy, off switch, settings, CARD-1708 salvage | nonzero, 0 failed; Windows-native skips owed | 190 | 6 | true |
| CP-2 | I1 | CP-1 | s4a-removal-cleanup | /*/*/(TaskWorktreeRetirementTests*)\|(WorktreeResidueSweepTests*)\|(WorktreeGuardedCleanupTests*)\|(SettledWorktreeRemovalTests*)\|(LandingGitTests*)\|(LandingRemovalPolicyControlTests*)\|(WorktreeCleanupJournalTests*)\|(WorktreeCleanupPresentationTests*)\|(WorktreeCleanupSettingsValidatorTests*)\|(CardDoneWorktreeCleanupTests*)\|(CardWorktreeCleanupRecoveryTests*)\|(CompletedCardWorktreeRemovalTests*)\|(AgentTaskLandPublicationTests*)\|(WorktreeNoFollowDeleteTests*)/* | GuardedWorktreeRemoval, no-follow delete, retirement, WorktreeCleanup consumers | nonzero, 0 failed; Windows-native skips owed | 500 | 14 | true |
| CP-3 | I1 | CP-1 | s4a-retention-guards | /*/*/(C1677RetentionServerTests*)\|(C1677RetentionSettingsTests*)\|(PhoneHomeConnectionTests*)\|(C0824WorkspaceSlotServerTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(MechanicalPcContractTests*)\|(NativeLinuxPolicyWitnessTests*)/* | RunnerWorkspaceRetention, slot server, policy sweep, mechanical contract | nonzero, 0 failed | 150 | 8 | true |
| CP-4 | I2 | tests/Antiphon.Tests -> bin-c0824int-b/ | s5b1-ref-only | /*/Antiphon.Tests.Application/(C0824RefOnlyIdentityTests*)\|(C0824RefOnlyGateTests*)\|(C0824RefOnlyLandingTests*)\|(C0824RefOnlyCleanupTests*)/* | S5b-1 identity, gate, late land, cleanup incl. the S4a-off no-claim mirror witness | nonzero, 0 failed/skipped | 17 | 6 | true |
| CP-5 | I2 | CP-4 | s5b1-legacy-base | /*/Antiphon.Tests.Application/DelegationWorktreeTests/(C508_ReuseKeepsRecordedBase*)\|(C508_BaseResolutionRequiresOwnedLease*) | Checkout base/admission unchanged | both intended methods, 0 failed/skipped | 2 | 2 | true |
