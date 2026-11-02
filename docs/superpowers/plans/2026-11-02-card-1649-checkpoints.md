# CARD-1649 A-1 A-2: checkpoint amendment

Code task 5ff161fd, 2026-11-02, server2 (Linux). Fixes review f742a321 advisories A-1 (the record-release route
stamps only the stuck task attempt and never a Dispatched, Working or Blocked task) and A-2 (the audit event
names the requester). This file amends only the `### Checkpoints` table of
[the CARD-0824 S1 round 2 fix amendment](2026-11-02-card-0824-S1-review-fix2-checkpoints.md). Rows are copied
from it with fresh build output names and these changes:

- CP-21 Min 34 (adds `C1649_RecordReleaseStampsOnlyTheStuckAttemptOfATaskThatIsNotLive`; A-1, A-2).
- CP-4 is unchanged (Min 24); it keeps `C0824_ReconcilerBoundsSlotUnknownRetries` (the reconciler half of the
  attempt-keyed count) and V-12 flag-off identity.
- CP-1 is unchanged; it carries V-12r. The runner is not touched by this change.

One run: CP-1, CP-4, CP-21.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1649-r/ | portable-slot-reset | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotResetTests/* | V-1, V-1b, V-2, V-11, V-12r, V-27, V-38, V-44, V-45, V-46 | class, 0 failed/skipped | 10 | 6 | true |
| CP-4 | S1 | tests/Antiphon.Tests -> bin-c1649-s/ | portable-slot-server | /*/Antiphon.Tests.Application/C0824WorkspaceSlotServerTests/* | V-10a, V-10b, V-12, V-13, V-37a, V-37b, V-39, V-40, V-41, V-42, V-43, V-55, D-3 reconciler | class, 0 failed/skipped | 24 | 20 | true |
| CP-21 | S1 | CP-4 | portable-drain-rebind-slot | /*/Antiphon.Tests.Application/PhoneHomeRollingRunnerTests/* | D-3 drain rebind, CARD-0727 drain regression, CARD-1649 A-1 A-2 | existing class, 0 failed/skipped | 34 | 25 | true |
