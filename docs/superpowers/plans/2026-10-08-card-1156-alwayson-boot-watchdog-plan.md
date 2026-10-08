# CARD-1156: taskless AlwaysOn boot watchdog

**Operator decision required before Code: recommend option A, detection only.**
This removes the documented CARD-0312 boot-driven restart/latch policy and keeps
the CARD-0079-only Working-stop rule. D-1 and D-2 explicitly resolve the policy
choice and conflicting instructions about unknown evidence. The plan is complete
under the defaults below; it is not authorization to implement those defaults.
Next: **decide**, then **test-design**. Verification is a separate stage in this
brief; the proposed verification design below supplies its scope and closed
checkpoint list, not executed evidence.

Date: 2026-10-08. Assigned branch: `feat/card-task-82ca0355`.
Source baseline: **2a0f8407486f37ffb3588c221c42c77d7d1fbb70**, also the fetched
`origin/master` on this dispatch. No source files or tests were changed during
planning. Read the live CARD-1156 and CARD-1151 descriptions with `card.ps1 get`.
Read the CARD-1151 plan and its test design, especially **Q-1 decided: option B**;
that decision supersedes the earlier safe-absence/retry proposal.

## Ground truth

Coordinates below are at the baseline. Services are under
`server/Application/Services/`; application tests are under
`tests/Antiphon.Tests/Application/` unless qualified otherwise.

| Card assumption / question | What the code does | Consequence |
|---|---|---|
| Taskless AlwaysOn boot silence can stop a session. | `BootReplyWatchdogService.RaiseAsync` (`:192-315`) loads the owner, creates/updates supervision state, saves the incident and disarms the watch, then calls `StopHungStandingSessionAsync` when AlwaysOn and not latched. That method calls `IDelegateSessionStopper.KillAsync` (`:323-339`). | Premise confirmed by source; this is a real stop interface, not a warning named “kill.” |
| The stopper might independently refuse Working. | DI maps it to `AgentSessionService` (`server/Program.cs:383`). `KillAsync` / `KillOnAsync` (`AgentSessionService.cs:1501-1640`) persists Stopping/SystemRequest and calls runtime Kill; there is no Working guard. Unconfirmed exit can retain a deferred kill intent. | Guarding only the runner's direct call count would miss both the service stop and its durable consequences. |
| Running means Working. | `SessionMessageQueueService.IsWorkingAsync:5087` delegates to `TranscriptWorkingStateQuery`. A real UserPrompt after the last turn boundary makes Working true; SessionStatus is independent. | A live prompt-only fixture is Working even without any AgentTask. Prove this with the real query before and after the sweep. |
| The watchdog checks that it is not Working. | No Working query, runner inventory, generation-conditioned stop, workspace safety check, or CARD-0079 predicate occurs on its stop path. | It can stop a Working AlwaysOn session. It violates the stated sole-exception rule. |
| It runs eight minutes after launch. | `BootReplyWatch.TryArmAsync:238-278` arms from the latest real session-scope prompt at/after `LaunchClock = max(StartedAt, LaunchResumedAt)`. Due is prompt time + `BootModelWaitDeadlineMinutes` (default 8). Equality is overdue (`Evaluate:152`). | Eight minutes is prompt-relative, not creation-relative. An already old prompt can be judged on the first sweep after restart. |
| A taskless session is required. | The exclusion at `BootReplyWatchdogService:177-189` recognizes only Dispatched and Working task rows bound to the session. Queued and Blocked owners do not exclude it. | “Taskless” is currently shorthand for “no Dispatched/Working owner,” not absence of every open task. D-3 tightens this without touching task recovery. |
| Only an accepted UserPrompt can arm this watch. | `BootReplyWatch.LoadBootTurnAsync:170-217` exposes both the latest real prompt and the latest accepted prompt. The session watch still uses the former, including QueuedUserPrompt. CARD-1151's task policy uses AcceptedSequence/AcceptedAt instead. | Do not accidentally apply CARD-1151's task clock to this session watch. Preserve the distinction and describe queued evidence honestly. |
| Every row proves a reply. | AssistantText, Thinking, ToolCall, ToolResult and TurnEnd answer; title, queue bookkeeping and synthetic restart boundaries do not. Unsupported transcript verification does not arm. Grok rules Pending/Failed withholds boot facts. | Preserve the existing predicate and provider gates. A redraw is not progress evidence. |
| A failed transcript pull prevents stopping. | `EvaluateAsync:143-170` pulls, then reevaluates stored rows. Runtime is optional; `AgentSessionRuntime.CatchUpTranscriptAsync:706-719` returns false both for no new rows and swallowed non-cancellation faults. | Stale stored silence can reach the stop. The bool is not freshness/completeness proof. |
| It stops at most twice per session. | `MaxProbeDrivenRestarts = 2` applies to an agent's shared `ConsecutiveFailures`. If already latched, or current failures + 1 > 2, it sets/keeps `LivenessLatchedAt`, emits Error and does not stop. Otherwise it increments failures, clears NextRestartAt, saves, then stops. | It is a shared counter, not a dedicated two-attempt session counter. Other confirmed failures can consume the budget. |
| Stopping immediately restarts it. | The hosted service calls the supervisor before the boot sweep. On a later tick the supervisor's no-live-session branch schedules capped backoff; a subsequent due tick calls StartAsync with Fresh:false. Live sessions, suspension, liveness/continuity holds and launch ownership can prevent this. | A stop is not evidence that restart succeeded. Default tick is 10 seconds, backoff base 5 seconds, healthy reset 10 minutes; RPC time and earlier sweep faults can delay work. |
| The latch recovers automatically. | `AgentSupervisorService:146` refuses a latched agent. Human Start clears the latch (`AgentControlService.ClearSupervisionLatchAsync:1214`). | Preserve old latches on upgrade; do not silently restart them or erase unrelated failure history. |
| Boot silence is CARD-0079. | CARD-0079 requires the typed AlwaysOn Claude Check seat, explicit automatic CompactBoundary plus synthetic continuation, configured silence (default 10 minutes), fresh matching evidence, committed intent and conditional stop, followed by strict same-conversation resume and useful Check/caller receipt. Boot watch checks none of these. | Renaming its stop or calling the CARD-0079 coordinator does not make it that exception. |
| Every unattended launch gets a probe. | `AgentSessionService.TryEnqueueBootProbeAsync:2904-2962` only queues a single probe when enabled, nothing was typed/queued, the provider verifies delivery and the owner is unattended. Typed Check seats are excluded. | Preserve launch-only probe scope. This plan adds no periodic probe, prompt, Enter, Ctrl+C or re-delivery. |
| Detection is already durably visible. | `AttentionService.BuildBootReplyMissingItemsAsync:2200-2284` requires a saved bootSeq incident within the 24-hour RecencyWindow, a live session and no subsequent model row. It projects the saved message/severity. | Save failure yields no row; sustained silence can disappear after 24 hours. Incidents also have 30-day/500-per-agent pruning. Derived current attention and active dedup retention are needed. |
| The existing tests forbid a stop. | `BootReplyWatchdogTests.a_standing_agent_goes_through_the_existing_restart_ladder` requires `[SessionId]` in Stopper.Killed and failures = 1. Another test requires the third failure to latch. The core incident test requires a cleared watch. | Reverse these three contracts explicitly; see V-1. The existing fixture does not itself assert Working or run the real stopper. |
| CARD-1151 S4/S5 is already wholly present. | At this baseline `BootStallAttentionTests` and `BootStallDocumentationTests` still contain S4 pending bodies; several BootStallDetectionTests S5 bodies are pending. Live pipeline read names S4 repair and S5 review-ready work. | Do not treat the brief's “S1-S4 landed” summary as code truth. Integrate the actual landed S4/S5 results before running their regression rows. |

The exact existing stop conjunction is: supervision's hosted loop enabled;
positive boot deadline; session status Starting/Running/Stopping; watch armed or
successfully re-derived; overdue after the optional pull; no bound
Dispatched/Working task; no existing LivenessProbeFailed incident for this
`(sessionId, bootSeq)`; an extant resolved AlwaysOn owner; no existing liveness
latch and `ConsecutiveFailures + 1 <= 2`; the SaveChanges succeeds; a stopper is
registered. It then **requests** a stop. Missing stopper is a no-op; stop exceptions
are logged. A later repeat does not retry that stop while its incident remains.
The sweep can re-arm a disarmed silent watch, but the incident check suppresses
the same episode. A failed save prevents that iteration's stop by throwing out
of RaiseAsync, not by a safety policy.

## Options

| Option | Behavior and policy | Risk of an unnoticed hung boot | Risk to healthy work / cost | Recommendation |
|---|---|---|---|---|
| **A. Detection only** | Remove this watchdog's stop, failure-counter and latch authority. Derive one attention row from current boot facts; Warning at boot due, Error at operator due. CARD-0079 stays independent. | A truly hung process keeps its seat until explicit action or genuine exit. Current-state attention survives optional telemetry failure, age and incident pruning; operator response is not guaranteed. | No boot-silence kill, including unknown evidence. A stale transcript can raise a false warning, which resolves on catch-up. Small server-only change. | **Recommended.** Same policy direction as CARD-1151 option B; no new stop protocol. |
| B. Retain a stop only with positive non-Working proof | Require authoritative matching session/generation/owner, fresh complete non-Working evidence, safe pending-input state and an atomic runner-side condition rechecked at the stop. Unknown keeps the session. | Positive safe cases recover automatically; unobservable cases still need attention. | A server-side IsWorking == false then generic Kill is a race, not a safe implementation. Requires a suitable conditional runner operation and protocol/compatibility verification; the existing compaction operation is specific to its own proof. | Reject for this card's smallest fix. Commission separately if operator wants it. |
| C. Broaden CARD-0079 semantics to cover silent boot | Define a second admitted stop episode with fresh proof, conditional stop, strict continuation and receipt obligations; update the documented sole-exception rule. | More automatic recovery, still no guarantee while runner/transcript is unavailable. | Boot silence does not prove the special compaction failure. Wrong-stop risk remains unless a new predicate/protocol is independently qualified. Wider change than B. | Reject. It is a new safety exception, not a reuse justified by the present evidence. |
| D. Keep code; amend AGENTS.md and runtime rule | Explicitly permit up to two boot-driven stops by the current shared counter, with its existing stale-read/generic-kill behavior. | Fast boot intervention, but failed kills, latches and disappearing attention can still leave hangs unnoticed. | Preserves the demonstrated Working-stop and late-reply race. Making docs agree does not remove the defect. | Reject; operator acceptance of that risk would be required. |

## Decisions

### D-1. Default: A, no automatic stop from the boot watchdog

Delete `StopHungStandingSessionAsync` and the watchdog's supervision-state writes,
including failure increments, NextRestartAt changes and creation of a liveness
latch. Do not leave a catch/finally stop, a “safe false Working” shortcut, a
conditional compaction call, a restart request, or an automatic requeue.
The automatic-stop whitelist for **this mechanism is empty**. This also protects
misclassified, stale and newly task-bound sessions: no lost emission guard can
turn telemetry into destructive authority.

This retires CARD-0312's documented intervention contract while enforcing the
AGENTS.md CARD-0079 rule. Approval concerns the tradeoff between retaining a hung
seat and risking a false stop, not permission to author this plan. Genuine process
exit, manual Stop/Start and the independent CARD-0079 coordinator retain their
existing policies. Do not clear historical latches/counters on upgrade.

### D-2. Default: unknown keeps the session; no literal legacy-kill fallback

The live card says “anything unknown keeps the session.” The brief also says
“anything unknown keeps today's behaviour.” Today a failed pull can still stop
the session, so these cannot both mean preservation of the current stop path.
Default here follows the card's safety outcome: unknown admits no **new**
notification transition or recovery, preserves current ownership/input/state,
and never restores the removed stop. Existing non-destructive watch/legacy
attention behavior outside the new emission whitelist remains available.

The operator must accept this interpretation with A, or select a different option.
Do not let Code resolve that contradiction by silently shipping a fallback kill.

### D-3. Positive emission whitelist, separate from the no-stop invariant

Add a small `StandingBootWatchPolicy` with `None`, `Detected`, `NeedsOperator`
notification stages and a read-only `StandingBootWatchObservation` helper.
Do not add a public controller flag or an interface for a pure service.
All of the following must be positively observed for the **new taskless AlwaysOn
episode** to be emitted/projected:

1. BootModelWaitDeadlineMinutes > 0, provider DeliveryVerification Supported,
   and GrokRulesState None or Ready (the existing exclusions are preserved).
2. A loaded live session with EndedAt null, valid accepted StartedAt and launch
   clock, and its unchanged ID/generation/runner binding at the writer recheck.
3. Exactly one current persistent-pointer owner, AlwaysOn true. A conflicting
   non-null StandingAgentId, missing owner or ambiguous pointer is unknown.
   Historical task fallback may retain legacy standalone diagnostics; it grants
   no new standing-owner attribution.
4. No Queued, Dispatched, Working **or Blocked** task owns that session. Terminal
   task history does not exclude it. Do not hand a Blocked/Queued owner's session
   to the standing watchdog merely because CARD-1151 only handles active tasks.
5. The current session-scope BootReplyWatch predicate positively identifies the
   same latest real prompt and prompt time on this launch, with no qualifying
   model reply. Persisted watch columns alone are insufficient if that prompt
   disappeared or the generation changed. Keep the cheap answered-session gate.
6. The relevant stage is due on the prompt clock; the writer rechecks episode
   identity, current owner/task exclusion and reply evidence before inserting.

An unknown required field/read declines emission and leaves the session alone.
Working is deliberately **not an admission input**: true, false and unavailable
all have the same no-stop outcome. Runner listing/absence and workspace silence
are also not safety proofs or new probes in this design.

Keep session-scope UserPrompt/QueuedUserPrompt admission; do not change shared
`BootReplyWatch.TryArmAsync` to use the task-only AcceptedSequence contract.
For queued-only evidence, say “queued prompt record” and “no reply observed,”
not “the model accepted the input” or “delivery succeeded.” Never resend it.
For an ordinary accepted prompt, retain the transcript-delivery distinction.
Housekeeping and interrupt interpretation stays with the existing shared
predicate; a questionable record can cause only detection, never recovery.

Attempt the existing runner transcript catch-up before first detection and
escalation, then read the predicate again. An arriving reply revokes emission.
Do not reinterpret CatchUpTranscriptAsync(false) as confirmed freshness, change
its public contract, or make its availability necessary for the no-stop result.
Stored evidence can support “no reply observed in the stored transcript”; if
freshness is not proven, do not label the provider dead. Required DB read failure
is unknown. Caller cancellation propagates; other telemetry faults are isolated.

### D-4. Bounded escalation and episode identity

Warning begins at `promptAt + bootWait`. Error begins at
`promptAt + max(bootWait, modelWait > 0 ? modelWait : 20 minutes)`.
The defaults are **8 and 20 minutes**. There is no new 6.4-minute preview for this
session watch and no new configuration knob. Zero/negative boot wait disables
this watch's notifications, preserving its existing setting semantics; it does
not authorize any stop. A first observation after operator due produces only
Error, not a burst of historical stages. Clock rollback does not mint an episode
or a duplicate stage. Error remains the displayed stage once positively reached
and recorded, while current unresolved facts still match.

Identity is `(sessionId, normalized StartedAt, launchClock, promptSequence)`;
the stage is a separate suffix. Use an explicit versioned FailureReason prefix
such as `standingBoot:v1;g=<ticks>;l=<ticks>;p=<seq>;stage=<detected|operator>`.
SessionId is already an incident column. Preserve the old `bootSeq=` parser for
historical/non-standing episodes; do not rewrite old incidents. A new accepted
generation, launch or refined real prompt is a new episode. Owner changes revoke
the attribution; they cannot turn the old episode into stop authority.

Use existing LivenessProbeFailed incident/attention enums. At most one optional
incident per episode/stage, two total; warn once then escalate once, with no
periodic reminders, model call, channel send or delegated operator task. Store
IDs, prompt kind/sequence, clocks and reason only, not prompt/composer text or
raw runner errors. Optional diagnostics must not be able to block attention.

### D-5. Telemetry isolation, dedup and active-record retention

Add `StandingBootWarningWriter`, following CARD-1151's short-lived-context
precedent without changing its task writer. Commit only its own incident.
Acquire the session row with `FOR UPDATE SKIP LOCKED`, re-read required facts,
dedup the exact episode/stage and insert. An already recorded operator stage
also suppresses a later detected-stage write. No RPC while holding this lock.
A contended/missing row declines this pass; real DB errors are logged and remain
non-destructive. Dispose/rollback on failure; no dirty telemetry entity may leak
to the next session or a later SaveChanges. Publish any existing invalidation
only after commit and best effort. Do not change lifecycle state in this writer.

Keep a valid unresolved standing watch armed after notification, so an operator
stage can follow and a later model reply can disarm it. Self-heal handles legacy
cleared watches using the existing prompt predicate. The generic non-AlwaysOn
diagnostic path keeps its existing detection-only behavior.

Protect the **new-format current unresolved episode receipts** from both age and
per-agent cap pruning in `AgentSupervisorService.PruneIncidentsAsync`. Resolve
their metadata against current session facts; uncertainty retains receipts,
never deletes dedup evidence. Normal steady state retains at most two receipts
for the current episode. A positively answered, replaced or terminal episode
becomes eligible for ordinary retention again. This is a cold pruning change,
not a supervisor restart-policy change. Avoid a new ledger/table/migration or
global exemption for all incidents. Tests must exercise the real pruning method,
including cap pressure, rather than just deleting unrelated incidents.
Retention checks episode resolution, not the notification-admission whitelist:
temporarily disabling the deadline, changing AlwaysOn or binding a task does not
by itself prove that the episode ended or permit deletion of its dedup receipts.

Task-binding or a model reply can race the last telemetry observation. The writer
must reject every change committed before its final read; no cross-service
atomic lifecycle claim is made for changes after that read. Any such saved
historical observation is hidden by the next current-state attention projection
and confers no recovery authority. Do not add locks to task dispatch/delivery to
make optional telemetry own those transitions.

### D-6. Current attention is authoritative; incidents are optional history

Update only the boot portion of `AttentionService`, preferably extracting a
`StandingBootAttentionProjection` helper to limit the CARD-1151 collision.
Enumerate current eligible standing sessions and compute the same boot facts and
stage, even when both watch columns are null after an old raise or a failed arm.
The projection is read-only: no runner RPC, queue operation, arm/save or telemetry
insert during GET. Return one LivenessProbeFailed item per current episode,
independent of any incident's CreatedAt and the 24-hour recency window.

The existing `OpenAgent`/`OpenDrawer` actions expose the agent's actual controls.
Text at Error: inspect the session/transcript; choose to wait, reply through the
normal session UI, or explicitly Stop and Start/resume. Do not invent task-only
Retry/Cancel actions for a taskless session or suggest automatic recovery. Show
prompt age, boot due and operator due. An Error is a bounded request for an
operator decision, not a promise that an operator has noticed it.

Suppress legacy `bootSeq=` items for sessions positively assigned to the new
standing projection, including a refined prompt whose new deadline is not due,
so old history cannot duplicate it or show obsolete restart wording. Apply the
current open-task exclusion to legacy items too, avoiding a task/session double
row after ownership changes. Preserve unrelated non-AlwaysOn legacy attention. A positively
answered/replaced/terminal episode clears the derived row; historical receipts
remain subject to retention. Re-read current owner/tasks to avoid duplicating
CARD-1151 task attention. No new client enum, endpoint or UI flow is required.

### D-7. Custody and delivery stay intact

Neither notification stage modifies session status, TerminationSource, EndedAt,
run attempt, task, queue rows, agent pointer, supervision counters/latch, park,
runner release, alias hold or native conversation. Counterfactual “absent” and
non-Working evidence still does not cause a stop. CARD-0079 tests must continue
to prove its own conditional-stop/continuation contract independently.

The unchanged delivery path retains LF, bracketed paste and separate Enter.
Preserve message IDs, Body bytes, Attempts, delivery baseline, spill bytes and
existing FIFO through warning, escalation, restart of the service and failures.
Do not send a notification into the watched session.

**What releases a session waiting for input, and after how long?** This plan
creates no new input-wait state or timer. A silent Working boot keeps its seat;
the 20-minute stage changes attention only. An explicit operator Stop, genuine
process exit/completion or another independently admitted existing lifecycle
action can release it. If it later waits for input, nothing automatically
releases it while BlockedTaskParking is disabled (the current default,
CARD-1083); there is **no automatic release deadline**. Enabling parking does
not make a taskless/Working boot eligible, and this plan does not enable it.

### D-8. Migration, activation and placement

**Database migration: no.** Existing incidents/watch columns suffice. Preserve
old latches, counters and incident formats; only explicit existing operator
actions clear old latches. No backfill, resume, replay or automatic Start occurs
on deployment. Already stopped sessions are not resurrected by this change.

**AppHost restart: no for this Plan/TestDesign or isolated verification; yes for
the eventual server implementation after land.** The orchestrator activates the
canonical checkout under `docs/apphost-runbook.md`, then verifies `/api/version`
against the landed source. No restart from this worktree; no runner upgrade is
needed for A. Rollback to the old server restores the unsafe boot-stop policy,
so rollback is not a neutral operational action and needs an explicit decision.

Read GET `/api/runner-defaults` and `/api/session-runners` at approximately
15:49 UTC: revision 2, no per-kind overrides; a Windows lane and Linux lanes
were available, one Linux entry draining and the accepting Linux entry fully
occupied. This is an observation, not a placement requirement. Re-read at
dispatch. All checkpoints below use the **portable .NET/PostgreSQL lane**; omit
`-Runner` and `-Platform`. If clearing an inherited OS pin, `-Platform Any`
unpins it. No fleet address or host is embedded in this plan. No native terminal
operation changes, so a Windows-only proof lane adds no necessary evidence here.

### D-9. Integration and collisions

| Work observed at 15:49 UTC | Collision | Landing/authoring rule |
|---|---|---|
| CARD-1151 S4 repair `cc45ec44` queued; S4 bodies still pending at this baseline | `AttentionService.cs`, `BootReplyWatchdogService.cs` task-ownership comment, `BootReplyWatchdogTests.cs` comment, runtime/orchestration docs and `BootStallDocumentationTests.cs` | Let S4 publish its task-attention/doc contracts first. Read the actual landed diff before S1/S4/S6; preserve task detection. Do not copy the old plan's rejected safe-absence branch. |
| CARD-1151 S5 Code `739f831c` at `662fed75533f2d3f48b53eadf58c5697488b5968`, ready for Review | `BootStallDetectionTests`, boundary tests and checkpoint/classification claims | Keep CARD-1156 tests in new classes/fixture. S5's task-watch stand-down and statement pins remain regressions. Do not silently count its current pending bodies as green. |
| CARD-1149/1150 S2 repair 4 Review `0336a6b0` in flight; older task rows also Blocked/ready | `AgentSessionService.cs`, `AgentTaskDispatcher.cs`, `DispatchBriefEvidence.cs`, `SessionMessageQueueService.DispatchBrief.cs`, boundary test partials | Those implementation files are read-only dependencies here. No launch/ensure/retry or shared helper rewrite. Preserve 18/18/4 and the subsequently landed S2 statement roster. Refresh review/landing status before verification. |
| Supervisor and shared attention/doc owners at future dispatch | `AgentSupervisorService.cs` cold prune method; `AttentionService.cs` boot projection; documentation | Check live scoped occupancy and serialize overlapping edits. The prune hunk must not alter restart/counter logic. Landing rebases; never rebase/reset a pushed runner task branch. |

This artifact alone has no collision with those source changes. Do not wait for
unrelated work to land the plan. Refresh scoped pipeline/task state before each
Code dispatch, and publish each 30-60 minute slice to its assigned task branch.

## Implementation slices

Paths in the Tests column are under `tests/Antiphon.Tests/`; Services are under
`server/Application/Services/`. Commit/push each slice with truthful pending
verification wording. S1-S3 form one coherent verification group; they must all
exist before that group's new tests are expected green.

| Slice | Minutes | Files / resulting behavior | Tests and checkpoint group |
|---|---:|---|---|
| S1 | 45 | `BootReplyWatchdogService.cs`: remove generic stop and supervision mutation; preserve detection/stand-down; explicitly reverse the three legacy assertions. | `Application/BootReplyWatchdogTests.cs`; new `Application/StandingBootWatchdogTests.cs`, `TestHelpers/StandingBootWatchFixture.cs`; V-1, V-2; After S1-S3. |
| S2 | 45 | New `StandingBootWatchPolicy.cs`, `StandingBootWatchObservation.cs`; add positive current-owner/task/episode admission and prompt-relative thresholds; retain existing session prompt semantics. | New `Application/StandingBootWatchPolicyTests.cs`; V-3/V-4; After S1-S3. |
| S3 | 60 | New `StandingBootWarningWriter.cs`; wire the existing sweep through isolated dedup writes and rechecks, retain active watch, no lifecycle side effects; use existing DI conventions. | StandingBootWatchdogTests/fixture: V-5/V-6/V-7, real runtime catch-up and fault seams; After S1-S3. |
| S4 | 60 | `AttentionService.cs`, optional extracted `StandingBootAttentionProjection.cs`; current derived boot row and legacy merge. `AgentSupervisorService.cs` cold prune protection only. | New `Application/StandingBootAttentionTests.cs`; V-8/V-9/V-10. CARD-1151 S4 and legacy attention regressions; After S4. |
| S5 | 45 | Complete integration/no-recovery/cost witnesses; use new fixture to avoid shared S2/S5 edits. Add necessary test classification metadata only if the new fixtures require it. | New `Application/StandingBootStatementBudgetTests.cs`; V-11/V-12; existing CARD-0079, launch-probe, supervisor and 18/18/4 regressions; After S5-S6. |
| S6 | 30 | `docs/session-runtime-invariants.md`, relevant boot paragraph of `docs/orchestration-loop.md`, watchdog comments; state standing detection, clocks, retained custody and sole CARD-0079 exception. AGENTS.md's exception stays unchanged under A. | New `Application/StandingBootDocumentationTests.cs`; V-13 and unchanged CARD-1151 documentation pin after its publication; After S5-S6. |

## Verification design

This is the proposed design for separate TestDesign, following the operator's
choice. No baseline, new test, mutation or build was executed for this plan.
TestDesign must inspect bodies and materialize/count the final argument roster,
especially after CARD-1151 S4/S5 and S2 land. It must not weaken this scope or
substitute skipped skeletons. If the operator chooses B/C/D, rewrite this design
before Code; the option-A checkpoints do not qualify an automatic-stop policy.

### Inspection and fixture

Use real PostgreSQL, isolated per-test data/schema and FakeTimeProvider; no wall
clock sleeps. Keep globally sweeping tests serial. Build the sweep, projection,
pruner and (for V-2/V-12) supervisor through real services. Use a recording
IDelegateSessionStopper plus runner counters for Kill/conditional compaction
stop/start/input/release, and assert both surfaces. Real transcript rows feed
`IsWorkingAsync`, not a bool supplied by the fixture. Fault/insertion barriers
are at actual EF command/save/commit and runtime pull seams, with bounded waits.
Exercise two separate providers/contexts for dedup; a second call on one object
does not establish restart/concurrency behavior.

Every new behavioral test asserts the positive result (row, stage, key, custody
or lack of duplicate) plus the absence of forbidden side effects. A no-op
implementation must fail the positive controls. Snapshot all supervision fields,
including an absent row, so removal of a counter assertion cannot conceal state
mutation by the watchdog. Snapshot sweep and supervisor boundaries separately:
the existing supervisor may legitimately update LastHealthyAt/UpdatedAt, but the
boot sweep may not. Seed prompt/composer canaries and assert absence from new incident and
attention messages. Fixtures never boot real Program against a production runner.

### Works-now witnesses and one-flip tables

The names below are exact proposed method names. `*` on a parameterized method
selects its argument expansions. Counts are designed executions, not measured
passes. A one-flip case changes only the named field from the admitted baseline;
provider/fixture setup remains identical.

| ID | Exact test / cases | Required outcome |
|---|---|---|
| V-1 | `BootReplyWatchdogTests` (11 retained tests). Rename `a_standing_agent_goes_through_the_existing_restart_ladder` to `a_standing_boot_stall_is_detected_without_stopping_or_driving_the_restart_ladder`; rename `the_third_consecutive_failure_latches_the_mechanism_off_instead_of_restarting` to `boot_silence_preserves_existing_failure_history_without_creating_a_latch`; rename the core incident test to `an_unanswered_standing_boot_raises_once_and_keeps_its_watch_for_escalation`. | First: Killed `[id]` -> empty, no newly created supervision state, same live session. Second: seeded failures 2 stays 2, latch null stays null, no “stopped restarting” claim, no stop. Third: keep the 30-minute seed; Warning -> Error because it is past operator due; the new versioned key replaces the bootSeq expectation; one incident remains, watch due/sequence stay armed, repeat creates no duplicate. Add a comment naming CARD-1156's intentional policy reversal; do not delete tests. Other eight behavioral contracts remain; comments/incident wording follow the new policy. |
| V-2 | `StandingBootWatchdogTests.C1156_Working_boot_keeps_its_session_and_supervisor_custody` (3: ClaudeCode/Grok/Codex) | A nine-minute real prompt, no model row, live matching runner; real Working is true. Sweep and supervisor ticks, including beyond 20 minutes, keep the session/pointer; no stop/start/compaction/input/park/release/alias hold and no watchdog counter mutation; Warning then Error. |
| V-3 | `StandingBootWatchPolicyTests.C1156_Emission_requires_each_positive_condition` (21: admitted, deadline-zero, deadline-negative, unsupported-provider, rules-pending, rules-failed, session-missing, session-terminal, ended-at-set, generation-unknown, owner-missing, owner-ambiguous, owner-not-alwayson, owner-conflict, task-queued, task-dispatched, task-working, task-blocked, prompt-missing, reply-present, identity-changed) | Admitted -> due stage. Each other single flip -> no new standing emission, never recovery. Also cover false/unknown observations as null, not default bool false. An unknown-policy test cannot use a fake missing row while silently substituting a valid owner. |
| V-4 | `StandingBootWatchPolicyTests.C1156_Stages_use_the_prompt_clock` (8: just-before-boot, at-boot, just-before-operator, at-operator, boot-longer-than-model, model-disabled, first-seen-after-operator, clock-rollback) | 8/20 minute defaults at exact boundaries; max rule, 20 fallback, only Error on late first observation; no duplicate/downgrade when an operator receipt exists. |
| V-5 | `StandingBootWatchdogTests.C1156_Episodes_deduplicate_and_reopen_only_for_new_identity` (6: repeated-tick, fresh-provider, concurrent-sweeps, new-prompt, new-generation, new-resume-clock) | One receipt per key/stage across repeats/providers/concurrency; changed identity permits its own receipt and supersedes the old visible episode. Null-cleared legacy watches self-heal without repeated old episode receipts. |
| V-6 | `StandingBootWatchdogTests.C1156_Telemetry_faults_preserve_custody_and_future_writes` (6: read-fault, insert-fault, save-fault, commit-fault, publish-fault, caller-cancel) | Optional faults do not stop, mutate supervision or poison the next session; rolled-back rows absent; publish failure retains one committed row. Caller cancellation propagates. After fault removal a fresh tick records once. Attention is independently checked in V-8. |
| V-7 | `StandingBootWatchdogTests.C1156_Fresh_evidence_revokes_stale_emission` (6: reply-during-pull, queued-owner-before-insert, blocked-owner-before-insert, pointer-changed-before-insert, generation-changed-before-insert, session-lock-held) | Real runtime catch-up saves the late reply; each changed fact committed before the writer's final read prevents insertion. Contention returns promptly without a Warning fault or generic kill; later valid tick succeeds. No indefinite lock waits. |
| V-8 | `StandingBootAttentionTests.C1156_Current_boot_attention_survives_optional_history` (7: no-incident, failed-save, warning-at-eight, error-at-twenty, older-than-24-hours, pruned-history, legacy-and-current) | One derived row with correct current severity/identity/actions and safe wording, including no history and old/pruned receipts; legacy/current merge produces one row. |
| V-9 | `StandingBootAttentionTests.C1156_Positive_resolution_clears_only_the_current_episode` (7: assistant, thinking, tool-call, tool-result, turn-end, terminal-session, replaced-launch) | Five real model kinds resolve; terminal/replacement clears old episode. A new unresolved replacement can independently appear. History remains. |
| V-10 | `StandingBootAttentionTests.C1156_Prune_preserves_active_dedup_and_releases_resolved_history` (4: age-cutoff, agent-cap, unknown-current-read, positive-resolution) | Real pruning retains the current episode receipts under both limits and uncertainty. A later sweep cannot re-mint them. After positive resolution ordinary pruning works. |
| V-11 | `StandingBootStatementBudgetTests.C1156_Boot_watch_statement_budgets` (8 paths below) and `C1156_Attention_and_pruning_statement_budgets` (3: zero-candidates, one-candidate, two-candidates) | All-context counting, fixed cheap-path numbers and bounded cold-path rosters; no hidden telemetry/persistence context or full transcript Text scan. |
| V-12 | `StandingBootWatchdogTests.C1156_Evidence_variants_never_authorize_recovery` (8: runtime-absent, pull-fault, successful-unchanged-pull, runner-absent, non-working, queued-prompt, legacy-latch, terminal-task-history) | Retain session/input and old latch/counters; seed the legacy-state row with nonzero failures, an existing latch, a future NextRestartAt and nondefault unrelated fields. Runner absence/false Working gives no kill authority. Queued evidence is labelled queued; terminal history does not withhold current standing detection. No periodic probe or retype. |
| V-13 | `StandingBootDocumentationTests.C1156_Docs_name_detection_clocks_custody_and_compaction_exception` (1) | Pin approved owner sentences about no boot stop, 8/20 defaults, no automatic release deadline and separate CARD-0079. This is documentation coverage, not behavioral proof. |

V-3's unit matrix is backed by service outcomes in V-2/V-7/V-12. Add explicit
integration assertions in those methods for absent-state vs existing-state,
no model alias hold, task-watch stand-down and queue/spill byte preservation.
Negative method counts never stand in for an exercised production branch.

### Regression obligations

| ID | Existing tests retained | Why |
|---|---|---|
| R-1 | `BootReplyWatchTests` (27 at this source), `BootLivenessProbeScopeTests` (8) | Prompt/reply/provider/launch-clock semantics and the once-per-launch probe. No new scheduled probe. |
| R-2 | Three existing `AttentionServiceTests` boot methods named in CP-13..15 | Preserve old-format/non-standing incidents and real reply/death resolution. |
| R-3 | `AgentSupervisionTests` exact methods in CP-20..22 | Actual no-session recovery, human latch clear and healthy-counter reset remain independent. |
| R-4 | `CheckCompactionContinuationTests` (2), `CheckCompactionRecoveryFlowTests` (1) | CARD-0079 fresh-evidence, continuation and conditional-stop path are not disabled by the boot fix. |
| R-5 | `BootStallAttentionTests.C1151_Attention_describes_detection_and_resolution` (5), `BootStallDetectionTests.C1151_Detection_does_not_release_or_park` (2), CARD-1151 documentation pin (1) | Task-bound behavior, no double ownership and shared docs survive integration. Pending at this baseline is not a pass: land/refresh the actual S4/S5 work first. |
| R-6 | `DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets` (at least original 3) | 18 held / 18 working-live / 4 inside-grace, plus all subsequently landed S2 argument rows unchanged. |
| R-7 | `SessionHealthTests.No_probe_prompts_are_ever_sent_to_an_idle_session` (1) | No periodic liveness prompts. |

### Statement budget

Use `FullCommandCounter` on **every** context, including runtime persistence and
isolated telemetry contexts. It counts DbCommand executions (including writes
and multi-statement batches as one round trip), not individual SQL substatements.
Report totals and ordered SQL rosters; exclude fixture setup and count reads and
writes without query-tag filtering. Transaction begin/commit faults need their
separate interceptors. None of the new paths calls runner List or workspace
probes. Record catch-up call counts separately from SQL.

| V-11 path | Budget per isolated one-session sweep | Basis |
|---|---:|---|
| boot deadline disabled | 0 | Preserve early return before scope/database work. |
| no live sessions | 1 | Live candidate query only. |
| healthy unarmed, already answered | 2 | Candidate + existing indexed model-reply EXISTS; no owner/transcript Text work. |
| armed, before deadline | 2 | Candidate + existing sequence/kind verdict query; no notification enrichment. |
| first detected stage | <= 32 | Cold observation + one optional catch-up with no new entries + writer lock/recheck/insert. |
| same recorded episode, no stage advance | <= 12 | Cheap current evidence and key read; zero catch-up, insert or supervision write. |
| first operator stage | <= 32 | Same cold path, at most one new Error receipt; not all historical stages. |
| runtime absent / stored-evidence detection | <= 26 | No runner work; same no-stop/dedup policy. |

These are **design ceilings**, not measured totals. TestDesign must enumerate
each planned command and pin exact expected cold-path totals within these caps;
Code prints and asserts those totals rather than automatically adopting whatever
it measured. New commands require an explained design revision, not wider
tolerances. Existing cheap 0/1/2/2 and dispatcher 18/18/4 pins are exact now.
Do not move new reads ahead of the young/answered gates to obtain shared helpers.

For the new attention helper, allow at most **2 + 4N** commands for N eligible
candidate sessions, including optional current-stage receipt reads; zero runner
calls and zero writes. The zero/one/two-candidate cases pin the fixed and linear
terms and the actual roster. Use bulk candidate/owner/open-task projections;
only prompt text needed for the existing housekeeping classifier may be loaded.
For pruning, allow at most **2 + 4N additional commands** over its inherited
delete/cap work to determine protected current episode keys. The same three
fixtures measure this delta as well as absolute totals. No per-incident scan of
whole transcripts. An inability to meet these bounds goes back to TestDesign.

### Guard inventory and production mutation controls

All PCs mutate **production** guards/side effects, never expected values or
fixtures. These are pending post-land SourceLanding Mutation obligations, not
permission to perform production faults against the live fleet. Each variant is
its own method-scoped red/restore/green cycle. Fully qualify the method filter as
`/*/*/<Class>/<ExactMethod>*` from the Method column; never run the whole class to
prove one mutation. Zero tests, fixture failure, build failure or a timeout is
not the expected red. Use the gated driver and retained per-PC evidence.

| PC / guard | Compiling production mutation | Method; expected red | Cycles |
|---|---|---|---:|
| PC-1 / no stop | Reinsert the old stopper call after recording a detected episode. | `StandingBootWatchdogTests.C1156_Working_boot_keeps_its_session_and_supervisor_custody`; nonempty stopper or changed session custody. | 1 |
| PC-2 / no supervisor mutation | Increment ConsecutiveFailures; separately set LivenessLatchedAt; separately clear an existing NextRestartAt. | `StandingBootWatchdogTests.C1156_Evidence_variants_never_authorize_recovery`; saved state differs from seeded snapshots. | 3 |
| PC-3 / positive admission | Bypass each whitelist clause group: enabled deadline, provider, rules state, live/EndedAt, valid generation, unique current owner/AlwaysOn/conflict, task exclusion, prompt/reply identity. Use one mutation per group with all its specified one-flip rows inspected. | `StandingBootWatchPolicyTests.C1156_Emission_requires_each_positive_condition`; a refused row emits. Integration PCs below also guard caller use. | 8 |
| PC-4 / prompt clock | Use now instead of promptAt; separately use min instead of max for the operator deadline. | `StandingBootWatchPolicyTests.C1156_Stages_use_the_prompt_clock`; wrong boundary/stage. | 2 |
| PC-5 / persistent dedup | Bypass the persisted stage-key check. | `StandingBootWatchdogTests.C1156_Episodes_deduplicate_and_reopen_only_for_new_identity`; duplicate receipts. | 1 |
| PC-6 / episode identity | Omit generation; separately omit launch clock from the key. | Same V-5 method; a new episode is incorrectly suppressed. | 2 |
| PC-7 / atomic dedup | Remove the writer's session row lock. Hold writer A after its dedup read but before INSERT; start B and await its complete attempt before releasing A. | Same V-5 method, concurrent-sweeps row; baseline B skips A's locked row, mutant B inserts then A inserts, producing two same-stage receipts. Use bounded barriers, no sleeps or barrier that requires both writers to acquire the same lock. | 1 |
| PC-8 / current evidence | Skip the post-pull predicate re-read; separately skip owner/task revalidation before insert. | `StandingBootWatchdogTests.C1156_Fresh_evidence_revokes_stale_emission`; stale incident survives a committed reply/owner change. | 2 |
| PC-9 / fault isolation | Use the sweep's context for failed telemetry so staged entities survive to the next save. | `StandingBootWatchdogTests.C1156_Telemetry_faults_preserve_custody_and_future_writes`; a failed row leaks or the next valid episode is lost. | 1 |
| PC-10 / no error recovery | Add a stopper in the non-cancellation telemetry fault handler. | Same V-6 method; fault produces stop evidence. | 1 |
| PC-11 / independent attention | Require a persisted incident before projection; separately restore the 24-hour age filter. | `StandingBootAttentionTests.C1156_Current_boot_attention_survives_optional_history`; missing current row. | 2 |
| PC-12 / escalation and merge | Force Warning at operator due; separately append the covered legacy row. | Same V-8 method; wrong severity or two visible items. | 2 |
| PC-13 / positive resolution | Omit the current reply/terminal/replacement revalidation. | `StandingBootAttentionTests.C1156_Positive_resolution_clears_only_the_current_episode`; stale visible row. | 1 |
| PC-14 / retained dedup | Remove active-receipt exemption from age deletion; separately remove it from per-agent cap deletion. | `StandingBootAttentionTests.C1156_Prune_preserves_active_dedup_and_releases_resolved_history`; deleted active receipt and duplicate on next sweep. | 2 |
| PC-15 / hot-path cost | Add a SELECT 1 before the watchdog's healthy-answer return. | `StandingBootStatementBudgetTests.C1156_Boot_watch_statement_budgets`; 3 instead of 2. | 1 |
| PC-16 / no probe/input | Enqueue a new probe from repeated detection. | `StandingBootWatchdogTests.C1156_Evidence_variants_never_authorize_recovery`; message/input count or byte snapshot changes. | 1 |

Total: **31 method-scoped cycles**. Every method has a positive admitted control;
TestDesign must check each grouped whitelist mutation produces the named
assertion red and add a caller-level mutant if the policy could become unused.
Existing CARD-0079 implementation guards are covered by its unchanged regressions
and its own mutation scope, not re-mutation of that independent mechanism here.
Documentation pins are inspected and exercised ordinarily; no runtime safety
claim rests solely on searching a source file for `KillAsync`.

### Out of scope and cost

No general Working classifier change, shared BootReplyWatch prompt rewrite,
runner absence certificate, new idle-stop operation, task failure/retry policy,
parking enablement, periodic probe, channel notification system or automatic
Fresh. No build/test execution is claimed by this planning dispatch.

Authoring: **285 minutes** across six slices. Ordinary checkpoint floor:
**78 minutes**, including three isolated builds (estimated 4 minutes each within
their rows); slot waits and separate TestDesign/Review are additional. Proposed
Code budget: **363 minutes plus slot waits**, split by slice group. Mutation:
31 cycles at approximately 4 minutes = **124 minutes**, separately commissioned
after ordinary Review and land. These are estimates, not recorded durations.

### Checkpoints

Closed ordinary scope for option A, after separate TestDesign freezes the roster.
All Group names denote the portable .NET/PostgreSQL lane. Serial rows also keep
builds from overlapping the shared Postgres witnesses; set
TUNIT_MAX_PARALLEL_TESTS=1. No whole-Unit/assembly run. Each row has one exact
filter and an isolated build or same-After build reuse. R-5 rows require the
real S4/S5 bodies to be published; never accept pending skips.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1156-watch/` | portable-watch-regression | `/*/*/BootReplyWatchdogTests/*` | V-1 | all 11, 0 failed/skipped | 11 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S3 | `CP-1` | portable-working-custody | `/*/*/StandingBootWatchdogTests/C1156_Working_boot_keeps_its_session_and_supervisor_custody*` | V-2 | all 3, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S3 | `CP-1` | portable-emission-whitelist | `/*/*/StandingBootWatchPolicyTests/C1156_Emission_requires_each_positive_condition*` | V-3 | all 21, 0 failed/skipped | 21 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S3 | `CP-1` | portable-stage-clocks | `/*/*/StandingBootWatchPolicyTests/C1156_Stages_use_the_prompt_clock*` | V-4 | all 8, 0 failed/skipped | 8 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1-S3 | `CP-1` | portable-episode-dedup | `/*/*/StandingBootWatchdogTests/C1156_Episodes_deduplicate_and_reopen_only_for_new_identity*` | V-5 | all 6, 0 failed/skipped | 6 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1-S3 | `CP-1` | portable-telemetry-faults | `/*/*/StandingBootWatchdogTests/C1156_Telemetry_faults_preserve_custody_and_future_writes*` | V-6 | all 6, 0 failed/skipped | 6 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S1-S3 | `CP-1` | portable-fresh-evidence | `/*/*/StandingBootWatchdogTests/C1156_Fresh_evidence_revokes_stale_emission*` | V-7 | all 6, 0 failed/skipped | 6 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S1-S3 | `CP-1` | portable-no-recovery | `/*/*/StandingBootWatchdogTests/C1156_Evidence_variants_never_authorize_recovery*` | V-12 | all 8, 0 failed/skipped | 8 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S1-S3 | `CP-1` | portable-boot-predicates | `/*/*/BootReplyWatchTests/*` | R-1 | all listed, 0 failed/skipped | 27 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S4 | `tests/Antiphon.Tests -> bin-c1156-attention/` | portable-current-attention | `/*/*/StandingBootAttentionTests/C1156_Current_boot_attention_survives_optional_history*` | V-8 | all 7, 0 failed/skipped | 7 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S4 | `CP-10` | portable-attention-resolution | `/*/*/StandingBootAttentionTests/C1156_Positive_resolution_clears_only_the_current_episode*` | V-9 | all 7, 0 failed/skipped | 7 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S4 | `CP-10` | portable-active-retention | `/*/*/StandingBootAttentionTests/C1156_Prune_preserves_active_dedup_and_releases_resolved_history*` | V-10 | all 4, 0 failed/skipped | 4 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | S4 | `CP-10` | portable-legacy-attention | `/*/*/AttentionServiceTests/An_open_boot_reply_incident_on_a_live_session_is_liveness_probe_failed` | R-2 | 1, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | S4 | `CP-10` | portable-legacy-answer | `/*/*/AttentionServiceTests/A_boot_prompt_that_was_answered_after_the_incident_is_no_longer_listed` | R-2 | 1, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | S4 | `CP-10` | portable-legacy-death | `/*/*/AttentionServiceTests/A_boot_reply_incident_on_a_dead_session_is_not_listed` | R-2 | 1, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | S4 | `CP-10` | portable-task-attention | `/*/*/BootStallAttentionTests/C1151_Attention_describes_detection_and_resolution*` | R-5 | all 5, no pending, 0 failed/skipped | 5 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-17 | S5-S6 | `tests/Antiphon.Tests -> bin-c1156-final/` | portable-watch-cost | `/*/*/StandingBootStatementBudgetTests/C1156_Boot_watch_statement_budgets*` | V-11 | all 8, exact frozen rosters, 0 failed/skipped | 8 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-18 | S5-S6 | `CP-17` | portable-projection-cost | `/*/*/StandingBootStatementBudgetTests/C1156_Attention_and_pruning_statement_budgets*` | V-11 | all 3, exact frozen rosters, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-19 | S5-S6 | `CP-17` | portable-delivery-scope | `/*/*/BootLivenessProbeScopeTests/*` | R-1 | all 8, 0 failed/skipped | 8 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-20 | S5-S6 | `CP-17` | portable-real-exit-recovery | `/*/*/AgentSupervisionTests/AlwaysOn_agent_with_no_session_is_scheduled_then_started` | R-3 | 1, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-21 | S5-S6 | `CP-17` | portable-human-latch-clear | `/*/*/AgentSupervisionTests/StartAsync_clears_the_liveness_latch_on_an_already_running_agent` | R-3 | 1, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-22 | S5-S6 | `CP-17` | portable-healthy-reset | `/*/*/AgentSupervisionTests/Healthy_uptime_resets_the_ladder` | R-3 | 1, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-23 | S5-S6 | `CP-17` | portable-compaction-evidence | `/*/*/CheckCompactionContinuationTests/*` | R-4 | all 2, 0 failed/skipped | 2 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-24 | S5-S6 | `CP-17` | portable-compaction-flow | `/*/*/CheckCompactionRecoveryFlowTests/*` | R-4 | 1, 0 failed/skipped | 1 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-25 | S5-S6 | `CP-17` | portable-task-custody | `/*/*/BootStallDetectionTests/C1151_Detection_does_not_release_or_park*` | R-5 | both, no pending, 0 failed/skipped | 2 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-26 | S5-S6 | `CP-17` | portable-dispatch-cost | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*` | R-6 | all landed arguments, 18/18/4 unchanged, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-27 | S5-S6 | `CP-17` | portable-no-periodic-probe | `/*/*/SessionHealthTests/No_probe_prompts_are_ever_sent_to_an_idle_session` | R-7 | 1, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-28 | S5-S6 | `CP-17` | portable-standing-docs | `/*/*/StandingBootDocumentationTests/C1156_Docs_name_detection_clocks_custody_and_compaction_exception` | V-13 | 1, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-29 | S5-S6 | `CP-17` | portable-task-docs | `/*/*/BootStallDocumentationTests/C1151_Docs_describe_detection_and_only_compaction_exception` | R-5 | 1, no pending, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Run one checkpoint-tool invocation per committed After group using this plan,
for example `run --plan docs/superpowers/plans/2026-10-08-card-1156-alwayson-boot-watchdog-plan.md --after S1-S3`,
then wait until the result is no longer 75. All drivers take the host build-slot
gate; bootstrap builds use `scripts/build-slot.ps1`. Slot timeout is not-run.
Do not edit source during a run. Retain ignored receipts with unedited CHECKPOINT
lines and per-row counts/source/build provenance; remove owned alternate bin
outputs afterward. Confirm inherited red using only its failing method at the
recorded base. No relaxed assertions, retries or enlarged timeouts to hide it.

## Handoff

Operator: select A/B/C/D and resolve D-2; the recommended defaults are A plus
unknown-keeps-session. If approved, TestDesign audits the current-owner whitelist,
telemetry/pruning races, statement rosters and checkpoint counts against the
actual landed S4/S5/S2 code, then hands Code this artifact. This plan requires no
answer to be written or published; implementation waits for that policy decision.
