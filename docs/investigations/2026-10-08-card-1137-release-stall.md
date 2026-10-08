# CARD-1137: residual release-terminal-seat stall (root cause)

Date: 2026-10-08. Investigate task `6ac659a5`. Branch `feat/card-task-6ac659a5` from
`13d75841d` (the warm-up remedy, `docs/investigations/2026-10-08-card-1137-warmup-remedy.md`).
`origin/master` at fetch: `31632adc03b78956c7dc2d056280d7309681a7ff`. Measurement only: the
instrumentation was in a detached scratch worktree, never committed, and is now deleted. No
production or test file on this branch was changed.

## Verdict: confirmed

Two contending tests share the process-wide `System.Text.Json` metadata lock, and the
runner's release handler waits on it synchronously.

1. `TerminalRunnerSeatReleaseTests.Pending_delivery_prevents_release` serializes an EF entity
   with default options: `JsonSerializer.Serialize(message)` at
   `tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs:1008` (again at `:1013`).
   `SessionQueuedMessage` has the navigation `AgentSession AgentSession`
   (`server/Domain/Entities/SessionQueuedMessage.cs:188`). Through it, reflection metadata
   reaches the whole domain graph: Agent, Board, Card, Project, Worktree, PipelineDefinition and
   so on. That is 122 types, 107 of them `Antiphon.Server.Domain.*`. STJ builds and configures
   these types inside `JsonTypeInfo.EnsureConfigured → ConfigureSynchronized`, which holds the
   monitor of `JsonSerializerOptions.Default`'s `CachingContext`. That monitor is one per
   process, and all equivalent option instances share it.
2. Alone, this first serialize takes **403 ms**. In the combined 56-result row it took
   **5.4–16.1 s** (6 unperturbed runs), because 56 tests in one host compete for CPU and JIT. The
   dumps show the owner in a JIT prestub, compiling Reflection.Emit property getters.
3. Meanwhile the HTTP-arm release runs on the runner side:
   `SessionRunnerRuntime.ReleaseTerminalSeatAsync` (`src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:1277`)
   → `ReleaseSlotUnderGateAsync` → `RunnerSession.KillAsync` (`:1505`) → `herdr.KillAsync` (`:3696`).
   The fixture child is `NativeChild.KillAsync` (`tests/.../RunnerSeatReleaseFixture.cs:953-954`).
   It raises `Exited` synchronously. The handler runs `HandleExited` (`:3855`) →
   `SessionRunnerEventHub.Publish` (`:3880`, `:4195`) → `JsonSerializer.Serialize(payload)`
   (`:4197`), the **first** `RunnerSessionExitedEvent` serialization in the process, with default
   options. That call blocks on the same monitor **synchronously, on the request thread**, while
   the release still holds the session launch gate.
4. The blocked kills all resume together when the owner releases the lock. If the wait plus the
   time before the kill exceeds the client's 10 s `HttpClient.Timeout`
   (`RunnerSeatReleaseFixture.cs:663`, `LoopbackBudgetSeconds`), the client throws
   `TaskCanceledException` → `IOException "Unable to read data from the transport connection"`.
   That is the stored CP-20/CP-22 signature at `SessionRunnerHttpClient.cs:61`. The discovery
   caller swallows the exception and records the committed intent as `Unresolved`, so the test
   sees `Released 0`.

**Why these three methods.** Only HTTP-arm `LiveSeat` releases that reach their first kill while
the owner holds the lock are blocked. In every instrumented run they were the first release of
`Discovery_request_uses_runner_owned_delivery_evidence`, `Unknown_server_session_with_idle_runner_is_released`
(local arm) and `One_runner_failure_does_not_hide_other_candidates`, plus up to three more that
finished inside the budget. All of them reach their first release about 10–20 s into the run,
which is when `Pending_delivery_prevents_release` reaches line 1008. After the first
`RunnerSessionExitedEvent` metadata exists, later kills take under 1 ms. This explains the
stored evidence. In CP-20 and CP-22 the two discovery tests failed **at the same instant**
(49.42/49.42 s and 48.95/48.95 s), because both were released by one monitor exit. About 20
other methods kept completing, because only threads that need *new* metadata on the shared
options wait.

The 5 s kill timeout and the `Task.Delay` are not involved: `_exited` is set synchronously,
before the wait. The Npgsql pool, the build/process-spawn limiter, the tailer poll gate and the
first-request endpoint build are also not involved; the warm-up is already in place.

## Evidence

Scratch build of `13d75841d` plus throwaway markers: client begin/headers/fail around
`SessionRunnerHttpClient.ReleaseTerminalSeatAsync`; runner phase markers in
`ReleaseTerminalSeatAsync`, `ReleaseSlotUnderGateAsync`, `RunnerSession.KillAsync`/`HandleExited`/`DisposeAsync`
and the tailer `DisposeAsync`; `Stopwatch` around `SessionRunnerEventHub.Publish`'s serialize and
around test line 1008; per-second thread-pool counters; and a watchdog that runs
`dotnet-dump collect --type Heap` once a release has been outstanding for 2 s. Every row used one
TUnit host with the combined filter `/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/*`,
run serially through `scripts/build-slot.ps1` (one isolated build `bin-c1137i/`, `UseAppHost=false`,
rebuilt for each instrumentation change). Raw traces stay in the session scratchpad and are not
committed.

### Repetitions

| Run | Mode | Result | Owner serialize (`:1008`) | First exited-serialize max wait | Waiters > 1 s | Release timeouts |
|---|---|---|---:|---:|---:|---:|
| r1 | late `dotnet-stack` capture | 56/56 | (not timed) | 4.2 s (6 kills resumed at the same ms) | 6 | 0 |
| r2 | dump at 2 s | **53/56**, same 3 | (not timed) | 10.9 s (incl. ~6 s dump pause) | 5 | 5 |
| r3 | no dump | 56/56 | 7 991 ms, 140 types | 3 783 ms | 6 | 0 |
| r4 | no dump | **53/56**, same 3 | **16 061 ms**, 143 types | **9 858 ms** | 6 | 5 |
| r5 | no dump | 56/56 | 7 016 ms | 3 550 ms | 5 | 0 |
| r6 | no dump | 56/56 | 11 527 ms | 7 871 ms | 6 | 0 |
| r7 | no dump | 56/56 | 6 060 ms | 3 557 ms | 5 | 0 |
| r8 | no dump | 56/56 | 5 433 ms | 2 518 ms | 5 | 0 |
| alone | `Pending_delivery_prevents_release` only | 1/1 | **403 ms**, 122 types | n/a | n/a | n/a |

In every combined run, 5–6 runner kill threads were blocked for more than 1 s on this one
`Serialize`. Red is the tail of that distribution: the wait comes within about 1 s of the 10 s
budget. 2 of 8 runs were red here, consistent with the remedy note's 2/20 and S1's 3/16.

### Dump (r2, taken mid-stall)

`syncblk`: `System.Text.Json.JsonSerializerOptions+CachingContext`, MonitorHeld 13, recursion 3,
owned by managed thread 21. Thread 21's stack is `Pending_delivery_prevents_release.MoveNext` →
`JsonSerializer.Serialize` → `JsonTypeInfo.EnsureConfigured.ConfigureSynchronized` (three
nested levels) → `DefaultJsonTypeInfoResolver.CreatePropertyInfo` → `ReflectionEmitCachingMemberAccessor.CreatePropertyGetter<TrackerKind>`,
stopped at a `PrestubMethodFrame` (JIT). The waiters are in `Monitor.ReliableEnter` under
`ConfigureSynchronized`. Five of them are on
`SessionRunnerEventHub.Publish ← RunnerSession.HandleExited ← NativeChild.KillAsync ← RunnerSession.KillAsync ← ReleaseSlotUnderGateAsync ← ReleaseTerminalSeatAsync`.
One is a test thread (`Attention_recovers_a_missed_invalidation_after_commit`, `JsonSerializer.Serialize`).

### Causal control

| Run | Throwaway change (scratch only) | Result | Owner serialize | Exited-serialize max wait | Waiters > 1 s | Timeouts |
|---|---|---|---:|---:|---:|---:|
| rc1–rc3 | `:1008`/`:1013` use `new JsonSerializerOptions()` | 56/56 ×3 | 3.5–9.5 s | 1.9–5.0 s | 5–6 | 0 |
| rcd1 | same, with the dump | **53/56**, same 3 | — | 17.7 s (dump) | 5 | 3 |
| rs1–rs3 | `:1008`/`:1013` use `new JsonSerializerOptions { MaxDepth = 64 }` | 56/56 ×3 | 2.3–9.7 s | **44–77 ms** | **0** | 0 |

`new JsonSerializerOptions()` did **not** separate the lock. The rcd1 dump shows the same
`CachingContext` monitor owned by the private-options serialize, because STJ shares one caching
context, and so one lock, across option instances with equivalent settings. With non-equivalent
settings the owner still spent up to 9.7 s of CPU building metadata, in rs3 overlapping the
runner kills, but the runner's exited-event serialize stayed under 80 ms. The mechanism is the
shared metadata lock, not general CPU load.

## Remaining uncertainties

- The 17–40× slowdown of the owner (403 ms alone, 5–16 s combined) is attributed to CPU/JIT
  contention from that one dump frame. It was not profiled further. Host load average was 10–22
  on 24 cores.
- `Discovery_request_uses_runner_owned_delivery_evidence` was blocked on its first HTTP-arm
  iteration in r2/r4. The provider of the stored CP-20/CP-22 iteration is not recorded there.
- The same pattern (default-options `JsonSerializer.Serialize` of EF entities that have
  navigations) also exists in `ReviewEvidenceRecoveryTests.cs:451-457` and
  `CardFilePrivacySyncAcceptanceTests.cs:130-131`. It can stall any lock-sensitive test that
  shares their host, such as the Unit lane or nightly runs. That was not measured here.
- Production: the real runner process does not serialize EF entity graphs, so this is a
  test-host coupling. Still, `SessionRunnerEventHub.Publish` (`SessionRunnerRuntime.cs:4197`)
  does first-use reflection serialization synchronously inside the release gate. That stays a
  latent dependency on the process-wide STJ lock. This is not evidence of a production defect.

## Not done, noted

- Fix idea (Code): at `TerminalRunnerSeatReleaseTests.cs:1008/1013`, compare a scalar projection of the pending-evidence fields instead of the entity with its navigation graph; `new JsonSerializerOptions()` is **not** enough.
- Possible guard: assert that the evidence snapshot's type has no navigation-typed members, or run the release kill path with a precomputed `RunnerSessionExitedEvent` type info. Both are design choices for Plan/Code.

--- next stage ---
next: code
handoff: CARD-1137 residual root cause confirmed: TerminalRunnerSeatReleaseTests.cs:1008/:1013 JsonSerializer.Serialize(SessionQueuedMessage) (nav AgentSession → 122 domain types) holds STJ Default CachingContext lock 5–16 s; runner HandleExited→EventHub.Publish (SessionRunnerRuntime.cs:4197) blocks on it inside release → 10 s timeout. Replace with scalar projection (not new JsonSerializerOptions()); re-run combined row ×20.
artifact: docs/investigations/2026-10-08-card-1137-release-stall.md
