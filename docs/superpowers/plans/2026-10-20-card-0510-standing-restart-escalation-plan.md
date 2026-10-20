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
