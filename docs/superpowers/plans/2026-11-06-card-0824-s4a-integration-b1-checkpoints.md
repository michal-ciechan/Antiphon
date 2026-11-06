# CARD-0824 S4a integration Review B-1 repair (task 8b9b7a9a): checkpoints

Source: feat/card-task-8b9b7a9a, stacked on the S4a integration tip f3a982888 (feat/card-task-5ec136af).
Review d8af9f97 B-1: the S5b-1 no-checkout (RefOnly) mirror branch of `TaskWorktreeRetirementService.TryRetireCoreAsync`
passes a live S4a gate to the mirror remove, and no witness turned the switch off after that branch's admission.
New witness `C0824DesktopLegacyReleaseSwitchTests.Late_switch_on_ref_only_mirror_continuation_sends_no_remove`
(runner-description cut, queued-send cut, switch-on control). Test-only change; no production edit.

Rows: CP-1 the new witness with the S4a switch/policy/settings/native classes and the RefOnly cleanup class;
CP-2 the retirement/removal and WorktreeCleanup consumers (GuardedWorktreeRemoval has no class of its own; its
consumers are these classes); CP-3 the policy sweep and mechanical contract. One run, three rows (Debug 5ae98d31).

## Performance budget

One cold build, serial rows; UseAppHost=false, TEMP=/tmp. Estimates from integration run 20261106-014505-8730 and
Review run 20261106-043327-2e51 (CP-1 95 s plus RefOnly cleanup about 35 s, CP-2 326 s, CP-3 177 s execution).
Loaded-host ceilings, not p95.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | I1 | tests/Antiphon.Tests -> bin-c0824b1-a/ | s4a-switch-ref-only | /*/*/(C0824DesktopLegacyRelease*)\|(C0824RefOnlyCleanupTests*)/* | B-1 RefOnly late-switch witness, S4a off switch, policy, settings, native, RefOnly cleanup | nonzero, 0 failed; Windows-native skips owed | 70 | 8 | true |
| CP-2 | I1 | CP-1 | s4a-removal-cleanup | /*/*/(TaskWorktreeRetirementTests*)\|(WorktreeResidueSweepTests*)\|(WorktreeGuardedCleanupTests*)\|(SettledWorktreeRemovalTests*)\|(WorktreeCleanupJournalTests*)\|(WorktreeCleanupPresentationTests*)\|(WorktreeCleanupSettingsValidatorTests*)\|(CompletedCardWorktreeRemovalTests*)\|(WorktreeNoFollowDeleteTests*)/* | retirement, residue, guarded removal consumers, WorktreeCleanup, no-follow delete | nonzero, 0 failed; Windows-native skips owed | 200 | 14 | true |
| CP-3 | I1 | CP-1 | policy-sweep | /*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)/* | policy sweep, mechanical PC contract | nonzero, 0 failed | 61 | 8 | true |
