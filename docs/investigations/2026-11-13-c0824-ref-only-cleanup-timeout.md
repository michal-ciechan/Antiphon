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

## Original-class phase qualification

The three diagnostic class runs use report-only commit 262e0f04029feb1b58b1180cb650e256f4b0990d with uncommitted TestTrace phase markers over otherwise unchanged test code. This is diagnostic baseline evidence, not clean-source acceptance. The exact temporary source digest is retained in .antiphon/debug-6130909c/instrumented-source.sha256; no marker is retained in the repair. The gated diagnostic build passed, 0 errors, 532.84 seconds excluding queue wait.

| Run | Desktop aggregate / remote / legacy-dirty seconds | Results | Load 1/5/15 before queue | Outer seconds |
|---|---|---|---|---:|
| 1 | 14.026 / 11.687 / 1.912 | 3 passed, 0 failed/skipped | 22.54/27.00/29.05 | 153.105 |
| 2 | 29.260 / 20.818 / 4.152 | 3 passed, 0 failed/skipped | 11.86/21.87/26.89 | 90.399 |
| 3 | 34.710 / 27.175 / 11.751 | 3 passed, 0 failed/skipped | 11.65/19.10/25.44 | 101.130 |

All 9 class results passed. These shared-process samples are warmer and at lower sampled load than the failed isolated run. The aggregate method durations were 14.026, 29.260, and 34.710 seconds.

Per-phase timings (three samples, provisional linear p95) and maximum descendant call counts below come from the temporary spans. A Git call can be answered in process; native counts are the fixture runner subset, not every product subprocess. Parent and child rows overlap and must not be summed. Fixture creation uses cached Git templates, not a full checkout of Antiphon.

| Phase | p50 / p95 / max seconds | Fixture Git calls / native starts / SQL calls (max) |
|---|---|---|
| report False | 4.666 / 9.635 / 10.187 | 46 / 12 / 50 |
| report True | 0.832 / 4.885 / 5.335 | 46 / 12 / 50 |
| persistent reports | 5.241 / 14.497 / 15.526 | 92 / 24 / 100 |
| world setup | 0.468 / 1.261 / 1.349 | 28 / 4 / 8 |
| consumer eligibility | 0.685 / 3.243 / 3.527 | 39 / 13 / 18 |
| consumer operator-release | 0.235 / 1.656 / 1.814 | 39 / 13 / 19 |
| consumer system-release | 0.220 / 0.260 / 0.264 | 39 / 13 / 19 |
| consumer retire-action | 0.270 / 0.329 / 0.335 | 39 / 13 / 22 |
| consumer legacy-candidate | 0.733 / 0.802 / 0.810 | 12 / 9 / 22 |
| consumer legacy-precheck | 0.369 / 0.386 / 0.388 | 10 / 7 / 27 |
| consumer legacy-action | 0.224 / 0.276 / 0.281 | 17 / 8 / 28 |
| residue and health | 1.452 / 2.340 / 2.439 | 14 / 12 / 37 |
| landing and guarded cleanup | 4.478 / 14.685 / 15.819 | 137 / 21 / 230 |
| consumer card-discovery | 0.243 / 2.527 / 2.781 | 4 / 3 / 21 |
| consumer card-admission | 0.244 / 2.102 / 2.309 | 8 / 4 / 22 |
| consumer card-replay | 0.194 / 0.351 / 0.368 | 8 / 4 / 23 |
| consumer card-intent | 0.299 / 0.578 / 0.609 | 45 / 11 / 40 |
| completed card | 1.541 / 8.185 / 8.923 | 70 / 24 / 117 |

The desktop world explicitly sets PauseBetweenSeconds=0. FixtureGit disables hooks and ignores system/global Git configuration; product Git process exit/drain already has a bounded cancellation budget. The class runs reached all instrumented phases; no input-specific hang or single long subprocess was observed. This is cumulative work plus cold/warm and host-contention variability. fsync-specific attribution was not measured.

## Implemented repair

The aggregate desktop test becomes five independently timed consumer-family tests. The two report layouts become two cases of their own test. Together with the unchanged remote and legacy-dirty methods, the class expands from 3 to 9 results. All 121 Should assertion calls, every assertion line, and all consumer loop cases are preserved. The policy and residue families get fresh worlds, preventing earlier consumer tasks from expanding their scans. Shared Postgres/model/template setup remains assembly/process scoped. The class remains Integration + Slow with the assembly-local ProcessSpawnLimit at width 1.

Each extracted family has a 30-second deadline, narrowed from the prior 60-second aggregate wall. This is a provisional cold/load-aware budget; ten fresh-process whole-class samples below are the bounded calibration collection. The inherited remote and legacy-dirty deadlines stay 60 seconds (remote observed p95 about 26.5 seconds in the three original class runs). A single aggregate deadline of roughly 116 seconds would merely hide cumulative cost and was not adopted. No production behavior changes.

Final builds use native output and UseAppHost=false. Since production source and project references are unchanged, BuildProjectReferences=false reuses the product assemblies from the diagnostic build and rebuilds the changed test project; matching properties are passed on no-build runs. This avoids source-revision metadata alone recompiling identical product code. Staged helper targets retain their normal outputs. No custom OutputPath is used.

## Loaded CP-2 and guard evidence; budget calibration

At clean committed source 9b4071ce0fa99f10f56cedcd998df7f26f113e31:

- Native test-project build passed (0 errors), 244.89 seconds excluding slot wait, matching UseAppHost=false and BuildProjectReferences=false on runs. Unchanged product assemblies came from the preceding full diagnostic build.
- The gated derived-index driver passed: derived-impact-index unchanged; windowsOnly=31, linuxOnly=1. No index commit was needed.
- Exact CARD-1894 CP-2 nine-class filter: 373 passed, 0 failed/skipped; outer wall 317.892 seconds, TUnit run 308.968 seconds. The increase from 367 to 373 is exactly the six additional results created by splitting the aggregate.
- Exact six-prefix guard union after the driver: 80 passed, 0 failed/skipped; outer wall 460.497 seconds, TUnit run 335.333 seconds.

CP-2's loaded landing-family sample was 26.378 seconds and legacy-policy family was 17.169 seconds, both passing their initial 30-second walls. Those samples show the initial cold/warm three-sample budget was too tight for the loaded tail. The final family deadlines are 55 seconds for landing (2x26.378 rounded up to five seconds), 35 seconds for legacy policy (2x17.169 rounded up), and 30 seconds for the other extracted families. All remain below the original 60-second wall; the fix still partitions independent work instead of raising the monolithic timeout. The calibration is provisional pending the final fresh-process class samples, and no failed sample is hidden or retried.

These 373/80 results bind the earlier, stricter-budget SHA above. The final calibration changes exactly two Timeout integer literals plus this report; final class repetitions verify those changed tests. Do not relabel the earlier CP-2/guard receipts as execution at the later SHA.

The changed paths do not intersect the nightly policy or live-lane ProtectedInputs census. policyHash remains 7763a891c3bdde500976e5391b028f3667590ba1186e036abfbf7ce826857fcb and PinnedInputDigest remains 2b9353e8b21fd177508ada0e40db00cdb4d1833933f70af9adc160a8ac709324. No re-pin was necessary.

At the pre-calibration repair, evidence-diff passed (2 commits, 0 violations) and merge-tree against fetched origin/master 6f5ce63cb209b856d757691c08426f6fad3dfab5 was clean (tree 4bf4c24bfb3575ffa0357b9eab530702cc689847). Final-tip checks are recorded below.
