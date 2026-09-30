# CARD-0801: skip the Windows-only FakeHerdrServer listener across Linux integration tests

Date: 2026-09-30. Stage: Plan, with TestDesign folded in (the brief asks for the closed
`### Checkpoints` table). Next: Code.
Baseline: `a8b4e9e5a0715a644a21c4270dac8b4d60810d4a` (`origin/master` at planning time).
Card `389837dc-6af0-4f78-9a07-931a48dfe732` on board `8988ca03-7414-47ad-b0b6-51556c701703`,
read with `scripts/card.ps1 get CARD-0801`. Owner docs read: `docs/testing-and-build.md`
(CARD-0459 worktree residue, Checkpoint manifest, Checkpoint runner tool, Build slots, Combined
class filters), `docs/docker-stack.md` (Linux roster), `docs/orchestration-loop.md` (stage
contract), and the measurement this card came from, which lives only on the task branch
`origin/feat/card-task-a7ce7cf4` as `docs/investigations/2026-09-28-full-suite-phase-timing.md`
(commit `a2c8520a`).

## Outcome and scope

Make an unfiltered Linux run of `tests/Antiphon.Tests` complete and write a TRX, with every test
that starts `FakeHerdrServer` reported as skipped with a reason that names this card, and with
Windows execution of those tests unchanged. Two mechanisms, each with one job:

1. **The shared listener boundary never hangs.** When `FakeHerdrServer` cannot create its named
   pipe, `WaitUntilListeningAsync` faults with that exception instead of staying pending, the
   accept loop ends, the exception is observed, and `DisposeAsync` completes. Off Windows the
   default listener refuses with a `PlatformNotSupportedException` whose message names CARD-0801
   and the attribute below, mirroring production `HerdrClient.ConnectPipeAsync`, which refuses
   non-Windows with `HerdrBackendUnavailableException`.
2. **Every affected class declares the limitation.** A `[RequiresFakeHerdr]` attribute (a TUnit
   `SkipAttribute` subclass) on the eight `Antiphon.Tests` classes that start the fake makes TUnit
   report all of their results `NotExecuted` off Windows, at registration, before any body or
   fixture runs. On Windows `ShouldSkip` is false and nothing changes.

Footprint: `tests/Antiphon.SessionRunner.Tests/FakeHerdrServer.cs`,
`tests/Antiphon.SessionRunner.Tests/RequiresFakeHerdrAttribute.cs` (new),
`tests/Antiphon.SessionRunner.Tests/FakeHerdrServerListenerTests.cs` (new),
`tests/Antiphon.Tests/Antiphon.Tests.csproj` (one `<Compile Include>` link), the ten test files
listed under S2, `docs/testing-and-build.md`, `docs/docker-stack.md`. No production code, no
change to `HerdrClient`, `HerdrPaneDisposalFixture`, `HerdrLabelFollowHttpFixture`,
`DirectSessionRunnerClient`, `tests/linux-test-roster.json`, the nightly, the checkpoint tool,
or any bundle.

## Ground truth

Measured on the `server2-temp` Linux runner (SDK 10.0.401, TUnit 1.44.0) during this Plan with a
throwaway TUnit project run through `scripts/build-slot.ps1` (lease held 6 s), plus source reads
at the baseline.

| Card / brief assumes | What the code and the host do | Consequence |
|---|---|---|
| `NamedPipeServerStream` throws `PlatformNotSupportedException` for the Windows-style pipe path on Linux. | `FakeHerdrServer.PipeName` is `Path.Combine(Environment.GetFolderPath(ApplicationData), "herdr", "sessions", <session>, "herdr.sock")`. On this runner `ApplicationData` resolves to an empty string, so the name is the relative path `herdr/sessions/antiphon-herdr-test-<guid>/herdr.sock`, and .NET throws `PlatformNotSupportedException: The name of a pipe on this platform must be a valid file name or a valid absolute path to a file name.` synchronously in the constructor, for both `4` and `MaxAllowedServerInstances`. On a Linux host where `ApplicationData` resolves, .NET accepts a rooted path as a Unix socket path, so the exception is host-dependent. | The platform gate is `OperatingSystem.IsWindows()`, not "did the constructor throw". D-2. |
| The async listener leaves callers waiting for a listening signal that never comes. | `AcceptLoopAsync` catches only `OperationCanceledException` and `IOException`. The constructor exception escapes the loop and faults `_loop`, which nobody awaits until `DisposeAsync`; `_listening` (a `TaskCompletionSource`) is never completed; `WaitUntilListeningAsync` is `_listening.Task.WaitAsync(ct)` with a default token, so it is pending forever. `AgentAttachHerdrTests.StartFake` blocks a thread on `.GetAwaiter().GetResult()`. The faulted `_loop` is the "repeated unobserved named-pipe exceptions" the card saw once the test host started collecting. | D-1 faults `_listening` and observes the exception. |
| The affected classes are `AgentAttachHerdrTests`, `HerdrAlwaysOnChannelParityTests` and the label-follow HTTP fixture's users. | Eight `Antiphon.Tests` classes in ten files start the fake (table below). Four of them reach it through `HerdrDisposalHttpFixture` → `HerdrPaneDisposalFixture.StartAsync` (`Fake.Start(); await Fake.WaitUntilListeningAsync();`), which the card does not name. `HerdrLabelFollowDbFixture` users (`HerdrLabelFollowTests`, `HerdrLabelFollowConcurrencyTests`) use `FakeSessionRunnerClient`, not the fake, and are unaffected. No other `Antiphon.Tests` file constructs `FakeHerdrServer`. | S2 annotates all eight. |
| `AgentAttachHerdrTests` is `[NotInParallel]` and calls `StartFake()` in every test. | Confirmed: 10 `[Test]` methods, 16 results (`[Arguments]` rows); the a7ce7cf4 probe with a temporary guard measured 16 skips, 0 failures, TRX written. | CP-3 expects 16 of its 91. |
| The full run with a guard reached 5,395 passes and 170 failures at 3,670 s then stalled. | The later measurement with a temporary guard at `FakeHerdrServer.Start()` completed: 12,797 discovered, 12,029 passed, 546 failed, 222 skipped, 3 h 00 m 09 s after slot grant on a busy host. The failures are pre-existing Linux red (CARD-0700, CARD-0713, C589/CARD-0800, Windows-only assumptions); none is this card's. `/tmp` pressure (CARD-0804) killed one attempt with `ENOSPC`. | CP-7's success criterion is completion plus the skip roster, not green. D-8. |
| A platform limitation can be declared per class. | TUnit 1.44: a class-level subclass of `SkipAttribute` overriding `ShouldSkip(TestRegisteredContext)` reports every test in the class `NotExecuted`, including tests declared in another `partial` file and every `[Arguments]` row, with the attribute's reason as the TRX message; a body that throws `SkipTestException` is also `NotExecuted`. `RunOn`/`ExcludeOn` exist as built-ins. Measured with the probe (4 class-level skips, 1 body skip, 1 pass; `total: 6, skipped: 5`). | D-3 uses a custom `SkipAttribute`. |
| The fake is shared through a project reference. | `Antiphon.Tests.csproj` compiles `FakeHerdrServer.cs`, `LocalHttpRunner.cs` and `HerdrPaneDisposalFixture.cs` from `tests/Antiphon.SessionRunner.Tests` as linked sources; there is no `InternalsVisibleTo` and no test-project reference. | The attribute file needs the same link; `internal` visibility is enough in both assemblies. |
| The Linux roster excludes these classes. | `tests/linux-test-roster.json`: `AgentAttachHerdrTests`, `HerdrAlwaysOnChannelParityTests`, `HerdrLabelFollowWireTests`, `HerdrLabelFollowFlowTests`, `HerdrPaneScriptTests` are `exclude`; `HerdrPaneDisposalEndpointTests`, `HerdrPaneDisposalApplicationTests`, `HerdrPaneDisposalHttpWireTests` are `include` (lanes `http-02`, `http-02`, `http-01`) although `FakeHerdrServer` is in their closure. `Assert-LinuxTestRoster` validates names, dispositions and owners only; nothing reads `sourceSha256`. | D-6 leaves the roster alone and documents the three. |
| Checkpoint rows need executed tests. | `PlanTableImporter` derives roster tokens from `(Name*)` groups and `RowRunner` matches them against executed **or** skipped names; `Min` accepts `<n> linux / <n> windows`; `run --rows` selects rows; `--baseline <ref>` reruns each red row's failures per class in a detached worktree of that commit. | Skip-roster rows pass on Linux with `Min` 0; the unfiltered row must not carry `--baseline`. |
| The Windows nightly picks the classes up. | `Invoke-AntiphonNightlyTests` runs the assembly without a `--treenode-filter` unless classes are given. | Windows keeps the broad run; the card's Windows proof is CP-3/4/5/8 on the desktop lane. |
| A new test class needs registry work. | `slow-tests-allowlist.txt` lists only `Slow` classes; `Antiphon.SessionRunner.Tests` has no lane-xor rule. | `FakeHerdrServerListenerTests` is `[Category("Unit")]` and needs no registry entry. |

### Affected `Antiphon.Tests` classes (the Linux skip roster)

Result counts are one per bare `[Test]` and one per `[Arguments]` row; none of these files uses a
data source or matrix attribute. `AgentAttachHerdrTests` is measured (16); the rest are read from
source by the same rule.

| Class | File(s) | Reaches the fake through | Results | Roster |
|---|---|---|---|---|
| `Application.AgentAttachHerdrTests` | `Application/AgentAttachHerdrTests.cs` | `StartFake()` (sync wait) | 16 | exclude |
| `Application.HerdrAlwaysOnChannelParityTests` (partial) | `Application/HerdrAlwaysOnChannelParityTests.cs`, `.StandingRecovery.cs`, `.SupervisionHold.cs` | `fake.Start(); await fake.WaitUntilListeningAsync()` in bodies; the `PtyHost` argument rows (8) and `Generic_herdr_codex_banner_is_not_startup_ready` (1) do not touch it | 24 | exclude |
| `Application.HerdrLabelFollowWireTests` | `Application/HerdrLabelFollowWireTests.cs` | `HerdrLabelFollowHttpFixture.StartAsync` | 4 | exclude |
| `Application.HerdrLabelFollowFlowTests` | `Application/HerdrLabelFollowFlowTests.cs` | `HerdrLabelFollowHttpFixture.StartAsync` | 19 | exclude |
| `Application.HerdrPaneDisposalEndpointTests` | `Application/HerdrPaneDisposalEndpointTests.cs` | `HerdrDisposalHttpFixture.StartAsync` → `HerdrPaneDisposalFixture` | 14 | include (http-02) |
| `Application.HerdrPaneDisposalApplicationTests` | `Application/HerdrPaneDisposalApplicationTests.cs` | same | 14 | include (http-02) |
| `Agents.HerdrPaneDisposalHttpWireTests` (partial) | `Agents/HerdrPaneDisposalHttpWireTests.cs`, `Agents/HerdrPaneDisposalExecutionWireTests.cs` | same | 9 | include (http-01) |
| `Scripts.HerdrPaneScriptTests` | `Scripts/HerdrPaneScriptTests.cs` | same | 10 | exclude |

Total: 110 results across three namespaces, which is why CP-3, CP-4 and CP-5 are three rows (the
documented combined-class filter fixes one namespace).

`tests/Antiphon.SessionRunner.Tests` has 16 more consumer classes (about 170 tests, for example
`HerdrAttachTests` 23, `HerdrLaunchShapeTests` 37, `HerdrAdoptionSweepTests` 21), many of them
mixed: `HerdrNamedTabPlacementTests` constructs the fake in 3 of 23 tests, `HerdrClientTests` in
4 of 16. They are out of this card's acceptance criterion (D-5).

## Decisions

### D-1: a failed listener faults `WaitUntilListeningAsync`, ends the accept loop, and is observed

In `AcceptLoopAsync`, the pipe construction is its own `try`: any exception other than
`OperationCanceledException` is passed to a new `FaultListening(Exception)` that, under
`_listenGate`, records it in a new `public Exception? ListenerFault { get; private set; }`, calls
`_listening.TrySetException(ex)`, reads `_listening.Task.Exception` so the task counts as observed,
and then the loop returns. `WaitUntilListeningAsync` (`_listening.Task.WaitAsync(ct)`) therefore
throws the listener's exception to every awaiter, including the synchronous
`.GetAwaiter().GetResult()` in `AgentAttachHerdrTests.StartFake`. `_loop` completes normally, so
`DisposeAsync` is unchanged and completes. `SignalListening`, `ResetListening`, the connected
path and every catch clause after the constructor stay byte-for-byte the same, so Windows is
unchanged on the happy path. A later construction failure (after `ResetListening`) faults the
replacement `TaskCompletionSource` the same way.

Rejected: throwing from `Start()` synchronously off Windows (a second mechanism for the same
failure; the async fault already covers it, and one path is what the seam test proves); retrying
the constructor in the loop (that is how a run emits an exception per iteration); a timeout inside
`WaitUntilListeningAsync` (hides the cause and turns a slow Windows start into a failure).

### D-2: the default listener carries the platform guard, with a message that names the card

`CreateDefaultListener()` is `if (!OperatingSystem.IsWindows()) throw new
PlatformNotSupportedException(PlatformReason); return new NamedPipeServerStream(...)` with the
existing arguments. `PlatformReason` is a `public const string` on `FakeHerdrServer`:
"CARD-0801: FakeHerdrServer listens on a Windows named pipe and herdr's backend is Windows-only;
tests that start it carry [RequiresFakeHerdr] and are skipped off Windows." A
`public static bool ListenerSupported => OperatingSystem.IsWindows()` sits beside it so the
attribute and the fake agree by construction.

Rejected: relying on the raw .NET exception (it depends on `ApplicationData` being empty; on a
Linux host with a home directory .NET accepts the rooted path, binds a Unix socket, and the test
then fails later inside `HerdrClient` with `HerdrBackendUnavailableException`); throwing TUnit's
`SkipTestException` at the boundary, which is what the measurement's temporary guard did (an
implicit skip: a fake that decides test outcomes, a roster nobody can read from the classes, and
a future consumer that silently stops running on Linux; the card asks for the limitation to be
explicit and for coverage not to be deleted silently).

### D-3: `[RequiresFakeHerdr]` is a custom `SkipAttribute` next to the fake

`tests/Antiphon.SessionRunner.Tests/RequiresFakeHerdrAttribute.cs`, namespace
`Antiphon.SessionRunner.Tests`, `internal sealed class RequiresFakeHerdrAttribute : SkipAttribute`
with `: base(FakeHerdrServer.PlatformReason)`, `public static bool ShouldSkipHere =>
!FakeHerdrServer.ListenerSupported`, and `ShouldSkip(TestRegisteredContext) =>
Task.FromResult(ShouldSkipHere)`. `Antiphon.Tests.csproj` links it beside the existing
`FakeHerdrServer.cs` link (`Link="TestHelpers\RequiresFakeHerdrAttribute.cs"`). Discovery-time
skip means no constructor, hook, fixture, Postgres schema or process is touched for a skipped
test, and the TRX carries the reason.

Rejected: `[RunOn(OS.Windows)]` (the same skip with a generic reason; it does not say which
Windows-only thing the class needs, is not greppable to the fake, and would blur with unrelated
Windows-only classes later); `Skip.Test`/`Skip.When` inside bodies (per method, 60+ sites, and
fixture-based classes would still start the fixture up to the skip); putting the attribute in
`tests/Shared` (cannot reference `FakeHerdrServer.PlatformReason` without duplicating it).

### D-4: class-level on all eight classes, including the parity class

`HerdrAlwaysOnChannelParityTests` has 8 `PtyHost` argument rows and one banner unit test that do
not need the fake; class-level skips them on Linux too. Accepted because the frozen Linux roster
already excludes the whole class from every admitted Linux lane, the class is a global
`[NotInParallel]` process-spawning parity suite whose Linux viability for those rows has never
been measured, and per-method gating would spread platform logic across three partial files
(five method attributes plus `Skip.When` in three bodies). Windows executes all 24 rows as
today. The loss is stated here and in the docs, not silent.

Rejected: method-level attributes plus `Skip.When(backend == Herdr && !IsWindows)` in the three
parameterised methods (keeps 9 Linux results of unknown value for 8 edit sites).

### D-5: `Antiphon.SessionRunner.Tests` consumers are not annotated in this card

D-1 and D-2 already turn their Linux hang into a prompt `PlatformNotSupportedException` with a
message that says what to do. Annotating them needs method-level analysis of mixed classes (see
the counts above) and a Windows proof for about 170 tests; the card's acceptance criterion is the
unfiltered `Antiphon.Tests` run. The report recommends a follow-up card: "Annotate
`Antiphon.SessionRunner.Tests` FakeHerdrServer consumers with `[RequiresFakeHerdr]`", with the
class/method counts from this plan.

### D-6: `tests/linux-test-roster.json` is not edited

Three roster-included classes now skip on Linux by attribute. The roster is CARD-0590's frozen
admission record (per-file `sourceSha256`, summary counts, shard assignments) and nothing reads
it to decide a skip; changing dispositions there is that card owner's call. `docs/docker-stack.md`
gets one sentence naming the three classes and this card.

### D-7: a listener seam makes the fault path testable on every platform

`internal Func<NamedPipeServerStream>? CreateListener { get; set; }` on `FakeHerdrServer`; the
accept loop calls it when set and `CreateDefaultListener()` otherwise. Tests inject an
`IOException` to prove D-1 without a platform dependency; the platform test proves D-2 on Linux.

Rejected: a Windows-only pipe-collision test (a second server on the same name with
`maxNumberOfServerInstances: 1`; platform-specific and racy); reflection into `_listening`.

### D-8: the unfiltered Linux run is one TUnit row, alone in its own tool run, red by inheritance

CP-7 is `/*/*/*/*` (the tool needs a filter cell; that path selects every test and is the same
run as no filter), `Serial: true`, its own build, in a `run --rows CP-7` without `--baseline`.
Its success criterion is completion with a TRX, the eight roster classes all `NotExecuted` with
the CARD-0801 reason, zero failures inside those classes, and executed ≥ 12,000. The row will
exit 1 on the pre-existing Linux failures (546 on 2026-09-29 at `7ca550ce`); that exit is the
expected state and is reported with the failure roster, not fixed under this card. This is the
plan's named broad-run exception (Checkpoint manifest rule 5): the invariant is the card's own
acceptance criterion, which only an unfiltered run can prove; the classes that cannot be bounded
are all of them; the cost is about 190 minutes.

Rejected: namespace chunks (each is a filtered run and does not prove the unfiltered assembly
completes); `--baseline` on this row (reruns every red class at the baseline, hours); a command
row through a tolerant wrapper script (hides failures and adds a script the card did not ask for).

### D-9: lanes

| Rows | Lane | Why |
|---|---|---|
| CP-1, CP-2, CP-3, CP-4, CP-5, CP-6, CP-7 | Linux (`server2-temp`, this card's runner) | the hang and the skip roster exist only off Windows; CP-7 is the acceptance run |
| CP-1, CP-3, CP-4, CP-5, CP-8 | Windows (`desktop`) | "the affected tests execute on Windows" and "Windows unchanged" need a Windows TRX; run by a dispatch pinned `-Platform Windows` with `--rows CP-1,CP-3,CP-4,CP-5,CP-8` |

CP-8 must never be selected on Linux (its 16 tests would fail promptly there by design).

### D-10: documentation

`docs/testing-and-build.md`, CARD-0459 section: replace the sentence "`AgentAttachHerdrTests`
does not run on Linux: ... Leave it out of a Linux filter and run it on Windows." with the gate:
classes that start `FakeHerdrServer` carry `[RequiresFakeHerdr]`
(`tests/Antiphon.SessionRunner.Tests/RequiresFakeHerdrAttribute.cs`) and report `NotExecuted`
off Windows with a reason naming CARD-0801; the fake faults `WaitUntilListeningAsync` instead of
hanging when its pipe cannot be created; a new consumer without the attribute fails promptly on
Linux with `PlatformNotSupportedException` and the fix is the attribute, never a catch; the
unfiltered Linux run of 2026-09-29 (3 h, 12,797 cases) is the cost reference.
`docs/docker-stack.md`, roster paragraph: the sentence from D-6.

## Slices

### S1: listener boundary, attribute, unit tests

Files: `tests/Antiphon.SessionRunner.Tests/FakeHerdrServer.cs` (D-1, D-2, D-7: `PlatformReason`,
`ListenerSupported`, `ListenerFault`, `CreateListener`, `CreateDefaultListener`,
`FaultListening`, the construction `try` in `AcceptLoopAsync`, XML docs on `Start` and
`WaitUntilListeningAsync`), `tests/Antiphon.SessionRunner.Tests/RequiresFakeHerdrAttribute.cs`
(new, D-3), `tests/Antiphon.Tests/Antiphon.Tests.csproj` (link),
`tests/Antiphon.SessionRunner.Tests/FakeHerdrServerListenerTests.cs` (new, `[Category("Unit")]`):

- `C801_OffWindows_StartFaultsListeningPromptly`: `Skip.When(OperatingSystem.IsWindows(), ...)`;
  `new FakeHerdrServer()`, `Start()`, `Should.ThrowAsync<PlatformNotSupportedException>(() =>
  fake.WaitUntilListeningAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token))`;
  message contains `CARD-0801` and `RequiresFakeHerdr`; `ListenerFault` is the same exception;
  `DisposeAsync` completes within 5 s.
- `C801_ListenerFailureFaultsWaitUntilListening`: `CreateListener = () => throw new
  IOException(marker)`; same shape, expects the `IOException` with the marker, then
  `DisposeAsync` completes.
- `C801_FaultedListenerIsObservedAndDisposes`: subscribe `TaskScheduler.UnobservedTaskException`
  filtering on the marker; start a fake with the throwing seam, never await
  `WaitUntilListeningAsync`, poll `ListenerFault` up to 5 s, dispose, drop the reference,
  `GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect()`; no event with the marker.
- `C801_AttributeSkipsOffWindowsAndNamesTheListener`: `RequiresFakeHerdrAttribute.ShouldSkipHere
  == !OperatingSystem.IsWindows()`; `new RequiresFakeHerdrAttribute().Reason` contains
  `CARD-0801` and `FakeHerdrServer`.

Commit S1 before running CP-1/CP-2.

### S2: annotate the eight classes and document

Add `[RequiresFakeHerdr]` (with `using Antiphon.SessionRunner.Tests;` where the file lacks it)
above the class attributes of: `Application/AgentAttachHerdrTests.cs`,
`Application/HerdrAlwaysOnChannelParityTests.cs` (the primary partial declaration only),
`Application/HerdrLabelFollowWireTests.cs`, `Application/HerdrLabelFollowFlowTests.cs`,
`Application/HerdrPaneDisposalEndpointTests.cs`, `Application/HerdrPaneDisposalApplicationTests.cs`,
`Agents/HerdrPaneDisposalHttpWireTests.cs` (the primary partial declaration only),
`Scripts/HerdrPaneScriptTests.cs`. Leave `StartFake`, the fixtures and every body unchanged.
Docs per D-10. Commit S2 before running CP-3 to CP-7.

## Verification design

| ID | Evidence |
|---|---|
| V-1 | Off Windows, `Start()` then `WaitUntilListeningAsync` throws `PlatformNotSupportedException` naming CARD-0801 within 5 s; `ListenerFault` set; dispose completes (CP-1, Linux) |
| V-2 | An injected listener failure faults `WaitUntilListeningAsync` with that exception, the loop ends, dispose completes (CP-1, both lanes) |
| V-3 | A faulted, never-awaited listener raises no `UnobservedTaskException` (CP-1, both lanes) |
| V-4 | The attribute skips exactly off Windows and its reason names the card and the fake (CP-1, both lanes) |
| V-5 | On Linux the eight classes report all 110 results `NotExecuted` with the CARD-0801 reason, 0 executed, 0 failed (CP-3, CP-4, CP-5 Linux) |
| V-6 | On Windows the same filters execute all 110 results, 0 failed, 0 skipped (CP-3, CP-4, CP-5 Windows) |
| V-7 | An unfiltered Linux `Antiphon.Tests` run completes and writes a TRX; the eight classes are all `NotExecuted`; no failure inside them; executed ≥ 12,000 (CP-7) |
| R-1 | `TestClassificationGuardTests` passes in `Antiphon.SessionRunner.Tests` with the new Unit class (CP-2) |
| R-2 | The Linux Unit lane has no NEW or INTRODUCED failure against the baseline (CP-6) |
| R-3 | `HerdrClientTests` (16) executes green on Windows against the changed accept loop (CP-8) |

### Positive controls for the later Mutation stage (method-scoped)

| PC | Mutation | Red at |
|---|---|---|
| PC-1 | delete the `FaultListening(ex)` call, keep the `return` | `C801_ListenerFailureFaultsWaitUntilListening` (5 s token cancels: `OperationCanceledException` instead of `IOException`); on Linux also `C801_OffWindows_StartFaultsListeningPromptly` |
| PC-2 | delete the `OperatingSystem.IsWindows()` guard in `CreateDefaultListener` | `C801_OffWindows_StartFaultsListeningPromptly` (message lacks `CARD-0801`; on a host with `ApplicationData` set, wrong exception type) |
| PC-3 | delete the `_listening.Task.Exception` read | `C801_FaultedListenerIsObservedAndDisposes` (the event fires with the marker); the weakest control, it depends on the finalizer pass |
| PC-4 | `ShouldSkip` returns `false` | Linux: `/*/*/AgentAttachHerdrTests/Attach_*` any one method executes and fails with `PlatformNotSupportedException` |
| PC-5 | remove `[RequiresFakeHerdr]` from `HerdrPaneScriptTests` | Linux: `/*/*/HerdrPaneScriptTests/ReasonFile_preserves_multiline_text_and_script_is_ascii` executes and fails |

### Execution

Linux (`server2-temp`), three tool runs, each `dotnet run --project tools/Antiphon.Checkpoints --
run --plan docs/superpowers/plans/2026-09-30-card-0801-linux-fake-herdr-listener-skip-plan.md
...` followed by `wait` until the exit is not 75:

1. After the S1 commit: `--rows CP-1,CP-2`.
2. After the S2 commit: `--rows CP-3,CP-4,CP-5,CP-6 --baseline a8b4e9e5a0715a644a21c4270dac8b4d60810d4a`
   (the baseline classifies CP-6's inherited Unit failures; on 2026-09-29 there were two).
3. `--rows CP-7` with no `--baseline`. Check `df -h /tmp` first (CARD-0804); the run holds one
   slot for about three hours; `wait --max-wait 570s` returns 75 about twenty times. This run
   is red by inheritance (D-8): report the `CHECKPOINT` line, the roster classes' skipped names,
   the failure count and roster from `failures.md`, and delete `bin-c801f/` afterwards (a red run
   keeps it).

Windows (`desktop`), one dispatch pinned `-Platform Windows`: `--rows CP-1,CP-3,CP-4,CP-5,CP-8`.

Every row is one line: `CHECKPOINT CP-n commit=<sha> build=... filter=... executed=N passed=N
failed=N skipped=N trx=<path>`. `--list-tests` is not evidence. Unlisted runs need a reason.

### Cost

Linux rows: 6 + 1 + 6 + 3 + 3 + 15 + 190 = 224 minutes; CP-7 dominates and is the card's own
acceptance run. Windows rows: 6 + 15 + 4 + 5 + 4 = 34 minutes. Authoring S1 + S2: 45-75 minutes.
Code `ExpectAbout` for the Linux dispatch: 280-320 minutes; for the Windows dispatch: 45-60
minutes. Estimates allow for host contention and are not measured timings.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial |
|---|---|---|---|---|---|---|---:|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c801r/` | listener-unit | `/*/*/FakeHerdrServerListenerTests/*` | V-1, V-2, V-3, V-4 | Linux: all 4 executed, 0 failed; Windows: 3 executed, 1 skipped (the off-Windows test), 0 failed | 4 linux / 3 windows | 6 | 6 | false |
| CP-2 | S1 | CP-1 | runner-classification | `/*/*/TestClassificationGuardTests/*` | R-1 | 1 executed, 0 failed | 1 | 1 | 1 | false |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c801/` | herdr-application | `/*/Antiphon.Tests.Application/(AgentAttachHerdrTests*)\|(HerdrAlwaysOnChannelParityTests*)\|(HerdrLabelFollowWireTests*)\|(HerdrLabelFollowFlowTests*)\|(HerdrPaneDisposalEndpointTests*)\|(HerdrPaneDisposalApplicationTests*)/*` | V-5, V-6 | Linux: 0 executed, 91 NotExecuted, each with the CARD-0801 reason, 0 failed; Windows: 91 executed, 0 failed, 0 skipped | 0 linux / 91 windows | 6 | 15 | false |
| CP-4 | S1-S2 | CP-3 | herdr-agents | `/*/*/HerdrPaneDisposalHttpWireTests/*` | V-5, V-6 | Linux: 0 executed, 9 NotExecuted with the CARD-0801 reason; Windows: 9 executed, 0 failed, 0 skipped | 0 linux / 9 windows | 3 | 4 | false |
| CP-5 | S1-S2 | CP-3 | herdr-scripts | `/*/*/HerdrPaneScriptTests/*` | V-5, V-6 | Linux: 0 executed, 10 NotExecuted with the CARD-0801 reason; Windows: 10 executed, 0 failed, 0 skipped | 0 linux / 10 windows | 3 | 5 | false |
| CP-6 | S1-S2 | CP-3 | unit-lane | `/*/*/*/*[Category=Unit]` | R-2 | >= 3000 executed; 0 NEW or INTRODUCED failures under the baseline; INHERITED failures listed by name | 3000 | 15 | 15 | false |
| CP-7 | S1-S2 | `tests/Antiphon.Tests -> bin-c801f/` | linux-unfiltered | `/*/*/*/*` | V-7 | completes with a TRX (exit 5, 6 or 2 is the defect); the eight roster classes all NotExecuted with the CARD-0801 reason; 0 failed inside them; skipped >= 110; failures elsewhere are inherited Linux red (546 on 2026-09-29), reported by class and not fixed here; the row exits 1 by design | 12000 linux / 1 windows | 190 | 190 | true |
| CP-8 | S1-S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c801rw/` | herdr-client-windows | `/*/*/HerdrClientTests/*` | R-3 | Windows only: 16 executed, 0 failed, 0 skipped; never selected on Linux | 0 linux / 16 windows | 4 | 4 | false |

## Follow-ups outside this card

- File "Annotate `Antiphon.SessionRunner.Tests` FakeHerdrServer consumers with
  `[RequiresFakeHerdr]`" (D-5): 16 classes, about 170 tests, several mixed; needs method-level
  placement and a Windows proof. Until then an unfiltered Linux run of that assembly fails those
  tests promptly instead of hanging.
- CARD-0590's owner may flip the three roster-included disposal classes to `exclude` with owner
  CARD-0801 (D-6); the roster's summary counts and shard lists move with them.
- The full-suite timing investigation and its testing-guide edits exist only on
  `origin/feat/card-task-a7ce7cf4`; landing them is the a7ce7cf4 task's business.
