# Why missed: g80 LandNotificationParkFollowUp red

One committed note for rc chunk `antiphon-all-group-80` in run `ae84478d`
(`rc/2026-11-17` = `3db1a43b02246706dc28075fce7e7b5e84804ddb`, Windows). CARD-1781.
The Windows extract is
`docs/investigations/2026-11-10-run-ae84478d-g80-LandNotificationParkFollowUp-red-windows-failure.md`
(read task `ca4e9ffb`, carried here byte-for-byte from `51c254ad6f4e555d6c41401892bc305064834278`).

## Failure

TEST FIXTURE DEFECT: an order/timing race in assembly teardown. Not a product regression and not
the environment. `AgentTaskLandNotificationService`, the hosted service, the test class and
`BridgeQueueHarness` are unchanged in `ac6d00022..3db1a43b0`. Run `6591e378` (`ac6d00022`) passed this chunk.

All six `AgentTaskLandNotificationParkFollowUpTests` bodies passed. Its `[After(Class)]` `Stop()`
threw `ObjectDisposedException: TestDbFixture` from `TestDbFixtureLifecycle.DropClonedDatabaseAsync`
(the `_disposeStarted` gate), reported on `C1773_CanceledNoteDoesNotLateConfirm`. The assembly's
last test (`C458_DownThenUpPreservesLegacyWorkspace`, another class) ended 240 ms after this class's
last test. Its thread ran `[After(Assembly)]`, which disposed the fixture, while `Stop()` was still
disposing the harness (about 280 ms).

Linux proof that TUnit 1.44.0 allows it: a scratch two-class TUnit 1.44.0 project. Class A's
`[After(Class)]` takes 2 s, and class B's 500 ms test ends last. Over three runs,
`[After(Assembly)]` started at 512-529 ms and A's `[After(Class)]` ended at 2103-2173 ms. The same
probe showed that `[AfterEvery(Class)]` runs after the class's own `[After(Class)]`, including one
that throws. A bounded wait in `[After(Assembly)]` on a BeforeEvery/AfterEvery count released at
2080-2113 ms with `open=0`, with no deadlock.

Fix: `ClassTeardownTracker` counts classes between `[BeforeEvery(Class)]` and `[AfterEvery(Class)]`.
`TestDbFixtureAssemblyHook.DisposeAsync` waits for it to go idle, bounded at 60 s, then disposes.
A wait of 100 ms or more prints `ANTIPHON_FIXTURE_TEARDOWN`. The disposed exception now names the clone.
Red: `ClassTeardownTrackerTests` 2 of 3 failed at `07ce32d96` (the stub; the idle case passes on the stub).

## Introducing commit/card

introducing commit/card: e0505cf8c8c32c935182451939c8fd594e3f9610 (CARD-1773) exposed it. It added a
class whose `[After(Class)]` disposes a whole `BridgeQueueHarness` before its clone drop. The race
itself has been latent since 48ba7a8c7d8c501832f74ec388a2025301373932 (CARD-0476): the clone drop
refuses once dispose has started, and nothing made the TUnit assembly teardown wait for an open
class teardown.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 3e56dbb0 (round 1 Final, subject `4f20588c8c7d43cb8852d398098f2aea7ba29892`), scope Full (Final), selection
  CP-1 `(AgentTaskLandNotification*)|(AgentTaskLandHeldNotificationTests*)` (ParkFollowUp 6/6),
  CP-2 `C0824RefOnlySnapshotMigration|C1514HostLoadMigration|ExpectationDebt|C0824RefOnlyIdentity|C0824RefOnlyDebt`,
  CP-3 `DispatchBaseNotificationTests`, CP-4 the classification guard batch. 219/219.
- eb0f53d8 (round 2 Final, subject `aa07b33d55df270d7c5b223c8fdadd1c248b40bd`), scope Full (Final), selection
  `(Card1773TranscriptPromptStampMigrationTests*)|(TranscriptHotPathMigrationTests*)`,
  `(C0824RefOnlySnapshotMigrationTests*)|(C1514HostLoadMigrationTests*)`, and the guard batch. 76/76, clean.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane, `Integration`/`Slow`,
`tests/Antiphon.Tests/Application/AgentTaskLandNotificationParkFollowUpTests.cs`. The class was inside
round 1's selection and passed. The defect is not in a body. It is the hook order between one class's
`[After(Class)]` and a different class's last test, which runs `[After(Assembly)]`. It shows only when
the chunk's last test belongs to another class and ends while this teardown is running. A class-scoped
filter, or one where this class finishes last, cannot show it. The rc packer put the class beside
`WorkerWorkspaceDefaultMigrationTests` (a 32.8 s test) in g80. Nothing asserted the
After(Class)/After(Assembly) ordering contract until `ClassTeardownTrackerTests`.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/TestHelpers/{TestDbFixture*.cs,ClassTeardownTracker*.cs}` -> `(ClassTeardownTrackerTests*)|(TestDbFixtureLifecycleTests*)`

`tests/impacted-classes.json` is not changed here. Its kinds are pinned by `ImpactedClassesSelectorTests`.

## Exemption

same cause as an already written note: none.

## Not covered

`Antiphon.Resilience.Tests/AssemblyHooks.cs` has its own `[After(Assembly)]` calling
`TestDbFixture.DisposeAsync()` and links `TestDbFixture.cs` but not the tracker. It has the same
exposure. Follow-up, not in this change: `ResilienceAssemblyTests` pins that mirror's compile list.

## Native Windows proof owed

On Windows, build `Antiphon.Tests` through the build slot and run
`--treenode-filter "/*/*/ClassTeardownTrackerTests/*"`: 2 of 3 red at `07ce32d96`, 3 of 3 green at
this branch's tip. On the next rc, check that `antiphon-all-group-80` passes, and check its log for
`ANTIPHON_FIXTURE_TEARDOWN` lines.
