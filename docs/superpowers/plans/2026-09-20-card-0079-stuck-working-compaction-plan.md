# CARD-0079: recover the fourth stuck-Working interpreter outage

Plan task: `15587e3f`, 2026-09-20. Source: investigation task `e78ba0b0`,
commit `954f98db85ac99425bbbcde2675694fe8d6ac303`,
[fourth recurrence investigation](../../investigations/2026-09-20-card-0079-4th-interpreter-down-recurrence.md).
Code references below are against that commit. Its parent code is `3c7a4057`.
The requested investigation branch is occupied by another worktree; this plan's
branch, `feat/card-task-15587e3f`, starts at the exact requested investigation SHA.

This is a plan, not an implementation or an operational recovery receipt. No live
session was stopped or restarted and no check was sent by this Plan dispatch.
TestDesign remains a separate stage; the proposed tests and positive controls
below are its input, not executed evidence.

## Outcome and completion boundary

Recover the current interpreter with an explicit Stop followed by **Fresh**, after
reviewing and retiring obsolete Check work. Prevent the next identical outage
from hiding behind `Working` for four hours: recognize a silent auto-compaction
continuation, record a durable recovery-needed episode, release provably untyped
Check occupants, and refuse new work into that generation. Preserve the real
working-state rule and all normal busy-session protections.

There is one policy decision before implementation. The default below preserves
the repository's rule that a stall is detection and decision, never an automatic
kill. It restores this outage through the operator runbook and gives subsequent
outages a bounded, visible recovery path. It **does not promise unattended
restoration**. If unattended kill/restart is required, approve that exception and
revise the recovery design before TestDesign. Merely changing D8 does not authorize
destroying the older turn that caused the queue to wait.

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
| Stop and Start automatically choose a clean conversation. | `AgentControlService.StopAsync:976` suspends supervision. Ordinary Start resumes Claude history; `{ "fresh": true }` explicitly selects a new ID. Queue switching refuses attempted Pending input and moves never-attempted input. | Use Fresh explicitly for this occurrence, retain old history, and inspect/cancel obsolete briefs before switching. |
| Failure or a launch acknowledgment proves recovery. | Existing checks settle on correlated reports. Delivery and caller receipt require whole matching UserPrompt evidence. Health or `Sent` alone does not prove either. | Close recovery only on a new, useful Check reading and its caller receipt. |

## Decisions

These are stated defaults for the caller to accept or amend, not silently granted
authority for automated session destruction.

- **D-1 — Operator-controlled process recovery.** Detection, bounded task failure,
  admission refusal, and attention are automatic. Kill/Stop and Fresh remain
  explicit operator actions. The brief asks for the current session's operator
  unblock; it does not expressly supersede the standing no-auto-kill rule for all
  future sessions. Source: [AGENTS.md](../../../AGENTS.md), Sessions and pty, and
  [ops-http.md, Killing](../../ops-http.md#killing): "A stalled session is a
  detection and decision state, never an automatic kill." Rejected: quietly
  borrowing the boot-stall license to kill; this is a reused conversation with
  earlier work, not an empty first boot turn. Caller decision: accept this
  operator-gated design, or commission an explicit, narrowly scoped automatic
  recovery exception. The latter needs a revised durable attempt budget,
  generation-fenced stop/start and crash-recovery design before code.
- **D-2 — Narrow first release.** Detect ClaudeCode standing Check seats, using
  `StandingSpecialistSeatPolicy` including configured-slug compatibility, plus a
  positively identified `(auto)` boundary in an unfinished ordinary turn. Include
  declared physical Check alternates. Do not act on generic workers, Codex, Grok,
  manual compaction, trigger-less legacy boundaries, or idle sessions. Rejected:
  a fleet-wide "Working too long" timer, which would include legitimate tools and
  unrelated provider states.
- **D-3 — Ten minutes of silent continuation.** Add typed
  `Delegation:CheckCompactionContinuationWaitMinutes`, default 10; zero disables
  discovery of new episodes, negative values fail validation. Measure from the
  later of the current accepted launch, the auto boundary, and its synthetic
  continuation prompt, using conservative event/arrival times as described below.
  This is a chosen operational bound, not a measured provider percentile. Keep
  the 60-second caller budget, 10-minute delivery budget, and 240-minute generic
  role fallback unchanged. Rejected: shortening Check's whole role ceiling or
  increasing the caller wait, neither of which fixes the session.
- **D-4 — A separate episode, never a fake idle verdict.** Persist the detected
  episode on the session, bound to its accepted `StartedAt` and boundary sequence.
  A recovery-needed session can still truthfully report `working=true`.
  Rejected: toggling session status to Stopped while its process lives, setting a
  model hold, reusing the boot-liveness latch, or disguising a compaction stall as
  missing native history in the continuity hold.
- **D-5 — Fresh for the current operator recovery.** The previous resume recovered
  for only about 25 minutes before another auto-compaction. Fresh preserves the old
  conversation row while avoiding resumption of the same compacted context.
  Rejected: `{}` Start as an equivalent repair, `/compact`, raw Enter/Esc, or a
  second copy of the old Check brief. This does not make Fresh the automatic
  policy for other standing conversations.
- **D-6 — Cancel obsolete input, retain possible delivery evidence.** Only linked,
  terminal Check briefs proven never attempted may be canceled automatically.
  Do not move historical Checks to the new session for replay. Rejected: deleting
  the whole queue, treating every Pending row as untyped, or sweeping human input.
- **D-7 — Preserve routing and notification contracts.** A stalled physical seat
  is ineligible. An already declared, qualified alternate may serve through the
  existing policy; do not invent one or alter pins/holds. With no usable seat,
  ship the existing degraded digest with the specific recovery-needed reason.
  Caller-facing failures use the existing durable delivery obligation.
- **D-8 — Separate TestDesign.** This brief requests a plan with PCs/guards but does
  not fold TestDesign into Plan. After D-1/D-3 acceptance, commission TestDesign
  to finalize the named tests, ordinary verification profile and mutation matrix.

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

Eligibility requires an owned, current, Running ClaudeCode Check session; an
ordinary, non-housekeeping UserPrompt in its unfinished turn; and an explicit
auto CompactBoundary later in that turn. The synthetic continuation is permitted
and contributes to the clock but is not a human prompt or evidence of progress.
No subsequent AssistantText, Thinking, ToolCall, ToolResult, ordinary UserPrompt,
TurnEnd, interrupt end, manual compact end or restart boundary may have advanced
the episode. Any such progress excludes this **silent continuation** diagnosis;
other stall/deadline policies continue to own later stalls. Do not infer the
episode from the last row's kind alone.

Respect both sequence and timestamp evidence: catch-up can append historical rows
above current rows. Anchor in the current launch/last effective end, reject an
auto boundary proven to predate it, and do not treat a backfilled stale row as a
new unfinished turn. For missing or conflicting ordering evidence, return
Unknown rather than confirming a stall. Null event timestamps may use CreatedAt
only where the current-generation sequence/end fence establishes ownership.
Use the later event/arrival time to delay suspicion on newly imported records;
future times do not breach. Exact 10:00 is overdue, 9:59.999 is not. Multiple
auto boundaries select the newest still-silent episode and restart its grace.

On a stored overdue suspect, pull the runner transcript, persist it without queue
side effects, and re-evaluate. Add an observation API/result that distinguishes
successful/no-change from unavailable/unsupported/persist-failed; retain the
existing boolean method's meaning for existing callers. Read the runner's
accepted generation and DB ownership before and after observation, then recheck
them under the existing session delivery/standing-start lock discipline at commit.
Missing generation evidence, an observation error, or a changed pointer/token
means no new recovery-needed transition. A failed pull is not proof of silence.
Do not hold a database transaction across runner I/O or call full Sync while
holding the delivery lock (it can re-enter queue flushing).

### Durable episode and lifecycle

Add nullable session fields for the latest episode:
`CompactionStallStartedAt` (accepted generation token),
`CompactionStallBoundarySequence`, `CompactionStallDetectedAt`,
`CompactionStallResolvedAt`, and bounded resolution/evidence as needed. These
are new fields, not changes to CompactionRecoveryWatermark. An active episode
requires matching current StartedAt, a detected time, and no resolution.
Write the episode and one `CompactionContinuationStalled` incident atomically.
Append enum values, never renumber them. Use the persisted generation/boundary
key for deduplication, not incident existence or an in-memory cooldown.

Run the service as an isolated dispatcher sweep before the delivery watchdog and
new dispatch. Discover eligible sessions even with zero pending tasks: traffic
must not be needed to reveal a broken standing interpreter. A bounded one-minute
cadence using injected TimeProvider is sufficient; isolate failures per session.
The dispatcher already runs only while Delegation is enabled. Disabling discovery
does not erase a recorded unresolved episode or authorize fresh work into it.

Transitions are:

- Healthy -> Suspected (read-only) -> Confirmed recovery-needed after successful
  observation and locked generation/evidence recheck.
- Confirmed -> Resolved on observed subsequent progress/end of the affected turn,
  or explicit stop/new accepted launch. Preserve episode evidence on the old row.
  An unacknowledged old generation cannot block a genuinely new launch; conversely
  a stale observation of old progress cannot resolve the replacement's episode.
- A same-conversation resume establishes a new generation/end fence. It can be
  considered again only for a new eligible auto boundary, not replay of this one.
  Runtime recovery is not yet proof of a successful Check; keep health recovery
  tied to an actual valid reading.

Keep reevaluating active episodes for late progress, not only undiscovered
sessions. Restart, incident pruning, or task settlement must not lose the
recovery-needed projection. Existing explicit Start/Fresh/Stop remains the process
control surface; this service never sends input or invokes Kill/Stop/Start.

### Occupancy, admission and queue handling

For a confirmed episode, the dispatcher gets a dedicated non-destructive failure
path before D8. Fail a Dispatched Check occupant only after both its delivery
watchdog age and the episode's silent-continuation deadline have elapsed. Require
its same physical seat/session/generation and its own linked brief, Pending and
**never attempted**, rechecked under the delivery lock. Prefer ExecutionTaskId;
legacy marker lookup must still be exact and scoped to this task/session/dispatch.
Do not call `AgentTaskService.CancelAsync` here: it calls StopDelegateAsync.

Use a dedicated failure code/reason such as `CompactionContinuationStalled` with
boundary, generation, age and the statement that the session was not killed.
Reuse the existing fail/event/caller-notification transaction and its recovery
outbox. This path cannot flow into the generic never-started kill or automatic
retry/escalation tails. At defaults an already-old episode's new occupant is
released at the first watchdog tick after ten minutes, not at 240 minutes.
Already Working, Sent, attempted-Pending, mismatched or uncertain tasks remain
with their existing settlement/deadline protections and are named in attention
for operator review. Old investigation occupants whose brief was untyped qualify.

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
It types nothing and preserves unrelated/attempted rows. Do not automatically
cancel the delivered pre-compaction prompt or replay it into the replacement.

### Visibility

Project one durable `CompactionContinuationStalled` attention item for an active
episode, including physical seat, session, accepted start, boundary time/sequence,
elapsed silence, affected task and pending-brief count. Link to existing agent
and session views and the operator procedure; no new card status or alert sink.
Add the kind's normal client visual/type mapping. Health must not advertise that
seat as warm-ready just because Status is Running. A declared alternate can keep
the logical service available. For a legacy primary, the session attention item
must still exist without routing/health rows. Clear episode attention on actual
resolution; do not mark logical service recovered merely because attention clears.

## Implementation slices

Each slice is a meaningful commit with its real validation outcome. Build only
after the policy decision and separate TestDesign have completed.

| Slice | Production/document files | Tests and acceptance |
|---|---|---|
| S0: operator repair | Execute the preceding runbook; record evidence in `docs/investigations/2026-09-20-card-0079-operator-recovery.md` (new). No production edit. | New generation, no replayed dead briefs, first useful Check plus caller receipt, subsequent census. State explicitly if not yet performed. |
| S1: episode policy and durable storage | New `server/Application/Services/CompactionContinuationPolicy.cs`; `server/Application/Settings/DelegationSettings.cs` and its existing validation/composition; `server/Domain/Entities/AgentSession.cs`; append incident/failure enums; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated `server/Migrations/*CheckCompactionStall*` and model snapshot. | New `CompactionContinuationPolicyTests.cs` and `CheckCompactionContinuationSettingsTests.cs`; migration round trip, strict threshold, ordinary/manual/unknown/generation/backfill guards. All new test paths in this table are under `tests/Antiphon.Tests/Application/`. |
| S2: observed detection and resolution | New `CheckCompactionContinuationService.cs`; `AgentSessionRuntime.cs` additive observation result; `AgentTaskDispatcher.cs` sweep integration; `server/Program.cs` DI as needed. | New `CheckCompactionContinuationTests.cs`; successful no-change pull vs failed pull; late reply; restart without fresh traffic; generation race; independent-session failure; dedup after incident pruning; resolved/new episode. Existing `CompactionRecoveryTests`, runtime catch-up/settlement tests. |
| S3: bounded occupancy and clean admission | `AgentTaskDispatcher.cs`; `SessionMessageQueueService.cs`; `SpecialistTaskRunner.cs`; `SpecialistRequestService.cs`; shared seat/session episode predicate near `StandingSpecialistSeatPolicy.cs`; `AgentTaskCheckService.cs` failure wording if needed. | New `CheckCompactionRecoveryFlowTests.cs`; extend `AgentTaskDeliveryWatchdogTests`, `AgentTaskStandingAgentDispatchTests`, `SessionMessageQueueWedgedHeadTests`, `SpecialistFailurePolicyTests`, `SpecialistStartIntentTests`. Prove both Check entry paths and a queued-before-detection dispatch race. |
| S4: attention, health and recovery documentation | `AttentionService.cs`, attention DTO/enum owner; `StandingSpecialistHealthService.cs`; `client/src/api/attention.ts`; `client/src/features/attention/attentionVisuals.ts`; `docs/session-runtime-invariants.md` and `docs/ops-http.md`. | New `CheckCompactionAttentionTests.cs`; extend `SpecialistHealthAttentionTests`, `attentionVisuals.test.ts`, `AttentionPanel.test.tsx` only if rendering changes. Include legacy primary with no routing row and a healthy alternate. |
| S5: end-to-end ordinary acceptance | Extend `CheckNoteDeliveryHandoffTests.cs`; compose existing standing recovery HTTP/queue-switch fixtures instead of mock-only success. Add evidence to the operator-recovery document after deployment. | Real DB/dispatcher/queue flow from stuck episode through explicit Stop/Fresh to a newly created Check and whole caller receipt; busy and already eligible callers; durable notification failure recovery. Existing `StandingSessionRecoveryHttpTests`, `StandingSessionQueueSwitchTests`, `ReceiptFailureDeliveryTests`. |

Do not alter auto-compaction scheduling, native tailer interpretation, model holds,
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
| PC-7 | `Silent_compaction_releases_only_the_untyped_check_occupant_without_killing` (`CheckCompactionRecoveryFlowTests`) | Restore unconditional D8 continue; task remains Dispatched instead of Failed. Separate control routes failure through the generic kill tail; recorded kill list must fail. |
| PC-8 | `Attempted_or_sent_brief_retains_its_existing_owner_and_delivery_evidence` (`CheckCompactionRecoveryFlowTests`) | Treat Pending as sufficient proof of never-attempted; a current-composer row is incorrectly canceled/failed. |
| PC-9 | `Legacy_check_refuses_the_stalled_primary_before_task_creation` (`CheckCompactionRecoveryFlowTests`) | Remove legacy admission gate; unexpected task/queue creation fails. |
| PC-10 | `Routed_check_excludes_only_the_stalled_physical_seat` (`CheckCompactionRecoveryFlowTests`) | Remove physical-seat gate, or apply primary episode to all alternates; selected candidate/launch evidence fails. Test each mutation separately. |
| PC-11 | `Confirmed_episode_survives_restart_and_incident_pruning` (`CheckCompactionContinuationTests`) | Use incident existence/in-memory dedup instead of persisted episode; duplicate incident or lost refusal/attention fails. |
| PC-12 | `Terminal_untyped_briefs_are_pruned_while_working_after_a_commit_gap` (`CheckCompactionRecoveryFlowTests`) | Put cleanup back behind idle or omit reconciliation after failure commit; obsolete Pending brief remains. Human/attempted rows must remain unchanged. |
| PC-13 | `Explicit_fresh_recovery_delivers_a_new_check_and_its_whole_caller_note` (`CheckNoteDeliveryHandoffTests`) | Break the post-recovery new Check dispatch/receipt path or use Sent as caller receipt; whole UserPrompt and correlated reading assertions fail. |
| PC-14 | `Healthy_working_session_still_defers_delivery_and_is_never_killed` (`CheckCompactionRecoveryFlowTests`) | Remove ordinary D8/D9 protection; healthy busy control fails. Also retain existing `a_working_session_with_a_pending_brief_is_neither_failed_nor_killed` in `AgentTaskDeliveryWatchdogTests`. |

Additional ordinary guards: 9:59.999 vs 10:00; setting zero/negative; repeated
auto boundaries; timestamp inversions and nulls; already terminal task; no queue
row; mismatched execution task; concurrent sweep; unavailable observation does
not block healthy other sessions; no pending task at detection; failure/outbox
commit vs note enqueue crash; record remains visible after task settlement;
real late progress resolves an episode but a health success still needs a valid
Check. Assert zero input/kill/start calls on every automatic detector path.

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
Apply the additive schema before activating the detector. Re-read effective
settings, the current seat generation and any active episode. Use the operator
acceptance window above; report role outcomes rather than counting the truncated
`INTERPRETER DOWN` text in parent Check events.

Rollback: stop new episode discovery with the dedicated setting, retain episode
and transcript evidence, and resolve the affected seat explicitly. Do not clear
model holds, globally bypass admission, downgrade data, or label the unchanged
stuck session healthy to make a dashboard green. A code rollback alone does not
recover that session. Record pending acceptance/Mutation work honestly.

## Next-stage decision

Caller: accept D-1's operator-controlled recovery and D-3's provisional 10-minute
bound, or request a revised plan with explicit unattended recovery authority.
The immediate operator unblock can be commissioned independently. Once accepted,
send this artifact to **TestDesign**, then Code; verification was not folded into
this dispatch. The provider-level cause remains unknown and is not required to
detect this episode, but failure of the fresh-session canary is new evidence for
Investigate rather than permission for another restart loop.
