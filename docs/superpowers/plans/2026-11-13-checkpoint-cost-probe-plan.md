# Checkpoint cost: shared ordinary classification probe

The guard union already uses one process. Its classification cases unnecessarily
rebuild the same ordinary probe six times. Preserve the distinct method-level Slow
probe and its live assembly inspection; share only the immutable ordinary result,
after existing owned temporary-directory cleanup completes. No slot policy,
checkpoint selection, output deletion or timeout changes.

## Performance budget

Debian 12/server2, SDK 10.0.404, 24 cores, contended four-slot broker. Historical
S4a TestClassificationPolicyTests 41 cases cost approximately 141 seconds (one
loaded observation, not p95). A shared ordinary probe should avoid five builds
and fifteen probe launches per full class. Build, admission and output cleanup
remain separate checkpoint phases. Record the two-request witness elapsed time
before/after on this host; retain the narrow green class's TRX. Expected witness
execution under 120 seconds, full class under 180 seconds, provisional; importer
15-minute row floor retained rather than changed here. No p95 or full CP-90
speedup is claimed. Collect next required Review and three ordinary run samples
under CARD-1864/1869, without extra green repetitions. Fixture lifetime is per
class/process and lazy; distinct method-level metadata probe keeps fresh setup.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1864-red/ | probe-red | `/*/*/TestClassificationPolicyTests/C1864_ordinary_probe_is_shared_after_its_directory_is_removed` | shared immutable probe | intended identity assertion red | 1 | 2 |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1864-green/ | classification-guards | `/*/*/TestClassificationPolicyTests/*` | every existing classification assertion and shared result | zero failed/skipped | 42 | 3 |
