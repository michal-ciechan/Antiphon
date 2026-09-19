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
