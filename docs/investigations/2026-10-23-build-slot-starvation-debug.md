# Build-slot starvation Debug, 2026-10-23

**Queue contention consumes the checkpoint's total wall budget, but not its per-build or per-row execution allowance.** Server2 admits four drivers under FIFO and a 16 GiB memory floor. Ready test hosts can exhaust the total budget behind earlier builds. Two other incident builds exhausted their execution allowances with zero slot wait. Keep these causes separate; increasing slots or excluding queue time cannot by itself repair an underpriced execution deadline.

Debug `e8b38df0`, inspected source `2a22d9b9855424f5d2758e329f3fced39f096df1`. Documentation only: no build, test, deployment, process stop, setting change or desktop/Windows qualification access. Filed exactly one Backlog fix, **CARD-1531**, after duplicate searches. Existing owners below retain throughput, cache and test-speed work.

Evidence is a bounded census of surviving server2 task worktrees, not all host activity. At **04:22:55Z**, 85 same-day scheduler directories contained 82 states: 78 done, three running and one stopped; three directories lacked state. Their executor logs contain 259 distinct granted lease IDs. Previous-day directories contributed no grants dated today. Standalone `row`/PowerShell recovery runs and bootstrap wrappers are outside this scheduler census. Subsequent recovery is identified separately. Grants select the measurement window by grant timestamp; run outcomes select by run start and use the captured state. Percentiles use nearest rank; median averages the middle pair. Times below are seconds.

Generated snapshots, analysis scripts, input digests and API extracts are ignored under `/work/worktrees/task-e8b38df0/.antiphon/debug-starvation/`. They may disappear with worktree retirement. The tables and exact source coordinates in this note are the durable record. Other tasks' source and evidence were only read.

The capacity is configured, not calculated from free RAM or core count:

| Item | Observed or implemented value | Evidence/meaning |
|---|---|---|
| Governing endpoint | `http://build-slots:8080/build-slots` | Inherited `ANTIPHON_BUILD_SLOTS_URL`; direct GET 200 at 04:22:55Z. The runner's own loopback broker is a different instance. |
| Admission | enabled; budget **4**, occupied **4** | Three build labels and one test-row label at that instant; all holders alive, no waiters. Occupancy is mixed drivers, not four compilers. |
| Build node hint | **6** | Applied to direct builds; 24 logical CPUs observed. `4 × 6` is not a CPU reservation or a limit on all compiler/test threads. |
| Memory | available **95,762 MiB**, floor **16,384 MiB** | Admission checks host `MemAvailable`; it does not reserve a per-job amount or revoke held leases. |
| Admission algorithm | FIFO position must fit `MaxConcurrent - leases.Count`, then memory must meet floor | [BuildSlotBroker](../../src/Antiphon.SessionRunner/BuildSlotBroker.cs), `TryAcquire`. New arrivals cannot continually overtake a polling head waiter. |
| Lifetime/polling | TTL 90 min; poll 15 s; silent waiter dropped after 60 s | [Settings](../../src/Antiphon.SessionRunner/BuildSlotSettings.cs). Shared broker uses renew mode, 20 s renew/90 s grace. |
| Configuration | shared service and runner both declare 4/6/16384 | [Compose](../../docker-compose.server2-runner.yml), lines 69–72 and 175–181. Desktop defaults are separately 2/4/6144. |
| Session capacity | declared 10, occupied 10 at 04:22:55Z | `GET /api/session-runners/server2/slots` measures agent sessions, not build leases or currently Working tasks. |
| Historical task concurrency | seven dispatched, not-yet-completed server2 tasks at 03:30, 03:57, 04:05 and 04:15Z | Project-scoped task GET and dispatch/completion timestamps; not seven simultaneous builds. |

**CARD-1506 does not change Linux/server2 admission.** [BuildDriverEnvironment](../../scripts/lib/BuildDriverEnvironment.cs) disables shared compilation, server/concurrent GC, MSBuild server use and node reuse only for Windows children. It returns no overrides on Linux. Master commits are `c15961b94b3ddef659af9fb9c32175422faf5e5c` (policy) and `462dfdd8e9f03dab9bb764bc33e91a5f8b5dc47b` (CARD-1525 absent-key repair). Neither changes slot capacity or the memory floor. No relaxation of that policy is recommended.

The incident has CPU pressure rather than observed memory-floor admission pressure. Among 203 five-second host samples from **03:58:08–04:14:58Z**, CPU averaged 75.9%, p90 94.0%, maximum 99.9%; one-minute load averaged 59.6 and peaked at 120.55 on 24 CPUs. Available memory derived from the host used-memory series and 125.90 GiB total was **85.20–99.81 GiB**. The retained executor logs contain **383 busy refusal polls and zero memory-floor refusal polls** today. This is not a complete broker history. `GET /build-slots/metrics` returned **405** at 04:25Z despite the route existing in current source/docs; CARD-1489's deployed metrics are not usable on this broker yet. No activation was attempted.

| Granted driver population | n | Minimum | Median | p90 | Maximum |
|---|---:|---:|---:|---:|---:|
| Today 00:00–04:22:55, all | 259 | 0 | 0 | 75 | 300 |
| Today, scheduled builds | 72 | 0 | 0 | 75 | 300 |
| Today, test-host rows | 112 | 0 | 0 | 45 | 300 |
| Today, command rows | 75 | 0 | 0 | 90 | 270 |
| Incident 02:30–04:15, all | 92 | 0 | 15 | 150 | 300 |
| Incident, scheduled builds | 34 | 0 | 0 | 120 | 285 |
| Incident, test-host rows | 44 | 0 | 15 | 135 | 300 |
| Incident, command rows | 14 | 0 | 37.5 | 180 | 270 |

These distributions use one `BUILD SLOT granted` record per lease, not the repeated CHECKPOINT lines that copy a shared build's wait into multiple failed rows. **Aborted acquisitions are censored, not zero-wait samples.** In particular CP-2 below waited longer than the maximum successful grant in the table. The large number of immediate grants also means the brief's saturation was not continuous all morning.

| Run, task / checkpoint | Build wait; reported build duration | Row admission / outcome |
|---|---|---|
| `034930-52fd`, `f46a522f`, CP-4..7 | 0; 371.4 | Explicit six-minute build allowance exhausted; no test launched. Executor exit 2 (`build-failed`), not a compiler diagnostic. |
| `035634-2a98`, `f46a522f`, CP-4..7 | 195; canceled result records 0 duration | Eight-minute total exhausted during build; lease held about 279 s after grant. No test launched, exit 5. |
| `040009-d640`, `ddd3fc81`, CP-10/12 | 285; 534.6, build **ok/verified** | CP-10 waited about 58 s until total deadline; CP-12 never started. Zero tests, exit 5. Separate bootstrap wait was 210 s according to the task's complete report; it precedes the run clock. |
| `035425-b9ef`, `663fcd39`, CP-1/2 | 120; canceled result records 0 duration | CP-1 waited 150 and command passed. Twelve-minute **total** killed CP-2's build before its 15-minute build allowance. Zero CP-2 tests, exit 5. |
| `034914-0c4b`, `550d40c4`, CP-2 | 0; 462.4, build **ok/verified** | Queued 03:57:21Z; last busy poll at 405 s; total expired around 04:04:19Z after about **418 s** waiting. Zero tests, exit 5. |
| `034937-1f5b`, `269a3142`, CP-2/4/7 | 0; 610.0 | Ten-minute build allowance exhausted. No tests, executor exit 2. Earlier `033639-6197` was a separate `refused: unreadable` after a compiler summary of zero errors. |

Run IDs in this table have the prefix `20261023-`. Raw coordinates are `/work/worktrees/task-<task>/.antiphon/checkpoints/<run>/{request.json,state.json,executor.log,report.json}`, plus `builds/<build-id>/build.log`. Build IDs respectively are `bin-c680-server`, `bin-ddd3fc81-proof`, `bin-c1526`, `bin-c0151-s1-green` and `bin-c1510-green`.

Two unedited log lines preserve the principal build-wait observations:

```text
2026-10-23T03:59:59.6762097+00:00 BUILD SLOT granted lease=79151fc9-fa39-4139-a0e1-3a1e8a6244b7 label="build:bin-c680-server" waited=195s maxcpucount=6
2026-10-23T04:05:08.1482628+00:00 BUILD SLOT granted lease=6f1afa3b-ddee-42ae-a7b1-478f28e5b3da label="build:bin-ddd3fc81-proof" waited=285s maxcpucount=6
```

| Classified scheduler outcome | Today: 78 terminal runs | Incident-start population: 31 terminal runs |
|---|---:|---:|
| Executor exit 0 | 38 | 14 |
| Total deadline | **4** | **4** |
| Build execution deadline, surfaced as exit 2 | **2** | **2** |
| Row/command execution deadline | 1 | 0 |
| Executed test failure (includes intentional red witnesses) | 17 | 8 |
| Executed command failure | 7 | 0 |
| Compiler error | 5 | 0 |
| Source-ledger `refused: unreadable` | 3 | 3 |
| Owner unverified | 1 | 0 |

Thus the incident has **six deadline-related runs versus eight actual test-failure runs**, plus three ledger refusals. The eight are not eight product regressions: red-first runs are included. Today's five compiler-error runs have explicit CS8122, CS1061, CS0104, CS0117 and CS0246-family diagnostics. The two build-deadline classifications use the 6/10-minute requests, 371/610-second build timing, unfinished build logs and contemporaneous task reports/transcript; the scheduler loses the driver's timeout reason when it maps a failed build to exit 2. The separate early row timeout was `f93ec86b/20261023-014342-1d38` CP-1, explicitly `timeout=2m`, after a 135 s admission wait.

`cleanup_refused` is a receipt-validation result, not proof that the tests failed or that queue wait caused cleanup trouble. The timed-out runs retain `still-present` outputs and `ReportValidator.Validate` checks cleanup refusal before accepting their receipt. Executor exit 0 in the table is likewise not a fresh validation of every receipt. Missing state, the stopped run and running runs are excluded from terminal counts.

Recovery did occur without erasing history: `550d40c4`'s complete report records direct-row reuse of the same verified build, wait 105 s, **51/51 passes**. `663fcd39` later recorded CP-1's 128 assertions and CP-2's **14/14** with validated receipts. Review `269a3142/20261023-041510-b6f4` recorded **16/16** in CP-2/4/7. These are historical outcomes, not executions by this Debug. Its killed waiter was a custom shell loop polling foreign builds/load (transcript sequences 233/240), not evidence of a broken FIFO broker or an abandoned checkpoint lease.

The deadline boundaries are visible in the source and match the evidence:

| Clock | Starts / includes | Consequence |
|---|---|---|
| Wrapper slot wait | `Enter-AntiphonBuildSlot`, before wrapped command; default 45 min | [build-slot.ps1](../../scripts/build-slot.ps1) launches only after acquisition and releases on completion. Bootstrap waits add task wall time, not the later scheduler's total. |
| Scheduler total | [RunScheduler](../../tools/Antiphon.Checkpoints/Execution/RunScheduler.cs) `RunAsync` calls `total.CancelAfter` before broker probe and passes its token into acquisitions/drivers | Includes build/row queue waits, work, and scheduler bookkeeping. This is the clock responsible for all four incident total expiries. |
| Scheduled build | `BuildOneAsync` acquires at line 416; calls `RunWithDeadlineAsync` at line 473 | **Excludes slot wait.** Bounds the confined driver, including relevant pre/post work, rather than only the compiler's printed elapsed time. |
| Row execution | `RunRowAsync` acquires at line 536 before constructing its `RowRequest.Deadline` | **Excludes slot wait.** A queued row can still lose to the independent total clock. |
| Derived defaults / overrides | [RowTimeout](../../tools/Antiphon.Checkpoints/Execution/RowTimeout.cs): row normally at least 15 min and three times estimate; selected-run total normally at least 30 min. CARD-1356 build deadline follows largest dependent row/default | `--row-timeout` replaces the build allowance exactly, including 6 or 10 min. The incident's 6/8, 10/15, 12/15 and total 12 values are explicit request overrides, not general defaults. |

The scheduler file is byte-identical in this checkout and the four principal incident worktrees (SHA-256 `873896a18d8f993127122c535d5b4a00351763845af650595b481e4a8557cd52`). Source-backed interpretation agrees with logs; no new binary execution was needed. Completed incident `Antiphon.Tests` builds (21) report median **369.7 s**, p90 **450.0 s**, maximum **534.6 s**, excluding slot wait; their compiler summaries have median 315.3 s and maximum 463.1 s. A six-minute allowance is already below the observed median confined-build cost. Queue exclusion cannot make that allowance sufficient.

There are two scheduling levels. Each checkpoint run serializes its builds, normally allows two Linux rows, and can overlap a ready row with its next build; `--serial` makes the run exclusive internally. Every build and row acquires a separate global lease. Seven tasks can therefore offer substantially more than seven activities; they are not each permanently holding three leases. The tool bootstrap normally finishes before the executor starts. All these phases use the same global budget, with **no priority for an already built artifact**.

For example, at 03:57:22Z four known live lease intervals comprised `269a3142`'s build, `a3467729`'s build and `663fcd39`'s build plus command row. `550d40c4` had released its successful build and was now fourth in the queue. It reached position one but no slot became available before its remaining total budget expired. That is verification starvation relative to a finite deadline, not proof of indefinite FIFO overtaking. Prioritization cannot preempt already held leases; unbounded test priority would merely move starvation to builds. Default canceled-row placeholders also erase partial wait (`waitedSeconds=0`), so state-only statistics understate the problem.

Recommended order, preserving the [CARD-0589/CARD-0723 contracts](../testing-and-build.md#build-slots-card-0589):

| Rank | Action / existing owner | Expected benefit and boundary |
|---|---|---|
| 1 | Avoid duplicate same-source builds using existing within-run `CP-n` references and validated direct-row reuse; commission budgets using measured work plus queue, rather than arbitrary short overrides. CARD-1493 / CARD-1356. | Cheapest immediate relief. `550d40c4` demonstrates recovery without recompiling. Preserve exact source, properties, output ownership and fresh execution receipts. Do not silently widen an assigned deadline or relabel an incomplete gate. |
| 2 | **CARD-1531: distinguish admission and total execution budgets**, with a separate finite hard wall cap and truthful terminal timing. | Addresses the two successful builds stranded before any test, and the 195 s wait consuming the eight-minute total. Per-driver clocks already exclude queue time. Suspend a run execution clock only during the union of intervals when all runnable work waits for admission and no driver executes; never sum concurrent waits or extend running work because a sibling queues. Keep bounded slot waits, owner/caller cancellation and joined cleanup. Requires design and fake-clock tests, not an unlimited timeout. |
| 3 | Bootstrap tool artifact reuse, then immutable cross-run Code/Review artifacts. CARD-1493 item 4, coordinate CARD-1380. | Avoids a queue admission and tool build per identical tool input; avoids full test builds across identical validated inputs. In the sample, Code `5019ea31` and Review `269a3142` both build source `705662142c95e9c3614a8a229f0fbd4b2bbf2c66`. Cache full dependency/toolchain/configuration inputs and hashes, atomic publication and consumer-owned scratch; existing fresh-output custody forbids simply sharing mutable `bin/obj`. Same-group importer warnings already exist in current source. |
| 4 | Consider bounded ready-test preference or borrowing between build/run lanes. CARD-1489 / CARD-1348. | Can shorten post-build admission, but must keep aggregate memory/driver bounds and aging for builds. No priority exists today, and tests are not all cheap; no unconditional reserved slot or unlimited test lane is justified. |
| 5 | Reduce service time in CARD-1514's existing plan. | Its Windows PersistenceFailure totals were 24.6–32.3 min; Linux was 249.2 driver seconds for 52 passes, not a controlled OS comparison. The plan owns fixture/materialization, Git-lane and measured clock work. It does **not** own the Code/Review artifact cache. This reduces long holds, not queue accounting. |
| 6 | Capacity experiment only after deployed metrics and operator authorization; retain 4/6/16384 for now. CARD-1489. | Memory headroom exists, but CPU p90 94% gives no measured throughput guarantee from more simultaneous builds. Prior investigation proposed a bounded 5-slot/4-node experiment, not established safe production capacity. Never lower the memory floor or CARD-1506 policy. |

Duplicate searches completed through `scripts/card.ps1 search ... -Board Antiphon -All`: `slot` (157), `starv` (27), `checkpoint deadline` (0), `total timeout` (2), `slot wait` (9). Full relevant cards read: CARD-0589, 0723, 1348, 1356, 1489, 1493, 1506 and 1514. **CARD-1531** (`65bc3000-1bac-415d-b031-c0974649f3af`) was created in Backlog for the distinct total-admission-clock gap; no duplicate cache/lane/slow-test cards were filed. The previous [throughput investigation](2026-10-22-server2-build-slot-throughput.md) and [CARD-1514 plan](../superpowers/plans/2026-10-23-card-1514-slow-test-speedups-plan.md) provide the existing implementation boundaries.

Reproduce the read-only measurements by enumerating `/work/worktrees/task-*/.antiphon/checkpoints/20261023-*/executor.log`, deduplicating granted lease UUIDs, and joining each run's state, request, report and build logs. Keep canceled acquisitions separately. Read the shared broker, not `/api/session-runners/server2/slots`, for build capacity. Host series came from `/api/hosts/server2/stats/series?metric=cpu|memory|load&window=30m`; task history from `/api/agent-tasks?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f&since=2026-10-23T00:00:00Z`. All API reads used the inherited task header without printing it. No test/build rerun is needed to land this documentation.

--- next stage ---
next: land
handoff: Land this documentation; plan CARD-1531 separately. Retain slot/memory policy and historical incomplete receipts. Coordinate reuse/lanes/test-speed work with the existing cards above.
artifact: docs/investigations/2026-10-23-build-slot-starvation-debug.md
