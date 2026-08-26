# CARD-0208 — `First_input_starts_the_refusal_clock…` flake: a fixed 1.1 s scheduling allowance inside a thread-pool-starved test process — plan

**Date:** 2026-08-26 · **Card:** CARD-0208 (`3ea5c156-abd9-4a52-b691-9c3aa1defa17`) · **Status:** plan
(investigate + design; no implementation in this pass — the two candidate fixes and the trace
instrumentation below were applied locally as experiments, measured, and reverted before this
commit; the working tree carries only this document) · **Verified against:** `master` @ `1b1b667`,
worktree `card-task-40597978`. Every file:line below was re-read out of the code on that commit;
every count below was measured on this machine (8 cores, Windows 10 19045, TUnit 1.44.0,
Microsoft Testing Platform 2.2.2, .NET 9.0.16), with other agents running throughout (machine
load 26–100 % at run start, recorded per run).

---

## Verdict up front

**The refusal clock is correct. The test's assertion is not stall-invariant, and the assembly lets
three separate process-spawning lanes run side by side, which starves the test process's thread
pool for 1.5–2.6 s at a time. Both halves are real; neither is a product defect.**

1. The failing copy is **`CodexTranscriptTailerTests`** (`CodexTranscriptTailerTests.cs:271`), not
   the identically-named `TranscriptAdoptionSafetyTests` copy (`:579`). The Codex class carries no
   `[NotInParallel]`, so its test runs beside 25–37 concurrent tests (§1.1); the Claude copy sits
   in the serialised `"ClaudeConfigDirEnv"` lane, overlapped nothing in 25 TRX-recorded runs, and
   took 1.85 s every time. Both copies carry the same assertion and both should get the same fix.
2. The assertion `unboundSeconds.ShouldBeInRange(0.3, 1.5)` (`:295`, and `:606` in the Claude
   copy) is "the fault arrived within 1.1 s of the 400 ms refusal deadline". That 1.1 s is a
   **scheduling allowance**, not a property of the clock, and the tailer's `Task.Delay(50)` poll
   (`CodexTranscriptTailer.cs:438`) measurably overshoots by **1.5–2.6 s** in a full-assembly run,
   with every tailer loop in the process resuming within ~70 ms of each other (§1.3). The clock
   itself reads exactly what it should — `UnboundSeconds` = fault time − first observation of
   input — it is the *fault* that is late, and the test is written so a late fault reads as a
   wrong clock.
3. The stall is **thread-pool starvation inside the test process**, not CPU contention as such:
   GC pause during the stalls was 0–15 ms; the pool sat flat at 10–24 threads with 3–32 work items
   pending and did not grow for the whole stall; the same test alone under a 6-worker 100 % CPU
   burner passed 20/20 with **no** stall; raising the pool floor to 64 threads (no other change)
   removed every ≥ 800 ms stall in 3 full runs on a machine at 68–93 % load (§2).
4. What fills the pool is the co-scheduling the card suspected, made precise: in **all three**
   traced failures the target ran beside the four `DaemonLogRotationTests` (each boots a
   `pwsh.exe` + `run-daemon.ps1` + `cmd`; the class has **no** parallel constraint at all),
   `SessionCpuWatchdogTests::Idle_session_burning_a_core…` (a real pty-host, in the
   `"ClaudeConfigDirEnv"` lane) and `FirstWriteRaceTests` (a real pty-host, in the
   `"SessionLiveness"` lane) — three spawn lanes at once, which is exactly the gap CARD-0200 §1.5/§4
   recorded as "a separate card if anyone sees a whole-assembly flake with that shape". Every stall
   released 50–100 ms before the `DaemonLogRotationTests` batch ended (§1.4). Applying a 1-wide
   `ProcessSpawnLimit` lane to the ten classes that create real OS processes cut the worst
   `Task.Delay(50)` overshoot from 2.1 s to **281 ms** (§2, F1).

| # | Decision | One line |
|---|---|---|
| D1 | **Fix the assertion in both copies**: keep the lower bound, replace the fixed upper bound with *elapsed wall time since the test appended input* (+0.1 s tolerance) | Measures the clock's actual claim — "starts at input, not at child start or tailer start" — and holds under any stall by construction; 4/4 full runs green with 3.35 s and 3.08 s faults in the trace (§2, F3) |
| D2 | **Add the `ProcessSpawnLimit` lane to this assembly** (its own slice, same commit series): a `ProcessSpawnLimit` class + `[ParallelLimiter<ProcessSpawnLimit>]` on the ten classes that start real children (§3.2) | The AGENTS.md rule the assembly never got; removes the 1.5–2.6 s process-wide stalls that also produced a `HerdrClientTests` 2 s pipe-connect timeout in the same series (§5); costs +40–75 s wall per full run |
| D3 | **No** fixed widening of `1.5` (to 3, 5, …) | Measured faults reached 3.35 s on a 100 %-loaded machine; any constant is a bet on machine load, and D1 loses nothing the constant had |
| D4 | **No** `ThreadPool.SetMinThreads` in test setup, **no** `--maximum-parallel-tests` cap | The floor was the decisive *diagnosis* (§2, F2b) but hides the spawn overlap instead of fixing it; the cap at 8 left 2.0–2.6 s stalls in place (§2, E1) |
| D5 | **No** change to `CodexTranscriptTailer` / `TranscriptTailer` | The clock (`refusingSince`, `CodexTranscriptTailer.cs:399-400`, `TranscriptTailer.cs:425`) does what CARD-0190 D2 says; in production the same stall would report "61.8 s" instead of "60.4 s" on a 60 s delay, which is harmless. A `TimeProvider` refactor to make both tailers clock-injectable is noted as a future option (§3.4), not required here |
| D6 | Evidence bar for the build pass: D1 alone **≥ 10 consecutive clean full-assembly runs** for both target copies (unpatched base rate 3/11 ≈ 27 %, §2 A ⇒ P(10 clean by luck) ≈ 4 %) *and* trace-free — no timing constant anywhere in the two tests changed; D2 measured separately by the ≥ 800 ms overshoot count going to 0 in ≥ 3 traced runs (§4) | Pass/fail alone cannot distinguish "fixed" from "lucky" here — A1–A5 passed 5/5 before the traced series failed 3/6 |

Net diff for D1: ~6 lines in each of two test methods. D2: one 12-line file plus one attribute
line on ten classes. **Risk: nil for D1** — it cannot make a wrong clock pass (a clock starting at
tailer start over-reads by ≥ 1.3 s, at child start by ≥ 3600 s; both fail the new bound by more
than the old one). D2's only risk is the wall-time cost, and a class that spawns nothing is untouched.

---

## 1. Established facts (Investigate, this pass)

### 1.1 Which copy fails, and what it runs beside — measured

Two tests carry this name. From five untraced full-assembly TRX runs (A1–A5) and six traced (A6–A11):

| Copy | Class constraint | Overlapping tests (per run) | Duration |
|---|---|---|---|
| `CodexTranscriptTailerTests.cs:271` | none | **125–139** distinct tests overlap its 2–4 s window; run-wide max concurrency 25–37 | 2.11–3.71 s (A1–A5), 3.5 s when failing |
| `TranscriptAdoptionSafetyTests.cs:579` | `[NotInParallel("ClaudeConfigDirEnv")]` (class) | **0** in every run | 1.85–1.88 s, every run |

The classes most often overlapping the Codex copy (summed over A1–A5): `PromptSubmissionMatchTests`
145, its own siblings 75, `TranscriptWorkingStateTests` 70, `HerdrClientTests` 60,
`GrokTranscriptTailerTests` 55, `CpuSpinDetectorTests` 35, `DaemonLogRotationTests` 20 (i.e. all
four, every run), `FirstWriteRaceTests` 5, `SessionCpuWatchdogTests` 5.

The failure shape, reproduced (A8):

```
failed First_input_starts_the_refusal_clock_from_the_input_not_the_child_start (3s 508ms)
  ShouldAssertException: unboundSeconds
      should be in range { from = 0.3, to = 1.5 }
      but was 1.8319352d
    at …CodexTranscriptTailerTests.cs:295
```

A9: `1.721`, A10: `1.723`. Every failure is the **upper** bound. The lower bound cannot fail:
`MaybeReportRefusal` (`CodexTranscriptTailer.cs:642`) returns until `now − since ≥ _refusalFaultDelay`
(`:652`), so `unbound` (`:658`) is ≥ 0.4 by construction.

### 1.2 What the test measures, file:line

```csharp
await Task.Delay(TimeSpan.FromMilliseconds(1300));              // :286  tailer polling, no input yet
hub.Count(SessionTranscriptFault).ShouldBe(0);
input.Append("The first prompt delivered …");                   // :289  ← the clock should start here
tailer.UnboundReason.ShouldBe("locating");
var fault = await hub.WaitForAsync(SessionTranscriptFault, 5 s); // :291  50 ms poll on the same pool
var unboundSeconds = fault.RootElement.GetProperty("UnboundSeconds").GetDouble();
unboundSeconds.ShouldBeInRange(0.3, 1.5, …);                     // :295  ← the assertion
```

Tailer side (`CodexTranscriptTailer.LocateAsync`, `:365`; the Claude tailer is the same shape at
`TranscriptTailer.cs:361`):

- each iteration calls `Evaluate()` (`:388`) then `await Task.Delay(_locatePollInterval, ct)`
  (`:438`; 50 ms in these tests, 250 ms in production);
- `refusingSince` is stamped `DateTime.UtcNow` on the **first iteration that observes non-empty
  input** (`:399-400`) — i.e. up to one poll after `Append`, plus whatever the loop is late by;
- the fault fires on the first iteration at which `now − refusingSince ≥ 400 ms`, and reports
  `UnboundSeconds = now − refusingSince` (`:652-658`).

So `UnboundSeconds ≈ 0.4 s + (how late the fault-firing iteration ran)`. The window `[0.3, 1.5]`
therefore asserts *"the iteration after the deadline ran within 1.1 s of it"*. Nothing in the tailer
promises that; the test inherited the assumption from the fresh-process case where a 50 ms
`Task.Delay` really does resume in ~50 ms (isolation: 0.43–0.53 s, §2 E4a).

### 1.3 The stall, from the trace

Temporary instrumentation in `LocateAsync` (env-gated, reverted) logged every `Evaluate()` ≥ 100 ms
and every `Task.Delay(50)` that resumed ≥ 150 ms late, with GC and pool counters. A8, the failing
session `0baa67d0` and its neighbours (times UTC):

```
15:07:30.740 0baa67d0 it=1 SLOW-EVAL 292ms files=2                 ← first pass, cold probe cache
15:07:31.414 0baa67d0 it=2 SLOW-DELAY 589ms (asked 50)
15:07:31.607 0baa67d0 it=3 SLOW-DELAY 192ms (asked 50)
15:07:31.821 0baa67d0 it=4 SLOW-DELAY 205ms (asked 50)
15:07:31.823 0baa67d0 it=5 SINCE-SET at 15:07:31.823 sinceLoopStart=1375ms   ← input observed
15:07:33.650 b6b0bc58 it=12 SLOW-DELAY 1709ms (asked 50)            ← a sibling test's tailer
15:07:33.653 0baa67d0 it=5 SLOW-DELAY 1830ms (asked 50)             ← THE stall
15:07:33.655 0baa67d0 FAULT unbound=1.832s since=15:07:31.823 delay=400ms
15:07:33.721 46557b60 it=9 SLOW-DELAY 1793ms (asked 50)             ← another sibling
```

Three independent tailer loops, each waiting on its own 50 ms timer, all resumed inside 71 ms of
each other after 1.7–1.8 s. `Evaluate()` was never the problem: its maximum across all traced runs
was 577 ms, on a first pass, and 5–20 ms thereafter. The test's own `hub.WaitForAsync` poll is on
the same pool and stalled with them — in E1-1 the fault published at 15:17:11.632 and the test
ended at 15:17:11.636 after a 2.5 s wait.

With counters (A9, A10, F2b runs; `tpThreads=before->after`, `pending=before->after`):

```
15:10:47.204 e1c817c7 it=11 SLOW-DELAY 1540ms gcPause+=0ms gen0+=0 gen2+=0 tpThreads=14->14 (min 8) pending=1->10
15:12:19.359 a3605271 it=10 SLOW-DELAY 1524ms gcPause+=0ms gen0+=0 gen2+=0 tpThreads=24->24 (min 8) pending=0->9
15:17:11.627 dcf92489 it=13 SLOW-DELAY 2601ms gcPause+=0ms gen0+=0 gen2+=0 tpThreads=17->17 (min 8) pending=3->10
```

GC contributed 0–15 ms. The pool's thread count did not move for the duration of any stall while
the pending count climbed. The timer's continuation is a pool work item; when every pool thread is
occupied and the pool does not add one, the continuation waits — and so does every other timer's.

### 1.4 What holds the pool — the co-scheduled set in every failure

TRX windows for the three traced failures (local time = UTC+1), tests spanning the *entire* stall:

| Run | Stall (UTC) | Spanning the whole stall (besides the Codex class's own tests) |
|---|---|---|
| A8 | 15:07:31.85–33.64 | `DaemonLogRotationTests` ×4 (ended 33.727–34.542) · `FirstWriteRaceTests::input_racing_a_cold_launch…` · `SessionCpuWatchdogTests::Idle_session_burning_a_core…` · `GrokTranscriptTailerTests::Child_exit_flushes…` · 4 `Herdr*Tests` |
| A9 | 15:10:45.70–47.19 | `DaemonLogRotationTests` ×4 (ended 47.262–47.505) · `FirstWriteRaceTests::input_racing…` · `SessionCpuWatchdogTests::Idle_session_burning…` · `GrokTranscriptTailerTests::Child_exit_flushes…` · 3 `Herdr*Tests` |
| A10 | 15:12:17.85–19.32 | `DaemonLogRotationTests` ×4 (ended 19.359–19.650) · `FirstWriteRaceTests::input_racing…` · `SessionCpuWatchdogTests::Idle_session_burning…` · `GrokTranscriptTailerTests::Child_exit_flushes…` · 3 `Herdr*Tests` |

The same three spawners every time, from three different lanes:

- `DaemonLogRotationTests` (`DaemonLogRotationTests.cs:17`) — **no constraint at all**. Each of its
  four tests starts `pwsh.exe -File scripts/run-daemon.ps1` (`:135-161`), which starts the fake
  service `cmd` and, in one test, rotates a 3 MB log; all four run concurrently.
- `SessionCpuWatchdogTests` (`SessionCpuWatchdogTests.cs:18`, `"ClaudeConfigDirEnv"`) — starts a
  real session through a detached pty-host.
- `FirstWriteRaceTests` (`FirstWriteRaceTests.cs:16`, `"SessionLiveness"`) — starts a real pty-host.

`NotInParallel` keys serialise only within a key. CARD-0200 §1.5 listed exactly this: six classes
on `"SessionLiveness"`, three on `"ClaudeConfigDirEnv"`, one bare, and **two spawners with no key
(`DaemonLogRotationTests`, `HerdrEventPumpTests`)** — and §4 declared it out of that card's scope
"if anyone sees a whole-assembly flake with that shape". This is that flake. (On re-reading:
`HerdrEventPumpTests` and the other `Herdr*Tests` use the in-process `FakeHerdrServer` named-pipe
fake and start no OS process, so they need no lane — §3.2 lists the ten that do.)

Why the spawners matter to a pool that is *inside* the test process: `Process.Start` is a
synchronous `CreateProcess` on the calling pool thread; `HerdrAdoptionSweepTests.cs:136,184,329`
and `HerdrPaneChildKillTests.cs:76` block a pool thread in `dummy.WaitForExit(5_000)`;
`PtyBackendSeamTests.cs:188` and `SessionRunnerRuntime.cs:2152` call `ReadToEnd()`;
`SessionRunnerRuntime.cs:1708` is a sync-over-async `GetAwaiter().GetResult()` on the herdr
screen read. Each of these parks a pool thread; four pwsh boots plus two pty-host launches at once
park several at the same moment, on top of ~30 other tests' async continuations. The isolated
burner run (§2, E4b) shows that the *external* CPU load of those children is not what stalls the
loop — the pool with nothing pending rides through 100 % CPU with a worst overshoot of 0 ≥ 300 ms.

### 1.5 Why A1–A5 passed and the card saw 3 failures in 4 runs

The stall has to land in the ~1.1 s window *after* `refusingSince` is stamped. A11 shows the miss
case: session `5398a8f5` stalled 1546 ms at iteration 6, *then* observed the input at 15:13:50.923
and faulted cleanly at 0.431 s. The same stall 100 ms later fails the test. Per-run rate here was
3/11 unpatched; the card's 3/4 on a different day is the same coin at a different machine load.

---

## 2. Measurement matrix

Build: `dotnet build tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c0208/`; run:
`dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c0208/ -- [--treenode-filter …] --report-trx`.
Verdicts are the runner's `Test run summary`; "stalls" counts `Task.Delay(50)` overshoots ≥ 800 ms
across all Codex tailer loops in the run (trace); "target" is the Codex copy's `UnboundSeconds`.
`bin-c0208` directories (7) were deleted afterwards.

| Config | Runs | Target failures | Stalls ≥ 800 ms per run (worst) | Notes |
|---|---:|---:|---|---|
| **A** full assembly, as-is, untraced (A1–A5) | 5 | 0 | — (no trace) | target dur 2.11–3.71 s; max concurrency 25–37 |
| **A** full assembly, as-is, traced (A6–A11) | 6 | **3** (1.832, 1.721, 1.723) | 3, 8, 3, 11, 4, 3 (1.55–2.10 s) | A6 also failed `HerdrClientTests::Report_methods_stamp_source…` — pipe connect > 2000 ms (§5) |
| **E1** `--maximum-parallel-tests 8` | 3 | 0 | 9, 3, 10 (2.0–2.6 s) | concurrency 13; a sibling faulted at 2.990 s; **cap does not remove the stall** |
| **E4a** target alone | 20 | 0 | 0 (0 ≥ 300 ms) | 0.431–0.526 s (+ one 0.957 s *repeat* fault at the 500 ms repeat interval — not a first fault) |
| **E4b** target alone + 6-worker CPU burner, machine at 100 % | 20 | 0 | 0 (0 ≥ 300 ms) | 0.407–0.474 s — **external CPU load alone does not reproduce it** |
| **E5** `CodexTranscriptTailerTests` class alone (16 tests, 14 concurrent) | 10 | 0 | 0 (0 ≥ 300 ms) | the class's own concurrency is not the trigger |
| **F1** full assembly + `ProcessSpawnLimit` lane on 10 classes (D2, applied locally) | 3 | 0 | **0, 0, 0 (281 ms)** | wall 2m05–2m41 vs 1m28; load-before 48–88 % |
| **F2b** full assembly + `ThreadPool.SetMinThreads(64)` (diagnosis only, applied via the trace hook) | 3 | 0 | **0, 0, 0 (571 ms)** | load-before 68–93 %; `GetMinThreads` read 64 (the `DOTNET_ThreadPool_MinThreads` env var did **not** take through `dotnet run` — it read 8 — hence the hook) |
| **F3** full assembly + D1 assertion in both copies (applied locally), no lane, no floor | 4 | **0** | 5, 12, 3, 5 (1.5–3.2 s) | target faults of **3.348 s** (F3-1) and **3.075 s** (F3-4) passed; load-before 86–100 %. F3-3 failed an unrelated test (§5) |

Reading across the rows: the stall needs the full assembly (E5, E4a), is not CPU (E4b), is not the
number of concurrent tests as such (E1), is removed by giving the pool more threads (F2b) or by
serialising the spawners (F1), and the assertion survives it when it measures the right quantity (F3).

---

## 3. The fix (Design)

### 3.1 D1 — the assertion, both copies

`CodexTranscriptTailerTests.cs:289-296` (and the same lines in the Claude copy,
`TranscriptAdoptionSafetyTests.cs:600-607`). Measured shape, applied for F3 and reverted:

```csharp
input.Append("The first prompt delivered after this session waited for Codex");
var sinceInput = Stopwatch.StartNew();
tailer.UnboundReason.ShouldBe("locating");
var fault = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptFault, TimeSpan.FromSeconds(5));
var elapsedSinceInput = sinceInput.Elapsed.TotalSeconds;

fault.ShouldNotBeNull();
var unboundSeconds = fault!.RootElement.GetProperty("UnboundSeconds").GetDouble();
unboundSeconds.ShouldBeGreaterThanOrEqualTo(0.3, "the fault must wait out the refusal delay");
unboundSeconds.ShouldBeLessThanOrEqualTo(elapsedSinceInput + 0.1,
    $"the refusal clock starts when input is delivered ({elapsedSinceInput:F2}s ago), "
    + "not when the child started (an hour earlier) or when the tailer started (1.3s earlier)");
```

Why this is the *right* bound and not a wider one:

- `refusingSince` is stamped strictly after `Append` (it is the first iteration to see the input),
  and the fault is published strictly before `WaitForAsync` returns, so
  `UnboundSeconds ≤ elapsedSinceInput` holds for every interleaving — a stall stretches both
  numbers together. The `+0.1` covers `DateTime.UtcNow` vs `Stopwatch` granularity only.
- The regressions the test exists to catch still fail it by a wider margin than before: a clock
  started at `tailer.Start()` reports `elapsedSinceInput + ≥ 1.3 s`; one started at `childStartUtc`
  reports `≥ 3600 s`. The old window rejected the first by 0.2 s (1.7 vs 1.5); the new bound rejects
  it by ≥ 1.2 s.
- The lower bound is unchanged and remains meaningful: it pins that the fault waited out the delay.
- No product timing constant moves; no test timing constant moves (`1300`, `400`, `50`, the 5 s
  wait). This is the "measure what you mean" fix, not a wider net.

### 3.2 D2 — the spawn lane (separate slice, same card)

Add `tests/Antiphon.SessionRunner.Tests/ProcessSpawnLimit.cs` — a copy of
`tests/Antiphon.Agents.Pty.Tests/ProcessSpawnLimit.cs` (`Limit => 1`, namespace
`Antiphon.SessionRunner.Tests`) with a summary that names CARD-0208 and the measured 2.1 s → 281 ms —
and put `[ParallelLimiter<ProcessSpawnLimit>]` on the classes that create a real OS process (grep:
`Process.Start`, `RunnerLaunchRequest`, `StartSessionWithTranscriptAsync`, `LaunchAsync(`,
`.StartAsync(`; then confirmed by reading):

| Class | Child it starts | Existing constraint (kept) |
|---|---|---|
| `DaemonLogRotationTests` | `pwsh.exe` → `run-daemon.ps1` → `cmd` fake service, ×4 concurrently today | none |
| `FirstWriteRaceTests` | detached pty-host | `"SessionLiveness"` |
| `HerdrAdoptionSweepTests` | `cmd.exe` dummies (`StartDummy`, `:557`) | `"SessionLiveness"` |
| `HerdrPaneChildKillTests` | `cmd.exe` dummy (`:97`) | `"SessionLiveness"` |
| `PtyBackendSeamTests` | pty-host via `--spawn` | bare `[NotInParallel]` |
| `PtyHostAdoptionTests` | detached pty-host | `"SessionLiveness"` |
| `SessionBufferBoundsTests` | pty-host | `"SessionLiveness"` |
| `SessionCpuWatchdogTests` | real session / pty-host | `"ClaudeConfigDirEnv"` |
| `SessionLivenessTests` | `cmd /c exit 0` (`:110-115`) and pty-hosts | `"SessionLiveness"` |
| `TranscriptAdoptionSafetyTests` | one `cmd.exe` via pty-host (CARD-0200 §1.7) | `"ClaudeConfigDirEnv"` |

Not on the lane, deliberately: `HerdrClientTests`, `HerdrEventPumpTests`, `HerdrLaunchShapeTests`,
`HerdrRunnerSessionTests`, `HerdrStatusPushTests` (all `FakeHerdrServer`, in-process named pipes,
no `Process.Start`), `PtyHostLeakSweep` (a `[Before(Assembly)]`/`[After]` sweep, not a test class).
Adding the lane to a class with an existing `NotInParallel` key is additive: TUnit honours both.

Cost, measured: full-assembly wall 1m28 → 2m05–2m41 (F1). That is the price of the AGENTS.md rule
("a new class that starts a child must take the same attribute") finally applying to this assembly,
and it is what protects every other fixed-window wait in it (§5). If the wall time matters more
than that later, the lever is `DaemonLogRotationTests` (four pwsh boots for one script's four
branches), not the lane.

### 3.3 Order and independence

Land D1 first, on its own commit, measured on its own (§4). Land D2 second, on its own commit,
measured by the stall count (§4). Doing them together would repeat CARD-0200's warning: "adding it
here would muddy the evidence" — D1 must be shown sufficient for *this* test without the lane,
because the lane is a hygiene change that other cards may loosen, and D2 must be shown to remove
the stalls without leaning on D1.

### 3.4 Noted, not planned: clock injection

Both tailers read `DateTime.UtcNow` directly (`CodexTranscriptTailer.cs:400,488,652`;
`TranscriptTailer.cs:425,791,820`) and poll with `Task.Delay`. A `TimeProvider` (with
`Task.Delay(…, timeProvider)`) would make `First_input_starts_the_refusal_clock…` and its four
siblings (`Stale_same_cwd_rollouts…`, `Input_delivered_before_a_restart…`, both `Child_exit…`)
deterministic and instant instead of 0.8–4 s of wall-clock waiting each. It touches ~10 call sites
per tailer plus the constructor signature and the runtime's two construction sites, and it is not
needed to fix this card. Worth its own card if the assembly's wall time or a further timing flake in
these classes ever justifies it.

---

## 4. Build pass — steps and evidence bar

Build to an alternate output path throughout (daemons hold `bin/`); delete the `bin-<name>` dirs
(~7 for this project graph) before finishing.

1. **D1.** Apply §3.1 to both copies. Rebuild. Run the full assembly **≥ 10 times** (`~1m30` each;
   chunk the loop under the 10-minute foreground window — 5 per call). Bar: 0 failures of either
   copy; no other timing value in the two tests changed. Run the two tests alone ×5 as a sanity
   check that the lower bound still holds (expect 0.41–0.53 s). Commit with the counts.
2. **D2.** Add `ProcessSpawnLimit.cs` and the ten attributes. Rebuild. To measure the *stall*, not
   just pass/fail, re-apply the trace hook locally (the reverted diff is described in §1.3: env-gated
   `File.AppendAllText` of every `Task.Delay` overshoot ≥ 150 ms in `LocateAsync`) or, cheaper,
   assert the observable: run the full assembly ×5 and record the Codex copy's duration from the
   TRX — with the lane it sat at 1.83–2.12 s (F1) against 2.11–3.71 s without. Bar: 0 failures and
   no overshoot ≥ 800 ms in ≥ 3 traced runs. Revert the hook; commit with the counts and the wall
   time delta.
3. Update AGENTS.md's TUnit bullet ("Process-spawning tests … also carry
   `[ParallelLimiter<ProcessSpawnLimit>]`") to say the lane now exists in all **four** pty-touching
   assemblies, naming `Antiphon.SessionRunner.Tests`.
4. Close CARD-0208 with a note pointing at CARD-0200 §4 (the gap it declared) and at §5 below for
   the two sightings that are not this card.

---

## 5. Found on the way (not this card's cause; recorded so nobody re-derives them)

- **`HerdrClientTests::Report_methods_stamp_source_antiphon_and_tab_close_acks`** failed once
  (A6) with `HerdrBackendUnavailableException: named pipe … did not accept a connection within
  2000 ms` (`HerdrClient.cs:412`, `ConnectTimeoutMs = 2_000` at `HerdrClientTests.cs:382`). The
  `FakeHerdrServer` accept loop is a pool work item; a 2 s pipe-connect timeout is the same
  fixed-window-inside-a-starved-pool shape as this card. D2 is expected to cover it; if it recurs
  after D2 it needs its own card, and the fix is not a wider timeout.
- **`TranscriptAdoptionSafetyTests::ToDto_without_a_tailer_leaves_transcript_bind_unknown`** failed
  once (F3-3, machine at 100 %) with `IOException: Access to the path
  '…\antiphon-adoption-tests-<guid>\pty-hosts\bin\20260826-160651-8cd58e49.copying' is denied` —
  a `ShadowCopyStore` staging directory under a per-test temp root. One sighting; different
  mechanism (a directory rename/delete race, not a timer); not investigated here.
- **`--list-tests` ignores `--treenode-filter`** in TUnit 1.44.0 (prints all 234), and neither
  `/*/*/(A|B)/*` (matched 0) nor `/*/*/A/*|/*/*/B/*` (matched A only) expressed a two-class union.
  Attribution by co-scheduling two classes in one process therefore had to be done by applying the
  candidate fix instead (F1) — which is the stronger experiment anyway.
- **`DOTNET_ThreadPool_MinThreads` did not reach the test host through `dotnet run`** (the process
  reported `GetMinThreads` = 8 with the variable set to `40`). The F2b diagnosis used an in-process
  `SetMinThreads(64)` behind a temporary env gate instead.

---

## 6. Artefacts

All under the session scratchpad, not committed:
`…\scratchpad\c0208\run-{A1..A11,E1-1..3,E4a-*,E4b-*,E5-*,F1-1..3,F2-1..3,F2b-1..3,F3-1..4}.log`
(runner output), `c0208-*.trx` (per-test start/end for the overlap analysis), `trace-*.log` (the
tailer loop trace), `Parse-Trx.ps1` / `Win.ps1` (overlap and stall-window queries), `burner.ps1`
(the 6-worker CPU load). The numbers in §2 are copied from those files; nothing in this document
is estimated.
