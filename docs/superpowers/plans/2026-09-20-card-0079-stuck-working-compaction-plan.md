# CARD-0079: recover the fourth stuck-Working interpreter outage

Current H-5 amendment: Plan task `0c6a0491` supplies the missing durable legacy
Check-note contract in
[the legacy Check-note publication plan](2026-09-20-card-0079-legacy-check-note-plan.md).
It extends S4/S6 only and preserves D-1 through D-10. The TestDesign findings and
counts below describe the earlier `cf897be0` review; its H-5 gap now has a concrete
design for **TestDesign re-review before Code**, not an executed verification
result or a new automatic-restart authorization.

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

S4/S6 additionally require H5-1 through H5-3 in the
[legacy publication amendment](2026-09-20-card-0079-legacy-check-note-plan.md#implementation-slices):
captured identity/run binding, atomic immutable-body/outbox production, recovery
through the notification scanner, narrow keyed-Check supersession behavior and
original-note receipt proof. These are prerequisites for H-5 acceptance.

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


## Verification design

TestDesign task `cf897be0`, 2026-09-20, inspected exact plan HEAD
`c7d924a897e424b0b0b671a7169ac0a86d3915c2`. The requested branch was occupied by
`C:/Antiphon/worktrees/card-task-fed4b83c`; this document is amended on
`feat/card-task-cf897be0` starting at that exact commit. That TestDesign dispatch
left the fix design above unchanged. Later Plan task `0c6a0491` adds the linked
H-5 amendment and current-stage references; the verification results remain historical.

**Original TestDesign review: return to Plan before Code.** Plan task `0c6a0491`
has now supplied [the H-5 contract](2026-09-20-card-0079-legacy-check-note-plan.md);
the current next stage is TestDesign to review it and update this verification
section before Code. The following findings/counts remain the original review.
The first 33 proposed controls
have concrete test construction paths below, with their independent assertions
split into additional controls. That review found one uncovered production delivery seam,
G-161/PC-161: legacy Check-note production before queue insertion. Its durable
identity/recovery contract is now in the linked amendment awaiting re-review. D-1/D-3 authorization
is settled; this is an engineering gap, not a request for another operator choice.

At this HEAD the new recovery service, entity, runner endpoint and test methods
are not implemented. “Executable design” below means a specified fixture,
production boundary, compiling defect and decisive assertion for Code to
implement and Mutation to exercise after land. It does not mean these tests
were discovered, compiled or run by TestDesign. No runtime or live recovery
evidence is claimed.

### Inspection

Paths below are repository-relative. For large partial classes the named bodies
and setup are the inspected subset; unchanged sibling cases are regression suites,
not purportedly reread bodies.

| Test/fixture bodies read | Boundaries -> verification |
|---|---|
| `tests/Antiphon.SessionRunner.Tests/RunnerSessionGenerationTests.cs`, all five methods and helpers; `RemoteControlConditionalInputTests.cs`, all methods/helpers; `LocalHttpRunner.cs`, launch, crash and dispose | Real generation echo, launch-lock races, native process exit and random-port HTTP -> V-3, V-6, R-3, R-6 |
| `tests/Antiphon.SessionRunner.Tests/TranscriptTailerCompactionTests.cs`, all methods, file writers and polling helper; `tests/Antiphon.Tests/Agents/Fixtures/compact-boundary.jsonl`; continuation record in `compact-full-manual.jsonl` | Existing boundary fixture is **manual**, not eligible auto; fork/bind discovery and continuation structure -> V-1, V-3, R-1 |
| `tests/Antiphon.Tests/Agents/SessionRunnerGenerationWireTests.cs`, all methods, StubHandler and Client | Capabilities, request serialization, echo mismatch, 404 and no fallback -> V-3, R-3 |
| `CheckNoteDeliveryHandoffTests.cs`, complete Handoff, seeding, receipt assertions, all seven tests | Both delivery legs; AlwaysOn is explicitly disabled and result settlement is direct SQL/EF; cannot reuse those shortcuts for automatic acceptance -> V-5, R-5 |
| `ReceiptFailureDeliveryTests.cs`, complete delivery/failure methods, service recreation, OffsetClock, DeliveryFault and CallerFailureFault | Failure/event/outbox atomicity, queue/attempt/receipt cuts, busy/eligible recipients, stopped-recipient confirmation -> V-5, V-6, R-5 |
| `StandingRecoveryFixture.cs`, complete; `AgentControlServiceIntegrationTests.BuildHarness`; `StandingSessionSwitchConcurrencyTests.cs` and `.CommitGap.cs`, complete methods/interceptors | Real reservation transaction and launch queue; commit-to-enqueue gate, competing Starts, current pointer, delivery and ownership recheck -> V-4, V-6, R-4 |
| `SpecialistStartIntentTests.cs`, complete Factory/ExerciseAsync; `AgentSupervisionTests.Stop_suspends_supervision_until_manual_start` and `Healthy_uptime_resets_the_ladder` | Human Stop before/during launch, missing native history, generic failure counters -> V-4, R-4 |
| `AgentTaskDeliveryWatchdogTests` initial seeding/cases plus D8/D9 Working, Sent, unattributed and inside-window bodies (lines 574–670) | Healthy Working must remain protected; pending/idle control; independent occupant age -> V-2, R-2 |
| `AgentTaskStandingAgentDispatchTests` first seven standing/occupancy methods (lines 1–200) | Live physical session, dead previous occupant, no second launch, unchanged owner pointer -> V-2, R-2 |
| `SessionMessageQueueWedgedHeadTests` CreateAsync, generation/receipt helpers and terminal/untyped/attempted/orphan cases (lines 500–631); `StandingSessionQueueSwitchTests` first four methods | Existing recovery preserves attempted composer evidence; marker-only rows and zero attempts are insufficient -> V-2, R-2 |
| `AgentSessionRuntimeTests.Persist.cs` skipped-row, duplicate, transient-stub, non-database-failure methods and PersistFixture; `AgentSessionRuntimeTests` stale/newer/null-generation exit methods | Partial persistence and stale exits; old boolean catch-up cannot certify success -> V-3, V-4, R-3 |
| `DataRetentionServiceTests.C508_IntentSessionRetention`, `C508_IntentTranscriptRetention`, CreateService/SeedSessionAsync; `SpecialistHealthAttentionTests.cs` health-policy and attention bodies | Retain referenced evidence with unrelated eligible prune control; qualification alone cannot clear outage -> V-4, V-7, R-7 |
| `SpecialistFailurePolicyTests.cs`, all bodies; `client/src/features/attention/attentionVisuals.test.ts`, all bodies and item factory | Unchanged default waits, scoped health and normal attention mapping -> V-1, V-7, R-7 |
| `StandingSessionRecoveryHttpTests.cs`, test and StandingRecoveryWebAppFactory; `TestHelpers/ProductionRunnerGuard.cs` | Program composition must use isolated/refusing runner; manual recovery contract remains intact -> V-4, R-4 |
| `tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs` launch helper, three compaction/transcript cases, shared JSONL read helpers; `EchoGatedSubmit.cs`; both application/runner csproj fixture links | New opt-in unfinished-turn scenario must preserve old fake behavior and stage the executable -> V-3, R-3 |
| `TestHelpers/BridgeQueueHarness.cs` CreateAsync, InsertEntryAsync and DisposeAsync; `DelegationTestServices.cs`; `TestDbFixture.cs`; `LandQueueRaceWorker.cs`, complete | Real queue/DI, cloned PostgreSQL database, explicit child connection, process death and drained output -> all integration V/R |
| `TestHelpers/FakeSessionRunnerClient.cs`, `ScriptedSessionRunnerClient.cs`; DirectSessionRunnerClient launch/capability/kill/transcript/dispose mapping | Fake GetAsync defaults to unbound Exited and cannot prove target exit; scripted Start unsupported; Claude tailing opt-in -> required setup below |

Production inspection also read the plan; `ITranscriptTailer`; TranscriptTailer
Snapshot/RunAsync/ProcessPending/EmitLine; SessionRunnerRuntime
KillGenerationAsync/SendConditionalInputAsync; AgentSessionRuntime catch-up/sync;
StandingQueueSwitchPolicy.NeverAttempted; AgentTaskCheckService.RunCheckAsync;
SessionMessageQueueService.CheckPublication; and FakeClaude native-home,
SubmitTurn/auto-compaction and JSONL emitters. Relevant owners read:
project-context, testing-and-build (ordinary/PC/delivery rules),
session-runtime-invariants, orchestration-loop (stage/landing rules) and ADR 0002.

**Required setup (Code work, absent at the inspected HEAD):**

1. Add `tests/Antiphon.Tests/TestHelpers/CheckCompactionFixture.cs`, based on the
   inspected BridgeQueueHarness, Handoff and BuildHarness. Use a cloned database
   connection everywhere; the existing StandingRecoveryFixture hardcodes the
   shared connection and starts with a stopped, non-AlwaysOn agent. Register the
   real control, launch queue, dispatcher, specialist entry paths, recovery
   coordinator, notification reconciliation and `AddDelegationWorktreeGraph`.
   Register real launch gates; a missing optional quota/model service does not
   count as testing its veto.
2. Seed a physical **AlwaysOn Claude Check** seat with a separate logical owner
   when testing alternates, both enabled and AlwaysOn, accepted microsecond G,
   immutable standing owner, native history, no holds, current running pointer,
   ended previous turn and a delivered, positively linked owning Check prompt.
   Append explicit auto B and synthetic C in that unfinished turn. Assert these
   preconditions before running the subject. Do not use the manual boundary
   fixture unchanged or silently set AlwaysOn=false to avoid a kill.
3. Add a synthetic auto JSONL fixture under
   `tests/Antiphon.Tests/Agents/Fixtures/compact-auto-silent-check.jsonl` and
   link/copy it to the runner tests as the existing compact fixture is linked.
   Derive schema from the inspected manual fixture, change only auto trigger,
   synthetic IDs/times and the correlated prompt/continuation. Label it
   synthetic, with no claim of a newly captured provider sample. FakeClaude's
   existing `ANTIPHON_FAKE_COMPACT_AFTER_TURNS` runs after SubmitTurn and can
   leave an ended turn; it is **not** the silent unfinished-turn fixture.
4. The native test driver must accept actual queue bytes, emit that exact
   UserPrompt, then B/C **before** any assistant/end for G. On strict resume G2,
   it retains native-history bytes and emits a useful deterministic Check
   reading plus the exact task report token/TurnEnd only after receiving the
   newly produced brief. Extend an opt-in FakeClaude scenario for this; never
   overwrite AgentTask.Status/Result to simulate successful acceptance.
   Isolate `CLAUDE_CONFIG_DIR` and native home in owned temporary directories.
   The runner test project currently does not stage FakeClaude. Add the same producer-target-path staging used by Antiphon.Tests; verify the executable and synthetic fixture are present before running. No skip for missing staged binaries counts as acceptance.
5. Add `CheckCompactionBoundary`, a concrete internal no-op async rendezvous
   with named points `observed`, `before-stop-commit`, `stop-committed`,
   `before-stop-rpc`, `stopped-committed`, `resume-committed`,
   `before-resume-enqueue`, `before-resume-spawn`, `launch-accepted`,
   `before-fence-write`, `retirement-committed`, `failure-committed`,
   `before-note-enqueue`, `note-committed`, `attempt-committed`,
   `prompt-accepted`, `before-receipt-commit`, `audit-committed` and
   `before-audit-publish`. Reuse EF command/transaction interceptors for
   actual persistence failures. Hooks only pause/throw at real boundaries;
   they do not inject verdicts or bypass guards.
6. Runner tests need analogous internal barriers before final stop validation,
   between final validation and signal, and during bound-file read. Expose an
   internal recording view of exact stop signals, analogous to
   SnapshotBackendWrites, observing real termination calls. A fake kill-count
   test cannot replace the real process-exit and replacement-alive assertions.
   Public production APIs must not acquire a test/force bypass.
7. Add `CheckCompactionCrashWorker.cs` using the inspected LandQueueRaceWorker
   child-dispatch pattern. Parent owns the isolated database and random-port
   runner; children inherit only test connection/root/IDs via environment.
   At each committed cut, child writes a rendezvous receipt containing its
   MVID and operation/G/G2 IDs. Parent kills that exact owned worker, awaits
   exit and drains stdout/stderr, then starts a new worker. Do not dispose the
   production graph gracefully and call that process-crash coverage.
8. Pure policy tests use a deterministic TimeProvider. Queue tests use an
   offset over real time, or drive FakeTimeProvider timers explicitly. Keep
   the production 10-minute rule; advance the clock, never shorten the policy.
   File/native checks independently exercise exact-boundary math and a real
   past-dated accepted generation. All child/process fixtures carry the
   assembly's ProcessSpawnLimit and are awaited. Do not co-run Pty tests.
9. A successful fresh-tail receipt needs consumed byte watermark, parsed
   revision, native binding identity, accepted generation and stable end-of-read
   status from the **real** tailer. Test >1 MiB multi-chunk input, trailing
   partial UTF-8/JSON, normalization failure, legitimate ignored metadata,
   truncation/replacement, claim change and runner restart. Fake/Scripted clients
   default this new capability to Unsupported; no automatic “success” default.
10. Add receipt helpers which query persisted transcript rows by session and
    generation/attempt floor, compare complete normalized immutable body, and
    return the exact UserPrompt sequence/UUID. Inputs must be derived from actual
    child/adapter submissions. A pointer-only transcript plus reading a local
    spill file is not whole-note receipt under this task's acceptance contract.
    Normal acceptance notes/briefs must fit the supported inline envelope;
    spilled/truncated cases remain pending until actual complete receipt exists.

**Boundary combinations.** Every end-to-end positive starts with the complete
eligible conjunction. Flip one necessary fact at a time, with every other guard
still eligible. Run the physical/logical AlwaysOn 2x2 matrix; legacy versus
declared alternate; Check versus Diagnose/Distill/general work; effective
Claude/Codex/Grok; Running/Starting/Stopped/Failed; Working/idle; auto/manual/
unknown/missing B; missing C; and 9:59.999/10:00/10:00.001. These are separate
input dimensions, not a single fixture where several vetoes accidentally mask
one another. The scope methods repeat at discovery, after observation and before
queued resume; PC-152 separately guards calling the shared scope predicate there.

Run every substantive-kind veto separately, before and after B as appropriate;
a matched tool pair before B is the positive opposite of an unmatched tool.
Use later B/C event/arrival times, null event time with/without generation fence,
future time, equal timestamps, stale high-sequence backfill and repeated
boundaries with/without newest C. The exact five-member maximum is checked with
each member uniquely latest. Changing output revision alone defers observation;
it is not useful progress or a new generic inactivity timer.

Queue evidence: each NeverAttempted member alone populated (including baseline
zero), negative/inconsistent attempts, nonnull LastDeliveryGeneration with otherwise
empty metadata, Pending/Sent/Cancelled, owning/other/missing task, exact and
ambiguous legacy marker, same/different session and same-ID different generation.
Confirmed delivered owning Check is permitted evidence; attempted Pending and
unconfirmed Sent are action vetoes. Include held/exhausted episode + old untyped
occupant and confirmed exit + young untyped occupant. The former waits for both
deadlines; the latter retires under its separate reason.

Crash cuts cross busy/eligible recipients for each session-delivery leg; reservation
and stop cuts cross human Stop, setting zero and changed generation at the next
effect boundary. Holds are each tested alone at stop and resume. Full products of
all unrelated hold types with all receipt corruptions are excluded: independent
single-fault guard tests plus the named cross-boundary races distinguish them,
without multiplying identical outcomes. Native/provider nondeterminism is a
separate live qualification, not a reason to loosen deterministic assertions.

### Delivery inventory

Durable operation identity is
`E=(PhysicalAgentId, SessionId, AcceptedStartedAt G, NativeBoundaryIdentity)`,
plus the unique episode ID and stop attempt ID. Resume reservation records G2.
DB arrival sequence is supporting evidence, not native identity.

| Path | Producer -> destination | Persistence and recovery | Observable receipt / tests |
|---|---|---|---|
| H-1 stop outcome | Recovery coordinator -> conditional runner endpoint -> captured process G -> coordinator | StopRequested, attempt and spent budget commit together before RPC. Lost response reconciles same attempt and positive G exit; absence remains held. | Exact generation's process exited, with matching durable action phase; request acceptance/kill ack alone fails. V-3/V-4/V-6, PC-18–24/63–71/77–80 |
| H-2 resume outcome | Episode-bound AgentControlService -> real AgentSessionLaunchQueue -> runner/native conversation -> coordinator | G2 reserved before enqueue. Worker death before/after enqueue adopts only stored G2, no second launch. Actual durable restart fence and retirement/outbox commit precede admission. | Same native-history marker, G2 accepted, launch completes and fence persists; still AwaitingCheck. V-4/V-6, PC-24/28/33/72–76/83–91/106/152/156–157 |
| H-3 interpreter brief/result | AgentTaskCheckService -> legacy SpecialistTaskRunner or routed SpecialistRequestService -> real dispatcher -> session queue -> G2 interpreter | Checked-task ID/check number joins new Check run ID, ExecutionTaskId, brief queue ID, episode and G2. Before task commit: no runnable orphan. After dispatch/before brief enqueue failure: retire through durable failure notification; never fake receipt. Queue/attempt/accepted-prompt gaps recover same brief. | Exact complete UserPrompt after brief attempt floor, then correlated useful reading/report/TurnEnd and settled Check. Busy G2 waits; eligible G2 is flushed by actual producer/launch path. V-5/V-6, PC-13/112–114/117 |
| H-4 routed Check caller note | AgentTaskCheckService -> PublishCheckRequestAsync -> real caller queue -> parent session | SpecialistRequest ID and (CheckedTaskId, CheckNumber), CallerMessageId, event and CallerPublishedAt are committed atomically. Recreate and retry the same completed request; queue/attempt/receipt cuts use same body/row. Suppression and missing CallerMessageId do not mean receipt. | Matching complete caller UserPrompt after attempt floor. Cross both caller states and producer/queue/attempt/receipt cuts. V-5/V-6, PC-30/115–116/121–123/159–160 |
| H-5 legacy Check caller note — **amended; TestDesign re-review pending** | AgentTaskCheckService -> LegacyCheckNotePublication P -> atomic Body/event/LegacyCheckNote outbox -> real parent queue | [H-5 amendment](2026-09-20-card-0079-legacy-check-note-plan.md#production-and-recovery-protocol) binds the captured subject execution/Check number, E/G2 and one interpreter task. Produced body and notification survive pre-enqueue death; the existing notification scanner retries the same key. | Original complete parent UserPrompt after the original attempt floor, busy/eligible; no new probe or interpretation. PC-161 now has the proposed scanner-discovery defect and real committed cut in the amendment. Finalize its coverage/cost in TestDesign. |
| H-6 interrupted/undelivered task failure note | Dedicated compaction failure/retirement -> existing AgentTaskLandNotificationService -> real caller queue -> parent | Failed + source event + DeliveryFailure notification commit atomically. Durable notification ID joins SourceLandNotificationId, SourceTaskId, body/digest, QueueMessageId and ConfirmingPromptSequence. Preserve obligations on retirement crash and task settlement. | Same complete parent UserPrompt, not merely Failed/Sent/Confirmed flag. Cross six cuts in ReceiptFailureDeliveryTests plus actual worker death, busy and eligible callers. V-5/V-6, PC-118–120/123 |
| H-7 automatic audit/attention | Episode phase transaction -> incident/event publication -> subscribed test client and attention GET | Episode ID + phase/transition identity survives publication failure/recreation; re-read current attention after reconnect, retry pending publication without duplicate actionable episode. Terminal audit retained independently. | Client receives phase notification through real application event transport and GET returns the matching episode/phase; for disconnected client, reconnect + GET is receipt of current state only, not proof of historical push. V-7/V-6, PC-107–111/124–126 |

H-3 through H-6 acceptance must end at recipient evidence. At each handoff test:
before business commit failure; after business commit/before queue insertion;
enqueue failure; queue committed/ack lost; attempt committed/before submit;
actual UserPrompt accepted/before verdict; verdict/receipt bookkeeping committed
or lost. Recreate all server services in ordinary tests; real worker death at the
same cuts in V-6. For a pre-commit refusal, assert no partially accepted business
result, then rerun that same allowed operation and require its intended recipient
receipt. Lost H-3 brief must end in the original task's complete failure note,
not a invented successful interpretation.

At the original TestDesign base, neither existing `A_note_whose_queue_row_cannot_be_persisted_fails_cleanly_and_the_retry_delivers`
nor a newly successful Check proves original-note recovery: that test produces
another advisory Check. **Delivery acceptance still requires the amended handoff
to pass TestDesign and implementation verification.** The required amendment was to name
the legacy recovery-check publication identity, where its immutable body/facts
commit, which worker retries it after death, its idempotency key and how the
original caller receipt joins E. Keep the authorized scope unchanged; do not
force all legacy Checks through a different routing policy merely to obtain
a request row. These design requirements are now specified in the
[H-5 amendment](2026-09-20-card-0079-legacy-check-note-plan.md); verification
re-review and execution remain pending.

Substitutes: a recording runner proves server orchestration/arguments, not native
tail freshness or exit. Stub HTTP proves wire serialization, not endpoint behavior.
A scripted native child proves real queue/transport/tailer plumbing, not Claude's
reasoning quality or the provider cause. Service recreation proves persisted-state
recovery, not process-death lock release. Incident GET proves durable projection,
not a SignalR push. A complete UserPrompt proves input receipt, not that the agent
read a spill file or produced a useful interpretation. Each missing proof has the
separate native, crash, recipient or live requirement above.

### Proves it works now

The title denotes the ordinary implementation acceptance lane to be run by Code.
This documentation-only TestDesign performed source and structure checks, with
**zero runtime tests/builds**.

- V-1: eligible conjunction and boundaries | Unit | CompactionContinuationPolicyTests,
  CheckCompactionContinuationSettingsTests and existing SpecialistFailurePolicyTests
  in the Unit lane | no early/overbroad recovery; default 10; zero disables;
  negatives/1–9 rejected; legitimate positive at exact 10.
- V-2: occupancy, admission and safe cleanup | real PostgreSQL/dispatcher/queue |
  CheckCompactionRecoveryFlowTests plus AgentTaskDeliveryWatchdogTests,
  AgentTaskStandingAgentDispatchTests and SessionMessageQueueWedgedHeadTests |
  dedicated reason/transaction, no generic kill/retry, no old G work replayed into G2.
- V-3: successful observation and conditional stop | real bound files,
  real runner process plus HTTP mapping | TranscriptTailerObservationTests,
  CompactionContinuationStopTests, RunnerSessionGenerationTests,
  TranscriptTailerCompactionTests, CompactionContinuationWireTests, RemoteControlConditionalInputTests,
  AgentSessionRuntimeTests and sequential FakeClaudeContractTests | new capability, complete observation, matched exit,
  all stale/unknown states refuse with live replacement preserved.
- V-4: one durable bounded restart | PostgreSQL, real control/reservation/launch queue |
  CheckCompactionContinuationTests, CheckCompactionAutomaticRestartTests,
  CheckCompactionRecoveryPersistenceTests, AgentSupervisionTests,
  SpecialistStartIntentTests, StandingSessionSwitchConcurrencyTests,
  StandingSessionQueueSwitchTests, AgentControlServiceIntegrationTests and StandingSessionRecoveryHttpTests |
  audit-before-effect, one G2, strict history, unchanged operator/hold contracts.
- V-5: producer-to-recipient | real queue + isolated scripted native runner HTTP |
  CheckNoteDeliveryHandoffTests and ReceiptFailureDeliveryTests | actual new Check
  brief/report and whole caller note, busy/eligible, failure and receipt cuts;
  acceptance remains pending until H-5's plan amendment is implemented.
- V-6: worker-death/concurrent-worker cuts | isolated PostgreSQL + owned child
  workers + runner | CheckCompactionCrashTests | same E/attempt/G2/body/queue IDs,
  no duplicate stop/resume/typing, no lost failure or audit publication.
- V-7: truthful visibility and retained evidence | application projection and
  client mapping | CheckCompactionAttentionTests, SpecialistHealthAttentionTests,
  DataRetentionServiceTests and attentionVisuals.test.ts | precise phases and actor,
  one attention item, retention controls, no launch/Sent-based recovery.

Each new test file has the class name in the command inventory below. All
application new classes are Integration except policy/settings; runner file
tests and process tests stay in their own project. Use zero HTTP traffic to the
production runner. Any class added or changed outside this inventory requires
its ordinary tests in the Code report.

### Guards the regression

- R-1: scope and clock regressions | P/C methods in PC table | exact eligible
  verdict with each isolated input; also execute narrow A scope tests at all three
  action boundaries. A test that still passes after widening to non-Check,
  non-AlwaysOn or non-compaction stalls is a defective test.
- R-2: inherited D8/D9 and queue corruption | F methods plus the existing
  watchdog Working/idle pair and standing live/dead-previous-session tests |
  exact task/queue states and unchanged evidence, zero unrelated input or kills.
- R-3: observation/termination races | T/S/W/D methods and existing generation/
  persist classes | exact refusal reason and zero real stop signals, or positive
  captured-process exit; current session remains Running after refusal.
- R-4: strict resume and durable budget | A/B/X methods, existing start/supervision/
  concurrency suites | same native ID, G2 only once, durable timestamp/receipt
  gates intact at exact 24h, no bypass of human or launch holds.
- R-5: premature/lost delivery acceptance | H/F/X methods and existing delivery
  suites | exact complete correlated UserPrompt in each destination after its
  attempt floor; old/wrong/partial/Sent/ack evidence never clears AwaitingCheck.
- R-6: crash and duplicate coordination | X methods plus named cut matrix |
  fresh processes prove one claimed action/reservation; each original notification
  reaches its recipient once without retyping an already accepted body.
- R-7: audit/attention/retention | N/B methods and V-7 suites |
  exact episode/actor/phase IDs, retained unresolved evidence with unrelated-pruned
  controls, meaningful Error/Warning state, no false recovered badge.

### Guard inventory

The inventory includes functional positive-path obligations where suppressing all
recovery would otherwise make safety negatives vacuously green. Every row maps to
one distinct PC. Original PC-1–33 topics remain represented in PC-1–33; extra rows
split their separately bypassable scope, timing, launch, delivery and retention
guards. No safety-critical guard is declared “none”. G-161 was explicitly
unverifiable at the TestDesign base; the H-5 amendment supplies its proposed seam
for TestDesign re-review. A mapping alone is not an executable seam.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | S1 evidence: Eligible auto continuation remains actionable. | PC-1 |
| G-2 | D2: Only explicit auto trigger qualifies. | PC-2 |
| G-3 | Evidence 3: Assistant text permanently disqualifies B. | PC-3 |
| G-4 | S2 observation: Failed pull is not successful unchanged observation. | PC-4 |
| G-5 | S2 observation: Persisted catch-up is reevaluated. | PC-5 |
| G-6 | D4 commit: Commit compares accepted generation. | PC-6 |
| G-7 | S4 pre-stop: Expired untyped occupant uses dedicated failure before D8. | PC-7 |
| G-8 | D6: Never-attempted requires every existing metadata field. | PC-8 |
| G-9 | S4 legacy: Legacy admission closes before task creation. | PC-9 |
| G-10 | S4 routed: Candidate selection excludes the stalled physical seat. | PC-10 |
| G-11 | D4 persistence: Episode survives service recreation and incident pruning. | PC-11 |
| G-12 | S4 cleanup: Cleanup runs while Working. | PC-12 |
| G-13 | D5/S6: Automatic path reaches the accepted strict resume. | PC-13 |
| G-14 | S4 unchanged D8: Ordinary Working/Pending keeps its task. | PC-14 |
| G-15 | D2 physical seat: Physical seat must be AlwaysOn. | PC-15 |
| G-16 | Evidence 2: Boundary is required. | PC-16 |
| G-17 | S2 tail: Partial final line makes observation Unknown. | PC-17 |
| G-18 | S2 stop final: Output revision is rechecked immediately before signal. | PC-18 |
| G-19 | S2 stop identity: Runner compares accepted generation. | PC-19 |
| G-20 | D10 exit: Only positive matching exit allows resume. | PC-20 |
| G-21 | D1 human stop: Human intent is checked before stop RPC. | PC-21 |
| G-22 | D10 audit: Stop intent commits before RPC. | PC-22 |
| G-23 | Crash lost response: Absence cannot establish captured-generation exit. | PC-23 |
| G-24 | Crash reservation: Persisted G2 is reused after crash. | PC-24 |
| G-25 | D9 supervisor: Active action excludes ordinary supervisor. | PC-25 |
| G-26 | D9 24h: Rolling timestamp survives pruning/recreation. | PC-26 |
| G-27 | D9 receipt: Elapsed 24 hours does not replace caller receipt. | PC-27 |
| G-28 | D5 history: Recovery uses strict same-conversation resume. | PC-28 |
| G-29 | S4 retired generation: Confirmed exit retires young untyped occupants. | PC-29 |
| G-30 | S6 receipt: Sent alone cannot clear recovery attention. | PC-30 |
| G-31 | D3 disable stop: Zero revokes a confirmed but unissued stop. | PC-31 |
| G-32 | D2 queue: Unresolved attempted input vetoes stop. | PC-32 |
| G-33 | S3 fence: Durable restart fence precedes G2 admission. | PC-33 |
| G-34 | D2 owner: Logical owner must be AlwaysOn. | PC-34 |
| G-35 | D2 specialist: Check role is required independently of AlwaysOn. | PC-35 |
| G-36 | D2 provider: Effective execution must be ClaudeCode. | PC-36 |
| G-37 | D2 pool: Pool ownership vetoes action. | PC-37 |
| G-38 | D2 ownership: Current physical standing ownership must be positive. | PC-38 |
| G-39 | D2 legacy correlation: Slug identity alone cannot authorize stop. | PC-39 |
| G-40 | D2 human work: Human ordinary prompt vetoes automatic action. | PC-40 |
| G-41 | Evidence tool: Unmatched tool before B vetoes action. | PC-41 |
| G-42 | D2 open work: Open non-Check assignment vetoes action. | PC-42 |
| G-43 | D2 cards: Pending card assignment vetoes action. | PC-43 |
| G-44 | D2 enabled: Physical seat must remain enabled. | PC-44 |
| G-45 | D2 enabled owner: Logical owner must remain enabled. | PC-45 |
| G-46 | D2 Check feature: Check interpreter must remain enabled. | PC-46 |
| G-47 | Evidence 2 continuation: C is required after newest B. | PC-47 |
| G-48 | D2 status: Session must be Running. | PC-48 |
| G-49 | D4 Working: Existing Working verdict is required. | PC-49 |
| G-50 | Evidence Thinking: Thinking permanently disqualifies B. | PC-50 |
| G-51 | Evidence ToolCall: ToolCall after B vetoes. | PC-51 |
| G-52 | Evidence ToolResult: ToolResult after B vetoes. | PC-52 |
| G-53 | Evidence prompt: Ordinary UserPrompt after B vetoes. | PC-53 |
| G-54 | Evidence unknown: Unknown substantive record/error/wall is conservative. | PC-54 |
| G-55 | Evidence end: Effective end after B vetoes. | PC-55 |
| G-56 | Evidence repeat: Newest silent B/C resets grace. | PC-56 |
| G-57 | Evidence ordering: Stale backfill cannot establish current episode. | PC-57 |
| G-58 | Evidence wait: Wait starts at maximum event/arrival/generation time. | PC-58 |
| G-59 | D3 settings: Minimum nonzero threshold is ten. | PC-59 |
| G-60 | S2 read error: I/O failure makes fresh-tail observation Unknown. | PC-60 |
| G-61 | S2 parse: Unparsed new record makes observation Unknown. | PC-61 |
| G-62 | S2 binding: Claim switch invalidates observation. | PC-62 |
| G-63 | S2 native identity: DB sequence cannot stand in for native identity. | PC-63 |
| G-64 | S2 persist: Partial persistence cannot confirm silence. | PC-64 |
| G-65 | S2 stop serialization: Final check and signal exclude observed output update. | PC-65 |
| G-66 | S2 final transcript: Final transcript revision mismatch refuses. | PC-66 |
| G-67 | S2 final binding: Final binding revision mismatch refuses. | PC-67 |
| G-68 | S2 capability: Missing capability sends no stop POST. | PC-68 |
| G-69 | S2 fallback: Refused/unknown stop never uses generic termination or input. | PC-69 |
| G-70 | S2 wire correlation: Stop response belongs to requested attempt and generation. | PC-70 |
| G-71 | S2 attempt replay: One runner attempt cannot signal twice. | PC-71 |
| G-72 | D1 queued human stop: Human intent is rechecked by queued resume worker. | PC-72 |
| G-73 | D3 disable resume: Zero blocks not-yet-issued resume. | PC-73 |
| G-74 | D3 delegation stop: Delegation disabled blocks stop effects. | PC-74 |
| G-75 | D3 delegation resume: Delegation disabled blocks resume effects. | PC-75 |
| G-76 | S3 disabled reconcile: Already-issued actions reconcile even when disabled. | PC-76 |
| G-77 | D9 active CAS: Concurrent server workers have one active operation. | PC-77 |
| G-78 | S1 episode uniqueness: Duplicate detection cannot create duplicate episode rows. | PC-78 |
| G-79 | D9 allowance commit: Allowance is consumed with durable stop request. | PC-79 |
| G-80 | D9 no refund: Ambiguous effects never refund allowance. | PC-80 |
| G-81 | D9 useful Check: Whole receipt without useful result cannot unlock budget. | PC-81 |
| G-82 | D9 healthy reset: Generic healthy uptime cannot reset restart budget. | PC-82 |
| G-83 | D9 failed action: Failed compaction action excludes supervisor retry. | PC-83 |
| G-84 | S3 capacity competitor: Capacity recovery cannot relaunch action-owned seat. | PC-84 |
| G-85 | S3 reservation token: Queued worker retains immutable G2. | PC-85 |
| G-86 | S3 pointer: Queued resume rechecks current pointer. | PC-86 |
| G-87 | S3 episode: Queued resume rechecks episode ownership. | PC-87 |
| G-88 | S3 no replay: Recovery does not enqueue an initial or old prompt. | PC-88 |
| G-89 | S3 missing history: Strict resume never falls back after missing native history. | PC-89 |
| G-90 | S3 old exits: G exit/results cannot close G2. | PC-90 |
| G-91 | S3 stop refusal: Stop refusal cannot mark live session Failed/Stopped. | PC-91 |
| G-92 | S4 D9 unchanged: Ordinary Working protects against generic kill. | PC-92 |
| G-93 | S4 age: Pre-stop release requires own delivery deadline. | PC-93 |
| G-94 | S4 episode age: Pre-stop release requires overdue episode. | PC-94 |
| G-95 | S4 identity: Only exact task/session/generation occupant is failed. | PC-95 |
| G-96 | S4 brief linkage: Missing or mismatched brief never proves untyped Check. | PC-96 |
| G-97 | S4 final dispatch: Final standing dispatch consults current episode. | PC-97 |
| G-98 | S4 routed claim: Routed claim rechecks episode after selection. | PC-98 |
| G-99 | S4 alternate scope: Blocked primary does not block healthy physical alternate. | PC-99 |
| G-100 | S4 no silent fallback: Routed exhaustion cannot fall back to blocked legacy primary. | PC-100 |
| G-101 | S4 terminal cleanup: Cleanup requires terminal owning Check. | PC-101 |
| G-102 | S4 cleanup recovery: Failure-commit/cleanup gap is recovered. | PC-102 |
| G-103 | S4 failure side effects: Dedicated failure cannot call StopDelegate or retry ladder. | PC-103 |
| G-104 | S4 owner delivered: Open owning delivered Check is interrupted, not undelivered. | PC-104 |
| G-105 | S4 terminal owner: Already terminal owning Check is immutable. | PC-105 |
| G-106 | S4 transaction before admission: All retirement/outbox commits precede G2 admission. | PC-106 |
| G-107 | S5 intent audit: Action audit records authorized automatic actor. | PC-107 |
| G-108 | S5 attention: Unresolved episode visible without incident or routing rows. | PC-108 |
| G-109 | S5 false recovery: Launch ack does not mean Recovered. | PC-109 |
| G-110 | S5 late progress: Late progress aborts without restart-success claim. | PC-110 |
| G-111 | S5 health: Running stalled seat is not warm-ready. | PC-111 |
| G-112 | S6 interpretation receipt: Recovery requires whole new interpreter brief receipt. | PC-112 |
| G-113 | S6 result identity: Recovery Check must be newly created for G2. | PC-113 |
| G-114 | S6 useful reading: Empty/invalid/uncorrelated report does not prove recovery. | PC-114 |
| G-115 | S6 caller identity: Caller receipt must match intended session. | PC-115 |
| G-116 | S6 receipt floor: Receipt must follow matching delivery attempt floor. | PC-116 |
| G-117 | S6 report end: Useful result requires closing correlated report/end. | PC-117 |
| G-118 | S6 failure obligation: Task failure/event/outbox are atomic. | PC-118 |
| G-119 | S6 failure recovery: Committed failure outbox is recovered after death. | PC-119 |
| G-120 | S6 keyed enqueue: Notification retry keeps one queue identity. | PC-120 |
| G-121 | S6 caller busy: Busy caller cannot be typed into. | PC-121 |
| G-122 | S6 already eligible: Eligible caller receives from producer's flush. | PC-122 |
| G-123 | S6 accepted crash: Complete recipient evidence prevents retyping after lost verdict. | PC-123 |
| G-124 | S5 publish recovery: Committed incident/attention publication survives failure. | PC-124 |
| G-125 | S1 retention: Unresolved episode row cannot be pruned. | PC-125 |
| G-126 | S1 terminal audit: Terminal action audit survives at least 90 days. | PC-126 |
| G-127 | S3 discovery: An eligible seat without pending tasks is discovered. | PC-127 |
| G-128 | S3 isolation: Observation failure on one seat does not stop other seats. | PC-128 |
| G-129 | D4 false idle: Detection cannot manufacture an end or stopped state. | PC-129 |
| G-130 | D2/D5 stop: model hold independently vetoes stop. | PC-130 |
| G-131 | D2/D5 resume: model hold independently vetoes resume. | PC-131 |
| G-132 | D2/D5 stop: quota hold independently vetoes stop. | PC-132 |
| G-133 | D2/D5 resume: quota hold independently vetoes resume. | PC-133 |
| G-134 | D2/D5 stop: authentication refusal independently vetoes stop. | PC-134 |
| G-135 | D2/D5 resume: authentication refusal independently vetoes resume. | PC-135 |
| G-136 | D2/D5 stop: capacity hold independently vetoes stop. | PC-136 |
| G-137 | D2/D5 resume: capacity hold independently vetoes resume. | PC-137 |
| G-138 | D2/D5 stop: continuity hold independently vetoes stop. | PC-138 |
| G-139 | D2/D5 resume: continuity hold independently vetoes resume. | PC-139 |
| G-140 | D2/D5 stop: Herdr hold independently vetoes stop. | PC-140 |
| G-141 | D2/D5 resume: Herdr hold independently vetoes resume. | PC-141 |
| G-142 | D2/D5 stop: boot liveness hold independently vetoes stop. | PC-142 |
| G-143 | D2/D5 resume: boot liveness hold independently vetoes resume. | PC-143 |
| G-144 | D6 metadata: DeliveryAttempts independently proves attempted/settled state. | PC-144 |
| G-145 | D6 metadata: LastDeliveryStartedAt independently proves attempted/settled state. | PC-145 |
| G-146 | D6 metadata: DeliveryVerdict independently proves attempted/settled state. | PC-146 |
| G-147 | D6 metadata: DeliveryVerdictAt independently proves attempted/settled state. | PC-147 |
| G-148 | D6 metadata: SentAt independently proves attempted/settled state. | PC-148 |
| G-149 | D6 metadata: CanceledAt independently proves attempted/settled state. | PC-149 |
| G-150 | D6 metadata: ChannelReplySettledAt independently proves attempted/settled state. | PC-150 |
| G-151 | S4 orphan input: Unrelated pending input vetoes stop. | PC-151 |
| G-152 | S3 scope recheck: Queued resume revalidates D2 scope after reservation. | PC-152 |
| G-153 | D4 pointer commit: Episode commit rechecks current pointer. | PC-153 |
| G-154 | D4 owner commit: Episode commit rechecks standing owner. | PC-154 |
| G-155 | S2 tail format: Non-Claude tailer cannot certify compaction silence. | PC-155 |
| G-156 | S3 reservation commit: Resume reservation commits before enqueue. | PC-156 |
| G-157 | S3 fence bookkeeping: Fence repair retries bookkeeping without another restart. | PC-157 |
| G-158 | S1 receipt retention: Receipt gate survives terminal episode pruning. | PC-158 |
| G-159 | S6 exact caller body: Partial matching caller body is not receipt. | PC-159 |
| G-160 | S6 suppression: Suppressed Check note cannot release recovery gate. | PC-160 |
| G-161 | S6 legacy publication: Each Produced legacy recovery Check note has immutable P/event/outbox identity and restart discovery before enqueue; contract supplied by the H-5 amendment, pending TestDesign re-review. | PC-161 |
| G-162 | S1 retained evidence: Unresolved recovery protects session evidence. | PC-162 |
| G-163 | S1 retained evidence: Unresolved recovery protects transcript evidence. | PC-163 |
| G-164 | S1 retained evidence: Unresolved recovery protects task_tree evidence. | PC-164 |
| G-165 | S1 retained evidence: Unresolved recovery protects queue evidence. | PC-165 |
| G-166 | S3 termination provenance: Automatic exit never masquerades as human Stop. | PC-166 |
| G-167 | S5 evidence bounds: Audit cannot persist complete prompts or unbounded diagnostics. | PC-167 |
| G-168 | S4 Check-only prune: Cleanup never prunes non-Check task briefs. | PC-168 |
| G-169 | S4 unrelated prune: Cleanup never prunes unrelated human/system input. | PC-169 |

### Positive controls

Each PC-n breaks only G-n. The defect column specifies a production change
that must compile once the planned implementation is present; the last column
names the exact test assertion that must fail. Names here refine the earlier
proposed names. Alias plus method is an exact class/method selector, not a
wildcard. Code implements these ordinary tests; ordinary Review judges them
before land. Mutation reports **break, intended red, restore, fresh green**
after land, including actual nonzero counts and each argument variant.

| Alias | Exact class | File to implement/extend |
|---|---|---|
| P | `CompactionContinuationPolicyTests` | `tests/Antiphon.Tests/Application/CompactionContinuationPolicyTests.cs` |
| D | `CheckCompactionContinuationTests` | `tests/Antiphon.Tests/Application/CheckCompactionContinuationTests.cs` |
| A | `CheckCompactionAutomaticRestartTests` | `tests/Antiphon.Tests/Application/CheckCompactionAutomaticRestartTests.cs` |
| F | `CheckCompactionRecoveryFlowTests` | `tests/Antiphon.Tests/Application/CheckCompactionRecoveryFlowTests.cs` |
| B | `CheckCompactionRecoveryPersistenceTests` | `tests/Antiphon.Tests/Application/CheckCompactionRecoveryPersistenceTests.cs` |
| T | `TranscriptTailerObservationTests` | `tests/Antiphon.SessionRunner.Tests/TranscriptTailerObservationTests.cs` |
| S | `CompactionContinuationStopTests` | `tests/Antiphon.SessionRunner.Tests/CompactionContinuationStopTests.cs` |
| W | `CompactionContinuationWireTests` | `tests/Antiphon.Tests/Agents/CompactionContinuationWireTests.cs` |
| H | `CheckNoteDeliveryHandoffTests` | `tests/Antiphon.Tests/Application/CheckNoteDeliveryHandoffTests.cs` |
| N | `CheckCompactionAttentionTests` | `tests/Antiphon.Tests/Application/CheckCompactionAttentionTests.cs` |
| C | `CheckCompactionContinuationSettingsTests` | `tests/Antiphon.Tests/Application/CheckCompactionContinuationSettingsTests.cs` |
| X | `CheckCompactionCrashTests` | `tests/Antiphon.Tests/Application/CheckCompactionCrashTests.cs` |

All A/D/F/B/N tests use the real coordinator or named production boundary in the
isolated fixture. P/C call the pure policy/validator. S/T use the real runner/
tailer with owned files/processes. W uses the production HTTP client and also
has the V-3 real endpoint counterpart. H runs the full producer/queue/transcript
chain. X owns real child workers. These classifications do not permit replacing
production state transitions with fixture writes.

To isolate a final guard, first drive earlier guards successfully, pause at the
named real handoff, change only the guarded fact, and resume. For final-runner
revision controls, preserve unrelated revisions: output-only for PC-18;
parsed harmless metadata with unchanged silent predicate for PC-66; same-content
rebind for PC-67. If implementation invalidates several tokens at once, construct
the request from the new observation with only the tested token stale; never
accept a mutant rejected by a different assertion. PC-65 requires an output-writer
rendezvous and completion receipts on both contenders; sleeps cannot prove it.

For NeverAttempted, mutate the shared predicate and populate only the named
field; PC-8 is the baseline-zero member. PC-144 uses DeliveryAttempts=1 with
all timestamps/verdicts empty. For holds, record the attempted boundary before
unrelated startup validation can fail: missing DI, native history or capability
must never make a no-effect assertion pass. Safety tests also assert the expected
refusal/phase, immutable other-session state, zero raw input/unconditional kill,
and a separately eligible positive case.

PC-78 uses a fresh database migrated from the mutated model/migration, not a
clone retaining the original index. Regenerate the altered migration through
EF CLI; do not hand-write production migrations. PC-125 and PC-162–165 separately
mutate episode/session/transcript/task/queue retention, one query per control,
with an unrelated eligible row which must still prune.

| Control | Exact method | Compiling defect to introduce | Required red assertion |
|---|---|---|---|
| PC-1 | `P.Auto_boundary_with_silent_continuation_is_overdue_at_ten_minutes` | Return NotEligible for the explicit auto boundary arm. | `verdict.Overdue.ShouldBeTrue() at exactly 10:00` |
| PC-2 | `P.Manual_and_unknown_compaction_never_arm_recovery` | Accept manual and missing trigger as auto. | `verdict.Eligible.ShouldBeFalse() for each trigger` |
| PC-3 | `P.Post_boundary_assistant_work_excludes_silent_continuation` | Remove AssistantText from the progress veto. | `verdict.Eligible.ShouldBeFalse() even 24 hours later` |
| PC-4 | `D.Successful_unchanged_pull_confirms_but_failed_pull_does_not` | Map unavailable observation to SuccessNoChange. | `episodes.Count.ShouldBe(0) in failed-pull arm; successful unchanged arm has one` |
| PC-5 | `D.Catch_up_progress_saves_the_suspected_session` | Reuse the pre-pull eligible verdict after successful pull. | `episodes.Count.ShouldBe(0) after newly received reply` |
| PC-6 | `D.Old_generation_observation_cannot_hold_the_replacement` | Omit the generation comparison at episode commit. | `replacementEpisodes.Count.ShouldBe(0) after same-ID resume` |
| PC-7 | `F.Held_recovery_releases_the_expired_untyped_occupant_without_generic_kill` | Restore the unconditional D8 continue before the compaction branch. | `occupant.Status.ShouldBe(Failed); genericKillCalls.Count.ShouldBe(0)` |
| PC-8 | `F.Attempted_or_sent_brief_retains_its_existing_owner_and_delivery_evidence` | Replace NeverAttempted with DeliveryAttempts == 0. | `row.Status.ShouldBe(Pending) with only LastDeliveryBaselineSequence=0` |
| PC-9 | `F.Legacy_check_refuses_the_stalled_primary_before_task_creation` | Omit legacy RunAsync episode refusal. | `newCheckTasks.Count.ShouldBe(0)` |
| PC-10 | `F.Routed_check_excludes_only_the_stalled_physical_seat` | Remove episode filtering from routed candidate selection. | `selectedPhysicalAgentId.ShouldBe(healthyAlternateId)` |
| PC-11 | `D.Confirmed_episode_survives_restart_and_incident_pruning` | Make current-episode lookup depend on matching incident existence. | `currentEpisodeId.ShouldBe(originalEpisodeId) after pruning` |
| PC-12 | `F.Terminal_untyped_briefs_are_pruned_while_working_after_a_commit_gap` | Return from compaction cleanup when IsWorkingAsync is true. | `obsoleteRow.Status.ShouldBe(Canceled)` |
| PC-13 | `H.Automatic_restart_delivers_a_new_check_and_its_whole_caller_note` | Omit Stopped-to-ResumeReserved transition. | `acceptedGenerations.ShouldBe([G,G2]) before whole interpreter and caller receipts` |
| PC-14 | `F.Healthy_working_session_still_defers_delivery_and_is_never_killed` | Remove ordinary Working/Pending D8 defer. | `healthyTask.Status.ShouldBe(Dispatched)` |
| PC-15 | `A.Physical_always_on_is_required` | Delete physical.AlwaysOn clause from shared action scope. | `conditionalStopCalls.Count.ShouldBe(0) for otherwise eligible non-AlwaysOn seat` |
| PC-16 | `P.Ten_minutes_without_a_turn_end_is_insufficient_without_boundary` | Treat missing B as ordinary-prompt time. | `verdict.Eligible.ShouldBeFalse() for 24-hour ordinary Check turn` |
| PC-17 | `T.Tail_observation_partial_line_is_unknown` | Return Success while pending bytes remain without LF. | `observation.IsSuccessful.ShouldBeFalse() with complete old B/C and partial new record` |
| PC-18 | `S.Late_output_prevents_conditional_stop` | Delete final output-revision comparison after the barrier. | `stopSignals.Count.ShouldBe(0) after output-only append` |
| PC-19 | `S.Replacement_generation_is_never_stopped` | Delete expectedAcceptedStartedAt comparison in conditional stop. | `replacementProcess.HasExited.ShouldBeFalse() for request carrying G` |
| PC-20 | `A.Unsupported_or_ambiguous_stop_never_falls_back_or_resumes` | Treat accepted-but-not-exited result as Stopped. | `resumeReservations.Count.ShouldBe(0) while captured process is alive` |
| PC-21 | `A.Human_stop_between_detection_and_stop_revokes_recovery` | Omit final Suspended check before conditional stop. | `conditionalStopCalls.Count.ShouldBe(0) after Stop commits at barrier` |
| PC-22 | `A.Restart_intent_must_commit_before_any_runner_effect` | Issue conditional-stop RPC before committing StopRequested. | `conditionalStopCalls.Count.ShouldBe(0) after injected transaction-commit failure` |
| PC-23 | `A.Lost_stop_response_reconciles_only_the_captured_generation` | Treat Missing runner result as captured exit during reconciliation. | `resumeReservations.Count.ShouldBe(0); episode remains NeedsDecision` |
| PC-24 | `X.Resume_reservation_survives_crash_without_a_second_launch` | Discard episode ResumeGeneration when recovering reserved launch. | `acceptedGenerations.Distinct().Count().ShouldBe(2) after worker death` |
| PC-25 | `A.Supervisor_cannot_bypass_an_active_compaction_operation` | Remove active-operation gate in AgentSupervisorService. | `supervisorLaunches.Count.ShouldBe(0) while operation owns seat` |
| PC-26 | `B.One_compaction_restart_per_day_survives_pruning_and_service_recreation` | Admit when incidents are absent instead of reading durable last-attempt timestamp. | `secondStopRequests.Count.ShouldBe(0) at 23:59:59.999` |
| PC-27 | `B.A_second_restart_needs_caller_receipt_even_after_a_day` | Remove caller-receipt half of budget eligibility. | `secondStopRequests.Count.ShouldBe(0) after 24h with useful Check but no receipt` |
| PC-28 | `A.Automatic_recovery_preserves_history` | Set recovery Fresh to true instead of false. | `acceptedResume.SessionId.ShouldBe(oldSessionId); native marker unchanged` |
| PC-29 | `F.Confirmed_stop_retires_exact_old_checks_without_replay` | Apply pre-stop watchdog age restriction to post-stop retirement. | `youngOccupant.Status.ShouldBe(Failed) with reason CompactionRecoveryRetiredGeneration` |
| PC-30 | `N.Resumed_is_not_recovered_until_the_whole_parent_note_arrives` | Use queue.Status == Sent as receipt predicate. | `episode.State.ShouldBe(AwaitingCheck) for Sent without matching prompt` |
| PC-31 | `A.Disabling_recovery_revokes_unissued_stop` | Omit effective zero threshold check at stop handoff. | `conditionalStopCalls.Count.ShouldBe(0) after threshold becomes zero` |
| PC-32 | `A.Attempted_input_vetoes_the_automatic_stop` | Remove unresolved-input veto from stop preflight. | `conditionalStopCalls.Count.ShouldBe(0) for attempted Pending and unconfirmed Sent` |
| PC-33 | `A.Failed_restart_fence_write_keeps_new_generation_admission_closed` | Open admission despite failed WriteRestartBoundaryIfInterruptedAsync persistence. | `newCheckTasks.Count.ShouldBe(0) even when the runtime is otherwise idle` |
| PC-34 | `A.Logical_owner_always_on_is_required` | Delete logicalOwner.AlwaysOn clause. | `conditionalStopCalls.Count.ShouldBe(0) with physical seat still AlwaysOn` |
| PC-35 | `A.Non_check_standing_specialist_never_restarts` | Accept any StandingSpecialistRole. | `conditionalStopCalls.Count.ShouldBe(0) for AlwaysOn Diagnose/Distill seats` |
| PC-36 | `A.Effective_non_claude_check_never_restarts` | Skip effective AgentKind comparison. | `conditionalStopCalls.Count.ShouldBe(0) for Codex/Grok effective overrides` |
| PC-37 | `A.Pool_owned_check_never_restarts` | Remove pool-owned exclusion. | `conditionalStopCalls.Count.ShouldBe(0) with all other Check facts true` |
| PC-38 | `A.Unproven_standing_owner_never_restarts` | Accept missing StandingAgentId/ambiguous legacy ownership. | `conditionalStopCalls.Count.ShouldBe(0) for unproven owner` |
| PC-39 | `A.Slug_lookalike_without_correlated_check_never_restarts` | Use configured slug match as sufficient prompt correlation. | `conditionalStopCalls.Count.ShouldBe(0) for matching slug with unrelated prompt` |
| PC-40 | `A.Human_turn_vetoes_automatic_restart` | Ignore current prompt origin/correlation when selecting owning Check. | `conditionalStopCalls.Count.ShouldBe(0) for human turn after old Check` |
| PC-41 | `P.Unmatched_tool_before_boundary_is_not_silent` | Remove unmatched pre-boundary ToolUseId check. | `verdict.Eligible.ShouldBeFalse() until matching result precedes B` |
| PC-42 | `A.Non_check_execution_vetoes_stop` | Remove open non-Check execution query. | `conditionalStopCalls.Count.ShouldBe(0) despite correlated Check tail` |
| PC-43 | `A.Pending_card_work_vetoes_stop` | Remove pending card assignment query. | `conditionalStopCalls.Count.ShouldBe(0)` |
| PC-44 | `A.Disabled_physical_seat_vetoes_stop` | Remove physical-seat enabled predicate. | `conditionalStopCalls.Count.ShouldBe(0)` |
| PC-45 | `A.Disabled_logical_owner_vetoes_stop` | Remove logical-owner enabled predicate. | `conditionalStopCalls.Count.ShouldBe(0)` |
| PC-46 | `A.Disabled_check_interpreter_vetoes_stop` | Ignore CheckInterpreterEnabled at action preflight. | `conditionalStopCalls.Count.ShouldBe(0)` |
| PC-47 | `P.Missing_continuation_never_arms_recovery` | Accept boundary without synthetic continuation. | `verdict.Eligible.ShouldBeFalse()` |
| PC-48 | `P.Non_running_session_never_arms_recovery` | Drop Running requirement. | `verdict.Eligible.ShouldBeFalse() for Starting/Stopped/Failed` |
| PC-49 | `P.Idle_compaction_never_arms_recovery` | Drop existing Working prerequisite. | `verdict.Eligible.ShouldBeFalse() for ended Check then auto B/C` |
| PC-50 | `P.Thinking_after_boundary_permanently_disqualifies_it` | Remove Thinking from substantive activity switch. | `verdict.Eligible.ShouldBeFalse() after another 24h` |
| PC-51 | `P.Tool_call_after_boundary_disqualifies_it` | Remove ToolCall from substantive activity switch. | `verdict.Eligible.ShouldBeFalse()` |
| PC-52 | `P.Tool_result_after_boundary_disqualifies_it` | Remove ToolResult from substantive activity switch. | `verdict.Eligible.ShouldBeFalse()` |
| PC-53 | `P.Ordinary_prompt_after_boundary_disqualifies_it` | Ignore ordinary UserPrompt after B. | `verdict.Eligible.ShouldBeFalse()` |
| PC-54 | `P.Unknown_activity_after_boundary_is_not_silence` | Make default substantive activity arm ignore unknown kinds. | `verdict.Eligible.ShouldBeFalse() for unknown record; error/wall variants included` |
| PC-55 | `P.Turn_end_after_boundary_disqualifies_it` | Ignore effective ends after boundary. | `verdict.Eligible.ShouldBeFalse() for TurnEnd/interrupt/manual/restart boundary` |
| PC-56 | `P.Repeated_boundaries_postpone_the_grace` | Select first boundary instead of latest silent boundary. | `verdict.Overdue.ShouldBeFalse() when old B is 11m and newest B/C is 1m` |
| PC-57 | `P.Historical_backfill_cannot_arm_current_generation` | Remove accepted-start/latest-effective-end ownership fence. | `verdict.Eligible.ShouldBeFalse() for historical B appended above current end` |
| PC-58 | `P.Latest_arrival_controls_the_ten_minute_boundary` | Use B.Timestamp alone instead of max of all five times. | `verdict.Overdue.ShouldBeFalse() with old B event and fresh C arrival` |
| PC-59 | `C.Only_zero_or_at_least_ten_minutes_validate` | Change validator minimum from 10 to 1. | `validation.Succeeded.ShouldBeFalse() for 1 through 9; negative also fails` |
| PC-60 | `T.Unreadable_bound_file_is_unknown` | Return success from the observation IOException handler. | `observation.IsSuccessful.ShouldBeFalse()` |
| PC-61 | `T.Unparsed_new_line_is_unknown` | Ignore normalization failure when completing read-to-end receipt. | `observation.IsSuccessful.ShouldBeFalse() after malformed LF-terminated record` |
| PC-62 | `T.Claim_switch_during_read_invalidates_observation` | Omit post-read binding identity comparison. | `observation.IsSuccessful.ShouldBeFalse() after claim revoke/rebind barrier` |
| PC-63 | `D.Native_boundary_identity_must_match` | Compare only DB boundary sequence and ignore native boundary UUID. | `episodes.Count.ShouldBe(0) when two native files have the same sequences` |
| PC-64 | `D.Persist_failure_withholds_automatic_action` | Ignore TryGetTranscriptPersistFailure/partial result in new observation API. | `conditionalStopCalls.Count.ShouldBe(0) when a post-B row fails persistence` |
| PC-65 | `S.Observed_output_wins_the_termination_race` | Release observation lock between final validation and process signal. | `stopSignals.Count.ShouldBe(0) when output wins the controlled rendezvous` |
| PC-66 | `S.Late_transcript_revision_prevents_stop` | Delete final transcript-revision equality check. | `stopSignals.Count.ShouldBe(0) for late harmless parsed metadata revision with unchanged silent predicate` |
| PC-67 | `S.Late_binding_revision_prevents_stop` | Delete final binding-revision equality check. | `stopSignals.Count.ShouldBe(0) for rebound same-content file` |
| PC-68 | `W.Missing_compaction_capability_sends_no_stop_request` | Remove compactionContinuationStopV1 capability gate. | `postedStopRequests.Count.ShouldBe(0) for features=null/missing` |
| PC-69 | `W.Conditional_stop_never_falls_back` | On conditional-stop refusal invoke existing KillAsync. | `genericKillRequests.Count.ShouldBe(0); rawInputRequests.Count.ShouldBe(0)` |
| PC-70 | `W.Crossed_stop_response_cannot_confirm_exit` | Accept mismatched response attempt/generation. | `result.ConfirmsExit.ShouldBeFalse()` |
| PC-71 | `S.Duplicate_stop_attempt_has_one_effect` | Ignore existing attempt identity at runner conditional stop. | `stopSignals.Count.ShouldBe(1) for two identical accepted requests` |
| PC-72 | `A.Human_stop_before_queued_resume_revokes_launch` | Remove queued worker Suspended/start-intent check. | `adapterCreates.Count.ShouldBe(0) after human Stop before worker dequeue` |
| PC-73 | `A.Zero_after_exit_prevents_resume` | Omit effective zero check at resume reservation. | `resumeReservations.Count.ShouldBe(0); phase.ShouldBe(DisabledNeedsDecision)` |
| PC-74 | `A.Disabled_delegation_prevents_unissued_stop` | Remove Delegation.Enabled check before stop handoff. | `conditionalStopCalls.Count.ShouldBe(0)` |
| PC-75 | `A.Disabled_delegation_prevents_unissued_resume` | Remove Delegation.Enabled check before resume handoff. | `resumeReservations.Count.ShouldBe(0)` |
| PC-76 | `A.Disabled_discovery_still_reconciles_issued_action` | Return before existing-action reconciliation when Delegation.Enabled=false. | `episode.State.ShouldBe(DisabledNeedsDecision) after matching exit is observed` |
| PC-77 | `X.Concurrent_workers_claim_one_stop_allowance` | Remove row-lock/CAS admission of ActiveCompactionRecoveryId. | `stopAttemptIds.Distinct().Count().ShouldBe(1)` |
| PC-78 | `B.Duplicate_boundary_has_one_durable_episode` | Remove the unique episode-key index from EF model and generated migration. | `duplicateInsertRejected.ShouldBeTrue() using two real contexts` |
| PC-79 | `X.Stop_commit_spends_allowance_before_worker_death` | Omit last-automatic-attempt write from StopRequested transaction. | `storedLastAttempt.ShouldBe(stopRequestedAt) after killed worker` |
| PC-80 | `B.Unknown_stop_does_not_refund_allowance` | Clear last-attempt timestamp on stop timeout. | `secondStopRequests.Count.ShouldBe(0)` |
| PC-81 | `B.Receipt_without_useful_check_does_not_unlock_restart` | Remove useful-reading requirement from eligibility. | `secondStopRequests.Count.ShouldBe(0) after 24h and receipt for empty result` |
| PC-82 | `B.Healthy_uptime_does_not_reset_compaction_budget` | Clear compaction last-attempt and receipt gate in supervisor healthy-reset branch. | `storedLastAttempt.ShouldBe(originalAttemptTime)` |
| PC-83 | `A.Failed_resume_remains_outside_supervisor_ladder` | Gate supervisor only for nonterminal episodes, admitting failed action. | `supervisorLaunches.Count.ShouldBe(0) across backoff ticks` |
| PC-84 | `A.Capacity_recovery_stands_down_for_action_owned_seat` | Omit episode gate at capacity launch admission. | `capacityLaunches.Count.ShouldBe(0)` |
| PC-85 | `A.Queued_resume_cannot_borrow_a_replacement_generation` | Fill queued generation from current AgentSession.StartedAt. | `obsoleteAdapterCreates.Count.ShouldBe(0) after G3 replaces G2` |
| PC-86 | `A.Changed_pointer_revokes_queued_resume` | Omit queued worker current-pointer comparison. | `adapterCreates.Count.ShouldBe(0) after owner points elsewhere` |
| PC-87 | `A.Superseded_episode_cannot_launch_queued_resume` | Ignore queued episode ID/current ActiveCompactionRecoveryId comparison. | `adapterCreates.Count.ShouldBe(0) after operator supersession` |
| PC-88 | `A.Strict_resume_never_replays_the_old_check` | Pass old owning Check brief as resume initialPrompt. | `newGenerationOldPromptRows.Count.ShouldBe(0)` |
| PC-89 | `A.Missing_resume_history_never_falls_back_to_fresh` | Retry missing-history launch with Fresh=true through manual selection. | `nativeLaunches.Count.ShouldBe(1); replacementSessionRows.Count.ShouldBe(0)` |
| PC-90 | `A.Late_old_exit_cannot_close_resumed_generation` | Remove generation fence from recovery completion/exit reconciliation. | `session.StartedAt.ShouldBe(G2); session.Status.ShouldBe(Running)` |
| PC-91 | `A.Conditional_refusal_preserves_live_session_status` | Set session.Status=Stopped on conditional refusal. | `session.Status.ShouldBe(Running)` |
| PC-92 | `F.Healthy_working_unattributed_task_is_not_killed` | Remove Working veto at generic watchdog kill tail. | `genericKillCalls.Count.ShouldBe(0) with Sent/unattributed task` |
| PC-93 | `F.Young_untyped_occupant_waits_until_its_deadline` | Ignore occupant DispatchedAt delivery-age check. | `occupant.Status.ShouldBe(Dispatched) at 9:59.999 despite old episode` |
| PC-94 | `F.Old_occupant_does_not_bypass_episode_grace` | Ignore episode deadline for pre-stop failure. | `occupant.Status.ShouldBe(Dispatched) when episode is 9:59.999` |
| PC-95 | `F.Foreign_generation_occupant_is_untouched` | Match occupant on AgentId alone. | `foreignTask.Status.ShouldBe(Dispatched)` |
| PC-96 | `F.Missing_or_mismatched_brief_cannot_release_occupant` | Treat absent/mismatched ExecutionTaskId as untyped. | `occupant.Status.ShouldBe(Dispatched)` |
| PC-97 | `F.Episode_confirmed_after_routing_blocks_final_dispatch` | Omit episode check in PlaceOnStandingAgentAsync. | `queuedTask.Status.ShouldBe(Queued); executionBriefs.Count.ShouldBe(0)` |
| PC-98 | `F.Episode_confirmed_after_selection_blocks_candidate_claim` | Omit episode predicate at candidate claim. | `blockedCandidateClaims.Count.ShouldBe(0)` |
| PC-99 | `F.Healthy_alternate_is_not_blocked_by_logical_owner_episode` | Look up episode by logical owner instead of physical candidate. | `selectedPhysicalAgentId.ShouldBe(healthyAlternateId)` |
| PC-100 | `F.Routed_exhaustion_does_not_reenter_blocked_primary` | Call legacy primary on routed episode refusal. | `blockedPrimaryTasks.Count.ShouldBe(0)` |
| PC-101 | `F.Open_check_brief_is_not_pruned` | Drop IsSettled from compaction brief-prune query. | `openCheckBrief.Status.ShouldBe(Pending)` |
| PC-102 | `X.Task_failure_commit_gap_retries_untyped_cleanup` | Omit terminal-brief cleanup from existing-episode reconciliation. | `obsoleteBrief.Status.ShouldBe(Canceled) after killed producer resumes` |
| PC-103 | `F.Compaction_failure_never_enters_generic_kill_or_retry` | Replace dedicated fail transaction with AgentTaskService.CancelAsync. | `delegateStopCalls.Count.ShouldBe(0)` |
| PC-104 | `F.Delivered_owning_check_keeps_delivery_evidence` | Replace owning Sent brief with Pending/zero attempts on retirement. | `deliveredBrief.ShouldBe(originalEvidenceSnapshot)` |
| PC-105 | `F.Terminal_owning_check_is_not_rewritten` | Fail owning Check without terminal-status predicate. | `terminalTask.ShouldBe(originalTaskSnapshot)` |
| PC-106 | `X.Retirement_failure_keeps_g2_admission_closed` | Open admission before retirement transaction commit. | `newCheckTasks.Count.ShouldBe(0) when retirement save fails` |
| PC-107 | `N.Automatic_audit_is_distinct_from_operator_action` | Stamp a human actor instead of check-compaction-recovery. | `audit.Actor.ShouldBe("check-compaction-recovery"); audit.Authorization.ShouldBe("CARD-0079/fed4b83c")` |
| PC-108 | `N.Legacy_episode_attention_survives_incident_pruning` | Build compaction attention from incident/routing rows only. | `attention.Items.Count(i => i.EpisodeId==episodeId).ShouldBe(1)` |
| PC-109 | `N.Launch_acknowledgement_keeps_validation_pending` | Set Recovered immediately after matching launch ack. | `episode.State.ShouldBe(AwaitingCheck); recoveryAttentionCount.ShouldBe(1)` |
| PC-110 | `N.Late_progress_aborts_without_claiming_restart` | Record Recovered instead of AbortedProgress on fresh progress. | `episode.State.ShouldBe(AbortedProgress); automaticSuccessIncidents.Count.ShouldBe(0)` |
| PC-111 | `N.Running_stalled_seat_is_not_warm_ready` | Ignore active compaction episode in physical seat health projection. | `physicalSeat.WarmReady.ShouldBeFalse()` |
| PC-112 | `H.Partial_new_interpreter_prompt_never_proves_recovery` | Accept matching task marker without whole brief. | `episode.State.ShouldBe(AwaitingCheck) for truncated brief even with correlated report` |
| PC-113 | `H.Old_check_result_cannot_recover_new_generation` | Drop Check creation/generation fence from recovery result observation. | `episode.State.ShouldBe(AwaitingCheck) for old G result` |
| PC-114 | `H.Unusable_new_check_result_keeps_recovery_pending` | Accept any terminal Succeeded Check as useful. | `episode.State.ShouldBe(AwaitingCheck) for whitespace/invalid reading` |
| PC-115 | `H.Foreign_session_receipt_cannot_recover_seat` | Remove ParentSessionId comparison from receipt lookup. | `episode.State.ShouldBe(AwaitingCheck) when exact body is in another session` |
| PC-116 | `H.Old_identical_caller_prompt_cannot_recover_seat` | Remove baseline/timestamp floor from receipt predicate. | `episode.State.ShouldBe(AwaitingCheck) for exact old body below floor` |
| PC-117 | `H.Unclosed_reading_cannot_recover_seat` | Accept AssistantText reading before corresponding TurnEnd/report settlement. | `episode.State.ShouldBe(AwaitingCheck)` |
| PC-118 | `F.Failure_and_caller_obligation_commit_together` | Save Failed/event before adding notification outside transaction. | `task.Status.ShouldBe(Dispatched) after obligation-insert fault` |
| PC-119 | `X.Committed_failure_reaches_parent_after_worker_death` | Skip delivery-failure notification reconciliation. | `matchingCompleteParentPrompts.Count.ShouldBe(1)` |
| PC-120 | `F.Failure_notification_reuses_existing_queue_row` | Ignore SourceLandNotificationId during recovery lookup. | `queueRowsForNotification.Count.ShouldBe(1)` |
| PC-121 | `H.Busy_parent_waits_then_receives_the_whole_note` | Publish recovery-produced Check note with MessageSendMode.Now. | `parentInputs.Count.ShouldBe(0) while Working` |
| PC-122 | `H.Eligible_parent_receives_without_a_rescue_tick` | Omit post-publication flush for recovery-produced Check note. | `matchingCompleteParentPrompts.Count.ShouldBe(1) before any manual sweep` |
| PC-123 | `X.Accepted_parent_prompt_survives_lost_verdict` | Skip transcript late-confirm in resumed notification delivery. | `parentSubmittedBodies.Count.ShouldBe(1)` |
| PC-124 | `X.Committed_audit_publication_is_retried_after_death` | Mark publication complete before event-bus send. | `subscribedClientEpisodeIds.ShouldContain(episodeId) after reconnect/recovery` |
| PC-125 | `B.Unresolved_episode_survives_retention` | Remove unresolved-phase exclusion from recovery-episode pruning. | `unresolvedEpisode.ShouldNotBeNull(); unrelatedOldTerminalEpisode.ShouldBeNull()` |
| PC-126 | `B.Terminal_recovery_audit_survives_ninety_days` | Apply ordinary 30-day incident retention to recovery action rows. | `episodeAt89Days.ShouldNotBeNull()` |
| PC-127 | `D.Silent_check_without_pending_tasks_is_discovered` | Drive discovery only from Pending queued-message IDs. | `episodes.Count.ShouldBe(1)` |
| PC-128 | `D.One_unavailable_seat_does_not_abort_other_discovery` | Let first observation exception escape the per-seat loop. | `healthyEligibleSeatEpisodes.Count.ShouldBe(1)` |
| PC-129 | `D.Detection_preserves_the_working_verdict` | Persist SessionRestartBoundary during Confirmed detection. | `working.ShouldBeTrue(); addedEndRows.Count.ShouldBe(0)` |
| PC-130 | `A.Model_hold_vetoes_stop` | Ignore ModelAvailability refusal in recovery stop preflight. | `conditionalStopCalls.Count.ShouldBe(0) with every other gate eligible` |
| PC-131 | `A.Model_hold_vetoes_resume` | Ignore ModelAvailability refusal in recovery resume preflight. | `adapterCreates.Count.ShouldBe(0) with every other gate eligible` |
| PC-132 | `A.Quota_hold_vetoes_stop` | Ignore SubscriptionQuotaGate refusal in recovery stop preflight. | `conditionalStopCalls.Count.ShouldBe(0) with every other gate eligible` |
| PC-133 | `A.Quota_hold_vetoes_resume` | Ignore SubscriptionQuotaGate refusal in recovery resume preflight. | `adapterCreates.Count.ShouldBe(0) with every other gate eligible` |
| PC-134 | `A.Authentication_refusal_vetoes_stop` | Ignore provider sign-in gate refusal in recovery stop preflight. | `conditionalStopCalls.Count.ShouldBe(0) with every other gate eligible` |
| PC-135 | `A.Authentication_refusal_vetoes_resume` | Ignore provider sign-in gate refusal in recovery resume preflight. | `adapterCreates.Count.ShouldBe(0) with every other gate eligible` |
| PC-136 | `A.Capacity_hold_vetoes_stop` | Ignore CapacityRecovery refusal in recovery stop preflight. | `conditionalStopCalls.Count.ShouldBe(0) with every other gate eligible` |
| PC-137 | `A.Capacity_hold_vetoes_resume` | Ignore CapacityRecovery refusal in recovery resume preflight. | `adapterCreates.Count.ShouldBe(0) with every other gate eligible` |
| PC-138 | `A.Continuity_hold_vetoes_stop` | Ignore StandingContinuityState refusal in recovery stop preflight. | `conditionalStopCalls.Count.ShouldBe(0) with every other gate eligible` |
| PC-139 | `A.Continuity_hold_vetoes_resume` | Ignore StandingContinuityState refusal in recovery resume preflight. | `adapterCreates.Count.ShouldBe(0) with every other gate eligible` |
| PC-140 | `A.Herdr_hold_vetoes_stop` | Ignore Herdr supervision gate refusal in recovery stop preflight. | `conditionalStopCalls.Count.ShouldBe(0) with every other gate eligible` |
| PC-141 | `A.Herdr_hold_vetoes_resume` | Ignore Herdr supervision gate refusal in recovery resume preflight. | `adapterCreates.Count.ShouldBe(0) with every other gate eligible` |
| PC-142 | `A.Liveness_hold_vetoes_stop` | Ignore boot-liveness latch refusal in recovery stop preflight. | `conditionalStopCalls.Count.ShouldBe(0) with every other gate eligible` |
| PC-143 | `A.Liveness_hold_vetoes_resume` | Ignore boot-liveness latch refusal in recovery resume preflight. | `adapterCreates.Count.ShouldBe(0) with every other gate eligible` |
| PC-144 | `F.DeliveryAttempts_prevents_untyped_cleanup` | Delete only DeliveryAttempts clause from NeverAttempted. | `row.Status.ShouldBe(Pending) with only DeliveryAttempts populated; input count stays zero` |
| PC-145 | `F.LastDeliveryStartedAt_prevents_untyped_cleanup` | Delete only LastDeliveryStartedAt clause from NeverAttempted. | `row.Status.ShouldBe(Pending) with only LastDeliveryStartedAt populated; input count stays zero` |
| PC-146 | `F.DeliveryVerdict_prevents_untyped_cleanup` | Delete only DeliveryVerdict clause from NeverAttempted. | `row.Status.ShouldBe(Pending) with only DeliveryVerdict populated; input count stays zero` |
| PC-147 | `F.DeliveryVerdictAt_prevents_untyped_cleanup` | Delete only DeliveryVerdictAt clause from NeverAttempted. | `row.Status.ShouldBe(Pending) with only DeliveryVerdictAt populated; input count stays zero` |
| PC-148 | `F.SentAt_prevents_untyped_cleanup` | Delete only SentAt clause from NeverAttempted. | `row.Status.ShouldBe(Pending) with only SentAt populated; input count stays zero` |
| PC-149 | `F.CanceledAt_prevents_untyped_cleanup` | Delete only CanceledAt clause from NeverAttempted. | `row.Status.ShouldBe(Pending) with only CanceledAt populated; input count stays zero` |
| PC-150 | `F.ChannelReplySettledAt_prevents_untyped_cleanup` | Delete only ChannelReplySettledAt clause from NeverAttempted. | `row.Status.ShouldBe(Pending) with only ChannelReplySettledAt populated; input count stays zero` |
| PC-151 | `A.Unrelated_pending_input_vetoes_stop` | Ignore unrelated pending rows in stop preflight. | `conditionalStopCalls.Count.ShouldBe(0) for human/channel/scheduled/unknown-origin input` |
| PC-152 | `A.Scope_change_after_reservation_revokes_resume` | Skip shared D2 scope invocation in queued recovery launch. | `adapterCreates.Count.ShouldBe(0) after physical/owner AlwaysOn or role/provider changes` |
| PC-153 | `D.Pointer_change_before_commit_cannot_hold_new_session` | Delete current PersistentSessionId comparison at commit. | `replacementEpisodes.Count.ShouldBe(0)` |
| PC-154 | `D.Owner_change_before_commit_invalidates_episode` | Delete standing-owner comparison at commit. | `episodes.Count.ShouldBe(0)` |
| PC-155 | `T.Unsupported_tailer_cannot_certify_silence` | Return successful observation for Codex/Grok tailers. | `observation.IsSuccessful.ShouldBeFalse()` |
| PC-156 | `X.Resume_enqueue_requires_committed_generation` | Enqueue strict resume before reservation transaction commits. | `adapterCreates.Count.ShouldBe(0) when reservation commit fails` |
| PC-157 | `A.Fence_repair_never_launches_a_second_generation` | Route failed fence persistence back to automatic restart. | `acceptedGenerations.Distinct().Count().ShouldBe(2) after repaired write` |
| PC-158 | `B.Pruned_terminal_episode_does_not_reset_receipt_gate` | Infer budget unlocked when prior episode row has been pruned. | `secondStopRequests.Count.ShouldBe(0) after 91d without caller receipt` |
| PC-159 | `H.Truncated_caller_note_is_not_receipt` | Replace complete-body match with task-marker containment. | `episode.State.ShouldBe(AwaitingCheck) with correct marker and missing final line` |
| PC-160 | `H.Superseded_check_without_note_never_recovers_seat` | Treat CallerPublishedAt as receipt despite suppressed CallerMessageId. | `episode.State.ShouldBe(AwaitingCheck); callerMessageId.ShouldBeNull()` |
| PC-161 | `H.Legacy_produced_note_survives_pre_enqueue_worker_death` | Proposed by the [H-5 amendment](2026-09-20-card-0079-legacy-check-note-plan.md#testdesign-handoff-h-5--g-161--pc-161): exclude LegacyCheckNote from AgentTaskLandNotificationHostedService's unresolved scan after killing the producer at its committed pre-enqueue cut. TestDesign must accept the fixture/control before Code. | `matchingCompleteOriginalParentPrompts.Count.ShouldBe(1)` fails with zero original-note prompts after worker death; no new Check, direct reconciliation or rescue flush. |
| PC-162 | `B.Unresolved_recovery_retains_session_evidence` | Remove recovery referencer only in PruneSessionsAsync. | `protectedIds.ShouldBe(originalIds); unreferencedControlIds.ShouldBeEmpty()` |
| PC-163 | `B.Unresolved_recovery_retains_transcript_evidence` | Remove recovery referencer only in PruneTranscriptsAsync. | `protectedIds.ShouldBe(originalIds); unreferencedControlIds.ShouldBeEmpty()` |
| PC-164 | `B.Unresolved_recovery_retains_task_tree_evidence` | Remove recovery referencer only in PruneTasksAsync. | `protectedIds.ShouldBe(originalIds); unreferencedControlIds.ShouldBeEmpty()` |
| PC-165 | `B.Unresolved_recovery_retains_queue_evidence` | Remove recovery referencer only in PruneQueuedMessagesAsync. | `protectedIds.ShouldBe(originalIds); unreferencedControlIds.ShouldBeEmpty()` |
| PC-166 | `A.Automatic_exit_has_compaction_termination_source` | Stamp OperatorRequest instead of CompactionContinuationRecovery. | `session.TerminationSource.ShouldBe(CompactionContinuationRecovery); state.Suspended.ShouldBeFalse()` |
| PC-167 | `N.Automatic_audit_contains_only_bounded_metadata` | Assign full captured prompt body to audit evidence instead of bounded metadata. | `serializedAudit.ShouldNotContain(fullPromptSentinel); evidenceLength.ShouldBeLessThanOrEqualTo(configuredEvidenceLimit)` |
| PC-168 | `F.Terminal_non_check_brief_survives_compaction_cleanup` | Remove Role==Check from compaction-specific terminal cleanup. | `nonCheckRow.Status.ShouldBe(Pending)` |
| PC-169 | `F.Unrelated_input_survives_compaction_cleanup` | Cancel every Pending row instead of exact linked obsolete Check rows. | `unrelatedRow.ShouldBe(originalRowSnapshot)` |


The original multi-assertion PCs are now explicit: PC-10's alternate-isolation
mutant is PC-99; PC-12's commit-gap mutant is PC-102; PC-15's scope clauses are
PC-15/34–46/151–154; PC-18's output/transcript/binding races are PC-18/65–67;
PC-20's transport/exit/fallback checks are PC-20/23/68–70/91; PC-21's queued
resume boundary is PC-72; PC-28's normal hold controls are PC-130–143 and its
missing-history control is PC-89; PC-31's other disable boundaries are PC-73–76.
PC-33 also requires PC-156–157 so failed bookkeeping cannot cause another launch.

At the original TestDesign HEAD, PC-161 was the sole **non-executable design**.
The [H-5 amendment](2026-09-20-card-0079-legacy-check-note-plan.md) now specifies
its durable producer, recovery entry point, cut and proposed compiling defect.
TestDesign must review that construction and any additional independent controls;
the original inventory totals below are not a re-reviewed or executed result.

### Out of scope

- Provider-level root cause and certainty that silent Claude is not computing:
  no deterministic test can establish this. The authorized ten-minute policy
  explicitly accepts that residual risk only for the qualifying Check pattern.
- Live production Stop/Fresh, force-compaction, deployment and the ten-real-Check
  census: separate operator/rollout work. This stage performs none. Natural
  compaction acceptance remains pending until observed after implementation.
- General stall detection, non-Check/non-AlwaysOn recovery, alternative providers,
  model rerouting, changing normal caller/role timeouts and automatic Fresh:
  excluded by authorization and pinned by negative cases.
- Client rendering redesign and AttentionPanel tests: no layout change is needed
  for the existing attention-kind mapping. If Code changes rendering, read its
  fixture and add its scoped tests before claiming V-7.
- Full assembly/browser/real-provider suites: the affected classes and native
  queue path are bounded above. This avoids unrelated suites; it does not waive
  the native process, durable database, crash or recipient requirements.
- Deliberate mutations before implementation land: ordinary Code and Review run
  V/R; every PC belongs to a separately commissioned SourceLanding Mutation.
  Unchanged generic I/O guards are tested through the affected regression classes;
  a new bypass in the recovery path still requires its own PC.

### Cost

All durations below are **estimates**, not measured timings at this HEAD.
They include the projected PC-161 cycle so the missing seam is not assigned zero
cost. Test authoring, Plan amendment work, ordinary human/agent review, live
observation and diagnosis of failures are additional; this is the minimum clean
verification execution budget, not a completion-time promise.

Ordinary **Code V/R floor**:

| Component | Minutes |
|---|---:|
| Isolated outputs, restore/build application + runner + Pty fixture project, PostgreSQL/template setup | 10 |
| Unit (V-1; includes policy/settings and existing Unit guards) | 2 |
| V-2 occupancy/queue affected classes | 12 |
| V-3 wire/persistence/runner/tailer plus sequential FakeClaudeContractTests | 15 |
| V-4 coordinator/budget and standing start/supervision affected classes | 20 |
| V-5 producer/recipient and persistence-cut methods | 12 |
| V-6 controlled process-death/concurrency methods | 20 |
| V-7 projection/retention classes and attentionVisuals Vitest | 6 |
| **Ordinary V/R only** | **87** |
| **Code setup/build + V/R floor** | **97** |

Ordinary command inventory, after the Plan amendment and Code implementation.
Run each command in the foreground; use a fresh results directory per invocation.
These grouped filters are the pinned TUnit class-segment OR form.

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c79/ --nologo
dotnet build tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c79/ --nologo
dotnet build tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c79/ --nologo

dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c79/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory .antiphon/c79-unit
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c79/ -- --treenode-filter '/*/*/(CheckCompactionRecoveryFlowTests*)|(AgentTaskDeliveryWatchdogTests*)|(AgentTaskStandingAgentDispatchTests*)|(SessionMessageQueueWedgedHeadTests*)/*' --report-trx --report-trx-filename occupancy.trx --results-directory .antiphon/c79-occupancy
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c79/ -- --treenode-filter '/*/*/(CompactionContinuationWireTests*)|(AgentSessionRuntimeTests*)/*' --report-trx --report-trx-filename wire.trx --results-directory .antiphon/c79-wire
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c79/ -- --treenode-filter '/*/*/(CheckCompactionContinuationTests*)|(CheckCompactionAutomaticRestartTests*)|(CheckCompactionRecoveryPersistenceTests*)/*' --report-trx --report-trx-filename automatic.trx --results-directory .antiphon/c79-automatic
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c79/ -- --treenode-filter '/*/*/(AgentSupervisionTests*)|(SpecialistStartIntentTests*)|(StandingSessionSwitchConcurrencyTests*)|(StandingSessionQueueSwitchTests*)|(StandingSessionRecoveryHttpTests*)|(AgentControlServiceIntegrationTests*)/*' --report-trx --report-trx-filename standing.trx --results-directory .antiphon/c79-standing
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c79/ -- --treenode-filter '/*/*/(CheckNoteDeliveryHandoffTests*)|(ReceiptFailureDeliveryTests*)/*' --report-trx --report-trx-filename delivery.trx --results-directory .antiphon/c79-delivery
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c79/ -- --treenode-filter '/*/*/CheckCompactionCrashTests/*' --report-trx --report-trx-filename crash.trx --results-directory .antiphon/c79-crash
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c79/ -- --treenode-filter '/*/*/(CheckCompactionAttentionTests*)|(SpecialistHealthAttentionTests*)|(DataRetentionServiceTests*)/*' --report-trx --report-trx-filename attention.trx --results-directory .antiphon/c79-attention
dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c79/ -- --treenode-filter '/*/*/(CompactionContinuationStopTests*)|(TranscriptTailerObservationTests*)|(RunnerSessionGenerationTests*)|(TranscriptTailerCompactionTests*)|(RemoteControlConditionalInputTests*)/*' --report-trx --report-trx-filename runner.trx --results-directory .antiphon/c79-runner

# Run only after the application and runner commands finish.
dotnet run --project tests/Antiphon.Agents.Pty.Tests --no-build --property:OutputPath=bin-c79/ -- --treenode-filter '/*/*/FakeClaudeContractTests/*' --report-trx --report-trx-filename fakeclaude.trx --results-directory .antiphon/c79-fakeclaude
pwsh -File scripts/test-client.ps1 attentionVisuals.test
```

Mutation **PC floor**, 169 mapped controls / 169 planned cycles, including the
unresolved H-5 obligation. One cycle is a compiling production defect, exact
method red, restoration with refreshed timestamps, fresh build and exact method
green. A class filter, build failure, zero tests, timeout or unrelated assertion
is not a successful positive control.

| PC group | Controls | One red or green method run, minutes | All red + green method time |
|---|---:|---:|---:|
| P/C pure policy and settings | 18 | 0.04 | 1.44 |
| W wire mapping | 3 | 0.10 | 0.60 |
| S/T runner and real tailer | 11 | 0.30 | 6.60 |
| D/F/A/B/N isolated application integration | 116 | 0.25 | 58.00 |
| H full producer/recipient (includes PC-161) | 12 | 0.60 | 14.40 |
| X worker-death/race | 9 | 1.20 | 21.60 |
| **Method red + green total** | **169** | | **102.64** |

| Additional Mutation component | Minutes |
|---|---:|
| Exact-L preflight, isolated build/template setup | 8.00 |
| Discovery/count validation before mutations | 5.00 |
| 169 mutation apply/restore/restoration checks at 0.10 minutes | 16.90 |
| 338 incremental red/green builds at 0.55 minutes | 185.90 |
| Method red + green total from above | 102.64 |
| **Mutation floor** | **318.44** |
| **Total verification floor: Code setup/build + V/R + every PC cycle** | **415.44 minutes (6 h 55 m)** |

These estimates budget serial PC execution. Additional test argument variants
execute inside the exact named method, but a guard split or a second distinct
production mutant adds a cycle and must update the cost. Snapshot commissioning
permits no unbound extra worktrees. Zero PC-sharding/batching savings are credited:
many controls edit the same coordinator/queue file and the SourceLanding contract
requires one managed snapshot.

For an exact example, PC-18 runs only:
```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c79-pc/ -- --treenode-filter '/*/*/CompactionContinuationStopTests/Late_output_prevents_conditional_stop' --report-trx --report-trx-filename pc18-red.trx --results-directory .antiphon/c79-pc18-red
```
The restored-green invocation uses the same filter with fresh green results;
SourceLanding places logs/TRX/restoration records under its assigned external
evidence root instead of those illustrative worktree result paths. Use the
alias/file table to select the project and exact method for every other PC.
A fresh rebuild after restore is mandatory even if incremental timestamps would
otherwise reuse a DLL. Record the actual tested L, MVID, mutation diff, failed
assertion and restored-green counts. No source edit while a run is in flight.

Quantified savings: grouping 21 named application Integration classes into seven
runs saves 14 independent test-host/database bootstraps. At 0.15 minutes each,
that is **2.10 estimated minutes** already reflected in the ordinary floor.
Policy/settings live in the required Unit lane, avoiding 18 duplicate
method invocations: **0.72 estimated minutes**. Exact-method PCs avoid rerunning
unrelated class methods; no unmeasured class/suite wall-time saving is claimed.
The obsolete 25.5-minute assembly measurement is not a current timing baseline.

After any new failure, reproduce that exact method at the base commit before
calling it inherited. Do not widen waits or add retries. Once ordinary tests pass,
retain fresh TRX counts and run the duration tripwire on affected tests. Record
owned alternate-output directories before builds and remove only those verified
paths inside this worktree after all children exit; no outputs were created by
this documentation stage.

**Handoff audit:** bodies/nearest fixtures read; guards=169, mapped=169,
missing guard mappings=0, duplicate PC mappings=0. Executable PC designs=168;
non-executable seam=1 (G-161/PC-161). Runtime methods implemented or executed by
this TestDesign=0. Numeric execution floor=97 + 318.44 = 415.44 estimated minutes.
At that review the “all PCs executable” Code-entry gate was **not met**. Plan
task `0c6a0491` now supplies H-5's durable original-note handoff in the linked
amendment. Current next: **TestDesign** to review that seam and recalculate any
changed controls/cost before Code; the Code-entry gate awaits that review.
No human choice or authorization expansion is needed.
