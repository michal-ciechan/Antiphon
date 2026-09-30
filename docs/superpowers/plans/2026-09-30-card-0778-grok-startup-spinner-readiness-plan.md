# CARD-0778: Grok startup readiness during spinner redraws

Date: 2026-09-30. Stage: Plan, task `a2b4c9ee-a652-45b2-a579-3a20369bebdd`.
Source inspected: `d7456a2352d15391f37eca8781c16db499a3759d`.
Next stage: **TestDesign**, including the Windows evidence prerequisite below.
This artifact changes no product code and does not claim native qualification.

## Problem and ground truth

Desktop session `98f50651-c481-4b01-9dd3-db0b352514c5` (Grok Build 1.0.41,
CARD-0508 task `76b7b036`) ended 72.3 seconds after dispatch on 2026-09-27,
before any normalized `UserPrompt`. CARD-0778 reports an empty composer,
`Starting session...`, MCP `(0/2)`, and a continuously changing spinner in its
retained terminal buffer. Its later task failure was dead-session reconciliation,
not a four-minute-lived Grok process. The card and its one history revision were
read in full for this plan.

| Card assumption / question | Observed implementation or evidence | Consequence |
|---|---|---|
| Spinner redraws can exhaust readiness. | `server/Infrastructure/Agents/SessionRunner/RunnerGrokAdapter.cs:172` calls `WaitForQuietAfterVisibleAsync`; `RunnerTerminalSession.cs:206` first requires visible raw output, then resets `lastChange` on every changed buffer sequence at `:240`. | A redraw stream faster than the quiet interval prevents success, even if a usable composer is already present. This code mechanism is established; its exact incident log is not. |
| There is a one-second quiet window and sixty-second maximum. | `server/Application/Settings/AgentRegistrySettings.cs:109` defaults to 1000/60000 ms, with a 2000 ms minimum from process start at `:111`. | Keep the defaults; change the evidence that settles, not the timeout to hide the symptom. |
| A quiet dashboard means input is safe. | `RunnerGrokAdapter.cs:182` checks sign-in only after quiet; `:201` then checks trust. There is no positive composer gate. | Quiet alone can also admit a quiet but unusable screen. Check blockers and positive input evidence on every current frame. |
| Trust clearance implies ready. | `RunnerGrokAdapter.cs:379` accepts a raw-history or current-screen trust match, writes `y`, then treats disappearance as success. `AgentRegistrySettings.cs:112` documents zero trust-settle as skipping verification. | Historical trust text must not authorize input; disappearance must lead back to the composer gate. Zero must not bypass that gate. |
| The launch was killed after the failed gate. | `server/Application/Services/AgentSessionService.cs:2203` turns false readiness into a launch exception; launch cleanup calls `KillAndDisposeAsync` at `:307` and `:658`. | Timing, empty transcript and KilledByRequest fit cleanup after readiness failure. Preserve cleanup; do not change working-session stop policy. Desktop incident logs are still required to establish the precise caller. |
| MCP `(0/2)` proves the composer cannot accept work. | No such provider contract was found. `docs/agent-kinds.md:318` describes lazy transcript creation and explicitly says redraw/MCP indicators are not submission evidence. | Neither MCP counts nor an empty-looking box establish input capability. Measure it. |
| This shares the Codex gate. | `server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs:137` uses `CodexReadyWait`; Grok does not. `src/Antiphon.Agents.Pty/CodexStartupReadiness.cs:294` already illustrates elapsed-time settling of a positive screen region. | Implement a Grok-specific classifier/wait. Do not change Codex, Raw, OpenCode, Claude or the shared quiet helper. |
| Existing fixtures establish this screen. | `client/src/features/board/__fixtures__/grok-startup.ts:1` contains only a 2026-08-23 control-sequence prefix. `src/Antiphon.FakeGrok/Program.cs:263` defaults to `Fake Grok ready`. The two existing Grok adapter modal test classes use a hand-authored cwd plus `>` as ready. | None is a captured 1.0.41 usable dashboard. Do not make these strings the new production readiness grammar. |

Read-only incident recheck on 2026-09-30 at approximately 07:38 UTC:
`GET /api/sessions/98f50651-c481-4b01-9dd3-db0b352514c5/transcript?since=0`
returned `entries=[]`, `lastSequence=0`; the corresponding `/buffer` returned
HTTP 500. No historical buffer fixture was recovered in this stage. No desktop
filesystem is reachable from this assigned Linux mirror. CPU pressure, two
unfinished MCP operations, and a stale desktop runner remain possible contributing
conditions, not demonstrated causes.

Owners read: `docs/project-context.md`, `docs/agent-kinds.md`,
`docs/ai-agent-tui-configuration.md`, `docs/session-runtime-invariants.md`,
`docs/adr/0002-modern-conpty-backend.md`, `docs/testing-and-build.md`,
`docs/ops-http.md`, `docs/logs.md`, and the Plan stage bundle.

## Decisions

**D-1 — Positive, current Grok input evidence.** Add a Grok startup classifier,
settle tracker and bounded wait in `src/Antiphon.Agents.Pty/GrokStartupReadiness.cs`.
Use one `SessionRunnerSnapshotDto` per observation through the existing
`RunnerTerminalSession.GetSnapshotAsync`; raw output and rendered screen must be
from that same response. Classify the rendered screen. Raw output is diagnostic
and replay evidence, never authority for a key or a ready verdict. Positive
evidence is the fixture-qualified Grok dashboard identity and actual empty input
region with its input-enabled hint/footer. Pin exact supported layouts in S1;
a banner, cwd, `>`, OSC title, absent blocker, or arbitrary visible text alone is
insufficient. Unknown layouts fail closed.

**D-2 — Ignore animation only outside the qualified input state.** Settle a
semantic region containing the recognized composer, its enabled state and relevant
dashboard identity. Do not include terminal sequence, cursor blink, elapsed
spinner glyphs, or an independently qualified decorative status row in its identity.
Do not strip all braille characters or whole MCP/startup lines from the screen:
those lines may carry meaningful blocking text. At least two completed positive
observations and `GrokReadyQuietPeriodMs` of the same semantic region are required.
A changed region, missing frame, blocker or unrecognized frame resets settlement.

The incident's `Starting session...` plus MCP `(0/2)` is **unqualified**, and is
not an allow rule in this plan. Default it to `StartingSession`/not-ready. S1 may
qualify a narrowly identified form as decorative only if the real CLI accepts a
complete nonce prompt while that form is still present, before any ready-state
transition. Otherwise only the subsequent proven input-enabled frame may settle;
the unchanged stuck startup must fail with useful evidence. A product rule that
accepts the incident state requires the S1 receipt and a reviewed TestDesign
amendment. This is an evidence prerequisite, not a request to guess in Code.

**D-3 — One bounded wait, including modal handling.** Keep maximum 60000 ms,
settle 1000 ms and minimum total age 2000 ms. Use an injectable `TimeProvider`
and monotonic elapsed time for the wait; translate the terminal's `StartedAt`
into the remaining minimum-age requirement once at entry. Include snapshot reads,
trust writes, trust clearance and minimum-age waiting within the same maximum.
Use the same clock for delays and deadline cancellation, with a bounded wait
around a snapshot operation that fails to honor cancellation. Recheck elapsed
time, exit and cancellation after each await, before returning success or typing.
Never accept a frame that returns after the deadline. Keep observing during the
minimum-age floor; a modal arriving then invalidates readiness. Nonpositive maximum
means false with no input; nonpositive settle still requires two observations.
An already-exited child returns false; caller cancellation propagates cancellation
through existing launch cleanup. No restart, retry, kill or prompt originates in
the readiness helper.

**D-4 — Sign-in precedes trust; trust is a single explicit action.** Evaluate
the existing current-screen sign-in detector first on every poll, including after
trust. Preserve `AgentLaunchBlockKind.ProviderSignInRequired`, its GROK_HOME remedy,
and zero input. Only a current recognized Grok trust dialog with both question and
affirmative choice authorizes the existing `y`, once per wait. Continue observing
after it leaves. `GrokTrustPromptSettleMs` remains a trust-clearance sub-budget,
capped by the overall deadline; zero disables that sub-budget only, never positive
composer verification. Old raw trust text cannot send `y` into an idle composer.
No other startup modal is auto-answered.

**D-5 — Capture before teardown, with bounded content.** Add
`server/Infrastructure/Agents/SessionRunner/GrokStartupCaptureStore.cs` and wire a
single terminal failure callback from the Grok wait through `RunnerGrokAdapter`.
Persist the last already-observed frame before readiness returns false; do not
make an extra failing runner call after timeout. Structured log fields are session
ID, reason, elapsed ms, frame sequence/time, positive observation count and whether
MCP was observed. Log a capture path, not raw/screen contents or credential paths.
Reasons distinguish `Unknown`, `StartingSession`, `SignIn`, `Trust`, `Working`,
`ComposerUnavailable`, `Deadline`, `Exited`, and snapshot failure; keep last screen
reason separate from terminal outcome such as Deadline.

Use Grok-specific typed settings in `AgentRegistrySettings`:
`GrokStartupCaptureDirectory` (default temp `antiphon-grok-startup`) and
`GrokStartupCaptureKeep` (default 10, clamp to 1..100, never unlimited).
Cap rendered screen and raw tail at 8192 UTF-16 units each *before* control-byte
escaping, record truncation and escape all controls in stored text. That bounds
encoded payload growth; cap metadata at 1024 bytes and the complete UTF-8 file at
128 KiB, recording truncation. Suppress raw and
screen entirely once sign-in has been seen, since raw history can retain OAuth
codes; retain metadata only. Do not print exception text that may contain captured
content. Prune only this store's completed `grok-startup-*.txt` files, never other
files. Missing frame and write/retention failure must preserve the failed readiness
verdict and cleanup; report metadata-only capture failure. Reuse the *pattern* of
the Codex store (`RunnerCodexAdapter.cs:182`), without refactoring its behavior.
These local diagnostic files are not transcript receipts and are not automatically
committed or uploaded.

**D-6 — Readiness is distinct from delivery.** Preserve rules initialization,
queue holds, LF/bracketed-paste/separate Enter, transcript baselines, prompt matching,
and all turn-end behavior. The proof after readiness is a whole matching
`UserPrompt` past the delivery baseline, with rules ACK first where configured.
No transcript is required *before* startup input: Grok creates it lazily on submit.
Do not mark the historical task delivered, release its seats, or restart production.

Rejected alternatives: increasing the 60-second timeout; accepting any quiet
normalized screen; accepting MCP zero/nonzero as a universal readiness flag;
matching ready text in accumulated raw history; probing the live composer by typing
work; changing shared PTY readiness or Codex; and generating a supposedly captured
fixture from the card's prose. None supplies the missing input-capability evidence.

## Slices

**S1 — Capture and freeze the contract (TestDesign prerequisite).** Add sanitized
evidence under `tests/Antiphon.Tests/Agents/Fixtures/card0778/`:
`startup-frames.json` and `provenance.md`. The fixture must contain original ordered
PTY chunks from a known reset state (not concatenated screen text), elapsed offsets,
sequence numbers, cols/rows, captured rendered checkpoints, CLI version, OS/backend,
session identity, and SHA-256 digests. Replay those chunks through the production
`TerminalScreen` at the captured dimensions and compare the independent recorded
frames. Preserve two or more distinct spinner redraws across more than the settle
interval. Record sanitization replacements without removing UI delimiters, changing
widths, or inventing chronology. A detached raw tail without an initial screen is
not a replayable capture; retain it as partial evidence and obtain a fresh capture.

Retrieve the original session's dated 2026-09-27 server, runner and pty-host logs
using the paths in `docs/logs.md`, not only the helper's latest rotation. Record
missing evidence as missing. If the retained incident buffer is gone, label the
replacement as a new reproduction. In an isolated Windows runner/PTY window,
capture fresh startup, trust-to-startup, proven idle composer, and startup redraws;
record both named MCP startup operations and any errors when available. Include an
isolated signed-out/current trust capture with all approval material removed.

Pair each claimed usable layout with a timestamped native normalized complete
`UserPrompt` nonce receipt from that session and the exact input submission time.
For the disputed startup state, the receipt must precede its transition to ordinary
idle. Use the established real-CLI stub proxy lane with both Grok redirect variables
and the nonce request oracle (`RealCliStubEnv.ForGrok`); no live messaging broker or
unrelated sessions. An early typed prompt that waits in the composer until startup
finishes does **not** qualify early readiness. A newer CLI capture qualifies only
that recorded version/layout and cannot retroactively prove 1.0.41. If the safe
isolated probe cannot reproduce or capture the necessary evidence, TestDesign
reports that specific prerequisite outstanding; Code does not fabricate it.

TestDesign amends D-1/D-2 with the actual input-region boundaries and supported
startup-state classification, finalizes the test methods below, and commits/pushes
the evidence and plan. No product code is needed to finish this slice. Preserve
the distinction between offline fixture replay and real Windows acceptance.

**S2 — Gate and diagnostic implementation.** Add the Grok helper and capture store
from D-1/D-5; replace only `RunnerGrokAdapter.WaitForReadyAsync` and its private
trust helper; document the retained timing properties' new meanings. Add the three
unit classes in the roster below. Update the two existing Grok modal tests' ready
fixtures to the captured positive screen, retaining their assertions and counts.
Use a constructor-compatible optional clock/options seam; do not change all
protocol interfaces or session-runner contracts to test a private polling loop.
Own and dispose every scripted adapter so its runner exit watcher is canceled.

**S3 — Boundary and native regression proof, documentation.** Add
`tests/Antiphon.Tests/Application/GrokStartupReadyOrderingTests.cs` using the real
Grok adapter with a scripted `ISessionRunnerClient`, production launch/runtime/queue
and isolated test DB. Extend
`SessionMessageQueueGrokPtyIntegrationTests.cs` with the native spinner case below.
Teach `src/Antiphon.FakeGrok/Program.cs` to paint the captured legitimate ready shape
at ordinary startup, preserving its existing `Fake Grok ready` marker for harnesses;
add an opt-in captured redraw replay mode for the new test. The marker itself must
never be a production readiness rule. The replay mode has a stopped-and-awaited
writer lifetime and does not interleave redraw bytes into submitted prompt output.
Keep existing input and ACP transcript semantics. Copy fixture assets via the test
project's existing fixture convention, and register any new Slow class in its
allowlist. Update `docs/agent-kinds.md`, `docs/session-runtime-invariants.md` and
`docs/logs.md` for the gate, diagnostic retention and qualification limits.

Commit and push each slice. Run the ordinary checkpoint group once after all three
slice commits exist. Product, fixture and documentation changes must be committed
before that run; do not edit the worktree while any owned driver is active.

## Verification design

This is the Plan's proposed closed roster. TestDesign is separate: it must resolve
S1's capture before turning it into an executable Code brief. Every method below
is one nonparameterized TUnit execution; internal matrices are assertions, not extra
test counts. TestDesign may revise the roster only by amending this plan and its
PC/cost/checkpoint mappings together.

### Coverage and new test roster

| ID | Class.Method (new unless marked regression) | Outcome to assert |
|---|---|---|
| V-1 | `GrokStartupReadinessTests.Captured_idle_frames_stay_ready_while_spinner_redraws` | Replay real raw chunks through `TerminalScreen`; qualified input-region identity remains equal while spinner frames and sequence differ; classifier marks each qualified frame positive. |
| V-2 | `GrokStartupReadinessTests.Unknown_or_blocked_current_frames_never_become_ready` | Corpus of blank/ANSI-only, bare prompt, partial/nonempty composer, unqualified startup, working and modal frames is negative even when quiet; explicit expected reason per case. |
| V-3 | `GrokStartupReadinessTests.Current_frame_overrides_raw_history` | Old ready/trust/sign-in raw text cannot override the opposite current rendered frame; no normalization that conflates past and present. |
| V-4 | `GrokStartupReadinessTests.Composer_change_or_blocker_restarts_settle` | Gated timelines with changed input identity and intervening blocker each need a fresh entire settle interval. |
| V-5 | `GrokStartupReadinessTests.Settle_requires_two_observations_and_elapsed_time` | Held second read never succeeds from clock advance alone; normal threshold and zero-settle both require two completed observations. |
| V-6 | `RunnerGrokAdapterReadyTests.Spinner_sequence_advance_does_not_prevent_positive_ready` | Real adapter reaches true from captured positive frames while `LastSequence` advances every poll; exactly one snapshot per decision and no split buffer/raw reads or input. |
| V-7 | `RunnerGrokAdapterReadyTests.Animated_sign_in_blocks_without_input_and_sets_launch_block` | Changing sign-in frame (also containing trust text) fails promptly, zero writes, correct provider block/remedy; no quiet wait first. |
| V-8 | `RunnerGrokAdapterReadyTests.Current_trust_is_answered_once_before_positive_ready` | Current trust sends exactly `y`, once despite repeated frames; stale raw trust sends nothing; only a subsequent settled qualified composer returns true. |
| V-9 | `RunnerGrokAdapterReadyTests.Post_trust_blank_or_sign_in_is_not_ready` | Trust disappearance into blank remains pending/fails; disappearance into sign-in sets the block and sends no further input; include zero trust sub-budget. |
| V-10 | `RunnerGrokAdapterReadyTests.One_deadline_covers_reads_trust_and_minimum_age` | Controlled-clock matrix: zero max, late successful snapshot, hung snapshot, late trust, minimum age beyond max and blocker during minimum age. Deadline never extends; held I/O receives cancellation; no success/input after expiry. |
| V-11 | `RunnerGrokAdapterReadyTests.Exit_and_cancellation_stop_without_input` | Exit before/between frames returns false; cancellation during read/delay propagates; no later keys, polls or orphan tasks. |
| V-12 | `RunnerGrokAdapterReadyTests.Timeout_captures_last_frame_and_io_failure_preserves_failure` | One capture of the actual last observed frame before false, reason/sequence linked to the session; log has metadata/path only. An unwritable destination still returns failure and permits caller cleanup. |
| V-13 | `GrokStartupCaptureStoreTests.Content_is_bounded_and_sign_in_material_is_suppressed` | Oversized screen/raw are capped before escaping, null frame is explicit, original lengths/truncation recorded; sign-in sentinel never appears in file/log even if it survives in raw history. |
| V-14 | `GrokStartupCaptureStoreTests.Retention_removes_only_owned_captures` | Keep boundary (including zero/negative/oversized configuration) leaves bounded own files and preserves an unrelated sentinel; actual filesystem results, not a constant comparison. |
| V-15 | `GrokStartupReadyOrderingTests.Work_waits_for_ready_rules_ack_and_complete_prompt` | Production launch holds queued work during unqualified redraws, then positive gate enables rules refresh, matching ACK, ordinary work and whole persisted nonce `UserPrompt` in order. A scripted runner provides ACP/normalized data; this is an integration receipt, not live CLI proof. |
| V-16 | `GrokStartupReadyOrderingTests.Unready_launch_cleans_up_and_keeps_work_pending` | All-budget unready spinner yields Failed and owned-generation cleanup, zero prompt/input, ordinary row still Pending/zero attempts, no fabricated transcript, diagnostic frame retained before disposal. |
| V-17 | `SessionMessageQueueGrokPtyIntegrationTests.Captured_spinner_reaches_ready_and_complete_user_prompt` | Windows modern ConPTY, isolated runner client, actual Grok adapter and launch gate: captured redraw stream continues past the settle interval, launch becomes ready, queued nonce arrives whole via native tailer/runtime in exactly one UserPrompt. Assert pre-ready hold, sequence advance, no duplicate submit and child cleanup. |
| R-1 | Existing `RunnerGrokAdapterTrustPromptTests` (4), `RunnerGrokAdapterSignInPromptTests` (3), `GrokAdapterTests` (2), `RunnerGrokAdapterTurnCompleteTests` (7), `RunnerCodexAdapterReadyTests` (12 expanded) | Modal protections, factory choice, turn-completion/transcript floor and untouched Codex readiness behavior. |
| R-2 | Existing `GrokRulesReadyOrderingTests` (2 expanded), `GrokRulesQueueBarrierTests` (28 expanded) | Rules receipt before ready and all existing queued/Now/send-now/expired-hold barriers. |
| R-3 | Existing `SessionMessageQueueGrokPtyIntegrationTests` (4), `GrokDelegateEndToEndTests` (5) | FakeGrok's ordinary launch display still supports real delivery, spill, native transcript and delegate paths. |

V-15/V-16 use the real adapter, not `FakeAgentProtocolAdapter.ReadyHold` alone.
Their durable chain is queued message ID -> session/generation -> transcript
baseline -> complete UserPrompt -> persisted delivery verdict; rules refresh ID,
generation and hash join the prerequisite ACK. Existing production queue recovery
is unchanged. Do not add unrelated crash/recovery behavior to this card.
V-17 retains the class's assembly-local process limiter and Headed serialization;
the test owns temporary state and kills/awaits only its children in `finally`.
No `Program` fixture may use production port 17204. Controlled-clock tests advance
timers as well as elapsed time, with an independent short real-time guard and
release/cancel/await cleanup for every held TaskCompletionSource.
V-6's fake must also answer legacy raw/buffer reads with valid advancing-sequence
data: PC-6 must fail the ready assertion, not throw a fake's NotSupportedException.
For V-17, the startup deadline is shorter than the test's outer guard; capture the
completed launch result/exception and assert successful readiness plus receipt.
PC-17 must reach that assertion after the legacy gate fails, not hang the test host.

### Positive controls

Execute after ordinary Review and confirmed land, in the commissioned SourceLanding
Mutation workspace. For every PC, use only its named method, fresh baseline/red/
restored-green outputs and TRX, with one executed test per phase. The exact filter
is `/*/*/<Class>/<Method>` using the literal Class.Method in the linked V row above;
there are no argument suffixes. Require baseline/green exit 0 and red exit 1 with
the named outcome assertion failing. Compile failures, fixture-load failures,
zero tests and a wedged process are not red evidence. Restore exact source and
refresh timestamps before rebuilding. Mutations share production files and run
serially; do not claim independent batching savings.

| PC | Method | Compiling production mutation | Expected red assertion |
|---|---|---|---|
| PC-1 | V-1 | Include the observed spinner/status decoration in the semantic region. | Captured qualified frames no longer share one input identity. |
| PC-2 | V-2 | Accept a bare composer marker without the qualified dashboard/input-enabled structure. | The quiet bare-prompt negative is accepted. |
| PC-3 | V-3 | Classify accumulated raw text in place of current rendered text. | Stale raw/current-screen verdict differs from the specified current verdict. |
| PC-4 | V-4 | Retain candidate start across a changed region or a negative observation (run each reset omission as a subcase). | Ready occurs before a fresh full settle interval. |
| PC-5 | V-5 | Remove the two-observation floor. | Zero-settle first frame reports ready; held second-read assertion fails. |
| PC-6 | V-6 | Wire Grok back to `WaitForQuietAfterVisibleAsync`. | Advancing-sequence positive fixture times out instead of returning true. |
| PC-7 | V-7 | Delay the sign-in check until after positive readiness, leaving its block construction intact. | Animated sign-in times out without the required ProviderSignInRequired block. |
| PC-8 | V-8 | Remove the per-wait trust-written latch. | Held current-trust frames receive more than one `y`. |
| PC-9 | V-9 | Return true as soon as trust leaves, bypassing positive reclassification. | Blank/sign-in transition incorrectly reports ready. |
| PC-10 | V-10 | Remove the post-read deadline check and allow the late positive response to satisfy settle. | Ready is true after the shared deadline; the independently gated test completes, rather than relying on a host hang. |
| PC-11 | V-11 | Remove the process-exit check between observations. | A qualified second frame admits an already-exited process. |
| PC-12 | V-12 | Remove only the adapter's failure-frame callback wiring. | No last-frame capture exists before the failed launch cleans up. |
| PC-13 | V-13 | Remove sign-in-content suppression (also run cap removal as a separate subcase). | Sign-in sentinel appears in persisted content; cap omission exceeds the declared bound. |
| PC-14 | V-14 | Bypass owned-file pruning. | The retained capture count exceeds the configured bounded count. |
| PC-15 | V-15 | Bypass the Grok adapter wait by returning true at entry. | Ordinary/rules input occurs during the explicitly held unqualified phase. |
| PC-16 | V-16 | Make the adapter's terminal Deadline result return true. | Failed/zero-input/Pending assertions fail for the all-budget unready launch. |
| PC-17 | V-17 | Restore sequence-quiet readiness for Grok in the native test build. | The real replay never reaches ready, and the complete UserPrompt receipt is absent. |

PC-4 and PC-13 each contain two separate red/restore subcases. There are **19**
control cycles for **17** new methods. PCs for integration/native cases mutate the
readiness production boundary, not the fake's expected strings or test assertions.

### Platform, execution and receipt rules

The live placement read at 07:38 UTC returned runner-defaults revision 2, a Linux
global default, an available Windows runner, a draining Linux runner, and another
Linux runner accepting new work. This is an observation, not a host pin. Re-read
`GET /api/runner-defaults` and `GET /api/session-runners` at dispatch; honor
`acceptingNewWork`, not `dispatchEligible` alone. CP-1/CP-2 are portable Linux or
Windows lanes. **CP-3 requires Windows** and cannot be credited by skipped Linux
tests. Qualify the same source SHA in the Windows slice; omit `-Runner` unless
intentionally pinning the captured host, and request `-Platform Windows` for S1's
native capture and CP-3. Linux success alone does not close CARD-0778.

The brief explicitly selects the CARD-0823 workaround: do not bootstrap or run
`tools/Antiphon.Checkpoints` for these rows. Run `scripts/run-checkpoint.ps1` one
row at a time under `scripts/build-slot.ps1`, and keep waiting for each owned driver
to exit. Avoid nesting a second `pwsh` process that separately requests another
slot while holding the first. The same-process recipe below uses a task-owned,
parameterless row script: the wrapper invokes `.ps1` with the call operator
(`scripts/build-slot.ps1:126`), and the broker reuses an existing lease for the
same PID/start time (`src/Antiphon.SessionRunner/BuildSlotBroker.cs:82`). The inner
driver releases that lease after its tests; the outer finally may find it already
released. Do not use `-NoSlot` or change broker budgets.

For CP-1, create this ignored `.antiphon/c778-cp1.ps1` containing the single command
(the ampersand is intentional; do not replace it with a child `pwsh`):

```powershell
& ./scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c778-ready/ -Filter '/*/*/(GrokStartupReadinessTests*)|(RunnerGrokAdapterReadyTests*)|(GrokStartupCaptureStoreTests*)|(RunnerGrokAdapterTrustPromptTests*)|(RunnerGrokAdapterSignInPromptTests*)|(GrokAdapterTests*)|(RunnerGrokAdapterTurnCompleteTests*)|(RunnerCodexAdapterReadyTests*)/*' -Expect GrokStartupReadinessTests,RunnerGrokAdapterReadyTests,GrokStartupCaptureStoreTests,RunnerGrokAdapterTrustPromptTests,RunnerGrokAdapterSignInPromptTests,GrokAdapterTests,RunnerGrokAdapterTurnCompleteTests,RunnerCodexAdapterReadyTests -MinExecuted 42 -ResultsRoot .antiphon/c778-cp1
```

Then execute `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c778-cp1 -- ./.antiphon/c778-cp1.ps1`.
Give CP-2/CP-3 their own parameterless row scripts with the exact table filter,
output, floor, and all class names in `-Expect`. Set `TUNIT_MAX_PARALLEL_TESTS=1`
for CP-2/CP-3's child test process and restore the previous environment afterward.
The driver adds `UseAppHost=false` on Linux; do not force Linux fake apphosts.
Native CP-3 uses the driver's Windows defaults and must find all staged binaries.
Do not add an extra solution build when an asset is missing: fix the row's build
or report it failed. This recipe is source-inspected, not executed in Plan;
TestDesign validates argument/lease behavior with the existing offline script
seams if needed, recording that extra validation command and its reason.

CARD-0818 and CARD-0828 affect Checkpoints test classes with disposal/owner-watch
hangs. None is in this plan's exact filters. Do not add a Unit-category or namespace
run that pulls them in, or mask their failures with retries. CARD-0835 means the
driver's `commit=HEAD` alone is insufficient: capture HEAD and tracked/untracked
source status immediately before and after each ordinary row, require committed
source throughout, and keep logs under ignored task-owned results paths. Any dirty
source invalidates ordinary clean-SHA evidence. Mutation reports must explicitly
identify their deliberate patch/digest rather than certifying it as pristine HEAD.

Use fresh result roots per row/rerun. Exit 4 is a reported slot timeout; a
`BUILD SLOT unleased` line cannot satisfy this plan's leased-run requirement.
Require fresh executed rosters, build/run exit receipts, exact counts and zero
skips. Report each CP with the standard CHECKPOINT line, commit, platform, elapsed
time, TRX and rerun count. Each additional build/test needs a stated reason.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---:|---|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c778-ready/` | grok-ready-unit | `/*/*/(GrokStartupReadinessTests*)\|(RunnerGrokAdapterReadyTests*)\|(GrokStartupCaptureStoreTests*)\|(RunnerGrokAdapterTrustPromptTests*)\|(RunnerGrokAdapterSignInPromptTests*)\|(GrokAdapterTests*)\|(RunnerGrokAdapterTurnCompleteTests*)\|(RunnerCodexAdapterReadyTests*)/*` | V-1..V-14, R-1 | all 42 executed, 0 failed, 0 skipped; Linux or Windows | 42 | 12 | 15 | true | n/a |
| CP-2 | S1-S3 | `tests/Antiphon.Tests -> bin-c778-ordering/` | grok-ready-ordering | `/*/*/(GrokStartupReadyOrderingTests*)\|(GrokRulesReadyOrderingTests*)\|(GrokRulesQueueBarrierTests*)/*` | V-15, V-16, R-2 | all 32 executed, 0 failed, 0 skipped; Linux or Windows, test Postgres required | 32 | 12 | 15 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c778-native/` | grok-ready-native | `/*/*/(SessionMessageQueueGrokPtyIntegrationTests*)\|(GrokDelegateEndToEndTests*)/*` | V-17, R-3 | all 10 executed, 0 failed, 0 skipped; Windows only, modern backend for V-17, test Postgres and staged native assets required | 10 | 25 | 25 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost

Estimates, not measured runs: CP-1 **12 minutes Linux / 15 Windows**, CP-2
**12 / 15**, and the deliberately slow native CP-3 **25 minutes Windows**,
each including its isolated build. Portable rows on Linux plus Windows native
qualification cost **49 minutes**, the sum of EstimatedMinutes. All-Windows costs
**55 minutes**. The native row's ten process/delivery scenarios dominate test time;
CP-2 also pays isolated PostgreSQL setup and rules/delivery polling. No broad-suite
run or checkpoint-tool bootstrap is budgeted.

S1: allow **45 minutes** for initial Windows log/capture work and **30 minutes**
for TestDesign fixture/roster finalization. Missing 1.0.41 or irretrievable logs may
need a separately commissioned reproduction; this estimate cannot promise that
evidence exists. S2/S3 authoring: **120 minutes**, plus ordinary Code verification
49 = **169 minutes** before repairs and slot wait. Ordinary Review and landing are
additional. SourceLanding Mutation: budget 16 unit control cycles at **9 minutes**
each (baseline/red/green isolated builds), two DB integration controls at **15**
each, one native control at **20**, plus **15** for restoration/reporting:
**209 minutes**. No batching discount; revise with measured row times in TestDesign.
Slot contention has no zero-wait guarantee and must be reported separately.

## Acceptance and handoff

TestDesign must supply the real terminal fixture/provenance and resolve the
input-enabled predicate before Code. If startup remains truly blocked by MCP,
retain that negative classification, document the two MCP operations and route the
underlying initialization cause back to investigation; do not claim a spinner
recognition patch cured it. The safe independently useful change remains current
positive readiness plus a bounded failure capture.

Code is complete only with the committed implementation/fixtures/docs, all three
checkpoint receipts at one source SHA (including Windows), and the PC design ready
for post-land Mutation. Readiness success alone never proves prompt delivery.
Deployment/production restarts are outside this Plan dispatch. Any later activation
must follow the canonical checkout runbook and directly verify `/api/version`.

Plan validation: the document/source audit passed for one Checkpoints heading,
three 12-column rows, floors 42/32/10 (84 planned executions), all nine existing
class expansion counts, and 17 new-method/PC mappings (19 control cycles).
`git diff --check` passed. No product build, test suite, live CLI launch, restart
or positive-control execution was performed in this stage.
