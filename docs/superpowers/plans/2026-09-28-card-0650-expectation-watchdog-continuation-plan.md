# CARD-0650: complete the expectation watchdog

Plan task `79295e2c`, 2026-09-28. Inspected checkout:
`a8cdf665f81ade954cc00eebf6df73270e7841de`.

Complete the existing watchdog with production observation adapters, configurable thresholds,
answer detection, durable operator escalation, and a minute Hangfire job on the main instance.
The watchdog must prompt a working orchestrator while ordinary caller notes cannot progress,
then page the explicitly configured operator conversation if the orchestrator cannot be reached
or does not answer. Every nudge and escalation has a durable card/task audit before outbound I/O.

This is a documentation-only continuation plan. It supersedes the implementation sequence and
checkpoint manifest in the [September 24 plan](2026-09-24-card-0650-expectation-watchdog-plan.md).
That plan's S1-S4 code is already present; do not recreate it or repeat its historical red-first
rounds. Source presence is not a claim of passing tests, deployment, or active protection.
Verification is a separate **TestDesign** handoff; the closed ordinary checkpoint list below is
the starting execution contract, not a claim that future methods already exist.

## Ground truth

The full card and related cards were read using `scripts/card.ps1 get` on 2026-09-28:
CARD-0650 is Review; CARD-0648 is **Backlog** (revision count 1); CARD-0641 is Review.
These are observations, not workflow changes made by this task.

| Requirement or assumption | Evidence at the inspected checkout | Consequence |
|---|---|---|
| The overnight freeze was five hours of silence. | The [incident investigation](../../investigations/2026-09-24-card-0650-silent-overnight-stall.md) joins Held/HeldAged events with the caller's queue and transcript. The holds were task events/logs; no hold note was enqueued. | Do not describe this incident solely as a queued-note delivery failure. Detection and independent transport are both required. |
| Start a new watchdog implementation. | `ExpectationWatchdogPolicy`, `ExpectationSnapshotReader`, `ExpectationWatchdogService`, `ExpectationLedger`, settings, three entities and the `AddExpectationWatchdogLedger` migration exist. | Extend the current durable model. Preserve its scope, episode, atomic audit and uncertainty guards. |
| Direct session prompting still needs to be invented. | `ExpectationNudgeDeliveryService`, `IExpectationPromptSender` and `SessionMessageQueueService.Expectations.cs` implement a committed attempt and a non-recovering direct send. | Reuse this path. It takes the session input lock but neither the repository lease nor the WhenIdle eligibility gate. |
| Existing direct-send code may be simplified. | The runtime owner documents Submitted/Released states, two empty Claude composer snapshots, and operator-token hold release; direct-send and endpoint tests cover multiple repairs. | Preserve these repairs and their regression coverage. Releasing a composer hold never means receipt or answer. |
| There is already a running watchdog. | No watchdog settings binding, service composition or recurring registration in `Program.cs`/`HangfireConfiguration.cs`; no response matcher, operator publisher or status endpoint exists. | S4 must wire the whole chain. Creating rows in tests does not prove a scheduled operational feature. |
| Timings are configurable as the card requests. | `ExpectationWindows` holds fixed 10-minute detection, 10-minute cooldown and 30-minute repeat constants. | Replace feature timing decisions with validated typed settings while retaining these defaults. |
| Snapshot inputs are production observations. | `ScanAllAsync` takes caller-supplied `ExpectationProbeInput`; services default to `NoExpectationCatchUp`. | Add actual runner/provider/session/workspace observation and transcript catch-up adapters; forbid no-op catch-up in production composition. |
| All notes are in the land outbox. | `ReadNotesAsync` reads `AgentTaskLandNotifications`, which also carries profiled task completions. Legacy completions/questions and Check notes can live only in `SessionQueuedMessages`. | Include queue-only caller notes, deduplicate linked outbox/queue debt, and retain each producer's receipt rules. |
| A receipt is an answer. | Current direct receipt matching accepts complete UserPrompt or QueuedUserPrompt; nudge stores `ReceiptAt` and `AnsweredAt` but no matcher sets the latter. | Distinguish submission from complete UserPrompt delivery, and delivery from a correlated assistant ACK. |
| Recovery is available under CARD-0648. | The card is still Backlog. A read-only `RepositoryChildJournalInspector` exists under CARD-0726; its Dead classification alone is not descendant/lock clearance. | Observe without recovering; integrate CARD-0648's eventual safe recovery owner and results, never duplicate its deletion logic. |
| A repository fence stops every task. | CARD-0672 lets already-prepared remote tasks launch without the desktop mutation lease; ordinary live land/capacity waits also have current typed hold classifications. | Evaluate the actual applicable dispatch paths. A local repository fence alone is insufficient to claim all local/remote dispatch is blocked. |
| The operator page can use an alert digest. | `IAntiphonMessagingProducer.SendAsync(ChannelReply)` is the direct outbound seam; `ChatChannel` supplies provider/conversation and Enabled. | Use a durable watchdog outbox and exact configured channel, independent of digest timers, note delivery and in-memory alert throttles. |

Platform census at about 19:03 UTC: `/api/runner-defaults` revision 2 chooses server2 globally;
`/api/session-runners` reported desktop/windows and server2/linux available and dispatch eligible.
This is not a deployment pin. The work is platform-neutral, uses the current default runner,
and needs no `-Runner` or `-Platform` override. Checkpoint lane: .NET/TUnit with isolated PostgreSQL
and a fake session runtime; no Windows-only native process, live broker or paid-agent fixture.

Owners consulted: [project conventions](../../project-context.md),
[orchestration](../../orchestration-loop.md), [card lifecycle](../../agent-card-lifecycle.md),
[session runtime](../../session-runtime-invariants.md), [HTTP operations](../../ops-http.md),
[Telegram](../../telegram.md), [bootstrap Hangfire operations](../../bootstrap.md#hangfire-jobs),
and [testing/checkpoints](../../testing-and-build.md#checkpoint-manifest-card-0585).

## Decisions

### D-1. Explicit intent and bounded configuration

Retain `ExpectationWatchdogSettings` and its directive model: stable Id, standing AgentId,
BoardId, AuditCardId, OperatorChannelId, Enabled, optional UTC expiry, and targets per runner
with an in-flight target and allowed provider/model/subscription identifiers. One enabled
directive per board; the agent and audit card must belong to that board. Subscription identifiers
are not credentials. Validate references against current persisted entities and the configured
runner catalog, not a hard-coded fleet. Do not derive targets from prompt prose or host limits.
"3 local + 3 server2" remains an example and deterministic incident fixture, not a live default.

Add positive integer minute settings: `QueuedMinutes=10`, `CapacityMinutes=10`,
`MissingSessionMinutes=10`, `NoteMinutes=10`, `AnswerMinutes=5`, `NudgeCooldownMinutes=10`,
`RepeatMinutes=30`; permit 1..1440 and require RepeatMinutes >= NudgeCooldownMinutes.
Use TimeProvider/UTC. Include timing and destination semantics in the normalized configuration
digest so an intentional config change retires stale unsent work and starts fresh deficit clocks.
Retain history. Syntax errors fail startup validation; missing runtime references become visible
configuration faults. Missing/disabled channels retain existing unsent operator debt and never
cause fallback to another conversation. Disabled/expired directives authorize no new sends.

Ship `Enabled=false`, `Directives=[]`. Activation supplies real references in local configuration,
through the canonical main-checkout restart; no production IDs, addresses or secrets in tracked
appsettings. This initial configuration workflow does not require a new UI or CRUD API.

### D-2. Five independent checks, with positive and unknown evidence

Use current board/project/caller scoping. Include board-bound tasks regardless of which caller
launched them; unbound tasks need matching project and proven standing-agent caller ownership.
Exclude ambiguous legacy ownership and show its diagnostic count. Count work by actual RunnerId.
Watchdog Check events, ACKs, its comments and its own job activity are never pipeline progress.

| Check | Trigger and clock | Suppression, unknowns and useful prompt |
|---|---|---|
| Stalled pipeline | A Queued task (Held is an event, not a status) in its current stint for >= QueuedMinutes, with no scoped dispatch in that interval. Retried/rerouted work starts a new stint; HeldAged does not. | Keep the current policy's evidence-backed moving land/full live capacity exceptions. Stale text, finished land, partial/ended cap and unrelated working tasks cannot suppress indefinitely. Include queue age, last dispatch, current hold, owner/operation when known and inspection action. |
| Dispatch-wide fence | Immediate on the first successful scan proving every relevant dispatch path is fenced. Consider queued demand and admissible candidates for ready backlog. | Unknown/unblocked paths defeat an all-board claim. A healthy prepared remote lane defeats a desktop lease-wide assertion. Quota/model refusals concern new admission, not already-admitted work. A healthy live journal operation is an ordinary wait; dead/ambiguous child or unknown lease owner is reported without inventing recovery authority. Partial fences are named by lane. |
| Below directive | Dispatched + Working below a runner target continuously for >= CapacityMinutes while eligible Backlog remains. | Exclude archived/owned/explicitly held/unrated-import cards and cards with open work. Queued/Blocked are reported separately; do not suggest duplicate dispatch. Reset the deficit clock when target is met or eligible backlog empties. Stage pins, dependencies, scope and host limits remain dispatcher/orchestrator decisions; report a constrained target rather than overriding them. |
| Silent in-flight | Dispatched, no report, no usable session, and no task-local transcript activity for >= MissingSessionMinutes from max(dispatch, last activity). Also roll up the existing fresh progress-stall verdict for live tasks. | Refresh transcript/session/task facts before minting a nudge. Runner unreachable or unobserved inventory is Unknown, not proof that a session is gone. New report/activity and intentional Blocked status suppress. No kill, resume, reroute, automatic Check launch or task settlement. |
| Undelivered caller notes | Unconfirmed land/completion outbox debt or queue-only Delegation/Check notes aged >= NoteMinutes from the original obligation/queue creation. Include Queued/AwaitingReceipt/retry/unavailable outbox states and pending, parked or interrupted/unconfirmed queue attempts. | Linked rows form one obligation, keyed by notification ID; otherwise use queue ID. Scope the destination to the configured agent, including older owned sessions. Exclude explicitly canceled/superseded queue-only notes, non-caller briefs, human/channel input and supervision. Honor the owning producer's exact receipt matcher; Sent, a screen echo or quoted note is insufficient. Recheck after catch-up; never mutate/replay the watched note. |

Unknown evidence preserves that subject's open condition; it cannot resolve it or suppress an
unrelated due note/answer deadline. Successful clear observations twice, at least one minute
apart, resolve an episode. A stopped runner is a known fence only when availability evidence
supports that classification; transport errors alone carry an observation fault.

### D-3. Production observers and CARD-0648 integration

Add a concrete `ExpectationObservationAdapter` and an `IExpectationCatchUp` implementation
using existing runner inventory, provider policy snapshots, `AgentSessionRuntime` catch-up and
workspace/progress evidence. They only observe: no dispatcher tick, admission Enforce call,
repository mutation lease, recovery script or extra LLM invocation. Snapshot facts carry as-of
times; missing/stale quota is Unknown. Use accepted session generation/runner ownership.

Prefer existing structured hold/owner evidence (`DispatchHoldDetails`, CARD-0672 hold classes)
to new strings. Where journal classification is required, reuse the read-only inspector with
an already resolved repository common directory; do not launch git on the detection path.
If that directory is unavailable, retain the held fence with unknown journal health. Never infer
descendants exited or locks cleared from PID death. The current inspector can distinguish live,
dead and ambiguous journal evidence; it is not authorization for deletion.

CARD-0648 owns boot/periodic recovery and restart integration. Until it lands, prompts recommend
`scripts/recover-repository-children.ps1` read-only inspection and the documented operator
decision. Never suggest automatic `-Execute -ConfirmDescendantsExited`. Once recovery exists,
consume its outcome if available, re-observe the actual fence, and let normal episode resolution
clear it. Do not build a second recovery or alert loop. CARD-0641 keeps note enqueue/receipt/retry
authority; this feature neither rewrites its outbox nor interprets note absence as a failed land.

### D-4. Independent prompt path and evidence states

Keep `ExpectationNudgeDeliveryService -> IExpectationPromptSender ->
SessionMessageQueueService.SendExpectationNowAsync -> DeliverAsync(overlayRecovery:false)`.
It bypasses WhenIdle and repository leases while preserving session serialization, current
standing ownership/generation, rules/modal/composer safety and LF + bracketed paste + separate
Enter. Never route through ordinary Mode.Now failure recovery, a queue reconciler, raw runner
input, Escape, auto-compact, kill or restart. Lock contention has a bounded wait; it becomes
operator escalation rather than an unbounded dependency on the session queue.

Extend the ledger with `AttemptStartedAt`, `AnswerDueAt`, `ReceiptSequence`, `AnsweredSequence`,
and an explicit immutable config identity for the nudge. Commit attempt time, frozen session,
generation and transcript floor before the first byte, under the existing session lock. Set the
answer deadline at that commit; for an unattempted nudge stuck behind unavailable execution,
the deadline cannot exceed CreatedAt + AnswerMinutes. Do not restart deadlines after restarts,
catch-up failures or a late receipt. Legacy attempted rows without a timestamp use CreatedAt
conservatively; never make them sendable again. Generate additive migrations via the CLI.

For this watchdog, complete **UserPrompt** with the full immutable body, matching session,
generation evidence and sequence floor is delivery. A complete QueuedUserPrompt is submission
only: keep `Submitted`, permit ordinary input under existing composer rules, and wait for the
UserPrompt before setting ReceiptAt/ReceiptSequence. Do not change shared channel/land receipt
contracts. Screen-only, missing-floor, partial and housekeeping evidence cannot confirm. Update
the inherited test that currently confirms both kinds to assert this distinction.

After a committed attempt, interruption becomes Uncertain; subsequent passes only reconcile
the same transcript and never retype. Keep every S4 composer repair, including two empty Claude
snapshots and operator-token release. Submitted/Released does not acknowledge, confirm or clear
operator debt. A current-generation unsubmitted body continues to protect ordinary input.

`ExpectationResponseMatcher` accepts only a whole line `[expectation-ack:<full-nudge-guid>]`
in non-error AssistantText after that nudge's complete UserPrompt in the frozen destination,
with an action or reason in the answer. Validate sequence/turn and generation; a later session
cannot acknowledge an old attempt. User/tool/thinking text, quoted markers, different IDs and
unrelated activity cannot answer. Store answer evidence; ACK never resolves the underlying
condition. Transcript pull failure preserves uncertainty without extending the deadline.

### D-5. Durable escalation and suppression

Reconcile receipts/ACKs and already-due operator work before creating new direct prompts. A
definitely unavailable/unsafe recipient or failed/uncertain send creates immediate operator
debt. A submitted/confirmed but unanswered prompt creates debt at AnswerDueAt. An interrupted
attempt discovered on restart is immediately due. The operator publisher must not acquire the
session lock or run catch-up as a prerequisite; failed/slow catch-up cannot postpone its deadline.

Use the existing nudge outbox state as the authority; add frozen page provider/conversation/body
and digest, publication ordinal, claim token/expiry, first-due/last-attempt times and last-published
time as needed. Claim transactionally with optimistic ownership; only one live claimant sends.
Freeze the configured enabled ChatChannel address at claim, audit before I/O, recheck eligibility
before publication, and stamp completion conditionally on that claim. Renew within a bounded
send budget; a crash leaves recoverable debt after claim expiry. No DB transaction spans I/O.
Config changes retire an unsent old destination rather than silently redirecting the same page.

Publish directly using `IAntiphonMessagingProducer.SendAsync(ChannelReply)`, setting Channel
and ConversationId from that frozen ChatChannel, with no agent preparation or digest route.
Include directive/nudge IDs, expected/actual counts, condition age, relevant card/task IDs,
prompt/answer status and a safe next action. Broker acceptance sets Published; it proves neither
gateway delivery nor human reading. Failure retains debt and retries after 1, 5, then 15 minutes
(15-minute cap). A lost publish answer may duplicate the same immutable page; preserve its ID
and ordinal and explicitly accept at-least-once delivery. Do not claim exactly once.

Suppress unpublished debt on valid ACK or positively observed resolution/config deactivation.
An ACK racing after publication cannot retract the message. Record that race honestly. An
unanswered episode gets no periodic repeat typing; operator reminders are at most one per
RepeatMinutes. An answered but unresolved episode may receive a new nudge after RepeatMinutes
from its answer. One aggregate nudge per directive per cooldown; a new higher-severity fence
may bypass once. Condition wording, holder-known/unknown flips and elapsed age are not new
identities. Preserve all subject audits while showing at most three examples inline.

Format against the resolved destination's **UTF-8 byte** ceiling before freezing the nudge body,
including its identity and ACK instruction. The existing 6,000-character formatter is not proof
of fitting a runner's write limit. Include the status pointer and truncate evidence only at safe
text boundaries. If the minimum marked prompt cannot fit, record refusal and page; no second,
different body or spill is invented after a committed attempt.

### D-6. Audit, scheduling, failure isolation and operational status

Keep nudge creation and Check events on affected tasks plus the directive audit-card discussion
in one transaction. Add correlated audit entries for attempt outcome, answer, operator due,
published/failed/suppressed/reminder and resolution. The audit-card fallback covers capacity and
admission conditions with no task. This is discussion/observational evidence, not a card move,
new decision column or tracker write. Failed audit persistence means no new outbound I/O.

Register `antiphon:expectation-watchdog`, cron `* * * * *` UTC, queue `expectations`, dedicated
one-worker Hangfire server in the main application. Keep the existing worker on `default`.
Both require `Hangfire:ServerEnabled`; watchdog registration/worker additionally requires
ExpectationWatchdog.Enabled. Register through the DI-resolved recurring manager and trigger
one startup catch-up. Hangfire is in memory; PostgreSQL owns every clock/claim/episode/outbox.
Set automatic Hangfire retries to zero; next sweep uses the durable state. No Windmill schedule.

Use separate scoped contexts for independent directives and durable short claims to prevent
overlap. A known fault in one directive cannot skip the rest or pending pages. Limit a pass to
50 seconds, probe to 5 seconds and direct send to 20 seconds; bound operator sends separately
to 5 seconds. Await owned cancellations; never leave detached I/O after timeout. Persist fair
cursors, including pagination within a directive (the current first-100 note read alone can
starve later rows). Budgeted/incomplete scans never count as a clear observation. Query scoped
projections and bounded transcript ranges instead of reloading the fleet. Do not wrap mutations
or outbound messages in general read-retry policies.

Add read-only `GET /api/expectation-watchdog?boardId=<guid>`: boardId required; use existing
task-token/project authorization. Return config faults/digest, enabled/expiry, last successful
scan and errors, lane counts, episodes, nudge timestamps, receipt/answer evidence IDs, operator
debt/attempt state and audit links. Paginate history. Cross-board reads disclose nothing; omit
channel addresses, secrets and full transcript bodies. No new ACK or send route. Preserve the
existing operator-only `/api/sessions/{id}/expectation-hold/release` route unchanged.

This is independent of dispatch and note delivery, **not** independent of the main process,
PostgreSQL, session transport or outbound broker. A process/DB outage cannot be detected by its
own Hangfire worker; status freshness makes that limit observable. An external outage monitor
and gateway delivery receipts are outside CARD-0650.

## Implementation slices

These S1-S4 are continuation slices, not the old plan's similarly numbered rounds. Commit each
slice before its checkpoint group, keep changes on the task branch, and use the immutable plan
commit in the Code brief. TestDesign must inspect new seams/fixtures and finish the guard/PC
inventory before Code. The slice table names proposed files, not files created by this Plan.

| Slice | Production files and boundary | Test files / completion |
|---|---|---|
| S1: real observations and full debt coverage | Extend `Application/Settings/ExpectationWatchdogSettings.cs`, validator and directive digest; `Application/Services/ExpectationObservation.cs`, `ExpectationSnapshotReader.cs`, `ExpectationWatchdogPolicy.cs`, `ExpectationWatchdogService.cs`; add `Infrastructure/Agents/ExpectationObservationAdapter.cs` and `ExpectationTranscriptCatchUp.cs`. Reuse existing runner/provider/journal/progress owners. Configurable clocks, independent unknowns, current dispatch applicability, queue-only notes and paging. | Extend directive tests; add `ExpectationObservationAdapterTests.cs`, `ExpectationNoteDebtTests.cs`; retain policy/snapshot/debt/episode/ledger/hold regressions. CP-1..3. |
| S2: answer and attempt lifecycle | Extend `Domain/Entities/ExpectationNudge.cs`, mappings in `Infrastructure/Data/AppDbContext.cs`, CLI migration/snapshot; modify nudge delivery, ledger, formatter and narrow expectation send partial; add `Application/Services/ExpectationResponseMatcher.cs` and `ExpectationResponseService.cs`. Persist deadlines/floors, distinguish submission, implement ACK/suppression/repeat state and durable publication fields. | Add `ExpectationResponseTests.cs`, `ExpectationSchedulingTests.cs`; update inherited receipt-kind method; retain all direct delivery/hold-release/public queue regressions. CP-4..5. |
| S3: operator delivery and scoped visibility | Add `Application/Services/ExpectationOperatorDeliveryService.cs`, status DTO/service and `Api/Endpoints/ExpectationWatchdogEndpoints.cs`. Claim/retry/recover publication, exact addressing, all state audits, status authorization. Extend existing outbox fields, not another notification system. | Add `ExpectationEscalationTests.cs`, `Api/ExpectationStatusTests.cs`. CP-6. |
| S4: scheduled complete feature | Add `Infrastructure/Agents/ExpectationWatchdogJob.cs`; wire options, validators, concrete adapters and services in `Program.cs`, recurring registration in `HangfireConfiguration.cs`, inert appsettings. Update owners `docs/orchestration-loop.md`, `ops-http.md`, `antiphon-api.md`, `bootstrap.md`, `session-runtime-invariants.md`, `telegram.md`, and the ACK paragraph in `server/Bundles/orchestrator.md`. | Add `Infrastructure/ExpectationWatchdogJobTests.cs`, `Application/ExpectationWatchdogScenarioTests.cs`; production-composition and virtual overnight scenarios, existing startup safety. CP-7. |

Paths above are under `server/` unless prefixed `docs/`; test paths are under
`tests/Antiphon.Tests/`, with unqualified test classes in `Application/`.

## Verification design

### Inspection and delivery inventory

Read current delivery fixture and its busy-caller test, receipt/debt/snapshot implementations,
hold-release HTTP fixture, isolated `ExpectationTestWorld`, startup-safety DI fixture, nudge
ledger/state, queue/outbox models and channel producer contract. Roster counts below are source
counts, not execution evidence. TestDesign must inspect remaining test bodies and append a
complete 1:1 safety-guard/PC mapping; do not hand Code an inferred mutation-clean claim.

| Path | Producer, destination and durable identity | Persistence, recovery and receipt |
|---|---|---|
| Observation -> nudge/audit | Minute job -> configured standing agent; directive/config/episode IDs and nudge GUID | Atomic nudge/task/card transaction, then current-session resolution. Rollback sends nothing; restart processes committed unsent intent after fresh eligibility. |
| Nudge -> session -> answer | Direct sender -> frozen session/generation; nudge GUID/body digest/floor | Attempt before bytes. Real queue delivery machinery with WhenIdle blocked; no automatic replay of uncertain input. Complete UserPrompt receipt then matching assistant ACK. Neither queue insert, screen nor Submitted is answer evidence. |
| Unanswered/unreachable -> operator | Due nudge -> explicit ChatChannel; nudge GUID/publication ordinal/claim | Audit and frozen page before broker I/O, persisted retry and claim recovery. Fake producer proves addressing/acceptance only; operator activation verifies actual gateway arrival separately. |

Use real PostgreSQL isolated schemas and the real SessionMessageQueueService around the existing
controlled runtime (`ExpectationDeliveryFixture`/`BridgeQueueHarness`), not an adapter-call-only
test. Extend that fixture with deterministic time, a real enabled ChatChannel and recording/failing
producer; its current world lacks the channel row. Inspect actual persisted UserPrompt bytes and
sequence. Cover busy and already-eligible recipients and every persistence/I/O crash boundary.
The controlled terminal does not prove native TUI behavior; preserve native input code. Any
native input change needs a stated scope change and named native checkpoint before execution.
Real Program test boots keep ProductionRunnerGuard/Hangfire disabled and never reach 17204.

### Ordinary coverage and planned new methods

Each name below is one non-parameterized TUnit execution. Matrix assertions inside it do not
inflate Min. Existing methods remain unless a changed contract explicitly requires updating one.

| ID | Class and new methods | Decisive assertions |
|---|---|---|
| V-1 | `ExpectationDirectiveTests.C650_Configurable_windows_validate_and_change_digest`; `ExpectationObservationAdapterTests`: `C650_Composition_reads_actual_catalog_and_policy`, `C650_Unknown_runner_preserves_other_due_conditions`, `C650_Prepared_remote_path_defeats_repository_wide_fence`, `C650_Live_dead_and_unknown_journals_do_not_grant_recovery`, `C650_Catchup_precedes_absence_judgment`, `C650_Paged_observation_eventually_visits_all_subjects` | Defaults/custom threshold minus-one/exact boundary; inactive config; fresh/stale/unknown probes; no dispatcher/lease/mutation call; >100 notes eventually visited without falsely clearing unseen subjects. |
| V-2 | `ExpectationNoteDebtTests`: `C650_Queue_only_completion_and_check_notes_age`, `C650_Linked_outbox_and_queue_form_one_obligation`, `C650_Confirmed_superseded_and_noncaller_rows_are_excluded`, `C650_Catchup_uses_owning_receipt_and_preserves_original_age` | Pending/parked/interrupted debt; correct destination/scope; quoted/partial/old receipt negative; no queue mutation or duplicate recovery. |
| V-3 | `ExpectationResponseTests`: `C650_Only_complete_UserPrompt_opens_ack_window`, `C650_Assistant_ack_needs_matching_id_turn_and_action`, `C650_Wrong_session_generation_and_sequence_do_not_answer`, `C650_Queued_submission_is_not_delivery_or_answer`, `C650_Deadline_survives_restart_and_catchup_failure`, `C650_Receipt_without_ack_still_becomes_due`, `C650_Late_ack_suppresses_unpublished_debt`, `C650_Ack_does_not_resolve_or_retype_uncertain_prompt`; `ExpectationSchedulingTests`: `C650_Unanswered_episode_pages_without_repeat_typing`, `C650_Acknowledged_unresolved_episode_repeats_after_answer_window`, `C650_Config_disable_change_and_expiry_cancel_stale_unsent_work`, `C650_Prompt_byte_ceiling_keeps_identity_and_audit` | Full correlated receipt/answer, deadline at N-1/N, committed-attempt crash, config change during claim, emoji/multibyte ceiling, at most one urgent cooldown bypass, all audit subjects retained. |
| V-4 | `ExpectationEscalationTests`: `C650_Unanswered_nudge_pages_exact_channel_at_deadline`, `C650_Unavailable_or_unsafe_recipient_pages_immediately`, `C650_Broker_failure_retries_same_frozen_page_after_restart`, `C650_Accepted_but_unstamped_page_recovers_with_same_identity`, `C650_Concurrent_claims_and_ack_have_one_normal_publication`, `C650_Disabled_channel_retains_visible_unsent_debt`, `C650_Reminders_are_bounded_and_audited`, `C650_Audit_failure_prevents_publish`; `ExpectationStatusTests`: `C650_Status_exposes_scan_and_delivery_evidence`, `C650_Status_requires_authorized_board_and_paginates`, `C650_Status_never_exposes_channel_address_or_transcripts` | Real DB claims/rollback/reload, fake broker acceptance/failure/timeout, no session lock or note/digest prerequisite, honest at-least-once boundary, audited late ACK race and sanitized status. |
| V-5 | `ExpectationWatchdogJobTests`: `C650_Registers_minute_job_on_dedicated_queue`, `C650_Disabled_host_or_feature_starts_no_worker`, `C650_Fresh_host_recovers_due_work_with_real_adapters`, `C650_Default_worker_occupancy_does_not_block_watchdog`, `C650_Slow_directive_does_not_starve_operator_debt`, `C650_Overlapping_passes_and_budget_cursors_are_safe`; `ExpectationWatchdogScenarioTests`: `C650_Overnight_fence_nudges_and_pages_without_task_notes`, `C650_Idle_and_working_callers_receive_complete_prompt`, `C650_Recovery_and_late_note_receipt_resolve_without_mutation`, `C650_All_five_conditions_have_durable_subject_audits` | Real DI composition and isolated Hangfire storage; no optional no-op adapter; controlled worker occupancy; startup replay/config; 5-hour incident with virtual time and three held tasks; full recipient UserPrompt and broker target; no git/recovery/kill/spawn/card-move side effects. |

Regressions: R-1 directive/ledger/pipeline/snapshot/debt/episode semantics (34 existing executions);
R-2 direct delivery (14), receipt (6), hold-release HTTP (4) and four exact public queue methods
(28 total); R-3 `DispatchHeldAttentionTests` (11 expanded executions); R-4
`HangfireStartupSafetyTests` (12). The old manifest's counts for debt, direct delivery, hold
attention and Hangfire are stale. Planned total is **129** = 85 existing + 44 new executions.

TestDesign's safety inventory must split independently bypassable scope/ownership/generation,
attempt/receipt/answer floors, queue/lease independence, composer and no-recovery, audit atomicity,
publication claim/address, config/cooldown, observation unknowns/fairness, and startup gates.
Each receives a compiling, method-scoped PC-n with a decisive failure. Mutation is a separately
commissioned post-land stage; neither a build error nor a test that only compares constants is red.

### Cost and execution rules

Estimated ordinary Code checkpoint floor: **51 minutes** (8+6+1+8+6+10+12), including four
isolated builds and three reused-build rows. Estimated authoring: 420 minutes; Code total about
471 minutes, split into the four slices. These are estimates, not measurements or timeout targets.
Reusing three builds saves three build invocations; no measured minute saving is claimed.
TestDesign will cost the method-scoped positive-control battery separately after its guard
inventory; this Plan does not commission it or hide it within the ordinary floor.

Use CARD-0723, one run per committed slice group. Build the checkpoint launcher once through
the build-slot wrapper (report this tooling bootstrap separately from CP verification), then
run it without rebuilding. Its detached checkpoint drivers acquire their own slots; do not hold
a second outer build slot while waiting for them. For example, after S1:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c650-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints -nodeReuse:false
dotnet run --no-build --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-28-card-0650-expectation-watchdog-continuation-plan.md --after S1
```

When `run` or `wait` returns 75, continue
`dotnet run --no-build --project tools/Antiphon.Checkpoints -- wait <run-id>` until final
completion; never abandon a running executor. The four CP builds plus launcher bootstrap are
the planned five builds; the bootstrap adds an estimated 2 minutes, so the overall Code budget
is about 473 minutes while the ordinary CP floor remains 51.
Do not manually run the old checkpoint script for each row. Commit before each group, report
CP/commit/build/filter/executed/passed/failed/skipped/TRX and reruns. Slot timeout is reported,
never bypassed. CLI migration generation is required authoring; any prerequisite build is an
explicitly reported unlisted run with reason, through the build-slot wrapper. Additional tests
need a stated coverage reason; failures rerun the affected CP. No solution-wide sweep is implied.

### Checkpoints

Closed list for the continuation. All rows must pass with no skipped results. Each new class's
entire roster above is required; existing roster floors must not be weakened. TestDesign may
amend this manifest with justified additional coverage before Code, and must commit that change.
Every row is serial to prevent two test hosts sharing an isolated build output or queue fixture;
this does not replace in-assembly process-spawn limiters. Escaped pipes are Markdown only.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c650-observe/` | observations | `/*/*/(ExpectationDirectiveTests*)\|(ExpectationPipelineTests*)\|(ExpectationSnapshotTests*)\|(ExpectationObservationAdapterTests*)/*` | V-1, R-1 | all 26 listed executions, 0 failed/skipped | 26 | 8 | true |
| CP-2 | S1 | CP-1 | note-debt-ledger | `/*/*/(ExpectationLedgerTests*)\|(ExpectationDebtTests*)\|(ExpectationEpisodeTests*)\|(ExpectationNoteDebtTests*)/*` | V-2, R-1 | all 19 listed executions, 0 failed/skipped | 19 | 6 | true |
| CP-3 | S1 | CP-1 | dispatch-held | `/*/*/DispatchHeldAttentionTests*/*` | R-3 | all 11 expanded executions, 0 failed/skipped | 11 | 1 | true |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c650-answer/` | receipt-answer | `/*/*/(ExpectationResponseTests*)\|(ExpectationSchedulingTests*)\|(ExpectationReceiptTests*)/*` | V-3, R-2 | all 18 listed executions, 0 failed/skipped | 18 | 8 | true |
| CP-5 | S2 | CP-4 | direct-input | `/*/*/(ExpectationDirectDeliveryTests*)\|(ExpectationHoldReleaseEndpointTests*)\|(SessionMessageQueueServiceTests*)/(C650_*)\|(Send_now_delivers_immediately_and_does_not_queue*)\|(Delivery_sends_body_then_a_separate_CR_not_one_combined_write*)\|(Multiline_delivery_is_wrapped_in_bracketed_paste*)\|(When_idle_message_is_held_while_the_agent_is_working*)` | R-2 | all 22 selected executions, 0 failed/skipped | 22 | 6 | true |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c650-operator/` | operator-status | `/*/*/(ExpectationEscalationTests*)\|(ExpectationStatusTests*)/*` | V-4 | all 11 listed executions, 0 failed/skipped | 11 | 10 | true |
| CP-7 | S4 | `tests/Antiphon.Tests -> bin-c650-scheduled/` | scheduled-scenario | `/*/*/(ExpectationWatchdogJobTests*)\|(ExpectationWatchdogScenarioTests*)\|(HangfireStartupSafetyTests*)/*` | V-5, R-4 | all 22 listed executions, 0 failed/skipped | 22 | 12 | true |

## Acceptance and handoff

Review must check ordinary CP evidence, producer-to-recipient delivery, scope/unknowns, audit and
claims, every preserved composer repair, and the explicitly separate submission/receipt/answer
states. Land through the normal workflow. No implementation, builds, tests, live messages or
deployment were performed by this Plan task.

After publication, activation is caller-owned: install real directive/channel configuration on
the main instance, use the canonical main-checkout restart and verify `/api/version` source SHA.
Observe recurring registration and a fresh successful status scan. In an explicitly commissioned
test conversation, demonstrate working-session receipt despite a held ordinary note, an ACK
preventing the page, an unanswered nudge arriving in the exact operator conversation, and restart
retention of due work/audits. No real dead journal, artificial production quota exhaustion, or
stopping a working task is required. Broker acceptance alone does not satisfy channel arrival.

Disable/expire the directive to stop further prompts/pages and preserve history; global disable
stops the dedicated worker/job. Re-enable only after fresh observation retires stale unsent debt.
Do not call the five-hour stall fixed until this activation evidence exists.

Next stage: **test-design**. Inspect touched test bodies and production seams, finish the complete
guard-to-positive-control inventory and its cost, and validate/refine this closed checkpoint
manifest. Preserve D-1..D-6 unless evidence requires an explicit plan amendment. No unresolved
product decision blocks that work; actual production IDs and channel receipt belong to activation.
