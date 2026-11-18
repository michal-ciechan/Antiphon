# Why missed: profile-413 transient test-DB failure while seeding

rc run f2de5b30 (Windows Full, rc/2026-11-26 = 29ba0c5002e6), chunk antiphon-all-profile-413:
`C0824DesktopLegacyReleasePolicyTests.Non_terminal_task_is_never_released(Dispatched)` and `(Working)` failed.
Both failed with `InvalidOperationException: An exception has been raised that is likely due to a transient failure.`
The exception was thrown by `NpgsqlExecutionStrategy.ExecuteAsync`.
`(Queued)` and `(Blocked)` passed in the same chunk. Debug task fda961b9.

## Facts

- `(Dispatched)` ran from 07:53:11.517Z to 07:53:28.947Z (17.43 s). `(Working)` failed the same way in the same chunk.
- The failing frame is the test's seeding write, not the code under test. The call chain is
  `C0824DesktopLegacyReleasePolicyTests.cs:208` > `DesktopLegacyWorld.CreateAsync` > `SettledRemovalHarness.CreateAsync` >
  `LandingSafetyHarness.SeedAsync` (`LandingSafetyHarness.cs:128`) > `AppDbContext.SaveChangesAsync` (`AppDbContext.cs:164`).
  The world has not added the status-bearing task yet at that point. The status cannot matter.
- The TRX keeps the wrapper's message and stack, but not its inner exception.
- Server2 at base f4d7830332: the exact method filter passed 4/4 twice (7.7 s and 6.2 s per case, run in parallel).

## Cause

The cause is environment/timing on the Windows qualification host. The inner exception was not recorded (UNOBSERVED).
It is not a product defect and not a test-logic defect.

`SeedAsync` is the first EF operation on that case's fresh cloned database: `TestDbClonePool` hands out
never-opened clones, or the test clones one directly on a pool miss. So it opens the first physical connection to that database.
`TestDbFixtureLifecycle.BuildOptions` sets no retry strategy. EF's default Npgsql strategy therefore wraps any exception
that `NpgsqlTransientExceptionDetector` calls transient as this `InvalidOperationException`. That is a `NpgsqlException`
with `IsTransient` set, or a `TimeoutException`.

Inner exceptions that fit the facts:

- A connection-open timeout. The test connection strings set no `Timeout`, so Npgsql's default of 15 s applies. That fits a
  17.43 s case only if the git fixture and the clone took at most about 2.4 s.
- `53300` too many clients (`max_connections` is 500, a06d64f71268841143fe623c0c344928be7910c4), `57P03` cannot connect now,
  or a reset or refused socket. These fit any duration.

Inner exceptions ruled out:

- The 30 s Npgsql command timeout. The whole case lasted 17.43 s, so the INSERT cannot have run for 30 s.
- Deadlock (`40P01`) or serialization failure (`40001`). The write is one INSERT in an implicit transaction, on a database
  that no other connection uses. Each case has its own clone, and `LandingGitFixture` draws `TaskId` from `Guid.NewGuid()`.
- A duplicate key (`23505`) or an enum or cast error. Npgsql does not call these transient. They surface as
  `DbUpdateException`, not as this wrapper.
- The harness's own save interceptors (`SaveFault`, `RetirementCut`). They are not armed in this test.

What overlapped at 07:53Z:

- The run was in a busy stretch: TRX 791 at 07:41Z and 862 at 08:08Z.
- The desktop production Postgres was serving about 200 qps, plus the live AppHost.
- A server2 rollout was active.

The Windows run uses its own test Postgres container. Whether that container hit connection or I/O pressure at 07:53Z
was not observed.

The test was added in d883c22e92bbd5d6d5333d9d7fab670f3f1c200b (CARD-0824 S4a). The seeding path dates from
cf59f10837c795f57d8dc426977e2eda0a11afeb (CARD-0448), and the clone pool from
a06d64f71268841143fe623c0c344928be7910c4. Nothing on this path changed for this rc.

## Fix (detection)

Commit 951ed73f8f32a8f4f73fa05d12f654b1a46b242a. Every context built from `TestDbFixtureLifecycle.BuildOptions` now
carries `TestDbFailureEvidence.Interceptor`. On a transient connection-open or command failure, it writes one compact line
to the test's stdout. The line has the tag `[db-failure]`, the phase, the database name, the elapsed time, and the whole
exception chain with types, `SqlState` and messages. The full `ToString()` follows that line. It writes nothing on success
or on a non-transient failure. The next occurrence then names its cause in the TRX StdOut, where
`scripts/orchestration/trx-failures.ps1` reads it. The witness is
`TestDbFixtureLifecycleTests.Transient_db_failure_writes_its_inner_exception_to_the_test_output`. It was red at
e899e564fca06e324f4f40d7cc6d170136d4abcd with nothing written, and is green at the fix.

No retry, timeout or assertion was changed: the cause is unproven, so loosening would hide the next sample.

## Introducing commit/card

introducing commit/card: none (environment). The failing test is d883c22e92bbd5d6d5333d9d7fab670f3f1c200b (CARD-0824).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- not applicable: no change introduced this failure. The class passes in a selection run on server2.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration, `tests/Antiphon.Tests/Application`.
When a Linux lane runs this class, it runs against server2's own test Postgres container, not under the
Windows host's Docker Desktop load, and a single transient connection failure does not reproduce from a selection.
Only the Windows Full lane under qualification load sees it. A pre-land check cannot catch it. What can be improved is
attribution, which this fix adds.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/TestHelpers/TestDb*.cs` -> `/*/*/TestDbFixtureLifecycleTests/*`

## Exemption

none
