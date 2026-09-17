# CARD-0550: restore V26's evidence channel at the recovery seam; make native land waits progress-aware

Plan task `3c593d7c`, 2026-09-17, inspected checkout `77e080f3` (master). Investigation:
[2026-09-17-card-0550-native-land-check-v26-regression.md](../../investigations/2026-09-17-card-0550-native-land-check-v26-regression.md)
(task `fb65210d`). Filed from CARD-0540's Code round; scope narrowed to V26 by the card's
revision 1 (task `f9cd2ab5`).

## Disposition in four lines

1. **Production is right; the test's evidence channel is stale.** `c3dc6f4c` (CARD-0481 round-4
   F3) made `ReconcileAsync` link an already-inserted keyed row before the destination guard, so
   the recovery never calls `EnqueueAsync` and the `queue-existing-key` observation that V26
   waits for is never written. The reuse contract V26 protects still holds at tip (same row,
   one prompt, receipt confirmed). Fix: fire the same boundary from the pre-link block after the
   link is saved (D-1). One production line, no test-body change.
2. **PC-13 is dead and gets a successor.** V26 no longer exercises the keyed `EnqueueAsync`
   existing-row branch, so CARD-0467's PC-13 cannot go red through it. A small unit case pins
   that branch directly and becomes PC-13's new killer; the pre-link boundary call gets its own
   control whose "mutant" is literally today's tip (D-3).
3. **The 60 s barrier is widened by evidence of work, not by a bigger number.** A land issues
   about 740 read-only git commands (56 s quiet, 140 s loaded). `UntilAsync` gains an optional
   progress probe: the 60 s quiet deadline renews whenever the fixture's `protocol-git-*.json`
   count grows, under a 600 s cap. A stalled land still fails in 60 s; a slow one is allowed to
   finish (D-4). Applied only to waits whose predicate depends on the land or dispatch protocol.
4. **Reducing the 740 git calls is a separate card.** The composition (252 `rev-parse`,
   128 `show-ref`, 86 `check-ref-format`, 63 `symbolic-ref`, 58 `remote get-url`,
   53 `worktree list`) is landing-protocol work, out of this card's scope (D-6).

## Ground truth

| Card/investigation assumption | Observed on `77e080f3` | Consequence |
|---|---|---|
| One of CARD-0462 / CARD-0527 / CARD-0544 introduced the V26 failure. | Bisect lands on `c3dc6f4c` (2026-09-15 21:54 +0100, CARD-0481 F3), position 101 of 120 in `3b0ca030..f091e84d`; the three landings are positions 1, 23 and 51 and inherit it. Test, fixture and options files are unchanged across the range. | No landing is reverted or re-reviewed. |
| Five native rows (V22, V23, V26, V30 receipt, V30 verdict) regressed. | Only V26 fails deterministically, at `AgentTaskLandDeliveryE2ETests.cs:132`. The others were `TimeoutException` / socket / duplicate-key noise under load and pass on a quiet machine (card revision 1; investigation §1, §5). | S3 corrects `docs/investigations/2026-09-16-card-0540-code-verification.md:44-47`. |
| `queue-existing-key` is the production seam that reuses a keyed row. | It is fired only inside `SessionMessageQueueService.EnqueueAsync` (`:356-364`, under the per-session lock). `ReconcileAsync` now links the row at `AgentTaskLandNotificationService.cs:50-68` **before** the enqueue branch (`:69`), sets `QueueMessageId = existing.Id`, `EnqueuedAt = existing.CreatedAt`, `State = AwaitingReceipt`, saves, and never calls `EnqueueAsync`. | The reuse seam moved; the observation must move with it (D-1). |
| The pre-link was a side effect. | It is the deliberate F3 repair: "Recovery first discovers an unlinked queue row by SourceLandNotificationId … Only the absence of an existing row permits the stopped/failed destination guard to block new input" (`2026-09-15-card-0481-…-plan.md:1106-1143`). V-19 `ReceiptFailureDeliveryTests.Delivered_caller_failure_is_confirmed_after_caller_stops_and_services_restart` asserts `EnqueueAttempts.ShouldBe(0, "recovery links the existing row without a new enqueue")` (`:270`), and CARD-0481's PC-26 restores the old ordering as its mutant. | Routing pre-linked rows back through `EnqueueAsync` is rejected (D-1). |
| The pre-link's identity guard is untested. | `AgentTaskLandNotificationRecoveryTests.C481_Recovery_rejects_a_keyed_row_with_a_different_destination` / `…_digest` (Running/Stopped/Failed, `:170-256`) prove `ConflictException`, `RetryPending`, no link, no duplicate row, zero enqueue-boundary calls. | No new guard test is needed for the mismatch arm. |
| V26's DB fingerprint at tip. | One `AgentTaskLandNotifications` row: Confirmed, `EnqueueAttempts = 0`, `EnqueuedAt` byte-equal to the queue row's `CreatedAt`, `DeliveryAttempts = 1`, `ConfirmingPromptSequence = 4`, one `[land …]` UserPrompt; `received.QueueMessageId == queueId` at `:131` passes. Pre-`c3dc6f4c` the same run had `EnqueueAttempts = 1` and a differing `EnqueuedAt`. | The behavioural assertions stay as they are; no fingerprint is added to V26 (D-2). |
| `EnqueueAttempts == 0` could pin the F3 contract in V26. | `ReconcileAsync`'s catch (`:224-236`) increments `EnqueueAttempts` on **any** exception in any branch, including `CatchUpTranscriptAsync` in the receipt branch on later scans. | Too noisy for a native lane whose defect is marginality; V-19 owns that contract (D-2). |
| PC-13 ("keyed enqueue existing-row branch: replace return-existing-ID with a conflict throw") is killed by V26. | The branch runs from `ReconcileAsync` only in the window between its own pre-check and the locked lookup, and from `AgentTaskDispatcher.cs:1344-1351` (never-started failure note). No test in `tests/Antiphon.Tests` observes `queue-existing-key`; V09 covers the mismatch arm (`ConflictException`) and the 23505 insert race (`:447-457`), not the match-and-return arm. | D-3 adds the unit case and retargets PC-13. |
| Every test boundary tolerates a new boundary name. | Of twelve `LandDeliveryBoundary` subclasses only `FailedInsert` (`AgentTaskLandNotificationRecoveryTests.cs:293`) throws on every name; its two users (`C481_…mismatch`, `C467_V08_RetryAndDestinationMatrix`) never reach a successful pre-link (the first throws `ConflictException` first; the second has no row). `C544Boundary.Reached` is asserted with `ShouldContain`, never as an exact sequence (`VerificationRoundDeliveryTests.cs:380`). `FileBoundary` writes an observation file for `queue-existing-key` unconditionally, before the `dispatch-task.txt` ownership filter (`LandDeliveryOptions.cs:74-76`). | The production call in D-1 is safe for the whole suite; C540 gains harmless observation files. |
| The pre-link wakes the flusher. | Only the enqueue branch calls `flushes.TryEnqueue(session)` (`:139`). `CompletionNoteWorkHostedService.ScanAsync` (`server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs:24-51`) runs every 1 s and flushes any session holding a Pending, `DeliveryAttempts == 0`, sourced-and-digested row; 3-5 `completion-scan` observations per tip run. | Left unchanged (D-5). |
| The barrier timeouts are a second defect. | `LandDeliveryFixture.UntilAsync(predicate, evidence, seconds = 60)` (`:339-343`) is the default for twenty call sites; the land protocol's git count to the `queue-inserted` barrier was 740 / 740 / 741 in the runs that reached it (56.7 s, 58.2 s, 139.4 s) and 294 / 678 / 332 / 509 in the runs that timed out at 60 s. Per-command latency 78-200 ms. Three waits already carry 120-150 s (`native caller ready`, `owned child startup`, `interrupted post-prompt queue rows recovered`). | D-4. |
| Git-call composition. | Tip run 1, 740 calls in 56.7 s, pid = the land child only: 252 `rev-parse`, 128 `show-ref`, 86 `check-ref-format`, 63 `symbolic-ref`, 58 `remote get-url`, 53 `worktree list`, 29 `status`, 19 `ls-files`, 18 `ls-remote`, 18 `fetch`, 8 `merge-base`, 4 `update-ref`, 1 `push`, 1 `worktree remove`; 419 against `repo`, 321 against `trees\source`. Evidence: `C:\src\Antiphon\.antiphon\acceptance\card-0467\d178e83cfc2d4f709163f4df409c2e47\protocol-git-*.json`. | Read-only inspection dominates; a protocol optimisation is a separate card (D-6). |
| The fixture already records protocol progress. | `EvidenceGit` (`LandDeliveryOptions.cs:129-146`) writes one `protocol-git-<guid>.json` per `ILandingGit` call (`RunAsync` and `RunOwnedAsync`) from whichever host runs the protocol; `PublicationMutationCount()` already enumerates them. `DelegationWorktreeService` is also an `ILandingGit` user, so dispatch-warning work is recorded the same way. | The progress probe is a file count; no new instrumentation (D-4). |
| Native rows have a per-test timeout. | No `[Timeout]` in `tests/Antiphon.E2E`; TUnit imposes none by default. | The 600 s cap in D-4 is the only absolute bound on a progress-renewed wait. |
| `WaitForBoundaryAsync` exists. | `LandDeliveryFixture.Dispatch.cs:85-86`, static-default 60 s; V25/V26/V30 inline the same `File.Exists(...barrier.json)` lambda through the static `UntilAsync`. | S2 routes those three through the instance helper. |

## Decisions

### D-1. Fire `queue-existing-key` from the pre-link block; keep the pre-link

`AgentTaskLandNotificationService.ReconcileAsync`, inside `if (existing is not null)` after
`await db.SaveChangesAsync(ct)`:

```csharp
if (boundary is not null)
    await boundary.ReachedAsync("queue-existing-key", note.TaskId, existing.Id, ct);
```

Same name, same identity argument (`existing.Id`) as the `EnqueueAsync` site, so the
vocabulary keeps one meaning: *an existing keyed queue row was reused instead of inserting*.
Fired after the save so an observer records a durable link, and so a throwing observer lands in
the existing catch with `QueueMessageId` set (state `AwaitingReceipt`, not `RetryPending`).
Production registers the no-op `LandDeliveryBoundary` (`Program.cs:343`), so this is a no-op
outside tests. Comment on the line: cites CARD-0481 F3 and CARD-0550.

Rejected:

- **Route pre-linked rows through `EnqueueAsync`.** Placed after the destination guard it
  re-introduces F3 (a Stopped/Failed caller's delivered row parks as `DestinationUnavailable`);
  placed before it, the call is a no-op whose only purpose is test evidence, and it would bump
  `EnqueueAttempts`, which V-19 pins at 0.
- **Test-only fix: assert the DB fingerprint (`EnqueueAttempts == 0`, `EnqueuedAt == row.CreatedAt`).**
  Noisy in a native run (catch-block increments, see ground truth) and it drops the property
  CARD-0467 wanted from V26: that a *production* seam performed the reuse
  ("rather than a test-only shortcut around that guard", `2026-09-09-card-0467-…-plan.md:523-525`).
- **A new boundary name (`outbox-prelinked`).** Needs `FileBoundary`'s observation list and the
  test edited for no semantic gain.

### D-2. V26's body is unchanged

No new assertion in `C467_V26_HardCrashAfterQueueInsertReusesRow`; its aliases
`C488_ApprovalDeliveryCrashMatrix` and `C488_ApprovalQueueInsertCrashReusesRow` follow. The only
V26 edit is S2's wait routing (D-4). Reason: the behavioural assertions (`QueueMessageId` null at
the cut, one keyed row with `DeliveryAttempts == 0`, `received.QueueMessageId == queueId`,
remote containment, one prompt) already pin the reuse contract; D-1 restores the evidence line.

### D-3. PC-13 gets a unit-level killer; the pre-link call gets its own control

New case in `tests/Antiphon.Tests/Application/AgentTaskLandNotificationRecoveryTests.cs` beside
V09, Integration lane: enqueue the same `sourceLandNotificationId` twice on one session with
equal destination and digest through a `BridgeQueueHarness` whose `LandDeliveryBoundary`
records names and identities; assert the second call returns the first row's id, exactly one
row carries the key, `queue-existing-key` was reached exactly once with `identity == row.Id`,
`queue-key-absent` exactly once, and no adapter input. PC-13's mutant (conflict throw in the
match arm) goes red there. The pre-link call gets a control whose mutant removes the D-1 line:
V26 red at `:132`, which is today's tip failure reproduced on purpose. Names are TestDesign's;
candidates in "Acceptance cases".

### D-4. Progress-aware `UntilAsync`; applied to protocol-bound waits only

`LandDeliveryFixture.UntilAsync` keeps its signature and gains two optional parameters:

```csharp
public static async Task UntilAsync(Func<Task<bool>> predicate, string evidence,
    int seconds = 60, Func<long>? progress = null, int capSeconds = 600)
```

Semantics: the deadline is `now + seconds`; each poll (100 ms, unchanged) re-reads `progress`
when supplied and, if the value changed, resets the deadline to `now + seconds`, never past
`start + capSeconds`. The `TimeoutException` message appends `quiet <seconds>s after <elapsed>s;
progress <last value>` so the next timeout classifies itself (stalled vs. slow) without a
bisect. Without `progress` the behaviour is byte-for-byte today's.

Instance members on `LandDeliveryFixture`:

- `public long ProtocolProgress()` = count of `protocol-git-*.json` in `Root` (same pattern
  `PublicationMutationCount()` already uses; enumerate, do not parse).
- `public Task UntilProtocolAsync(Func<Task<bool>> predicate, string evidence, int quietSeconds = 60)`
  = the static wait with `progress: ProtocolProgress`.
- `WaitForBoundaryAsync(boundary)` (existing, Dispatch partial) becomes
  `UntilProtocolAsync(() => File.Exists(...), boundary)`.

Call sites that switch to the protocol-aware wait (predicate can only become true after land or
dispatch git work has finished):

| File:line | Wait | Why protocol-bound |
|---|---|---|
| `LandDeliveryFixture.cs:206` | `ReceiptAsync` "complete native Land receipt" | V22/V24/V27/V29/C498 call it straight after `ReleaseExecutionAsync`; the land runs inside this wait. |
| `AgentTaskLandDeliveryE2ETests.cs:101` | V25 `terminal-committed.barrier.json` | reached after the protocol's terminal commit |
| `:121` | V26 `queue-inserted.barrier.json` | the card's timeout |
| `:165` | V30 `receipt-before-save` / `queue-before-verdict` barrier | after protocol and delivery |
| `:36` | V23 "busy caller has queued outcome" | after protocol |
| `:222` | V31 "two persisted enqueue failures" | after protocol |
| `:240` | V32 "unreceived outcome held at actual queue" | after protocol |
| `:319` | C498 "busy outcome queued" | after protocol |
| `LandDeliveryFixture.Dispatch.cs:72` | `WaitForDispatchIntentsAsync` | dispatch-base computation runs `ILandingGit` |
| `:85` | `WaitForBoundaryAsync` | all C540 crash cuts |
| `DispatchBaseWarningDeliveryE2ETests.cs:90` | inline `queue` cut wait in `CrashAsync` | same as above, keeps its key-retention assertion |

Unchanged (not protocol-bound or already sized): `native caller ready` 120, `owned child
startup` 120, `AssertOnePromptAsync` / `TwoNotificationScansAsync` (two 1 s scans), V24 "both
hold age receipts" (`:72`, monitor clock), V32 "outcome warning and error obligations" (`:247`,
monitor clock), C540 "two owned enqueue failures" (`DispatchBaseWarningDeliveryE2ETests.cs:61`,
follows the already-aware intents wait; the failures are scan-driven),
`WaitForLateConfirmedInterruptedRowsAsync` 150 (sweep period; its comment forbids
extending `ReceiptAsync`'s deadline as a substitute, and this plan does not), `RequestAsync` /
`StatusAsync` 60 s process waits (`delegate.ps1` returns at the 202, not after the land).

Rejected:

- **Flat raise of the default to 240-300 s.** Every genuine failure in twenty waits reports
  4-5× later (a broken build across ~40 native rows costs hours), and it is still a guess:
  the loaded run needed 140 s, the next machine may need more.
- **Latency-scaled deadline** (measure the fixture's own git latency during `InitializeAsync`,
  multiply by ~740). Encodes the protocol's current call count and a start-of-test latency
  that does not track load during the test.
- **Cut the git calls.** Production landing-protocol change with its own risk profile; D-6.

### D-5. The pre-link does not wake the flusher

Unchanged. The 1 s completion scan is the documented backstop (V28 proves it for the
lost-flush cut), V26's busy caller flushes on `TurnEnd` regardless, and adding a wakeup to the
CARD-0481 path is scope this card has no defect for. Recorded so the next reader does not
re-derive it.

### D-6. Out of this card

- Reducing the ~740 git calls per land (composition in ground truth). File as a follow-up
  card if the nightly native lane shows the cap being approached; the D-4 timeout message
  now carries the count needed to judge that.
- The card's item 3 (per-card V/R scoping never selects `[Category("OptIn")]` native rows).
  Policy; CARD-0544's nightly sweep and CARD-0545 own it. This card's own verification
  profile includes the native class (D-7).
- `AgentTaskDispatcher.cs:1344` never-started note path: untouched; its keyed branch is
  covered by D-3's unit case.

### D-7. Verification profile (coverage-to-class)

Unit lane plus named classes, then the two native classes; no full-assembly run.

- Unit: `--treenode-filter "/*/*/*/*[Category=Unit]"` (expected ≈2,514 with the three
  pre-existing `HerdrPaneDisposalEndpointTests` lane-xor reds and one symlink skip; CARD-0547's
  count from `aa02c398`).
- Integration, one invocation each: `AgentTaskLandNotificationRecoveryTests` (V08, V09, C481,
  new D-3 case), `ReceiptFailureDeliveryTests` (21, V-19 both variants),
  `AgentTaskReplyC527RecoveryTests` (queue-inserted rows exercise the pre-link with a boundary
  instance), `PostLandMutationDeliveryTests`, `VerificationRoundDeliveryTests`,
  `AgentTaskLandReceiptTests`.
- New wait-semantics case for D-4 (E2E project, no `OptIn` category so the default selection
  runs it; TestDesign may place it in `Antiphon.Tests` as Unit instead).
- Native, `Antiphon.E2E` built to `bin-c550/`, `client/dist` present
  (`UsePrebuiltFrontend = true`; the fixture refuses without `client/dist/index.html`), rows
  run sequentially (`[NotInParallel("C467LandDelivery")]`), chunked under the 10-minute
  foreground window and never co-scheduled with `Antiphon.Agents.Pty.Tests`:
  1. `C467_V26_HardCrashAfterQueueInsertReusesRow`, `C488_ApprovalDeliveryCrashMatrix`,
     `C488_ApprovalQueueInsertCrashReusesRow` (the fix; ~2-3 min each);
  2. the rest of `AgentTaskLandDeliveryE2ETests` (28 more rows; V29 ×6, V30 ×2, V32 ×2,
     C488 ×9, C498 ×2), three to four rows per invocation;
  3. `DispatchBaseWarningDeliveryE2ETests` (10 rows) because both fixture partials change.
  Report per row: pass/fail, wall time, and the git count from the D-4 message when a wait
  renewed (the first evidence of how often renewal is needed on this machine).

Expected native wall: about 31 + 10 rows × 2-4 min ≈ 1.5-2.5 h sequential; a row that renews
past 60 s is reported, not treated as a failure.

## Components (exact)

| Component | Change |
|---|---|
| `server/Application/Services/AgentTaskLandNotificationService.cs` | D-1: one guarded `boundary.ReachedAsync("queue-existing-key", note.TaskId, existing.Id, ct)` after the pre-link save, with comment. |
| `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs` | D-4: `UntilAsync` optional `progress`/`capSeconds`; `ProtocolProgress()`; `UntilProtocolAsync(...)`; `ReceiptAsync` uses it. |
| `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Dispatch.cs` | D-4: `WaitForDispatchIntentsAsync`, `WaitForBoundaryAsync` use `UntilProtocolAsync`. |
| `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs` | D-4 routing at the eight lines in the D-4 table; no assertion changes. |
| `tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs` | D-4 routing at `:90`. |
| `tests/Antiphon.Tests/Application/AgentTaskLandNotificationRecoveryTests.cs` | D-3 unit case. |
| new `tests/Antiphon.E2E/LandDeliveryWaitTests.cs` (or TestDesign's placement) | D-4 wait semantics. |
| `docs/investigations/2026-09-16-card-0540-code-verification.md:44-47` | S3 correction. |
| `docs/testing-and-build.md` | S3: one paragraph under the E2E guidance on protocol-aware waits and how to read the timeout message. |

No migration, no settings, no client change. Server restart after landing is routine; the
production path is a no-op boundary call.

## Slices

### S1. Evidence channel (production, one line) — lands V26

Files: `AgentTaskLandNotificationService.cs`; D-3 unit case in
`AgentTaskLandNotificationRecoveryTests.cs`.
Tests: D-3 case red-then-green is not required (it is new coverage, not a fix), but run it
mutated once as PC-13's control during Mutation; V26 and its two aliases green at the
committed SHA; `ReceiptFailureDeliveryTests` 21/21; `AgentTaskLandNotificationRecoveryTests`
full class; `AgentTaskReplyC527RecoveryTests` full class.
Commit S1 before the first native run.

### S2. Progress-aware waits (test infrastructure)

Files: both `LandDeliveryFixture` partials, both native test files, the new wait test.
Tests: the wait-semantics case; native chunks 2 and 3 of D-7. Commit S2 before starting
chunk 2.

Guard for the Code task: do not change `seconds` defaults, do not touch
`WaitForInterruptedDispatchEligibilityAsync` / `WaitForLateConfirmedInterruptedRowsAsync`,
and do not add `progress` to scan-count waits; a scan-count wait that needs renewal is a
finding, not a tuning target.

### S3. Documentation

- `2026-09-16-card-0540-code-verification.md` R-5 paragraph: five rows timed out in that
  loaded run; on a quiet machine only V26 fails, at the line-132 evidence assertion, cause
  `c3dc6f4c`; link this plan and the investigation.
- `docs/testing-and-build.md`: protocol-aware waits (quiet 60 s renewed by `protocol-git-*`
  growth, cap 600 s), where the count appears in a timeout message, and that a stalled land
  still fails at 60 s.

## Acceptance cases and guard candidates for TestDesign

| ID | Kind | Candidate | Proves |
|---|---|---|---|
| V-1 | native E | `C467_V26_HardCrashAfterQueueInsertReusesRow` + two C488 aliases, unchanged body | after the queue-insert kill, the recovery child reuses the exact row, writes `queue-existing-key-*.observation.json` with `identity == queueId` and `pid == recovery child`, one prompt |
| V-2 | I | `AgentTaskLandNotificationRecoveryTests.C550_Keyed_enqueue_returns_the_existing_row_and_records_the_boundary` | D-3 |
| V-3 | I | `ReceiptFailureDeliveryTests.Delivered_caller_failure_is_confirmed_after_caller_stops_and_services_restart` (Stopped, Failed) stays green | F3 ordering intact; `EnqueueAttempts == 0` |
| V-4 | I | `AgentTaskLandNotificationRecoveryTests.C481_Recovery_rejects_a_keyed_row_with_a_different_*` (6 rows) stay green | the boundary fires only after a validated link |
| V-5 | unit-style | `LandDeliveryWaitTests`: (a) predicate never true, progress constant → throws at ≈`seconds`; (b) progress increments each poll → throws only at ≈`capSeconds`; (c) progress increments for a while then stops → throws ≈`seconds` after the last change; (d) message carries elapsed and last progress value; small `seconds`/`capSeconds` (1-3 s) | D-4 semantics |
| V-6 | native E | full `AgentTaskLandDeliveryE2ETests` and `DispatchBaseWarningDeliveryE2ETests` | no wait regressed; renewal counts reported |

Positive controls (Mutation, method-scoped):

| PC | Mutant | Red in |
|---|---|---|
| PC-1 | remove the D-1 `ReachedAsync` line | V-1 at `:132` (today's tip failure) |
| PC-2 (PC-13 successor) | `EnqueueAsync` match arm (`SessionMessageQueueService.cs:356-364`): throw `ConflictException` instead of returning `existing.Id` | V-2 |
| PC-3 | `EnqueueAsync`: skip the `queue-existing-key` `ReachedAsync` | V-2 (boundary count 0) |
| PC-4 | `UntilAsync`: never reset the deadline on progress | V-5 (b), (c) |
| PC-5 | `UntilAsync`: ignore `capSeconds` | V-5 (b) |
| PC-6 (= CARD-0481 PC-26) | move the pre-link below the destination guard | V-3 (`DestinationUnavailable`) |

## Carried-forward controls

CARD-0467 PC-7, PC-10, PC-11, PC-15, PC-16, PC-17, PC-23 keep their native killers unchanged;
PC-13 is retargeted to V-2 by this plan. CARD-0481 PC-26 is unchanged.

## Scope boundaries

In: D-1 line, D-3 case, D-4 fixture/test routing, S3 docs. Out: git-call reduction, OptIn
scoping policy, pre-link flush wakeup, any change to `seconds` defaults or to the
`InterruptedAttemptAge` waits, `AgentTaskDispatcher` never-started path.

## Cost and next stage

Code: S1 ≈ 30 min, S2 ≈ 1.5 h, S3 ≈ 20 min; verification ≈ 2-2.5 h of sequential native
rows plus ≈ 10 min Unit/Integration. Estimate band for the Code dispatch: 4-5 h wall, most of
it native runs that cannot be parallelised. Restart: server (routine, no behaviour change).

Next: test-design, to ratify V-1..V-6 and PC-1..PC-6, settle the placement of V-5 and the
name of V-2, and fix the native chunking.

## Stated defaults (decide only if the operator objects)

- D-1 over the test-only fingerprint fix.
- Cap 600 s and quiet 60 s in D-4.
- No pre-link flush wakeup (D-5).
- No follow-up card filed for the git-call count by this plan; the timeout message carries
  the evidence for whoever files it.
