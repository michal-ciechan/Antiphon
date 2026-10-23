# CARD-1517 / CARD-1518: conditional Claude interruption and permission-aware stalls

Plan owner: task `0b591e94`, 2026-10-22. Source inspected at
`c5da1d2387c08adf86e301a3874a5cbcfd4d6a98`; fetched master was
`19a0b6aa27c9cd925ddb045d5de1e6be364f42e2`. The inspected interrupt,
reply, deadline, slot-release and Claude detector files have no differences between
those revisions. This artifact changes documentation only. TestDesign is a separate
stage; the checkpoint selections below constrain its ordinary verification scope.

## Outcome and boundaries

A current Claude approval dialog must become an actionable permission-wait condition
within one bounded observation interval. A caller can cancel a qualified dialog or
explicitly fail the task without stopping its process. `-Refine -Interrupt` gains a
separately qualified Claude policy for one conditional Ctrl+C on a stuck non-modal
turn. Neither action proves refinement delivery: only the matching complete
`UserPrompt` does. A terminal task with a retained runner seat gets the existing
operator slot-release route, including when its server session row is absent.

Read the live Antiphon cards CARD-1517, CARD-1518 and CARD-0680, and the
[Debug report](../../investigations/2026-10-22-review-b985ca59-stall-debug.md).
The report establishes an unstarted Bash call blocked by a manual approval dialog,
not a command exceeding its execution timeout. It does **not** establish that a
missing server session row caused the original retention: its slot read said
`orphan=false`, with a live process and no open task. The brief adds the later
missing-row/kill-404 recovery case. Cover both shapes without conflating them.

CARD-0680 remains Backlog. It owns recognition coverage and **every direct-send
guard**, including the watchdog canary. These cards consume that shared result;
they do not close CARD-0680 or implement another regex detector. Before enabling
either new key path, require CARD-0680's direct-send protection and its activation
evidence on the applicable backend. Observation, diagnosis and recovery guidance
can ship without enabling input. A dependency unavailable at implementation time
is named as such, never replaced with an unguarded fallback.

## Ground truth

| Assumption in the cards/brief | What the inspected code actually does | Consequence |
|---|---|---|
| Claude cannot be interrupted. | [MidTurnInterruptPolicy](../../../server/Application/Services/MidTurnInterruptPolicy.cs) refuses non-Grok with `kind-not-grok`. The runner's conditional-input transport is provider-independent. | Add a Claude admission policy; do not infer a vendor key contract from the Grok policy. |
| Refine was delivered when the API accepted it. | [AgentTaskReplyService](../../../server/Application/Services/AgentTaskReplyService.cs) persists a Refined event and a marked WhenIdle row before attempting the key. `DeliveryVerdict` supplies the separate delivery result. | Keep the split outcome and full-prompt receipt. A refused key leaves an observable Pending row. |
| A stable screen is enough to write. | [SessionRunnerRuntime.SendConditionalInputAsync](../../../src/Antiphon.SessionRunner/SessionRunnerRuntime.cs) checks accepted generation and output sequence under the launch gate, again immediately before input. It does not judge task ownership, modal kind, drafts or progress. | Preserve runner fences and add provider admission before invoking them. Use the sequence of the snapshot actually classified. |
| CARD-0680 must create the first Claude dialog detector. | [ClaudeBlockingPromptDetector](../../../src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs) already recognizes `ToolPermission` and `Choice`, mostly for startup readiness. Its title alone can be just “Do you want to proceed?”. | Reuse it; CARD-0680 owns active-dialog/direct-send coverage and extracting the bounded diagnostic text. Do not use its `AffirmativeKey`. |
| Grok composer rules also describe Claude. | [ClaudeScreen](../../../src/Antiphon.Agents.Pty/ClaudeScreen.cs) has `TryReadComposer`, `ComposerContentIsEmpty`, `IsSettled` and `IsWorking`. Its contract requires two settled empty observations to exclude a ghost composer. | Use those Claude predicates, not `GrokComposerScreen`; unreadable is not empty. |
| `modalBlocked=false` rules out a permission prompt. | [SessionMessageQueueService](../../../server/Application/Services/SessionMessageQueueService.cs) projects persisted Remote Control episodes in `IsModalBlockedLockedAsync` and queue detail. | Consume CARD-0680's provider-modal projection; RC absence is not permission absence. |
| ToolCall means a local process started. | [TaskDeadlinePolicy.ClassifyPhase](../../../server/Application/Services/TaskDeadlinePolicy.cs) selects LocalExecution for every working ToolCall. Its cheap preview gate can defer evaluation for minutes. | Detect approval waits independently of that gate and annotate the same deadline evaluation with current evidence. |
| Generic stall detection will surface approval promptly. | [TaskProgressPolicy](../../../server/Application/Services/TaskProgressPolicy.cs) defaults to 30 minutes, with a minimum transcript-row population; it is detection only. | Do not wait for that verdict to discover an affirmative modal. Keep its existing execution-progress behavior. |
| A task failure releases a seat. | [AgentTaskDispatcher.TryFailOverdueAsync](../../../server/Application/Services/AgentTaskDispatcher.cs) fails/notifies without killing an ingested Working session. Parking and terminal automatic release are disabled by default. | Preserve task/process separation; failure wording must say that the seat remains. |
| Server session kill is the only release route. | [RunnerSlotService.ReleaseAsync](../../../server/Application/Services/RunnerSlotService.cs) resolves the named runner directly, records intent, calls ReleaseSlot and audits a server session only **if present**. [SessionRunnerEndpoints](../../../server/Api/Endpoints/SessionRunnerEndpoints.cs) requires operator authentication. | Missing server row needs guidance and a regression pin, not another kill endpoint or a fabricated session row. |
| `release-orphans` force-releases the same population. | Its current implementation delegates to the conditional terminal-seat coordinator; shipping automatic-release settings make it a no-op. Explicit single-seat release remains forceful. | Name the single-seat verb and its authority. An orphan label grants no automatic release permission. |
| All permission handling is absent. | [WatchdogService](../../../server/Application/Services/WatchdogService.cs) has a specific unsafe-rm No/refusal path. It is not general permission-wait classification and uses a different input path. | Preserve that separately owned rule; do not widen it to all Bash approvals. CARD-0680 must account for it when protecting direct sends. |
| Existing Claude canaries prove this key/backend. | [ClaudeInterruptCanaryTests](../../../tests/Antiphon.Agents.Pty.Tests/ClaudeInterruptCanaryTests.cs) measures Esc with marker/no-marker cases; [ClSession](../../../tests/Antiphon.Agents.Pty.Tests/ClSession.cs) skips Linux. | New qualification must exercise Ctrl+C and dialog cancellation on the actual backend; neither historical Esc nor a fake proves Ctrl+C on UnixPty. |

## Decisions

- **D-1 — Keep provider policy explicit.** Add `ClaudeMidTurnInterruptEnabled`
  and `ClaudePermissionCancelEnabled`, initially false, independently of
  `GrokMidTurnInterruptEnabled`, plus an initially empty list of qualified PTY
  backends for each operation. Read the resolved backend from the session's bound
  runner capabilities; absent/unsupported backend evidence refuses. Activation
  records the S0 Claude CLI version and backend, and a provider upgrade requires
  disabling/requalifying the operation before re-enabling it. This is an explicit
  rollout contract, **not** a new automatic Claude-version inventory: current
  runner capabilities expose Codex CLI version, not Claude CLI version. Reject
  simply adding Claude to the Grok kind check: composer and interruption semantics
  differ. Older peers lacking qualified observation/input support remain refused.
- **D-2 — Separate turn interruption from dialog cancellation.** Claude Ctrl+C is
  admitted only on a non-modal stuck Working turn. A permission modal returns
  `permission-dialog-use-cancel`; a qualified explicit CancelDialog action sends
  one Esc through the same conditional transport. Questions, trust/effort menus,
  generic Choice and unknown layouts get inspection/fail-task guidance. Reject
  Ctrl+C-to-Esc fallback, numeric answers, Enter and automatic approval. An operator
  can answer through the existing interactive session; this plan adds no approve API.
- **D-3 — Require evidence of a stuck turn.** Non-modal Ctrl+C needs a fresh
  transcript catch-up, the current task/attempt/session binding, Working=true,
  two settled readable empty Claude composers, no pending terminal input, no open
  question/modal, and the existing configured no-progress interval (30 minutes by
  default). Use TaskProgressPolicy's last-novel-progress evidence where available;
  for a quiet sparse transcript use the age of the current turn's last qualifying
  row, capped at dispatch, with no newer known workspace progress. Fresh output
  content/progress resets admission; animation alone does not. Silence is a
  cancellation eligibility fact for this explicit request, not proof of a deadlock.
  A normal Working turn, an idle turn, an unknown working verdict, disabled stall
  interval, or unproven current-turn identity refuses. Reject interrupt-on-Working
  alone and reducing the ordinary stall interval to the permission scan interval.
- **D-4 — Surface a modal within one 60-second scan interval.** Inspect eligible
  live Claude task sessions and reobserve open episodes on the existing dispatcher
  cadence, throttled per session to 60 seconds; share CARD-0680 observations if its
  cadence already meets this bound. Surface a positive current observation
  immediately, without waiting 30 or 90 minutes. Use a 90-second freshness limit,
  measured from a completed read by server TimeProvider, and always refresh before
  an action or deadline failure. A failed read is Unknown, never Clear. These are
  diagnostic bounds, not process-release deadlines. Reject a new unrestricted
  per-five-second fleet snapshot sweep and guessing from absence of ToolResult.
- **D-5 — Preserve deadline budgets.** PermissionWait is an evidence-backed phase
  annotation, with early attention; it does not reset, pause or lengthen the
  current 90-minute local-tool budget or role ceiling. At the boundary, a still
  current dialog produces a permission-wait failure reason and retained-seat
  guidance, not “running a local tool”. Unknown/stale evidence uses the ordinary
  deadline with an explicit evidence-unavailable qualifier. Explicit FailTask is
  available immediately for a verified permission episode. Reject indefinite
  deadline suppression and automatic failure/stop just because a dialog appears.
- **D-6 — At most one attempted key, durable across races and restart.** Reserve
  a task/attempt/request identity durably before input, bind it to the exact queue
  row (for refinement), generation, observed sequence and episode/turn. Concurrent
  replay returns one result. An intent whose result is unknown consumes its write
  allowance; neither retry nor a fresh request id may repeat a key on the same
  unchanged episode. A replay with different action/body refuses. Reject retries
  after timeout and inferring success from an RPC ack or screen redraw.
- **D-7 — Make limitations visible before steering.** Task detail/CLI status expose
  the live qualification/admission reason and available actions. Refine keeps its
  existing response fields and ordinary WhenIdle behavior. Its request-time
  `interruptWritten` history remains immutable; its current `refinementDelivered`
  projection reads the queue verdict rather than an old event's pending string.
  Reject silently promising that `-Interrupt` will unblock every provider modal.
- **D-8 — Use existing release authority.** Terminal live-session recovery uses
  the existing operator single-seat release after preserving source/evidence and
  checking current ownership. It does not revive the failed task or delete its
  worktree. No new automatic kill, orphan sweep enablement, force fallback or
  task-token elevation is added. The caller obtains operator recovery when its
  task token cannot perform the release.
- **D-9 — Reuse one modal contract.** CARD-0680 owns detector correctness and
  delivery vetoes. CARD-1518 owns task episodes, phase/attention projection and
  explicit recovery actions; CARD-1517 owns refinement admission/receipt. A shared
  observation may have a different final type name in CARD-0680; adapt that type
  once, not by maintaining another classifier. No third owner of modal matching.

These are the proposed implementation decisions, with no unresolved product-choice
question required to finish this plan. Provider qualification is an execution gate,
not permission to substitute a key or claim an unsupported backend works.

## Shared evidence and state transitions

Consume a typed provider-modal observation with session id, runner binding/store,
accepted generation, output sequence, observation completion time, kind, current
dialog fingerprint, diagnostic excerpt and cancellation affordance. The excerpt
must include the captured refusal sentence and available cancel instruction. Keep
it to 2,048 characters, extract the active dialog rather than the entire terminal
or command body, strip terminal controls, and apply existing diagnostic redaction.
Do not log/persist raw screens, credentials or arbitrary tool arguments. A generic
detector title alone is insufficient for the requested attention evidence.

The observation is **diagnostic**, never a UserPrompt, completion or proof that
every child process is idle. Bind a tool-specific PermissionWait only when the
current generation/attempt has one attributable unresolved tool call and the
active modal identifies its tool. Multiple/unmatched calls get “approval dialog;
execution attribution unknown”, while ordinary deadline accounting continues.
No process census is added to the hot path: the Debug report's absent child proves
the incident, not a universal precondition for modal recognition.

Persist a task permission-wait episode, keyed to task attempt, session generation
and modal/tool identity. Its first-observed time is immutable. Reobservations update
last-observed/sequence; repeated identical scans produce no duplicate warning.
Generation change, verified current clear screen, matching tool result or a new
turn closes that episode. Loss of observation marks it stale, preserving history
without granting current key authority. Reappearance after a verified clear starts
a new episode. An old screen never reopens a completed attempt.

Append an `ApprovalDialog` attention kind and corresponding client mapping. Include
task/card links, diagnostic text, first/last observation and freshness, pending
refinement count/verdict, live session/seat status, and supported recovery actions.
Place it before generic ProgressStalled/Overdue for the same task, with the deadline
still included in its evidence. Retain a terminal episode as recovery information
while its seat is known live; stale or missing inventory must be labelled unknown.
The same phase facts feed `DelegateCheckProbe`, task detail and deadline failure
wording. Do not require a caller check or an open browser for the scan to run.
Persist before publishing the normal attention/query invalidation event; reconnect
reads must reconstruct the row without relying on an event having been received.

### Interruption and refinement

Refine preserves its existing status rules: Queued edits the brief; Blocked needs
Reply; new terminal-task refinement refuses; Dispatched/Working stores WhenIdle.
Retain request identity across network retries. Use a durable intervention-intent
record with a unique `(taskId, attempt, requestId)` and one attempted key per bound
turn/episode; serialize with the queue lock and database uniqueness, not just an
in-memory replay check. Avoid recursively acquiring the queue lock during enqueue
or catch-up. The intent references the committed queue row before authorizing input.

After catch-up, a natural boundary may already have delivered the row: report
already-sent and write nothing. Otherwise apply D-1/D-3, then send only `\x03`
with the last classified snapshot's generation/sequence. Record written/refused/
unknown separately from delivery. On ambiguous result, preserve the row and intent;
do not resubmit the key. A provider interruption marker or cancelled TurnEnd uses
the existing interrupted-turn path, retains partial-work evidence and keeps the
task open. Flush only after the real boundary and CARD-0680's delivery admission;
do not fabricate a boundary when a canary shows that Claude only restored a draft.
No subsequent Enter clears or submits a restored composer. Full matching
UserPrompt confirmation, including the spill-pointer contract, changes the live
refinement-delivered result. Head-only text and composer echo remain insufficient.

### Explicit permission-wait actions

Add task-scoped `POST /api/agent-tasks/{id}/permission-wait/actions`, with
`episodeId`, `requestId`, expected attempt/generation and action `CancelDialog` or
`FailTask`. Authenticate the caller and apply existing task ownership/operator
authority; do not expose an unauthenticated runner input proxy. There is no arbitrary
input/key field. A new script `scripts/permission-wait.ps1 inspect|cancel|fail`
uses these typed requests and the existing task-token transport, with a reason for
failure. Show the action's refusal as well as the retained task/session state.

CancelDialog requires a live, current, specifically qualified ToolPermission
episode, the same current task binding, no user draft/pending terminal input and
current generation/sequence. Reserve intent, send exactly one conditional Esc and
observe the result. A vanished dialog before the write is a no-op/refusal, never
Esc into an idle composer. New output between observation and write is a refusal.
`written` does not mean `dialogCleared`; clear observation does not mean refinement
delivered. Unknown result stays visible and cannot retry. If supported by the
measured native transcript, the resulting interrupt boundary lets the normal queue
flush. Otherwise retain the queue and report intervention needed.

FailTask is an explicit caller action on a still-open verified permission episode.
It writes no key, uses the ordinary non-killing failure/notification path, and
retains pending refinement rows, files and process custody. Recheck task attempt
and generation before settlement. A concurrent completion wins over this stale
action. Persist action/failure obligation atomically where the existing failure
path allows, then recover notification through its durable mechanism. A complete
caller UserPrompt is the notification receipt; HTTP success is not delivery.
New actions on an already-terminal task refuse and return the release guidance.

### Session wait and release checklist

| Waiting state | What releases or resumes it? | After how long? |
|---|---|---|
| Current approval dialog, task still Working | An authorized interactive answer; qualified explicit CancelDialog; explicit cancellation/stop chosen by its owner; or process exit. Refine alone can remain Pending. | Attention on the next observation interval (60 seconds plus read/scheduling latency). **No automatic process-release deadline.** |
| Explicit FailTask or existing deadline failure, process still live | Operator single-seat force release after ownership/source inspection; or process exit. Failed is never silently made Working by a reply. | On that explicit action; the task deadline does not free the seat. |
| Blocked/AwaitingInput after an interrupted turn | Existing Reply/input rules; parking only if separately enabled and all publication/release gates pass. | With `BlockedTaskParking:Enabled=false`, **no automatic release deadline exists** (CARD-1083). |
| Server session row absent, runner still lists the seat | Explicit operator runner-slot release addressed by the runner inventory, independent of the missing row. | On that explicit action; `release-orphans` has no shipping automatic bound. |

Recovery instructions must resolve runner identity from current slot inventory, not
from a hard-coded fleet location:

```powershell
pwsh -NoProfile -File scripts/runner-slots.ps1 list -RunnerId <observed-runner-id>
pwsh -NoProfile -File scripts/runner-slots.ps1 release -RunnerId <observed-runner-id> -SessionId <session-guid> -Reason "Terminal task retained this inspected seat; source and evidence preserved"
```

The second command kills the process and drops its runner manifest only when release
is confirmed. It is **operator-only**, not an automatic consequence of this plan.
Verify a fresh slot list shows removal and the release intent/audit completed;
transport ambiguity is not success. Do not run either command against the incident
session as part of Plan/TestDesign. Preserve the original task, report, branch,
checkpoint receipt and partial work; a new Review is a separately dispatched task.

## Implementation slices

### S0 — Qualify the two Claude control operations

Add `tests/Antiphon.Agents.Pty.Tests/ClaudeConditionalControlCanaryTests.cs` and a
private portable launch helper beside it. Reuse the existing safe launch/isolation
patterns, but do not change ClSession's skip rule for unrelated tests. Exercise
UnixPty and modern Windows ConPTY in separate native lanes. Record CLI version,
backend, exact byte, tool/process evidence and native/normalized row order.

The two tests are `C1517_ToolCtrlCLeavesSessionAlive` and
`C1518_PermissionEscapeCancelsWithoutToolExecution`. The first uses a harmless
owned long-running tool and then a distinct full UserPrompt; the second creates a
harmless approval-gated write in an isolated scratch tree, verifies it has not run,
cancels it and verifies the file remains absent and the session remains usable.
No repeated Esc, Enter, approval or production-session probe. Record draft/idle/
unknown-screen refusals at the server-policy layer; do not deliberately Ctrl+C an
idle real session that might quit. A missing marker is a measured unsupported case,
not permission to synthesize one. A failed or skipped native qualification leaves
that operation/backend disabled and its checkpoint incomplete.

### S1 — Consume CARD-0680 observations and persist task episodes

New proposed files:
`server/Application/Services/PermissionWaitObservationService.cs`,
`server/Application/Services/PermissionWaitPolicy.cs`,
`server/Domain/Entities/TaskPermissionWaitEpisode.cs`, and
`tests/Antiphon.Tests/Application/PermissionWaitSupervisionTests.cs`.
Change `server/Infrastructure/Data/AppDbContext.cs`, CLI-generated migration files,
`server/Application/Settings/DelegationSettings.cs`, `server/Program.cs` and
`server/Application/Services/AgentTaskDispatcher.cs` for registration/bounded scans.
Adapt CARD-0680's shared observation contract here; do not edit its recognition or
every-send guard independently. Tests cover the captured prompt, no spawned tool,
generation and freshness, restart dedup, unknown reads, clear/reappear and a
current long-running tool without a dialog.

### S2 — Conditional Claude refinement, preserving the Grok contract

Change `MidTurnInterruptPolicy.cs`, `AgentTaskReplyService.cs`,
`AgentTaskDtos.cs`, `DelegationSettings.cs`, `scripts/delegate.ps1` and task-detail
projection in `AgentTaskService.cs`. Add a concrete
`ClaudeMidTurnInterruptPolicy.cs` and shared `SessionInterventionService.cs`,
`server/Domain/Entities/SessionInterventionIntent.cs`, its EF mapping/migration,
and `tests/Antiphon.Tests/Application/ClaudeConditionalRefinementTests.cs`.
Reuse `ClaudeScreen` and the existing conditional-input transport; no new raw
input protocol. Publish qualification/admission without claiming the request will
necessarily still be admissible when it writes.

Tests must call the real reply service and queue using the recording-runner pattern
in `AgentTaskMidTurnRefineTests`: persisted row before key, no key on normal Working,
idle/draft/question/modal/unknown/stale/unsupported state, concurrent duplicate
request, natural boundary race, intent-only restart, uncertain write, interrupted
turn preservation and complete-vs-head-only UserPrompt. Live delivered projection
must update on replay after confirmation. Keep Grok admission/settings unchanged.

### S3 — Permission phase, attention and explicit action service

Change `TaskDeadlinePolicy.cs`, `AgentTaskDispatcher.cs`, `DelegateCheckProbe.cs`,
`AttentionService.cs`, `server/Application/Dtos/AttentionDtos.cs`,
`AgentTaskDtos.cs`, `server/Api/Endpoints/AgentTaskEndpoints.cs` and registration.
Add `PermissionWaitActionService.cs`, `PermissionWaitActionTests.cs`, and
`scripts/permission-wait.ps1`. Reuse S2's intervention intent; do not create a second
write/replay implementation. Tests exercise the actual sweep, attention projection,
check digest, API authorization and action service, not just pure policy helpers.

Add the kind and typed action/detail support to `client/src/api/attention.ts`,
`client/src/features/attention/attentionVisuals.ts` and
`AttentionInspectionDrawer.tsx`, using existing query/mutation/event invalidation
patterns. Add `PermissionWaitAttention.test.tsx` beside the drawer. The UI shows the
dialog/reason and offers only currently supported actions; a stale view must still
be refused server-side. It does not render a Yes/approve button or represent a
cancelled dialog as a delivered refinement. Existing interactive session navigation
remains the route for a human answer.

### S4 — Terminal-seat recovery and owner documentation

Extend task detail/attention evidence and `AttentionService.Seats.cs` to name the
existing operator single-seat route for retained terminal seats, including missing
server session rows. Add
`tests/Antiphon.Tests/Application/TerminalPermissionWaitRecoveryTests.cs` using
`RunnerSlotEndpointTests`/`PhoneHomeTestHost` as the fixture. Exercise the real
server endpoint with a simulated phone-home peer, no AgentSession row, a terminal
task and a Pending refinement: correct runner request, authenticated release,
audit, no resurrection/queue delivery/worktree deletion, and observed occupancy
removal. Keep the existing operator-token, refusal and lost-ack contracts. This
route already works by code inspection; do not manufacture a failing release test.
The new terminal recovery **projection** is the changed behavior that must go red.

Update `docs/session-runtime-invariants.md`, `docs/orchestration-loop.md`,
`docs/ops-http.md` and `docs/antiphon-api.md` with qualification, outcomes, action
authority and the wait/release table. Update the relevant provider-contract entry
in `ProviderContractCatalog.cs` only with measured S0 facts. Scripts remain ASCII.
No parking enablement, runner restart, deployment, card-state change or incident
session intervention is part of these slices.

## Verification handoff

The brief does not fold TestDesign into Plan. TestDesign must append the required
`## Verification design` (inspection, delivery inventory, V/R, guard inventory,
one PC per independently bypassable guard, exclusions and numeric costs) and bind
the following reserved names to concrete test bodies/fixtures before Code.
Preserve these behavior boundaries and the minimal run budget. No whole Unit lane,
namespace, assembly, E2E suite or speculative repeated load run is required.

For each changed behavior, commit its compiling failing test first, run **one
method-scoped red**, then implement, commit/push and run **one narrow green**.
An infrastructure/build failure or zero executions is not a red witness. A baseline
green existing safety control is reported as such; later Mutation supplies its
guard-specific positive control, without a redundant Code mutation cycle. Do not
make native vendor canaries red by sending unsafe extra keys. One qualification
per operation/backend is sufficient; it supplies measurement rather than a claimed
software regression red. TestDesign may split a reserved method into parameterized
cases and must then correct `Min` from the actual roster, not from assertion counts.

Acceptance inputs that must survive that expansion:

- **V-1/R-1:** real tool Ctrl+C and real dialog Esc survival/boundary/next-prompt
  qualification; Linux evidence cannot certify Windows, nor the reverse.
- **V-2/R-2:** affirmative manual Bash dialog, unstarted child, pending refinement,
  early attention with refusal text; unknown/stale/wrong generation are never clear.
- **V-3/R-3:** Claude stuck-turn admission and real queue UserPrompt confirmation;
  normal Working, idle, draft, restored draft, modal/question and unsupported peers
  send no key. Natural boundary, concurrent replay, restart and uncertain writes
  retain at-most-one behavior. Grok and ordinary non-interruptive Refine stay intact.
- **V-4/R-4:** at 89:59/90:00, a fresh matching modal produces permission wording;
  a real running child without a modal and stale/unknown evidence retain ordinary
  deadlines. Role ceiling and interrupted-turn handling stay intact. No automatic
  key/kill/retry/parking, and scan/dedup failures cannot hide other tasks' deadlines.
- **V-5/R-5:** explicit cancel is fenced and idempotent; stale binding, progressed
  output, pending user input and unsupported dialog refuse. Explicit failure
  terminalizes only its current attempt, never kills it, and produces a complete
  caller notification through the real queue, including busy caller/recovery.
- **V-6/R-6:** retained terminal seat guidance, missing-server-row release,
  operator authorization, ambiguous release and fresh occupancy proof; no task
  resurrection and no queue/source destruction. UI renders stale/unsupported and
  pending/delivered accurately and handles a refused action after a stale view.

### Checkpoints

Read `GET /api/runner-defaults` and `GET /api/session-runners` again at dispatch.
Plan read both successfully at approximately 23:14 UTC, revision 2: an eligible
Linux default and an eligible Windows runner were available. Placement is live
configuration, not a fleet address in this plan. Ordinary integration rows use
the **portable .NET/Postgres lane**, preferably the effective default; omit
`-Runner` and `-Platform`. CP-1 is the **Linux UnixPty native lane** and CP-2 the
**Windows modern ConPTY native lane**; only those require `-Platform Linux` or
`-Platform Windows`. Explicit `Any` removes a prior OS pin. Neither pins a host.

The two native rows select exactly the two S0 methods. New server test classes
below reserve one non-parameterized method per named boundary, with multiple
assertions not miscounted as executions. TestDesign must inspect and finalize
their body/roster; these are planned tests, not claims that they exist today.
CP-3 through CP-10 share one committed S1-S4 build on the green pass. Red witnesses
use the same exact method selection against the relevant committed test-first
slice, with a fresh isolated build and recorded SHA; never reuse output across
different source commits. CP-10 is an unchanged regression pin, green only.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S0 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1517-linux/` | linux-native-control | `/*/*/ClaudeConditionalControlCanaryTests/*` | V-1, R-1 | both named native methods, 2 executed, 0 failed/skipped | 2 | 10 | true | `ANTIPHON_HEADED_TESTS=1` |
| CP-2 | S0 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1517-windows/` | windows-native-control | `/*/*/ClaudeConditionalControlCanaryTests/*` | V-1, R-1 | both named native methods, 2 executed, 0 failed/skipped | 2 | 10 | true | `ANTIPHON_HEADED_TESTS=1` |
| CP-3 | S1-S4 | `tests/Antiphon.Tests -> bin-c1517-server/` | permission-observation | `/*/*/PermissionWaitSupervisionTests/C1518_EpisodeEvidenceAndAttention` | V-2, R-2 | named method, 1 executed, 0 failed/skipped | 1 | 7 | true | n/a |
| CP-4 | S1-S4 | CP-3 | claude-refinement-receipt | `/*/*/ClaudeConditionalRefinementTests/C1517_StuckTurnInterruptAndDelivery` | V-3, R-3 | named method, 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-5 | S1-S4 | CP-3 | claude-refusal-replay | `/*/*/ClaudeConditionalRefinementTests/C1517_RefusalsAndReplay` | V-3, R-3 | named method, 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-6 | S1-S4 | CP-3 | permission-deadline | `/*/*/PermissionWaitSupervisionTests/C1518_DeadlineAndProgressBoundaries` | V-4, R-4 | named method, 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-7 | S1-S4 | CP-3 | permission-cancel | `/*/*/PermissionWaitActionTests/C1518_CancelIsFencedAndAtMostOnce` | V-5, R-5 | named method, 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-8 | S1-S4 | CP-3 | permission-fail-receipt | `/*/*/PermissionWaitActionTests/C1518_FailFastPreservesSessionAndDeliversCallerReceipt` | V-5, R-5 | named method, 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-9 | S1-S4 | CP-3 | terminal-seat-recovery | `/*/*/TerminalPermissionWaitRecoveryTests/C1518_MissingServerRowUsesRunnerSlotRelease` | V-6, R-6 | named method, 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-10 | S1-S4 | CP-3 | grok-interrupt-regression | `/*/*/AgentTaskMidTurnRefineTests/C0491_InterruptWritesOneConditionalCtrlCAfterTheMarkedRow` | V-3, R-3 | existing Working/Dispatched cases, 2 executed, 0 failed/skipped | 2 | 1 | true | n/a |
| CP-11 | S1-S4 | n/a | permission-attention-client | `pwsh -NoProfile -File scripts/test-client.ps1 PermissionWaitAttention.test.tsx` | V-6, R-6 | only named file, all tests pass; typecheck/lint pass | n/a | 3 | true | n/a |

Run each committed slice group through `tools/Antiphon.Checkpoints -- run --plan`
with this path and `--expected-source-sha <committed-sha>`: select `--rows CP-1`
on Linux native, `--rows CP-2` on Windows native, and
`--rows CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11` on the portable lane.
CP-1/CP-2 intentionally build separately because outputs cannot be shared across
native platforms; a duplicate-build advisory is not a reason to reuse them.
Then `wait`
through exit 75 until settled. The checkpoint driver owns its build slots; any
bootstrap/build/test outside it goes through `scripts/build-slot.ps1`. Native rows
are serial and never co-scheduled with Antiphon.Tests/FakeClaude. Exit 4 is not run,
not a reason to bypass the slot. Retain unedited CHECKPOINT lines, nonzero fresh
TRX counts and source provenance in the report, generated payloads ignored. Delete
owned alternate-output directories using the documented custody rules. A required
native row unavailable/skipped remains incomplete; do not report Full/green.

### Cost and next stage

Estimated ordinary green floor is **37 minutes** (20 native + 14 server + 3
client), excluding slot wait, dependency work and authoring. Up to seven new
server method-scoped baseline red witnesses plus one selected client red add
approximately **10-38 minutes**, depending
on whether their compiling test commits share an isolated build; do not rerun the
eight green server selections for each red. Allow approximately **3-5 hours**
implementation/fixture work across these slices. These are estimates, not measured
test outcomes. No production build/test, live canary or key was run by this Plan.

TestDesign must supply the numeric post-land PC floor and finalize the native
fixture and async-delivery guard inventory. It must not broaden this ordinary
profile to a suite. Its next stage can be Code only after the verification section
is complete and the CARD-0680 dependency/qualification gates are explicit.
Land this plan through its own task owner; its docs publication does not activate
any feature or release the incident seat.

CARD-1518 diagnosis is implemented under the narrower
[permission-wait classification plan](2026-10-23-card-1518-permission-wait-classification-plan.md).
CARD-1517 intervention and native qualification remain separate.
