# C488_ReviewDeliveryCrashMatrix_Batch3(queue-committed-before-wakeup) failed in rc run a6278259 (2026-11-19)

Debug task c460dc42. Read-only investigation on the desktop while the Windows Full qualification was live.
No builds, test runs or restarts were made.

## Verdict

**An intermittent miss in recovery, with a latent single-shot recovery defect behind it.** It is not a
regression, not DB latency and not the environment.

- After the crash cut, the restarted child host **never attempted delivery**. It made no `POST /input` to
  the caller in 180 s. It also left **no `completion-scan` observation**: its startup recovery sweep never
  saw the committed, unwoken queue row. In every passing run that sweep fires within about 1 s of child
  start and types the receipt within about 0.2 s of `Now listening`.
- After the startup sweep, the only remaining recovery source is the 15-minute backstop sweep. The 5 s
  notification scan never wakes the flush for a linked queue row that is still Pending with zero attempts.
  When the one startup sweep misses, nothing recovers inside the test's 180 s window.
- Why the one sweep missed cannot be decided from the retained evidence. The fixture snapshot records only
  the Code task's rows, not the Review task's note and queue row, and the Testcontainers database was
  deleted at teardown. Both candidates are below.
- **DB latency is ruled out.** This test runs its own Testcontainers Postgres on the desktop, reached as
  localhost; it never touches the server2 Postgres. The failure is also not slowness: zero delivery
  attempts were made. See section 3.

## 1. What the test waits for

`tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:420` `C488_ReviewDeliveryCrashMatrix("queue-committed-before-wakeup")`:

1. `UseChildAsync("review-before-wakeup")` starts a real child Program host. The fixture's `FileBoundary`
   (`tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs:269`) blocks that host at the
   `queue-committed-before-wakeup` boundary.
2. `StartReviewDelegateAsync("raw")` waits up to 180 s for the native FakeGrok Review session.
   `ReleaseReviewReportAsync()` then lets it emit its report.
3. `AssertReviewHeldAsync` waits up to 180 s for `queue-committed-before-wakeup.barrier.json`. It asserts
   that the Review is Succeeded with one Clean outcome, that the TaskCompletion note is linked to its queue
   row, and that `ConfirmedAt` is null. It does **not** assert the row's `Status` or `DeliveryAttempts`.
4. `KillChildAsync()` kills the held host. This is the "wakeup lost" cut: the reconcile stopped in
   `AgentTaskLandNotificationService.cs:209`, just before `flushes.TryEnqueue(session)` at line 211.
5. `UseChildAsync("none")` starts a clean child host.
6. `WaitForReviewReceiptAsync()` (`LandDeliveryFixture.Review.cs:104-113`) polls every 100 ms for **180 s**,
   not 5 minutes. It looks for a TaskCompletion note of the Review task with `ConfirmedAt != null`. The
   call passes no progress probe, so the wait is a plain 180 s deadline (`tests/Shared/ProgressAwareWait.cs:36`).
   The timeout message is the bare evidence string `complete native Review receipt`.

The production recovery this arm exercises: on boot, `CompletionNoteWorkHostedService.RecoverAsync`
(`server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs:35-105`) sweeps
`SessionQueuedMessages` rows with `Status == Pending && DeliveryAttempts == 0 && SourceTaskId != null &&
ContentDigest != null`. For each row it passes the `completion-scan` boundary, which the fixture records as
`completion-scan-*.observation.json`, then calls `ScheduleFlush`, then `FlushIfIdleAsync`. That types the
completion into the idle native caller, and the 5 s land-notification scan then confirms the receipt.
After this first sweep, the next sweep is `now + 15 min` (line 52). `AgentTaskLandNotificationService.ReconcileAsync`
calls `flushes.TryEnqueue` only on a fresh enqueue (line 211) or on relinking an orphaned row (line 134).
For an already-linked Pending row it never wakes the flush (lines 213-449).

## 2. Evidence

Fixture root (retained): `C:\Antiphon\releases\checkout-fc9a0547\.antiphon\acceptance\card-0467\d78ee5e9f3964087b2c88cf2e3429ea4`.
TRX: `...\logs\2026-11-18-1823-a6278259\e2e-all-split-8.trx`. The test ran 00:57:35.52-01:03:24.64Z (5:49).
Its sibling `queue-inserted-before-outbox-link` passed in 3:22.

### Timeline (UTC, 2026-11-19)

| Time | Source | Event |
|---|---|---|
| 00:57:3x | TRX stdout | `[testcontainers.org] Docker container c60167aaf3d4 ready` (`pg_isready --host localhost --dbname antiphon_e2e`) |
| 00:57:53.29 | caller `updates.jsonl` | Caller `b7023225` turn 1 `C467 caller ready`, turn completed (idle from here) |
| 00:58:06.74 | child-logs | Child 1 (pid 70208, cut `review-before-wakeup`) `Now listening on: http://127.0.0.1:53063` |
| 00:58:12.63-.74 | review `updates.jsonl.timing` | FakeGrok Review `e2a810f6` user prompt, the full report as `agent_message_chunk`, then `turn_completed` |
| 00:58:12.71 | fixture root | `review-report-hold.release` |
| 00:58:13.44 | child-logs | `Broadcasting SessionFinished for session "e2a810f6-..." (C494 review provider)` |
| 01:00:13.32 | child-logs WRN | `Session "e2a810f6-...": no text from the turn-ending response after 120s — settling on what the turn produced` |
| 01:00:13.38 | child-logs WRN | `the turn-ending response for task 81e45474 never wrote its own text within the 120s grace; settling on 347 characters of mid-turn text instead` |
| 01:00:14.21 | child-logs | `Task 81e45474 settled as "Succeeded" (315 chars, $0.000, "Marked")` |
| 01:00:14.31 | barrier | `queue-committed-before-wakeup.barrier.json` `{"taskId":"81e45474-...","identity":"fee2adeb-8e38-4782-b560-d12e6d7e1489","pid":70208}` (queue row `fee2adeb`) |
| 01:00:14.35-.39 | child-logs | Child 1: GET `/sessions`, GET `/sessions/b7023225` (caller), GET `/sessions/e2a810f6` (the same pattern appears in passing runs) |
| 01:00:14.45 | fixture root | `child-70208.stdout.log` written: child 1 killed |
| 01:00:23.52 | child-logs | Child 2 (pid 67924, cut `none`) `Now listening on: http://127.0.0.1:53354` |
| 01:00:22.9-01:03:23.6 | fixture root | 37 `notification-scan` and 37 `dispatch-warning-intent-scan` observations from pid 67924, every 5 s; **0 `completion-scan` observations** |
| 01:00:23-01:03:23 | child-logs | Child 2's runner calls on the caller: 18x GET `/snapshot` (the 10 s capacity scan), 10x GET `/transcript` (periodic catch-up), 2x GET `/sessions/{id}`. **No `POST /sessions/b7023225/input`** |
| 01:03:24.64 | TRX | `TimeoutException: complete native Review receipt` at `LandDeliveryFixture.Review.cs:107` |
| 01:03:23.71 | `delivery-evidence.json` | The caller has exactly one UserPrompt (`C467 caller ready`); its native `updates.jsonl` has no second `user_message_chunk` |

No `[ERR]` lines appear in the child log after host start. No `Completion note recovery failed`,
`Supervision recovery failed`, `Deferred completion flush failed` or `Deferring redelivery` warnings were
logged. The only WRN lines are the two 120 s grace lines above, the standing EF/WebRoot/PhoneHome
configuration warnings, `PtyHostCensusDiverged` (12 desktop PtyHost processes, nothing killed), and at
01:03:22 `Runner server2 unavailable for 3.0 min`. The last is test-host config inheriting
`PhoneHomeRunner:Runners`; it is benign and logged after the decisive window.

### Passing runs of the same arm, from retained fixture roots

| Run root | Release to barrier | 120 s grace | `completion-scan` in child 2 | Child 2 `POST /input` | Barrier to end |
|---|---|---|---|---|---|
| checkout-4b599b91 / 6374b91e (2026-11-17) | 122.3 s | yes | 1 (pid 135100, 06:57:07.76) | yes | 25 s |
| checkout-4025018e / 2675f93b (2026-11-18) | 121.9 s | yes | 1 (pid 155400, 00:34:59.79) | 00:35:01.18, 0.14 s after `Now listening` 00:35:01.05 | 24 s |
| two older roots (2026-11-05, 2026-11-16) | 126.5 s / 126.3 s | yes | - | - | 27 s |
| **checkout-fc9a0547 / d78ee5e9 (failing)** | 121.6 s | yes | **0** | **none in 180 s** | timeout |

The 120 s grace therefore appears in every run and does not discriminate. What discriminates is that
child 2's startup completion sweep never observed the row.

### Candidate mechanisms (undecided: evidence gap)

- **(A) Child 1 claimed the attempt before it was killed.** `SessionMessageQueueService.DeliverNextLockedAsync`
  sets `Status = Sent` and `DeliveryAttempts++` and saves before typing (lines 2955-2989). A claim cut
  off by the kill leaves a row the zero-attempt sweep excludes. By CARD-1892 design, a pre-typing crash
  on a Grok caller is then held and never retyped, so no receipt would ever arrive. Evidence against:
  child 1 made no caller `/snapshot` read after the barrier, and in the passing child 2 that read was the
  first runner call of a flush (00:35:01.094, then GET session at .174, then POST at .184). Child 2 also
  did not pull the caller transcript on every 5 s scan, which reconcile does for an attempted row
  (lines 326-385). No wake source other than the barrier-held reconcile is visible in child 1.
- **(B) Child 2's single startup sweep missed or never reached the Pending-row query.** For example, the
  `RecoverMissingAsync` phase before it may have blocked, or the pass may have run under some condition
  not visible in these logs. No failure warning was logged, and no second sweep was due for 15 minutes.

Under either candidate, the defect class is the same: delivery after a lost wakeup has a single recovery
shot inside any practical window.

## 3. Decision: not DB latency, not environment, not a regression

- **DB latency (server2 Postgres cutover, 2026-11-18 about 16:00Z): ruled out.** `LandDeliveryFixture` uses
  `_app.OwnedDatabase`, a per-test Testcontainers Postgres on the desktop Docker
  (`pg_isready --host localhost`; container `c60167aaf3d4` was created and deleted inside the test). It
  never uses 100.93.77.126. Child 2 logged `database-connect elapsedMs=129`, which is local. No wait in
  this path is a DB round-trip budget: the 180 s waits are end-to-end, and the passing path needs under
  25 s of them. The failure made zero attempts. It did not slow down.
- **Environment (the desktop AppHost runs rc): ruled out as cause.** The test owns its runner
  (`http://127.0.0.1:52980`, asserted not to be port 17204) and its database. The desktop CPU was busy
  with the qualification (chunk build 472 s), and that can change interleavings. But the passing path
  needs under 1 s of work after child 2 starts, and no slow call appears; the only `LIVE_SLOW_CALL` is
  the parent's 1.1 s `POST /sessions` at 00:57:49.
- **Regression: ruled out.** The last passing cut, 29ba0c50 (2026-11-17), and the failing cut, 997f9b8d,
  differ only in land verification, runner temp-sweep and slot-quarantine logging. They do not differ in
  `SessionMessageQueueService`, `AgentTaskLandNotificationService`, `CompletionNoteWorkHostedService`,
  `CompletionNoteWork`, `AgentTaskReplyService`, `AgentSessionRuntime`, `AgentTaskDispatcher` or the E2E
  fixtures (`git diff --stat 29ba0c50 997f9b8d -- <those paths>` is empty). The test sources at 997f9b8d
  equal master 0c3c3129.

## 4. History: has it failed before?

- `C488_ReviewDeliveryCrashMatrix_Batch3(queue-committed-before-wakeup)` passed in every earlier rc run
  with a retained TRX under `C:\Antiphon\releases`: **34d41b7e, b7812833, 160200cb, a21d8174, 06863906,
  f2de5b30** (2026-11-14 to 2026-11-17). **a6278259 is its first failure.** Its sibling
  `queue-inserted-before-outbox-link` passed in all seven runs, including a6278259.
- Failure seeds under `C:\Users\lndco\antiphon-seeds` (`34d41b7e.report.json`, `b7812833.report.json`)
  list other rows of this method only. `Batch2` failed in run **6591e378**
  (`docs/investigations/2026-11-10-why-missed-e2e-split-7-land-delivery.md`). `Batch4(before-typing)`
  failed in run **34d41b7e** (`docs/investigations/2026-11-10-why-missed-e2e-split9-landdelivery.md`).
  Batch3 never appears.
- Before the CARD-1584 batch split, `C488_ReviewDeliveryCrashMatrix(queue-committed-before-wakeup)` appears
  in `docs/investigations/2026-10-15-release-candidate-defects.md:98`. That was a whole-class failure of
  `AgentTaskLandDeliveryE2ETests`, an environment-level event, not this row-specific miss.

## 5. Why missed

1. **The failure is a rare interleaving, and the lineage runs the arm once per rc run.** It passed 6/6
   earlier runs. No code change in the window touches the path, so no impact selection would have picked
   it up for a recheck.
2. **Merge and Code checks never run it.** The fixture is Windows-native (`NativeWindowsGate`: native
   FakeGrok on the shipped ConPTY pair). Merge verification is short and server2-side, so only the rc
   Windows lane exercises it, and only once per run.
3. **The test proves only the happy recovery path.** It shows the startup sweep can recover a lost wakeup.
   No unit or E2E witness covers "the startup sweep did not deliver". The next recovery source, the
   15-minute backstop, lies outside every test window, so the single-shot design never shows up as a gap.
4. **Earlier passes left no early warning, and this failure is not self-diagnosing.** `SnapshotAsync`
   (`LandDeliveryFixture.cs:319-331`) snapshots only the Code task's rows (`TaskId`), not the Review task's
   note and queue row. `WaitForReviewReceiptAsync` has no progress probe and no diagnostic timeout text.
   The Review row's `Status`, `DeliveryAttempts` and `HoldUntil` at failure are therefore unrecorded, which
   is why A and B cannot be separated.
5. **A standing 120 s settlement grace eats the budget unseen.** Every run of this arm spends about 122 s
   of `AssertReviewHeldAsync`'s 180 s waiting for the native Grok turn-end text, then settles from mid-turn
   text. That leaves about 58 s of margin on that wait and inflates each run to about 210 s. Nobody flagged
   it because the arm still passed.

## 6. Fix proposal (not implemented)

Use one Code task with three slices. Slices 1 and 2 run on server2; only the E2E rerun needs the Windows lane.

1. **Production, second recovery source (`server/Application/Services/AgentTaskLandNotificationService.cs`).**
   In `ReconcileAsync`, after the linked `row` is loaded and its identity checks pass (around lines
   213-252), add a wake for that row when all of the following hold:
   - `row.Status == QueuedMessageStatus.Pending`
   - `row.DeliveryAttempts == 0`
   - `row.HoldUntil is null || row.HoldUntil <= now`
   - `boundary?.DropWakeup("completion", note.Id) != true`

   The wake is `flushes.TryEnqueue(deliveredTo)`. `TryEnqueue` is coalesced per session, and
   `FlushIfIdleAsync` is idle-gated and lock-serialized, so the 5 s hosted scan becomes a second recovery
   source for a lost wakeup. It costs one in-process channel write per open, unattempted note per pass.
   The `DropWakeup` guard keeps the `idle-lost-wakeup` and `lost-flush` arms meaningful. Add a unit
   witness in `tests/Antiphon.Tests/Application`, near the existing `queue-committed-before-wakeup` users:
   `PermissionWaitDeliveryTests` or `TurnEndGapRestartDeliveryTests`. The witness: a TaskCompletion note
   linked to a Pending zero-attempt row, no startup sweep, and one hosted-scan reconcile. It asserts the
   flush queue received the caller session. It must be red at base and green after the change.
2. **Harness evidence (`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs` and `LandDeliveryFixture.Review.cs`).**
   - Have `SnapshotAsync` also capture, when `ReviewTaskId != Guid.Empty`, the Review task's notes
     (`State`, `QueueMessageId`, `LastErrorCode`, `EnqueueAttempts`), its queue rows (`Status`,
     `DeliveryAttempts`, `LastDeliveryStartedAt`, `HoldUntil`) and its StageOutcome.
   - Make `WaitForReviewReceiptAsync` catch the `TimeoutException` and rethrow with that state and the
     count of `completion-scan-*.observation.json` files.

   The next occurrence then separates A (Sent with one attempt) from B (Pending with zero attempts and
   no completion-scan).
3. **Follow-up card, separate from this fix.** The FakeGrok Review's turn-ending text is never attributed,
   which costs a fixed 120 s grace (`no text from the turn-ending response after 120s`) in every run of
   the C488 and C494 Review arms. Either fix the attribution or make the test wait grace-aware, before
   that margin flakes under load.

Rerun filters:
- After the fix, server2: the new unit witness, by exact `--treenode-filter` on its class and method.
- After the fix, desktop Windows rc lane: `--treenode-filter "/*/*/AgentTaskLandDeliveryE2ETests/C488_ReviewDeliveryCrashMatrix_Batch3*"`,
  both rows, and preferably also `C488_ReviewDeliveryCrashMatrix_Batch4*` for `idle-lost-wakeup`.
- Before the fix: rerunning the same Batch3 filter at rc 997f9b8d is expected to pass at the historical
  6/7 rate. A pass does not clear the defect.

--- next stage ---
next: code
handoff: Implement slices 1 and 2 above on server2: the reconcile wake for a linked Pending zero-attempt row, with a unit witness that is red at base, plus the fixture's Review-row snapshot and diagnostic timeout. Then run the Batch3 E2E filter on the Windows lane. Slice 3 is a follow-up card.
artifact: docs/investigations/2026-11-19-c488-review-delivery-crash-matrix-batch3-failure.md
