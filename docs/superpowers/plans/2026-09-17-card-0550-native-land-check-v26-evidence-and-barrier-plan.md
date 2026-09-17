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

## Verification design

TestDesign task `05522ab3`, 2026-09-17, on the plan commit `dde854b5`. Ratifies V-1..V-6 and
PC-1..PC-6 of "Acceptance cases and guard candidates", renumbers the controls into a complete
guard inventory (fifteen guards, fifteen controls, one-to-one), settles V-5's placement (Unit lane
of `Antiphon.Tests`, driven by a fake clock, no wall time) and settles native chunking from
measured per-row durations plus a runner probe of the filter grammar. The fix design above is
unchanged; where D-7 said "three to four rows per invocation" and "TestDesign may place it in
`Antiphon.Tests`", this section is the settled reading.

### Inspection

Bodies read in full: `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs` (377 lines),
`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Dispatch.cs` (193),
`tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs` (178; `FileBoundary`, `EvidenceGit`,
`LandClock`), `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs` (343; all 31 rows),
`tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs` (167; all 10 rows),
`tests/Antiphon.Tests/Application/AgentTaskLandNotificationRecoveryTests.cs` (299; V08, V09,
V10, V14, C481 ×6, `FailedInsert`), `ReceiptFailureDeliveryTests.cs:180-300` (V-19 body and its
`:263` / `:270` assertions), `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs:1-150`
(`ConfigureServices` runs against the same `ServiceCollection` the queue's scope factory is built
from), `LandQueueRaceWorker.cs` (the `Rendezvous` boundary registered exactly that way),
`AgentTaskLandReceiptTests.SeedAsync`, `server/Application/Services/LandDeliveryBoundary.cs`,
`AgentTaskLandNotificationService.cs` (286; `ReconcileAsync` pre-link block `:52-68`, catch
`:225-238`), `SessionMessageQueueService.cs:175-200` and `:330-475` (keyed match arm `:352-367`,
23505 catch `:447-457`), `tests/Shared/TestClassificationMetadata.cs` and
`TestClassificationGuardTests.cs` (lane-xor is enforced for `Antiphon.Tests` only; `Antiphon.E2E`
has no Slow classes and every heavy class carries `OptIn`), `tests/test-execution-policy.json`
(E2E is a manual suite; the nightly never selects it), `C544DeliveryRig.C544Boundary`,
`AgentTaskReplyC527RecoveryTests.C527FailureBoundary`, `PostLandMutationDeliveryTests` boundary
subclasses (all name-targeted; none throws on an unknown name except `FailedInsert`).

Runner facts established today on `Antiphon.Tests` built to `bin-td550/` (TUnit 1.44.0, MTP 2.2.2;
outputs deleted afterwards):

| Filter form (leaf segment) | Result |
|---|---|
| `C448_V31_VerificationEvidenceMustDescribeTheExactCommit` (exact method, 13 `[Arguments]` rows) | 13 ran |
| `C448_V31*` | 14 ran (every row of every method with that prefix) |
| `C448_V31_VerificationEvidenceMustDescribeTheExactCommit*base-changed*` (argument substring) | **zero** |
| `(C448_V31*base-changed*)\|(C448_V31*unknown-skip*)` | **zero** |
| `(C448_V31_VerificationEvidenceMustDescribeTheExactCommit)\|(C488_UnknownVersionRefuses)` (exact names in groups) | **zero** |
| `(C448_V31*)\|(C488_Unknown*)` and `C448_V31*\|C488_Unknown*` | 15 ran |
| `--list-tests --treenode-filter …` | lists the whole assembly; not selection evidence (matches `docs/testing-and-build.md`) |

Consequences: the leaf matches the method name, never the display name with arguments, so a
single `[Arguments]` row cannot be addressed; a multi-method invocation needs
`(Method1*)|(Method2*)` with a trailing `*` on every alternative. Every method name in the two
native classes is a unique prefix, so `Name*` selects exactly that method.

Prior-run durations (TRX under `C:\Antiphon\.antiphon\code-4435b705\`,
`…\review-4cba9241\`, `…\investigate-fb65210d\`): C540 rows 0:58-1:41 each, `PreTyping` 2:22-2:32,
`PostPrompt` 1:08-2:46 (5-row chunk 5:54, 4-row chunk 6:06); V22 1:33-1:38 (to its 60 s
timeout), V23 2:48 pass, V26 1:58 pass / 2:50 timeout / 4:22 pass with a 240 s wait under load,
V30 rows 1:25-1:41 (to timeout). No TRX exists for V24, V25, V27, V28, V29, V31, V32, C498;
those are estimated from their shapes below.

Boundaries → cases:

- Pre-link observer throws vs returns → V-7 rows `true` / `false`; identity matches (V-7) vs
  destination or digest mismatch (V-4, six rows); caller Running with a Pending row (V-7) vs
  Stopped/Failed with a Delivered row (V-3); Confirmed/NotRequired early return (existing
  `:33`, unchanged; excluded, no new behaviour).
- Keyed enqueue: key absent (first insert) then present with equal identity (V-2); present with
  a different session (V09 `:155`, existing); 23505 race (V09 `RunPairAsync`, existing); a
  different digest on the same session is an existing untested arm and out of this card (D-6
  reasoning: no plan line touches it).
- Wait: progress constant (V-5.1), grows every poll to the cap (V-5.2), grows then stops
  (V-5.3), grows and the predicate succeeds after the plain deadline (V-5.4), no probe at all
  (V-5.5), predicate true before any deadline (V-5.6). `seconds > capSeconds` and
  `seconds == capSeconds` are excluded: the plan fixes 60/600 and no call site passes a cap.
- Routing: every literal evidence string in the four E2E files is pinned to plain or
  protocol-aware (V-8); the three inline barrier lambdas must be gone (V-8).
- Probe: zero, one committed record, a `.tmp` partial and a foreign `attention-*.json` (V-9).

Known limits of the unchanged V26 body (D-2): `:132` asserts only that a
`queue-existing-key-*.observation.json` exists; identity and pid are not asserted there. V-7
pins identity; the pid check (observation `pid` equals the recovery child's `child-*.json` pid)
is a reported item in the D-7 native report, not an assertion.

Missing setup recorded: native rows boot a Testcontainers Postgres
(`AntiphonAppFixture` `PostgreSqlBuilder`), so Docker Desktop must be running; the fixture
refuses without `client/dist/index.html` newer than every `client/src` file and without the
staged `fakegrok/fakegrok.exe` (build target `CopyLandFakeGrok`); `Antiphon.Tests` Integration
rows need the shared dev Postgres (`TestDbFixture.CreateIsolatedSchemaAsync`); V-8 reads
`tests/Antiphon.E2E/**` source from the checkout found by walking up to `Antiphon.sln`.

### Delivery inventory

No delivery path is added or rerouted by this card. D-1 adds an observation after a link that
already happens; D-4 changes only how long a test waits. The path V-1 and V-6 exercise, with
the durable identity that connects every hop:

| Hop | Producer | Destination | Persistence boundary | Recovery | Observable receipt |
|---|---|---|---|---|---|
| Outcome → outbox | land child, `AgentTaskLandingProtocol` terminal commit | `AgentTaskLandNotifications` row (`Id` = the key) | same transaction as the terminal event (`terminal-committed` boundary) | boot/1 s scan `AgentTaskLandNotificationHostedService` → `ReconcileAsync` | row exists with `SourceEventId` |
| Outbox → queue | `ReconcileAsync` enqueue branch | `SessionQueuedMessages` row with `SourceLandNotificationId == note.Id` (unique index) | queue insert commits before the outbox link (`queue-inserted` boundary) | pre-link block links the existing keyed row (`c3dc6f4c`); D-1 observes it | `note.QueueMessageId == row.Id`, `EnqueuedAt == row.CreatedAt` |
| Queue → terminal | `SessionMessageQueueService` turn-end flush / completion scan / stranded sweep | caller pty (native FakeGrok) | `Sent` + `LastDeliveryBaselineSequence` | interrupted-attempt eligibility + transcript late-confirm (C540 rows) | `updates.jsonl` `user_message_chunk` |
| Terminal → receipt | transcript catch-up in `ReconcileAsync` receipt branch | `ConfirmedAt`, `ConfirmingPromptSequence` on the note | `receipt-before-save` boundary | next scan re-reads the transcript | `TranscriptEntries` UserPrompt at that sequence, complete and confirmed by `PromptSubmissionMatch` |

Producer-to-recipient tests through the real queue and delivery path, all in V-1/V-6:

- Busy recipient: V-1 (V26, busy caller, crash at queue insert, recovery reuses the row, one
  prompt), V23, C498(busy), C540 busy/queue rows.
- Already eligible recipient: V22, V28, V29 ×6, C540 idle rows.
- Crash / enqueue-failure recovery at each handoff: V25 (terminal), V26 (queue), V30 ×2
  (receipt, verdict), V31 (two enqueue failures), V27 (lost request wakeup), V28 (lost flush
  wakeup), C540 claim/projection/pre-enqueue/queue/attempt/verdict/receipt cuts.

Delivery acceptance in every native row is `ReceiptAsync`: `ConfirmedAt != null`, the
`SessionQueuedMessages` row named by `QueueMessageId`, and the UserPrompt transcript entry at
`ConfirmingPromptSequence` whose text is confirmed by and complete for the body, plus
`AssertOnePromptAsync` (exactly one such prompt in the DB and one `user_message_chunk` in the
native `updates.jsonl`). A 202, a queue insert, a terminal event, a `Sent` flag or a runner
acknowledgement alone never satisfies a row.

Substitutes and what they cannot prove: V-2 and V-7 run on `BridgeQueueHarness` with
`FakeAgentProtocolAdapter` and `deliverIfIdle: false`; they prove queue-row identity, the
outbox link and the observation, not typing or receipt (`Adapter.Inputs.ShouldBeEmpty()` is
asserted). V-3 (V-19) confirms against a synthetic transcript entry; it proves the confirm
logic and the F3 ordering, not the pty. V-5 proves the wait algorithm against a fake clock,
not that any land finishes. V-8 proves routing by source text, not by running a land. V-9
proves the probe counts files, not that the protocol writes them (V-6 rows do, through
`wait-*.json`, see fixture change 3).

### Proves it works now

| ID | Behaviour | Layer | Test / command | Expected |
|---|---|---|---|---|
| V-1 | After the queue-insert kill, the recovery child links the same keyed row and the pre-link observation is written | native E2E | `AgentTaskLandDeliveryE2ETests.C467_V26_HardCrashAfterQueueInsertReusesRow` and aliases `C488_ApprovalDeliveryCrashMatrix`, `C488_ApprovalQueueInsertCrashReusesRow` (bodies unchanged except S2 routing) | pass; `queue-existing-key-*.observation.json` present, `received.QueueMessageId == queueId`, one prompt |
| V-2 | Keyed `EnqueueAsync` match arm returns the existing row, one row per key, both boundaries recorded once | Integration | `AgentTaskLandNotificationRecoveryTests.C550_Keyed_enqueue_returns_the_existing_row_and_records_the_boundary` | pass |
| V-3 | F3 ordering intact; recovery links without a new enqueue | Integration | `ReceiptFailureDeliveryTests.Delivered_caller_failure_is_confirmed_after_caller_stops_and_services_restart` (Stopped, Failed) | pass; `:270` `EnqueueAttempts == 0` |
| V-4 | The observation fires only after a validated identity | Integration | `AgentTaskLandNotificationRecoveryTests.C481_Recovery_rejects_a_keyed_row_with_a_different_destination` / `…_digest` (6 rows) | pass; `:251` `enqueueBoundary.Calls == 0` |
| V-5 | D-4 wait semantics under a fake clock | Unit (`Antiphon.Tests`) | `ProgressAwareWaitTests` six methods (bodies below) | pass |
| V-6 | No native wait regressed; renewal recorded | native E2E | chunks N1a-N12 and D1-D3 (table under Cost) | 41/41 pass; per row wall, `wait-*.json` with `renewed = true` reported with `protocolGitAfter - protocolGitBefore` |
| V-7 | Pre-link observation: identity `(note.TaskId, row.Id)`, after the saved link, once per link, survives a throwing observer | Integration | `AgentTaskLandNotificationRecoveryTests.C550_Recovery_prelink_records_queue_existing_key_after_the_link_is_saved(bool observerThrows)` (2 rows) | pass |
| V-8 | The eleven protocol-bound waits use the progress-aware helper; scan and clock waits stay plain | Unit (`Antiphon.Tests`) | `LandDeliveryWaitRoutingTests.Protocol_bound_waits_renew_on_git_progress_and_scan_waits_do_not` | pass |
| V-9 | `ProtocolProgress()` counts committed `protocol-git-*.json` records only | E2E project, no fixture boot, no `OptIn` | `LandDeliveryFixtureProbeTests.ProtocolProgress_counts_committed_protocol_git_records_only` | pass |

### Exact bodies for Code

Production (D-1, verbatim from the plan), `AgentTaskLandNotificationService.ReconcileAsync`,
immediately after the `await db.SaveChangesAsync(ct);` at `:66`, inside `if (existing is not null)`:

```csharp
                    // CARD-0481 F3 links a producer-committed keyed row here without enqueueing; CARD-0550
                    // fires the reuse observation from this seam so V26 sees the same vocabulary as EnqueueAsync.
                    if (boundary is not null)
                        await boundary.ReachedAsync("queue-existing-key", note.TaskId, existing.Id, ct);
```

Fixture change 1, new shared helper `tests/Shared/ProgressAwareWait.cs`, linked into both
projects with `<Compile Include="..\Shared\ProgressAwareWait.cs" Link="TestSupport\ProgressAwareWait.cs" />`
(`Antiphon.E2E.csproj` and `Antiphon.Tests.csproj`, next to the existing
`TestClassificationMetadata.cs` link):

```csharp
using System.Globalization;

namespace Antiphon.TestSupport;

/// <summary>CARD-0550 D-4: a quiet deadline renewed by evidence of progress, under an absolute cap.</summary>
public static class ProgressAwareWait
{
    public const int PollMilliseconds = 100;

    /// <summary>
    /// Polls <paramref name="predicate"/> every <see cref="PollMilliseconds"/> until it is true. The deadline
    /// starts at <paramref name="seconds"/>; when <paramref name="progress"/> is supplied and its value has
    /// changed since the previous poll, the deadline becomes now + <paramref name="seconds"/>, never past
    /// start + <paramref name="capSeconds"/>. Without a probe the behaviour and the timeout message are
    /// exactly the plain wait's.
    /// </summary>
    public static async Task UntilAsync(Func<Task<bool>> predicate, string evidence, int seconds = 60,
        Func<long>? progress = null, int capSeconds = 600, TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        var start = clock.GetUtcNow();
        var cap = start.AddSeconds(capSeconds);
        var deadline = start.AddSeconds(seconds);
        var last = progress?.Invoke();
        while (clock.GetUtcNow() < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(TimeSpan.FromMilliseconds(PollMilliseconds), clock);
            if (progress is null) continue;
            var current = progress();
            if (current == last) continue;
            last = current;
            var renewed = clock.GetUtcNow().AddSeconds(seconds);
            deadline = renewed < cap ? renewed : cap;
        }
        if (progress is null) throw new TimeoutException(evidence);
        var elapsed = (clock.GetUtcNow() - start).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
        throw new TimeoutException($"{evidence} (quiet {seconds}s after {elapsed}s; progress {last})");
    }
}
```

Fixture change 2, `LandDeliveryFixture.cs`: replace the static `UntilAsync` (`:339-344`) with the
plan's signature delegating to the helper, and add the probe and the protocol-aware instance wait.
`ReceiptAsync` (`:206`) calls `UntilProtocolAsync(…, "complete native Land receipt")`.

```csharp
    public static Task UntilAsync(Func<Task<bool>> predicate, string evidence, int seconds = 60,
        Func<long>? progress = null, int capSeconds = 600)
        => ProgressAwareWait.UntilAsync(predicate, evidence, seconds, progress, capSeconds);

    /// <summary>Count of committed protocol git records under Root; the D-4 progress probe. Enumerate, never parse.</summary>
    public long ProtocolProgress() => Directory.GetFiles(Root, "protocol-git-*.json").LongLength;

    /// <summary>
    /// A wait whose predicate can only become true after land or dispatch git work. Quiet deadline renewed by
    /// protocol-git-* growth under the 600 s cap. Writes wait-*.json so a renewal that then succeeded is still
    /// visible in the evidence (the timeout message only exists when the wait failed).
    /// </summary>
    public async Task UntilProtocolAsync(Func<Task<bool>> predicate, string evidence, int quietSeconds = 60)
    {
        var started = DateTime.UtcNow; var before = ProtocolProgress(); var outcome = "satisfied";
        try { await UntilAsync(predicate, evidence, quietSeconds, ProtocolProgress); }
        catch (TimeoutException) { outcome = "timeout"; throw; }
        finally
        {
            var elapsed = (DateTime.UtcNow - started).TotalSeconds;
            await File.WriteAllTextAsync(Path.Combine(Root, $"wait-{Guid.NewGuid():N}.json"), JsonSerializer.Serialize(new {
                evidence, outcome, quietSeconds, elapsedSeconds = elapsed, protocolGitBefore = before,
                protocolGitAfter = ProtocolProgress(), renewed = elapsed > quietSeconds }));
        }
    }
```

Fixture change 3, `LandDeliveryFixture.Dispatch.cs`:

```csharp
        await UntilProtocolAsync(async () => (await DispatchIntentsAsync()).Count == 2, "two committed reduced dispatch intents");
    …
    public Task WaitForBoundaryAsync(string boundary) => UntilProtocolAsync(() =>
        Task.FromResult(File.Exists(Path.Combine(Root, boundary + ".barrier.json"))), boundary);
```

Test-file routing (assertions untouched):

| File:line | Replacement |
|---|---|
| `AgentTaskLandDeliveryE2ETests.cs:36` | `await busy.UntilProtocolAsync(async () => { … }, "busy caller has queued outcome");` |
| `:101` | `await f.WaitForBoundaryAsync("terminal-committed");` |
| `:121` | `await f.WaitForBoundaryAsync("queue-inserted");` |
| `:165` | `await f.WaitForBoundaryAsync(barrier);` |
| `:222` | `await f.UntilProtocolAsync(async () => { … }, "two persisted enqueue failures");` |
| `:240` | `await f.UntilProtocolAsync(async () => { … }, "unreceived outcome held at actual queue");` |
| `:319` | `await f.UntilProtocolAsync(async () => { … }, "busy outcome queued");` |
| `DispatchBaseWarningDeliveryE2ETests.cs:90` | `await f.UntilProtocolAsync(async () => { … }, boundary);` (key-retention assertion kept inside the lambda) |

Unchanged, by design: `:72` "both hold age receipts", `:247` "outcome warning and error
obligations", `DispatchBaseWarningDeliveryE2ETests.cs:61` "two owned enqueue failures",
`LandDeliveryFixture.cs:122` (120 s), `:233`, `:256` (120 s), `Dispatch.cs:91`, `:182` (150 s).

New Integration cases in `AgentTaskLandNotificationRecoveryTests` (class already
`[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`), placed after V09, plus one
private boundary:

```csharp
    [Test]
    public async Task C550_Keyed_enqueue_returns_the_existing_row_and_records_the_boundary()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var boundary = new RecordingBoundary();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString,
            ConfigureServices = services => services.AddSingleton<LandDeliveryBoundary>(boundary) });
        var task = Guid.NewGuid(); var notification = Guid.NewGuid();
        Guid first = Guid.Empty, second = Guid.Empty;
        await h.Queue.EnqueueAsync(h.SessionId, "immutable keyed body", MessageSendMode.WhenIdle, CancellationToken.None,
            QueuedMessageOrigin.Delegation, sourceTaskId: task, contentDigest: "digest", deliverIfIdle: false,
            sourceLandNotificationId: notification, onCreated: id => first = id);
        first.ShouldNotBe(Guid.Empty);
        // The match arm: same key, same session, same digest. It must hand back the existing row, not conflict or insert.
        await Should.NotThrowAsync(() => h.Queue.EnqueueAsync(h.SessionId, "immutable keyed body", MessageSendMode.WhenIdle, CancellationToken.None,
            QueuedMessageOrigin.Delegation, sourceTaskId: task, contentDigest: "digest", deliverIfIdle: false,
            sourceLandNotificationId: notification, onCreated: id => second = id));
        second.ShouldBe(first, "the keyed match arm returns the existing row's id");
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == notification)).ShouldBe(1);
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == task)).ShouldBe(1);
        boundary.Reached.Where(r => r.Boundary == "queue-key-absent").ShouldHaveSingleItem().Identity.ShouldBe(notification);
        var reused = boundary.Reached.Where(r => r.Boundary == "queue-existing-key").ShouldHaveSingleItem();
        reused.Identity.ShouldBe(first);
        reused.TaskId.ShouldBe(task);
        boundary.Reached.Count(r => r.Boundary == "queue-inserted").ShouldBe(0, "no afterLandQueueInsert callback was supplied");
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C550_Recovery_prelink_records_queue_existing_key_after_the_link_is_saved(bool observerThrows)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var caller = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        await using var db = new AppDbContext(options);
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, caller.SessionId);
        // CARD-0481 F3 shape: the producer committed the keyed row and died before the outbox link.
        await caller.Queue.EnqueueAsync(caller.SessionId, note.Body, MessageSendMode.WhenIdle, CancellationToken.None,
            QueuedMessageOrigin.Delegation, sourceTaskId: note.TaskId, contentDigest: note.ContentDigest,
            deliverIfIdle: false, sourceLandNotificationId: note.Id);
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceLandNotificationId == note.Id);
        (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id)).QueueMessageId.ShouldBeNull();

        var boundary = new RecordingBoundary();
        if (observerThrows) boundary.Throw.Add("queue-existing-key");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using (var recoveredDb = new AppDbContext(options))
            await new AgentTaskLandNotificationService(recoveredDb, caller.Queue, new CompletionNoteFlushQueue(), caller.Runtime, clock, boundary)
                .ReconcileAsync(note.Id, CancellationToken.None);

        var saved = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        saved.QueueMessageId.ShouldBe(row.Id, "the link is saved before the observer runs");
        saved.EnqueuedAt.ShouldBe(row.CreatedAt);
        saved.State.ShouldBe(LandNotificationState.AwaitingReceipt);
        saved.ConfirmedAt.ShouldBeNull();
        saved.EnqueueAttempts.ShouldBe(observerThrows ? 1 : 0);
        saved.LastErrorCode.ShouldBe(observerThrows ? "notification_reconcile_failed:IOException" : null);
        boundary.Reached.ShouldBe(new[] { ("queue-existing-key", note.TaskId, row.Id) }, "one reuse observation, no enqueue boundaries");
        (await db.SessionQueuedMessages.AsNoTracking().CountAsync(m => m.SourceLandNotificationId == note.Id || m.SourceTaskId == note.TaskId)).ShouldBe(1);
        caller.Adapter.Inputs.ShouldBeEmpty();

        // A later scan of the linked note neither repeats the observation nor enqueues.
        boundary.Throw.Clear();
        await using (var scannedDb = new AppDbContext(options))
            await new AgentTaskLandNotificationService(scannedDb, caller.Queue, new CompletionNoteFlushQueue(), caller.Runtime, clock, boundary)
                .ReconcileAsync(note.Id, CancellationToken.None);
        boundary.Reached.Count.ShouldBe(1);
        (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id)).EnqueueAttempts.ShouldBe(observerThrows ? 1 : 0);
        (await db.SessionQueuedMessages.AsNoTracking().CountAsync(m => m.SourceTaskId == note.TaskId)).ShouldBe(1);
        caller.Adapter.Inputs.ShouldBeEmpty();
    }

    private sealed class RecordingBoundary : LandDeliveryBoundary
    {
        public List<(string Boundary, Guid TaskId, Guid Identity)> Reached { get; } = [];
        public HashSet<string> Throw { get; } = [];
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            lock (Reached) Reached.Add((boundary, taskId, identity));
            return Throw.Contains(boundary) ? Task.FromException(new IOException("owned observer failure at " + boundary)) : Task.CompletedTask;
        }
    }
```

V-5, new `tests/Antiphon.Tests/ProgressAwareWaitTests.cs` (beside `ProcessSpawnLimitTests.cs`;
`Microsoft.Extensions.TimeProvider.Testing` 9.5.0 is already referenced). One poll interval per
driver step; the driver owns the progress value and advances only after the loop has armed its
next poll timer, so every count below is exact and no wall time passes:

```csharp
using Antiphon.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>CARD-0550 D-4: quiet deadline renewed by progress, bounded by the cap. Fake clock; no wall time.</summary>
[Category("Unit")]
public sealed class ProgressAwareWaitTests
{
    private const string Evidence = "owned evidence";

    /// <summary>A fake clock that also counts timer registrations: one per Task.Delay the wait loop arms.</summary>
    private sealed class CountingClock : TimeProvider
    {
        public readonly FakeTimeProvider Inner = new(new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero));
        public int Timers;
        public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow();
        public override long GetTimestamp() => Inner.GetTimestamp();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref Timers);
            return Inner.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed class Drive
    {
        public readonly CountingClock Clock = new();
        public int Polls;
        public long Progress;
        public int GrowUntilStep;
        public Func<bool> Satisfied = () => false;
        public Task Wait = Task.CompletedTask;

        public void Start(bool withProgress = true) =>
            Wait = ProgressAwareWait.UntilAsync(() =>
            {
                Interlocked.Increment(ref Polls);
                return Task.FromResult(Satisfied());
            }, Evidence, seconds: 60, progress: withProgress ? () => Volatile.Read(ref Progress) : null, capSeconds: 600, clock: Clock);

        /// <summary>
        /// One poll interval per step; progress grows by one per step while step &lt;= GrowUntilStep. After each
        /// advance the driver waits until the loop has armed its next poll timer or settled, so the clock never
        /// moves past an unarmed loop (the pooled continuation would otherwise miss a tick and hang the driver).
        /// </summary>
        public async Task<int> RunAsync(int maxSteps)
        {
            var steps = 0;
            while (!Wait.IsCompleted && steps < maxSteps)
            {
                var armed = Volatile.Read(ref Clock.Timers);
                steps++;
                if (steps <= GrowUntilStep) Volatile.Write(ref Progress, Progress + 1);
                Clock.Inner.Advance(TimeSpan.FromMilliseconds(ProgressAwareWait.PollMilliseconds));
                while (!Wait.IsCompleted && Volatile.Read(ref Clock.Timers) == armed) await Task.Yield();
            }
            return steps;
        }
    }

    [Test]
    public async Task Constant_progress_times_out_at_the_quiet_deadline()
    {
        var d = new Drive { GrowUntilStep = 0 };
        d.Start();
        (await d.RunAsync(10_000)).ShouldBe(600, "60 s of 100 ms polls with no progress");
        var error = await Should.ThrowAsync<TimeoutException>(() => d.Wait);
        error.Message.ShouldBe("owned evidence (quiet 60s after 60.0s; progress 0)");
    }

    [Test]
    public async Task Growing_progress_renews_the_quiet_deadline_until_the_cap()
    {
        var d = new Drive { GrowUntilStep = int.MaxValue };
        d.Start();
        var steps = await d.RunAsync(10_000);
        d.Wait.IsCompleted.ShouldBeTrue("the cap bounds renewal");
        steps.ShouldBe(6000, "renewed every poll, stopped by the 600 s cap");
        var error = await Should.ThrowAsync<TimeoutException>(() => d.Wait);
        error.Message.ShouldBe("owned evidence (quiet 60s after 600.0s; progress 6000)");
    }

    [Test]
    public async Task Progress_that_stops_times_out_one_quiet_period_after_the_last_change()
    {
        var d = new Drive { GrowUntilStep = 300 };
        d.Start();
        (await d.RunAsync(10_000)).ShouldBe(900, "last change at 30 s, quiet deadline 60 s later");
        var error = await Should.ThrowAsync<TimeoutException>(() => d.Wait);
        error.Message.ShouldBe("owned evidence (quiet 60s after 90.0s; progress 300)");
    }

    [Test]
    public async Task A_slow_but_progressing_predicate_is_allowed_to_finish_past_the_quiet_deadline()
    {
        var d = new Drive { GrowUntilStep = int.MaxValue };
        d.Satisfied = () => Volatile.Read(ref d.Polls) >= 700;
        d.Start();
        var steps = await d.RunAsync(10_000);
        d.Wait.IsCompletedSuccessfully.ShouldBeTrue("a land that keeps issuing git commands is allowed to finish after 60 s");
        steps.ShouldBe(699, "poll 700 follows the 699th advance");
    }

    [Test]
    public async Task Without_a_progress_probe_the_wait_is_the_plain_sixty_second_deadline()
    {
        var d = new Drive { GrowUntilStep = int.MaxValue };
        d.Start(withProgress: false);
        (await d.RunAsync(10_000)).ShouldBe(600);
        var error = await Should.ThrowAsync<TimeoutException>(() => d.Wait);
        error.Message.ShouldBe(Evidence, "no probe: the message is the evidence verbatim, as before CARD-0550");
    }

    [Test]
    public async Task A_satisfied_predicate_returns_before_any_deadline()
    {
        var d = new Drive { GrowUntilStep = 0 };
        d.Satisfied = () => Volatile.Read(ref d.Polls) >= 5;
        d.Start();
        (await d.RunAsync(10_000)).ShouldBe(4);
        d.Wait.IsCompletedSuccessfully.ShouldBeTrue();
    }
}
```

Timing note for Code: `FakeTimeProvider.Advance` fires the `Task.Delay(…, clock)` timer
synchronously; the loop's continuation either runs inline (the next timer is armed before
`Advance` returns) or on the pool (the driver's spin waits for the arming). `CountingClock` wraps
rather than subclasses `FakeTimeProvider`, so the design does not depend on `CreateTimer` being
overridable there. Do not replace the fake clock with wall time or the timer count with a poll
count: a poll-count spin can advance the clock between the predicate returning and the next
`Task.Delay` being armed, which leaves the loop waiting for a tick that never comes.

V-8, new `tests/Antiphon.Tests/LandDeliveryWaitRoutingTests.cs`:

```csharp
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>CARD-0550 D-4: which E2E waits renew on protocol git progress and which stay plain. Source-pinned.</summary>
[Category("Unit")]
public sealed class LandDeliveryWaitRoutingTests
{
    private static readonly string[] PlainLand = ["both hold age receipts", "outcome warning and error obligations"];
    private static readonly string[] ProtocolLand = ["busy caller has queued outcome", "two persisted enqueue failures",
        "unreceived outcome held at actual queue", "busy outcome queued"];
    private static readonly string[] PlainFixture = ["native caller ready", "owned child startup", "two additional completed notification scans"];
    private static readonly string[] ProtocolFixture = ["complete native Land receipt"];
    private static readonly string[] PlainDispatchFixture = ["two completed notification scans", "interrupted post-prompt queue rows recovered after eligibility"];
    private static readonly string[] ProtocolDispatchFixture = ["two committed reduced dispatch intents"];

    [Test]
    public void Protocol_bound_waits_renew_on_git_progress_and_scan_waits_do_not()
    {
        var root = RepositoryRoot();
        var land = File.ReadAllText(Path.Combine(root, "tests", "Antiphon.E2E", "AgentTaskLandDeliveryE2ETests.cs"));
        var dispatchTests = File.ReadAllText(Path.Combine(root, "tests", "Antiphon.E2E", "DispatchBaseWarningDeliveryE2ETests.cs"));
        var fixture = File.ReadAllText(Path.Combine(root, "tests", "Antiphon.E2E", "Fixtures", "LandDeliveryFixture.cs"));
        var dispatchFixture = File.ReadAllText(Path.Combine(root, "tests", "Antiphon.E2E", "Fixtures", "LandDeliveryFixture.Dispatch.cs"));
        foreach (var e in PlainLand) NearestWait(land, e).ShouldBe("UntilAsync(", e);
        foreach (var e in ProtocolLand) NearestWait(land, e).ShouldBe("UntilProtocolAsync(", e);
        foreach (var e in PlainFixture) NearestWait(fixture, e).ShouldBe("UntilAsync(", e);
        foreach (var e in ProtocolFixture) NearestWait(fixture, e).ShouldBe("UntilProtocolAsync(", e);
        foreach (var e in PlainDispatchFixture) NearestWait(dispatchFixture, e).ShouldBe("UntilAsync(", e);
        foreach (var e in ProtocolDispatchFixture) NearestWait(dispatchFixture, e).ShouldBe("UntilProtocolAsync(", e);
        // The three crash-cut barriers go through the instance helper; the inline lambdas are gone.
        land.ShouldContain("WaitForBoundaryAsync(\"terminal-committed\")");
        land.ShouldContain("WaitForBoundaryAsync(\"queue-inserted\")");
        land.ShouldContain("WaitForBoundaryAsync(barrier)");
        land.ShouldNotContain("terminal committed crash cut");
        land.ShouldNotContain("queue inserted crash cut");
        land.ShouldNotContain("native prompt persisted before receipt/verdict save");
        dispatchFixture.ShouldContain("public Task WaitForBoundaryAsync(string boundary) => UntilProtocolAsync(");
        // C540: the inline queue-cut wait renews and keeps its key-retention assertion; the enqueue-failure wait stays plain.
        NearestWait(dispatchTests, "every dispatch warning must retain its notification key before the insert barrier").ShouldBe("UntilProtocolAsync(");
        NearestWait(dispatchTests, "two owned enqueue failures").ShouldBe("UntilAsync(");
    }

    private static string NearestWait(string text, string evidence)
    {
        var at = text.IndexOf("\"" + evidence + "\"", StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, evidence);
        var plain = text.LastIndexOf("UntilAsync(", at, StringComparison.Ordinal);
        var protocol = text.LastIndexOf("UntilProtocolAsync(", at, StringComparison.Ordinal);
        return protocol > plain ? "UntilProtocolAsync(" : "UntilAsync(";
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Antiphon.sln not found above " + AppContext.BaseDirectory);
    }
}
```

(`"UntilProtocolAsync("` never contains `"UntilAsync("`, so the two `LastIndexOf` searches are
disjoint; the helper's own definitions sit at the bottom of `LandDeliveryFixture.cs`, after every
literal they could shadow.)

V-9, new `tests/Antiphon.E2E/LandDeliveryFixtureProbeTests.cs` (no `OptIn`; no app boot; the
fixture constructor only sets the Hangfire environment variable, restored on dispose):

```csharp
using Antiphon.E2E.Fixtures;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

[NotInParallel("C467LandDelivery")]
public class LandDeliveryFixtureProbeTests
{
    [Test]
    public async Task ProtocolProgress_counts_committed_protocol_git_records_only()
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        try
        {
            f.ProtocolProgress().ShouldBe(0);
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-git-{Guid.NewGuid():N}.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-git-{Guid.NewGuid():N}.json.tmp"), "{}");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"attention-{Guid.NewGuid():N}.json"), "{}");
            f.ProtocolProgress().ShouldBe(1, "a partial .tmp record and a foreign evidence file are not progress");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-git-{Guid.NewGuid():N}.json"), "{}");
            f.ProtocolProgress().ShouldBe(2);
        }
        finally { Directory.Delete(f.Root, recursive: true); }
    }
}
```

### Guards the regression

- R-1: the pre-link stops writing the reuse observation (today's tip) | V-7 `boundary.Reached.ShouldBe(new[] { ("queue-existing-key", note.TaskId, row.Id) })`; V-1 `AgentTaskLandDeliveryE2ETests.cs:132` `ShouldNotBeEmpty()`.
- R-2: the pre-link moves below the destination guard (F3 undone) | V-3 `ReceiptFailureDeliveryTests.cs:263` `confirmed.State.ShouldBe(Confirmed)`.
- R-3: the pre-link links or observes a mismatched row | V-4 `:225` `QueueMessageId.ShouldBeNull`, `:230` `LastErrorCode` ConflictException, `:251` `Calls == 0`.
- R-4: the keyed match arm conflicts or inserts a second row | V-2 `Should.NotThrowAsync`, `second.ShouldBe(first)`, key count `== 1`.
- R-5: renewal masks a stalled land | V-5 `Constant_progress_times_out_at_the_quiet_deadline` `steps == 600`.
- R-6: renewal becomes unbounded | V-5 `Growing_progress_renews_the_quiet_deadline_until_the_cap` `IsCompleted` after 10,000 steps and `steps == 6000`.
- R-7: a scan-count or clock wait gains renewal (the S2 guard) | V-8 plain relation for the eight plain literals.
- R-8: a protocol-bound wait falls back to the plain 60 s | V-8 protocol relation for the seven protocol literals and the three `WaitForBoundaryAsync` calls.
- R-9: the plain wait's behaviour or message drifts | V-5 `Without_a_progress_probe_…` `steps == 600` and `Message == "owned evidence"`.
- R-10: the probe stops tracking protocol records | V-9 `ProtocolProgress().ShouldBe(1)` / `(2)`.

### Guard inventory

Plan-to-inventory renumbering: plan PC-1 → PC-1; plan PC-2 → PC-5; plan PC-3 → PC-6; plan PC-4 → PC-8;
plan PC-5 → PC-9; plan PC-6 → PC-7. Guards 2, 3, 4, 10, 11, 12, 13, 14, 15 are new.

- G-1: D-1, the pre-link block fires `queue-existing-key` after linking | PC-1
- G-2: D-1 "after a validated link", no observation on the mismatch path | PC-2
- G-3: D-1 "fired after the save", the observer runs after the link is durable | PC-3
- G-4: D-1 identity arguments `(note.TaskId, existing.Id)` | PC-4
- G-5: D-3 / CARD-0467 PC-13 successor, keyed match arm returns the existing id, never conflicts or inserts | PC-5
- G-6: D-3, match arm records `queue-existing-key` with `identity == existing.Id` | PC-6
- G-7: D-1 rejected alternative / CARD-0481 F3 (PC-26), pre-link precedes the destination guard | PC-7
- G-8: D-4, progress growth renews the quiet deadline | PC-8
- G-9: D-4, renewal never passes `start + capSeconds` | PC-9
- G-10: D-4, a stalled wait (constant progress) still fails at `seconds` | PC-10
- G-11: D-4, without a probe the wait is byte-for-byte today's (deadline and message) | PC-11
- G-12: D-4, the timeout message carries elapsed seconds and the last progress value | PC-12
- G-13: D-4 table, the eleven protocol-bound waits use the progress-aware helper | PC-13
- G-14: D-4 "unchanged" list / S2 guard, scan-count and clock waits stay plain | PC-14
- G-15: D-4, `ProtocolProgress()` counts committed `protocol-git-*.json` records | PC-15

guards=15, mapped=15, missing=0, duplicate PC mappings=0.

### Positive controls

Every control is method-scoped (`--treenode-filter "/*/*/Class/Method*"`), red first, restore,
green. Rebuild between mutate and restore. Native controls need Docker Desktop and a current
`client/dist`.

- PC-1: break G-1 by deleting the D-1 `if (boundary is not null) await boundary.ReachedAsync("queue-existing-key", …)` line; expect `AgentTaskLandNotificationRecoveryTests.C550_Recovery_prelink_records_queue_existing_key_after_the_link_is_saved` (both rows) red at `boundary.Reached.ShouldBe(new[] { ("queue-existing-key", note.TaskId, row.Id) })` (actual: empty), and `AgentTaskLandDeliveryE2ETests.C467_V26_HardCrashAfterQueueInsertReusesRow` red at `:132` `ShouldNotBeEmpty()` (today's tip failure reproduced on purpose). Run the Integration red first; the native red is the card's symptom and is run once.
- PC-2: break G-2 by adding a second call `await boundary.ReachedAsync("queue-existing-key", note.TaskId, existing.Id, ct);` (guarded by `boundary is not null`) as the first statement inside the mismatch branch, before `throw new ConflictException(…)`, keeping the D-1 line in place; expect `C481_Recovery_rejects_a_keyed_row_with_a_different_destination(Running)` red at `:230` `saved.LastErrorCode.ShouldBe("notification_reconcile_failed:ConflictException")` (actual `…:IOException`, `FailedInsert` throws on every name).
- PC-3: break G-3 by moving the D-1 line above `await db.SaveChangesAsync(ct);` (`:66`); expect `C550_Recovery_prelink_records_queue_existing_key_after_the_link_is_saved(true)` red at `saved.QueueMessageId.ShouldBe(row.Id)` (actual null: the throw preceded the save and the catch cleared the tracker; state `RetryPending`). The `false` row stays green, which shows the control is specific to ordering.
- PC-4: break G-4 by passing `note.Id` instead of `existing.Id` as the identity; expect `C550_Recovery_prelink_records_queue_existing_key_after_the_link_is_saved` (both rows) red at the `boundary.Reached.ShouldBe(…)` tuple comparison. V-1 stays green (V26 never reads the identity), which is why V-7 exists.
- PC-5: break G-5 in `SessionMessageQueueService.EnqueueAsync` match arm (`:356-363`) by replacing `onCreated?.Invoke(existing.Id); … return await GetQueueAsync(sessionId, ct);` with `throw new ConflictException("mutant");`; expect `C550_Keyed_enqueue_returns_the_existing_row_and_records_the_boundary` red at `Should.NotThrowAsync` (ConflictException). `C467_V09_KeyedQueueRacesAndDistinctEvents` stays green (its pair resolves through the 23505 catch, not the match arm), which is why PC-13 needed a successor.
- PC-6: break G-6 by deleting the `queue-existing-key` `ReachedAsync` call in the match arm; expect `C550_Keyed_enqueue_returns_the_existing_row_and_records_the_boundary` red at `boundary.Reached.Where(r => r.Boundary == "queue-existing-key").ShouldHaveSingleItem()` (actual: none).
- PC-7: break G-7 by moving the whole `if (note.QueueMessageId is null) { var existing … }` pre-link block below the `destinationStatus is Stopped or Failed` guard; expect `ReceiptFailureDeliveryTests.Delivered_caller_failure_is_confirmed_after_caller_stops_and_services_restart(Stopped)` and `(Failed)` red at `:263` `confirmed.State.ShouldBe(LandNotificationState.Confirmed)` (actual `DestinationUnavailable`).
- PC-8: break G-8 in `ProgressAwareWait.UntilAsync` by deleting the deadline reset (`deadline = renewed < cap ? renewed : cap;`); expect `ProgressAwareWaitTests.Growing_progress_renews_the_quiet_deadline_until_the_cap` red at `steps.ShouldBe(6000)` (actual 600), `Progress_that_stops_…` red at `ShouldBe(900)` (actual 600), `A_slow_but_progressing_predicate_…` red at `IsCompletedSuccessfully` (faulted at 60 s).
- PC-9: break G-9 by `deadline = renewed;` (cap ignored); expect `Growing_progress_renews_the_quiet_deadline_until_the_cap` red at `d.Wait.IsCompleted.ShouldBeTrue()` after 10,000 driver steps (the wait is still renewing; it parks on a fake timer and is discarded).
- PC-10: break G-10 by initialising `deadline = progress is null ? start.AddSeconds(seconds) : cap;`; expect `Constant_progress_times_out_at_the_quiet_deadline` red at `ShouldBe(600)` (actual 6000) and `Progress_that_stops_…` red at `ShouldBe(900)` (actual 6000).
- PC-11: break G-11 by throwing the suffixed message unconditionally (delete the `if (progress is null) throw new TimeoutException(evidence);` line); expect `Without_a_progress_probe_the_wait_is_the_plain_sixty_second_deadline` red at `error.Message.ShouldBe(Evidence)`.
- PC-12: break G-12 by throwing `new TimeoutException(evidence)` on the progress path too (delete the suffix); expect `Constant_progress_times_out_at_the_quiet_deadline` red at `error.Message.ShouldBe("owned evidence (quiet 60s after 60.0s; progress 0)")`.
- PC-13: break G-13 by reverting `AgentTaskLandDeliveryE2ETests.cs` V26 to `await LandDeliveryFixture.UntilAsync(() => Task.FromResult(File.Exists(Path.Combine(f.Root, "queue-inserted.barrier.json"))), "queue inserted crash cut");`; expect `LandDeliveryWaitRoutingTests.Protocol_bound_waits_renew_on_git_progress_and_scan_waits_do_not` red at `land.ShouldContain("WaitForBoundaryAsync(\"queue-inserted\")")`. No rebuild of `Antiphon.E2E` is needed; the Unit test reads the source file.
- PC-14: break G-14 by routing V24's wait through `f.UntilProtocolAsync(…, "both hold age receipts")`; expect the same method red at `NearestWait(land, "both hold age receipts").ShouldBe("UntilAsync(")` (actual `UntilProtocolAsync(`).
- PC-15: break G-15 by `public long ProtocolProgress() => 0;`; expect `LandDeliveryFixtureProbeTests.ProtocolProgress_counts_committed_protocol_git_records_only` red at `f.ProtocolProgress().ShouldBe(1, …)`.

Batching under the different-file, different-method rule: {PC-1 Integration red, PC-5, PC-8, PC-13,
PC-15} may share one red/green cycle; {PC-6, PC-9, PC-14} may share one; PC-2, PC-3, PC-4 (same
block), PC-10, PC-11, PC-12 (same method as PC-8/PC-9) and PC-7 run alone. PC-1's native red is its
own cycle.

### Out of scope

- The `EnqueueAsync` different-digest-same-session arm and the never-started note path
  (`AgentTaskDispatcher.cs:1344`): untouched by the plan (D-6); no guard here.
- `queue-key-absent` semantics and the 23505 race: CARD-0467 V09 owns them; V-2 asserts the
  absent observation only as context for the reuse observation.
- Pid identity of the observation file and per-row renewal counts: reported from evidence
  (`queue-existing-key-*.observation.json`, `child-*.json`, `wait-*.json`), not asserted, so
  that V26's body stays unchanged (D-2).
- Wait-record file contents beyond `renewed`/`elapsedSeconds`: evidence for the operator, not a
  guard.
- Reducing the ~740 git calls, `OptIn` scoping policy, pre-link flush wakeup, `seconds`
  defaults, the `InterruptedAttemptAge` waits: plan D-5/D-6 and the S2 guard.
- Real-time V-5 variants (the plan's "1-3 s" suggestion): superseded by the fake clock; a
  wall-clock version would add ~10 s per run and be load-sensitive.
- Running V-5 or V-8 in `Antiphon.E2E`: the nightly never selects E2E
  (`tests/test-execution-policy.json`), so both live in the `Antiphon.Tests` Unit lane where they
  run every night and in every local Unit loop.

### Cost

Native chunking (settled). One `dotnet run --project tests/Antiphon.E2E --no-build
--property:OutputPath=bin-c550/ -- --treenode-filter "<filter>" --report-trx
--report-trx-filename <chunk>.trx --results-directory .antiphon/c550-code/native` per chunk;
rows serialise under `[NotInParallel("C467LandDelivery")]`; never co-scheduled with
`Antiphon.Agents.Pty.Tests` or with an `Antiphon.Tests` Integration run. Leaf filters below
prefix `/*/*/AgentTaskLandDeliveryE2ETests/` (N) or `/*/*/DispatchBaseWarningDeliveryE2ETests/` (D).

| Chunk | Leaf filter | Rows | Wall (m = measured from TRX, e = estimated) |
|---|---|---|---|
| N1a | `C467_V26_HardCrashAfterQueueInsertReusesRow*` | 1 | 2.0 m quiet; 4.4 m loaded with renewal |
| N1b | `(C488_ApprovalDeliveryCrashMatrix*)\|(C488_ApprovalQueueInsertCrashReusesRow*)` | 2 | 4.0 e |
| N2 | `(C467_V22_AlreadyIdleGetsOutcomeWithoutNewInput*)\|(C467_V28_LostFlushWakeupRecoversOnIdleCaller*)\|(C467_V31_EnqueueFailureRecoversAutomatically*)` | 3 | 5.5 e (V22 1.6 m) |
| N3 | `(C467_V23_BusyCallerDoesNotBlockAnotherLand*)\|(C467_V25_HardCrashAfterOutcomeCommitRecoversReceipt*)` | 2 | 5.3 (V23 2.8 m, V25 2.5 e) |
| N4 | `C467_V24_BlockedWriterThenReleaseDeliversBothNotes*` | 1 | 5.0 e (two child boots, two receipts) |
| N5 | `C467_V30_ReceiptSaveFailureNeverRetypes*` | 2 | 5.0 e (rows 1.4-1.7 m to their timeouts) |
| N6 | `(C467_V27_LostRequestWakeupRecoversAtBoot*)\|(C488_ApprovalLostFlushRecovers*)` | 2 | 4.1 e |
| N7 | `C467_V32_StatusPollingCannotDischargeUnreceivedOutcome*` | 2 | 6.0 e |
| N8 | `C467_V29_RealOutcomeProducerMatrix*` | 6 | 12-14 e; **background invocation** (single-row selection is impossible, see Inspection): start with `run_in_background`, check the log/TRX no more often than every 4 min, edit no source meanwhile, 20 min budget |
| N9 | `(C498_FailureOutcomeReachesCaller*)\|(C488_ApprovalEnqueueFailureRecovers*)` | 3 | 6.0 e |
| N10 | `(C488_ApprovalOutcomeReceiptMatrix*)\|(C488_ReviewToLandReceiptMatrix*)\|(C488_ApprovalBusyCallerDoesNotBlock*)` | 3 | 6.0 e |
| N11 | `(C488_ReviewDeliveryCrashMatrix*)\|(C488_ReviewEvidenceCrashRecovers*)` | 2 | 5.0 e |
| N12 | `(C488_ApprovalReceiptSaveFailureNeverRetypes*)\|(C488_ApprovalPollingCannotConfirm*)` | 2 | 5.5 e |
| D1 | `(C540_CollapsedWarningsReachIdleCaller*)\|(C540_CollapsedWarningsWaitForBusyCaller*)\|(C540_ClaimCrashRecoversCollapsedWarnings*)\|(C540_ProjectionCrashRecoversOriginalPairs*)\|(C540_PreEnqueueCrashRecoversWarnings*)` | 5 | 5.9 m |
| D2 | `(C540_EnqueueFailureRetriesWarnings*)\|(C540_QueueInsertCrashReusesRows*)\|(C540_PreTypingCrashRecoversWarnings*)\|(C540_ReceiptCrashDoesNotRetype*)` | 4 | 6.1 m |
| D3 | `C540_PostPromptCrashDoesNotRetype*` | 1 | 2.8 m |

Order: S1 committed → N1a (the fix) → Integration classes → S2 committed → N1b..N12, D1..D3.
Sum of rows ≈ 87 min plus 16 host starts ≈ 5 min: **≈ 92 min quiet, ≈ 140 min loaded** (estimated
band; only D1-D3, N1a, V22, V23, V30 have TRX support).

Ordinary V/R floor (Code):

| Item | Filter / command | Minutes |
|---|---|---|
| Build `Antiphon.Tests` | `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c550/` | 2.1 measured cold today (0.3 incremental) |
| Build `Antiphon.E2E` (stages FakeGrok, runner) | `dotnet build tests/Antiphon.E2E --property:OutputPath=bin-c550/` | 4 estimated |
| `client/dist` currency | `npm run build` in `client/` only if `client/src` is newer | 0-2 estimated |
| Unit lane (V-5, V-8, classification guard, everything else) | `--treenode-filter "/*/*/*/*[Category=Unit]"` | 4.3 measured 2026-09-17 (review notes, 2,467 cases) |
| `AgentTaskLandNotificationRecoveryTests` (V-2, V-4, V-7 + existing) | `/*/*/AgentTaskLandNotificationRecoveryTests/*` | 4 estimated |
| `ReceiptFailureDeliveryTests` (V-3) | `/*/*/ReceiptFailureDeliveryTests/*` | 5 estimated |
| `AgentTaskReplyC527RecoveryTests`, `PostLandMutationDeliveryTests`, `VerificationRoundDeliveryTests`, `AgentTaskLandReceiptTests` | one invocation each | 2 + 4 + 6 + 1 estimated |
| V-9 | `/*/*/LandDeliveryFixtureProbeTests/*` | 0.5 estimated |
| Native V-1 + V-6 | chunk table | 92 quiet / 140 loaded |
| **Ordinary total** | | **≈ 127 min quiet (≈ 2.1 h), ≈ 175 min loaded (≈ 2.9 h)** |

PC floor (Mutation), per cycle = mutate + rebuild + red + restore + rebuild + green:

| Control(s) | Red/green vehicle | Minutes |
|---|---|---|
| PC-1 | V-7 Integration (0.3 build + 0.5 run, ×2) + V-1 native (2.0 + 2.0 + two E2E rebuilds 2.5) | 8.1 |
| PC-2, PC-3, PC-4 (separate) | V-4 / V-7 Integration, 1.6 each | 4.8 |
| PC-5, PC-6 (separate) | V-2 Integration, 1.6 each | 3.2 |
| PC-7 | V-3 two rows, 0.3 build + 1.0 run, ×2 | 2.6 |
| PC-8 .. PC-12 (separate, same method) | V-5 Unit, 0.3 build + 0.3 run, ×2 each | 6.0 |
| PC-13, PC-14 | V-8 Unit, source edit only, 0.4 run ×2 each | 1.6 |
| PC-15 | V-9, E2E rebuild 1.2 + run 0.5, ×2 | 3.4 |
| One initial build of both test projects | | 6.0 |
| **PC total** | | **≈ 36 min unbatched (estimated); ≈ 30 min with the two batches above (saves ≈ 6 min)** |

Total verification floor = setup/build (≈ 6-8) + V/R (≈ 120) + every PC red/restore/green (≈ 30-36)
= **≈ 160 min (≈ 2.7 h) quiet, ≈ 210 min (≈ 3.5 h) loaded**, estimated except where marked measured.
Savings versus the plan's own profile: V-5 costs seconds instead of the four 1-3 s real-time cases
(negligible), V-7 gives PC-1 a 30 s red before the 2 min native one (saves nothing on the floor,
saves a native rerun on a wrong first attempt), and the V29 matrix runs once as one background
invocation instead of an impossible per-row split.

Handoff checklist: bodies read (all touched tests and fixtures, plus the nearest fixture for each new
file); guards=15, mapped=15, missing=0, duplicate PC mappings=0; every PC is a one-line compiling
mutation with a named red assertion; Cost is numeric with measured/estimated labels; no placeholders.
