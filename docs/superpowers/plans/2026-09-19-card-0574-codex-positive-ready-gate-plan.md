# CARD-0574: require positive Codex startup readiness before delivering work

Date: 2026-09-19. Plan task: `ec339e70`. Source examined: `84bbaf51baec549292d8e0db17f8d538a36b0c60`.

Replace Codex's quiet-then-optional-MCP wait with one shared, bounded startup gate. A successful result requires a positively identified loaded-model, empty-composer layout, continuously free of startup blockers for the settle window. An absent MCP marker, a quiet terminal, or an expired wait cannot independently authorize work input.

This is a Plan deliverable. The brief requests acceptance guards and a regression test plan but does not fold TestDesign into this dispatch. Next stage: **test-design**. That stage must add the executable verification design, exact ordinary test selection and method-scoped positive controls before Code starts. No production behavior or test implementation changes in this commit.

## Evidence and predecessor ownership

- Incident and mechanism: [CARD-0574 investigation](../../investigations/2026-09-19-card-0574-codex-boot-prompt-wedge.md). Attempt 1 typed into `model: loading` and queued the brief; attempt 2 typed around MCP `(1/3)` and held the brief in a composer with `tab to queue message`. Neither produced a native UserPrompt. The precise internal MCP/client stall is not established; the unsafe readiness paths are established in source.
- `scripts/card.ps1 get CARD-0299`, read during this Plan on 2026-09-19, reports **Done**, all four slices shipped. Git history confirms S1 `fd6c8a50` (submit evidence), S3 `94b29a4e` (MCP wait), S2 `78629298` (one BootWedged relaunch), S4 `89642ce0` (docs). Build on these; do not reimplement their submission or recovery machinery.
- A board-scoped `GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&status=Queued,Dispatched,Working,Blocked` returned no active CARD-0299 or CARD-0133 tasks. The only matching CARD-0574 task was this Plan. This is a point-in-time overlap check, not a reservation for Code.
- The [CARD-0299 plan](2026-09-01-card-0299-codex-plan-unsubmitted-fix-plan.md) deliberately specified first-snapshot absence and timeout-and-proceed. CARD-0574 explicitly supersedes those S3 choices; the earlier implementation followed its plan.
- The brief's `docs/investigations/2026-08-27-card-0133-codex-readin*.md` glob does not exist in this checkout. Read its actual [readiness plan](2026-08-27-card-0133-codex-readiness-and-boot-wedge-plan.md), [partial S0 probe](../../investigations/2026-08-27-card-0133-s0-boot-wedge-probe.md), and [superseding harness diagnosis](../../investigations/2026-08-30-card-0133-s0-harness-diagnosis.md). The plan's status addendum confirms S0-P4 (safe composer clearing) and S4 (active input probe) remain deferred. `disable_paste_burst=true` is already shipped.

## Ground truth

| Card assumption or proposed shortcut | What this checkout does / evidence | Design consequence |
|---|---|---|
| There is a first-snapshot race. | `src/Antiphon.Agents.Pty/CodexDetectors.cs`, `CodexMcpBoot.WaitUntilAbsentAsync`, returns on its first MCP-free snapshot. `CodexMcpBootTests.WaitUntilAbsent_returns_immediately_when_the_line_is_not_on_screen` explicitly pins that behavior. | Remove the negative-only readiness path and replace the obsolete test contract. |
| The ready gate waits while MCP boots. | `RunnerCodexAdapter.WaitForReadyAsync` first waits for 1,000 ms of quiet after visible output, observing trust; only then runs the MCP helper. Helper expiry logs and proceeds after 10,000 ms. | Startup observation, positive evidence, debounce and the deadline belong in one loop. Expiry must withhold work. |
| Model loading is already recognized as busy. | Neither ready path checks `model: loading`. The incident's footer already names `gpt-6-astra` while its banner still says loading. | A footer model name cannot override a loading banner. |
| MCP completion can be recognized as 100%. | The repository pins `Starting MCP server` and `Booting MCP server` strings; there is no established completion event/percentage in the snapshot contract. `100% context left` occurs on the failing frame. | Never parse context remaining as startup progress; do not require seeing an MCP phase when none is painted. |
| A composer/placeholder proves readiness. | Attempt 1 shows an empty placeholder while the brief is queued and the model is loading. Attempt 2 has a loaded model but MCP boot and queue-mode footer. | Require the full conjunction; neither placeholder nor loaded model suffices alone. |
| Raw output is current UI state. | Raw output accumulates historical trust/MCP lines. `RunnerTerminalSession` exposes separate raw/screen calls, each fetching a snapshot. | Read one current rendered snapshot per decision; do not combine different frames or use old raw blocker text. |
| Fixing `SendPromptAsync` covers delegation. | Delegation briefs use `SessionMessageQueueService`, after launch readiness, not `CodexSubmitConfirmation`. Both delivery paths depend on adapter readiness. | Fix the adapter gate and exercise launch-to-queue ordering. Keep submit confirmation and BootWedged as independent backstops. |
| Only one adapter needs a change. | Production uses `RunnerCodexAdapter`; in-process `CodexAdapter` uses `CodexReadyDetector`. Their lockstep comments are not executable parity proof. | Both drive the same classifier/tracker/waiter; cover both boundaries. |
| Existing fakes are ready-layout accurate. | `ScriptedCodexRunnerClient` has scripted Working/idle behavior, counters and transcripts, but no startup timeline. `CodexAdapterLocalShellTests` currently accepts a bare cmd prompt as Codex ready. `CxSession.WaitForComposerAsync` accepts a banner without a modal. `HerdrAlwaysOnChannelParityTests.Herdr_launch_definition_starts_adopts_and_exits` also exercises Codex against `FakeHerdrServer`'s generic `agent:{kind}` screen. | Extend the scripted client and update shell and Herdr startup fixtures; none is an independent readiness oracle today. |
| Readiness failure needs a new kill/retry path. | `AgentSessionService.WaitForReadyOrThrowAsync` already rejects false; launch catches perform generation-bound kill then dispose. `SessionMessageQueueBootWedgeTests` covers the separate post-typing one-relaunch policy. | Reuse launch failure ownership. Do not call BootWedged for a startup that never received work. |

## Decisions

**D-1 — Positive screen contract, shared across adapters.** Use a concrete `CodexStartupScreen` classifier plus a per-wait `CodexReadyTracker` in the Pty library, with an asynchronous waiter that accepts snapshot/startup-observer/exit delegates. Reuse `CodexReadyDetector` as the in-process entry point. These names are proposed, not existing APIs. The classifier is pure; the tracker accepts elapsed time; only the waiter performs I/O and delays. This follows the existing `CodexTurnScreenTracker` pattern and permits deterministic tests without Codex, ConPTY, HTTP or PostgreSQL.

**D-2 — One deadline and a positive settle window.** Retain `CodexReadyMaxWaitMs = 60_000` as the total readiness budget, starting at gate entry, including trust handling, every poll and debounce. Reuse `CodexReadyQuietPeriodMs = 1_000` as the duration for which the recognized ready layout must remain stable; do not increase it as the fix. Poll at 50 ms, bounded by remaining time. Require at least two fresh reads even with a tiny test settle. A blocker or incomplete/repaint frame resets the candidate. Deadline exhaustion returns false; no branch logs "typing anyway". The existing maximum is a failure bound, not a promise that every cold MCP startup finishes within it.

**D-3 — Preserve config binding without preserving the bypass.** Keep `CodexBootStatusMaxWaitMs` bindable for compatibility, with its non-negative validator, but deprecate its old control of readiness. A positive value becomes a once-per-wait diagnostic threshold after MCP is first observed; `0` suppresses this intermediate diagnostic only. It neither disables the positive gate nor adds time to D-2. Final timeout always logs the blocker. Document this intentional behavior change and pin zero/default/custom values in tests. Reject retaining two independent failure budgets, removing a deployed key silently, or using its old zero value as a safety bypass. No new timing knob is necessary.

**D-4 — Positive layout is a conservative observation, not an input-liveness certificate.** Require a current loaded-model banner and a recognized bottom composer/status layout, with the blockers below absent. No active sentinel, Enter, resize, slash command or erase sequence is a readiness probe. S0-P4 never qualified a safe Codex clear operation, and a probe typed during boot has the same race as the brief. Re-reading a stable layout cannot prove that a frozen input loop will process the next byte. Keep that limit explicit; retain the existing composer, submit and transcript checks after ready. A future active probe requires its own measured clear-and-remain-empty contract before adoption.

**D-5 — Keep the existing narrow trust action; treat other modals as not ready.** Evaluate trust against the current frame, accept the known `Do you trust the contents of this directory` + `Yes, continue` shape once, reset the candidate, and obtain a new snapshot. This is the sole startup-input exception: choosing the cwd already authorizes this established action. Do not replay stale raw trust text into a different modal. An update notice in the banner is allowed; a blocking update/continue picker, sandbox onboarding/setup, sign-in prompt or unknown composer layout withholds readiness. Do not add auto-upgrade, sandbox choices or `/usage` actions to this fix.

**D-6 — Prefer prevention to a longer-wait retry.** The production one-relaunch backstop already retried this incident and failed twice. Adding a fixed 5/10/30-second initial delay would penalize warm launches, cannot bound model/MCP loading, and still eventually types without evidence. A larger absence-only cap leaves the first-snapshot bug unchanged. Implement the positive gate; retain BootWedged's limit of one after a genuine post-typing failure. Do not automatically relaunch on the new readiness timeout or raise retry limits in this card.

**D-7 — Bound scope to startup.** Keep delivery attempts, generation fencing, bracketed paste/LF/separate Enter, `disable_paste_burst`, transcript confirmation, provider-stall detection, done detection, model routing and MCP configuration intact. No CLI upgrade, credential/home edit, database migration, new incident enum or queue recovery redesign is needed. Report the final blocker in structured adapter logs, correlated to session ID, with elapsed time and MCP-ever-seen; use existing generic persisted not-ready failure text. No raw prompt or complete screen is needed in routine logs.

These are implementer decisions within the requested fix, not outstanding operator approval questions. TestDesign remains separate; there is no assumption that an unmeasured active probe or real-provider experiment has been authorized or passed.

## Readiness contract and algorithm

Classify the **current rendered screen** into a ready candidate or a reason it is not ready. Use normalized line endings/terminal padding but preserve row boundaries. Do not concatenate a cumulative raw log onto it. Raw visible-output evidence may reject blank/title-only startup; it cannot supply a missing current composer.

A ready candidate requires all of:

1. A recognizable Codex startup banner with a nonempty selected model value on its `model:` row; `loading` or an unknown/loading variant is not selected. Do not hard-code Astra, effort level, version number or cwd. If this banner is incomplete/absent, wait rather than interpreting the footer alone as success. This is a cold-launch contract; it must not be installed as a requirement on ordinary warm-session message delivery.
2. The bottom active composer row begins with the observed prompt glyph (`>` or `›`) and contains either no text or an exact supported idle hint. Initial supported hint shapes are `Ask Codex to do anything`, `Improve documentation in @filename`, and `Write tests for @filename`, grounded in the incident and CARD-0133 controls. Recognize these only in the bottom composer region with its associated status/footer; a quote in scrollback is not a composer. Whitelist any additional hint only with a source fixture. Arbitrary text after a prompt glyph, continuation lines, or a pasted-text chip is a nonempty composer.
3. A recognized adjacent status/footer layout completes that region (model/effort/cwd style or the observed context/shortcut style). A bare `>` shell prompt, banner alone, footer alone, unknown suffix, and partially repainted bottom region remain unknown. Exact row/box parsing and the supported narrow/wide layouts must be pinned to fixtures in TestDesign; no full-screen substring shortcut.
4. No startup/interaction blocker in the active UI: model loading; either MCP boot marker; `MCP startup incomplete` failure; Working/interrupt state; `tab to queue message`; `Queued follow-up inputs`; uncleared trust; blocking `Press enter to continue`/update picker; sandbox setup/input-disabled state. Do not treat transient removal of one blocker as a latched success. Do not accept `(3/3)` while a Starting/Booting line still stands: it is not an independently established completion signal.

Missing/unknown information gives **not ready**. A static update-available notice or the usage-reset informational banner does not independently block a complete idle layout. MCP never being observed is allowed, since servers can be disabled or complete between polls, but only after the entire positive conjunction settles. MCP observed then gone is subject to the same conjunction and full settle; absence alone never counts.

The waiter starts one monotonic timer. Each iteration:

1. Honor cancellation, expiry and process exit before acting. Fetch a fresh snapshot within the remaining budget. In the runner path expose a small `RunnerTerminalSession` snapshot accessor so raw text and rendered screen come from one existing `SessionRunnerSnapshotDto`; no wire-contract change. In-process reads use the runner's current screen and visible-output check, conservatively retrying an incomplete frame.
2. Apply the existing trust action only if this current frame matches it and it has not already been accepted. Reset the candidate after any startup input. Re-read before classification; do not reuse the pre-Enter frame.
3. Classify. Any blocker/unknown resets the candidate and updates the bounded diagnostic reason. A positive observation starts/continues the candidate. A change to the normalized relevant banner/composer/footer region restarts settling; ignore only presentation padding/cursor decoration covered by fixtures. An output counter alone never makes a frame positive.
4. Return true only on a fresh, still-positive observation after the full settle interval and before the deadline. Perform cancellation/exit/deadline checks again before success. Otherwise wait for the lesser of poll interval and remaining budget.

Cancellation, snapshot failure/unavailability, exit and timeout must never become true. A snapshot exception may propagate to the existing launch failure cleanup; do not swallow it into success. Bound awaited snapshot/startup operations with the same remaining-budget token so a stalled HTTP request cannot introduce a second unbounded wait. A deadline cancellation is translated to the ordinary not-ready result; caller cancellation retains the launch service's existing cleanup semantics. Reset tracker state per call and per launch; never cache readiness across generations.

There is inevitably an observation-to-write interval, and an asynchronous UI could display new startup activity after the last ready sample. This design closes the demonstrated loading/first-absence/timeout paths; it does not claim atomic acceptance by the external CLI. Existing composer/submit/transcript verification and one BootWedged recovery continue to bound that residual case. Record a future counterexample as a new fixture rather than widening a sleep.

## Acceptance guards

| Guard | Required result |
|---|---|
| A-1: initial MCP-free blank/banner/loading frame, then delayed MCP | Zero work bytes/submit Enters while waiting; no successful ready result before the subsequent complete idle layout has settled. |
| A-2: incident attempt 1 and attempt 2 frames, including misleading model/footer/100% context | Both remain not ready indefinitely and terminate at the configured deadline with no work input. |
| A-3: positive candidate followed by MCP/loading/queue-mode/blank redraw | Candidate resets. A later good frame owes a new full settle; no latch survives the blocker. |
| A-4: warm-cache/no-MCP cold launch; completed-MCP launch | Both return true promptly after positive settling, without waiting for a boot marker to appear or for the full deadline. |
| A-5: old MCP bound or zero-disable setting | Neither releases work. Optional warning can fire once; gate continues under the single total budget. |
| A-6: trust/update/history | Exactly one existing trust response when currently visible; no repeat from raw history. Nonmodal notice may pass; blocking update/sandbox/sign-in/unknown UI types no work and times out. |
| A-7: nonempty/queued composer, unknown hint, title-only output, footer-only output | Never ready. No attempt to clear, submit or overwrite the content. |
| A-8: timeout, cancellation, child exit, snapshot failure | No success; existing launch failure/cancellation ownership tears down the accepted generation. No queue attempt charged, no BootWedged incident or relaunch count consumed solely for pre-input waiting. |
| A-9: both adapters and real launch-to-delivery ordering | Shared behavior; launch ownership remains held while waiting. Work reaches the normal delivery path only after true, once, and with existing native-prompt matching intact. |
| A-10: bounded observability | Final failure log identifies the last blocker and elapsed budget; success log/debug trace identifies settled positive readiness. No full prompt/screen logging; no "typing anyway". |

## Implementation slices

Commit/push each meaningful slice with its actual verification outcome. Recheck active same-area Code work before dispatch. Proposed source scope: `src/Antiphon.Agents.Pty/Codex*`, the two Codex adapters, `RunnerTerminalSession`, Codex registry settings and named tests/docs below.

### S1 — Shared positive classifier and deterministic state tracker

Files: add `src/Antiphon.Agents.Pty/CodexStartupReadiness.cs`; refactor `src/Antiphon.Agents.Pty/CodexDetectors.cs`; add `tests/Antiphon.Agents.Pty.Tests/CodexStartupReadinessTests.cs` and `CodexReadyTrackerTests.cs`; update `CodexMcpBootTests.cs`; add sanitized layout fixtures under `tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/`.

Retain useful pure MCP-marker detection. Add the new classifier/tracker/waiter and its tests alongside the old helper in this first slice so existing callers still compile. Implement elapsed-time state tests with no real sleeps. Include a delegate-based waiter seam exercised by `CodexReadyWaitTests.cs`, using a local controlled clock with correctly advancing timers (not a frozen clock registered into the whole server). S2 removes the unsafe waiter and its obsolete tests when it migrates both callers; each committed slice must build independently.

### S2 — Integrate both adapter paths, deadline, trust and config compatibility

Files: `server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs`, `server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs`, `server/Infrastructure/Agents/Pty/CodexAdapter.cs`, `server/Application/Settings/AgentRegistrySettings.cs`, `src/Antiphon.Agents.Pty/CodexDetectors.cs`; keep `server/Infrastructure/Agents/Pty/AgentRegistrySettingsValidator.cs` non-negative compatibility contract. Tests: extend `tests/Antiphon.Tests/Agents/ScriptedCodexRunnerClient.cs`; add `RunnerCodexAdapterReadyTests.cs`; update `AgentRegistrySettingsTests.cs`, `CodexAdapterLocalShellTests.cs` and `tests/Antiphon.Agents.Pty.Tests/CodexMcpBootTests.cs`. Remove the old waiter and replace tests specifying immediate absence success, zero bypass and timeout-and-proceed now that both callers use the new gate.

Script startup snapshots/status/cancellation separately from existing turn indicator/read counters; record ordered input writes and readiness completion. Existing submit/done tests must retain their meaning. Make local-shell fixtures paint a genuine synthetic Codex startup layout after their slow-start/trust conditions, instead of weakening the production classifier to accept cmd. Keep their child-process limiter and teardown. Trust uses the current snapshot, not raw history. Both adapters receive identical waiter options and blocker behavior.

Also update the Codex argument of `tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.cs` to opt into a grounded startup screen. Add a narrowly scoped fixture option to `tests/Antiphon.SessionRunner.Tests/FakeHerdrServer.cs` if required; preserve generic default screens used by other tests. Its current `agent:{kind}` screen must no longer pass readiness. Re-run the named parity class and retain its launch/adopt/exit assertions. This is test fixture compatibility, not a production Herdr protocol change.

### S3 — Pin launch ownership and delivery/recovery boundaries

Files: tests in `tests/Antiphon.Tests/Application/AgentSessionLaunchFailureTests.cs`, `AgentSessionLaunchQueueOwnershipTests.cs`, and a focused new `CodexStartupDeliveryTests.cs` if a runner-backed launch-to-queue harness is clearer. Reuse existing service/DB fixtures and adapt them to the scripted `ISessionRunnerClient`; use the real `RunnerCodexAdapter` for at least one withheld-then-ready launch through normal queue delivery. Do not satisfy all application coverage with `FakeAgentProtocolAdapter.ReadyResult` alone.

Assert the input trace before readiness, during timeout cleanup, and after success; transcript row/body identity is the final delivery oracle. Scope DB assertions to seeded IDs. Re-run `SessionMessageQueueBootWedgeTests` and `SessionMessageQueueDeliveryVerificationTests` for the existing post-input behavior. Production service changes are not expected: if integration uncovers a bypass of the launch gate, report it and amend this plan before changing queue semantics.

### S4 — Update living contracts and operator verification guidance

Files: `docs/agent-kinds.md` Codex behavior, `docs/session-runtime-invariants.md` startup/readiness invariant, and this plan's execution/test evidence addendum. Explain fail-closed readiness, the legacy boot-threshold semantics, the difference between ready and delivered, and the remaining input-liveness limitation. Preserve historical investigation/plan prose as evidence; add a short supersession pointer to CARD-0299 only if needed, rather than rewriting its original decisions. Generated `docs/cards/` files stay untouched.

## Regression test plan for TestDesign

This is coverage intent and fixture design, not a claim that these proposed methods already exist or ran. TestDesign must translate each row into named methods/assertions and a final verification section; it must not hand Code a real-CLI prerequisite for the gating logic.

| Coverage | Fixture/sequence and independent oracle | Proposed class |
|---|---|---|
| Initial-absence regression | First snapshot lacks MCP, model is loading; hold longer than the old quiet period; introduce boot; finish with idle. Ready task remains incomplete and input trace empty until idle settles. | `CodexReadyTrackerTests`, `RunnerCodexAdapterReadyTests` |
| Incident reproductions | Sanitized literal rendered frames from both CARD-0574 attempts; advance the entire budget. False with reason Loading/Queued or McpBoot; zero work bytes. | `CodexStartupReadinessTests`, `RunnerCodexAdapterReadyTests` |
| Real positive shape | Complete idle banner/model/composer/footer from an existing successful capture; recognized hints and blank composer; never-MCP and seen-MCP variants. False at settle-minus-one, true at settle, no extra 10-second sleep. | `CodexStartupReadinessTests`, `CodexReadyTrackerTests` |
| Debounce reset | Idle for settle-minus-one, then boot/loading/queue mode/empty/partial frame, then idle. Assert the exact new earliest success time. | `CodexReadyTrackerTests` |
| Unknown/layout false positives | Banner only; plain shell `>`; hint in scrollback; hint plus extra text; multiline text/chip; footer-only model; loading header plus named model footer; `100% context left`; `(3/3)` still booting. | `CodexStartupReadinessTests` |
| Historical/current separation | Raw output retains trust and MCP while current screen is ready; no trust write or stale blocker. Current modal over historical idle stays blocked. | `RunnerCodexAdapterReadyTests` |
| Slow/persistent MCP | Boot lasts beyond legacy 10-second threshold but finishes within 60-second budget; then permanently boots through deadline. First can succeed only after settling; second false. Warning count bounded. | `CodexReadyWaitTests`, `RunnerCodexAdapterReadyTests` |
| Legacy zero/custom setting | Same loading/boot fixture at `0`, default and small custom thresholds. No bypass; expected intermediate warning behavior; total deadline unchanged. | `AgentRegistrySettingsTests`, `RunnerCodexAdapterReadyTests` |
| Single budget | Spend most of budget before/while accepting trust, leave less than settle; false. Hanging snapshot honors linked deadline. Ready frame at/after expiry never succeeds. | `CodexReadyWaitTests` |
| Cancellation/exit/errors | Cancel before first read and during wait/trust/read; child exits while candidate is pending; snapshot throws. No success or payload writes; all waiter work awaited. | `CodexReadyWaitTests`, `RunnerCodexAdapterReadyTests` |
| Both adapters | Local shell prints loading, waits, then a complete idle layout; trust fixture prints idle only after response. Assert false-before and true-after, plus timeout cleanup. | `CodexAdapterLocalShellTests` |
| Herdr fixture compatibility | Codex argument opts into a complete positive startup fixture; launch/adopt/exit still pass, generic fake banner alone remains insufficient. Claude/Grok arguments retain their own behavior. | `HerdrAlwaysOnChannelParityTests` |
| Queue boundary | Actual adapter + launch service + queue; hold startup and request flush; no attempt/input; release positive frames, native full-body UserPrompt exactly once; separately deadline then generation kill/dispose and no BootWedged charge. | `CodexStartupDeliveryTests`, `AgentSessionLaunchFailureTests`, `AgentSessionLaunchQueueOwnershipTests` |
| Existing backstops | Existing swallowed-submit, positive Working, transient emptied-composer and one-relaunch cases remain valid. | `RunnerCodexAdapterSubmitConfirmTests`, `RunnerCodexAdapterTurnCompleteTests`, `SessionMessageQueueDeliveryVerificationTests`, `SessionMessageQueueBootWedgeTests` |

Fixture provenance is a TestDesign requirement: negative frames come directly from the committed investigation; positive layouts must come from an existing successful Codex capture or a documented existing fixture, with CLI version/geometry and redaction noted. Do not invent a green startup contract by making the fake emit whatever the new predicate expects. If existing captures cannot establish a full positive shape, hand back that precise missing evidence for measurement; do not silently enable a heuristic banner-only fallback. Synthetic timeline fixtures can vary the order/timing of these grounded frames freely.

Suggested positive-control targets for the later Mutation stage: restore first-absence return; ignore loading; treat timeout/zero as success; latch a single ready frame; omit queue-mode/nonempty-composer blocker; restart the overall deadline after trust; bypass the shared gate in one adapter. TestDesign must assign each to an exact method and expected assertion failure. No mutation runs in this Plan dispatch.

Ordinary verification should build once into `bin-card0574/` (forward slash) and run the Unit lane plus the named affected integration classes, sequentially across `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests`, following [testing/build operations](../../testing-and-build.md). Example individual class invocation, to be finalized by TestDesign:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0574/ -- --treenode-filter '/*/*/RunnerCodexAdapterReadyTests/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/card0574-ready
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0574/ -- --treenode-filter '/*/*/CodexReadyTrackerTests/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/card0574-tracker
```

Use fresh results directories per invocation and inspect nonzero executed counts in fresh TRX. Run foreground, commit before a large run, freeze source until it ends, and remove only the verified worktree-local `bin-card0574` outputs when finished. Do not widen assertions/retries/timeouts to hide a failure; reproduce a suspected inherited failing method at the base. No client/E2E suite is implicated. Default regression coverage needs no installed Codex, auth, model spend, browser, production runner or user home. Local-shell tests use Windows ConPTY; application integration tests use the established isolated test database.

## Risks and rollout evidence

- **False negatives from UI drift:** exact positive layouts may change. Unknown layouts time out safely; collect a sanitized frame and extend fixtures deliberately. Do not fix this by permitting arbitrary `>` rows. The diagnostic reason must distinguish unknown layout from active loading/MCP.
- **Slow but healthy startup:** the existing 60-second overall budget can reject an MCP whose configured startup timeout is longer. That is preferable to typing at 10 seconds. Measure observed gate duration and last blocker before proposing a separately justified cap change; no timeout increase is part of this fix.
- **Rendered-but-deaf UI:** passive evidence does not qualify an input round trip. Keep CARD-0133 S0-P4/S4 deferred and preserve the subsequent delivery ladder. A ready layout followed by a post-input wedge is evidence for that next investigation, not proof this gate confirmed input liveness.
- **Operational success needs activated binaries:** after Code/Review/land and caller-owned activation, verify the served SHA before using normal launches as evidence. Record readiness duration/reason, first work-input time, native UserPrompt and any BootWedged recovery on subsequent authorized Codex launches. A successful ready return alone is not delivery. Compare cold and warm launches; do not infer a fleet rate from one warm success or start a paid census from this Plan.

## Plan validation

Read-only code/history/board checks support the ground-truth table and predecessor status. All six relative Markdown links resolve in this checkout. No builds, tests, CLI launches, live application writes or deployments were performed for this documentation-only stage. Before settlement, run `git diff --cached --check`, commit this file, and push the task branch. TestDesign owns the executable verification design and coverage audit.


## Verification design

TestDesign task: eda54a92, 2026-09-19; inspected source: b49623d9e37c4b908ad6e75104cb350d84139650. This section is appended to the landed plan; D-1 through D-7 remain the fix design. New method names below are implementation requirements, not claims of implemented or passing tests. Code implements tests and runs ordinary V/R; ordinary Review judges that evidence before land; Mutation executes positive controls against the confirmed landed source.

### Inspection

Test and helper bodies read before naming cases:

- tests/Antiphon.Agents.Pty.Tests/CodexMcpBootTests.cs (all five methods), SubmitEvidenceTests.cs, CxSession.WaitForComposerAsync, startup/submit portion of CodexDoneDetectionCanaryTests.Turn_lifecycle_contract_on_the_modern_backend and the Pty test project resource/package entries | negative-only absence/zero/expiry -> R-7/R-8/R-22/R-24. Retain pure marker detection; remove the four obsolete waiter contracts when both callers migrate. The canary's banner-only predicate is not a startup oracle.
- tests/Antiphon.Tests/Agents/ScriptedCodexRunnerClient.cs (whole helper), CodexAdapterLocalShellTests.cs (all bodies and launch/options/turn/poll helpers), PtyTempBatch.cs, RunnerCodexAdapterSubmitConfirmTests.cs and RunnerCodexAdapterTurnCompleteTests.cs | startup versus turn counters, swallowed Enter, prompt baseline, local teardown -> V-4, R-24/R-33..R-40/R-59.
- AgentRegistrySettingsTests binding, valid-settings helper, non-positive timing and negative/zero boot-status bodies | zero/default/custom compatibility -> V-3/R-60. Keep the non-negative deployed-key contract.
- AgentSessionLaunchQueueOwnershipTests.cs (all bodies and OwnershipFixture); AgentSessionLaunchFailureTests.Launch_timeout_is_infrastructure_but_requested_cancellation_has_no_failure_outcome and LaunchFixture construction | Starting/Running, readiness cancellation and ownership -> V-6/V-8, R-41..R-49.
- HerdrAlwaysOnChannelParityTests.Herdr_launch_definition_starts_adopts_and_exits, BuildHarness and ConfirmHerdrDeliveryAsync; FakeHerdrServer launch detection, PaneSendTextJson, PaneReadJson, AgentStartJson and screen setters | all three kind arguments and launch-screen echo overwrite -> V-5. The current Herdr receipt helper synthesizes a DB receipt after seeing a nonce; it cannot establish native Codex acceptance.
- SessionMessageQueueBootWedgeTests.cs (all bodies/helpers); four Codex submit-evidence bodies in SessionMessageQueueDeliveryVerificationTests.cs at lines 631-708; SessionMessageQueueInterruptedAttemptTests matching-prompt, held-composer, absent-composer, age and busy recovery bodies | unchanged post-input backstops -> R-54/R-55/R-59.
- BridgeQueueHarness.CreateAsync, transcript insertion and receiver wiring; TestDbFixture isolated database API; ReceiptFailureDeliveryTests.cs (all bodies, fault interceptors, CreateProvider and OffsetClock); CapacityRecoveryTaskTests.CreateDispatcherProvider; ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync/SeedWarmAgentAsync; CheckNoteDeliveryHandoffTests.Handoff.CreateAsync/EnsureInterpreterAsync and FailFirstInsert | producer, queue, persistence cuts and recipient -> V-6..V-8/R-50..R-58.
- PostLandMutationDeliveryWorker.cs (whole owned-child helper), ScriptedSessionRunnerClient.cs and FakeAgentProtocolAdapter composer/start-registration fields | nearest fixtures for new crash worker/retained receiver -> V-7. Reuse ownership/drain discipline, not the landing-specific production boundary.
- Both Codex ready/trust adapter paths, CodexReadyDetector/CodexMcpBoot, RunnerTerminalSession startup/attach/snapshot/input, queue enqueue/flush/status checks, dispatcher initial enqueue/watchdog, and AgentSessionService ready/Running/flush/recovery/cleanup | source seams and identities below. Owners read: testing/build, orchestration stage rules, session invariants, project context, Codex kind contract and Herdr delivery contract.

Grounded fixture artifact: [startup captures](2026-09-19-card-0574-startup-captures.json). The neighboring CARD-0449 capture JSON was read before choosing this format. It preserves complete rendered positive frames, source SHA-256, replay prefix length, synchronized frame number, CLI version, row count, geometry qualification and redaction.

| Fixture | Provenance and boundaries |
|---|---|
| P-1 | CARD-0133 successful control 5851adc8, CLI 0.147.0, synchronized frame 8: selected Terra/high, “Improve documentation in @filename”, model/effort/cwd footer, static update notice. |
| P-2 | CARD-0299 successful Plan control f83bb10b, CLI 0.151.0, frame 44: selected Terra/xhigh, “Ask Codex to do anything”, model/effort/cwd footer and usage-reset information. |
| P-3 | Archived 03b55060, CLI 0.153.4, frame 25: selected Astra/xhigh, same composer/footer family, static update and usage information. This inspected frame proves a layout, not that task's business outcome. |
| N-1/N-2 | Literal sanitized frames in the CARD-0574 investigation: Loading + queued-follow-up; and loaded model + MCP 1/3 + occupied composer + tab-to-queue + 100% context. Preserve both combined incidents. Independently apply each blocker alone to P-3 so another blocker cannot mask a broken guard. |
| H-1 | “Write tests for @filename” comes from CARD-0195's recorded composer/footer and CARD-0133's hint observations. Substitute only that exact hint into P-2, labeling the result derived. Blank composer and ASCII > are derived variants; > is also grounded in CARD-0574's rendered snapshots. |

Archives were replayed through this commit's TerminalScreen, splitting only after ESC[?2026l, at 120 columns by 30 rows. Original terminal dimensions are not independently retained in those files; the artifact states that limitation. This establishes row relationships, not live resizing qualification. The investigation's successful 5e68cf83 archive was also inspected: it contains work typed while loading and later queued follow-up; eventual success does not make those frames clean positive fixtures.

Code copies these frames and provenance into tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup and links/embeds the same data for Antiphon.Tests. Add explicit resource entries: the Pty project currently copies golden/** and probes/**, not the new directory. Read the fixture, rather than duplicating a green string.

The measured positive layout is a recognizable boxed Codex banner/model, followed by the final active composer row, zero or one spacer row, then the complete model/effort/cwd footer; trailing empty rows are padding. Preserve internal row boundaries and distinguish the banner's >_ from a composer. Derived 80/160-column padding/cwd variants are permissible only with every required row complete; clipping or wrapping a required row remains unknown. No original narrow-layout geometry is claimed.

**Unverifiable seam requiring Plan before Code:** contract item 3 also offers a context/shortcut-only footer family. None of the inspected artifacts supplies a complete blocker-free loaded-model positive frame for that family. “100% context left” in N-2 and the shortcut row beside a loading model are not such evidence. Plan must explicitly narrow initial support to the grounded model/effort/cwd family, or supply a sanitized complete positive capture for the additional family. Do not fabricate a green fixture or silently broaden a substring predicate. R-6 pins the unsupported family as unknown under the current evidence. This is an evidence/contract resolution, not a human authorization question.

**Source seam requiring Plan scope resolution:** fresh `LaunchInteractiveProcessAsync` failure passes `acceptedGeneration` to `KillAndDisposeAsync`, but `ResumeInterruptedLaunchAsync`'s attached-adapter failure branch calls it without that argument (AgentSessionService.cs:704). The helper's optional-null branch invokes unconditional `KillAsync`. Thus the Ground truth statement that launch failure cleanup is generation-bound does not cover resumed startup. This is a source finding, not a runtime reproduction. Plan must explicitly include the resumed cleanup fence or name a prerequisite repair; S3 currently expects no service change. R-61/G-59/PC-59 records the required negative test. Attach generation G1, hold startup, replace the runner with G2, then expire the resumed gate: only G1 may be targeted, G2 must survive. Do not silently expand Code scope or exempt recovery from D-7.

**Queue-admission seam requiring Plan scope resolution:** SessionMessageQueueService.EnqueueAsync tests IsAcceptingInputAsync, but FlushSessionAsync (line 1188) checks only working state before DeliverNextLockedAsync, and OnTurnEndAsync (line 1044) calls that helper directly. The helper's Starting check at line 1530 is conditional on the Grok rules barrier; it is not a general Codex Running gate. AgentSessionRuntime.SendInputAsync also forwards bytes without that status check. Therefore R-43/R-44 cannot be described as deletion tests for an existing general admission guard. Hold a live Codex recipient on a complete positive frame before the settle deadline, with durable Starting and an early Pending brief; exercise each entry point with no other modal, working or missing-adapter guard to mask admission. Plan must explicitly scope the missing admission protection or establish and test another controlling invariant. This is a source gap, not a claim that a live incident reproduced. S3 itself requires return to Plan upon a discovered bypass. PC-43/PC-44 are specified for the scoped repair; if Plan chooses one shared guard, return to TestDesign to consolidate its mutation mapping while retaining both ordinary entry-point cases.

Missing setup to implement after those resolutions:

1. Inject time only at the shared waiter seam. Trackers take elapsed ticks; waiter tests advance timers and use snapshot/action barriers. The Pty test project needs Microsoft.Extensions.TimeProvider.Testing 9.5.0 (already used by Antiphon.Tests), or a local clock with equivalent timer advancement. Keep queue DI on System/OffsetClock; no frozen server clock.
2. Give ScriptedCodexRunnerClient an opt-in startup timeline, immutable per-call DTO, generation, exit latch, ordered writes, snapshot/trust gates and generation-kill records. Preserve existing turn counters/default IdleScreen. Its current idle screen lacks a startup model banner.
3. A retained receiver consumes actual input bytes, removes only bracketed-paste delimiters, and emits UserPrompt only on separate submit Enter. It must never copy expected text from a queue row. Sync its transcript DTOs through real AgentSessionService.SyncTranscriptAsync. Keep receiver state independent of application service instances.
4. Build CodexStartupDeliveryTests' harness from BridgeQueueHarness's complete launch graph, AddDelegationWorktreeGraph, dispatcher/task/notification services and real RunnerCodexAdapter factory. Use an isolated TestDbFixture database and temporary workspace. The capacity-recovery factory is warm-only and lacks a launch service, real adapter and Codex definition; do not copy it unchanged. Set task/agent/session kind and model consistently, register runtime adapters at the normal launch point, and disable unrelated launch boot probes.
5. Add CodexStartupDeliveryWorker beside PostLandMutationDeliveryWorker for abrupt process cuts. Parent owns a retained fake runner/recipient exposed over a new per-test named pipe; child runs the real dispatcher/launch/queue with a pipe-backed ISessionRunnerClient and the parent's database connection. Pause at an EF interceptor, snapshot barrier or receiver acknowledgement; kill only that owned child, await exit and drain both streams. Start a fresh child/provider against the same DB/receiver. Marker: ANTIPHON_C574_STARTUP_WORKER; entry: CodexStartupDeliveryTests.Crash_cut_worker_entry. Give the class ParallelLimiter<ProcessSpawnLimit>; the child starts no private Testcontainer. No real Program, Codex, provider home, production runner or external executor.
6. Local-shell scripts paint P-2 and hold Loading/trust transitions behind test-owned file/pipe barriers. Repaint the whole region and position cmd's prompt on the composer row; do not append a bare prompt beneath the footer. Preserve AsCodexTurn semantics, limiter and awaited teardown. Runaway watchdogs may use real time; readiness timing uses barrier order, not sleeps against Codex.
7. FakeHerdrServer gets an opt-in Codex startup screen applied after launch-script echo, covering ApplyLaunchDetection and AgentStartJson. Otherwise PaneSendTextJson would overwrite it with the script invocation. Preserve generic defaults and ordinary body echo; only the Codex parity argument opts in.

### Delivery inventory

The gate changes admission to existing asynchronous launch/brief paths. It adds no outbox or business event. Ready=true, an accepted request, queue insert, Sent, terminal event or transport acknowledgement alone never satisfies delivery acceptance.

| Path | Producer -> destination | Persistence and durable identity | Recovery and observable receipt |
|---|---|---|---|
| DLV-1 brief | real AgentTaskDispatcher.TickAsync -> launch queue/AgentSessionService -> real SessionMessageQueueService -> RunnerCodexAdapter -> retained scripted recipient | task Id/Attempt/AgentSessionId; session Id + AcceptedStartedAt; queue Id/Sequence/ExecutionTaskId/exact Body; attempt baseline/LastDeliveryGeneration | Starting holds Pending at attempts 0 with no work bytes. After positive settle, normal flush. Exactly one full recipient UserPrompt in the right session above the attempt floor must match the whole typed Body. For Codex's spilled brief require the entire pointer plus complete file bytes and original goal first/last lines. A marker alone is insufficient. |
| DLV-2 interrupted delivery | fresh launch owner or stranded/turn-end recovery -> same recipient | same committed queue and generation identities; receiver composer/transcript survives application loss | Starting attach reruns readiness. Running Pending rows use normal sweep. Held body gets Enter only; complete late receipt suppresses input. Every successful recovery ends in full UserPrompt with original queue identity. |
| DLV-3 failure | launch not-ready failure / existing delivery watchdog -> durable DeliveryFailure notification -> parent session queue -> parent recipient | original task Id/Attempt/ParentSessionId, failed SourceEventId, notification Id/ContentDigest, queue SourceLandNotificationId/Id and confirming sequence | Before-insert failure cannot recover a nonexistent brief. Existing watchdog fails visibly and notification recovery delivers the complete note. Busy parent holds Pending; eligible parent needs no unrelated turn end. Failed event or CompletionNoteQueuedAt does not prove receipt. |

Run CodexStartupDeliveryTests.Producer_brief_reaches_an_already_eligible_recipient_whole and Producer_brief_waits_for_busy_recipient_and_arrives_whole through the actual dispatcher, launch service, adapter and queue. During delayed model/MCP startup, the real producer inserts the early brief while the gate remains held. In the busy case mark the recipient working after launch restart-boundary handling and before final flush; release via the ordinary TurnEnd callback. Also exercise warm reuse without a startup banner: the new cold contract must not gate ordinary queued messages. Seeding a Pending row is not a substitute for these producer tests.

| Handoff/cut | Exact method in CodexStartupDeliveryTests, unless qualified | Required recovery evidence |
|---|---|---|
| Dispatched/Starting committed, before launch scheduling/brief insertion; insert throws | Enqueue_failure_is_reported_to_the_original_caller, cuts dispatch-committed and brief-insert, busy parent false/true | Fresh owner sees original task. No invented brief receipt; existing watchdog and notification recovery produce one complete parent failure UserPrompt. R-58. |
| Brief insert committed before nudge/inline delivery | Committed_brief_is_recovered_after_service_recreation | Crash child after SavedChanges. Original Pending row survives; resume gate or flush Running, then full prompt once. R-53. |
| Startup wait, candidate at settle-minus-one | Restart_during_boot_reverifies_before_delivering | Crash at snapshot barrier; new tracker owes a full settle, ownership held, no early bytes. R-49. |
| Ready returned before Running commit; Running committed before flush | Crash_at_running_handoff_preserves_the_queued_brief, both cuts | First recovers Starting through attach/ready; second recovers Pending via stranded sweep. Both require full receipt. |
| Sent attempt commit before first byte | Crash_after_attempt_commit_recovers_an_untyped_brief | Crash after save; absent composer permits existing revert/retry, queue identity retained, one accepted body. |
| Body held before Enter; Enter transport throws | Interrupted_typed_brief_recovers_with_enter_only, crash/throw variants | Same receiver/generation, no second body write, recovery Enter followed by full prompt. R-54. |
| Native-style UserPrompt committed before application verdict | Accepted_prompt_before_verdict_commit_is_not_typed_again | Sync and late-confirm; no later bytes; original row and one whole prompt. R-55. |
| Failure/obligation commit, note insert/commit, note attempt, accepted-before-confirm | inherited ReceiptFailureDeliveryTests.Caller_failure_obligation_survives_persistence_cuts (all 12 busy/cut arguments); new Readiness_failure_notice_survives_worker_crash with failed-committed/note-committed/prompt-accepted | Both service-recreation matrix and owned-child cuts recover the original obligation and parent's full receipt. No duplicate note or fresh task. |

Substitutes and limits: the scripted recipient models the Codex composer/rollout writer and proves application ordering, bytes and receipt matching, not a native Codex input loop's liveness or provider response. Virtual timelines do not measure MCP duration. Local shell proves ConPTY/adapter wiring, not Codex semantics. FakeHerdr launch/adopt/exit cannot prove native receipt. EF faults/service recreation do not prove abrupt worker loss, which is why V-7 includes owned-child crashes. Review must reject evidence ending before the complete recipient transcript.

### Proves it works now

New methods are obligations for Code; the present documentation stage runs no implementation test. All ordinary invocations in Cost require fresh TRX and nonzero intended methods.

- V-1: positive fixture contract | Pty unit | CodexStartupReadinessTests.Grounded_loaded_model_composer_layouts_are_ready | P-1/P-2/P-3 positive; three exact hints, blank, > and ›, LF/CRLF and trailing padding variants pass; clipped/unknown regions fail. No version/model/effort/cwd hard-coding.
- V-2: delayed model/MCP and settle | tracker/waiter | CodexReadyTrackerTests.No_mcp_and_completed_mcp_both_require_only_the_positive_settle, R-17..R-23, R-39 | virtual settle 1,000: false at 999, true at 1,000 on a new sample. Loading to 2,000; MCP at 2,050; positive at 12,000; earliest success 13,000. No-MCP needs no extra 10-second wait.
- V-3: deadline/settings | waiter/runner/config | R-22..R-32; AgentRegistrySettingsTests.Codex_boot_threshold_binds_zero_default_and_custom_without_changing_ready_budget | thresholds 0/10,000/250 do not authorize work. Positive thresholds warn once after first MCP + threshold; zero suppresses only that warning. Permanent blocker false at 60,000. Clear at 58,950 can succeed at 59,950; clear at 59,001 cannot settle before expiry. Non-positive ready settings remain invalid; direct zero waiter budget fails closed.
- V-4: both adapter boundaries | runner fake/local shell | R-33..R-40 plus updated existing shell tests | both withhold Loading, both MCP forms and incomplete redraw; both require release plus settle. Runner crosses those states with zero/default/custom threshold; shell crosses Loading/MCP/blank with zero/custom, trust-then-ready and persistent timeout. Repeated waits owe new settling. No real Codex.
- V-5: Herdr compatibility | application/real runner/FakeHerdr | HerdrAlwaysOnChannelParityTests.Herdr_launch_definition_starts_adopts_and_exits, all three kind arguments; Generic_herdr_codex_banner_is_not_startup_ready | original launch/adopt/exit assertions survive with Codex's P-3 opt-in; generic agent:codex refuses.
- V-6: incident shapes and recipient states | isolated DB/producer/launch/queue/adapter | producer methods above; CodexStartupDeliveryTests.Incident_startup_frames_never_receive_work; R-41..R-45 | N-1/N-2 stay blocked to deadline with no work/attempts. Early brief remains Pending; release yields one complete recipient prompt joined to task/session/row/body/floor. Warm reuse works without banner.
- V-7: crash/enqueue recovery at every handoff | retained receiver/DB/owned child | handoff table methods, R-49/R-53..R-55 | record durable cut image, await child death, restart services; full prompt once, or full failure receipt for the nonexistent pre-insert row. Snapshot/Enter failures are separate fault variants.
- V-8: refusal and resource ownership | application | R-46..R-48/R-58/R-61; Readiness_failure_notice_survives_worker_crash; inherited AgentSessionLaunchFailureTests.Launch_timeout_is_infrastructure_but_requested_cancellation_has_no_failure_outcome | generic not-ready error, accepted-generation kill before dispose on fresh and resumed launch, eventual ownership release, no BootWedged/relaunch/attempt charge, complete failure receipt. Preserve caller-cancellation classification.
- V-9: bounded observability | structured logger | R-56 | timeout names blocker, session, elapsed budget, MCP-ever-seen; success names positive settle. No complete screen/work text and no “typing anyway”; informational banners never cause actions.

Boundary combinations: each positive fixture crosses glyph/hint/line-ending normalization; every blocker alone crosses P-3, with N-1/N-2 retaining the combined incidents. Legacy values cross Loading/MCP; deadline cuts cross trust/read/final success; recovery crosses every persistence handoff. Full permutations of unrelated modals add no new independent bypass. Relevant model/hint/footer/row changes reset settling; right padding/cursor decoration and output counter churn over an identical relevant layout do not. Unsupported narrow wraps/context-only footers are explicitly excluded from positive acceptance pending the Plan resolution above.

### Guards the regression

Each R row gives an exact method and decisive assertion. IsReady/Ready refer to returned verdicts, not mandatory API spelling. Assertion messages must name the R ID. A permitted propagated snapshot exception is non-success, never true.

- R-1: D-1, contract 1; recognizable Codex banner required | `CodexStartupReadinessTests.Missing_codex_banner_is_unknown` | IsReady.ShouldBeFalse() for the missing-banner layout.
- R-2: Contract 1; model row must contain a nonempty selected value | `CodexStartupReadinessTests.Empty_model_row_is_unknown` | IsReady.ShouldBeFalse() with only the model value erased.
- R-3: D-1/A-2; loading in banner overrides a named footer | `CodexStartupReadinessTests.Loading_model_never_borrows_the_footer_model` | IsReady.ShouldBeFalse() with P-3's model changed to loading.
- R-4: D-1/A-7; composer empty or exact supported hint only | `CodexStartupReadinessTests.Composer_content_must_be_empty_or_an_exact_supported_hint` | IsReady.ShouldBeFalse() for appended text, unknown hint, continuation and pasted chip.
- R-5: Contract 2; select the active bottom composer, not scrollback | `CodexStartupReadinessTests.Historical_composer_above_an_unknown_bottom_is_not_ready` | IsReady.ShouldBeFalse() for the decoy pair.
- R-6: Contract 3; adjacent recognized footer required | `CodexStartupReadinessTests.Missing_or_partial_footer_keeps_the_composer_unknown` | IsReady.ShouldBeFalse() for missing, partial and unknown footer.
- R-7: Contract 4/A-1; Starting MCP marker blocks even at 3/3 | `CodexStartupReadinessTests.Starting_mcp_blocks_every_progress_fraction` | IsReady.ShouldBeFalse() for 1/3 and 3/3 over otherwise positive P-3.
- R-8: Contract 4; Booting MCP marker independently blocks | `CodexStartupReadinessTests.Booting_mcp_blocks_an_otherwise_ready_layout` | IsReady.ShouldBeFalse().
- R-9: Contract 4; MCP startup incomplete is not ready | `CodexStartupReadinessTests.Incomplete_mcp_startup_is_a_blocker` | IsReady.ShouldBeFalse().
- R-10: Contract 4; Working/interrupt is not idle startup | `CodexStartupReadinessTests.Working_or_interrupt_state_withholds_readiness` | IsReady.ShouldBeFalse() for each isolated busy indicator.
- R-11: A-2; tab-to-queue footer is not submittable | `CodexStartupReadinessTests.Queue_mode_footer_blocks_a_loaded_model` | IsReady.ShouldBeFalse().
- R-12: A-2; queued follow-up inputs block even with an empty composer | `CodexStartupReadinessTests.Queued_follow_up_blocks_an_empty_composer` | IsReady.ShouldBeFalse().
- R-13: D-5; uncleared trust cannot pass classification | `CodexStartupReadinessTests.Uncleared_trust_is_not_a_ready_composer` | IsReady.ShouldBeFalse().
- R-14: D-5; blocking continue/update picker withholds readiness | `CodexStartupReadinessTests.Blocking_update_is_distinct_from_a_static_notice` | IsReady.ShouldBeFalse() for picker; unmodified static notice still passes.
- R-15: D-5; sandbox setup/input-disabled withholds readiness | `CodexStartupReadinessTests.Sandbox_setup_and_input_disabled_are_blockers` | IsReady.ShouldBeFalse() for each isolated phrase.
- R-16: D-5; sign-in modal withholds readiness | `CodexStartupReadinessTests.Sign_in_modal_is_not_ready` | IsReady.ShouldBeFalse().
- R-17: D-2/A-4; owe a full positive settle interval | `CodexReadyTrackerTests.Ready_requires_the_full_settle_interval` | Ready.ShouldBeFalse() at settle-minus-one.
- R-18: D-2; at least two fresh observations | `CodexReadyWaitTests.One_observation_never_establishes_readiness` | gate.IsCompleted.ShouldBeFalse() after a full settle when only the first positive snapshot has completed and the second read is held. Releasing the second fresh positive frame permits success; a cached frame and elapsed timer cannot substitute.
- R-19: A-3; every blocker/unknown resets the candidate | `CodexReadyTrackerTests.Every_blocker_and_incomplete_frame_restarts_settling` | Ready.ShouldBeFalse() at the old candidate deadline.
- R-20: D-2; relevant positive screen churn resets settling | `CodexReadyTrackerTests.Relevant_layout_changes_restart_settling` | Ready.ShouldBeFalse() after model/hint/footer changes at settle-minus-one.
- R-21: Algorithm; no tracker state across calls or generations | `CodexReadyWaitTests.A_new_wait_and_generation_start_without_a_candidate` | secondWait.IsCompleted.ShouldBeFalse() before its own settle.
- R-22: D-2/A-8; deadline, including zero total wait, never succeeds | `CodexReadyWaitTests.Deadline_or_zero_total_budget_never_authorizes_input` | result.ShouldBeFalse() for permanent blockers and ready-at-expiry.
- R-23: D-2; trust and polling share the original budget | `CodexReadyWaitTests.Trust_consumes_the_original_total_budget` | result.ShouldBeFalse() when trust leaves less than a settle.
- R-24: D-3; legacy zero/default/custom threshold cannot release input | `RunnerCodexAdapterReadyTests.Legacy_boot_threshold_never_bypasses_the_positive_gate` | ready.IsCompleted.ShouldBeFalse() while model/MCP remains blocked.
- R-25: Algorithm/A-8; bound snapshot awaits | `CodexReadyWaitTests.A_stalled_snapshot_finishes_at_the_gate_deadline` | gate.IsCompleted.ShouldBeTrue() at virtual deadline with no caller cancellation.
- R-26: Algorithm/A-8; bound startup-input awaits | `CodexReadyWaitTests.A_stalled_trust_write_finishes_at_the_gate_deadline` | gate.IsCompleted.ShouldBeTrue() at virtual deadline.
- R-27: Algorithm; cancellation at entry prevents reading or startup input | `CodexReadyWaitTests.Cancellation_at_entry_prevents_reads_and_input` | readCalls.ShouldBe(0) and writes.ShouldBeEmpty().
- R-28: Algorithm; cancellation during read prevents a trust action | `CodexReadyWaitTests.Cancellation_during_read_prevents_trust_input` | writes.ShouldBeEmpty() when the snapshot delegate cancels the caller then returns trust.
- R-29: Algorithm; recheck cancellation before success | `CodexReadyWaitTests.Cancellation_on_the_final_snapshot_cannot_return_ready` | Should.ThrowAsync<OperationCanceledException>(gate).
- R-30: Algorithm; exited process never enters ready/startup action | `CodexReadyWaitTests.Exited_process_never_receives_startup_input` | writes.ShouldBeEmpty() and result.ShouldBeFalse().
- R-31: Algorithm; recheck exit before success | `CodexReadyWaitTests.Exit_on_the_final_snapshot_cannot_return_ready` | result.ShouldBeFalse().
- R-32: Algorithm/A-8; null/unavailable/throwing snapshots fail closed | `CodexReadyWaitTests.Unavailable_or_failed_snapshot_cannot_reuse_a_ready_frame` | no successful result; snapshot exception remains the injected exception.
- R-33: D-1; one runner snapshot per decision, no mixed frames | `RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision` | snapshotReads.ShouldBe(decisions) on an alternating-frame script.
- R-34: D-5; historical raw text cannot trigger current trust or readiness | `RunnerCodexAdapterReadyTests.Raw_history_cannot_authorize_trust_input` | writes.ShouldBeEmpty() when raw trust overlays a current update picker.
- R-35: D-5; only the complete known trust shape may receive Enter | `RunnerCodexAdapterReadyTests.Trust_acceptance_requires_both_current_labels` | trustEnters.ShouldBe(0) when Yes-continue is absent.
- R-36: D-5; trust may be accepted only once per launch | `RunnerCodexAdapterReadyTests.Repeated_trust_frames_receive_only_one_enter` | trustEnters.ShouldBe(1) across repeated current trust frames.
- R-37: D-5; startup input resets candidate and requires a new frame | `CodexReadyWaitTests.Trust_response_forces_a_new_snapshot_and_full_settle` | ready.IsCompleted.ShouldBeFalse() until the post-action full settle.
- R-38: D-4; readiness never probes, clears or submits work | `RunnerCodexAdapterReadyTests.Readiness_has_no_input_except_the_known_trust_enter` | writes.ShouldBeEmpty() and resizeCalls.ShouldBe(0) on ordinary startup.
- R-39: S2/A-9; runner adapter actually invokes the shared gate | `RunnerCodexAdapterReadyTests.Delayed_mcp_after_loading_withholds_runner_ready` | ready.IsCompleted.ShouldBeFalse() at the loading checkpoint.
- R-40: S2/A-9; in-process adapter actually invokes the shared gate | `CodexAdapterLocalShellTests.Loaded_layout_release_is_required_by_the_in_process_adapter` | ready.IsCompleted.ShouldBeFalse() while the child holds Loading.
- R-41: S3/A-9; Running published only after successful ready | `CodexStartupDeliveryTests.Starting_session_is_not_published_running_before_positive_ready` | stored.Status.ShouldBe(SessionStatus.Starting) at the held startup barrier.
- R-42: S3; WhenIdle enqueue must not deliver into Starting | `CodexStartupDeliveryTests.Enqueue_during_boot_keeps_the_brief_pending_without_attempt` | row.DeliveryAttempts.ShouldBe(0) and workWrites.ShouldBeEmpty().
- R-43: S3; explicit flush must not bypass Starting | `CodexStartupDeliveryTests.Explicit_flush_during_boot_types_nothing` | workWrites.ShouldBeEmpty() after the explicit flush.
- R-44: S3; turn-end flush must not bypass Starting | `CodexStartupDeliveryTests.Turn_end_during_boot_types_nothing` | workWrites.ShouldBeEmpty() after the boundary callback.
- R-45: S3/A-9; launch ownership remains held while gate is pending | `CodexStartupDeliveryTests.Launch_ownership_spans_the_entire_startup_gate` | launchQueue.Owns(sessionId).ShouldBeTrue() at held startup.
- R-46: S3/A-8; cleanup cannot kill a replacement generation | `CodexStartupDeliveryTests.Readiness_failure_targets_only_the_accepted_generation` | killGenerationCalls.ShouldBe([acceptedGeneration]) and replacementAlive.ShouldBeTrue().
- R-47: S3/A-8; failed launch kills before adapter disposal | `CodexStartupDeliveryTests.Readiness_failure_kills_before_disposal` | lifecycle.ShouldBe(["KillGeneration", "Dispose"]).
- R-48: D-6/A-8; pre-input refusal cannot spend BootWedged/relaunch/attempts | `CodexStartupDeliveryTests.Readiness_timeout_does_not_consume_delivery_or_boot_wedge_attempts` | BootWedgeRelaunchCount.ShouldBe(0), DeliveryAttempts.ShouldBe(0), BootWedged incidents empty.
- R-49: S3/recovery; interrupted Starting launch reruns the same gate | `CodexStartupDeliveryTests.Restart_during_boot_reverifies_before_delivering` | workWrites.ShouldBeEmpty() while the resumed frame remains Loading.
- R-50: A-9/delivery; complete recipient body is necessary evidence | `CodexStartupDeliveryTests.A_marker_or_prefix_receipt_does_not_confirm_the_brief` | DeliveryVerdict.ShouldNotBe(Delivered) and completeReceiptCount.ShouldBe(0) before full receipt.
- R-51: A-9/delivery; old matching receipt cannot confirm a new attempt | `CodexStartupDeliveryTests.Old_receipt_does_not_confirm_this_attempt` | DeliveryVerdict.ShouldNotBe(Delivered) with the same full body below the floor.
- R-52: A-9/delivery; receipt must belong to the intended session | `CodexStartupDeliveryTests.Wrong_session_receipt_does_not_confirm_this_attempt` | DeliveryVerdict.ShouldNotBe(Delivered) with the full body only in another session.
- R-53: S3/recovery; persisted pending row survives service loss and delivers whole | `CodexStartupDeliveryTests.Committed_brief_is_recovered_after_service_recreation` | completeReceiptCount.ShouldBe(1) after recovery; original row Id/Sequence unchanged.
- R-54: S3/recovery; interrupted held composer is completed by Enter only | `CodexStartupDeliveryTests.Interrupted_typed_brief_recovers_with_enter_only` | bodyWrites.ShouldBe(1) and recoveredWrites.ShouldBe(["\r"]).
- R-55: S3/recovery; complete late receipt precedes any new write | `CodexStartupDeliveryTests.Accepted_prompt_before_verdict_commit_is_not_typed_again` | writesAfterRestart.ShouldBeEmpty() and completeReceiptCount.ShouldBe(1).
- R-56: D-7/A-10; bounded diagnostics exclude prompt and full screen | `RunnerCodexAdapterReadyTests.Timeout_diagnostics_name_blocker_without_prompt_or_screen` | capturedLogs.ShouldNotContain(secretSentinel).
- R-57: Delivery inventory failure leg; terminal failure notification remains recoverable | `ReceiptFailureDeliveryTests.Caller_failure_obligation_survives_persistence_cuts` | RemindUnacknowledgedFailuresAsync(...).ShouldBe(1) after a durable failure cut; the ordinary method then requires the complete caller receipt.
- R-58: Delivery inventory before-enqueue loss; fail visibly to the original caller | `CodexStartupDeliveryTests.Enqueue_failure_is_reported_to_the_original_caller` | failureObligation.ShouldNotBeNull() followed by exactly one complete caller UserPrompt.

- R-59: preserve post-input backstops | RunnerCodexAdapterSubmitConfirmTests, RunnerCodexAdapterTurnCompleteTests, SessionMessageQueueBootWedgeTests and SessionMessageQueueDeliveryVerificationTests | one body write for swallowed Enter, preserved transcript baseline, no bare-quiet completion, transient-empty no-latch, one BootWedged relaunch and second-wedge failure. These inherited algorithms are not changed. Their internal guard census remains owned by predecessor plans; this is compatibility coverage, not a claim of rerun historical PCs.
- R-60: preserve config/fixture compatibility | AgentRegistrySettingsTests; CodexMcpBootTests.IsVisible_matches_starting_and_booting_forms; local-shell/Herdr methods in V-4/V-5 | non-negative legacy key, both marker forms and all three Herdr launch/adopt/exit arguments.
- R-61: S3/D-7, resumed cleanup seam | `CodexStartupDeliveryTests.Resumed_readiness_failure_cannot_kill_replacement` | unconditionalKills.ShouldBe(0), killGenerationCalls.ShouldBe([attachedGeneration]), replacementAlive.ShouldBeTrue() after G1's gate expires with G2 present. Requires the explicit Plan resolution above.
- R-62: delivery inventory, attempt-commit recovery | `CodexStartupDeliveryTests.Crash_after_attempt_commit_recovers_an_untyped_brief` | completeReceiptCount.ShouldBe(1), exact full body and original queue Id after recovery of the committed attempt with no body ever written.
- R-63: S3, eligible recipient needs launch completion to drain the queue | `CodexStartupDeliveryTests.Producer_brief_reaches_an_already_eligible_recipient_whole` | completeReceiptCount.ShouldBe(1) after release and awaited launch flush, without a later turn-end or stranded sweep.
- R-64: S3, busy recipient needs its ordinary turn boundary to drain the queue | `CodexStartupDeliveryTests.Producer_brief_waits_for_busy_recipient_and_arrives_whole` | completeReceiptCount.ShouldBe(1) after the ordinary TurnEnd callback, with no explicit flush or stranded sweep; before callback the row is Pending and workWrites empty.

R-42/R-43/R-44 must register a runtime adapter while durable status is Starting; otherwise absence of an adapter could hide a removed Starting guard. R-50..R-52 seed an observable baseline and keep the composer occupied, preventing unchanged screen-degraded behavior from masquerading as transcript acceptance. R-46 includes replacement generation plus 404/mismatch: no unconditional-kill fallback. R-48 asserts incident, message-attempt and task-relaunch counters independently.

### Guard inventory

Every safety-critical assertion introduced here and each delivery/recovery guard on the changed path is inventoried. Independently bypassable entry points and conditions have separate mappings. None is exempt or left to a happy-path argument. The inherited compatibility suites in R-59/R-60 retain their predecessor guard inventories; this card does not claim mutation qualification of every unrelated feature in those suites.

- G-1: D-1, contract 1; recognizable Codex banner required | PC-1.
- G-2: Contract 1; model row must contain a nonempty selected value | PC-2.
- G-3: D-1/A-2; loading in banner overrides a named footer | PC-3.
- G-4: D-1/A-7; composer empty or exact supported hint only | PC-4.
- G-5: Contract 2; select the active bottom composer, not scrollback | PC-5.
- G-6: Contract 3; adjacent recognized footer required | PC-6.
- G-7: Contract 4/A-1; Starting MCP marker blocks even at 3/3 | PC-7.
- G-8: Contract 4; Booting MCP marker independently blocks | PC-8.
- G-9: Contract 4; MCP startup incomplete is not ready | PC-9.
- G-10: Contract 4; Working/interrupt is not idle startup | PC-10.
- G-11: A-2; tab-to-queue footer is not submittable | PC-11.
- G-12: A-2; queued follow-up inputs block even with an empty composer | PC-12.
- G-13: D-5; uncleared trust cannot pass classification | PC-13.
- G-14: D-5; blocking continue/update picker withholds readiness | PC-14.
- G-15: D-5; sandbox setup/input-disabled withholds readiness | PC-15.
- G-16: D-5; sign-in modal withholds readiness | PC-16.
- G-17: D-2/A-4; owe a full positive settle interval | PC-17.
- G-18: D-2; at least two fresh observations | PC-18.
- G-19: A-3; every blocker/unknown resets the candidate | PC-19.
- G-20: D-2; relevant positive screen churn resets settling | PC-20.
- G-21: Algorithm; no tracker state across calls or generations | PC-21.
- G-22: D-2/A-8; deadline, including zero total wait, never succeeds | PC-22.
- G-23: D-2; trust and polling share the original budget | PC-23.
- G-24: D-3; legacy zero/default/custom threshold cannot release input | PC-24.
- G-25: Algorithm/A-8; bound snapshot awaits | PC-25.
- G-26: Algorithm/A-8; bound startup-input awaits | PC-26.
- G-27: Algorithm; cancellation at entry prevents reading or startup input | PC-27.
- G-28: Algorithm; cancellation during read prevents a trust action | PC-28.
- G-29: Algorithm; recheck cancellation before success | PC-29.
- G-30: Algorithm; exited process never enters ready/startup action | PC-30.
- G-31: Algorithm; recheck exit before success | PC-31.
- G-32: Algorithm/A-8; null/unavailable/throwing snapshots fail closed | PC-32.
- G-33: D-1; one runner snapshot per decision, no mixed frames | PC-33.
- G-34: D-5; historical raw text cannot trigger current trust or readiness | PC-34.
- G-35: D-5; only the complete known trust shape may receive Enter | PC-35.
- G-36: D-5; trust may be accepted only once per launch | PC-36.
- G-37: D-5; startup input resets candidate and requires a new frame | PC-37.
- G-38: D-4; readiness never probes, clears or submits work | PC-38.
- G-39: S2/A-9; runner adapter actually invokes the shared gate | PC-39.
- G-40: S2/A-9; in-process adapter actually invokes the shared gate | PC-40.
- G-41: S3/A-9; Running published only after successful ready | PC-41.
- G-42: S3; WhenIdle enqueue must not deliver into Starting | PC-42.
- G-43: S3; explicit flush must not bypass Starting | PC-43.
- G-44: S3; turn-end flush must not bypass Starting | PC-44.
- G-45: S3/A-9; launch ownership remains held while gate is pending | PC-45.
- G-46: S3/A-8; cleanup cannot kill a replacement generation | PC-46.
- G-47: S3/A-8; failed launch kills before adapter disposal | PC-47.
- G-48: D-6/A-8; pre-input refusal cannot spend BootWedged/relaunch/attempts | PC-48.
- G-49: S3/recovery; interrupted Starting launch reruns the same gate | PC-49.
- G-50: A-9/delivery; complete recipient body is necessary evidence | PC-50.
- G-51: A-9/delivery; old matching receipt cannot confirm a new attempt | PC-51.
- G-52: A-9/delivery; receipt must belong to the intended session | PC-52.
- G-53: S3/recovery; persisted pending row survives service loss and delivers whole | PC-53.
- G-54: S3/recovery; interrupted held composer is completed by Enter only | PC-54.
- G-55: S3/recovery; complete late receipt precedes any new write | PC-55.
- G-56: D-7/A-10; bounded diagnostics exclude prompt and full screen | PC-56.
- G-57: Delivery inventory failure leg; terminal failure notification remains recoverable | PC-57.
- G-58: Delivery inventory before-enqueue loss; fail visibly to the original caller | PC-58.
- G-59: S3/D-7 and resumed cleanup source seam; failure cleanup targets only the attached generation | PC-59.
- G-60: Delivery inventory attempt-commit cut; an untyped interrupted attempt is recovered and delivered whole | PC-60.
- G-61: S3/DLV-1; successful launch drains an early brief to an already eligible recipient | PC-61.
- G-62: S3/DLV-1; the ordinary turn boundary drains the queued brief to a previously busy recipient | PC-62.

### Positive controls

Controls mutate production decisions, never assertions or fixture prerequisites. Boolean/branch/call substitutions below must compile against the implemented seam. Code keeps each predicate independently exercisable and records the actual source symbol with its test evidence before ordinary Review. Single-blocker positives isolate the check under mutation; the two combined incident frames alone would conceal several survivors.

Each row names the exact ClassName.ExactTestMethod for a method-scoped filter. A parameterized method runs all of its arguments; retain per-variant evidence. The classifier/tracker/waiter classes live in Antiphon.Agents.Pty.Tests; the remaining classes live in Antiphon.Tests.

- PC-1: break G-1 by accept a footer/composer pair without the Codex banner; expect `CodexStartupReadinessTests.Missing_codex_banner_is_unknown` red at IsReady.ShouldBeFalse() for the missing-banner layout.
- PC-2: break G-2 by accept an empty model value as selected; expect `CodexStartupReadinessTests.Empty_model_row_is_unknown` red at IsReady.ShouldBeFalse() with only the model value erased.
- PC-3: break G-3 by skip the loading/unknown-model rejection; expect `CodexStartupReadinessTests.Loading_model_never_borrows_the_footer_model` red at IsReady.ShouldBeFalse() with P-3's model changed to loading.
- PC-4: break G-4 by accept any suffix after the prompt glyph; expect `CodexStartupReadinessTests.Composer_content_must_be_empty_or_an_exact_supported_hint` red at IsReady.ShouldBeFalse() for appended text, unknown hint, continuation and pasted chip.
- PC-5: break G-5 by select the first historical matching composer/footer pair; expect `CodexStartupReadinessTests.Historical_composer_above_an_unknown_bottom_is_not_ready` red at IsReady.ShouldBeFalse() for the decoy pair.
- PC-6: break G-6 by skip the footer predicate; expect `CodexStartupReadinessTests.Missing_or_partial_footer_keeps_the_composer_unknown` red at IsReady.ShouldBeFalse() for missing, partial and unknown footer.
- PC-7: break G-7 by omit MarkerA from the blocker check; expect `CodexStartupReadinessTests.Starting_mcp_blocks_every_progress_fraction` red at IsReady.ShouldBeFalse() for 1/3 and 3/3 over otherwise positive P-3.
- PC-8: break G-8 by omit MarkerB from the blocker check; expect `CodexStartupReadinessTests.Booting_mcp_blocks_an_otherwise_ready_layout` red at IsReady.ShouldBeFalse().
- PC-9: break G-9 by omit the incomplete-startup rejection; expect `CodexStartupReadinessTests.Incomplete_mcp_startup_is_a_blocker` red at IsReady.ShouldBeFalse().
- PC-10: break G-10 by omit the Working/interrupt rejection; expect `CodexStartupReadinessTests.Working_or_interrupt_state_withholds_readiness` red at IsReady.ShouldBeFalse() for each isolated busy indicator.
- PC-11: break G-11 by allow the queue-mode footer as an idle footer; expect `CodexStartupReadinessTests.Queue_mode_footer_blocks_a_loaded_model` red at IsReady.ShouldBeFalse().
- PC-12: break G-12 by omit the queued-follow-up rejection; expect `CodexStartupReadinessTests.Queued_follow_up_blocks_an_empty_composer` red at IsReady.ShouldBeFalse().
- PC-13: break G-13 by omit the trust-modal rejection; expect `CodexStartupReadinessTests.Uncleared_trust_is_not_a_ready_composer` red at IsReady.ShouldBeFalse().
- PC-14: break G-14 by omit the blocking-continue/update rejection; expect `CodexStartupReadinessTests.Blocking_update_is_distinct_from_a_static_notice` red at IsReady.ShouldBeFalse() for picker; unmodified static notice still passes.
- PC-15: break G-15 by omit the sandbox/input-disabled rejection; expect `CodexStartupReadinessTests.Sandbox_setup_and_input_disabled_are_blockers` red at IsReady.ShouldBeFalse() for each isolated phrase.
- PC-16: break G-16 by omit the sign-in rejection; expect `CodexStartupReadinessTests.Sign_in_modal_is_not_ready` red at IsReady.ShouldBeFalse().
- PC-17: break G-17 by compare elapsed settle against zero instead of the configured settle; expect `CodexReadyTrackerTests.Ready_requires_the_full_settle_interval` red at Ready.ShouldBeFalse() at settle-minus-one.
- PC-18: break G-18 by cache the first non-null snapshot in a local variable and reuse it in subsequent waiter iterations instead of invoking the snapshot delegate again, while leaving tracker timing unchanged; expect `CodexReadyWaitTests.One_observation_never_establishes_readiness` red at gate.IsCompleted.ShouldBeFalse() after advancing through the full settle. The unmutated second read is held and released in finally. Removing a redundant count check alone is not an acceptable mutation.
- PC-19: break G-19 by retain candidateSince when classification is not ready; expect `CodexReadyTrackerTests.Every_blocker_and_incomplete_frame_restarts_settling` red at Ready.ShouldBeFalse() at the old candidate deadline.
- PC-20: break G-20 by stop comparing the normalized relevant region; expect `CodexReadyTrackerTests.Relevant_layout_changes_restart_settling` red at Ready.ShouldBeFalse() after model/hint/footer changes at settle-minus-one.
- PC-21: break G-21 by reuse the preceding wait's settled tracker on the next call; expect `CodexReadyWaitTests.A_new_wait_and_generation_start_without_a_candidate` red at secondWait.IsCompleted.ShouldBeFalse() before its own settle.
- PC-22: break G-22 by return true on the deadline-exhaustion branch; expect `CodexReadyWaitTests.Deadline_or_zero_total_budget_never_authorizes_input` red at result.ShouldBeFalse() for permanent blockers and ready-at-expiry.
- PC-23: break G-23 by restart the monotonic deadline after the trust response; expect `CodexReadyWaitTests.Trust_consumes_the_original_total_budget` red at result.ShouldBeFalse() when trust leaves less than a settle.
- PC-24: break G-24 by return true when BootStatusMaxWait expires or is zero; expect `RunnerCodexAdapterReadyTests.Legacy_boot_threshold_never_bypasses_the_positive_gate` red at ready.IsCompleted.ShouldBeFalse() while model/MCP remains blocked.
- PC-25: break G-25 by pass only the caller token to the snapshot delegate; expect `CodexReadyWaitTests.A_stalled_snapshot_finishes_at_the_gate_deadline` red at gate.IsCompleted.ShouldBeTrue() at virtual deadline with no caller cancellation.
- PC-26: break G-26 by pass only the caller token to the trust-write delegate; expect `CodexReadyWaitTests.A_stalled_trust_write_finishes_at_the_gate_deadline` red at gate.IsCompleted.ShouldBeTrue() at virtual deadline.
- PC-27: break G-27 by ignore the caller token and cancellation check at gate entry; expect `CodexReadyWaitTests.Cancellation_at_entry_prevents_reads_and_input` red at readCalls.ShouldBe(0) and writes.ShouldBeEmpty().
- PC-28: break G-28 by omit cancellation enforcement between snapshot and trust action; expect `CodexReadyWaitTests.Cancellation_during_read_prevents_trust_input` red at writes.ShouldBeEmpty() when the snapshot delegate cancels the caller then returns trust.
- PC-29: break G-29 by omit the final cancellation check after the snapshot completes; expect `CodexReadyWaitTests.Cancellation_on_the_final_snapshot_cannot_return_ready` red at Should.ThrowAsync<OperationCanceledException>(gate).
- PC-30: break G-30 by omit the pre-read exit check; expect `CodexReadyWaitTests.Exited_process_never_receives_startup_input` red at writes.ShouldBeEmpty() and result.ShouldBeFalse().
- PC-31: break G-31 by omit the final exit check; expect `CodexReadyWaitTests.Exit_on_the_final_snapshot_cannot_return_ready` red at result.ShouldBeFalse().
- PC-32: break G-32 by reuse the last good screen after null/exception; expect `CodexReadyWaitTests.Unavailable_or_failed_snapshot_cannot_reuse_a_ready_frame` red at no successful result; snapshot exception remains the injected exception.
- PC-33: break G-33 by read raw and rendered data in two separate GetSnapshotAsync calls; expect `RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision` red at snapshotReads.ShouldBe(decisions) on an alternating-frame script.
- PC-34: break G-34 by pass cumulative RawOutput into the trust predicate in addition to current screen; expect `RunnerCodexAdapterReadyTests.Raw_history_cannot_authorize_trust_input` red at writes.ShouldBeEmpty() when raw trust overlays a current update picker.
- PC-35: break G-35 by weaken the two-label trust predicate to the question alone; expect `RunnerCodexAdapterReadyTests.Trust_acceptance_requires_both_current_labels` red at trustEnters.ShouldBe(0) when Yes-continue is absent.
- PC-36: break G-36 by remove the accepted-trust latch; expect `RunnerCodexAdapterReadyTests.Repeated_trust_frames_receive_only_one_enter` red at trustEnters.ShouldBe(1) across repeated current trust frames.
- PC-37: break G-37 by continue the pre-trust candidate after the Enter; expect `CodexReadyWaitTests.Trust_response_forces_a_new_snapshot_and_full_settle` red at ready.IsCompleted.ShouldBeFalse() until the post-action full settle.
- PC-38: break G-38 by add one WriteAsync("ready-probe", ct) in the readiness loop; expect `RunnerCodexAdapterReadyTests.Readiness_has_no_input_except_the_known_trust_enter` red at writes.ShouldBeEmpty() and resizeCalls.ShouldBe(0) on ordinary startup.
- PC-39: break G-39 by replace RunnerCodexAdapter.WaitForReadyAsync body after EnsureStarted with return true; expect `RunnerCodexAdapterReadyTests.Delayed_mcp_after_loading_withholds_runner_ready` red at ready.IsCompleted.ShouldBeFalse() at the loading checkpoint.
- PC-40: break G-40 by replace CodexAdapter.WaitForReadyAsync delegate call with Task.FromResult(true); expect `CodexAdapterLocalShellTests.Loaded_layout_release_is_required_by_the_in_process_adapter` red at ready.IsCompleted.ShouldBeFalse() while the child holds Loading.
- PC-41: break G-41 by move session.Status=Running and its save before WaitForReadyOrThrowAsync; expect `CodexStartupDeliveryTests.Starting_session_is_not_published_running_before_positive_ready` red at stored.Status.ShouldBe(SessionStatus.Starting) at the held startup barrier.
- PC-42: break G-42 by omit IsAcceptingInputAsync from the enqueue-inline condition; expect `CodexStartupDeliveryTests.Enqueue_during_boot_keeps_the_brief_pending_without_attempt` red at row.DeliveryAttempts.ShouldBe(0) and workWrites.ShouldBeEmpty().
- PC-43: break G-43, after Plan scopes the missing admission protection, by omit the acceptance check on FlushSessionAsync's path; expect `CodexStartupDeliveryTests.Explicit_flush_during_boot_types_nothing` red at workWrites.ShouldBeEmpty() after the explicit flush. Use a complete positive frame still owing settle, so no blocker masks the removed check.
- PC-44: break G-44, after Plan scopes the missing admission protection, by omit the acceptance check on OnTurnEndAsync's path; expect `CodexStartupDeliveryTests.Turn_end_during_boot_types_nothing` red at workWrites.ShouldBeEmpty() after the boundary callback. Use the same positive-but-unsettled boundary. A single shared repair must return for consolidation of G-43/G-44, not run duplicate mutations against one guard.
- PC-45: break G-45 by remove the owned-session entry immediately after scheduling launch; expect `CodexStartupDeliveryTests.Launch_ownership_spans_the_entire_startup_gate` red at launchQueue.Owns(sessionId).ShouldBeTrue() at held startup.
- PC-46: break G-46 by replace KillGenerationAsync with unconditional KillAsync; expect `CodexStartupDeliveryTests.Readiness_failure_targets_only_the_accepted_generation` red at killGenerationCalls.ShouldBe([acceptedGeneration]) and replacementAlive.ShouldBeTrue().
- PC-47: break G-47 by omit the generation kill before Dispose; expect `CodexStartupDeliveryTests.Readiness_failure_kills_before_disposal` red at lifecycle.ShouldBe(["KillGeneration", "Dispose"]).
- PC-48: break G-48 by on not-ready failure increment the bound task BootWedgeRelaunchCount; expect `CodexStartupDeliveryTests.Readiness_timeout_does_not_consume_delivery_or_boot_wedge_attempts` red at BootWedgeRelaunchCount.ShouldBe(0), DeliveryAttempts.ShouldBe(0), BootWedged incidents empty.
- PC-49: break G-49 by skip WaitForReadyOrThrowAsync in ResumeInterruptedLaunchAsync; expect `CodexStartupDeliveryTests.Restart_during_boot_reverifies_before_delivering` red at workWrites.ShouldBeEmpty() while the resumed frame remains Loading.
- PC-50: break G-50 by make PromptSubmissionMatch's complete-body predicate accept a task marker alone; expect `CodexStartupDeliveryTests.A_marker_or_prefix_receipt_does_not_confirm_the_brief` red at DeliveryVerdict.ShouldNotBe(Delivered) and completeReceiptCount.ShouldBe(0) before full receipt.
- PC-51: break G-51 by remove the confirmation sequence/time floor; expect `CodexStartupDeliveryTests.Old_receipt_does_not_confirm_this_attempt` red at DeliveryVerdict.ShouldNotBe(Delivered) with the same full body below the floor.
- PC-52: break G-52 by remove the transcript confirmation session filter; expect `CodexStartupDeliveryTests.Wrong_session_receipt_does_not_confirm_this_attempt` red at DeliveryVerdict.ShouldNotBe(Delivered) with the full body only in another session.
- PC-53: break G-53 by exclude the pending row from FlushStrandedQueuesAsync discovery; expect `CodexStartupDeliveryTests.Committed_brief_is_recovered_after_service_recreation` red at completeReceiptCount.ShouldBe(1) after recovery; original row Id/Sequence unchanged.
- PC-54: break G-54 by retype the body instead of the Enter-only recovery branch; expect `CodexStartupDeliveryTests.Interrupted_typed_brief_recovers_with_enter_only` red at bodyWrites.ShouldBe(1) and recoveredWrites.ShouldBe(["\r"]).
- PC-55: break G-55 by skip late confirmation before interrupted-attempt recovery; expect `CodexStartupDeliveryTests.Accepted_prompt_before_verdict_commit_is_not_typed_again` red at writesAfterRestart.ShouldBeEmpty() and completeReceiptCount.ShouldBe(1).
- PC-56: break G-56 by include the rendered screen in the structured timeout log; expect `RunnerCodexAdapterReadyTests.Timeout_diagnostics_name_blocker_without_prompt_or_screen` red at capturedLogs.ShouldNotContain(secretSentinel).
- PC-57: break G-57 by skip pending DeliveryFailure notification recovery in RemindUnacknowledgedFailuresAsync; expect `ReceiptFailureDeliveryTests.Caller_failure_obligation_survives_persistence_cuts` red at RemindUnacknowledgedFailuresAsync(...).ShouldBe(1) after a durable failure cut; the ordinary method then requires the complete caller receipt.
- PC-58: break G-58 by skip creating the DeliveryFailure notification in FailNeverStartedAsync; expect `CodexStartupDeliveryTests.Enqueue_failure_is_reported_to_the_original_caller` red at failureObligation.ShouldNotBeNull() followed by exactly one complete caller UserPrompt.
- PC-59: break G-59, after the scoped repair lands, by remove the attached-generation argument from the resumed catch's KillAndDisposeAsync call (the optional-null overload still compiles); expect `CodexStartupDeliveryTests.Resumed_readiness_failure_cannot_kill_replacement` red at unconditionalKills.ShouldBe(0). Restore must also prove G2 remains alive and only G1 was targeted.
- PC-60: break G-60 by skip the interrupted-attempt revert/requeue branch when the recovery probe finds no composer body and no matching receipt; expect `CodexStartupDeliveryTests.Crash_after_attempt_commit_recovers_an_untyped_brief` red at completeReceiptCount.ShouldBe(1) after the bounded recovery pass. Returning without recovery is the compiling defect; a fixture timeout is not red.
- PC-61: break G-61 by omit the final _messageQueue.FlushSessionAsync call in LaunchInteractiveProcessAsync; expect `CodexStartupDeliveryTests.Producer_brief_reaches_an_already_eligible_recipient_whole` red at completeReceiptCount.ShouldBe(1) after awaited launch completion. Keep unrelated recovery workers disabled so they cannot mask the missing launch flush.
- PC-62: break G-62 by return from SessionMessageQueueService.OnTurnEndAsync before its queued-message delivery path; expect `CodexStartupDeliveryTests.Producer_brief_waits_for_busy_recipient_and_arrives_whole` red at completeReceiptCount.ShouldBe(1) after awaited ordinary TurnEnd. No fallback explicit flush or sweep is permitted in this method.

Mutation reports **break, exact intended red assertion, restore, freshly rebuilt green** for every PC after land. Code implements/runs ordinary V/R; separate ordinary Review judges tests and pending PCs before land. A zero-test run, compiler error, fixture exception or generic timeout is not red. Tests with a deliberately stalled operation assert completion at virtual deadline, then cancel/release and await their fixture in finally; no hanging task survives a red assertion. Receipt tests assert row existence/count/body directly after bounded operations, rather than turning a missing receipt into an uninformative polling timeout.

No batching savings are assumed: most readiness controls touch the same classifier/waiter files. Independent different-file/method controls may be batched only with separate exact-method evidence and restored-green runs. SourceLanding Mutation uses the exact landed snapshot and external evidence root; no commit/push from that snapshot.

### Out of scope

- Additional context/shortcut-only positive footer family and measured narrow/wrapped layouts: no complete positive capture in the inspected evidence. R-6 rejects them for now; Plan must resolve contract item 3 before Code.
- Production repair of resumed-launch generation fencing is outside this documentation stage. Plan must resolve its scope because the current optional-null cleanup contradicts the stated D-7 assumption. The required test and PC remain inventoried, not waived.
- Production repair of explicit-flush/turn-end Starting admission is likewise a Plan scope decision. Both negative ordinary cases remain required; settle the actual guard topology before Code and Mutation.
- Native Codex input-liveness qualification, active sentinel/clear probes, real CLI canaries and paid cold-start census: D-4/D-7 and this brief explicitly require offline state-machine coverage. Passive ready never certifies a provider reply.
- Original geometry qualification: archived replay geometry is declared, not inferred as original metadata. Captured positive row relationships plus synthetic width boundaries do not certify live resize behavior.
- Manual Mode.Now retains its existing explicit Starting conflict; this card changes no send-mode API. The early-message requirement here is the real delegation WhenIdle queue path, tested before ready, after ready, busy and already eligible.
- Queue recovery redesign, new incident types, retry-limit changes, provider homes/auth/config, MCP configuration, CLI upgrade, frontend/E2E and production deployment are excluded by D-7. If S3 reveals a production bypass, return to Plan before changing queue semantics.
- Herdr restart recovery remains its existing refusal/clean-relaunch policy; the Starting-attach recovery test is on the PtyHost Codex delegation path. Herdr launch/adopt/exit compatibility still runs for every existing kind.

### Cost

All numbers below are **estimated minutes**, not measurements from this documentation stage. The three Plan resolutions are outside the execution floor; after they are resolved, the floor below budgets the current 62-control design. If the queue repair uses one shared guard, consolidate and recalculate before Code. No source build or application test was run here. Artifact replay/hash/link/format checks are documentation validation, not V/R.

Ordinary Code floor (Final profile; no Interim):

| Component | Selection | Minutes |
|---|---|---:|
| Setup/build | build both test graphs into bin-card0574/; stage fake children/resources; lazy Postgres/template preflight | 5 |
| Shared gate | four named Pty classes (classifier/tracker/waiter/MCP marker) | 1 |
| Unit lane | Antiphon.Tests Category=Unit, including runner-ready/config/submit/done | 3 |
| Local shell | CodexAdapterLocalShellTests | 4 |
| Herdr | HerdrAlwaysOnChannelParityTests, including all partial-class methods and all launch-kind arguments | 6 |
| New application delivery | CodexStartupDeliveryTests, all producer/startup/cut/receipt variants and owned-child teardown | 9 |
| Existing delivery/failure | SessionMessageQueueBootWedgeTests, SessionMessageQueueDeliveryVerificationTests, SessionMessageQueueInterruptedAttemptTests, ReceiptFailureDeliveryTests, plus named launch failure/ownership tests | 7 |
| **Code setup/build + ordinary V/R floor** | **5 + 30** | **35** |

Build once per graph, then use these selections. Run projects sequentially. Fresh results directories are generated per invocation; inspect actual TRX method names, counts and outcomes, including every parameterized Herdr kind. Missing/skip/zero counts for required coverage do not satisfy acceptance.

~~~powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-card0574/ --nologo
dotnet build tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0574/ --nologo

$c574PtyResults = Join-Path '.antiphon' ('c574-pty-' + [Guid]::NewGuid().ToString('N'))
dotnet run --project tests/Antiphon.Agents.Pty.Tests --no-build --property:OutputPath=bin-card0574/ -- --treenode-filter '/*/*/(CodexStartupReadinessTests*)|(CodexReadyTrackerTests*)|(CodexReadyWaitTests*)|(CodexMcpBootTests*)/*' --report-trx --report-trx-filename run.trx --results-directory $c574PtyResults

$c574UnitResults = Join-Path '.antiphon' ('c574-unit-' + [Guid]::NewGuid().ToString('N'))
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-card0574/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename run.trx --results-directory $c574UnitResults

$c574AffectedResults = Join-Path '.antiphon' ('c574-affected-' + [Guid]::NewGuid().ToString('N'))
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-card0574/ -- --treenode-filter '/*/*/(CodexAdapterLocalShellTests*)|(HerdrAlwaysOnChannelParityTests*)|(CodexStartupDeliveryTests*)|(SessionMessageQueueBootWedgeTests*)|(SessionMessageQueueDeliveryVerificationTests*)|(SessionMessageQueueInterruptedAttemptTests*)|(ReceiptFailureDeliveryTests*)|(AgentSessionLaunchQueueOwnershipTests*)/*' --report-trx --report-trx-filename run.trx --results-directory $c574AffectedResults

$c574FailureResults = Join-Path '.antiphon' ('c574-launch-failure-' + [Guid]::NewGuid().ToString('N'))
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-card0574/ -- --treenode-filter '/*/*/AgentSessionLaunchFailureTests/Launch_timeout_is_infrastructure_but_requested_cancellation_has_no_failure_outcome' --report-trx --report-trx-filename run.trx --results-directory $c574FailureResults
~~~

Code must mark new non-process classifier/tracker/waiter/runner tests Unit and new DB/process tests Integration. Record fixture resources found at runtime. Do not run the whole Antiphon.Tests assembly for this bounded change. Run test-duration-tripwire.ps1 against fresh TRX and assess slow methods; do not widen timeouts/assertions or add retries to hide red. Reproduce suspected inherited failing methods at the base, not the full assembly.

Mutation floor:

| Component | Count and per-control red / restore / fresh green | Minutes |
|---|---|---:|
| Snapshot setup/build and discovery | both projects, exact landed SHA, actual method discovery | 4 |
| Initial unmutated exact-method green checks | all 62 named methods; parameter expansions retained | 8 |
| PC-1..PC-39 plus PC-56 | 40 × (0.75 + 0.05 + 0.75), rebuild costs included | 62 |
| PC-40 in-process shell | 1 × (1.50 + 0.05 + 1.50) | 3.05 |
| PC-41..PC-55 plus PC-57..PC-62 | 21 × (2.00 + 0.05 + 2.00), DB/worker cost included | 85.05 |
| Restoration/output inventory/evidence report | restored source hashes and awaited children | 3 |
| **Mutation floor** | **4 + 8 + 62 + 3.05 + 85.05 + 3** | **165.10** |
| **Total verification floor** | **Code setup/build 5 + ordinary V/R 30 + every PC and Mutation setup/report 165.10** | **200.10** |

The estimates include all 62 red/restore/green cycles; none is budgeted as optional. No batch savings are credited (0 minutes), because overlapping readiness mutations are not independent. Deterministic virtual time avoids at least 20 full 60-second startup-timeout cases: 20 minutes of real waits become under 1 minute of gate execution, an estimated saving of at least 19 minutes before build overhead. Method-scoped PCs also avoid class/suite reruns, but no additional numeric wall-time saving is claimed without current measurements. Paid CLI launches: zero by design, not skipped acceptance.

The exact invocation for PC-39, for example, is below. Every other PC uses its own row's exact class and method in the same form, choosing the project specified above; never substitute a whole class filter. Omit --no-build during mutated and restored-green runs. The assigned external evidence root owns result directories.

~~~powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0574-pc/ -- --treenode-filter '/*/*/RunnerCodexAdapterReadyTests/Delayed_mcp_after_loading_withholds_runner_ready'
~~~

Inventory only this task's alternate output paths before running; cleanup validates resolved paths remain under the worktree and removes only recorded producer-owned bin-card0574 / bin-card0574-pc outputs. Use forward slashes for OutputPath. Commit before long ordinary runs, freeze source during each run, and report the tested commit. Mutation refreshes restored-file timestamps or rebuilds explicitly; restored text with a stale mutated DLL is not green.

Pre-handoff audit: test/fixture bodies read; **guards=62, mapped=62, missing=0, duplicate PC mappings=0** in the declared entry-point inventory. Each control specifies a compiling mutation recipe, exact method and intended assertion. Execution readiness is withheld for PC-43/PC-44/PC-59 until Plan scopes the missing guards; a shared queue guard requires mapping consolidation. Numeric floor is 200.10 estimated minutes. The positive-footer evidence, resumed-cleanup scope and queue-admission scope seams require **Plan**, then **TestDesign** to close this audit, before Code. No placeholder case or uncosted control is delegated to Code. This completed TestDesign report records explicit prerequisites; it does not claim an executable implementation or green tests.

Documentation validation completed: landed-plan prefix unchanged; 62 unique PC methods and one-to-one numeric mappings; seven local Markdown links resolve; all three rendered-screen hashes, archived-source hashes and 30-row counts match; no template placeholders; git diff --check passes. No application build, test run or native CLI launch was performed.
