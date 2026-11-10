# Run 6591e378 (rc/2026-11-15, desktop Windows) - e2e-all-split-7 land delivery failure

Read-only extraction from
`C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378\`
(`e2e-all-split-7.trx`, `e2e-all-split-7-tests.log`, `e2e-all-split-7.timing.json`,
`e2e-all-split-7-selection.discovery.json`,
`e2e-all-split-7-execution\log_261110190530475.diag`).
Source under test: `C:\Antiphon\releases\checkout-4dd2a426` at
`ac6d00022e940ee3b90584c7d313567b4491c498` (`rc/2026-11-15`). No code change is proposed here.
The qualification was still running when this extract was taken. Nothing in that run was stopped
or written.

## Chunk

| Item | Value |
|---|---|
| Suite / chunk | `e2e` / `all-split-7` |
| TRX | `e2e-all-split-7.trx` (184482 bytes, finish `2026-11-10T19:12:58.3672761Z`) |
| Timing | started `2026-11-10T19:05:30.2950313Z`, completed `2026-11-10T19:12:59.0193468Z`, `lastPhase=complete` |
| Run wall | 448.647 s (TUnit summary duration `7m 27s 773ms`) |
| Counters | total 2, executed 2, passed 1, failed 1, error 0, timeout 0, aborted 0, notExecuted 0 |
| Runner outcome | `Failed`. RunInfo: `Exit code indicates failure: '2'` on `DESKTOP-KTLKPIF` |
| Host | TUnit 1.44.0, MTP 2.2.2, .NET 9.0.22, win-x64, Windows 10.0.19045 |
| Build | `buildSeconds` 655.862, `buildSlot=not-used`, `queueWaitSeconds` 0. The chunk wall is the 448.647 s run, not that build figure |
| Assembly | `C:\Antiphon\releases\checkout-4dd2a426\tests\Antiphon.E2E\bin\Debug\net9.0\Antiphon.E2E.dll` |
| Invocation | `C487_INVOCATION=6591e378c04d47bc898cf0927352b01b` |
| Timing run id inside the fixture | `7910da8f4f484012846cd26ac20b49dd` |

Name-filtered directory listing (`e2e-all-split-7*` only):

- `e2e-all-split-7-execution\` containing `log_261110190530475.diag` (374589 bytes)
- `e2e-all-split-7-selection.discovery.json` (1094 bytes)
- `e2e-all-split-7-tests.log` (103776 bytes) — this is the console log
- `e2e-all-split-7.timing.json` (1015 bytes)
- `e2e-all-split-7.trx` (184482 bytes)

No separate console file uses that name prefix. The tests log is the console transcript.

## Failed test

- Test: `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix_Batch2(enqueue-exception)`
- Outcome: Failed
- Duration: `00:04:08.0607910` (248.061 s)
- Start: `2026-11-10T19:05:30.9037250Z`
- End: `2026-11-10T19:09:38.9645160Z`
- Computer: `DESKTOP-KTLKPIF`
- There is no expected-versus-actual pair. The message is only the wait's evidence string.

### Assertion message

```
TimeoutException: two persisted review enqueue failures
```

Wrapped by the runner as
`TUnit.Engine.Exceptions.TestFailedException: [Test Failure] TimeoutException: two persisted review enqueue failures`.

### Stack (top frames, as reported)

```
   at Antiphon.TestSupport.ProgressAwareWait.UntilAsync(Func`1 predicate, String evidence, Int32 seconds, Func`1 progress, Int32 capSeconds, TimeProvider clock) in C:\Antiphon\releases\checkout-4dd2a426\tests\Shared\ProgressAwareWait.cs:line 36
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix(String cut) in C:\Antiphon\releases\checkout-4dd2a426\tests\Antiphon.E2E\AgentTaskLandDeliveryE2ETests.cs:line 430
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix(String cut) in C:\Antiphon\releases\checkout-4dd2a426\tests\Antiphon.E2E\AgentTaskLandDeliveryE2ETests.cs:line 456
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

Line 36 is the branch that throws when `progress` is null:

```
if (progress is null) throw new TimeoutException(evidence);
```

The following line would have appended `(quiet {seconds}s after {elapsed}s; progress {last})`.
That suffix is absent, so this expiry is the plain deadline, not a progress-renewed quiet timeout.
Line 456 is the async state-machine frame at the end of `C488_ReviewDeliveryCrashMatrix`. The call
that timed out is line 430.

### What the wait required

`C488_ReviewDeliveryCrashMatrix_Batch2("enqueue-exception")` calls
`C488_ReviewDeliveryCrashMatrix`. `ReviewCut("enqueue-exception")` is `"enqueue-errors"`.
The method then does:

```
await LandDeliveryFixture.UntilAsync(async () =>
{
    await using var db = f.CreateContext();
    return await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == f.ReviewTaskId
        && n.Kind == LandNotificationKind.TaskCompletion && n.EnqueueAttempts == 2 && n.QueueMessageId == null);
}, "two persisted review enqueue failures", 180);
```

`LandDeliveryFixture.UntilAsync` forwards to `ProgressAwareWait.UntilAsync` with no progress probe.
The third argument is 180 seconds. The predicate never returned true inside that budget.

The cut (`LandDeliveryOptions.ReachedAsync`) throws on the first two `before-enqueue` reaches:

```
if (options.Cut == "enqueue-errors" && boundary == "before-enqueue" && Interlocked.Increment(ref _enqueueCalls) <= 2)
{
    await File.WriteAllTextAsync(... $"enqueue-failure-{Guid.NewGuid():N}.json" ...);
    throw new IOException("Owned notification insert failure");
}
```

The captured stdout never prints that `IOException`, an `enqueue-failure-*.json` path, or the
notification row. The wait itself does not log the attempt count it observed.

The test ended at `19:09:38.964Z`. One hundred and eighty seconds earlier is `19:06:38.964Z`,
about 68 s after the test start (`19:05:30.903Z`). That matches fixture setup, then a full 180 s
poll. It is not an early crash inside the wait.

## Stderr and nearby log lines

Stderr for the failed test is one line (130 bytes):

```
[TUnit] External span cap of 100 reached; subsequent spans will be dropped. Set TUNIT_OTEL_MAX_EXTERNAL_SPANS to raise the limit.
```

Stdout application lines before the span dump, in order:

- `00:00:00.22` testcontainers connected to Docker. Host `npipe://./pipe/docker_engine`, server `29.5.3`, kernel `6.18.58.1-microsoft-standard-WSL2`, API `1.54`, operating system `Docker Desktop`, memory `7.76 GB`.
- `00:00:00.57`–`00:00:02.17` container `a0b351c592c9` created, started, ready.
- `00:00:02.25`–`00:00:06.26` container `40299df7413c` created and ready. `pg_isready --host localhost --dbname antiphon_e2e --username test` ran four times (`00:00:02.62`, `00:00:03.79`, `00:00:05.05`, `00:00:06.17`) and then the container was ready.
- `19:05:39 INF` `LIVE_TIMING` phase `fixture-setup` operation `config-load` elapsed `118.6923` ms, outcome `completed`.
- `19:05:39 WRN` PhoneHomeRunner singleton keys ignored because `PhoneHomeRunner:Runners` is set.
- `19:05:42 WRN` EF `BoolWithDefaultWarning` for `Agent.Kind` and `AgentTask.AgentKind` (sentinel `Raw`).
- `19:05:43 ERR` `Failed executing DbCommand` (`21ms`) `SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory"`. This is the fresh database before migrations. Migrations continue.
- `19:05:57 WRN` and `19:06:06 WRN` non-transactional migration operations from `AddTranscriptHotPathIndexes` and `Card1773TranscriptPromptStampIndex`.
- `19:06:09 WRN` EF `FirstWithoutOrderByAndFilterWarning`.
- `19:06:07.955Z` through `19:06:16.667Z` client `GET`s to `127.0.0.1:59982` (`/host-stats/history`, `/sessions`, `/host-stats`, `/capabilities`, then `/sessions/37c1a201-1ea2-41bc-860e-26ca417ef3ea`) all `http.response.status_code: 200`.
- After the span cap, no further `INF`/`WRN`/`ERR` lines were captured. The next stdout line is `[testcontainers.org 00:04:05.31] Delete Docker container 40299df7413c`.

No sharing-violation text, no Docker daemon error, and no `PostgresException` after migrate.

## What ran at the same time

Inside this chunk, nothing else ran during the failure. The two cases are sequential in one
process (the testcontainers clock continues across them):

| Test | Outcome | Start | End | Duration |
|---|---|---|---|---|
| `C488_ReviewDeliveryCrashMatrix_Batch2(enqueue-exception)` | Failed | `19:05:30.903Z` | `19:09:38.964Z` | `00:04:08.0607910` |
| `C488_ReviewDeliveryCrashMatrix_Batch2(queue-before-commit)` | Passed | `19:09:39.010Z` | `19:12:58.251Z` | `00:03:19.2410888` |

The passing case starts 46 ms after the failing case ends. Its container `6bbb653917d8` is created
at testcontainers `00:04:08.78`, three seconds after the failing case deletes `40299df7413c` at
`00:04:05.31`. Same host process, no overlap.

## Passing siblings of the same class

This chunk selected only `C488_ReviewDeliveryCrashMatrix_Batch2`. Passed in that class, in this chunk:

- `C488_ReviewDeliveryCrashMatrix_Batch2(queue-before-commit)` — Passed, `00:03:19.2410888`

That arm maps to cut `review-queue-before-commit`, not `enqueue-errors`. It bounds the fixture:
the same class, the same testcontainers Postgres path, and a live Docker daemon can finish a
review crash cut. It does not exercise the two-failure enqueue predicate.

Not in this chunk, so this extract does not record their outcome: Batch1
(`settlement-before-save`, `settlement-before-commit`), Batch3
(`queue-inserted-before-outbox-link`, `queue-committed-before-wakeup`), Batch4
(`idle-lost-wakeup`, `before-typing`), Batch5 (`prompt-before-verdict`), and the other
`C488_*` methods on the class.

Discovery (`e2e-all-split-7-selection.discovery.json`) lists both Batch2 nodes with
`excluded: true` and category `OptIn`. The TRX executed both. Discovery is not the execution result.

## Classification

Poll expiry, not a deterministic assertion on returned state.

Evidence:

- The only failure text is `TimeoutException: two persisted review enqueue failures`.
- Throw site is `ProgressAwareWait.cs:36`, the `progress is null` branch. No quiet/progress suffix, so the plain 180 s deadline expired.
- Test end minus 180 s falls inside the test, after Docker and migrations were already ready (`19:05:43` history-table error, migrations through `19:06:06`, HTTP 200 by `19:06:16`).
- TRX counters record `timeout=0`. TUnit counted a failed assertion (the thrown `TimeoutException`), not a runner timeout.
- No `ShouldBe` / expected / actual values appear in the message, the stack, or the diag `TrxExceptionProperty`.
- No sharing violation.

The log does not include the `AgentTaskLandNotifications` row at expiry, so this extract cannot
say whether `EnqueueAttempts` was 0, 1, or already 2 with a non-null `QueueMessageId`. A later
debug pass has to decide whether the review completion path never persisted two enqueue failures,
or persisted them after the 180 s budget. The captured failure itself is the poll expiring.

## Docker outage and the git fixture

This chunk did not run during the Docker-down window and it is not in the victim list.

- Chunk window: `2026-11-10T19:05:30.2950313Z` to `2026-11-10T19:12:59.0193468Z`.
- Named Docker-down window: `16:46Z` to `16:55Z` on the same day. The chunk starts 2 h 10 min after that window ends.
- `origin/feat/card-task-88c9a263:docs/investigations/2026-11-10-docker-outage-victim-chunks.txt` lists only `antiphon-all-group-115.trx`, `antiphon-all-group-116.trx`, `antiphon-all-group-117.trx`, and `antiphon-all-group-118.trx`. `e2e-all-split-7` is absent.
- The failing test connected to Docker Desktop and brought up database `antiphon_e2e` in container `40299df7413c`. That is the normal E2E Postgres, not a Docker-down fallback. The land fixture still uses its own git checkout inside the test. The database side of this run was Docker.

A reading that attributes rc failures in this run to the 16:46Z–16:55Z outage, or that only
inspects groups 115–118, misses this chunk.

This is also a different failure from the rc/2026-11-12 `e2e-all-split-5` note
(`C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(busy)`, headline `should contain "busy"`).
That one was an expected-versus-actual assertion. This one is the review `enqueue-exception` wait.

## Handoff for debug

Cause to investigate: `C488_ReviewDeliveryCrashMatrix_Batch2(enqueue-exception)` on
`ac6d00022e940ee3b90584c7d313567b4491c498` waited 180 s for a `TaskCompletion` land notification
on `ReviewTaskId` with `EnqueueAttempts == 2` and `QueueMessageId == null`, and the predicate
stayed false. The `enqueue-errors` cut is supposed to throw `IOException("Owned notification insert failure")`
on the first two `before-enqueue` reaches. The sibling `queue-before-commit` arm passed immediately
afterwards on the same process, with Docker up. The row state at timeout was not logged.
