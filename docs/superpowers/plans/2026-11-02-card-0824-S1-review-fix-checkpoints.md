# CARD-0824 S1 review round 1 fix: checkpoint amendment

Code task 00abf1bf, 2026-11-02, server2 (Linux). Fixes review 3a354688 D-1 (ownership before destructive git),
D-2 (cold-reset preflight) and D-3 (drain rebind slot release). This file amends only the `### Checkpoints`
table of [the S1 code checkpoint amendment](2026-11-02-card-0824-S1-code-checkpoints.md). Rows are copied from
it with fresh build output names and these changes:

- CP-2 Min 16 (adds `C0824_RestartKeepsLeasedSlotForTheServersRelease`, D-3 restart recovery).
- CP-8 Min 14 (adds `C0824_ReleaseProvesOwnershipBeforeAnyDestructiveGit` x3: primary, sibling, symlink; D-1).
- CP-9 Min 12 (adds `C0824_ColdResetQuarantinesWhatALeaseWould` x3; D-2).
- CP-4 Min 23 (adds `C0824_ReconcilerBoundsSlotUnknownRetries`; D-3).
- CP-21 (new): the full `PhoneHomeRollingRunnerTests` class, the affected integration class of the dispatcher's
  drain rebind (`RebindDrainingAsync`) and of `RemoteWorkspaceService.RemoveMirrorAsync`; it carries the new
  `C0824_DrainRebindReleasesTheOldSlotFirst` x2 (D-3) and the existing CARD-0727 drain witnesses. 30 tests
  before this fix, 32 after.

Run A: CP-1, CP-2, CP-3, CP-6..CP-10, CP-15, CP-16. Run B: CP-4, CP-5, CP-17..CP-21.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-f1r/ | portable-slot-reset | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotResetTests/* | V-1, V-1b, V-2, V-11, V-12r, V-27, V-38, V-44, V-45, V-46 | class, 0 failed/skipped | 10 | 4 | true |
| CP-2 | S1 | CP-1 | portable-slot-custody | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotCustodyTests/* | V-3a, V-3b, V-4, V-6, V-6b, V-7a, V-7b, V-8, V-9, V-47, V-48, V-49, D-3 restart | class, 0 failed/skipped | 16 | 2 | true |
| CP-3 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-f1l/ | linux-native-slot-process | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotProcessScanTests/* | V-5a, V-5b, V-5c | class, 0 failed/skipped | 3 | 4 | true |
| CP-6 | S1 | CP-1 | portable-binding-regression | /*/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests/* | R-2 | existing class, 0 failed/skipped | 20 | 3 | true |
| CP-7 | S1 | CP-1 | portable-slot-overflow | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotOverflowTests/* | V-31a, V-31b, V-32a, V-32b, V-33a, V-33b, V-50 | class, 0 failed/skipped | 9 | 2 | true |
| CP-8 | S1 | CP-1 | portable-slot-rehome | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotRehomeTests/* | V-34, V-34b, V-34c, V-35a, V-35b, V-51, V-52, D-1 | class, 0 failed/skipped | 14 | 2 | true |
| CP-9 | S1 | CP-1 | portable-slot-cold-reset | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotColdResetTests/* | V-28a, V-28b, V-28c, V-29, V-30a, V-30b, V-30c, V-53, D-2 | class, 0 failed/skipped | 12 | 2 | true |
| CP-10 | S1 | CP-3 | linux-native-slot-rehome | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotNativeRehomeTests/* | V-36 | class, 0 failed/skipped | 1 | 2 | true |
| CP-15 | S1 | CP-1 | portable-native-policy-regression | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | R-4 (+ R-6 notes) | existing class, 0 failed/skipped | 3 | 1 | true |
| CP-16 | S1 | CP-1 | portable-workspace-service-regression | /*/Antiphon.SessionRunner.Tests/RunnerWorkspaceServiceTests/* | R-3 | existing class, 0 failed/skipped | 35 | 3 | true |
| CP-4 | S1 | tests/Antiphon.Tests -> bin-c0824-f1s/ | portable-slot-server | /*/Antiphon.Tests.Application/C0824WorkspaceSlotServerTests/* | V-10a, V-10b, V-12, V-13, V-37a, V-37b, V-39, V-40, V-41, V-42, V-43, V-55, D-3 reconciler | class, 0 failed/skipped | 23 | 14 | true |
| CP-5 | S1 | CP-4 | portable-slot-regression | /*/Antiphon.Tests.Application/RemoteWorkspacePreparerTests/* | R-1 | existing class, 0 failed/skipped | 18 | 3 | true |
| CP-17 | S1 | CP-4 | portable-seat-ambiguity-regression | /*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/Legacy_ambiguous_answer_requires_fresh_absence_without_another_release | R-5 | method, 0 failed/skipped | 1 | 2 | true |
| CP-18 | S1 | CP-4 | portable-dispatcher-ops-regression | /*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/Supported_operations_preserve_contracts | dispatcher handles every PhoneHomeOperation | method, 0 failed/skipped | 1 | 2 | true |
| CP-19 | S1 | CP-4 | portable-migration-regression | /*/Antiphon.Tests.Application/AgentTaskInternalDecisionMigrationTests/* | no pending model change after Card0824WorkspaceSlots | existing class, 0 failed/skipped | 1 | 3 | true |
| CP-20 | S1 | CP-4 | portable-delegate-script-regression | /*/Antiphon.Tests.Application/DelegateScriptKindTests/* | delegate.ps1 unchanged except -ColdWorkspace | existing class, 0 failed/skipped | 38 | 6 | true |
| CP-21 | S1 | CP-4 | portable-drain-rebind-slot | /*/Antiphon.Tests.Application/PhoneHomeRollingRunnerTests/* | D-3 drain rebind, CARD-0727 drain regression | existing class, 0 failed/skipped | 32 | 15 | true |
