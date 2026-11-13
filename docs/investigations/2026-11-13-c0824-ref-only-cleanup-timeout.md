# C0824 ref-only cleanup timeout: Debug 6130909c

## Red witness

Unchanged source 3457d890c9723150785a37fa5745e0cf8a8d81b0, assigned fresh worktree, native Debug output, Linux server2 (24 CPUs). Ten sequential fresh test processes: 9 passed, 1 timed out. No synthetic load, retries, timeout changes, or production changes. Build passed (0 errors; 550.86 seconds plus 45 seconds of slot waiting).

Method: C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs. Every invocation acquired its own host build slot and used the assembly-shared Postgres fixture with isolated cloned databases. The load and raw /proc/stat CPU counters were recorded before each gated invocation; load at the later lease grant may differ. Raw evidence: /work/worktrees/task-6130909c/.antiphon/debug-6130909c/.

| Run | Body seconds | Outcome | Load 1/5/15 min before queue | Outer seconds including queue/startup/teardown |
|---|---:|---|---|---:|
| 1 | 34.974 | Passed | 22.92/36.30/39.93 | 222.960 |
| 2 | 60.708 | Failed | 56.85/38.95/39.63 | 294.679 |
| 3 | 54.583 | Passed | 31.31/34.69/37.52 | 133.043 |
| 4 | 54.838 | Passed | 22.08/29.69/35.27 | 116.190 |
| 5 | 21.372 | Passed | 16.30/24.84/32.83 | 64.521 |
| 6 | 39.531 | Passed | 13.58/22.37/31.43 | 193.703 |
| 7 | 29.586 | Passed | 31.00/29.14/32.43 | 107.697 |
| 8 | 40.932 | Passed | 34.81/31.92/33.13 | 98.045 |
| 9 | 23.269 | Passed | 23.14/28.46/31.75 | 81.312 |
| 10 | 32.126 | Passed | 26.87/28.79/31.62 | 88.584 |

Linear observed p50/p95/max: 37.252/58.066/60.708 seconds. The timeout sample is censored: observed p95 is a lower bound on the corresponding full-work measurement, not proof that the work finishes within 60.7 seconds.

Run 2 is the inherited red witness: TUnit timeout at 60.708 seconds. Its phase journal records 34.747 seconds in process waits across 161 fixture Git processes; the longest recorded individual wait is 3.174 seconds. Run 1 passed in 34.974 seconds with 171 fixture Git processes, 643 SQL commands in the body, five fixture-template clones, and 10.576 seconds in process waits; its longest recorded Git wait is 1.914 seconds. These are nested measurement families and must not be added together. No evidence of a deterministic index lock, hook wait, or one non-exiting child. Unattributed remainder is 25.346 seconds in run 2 and 23.949 seconds in run 1, including uninstrumented setup/JIT/EF/filesystem work; it is not zero.

## Diagnosis and repair direction

One integration test combines four retirement consumers, three legacy-policy consumers, residue/health projections, a complete landing protocol plus guarded cleanup, four completed-card consumers, and two persistent-report fixtures. The original desktop world also carries prior consumer tasks into later policy scans. Independent scenarios share one fixed 60-second wall. Split these by fixture/consumer family, keep every assertion and consumer case, and keep bounded per-test deadlines. Raising the aggregate timeout would need at least roughly 116 seconds from this censored p95 and is expressly rejected.

The existing failing test is the test-first witness for a test-only scheduling/fixture-lifetime repair; no production assertion is weakened. The candidate preserves all 121 Should assertion calls and all consumer loop selectors.

## History and why missed

CARD-0824 is 4cda8fe1-f2b7-4df6-a8c5-b5f272d011be on board 8988ca03-7414-47ad-b0b6-51556c701703. The test was introduced by ba841fcbb99baa67b0f5b0b5683f492a9ef82eca (2026-11-05); c8e61a69d18e876d84794e34e594e1d9a437965d added the two persistent-report arms later that day. The prior investigation docs/investigations/2026-11-10-why-missed-g97-c0824-ref-only-cleanup-red.md records 10.2 seconds for this method on Linux. That is one historical sample, not its introducing-commit p95; no introducing-commit timing receipt was found.

Recent changes include 0b574e1e3 (2026-11-10, in-process DesktopLegacyWorld commits/pushes), 11c77fae6 (2026-11-09, cloned landing fixtures), and a24e32209 (2026-11-12, admit a temp source with a persistent report root). The first two reduce setup work. The last repairs a distinct deterministic early fixture refusal and makes the full report arm reachable. The method body itself has not changed since the November 5 durable-provenance work. No new input-specific product hang was established.

CARD-1894 Review selected this exact class in CP-2 and reported 367/367 green. The test was not omitted. A single green selection cannot reveal an intermittent tail demonstrated here; the prior Review's exact host load is unknown, so lower prior load is an explanation, not an established historical fact. Selection omissions and that earlier temp-root refusal are separate issues.

## Windows boundary

No Windows/desktop checkout, run, session, or logs were accessed. For rc run 97150a0b, inspect the TRX UnitTestResult for the original fully qualified desktop-consumer method, its outcome/duration and any timeout output. This method has no declared Windows skip. tests/expected-platform-skips.json lists only Remote_cleanup_still_uses_its_binding_and_generation from this class, with the Linux phone-home recipient reason. Do not infer a desktop-test pass from that permitted remote-test skip or a group count.

## Verification continuation

The original class will run three times with temporary, uncommitted consumer-phase instrumentation; the repair will then run ten whole-class repetitions, the nine-class CARD-1894 CP-2 selection, and the requested six-prefix guard union after the derived-index driver. Final counts, timing and source provenance will be appended below.
