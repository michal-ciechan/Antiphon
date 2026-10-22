# Server2 build-slot throughput, 2026-10-22

**Recommendation: retain `MaxConcurrent=4`, `MaxCpuCount=6`, and the 16,384 MiB memory floor for production now. Remove redundant builds before increasing admission. If the operator wants a capacity experiment, trial five slots with four MSBuild nodes per direct build, not six slots with six nodes.** The experiment is not an established safe capacity or a measured speedup.

This investigation confirms minute-scale queuing, expensive repeated compilation, and CPU saturation during compiler bursts. It does **not** find a memory-floor bottleneck on server2. Four slots are sometimes underfilled between polls and sometimes CPU-saturating; `4 × 6` is not a CPU reservation. The shared compiler and test hosts have their own parallelism.

Task `17dd54d6`, board CARD-1489. Source inspected: `2fc03221aad59278be9a526893b91e3a88036ac5`, branch `feat/card-task-17dd54d6`. Runtime observations are separately dated below; the shared broker's deployed image SHA was not independently verified. No settings, source, running processes, deployments, cards, or Windows qualification were changed. No build or test was launched. Auth files and Codex homes were not accessed.

## Evidence and limits

| Evidence | Scope and acquisition |
|---|---|
| Task history | `GET /api/agent-tasks?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f&since=2026-10-22T09:00:00Z`: 45 task rows at approximately 12:17 UTC. Filtered to server2 and known session IDs. |
| Transcripts | 39 successful `GET /api/sessions/{id}/transcript?since=0` responses, excluding this investigation. Used tool results, not assistant claims, for lease evidence. Repeated log displays were deduplicated by lease UUID. |
| Checkpoint records | 36 completed `report.json` files and their available `executor.log`, build logs and resolved manifests in those tasks' worktrees. Unused manifest build definitions were excluded. These are historical measurements, not a new validation of their test verdicts. |
| Historical measurement window | 09:00–12:00 UTC. Exact checkpoint grant/release timestamps come from executor logs. Transcript result timestamps can be later than execution, so transcript-derived hourly totals are approximate. |
| Host series | 360 five-second CPU samples, 11:46:28–12:16:23 UTC, from `GET /api/hosts/server2/stats/series?metric=cpu&window=30m`; also retrieved load, memory and `/api/hosts/stats`. |
| Direct sample | 40 broker/proc snapshots in four approximately 45-second bursts between 12:17:06 and 12:22:07 UTC; 36 five-second CPU intervals. Gaps between bursts are not measured. |
| Broker history access | The broker exposes current state, not a history/percentile endpoint. Its source logs grants, releases and reaps. Read-only SSH retrieval of `antiphon-runner-build-slots-1` logs failed host-key verification; trust was not bypassed. |

Generated analysis and extracts are ignored under `/work/worktrees/task-17dd54d6/logs/investigation-17dd54d6/` (`metrics.json`, `sample-metrics.json`, `executor-grants.json`, `counted-builds.json`, `reports.json`, `transcript-index.json`, `samples.jsonl`). They are local working evidence and may disappear with mirror retirement. This committed note retains the findings and source coordinates needed by the caller. The counts are a bounded Antiphon-task sample, **not a complete census of all host work**. No three-hour host CPU archive was available through the 30-minute series API.

## Configuration and admission

The live shared endpoint `http://build-slots:8080/build-slots` answered:

| Setting | Effective observation | Source |
|---|---:|---|
| Enabled | `true` | Live GET; code default is true |
| Concurrent drivers | 4 | Live GET and `SessionRunner__BuildSlots__MaxConcurrent` |
| MSBuild node hint | 6 | Live GET, sampled grants and direct build argv |
| Available-memory floor | 16,384 MiB | Live GET and `SessionRunner__BuildSlots__MinAvailableMemoryMb` |
| Lease lifetime | 90 minutes | Live grant/expiry pairs; `BuildSlotSettings.LeaseTtlMinutes` |
| Retry interval | 15 seconds | Executor 409 payloads and settings default |
| Shared broker liveness | Compose specifies renewal mode, 20-second renewal, 90-second grace | Declared configuration; those interval values are not exposed by GET |

The inherited runner environment independently contained `4`, `6`, `16384`, and `ANTIPHON_BUILD_SLOTS_URL=http://build-slots:8080/build-slots`. The [compose file](../../docker-compose.server2-runner.yml) has the three budget values in **both** `session-runner.environment` and the separate `build-slots` service. The broker service runs `SessionRunner__BuildSlotsOnly=true`; its renewal policy permits holders in other PID namespaces. The configured shared endpoint, rather than the runner's loopback broker, governs these delegates. Changing only the session-runner stanza would not change the separate broker's admission budget.

[BuildSlotSettings](../../src/Antiphon.SessionRunner/BuildSlotSettings.cs) has desktop defaults 2/4/6144. [BuildSlotRoutes](../../src/Antiphon.SessionRunner/BuildSlotRoutes.cs) binds `SessionRunner:BuildSlots` once through options. [BuildSlotBroker](../../src/Antiphon.SessionRunner/BuildSlotBroker.cs), `TryAcquire`, implements:

1. Reap expired/dead holders and silent waiters; repeated acquire by the same PID/start identity returns the existing lease.
2. Add or refresh a FIFO waiter. Grant only if its position is within the number of free slots. A newcomer can therefore receive busy while a slot is free.
3. Check live available memory against the floor before granting. Unknown memory does not refuse admission. Already-held leases are not revoked when memory falls.
4. Keep only current leases/waiters. Release deletes the lease; there are no retained aggregate wait, throughput or percentile counters.

The default waiter-silence limit is 60 seconds; sweep interval is 30 seconds. The shared compose deployment uses renew-based holder liveness rather than local PID ownership. The memory reading is Linux `MemAvailable`, not `MemFree`. This container had 24 logical CPUs, no CPU quota (`cpu.cfs_quota_us=-1`), and an effectively unlimited cgroup-v1 memory limit (`9223372036854771712`). `memory.failcnt` was zero; that is not proof of no historical host OOM.

**Observability defect:** around 12:16, the Hosts API reported server2's build slots as `occupied=0`, while the shared broker had active leases. [HostStatsStore.ReadBuildSlots](../../src/Antiphon.SessionRunner/HostStatsStore.cs) reads its injected **local** broker; it does not follow `ANTIPHON_BUILD_SLOTS_URL`. The host CPU series remains useful, but the page's slot counts are not the shared broker's counts in this topology. The older loopback probe examples in the operational docs have the same ambiguity. Caller triage is needed; this read-only task did not file or edit a card.

## Queue cost and build frequency

For **128 checkpoint leases granted and released inside 09:00–12:00**, the measured wait was:

| Population | Count | Mean wait | Median | p95 | Maximum |
|---|---:|---:|---:|---:|---:|
| All checkpoint drivers | 128 | 83.2 s | 60 s | 240 s | 270 s |
| Builds | 44 | 53.5 s | 0 s | 210 s | 240 s |
| Test/command rows after build | 84 | 98.8 s | 105 s | 240 s | 270 s |

Percentiles use nearest rank. Hourly checkpoint grants were 31, 52 and 45 for 09, 10 and 11 UTC; their mean waits were 31.5, 94.3 and 106.0 seconds. Exact completed checkpoint drivers in those hours were **28, 53 and 47**, or **42.7/hour across the three-hour sample**. This is a lower bound on host driver throughput, since wrappers and other work are additional.

Transcripts add 73 distinct wrapper grants absent from those executor logs: 201 observed grants total, mean wait 78.2 seconds, p95 225 seconds. That is approximately **67 observed admissions/hour**, not a complete throughput rate. Sixty-two of those extra grants also have a released/held-duration line. The checkpoint logs contain 917 busy observations and **zero memory-floor observations**; repeated polls are observations, not 917 separate queued jobs.

Checkpoint receipts record **45 `tests/Antiphon.Tests` build attempts completing in the window: 10, 21 and 14/hour**, comprising 44 successful builds and one failed build. Thus even this subset rebuilt the large test project **15 times/hour**. Wrapper builds, implicit tool builds, mutation/baseline work and other projects increase the total. The 39 available MSBuild elapsed summaries average **296 seconds**, median **307 seconds**, p95 **404 seconds**, maximum **581 seconds**; these include the failed attempt. Six build logs were absent, so they contribute to receipt counts but not elapsed statistics. Manifest entries marked `unused` were excluded; counting all definitions would have invented 89 extra builds.

Build leases held 15,036 seconds in aggregate (mean 342 seconds). Row leases held 7,649 seconds (mean 91 seconds): **34% of checkpoint slot-holding time was rows, not builds**. A count of four occupied slots consequently does not mean four simultaneous six-node builds.

A concrete expensive short command: task `00195a3a`, transcript sequence 109, waited **226 seconds** to rebuild `tools/Antiphon.Checkpoints`, then held its slot only **8 seconds**. FIFO does not prioritize short tool bootstraps over test rows.

## CPU, memory and work outside driver ancestry

The historical 30-minute CPU series averaged **70.1%**, median 75.1%, p95 96.9%, maximum 99.8%. The earlier Hosts snapshot's 30-minute load maximum was **62.7**. Load alone does not distinguish runnable threads from blocked work.

During the direct sample, workload declined: only **11/40** snapshots were full, **8/40** had waiters, and **3/40** had both a free slot and waiters. The last snapshot was 2/4 with no queue. Thus the brief's earlier near-continuous saturation was not continuous throughout this investigation. FIFO waiters only claim a freed slot on a retry; the 15-second polling interval explains short underfilled gaps without a memory refusal.

| Direct measurement | Result |
|---|---|
| CPU while a queue was visible | Mean 71.6%, range 31.6–93.7%; only seven measured intervals |
| CPU over all 36 intervals | Mean 50.2%, maximum 93.7%; includes the later quiet period |
| I/O wait | Mean 0.23%, maximum 1.95% |
| Available host memory | 92.0–99.5 GiB, versus a 16 GiB admission floor |
| Swap used | Stable at 23.8 MiB |

This is evidence for **bursty compiler CPU pressure plus mixed driver service times**, with some underfilled intervals. It is not evidence that builds are predominantly blocked on disk. It also does not establish that every MSBuild phase can use all CPUs.

Process CPU below is `/proc/<pid>/stat` tick delta divided by elapsed seconds and `CLK_TCK=100`, in **CPU cores**, not host percentages. Processes that start and finish between polls are missed. Only this runner's PID namespace was visible.

| Visible process group | Mean cores | Peak cores | Relation to slots |
|---|---:|---:|---|
| Shared `VBCSCompiler` | 5.35 | 17.34 | PID 5493, PPID 1; serves compiler requests from builds, survives their wrappers. Up to 8.0 GiB RSS in this sample. Physically outside holder ancestry, but its CPU coincided with build work; do not label it an independently rogue build. |
| MSBuild workers | 0.54 | 4.15 | Mostly `/nodeReuse:false` children of active builds. One retained `/nodeReuse:true` worker, PID 31234, consumed zero measured CPU and about 191 MiB RSS. It cannot explain the observed load. |
| Direct build drivers | 0.17 | 1.41 | Direct observed builds carried `-maxcpucount:6`. |
| Test hosts and their `dotnet run` parents | 1.78 | 5.69 | Matched output tags/parents to row leases in later snapshots. For example, `bin-c0167r2` hosts matched CP-5/CP-6 leases; `bin-c1464red` matched `c1464-precheck`. No sampled busy test host was established as an escaped unleased run. |
| Checkpoint executors/helpers | 0.49 | 2.05 | Git/source/output bookkeeping also runs before acquisition and after release. This category is not all unleased CPU. |
| Agents | 0.75 | 1.15 | Outside build slots, as expected. |
| Nested Docker processes | 0.020 | 0.101 | Daemon/container management is outside the wrapper tree. Host Docker and other host containers are not in this PID view. |
| Node | 0.012 | 0.042 | Includes this lightweight sampler; no Vitest/tsc/eslint process was observed. |
| Git | 0 observed | 0 observed | Brief Git children can escape five-second sampling; this does not prove zero Git cost. |

Mean host busy CPU was 12.05 cores versus 9.36 accounted by sampled visible processes. The **2.69-core residual is unassigned**, combining other host namespaces, kernel accounting, and short-lived/missed processes. It cannot responsibly be attributed to Docker or ungated tests without host-level process access.

[Directory.Build.rsp](../../Directory.Build.rsp) disables worker node reuse, but leaves shared compilation enabled. `MaxCpuCount` caps MSBuild nodes, **not** compiler threads or TUnit parallelism. There are also real node-cap gaps: [scripts/lib/build-slot.ps1](../../scripts/lib/build-slot.ps1), `Add-AntiphonMaxCpuCount`, only rewrites direct `dotnet build/test/publish/pack/msbuild`. It does not rewrite a build hidden inside `pwsh -Command`, and `dotnet run` with an implicit build cannot receive that argument safely. Task `8333e231`, sequences 120/125/130/135, used a leased PowerShell command containing a build without an explicit node count. Its sequence 103 explicitly printed the implicit-build warning. Those commands are leased but not capped by the grant. Tests that themselves invoke MSBuild similarly require separate accounting rather than assuming one node per slot.

## Repeated work and safe reuse

The 44 successful checkpoint builds represent **29 distinct commit/project pairs**. Fifteen repeats (34%) are candidates for further examination, not fifteen proven interchangeable artifacts. SDK, configuration, global properties, repeat-generation settings, generated inputs and output custody also matter.

Nine repeats occurred within the same run, project and commit. Resolved manifests show **five** already have matching `After`, `Repeat=1`, and global build properties, so the existing `CP-n` reuse mechanism can avoid a second build without changing policy:

| Task / run | Redundant second output | Measured lease held | Wait before that build |
|---|---|---:|---:|
| `82104d2c / 20261022-101841-f05b` | `bin-dispatch-save-regression` | 219 s | 105 s |
| `82104d2c / 20261022-103300-7977` | `bin-dispatch-save-regression` | 259 s | 75 s |
| `8c58151b / 20261022-105833-b10a` | `bin-dispatch-save-regression` | 217 s | 210 s |
| `00195a3a / 20261022-111120-c8dc` | `bin-c1479-regression` | 210 s | 0 s |
| `c1da64a5 / 20261022-113447-251d` | `bin-c1479-regression` | 219 s | 240 s |

These five builds consumed **18.7 slot-minutes plus 10.5 minutes of their own queue wait**. Avoiding analogous builds would remove 11% of successful build attempts in this sample and about 5% of its checkpoint slot-holding demand. It would save each affected task roughly 3.5–7.7 minutes of second-build wait/hold work, subject to row scheduling; these are observed costs, not a simulated new makespan. The four remaining within-run repeats had different `After` groups (CARD-0167 S1 versus S2/S3). Current import rules prohibit directly reusing across those groups. A future consolidated verification manifest needs an explicitly reviewed common committed group; do not silently rewrite historical stage evidence.

Cross-task same-SHA examples include:

- `ad13d6cd36018d2eaedc39b80f99d5612fefd27c`: reviews `8e8726fd` and `7db88932`, builds 388 and 194 seconds.
- `9837fc967ebc4baa46703cb2a22847d9b9139eb0`: Code `00195a3a` and Review `c1da64a5`, each built both green and regression outputs: four full builds at the same source.
- `0e8be8391245d51ce506bfcbd2e9c93bd0257641`: Code `8333e231` and Review `df795c38`, 207 and 368 seconds.

[PlanTableImporter](../../tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs) resolves `CP-n` to the earlier build ID and checks the same `After` and `Repeat`. [RunScheduler](../../tools/Antiphon.Checkpoints/Execution/RunScheduler.cs) builds each needed ID once per run, keeps one local build lane, and may run two rows on Linux beside the next build. Each build and each row separately acquires a global lease. Different output IDs remain different builds even when their inputs match.

Fresh output custody currently refuses pre-existing output/stamps. A cross-run incremental cache cannot simply reuse a `bin-*` directory without changing that contract. A future immutable cache should key source tree/commit, SDK/toolchain, OS/architecture, configuration, all relevant properties/repeat settings, dependency inputs and generated-input identity; publish atomically, verify hashes, and give each consumer owned output/scratch and fresh execution receipts. Cache the full required artifact set, not one DLL. Shared mutable output and `obj` directories would reintroduce races and stale-mutant results.

An unchanged-base artifact can help an inherited-failure check **only when it contains the selected tests and compatible dependencies**. A new red-witness test does not exist in an older base assembly: it must still be compiled against the relevant baseline production source. Green and mutated/restored source are different inputs. Preserve required red/green and SourceLanding mutation evidence; eliminate duplicate builds of the same inputs, not required test executions. Review-time ad hoc mutations in the sampled transcripts add compilation beyond the normal stage flow; the current owner policy commissions sourced mutation after land.

## Options and operator decision

| Option | Expected effect, with uncertainty | Cost/risk |
|---|---|---|
| **Keep 4/6/16384 and reuse same-group outputs** | Removes the measured 18.7 slot-minutes per comparable three-hour workload without increasing CPU concurrency. Fleet p95 improvement is unmeasured. | Small plan corrections for future work; preserve all exact filters and source bindings. Recommended first. |
| **Trial 5 slots / 4 nodes** | If service times were unchanged, admission capacity rises 25%; for a fixed queue the service-wave wait component falls about 20%. Applied mechanically to the 240-second p95, that is about 48 seconds, **not a prediction**. A steady-state queue can improve more or regress. | Compiler/test parallelism remains uncapped. Reducing nodes can lengthen builds. More simultaneous hosts raise memory use. Trial only after direct/nested build argv are accounted for. |
| 6 slots / 6 nodes | No defensible wait estimate from this sample. | More simultaneous compilation during existing 97–100% CPU peaks; greater tail-latency and OOM risk. Not recommended. |
| Separate build/run lanes | Checkpoint rows occupy 34% of sampled slot time; separating long runs can reduce build head-of-line blocking. | Do not make test hosts unlimited. Start design with bounded lanes, e.g. three build + two run drivers and an aggregate memory/admission guard, then measure. This is a policy/design change under CARD-1348, not a 30-minute wrapper edit. |
| Shared immutable artifacts per validated inputs | Same-SHA repeats show real opportunity; a cache hit can avoid roughly 3–7 minutes of compilation plus queueing. Hit rate and consumer/custody overhead unmeasured. | Larger implementation than this brief's small Code slice. Source and toolchain provenance, test portability, cleanup and eviction must be designed first. |
| Priority or reserved land slot | No present server2 land-wait benefit: the recorded land architecture uses the desktop broker. | CARD-1461 is a desktop memory-floor issue; priority cannot make insufficient memory available. A permanently reserved one of four slots cuts general capacity 25% when unused. If remote lands are later enabled, prefer bounded borrowing/priority with starvation protection. |
| Reduce retry polling interval | Could remove part of the 0–15-second vacancy delay. | It does not remove minute-scale service queues; increases polling. No change recommended from seven queued CPU intervals. |

The budget value belongs to the operator. The recommended decision is **retain four for sustained production**, approve same-group build reuse in upcoming Code/Review manifests, and commission broker metrics before a capacity experiment. If immediate experimentation is preferred, select **five/four/16384** for a matched-load 30-minute trial and compare throughput, mean/p95 wait, build duration and CPU against four/six. Keep the 16 GiB floor. Suggested stop criteria are sustained CPU above 90% with no throughput gain, p95 build duration worsening more than 20%, or available memory approaching 24 GiB; these are proposed trial criteria, not new policy. The floor checks admission only and reserves no memory for later growth, so it cannot guarantee against OOM. No experiment or operational change was performed here.

The caller should also triage the Hosts-page local/shared-broker mismatch and uncapped nested/implicit builds. Neither justifies touching the Windows qualification.

## Existing board work and a bounded Code handoff

Complete board searches used `scripts/card.ps1 search 'build slot' -Board Antiphon -All` (30 hits) and `'build-slot'` (43 hits); full descriptions were read for the relevant cards:

| Card | State observed | Relevance |
|---|---|---|
| CARD-1489 | InProgress | This throughput investigation; keep the decision and proposed metrics slice here. |
| CARD-1348 | Review | Build-only lease/separate-run follow-up already exists. Reconcile its design before any lane change. |
| CARD-1380 | InProgress | Warm land artifacts and non-code skip; do not duplicate it as a new cache-for-lands card. |
| CARD-1462 | Review | Why lands use desktop and possible remote alternatives. |
| CARD-1461 | Done | Default-off desktop memory-floor fallback landed on master; card says not activated. It is not a shared server2 budget change. |

**Proposed Code task, 45–60 minutes: bounded read-only broker metrics.** This is the small source change recommended to make the budget experiment measurable. Scope: `BuildSlotBroker`, a small metrics recorder/DTO, `BuildSlotRoutes`, and focused `Antiphon.SessionRunner.Tests` tests. Add `GET /build-slots/metrics` with process-start/window timestamps, sample counts, grants, explicit releases, reaps, mean/p95 acquire wait, held duration and observed completion rate. Retain at most three hours and 4096 lifecycle records; disclose truncation and partial windows. Use `TimeProvider`; count a grant once despite acquire retries, separate reaps from successful releases, and count refusal polls separately from queued jobs. A read must not mutate admission state. Do not change settings, FIFO, floor, lease lifetime, wrappers, deployment, or the Hosts UI in this slice.

S1-test: add a loopback endpoint witness that queues for a fake-clock duration, grants/releases, then requires the metrics route and its counts; before implementation it fails on the missing route (404), with a successful build. Commit/push that red witness first. S1: implement the bounded recorder and five tests covering (1) timed queue/hold, (2) idempotent acquire/release and reap separation, (3) time-window expiry, (4) capacity/truncation, (5) empty/partial windows and read-only snapshots. Existing broker/endpoint tests retain the admission contract. This task does not require a full `Antiphon.Tests` build.

### Checkpoints

This is the **proposed next Code task's closed list**, not executions claimed by this Debug task. Use the checkpoint tool once per committed slice; wait to completion. The red and green outputs differ because source differs. CP-3 reuses CP-2's output at the same `After`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-test | tests/Antiphon.SessionRunner.Tests -> bin-c1489-metrics-red/ | metrics-red | `/*/*/BuildSlotMetricsTests/Queued_wait_and_completed_lease_are_counted_once` | V-1 missing observable metrics | 1 executed, 1 intended assertion failure, build succeeds | 1 | 8 |
| CP-2 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1489-metrics-green/ | metrics-green | `/*/*/BuildSlotMetricsTests/*` | V-1 through V-5 | 5 executed, 0 failed, 0 skipped | 5 | 8 |
| CP-3 | S1 | CP-2 | admission-regression | `/*/*/(BuildSlotBrokerTests*)\|(BuildSlotEndpointTests*)/*` | R-1 FIFO/floor/renewal and existing HTTP contract | all listed, 0 failed, 0 skipped | 19 | 3 |

Estimated coding 25–35 minutes plus 19 checkpoint minutes. If the checkpoint tool needs a bootstrap, budget and disclose one gated isolated tool build before these rows; do not rebuild it for every row. No broad suite, production-broker mutation, desktop test or deployment belongs to this task. The existing bare-host endpoint test should keep its refusal of unrelated session routes while admitting the new metrics route. Caller chooses whether to commission this task after the budget decision.

## Reproduction and retained examples

All API reads used the inherited task header without printing it. Repeat the scoped task and session GETs above, and poll `ANTIPHON_BUILD_SLOTS_URL` rather than the runner loopback URL. For live utilization, difference the first eight `/proc/stat` CPU counters (idle plus iowait are non-busy), `/proc/<pid>/stat` counters, `/proc/meminfo` and `/proc/loadavg`; this host reports `getconf CLK_TCK` as 100. Validate PID start times between samples. A host-level process census remains necessary to allocate the residual CPU.

Exact historical lease lines illustrating a long wait and short tool build, task `00195a3a`, sequence 109:

```text
BUILD SLOT granted lease=475a7547-717b-4e84-96a5-927475c706ee waited=226s maxcpucount=6
BUILD SLOT released lease=475a7547-717b-4e84-96a5-927475c706ee held=8s
```

Source-bound historical examples, retained unedited (these are not fresh tests by this task):

```text
CHECKPOINT CP-2 commit=9837fc967ebc4baa46703cb2a22847d9b9139eb0 build=ok filter=/*/Antiphon.Tests.Application/RunnerSyncLeaseFairnessTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-00195a3a/.antiphon/checkpoints/20261022-111120-c8dc/rows/CP-2/run.trx slot=granted waited=15s dirty=0 source=9837fc967ebc4baa46703cb2a22847d9b9139eb0 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=9837fc967ebc4baa46703cb2a22847d9b9139eb0 build=ok filter=/*/Antiphon.Tests.Application/(RunnerSettlementSyncTests*)|(AgentTaskLandDispatchYieldTests*)|(RepositoryLeaseWaitersTests*)/* executed=47 passed=47 failed=0 skipped=0 trx=/work/worktrees/task-00195a3a/.antiphon/checkpoints/20261022-111120-c8dc/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=9837fc967ebc4baa46703cb2a22847d9b9139eb0 sourceState=clean buildSource=verified
```

--- next stage ---
next: decide
handoff: Operator chooses retain 4/6 or a bounded 5/4 trial; recommend retain 4/6, consolidate eligible builds, and commission the 45–60 minute metrics Code task on CARD-1489. Coordinate lane changes with CARD-1348 and land caching with CARD-1380. Triage the local/shared broker observability mismatch separately.
artifact: docs/investigations/2026-10-22-server2-build-slot-throughput.md
