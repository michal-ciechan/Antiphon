# CARD-0222 — why `Antiphon.Tests` "hangs" (fable, 2026-08-28)

Brief: reproduce `HerdrAlwaysOnChannelParityTests` hanging on purpose, isolate the Herdr vs PtyHost
arm, get a stack or a dump of the hung process instead of a CPU snapshot, and say whether this and
CARD-0165 ("full `Antiphon.Tests` run silently stalls partway through") are one bug. The plan is
`docs/superpowers/plans/2026-08-28-card-0222-tests-hang-plan.md`; the ledger guard it names is
already on master (`Directory.Build.targets`).

**Verdict up front: there are two unrelated "hangs" behind tonight's reports, both reproduced on
demand, both with the process caught in the act, and neither is herdr-specific, CPU-related, or
Testcontainers.**

1. **The test process hangs in a poll loop against a frozen clock.** The test harness registers its
   own `MutableTimeProvider` whose `GetUtcNow()` only moves on an explicit `Advance()`, as the one
   `TimeProvider` for the whole server graph. `SessionMessageQueueService` has six wait loops of the
   shape `deadline = UtcNow() + N; while (…) await Task.Delay(poll, _timeProvider, ct)`. The delay
   runs in real time (the provider does not override `CreateTimer`), the deadline never arrives, so
   a delivery that enters `SettlePostEvidenceAsync` with live session metadata polls forever at
   ~0.15 s CPU per 10 s. `dumpasync` on two hung processes shows exactly that leaf, on **both**
   arms. It is intermittent because whether the loop is entered depends on a race (whether the
   runtime holds `LastSequence` for the session when the delivery reaches the settle), not on load.
   A three-line change to the test's clock — an offset over the real clock instead of a frozen
   instant — turns four consecutive ≥300 s hangs into 5/5 passes in 23–25 s. **That change is
   flagged for whoever next touches `HerdrAlwaysOnChannelParityTests.cs`, not committed** (the
   brief's CARD-0224 collision rule; the exact patch is in §2.5).
2. **The build in front of the tests takes 20+ minutes at near-zero CPU, which reads as a hang.**
   MSBuild's per-project file-writes ledger (`obj/…/<Project>.csproj.FileListAbsolute.txt`) had
   grown to **228 MB / 770,706 lines** for `Antiphon.SessionRunner` and **97 MB / 520,713 lines**
   for `Antiphon.Tests`, because every `--property:OutputPath=bin-<name>/` build shares one `obj/`
   and appends, and `IncrementalClean` only ever prunes entries under the *current* `OutDir`.
   `IncrementalClean` reads, filters and rewrites that ledger on every build. Measured: a
   `dotnet build tests/Antiphon.Tests` took **21m31s**; the Tests project alone spent **157 s of
   181 s** in `ReadLinesFromFile` + `FindUnderPath`; with the ledger reset the same build is
   **13 s**, and the full graph **1m28s**. A guard target now deletes a ledger over 2 MB before
   the SDK reads it (§3.4), and the three bloated ledgers in the main checkout were reset.

**CARD-0165 is not this bug, on dates alone** — the class with the frozen clock was added on
2026-08-25 (`02df16f`), the day *after* CARD-0165's two stalls — and it is not the ledger either
(that cost is paid before any test output; CARD-0165 stalled after hundreds of tests had printed).
It may well be the same **defect class**: `AgentSupervisionTests` (which existed on 08-24, is
`[NotInParallel]` like this one, and runs in the same end-of-run phase) carries a byte-identical
frozen `MutableTimeProvider`, and six more harnesses register `FakeTimeProvider(DateTimeOffset.UtcNow)`
over the same queue service. One targeted run of `AgentSupervisionTests` passed 7/7 in 18 s, so
that is a candidate, not a finding. §4 has the recipe that would settle it in one hung run.

## 1. Method and tools

- Builds to `--property:OutputPath=bin-c222/` (the daemons hold `bin/`); all `bin-c222` dirs
  deleted afterwards.
- A watchdog runner (`run-watched.ps1`, scratchpad) starts `Antiphon.Tests.exe` (or
  `dotnet run --no-build`) with a tree-node filter, tails its output, and if the process is still
  alive at the deadline: samples CPU over 10 s, lists the process tree and the Testcontainers
  containers, runs **`dotnet-stack report -p`** (managed thread stacks, no dump needed), optionally
  **`dotnet-dump collect`**, and only then kills. Both tools were installed as global tools tonight
  (`dotnet tool install -g dotnet-stack` / `dotnet-dump`); `procdump` is also on the machine.
- **`dotnet-dump analyze <dmp> -c dumpasync -c exit`** is the command that found the bug. Thread
  stacks of an async deadlock show *nothing* — every thread is an idle pool thread and `Main` is
  awaiting the platform's run task — because the pending work is async state machines on the heap.
  `dumpasync` prints them with their continuation chain, which here ran from the platform entry
  point through TUnit's scheduler to the test method to the exact server method and the
  `Task+DelayPromise` it was awaiting. Note the `-stacks` flag in older docs is not accepted by
  dotnet-dump 9.0.661903; the bare command prints chains.
- For the build: `dotnet-stack` on the one MSBuild worker node that still ticked, three samples
  ten seconds apart, then `-clp:PerformanceSummary` for per-target/per-task time.
- Test selection: `--treenode-filter "/*/Antiphon.Tests.Application/HerdrAlwaysOnChannelParityTests/*"`
  runs the whole class (5 tests). A parameterised arm is a **child node** of the test:
  `…/AlwaysOn_channel_bound_survives_child_death_and_replies/*PtyHost*` selects it; the display-name
  spellings `…replies(PtyHost)`, `…replies\(PtyHost\)` and `…/*PtyHost*` at the test level all
  select zero tests (exit code 8). `--list-tests` ignores the filter, so it cannot be used to check
  one.

## 2. Finding 1 — the test process: a real-time poll against a frozen deadline

### 2.1 Reproduction

| Run | Binary | Filter | Result |
|---|---|---|---|
| `herdr-exe-1` | master `a6e6c01`, exe direct | whole class | **hung**: banner only, 17.4 s CPU at 300 s, +0.14 s over 10 s; dump taken |
| `ptyhost-arm-w1` | same | PtyHost arm (child-node filter) | **hung**: 19.6 s CPU at 300 s, +0.19 s/10 s |
| `ptyhost-arm-dump` | same | PtyHost arm | **hung**: 17.5 s CPU at 241 s, +0.16 s/10 s; dump taken |
| `fixed-class-1` (mis-built, see 2.4) | same | whole class | **hung** (fourth) |
| `fixed-class-2`, `-3` | clock patched, full graph rebuilt on `23de792` | whole class | 5/5 passed, 24 s and 23 s |
| `fixed-via-dotnet-run` | patched, via `dotnet run --no-build` | whole class | 5/5 passed, 30 s wall |
| `fixed-ptyhost-arm` | patched | PtyHost arm | passed, 21 s |

Four out of four unpatched runs hung, on both arms, on a machine that was otherwise quiet (the only
other dotnet processes were the dev server, the relay, and an unrelated GymStat app; no builds).
Every hung run had its `postgres:16-alpine` **and** `testcontainers/ryuk` containers up for the whole
window, so the Testcontainers start had completed; the `[Before(Assembly)]` hooks are not involved.
The banner-only output is normal, not a symptom: TUnit prints nothing per test until it finishes,
and this class's first test never did.

### 2.2 Where it is stuck

`dumpasync` on `herdr-exe-1.dmp` (Herdr arm) and `ptyhost-arm-dump.dmp` (PtyHost arm) show the
same chain, identical in every frame but the addresses:

```
System.Threading.Tasks.Task+DelayPromise
  (0)  SessionMessageQueueService+<SettlePostEvidenceAsync>d__64
  (11) SessionMessageQueueService+<DeliverAsync>d__53
  (12) SessionMessageQueueService+<DeliverNextLockedAsync>d__35
  (20) SessionMessageQueueService+<EnqueueAsync>d__19
  (1)  ChannelBridgeService+<FlushLaneAsync>d__14
  (0)  ChannelInboundDebouncer+<AddAsync>d__10
  (5)  ChannelBridgeService+<HandleInboundAsync>d__13
  (22) HerdrAlwaysOnChannelParityTests+<AlwaysOn_channel_bound_survives_child_death_and_replies>d__6
  …    TUnit.Engine.TestExecutor / TestScheduler.ExecuteAllPhasesAsync / … / MicrosoftTestingPlatformEntryPoint.<Main>
```

State 22 of the test is the channel step (`harness.Bridge.HandleInboundAsync(inbound, …)`, line
193/204 on `23de792`): the supervisor resume has already succeeded; the test is delivering the
channel prompt into the resumed session. Every other pending state machine in the dump is
housekeeping (`RunnerTerminalSession.WaitForExitAsync` on a plain `Task.Delay(250)`, the fake herdr
accept loop, the platform's message consumers, ryuk's keep-alive). No thread holds a lock, no socket
read is outstanding, nothing is waiting on Docker or Postgres.

### 2.3 Why it never comes back

`server/Application/Services/SessionMessageQueueService.cs:2084` (`SettlePostEvidenceAsync`):

```csharp
var settleFor = TimeSpan.FromMilliseconds(Math.Clamp(_verification.PostEvidenceSettleMs, 0, 3_000));
var deadline = UtcNow() + TimeSpan.FromSeconds(3);
var lastChange = UtcNow();
while (true)
{
    …
    if (!_runtime.TryGetLiveMetadata(sessionId, out var metadata))
        return new SubmitBaseline(null, screen);          // exit A: no live metadata yet
    if (lastSequence != metadata.LastSequence) { lastSequence = …; lastChange = UtcNow(); }
    var now = UtcNow();
    if (now - lastChange >= settleFor || now >= deadline) // exit B: only ever true if the clock moves
        return new SubmitBaseline(lastSequence, screen);
    await Task.Delay(remaining < pollFor ? remaining : pollFor, _timeProvider, ct);
}
```

with `UtcNow() => _timeProvider.GetUtcNow()` (line 2982). The harness (`BuildHarness`, line 479/493)
does `services.AddSingleton<TimeProvider>(new MutableTimeProvider(DateTimeOffset.UtcNow))` where

```csharp
private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
```

`GetUtcNow()` is frozen; `CreateTimer` is inherited from the base class, so `Task.Delay(…,
_timeProvider, …)` fires in real time. Once the loop passes exit A it can only leave through exit B,
and `now`, `lastChange` and `deadline` are all the same frozen instant (the harness sets
`PostEvidenceSettleMs` to the default 500, so `0 >= 500` is false; with `PollIntervalMs = 50` it
re-polls twenty times a second, which is the ~1.5 % CPU that was observed). The test only ever
calls `Clock.Advance` once, at line 152, to get past the supervisor's 5 s backoff — long before the
channel step — and nothing else in the process moves the clock.

The same shape exists at lines 1706 (`WaitForTranscriptConfirmAsync`), 1915 and 1957 (grace
windows), 2043 (`WaitForComposerEvidenceAsync`) and 2063 (`WaitForSequenceAdvanceAsync`); which one
a given run wedges in depends on which exit-A branch it passes. The two dumps both landed in 2087.

### 2.4 Why it is intermittent, and independent of load

Exit A is the whole story. `_runtime.TryGetLiveMetadata` answers true only once the runtime holds
a `LastSequence` for the resumed session — on the PtyHost arm that is the fake adapter's first
output event reaching `AgentSessionRuntime`; on the herdr arm it is the S3/CARD-0164 refresh via
`pane.get`. Whether that has happened by the time the channel delivery reaches the settle is a race
between two in-process pipelines with no ordering between them. Lose the race (no metadata yet):
exit A, the delivery proceeds, the test passes in ~25 s — which is what CARD-0040's delegate saw.
Win it: exit B is unreachable and the process waits forever. CPU load only changes the odds; the
card's first hang (99 % load) and second (14–25 %) are the same bug either way, which is exactly
what its "18 s of CPU in 36 minutes / 54 s in 51 minutes" numbers already said — those are the
poll loop, not starvation.

A note on tonight's `fixed-class-1` run, which hung *after* the patch: my first
`Directory.Build.targets` had `--` inside an XML comment, MSBuild refused the whole project, and the
watchdog ran the previous binary. The run that followed the corrected build then failed one test
(`resumedPane` was `w1:p3`, expected `w1:p2`) — because CARD-0224 S1 (`23de792`, 21:05:49) landed
on master *while my 21-minute build was running*: `bin-c222`'s `Antiphon.SessionRunner.dll` (20:59)
predated it and had no `ResolveTargetPaneAsync`, while the test DLL (rebuilt with
`--no-dependencies`) asserted CARD-0224's pane reuse. A full-graph rebuild made it 5/5. Both are
recorded so nobody reads those two runs as counter-evidence.

### 2.5 The fix (flagged, not committed)

Replace the provider with an **offset over the real clock**, keeping `Advance` with its meaning:

```csharp
/// CARD-0222: an OFFSET over the real clock, not a frozen instant — this is the only
/// TimeProvider in the harness, so it also feeds every `deadline = UtcNow() + N` poll loop in
/// SessionMessageQueueService, whose Task.Delay(…, _timeProvider) runs in real time. A frozen
/// GetUtcNow never reaches the deadline (dumpasync leaf: SettlePostEvidenceAsync → DelayPromise).
private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private TimeSpan _offset = start - DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
    public void Advance(TimeSpan by) => _offset += by;
}
```

Verified as §2.1's last four rows. The full diff is also at the scratchpad's
`card-0222-clock.patch` for this session. **Not committed**: the brief asks that this file be left
to CARD-0224's merger. CARD-0224 S1 is on master now (`23de792`), no worktree is ahead of master on
the file, so the next person to touch it can drop the three lines in; the class docstring's
"same lesson as `AgentSupervisionTests`" should gain the clock rule too.

Alternatives considered and rejected: `FakeTimeProvider` from
`Microsoft.Extensions.TimeProvider.Testing` is *worse* here — it freezes timers as well as the
clock, so the first `Task.Delay` never fires and the loop never even polls; and changing the six
production loops to bound themselves on `Stopwatch` instead of `_timeProvider` would be teaching
production code to distrust its own clock to suit one test double.

### 2.6 The other harnesses with a frozen clock

| Harness | Clock | Registers the queue service | Ran tonight |
|---|---|---|---|
| `AgentSupervisionTests` | byte-identical frozen `MutableTimeProvider` (line 588), advanced at 62/95/150 | yes (line 465) | 7/7 in 18 s, one run |
| `AgentTaskCheckInterpreterTests`, `AgentTaskCheckScheduleTests`, `AgentTaskDeadSessionReconciliationTests`, `AgentTaskReplyIntegrationTests`, `OrchestratorTrackerCadenceTests`, `SessionHealthTests` | `FakeTimeProvider(DateTimeOffset.UtcNow)` (timers frozen too) | yes | not run |

None of these was caught hanging tonight; all of them can, by construction, if a test drives a
verified delivery through the queue with live metadata. The same three-line offset provider (or
`FakeTimeProvider` with `AutoAdvanceAmount` where the timers matter) is the fix in each; the plan
sequences them behind the herdr one.

## 3. Finding 2 — the build: `IncrementalClean` over a 228 MB ledger

### 3.1 Reproduction

The very first command of this task — `dotnet build tests/Antiphon.Tests
--property:OutputPath=bin-c222/` on a quiet machine — did not return inside the 10-minute
foreground window. At 10m34s: the `dotnet build` process had 19.4 s CPU and 0.00 s over the next
10 s; its seven MSBuild worker nodes had ~11 s each and 0.00–0.12 s over 10 s; one node (`23128`,
236 MB working set) ticked at 0.9 s per 10 s. This is the shape tonight's data point 1 described
("hung ~55 minutes, 1.47 s CPU, nothing spawned") — and the CPU figure there was the *outer*
`dotnet` process, which does nothing but wait on its nodes.

### 3.2 Where it was

`dotnet-stack` on node 23128, three samples ten seconds apart, the only non-idle thread each time:

```
System.Environment.get_CurrentDirectoryCore()
Microsoft.Build.Framework.TaskEnvironment.GetAbsolutePath(string)
Microsoft.Build.Tasks.FindUnderPath.Execute()
Microsoft.Build.BackEnd.TaskBuilder.ExecuteInstantiatedTask …
```

`FindUnderPath` appears in `Microsoft.Common.CurrentVersion.targets` only inside `IncrementalClean`
(lines 5711/5716: `Files="@(_CleanOrphanFileWrites)"`) and `_CleanRecordFileWrites`. Its input is
`_CleanPriorFileWrites`, read by `_CleanGetCurrentAndPriorFileWrites` from
`$(IntermediateOutputPath)$(CleanFile)` = `obj/Debug/net9.0/<Project>.csproj.FileListAbsolute.txt`.

### 3.3 The ledgers

| Project | Ledger size | Lines | Written |
|---|---|---|---|
| `src/Antiphon.SessionRunner` | **228,447,307 B** | **770,706** | 21:16 (this build, 18 min in) |
| `tests/Antiphon.Tests` | **97,172,970 B** | **520,713** | 21:19 (this build, 21.5 min in) |
| `server` | 16,050,383 B | — | 21:02 |
| `tests/Antiphon.SessionRunner.Tests` | 2,826,336 B | — | — |
| healthy (Tests, after reset) | 56,237 B | 653 | — |

All 770,706 SessionRunner lines are distinct. The paths are **nested alternate-output trees**:

```
C:\src\Antiphon\src\Antiphon.SessionRunner\bin-verify0108\bin-c0106verify\bin-card84-s4-verify\bin-card84-s2-verify\Antiphon.PtyHost.deps.json
  49152 lines under  bin-check0112\bin-c112\bin-c107
  16871 lines under  bin-profile \workspace\session-runner-logs\pty-hosts      (note the trailing space)
  16871 lines under  bin-defendertest\bin-profile2 2\workspace
```

That is the CARD-0110 story recorded in `Directory.Build.props` — before the `bin-*/**` default
exclude (2026-08-21) a `bin-X` build swallowed every sibling `bin-Y` tree as content, recursively —
except that the *ledger* kept every path those builds ever wrote, and nothing since has removed
them: `IncrementalClean` deletes orphaned files under the current `OutDir`, and prunes only those
entries; an entry under any *other* `bin-<name>` is outside `OutDir` and survives forever. The
exclude stopped the swallowing but not the growth: each new `bin-<name>` build still appends ~650
entries the next build must read, filter and rewrite.

### 3.4 Measured cost, A/B, and the guard

`dotnet build tests/Antiphon.Tests --no-restore --no-dependencies --property:OutputPath=bin-c222/
-clp:PerformanceSummary`, same binary state, twice:

| | Ledger in place (97 MB) | Ledger moved aside |
|---|---|---|
| `_CleanGetCurrentAndPriorFileWrites` (`ReadLinesFromFile`) | **140,882 ms** | 21 ms |
| `IncrementalClean` (`FindUnderPath` ×5) | **15,940 ms** | 9 ms |
| Whole build | **181 s** | **13.4 s** |

Full graph, same command as the 21m31s run, after resetting the three bloated ledgers: **1m28s**.
`ReadLinesFromFile` is the surprising line — 141 s to read 97 MB — and I did not chase why (the
per-line `TaskItem` construction is the likely suspect); the fix does not depend on it.

Shipped on master as `Directory.Build.targets`: a target `BeforeTargets="_CleanGetCurrentAndPriorFileWrites"`
that measures the ledger with `[System.IO.File]::ReadAllText(...).Length` (the one size probe the
MSBuild property-function allowlist permits; a 50 KB read on a healthy ledger) and, over
`$(AntiphonCleanFileMaxBytes)` (2,000,000; `0` disables), logs a warning naming the card and deletes
the file. Verified on `Antiphon.SessionRunner.Contracts` with a synthetic 3 MB ledger: build 1 warns
and the ledger comes back at 1,414 B; build 2 is silent. The only thing lost by a reset is
`IncrementalClean`'s ability to delete outputs orphaned by *earlier* builds in that `OutDir` — the
state of every fresh clone. The three bloated ledgers in `C:\src\Antiphon` were moved to this
session's scratchpad (`*.FileListAbsolute.txt.before`) rather than deleted, in case anyone wants the
full lists; the 14 worktrees under `C:\Antiphon\worktrees\` each have their own `obj/` and were not
touched — the guard will reset theirs on their next build and say so in the build output.

## 4. Reconciling the reports

- **Card, hang 1 and 2** (36 min / 51 min, both arms): finding 1. The CPU-time figures were the
  poll loop. The "bounded 90-second isolation attempt on the PtyHost arm did not reach the test
  phase within build + timeout": finding 2 — the build alone was taking longer than that.
- **Data point 1** (broad `Application/*` filter, *with* build, ~55 min, 1.47 s CPU, "no
  `testhost.exe`"): finding 2. With `EnableMicrosoftTestingPlatformRunner` there is never a
  `testhost.exe`; the child is `Antiphon.Tests.exe`, and it had not been spawned yet because the
  build had not finished. The "retry of the same command succeeded in under a minute" I could not
  reproduce and cannot explain from the ledger alone — a retry pays the same `IncrementalClean` —
  so that detail stays open; one candidate is that the first attempt was killed while a reused
  MSBuild node still held the build result for the slow projects, but I did not test it.
- **Data point 2** (`--no-build`, three consecutive hangs, 1.59 s → 1.625 s CPU): finding 1. Those
  CPU numbers are the `dotnet run` parent, which only waits on the child; the child was in the
  poll loop. `dotnet build-server shutdown` cannot touch a test process.
- **Data point 3** (`Antiphon.SessionRunner.Tests` never hangs): it has no `SessionMessageQueueService`,
  no `TestDbFixture` and no frozen clock — it never runs the delivery path at all. "Large assembly
  / Testcontainers / shared `[NotInParallel]` resource" were all ruled out directly: the container
  was up in every hung run, and no thread was blocked on anything.
- **CARD-0165**: not finding 1 (class created 08-25, stalls on 08-24) and not finding 2 (build
  time, not run time). Same defect class is plausible via `AgentSupervisionTests` or one of the
  `FakeTimeProvider` harnesses (§2.6): TUnit schedules the ungrouped `[NotInParallel]` tests as
  their own phase after the parallel phase (`TestScheduler.ExecuteAllPhasesAsync` in the dump
  chain), so a hang there presents exactly as CARD-0165 described — every parallel test's line
  already printed, a skipped canary as the last visible output, then silence with the process
  alive. **Recipe to settle it**: run the full suite (or the `Application` namespace) under the
  watchdog with `-Dump`, and when it stalls read `dumpasync`; if the leaf is a `DelayPromise`
  under a `UtcNow()`-bounded loop in a harness with a frozen clock, it is this class and the
  provider swap is the fix; if it is anything else, the chain names it. Until that run happens,
  keep CARD-0165 open and un-merged with this card.

## 5. What is on master from this task, and what is not

- **On master**: `Directory.Build.targets` (the ledger guard, with the measurements in its
  comment); this document; the plan; two AGENTS.md gotchas. The three bloated ledgers in the main
  checkout are reset (an artefact under `obj/`, not a tracked file).
- **Not on master, by instruction**: the `MutableTimeProvider` change in
  `HerdrAlwaysOnChannelParityTests.cs` (§2.5) and its twin in `AgentSupervisionTests.cs`. Until the
  first lands, this class hangs on roughly every run that wins the metadata race; run it with a
  `--timeout` or under the watchdog, never bare in a foreground window.
