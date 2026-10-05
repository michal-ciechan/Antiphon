# CARD-1056: reconcile retained spill bytes after screen delivery

Plan date: 2026-10-05. Inspected source: `1b30bbe15220a93b9cf68ebfa33384112ae35d82`.
Card: CARD-1056 on Antiphon, read through `scripts/card.ps1 get CARD-1056 -Board Antiphon -Json`.
Stage: Plan; **TestDesign remains a separate next stage**. The dispatch did not fold
test design into Plan. The checkpoint and PC requirements below are the bounded
handoff to that stage, not a claim of executed verification.

## Outcome and scope

A queue row that reached `Sent/Delivered` through Codex's degraded screen verdict
must retain its `RemoteSpillBody` until the same session records the complete
submitted wire prompt after the original attempt floor. Once that receipt exists,
reconciliation changes the verdict to `LateConfirmed` and clears the spill body
without typing, pressing Enter, restarting the session, or charging an attempt.

Use **Q** for the durable queue ID, **W** for its frozen `Body` (the actual typed
pointer), **E** for `RemoteSpillBody`, and **A** for the original attempt metadata:
`LastDeliveryStartedAt`, `LastDeliveryBaselineSequence`, `LastDeliveryGeneration`,
and `DeliveryAttempts`. A complete W proves submission; reading the referenced file
or obtaining a model response is a separate obligation.

The implementation is two independently committed slices of 30–60 minutes each.
It changes queue reconciliation and its focused persistence tests. It does not
change the Codex screen verdict, transport encoding, submission timing, runner
spill writing, provider discovery, or CARD-1029's workspace fixture.

## Ground truth

| Card assumption / requirement | Current code at the inspected SHA | Consequence for this plan |
|---|---|---|
| A genuinely null baseline can produce screen-only Delivered. | `SessionMessageQueueService.CaptureTranscriptBaselineAsync` returns unobservable when there are no stored rows. `WaitForTranscriptConfirmAsync` can return `Confirmed(Screen)` after positive Codex submit evidence. | Exercise the real empty-transcript branch; do not null the baseline after an observable attempt. |
| E is retained at the degraded verdict. | `AcceptedByCompleteUserPrompt` and `StampAttemptVerdict` leave E intact for Screen. | Preserve this behavior and test it before publishing any receipt. |
| A later complete W should release E. | `DeliverNextLockedAsync` selects interrupted Sent via `QueueAttention.InterruptedSent`, which requires a null verdict, then selects Pending. Sent/Delivered is in neither set. | Add an independently selected receipt obligation. Merely changing the matcher cannot repair discovery. |
| Periodic recovery should find a lone owed row. | `QueueAttention.NeedsAttention` unions deliverable Pending and interrupted Sent. `FlushStrandedQueuesAsync` additionally gates origins, liveness, Running and idle. | Receipt reconciliation needs its own discovery/pass before these input-admission gates. |
| Existing late confirmation can be reused unchanged. | `LateConfirmAttemptedMessagesAsync` can park partial matches, turn Sent into Pending, reset SentAt, and its underlying queries admit QueuedUserPrompt and some ToolResult rows. | Reuse complete-text matching primitives, not this mutation/retry routine for an already delivered spill. |
| Original sequence/time floors decide eligibility. | Observable matching uses `Sequence > baseline`; null-baseline matching requires native `Timestamp >= LastDeliveryStartedAt - tolerance`. `CreatedAt` is not that timestamp. | Preserve inclusive timestamp equality, strict sequence inequality, and the originally persisted floor across later flushes/recreation. |
| Preserve the original generation. | A is persisted before input. Existing generic late-confirm queries do not compare receipt timestamps against `LastDeliveryGeneration`; that field primarily guards input recovery. | Preserve the field exactly. Do not invent a new current-generation equality gate or replace the attempt floor with the session's later StartedAt. |
| Runtime ingestion followed by a flush can observe the late receipt. | `AgentSessionRuntime.SyncTranscriptAsync` persists via the production path; only added turn boundaries flush the queue. `CatchUpTranscriptAsync` explicitly has no queue side effects because callers can hold the queue lock. | Test production ingestion and invoke receipt reconciliation through queue flush/sweep. Keep CatchUp side-effect-free. A receipt need not wait for TurnEnd. |
| Existing tests already cover this transition. | `DurableRunnerSpillReceiptTests.Screen_only_delivery_keeps_spill_bytes_and_a_retry_writes_the_same_body` stops after retained bytes and courier replay. It never publishes W and asserts late release. | Add the missing receipt transition and failure/restart controls. |
| CARD-1029 provides clean acceptance here. | The card cites two failing methods from `2d464ec51671801a309a473648b347b10371a617`; their classes/fixture are absent from this branch. Historical method bodies confirm the intended null/old/equal/new timestamp assertions. The card identifies dirty-transplant evidence separately. | Treat those results as the reported reproduction, not a test run on this source. Implement standalone focused tests using present fixtures. |

Inspected owners: `docs/session-runtime-invariants.md` (queue-owned spill,
attempt-generation recovery, complete prompt evidence and state publication),
`docs/project-context.md`, `docs/ops-http.md`, `docs/orchestration-loop.md`, and
`docs/testing-and-build.md`. Relevant production bodies are in
`server/Application/Services/SessionMessageQueueService.cs`, `QueueAttention.cs`,
`AgentSessionRuntime.cs`, and `server/Domain/Entities/SessionQueuedMessage.cs`.

## Decisions

- **D-1 — Model retained delivered spills as a receipt obligation.** Add a shared
  expression in `QueueAttention`, provisionally `DeliveredSpillAwaitingReceipt`:
  `Status == Sent`, `DeliveryVerdict == Delivered`, non-null E, and
  `DeliveryAttempts > 0`. Require an original sequence or start-time floor before
  matching. Scope by queue session in the action query. Do not add a provider-name
  restriction: the stored obligation is the same for any provider taking this
  existing screen fallback. Codex is the required reproduction.
  Reject broadening `InterruptedSent`: that path can re-Enter or retype.
- **D-2 — Reconcile with a dedicated, receipt-only helper under the queue's session
  semaphore.** Load the eligible rows afresh; inspect persisted transcript rows;
  apply no mutation for missing, partial or ineligible evidence. Never call
  `RecoverDeliveryRunLockedAsync`, `HandleTruncationAsync`, `DeliverAsync` or
  `RevertRunAsync` for this set. Reject feeding it directly to the existing
  late-confirm loop, whose truncation policy would reopen delivery.
- **D-3 — Require a complete actual UserPrompt for the new release path.** Restrict
  candidates to Q's session and `Kind == UserPrompt`. Use the existing normalized
  full-body semantics (`PromptSubmissionMatch.RequiresTextMatch` plus
  `IsCompleteIn`), never a marker/head or weak-any-prompt match. Search beyond an
  incomplete candidate for a later complete one. QueuedUserPrompt, ToolResult,
  assistant text and screen redraw are not release receipts. Do not globally
  tighten existing submission matchers in this card.
- **D-4 — Keep A immutable.** With an observable baseline require sequence strictly
  greater than the stored baseline. Otherwise require non-null native Timestamp
  at/after the original start minus the existing configured tolerance. No stored
  floor means no automatic release. Do not recapture a floor, use CreatedAt,
  timestamp a received row with the reconciliation time, or compare against a
  newly resumed session generation. Preserve the established tolerance boundary;
  adding a separate timestamp >= generation restriction would reject the cited
  equality/plus-one controls for a fresh session.
- **D-5 — Commit the release atomically and once.** In one SaveChanges, clear E,
  set `DeliveryVerdict = LateConfirmed`, and stamp `DeliveryVerdictAt` with the
  reconciliation time. Preserve Q, W, spill path, A, `Status == Sent`, and the
  original SentAt. A failed save leaves the durable obligation available to the
  next pass; an already cleared row is no longer a candidate. Reuse existing
  queue-change publication/result plumbing after commit; do not synthesize a
  delivery or turn end. No new schema, receipt column or migration is needed.
- **D-6 — Reach the obligation independently of input eligibility.** Run the helper
  from `OnTurnEndAsync`, `FlushSessionAsync` and `FlushIfIdleAsync` under their
  existing lock, before any working/input-transport gate can skip it. Carry its
  result through the existing queue-change/late-confirm collector where relevant.
  Add a separate receipt-only pass to `FlushStrandedQueuesAsync`, selected using
  the same D-1 expression before the ordinary candidate early return. This pass
  is independent of origin, AlwaysOn, attempts cap, interrupted recovery age/window,
  liveness, status and working-state gates: committed evidence requires no input
  transport. Ordinary pending delivery still runs only through its existing gates.
  Cleared receipts do not increment the sweep's count of newly delivered messages.
  Reject adding these rows to input discovery and relying on downstream luck.
- **D-7 — Retain the current ingestion/locking contract.** Match committed rows only.
  Do not add a queue callback inside PersistTranscript/CatchUp or under the session
  state gate. Normal ingestion plus the next explicit flush or periodic stranded
  pass is the convergence contract, including a persisted UserPrompt with no
  TurnEnd. No new background worker or polling interval is introduced.
- **D-8 — Use current portable fixtures.** The primary proof uses PostgreSQL,
  `BridgeQueueHarness`, its recipient adapter and `EmptyRunnerClient.SetTranscript`,
  followed by real `AgentSessionRuntime.CatchUpTranscriptAsync`/`SyncTranscriptAsync`.
  Only the terminal/provider and transcript source are scripted. This proves queue
  persistence and ingestion, not native Codex/PTY or phone-home transport behavior.
  The unchanged transport needs no new native qualification in this repair.
  Do not transplant the unlanded CARD-1029 fixture or expand its unrelated repair.
- **D-9 — Follow live placement, not a host baked into this artifact.** Read
  `GET /api/runner-defaults` and `GET /api/session-runners` before subsequent
  dispatch. Both succeeded during planning on 2026-10-05: defaults revision 2 has
  no kind override or unresolved reference; an eligible Linux default and an
  eligible Windows runner were present, with differing occupancy. All checkpoints
  below name the portable PostgreSQL integration lane. Omit `-Runner` and
  `-Platform`; there is no OS requirement. Capacity observations are not a slot
  reservation. No fleet address or fixed host is part of the plan.

These are design decisions derived from the card and inspected contracts, not
unanswered product choices. No decision-stage approval is needed before TestDesign.

## Implementation slices

### S1 — Direct receipt reconciliation (45–60 minutes, including CP-1)

Files:

- `server/Application/Services/QueueAttention.cs`: add the separate receipt
  predicate with comments distinguishing it from retry discovery.
- `server/Application/Services/SessionMessageQueueService.cs`: add the locked
  receipt matcher/reconciliation helper and direct-flush integration in D-2–D-7.
- `tests/Antiphon.Tests/Application/SessionMessageQueueDeliveredSpillTests.cs`
  (new): the nine core cases below, with a local fixture built on the present
  `BridgeQueueHarness`. Keep fixture-only helpers private/internal in this file
  unless S2 genuinely needs a shared `TestHelpers/DeliveredSpillFixture.cs`.

Use one owned isolated DB schema per test. Establish the primary row by real
queue enqueue: set the recipient kind to Codex, keep the transcript empty,
capture the adapter's actual submitted W, withhold its transcript publication,
and expose positive Codex submit evidence through the existing adapter screen
controls. Assert null baseline, Sent/Delivered, E retained, and exactly one actual
submission. Publish that captured W through the runner transcript DTO and real
runtime ingestion; do not insert the expected successful receipt directly into
the database. Reload from a fresh DbContext for every durable assertion.

Negative rows can be synthetic adversaries. Do not make the primary reproduction
green by seeding Sent/Delivered or altering its baseline after input. Commit and
push the whole coherent slice before CP-1; record its tested SHA and outcome.

### S2 — Periodic and durable recovery (35–55 minutes, including CP-2/CP-3)

Files:

- `server/Application/Services/SessionMessageQueueService.cs`: D-6's receipt-only
  periodic pass, sharing S1's predicate/helper rather than copying a second matcher.
- `tests/Antiphon.Tests/Application/SessionMessageQueueDeliveredSpillRecoveryTests.cs`
  (new): the five recovery cases below.
- Optional `tests/Antiphon.Tests/TestHelpers/DeliveredSpillFixture.cs`: only shared
  setup extracted from S1; reuse harness `AttachSessionId`/`AttachAgentId`,
  `ConfigureDbContext` and isolated-schema hooks for graph recreation and a
  targeted failed release save. Own/dispose both graphs and remove retained scratch.
- `docs/session-runtime-invariants.md`: document the receipt-only transition,
  original-floor preservation and sweep convergence next to the queue-owned spill
  invariant, linking the two new test classes.

Prove a lone delivered row is discovered with no Pending row, including a
non-AlwaysOn UI-origin session and a row older than the interrupted recovery
window. Prove a busy session releases E after its receipt while unrelated Pending
input stays untouched. Prove graph recreation after screen delivery and after
receipt persistence, and a failed atomic release commit followed by recovery.
Commit/push before CP-2/CP-3. No source edits while a checkpoint is running.

## TestDesign handoff requirements

Append the full `## Verification design` in the next stage, after inspecting the
named fixtures. Preserve this fix design and refine the executable guard inventory,
boundary roster and cost there. Core and recovery method names below are proposed
new single TUnit executions; internal negative/boundary loops are not extra test
counts. If TestDesign uses Arguments, update the manifest to the actual expansion.

Existing bodies read: `DurableRunnerSpillReceiptTests` (11 tests),
`SessionMessageQueueInterruptedAttemptTests` (15 tests), the discovery controls in
`SessionMessageQueueWedgedHeadTests`, `BridgeQueueHarness` (including attach and
runner snapshot paths), and `FakeAgentProtocolAdapter` (composer/submit recording).
`PhoneHomeSpillTransportTests` and `SessionQueueReceiptPlumbingTests` were inspected
as boundary examples: the former stops at transport and the latter's native queue
cases require Windows. Neither substitutes for the new persisted late receipt.

### Behaviors and one positive control per behavior

Core class is `SessionMessageQueueDeliveredSpillTests`; recovery class is
`SessionMessageQueueDeliveredSpillRecoveryTests`. Every PC runs only
`/*/*/<Class>/<ExactMethod>` with separate baseline/red/restored-green receipts.
The compiling production mutations described here are for post-land Mutation;
ordinary Code/Review runs the V/R checkpoint list. Expected assertion failure is
required; zero tests, fixture exceptions and build failures are not a red control.

| ID | Method (class) and decisive behavior | Distinct PC: production defect to inject and expected red |
|---|---|---|
| V-1 / PC-1 | `C1056_Screen_then_complete_prompt_releases_once` (core): E retained before receipt; then LateConfirmed/E null after real ingestion and direct flush; exactly one submission. | Skip the helper's confirmed-row update; final E-null/LateConfirmed assertion fails. |
| R-1 / PC-2 | `C1056_Only_UserPrompt_can_release` (core): identical QueuedUserPrompt, assistant, tool and queue-event bodies retain E; captured UserPrompt releases it. | Admit QueuedUserPrompt in the new candidate query; E-retained assertion fails on that row. |
| R-2 / PC-3 | `C1056_Complete_wire_is_required` (core): head/marker/near-complete W leaves Sent/Delivered and E unchanged; a later complete W wins over earlier partial evidence. | Replace whole-body acceptance with identity/head acceptance; partial-prompt E-retained assertion fails. |
| R-3 / PC-4 | `C1056_Receipt_session_must_match` (core): another session's complete W cannot discharge Q. | Remove the session predicate from the receipt query; E-retained assertion fails. |
| R-4 / PC-5 | `C1056_Sequence_must_exceed_original_floor` (core): stored sequence at/below B is rejected; B+1 qualifies. This is a seeded historical receipt-obligation case, not a forged null-baseline reproduction. | Change `> B` to `>= B`; at-floor retention assertion fails. |
| R-5 / PC-6 | `C1056_Null_timestamp_cannot_release_unobservable_spill` (core): native null timestamp remains ineligible despite new CreatedAt/sequence. | Coalesce null Timestamp to CreatedAt; E-retained assertion fails. |
| R-6 / PC-7 | `C1056_Timestamp_uses_original_attempt_floor` (core): with null B, test start-minus-tolerance minus one database-representable tick, equality, and plus one; advance the test clock before reconciling; old stays owed and current captured W can recover it. | Calculate confirmFrom from reconciliation time rather than original start; equality/current receipt release fails. TestDesign also pins the lower-bound guard separately if its bypass is independently testable. |
| R-7 / PC-8 | `C1056_Unattempted_rows_are_not_reconciled` (core): seeded inconsistent Delivered/zero-attempt row stays unchanged despite matching text. | Remove the attempts-positive guard from the shared predicate; E-retained assertion fails. |
| R-8 / PC-9 | `C1056_Floorless_rows_are_not_reconciled` (core): attempts-positive but both floors absent retains E. | Use MinValue/zero as a fallback floor; E-retained assertion fails. |
| V-2 / PC-10 | `C1056_Stranded_sweep_finds_delivered_spill_without_pending` (recovery): discover a lone owed row, including non-AlwaysOn/UI origin and outside retry age/window; sweep releases it and reports zero newly typed deliveries. | Omit the receipt-only discovery/pass; final E-null assertion fails. |
| V-3 / PC-11 | `C1056_Reconcile_while_working_does_not_send_pending` (recovery): publish W without TurnEnd; direct flush and sweep can release it while a queued unrelated body remains Pending; Inputs/submissions/attempts do not increase. | Route receipt reconciliation through ordinary idle admission; busy-session E-null assertion fails. Also retain decisive no-input assertions for any accidental retry-path wiring. |
| V-4 / PC-12 | `C1056_Fresh_graph_reconciles_committed_receipt` (recovery): fresh service graph reads Q/E/A; captured recipient W is persisted through production ingestion; completion survives another graph replacement with no new submission. | Require an in-memory staged spill lookup before matching; fresh-graph release assertion fails. |
| R-9 / PC-13 | `C1056_Failed_release_commit_retries_atomically` (recovery): interceptor refuses precisely the verdict/E update; fresh context sees Delivered plus E, next fresh graph releases both atomically. | Split verdict and E into separate saves with verdict first; the injected failure leaves the forbidden partial durable state and the atomic-state assertion fails. |
| R-10 / PC-14 | `C1056_Confirmation_preserves_attempt_identity_and_is_idempotent` (recovery): Q/W/path/A/SentAt identical after receipt; second pass makes no further change; second unrelated spill stays owned. | Replace LastDeliveryStartedAt on confirmation with now; original-metadata equality fails. |

TestDesign must split independently bypassable safety guards into additional PCs
where needed (notably both timestamp-floor errors and no-input safety), rather
than treating this behavior roster as permission to omit a guard. Every resulting
PC must name one method and its failing assertion. Use PostgreSQL microsecond
precision for timestamp boundary offsets; a .NET 100 ns tick can collapse in storage.

The primary fixture does not need a second submission for positive evidence: release
the captured recipient W. Busy-before-initial-delivery coverage remains in the
existing `Queued_brief_is_written_by_the_runner_when_a_busy_recipient_becomes_eligible`;
the new busy case covers *after* screen acceptance and before TurnEnd. A truly empty
transcript must not be faked by deleting the activity that established a busy state.

### Checkpoint execution and cost

Lane for **every** row: **portable PostgreSQL integration**, eligible default runner,
no OS/host pin. No whole-Unit, namespace-wide or assembly-wide run is authorized.
The two existing classes exercise spill ownership and interrupted retry invariants;
the four selected wedged-head methods specifically guard unchanged discovery/caps.

Run the manifest through `tools/Antiphon.Checkpoints` once per committed After
group with `--expected-source-sha <that group's commit>`; wait until the run is
finished (exit other than 75). Use the documented build-slot-gated tool bootstrap
and alternate `bin-c1056-tool/` output if a bootstrap build is needed; count it as
setup, not an unreported test. Each row's driver takes its own host build slot.
Slot timeout is not-run/blocked, never permission for an unleased retry. Keep source
frozen, preserve unedited CHECKPOINT lines and validate clean SHA/build provenance.
Remove owned alternate bin directories before finishing. Generated receipts/TRX
stay ignored; Code/Review run the task-range evidence diff guard.
Independent ordinary Review runs all three rows against the final candidate SHA;
an S1 receipt alone is not evidence for the final S2 source. Reuse CP-2 output for
CP-3 only at that same SHA and with matching build provenance.

Estimated ordinary CP floor: **21 minutes** (10 + 10 + 1), plus 2–4 minutes tool
bootstrap if required and roughly 55–85 minutes authoring across the two slices.
Proposed roster: **47 executions** (20 + 20 + 7), zero failed/skipped. Counts are
source-derived/planned, not measured in this Plan dispatch. TestDesign verifies the
exact roster before Code. Mutation estimate for the 14 proposed PCs: **70–98
minutes** for separate red/restored-green builds and exact methods, plus a 10-minute
baseline; adjust for additional guard PCs explicitly. No repeated full suite is
part of either estimate; wider native CARD-1029 and SourceLanding qualifications
are separate obligations.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1056-core/` | portable-pg-spill-core | `/*/Antiphon.Tests.Application/(SessionMessageQueueDeliveredSpillTests*)\|(DurableRunnerSpillReceiptTests*)/*` | V-1, R-1–R-8; existing spill ownership/release | all 9 new core and 11 existing methods, 0 failed/skipped | 20 | 10 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1056-recovery/` | portable-pg-spill-recovery | `/*/Antiphon.Tests.Application/(SessionMessageQueueDeliveredSpillRecoveryTests*)\|(SessionMessageQueueInterruptedAttemptTests*)/*` | V-2–V-4, R-9–R-10; existing interrupted recovery | all 5 new recovery and 15 existing methods, 0 failed/skipped | 20 | 10 |
| CP-3 | S2 | CP-2 | portable-pg-discovery-regression | `/*/Antiphon.Tests.Application/SessionMessageQueueWedgedHeadTests/(A_lone_capped_crashed_row_is_discovered_and_parked_by_the_sweep*)\|(A_parked_row_stops_being_discovered_once_it_is_at_rest*)\|(Fresh_NoSubmitOutput_is_discovered_before_stranded_age_and_delivered_whole*)\|(Fresh_ordinary_Pending_is_excluded_until_stranded_age*)` | R-11 unchanged retry discovery, parked caps and fresh Pending age | all 4 named methods / 7 argument-expanded executions, 0 failed/skipped | 7 | 1 |

## Limits and handoff

No test or build was run while writing this plan. The source selectors corroborate
the card's root-cause lead; clean-source reproduction is the first implementation
proof. If the primary focused test unexpectedly passes on the unchanged base,
stop and investigate the discrepancy rather than weakening its assertions.

This obligation applies while Q and its receipt are retained. Existing retention
policy can eventually prune settled queue/session history; this card introduces no
new unlimited-retention promise or retention schema. It also does not delete the
runner's inbox file when clearing E. Generic queued-submission semantics, pending
retry/truncation policy, legacy courier reconstruction, channel reply settlement,
live native Codex qualification and CARD-1029 fixture work remain outside this fix.

Next: **test-design**. Complete the verification design and guard inventory in this
artifact, confirm the 47-case narrow manifest/fixture setup, and then hand Code the
pinned plan commit. Ordinary Code -> independent Review -> land precedes separate
SourceLanding Mutation; these ordinary results discharge no previously owed PCs.
