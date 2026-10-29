# RC Full qualification speed-ups

Task `e197fb96`; planning base `e4e65f69078f6340ba5fb8e0cee27f6778d58802`.
CARD-1514 is the existing timing/fixture workstream, not a newly assigned card identity.
Status: skeleton published first; code grounding and checkpoint design follow in separate commits.

Evidence: [run 28bbb58e breakdown](../../investigations/2026-10-29-rc-run-28bbb58e-time-breakdown.md), read in full. Its historical Windows run lasted 1378.1 minutes: setup 11.0, chunks 1365.7, inter-chunk gaps 1.4. No desktop files were read by this task.

## Decisions

- D-1: Measure first. The process responsible for 114.2 minutes of post-session delay is unknown; telemetry is only a hypothesis.
- D-2: Preserve Full coverage, exclusion rules, process custody and frozen candidate identity. Speed is not gate credit.
- D-3: Keep native and in-process concurrency at 1 until an independent Debug comparison supports one change at a time.
- D-4: Implement reviewed changes on both master and the active rc line before a new Full run; do not mutate an existing sealed attempt.
- D-5: Route portable work by live runner defaults, native Job Object evidence by Windows platform. Read runner-defaults and session-runners on 2026-10-29; both portable and Windows lanes are available. Do not pin a fleet location.

## Ground truth

| Brief assumption | Evidence / code fact to preserve or verify |
|---|---|
| Qualification could be slowed by retries | All attempts were 1; duplicate chunks absent; retry overhead 0. |
| Process exit delay has an obvious cause | 384 chunks lingered after MTP session end; root versus descendant was not captured. |
| Setup dominates every small chunk | 190 Antiphon chunks show about 14 s pre-first-test; fixture attribution is missing. |
| Parallelism is safe because CPU was low | No host-wide CPU/memory samples exist; the live-tree CPU counter loses exited processes. |
| Planner evidence supports regrouping | Per-suite plans were overwritten; only the final suite survived. |

## A. Instrumentation before optimization

Persist owner exit observations (root and descendants), per-suite plans, fixture stamps, host CPU/memory/disk samples and actual per-chunk discovery. Retain setup/planning phases, authoritative result counts and missing/torn timing evidence. Each addition needs a failing witness before its implementation. Direct saving: 0 minutes; enables attribution. Files, risks and exact witnesses will be grounded next.

## B. Cheap safe wins after attribution

- Exit delay: 114.2 min measured; **estimated 60-110 min** recoverable after identifying the owner. Never release custody early.
- Group 105: 7.8 min active then 52.4 min idle; prove the hang repair and derive a watchdog from real healthy chunk distributions. The reported 35-40 min suggestion is not twice the largest 32.3 min healthy chunk.
- Packing: 138 profile chunks paid repeated fixed cost; **estimated 35-45 min** before exit-delay improvements, subject to isolation and exact coverage census.
- Setup sharing: about 47.1 min observed; **estimated 20-40 min** only if fixture stamps identify reusable setup. Fresh startup tests retain fresh instances.

Savings overlap; no additive overall promise. Slow individual tests (854.5 s maximum), 45 tests near 30 s and E2E's 220.9 min are follow-up profiling candidates, not proven sleep removals.

## C. Risky parallelism trial

122 serial Antiphon chunks consumed 657.5 min. Historical **estimated 150-330 min** saving is an upper opportunity, not acceptance evidence. Trial nativeActiveMax 2 OR ProcessSpawnLimit 2 in measured Debug with the same SHA, exact test roster and comparable host load. Specify repeat count, flake acceptance, resource isolation and rollback before Code. Do not parallelize E2E or co-schedule Antiphon.Tests with Antiphon.Agents.Pty.Tests as part of this trial.

## D. Contracts that must not change

Full discovered-test census, exact selected identities, exclusions and executed coverage ledger remain mandatory. Existing failed receipts stay failed. Frozen rc source/binaries and attempt identity remain sealed; reviewed fixes require a fresh candidate/attempt. No tags, publish, rc rebase or product code edits in this Plan task.

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards) and [R1-R9](../../test-performance-standards.md). This documentation task runs no builds/tests. Future checkpoints need per-row phase costs, measured p95 and approximately 2x timeouts; missing samples are provisional, never fabricated. Every expected/observed selection over 30 minutes requires a Debug task. Shared expensive setup belongs once per run/assembly, with test isolation preserved. Measurement locations and bounded sampling will be completed with the checkpoints.

## Verification handoff

Plan review by a different company's model is requested by the brief. Verification remains a separate TestDesign stage after review; this skeleton is not executable Code authority.
