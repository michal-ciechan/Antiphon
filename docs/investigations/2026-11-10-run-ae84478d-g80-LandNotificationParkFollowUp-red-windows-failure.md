# Run ae84478d g80: AgentTaskLandNotificationParkFollowUpTests red on Windows

Read-only extract. Run `ae84478d` (`rc/2026-11-17` = `3db1a43b02246706dc28075fce7e7b5e84804ddb`, desktop,
still running). Chunk `antiphon-all-group-80`. Sources read, none changed:
`C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\logs\2026-11-11-1907-ae84478d\`
`antiphon-all-group-80.trx`, `antiphon-all-group-80-tests.log`, `antiphon-all-group-80.timing.json`,
`antiphon-all-group-80-execution\log_261111231136483.diag`.

## Verdict

Order/timing flake in the test harness, not a product regression and not the environment.
Not related to the CARD-0747 receipt replay bug or `AgentTaskLandNotificationService`. All six
test bodies of the class passed. The failure is the class's `[After(Class)]` teardown: TUnit
1.44 started the assembly's `[After(Assembly)]` (which disposes `TestDbFixture`) while this
class's `[After(Class)]` was still running, so the class's clone drop hit a disposed fixture.

## The failure

| Field | Value |
|---|---|
| Chunk | `antiphon-all-group-80`: 409 total, 408 passed, 1 failed, 3 m 26.98 s |
| Reported test | `C1773_CanceledNoteDoesNotLateConfirm` (carries the class's AfterClass failure; body passed) |
| Test body | 23:15:01.185 to 23:15:01.268 UTC, 82 ms |
| Error logged | 23:15:01.549 UTC (diag, `TUnitFrameworkLogger ERROR Error executing After hooks`) |
| Kind | `TUnit.Core.Exceptions.AfterClassException` wrapping `System.ObjectDisposedException` |

Message, verbatim:

```
AfterClass hook failed: Cannot access a disposed object.
Object name: 'TestDbFixture'.
 ---> System.ObjectDisposedException: Cannot access a disposed object.
Object name: 'TestDbFixture'.
   at Antiphon.Tests.TestHelpers.TestDbFixtureLifecycle.DropClonedDatabaseAsync(String databaseName) in ...\TestDbFixtureLifecycle.cs:line 474
   at Antiphon.Tests.TestHelpers.IsolatedTestSchema.DisposeAsync() in ...\TestDbFixture.cs:line 145
   at Antiphon.Tests.Application.AgentTaskLandNotificationParkFollowUpTests.Stop() in ...\AgentTaskLandNotificationParkFollowUpTests.cs:line 70
   at TUnit.Engine.Discovery.ReflectionHookDiscoveryService....CreateHookDelegate...
   at TUnit.Engine.Helpers.HookTimeoutHelper.CreateTimeoutHookActionAsync[T](...)
   at TUnit.Engine.Services.HookExecutor.ExecuteHookWithActivityAsync[TContext](...)
   at TUnit.Engine.Services.HookExecutor.ExecuteAfterClassHooksAsync(Type testClass, CancellationToken cancellationToken)
```

There is no expected/actual pair: no assertion failed. Line 474 is the `_disposeStarted` check in
`DropClonedDatabaseAsync`, set only by `TestDbFixtureLifecycle.DisposeAsync`, which only the
`[After(Assembly)]` hook in `TestDbFixtureAssemblyHook` calls. Stdout of the reported test holds only
its `ANTIPHON_TEST_PHASE_ID` line; no stderr near it.

## Passing siblings (same class)

All six bodies passed; they bound the cause to teardown:

| Test | Start to end (UTC) | Duration |
|---|---|---|
| `C1773_OneTranscriptPullPerSessionPerPass` | 23:11:56.060 to 23:11:56.923 | 0.863 s |
| `C1773_MissingQueueParksAfterThreeMisses` | 23:13:53.864 to 23:13:54.001 | 0.137 s |
| `C1773_PromptStampIndexIncludesKind` | 23:14:32.524 to 23:14:33.077 | 0.553 s |
| `C1773_ExternalConfirmDropsTheParkEntry` | 23:15:00.621 to 23:15:00.778 | 0.157 s |
| `C1773_BackoffDoublesAndCapsAtFiveMinutes` | 23:15:00.778 to 23:15:01.184 | 0.406 s |
| `C1773_CanceledNoteDoesNotLateConfirm` (body) | 23:15:01.185 to 23:15:01.268 | 0.083 s |

## What ran at the same time

The class's tests are `[NotInParallel("card-1773-park-follow-up")]` and were spread over the
whole chunk; its last test was the second-to-last test of the chunk to finish. The last ones:

| Test (class) | Start to end (UTC) |
|---|---|
| `C467_V18_StatusAndAcceptance(running)` (`DelegateScriptLandStatusTests`) | 23:14:58.824 to 23:15:00.620 |
| `C1773_CanceledNoteDoesNotLateConfirm` (this class) | 23:15:01.185 to 23:15:01.268 |
| `C458_DownThenUpPreservesLegacyWorkspace` (`WorkerWorkspaceDefaultMigrationTests`) | 23:14:28.723 to **23:15:01.507** (32.78 s, slowest of the chunk) |

Sequence: 23:15:01.268 this class's last test ends and its `[After(Class)]` `Stop()` starts
`BridgeQueueHarness.DisposeAsync()` (stops the action worker, deletes rows from about 18 tables,
disposes the provider). 23:15:01.507 the assembly's last test (`C458_...`, another class) ends;
its thread runs `[After(Assembly)]` and `TestDbFixtureLifecycle.DisposeAsync()` sets
`_disposeStarted`. 23:15:01.549 `Stop()` reaches `_schema.DisposeAsync()` and throws.
The harness dispose had taken about 280 ms, longer than the 240 ms gap.

## Why TUnit allows it

TUnit 1.44.0 (`TUnit.Engine/Services/TestLifecycleCoordinator.cs`, `DecrementAndCheckAfterHooks`)
decrements the class counter and the assembly counter together when a test finishes, before any
After(Class) hook runs. `TestExecutor.ExecuteAfterClassAssemblyHooks` then runs After(Class) and
After(Assembly) only for the flags that test owns. A test that makes the class count 0 runs that
class's teardown; a later test of another class that makes the assembly count 0 runs After(Assembly)
without waiting for the first class's teardown. Any class whose `[After(Class)]` disposes an
`IsolatedTestSchema` is exposed when its teardown outlasts the gap to the assembly's last test.

## Deterministic or timing

Timing. Evidence: no assertion failed; all six bodies passed; the exception is the fixture's
disposed gate (line 474), whose only setter is the assembly teardown; the error (01.549) follows
the assembly's last test end (01.507) and follows this class's last test end (01.268) by 281 ms.
It depends on which class's test finishes last in the chunk and how long the harness dispose
takes under load.

## Earlier runs and changes since `ac6d00022`

- Run `6591e378` (`rc/2026-11-15` = `ac6d00022`): `antiphon-all-group-80` is listed in Appendix B,
  "Chunks that can be seeded as passed" (`origin/feat/card-task-d6579463` `7a3c45c3f`,
  `2026-11-11-run-6591e378-final-result.md`, line 2352).
- `ac6d00022..3db1a43b0`: no change to `AgentTaskLandNotificationParkFollowUpTests.cs`,
  `BridgeQueueHarness.cs` or `AgentTaskLandNotificationService.cs`. The fixture changed only by
  CARD-1829 (`1b16bcfe7`, `4c8473448`): the `[Before(Assembly)]`/`[After(Assembly)]` attributes moved
  from `TestDbFixture` to the new `TestDbFixtureAssemblyHook`, with the same behaviour. The race is
  latent since the class landed (CARD-1773) and in TUnit itself.
- Debug `3593eb2f` (`origin/feat/card-task-3593eb2f`, CARD-0747) changes `AgentTaskLandNotificationService`
  and adds `LandNoteExpirySweepService` and `C0747LandReceiptReplayTests`. It does not touch this
  class or the fixture, and is not in this rc. Unrelated to this failure.

## Fix (branch `feat/card-task-ca4e9ffb`)

- `tests/Antiphon.Tests/TestHelpers/ClassTeardownTracker.cs`: counts classes between TUnit's
  global `[BeforeEvery(Class)]` and `[AfterEvery(Class)]` hooks. AfterEvery(Class) runs after the
  class's own After(Class) hooks, also when they fail (`HookExecutor.ExecuteAfterClassHooksAsync`).
- `TestDbFixtureAssemblyHook.DisposeAsync` waits for the tracker to be idle, bounded at 60 s, then
  disposes the fixture. A wait of 100 ms or more prints `ANTIPHON_FIXTURE_TEARDOWN waited <ms>`;
  an expired bound prints the still-open class names and disposes anyway.
- `TestDbFixtureLifecycle.DropClonedDatabaseAsync` names the clone in the disposed exception, so a
  future occurrence shows which drop lost the race. `ObjectName` stays `TestDbFixture`; the V-6
  `clone-dispose-after` contract is unchanged.
- Witnesses: `ClassTeardownTrackerTests` (Unit). Red with the stub commit, green with the fix.
  Not built or run here: the brief is read-only on the desktop while the qualification runs.

Not covered: `Antiphon.Resilience.Tests` has its own `[After(Assembly)]` calling
`TestDbFixture.DisposeAsync()` and has the same exposure. It links `TestDbFixture.cs` but not the
tracker. Follow-up, not in this change.

## Why it was missed before the rc run

- The race needs a chunk in which a class with an `[After(Class)]` clone drop finishes second-to-last,
  and the assembly's last test ends while that teardown is still running. Single-class filtered runs
  (the CARD-1773 Code/Review verification) always run the class's own teardown before the assembly
  teardown. They cannot show it.
- Linux checkpoint runs and earlier rc chunks happened to order differently. Run `6591e378` passed this
  chunk.
- Nothing asserted the ordering contract between After(Class) and After(Assembly). The CARD-0476
  teardown matrix tests the fixture's own state machine, not TUnit's hook order.
- The exception named only `TestDbFixture`, so the AfterClass failure read like a test fault.

## Native Windows proof owed

Build `Antiphon.Tests` on Windows through the build slot and run
`--treenode-filter "/*/*/ClassTeardownTrackerTests/*"` at `4d3057c31` (red: 2 of 3 fail) and at the fix
commit (green: 3 of 3). Then rerun `antiphon-all-group-80` on the next rc and check its log for
`ANTIPHON_FIXTURE_TEARDOWN` lines.
