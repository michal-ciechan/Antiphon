# CARD-1157 test design: the receipt scan follows the keyed row

TestDesign task `149633f2` for
`docs/superpowers/plans/2026-10-09-card-1157-receipt-scan-destination-plan.md` (commit
`61ca50a10bd2efc0cbf239e31c286f3c40c2ebdb` on `feat/card-task-24f528a8`; `origin/master`
`59d67869b6a60f13510827baf1aeb499b1b93280` at fetch, the plan commit's parent). Every source
citation is at `61ca50a10`. Production code was not edited. This file is the checkpoint `--plan`
input for Code: the tool reads the first `### Checkpoints` table below. The fix design D-1..D-8
stands; §2 records the corrections the measured tree forces and §5 appends the verification
design the bundle requires. Where this file and the plan's "Verification design" differ, this
file wins.

**Fail-closed rule every test pins.** A note is `Confirmed` only from a complete matching
receipt (`LandNoteReceipt.IsReceipt`: identity head window and whole-text completeness) found
by `FirstReceiptAsync` over `LandNoteReceipt.Prompts` on the session the keyed queue row actually
targets. A miss leaves the note's state exactly as it was. The keyed row's session is scanned
only when every positive condition of D-2 holds and that session's row exists; the keyed-row
session missing, or any non-whitelist shape, scans the parent exactly as today, including the
CARD-1121 parent proof. A one-kind note never changes target. The CARD-1121 proof is bound to
the session actually scanned (`Context.ScanSessionId`), so a proof for the parent can never
certify the destination and the reverse. A Running scanned session is never cached (CARD-1121
A-1 applied to the scanned session) and can confirm. Nothing stops, parks or fails a Working
session; `KillCalls` is 0 and every session status is unchanged after every pass. `Sent`,
`SentAt`, adapter submissions, an equal body and cache metrics are never receipt.

## 1. Measurement against the current tree

Three diagnostic rows ran on this Linux runner mirror at `61ca50a10` through
`scripts/run-checkpoint.ps1` (slot granted, waited 0 s each). The probe class
`C1157TestDesignProbeTests` was never committed (hence `dirty=1`), and the `bin-c1157-td/`
outputs were deleted. They are diagnostics of today's tree, not evidence for any V row.

- `CHECKPOINT TD-1 commit=61ca50a10bd2efc0cbf239e31c286f3c40c2ebdb build=ok filter=/*/*/AgentTaskLandReceiptWatermarkTests/(C1121_UnchangedTranscript*)|(C1121_ForeignDestination*) executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-149633f2/.antiphon/c1157-td/TD-1-20261009-035411-8e09/run.trx slot=granted waited=0s dirty=1 source=61ca50a10bd2efc0cbf239e31c286f3c40c2ebdb+dirty:297b180a89f709724e4ec2105eb1e92e17802d0caa37f0d2360d0906a7ab18ba sourceState=dirty buildSource=verified` (build plus run held the slot 329 s).
- `CHECKPOINT TD-2 commit=61ca50a10bd2efc0cbf239e31c286f3c40c2ebdb build=reused filter=/*/*/AgentTaskLandReceiptWatermarkSafetyTests/C1121_ReconcilePinsStatementAndRowBudgets* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-149633f2/.antiphon/c1157-td/TD-2-20261009-035951-71e9/run.trx slot=granted waited=0s dirty=1 source=61ca50a10bd2efc0cbf239e31c286f3c40c2ebdb+dirty:297b180a89f709724e4ec2105eb1e92e17802d0caa37f0d2360d0906a7ab18ba sourceState=dirty buildSource=verified` (73 s).
- `CHECKPOINT TD-3 commit=61ca50a10bd2efc0cbf239e31c286f3c40c2ebdb build=reused filter=/*/*/C1157TestDesignProbeTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-149633f2/.antiphon/c1157-td/TD-3-20261009-040107-045d/run.trx slot=granted waited=0s dirty=1 source=61ca50a10bd2efc0cbf239e31c286f3c40c2ebdb+dirty:297b180a89f709724e4ec2105eb1e92e17802d0caa37f0d2360d0906a7ab18ba sourceState=dirty buildSource=verified` (74 s).

### 1.1 The CARD-1121 budgets hold

| Measured today (warm store, runner NotFound unless stated) | Commands | Receipt selects / rows | Scanned | Row |
|---|---:|---|---|---|
| Quiet two-kind Outcome, parent scan | 6 / 5 / 5 | 1 / 0 / 0, 48 | `[A]` | TD-1 `two-kind-outcome-404`; TD-2 `one-kind-852`, `two-kind-1506`, `changed-transcript-two-kind-48` |
| Cold store (restart) | 7 / 5 / 5 | 1 / 0 / 0 | `[A]` | TD-1 `two-kind-outcome-cold-store`; TD-2 `two-kind-48` |
| Unprofiled TaskCompletion (its generation SELECT) | 7 / 6 / 6 | 1 / 0 / 0 | `[A]` | TD-1 `task-completion-unprofiled-404` |
| Runner holds one already-committed row (live pull) | 9 / 8 / 8 | 1 / 0 / 0 | `[A]` | TD-1 `two-kind-outcome-live-committed-row`; TD-2 `live-committed-row-9-8` |
| Runtime ingest of one new row, warm store | 4 | `StartedAt`, `session-state.identity`, `MAX(Sequence)`, one INSERT | | TD-3 `ingest-one-row-warm` |
| Today's foreign shape (row on B, complete body on B at 15, parent A Stopped) | 6 / 5 / 5 | 1 / 0 / 0, 48 | `[A]`; `AwaitingReceipt`; Proofs 1, Hits 2 | TD-3 `foreign-shape-today`; TD-1 `C1121_ForeignDestinationNoteStaysOpenExactlyAsToday` (3) |

The quiet pass-1 roster, in order: note SELECT, note reload, the destination projection, the
keyed-row SELECT, the receipt SELECT, the `ConcurrencyToken` UPDATE. The destination projection
as measured is one statement of the shape
`SELECT a."Status", a."StartedAt" FROM "AgentSessions" AS a WHERE a."Id" = @__parent_0 LIMIT 2`.
The D-3 SELECT is the same projection for `row.AgentSessionId` (a different parameter name) and
therefore exactly one command: the `+1` in every follow-shape pin below is derived from that
measured statement, not re-measured, because today's tree never issues it.

Also measured in TD-3: after the harness's real enqueue pass the keyed row already carries
`SourceLandNotificationId == note.Id` (`SessionMessageQueueService.cs` line 747 sets it on the
production insert). The resemblance rule over the fixture body (2,928 normalized characters):
exact and framed-but-whole (prefix + body + suffix) are receipt; head-only (first 300 characters),
tail-altered (last character changed), middle-altered (one character) are not; a whitespace-only
difference is receipt. Those are the existing CARD-0055/CARD-0024 rules this card does not change.

### 1.2 Derived CARD-1157 pins

Counted exactly as the CARD-1121 tests count (every production command from the counted
contexts; fixture setup and assertion reads excluded by resetting after arrangement). A different
count fails the checkpoint; Code names the extra statement in a plan correction and does not raise
a pin in the same dispatch.

| Arm | Commands | Receipt selects | Session projections per pass | Scanned | Derivation |
|---|---|---|---|---|---|
| Whitelist, B Stopped or Failed, complete prompt on B | `[7, 2, 2]` | `[1, 0, 0]` | `[2, 0, 0]` | `[B]` | 6 + D-3 SELECT; passes 2 and 3 are the confirmed early return (fetch + reload) |
| Whitelist, B Stopped, no prompt anywhere | `[7, 6, 6]` | `[1, 0, 0]` | `[2, 2, 2]` | `[B]` | 6 + D-3 SELECT every pass; reuse omits only the receipt SELECT |
| Whitelist, B's session row missing | `[7, 6, 6]` | `[1, 0, 0]` | `[2, 2, 2]` | `[A]` | the D-3 SELECT returns null; parent scan and parent proof |
| D-2 false by one flip, prompt on B | `[6, 5, 5]` | `[1, 0, 0]` | `[1, 1, 1]` | `[A]` | today's path; no D-3 SELECT |
| Whitelist, B Running, prompt committed on B, runner NotFound | `[7, 2, 2]` | `[1, 0, 0]` | `[2, 0, 0]` | `[B]` | `CatchUpTranscriptAsync` on a 404 adds no SQL (the plan left this unpinned; it is pinned here because the live catch-up is a failed pull) |
| Whitelist, B Running, prompt only in the runner | `[11, 2, 2]` | `[1, 0, 0]` | `[2, 0, 0]` | `[B]` | 7 + the measured 4-statement warm ingest; the two pulled rows (prompt, TurnEnd) batch into one INSERT |
| Whitelist, B Running, no prompt | `[7, 7, 7]` | `[1, 1, 1]` | `[2, 2, 2]` | `[B, B, B]` | never cached (A-1 on the scanned session) |
| Whitelist, A Running, B Stopped, no prompt | `[7, 6, 6]` | `[1, 0, 0]` | `[2, 2, 2]` | `[B]` | A-1 reads the scanned session's status, not the parent's |
| `ParentSessionId` already B, row on B | `[6, 2, 2]` | `[1, 0, 0]` | `[1, 0, 0]` | `[B]` | D-2 false on equal ids; no second SELECT |
| Row on A, prompt only on B | `[6, 5, 5]` | `[1, 0, 0]` | `[1, 1, 1]` | `[A]` | today's shape |
| Whitelist, the D-3 SELECT faults once | `[7, 7, 2]` | `[0, 1, 0]` | `[2, 2, 0]` | `[]`, `[B]` | pass 1: five statements through the faulted projection (the counter records at the executing hook), then the catch's note reload and UPDATE; pass 2 confirms |

## 2. Plan corrections the measured tree forces

- **T-1, harness back-pointer (D-5).** The harness does not need to set
  `SourceLandNotificationId`: `SeedLinkedNoteAsync` runs the real enqueue pass, and TD-3 measured
  `row.SourceLandNotificationId == note.Id` afterwards. Every integration fixture below therefore
  runs the production back-pointer; V-1 `back-pointer-null` is the only null exercise and
  `back-pointer-other` the only conflicting one.
- **T-2, D-2 item 1 is two guards.** `note.ParentSessionId is Guid parent && parent != Guid.Empty`
  is bypassable twice. V-1 gains `parent-null` beside `parent-empty`, and `baseline-zero` pins the
  `>= 0` boundary as admitted. V-1 is 29 rows; CP-1's floor is 29.
- **T-3, the Running budget is pinned.** With runner NotFound a live catch-up is a swallowed pull
  with no SQL, so the `running` arm is `[7, 2, 2]`. The live-pull Running shape gets its own arm
  (`running-runner-holds-prompt`, `[11, 2, 2]`), which is also the witness that catch-up targets the
  scanned session.
- **T-4, distinct generations.** `AddStoppedSessionAsync` sets `StartedAt = now`, which can equal
  the parent's at microsecond precision only by coincidence. Every CARD-1157 fixture creates B with
  `StartedAt` one hour before A's, so a stamp built from the parent's `StartedAt` against B's
  snapshot refuses `state:AcceptedGeneration` and the quiet-miss budget arm goes red (G-8).
- **T-5, generation of the carried row.** Production records `LastDeliveryGeneration` from the
  session it types into. `MarkSentAsync` reads `StartedAt` before the `change` callback, so the
  fixtures retarget the row first (new `destination` parameter), making the generation B's. D-2
  has no generation conjunct, so this is fixture fidelity, not a guard.
- **T-6, the keyed-row session cannot be missing under the production schema.**
  `FK_SessionQueuedMessages_AgentSessions_AgentSessionId` is `ON DELETE CASCADE`
  (`server/Migrations/20260619192234_AddSessionQueuedMessages.cs` line 31; model snapshot
  `SessionQueuedMessage` relationship). D-3's null branch is reachable only as the end state of a
  race: B deleted between the keyed-row SELECT and the D-3 SELECT. The harness arranges exactly that
  end state by dropping the constraint in the per-test isolated schema before deleting B's row; the
  queue row then survives and names a session that no longer exists. This is fixture SQL on a
  cloned database and touches no production path.
- **T-7, PC-12 is retired; PC-A is its successor.** The CARD-1121 design's PC-12 ("scan
  `row.AgentSessionId`") is this card's behavior for the whitelist shape. Mutation must not run it
  against the rewritten method.
- **T-8, the plan's V-2 lacked the false-confirmation arms the brief names.** A prompt on the
  parent while the row went to B, a prompt on a third session, a head-only or tail-altered prompt on
  B, a prompt on B at or below the floor, and a non-accepted kind on B are all "stays open, B was the
  only session scanned" (V-2 method 2). Both proof cross-binding directions are proved at the real
  seam (V-2 method 5) and at the cache (V-5).
- **T-9, statement-budget arms are extended** with `running-quiet-miss` and
  `parent-running-row-stopped-quiet-miss` so A-1's status source is pinned in both directions.

## 3. Rewritten V-10 (D-5)

`AgentTaskLandReceiptWatermarkTests.C1121_ForeignDestinationNoteStaysOpenExactlyAsToday` becomes
`C1121_ForeignDestinationFollowsKeyedRow(string arm)`, three results, same class (36 results stay 36).
Fixture: the existing `ArrangeAsync` (A: 48 candidates above floor 10, one at-floor row, TurnEnd;
A Stopped after the enqueue pass); B from `AddSessionAsync(SessionStatus.Stopped, startedAt: A's
StartedAt minus one hour)`; B's transcript ingested through the runtime with the complete body as
`UserPrompt` at sequence 15 (runner sequence `Floor + 5`); the row retargeted to B through
`MarkSentAsync(note, destination: B)` so `LastDeliveryGeneration` is B's and the baseline is 10;
runner NotFound; counters reset; coherence asserted for A and B.

| Arm | Arrange | Decisive assertions |
|---|---|---|
| `row-destination-confirms` | as above, both stores warm | pass 1 `Sessions` `[B]`, `ReceiptSelects` 1, `Commands` 7; `State` `Confirmed`, `ConfirmingPromptSequence` 15 and equal to `InMemoryFirstReceiptAsync(B, 10, false, Outcome, body)`; passes 2 and 3 `Commands` 2, `ReceiptSelects` 0; `Publishes` 0, `Proofs` 0. No-cache control: a second linked note arranged identically (parent set Running around its enqueue pass as today's control does), reconciled three times by a service with `scanCache: null`, has the same `(State, ConfirmingPromptSequence, LastErrorCode, EnqueueAttempts, ParentSessionId, QueueMessageId is null)` tuple and a non-null `ConfirmedAt`: the cache is not what confirms |
| `parent-equals-row-destination` | `ParentSessionId` set to B before any pass; row on B | pass 1 `Sessions` `[B]`, `Commands` 6, session projections 1; `Confirmed` at 15; D-2 false on equal ids |
| `row-equals-parent-miss` | row left on A; body only on B | passes `Commands` `[6, 5, 5]`, `ReceiptSelects` `[1, 0, 0]`, `ReceiptRows` 48, `Sessions` `[A]`; `AwaitingReceipt`, `ConfirmedAt` null; `Publishes` 1 |

Every arm: `Adapter.Inputs` empty, `Runner.KillCalls` 0, keyed row `Status`/`SentAt`/`DeliveryAttempts`
unchanged, A and B `Status` unchanged. The old `cached-stays-open` expectation (`AwaitingReceipt`
from a parent scan) is deliberately replaced; its "cache cannot mask the defect" purpose is now
carried by V-2 method 3 (`C1157_RefusedFollowKeepsParentScan`) for every non-whitelist shape.

## 4. False-confirmation risk register

| Risk | Guard | Pinned by | PC |
|---|---|---|---|
| Prompt on the parent A while the row was delivered to B | D-1: no parent fallback after a destination miss | V-2 method 2 `parent-holds-prompt`; V-2 method 5 `destination-proof-then-destination-vanishes` (stays open while B exists) | PC-H |
| Prompt on B while any D-2 conjunct fails | D-2 whitelist; parent scanned | V-1 (29); V-2 method 3 (4 shapes) | PC-B.`<row>`, PC-G |
| Prompt on a third session C | exactly one of {A, B} is scanned | V-2 method 2 `third-session-holds-prompt` (`Sessions` has one element, B) | witnessed under PC-H (any wider scan breaks the single-element assertion); no distinct compiling defect scans a session neither the note nor the row names |
| Head-only or tail-altered prompt on B | `IsReceipt` identity and completeness through `FirstReceiptAsync` | V-2 method 2 `head-only-on-destination`, `tail-altered-on-destination` | PC-I |
| Confirmed without any prompt (`Sent`, equal body, cache hit) | `FirstReceiptAsync` result is the only confirmation; the cache never confirms | V-2 method 4 `quiet-miss` (Hits 2, stays open); R-1 `C1121_RealQueueDeliveryIsNeverHiddenByTheCache` | PC-D |
| Prompt on B at or below the keyed row's floor | the floor passed to `Prompts` is `row.LastDeliveryBaselineSequence` on either target | V-2 method 2 `below-floor-on-destination` | PC-U |
| Body typed as a non-accepted kind on B | `Prompts` kinds from `note.IsLegacy`/`note.Kind`, unchanged | V-2 method 2 `assistant-text-on-destination`; method 3 `is-legacy-outcome` (one-kind never follows) | R-4 (CARD-1121 PC family); no new guard |
| A parent proof certifies a destination scan or the reverse | `Context.ScanSessionId` (ContextRefusal), `stamp:SessionId`, `identity:ScanSession` against `ScanSessionId` | V-5 (4 rows); V-2 method 5 (both directions at the real seam); V-2 method 4 `quiet-miss` (reuse happened only because the stamp is B's) | PC-C, PC-M, PC-N, PC-O |
| A Running destination cached, then a late prompt hidden | A-1 on the scanned session's status | V-2 method 4 `running-quiet-miss` (`[1, 1, 1]`, Publishes 0); method 1 `running`, `running-runner-holds-prompt` | PC-L |
| Stamp taken for the wrong session or generation | before/after stamps from `ObserveReceiptStateAsync(scanSession)` with the scanned session's `StartedAt` | V-2 method 4 `quiet-miss` (pass-2 receipt selects 0; refusals contain neither `identity:ScanSession` nor `state:AcceptedGeneration`) | PC-K, PC-O |
| Framed-but-whole prompt on B (prefix + body + suffix) | receipt by CARD-0024; not a false confirmation | V-2 method 1 `stopped-framed-prompt` confirms | existing rule, out of scope |
| Body under 12 normalized characters confirms on any record (weak arm) | existing CARD-0055 rule; land-note bodies are hundreds of characters | out of scope, cited | none |
| Two notes with the same body on B confirm from one prompt | identical to today's parent behavior | out of scope, cited | none |
| Row typed into an earlier generation of B | D-2 has no generation conjunct; parity with today's parent scan for these kinds (only `TaskCompletion` checks `LastDeliveryGeneration`, reconciler lines 244-251, and it is outside D-2); the complete prompt must still be in B's transcript above the floor | out of scope; an optional later plan correction (a conjunct 13 after the D-3 SELECT) is the orchestrator's call, not needed for fail-closed | none |

## 5. Verification design

### Inspection

Bodies read in full: `server/Application/Services/AgentTaskLandNotificationService.cs`,
`LandReceiptScanCache.cs`, `LandNoteReceipt.cs`, `StandingQueueSwitchPolicy.cs`,
`src/Antiphon.SessionRunner.Contracts/PromptSubmissionMatch.cs`, `SessionGeneration.cs`,
`server/Domain/Enums/LandingEnums.cs`, domain `AgentTaskLandNotification.cs`,
`SessionQueuedMessage.cs` (lines 1-120), `AgentSession.cs` members;
`AgentControlService.cs` 560-580 and 725-745 (the `NeverAttempted` carry);
`SessionMessageQueueService.cs` 735-760 (row insert), 2455-2470 and 3418-3432 (baseline);
`AgentSessionRuntime.cs` 700-770 (`CatchUpTranscriptAsync`, `PullAndPersistAsync`,
`CatchUpForReceiptAsync`, `ObserveReceiptStateAsync`); `ExpectationSnapshotReader.cs` 820-845;
`TypedBodySpill.PointerHeadline`; `server/Infrastructure/Data/AppDbContext.cs` 1589-1642 and the
model snapshot's `SessionQueuedMessage` relationship; migration `20260619192234`. Tests and
fixtures: `tests/Antiphon.Tests/TestHelpers/LandReceiptScanHarness.cs` (whole),
`ReceiptScanRecognizer.cs` (whole), `FullCommandCounter.cs` (whole),
`AgentTaskLandReceiptWatermarkTests.cs` (whole: `Pass`, `ArrangeAsync`, V-3 through V-11),
`AgentTaskLandReceiptWatermarkSafetyTests.cs` 1-97 and 364-476 (`Snapshot`, `TranscriptStatements`,
V-9), `LandReceiptScanCacheTests.cs` 1-80 and 360-382 (`scan-session-incoherent`),
`AgentTaskLandReceiptTests.SeedAsync`, `BridgeQueueHarness.cs` 295-315 and 493-520,
`RunnerBranchContractDocumentationTests.Read`. Documents: the plan, the CARD-1121 test design and
sample, `docs/session-runtime-invariants.md` 235-265, `docs/testing-and-build.md` (manifest, runner
tool, build slots, PC execution, combined filters).

Boundaries and where each lands: parent null versus `Guid.Empty` (V-1 `parent-null`,
`parent-empty`); row session `Guid.Empty` (V-1); equal ids (V-1 `same-session`; V-3
`parent-equals-row-destination`); baseline null, -1, 0, 10 (V-1; V-2 method 2
`below-floor-on-destination`); attempts 0 versus 1 (V-1); body one character and case (V-1);
back-pointer null, equal, other (V-1; V-2 method 3); queue status Sent, Pending, Canceled (V-1; V-2
method 3); all eight kinds plus 999 and the legacy flip (V-1; V-2 method 3); snapshot, delivery,
pointer, spill (V-1); expected text (V-1); scanned-session status Stopped, Failed, Running (V-2
methods 1 and 4); parent Running with B Stopped (V-2 method 4); B's row present, missing, appearing,
vanishing (V-2 methods 4 and 5); prompt at 10 versus 15 (V-2 method 2 versus 1); runner NotFound
versus live entries for B only (V-2 method 1); resemblance head-only, tail-altered, framed (V-2
methods 2 and 1); third session (V-2 method 2); `StartedAt` of A and B one hour apart (fixture; G-8);
a faulted D-3 SELECT (V-2 method 1); cache present versus absent (V-2 method 1, V-3). Excluded:
`DeliveryVerdict` values on the destination row (not a D-2 member; the receipt rule guards it and
CARD-1121 V-1 pins the cache side); text over the cap (cache refusal only, CARD-1121 V-6
`oversized-body`); the profiled completion path (conjunct 5 is unit-tested; the integration path
cannot reach the whitelist by construction and CARD-1121 V-6 `profiled-completion` pins today's parent
scan); the unlinked `ConflictException` (plan out of scope).

Missing setup the harness must gain (`tests/Antiphon.Tests/TestHelpers/LandReceiptScanHarness.cs`):

1. `AddSessionAsync(SessionStatus status, DateTime? startedAt = null, Guid? id = null)` returning
   the id; `AddStoppedSessionAsync()` delegates to it. CARD-1157 fixtures pass A's `StartedAt`
   minus one hour (T-4).
2. `MarkSentAsync(note, change, Guid? destination = null)`: assigns `AgentSessionId` before reading
   the destination's `StartedAt` (T-5).
3. `DetachQueueRowsFromSessionsAsync()`: `ALTER TABLE "SessionQueuedMessages" DROP CONSTRAINT
   "FK_SessionQueuedMessages_AgentSessions_AgentSessionId"` on the isolated schema, and
   `DeleteSessionAsync(Guid)` (`ExecuteDeleteAsync` of the `AgentSessions` row) (T-6).
4. `FaultNthSessionProjection : DbCommandInterceptor`: while armed, throws
   `InvalidOperationException("planned session projection fault")` from the executing hook on the
   Nth command whose text starts `SELECT a."Status", a."StartedAt" FROM "AgentSessions"` since
   arming, once. Registered after `Commands` so the faulted statement is counted.
5. The new class's `Pass` record adds `SessionProjections` (commands with that prefix), `PullsA`,
   `PullsB` (`Runner.Pulls(id)`) and `TranscriptStatements` (as the safety class computes it).
6. `Runner.NextEntries` is already per session; the B-only live arm returns entries for B and
   throws for any other id (a pull of A would be counted and fail `PullsA.ShouldBe(0)`).
7. `SeedTranscriptAsync(session, atFloorText, candidates, candidate)` and
   `InMemoryFirstReceiptAsync(session, ...)` already take a session and need no change.

### Delivery inventory

This change adds no producer, queue row, event or acknowledgement. The delivery it touches is the
existing one, read from the destination the row actually went to: producer = the queue's typed
delivery of the keyed row into the recipient pty, now the carried row's session B (carried by
`AgentControlService.AcceptAsync` while `NeverAttempted`, then typed by a flush on B which records
`LastDeliveryBaselineSequence` as B's `MAX(Sequence)`); destination = B's transcript
(`TranscriptEntries`, kind `UserPrompt` or an admitted `QueuedUserPrompt`); persistence boundary =
`AgentSessionRuntime.PersistTranscriptAsync` commit and `SessionStateStore` publication for B;
recovery = the hosted reconciler's pass with its catch-up pull of B; observable receipt =
`AgentTaskLandNotification.State = Confirmed` with `ConfirmingPromptSequence` equal to the first
complete matching prompt on B; durable identity = `note.Id` → `note.QueueMessageId` →
`row.AgentSessionId` → `TranscriptEntries(B)` above `row.LastDeliveryBaselineSequence`.

Producer-to-recipient evidence and substitutes:

- **Substitute declared: the carry and the typing on B.** The harness has one adapter, bound to A.
  The carry (`AgentControlService` lines 732-736) and the flush on B are replaced by fixture writes
  that set the same columns (`UpdateRowAsync`/`MarkSentAsync(destination: B)`: `AgentSessionId`,
  `Sent`, attempts 1, baseline 10, `LastDeliveryGeneration` = B's `StartedAt`). What this cannot
  prove: that a real standing-switch carry keeps the row `NeverAttempted` and that a real flush on
  B records B's `MAX(Sequence)`; those are `AgentControlService`/`SessionMessageQueueService`
  behaviors outside this card and unchanged by it. The real-queue proof of the shared confirmation
  path stays CARD-1121 V-11 and V-12 (R-1, R-2 rerun here).
- **Recipient evidence through the real runtime ingest.** Every confirming arm's prompt reaches
  the table only through `PersistTranscriptAsync` (`IngestEventsAsync`), never fixture SQL, and the
  verdict is the complete matching `UserPrompt` row on B (`ConfirmingPromptSequence` equal to the
  in-memory oracle over B's committed rows).
- **Busy recipient:** `running-runner-holds-prompt` (V-2 method 1): B Running, the prompt exists
  only in the runner's transcript; the reconciler's own catch-up of B persists it and confirms;
  nothing is typed, nothing waits, nothing is killed.
- **One already eligible:** `stopped` and `failed` (method 1): the prompt is committed on B before
  the first pass.
- **Crash or failure at the new handoff:** `stopped-after-session-select-fault` (method 1): the D-3
  SELECT faults once; pass 1 ends in the existing catch (`notification_reconcile_failed:*`,
  `EnqueueAttempts` +1, `AwaitingReceipt`, nothing confirmed or closed); pass 2 scans B and confirms.
  A crash of the recipient after typing is CARD-1121 V-12 (R-2).
- A request, queue insert, `Sent` flag, `SentAt`, adapter `SubmittedBodies` or cache metric is never
  accepted as delivery in any assertion below.

### Proves it works now

Slice S1 (policy lane, no database; `tests/Antiphon.Tests/Application/ReceiptScanDestinationTests.cs`,
`[Category("Unit")]`). Positive fixture `Positive()`: note Id N, `IsLegacy` false, kind `Outcome`,
`ParentSessionId` A, `Body` = a fixed multi-line body of at least 200 normalized characters,
`State` `AwaitingReceipt`, `QueueMessageId` = row Id, both completion JSON members null; row Id Q,
`AgentSessionId` B, `SourceLandNotificationId` N (the production shape), `Status` `Sent`,
`DeliveryAttempts` 1, `LastDeliveryBaselineSequence` 10, `RemoteSpillBody` null, `Body` equal;
`expectedText` equal to the body. Each row changes exactly one member from a fresh copy.

- V-1: the predicate admits only the whitelist | policy | `ReceiptScanDestinationTests.C1157_FollowsQueueDestinationOnlyWhenEveryPositiveHolds(string flip)`, 29 `[Arguments]` rows | `ReceiptScanTarget.FollowsQueueDestination(note, row, expectedText).ShouldBe(expected, flip)`.

| Rows | Flip | Returns |
|---|---|---|
| `positive-held`, `positive-aged`, `positive-conflict`, `positive-outcome` | kind | true |
| `back-pointer-null` | `row.SourceLandNotificationId = null` | true (D-2 item 3, first disjunct) |
| `baseline-zero` | baseline 0 | true (`>= 0` boundary) |
| `kind-dispatch-base`, `kind-delivery-failure`, `kind-task-completion`, `kind-legacy-check-note`, `kind-undefined` (`(LandNotificationKind)999`) | kind | false (item 4) |
| `is-legacy` | `IsLegacy = true`, kind Outcome | false (item 4) |
| `parent-null`, `parent-empty` | `ParentSessionId` null; `Guid.Empty` | false (item 1) |
| `same-session` | `row.AgentSessionId = A` | false (item 2) |
| `queue-destination-empty` | `row.AgentSessionId = Guid.Empty` | false (item 2) |
| `back-pointer-other` | a fresh Guid | false (item 3) |
| `completion-snapshot`, `completion-delivery` | `"{}"` on that member | false (item 5) |
| `status-pending`, `status-canceled` | status | false (item 6) |
| `attempts-zero` | 0 | false (item 7) |
| `baseline-null`, `baseline-negative` | null; -1 | false (item 8) |
| `remote-spill-body` | `"spilled"` with bodies still equal | false (item 9) |
| `body-one-char`, `body-case-differs` | `row.Body` appended one character; one letter's case flipped | false (item 10) |
| `pointer-headline` | note body, row body and `expectedText` all set to body + newline + `TypedBodySpill.PointerHeadline`, so items 10 and 12 hold and 11 alone fails | false (item 11) |
| `expected-text-differs` | `expectedText` = body + one character, bodies equal | false (item 12) |

Slice S2 (policy rows in the same file, after the cache change; `docs/session-runtime-invariants.md`).

- V-4: the invariant names the scanned session | policy, file read | `ReceiptScanDestinationTests.C1157_RuntimeInvariantNamesTheScannedSession`, 1 result; reads the file through the `RunnerBranchContractDocumentationTests.Read` pattern and collapses whitespace | contains the D-8 sentence verbatim (collapsed), "the scanned session's `StartedAt`" and "a Stopped or Failed scanned session"; does not contain "the destination's `StartedAt`" or "a Stopped or Failed destination" (both occurrences in the CARD-1121 paragraph are replaced, lines 250 and 256 today). Documentation only; V-2 and V-3 prove behavior.
- V-5: the proof binds the scanned session | policy | `ReceiptScanDestinationTests.C1157_ProofBindsTheScannedSession(string arm)`, 4 rows over the production `TryBuildContext`, `StateStamp.TryCreate`, `Publish`, `TryReuse` with a manual clock, parent A and row destination B (A-3 admits them unequal):

| Arm | Arrange | Expect |
|---|---|---|
| `build-context-names-the-parent` | `TryBuildContext` on the positive case | true; `context.ScanSessionId == A` although `QueueDestination == B` |
| `positive-destination-scan` | context `with { ScanSessionId = B }`; publish and reuse stamps both for B (SessionId B, `AcceptedGeneration` equal to B's `StartedAt`) | `TryReuse` true |
| `parent-stamp-cannot-vouch-for-destination-scan` | context `with { ScanSessionId = B }`; publish and reuse stamps both for A | false, refusal `identity:ScanSession` |
| `proof-context-scan-session-is-bound` | proof published with context `ScanSessionId = A` and a stamp for B (an unsafe publication only a mutated reconciler could make); reuse with context `ScanSessionId = B` and the same B stamp | false, refusal `context:ScanSessionId` (the only row where the stamp and identity checks both pass, so this member alone refuses) |

The existing `LandReceiptScanCacheTests.C1121_MalformedIdentityNeverReusesNegativeScan`
`scan-session-incoherent` row keeps its `identity:ScanSession` expectation (`ScanSessionId` defaults
to the parent) and must not be weakened; the existing V-1 `stamp-session` row pins `stamp:SessionId`
(R-3).

- V-3: the rewritten foreign-destination method | integration | §3 | as tabled there.

Slice S3 (managed PostgreSQL lane; `tests/Antiphon.Tests/Application/ReceiptScanDestinationIntegrationTests.cs`,
`[Category("Integration")]`, `[Timeout(180_000)]`, `LandReceiptScanHarness`). Default fixture
`ArrangeFollowAsync(h, destinationStatus, promptOnB, ...)`: A seeded with 48 unrelated
`UserPrompt` candidates above floor 10 plus the at-floor row and a TurnEnd through the runtime
before the real enqueue pass; B from `AddSessionAsync(status, A.StartedAt - 1 h)` seeded the same
way through the runtime with the arm's prompt substituted at sequence 15 by the `candidate`
callback; the row retargeted and typed through `MarkSentAsync(note, destination: B)` (Sent,
attempts 1, baseline 10, generation B's); A set Stopped; coherence asserted for A and B; runner
NotFound; counters reset; three `ReconcileAsync` passes through one service instance. Every arm
asserts: `Adapter.Inputs` empty; `Runner.KillCalls` 0; the keyed row's `Status`, `SentAt`,
`DeliveryAttempts`, `AgentSessionId` unchanged; A's and B's `Status` unchanged after the passes;
`PullsA` 0 and `PullsB` equal to the passes that reached catch-up; the note never enters
`DestinationUnavailable` or `Canceled`; `LastErrorCode` never starts `notification_reconcile_failed`
except where stated.

- V-2: the reconciler follows the keyed row under D-2 and only then | integration | five methods, 23 results:

Method 1, `C1157_SentRowDestinationReceiptConfirms(string destination)`, 6 rows. Decisive:
`Sessions` `[B]` on the scanning pass, `State` `Confirmed`, `ConfirmingPromptSequence` equal to the
in-memory oracle over B (15, or the pulled sequence), `Publishes` 0, `Proofs` 0 after confirmation.

| Row | Arrange | Expect |
|---|---|---|
| `stopped` | B Stopped; complete body at 15 on B | `Commands` `[7, 2, 2]`, `ReceiptSelects` `[1, 0, 0]`, `SessionProjections` `[2, 0, 0]`, `Floors` `[10]`; Confirmed at 15. No-cache control as in §3: a second note arranged identically and reconciled by a service with `scanCache: null` confirms the same tuple |
| `failed` | B Failed | as `stopped` (A-1 admits Failed; the catch-up's terminal flag is true) |
| `stopped-framed-prompt` | B holds `"prefix\n" + body + "\nsuffix"` at 15 | Confirmed at 15 (receipt by CARD-0024; the oracle agrees) |
| `running` | B Running; body at 15 committed; runner NotFound | `Commands` `[7, 2, 2]`; Confirmed at 15; `Publishes` 0; refusals contain `eligibility:DestinationStatus` 1; B `Status` still Running; `LastErrorCode` null |
| `running-runner-holds-prompt` | B Running; no prompt committed; `Runner.Transcript = Entries`, `NextEntries` returns for B only `[Event(B, 60, UserPrompt, body), Event(B, 61, TurnEnd, null)]` and throws for any other id | pass 1 `PullsB` 1, `PullsA` 0; the prompt committed exactly once on B at 60 (`PromptSequenceAsync` pattern); `ReceiptSelects` 1, `Sessions` `[B]`; Confirmed at 60 and equal to the oracle; `Commands` `[11, 2, 2]`; `Publishes` 0; B still Running |
| `stopped-after-session-select-fault` | as `stopped`; `FaultNthSessionProjection` armed for the 2nd projection | pass 1: `Commands` 7, `ReceiptSelects` 0, `LastErrorCode` starts `notification_reconcile_failed:InvalidOperationException`, `EnqueueAttempts` +1, `AwaitingReceipt`, `ConfirmedAt` null; pass 2: `ReceiptSelects` 1, `Sessions` `[B]`, Confirmed at 15, `LastErrorCode` null; pass 3 `Commands` 2 |

Method 2, `C1157_FollowScanNeverConfirmsFromAnotherSessionOrResemblance(string arm)`, 6 rows; B
Stopped, whitelist true. Decisive on every row: `Sessions` `[B]` on pass 1 (one element),
`ReceiptSelects` `[1, 0, 0]`, `Commands` `[7, 6, 6]`, `State` `AwaitingReceipt`, `ConfirmedAt` null,
`ConfirmingPromptSequence` null, `Publishes` 1, `Proofs` 1, `Hits` 2, and the in-memory oracle over
B above floor 10 is null.

| Row | Arrange | Also expect |
|---|---|---|
| `parent-holds-prompt` | complete body at 15 on A; B has 48 unrelated candidates | `ReceiptRows` 48; the oracle over A at floor 10 is 15 (the parent prompt is real and was not scanned) |
| `third-session-holds-prompt` | a third Stopped session C holds the body at 15; A and B unrelated | `ReceiptRows` 48; `Runner.Pulls(C)` 0 |
| `head-only-on-destination` | B holds `body[..300]` at 15 | fixture self-check: `PromptSubmissionMatch.IsConfirmedBy(body, text)` true and `IsCompleteIn` false; `ReceiptRows` 48 |
| `tail-altered-on-destination` | B holds the body with its last character changed at 15 | `ReceiptRows` 48 |
| `below-floor-on-destination` | B's at-floor row (sequence 10) is the complete body; candidates unrelated | `ReceiptRows` 48; the oracle over B at floor 9 is 10 (only the floor excludes it) |
| `assistant-text-on-destination` | B holds the body as `AssistantText` at 15 | `ReceiptRows` 47 |

Method 3, `C1157_RefusedFollowKeepsParentScan(string flip)`, 4 rows; complete body at 15 on B,
nothing on A, A and B Stopped. Decisive: `Sessions` `[A]`, `ReceiptSelects` `[1, 0, 0]`, `ReceiptRows`
48, `Commands` `[6, 5, 5]`, `SessionProjections` `[1, 1, 1]`, `PullsA` 1 per pass, `PullsB` 0,
`AwaitingReceipt`, `ConfirmedAt` null, `Publishes` 1 (the parent proof), the oracle over B at floor 10
is 15 (a receipt existed where the row went and was correctly not consulted).

| Row | Flip |
|---|---|
| `one-kind-dispatch-base` | `note.Kind = DispatchBase` (one-kind; SQL has `t."Kind" = 'UserPrompt'`) |
| `is-legacy-outcome` | `note.IsLegacy = true` (the census's legacy one-kind group) |
| `status-pending` | `row.Status = Pending`, attempts 1 (not parked: below `MaxDeliveryAttempts`) |
| `back-pointer-other` | `row.SourceLandNotificationId` = a fresh Guid |

Method 4, `C1157_FollowScanStatementBudget(string arm)`, 5 rows, §1.2 pins exactly; also
`TranscriptStatements` equal to `ReceiptSelects` per pass (no probe), every receipt SQL accepted by
`ReceiptScanRecognizer.IsReceiptScan` with the two-kind `IN` list, floor parameter 10 on every scan.

| Row | Arrange | Expect |
|---|---|---|
| `confirm` | B Stopped, body at 15 on B | `[7, 2, 2]`, receipt `[1, 0, 0]`, projections `[2, 0, 0]`, `Sessions` `[B]`, `Publishes` 0 |
| `quiet-miss` | B Stopped, no prompt anywhere | `[7, 6, 6]`, receipt `[1, 0, 0]`, rows `[48, 0, 0]`, projections `[2, 2, 2]`, `PullsB` `[1, 1, 1]`; `Proofs` 1, `Publishes` 1, `Hits` 2; refusals contain neither `identity:ScanSession` nor `state:AcceptedGeneration` nor `context:ScanSessionId`; stays open |
| `row-session-missing` | `DetachQueueRowsFromSessionsAsync`, then `DeleteSessionAsync(B)` after the row was typed to B | `[7, 6, 6]`, receipt `[1, 0, 0]`, projections `[2, 2, 2]`, `Sessions` `[A]`, rows 48, `PullsA` 1 per pass, `PullsB` 0; `Publishes` 1; `AwaitingReceipt`; `LastErrorCode` null (not `destination_unavailable`) |
| `running-quiet-miss` | B Running, no prompt | `[7, 7, 7]`, receipt `[1, 1, 1]`, rows `[48, 48, 48]`, `Sessions` `[B, B, B]`, `PullsB` 1 per pass; `Proofs` 0, `Publishes` 0, `Hits` 0; refusals `eligibility:DestinationStatus` 3; B still Running; `LastErrorCode` null |
| `parent-running-row-stopped-quiet-miss` | A Running (set after the enqueue pass), B Stopped, no prompt | `[7, 6, 6]`, receipt `[1, 0, 0]`, `Sessions` `[B]`; `Publishes` 1 (the scanned session is terminal); A still Running |

Method 5, `C1157_ProofForOneScanTargetNeverCertifiesTheOther(string direction)`, 2 rows;
`DetachQueueRowsFromSessionsAsync` first.

| Row | Arrange | Expect |
|---|---|---|
| `parent-proof-then-destination-appears` | row typed to a fresh Guid B with no session row; A quiet | passes 1-2: `[7, 6]`, receipt `[1, 0]`, `Sessions` `[A]`, `Publishes` 1. Then `AddSessionAsync(Stopped, A.StartedAt - 1 h, id: B)` and B's transcript ingested with the body at 15. Pass 3: receipt 1, `Sessions` `[B]`, Confirmed at 15, `Hits` still 1, refusals `context:ScanSessionId` 1 (D-3 present changes only the scan target; every other context member is equal) |
| `destination-proof-then-destination-vanishes` | whitelist true, B quiet; A holds the complete body at 15 | passes 1-2: `[7, 6]`, receipt `[1, 0]`, `Sessions` `[B]`, `AwaitingReceipt` (the parent prompt is not this row's delivery). Then `DeleteSessionAsync(B)`. Pass 3: receipt 1, `Sessions` `[A]`, Confirmed at 15 (today's parent behavior once the row's session is gone), refusals `context:ScanSessionId` 1, `Hits` still 1 |

### Guards the regression

- R-1: the CARD-1121 behavior class other than V-10 is unchanged (V-3, V-4, V-5, V-6, V-11: unchanged transcript reuse, committed-change rescans, attempt and payload rescans, unknown evidence, real-queue delivery) | `AgentTaskLandReceiptWatermarkTests` (36 results); decisive: per-pass command totals `[6, 5, 5]` on `two-kind-outcome-404`, `ReceiptSelects` 1 after every committed change, `Publishes` 0 on `running-destination-real-delivery`.
- R-2: races, faults, failed saves, statement budgets and busy/crashed recovery on the parent scan are unchanged by the `ScanSessionId` wiring | `AgentTaskLandReceiptWatermarkSafetyTests` (18 results); decisive: `metrics.Publishes.ShouldBe(1)` on `unique-violation-reload-after-cached-miss`, the `[6, 5, 5]`/`[9, 8, 8]` rosters, `recovery.ReceiptSelects.ShouldBe(1)` on every V-12 arm.
- R-3: the cache's whitelist, lifetime, capacity and identity rows are unchanged by the added context member | `LandReceiptScanCacheTests` (115 results); decisive: every `reused.ShouldBeFalse(flip)` including `stamp-session` and `scan-session-incoherent`, every `positive-*` true.
- R-4: the receipt projection, first-match, floor, queued-kind and pointer rules are unchanged (`Prompts` now receives the scanned session; its signature and SQL are unchanged) | `AgentTaskLandReceiptTests` (42) and `AgentTaskLandQueuedReceiptTests` (22); decisive: `ConfirmingPromptSequence` equals the in-memory oracle per shape, the closed projection SQL.
- R-5: the parent-existence gate (`DestinationUnavailable` on a missing parent before the keyed row is consulted), the keyed-row conflict refusals and the watchdog's unchanged `IsReceivedAsync` | `AgentTaskLandNotificationRecoveryTests` (43) and `ExpectationNoteDebtTests` (7); decisive: the `DestinationUnavailable`/`NextAttemptAt` matrix, the `UndeliveredNotes` subject keys.

### Guard inventory

Each row is one independently bypassable guard mapped to one distinct PC. V-1's 23 refusing rows
are 23 guards; the six admitting rows share one forced-refusal control.

| G | Plan ref and safety-critical guard | PC |
|---|---|---|
| G-1.`<row>` (23) | D-2 items 1-12: each conjunct alone keeps the parent scan | PC-B.`<row>` |
| G-1.positive | D-2: the exact production shape (and `back-pointer-null`, `baseline-zero`) is admitted | PC-B.positive |
| G-2 | D-1: when D-2 is true and B exists, `Prompts` and the receipt scan run on `row.AgentSessionId` | PC-A |
| G-3 | D-2/D-3 evidence order step 3: a refused predicate scans the parent | PC-G |
| G-4 | D-1 rejected alternative: a destination miss never falls back to a parent scan | PC-H |
| G-5a | D-1: confirmation only from `FirstReceiptAsync` evidence; a miss leaves the state as it was | PC-D |
| G-5b | D-1: the evidence is `IsReceipt` (identity and completeness), on the destination as on the parent | PC-I |
| G-6a | D-3: a null row-session projection passes the parent to `Prompts` and catch-up | PC-E |
| G-6b | D-3: a null row-session projection does not set `DestinationUnavailable` or any terminal state | PC-J |
| G-7 | D-4: catch-up pulls the scanned session, not the parent | PC-F |
| G-8 | D-4: before/after stamps use the scanned session's `StartedAt` (and its store read) | PC-K |
| G-9 | D-4/A-1: the context's `DestinationStatus` is the scanned session's status | PC-L |
| G-10 | D-4: `TryReuse` refuses `identity:ScanSession` against `ScanSessionId`, not `ParentSessionId` | PC-C |
| G-11 | D-4: `ContextRefusal` compares `ScanSessionId` | PC-M |
| G-12 | D-4: `TryBuildContext` sets `ScanSessionId` to the parent | PC-N |
| G-13 | D-4/evidence order step 7: the reconciler publishes a certificate whose `ScanSessionId` is the session it scanned | PC-O |
| G-14 | D-7: the reconciler never stops or fails the scanned session | PC-P |
| G-15 | Statement budget: a refused predicate issues no second session SELECT | PC-Q |
| G-16 | D-1: no invented terminal state on a destination miss | PC-R |
| G-17 | D-4 positive: a destination-bound proof with a destination stamp is admitted by the cache | PC-S |
| G-18 | D-1/CARD-0641: the destination scan floor is the keyed row's baseline | PC-U |

Guards 41 (23 + 1 + 17), mapped 41, missing 0, duplicate PC maps 0. Not guards: the V-4 document
pin (documentation; V-2 and V-3 carry the behavior), the harness helpers and the isolated-schema
constraint drop (fixture), the faulted-SELECT arm's recovery (the pre-existing catch at reconciler
lines 329-342, whose mutation family is CARD-1121's and R-5's; this card adds no guard there).

### Positive controls

Mutation runs each PC method-scoped after land: baseline, red, exact restore, green, with the
method prefix as the filter so every argument row runs and the red is the named row's assertion.
One mutation per cycle except where a batch is named. Code runs only V/R rows; Review judges this
roster before land. A zero-test run, build failure or fixture error is not a red.

- PC-B.`<row>` (23): in `ReceiptScanTarget.FollowsQueueDestination` delete only the conjunct that
  V-1 row names (for example drop the `parent != Guid.Empty` test for `parent-empty`; drop the
  `is Guid` test for `parent-null`; compare bodies `OrdinalIgnoreCase` for `body-case-differs`; drop
  the pointer-headline test; accept any `SourceLandNotificationId` for `back-pointer-other`; accept
  `Pending` for `status-pending`); expect `/*/*/ReceiptScanDestinationTests/C1157_FollowsQueueDestinationOnlyWhenEveryPositiveHolds*`
  red at that row's `ShouldBe(false, flip)`. Never batched: one file, one method.
- PC-B.positive: add `if (note.Kind == LandNotificationKind.Outcome) return false;` to the predicate;
  expect the same filter red at `positive-outcome`, `back-pointer-null` and `baseline-zero`.
- PC-A: make `FollowsQueueDestination` return false unconditionally; expect
  `/*/*/ReceiptScanDestinationIntegrationTests/C1157_SentRowDestinationReceiptConfirms*` red at
  `stopped`'s `State.ShouldBe(Confirmed)` (and `Sessions.ShouldBe([B])`).
- PC-G: in `ReconcileAsync` treat D-2 as true whenever the ids differ (skip the predicate); expect
  `/*/*/ReceiptScanDestinationIntegrationTests/C1157_RefusedFollowKeepsParentScan*` red at
  `one-kind-dispatch-base`'s `Sessions.ShouldBe([A])` and `State.ShouldBe(AwaitingReceipt)`.
- PC-H: after a null `FirstReceiptAsync` on the destination, run `Prompts`/`FirstReceiptAsync` again
  on the parent and confirm on a hit; expect
  `/*/*/ReceiptScanDestinationIntegrationTests/C1157_FollowScanNeverConfirmsFromAnotherSessionOrResemblance*`
  red at `parent-holds-prompt`'s `State.ShouldBe(AwaitingReceipt)` and `Sessions` single element.
- PC-D: on a D-2 hit set `Confirmed`, `ConfirmedAt` and `ConfirmingPromptSequence = baseline + 1`
  without calling `FirstReceiptAsync`; expect
  `/*/*/ReceiptScanDestinationIntegrationTests/C1157_FollowScanStatementBudget*` red at
  `quiet-miss`'s `State.ShouldBe(AwaitingReceipt)`.
- PC-I: in `LandNoteReceipt.IsReceipt` drop the `IsCompleteIn` conjunct; expect the method-2 filter
  above red at `head-only-on-destination`'s `State.ShouldBe(AwaitingReceipt)` (other receipt tests
  also go red; the named red is this row).
- PC-E: on a null D-3 projection still pass `row.AgentSessionId` to `Prompts` and catch-up; expect
  the budget filter red at `row-session-missing`'s `Sessions.ShouldBe([A])`.
- PC-J: on a null D-3 projection set `DestinationUnavailable` with `destination_unavailable` and
  return; expect the budget filter red at `row-session-missing`'s `State.ShouldBe(AwaitingReceipt)`.
- PC-F: on a D-2 hit call `CatchUpTranscriptAsync`/`CatchUpForReceiptAsync` with the parent id;
  expect the method-1 filter red at `running-runner-holds-prompt`'s `PullsB.ShouldBe(1)` and
  `State.ShouldBe(Confirmed)`.
- PC-K: on a D-2 hit pass `destination.StartedAt` (the parent's) to `ReceiptStamp`; expect the budget
  filter red at `quiet-miss`'s pass-2 `ReceiptSelects.ShouldBe(0)` (refusal `state:AcceptedGeneration`
  appears, which the row also asserts absent).
- PC-L: build the scan context with `destination.Status` (the parent's) instead of the scanned
  session's; expect the budget filter red at `running-quiet-miss`'s `ReceiptSelects.ShouldBe([1, 1, 1])`
  and at `parent-running-row-stopped-quiet-miss`'s `ReceiptSelects.ShouldBe([1, 0, 0])`.
- PC-C: in `TryReuse` compare `stamp.SessionId` with `context.ParentSessionId` again; expect the
  budget filter red at `quiet-miss`'s pass-2 `ReceiptSelects.ShouldBe(0)`.
- PC-M: delete the `ScanSessionId` comparison from `ContextRefusal`; expect
  `/*/*/ReceiptScanDestinationTests/C1157_ProofBindsTheScannedSession*` red at
  `proof-context-scan-session-is-bound`'s `reused.ShouldBeFalse`.
- PC-N: make `TryBuildContext` set `ScanSessionId = row.AgentSessionId`; expect the same filter red
  at `build-context-names-the-parent`.
- PC-O: publish and reuse with the context as built (never copy `ScanSessionId` to the scanned
  session); expect the budget filter red at `quiet-miss`'s pass-2 `ReceiptSelects.ShouldBe(0)`
  (refusal `identity:ScanSession`).
- PC-P: on a destination miss `ExecuteUpdateAsync` the scanned session's `Status` to `Failed`;
  expect the budget filter red at `running-quiet-miss`'s `Status.ShouldBe(Running)` after the passes.
- PC-Q: issue the D-3 projection before evaluating D-2 (unconditionally when the ids differ); expect
  the method-3 filter red at `one-kind-dispatch-base`'s `Commands.ShouldBe([6, 5, 5])` and
  `SessionProjections.ShouldBe([1, 1, 1])`.
- PC-R: on a destination miss set `note.State = Canceled` with `queue_canceled_unconfirmed`; expect
  the budget filter red at `quiet-miss`'s `State.ShouldBe(AwaitingReceipt)`.
- PC-S: in `TryReuse` refuse whenever `context.ScanSessionId != context.ParentSessionId`; expect the
  V-5 filter red at `positive-destination-scan`'s `reused.ShouldBeTrue`.
- PC-U: on a D-2 hit pass `baselineSequence: 0` (or null with a timestamp floor) to `Prompts`;
  expect the method-2 filter red at `below-floor-on-destination`'s `State.ShouldBe(AwaitingReceipt)`.

Batchable (different files and methods, no interaction): PC-M with PC-U; PC-N with PC-Q. Every
other control runs alone. PC-12 of CARD-1121 is retired (T-7).

### Out of scope

- `ExpectationSnapshotReader.IsReceivedAsync` and the dispatcher's released-seat `Prompts` calls
  (plan). The watchdog may list a followed note as debt until the reconciler confirms it; R-5 pins
  the predicate unchanged.
- The unlinked `ConflictException` (reconciler lines 84-85), one-kind retargeting, profiled
  completions, pointer bodies, null baselines (plan).
- A data repair of the 13 census rows; AppHost restart; a migration (plan D-6).
- A real standing-switch carry through `AgentControlService` and a real flush on B (declared
  substitute; those services are unchanged).
- The weak arm (bodies under 12 normalized characters), duplicate bodies on one session, and the
  row's delivery generation on the destination (§4; parity with today).
- Whole Unit/Application/assembly runs: none; the closed table below is the ordinary scope.

### Checkpoints

Managed lanes only; no production runner, no real provider. CP-1, CP-3 and CP-4 need no database.
The PostgreSQL rows own a Testcontainers PostgreSQL and an isolated schema per test. Off Windows
the checkpoint tool adds `UseAppHost=false`. Three isolated builds, one per slice; every other row
reuses its slice's build. Serial rows. Omit `-Runner` and `-Platform`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1157-policy/` | policy | `/*/*/ReceiptScanDestinationTests/C1157_FollowsQueueDestinationOnlyWhenEveryPositiveHolds*` | V-1 | 29 executed, 0 failed/skipped | 29 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1157-follow/` | pg-watermark | `/*/*/AgentTaskLandReceiptWatermarkTests/*` | V-3, R-1 | 36 executed, 0 failed/skipped | 36 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | CP-2 | policy-binding | `/*/*/ReceiptScanDestinationTests/(C1157_RuntimeInvariantNamesTheScannedSession*)\|(C1157_ProofBindsTheScannedSession*)` | V-4, V-5 | 5 executed, 0 failed/skipped | 5 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S2 | CP-2 | policy-cache | `/*/*/LandReceiptScanCacheTests/*` | R-3 | 115 executed, 0 failed/skipped | 115 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c1157-dest/` | pg-destination | `/*/*/ReceiptScanDestinationIntegrationTests/*` | V-2 | 23 executed, 0 failed/skipped | 23 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S3 | CP-5 | pg-safety | `/*/*/AgentTaskLandReceiptWatermarkSafetyTests/*` | R-2 | 18 executed, 0 failed/skipped | 18 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S3 | CP-5 | pg-receipts | `/*/Antiphon.Tests.Application/(AgentTaskLandReceiptTests*)\|(AgentTaskLandQueuedReceiptTests*)/*` | R-4 | 64 executed including all 12 C1073 results, 0 failed/skipped | 64 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S3 | CP-5 | pg-recovery | `/*/Antiphon.Tests.Application/(AgentTaskLandNotificationRecoveryTests*)\|(ExpectationNoteDebtTests*)/*` | R-5 | 50 executed, 0 failed/skipped | 50 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Run through the checkpoint tool, one run per committed slice:
`dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-09-card-1157-receipt-scan-destination-test-design.md --after S1 --expected-source-sha <S1 full sha>`,
then `--after S2`, then `--after S3`; `wait` while the exit is 75; exit 4 is a slot timeout to
report, never an unleased retry. Bootstrap the tool through `scripts/build-slot.ps1` if it is not
built; do not wrap the checkpoint run in a second slot. Commit and push before each group and do
not edit source while a run is in flight. A red row is fixed and rerun as the same row; a
production fix found in S3 reruns CP-2 through CP-4 as the same rows. Delete the three row-owned
`bin-c1157-*` outputs after the last green run. Run `scripts/check-evidence-diff.ps1` over the full
Code task range. If CARD-1163 lands first, CP-2's count stays 36 (it changes an existing arm's
fixture, not the roster).

### Cost

- Ordinary V/R floor for Code (sum of EstimatedMinutes, CP-1..CP-8) = **49 minutes**, estimated:
  three isolated builds at about 4 minutes cold (TD-1 measured the first build plus a 9-result run
  at 329 s on this mirror) or 3 minutes warm inside CP-1, CP-2 and CP-5; CP-3, CP-4, CP-6, CP-7 and
  CP-8 reuse a build, which saves five builds, about 15 minutes against building per row. Slot waits
  are outside the floor (0 s on all three diagnostic leases today). Filters and minutes are the
  table's.
- PC floor for Mutation = **143 minutes**, estimated: 27 policy-lane cycles (PC-B × 23,
  PC-B.positive, PC-M, PC-N, PC-S; no database) at 2.5 minutes (incremental build plus a sub-second
  run) = 67.5 minutes; 16 PostgreSQL cycles (PC-A, C, D, E, F, G, H, I, J, K, L, O, P, Q, R, U) at
  4.7 minutes (incremental build plus container and a 20-60 s row) = 75.2 minutes. The two named
  batches save two cycles (about 7 minutes) if Mutation takes them.
- Total = 75 (this design, measured wall-clock including the three diagnostic rows: one 329 s build
  and run, two 73-74 s runs) + 49 ordinary + 143 PC = **267 minutes**, of which the 75 is measured and
  the rest estimated. Savings: 15 build-minutes per ordinary round from build reuse; 27 of 43 PC
  cycles stay off PostgreSQL, about 59 minutes less than running them as integration cycles.

Bundle check: bodies read; guards 41, mapped 41, missing 0, duplicate PC maps 0; every PC names a
compiling defect and an exact method-prefix filter; Cost is numeric; no placeholders.

## 6. Collisions with in-flight work and slice order

| Branch | Stage | Files it touches that this card touches | Overlap |
|---|---|---|---|
| CARD-1163 (`feat/card-task-296ea015`, `test(CARD-1163): publish a proof before the cache clock faults`) | Code, based on master `59d67869b` | `tests/Antiphon.Tests/Application/AgentTaskLandReceiptWatermarkTests.cs` lines 340-436 and 550-553 (V-6 `cache-clock-fault` fixture); the CARD-1121 test design | Same file as CARD-1157 S2 (V-10 method, lines 437-501). Disjoint hunks; Git merges cleanly in either order; the class count stays 36. Dispatch S2 after CARD-1163 lands, or in parallel accepting a trivial rebase at land. |
| CARD-1161 (`feat/card-task-4b41be48`, `89b16646`) | Plan (no Code yet) | `docs/session-runtime-invariants.md` (a bullet after `C1153_Certificate_age_is_monotonic`, about line 470+); `AgentTaskDispatcher.cs`, `DeadSessionFirstSeenState.cs`, `DelegationSettings.cs`, a new test file | No source overlap. The doc edits are in different paragraphs (CARD-1157 edits lines 247-264). |
| CARD-1156 (`feat/card-task-c467dcb5`, Review) | Review | `docs/session-runtime-invariants.md` at line 642 (+25); `AttentionDtos.cs`, `AttentionService.cs`, `StandingBoot*`, `DelegationSettings.cs`, `AgentSupervisionState.cs`, client/attention strings | No source overlap; doc hunks disjoint. |
| CARD-1158 (`feat/card-task-ff9b5c25`) | Code | three unrelated test files | None. |

No in-flight branch touches `AgentTaskLandNotificationService.cs`, `LandReceiptScanCache.cs`,
`LandNoteReceipt.cs`, `LandReceiptScanHarness.cs` or `ReceiptScanRecognizer.cs` (the CARD-1121
repair branches that did are on master). The new files `ReceiptScanTarget.cs`,
`ReceiptScanDestinationTests.cs` and `ReceiptScanDestinationIntegrationTests.cs` collide with nothing.

Slice order, each on the same Code task branch, pushed and checkpointed before the next:

1. **S1 (about 40 min).** `server/Application/Services/ReceiptScanTarget.cs` (D-2);
   `tests/Antiphon.Tests/Application/ReceiptScanDestinationTests.cs` with V-1 (29 rows). The
   reconciler does not call it yet. Commit, then CP-1.
2. **S2 (about 55 min).** `AgentTaskLandNotificationService.cs` D-1, D-3, D-4 (the D-3 projection,
   scanned-session catch-up, stamps with the scanned session's `StartedAt`, context status from the
   scanned session, the `ScanSessionId` copy before certification); `LandReceiptScanCache.cs`
   (`ScanSessionId` last on `Context`, `TryBuildContext` sets the parent, `ContextRefusal` compares
   it, `TryReuse` compares the stamp to it); harness items 1-2 and 5-6 of "Missing setup"; the
   rewritten V-10 (§3); V-4 and V-5 in `ReceiptScanDestinationTests.cs`;
   `docs/session-runtime-invariants.md` per D-8 with both "destination" phrases replaced. Commit,
   then CP-2, CP-3, CP-4.
3. **S3 (about 50 min).** Harness items 3-4; `ReceiptScanDestinationIntegrationTests.cs` (five
   methods, 23 results). No production edit expected; if one is needed, rerun CP-2 through CP-4 as
   the same rows. Commit, then CP-5 through CP-8.

Code brief line:
`checkpoints: docs/superpowers/plans/2026-10-09-card-1157-receipt-scan-destination-test-design.md@<this commit> section "### Checkpoints"`.
Defaults the Code brief inherits unless overridden: T-1 through T-9 above; A's and B's `StartedAt`
one hour apart; the FK drop confined to the isolated schema; PC-12 retired.
