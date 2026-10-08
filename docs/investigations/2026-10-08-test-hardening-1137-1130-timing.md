# Investigation: CARD-1137 and CARD-1130 timing claims (test-hardening batch S1)

Date: 2026-10-08 (UTC 00:12-01:13). Investigate task `85cd9794`. This is slice S1 of
`docs/superpowers/plans/2026-10-08-test-hardening-batch-1137-1134-1130-1126-1152-plan.md`.
S5 (the CARD-1130 runtime verification after the CARD-1105 audit land) and S2-S4 are out of
scope here.

Source under test: **`7ae4ea6b95b86c5c11e5a6ee82b34c8f22490408`** (branch base, unmodified).
`origin/master` later advanced to `4d6936d18`. None of the commits in between touch
`RunnerSeatReleaseFixture.cs`, `RunnerSeatOrphanSweepTests.cs`, `TerminalRunnerSeatReleaseTests.cs`,
`SessionRunnerHttpClient.cs`, `SessionRunnerRuntime.cs`, `RollingVolumeRecycleScriptTests.cs` or
`RemoteScriptContractTests.cs` (`git diff --stat HEAD origin/master` over those paths is empty).

Lane: server2 runner mirror (Linux, 24 cores, 125 GB, .NET 9.0.20, TUnit 1.44, Testcontainers
PostgreSQL). One isolated build: `scripts/build-slot.ps1 -Label card1137-s1-build -- dotnet build
tests/Antiphon.Tests --property:OutputPath=bin-s1inv/ --property:UseAppHost=false` (lease
`3f0859ed`, 221 s). Every run was `build-slot.ps1 -- dotnet tests/Antiphon.Tests/bin-s1inv/Antiphon.Tests.dll
--treenode-filter <f> --report-trx`, one at a time. No source edits. No production contact.
The `bin-s1inv` outputs (28 directories) were deleted afterwards. Raw TRX files, load samples and
`dotnet-stack` snapshots are generated evidence in this session's scratchpad. They are not committed.

## Verdicts

| Card | Verdict | Basis |
|---|---|---|
| CARD-1137 | **Not closeable. The failure reproduces at master with the landed 10 s budget.** The root cause is confirmed (below). It is wider than the named method, so it needs a re-plan, not a residual one-line edit. | 3 of 16 runs of the card's combined 56-result filter went red. The named method failed in 2 of the 16 (same `ListAsync` frame as the card). 8-11 HTTP-arm `LiveSeat` methods failed together in each red run. The separate class rows (CP-1/2/3) were 8/8 green. |
| CARD-1130 | **Closeable on source.** The requested change landed in `c36f04f04e899147631d2a8061eb9d6a119d5304`. No residual edit is needed. Runtime confirmation under load is still S5 (CP-42..CP-45) after the CARD-1105 audit land. It was not run here, per the brief. | The deadline is 120 s at `RollingVolumeRecycleScriptTests.cs:1021-1023`. All three V-2 call sites use it. |

## CARD-1137

### What landed

`2f751049b860f56d1e1d46aa3b293ec2a7274bcb` (CARD-1135, 2026-10-07 06:57 UTC) added the
`LoopbackBudgetSeconds = 10` constant with a CARD-1137 comment at
`tests/Antiphon.Tests/Application/RunnerSeatReleaseFixture.cs:659-662`. It is applied to:

- the long-lived mutation client at `:838`: `HttpClient.Timeout = 10 s`, used by `SendInputAsync`;
- `SessionRunnerSettings.ListTimeoutSeconds` and `RequestTimeoutSeconds` at `:843-844`.
  `SessionRunnerHttpClient.ListAsync` reads these as a linked `CancelAfter` and the resilience
  budget at `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:557`, `:559-563`;
- the read-client factory at `:937`.

Before this commit the list deadline was the production default of 3 s
(`server/Application/Settings/SessionRunnerSettings.cs:13`). Production timeouts are unchanged.

### Load recipe and results

The card's failing shape is the CARD-1124 R-7 filter
`/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/*`. That is 17 + 39 = 56
results in one TUnit host at default parallelism. In every run all 56 started within 30-167 ms;
the card saw 65 ms. Runs were serial, one TUnit host at a time. No synthetic load was generated.
The load is the host's real ambient fleet load: other agents' builds and checkpoint rows held 2-4
of the 4 build slots, and the 1-minute load average was 7-63 across the session. The card recorded
9-14. Load was sampled every 1-10 s per run.

| Run | Start (UTC) | Host 1-min load (min-max) | Other slots held | Our lease | Passed | Failed | Named method |
|---|---|---|---|---|---|---|---|
| CP-1 (alone, `TUNIT_MAX_PARALLEL_TESTS=1`) | 00:16:06 | 36.3-62.8 | 4/4 | d65fea8e | 1/1 | 0 | Passed 10.0 s |
| comb1 | 00:17:43 | 23.3-37.6 | 4/4 | 70c8561b | 56/56 | 0 | Passed 62.4 s |
| **comb2** | 00:19:59 | 16.6-30.8 | 3/4 | ba96ab3e | 45/56 | **11** | **Failed 35.0 s** |
| comb3 | 00:21:46 | 12.7-46.4 | 2/4 | c7b49f02 | 56/56 | 0 | Passed 58.2 s |
| comb4 | 00:24:57 | 28.3-40.8 | 4/4 | 2cd7da04 | 56/56 | 0 | Passed 58.2 s |
| comb5 | 00:27:25 | 14.0-37.3 | 3/4 | 963c9031 | 56/56 | 0 | Passed 47.4 s |
| comb6 | 00:30:01 | 18.5-45.6 | 2/4 | 75893a92 | 56/56 | 0 | Passed 47.5 s |
| **comb7** | 00:33:34 | 14.3-30.3 | 4/4 | 2435535f | 48/56 | **8** | Passed 48.6 s |
| comb8 | 00:35:48 | 11.0-34.0 | 2/4 | 1221d435 | 56/56 | 0 | Passed 49.0 s |
| comb9 | 00:37:38 | 17.1-45.1 | 3/4 | 905784cb | 56/56 | 0 | Passed 42.0 s |
| comb10 | 00:41:04 | 16.1-25.8 | 4/4 | 816e9650 | 56/56 | 0 | Passed 50.9 s |
| comb11 | 00:43:23 | 11.4-19.9 | 3/4 | 0fa2add4 | 56/56 | 0 | Passed 43.1 s |
| comb12 | 00:45:05 | 9.1-27.5 | 3/4 | 573ab3e7 | 56/56 | 0 | Passed 49.9 s |
| comb13 (+stacks) | 00:47:25 | 16.2-32.2 | 3/4 | 0bcc14bb | 56/56 | 0 | Passed 44.8 s |
| comb14 (+stacks) | 00:51:05 | 18.1-33.2 | 3/4 | 55768e6e | 56/56 | 0 | Passed 57.9 s |
| **comb15** (+stacks) | 00:53:08 | 15.3-24.8 | 3/4 | 4f52efc4 | 45/56 | **11** | **Failed 36.1 s** |
| comb16 (+stacks) | 00:55:01 | 10.0-23.4 | 3/4 | a22aeeae | 56/56 | 0 | Passed 51.2 s |
| CP-2 x5 (`RunnerSeatOrphanSweepTests/*`) | 00:58-01:12 | 7.2-29.7 | 2-3/4 | 08c5beae, 6442f488, 7808a3e7, 5a2b1839, 4cba9110 | 17/17 each | 0 | Passed 20.5-22.5 s |
| CP-3 x2 (`TerminalRunnerSeatReleaseTests/*`) | 01:03, 01:06 | 11.4-29.8 | 3/4 | 02fa0ada, dc1b92c3 | 39/39 each | 0 | n/a |

Totals: the combined shape was red in 3 of 16 runs (19 %), and the named method in 2 of 16.
The class-separated plan rows were 8 of 8 green. Red did not track host load: red runs saw
1-minute load 14-31, while green runs reached 46. The first red (comb2) happened while load was
falling, from 31 to 17.

### The failure is a cohort, not one method

In every red run the failing methods were the HTTP-arm `LiveSeat` users. Each of their loops
starts with `phoneHome = false`: `RunnerSeatOrphanSweepTests.cs:184`, `:279`, `:314`, `:346`, `:394`,
`:445`. The failures were:

- comb2 and comb15: `Discovery_request_uses_runner_owned_delivery_evidence`,
  `Existing_job_discovers_debt_without_settlement_callback`,
  `Unknown_server_session_with_live_work_is_preserved`, `One_runner_failure_does_not_hide_other_candidates`,
  `Unknown_server_session_with_idle_runner_is_released`, `Discovery_is_idempotent_across_restart`,
  `Server_restart_reacquires_runner_delivery_evidence`, `Evidence_missing_or_peer_unsupported_defers_discovery`
  (orphan class), plus `Failed_settlement_releases_without_success_branch` and
  `Blocked_report_with_running_runner_requires_a_published_park` (release class), plus
  `Sweep_budget_is_bounded_and_resumes_fairly`. The last is an assertion failure, not a timeout:
  `RunnerSeatReleases.CountAsync()` was 6 against an expected 7 at `RunnerSeatOrphanSweepTests.cs:249`.
- comb7: eight of the same timeouts. The named method passed in that run.

The failure frames:

- The named method: `TaskCanceledException` from `SessionRunnerHttpClient.SendReadAsync`
  (`SessionRunnerHttpClient.cs:133`) ← `ListAsync` (`:564`) ← fixture inventory lambda
  (`RunnerSeatReleaseFixture.cs:276`) ← `SeatDirectory.GetInventoryAsync` (`:1076`) ← `AcquireAsync`
  (`:108`) ← `RunnerSeatOrphanSweepTests.cs:356`. This is the card's stack, with the line numbers
  shifted by the CARD-1135 edit.
- Every other timeout: "The request was canceled due to the configured HttpClient.Timeout of 10
  seconds elapsing" in `SessionRunnerHttpClient.SendInputAsync` (`:607`) ← `LiveSeat.SubmitAsync`
  (`RunnerSeatReleaseFixture.cs:755`). The inner exception is `HttpConnection.InitialFillAsync`:
  "Unable to read data from the transport connection". So the request was written and the
  in-process runner sent no response headers within 10 s.

The timeouts in each red run expired together, in 37 ms (comb2: 00:21:13.268-.305), 7 ms
(comb7: 00:35:17.687-.694) and 92 ms (comb15: 00:54:27.358-.450), all 33-36 s after the
methods started. The process was not frozen. Other methods kept completing throughout the
10 s before each burst; for example, comb2 had completions at 00:21:08.9, 10.6, 11.2, 11.8, 12.1 and 13.2.

### Mechanism (reconstructed from stacks captured inside a red run)

`dotnet-stack report` snapshots were taken at about +25 s and +30 s after test start in comb13-16.
comb15 went red. Its +30 s snapshot (00:54:21.0) falls inside the stall: the timeouts expired at
+36.1 s, so the requests left at about +26 s. It shows:

- **11 pool workers** were in the in-process runner apps' first-request endpoint build. That is
  one per HTTP-arm seat, matching the 10 timeouts plus the sweep method. The chain is Kestrel
  `HttpProtocol.ProcessRequests` → `EndpointRoutingMiddleware.InitializeCoreAsync` →
  `DfaMatcherFactory.CreateMatcher` → `CompositeEndpointDataSource.EnsureEndpointsInitialized` →
  `RouteEndpointDataSource.CreateRouteEndpointBuilder` →
  `RequestDelegateFactory.HandleRequestBodyAndCompileRequestDelegateForJson` →
  `JsonSerializerOptions.GetTypeInfo` → `JsonTypeInfo.EnsureConfigured.ConfigureSynchronized`.
  - **8 of these 11 were parked at the entry frame of `ConfigureSynchronized`**, with only native
    frames above it. That is the shape of a contended `lock` inside that method.
  - The other 3 were doing reflection work under it: `DefaultJsonTypeInfoResolver.DeterminePropertyAccessors`
    for `Antiphon.SessionRunner.Contracts.WorkspaceParkSourceMode`, `RuntimeType.MakeGenericType`,
    and nested `CachingContext.GetOrAddTypeInfo`.
- **23 pool workers** were in Npgsql per-data-source type-mapping initialisation, all CPU work:
  `TypeInfoMappingCollection.AddStructArrayType` ←
  `AdoTypeInfoResolverFactory+RangeArrayResolver.AddMappings` ← `TypeInfoCache.GetOrAddInfo`.
- The test process's CPU rate dropped during the stall. Its counter rose 18334→19794 ticks between
  00:54:16 and 00:54:25, about 1.6 cores. Outside the stall it was about 6 cores. This window also
  includes the two snapshot pauses.

The fixture's HTTP arm is built at `RunnerSeatReleaseFixture.cs:806-845`. It creates a fresh
`WebApplication` per seat and maps the production `MapRunnerCapabilitiesRoute`, the production
`MapTerminalSeatReleaseRoutes`, `GET /sessions` and `POST /sessions/{id}/input`. Then it hands
`Client` to the test with **no warm-up request**. The phone-home arm, by contrast, completes a
real `ListAsync` before admitting input (`:790-803`). ASP.NET Core builds every endpoint of an app
lazily, on its first request. So the first `SubmitAsync` or `AcquireAsync` on each HTTP-arm seat
pays for compiling every mapped route's request delegate and configuring its JSON contracts.
That cost falls inside the test's 10 s per-request budget.

The combined row starts 10-11 of these seats at nearly the same moment, about +25 s, after the
schema and harness setup. The endpoint builds then serialise on the JSON configuration lock
while about 23 other workers are doing Npgsql type initialisation for the other 45 methods. When
the lock holder plus the queue take longer than 10 s, every waiting seat's request expires at once.

Green runs show the same race won. comb14's +30 s snapshot had 11 endpoint-init workers and 15
Npgsql-init workers, but none were parked in `ConfigureSynchronized`. comb13's +20 s snapshot
showed 9 workers already past routing, inside `SessionRunnerRuntime.SendInputAsync`.

The separate class rows (CP-2 with 17, CP-3 with 39) put fewer cold HTTP-arm seats and fewer
concurrent Npgsql initialisations into one host, and were green 8 of 8. That is consistent with
this mechanism, but 8 runs cannot rule out a lower red rate there.

### Why the 10 s budget did not settle it

The CARD-1135 comment calls the 3 s default too short for "a cold in-process Kestrel inventory
read". The cold-start cost is real and was correctly identified. But the 10 s figure bounds the
cost for one seat. It does not bound the cost of 10-11 seats paying it at once under one
process-wide lock while the CPU is shared with the other methods' Npgsql initialisation.
Measured outcome: 3/16 red at 10 s.

### Uncertainties

- The lock object is inferred, not observed. It comes from the stack shape: 8 threads at the
  entry frame of `ConfigureSynchronized` with native frames above, and 3 working inside. I did not
  read the System.Text.Json source to prove which object is locked, or that it is shared across the
  per-seat `WebApplication` instances. If the lock is per-app, the stall is pure CPU starvation of
  10 parallel cold builds.
- The 23 Npgsql-init workers are shown to be concurrent with the stall. Their causal share is not
  isolated: I ran no experiment with them removed.
- Thread-pool exhaustion is not supported. Green-run snapshots (comb13) had 17-26 idle workers,
  and the thread count ramped from 57 to about 100 per run. I did not run a
  `DOTNET_ThreadPool_ForceMinWorkerThreads` experiment.
- The stack sampler pauses the process briefly. One of the three red runs (comb15) had it
  attached, but comb2 and comb7 went red without it, so the failure does not depend on the sampler.
- The `Sweep_budget_is_bounded_and_resumes_fairly` count of 6 against 7 in two red runs is likely
  one candidate whose discovery hit the same cold-runner stall. I did not verify that.
- 16 combined runs give a red-rate estimate of about 19 % with a wide interval.

## CARD-1130

Read only. The C1008/C1105 remote classes were not run, per the brief.

- **Deadline:** `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:1021-1023`, inside
  `C1008HostFixture.Run` (signature at `:964`). The code is
  `new CancellationTokenSource(TimeSpan.FromSeconds(120))` with a CARD-1130 comment. Lines
  `:1024-1025` still kill the owned child tree and rethrow on expiry. `git blame` attributes
  `:1021-1023` to `c36f04f04e` (CARD-1105, 2026-10-07 01:11 UTC), whose diff replaced the 30 s value
  in this method only.
- **Call sites of the card's method:** in `RemoteScriptContractTests.C1105_Redeploy_accepts_previous_generation_and_requires_new_bind`
  (`tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:935`, `[ParallelLimiter<ProcessSpawnLimit>]`):
  accepted `Run` at `:940`, rejected `Run` at `:957`, resume `Run` at `:964`. Each call creates its
  own 120 s source. So each `deploy-parent` run, which the card measured at 22-30 s, has at least a
  4x margin. The method's worst case is 3 x 120 s, inside the 15-minute row floor.
- **Deadlines that stay at 30 s** are not on the card's path, as the plan's ground truth said:
  `C1008WrapperFixture.Run` (PowerShell child, `RollingVolumeRecycleScriptTests.cs:745`, deadline
  `:768`), the constructor's compose-model renders (`:851`, `:870`), and the receipt-copy child
  (`RemoteScriptContractTests.cs:846`).
- **Not done from the card's optional asks:** the `C1008_FIXTURE_RUN_TIMEOUT_SECONDS` override and
  splitting V-2. The plan's D-1 rejects both, and this read found no failure that needs them.
- No post-landing elapsed measurement of V-2 under load was found in `docs/`. That is what S5's
  CP-42..CP-45 will record.

## Not done, noted

- No fix was designed or applied. One idea for the re-plan: make each HTTP-arm `LiveSeat` complete
  its endpoint build before handing out `Client`, the way the phone-home arm already completes a
  real `ListAsync`. The warm-up itself would need its own explicit, serialised and bounded wait, so
  that its cost does not land inside a test's per-request budget.
- I did not test whether CP-2 and CP-3 alone stay green at a higher sample count.

--- next stage ---
next: plan
handoff: CARD-1137 still reproduces at 7ae4ea6 (combined 56-result R-7 filter red 3/16; 8-11 HTTP-arm LiveSeat methods time out together; separate CP-2/CP-3 rows 8/8 green). Cause: the first request to each fresh in-process runner app builds its endpoints under the 10 s test budget, serialised in JsonTypeInfo.ConfigureSynchronized. Re-plan the fixture remedy. CARD-1130 is closeable on source via c36f04f04, pending S5.
artifact: docs/investigations/2026-10-08-test-hardening-1137-1130-timing.md
