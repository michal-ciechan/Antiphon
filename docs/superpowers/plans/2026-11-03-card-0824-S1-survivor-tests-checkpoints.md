# CARD-0824 S1 mutation survivor witnesses: checkpoint amendment

Code task a9f45276, 2026-11-03, server2 (Linux). Test-only. It adds one witness per survivor named by the
mutation sweep `docs/investigations/2026-11-03-mutation-0824-s1.md` (task e87df8c7, on
`feat/card-task-e87df8c7`, commit 2c6e7cebb). No production file changes.

| Survivor | Guard | New case | Class |
|---|---|---|---|
| X37 | retirement preflight (`RetireIdleAsync`) | `C0824_IdleOverflowWithLateWorkQuarantinedNotRemoved` x3 (untracked, unpushed-head, reflog-only) | `C0824WorkspaceSlotOverflowTests` |
| X17 | lease-time free check sees a holder (`CheckFreeAsync`) | `C0824_LeaseQuarantinesFreeSlotWithHolder` | `C0824WorkspaceSlotCustodyTests` |
| X11 | unpushed check fails closed (`UnpushedAsync`) | `C0824_UnpushedCheckFailureQuarantinesNotResets` | `C0824WorkspaceSlotRehomeTests` |
| X31 | disposition allowlist (`ReleaseQuarantineAsync`) | `C0824_QuarantineReleaseUnknownDispositionRefused` x3 (pushed, Bogus, empty) | `C0824WorkspaceSlotRehomeTests` |
| X06 | only a Leased slot is settled (`ReleaseAsync`) | `C0824_ReleaseOfLeasingSlotRefused` | `C0824WorkspaceSlotCustodyTests` |
| X47 | unreadable slot ledger keeps everything (retention guard 3b) | `Unreadable_slot_ledger_keeps_a_removable_mirror` | `C1677RetentionEngineTests` |
| PC-74 | slot lease under the dispatcher `MutationLock` (G-74) | `C0824_LeaseWaitsForTheDispatcherMutationLock` | `C0824WorkspaceSlotCustodyTests` |

The new cases sit in existing classes. Overflow and Rehome already carry `[NativeLinuxOnly]` with policy rows;
Custody and the retention engine class stay portable. The policy census is unchanged, and CP-9 is its regression.

Mins: Custody 16 -> 19, Overflow 9 -> 12, Rehome 14 -> 18, retention engine 49 -> 50. CP-5..CP-8 are the
remaining C0824 runner classes, unchanged, run as regression for the shared scratch helper
(`C0824StoreRuntime` gained an optional Input gate).

One run: S1.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-sv/ | slot-custody | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotCustodyTests/* | X17, X06, PC-74 + existing V-3..V-49 | class, 0 failed/skipped | 19 | 3 | true |
| CP-2 | S1 | CP-1 | slot-overflow | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotOverflowTests/* | X37 + existing V-31..V-50 | class, 0 failed/skipped | 12 | 3 | true |
| CP-3 | S1 | CP-1 | slot-rehome | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotRehomeTests/* | X11, X31 + existing V-34..V-52, D-1 | class, 0 failed/skipped | 18 | 3 | true |
| CP-4 | S1 | CP-1 | retention-engine | /*/Antiphon.SessionRunner.Tests/C1677RetentionEngineTests/* | X47 + existing CARD-1677 engine cases | class, 0 failed/skipped | 50 | 4 | true |
| CP-5 | S1 | CP-1 | slot-reset-regression | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotResetTests/* | regression | existing class, 0 failed/skipped | 10 | 3 | true |
| CP-6 | S1 | CP-1 | slot-cold-reset-regression | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotColdResetTests/* | regression | existing class, 0 failed/skipped | 12 | 3 | true |
| CP-7 | S1 | CP-1 | slot-process-scan-regression | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotProcessScanTests/* | regression | existing class, 0 failed/skipped | 3 | 2 | true |
| CP-8 | S1 | CP-1 | slot-native-rehome-regression | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotNativeRehomeTests/* | regression | existing class, 0 failed/skipped | 1 | 2 | true |
| CP-9 | S1 | CP-1 | native-policy-regression | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | policy census unchanged | existing class, 0 failed/skipped | 3 | 1 | true |
