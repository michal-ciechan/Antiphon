# CARD-1660: C0824WorkspaceSlotServerTests per-test budget

Code task 08c004b5, 2026-11-02, server2 (Linux), base 30cb5279b. Test-only: the class-level
`[Timeout(20_000)]` on `C0824WorkspaceSlotServerTests` (CARD-0824 S1 CP-4) becomes `[Timeout(35_000)]`,
about 2x the measured loaded per-test p95 (R5). No assertion, inner bound or product file changes.

Measurements (native Debug build of `tests/Antiphon.Tests` at 30cb5279b, whole class, TRX per-test durations;
the 24 cases run in parallel, each building its own rig, so a duration includes sibling contention):

| Run | Load (1 min) | Class wall | p50 | p95 | max | Result |
|---|---|---|---:|---:|---:|---|
| alone 1 (first run after build) | 24.3 -> 28.7 | 76.7 s | 11.34 | 15.81 | 15.83 | 24/24 |
| alone 2 | 28.7 -> 21.0 | 30.2 s | 10.29 | 12.62 | 12.71 | 24/24 |
| alone 3 | 21.0 -> 17.6 | 31.4 s | 10.24 | 13.09 | 13.14 | 24/24 |
| beside 16 busy threads (one bounded burner pid) | 16.8 -> 37.2 | 50.3 s | 14.69 | 17.20 | 17.27 | 24/24 |

Loaded p95 17.2 s is 86% of the old 20 s budget; three loaded runs on 2026-11-02 (Merge batch 10 stage B,
Merge batch 13, the CARD-1649 worker) went red at 20 s. New budget ceil(2 x 17.27) = 35 s. This is a small
sample (four runs), provisional under docs/timeout-audit.md; re-audit after 20 comparable runs.

Setup is per test (isolated schema, PhoneHomeTestHost, scripted peer; transport cases add a scratch git
origin and clone) and is configured differently by each test (settings, services, capabilities, interceptors),
so sharing it once per class is not a simple or safe change and is not made here. The cold-start cost is the
process-wide first-use cost (a serial rerun on 2026-11-02 measured the first case at about 9 s, the rest about 1 s).

`TimeoutAuditTests` uses fixture repositories and pins no value from this class; it runs once unchanged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1660/ | slot-server-budget | /*/Antiphon.Tests.Application/C0824WorkspaceSlotServerTests/* | CARD-0824 CP-4 class at the 35 s budget | class, 0 failed/skipped | 24 | 3 | true |
| CP-2 | S1 | CP-1 | timeout-audit | /*/*/TimeoutAuditTests/* | timeout census unchanged | class, 0 failed/skipped | 7 | 1 | true |
