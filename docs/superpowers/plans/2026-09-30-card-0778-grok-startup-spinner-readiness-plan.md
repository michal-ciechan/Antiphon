# CARD-0778: Grok startup readiness during spinner redraws

Date: 2026-09-30. Stage: Plan, task `a2b4c9ee-a652-45b2-a579-3a20369bebdd`.
Source inspected: `d7456a2352d15391f37eca8781c16db499a3759d`.
TestDesign static audit: task `307e1a7f-1a23-4678-9e69-13c4a5978e29`, against
`8864419001bc99363a5476ca55bbea9d08f04cbd` (the assigned Plan commit).
Next stage: **Code S2/S3**. S1 capture and predicate review are complete; the
separate Windows CP-3 qualification remains outstanding. The recorded Grok 1.0.41
layout is qualified only at 120x30 on the captured modern ConPTY backend.

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
evidence is the fixture-qualified Grok dashboard at 120x30: scan upward from the
bottom for an empty, complete composer box and derive its row/column bounds from
its borders, not fixed row numbers. The hint row is exactly two spaces followed by
`Shift+Tab:mode  │  Ctrl+x:shortcuts`; the status row two rows above the box is
blank within the box's columns (a scrollbar glyph at column 119 is outside it).
Header spinner, MCP counts and footer label text are decorative. Other terminal
sizes fail closed until captured and qualified;
a banner, cwd, `>`, OSC title, absent blocker, or arbitrary visible text alone is
insufficient. Unknown layouts fail closed.

**D-2 — Ignore animation only outside the qualified input state.** Settle the
region from two rows above the box through the hint row, within the box's columns.
It includes the status row and composer state; omit header and footer label text.
Do not include terminal sequence, cursor blink or elapsed spinner glyphs.
Do not strip all braille characters or whole MCP/startup lines from the screen:
those lines may carry meaningful blocking text. At least two completed positive
observations and `GrokReadyQuietPeriodMs` of the same semantic region are required.
A changed region, missing frame, blocker or unrecognized frame resets settlement.

`Starting session…` on the status row is **not ready**: the early input was queued,
the hint switched to `Ctrl+;:queue`, and the native prompt and stub user turn came
after the idle transition. The incident's final frame **is ready** under this
predicate; the old quiet gate reset on spinner redraws for all 60 seconds.

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
No other startup modal is auto-answered. The trust detector strings were measured
on 1.0.13; V-8/V-9 trust frames are synthetic and a real 1.0.41 trust capture
remains outstanding.

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

### S1 execution sequence and evidence contract (completed)

S1 completed in the Windows capture task `386a95bf`; the reviewed fixture and
provenance are committed. The ordered sequence below records the evidence method.

1. Dispatch a separate desktop task with `-Platform Windows -Worktree`, after
   checking live placement/accepting-new-work. Record its source SHA and CLI
   version. Retrieve the dated incident logs first: canonical checkout
   `server\logs\antiphon-20260927*.log`, the runner account's
   `%TEMP%\antiphon-logs\session-runner-20260927*.log`, and
   `C:\logs\antiphon\session-runner\pty-hosts\logs\98f50651c4814b019dd3db0b352514c5.log`.
   Resolve the actual canonical checkout and log overrides on that desktop;
   neither this mirror nor `scripts/logs.ps1`'s newest-file selection supplies
   dated evidence. Correlate the session/task IDs and launch/exit timestamps;
   record absent rotations without substituting a new reproduction.
2. Prepare a task-owned probe in ignored scratch space, a disposable cwd, a unique
   session ID, and a dedicated Grok home. The operator must sanction the isolated
   real CLI launch, any required login to that disposable home, the nonce submit,
   and residual provider quota/spend if the installed CLI ignores its redirect.
   This is the established `RealCliStubProxy` opt-in, not ordinary fake testing.
   Never inspect/copy OAuth contents, alter `GROK_AUTH_PATH`, or silently fall back
   to the user's primary home or live provider. A missing usable isolated login
   is a prerequisite failure; the operator provisions it via Grok's login flow.
   Read-only log collection, fixture review and CP-1/2/3 using FakeGrok require
   no real-provider spend decision. No sanction is requested or implied for a
   production restart or a production prompt.
3. Start `FakeLlmApiServer` on owned loopback and create the environment using
   `RealCliStubEnv.ForGrok(stub.BaseUrl, syntheticKey)`, then add the dedicated
   `GROK_HOME`. Both redirect variables must survive the final environment merge.
   Script title and user-turn endpoints. Source has measurements of user turns
   at both `/chat/completions` and `/responses` on different versions: require
   the **whole nonce-bearing user body**, not a title-only nonce hit, plus the
   synthetic `/api-key` oracle. Keep only method/path/body identity/timing from
   requests, never Authorization headers. A redirect miss ends the probe and
   cannot be relabeled safe because the CLI exited successfully.
4. Launch through an isolated modern `PtyAgentRunner`, subscribing to `OnData`
   **before** `StartAsync`. This existing event runs after `TerminalScreen.Feed`
   (`PtyAgentRunner.PublishDecoded`), so record each decoded chunk, a monotonic
   offset, a recorder sequence, and its contemporaneous `SnapshotScreen()`.
   Record a reset/blank initial screen and dimensions before the first chunk.
   The recorder sequence is not a runner `LastSequence`; label both identities
   correctly if a runner is also used. Verify `Backend` is modern without
   fallback and record the shipped DLL/EXE digests. Hold captured content in
   memory until step 6 redacts it; never print raw frames or persist approval
   codes/tokens, including values split across chunk boundaries. Do not enable the generic
   `ANTIPHON_PTY_AUDIT`: its `meta.txt` writes the launch environment, and its
   snapshots use `SnapshotText` (raw history), so it is unsuitable for this safe
   rendered-frame fixture. Do not infer original chunks by diffing ring-buffer
   tails. A task-owned probe may need one isolated, leased build; commission and
   report that S1 evidence command separately from the three ordinary CP rows.
5. Capture fresh trust, startup and idle runs, and a separate empty-home sign-in
   run without approving its device code. Answer only the recognized current
   trust choice. To test disputed early input, submit the harmless whole nonce
   while the captured startup frame is still present, once, using LF/bracketed
   paste/separate Enter. This is an explicitly commissioned measurement, never
   a probe added to production readiness. Use the native ACP stream from that
   same session and `GrokTranscriptTailer`/normalizer to retain the complete
   normalized `UserPrompt` and native source identity. Record submission, native
   prompt timestamp, first observed receipt, stub user-turn receipt and first
   ordinary-idle transition on one correlated timeline. Require the prompt
   receipt **and actual user-turn request** before that idle transition to call
   startup usable; ambiguity or input merely queued until idle fails qualification.
   The existing B-server canary waits for readiness before submission, so running
   it unchanged cannot prove this property. Kill/await only the probe's children,
   stop/await its recorder, tailer and stub, and retain sanitized evidence.
6. Sanitize locally with length/cell-width preserving replacements, then replay
   all ordered chunks through `TerminalScreen` and compare against the captured
   checkpoints. Preserve the original capture metadata and replacement manifest;
   do not derive the expected screens by replay in the test itself. Commit only
   sanitized fixture/provenance. Review D-1/D-2 against the actual composer bounds,
   enabled hint/footer and relevant identity. If no positive spinner layout is
   observed, V-1/V-6/V-17 have no qualified input: return to investigation rather
   than inventing a decorative spinner. Unknown/startup stays negative meanwhile.

**S1 capture result (Code task `386a95bf`, 2026-09-30; reviewed CLEAN by Final Review `643ebcc5`).** Steps 1-6
ran on the desktop against grok 1.0.41, the incident's version. The committed
`startup-frames.json`/`provenance.md` hold two replayable reproductions, a screen-only sign-in
capture, and the incident's full raw `.ansi.log` stream as partial evidence (found at
`C:\logs\antiphon\session-runner\<id:N>.ansi.log`; no chunk timing). Offline replay matches every
checkpoint.

A positive spinner layout **was** observed and receipted. An empty composer box, the exact idle
hint `Shift+Tab:mode  │  Ctrl+x:shortcuts` and a blank status row, while an ASCII header spinner
and MCP counter redraw, accepted a whole nonce prompt immediately.

The disputed `Starting session…` state **queues** input (`#1` row, `Ctrl+;:queue`) until startup
ends, so it is not qualified. Working frames reuse the empty box but change the hint and status row.

Other findings:
- The trust dialog was not reproducible with a disposable API-key home.
- grok imports two MCP servers from outside `GROK_HOME`.
- grok 1.0.41 drops the LF of a bracketed-paste body.

See `provenance.md` for bounds, receipts and missing evidence. D-1/D-2 above
record the reviewed predicate.

Fixture schema is one JSON object with `schemaVersion: 1` and named `captures`.
Each capture records `captureId`, incident/reproduction origin, CLI version,
source SHA, OS/backend/no-fallback evidence, session ID, UTC/monotonic clock
correlation, cols/rows, known initial screen, ordered `chunks` (index, elapsed
offset, exact sanitized text), and `checkpoints` (after-chunk index, independently
captured rendered text, expected reason, qualified composer row/column bounds
when applicable). `probes` link the same capture/session to the exact nonce body,
input writes/time, normalized/native prompt identity and sequence, stub receipt
and transition time. Store digests of the sanitized JSON and native assets in
`provenance.md`; document every replacement and any missing evidence there.
Include a corpus of negative captured states and explicitly labeled synthetic
perturbations (bare marker, missing enabled footer, nonempty composer, stale raw
history); synthetic cases never establish a supported layout. Keep real sign-in
codes/tokens out of both chunks and screenshots. V-1 checks fixture chronology,
indices/dimensions, digests and all recorded screen checkpoints before asserting
classifier identity. A fixture error is setup failure, never a positive-control red.

FakeGrok's native mode replays a documented **synthetic repetition of captured
positive redraws**; it does not claim that loop is original incident chronology.
It continues until input starts, beyond the adapter maximum in PC-17, and its
writer is stopped/awaited before input echo or ACP output. Verify rendered shape
after Windows ConPTY: writing a captured output stream into a second terminal
does not guarantee byte-identical output. Ordinary FakeGrok paints the qualified
ready layout and retains its existing marker outside the semantic input region.

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

## Exact file footprint and dispatch dependencies

This TestDesign commit changes **only this plan**:
`docs/superpowers/plans/2026-09-30-card-0778-grok-startup-spinner-readiness-plan.md`.
The complete planned S1-S3 footprint is the following closed list, plus this plan
for the evidence amendment. A new helper stays inside its named test file; any
additional tracked file needs a recorded footprint amendment before Code proceeds.

| Slice | Exact repository path | Action |
|---|---|---|
| S1 | `tests/Antiphon.Tests/Agents/Fixtures/card0778/startup-frames.json` | Add sanitized capture corpus |
| S1 | `tests/Antiphon.Tests/Agents/Fixtures/card0778/provenance.md` | Add capture/receipt/predicate record |
| S2 | `src/Antiphon.Agents.Pty/GrokStartupReadiness.cs` | Add Grok-only classification/settlement/wait |
| S2 | `server/Infrastructure/Agents/SessionRunner/GrokStartupCaptureStore.cs` | Add bounded capture store |
| S2 | `server/Infrastructure/Agents/SessionRunner/RunnerGrokAdapter.cs` | Replace readiness/trust path; optional clock seam |
| S2 | `server/Application/Settings/AgentRegistrySettings.cs` | Add capture settings; timing documentation |
| S2 | `tests/Antiphon.Tests/Agents/GrokStartupReadinessTests.cs` | Add V-1..V-5 and shared fixture reader |
| S2 | `tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTests.cs` | Add V-6..V-12 and scripted client |
| S2 | `tests/Antiphon.Tests/Agents/GrokStartupCaptureStoreTests.cs` | Add V-13/V-14 |
| S2 | `tests/Antiphon.Tests/Agents/RunnerGrokAdapterTrustPromptTests.cs` | Captured ready screen; dispose owned adapters |
| S2 | `tests/Antiphon.Tests/Agents/RunnerGrokAdapterSignInPromptTests.cs` | Captured ready screen; dispose owned adapters |
| S3 | `tests/Antiphon.Tests/Application/GrokStartupReadyOrderingTests.cs` | Add V-15/V-16 and local harness |
| S3 | `tests/Antiphon.Tests/Application/SessionMessageQueueGrokPtyIntegrationTests.cs` | Add V-17 and local launch harness |
| S3 | `src/Antiphon.FakeGrok/Program.cs` | Ready display and opt-in replay lifetime |
| S3 | `tests/Antiphon.Tests/slow-tests-allowlist.txt` | Register the new Slow ordering class |
| S3 | `docs/agent-kinds.md` | Grok readiness semantics |
| S3 | `docs/session-runtime-invariants.md` | Startup versus delivery invariant |
| S3 | `docs/logs.md` | Bounded startup capture retrieval |

No `.csproj` change is needed: `Antiphon.Tests.csproj:32` already copies
`Agents\Fixtures\**`. Resolve from `AppContext.BaseDirectory/Agents/Fixtures/card0778`;
pass that staged path to FakeGrok's opt-in replay and keep its ordinary captured
display literal in `Program.cs`. The Pty helper cannot reference the server's
`SessionRunnerSnapshotDto`; the adapter supplies its screen/raw/sequence/time in
a Pty-owned value or callback, using one DTO observation. No shared protocol or
`RunnerTerminalSession` change is required.

CARD-0849's supplied scope overlaps only at broad `tests`/`docs` labels; this list
has no deploy, compose or `scripts/c590-remote.sh` writes. That does **not** establish
collision freedom: the orchestrator must compare CARD-0849's concrete paths, plus
CARD-0719/CARD-0846's final footprints, with this list at dispatch. Their live branch
states were not inspected here. Native receipt work reads the same implementation
SHA in a separate Windows worktree; do not let that task independently rewrite the
same tests while Linux Code owns them. Preserve all accepted landed changes through
normal landing; this task does not rebase/merge master.

## Verification design

This is the statically audited closed roster. S1 must resolve its capture before
the product part becomes executable. Every method below
is one nonparameterized TUnit execution; internal matrices are assertions, not extra
test counts. TestDesign may revise the roster only by amending this plan and its
PC/cost/checkpoint mappings together.

### Inspection

| Bodies inspected at the audit SHA | Boundary and consequence |
|---|---|
| `RunnerGrokAdapter.WaitForReadyAsync`, private trust helper, `RunnerTerminalSession.WaitForQuietAfterVisibleAsync`, `AgentSessionService.WaitForReadyOrThrowAsync` | V-6..V-12/V-15/V-16 must cross the actual adapter and launch cleanup. Sign-in currently logs screen/home; replacing that log is part of D-5. |
| All nine existing R classes below, including argument attributes and native skips | Counts below are source-derived. Existing trust/sign-in ready strings are insufficient for D-1. R-1's turn tests skip readiness entirely; preserve their turn assertions. |
| `GrokRulesReadyOrderingTests`, `BridgeQueueHarness` registration/seeding/helpers, `TestDbFixture` | Existing ordering uses `FakeAgentProtocolAdapter.ReadyHold`, cancels the ordinary message on its happy arm, and cannot supply V-15. The harness also pre-registers a fake runtime adapter; replacing only its factory is insufficient. Use a local harness with the real adapter registered for the same session/generation and an isolated cloned DB. |
| `SessionMessageQueueGrokPtyIntegrationTests`, `GrokDelegateEndToEndTests`, `DirectSessionRunnerClient` | Four existing native queue methods manually start FakeGrok and seed Running. V-17 must use `AgentSessionService.LaunchInteractiveAsync`, real `RunnerGrokAdapter` and `AgentSessionRuntime.SyncTranscriptAsync`; it cannot copy their readiness-marker shortcut or seed a confirming UserPrompt. The direct client's capabilities say modern by construction, so also require actual backend/no-fallback evidence. |
| `RealCliStubEnv.ForGrok`, Grok canary bodies, `GrokTranscriptTailer`, `PtyAgentRunner.PublishDecoded`, `TerminalScreen.Feed`, `PtySessionAudit` | S1 needs a pre-start OnData recorder, a native receipt and full user-turn stub oracle. Generic audit and the B-server canary cannot supply this capture unchanged. |
| `Antiphon.FakeGrok.Program` startup/input path, test project staging, Slow allowlist | V-17 writer lifetime must end before prompt processing; ordinary display keeps marker compatibility. Native binaries are already staged by the test graph. |
| `run-checkpoint.ps1`, `build-slot.ps1`, broker acquire path, testing owner | Filters are passed literally; same-PID/start-time acquisition is idempotent. No offline driver harness or real test was executed in this audit. |

### Compile-free census

Counted `[Test]` declarations and each method's literal `[Arguments]` attributes
from the real files at `8864419001bc99363a5476ca55bbea9d08f04cbd` with a Node
source scan, then checked the bodies. These classes have no inherited tests,
class argument expansion, matrices, repeats or data-source expansion. No
`--list-tests`, compiler, build, test host or Postgres was started.

| Existing class (file under `tests/Antiphon.Tests`) | Methods | Expanded cases | Source expansion |
|---|---:|---:|---|
| `Agents/RunnerGrokAdapterTrustPromptTests.cs` | 4 | 4 | Four plain tests |
| `Agents/RunnerGrokAdapterSignInPromptTests.cs` | 3 | 3 | Three plain tests |
| `Agents/GrokAdapterTests.cs` | 2 | 2 | Two plain tests |
| `Agents/RunnerGrokAdapterTurnCompleteTests.cs` | 7 | 7 | Seven plain tests |
| `Agents/RunnerCodexAdapterReadyTests.cs` | 10 | 12 | `Legacy_boot_threshold_never_bypasses_the_positive_gate`: arguments 0, 10000, 250; nine plain methods |
| `Application/GrokRulesReadyOrderingTests.cs` | 1 | 2 | `Receipt_and_refresh_are_committed_while_readiness_holds_all_input`: false, true |
| `Application/GrokRulesQueueBarrierTests.cs` | 1 | 28 | `Closed_rules_barrier_holds_ordinary_input_on_each_delivery_entry_point`: four entry points times seven origins |
| `Application/SessionMessageQueueGrokPtyIntegrationTests.cs` | 4 | 4 | Four plain methods, Windows guards |
| `Application/GrokDelegateEndToEndTests.cs` | 5 | 5 | Five plain methods, Windows guards |
| **Existing total** | **37** | **67** | Static count, not passing executions |

CP-1 has 26 existing methods / 28 cases, plus 5 classifier, 7 adapter and 2 store
methods: **40 methods / 42 cases**. CP-2 has 2 existing methods / 30 cases plus 2:
**4 methods / 32 cases**. CP-3 has 9 existing methods / 9 cases plus 1:
**10 methods / 10 cases**. Total planned: **54 methods / 84 cases**. None of the
four new classes or V-17 exists yet. Internal subcases below do not increase the
17 new methods or these floors. Fresh execution TRX must later confirm exactly
these per-class expansions, zero skips and no unintended suffix-matched class.

### Delivery inventory

Durable identity is ordinary queued-message ID -> `(SessionId, AcceptedStartedAt)`
-> captured transcript floor -> complete normalized nonce UserPrompt -> persisted
delivery verdict. Rules add refresh-message ID, rules generation and SHA-256 to
the ACK join; a rules-file receipt alone never confirms ordinary work.

| Producer -> destination | Persistence / recovery boundary | Observable receipt and coverage |
|---|---|---|
| Launch reservation -> runner/adapter readiness | Accepted generation and rules-file receipt are committed before the wait. No new queue is introduced. | V-15 keeps the real launch pending on unqualified frames; V-16 fails/cleans only its generation, preserving ordinary Pending/zero attempts. |
| Rules refresh queue -> Grok composer | Existing refresh row waits for readiness, then matching ACK gates ordinary work. | V-15 verifies wrong ID/generation/hash ACKs do not release work, followed by the matching ACK and whole ordinary UserPrompt. R-2 covers all four delivery entry points and seven origins. |
| Ordinary queue -> runtime -> runner input | Existing baseline and attempt persist before delivery. Starting/busy recipients hold; an eligible recipient uses the same queue. | V-15 includes held-at-startup, busy-after-ready and already-eligible subcases within its one method. Confirm full body, sequence greater than floor, Sent/Delivered and exactly one submit. V-17 repeats the actual PTY/tailer/persistence path. |
| Native ACP -> tailer/normalizer -> runtime -> TranscriptEntries | Runtime catch-up persists native rows; screen redraw and Sent alone are insufficient. | V-17 starts with no prompt receipt, syncs the real tailer, then asserts exactly one full nonce UserPrompt after the baseline. S1 uses the real CLI; FakeGrok does not prove CLI input capability. |
| Failed readiness -> capture -> launch failure cleanup | Capture uses the already observed frame before adapter disposal; I/O failure cannot turn launch success or skip cleanup. | V-12/V-16 assert event order, correct session/frame metadata, no input, owned-generation stop and Pending work. |

There is no new durable enqueue/recovery algorithm in this card. Crash before
launch enqueue, crash between rules receipt and refresh enqueue, receipt ingestion
reconnect, and ordinary enqueue-transaction failure remain existing mechanisms;
their full crash matrices are excluded because none of those production paths is
changed. R-3 retains the existing swallowed-Enter/Pending/Enter-only recovery.
Do not claim that these exclusions were newly crash-qualified. A change to any
such boundary requires a plan/footprint/PC/checkpoint amendment. V-15's scripted
runner proves application ordering/persistence, V-17's FakeGrok proves native
transport, and only S1 proves the recorded real CLI layout.

### Coverage and new test roster

| ID | Class.Method (new unless marked regression) | Outcome to assert |
|---|---|---|
| V-1 | `GrokStartupReadinessTests.Captured_idle_frames_stay_ready_while_spinner_redraws` | Replay `idle-…21944bcbd833` chunks 22–48 (3335–7954 ms) through `TerminalScreen`, checking the recorded checkpoints and line-ending-independent content digests in `provenance.md`; the qualified input-region identity remains equal while the header spinner/sequence change. |
| V-2 | `GrokStartupReadinessTests.Unknown_or_blocked_current_frames_never_become_ready` | Replay `startup-…de8210c2232b` starting/queued checkpoints and `idle-…21944bcbd833` working and real nonempty composer checkpoints 49–55. Add a labelled synthetic other-size frame; blank/ANSI-only, bare prompt, partial composer and synthetic trust remain negative. Do not use `syn-nonempty-composer`, whose missing right border/trailing spaces cannot come from `TerminalScreen`. |
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
| V-15 | `GrokStartupReadyOrderingTests.Work_waits_for_ready_rules_ack_and_complete_prompt` | Production launch holds queued work during unqualified redraws, then positive gate enables rules refresh, matching ACK, ordinary work and whole persisted **single-line** nonce `UserPrompt` in order. A scripted runner provides ACP/normalized data; this is an integration receipt, not live CLI proof. |
| V-16 | `GrokStartupReadyOrderingTests.Unready_launch_cleans_up_and_keeps_work_pending` | All-budget unready spinner yields Failed and owned-generation cleanup, zero prompt/input, ordinary row still Pending/zero attempts, no fabricated transcript, diagnostic frame retained before disposal. |
| V-17 | `SessionMessageQueueGrokPtyIntegrationTests.Captured_spinner_reaches_ready_and_complete_user_prompt` | Windows modern ConPTY, isolated runner client, actual Grok adapter and launch gate: captured redraw stream continues past the settle interval, launch becomes ready, queued **single-line** nonce arrives whole via native tailer/runtime in exactly one UserPrompt. Assert pre-ready hold, sequence advance, no duplicate submit and child cleanup. |
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
Mark `GrokStartupReadyOrderingTests` Integration/Slow with MessageQueue
serialization and register its fully qualified name in the Slow allowlist.
The three new unit classes are Unit, without a process-spawn limiter: their
clients are scripted in-process objects. Do not launch children from those fakes.
No `Program` fixture may use production port 17204. Controlled-clock tests advance
timers as well as elapsed time, with an independent short real-time guard and
release/cancel/await cleanup for every held TaskCompletionSource.
V-6's fake must also answer legacy raw/buffer reads with valid advancing-sequence
data: PC-6 must fail the ready assertion, not throw a fake's NotSupportedException.
For V-17, the startup deadline is shorter than the test's outer guard; capture the
completed launch result/exception and assert successful readiness plus receipt.
PC-17 must reach that assertion after the legacy gate fails, not hang the test host.

### Deterministic subcases and outcome assertions

These complete the V roster without adding methods or argument expansions.

- V-2 includes fixture identity without an enabled footer, nonempty/partial input,
  quiet `Starting session...`, working text, unknown choice modal, ANSI-only text,
  and meaningful blocking text on a row that also carries a spinner/MCP count.
  Test classification and reason, not just absence of success.
- V-4 uses a 1000 ms settle: positive A at 0 and 999 remains pending; at 1000
  it may settle. In separate timelines change A to B, then insert a blocker,
  missing frame and Unknown frame; each restarts the entire interval. Observe
  before and exactly at the new threshold. V-5 covers settle 0 and negative
  settle, two observations at the same timestamp, and a held second observation.
  Assertions at intermediate gates are essential: a final true alone cannot
  detect early readiness.
- V-6 records snapshot decisions and legacy buffer calls separately. Return valid
  but contradictory data on any extra read so a duplicate snapshot cannot pass
  unnoticed. Also assert zero startup writes, resizes, kills and prompt submits.
  Give the legacy quiet mutation a short finite real-clock maximum; fake elapsed
  time alone cannot advance the unchanged legacy helper's wall clock.
- V-7 captures both the launch-block kind/remedy and a zero-write assertion when
  current sign-in/trust overlap. V-8 has question-only, choice-only, stale raw
  trust over a current idle/unknown screen, repeat trust and trust recurrence;
  each valid trust wait writes precisely `y`, never Enter or a prompt. V-9
  covers trust-clear into blank/sign-in/positive, zero/negative trust sub-budget,
  and a trust frame still present at its positive sub-budget boundary.
- V-10 covers max -1/0, settle longer than max, minimum age longer than max,
  already-old process, UTC jumps with monotonic time unchanged, a completed
  snapshot arriving at/after max, a snapshot ignoring cancellation, and trust
  first seen just before max. A modal arriving during minimum age must invalidate
  prior positive frames. For a held I/O operation, schedule its forced completion
  at max + one poll as cleanup; compare the wait's captured completion timestamp
  to max. A missing bound thus fails an elapsed-time **assertion after completion**,
  not an outer timeout. A late completed read must be released so the post-await
  expiry branch is actually reached; do not accidentally kill it in the fake.
- V-11 covers exit before entry/between frames and cancellation before entry,
  during read/delay/trust. Assert the caller's cancellation escapes, then snapshot
  all side-effect counts and release held work; no subsequent poll/input is allowed.
  Only the readiness helper gets a controlled timer-capable clock. The real queue
  graph in V-15/V-16 keeps `TimeProvider.System` or the existing scaled clock.
- V-12 covers Deadline, missing first snapshot and a thrown snapshot after one
  good observation. Use distinct frame sequences and content to detect a stale
  frame; distinguish last classification from terminal outcome. Count all runner
  reads after failure selection. A regular file placed where the capture directory
  should be is the portable unwritable-path case (chmod alone fails under root).
  Assert false plus one attempted callback and metadata-only failure logging;
  V-16 supplies actual launch cleanup. Retention I/O failure uses an injected I/O
  failure or an owned locked file where supported, not host directory permissions.
- V-13 uses lengths 8191/8192/8193, surrogate-pair edges, all control characters,
  large non-ASCII metadata, null frame and a synthetic sign-in secret also present
  in later raw history. Assert decoded content limits **and actual UTF-8 file
  length**, valid escaping and truncation metadata, no sentinel in capture/log.
  D-5's 128 KiB file bound can be enforced by construction: two capped 8192-unit
  fields expanded at most six ASCII bytes per unit, <=1024 metadata bytes and
  <=4096 fixed-format bytes total <=103424 bytes. Assert that format budget;
  do not invent an unreachable extra truncation branch and call it mutation-tested.
  If Code uses a separate reachable file-budget branch, add its own PC.
- V-14 writes actual files with distinct ordered metadata and owns its temp root.
  Keep values -1/0/1/10/100/101 cover clamp edges. Preserve unrelated `.txt`,
  another store's capture and an incomplete own temporary file. Verify survivor
  names/counts and no unrelated file deletion, not merely option values.
- V-15 holds explicit startup/ready/rules/busy gates and records write order.
  Wrong rules ACK identity and head-only/tail-only or pre-baseline ordinary prompt
  data must not confirm delivery; then release full matching data and assert
  Sent/Delivered. Its scripted runner feeds `AgentSessionRuntime.SyncTranscriptAsync`
  rather than inserting the expected final UserPrompt directly into PostgreSQL.
  Do not read `h.Adapter`/`h.Runner` from a BridgeQueueHarness whose fake registrations
  were replaced: retain the actual scripted runner/adapter separately.
- V-17 pins a short adapter maximum inside a longer outer guard and retains a
  task/result for the actual launch. Capture a completed launch exception and
  assert `launchError.ShouldBeNull()` **before** any receipt wait. PC-17 must fail
  that assertion after readiness returns false. Baseline then requires native
  tailer/runtime receipt, full nonce equality, sequence above floor, one UserPrompt,
  one submit and a persisted Delivered verdict. A continuously redrawing fake must
  not stop its animation merely because the test would like launch to succeed.

### Guard inventory

The following control table is also the guard inventory: `G-n -> PC-n` is a
one-to-one mapping, and its Guard column names the decision and boundary.
There are **45 guards, 45 mapped controls, 0 missing and 0 duplicate PC mappings**.
This expands the Plan's 19 cycles: independently bypassable reset, timing,
current-frame input and diagnostic guards cannot share one mutation and be
reported as individually controlled. All 45 use the same 17 V methods. R-1/R-2/R-3
preserve existing delivery/rules/cleanup/turn behavior; they do not authorize
mutations of unchanged shared queue, rules or Codex production code on this card.
S1's evidence/provenance and clean-SHA/platform checks are commissioning/receipt
checks, not new product guards; no compiling product mutation proves those checks.

### Positive controls

Execute after ordinary Review and confirmed land, in the commissioned SourceLanding
Mutation workspace. For every PC, use only its named method, fresh baseline/red/
restored-green outputs and TRX, with one executed test per phase. The exact filter
is `/*/*/<Class>*/<Method>` using the literal Class.Method in the linked V row above;
there are no argument suffixes. Require baseline/green exit 0 and red exit 1 with
the named outcome assertion failing. Compile failures, fixture-load failures,
zero tests and a wedged process are not red evidence. Restore exact source and
refresh timestamps before rebuilding. Mutations share production files and run
serially; do not claim independent batching savings.

| Guard -> PC | Method | Guard / compiling production mutation | Required red outcome assertion |
|---|---|---|---|
| G-1 -> PC-1 | V-1 | D-2 decoration independence: include the recorded spinner in semantic identity. | `identity.ShouldBe(firstIdentity)` across captured positive frames. |
| G-2 -> PC-2 | V-2 | D-1 positive structure: admit bare `>` without qualified dashboard/footer. | `barePrompt.IsReady.ShouldBeFalse()`. |
| G-3 -> PC-3 | V-3 | D-1 current-frame classification: classify raw history instead. | `actual.Reason.ShouldBe(expectedCurrentReason)`. |
| G-4 -> PC-4 | V-4 | D-2 changed-input reset: retain candidate start after identity changes. | `readyBeforeNewThreshold.ShouldBeFalse()`. |
| G-5 -> PC-5 | V-5 | D-2 two observations: remove the count floor. | `firstZeroSettleObservation.IsReady.ShouldBeFalse()`. |
| G-6 -> PC-6 | V-6 | D-2 adapter wiring: restore legacy sequence-quiet wait. | Await its bounded result, then `ready.ShouldBeTrue()`; false is the red, not a test timeout. |
| G-7 -> PC-7 | V-7 | D-4 prompt sign-in block: defer sign-in classification until positive readiness. | `launchBlock.ShouldNotBeNull()` and Kind equals ProviderSignInRequired after the bounded wait returns. |
| G-8 -> PC-8 | V-8 | D-4 trust once: remove the trust-written latch. | `trustWrites.Count.ShouldBe(1)` on repeated current trust. |
| G-9 -> PC-9 | V-9 | D-4 post-trust positive gate: return true on trust disappearance. | `blankTransitionReady.ShouldBeFalse()`. |
| G-10 -> PC-10 | V-10 | D-3 post-read expiry: admit a completed positive read after max. | `lateReadReady.ShouldBeFalse()` with the late read explicitly released. |
| G-11 -> PC-11 | V-11 | D-3 process exit: omit exit rejection between observations. | `exitedReady.ShouldBeFalse()`. |
| G-12 -> PC-12 | V-12 | D-5 failure callback: remove adapter wiring only. | `captureCount.ShouldBe(1)` before readiness returns false. |
| G-13 -> PC-13 | V-13 | D-5 sign-in suppression: persist screen/raw after sign-in seen. | `persistedText.ShouldNotContain(secretSentinel)`. |
| G-14 -> PC-14 | V-14 | D-5 bounded retention: omit pruning. | `ownedCompletedFiles.Count.ShouldBe(keep)` after keep+1 captures. |
| G-15 -> PC-15 | V-15 | D-6 readiness holds queue: adapter returns true immediately. | `writesDuringHeldStartup.ShouldBeEmpty()` after launch is allowed to progress to the recorded first-write gate. |
| G-16 -> PC-16 | V-16 | D-3/D-6 failed admission: terminal Deadline returns true. | `session.Status.ShouldBe(Failed)` after launch settles; script lets any erroneously admitted work complete. |
| G-17 -> PC-17 | V-17 | D-2/D-6 native gate: restore legacy quiet wait for the real adapter. | `launchError.ShouldBeNull()` on the completed launch; do not wait for a nonexistent receipt to go red. |
| G-18 -> PC-18 | V-4 | D-2 negative/missing reset: preserve candidate across an invalid observation. | `readyAfterNegativeBeforeThreshold.ShouldBeFalse()`. |
| G-19 -> PC-19 | V-5 | D-2 elapsed settlement: ignore elapsed time after two observations. | `secondSameTimestamp.IsReady.ShouldBeFalse()` at positive settle. |
| G-20 -> PC-20 | V-6 | D-1 coherent snapshot: fetch a second DTO for raw or screen. | `snapshotReads.ShouldBe(completedDecisions)` and no split reads. |
| G-21 -> PC-21 | V-7 | D-4 sign-in precedes trust: move trust action ahead of sign-in classification. | `writes.ShouldBeEmpty()` on the overlap frame. |
| G-22 -> PC-22 | V-8 | D-4 affirmative choice required: authorize trust from question alone. | `questionOnlyWrites.ShouldBeEmpty()`. |
| G-23 -> PC-23 | V-8 | D-4 current trust only: authorize `y` from raw-history trust. | `staleTrustWrites.ShouldBeEmpty()`. |
| G-24 -> PC-24 | V-9 | D-4 trust sub-budget: ignore its positive expiry while overall time remains. | `trustCompletionElapsed.ShouldBeLessThanOrEqualTo(trustBudget)` after the scripted later release. |
| G-25 -> PC-25 | V-10 | D-3 minimum process age: ignore remaining minimum age. | `readyBeforeMinimumAge.ShouldBeFalse()`. |
| G-26 -> PC-26 | V-10 | D-3 observe during minimum age: sleep to floor and accept stale positive state. | `readyWithFloorModal.ShouldBeFalse()`. |
| G-27 -> PC-27 | V-10 | D-3 nonpositive maximum: treat zero/negative max as immediate success. | `nonpositiveMaxReady.ShouldBeFalse()` and zero writes. |
| G-28 -> PC-28 | V-10 | D-3 bounded snapshot await: directly await the cancellation-ignoring read. | `completionElapsed.ShouldBeLessThanOrEqualTo(max)` after forced I/O release at max+poll. |
| G-29 -> PC-29 | V-10 | D-3 one budget includes trust: grant a fresh maximum when trust starts. | `trustCompletionElapsed.ShouldBeLessThanOrEqualTo(originalMax)` on late trust. |
| G-30 -> PC-30 | V-11 | D-3 caller cancellation: swallow OperationCanceledException and return false. | `Should.ThrowAsync<OperationCanceledException>(wait)`; a false return fails it. |
| G-31 -> PC-31 | V-12 | D-3 snapshot failure: reuse last positive as success on read exception. | `snapshotFailureReady.ShouldBeFalse()`. |
| G-32 -> PC-32 | V-6 | D-3/D-4 no probe input: write a harmless key before returning true. | `startupWrites.ShouldBeEmpty()`. |
| G-33 -> PC-33 | V-13 | D-5 screen bound before escaping: omit the screen source cap. | `decodedScreen.Length.ShouldBeLessThanOrEqualTo(8192)`. |
| G-34 -> PC-34 | V-13 | D-5 raw-tail bound before escaping: omit the raw source cap. | `decodedRaw.Length.ShouldBeLessThanOrEqualTo(8192)` and expected last-tail content. |
| G-35 -> PC-35 | V-13 | D-5 content escaping: write control characters literally. | `storedContentHasLiteralControls.ShouldBeFalse()` (excluding fixed format delimiters). |
| G-36 -> PC-36 | V-13 | D-5 metadata byte budget: omit UTF-8 metadata truncation. | `metadataByteLength.ShouldBeLessThanOrEqualTo(1024)`. |
| G-37 -> PC-37 | V-14 | D-5 owned completed files only: prune all `.txt` files. | `File.Exists(unrelatedSentinel).ShouldBeTrue()`. |
| G-38 -> PC-38 | V-14 | D-5 minimum keep: permit keep 0 instead of clamping to 1. | `ownedCompletedFiles.Count.ShouldBe(1)` for configured 0/-1. |
| G-39 -> PC-39 | V-14 | D-5 maximum keep: permit configured 101. | `ownedCompletedFiles.Count.ShouldBe(100)` after 101 captures. |
| G-40 -> PC-40 | V-12 | D-5 diagnostic failure preserves failure: return true on capture I/O failure. | `unwritableCaptureReady.ShouldBeFalse()`. |
| G-41 -> PC-41 | V-12 | D-5 log allowlist: append raw screen/home/exception text to the failure log. | `log.ShouldNotContain(secretOrPathSentinel)` on every failure arm. |
| G-42 -> PC-42 | V-10 | D-3 monotonic elapsed time: use UtcNow subtraction for deadline/settle. | `readyAfterUtcJumpWithoutElapsed.ShouldBeFalse()`; later monotonic threshold still succeeds. |
| G-43 -> PC-43 | V-12 | D-5 actual last-frame identity: capture the first frame instead of the last. | `capture.Sequence.ShouldBe(lastObservedSequence)` and its session/reason/time match. |
| G-44 -> PC-44 | V-12 | D-5 no post-failure runner read: fetch another frame for the capture. | `readsAfterFailureDecision.ShouldBe(0)`; the extra fake read completes with distinct data. |
| G-45 -> PC-45 | V-2 | D-2 preserve meaningful startup/MCP text: strip the whole spinner/status row before classification. | `blockedStatusFrame.IsReady.ShouldBeFalse()` despite intact composer chrome. |

Use the assertion expressions as named outcomes (retain equivalent explicit labels
in Shouldly messages when Code chooses member names). A mutation must reach that
assertion with valid fixtures and completed harness work. Bind each mutation to
the final production guard; if another equivalent guard masks it, remove the
complete decision for that PC or amend the inventory, never report a surviving
mutation as a pass. Each row is one serial baseline/red/restored-green cycle;
PC-4 and PC-18 replace the former two reset variants, PC-13/33/34 split suppression
and content caps. No hidden variants are included in the cost. Integration/native
controls mutate production readiness, not fixture bytes or test expectations.

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

| Task placement | Commissioned work | Completion receipt |
|---|---|---|
| Desktop, `-Platform Windows` | S1 capture/provenance and reviewed D-1/D-2 predicate, **before** S2 product work | Fixture commit, source/CLI/backend identities, complete native nonce plus stub user-turn receipt, actual region bounds and classification; missing evidence keeps S1 open |
| Linux Code on server2 | After S1 commit is available: S2/S3 edits, then CP-1 and CP-2 serially | Same committed implementation SHA; CP-1 42 and CP-2 32 executed, zero failures/skips; test Postgres for CP-2 |
| Separate desktop task, `-Platform Windows` | After Linux implementation commit is pushed: CP-3, read-only qualification of that **same SHA** | 10 executed, zero failures/skips; test Postgres, staged assets, actual modern backend for V-17 |

The Linux Code task returns its two receipts and explicitly pending CP-3; it must
not say the complete Code verification passed. The orchestrator commissions the
Windows qualification and joins all three receipts before ordinary Review. An
all-Windows Code task may run all three rows in table order. Windows fixes require
a new pushed implementation SHA and rerun of all three rows at that SHA; an old
Linux receipt and a newer Windows receipt do not combine into a clean acceptance.
S1's capture SHA may predate the implementation (its provenance is retained);
the three ordinary qualification receipts must match one another.

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

TestDesign inspected, but did not execute, those seams. `build-slot.ps1` invokes
the row `.ps1` with `&`; `run-checkpoint.ps1` splits `-Expect` on commas and passes
Filter through `ProcessStartInfo.ArgumentList`; `BuildSlotBroker` reuses the
same holder PID/start time. Keep the inner command in the same PowerShell process.
The explicit CP-2/CP-3 commands for their respective parameterless row scripts
are below (Markdown table `\|` escapes are **not** shell filter characters):

```powershell
# .antiphon/c778-cp2.ps1 -- run from the repository root
$c778PriorParallel = $env:TUNIT_MAX_PARALLEL_TESTS
try {
    $env:TUNIT_MAX_PARALLEL_TESTS = '1'
    & ./scripts/run-checkpoint.ps1 -Name CP-2 -Project tests/Antiphon.Tests -OutputPath bin-c778-ordering/ -Filter '/*/*/(GrokStartupReadyOrderingTests*)|(GrokRulesReadyOrderingTests*)|(GrokRulesQueueBarrierTests*)/*' -Expect GrokStartupReadyOrderingTests,GrokRulesReadyOrderingTests,GrokRulesQueueBarrierTests -MinExecuted 32 -ResultsRoot .antiphon/c778-cp2
    $c778Exit = $LASTEXITCODE
} finally {
    $env:TUNIT_MAX_PARALLEL_TESTS = $c778PriorParallel
}
exit $c778Exit
```

Invoke `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c778-cp2 -- ./.antiphon/c778-cp2.ps1`.

```powershell
# .antiphon/c778-cp3.ps1 -- Windows only, from the same implementation SHA
if (-not $IsWindows) { throw 'CP-3 requires Windows; Linux skips are not evidence.' }
$c778PriorParallel = $env:TUNIT_MAX_PARALLEL_TESTS
try {
    $env:TUNIT_MAX_PARALLEL_TESTS = '1'
    & ./scripts/run-checkpoint.ps1 -Name CP-3 -Project tests/Antiphon.Tests -OutputPath bin-c778-native/ -Filter '/*/*/(SessionMessageQueueGrokPtyIntegrationTests*)|(GrokDelegateEndToEndTests*)/*' -Expect SessionMessageQueueGrokPtyIntegrationTests,GrokDelegateEndToEndTests -MinExecuted 10 -ResultsRoot .antiphon/c778-cp3
    $c778Exit = $LASTEXITCODE
} finally {
    $env:TUNIT_MAX_PARALLEL_TESTS = $c778PriorParallel
}
exit $c778Exit
```

Invoke `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c778-cp3 -- ./.antiphon/c778-cp3.ps1`.
For any rerun, change only the fresh results-root suffix and record the rerun count.
CP-3's own isolated build must stage `fakegrok/fakegrok.exe`,
`fakeclaude/fakeclaude.exe`, the PtyHost executable/dependencies, and
`conpty/win-x64/conpty.dll` plus sibling `OpenConsole.exe`. Assert V-17's actual
host decision is ModernConPty with no fallback; the requested backend and direct
client's hard-coded capability response cannot certify that. Existing R-3 cases
keep their declared inbox/modern choices. Missing assets or non-Windows guards
can currently produce SkipTestException in those classes: **any skip fails this
plan's receipt**, even if the test process exits zero. No
`ANTIPHON_REAL_CLI_STUB_TESTS` or real-provider headed opt-in is needed for CP-3;
its children are the built fakes.

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

### Out of scope

No production restart/deploy, provider/MCP configuration repair, shared quiet
helper/Codex change, new kill/retry policy, shared queue recovery change, live
broker traffic, or historical-delivery rewrite is authorized. S1 may establish
that MCP initialization itself is blocking; that result is evidence for separate
investigation, not authority to accept the stuck screen. PC execution belongs to
post-land SourceLanding Mutation; this static TestDesign runs none.

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
additional. SourceLanding Mutation: budget **42 unit control cycles** (PC-1..14
and PC-18..45) at **9 minutes** each (baseline/red/green isolated builds), two DB
integration controls (PC-15/16) at **15** each, one Windows native control (PC-17)
at **20**, plus **15** for restoration/reporting: 378 + 30 + 20 + 15 =
**443 minutes**. This is 45 cycles / 135 method-scoped phase executions and builds,
not 45 additional ordinary tests. PC-17 requires a Windows SourceLanding task;
portable PCs can use Linux. No batching discount because controls share production
files/decisions. Ordinary V/R + Mutation verification floor is **492 minutes**;
with S1's 75 and S2/S3 authoring's 120, the estimated total is **687 minutes**
before ordinary Review/land, repairs, login/access delays and slot wait. This
replaces Plan's 209-minute Mutation estimate; no measured runtime was obtained.
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

TestDesign validation is static only: one Checkpoints heading, three 12-column
rows, floors 42/32/10 (84 planned executions), nine existing class counts from
source (37 methods / 67 cases), 17 planned new methods, and 45 distinct guard/PC
mappings. Commands were file reads/searches, a compile-free Node census, Markdown
manifest/cross-reference checks, PowerShell AST parsing of the three row command
fences (no invocation), and `git diff --check`; no product build, test
suite, offline driver test, live CLI launch, restart or PC execution occurred.
The unavailable Python interpreter was replaced with Node for the census; it did
not start a test. Static count/filter validation is not an executed checkpoint.
Outstanding acceptance evidence: S1's real Windows capture/receipt and reviewed
input boundaries, then Code's committed implementation and all three CP receipts.
