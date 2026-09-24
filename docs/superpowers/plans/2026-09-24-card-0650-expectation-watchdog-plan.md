# CARD-0650: expectation watchdog

Plan date: 2026-09-24. Plan task: `af7c5c18`. Inspected source:
`c5e32c2f9029cd1c8bb2132bbda59bcd44bd1424`.
Card: `6cb4a3b9-f903-48cc-bb22-e2623c7e9229`, board
`8988ca03-7414-47ad-b0b6-51556c701703`. The card and CARD-0648/CARD-0641
were read through `scripts/card.ps1 get`, not inferred from exported card files.

Deliver a main-instance Hangfire watchdog that compares an explicit standing directive with
board work, records a durable nudge, submits it directly to the standing orchestrator, and
pages the configured operator conversation when submission fails or the orchestrator does not
answer. It detects and explains; dispatch, recovery, cancel, restart, landing and card moves
retain their existing owners. Verification design is folded into this Plan by the brief's
explicit executable-checkpoint requirement and `next: code` instruction. Decisions below are
implementation choices settled by this Plan, not unresolved defaults requiring Decide.

## Ground truth

| Card assumption / question | Inspected code actually does | Design consequence |
|---|---|---|
| A periodic job can use the main Hangfire. | `server/Program.cs:723-732,858-869` registers in-memory storage, one worker, and recurring census/residue jobs only when `Hangfire:ServerEnabled`. `HangfireConfiguration.cs` uses a DI-resolved manager. | Re-register the minute job at boot; persist watchdog evidence in PostgreSQL. Give the watchdog its own Hangfire queue and worker so a long census cannot starve it. Preserve the test-host worker gate. |
| A Held task is a task status. | `AgentTaskStatus.Queued` plus `AgentTaskEventType.Held/HeldAged` is the durable shape. `AgentTaskDispatcher.LoadQueuedHoldIndexAsync` derives hold episodes after Dispatched and deduplicates detail. | Do not add a Held status or reset age on repeated warning events. Use the current queue stint and last real dispatch. |
| A fence affecting one lane blocks the fleet. | Dispatcher checks include repository exclusion, model hold, runner availability, scope, pins, remote preparation and capacity. `DispatchHoldDetails` owns stable reason strings. Local and remote tasks can have different blockers. | Evaluate a declared board/repository/runner scope; require evidence for every relevant path before saying all dispatch is fenced. A server2 failure alone does not mean local dispatch is fenced. |
| Provider quota holds queued tasks. | `SubscriptionQuotaGate.EnforceAsync` refuses admission; dispatcher comments and its quota arm explicitly treat quota as a warning for already-admitted tasks (CARD-0336). Missing/stale samples pass through. | Label quota as a new-admission fence only when fresh policy verdicts refuse every configured candidate. Never claim existing queued work is blocked by quota alone. |
| The CARD-0641 lease gate is still the current notification implementation. | At the inspected SHA, `AgentTaskLandNotificationService` has already lost its repository-lease dependency. It still calls `EnqueueAsync(... WhenIdle, deliverIfIdle: false, sourceLandNotificationId: ...)`. Non-legacy land lifecycle receipts already accept submitted QueuedUserPrompt. | The historical incident justifies an independent path, but do not reimplement or undo CARD-0641. Test independence from idle waiting and blocked notification reconciliation, rather than asserting the deleted gate still exists. |
| Send-now is a durable delivery primitive. | `SessionMessageQueueService.EnqueueAsync` Mode.Now takes the session lock and verifies delivery but creates no queue row. Its failure path can invoke `HandleDeliveryFailureAsync`, which can kill an always-on session. `EnqueueDeliveringNowAsync` creates a row but can return it to ordinary Pending recovery. | Add a narrow, non-recovering direct-send entry point reusing delivery machinery; own durability and uncertainty in a separate watchdog ledger. Do not use the row-producing overlay path or generic failure recovery. |
| A direct prompt can ignore any queue problem. | `HeldBackTypingBlocksTheComposer` protects against appending a new body to a parked attempted row. Runtime rules require LF, bracketed paste, separate Enter and transcript evidence. | Bypass the idle policy and repository lease, not composer safety, modal/rules gates or session ownership. Unsafe input routes to the operator. |
| Existing stalls cover all missing work. | `DetectStalledProgressAsync` selects Dispatched/Working tasks with a session and uses `TaskProgressPolicy`, a fresh transcript pull, workspace evidence and incident dedupe. `AutoEscalateStalledAsync` is a separate configured role policy. | Roll up current progress-stall evidence without changing either policy. Add the missing dispatched/no-session observation. Watchdog itself never calls escalation or stop. |
| A standing throughput directive is already machine readable. | `OrchestratorSettings` holds card-spawn tick settings, while `AgentTaskPipelineStatusService` exposes fleet-wide advisory stage recommendations. Neither means “3 local + 3 server2 for this board tonight.” | Add explicit watchdog directive configuration. Do not read prose from Agent.Details, infer target from MaxConcurrentTasks, or consume fleet totals as board totals. |
| Existing card-spawn eligibility means ready Backlog. | `OrchestratorService.LoadEligibleCandidatesAsync` selects active-column cards, not Backlog. `Card` has AutoDispatchHeldAt, ownership and archive fields; imported unrated cards have an existing NeedsHumanReview projection. | Define a read-only backlog-candidate predicate for this advisory feature; do not call card spawning or treat arbitrary backlog count as executable work. |
| Existing alert routing guarantees an operator gets a page. | `IAlertService` is deliberately non-throwing; `ChannelAlertRouter` uses an in-memory throttle and `AlertDigestFlusher` is best effort. `IAntiphonMessagingProducer.SendAsync(ChannelReply)` is the existing direct broker seam. | Send the operator escalation from durable watchdog debt to an explicitly configured channel, bypassing digest/alert throttles. Record broker acceptance separately from human receipt. |
| Every nudge can be audited on its subject. | `AgentTaskEvent` lives in `Domain/Entities/AgentTask.cs`; `Check` is already observational. `CardComment` is a persisted discussion, unlike session-inject comments or Move/Reopen revisions. | Add a Check event on each affected task; use the directive's audit card discussion when there is no task (capacity/admission fence). Commit these with the nudge before I/O. |

Source owners: `docs/project-context.md`, `docs/orchestration-loop.md`,
`docs/agent-card-lifecycle.md`, `docs/session-runtime-invariants.md`, `docs/ops-http.md`,
`docs/telegram.md`, and `docs/testing-and-build.md` (checkpoint manifest and CARD-0403).
CARD-0648 is still Backlog in the read card: this plan does not assume its recovery is available.

## Decisions

- **D-1 — Explicit typed server configuration owns the directive.** Add
  `ExpectationWatchdogSettings` under `ExpectationWatchdog`, bound and validated in Program.
  `Enabled=false` and `Directives=[]` ship inert. A directive has stable `Id`, `AgentId`,
  `BoardId`, `AuditCardId`, `OperatorChannelId`, `Enabled`, optional `ActiveUntilUtc`, and
  `Targets` containing `RunnerId` (null means local), positive `InFlightTarget`, and explicit
  allowed `(AgentKind, ModelLevel, SubscriptionKey)` candidates for new work. SubscriptionKey
  is an identifier, never a credential. Candidate declarations are observation scope, not
  authority to override routing, model holds, quota, capabilities or stage concurrency.
  One enabled directive per board; target runner IDs are unique and configured runners only.
  Example intent: local target 3 plus runner `server2` target 3. Resolve real GUIDs and actual
  allowed candidates from the main instance during activation; commit no production IDs or
  channel addresses in appsettings. The audit card must belong to the board; the standing
  non-pool agent must own that board; the channel must exist and be Enabled. Invalid references
  produce a visible configuration fault and no sends. Syntax/duplicate/range errors fail options
  validation. Config edits require the normal main-instance config/restart workflow for v1.
  Persist a normalized configuration digest with observations and nudges so changing targets
  retires old episodes without erasing their history. Reject card-description parsing (ambiguous
  and unauditable), Agent.Details parsing (prompt text is not configuration), global capacity
  defaults (not an instruction), and a new management UI/CRUD subsystem for this first cut.

- **D-2 — Time settings and deployment are explicit.** UTC clock from TimeProvider throughout.
  Cron `* * * * *` UTC; QueuedMinutes=10, CapacityMinutes=10, MissingSessionMinutes=10,
  NoteMinutes=10, AnswerMinutes=5, RepeatMinutes=30. A confirmed dispatch-wide fence is due on
  its first observation (normally within one minute), not after QueuedMinutes. The five-minute
  answer deadline starts at the committed attempt/known-unavailable time, not a receipt that
  may never appear. Unavailable recipient, unsafe composer and explicit transport failure are
  eligible for immediate operator escalation. One minute is a scheduling target while the main
  process/DB is available, not an SLA during process or database failure. No Windmill job,
  Scheduled Task, runner-side timer or LLM invocation performs detection.

- **D-3 — Scope and evidence precede classification.** Board-bound tasks are included by card
  BoardId and matching ProjectId; null legacy ProjectId is resolved using `AgentTaskScope.Resolve`
  conventions. Unbound tasks count only when their project matches and their caller is provably
  owned by the configured standing agent (immutable StandingAgentId; ambiguous legacy ownership
  is excluded and counted diagnostically). Do not use cwd/name or a fleet pipeline total as proof.
  Count non-specialist Dispatched/Working tasks by task RunnerId, not by where their Git checkout
  lives. Queued and Blocked are shown separately, never credited as running capacity. The targets
  are board throughput, so include other callers' board-bound work to avoid duplicate pickup.
  New-work candidates are non-archived Backlog cards on this board with no owner/live card session,
  no open bound task, no explicit AutoDispatchHeldAt, and not NeedsHumanReview under
  `BoardService.NeedsHumanReview`. The inspected Card shape provides no universal dependency-ready
  flag; prose dependencies are not parsed into invented permission. Call these “eligible backlog
  candidates” and ask the orchestrator to select the next admissible stage. Never assert a launch
  was admitted. Stage pins/limits and workspace overlap remain the dispatcher's decision.

- **D-4 — Read authoritative observations without becoming another dispatcher.** Implement
  `ExpectationSnapshotReader` and a pure `ExpectationWatchdogPolicy`. Query scoped durable
  task/events/notes/transcripts; use runner registry availability and the existing quota/model
  policy for the directive's declared candidates. Do not call TickAsync, acquire a repository
  mutation lease, run git, or invoke scripts to learn availability. Add a typed read-only
  classification helper beside `DispatchHoldDetails`, mapping its known stable messages to
  repository-fenced, repository-owner-unknown, runner-unavailable, model-held, or ordinary-wait;
  preserve original detail as evidence and classify unknown text as Unknown. This string adapter
  is diagnostic, never mutation authority. Holds are only current for tasks still Queued in that
  queue stint, and are labelled last-observed; live runner/model/admission facts carry as-of times.
  Immediate global classification requires every relevant queued path to have an explicit fence
  or every available new-admission candidate to be blocked. Ordinary cap, normal known land,
  remote preparation/backoff, scoped writer and not-before waits are not global fences. An empty
  candidate set is not proof that all paths are blocked. Missing/stale quota or unavailable probes
  are Unknown, not quota refusal. A single unblocked or unknown path defeats the “all” assertion.
  Unknown observation never resolves a known open episode; resolution requires a successful
  observation showing the condition cleared. Record last successful scan time and observation
  errors for status inspection.

- **D-5 — Three small durable records own episodes and delivery, not Hangfire memory.** Add
  `ExpectationWatchState` (directive ID, config digest, scan clocks, next nudge time, optimistic
  token), `ExpectationEpisode` (directive, kind, stable subject key, first/last observed, resolved,
  evidence), and `ExpectationNudge` (ordinal, episode references/evidence snapshot, immutable
  body/digest, destination session+generation, attempt/baseline/receipt/answer fields, operator
  outbox state and audit links). Names may be regularized to repository conventions without
  changing this contract. Enforce one unresolved episode per `(DirectiveId, Kind, SubjectKey)`
  with a filtered unique index and one nudge ordinal per directive. Serialize changes by a
  short database transaction/row lock plus concurrency token; no external I/O inside it.
  Create the nudge and its task Check events/card discussion in the same transaction. A failed
  audit write means no outbound prompt. Use existing card comment semantics and BoardChanged
  publication; watchdog performs no tracker synchronization. Store sanitized evidence, IDs and
  short reasons, not full transcripts, credentials, raw broker errors or whole filesystem journals.
  CLI-generate migrations; indexes support open-episode and pending-delivery scans. Retain history
  under existing database backup policy; no pruning or retention daemon in this card.

- **D-6 — Deduplicate by condition identity, aggregate delivery per directive.** Subject keys:
  queue pipeline = directive + repository scope; dispatch fence = directive + affected scope;
  capacity = runner lane; missing/stalled task = task ID + dispatch stint; note = notification ID.
  Reason wording, elapsed minutes, queue length, holder-known/unknown flips and session restarts
  are evidence updates, not new episodes. Repeated successful clear observations (two consecutive
  scans, at least one minute apart) resolve; a recurrence then starts a new episode. A new
  dispatch stint is a new subject. One direct nudge per directive per ten-minute cooldown; a new
  higher-severity global fence can bypass it once, batching all due conditions. Repeat an unresolved
  acknowledged episode no sooner than 30 minutes after acknowledgement; an unanswered episode
  escalates once and receives operator reminders at most every 30 minutes, without repeated TUI
  typing. No one-nudge-per-task storm. Fence explains queue/capacity symptoms in the same body
  while retaining their individual observations. Cap inline text by the destination's existing
  UTF-8 safe ceiling; include all subject IDs in audit, at most three examples in the prompt and
  a status-route pointer. Never truncate the identity/ack marker or silently spill a different
  body after its digest has been frozen.

- **D-7 — A narrow direct Now sender with no recovery side effects.** Add an internal
  `SendExpectationNowAsync` entry point (prefer a partial file) to SessionMessageQueueService,
  exposed through an I/O seam to the watchdog. Reuse its session lock, readiness/ownership and
  generation checks, rules admission, backend ceilings, composer evidence, LF/paste/Enter and
  DeliverAsync/receipt matching. It does not test IsWorking as an idle gate, use
  AgentTaskLandNotificationService, enqueue a WhenIdle row, wait for a repository lease, or
  invoke HandleDeliveryFailureAsync/HandleTruncationAsync recovery. Add a narrow delivery option
  disabling DeliverAsync's existing Escape/overlay-recovery arm for this entry point; the default
  for all existing callers stays unchanged. Errors become typed
  delivery outcomes in the watchdog ledger; no kill, restart, compact, Escape into an unknown
  modal, cancel or retype. A working session may accept a submitted queued command; full
  UserPrompt or submitted QueuedUserPrompt is acceptable receipt, not QueueEnqueue/Dequeue.
  Reuse `HeldBackTypingBlocksTheComposer` with all pending rows treated as held back, and guard
  interrupted Sent rows awaiting reconciliation too. Also treat a previous unconfirmed watchdog
  attempt in the current generation as potentially occupying the composer. These checks happen
  under the same lock before any byte, including ordinary input paths following a watchdog
  attempt; do not protect only the watchdog from the queue and leave the queue free to append
  to the watchdog's stranded body. Clear this narrow hold only on full late receipt, proven
  empty composer or changed generation. A modal/unknown screen routes to the operator.
  Existing public Mode.Now behavior is unchanged. Reject raw `/input`, SendKeys, overlay
  queue reuse, or using the local slash-command poll method (it deliberately lacks receipt).

- **D-8 — Receipt, answer and resolution are different facts.** Before any TUI bytes, commit
  Attempting with frozen destination, accepted generation and transcript sequence floor under
  the session lock. Resolve the agent's current owned session for each new nudge; never reuse
  an old caller session merely because a task points to it. Recheck that binding before typing.
  A process crash after this commit is Uncertain: catch up the same transcript and search for
  the complete immutable body beyond the floor; never resend that nudge automatically. A known
  pre-input refusal still pages the operator instead of falling back to WhenIdle. Successful
  send return/screen change alone does not set ConfirmedAt. Require full submitted body,
  correct session/generation and sequence floor; with no observable baseline keep Unconfirmed
  rather than adopting an old matching line. Fresh prompts use a unique nudge ID.
  The prompt asks for an assistant reply containing a whole line
  `[expectation-ack:<full-nudge-guid>]` followed by the action or reason for waiting. Only a
  non-error AssistantText entry in the destination after the confirming prompt acknowledges;
  user/tool/thinking text, unrelated assistant activity, another ID/session or an earlier reply
  does not. ACK means answered, not repaired, and cannot reset the condition age. A generation
  change invalidates further typing, not already-confirmed historic receipt. Catch-up precedes
  absence judgments; a pull failure records Unknown and cannot postpone the operator deadline.

- **D-9 — Operator escalation is durable and independent.** For a missing/unsafe destination,
  failed/uncertain submission, or no ACK by AnswerMinutes, commit operator debt and an audit
  event/comment before calling `IAntiphonMessagingProducer.SendAsync` with a ChannelReply to the
  directive's current explicitly configured enabled OperatorChannelId. Freeze the resolved
  channel address on that attempt; never choose every alert sink or the last conversation by
  guess. Page includes nudge ID, expected/actual counts, condition age, delivery/answer state,
  relevant card/task IDs and safe next action. No secrets or provider sign-in contents.
  Record PublishedAt only on broker acceptance; it is not gateway or human confirmation.
  Broker failure leaves durable debt; retry after 1, 5, then 15 minutes, capped at 15. Suppress
  debt if a valid ACK/resolution arrives before publication. A crash after broker acceptance but
  before DB stamp may duplicate the same page: the current producer has no idempotency key.
  Preserve the same nudge ID/text and document at-least-once delivery rather than claiming exactly
  once or dropping the page. A missing/disabled channel remains visibly unsent with LastError;
  no silent success, alert-sink fallback or misrouting. Operational outage paging is not a card
  decision: if a human decision is needed, the orchestrator uses the existing Move/Reopen reason
  and attention workflow. Do not synthesize a decision card move here.

- **D-10 — Hangfire isolation, not another polling loop.** Register
  `antiphon:expectation-watchdog` on queue `expectations` with a dedicated one-worker Hangfire
  server in the main application; the existing worker consumes `default` only. Both are inside
  `Hangfire:ServerEnabled`; the new worker/job additionally requires feature Enabled. Preserve
  the default worker's count and jobs. A startup catch-up job uses the same sweep and durable
  dedupe; a restart need not wait to rediscover ten-minute-old queue debt. AutomaticRetry=0:
  next minute performs durable recovery. Per-directive database claims prevent overlap if two
  sweeps meet; Hangfire locks alone are not correctness. Service already-due operator debt before
  new direct sends/probes, so a slow terminal cannot postpone an existing page. Observation is bounded (page DB input,
  latest-event/aggregate queries, at most 100 rows per receipt catch-up page); each external probe
  has a cancellation budget and failures cannot skip other directives or operator debt. Start
  with 5 seconds per availability probe, 20 per direct send and 50 per job pass; continue fair
  persisted cursors next minute when exhausted. These are new-call budgets, not weakened shared
  delivery timeouts. Never detach a child operation or continue one after its owned cancellation.
  Process/DB outage remains outside this in-process watchdog's detection envelope.

- **D-11 — Coordinate recovery without duplicating it.** CARD-0648 owns dead/PID-reused journal
  classification, descendant/lock checks, recovery and restart integration. This watchdog never
  deletes a journal, calls `recover-repository-children.ps1 -Execute`, assumes unknown means dead,
  or grants `-ConfirmDescendantsExited`. Before CARD-0648 lands, name the recorded fence and
  suggest the manual script's read-only inspection followed by its documented human decision.
  After it lands, read its durable recovery outcome if exposed and re-observe the fence; a cleared
  fence resolves by D-6, ambiguous/live descendants remain a nudge. CARD-0648 must not ship a
  second competing caller/page loop; its diagnostics feed this observer. CARD-0641 continues to
  own ordinary note receipts/retries. Do not manually mark its notes delivered or resend them.
  Existing stall, boot/delivery and compaction policies keep their authority and thresholds.

## Five checks and their exact clocks

| Check | Positive evidence and age | Exclusions / clear evidence | Prompt and subject |
|---|---|---|---|
| V-2a Stalled pipeline | At least one scoped Queued task has been in its current queue stint >=10m AND no scoped Dispatched event in the preceding 10m. Use latest Created/Retried/requeue transition; repeated Held/HeldAged does not reset it. | No queued debt, younger stint, or recent scoped dispatch. Unrelated board dispatch does not reset. Long working tasks alone do not satisfy this check. | Oldest queued IDs, held-since and last-observed reason, last actual dispatch. One pipeline episode per repository scope. |
| V-2b Dispatch-wide fence | Nonempty current queued paths all have explicit fence evidence, or a ready backlog with every declared new-admission candidate blocked. First successful observation is due immediately. Repository fence can cover local and remote Worktree paths sharing that repository; a runner fence covers only that runner. | Any unblocked/unknown path; ordinary waits; quota on already admitted queue; stale quota. When only server2 is blocked, describe server2 scope, never all-board dispatch. | Enumerate scope, reason codes, evidence time and recovery/inspection action. Repo unknown-owner action is ownership inspection, not unlock. |
| V-2c Capacity below directive | Per runner, Dispatched+Working < target continuously for >=10m while eligible backlog candidates remain. Persist first deficit observation; configuration enable/change starts a fresh clock. | Target met, candidates exhausted, directive paused/expired, board archived. Queued deficits remain visible but explain existing held queue instead of telling the agent to duplicate queued work. | Expected/actual/queued/blocked by runner; candidate examples; ask to select an admissible next card/stage. |
| V-3a Silent in-flight | Dispatched with no report, no current usable session, and no transcript activity after max(dispatch time, last task-local activity) for >=10m. Null binding or confirmed terminal/missing session qualifies; runner unreachable is Unknown, not proof of no session. Also roll up current TaskProgressPolicy verdicts for live sessions using existing thresholds and catch-up. | Terminal task/report, new activity, proven live productive session, approved intentional blocked state. Refresh DB/session evidence immediately before minting nudge; dispatcher may have reconciled concurrently. | Task/session IDs and separate missing-session vs progress-stalled evidence. Suggest status/transcript/checkpoint inspection; no automatic cancel, escalation or stop. |
| V-3b Undelivered notes | Non-legacy caller-bound AgentTaskLandNotification not Confirmed/NotRequired, especially Queued/AwaitingReceipt/RetryPending/DestinationUnavailable, older than10m from CreatedAt; include parked or canceled-unconfirmed linked queue debt. Only notes to sessions proven owned by configured agent. | Confirmed, NotRequired, historical LegacyUnverified. A receipt arriving during catch-up excludes the note on refreshed observation. NextAttemptAt, retry count or Sent status never reset age. | Notification/task ID, kind, age, immutable destination and last error; independent watchdog send/page. Include notes to an older owned session, but route new nudge to current owned session. |

Check IDs denote verification groups, not claims that tests already exist. A stable observation
snapshot contains per-query as-of/unknown data. Do not use log scraping or generate false progress
from Watchdog Check events, ACKs, card comments, queue warning notes or Hangfire job activity.

## Implementation slices

Each round is independently committed/pushed after its test-only red checkpoint and after its
green implementation checkpoint. Stay on the task's own branch. Keep the feature disabled until
all rounds have passed Review. Bounds below include the approximately three-minute checkpoint
allowance; dispatch later rounds from the prior round's recorded commit via StartRef.

| Slice / round bound | Files and work | Tests / completion evidence |
|---|---|---|
| **S1, 85m**: directive and durable record | Add `server/Application/Settings/ExpectationWatchdogSettings.cs` and validator; `server/Domain/Entities/ExpectationWatchState.cs`, `ExpectationEpisode.cs`, `ExpectationNudge.cs`; EF mappings in `server/Infrastructure/Data/AppDbContext.cs`; CLI migration and snapshot under `server/Migrations/`; concrete `ExpectationLedger.cs` for atomic episode/nudge/audit writes. No worker/sends enabled. | `tests/Antiphon.Tests/Application/ExpectationDirectiveTests.cs`, `ExpectationLedgerTests.cs`; CP-1/2. Persist/reload with real PostgreSQL; uniqueness/rollback tests use independent contexts. |
| **S2, 90m**: queue, fences and capacity | Add `server/Application/Services/ExpectationSnapshotReader.cs`, `ExpectationWatchdogPolicy.cs` and typed observation DTOs; extend `DispatchHoldDetails.cs` with diagnostic classification only. Reuse scope, quota/model policy and runner registry as read-only seams. Add `ExpectationWatchdogService.cs` to record observations/cursors without delivery. | `ExpectationPipelineTests.cs` plus `ExpectationSnapshotTests.cs`; CP-3/4. Real DB scope fixtures, fake runner/policy inputs, controlled TimeProvider. |
| **S3, 80m**: silent tasks, notes, episodes | Complete snapshot/policy/service checks, existing stall-policy integration, fresh-read-before-nudge, config retirement, dedupe/cooldown, aggregate immutable prompt and atomic task/card audit. Add `ExpectationPromptFormatter.cs`. | `ExpectationDebtTests.cs`, `ExpectationEpisodeTests.cs`; CP-5/6. Real DB notes/queue/transcript joins and concurrent sweep race. |
| **S4, 90m**: independent safe direct delivery | Add `server/Application/Interfaces/IExpectationPromptSender.cs`; implement adapter to new internal `SessionMessageQueueService.Expectations.cs`; narrowly share preflight/receipt/composer protection in `SessionMessageQueueService.cs`. Add `ExpectationNudgeDeliveryService.cs` for claim, immutable baseline, attempt, late receipt and no replay after uncertainty. | `ExpectationDirectDeliveryTests.cs`, `ExpectationReceiptTests.cs`; CP-7/8/9. Exercise actual queue service through controlled runtime I/O; do not only assert a mock adapter was called. Extend common composer admission to honor uncertain watchdog input. |
| **S5, 85m**: response and operator debt | Add `ExpectationResponseMatcher.cs`, `ExpectationOperatorDeliveryService.cs` using existing messaging producer and ChatChannel lookup. Durable broker attempts/backoff and audit updates through ledger. Add read-only `server/Api/Endpoints/ExpectationWatchdogEndpoints.cs` with scoped status DTOs and normal endpoint authorization; no ACK/dispatch mutation route. | `ExpectationEscalationTests.cs`, `ExpectationStatusTests.cs`; CP-10/11. Real DB restart/cancellation/ACK cases, recording/failing fake producer, endpoint read with job/runner disabled. |
| **S6, 75m**: Hangfire wiring and complete scenarios | Add `server/Infrastructure/Agents/ExpectationWatchdogJob.cs`; change `HangfireConfiguration.cs`, `server/Program.cs` and example inert `server/appsettings.json`; update `docs/orchestration-loop.md`, `docs/ops-http.md`, `docs/antiphon-api.md`, `docs/session-runtime-invariants.md`, `docs/telegram.md`, and a short watchdog/ACK paragraph in `server/Bundles/orchestrator.md`. Document enable/disable, as-of/failures, ledger/status route, manual recovery handoff, broker acceptance limit. | `tests/Antiphon.Tests/Infrastructure/ExpectationWatchdogJobTests.cs`, `tests/Antiphon.Tests/Application/ExpectationWatchdogScenarioTests.cs`; CP-12/13 and existing Hangfire startup safety. Five-hour incident reproduced with virtual time, not a five-hour test. |

Proposed read-only route: `GET /api/expectation-watchdog?boardId=<guid>` (boardId required),
return configuration identity/validation, last successful scan, lane counts, open episodes,
recent nudges with receipt/answer/operator states and audit pointers. Use existing task-token
scope policy; wrong/unauthorized board must not disclose channels, transcripts or other boards.
No route changes directive or authorizes a send. Endpoint maps belong in the API owner doc.

## Verification design

Plan is documentation only: no build/test was run or claimed here. Code uses the closed list
below, overriding a generic full Unit/assembly sweep. PostgreSQL is required for persistence,
locking and restart behavior; do not replace it with EF InMemory. Pure policy/time/format tests
are Unit; persistence, session delivery, endpoint and job-registration tests are Integration.
Each fixture deletes only its own rows and uses independent IDs; any concurrent DB fixture uses
the test isolation rules in `docs/testing-and-build.md`. No production runner, broker or paid
model. Existing `AntiphonWebAppFactory`/ProductionRunnerGuard disables Hangfire on real Program
boots; isolated registration tests inspect DI/storage without running census/native jobs.

The native delivery contract is reused, not reimplemented. Controlled runtime fixtures must
observe actual normalized paste and separate Enter, return transcript rows via catch-up, and
count stop/restart calls. Fake a paused WhenIdle reconciler and an occupied repository lease;
the watchdog service has neither as a prerequisite. No raw-input bypass is acceptable. If Code
changes native typing itself, that is scope expansion: state why and amend the manifest with the
necessary named native canaries before running them. Do not silently omit that coverage to meet
the round estimate.

### Coverage and explicit test roster

Create exactly these ordinary methods initially, without Arguments expansion. Internal matrix
cases do not inflate Min counts. Test names are the execution contract; add independent assertions
for negative variants inside the named method or deliberately amend the plan with new methods.

| Group | Classes and exact proposed methods | Required assertions |
|---|---|---|
| **V-1**, 8 executions | `ExpectationDirectiveTests`: `C650_Rejects_invalid_and_duplicate_directives`, `C650_Validates_scope_and_recipient_references`, `C650_Config_digest_changes_only_for_semantic_edits`, `C650_Disabled_or_expired_directive_has_no_effects`. `ExpectationLedgerTests`: `C650_Persists_episode_and_nudge_with_audit`, `C650_Concurrent_create_has_one_open_episode`, `C650_Audit_failure_rolls_back_nudge`, `C650_Reload_keeps_clocks_and_attempt_identity`. | Reject wrong-board audit card/agent, pool agent, duplicate runner/board, invalid target; valid local+server2 allowed. Durable exact body/audit identity; no send before committed audit. |
| **V-2**, 12 executions | `ExpectationPipelineTests`: `C650_Queue_age_and_dispatch_progress_use_exact_boundaries`, `C650_Requeue_resets_age_but_Held_does_not`, `C650_All_paths_fenced_is_immediate`, `C650_Partial_or_unknown_path_is_not_global_fence`, `C650_Quota_fences_new_admission_only`, `C650_Capacity_deficit_requires_continuous_ready_work`, `C650_Ordinary_waits_are_not_global_fences`, `C650_Unknown_observation_does_not_resolve`. `ExpectationSnapshotTests`: `C650_Scope_excludes_other_boards_and_ambiguous_callers`, `C650_Counts_runner_lanes_and_excludes_specialists`, `C650_Backlog_candidates_exclude_owned_held_unrated_and_open_work`, `C650_Hold_classifier_matches_dispatcher_messages`. | At N-1 tick no condition, at N condition. A dispatch on another board is not progress. Local healthy/server2 down negative; common repository fence positive. Fresh all-candidate quota vs missing/stale sample. Known land/cap/pin/remote preparation negative. |
| **V-3**, 10 executions | `ExpectationDebtTests`: `C650_Dispatched_without_session_and_report_is_detected`, `C650_Unknown_runner_is_not_missing_session`, `C650_Fresh_stall_policy_verdict_is_rolled_up`, `C650_Note_age_includes_parked_and_retry_debt`, `C650_Receipt_catchup_and_concurrent_settlement_withhold_stale_nudge`. `ExpectationEpisodeTests`: `C650_Repeated_sweeps_and_reason_flips_emit_one_nudge`, `C650_Concurrent_sweeps_commit_one_audit_and_send_claim`, `C650_Cooldown_recurrence_and_config_change_keep_correct_clocks`, `C650_Fence_batches_queue_and_capacity_subjects`, `C650_Unknown_scan_preserves_episode_and_fair_cursor`. | Null/terminal/missing session positive; late transcript/report negative. Existing stall productive-file/commit evidence suppresses false stall. Terminal/legacy notes excluded. Recreate service between scans. Two clear scans needed; ten-minute cooldown, one urgent bypass, thirty-minute reminder; all subject audits present. |
| **V-4**, 12 executions | `ExpectationDirectDeliveryTests`: `C650_Working_caller_receives_prompt_without_note_or_lease_progress`, `C650_Input_uses_normalized_paste_and_separate_enter`, `C650_Unsafe_composer_or_modal_sends_no_bytes`, `C650_Delivery_failure_never_stops_or_restarts_session`, `C650_Destination_change_prevents_typing`, `C650_Uncertain_watchdog_body_blocks_later_ordinary_input`. `ExpectationReceiptTests`: `C650_Complete_submitted_prompt_confirms_both_supported_kinds`, `C650_Rejects_wrong_session_floor_partial_and_housekeeping_receipts`, `C650_Crash_after_attempt_commit_never_retypes`, `C650_Late_receipt_recovers_uncertain_attempt`, `C650_No_observable_baseline_never_invents_confirmation`, `C650_New_session_does_not_adopt_old_attempt`. | Run real SessionMessageQueueService with virtual runtime; full body delivered while IsWorking=true and ordinary note stuck. Test failure outcomes including truncation, no transcript, timeout, unreachable, modal, parked Pending and interrupted Sent. Zero kill/compact/retype and correct generation; repeated ordinary flush cannot append to uncertain watchdog input. |
| **V-5**, 10 executions | `ExpectationEscalationTests`: `C650_Unanswered_nudge_pages_exact_channel_at_deadline`, `C650_Only_post_receipt_assistant_ack_suppresses_page`, `C650_Unavailable_or_unsafe_recipient_pages_immediately`, `C650_Broker_failure_survives_restart_and_retries_same_identity`, `C650_Published_is_not_human_confirmation`, `C650_Concurrent_escalation_and_ack_do_not_duplicate_normal_send`, `C650_Disabled_channel_retains_visible_debt`, `C650_Reminders_are_bounded_and_ack_does_not_resolve_condition`. `ExpectationStatusTests`: `C650_Status_shows_scan_delivery_answer_and_audit_states`, `C650_Status_requires_authorized_board_scope`. | No dependence on note reconciler or alert digest. Wrong/old/user/tool/error/partial ACK negative. Simulate post-publish crash: at-least-once duplicate retains same ID. ACK after immediate escalation cannot undo publication. Successful publication does not imply gateway delivery. |
| **V-6**, 8 executions | `ExpectationWatchdogJobTests`: `C650_Registers_minute_job_on_dedicated_queue`, `C650_Disabled_host_starts_no_watchdog_worker`, `C650_Fresh_host_recovers_durable_due_work`, `C650_Probe_failure_does_not_starve_other_directive_or_page`, `C650_Default_job_occupancy_does_not_block_watchdog_queue`. `ExpectationWatchdogScenarioTests`: `C650_Overnight_fence_nudges_then_pages_without_task_notes`, `C650_Recovered_journal_fence_resolves_without_watchdog_mutation`, `C650_Directive_replenishment_and_late_note_receipt_are_independent`. | Real DI job calls controlled services, real persistent state, virtual UTC. Simulate 3 local+3 server2, three old held tasks, normal WhenIdle consumer permanently blocked; one immediate aggregate nudge and one deadline page. Record recovery evidence then clear fence. Zero recovery script/git/kill/spawn/card-move calls. |

Regressions: **R-1** existing `DispatchHeldAttentionTests` (8 ordinary executions) retains
hold visibility; **R-2** existing `SessionMessageQueueServiceTests` exact methods
`Send_now_delivers_immediately_and_does_not_queue`,
`Delivery_sends_body_then_a_separate_CR_not_one_combined_write`,
`Multiline_delivery_is_wrapped_in_bracketed_paste`,
`When_idle_message_is_held_while_the_agent_is_working` (4 executions) retain public queue
semantics; **R-3** `HangfireStartupSafetyTests` (9 ordinary executions) retains worker-disabled
tests, existing recurring jobs and dashboard access. Counts are planned roster floors verified
against current source; record actual TUnit executions, never assertion-loop counts.

### Guard inventory for later method-scoped positive controls

Code proves red-first outcomes at the checkpoints. Post-land Mutation, if commissioned, owns
these compiling fault injections; it is not hidden inside the three-minute ordinary allowance.
Each PC selects `/*/*/ClassName/ExactMethod`, expects the named assertion failure, restores,
then runs the same method green. Fixture/build errors or zero executions are not red.

| PC | Guard and compiling bypass | Exact test; expected red |
|---|---|---|
| PC-1 | Remove board/caller scope filter. | `ExpectationSnapshotTests.C650_Scope_excludes_other_boards_and_ambiguous_callers`; foreign task counted. |
| PC-2 | Use Any instead of All for fenced paths. | `ExpectationPipelineTests.C650_Partial_or_unknown_path_is_not_global_fence`; false global fence. |
| PC-3 | Credit Queued as running capacity. | `ExpectationSnapshotTests.C650_Counts_runner_lanes_and_excludes_specialists`; wrong lane count. |
| PC-4 | Reset episode FirstObservedAt each tick. | `ExpectationEpisodeTests.C650_Cooldown_recurrence_and_config_change_keep_correct_clocks`; boundary nudge absent. |
| PC-5 | Remove dedupe/serialized re-read when creating nudge. | `ExpectationEpisodeTests.C650_Concurrent_sweeps_commit_one_audit_and_send_claim`; duplicate rows/claims. |
| PC-6 | Commit nudge before audit transaction. | `ExpectationLedgerTests.C650_Audit_failure_rolls_back_nudge`; orphan nudge survives. |
| PC-7 | Gate direct send on IsWorking or route through WhenIdle. | `ExpectationDirectDeliveryTests.C650_Working_caller_receives_prompt_without_note_or_lease_progress`; expected prompt absent. |
| PC-8 | Bypass composer hold in watchdog preflight. | `ExpectationDirectDeliveryTests.C650_Unsafe_composer_or_modal_sends_no_bytes`; input bytes observed. |
| PC-9 | Ignore uncertain watchdog body in ordinary admission. | `ExpectationDirectDeliveryTests.C650_Uncertain_watchdog_body_blocks_later_ordinary_input`; appended input observed. |
| PC-10 | Call generic failure recovery from watchdog send. | `ExpectationDirectDeliveryTests.C650_Delivery_failure_never_stops_or_restarts_session`; stop/restart count positive. |
| PC-11 | Drop current-owner/generation recheck. | `ExpectationDirectDeliveryTests.C650_Destination_change_prevents_typing`; old session receives bytes. |
| PC-12 | Relax receipt to ID/header-only. | `ExpectationReceiptTests.C650_Rejects_wrong_session_floor_partial_and_housekeeping_receipts`; partial record confirms. |
| PC-13 | Remove sequence floor. | Same exact PC-12 method; old complete record confirms. Separate mutation/evidence. |
| PC-14 | Remove destination restriction. | Same exact PC-12 method; other-session complete record confirms. Separate mutation/evidence. |
| PC-15 | Reset Attempting on restart to sendable. | `ExpectationReceiptTests.C650_Crash_after_attempt_commit_never_retypes`; second body typed. |
| PC-16 | Accept any assistant/user occurrence of ACK without receipt/floor. | `ExpectationEscalationTests.C650_Only_post_receipt_assistant_ack_suppresses_page`; false ACK suppresses page. |
| PC-17 | Treat caught broker exception as Published. | `ExpectationEscalationTests.C650_Broker_failure_survives_restart_and_retries_same_identity`; debt/retry lost. |
| PC-18 | Start watchdog worker outside ServerEnabled guard. | `ExpectationWatchdogJobTests.C650_Disabled_host_starts_no_watchdog_worker`; background server registered. |

These 18 PCs cover the principal independently bypassable ownership/delivery/durability guards.
The ordinary boundary matrices additionally pin time, kind, input encoding, unknown data, and
no unintended recovery. Do not claim a mutation-clean result from ordinary V/R alone.

### Cost

Ordinary checkpoint estimate: **18.3 minutes** (six approximately three-minute rounds), plus
authoring within **505 minutes total** (85+90+80+90+85+75). These are planning estimates, not
measurements. Cold restore, migration setup or shared-host load may exceed them. Report elapsed
time, counts and reruns; never tune timeouts/assertions or omit a row to fit. The scoped regression
rows reuse an unchanged build. Positive-control estimate is a separate **54 minutes** for 18
method red/restore/green cycles at approximately three minutes each, subject to measured native/
fixture cost. No full assembly, paid-agent canary or production send is authorized by a checkpoint.

### Checkpoints

`Sx-tests` is a committed/pushed test-only slice plus inert, source-compatible scaffolding needed
to compile new services/schema. The red method must call the future production seam and fail
its behavioral assertion with baseline/no-op behavior; a deliberately thrown test assertion,
self-comparison, compile error or missing fixture is not red. Checkpoint red exit 1 is expected
and reported as such. Green rows have zero failed/skipped and include every listed method.
For negative guards not demonstrable against a no-op baseline, retain the explicit PC above.

Use `scripts/run-checkpoint.ps1`, fresh ResultsRoot per CP/attempt, table Min as `-MinExecuted`,
and `-Expect` for each intended class or exact selected method. Commit before every run and
record verified SHA. One build and one filter per row; `Build=CP-n` means `-NoBuild` against that
unchanged After commit. Table pipes are Markdown-escaped; pass literal `|`, not `\|`, to TUnit.
All class operands have trailing wildcards (CARD-0403). New scaffolds are not a delivered slice
until the green implementation commit exists. A fix reruns only its failing CP with the rerun
count/reason recorded; every additional build/test needs a stated reason.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-tests | `tests/Antiphon.Tests -> bin-c650-r1/` | directive-red | `/*/*/ExpectationLedgerTests*/C650_Persists_episode_and_nudge_with_audit` | V-1 | 1 executed, missing durable nudge/audit assertion fails | 1 | 1.1 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c650-r1/` | directive-green | `/*/*/(ExpectationDirectiveTests*)\|(ExpectationLedgerTests*)/*` | V-1 | all 8, 0 failed/skipped | 8 | 1.9 |
| CP-3 | S2-tests | `tests/Antiphon.Tests -> bin-c650-r2/` | pipeline-red | `/*/*/ExpectationPipelineTests*/C650_All_paths_fenced_is_immediate` | V-2 | 1 executed, missing immediate condition assertion fails | 1 | 1.1 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c650-r2/` | pipeline-green | `/*/*/(ExpectationPipelineTests*)\|(ExpectationSnapshotTests*)\|(DispatchHeldAttentionTests*)/*` | V-2, R-1 | all 12 new + 8 existing, 0 failed/skipped | 20 | 1.9 |
| CP-5 | S3-tests | `tests/Antiphon.Tests -> bin-c650-r3/` | debt-red | `/*/*/ExpectationDebtTests*/(C650_Dispatched_without_session_and_report_is_detected*)\|(C650_Note_age_includes_parked_and_retry_debt*)` | V-3 | 2 executed, missing-condition assertions fail | 2 | 1.1 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c650-r3/` | debt-green | `/*/*/(ExpectationDebtTests*)\|(ExpectationEpisodeTests*)/*` | V-3 | all 10, 0 failed/skipped | 10 | 1.9 |
| CP-7 | S4-tests | `tests/Antiphon.Tests -> bin-c650-r4/` | direct-red | `/*/*/ExpectationDirectDeliveryTests*/C650_Working_caller_receives_prompt_without_note_or_lease_progress` | V-4 | 1 executed, missing submitted prompt assertion fails | 1 | 1.1 |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c650-r4/` | direct-green | `/*/*/(ExpectationDirectDeliveryTests*)\|(ExpectationReceiptTests*)/*` | V-4 | all 12, 0 failed/skipped | 12 | 1.6 |
| CP-9 | S4 | CP-8 | ordinary-input | `/*/*/SessionMessageQueueServiceTests*/(Send_now_delivers_immediately_and_does_not_queue*)\|(Delivery_sends_body_then_a_separate_CR_not_one_combined_write*)\|(Multiline_delivery_is_wrapped_in_bracketed_paste*)\|(When_idle_message_is_held_while_the_agent_is_working*)` | R-2 | all 4, 0 failed/skipped | 4 | 0.3 |
| CP-10 | S5-tests | `tests/Antiphon.Tests -> bin-c650-r5/` | operator-red | `/*/*/ExpectationEscalationTests*/C650_Unanswered_nudge_pages_exact_channel_at_deadline` | V-5 | 1 executed, missing exact-channel page assertion fails | 1 | 1.1 |
| CP-11 | S5 | `tests/Antiphon.Tests -> bin-c650-r5/` | operator-green | `/*/*/(ExpectationEscalationTests*)\|(ExpectationStatusTests*)/*` | V-5 | all 10, 0 failed/skipped | 10 | 1.9 |
| CP-12 | S6-tests | `tests/Antiphon.Tests -> bin-c650-r6/` | job-red | `/*/*/ExpectationWatchdogJobTests*/C650_Registers_minute_job_on_dedicated_queue` | V-6 | 1 executed, missing recurring registration assertion fails | 1 | 1.1 |
| CP-13 | S6 | `tests/Antiphon.Tests -> bin-c650-r6/` | scenario-green | `/*/*/(ExpectationWatchdogJobTests*)\|(ExpectationWatchdogScenarioTests*)\|(HangfireStartupSafetyTests*)/*` | V-6, R-3 | all 8 new + 9 existing, 0 failed/skipped | 17 | 2.2 |

Example CP-4 invocation:

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-4 -Project tests/Antiphon.Tests -OutputPath bin-c650-r2/ -Filter '/*/*/(ExpectationPipelineTests*)|(ExpectationSnapshotTests*)|(DispatchHeldAttentionTests*)/*' -MinExecuted 20 -Expect ExpectationPipelineTests,ExpectationSnapshotTests,DispatchHeldAttentionTests -ResultsRoot .antiphon/c650-cp4-attempt1
```

Planned green floor **81 executions** (8+20+10+12+4+10+17); baseline-red floor **7**.
Await every command in the foreground; no source edits while a run is active. Report CP, SHA,
build status, filter, executed/passed/failed/skipped, TRX, elapsed time and reruns. Verify an
inherited failure at the base with that exact test before attributing it. Keep an exact inventory
of producer-owned bin-c650-rN directories, validate their resolved absolute paths remain inside
the Code worktree, then remove only those directories before settlement. No daemon restart.

## Acceptance, rollout and handoff

Ordinary Review must cover all rounds, especially separate prompt/receipt/answer states,
durable audit-before-send, no recovery side effects, scope, race/uncertainty tests and startup
test isolation. Land through the normal Review/landing workflow. Ship disabled; enable on the
main instance only after resolving a real standing agent, board/audit card, allowed candidates
and operator channel in configuration. Do not mark the production stall fixed merely because
this plan or the code was published. Activation follows the canonical main-checkout restart
and `/api/version` SHA check.

Acceptance uses an explicitly commissioned controlled directive/test conversation: observe a
minute job, a complete busy-session prompt, an ACK suppressing a page, an unanswered nudge
publishing to the configured conversation, and durable audit/status after a restart. Do not
create a real dead journal, exhaust quota or kill working production tasks for the canary.
The deterministic overnight scenario is the ordinary automated evidence; live channel delivery
is a separate activation observation, not claimed by broker acceptance or Plan.

Disable/expire the directive to stop new observations/sends; retain evidence. Global feature off
stops the new Hangfire worker/job. On re-enable, reread conditions and cancel stale unsent debt
before sending anything. Read-only status remains available while disabled. A missing channel,
recipient or CARD-0648 recovery implementation is diagnosable, never an excuse to fall back to
the failed note path. No unresolved product decision blocks S1.

Next: **Code**, S1 then the bounded rounds above. Commission with the immutable plan commit:
`checkpoints: docs/superpowers/plans/2026-09-24-card-0650-expectation-watchdog-plan.md@<full-plan-sha> section "### Checkpoints"`.
