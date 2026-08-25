# CARD-0200 — `TranscriptAdoptionSafetyTests` flake: a zero-wait read of an asynchronous re-adoption bind — plan

**Date:** 2026-08-25 · **Card:** CARD-0200 (`bbf04ca0-3315-41f6-9ed0-b1cd92841e04`) · **Status:** plan
(investigate + design; no implementation in this pass — the one code change below was applied
locally as an experiment, measured, and reverted before commit) · **Verified against:** `master` @
`5e50deb`, worktree `card-task-a6437d7a`. Every file:line below was re-read out of the code on that
commit; every count below was measured on this machine (8 cores, Windows 10 19045, TUnit 1.44.0,
Microsoft Testing Platform 2.2.2, .NET 9.0.16).

---

## Verdict up front

**The card's hypothesis is wrong on both halves, and the fix it points at would change nothing.**

1. The class does **not** run its 36 tests concurrently. `[NotInParallel("ClaudeConfigDirEnv")]` at
   class level puts every test in the class on the same constraint key, and TUnit's contract for a
   key is "not executed in parallel with other tests that share the same constraint keys" — applied
   to "a test method, test class, or assembly" (`TUnit.Core.NotInParallelAttribute` XML doc,
   1.44.0). Measured, not assumed: the TRX of a full-class run shows **0 overlapping pairs of 36**
   (§1.1). Adding `[ParallelLimiter<ProcessSpawnLimit>]` — which does not even exist in this
   assembly (§1.5) — would cap at 1 something that is already at 1.
2. The failing assertion is **not** the 10-second `TranscriptBound` poll after the exact-id bind.
   It is the line *after* the runner restart, `adopted.TranscriptBound.ShouldBe(true)`
   (`TranscriptAdoptionSafetyTests.cs:1569`), which reads the DTO **with no wait at all** immediately
   after `AdoptOrphanedHostsAsync` returns. The sidecar re-tail that would make it true runs on a
   `Task.Run` the adoption path fires three lines before it returns (§1.3). Whether the test wins
   or loses that race is thread-pool scheduling — which is exactly why it is 0/15 alone and 3/9
   inside the class (§1.4).

| # | Decision | One line |
|---|---|---|
| D1 | Fix the **test**, not the runtime: poll the DTO for `TranscriptBound == true` after re-adoption, the same way the same test already polls after the exact-id bind (and every sibling polls via `PollForEntriesAsync`) | The assertion assumed an async bind was synchronous; the runtime's "unbound while locating" state is documented and correct |
| D2 | **No** `ParallelLimiter`, **no** `NotInParallel` change, **no** wider deadline | None of them touch the mechanism; landing one would put a false causal story into the repo's record |
| D3 | Do **not** make `TranscriptTailer.Start()` bind the sidecar path synchronously | It would put filesystem I/O on the adoption sweep's critical path for N sessions to satisfy a test's timing assumption — rejected, stated in §2.3 |
| D4 | Evidence bar for the build pass: **≥ 15 consecutive clean full-class runs** (unpatched base rate ≈ 33 % ⇒ P(15 clean by luck) ≈ 0.25 %) plus the rest of the assembly green once | Already achieved by the experiment (0/15 patched vs 3/9 unpatched, §1.6); the build pass reproduces it on its own commit |

Net diff: ~12 lines inside one test method (or ~20 if the two identical polls are hoisted into a
helper), plus ~4 lines in the same method so it stops leaking a 24-hour pty-host per run — a
separate defect in the same test, found during cleanup (§1.7, §2.1b). **Risk: nil** — it is a test-only change that makes a wait explicit; it cannot make a
genuinely broken bind pass, because the bar (`TranscriptBound == true` **and**
`TranscriptBindHow == Sidecar`) is unchanged.

---

## 1. Established facts (Investigate, this pass)

### 1.1 The class runs strictly serially — measured

Command (built to an alternate output path because the always-on daemons hold `bin/`):

```
dotnet build tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c0200/
dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c0200/ -- \
  --treenode-filter "/*/*/TranscriptAdoptionSafetyTests/*" --report-trx --report-trx-filename c0200-run1.trx
```

The TRX carries `startTime`/`endTime` per test. Sorted by start, **every** test's start is at or
after the previous test's end — `overlapping pairs: 0 of 36`, wall 62 s ≈ the sum of the 36
durations. First test 17:04:36.455, last (`ToDto…`) 17:05:35.103 → 2.73 s. There is no
intra-class concurrency to cap.

The comment on the attribute (`:25`, `// mutates the process-wide CLAUDE_CONFIG_DIR variable`) is
the reason it is there: `TranscriptTree`'s constructor sets the env var (`:1674`) and
`Dispose` nulls it (`:1689`), and `TranscriptTailer.ResolveProjectsRoot` reads it once per
`LocateAsync` (`TranscriptTailer.cs:349`, `:889-895`). The same key is on
`TranscriptTailerCompactionTests` and `SessionCpuWatchdogTests`, so those three classes form one
serial lane. The key's side effect is that the whole 36-test class is serialised too.

### 1.2 The failure is at `:1569`, after the restart — reproduced 3 times in 9 runs

Nine unpatched full-class runs (one with the TRX above, eight in a loop): **3 failed, 6 passed**.
All three failures are identical:

```
FAILED: ToDto_reports_unbound_while_locating_exact_after_bind_and_sidecar_after_readopt (00:00:02.34)
ShouldAssertException: adopted.TranscriptBound
    should be True
    but was False
   at …TranscriptAdoptionSafetyTests.cs:line 1569
```

The 10-second exact-bind poll (`:1547-1558`) passed in every run — it is the *next* block that
fails, and it has no poll:

```csharp
(await runtimeB.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), cts.Token))
    .ShouldBe(1);                                                    // :1566-1567
var adopted = runtimeB.Get(sessionId);                               // :1568
adopted.TranscriptBound.ShouldBe(true);                              // :1569  <-- fails
adopted.TranscriptBindHow.ShouldBe(TranscriptBindMethods.Sidecar);   // :1570
```

Failing runs take ~2.3 s, passing ones ~2.7 s: the failing run simply asserted earlier than the
bind landed.

### 1.3 The mechanism, file:line

`AdoptOrphanedHostsAsync` (`SessionRunnerRuntime.cs:396`) → per-session `AdoptAsync` →

```csharp
RestoreTailerFromSidecar(sidecar, cwd, manifest.ChildStartTimeUtc ?? sidecar?.ChildStartUtc); // :1291
…
return true;                                                                                 // :1294
```

with no `await` between the two. `RestoreTailerFromSidecar` (`:1570-1618`) builds a
`TranscriptTailer` with `knownTranscriptPath: sidecar?.TranscriptPath` and calls
`_tailer.Start()`, which is

```csharp
public void Start() => _loop = Task.Run(() => RunAsync(_cts.Token));   // TranscriptTailer.cs:143
```

`RunAsync` → `LocateAsync`, whose **first** step is the sidecar bind
(`:370-376`: `_knownTranscriptPath` exists ⇒ `TryBind(known, Sidecar)`), and `TryBind` is what sets
`BoundTranscriptPath` (`:487`) and `BindHow` (`:488`). `ToDto` reports

```csharp
TranscriptBound: _tailer is null ? null : boundPath is not null,       // SessionRunnerRuntime.cs:1675
TranscriptBindHow: boundPath is not null ? _tailer!.BindHow : null,    // :1676
```

So between `AdoptOrphanedHostsAsync` returning and the thread pool running that `Task.Run`, the DTO
truthfully reads `TranscriptBound: false, TranscriptBindHow: null` — the very "unbound while
locating" state the test's own name asserts for the *fresh* launch at `:1541-1542`. The test then
expects the re-adopted session to have skipped that state, which nothing guarantees.

Note that Exact and Sidecar binds deliberately publish **no** `SessionTranscriptBound` hub event
(`TranscriptTailer.cs:492-499`), so there is no event to await here; polling the DTO is the
available signal, and it is the one the same test already uses ten lines earlier.

### 1.4 Why it looks class-dependent (and is not spawn contention)

| Shape | Runs | Failures |
|---|---|---|
| Test alone (`--treenode-filter …/ToDto_reports_unbound…`) | 15 | **0** |
| Full class, unpatched | 9 | **3** |
| Full class, with the §2 poll applied locally | 15 | **0** |

The race is `Task.Run` scheduling versus the test thread's next statement. In a fresh process the
pool is idle and the sidecar step (one `File.Exists` + an in-memory `TryClaim`) completes in
microseconds — the test "reliably passes alone". Thirty-five tests in, the pool has queued
continuations from the previous tests' hub pumps, cancelled tailer loops and pty-host client
readers, and the `Task.Run` lands late often enough to lose one time in three. That is a
thread-pool-state effect, **not** process-spawn contention: this test spawns exactly one
`cmd.exe` (via a detached pty-host) in both shapes, nothing else is spawning concurrently in either
shape (§1.1), and the step that has to win the race does no process work at all.

Corollary: the card's "reproduce it and it shows 1/36" is the per-test failure count in a run, not
a 1-in-36 rate. The per-run rate on this machine is ~33 %.

### 1.5 `ProcessSpawnLimit` does not exist in this assembly — and could not be the cause anyway

`ProcessSpawnLimit` is defined three times, once per assembly that uses it:
`tests/Antiphon.Agents.Pty.Tests/ProcessSpawnLimit.cs`, `tests/Antiphon.PtyHost.Tests/ProcessSpawnLimit.cs`,
`tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs`. **`Antiphon.SessionRunner.Tests` has no
copy and no `[ParallelLimiter<…>]` anywhere.** Its process-spawning classes are serialised by
`[NotInParallel]` keys instead: `"SessionLiveness"` on six classes (`FirstWriteRaceTests`,
`HerdrAdoptionSweepTests`, `HerdrPaneChildKillTests`, `PtyHostAdoptionTests`,
`SessionBufferBoundsTests`, `SessionLivenessTests`), `"ClaudeConfigDirEnv"` on three, bare
`[NotInParallel]` on `PtyBackendSeamTests`, and two spawners with no key at all
(`DaemonLogRotationTests`, `HerdrEventPumpTests`).

That is a real gap against AGENTS.md's "a new class that starts a child must take the same
attribute" — in a **whole-assembly** run, a `"SessionLiveness"` class and a `"ClaudeConfigDirEnv"`
class *can* spawn hosts side by side. But it cannot be this card's cause: the card's repro (and
every run above) is filtered to one class, where nothing overlaps, and the failure still occurs at
33 %. It is recorded in §4 as out of scope, not folded into this fix.

### 1.6 The experiment that confirms the mechanism

The §2 poll was applied locally to `:1568-1570` only (no other change), the assembly rebuilt to
`bin-c0200/`, and the full class looped: **15 runs, 0 failures** (wall 62-72 s each), against 3/9
unpatched on the same build host, same session, same alternate output path, immediately before.
The patch was then reverted (`git checkout -- …`; working tree clean before the plan commit).

### 1.7 Found on the way: the same test leaks one pty-host per run, for 24 hours

Not the flake's cause, but the same test and the same build pass. Cleaning up after the runs above
found **47 lingering `Antiphon.PtyHost.exe` processes**, every one launched from
`%TEMP%\antiphon-0180-dto-<guid>\pty-hosts\bin\…` — this test's `logRoot` (`:1514`) — 39 from this
pass's 39 runs (pass and fail alike) and 8 from earlier verifiers' runs of the same test. No other
test in the class or assembly had left one.

Mechanism: the test builds `new SessionRunnerSettings { SessionLogPath = logRoot }` (`:1517`) and so
inherits `PtyHostLingerHours = 24` (`SessionRunnerSettings.cs:73`). `runtimeB.KillAsync` (`:1572`)
and the `finally`'s `Process.GetProcessById(childPid).Kill(entireProcessTree: true)` (`:1578`) kill
the **child** (`cmd.exe`); the **host** is the child's parent, not its descendant, and by design
lingers for the TTL after its child exits (`HostSession.cs:312`) so a restarted runner can collect
the exit. Two knock-ons: the host's cwd is the test process's cwd, so it pins the test **output
directory** (`bin/` or `bin-<name>/`) against deletion; and its exe lives under `logRoot`, so the
`Directory.Delete(logRoot)` at `:1581` silently fails and the temp tree stays too. Every other
host-spawning class in the assembly sets `PtyHostLingerHours = 0.02` (72 s) and kills the host pid
from the manifest/DTO in teardown (`PtyHostAdoptionTests.cs:207`, `SessionBufferBoundsTests.cs:222`,
`SessionLivenessTests.cs:186`, …); this test does neither.

---

## 2. Design (the fix the build pass implements)

### 2.1 The change

In `ToDto_reports_unbound_while_locating_exact_after_bind_and_sidecar_after_readopt`, replace the
zero-wait read at `:1568-1570` with the same poll the method already uses at `:1547-1558`:

```csharp
(await runtimeB.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), cts.Token))
    .ShouldBe(1);

// The sidecar re-tail runs on the tailer's own loop (TranscriptTailer.Start => Task.Run), which
// AdoptOrphanedHostsAsync does not wait for — a zero-wait Get reads "unbound while locating"
// about one run in three (CARD-0200). Poll, exactly as the exact-id bind above is polled.
RunnerSessionDto? adopted = null;
var adoptDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
while (DateTime.UtcNow < adoptDeadline)
{
    adopted = runtimeB.Get(sessionId);
    if (adopted.TranscriptBound == true)
        break;
    await Task.Delay(100);
}

adopted.ShouldNotBeNull();
adopted!.TranscriptBound.ShouldBe(true);
adopted.TranscriptBindHow.ShouldBe(TranscriptBindMethods.Sidecar);
```

The bar is unchanged: the DTO must reach `TranscriptBound == true` **and** report
`TranscriptBindHow == Sidecar`. A re-adoption that re-discovered instead of re-tailing (`Exact`)
or never bound still fails, now with the same 10 s budget the exact-id half already gets.

**Optional tidy (implementer's call, same behaviour):** hoist both polls into one private helper
on the class —

```csharp
private static async Task<RunnerSessionDto> WaitForTranscriptBoundAsync(
    SessionRunnerRuntime runtime, Guid sessionId, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    RunnerSessionDto dto;
    do
    {
        dto = runtime.Get(sessionId);
        if (dto.TranscriptBound == true) return dto;
        await Task.Delay(100);
    } while (DateTime.UtcNow < deadline);
    return dto;
}
```

— and call it at both sites (`:1547` and the new one). The exact-id site keeps its assertions
verbatim. Keep the 10 s budget: it is the class's standing figure (`PollForEntriesAsync(...,
TimeSpan.FromSeconds(10))` at `:611`, `:1465`, and elsewhere), it is a ceiling not a wait (the
bind lands in milliseconds; a passing test costs nothing extra), and a shorter one would
re-introduce exactly the timing assumption being removed.

### 2.1b Same test, same pass: stop leaking the host (§1.7)

Two lines of the shape every sibling already uses:

```csharp
var settings = new SessionRunnerSettings { SessionLogPath = logRoot, PtyHostLingerHours = 0.02 };  // :1517
…
int? childPid = locating.Pid;
int? hostPid = locating.HostPid;                    // RunnerSessionDto.HostPid, SessionRunnerContracts.cs:87
…
finally
{
    foreach (var pid in new[] { childPid, hostPid })
    {
        if (pid is int p)
        {
            try { Process.GetProcessById(p).Kill(entireProcessTree: true); }
            catch (ArgumentException) { /* already gone */ }
        }
    }
    try { Directory.Delete(logRoot, recursive: true); } catch { /* best effort */ }
}
```

`ToDto` populates it (`SessionRunnerRuntime.cs:1671`, `_hostPid > 0 ? _hostPid : null`); if it were ever null, read it from the
manifest as `PtyHostAdoptionTests.cs:155` does:
`PtyHostManifest.TryLoad(PtyHostManifest.PathFor(settings.PtyHostManifestDir, sessionId))!.HostPid`.
Kill the host **after** `runtimeB.KillAsync` has done its job (it is in `finally`, so it is) — the
test's re-adoption half needs the host alive until then. The linger value alone would also clear
the leak, 72 s later; the explicit kill is what lets `Directory.Delete(logRoot)` succeed inside the
test.

### 2.2 What does not change

- `[NotInParallel("ClaudeConfigDirEnv")]` stays as is. It is doing the job its comment says.
- No `ParallelLimiter`, no `ProcessSpawnLimit` copied into this assembly for this card (§1.5, §4).
- No runtime change (§2.3). No AGENTS.md change: "poll a DTO, don't read it once" is already this
  class's own convention — the fix brings the one outlier into line rather than adding a rule.
- The card's `CARD-0186 S2` note stands: unrelated code path, confirmed.

### 2.3 Rejected alternatives, stated

| Alternative | Why not |
|---|---|
| `[ParallelLimiter<ProcessSpawnLimit>]` on the class or the test | Measured serial already (0 overlaps of 36). It would "fix" nothing, pass by luck on the next run, and record a wrong cause. |
| Widen the 10 s exact-bind deadline | That poll never failed. |
| Make `TranscriptTailer.Start()` perform the sidecar bind synchronously when `knownTranscriptPath` is set | Puts `File.Exists` + claim + `onBound` (a sidecar **write**, `SaveSidecar`) on `AdoptOrphanedHostsAsync`'s critical path for every session in a restart sweep, and changes a documented production state ("unbound while locating") to suit one assertion. The runtime is right; the test's expectation of it was not. |
| `await _tailer.Loop`-style hook / a `Bound` `TaskCompletionSource` on the tailer for tests | More surface for the same result; the DTO poll is already the class's idiom and needs no production seam. |
| Sleep a fixed 200 ms before the read | The class-wide `Task.Delay` idiom is polling with a deadline for a reason — a fixed sleep is the same bug with a different constant. |

---

## 3. Verification (what the build pass must show)

Base rate to beat is ~33 % per full-class run, so single green runs prove nothing.

1. **Full class, ≥ 15 consecutive runs, 0 failures.** Reuse the loop (a one-off script, not
   committed; the shape is):

   ```powershell
   dotnet build tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c0200/
   1..15 | ForEach-Object {
     dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c0200/ -- `
       --treenode-filter "/*/*/TranscriptAdoptionSafetyTests/*" 2>&1 | Select-String 'failed:'
   }
   ```

   Every line must read `failed: 0`. P(15 clean by luck at 33 %) ≈ 0.25 %; the experiment in §1.6
   already hit 0/15. Report the count, not "green".
2. **The test alone, 5 runs, 0 failures** (it never failed alone; this guards against the edit
   itself regressing the exact-id half).
3. **The whole `Antiphon.SessionRunner.Tests` assembly once, green** — the only other thing the
   edit could touch is compile. Do not co-schedule it with `Antiphon.Agents.Pty.Tests`
   (AGENTS.md).
4. **Timing sanity:** the test's duration stays ~2.5 s (TRX `duration`). A jump to ~12 s would
   mean the poll is timing out and the `Sidecar` assertion is being reached late — investigate,
   do not widen.
5. **Negative check that the bar still bites:** temporarily change the final assertion to
   `ShouldBe(TranscriptBindMethods.Exact)` and confirm it fails (re-adoption must re-tail, not
   re-discover); revert. One run is enough — this is deterministic.
6. **Leak check (§1.7 / §2.1b):** after the loop,
   `Get-Process Antiphon.PtyHost | Where-Object Path -like '*antiphon-0180-dto*'` returns
   **nothing** and `Get-ChildItem $env:TEMP -Directory -Filter 'antiphon-0180-dto-*'` is empty.
   Before this fix each full-class run left exactly one of each. (This pass killed the 47 it
   found — all from this test — before writing the plan; do not count pre-existing ones.)
7. Delete every `bin-c0200/` directory afterwards (`Get-ChildItem -Recurse -Depth 2 -Directory
   -Filter bin-c0200 | Remove-Item -Recurse -Force`) — ~12 of them, one per project in the graph.

Commit message should carry the counts (e.g. `15/15 full-class runs green, was 3/9`), per this
repo's "the commit message is read in preference to the report" rule.

---

## 4. Out of scope, stated

- **`Antiphon.SessionRunner.Tests` has no `ProcessSpawnLimit` lane** (§1.5). Its spawning classes
  are serialised by `NotInParallel` keys that do not all match, and two (`DaemonLogRotationTests`,
  `HerdrEventPumpTests`) carry none. In a full-assembly run, hosts from different key groups can
  overlap. That is the AGENTS.md rule not being applied to this assembly; it is a separate card if
  anyone sees a whole-assembly flake with that shape. It is **not** this card's cause and adding
  it here would muddy the evidence that the poll alone is sufficient.
- The Exact/Sidecar binds' lack of a `SessionTranscriptBound` event (`TranscriptTailer.cs:492-499`)
  is deliberate (audit-trail noise) and is not revisited.

---

## 5. Artefacts from this pass

- Nothing committed but this document. The experimental poll was applied, measured (§1.6) and
  reverted; `git status` was clean before the plan commit.
- Loop/TRX-analysis scripts lived in the gitignored `.antiphon/` of the worktree
  (`c0200-loop.ps1`, `c0200-trx.ps1`); TRX files in `bin-c0200/TestResults/`, deleted with the
  build output.
