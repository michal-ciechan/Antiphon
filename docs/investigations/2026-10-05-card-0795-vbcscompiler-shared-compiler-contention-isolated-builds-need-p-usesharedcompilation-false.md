# Desktop CPU evidence — CARD-0795 context

2026-10-05; task `4de0a86d-ff56-4a01-9c31-a93b6a78f50f`; eight logical processors.

**Outcome: a later 60-second window identifies Roslyn's shared compiler at 67.72% of machine CPU. The original missing ~50% and a runaway/shared-contention mechanism are NOT confirmed.** The compiler started after the first measurement. No production process, configuration, exclusion, or source implementation was changed.

This task is not bound to a card (`summary.cardId`, `cardIdentifier`, `boardId` all null). The filename uses the existing relevant CARD-0795 title, rather than inventing a card number. Full board searches for `CPU` and `compiler` completed; CARD-0795 was read in full. It is already Backlog (id `e23191d9-c6ab-4513-9c95-710f8aab022f`), so no duplicate was filed. Its prior evidence concerns server2; this report adds desktop measurements without claiming the same defect is proved here.

## Provenance and method

Evidence root: **`C:\Antiphon\evidence\task-4de0a86d`**. Scripts and raw CSV/JSON/traces stay outside Git. Source inspected: `f41748999c285a6ea00a7b8bf486ec51ee92fa16`. Served `/api/version`: `0de12dac930ad52a235604c566ee643e71b21daa`, with `land-v2` and `operator-shutdown-v1` (`server-version.json`). Source citations refer to inspected HEAD; they do not establish activation. `SessionMessageQueueService.cs` differs between those revisions; the cited Windows CPU probe, landing Git helper and dispatcher hosted service do not.

Collection was foreground and bounded. No builds or tests were run. Installed tools: dotnet-trace 10.0.745401; dotnet-counters 9.0.661903. No installations were needed. Read-only Docker/SQL probes used existing images and SELECTs; no statistics reset. The explicitly authorized utility container used `--rm --pull never --pid=host --read-only --network none --cap-drop ALL alpine:3`; its executable names were retained without full command lines. There was no kill, restart, reconfiguration or exclusion change.

Three different windows must not be combined:

| Collection | UTC window | Result and limitation |
|---|---|---|
| Process watcher | 19:15:06–19:16:06 (~60.26 s) | 217 CIM polls; 672 new observed processes (11.15/s lower bound). Requested 250 ms cadence, observed mean cadence ~278 ms; processes shorter than a poll can escape. |
| First PDH collection | 19:15:10–19:16:25 | 60 requested one-second samples stretched to ~75 s by collection/serialization. Total 97.47%, privileged 50.70%, user 46.76%, interrupts 2.06%, DPC 0.75%; ~108,762 context switches/s and 356,792 system calls/s. Observer materially perturbed load. |
| PDH without full CIM watcher | 19:16:51–19:17:59 | 60 samples over ~68 s. Total 88.67%, privileged 44.39%, user 44.40%, interrupts 1.91%, DPC 0.51%. Dynamic process-instance rates were unusable. |
| Native cumulative process CPU | **19:19:27.980–19:20:27.984** | **60.004 s**, 61 snapshots, keyed by PID AND creation time. Ranked table below. A separate 12-second Git-only CIM probe overlapped part of this window, so PowerShell/WMI include observer work. |
| Later total-only PDH | 19:20:20–19:21:50 | 60 samples over ~90 s, mostly after native collection: total 99.90%, privileged 38.56%, user 62.89%, interrupts 1.78%, DPC 0.76%. One user sample is 127.97%; these split counters are anomalous and cannot be reconciled as exact accounting. |

PDH `% Processor Time` for a process is divided by eight to express machine percent. Recycled names/PIDs caused implausible individual rates: the second PDH aggregation sums to **177.53%** of the machine (including node 88.73%). It is rejected, not a CPU attribution. Evidence explicitly demonstrates reuse: the first watcher has **PID 20204 = node.exe**, created 2026-10-04 16:50:17 local; native later has **PID 20204 = VBCSCompiler.exe**, created 2026-10-05 20:19:01 local. New/invalid PDH rows (`Status=2147485649`) were excluded, but that alone does not validate surviving alias rates.

`native-process-cpu.ps1` uses local inherited PowerShell and `NtQuerySystemInformation(SystemProcessInformation)` to read cumulative user/kernel ticks without WMI. `native-analysis.py` differences counters for `(PID, creation time)`; newly observed processes contribute their lifetime ticks. CPU percent = seconds / (60.003963 × 8) × 100. Processes born and exited between snapshots are absent, so the native sum is a lower bound, not a complete accounting of the original incident. Scripts and `native-process-cpu.csv` preserve the calculation.

## Ranked measured consumers

This table applies **only** to the native 60.004-second window. Kernel time is included in each process total; interrupts/DPC and containers must not be added again to these rows.

| Rank | Consumer | Machine CPU | Expected versus wasteful |
|---:|---|---:|---|
| 1 | VBCSCompiler.exe, PID 20204, parent 30212 | **67.72%** | Shared Roslyn compiler, ~5.42 cores. Active compilation versus contention/spin is unresolved. |
| 2 | vmmem, PID 15604 | 6.29% | WSL/VM work; guest/container snapshot does not explain every host cost. |
| 3 | All pwsh.exe | 3.24% | Mixed observer and other live work; not all task-owned. |
| 4 | WmiPrvSE.exe, PID 4120 | 3.24% | Includes observer cost; other callers not attributed. |
| 5 | All dotnet.exe | 3.07% | Build/tool activity, including server child PID 28784 (1.90%) and its child 28468 (0.90%). These exited before command classification; project/owner unknown. |
| 6 | MsMpEng.exe | 2.15% | Defender activity; scanned paths unknown. |
| 7 | Antiphon.Server.exe, PID 7968 | 1.85% | Production server work; varies substantially by window. |
| 8 | dcpctrl.exe | 1.65% | Aspire supervision; process watcher shows Docker children. |
| 9 | All svchost.exe | 1.13% | Windows services, not resolved individually. |
| 10 | System | 0.89% | System process CPU only; not synonymous with all kernel CPU. |
| 11 | All claude.exe | 0.84% | Live sessions. |
| 12 | com.docker.backend.exe | 0.73% | Docker host plumbing, outside container accounting. |
| 13 | Antiphon.SessionRunner.exe | 0.57% | Production session runtime. |
| — | Other observed processes | 3.79% | Browser/UI/PTY/Git and other services. |
| — | **Total observed process CPU** | **97.15%** | Includes observer work and vmmem accounting; not a claim that the original ~50% gap is solved. |

Compiler raw native rows: user ticks **500,937,500 → 3,716,406,250**, kernel **44,062,500 → 79,375,000** (100 ns units): **325.078125 CPU seconds** in 60.003963 wall seconds. Creation identity stays `134357015415721739`. Follow-up Get-Process confirmed executable/start time and total CPU ~715 seconds. Trace identifies SDK path `C:\Program Files\dotnet\sdk\10.0.300\Roslyn\bincore\VBCSCompiler.exe`. Its later 30-second trace contains waits (`WaitHandle.WaitOneNoCheck` 37.53%, `ManualResetEventSlim.Wait` 18.76%) and 43.71% unresolved exclusive time; it does not show a sustained runaway after the burst.

Shared compilation remains enabled according to **`docs/testing-and-build.md:756`**. **`Directory.Build.rsp:5`** specifies `-nodeReuse:false`; that is the observed repository build policy, not proof of which arguments launched this compiler. Compiler parent 30212 was gone before inspection. CARD-0795's reported server2 mechanism remains a relevant hypothesis, not a confirmed cause for this desktop burst.

## Short-lived processes and observer effect

`process-observations.csv` records 306 Git, 193 conhost, 19 Docker, 18 Python, 12 Bash, 11 Edge, 11 git-remote-https and smaller groups. There were **zero newly observed MSBuild or dotnet processes in this first window**; those later appeared outside it. Server PID 7968 directly parented **169 observed Git starts** (~2.8/s lower bound). A separate sanitized 12-second sample (`server-git-commands.csv`) caught 11 `rev-parse`, 3 `log`, 3 `status --porcelain` commands; full command lines were not stored or printed.

This proves Git subprocess activity, not waste. Landing identity explicitly issues multiple Git checks at **`server/Infrastructure/Git/LandingGit.cs:707`**; source cleanliness uses `status --porcelain=v1` at **`:388`**. Dispatcher cadence consumes `Delegation:PollIntervalSeconds` at **`server/Infrastructure/Orchestration/AgentTaskDispatcherHostedService.cs:35`**, default 5 at **`server/Application/Settings/DelegationSettings.cs:18`**. No evidence attributes all sampled Git children to that dispatcher or proves its interval is excessive.

First PDH estimates attributed 18.19% to PowerShell and 13.24% to WMI. These are perturbed-window estimates, not a baseline adjustment to subtract from the original snapshot. The original five-process snapshot omitted other processes and was not time-aligned with this investigation; it cannot be reconstructed from the brief alone. The native method avoids the full WMI sweep, although the short Git probe still contributes to its window.

## Server/runner trace and counters

Files: `server.nettrace`, `server-profile.speedscope.json`, `server-top-inclusive.txt`, `server-top-exclusive.txt`, `server-top-inclusive-verbose.txt`, `compiler.nettrace`, `compiler-top-inclusive.txt`, `compiler-top-exclusive.txt`; detailed full-symbol analysis in `detail-analysis.txt`.

Server trace requested 45 s with `--profile dotnet-sampled-thread-time --buffersize 32`; collection and rundown completed. This profile estimates **thread wall time**, including native waits, rather than an exclusive CPU budget. Top report: Missing Symbol 92.94% inclusive / 74.31% exclusive; ThreadPool worker 45.36% inclusive; Hangfire cancellation wait 13.68%; RandomAccess read callback 11.23%; Kafka Consume 5.46%; ManualResetEventSlim wait 4.55%. These waits cannot establish Hangfire/Kafka as CPU hot spots.

Full names expose small samples in `Antiphon.Server.Application.Services.SessionMessageQueueService` delivery/transcript-confirmation methods (~0.04–0.09% inclusive per frame), and `Antiphon.Server.Infrastructure.Git.LandingGit.IdentityAsync` (~0.044%). They prove those paths were active, not that they dominate CPU. No reliable CPU allocation among pollers, transcript tailers, Hangfire, EF, SignalR, channel pumps, delegation or runner sync is possible from this unresolved wall-time trace.

`server-runtime.csv`: 55 counter rows per metric during the requested 45-second collection plus completion/rundown; CPU mean 8.48%, maximum 17.86%; ThreadPool queue mean 5.07, maximum 91; 74 gen0 / 23 gen1 / 11 gen2 collections; allocation ~85.05 MB/s mean; summed GC pauses ~846.6 ms; 664 exception events. Counts do not identify exception causes. `runner-runtime.csv`: 45 rows, CPU mean 1.15%, maximum 4.26%; queue mean 0.78, maximum 7; 116 gen0 / 45 gen1 / 1 gen2; ~15.44 MB/s allocation and ~416.1 ms summed pauses. These precede the native compiler window and must not be substituted into its table.

`session-state-diagnostics.json` additionally records 15 live sessions, 260 cached sessions, 186,775 cache hits, 145 loads, zero load faults, 45,397 ingest calls, 5,394 committed rows, 40,179 duplicate batches. These are lifetime counters from a single point, not rates or proof of wasted CPU. The host CPU formula subtracts idle from kernel at **`src/Antiphon.SessionRunner/WindowsHostStatsProbe.cs:105`**; source inspection reveals no simple double-count-idle bug.

## Docker and Postgres

Both Ubuntu and docker-desktop are running WSL2 distros. Six Docker samples (`docker-samples.jsonl`) span ~60 seconds in the first window. Docker's percent is normalized to a single core; divide by eight:

| Container | Mean Docker CPU | Mean machine CPU | Range, Docker CPU |
|---|---:|---:|---:|
| antiphon-postgres | 31.69% | 3.96% | 12.26–44.80% |
| antiphon-redpanda | 3.62% | 0.45% | 0.41–19.57% |
| desktop-caddy-1 | 3.36% | 0.42% | 3.05–3.78% |
| windmill-desktop-worker | 2.48% | 0.31% | 1.91–3.07% |
| desktop-links-1 | 0 | 0 | 0 |

Container mean sum ~5.14% machine. It is not additive to vmmem. `docker-top-*.txt` records executable/PID/parent/CPU for the four active containers. Host utility top (`docker-host-top.txt`) shows 93% then 82% guest idle; `/initd` PID 16 shows 4% then 2% in its top display, alongside dockerd, containerd, Redpanda, Postgres, Windmill and Caddy. Two top snapshots do not produce a per-process 60-second VM budget. Later Ubuntu top showed 65.6% idle with 14.8% user, 10.9% system, 8.6% softirq; the original statement that Ubuntu was 94% idle is not a stable current fact. WSL distros share host VM resources; these samples do not isolate host overhead by distro.

`pg-before.csv` / `pg-after.csv` and `pg-deltas.json` give normalized query statistics without data values. Delta window: approximately 19:15:06–19:16:14 UTC. The captured top-100 SELECT set adds **21.04 s total execution time**, which is elapsed query time, not CPU time; concurrent executions and waits prevent conversion directly into machine CPU.

| Query id | Delta calls | Delta execution ms | Mean ms/call | Shape |
|---|---:|---:|---:|---|
| 2824069175592795718 | 60 | 15,120.87 | 252.02 | Full TranscriptEntries entities, session + non-null Text + one Kind + Sequence floor, ordered Sequence |
| 8388454389200601854 | 20 | 3,173.42 | 158.67 | Same, two Kinds |
| 6647405445446065752 | 170 | 536.31 | 3.16 | Transcript EXISTS with queued-message rules exclusion |
| -3921260883416737319 | 1 | 335.23 | 335.23 | Transcript projection for one session |

First two account for ~86.9% of captured SELECT execution time. `pg-top-cumulative.csv` records lifetime top queries; for the first query, 579,176 calls and ~61,614,742 ms cumulative time at the later read. Lifetime totals have no reset-time provenance here and are not a one-minute CPU attribution.

`pg-activity.csv`: one point-in-time snapshot has active and idle client backends; idle ones wait ClientRead, background launcher rows wait normally. No query text values or secrets were printed. `pg-transcript-indexes.csv` confirms an existing unique `(AgentSessionId, Sequence)` index plus UUID/API-error/end indexes; a missing index or runaway query is not established. Delivery confirmation's inspected code queries sequence/kind and projects Text at **`server/Application/Services/SessionMessageQueueService.cs:4127`**, while the dominant measured query projects full entities, so it is not conclusively assigned to this exact method.

## Defender

`defender-status.json`: AMRunningMode Normal, AntivirusEnabled and RealTimeProtectionEnabled true. Last quick scan 2026-10-01 09:35:18–09:42:38 local; full-scan timestamps null. None proves which files are being scanned now.

`defender-preferences.json`: each exclusions field returns **“N/A: Must be an administrator to view exclusions”**. Therefore the current exclusion list, and whether `C:\Antiphon\worktrees`, `C:\src\Antiphon`, `bin-*` or `.antiphon\checkpoints` is scanned/excluded, are **unknown**. This session did not elevate, inspect protected stores or add exclusions. Defender measured 8.55% in the observer-heavy first PDH window and 2.15% in the native window; activity varies and cannot be blamed for the whole gap.

## Remaining uncertainties / what resolves them

1. Recover a time-aligned complete process observation from the original 73–94% incident, or repeat it using cumulative PID+creation-time sampling without CIM; compiler PID 20204 did not exist until 19:19:01 UTC, after the first window.
2. Link the compiler burst to its launching build and exact arguments/host lease, then observe whether it stays CPU-active after builds finish. The departed parent and later waiting trace leave legitimate compilation and shared contention both live.
3. Capture bounded CPU-on-processor stacks with native/managed symbol resolution during the server's actual hot interval. The supplied EventPipe profile has too much unresolved/native wait time to assign a dominant subsystem.
4. An administrator's read-only Defender exclusion/performance evidence must identify file paths before exclusion benefits can be quantified.
5. Identify the two transcript-query callers and collect parameter-free query-plan/buffer evidence in the hot interval; stored statement totals already establish their relative elapsed-time weight, not a mechanism or an index remedy.
6. A simultaneous VM `/proc` cumulative per-process sample and host cumulative window is needed to partition vmmem between guest computation and host plumbing. The current top snapshots cannot do it.

### Not done, noted

Candidate follow-up ideas only, without design or implementation: evaluate CARD-0795's `-p:UseSharedCompilation=false` against the enabled policy at `docs/testing-and-build.md:756`; consider server2 placement for eligible builds through existing runner selection; assess narrowly scoped generated-output Defender exclusions (`bin-*` and `.antiphon\checkpoints` under the two named roots) only after read-only scan evidence; assess `Delegation:PollIntervalSeconds` and the dominant transcript-query paths only after caller/CPU attribution—no setting, source, index, routing or exclusion was changed.

--- next stage ---
next: investigate
handoff: Resolve the original CPU gap with simultaneous native PID+creation-time and symbolized CPU-stack sampling; correlate the compiler burst with its build/lease, identify the dominant transcript-query callers, and obtain administrator read-only Defender evidence. CARD-0795 is existing context; desktop runaway contention remains unconfirmed.
artifact: docs/investigations/2026-10-05-card-0795-vbcscompiler-shared-compiler-contention-isolated-builds-need-p-usesharedcompilation-false.md
