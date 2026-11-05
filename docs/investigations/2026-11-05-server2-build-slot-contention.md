# server2 build-slot contention: confirmed admission contention, incomplete capacity diagnosis

Date: 2026-11-05 UTC. Task: `a8d643be-e182-4ec8-b67c-767be7e58e22`, attached to Antiphon CARD-1514. Source inspected: `d10e1c51a72852aa51895109b66e567f81346dca`. Historical reconstruction window: **2026-11-04 20:16 through 2026-11-05 08:16 UTC**. Live observations began at 08:16:49.

**Confirmed:** seven or eight concurrent tasks repeatedly compete for four shared, non-preemptive build/test leases; individual tasks can hold multiple leases. Long test selections and cold builds occupy these leases while short builds/tests queue. This consumes task wall budgets and demonstrably leaves verification unfinished. The evidence does **not** establish a broken FIFO implementation, dead-holder leak, memory-floor incident, or a safe larger slot count.

This is an evidence report, not a fix design. No live setting, process, service, branch selection, product code, or test was changed. No build/test was launched. Existing task authorization covered the read-only investigation; no additional approval was needed.

## 1. Mechanism and a reconstructed full-pool incident

`src/Antiphon.SessionRunner/BuildSlotBroker.cs:94` appends new waiters, retaining their original position across polls. At line 102 it calculates free slots; line 103 refuses a caller outside the free-slot window. At line 110 memory is a separate admission condition. Neither task role, compile/test type, expected duration nor per-task occupancy participates in admission. Builds and test runs use the same `BuildSlotBroker` singleton (`BuildSlotRoutes.cs:24`). Thus a long test occupies capacity that could admit a short compile, without either having priority. With multiple free slots, callers inside the head window can be granted in poll order; this is FIFO eligibility, not strict timestamp ordering of simultaneous grants.

At **05:19:56**, the stored broker response showed `occupied=4`, `budget=4`, `availableMb=95672`, `floorMb=16384`, and these four leases:

| Lease | Task | Label | Granted UTC | Subsequent measured hold |
|---|---|---|---|---:|
| `1b2d27ce-cd51-4a53-94c7-057f652be7e5` | `f059674d` | `g71-master-class-13-through-20-retry` | 05:07:21.920 | 974 s |
| `5d12cf0e-f90d-4095-85d3-e8b66947127d` | `f059674d` | `g71-master-class-4-through-12-retry` | 05:07:52.042 | 1103 s |
| `52a8f737-8d46-41c0-afaa-1384b7c40bc0` | `27e2f0b5` | `build:bin-c1718-r2-server` | 05:17:48.945 | See lease ledger |
| `fc3f3cd1-f08e-4b22-af62-f7ea8931a78c` | `727a57dd` | `merge72-g1` | 05:18:36.849 | 676 s |

The head waiter was `build:bin-c1718-r2-runner`, queued since 05:14:09.662; the stored response truncates later waiters, so it cannot establish total queue length. The two no-build repetition batches belonged to one Debug task and occupied **half the pool concurrently**. Their logs contain successive RUN/EXIT records, rather than just polling an already-finished operation. The archived report describes separate test processes and PostgreSQL containers; no build occurs inside those batches.

Provenance: normalized session `7b8ee554-9f75-4dfe-9534-88696700e751`, sequence **287**, 05:19:56.268; the same known session's provider diagnostic JSONL retains all four lease objects in `toolUseResult.stdout` (normalized text truncates sooner). The queue remains truncated in both. Batch logs are members `g71-evidence/master-batch-13-through-20-retry.log` and `g71-evidence/master-batch-4-through-12-retry.log` of `/tmp/antiphon-f059674d-evidence.tar.gz`. Their grant/release lines and the bounded broker response are retained with this investigation's external evidence.

An independent short-run witness:

```text
2026-11-05T05:15:39.4845370+00:00 BUILD SLOT granted lease=da9a356a-569c-465d-a719-ba36c26929ab label="CP-1@bin-c1720-final" waited=585s maxcpucount=6
2026-11-05T05:17:07.2092762+00:00 BUILD SLOT released lease=da9a356a-569c-465d-a719-ba36c26929ab label="CP-1@bin-c1720-final"
```

These are unedited `task-c3ba1792/.antiphon/checkpoints/20261105-044841-7f06/executor.log:64` and line 65. Admission took **585 s**, the lease **87.725 s**, and CP-1 passed **117**, failed **0**, skipped **4**. Source: task report `/work/worktrees/task-c3ba1792/.antiphon/task-c3ba1792.md`, and normalized session `25182523-844b-4b2d-a2d5-71e6b879d22b`, sequence **68**. This reconstructs real queue delay, not a slow passing test mislabeled as slot wait.

## 2. Configuration and resource measurements

| Control | Code default | server2 Compose value / observed value | Authority |
|---|---:|---|---|
| `SessionRunner:BuildSlots:MaxConcurrent` | 2 | 4 / live budget 4 | `BuildSlotSettings.cs:16`; `docker-compose.server2-runner.yml:176` |
| `...:MaxCpuCount` | 4 | 6 / live 6 | settings line 19; Compose line 177 |
| `...:MinAvailableMemoryMb` | 6144 | 16384 / live floor 16384 | settings line 22; Compose line 178 |
| `...:LeaseTtlMinutes` | 90 | no override in broker Compose section | settings line 25 |
| `...:RetryAfterMs` | 15000 | no override in section | settings line 28 |
| `...:WaiterSilenceMs` | 60000 | no override in section | settings line 31 |
| `...:SweepIntervalMs` | 30000 | no override in section | settings line 33 |
| `...:HolderLiveness` | `pid` | Compose `renew` | settings line 35; Compose line 179 |
| `...:RenewGraceSeconds`, `...:RenewEverySeconds` | 90, 20 | 90, 20 | settings lines 36–37; Compose lines 180–181 |

Section binding is `src/Antiphon.SessionRunner/BuildSlotRoutes.cs:17`. Compose's environment spelling uses `SessionRunner__BuildSlots__...`. Delegate endpoint is `ANTIPHON_BUILD_SLOTS_URL=http://build-slots:8080/build-slots`; the session runner's loopback broker is not this shared service.

Live GET at **08:16:49** returned enabled=true, budget=4, maxCpuCount=6, occupied=2, availableMb=96719, floorMb=16384, no waiters, both holders alive. GET does not expose liveness mode, TTL settings, image SHA, or environment overrides; the deployed liveness mode is therefore not independently proven by that GET. Compose specifies renew mode.

| Measurement | Observation | Interpretation / limit |
|---|---|---|
| `nproc` | 24 | Visible logical CPUs, not a physical-core census |
| `free -m` at 08:16:49 | total 128919 MiB; available 96721; swap total 529, used 31 | Ample current memory; not the floor refusal path |
| `uptime` at 08:16:49 | uptime 226 days; load 23.91 / 28.00 / 38.65 | Load is runnable/uninterruptible demand, not CPU utilization |
| `df -h /work /state /` | underlying 705G; 283G used, 394G available, 42% | No capacity exhaustion in this sample; does not measure I/O latency |
| cgroup v1 limits visible inside runner | CPU quota -1, period 100000; memory limit 9223372036854771712 | No effective quota at the inspected cgroup; not an audit of all host/container limits |
| Six 5-second `/proc/stat` deltas, 08:23:50–08:24:20 | idle 8.4%, 21.5%, 35.8%, 53.8%, 64.4%, 75.0%; I/O wait 0–0.7% | Pool had one occupied slot and no queue in these samples, so not a full-pool capacity benchmark |
| `/proc/meminfo` during those deltas | available approximately 98,503–100,779 MiB; swap usage stable | No observed swap storm |
| Stored full-pool response, 05:19:56 | 95672 MiB available, floor 16384 | That busy incident was not memory-floor admission |
| Stored compilation observation | approximately load 73 on 24 CPUs | `/work/worktrees/task-f059674d/docs/investigations/2026-11-05-debug-rapid-fire-merge-g71.md:49`; supports CPU pressure, not a utilization/latency attribution |
| Stored process sample at approximately 05:01 | `VBCSCompiler` PID 7673 reports 850% CPU | `/tmp/11a6d8bb-evidence/process-sample.txt:37`; process-lifetime CPU average, not an instantaneous host sample |

`-maxcpucount:6` caps MSBuild nodes for supported direct commands; it is not a six-core CPU quota, test concurrency cap, or compiler-thread cap. `scripts/build-slot.ps1:96` applies it; lines 97–104 warn about implicit builds and non-dotnet wrappers. The inspected `2ede815a` shell driver explicitly passes `-maxcpucount:"${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-1}"`, so that particular shell wrapper is not evidence of an uncapped build. Four six-node grants already permit 24 MSBuild nodes, alongside compiler threads, test hosts, agents and Docker.

Host access gap: `ssh -o BatchMode=yes -o ConnectTimeout=10 mc@server2 ...` failed **Host key verification failed**. No trust bypass was used. This runner's Docker socket is the **nested test Docker daemon**: `docker ps` and `docker stats --no-stream` list test PostgreSQL containers, not `antiphon-runner-session-runner-1` or `antiphon-runner-build-slots-1`. The later nested stats showed `amazing_nobel` 0.19% CPU / 63.13 MiB and `practical_hopper` 48.18% / 320.2 MiB; these are not outer runner stats. Exact host `docker stats`, broker image/environment, broker logs, and disk latency remain unavailable here.

**Resource verdict:** memory-floor and disk-space pressure are excluded for the measured busy snapshot/current samples. CPU pressure is supported by historical load and compiler evidence. CPU-bound versus I/O-bound operation at peak occupancy is **not conclusively distinguishable** from these measurements. No safe capacity N above four has been measured.

## 3. Lease history: reconstructed sample, not a complete broker census

The shared broker's metrics GET returned:

```text
HTTP/1.1 405 Method Not Allowed
Allow: DELETE
```

Current source maps GET metrics (`BuildSlotRoutes.cs:69`). `docs/testing-and-build.md:1173` documents this older-broker response and the independently pinned image. `scripts/c590-remote.sh:968` reads `BUILD_SLOTS_SHA12`; lines 998–1001 only initialize an absent pin before starting the broker. This explains how a runner rollout can preserve an older broker, but the actual live image/pin was not readable. Even the current metrics implementation retains **three hours**, at most 4096 lifecycle records (`BuildSlotMetricsRecorder.cs:14`), not a durable twelve-hour per-lease log.

The sample combines normalized ToolResult events, task-owned wrapper logs, timestamped checkpoint executor logs, and extracted executor logs from retained archives. Lease UUID is the deduplication key. Assistant summaries and inherited Markdown copies were excluded from statistics. A dated event must fall inside the twelve-hour window. For undated wrapper logs, the known task dispatch must be within the window and the original log modification time must fall between dispatch and the window end; known archived batches were validated through their stored earlier task report. Repeated tail output counts once. Held seconds come from release lines or timestamp differences between matching grant/release lines. Wrapper seconds are rounded; timestamped differences have millisecond precision.

Missing terminal lines are **unknown**, not leaks or zero-duration holds. Missing labels remain explicitly unrecorded in the ledger. No statistical imputation was used. This is a convenience sample with survivorship and archive-availability bias; the results are **not fleet percentiles**, and top holders are lower-bound sampled totals. Summed waits are per-driver seconds and can overlap within one task; they must not be interpreted as that task's elapsed wall time.

| Sample | Wait n | Median wait | p95 wait | Hold n | Median hold | p95 hold |
|---|---:|---:|---:|---:|---:|---:|
| Twelve-hour reconstruction (384 distinct leases) | 383 | 0 s | 421 s | 378 | 99.749 s | 724.455 s |
| After 04:00 (213 leases) | 213 | 210 s | 511 s | 209 | 120 s | 804 s |

The after-04:00 subset uses exact grant timestamps where retained, otherwise the containing task's dispatch timestamp; it excludes undated leases from earlier-started tasks. Percentiles use nearest rank (ceil(0.95*n)); median averages the middle pair. Maximum observed wait: **645 s**; maximum observed hold: **4502.532 s**. 1 wait and 6 terminal-hold values are missing; 136 labels were not retained/reconstructed.

Top sampled holders by measured slot-seconds (all twelve hours):

| Task | Completed holds | Slot-seconds | Summed wait-seconds |
|---|---:|---:|---:|
| `23889c9b` | 55 | 8992.338 | 9321 |
| `e24018e3` | 53 | 4964 | 0 |
| `47d31366` | 6 | 4887.784 | 0 |
| `11a6d8bb` | 24 | 4305.227 | 1935 |
| `c3ba1792` | 9 | 4272.449 | 2746 |
| `a2cfcdc1` | 8 | 4046 | 1638 |
| `133255e3` | 11 | 3100.784 | 3061 |
| `ae6457ac` | 9 | 2967 | 0 |
| `e778bd07` | 13 | 2883 | 15 |
| `727a57dd` | 7 | 2565 | 2043 |
| `2ede815a` | 9 | 2412.520 | 1951 |
| `d19080aa` | 14 | 2288 | 0 |
| `2e0375f1` | 11 | 2253.157 | 3420 |
| `a4933b4f` | 14 | 2229 | 2401 |
| `0c0e9de1` | 9 | 2186.755 | 2295 |

## 4. Long holders, polling, and liveness

| Lease | Task / label | Wait | Hold | Grant → release UTC, if retained |
|---|---|---:|---:|---|
| `7990b9ea-f081-4f2f-b4fc-2f5d8059d5fc` | `47d31366` / CP-2@bin-review47d31366 | 0 s | 4502.532 s | 2026-11-05T01:31:50.2483458+00:00 → 2026-11-05T02:46:52.7801554+00:00 |
| `42f9af99-3231-48a6-a88c-623d14da4dfe` | `a2cfcdc1` / c1720-review-runA | 135 s | 2901 s | not retained → not retained |
| `29319c0c-5c47-477a-988b-b2281d69c9cc` | `c3ba1792` / CP-2@bin-c1720-final | 360 s | 2702.101 s | 2026-11-05T05:23:07.4483984+00:00 → 2026-11-05T06:08:09.5492323+00:00 |
| `f2bce421-1a5c-4a1a-9d6a-139f0695d85f` | `133255e3` / CP-3@bin-card1722 | 285 s | 2101.877 s | 2026-11-05T07:17:47.1956368+00:00 → 2026-11-05T07:52:49.0728656+00:00 |
| `419817ec-fb91-4945-9362-9210a8f720be` | `e778bd07` / unknown (no label retained) | 0 s | 1351 s | not retained → not retained |
| `a32de711-dc61-4991-aed2-4ed5bce3ff13` | `e24018e3` / unknown (no label retained) | 0 s | 1244 s | not retained → not retained |
| `ddea69ae-0419-4d4e-bc3b-7042d0b8d7f1` | `11a6d8bb` / CP-54@bin-review11a6d8bb | 345 s | 1201.266 s | 2026-11-05T04:57:38.5994414+00:00 → 2026-11-05T05:17:39.8652639+00:00 |
| `8b4fe9a4-8991-48de-8b65-709b5aaaf2b6` | `11a6d8bb` / CP-48@bin-review11a6d8bb | 0 s | 1201.254 s | 2026-11-05T04:15:59.7039193+00:00 → 2026-11-05T04:36:00.9570721+00:00 |

The 75-minute whole-Unit lease is before the reported 04:00 congestion, so it proves the admission mechanism and long service times, not the cause of the later specific queue. During the later incident, Review `11a6d8bb` held Checkpoints CP-48 from **04:15:59.704 to 04:36:00.957** and Scripts CP-54 from **04:57:38.599 to 05:17:39.865**, approximately 1201 seconds each. Its archived source receipts show both timed out at their original 20-minute caps with no TRX. Archive: `/tmp/11a6d8bb-evidence/checkpoint-raw.tar.gz`; executor member is identified in the evidence catalog.

The 48-minute `c1720-review-runA` lease ran the Checkpoints namespace, not the whole Unit suite. Normalized session `99fce261-fb7b-4ec8-b78d-86c1d6a3d216`, sequence **178**, records the actual `dotnet run --no-build` command; sequence **210** records 816 total, 778 succeeded, 2 failed, 36 skipped, duration 48m14.912s, and lease release held=2901s. It is active test execution, not a polling-only command.

The polling distinction is explicit in code. `scripts/build-slot.ps1:123` waits for its child and releases in `finally` at line 167. That wrapper's own low CPU is expected while its test child works. `tools/Antiphon.Checkpoints/Execution/RunScheduler.cs:458` acquires one build lease and line 520 awaits the build inside that lease scope. Line 600 acquires a separate row lease and line 650 awaits row execution. `Commands/WaitCommand.cs:51` reads run state/report/log paths and polls; it is not the scheduler's build/row acquisition path. The ordinary checkpoint poller therefore does not, by itself, account for the active driver's lease.

The collected evidence does **not** prove a wrapper holding a slot solely to poll a finished detached run, nor a parent wrapper around the checkpoint orchestrator waiting for a child to obtain a second live-pool lease. The two multi-test repetition batches did retain leases across successive test startups/teardowns; their idle fraction is unmeasured.

PID-mode semantics: `BuildSlotBroker.cs:201` retains a past-TTL lease only if `SameHolderStillRunning` succeeds; line 247 restricts that exception to `HolderLiveness == "pid"` and matching start time within one second. Otherwise line 213 checks death/recycling or renewal silence. The timer sweeps every configured interval (`BuildSlotRoutes.cs:86`), and acquisition also sweeps (`BuildSlotBroker.cs:84`). Renew-mode `holderAlive` means a recent renewal (`BuildSlotBroker.cs:241`), not proof of OS PID liveness across containers. A live wrapper renewing while a child is idle remains possible in principle; it was not demonstrated here.

No sampled hold exceeds the 90-minute default. The 05:19 response had all four `holderAlive=true`; the current snapshots likewise show live holders. At 08:24:20 the one listed holder PID 12938 was a non-zombie `Sl` pwsh process in `ps`. That does not certify all past holders. Missing broker reap logs prevent a historical leak/reap census. Unmatched ledger grants are not sufficient leak evidence.

## 5. Task concurrency and budget outcomes

Read-only pipeline snapshot at **08:17:00.279**:

| Scope / setting | Observed effective value |
|---|---|
| `hosts[server2]` | inFlight **7**, effectiveLimit **10**, configured null, declared 10, source `runner`, remaining 3, fleet scope |
| Project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f` | mode `LegacyOpen`; maxParallel **12**, source `project`; parallel 5, queued 0, remaining 7 |
| Per-role combined parallel bounds | Code 5; Review 5; Debug 4; Investigate 3; Plan 3; other listed stage roles 1; Custom 12 |
| Project in-flight rows | Code 3; Merge 1; Investigate 1 |
| Runner seats | server2 capacity 10, occupied 7, available and dispatchEligible true |
| Top-level `maxConcurrentTasks=2`, `inFlightAgainstCap=0` | Local host only, not the server2 limit |

Fleet host totals include occupied seats outside the scoped active-task rows. They are not seven simultaneous builds. Stored task dispatch/completion intervals independently reconstruct **7** overlapping server2 tasks at 04:45, **8** at 05:15, **8** at 06:45, and **7** at 07:30. These are task lifecycle intervals, not proof each was continuously executing or holding a slot. The current settings snapshot is not proof of the historical cap value. Its source and revisions are retained.

The slot limit and seat limit are distinct, with no one-lease-per-task restriction. The full-pool incident shows why comparing only seven tasks to four slots understates demand: one task alone was using two slots, and the checkpoint scheduler can run independent rows concurrently.

Confirmed user-visible outcomes from stored task reports/transcripts:

| Task | Evidence of delay / incomplete work | Attribution limit |
|---|---|---|
| `c3ba1792` | CP-1 waited 585s; CP-2 then held about 2702s and timed out at 45 minutes; CP-3 interrupted, CP-4–11 unrun | Both admission and intrinsically long test execution matter |
| `27e2f0b5` | Archived executors reconstruct 12 leases, summed wait 3900s and hold 1790.992s | Some leases overlap; 3900s is not task wall time |
| `133255e3` | CP-1/2/3 waited 360/375/285s; CP-3 Unit held 2101.877s and timed out at 35 minutes without TRX | Named tests passed; Unit timeout is a separate execution problem |
| `71dacd6c` | Four sampled leases; summed waits 930s, holds 767.132s | Sample total, not a universal test-latency estimate |
| `c781f7f8` | Green attempt acquired after 240s but exited 5 before tests; 60-minute allocation exhausted, green verification pending | The lease was granted; exit 5 is not a slot-admission timeout |
| `a4933b4f` | 14 sampled leases; 2401s summed wait, 2229s held; initial build waited 586s; G10 admission timed out at 8 minutes with zero tests; 1 killed, 3 survived, 21 unrun | Confirms lost verification budget; does not make the 21 unrun mutants survivors |

Normalized final outcome references: `c3ba1792` session `25182523-844b-4b2d-a2d5-71e6b879d22b` sequence 68; `133255e3` session `97750719-0398-42cd-a087-b0d3be9a8469` sequence 54; `a4933b4f` session `4adfad8b-df74-44fb-b83a-bf8d2dc87aa3` sequence 43. The complete task/session mapping and per-lease provenance follow below. Stored result bodies were also retrieved through GET `/api/agent-tasks/{id}`.

The checkpoint clock already distinguishes admission-only time from execution (`Execution/RunBudgetClock.cs:75`); row deadlines begin after admission (`Execution/RunScheduler.cs:634`). That does not make broker waiting free against the delegate's externally allotted wall budget. It also means a 35/45/75-minute **execution** timeout cannot simply be explained away as the preceding slot wait.

## 6. Remaining uncertainties and operator decision boundary

| Unresolved question | Evidence needed to resolve it |
|---|---|
| Complete twelve-hour lease distribution, top holders, reap history | Read-only host export of broker stdout/rolling log for 20:16–08:16, including grant/release/reap lines; join task/session IDs and deduplicate UUIDs |
| Exact deployed broker version and liveness settings | Allowlisted outer `docker inspect` fields: image identity/start time and only `SessionRunner__BuildSlots__*` values; inspect the single non-secret `BUILD_SLOTS_SHA12` value through its owner path, without dumping the stack env |
| CPU versus I/O bottleneck at four occupied slots | Time-aligned outer-container CPU/memory/block-I/O stats and host CPU/iowait/disk-latency samples during actual full-pool queueing |
| Safe capacity greater than four | Measured concurrent workload peak resource/latency behavior; free memory and a 24-CPU census alone do not establish safety |
| Idle-child or renewed-wrapper leak | Match suspect lease IDs and renewal/reap history to holder start identity, descendant lifetime and progress samples; no such suspect was confirmed here |
| Complete queue fairness / dropped waiter history | Broker grant, waiter-drop, release timestamps and caller poll timelines; the current sample establishes queue delay, not infinite starvation |
| Why the Checkpoints/Scripts/Unit selections run so long | Existing exact-row performance evidence; this contention investigation does not diagnose all test execution failures |

Known control owners, for a separately commissioned decision: build capacity/CPU/floor/liveness are `SessionRunner:BuildSlots` and `docker-compose.server2-runner.yml:176`; host dispatch budget is GET/PUT `/api/hosts/server2/budget`, **operator-only** (`docs/orchestration-loop.md:566`); project/stage effective admission policy is the pipeline `concurrencyScopes` snapshot. No value was written. Workload throughput versus latency, allocation of the host budget, and permission for any controlled capacity experiment remain operator decisions. This report does not rank unmeasured designs or invent a safe N.

Not done, noted: capacity/concurrency changes, separate compile/test pools, shorter lease scopes, leak remediation and wait attention belong in Decide/Plan; no fix was designed or implemented.

## 7. Evidence retention and repeatable reads

Generated evidence is outside Git at `/work/evidence/investigation-a8d643be/`; the initial collection also remains under this task's ignored `.antiphon/investigation-a8d643be/`. The committed ledger below preserves the essential values and original file:line or transcript-sequence provenance if another task later retires its raw files. Original archives and other tasks' source/output files were read only, never extracted over or cleaned. Collection used inherited local processes only.

Read-only reruns from a runner shell:

```sh
curl -fsS "$ANTIPHON_BUILD_SLOTS_URL"
curl -i "$ANTIPHON_BUILD_SLOTS_URL/metrics"
nproc
free -m
uptime
df -h /work /state /
vmstat 2 6
```

GET `/api/agent-tasks/pipeline?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f` with the inherited task-token header refreshes effective settings; do not print the token. Host-owned broker logs/stats require an already-trusted host access lane. No POST/DELETE lease operation, settings change, restart, reaper invocation, or synthetic leased workload is part of these reads.

## Appendix A. Reconstructed per-lease ledger

All 384 deduplicated sampled lease IDs are below. Durations are seconds. A missing release/hold is unknown, not a leak finding. Times without a retained timestamp are not inferred from transcript delivery time. Source references map to Appendix B; file line numbers refer to original or retained extracted logs. For session references, the # suffix is the normalized transcript sequence.

| Lease UUID | Task | Label | Wait s | Held s | Grant UTC | Evidence |
|---|---|---|---:|---:|---|---|
| `2766cc63-e578-413d-823d-6794218977f0` | `0c0e9de1` | build:bin-c1718-nested-red-runner | 645 | 40.669 | 2026-11-05T06:54:45.0328776+00:00 | E320:57, E320:58 |
| `549dc17c-4609-4b71-afe2-d8ede2eb8d3c` | `0c0e9de1` | CP-1@bin-c1718-nested-red-runner | 150 | 5.790 | 2026-11-05T06:58:29.9971021+00:00 | E320:72, E320:73 |
| `731c635f-7d88-4e10-ae80-f2d8f08cbe75` | `0c0e9de1` | build:bin-c1718-nested-red-server | 405 | 434.340 | 2026-11-05T07:05:30.7932924+00:00 | E320:108, E320:109 |
| `e48da8ad-40b0-490a-9c4a-3d2530be2ef0` | `0c0e9de1` | CP-2@bin-c1718-nested-red-server | 330 | 26.602 | 2026-11-05T07:18:33.1639342+00:00 | E320:138, E320:139 |
| `b0f32817-f289-4e5a-86c9-9045177f9d75` | `0c0e9de1` | build:bin-c1718-nested-runner | 225 | 181.551 | 2026-11-05T07:25:43.8218627+00:00 | E321:22, E321:30 |
| `c2946de0-66bf-43dd-9f0c-d2dac5fc0c97` | `0c0e9de1` | build:bin-c1718-nested-runner | 45 | 147.171 | 2026-11-05T07:33:28.5700092+00:00 | E322:7, E322:8 |
| `e08e3867-174b-41e1-a0f7-e0045ba0be14` | `0c0e9de1` | CP-3@bin-c1718-nested-runner | 135 | 86.763 | 2026-11-05T07:38:53.5295979+00:00 | E322:21, E322:22 |
| `810b42f2-38c8-4ebe-8519-b973d3825a87` | `0c0e9de1` | build:bin-c1718-nested-server | 150 | 903.809 | 2026-11-05T07:43:07.0960408+00:00 | E322:36, E322:37 |
| `9e1c90b9-c348-4a97-af2f-36e0233b206d` | `0c0e9de1` | CP-4@bin-c1718-nested-server | 90 | 360.060 | 2026-11-05T08:00:54.1506236+00:00 | E322:46, E322:47 |
| `abbb1f90-ffe5-46cf-bcc4-28429b9a77fb` | `0c0e9de1` | build:bin-c1718-nested-server | 120 | unknown | 2026-11-05T08:10:14.7752576+00:00 | E323:13 |
| `11a6cb84-503a-4dcb-8280-145c52fb7f8e` | `0cd23a4c` | merge74-base-debounce-build | 135 | 710 | not retained | E327:4, E327:3501 |
| `7ddac943-2b95-4608-8fec-b09f2e162bd0` | `0cd23a4c` | unknown (no label retained) | 0 | 43 | not retained | E328:1, E328:26 |
| `993f173b-8876-4af2-b129-56cd416ee2de` | `0cd23a4c` | merge74-solution | 195 | 584 | not retained | E332:5, E332:3880 |
| `c78510af-0a6f-4dee-abdf-92a1e84b27c1` | `0cd23a4c` | merge74-base-evaluator | 405 | 17 | not retained | E329:8, E329:21 |
| `f2f89a94-5789-45d0-b9c3-84753bd1f072` | `0cd23a4c` | merge74-final-tests | 466 | 377 | not retained | E330:9, E330:55 |
| `fb465a14-79fa-48d1-9018-d1f6ba1c0702` | `0cd23a4c` | merge74-messaging-migration | 511 | 32 | not retained | E331:10, E331:41 |
| `1f2c37f9-f86a-4ea8-a56b-476f7dbfe367` | `11a6d8bb` | build:bin-review11a6d8bb | 0 | 424.021 | 2026-11-05T03:43:44.7854754+00:00 | E381:3, E381:4 |
| `07a6a090-b241-42ca-88be-855b039cda31` | `11a6d8bb` | CP-23@bin-review11a6d8bb | 15 | 122.407 | 2026-11-05T03:51:17.9598769+00:00 | E381:7, E381:8 |
| `afc44586-0035-4d22-9501-250927df6ce5` | `11a6d8bb` | CP-24@bin-review11a6d8bb | 45 | 36.014 | 2026-11-05T03:54:05.8287539+00:00 | E381:13, E381:14 |
| `e01b776a-2ff7-49d6-b26f-5fa7373b93ad` | `11a6d8bb` | CP-26@bin-review11a6d8bb | 45 | 34.108 | 2026-11-05T03:55:26.9738600+00:00 | E381:19, E381:20 |
| `a8ea1b27-9415-48fd-b6d5-95e7b472460c` | `11a6d8bb` | CP-27@bin-review11a6d8bb | 105 | 107.322 | 2026-11-05T03:57:46.3096426+00:00 | E381:30, E381:31 |
| `b65fc5a2-cf5a-438e-9b33-ae44db3cac92` | `11a6d8bb` | CP-28@bin-review11a6d8bb | 75 | 24.504 | 2026-11-05T04:00:48.8543932+00:00 | E381:39, E381:41 |
| `b85c33b9-bfc8-47d8-bf85-b12238b00bfa` | `11a6d8bb` | CP-30@bin-review11a6d8bb | 60 | 102.184 | 2026-11-05T04:02:13.6037842+00:00 | E381:47, E381:48 |
| `2be2436e-68cd-442a-8b5a-9cc044405264` | `11a6d8bb` | CP-31@bin-review11a6d8bb | 0 | 28.077 | 2026-11-05T04:03:55.9251041+00:00 | E381:49, E381:50 |
| `1b398e9e-0063-4ec7-bd6d-5a8d85e0477f` | `11a6d8bb` | CP-40@bin-review11a6d8bb | 0 | 21.813 | 2026-11-05T04:04:24.2490513+00:00 | E381:51, E381:52 |
| `357f6ab4-afff-4a15-8cb7-c2c04c7a1cb0` | `11a6d8bb` | CP-41@bin-review11a6d8bb | 0 | 27.388 | 2026-11-05T04:04:46.2622842+00:00 | E381:53, E381:54 |
| `db6665ff-ada1-4ca2-bdb0-4151af4fa6c4` | `11a6d8bb` | CP-42@bin-review11a6d8bb | 0 | 21.460 | 2026-11-05T04:05:13.8006773+00:00 | E381:55, E381:56 |
| `3b516874-7ce2-4603-9a01-a6fe5898bcf6` | `11a6d8bb` | CP-43@bin-review11a6d8bb | 0 | 11.074 | 2026-11-05T04:05:35.4699994+00:00 | E381:57, E381:58 |
| `413c81c8-95be-4d21-b8c4-cc9410dd9f38` | `11a6d8bb` | CP-44@bin-review11a6d8bb | 0 | 105.879 | 2026-11-05T04:05:46.7506675+00:00 | E381:59, E381:60 |
| `ad776829-b8a8-470e-8fbb-69a56f41f480` | `11a6d8bb` | CP-45@bin-review11a6d8bb | 0 | 197.001 | 2026-11-05T04:07:32.7715817+00:00 | E381:61, E381:62 |
| `04949506-5dc2-42b6-b4cf-22c8105cc38b` | `11a6d8bb` | CP-46@bin-review11a6d8bb | 0 | 122.320 | 2026-11-05T04:10:50.0744783+00:00 | E381:63, E381:64 |
| `a7e3f928-23cd-4811-a5b6-4d7841a5777d` | `11a6d8bb` | CP-47@bin-review11a6d8bb | 0 | 186.609 | 2026-11-05T04:12:52.9478996+00:00 | E381:65, E381:66 |
| `8b4fe9a4-8991-48de-8b65-709b5aaaf2b6` | `11a6d8bb` | CP-48@bin-review11a6d8bb | 0 | 1201.254 | 2026-11-05T04:15:59.7039193+00:00 | E381:67, E381:68 |
| `f6b0a7d7-da64-4c90-be35-e1c589071270` | `11a6d8bb` | CP-49@bin-review11a6d8bb | 420 | 6.320 | 2026-11-05T04:43:01.4532489+00:00 | E381:104, E381:105 |
| `0f1f06db-fa58-49d2-8938-e1934894dc7d` | `11a6d8bb` | CP-50@bin-review11a6d8bb | 105 | 4.814 | 2026-11-05T04:44:52.9977749+00:00 | E381:115, E381:116 |
| `5528b227-88c5-40ba-b8d2-ff26295308bc` | `11a6d8bb` | CP-51@bin-review11a6d8bb | 15 | 61.238 | 2026-11-05T04:45:13.0633667+00:00 | E381:119, E381:120 |
| `7bce5440-5709-4be9-9076-2113419e4bc1` | `11a6d8bb` | CP-52@bin-review11a6d8bb | 105 | 40.767 | 2026-11-05T04:47:59.5947172+00:00 | E381:130, E381:131 |
| `23f9d56c-e6a5-40c8-ab4d-d26fced3d8f7` | `11a6d8bb` | CP-53@bin-review11a6d8bb | 165 | 25.388 | 2026-11-05T04:51:25.9563587+00:00 | E381:146, E381:147 |
| `ddea69ae-0419-4d4e-bc3b-7042d0b8d7f1` | `11a6d8bb` | CP-54@bin-review11a6d8bb | 345 | 1201.266 | 2026-11-05T04:57:38.5994414+00:00 | E381:177, E381:178 |
| `ef779a06-bfb4-4834-9553-07b2a9b2c6bb` | `11a6d8bb` | CP-55@bin-review11a6d8bb | 435 | 191.999 | 2026-11-05T05:24:55.1383797+00:00 | E381:215, E381:216 |
| `340ac268-1a26-4671-86e5-ff88d602084b` | `122f2dcf` | unknown (no label retained) | 0 | 162 | not retained | E173:1, E173:52 |
| `378e9e21-6637-47db-adc7-a5d3063d5fba` | `122f2dcf` | unknown (no label retained) | 0 | 161 | not retained | E168:1, E168:52 |
| `5ecfaafd-5906-45e5-be43-3edb5b3ea6d0` | `122f2dcf` | unknown (no label retained) | 0 | 210 | not retained | E175:1, E175:3866, E176 |
| `63a61d8e-b976-4dd3-8170-48dd7e76e473` | `122f2dcf` | unknown (no label retained) | 0 | unknown | not retained | E386 |
| `647aca3b-037c-472d-bd52-d96af0aeebff` | `122f2dcf` | unknown (no label retained) | 0 | 114 | not retained | E169:1, E169:28 |
| `75e3d5a6-87e5-4b5b-94e2-4c214ae0a5c7` | `122f2dcf` | unknown (no label retained) | 0 | 4 | not retained | E385 |
| `a4c03e11-af6b-4df8-acda-396f1bb4bd59` | `122f2dcf` | unknown (no label retained) | 0 | 180 | not retained | E170:1, E170:28 |
| `ae981605-712d-4c9f-a495-3631d146214d` | `122f2dcf` | unknown (no label retained) | 0 | 166 | not retained | E167:1, E167:26 |
| `afb3dd2d-a9b8-4c89-99b4-31d1ec89ab90` | `122f2dcf` | unknown (no label retained) | unknown | 200 | not retained | E387 |
| `bee3f25f-a0b5-4328-934a-41f0517f24c6` | `122f2dcf` | unknown (no label retained) | 0 | 127 | not retained | E174:1, E174:28 |
| `cb094d43-67ed-4740-8298-a5c44063344c` | `122f2dcf` | unknown (no label retained) | 0 | 303 | not retained | E171:1, E171:26 |
| `f4089007-75d9-400f-9ba8-7c7c334c70a1` | `122f2dcf` | unknown (no label retained) | 0 | 193 | not retained | E172:1, E172:26 |
| `5ed4c3b5-2a9e-4c92-b45a-5c6621090de4` | `133255e3` | card1722-checkpoint-tool | 90 | 7 | not retained | E339:3, E339:35 |
| `a0c88891-0eeb-468e-9a82-ab306b8d9d78` | `133255e3` | card1722-receipt-red | 196 | 7 | not retained | E338:5, E338:21 |
| `de229916-f910-442b-a9ab-73e5a2e872d2` | `133255e3` | card1722-receipt-green | 375 | 7 | not retained | E337:8, E337:13 |
| `f0468c7a-2b27-4837-85f5-80ed496454bd` | `133255e3` | card1722-starvation-probe-build | 135 | 31 | not retained | E336:4, E336:14 |
| `5507a724-b03c-47eb-bc9a-8f06c248950f` | `133255e3` | build:bin-card1722 | 360 | 297.418 | 2026-11-05T06:35:15.3863897+00:00 | E295:33, E295:34, E296:33, E296:34 |
| `cafdcd16-f10c-4995-8c90-5664c8aa4a5b` | `133255e3` | CP-1@bin-card1722 | 195 | 47.878 | 2026-11-05T06:43:48.6450349+00:00 | E295:52, E295:53, E296:52, E296:53 |
| `f4a5659e-bbdf-43f2-9c1d-1cfad585a2d8` | `133255e3` | build:bin-card1722 | 555 | 0.337 | 2026-11-05T06:55:17.8576180+00:00 | E297:50, E297:51, E298:50, E298:51 |
| `4f065344-5040-4b6a-8957-34b0ef834eea` | `133255e3` | build:bin-card1722 | 135 | 393.913 | 2026-11-05T06:58:43.6456600+00:00 | E299:15, E299:16, E300:15, E300:16 |
| `17eff509-438d-4c04-b598-aecc6d927317` | `133255e3` | CP-1@bin-card1722 | 360 | 37.236 | 2026-11-05T07:12:24.7137231+00:00 | E299:79, E299:81, E300:79, E300:81 |
| `0141d6ff-992f-4e14-8a07-260370cb1d1d` | `133255e3` | CP-2@bin-card1722 | 375 | 170.125 | 2026-11-05T07:12:39.7123336+00:00 | E299:80, E299:95, E300:80, E300:95 |
| `f2bce421-1a5c-4a1a-9d6a-139f0695d85f` | `133255e3` | CP-3@bin-card1722 | 285 | 2101.877 | 2026-11-05T07:17:47.1956368+00:00 | E299:107, E299:128, E300:107, E300:128 |
| `256b5d7f-1391-479c-b3d0-1475a9f21bc7` | `1c4f8dd2` | c1716-classes-run | 0 | 554 | not retained | E400, E401 |
| `25ee55d5-85e4-4cf9-aa53-78bb9413b5dc` | `1c4f8dd2` | c1716-red-build | 0 | 135 | not retained | E388 |
| `4c1b51d8-da34-40e3-b139-5e5b3821351c` | `1c4f8dd2` | unknown (no label retained) | 0 | 68 | not retained | E396 |
| `4f2bf6d0-20df-46eb-8651-23ccce8950c4` | `1c4f8dd2` | unknown (no label retained) | 0 | 59 | not retained | E393 |
| `53637146-3a1c-4e13-baa2-5b392cee81b4` | `1c4f8dd2` | unknown (no label retained) | 0 | 45 | not retained | E398 |
| `75d78b69-b19a-4c58-a402-0433732fc9cb` | `1c4f8dd2` | unknown (no label retained) | 0 | 35 | not retained | E397 |
| `76609860-2ea1-44bd-a2b3-0a2deba34fb8` | `1c4f8dd2` | c1716-fix-run | 0 | 32 | not retained | E392 |
| `79820356-f198-4072-84fe-b3124cfaaf23` | `1c4f8dd2` | c1716-tip-build | 0 | 131 | not retained | E399 |
| `80404aa0-fc2a-44e3-9342-8981aecc3a47` | `1c4f8dd2` | unknown (no label retained) | 0 | 30 | not retained | E393 |
| `910920c1-21b8-429b-85ef-e33d8bb35417` | `1c4f8dd2` | c1716-red-build | 0 | 139 | not retained | E389 |
| `a66f274f-14ac-4362-91d8-970679c77158` | `1c4f8dd2` | unknown (no label retained) | 0 | 49 | not retained | E394 |
| `b830aa7f-86f3-424c-a4da-df30ac2d22e0` | `1c4f8dd2` | unknown (no label retained) | 0 | 32 | not retained | E394 |
| `c108b1f1-3e9f-47b2-b7e1-4af49bd182d2` | `1c4f8dd2` | c1716-red-run | 0 | 57 | not retained | E390 |
| `d924cddc-db61-4f73-81e3-74330754efe5` | `1c4f8dd2` | unknown (no label retained) | 0 | 89 | not retained | E396 |
| `de78eedf-33db-424f-ab4c-1de435a3bbc8` | `1c4f8dd2` | unknown (no label retained) | 0 | 35 | not retained | E398 |
| `e1109de4-4469-4408-a07f-efc91d308f57` | `1c4f8dd2` | c1716-fix-build | 0 | 125 | not retained | E391 |
| `f838d1fe-22ea-4006-86aa-5504adb5f3c2` | `1c4f8dd2` | unknown (no label retained) | 0 | 93 | not retained | E395 |
| `fc0e0658-3b2d-49f6-9c33-39c02ba1f596` | `1c4f8dd2` | unknown (no label retained) | 0 | 35 | not retained | E395 |
| `ffc30e9a-3e88-4508-8ee8-b951dbdaf8cc` | `1c4f8dd2` | unknown (no label retained) | 0 | 54 | not retained | E397 |
| `02a759fd-9667-4867-8471-056b44e5aabd` | `23889c9b` | c1717-r2-compile4 | 420 | 170 | not retained | E206:8, E206:3464, E207 |
| `099a1f97-4321-4ed9-863b-a5362a673964` | `23889c9b` | c1717-r2-red467-build2 | 361 | 121 | not retained | E246:7, E246:3371, E247 |
| `10135daa-df2e-4e84-8b48-60bd761f9d6e` | `23889c9b` | c1717-r2-devrun4 | 240 | 14 | not retained | E216:5, E216:29, E217 |
| `163e2dfe-61e0-4e0e-b043-2c745f027554` | `23889c9b` | c1717-r2-red467-witness3 | 510 | 32 | not retained | E252:10, E252:273, E253, E254 |
| `24fd272b-bff6-4ba0-89a6-11b913ec0245` | `23889c9b` | c1717-r2-mut-control-build | 120 | 533 | not retained | E239:3, E239:3488, E240 |
| `28852e5d-a34d-458f-b1c3-77ad5b46ad73` | `23889c9b` | c1717-r2-mut-B-run | 135 | 17 | not retained | E232:4, E232:104 |
| `2a50b84e-592e-46e0-8231-4fe6a04443fc` | `23889c9b` | c1717-r2-mut-control-run | 300 | 34 | not retained | E241:6, E241:30, E240 |
| `2ecc03b2-c6b4-4a7f-85ff-a5f6f4310aa5` | `23889c9b` | c1717-r2-m15-run | 165 | 26 | not retained | E221:4, E221:78, E222 |
| `2ef29ec8-5949-40f2-90c0-6b9eba2d7a4f` | `23889c9b` | c1717-r2-red467-witness | 0 | 30 | not retained | E250:1, E250:267, E251 |
| `2fcc6bff-5dec-4235-a51c-5c0dfebcc0d5` | `23889c9b` | c1717-r2-mut-D-build | 60 | 203 | not retained | E235:2, E235:3412 |
| `35446661-473b-4dc8-9a78-28d3b32663b7` | `23889c9b` | c1717-r2-mut-E-build | 136 | 54 | not retained | E237:4, E237:140 |
| `38403fcb-9ddb-4b38-bd84-b6691dfdef46` | `23889c9b` | c1717-r2-mut-control2-run | 511 | 48 | not retained | E243:10, E243:79 |
| `402a536c-0928-429c-ba25-d85dbb66902c` | `23889c9b` | unknown (no label retained) | 0 | 286 | not retained | E200:1, E200:3411, E201 |
| `41020f05-a1ac-4726-9cbe-4a2ddd1fe871` | `23889c9b` | c1717-r2-red467-c1017 | 0 | 26 | not retained | E248:1, E248:87, E249 |
| `576f4d36-5a1f-466c-9085-480a85aa3fe5` | `23889c9b` | c1717-r2-mut-A-build | 135 | 364 | not retained | E229:4, E229:3416 |
| `5ae1ee70-a0b8-4907-aa4b-2fb6df45ec5f` | `23889c9b` | c1717-r2-mut-C-build | 120 | 132 | not retained | E233:3, E233:139 |
| `6ea22a80-3c17-4af0-9a36-d6d4bd3e6de8` | `23889c9b` | c1717-r2-compile5 | 165 | 288 | not retained | E208:4, E208:3460, E209 |
| `71f54763-8bb9-4e8f-af14-1ba866ec667d` | `23889c9b` | c1717-r2-build-head | 15 | 406 | not retained | E198:2, E198:3487, E199 |
| `8a331f3a-58dc-447e-9a73-ec1171b629b3` | `23889c9b` | unknown (no label retained) | 0 | 120 | not retained | E212:1, E212:56, E213 |
| `8b3e0031-6bd6-4fa0-9346-1029e2e9e2ff` | `23889c9b` | c1717-r2-mut-A-run | 180 | 44 | not retained | E230:4, E230:180 |
| `93bf4a1d-dc35-4c78-8528-0a99ca649dc2` | `23889c9b` | c1717-r2-devrun5 | 301 | 12 | not retained | E218:6, E218:30, E219 |
| `9680ad47-8357-4401-90c6-b7468f8b36ac` | `23889c9b` | c1717-r2-mut-B-build | 210 | 597 | not retained | E231:5, E231:3415 |
| `a7109627-6d02-4412-80c2-6b38f6556d1f` | `23889c9b` | c1717-r2-mut-control2-build | 166 | 188 | not retained | E242:4, E242:3460 |
| `a7ee90f2-722b-4e79-9928-25df5a3f3df6` | `23889c9b` | unknown (no label retained) | 0 | 162 | not retained | E244:1, E244:3486, E245 |
| `a8b71149-ad7a-4335-9d76-891c4cf5b98d` | `23889c9b` | c1717-r2-mut-C-run | 135 | 18 | not retained | E234:4, E234:129 |
| `acbc3e1f-ef11-4044-a207-8e3153c878d9` | `23889c9b` | c1717-r2-compile2 | 0 | unknown | not retained | E202:1, E203 |
| `d3d7e938-f82c-418a-8650-d49d3dffdc12` | `23889c9b` | c1717-r2-mut-E-run | 435 | 43 | not retained | E238:9, E238:114 |
| `d3e9b11c-087d-4ffc-8c2d-b0b1245b5ca1` | `23889c9b` | c1717-r2-devrun3 | 300 | 83 | not retained | E214:6, E214:30, E215 |
| `dc5812c9-a32a-4495-8522-0db88fcb7396` | `23889c9b` | unknown (no label retained) | 0 | 399 | not retained | E220:1, E220:3457 |
| `e6022f14-a711-4b4b-88c1-523fc60cb934` | `23889c9b` | c1717-r2-compile3 | 90 | 88 | not retained | E204:3, E204:91, E205 |
| `eae68c67-6cde-4f9b-a03a-692ca3b6fa34` | `23889c9b` | unknown (no label retained) | 0 | 31 | not retained | E210:1, E210:25, E211 |
| `ef6eba75-c1b0-4082-85e5-6279b836c22d` | `23889c9b` | c1717-r2-mut-D-run | 451 | 22 | not retained | E236:9, E236:66 |
| `bca02ee2-19fe-4080-8287-f2ff261396d4` | `23889c9b` | build:bin-c1717-r2 | 345 | 303.937 | 2026-11-05T05:06:25.4258009+00:00 | E223:32, E223:33 |
| `20c52dd6-7e89-4add-895b-5d5ad90ad822` | `23889c9b` | build:bin-c1717-r2 | 90 | 732.261 | 2026-11-05T05:37:21.1402380+00:00 | E224:11, E224:12 |
| `a5fa9931-1483-4040-81a5-df60ac138d17` | `23889c9b` | CP-23@bin-c1717-r2 | 210 | 127.220 | 2026-11-05T05:53:36.5094602+00:00 | E224:31, E224:32 |
| `d1ba4372-fd2d-4dc3-96cc-91c7298ed911` | `23889c9b` | CP-24@bin-c1717-r2 | 315 | 38.836 | 2026-11-05T06:00:59.5251235+00:00 | E224:60, E224:61 |
| `ecfd343b-c214-47b2-afd0-1743173eb5cc` | `23889c9b` | CP-26@bin-c1717-r2 | 180 | 156.373 | 2026-11-05T06:04:38.6064659+00:00 | E224:77, E224:78 |
| `10df89d1-1e01-4b56-9a00-1343a330ac2e` | `23889c9b` | CP-27@bin-c1717-r2 | 285 | 147.132 | 2026-11-05T06:12:00.1908502+00:00 | E224:103, E224:104 |
| `86bebc23-1bfc-42b1-abe9-5f5e324f1eb7` | `23889c9b` | CP-28@bin-c1717-r2 | 225 | 21.961 | 2026-11-05T06:18:12.6143964+00:00 | E224:124, E224:125 |
| `af259635-40b6-4b99-87da-46de4453e45e` | `23889c9b` | CP-30@bin-c1717-r2 | 135 | 87.712 | 2026-11-05T06:20:50.3383807+00:00 | E224:138, E224:139 |
| `d0d0aedc-d123-4e8f-b846-55ecddb764e9` | `23889c9b` | CP-31@bin-c1717-r2 | 30 | 244.459 | 2026-11-05T06:22:48.1807229+00:00 | E224:143, E224:144 |
| `5507e213-01e9-4835-9fde-f6641da673a5` | `23889c9b` | build:bin-c1717-r2 | 240 | 324.956 | 2026-11-05T06:47:00.3084817+00:00 | E225:23, E225:24 |
| `b0bcc747-7c11-47a1-8acf-ffbae89ffeba` | `23889c9b` | CP-23@bin-c1717-r2 | 195 | 137.720 | 2026-11-05T06:56:09.7177855+00:00 | E225:42, E225:43 |
| `af7600cb-c419-45e7-8970-754b59611e85` | `23889c9b` | CP-24@bin-c1717-r2 | 375 | 40.761 | 2026-11-05T07:04:42.6943056+00:00 | E225:76, E225:77 |
| `b3383222-5d90-4c09-a7e5-3382dd2cf56b` | `23889c9b` | CP-26@bin-c1717-r2 | 255 | 180.278 | 2026-11-05T07:09:39.1630874+00:00 | E225:100, E225:101 |
| `05439fda-4ea3-4583-9734-5973a4a0d79c` | `23889c9b` | CP-27@bin-c1717-r2 | 210 | 139.003 | 2026-11-05T07:16:09.5835769+00:00 | E225:120, E225:121 |
| `c6286128-7fc4-4a72-9123-63fa40c547ce` | `23889c9b` | CP-28@bin-c1717-r2 | 45 | 11.706 | 2026-11-05T07:19:13.7564227+00:00 | E225:126, E225:127 |
| `fa7180a6-9bb8-45a2-b5b4-db6c46730dac` | `23889c9b` | CP-30@bin-c1717-r2 | 75 | 67.856 | 2026-11-05T07:20:40.6704574+00:00 | E225:135, E225:136 |
| `eda1b6a0-8f6e-43cb-8ff6-9315c3acb7b8` | `23889c9b` | CP-31@bin-c1717-r2 | 75 | 158.705 | 2026-11-05T07:23:04.1382502+00:00 | E225:144, E225:145, E226 |
| `5d66955f-b8e6-4051-9d78-1413c4abe588` | `23889c9b` | build:bin-c1717-r2 | 225 | 896.107 | 2026-11-05T07:45:48.8410454+00:00 | E227:22, E227:23, E228 |
| `c26037ed-c51e-43aa-b9d0-67ebbb6e4ea7` | `23889c9b` | CP-23@bin-c1717-r2 | 135 | 114.601 | 2026-11-05T08:04:52.3804576+00:00 | E227:36, E227:37 |
| `4c53b464-2287-42f6-a7ab-21c79c972750` | `23889c9b` | CP-24@bin-c1717-r2 | 15 | 37.015 | 2026-11-05T08:07:02.1116709+00:00 | E227:40, E227:41 |
| `64702f80-b9ca-4b40-b4bb-d9c5d70605d4` | `23889c9b` | CP-26@bin-c1717-r2 | 0 | 184.044 | 2026-11-05T08:07:39.3372159+00:00 | E227:42, E227:43 |
| `14a0b28a-0c72-4f67-92c6-3473439fff2a` | `23889c9b` | CP-27@bin-c1717-r2 | 0 | 190.761 | 2026-11-05T08:10:43.5599257+00:00 | E227:44, E227:45 |
| `e3da3c12-19a2-4022-bb90-6bd0e866e41a` | `23889c9b` | CP-28@bin-c1717-r2 | 0 | 10.213 | 2026-11-05T08:13:54.4670338+00:00 | E227:46, E227:47 |
| `3560eb59-6cae-4c1c-b966-27bbe1484c2b` | `23889c9b` | CP-30@bin-c1717-r2 | 0 | 47.721 | 2026-11-05T08:14:04.7668566+00:00 | E227:48, E227:49 |
| `70a294d1-f533-4515-bd55-b623d1e4e20c` | `23889c9b` | CP-31@bin-c1717-r2 | 0 | unknown | 2026-11-05T08:14:52.6098594+00:00 | E227:50 |
| `a9c77c74-cd0a-4fe7-bf04-cbb6f8b4fd6e` | `27e2f0b5` | build:bin-c1718-r2-red-runner | 30 | 242.568 | 2026-11-05T04:46:27.0582108+00:00 | E377:6, E377:7 |
| `b758e05d-6712-4884-a969-d1032b3d3c8a` | `27e2f0b5` | build:bin-c1718-appsettings-red | 120 | 194.681 | 2026-11-05T04:50:31.6228836+00:00 | E378:13, E378:14 |
| `20ab2874-8d77-4df7-bb64-e95615d08650` | `27e2f0b5` | CP-1@bin-c1718-r2-red-runner | 150 | 10.064 | 2026-11-05T04:53:54.8447558+00:00 | E377:21, E377:22 |
| `478d1e02-3347-4f25-b4f2-6e8bdacf7074` | `27e2f0b5` | CP-3@bin-c1718-appsettings-red | 225 | 8.818 | 2026-11-05T04:58:13.7441879+00:00 | E378:34, E378:35 |
| `9379ec63-9ede-42ab-aef0-fa15ffeb3700` | `27e2f0b5` | build:bin-c1718-r2-red-server | 225 | 459.247 | 2026-11-05T04:58:32.3529352+00:00 | E377:42, E377:43 |
| `fbcdd046-5508-4e7d-9d46-abe3a6af4c45` | `27e2f0b5` | build:bin-c1718-r2-runner | 390 | 66.321 | 2026-11-05T05:06:14.2937965+00:00 | E379:36, E379:37, E380:36, E380:37 |
| `404f68b8-c336-406e-8479-cdd10641a31c` | `27e2f0b5` | CP-2@bin-c1718-r2-red-server | 645 | 28.887 | 2026-11-05T05:17:19.8789457+00:00 | E377:98, E377:99 |
| `52a8f737-8d46-41c0-afaa-1384b7c40bc0` | `27e2f0b5` | build:bin-c1718-r2-server | 600 | 271.969 | 2026-11-05T05:17:48.9464970+00:00 | E379:88, E379:89, E380:88, E380:89 |
| `3dcce975-f365-4244-85e4-744fea9d2fee` | `27e2f0b5` | build:bin-c1718-r2-runner | 495 | 36.546 | 2026-11-05T05:22:24.7769160+00:00 | E376:45, E376:46 |
| `609e9e94-d70e-4205-8030-a9754fabef18` | `27e2f0b5` | CP-2@bin-c1718-r2-server | 330 | 339.950 | 2026-11-05T05:28:08.1857139+00:00 | E379:118, E379:119, E380:118, E380:119 |
| `2cddc3eb-4b17-4fc1-8f86-3cb7a2ff21cb` | `27e2f0b5` | CP-1@bin-c1718-r2-runner | 405 | 120.901 | 2026-11-05T05:29:56.8251375+00:00 | E376:81, E376:82 |
| `9408538b-61f2-4b12-bc3c-b0e5f5dde952` | `27e2f0b5` | CP-3@bin-c1718-r2-runner | 285 | 11.040 | 2026-11-05T05:36:42.9650728+00:00 | E376:107, E376:108 |
| `018e7597-7160-4306-a668-b55510f6f842` | `2e0375f1` | c1591r2-mutant-MB2 | 285 | 191 | not retained | E290:6, E290:1842 |
| `0e2b3607-6c80-44ff-aea0-bf478c34bbab` | `2e0375f1` | c1591r2-mutant-MC | 570 | 307 | not retained | E291:11, E291:2253 |
| `380c99f3-fc45-417a-b545-b9c55c470223` | `2e0375f1` | c1591r2-mutant-MD | 420 | 244 | not retained | E292:8, E292:1778 |
| `4418263a-aa58-420d-b4f5-6808dc82cf90` | `2e0375f1` | c1591r2-mutant-MB1 | 225 | 298 | not retained | E289:5, E289:1756 |
| `95e2e5c3-0256-4dda-ad9d-c1d7eb52c691` | `2e0375f1` | c1591r2 | 255 | 29 | not retained | E293:6, E293:38 |
| `e44b1baa-174c-4db8-8e47-b1be5ceb3c14` | `2e0375f1` | c1591r2-mutant-MA | 165 | 303 | not retained | E287:4, E287:2007, E288 |
| `e6424bba-2417-40c3-9ddb-fd6ac3ef331a` | `2e0375f1` | build:bin-c1591-repair | 135 | 354.446 | 2026-11-05T06:22:00.5030691+00:00 | E286:15, E286:16 |
| `e51f9b07-95c8-4c4f-a18e-73d984f14fcf` | `2e0375f1` | CP-1@bin-c1591-repair | 225 | 68.881 | 2026-11-05T06:31:58.9243800+00:00 | E286:36, E286:37 |
| `5653862a-30fa-47ec-b251-871fd963c17f` | `2e0375f1` | CP-3@bin-c1591-repair | 405 | 206.687 | 2026-11-05T06:39:53.8237844+00:00 | E286:72, E286:73 |
| `c11708ca-a9a4-4453-8ff3-8974e11d1045` | `2e0375f1` | CP-4@bin-c1591-repair | 555 | 152.645 | 2026-11-05T06:52:35.7285963+00:00 | E286:121, E286:122 |
| `41dba6ab-b287-4f3d-b4b5-0be0fdbb7c54` | `2e0375f1` | CP-5@bin-c1591-repair | 180 | 98.498 | 2026-11-05T06:58:08.5519279+00:00 | E286:138, E286:139 |
| `517d081e-1108-48cc-a8e4-1bc6f0e5eefd` | `2ede815a` | code-2ede815a-ef-model-build | 391 | 58 | not retained | E375:8, E375:77 |
| `bac8e56c-691f-464d-b63c-fc66a4d2e9ff` | `2ede815a` | code-2ede815a-native-red-tests | 345 | 235 | not retained | E374:7, E374:1086 |
| `f388cffb-c39f-4798-abbe-d36bda514808` | `2ede815a` | code-2ede815a-native-red | 75 | 357 | not retained | E373:3, E373:3514 |
| `aea2a416-993b-4df9-abd1-e2f215ffa4ff` | `2ede815a` | build:bin-c1591-repair | 15 | 724.455 | 2026-11-05T04:45:33.5229377+00:00 | E371:5, E371:6 |
| `c73daaf7-ceaf-4fa2-935e-65b351b3279f` | `2ede815a` | build:bin-c1591-repair | 195 | 397.331 | 2026-11-05T05:29:55.0057678+00:00 | E372:20, E372:21 |
| `5625381d-47dd-477f-b912-aa4dd6721780` | `2ede815a` | CP-1@bin-c1591-repair | 225 | 94.889 | 2026-11-05T05:41:15.7936432+00:00 | E372:41, E372:42 |
| `2887c657-3e91-4d5e-87b8-0ba05710e899` | `2ede815a` | CP-3@bin-c1591-repair | 300 | 206.729 | 2026-11-05T05:47:50.8969905+00:00 | E372:68, E372:69 |
| `1bff87c1-eefd-44de-9807-cede8d3e31f3` | `2ede815a` | CP-4@bin-c1591-repair | 135 | 172.735 | 2026-11-05T05:53:33.5265417+00:00 | E372:82, E372:83 |
| `501be951-8264-4be8-8b0e-f1bf74860219` | `2ede815a` | CP-5@bin-c1591-repair | 270 | 166.381 | 2026-11-05T06:00:56.6295862+00:00 | E372:107, E372:108 |
| `1859bd45-fdb2-4569-a8a9-796e843bde83` | `47d31366` | unknown (no label retained) | 0 | 8 | not retained | E333:1, E333:33 |
| `dca7c09b-51f7-4476-add7-49ee3bbe45c8` | `47d31366` | unknown (no label retained) | 0 | 1 | not retained | E334:1, E334:34 |
| `e8d7808f-9141-4ae0-9ee6-eb0fba91a216` | `47d31366` | unknown (no label retained) | 0 | 1 | not retained | E335:1, E335:9 |
| `d2e2b68c-92e8-42be-82bd-452009775f39` | `47d31366` | build:bin-review47d31366 | 0 | 224.340 | 2026-11-05T01:25:14.6607880+00:00 | E382:3, E382:4 |
| `1b59a74c-def9-4edb-97ac-7a1749439b42` | `47d31366` | CP-1@bin-review47d31366 | 0 | 150.912 | 2026-11-05T01:29:19.2064498+00:00 | E382:5, E382:6 |
| `7990b9ea-f081-4f2f-b4fc-2f5d8059d5fc` | `47d31366` | CP-2@bin-review47d31366 | 0 | 4502.532 | 2026-11-05T01:31:50.2483458+00:00 | E382:7, E382:9 |
| `a49b7294-78de-4ad4-b022-cedb893066f6` | `5c10955b` | 5c10955b-cpu-load-build | 271 | 107 | not retained | E294:6, E294:78 |
| `08fa554b-31ba-4169-932e-0ec9514587e5` | `6dfad742` | c1714-red-run | 0 | 40 | not retained | E043:1, E043:197, E044 |
| `27018cf5-0c25-4c84-b979-1b4e7732c61a` | `6dfad742` | c1714-run-B1 | 0 | 94 | not retained | E047:1, E047:30, E048 |
| `72aab3f5-0ab3-46e2-a372-5e6a2ece33f4` | `6dfad742` | c1714-green-build | 0 | 120 | not retained | E039:1, E039:3393, E040 |
| `b30c3051-184d-4aae-89dd-8e612a4931cd` | `6dfad742` | c1714-red-build | 0 | 217 | not retained | E041:1, E041:3393, E042 |
| `e2043bc1-94af-4efb-bbae-4f15b26e315b` | `6dfad742` | c1714-run-A | 0 | 240 | not retained | E045:1, E045:28, E046 |
| `f5746297-cdb1-48fa-8610-c6fb01589a74` | `6dfad742` | c1714-run-B2 | 0 | 168 | not retained | E049:1, E049:32, E050 |
| `3b9daca4-7cae-46a2-9a9a-1094e9df86cd` | `71dacd6c` | build:bin-c1432 | 195 | 324.181 | 2026-11-05T06:44:52.4543904+00:00 | E318:20, E318:21 |
| `c4fb7e6f-cd04-4482-9fb2-88cbc4052c2c` | `71dacd6c` | CP-1@bin-c1432 | 285 | 17.729 | 2026-11-05T06:55:37.5111887+00:00 | E318:46, E318:47 |
| `e649a1c2-a512-434f-bce4-576f6945154d` | `71dacd6c` | build:bin-c1432 | 90 | 416.737 | 2026-11-05T06:59:39.4292088+00:00 | E319:11, E319:12 |
| `086fb582-c3e8-41f3-b9d3-f931a44143cf` | `71dacd6c` | CP-1@bin-c1432 | 360 | 8.485 | 2026-11-05T07:12:59.8167947+00:00 | E319:43, E319:44 |
| `3d97e93a-6094-4e00-9b44-9e0e51cf5380` | `727a57dd` | merge72-solution-serial | 331 | 67 | not retained | E264:7, E264:160 |
| `420d2620-ab11-40dd-84ed-99a916c66bae` | `727a57dd` | merge72-solution | 15 | 804 | not retained | E265:2, E265:3882 |
| `5cc2825c-9ec1-4f35-bbbf-9c08c09e1f2f` | `727a57dd` | merge72-wake-b | 375 | 389 | not retained | E270:8, E270:59 |
| `64cd5cd3-565d-4caa-9768-3fe0d5be93b7` | `727a57dd` | merge72-wake-a | 391 | 238 | not retained | E269:8, E269:33 |
| `746f7c49-f526-4141-ac7e-a5b22364ec30` | `727a57dd` | merge72-seats-guards | 135 | 305 | not retained | E268:4, E268:29 |
| `bcd81550-bdcf-490d-a112-ec24a51c01b7` | `727a57dd` | merge72-distillation-alone | 195 | 86 | not retained | E266:5, E266:30 |
| `fc3f3cd1-f08e-4b22-af62-f7ea8931a78c` | `727a57dd` | merge72-g1 | 601 | 676 | not retained | E267:11, E267:36 |
| `5043ebc1-cd90-4836-a005-42a623d0f501` | `73c027ae` | build:bin-c1591-s1 | 0 | 247.479 | 2026-11-05T02:44:52.6400398+00:00 | E197:3, E197:4 |
| `070b7de2-553d-462f-b7fd-1bd47c712fe1` | `73c027ae` | CP-1@bin-c1591-s1 | 0 | 76.803 | 2026-11-05T02:49:27.8520372+00:00 | E197:5, E197:6 |
| `000a1c91-b445-41fc-8d60-684a6584283a` | `99e23919` | unknown (no label retained) | 0 | 42 | not retained | E082:1, E082:288 |
| `11cd96d5-036f-4796-a504-2d46b1298a75` | `99e23919` | unknown (no label retained) | 0 | 202 | not retained | E077:1, E077:874, E078 |
| `1ec5cea1-aad7-459f-8219-dd3acf9d81d7` | `99e23919` | unknown (no label retained) | 0 | 25 | not retained | E083:1, E083:199 |
| `287b2d14-d3cd-4434-b11a-d3e49ea21062` | `99e23919` | review-99e23919-M2M5-build | 0 | 145 | not retained | E070:1, E070:3347, E071 |
| `30cc2b1f-d888-42ad-9734-ec25f6ef2a08` | `99e23919` | review-99e23919-redbuild | 0 | 149 | not retained | E066:1, E066:3393, E067 |
| `8e575f01-261a-4457-b79f-86a1c415c37a` | `99e23919` | review-99e23919-build | 0 | 163 | not retained | E064:1, E064:3393, E065 |
| `a0d564c7-d4a7-465d-953b-4ec8b74a15c1` | `99e23919` | unknown (no label retained) | 0 | 67 | not retained | E079:1, E079:26 |
| `a7499d9c-ea15-4f74-a268-cf38399d3512` | `99e23919` | review-99e23919-M1M3-build | 0 | 200 | not retained | E068:1, E068:3393, E069 |
| `ab581ac6-0675-41e6-b83a-261733b912fb` | `99e23919` | unknown (no label retained) | 0 | 33 | not retained | E080:1, E080:113 |
| `bc8a34db-d8e6-4cd6-8c39-5a1393a72b6f` | `99e23919` | unknown (no label retained) | 0 | 51 | not retained | E081:1, E081:114 |
| `cb1b96ed-690c-45b2-836b-370a1ded9960` | `99e23919` | unknown (no label retained) | 0 | 290 | not retained | E072:1, E072:28, E073, E074 |
| `f16047d9-c0f5-47b3-bebe-3ed7ed4d29c0` | `99e23919` | unknown (no label retained) | 0 | 73 | not retained | E075:1, E075:30, E076 |
| `0d3a7eeb-189b-4d72-95aa-232cc2694a4d` | `a2cfcdc1` | c1720-review-mutrun | 0 | 10 | not retained | E311:1, E311:105, E312 |
| `42f9af99-3231-48a6-a88c-623d14da4dfe` | `a2cfcdc1` | c1720-review-runA | 135 | 2901 | not retained | E313:4, E313:149, E314, E315 |
| `44741500-5d08-4367-ad52-f5dd2c01c68d` | `a2cfcdc1` | c1720-review-build | 346 | 233 | not retained | E301:7, E301:3512 |
| `6dab028c-e8fb-4f13-8b36-db0c3f809600` | `a2cfcdc1` | c1720-review-iso | 465 | 93 | not retained | E309:9, E309:62, E310 |
| `8eca2d18-7c64-4f89-a9ad-0c2cb8e74587` | `a2cfcdc1` | c1720-review-mutbuild | 211 | 380 | not retained | E302:5, E302:3397, E303, E304 |
| `a4ce2597-67e7-43c0-936b-7597ca7c9c43` | `a2cfcdc1` | c1720-review-greenrun | 0 | 12 | not retained | E307:1, E307:27, E308 |
| `af5448ec-ee5a-47bb-9177-9935aaee70e9` | `a2cfcdc1` | c1720-review-runB | 436 | 169 | not retained | E316:9, E316:55, E317 |
| `ceb21271-c7fa-4338-a245-a08e55288817` | `a2cfcdc1` | c1720-review-restorebuild | 45 | 248 | not retained | E305:2, E305:3394, E306 |
| `0206cc83-2626-49bb-ac25-6b3bb57d4636` | `a4933b4f` | a493-W5-exact | 255 | 53 | not retained | E359:6, E359:31 |
| `0fa16aa8-4e60-4f2f-a140-9cf7daddd8d0` | `a4933b4f` | a493-mutant-build | 586 | 281 | not retained | E365:11, E365:3508 |
| `17205f4a-c8ac-4af0-b895-24fe2bb163c2` | `a4933b4f` | a493-W7-pack | 360 | 101 | not retained | E362:7, E362:32 |
| `43586a48-ee9f-476e-bdec-32556a5e5837` | `a4933b4f` | unknown (no label retained) | 0 | 132 | not retained | E364:1, E364:26 |
| `539caa4e-b3ff-4c6c-a748-1435356c2ecf` | `a4933b4f` | a493-W1-pack | 165 | 106 | not retained | E358:4, E358:53 |
| `5e6f5b0c-523b-431e-8468-56cdce6501ba` | `a4933b4f` | a493-restored-build | 225 | 578 | not retained | E366:5, E366:3427 |
| `5f8225f8-e3ba-4204-a7a7-7c5b353e203b` | `a4933b4f` | a493-green-base-endpoint | 15 | 41 | not retained | E368:2, E368:33 |
| `9b171fb4-5db6-472c-ae9a-46fa88bb48e5` | `a4933b4f` | a493-W5-pack | 135 | 135 | not retained | E360:4, E360:29 |
| `9e644a09-bf92-4eb8-851b-03c0b7ecabf4` | `a4933b4f` | a493-green-control | 210 | 221 | not retained | E369:5, E369:369 |
| `c07c678d-ed67-4dff-b6bf-35afa87b8825` | `a4933b4f` | a493-green-restored | 60 | 189 | not retained | E370:2, E370:27 |
| `d1658d10-157a-4870-9030-2fa8fe183c6a` | `a4933b4f` | a493-switch-build | 105 | 278 | not retained | E367:3, E367:3425 |
| `e9f45979-8d5d-4280-ab06-5118c1fad9c0` | `a4933b4f` | unknown (no label retained) | 0 | 30 | not retained | E363:1, E363:26 |
| `f01bfabe-dc02-44d6-aa59-1d7368b5eb6f` | `a4933b4f` | a493-W7-exact | 75 | 53 | not retained | E361:3, E361:28 |
| `f1ab345e-a8e8-4a18-a3e3-3c78c3c08b72` | `a4933b4f` | a493-W1-exact | 210 | 31 | not retained | E357:5, E357:76 |
| `2add6f6e-6cca-4f13-a916-3a21dadc85ca` | `ae6457ac` | unknown (no label retained) | 0 | 90 | not retained | E035:1, E035:25 |
| `393cd172-5d56-4bd2-a253-57bea89bbb8e` | `ae6457ac` | merge68-landgraph | 0 | 18 | not retained | E036:1, E036:13, E037 |
| `5fa0d6ef-8ad9-4ac2-908b-9affb9b5ab81` | `ae6457ac` | merge68-g1 | 0 | unknown | not retained | E402 |
| `7863a716-2199-4742-b60d-6e7d7b3f8030` | `ae6457ac` | unknown (no label retained) | 0 | 949 | not retained | E033:1, E033:26, E034 |
| `80d1a5f4-cab6-4607-aea8-f9350e38a78a` | `ae6457ac` | merge68-broker | 0 | 27 | not retained | E021:1, E021:25, E022 |
| `89840039-1148-47cb-b32e-5a1280b7b09d` | `ae6457ac` | merge68-g1b | 0 | 628 | not retained | E027:1, E027:26, E028, E029 |
| `a38ba99a-d41c-46f7-a978-ff53911ab893` | `ae6457ac` | unknown (no label retained) | 0 | 139 | not retained | E038:1, E038:29 |
| `c074bd2f-08ff-4d31-8a9c-1611b35ddbaa` | `ae6457ac` | merge68-sln | 0 | 272 | not retained | E023:1, E023:3849, E024, E025 |
| `c25c13cb-1c79-431f-b14d-91b33e2a7641` | `ae6457ac` | unknown (no label retained) | 0 | 20 | not retained | E026:1, E026:25 |
| `c3a26064-0cdf-4977-b073-a5067e56c5e6` | `ae6457ac` | unknown (no label retained) | 0 | 824 | not retained | E030:1, E030:26, E031, E032 |
| `1d3b5fec-9b94-494c-857d-4cf78af5ec85` | `c3ba1792` | c1720-base-inode-witness | 330 | 7 | not retained | E255:7, E255:38 |
| `86d9d98e-de67-4e54-864b-19d47a32366a` | `c3ba1792` | c1720-unit-roster | 151 | 11 | not retained | E263:4, E263:21913 |
| `a2989f51-bbb9-4970-bbbf-db56fe80b606` | `c3ba1792` | c1720-red-witness | 45 | 6 | not retained | E262:2, E262:44, E261 |
| `dadf8ca9-76e4-41b2-ba3b-d72473e87c54` | `c3ba1792` | unknown (no label retained) | 0 | 673 | not retained | E260:1, E260:3506, E261 |
| `e4aeba9b-6e02-4833-a528-23d59cb95380` | `c3ba1792` | c1720-prefix-filter-probe | 375 | 1 | not retained | E259:8, E259:32 |
| `eebb9aca-cfd5-4d69-996b-2a7681b3f13e` | `c3ba1792` | c1720-inode-isolation | 375 | 13 | not retained | E258:8, E258:34 |
| `79eca241-0777-4c51-9a15-5277750e51eb` | `c3ba1792` | build:bin-c1720-final | 120 | 771.623 | 2026-11-05T04:52:01.1393615+00:00 | E257:13, E257:14 |
| `da9a356a-569c-465d-a719-ba36c26929ab` | `c3ba1792` | CP-1@bin-c1720-final | 585 | 87.725 | 2026-11-05T05:15:39.4845370+00:00 | E257:64, E257:65 |
| `29319c0c-5c47-477a-988b-b2281d69c9cc` | `c3ba1792` | CP-2@bin-c1720-final | 360 | 2702.101 | 2026-11-05T05:23:07.4483984+00:00 | E256:7, E256:8, E257:96, E257:97 |
| `4aa27564-4c04-4c4a-81e5-5fa080d378e2` | `c3ba1792` | CP-3@bin-c1720-final | 405 | unknown | 2026-11-05T06:14:54.7954317+00:00 | E256:43, E257:132 |
| `ab4306fd-2a3e-472a-8769-77d68dab3840` | `c781f7f8` | build:bin-c1722-red | 270 | 273.772 | 2026-11-05T07:13:04.7399471+00:00 | E324:26, E324:27 |
| `de891fab-a14d-443b-a721-4e30dc78f41d` | `c781f7f8` | build:bin-c1722-red-b | 45 | 635.845 | 2026-11-05T07:19:35.2098295+00:00 | E325:7, E325:22 |
| `20c96780-a0a1-4925-9304-cdc61409992d` | `c781f7f8` | build:bin-c1722-green | 240 | 524.048 | 2026-11-05T07:44:57.1254032+00:00 | E326:23, E326:24 |
| `2160df0e-a2f8-483a-8e35-155b42a9d5e5` | `c9ebcc1d` | merge73-solution | 285 | 375 | not retained | E284:6, E284:3880 |
| `7b9d0539-221e-4544-8307-455286cc7f63` | `c9ebcc1d` | merge73-guards-witness-seats | 75 | 133 | not retained | E285:3, E285:28 |
| `0ad6928a-724e-466e-ae58-6cf080618ab7` | `d19080aa` | unknown (no label retained) | 0 | 158 | not retained | E015:1, E015:1713 |
| `0b7a434d-b4e3-428a-9ec2-dc80e9d69c80` | `d19080aa` | unknown (no label retained) | 0 | 309 | not retained | E005:1, E005:28, E006 |
| `3390acc1-8b61-46d3-bf79-474a478e2d86` | `d19080aa` | unknown (no label retained) | 0 | 88 | not retained | E007:1, E007:26, E008 |
| `45a2ee3e-77df-4fb0-a009-4b0f4b7baeed` | `d19080aa` | unknown (no label retained) | 0 | 134 | not retained | E011:1, E011:1844 |
| `5cac0746-a96d-4ef1-a11b-e8e491335256` | `d19080aa` | unknown (no label retained) | 0 | 137 | not retained | E009:1, E009:1741 |
| `6cdd7e74-d44a-4b2d-b6f4-3accf13390c1` | `d19080aa` | unknown (no label retained) | 0 | 261 | not retained | E001:1, E001:3472, E002 |
| `70f0e7a5-9ec3-40ac-a388-970d218e04ed` | `d19080aa` | unknown (no label retained) | 0 | 68 | not retained | E012:1, E012:177 |
| `7763db76-aeed-4008-b3b7-63e9290eca45` | `d19080aa` | unknown (no label retained) | 0 | 321 | not retained | E003:1, E003:48, E004 |
| `77adc488-10b5-405c-8dcf-fb00a91b9aa5` | `d19080aa` | unknown (no label retained) | 0 | 292 | not retained | E017:1, E017:2247, E018 |
| `845572fc-2b5d-4c32-b49e-ac6eb459e2b0` | `d19080aa` | unknown (no label retained) | 0 | 63 | not retained | E010:1, E010:80 |
| `87982083-0683-4d5b-90f4-5a3814d5eb8f` | `d19080aa` | unknown (no label retained) | 0 | 66 | not retained | E013:1, E013:100 |
| `962de166-d6ad-4d3c-ad3c-b162f527d942` | `d19080aa` | unknown (no label retained) | 0 | 195 | not retained | E014:1, E014:1707 |
| `c415990f-c89b-4ba6-b9f0-2e02522c9f0e` | `d19080aa` | unknown (no label retained) | 0 | 176 | not retained | E016:1, E016:1690 |
| `f54b2c36-b3ca-4cb0-a5d2-50a4a184931e` | `d19080aa` | unknown (no label retained) | 0 | 20 | not retained | E019:1, E019:26, E020 |
| `48ca1c73-0653-4619-9fbd-5e09ed33c8f1` | `d6e48431` | review-d6e48431-control-run | 421 | 7 | not retained | E282:8, E282:100, E283 |
| `676ae5e2-8b0b-487a-a17d-58622ebaedf4` | `d6e48431` | review-d6e48431-mutant-MB-run | 150 | 4 | not retained | E280:4, E280:134, E281 |
| `6ea65792-8318-4992-8b13-898b7a87327b` | `d6e48431` | review-d6e48431-mutant-build | 375 | 64 | not retained | E274:8, E274:70, E275 |
| `73122467-f977-42f0-b373-11bf0fb0733d` | `d6e48431` | review-d6e48431-mutant-MA-run | 271 | 11 | not retained | E278:6, E278:102, E279 |
| `a3b4a649-a843-49e3-a3a5-58a50dbd911f` | `d6e48431` | review-d6e48431-checkpoint-bootstrap | 240 | 23 | not retained | E272:5, E272:37, E273 |
| `e014bcd1-8a31-44ad-ab67-9df7cdd04bc7` | `d6e48431` | review-d6e48431-mutant-build2 | 225 | 86 | not retained | E276:5, E276:297, E277 |
| `7076bb9b-1ad6-4808-b91a-917ba3db00cc` | `d6e48431` | build:bin-c1718-r2-runner | 150 | 97.010 | 2026-11-05T05:56:26.7971212+00:00 | E271:16, E271:17 |
| `7d4b6509-9975-42a1-8bef-b7252222fcd1` | `d6e48431` | CP-1@bin-c1718-r2-runner | 240 | 122.905 | 2026-11-05T06:02:12.4245039+00:00 | E271:38, E271:39 |
| `54c7e212-4c8c-4037-b5e5-3ecbf8ef0c85` | `d6e48431` | build:bin-c1718-r2-server | 195 | 844.435 | 2026-11-05T06:07:54.5734383+00:00 | E271:57, E271:58 |
| `823aa929-d19b-4182-9dc8-cceacc9e8465` | `d6e48431` | CP-2@bin-c1718-r2-server | 150 | 343.824 | 2026-11-05T06:24:49.7883023+00:00 | E271:72, E271:73 |
| `012e1db0-88f4-4ede-b243-61a2c1f13902` | `e24018e3` | unknown (no label retained) | 0 | 134 | not retained | E122:1, E122:38, E123 |
| `073b076d-deac-44b7-947f-195c348c87d4` | `e24018e3` | unknown (no label retained) | 0 | 20 | not retained | E112:1, E112:85 |
| `08ac61fe-eef9-4d93-808c-9e4633a6a45f` | `e24018e3` | unknown (no label retained) | 0 | 82 | not retained | E137:1, E137:52 |
| `102476d9-ab39-431c-bb24-89992233f865` | `e24018e3` | unknown (no label retained) | 0 | 21 | not retained | E097:1, E097:43, E098 |
| `1c76cee9-3723-4799-8e30-555529819ea0` | `e24018e3` | unknown (no label retained) | 0 | 172 | not retained | E128:1, E128:3472, E129 |
| `1d9b8623-1cc7-4909-9a68-eff0ff28fcde` | `e24018e3` | unknown (no label retained) | 0 | 18 | not retained | E105:1, E105:43 |
| `22e6baf8-2850-49c4-afc5-4d50dc169aff` | `e24018e3` | unknown (no label retained) | 0 | 273 | not retained | E124:1, E124:63, E125 |
| `2a824eb8-d14a-4f42-9e02-cdb1b8f3867d` | `e24018e3` | unknown (no label retained) | 0 | 20 | not retained | E101:1, E101:43 |
| `37812c70-6f89-416c-b3ec-ffc5e1fb4cb8` | `e24018e3` | e24018e3-A-restored-build | 0 | 24 | not retained | E119:1, E119:297, E120 |
| `3aa8aee8-ef70-4a4a-b134-155c38b9bdda` | `e24018e3` | unknown (no label retained) | 0 | 27 | not retained | E104:1, E104:43 |
| `42a552b9-d057-40fc-8667-c5b5f947cc88` | `e24018e3` | e24018e3-A-restored-run | 0 | 159 | not retained | E153:1, E153:3391, E154, E155 |
| `517066d0-64f9-4a0c-9d6c-f94c4488cad1` | `e24018e3` | unknown (no label retained) | 0 | 39 | not retained | E403 |
| `54d9dac7-6a81-4f61-ba3f-62d3705a0c60` | `e24018e3` | unknown (no label retained) | 0 | 93 | not retained | E141:1, E141:26 |
| `559379f4-adaf-4243-8de9-6c1c51560b4b` | `e24018e3` | unknown (no label retained) | 0 | 75 | not retained | E133:1, E133:26 |
| `57680a37-790a-41ee-8084-69d7173454f6` | `e24018e3` | unknown (no label retained) | 0 | 102 | not retained | E130:1, E130:26, E131 |
| `63833df3-f386-4012-bb11-5718d8157cce` | `e24018e3` | unknown (no label retained) | 0 | 79 | not retained | E161:1, E161:26, E162 |
| `64364c6c-e63a-4a18-aa7d-81de4566d8ae` | `e24018e3` | unknown (no label retained) | 0 | 19 | not retained | E121:1, E121:26 |
| `67f97ef8-f0ff-4d3b-880f-fba4714df24e` | `e24018e3` | unknown (no label retained) | 0 | 21 | not retained | E106:1, E106:43 |
| `681df3cd-ae9b-4e19-9bd2-e897c8745766` | `e24018e3` | unknown (no label retained) | 0 | 116 | not retained | E151:1, E151:38 |
| `6cdf2629-c104-41e4-8c05-00d34695ef92` | `e24018e3` | unknown (no label retained) | 0 | 95 | not retained | E139:1, E139:33 |
| `6da7d17c-0952-41e3-abc0-c24afefcfb8e` | `e24018e3` | unknown (no label retained) | 0 | 23 | not retained | E103:1, E103:43 |
| `7983ffe3-2736-4207-9d82-d08d33f1c993` | `e24018e3` | unknown (no label retained) | 0 | 20 | not retained | E100:1, E100:66 |
| `7d190e2f-4b0c-4634-8464-457fbdec95d8` | `e24018e3` | unknown (no label retained) | 0 | 19 | not retained | E110:1, E110:66 |
| `8458c7c8-f5e7-4921-a0dd-ba18a4acf012` | `e24018e3` | unknown (no label retained) | 0 | 90 | not retained | E142:1, E142:26 |
| `857aab29-4c5c-435c-8199-727eeb1fcf65` | `e24018e3` | unknown (no label retained) | 0 | 114 | not retained | E150:1, E150:39 |
| `86631cd6-affb-4260-9488-bb78a704d6c0` | `e24018e3` | unknown (no label retained) | 0 | 78 | not retained | E143:1, E143:52 |
| `8f14b5d4-cedd-4f29-8ee7-311d114ebe46` | `e24018e3` | unknown (no label retained) | 0 | 34 | not retained | E126:1, E126:26, E127 |
| `98cdfef2-2ade-4c65-9d5f-3e7e8c66ab2a` | `e24018e3` | unknown (no label retained) | 0 | 26 | not retained | E107:1, E107:79 |
| `9df981d4-571a-4dcd-bc94-2a0f33c4324a` | `e24018e3` | unknown (no label retained) | 0 | 95 | not retained | E146:1, E146:82 |
| `a32de711-dc61-4991-aed2-4ed5bce3ff13` | `e24018e3` | unknown (no label retained) | 0 | 1244 | not retained | E163:1, E163:26, E164 |
| `a372d933-38c1-4323-a49e-25f736cf3e34` | `e24018e3` | unknown (no label retained) | 0 | 74 | not retained | E134:1, E134:26 |
| `a8beaece-ad3b-4e7e-a1eb-716359a5e789` | `e24018e3` | unknown (no label retained) | 0 | 23 | not retained | E113:1, E113:26, E114 |
| `ac9d18d6-57da-46a1-89c3-0b2f06c33110` | `e24018e3` | unknown (no label retained) | 0 | 20 | not retained | E099:1, E099:82 |
| `b94b3fc9-e7d1-4914-bfb5-c1a5164f1d24` | `e24018e3` | unknown (no label retained) | 0 | 19 | not retained | E111:1, E111:68 |
| `bae0cb52-eadf-4422-83b5-d2f8a0de52f5` | `e24018e3` | unknown (no label retained) | 0 | 73 | not retained | E145:1, E145:54 |
| `bd9a4bfa-d095-49c1-8b96-e60c8744af33` | `e24018e3` | unknown (no label retained) | 0 | 76 | not retained | E138:1, E138:52 |
| `be02efee-6855-4a2e-b285-744a2dca809e` | `e24018e3` | unknown (no label retained) | 0 | 73 | not retained | E144:1, E144:80 |
| `c0367c36-e5fe-436f-ba40-d2d8ddad619f` | `e24018e3` | unknown (no label retained) | 0 | 27 | not retained | E102:1, E102:26 |
| `c72252a9-1f34-4059-999b-c24dd00f5047` | `e24018e3` | unknown (no label retained) | 0 | 78 | not retained | E147:1, E147:75 |
| `c7b51c3b-a2a7-4a72-9263-4f3352e3f63f` | `e24018e3` | unknown (no label retained) | 0 | 31 | not retained | E115:1, E115:297, E116 |
| `cc0459b6-95f8-4f5a-850b-8f8661aa8c3b` | `e24018e3` | unknown (no label retained) | 0 | 91 | not retained | E152:1, E152:26 |
| `cd46d2cd-a883-446e-8f49-fd8271cbe6ee` | `e24018e3` | e24018e3-B-restored-build | 0 | 127 | not retained | E159:1, E159:3397, E160 |
| `cf9b121b-f5e5-4d27-87db-79e68abefbda` | `e24018e3` | unknown (no label retained) | 0 | 22 | not retained | E096:1, E096:79 |
| `e3e5bfb7-e1ee-4ebe-ad37-d1d46648186c` | `e24018e3` | unknown (no label retained) | 0 | 115 | not retained | E149:1, E149:38 |
| `eb05cde1-738e-46d1-a8bb-7b92b703fefe` | `e24018e3` | unknown (no label retained) | 0 | 27 | not retained | E117:1, E117:26, E118 |
| `ecd6f641-a77b-4b99-a88c-f6fee30b55cc` | `e24018e3` | unknown (no label retained) | 0 | 27 | not retained | E109:1, E109:43 |
| `f1a96bbe-de45-4926-b146-c44f1aff7d96` | `e24018e3` | unknown (no label retained) | 0 | 17 | not retained | E108:1, E108:64 |
| `f23145c9-8eaf-4799-b5d2-470eb0ebc585` | `e24018e3` | unknown (no label retained) | 0 | 84 | not retained | E132:1, E132:52 |
| `f7abf59b-5e64-4aa0-8b60-29784cf95003` | `e24018e3` | unknown (no label retained) | 0 | 273 | not retained | E135:1, E135:17, E136 |
| `fbc17255-5eac-44ca-9f0b-e35e9968b1fb` | `e24018e3` | unknown (no label retained) | 0 | 83 | not retained | E140:1, E140:49 |
| `fdb5be2f-f1df-4b1f-83be-e579c5c0e537` | `e24018e3` | unknown (no label retained) | 0 | 153 | not retained | E156:1, E156:3397, E157, E158 |
| `fea85472-3ab1-4162-8e40-dd9f3e91126f` | `e24018e3` | unknown (no label retained) | 0 | 41 | not retained | E165:1, E165:304, E166, E116 |
| `ff73fb23-7acb-4e27-ba27-6626ffd98579` | `e24018e3` | unknown (no label retained) | 0 | 78 | not retained | E148:1, E148:64 |
| `2f0c6091-facb-4ba2-b3e5-e9ba07f908c1` | `e778bd07` | unknown (no label retained) | 0 | 47 | not retained | E192:1, E192:52 |
| `36922c15-cd90-4c7e-9367-a6ec3e822a1a` | `e778bd07` | e778-distill-rerun | 15 | 73 | not retained | E193:2, E193:27, E194 |
| `3bebc0f7-d9d0-4320-8bf1-00e69c9f4ace` | `e778bd07` | e778-mutant-build | 0 | 106 | not retained | E184:1, E184:3359, E185 |
| `419817ec-fb91-4945-9362-9210a8f720be` | `e778bd07` | unknown (no label retained) | 0 | 1351 | not retained | E181:1, E181:26, E182, E183 |
| `8b9afe89-8f6f-4abe-9224-5c1474102a05` | `e778bd07` | unknown (no label retained) | 0 | 581 | not retained | E177:1, E177:69, E178 |
| `95a7887a-a3f3-440a-9168-cd2dc846afa7` | `e778bd07` | unknown (no label retained) | 0 | 68 | not retained | E190:1, E190:157 |
| `9a56df92-5f70-4834-a98f-42755a5b17d3` | `e778bd07` | unknown (no label retained) | 0 | 59 | not retained | E188:1, E188:55 |
| `a9340846-1740-4945-9296-386c0e355677` | `e778bd07` | unknown (no label retained) | 0 | 52 | not retained | E191:1, E191:78 |
| `b9cbaf7c-d894-4d4e-837a-241609d9afe1` | `e778bd07` | unknown (no label retained) | 0 | 63 | not retained | E195:1, E195:26, E196 |
| `d7c55b64-a81d-4104-8a45-a0c47864dc4e` | `e778bd07` | unknown (no label retained) | 0 | 97 | not retained | E189:1, E189:82 |
| `e975e9cf-bec9-4f68-b2d2-6f3a038d9e64` | `e778bd07` | e778-mutant-build | 0 | 142 | not retained | E404 |
| `f1a98361-f796-4c8e-9328-c8dd91d64e16` | `e778bd07` | unknown (no label retained) | 0 | 93 | not retained | E179:1, E179:26, E178, E180 |
| `ff28a5c8-2f91-464f-a42e-ad6e31f362fc` | `e778bd07` | e778-restored-build | 0 | 151 | not retained | E186:1, E186:3457, E187 |
| `1527f3c9-c969-4033-9d4b-1126113c0d84` | `eab3a4fc` | unknown (no label retained) | 0 | 90 | not retained | E055:1, E055:26, E056 |
| `280a48f1-227c-445a-a7ae-102589ecea97` | `eab3a4fc` | c448-m7-run | 0 | 11 | not retained | E059:1, E059:66, E060 |
| `9b866fbd-009c-4f9d-8974-62ebd33acd49` | `eab3a4fc` | c448-m7-build | 0 | 168 | not retained | E057:1, E057:3472, E058 |
| `b3245b82-c725-4026-83da-37d27641de14` | `eab3a4fc` | unknown (no label retained) | 0 | 302 | not retained | E053:1, E053:28, E054 |
| `b44ab0c1-0807-4b66-b548-5f6fe327478f` | `eab3a4fc` | c448-tip-build | 0 | 137 | not retained | E061:1, E061:3397, E062 |
| `f305ac20-3b71-473d-8bb6-0eb5281ee023` | `eab3a4fc` | unknown (no label retained) | 0 | 7 | not retained | E063:1, E063:26, E056 |
| `fb99bb77-e422-485f-9da0-8a25cd9935c4` | `eab3a4fc` | unknown (no label retained) | 0 | 193 | not retained | E051:1, E051:48, E052 |
| `1b2d27ce-cd51-4a53-94c7-057f652be7e5` | `f059674d` | g71-master-class-13-through-20-retry | 165 | 974 | not retained | E383:4, E383:38 |
| `5d12cf0e-f90d-4095-85d3-e8b66947127d` | `f059674d` | g71-master-class-4-through-12-retry | 195 | 1103 | not retained | E384:5, E384:43 |
| `01478a74-ba16-436a-8345-f081f3bff60b` | `f35f1237` | merge69-sln | 0 | 210 | not retained | E084:1, E084:3857, E085 |
| `0ef4b5ee-8d52-418a-aed6-d0f0046c2121` | `f35f1237` | unknown (no label retained) | 0 | 138 | not retained | E094:1, E094:25 |
| `0f624038-efb9-4593-af32-79ce188dd08a` | `f35f1237` | landgraph-eval | 0 | 5 | not retained | E405 |
| `444661ff-74e4-4a61-8d7a-1c5186f3d59c` | `f35f1237` | unknown (no label retained) | 0 | 102 | not retained | E090:1, E090:25 |
| `4f18e1f9-373c-4d1b-ae85-fa8402dd5c96` | `f35f1237` | unknown (no label retained) | 0 | 382 | not retained | E087:1, E087:25 |
| `675ba1cb-a548-42a3-a0bf-bd2322a18924` | `f35f1237` | unknown (no label retained) | 0 | 209 | not retained | E093:1, E093:25 |
| `99d6b107-ea42-48be-b061-3f4dd03df928` | `f35f1237` | unknown (no label retained) | 0 | 143 | not retained | E089:1, E089:25 |
| `a6333d34-257a-44c3-9ed5-9f562241e855` | `f35f1237` | unknown (no label retained) | 0 | 251 | not retained | E091:1, E091:25 |
| `c7ae7e02-b1a6-46ac-98da-2d4dfa2b44bb` | `f35f1237` | unknown (no label retained) | 0 | 218 | not retained | E086:1, E086:27 |
| `e55f8edf-5d4e-4305-a38d-5c167384d8c4` | `f35f1237` | unknown (no label retained) | 0 | 14 | not retained | E092:1, E092:29 |
| `f07f6bfb-c83f-4122-bd7d-0f3b12b32f26` | `f35f1237` | unknown (no label retained) | 0 | 191 | not retained | E088:1, E088:25 |
| `f1e8a428-7c5a-4bc6-8ad9-945cd7339f0d` | `f35f1237` | unknown (no label retained) | 0 | 160 | not retained | E095:1, E095:29 |
| `0bf8c2d8-a62e-4db0-ba8c-7bc750f2a27c` | `ff3ef62e` | ff3ef62e-master-normal-receipt | 285 | 102 | not retained | E344:6, E344:31 |
| `1a4c32bb-ed51-4dbc-be86-d8457f3d5f06` | `ff3ef62e` | ff3ef62e-master-starvation-proof | 270 | 7 | not retained | E347:6, E347:21 |
| `1c783eab-583d-4a8c-9472-c10bec900201` | `ff3ef62e` | ff3ef62e-rc-build | 255 | 271 | not retained | E349:6, E349:3475 |
| `2b14ef76-8125-46db-8e38-b2103b6beb17` | `ff3ef62e` | ff3ef62e-master-build | 255 | 225 | not retained | E341:6, E341:3587 |
| `49955247-d485-453a-ac6a-aacb2f47a4b1` | `ff3ef62e` | ff3ef62e-rc-loaded-receipt | 315 | 9 | not retained | E350:7, E350:32 |
| `4f731605-84f8-4e56-abbc-60d35aed6de4` | `ff3ef62e` | ff3ef62e-rc-normal-scheduler | 225 | 13 | not retained | E353:5, E353:96 |
| `5c7f5f5a-49b2-48ba-b358-a2cb03491de2` | `ff3ef62e` | ff3ef62e-cpu-io-load-build | 271 | 604 | not retained | E340:6, E340:3587 |
| `6691e651-3a25-4ce9-9859-aa45b2292aea` | `ff3ef62e` | ff3ef62e-evidence-probe-build | 271 | 12 | not retained | E348:6, E348:16 |
| `688004b5-a5f1-4266-bf28-bb839c6dd8c6` | `ff3ef62e` | ff3ef62e-master-repeat20-build | 195 | 256 | not retained | E346:5, E346:3465 |
| `72d8fbcc-3327-4a10-81bb-001a4ea3fb94` | `ff3ef62e` | ff3ef62e-rc-repeat20-build | 315 | 180 | not retained | E354:7, E354:3355 |
| `73c8d7da-5c26-4af9-bd17-a193e000fc99` | `ff3ef62e` | ff3ef62e-rc-normal-receipt | 255 | 123 | not retained | E352:6, E352:31 |
| `7b9d5db0-8012-415d-9893-229f86ffe9a4` | `ff3ef62e` | ff3ef62e-master-loaded-receipt | 330 | 13 | not retained | E342:7, E342:32 |
| `9e5d8a9a-1555-4222-a097-86afd71a72a6` | `ff3ef62e` | ff3ef62e-rc-starvation-proof | 345 | 3 | not retained | E356:7, E356:29 |
| `9fc69a1e-ce8a-4627-9e47-c5eaa5d824f4` | `ff3ef62e` | ff3ef62e-master-normal-scheduler | 270 | 15 | not retained | E345:6, E345:64 |
| `b07e2184-a2b2-4231-929a-6f7a2042ba1d` | `ff3ef62e` | ff3ef62e-rc-starvation-proof-corrected | 240 | 7 | not retained | E355:5, E355:20 |
| `ceeaa302-c28a-483c-9dc6-2d368623487e` | `ff3ef62e` | ff3ef62e-rc-loaded-scheduler | 315 | 7 | not retained | E351:7, E351:32 |
| `fa1a2b67-625f-47e7-8592-884647d552d9` | `ff3ef62e` | ff3ef62e-master-loaded-scheduler | 330 | 12 | not retained | E343:7, E343:32 |

## Appendix B. Source catalog

| Ref | Original log / transcript or retained archive member |
|---|---|
| E001 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/build-fix.log |
| E002 | session:482777ad-500f-425a-a574-b8e105fcab7e#38 |
| E003 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/g1.log |
| E004 | session:482777ad-500f-425a-a574-b8e105fcab7e#50 |
| E005 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/g2.log |
| E006 | session:482777ad-500f-425a-a574-b8e105fcab7e#56 |
| E007 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/guards.log |
| E008 | session:482777ad-500f-425a-a574-b8e105fcab7e#59 |
| E009 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/mut-M1.log |
| E010 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/mut-M2.log |
| E011 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/mut-M3.log |
| E012 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/mut-M4.log |
| E013 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/mut-M5.log |
| E014 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/mut-M6.log |
| E015 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/mut-M7.log |
| E016 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/mut-M8.log |
| E017 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/red.log |
| E018 | session:482777ad-500f-425a-a574-b8e105fcab7e#53 |
| E019 | /tmp/claude-1654/-work-worktrees-task-d19080aa/482777ad-500f-425a-a574-b8e105fcab7e/scratchpad/v36-fix.log |
| E020 | session:482777ad-500f-425a-a574-b8e105fcab7e#45 |
| E021 | /tmp/claude-1654/-work-worktrees-task-ae6457ac/cdd2a6bc-9da3-44de-9e22-5de74b784b6f/scratchpad/broker.log |
| E022 | session:cdd2a6bc-9da3-44de-9e22-5de74b784b6f#54 |
| E023 | /tmp/claude-1654/-work-worktrees-task-ae6457ac/cdd2a6bc-9da3-44de-9e22-5de74b784b6f/scratchpad/build.log |
| E024 | session:cdd2a6bc-9da3-44de-9e22-5de74b784b6f#28 |
| E025 | session:cdd2a6bc-9da3-44de-9e22-5de74b784b6f#33 |
| E026 | /tmp/claude-1654/-work-worktrees-task-ae6457ac/cdd2a6bc-9da3-44de-9e22-5de74b784b6f/scratchpad/c1210.log |
| E027 | /tmp/claude-1654/-work-worktrees-task-ae6457ac/cdd2a6bc-9da3-44de-9e22-5de74b784b6f/scratchpad/g1.log |
| E028 | session:cdd2a6bc-9da3-44de-9e22-5de74b784b6f#41 |
| E029 | session:cdd2a6bc-9da3-44de-9e22-5de74b784b6f#43 |
| E030 | /tmp/claude-1654/-work-worktrees-task-ae6457ac/cdd2a6bc-9da3-44de-9e22-5de74b784b6f/scratchpad/g2.log |
| E031 | session:cdd2a6bc-9da3-44de-9e22-5de74b784b6f#59 |
| E032 | session:cdd2a6bc-9da3-44de-9e22-5de74b784b6f#61 |
| E033 | /tmp/claude-1654/-work-worktrees-task-ae6457ac/cdd2a6bc-9da3-44de-9e22-5de74b784b6f/scratchpad/g3.log |
| E034 | session:cdd2a6bc-9da3-44de-9e22-5de74b784b6f#65 |
| E035 | /tmp/claude-1654/-work-worktrees-task-ae6457ac/cdd2a6bc-9da3-44de-9e22-5de74b784b6f/scratchpad/guards.log |
| E036 | /tmp/claude-1654/-work-worktrees-task-ae6457ac/cdd2a6bc-9da3-44de-9e22-5de74b784b6f/scratchpad/lg-build.log |
| E037 | session:cdd2a6bc-9da3-44de-9e22-5de74b784b6f#46 |
| E038 | /tmp/claude-1654/-work-worktrees-task-ae6457ac/cdd2a6bc-9da3-44de-9e22-5de74b784b6f/scratchpad/scripts.log |
| E039 | /tmp/claude-1654/-work-worktrees-task-6dfad742/d817f8fd-248f-42f9-a7a7-42c44c70e0a3/scratchpad/build-green.log |
| E040 | session:d817f8fd-248f-42f9-a7a7-42c44c70e0a3#157 |
| E041 | /tmp/claude-1654/-work-worktrees-task-6dfad742/d817f8fd-248f-42f9-a7a7-42c44c70e0a3/scratchpad/build-red.log |
| E042 | session:d817f8fd-248f-42f9-a7a7-42c44c70e0a3#135 |
| E043 | /tmp/claude-1654/-work-worktrees-task-6dfad742/d817f8fd-248f-42f9-a7a7-42c44c70e0a3/scratchpad/red.log |
| E044 | session:d817f8fd-248f-42f9-a7a7-42c44c70e0a3#140 |
| E045 | /tmp/claude-1654/-work-worktrees-task-6dfad742/d817f8fd-248f-42f9-a7a7-42c44c70e0a3/scratchpad/runA.log |
| E046 | session:d817f8fd-248f-42f9-a7a7-42c44c70e0a3#162 |
| E047 | /tmp/claude-1654/-work-worktrees-task-6dfad742/d817f8fd-248f-42f9-a7a7-42c44c70e0a3/scratchpad/runB1.log |
| E048 | session:d817f8fd-248f-42f9-a7a7-42c44c70e0a3#167 |
| E049 | /tmp/claude-1654/-work-worktrees-task-6dfad742/d817f8fd-248f-42f9-a7a7-42c44c70e0a3/scratchpad/runB2.log |
| E050 | session:d817f8fd-248f-42f9-a7a7-42c44c70e0a3#169 |
| E051 | /tmp/claude-1654/-work-worktrees-task-eab3a4fc/f1d25da6-484c-46ab-af52-0cde7b5ef2d5/scratchpad/affected1.log |
| E052 | session:f1d25da6-484c-46ab-af52-0cde7b5ef2d5#44 |
| E053 | /tmp/claude-1654/-work-worktrees-task-eab3a4fc/f1d25da6-484c-46ab-af52-0cde7b5ef2d5/scratchpad/affected2.log |
| E054 | session:f1d25da6-484c-46ab-af52-0cde7b5ef2d5#46 |
| E055 | /tmp/claude-1654/-work-worktrees-task-eab3a4fc/f1d25da6-484c-46ab-af52-0cde7b5ef2d5/scratchpad/guards.log |
| E056 | session:f1d25da6-484c-46ab-af52-0cde7b5ef2d5#42 |
| E057 | /tmp/claude-1654/-work-worktrees-task-eab3a4fc/f1d25da6-484c-46ab-af52-0cde7b5ef2d5/scratchpad/m7-build.log |
| E058 | session:f1d25da6-484c-46ab-af52-0cde7b5ef2d5#27 |
| E059 | /tmp/claude-1654/-work-worktrees-task-eab3a4fc/f1d25da6-484c-46ab-af52-0cde7b5ef2d5/scratchpad/m7-run.log |
| E060 | session:f1d25da6-484c-46ab-af52-0cde7b5ef2d5#29 |
| E061 | /tmp/claude-1654/-work-worktrees-task-eab3a4fc/f1d25da6-484c-46ab-af52-0cde7b5ef2d5/scratchpad/tip-build.log |
| E062 | session:f1d25da6-484c-46ab-af52-0cde7b5ef2d5#40 |
| E063 | /tmp/claude-1654/-work-worktrees-task-eab3a4fc/f1d25da6-484c-46ab-af52-0cde7b5ef2d5/scratchpad/v36.log |
| E064 | /tmp/claude-1654/-work-worktrees-task-99e23919/a939a397-770a-4902-a3fd-c67b404eb2d5/scratchpad/build.log |
| E065 | session:a939a397-770a-4902-a3fd-c67b404eb2d5#36 |
| E066 | /tmp/claude-1654/-work-worktrees-task-99e23919/a939a397-770a-4902-a3fd-c67b404eb2d5/scratchpad/build2.log |
| E067 | session:a939a397-770a-4902-a3fd-c67b404eb2d5#77 |
| E068 | /tmp/claude-1654/-work-worktrees-task-99e23919/a939a397-770a-4902-a3fd-c67b404eb2d5/scratchpad/buildM13.log |
| E069 | session:a939a397-770a-4902-a3fd-c67b404eb2d5#89 |
| E070 | /tmp/claude-1654/-work-worktrees-task-99e23919/a939a397-770a-4902-a3fd-c67b404eb2d5/scratchpad/buildM25.log |
| E071 | session:a939a397-770a-4902-a3fd-c67b404eb2d5#124 |
| E072 | /tmp/claude-1654/-work-worktrees-task-99e23919/a939a397-770a-4902-a3fd-c67b404eb2d5/scratchpad/runA.log |
| E073 | session:a939a397-770a-4902-a3fd-c67b404eb2d5#63 |
| E074 | session:a939a397-770a-4902-a3fd-c67b404eb2d5#65 |
| E075 | /tmp/claude-1654/-work-worktrees-task-99e23919/a939a397-770a-4902-a3fd-c67b404eb2d5/scratchpad/runB1.log |
| E076 | session:a939a397-770a-4902-a3fd-c67b404eb2d5#79 |
| E077 | /tmp/claude-1654/-work-worktrees-task-99e23919/a939a397-770a-4902-a3fd-c67b404eb2d5/scratchpad/runB2.log |
| E078 | session:a939a397-770a-4902-a3fd-c67b404eb2d5#91 |
| E079 | /tmp/claude-1654/-work-worktrees-task-99e23919/a939a397-770a-4902-a3fd-c67b404eb2d5/scratchpad/runB2b.log |
| E080 | /tmp/claude-1654/-work-worktrees-task-99e23919/a939a397-770a-4902-a3fd-c67b404eb2d5/scratchpad/runC1017.log |
| E081 | /tmp/claude-1654/-work-worktrees-task-99e23919/a939a397-770a-4902-a3fd-c67b404eb2d5/scratchpad/runM13.log |
| E082 | /tmp/claude-1654/-work-worktrees-task-99e23919/a939a397-770a-4902-a3fd-c67b404eb2d5/scratchpad/runM25.log |
| E083 | /tmp/claude-1654/-work-worktrees-task-99e23919/a939a397-770a-4902-a3fd-c67b404eb2d5/scratchpad/runRed.log |
| E084 | /tmp/claude-1654/-work-worktrees-task-f35f1237/e88bddc5-39ff-42f9-8c2a-f0e0a814beb0/scratchpad/build.log |
| E085 | session:e88bddc5-39ff-42f9-8c2a-f0e0a814beb0#32 |
| E086 | /tmp/claude-1654/-work-worktrees-task-f35f1237/e88bddc5-39ff-42f9-8c2a-f0e0a814beb0/scratchpad/run-clg1.log |
| E087 | /tmp/claude-1654/-work-worktrees-task-f35f1237/e88bddc5-39ff-42f9-8c2a-f0e0a814beb0/scratchpad/run-clg2.log |
| E088 | /tmp/claude-1654/-work-worktrees-task-f35f1237/e88bddc5-39ff-42f9-8c2a-f0e0a814beb0/scratchpad/run-clg3.log |
| E089 | /tmp/claude-1654/-work-worktrees-task-f35f1237/e88bddc5-39ff-42f9-8c2a-f0e0a814beb0/scratchpad/run-guards.log |
| E090 | /tmp/claude-1654/-work-worktrees-task-f35f1237/e88bddc5-39ff-42f9-8c2a-f0e0a814beb0/scratchpad/run-neighbours.log |
| E091 | /tmp/claude-1654/-work-worktrees-task-f35f1237/e88bddc5-39ff-42f9-8c2a-f0e0a814beb0/scratchpad/run-nightly.log |
| E092 | /tmp/claude-1654/-work-worktrees-task-f35f1237/e88bddc5-39ff-42f9-8c2a-f0e0a814beb0/scratchpad/run-removal.log |
| E093 | /tmp/claude-1654/-work-worktrees-task-f35f1237/e88bddc5-39ff-42f9-8c2a-f0e0a814beb0/scratchpad/run-reply.log |
| E094 | /tmp/claude-1654/-work-worktrees-task-f35f1237/e88bddc5-39ff-42f9-8c2a-f0e0a814beb0/scratchpad/run-s4a.log |
| E095 | /tmp/claude-1654/-work-worktrees-task-f35f1237/e88bddc5-39ff-42f9-8c2a-f0e0a814beb0/scratchpad/run-worktree.log |
| E096 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A0.log |
| E097 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A1.log |
| E098 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#86 |
| E099 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A10.log |
| E100 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A10b.log |
| E101 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A11.log |
| E102 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A12.log |
| E103 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A2.log |
| E104 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A3.log |
| E105 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A4a.log |
| E106 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A4b.log |
| E107 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A4c.log |
| E108 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A5.log |
| E109 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A6.log |
| E110 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A7.log |
| E111 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A8.log |
| E112 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-A9.log |
| E113 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-base-run.log |
| E114 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#57 |
| E115 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-mutant-build.log |
| E116 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#64 |
| E117 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-none.log |
| E118 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#74 |
| E119 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-restored-build.log |
| E120 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#118 |
| E121 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/A-restored-run.log |
| E122 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-B11b-c1360.log |
| E123 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#185 |
| E124 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-B11b-detail.log |
| E125 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#180 |
| E126 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-B11b-retry.log |
| E127 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#172 |
| E128 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-base-build.log |
| E129 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#84 |
| E130 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-base.log |
| E131 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#92 |
| E132 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B1.log |
| E133 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B10.log |
| E134 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B11.log |
| E135 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B11b.log |
| E136 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#149 |
| E137 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B2.log |
| E138 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B3.log |
| E139 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B4.log |
| E140 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B5.log |
| E141 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B6a.log |
| E142 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B6b.log |
| E143 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B6c.log |
| E144 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B7a.log |
| E145 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B7b.log |
| E146 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B7c.log |
| E147 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B8a.log |
| E148 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B8b.log |
| E149 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B9a.log |
| E150 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B9b.log |
| E151 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-B9c.log |
| E152 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-m-none.log |
| E153 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-mutant-build.log |
| E154 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#112 |
| E155 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#120 |
| E156 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-mutant-build3.log |
| E157 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#162 |
| E158 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#166 |
| E159 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-restored-build.log |
| E160 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#201 |
| E161 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-restored.log |
| E162 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#203 |
| E163 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/B-scan-survivors.log |
| E164 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#198 |
| E165 | /tmp/claude-1654/-work-worktrees-task-e24018e3/85d5a836-b473-4d06-9ec5-6243e857e53b/scratchpad/ev/base-build-sr.log |
| E166 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#49 |
| E167 | /tmp/claude-1654/-work-worktrees-task-122f2dcf/26b0fc76-16cc-426d-a53b-830f1d03ed26/scratchpad/G1.log |
| E168 | /tmp/claude-1654/-work-worktrees-task-122f2dcf/26b0fc76-16cc-426d-a53b-830f1d03ed26/scratchpad/G2.log |
| E169 | /tmp/claude-1654/-work-worktrees-task-122f2dcf/26b0fc76-16cc-426d-a53b-830f1d03ed26/scratchpad/G3.log |
| E170 | /tmp/claude-1654/-work-worktrees-task-122f2dcf/26b0fc76-16cc-426d-a53b-830f1d03ed26/scratchpad/G4.log |
| E171 | /tmp/claude-1654/-work-worktrees-task-122f2dcf/26b0fc76-16cc-426d-a53b-830f1d03ed26/scratchpad/G5.log |
| E172 | /tmp/claude-1654/-work-worktrees-task-122f2dcf/26b0fc76-16cc-426d-a53b-830f1d03ed26/scratchpad/G6.log |
| E173 | /tmp/claude-1654/-work-worktrees-task-122f2dcf/26b0fc76-16cc-426d-a53b-830f1d03ed26/scratchpad/L2.log |
| E174 | /tmp/claude-1654/-work-worktrees-task-122f2dcf/26b0fc76-16cc-426d-a53b-830f1d03ed26/scratchpad/L3.log |
| E175 | /tmp/claude-1654/-work-worktrees-task-122f2dcf/26b0fc76-16cc-426d-a53b-830f1d03ed26/scratchpad/land-build.log |
| E176 | session:26b0fc76-16cc-426d-a53b-830f1d03ed26#91 |
| E177 | /tmp/claude-1654/-work-worktrees-task-e778bd07/82366522-a261-49ee-869e-3f9ad28cf4e2/scratchpad/G1.log |
| E178 | session:82366522-a261-49ee-869e-3f9ad28cf4e2#64 |
| E179 | /tmp/claude-1654/-work-worktrees-task-e778bd07/82366522-a261-49ee-869e-3f9ad28cf4e2/scratchpad/GUARD.log |
| E180 | session:82366522-a261-49ee-869e-3f9ad28cf4e2#71 |
| E181 | /tmp/claude-1654/-work-worktrees-task-e778bd07/82366522-a261-49ee-869e-3f9ad28cf4e2/scratchpad/SCAN.log |
| E182 | session:82366522-a261-49ee-869e-3f9ad28cf4e2#77 |
| E183 | session:82366522-a261-49ee-869e-3f9ad28cf4e2#80 |
| E184 | /tmp/claude-1654/-work-worktrees-task-e778bd07/82366522-a261-49ee-869e-3f9ad28cf4e2/scratchpad/build1.log |
| E185 | session:82366522-a261-49ee-869e-3f9ad28cf4e2#42 |
| E186 | /tmp/claude-1654/-work-worktrees-task-e778bd07/82366522-a261-49ee-869e-3f9ad28cf4e2/scratchpad/build2.log |
| E187 | session:82366522-a261-49ee-869e-3f9ad28cf4e2#56 |
| E188 | /tmp/claude-1654/-work-worktrees-task-e778bd07/82366522-a261-49ee-869e-3f9ad28cf4e2/scratchpad/r-B10.log |
| E189 | /tmp/claude-1654/-work-worktrees-task-e778bd07/82366522-a261-49ee-869e-3f9ad28cf4e2/scratchpad/r-B11b.log |
| E190 | /tmp/claude-1654/-work-worktrees-task-e778bd07/82366522-a261-49ee-869e-3f9ad28cf4e2/scratchpad/r-B5.log |
| E191 | /tmp/claude-1654/-work-worktrees-task-e778bd07/82366522-a261-49ee-869e-3f9ad28cf4e2/scratchpad/r-B6a.log |
| E192 | /tmp/claude-1654/-work-worktrees-task-e778bd07/82366522-a261-49ee-869e-3f9ad28cf4e2/scratchpad/r-B6b.log |
| E193 | /tmp/claude-1654/-work-worktrees-task-e778bd07/82366522-a261-49ee-869e-3f9ad28cf4e2/scratchpad/r-distill.log |
| E194 | session:82366522-a261-49ee-869e-3f9ad28cf4e2#82 |
| E195 | /tmp/claude-1654/-work-worktrees-task-e778bd07/82366522-a261-49ee-869e-3f9ad28cf4e2/scratchpad/r-off.log |
| E196 | session:82366522-a261-49ee-869e-3f9ad28cf4e2#46 |
| E197 | /work/worktrees/task-73c027ae/.antiphon/checkpoints/20261105-024432-7f38/executor.log |
| E198 | /work/worktrees/task-23889c9b/.antiphon/c1717/build1.log |
| E199 | session:7b8ee554-9f75-4dfe-9534-88696700e751#118 |
| E200 | /work/worktrees/task-23889c9b/.antiphon/c1717/build2.log |
| E201 | session:7b8ee554-9f75-4dfe-9534-88696700e751#145 |
| E202 | /work/worktrees/task-23889c9b/.antiphon/c1717/build3.log |
| E203 | session:7b8ee554-9f75-4dfe-9534-88696700e751#211 |
| E204 | /work/worktrees/task-23889c9b/.antiphon/c1717/build4.log |
| E205 | session:7b8ee554-9f75-4dfe-9534-88696700e751#218 |
| E206 | /work/worktrees/task-23889c9b/.antiphon/c1717/build5.log |
| E207 | session:7b8ee554-9f75-4dfe-9534-88696700e751#292 |
| E208 | /work/worktrees/task-23889c9b/.antiphon/c1717/build6.log |
| E209 | session:7b8ee554-9f75-4dfe-9534-88696700e751#364 |
| E210 | /work/worktrees/task-23889c9b/.antiphon/c1717/devrun1.log |
| E211 | session:7b8ee554-9f75-4dfe-9534-88696700e751#150 |
| E212 | /work/worktrees/task-23889c9b/.antiphon/c1717/devrun2.log |
| E213 | session:7b8ee554-9f75-4dfe-9534-88696700e751#183 |
| E214 | /work/worktrees/task-23889c9b/.antiphon/c1717/devrun3.log |
| E215 | session:7b8ee554-9f75-4dfe-9534-88696700e751#220 |
| E216 | /work/worktrees/task-23889c9b/.antiphon/c1717/devrun4.log |
| E217 | session:7b8ee554-9f75-4dfe-9534-88696700e751#299 |
| E218 | /work/worktrees/task-23889c9b/.antiphon/c1717/devrun5.log |
| E219 | session:7b8ee554-9f75-4dfe-9534-88696700e751#368 |
| E220 | /work/worktrees/task-23889c9b/.antiphon/c1717/m15-build.log |
| E221 | /work/worktrees/task-23889c9b/.antiphon/c1717/m15-run.log |
| E222 | session:7b8ee554-9f75-4dfe-9534-88696700e751#425 |
| E223 | /work/worktrees/task-23889c9b/.antiphon/checkpoints/20261105-045925-c410/executor.log |
| E224 | /work/worktrees/task-23889c9b/.antiphon/checkpoints/20261105-053431-3ba6/executor.log |
| E225 | /work/worktrees/task-23889c9b/.antiphon/checkpoints/20261105-064158-fc9d/executor.log |
| E226 | session:7b8ee554-9f75-4dfe-9534-88696700e751#418 |
| E227 | /work/worktrees/task-23889c9b/.antiphon/checkpoints/20261105-074105-d4ef/executor.log |
| E228 | session:7b8ee554-9f75-4dfe-9534-88696700e751#440 |
| E229 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/A.build.log |
| E230 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/A.run.log |
| E231 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/B.build.log |
| E232 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/B.run.log |
| E233 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/C.build.log |
| E234 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/C.run.log |
| E235 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/D.build.log |
| E236 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/D.run.log |
| E237 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/E.build.log |
| E238 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/E.run.log |
| E239 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/control.build.log |
| E240 | session:7b8ee554-9f75-4dfe-9534-88696700e751#326 |
| E241 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/control.run.log |
| E242 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/control2.build.log |
| E243 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/mutlogs/control2.run.log |
| E244 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/red467.build.log |
| E245 | session:7b8ee554-9f75-4dfe-9534-88696700e751#180 |
| E246 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/red467.build2.log |
| E247 | session:7b8ee554-9f75-4dfe-9534-88696700e751#254 |
| E248 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/red467.c1017.log |
| E249 | session:7b8ee554-9f75-4dfe-9534-88696700e751#188 |
| E250 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/red467.witness.log |
| E251 | session:7b8ee554-9f75-4dfe-9534-88696700e751#192 |
| E252 | /tmp/claude-1654/-work-worktrees-task-23889c9b/7b8ee554-9f75-4dfe-9534-88696700e751/scratchpad/red467.witness3.log |
| E253 | session:7b8ee554-9f75-4dfe-9534-88696700e751#294 |
| E254 | session:7b8ee554-9f75-4dfe-9534-88696700e751#296 |
| E255 | /work/worktrees/task-c3ba1792/.antiphon/c1720-base-inode-witness.log |
| E256 | /work/worktrees/task-c3ba1792/.antiphon/c1720-final-stopped-wait.log |
| E257 | /work/worktrees/task-c3ba1792/.antiphon/checkpoints/20261105-044841-7f06/executor.log |
| E258 | /work/worktrees/task-c3ba1792/.antiphon/c1720-inode-isolation.log |
| E259 | /work/worktrees/task-c3ba1792/.antiphon/c1720-prefix-filter-probe.log |
| E260 | /work/worktrees/task-c3ba1792/.antiphon/c1720-red-build.log |
| E261 | session:99fce261-fb7b-4ec8-b78d-86c1d6a3d216#13 |
| E262 | /work/worktrees/task-c3ba1792/.antiphon/c1720-red.log |
| E263 | /work/worktrees/task-c3ba1792/.antiphon/c1720-unit-roster.log |
| E264 | /work/worktrees/task-727a57dd/.antiphon/merge72/build-serial.log |
| E265 | /work/worktrees/task-727a57dd/.antiphon/merge72/build.log |
| E266 | /work/worktrees/task-727a57dd/.antiphon/merge72/distillation-alone.log |
| E267 | /work/worktrees/task-727a57dd/.antiphon/merge72/g1-direct.log |
| E268 | /work/worktrees/task-727a57dd/.antiphon/merge72/seats-guards.log |
| E269 | /work/worktrees/task-727a57dd/.antiphon/merge72/wake-a.log |
| E270 | /work/worktrees/task-727a57dd/.antiphon/merge72/wake-b.log |
| E271 | /work/worktrees/task-d6e48431/.antiphon/checkpoints/20261105-055344-7aa1/executor.log |
| E272 | /tmp/claude-1654/-work-worktrees-task-d6e48431/20672362-0ce7-49ab-9ec6-f505b9850126/scratchpad/bootstrap.log |
| E273 | session:20672362-0ce7-49ab-9ec6-f505b9850126#46 |
| E274 | /tmp/claude-1654/-work-worktrees-task-d6e48431/20672362-0ce7-49ab-9ec6-f505b9850126/scratchpad/mutbuild.log |
| E275 | session:20672362-0ce7-49ab-9ec6-f505b9850126#71 |
| E276 | /tmp/claude-1654/-work-worktrees-task-d6e48431/20672362-0ce7-49ab-9ec6-f505b9850126/scratchpad/mutbuild2.log |
| E277 | session:20672362-0ce7-49ab-9ec6-f505b9850126#76 |
| E278 | /tmp/claude-1654/-work-worktrees-task-d6e48431/20672362-0ce7-49ab-9ec6-f505b9850126/scratchpad/run-MA.log |
| E279 | session:20672362-0ce7-49ab-9ec6-f505b9850126#82 |
| E280 | /tmp/claude-1654/-work-worktrees-task-d6e48431/20672362-0ce7-49ab-9ec6-f505b9850126/scratchpad/run-MB.log |
| E281 | session:20672362-0ce7-49ab-9ec6-f505b9850126#85 |
| E282 | /tmp/claude-1654/-work-worktrees-task-d6e48431/20672362-0ce7-49ab-9ec6-f505b9850126/scratchpad/run-control.log |
| E283 | session:20672362-0ce7-49ab-9ec6-f505b9850126#79 |
| E284 | /work/worktrees/task-c9ebcc1d/.antiphon/merge73/build.log |
| E285 | /work/worktrees/task-c9ebcc1d/.antiphon/merge73/tests.log |
| E286 | /work/worktrees/task-2e0375f1/.antiphon/checkpoints/20261105-061927-a414/executor.log |
| E287 | /tmp/claude-1654/-work-worktrees-task-2e0375f1/211e759f-27f6-4abc-b824-a1b47c947d20/scratchpad/mut-MA.log |
| E288 | session:211e759f-27f6-4abc-b824-a1b47c947d20#146 |
| E289 | /tmp/claude-1654/-work-worktrees-task-2e0375f1/211e759f-27f6-4abc-b824-a1b47c947d20/scratchpad/mut-MB1.log |
| E290 | /tmp/claude-1654/-work-worktrees-task-2e0375f1/211e759f-27f6-4abc-b824-a1b47c947d20/scratchpad/mut-MB2.log |
| E291 | /tmp/claude-1654/-work-worktrees-task-2e0375f1/211e759f-27f6-4abc-b824-a1b47c947d20/scratchpad/mut-MC.log |
| E292 | /tmp/claude-1654/-work-worktrees-task-2e0375f1/211e759f-27f6-4abc-b824-a1b47c947d20/scratchpad/mut-MD.log |
| E293 | /tmp/claude-1654/-work-worktrees-task-2e0375f1/211e759f-27f6-4abc-b824-a1b47c947d20/scratchpad/toolbuild.log |
| E294 | /work/worktrees/task-5c10955b/.antiphon/investigate-5c10955b/build.log |
| E295 | /work/worktrees/task-133255e3/.antiphon/checkpoints/20261105-062846-f673/executor.log |
| E296 | /work/evidence/task-133255e3/red-checkpoint-receipt/executor.log |
| E297 | /work/worktrees/task-133255e3/.antiphon/checkpoints/20261105-064536-90a4/executor.log |
| E298 | /work/evidence/task-133255e3/final-refused-receipt/executor.log |
| E299 | /work/worktrees/task-133255e3/.antiphon/checkpoints/20261105-065559-ab7a/executor.log |
| E300 | /work/evidence/task-133255e3/final-checkpoint-receipt/executor.log |
| E301 | /tmp/claude-1654/-work-worktrees-task-a2cfcdc1/99fce261-fb7b-4ec8-b78d-86c1d6a3d216/scratchpad/ev/build-head.log |
| E302 | /tmp/claude-1654/-work-worktrees-task-a2cfcdc1/99fce261-fb7b-4ec8-b78d-86c1d6a3d216/scratchpad/ev/build-mut.log |
| E303 | session:99fce261-fb7b-4ec8-b78d-86c1d6a3d216#227 |
| E304 | session:99fce261-fb7b-4ec8-b78d-86c1d6a3d216#229 |
| E305 | /tmp/claude-1654/-work-worktrees-task-a2cfcdc1/99fce261-fb7b-4ec8-b78d-86c1d6a3d216/scratchpad/ev/build-restore.log |
| E306 | session:99fce261-fb7b-4ec8-b78d-86c1d6a3d216#235 |
| E307 | /tmp/claude-1654/-work-worktrees-task-a2cfcdc1/99fce261-fb7b-4ec8-b78d-86c1d6a3d216/scratchpad/ev/green.log |
| E308 | session:99fce261-fb7b-4ec8-b78d-86c1d6a3d216#237 |
| E309 | /tmp/claude-1654/-work-worktrees-task-a2cfcdc1/99fce261-fb7b-4ec8-b78d-86c1d6a3d216/scratchpad/ev/iso.log |
| E310 | session:99fce261-fb7b-4ec8-b78d-86c1d6a3d216#217 |
| E311 | /tmp/claude-1654/-work-worktrees-task-a2cfcdc1/99fce261-fb7b-4ec8-b78d-86c1d6a3d216/scratchpad/ev/mut.log |
| E312 | session:99fce261-fb7b-4ec8-b78d-86c1d6a3d216#232 |
| E313 | /tmp/claude-1654/-work-worktrees-task-a2cfcdc1/99fce261-fb7b-4ec8-b78d-86c1d6a3d216/scratchpad/ev/runA.log |
| E314 | session:99fce261-fb7b-4ec8-b78d-86c1d6a3d216#191 |
| E315 | session:99fce261-fb7b-4ec8-b78d-86c1d6a3d216#210 |
| E316 | /tmp/claude-1654/-work-worktrees-task-a2cfcdc1/99fce261-fb7b-4ec8-b78d-86c1d6a3d216/scratchpad/ev/runB.log |
| E317 | session:99fce261-fb7b-4ec8-b78d-86c1d6a3d216#170 |
| E318 | /work/worktrees/task-71dacd6c/.antiphon/checkpoints/20261105-064052-db23/executor.log |
| E319 | /work/worktrees/task-71dacd6c/.antiphon/checkpoints/20261105-065751-d2a1/executor.log |
| E320 | /work/worktrees/task-0c0e9de1/.antiphon/checkpoints/20261105-064344-a371/executor.log |
| E321 | /work/worktrees/task-0c0e9de1/.antiphon/checkpoints/20261105-072103-537b/executor.log |
| E322 | /work/worktrees/task-0c0e9de1/.antiphon/checkpoints/20261105-073147-6648/executor.log |
| E323 | /work/worktrees/task-0c0e9de1/.antiphon/checkpoints/20261105-080747-833b/executor.log |
| E324 | /work/worktrees/task-c781f7f8/.antiphon/checkpoints/20261105-070816-521e/executor.log |
| E325 | /work/worktrees/task-c781f7f8/.antiphon/checkpoints/20261105-071839-2fb6/executor.log |
| E326 | /work/worktrees/task-c781f7f8/.antiphon/checkpoints/20261105-074036-79d5/executor.log |
| E327 | /work/worktrees/task-0cd23a4c/.antiphon/merge-74/base-debounce-build.log |
| E328 | /work/worktrees/task-0cd23a4c/.antiphon/merge-74/base-debounce-test.log |
| E329 | /work/worktrees/task-0cd23a4c/.antiphon/merge-74/base-evaluator-build.log |
| E330 | /work/worktrees/task-0cd23a4c/.antiphon/merge-74/main-tests.log |
| E331 | /work/worktrees/task-0cd23a4c/.antiphon/merge-74/messaging-tests.log |
| E332 | /work/worktrees/task-0cd23a4c/.antiphon/merge-74/solution-build.log |
| E333 | /work/evidence/review-47d31366/bootstrap.log |
| E334 | /work/evidence/review-47d31366/classification-probe.log |
| E335 | /work/evidence/review-47d31366/git-retention-probe.log |
| E336 | /work/evidence/task-133255e3/probe-build.log |
| E337 | /work/evidence/task-133255e3/receipt-green.log |
| E338 | /work/evidence/task-133255e3/receipt-red.log |
| E339 | /work/evidence/task-133255e3/tool-build.log |
| E340 | /work/evidence/task-ff3ef62e/runs/cpu-io-load-build.log |
| E341 | /work/evidence/task-ff3ef62e/runs/master-build.log |
| E342 | /work/evidence/task-ff3ef62e/runs/master-loaded-receipt/console.log |
| E343 | /work/evidence/task-ff3ef62e/runs/master-loaded-scheduler/console.log |
| E344 | /work/evidence/task-ff3ef62e/runs/master-normal-receipt/console.log |
| E345 | /work/evidence/task-ff3ef62e/runs/master-normal-scheduler/console.log |
| E346 | /work/evidence/task-ff3ef62e/runs/master-repeat20-build.log |
| E347 | /work/evidence/task-ff3ef62e/runs/master-starvation-proof.log |
| E348 | /work/evidence/task-ff3ef62e/runs/probe-build.log |
| E349 | /work/evidence/task-ff3ef62e/runs/rc-build.log |
| E350 | /work/evidence/task-ff3ef62e/runs/rc-loaded-receipt/console.log |
| E351 | /work/evidence/task-ff3ef62e/runs/rc-loaded-scheduler/console.log |
| E352 | /work/evidence/task-ff3ef62e/runs/rc-normal-receipt/console.log |
| E353 | /work/evidence/task-ff3ef62e/runs/rc-normal-scheduler/console.log |
| E354 | /work/evidence/task-ff3ef62e/runs/rc-repeat20-build.log |
| E355 | /work/evidence/task-ff3ef62e/runs/rc-starvation-proof-corrected.log |
| E356 | /work/evidence/task-ff3ef62e/runs/rc-starvation-proof.log |
| E357 | /tmp/a4933b4f-evidence/W1-exact.log |
| E358 | /tmp/a4933b4f-evidence/W1-pack.log |
| E359 | /tmp/a4933b4f-evidence/W5-exact.log |
| E360 | /tmp/a4933b4f-evidence/W5-pack.log |
| E361 | /tmp/a4933b4f-evidence/W7-exact.log |
| E362 | /tmp/a4933b4f-evidence/W7-pack.log |
| E363 | /tmp/a4933b4f-evidence/W8-exact.log |
| E364 | /tmp/a4933b4f-evidence/W8-pack.log |
| E365 | /tmp/a4933b4f-evidence/build-mutant.log |
| E366 | /tmp/a4933b4f-evidence/build-restored.log |
| E367 | /tmp/a4933b4f-evidence/build-switch.log |
| E368 | /tmp/a4933b4f-evidence/green-base-endpoint.log |
| E369 | /tmp/a4933b4f-evidence/green-control.log |
| E370 | /tmp/a4933b4f-evidence/green-restored.log |
| E371 | /tmp/antiphon-code-2ede815a/evidence/20261105-044507-244d/executor.log |
| E372 | /tmp/antiphon-code-2ede815a/evidence/20261105-052612-9d03/executor.log |
| E373 | /tmp/antiphon-code-2ede815a/native-red-build.log |
| E374 | /tmp/antiphon-code-2ede815a/native-red-tests.log |
| E375 | /tmp/antiphon-code-2ede815a/server-build.log |
| E376 | /work/evidence/code-27e2f0b5/fresh-runner-evidence.tar.gz :: .antiphon/checkpoints/20261105-051349-41b0/executor.log (extracted at .antiphon/investigation-a8d643be/27-fresh-runner-evidence-20261105-051349-41b0.log) |
| E377 | /work/evidence/code-27e2f0b5/historical-negative-evidence.tar.gz :: .antiphon/checkpoints/20261105-044522-956f/executor.log (extracted at .antiphon/investigation-a8d643be/27-historical-negative-evidence-20261105-044522-956f.log) |
| E378 | /work/evidence/code-27e2f0b5/primary-checkpoint-evidence.tar.gz :: .antiphon/checkpoints/20261105-044749-28df/executor.log (extracted at .antiphon/investigation-a8d643be/27-primary-checkpoint-evidence-20261105-044749-28df.log) |
| E379 | /work/evidence/code-27e2f0b5/primary-checkpoint-evidence.tar.gz :: .antiphon/checkpoints/20261105-045917-ce44/executor.log (extracted at .antiphon/investigation-a8d643be/27-primary-checkpoint-evidence-20261105-045917-ce44.log) |
| E380 | /work/evidence/code-27e2f0b5/server-and-refused-runner-evidence.tar.gz :: .antiphon/checkpoints/20261105-045917-ce44/executor.log (extracted at .antiphon/investigation-a8d643be/27-server-and-refused-runner-evidence-20261105-045917-ce44.log) |
| E381 | /tmp/11a6d8bb-evidence/checkpoint-raw.tar.gz :: .antiphon/checkpoints/20261105-034329-ccaf/executor.log (extracted at .antiphon/investigation-a8d643be/11a6-0-executor.log) |
| E382 | /work/evidence/review-47d31366/checkpoint-evidence.tar.gz :: .antiphon/checkpoints/20261105-012458-a112/executor.log |
| E383 | /tmp/antiphon-f059674d-evidence.tar.gz :: g71-evidence/master-batch-13-through-20-retry.log (followed by canceled master-batch-4-through-20.log) |
| E384 | /tmp/antiphon-f059674d-evidence.tar.gz :: g71-evidence/master-batch-4-through-12-retry.log |
| E385 | session:26b0fc76-16cc-426d-a53b-830f1d03ed26#29 |
| E386 | session:26b0fc76-16cc-426d-a53b-830f1d03ed26#33 |
| E387 | session:26b0fc76-16cc-426d-a53b-830f1d03ed26#49 |
| E388 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#66 |
| E389 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#71 |
| E390 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#73 |
| E391 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#127 |
| E392 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#129 |
| E393 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#134 |
| E394 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#137 |
| E395 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#142 |
| E396 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#144 |
| E397 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#147 |
| E398 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#149 |
| E399 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#154 |
| E400 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#156 |
| E401 | session:54caa9bd-bfda-4f30-b0b9-abf5cedce7a9#163 |
| E402 | session:cdd2a6bc-9da3-44de-9e22-5de74b784b6f#35 |
| E403 | session:85d5a836-b473-4d06-9ec5-6243e857e53b#103 |
| E404 | session:82366522-a261-49ee-869e-3f9ad28cf4e2#40 |
| E405 | session:e88bddc5-39ff-42f9-8c2a-f0e0a814beb0#23 |

## Appendix C. Task identity mapping

| Short ID | Full task ID | Session ID | Role |
|---|---|---|---|
| 0c0e9de1 | 0c0e9de1-aadc-4d41-8b7d-630795d383c0 | 8fbbaf34-05e6-4da9-95c5-9a2fd7387ab1 | Code |
| 0cd23a4c | 0cd23a4c-9851-4b35-a19b-ebaf22e0dcab | 64ba0577-9a4e-479d-ac33-349de0628fc9 | Merge |
| 11a6d8bb | 11a6d8bb-e139-46c6-b024-7bfbe0afb2b9 | 9152d5ff-b55a-4101-890d-acce25738137 | Review |
| 122f2dcf | 122f2dcf-d726-47c0-9638-954de585c8af | 26b0fc76-16cc-426d-a53b-830f1d03ed26 | Merge |
| 133255e3 | 133255e3-d409-49e8-a9ee-6d0709679e71 | 97750719-0398-42cd-a087-b0d3be9a8469 | Code |
| 1c4f8dd2 | 1c4f8dd2-38f5-47da-9be2-ef8e435e5ac1 | 54caa9bd-bfda-4f30-b0b9-abf5cedce7a9 | Code |
| 23889c9b | 23889c9b-0eed-4812-9779-addac45144ab | 7b8ee554-9f75-4dfe-9534-88696700e751 | Code |
| 27e2f0b5 | 27e2f0b5-6507-4424-91aa-a4d7e378d33d | 2e783cba-ed5a-4082-b6e5-a9adbd7e42d5 | Code |
| 2e0375f1 | 2e0375f1-53dd-4758-9ed3-b37414892d0d | 211e759f-27f6-4abc-b824-a1b47c947d20 | Review |
| 2ede815a | 2ede815a-1ca4-4597-885d-b7d2509e5b0e | ad1e4868-03bd-4254-9236-7c6eb8a077d3 | Code |
| 47d31366 | 47d31366-a695-444a-8498-120656596668 | f3140153-72fd-484c-83c8-c213453237e7 | Review |
| 5c10955b | 5c10955b-0618-4b90-9c51-d5d9f42de4f7 | 6b507db9-3759-4a83-a7a1-585bb0b6a7fa | Investigate |
| 6dfad742 | 6dfad742-ef1b-45d5-9f80-b7a1a690eb14 | d817f8fd-248f-42f9-a7a7-42c44c70e0a3 | Code |
| 71dacd6c | 71dacd6c-a637-440c-aff3-f8b7e242e7c7 | b35f3ac9-657f-408a-8025-8ccb4a49aa87 | Code |
| 727a57dd | 727a57dd-0a24-4056-91be-7f9a85c05fe7 | 90285bb8-498f-4096-893c-589399e20837 | Merge |
| 73c027ae | 73c027ae-8ab4-4e6d-a145-50d426f513ea | ecdcb907-12c3-49b1-a212-418e36c8b250 | Code |
| 99e23919 | 99e23919-bec3-4777-8ef1-e387c5e44a64 | a939a397-770a-4902-a3fd-c67b404eb2d5 | Review |
| a2cfcdc1 | a2cfcdc1-7571-4f1f-af5a-146d9ef28c92 | 99fce261-fb7b-4ec8-b78d-86c1d6a3d216 | Review |
| a4933b4f | a4933b4f-a694-48a8-a655-a1b82fb3b66f | 4adfad8b-df74-44fb-b83a-bf8d2dc87aa3 | Custom |
| ae6457ac | ae6457ac-0cce-4c0b-9e06-d6828793b5d2 | cdd2a6bc-9da3-44de-9e22-5de74b784b6f | Merge |
| c3ba1792 | c3ba1792-e318-4638-b9f2-b8222086960b | 25182523-844b-4b2d-a2d5-71e6b879d22b | Code |
| c781f7f8 | c781f7f8-e0d4-435a-8a37-cc71592aad17 | a0092f26-476f-4102-af4f-f10ac994d8b1 | Code |
| c9ebcc1d | c9ebcc1d-b220-4b15-98a5-28b875474f07 | abd0df69-da75-42fb-88f2-96cd837ec9b2 | Merge |
| d19080aa | d19080aa-d1a4-4705-873a-990763c26ccd | 482777ad-500f-425a-a574-b8e105fcab7e | Review |
| d6e48431 | d6e48431-33ea-4247-89d1-aa1f582a6238 | 20672362-0ce7-49ab-9ec6-f505b9850126 | Review |
| e24018e3 | e24018e3-553b-4f5d-a9ae-aa746c136267 | 85d5a836-b473-4d06-9ec5-6243e857e53b | Mutation |
| e778bd07 | e778bd07-462a-4519-b7d1-03b2925d11ea | 82366522-a261-49ee-869e-3f9ad28cf4e2 | Code |
| eab3a4fc | eab3a4fc-cfb8-4ea3-ab3d-97f6d31c8100 | f1d25da6-484c-46ab-af52-0cde7b5ef2d5 | Code |
| f059674d | f059674d-a3ed-4582-b09f-6a2dacd70fb0 | a5554f47-4346-458e-a3ce-e2af38f5a3a5 | Debug |
| f35f1237 | f35f1237-d903-4376-8dcc-a7500f01137f | e88bddc5-39ff-42f9-8c2a-f0e0a814beb0 | Merge |
| ff3ef62e | ff3ef62e-ca6c-4150-82ce-12966e3b0339 | 0e8b30dc-b53f-4d54-b50b-617295bd6264 | Investigate |

--- next stage ---
next: decide
handoff: Shared four-slot contention is confirmed; peak CPU/I/O attribution and safe capacity remain unresolved. Obtain outer broker logs/settings and time-aligned full-pool resource samples before choosing capacity or scheduling changes. Preserve separate long-test failures and operator-only host budget authority.
artifact: docs/investigations/2026-11-05-server2-build-slot-contention.md
