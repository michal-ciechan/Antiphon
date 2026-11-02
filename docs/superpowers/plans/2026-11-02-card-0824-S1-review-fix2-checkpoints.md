# CARD-0824 S1 review round 2 fix: checkpoint amendment

Code task aa89c309, 2026-11-02, server2 (Linux). Fixes review f4c293bc D-1 (drain `slot_unknown` bounded, operator
recorded release) and D-2 (default handoff digest omits `WorkspaceColdReset`). This file amends only the
`### Checkpoints` table of [the round 1 fix amendment](2026-11-02-card-0824-S1-review-fix-checkpoints.md) for this
round's touched rows. Rows are copied from it with fresh build output names and these changes:

- CP-4 Min 24 (adds `C0824_DefaultHandoffDigestIsThePreSlotDigest`; D-2, V-12 flag-off identity).
- CP-21 Min 33 (adds `C0824_DrainSlotUnknownIsBoundedAndAnOperatorRecordsTheRelease`; D-1).
- CP-1, CP-8 and CP-9 are unchanged (the runner contract gains one constant); V-12r stays inside CP-1.

One run: CP-1, CP-8, CP-9, CP-4, CP-21.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-f2r/ | portable-slot-reset | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotResetTests/* | V-1, V-1b, V-2, V-11, V-12r, V-27, V-38, V-44, V-45, V-46 | class, 0 failed/skipped | 10 | 4 | true |
| CP-8 | S1 | CP-1 | portable-slot-rehome | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotRehomeTests/* | V-34, V-34b, V-34c, V-35a, V-35b, V-51, V-52, D-1 | class, 0 failed/skipped | 14 | 2 | true |
| CP-9 | S1 | CP-1 | portable-slot-cold-reset | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotColdResetTests/* | V-28a, V-28b, V-28c, V-29, V-30a, V-30b, V-30c, V-53, D-2 | class, 0 failed/skipped | 12 | 2 | true |
| CP-4 | S1 | tests/Antiphon.Tests -> bin-c0824-f2s/ | portable-slot-server | /*/Antiphon.Tests.Application/C0824WorkspaceSlotServerTests/* | V-10a, V-10b, V-12, V-13, V-37a, V-37b, V-39, V-40, V-41, V-42, V-43, V-55, D-3 reconciler | class, 0 failed/skipped | 24 | 14 | true |
| CP-21 | S1 | CP-4 | portable-drain-rebind-slot | /*/Antiphon.Tests.Application/PhoneHomeRollingRunnerTests/* | D-3 drain rebind, CARD-0727 drain regression | existing class, 0 failed/skipped | 33 | 15 | true |
