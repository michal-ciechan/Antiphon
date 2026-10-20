# CARD-0510: bounded fresh recovery after three failed standing resumes

Plan task `c2cfa28e`, 2026-10-20. Target: **master only**. Inspected HEAD and freshly fetched
`origin/master`: `cff9b9bdadab0285d748be3d725bd76a406450d0`.

**Not already implemented.** Automatic recovery still resumes; CARD-0561 added a five-failure
continuity hold. CARD-0466 expressly forbids automatic Fresh. This is a proposed, bounded
exception to those decisions, not restoration of a forgotten setting.

**Disposition: decide on D-1 through D-8, then TestDesign.** In particular, D-6 preserves
attempted-input safety and D-7 defers fullness-triggered replacement. Those defaults do
**not** fully resolve the card's 2026-10-15 recurrence additions. Do not close that part of
the card or dispatch Code as though automatic ambiguous-message replay were designed here.
TestDesign is separate; the witness table below is its bounded input, not completed verification.

Owners read: [runtime invariants](../../session-runtime-invariants.md#standing-conversation-continuity-card-0466),
[HTTP operations](../../ops-http.md), [testing/build](../../testing-and-build.md),
[project conventions](../../project-context.md), and [orchestration](../../orchestration-loop.md).
Related decisions: [CARD-0466](2026-09-09-card-0466-standing-session-continuity-plan.md) and
[CARD-0561](2026-09-18-card-0561-poisoned-transcript-resume-loop-plan.md).

## Ground truth

Paths and line numbers below refer to the inspected SHA.

| Card assumption / question | What current code does | Design consequence |
|---|---|---|
| Automatic restarts always use `Fresh:false`. | `server/Application/Services/AgentSupervisorService.cs:282` and `:470` do, for ordinary and capacity recovery. `AgentControlService.cs:160` independently refuses automatic Fresh/selection/retry with `standing_recovery_operator_required`. | Changing one supervisor boolean is insufficient. Add a narrowly authorized internal path. |
| The supervisor retries forever. | `AgentSupervisorService.cs:146,476` skips continuity holds and trips `RepeatedResumeFailure` at `ResumeFailureHoldAttempts` (default 5; 0 disables). | The original premise is outdated; the recurrence correctly describes the new hold. |
| Reuse `RestartBackoffFailures` as the trigger. | `RestartFailurePolicy.cs:56` charges it for infrastructure too. `ContinuityResumeFailures` counts Unknown and LaunchOrProcessFailure, including the initial terminal observation and pre-admission Start exceptions. `C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling` starts with a crash plus four resumes. | None is an exact count of three failed automatic resume launches. Keep pacing/legacy hold accounting and add narrowly scoped accounting. |
| A threshold setting might already enable Fresh. | `SupervisionSettings.cs:26` declares `FreshAfterResumeFailures` deprecated/ignored; `server/Program.cs:232` warns if configured. | Leave that compatibility setting inert. Use a new, unambiguous setting. |
| A successful Start means recovered. | Start queues work. Reset requires Running, completed interactive boot and `HealthyUptimeResetMinutes` (default 10); manual acceptance resets the continuity counter. | Preserve the healthy-uptime boundary; acceptance alone must not re-arm automatic Fresh. |
| Every standing kind has a native resume to escalate. | `AgentControlService.FindResumableSessionAsync` supports native continuity for Claude/Grok; Codex/OpenCode/Raw already take the new-session path. | Scope this exception to the two resumable kinds; do not redesign other launchers. |
| Fresh can discard the poisoned delivery and continue. | `AgentControlService.cs:564-574` refuses any attempted pending non-rules message. `StandingQueueSwitchPolicy.NeverAttempted` checks count, baseline, timestamps, verdict and settlement evidence. Only untouched pending rows move at `:733`. | Keep this guard under D-6; a missing transcript record alone is not proof of no submission. |
| A hold is invisible. | `StandingContinuityState.HoldAsync` records an Info incident; `AttentionService.cs:1986` renders a durable Error attention item. The hold writer does not raise through `IAlertService`. | Add a routed alert for an exhausted/refused recovery, while retaining durable attention. Do not claim there is no existing UI signal. |
| Context fullness >=100% proves an unusable conversation. | `SessionContextUsage.Compute` derives provider-specific usage against a resolved ceiling; the supervisor does not use it as a restart trigger. CARD-0561 explicitly rejected its then-suspect fullness reading as replacement authority. | D-7 keeps it diagnostic pending a separate evidence-based design. |
| Pending notifications can simply be canceled or moved. | Queue rows can carry `SourceLandNotificationId`, `SourceTaskId` and channel identities. `AgentTaskLandNotificationService` binds destination/payload and treats cancellation as canceled, not delivered. | Blanket cancel/replay or raw retargeting of keyed messages is outside this minimal cut. |

Platform inspection completed with authenticated `GET /api/runner-defaults` and
`GET /api/session-runners` on 2026-10-20. Defaults revision was 2; an eligible Linux default
and an eligible Windows runner were present. All planned checks use the **portable backend
Integration lane**, real isolated PostgreSQL schemas and fake adapters. No native desktop
requirement was found. Resolve placement again at dispatch; omit `-Runner` and `-Platform`.
This plan embeds no fleet address or host pin.

## Decisions

- **D-1 — three actual automatic resume failures, then one Fresh.** Proposed setting
  `Supervision:AutomaticFreshAfterResumeFailures = 3`; 0 disables the exception.
  Eligible: an ordinary AlwaysOn, non-pool, non-specialist standing Claude/Grok agent with an
  owned current conversation. The initial crash is not one of the three. A resume counts
  after it was durably accepted and its generation ends before healthy reset with Unknown
  or LaunchOrProcessFailure. Infrastructure, cancellation, unavailable history, and
  quota/model/placement/preflight refusals neither increment nor reset this streak.
  Thus "consecutive" means qualifying failed resumes without intervening sustained health
  or a human-accepted start, not consecutive supervisor ticks. Rejected: all backoff failures
  (infrastructure cannot diagnose poisoned history), or counting the triggering crash
  (would Fresh after only two failed retries).

- **D-2 — durable accounting beside existing supervision state.** Add
  `AutomaticResumeFailures` (0), `LastAutomaticResumeSessionId` / `LastAutomaticResumeStartedAt`
  (nullable generation marker), `AutomaticFreshConsumed` (false), and
  `AutomaticFreshAlertedHoldAt` (nullable held-episode marker) to
  `AgentSupervisionState`, with an EF CLI-generated migration. Stamp the resume marker in
  `StartInteractiveSessionAsync`'s acceptance transaction only for an eligible automatic
  resume. Charge only matching terminal generations, through the existing
  `LastObservedRestartSessionId/StartedAt` deduplication; synchronous failure bookkeeping
  and later observation must share that consumption rule. Keep legacy counter semantics.
  Reset the new count, marker and consumed flag on sustained healthy uptime and accepted
  human Start/Retry/Select/Fresh, not on polling, server restart, rejected Start, queued
  automatic acceptance, or ordinary `StandingContinuityState.Clear`. Existing rows begin
  with no proven automatic resume history; do not reconstruct it from incident counts.
  Rejected: memory-only counters or reinterpreting the legacy counter.

- **D-3 — Fresh is a new conversation, once per unhealthy episode.** Allocate a new Antiphon
  session ID, preserve the old row/transcript/native history, set the new standing owner and
  current pointer, and enqueue with `resume:false` and normal fresh bootstrap notes.
  Do not pass a native resume target or overwrite the old ID. Preserve configured model,
  provider, working directory, backend and placement. Consume the one-Fresh allowance in
  the same reservation transaction as the new pointer. A failed fresh boot does not
  re-arm it; existing backoff and five-failure hold govern subsequent recovery until health
  or an explicit accepted human start. Pre-reservation refusal consumes nothing.
  Rejected: an unlimited fresh loop or resetting all counters as soon as launch is queued.
  An operator-configured legacy hold threshold that trips before three admitted failures
  still wins; do not silently raise or disable it to reach Fresh.

- **D-4 — authorization stays internal and generation-bound.** Add an internal supervised
  Fresh entry point carrying the expected old session ID/StartedAt; do not add an HTTP
  bypass flag or impersonate a manual start. Revalidate D-1/D-2 under the existing ordered
  queue/session/agent locks immediately before reservation. Preserve runner-liveness,
  ownership, settings-version, suspension, liveness-latch, Herdr hold and card-work checks;
  additionally refuse open execution assignments for automatic Fresh. Never stop a live
  or Working process. Preserve specialist/compaction recovery's strict same-conversation
  contract. Ordinary and capacity-granted restart paths use the same decision, with the
  capacity grant still mandatory. A concurrent Stop or Start wins by revoking the stale
  reservation, and two ticks cannot consume two Fresh allowances.
  Only `RepeatedResumeFailure` may be bypassed, and only with the new proven threshold
  and unused allowance. Other continuity reasons stay held. Legacy held rows without
  that evidence receive the alert in D-8, not an automatic replacement.
  Rejected: removing the general `automatic` guard or clearing every continuity hold.

- **D-5 — manual Start retains explicit continuity semantics.** A plain manual start resumes;
  it does not infer Fresh from either failure counter. Existing Fresh/Retry/Select actions
  remain the acknowledgment mechanism. Rejected: making the same UI Start action silently
  change meaning depending on a hidden retry count.

- **D-6 — proposed minimal queue boundary: retain attempted-input refusal and alert.**
  Untouched pending input uses the existing ordered transfer, with queue identity retained.
  Attempted pending input keeps its source row, body, delivery history and pending state;
  no cancellation, deletion, evidence reset or replay is authorized here. If it blocks the
  eligible automatic Fresh, retain/set the continuity hold, cancel the automatic schedule,
  and emit one actionable Error incident/alert naming agent, old session and queue IDs.
  This makes the recurrence's refusal visible but does not automatically repair it.
  For keyed land/completion/channel rows, keep the destination/receipt contract: if the
  existing transfer cannot preserve all producer bindings, refuse and alert rather than
  widen that contract in this cut. Rejected: generic cancel (can extinguish owed reports),
  blind replay (can repeat an action), or moving old baselines into a new transcript.
  **Caller decision:** accept this bounded cut and keep the attempted-delivery ask open,
  or commission a separate queue recovery design before Code.

- **D-7 — no fullness-only trigger in this cut.** Keep >=100% as diagnostic evidence.
  To design that extension, measure the exact generation's provider/model, occupancy row,
  resolved ceiling, compaction boundary, transcript-ingest health and submitted-prompt
  evidence, then distinguish unrecoverable history from a stale/incorrect reading.
  Fullness must never become permission to kill Working sessions.
  **Caller decision:** accept deferral or send that extension to Investigate.

- **D-8 — explain every forced replacement or refusal.** Persist one
  `StandingAutomaticFreshStarted` Warning incident in the successful reservation transaction:
  agent, old/new IDs, qualifying count, threshold and reason; metadata only.
  Route through the existing alert service and publish `AgentChanged` after commit.
  For a refused/exhausted escalation, preserve durable continuity attention and issue an
  Error alert keyed to the held episode. Compare/persist `AutomaticFreshAlertedHoldAt`
  against `ContinuityHeldAt` under the agent lock, so repeated ticks and service recreation
  do not repeatedly notify an unchanged hold, including one inherited at deployment.
  The incident/marker commit precedes the alert call: existing `AlertService` routing is
  best effort, not a transactional outbox. A crash in that gap can lose routing; the durable
  incident and attention remain. Guaranteed external receipt would be a separate scope.
  Acceptance copy says "fresh launch queued; prior conversation retained", never
  "recovered". Existing Recovered marks
  sustained health. Use standard alert routing; tests prove persistence/dispatch to that
  service, not delivery through an external messaging provider.

## Implementation slices

Each slice commits a buildable failing witness before its fix and pushes both outcomes.
No live agent, queue, runner configuration, RC branch or deployment changes belong to this task.

| Slice | Files | Result and test ownership |
|---|---|---|
| S1 — account for admitted automatic resume generations | `server/Domain/Entities/AgentSupervisionState.cs`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated `server/Migrations/*AddStandingAutomaticFreshRecovery*` plus snapshot; `server/Application/Settings/SupervisionSettings.cs`; `server/Application/Services/RestartFailurePolicy.cs`; `AgentControlService.cs`; `AgentSupervisorService.cs`; new `tests/Antiphon.Tests/Application/StandingRestartEscalationTests.cs` | Persist/count/reset D-1/D-2 without changing legacy accounting. V-1/V-2 below; reuse `AgentSupervisionTests.BuildHarness`, fake adapters, mutable clock and service-provider recreation. |
| S2 — reserve one authorized Fresh | New concrete `server/Application/Services/StandingRestartEscalationPolicy.cs`; `AgentSupervisorService.cs`; `AgentControlService.cs`; new escalation tests; `tests/Antiphon.Tests/Application/StandingSessionSwitchConcurrencyTests.cs` and `.CommitGap.cs` only if extending their existing fixtures | Route both supervisor restart branches through the decision, preserve capacity/compaction guards, atomic allowance/pointer, explicit manual behavior. V-3/V-4/V-5. No runner protocol change. |
| S3 — audit/alert and retain queue safety | `server/Domain/Enums/AgentIncidentKind.cs` (append values); matching type-only union member in `client/src/api/agents.ts`; `AgentSupervisorService.cs`; `AgentControlService.cs`; `StandingContinuityState.cs` if shared hold-alert emission is needed; escalation tests; `docs/session-runtime-invariants.md`; `docs/ops-http.md` | D-6/D-8 refusal and replacement signals, V-6/V-7/V-8; existing incident UI displays the new kind without a component change. Update continuity owner documentation to describe the narrow exception and unchanged deprecated setting. No generic queue replay implementation. |

For S2/S3, check keyed producer bindings before admitting transfer; keep the existing queue
locks across reservation. Do not call a queue method that reacquires the same semaphore
while holding it. Do not move producer identities by an ad hoc database update.

## Test-design handoff

One method-scoped red witness and one green rerun of that **same method** per behavior.
New tests live in `StandingRestartEscalationTests.cs` in the portable backend Integration
lane. Use real service/DB/queue paths with fake adapters, not mocked `StartAsync`.
Where a preserved guard already passes on master, the red witness is the named local
positive-control mutation, restored before the green. Compile failures, zero selected
tests and fixture failures do not qualify as red. TestDesign must finalize argument
rosters, exact counts and the executable checkpoint manifest before Code.

| ID | Proposed exact method | Red witness / decisive assertion | Narrow green |
|---|---|---|---|
| V-1 | `C510_counts_only_distinct_admitted_failed_resumes` | Suppress generation stamping, or remove consumption dedup: initial crash/preflight refusal must count zero; three distinct admitted failing resumes count three despite duplicate ticks, sync/async observation and provider reconstruction. | Same method after restoring/fixing accounting. |
| V-2 | `C510_health_and_human_acceptance_reset_the_episode` | Reset on queued Start instead of completed healthy uptime: the clock just below the healthy boundary must retain the streak/allowance; at the boundary reset. Accepted manual action resets, refused action does not. | Same method, fake clock boundary and persisted-state assertions. |
| V-3 | `C510_three_failed_resumes_make_the_next_launch_fresh` | Current master resumes again. Drive a triggering crash then three failed accepted resumes; the next due launch must use a different ID and omit resume arguments; first three retries retain the old ID. Parameterize ordinary and granted-capacity paths. | Same method; preserved old row/transcript and fresh adapter launch arguments are the oracle. |
| V-4 | `C510_fresh_is_consumed_once_until_health` | Omit the atomic consumed flag: fail the fresh boot, recreate services and tick again; no second new conversation before reset. | Same method, launch/pointer history and retained backoff/hold. |
| V-5 | `C510_stale_or_ineligible_recovery_cannot_replace_history` | Remove one admission guard per method-scoped PC: concurrent Start/Stop, changed generation, live/Working, open task, specialist, Herdr/other continuity hold, missing history, no capacity grant. Each must prevent replacement. Include legacy repeated hold without new evidence and plain manual resume. | Same method's finalized parameter roster; one winning reservation and zero forbidden launches. |
| V-6 | `C510_attempted_input_blocks_fresh_and_alerts_once` | Current refusal alone has no dedicated routed escalation alert; remove the queue guard as PC. Real queue stamps an attempted row before reservation: no new launch, unchanged evidence, durable hold and one Error alert; repeat/recreate must not duplicate it. Include keyed pending input whose destination cannot safely transfer. | Same method; verify queue/producer state and captured alert request. |
| V-7 | `C510_untouched_input_transfers_and_fresh_is_audited` | Force supervisor to resume or suppress audit: new ID must own an untouched ordinary queued row in order, with one Warning incident/alert and old history retained. Flush through real queue and observe matching UserPrompt in the new destination; queue status alone is insufficient. | Same method; committed transition, delivered body and incident identities. |
| V-8 | `C510_disabled_policy_and_fullness_do_not_authorize_fresh` | Ignore the new zero setting, revive the deprecated setting, or use fullness as authority. Each must retain current continuity semantics; infrastructure-only outcomes must never replace history even above the pacing threshold. | Same method's finalized parameter roster. |

Retain narrow regression witnesses from
`StandingRestartAccountingTests` (`Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff`,
`Async_infrastructure_failure_is_consumed_once_across_recreation`,
`Observation_probe_timeout_and_requested_cancellation_authorize_no_attempt`),
`StandingContinuityRecoveryTests.Missing_native_target_holds_after_one_resume_without_create`,
and `StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders`.
The C561 legacy hold tests should explicitly disable the new exception when asserting
the previous policy; preserve those assertions rather than deleting them.

### Planned checkpoints

These are lane/slice assignments for TestDesign, **not** an executable closed manifest yet.
All exact method filters use `/*/Antiphon.Tests.Application/<Class>/<Method>`.

| Checkpoint | After | Lane | Selection |
|---|---|---|---|
| CP-1 | S1 | Portable backend Integration | V-1 and V-2, each its own exact method row; legacy accounting regressions above. |
| CP-2 | S2 | Portable backend Integration | V-3 through V-5, exact methods; existing missing-native and reservation concurrency methods. |
| CP-3 | S3 | Portable backend Integration | V-6 through V-8, exact methods; real-queue reservation regression. |

TestDesign promotes these to the required `## Verification design` / `### Checkpoints`
schema, with one isolated build per committed slice group and reusable output for its
method rows, actual expected counts and costs. Run through the checkpoint tool/build-slot
gate; await every run, including every exit-75 continuation. No whole Unit, assembly or
unrelated native suite. Keep generated receipts ignored and remove owned alternate
`bin-<name>/` outputs after the driver finishes. A failed test needs a same-method base
comparison before being labeled inherited.

## Waiting and release

This design launches only after the old process is terminal and runner liveness is known;
it introduces no stall kill. A fresh standing session may wait at its composer and remains
owned by that standing agent. It has no automatic input-wait release deadline. Explicit
Stop/retirement owns release; the independent compaction contract remains unchanged.
A continuity-held terminal agent waits for explicit Retry/Select/Fresh or resolution of
queued input, without a new running seat. With blocked-task parking disabled there is no
automatic parking/release deadline (CARD-1083).

## Plan validation and next action

Read-only source/API inspection establishes the absence of automatic escalation; no
builds, tests, live recovery or historical incident reproduction were run for this plan.
The decision required is concrete: adopt the bounded one-Fresh policy, and accept D-6/D-7's
scope limits, or commission queue/fullness investigation before implementation.
Once decided, dispatch **TestDesign** against this artifact, then Code with its completed
verification section. Master publication is not authorization to activate it on an RC host.

## Verification design

TestDesign task `1d1aa4b5`, 2026-10-20, inspected source
`1c3ff72633fa83bfe1fc5bda75cbacbd8db6bdbb`. The commissioning brief accepts D-1 through D-8
exactly as written, including the attempted-input refusal and fullness deferral. That
acceptance supersedes the historical decision-pending disposition above; the fix design
is unchanged. This appendix replaces the provisional witness/checkpoint roster, not the
implementation decisions. Target remains master only.

### Inspection

- Read `AgentSupervisionTests.BuildHarness`, clock, provider recreation and cleanup;
  `StandingRestartAccountingTests.cs` and `.ResumeFailureHold.cs`, including all seven
  C561 bodies and their helpers; `SessionExitObservation.ObserveMatchingAsync` |
  admitted-generation accounting, reset and legacy holds -> V-1/V-2, R-1/R-5.
- Read `StandingContinuityRecoveryTests` and `StandingRecoveryFixture`;
  `StandingSessionSwitchConcurrencyTests.cs`, `.CommitGap.cs`, `.Delivery.cs`, `.Stop.cs`
  and `.Generation.cs` test bodies and boundary interceptors |
  reservation, ordering, Stop, superseded work and missing native history -> V-3/V-4/V-5,
  R-2/R-3/R-4. These are the nearest fixtures for the new escalation file.
- Read `CapacityRecoverySupervisionTests.Card0412_D8_stalled_standing_start_rearms_and_actually_starts_again`
  and `Card0412_V17_capacity_wait_skips_crash_scheduling_branch`, plus
  `CapacityRecoveryQueueTests` | real grant/redemption setup -> V-3/V-5;
  channel capacity policy itself is unchanged and excluded.
- Read `SessionMessageQueueInterruptedAttemptTests` receipt/Enter-only/busy bodies,
  `SessionMessageQueueDeliveryVerificationTests` initial success/swallowed-submit bodies,
  `BridgeQueueHarness` submission callback and transcript insertion helpers, and
  `FakeAgentProtocolAdapter` input/submission implementation |
  transferred input through recipient receipt -> V-7.
- Read `AgentTaskLandNotificationRecoveryTests` keyed recovery/reconciliation bodies |
  immutable destination, body and digest -> V-6; no new keyed retargeting implementation.
- Read `StandingSessionOwnershipTests.C561_model_and_migration_agree` and upgrade fixture,
  `TestDbFixture.CreateIsolatedSchemaAsync` and context construction |
  migration/model/default persistence -> V-1, R-6.
- Read supervisor ordinary/capacity/health/hold branches, control admission and reservation,
  `RestartFailurePolicy`, `StandingQueueSwitchPolicy`, `StandingContinuityState`,
  interactive launch queue, reconciliation's missing-runner/interrupted-launch branches and
  `AlertService.RaiseAsync` | all D-1..D-8 boundaries -> V-1..V-8 and guard inventory.
  Owners consulted: testing/build checkpoint and delivery contracts, project conventions,
  runtime continuity/delivery contracts and orchestration's Code/Review/Land/Mutation order.

**Common recipe H.** Add `tests/Antiphon.Tests/Application/StandingRestartEscalationTests.cs`
as an Integration fixture with unkeyed `[NotInParallel]`, following the inspected
supervision/recovery fixtures. Use `TestDbFixture.CreateIsolatedSchemaAsync` for every
new case: despite its name it now creates a cloned **database**, not a search-path schema.
Pass its connection string to every context, runtime callback, recreated provider and
cleanup; never mix a private store with `AgentSupervisionTests.CreateContext()`'s shared
store. Extend the existing harness only with optional service registration/clock injection,
or make a local wrapper, so real Control/Supervisor/LaunchQueue/MessageQueue services remain
in use. Use a mutable clock for due times and health; use awaited TCS/interceptor barriers
for races, never a timing-only assertion. Drain launches before disposing each provider.
The fake adapters do not spawn OS processes; adding a process-spawning substitute would
require the assembly process limiter and a declared scope/cost amendment.

Classification: V-1/V-2/V-3/V-4 add policy/persistence behavior; V-5/V-6/V-8 exercise admission and recovery safety; V-7 is producer-to-recipient delivery verification. R rows preserve inherited behavior. Every PC is a compiling production mutation, never a fixture or assertion mutation. New tests below are specifications, not existing green evidence. Use explicit method-level
`[Arguments]` rows; no hidden dynamic-data expansion. Each listed case contributes one
TUnit result. Every case first establishes that all unrelated admission conditions pass.
Record boundary hits, owned IDs and the exact generation before exercising the guard.
For PC-6/PC-7 isolate ID and StartedAt mismatch with the other component matching and an unconsumed terminal observation. For PC-15 use a Starting row with an old completed-boot timestamp so only status prevents reset. For capacity-key cases keep the provider grant valid for a different action and prove redemption is not performed on behalf of the stale consumer. For a negative case, remove only its obstruction in a fresh fixture and demonstrate that
the same path can reserve/launch. Do not reset an actual attempted row to make that canary.
This canary is an internal assertion, not an additional TUnit execution.

**Witness recipe W.** For new behavior, commit/push a buildable failing witness before
its implementation; run its exact method and rerun that method green after the fix.
Provide buildable contracts/scaffolding first where a test names a new member; compiler,
fixture and zero-selection failures are not witnesses. Retain one decisive red per
behavior, not one full suite per variant. Preserved guards already green on the base get
their red witness in the named post-land PC cycle. Code runs the ordinary V/R table;
Review judges coverage before land; Mutation runs every PC after land. TestDesign runs
neither the future tests nor mutations.

### Delivery inventory

| Path / durable identity | Producer -> destination | Persistence and handoffs | Recovery and recipient oracle |
|---|---|---|---|
| Fresh launch: agent ID + old/new session IDs + accepted StartedAt | Supervisor ordinary or capacity-granted decision -> Control reservation -> real LaunchQueue -> interactive adapter | Pointer, owner, consumed allowance and Warning incident commit together; in-memory enqueue follows commit; worker compares its immutable generation before launch and input. | V-3/V-4/V-5/V-7 exercise both routes, rollback, lost enqueue, interrupted boot and obsolete work. Adapter start proves launch only. A boot note or transferred input counts as received only with its matching complete destination UserPrompt after the attempt floor. Missing native history after a lost initial launch may hold: preserve the new pointer, spent allowance and input debt, alert, and never invent another Fresh. |
| Ordinary untouched input: queue ID + original complete body + source/destination IDs | Real MessageQueue.EnqueueAsync producer on old terminal conversation -> reservation transfer -> real destination queue -> adapter | Original queue ID/body retained; destination/sequence transfer is in the reservation transaction. The destination queue separately persists attempt/baseline/generation before writing; verdict follows submission. | V-7 covers already idle and busy recipients, enqueue failure, transfer rollback, commit/enqueue gap, interrupted launch, claim-before-write and submit-before-verdict. Recover by actual reconciliation/supervision and FlushStrandedQueuesAsync, retaining the same IDs. For recoverable paths demand a complete correlated UserPrompt at the new destination, above baseline/in the timestamp window, and no copy at the old destination. For genuinely ambiguous paths demand retained debt and hold, never a receipt claim. |
| Normal fresh bootstrap: new session ID + launch-note queue ID/body | Launch composer -> LaunchInteractiveAsync -> real MessageQueue -> new conversation | Use actual composed fresh notes; capture the exact body and queue identity when created. Test receipt independently from InteractiveLaunchCompletedAt. | V-3/V-7 require the full submitted fresh note when configured, and no resume note/native target. The pre-enqueue crash case may preserve debt/hold instead of promising recreation of an unpersisted bootstrap note. State that loss explicitly; do not seed the missing note and call it recovery. |
| Attempted/keyed input: queue ID + land-notification/task/channel producer identity | Existing producer -> old destination, with automatic Fresh refused | Preserve queue status, body, delivery evidence, destination, producer record, digest and producer link. No new handoff is authorized. | V-6 uses an actually attempted ordinary row and real keyed producers/producer records. Eight isolated evidence-field cases and three producer-binding cases must hold/refuse and retain all debt. No cancellation/replay/retarget. These negative cases cannot claim recipient delivery. |
| Started/refused alert: incident ID + agent/session IDs + held-episode timestamp | Committed incident/episode marker -> real IAlertService -> persisted Alert -> recording IAlertRouter | Incident and dedup marker commit before dispatch; AlertService saves its own row before router invocation. AgentChanged publishes after reservation commit. | V-6/V-8 test normal routed calls, inherited/exhausted holds, repeated ticks/provider recreation, rollback and commit-to-alert failure. Inspect durable incident/attention from a fresh context even when routing fails. D-8 explicitly accepts best-effort routing loss in that gap. Recording router receipt proves the internal handoff only, not operator/external-provider delivery. |

The recipient simulator must attach `OnSubmitted` to the actual adapter selected by the
launch factory and persist the **observed submitted body**, following BridgeQueueHarness.
It must not preinsert the expected body or write a transcript merely because enqueue/start
returned. Include a withheld-submit phase with no complete matching UserPrompt, then
release the gate and require the complete prompt. Join receipt to the queue attempt,
destination and generation, not text anywhere in the database. Record original and
submitted multi-line payloads; allow only the production's documented normalization.

Substitutes: fake adapters plus callback-generated transcripts prove application transport
and receipt decisions, not native Claude/Grok JSONL ingestion or real provider execution.
Real Postgres plus injected boundary exceptions/provider reconstruction prove durable state
and recovery decisions, not an OS crash's process custody. A recording router proves receipt
by the alert-router interface, not Telegram, email or a human. Events, Sent, acknowledgments,
accepted requests and queue inserts are never recipient evidence. No delivery promise here
ends at one of those signals.

### Proves it works now

All new methods below belong to `StandingRestartEscalationTests`, recipe H + W, portable
backend Integration. Exact commands are their CP filters below. Named case strings are
binding inputs; each oracle must be a production-result assertion, with a useful message
identifying the case. The S1/S2/S3 implementation slices remain those above.

| ID | Exact method / results | Variant/case roster and reachability | Decisive expected result / red witness |
|---|---|---|---|
| V-1 | `C510_counts_only_distinct_admitted_failed_resumes` / 9 | unknown-async, process-async, sync-after-commit, duplicate-observation, infrastructure, canceled, unavailable, preflight, recreate. Drive real admitted generations for the positive cases; duplicate ticks and synchronous/later observation share a generation. Negative cases start at count two; preflight separately exercises quota/model/placement refusal with no acceptance. | Initial crash counts zero; three distinct admitted qualifying failures count exactly three. Excluded failures neither increment nor reset; marker and dedup survive a fresh provider. Suppressed acceptance stamp or duplicated consumption gives red at the persisted count. |
| V-1 | `C510_migration_starts_without_invented_history` / 3 | legacy counts 0, 3, 500. Migrate isolated store to migration predecessor, seed old-format supervision/incident rows using raw SQL or a pre-migration model, then migrate forward and recreate provider. | New count=0, marker pair=null, consumed=false, alerted-hold=null regardless of old counters; old state unchanged; model matches snapshot. A nonzero migration default gives red. |
| V-2 | `C510_health_and_human_acceptance_reset_the_episode` / 11 | healthy-before, healthy-boundary, starting, running-incomplete, manual-start, manual-retry, manual-select, manual-fresh, rejected-human, automatic-accepted, clear-only. Seed a spent episode; at healthy cases use boundary minus one clock tick then exact boundary. | Only Running + completed boot + elapsed threshold, or accepted explicit human action, clears count/marker/consumed. Before that all persist; rejection and ordinary Clear preserve them. Reset-on-queued-acceptance gives red. |
| V-3 | `C510_three_failed_resumes_make_the_next_launch_fresh` / 4 | explicit (Claude, ordinary), (Grok, ordinary), (Claude, capacity), (Grok, capacity). Trigger initial crash then three real accepted failures; capacity uses EnsureWait/GrantReady or Reconcile/Redeem, as in the inspected fixture. | First three retries retain old ID/native target. Next due launch has a distinct owned ID, fresh args/notes, no native resume target; old row/transcript unchanged; effective provider/model/cwd/backend/placement retained. Require real-queue bootstrap UserPrompt. Current resume-only behavior is red. |
| V-4 | `C510_fresh_is_consumed_once_until_health` / 4 | fresh-boot-failure, recreate, reservation-rollback, pre-reservation-refusal. Observe allowance at transaction boundary; fail boot then tick through legacy hold; recreate services. | Pointer and consumed flag commit together; rollback/refusal consumes nothing, committed boot failure stays spent, no second new ID, backoff/legacy hold retained. Omitted allowance or acceptance reset gives red. |
| V-5 | `C510_stale_or_ineligible_recovery_cannot_replace_history` / 56 | 28 cases, each with ordinary/capacity path: not-always-on, pool, specialist, unsupported-kind, wrong-owner, pointer-changed, generation-changed, settings-changed, suspended, latched, herdr-held, card-work, execution-work, db-live, runner-live, runner-unknown, queued-launch, compaction, no-grant, wrong-grant, other-continuity-hold, inherited-unproven-hold, early-legacy-hold, below-threshold, spent, missing-history, public-automatic-fresh, public-capacity-fresh. For unsupported-kind internally check Codex/OpenCode/Raw; live checks include Starting/Running/Stopping and a Working turn. | Each named obstruction prevents fresh reservation: no new pointer/owner, no forbidden adapter spawn/kill/input, no allowance spend. Set only that obstruction after preflight where raceable; assert barrier was reached and unrelated checks were satisfied. Ordinary no-grant/wrong-grant cases install an outstanding capacity wait so the supervisor must enter the capacity lane. Use real request validation for public cases. Removing each guard is its own PC, not a single all-guards mutation. |
| V-5 | `C510_concurrent_recovery_and_intent_changes_have_one_winner` / 5 | two-ticks, manual-start, stop-before-spawn, stop-before-input, commit-enqueue-gap. Reuse the inspected transaction/adapter gates; superseding intent is committed while first contender is paused. | One reservation at most; later accepted intent wins, stale work cannot overwrite it or type, both queue locks serialize transfer through enqueue, all tasks drained. A bypassed reservation/worker fence gives red. |
| V-5 | `C510_manual_start_remains_resume` / 1 | Unheld stopped owned session, both counters above threshold, unused allowance; plain manual Start. | Same session ID and resume args, no automatic Fresh incident; human acceptance resets episode. Inferring Fresh from counters is red. |
| V-6 | `C510_attempted_input_blocks_fresh_and_alerts_once` / 11 | attempts, baseline, started-at, verdict, verdict-at, sent-at, canceled-at, channel-settled-at, keyed-land, keyed-task, keyed-channel. First case obtains an attempted Pending row through the real queue's failed submit. Other evidence rows isolate one field on a Pending row (including legacy inconsistent combinations). Keyed cases begin untouched with real producer bindings. | Refuse Fresh, retain every original queue/producer field and old pointer, hold and cancel schedule, one Error alert naming agent/session/queue IDs. Repeated ticks and new provider do not repeat it. Existing refusal without dedicated alert is red; each evidence/binding bypass has its own PC. |
| V-7 | `C510_untouched_input_transfers_and_fresh_is_audited` / 2 | recipient-idle, recipient-busy. Real enqueue on terminal source of two distinguishable multiline ordinary messages; capture queue IDs. Grant Fresh then block recipient with a real Working transcript or leave already eligible. | Same IDs/body, ordered destination append, no old-destination submit. Busy recipient takes neither message until its TurnEnd; then real flush produces whole matching UserPrompt(s) in source order, exactly one receipt per body (one batch may contain both). Warning incident/alert identifies old/new IDs and counts; acceptance copy says queued, not recovered. Transfer/flush/audit omission is red. |
| V-7 | `C510_delivery_handoffs_preserve_receipt_or_debt` / 7 | producer-enqueue-failure, reservation-save, commit-before-enqueue, launch-before-running, queue-claim-before-write, submit-before-verdict, verdict-before-recreate. Inject a single boundary fault after proving its hit; reconstruct services over same store and use real recovery. | Pre-insert producer failure creates no debt and a later explicit producer retry delivers once; rollback preserves old rows then successful Fresh delivers. Committed lost launch retains new pointer/spent allowance/debt; commit-before-enqueue case configures native history absent: the actual reconciler/supervisor resumes the committed ID once, receives native-missing, and holds with debt intact and no receipt. Launch-before-running configures an extant matching runner generation: actual interrupted-launch recovery delivers at that same ID. Queue-claim-before-write configures the same live idle generation with an empty composer: the stranded sweep recovers and delivers once. Verdict-before-recreate retains its existing receipt without new input. Post-attempt cases never move that row to another conversation. Submit-before-verdict late-confirms the existing whole UserPrompt without a second body write; no-receipt cases never report receipt. Skip-recovery or clear-debt mutation is red. |
| V-8 | `C510_disabled_policy_and_fullness_do_not_authorize_fresh` / 4 | new-setting-zero, deprecated-setting-only, fullness-only, infrastructure-only. Each has a valid owned terminal conversation; fullness also exercises live/Working. Use full state, not a helper returning a bool. | No automatic Fresh for zero setting; deprecated setting inert; >=100% occupancy cannot replace/kill; infrastructure grows pacing only. Preserve counters/old history. Enabling the forbidden trigger is red. |
| V-8 | `C510_audit_and_alert_follow_committed_episode` / 8 | started, refused, exhausted, inherited-hold, new-episode, commit-alert-gap, router-failure, reservation-rollback. Use real AlertService and a recording/failing router, transaction gate and fresh read contexts. | Successful reservation has one Warning incident with old/new/count/threshold/reason and AgentChanged after commit. Refused/exhausted/inherited holds have durable Error attention plus one routed Error per held episode; a new held timestamp permits one new alert. Rollback publishes/routes nothing. Commit-alert gap and router failure retain incident/hold and suppress same-episode retries, accepting the D-8 routing-loss limit. No prompt/native transcript/secrets in metadata; no Recovered until V-2's health boundary. |

For V-5 do not satisfy refusal merely by an earlier independent blocker. Invoke the internal
supervised entry point at its reservation boundary as well as the supervisor route where
necessary; a supervisor early-return alone does not exercise the locked recheck. The accepted
positive path V-3 and fresh-fixture canary must reach the same reservation. Explicitly vary
the two expected-generation components independently. Unavailable history is a distinct
continuity hold, never inferred from infrastructure exception prose.

### Guards the regression

Existing methods keep their assertions; recipe H's isolation rules apply to extensions.
All are Integration, except that the model-consistency method is a metadata-only assertion
inside its existing Integration fixture. The seven C561 tests receive explicit
`AutomaticFreshAfterResumeFailures = 0` in their settings so they continue to measure their
original policy; do not weaken the five-failure and never-give-up assertions.

- R-1: pacing/consumption — `StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff`
  (4 results), `Async_infrastructure_failure_is_consumed_once_across_recreation` (1),
  `Observation_probe_timeout_and_requested_cancellation_authorize_no_attempt` (1).
  Preserve session identity, fixed qualifying counters, one consumed infrastructure result
  and zero starts on unknown inventory/canceled observation.
- R-2: missing native target — `StandingContinuityRecoveryTests.Missing_native_target_holds_after_one_resume_without_create`
  (1): native-missing hold, same ID, forbidden fresh adapter never starts.
- R-3: existing reservation serialization —
  `StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation`
  (4) and `Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation` (1):
  one accepted winner, held target lock across commit/enqueue, stale adapter never starts.
- R-4: queue/transfer race —
  `StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders`
  (2): attempted source stays put when delivery wins; otherwise untouched row transfers
  while source input stays empty. This proves exclusion, not receipt; V-7 supplies receipt.
- R-5: legacy hold/reset — all seven exact methods in
  `StandingRestartAccountingTests.ResumeFailureHold.cs` (7):
  `C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling`,
  `C561_a_start_that_throws_charges_the_same_counter_and_trips_the_same_hold`,
  `C561_infrastructure_outcomes_grow_the_ladder_and_never_hold`,
  `C561_healthy_uptime_resets_the_resume_failure_counter`,
  `C561_a_human_retry_resets_the_counter_and_the_next_five_hold_again`,
  `C561_a_zero_threshold_restores_the_never_give_up_ladder`,
  `C561_a_supervised_resume_clears_the_hold_fields_but_not_the_counter`.
  Decisive assertions are their inspected threshold, held reason, NextRestartAt and
  preserved/reset counter assertions.
- R-6: model/migration — `StandingSessionOwnershipTests.C561_model_and_migration_agree`
  (1): HasPendingModelChanges is false after CLI migration generation.
### Guard inventory

Each independently bypassable safety guard has one distinct PC. Guards are included even
when inherited or tested only by a future method. Configuration retention is one launch-spec
invariant; the model substitution is its representative compiling defect and all configured
fields are asserted. No safety guard is excluded as "already covered" or "too expensive".

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | D-1: Zero new threshold disables automatic Fresh. | PC-1 |
| G-2 | D-1: Deprecated FreshAfterResumeFailures has no authority. | PC-2 |
| G-3 | D-7: Fullness is diagnostic, including for Working sessions. | PC-3 |
| G-4 | D-1/D-2: Only durably admitted automatic resumes obtain the marker. | PC-4 |
| G-5 | D-1/D-2: Initial crash is not an admitted resume failure. | PC-5 |
| G-6 | D-2: Terminal evidence must match marker session ID. | PC-6 |
| G-7 | D-2: Terminal evidence must match marker StartedAt. | PC-7 |
| G-8 | D-2: One generation is consumed once across sync and later observation. | PC-8 |
| G-9 | D-1: Infrastructure outcomes do not increment the new count. | PC-9 |
| G-10 | D-1: Requested cancellation does not increment the new count. | PC-10 |
| G-11 | D-1: Unavailable native history does not increment or grant Fresh. | PC-11 |
| G-12 | D-1: Excluded outcomes do not reset an existing streak. | PC-12 |
| G-13 | D-2: New fields persist across provider recreation. | PC-13 |
| G-14 | D-2: Migration cannot reconstruct authority from old counters/incidents. | PC-14 |
| G-15 | D-2: Health reset requires Running. | PC-15 |
| G-16 | D-2: Health reset requires completed interactive boot. | PC-16 |
| G-17 | D-2: Health reset requires the complete uptime interval. | PC-17 |
| G-18 | D-2: Human reset happens only on accepted action. | PC-18 |
| G-19 | D-2/D-3: Queued automatic acceptance never re-arms Fresh. | PC-19 |
| G-20 | D-2: Ordinary continuity Clear cannot reset the episode. | PC-20 |
| G-21 | D-3: Fresh allocates a different Antiphon session ID. | PC-21 |
| G-22 | D-3: Fresh preserves old transcript/history. | PC-22 |
| G-23 | D-3: Fresh is launched without native resume authority. | PC-23 |
| G-24 | D-3: Configured execution identity is retained. | PC-24 |
| G-25 | D-3: Allowance and pointer are one atomic reservation. | PC-25 |
| G-26 | D-3: A failed committed Fresh remains spent. | PC-26 |
| G-27 | D-3: A pre-reservation refusal consumes nothing. | PC-27 |
| G-28 | D-3: Earlier configured legacy hold wins. | PC-28 |
| G-29 | D-4: General automatic Start cannot request Fresh. | PC-29 |
| G-30 | D-4: Public CapacityRecovery cannot request Fresh. | PC-30 |
| G-31 | D-1/D-4: Expected old pointer is rechecked under lock. | PC-31 |
| G-32 | D-1/D-4: Expected old StartedAt is rechecked under lock. | PC-32 |
| G-33 | D-1/D-4: Owned current history is required. | PC-33 |
| G-34 | D-1/D-4: Agent settings version is rechecked. | PC-34 |
| G-35 | D-1/D-4: Suspension revokes automatic admission. | PC-35 |
| G-36 | D-1/D-4: Liveness latch revokes automatic admission. | PC-36 |
| G-37 | D-1/D-4: Herdr hold remains authoritative. | PC-37 |
| G-38 | D-1/D-4: Pending card work blocks automatic Fresh. | PC-38 |
| G-39 | D-1/D-4: Open execution assignments block automatic Fresh. | PC-39 |
| G-40 | D-1/D-4: Only AlwaysOn standing agents qualify. | PC-40 |
| G-41 | D-1/D-4: Pool delegates are excluded. | PC-41 |
| G-42 | D-1/D-4: Specialist seats are excluded. | PC-42 |
| G-43 | D-1/D-4: Only Claude/Grok native-resumable kinds qualify. | PC-43 |
| G-44 | D-1/D-4: Live DB generations/Working turns cannot be replaced. | PC-44 |
| G-45 | D-1/D-4: A live runner process blocks replacement. | PC-45 |
| G-46 | D-1/D-4: Unknown runner liveness cannot authorize replacement. | PC-46 |
| G-47 | D-1/D-4: An owned queued launch blocks replacement. | PC-47 |
| G-48 | D-1/D-4: Compaction recovery keeps strict same-conversation admission. | PC-48 |
| G-49 | D-1/D-4: A capacity wait needs an issued matching grant. | PC-49 |
| G-50 | D-1/D-4: Capacity grant/action identity must match. | PC-50 |
| G-51 | D-1/D-4: Only RepeatedResumeFailure hold may be bypassed. | PC-51 |
| G-52 | D-1/D-4: Legacy held rows have no invented admitted-failure evidence. | PC-52 |
| G-53 | D-1/D-4: Three qualifying failures are required. | PC-53 |
| G-54 | D-1/D-4: Spent allowance blocks another reservation. | PC-54 |
| G-55 | D-1/D-4: Missing native/current history remains held. | PC-55 |
| G-56 | D-4: Concurrent ticks serialize the allowance decision. | PC-56 |
| G-57 | D-4: Both source and target queue locks bridge reservation to enqueue. | PC-57 |
| G-58 | D-4: Stop revokes an accepted launch before spawn. | PC-58 |
| G-59 | D-4: Stop revokes input after spawn. | PC-59 |
| G-60 | D-5: Plain manual Start preserves resume semantics. | PC-60 |
| G-61 | D-6: DeliveryAttempts independently forbids attempted-input transfer. | PC-61 |
| G-62 | D-6: LastDeliveryBaselineSequence independently forbids attempted-input transfer. | PC-62 |
| G-63 | D-6: LastDeliveryStartedAt independently forbids attempted-input transfer. | PC-63 |
| G-64 | D-6: DeliveryVerdict independently forbids attempted-input transfer. | PC-64 |
| G-65 | D-6: DeliveryVerdictAt independently forbids attempted-input transfer. | PC-65 |
| G-66 | D-6: SentAt independently forbids attempted-input transfer. | PC-66 |
| G-67 | D-6: CanceledAt independently forbids attempted-input transfer. | PC-67 |
| G-68 | D-6: ChannelReplySettledAt independently forbids attempted-input transfer. | PC-68 |
| G-69 | D-6: Keyed land producer destination/receipt binding cannot be retargeted. | PC-69 |
| G-70 | D-6: Keyed task producer destination/receipt binding cannot be retargeted. | PC-70 |
| G-71 | D-6: Keyed channel producer destination/receipt binding cannot be retargeted. | PC-71 |
| G-72 | D-6: Untouched queue identity/body/order survives transfer. | PC-72 |
| G-73 | D-6: Busy recipient cannot receive transferred input early. | PC-73 |
| G-74 | D-6: Transfer is committed or rolled back with reservation. | PC-74 |
| G-75 | D-3/D-6: Committed lost enqueue retains spent allowance and input debt. | PC-75 |
| G-76 | D-3/D-6: Interrupted launch recovers only its accepted generation. | PC-76 |
| G-77 | D-6: An interrupted claimed message cannot cross conversations. | PC-77 |
| G-78 | D-6: Submitted receipt is recovered without duplicate body input. | PC-78 |
| G-79 | D-6: Transfer recovery must reach the real recipient. | PC-79 |
| G-80 | D-6/D-8: Refusal leaves a continuity hold. | PC-80 |
| G-81 | D-6/D-8: Refusal cancels the automatic restart schedule. | PC-81 |
| G-82 | D-8: Held-episode alert marker deduplicates across process recreation. | PC-82 |
| G-83 | D-8: A later held episode may alert again. | PC-83 |
| G-84 | D-8: Dedup marker and durable incident precede alert dispatch. | PC-84 |
| G-85 | D-8: Fresh audit commits with the reservation. | PC-85 |
| G-86 | D-8: Audit/alerts carry metadata without queued or native content. | PC-86 |
| G-87 | D-8: Queued Fresh is not declared recovered. | PC-87 |
| G-88 | D-8: AgentChanged for Fresh follows commit. | PC-88 |
| G-89 | D-8: Routing failure cannot erase durable hold/attention. | PC-89 |

### Positive controls

Every row means **break / observe the named assertion red / restore / rebuild / observe the
same method green**, after land, on the bound SourceLanding snapshot. Run every PC; none is
optional. Exact-method filters below select all declared arguments for that method. Require
nonzero execution, the intended assertion failure, restoration diff and a fresh green build;
a compiler/fixture failure, earlier unrelated refusal or zero selected cases is not red.
Record the specific failing argument, boundary hit and red assertion per PC, even when
independent cycles share a build. Do not batch controls touching the same production method.

The mutations below describe compiling production defects, not edits to assertions or
expected data. Because the implementation does not exist yet, Code must bind each payload to
an exact production symbol/location and committed source SHA and retain the planned detecting
method/argument/assertion in its handoff. A payload not bound after Code remains **binding
required**, never silently treated as run or dropped. A seam that cannot expose its decisive
assertion returns to Plan; it is not substituted with a mock receipt. Review judges these
bindings and ordinary evidence before land. No PC execution is claimed by this TestDesign.

| PC | Break guard by this compiling defect | Detecting filter | Case and exact expected red assertion |
|---|---|---|---|
| PC-1 | Break G-1: Treat zero as the default threshold in the new policy. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_disabled_policy_and_fullness_do_not_authorize_fresh` | new-setting-zero: new ID count remains zero. |
| PC-2 | Break G-2: Read the deprecated setting as the effective threshold. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_disabled_policy_and_fullness_do_not_authorize_fresh` | deprecated-setting-only: old pointer is unchanged. |
| PC-3 | Break G-3: Add fullness >= 100 as an OR arm to Fresh eligibility. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_disabled_policy_and_fullness_do_not_authorize_fresh` | fullness-only: no new session and no kill. |
| PC-4 | Break G-4: Stamp the resume marker/count before the acceptance transaction. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_counts_only_distinct_admitted_failed_resumes` | preflight: count stays two after each rejected preflight. |
| PC-5 | Break G-5: Charge the new count when the terminal session has no automatic-resume marker. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_counts_only_distinct_admitted_failed_resumes` | unknown-async: initial crash new count equals zero. |
| PC-6 | Break G-6: Drop only the marker SessionId equality from charging. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_counts_only_distinct_admitted_failed_resumes` | duplicate-observation: nonmatching terminal ID leaves count unchanged. |
| PC-7 | Break G-7: Drop only the marker StartedAt equality from charging. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_counts_only_distinct_admitted_failed_resumes` | duplicate-observation: old generation leaves count unchanged. |
| PC-8 | Break G-8: Omit the shared LastObservedRestart marker write on synchronous bookkeeping. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_counts_only_distinct_admitted_failed_resumes` | sync-after-commit: count is one after synchronous and terminal observation. |
| PC-9 | Break G-9: Include Infrastructure in the qualifying outcome predicate. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_counts_only_distinct_admitted_failed_resumes` | infrastructure: count stays two while backoff grows. |
| PC-10 | Break G-10: Charge an admitted canceled operation as Unknown. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_counts_only_distinct_admitted_failed_resumes` | canceled: count stays two after requested cancellation. |
| PC-11 | Break G-11: Persist an admitted native-missing outcome as Unknown before the supervisor hold/count decision. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_counts_only_distinct_admitted_failed_resumes` | unavailable: count stays two and native-missing hold remains. |
| PC-12 | Break G-12: Assign zero to AutomaticResumeFailures in the infrastructure branch. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_counts_only_distinct_admitted_failed_resumes` | infrastructure: count stays two. |
| PC-13 | Break G-13: Clear the new count on supervisor construction/first tick. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_counts_only_distinct_admitted_failed_resumes` | recreate: persisted count stays three after reconstruction. |
| PC-14 | Break G-14: Backfill AutomaticResumeFailures from ContinuityResumeFailures. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_migration_starts_without_invented_history` | legacy count 500: new count equals zero and markers are null. |
| PC-15 | Break G-15: Remove the Running predicate from healthy reset. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_health_and_human_acceptance_reset_the_episode` | starting: spent episode remains unchanged while Starting. |
| PC-16 | Break G-16: Use StartedAt in place of a required InteractiveLaunchCompletedAt. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_health_and_human_acceptance_reset_the_episode` | running-incomplete: spent episode remains unchanged with incomplete boot. |
| PC-17 | Break G-17: Reset on any positive uptime instead of the configured boundary. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_health_and_human_acceptance_reset_the_episode` | healthy-before: count and consumed flag remain before the boundary. |
| PC-18 | Break G-18: Move episode reset before human preflight/reservation. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_health_and_human_acceptance_reset_the_episode` | rejected-human: rejected human action preserves count and allowance. |
| PC-19 | Break G-19: Run human episode reset on an automatic acceptance. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_health_and_human_acceptance_reset_the_episode` | automatic-accepted: consumed stays true and count is preserved. |
| PC-20 | Break G-20: Add count/marker/allowance resets to StandingContinuityState.Clear. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_health_and_human_acceptance_reset_the_episode` | clear-only: all episode fields survive Clear. |
| PC-21 | Break G-21: Reuse source ID in the Fresh reservation. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_three_failed_resumes_make_the_next_launch_fresh` | Claude ordinary: new ID differs from old ID. |
| PC-22 | Break G-22: Delete the old session transcript after reservation. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_three_failed_resumes_make_the_next_launch_fresh` | Grok ordinary: old transcript rows and text equal the pre-reservation snapshot. |
| PC-23 | Break G-23: Pass resume:true and the old native target for supervised Fresh. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_three_failed_resumes_make_the_next_launch_fresh` | Grok capacity: fresh launch arguments contain no resume target and fresh note receipt exists. |
| PC-24 | Break G-24: Replace the effective model in the fresh launch spec with the registry default. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_three_failed_resumes_make_the_next_launch_fresh` | Claude capacity: captured model equals configured model; provider/cwd/backend/placement also match. |
| PC-25 | Break G-25: Persist consumed=true before the reservation transaction. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_fresh_is_consumed_once_until_health` | reservation-rollback: injected reservation rollback leaves consumed=false and the old pointer. |
| PC-26 | Break G-26: Clear consumed in the fresh launch-failure path. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_fresh_is_consumed_once_until_health` | fresh-boot-failure: no second new ID through the later hold. |
| PC-27 | Break G-27: Set consumed=true in the fresh-admission refusal branch. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_fresh_is_consumed_once_until_health` | pre-reservation-refusal: refused reservation leaves consumed=false. |
| PC-28 | Break G-28: Skip the legacy hold whenever the new feature is enabled. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | early-legacy-hold: legacy hold remains before three admitted failures. |
| PC-29 | Break G-29: Remove only the automatic branch of the public operator-required check. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | public-automatic-fresh: public automatic Fresh is refused with no reservation. |
| PC-30 | Break G-30: Remove only the CapacityRecovery branch of that check. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | public-capacity-fresh: public capacity Fresh is refused with no reservation. |
| PC-31 | Break G-31: Omit expected-old-session/pointer comparison in the internal reservation. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | pointer-changed: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-32 | Break G-32: Omit expected-old-StartedAt comparison in the internal reservation. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | generation-changed: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-33 | Break G-33: Skip ownership validation for the supervised Fresh source. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | wrong-owner: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-34 | Break G-34: Omit UpdatedAt/version equality before reservation. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | settings-changed: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-35 | Break G-35: Remove Suspended from the locked intent predicate. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | suspended: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-36 | Break G-36: Remove LivenessLatchedAt from the locked intent predicate. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | latched: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-37 | Break G-37: Skip the HerdrFailureHeldAt check in internal Fresh. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | herdr-held: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-38 | Break G-38: Skip ResolveStartCardAsync in internal Fresh. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | card-work: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-39 | Break G-39: Restrict execution-work rejection to manual selected history only. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | execution-work: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-40 | Break G-40: Drop the AlwaysOn eligibility predicate. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | not-always-on: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-41 | Break G-41: Drop IsPoolDelegate exclusion. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | pool: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-42 | Break G-42: Skip specialist-seat eligibility check. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | specialist: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-43 | Break G-43: Allow Codex in the supported-kind predicate. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | unsupported-kind: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-44 | Break G-44: Skip live-session rejection for internal Fresh. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | db-live: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-45 | Break G-45: Ignore involved live processes in the runner inventory. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | runner-live: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-46 | Break G-46: Catch unavailable inventory and substitute an empty successful list. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | runner-unknown: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-47 | Break G-47: Omit launch-queue ownership rejection from internal Fresh. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | queued-launch: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-48 | Break G-48: Skip CheckCompactionAdmission on the supervised Fresh branch. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | compaction: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-49 | Break G-49: Route the no-grant capacity branch directly to supervised Fresh without redemption. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | no-grant: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-50 | Break G-50: Use the provider current action key instead of the waiting consumer action key for both identity comparison and redemption. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | wrong-grant: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-51 | Break G-51: Permit any ContinuityReason in internal Fresh. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | other-continuity-hold: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-52 | Break G-52: Fall back to ContinuityResumeFailures when the new marker/count is absent. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | inherited-unproven-hold: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-53 | Break G-53: Change qualifying count >= threshold to >= threshold - 1. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | below-threshold: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-54 | Break G-54: Omit AutomaticFreshConsumed eligibility check. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | spent: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-55 | Break G-55: Treat missing owned target as permission for Fresh. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | missing-history: no forbidden new session, allowance spend, kill or input; named refusal/hold remains. |
| PC-56 | Break G-56: Read eligibility before the agent lock and omit its locked recheck. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_concurrent_recovery_and_intent_changes_have_one_winner` | two-ticks: exactly one new reservation and one Fresh launch. |
| PC-57 | Break G-57: Release target queue semaphore immediately after transaction commit. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_concurrent_recovery_and_intent_changes_have_one_winner` | commit-enqueue-gap: later resume remains blocked until enqueue takes ownership. |
| PC-58 | Break G-58: Skip immutable generation/intent check before adapter creation. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_concurrent_recovery_and_intent_changes_have_one_winner` | stop-before-spawn: obsolete adapter never starts. |
| PC-59 | Break G-59: Skip immutable generation/intent check before bootstrap input. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_concurrent_recovery_and_intent_changes_have_one_winner` | stop-before-input: obsolete adapter submits no prompt and winner state is unchanged. |
| PC-60 | Break G-60: Infer fresh from the new failure count on manual Start. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_manual_start_remains_resume` | single case: accepted session ID equals old ID and args resume. |
| PC-61 | Break G-61: Remove only the DeliveryAttempts conjunct from StandingQueueSwitchPolicy.NeverAttempted. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | attempts: old pointer and all queue evidence stay unchanged; no new launch. |
| PC-62 | Break G-62: Remove only the LastDeliveryBaselineSequence conjunct from StandingQueueSwitchPolicy.NeverAttempted. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | baseline: old pointer and all queue evidence stay unchanged; no new launch. |
| PC-63 | Break G-63: Remove only the LastDeliveryStartedAt conjunct from StandingQueueSwitchPolicy.NeverAttempted. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | started-at: old pointer and all queue evidence stay unchanged; no new launch. |
| PC-64 | Break G-64: Remove only the DeliveryVerdict conjunct from StandingQueueSwitchPolicy.NeverAttempted. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | verdict: old pointer and all queue evidence stay unchanged; no new launch. |
| PC-65 | Break G-65: Remove only the DeliveryVerdictAt conjunct from StandingQueueSwitchPolicy.NeverAttempted. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | verdict-at: old pointer and all queue evidence stay unchanged; no new launch. |
| PC-66 | Break G-66: Remove only the SentAt conjunct from StandingQueueSwitchPolicy.NeverAttempted. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | sent-at: old pointer and all queue evidence stay unchanged; no new launch. |
| PC-67 | Break G-67: Remove only the CanceledAt conjunct from StandingQueueSwitchPolicy.NeverAttempted. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | canceled-at: old pointer and all queue evidence stay unchanged; no new launch. |
| PC-68 | Break G-68: Remove only the ChannelReplySettledAt conjunct from StandingQueueSwitchPolicy.NeverAttempted. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | channel-settled-at: old pointer and all queue evidence stay unchanged; no new launch. |
| PC-69 | Break G-69: Exempt only the land identity from automatic Fresh transfer refusal. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | keyed-land: producer destination/link/digest and queue row remain unchanged with held refusal. |
| PC-70 | Break G-70: Exempt only the task identity from automatic Fresh transfer refusal. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | keyed-task: producer destination/link/digest and queue row remain unchanged with held refusal. |
| PC-71 | Break G-71: Exempt only the channel identity from automatic Fresh transfer refusal. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | keyed-channel: producer destination/link/digest and queue row remain unchanged with held refusal. |
| PC-72 | Break G-72: Reverse pending transfer enumeration while assigning new sequence numbers. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_untouched_input_transfers_and_fresh_is_audited` | recipient-idle: observed complete prompt bodies retain source order and original queue IDs. |
| PC-73 | Break G-73: Force transferred pending rows through the immediate-delivery arm while Working. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_untouched_input_transfers_and_fresh_is_audited` | recipient-busy: no matching receipt or submission before recipient TurnEnd. |
| PC-74 | Break G-74: Move the queue destination update outside the reservation transaction. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_delivery_handoffs_preserve_receipt_or_debt` | reservation-save: rollback preserves old queue destination, body and identity. |
| PC-75 | Break G-75: Clear consumed and cancel transferred rows when enqueue fails after commit. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_delivery_handoffs_preserve_receipt_or_debt` | commit-before-enqueue: new pointer/spent allowance/uncanceled debt remain; no second Fresh. |
| PC-76 | Break G-76: Resume interrupted launch using current mutable generation instead of accepted generation. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_delivery_handoffs_preserve_receipt_or_debt` | launch-before-running: superseded launch cannot type; matching generation retains receipt or held debt. |
| PC-77 | Break G-77: Treat a claimed row with null verdict as NeverAttempted during subsequent Fresh. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_delivery_handoffs_preserve_receipt_or_debt` | queue-claim-before-write: attempted row keeps destination and evidence with no replay across sessions. |
| PC-78 | Break G-78: Skip late-confirm lookup and unconditionally retype the queued body. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_delivery_handoffs_preserve_receipt_or_debt` | submit-before-verdict: one whole UserPrompt and no second body write after recovery. |
| PC-79 | Break G-79: Return before processing eligible standing-session rows in DeliverNextLockedAsync. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_untouched_input_transfers_and_fresh_is_audited` | recipient-idle: complete destination UserPrompt for each queued body appears within the bounded drain. |
| PC-80 | Break G-80: Return from automatic queue refusal without recording/retaining hold. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | attempts: ContinuityHeldAt is nonnull and old session/queue IDs are retained. |
| PC-81 | Break G-81: Retain NextRestartAt when recording the automatic queue-refusal hold. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | attempts: NextRestartAt is null after refusal. |
| PC-82 | Break G-82: Omit AutomaticFreshAlertedHoldAt persistence. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_audit_and_alert_follow_committed_episode` | inherited-hold: one routed Error across ticks and recreated provider. |
| PC-83 | Break G-83: Use a permanent alerted bool instead of comparing held timestamps. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_audit_and_alert_follow_committed_episode` | new-episode: second episode produces its own single Error alert. |
| PC-84 | Break G-84: Call RaiseAsync before committing the incident/marker. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_audit_and_alert_follow_committed_episode` | commit-alert-gap: recording router reads committed incident/marker; rollback routes nothing. |
| PC-85 | Break G-85: Persist StandingAutomaticFreshStarted outside the reservation transaction. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_audit_and_alert_follow_committed_episode` | reservation-rollback: failed reservation leaves no fresh incident and no changed pointer. |
| PC-86 | Break G-86: Append the queued body/native transcript to incident detail. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_audit_and_alert_follow_committed_episode` | started: secret/body canaries absent from incident, alert and event payloads. |
| PC-87 | Break G-87: Write Recovered or recovered acceptance wording on queued Fresh. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_audit_and_alert_follow_committed_episode` | started: no Recovered incident before healthy boundary; copy explicitly says launch queued. |
| PC-88 | Break G-88: Publish AgentChanged before reservation commit. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_audit_and_alert_follow_committed_episode` | reservation-rollback: rollback emits no fresh AgentChanged and observer sees committed pointer on success. |
| PC-89 | Break G-89: Clear the hold/incident when the alert router throws. | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_audit_and_alert_follow_committed_episode` | router-failure: fresh-context hold and actionable attention remain after routing failure. |

Inventory check: guards=89, mapped=89, missing=0, duplicate PC maps=0.
All 89 PCs are commissioned for Mutation; execution at this design stage is 0/89,
with every row pending. "All PCs run" is the post-land completion gate, not a claim about
this pre-Code document.

### Out of scope

- Automatic replay/cancellation of ambiguous attempted input and new keyed retargeting:
  D-6 explicitly chooses refusal/retained debt/alert. Recovery cases do not discharge that debt.
- Fullness-triggered replacement, native provider qualification, live agent incident replay,
  actual external alert delivery and a transactional alert outbox: D-7/D-8 exclusions.
- RC activation/deployment, runner protocol changes, blanket Unit/assembly/native suites:
  this is master-only portable backend verification. The closed narrow profile overrides the
  generic full-suite default. Client edit is an enum union only; compare its new discriminator
  with the appended backend value during Review, no new browser behavior is claimed.
- OS hard-kill custody and provider-history corruption repair: fault injection reconstructs
  application services only. A missing history at the lost-enqueue boundary must remain held,
  with honest loss/debt evidence, rather than claim a recovered prompt.

### Checkpoints

Schema: [testing/build checkpoint manifest](../../testing-and-build.md#checkpoint-manifest-card-0585).
Each row has one exact filter and one isolated build binding. Reuse occurs only within the
same After group. The union is the whole ordinary V/R scope: 13 new methods / 125 results,
15 existing methods / 22 results, **147 TUnit results**. No whole-class/Unit selection.
The OR operands use the documented TUnit 1.44 trailing wildcard workaround; verify the fresh TRX contains exactly the listed class-qualified methods/arguments and no prefix-neighbor methods. Single-method filters remain literal. All counts assume explicit method-level Arguments exactly as listed above; change the roster
and manifest together if implementation requires a different expansion. Min counts executions,
not assertions, loops, PCs or minutes. All rows expect zero failed/skipped.

Use the checkpoint tool once per committed slice group, selecting the corresponding CP IDs,
with the committed SHA supplied. Use `--after S1`, `--after S2`, or `--after S3` and `--serial`. Run the tool through build-slot.ps1; checkpoint rows obtain
their own slots. Wait on every exit 75 until terminal; do not settle with work running. Keep
source frozen for each run. The required W red witnesses are separately reported exact-method
runs against the pushed failing-witness commit, with a fresh isolated build and the same
method's green in this manifest; they never certify the final source. Failed final rows rerun
only after a justified fix. Report all receipts and any base comparison; no unlisted full runs.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c510-s1/` | admitted-counts | `/*/Antiphon.Tests.Application/(StandingRestartEscalationTests*)\|(StandingRestartAccountingTests*)/(C510_counts_only_distinct_admitted_failed_resumes*)\|(Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff*)\|(Async_infrastructure_failure_is_consumed_once_across_recreation*)\|(Observation_probe_timeout_and_requested_cancellation_authorize_no_attempt*)` | V-1, R-1 | all 15 listed cases, 0 failed/skipped | 15 | 5 |
| CP-2 | S1 | `CP-1` | episode-reset | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_health_and_human_acceptance_reset_the_episode` | V-2 | all 11 listed cases, 0 failed/skipped | 11 | 1 |
| CP-3 | S1 | `CP-1` | migration-defaults | `/*/Antiphon.Tests.Application/(StandingRestartEscalationTests*)\|(StandingSessionOwnershipTests*)/(C510_migration_starts_without_invented_history*)\|(C561_model_and_migration_agree*)` | V-1, R-6 | all 4 listed cases, 0 failed/skipped | 4 | 2 |
| CP-4 | S1 | `CP-1` | legacy-hold | `/*/Antiphon.Tests.Application/StandingRestartAccountingTests/(C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling*)\|(C561_a_start_that_throws_charges_the_same_counter_and_trips_the_same_hold*)\|(C561_infrastructure_outcomes_grow_the_ladder_and_never_hold*)\|(C561_healthy_uptime_resets_the_resume_failure_counter*)\|(C561_a_human_retry_resets_the_counter_and_the_next_five_hold_again*)\|(C561_a_zero_threshold_restores_the_never_give_up_ladder*)\|(C561_a_supervised_resume_clears_the_hold_fields_but_not_the_counter*)` | R-5 | all 7 listed cases, 0 failed/skipped | 7 | 1 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c510-s2/` | fresh-launch | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_three_failed_resumes_make_the_next_launch_fresh` | V-3 | all 4 listed cases, 0 failed/skipped | 4 | 5 |
| CP-6 | S2 | `CP-5` | one-allowance | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_fresh_is_consumed_once_until_health` | V-4 | all 4 listed cases, 0 failed/skipped | 4 | 1 |
| CP-7 | S2 | `CP-5` | admission-guards | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_stale_or_ineligible_recovery_cannot_replace_history` | V-5 | all 56 listed cases, 0 failed/skipped | 56 | 3 |
| CP-8 | S2 | `CP-5` | reservation-races | `/*/Antiphon.Tests.Application/(StandingRestartEscalationTests*)\|(StandingSessionSwitchConcurrencyTests*)/(C510_concurrent_recovery_and_intent_changes_have_one_winner*)\|(Concurrent_starts_and_supervisor_reserve_one_generation*)\|(Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation*)` | V-5, R-3 | all 10 listed cases, 0 failed/skipped | 10 | 2 |
| CP-9 | S2 | `CP-5` | manual-resume | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_manual_start_remains_resume` | V-5 | all 1 listed cases, 0 failed/skipped | 1 | 0.5 |
| CP-10 | S2 | `CP-5` | missing-native | `/*/Antiphon.Tests.Application/StandingContinuityRecoveryTests/Missing_native_target_holds_after_one_resume_without_create` | R-2 | all 1 listed cases, 0 failed/skipped | 1 | 0.5 |
| CP-11 | S3 | `tests/Antiphon.Tests -> bin-c510-s3/` | queue-refusals | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_attempted_input_blocks_fresh_and_alerts_once` | V-6 | all 11 listed cases, 0 failed/skipped | 11 | 5 |
| CP-12 | S3 | `CP-11` | transfer-receipt | `/*/Antiphon.Tests.Application/(StandingRestartEscalationTests*)\|(StandingSessionSwitchConcurrencyTests*)/(C510_untouched_input_transfers_and_fresh_is_audited*)\|(Reservation_and_real_queue_flush_serialize_in_both_orders*)` | V-7, R-4 | all 4 listed cases, 0 failed/skipped | 4 | 2 |
| CP-13 | S3 | `CP-11` | delivery-cuts | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_delivery_handoffs_preserve_receipt_or_debt` | V-7 | all 7 listed cases, 0 failed/skipped | 7 | 3 |
| CP-14 | S3 | `CP-11` | audit-alert | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_audit_and_alert_follow_committed_episode` | V-8 | all 8 listed cases, 0 failed/skipped | 8 | 2 |
| CP-15 | S3 | `CP-11` | disabled-triggers | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/C510_disabled_policy_and_fullness_do_not_authorize_fresh` | V-8 | all 4 listed cases, 0 failed/skipped | 4 | 1 |

### Cost

All figures are **estimates**, not measurements; TestDesign did not run builds/tests. Ordinary
V/R floor (Code) = sum CP EstimatedMinutes = **34 minutes**. This includes three
4-minute builds (12) plus 22 minutes of the named filtered runs. No full Unit/native
sweep is charged. Nine new-behavior failing witnesses (count, migration, reset, fresh, allowance,
queue refusal/alert, transfer receipt, delivery handoffs, audit/alert: the corresponding exact
methods above) add an estimated 50 minutes: nine isolated builds at 4 minutes plus
14 minutes of filtered execution. Admission/race/manual/disabled guard reds use their PCs,
separate from ordinary green scope; preserved-guard reds belong to Mutation. Setup/import/
evidence validation allowance is 3 minutes. Code execution floor including witnesses and
setup is therefore **87 minutes**, plus implementation/diagnosis time.

Post-land PC floor (Mutation), conservatively serial with no shared mutable build, is:
89 red builds + 89 restored green builds at 4 minutes = 712 minutes;
exact-method red/green execution = 341 minutes as itemized below;
89 restores/diff checks at 0.25 minute = 22.25 minutes.
Mutation setup/source/evidence validation = 3 minutes. **PC floor = 1078.25 minutes**.
This is substantial because the accepted plan preserves many independent safety gates;
commission it explicitly, not inside a short Code expectation. Parallel savings are zero
in this estimate because most mutants touch the same reservation/supervision methods.

| Exact method filter suffix (prefix `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/`) | PCs | Minutes per red or green method run | Both colors total minutes |
|---|---:|---:|---:|
| `C510_counts_only_distinct_admitted_failed_resumes` | 10 | 1 | 20 |
| `C510_migration_starts_without_invented_history` | 1 | 2 | 4 |
| `C510_health_and_human_acceptance_reset_the_episode` | 6 | 1 | 12 |
| `C510_three_failed_resumes_make_the_next_launch_fresh` | 4 | 1 | 8 |
| `C510_fresh_is_consumed_once_until_health` | 3 | 1 | 6 |
| `C510_stale_or_ineligible_recovery_cannot_replace_history` | 28 | 3 | 168 |
| `C510_concurrent_recovery_and_intent_changes_have_one_winner` | 4 | 2 | 16 |
| `C510_manual_start_remains_resume` | 1 | 0.5 | 1 |
| `C510_attempted_input_blocks_fresh_and_alerts_once` | 13 | 1 | 26 |
| `C510_untouched_input_transfers_and_fresh_is_audited` | 3 | 2 | 12 |
| `C510_delivery_handoffs_preserve_receipt_or_debt` | 5 | 3 | 30 |
| `C510_disabled_policy_and_fullness_do_not_authorize_fresh` | 3 | 1 | 6 |
| `C510_audit_and_alert_follow_committed_episode` | 8 | 2 | 32 |

Total verification execution floor = Code setup/build + failing witnesses + ordinary V/R +
each PC red/restore/green = **1165.25 minutes**,
excluding authoring, Review analysis, queue waits and failures/repairs. Report measured
costs later without replacing these estimates. Reusing three slice builds instead of a new
build for every one of the 15 ordinary rows saves **48 estimated minutes** (12 avoided builds
at 4 minutes). Grouping same-behavior legacy tests into CP-1/CP-8/CP-12 avoids extra invocations;
no extra numerical savings are assumed without measurements.

Handoff gate: D-1..D-8 accepted; every V/R selected; 147 ordinary results specified;
guards=89, mapped=89, missing=0, duplicate PC maps=0; all 89 PCs required after land,
not yet executed. Source-location payload binding remains required after Code. Next: **code**.
