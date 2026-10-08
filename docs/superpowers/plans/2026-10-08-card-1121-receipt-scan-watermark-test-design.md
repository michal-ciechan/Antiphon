# CARD-1121 test design: fail-closed receipt scan cache

TestDesign task `7a8b3e64` for
`docs/superpowers/plans/2026-10-08-card-1121-receipt-scan-watermark-plan.md` (master
`302b870fe`) and the measured sample
`docs/investigations/2026-10-08-card-1121-receipt-scan-sample.md` (this branch,
`1cb99ac53`). Baseline for every source citation: `1cb99ac53a13e911105a6a6d4999c7c67522387e`
(the plan inspected `b5e78700`; none of the cited files changed between them). Production
code was not edited. This file is the checkpoint `--plan` input for Code: the tool reads the
first `### Checkpoints` table below. The fix design D-1..D-9 stands; §2 records the four
amendments the measured sample and the brief's fail-closed rule force, and every test below
pins the amended rule, not the plan's literal text where they differ.

Fail-closed rule every test pins: a cache may skip a receipt scan **only** under the
positive whitelist in §3 (transcript committed state frozen and unchanged, destination
session terminal, note/attempt/payload identity unchanged); anything unknown keeps today's
scan; a cache can never hide a prompt that could match; telemetry and clock faults never
change an outcome; the cache is in-memory, so a restart rescans.

## 1. Measurement gate verdict and the sample's open questions

**Verdict: proceed to Code.** The sample measured 180 repeated-miss receipt SELECTs per
72.6 s (11,963 ms execution, 223,965 Text rows) over 20 never-matching notes whose parent
sessions are all Stopped/Failed with unchanged `MAX(Sequence)`. The repeated cohort is the
quiet, unchanged, equal-body, non-profiled, non-spilled cohort the whitelist targets. Under
the amendments in §2 the whitelist covers 19 of 20 notes and 100 % of returned rows. Under the
plan's literal D-4 it covers **0 notes** (A-2), and under a strict reading of D-3's "same
destination" it covers 7 notes and 27 % of rows (A-3); both facts were invisible to the plan,
which was written before the sample.

**Q1, legacy Outcome notes with a null delivery verdict: eligible.** D-3 admits either legacy
mode (bound in the key) and a null verdict; `LandNoteReceipt.AcceptsQueuedPrompt` makes the
legacy note one-kind (`UserPrompt` only), which is the measured `t."Kind" = $3` shape.
Pinned by V-3 arm `one-kind-legacy-outcome-null-verdict-404` (cached, zero receipt SELECTs on
warm passes) and by V-1 rows `verdict-null-vs-delivered` (null is bound as identity, not a
wildcard) and `positive-verdict-null`.

**Q2, pointer-headline and oversized-text exclusions.** The pointer exclusion cannot bite on
the measured cohort: the census found `row.Body = note.Body` for all 19 eligible notes, and a
spilled row carries the pointer headline plus inbox path instead of the body
(`AgentTaskLandNotificationService.cs` lines 199-203), so none of them is a pointer row. The
oversized-text exclusion is **not settled by the sample**: the census recorded no body
lengths, and body equality only bounds the body by the backend's single-write ceiling
(`DelegationSettings.ModernPtySingleWriteMaxBytes` = 86,400 bytes on the modern ConPty backend;
1,024 bytes on InboxConhost), so an equal body can still exceed D-6's 4,096-character cap.
The fixture Outcome body measures 3,166 characters. Required before the Code brief, from the
database-owner lane, read-only, metadata only (no text):

```sql
SELECT n."Kind", n."IsLegacy", length(n."Body") <= 4096 AS within_cap, count(*)
FROM "AgentTaskLandNotifications" n
WHERE n."State" NOT IN (3, 4, 7) AND n."QueueMessageId" IS NOT NULL
GROUP BY 1, 2, 3 ORDER BY 1, 2, 3;
```

Design response: the cap is one named constant, `LandReceiptScanCache.MaxExpectedTextChars`,
tested at its exact boundary (V-1 `positive-text-at-cap`, `text-over-cap`; V-2
`text-at-cap-admitted`, `text-over-cap-refused`; V-6 `oversized-body`), so changing it is a
one-constant Code-brief amendment. If the read shows eligible bodies above 4,096 characters,
set the cap to 16,384 and `MaxProofs` to 256 (the same 8 MiB retained-text bound: 256 × 16,384
× 2 bytes; the production cohort is 20 notes). An over-cap note keeps today's scan every pass
and never produces a wrong outcome.

**Q3, the TaskCompletion note.** It is profiled (`CompletionSnapshotJson` present), so D-3
excludes it; it reaches the SELECT with zero candidate rows (9 calls per 72.6 s, 0 rows) and
pays the existing `StartedAt` generation SELECT (lines 232-233) before catch-up. It stays on
today's path at negligible cost. Pinned by V-6 arm `profiled-completion` (scans every pass,
no proof, 7 commands). An unprofiled TaskCompletion (CARD-0527 commit-outcome note) is admitted
by D-3 and pinned by V-1 `positive-task-completion-unprofiled` and V-3
`task-completion-unprofiled-404`.

**Out of scope, cited only: the 13 two-kind notes whose queue rows were delivered to a
session other than `note.ParentSessionId`.** The reconciler scans the parent, so a receipt can
never appear in the scanned transcript. That is a separate defect card (destination
re-homing or terminal classification). This design makes the cache unable to mask it: V-10
proves such a note stays `AwaitingReceipt` with `ConfirmedAt` null exactly as today, never
confirms from the row's session even when that session holds the complete prompt, and rescans
the moment either session id changes; PC-12 proves the test detects a scan of the wrong
session.

## 2. Plan amendments forced by the sample and the brief

- **A-1, destination must be terminal (whitelist addition).** The brief's rule names "session
  terminal" as a positive condition; the plan did not. Bind the destination `AgentSession.Status`
  ∈ {Stopped, Failed} into the proof context, read in-pass by projecting `Status` and
  `StartedAt` from the existing destination SELECT (`AgentTaskLandNotificationService.cs`
  line 55: `AnyAsync` becomes `Select(s => new { s.Status, s.StartedAt }).SingleOrDefaultAsync`;
  null keeps `destination_unavailable`). Running, Starting or any other value: no reuse, no
  certificate. Zero additional commands. The measured cohort is 100 % terminal; a live
  destination's runner remains the authority and keeps today's full scan.
- **A-2, terminal-branch observation (D-4 amendment).** For a non-live session the runner's
  `GET /sessions/{id}/transcript` throws `KeyNotFoundException`, the runner maps it to 404
  (`src/Antiphon.SessionRunner/SessionReadLaunchRoutes.cs`), `ReadRequiredJsonAsync` throws, and
  `CatchUpTranscriptAsync` swallows it and returns false without touching the store. Under D-4's
  literal text ("a failed pull permits no reuse/certificate") no note in the measured cohort
  could ever be cached. Amendment: the pull is still attempted every pass and never skipped;
  for a destination that passed A-1 its failure is the expected answer, not "unknown". The
  observation is the runtime-owned store read **after** the pull: store present and enabled,
  `ReadAsync` Ready, non-empty `ServerEpoch`, `Revision > 0`, `AcceptedGeneration` non-null and
  `SessionGeneration.Equal` to the `StartedAt` projected by A-1, no retained
  `TryGetTranscriptPersistFailure`, and no `NeedsReload` from a persist in this pass. Anything
  else is unknown and keeps today's scan. Safety rests on the single-writer contract the plan
  already relies on: runtime ingest publishes under the gate (measured: one ingest moved the
  snapshot from revision 2 to 3), retention and cascade deletion reseed through the mutation
  fence (measured: one fenced delete moved revision 2 to 3 and reset epoch 0 to 1), and
  out-of-process SQL keeps the owner's restart requirement, which empties the cache.
- **A-3, keyed-row destination is bound, not required equal.** D-3's "a real keyed row in the
  same destination" is read as: the row named by `note.QueueMessageId` exists and its
  `AgentSessionId` is bound into the context (D-5), while the scan stays over
  `note.ParentSessionId` as today. Chosen because the brief states 100 % row coverage; the
  strict reading excludes the 13 two-kind notes (18,072 of 24,885 rows per pass). If the
  orchestrator chooses strict equality, exactly one expected value flips (V-10
  `cached-stays-open` becomes "scans every pass, stays open") and the savings figure becomes
  6,813 rows per pass.
- **A-4, statement budgets measured, not derived.** The plan's 9/8 commands assume a live pull
  returning one committed row. The cohort's pull fails, so the quiet pass is 6 commands today
  and 5 on reuse. Both shapes are pinned in §4; the live-row shape stays 9/8.

- **A-5, Code S1 (task `56c39ac6`), operator decisions applied.** Q2 takes the one-constant
  fallback: `MaxExpectedTextChars = 16384` and `MaxProofs = 256` (same 8 MiB bound); every
  boundary row is written against the constants, and V-2 `capacity-1025th-evicts-oldest` is
  named `capacity-overflow-evicts-oldest` (`MaxProofs + 1` notes). W-6's `ResetEpoch >= 0`
  guard gets its own V-1 state row `reset-epoch-negative` (V-1 96 rows; CP-1 108 results).
  Eligibility and state rows apply the flip to **both** sides so the named guard alone refuses
  (a reuse-side-only flip would also be refused by the context or stamp comparison and could not
  go red on that guard's mutation). `note-id` refuses as `no-proof`: `NoteId` is the proof's
  key. The `CountingReader` promotion moves to S2, its first consumer, so S1 edits no R-1 file.

Two implementation seams the amendments and tests need, within the plan's S1/S2 scope:
new `LandDeliveryBoundary` names `receipt-scan-before-stamp` (after catch-up and the
observation, before the before-stamp read), `receipt-scan-exhausted` (after
`FirstReceiptAsync` returned null and its enumerator was disposed, before the after-stamp
read) and `receipt-scan-before-certificate` (after the final `SaveChangesAsync` succeeded,
before `Publish`), beside the existing `receipt-before-save`; and an optional
`LandReceiptScanCache? scanCache = null` constructor parameter on
`AgentTaskLandNotificationService` so the existing hand-built callers compile unchanged.

## 3. The whitelist every test pins (positive conditions, in the reconciler's evidence order)

| W | Positive condition (all required) | Pinned by |
|---|---|---|
| W-1 | Note reloaded; state is Queued, RetryPending, AwaitingReceipt, DestinationUnavailable or Canceled (enumerated, not `Enum.IsDefined`); Confirmed, NotRequired, LegacyUnverified and undefined values refuse. | V-1 eligibility rows, V-3 |
| W-2 | Kind is Held, Aged, Conflict, Outcome, DispatchBase, DeliveryFailure or unprofiled TaskCompletion (enumerated); LegacyCheckNote and undefined values refuse; `IsLegacy` is bound either way. | V-1, V-3, V-6 |
| W-3 | Destination session exists and its `Status` is Stopped or Failed (A-1); `StartedAt` captured in the same SELECT. | V-1, V-6, V-11 |
| W-4 | Keyed row exists; `row.Body` ordinal-equal to `note.Body`; no pointer headline; `RemoteSpillBody` null; no `CompletionSnapshotJson`/`CompletionDeliveryJson`; expected text is exactly that body; text length ≤ `MaxExpectedTextChars`; `DeliveryAttempts > 0`; `LastDeliveryBaselineSequence` non-null and ≥ 0 (timestamp-only baselines refuse); queue status Pending, Sent or Canceled; verdict null or one of the eleven named values. | V-1, V-6 |
| W-5 | Catch-up pull attempted this pass (never skipped on a cache hit); for a W-3 destination its failure is acceptable (A-2); a retained persist failure or a `NeedsReload` persist this pass refuses. | V-3, V-6, V-7 |
| W-6 | Store present and enabled; snapshot Ready, `ServerEpoch` non-empty, `Revision > 0`, `ResetEpoch ≥ 0`, `AcceptedGeneration` equal to W-3's `StartedAt`, `Count ≥ 0`, `LastSequence ≥ Count`. | V-1 state rows, V-6 |
| W-7 | An unexpired proof (< 5 minutes from the completed scan's monotonic timestamp; hits never extend) whose context equals every bound member (NoteId, QueueMessageId, ParentSessionId, row destination, `SourceLandNotificationId`, IsLegacy, Kind, note state, queue status, verdict, attempts, baseline, `LastDeliveryStartedAt`, `LastDeliveryGeneration`, destination status, exact text) and whose stamp equals the current W-6 stamp member for member (SessionId, ServerEpoch, Revision, ResetEpoch, AcceptedGeneration, Count, LastSequence). Null equals only null. | V-1 identity/attempt/payload/certificate rows, V-4, V-5 |
| W-8 | A proof is published only from a completed no-match enumeration whose before-stamp equals its after-stamp, after the final `SaveChangesAsync` succeeded, with no exception or cancellation; a match, a pointer-hash failure after a match, a reader fault, or a failed save publishes nothing. | V-7, V-8 |
| W-9 | On a hit the reconciler still performs catch-up, the keyed-row lookup, the existing error/status bookkeeping and the final note UPDATE; only `FirstReceiptAsync` is omitted. | V-3, V-9 |
| W-10 | Any exception from the cache itself (lookup, publish, clock, metrics) is caught and falls back to today's scan; cancellation propagates. | V-6 `cache-clock-fault`, V-7 |

## 4. Statement budgets

Measured on this mirror (diagnostic runs in §6) against today's tree, prelinked non-legacy
Outcome note (two-kind), 48 candidates above floor 10 plus one below it, destination Stopped,
store registered and coherent (49 rows in both):

| Measured today | Commands | Roster |
|---|---:|---|
| Quiet pass, runner answers 404/empty | 6 | note SELECT, note reload SELECT, destination SELECT, queue-row SELECT, receipt SELECT (48 rows), note UPDATE (`ConcurrencyToken`) |
| Quiet pass, runner returns one already-committed UUID row | 9 | the 6 above plus `StartedAt` SELECT, `session-state.identity` SELECT, `MAX(Sequence)` |
| Confirming pass | 6 | as quiet; the UPDATE carries `ConfirmedAt`, `ConfirmingPromptSequence`, `State` |
| Runtime ingest of one row (warm store) | 4 | `StartedAt`, identity, `MAX`, INSERT; snapshot revision +1 |
| Runtime ingest batch of 49 rows (cold store) | 5 | `session-state.seed`, `StartedAt`, identity, `MAX`, one batched INSERT; snapshot Count 49, LastSequence 58 |
| Fenced delete of one row (`BeginMutationAsync` + `PublishCommittedAsync`) | 2 | DELETE, `session-state.seed`; revision +1, reset epoch +1, Count 48 |
| Warm `SessionStateStore.ReadAsync` | 0 | |
| Measured receipt SQL | | `SELECT t."Sequence", t."Text" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__session_0 AND t."Text" IS NOT NULL AND t."Kind" IN ('UserPrompt', 'QueuedUserPrompt') AND t."Sequence" > @__floor_1 ORDER BY t."Sequence"` (one-kind: `t."Kind" = 'UserPrompt'`) |

Pinned with the cache (V-3, V-9). Counts are production commands from every fixture scope
(test context, harness scoped contexts, loader scope), reads and writes together; fixture
setup, fixture assertion reads and transaction protocol are excluded by resetting after
seeding and by never reading inside the measured window. The destination SELECT shape changes
from `SELECT EXISTS(...)` to `SELECT a."Status", a."StartedAt" ... LIMIT 2` and stays one
command.

| Case | Receipt commands / client rows | All production commands |
|---|---|---|
| Cold store, first negative scan, 404 runner | 1 / 48 | **7** (6 + the one `session-state.seed` of the observation read) |
| Warm store, first negative scan, 404 runner | 1 / 48 | **6** |
| Same context and stamp, passes 2 and 3 | 0 / 0 each | **5** each; three-pass total 16 warm (17 cold), one receipt SELECT, 48 rows, one proof published, two hits |
| Live pull returning a committed row (terminal session the runner still retains), first then reuse | 1 / 48, then 0 / 0 | **9**, then **8** |
| Running destination (A-1), otherwise identical | 1 / 48 each | **6** each (9 with the live row); zero cache-support commands; proofs 0 |
| New matching commit between passes (ingest counted separately: 4) | 1 / rows through first match | **6**; confirmation sequence equals the in-memory oracle |
| Excluded: pointer row, oversized body, timestamp-only baseline, LegacyCheckNote | 1 / 48 each | **6** each |
| Excluded: profiled TaskCompletion | 1 / 48 each | **7** each (its generation SELECT) |
| One-kind legacy Outcome with 852 candidates; two-kind with 1,506 | 1 / 852 and 1 / 1,506, then 0 / 0 | 6 then 5; command count does not grow with rows |
| Unprofiled TaskCompletion, generation equal | 1 / 48, then 0 / 0 | **7**, then **6** |

Receipt-SELECT recognizer (closed): optional first line `-- land-note.receipt-scan`; text
begins `SELECT t."Sequence", t."Text" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @`;
contains `t."Text" IS NOT NULL`, either `t."Kind" = 'UserPrompt'` or
`t."Kind" IN ('UserPrompt', 'QueuedUserPrompt')`, and `t."Sequence" > @`; ends with
`ORDER BY t."Sequence"`; contains no `LIMIT`, `ToolInput`, `ApiErrorTimeZoneId` or
`ModelCalls`. The `session-state.seed` statement also orders by Sequence inside LATERAL joins
and must not be counted (it has `LIMIT 1` and the tag). The floor parameter is read from the
captured `NpgsqlParameter` and must equal `row.LastDeliveryBaselineSequence` on every scan.

Post-land acceptance window (plan "After implementation"): in a 72.6 s window after one full
pass has warmed the cache, the two receipt query IDs rediscovered by normalized SQL must show
delta calls ≤ 9 (the profiled TaskCompletion note only) and delta rows 0; receipt outcomes
unchanged; database CPU reported separately.

## 5. Verification design

### Inspection

Bodies read in full: `server/Application/Services/LandNoteReceipt.cs`,
`AgentTaskLandNotificationService.cs`, `SessionStateStore.cs`, `SessionStateSnapshot.cs`,
`LandDeliveryBoundary.cs`, `server/Infrastructure/Data/SessionStateLoader.cs` (seed SQL and
`LoadAsync`), `SessionStateCommandMetrics.cs`, `server/Application/Settings/SessionStateSettings.cs`,
`src/Antiphon.SessionRunner.Contracts/SessionGeneration.cs`, domain
`AgentTaskLandNotification.cs`, `SessionQueuedMessage.cs`, `DeliveryVerdict.cs`,
`QueuedMessageStatus.cs`, `LandingEnums.cs`; `AgentSessionRuntime.cs` constructors 56-99,
catch-up/sync/persist/publication 690-870 and persist core 880-1110;
`AgentTaskLandNotificationHostedService.cs` paging 97-121; `DataRetentionService.MutateTranscriptAsync`
309-329; `TypedBodySpill.cs` 1-60 with `SessionMessageQueueService.cs` 360-400 and
`DelegationSettings.cs` ceilings 180-300; `SessionRunnerHttpClient.GetTranscriptAsync` and
`ReadRequiredJsonAsync`; `SessionReadLaunchRoutes.cs` transcript route and 404 mapping;
`TaskCompletionNotification.IsProfiled`, `SerializeDelivery`, `Sha256`;
`LandNotificationPayload.Create`. Tests and fixtures: `AgentTaskLandReceiptTests.cs`
(`SeedAsync`, `C467_V12`, `C467_V13`), `AgentTaskLandReceiptTests.Scan.cs` (whole, including
`ReceiptScanProbe`/`CountingReader`), `AgentTaskLandQueuedReceiptTests.cs` (whole,
`ReceiptFixture`), `SessionStateCommitTests.cs` (whole, `SaveGate`/`SaveFault`/`RejectRow`/
`RacingRow`), `AgentTaskLandNotificationRecoveryTests.cs`/`.H5.cs` and
`ExpectationNoteDebtTests.cs` (rosters, heads), `RemoteCompletionSpillTests.cs` (profiled
delivery JSON builder, lines 199-231 and 402-436); helpers `BridgeQueueHarness.cs` (whole,
`EmptyRunnerClient` 685-722), `SessionStateTestFixture.cs`, `FullCommandCounter.cs`,
`CountingCommandInterceptor.cs`, `QueuedReceiptAssertions.cs`, `SaveSnapshotInterceptor.cs`,
`ThrowOnceSaveInterceptor.cs`, `ControlledTimeProvider.cs`, `TranscriptHotPathFixture.cs`
(`TranscriptCommandCapture`), `ScriptedSessionRunnerClient.cs` and `FakeSessionRunnerClient.cs`
transcript members. Documents: the plan, the sample, `docs/testing-and-build.md` (manifest,
runner tool, build slots, PC execution), `docs/session-runtime-invariants.md` (committed
projection and receipt bullets), `2026-10-08-card-1153-test-design.md` (house format).

Boundaries and where each lands: lifetime at exactly 5 minutes and one tick before (V-2);
capacity 1,024 versus 1,025 (V-2); text 4,096 versus 4,097 (V-1, V-2, V-6); `Revision` 0 and 1
(V-1); `Count`/`LastSequence` consistency (V-1); null versus value for `LastDeliveryStartedAt`,
`LastDeliveryGeneration`, verdict (V-1); floor equal versus one below (V-5
`lower-baseline-includes-existing-match`); runner sequence rebase to `max + 1` (V-4); every
`SessionStateReadiness` value (V-1); every `SessionStatus` on the destination (V-1 Stopped,
Failed, Running, Starting, undefined; V-6 Running/Starting); every `DeliveryVerdict` value plus
null (V-1); undefined enum values for kind, state, status, verdict (V-1); 48, 852 and 1,506
candidates (V-9); cold versus warm store (V-3, V-9); reader fault on the second row (V-7); gate
held by a writer (V-7); same-cache fresh scope versus new singleton (V-2, V-8). Excluded
boundaries: the 512-UUID identity chunk (fixture seeding only; a 1,506-row ingest crosses it
incidentally and is not asserted); `WarmupBatchSize`/`MaxSessions` store pressure (store
owner's tests; a non-admitted entry reseeds every read, which can only produce a miss).

Missing setup recorded (the new harness must supply all of it; none exists today):

1. `BridgeQueueHarness` does not register `SessionStateStore`; the runtime's `_states` is null
   there. The harness registers `IOptions<SessionStateSettings>`, `ISessionStateLoader` →
   `SessionStateLoader`, `SessionStateStore` and `LandReceiptScanCache` through
   `ConfigureServices` (last registration wins; measured working).
2. Transcript candidates must be fed through `Runtime.PersistTranscriptAsync` in one batch
   **before** the enqueue pass. Measured: the enqueue pass warms the store, and 49 rows
   inserted by direct SQL afterwards left the snapshot at `Count 0` while the database held 49.
   The harness asserts coherence (`store.Count == database count`) before the cold scan and
   fails loudly otherwise.
3. The harness adapter's `OnSubmitted` inserts `UserPrompt` rows by direct SQL. The new harness
   rebinds it to a runtime ingest with a UUID, so a real queue delivery publishes through the
   store as production's live stream does (V-11).
4. `EmptyRunnerClient.GetTranscriptAsync` never throws (it answers an empty transcript). The
   harness registers a scripted `ISessionRunnerClient` decorator with modes `NotFound` (throws,
   production's 404 shape), `Empty`, `CommittedRow(uuid)` and `Entries(list)`, plus a `Pulls`
   counter.
5. Command accounting attaches `FullCommandCounter` and a promoted `ReceiptScanRecognizer`
   (the `CountingReader` from `AgentTaskLandReceiptTests.Scan.cs`, moved to
   `tests/Antiphon.Tests/TestHelpers/`) to both the harness contexts (`ConfigureDbContext`) and
   the test context.
6. Deterministic cuts use a `CutBoundary : LandDeliveryBoundary` with named gate pairs
   released in `finally`; reader faults use a `ReceiptReaderFault` interceptor that throws, or
   cancels a token, after N rows.
7. The profiled fixtures (V-6 `profiled-completion`, V-8 `profiled-pointer-hash-mismatch`) build
   `CompletionSnapshotJson`/`CompletionDeliveryJson` the way `RemoteCompletionSpillTests.cs`
   lines 402-436 do, with a real spill file and `TaskCompletionNotification.Sha256`.

### Delivery inventory

This change adds no producer, queue row, event, acknowledgement or asynchronous handoff. The
only delivery it touches is the existing receipt confirmation, on the consumer's read side:
producer = the queue's typed delivery into the recipient pty; destination = the recipient
transcript (`TranscriptEntries`, Kind `UserPrompt`/`QueuedUserPrompt`); persistence boundary =
`AgentSessionRuntime.PersistTranscriptAsync` commit followed by `SessionStateStore` publication
under the session gate; recovery = the hosted reconciler's 5-second pass with catch-up pull;
observable receipt = `AgentTaskLandNotification.State = Confirmed` with
`ConfirmingPromptSequence` equal to the first complete matching prompt; durable identity =
`note.Id` joined to `note.QueueMessageId`. The cache may only omit one SELECT inside that
recovery pass and never changes any of the above.

Producer-to-recipient test through the real queue: V-11 drives `Queue.FlushIfIdleAsync` so the
harness adapter submits the note, the submission is ingested through the runtime (not fixture
SQL), and the reconciler confirms it; arm `relaunch-after-cached-miss` proves a cached miss
cannot survive the recipient coming back to life and taking the prompt. Substitutes declared:
the busy recipient, already-eligible row and crash/enqueue-failure recovery shapes are the
existing `C641_*`, `C467_V12_*` cuts and `C550_*` boundary tests (R-1, R-3); this change adds
no handoff for them to cross, so they prove the path is unchanged, not that the cache is
correct. Transcript-confirmed `UserPrompt` rows are the verdict in every V-4, V-7, V-10 and
V-11 confirmation; `Sent`, `SentAt`, adapter `SubmittedBodies` and cache metrics are never
accepted as receipt.

### Proves it works now

Slice S1 (policy lane, no database; `tests/Antiphon.Tests/Application/LandReceiptScanCacheTests.cs`,
`[Category("Unit")]`). Fixture `PositiveCase()`: non-legacy Outcome note in `AwaitingReceipt`;
keyed row Sent, attempts 1, baseline 10, `LastDeliveryStartedAt` and `LastDeliveryGeneration`
set, verdict null, `Body == note.Body`, same session; destination Stopped with `StartedAt`
equal to the generation; snapshot Ready, epoch G, revision 7, reset 0, generation = `StartedAt`,
Count 49, LastSequence 58; expected text = body. The positive control publishes with
`before == after` and `saveCompleted: true` at t0 and reuses at t0 + 1 s. Each row changes
exactly one member on the reuse side (certificate rows change the publish side) from a fresh
copy.

- V-1: only whitelisted evidence reuses a negative scan | policy | `LandReceiptScanCacheTests.C1121_OnlyWhitelistedEvidenceReusesNegativeScan(string flip)`, 95 `[Arguments]` rows | the 30 `positive-*` rows return true; every other row returns false and names the refusing member in the cache's refusal reason.

| Group | Row keys (one `[Arguments]` each) | Expected |
|---|---|---|
| identity (5) | `note-id`, `queue-message-id`, `parent-session`, `queue-destination`, `source-land-notification-id` | false |
| attempt (6) | `delivery-attempts`, `baseline-sequence`, `last-delivery-started-at`, `last-delivery-started-at-null`, `last-delivery-generation`, `last-delivery-generation-null` | false; a null on one side is never equal to a value |
| payload (8) | `expected-text-one-char`, `is-legacy`, `kind-held-vs-aged`, `queue-status-sent-vs-pending`, `verdict-null-vs-delivered`, `verdict-delivered-vs-late-confirmed`, `note-state-awaiting-vs-queued`, `destination-status-stopped-vs-failed` | false |
| eligibility (22) | `attempts-zero`, `baseline-null`, `baseline-negative`, `kind-legacy-check-note`, `kind-undefined-enum`, `note-state-confirmed`, `note-state-not-required`, `note-state-legacy-unverified`, `note-state-undefined-enum`, `queue-status-undefined-enum`, `verdict-undefined-enum`, `completion-snapshot-present`, `completion-delivery-present`, `pointer-headline-body`, `remote-spill-body-present`, `queue-body-not-ordinal-equal`, `queue-body-case-differs`, `expected-text-not-body`, `text-over-cap`, `destination-running`, `destination-starting`, `destination-status-undefined-enum` | false: `TryBuildContext` refuses, so nothing is published or reused |
| state (12) | `readiness-cold`, `readiness-loading`, `readiness-missing`, `readiness-faulted`, `accepted-generation-null`, `accepted-generation-not-equal-started-at`, `server-epoch-empty`, `revision-zero`, `revision-negative`, `count-negative`, `last-sequence-below-count`, `observation-unknown` | false: `StateStamp.TryCreate` or the observation refuses |
| certificate (12) | `no-certificate`, `before-after-differ`, `match-observed`, `save-not-completed`, `expired`, `stamp-session`, `stamp-epoch`, `stamp-revision`, `stamp-reset-epoch`, `stamp-generation`, `stamp-count`, `stamp-last-sequence` | false; `Publish` is a no-op for the first four |
| positive (30) | `positive-held`, `positive-aged`, `positive-conflict`, `positive-outcome`, `positive-outcome-legacy`, `positive-dispatch-base`, `positive-delivery-failure`, `positive-task-completion-unprofiled`, `positive-queue-status-pending-parked`, `positive-queue-status-canceled`, `positive-note-state-queued`, `positive-note-state-retry-pending`, `positive-note-state-destination-unavailable`, `positive-note-state-canceled`, `positive-destination-failed`, `positive-text-at-cap`, `positive-generation-null-bound`, `positive-started-at-null-bound`, `positive-verdict-null`, `positive-verdict-delivered`, `positive-verdict-no-composer-evidence`, `positive-verdict-no-submit-output`, `positive-verdict-no-transcript-record`, `positive-verdict-truncated`, `positive-verdict-forbidden-body`, `positive-verdict-local-command-not-accepted`, `positive-verdict-backend-unreachable`, `positive-verdict-late-confirmed`, `positive-verdict-modal-blocked`, `positive-verdict-spill-body-missing` | true; the same production `TryBuildContext`/`StateStamp.TryCreate`/`TryReuse` the reconciler calls |

- V-2: lifetime, capacity, size and binding fail closed | policy | `LandReceiptScanCacheTests.C1121_CacheLifetimeAndCapacityFailClosed(string rule)`, 12 rows: `lifetime-reuse-one-tick-before`, `lifetime-refuse-at-five-minutes`, `hit-does-not-extend` (hits at 4 m 59 s, refuse at 5 m), `capacity-1025th-evicts-oldest` (1,025 distinct notes; first gone, count 1,024), `eviction-keeps-exact-binding` (a survivor's context with another entry's stamp refuses), `text-at-cap-admitted`, `text-over-cap-refused` (publish is a no-op, refusal counter increments), `terminal-removal` (`Invalidate` removes, count decrements), `new-singleton-miss`, `concurrent-publications-keep-own-binding` (two tasks publish C1/S1 and C2/S2 for one note 200 times; the final entry matches only its own pair, never C1/S2 or C2/S1), `invalid-elapsed-refuses` (publish timestamp after now, and a clock whose `GetTimestamp` throws: false, no exception escapes), `metrics-never-throw-and-count` (proofs, hits, misses, publishes, refusals by reason) | each row's named assertion passes with a manual cache clock (`FakeTimeProvider` timestamps), no sleeps.

Slice S2 (managed PostgreSQL lane; `tests/Antiphon.Tests/Application/AgentTaskLandReceiptWatermarkTests.cs`,
`[Category("Integration")]`, `tests/Antiphon.Tests/TestHelpers/LandReceiptScanHarness.cs`).
Default fixture: isolated schema; harness per item list above; note from
`AgentTaskLandReceiptTests.SeedAsync` with kind/legacy per arm; 48 candidates plus one
below-floor row ingested in one batch before the enqueue pass; enqueue pass; row marked Sent,
attempts 1, baseline 10, started-at and generation set, verdict null; destination set Stopped;
runner `NotFound`; counters reset; then three `ReconcileAsync` passes through one service
instance. Every arm asserts: catch-up pulls equal passes; `h.Adapter.Inputs` empty;
`Runner.KillCalls == 0`; the keyed row's Status, SentAt and attempts unchanged; the note's
state, `ConfirmedAt` and `ConfirmingPromptSequence` as stated; cache metrics as stated.

- V-3: unchanged transcript skips only the receipt SELECT | integration | `AgentTaskLandReceiptWatermarkTests.C1121_UnchangedTranscriptSkipsOnlyReceiptSelect(string shape)`, 6 rows: `two-kind-outcome-404` (6/5/5 commands, receipt 1/0/0, rows 48/0/0), `two-kind-outcome-cold-store` (7/5/5; store first touched by the observation), `one-kind-legacy-outcome-null-verdict-404` (6/5/5; SQL has `t."Kind" = 'UserPrompt'`), `dispatch-base-canceled-row` (queue row Canceled, note becomes Canceled with `queue_canceled_unconfirmed`; 6/5/5; never confirms), `task-completion-unprofiled-404` (7/6/6 with its generation SELECT), `two-kind-outcome-live-committed-row` (runner `CommittedRow` of an existing UUID; 9/8/8) | per-pass command totals and receipt SELECT counts exactly as listed; proofs 1, publishes 1, hits 2; `ConfirmedAt` null; state AwaitingReceipt (Canceled in the canceled row).
- V-4: any committed change reopens the full scan | integration | `AgentTaskLandReceiptWatermarkTests.C1121_AnyCommittedChangeReopensFullScan(string change)`, 6 rows after a cached miss: `matching-user-prompt` (ingest the body as UserPrompt: next pass 1 receipt SELECT, Confirmed at the stored sequence 59, proofs 0 afterwards), `matching-queued-user-prompt` (two-kind note confirms; the same ingest on the legacy arm would not, which R-1 already pins), `rebased-low-sequence-old-timestamp` (runner sequence 1, timestamp one hour old, new UUID: stored at `max + 1`, Confirmed there), `unrelated-assistant-text` (next pass full scan 1/48, miss, new proof; the pass after reuses), `fence-prune-reseed` (`BeginMutationAsync` + `ExecuteDelete` of one candidate + `PublishCommittedAsync`: reset epoch +1; next pass 1/47, miss, new proof), `ingest-lower-runner-sequence-after-reseed` (after the fence, a matching prompt with runner sequence 3 rebases above the floor and confirms; the pre-fence proof never reuses) | `ConfirmingPromptSequence` equals `InMemoryFirstReceipt` over the database rows above the original floor; receipt floor parameter equals the row's baseline; no retyping.
- V-5: attempt and payload changes reopen the original floor | integration | `AgentTaskLandReceiptWatermarkTests.C1121_AttemptAndPayloadChangesReopenOriginalFloor(string change)`, 6 rows after a cached miss with a matching prompt already present **at sequence 10 (the floor)**: `lower-baseline-includes-existing-match` (baseline 9: next pass confirms at 10; floor parameter 9), `attempts-increment` (full scan, miss, new proof bound to attempts 2), `expected-body-changed-to-existing-prompt` (note and row bodies set to candidate 20's text: confirms at 20), `queue-identity-changed` (note re-keyed to a second Sent row with the same body: full scan, new proof bound to the new row and its `SourceLandNotificationId`), `legacy-flag-flip-widens-acceptance` (starts legacy with a `QueuedUserPrompt` match at 30: miss cached; `IsLegacy = false`: confirms at 30), `kind-flip` (DispatchBase to Outcome with the same queued match: confirms at 30) | the first matching sequence is the already-present row, which a suffix cursor at the previous `LastSequence` would hide; floor parameter is the row's current baseline on every scan.
- V-6: unknown evidence uses the existing scan | integration | `AgentTaskLandReceiptWatermarkTests.C1121_UnknownEvidenceUsesExistingScan(string evidence)`, 13 rows, three passes each with one receipt SELECT per pass, proofs 0, publishes 0: `store-absent` (harness without the store), `store-disabled` (`SessionStateSettings.Enabled = false`), `destination-running`, `destination-starting`, `runner-404-with-retained-persist-failure` (an earlier ingest failed through a `SaveFault`; `TryGetTranscriptPersistFailure` true), `runner-pull-persist-fails` (runner returns a new row each pass whose INSERT a `RejectRow` interceptor refuses: `NeedsReload`), `timestamp-only-baseline`, `profiled-completion` (snapshot and delivery JSON with the row in `MemberQueueIds`, report file present; 7 commands), `pointer-headline-row` (row body is the owned pointer, `RemoteSpillBody` holds the body), `legacy-check-note`, `oversized-body` (4,097-character detail), `cache-clock-fault` (cache clock throws: scans every pass, `LastErrorCode` null, no `notification_reconcile_failed`), `fallback-confirms-with-unavailable-runner` (a valid complete `UserPrompt` already in the database and runner `NotFound`: first pass confirms) | each existing refusal gate is reached and passed by the fixture so the scan executes; the cache never publishes.
- V-10: a foreign-destination note stays open exactly as today | integration | `AgentTaskLandReceiptWatermarkTests.C1121_ForeignDestinationNoteStaysOpenExactlyAsToday(string arm)`, 3 rows: `cached-stays-open` (row `AgentSessionId` = session B whose transcript holds the complete body as `UserPrompt` above the floor; parent A Stopped with 48 unrelated rows: pass 1 scans A 1/48 and publishes, passes 2-3 reuse; note AwaitingReceipt, `ConfirmedAt` null; every note column equals a control run of the same fixture with no cache registered), `parent-rehomed-to-row-destination` (`ParentSessionId = B`: full scan of B, Confirmed at B's sequence), `row-destination-changed` (row `AgentSessionId = A`: full scan of A, miss, new proof) | A-3 default; one expected value flips under strict D-3.
- V-11: a real queue delivery is never hidden | integration, real queue | `AgentTaskLandReceiptWatermarkTests.C1121_RealQueueDeliveryIsNeverHiddenByTheCache(string arm)`, 2 rows: `running-destination-real-delivery` (destination Running; `Queue.FlushIfIdleAsync` delivers through the adapter; the submission is ingested through the runtime; reconcile confirms at that row's sequence; proofs 0 because A-1), `relaunch-after-cached-miss` (cached miss on Stopped; status set Running as the launch path does; the adapter submits the body and the runtime ingests it; next pass: W-3 fails, full scan, Confirmed at the new sequence; the reconciler typed nothing) | transcript-confirmed `UserPrompt` is the verdict; `SubmittedBodies` is not.

Slice S3 (`tests/Antiphon.Tests/Application/AgentTaskLandReceiptWatermarkSafetyTests.cs`).

- V-7: a racing commit cannot publish or reuse a stale miss | integration, deterministic cuts | `AgentTaskLandReceiptWatermarkSafetyTests.C1121_RacingCommitCannotPublishOrReuseStaleMiss(string cut)`, 5 rows: `commit-between-exhaustion-and-after-stamp` (hold `receipt-scan-exhausted`; ingest the match; release: no proof; next pass confirms at the new sequence), `writer-holds-gate-during-reuse-observation` (an ingest held at `SaveGate` owns the session gate; pass 2 must not complete while it is held; release: the ingest publishes, pass 2 observes the new revision, full scan, confirms), `reader-throws-after-one-row` (`notification_reconcile_failed:*` recorded, `EnqueueAttempts` +1, no proof; next pass full scan; an ingested match then confirms), `reader-cancelled-after-one-row` (`OperationCanceledException` escapes `ReconcileAsync`; no proof; note unchanged; next pass full scan), `commit-before-before-stamp` (hold `receipt-scan-before-stamp`; ingest the match; release: the scan itself finds it, confirms, no proof) | all gates released in `finally`; cancellation remains cancellation; the late receipt confirms at its own sequence in original first-match order.
- V-8: a matched but uncommitted receipt is never cached | integration | `AgentTaskLandReceiptWatermarkSafetyTests.C1121_MatchedButUncommittedReceiptIsNeverCached(string cut)`, 4 rows: `receipt-before-save-fails` (boundary throws at `receipt-before-save`; no proof; a fresh scope with the same cache re-reads and confirms at the same sequence), `final-save-fails-after-match` (`ThrowOnceSaveInterceptor`; same recovery), `profiled-pointer-hash-mismatch-then-repair` (excluded profiled fixture with `SpillPath`/`SpillSha256`; wrong file bytes: `completion_pointer_content_mismatch`, no proof and, being excluded, no publish attempt; repair the bytes without a new prompt: confirms the same sequence), `negative-save-fails` (no match, final save throws: no proof; next pass full scan, then a proof) | proof count 0 after each failure; confirmation sequence identical before and after recovery.
- V-9: reconcile pins statement and row budgets | integration | `AgentTaskLandReceiptWatermarkSafetyTests.C1121_ReconcilePinsStatementAndRowBudgets(string shape)`, 5 rows: `two-kind-48` (cold 7, then 5, 5; receipt 1/48 then 0/0; recognizer matches; floor parameter 10 each scan; projection excludes `ToolInput`, `ApiErrorTimeZoneId`, `ModelCalls`; no other `TranscriptEntries` statement in any pass; proofs 1), `one-kind-852` (legacy Outcome; 1/852 then 0/0; 6/5/5), `two-kind-1506` (1/1,506 then 0/0; 6/5/5), `changed-transcript-two-kind-48` (after a cached miss an unrelated `AssistantText` ingest (4 commands, outside the window); next pass 6 with 1/48, miss, new proof; then 5), `live-committed-row-9-8` (runner `CommittedRow`: 9 then 8; the extra three are the catch-up's `StartedAt`, identity and `MAX`, none a cache probe) | exact totals from §4; a hidden full-entity materialization or any per-note existence/count/max probe on the reuse path fails the roster assertion.

### Guards the regression

- R-1: existing projection, first receipt, floors, queued-kind whitelist, full body and pointer rules, no retyping | `AgentTaskLandReceiptTests` (42 results: `C467_V12` 4, `C467_V13` 5, `C467_V11` 15, six `C488_*`, `C1073_ReceiptDecisionMatchesTheInMemoryScan` 11, `C1073_LowFloorScan…` 1) and `AgentTaskLandQueuedReceiptTests` (22 results); decisive assertions: `ConfirmingPromptSequence` equals the in-memory oracle per shape, `ReceiptSql` single item with the closed projection, `reader.Rows == 1` on the low-floor hit, `Adapter.Inputs` empty.
- R-2: shared runtime core keeps commit-before-publication, partial fallback, collision recovery, restart generation and concurrent ingestion | `SessionStateCommitTests` (8); decisive: `reader.IsCompleted` false during a held save, `Revision` strictly greater after a commit, `duplicateBatches == 1`, `Commands.Snapshot()["identity"] == 14`.
- R-3: recovery remains owed and the watchdog's shared receipt predicate/age is unchanged | `AgentTaskLandNotificationRecoveryTests` (43 results across both partial files) and `ExpectationNoteDebtTests` (7); decisive: `DestinationUnavailable`/`NextAttemptAt` matrix values, keyed-row conflict refusals, `UndeliveredNotes` subject keys.

### Guard inventory

Each row is one independently bypassable guard mapped to one distinct PC. V-1's 65 refusing
rows are 65 guards (G-1.`<row>`), each with its own PC-1.`<row>` subcontrol; the 30 positive
rows share one forced-refusal control (PC-1.positive).

| G | Plan ref and safety-critical guard | PC |
|---|---|---|
| G-1.`<row>` (65) | D-3/D-5/D-2/A-1: each identity, attempt, payload, eligibility, state and certificate member in V-1 refuses reuse when it differs or is disqualifying | PC-1.`<row>` |
| G-1.positive | D-3: the exact unchanged positive arm is admitted by the same production code | PC-1.positive |
| G-2a | D-6 five-minute absolute lifetime | PC-2a |
| G-2b | D-6 hits never extend lifetime | PC-2b |
| G-2c | D-6 capacity 1,024 with eviction | PC-2c |
| G-2d | D-6 text admission at the cap | PC-2d |
| G-2e | D-6 terminal removal | PC-2e |
| G-2f | D-6 concurrent publication keeps its own context/stamp binding | PC-2f |
| G-2g | D-6 invalid elapsed-time evidence refuses reuse | PC-2g |
| G-3a | D-7 step 4: a hit omits only `FirstReceiptAsync` | PC-3a |
| G-3b | D-7 steps 2/4: a hit never skips the pull, the keyed-row lookup, bookkeeping or the final UPDATE | PC-3b |
| G-4a | D-2: Revision/Count/LastSequence change invalidates | PC-4a |
| G-4b | D-2: ResetEpoch/ServerEpoch change invalidates | PC-4b |
| G-5 | D-5/D-7: the scan floor is always the row's original baseline | PC-5 |
| G-6a | A-1 wiring: the projected destination status reaches the context | PC-6a |
| G-6b | A-2/D-4: retained persist failure or `NeedsReload` is unknown | PC-6b |
| G-6c.profile, G-6c.pointer, G-6c.null-floor, G-6c.legacy-check, G-6c.oversized | D-3 integration exclusions each keep today's scan | PC-6c.`<arm>` (5) |
| G-6d | D-4: no store or disabled store means no certificate | PC-6d |
| G-7a | D-7: before-stamp must equal after-stamp | PC-7a |
| G-7b | D-7/D-2: the reuse observation is a fresh serialized store read | PC-7b |
| G-7c | D-7: partial enumeration, fault or cancellation publishes nothing | PC-7c |
| G-8a | D-7: a text hit with a failed pointer hash is not a negative scan | PC-8a |
| G-8b | D-7 step 7: publish only after the final `SaveChangesAsync` succeeded | PC-8b |
| G-8c | D-7: a hit whose confirmation save failed is not a cacheable miss | PC-8c |
| G-9a | D-2/D-5: no per-note transcript MAX/Any/Count probe on the reuse path | PC-9a |
| G-9b | CARD-1073: the receipt projection stays `Sequence, Text` | PC-9b |
| G-10 | A-1 policy: Running/Starting destinations are not whitelisted | PC-10 |
| G-11 | A-2: `AcceptedGeneration` must equal the destination `StartedAt` | PC-11 |
| G-12 | Out-of-scope defect not masked: the scan target stays `ParentSessionId` | PC-12 |
| G-13 | W-10: cache exceptions fall back to the scan, never fail the note | PC-13 |
| G-14 | D-6: confirmation and terminal states remove the note's proof | PC-14 |

Guards 99 (65 + 1 + 7 + 2 + 2 + 1 + 1 + 1 + 5 + 1 + 3 + 3 + 2 + 1 + 1 + 1 + 1 + 1), mapped
99, missing 0, duplicate PC maps 0. Not a guard: the harness coherence self-check (fixture
only) and the optional `land-note.receipt-scan` tag (diagnostic only; the recognizer tolerates
its absence).

### Positive controls

Mutation runs each PC method-scoped: baseline, red, exact restore, green, in the separately
commissioned Mutation stage against landed source. Filters use the method prefix so every
argument row of that method runs; the red is the named row's assertion. One mutation per
cycle; PC-1 rows share one file and method and are therefore never batched. Code runs only the
V/R rows; Review judges this roster before land.

- PC-1.`<row>` (65): in `LandReceiptScanCache.TryBuildContext`/`StateStamp.TryCreate`/`TryReuse`, delete only the comparison or precondition that row names (for example drop `QueueMessageId` from the context equality; accept `Revision >= 0`; treat a null `LastDeliveryGeneration` as a wildcard; admit `LandNotificationKind.LegacyCheckNote`; compare bodies with `OrdinalIgnoreCase`; drop the `SessionStatus.Running` refusal); expect `/*/*/LandReceiptScanCacheTests/C1121_OnlyWhitelistedEvidenceReusesNegativeScan*` red at that row's `reused.ShouldBeFalse(flip)`.
- PC-1.positive: make `TryBuildContext` refuse `LandNotificationKind.Outcome`; expect the same filter red at `positive-outcome`'s `reused.ShouldBeTrue`.
- PC-2a: drop the lifetime check; PC-2b: refresh the publish timestamp on a hit; PC-2c: remove capacity eviction; PC-2d: compare text length with `>` instead of `>=` at the cap plus one (or drop the check); PC-2e: make `Invalidate` a no-op; PC-2f: store the newest stamp under the previous context on replacement; PC-2g: accept a negative elapsed time; expect `/*/*/LandReceiptScanCacheTests/C1121_CacheLifetimeAndCapacityFailClosed*` red at the named row.
- PC-3a: in `ReconcileAsync`, always call `FirstReceiptAsync` even on a hit; expect `/*/*/AgentTaskLandReceiptWatermarkTests/C1121_UnchangedTranscriptSkipsOnlyReceiptSelect*` red at pass-2 `receiptSelects.ShouldBe(0)`. PC-3b: return before catch-up and the final `SaveChangesAsync` on a hit; expect the same method red at `pulls.ShouldBe(3)` or `commands.ShouldBe(5)`.
- PC-4a: in `StateStamp` equality ignore Revision, Count and LastSequence; expect `/*/*/AgentTaskLandReceiptWatermarkTests/C1121_AnyCommittedChangeReopensFullScan*` red at `matching-user-prompt`'s `State.ShouldBe(Confirmed)`. PC-4b: ignore ResetEpoch and ServerEpoch; expect red at `fence-prune-reseed`'s `receiptRows.ShouldBe(47)` and `ingest-lower-runner-sequence-after-reseed`'s confirmation.
- PC-5: on a context change pass the previous proof's LastSequence as the baseline to `LandNoteReceipt.Prompts`; expect `/*/*/AgentTaskLandReceiptWatermarkTests/C1121_AttemptAndPayloadChangesReopenOriginalFloor*` red at `lower-baseline-includes-existing-match`'s `ConfirmingPromptSequence.ShouldBe(10)` and the floor-parameter assertion.
- PC-6a: pass `SessionStatus.Stopped` into the context instead of the projected status; expect `/*/*/AgentTaskLandReceiptWatermarkTests/C1121_UnknownEvidenceUsesExistingScan*` red at `destination-running`'s `receiptSelects.ShouldBe(1)` on pass 2. PC-6b: in the runtime observation ignore `TryGetTranscriptPersistFailure` and `NeedsReload`; expect red at `runner-404-with-retained-persist-failure` and `runner-pull-persist-fails`. PC-6c.`<arm>` (5): bypass one integration exclusion (treat the profiled rendering's `WireText` as cacheable; treat the pointer row's `row.Body` as the ordinary body; synthesize a baseline of 0 for a timestamp-only row; admit LegacyCheckNote in the reconciler; skip the length check); expect red at that arm. PC-6d: when `_states` is null or disabled, synthesize a Ready stamp; expect red at `store-absent`/`store-disabled`.
- PC-7a: publish with the after-stamp even when the before-stamp differs; expect `/*/*/AgentTaskLandReceiptWatermarkSafetyTests/C1121_RacingCommitCannotPublishOrReuseStaleMiss*` red at `commit-between-exhaustion-and-after-stamp`'s `proofs.ShouldBe(0)`. PC-7b: reuse the snapshot captured before catch-up instead of reading the store after it; expect red at `writer-holds-gate-during-reuse-observation`'s completion-order assertion. PC-7c: publish from the `catch` path or after a reader fault; expect red at `reader-throws-after-one-row`/`reader-cancelled-after-one-row`.
- PC-8a: publish when `evidence` became null after the pointer-hash failure; PC-8b: publish before the final `SaveChangesAsync`; PC-8c: publish when the hit's confirmation save threw; expect `/*/*/AgentTaskLandReceiptWatermarkSafetyTests/C1121_MatchedButUncommittedReceiptIsNeverCached*` red at the named cut's `proofs.ShouldBe(0)` or its same-sequence re-confirmation. For PC-8a the otherwise-excluded profiled fixture is reached only under this explicit unsafe mutation.
- PC-9a: add a `MaxAsync(Sequence)` probe on the reuse path; PC-9b: project the full `TranscriptEntry` in the reconciler's receipt query; expect `/*/*/AgentTaskLandReceiptWatermarkSafetyTests/C1121_ReconcilePinsStatementAndRowBudgets*` red at the command-total or projection assertion.
- PC-10: whitelist `SessionStatus.Running` in `TryBuildContext`; expect `C1121_OnlyWhitelistedEvidenceReusesNegativeScan*` red at `destination-running`.
- PC-11: drop the `SessionGeneration.Equal(AcceptedGeneration, StartedAt)` check; expect the same method red at `accepted-generation-not-equal-started-at`.
- PC-12: scan `row.AgentSessionId` instead of `note.ParentSessionId`; expect `/*/*/AgentTaskLandReceiptWatermarkTests/C1121_ForeignDestinationNoteStaysOpenExactlyAsToday*` red at `cached-stays-open`'s `State.ShouldBe(AwaitingReceipt)`.
- PC-13: remove the `catch` around `TryReuse`/`Publish`; expect `C1121_UnknownEvidenceUsesExistingScan*` red at `cache-clock-fault`'s `LastErrorCode.ShouldBeNull()`.
- PC-14: do not call `Invalidate` on confirmation; expect `C1121_AnyCommittedChangeReopensFullScan*` red at `matching-user-prompt`'s `proofs.ShouldBe(0)` after the confirming pass.

### Out of scope

- Re-homing or terminal classification of the 13 foreign-destination notes: separate card; V-10 only proves the cache does not mask or alter today's outcome.
- Live (Running) destinations: never cached under A-1; keep today's cost; V-6 and V-11 pin the refusal, no test claims a saving there.
- Profile/spill/pointer paths, LegacyCheckNote, CARD-0079 compaction stop, queue send mode, retention, parking, release and Working decisions: unchanged by D-9; covered only as exclusions (V-6, V-8) and by R-1/R-3.
- `SessionStateStore` pressure, eviction and warm-up behaviour: owner's suites; a non-admitted or evicted entry reseeds with a new revision and can only produce a miss.
- Out-of-process SQL edits to `TranscriptEntries`: the owner's restart requirement stands; a restart starts the cache empty (V-2 `new-singleton-miss`); no test simulates a bypass as a supported path.
- Whole Unit/Application/assembly runs: none; the closed table below is the ordinary scope.
- The body-length census (Q2) runs in the database-owner lane, not on this mirror, which has no production database.

### Checkpoints

Managed lanes only; no production runner, no real provider. CP-1 needs no database. CP-2
through CP-6 own a Testcontainers PostgreSQL and an isolated schema per test. Builds add
`UseAppHost=false` off Windows by default. CP-4 to CP-6 reuse CP-3's output (same `After`).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1121-policy/` | policy | `/*/*/LandReceiptScanCacheTests/*` | V-1, V-2 | 108 executed (A-5), 0 failed/skipped | 108 | 4 | true | n/a |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1121-behavior/` | pg-behavior | `/*/*/AgentTaskLandReceiptWatermarkTests/*` | V-3, V-4, V-5, V-6, V-10, V-11 | 36 executed, 0 failed/skipped | 36 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1121-safety/` | pg-safety | `/*/*/AgentTaskLandReceiptWatermarkSafetyTests/*` | V-7, V-8, V-9 | 14 executed, 0 failed/skipped | 14 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S3 | `CP-3` | pg-receipts | `/*/Antiphon.Tests.Application/(AgentTaskLandReceiptTests*)\|(AgentTaskLandQueuedReceiptTests*)/*` | R-1 | 64 executed including all 12 C1073 results, 0 failed/skipped | 64 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S3 | `CP-3` | pg-ingest | `/*/*/SessionStateCommitTests/*` | R-2 | 8 executed, 0 failed/skipped | 8 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S3 | `CP-3` | pg-recovery | `/*/Antiphon.Tests.Application/(AgentTaskLandNotificationRecoveryTests*)\|(ExpectationNoteDebtTests*)/*` | R-3 | 50 executed, 0 failed/skipped | 50 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Run through the checkpoint tool, one run per committed slice group:
`dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-08-card-1121-receipt-scan-watermark-test-design.md --after S1 --expected-source-sha <S1 full sha>`,
then `--after S2`, then `--after S3`; `wait` while exit is 75; exit 4 is a slot timeout to
report, never an unleased retry. Commit and push before each group and do not edit source
while a run is in flight. A red row is fixed and rerun as the same row. Delete the three
row-owned `bin-c1121-*` outputs after the last green run. Run `scripts/check-evidence-diff.ps1`
over the full Code task range.

### Cost

- Ordinary V/R floor for Code (sum of EstimatedMinutes, CP-1..CP-6) = **31 minutes**, estimated: three isolated builds at about 2.5 minutes each (measured on this mirror: 103 s cold, 143-180 s with the test project changed) inside CP-1, CP-2 and CP-3; CP-4 to CP-6 reuse CP-3's build, which saves three builds, about 7.5 minutes against the plan's six-build table. Slot waits are outside the floor (0 s on all three diagnostic leases today).
- PC floor for Mutation = **300 minutes**, estimated: 75 policy-lane cycles (PC-1 × 66, PC-2a..g, PC-10, PC-11 fall in `LandReceiptScanCacheTests`, no database) at 2.5 minutes each (incremental build plus a sub-second run) = 188 minutes; 24 PostgreSQL cycles (PC-3a/b, PC-4a/b, PC-5, PC-6a/b/c×5/d, PC-7a/b/c, PC-8a/b/c, PC-9a/b, PC-12, PC-13, PC-14) at 4.7 minutes each (incremental build plus container and a 20-60 s row) = 113 minutes. PC-1 rows cannot be batched because they share one file and method; SourceLanding forbids shards.
- Total = 240 (this design, including two diagnostic builds and runs) + 31 ordinary + 300 PC = **571 minutes**, estimated. Savings: 7.5 build-minutes per ordinary round from build reuse; the policy lane keeps 75 of 99 PC cycles off PostgreSQL, about 165 minutes less than running them as integration cycles.

Bundle check: bodies read; guards 99, mapped 99, missing 0, duplicate PC maps 0; every PC
names a compiling defect and an exact method prefix filter; Cost is numeric; no placeholders.

## 6. Diagnostic runs

Run on this Linux runner mirror (nested Docker, Testcontainers PostgreSQL) at
`1cb99ac53a13e911105a6a6d4999c7c67522387e` through `scripts/run-checkpoint.ps1` (slot
granted, waited 0 s). They are diagnostics of today's tree, not checkpoint executions and not
evidence for any V row. The probe class was never committed; its outputs live in the session
scratchpad and the ignored `.antiphon/c1121-td/`; all `bin-c1121-td/` outputs were deleted.

- `CHECKPOINT TD-PROBE commit=1cb99ac53a13e911105a6a6d4999c7c67522387e build=ok filter=/*/*/C1121TestDesignProbeTests/* executed=4 passed=4 failed=0 skipped=0 slot=granted waited=0s dirty=1 sourceState=dirty buildSource=verified` (dirty = the uncommitted probe file; 180 s build, 41 s run). Four arms (runner empty or one committed row, store absent or registered): the per-pass rosters in §4; receipt SQL text as quoted; the store snapshot stayed at revision 1 across quiet passes with a live committed-row pull; the matching-prompt ingest cost 4 commands, moved revision 1 to 2, rebased runner sequence 1 to stored 59, and the next pass confirmed at 59.
- `CHECKPOINT TD-PROBE2 commit=1cb99ac53a13e911105a6a6d4999c7c67522387e build=ok filter=/*/*/C1121TestDesignProbe2Tests/* executed=1 passed=1 failed=0 skipped=0 slot=granted waited=0s dirty=1 sourceState=dirty buildSource=verified` (143 s build, 48 s run). Seeding 49 rows through `PersistTranscriptAsync` before the enqueue pass cost 5 commands including the cold seed and left the store coherent (Count 49, LastSequence 58, database 49); two quiet passes cost 6 commands each with the snapshot unchanged at revision 2; a fenced single-row delete cost 2 commands and moved revision 2 to 3 and reset epoch 0 to 1.
- The first probe also measured the hazard in item 2 of "Missing setup": rows inserted by direct SQL after the enqueue pass left the snapshot Ready with Count 0 while the database held 49 rows.

## 7. Slice order and Code handoff

1. **S1 (45-60 min):** `server/Application/Services/LandReceiptScanCache.cs` (context, stamp, whitelist with A-1 status member, bounded proof store, metrics, catch-all fallback); `AgentSessionRuntime.cs` shared catch-up core plus the internal receipt observation (D-4 as amended by A-2); `tests/Antiphon.Tests/Application/LandReceiptScanCacheTests.cs` (V-1 95 rows, V-2 12 rows); promote `CountingReader` to `tests/Antiphon.Tests/TestHelpers/ReceiptScanRecognizer.cs`. Run CP-1.
2. **S2 (45-60 min):** `AgentTaskLandNotificationService.cs` ordering/integration with the status+StartedAt projection, boundary names, optional `scanCache` parameter; `server/Program.cs` singleton registration; `tests/Antiphon.Tests/TestHelpers/LandReceiptScanHarness.cs` (items 1-7 above); `AgentTaskLandReceiptWatermarkTests.cs` (V-3, V-4, V-5, V-6, V-10, V-11). Run CP-2.
3. **S3 (45-60 min):** `AgentTaskLandReceiptWatermarkSafetyTests.cs` (V-7, V-8, V-9) with the harness cut and reader-fault probes; `docs/session-runtime-invariants.md`: extend the "Working state is a committed projection (CARD-0701)" bullet with the negative-certificate reuse/invalidation contract and the terminal-destination condition. Run CP-3..CP-6.

Code brief line: `checkpoints: docs/superpowers/plans/2026-10-08-card-1121-receipt-scan-watermark-test-design.md@<this commit> section "### Checkpoints"`.
Defaults the Code brief inherits unless overridden: A-1 terminal-only, A-2 terminal-branch
observation, A-3 bound-not-equal destination, `MaxExpectedTextChars = 4096` with
`MaxProofs = 1024` pending the Q2 census.
