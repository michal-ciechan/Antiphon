# CARD-1911: what keeps creating DynamicMethods on Antiphon.Server (2026-11-14)

Task 5eac8f0a (Investigate, desktop). This follows [2026-11-13-desktop-cpu-trace.md](2026-11-13-desktop-cpu-trace.md) (task b754552c, on branch `feat/card-task-b754552c`). That trace measured the finalizer thread at 0.45 cores in `DynamicResolver+DestroyScout.Finalize` and `GC.ReRegisterForFinalize`, behind 226 MB/s of allocation and a gen2 GC every 6.5 s.

## Root cause

EF Core compiled-query cache misses create the DynamicMethods. All 166 dynamic methods loaded in a 60 s window were `dynamicClass.lambda_method*`, created under `CompiledQueryCache.GetOrAddQuery -> QueryCompilationContext.CreateQueryExecutor` (and `LiftableConstantProcessor`) `-> Expression.Compile()`. Three query sites account for all of them. Each miss compiles a query shape that is never reused: its cache key carries a per-call constant. Every compiled delegate stays live in the cache, and its DestroyScout re-registers for finalization on every GC. The GC rate is driven 71% by one string-copy site (`TranscriptKinds.*` `TrimStart()`).

## Top creation sites (60 s JIT trace, 2.58 dynamic methods/s in total)

| # | Site | Rate | Compiles/64 s | Why the cache misses | Card |
|---|---|---|---|---|---|
| 1 | `HostLoadIngestService.IngestAsync` -> `ToListAsync` (`server/Infrastructure/HostLoad/HostLoadIngestService.cs:42-43`, predicate `:106-118`) | 1.86/s (72%) | 12 (10 DMs each, one per ~5 s ingest batch) | `IdentityPredicate` builds an OR chain with `Expression.Constant(guid/guid/long)` per key; every batch has new keys, so it is a new query | CARD-1912 |
| 2 | `AgentTaskDispatcher.DispatchCoreAsync` -> `ExecuteUpdateAsync` (`server/Application/Services/AgentTaskDispatcher.cs:535-540`) | 0.56/s | 12 (3 DMs each, every dispatcher tick) | `UtcNow()` instance call inside the lambda is inlined as a `TIMESTAMPTZ` literal (reproduced below) | CARD-1913 |
| 3 | `AgentTaskDispatcher.RecoverDeliveryFailureNotificationsAsync` -> `ToListAsync` (`AgentTaskDispatcher.cs:5283-5289`), from `RemindUnacknowledgedFailuresAsync` / `RunMaintenanceAsync` | 0.12/s | ~2-3 | same `UtcNow()` inlining | CARD-1913 |

Control: the `CountAsync` at `AgentTaskDispatcher.cs:526` runs every tick in the same method, has no `UtcNow()`, and was not recompiled in the window. That rules out general cache eviction for the dispatcher sites.

The process `lambda_method` counter stood at 563,672 at 00:42Z, about 18.8 h after the server started (2026-11-13 05:54 local).

## Reproduction of the `UtcNow()` inlining (sites 2 and 3)

The repro used the server's exact packages, Microsoft.EntityFrameworkCore 9.0.21 and Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4, with `ToQueryString()` and no database. Source is at `C:\Antiphon\traces\2026-11-14\tools\efrepro-Program.cs`. Each query ran 3 times, counting `CoreEventId.QueryCompilationStarting`:

```
instance UtcNow(): WHERE r."At" <= TIMESTAMPTZ '2026-11-14T00:48:18.660225Z'
captured local:    -- @__now_0='2026-11-14T00:48:18.6682258Z'   WHERE r."At" <= @__now_0
compilations: instance=3 local=1 (tagged queries; 3 calls each)
```

The C# compiler emits `this` as a `ConstantExpression`. EF 9's funcletizer evaluates the closure-free subtree `Constant(this).UtcNow()` and inlines the value as a constant, so the cache key changes on every call. A regex over `server/**/*.cs` finds 43 candidate lines in 14 files (`ChannelOutboundDeliveryPump.cs` 11, `SessionMessageQueueService.cs` 8, `AgentTaskDispatcher.cs` 6, ...) that compare to `UtcNow()` or pass it to `SetProperty`. Not every hit is in an EF lambda. Only sites 2 and 3 fired inside this 60 s window.

## Allocation (60 s AllocationTick trace): the GC-rate driver

The trace recorded 126,059 ticks, about 216.6 MB/s sampled, which agrees with the 226 MB/s from dotnet-counters on 2026-11-13.

| Type @ first Antiphon frame | MB/s |
|---|---|
| String @ `TranscriptKinds.IsTaskNotificationPrompt` | 53.7 |
| String @ `TranscriptKinds.IsLocalCommandRecord` | 50.9 |
| String @ `TranscriptKinds.IsCompactionContinuationPrompt` | 50.3 |
| byte[] (no Antiphon frame) | 11.7 |
| byte[] @ `PhoneHomeFraming.ReadFrameAsync` | 10.7 |
| String @ `SessionRunnerHttpClient.ReadGeneratedAsync` | 6.9 |
| `PromptRow` enumerator @ `TaskReportTurnSelector.NewestRealBetween` | 3.0 |

- The three `TranscriptKinds` predicates (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:598-601, 640-647, 753-756`) each call `text.TrimStart()`. That copies the whole prompt whenever it has leading whitespace. Together they are 154.9 MB/s, 71% of all allocation. About 94% arrives through `TranscriptPromptSpan.IsHousekeepingPrompt <- TaskReportTurnSelector.IsHousekeepingRow`, which is CARD-1910's per-boundary rescans. Filed as CARD-1914, independent of CARD-1910.
- Emit-type allocations were too small to be sampled usefully (12 ticks in total). The JIT trace is the authoritative creation count.

## Measurements and overhead

| Window (UTC) | Providers | Server CPU during the trace |
|---|---|---|
| 00:40:45-00:41:50 | `Microsoft-Windows-DotNETRuntime:0x1:5` (GC + AllocationTick) | 0.99 cores |
| 00:41:56-00:43:00 | `Microsoft-Windows-DotNETRuntime:0x18:5` (Loader + JIT verbose) | 1.01 cores |

Server CPU stayed at the 1.01-core steady state measured on 2026-11-13, so tracing overhead was within noise. Raw traces, aggregates, tool sources, a README and `SHA256SUMS.txt` are in `C:\Antiphon\traces\2026-11-14\` (not committed). The aggregator is a small TraceEvent 3.1.21 console app (`tools/tracetool-Program.cs`). It groups `GCAllocationTick` by type and stack, and groups `MethodJittingStarted`/`MethodLoadVerbose` for `dynamicClass` methods by stack. `MethodLoadVerbose` carried no resolvable stack, so `MethodJittingStarted` gave the stacks.

## Remaining uncertainties

- **Not measured: how many DynamicMethods are live.** That needs a heap type count, which was not allowed. The claim that cached, never-reused entries keep the finalizer busy is inferred from the 2026-11-13 DestroyScout cost plus EF's MemoryCache retention, not counted. After CARD-1912 and CARD-1913, the DestroyScout cost should fall as the cache turns over.
- Only a 60 s window was sampled. Sites that fire less than once a minute, such as other `UtcNow()` users and land/reconcile sweeps, may also miss the cache and were not seen.
- Not determined: why the 2026-11-13 CPU sample named other EF callers (`PhoneHomeRunnerDirectory.GetBindingAsync`, `SessionReconciliationService`, ...) as DynamicMethod creators. Those were CPU samples inside EF code generally, not proven cache misses, and none of them compiled during this window.
- The split between DestroyScout cost from the existing live set and from the current creation rate is unknown.

## Not done, noted

- Fix ideas, one line each, are on the cards: parameterise the host-load identity lookup (CARD-1912), hoist `UtcNow()` into locals and guard EF lambdas against instance-method calls (CARD-1913), use span-based `TrimStart` (CARD-1914). `PhoneHomeFraming.ReadFrameAsync` byte[] at 10.7 MB/s could use pooled buffers (no card filed).
