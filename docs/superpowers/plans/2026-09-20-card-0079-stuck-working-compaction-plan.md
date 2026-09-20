# CARD-0079: recover the fourth stuck-Working interpreter outage

Plan task: `15587e3f`, 2026-09-20. Source: investigation task `e78ba0b0`,
commit `954f98db85ac99425bbbcde2675694fe8d6ac303`,
[fourth recurrence investigation](../../investigations/2026-09-20-card-0079-4th-interpreter-down-recurrence.md).
Code references below are against that commit. Its parent code is `3c7a4057`.
The requested investigation branch is occupied by another worktree; this plan's
branch, `feat/card-task-15587e3f`, starts at the exact requested investigation SHA.

Amendment task: `fed4b83c`, 2026-09-20, based on plan commit
`eb3a3fddb0dd26c8f79b1def89f05bab488d4158`. The operator explicitly authorized
automatic unattended restart for this specific silent post-compaction pattern.
That settles the former D-1/D-3 fork; this amendment supersedes the original
operator-only design. The original branch remains checked out elsewhere, so the
amendment is authored on `feat/card-task-fed4b83c` at that exact base and published
as a fast-forward to the requested plan branch.

This is a plan, not an implementation or an operational recovery receipt. No live
session was stopped or restarted and no check was sent by this Plan dispatch.
TestDesign remains a separate stage; the proposed tests and positive controls
below are its input, not executed evidence.

## Outcome and completion boundary

Automatically stop and restart an eligible AlwaysOn Claude Check seat when its
current unfinished Check turn has an explicit auto `CompactBoundary`, its
synthetic continuation, no subsequent `TurnEnd` or useful progress, and at least
ten minutes of silence. Commit an audit record before the automatic action;
conditionally stop only that accepted generation, then strictly resume the same
conversation once. Release obsolete untyped Check occupants and prevent new work
from entering the retiring generation. Never make a still-running turn look idle.

The exception does not extend to general stalls, other AlwaysOn jobs, transient
delegates, or long turns that have made progress after compaction. Operator Stop
and all existing holds still win. The immediate Stop/Fresh runbook below remains
available independently, including when automatic recovery refuses or its budget
is exhausted. A launch acknowledgment is not proof that Check service recovered.

## Ground truth

| Card assumption or tempting inference | What the supplied evidence and code actually establish | Design consequence |
|---|---|---|
| Haiku is held again. | Investigation: zero active holds, zero `haiku is held` events; 0/93 successful Checks and 91 unavailable incidents in its overnight window. | No model-availability edits, hold clearing, or implicit rerouting. Historical counts are not a new live census. |
| A dead migrated session occupies the interpreter. | The occupant and specialist bind to the same live session and accepted start time. `PlaceOnStandingAgentAsync` already scopes occupancy to the live session (`AgentTaskDispatcher.cs:5150` vicinity). | Preserve the original CARD-0079 generation/identity protections. |
| Auto-compaction ought to mean idle. | `SessionMessageQueueService.cs:4308-4405` deliberately leaves auto/unknown boundaries neither activity nor turn ends. The ordinary prompt at seq 1255 outranks TurnEnd 1254; boundary 1256 and synthetic prompt 1257 do not finish it. | Never convert an old auto boundary into a synthetic TurnEnd or age `IsWorkingAsync` to false. |
| The provider certainly died. | No output follows this boundary in the investigation; why is unknown. `lastSeenAt`, a running process, and a rendered old brief cannot prove model progress or its absence. | Name the condition `CompactionContinuationStalled`, not a proven crash or delivery failure. |
| The 10-minute watchdog will release the occupant. | D8 at `AgentTaskDispatcher.cs:1487-1498` defers Pending + Working indefinitely. D9 at `:1582` independently withholds a kill on Working. | Handle the recognized episode explicitly; do not simply remove D8 or fall through to the generic never-delivered kill tail. |
| The model-wait deadline covers any long Working state. | `TaskDeadlinePolicy.ClassifyPhase` does not classify CompactBoundary. `LoadLastEntryAsync` can select by timestamp rather than sequence; the observed failures name CompactBoundary despite continuation seq 1257. | Detect the ordered episode, not just `last.Kind == UserPrompt`; leave general phase/role deadlines alone. |
| Canceling the caller cancels the occupant. | `SpecialistTaskRunner.CancelIfStillQueuedAsync` cancels only Queued tasks. Dispatched occupants survive the 60-second caller wait and reached the 240-minute ceiling. | Bound affected occupancy independently of the caller's wait. Do not change the global wait or ceiling. |
| The queue is stuck retrying Enter. | Investigation: 50 Pending rows, all with zero attempts; the old CARD-0501 head was canceled. `CancelDeadBriefsAsync` runs inside delivery, which Working prevents. | Clean proven obsolete untyped briefs without requiring a delivery attempt or an idle session. Preserve attempted-composer recovery. |
| Any transcript catch-up return proves a successful fresh observation. | `AgentSessionRuntime.CatchUpTranscriptAsync:690` returns whether it stored new entries. `false` means either no new entries or a swallowed failure. The dispatcher's wrapper also swallows failure. | Introduce an explicit observation-success result for this detector; absence after an unsuccessful pull cannot confirm a new episode. |
| The new request graph is the only Check path. | `AgentTaskCheckService.RoutedAsync` selects it only for a configured qualified chain; `SpecialistTaskRunner.RunAsync` remains the legacy primary path. | Gate both paths and the final standing dispatch, including the compatibility slug seat. |
| Stop then Start is already a safe unattended restart API. | `AgentControlService.StopAsync:976` records `SuspendedByUser`; `StartAsync:143` rejects automatic Fresh/history selection. The supervisor resumes with `Fresh:false` and its ordinary failure ladder. | Add an internal episode-bound strict-resume path. Do not call human Stop, pretend `automatic:false`, permit automatic Fresh, or let the supervisor independently relaunch during this action. |
| A generation check also guards against late output. | `SessionRunnerRuntime.KillGenerationAsync:779` locks launch identity only. The conditional-input path also checks output revision, but neither is a compaction-specific stop predicate. | Add a narrow conditional compaction-stop contract; recheck transcript/binding and observed output at the runner immediately before signaling the process. No fallback to unconditional kill. |
| Fetching the runner snapshot guarantees its native tailer is current. | `ITranscriptTailer.Snapshot()` returns everything parsed so far; it is not a fresh read-to-end receipt. | Require a new successful bound-tail observation with a consumed-file watermark. A paused/unbound/erroring tailer cannot authorize stop. |
| Existing incidents provide a durable restart budget. | `AgentIncident` is pruned (normally 30 days / 500 per agent); `AgentSupervisionState` failure counts reset after healthy uptime. | Keep episode state and the automatic-compaction restart budget separately; incident pruning, uptime, process/server restart and task settlement cannot reset them. |
| Failure or a launch acknowledgment proves recovery. | Existing checks settle on correlated reports. Delivery and caller receipt require whole matching UserPrompt evidence. Health or `Sent` alone does not prove either. | Close recovery only on a new, useful Check reading and its caller receipt. |

## Decisions

D-1/D-3 record the supplied operator decision. The remaining decisions define the
narrow implementation of that authorization; none is a pending approval gate.

- **D-1 — Authorized automatic restart, with a named exception.** The operator's
  `fed4b83c` brief expressly replaces operator-controlled recovery for the exact
  silent compaction-continuation condition. The service may terminate that old
  turn and resume its process unattended after all guards below pass. Keep
  [AGENTS.md](../../../AGENTS.md)'s standing no-auto-kill rule verbatim and add a
  narrowly worded CARD-0079 exception/link when implementing; document the same
  scope in [ops-http.md](../../ops-http.md#killing) and the runtime owner. Rejected:
  detection-only as completion of this card, or a generic Working-age kill rule.
- **D-2 — AlwaysOn Check seats only.** Require physical seat and logical owner
  AlwaysOn, neither pool-owned nor suspended; effective ClaudeCode execution;
  current standing ownership; and `StandingSpecialistSeatPolicy` Check identity.
  A configured-slug legacy primary additionally needs positive evidence that the
  unfinished prompt is its own correlated Check, not merely a matching name.
  Declared qualified Claude Check alternates qualify independently. Require no
  card/non-Check assignment, interactive human turn, outstanding tool call, or
  unresolved attempted queue input. Exclude other standing jobs, ephemeral work,
  Codex/Grok, manual/unknown compaction and idle sessions. Rejected: treating
  AlwaysOn alone, a slug alone, or an old boundary anywhere in history as license.
- **D-3 — Authorized ten-minute silent bound.** Add typed
  `Delegation:CheckCompactionContinuationWaitMinutes`, default **10**. Accept zero
  (disable new discovery and any not-yet-issued automatic stop/resume) or values
  at least 10; reject negatives and 1–9. At the default, act at elapsed >= 10:00,
  never at 9:59.999, measured by the formula below. A later response permanently
  disqualifies that boundary; do not reset a generic inactivity timer. This is an
  operator-selected bound, not proof that a silent provider cannot still be
  computing. Keep all caller/delivery/role deadlines unchanged. Rejected: aging
  every Working turn, or killing ten minutes after any historical compaction.
- **D-4 — A separate episode, never a fake idle verdict.** Persist an episode
  bound to the physical agent, session, accepted `StartedAt` and boundary identity.
  A recovery-needed session can still truthfully report `working=true`.
  Rejected: toggling session status to Stopped while its process lives, setting a
  model hold, reusing the boot-liveness latch, or disguising a compaction stall as
  missing native history in the continuity hold.
- **D-5 — Automatic strict resume; Fresh remains the operator repair.** Restart
  means stop generation G and launch G2 of the same session/native conversation,
  using `Fresh:false`, no initial prompt, and existing ownership/continuity rules.
  Do not replay the delivered pre-compaction Check or synthesize its success.
  This preserves CARD-0466; authorization to restart does not require discarding
  history. The previous resume was followed by another compaction after roughly
  25 minutes, so a repeated stall must not become a resume loop. The independent
  runbook uses explicit Fresh to test that stronger remedy. Rejected: automatic
  Fresh/history fallback, raw Enter/Esc, `/compact`, or a repeated old brief.
- **D-6 — Cancel obsolete input, retain possible delivery evidence.** Only linked,
  terminal Check briefs proven never attempted may be canceled automatically.
  Do not move historical Checks to the new session for replay. Rejected: deleting
  the whole queue, treating every Pending row as untyped, or sweeping human input.
- **D-7 — Preserve routing and notification contracts.** A stalled physical seat
  is ineligible. An already declared, qualified alternate may serve through the
  existing policy; do not invent one or alter pins/holds. With no usable seat,
  ship the existing degraded digest with the specific recovery-needed reason.
  Caller-facing failures use the existing durable delivery obligation.
- **D-8 — Separate TestDesign.** This amendment requests updated slices/guards/PCs,
  not folded verification. D-1/D-3 are settled; send this artifact directly to
  TestDesign to finalize executable coverage and the ordinary verification profile.
- **D-9 — One bounded action, durable across restarts.** Allow one stop/resume
  operation per episode and at most one automatic compaction restart per physical
  agent in a rolling 24 hours. Another automatic restart additionally requires a
  useful Check result and whole caller receipt after the preceding one. Consume
  the allowance at durable stop-request commit; never refund an ambiguous action.
  A refused/failed resume or a second episode before both conditions are met
  holds the seat for an operator, with a reason. Time alone, incident pruning, server
  recreation and generic healthy uptime never release this hold. Reject an
  unbounded supervisor retry ladder driven by this exception.
- **D-10 — Audit before effects; uncertainty withholds effects.** Persist the
  evidence and action intent before contacting the runner, retain phase outcomes,
  and publish incidents/attention from that durable record. Unknown ownership,
  stale transcript, stop outcome, or launch identity blocks further destructive
  action. A failed read is never evidence of silence. A refusal remains visible;
  it is not reported as a successful unattended restart.

## Immediate operator unblock (independent of shipping the fix)

This procedure is for the caller's operator/operations dispatch. It uses existing
front doors; it is not a request to change database rows by hand. Capture the
results and UTC times in a tracked incident follow-up when performed.

1. Resolve the full agent ID from `GET /api/agents` for
   `antiphon-check-interpreter`. Compare its current session and accepted start
   against the investigation's `cea73d57-3072-4cc2-8cb0-ab859e7d3415` and
   `2026-09-14T22:50:07.718176Z`. Read current server `/api/version`, model
   availability, the scoped task list, session queue, server transcript and runner
   transcript/snapshot. Do not use the six-day-old snapshot as present ownership
   authority. If the session changed or now completes turns, reassess this
   procedure before stopping the replacement.
2. Save a bounded evidence tail, queue IDs/task links/attempt metadata, and active
   Check task IDs. Verify the ordinary prompt -> auto boundary -> continuation ->
   no reply/end pattern after fresh transcript collection. Inspect any other
   active work on the physical seat before deciding what can be canceled.
3. `POST /api/agents/{agentId}/stop` through the server. Verify supervision is
   suspended and the reviewed process is stopped before continuing. Stop acts on
   the agent's current session; re-read ownership immediately before it. Do not
   use runner kill-all, delete the agent, or restart AppHost for this recovery.
4. Settle/cancel only the obsolete interpreter Check task IDs found in step 2,
   re-reading their status first. Existing route:
   `POST /api/agent-tasks/{taskId}/cancel`. Cancellation may stop a delegate, so
   do it before launching the replacement and never after a stale task has been
   rebound. Let normal reconciliation settle already terminal rows.
5. Read `GET /api/sessions/{oldSessionId}/queue` again. For Pending briefs linked
   to those now-terminal Check tasks, confirm all attempt metadata is empty,
   then cancel their exact IDs with
   `DELETE /api/sessions/{oldSessionId}/queue/{messageId}`. The endpoint itself
   allows any Pending row, so the operator must not bulk-delete by status alone.
   Retain unrelated human/system input and attempted rows for individual review.
   Resolve a `standing_resume_delivery_pending` refusal through those controls;
   do not bypass it with SQL or erase evidence.
6. `POST /api/agents/{agentId}/start` with JSON `{ "fresh": true }`. Verify a
   different session ID, a new accepted generation, live runner ownership,
   restored supervision, expected Claude/haiku identity, and successful launch.
   Preserve quota, provider, continuity and Herdr refusals; report one if encountered.
   No silent model substitution or repeated fresh-launch loop.
7. Observe the next real scheduled Check against current work (or use the existing
   authorized check workflow in an operations dispatch). Require its whole brief
   in the replacement interpreter's UserPrompt, a correlated nonempty reading and
   closing report/TurnEnd, `Role=Check` Succeeded, and the complete resulting note
   received by the intended parent session. A direct "hello" or a blank qualification
   result is not this acceptance test. Do not resend the 50 stale observations.
8. Record the first valid reading and then a window of at least ten consecutive
   settled real Check requests, with succeeded/failed/canceled totals and unavailable
   incidents. Retain longer observation through the next natural auto-compaction;
   ten successes alone do not prove the permanent fix. If Fresh also cannot answer,
   capture its new transcript and launch diagnostics and return to Investigate.

## Detection and state transitions

### Evidence policy

Add a read-only `CompactionContinuationPolicy` and a scoped
`CheckCompactionContinuationService`. The policy returns a structured verdict
with session/generation, ordinary prompt sequence, boundary sequence, effective
wait start, elapsed duration, and reason. It consumes the existing working
verdict rather than reimplementing `IsWorkingAsync`.

The automatic trigger is the conjunction of all these facts, never just its timer:

1. D-2 scope and current ownership hold; DB status is Running and the existing
   transcript-derived verdict is Working. Seat/owner and Check interpretation
   remain enabled, unsuspended, without liveness, continuity, Herdr, provider,
   quota or capacity hold. Both generation tokens are present and equal.
2. In that generation, after its latest effective end, the owning ordinary
   UserPrompt is a positively correlated Check; explicit `(auto)` CompactBoundary
   B follows it; the known synthetic continuation prompt C follows B. The original
   Check may already be terminal, as in the investigation; its identity must
   still be provable. Missing C or an ambiguous prompt does not qualify.
3. No TurnEnd follows B. There is also no interrupt/manual-compact/restart end,
   AssistantText, Thinking, ToolCall, ToolResult, ordinary UserPrompt, error/wall
   or unclassified substantive activity after B, except C. A later substantive
   record disqualifies B permanently, even if that later work itself goes quiet.
   Repeated auto boundaries must form a still-silent chain with a continuation
   after the newest boundary; they postpone eligibility, never accelerate it.
4. `now - waitStart >= N minutes`, where N defaults to 10 and
   `waitStart = max(acceptedStartedAt, B.Timestamp, B.CreatedAt,
   C.Timestamp, C.CreatedAt)`. Ignore null event time only under the ownership
   fence below; arrival time remains mandatory. Both DB and a successful fresh
   runner observation satisfy the same evidence predicate before action.

There must be no unmatched in-flight tool call before B either. Pending human,
Channel, Scheduled, unknown-origin or unrelated machine input, open non-Check/card
work, or unresolved attempted/Sent input veto automatic stop rather than being
discarded. Historical transcript-confirmed Sent Checks are retained as history;
the delivered Check that owns B is explicitly the turn authorized for termination.

These guards spare a legitimately long continuation that emits thinking/text or
tool activity, and all long non-Check turns. No observable test distinguishes
silent internal provider computation from this outage with certainty. The
operator's ten-minute choice accepts that residual risk only for this bounded
Check pattern; a spinner, `lastSeenAt`, CPU use, or old screen text never proves
either progress or failure. A changed output revision at final stop preflight
conservatively defers and requires another observation; it does not become
delivery evidence or start a generic inactivity countdown.

Respect both sequence and timestamp evidence: catch-up can append historical rows
above current rows. Anchor in the current launch/last effective end, reject an
auto boundary proven to predate it, and do not treat a backfilled stale row as a
new unfinished turn. For missing or conflicting ordering evidence, return
Unknown rather than confirming a stall. Null event timestamps may use CreatedAt
only where the current-generation sequence/end fence establishes ownership.
Use the later event/arrival time to delay suspicion on newly imported records;
future times do not breach. Exact 10:00 is overdue, 9:59.999 is not. Multiple
auto boundaries select the newest still-silent episode and restart its grace.

On a stored overdue suspect, pull a fresh bound transcript, persist it without
queue side effects, and re-evaluate. Add an observation result distinguishing
success/no-change from unavailable, unsupported, unbound, stale-tail and partial
persist failure; retain the old boolean API's meaning for existing callers.
Require the Claude tailer to complete a read-to-end pass of the currently claimed
file, with a stable binding identity and consumed byte watermark. A partial line,
skipped/unparsed new record, claim switch, unreadable file or outstanding persist
failure is Unknown. The result carries runner generation, native boundary/prompt
identity, transcript revision and output revision; DB arrival sequence alone is
not a native event ID. Do not assume `Snapshot()` already has this contract.

Read ownership before and after observation and recheck under delivery/standing
start locks at commit. No DB transaction spans runner I/O. Do not call full Sync
inside a delivery lock, since it can re-enter queue flushing. The final runner
stop check below closes the ordinary catch-up-to-stop race for observable progress.

### Durable episode and lifecycle

Use a new `CheckCompactionRecovery` entity instead of overwriting latest-episode
fields on the session. Its unique key is `(PhysicalAgentId, SessionId,
AcceptedStartedAt, BoundaryIdentity)`; store DB boundary/continuation sequences
alongside native identities. Carry prompt/task correlation, boundary/arrival
times, configured threshold, detected/observed times, bounded evidence, state,
reason, attempt ID, stop-request/outcome times, resume target/generation,
launch outcome, and useful-Check/caller-receipt identities. Append enum values;
do not change CompactionRecoveryWatermark, model holds or boot-liveness state.
Persist phase transitions and their incidents atomically. Keep unresolved rows
and referenced evidence out of ordinary retention. Retain terminal action audit
at least 90 days; keep the last automatic-attempt timestamp and receipt-gated
eligibility on AgentSupervisionState independently of incident/episode pruning.

An `ActiveCompactionRecoveryId` on AgentSupervisionState and row-locked
compare-and-set transitions serialize sweep, restart, supervisor and duplicate
observations. Only one operation can own stop/resume at a time. Additional detected
episodes may be recorded for audit but cannot claim that slot; AwaitingCheck keeps
the last-attempt receipt gate without hiding a new episode from detection.
The operation blocks automatic start by other mechanisms until resolved; it does
not set `Suspended`, which remains human intent. A restart cannot reset D-9.

Run the service before the delivery watchdog and new dispatch, at most once per
minute with TimeProvider, including seats with no pending tasks. Its existing-action
reconciliation must run before the dispatcher's Delegation-enabled early return;
discovery/effects remain gated by that setting. Isolate failures per seat.
Reconcile existing actions even when discovery is disabled; zero or
disabled delegation prevents any next not-yet-issued stop/resume and records
`DisabledNeedsDecision` if a stop already happened. Never forget in-flight effects.

| State/transition | Durable fact and permitted side effect |
|---|---|
| Suspected (read-only) -> Confirmed | Fresh observation and locked predicate pass; write episode plus Warning `CompactionContinuationStalled`. Admission closes only for this generation. |
| Confirmed -> AbortedProgress / Superseded | Late progress/end or changed ownership before stop. Record why; do not stop or resume. A new generation is not marked stalled by old evidence. |
| Confirmed -> NeedsDecision | A safety veto, D-9 budget, unsupported runner, or permission/hold blocks action. Record the precise reason and zero automatic effects. |
| Confirmed -> StopRequested | Recheck intent, scope, silence and queue; atomically claim the one allowance, attempt ID, expected generation and observation plus Warning `CompactionContinuationRestartRequested`. Commit before RPC. |
| StopRequested -> Stopped | Conditional runner result positively confirms target generation exited. Stamp distinct `SessionTerminationSource.CompactionContinuationRecovery`; never `OperatorRequest`. |
| Stopped -> ResumeReserved | Existing start composition/reservation accepts a strict same-ID resume and atomically records new generation G2 on the episode; no new native history and no prompt replay. |
| ResumeReserved -> AwaitingCheck | Matching launch completes. Open admission for G2; preserve the old episode and record `RestartedAwaitingCheck`, not Recovered. |
| AwaitingCheck -> Recovered | A newly created Check has whole interpreter UserPrompt, correlated useful reading/report/TurnEnd and whole caller-note receipt. Record those IDs, then release the receipt half of D-9 (24-hour limit still applies). |
| Any owned action -> NeedsDecision / SupersededByOperator | Unprovable stop, failed/refused launch, repeated episode, or human Stop/selection. No general supervisor retry or Fresh fallback. Explicit human recovery may supersede the operation without resetting the rolling allowance. |

### Conditional stop, strict resume and crash cuts

Add a dedicated runner capability `compactionContinuationStopV1` and narrow
`POST /sessions/{id}/stop-compaction-continuation` request. It carries attempt ID,
expected accepted generation, native boundary/continuation identity, threshold,
and the successful observation's transcript/binding/output revisions. Only the
server recovery service calls it; no generic public `force`/`autoRestart` flag.
The runner validates Claude format, the bound fresh-tail result and silent
pattern under its existing launch gate, then checks revisions again at the
termination boundary. Use a test barrier immediately before the final check.
Any new record, output, end, binding or generation change refuses without
termination; do not convert a refusal into a Failed/Stopped session row.
Unknown, missing, legacy capability or timeout never falls back to `/kill`,
`kill-generation`, raw input or process-name termination. Existing kill-generation
semantics for other callers stay unchanged. Serialize accepted stop with output/
transcript observation so already-observed progress wins; a provider can still
produce its first token after the stop decision, the residual risk stated above.

Add an internal episode-bound resume entry in AgentControlService, using the
existing strict `Fresh:false` reservation, ownership and launch queue. Do not
pretend it is a manual Start or weaken automatic Fresh refusal. Carry the episode
ID through reservation and the queued launch, and revalidate it, current pointer,
old generation, D-2 intent and all normal launch gates before launch side effects.
No quota/auth/model override is granted. Ordinary supervisor and capacity recovery
stand down for an action-owned seat, including after a process restart. No
transaction spans process I/O; serialize with the same delivery/start lock order,
and do not hold a non-reentrant queue lock while invoking code that acquires it.

Resume is permitted only after positively observed exit of G. A DB Stopped row,
kill request acknowledgment without exit, runner absence or unreachable runner
does not prove that. Persist G2 with its launch reservation before enqueueing so
crash reconciliation recognizes the exact accepted launch, rather than selecting
another generation. The actual accepted launch produces the existing restart/end
fence; detection never manufactures a TurnEnd or restart boundary. The existing
`WriteRestartBoundaryIfInterruptedAsync` is best-effort. For this recovery, verify
its durable fence before allowing G2 queue delivery; a failed fence write keeps
admission closed and retries only that bookkeeping, never another process restart.

Crash handling must be executable, not an in-memory retry promise:

- Before StopRequested commits: zero stop calls. Audit-store failure withholds
  the action. Once committed, multiple sweep instances own the same attempt.
- After stop request with a lost response: reconcile the captured generation.
  Positive same-generation exit permits progress; if it still lives, only a fresh
  successful observation and the same conditional operation may finish the stop.
  Reusing the operation does not consume another allowance. Missing/changed/
  ambiguous state holds; never stop the replacement or launch on an assumption.
- After Stopped but before resume reservation: revalidate intent and resume once.
  After reservation but before enqueue/ack: use G2 and existing interrupted-launch
  recovery, with the episode check in that path too. A failed launch ends this
  attempt; do not let the ordinary supervisor start a second one.
- After launch but before completion bookkeeping: adopt only the stored G2 and
  reconcile Check/receipt evidence. Late exits/results for G cannot close G2 or
  resolve its episode. Human Stop wins at every remaining side-effect boundary.

### Occupancy, admission and queue handling

For a confirmed episode, the dispatcher gets a dedicated non-destructive failure
path before D8. Fail a Dispatched Check occupant only after both its delivery
watchdog age and the episode's silent-continuation deadline have elapsed. Require
its same physical seat/session/generation and its own linked brief, Pending and
**never attempted**, rechecked under the delivery lock. Prefer ExecutionTaskId;
legacy marker lookup must still be exact and scoped to this task/session/dispatch.
Do not call `AgentTaskService.CancelAsync` here: it calls StopDelegateAsync.

Use a dedicated failure code/reason such as `CompactionContinuationStalled` with
boundary, generation, age and the episode/automatic-action state. Do not claim
the session was killed before an exit result, or claim it was never killed when
this episode did terminate it.
Reuse the existing fail/event/caller-notification transaction and its recovery
outbox. This path cannot flow into the generic never-started kill or automatic
retry/escalation tails. At defaults an already-old episode's new occupant is
released at the first watchdog tick after ten minutes, not at 240 minutes.
Before an automatic stop, Working/Sent tasks retain their evidence and are never
failed as undelivered. Attempted-Pending, unconfirmed Sent, mismatched or uncertain
tasks veto automatic action. Old investigation occupants with untyped briefs
qualify for the non-destructive deadline path.

After a positively confirmed automatic stop, retire all exact same-generation
untyped Check occupants, including one younger than its delivery deadline, with
the distinct reason `CompactionRecoveryRetiredGeneration` (not delivery timeout).
Fail the owning delivered Check only if still open, recording that its turn was
interrupted by this authorized recovery. Preserve its Sent/receipt rows; do not
rewrite it as never delivered, requeue it, or rebind it to G2. A terminal owning
Check stays terminal. Commit task outcomes, events and caller outbox obligations
before reopening G2 admission, and keep this path out of generic fail/retry/kill
tails. These exact task transitions are replay-safe after a stop/commit crash.

Make admission consult one shared current-episode predicate in three places:
legacy Check `SpecialistTaskRunner.RunAsync`, routed physical-candidate selection
and claim in `SpecialistRequestService`, and `PlaceOnStandingAgentAsync` before
committing Dispatched. Cover queued work admitted before detection as well as
new requests. No branch may silently fall back to the same blocked primary.
Only the affected physical seat is excluded; do not block a declared healthy
alternate by confusing the logical owner's session with owner-wide suspension.
Map refusal to the existing degraded-digest flow with a specific reason, not to
Disabled (which can suppress the unavailable explanation).

Give terminal untyped Check-brief pruning an entry point callable while Working,
using queue-owned locking and full never-attempted evidence
(`StandingQueueSwitchPolicy.NeverAttempted` where applicable). Reuse the existing
linked-task cancellation rules; do not implement a second text-only cleanup.
Retry this cleanup after a failure commit and at subsequent episode sweeps, so a
crash between task failure and queue cancellation cannot leave permanent residue.
It types nothing and preserves unrelated/attempted rows. Cancel obsolete Check
briefs only after their tasks are terminal and their full never-attempted proof
passes. Do not cancel the delivered pre-compaction queue record or replay it into
the resumed generation. No older Check task or queue brief crosses this restart;
only newly requested, correlated Check work can demonstrate recovery.

### Visibility

Project one durable `CompactionContinuationStalled` attention item for an active
episode, including physical seat, session, accepted start, boundary time/sequence,
elapsed silence, affected task, pending-brief count, action phase and refusal or
failure reason. Explicitly label automatic stop/resume and its operation ID.
Link to agent/session incident history and the operator procedure; no new card
status or alert sink. Warning incidents record detection and intent before RPC;
All action audit names actor `check-compaction-recovery` and authorization
`CARD-0079/fed4b83c`; outcome incidents record conditional-stop refusal/confirmed exit, resume accepted,
launch success/failure and receipt-confirmed recovery. NeedsDecision/failure is
Error; do not page every sweep or use a human-decision alert channel. Use existing
incident notification policy and event bus after transaction commit, with durable
retry for publication. Store metadata/IDs and bounded diagnostics, not copied
prompt bodies or secrets. Retain the action audit even after attention clears.
Add the kind's normal client visual/type mapping. Health must not advertise that
seat as warm-ready just because Status is Running. A declared alternate can keep
the logical service available. For a legacy primary, the session attention item
must still exist without routing/health rows. At AwaitingCheck display resumed /
validation pending and allow a new Check; clear recovery attention only on the
receipt-backed success or a documented operator supersession. A false alarm ended
by late progress clears its stall attention with an AbortedProgress audit, not a
claim that an automatic restart succeeded.

## Implementation slices

Each slice is a meaningful commit with its real validation outcome. D-1/D-3 are
settled; Build follows separate TestDesign. Paths for application test names below
are `tests/Antiphon.Tests/Application/` unless otherwise stated. New production
types and tests are proposed, not existing APIs.

| Slice | Production/document files | Tests and acceptance |
|---|---|---|
| S0: operator repair | Execute the preceding runbook; record evidence in `docs/investigations/2026-09-20-card-0079-operator-recovery.md` (new). No production edit. | New generation, no replayed dead briefs, first useful Check plus caller receipt, subsequent census. State explicitly if not yet performed. |
| S1: policy, durable operation and budget | New `server/Application/Services/CompactionContinuationPolicy.cs`; `server/Application/Settings/DelegationSettings.cs` (validator in same file); new `server/Domain/Entities/CheckCompactionRecovery.cs`; `AgentSupervisionState.cs`; appended incident/failure/termination and new phase enums under `server/Domain/Enums/`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated migration/snapshot; `DataRetentionService.cs`. | New `CompactionContinuationPolicyTests`, `CheckCompactionContinuationSettingsTests`, `CheckCompactionRecoveryPersistenceTests`; extend `DataRetentionServiceTests`. Threshold, null/backfilled time, Check-only scope, progress permanently disqualifies, unique active operation, audit retention and durable 24-hour/receipt budget. |
| S2: reliable observation and guarded runner stop | `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs` and new `CompactionContinuationStop.cs`; `src/Antiphon.SessionRunner/Program.cs`, `SessionRunnerRuntime.cs`, `ITranscriptTailer.cs`, `TranscriptTailer.cs`; `server/Application/Interfaces/ISessionRunnerClient.cs`; `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`; `AgentSessionRuntime.cs`, `AgentSessionService.cs`; `tests/Antiphon.Tests/TestHelpers/{Fake,Scripted,Direct}SessionRunnerClient.cs`. | New `tests/Antiphon.SessionRunner.Tests/CompactionContinuationStopTests.cs` and `TranscriptTailerObservationTests.cs`; extend `RunnerSessionGenerationTests`, `TranscriptTailerCompactionTests`, `AgentSessionRuntimeTests.Persist.cs`; new `tests/Antiphon.Tests/Agents/CompactionContinuationWireTests.cs`. Fresh read-to-end, partial/error/unbound observations, late output/end, generation race, missing capability, exit confirmation and no unconditional fallback. Other tailers report Unsupported for this Claude-only observation. |
| S3: automatic coordinator and strict resume | New `CheckCompactionContinuationService.cs`; `AgentTaskDispatcher.cs` sweep; `AgentControlService.cs` internal episode-bound reservation; `AgentSessionLaunchQueue.cs`; `AgentSupervisorService.cs`; `SessionReconciliationService.cs` for interrupted launch; `StandingSpecialistSeatPolicy.cs`; `server/Program.cs` DI. | New `CheckCompactionContinuationTests`, `CheckCompactionAutomaticRestartTests`; extend `AgentSupervisionTests`, `SpecialistStartIntentTests`, `StandingSessionSwitchConcurrencyTests`, `AgentSessionRuntimeTests`. Confirmed episode -> automatic conditional stop -> one strict resume without a human call; all crash cuts, suspended/hold changes, zero setting, source/replacement generation and supervisor races. |
| S4: occupancy, queue and admission | `AgentTaskDispatcher.cs`, `SessionMessageQueueService.cs`, `SpecialistTaskRunner.cs`, `SpecialistRequestService.cs`, shared episode predicate; `AgentTaskCheckService.cs` failure wording and existing outbox path. | New `CheckCompactionRecoveryFlowTests`; extend `AgentTaskDeliveryWatchdogTests`, `AgentTaskStandingAgentDispatchTests`, `SessionMessageQueueWedgedHeadTests`, `SpecialistFailurePolicyTests`. Both Check entry paths, final dispatch race, old/young untyped occupants, exact owning delivered Check, no replay into G2, healthy alternate, and attempted/human-input veto. |
| S5: visible audit and exception documentation | `AttentionService.cs`, `server/Application/Dtos/AttentionDtos.cs`, `StandingSpecialistHealthService.cs`, existing incident/event publication; `client/src/api/attention.ts`, `client/src/features/attention/attentionVisuals.ts`; `AGENTS.md` narrow exception alongside unchanged rule; `docs/session-runtime-invariants.md`, `docs/ops-http.md`. | New `CheckCompactionAttentionTests`; extend `SpecialistHealthAttentionTests`, `attentionVisuals.test.ts`, `AttentionPanel.test.tsx` only if rendering changes. Intent precedes effects, refusals/errors visible, no false Recovered at launch, legacy primary without routing rows, durable visibility after pruning/publication failure. |
| S6: complete automatic flow and acceptance | Extend `CheckNoteDeliveryHandoffTests.cs`, using real DB/dispatcher/queue and an isolated scripted runner through the new HTTP contract; add deployment evidence to `docs/investigations/2026-09-20-card-0079-operator-recovery.md` only when performed. | Stuck eligible episode -> automatic stop/strict resume (no manual Stop/Start) -> new Check -> useful result -> whole parent receipt, with busy and eligible parents and crash-recovered notification. Extend `ReceiptFailureDeliveryTests`, retain manual `StandingSessionRecoveryHttpTests` and `StandingSessionQueueSwitchTests`. Separate fake-runner mechanism evidence from live natural-compaction acceptance. |

Do not alter auto-compaction scheduling, native transcript event interpretation, model holds,
role-wide ceilings, CARD-0501 attempt accounting, or standing ownership rules in
these slices. If implementation discovers those contracts need changing, return
the specific finding to the caller instead of broadening the repair silently.

## Guards and positive-control input for TestDesign

Method names below are proposed new names unless explicitly identified as existing.
TestDesign must pin each PC to one exact method and one controlled production
mutation, with the expected assertion and a restored-green result. Code/ordinary
Review do not claim these PCs executed; SourceLanding Mutation follows confirmed
land under the repository workflow.

| PC | Proposed exact method (class) | Mutation and required red observation |
|---|---|---|
| PC-1 | `Auto_boundary_with_silent_continuation_is_overdue_at_ten_minutes` (`CompactionContinuationPolicyTests`) | Treat auto boundary as ineligible; expected overdue verdict fails. Seed the investigation's TurnEnd/UserPrompt/auto/continuation sequence. |
| PC-2 | `Manual_unknown_and_idle_compaction_never_arm_recovery` (`CompactionContinuationPolicyTests`) | Broaden auto eligibility to manual/unknown/idle; expected no episode fails. |
| PC-3 | `Post_boundary_work_excludes_silent_continuation` (`CompactionContinuationPolicyTests`) | Remove progress veto; each relevant assistant/thinking/tool/result/ordinary-prompt case must fail at an eligibility assertion. |
| PC-4 | `Successful_unchanged_pull_confirms_but_failed_pull_does_not` (`CheckCompactionContinuationTests`) | Collapse no-change and pull-failure outcomes; assertion on durable episode presence/absence fails. No transport/build exception counts as red. |
| PC-5 | `Catch_up_progress_saves_the_suspected_session` (`CheckCompactionContinuationTests`) | Skip post-pull reevaluation; the late response/end incorrectly creates an episode. |
| PC-6 | `Old_generation_observation_cannot_hold_the_replacement` (`CheckCompactionContinuationTests`) | Remove the generation/pointer fence at commit; replacement is incorrectly marked recovery-needed. Include same-ID resume. |
| PC-7 | `Held_recovery_releases_the_expired_untyped_occupant_without_generic_kill` (`CheckCompactionRecoveryFlowTests`) | Restore unconditional D8 continue; task remains Dispatched instead of Failed. Seed a confirmed episode whose automatic allowance is exhausted; assert zero generic kill calls. |
| PC-8 | `Attempted_or_sent_brief_retains_its_existing_owner_and_delivery_evidence` (`CheckCompactionRecoveryFlowTests`) | Treat Pending as sufficient proof of never-attempted; a current-composer row is incorrectly canceled/failed. |
| PC-9 | `Legacy_check_refuses_the_stalled_primary_before_task_creation` (`CheckCompactionRecoveryFlowTests`) | Remove legacy admission gate; unexpected task/queue creation fails. |
| PC-10 | `Routed_check_excludes_only_the_stalled_physical_seat` (`CheckCompactionRecoveryFlowTests`) | Remove physical-seat gate, or apply primary episode to all alternates; selected candidate/launch evidence fails. Test each mutation separately. |
| PC-11 | `Confirmed_episode_survives_restart_and_incident_pruning` (`CheckCompactionContinuationTests`) | Use incident existence/in-memory dedup instead of persisted episode; duplicate incident or lost refusal/attention fails. |
| PC-12 | `Terminal_untyped_briefs_are_pruned_while_working_after_a_commit_gap` (`CheckCompactionRecoveryFlowTests`) | Put cleanup back behind idle or omit reconciliation after failure commit; obsolete Pending brief remains. Human/attempted rows must remain unchanged. |
| PC-13 | `Automatic_restart_delivers_a_new_check_and_its_whole_caller_note` (`CheckNoteDeliveryHandoffTests`) | Disable the episode resume transition; a same-session new accepted generation, successful new Check and caller receipt fail. No manual Stop/Start is called by this test. |
| PC-14 | `Healthy_working_session_still_defers_delivery_and_is_never_killed` (`CheckCompactionRecoveryFlowTests`) | Remove ordinary D8/D9 protection; healthy busy control fails. Also retain existing `a_working_session_with_a_pending_brief_is_neither_failed_nor_killed` in `AgentTaskDeliveryWatchdogTests`. |
| PC-15 | `Only_owned_always_on_claude_check_seats_may_auto_restart` (`CheckCompactionAutomaticRestartTests`) | Bypass the shared automatic-scope predicate; representative non-AlwaysOn, ordinary AlwaysOn, non-Claude, pool, human-turn and slug-lookalike cases violate zero stop/resume assertions. |
| PC-16 | `Ten_minutes_without_a_turn_end_is_insufficient_without_boundary_and_continuation` (`CompactionContinuationPolicyTests`) | Remove the boundary/continuation requirement; a long ordinary Working turn wrongly becomes eligible. |
| PC-17 | `Tail_observation_failure_is_unknown_not_silence` (`TranscriptTailerObservationTests`, runner project) | Treat incomplete/failed read-to-end as successful; expected Unknown fails with unparsed trailing bytes still present. |
| PC-18 | `Late_output_or_turn_end_prevents_conditional_stop` (`CompactionContinuationStopTests`, runner project) | Remove the final revision/pattern recheck after the test barrier; inject output or TurnEnd there and assert the process kill count stays zero. |
| PC-19 | `Replacement_generation_is_never_stopped` (`CompactionContinuationStopTests`, runner project) | Remove accepted-generation comparison; replacement gets a kill call instead of a mismatch refusal. |
| PC-20 | `Unsupported_or_ambiguous_stop_never_falls_back_or_resumes` (`CheckCompactionAutomaticRestartTests`) | Treat Unknown/Missing/unsupported as confirmed exit; resume count changes from zero. Separately assert generic kill and raw-input call lists remain empty. |
| PC-21 | `Human_stop_between_detection_and_effect_revokes_recovery` (`CheckCompactionAutomaticRestartTests`) | Remove final intent check at the selected stop or queued-resume boundary; no-effect assertion fails. TestDesign names one boundary per mutation. |
| PC-22 | `Restart_intent_must_commit_before_any_runner_effect` (`CheckCompactionAutomaticRestartTests`) | Move conditional stop ahead of durable commit; injected commit failure still produces a stop call, violating zero effects. |
| PC-23 | `Lost_stop_response_reconciles_only_the_captured_generation` (`CheckCompactionAutomaticRestartTests`) | Infer stopped from runner absence; an unknown outcome improperly launches G2. Include positive exited-generation recovery as an ordinary control. |
| PC-24 | `Resume_reservation_survives_crash_without_a_second_launch` (`CheckCompactionAutomaticRestartTests`) | Ignore the persisted G2 reservation after service recreation; assert more than one accepted generation/launch for the operation. |
| PC-25 | `Supervisor_cannot_bypass_an_active_or_failed_compaction_operation` (`CheckCompactionAutomaticRestartTests`) | Remove supervisor episode gate; concurrent sweep or failed-resume case records an extra launch. |
| PC-26 | `One_compaction_restart_per_day_survives_pruning_and_service_recreation` (`CheckCompactionRecoveryPersistenceTests`) | Replace durable last-attempt admission with incident existence; second episode inside 24 hours incorrectly stops the seat. |
| PC-27 | `A_second_restart_needs_useful_check_and_caller_receipt_even_after_a_day` (`CheckCompactionRecoveryPersistenceTests`) | Remove receipt eligibility; after 24 hours without receipt, second stop is incorrectly authorized. |
| PC-28 | `Automatic_recovery_preserves_history_and_all_launch_holds` (`CheckCompactionAutomaticRestartTests`) | Change recovery request to Fresh; assert the same session/native ID is resumed and the existing automatic-Fresh refusal is not bypassed. Hold/no-override variants remain ordinary guards. |
| PC-29 | `Confirmed_stop_retires_exact_old_checks_without_replay` (`CheckCompactionRecoveryFlowTests`) | Omit retirement of a younger untyped occupant; assert it remains open or its old brief appears in G2. Assert delivered owning Check history remains unchanged. |
| PC-30 | `Resumed_is_not_recovered_until_the_whole_parent_note_arrives` (`CheckCompactionAttentionTests`) | Accept queue Sent as receipt; recovery attention and D-9 receipt gate incorrectly clear before whole parent UserPrompt. |
| PC-31 | `Disabling_recovery_revokes_unissued_stop_and_resume` (`CheckCompactionAutomaticRestartTests`) | Omit the effective zero-setting check at a selected side-effect boundary; an already confirmed episode still issues a new stop/resume. |
| PC-32 | `Attempted_or_unrelated_input_vetoes_the_automatic_stop` (`CheckCompactionAutomaticRestartTests`) | Remove queue/work preflight veto; attempted Pending, unconfirmed Sent or unrelated input produces a stop instead of visible NeedsDecision. |
| PC-33 | `Failed_restart_fence_write_keeps_new_generation_admission_closed` (`CheckCompactionAutomaticRestartTests`) | Ignore the durable restart-fence check; with its write failed, assert recovery remains validation-blocked and no new Check task is admitted. Incorrect AwaitingCheck/admission fails even if the ordinary Working gate still prevents typing. |

Additional ordinary guards: 9:59.999 vs 10:00; setting zero/negative/1–9; repeated
auto boundaries; timestamp inversions and nulls; already terminal task; no queue
row; mismatched execution task; concurrent sweep; unavailable observation does
not block healthy other sessions; no pending task at detection; failure/outbox
commit vs note enqueue crash; record remains visible after task settlement;
real late progress resolves an episode but a health success still needs a valid
Check. Assert zero input/stop/resume on every veto path and exactly one conditional
stop plus one strict resume on the accepted path, with exact session/generation
and attempt IDs. A delivered old Check is never retyped. Keep positive passing
controls for legitimate >10-minute compaction continuations emitting Thinking,
tool activity or text; idle/manual/missing-boundary cases; and stale backfill.

Exercise every crash cut in the lifecycle section, duplicate concurrent sweeps,
owner/seat AlwaysOn or routing changes, quota/auth/model/capacity/continuity/Herdr
holds at both preflight and resume, 24-hour exact threshold, terminal episode
retention, runner restart during native-tail observation, and successful no-change
observation followed by progress at final stop. A stop refusal cannot mutate the
session to Failed. Audit metadata must show automatic actor, threshold, evidence,
expected/observed generations, outcome and refusal; never log a fabricated success.

For delivery acceptance, inventory and join the original checked task/check number,
Check run task, interpreter session/generation, its brief queue row, correlated
result, parent-note queue/notification identity, and parent whole-UserPrompt
receipt. Exercise both already eligible and busy parents, plus failure between
durable failure/outbox commit and enqueue. No screen-only or fake-Sent proof.

## Verification handoff and rollout

Plan validation is source inspection, linked-file checks and `git diff --check`;
there are no runtime test counts to claim in this dispatch.

TestDesign should use the repository's ordinary Unit lane plus the named affected
integration classes above, with fresh TRX counts and explicit coverage-to-class
mapping. Extend only the classes actually changed; do not run an unrelated full
assembly by default. Use an isolated `bin-c79/` output with a forward slash and
inventory/remove the task's outputs after all foreground commands finish.
Representative ordinary commands, to be finalized by TestDesign:

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c79/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c79/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory .antiphon/c79-unit
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c79/ -- --treenode-filter '/*/*/(CheckCompactionContinuationTests*)|(CheckCompactionRecoveryFlowTests*)|(AgentTaskDeliveryWatchdogTests*)/*' --report-trx --report-trx-filename recovery.trx --results-directory .antiphon/c79-recovery
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c79/ -- --treenode-filter '/*/*/(CompactionContinuationStopTests*)|(TranscriptTailerObservationTests*)|(RunnerSessionGenerationTests*)/*' --report-trx --report-trx-filename runner.trx --results-directory .antiphon/c79-runner
pwsh -File scripts/test-client.ps1 attentionVisuals.test
```

Other named integration classes need their own correctly combined class filters;
this example is not the complete battery. Use `DelegationTestServices` for dispatcher
graphs; real queue timers need an offset/running clock or FakeTimeProvider with
properly advanced timers. Program-host tests must refuse the production runner.
Use assembly-local ProcessSpawnLimit for process fixtures and run Pty tests
sequentially if the finalized scope needs them. Do not add a live forced-compaction
test on the production interpreter. Baseline failures must be reproduced at the
base SHA before being called pre-existing.

PC execution is method-scoped red/restore/green, for example
`--treenode-filter '/*/*/CheckCompactionContinuationTests/Old_generation_observation_cannot_hold_the_replacement'`.
Check actual executed names and nonzero TRX counts; no build error, fixture error
or zero-test run is a successful control. Follow external evidence/restoration
rules for SourceLanding; this Plan worktree is not a SourceLanding snapshot.

After ordinary Code, separate Review and confirmed land, activate only through
the canonical checkout/runbook and verify `/api/version` against the intended SHA.
Apply the additive schema and deploy/verify the new runner capability before
enabling automatic actions. Re-read effective settings, current seat generation,
standing ownership and active episode. Missing capability withholds automatic
effects and remains visible. In an isolated test deployment, seed the captured
pattern and drive real automatic conditional stop/resume with no manual recovery;
capture every phase and the first new useful Check with parent receipt. In live
operation observe the next natural compaction, the next ten real Checks and their
role outcomes, noting any conditional-stop refusal or recovery hold. Do not
force-compact/kill a production interpreter as a test. Absence of a natural stall
means live automatic-restart acceptance is still pending, not proven by ten
ordinary successes. The operator Fresh runbook is separately reported evidence.

Rollback: set the dedicated setting to zero, preventing new discovery and any
unissued automatic stop/resume, retain action/transcript evidence and reconcile
already-issued effects. A stopped seat becomes visible DisabledNeedsDecision,
never an excuse for an implicit supervisor resume. Do not clear
model holds, globally bypass admission, downgrade data, or label the unchanged
stuck session healthy to make a dashboard green. A code rollback alone does not
recover that session. Record pending acceptance/Mutation work honestly.

## Next-stage handoff

D-1/D-3 are settled by the operator's explicit authorization. Send this amended
artifact to **TestDesign**, then Code; verification was not folded into this
dispatch. Finalize method-scoped PCs, ordinary profile and cross-process race
fixtures for the automatic path. The provider-level cause is still unknown;
failure or recurrence after the bounded strict resume records NeedsDecision and
calls for investigation/operator Fresh, never a broader restart loop. This Plan
dispatch changed no runtime behavior and performed no live recovery.
