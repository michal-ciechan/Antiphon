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

### Proposed checkpoints (Plan-stage record)

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

## Verification design

TestDesign inspected `a672022f59c6a8eeb04bd65745e1bca828c7c7de` on 2026-10-05.
This appendix completes the Plan handoff without changing D-1–D-9 or S1/S2. The
previous checkpoint heading is relabeled as a proposal because the importer selects
the first exact heading; the table below is authoritative. No executed verification
is claimed in this TestDesign dispatch.

### Inspection

Paths are repository-relative. Neither proposed new test file exists yet.

| Bodies read | Boundaries -> verification or exclusion |
|---|---|
| `server/Application/Services/SessionMessageQueueService.cs`: enqueue commit/staging, all three direct flushes, stranded sweep, DeliverNextLockedAsync, late confirmation, attempt stamping, baseline capture, transcript confirm/unobservable matcher, collector and publication | Discovery/action separation, input exclusion, floors, atomic release and result plumbing -> V-1–V-4, R-1–R-13. |
| `QueueAttention.cs`, `server/Domain/Entities/SessionQueuedMessage.cs`, `src/Antiphon.SessionRunner.Contracts/PromptSubmissionMatch.cs` (whole bodies) | Each predicate conjunct, session scope, weak-body refusal and complete normalized W -> R-1–R-11. |
| `AgentSessionRuntime.cs`: CatchUp/Sync, PersistTranscript/Core and commit/publication, liveness, transport admission, TryRemove; `CodexWorkingIndicator.IsVisible` | UUID dedup, rebased sequence, native timestamp, committed evidence and lock order -> V-1, V-3, V-4, R-5–R-6, R-9. |
| `tests/Antiphon.Tests/Application/DurableRunnerSpillReceiptTests.cs`: all 11 methods and BindRunnerAsync | Nearest fixture for both new files; ownership, busy recipient, screen-only retention and courier replay -> V-1, V-4, R-12. Existing screen-only method defaults to Claude, so it is not the required Codex reproduction. |
| `SessionMessageQueueInterruptedAttemptTests.cs`: all 15 methods | Existing retry/receipt separation, Enter-only, window, unavailable snapshot and RC barriers -> R-13. |
| `SessionMessageQueueWedgedHeadTests.cs`: four CP-3 bodies plus CreateAsync, GenerationAsync, MessageAsync, SeedCrashedAtCapAsync, AssertDiscoveryAsync and AssertUserPromptsAsync | 1+2+2+2 argument-expanded results; independent production predicate assertions -> R-11. |
| `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs`, `QueuedReceiptAssertions.cs`, `TestDbFixture.cs`, `ScaledTimeProvider.cs`, `MockEventBus.cs`, `SingleRunnerDirectory.cs`, `RemoteControlRecoveryHarness.cs`; `tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs` (whole bodies) | Attach/dispose, isolated store, input capture, default direct inserts, clocks, faults and unavailable directory -> new fixture setup and existing C514 dependencies. |
| `SessionMessageQueueDeliveryVerificationTests.Codex_unobservable_working_indicator_confirms_by_screen`, `C475_OverlayRecoveryPullsBeforeWorkingDecision`; `TranscriptBindingIncidentTests.UnwritableIncidentInterceptor` | Existing screen string, runner DTO constructor and interceptor hook examples; no added ordinary filter. |
| `PhoneHomeSpillTransportTests`: three transport bodies and RunnerSide; `SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`, Windows/limiter declarations and InsertFault | Boundary examples only: file/frame evidence does not prove UserPrompt, and native queue fixture requires Windows. Excluded from portable scope, not substituted for receipt evidence. |
| `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs`: ExtractSection/SplitRow; Coverage/PlanCoverageReader section handling; testing, project, session-runtime and orchestration owner policies | Single manifest heading, escaped OR pipes, execution counts, build reuse and stage order -> CP-1–CP-3. |

**Missing setup to implement in Code, using existing seams:**

- One `CreateIsolatedSchemaAsync()` owner per new method, fresh sessions per internal
  case. Despite its name, this creates a **cloned database**, not a SearchPath schema.
  Pass its ConnectionString to every context/graph; never use the default CreateContext
  in new tests. Register RemoteSpillCourier and bind RunnerId/RunnerStoreId/RunnerCwd
  together to an owned portable temporary workspace.
- Set AgentKind=Codex before real WhenIdle enqueue into an empty transcript. Replace
  OnSubmitted on every graph: capture actual text, insert no transcript or TurnEnd.
  Keep SwallowSubmits=0; use SubmitAck `\n• Working (0s • esc to interrupt)`.
  The existing working-indicator example uses SwallowSubmits=99, which is unsuitable
  here because no body is submitted. Assert pre-enqueue transcript count zero, then
  one submitted W, persisted null baseline, Sent/Delivered and E retained. Snapshot
  A, Inputs, ConditionalInputs, Prompts and lifecycle at this boundary.
- Publish **captured W**, never a reconstructed expected string, via
  EmptyRunnerClient.SetTranscript and real CatchUpTranscriptAsync/SyncTranscriptAsync.
  Use matching session, UserPrompt kind, a stable unique UUID and controlled native
  Timestamp. Fresh-context assertions check that exact complete prompt/UUID/session
  and its original floor. QueuedReceiptAssertions' callback directly inserts EF rows
  despite its comment; it cannot supply this primary ingestion proof.
- Use `new ScaledTimeProvider(1)` through TimeProvider, with Advance for clock movement.
  Timers keep running. Normalize stored attempt times; T-1/T/T+1 uses **10 .NET ticks
  (one PostgreSQL microsecond)**. Advance beyond tolerance before reconciliation;
  verdict time must lie in its before/after clock interval. Never freeze queue timers.
- Add local SaveChangesInterceptor modes for Added queue row, Added transcript row
  with the captured UUID, and Modified Q clearing previously non-null E. Arm after
  setup; count intended fault hits. The E-clear fault must allow a verdict-only save
  so PC-13 exposes split atomicity. For failed ingestion, throw a non-DbUpdate injected
  exception while armed, or refuse every row fallback too: one failed batch is repaired
  by production fallback and cannot prove failed persistence.
- Recreate with PreserveDatabaseOnDispose, AttachSessionId/AttachAgentId, the same
  database/clock, and a separately retained recipient snapshot. Dispose the old graph
  before creating the next. Override the attached graph's default callback again.
  Retain and delete all TempRoot paths after graphs/contexts close, including preserved
  roots. Every durable assertion reloads through a fresh context.
- For absent liveness use Runtime.TryRemove; the empty runner List then proves absence.
  Pid=null does not remove registration. For independent transport refusal add a local
  directory fixture patterned on SingleRunnerDirectory: known bound runner, matching
  remote binding and live inventory, but Resolve throws typed Unavailable when armed
  after screen acceptance. Record Resolve calls. No live broker is involved.
- For lock ownership hold Queue.GetLock, call FlushIfIdle with a five-second token,
  catch cancellation, release in finally and await all work. Assert cancellation while
  waiting and no mutation; an unlocked mutant completes and fails the assertion.
  Separately CatchUp under the held lock must complete/persist within five seconds;
  catch cancellation into an explicit failed assertion, so recursive flush is red
  without an orphaned wait. These test budgets do not change production timeouts.

### Delivery inventory

The changed async path is Q's outstanding receipt obligation, not a new input
transport. The initial real queue delivery establishes Q/W/E/A honestly. Enqueue,
Sent, a screen, input request, event or ack never proves recipient delivery: require
the complete persisted **actual submitted W** joined by session, original A and Q's
pointer path. Clearing E proves W submission, not that the model read the file.

| Handoff and durable identity | Producer -> destination / persistence boundary | Recovery and observable receipt |
|---|---|---|
| H1: Q/W/E | Producer -> queue row; E/path bound to Q and committed before input | V-4 refuses first queue insert: no durable row/input/UserPrompt. Caller retries real Enqueue and finishes through complete UserPrompt. Failed insert has no committed Q; successful Q is fixed thereafter. Also commit with deliverIfIdle=false, recreate before first flush, and finish that same Q. |
| H2: Q/W/A | Real WhenIdle queue -> fake recipient; attempt/frozen wire commit before body/Enter, then screen Sent/Delivered retains E | V-1 starts already eligible with empty history. R-12's busy-recipient method runs busy -> idle -> complete UserPrompt. V-4 covers committed queue/lost wakeup. R-13 retains interrupted-attempt controls; their seeded state is not a native crash reproduction. |
| H3: session/UUID/W/A | Captured recipient W -> runner DTO -> AgentSessionRuntime -> committed TranscriptEntry | V-4 recreates after screen acceptance before ingestion, and after receipt persistence before release. R-9 refuses ingestion for the UUID; E remains, then fresh graph replays the same DTO and persists complete UserPrompt. Snapshot-present-but-uncommitted is separately checked. |
| H4: Q/E/A + receipt UUID | Direct flush or sweep -> locked receipt helper -> single save of E=null/LateConfirmed/verdict time | R-9 refuses E-clear save: old complete durable tuple survives, fresh graph converges without input. V-2 discovers lone obligation; V-3 is busy/no TurnEnd; R-10 repeats/recreates after commit and preserves another owed spill. |
| H5: Q/session/confirmed-ID sets | Committed release -> existing QueueChanged / OnTurnEnd collector -> queue consumers | V-1 checks post-commit event and owning IDs, including channel subset. R-9 uses MockEventBus.ThrowOnceOnEvent after commit, then recreates and reads GetQueue without redelivery. This remains a best-effort refresh hint, not a new durable event/UI-delivery obligation. |

H1/H3/H4/H5 respectively include enqueue, ingestion, release and publication failure.
Every successful new recovery arm finishes with matching complete UserPrompt evidence.
Crash cuts use graph disposal/fault injection, not OS termination. H2's unchanged
native body-before-Enter/runner crashes remain separate transport qualification;
H1/H3 do not claim to prove them. The fake adapter substitutes for Codex/PTY, and
SetTranscript substitutes for provider JSONL/snapshot transport. Queue, PostgreSQL,
runtime ingestion and matching are production code. These substitutes cannot prove
native paste encoding, socket delivery, provider file-reading or model response.

### Proves it works now

Each new method is one `[Test]`, no Arguments/data source. Internal cases use fresh
sessions and diagnostic assertion messages, not extra TUnit counts. All are portable
PostgreSQL integration tests.

| ID | Behavior / layer | Test / command | Expected |
|---|---|---|---|
| V-1 | Screen -> committed receipt -> release; queue/runtime/DB | `SessionMessageQueueDeliveredSpillTests.C1056_Screen_then_complete_prompt_releases_once` / CP-1 | Real empty-history Codex enqueue retains E before receipt. Independent first-release cases for FlushSession, FlushIfIdle, OnTurnEnd: complete captured W gives Sent/LateConfirmed/E=null, current verdict time, Q/W/path/A/SentAt preserved, no new input. OnTurnEnd returns Q, and channel Q in both ID sets. QueueChanged after commit; no synthetic transcript/SessionFinished. Seeded ClaudeCode/Grok obligations test provider neutrality. |
| V-2 | Independent lone receipt discovery; sweep/DB | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Stranded_sweep_finds_delivered_spill_without_pending` / CP-2 | No Pending companion. Every eligible receipt releases across the input-admission matrix below. Zero newly delivered count and zero additional input. |
| V-3 | Busy receipt, no TurnEnd; runtime/queue | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Reconcile_while_working_does_not_send_pending` / CP-2 | Captured UserPrompt makes recipient working. Real enqueue of unrelated WhenIdle body stays Pending/attempts=0. Independently both direct flushes and sweep release E without touching pending tuple/input counts. CatchUp under queue lock persists without callback; Sync with only UserPrompt leaves E until explicit flush. Held-lock flush cancels safely; unlocked flush succeeds. |
| V-4 | Durable restart/lost-wakeup convergence; queue/recipient/DB | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Fresh_graph_reconciles_committed_receipt` / CP-2 | Internal cuts: failed insert/retry; committed Q before first flush; screen verdict before ingestion; committed receipt before reconcile; release before next graph. Preserve committed Q/W/E/A where applicable, actual submission once across graphs, one complete UserPrompt after replay, no staged-memory dependency. |

V-2 neutral case: live/Running/idle/AlwaysOn/Delegation, attempts=1, within interrupted
minimum/maximum ages. Isolate one factor per case: Ui vs machine origins; AlwaysOn=false
with Delegation (avoids origin masking); non-AlwaysOn Ui; attempts at cap/cap+1;
attempt younger than InterruptedAttemptAge; original attempt older than
InterruptedAttemptWindow; every non-Running SessionStatus with live registration;
Running with recipient absent; and Running/live/known runner with Resolve unavailable.
Include all-adverse Ui/non-AlwaysOn/old/cap+1/Stopped/absent-transport combination.
Age by advancing clock, never overwriting original A. Cap variants are identified
historical synthetic obligations. Idle cases ingest a real TurnEnd after W; V-3
has no TurnEnd. Independent factors plus conjunction replace a redundant full
Cartesian product, while exposing each gate independently.

### Guards the regression

| ID | Regression | Test and decisive assertion |
|---|---|---|
| R-1 | Wrong receipt kind | `SessionMessageQueueDeliveredSpillTests.C1056_Only_UserPrompt_can_release`: identical W in QueuedUserPrompt, AssistantText, ToolResult (AskUserQuestion and completed-answer shapes), queue-operation and screen evidence retains entire tuple; captured UserPrompt finally releases. |
| R-2 | Partial/weak evidence, premature stop on first candidate | `SessionMessageQueueDeliveredSpillTests.C1056_Complete_wire_is_required`: marker-only, first 200 chars, tail, head+tail missing middle, missing final non-space character, unrelated/null/empty text retain E without parking/revert. For this test stage W longer than 200 chars but below runner limit, so a head is truly partial. Seeded normalized W lengths 0/11 retain; 12 is positive. Full W with ANSI/CRLF, whitespace elision or framing qualifies. Partial then complete in one batch qualifies. |
| R-3 | Cross-session receipt or action | `SessionMessageQueueDeliveredSpillTests.C1056_Receipt_session_must_match`: foreign identical W cannot discharge local Q. Separately, another session has its own eligible Q and complete receipt; flushing local session must leave that Q untouched. Finish local via captured own receipt. |
| R-4 | Wrong sequence or timestamp override | `SessionMessageQueueDeliveredSpillTests.C1056_Sequence_must_exceed_original_floor`: seeded historical B, independent B-1/B/B+1 crossed with null/old/current native timestamps. At/below B retains even with fresh time; B+1 releases even with null/old time. Check stored sequences after ingestion, which can rebase DTO numbers. |
| R-5 | CreatedAt/high sequence replaces native timestamp | `SessionMessageQueueDeliveredSpillTests.C1056_Null_timestamp_cannot_release_unobservable_spill`: real null-baseline attempt, null native time/new CreatedAt/high sequence retains; same captured W with distinct UUID and eligible native time releases. |
| R-6 | Moved floor or wrong equality | `SessionMessageQueueDeliveredSpillTests.C1056_Timestamp_uses_original_attempt_floor`: null B, T=start-max(0,tolerance), T-1 microsecond/T/T+1 in separate sessions, after clock advance, with both unchanged and later session StartedAt generations. Old retains, equality/+1 release. Repeat tolerance 0, default 30 and negative (clamped 0); current receipt recovers old arm. |
| R-7 | Ineligible candidate conjunct | `SessionMessageQueueDeliveredSpillTests.C1056_Unattempted_rows_are_not_reconciled`: PostgreSQL predicate and public busy flush isolate attempts 0/1, Pending/Canceled/Sent, null/NoSubmitOutput/LateConfirmed/Delivered verdict, E absent/present; all other factors eligible. Excluded IDs and durable tuples unchanged. Predicate assertions prevent downstream retry logic masking a guard. |
| R-8 | Floorless accepts arbitrary history | `SessionMessageQueueDeliveredSpillTests.C1056_Floorless_rows_are_not_reconciled`: attempts=1/B=null/start=null retains despite complete prompt; B-only/start-only qualify. Explicitly clear floors only on synthetic adversary because seed helper supplies start automatically; never alter primary reproduction's baseline. |
| R-9 | Failure loses obligation or publishes uncommitted release | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Failed_release_commit_retries_atomically`: uncommitted snapshot/failed ingestion retain E then replay/ingest/release; E-clear save refusal leaves Delivered/E/original verdict time with no release event; fresh graph commits both fields. Failed post-commit event leaves durable release/GetQueue intact after recreation, no extra submission. |
| R-10 | Identity, generation, idempotence or another spill damaged | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Confirmation_preserves_attempt_identity_and_is_idempotent`: compare full Q/session/W/path/A/Status/SentAt tuple after current generation changes; distinct second owed W keeps E. Repeat flush/sweep/recreate: release time, transcript count, inputs and lifecycle unchanged. |
| R-11 | Ordinary discovery/caps drift | CP-3's four existing methods: lone capped crash becomes Pending without input; parked row excluded by both production predicates; fresh NoSubmitOutput included and complete UserPrompt recorded; fresh ordinary Pending excluded with attempts=0/no input. |
| R-12 | Existing spill ownership/courier contracts drift | All 11 inspected `DurableRunnerSpillReceiptTests` methods in CP-1: busy-to-idle complete UserPrompt, screen E retention, pre-SendNow failure retains reconstructable bytes, missing-body refusal. Not substitutes for V-1 runtime ingestion. |
| R-13 | Interrupted recovery confused with delivered receipts | All 15 inspected `SessionMessageQueueInterruptedAttemptTests` methods in CP-2: preserve existing late-confirm, Enter-only, retype, modal, snapshot and window assertions. No global matcher tightening or fixture rewrite. |

Successful new primary receipt evidence always comes from actual capture and runtime
ingestion. Synthetic inconsistent queue rows isolate defensive/historical boundaries.
Assert each negative pass before adding positive evidence. Each direct entry needs
its own first-release assertion before any other entry or fallback sweep runs.

### Guard inventory

Scope: D-1–D-7 receipt/recovery invariants, initial durable handoff used by the proof,
and D-6's explicitly protected ordinary discovery predicates. Companion suites add
unchanged-contract coverage; they do not commission a mutation audit of unrelated
legacy RC/paste/provider internals. Each independently bypassable guard maps 1:1.
The immutable persisted tuple is one write-footprint invariant; R-10 asserts every
member, not only the member perturbed by PC-14.

| Guard | Plan reference and safety-critical guard/invariant | PC |
|---|---|---|
| G-1 | D-5: eligible complete receipt performs durable release | PC-1 |
| G-2 | D-3: only actual UserPrompt can release | PC-2 |
| G-3 | D-3: require complete W, not head identity | PC-3 |
| G-4 | D-3: receipt belongs to Q's session | PC-4 |
| G-5 | D-4: sequence strictly exceeds B | PC-5 |
| G-6 | D-4: native Timestamp required with null B | PC-6 |
| G-7 | D-4: original attempt defines time floor | PC-7 |
| G-8 | D-1: attempts positive | PC-8 |
| G-9 | D-1/D-4: original floor exists | PC-9 |
| G-10 | D-6: independently discover lone obligation before Pending early return | PC-10 |
| G-11 | D-6: reconcile while recipient works | PC-11 |
| G-12 | D-5: fresh graph reads durable obligation, not staged memory | PC-12 |
| G-13 | D-5: atomic release/verdict on failed commit | PC-13 |
| G-14 | D-4/D-5: immutable Q/W/path/A/Status/SentAt write footprint | PC-14 |
| G-15 | D-4: reject native timestamp below T | PC-15 |
| G-16 | D-4: equality at T is eligible | PC-16 |
| G-17 | D-3: weak/unidentifiable W cannot release | PC-17 |
| G-18 | D-1: candidate Status is Sent | PC-18 |
| G-19 | D-1: candidate verdict is Delivered | PC-19 |
| G-20 | D-1/D-5: candidate still owns non-null E | PC-20 |
| G-21 | D-1/D-2: action rows scoped to requested session | PC-21 |
| G-22 | D-2: no terminal bytes, including Enter | PC-22 |
| G-23 | D-2: never kill/restart recipient | PC-23 |
| G-24 | D-2/D-3: partial evidence cannot park/revert/change attempt | PC-24 |
| G-25 | D-3: later complete candidate wins over earlier partial | PC-25 |
| G-26 | D-4: B takes precedence over timestamp arm | PC-26 |
| G-27 | D-4: later generation cannot invalidate eligible old receipt | PC-27 |
| G-28 | D-6: FlushSession independently reaches helper | PC-28 |
| G-29 | D-6: FlushIfIdle independently reaches helper | PC-29 |
| G-30 | D-6: OnTurnEnd independently reaches helper | PC-30 |
| G-31 | D-6: OnTurnEnd carries confirmed Q IDs | PC-31 |
| G-32 | D-6: origin-independent receipt discovery | PC-32 |
| G-33 | D-6: AlwaysOn-independent receipt discovery | PC-33 |
| G-34 | D-6: attempts cap cannot suppress receipt | PC-34 |
| G-35 | D-6: interrupted minimum age cannot delay receipt | PC-35 |
| G-36 | D-6: interrupted maximum window cannot abandon receipt | PC-36 |
| G-37 | D-6: no live recipient required for committed receipt | PC-37 |
| G-38 | D-6: no Running status required for committed receipt | PC-38 |
| G-39 | D-6: receipt-only pass counts zero new typed deliveries | PC-39 |
| G-40 | D-5: no release publication before successful commit | PC-40 |
| G-41 | D-7: CatchUp has no queue side effects under queue lock | PC-41 |
| G-42 | D-7: uncommitted runner snapshot cannot release | PC-42 |
| G-43 | D-2: shared session semaphore owns reconciliation | PC-43 |
| G-44 | S1/D-8: queue persistence before initial input | PC-44 |
| G-45 | D-6: ordinary interrupted Sent at cap discoverable | PC-45 |
| G-46 | D-6: ordinary parked Pending excluded at rest | PC-46 |
| G-47 | D-6: fresh NoSubmitOutput discoverable in window | PC-47 |
| G-48 | D-6: fresh ordinary Pending waits for stranded age | PC-48 |
| G-49 | D-5/D-7: receipt-only flush creates no finish/turn boundary | PC-49 |
| G-50 | D-1: provider-neutral stored obligation | PC-50 |
| G-51 | D-5: release reaches existing queue-change plumbing | PC-51 |
| G-52 | D-6: ordinary Pending still working-gated | PC-52 |
| G-53 | D-6: input transport unavailability cannot suppress receipt | PC-53 |
| G-54 | D-6: collector includes Channel Q in channel set | PC-54 |
| G-55 | D-5: initial screen verdict retains E until complete UserPrompt | PC-55 |
| G-56 | D-4: negative configured tolerance clamps to zero | PC-56 |
| G-57 | D-4: sequence comparison uses original persisted B, never recaptured max | PC-57 |
| G-58 | D-4: no extra timestamp floor at session generation | PC-58 |

### Positive controls

Code implements ordinary V/R and the named assertion messages below. Review judges
the design before land. SourceLanding Mutation executes baseline / break-red /
restore-green after land, with a distinct output/results path per phase and PC.
PC-1–PC-14 preserve the proposed numbering; PC-15–PC-58 split additional guards.
A build/fixture error, escaping timeout, skipped or zero-test run is not red.

Every control uses exactly `/*/*/<class>/<method>` from its last cell. Only existing
argument-expanded PC-46/47/48 add a trailing wildcard on that exact method, with
MinExecuted=2; all others use the literal method and MinExecuted=1. No whole-class,
namespace or Unit run. Require the named decisive assertion to fail with driver
exit 1, restore production source/timestamps, rebuild and require exit 0. No batching
controls that share a production file/method; nearly all here share the queue service.
Never commit sourced mutations or generated evidence.

| PC | Compiling production defect (break matching G-n) | Exact method and expected red assertion |
|---|---|---|
| PC-1 | Skip new helper's confirmed-row update/save. | `SessionMessageQueueDeliveredSpillTests.C1056_Screen_then_complete_prompt_releases_once`: assertion `release-complete` — fresh row E null and LateConfirmed. |
| PC-2 | Broaden candidate kind equality to include QueuedUserPrompt. | `SessionMessageQueueDeliveredSpillTests.C1056_Only_UserPrompt_can_release`: assertion `non-user-retains` — queued prompt alone retains E. |
| PC-3 | Replace new IsCompleteIn check with IsConfirmedBy. | `SessionMessageQueueDeliveredSpillTests.C1056_Complete_wire_is_required`: assertion `partial-retains` — first-200-char receipt retains E. |
| PC-4 | Remove only receipt-query session predicate. | `SessionMessageQueueDeliveredSpillTests.C1056_Receipt_session_must_match`: assertion `foreign-receipt-retains` — local E unchanged. |
| PC-5 | Change new sequence predicate from > B to >= B. | `SessionMessageQueueDeliveredSpillTests.C1056_Sequence_must_exceed_original_floor`: assertion `sequence-equal-retains` — receipt at B retains E. |
| PC-6 | Coalesce native Timestamp to CreatedAt before time comparison. | `SessionMessageQueueDeliveredSpillTests.C1056_Null_timestamp_cannot_release_unobservable_spill`: assertion `null-native-retains` — null native time retains E. |
| PC-7 | Use UtcNow()-tolerance instead of original start-tolerance. | `SessionMessageQueueDeliveredSpillTests.C1056_Timestamp_uses_original_attempt_floor`: assertion `original-floor-releases` — equality receipt releases after clock advance. |
| PC-8 | Delete attempts>0 from shared receipt predicate. | `SessionMessageQueueDeliveredSpillTests.C1056_Unattempted_rows_are_not_reconciled`: assertion `zero-attempt-excluded` — PostgreSQL candidate IDs exclude adversary. |
| PC-9 | Replace floorless skip with MinValue time floor. | `SessionMessageQueueDeliveredSpillTests.C1056_Floorless_rows_are_not_reconciled`: assertion `floorless-retains` — no-floor Q keeps E. |
| PC-10 | Omit receipt-only sweep pass, leaving ordinary sweep. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Stranded_sweep_finds_delivered_spill_without_pending`: assertion `lone-obligation-releases` — lone owed E clears. |
| PC-11 | Return empty at new helper entry when ReadWorkingAsync is true. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Reconcile_while_working_does_not_send_pending`: assertion `busy-receipt-releases` — busy receipted Q releases. |
| PC-12 | Require non-null RemoteSpillCourier and IsStaged(sessionId) at helper entry. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Fresh_graph_reconciles_committed_receipt`: assertion `fresh-graph-releases` — no staged memory but E releases. |
| PC-13 | Save LateConfirmed/verdict time first, then clear E in a second SaveChanges. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Failed_release_commit_retries_atomically`: assertion `release-failure-atomic` — E-clear refusal leaves original Delivered/E/time. |
| PC-14 | Assign LastDeliveryStartedAt=UtcNow() on receipt confirmation. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Confirmation_preserves_attempt_identity_and_is_idempotent`: assertion `attempt-identity-preserved` — entire saved identity tuple equals post-release tuple. |
| PC-15 | Keep non-null native timestamp check but remove >= T. | `SessionMessageQueueDeliveredSpillTests.C1056_Timestamp_uses_original_attempt_floor`: assertion `timestamp-before-retains` — T-1 microsecond retains E. |
| PC-16 | Change >= T to > T. | `SessionMessageQueueDeliveredSpillTests.C1056_Timestamp_uses_original_attempt_floor`: assertion `timestamp-equal-releases` — native T releases. |
| PC-17 | Remove RequiresTextMatch refusal, keeping IsCompleteIn's weak arm. | `SessionMessageQueueDeliveredSpillTests.C1056_Complete_wire_is_required`: assertion `weak-wire-retains` — normalized length 11 retains E. |
| PC-18 | Delete Status==Sent from shared receipt predicate. | `SessionMessageQueueDeliveredSpillTests.C1056_Unattempted_rows_are_not_reconciled`: assertion `non-sent-excluded` — candidate IDs exclude Pending/Canceled. |
| PC-19 | Delete DeliveryVerdict==Delivered from receipt predicate. | `SessionMessageQueueDeliveredSpillTests.C1056_Unattempted_rows_are_not_reconciled`: assertion `other-verdict-excluded` — candidate IDs exclude wrong/null verdict. |
| PC-20 | Delete RemoteSpillBody!=null from receipt predicate. | `SessionMessageQueueDeliveredSpillTests.C1056_Unattempted_rows_are_not_reconciled`: assertion `no-owned-spill-excluded` — synthetic Sent/Delivered/E=null ID excluded independently of verdict guard. |
| PC-21 | Remove requested-session constraint from helper action-row query. | `SessionMessageQueueDeliveredSpillTests.C1056_Receipt_session_must_match`: assertion `foreign-obligation-untouched` — independently receipted other-session row unchanged. |
| PC-22 | Add await _runtime.SendInputAsync(sessionId, "\r", ct) after confirmed match. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Reconcile_while_working_does_not_send_pending`: assertion `receipt-zero-input` — Inputs equals pre-reconcile sequence, including empty-composer Enter. |
| PC-23 | Add await _runtime.KillAsync(sessionId, TimeSpan.FromSeconds(1), ct) after match. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Confirmation_preserves_attempt_identity_and_is_idempotent`: assertion `receipt-zero-lifecycle` — no additional Kill/Start/lifecycle entries. |
| PC-24 | On partial match set Status=Pending, SentAt=null and save before returning. | `SessionMessageQueueDeliveredSpillTests.C1056_Complete_wire_is_required`: assertion `partial-state-unchanged` — tuple still Sent with original SentAt. |
| PC-25 | Inspect only first ordered UserPrompt candidate instead of scanning. | `SessionMessageQueueDeliveredSpillTests.C1056_Complete_wire_is_required`: assertion `later-complete-releases` — partial then whole same ingestion batch releases. |
| PC-26 | Prefer LastDeliveryStartedAt branch whenever B and start both exist. | `SessionMessageQueueDeliveredSpillTests.C1056_Sequence_must_exceed_original_floor`: assertion `sequence-dominates-time` — current timestamp at B does not release. |
| PC-27 | Require saved LastDeliveryGeneration equal current session StartedAt. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Confirmation_preserves_attempt_identity_and_is_idempotent`: assertion `old-generation-receipt-releases` — old attempt still releases. |
| PC-28 | Omit only FlushSession receipt-helper call. | `SessionMessageQueueDeliveredSpillTests.C1056_Screen_then_complete_prompt_releases_once`: assertion `flush-session-releases` — release before any other entry called. |
| PC-29 | Omit only FlushIfIdle receipt-helper call. | `SessionMessageQueueDeliveredSpillTests.C1056_Screen_then_complete_prompt_releases_once`: assertion `flush-if-idle-releases` — release before any other entry called. |
| PC-30 | Omit only OnTurnEnd receipt-helper call. | `SessionMessageQueueDeliveredSpillTests.C1056_Screen_then_complete_prompt_releases_once`: assertion `turn-end-releases` — first entry releases. |
| PC-31 | Drop new helper's general confirmed IDs from OnTurnEnd result. | `SessionMessageQueueDeliveredSpillTests.C1056_Screen_then_complete_prompt_releases_once`: assertion `turn-end-confirmed-ids` — result contains Q. |
| PC-32 | Add machine-origin restriction to receipt sweep discovery. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Stranded_sweep_finds_delivered_spill_without_pending`: assertion `ui-receipt-releases` — Ui receipt releases. |
| PC-33 | Require owning agent AlwaysOn in receipt sweep discovery. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Stranded_sweep_finds_delivered_spill_without_pending`: assertion `non-always-on-releases` — Delegation/non-AlwaysOn releases. |
| PC-34 | Add DeliveryAttempts<MaxAttempts to receipt predicate. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Stranded_sweep_finds_delivered_spill_without_pending`: assertion `capped-receipt-releases` — at-cap receipt releases. |
| PC-35 | Add LastDeliveryStartedAt<=now-InterruptedAttemptAge to receipt selection. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Stranded_sweep_finds_delivered_spill_without_pending`: assertion `fresh-receipt-releases` — fresh attempt receipt releases. |
| PC-36 | Add LastDeliveryStartedAt>=now-InterruptedAttemptWindow to receipt selection. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Stranded_sweep_finds_delivered_spill_without_pending`: assertion `old-receipt-releases` — old attempt receipt releases. |
| PC-37 | Filter receipt sweep sessions by ListLiveOrUnknownSessions. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Stranded_sweep_finds_delivered_spill_without_pending`: assertion `absent-recipient-releases` — Running/absent recipient releases. |
| PC-38 | Filter receipt sweep sessions by Status==Running. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Stranded_sweep_finds_delivered_spill_without_pending`: assertion `non-running-releases` — non-Running/live registration releases. |
| PC-39 | Add receipt-confirmed count to sweep returned delivery count. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Stranded_sweep_finds_delivered_spill_without_pending`: assertion `receipt-sweep-deliveries-zero` — sweep returns zero. |
| PC-40 | Publish receipt queue-change before release SaveChanges. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Failed_release_commit_retries_atomically`: assertion `failed-release-no-publication` — after clearing setup events, failed E-clear save publishes nothing. |
| PC-41 | After CatchUp persist resolve queue from a scope and await FlushSessionAsync(sessionId, ct). | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Reconcile_while_working_does_not_send_pending`: assertion `catchup-under-lock-completes` — cancellation caught into false assertion; original completes under held lock. |
| PC-42 | On empty persisted candidates resolve ISessionRunnerClient in a new scope and accept eligible matching UserPrompt from GetTranscriptAsync without persistence. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Failed_release_commit_retries_atomically`: assertion `uncommitted-receipt-retains` — runner DTO present, DB UUID absent, E retained. |
| PC-43 | Replace FlushIfIdle GetLock(sessionId) with new SemaphoreSlim(1, 1). | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Reconcile_while_working_does_not_send_pending`: assertion `receipt-flush-waits-for-session-lock` — operation cancels waiting under held real lock, no mutation. |
| PC-44 | Add runtime body input immediately before enqueue queue-row SaveChanges. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Fresh_graph_reconciles_committed_receipt`: assertion `failed-enqueue-zero-input` — refused insert has empty Inputs. |
| PC-45 | Add DeliveryAttempts<3 to QueueAttention.InterruptedSent (fixture cap is 3). | `SessionMessageQueueWedgedHeadTests.A_lone_capped_crashed_row_is_discovered_and_parked_by_the_sweep`: existing row.Status.ShouldBe(Pending) fails because row remains Sent. |
| PC-46 | Remove attempts-cap conjunct from QueueAttention.DeliverablePending. | `SessionMessageQueueWedgedHeadTests.A_parked_row_stops_being_discovered_once_it_is_at_rest`: existing AssertDiscoveryAsync(expected:false) candidate-ID assertion fails. |
| PC-47 | Remove NoSubmitOutput alternative from QueueAttention.DeliverablePending. | `SessionMessageQueueWedgedHeadTests.Fresh_NoSubmitOutput_is_discovered_before_stranded_age_and_delivered_whole`: existing AssertDiscoveryAsync(expected:true) candidate-ID assertion fails. |
| PC-48 | Replace CreatedAt<=strandedCutoff with true in QueueAttention.DeliverablePending; keep cap. | `SessionMessageQueueWedgedHeadTests.Fresh_ordinary_Pending_is_excluded_until_stranded_age`: existing AssertDiscoveryAsync(expected:false) candidate-ID assertion fails. |
| PC-49 | After receipt-only FlushIfIdle success call PublishFinishedAsync(sessionId, ct). | `SessionMessageQueueDeliveredSpillTests.C1056_Screen_then_complete_prompt_releases_once`: assertion `receipt-no-finished-event` — zero SessionFinished after clearing setup events. |
| PC-50 | Add Codex-only session-kind gate to new receipt helper. | `SessionMessageQueueDeliveredSpillTests.C1056_Screen_then_complete_prompt_releases_once`: assertion `provider-neutral-releases` — historical ClaudeCode/Grok receipts release. |
| PC-51 | Suppress PublishQueueChangedAsync for receipt-only FlushIfIdle success. | `SessionMessageQueueDeliveredSpillTests.C1056_Screen_then_complete_prompt_releases_once`: assertion `receipt-queue-change-published` — correct-session event after durable release. |
| PC-52 | Make existing FlushIfIdle !ReadWorkingAsync condition unconditional true; keep receipt helper. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Reconcile_while_working_does_not_send_pending`: assertion `busy-pending-untouched` — unrelated row Pending/attempts=0 and input sequence unchanged. |
| PC-53 | Require EnsureInputTransportAvailableAsync before helper matching; return empty on typed Unavailable. | `SessionMessageQueueDeliveredSpillRecoveryTests.C1056_Stranded_sweep_finds_delivered_spill_without_pending`: assertion `unavailable-transport-releases` — live/Running/known runner receipt releases despite unavailable input. |
| PC-54 | Omit Channel Q from new ConfirmedChannelMessageIds while keeping general IDs. | `SessionMessageQueueDeliveredSpillTests.C1056_Screen_then_complete_prompt_releases_once`: assertion `turn-end-channel-confirmed-ids` — Channel Q in both sets; Ui Q only in general set. |
| PC-55 | Make AcceptedByCompleteUserPrompt return true for screen confirmation. | `SessionMessageQueueDeliveredSpillTests.C1056_Screen_then_complete_prompt_releases_once`: assertion `screen-spill-retained` — initial Sent/Delivered keeps original E before any transcript receipt. |
| PC-56 | Remove Math.Max(0, tolerance) clamp in new null-baseline floor calculation. | `SessionMessageQueueDeliveredSpillTests.C1056_Timestamp_uses_original_attempt_floor`: assertion `negative-tolerance-clamped` — native timestamp at original start releases with negative configured tolerance. |
| PC-57 | Replace stored B in new receipt query with current persisted transcript Max sequence. | `SessionMessageQueueDeliveredSpillTests.C1056_Sequence_must_exceed_original_floor`: assertion `sequence-after-releases` — stored B+1 complete receipt releases without recapture. |
| PC-58 | Add native Timestamp>=LastDeliveryGeneration condition to null-baseline receipt query. | `SessionMessageQueueDeliveredSpillTests.C1056_Timestamp_uses_original_attempt_floor`: assertion `tolerance-pre-generation-releases` — T equality in the 30-second tolerance window before original generation still releases. |

PC-22/23/44 use the existing injected _runtime and inspected APIs. PC-41/42 use
IServiceScopeFactory/ISessionRunnerClient, with a local scope; no new production test
hook, reflection or live service is needed. Code's private helper names may differ:
mutate the equivalent branch but retain the same exact method/assertion. PC-28/29/30
remove the independent entry call site. Mutations must not edit tests, DTOs, fault
configuration or expected predicate IDs. All test assertion labels in this table
are requirements for the new test bodies, not claims those files already exist.

### Out of scope

- Native Codex/ConPTY, JSONL parsing, phone-home WebSocket and runner file writing:
  unchanged D-8 transport, separately qualified. No Windows skip substitutes for the
  portable proof; no CARD-1029 transplant. No model/file-reading claim from W receipt.
- Retention promises, runner inbox deletion, generic queued-submission/late-confirm
  semantics, unrelated RC/launch/paste internals and global matcher changes: outside
  this receipt-only repair. Companion suites retain wider regression coverage without
  commissioning a full legacy mutation audit.
- OS-kill/power-loss fidelity: provider replacement and PostgreSQL commit/fault tests
  exercise the durable handoffs; they do not emulate physical process termination.
- Full Cartesian eligibility matrix: isolated factors plus all-adverse conjunction
  cover the independent guards without hundreds of redundant graph instances.
- PC execution in TestDesign/Code/ordinary Review: the separate post-land Mutation
  stage owns it. No PCs are waived.

### Checkpoints

Portable PostgreSQL integration; resolve live placement as D-9 requires, without
runner/platform pins. The union is the entire ordinary scope. One exact filter per
row; CP-3 reuses CP-2 only on the same committed S2 SHA/build provenance.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1056-core/` | portable-pg-spill-core | `/*/Antiphon.Tests.Application/(SessionMessageQueueDeliveredSpillTests*)\|(DurableRunnerSpillReceiptTests*)/*` | V-1, R-1–R-8, R-12 | 9 new core + 11 existing; exactly 20 executions, 0 failed/skipped | 20 | 10 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1056-recovery/` | portable-pg-spill-recovery | `/*/Antiphon.Tests.Application/(SessionMessageQueueDeliveredSpillRecoveryTests*)\|(SessionMessageQueueInterruptedAttemptTests*)/*` | V-2–V-4, R-9–R-10, R-13 | 5 new recovery + 15 existing; exactly 20 executions, 0 failed/skipped | 20 | 10 |
| CP-3 | S2 | CP-2 | portable-pg-discovery-regression | `/*/Antiphon.Tests.Application/SessionMessageQueueWedgedHeadTests/(A_lone_capped_crashed_row_is_discovered_and_parked_by_the_sweep*)\|(A_parked_row_stops_being_discovered_once_it_is_at_rest*)\|(Fresh_NoSubmitOutput_is_discovered_before_stranded_age_and_delivered_whole*)\|(Fresh_ordinary_Pending_is_excluded_until_stranded_age*)` | R-11 | 4 named methods; expansion 1+2+2+2; exactly 7 executions, 0 failed/skipped | 7 | 1 |

Commit/push S1 before CP-1 and S2 before CP-2/CP-3. Run the checkpoint tool with
`run --plan docs/superpowers/plans/2026-10-05-card-1056-codex-delivered-spill-release-plan.md`,
`--after S1` or `--after S2`, and `--expected-source-sha` matching the committed group.
Use documented host-slot-gated tool bootstrap into bin-c1056-tool/ if required.
Await every run/wait until terminal; exit 75 is still running. No source edits during
runs. Keep unedited CHECKPOINT lines and validate clean SHA/build receipts. Slot
exit 4 is not-run, never permission for unleased retry. Code/Review run the evidence
diff guard over the whole task range; clean owned alternate outputs, keep generated
logs/TRX/JSON ignored. Independent ordinary Review runs all three rows against the
final candidate SHA; S1-only evidence does not certify S2.

Roster audit: **44 methods / 47 planned executions = 9+11+5+15+(1+2+2+2)**.
The 14 new names are exactly V-1–V-4 and R-1–R-10. Both existing full classes have
one execution per method (11 and 15); the four CP-3 methods expand exactly as shown.
Loops, assertion rows, providers and cuts are not Min counts. Wider actual execution
is filter drift even if Min passes. Source inspection is not execution evidence:
Code must confirm names and expansions in fresh TRX after implementing the tests.

### Cost

All values are **estimated**, not measured. Ordinary **Code V/R floor = 21 minutes**:
CP-1's core/spill filter 10 + CP-2's recovery/interrupted filter 10 + CP-3's four-method
discovery filter 1. Each building row includes its build; CP-3 reuses output.
Tool setup/bootstrap budgets **4 minutes** separately, so setup + ordinary Code
verification = **25 minutes**, before the Plan's 55–85 minute authoring estimate.

**Mutation floor = 58 x 6 = 348 minutes.** Each PC-1–PC-58's exact method filter above
budgets baseline isolated build/test **2 minutes**, compiling break/red isolated
build/test **2 minutes**, and restoration/green isolated build/test **2 minutes**.
Separate phase receipts are required even when two PCs target the same method.
That is **174 phase invocations / 183 TUnit results** before reruns: 55 single-result
controls plus three two-result controls, each in three phases. The extra 44 PCs split
independently bypassable guards; they do not change the ordinary 47-execution roster.

Combined setup + Code V/R + each PC baseline/red/restore/green =
**4 + 21 + 348 = 373 minutes**. Independent ordinary Review adds **21 minutes**,
for **394 minutes** across stages with reusable bootstrap; each separately needed
bootstrap adds 4. Report slot queue time, failures and reruns in actual elapsed cost.

CP-3 reuse saves an estimated **2 minutes / one isolated build**. No additional
measured savings claim: no benchmark was run, so there is no comparable broad-suite
wall time. No batching savings are budgeted because controls share production files.

Before handoff: required bodies read; **guards=58, mapped=58, missing=0,
duplicate PC maps=0**. Every control has an available fixture mechanism, compiling
production defect, exact test method and decisive assertion. New setup is recorded
above; there is no unverifiable seam or human choice. Next: **Code** on this committed
plan. Ordinary Review and land precede SourceLanding Mutation.
