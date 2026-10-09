# CARD-1157: receipt scan follows the keyed row when the whitelist holds

Plan task: `24f528a8`. Date: 2026-10-09. Code inspected at
`59d67869b6a60f13510827baf1aeb499b1b93280`. Plan only. No product test was run.
**Next: code.** The verification design below is the closed checkpoint list.

`origin/master` at fetch was `59d67869b6a60f13510827baf1aeb499b1b93280`.
`git merge-tree --write-tree origin/master HEAD` at that same commit wrote
`ac429346b782aea06c81a3db42aa4d162113ad7a` and exited 0.

Owners read before Code: `docs/project-context.md`, `docs/session-runtime-invariants.md`
(the CARD-1121 receipt paragraph), `docs/testing-and-build.md` (checkpoint manifest),
`docs/resilience.md`. `GET /api/runner-defaults` (revision 2, no per-kind defaults) and
`GET /api/session-runners` were read. Checkpoints name the managed lane below. Omit
`-Runner` and `-Platform`. Do not encode a fleet location.

## Outcome

A two-kind land note whose keyed queue row was sent to a different session stays
`AwaitingReceipt`, because `ReconcileAsync` builds `LandNoteReceipt.Prompts` for
`note.ParentSessionId`. The CARD-1121 cache may then skip repeating that parent
scan (decision A-3, test V-10). This plan makes the reconciler scan the keyed
row's session when every positive condition in D-2 holds and that session row
exists, and confirm only from `LandNoteReceipt.IsReceipt` on that scan. A miss
stays open. Any other shape keeps today's parent scan.

Closing the note without a matching prompt is rejected. `Sent`, `SentAt` and an
equal body are not receipt (`C1121_RealQueueDeliveryIsNeverHiddenByTheCache`).

## Measurement

The census is `docs/investigations/2026-10-08-card-1121-receipt-scan-sample.md`
(commit `1cb99ac5` on `feat/card-task-33731d74`, now on master). It ran read-only
against the desktop `antiphon` database. This mirror has no `antiphon-postgres`
container, so that census was not re-run here. Live `GET /api/version` during
planning was `51f175dbf738519e8e50842697499c6c6c12b2c1`, which is not this branch
and does not contain this plan.

Cited census facts, metadata only:

| Fact | Value |
|---|---|
| Open linked notes with a floor | 44 |
| Of those, reaching the receipt SELECT each pass | 20 |
| Two-kind group: nonlegacy Aged and Outcome, `AwaitingReceipt`, queue `Sent`, body equal, no spill, not profiled | 13 notes, 18,072 candidate rows per pass |
| Queue destination versus parent | all 13 differ |
| Parent sessions in that window | `Stopped` or `Failed`; `MAX(Sequence)` unchanged across the window |
| Note dates | 2026-09-01 through 2026-09-30 |
| Per-kind split inside the 13 | not published. The label `Aged(1)/Outcome(3)` is the enum ordinals (`LandNotificationKind` in `server/Domain/Enums/LandingEnums.cs`) |

## Ground truth

| Question | What the code does | Consequence |
|---|---|---|
| Which session is scanned? | `AgentTaskLandNotificationService.ReconcileAsync` takes `session` from `note.ParentSessionId` (lines 63-67) and passes that id to `LandNoteReceipt.Prompts` (line 263). `Prompts` filters `AgentSessionId == session` (`LandNoteReceipt.cs` lines 39-41). | A prompt stored only on the queue row's session is not a candidate. |
| Which session does the queue row name? | `SessionQueuedMessage.AgentSessionId`. Enqueue uses the parent id (`ReconcileAsync` line 143) and sets `SourceLandNotificationId` to `note.Id` (line 146). | At enqueue the two ids are the same. |
| How can they differ afterwards? | `AgentControlService` loads `Pending` rows with `StandingQueueSwitchPolicy.NeverAttempted` (lines 568-571) and assigns `AgentSessionId` to the new session (lines 732-736). `NeverAttempted` requires `DeliveryAttempts == 0` and a null baseline (`StandingQueueSwitchPolicy.cs` lines 7-11). A later flush records `LastDeliveryBaselineSequence` as `MAX(Sequence)` of the session id it types (`SessionMessageQueueService.cs` lines 2460-2463 and 3422-3429). | A row carried while still unattempted, then sent, has its baseline on the new session. The note's `ParentSessionId` is not updated on that path. |
| What does a mismatch do on an unlinked note? | If `QueueMessageId` is null and a row with `SourceLandNotificationId == note.Id` has a different `AgentSessionId`, `ReconcileAsync` throws `ConflictException` (lines 84-85). | That path stays. D-2 applies only after the note is already keyed. |
| What does the watchdog do? | `ExpectationSnapshotReader.IsReceivedAsync` returns false when `keyed.AgentSessionId !=` the parent (lines 833-836) and does not read the other transcript. | It stays. Confirmation remains the reconciler writing `Confirmed`. |
| What does the cache require? | `LandReceiptScanCache.TryBuildContext` binds `row.AgentSessionId` and does not require it to equal the parent (lines 103-113, A-3). `TryReuse` refuses unless `stamp.SessionId == context.ParentSessionId` (lines 219-220). | A foreign row can cache a parent miss. V-10 `cached-stays-open` pins that (`AgentTaskLandReceiptWatermarkTests.cs` lines 442-478). |
| Which kinds accept two prompt kinds? | `LandNoteReceipt.AcceptsQueuedPrompt`: non-legacy `Held`, `Aged`, `Conflict`, `Outcome` (lines 17-23). Other kinds, including a legacy Outcome, take `UserPrompt` only. | The census mismatch group is inside this predicate. The one-kind rows in the same census are not described as mismatched. |

## Decisions

### D-1. Scan the keyed row's session; do not invent a terminal state

Chosen: under D-2, `Prompts` and catch-up use `row.AgentSessionId`. Confirmation
stays `FirstReceiptAsync` plus `IsReceipt`. A miss leaves the note's state as it
was. No new `LandNotificationState`, no column, no migration.

Rejected: mark the note `Canceled` or `DestinationUnavailable` because the ids
differ. That would close the census notes without a transcript prompt.

Rejected: scan the row session and, on a miss, also scan the parent. The parent
scan is the cost the census measured, and a parent prompt is not the row's delivery.

### D-2. Whitelist, in this order

`ReceiptScanTarget.FollowsQueueDestination(note, row, expectedText)` returns true
only when all of these hold. The first failure returns false. The reconciler then
scans the parent, which is today's path, including the CARD-1121 parent proof.

1. `note.ParentSessionId` is a non-empty `Guid`.
2. `row.AgentSessionId` is non-empty and different from that parent.
3. `row.SourceLandNotificationId` is null, or equal to `note.Id`. A different id returns false.
4. `LandNoteReceipt.AcceptsQueuedPrompt(note.IsLegacy, note.Kind)` is true.
5. `note.CompletionSnapshotJson` is null and `note.CompletionDeliveryJson` is null.
6. `row.Status` is `QueuedMessageStatus.Sent`.
7. `row.DeliveryAttempts > 0`.
8. `row.LastDeliveryBaselineSequence` is a `long` and `>= 0`.
9. `row.RemoteSpillBody` is null.
10. `row.Body` equals `note.Body` by ordinal comparison.
11. `row.Body` does not contain `TypedBodySpill.PointerHeadline`.
12. `expectedText` equals `note.Body` by ordinal comparison.

The reconciler passes the `expected` string it already computed (profile wire text
or owned pointer text, otherwise `note.Body`). A profiled or pointer note fails
12 or 5 and stays on the parent scan.

Null `SourceLandNotificationId` is admitted. The note is already loaded by
`note.QueueMessageId`, and migration `20260910003242_AddDurableLandDelivery` added
the column without a non-legacy backfill. The census dates start 2026-09-01.
Rejected: require a non-null equal back-pointer. That would leave a pre-column
keyed note on the parent scan. A conflicting back-pointer still refuses.

Equal ids return false so the reconciler does not issue the extra session SELECT.
The scan id is then the parent, which is the same session.

Kinds outside `AcceptsQueuedPrompt` return false. That includes legacy Outcome,
`DispatchBase`, `DeliveryFailure`, `TaskCompletion`, `LegacyCheckNote`, and an
undefined enum value. Rejected: limit the true kinds to Aged and Outcome only.
Those two are the census label; Held and Conflict share `AcceptsQueuedPrompt`.
Rejected: admit one-kind mismatches. The census does not describe them.

### D-3. A missing row-session row scans the parent

When D-2 is true, one `SELECT` of `AgentSessions` for `row.AgentSessionId`
projects `Status` and `StartedAt` (`SingleOrDefaultAsync`). Null scans the parent
and does not set `DestinationUnavailable`. The parent-existence gate (lines 67-74)
stays in front of this and still returns `DestinationUnavailable` when the parent
row is missing, before the keyed row is consulted.

### D-4. Cache A-3 stays bound-not-equal; the proof binds the scanned session

`TryBuildContext` still does not require the two session ids to be equal.

Add `Guid ScanSessionId` as the last field of `Context`. `TryBuildContext` sets
it to the parent id. `ContextRefusal` compares it. `TryReuse` refuses
`identity:ScanSession` unless `stamp.SessionId == context.ScanSessionId`
(replacing the comparison with `context.ParentSessionId` at line 220).

The reconciler, when it scans `row.AgentSessionId`, copies the context with
`ScanSessionId` set to that id before catch-up certification and `Publish`.
Catch-up, `ObserveReceiptStateAsync`, and the stamp's `StartedAt` use the scanned
session. `ReceiptScanContext` receives that session's `Status`, so A-1's Stopped
or Failed check applies to the scanned session. A Running row destination is
scanned and can confirm, and `TryBuildContext` refuses a proof for it.

A proof whose `ScanSessionId` is the parent does not reuse for a later scan of
the row session. The cache still does not confirm a note. A metrics or cache
fault does not change the scan target.

### D-5. V-10's "stays open" arm is a deliberate assertion change

`C1121_ForeignDestinationNoteStaysOpenExactlyAsToday` currently expects the
complete body on session B to leave the note `AwaitingReceipt` while the scan
reads parent A, with and without a cache. After D-1 that fixture is the whitelist
shape (the harness must set `SourceLandNotificationId` to the note id, which
production enqueue does and the harness does not today). The method is renamed
`C1121_ForeignDestinationFollowsKeyedRow`. Successor assertions:

| Arm | Arrange | Expect |
|---|---|---|
| `row-destination-confirms` | Row on B, body ingested on B above the floor, parent A Stopped, both stores warm, runner NotFound | Pass 1 `Sessions` is `[B]`, `State` is `Confirmed`, `ConfirmingPromptSequence` is B's prompt sequence. A second service with no cache, same fixture, confirms the same columns. `Publishes` is 0 |
| `parent-equals-row-destination` | `ParentSessionId` set to B before any pass, row already on B | Scans `[B]`, confirms at B's sequence. D-2 is false because the ids are equal |
| `row-equals-parent-miss` | Row `AgentSessionId` set to A before any pass, body only on B | Scans `[A]`, receipt rows 48, stays `AwaitingReceipt`, one new proof |

The old `cached-stays-open` outcome is replaced by `row-destination-confirms`
plus the refused-follow test in V-2. The old `parent-rehomed-to-row-destination`
and `row-destination-changed` arrangements ran after three mismatch passes.
Those passes would confirm under D-1, so each arm is arranged on its own.

CARD-1121 PC-12 mutated production to scan `row.AgentSessionId` and expected
`cached-stays-open` to go red. That mutation is this card's behavior for the
whitelist. PC-12 is retired here. The successor is PC-A below. Do not leave
PC-12 as a required red on the new assertion.

### D-6. No migration. Restart activates the server process

No EF migration and no settings change. The reconciler and the process-local
cache run in the server. A running process keeps scanning the parent until it is
restarted onto this build. This plan does not restart AppHost. The
session-runtime paragraph already says a new process starts the cache empty.

### D-7. Do not stop or fail a Working session

Catch-up of a Running row destination is the existing pull. This plan does not
call a runner stop, does not set a session `Failed`, and does not park. CARD-0079
is unchanged. This plan does not add a session that waits for input, so the
CARD-1083 parking checklist does not apply.

### D-8. Amend one invariant paragraph

In `docs/session-runtime-invariants.md`, in the CARD-1121 receipt paragraph,
replace "the destination's `StartedAt`" with "the scanned session's `StartedAt`"
and "a Stopped or Failed destination" with "a Stopped or Failed scanned session",
and add this sentence immediately after that paragraph:

`The scanned session is the keyed row's AgentSessionId when ReceiptScanTarget.FollowsQueueDestination is true and that session row exists; otherwise it is the note's ParentSessionId (CARD-1157).`

`ExpectationSnapshotReader.IsReceivedAsync` is not edited.

## Evidence order

For a note that already has `QueueMessageId` and `DeliveryAttempts > 0`:

1. Parent row exists, or the existing `DestinationUnavailable` return.
2. Load the keyed row. Resolve `expected` exactly as today.
3. Evaluate D-2. False: `scanSession` is the parent, `scanStatus` and `StartedAt` are the parent projection already loaded. No second session SELECT.
4. D-2 true: the D-3 SELECT. Null: `scanSession` falls back to the parent.
5. Catch-up `scanSession`, then the existing before-stamp, `FirstReceiptAsync`, and after-stamp. The stamp's `StartedAt` is the scanned session's.
6. A hit sets `Confirmed`, `ConfirmedAt`, and `ConfirmingPromptSequence` as today. A miss does not change `State`.
7. `Publish` only for a negative exhausted scan whose context `ScanSessionId` is `scanSession`, after the final save, under the existing certificate rules.

## Statement budget

Equal ids, two-kind, runner NotFound, warm store: `C1121_UnchangedTranscriptSkipsOnlyReceiptSelect` arm `two-kind-outcome-404` stays `[6, 5, 5]` commands and receipt selects `[1, 0, 0]`. D-2 false adds no SELECT.

Derived pins for a warm store and runner NotFound, counted the same way as that test (fixture setup excluded). A different count fails the checkpoint. Name the extra command in a plan correction. Do not raise the pin in the same dispatch.

| Arm | Commands | Receipt selects | Receipt session |
|---|---|---|---|
| Whitelist, prompt on B, B Stopped | `[7, 2, 2]` | `[1, 0, 0]` | `[B]` on pass 1. Passes 2 and 3 are the confirmed early return (fetch + reload) |
| Whitelist, no prompt on B, B Stopped | `[7, 6, 6]` | `[1, 0, 0]` | `[B]`. The extra command on every pass is the row-session SELECT. Reuse omits the receipt SELECT |
| Whitelist true, B's session row missing | `[7, 6, 6]` | `[1, 0, 0]` | `[A]`. Fallback parent scan, parent proof allowed |
| D-2 false (one flip) | `[6, 5, 5]` | `[1, 0, 0]` | `[A]` |

The `+1` against the 6/5/5 pin is that one `AgentSessions` projection. Catch-up on NotFound adds no transcript SQL, matching the landed 404 pin.

## Verification design

### V-1. One flip of the predicate

Unit, no database. `ReceiptScanDestinationTests.C1157_FollowsQueueDestinationOnlyWhenEveryPositiveHolds(string flip)`.

Positive fixture: non-legacy Outcome, parent A, row on B, `SourceLandNotificationId` null, `Sent`, attempts 1, baseline 10, bodies equal, `expectedText` equal to the body, no spill, no snapshot. Returns true. Each other row changes one member from a fresh copy.

| Row | Returns |
|---|---|
| `positive-held`, `positive-aged`, `positive-conflict`, `positive-outcome`, `back-pointer-null` | true |
| `kind-dispatch-base`, `kind-delivery-failure`, `kind-task-completion`, `kind-legacy-check-note`, `kind-undefined`, `is-legacy`, `same-session`, `parent-empty`, `queue-destination-empty`, `status-pending`, `status-canceled`, `attempts-zero`, `baseline-null`, `baseline-negative`, `body-one-char`, `body-case-differs`, `pointer-headline`, `remote-spill-body`, `completion-snapshot`, `completion-delivery`, `back-pointer-other`, `expected-text-differs` | false |

27 argument rows. `kind-undefined` is `(LandNotificationKind)999`.

### V-2. The reconciler uses the predicate

Integration, `LandReceiptScanHarness`, `TUNIT_MAX_PARALLEL_TESTS=1`.

`ReceiptScanDestinationIntegrationTests.C1157_SentRowDestinationReceiptConfirms(string destination)`:

- `stopped`: B Stopped, runner NotFound, warm store, complete `UserPrompt` on B above the floor. Confirmed at that sequence. `Sessions` is `[B]`. No-cache control confirms the same note columns. `Publishes` is 0. Commands `[7, 2, 2]`.
- `running`: B Running, same prompt. Confirmed at that sequence. `Publishes` is 0. `Runner.KillCalls` is 0. The note's `LastErrorCode` does not start with `notification_reconcile_failed`. Command total is not pinned (a live catch-up adds the reads V-3 already counts on the parent).

`ReceiptScanDestinationIntegrationTests.C1157_RefusedFollowKeepsParentScan(string flip)`:

- `one-kind-dispatch-base`, `status-pending`, `back-pointer-other`.
- Complete body is on B. Three passes: `Sessions` is `[A]`, state `AwaitingReceipt`, `ConfirmedAt` null, commands `[6, 5, 5]`, receipt selects `[1, 0, 0]`. `KillCalls` is 0. The keyed row's status, `SentAt` and attempts are unchanged.

`ReceiptScanDestinationIntegrationTests.C1157_FollowScanStatementBudget(string arm)`:

- `confirm`, `quiet-miss`, `row-session-missing`, with the table in Statement budget.
- `quiet-miss` also asserts one proof whose scanned session is B: pass 2's receipt selects are 0. `row-session-missing` asserts the receipt session is A.

Harness note: set `SourceLandNotificationId = note.Id` on the confirm and quiet-miss fixtures so the row matches production enqueue. `back-pointer-null` in V-1 is the unit row that admits null. The integration confirm fixture uses the non-null production value.

### V-3. Rewritten foreign-destination method

`C1121_ForeignDestinationFollowsKeyedRow` arms in D-5. Existing
`C1121_UnchangedTranscriptSkipsOnlyReceiptSelect` is not edited and is re-run.

### V-4. Invariant sentence

`ReceiptScanDestinationTests.C1157_RuntimeInvariantNamesTheScannedSession` asserts
`docs/session-runtime-invariants.md` contains the D-8 sentence and the two replaced
phrases. It does not prove behavior. V-2 and V-3 do.

### Production mutation controls

Mutation stage, after land, method-scoped, one production edit at a time, then
restore. Code does not run these. A zero-test or build failure is not a red.

| PC | Production edit | Filter | Red |
|---|---|---|---|
| PC-A | `FollowsQueueDestination` returns false unconditionally | `/*/*/ReceiptScanDestinationIntegrationTests/C1157_SentRowDestinationReceiptConfirms*` | `stopped` stays `AwaitingReceipt` or `Sessions` is not `[B]` |
| PC-B.`<row>` | Delete only the conjunct that V-1 row names | `/*/*/ReceiptScanDestinationTests/C1157_FollowsQueueDestinationOnlyWhenEveryPositiveHolds*` | That false row returns true. One cycle per false row. Do not batch |
| PC-C | `TryReuse` compares `stamp.SessionId` to `ParentSessionId` again | `/*/*/ReceiptScanDestinationIntegrationTests/C1157_FollowScanStatementBudget*` | `quiet-miss` pass 2 receipt selects are not 0 |
| PC-D | On a D-2 hit, set `Confirmed` without calling `FirstReceiptAsync` | same budget filter | `quiet-miss` is `Confirmed` |
| PC-E | On a null row-session SELECT, still pass `row.AgentSessionId` to `Prompts` | same budget filter | `row-session-missing` receipt session is not `[A]`, or the note's error starts with `notification_reconcile_failed` |

PC-B's positive rows are witnessed by the inverse: make `AcceptsQueuedPrompt` reject `Outcome` and `positive-outcome` returns false.

### Out of scope

- `ExpectationSnapshotReader.IsReceivedAsync` and released-seat `Prompts` calls.
- The unlinked `ConflictException` at `ReconcileAsync` lines 84-85.
- Retargeting one-kind notes, profiled completions, pointer bodies, and null baselines.
- A data repair that updates `ParentSessionId` on the 13 existing rows. D-1 confirms them on a later pass after restart when D-2 matches, without rewriting the parent id.
- AppHost restart, fleet placement, and a new migration.

### Checkpoints

Closed list. One isolated build per build row. Off Windows the checkpoint tool
adds `UseAppHost=false`. Serial rows. Managed policy lane has no database.
Managed PostgreSQL lane uses the harness's own container and an isolated schema.
No production runner and no real provider.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1157-policy/` | policy | `/*/*/ReceiptScanDestinationTests/C1157_FollowsQueueDestinationOnlyWhenEveryPositiveHolds*` | V-1 | 27 executed, 0 failed/skipped | 27 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1157-follow/` | pg-follow | `/*/*/ReceiptScanDestinationIntegrationTests/*` | V-2 | 8 executed, 0 failed/skipped | 8 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | CP-2 | pg-v10 | `/*/*/AgentTaskLandReceiptWatermarkTests/(C1121_ForeignDestination*)\|(C1121_UnchangedTranscript*)` | V-3 | 9 executed, 0 failed/skipped | 9 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S2 | CP-2 | doc-pin | `/*/*/ReceiptScanDestinationTests/C1157_RuntimeInvariantNamesTheScannedSession*` | V-4 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost

Ordinary Code floor is 27 minutes (5 + 12 + 8 + 2). `-ExpectAbout` adds the slice
authoring below. Code brief line:
`checkpoints: docs/superpowers/plans/2026-10-09-card-1157-receipt-scan-destination-plan.md@<plan sha> section "### Checkpoints"`.

Run `dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this file> --after S1` after the S1 commit, then `--after S2` after the S2 commit. Bootstrap the tool through `scripts/build-slot.ps1` if it is not already built. Do not wrap the checkpoint run in a second slot. Await `wait` while the exit is 75.

## Slices

1. **S1 (about 40 min).** Add `server/Application/Services/ReceiptScanTarget.cs` with `FollowsQueueDestination` implementing D-2. Add `tests/Antiphon.Tests/Application/ReceiptScanDestinationTests.cs` with V-1 only. The reconciler does not call it yet. Commit, then CP-1.

2. **S2 (about 55 min).** Wire `ReconcileAsync` to D-1, D-3 and D-4. Add `ScanSessionId` on the cache context and the `TryReuse` comparison. Amend `docs/session-runtime-invariants.md` per D-8. Add V-2 and V-4. Rewrite the V-10 method per D-5. Commit, then CP-2, CP-3 and CP-4.

Files Code touches: `ReceiptScanTarget.cs` (new), `AgentTaskLandNotificationService.cs`, `LandReceiptScanCache.cs`, `docs/session-runtime-invariants.md`, `ReceiptScanDestinationTests.cs` (new), `ReceiptScanDestinationIntegrationTests.cs` (new), `AgentTaskLandReceiptWatermarkTests.cs`.
