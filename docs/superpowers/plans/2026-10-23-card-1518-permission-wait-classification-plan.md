# CARD-1518: classify provider input waits before the execution deadline

Plan owner: `f0cb46f5-39e4-40e0-bca7-e6606b37fd57`, 2026-10-23.
Inspected source and fetched master:
`3334e82ae8e52cb075893a7ff74e61aa6aab511f`. Documentation only; no build,
test, provider input, deployment, or Windows qualification was run by Plan.
TestDesign remains a separate stage: this brief requests a verification proposal
but does not fold TestDesign into Plan. Its next stage is **test-design**.

## Outcome and scope

Within one diagnostic scan interval, a current Claude permission/question dialog
becomes a visible decision for the caller, even with a sparse transcript and no
spawned command. Stall, Check, attention and deadline wording use the same typed
facts. The existing deadline still expires: a dialog changes the diagnosis, not
the budget. Failure leaves session, worktree and queued refinement intact and
names the supported recovery route.

Read the full live Antiphon cards CARD-1518, CARD-1517 and their named dependencies
CARD-0680, CARD-0294, CARD-0512, CARD-0153 and CARD-0020, and the
[incident evidence](../../investigations/2026-10-22-review-b985ca59-stall-debug.md).
The captured refusal was: “Compound command contains cd with write operation -
manual approval required to prevent path resolution bypass”. The dialog offered
Yes/No and “Esc to cancel”; Debug found no tool child and an undelivered refinement.
Neither command timeout could run before the command started.

This plan supersedes the **CARD-1518** portion of the
[older combined plan](2026-10-22-card-1517-1518-claude-permission-stalls-plan.md).
**CARD-1517 does not fold into these implementation slices.** It retains measured,
opt-in conditional Ctrl+C, any separately qualified explicit modal cancellation,
idempotent intervention intents and refinement confirmation. Its native Windows
qualification and supervisor PID 209072 are outside this task. Both cards consume
CARD-0680's landed observation and final input fence. Neither owns another screen
detector. CARD-1518 can ship diagnosis without qualifying or enabling a new key.
The older proposed CancelDialog/FailTask API and permission-wait script are not
part of this smaller plan; existing interactive and operator recovery suffice.

## Ground truth

| Card assumption / earlier plan | Code at the inspected source | Consequence |
|---|---|---|
| Every ToolCall means a local command is running. | `server/Application/Services/TaskDeadlinePolicy.cs:264`, `ClassifyPhase`, maps every ToolCall to `LocalExecution`. `EvaluateAsync` first gates on shared Working and an 80% preview. | Overlay current dialog facts on classification; detect independently of the preview gate. Keep the original clocks. |
| Generic stalls identify an approval promptly. | `DetectStalledProgressAsync` requires the configured novelty threshold (normally 30 minutes); `TaskProgressPolicy` also needs a populated transcript. It records `TaskProgressStalled`, without a caller note. | Add a bounded diagnostic pass on the existing dispatcher, before overdue processing; no minimum transcript population for an affirmative modal. |
| CARD-0680 is still missing. | `ProviderDialogObservation`, `ClaudeDialogObservation`, `ProviderDialogObservationService` and the runner input fence are present on master. The live card is still Review; publication is distinct from activation. | Consume the actual landed types; preserve dependency qualification obligations. |
| The observation supplies a safe excerpt, fingerprint and tool identity. | The sealed record carries `SessionId`, `RuntimeEpoch`, nullable `AcceptedStartedAt`, output/input counters, `State`, nullable string `Kind`, nullable `CanCancel`, `InputHistoryKnown`, and nullable `SubmittedInput`. It carries **no** excerpt, observation time, fingerprint or ToolUseId. | Do not program against the old proposed contract. Add a server completed-read timestamp, fixed safe wording and a task episode identity; no screen-text persistence or contract expansion. |
| `modalBlocked=false` is still RC-only. | CARD-0680 now composes Claude observation into queue admission/projection and guards automated writes; unavailable input history is Unknown. | The original false projection is historical. The remaining defect is task classification and caller guidance. |
| A live snapshot wrapper is the simplest observer. | `AgentSessionRuntime.ObserveDialogAsync` already handles test adapters and the bounded real observation. `TryGetLiveSnapshotAsync` additionally reads the buffer, and its test-adapter branch does not populate Dialog/generation. | Use `ObserveDialogAsync`/the shared observation service; no buffer read and no new parser. Fixtures must provide the real observation type. |
| Check can obtain these facts already. | `DelegateCheckProbe` reads DB/transcript facts and `TaskDeadlinePolicy`, with no provider observation. Its constructor deliberately has no terminal write surface. | Inject a narrow concrete read-only fact reader/cache, not an input controller; retain the Check no-write guarantee. |
| Failure releases the process or enables Refine recovery. | `TryFailOverdueAsync` calls the non-killing failure path. `AgentTaskReplyService` refuses terminal-task Refine. A live pool row is retained by the existing ownership rules. | Keep terminal task state immutable; display retained-seat guidance and do not promise Reply/Refine will resurrect it. |
| Caller delivery needs a new outbox. | `AgentTaskLandNotifications` already supports non-land notes, nullable RequestId, a unique SourceEventId and uniquely keyed queue rows. Non-completion kinds do not stamp task completion. | Append one non-completion note kind and reuse the outbox/reconciler. No new table or scheduler. |
| A missing server session row prevents release. | `RunnerSlotService.ReleaseAsync` resolves the named runner directly; the explicit single-seat endpoint requires operator authentication and audits a session row only if present. | Document and pin the existing route. Do not create a kill endpoint or synthetic session row. |
| A silent wait has a bounded seat lifetime. | Parking and automatic terminal release are disabled by default. CARD-1083 supplies no automatic release deadline while parking is off. | Name the continuing seat cost and the explicit release decision; 60 seconds and 90 minutes below are not seat-release timers. |

## Decisions

**D-1 — Reuse the landed diagnostic evidence unchanged.** Add a small
`TaskProviderWaitReader` and immutable `TaskProviderWaitFacts` in Application,
using `AgentSessionRuntime.ObserveDialogAsync` and
`ProviderDialogObservationService.Refusal`'s identity validity rules. Only
current Claude observations whose result is `ModalBlocked` qualify as observed
input waits. `ToolPermission` says permission, `Question` says question; other
blocked kinds say provider dialog without inventing its purpose. Missing kind
still supports the generic blocked label when the remaining evidence is valid.
`CanCancel` is display metadata only. `SubmittedInputProof` grants no authority
and is not required for read-only diagnosis.

Reject a second regex, `Detect == null` meaning Clear, an absent child implying a
permission prompt, or treating the input-admission Unknown hold as an observed
permission. Reject adding diagnostic excerpts to the wire contract here: the
captured text belongs in the regression fixture/evidence, not an unsanitized note.

**D-2 — Bind and expire the facts.** Snapshot the task id/attempt/session/dispatch,
session `StartedAt` and runner binding/store before the bounded read and re-read
that binding after it. Missing or changed identity, an empty epoch, missing or
unequal accepted generation, unknown input history, invalid counters, read failure
or cancellation cannot produce a current wait. Use `SessionGeneration.Equal`;
never compare provider timestamps as a substitute for generation.

A DI-owned observation store serializes reads per session, records completed-read
time with `TimeProvider`, and is keyed by task attempt, session generation and
runner binding. Retain RuntimeEpoch/output sequence/input revision with the facts.
A newly observed epoch replaces and invalidates the old facts; a delayed older
read cannot overwrite a later one. Cache age is measured from completed read,
not access. Age through 90 seconds is usable for diagnostic display; older or
negative-age data is Unknown. Missing/failed refresh invalidates current status
immediately. No persisted Warning is ever promoted back into fresh evidence.
Force a new bounded read immediately before a deadline disposition, after the
existing transcript catch-up; do not use the display cache for that decision.

Reject indefinitely caching Blocked, manufacturing a current epoch from a GUID,
refresh-on-access, or carrying a wait across retry/relaunch. These are diagnostic
fences, not permission to send any input.

**D-3 — Detect promptly on the existing dispatcher.** Add
`AgentTaskDispatcher.PermissionWait.cs` and a pass before `FailOverdueTasksAsync`.
Eligible tasks are Dispatched/Working Claude tasks with a live owned session;
also revisit their recorded episodes while a task remains open. Throttle to one
observation per session per 60 seconds, sharing concurrent Check reads. Each read
retains CARD-0680's two-second bound. Start due work on the next dispatcher tick;
isolate errors per task, use bounded concurrency of four reads and fair task order.
Concurrent reads must not share an EF DbContext: capture identities before the
read batch and revalidate/write through isolated per-task contexts afterward.
No new hosted service, fleet poller, process census, build-slot read or workspace
probe. This pass is independent of the generic StallDetection enable/30-minute
threshold and of deadline preview. Its operational target is the next due pass
(60 seconds plus tick/queue/read latency); do not claim a hard fleet-wide SLA if
the dispatcher itself is delayed. Tests cover due-at-60 and peer failure isolation.

For a newly observed wait, catch up the transcript before recording it and recheck
the task/session binding. Generic stall detection keeps its existing novelty and
severity rules, but uses these same facts to describe a current modal when it
does raise. A current provider-wait attention row takes precedence over generic
ProgressStalled/Overdue for that task and includes the unchanged deadline. Keep
boot-stall ownership and other existing failure/recovery guards intact.

Reject waiting 30/90 minutes for the diagnosis, or forcing the normal novelty
policy to pretend that every one-row permission wait is a generic progress stall.

**D-4 — Change diagnosis, preserve every budget.** Append internal
`TaskDeadlinePolicy.DeadlineKind.ProviderInputWait` without renumbering. With
shared Working=true, a ToolCall tail and current blocked facts, this phase uses
the existing `LocalExecutionDeadlineMinutes` (normally 90), with elapsed time
still `min(last-entry age, task age)`. Pass facts explicitly into the side-effect
free policy; keep the existing no-facts overload/default behavior. Question and
generic modal kinds also use this phase for a ToolCall. For other transcript
tails retain the selected model/ceiling limit and attach the dialog diagnosis;
do not turn a 20-minute model limit into 90 minutes. The tighter role ceiling
still wins and carries the same diagnostic context.

At exactly 90 minutes a still-blocked ToolCall fails with the provider-wait
wording and recovery guidance. At 89:59 it does not. Unknown/stale/clear evidence
uses the existing phase and limit; an unknown read must never suppress a genuine
execution deadline. Explicitly say observation unavailable when relevant, rather
than asserting that a tool is executing. A modal appearing/disappearing never
resets dispatch, transcript, progress or deadline clocks. Pulling a genuinely
new ToolResult/TurnEnd may change the normal verdict as it does today.

Reject pausing the deadline indefinitely, charging from first observation,
lengthening role ceilings, converting tasks to Blocked, or treating a screen as
proof of UserPrompt delivery, tool-process absence, or a completed turn. Without
tool correlation in the shared observation, say that execution is unconfirmed;
do not assert that every outstanding call is the blocked command. Transcript
ToolUseId may identify an outstanding call, but cannot bind a screen to it.

**D-5 — Durable, deduplicated diagnostic events and caller notes.** Use existing
`AgentTaskEventType.Warning` with a versioned `ProviderInputWait` detail payload.
Store only bounded identifiers, kind, episode state and observation provenance,
never raw screen/command/answer text. Deduplicate under the task row lock and a
fresh session binding check, following `BootStallWarningWriter`'s short isolated
transaction and nonblocking session-lock discipline. Identify an episode by task
attempt, session generation, runtime epoch, current accepted prompt and
outstanding-call identity (ToolUseId when present, otherwise transcript sequence),
and dialog kind. If transcript attribution is unavailable, use an explicitly
unknown attribution within this attempt, not a made-up tool id.

Record opening and observed resolution transitions in Warning detail; replaying
the same open episode does not emit a new note, including after service restart.
Verified Clear, a new accepted turn, matching tool result or binding replacement
ends that episode. A later blocked observation can open a new episode after a
recorded resolution. Unknown does not resolve it or create another opening.
Historical events remain history; current display still needs D-2 fresh facts.
Changing output sequence/animation alone is not a new episode. No schema migration
is expected: event Detail is existing text and notification Kind is an integer.

Append **`LandNotificationKind.ProviderInputWait`** (currently next ordinal 8).
Write its immutable body/digest, destination snapshot, SourceEventId and initial
outbox state in the same transaction as the opening Warning. `RequestId=null`.
It is **not** `TaskCompletion`, `DeliveryFailure`, or `LegacyCheckNote`; it must
not set CompletionNoteQueuedAt or suppress later Check/completion delivery.
Use the existing reconciler and keyed `SourceLandNotificationId` queue insert,
WhenIdle, `QueuedMessageOrigin.Delegation`, with conversation key
`provider-wait:<notification-id>`. Reconciliation recovers lost wakeups/links.
Keep the new kind on complete **UserPrompt** receipt, not QueuedUserPrompt;
require a non-null recorded delivery generation matching the current recipient
generation (extend the existing generation check for this kind). No raw fallback,
SendNow or retry after ambiguous input is introduced.

Reject a direct best-effort note, reusing the completion kind, or adding a second
outbox/scheduler. Existing outbox enum consumers/receipt/debt projections must be
audited by TestDesign, not assumed to accept a new kind harmlessly.

**D-6 — One actionable surface, no new board column.** Append
`AttentionKind.ProviderInputWait` (currently next ordinal 60), with existing
OpenDrawer/OpenAgent/OpenCard navigation only. It is a Warning requiring a
decision, not a new task status or permission-action button. Add the matching
client union/visual/rail mapping. Check renders the same structured facts and
fixed wording, before the generic stalled/deadline explanation. Existing task
event detail is the durable task-detail surface; no additional public action API.
Read-only attention uses the observation store; it does not run a snapshot loop
per browser request. Unknown/stale history is labeled as history, never a fresh
permission row. Terminal recovery remains visible in RecentFailure and existing
seat rows; do not keep an apparently open decision on a terminal task.

The note goes to the captured `ParentSessionId` when ReplyTo=Session. A missing
parent remains DestinationUnavailable and visible as existing note debt. With
ReplyTo=None, mark NotRequired; retain the task event and attention. Do not pick
another orchestrator or send an alert-sink/channel message. The caller arranges
the decision under its existing authority; if a human choice is needed, it records
that question on the card's existing NeedsDecision move/reopen revision. Detection
does not move the card. Busy callers receive the note at the normal safe boundary;
the early durable attention is visible even before transcript-confirmed receipt.

**D-7 — Recovery is explicit and preserves terminal truth.** CARD-1518 sends no
approval, answer, Ctrl+C, Esc, Enter, restart, kill or automatic retry. An authorized
operator can inspect and act through existing interactive input; supported
CARD-1517 actions may later be linked once qualified. Never advertise an available
CancelDialog merely because `CanCancel=true`. A Failed task remains Failed, and
new Refine still refuses. A replacement task is a separate, explicitly commissioned
attempt with preserved source/evidence and the original Code/Review ownership.

**D-8 — Portable placement and bounded verification.** Planning read
`GET /api/runner-defaults` (revision 2) and `GET /api/session-runners`: the effective
default was eligible Linux, and an eligible Windows runner also existed. These
are observations, not pins. Re-read them at dispatch; omit `-Runner` and `-Platform`
unless a later task truly needs a host/OS. All rows below name their lane. No Unit
lane, full assembly, live CLI canary, desktop operation or Windows-qualification
interference is part of this card's ordinary verification.

These are concrete design decisions, not choices awaiting approval. TestDesign
must finish executable fixture/guard bindings before Code, without broadening
the feature into intervention policy.

## Caller wording and recovery

Use the following literal templates (braces are values, not prose to improvise).
Persist the observation time so a delayed note makes its age explicit.

```text
[antiphon-permission-wait:{taskShortId}] Decision needed for {cardLabel}: Claude {dialogLabel} observed at {observedAtUtc} in session {sessionId}.
The task remains {taskStatus}. The outstanding ToolCall does not prove that execution started. Pending refinements: {pendingCount}; receipt: {receiptLabel}.
Inspect the current session and choose an authorized answer or cancellation. Antiphon has sent no approval or cancellation key. Refine may remain queued while the dialog is open; only a complete UserPrompt confirms delivery.
The existing {limitMinutes}-minute {clockLabel} deadline remains in force. Check current task and session state before acting on this note.
```

`dialogLabel` is exactly `permission dialog`, `question dialog`, or `provider
dialog`; use `task {taskShortId}` when there is no card. When there is no attributable
ToolCall, replace the second sentence with `Tool execution attribution is unknown.`
`receiptLabel` is `not confirmed`, `confirmed`, or `none queued`, derived from
actual queue verdicts; a queue insert or Sent without complete receipt is not
confirmed. The kind-specific headline and existing linked ids are enough; do not
copy the raw refusal/command from the screen.

Deadline summary for the captured case:

```text
Mid-turn with a current Claude permission dialog. The local-tool clock is {elapsed}, against its 90-minute budget. Tool execution is not confirmed. Last transcript entry: ToolCall, {age} ago.
The task is Failed. The session was not killed; its runner seat may still be occupied. New Refine calls on this task are refused. Preserve the worktree and pending queue, then inspect session {sessionId} and its runner seat before choosing recovery.
```

For unavailable evidence append `Current provider dialog observation unavailable;
the ordinary deadline still applies.` For a role-ceiling winner retain its actual
clock/limit; do not mislabel every failure as the 90-minute phase. Failure uses the
existing failure-notification path; the early decision note is not its replacement.

The documented terminal-task/live-seat route is:

1. Read task, transcript and pending messages; inspect the runner id from the
   task/session/catalogue and `GET /api/session-runners/{runnerId}/slots`.
   `openTaskId=null`, `orphan=false`, absent inventory, and a missing server
   session row each have different meanings; none proves process exit.
2. Preserve the branch, worktree, scratch evidence and valid checkpoint receipts.
   Do not clean a live worktree. A WIP checkpoint is a separately authorized
   action, not an automatic side effect of this diagnostic pass.
3. For useful continuing work, arrange explicit interactive recovery or a
   separately commissioned replacement; do not silently resurrect Failed or
   copy its pending refinement into a new session as though it were delivered.
4. If the operator chooses to release the retained seat, use
   `scripts/runner-slots.ps1 release -RunnerId <observed-runner-id> -SessionId
   <session-id> -Reason <reason>` (equivalently the explicit single-seat POST).
   This is a **process-tree kill**, requires the existing operator credential,
   and is available even if the server session row is absent. Recheck ownership
   first; do not use release-orphans or a fleet default as a substitute. A task
   token cannot elevate itself to operator authority. List again and retain the
   release/audit result; an HTTP acknowledgement alone does not prove exit.

### Session wait and release checklist

| State | What resumes or releases it? | After how long? |
|---|---|---|
| Open task, current permission/question modal | Authorized interactive resolution, a separately qualified explicit CARD-1517 action, explicit owner stop, or actual process exit. This card performs none. | At the owner's decision; no automatic release deadline with parking disabled. Detection is due every 60 seconds. |
| Same modal at the phase/role boundary | Existing policy fails the task and notifies; it does not release the process/seat or pending queue. | Normally at the 90-minute ToolCall budget, or the tighter configured clock. This is a task deadline only. |
| Failed task, session/seat still live | Operator explicitly releases the observed single seat after preserving evidence, or the process actually exits/is subsequently owned through an existing authorized path. | No deadline is added. Parking off means no automatic release promise (CARD-1083). |
| Decision note queued to a busy caller | Existing WhenIdle queue plus complete UserPrompt receipt; unavailable destination remains durable debt. | At a real safe boundary, not guaranteed within the diagnostic interval. Never force input to satisfy an SLA. |

## Slices and file ownership

S1 is the first Code slice **after TestDesign**. No CARD-1517 key qualification
blocks it. Order S1 -> S2 -> S3 -> S4, one implementation owner on one task branch.
Each slice is a 30–60 minute authoring unit, independently committed and pushed;
S1 is intentionally red and not landable. Keep all four in the same Code
assignment or resume the pushed branch. S2–S4 share their final test-project build
to conserve slots; do not land an intermediate unwitnessed slice.

| Slice | Change and named files | Tests / time |
|---|---|---|
| S1 | Add incident-shaped fixtures in `tests/Antiphon.Tests/Application/PermissionWaitTests.cs` and `PermissionWaitFixture.cs`. Use existing dispatcher/queue/policy entry points, real `ProviderDialogObservation` and the literal captured frame. Minimal test-only seam plumbing is allowed; no production behavior fix. Avoid references to a not-yet-existing enum so the red tests compile. | V-1 and V-4 red: first-tick Warning absent and failure says local tool instead of permission. 30–45 minutes; CP-1. Commit failing tests before the fix. |
| S2 | Add `server/Application/Services/TaskProviderWaitReader.cs` (facts, bound read, DI-owned cache) and modify `TaskDeadlinePolicy.cs`, `AgentTaskDispatcher.cs` final overdue re-evaluation, `Program.cs` registration. Keep the policy read-only and the cheap initial deadline pass. Add observation/deadline portions of `PermissionWaitTests`. | V-2..V-5 and R-1/R-2; 45–60 minutes. Commit tests before behavior. Verification joins S3/S4 at CP-2 onward. |
| S3 | Add `AgentTaskDispatcher.PermissionWait.cs` and `ProviderWaitWarningWriter.cs`; wire the pre-deadline pass and same-facts stall wording. Append kind in `server/Domain/Enums/LandingEnums.cs`; add the narrow conversation-key/generation handling in `AgentTaskLandNotificationService.cs`. Reuse existing event/outbox schema and recovery. | V-1, V-6..V-8, V-11 and R-3; 45–60 minutes. Atomic warning/note, restart/races, real queue receipts. No new notification worker. |
| S4 | Modify `DelegateCheckProbe.cs`, `AttentionService.cs`, `Application/Dtos/AttentionDtos.cs`; client `api/attention.ts`, `features/attention/attentionVisuals.ts`, `features/home/tasks/homeTasksModel.ts` and focused tests. Add terminal recovery pins in `PermissionWaitTests`/existing runner endpoint fixture. Update `docs/session-runtime-invariants.md`, `docs/ops-http.md`, `docs/agent-card-lifecycle.md`; link this plan from the older combined plan when implementation lands. | V-9, V-10, V-12, R-4 and client V-13; 45–60 minutes. Run CP-2..CP-8 once after final commits. |

No changes to `ClaudeDialogObservation`, detector patterns, runner input writes,
`MidTurnInterruptPolicy`, task-status transitions, release authority, migrations
or native CLI qualification are intended. If TestDesign finds a necessary schema
or detector change, amend this plan explicitly instead of hiding it in a slice.

## Rollout, activation and rc status

This plan and its feature implementation target master. At planning time fetched
master is the inspected SHA above. The release guide still names rc/2026-10-18;
the remote also has rc/2026-10-20 at
`5a126f6fc5ae4e16d803b628716b8f0485a37174`, and `GET /api/version` reports that
exact SHA. Inspection of that rc tree finds none of the three CARD-0680 observation
files. Master is not its ancestor. Therefore CARD-0680 publication and this plan
are **not evidence of active rc support**. Confirm the effective release target
with the caller's release workflow before promotion; do not infer it from a stale
document or restart a product host from master.

Ordinary Code V/R -> independent Final/Full Review -> master land, with the
required post-land verification companion carrying all pending PCs. No Interim
claim. Promote CARD-0680 and this feature together through the release process;
old runners yield Unknown and retain normal deadlines. Verify actual loaded
server/runner SHAs, `claudeDialogGuardV1`, and the dependency's applicable
qualification receipts before claiming the diagnosis is active. No new key feature
is enabled. Observe the first naturally occurring or separately commissioned
disposable modal read-only: one Warning/note, correct attention/Check text, pending
queue unchanged. There is no need to wait 90 real minutes: the ordinary fake-clock
boundary tests supply that evidence. No live Windows run is added to this plan.

An emergency rollback disables/removes only the new diagnostic pass/reader at the
normal release boundary; ordinary deadlines remain, and already persisted notes
must retain compatible reading/reconciliation. Do not roll back CARD-0680's send
guard to regain typing. A downgrade before the new enum consumer is available
requires draining/preserving new-kind note debt, not deleting it.

## Verification design

This is a bounded proposal for the separate TestDesign stage. It must bind the
new methods to executable fixtures and finish the independently bypassable guard
inventory. Names below are planned, not claims of executed tests. No Plan builds.

### Inspection and fixture boundaries

Read bodies in `TaskDeadlinePolicyTests`, `AgentTaskOverdueDeadlineTests`,
`TaskProgressStallSweepTests`, `ClaudeDialogDeliveryTests`,
`ExpectationDeliveryFixture`, `AgentTaskLandHeldNotificationTests`,
`RunnerSlotEndpointTests`, and the existing WorktreeLockChild/CrashWorkerProcess
helpers. Use an isolated PostgreSQL schema with the real dispatcher, event writer,
outbox reconciler and `SessionMessageQueueService`; `BridgeQueueHarness` supplies
recorded terminal writes and full transcript insertion. Do not fake a delivery
receipt or test only a formatter. No live provider account or production runner.

For the real-child contrast, use a locally inherited controlled test child with a
readiness/release barrier and a 30-second outer bound; run the 90-minute policy
through a fake clock. Assert it is alive while classified, then release/join it
in fixture cleanup. Use assembly-local `ParallelLimiter<ProcessSpawnLimit>` and
the established owned-process fixture discipline; no unrelated process census or
90-minute sleep. TestDesign chooses and names the exact helper before Code.

### Optional-input matrix

Every row needs absent/empty/value cases using the **actual** types below, not
`dynamic`, impossible nulls on structs, or only a happy-path anonymous object.
Implement most rows as labeled subcases of V-2/V-3/V-11 so they share one build;
an internal matrix still counts as one TUnit execution.

| Read / actual type | Absent or empty | Present value / oracle |
|---|---|---|
| Task binding `Guid? AgentSessionId`, `DateTime? DispatchedAt`; session row `FirstOrDefault` | null ids/times, Guid.Empty, missing session: no current classification/event. Do not dereference a missing row or use default DateTime as acceptance. | Exact current task/attempt/dispatch/session/generation admits read; changes across await invalidate it. |
| `AgentSession.RunnerId : string?`, `RunnerStoreId : Guid?` | null/empty/desktop alias follows existing local resolution; a remote binding with absent/empty store is unknown. Never select the fleet default to inspect an existing task. | Known remote runner/store remains identical through read; changed binding rejects. |
| `ProviderDialogObservation?` | omitted JSON Dialog, null result, bounded read timeout/unavailable: Unknown, original deadline. | Real serialized Blocked/Clear/Unknown values; Blocked alone is insufficient without valid identity. |
| `Guid SessionId`, `Guid RuntimeEpoch`; `DateTime? AcceptedStartedAt` | Guid.Empty epoch/session, null generation, default generation: Unknown. | Correct session/nonempty epoch/normalized matching generation; stale/newer generation or wrong session rejects. |
| `long OutputSequence`, `long InputRevision`; `bool InputHistoryKnown` | zero counters are valid initial values, not missing; negative counters invalid. False history is Unknown, including adopted/older peers. | Valid counters retained, newer read wins; epoch replacement invalidates old cache. No truthiness test on 0. |
| `ProviderDialogState`, `string? Kind` | Unknown/invalid enum -> no observed wait; null/empty kind on valid Blocked -> generic dialog. | ToolPermission, Question, Choice/trust/effort -> fixed appropriate label; arbitrary string -> generic, never echoed. |
| `bool? CanCancel`, `SubmittedInputProof? SubmittedInput` | null/false cancellation; absent proof: no auto-action, no loss of otherwise valid diagnosis. | true cancellation and present proof still authorize no input and prove no refinement receipt. These optional metadata fields are intentionally not input-policy reads. |
| Server completed-read timestamp `DateTime` in optional facts | no cached facts, malformed persisted history or restart: Unknown. Negative age invalid. | 0, 60, 90 seconds usable; >90 stale; failed refresh invalidates immediately; repeated access never extends age. |
| Transcript `LastEntry?`, accepted-prompt query, `ToolUseId : string?`, `ToolName : string?`, `Timestamp : DateTime?` | empty transcript, no accepted prompt, null/empty IDs/names: show generic attribution only; timestamp fallback stays CreatedAt. | Attributable unresolved ToolCall; matched result/new turn resolves episode. Multiple calls do not prove which command the screen blocks. |
| Pending message collection / `DeliveryVerdict?` | empty list -> none queued; null verdict or Pending with zero attempts -> not confirmed. | Sent+complete matching receipt -> confirmed. Head-only, echo and QueuedUserPrompt do not confirm this decision note. |
| Queue `DateTime? LastDeliveryGeneration`, `long? LastDeliveryBaselineSequence`, `DateTime? LastDeliveryStartedAt` | null generation cannot confirm this new kind; both floor fields absent cannot confirm. Baseline 0 is a valid floor, not absent. | Matching recipient generation plus a complete prompt above the recorded floor confirms; mismatched generation and older prompts do not. Preserve the existing timestamp-floor fallback when only sequence is absent. |
| `ParentSessionId : Guid?`, ReplyTo, destination row | None -> NotRequired; Session with null/empty/missing/stopped destination -> durable unavailable debt, no reroute. | Existing busy or idle caller receives only its keyed immutable note through the queue. |
| Prior Warning detail / notification optional link | no prior row, empty/malformed/unknown-version payload or missing QueueMessageId: no invented freshness; existing outbox recovery handles missing link. | Valid episode dedupes under task lock; lost queue-link acknowledgement reuses the one SourceLandNotificationId. |
| Recovery runner catalogue/slots optional result and session row | unavailable catalogue/list, no listed seat, absent session row: label unknown/no listed seat, never “released” from absence alone. | Live occupying seat gets explicit single-seat guidance; server-row-present and row-absent paths both preserve task failure and source. No automatic release RPC. |

### Delivery inventory

| Producer -> destination | Persistence and identity | Recovery / receipt witness |
|---|---|---|
| Permission pass -> captured caller session | Opening Warning and `ProviderInputWait` outbox row committed together, unique SourceEventId; immutable body/digest and destination. | Rollback yields neither; restart finds both. Queue row keyed by SourceLandNotificationId. V-6/V-7/V-8 exercise busy and idle callers, insert/link/wakeup failures and complete UserPrompt above the attempt floor in the correct generation. |
| Outbox -> safe caller input | Existing WhenIdle queue owns body, Enter, attempts and CARD-0680 guard. | Head-only, wrong session/generation, echo, QueuedUserPrompt, missing floor and Sent-only remain unconfirmed. An uncertain write keeps custody; no extra decision-note path writes keys. |
| Warning commit -> attention/task UI/Check reads | `AgentTaskChanged` after commit; persisted event/outbox and fresh observation facts are the source. | A dropped event does not lose the record; a reconnect reads it. Restart starts current facts Unknown until refreshed. V-9 checks note/digest/attention consistency. Event publication is not recipient receipt. |
| Existing overdue failure -> caller | Existing non-killing failure notification, including current diagnostic and recovery wording. | V-4/V-10 and existing caller-notified regression prove this still occurs; early warning must not set a completion stamp or suppress failure. |

### Proves it works now

All V-1..V-12 are planned single `[Test]` methods in partial
`PermissionWaitTests` under `tests/Antiphon.Tests/Application/` (one execution
each, with labeled internal subcases). TestDesign may split a costly method while
updating its exact row/count, but may not substitute an assembly filter.

| ID | Method | Decisive oracle / red witness |
|---|---|---|
| V-1 | `C1518_CapturedDialogNeedsDecisionBeforeStall` | Replay the exact captured permission frame through CARD-0680, seed one current ToolCall and a queued zero-attempt refinement, with no tool launch invoked. At first eligible tick/60-second cadence, one Warning and outbox note exist with literal decision text; task Working, no input/kill/restart, source marker and queue unchanged. Baseline red: no early Warning/note. |
| V-2 | `C1518_OptionalObservationInputs` | Actual contract deserialization plus reader/dispatcher cases for the optional-input table. Only positively current Blocked classifies. Invalid/null/stale cases preserve original deadline and never throw. Omit identity/history/age checks in separate PCs to make their labeled case red. |
| V-3 | `C1518_ObservationBindingRacesAndExpiry` | Change attempt, session generation/binding, epoch and competing read ordering across the await; repeat at age 90s and just after. Old facts cannot publish/revive; failed refresh invalidates; caller cancellation propagates; no refresh-on-access. Bypassing each fence makes its visible-event/current-facts assertion red. |
| V-4 | `C1518_NinetyMinuteBoundaryKeepsCustody` | ToolCall at 89:59, exactly 90:00, and >90 with current modal; unchanged 90-minute clock, current permission wording only on boundary failure. A vanished/unknown modal on final read uses original classification. Task fails without input/stop/retry; queue, owner row and worktree survive; early note did not stamp completion. Baseline red: failure misdiagnoses local execution. |
| V-5 | `C1518_RealChildAndOtherClocksRemainBounded` | A real owned child remains alive, no dialog: original LocalExecution at 90m. Include tighter role ceiling, disarmed limits, question on a model-wait tail, idle/compaction and newer ToolResult controls. No tool census controls classification. Suppressing or extending budgets makes the expected original verdict red. |
| V-6 | `C1518_EpisodeAndNoteCommitOnce` | Concurrent scans, restart, output animation and unknown refresh produce one opening event/note; Clear then reappearance/new turn can produce a new episode. Rollback leaves neither; changed task/binding under lock writes neither. Bypass dedupe/atomic persistence to fail cardinality/paired-commit assertions. |
| V-7 | `C1518_DecisionReachesBusyAndIdleCaller` | Real queue with busy then eligible and already idle callers; only captured destination gets complete immutable UserPrompt. No completion stamp; full later Check/failure still deliver. No routing for ReplyTo=None; unavailable destination retains debt. Wrong kind/destination/receipt acceptance makes its assertion red. |
| V-8 | `C1518_NoteRecoveryRetainsOneQueueRow` | Inject failure before insert, after queue commit/before link, and after link/before wakeup. Reconcile after service restart; one linked authoritative row and one whole recipient receipt. Uncertain typed input never causes a second body; partial/wrong-generation/QueuedUserPrompt/Sent-only evidence cannot confirm. PCs target link recovery, keyed insert and receipt predicates separately. |
| V-9 | `C1518_CheckAttentionAndStallAgree` | Permission, Question and generic Choice all project the same current facts in Check, attention and a qualifying stall incident. Below 30m only provider wait surfaces; at 30m generic stall wording is qualified. Clear/stale/restart removes current permission claim; no raw command/unknown kind leaked. No synthetic Working, delivery or card transition. Removing each consumer mapping makes that surface disagree. |
| V-10 | `C1518_FailedSeatRecoveryDoesNotResurrect` | Failed task plus retained live seat, both present/missing server session rows: Refine refused, pending row/source preserved, explicit observed-runner guidance. No release call from scan/Check/attention. Separately call the existing operator endpoint on the isolated fake runner, verify authority/audit and no worktree removal; unprivileged call refuses. This proves route plumbing, not native process-tree termination. |
| V-11 | `C1518_CadenceAndReadFailuresAreBounded` | Fake clock/read barrier counts per-session reads: no scan before due, due at60, shared concurrent reads, max four in flight, one failed peer does not block others; sparse transcript still admitted. Missing optional rows are exercised at the actual producer/consumer boundary. No live fleet reads. Removing cadence or per-task isolation makes counts/later-warning assertion red. |
| V-12 | `C1518_DetectionHasNoTerminalSideEffects` | Positive Blocked input through each changed path with recording input, launch, stopper, release and workspace collaborators: zero calls; Working/Failed status follows only existing deadline rule. Record positive Warning/attention first so a no-op implementation cannot pass. Any introduced key/restart/stop/cleanup makes this test red. |
| V-13 | client `ProviderInputWait.test.tsx`, test names prefixed `C1518` | Render existing attention panel/visual and home-rail mapping: observed permission vs unknown history, navigation only, terminal failure guidance; no approval/reply-to-terminal action and no new column. Removing the kind mapping or showing an intervention action is red. |

### Regression selection

- **R-1:** `TaskDeadlinePolicyTests.a_running_local_tool_takes_the_much_longer_local_execution_deadline`,
  `a_warm_pool_session_is_never_charged_for_a_stall_it_inherited`, and
  `a_compaction_continuation_prompt_is_not_a_stalled_model_call`.
- **R-2:** `AgentTaskOverdueDeadlineTests.an_overdue_pool_delegate_keeps_its_row_idle_while_the_session_lives`,
  `an_unresolved_api_error_recovery_stands_the_sweep_down`,
  `a_working_boot_stall_warns_without_failure_stop_or_retry`, and
  `the_caller_is_told_rather_than_left_to_discover_it`.
- **R-3:** `ClaudeDialogDeliveryTests.C680_QueueAndExpectationKeepDialogInputPending`;
  existing direct-send guard/typed-body custody stays intact. V-7/V-8 independently
  verify the new producer through that queue, rather than inferring it from R-3.
- **R-4:** `TaskProgressStallSweepTests.A_stalled_task_is_never_killed_escalated_or_failed_by_the_stall_sweep`
  and `RunnerSlotEndpointTests.Force_release_without_the_operator_token_is_forbidden_even_from_loopback`.

### Positive-control proposal

One PC per independently bypassable behavior; TestDesign must expand grouped
identity/receipt cases into separately identified guards/variants before Code.
Do not map several guards to a single mutation merely because one method covers
them. All controls are pending for post-land SourceLanding Mutation, not Plan or
ordinary Code. Bind to exact `/*/*/PermissionWaitTests/<method>` selections.

| PC | Compiling defect | Exact V method / intended failure |
|---|---|---|
| PC-1 | Remove the early pass from dispatcher | V-1: no opening Warning before 30m. |
| PC-2 | Treat a missing Dialog as observed permission | V-2: absent-object case falsely produces wait. |
| PC-3 | Skip accepted-generation comparison | V-3: old generation publishes a current decision. |
| PC-4 | Permit an older competing read to replace the newer facts | V-3: obsolete Blocked revives after Clear. |
| PC-5 | Renew freshness when cached data is read | V-3: age >90 still claims current wait. |
| PC-6 | Keep ToolCall classified LocalExecution despite current modal | V-4: permission failure wording/kind absent at90. |
| PC-7 | Pause the local clock on Blocked/Unknown | V-4: boundary does not fail; V-5 uses the corresponding Unknown variant. |
| PC-8 | Remove episode deduplication under task lock | V-6: concurrent/restart opening count exceeds one. |
| PC-9 | Commit Warning before creating the note in a second transaction | V-6: injected crash leaves an unnotified opening. |
| PC-10 | Use TaskCompletion for the early note | V-7: completion stamp/Check suppression changes. |
| PC-11 | Ignore keyed row recovery after lost link acknowledgement | V-8: duplicate row/body or missing complete receipt. |
| PC-12 | Accept QueuedUserPrompt/head-only/wrong-generation receipt | V-8: each separately applied variant falsely confirms. |
| PC-13 | Omit the current-facts branch from Check/attention/stall | V-9: separately applied consumer variants disagree. |
| PC-14 | Allow Refine on the Failed task | V-10: terminal task is mutated or a new queue row appears. |
| PC-15 | Remove read cadence/peer error isolation | V-11: separately applied variants exceed count or omit second task. |
| PC-16 | Add a cancellation key on observed wait | V-12: positive diagnostic plus nonzero input. TestDesign must also bind any independently accessible stop/release collaborator. |
| PC-17 | Omit client kind mapping or enable a modal-action button | V-13: separately applied variants lose label or show unauthorized action. |

Red must be a reached behavior assertion, never compilation/fixture failure,
zero tests or a timeout substituted for a refusal. Restore each mutation and
rebuild before exact-method green. Preserve each variant's tested SHA, counts,
assertion and restoration evidence. No claim of PC-clean before Mutation.

### Execution and cost

S1 runs only V-1/V-4 red, against reachable existing APIs; expect two intended
assertion failures. All other new tests are committed before their production
fixes and have the named later PC witness; no extra full-project red build per
test. After S2–S4's final commits, CP-2 builds **Antiphon.Tests once**; CP-3..CP-7
reuse that exact output at the same After group/source. CP-8 is the portable
client lane, not another .NET build. No whole namespace, Unit lane or native
provider run. One initial checkpoint-tool bootstrap is separate setup cost if
no usable tool is present; report it, gated, rather than hiding it.

Estimated ordinary checkpoint floor is **26 minutes** (two 9-minute isolated
test builds included), plus at most **2 minutes** tool setup if needed; no slot
wait is included. Authoring is **165–225 minutes** across four resumable slices.
Provisional PC floor is **90 minutes** (build/restoration plus the 17 controls and
listed variants); TestDesign replaces this with its expanded numeric manifest.
Verification total proposal: **116 minutes**, or **118 with tool setup**, before
slot waits, separate Review/activation work and authoring. Sharing CP-2 across
the five remaining .NET rows avoids five redundant test-project builds (roughly
40 minutes at an eight-minute build allowance). No saving excuses a missing row.

Run the checkpoint tool with `run --plan <this-path> --rows <slice-row-ids>
--expected-source-sha <full-sha>`, then `wait` on the same run until exit is not 75.
Every build/test driver uses the host build-slot gate; the checkpoint runner
takes it itself. Client wrapper is gated as shown below. Exit 4 is not-run, never
permission to bypass the gate. Commit/push before a long run and keep source
frozen while it runs. Preserve unedited CHECKPOINT lines and fresh TRX/JSON
receipts in ignored evidence; Code/Review also run `scripts/check-evidence-diff.ps1`
over base..HEAD. Verify inherited red at base with its exact method. Clean up
owned `bin-.../` outputs after retaining evidence, and never touch daemon bins.

### Checkpoints

Closed proposed list for TestDesign to finalize. CP-1 is intentionally red;
CP-2..CP-8 are the complete ordinary green selection. Every .NET row uses the
**portable .NET/PostgreSQL lane**; CP-2 also contains the bounded locally inherited
child test. CP-8 uses the **portable client/Vitest lane**. No host is pinned.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1518-red/` | incident-red | `/*/*/PermissionWaitTests/(C1518_CapturedDialogNeedsDecisionBeforeStall*)\|(C1518_NinetyMinuteBoundaryKeepsCustody*)` | V-1, V-4 | 2 reached assertion failures, 0 skipped | 2 | 9 |
| CP-2 | S2-S4 | `tests/Antiphon.Tests -> bin-c1518-green/` | observation-deadline | `/*/*/PermissionWaitTests/(C1518_OptionalObservationInputs*)\|(C1518_ObservationBindingRacesAndExpiry*)\|(C1518_NinetyMinuteBoundaryKeepsCustody*)\|(C1518_RealChildAndOtherClocksRemainBounded*)` | V-2..V-5 | 4 passed, 0 failed/skipped | 4 | 9 |
| CP-3 | S2-S4 | CP-2 | episode-delivery | `/*/*/PermissionWaitTests/(C1518_CapturedDialogNeedsDecisionBeforeStall*)\|(C1518_EpisodeAndNoteCommitOnce*)\|(C1518_DecisionReachesBusyAndIdleCaller*)\|(C1518_NoteRecoveryRetainsOneQueueRow*)` | V-1, V-6..V-8 | 4 passed, 0 failed/skipped | 4 | 2 |
| CP-4 | S2-S4 | CP-2 | surfaces-recovery | `/*/*/PermissionWaitTests/(C1518_CheckAttentionAndStallAgree*)\|(C1518_FailedSeatRecoveryDoesNotResurrect*)\|(C1518_CadenceAndReadFailuresAreBounded*)\|(C1518_DetectionHasNoTerminalSideEffects*)` | V-9..V-12 | 4 passed, 0 failed/skipped | 4 | 1 |
| CP-5 | S2-S4 | CP-2 | deadline-regression | `/*/*/TaskDeadlinePolicyTests/(a_running_local_tool_takes_the_much_longer_local_execution_deadline*)\|(a_warm_pool_session_is_never_charged_for_a_stall_it_inherited*)\|(a_compaction_continuation_prompt_is_not_a_stalled_model_call*)` | R-1 | 3 passed, 0 failed/skipped | 3 | 1 |
| CP-6 | S2-S4 | CP-2 | ownership-regression | `/*/*/AgentTaskOverdueDeadlineTests/(an_overdue_pool_delegate_keeps_its_row_idle_while_the_session_lives*)\|(an_unresolved_api_error_recovery_stands_the_sweep_down*)\|(a_working_boot_stall_warns_without_failure_stop_or_retry*)\|(the_caller_is_told_rather_than_left_to_discover_it*)` | R-2 | 4 passed, 0 failed/skipped | 4 | 1 |
| CP-7 | S2-S4 | CP-2 | queue-stall-authority | `/*/*/(ClaudeDialogDeliveryTests*)\|(TaskProgressStallSweepTests*)\|(RunnerSlotEndpointTests*)/(C680_QueueAndExpectationKeepDialogInputPending*)\|(A_stalled_task_is_never_killed_escalated_or_failed_by_the_stall_sweep*)\|(Force_release_without_the_operator_token_is_forbidden_even_from_loopback*)` | R-3, R-4 | 3 passed, 0 failed/skipped | 3 | 1 |
| CP-8 | S2-S4 | n/a | client-decision | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1518-client -- pwsh -NoProfile -File scripts/test-client.ps1 ProviderInputWait.test.tsx -t C1518` | V-13 | all named client assertions passed; no full suite | n/a | 2 |

--- next stage ---
next: test-design
handoff: Bind CARD-1518's narrow optional-input, outbox-delivery and deadline witnesses to real fixtures; expand independently bypassable PC guards and confirm counts/cost. Retain CARD-0680 types, unchanged deadlines, existing operator recovery and separate CARD-1517 key qualification. S1 incident red tests are first to Code after this verification amendment.
artifact: docs/superpowers/plans/2026-10-23-card-1518-permission-wait-classification-plan.md
