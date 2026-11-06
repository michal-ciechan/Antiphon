# CARD-0824 S5b-2 continuation 3

Work remains incomplete. Feature admission stays OFF. C1 is the first acceptance to close; no later frozen variant is claimed complete here. Original Code/landing owner: `543d8c5b-8a8a-4796-bb6a-f9d28447cbb8`. Task `7d6aabc8`, assigned branch `feat/card-task-7d6aabc8`, worktree `/work/worktrees/task-7d6aabc8`, full task base `82efe8fb94630937152a292308b922c39beb7d89`.

Plan: `docs/superpowers/plans/2026-11-05-card-0824-s5b-ref-only-plan.md`. Verification design: `docs/superpowers/testdesign/2026-11-05-card-0824-s5b-2-testdesign.md`. Prior outcomes and owed variants remain preserved in `docs/superpowers/reports/2026-11-06-card-0824-s5b-2-code-continuation-2.md`.

## Initial verification and discovery repair

Native incremental baseline compile at the task base passed: 0 errors, 1756 warnings, compiler 184.88s; slot=granted waited=0s. This brief-required unlisted build uses native default output, `UseAppHost=false`, `TEMP=/tmp`; it also bootstraps the checkpoint tool. No baseline test credit.

Exact CP-3 first ran at base `82efe8fb94630937152a292308b922c39beb7d89`, containing the inherited retained-agent fix `ae201bde0a3c6ccdc3d670d2ffe2f35ce401367e`. Both initial attempts failed **before discovery of any method**, with no TRX and no passing or behavioral-red credit. Last behavioral C1 red remains `7e88a6e3af000edc620db4454103912be7441efc` until an actual later assertion result is recorded.

The initial hypothesis of test-concurrency configuration was disproved by the second attempt with `TUNIT_MAX_PARALLEL_TESTS=1`. Read-only IL inspection of the locally pinned TUnit 1.44 engine shows `ShouldScanAssembly` refusing `Assembly.Location.Contains("ref")`. Its collector creates a semaphore with `min(assemblyCount, 2*ProcessorCount)` and does not handle zero admitted assemblies. Direct reflection returned `False` for the real cold-output Antiphon.Tests assembly and `True` for the native-output assembly. Thus the frozen `bin-c0824-ref2-*` names are the decisive inherited path fault. The test design now uses `bin-c0824-s5b2-*`; exact filters, minima, assertions, row caps and build caps are retained. The ignored manifest pins slotWait=12m, build=20m and CP-3=6m. No framework/package change or timeout widening.

Run `20261106-052819-4f17`: build admission 15s, compiler 321.80s; row admission 45s, execution 7.937s. Run `20261106-053751-5230`: build admission 0s; row admission 60s, execution 14.332s. Both producing builds verified clean committed source; neither failed enclosing report is a green certificate. Their task-owned cold binary outputs were removed by literal absolute path after the executors joined; evidence remains ignored under `.antiphon/checkpoints/`.

The following essential lines are retained without edits:

### 20261106-052819-4f17

```text
source: 82efe8fb94630937152a292308b922c39beb7d89 state=clean buildSource=verified
CHECKPOINT CP-3 commit=82efe8fb94630937152a292308b922c39beb7d89 build=n/a filter=/*/Antiphon.Tests.Application/(C0824RefOnlyContinuationTests*)|(C0824RefOnlyConsumerTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=granted waited=45s dirty=0 source=82efe8fb94630937152a292308b922c39beb7d89 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=45s executed=7.937s wall=52.975s
BUDGET wall=475.867s execution=415.870s admissionOnly=59.998s
verdict: RED exit=2
```

### 20261106-053751-5230

```text
source: 82efe8fb94630937152a292308b922c39beb7d89 state=clean buildSource=verified
CHECKPOINT CP-3 commit=82efe8fb94630937152a292308b922c39beb7d89 build=n/a filter=/*/Antiphon.Tests.Application/(C0824RefOnlyContinuationTests*)|(C0824RefOnlyConsumerTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=granted waited=60s dirty=0 source=82efe8fb94630937152a292308b922c39beb7d89 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=60s executed=14.332s wall=74.555s
BUDGET wall=559.981s execution=499.944s admissionOnly=60.036s
verdict: RED exit=2
```
