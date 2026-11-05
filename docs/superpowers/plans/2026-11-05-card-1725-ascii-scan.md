# CARD-1725: bounded ASCII harness cost

Original Code task / landing owner: 7626aee3. Base 388ff482046a62f813a251208bfeabe7c21808ea.

## Evidence and implementation

The Linux probe starts an eight-second clock before pwsh startup, Add-Type compilation and the ASCII pipeline. On the base, a gated manual real-harness probe took 7.380s / 212688896 bytes peak RSS with a one-byte runner and the real 75349-byte harness. With a 2 MiB ASCII runner it exceeded eight seconds, was killed by that watchdog and returned 137 (8.210s / 216866816 bytes). Local cgroup oom_kill and failcnt were both zero. This explains a concrete timeout-kill mechanism, not the exact historical Review process (its kernel evidence is unavailable). Ambient load 4.10 then 7.07 on the 24-CPU host. Retain the raw diagnostic logs externally/ignored.

Invariant: the real C585 harness scans every bounded reader byte and reports the exact non-ASCII count while completing inside the unchanged watchdog. Replace per-byte pipeline dispatch with a plain byte loop if the measurement confirms improvement. No product behavior, reader limit, deadline or assertion changes. New witnesses send 2 MiB through each actual ASCII call site: R3 runner and R4 parsed harness replaced after parsing, using the existing observation fixture. NUL is ASCII (<=127); original ordinary and invalid-byte controls remain. The pre-fix commit adds witnesses only, then CP-1 must fail on watchdog assertions before the fix.

## Ordinary verification

- V-1: C1725_LargeAsciiCallers R3 and R4, real harness, 2 MiB ASCII; no watchdog, exit 0, both PASS rows.
- V-2: C1233_FileCallers R1-R4, original regular/hash-change/non-ASCII/FIFO admission and independent read-duration assertions unchanged.
- V-3: manual Linux before/after wall time and sampled peak RSS; retain unknown phase attribution. Inspect exact-count behavior and all reads remain bounded.
- R-1: full MechanicalPcContractTests plus TestClassificationPolicyTests, TestClassificationGuardTests, SlowTestRegistryMergeTests, SlowTestTripwireTests.
- R-2: whole Unit lane partitioned A-D/E-H/I-L/M-P/Q-T/U-Z. Broad invariant: no Unit regression from test-harness change. Unit omits delivery, landing, leases and persistence. Unbounded downstream impact: none; no shared runtime code changes. Selected class sets in these shards are deliberately all Unit classes, with provisional aggregate cost 35m. No full-assembly run.

No OS-selected production behavior changes: native Linux FIFO fixtures and existing probe used unchanged. Runtime real object is pwsh launched by CheckpointBoundedProbe with copied production scripts; no new production OS seam. Native Windows qualification not required for this script-only portable scan; Linux witnesses use real input and outcomes. rc startup unaffected. Restart: none.

## Performance budget

Fresh isolated checkpoint build once per group; reuse across rows. Cold build estimate 5m; CP-1 execution <=60s per parameter, original eight-second probe and two-second reap unchanged. Each sample includes fresh pwsh because script startup/compilation is part of this test subject, not a shareable DB/host fixture. Roots isolated via CheckpointTestBase, process lane limiter unchanged. CP-1 estimate 6m, guards 2m, Unit shards 35m aggregate, total 43m provisional plus bootstrap/admission. No measured p95: base manual sample n=1 per size; collect one matching post-fix pair, no repeats after green. CARD-1725/CARD-1404 track the provisional estimates and checkpoint importer 15m floor/3x estimate exception. Slot admission bounded separately at default 45m. No selection expected >30m; if observed trigger caller Debug with evidence. Per-probe elapsed/read durations retained in TRX stdout; startup/compile versus scan attribution initially unknown. Manual profiling samples peak RSS every 20ms; values are lower bounds. No synthetic host load is created.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1725-final/` | ascii-files | `/*/*/CheckpointInheritedFileTests*/*` | V-1,V-2 | 6 cases, 0 failed/skipped after fix | 6 | 6 | true |
| CP-2 | S1-S2 | CP-1 | required-guards | `/*/*/(MechanicalPcContractTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)/*` | R-1 | all classes, 0 failed/skipped | 1 | 2 | true |
| CP-3 | S1-S2 | CP-1 | unit-ad | `/*/*/(A*)\|(B*)\|(C*)\|(D*)/*[Category=Unit]` | R-2 | nonzero, 0 failed/skipped | 1 | 10 | true |
| CP-4 | S1-S2 | CP-1 | unit-eh | `/*/*/(E*)\|(F*)\|(G*)\|(H*)/*[Category=Unit]` | R-2 | nonzero, 0 failed/skipped | 1 | 3 | true |
| CP-5 | S1-S2 | CP-1 | unit-il | `/*/*/(I*)\|(J*)\|(K*)\|(L*)/*[Category=Unit]` | R-2 | nonzero, 0 failed/skipped | 1 | 3 | true |
| CP-6 | S1-S2 | CP-1 | unit-mp | `/*/*/(M*)\|(N*)\|(O*)\|(P*)/*[Category=Unit]` | R-2 | nonzero, 0 failed/skipped | 1 | 7 | true |
| CP-7 | S1-S2 | CP-1 | unit-qt | `/*/*/(Q*)\|(R*)\|(S*)\|(T*)/*[Category=Unit]` | R-2 | nonzero, 0 failed/skipped | 1 | 10 | true |
| CP-8 | S1-S2 | CP-1 | unit-uz | `/*/*/(U*)\|(V*)\|(W*)\|(X*)\|(Y*)\|(Z*)/*[Category=Unit]` | R-2 | nonzero, 0 failed/skipped | 1 | 2 | true |

### Mutation

PC-1/R3 and PC-2/R4: pending for SourceLanding Mutation. Revert only the corresponding ASCII scan from loop to Where-Object pipeline; exact C1725_LargeAsciiCallers method selector (both parameters); expected watchdog assertion red, restore green. Candidate under docs/superpowers/mutations/2026-11-05-card-1725-candidate.md is unbound. Code owns no deliberate mutants. Ordinary red/green is separate from Mutation credit.

## Measured implementation result

S2 uses plain foreach counting, retaining every-byte classification and exact count. The V-1 method additionally places invalid bytes 128,255,129 across the first and final positions of each 2 MiB input and requires exactly three reported errors within the same watchdog. This prevents an early-exit or boolean-only scan from satisfying the test.

Ordinary red SHA 794bbf04a8de4f09c489722344791453fe09417e, CP-1 run 20261105-184534-6c06: 6 executed, 4 passed, 2 watchdog assertion failures (R3/R4), zero skips; original four cases green. Clean source, verified build, slot=granted waited=0s. Fix diagnostic (uncommitted, not qualification): one-byte runner plus real harness 2.830s / 210710528 bytes sampled peak RSS; 2 MiB runner 2.755s / 216240128 bytes, both exit 0, no watchdog, load 14.99. Memory is essentially unchanged; this is a CPU/pipeline-dispatch cost repair, not an OOM repair. Fixed ordinary checkpoints must still qualify committed S2. No p95 claim.
