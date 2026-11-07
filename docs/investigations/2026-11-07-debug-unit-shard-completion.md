# Debug: why a Unit shard produces no TRX within its cap (CARD-1728)

Debug task b1e4f841, 2026-11-07, server2 (24 cores, Linux), source
`5d5973be2cd867bf592cd0363240e728f64d3b28` (clean, `buildSource=verified`).
Related: CARD-1720 (whole-Unit row budget, landed), CARD-1725 (A-D shard stopped),
CARD-1734 (stop-path terminal report loss, separate fix).

## Verdict

A Unit shard is neither hung nor slowed down by the coverage index. Its tests run inside one
test host, and in that host every test under `[ParallelLimiter<ProcessSpawnLimit>]` runs one
at a time (`ProcessSpawnLimit.Limit == 1`, CARD-0050 S5). After a short parallel burst, the
host works through that serial chain. The chain alone runs past the 45-minute row cap. When the
row deadline kills the host, Microsoft.Testing.Platform has written no TRX yet, and SIGTERM does
not make it write one either. On top of that, a class-letter OR filter adds 4 to 7 minutes of
discovery before the first test runs.

The measured A-L shard ran 3,268 of its tests green (0 failed) in 45 minutes and was still in
the serial chain when the cap hit. It produced no TRX.

## Measurement

Run `20261107-090804-8143` used the checkpoint tool with one build and two overlapping rows. The
ignored plan was `.antiphon/c1728/plan.md`. Each row ran with `ANTIPHON_TEST_PHASE_ROOT`, so the
CARD-1514 per-test journal recorded every case start and end as it happened. That journal is the
only record that survives the kill. Host load (`/proc/loadavg`) was 17.48 / 23.18 / 19.21 at
start and 31.51 / 25.87 / 26.72 at end. During the run, another task's whole-Unit host
(task-8da334c5) was running at about 90% CPU.

Run report (unedited):

```
CHECKPOINT CP-1 commit=5d5973be2cd867bf592cd0363240e728f64d3b28 build=ok filter=/*/*/PlanCoverageBindingTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-b1e4f841/.antiphon/checkpoints/20261107-090804-8143/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=5d5973be2cd867bf592cd0363240e728f64d3b28 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=251.7830372s startup=6.3427285s testsWall=148.9430512s teardown=0.6528881s hostWall=155.9385888s
CHECKPOINT CP-2 commit=5d5973be2cd867bf592cd0363240e728f64d3b28 build=reused filter=/*/*/(A*)|(B*)|(C*)|(D*)|(E*)|(F*)|(G*)|(H*)|(I*)|(J*)|(K*)|(L*)/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=45m slot=granted waited=0s dirty=0 source=5d5973be2cd867bf592cd0363240e728f64d3b28 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=2701.962s wall=2702.031s reason=row-deadline-exceeded
BUDGET wall=2971.046s execution=2971.046s admissionOnly=0.000s
verdict: RED exit=5 reason=row-deadline-exceeded
```

### Phase table, A-L Unit shard (CP-2)

| Phase | Seconds | Source |
|---|---:|---|
| Admission (slot wait) | 0 | `waited=0s` |
| Build | 252 (shared with CP-1) | `PHASES CP-1 build=` |
| Discovery: host start to first test start | 261 | host started 09:12:38 (`ps`); first journal start 09:16:59 |
| Parallel burst: 2,177 cases | about 45 | journal, all done by +52 s |
| Serial tail: 1,090 cases until the kill | 2,250 wall, 2,272 summed | journal; sum is about equal to wall, so cases ran one at a time |
| of which `ProcessSpawnLimit` classes | 1,988 summed (260 cases) | journal plus class attributes |
| of which namespace `Antiphon.Tests.Checkpoints` | 1,904 summed | journal |
| Coverage index | 0 | the tool runs no coverage step during `run` |
| Report write | report.md and report.json written | the run report survives a row deadline; there is no row TRX |
| Row deadline | 2,702 | `reason=row-deadline-exceeded`, exit 5 |

At the kill the journal held 3,268 complete cases (3,242 passed, 26 skipped, 0 failed) and one
case in flight, `C1364BoundLandWaitProofTests.C1364_Host_recycle_post_removal_reports_waited`.
Five process-spawning classes had not started: `CheckpointTempUsageTests`,
`HostJqPrerequisiteScriptTests`, `LiveLaneEvidenceTests`, `LiveLaneExecutableTests` and
`LiveLaneInventoryTests`. The A-L shard therefore needs well over 45 minutes on this host. Most
of that time is the serial chain.

Largest serial-tail classes, summed case seconds: `CheckpointTrackedBaselineTests` 471 (13
cases), `CheckpointShapeConflictTests` 394 (17), `CheckpointBoundedReaderTests` 304 (11),
`C1364BoundLandWaitProofTests` 281 (4), `CheckpointReceiptSafetyTests` 224 (8),
`CheckpointOutputConfinementTests` 216 (6), `CheckpointConfinementLedgerTests` 187 (21). All of
them carry `ParallelLimiter<ProcessSpawnLimit>` and start the checkpoint tool, pwsh, git or
dotnet.

### Discovery cost of the filter shape

Each probe ran the same build, copied to `.antiphon/c1728/probe-bin` so the sealed output stayed
untouched, under its own build-slot lease. The time measured is host start to the first
journal `start` record.

| Filter | First test after | Load at start |
|---|---:|---:|
| `/*/*/PlanCoverageBindingTests/*` (CP-1) | 6.3 s | 17.5 |
| `/*/Antiphon.Tests.Checkpoints/*/*[Category=Unit]` | 46.9 s | about 27 |
| `/*/*/*/*[Category=Unit]` | 116.3 s | 28.7 |
| `/*/*/(A*)\|...\|(L*)/*[Category=Unit]` (12 OR operands), probe | 419.4 s | 15.7, rising to 35.9 |
| the same filter, CP-2 | about 261 s | 17.5 |

The whole-Unit host then completed 3,834 cases within 20.6 s of its first start. Parallel Unit
work is cheap. The OR filter shape costs more than the lane's entire parallel work. Earlier
memory notes record a 100-operand filter that ran no test in 15 minutes.

### Coverage index (PlanCoverageBindingTests)

`historical_plans_bind_without_suppression` took 149.0 s. It calls `CoverageCommand.Run` four
times (two plans, each twice for determinism), and each call builds a `TestAssertionIndex` over
the test tree, so about 37 s per index. `historical_row_timeout_checklist_binds_the_advisory_method`
took 70.2 s for two index builds. Both tests run in the parallel phase. They cost about 2.5
minutes, are not on the serial chain, and do not cause a missing TRX. Caching one index per test
would cut that cost, but it is secondary.

### Stop and kill paths keep no TRX

- On a row deadline, `ProcessDriver` kills the host tree at once (`Kill(entireProcessTree: true)`).
  MTP writes the TRX only at session end, so a killed host never writes one.
- A probe sent SIGTERM to `/*/*/CheckpointLargeStampTests/*` 12 s after its first test started.
  MTP cancelled the running tests (`OperationCanceledException`), then the host aborted with
  exit 134 (core dumped) and wrote no TRX. A graceful signal does not rescue the receipt.
- A SIGINT probe from a non-interactive bash job had no effect, because bash sets SIGINT to
  ignored for background jobs. That probe is inconclusive for SIGINT.
- The CARD-1514 phase journal (`cases-<pid>-<nonce>.jsonl`) flushes one line per start and
  completion. It held the full completed and in-flight attribution above after the kill.
- Supported `stop` losing the run report (exit 6) is CARD-1734's fix and was not re-measured here.

## Why nearly every Code task ends "Final verification incomplete"

The Default Code/Review recipe (`docs/testing-and-build.md`) and Final profile v1 ask for the
Unit lane. The A-L half alone runs past 45 minutes because of the `ProcessSpawnLimit` chain.
Earlier whole-lane measurements took 70 minutes of wall time across three concurrent hosts, and
3h05m across two lanes of 20 shards. No single row with a cap of 45 minutes or less can finish
the lane. Standing operating rule 8 (`docs/orchestration-loop.md`) already says "no whole-Unit
or whole-lane runs from Code or Review". The Final default and the recipe contradict it. Each
task therefore pays 36 to 52 minutes for a row that cannot produce a receipt.

## Recommendation (smallest first)

1. **Decision for the operator (policy, no code):** keep the whole Unit lane on the nightly and
   rc backstop, and take it out of per-task Code/Review Final ordinary scope on server2, as
   standing rule 8 already says. Per task, run the affected classes plus the named guard classes.
   Only this change stops the "incomplete" outcome at once. It needs the Final profile and the
   recipe text aligned through the ordinary pipeline.
2. **If a per-task Unit receipt is still required, shard by measured serial seconds, not by
   letter.** Use namespace or single-wildcard class-prefix filters with no OR operands (47 s
   discovery against 4 to 7 minutes). Size each shard by the summed `ProcessSpawnLimit` case
   seconds from the phase journal. The serial work of `Antiphon.Tests.Checkpoints` alone was at
   least 32 minutes for A-L on this host, so it needs several rows of its own. Run the rows
   concurrently in separate hosts: the limiter is per process, and the tool overlaps two rows on
   Linux. The whole lane still needs about an hour, so this is a backstop shape, not a per-task
   gate.
3. **Code, needs Review: shorten the chain.** Give the checkpoint-tool process tests, which start
   dotnet, pwsh or git but no ConPTY, their own limiter with Limit greater than 1. Keep
   `ProcessSpawnLimit = 1` for ConPTY, fakeclaude and session-runtime spawns. That chain is 84% of
   the A-L tail. A limit of N would divide it by close to N, but each class must first be checked
   for shared mutable state such as environment variables, cwd, temp-root sweeps or process
   censuses. Do not do this without a flake run.
4. **Tool, small: report partial attribution on a kill.** Set the CARD-1514 phase variables on
   every `tests/Antiphon.Tests` row. On a row deadline, print the journal's
   complete/failed/skipped counts and in-flight case names as a non-receipt diagnostic. A partial
   result never becomes a receipt.
5. Secondary: build one `TestAssertionIndex` per test in `PlanCoverageBindingTests` (about 150 s
   down to about 40 s).

No timeout was widened and no assertion changed. The run's explicit `totalTimeoutMinutes: 60`
is above the derived 50 minutes. It sums the 45-minute row cap with the measured build and
leaves both row caps unchanged.

## Evidence (ignored, runner mirror)

- Run folder: `/work/worktrees/task-b1e4f841/.antiphon/checkpoints/20261107-090804-8143/`
  (`report.md`, `report.json`, `rows/CP-1/run.trx`, `rows/CP-2/console.log`).
- Phase journals: `/work/worktrees/task-b1e4f841/.antiphon/c1728/phase/chunk-*/cases-*.jsonl`.
  CP-2 is `chunk-d5b0330b…/cases-31212-…jsonl` (6,538 lines). The probes are the groups
  `dunit`, `dal`, `dns`, `sigint` and `sigterm`.
- Plan: `.antiphon/c1728/plan.md`. Load samples: `.antiphon/c1728/loadavg-{start,end}.txt`.
