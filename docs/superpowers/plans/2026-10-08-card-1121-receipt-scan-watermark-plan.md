# CARD-1121: fail-closed receipt scan watermark

Plan task: `4c27afbe`. Date: 2026-10-08. Source inspected:
`b5e78700ae9a76430c13d75cc03399055dd82e59`. Plan only; no implementation or test
execution is claimed. **Next: test-design**, including the measurement gate below.
The verification design is specified here for that stage to qualify, not yet an
authorization to skip the card's live measurement prerequisite.

## Outcome and scope

The repetition is **not bounded by CARD-1073**. An eligible unresolved note with
unchanged transcript and attempt still enumerates every candidate on every pass.
A watermark is justified by that code path; whether this remains a material
production cost after CARD-1073 requires a fresh sample. This runner cannot see
the production Postgres container, so that sample remains a named prerequisite.

The proposed smallest change is a bounded, process-local **negative scan
watermark**, keyed by note and exact attempt/payload identity, certified by the
existing committed session-state revision. It skips only an unchanged scan
already completed without a match. Any change or uncertainty performs today's
entire scan from the original delivery floor. It does **not** advance that floor,
resume at a maximum sequence, infer delivery, delay the next reconciliation,
change retention, or stop/retry/release a session.

Owners read: `docs/project-context.md`, `docs/resilience.md`,
`docs/ops-http.md`, `docs/logs.md`, `docs/testing-and-build.md`,
`docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`, and the delivery and
committed-projection contracts in `docs/session-runtime-invariants.md`.

## Ground truth

The card reads used `scripts/card.ps1 get CARD-1121 -Board Antiphon` and the same
command for CARD-1073. CARD-1073 is Done; its landed commit `2af727db` is an ancestor
of the inspected HEAD. At 2026-10-08 11:42 UTC, `GET /api/version` returned that
same full HEAD and `land-v2`. Thus the old card's "restart pending" text is not
the current activation evidence.

| Card assumption / question | What the inspected code actually does | Consequence |
|---|---|---|
| CARD-1073 removed the repeated load | `LandNoteReceipt.FirstReceiptAsync`, lines 55-72, orders by Sequence, projects Sequence/Text, and returns on the first complete match; a miss exhausts the reader. | Projection and early success help, but repeated misses still return all candidate Text. |
| A floor or timer bounds repeated misses | `AgentTaskLandNotificationService.ReconcileAsync`, lines 228-248, always catches up then rebuilds `Prompts` from the queued row's original attempt floor. There is no examined-sequence/revision field. `NextAttemptAt` gates only a note with no QueueMessageId (line 45). | Linked notes do not get scan backoff from NextAttemptAt. |
| Only AwaitingReceipt is scanned | `AgentTaskLandNotificationHostedService`, lines 97-121, pages 128 IDs, excluding only Confirmed, NotRequired, LegacyUnverified; it waits five seconds after the pass. Canceled and parked attempted rows can reach the scan. | Age, status Canceled, and exhausted attempts are not proof that a late receipt is impossible. |
| Any prompt after a floor is a receipt | `LandNoteReceipt.Prompts`, lines 29-47, requires destination, non-null Text, accepted kinds, strict Sequence > baseline, or Timestamp >= delivery-start minus nonnegative tolerance when baseline is absent. Both floors absent returns null. | Preserve the predicate and matcher's identity **and** completeness checks unchanged. |
| All notes accept the same kinds | `AcceptsQueuedPrompt`, lines 20-22: nonlegacy Held/Aged/Conflict/Outcome accept UserPrompt or QueuedUserPrompt; other kinds and legacy notes accept UserPrompt only. | LastUserPromptSequence alone cannot certify an unchanged two-kind scan. |
| The expected text is always immutable Body | Reconciler lines 168-227 select profiled WireText, validate membership, or resolve an owned spill pointer and its saved body. Lines 249-258 additionally check a profiled pointer's file hash **after** finding a prompt. | A text hit whose file check fails is not a negative scan. Complex rendering/pointer cases remain on today's path. |
| Catch-up false proves quiet | `AgentSessionRuntime.CatchUpTranscriptAsync`, lines 706-721, returns false for no new row **and** a swallowed runner error; persistence can also return no sequence on failure. | Never use that Boolean as skip authority. |
| A stored high sequence proves a complete scan | Runtime lines 914-1068 dedup UUID/kind, rebase incoming sequence, and can fall back to individual commits. `SessionStateSnapshot.Append` rejects a non-append and the store reseeds. | No standalone MAX(sequence), runner sequence, row count, or timestamp watermark. |
| There is no safe observation seam | `SessionStateStore.ReadAsync` and `BeginWriteAsync` share a session gate; runtime lines 834-858 holds it through commit and publication. Snapshot carries ServerEpoch, Revision, ResetEpoch, AcceptedGeneration, Count, LastSequence. | A successful stable before/after observation can certify that an exhausted negative scan is still applicable. |
| Reset/deletion leaves the old cache authoritative | `DataRetentionService.MutateTranscriptAsync` fences deletion and reseeds; `SessionStateDeletion` fences cascade commits. Missing/faulted state is unavailable. The owner explicitly requires a restart after out-of-process transcript SQL changes. | Reuse only inside the existing single-server writer contract; reset, restart, reseed change, eviction, or unavailable state invalidates reuse. |
| Current tests already pin repeated-miss cost | `AgentTaskLandReceiptTests.Scan.cs` has 11 differential cases and one first-hit projection/client-read assertion. It has no second-pass miss budget. | Add real reconciler SQL/reader counts; preserve these existing tests. |

The production insert census (`rg` over server, excluding migrations) found
TranscriptEntries additions in `AgentSessionRuntime` only. Retention and cascade
deletion use the state mutation fence. There is no production in-place prompt
Text/Kind update in this tree. A future writer must preserve the committed-state
contract or invalidate this optimization before it ships.

### Measurement status and required comparison

The historical investigation is
`docs/investigations/2026-10-06-card-1073-two-transcript-query-shapes.md`:
72 one-kind and 108 two-kind calls in 70 seconds; respectively 3,033.8 ms and
7,601.2 ms execution, about 852 and 1,506 rows/call. Combined 10,635 ms/70 s =
10.3 s/68 s. Those were the **old full-entity** query IDs, not IDs to assume for
the newly projected statements.

No fresh pg_stat_statements or CPU number was obtained in this dispatch.
`docker ps --format '{{.Names}}'` returned no containers, and the documented
`docker exec antiphon-postgres psql ...` probe failed with "No such container".
The assigned mirror cannot reach the desktop checkout. This is an evidence gap,
not evidence of zero cost and not a request to install a second database.

TestDesign must obtain the following from the canonical database-owner lane
using the credentials/container custody already documented in `docs/logs.md`:

1. Record source and `/api/version` SHA, observation UTC, database identity,
   `pg_stat_statements_info.stats_reset`, and the **current projected** query IDs.
   Discover them by normalized SQL containing TranscriptEntries, the
   Sequence/Text SELECT, destination/floor predicates and ORDER BY Sequence;
   distinguish one-kind and two-kind shapes. Do not reuse the old IDs by memory.
2. Take two read-only counter snapshots at least 70 seconds apart, without
   resetting statistics: queryid, calls, rows, total_exec_time. Reject the delta
   if reset time changes, counters decrease, activation changes, or either query
   was evicted. Store normalized SQL shape as provenance, never prompt text or
   SQL parameter values. Split the wait into foreground intervals <=60 seconds.
3. Calculate delta calls, rows/call, execution ms/call, execution ms/minute and
   `delta_exec_ms * 68 / actual_elapsed_seconds / 1000` for comparison with 10.3.
   Sample database-container CPU at both ends and mid-window; report it
   separately from database elapsed execution time.
4. Read metadata-only unresolved-note/queued-row cohorts: kind, legacy flag,
   state, attempt count/floor, destination generation, payload equality,
   profile/spill eligibility, candidate counts, and whether destination committed
   revision changes during the window. Do not fetch prompt Text for this census.
   Attribute repeated calls to the eligible quiet cohort, rather than all open
   notes or all queries containing TranscriptEntries.
5. Proceed to Code only when the current expensive misses repeat and the proposed
   whitelist covers that repeated cohort. Record absolute savings opportunity
   rather than inventing a percentage target. If activity/profile/pointer/error
   cases dominate and the whitelist would rarely hit, return to Plan; do not
   silently widen it. If repetition has disappeared, record that result and
   recommend no implementation. A quiet workload alone is inconclusive.

After implementation, land and verified server activation, repeat the same
window and cohort census. Certification is unchanged receipt outcomes plus
reduced repeated receipt-SELECT calls/returned Text, not merely lower wall time
under a different workload. Busy revisions intentionally still do full scans.

## Decisions

**D-1 — Cache negative work, not a delivery verdict.** A per-note watermark means
"this exact candidate set and expected text were fully examined without a
match." The only optimization is to omit the receipt SELECT on an exact reuse.
Rejected: setting Confirmed from cache, treating Sent/LateConfirmed as proof,
or sharing a cached result across notes. Each would change receipt authority.

**D-2 — Use exact committed revision identity; never advance the delivery floor.**
Store `(SessionId, ServerEpoch, Revision, ResetEpoch, AcceptedGeneration, Count,
LastSequence)` as an opaque stamp. Require positive Ready state, a nonempty server
epoch, positive revision, known generation and internally valid counts/sequences.
Any changed member invalidates the proof, including assistant-only activity.
Rejected: persisted per-note MAX(sequence), LastUserPromptSequence, count/max
equality, timestamp-only cursors, or reuse across "apparently append-only"
revisions. These need additional proofs for queued prompts, resets, partial
ingestion and visibility races. This first slice buys quiet-pass savings only.

**D-3 — Narrow payload eligibility.** Permit only Held, Aged, Conflict, Outcome,
DispatchBase, DeliveryFailure and unprofiled TaskCompletion, enumerated positively.
Permit either legacy mode but bind that exact mode and kind in the key. Exclude
LegacyCheckNote and unknown/future enum values. Require a real keyed row in the
same destination, row.Body ordinal-equal to note.Body, no pointer headline, no
RemoteSpillBody, no completion snapshot/delivery JSON, and expected text equal to
that ordinary body. Require a nonnegative sequence baseline and a positive
DeliveryAttempts count; timestamp-only and unobservable baselines keep the existing path. Bind the
exact nullable LastDeliveryStartedAt and LastDeliveryGeneration as identity even
when one is absent; null is not a wildcard. No diagnostic/status value grants
eligibility. Rejected: optimizing profile/spill paths in this round; their mutable
external evidence makes a safe negative certificate needlessly larger.
The allowed nonterminal note states are explicitly Queued, RetryPending,
AwaitingReceipt, DestinationUnavailable and Canceled; queue status is explicitly
Pending, Sent or Canceled. Permit a null verdict or the eleven current named
DeliveryVerdict values (Delivered through SpillBodyMissing), enumerated in code;
future/unrecognized states or verdicts fall back, rather than entering through
an Enum.IsDefined rule that silently widens as the enum grows.

**D-4 — Catch-up remains first and failures remain unknown.** Factor the existing
fetch/persist operation into a shared private core in `AgentSessionRuntime.cs`.
Keep public CatchUpTranscriptAsync's Boolean semantics and all existing callers.
Add an internal receipt observation path that distinguishes a successful
fetch/persist/publication from unknown, and reads the **same runtime-owned**
SessionStateStore. It must not resolve a second store or perform a second pull.
Its positive result requires enabled store, non-null accepted generation,
no NeedsReload/retained persist failure, and a Ready stamp whose generation equals
the accepted one. A failed pull never borrows an older successful catch-up result.
Compare generation tokens
with `SessionGeneration.Equal` at PostgreSQL microsecond precision, not elapsed
time or a raw producer-clock ordering. An empty input/no accepted generation
stays unknown. A failed pull, partial/stub persist, failed publication/reseed, missing store, disabled
store, or state-read error permits no reuse/certificate. Caller cancellation
propagates. Ordinary noncancellation optimization errors fall back to the
existing database scan; do not turn them into an early return. Do not add a
resilience retry or widen any timeout.

**D-5 — Match an exact attempt context.** A proof binds NoteId, QueueMessageId,
ParentSessionId, queued row destination/source-note identity, IsLegacy, Kind,
DeliveryAttempts, LastDeliveryBaselineSequence, LastDeliveryStartedAt,
LastDeliveryGeneration, and the exact ordinary expected string. Bind the known
note/queue state and nullable verdict too, conservatively rescanning when those
change. Do not key on note.ConcurrencyToken: ReconcileAsync changes it every
pass. Do not reuse a digest instead of exact string equality. A successful
runtime observation supplies the current generation without an extra per-note
AgentSessions SELECT. Existing TaskCompletion generation refusal stays ahead of
this optimization.

**D-6 — Bounded singleton, no schema.** Add DI-owned `LandReceiptScanCache`; no
static mutable state and no new service interface. At most 1,024 proofs, at most
4,096 UTF-16 characters of expected text per proof, and five minutes absolute
lifetime measured with the clock's monotonic timestamp from the completed scan
(hits never extend it; invalid elapsed-time evidence refuses reuse). Capacity eviction and
expiry cause full scans. These are conservative implementation bounds, not
delivery timers. Use a locked bounded dictionary with insertion/eviction under
that lock; hold no lock across I/O. Terminal notes remove their proof. Restart
starts empty. A superseded concurrent writer may only cause a cache miss, never
erase the binding between a certificate and its own context/stamp. Upper-bound
retained body payload is 8 MiB plus keys/object overhead. No new configuration is
needed; oversized bodies fall back. Rejected: durable fields/migration and an
unbounded dictionary; neither is needed to remove within-process repetition.

**D-7 — Publish only a completed stable miss.** Read the before stamp after
catch-up, run unchanged Prompts/FirstReceiptAsync, and observe the state again
only after the reader exhausted and disposed. Publish only when both stamps and
the bound context agree, result was no match, no error/cancellation occurred, and
the existing final SaveChanges completed. A matching prompt, even if its file
validation or confirmation save fails later, never creates a negative proof.
An existing stale proof cannot become a proof of a newly read revision.
Do not hold the state gate during runner I/O, queue actions or note SaveChanges.
Rejected: cache-on-final-null, stamping before enumeration completes, or a
certificate covering rows arriving during a scan.

**D-8 — Activation and rollback.** Migration: **no**. Index changes: **no**.
AppHost/server restart after code land: **yes**, from the canonical checkout
through the owner runbook, then verify `/api/version` equals its HEAD. Plan-only
publication needs no restart. No runner restart. Existing SessionState.Enabled
false must disable this optimization; it requires the usual server restart and
keeps the prior SQL behavior. Never deploy/restart from this task worktree.

**D-9 — Preserve lifecycle ownership.** No queue send-mode, bracketed paste,
receipt matcher, retention, compaction, parking, release or Working decision
changes. CARD-0079 and LegacyCheckNote remain on their existing path. This plan
adds no session waiting for input, so no new release/deadline checklist applies.
For existing waiting sessions CARD-1083 still applies: parking is off by default
and nothing here automatically releases their seats, after any duration.

### Whitelist and evidence ORDER in the reconciler

All of the following must be positively established, in this order:

1. **Durable identity and existing refusal gates.** Reload note, terminal check,
   destination/queue lookup, rendering/pointer handling, attempt gate and existing
   TaskCompletion generation check, as today. Read-only cache invalidation may
   happen here but cannot change a note's state.
2. **Existing catch-up.** Pull once, persist under the runtime state gate, finish
   publication. Obtain the explicit receipt observation from D-4. Never skip this
   pull because an old cache entry exists.
3. **Current scan definition.** Construct unchanged Prompts and exact expected
   text. If Prompts is null retain today's behavior. Evaluate D-3 and bind D-5;
   any exclusion continues through today's scan.
4. **Reuse evidence.** Only an unexpired, size-admitted, complete-negative proof
   with identical context and the current positive D-2 stamp permits omission of
   FirstReceiptAsync. Continue to the usual final note SaveChanges; a skip is not
   a method-level return and does not suppress error/status bookkeeping.
5. **Fresh scan evidence.** On a miss, capture the before stamp, stream exactly
   today's ordered projection from the original floor, then dispose its reader.
   An optional `land-note.receipt-scan` EF tag on this reconciler query aids
   counting; it must not change the shared Prompts helper or other consumers.
6. **Receipt before optimization.** On a hit run the existing pointer validation
   and receipt persistence/boundary logic; clear negative proof. On no hit, get
   the after stamp. Unknown/different observation means no new proof.
7. **Commit before certificate.** Execute existing SaveChanges. Only its success
   permits publishing the stable negative proof. Exception/cancellation does not
   publish. Cache errors cannot replace receipt handling or suppress a future
   scan.

Why no prompt can be hidden: the certificate comes from the unchanged complete
scan. Committed transcript changes serialize before a state read and change the
revision/reset/epoch; attempt and payload changes change the context. Therefore
an identical positive pair describes the same already-examined predicate and
rows. A concurrent commit after the reuse observation is seen next pass, just
as a commit after today's SELECT snapshot is seen next pass. A commit during a
fresh scan invalidates its certificate. Out-of-process SQL retains the owner's
restart requirement. None of this relies on the highest sequence being complete.

## Slices and collisions

Each slice is 30-60 minutes of implementation/targeted verification, committed
and pushed before its checkpoint group. If a slice grows, split at its named
boundary and amend the closed manifest before starting additional runs.

| Slice | Minutes | Files and deliverable | Tests/checkpoints |
|---|---:|---|---|
| S1 | 45-60 | New `server/Application/Services/LandReceiptScanCache.cs` (context, stamp, whitelist, bounded proof store); `AgentSessionRuntime.cs` shared catch-up core and explicit receipt observation; new `tests/Antiphon.Tests/Application/LandReceiptScanCacheTests.cs`. No reconciler skip enabled yet. | V-1/V-2, CP-1. |
| S2 | 45-60 | `server/Application/Services/AgentTaskLandNotificationService.cs` ordering/integration; `server/Program.cs` singleton registration and optional compatibility injection for hand-built callers; new `tests/Antiphon.Tests/Application/AgentTaskLandReceiptWatermarkTests.cs` and `tests/Antiphon.Tests/TestHelpers/LandReceiptScanHarness.cs`. | V-3 through V-6, CP-2. |
| S3 | 45-60 | New `tests/Antiphon.Tests/Application/AgentTaskLandReceiptWatermarkSafetyTests.cs`; complete deterministic race/fault/command reader probes in the new harness; document reuse/invalidation under the existing committed-projection and receipt paragraphs in `docs/session-runtime-invariants.md`. Only fixes within S1/S2 production scope. | V-7 through V-9 and R-1 through R-3, CP-3 through CP-6. |

The new harness uses BridgeQueueHarness.ConfigureServices to register the real
SessionStateLoader/Store and the cache, plus isolated-schema DbCommand/SaveChanges
interceptors and its fake runner. Feed post-warmup transcript changes through the
real runtime ingest or retention fence, not direct SQL that bypasses publication.
Existing BridgeQueueHarness behavior need not change globally. Offset/advancing
clocks drive the queue; a separately owned cache-only clock may be manually
advanced. Barriers are deterministic and released in finally; no sleeps as proof.

CARD-1149 and CARD-1150 were both InProgress when read. Their S2 owns
`SessionMessageQueueService.DispatchBrief.cs` and `SessionReconciliationService.cs`;
CARD-1151 was InProgress and owns `AgentTaskDispatcher*` (and its boot-stall retry
boundary). **Do not edit those files** or solve their launch/Working defects here.
This scope has no direct path collision with the stated files. It does touch
AgentSessionRuntime and Program plus the shared session owner document; the
orchestrator must recheck actual in-flight scopes before Code and serialize if
another task owns those paths. A broad dispatcher/queue cleanup is not a slice.

## Verification design

TestDesign must first close the measurement gate, qualify the proposed harness
against the current landed tree, and finalize exact PC locations in the eventual
implementation. It must not silently substitute a sequence cursor. These are
behavioral tests of production helpers/reconciliation, not source-text assertions.
Every new method has a production mutation control below. Internal table rows
are labeled assertions, **not** separate executed TUnit results.

### New methods and one-flip tables

| ID | Class.Method (one result each) | Required witness |
|---|---|---|
| V-1 | `LandReceiptScanCacheTests.C1121_OnlyWhitelistedEvidenceReusesNegativeScan` | Start with an actually published eligible negative certificate; exact unchanged control reuses it. Flip one field/condition at a time in the table below; each must refuse reuse. Invoke the same production eligibility/comparison code used by the reconciler. |
| V-2 | `LandReceiptScanCacheTests.C1121_CacheLifetimeAndCapacityFailClosed` | Real bounded cache: five-minute boundary expires, hits do not extend it, 1,025th distinct note evicts within cap, >4,096-character text never retained, terminal removal and a new singleton miss. Concurrent old/new publications retain their own exact bindings. |
| V-3 | `AgentTaskLandReceiptWatermarkTests.C1121_UnchangedTranscriptSkipsOnlyReceiptSelect` | Real reconciler, cold scan then two warm passes, independent one-kind legacy Outcome and two-kind Aged/Outcome fixtures. First pass reads all 48 unrelated candidates; subsequent passes have zero receipt SELECTs. Catch-up still called once/pass, no ConfirmedAt/sequence, keyed row unchanged, Inputs/stops/releases zero. |
| V-4 | `AgentTaskLandReceiptWatermarkTests.C1121_AnyCommittedChangeReopensFullScan` | After a cached miss, real ingest adds matching UserPrompt, matching QueuedUserPrompt, old-timestamp/rebased-sequence matching prompt, or unrelated AssistantText, each in a fresh fixture. Every revision change makes one full scan; only eligible matches confirm, with the same first sequence as the original scanner. Also fence a prune/reseed and ingest lower runner sequences; no stale proof survives. |
| V-5 | `AgentTaskLandReceiptWatermarkTests.C1121_AttemptAndPayloadChangesReopenOriginalFloor` | After a miss, change exactly one of original baseline (lower it to include an existing match), attempts, expected ordinary body (to an already-existing prompt), queue identity, or legacy/kind acceptance. Assert the scan's original-floor parameter, first matching sequence and no retyping. Match rows already present before the change defeat an unsafe suffix cursor. |
| V-6 | `AgentTaskLandReceiptWatermarkTests.C1121_UnknownEvidenceUsesExistingScan` | Independently flip enabled store to disabled/absent, make runner pull fail, fail/reseed a persistence attempt, supply a timestamp-only baseline, a profiled rendering, a body/pointer mismatch, or LegacyCheckNote. Each qualifying existing scan still executes on consecutive passes; no negative certificate. Include a valid old DB receipt with unavailable runner to prove fallback still confirms. Existing refusal gates are asserted separately and never bypassed to force a query. |
| V-7 | `AgentTaskLandReceiptWatermarkSafetyTests.C1121_RacingCommitCannotPublishOrReuseStaleMiss` | Deterministic cuts: receipt commits after full reader exhaustion but before after-stamp; writer holds commit/publication gate before reuse observation; reader throws after one row; reader cancellation after one row. Changed/partial observations never publish a proof. Release all gates, reconcile, assert late receipt at its original sequence and original first-match ordering; cancellation remains cancellation. |
| V-8 | `AgentTaskLandReceiptWatermarkSafetyTests.C1121_MatchedButUncommittedReceiptIsNeverCached` | Match then fail receipt-before-save/final save; a fresh scope with the same cache re-reads and confirms. Separate excluded profiled pointer fixture finds a text match with invalid file hash, repairs the file without appending a prompt, then confirms that same sequence. A negative no-match save failure also publishes no certificate. |
| V-9 | `AgentTaskLandReceiptWatermarkSafetyTests.C1121_ReconcilePinsStatementAndRowBudgets` | Pin counts in the statement-budget subsection; exact SQL shape remains Sequence/Text ordered ascending; parameter floor unchanged; no extra per-note existence/count/max query and no hidden full entity materialization. Witness quiet repeated misses and changed transcript, not only early success. |

V-1 uses a fresh copy of the positive certificate/context for **each** one-flip
row. Distinguish "missing" from zero/false; all defaults must be disqualifying
unless explicitly allowed by D-3. Do not implement a blacklist of known bad cases.

| One-flip group | Independently changed member / evidence | Expected reuse |
|---|---|---|
| identity | NoteId; QueueMessageId; ParentSessionId; queued destination; SourceLandNotificationId | false for each |
| attempt | DeliveryAttempts; baseline; LastDeliveryStartedAt; LastDeliveryGeneration | false for each; explicit null is not equal to a known value |
| payload | exact expected/body; IsLegacy; Kind; queue status; nullable verdict; note state | false for each; unchanged legitimate Canceled/parked state may reuse a negative scan but never confirms |
| eligibility | attempts=0; baseline=null or negative; unsupported kind/state/verdict; LegacyCheckNote; profile snapshot present; profile delivery present; pointer headline; RemoteSpillBody present; unequal queue/note body; oversized text | false for each; exercise every currently supported kind in the positive arm |
| state | store absent/disabled; pull/persist/publication unknown; readiness Cold/Loading/Missing/Faulted; accepted generation absent or inconsistent | false for each |
| certificate | none; incomplete enumeration; match observed; failed save; expired; wrong session/epoch/revision/reset/generation/count/last sequence | false for each |

### Statement-budget pin

Measure DbCommands from **all** fixture scopes, not just the reconciler DbContext;
count reads/writes separately and recognize the receipt SELECT by closed
projection/predicate/order (and its tag if added). Exclude fixture setup, fixture
assertion reads and transaction-control protocol, not production operations.
Reset counters only after seeding/warming. Runtime fake transcript returns one
already-committed UUID-bearing row, so every successful catch-up performs exactly
one session-generation SELECT, one UUID/kind membership SELECT and one MAX query;
it writes no transcript rows. Use a prelinked ordinary Outcome, no profile/spill,
known sequence floor, no TaskCompletion extra generation SELECT, unchanged
AwaitingReceipt state and an enabled admitted state store.

| Case | Receipt commands / client rows | All production commands in the fixture |
|---|---|---|
| Warm state, first negative scan, 48 candidates | 1 / 48 | **9**: note fetch + reload (2), destination existence (1), queue lookup (1), existing catch-up reads (3), receipt SELECT (1), note UPDATE (1). |
| Same note/context/state, second and third pass | 0 / 0 each | **8** each; catch-up still runs, final note UPDATE still runs. Three-pass total **25**, one receipt SELECT, 48 receipt rows. |
| New matching commit between passes | 1 / rows through first match | **9** after separately completing the ingest; confirmation sequence equals the original scanner. |
| Optimization excluded, otherwise same scan fixture | 1 / 48 each | **9** each; no new cache-support DB command. |
| First read of an admitted cold state store | 1 / 48 | At most **10** (one existing session-state seed in addition); subsequent passes meet the warm pin. |

Repeat the receipt command/row pin with 1,506 candidates for the historical
two-kind shape and 852 for one-kind; the number of SQL commands does not grow
with row count. Assert selected columns exclude ToolInput, ApiErrorTimeZoneId
and ModelCalls, as CARD-1073 already does. Count cache proof entries as well as
commands so skipping proof publication cannot pass as a cost improvement.
These exact fixture counts are derived from this source, not a claimed test run.
TestDesign must confirm them before Code; an unexpected command needs a named
cause and plan correction, not a loosened ceiling. No new hot-loop statement is
an accepted implementation choice.

### Regression selection

| ID | Existing selection | Invariant |
|---|---|---|
| R-1 | `AgentTaskLandReceiptTests`, `AgentTaskLandQueuedReceiptTests` | Existing projection/first receipt, floors, queued-kind whitelist, full body and pointer rules, no retyping. |
| R-2 | `SessionStateCommitTests` | Shared runtime core preserves commit-before-publication, partial fallback, collision recovery, restart generation and concurrent ingestion. |
| R-3 | `AgentTaskLandNotificationRecoveryTests`, `ExpectationNoteDebtTests` | Existing recovery remains owed; watchdog's shared receipt predicate/age remains unchanged. |

Do not include a whole Unit/Application/assembly run. If a selected regression
fails, reproduce **that method** at the task base before labeling it inherited;
the historical CARD-1073 red roster is not current evidence. Keep each failure
and baseline outcome in the report; do not widen deadlines/assertions or retry
away a failure. No new test may skip on its required platform/dependency.

### Production mutation controls

Run PCs in the separately commissioned Mutation stage, against its landed
source and external evidence root. Each mutation changes production code only,
one change at a time; baseline, expected assertion red, exact restoration and
freshly built green are all method-scoped. No class/suite PC filter. Every V-1
one-flip field comparison and eligibility arm has its **own** subcontrol under
PC-1; removing an entire policy is not a substitute for killing every guard.
If a guard cannot be made observably red, strengthen its witness before claiming
coverage. Source changes remain frozen during each run.

| PC | Production mutation | Exact detecting filter | Expected red |
|---|---|---|---|
| PC-1.<one-flip-member> | In LandReceiptScanCache, omit only that context/stamp equality or whitelist precondition. Separately force the unchanged positive arm to refuse. | `/*/*/LandReceiptScanCacheTests/C1121_OnlyWhitelistedEvidenceReusesNegativeScan` | That labeled false row becomes true, or the unchanged positive witness becomes false. Includes each readiness, unknown-evidence and complex-payload exclusion independently. |
| PC-2a/b/c/d/e | Independently remove expiry, renew expiry on hit, remove capacity eviction, remove text-size admission, or retain terminal entry. | `/*/*/LandReceiptScanCacheTests/C1121_CacheLifetimeAndCapacityFailClosed` | Boundary/size/retention assertion for the changed rule fails. |
| PC-2f | Store a replacement proof's stamp with the previous context in concurrent publication. | `/*/*/LandReceiptScanCacheTests/C1121_CacheLifetimeAndCapacityFailClosed` | Exact-context lookup or no-cross-binding assertion fails. |
| PC-3a/b | In reconciler, always scan on a cache hit; separately move cache-hit return before catch-up/final bookkeeping. | `/*/*/AgentTaskLandReceiptWatermarkTests/C1121_UnchangedTranscriptSkipsOnlyReceiptSelect` | Repeated receipt SELECT count becomes nonzero; or catch-up/update count is missing. |
| PC-4a/b | In cache, ignore committed revision/count/last-sequence changes as one unsafe shortcut; separately ignore reset/epoch changes. | `/*/*/AgentTaskLandReceiptWatermarkTests/C1121_AnyCommittedChangeReopensFullScan` | New eligible UserPrompt/QueuedUserPrompt fails to confirm, or reset case misses the receipt. |
| PC-5 | Replace original Prompts sequence floor with the prior scan's LastSequence on a context change. | `/*/*/AgentTaskLandReceiptWatermarkTests/C1121_AttemptAndPayloadChangesReopenOriginalFloor` | Already-present newly eligible receipt is hidden; first sequence/floor assertion fails. |
| PC-6a/b | In runtime receipt observation, report positive on runner failure; separately treat retained persist failure/NeedsReload as clean. | `/*/*/AgentTaskLandReceiptWatermarkTests/C1121_UnknownEvidenceUsesExistingScan` | Unknown consecutive passes incorrectly skip a receipt SELECT or publish a certificate. |
| PC-6c | Bypass an integration-level optimization exclusion (profile, pointer, null floor or LegacyCheckNote), one per cycle. | `/*/*/AgentTaskLandReceiptWatermarkTests/C1121_UnknownEvidenceUsesExistingScan` | The excluded fixture incorrectly caches/skips. Ensure the fixture reaches the existing scan before applying this mutation. |
| PC-7a/b/c | Publish a negative proof with the after stamp despite changed before stamp; bypass the serialized state read on reuse; publish after a partial reader failure/cancellation (separate mutations). | `/*/*/AgentTaskLandReceiptWatermarkSafetyTests/C1121_RacingCommitCannotPublishOrReuseStaleMiss` | Barrier/recovery witness observes stale reuse or a wrongly retained certificate; late receipt is missed. |
| PC-8a/b/c | Cache on final evidence=null after pointer failure; publish before final SaveChanges; mark a text hit as a cacheable miss after confirmation-save failure (separate mutations). | `/*/*/AgentTaskLandReceiptWatermarkSafetyTests/C1121_MatchedButUncommittedReceiptIsNeverCached` | Certificate absent assertion fails or the repaired/retried same-sequence receipt fails to confirm. For pointer control, enable the otherwise excluded fixture only as part of this explicit unsafe production mutation. |
| PC-9a/b | Add a per-note transcript MAX/Any probe on the warm reuse path; separately restore full-entity receipt projection. | `/*/*/AgentTaskLandReceiptWatermarkSafetyTests/C1121_ReconcilePinsStatementAndRowBudgets` | Exact command budget or closed projection assertion fails. |

PC-1 expands to method-scoped cycles, not a single broad mutation. Where two
independent guards mask a particular unsafe production line, design the witness
to reach that line or record the precisely scoped multi-line unsafe change;
never count a build failure, fixture failure, zero-test run or unrelated red.
The mutation roster is larger than the implementation; authorize its cost
separately rather than silently executing it during Code.

### Checkpoints

This is the closed ordinary Code/Review list. Each row has one isolated build
and one exact filter. CP-1 is the **managed policy lane**; CP-2 through CP-6 are
the **managed PostgreSQL lane**, with an isolated test schema and owned test
container, no real provider, no production runner. Both lanes are OS-independent.
Use current runner policy; omit `-Runner` and `-Platform` for dispatch (Any is
only needed to clear an existing platform pin). Do not encode a fleet location.
All rows are serial to keep the shared worktree's source/output provenance clear.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1121-policy/` | managed-policy | `/*/*/LandReceiptScanCacheTests/*` | V-1, V-2 | all 2 methods, 0 failed/skipped | 2 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1121-behavior/` | managed-pg-behavior | `/*/*/AgentTaskLandReceiptWatermarkTests/*` | V-3, V-4, V-5, V-6 | all 4 methods, 0 failed/skipped | 4 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1121-safety/` | managed-pg-safety | `/*/*/AgentTaskLandReceiptWatermarkSafetyTests/*` | V-7, V-8, V-9 | all 3 methods, 0 failed/skipped | 3 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c1121-receipts/` | managed-pg-receipts | `/*/*/(AgentTaskLandReceiptTests*)\|(AgentTaskLandQueuedReceiptTests*)/*` | R-1 | all selected including all 12 C1073 results, 0 failed/skipped | 12 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c1121-ingest/` | managed-pg-ingest | `/*/*/SessionStateCommitTests/*` | R-2 | all 8 methods, 0 failed/skipped | 8 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c1121-recovery/` | managed-pg-recovery | `/*/*/(AgentTaskLandNotificationRecoveryTests*)\|(ExpectationNoteDebtTests*)/*` | R-3 | all selected, 0 failed/skipped | 7 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Run via the checkpoint tool, one invocation per committed slice group, e.g.
`run --plan docs/superpowers/plans/2026-10-08-card-1121-receipt-scan-watermark-plan.md --after S1 --expected-source-sha <S1-full-sha>`;
then S2 and S3. Use its documented slot-owning driver; any bootstrap build must
use `scripts/build-slot.ps1` and be named as tool bootstrap, not an extra product
verification. Await every run and continue `wait` while exit is 75; exit 4 is
not run/slot timeout, never an unleased retry. Commit/push before each group and
do not edit source while it runs. Preserve unedited CHECKPOINT lines with counts,
SHA, source-clean and build provenance. Delete only inventoried row-owned
alternate outputs after completion; keep generated receipts/TRX/logs ignored.
Run `scripts/check-evidence-diff.ps1` over the full Code/Review task range.

### Cost

Ordinary checkpoint floor: **34 minutes**, including the six isolated builds.
Implementation slices: about 135-180 minutes including their local checkpoint
work; this is a multi-slice Code dispatch, not this plan's 45-minute budget.
TestDesign includes one 70-second live measurement window plus qualification of
the roster/counts and PC reachability. Separate Mutation will need a costed
expanded PC-1 roster; do not disguise its cycles as the nine ordinary results.

## Platform observation and handoff

Required reads completed: GET `/api/runner-defaults` (revision 2, global default
set, no kind overrides) and GET `/api/session-runners`. The preferred Linux entry
was draining and not accepting work; another Linux entry was accepting and full
(10/10); the Windows entry was eligible (0/2). These are dated observations, not
placement instructions. Re-read at dispatch and obey current defaults/capacity.
The implementation/checkpoint lanes need no particular host or operating system.
The production database measurement and post-land AppHost activation belong to
the canonical database/stack owner lane only.

TestDesign handoff: obtain/attach the fresh post-CARD-1073 delta and eligible
quiet-cohort census; confirm the exact statement fixture counts; qualify all
one-flip/PC witnesses against the real runtime state store; return code only if
the narrow negative-proof cache will address measured repetition. No production
change, restart, schema migration or live-session action was performed by Plan.
