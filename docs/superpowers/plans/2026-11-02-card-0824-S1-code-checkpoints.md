# CARD-0824 S1 Code: checkpoint amendment

Code task cf298805, 2026-11-02, server2 (Linux). This file amends only the `### Checkpoints` table of the
[S1 test design](2026-11-02-card-0824-S1-test-design.md) for the Code stage's runs; the design itself is
unchanged. Rows CP-1..CP-3, CP-5..CP-10 and CP-15..CP-17 are copied verbatim. CP-4's executed floor is
15, not 28: the server class carries V-10a, V-10b (2 cases), V-12 (2), V-13 (2), V-37a, V-39 and V-55 (6).
The real-transport delivery witnesses V-40..V-43, V-54 (land cleanup), V-56 (base selection) and V-37b
(the cold-reset route) are deferred to a follow-up Code slice and are not claimed by this table.
V-10b is witnessed at the shared removal primitive `RemoteWorkspaceService.RemoveMirrorAsync` (the
retirement and drain re-arm consumers both call it), with the cases `slot-managed-refusal` and
`slot-bound-no-request` instead of one case per consumer.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-s1r/ | portable-slot-reset | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotResetTests/* | V-1, V-1b, V-2, V-11, V-12r, V-27, V-38, V-44, V-45, V-46 | class, 0 failed/skipped | 10 | 4 | true |
| CP-2 | S1 | CP-1 | portable-slot-custody | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotCustodyTests/* | V-3a, V-3b, V-4, V-6, V-6b, V-7a, V-7b, V-8, V-9, V-47, V-48, V-49 | class, 0 failed/skipped | 15 | 2 | true |
| CP-3 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-s1l/ | linux-native-slot-process | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotProcessScanTests/* | V-5a, V-5b, V-5c | class, 0 failed/skipped | 3 | 4 | true |
| CP-6 | S1 | CP-1 | portable-binding-regression | /*/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests/* | R-2 | existing class, 0 failed/skipped | 20 | 3 | true |
| CP-7 | S1 | CP-1 | portable-slot-overflow | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotOverflowTests/* | V-31a, V-31b, V-32a, V-32b, V-33a, V-33b, V-50 | class, 0 failed/skipped | 9 | 2 | true |
| CP-8 | S1 | CP-1 | portable-slot-rehome | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotRehomeTests/* | V-34, V-34b, V-34c, V-35a, V-35b, V-51, V-52 | class, 0 failed/skipped | 11 | 2 | true |
| CP-9 | S1 | CP-1 | portable-slot-cold-reset | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotColdResetTests/* | V-28a, V-28b, V-28c, V-29, V-30a, V-30b, V-30c, V-53 | class, 0 failed/skipped | 9 | 2 | true |
| CP-10 | S1 | CP-3 | linux-native-slot-rehome | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotNativeRehomeTests/* | V-36 | class, 0 failed/skipped | 1 | 2 | true |
| CP-15 | S1 | CP-1 | portable-native-policy-regression | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | R-4 (+ R-6 notes) | existing class, 0 failed/skipped | 3 | 1 | true |
| CP-16 | S1 | CP-1 | portable-workspace-service-regression | /*/Antiphon.SessionRunner.Tests/RunnerWorkspaceServiceTests/* | R-3 | existing class, 0 failed/skipped | 35 | 3 | true |
| CP-4 | S1 | tests/Antiphon.Tests -> bin-c0824-s1s/ | portable-slot-server | /*/Antiphon.Tests.Application/C0824WorkspaceSlotServerTests/* | V-10a, V-10b, V-12, V-13, V-37a, V-39, V-55 | class, 0 failed/skipped | 15 | 14 | true |
| CP-5 | S1 | CP-4 | portable-slot-regression | /*/Antiphon.Tests.Application/RemoteWorkspacePreparerTests/* | R-1 | existing class, 0 failed/skipped | 18 | 3 | true |
| CP-17 | S1 | CP-4 | portable-seat-ambiguity-regression | /*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/Legacy_ambiguous_answer_requires_fresh_absence_without_another_release | R-5 | method, 0 failed/skipped | 1 | 2 | true |
