# CARD-1514: rc g105 CheckpointReceiptStallBudget and RunScheduler investigation

Confirmed two different mechanisms: a receipt test's five-second startup scheduling false failure, and a production scheduler race that admits a row before its build-source receipt is published. Both mechanisms are present in the pinned rc and master sources. This investigation changes documentation only.

## Identity and evidence

- Task `ff3ef62e-ca6c-4150-82ce-12966e3b0339`, assigned branch `feat/card-task-ff3ef62e`. Card title: "Investigate and speed up the slowest test classes (AgentTaskLandPersistenceFailureTests 24-32 min, CheckpointMatrix 26 min, Publication)". Task API binding: board Antiphon `8988ca03-7414-47ad-b0b6-51556c701703`, CARD-1514 `c92555be-65d0-45b6-a759-3d1ba5938bd2`; retained in `runs/task-binding.json` (card title in `runs/card-binding.json`).
- Tested rc source: `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a`, the assigned starting commit and caller's rc/2026-11-06 tip.
- Tested master source: fetched `origin/master` `28cdefab326782f2c91ca361768a7252dc875ec7`, exported without switching the assigned branch. It is a pinned snapshot, not a claim about later master commits.
- The entire `tests/Antiphon.Tests/Checkpoints`, `tools/Antiphon.Checkpoints`, and `src/Antiphon.HostCleanup` trees compare equal between those SHAs (`runs/subject-source-diff.txt` is empty). Other project/test infrastructure differs and was built separately.
- Retained external evidence: `/work/evidence/task-ff3ef62e/`. Raw logs, TRX, repeat ordinals, driver receipts, source archive, harnesses, assembly hashes and measurements are under that root; generated payloads are not committed.
- Host: server2 Linux x86_64, Debian 12, 24 CPUs, SDK 10.0.403, runtime 9.0.22, TUnit 1.44.0, MTP 2.2.2. No desktop execution, restart, deployment or modification of the live Windows qualification occurred.

The original Windows observations are caller-provided evidence from run `b19a9880`, group `antiphon-all-group-105`, reported at 2026-11-05 05:25Z; see retained `brief.md:5`. Its full original TRX was not reachable here. Linux TRX below supplies independent reproductions, not a claim that the original Windows CP-2 details were retrieved.

## Executions and measurements

Each full class ran in its own test host with native TUnit repeat 20, four maximum parallel tests, a five-minute test-host safety limit, and one build-slot lease per batch. The generated attributes are `[Repeat(19)]` and `RepeatEvidenceAttribute(20)`. Every selected case has exactly the ordinals 0–19; none emitted `C885_REPEAT_INVALID`. These are one-host repetitions, not twenty fresh-process launches.

The first four rows below are full classes. Subsequent load rows select only the originally failing method, twenty additional executions per method and revision. This keeps the additional load witness scoped after the full-class samples established the defects. Every row retains its own `run.trx`, `console.log` and `driver.json` under `runs/<label>/`.

| Evidence label | Executed | Passed | Failed | Admission wait (s) | First-to-last TRX case (s) |
|---|---:|---:|---:|---:|---:|
| rc-normal-receipt | 120 | 120 | 0 | 256.1 | 112.427 |
| rc-normal-scheduler | 360 | 358 | 2 | 226.0 | 6.750 |
| master-normal-receipt | 120 | 120 | 0 | 286.7 | 96.218 |
| master-normal-scheduler | 360 | 359 | 1 | 271.8 | 7.975 |
| rc-loaded-receipt | 20 | 20 | 0 | 315.9 | 2.407 |
| rc-loaded-scheduler | 20 | 20 | 0 | 316.0 | 0.976 |
| master-loaded-receipt | 20 | 20 | 0 | 331.0 | 2.946 |
| master-loaded-scheduler | 20 | 20 | 0 | 331.6 | 1.708 |

Total: **1040 executed, 1037 passed, 3 failed, zero skipped**. The three failures are the scheduler method in the normal full-class samples. All eighty loaded target-method executions passed.

Target-case TRX duration in milliseconds, twenty samples per cell (nearest-rank p95 / maximum):

| Source / mode | Receipt p95 / max (ms) | Scheduler p95 / max (ms) |
|---|---:|---:|
| rc-normal | 157.728 / 415.579 | 196.234 / 255.582 |
| master-normal | 117.808 / 176.138 | 180.959 / 441.289 |
| rc-loaded | 114.476 / 204.364 | 368.975 / 368.981 |
| master-loaded | 158.478 / 349.734 | 629.538 / 637.160 |

These are reported test-case durations including fixture hooks, not separately measured worker-start or syscall spans. Engine/discovery and internal I/O phase attribution remains unknown. Full receipt-class reported durations were 116.634 s (rc) and 98.631 s (master); scheduler-class durations were 9.909 s and 11.430 s.

Build and run admission are separate from test duration. Native default output was used throughout, with `UseAppHost=false` on builds and matching no-build runs. Repeat builds also passed `AntiphonCheckpointRepeat=20` and `AntiphonCheckpointRepeatProject=<that checkout's absolute test csproj>`. The master source exports lack Git metadata, so Git informational diagnostics appear in their successful build logs; these runs are not source-clean checkpoint qualification receipts.

The load experiment used a separate cold native build from an independent master export, admitted **2026-11-05T06:01:43.540Z**, completed **2026-11-05T06:11:47.585Z**, exit zero (602.88 s build time, 604 s lease held). Four unpaced SHA-256 worker threads and a 32 KiB overwrite/fsync loop with a 10 ms delay between iterations ran from **2026-11-05T06:04:39.133Z** to **2026-11-05T06:09:55.119Z**, recording **27013 writes / 885161984 bytes written**. All four loaded test execution windows are fully inside both intervals; see `runs/load-overlap.json`. Stress workers were stopped and joined. This records applied CPU/I/O work, not a measured claim of whole-host CPU saturation. Ambient shared-host load varied substantially (recorded load averages included 13–61 on 24 CPUs), so the normal rows are class-isolated shared-host observations rather than quiet-host controls.

Six gated builds completed with zero errors: two ordinary source builds, two repeat-enabled builds, the evidence invoker build, and the separate load build. Their logs retain warnings and admission waits; no passing test claim is inferred from a build exit.

## Receipt test: confirmed startup scheduling false failure

The failed assertion is a real-clock startup guard, before the behavior the test intends to assess:

1. `tests/Antiphon.Tests/Checkpoints/CheckpointReceiptStallBudgetTests.cs:100` queues the caller with `Task.Run`.
2. Line 103 blocks for at most five real seconds awaiting `stream.Entered`.
3. Only line 104 advances the injected clock to the three-second stall deadline. The clock is manual (`tests/Antiphon.Tests/TestHelpers/ControlledTimeProvider.cs:15`, `:41`).
4. The stream sets `Entered` at the first instruction of its fake read (`CheckpointReceiptStallBudgetTests.cs:127`). The production I/O thread is created only after the queued caller runs (`src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs:201`, `:215`).

An evidence-only console driver invokes this unchanged existing test method. First it runs normally. Then it restricts only its own process's pool to one worker, occupies that worker, and releases it from an owned dedicated thread after six seconds. Both revisions produce the exact original `stream.Entered.Wait(TimeSpan.FromSeconds(5)) ... True ... False` assertion at line 103. Pool limits are restored and all owned work is joined. No production deadline is advanced before this startup failure.

| Source | Ordinary invocation | Occupied-pool invocation |
|---|---:|---:|
| rc | pass, 523.9 ms | expected assertion, 6002.1 ms including cleanup |
| master | pass, 791.1 ms | expected assertion, 6002.2 ms including cleanup |

Evidence: `runs/rc-starvation-proof-corrected.log` and `runs/master-starvation-proof.log`; invoker `probe/Program.cs:14` and `:30`. The assertion occurs at the five-second guard; the method returns after the blocker is released at six seconds because its existing `finally` awaits the queued read. These two controlled assertions are not included in TUnit failure counts. Ordinary class runs did not reproduce this receipt assertion naturally.

One earlier probe launch exited 134 before invoking the method because the standalone host's dependency graph selected the wrong DiagnosticSource version. The successful invocations use the test assembly's runtimeconfig and depsfile. That launch error has zero test credit and is retained in `runs/rc-starvation-proof.log`. Two pending fresh-process loops were also stopped before any test child started and replaced by native repeat batches; `runs/pending-batch-cancellation.json` records ownership and zero-child verification.

## Scheduler: confirmed production readiness/publication race

The reported method fails naturally on both revisions. The full-class samples reproduce its final green-count assertion: rc ordinals 16 and 18, master ordinal 11. All other methods passed. The failing durations are 186.633 and 196.235 ms on rc and 180.959 ms on master, far below the five-second polling bound.

The missing second row is specifically `CP-2: build_source_mismatch`, with `executed=0`, `trx=n/a`, and `buildSource=unknown`. Independent stored rows:

- rc `runs/rc-normal-scheduler/run.trx:1607`, execution `8f5e7020-b155-432f-ac06-94f0762593e0`; and line 1644, execution `d873fdf0-1715-4ed1-9132-e90757424dfc`.
- master `runs/master-normal-scheduler/run.trx:1642`, execution `751b1385-f1c3-46cc-9629-9ce730a8f43f`.

Essential unedited CP-2 lines, present in all three failure messages:

```text
CHECKPOINT CP-2 commit=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa build=reused filter=/*/*/CP-2Tests/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=unavailable waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-2 EXIT CODE: 2 reason=build_source_mismatch
```

The `aaaa...` commit is fixture input; actual tested-source provenance is the two SHAs above. The fake slot's `unavailable` label is expected: `RunSchedulerTests.cs:592` chooses `FixedSlotClient("unavailable")`; `tools/Antiphon.Checkpoints/Slots/FixedSlotClient.cs:11` returns that label with default exit code zero (`Slots/BuildSlotClient.cs:21`). It does not demonstrate broker unavailability. Both build-concurrency assertions precede the final green count and passed (`RunSchedulerTests.cs:35`).

Reconstructed interleaving, from the stored `unknown` receipt state and the unchanged source. Paths in this table are relative to `tools/Antiphon.Checkpoints/`:

| Step | Build task | Scheduler / row task |
|---|---|---|
| 1 | Assigns shared `progress.State = "ok"` at `Execution/RunScheduler.cs:541`. | CP-1 can still be running. |
| 2 | Has not yet created/published the receipt; `built.Write(...)` follows at line 545. | CP-1 completion wakes the scheduler's `Task.WhenAny` loop at line 240. |
| 3 | Receipt publication remains in progress. | `Pick` accepts the nonexclusive CP-2 because the build is no longer pending/building/failed (`RunScheduler.cs:747`, `:753`); a build still in flight blocks only exclusive rows. |
| 4 | Receipt is still absent at the row's check. | `CheckpointBuildBinding.Check()` returns `unknown` for `!File.Exists(PathName)` (`Evidence/SourceRunGuard.cs:97`). `RowRunner.cs:139`–141 refuses before launching the fake test process. |
| 5 | Publication and build-task completion can follow. | CP-2 already has its terminal source-mismatch result. |

The binding write performs filesystem preflight, serialization and output sealing (`Evidence/SourceRunGuard.cs:121`–127). Thus shared readiness is observable before that publication has completed. This is a real production ordering defect; the test's final success assertion exposes it. The observed `unknown` is the missing-file return, whereas an exception reading/parsing an existing receipt would return `mismatch` at `SourceRunGuard.cs:110`. The supplied revisions have the same mechanism, and Linux reproductions establish that Windows path handling is not required.

## Remaining uncertainties

- Original Windows CP-2's full message and native timing/transcript were not supplied. Matching the original Windows case to the reproduced missing-receipt branch remains an inference until that TRX row is obtained.
- The receipt-test mechanism is reconstructed exactly, but original Windows CPU pressure versus pool scheduling delay was not measured here. The caller's slow group 104 and simultaneous cleanup are correlation, not measured causation.
- Class-isolated Linux samples and controlled load do not reproduce the whole Windows qualification environment. Native Windows validation is still owed after the live owner's lock is released.
- No product repair, test repair, mutation or post-fix verification was performed. The three natural full-class scheduler failures remain part of the result even if later loaded samples pass.

## Reproduction

The retained invokers contain exact argv, labels and source paths. Rebuild the selected checkout through `scripts/build-slot.ps1` in its native output with the three properties stated above, then run:

```sh
node /work/evidence/task-ff3ef62e/run-native.mjs <checkout> <fresh-label> CheckpointReceiptStallBudgetTests
node /work/evidence/task-ff3ef62e/run-native.mjs <checkout> <fresh-label> RunSchedulerTests
```

The scripts take a new lease for each test host. Passing a fourth argument selects an exact method for the loaded rows. The probe's successful gate command uses `dotnet exec --runtimeconfig <test.runtimeconfig.json> --depsfile <test.deps.json> <probe.dll> <test.dll>`; its build is separately gated and its expected-assertion exit zero means the control behaved as requested, not that the invoked test passed.

## Cleanup and publication scope

All foreground build/test drivers completed and every owned stress worker was joined before cleanup. The initially empty native-output inventory was checked, then **72 task-created bin/obj roots**, both task-created source-export trees, probe bin/obj and the stress payload were removed using individual literal absolute paths. No tracked production/test file was mutated. `runs/cleanup.json` and `cleanup.mjs` retain the cleanup record; raw TRX/log evidence and the pinned source archive remain external. Only this report and the requested-path pointer are committed; no generated payload is staged.

## Not done, noted

Smallest directions to assess in Plan: test-owned receipt startup/completion gates with a measured generous safety bound; scheduler readiness published after the build receipt is ready. A test-only wait increase does not remove the reproduced production race.

--- next stage ---
next: plan
handoff: Plan from the confirmed receipt-test startup scheduling failure and cross-platform scheduler readiness/publication race; preserve rc/master failures and keep native Windows attribution and verification explicitly owed.
artifact: docs/investigations/2026-11-05-card-1514-investigate-and-speed-up-the-slowest-test-classes-agenttasklandpersistencefailuretests-24-32-min-checkpointmatrix-26-min-publication.md
