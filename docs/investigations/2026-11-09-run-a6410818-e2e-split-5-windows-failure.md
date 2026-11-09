# Run a6410818 (rc/2026-11-12, desktop Windows) - e2e-all-split-5 failure

Read-only extraction from
`C:\Antiphon\releases\candidates\rc-2026-11-12-5fef2c23b894\logs\2026-11-08-1749-a6410818\`
(`e2e-all-split-5.trx`, `e2e-all-split-5-tests.log`, `e2e-all-split-5.timing.json`,
`e2e-all-split-5-selection.discovery.json`, `e2e-all-split-5-execution\log_261109030939833.diag`).
Source under test: `C:\Antiphon\releases\checkout-c9cb914c` at
`5fef2c23b89414b8818aaf40db9ad738bce9524c`. No code change is proposed here.

## Failed test

- Test: `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(busy)`
- Outcome: Failed. Duration `00:01:13.1359379` (start `2026-11-09T03:09:40.3747Z`, end `03:10:53.5106Z`).
- Chunk summary: total 2, passed 1, failed 1 (the `(attempt)` arm passed in `00:01:03.5300026`).
- Chunk run wall 138.1 s, build 577.4 s, no build slot used; TUnit 1.44.0, MTP 2.2.2, .NET 9.0.22, win-x64.

### Error message

```
ShouldAssertException: owed.Headline
    should contain (case insensitive comparison)
"busy"
    but was actually
"Land Aged notification: Queued; not yet enqueued"
```

### Stack (complete as reported; TUnit internals omitted by the runner)

```
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(String state) in C:\Antiphon\releases\checkout-c9cb914c\tests\Antiphon.E2E\AgentTaskLandDeliveryE2ETests.cs:line 299
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(String state) in C:\Antiphon\releases\checkout-c9cb914c\tests\Antiphon.E2E\AgentTaskLandDeliveryE2ETests.cs:line 321
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

(Line 321 is the `await using var f` dispose frame at the end of the method; the assertion is line 299.)

## Test code at the failure (checkout-c9cb914c)

```
282:  await f.AdvanceLandClockAsync(301);
283:  await LandDeliveryFixture.UntilAsync(... Kind == Aged count == 1 ..., "outcome warning obligation");
287:  await f.StatusAsync();
288:  await f.AdvanceLandClockAsync(901); await f.StatusAsync();
289:  await LandDeliveryFixture.UntilAsync(... Kind == Aged count == 2 ..., "outcome warning and error obligations");
293:  var attention = await f.AttentionAsync();
294:  var owed = await f.PollingAttentionAsync(attention, requestId);
295:  owed.Severity.ShouldBe(AlertSeverity.Error);
296:  // The feed shows the newest listed receipt (CARD-1085): the Warning notice, 600 s old; the
297:  // Error notice is not yet listed. While the attempted Outcome holds the caller's queue lock
298:  // before typing, the Warning cannot be enqueued; busy enqueues it.
299:  owed.Headline.ShouldContain(state == "busy" ? "busy" : "Land Aged notification: Queued; not yet enqueued");
```

The headline is built in `server/Application/Services/AttentionService.cs:1589`:

```
var state = note.QueueMessageId is null ? "not yet enqueued" : row is null ? "missing queue row"
  : row.DeliveryAttempts == 0 ? (busy ? "queued; caller busy" : "queued; caller idle") : "attempted; receipt unconfirmed";
```

So at the attention read the Warning Aged notice still had `QueueMessageId == null` (state `Queued`):
the hosted land-notification scan had not yet enqueued it to the busy caller. Nothing between
lines 283 and 293 waits for the Warning to be enqueued; the waits are on the Aged row *count* only.

The enqueue path (`server/Application/Services/AgentTaskLandNotificationService.cs`):

- `:72` `if (note.QueueMessageId is null && note.NextAttemptAt > now) return;`
- `:148-152` CARD-1705: a caller already in the scan's `busySessions` set (its queue lock was held past
  `ScanQueueLockWait` = 1 s, `:48`) is skipped unchanged and retried on the next scan.
- `:163-169` `messages.EnqueueAsync(..., lockWait: busySessions is null ? null : ScanQueueLockWait)`.

## Server / runner log lines in the failing test's output

The captured stdout (234 KB) carries no land/notification/queue log line and no
"Land notification ... remains unresolved" warning. Only these non-EF lines appear:

```
[03:09:47 INF] LIVE_TIMING run=7f5024f78c014936b5fd6bba7b74656a phase=fixture-setup operation=config-load elapsedMs=151.9377 outcome=completed
[03:09:47 WRN] PhoneHomeRunner singleton keys are ignored because PhoneHomeRunner:Runners is set.
[03:09:50 ERR] Failed executing DbCommand (11ms) ... SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory"   (normal first-migration probe)
[03:10:03 WRN] The migration operation 'DO $retry$ ...' from migration 'AddTranscriptHotPathIndexes' cannot be executed in a transaction.
[03:10:30 WRN] PtyHostCensusDiverged: Session census diverged. Runner sessions Running: 1 (with a live agent child: 1, unclaimed by the database: 0). Live Antiphon.PtyHost processes: 13 (surplus over sessions with a live child: 12); live claude processes: 13.
[03:10:53 WRN] Session runner event stream disconnected (SessionRunnerEventPump)
System.IO.IOException: Unable to read data from the transport connection: An existing connection was forcibly closed by the remote host..
 ---> System.Net.Sockets.SocketException (10054) ...
   at ...SessionRunnerHttpClient.StreamEventsAsync(CancellationToken ct) in ...\SessionRunnerHttpClient.cs:line 1001
   at ...SessionRunnerEventPump.ExecuteAsync(CancellationToken stoppingToken) in ...\SessionRunnerEventPump.cs:line 53
[testcontainers.org 00:01:10.72] Delete Docker container 9cb2cc33e46d
```

The 03:10:53 socket reset is fixture teardown after the assertion (isolated runner killed); it is
not the cause. The single `GET /api/attention` span starts at `2026-11-09T03:10:50.52Z`; the
isolated runner was on `127.0.0.1:51931`, the server on `127.0.0.1:52057`. No startup, port, git
or pwsh error text appears. Docker Desktop 29.5.3 (WSL2 kernel 6.18.54.1), Postgres testcontainer
ready in 4.7 s. The `.diag` file (1.06 MB) repeats the same failure text and nothing else relevant.

Note: the discovery manifest lists both cases with `"excluded": true, "categories": ["OptIn"]`,
yet both executed (failed-first / rc roster selection runs OptIn cases).

## Neighbouring durations

| Chunk | Test | Seconds |
|---|---|---|
| split-4 (03:06:47-03:09:39) | C467_V30_ReceiptSaveFailureNeverRetypes(receipt) | 99.3 |
| split-4 | C467_V30_ReceiptSaveFailureNeverRetypes(verdict) | 70.8 |
| split-5 | C467_V32_...(busy) **Failed** | 73.1 |
| split-5 | C467_V32_...(attempt) Passed | 63.5 |
| split-6 (started 03:11:57) | still running at read time | - |

## History of this test on the desktop (all TRX under `releases\candidates`, last 8 days)

| Run | busy | attempt |
|---|---|---|
| c8a7bcee (11-01) | Passed 118.5 s | Passed 80.1 s |
| 7f8e9fd1 (11-03) | Passed 96.1 s | Passed 66.2 s |
| f40ad905 (11-03) | Passed 96.7 s | Passed 73.3 s |
| 0b05506b (11-04) | Passed 97.5 s | Failed 84.8 s |
| 766e3df5 (11-04) | Passed 217.5 s | Failed 163.9 s |
| b19a9880 (11-04) | Passed 91.9 s | Failed 51.9 s |
| 472c7495 (11-06) | Passed 102.1 s | Passed 72.0 s |
| 8e938b6d (11-07) | Passed 106.8 s | Passed 72.1 s |
| c3f65ddb (11-07) | Passed 102.7 s | Passed 74.9 s |
| **a6410818 (11-09)** | **Failed 73.1 s** | Passed 63.5 s |

First failure of the `busy` arm in ten runs, and its fastest run by ~19 s. The `attempt` arm failed
three times on 11-04 with the related headline mismatch fixed by `4c7026d6d`/`7c9c4cb15`
("C467_V32 waits for the Warning notice before 901 s").

Changes in the land-notification path between the last passing rc (`0e7aef0379a0`, run c3f65ddb)
and this rc (`5fef2c23b894`, 219 commits): `593ba300b` fix(card-1769) "park unchanged
AwaitingReceipt rescans" in `AgentTaskLandNotificationHostedService.cs` (skips a linked
AwaitingReceipt note while its session stamp is unchanged, 1-5 min backoff). CARD-1705 (1 s scan
lock wait, `b48989fa6`) and CARD-1759 (`4d91773c1`) were already in the passing rc.

## Desktop tool versions (PATH)

- git: `C:\Program Files\Git\cmd\git.exe`, `git version 2.50.1.windows.1`
- pwsh: `...\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe\pwsh.exe`, `7.6.9`
- dotnet SDK: `10.0.300` (test host .NET 9.0.22)

## Diagnosis

The `busy` arm asserts (line 299) that the newest listed receipt, the 600 s-old Warning Aged
notice, already has a queue row on the busy caller ("queued; caller busy"). The test only waits for
the Aged rows to *exist* (lines 283-292), never for the Warning to be *enqueued*. Enqueue happens
asynchronously in the hosted land-notification scan (5 s period), which since CARD-1705 also skips a
caller whose queue lock is held for more than 1 s and retries next pass
(`AgentTaskLandNotificationService.cs:48,148-152,169`). On this run the attention read came at
03:10:50.52 while the Warning was still `QueueMessageId == null`, so `AttentionService.cs:1589`
rendered "not yet enqueued". This is a test-side ordering race (no wait on the enqueue the
assertion depends on), exposed by a faster run (73 s against 92-218 s before) and possibly by
CARD-1769's changed scan timing; nothing in the logs shows a product error, startup/port problem,
git/pwsh fault or a Windows-only path issue. Whether the Warning would have been enqueued a scan
later (pure race) or was being held off by the caller's queue lock (CARD-1705 skip) cannot be told
from the captured logs, because the hosted scan logs nothing on a skip and the fixture's
`status-polls.txt` / root files are not retained.

Why missed before rc: the assertion's precondition depends on scheduling between the monitor sweep,
the hosted notification scan and the test thread; it passed 9/9 earlier desktop runs and the
`(busy)` arm was not part of the 11-04 C467_V32 repair, so no pre-rc gate exercised it at a
faster-than-usual pace.
