# CARD-0680: shared Claude dialog observation and guarded delivery

Plan owner: `e627fd75`, 2026-10-23. Inspected checkout:
`98e96f029a8117034445d6c236db0cd0c045a0e6`. Compared the relevant detector,
composer, runner, queue, runtime and watchdog files with fetched master
`1c955459a3ef39981b459cbcc12d8f656551173d`: no differences in those files.
This is a documentation deliverable. TestDesign remains a separate stage; the
closed checkpoint selections below are its ordinary verification budget.

## Outcome and scope

Give every automated Claude send one shared, current answer to whether a dialog
blocks input. A permission dialog, AskUserQuestion, or unclassified choice must
withhold the body, Enter and recovery keys. Queue-backed work stays pending;
direct work reports a typed refusal. Recognition reads the rendered screen and
writes no terminal input. Protect both the server's admission and the final
runner write, including a dialog that appears between the body and Enter.

This is the prerequisite for
[CARD-1517/1518](2026-10-22-card-1517-1518-claude-permission-stalls-plan.md), not
their permission-episode, attention, action, deadline or refinement feature.
Also specify a narrower, positively evidenced alternative to the parent's
unreadable Working-composer requirement. Its native qualification and production
interrupt policy remain with the original CARD-1517/1518 Code owner.

Read the full live Antiphon CARD-0680 with `scripts/card.ps1 get CARD-0680
-Board Antiphon -Json`: revision 1, InProgress, platform Any. Its required
activation witness is a real expectation-watchdog nudge refused while a dialog
is up. A detector-only test does not satisfy that witness.

### Evidence carried forward

The S0 branch is `origin/feat/card-task-38c4e51a`, final tested tip
`7c59b5619e22b77bf43727e9074195be84a2f024`. Read its
`docs/investigations/2026-10-22-card-1517-1518-s0-code-evidence.md` and the full
stored Result from `GET /api/agent-tasks/38c4e51a-34b1-4470-921c-44d58a8e9be0`.
The requested `git show ...:.antiphon/task-38c4e51a.md` fails because that report
is absent from the Git tree; the stored Result supplies its final execution.
The committed investigation explicitly predates that last run.

Preserve the actual outcomes: earlier Claude 2.1.280 / UnixPty qualification
failed both preconditions; final Claude **2.1.309 / UnixPty** CP-1 executed **2,
passed 1, failed 1, skipped 0**. Permission Esc passed, including no gated tool
execution, a native interruption marker, process survival and complete next
UserPrompt/response. Ctrl+C failed **before any control byte** because Working
had no composer readable by `ClaudeScreen.TryReadComposer`. The receipt was
rejected as `report_failed`. Windows qualification remains owed. Neither the
failure nor the successful Esc canary establishes Ctrl+C behavior or permits
production activation.

## Ground truth

| Card or parent-plan assumption | Inspected implementation | Planning consequence |
|---|---|---|
| There is no Claude permission detector. | `src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs` already recognizes ToolPermission, Choice, trust and effort. Most consumers are startup readiness/adapter paths. | Extend and reuse this detector; the missing contract is active-session observation and delivery protection. The refined premise is valid, so no Investigate reset is needed. |
| AskUserQuestion is specifically recognized. | `Detect` gates on `1yes`, `2no`, `entertoconfirm` or `esctocancel`; its catch-all is Choice. There is no explicit question kind or coverage of all question layouts. | Pin native single-choice, multi-choice and free-text question layouts. Unknown choices still veto sends; do not infer an affirmative answer key. |
| A detector miss means a safe terminal. | Empty/partial screens return null, and the current matcher searches the whole rendered frame for text fragments. | Distinguish Unknown from positively observed Clear, and active bottom dialogs from quoted/history text. Never scan the retained raw buffer. |
| Queue ModalBlocked includes provider prompts. | `SessionMessageQueueService.IsModalBlockedLockedAsync` and queue projection query only current-generation `RemoteControlModalEpisodes`. | Compose existing RC blocking with shared Claude observation; retain the reason rather than always saying Remote Control. |
| The expectation watchdog's safety checks cover Claude dialogs. | `SessionMessageQueueService.Expectations.cs` checks that RC projection and earlier composer holds; `DeliverAsync(... overlayRecovery: false)` checks catalog overlays. It has no shared Claude permission/question admission. | Guard both pre-attempt admission and every actual byte write; preserve existing attempt/receipt rules. |
| Every automated byte passes through DeliverAsync. | `AgentChannelService`, `WatchdogService`, `SessionHealthActions`, adapters' boot submits and local-command/retry paths also write. | Migrate the complete writer inventory below, including the separate Enter and retries. |
| Conditional input excludes drafts and modals. | `SessionRunnerRuntime.SendConditionalInputAsync` fences accepted generation and LastSequence under the launch gate, rechecking before `WriteInputUnderGateAsync`. It does not classify dialogs or compare input revisions. | Extend this transport with capability-gated dialog/input evidence; preserve its fences and refusal semantics. |
| An unreadable Working composer can be treated as empty. | `ClaudeScreen.TryReadComposer` deliberately rejects unknown/hidden frames. CARD-0650 holds require two readable empty observations. | Do not modify that predicate or release those holds. A separate positive input-history proof can qualify only the narrow interruption case. |
| An old ToolCall proves a running tool or idle composer. | A permission request can already have a ToolCall without any child execution. `TaskProgressPolicy` also returns null for sparse transcripts. | Combine a caught-up current turn, attributable outstanding call, age, Working screen, modal clearance and input custody. Age alone authorizes nothing. |
| Existing runner input state proves no draft. | Runner `BackendInput` records completed writes; `_terminalInputUncertain`, writers and composer-pending state are private. `TerminalSeatDeliveryEvidence` captures submission, not a general no-draft guarantee. `AgentSessionLiveSnapshot` also drops accepted-generation metadata. | Carry generation/input evidence explicitly; missing/adopted/uncertain evidence refuses. Do not reuse seat-release authority as interrupt authority. |
| All watchdog modal responses should be suppressed. | `WatchdogService` has a separately measured exact unsafe-rm menu path that sends one `2`, never Enter, then uses a non-killing failure route. | Preserve that narrow negative action with current matching-modal fencing; never turn it into generic approval or a generic bypass. |

## Decisions

- **D-1 — One classifier, three observation states.** Reuse
  `ClaudeBlockingPromptDetector` for Blocked and extend it for measured question
  layouts. Unknown covers missing, partial, oversized, stale, unsupported or
  unattributable observations. Clear needs a current recognized non-modal frame
  (live composer or qualified Working layout), not merely `Detect == null`.
  Reject a second server regex and treating absent data as permission to send.
- **D-2 — Bounded, demand-driven observation.** Classify at existing snapshot
  reads and immediately before guarded writes. Memoize parsing by session object,
  accepted generation and output sequence; invalidate on output, replacement,
  adoption and exit. Cap an inspected rendered frame at 65,536 characters and
  512 rows; exceeding either gives Unknown, never a truncated Clear. One linear
  scan, no process census, transcript reread or per-byte scanning. Server reads
  use a cancellable two-second budget and no observation retry loop. Cached
  diagnostics expire after 90 seconds; cached Clear never authorizes a write.
  Reject a new fleet poller. CARD-1518 can consume this on its existing planned
  60-second diagnostic cadence without adding another detector.
- **D-3 — Fence each automated write.** Add an opt-in guarded mode to the existing
  conditional-input request and advertise a distinct capability, proposed
  `claudeDialogGuardV1`. Require the current accepted generation, classified
  output sequence and input revision, and freshly classify again at the runner
  write boundary under the existing gate. A failed check writes zero bytes and
  returns ModalBlocked, ObservationUnknown or StaleObservation as appropriate.
  Unsupported peers never fall back to raw input. Reject a server-only preflight:
  a dialog can appear before the submitting Enter or retry.
- **D-4 — Preserve attempt custody.** A known pre-body veto consumes no delivery
  attempt and leaves queued work Pending. A veto after a body has been written
  withholds Enter and all escape/retype recovery; keep the existing typed-body
  hold and reconcile from transcript. A timeout after a write starts stays
  uncertain and is never retried as though no bytes were written. Preserve LF,
  bracketed paste, separate Enter and complete UserPrompt receipts. Reject
  marking ModalBlocked as Delivered or using a redraw as a receipt.
- **D-5 — Separate intentional dialog actions from automated messages.** Default
  automated APIs are guarded. Keep the existing explicit interactive terminal
  input route able to answer a dialog, while recording its input revision. No
  public `ignoreModal` flag is added. Startup trust/effort handling and the exact
  unsafe-rm negative action keep their established authority, with narrowly
  typed internal intents and fresh matching-layout checks. They cannot submit
  arbitrary task/channel/watchdog text. RC maintenance retains its own menu
  checks and must additionally refuse a concurrent Claude dialog. Later explicit
  CancelDialog is owned by CARD-1518, not an exception silently added here.
- **D-6 — No-composer interruption needs affirmative input history.** Retain the
  parent's two-readable-empty-composer route unchanged. The additional route is
  restricted to a qualified, non-modal outstanding-tool Working turn, with a
  matching complete submitted UserPrompt, positive input custody and unchanged
  input revision since its submit, plus the same no-progress interval. It does
  not clear any pending/uncertain composer hold. Reject `unreadable == empty`,
  Working alone, a stale ToolCall alone and Grok composer rules.
- **D-7 — Backend qualification remains an execution gate.** Ordinary refusal
  protection must work independently of an interrupt feature flag. Keep both
  parent control operations disabled until their own operation/backend canary
  and this card's direct-send activation witness pass. Requalify after a Claude
  CLI/backend change. Fake/replay evidence cannot certify native key semantics.
  A failed Ctrl+C qualification does not prevent shipping refusal protection.
- **D-8 — Keep scope small and ownership explicit.** This card supplies volatile
  observation, input-evidence transport and guarded sends. No new database
  episode, attention kind, scheduler, approve API, automatic key, kill, parking
  enablement or stall/deadline change. CARD-1517/1518 supplies durable intervention
  intents and all subsequent actions. Reject implementing the parent feature
  inside the dependency or duplicating its S0 fixture on two evolving branches.

These are implementation decisions, not unresolved product defaults. Native
measurements can refuse qualification; they cannot weaken these admission rules.

## Shared contract and write boundary

Add a bounded `ProviderDialogObservation` to the existing runner snapshot and
propagate it through HTTP/phone-home clients and `AgentSessionLiveSnapshot`.
Proposed fields: session/provider, runner store/runtime epoch, accepted generation,
output sequence, input revision, completed-observation time, Clear/Blocked/Unknown,
dialog kind, active-dialog fingerprint, safe excerpt and explicit cancel affordance.
Unknown cancellation affordance stays unknown. Never expose/use `AffirmativeKey`
in this contract. Extract at most 2,048 characters of dialog heading/refusal/cancel
instructions; omit command bodies, answers and credentials. Use existing diagnostic
redaction before persistence/logging. Fingerprints are correlation, not authority.
Diagnostic freshness uses the server's completed-read time, not an unchecked
runner clock. Preserve runner epoch/generation separately; a clock timestamp
cannot repair a mismatched identity.

Keep the state with the owning live session; a small server observation service
projects it and combines it with current-generation RC blocking. No persisted
Boolean survives a restart as Clear. A positive old Blocked may remain diagnostic
history but cannot authorize any key. Unknown gets a distinct reason while
`SessionQueueDto.ModalBlocked` conservatively holds input; the UI must not label
unavailable evidence as an observed permission dialog. Read paths do not mutate
queue/task state or acquire the queue lock recursively.

Runner guarded input must compare the classified snapshot's sequence, not a later
unrelated `GetSession` sequence. Recheck after any asynchronous wait and before the
backend call, including the existing `ConditionalInputBeforeWrite` seam. Check
generation, same session object, exit/release state, input revision and current
dialog status. Final observation and normal input share the launch/write gate;
screen capture and sequence must be read coherently under the screen monitor.
Retain the existing sequence refusal on intervening output; do not mask changes
as animation to force a write. This is the existing observation fence extended
to modal/input evidence, not a claim to freeze the vendor UI after a byte is sent.

Assign a monotonic input revision **before each attempted backend write**, including
interactive input, modal keys and conditional writes. Record in-flight/uncertain
state; failure never leaves a seemingly unchanged safe revision. Arm a positive
submission capture only from two settled readable empty composers before the
entire submitted body, under the same exclusive input ownership; reuse existing
observations when available and otherwise leave this optional proof unavailable.
Expose that capture only after full native UserPrompt identity is matched beyond
the pre-submit transcript floor, in the same conversation and generation. Include
the submit revision and current revision, without retaining prompt text in the
observation. Existing terminal delivery parsing can share factual primitives;
its release token and terminal verdict cannot be used as interrupt permission.
Restart/adoption, external/untracked writers, changed transcript binding, incomplete
input history or ambiguous submission invalidate this positive proof. Herdr and
other backends lacking complete input custody remain unsupported for D-6.

### Automated writer inventory

| Boundary / files | Required disposition |
|---|---|
| `SessionMessageQueueService.cs`: Now, SendNow, WhenIdle flush, body writes, separate/retried Enter, overlay recovery, local slash-command poll | Use shared observation at admission and guarded writes at every byte boundary. Current Claude dialog blocks proactive/recovery Esc too. Preserve queued row and typed-body hold on partial delivery. |
| `SessionMessageQueueService.Expectations.cs`: `SendExpectationNowAsync` | Refuse current modal/unknown before `commitAttempt`; recheck through the same guarded body/Enter path after commit. Return existing typed refused/uncertain outcome with a specific reason. Do not drain held ordinary rows. |
| `AgentSessionRuntime.cs` and `AgentChannelService.cs` direct sends | Ordinary automated input defaults to guarded; channel refusal publishes no successful message event. Do not infer interactive intent from `trackManualTurn`. |
| `WatchdogService.cs` configured responses | Guard before any configured response and before claiming/auditing success. No raw-buffer fallback may establish Claude Clear. |
| `WatchdogService.cs` exact unsafe-rm No path / `SendModalKeyAsync` | Preserve one exact negative key and pending-input veto; bind it to the fresh matching modal generation/sequence/revision. No Enter and no arbitrary-response escape hatch. |
| `SessionHealthActions.cs` raw maintenance and RC dismissal; `RemoteControlRecoveryService.cs` conditional maintenance | Route through guarded automated input, or the existing typed RC intent with an additional Claude-modal veto. The private runner-client call cannot bypass it. |
| `ClaudeAdapter.cs`, `RunnerClaudeAdapter.cs`, `RunnerTerminalSession.cs`: boot `SendPromptAsync`, verified and verification-disabled submits | Apply the same guard to body and each submit callback, including `VerifiedPromptSubmitter` retries. Startup trust/effort is a separate exact-layout phase, never a general text-send exemption. Unsupported in-process adapter refuses guarded input rather than using its default raw write. |
| `AgentSessionService.SendInputAsync` / `SessionEndpoints` explicit terminal input | Keep intentional interactive behavior through a separately named runtime entry point; track revisions/uncertainty. Every automated caller is migrated off that entry. Preserve existing authentication; this card grants no new authority. |

TestDesign must re-enumerate call sites with `git grep` and bind every row to an
executed negative/control witness. This is an input-path audit, not a new general
authorization framework. No public runner protocol bypass is added for convenience.

## Qualifying Ctrl+C without a readable Working composer

The Working turn is **not idle**. Measure whether it is the same outstanding-tool
turn and whether any input could have produced a draft. The proposed production
admission for the new route is the conjunction below; CARD-1517 S2 implements it.

1. Revalidate task/attempt/session/runner binding and catch up the transcript
   successfully. Identify the complete current UserPrompt and exactly one
   attributable unresolved tool call after it. Ambiguous parallel calls, an open
   AskUserQuestion, later queued prompt, matching ToolResult, interrupt or TurnEnd
   refuse this narrow route. The tool need not be claimed dead or idle.
2. Require the configured no-progress interval (currently 30 minutes by default,
   disabled/zero refuses). Measure from the newest of dispatch, current prompt,
   outstanding call, novel transcript content, observed meaningful screen progress
   and known workspace progress. A first screen-history observation cannot prove
   earlier screen silence: anchor it at that observation for this narrow route.
   Sparse transcripts use that explicit current-turn anchor rather than interpreting
   `TaskProgressPolicy`'s null verdict as a stall. Future/skewed or missing timestamps
   refuse. Check just before and at the threshold with TimeProvider in tests.
3. Require two current settled non-modal Working frames using
   `ClaudeScreen.IsWorking` and `IsSettled`, separated by the existing evidence
   settle interval. Both must belong to the same generation/input proof; newly
   meaningful content or disappearing Working marker refuses. Do not require raw
   output silence: the spinner animates. The write still uses the last classified
   frame's exact output sequence and refuses subsequent output.
4. Require the positive capture described above: a fully matched submitted prompt,
   no later input of any kind, no pending writer, no uncertain write, no pending
   terminal input and no queue/watchdog/canceled-body composer hold. A readable
   nonempty composer always vetoes. An unreadable frame alone grants nothing.
   Untyped pending refinement rows are allowed; they are not input evidence.
5. Apply the parent's explicit request, enabled/qualified backend, committed queue
   row and durable at-most-one intervention intent. One conditional `\x03`; no
   Esc/Enter fallback. Its transport acknowledgement is not an interrupt boundary
   or delivery. Wait for the real native interruption marker/cancelled boundary,
   then use ordinary guarded queue delivery and its complete UserPrompt receipt.

The original Code owner's S0 canary may use a short, declared fixture age because
it measures vendor key behavior, not the 30-minute product policy. Keep that
distinction explicit; production threshold coverage uses a controlled clock.
Starting from its existing `ClaudeConditionalControlSession`, prove two empty
idle composer observations before submitting the full probe, a matching native
UserPrompt, exclusively fixture-owned input with no later writes, the owned Node
PID still alive, the current outstanding Bash call, and two non-modal Working
frames. Replace **only** the pre-Ctrl+C `RequireEmptyComposerAsync(working: true)`
requirement with this positively evidenced helper. Keep the post-interrupt readable
empty composer requirement and full follow-up receipt unchanged. Never type a
draft into an unsafe native session just to create a red.

Record CLI version, actual backend, input count/revision, tool identity/age,
current-turn native and normalized row order, Working/dialog observations, exact
single byte, tool termination, Claude survival and next full UserPrompt/response/
TurnEnd. A changed generation, fresh progress or new input during observation
must be refused in the controlled runner/policy fixtures. If the alternative
still fails, measure which conjunct failed and retain Ctrl+C disabled; do not
relax the composer parser, age, control byte count or follow-up assertions.

## Implementation slices and ownership

### S1 — Shared observation and guarded runner transport (CARD-0680)

Change `src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs`; add a pure
`ClaudeDialogObservation.cs` beside it. Add
`src/Antiphon.SessionRunner.Contracts/ProviderDialogObservation.cs`; extend
`SessionRunnerContracts.cs` and `ConditionalInput.cs` additively, with missing
fields/capability meaning unsupported. Update `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`
for coherent snapshots, bounded classification, input revisions and the final
guard. Use HTTP and phone-home's existing snapshot/conditional-input transport;
update their forwarding/capability registrations and mapped results as needed.

Tests: new `tests/Antiphon.Agents.Pty.Tests/ClaudeDialogObservationTests.cs` for
captured permission/question layouts, ordinary prose/old frames, bounded/partial
frames and no-key observation; new
`tests/Antiphon.SessionRunner.Tests/ClaudeDialogInputGuardTests.cs` for actual
production decision/write plumbing, current-clear control, output/input/generation
races, final recheck, transport round trips, missing capability and adoption.
Input-proof tests must fail on a bypassed revision/uncertainty check, not merely
assert serialized fields. CP-1 through CP-3 close this slice.

### S2 — Every automated sender consumes the contract (CARD-0680)

Add `server/Application/Services/ProviderDialogObservationService.cs`; update
`AgentSessionRuntime.cs`, `SessionMessageQueueService.cs`, its Expectations partial,
`AgentChannelService.cs`, `WatchdogService.cs`, `AgentSessionService.cs`,
`RemoteControlRecoveryService.cs`, `server/Infrastructure/Supervision/SessionHealthActions.cs`,
`server/Application/Dtos/SessionQueueDtos.cs`, `server/Program.cs` and the adapter/
runner-client files named in the inventory. Do not give a failed read a Clear
default in an optional constructor or test adapter. Inspect owners before any
channel/session/API changes. RC and Grok behavior retain their separate contracts.

Tests: new `tests/Antiphon.Tests/Application/ClaudeDialogDeliveryTests.cs`, using
`ExpectationDeliveryFixture`/`BridgeQueueHarness`, recording adapters and the real
queue/services; cover pre-attempt refusal, modal appearing after body, Enter/retry
veto, retained queue/hold, clear-screen delivery control with full UserPrompt,
watchdog/channel/maintenance/boot paths, and the narrow interactive/startup/No
exceptions. Pin the two existing composer-hold and unsafe-rm regressions below.
CP-4 through CP-7 close this slice. No new episode table/migration is needed.

### S3 — Native watchdog activation witness and owner docs (CARD-0680)

Add `tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs` and an isolated
native fixture using the existing safe real-CLI/queue fixture patterns. Start
only owned sessions on an isolated runner, with hooks/remote-control disabled,
inherited task authority removed and a disposable cwd; never target the production
runner or a standing agent. Hold one real permission dialog, then a fresh session's
real AskUserQuestion. Invoke the actual expectation nudge delivery service once
per held dialog, not a direct classifier or hand-written refusal stub. Require
the refusal reason, zero backend input delta including Enter/Esc, unchanged
dialog, no submitted nudge/answer and no permission-gated write. Observe the
dialog before teardown; teardown joins only fixture-owned processes.

Update `docs/session-runtime-invariants.md`, `docs/ops-http.md`,
`docs/antiphon-api.md` and `ProviderContractCatalog.cs` with measured observation,
refusal, compatibility and recovery behavior. CP-8 and CP-9 are the Windows and
UnixPty activation witnesses; neither substitutes for the other. No rollout or
watchdog setting change is authorized by a test passing.

### Q1 — Resume S0 qualification on its original owner (CARD-1517/1518)

After S1/S2 are published, commission the existing Code owner
`38c4e51a-34b1-4470-921c-44d58a8e9be0` to consume them on a caller-provided base.
Do not merge/rebase its pushed branch as part of this Plan or transplant its entire
task. Its existing `tests/Antiphon.Agents.Pty.Tests/ClaudeConditionalControlCanaryTests.cs`
and `ClaudeConditionalControlSession.cs` at `7c59b561...` own the fixture changes
described above. Keep the production `TranscriptNormalizer.cs` link used by that
fixture. CP-10/CP-11 below reserve its one exact Ctrl+C method per backend.
They are continuation rows, not CARD-0680 implementation-completion claims.
Its existing permission-Esc canary/history remains in the parent plan; no redundant
Linux Esc rerun is requested solely by this planning task.

The original owner's S2 then implements the full D-6 conjunction in its proposed
`ClaudeMidTurnInterruptPolicy.cs`/`SessionInterventionService.cs` and
`ClaudeConditionalRefinementTests.cs`, consuming S1 input metadata. It must add
the hidden-composer positive/negative variants to the parent's CP-4/CP-5 during
TestDesign. Native fixture success alone cannot enable production interruption.

## What this unblocks

| Completed prerequisite | Parent slices that can proceed | Gate still owed |
|---|---|---|
| S1 shared observation | Parent S1 permission episodes and S3 early diagnostic/phase projection | Parent freshness, dedup, persistence, deadline and attention tests; no input enabled yet. |
| S2 guarded direct sends and S3 native nudge witness for a backend, activated by the deployment owner | Parent S3 explicit permission-cancel path can consume the already measured UnixPty Esc behavior; parent S2 queue/refinement can consume the shared veto | Parent action authority, replay/races, intent and receipt tests; Windows Esc remains owed. Activation must prove loaded guard/capability and source, not merely a land. |
| Q1 passes for an operation/backend, plus parent S2 positive input-proof policy tests | Parent S2 conditional Ctrl+C refinement on that backend | Parent durable at-most-one intent, natural-boundary/uncertain-write cases and complete delivered-refinement receipt. Other backends remain disabled. |
| No new dependency | Parent S4 terminal-seat recovery guidance/regression work | It can proceed independently. No release authority or automatic release deadline is added here. |

### Session wait and release checklist

| Waiting condition | What resumes or releases it? | Deadline |
|---|---|---|
| Permission/question dialog holds automated text | Authorized interactive answer, later qualified explicit parent CancelDialog, or owner stop/process exit; fresh Clear permits normal eligibility checks again | No automatic release deadline. Observation expiry only changes evidence to Unknown. |
| Body written but Enter withheld | Existing complete transcript/composer/generation evidence, or the narrowly applicable existing audited expectation-hold release | Time alone never clears a typed-body hold; no extra key is sent by this card. |
| Blocked/AwaitingInput session retains capacity | Existing Reply/input flow, explicit owner/operator release, or separately enabled parking with all its publication gates | With parking disabled, **no automatic release deadline exists** (CARD-1083). |
| Task fails while its Claude process stays live | Existing explicit operator single-seat release after ownership/source inspection, or process exit | Task failure/deadline is not a process-release deadline. |

## Verification handoff

The brief does not fold TestDesign into Plan. Next is **test-design**, which must
append `## Verification design`, bind the named methods to concrete fixtures,
finalize V/R and independently bypassable guard/PC inventory, input-path coverage,
async receipt/recovery inventory and numeric PC costs. Preserve this closed
ordinary profile. Parent Q1 and its CP-4/CP-5 amendment must retain original owner
and failed-run provenance. No whole Unit, namespace, assembly or E2E suite is owed.

For each changed software behavior CP-1..CP-5, commit/push its compiling failing
test and execute **one exact-method red**, then commit/push its implementation and
execute **one narrow green**. New transport/type skeletons may compile first but
must not implement the tested behavior before the red. Missing APIs, build/fixture
failure, zero executions or a skip are not red. CP-6/CP-7 are unchanged regression
pins, green only. Native CP-8..CP-11 are vendor/activation measurements, once per
named backend, not artificially broken native red tests. Rerun only changed or
failed rows; preserve failures and do not broaden the test profile to chase them.

Reserved acceptance IDs:

- **V-1/R-1:** current permission/question/other dialog vs Clear/Unknown; bounded
  observation, old text/blank/partial/oversized frames, no keystrokes or raw dump.
- **V-2/R-2:** final runner write fencing and input custody across generation,
  output, new/failed/interactive input, adoption, unsupported peer and transport;
  body permitted then dialog before Enter produces zero subsequent bytes.
- **V-3/R-3:** real queue/watchdog/direct-path refusal plus successful clear
  control; no false delivered event, retry, hold release or lost pending row.
- **V-4/R-4:** existing unreadable-composer hold and exact negative modal action
  remain intact; intentional interactive input remains possible and invalidates proof.
- **V-5/R-5:** actual expectation nudge on native permission and question dialogs
  refuses with zero bytes, unchanged dialog and absent tool/nudge execution.
- **V-6/R-6 (parent Q1):** one Ctrl+C with positive no-later-input Working-turn
  evidence; native boundary, stopped owned tool, live Claude, readable empty next
  composer and complete follow-up receipt. Production age/draft/replay admission
  belongs to parent CP-4/CP-5, not this native result.

## Verification design (Code owner 52a5dc76)

The commissioning brief skips the separate TestDesign stage. The closed rows
below remain unchanged. CP-1 uses the existing transcribed permission/idle
layouts plus explicitly synthetic question fixtures; those fixtures do not
qualify native AskUserQuestion layouts. CP-2/CP-3 exercise the real runner with
controlled child I/O, rendered terminal bytes and backend-write recordings.
They assert refusals and bytes, including a clear control, rather than comparing
the classifier to itself. CP-4 uses the real expectation delivery service, queue
and isolated database, asserting pre-attempt custody and a complete UserPrompt
receipt. CP-5 invokes actual direct services. Its required full inventory also
includes boot, retries, interactive/startup exceptions and RC maintenance; those
cases remain implementation obligations until executed, even if an initial
subset of that method passes. CP-6/CP-7 retain their original assertions.

Async boundaries: admission precedes the durable expectation attempt; the body
and each Enter are separately fenced; a post-body refusal retains the typed-body
hold and does not claim delivery. Only the complete correlated UserPrompt past
the floor confirms delivery. A failed backend write changes input revision and
invalidates positive history. Generation/replacement/adoption invalidate prior
identity. An unavailable transport or capability has no raw fallback. Native
CP-8/CP-9 remain owed under the commissioning restriction; Q1 stays with 38c4e51a.

Pending SourceLanding Mutation inventory (none executed by Code):

| PC | Independently bypassable guard / variants | Exact method | EstimatedMinutes |
|---|---|---|---:|
| PC-1 | frame bounds: character cap / row cap | C680_OnlyCurrentBoundedDialogsBlock | 3 |
| PC-2 | current composer / unknown frame / question choice | C680_OnlyCurrentBoundedDialogsBlock | 4 |
| PC-3 | dialog refusal / separate Enter refusal / final output fence | C680_EveryWriteRechecksTheCurrentDialog | 4 |
| PC-4 | accepted generation / runtime identity / input revision | C680_EveryWriteRechecksTheCurrentDialog | 4 |
| PC-5 | failed-write revision / uncertainty / adoption proof | C680_InputProofInvalidatesOnWriteFailureAndAdoption | 4 |
| PC-6 | pre-attempt refusal / pending row retention / receipt requirement | C680_QueueAndExpectationKeepDialogInputPending | 5 |
| PC-7 | each automated writer and unsupported-peer fallback | C680_AllAutomatedWritersUseTheGuard | 8 |
| PC-8 | typed-body hold on withheld Enter and retry | C680_QueueAndExpectationKeepDialogInputPending | 3 |
| PC-9 | exact negative action / interactive input distinction | C680_AllAutomatedWritersUseTheGuard | 3 |

The PC estimate is a 38-minute floor plus discovery/reporting. Mutation must
expand every inventory variant into a method-scoped cycle and identify missing
controls; a listed method is not a claim that all its required cases exist yet.
Ordinary portable green remains the plan's 15-minute estimate, native watchdog
16 minutes separately owed, parent Q1 12 minutes separately owned. No arbitrary
mutants or native deliberate red runs belong to this Code task.

### Checkpoints

Read `GET /api/runner-defaults` and `GET /api/session-runners` again before each
dispatch. Plan read both successfully on 2026-10-23 around 00:09 UTC: defaults
revision 2, an eligible Linux default and an eligible Windows runner; the temporary
entry was unavailable. No fleet location is embedded here. Omit `-Runner` unless
pinning a specific host. Portable rows omit `-Platform`; `-Platform Any` unpins.
Only native rows request `-Platform Windows` or `-Platform Linux` as named below.
Catalogue eligibility does not override the earlier Windows qualification hold;
the caller must resolve that operational constraint before commissioning Windows
runs. No such run was performed by Plan.

All new methods are reserved non-parameterized methods (one execution each), with
multiple labelled fixture cases inside; existing CP-6/CP-7 each select one method.
If TestDesign changes the roster, it must correct Min before Code. CP-4..CP-7
share output only for the same committed S2 green slice; reds have their own
committed source/build and reuse nothing from another SHA.

Lanes: CP-1 is portable replay without a real CLI; CP-2/CP-3 are the portable
controlled-runner lane; CP-4..CP-7 are portable .NET/Postgres. CP-8/CP-10 are
**Windows/native-owed**, modern ConPTY; CP-9/CP-11 are **Linux/native-owed**,
UnixPty. Both native watchdog rows additionally require isolated runner/Postgres.
All four real-TUI rows are owed, not measured by Plan. Cross-platform duplicate
builds are intentional: output cannot be reused between operating systems.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Agents.Pty.Tests -> bin-c680-observe/` | portable-dialog-observation | `/*/*/ClaudeDialogObservationTests/C680_OnlyCurrentBoundedDialogsBlock` | V-1, R-1 | 1 executed, 0 failed/skipped | 1 | 3 | true | n/a |
| CP-2 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c680-runner/` | portable-guarded-writes | `/*/*/ClaudeDialogInputGuardTests/C680_EveryWriteRechecksTheCurrentDialog` | V-2, R-2 | 1 executed, 0 failed/skipped | 1 | 3 | true | n/a |
| CP-3 | S1 | CP-2 | portable-input-proof | `/*/*/ClaudeDialogInputGuardTests/C680_InputProofInvalidatesOnWriteFailureAndAdoption` | V-2, R-2 | 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c680-server/` | postgres-queue-watchdog-veto | `/*/*/ClaudeDialogDeliveryTests/C680_QueueAndExpectationKeepDialogInputPending` | V-3, R-3 | 1 executed, 0 failed/skipped | 1 | 5 | true | n/a |
| CP-5 | S2 | CP-4 | postgres-direct-writer-inventory | `/*/*/ClaudeDialogDeliveryTests/C680_AllAutomatedWritersUseTheGuard` | V-3, R-3, V-4, R-4 | 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-6 | S2 | CP-4 | postgres-composer-hold-regression | `/*/*/ExpectationDirectDeliveryTests/C650_Unreadable_Claude_composer_keeps_holding_until_it_is_proved_empty` | V-4, R-4 | 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-7 | S2 | CP-4 | postgres-exact-no-regression | `/*/*/WatchdogServiceTests/C566_Unsafe_delete_refusal_sends_verified_no_once_per_episode` | V-4, R-4 | 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-8 | S3 | `tests/Antiphon.Tests -> bin-c680-native-win/` | watchdog-native-windows | `/*/*/ClaudeDialogWatchdogCanaryTests/C680_LiveNudgeRefusesPermissionAndQuestionDialogs` | V-5, R-5 | 1 executed, both dialog cases witnessed, 0 failed/skipped | 1 | 8 | true | `ANTIPHON_HEADED_TESTS=1` |
| CP-9 | S3 | `tests/Antiphon.Tests -> bin-c680-native-unix/` | watchdog-native-unix | `/*/*/ClaudeDialogWatchdogCanaryTests/C680_LiveNudgeRefusesPermissionAndQuestionDialogs` | V-5, R-5 | 1 executed, both dialog cases witnessed, 0 failed/skipped | 1 | 8 | true | `ANTIPHON_HEADED_TESTS=1` |
| CP-10 | Q1 | `tests/Antiphon.Agents.Pty.Tests -> bin-c680-control-win/` | parent-control-windows | `/*/*/ClaudeConditionalControlCanaryTests/C1517_ToolCtrlCLeavesSessionAlive` | V-6, R-6 | 1 executed, 0 failed/skipped | 1 | 6 | true | `ANTIPHON_HEADED_TESTS=1` |
| CP-11 | Q1 | `tests/Antiphon.Agents.Pty.Tests -> bin-c680-control-unix/` | parent-control-unix | `/*/*/ClaudeConditionalControlCanaryTests/C1517_ToolCtrlCLeavesSessionAlive` | V-6, R-6 | 1 executed, 0 failed/skipped | 1 | 6 | true | `ANTIPHON_HEADED_TESTS=1` |

Execute one committed slice group through `tools/Antiphon.Checkpoints`:
`run --plan docs/superpowers/plans/2026-10-23-card-0680-claude-dialog-send-guard-plan.md
--expected-source-sha <full-committed-sha> --rows <explicit-CP-list>`.
Use `CP-1,CP-2,CP-3`, then `CP-4,CP-5,CP-6,CP-7`; each native row is a separate
platform run. The original owner selects only its Q1 rows here, alongside the
parent plan's separately owed selections. Wait through exit 75 until the executor
settles; do not end a delegate with a run in flight. Use the checkpoint's build
slot; any necessary tool bootstrap goes through `scripts/build-slot.ps1` and is
named in the report. Slot timeout is not run, never permission to bypass the gate.
Do not co-schedule native/FakeClaude runs with Antiphon.Tests. Keep generated
receipts/TRX ignored, retain unedited CHECKPOINT lines and exact source/build
provenance in the stored report, and remove owned alternate output through the
documented custody procedure.

Estimated green budget: **15 minutes** portable, **16 minutes** native watchdog,
and **12 minutes** parent Ctrl+C qualification, excluding slot waits; five exact
software reds add approximately **9-18 minutes** depending on shared compiling
test commits. These are budgets, not measurements. TestDesign supplies the later
method-scoped PC floor without adding redundant mutation cycles to Code.
CARD-0680 can complete with its own CP-1..CP-9; Q1 failure remains an explicit
parent gate, never relabelled green or evidence that guarded delivery failed.

## Dispatch handoff

Land this documentation through its own owner. Next: **test-design** for the
CARD-0680 implementation rows and the explicit parent Q1/CP-4/CP-5 amendment.
Code is not ready solely because checkpoint names exist: concrete fixture and
guard design remains owed. No build/test, native Claude input, deployment,
runner restart, card mutation or production feature activation occurred in Plan.
