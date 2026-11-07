# Desktop server and session runner CPU trace (2026-11-07)

Operator question (2026-11-07): "Can we also do another dotnet trace of what desktop server and session
runner are doing in a 5 minute window? To optimise calls for CPU".

**Verdict.** Both processes were traced together for 5 minutes (12:16:35Z–12:21:35Z) while the desktop
did normal work. In that window the **server used 1.30 cores** on average. Its lifetime average is
1.16 cores. The **session runner used 0.23 cores**, against a lifetime average of 0.16. Three findings
are new and each is above 10% of its process:

1. **Server, ~32%:** `CheckCompactionContinuationService.LoadFactsAsync` (CARD-0079 discovery) loads a
   Check seat session's whole transcript. It then runs `string.Contains` for every user prompt against
   three tokens per Check task in that session. 28.6% of the server's active samples are in
   `SpanHelpers.IndexOf` under that one lambda.
2. **Server, ~15%:** worktree retirement content binding. `TaskWorktreeRetirementService` →
   `GuardedWorktreeRemoval` takes a SHA-256 of every regular file and stats the nested repositories.
3. **Runner, ~26%:** each bound `TranscriptTailer` polls every 300 ms. On every poll it opens the
   transcript file to check its identity and runs two more stats, even when the file has not grown.

The runner also spends ~20% in `HostStatsSamplerService`. Of that, 10% is a system-wide process snapshot
taken once for each sampled process. Another ~17% is the unbound-tailer locate scan already covered by
CARD-1624, which is in Review and is not yet in the running build. No product code was changed.

## Earlier traces and what changed in the method

| When | Tool | Finding | Outcome |
|---|---|---|---|
| 2026-09-14 (CARD-0525) | `dotnet-trace collect --profile dotnet-sampled-thread-time`, 15 s per process, nettrace + speedscope in `logs/perf-traces/2026-09-14-cpu-investigation/` (main checkout, untracked) | 99.98% of samples were `UNMANAGED_CODE_TIME`, read as "threads parked in native waits" | Open investigation card |
| 2026-10-31 (CARD-1622, CARD-1624) | PerfView ETW CPU sampling, 30 s | Server: 31% in `AgentTaskService.ListAsync` cost walk. Runner: 40% in unbound `TranscriptTailer.LocateAsync`, plus 12% in the CPU watchdog | CARD-1622 Done (no longer visible in this trace). CARD-1624 in Review |

This run reuses the CARD-0525 tool, `dotnet-trace` with the `dotnet-sampled-thread-time` profile, and
the CARD-1622/1624 report format: CPU share tables, the call path, the root cause and a suggested fix.
The analysis method is different, and that difference explains the CARD-0525 result:

- **The runtime's managed/external sample flag cannot tell CPU from waiting.** In both traces,
  1,729,265 of 1,729,286 runner samples and 1,703,569 of 1,703,610 server samples carry type
  1 ("external"). Every sample suspends the runtime, which puts each thread into preemptive mode. CARD-0525's
  "99.98% `UNMANAGED_CODE_TIME`" is this artifact. It did not show that the threads were idle.
- In this run, idle is decided by the **leaf frame** instead. A sample is idle if its leaf is a wait
  primitive (`LowLevelLifoSemaphore`, `WaitHandle`, `Monitor.Wait`, `ManualResetEventSlim`, `SemaphoreSlim`,
  `Sleep`), the IOCP poller, a parked pool worker, Kestrel's heartbeat, a synchronous pipe `ReadFile`,
  or a blocking `Confluent.Kafka` consume or poll. Every other sample is **active**: on CPU, or runnable,
  or inside a blocking file syscall.
- Check against the OS view: active server samples come to 492 thread-seconds, against about 390
  CPU-seconds from the OS. Active runner samples come to 164 thread-seconds, against about 63 CPU-seconds.
  The machine was saturated, so the active count also includes time spent runnable and waiting, and time
  in blocking file syscalls. **Shares are of active samples. Treat them as process-CPU shares, with
  file-syscall-heavy paths as upper bounds.**
  A PerfView ETW trace would give true CPU and kernel attribution. Use it to confirm the runner items
  before or while fixing them.

## Processes and window

| | Server | Session runner |
|---|---|---|
| PID | 330800 (`Antiphon.Server.exe`) | 153560 (`Antiphon.SessionRunner.exe`) |
| Identified by | `Antiphon.Server.exe` under the Aspire DCP launcher. It listens on 53739, which dcpctrl (PID 212684) proxies to 17202 | listener on 127.0.0.1/::1:17204 |
| Command line | `"C:\src\Antiphon\server\bin\Debug\net9.0\Antiphon.Server.exe"` (no arguments, nothing to redact) | `C:\src\Antiphon\src\Antiphon.SessionRunner\bin\Debug\net9.0\Antiphon.SessionRunner.exe --urls http://localhost:17204` |
| Started | 2026-11-06 22:01:16 local | 2026-11-03 07:54:56 local |
| CPU seconds at start of window | 59,715 s (lifetime avg ≈ 1.16 cores) | 58,632 s (lifetime avg ≈ 0.16 cores) |
| Build | **Debug** (JIT optimisation off for repo code) | **Debug** |
| CPU in window, % of one core (5 s samples: avg / median / max) | 130.5 / 126.3 / 267.4 | 23.0 / 21.8 / 95.5 |
| Working set (min–max) | 1,168–1,536 MB | 694–878 MB |
| Threads (min–max) | 67–75 | 20–27 |
| Handles (min–max) | 1,204–1,283 | 667–711 |
| GC in window | 124 GCs (gen0 58, gen1 12, **gen2 54**). 8.7 s GC start→stop, 2.0 s GC suspension, max pause 855 ms | 592 GCs (gen0 34, **gen1 550**, gen2 8). 2.8 s GC start→stop, max pause 451 ms |
| Sampled threads | 48 | 40 |

**Machine during the window** (8 logical CPUs): 93% average total CPU (peaks at 100%); processor queue length
averaged 24 and peaked at 80. Other CPU users, in average cores: `Antiphon.Tests` 0.72 (the **live
Windows qualification run**, group 64, out of scope), `pwsh` 0.51 (qualification harness plus a recursive
file search this task ran for the earlier traces; it overlapped the first ~3.5 minutes and was stopped), `dcpctrl`
0.27 (Aspire proxy for 17202), `com.docker.backend` 0.18, `claude` 0.16, everything else under 0.06 each.
`dotnet-trace` itself used 2.2 CPU-seconds. The window was normal load with a saturated host, so
timings include run-queue wait. Server2 tasks were running remotely.

GC suspensions with reason 0 (98,145 in the runner and 58,189 in the server) are the profiler's own
per-sample suspensions and are excluded from the GC figures above. Server gen2 ran every ~5.6 s, which
points to large-object churn. The transcript text lists in finding 1 are the likely source (not proven).

## Server: where the active samples go (95,226 active of 1,703,610)

Area unions; a sample is counted once per row; rows overlap:

| Share | Area (repo frames on the stack) |
|---:|---|
| **31.97%** | S1 `CheckCompactionContinuationService` (`LoadFactsAsync` and discovery) |
| **15.02%** | S2 worktree retirement content binding (`GuardedWorktreeRemoval`, `WorktreeNoFollowDelete`, `TaskWorktreeRetirementService`, `WorktreeNestedRepositories`) |
| **12.10%** | S4 runner HTTP reads plus phone-home JSON (`SessionRunnerHttpClient.ReadJsonAsync`, `PhoneHomeFraming`) |
| 10.58% | S8 `LandingGit` process execution (any path; overlaps S2 and S5) |
| 9.33% | S3 `AgentTaskLandNotificationService.ReconcileAsync` / `LandNoteReceipt` |
| 5.50% | S9 `AppDbContext.SaveChangesAsync` |
| 5.20% | S5 delegate check git status probe (`DelegateCheckProbe` → `GitWorkspaceService` → `InProcessGitStatus`) |
| 4.05% | S7 worktree metadata and health file reads (`WorktreeManager.LoadMetadataRecordsAsync`, `WorktreeHealthService`) |
| 2.85% | S6 `TaskReportTurnSelector` |
| 0.01% | Hangfire (dispatcher threads are idle; this confirms the CARD-0525 side question) |

Library areas, inclusive: EF Core 65.8%, Npgsql 65.7% (most user code runs as continuations after an
Npgsql read completes, so these overlap almost everything); LINQ 37.6%; process spawn and I/O 15.6%;
file I/O 10.8%; HttpClient 8.6%; System.Text.Json 7.3%; reflection 6.2%; LibGit2Sharp 2.8%; Kestrel 2.6%;
Regex 0.05%; logging 0.09%.

Top exclusive (leaf) frames: `SpanHelpers.IndexOf` 28.56%, `FileSystem.FillAttributeInfo` 5.68%,
`WorktreeNoFollowDelete.TryStamp` 3.66%, `WorktreeNoFollowDelete.WindowsRegularFileHash` 3.15%, `Buffer._Memmove`
2.57%, `BCrypt.BCryptHashData` 2.07%, `Utf8JsonReader.ConsumeStringAndValidate` 1.79%,
`TaskReportTurnSelector.LastBefore` 1.78%, STJ `ObjectWithParameterizedConstructorConverter.ReadConstructorArguments…` 1.59%,
`Winsock.WSASend` 1.58%, `FileSystemEnumerator.MoveNext` 1.54%, `RelationalDataReader.DisposeAsync` 1.33%,
`WSARecv` 1.32%, `RuntimeMethodInfo.get_ContainsGenericParameters` 1.28%, `Kernel32.CreateProcess` 1.27%,
`DynamicMethod.CreateDelegate` 1.24%, `DateTime.UtcNow` 1.13%, `CreateFilePrivate` 1.08%,
`LibGit2Sharp.RepositoryStatus..ctor` 1.02%. The full top-30 lists are in the untracked
`server-report.md` (see Raw data).

Top repo frames, inclusive: `CheckCompactionContinuationService.LoadFactsAsync` 31.96%, `LandingGit.ExecuteAsync`
10.54%, `AgentTaskLandNotificationService.ReconcileAsync` 9.24%, `SessionRunnerHttpClient.SendReadAsync` 6.47%,
`GuardedWorktreeRemoval.RemoveDirectoryAsync` 6.26%, `AppDbContext.SaveChangesAsync` 5.50%,
`WorktreeNoFollowDelete.RegularFileHash` 5.38%, `GuardedWorktreeRemoval.BindContentAsync` 5.31%,
`SessionRunnerHttpClient.ReadJsonAsync` 5.25%, `InProcessGitReads.TryAnswerSilently` 4.92%,
`TaskWorktreeRetirementService.LegacyReleaseAdmissionAsync` 4.83%, `GitWorkspaceService.RunCoreAsync` 4.75%.

Threads: three threads are permanently parked in `Confluent.Kafka` (producer poll,
`InboundUnconsumedEventConsumer`, and `KafkaAntiphonMessagingConsumer.PollAsync`) and are counted as idle.
The rest are pool workers, whose stacks start from Npgsql read continuations or Kafka-consumer callbacks.
There were no named managed threads apart from the finalizer.

### Hottest server stacks, collapsed to repo frames

| Share | Stack (outer > inner ==> leaf) |
|---:|---|
| 28.55% | `CheckCompactionContinuationService.LoadFactsAsync > <LoadFactsAsync>b__1 > <LoadFactsAsync>b__9` ==> `String.Contains` → `SpanHelpers.IndexOf` |
| 1.78% | `TaskReportTurnSelector.SelectAsync > LastBefore` (self) |
| 1.77% | `LandingGit.RunAsync > GuardedWorktreeRemoval.TrackedNamesAsync > BindContentAsync > EntryManifest > WorktreeNoFollowDelete.RegularFileHash > WindowsRegularFileHash` (self, plus 1.69% in `SHA256.HashData`) |
| 1.59% | `AdmittedReadHandler > LiveTimingHttpHandler > SessionRunnerHttpClient.SendReadAsync > ReadJsonAsync<T>` ==> STJ constructor-argument converter |
| 1.33% | (no repo frame) ==> `RelationalDataReader.DisposeAsync` |
| 1.33% | `DelegateCheckProbe.GatherGitAsync > GitWorkspaceService.GetWorkingTreeCountsAsync > InProcessGitStatus.StatusEntries > WalkReadable` ==> `Directory.Exists`/`FillAttributeInfo` |
| 1.23% | `TaskWorktreeRetirementService.LegacyReleaseAdmissionAsync > GuardedWorktreeRemoval.LiveAuthorizationAsync > RemoveDirectoryAsync > CustodyOf > WorktreeNestedRepositories.Find > Attributes` ==> `File.GetAttributes` |
| 0.94% | `WorktreeManager.LoadMetadataRecordsAsync` ==> `File.ReadAllTextAsync`/`CreateFile` |
| 0.85% | `SessionRunnerHttpClient.ReadJsonAsync<T>` ==> `Utf8JsonReader.ConsumeStringAndValidate` |
| 0.71% | `AgentTaskLandNotificationService.ReconcileAsync` ==> `EntityEntry.ReloadAsync` / `SingleOrDefaultAsync` → reflection (`ContainsGenericParameters`, `DynamicMethod.CreateDelegate`, funcletizer) |

## Session runner: where the active samples go (53,529 active of 1,729,286)

| Share | Area |
|---:|---|
| **25.53%** | R2 bound tailer per-poll identity and stat (`TranscriptTailer.RunAsync`, `TerminalSeatReleaseObservation.IsCurrentFile/CaptureFile`, `TerminalTranscriptCompletion.Observe`; R1 excluded) |
| **20.30%** | R3 `HostStatsSamplerService` (10.03% `Process.WorkingSet64` → `NtProcessInfoHelper.GetProcessInfos`; 9.86% `WindowsHostStatsProbe.ReadSystemTimes`) |
| 19.36% | R5 runtime thread-pool `CpuUtilizationReader.get_CurrentUtilization` (no repo frame; see caveat) |
| **16.85%** | R1 unbound-tailer locate and fork scan (`LocateAsync`, `EvaluateCandidates`, `TryFindNewerFork`): **the CARD-1624 area**, unfixed in this build |
| 7.71% | JSON serialisation of responses (`System.Text.Json.WriteStack`, no repo frame) |
| 6.48% | R4 session read routes (`GetBuffer` → `ReadAnsiLog` 2.7%, `GetSnapshotAsync`) |
| 1.88% | R6 `SidecarKnownSessionProbe.Exists` |
| 0.43% | R7 PtyHost framing reads (expected) |

Library areas, inclusive: timers 63.6% (everything runs from `Task.Delay` and `PeriodicTimer` callbacks),
file I/O 41.6%, Kestrel 15.7%, process API 10.5%, System.Text.Json 8.5%. Top exclusive frames:
`FileSystem.FillAttributeInfo` 22.76%, `CpuUtilizationReader.get_CurrentUtilization` 19.36%,
`NtProcessInfoHelper.GetProcessInfos` 10.03%, `WindowsHostStatsProbe.ReadSystemTimes` 9.86%,
`Kernel32.CreateFilePrivate` 6.30%, `FileSystemEnumerator.DirectoryFinished` 5.43%,
`FileSystemEnumerator.MoveNext` 3.10%, `WriteStack.Pop` 2.95%, `WSASend` 2.37%,
`FileSystemEnumerator.CreateRelativeDirectoryHandle` 2.31%, `OptimizedInboxTextEncoder.Encode` 2.15%.

At sample time the runner had 35 sessions: 13 Running (12 with a bound transcript) and 22 Exited.

**Caveat on R5 and `ReadSystemTimes`.** Both are single `GetSystemTimes` P/Invokes, made every 0.5 s and every 5 s.
Neither can plausibly take 5–10% of a thread on its own, which suggests EventPipe attributes some
preemptive-mode time to the method that contains an inlined P/Invoke. Treat both as unproven until an ETW
trace confirms them. They are not recommended for action. The `WorkingSet64` item is credible:
`Process.WorkingSet64` triggers a whole-system `NtQuerySystemInformation` process and thread snapshot.

### Hottest runner stacks, collapsed to repo frames

| Share | Stack ==> leaf |
|---:|---|
| 10.03% | `HostStatsSamplerService.ExecuteAsync > SampleOnceAsync > ReadProcesses > ReadOne > WorkingSet` ==> `Process.WorkingSet64` → `GetProcessInfos` |
| 9.86% | `HostStatsSamplerService.SampleOnceAsync > WindowsHostStatsProbe.Read > ReadSystemTimes` (see caveat) |
| 6.90% | `TranscriptTailer.RunAsync` ==> `FileInfo.Exists` (`FillAttributeInfo`) |
| 6.14% | `TranscriptTailer.RunAsync > TerminalSeatReleaseObservation.IsCurrentFile > CaptureFile` ==> `File.Exists` |
| 5.98% | `… IsCurrentFile > CaptureFile` ==> `File.OpenHandle` (`CreateFile`) |
| 5.41% | `TranscriptTailer.LocateAsync > EvaluateCandidates` ==> `Directory.EnumerateFiles` (`DirectoryFinished`) |
| 3.90% | `TranscriptTailer.RunAsync > TerminalTranscriptCompletion.Observe` ==> `FileInfo.Exists` |
| 2.31% | `… EvaluateCandidates` ==> `CreateRelativeDirectoryHandle` |
| 1.68% | `SessionReadLaunchRoutes.UseRunnerExceptionMapping` ==> response JSON text encoding |
| 1.49% | `TranscriptTailer.RunAsync > TryFindNewerFork` ==> `Directory.EnumerateFiles` |

## Recommendations

Shares are of the process's active samples in the window. The cores figure multiplies the share by
that process's OS CPU in the window (server 1.30 cores, runner 0.23 cores).

| # | Call path | Share | Kind of fix | Expected saving | Risk | Card |
|---|---|---:|---|---|---|---|
| 1 | `server/Application/Services/CheckCompactionContinuationService.cs:1029` `LoadFactsAsync`, specifically the `tokens.Any(token => row.Text.Contains(token))` predicate at `:1066`. It is reached from discovery (`DiscoverSeatAsync`, `:127` and `:140`, gated to once a minute by `CheckCompactionContinuationGate`) and from `AdvanceAsync` (`:287`). `AdvanceAsync` runs for every unresolved episode on every dispatcher tick and is not gated. | 31.97% (≈0.41 cores) | Avoid recomputation and cache. Load only the rows the policy judges (after the latest compaction boundary), not the whole session. Compute the correlated-Check flag once per immutable row: cache by (session, sequence), or skip seats whose max sequence has not changed since the last pass. Replace the M prompts × 3N tokens `Contains` with one scan per prompt that pulls GUID-shaped substrings into a `HashSet` lookup. Project `Text` only for `UserPrompt` rows. | Most of 0.4 cores. Probably also most of the gen2 churn | Medium. CARD-0079 is the only automatic stop of a Working session, so the policy verdict must stay identical. Pin it with the existing `CompactionContinuationPolicy` tests plus a fact-equivalence test | CARD-1754 |
| 2 | `TaskWorktreeRetirementService.LegacyReleaseAdmissionAsync` → `GuardedWorktreeRemoval.LiveAuthorizationAsync/RemoveDirectoryAsync/BindContentAsync/EntryManifest/TrackedBytesAsync/CustodyOf` → `WorktreeNoFollowDelete.RegularFileHash` (SHA-256 of each file), `TryStamp`, `WorktreeNestedRepositories.Attributes` | 15.02% (≈0.20 cores) | Cache and avoid repeated work. First measure whether the same worktree is bound again on every sweep. If so, cache the manifest keyed by the directory stamp, and back off refused retirements. Hash only files that git does not already report clean (use the index stat cache) | Most of 0.2 cores if passes repeat; less if the window happened to retire many worktrees | Medium to high. This is deletion custody, and CARD-1714 (Review) touches the same code, so coordinate | CARD-1755 |
| 3 | `src/Antiphon.SessionRunner/TranscriptTailer.cs:472–530` bound poll loop: `new FileInfo(path).Exists` (`:488`), `TerminalObservation.IsCurrentFile(path)` (`:520`, `TerminalSeatReleaseObservation.cs:151` → `CaptureFile` `:288` = `File.Exists` + `File.OpenHandle` + identity + close), and `_completion.Observe` (one more stat). This runs every 300 ms (`PollInterval`, `:39`) for each bound session | 25.53% (≈0.06 cores, upper bound because of the file syscalls) | Avoid per-poll syscalls. Re-check identity only when length or mtime changes, or every few seconds. Reuse one `FileInfo`. Back the poll off for quiet or exited sessions (300 ms → 2 s after a quiet period) | Most of R2, roughly 0.04–0.06 cores, plus fewer kernel transitions and fewer filter-driver hits | Low to medium. File-identity change detection (CARD-0519/0667) must stay prompt for active sessions. Keep a bounded maximum staleness | CARD-1756 |
| 4 | `src/Antiphon.SessionRunner/HostStatsSamplerService.cs:198` `WorkingSet(pid)`, which is `Process.GetProcessById(pid).WorkingSet64` called once per target per 5 s tick (`ReadProcesses` `:89`) | 10.03% (WorkingSet) / 20.30% (sampler) | Batch. Take one process snapshot per tick, or call `OpenProcess` + `K32GetProcessMemoryInfo` per pid, instead of a whole-system snapshot per process | ≈0.02 cores; scales with session count | Low | CARD-1757 |
| 5 | Server `SessionRunnerHttpClient.SendReadAsync/ReadJsonAsync<T>` (`AdmittedReadHandler` → `LiveTimingHttpHandler`), plus `PhoneHomeFraming.ReadFrameAsync` deserialisation. On the runner, the matching cost is JSON response writing (7.7%) and the read routes (6.5%) | 12.10% server (≈0.16 cores), ~14% runner | Avoid repeated reads. Count the polled routes per minute first (LiveTiming), then make the hot reads conditional (since-sequence or ETag) or push them over phone-home. Use source-generated `JsonSerializerContext` for the large runner DTOs | Up to ~0.1 cores server plus ~0.03 runner | Low to medium. Transport contract changes need runner and server parity | CARD-1758 |
| 6 | `server/Application/Services/AgentTaskLandNotificationService.cs:58–59`: `SingleAsync` immediately followed by `db.Entry(note).ReloadAsync`, plus the per-note `SingleOrDefaultAsync` queries in `ReconcileAsync`, called for every open note on each sweep | 9.33% (≈0.12 cores), of which 3.2% is reflection and query compilation | Avoid per-call allocation and compilation. Drop the redundant reload (or use `AsNoTracking` and attach); use `EF.CompileAsyncQuery` for the per-note lookups; skip notes whose `NextAttemptAt` is in the future before loading them | ≈0.05–0.08 cores | Low. Concurrency-token semantics must stay the same | CARD-1759 |
| 7 | `server/Application/Services/TaskReportTurnSelector.cs:409` `LastBefore`, a linear scan from the start called once per end-turn in the `SelectAsync` loop (`:93`), which makes it O(ends × prompts) | 2.85% | Algorithmic. Use binary search over the sequence-ordered rows, or one merged walk | ≈0.03 cores | Very low | CARD-1760 |

Not recommended for action yet: R5 and `ReadSystemTimes` (see caveat). The CARD-1624 locate scan is
16.85% of the runner here. That supports landing CARD-1624 and needs no new card. S5 (delegate check git
status, 5.2%) and S7 (worktree metadata reads, 4.1%) are next in line if a later trace still shows them.
Both processes run **Debug** builds. Release JIT would shrink the repo-code self time (items 2, 3 and 7),
but whether the AppHost runs Release is an operator decision about the dev loop.

## Commands (exact)

```powershell
# identify (by listening port, start time and command line)
Get-NetTCPConnection -State Listen -LocalPort 17202,17204          # 17204 -> 153560; 17202 -> dcpctrl proxy
Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'Antiphon|AppHost' }   # Antiphon.Server.exe 330800
Get-NetTCPConnection -State Listen -OwningProcess 330800             # 53739 (behind the 17202 proxy)
dotnet-trace --version                                               # 10.0.745401 (global tool, already installed)

# collect, concurrently, 5 minutes each (low overhead: SampleProfiler + GC informational events only;
# no allocation tick, gc-verbose or contention keywords)
dotnet-trace collect --process-id 330800 --duration 00:05:00 --profile dotnet-sampled-thread-time,gc-collect --format NetTrace -o .antiphon\trace\server.nettrace
dotnet-trace collect --process-id 153560 --duration 00:05:00 --profile dotnet-sampled-thread-time,gc-collect --format NetTrace -o .antiphon\trace\runner.nettrace
#   providers enabled: Microsoft-DotNETCore-SampleProfiler 0xF00000000000:4,
#   Microsoft-Windows-DotNETRuntime 0x1:4, Microsoft-Windows-DotNETRuntimePrivate 0x1:4
pwsh -NoProfile -File .antiphon\trace\os-sampler.ps1 -PidList 330800,153560 -Seconds 280 -Out .antiphon\trace\os-samples.csv
#   every 5 s: per-process CPU delta, working set, threads, handles; \Processor(_Total)\% Processor Time;
#   \System\Processor Queue Length; per-process CPU deltas for the whole machine (top list)

# analyse: throwaway TraceEvent 3.1.21 console tool (restored offline from the local NuGet cache), not committed
pwsh -NoProfile -File scripts/build-slot.ps1 -Label trace-analyzer-7ca19c43 -- dotnet build .antiphon\trace\analyzer\TraceAnalyzer.csproj --source "$env:USERPROFILE\.nuget\packages"
.antiphon\trace\analyzer\bin\Debug\net10.0\TraceAnalyzer.exe .antiphon\trace\server.nettrace .antiphon\trace\server-report.md
.antiphon\trace\analyzer\bin\Debug\net10.0\TraceAnalyzer.exe .antiphon\trace\runner.nettrace .antiphon\trace\runner-report.md
```

The analyser converts each nettrace to an etlx with `TraceLog.CreateFromEventPipeDataFile`. It walks every
`Microsoft-DotNETCore-SampleProfiler/Thread/Sample` call stack and classifies samples by leaf frame,
as described above. It then builds the tables: exclusive (leaf), inclusive (any frame), repo inclusive,
nearest repo frame with its callee and leaf, area unions, collapsed repo stacks, and per-thread counts.
It reads GC events as follows: `GC/Start`/`Stop` with `Depth`; `SuspendEEStart` reasons 1 and 6 only
for the pause figures.

To repeat this run: substitute the current PIDs, keep the same two `collect` lines, and
classify samples by leaf. Do not use the sample type flag.

## Raw data (untracked, not committed)

Under `C:\Antiphon\worktrees\card-task-7ca19c43\.antiphon\trace\` (gitignored by `.antiphon/`):
`server.nettrace` (114,527,814 B), `runner.nettrace` (34,532,578 B), `server.etlx` (245,309,740 B),
`runner.etlx` (248,302,588 B), `server-report.md` and `runner-report.md` (full tables), `os-samples.csv`
and `os-samples-top.csv`, `window.txt`, and `analyzer\` (tool source). The traces contain method and type
names. They may also contain string fragments, so they stay local.
