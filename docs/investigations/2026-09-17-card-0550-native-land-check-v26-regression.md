# CARD-0550 investigation: native land check V26 fails on master

Date: 2026-09-17. Task fb65210d (Investigate, Shared). Evidence only; no fix designed here.

## Outcome in one line

`C467_V26_HardCrashAfterQueueInsertReusesRow` still fails at master tip `7fc575c9` on the
evidence assertion at `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:132`
(`queue-existing-key-*.observation.json should not be empty`). The bisect lands on
`c3dc6f4c` (CARD-0481 round-4 F3 repair, 2026-09-15 21:54 +0100), which predates all three
landings named on the card, so CARD-0527/0549 (`e2a49f3a`), CARD-0462 (`27daabe5`) and
CARD-0544 (`f091e84d`) all inherit it. Mechanism: `c3dc6f4c` added a pre-link block to
`AgentTaskLandNotificationService.ReconcileAsync` that links an already-inserted keyed queue
row directly and never calls `SessionMessageQueueService.EnqueueAsync`, and the
`queue-existing-key` boundary the test waits for lives inside `EnqueueAsync`. The delivery
contract V26 protects (exact queue row reused, one prompt, receipt confirmed) still holds at
tip; what regressed is the test's evidence channel. Secondary finding: the test's 60 s barrier
deadline is marginal on this machine (the land protocol issues about 740 git commands; 56 s
quiet, 140 s loaded), which is what produced every `TimeoutException: queue inserted crash
cut` on the card, in CARD-0540's Code round, and in two of my own tip/parent runs.

## 1. Reproduction status at current master tip

Build: `dotnet build tests/Antiphon.E2E --property:OutputPath=bin-c550/` at `7fc575c9`
(1 m 26 s). Run:

```
dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-c550/ -- --treenode-filter "/*/*/AgentTaskLandDeliveryE2ETests/C467_V26_HardCrashAfterQueueInsertReusesRow" --report-trx --report-trx-filename v26-tip-7fc575c9-run1.trx --results-directory C:/src/Antiphon/.antiphon/c550-investigate
```

| Run | SHA | Result | Failure | Barrier reached | `queue-existing-key` files |
|---|---|---|---|---|---|
| tip run 1 | `7fc575c9` | failed, 2 m 01 s | `ShouldAssertException` at line 132 | yes (`queue-inserted.barrier.json`, pid 4740) | 0 |
| tip run 2 | `7fc575c9` | failed, 2 m 47 s | `TimeoutException: queue inserted crash cut` (line 121, 60 s wait) | no; land at Phase 5 after 509 git calls in 60 s | n/a |

Tip run 1 is the card's failure exactly as task f9cd2ab5 recorded it. Tip run 2 is the
secondary timing failure (section 5), not a second mechanism. Machine load during these
runs: 55-62 % total CPU, seven other `claude` processes, 36 live `Antiphon.PtyHost`
processes (census-diverged surplus 35, pre-existing).

DB fingerprint of tip run 1 (`delivery-evidence.json`, evidence root
`tip-7fc575c9-run1-root`): one `AgentTaskLandNotifications` row, Kind Outcome, State
Confirmed, `QueueMessageId = 14d4f438…` equal to the single `SessionQueuedMessages` row,
`EnqueueAttempts = 0`, `EnqueuedAt = 2026-09-17T20:30:42.997322Z` **identical to the queue
row's `CreatedAt`**, `DeliveryAttempts = 1`, `ConfirmingPromptSequence = 4`, one
`[land …]` UserPrompt. That is: the crashed child's row was reused, delivered once, and
confirmed. Only the observation file is missing.

## 2. Bisect

Last known good on the card is `3b0ca030` (task 4cba9241, 2026-09-16 07:46Z, baseline TRX
`C:\Antiphon\.antiphon\review-4cba9241\baseline-land-C467_V26_HardCrashAfterQueueInsertReusesRow\run.trx`,
Passed, 1 m 58 s). The range `3b0ca030..f091e84d` is 120 linear commits. Position of the
candidates counted from the newest:

| Position | SHA | Commit |
|---|---|---|
| 1 | `f091e84d` | CARD-0544 landing tip |
| 23 | `27daabe5` | CARD-0462 landing tip |
| 51 | `e2a49f3a` | CARD-0527/0549 landing tip |
| **101** | **`c3dc6f4c`** | **fix(CARD-0481): recover delivered caller row before terminal guard** |
| 102 | `f9b4b6d3` | docs(CARD-0481): F2 ordinary verified (parent of the above) |

`git log -S"The producer can commit and deliver the keyed row before this outbox links it" --
server/Application/Services/AgentTaskLandNotificationService.cs` returns only `c3dc6f4c`.
The test, fixture and `LandDeliveryOptions` files are unchanged across
`3b0ca030..f091e84d` (`git log` on those four paths is empty), and no landing-protocol file
(`AgentTaskLandService`, `AgentTaskLandingProtocol`, `server/Infrastructure/Git`) changed
between `3b0ca030` and `f9b4b6d3`.

Detached worktrees `C:\Antiphon\worktrees\c550-pre-c3dc6f4c` (`f9b4b6d3`) and
`c550-at-c3dc6f4c` (`c3dc6f4c`), each built with the default output path and given the main
checkout's `client/dist` (the fixture refuses to start without `client/dist/index.html`).

| Run | SHA | Test code | Result | `queue-existing-key` files | Note `EnqueueAttempts` | Note `EnqueuedAt` vs row `CreatedAt` |
|---|---|---|---|---|---|---|
| at run 1 | `c3dc6f4c` | stock | failed at line 132, 2 m 03 s | 0 | 0 | equal |
| at run 2 | `c3dc6f4c` | barrier wait 240 s | failed at line 132, 5 m 01 s | 0 | 0 | equal (`21:00:59.736763Z`) |
| pre run 1-3 | `f9b4b6d3` | stock | `TimeoutException: queue inserted crash cut` ×3 | n/a | n/a | n/a |
| pre run 4 | `f9b4b6d3` | barrier wait 240 s | **passed**, 4 m 24 s | **1** | **1** | differs (`20:53:13.84Z` vs `20:52:54.67Z`) |

The only test-code change in the two worktrees is the diagnostic patch
`C:\Antiphon\.antiphon\investigate-fb65210d\diagnostic-barrier-240s.patch`: the `UntilAsync`
deadline for the `queue-inserted.barrier.json` wait at
`AgentTaskLandDeliveryE2ETests.cs:121` raised from the 60 s default
(`LandDeliveryFixture.cs:315`) to 240 s. It was applied identically to both sides, never
committed, and does not touch the line-132 assertion. Pre runs 1-3 never reached the barrier
because the land protocol took longer than 60 s under load (section 5); their DB snapshots
show the request still pending at Phase 2-9 with no notification row.

Pre run 4's observation file, written by the **recovery** child (pid 48480, the second
`child-*.json` identity; the crashed child was pid 7488):

```
{"boundary":"queue-existing-key","taskId":"e579fda1-cc36-429b-96bd-0befd22e5143","identity":"d3c5d4c1-9341-439c-ba7f-9a9cf68e5727","at":"2026-09-17T20:53:14.514554Z","pid":48480}
```

`identity` equals the reused queue row id and the note's `QueueMessageId`.

## 3. Mechanism

The V26 scenario: busy caller, cut `queue`. The producing child inserts the keyed queue row
and is killed at the `queue-inserted` boundary (`LandDeliveryOptions.cs:97`,
`AgentTaskLandNotificationService.cs:129`) before `note.QueueMessageId` is saved. The test
then restarts a child with cut `none`, releases the busy caller, and expects the outbox to
recover the row.

Before `c3dc6f4c` (as measured at `f9b4b6d3`): `ReconcileAsync` reaches the enqueue branch
(`note.QueueMessageId is null`), passes the destination Stopped/Failed gate and the lease
probe, increments `EnqueueAttempts` (line 121), and calls `messages.EnqueueAsync(…,
sourceLandNotificationId: note.Id, …)` (lines 125-130). Inside `EnqueueAsync`, under the
per-session queue lock, the keyed lookup at `SessionMessageQueueService.cs:352-364` finds the
existing row, validates destination and digest, invokes `onCreated(existing.Id)` and fires
`boundary.ReachedAsync("queue-existing-key", …)` (line 362). `FileBoundary.ReachedAsync`
writes the observation file for that boundary name (`LandDeliveryOptions.cs:74-76`).
`ReconcileAsync` then sets `EnqueuedAt = now` and wakes the flusher (line 139).

After `c3dc6f4c` (as measured at `c3dc6f4c` and tip): the new block at
`AgentTaskLandNotificationService.cs:50-68` runs first. When `note.QueueMessageId is null`
it looks up `SessionQueuedMessages` by `SourceLandNotificationId == note.Id` (line 55),
validates the same destination/digest identity, sets `QueueMessageId = existing.Id`,
`EnqueuedAt = existing.CreatedAt` (line 61), `State = AwaitingReceipt`, saves, and falls
through to the receipt branch. The enqueue branch at line 69 is skipped, so `EnqueueAsync`
is never called and the `queue-existing-key` boundary is unreachable on this path. The DB
fingerprint in every failing run matches exactly: `EnqueueAttempts = 0` and `EnqueuedAt`
byte-equal to the row's `CreatedAt`; in the passing run they are 1 and different.

This was the intent of `c3dc6f4c`, not a side effect. Its plan section "Round-4 F3 repair
design" (`docs/superpowers/plans/2026-09-15-card-0481-wall-reroute-capacity-gate-plan.md:1106-1143`)
says "Recovery first discovers an unlinked queue row by SourceLandNotificationId … Only the
absence of an existing row permits the stopped/failed destination guard to block new input",
and its test V-19 (`tests/Antiphon.Tests/Application/ReceiptFailureDeliveryTests.cs:61`,
`:270`) asserts `EnqueueAttempts.ShouldBe(0, "recovery links the existing row without a new
enqueue")`. The F3 bug it fixed was a delivered-but-unlinked row whose caller had since
Stopped/Failed: the old ordering hit the destination guard first and parked the note as
`DestinationUnavailable`.

Remaining live callers of the keyed `EnqueueAsync` branch after `c3dc6f4c`:
`ReconcileAsync` itself only when no row exists at its own pre-check (so the branch at
`SessionMessageQueueService.cs:356-364` now fires only if a row appears between the
pre-check and the locked lookup), and `AgentTaskDispatcher.cs:1344-1351` (never-started
failure note). The unit-level helper `tests/Antiphon.Tests/TestHelpers/LandQueueRaceWorker.cs`
(`:62`, `:128`) exercises the `queue-key-absent` side, not `queue-existing-key`.

## 4. What V26 protects, and what still holds at tip

CARD-0508's row for this cut (`docs/superpowers/plans/2026-09-14-card-0508-…:1343`):
"Busy parent; observe keyed queue row and null note.QueueMessageId, kill child, restart,
reuse exact queue ID, release parent and receive once." At tip every behavioural assertion
up to line 131 passes: `note.QueueMessageId == null` at the cut, one keyed row with
`DeliveryAttempts == 0`, receipt confirmed, `received.QueueMessageId == queueId`
(line 131). The DB shows exactly one queue row, `DeliveryAttempts = 1`, one confirming
UserPrompt. The assertions after line 132 (`AssertRemoteAsync`, `AssertOnePromptAsync`)
are not reached by the runner but their inputs are present in the snapshot (remote
containment recorded, one `[land …]` prompt).

Two behavioural differences of the pre-link path were checked because they are not covered
by V26 or V-19:

- No `flushes.TryEnqueue(session)` after the pre-link save (only the enqueue branch wakes
  the flusher, line 139). Backstop: `CompletionNoteWorkHostedService.ScanAsync`
  (`server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs:24-51`) runs
  every 1 s and enqueues a flush for any session holding a Pending row with
  `DeliveryAttempts == 0`, `SourceTaskId` and `ContentDigest` set; the land Outcome row has
  both. Evidence: 3-5 `completion-scan-*.observation.json` files in each tip/at run.
- The pre-link runs outside the per-session queue lock (`SessionMessageQueueService.cs:344`).
  It performs no insert, so it cannot create a duplicate row; it only writes the note.

Affected test names: `C467_V26_HardCrashAfterQueueInsertReusesRow` and its two aliases
`C488_ApprovalDeliveryCrashMatrix` (`AgentTaskLandDeliveryE2ETests.cs:284`) and
`C488_ApprovalQueueInsertCrashReusesRow` (`:299`), which call V26 directly.
`DispatchBaseWarningDeliveryE2ETests.C540_QueueInsertCrashReusesRows` (`:71`) uses the same
`queue` cut but asserts no `queue-existing-key` file.

## 5. Secondary finding: the 60 s barrier deadline is marginal on this machine

Every `TimeoutException: queue inserted crash cut` seen on this card is the same thing: the
land protocol did not finish within the 60 s `UntilAsync` default, so the `queue-inserted`
barrier was never reached. Measured from the `protocol-git-*.json` records the fixture's
`EvidenceGit` writes (one per git invocation, all from the child pid):

| Run | Git invocations to the barrier | Wall time | Outcome |
|---|---|---|---|
| tip run 1 | 740 | 56.7 s (20:29:46 to 20:30:42) | barrier reached with about 3 s to spare |
| at run 1 | 740 | 58.2 s | barrier reached |
| pre run 4 (240 s wait) | 741 | 139.4 s (20:50:54 to 20:53:14) | barrier reached at 2 m 19 s |
| pre run 1 / 2 / 3 | 294 / 678 / 332 in 60 s | timed out | request still pending at Phase 2 / 9 / 5 |
| tip run 2 | 509 in 60 s | timed out | Phase 5 |

Per-command latency ranged from about 78 ms (quiet) to about 200 ms (loaded); a full land
needs about 740 commands, so 60 s is inside the noise band. Prior instances of the same
symptom: the 2026-09-16 on-branch V26 run in `C:\Antiphon\.antiphon\review-4cba9241\land-C467_V26…\run.trx`
(07:30Z, `TimeoutException: queue inserted crash cut`) whose retry at 07:55Z passed; and
CARD-0540's Code round (task 4435b705, `C:\Antiphon\.antiphon\code-4435b705`,
`land-C467_V26….trx`: `TimeoutException: queue inserted crash cut`), which is what the
original filing generalised into "all five time out". Task f9cd2ab5's quiet-machine rerun
and my tip run 1 both got past the barrier and hit line 132; that is the only failure that
survives a quiet machine.

## 6. Why no landing's review caught it

`c3dc6f4c` landed with CARD-0481 on 2026-09-15, before the three landings on the card, so
bisecting those three could only have shown all three red. CARD-0481's own verification
profile was Unit plus twenty named integration classes with "no new test spawns a native
process" (`…card-0481-wall-reroute-capacity-gate-plan.md:890`, `:1138`); the
`[Category("OptIn")]` native class `AgentTaskLandDeliveryE2ETests` was in no card's V/R
scope until CARD-0540's Code round ran the broader native selection on 2026-09-17. The
card's item 3 (per-card scoping blind spot for OptIn native rows) is confirmed by this
history; it is a policy question, not something this investigation resolves.

Documentation correction still owed (noted on the card revision): the R-5 paragraph at
`docs/investigations/2026-09-16-card-0540-code-verification.md:44-47` claims five native
rows regressed; only V26 does, and its failure is the line-132 assertion, not a timeout.

## 7. Remaining uncertainties

- No run in this investigation was on a quiet machine; the barrier-timing numbers are an
  upper bound from a loaded host and the "78 ms quiet" figure comes from the two runs that
  happened to clear the deadline.
- I did not measure whether an *idle* caller's recovered row (pre-link path, no explicit
  flush wakeup) is delivered within one scan tick; the 1 s scan backstop is inferred from
  the code and the completion-scan observation files, not from a timed idle-caller run.
- V26's post-132 assertions were not executed at tip by the runner; their inputs are present
  in the DB snapshot but `AssertOnePromptAsync` (two further notification scans, native
  `updates.jsonl` count) was not run to completion.

## 8. Not done, noted

- Fix idea: either move V26's evidence expectation to the boundary the recovery path now
  reaches (a new named boundary in the pre-link block, or the DB fingerprint
  `EnqueueAttempts == 0 && EnqueuedAt == row.CreatedAt`), or fire `queue-existing-key` from
  the pre-link block too; one line, not designed here.
- Fix idea for section 5: raise the `queue-inserted` barrier wait (and audit the other 60 s
  waits) or cut the roughly 740 git invocations per land; not designed here.
- `docs/investigations/2026-09-16-card-0540-code-verification.md:44-47` correction: small
  docs follow-up, not done here.

## 9. Evidence index

External evidence root: `C:\Antiphon\.antiphon\investigate-fb65210d\` (8.9 MB): every TRX
and log named above (`v26-tip-7fc575c9-run{1,2}`, `v26-at-c3dc6f4c-run1`,
`v26-at-c3dc6f4c-run2-barrier240`, `v26-pre-f9b4b6d3-run{1,2,3}`,
`v26-pre-f9b4b6d3-run4-barrier240`), one folder per acceptance root with its
`delivery-evidence.json`, barrier/observation JSON, `child-logs/`, `server-logs/` and
`protocol-git-count.txt` (`tip-7fc575c9-run{1,2}-root`, `at-c3dc6f4c-root-{1,2}-<nonce>`,
`pre-f9b4b6d3-root-{1..5}-<nonce>`; root 1 is pre run 4, the pass), the diagnostic patch,
and `analyse_root.py` (the script that produced the tables). The main checkout keeps the
tip TRX/logs under `.antiphon/c550-investigate/` and the two tip acceptance roots under
`.antiphon/acceptance/card-0467/` (gitignored). The two bisect worktrees and the eleven
`bin-c550/` output directories were removed after copying.
