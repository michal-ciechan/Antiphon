# CARD-0382: refuse unsafe Grok rules on Windows argv

Status: Plan complete; next stage TestDesign. Scope: the explicitly permitted fail-closed alternative. This prevents the bad launch; it does **not** make a multiline orchestrator bundle launch successfully. Do not report the card's successful-delivery/detection acceptance criteria as met by this mitigation.

Repository evidence: `dde6e057f7a987cfaf5054ba851e15283b8e7835`. Read the live descriptions of CARD-0382 (GitHub #41) and CARD-0395 on 2026-09-06. No production process, profile, wrapper, or board state was changed during planning.

## Decisions

### D-1. Use the authorized fail-closed alternative

On Windows, refuse a Grok standing-instructions payload containing CR, LF, or NUL, or more than **4,096 UTF-16 code units**, before it is appended to launch arguments. Check the complete resolved argv again before launching. Keep `InstructionBundleComposer.EnsureWithinCommandLineBudget` as an additional total-size guard; its existing 30,000-character default is not newline validation.

The 4,096 limit is a conservative application policy, not a claimed Windows per-token limit or a proof that arbitrary operator wrappers preserve every character. Spaces alone are allowed, and allowed text is passed unchanged. Never normalize newlines into spaces, truncate, encode text into an opaque argv blob, omit the rules and continue, or select another provider/backend automatically. The guard applies to Windows Grok on both Herdr and PtyHost, independent of whether the executable is Grok or a configured wrapper. Other providers and non-Windows launches keep their present behavior.

Reason: no verified native file/env channel equivalent to the existing per-session append has been established for the installed binary. A short instruction telling the model to read a file would change this from system-prompt delivery into model-dependent tool use. That is not proof of full delivery.

### D-2. What the CLI evidence actually establishes

The installed executable resolves to `C:\Users\lndco\.grok\bin\grok.exe`. `grok.exe --version` returned `grok 1.0.13 (5e9a58528b76) [stable]`; `grok.exe --help` exposes `--rules <RULES>` as extra system-prompt rules, with no rules-file flag or rules environment variable advertised. `--prompt-file` is a single-turn user-prompt input, not this channel.

The actual upstream TUI implementation was traced, rather than assuming response-file support:

| Upstream source, pinned at public revision `72a61251fcffb464bcc687aeb5a998e5a98ec0c9` | Evidence |
|---|---|
| [TUI parser, `app/cli.rs:526`](https://github.com/xai-org/grok-build/blob/72a61251fcffb464bcc687aeb5a998e5a98ec0c9/crates/codegen/xai-grok-pager/src/app/cli.rs#L526) | `rules` is an optional string; the flag also has an append-system-prompt alias. No file parser or env binding is attached. |
| [TUI startup, `app/mod.rs:833`](https://github.com/xai-org/grok-build/blob/72a61251fcffb464bcc687aeb5a998e5a98ec0c9/crates/codegen/xai-grok-pager/src/app/mod.rs#L833) and [ACP metadata, `acp/mod.rs:483`](https://github.com/xai-org/grok-build/blob/72a61251fcffb464bcc687aeb5a998e5a98ec0c9/crates/codegen/xai-grok-pager/src/acp/mod.rs#L483) | The string is cloned into connection options and then into metadata. |
| [Runtime prompt construction, `mvp_agent/mod.rs:1104`](https://github.com/xai-org/grok-build/blob/72a61251fcffb464bcc687aeb5a998e5a98ec0c9/crates/codegen/xai-grok-shell/src/agent/mvp_agent/mod.rs#L1104) | The metadata string is appended inside the human-rules block. No `@path` read occurs. |
| [Config overlay, `env_overlay.rs:9`](https://github.com/xai-org/grok-build/blob/72a61251fcffb464bcc687aeb5a998e5a98ec0c9/crates/codegen/xai-grok-config/src/env_overlay.rs#L9) and [rule-directory limitation, `inspect/mod.rs:551`](https://github.com/xai-org/grok-build/blob/72a61251fcffb464bcc687aeb5a998e5a98ec0c9/crates/codegen/xai-grok-shell/src/inspect/mod.rs#L551) | `GROK_CONFIG_PATH` loads JSON/TOML configuration, not markdown. `extra_rule_dirs` only reclassifies already discovered files; arbitrary-directory discovery is explicitly not wired. It cannot justify the proposed rules-file transport. |

Version boundary: public `SOURCE_REV` is `a549186d9d39311f2d3ee4208db62af8c65aa476`, not the installed binary's monorepo revision. None of the 41 public SOURCE_REV revisions inspected matched `5e9a58528b76`. Thus this is a source trace plus installed CLI help evidence, **not** a binary-matched or wire-level canary proving every possible transport absent in 1.0.13. This uncertainty is a reason to refuse unsupported transport, not to ship a guessed `--rules @path` syntax. No model request or headed Grok probe was run for this Plan.

### D-3. Refusal must be specific and occur before side effects

Use stable problem code `grok_rules_argv_unsafe`. The diagnostic identifies the agent/task/session, the offending flag, the reason (`line_break`, `nul`, `token_too_long`, or `missing_value`), and the character count/limit when relevant. It must not include the rules text, environment values, a command line, or a suggested unsafe override.

Named/card composition should throw a `ConflictException` with that code before launch queue insertion. Dispatcher handling must settle a composition refusal through the existing failed-dispatch path, with no brief queued and no timeout-based retry. Audit the post-commit window explicitly: `AgentTaskDispatcher` currently persists the claimed session/task before it builds the launch spec at line 2959. Its outer catch at 590 already calls `FailAndNotifyAsync` (2038), but labels the error as occurring before a session existed. Add a narrow rules-refusal branch that terminalizes the newly claimed, unlaunched session with the correct reason before normal task failure/notification; preserve the generic handling for other failures. Do not assume task settlement automatically repairs the session row. A rejected launch must leave no `Starting` session or `Dispatched` task waiting for the watchdog; record system-request termination through the existing lifecycle helpers where a session row exists. Notification of the parent remains valid; the forbidden queue action is the child's task brief.

Runner refusals use the same problem code and HTTP 409 before creating/replacing a `RunnerSession`, starting a PtyHost, connecting to Herdr, allocating/reusing a pane, writing a launch script, or typing. Implement a dedicated runner exception and explicit mapping in `POST /sessions`; an uncaught `ArgumentException` is not a suitable response contract.

### D-4. One validation policy, early checks plus a launch backstop

Add a small pure `GrokRulesArgvPolicy` in `src/Antiphon.SessionRunner.Contracts/`, which both server and runner already reference. It owns the 4,096 constant, reason codes, payload validation, and recognition of `--rules value`, `--rules=value`, `--append-system-prompt value`, and its equals form. Check every occurrence, including duplicates; a later good value must not hide an earlier bad one. Respect the child CLI's option terminator; never assume the resolved executable's basename determines its provider. The caller supplies the known Grok kind and Windows decision; tests can exercise both platform decisions without global environment mutation.

Call payload validation at the two composition sites before their `AddRange`, after channel rendering on the named-agent path. Call argv validation at `AgentSessionService.BuildRuntimeLaunchSpecAsync` after session identity/other overlays and before adapter start. This catches unsafe profile/definition args that did not originate in bundle composition. Keep model selection, identity selection, composition order, stamps, and the full command-line budget intact.

The runner checks again using the runner OS and the request's Grok transcript/kind identity. For Herdr it must inspect the **effective** arguments, including the whole-argument `$env:NAME` / `${env:NAME}` substitution currently performed by `HerdrLaunchScript.TryResolveEnvToken`. Reuse that resolution logic for validation; checking only the short token would let a multiline env value bypass the guard. Resolve each complete argv element before flag scanning, so equals forms and an env-supplied flag are also covered. The guard's early check and the script must use the same substitution semantics. This does not authorize adding a new general env expansion facility.

The process-boundary contract is the invariant, not an assertion that `CreateProcessW` itself categorically cannot represent LF. The card establishes a failing launch chain. The present PowerShell source-string test establishes escaping in a script, not the native child argv produced by all subsequent parsers.

### D-5. CARD-0395 owns the durable-file implementation

This mitigation writes **no rules file**, so it introduces no second convention. Reserve one shared future convention: `<SessionLogPath>\instructions\grok\<Antiphon-session-id:N>\rules.md`, UTF-8 without BOM, with the exact final rendered composition (no extra front matter or truncation). A future file transport must materialize it on the runner host, atomically replace it on an actual launch/resume, retain it for the session/resume lifetime, and expose the same absolute path to CARD-0395's reminder mechanism. It must not use the short-lived Herdr `.launch.ps1`, a shared cwd `AGENTS.md`, or one global `.grok/rules/` entry that other agents would load.

That is a design reservation for CARD-0395, not a format already implemented or supported by Grok. Confirm a native append mechanism with the installed binary and a captured request before enabling it. If a future solution uses model-driven re-reading, measure and document the changed delivery semantics. The upstream source linked above also says rules are creation-only and not refreshed on resume; CARD-0395 must measure the installed version instead of inheriting the current blanket compaction/resume assurances. Compaction events, hooks, typed reminders, and session-resume identity changes are outside this mitigation.

### D-6. Operational impact and acceptance boundary

Every composed attachment normally contains newlines, so this change refuses most **new Windows Grok delegates**, as well as the standing orchestrator in the reproduction. Already-running sessions and warm reuse with no process launch are not altered. This is an availability tradeoff explicitly allowed by the brief's fail-closed alternative; it is not a transparent repair. Do not special-case `fakegrok` or silently strip bundles to make tests or deployment appear healthy.

| Card acceptance | Result of the selected alternative |
|---|---|
| Full composed orchestrator rules reach Grok without clap failure | Still outstanding: this launch is refused before Grok starts. |
| Herdr detects Grok well before its timeout; pane stays open | Still outstanding for the refused bundle. No pane is touched and no detect timer is started. |
| Multiline composed rules never become a Windows native argv argument | Required red-then-green automated proof below. |

The next stage can design tests for this authorized mitigation without a new user answer. The caller must keep the successful-launch work visible on CARD-0382/CARD-0395 and must not close those acceptance items on refusal-only evidence. If restoring bundled Windows Grok availability is required in this same change, return to investigation of a measured file/env transport; do not widen Code into an unplanned provider integration.

## Ground truth in Antiphon

Line anchors below refer to the repository evidence commit; linked files are the implementation owners.

| Assumption or question | Actual composition/launch code |
|---|---|
| `AgentControlService` directly appends standing rules | It now calls `ComposeForAgentAsync` at [AgentControlService.cs:262](../../../server/Application/Services/AgentControlService.cs), then passes `composition.ExtraArgs` into `AgentLaunchOptions` at line 272. |
| Where the actual standing text is formed | [AgentSessionLaunchComposer.cs:95](../../../server/Application/Services/AgentSessionLaunchComposer.cs): attachments, reply style, prompt append; channel rendering at 104; budget at 109; Grok `--rules` plus **rendered text** at 114-116. Card and orchestration launch callers also use this composer (`CardService:1239`, `OrchestratorService:939`). |
| Where delegates differ | [AgentTaskDispatcher.cs:3496](../../../server/Application/Services/AgentTaskDispatcher.cs), `ComposeDelegateArgs`: role/attached bundles at 3564, budget at 3569, raw Grok payload at 3579. Both registry and profile branches call it (`BuildLaunchSpec:3385`, `BuildLaunchSpecAsync:3428`). |
| Does the existing guard detect line breaks? | [InstructionBundleComposer.cs:151](../../../server/Application/Services/InstructionBundleComposer.cs) counts UTF-16 characters plus argument overhead. It performs no CR/LF validation. Composition preserves custom text verbatim; separators are `\n\n`. Default budget is 30,000 in `DelegationSettings:286`. |
| Could a resolver fix the payload automatically? | [AgentRegistry.cs:113](../../../server/Application/Services/AgentRegistry.cs) and [AgentTuiLaunchResolver.cs:394](../../../server/Application/Services/AgentTuiLaunchResolver.cs) append `ExtraArgs` literally. The managed profile supplies its own executable/base arguments. [AgentLaunchSpec.cs](../../../server/Application/Dtos/AgentLaunchSpec.cs) carries argv as a list; it has no rules-file payload. |
| One server process-start funnel | [AgentSessionService.cs:1450](../../../server/Application/Services/AgentSessionService.cs): `BuildRuntimeLaunchSpecAsync` adds identity, Claude overlay, cwd/session/backend/Herdr data, and the API-key tripwire. Its three callers lead to adapter starts for card, fresh, and resume. |
| Last transport boundary | [SessionRunnerRuntime.cs:94](../../../src/Antiphon.SessionRunner/SessionRunnerRuntime.cs) validates requests, then constructs/registers the session and starts the selected child lane. Herdr writes the fully quoted args at [HerdrLaunchScript.cs:65](../../../src/Antiphon.SessionRunner/HerdrLaunchScript.cs); [HerdrPaneChild.cs:578](../../../src/Antiphon.SessionRunner/HerdrPaneChild.cs) writes the script, types it, polls detection, then deletes the script on success. |
| Existing test proves safe native transport | [HerdrLaunchShapeTests.cs:21](../../../tests/Antiphon.SessionRunner.Tests/HerdrLaunchShapeTests.cs) asserts script text/BOM containing the multiline rules. It never inspects Grok argv. Keep its generic PowerShell escaping coverage, but rename/reword it and use a non-rules generic fixture so it does not advertise the forbidden Grok launch as supported. |
| Timeout closes the pane in the current checkout | The card records the older observed failure. CARD-0383 has since added idle-shell retention in `HerdrPaneChild.TryKeepIdleShellPaneOnDetectTimeoutAsync:989`. Preserve that and native-session resume guards. This fix acts before either behavior is needed. |

## Code slices

| Slice | Files and concrete change | Tests |
|---|---|---|
| S1: shared policy | New `src/Antiphon.SessionRunner.Contracts/GrokRulesArgvPolicy.cs`; add a server wrapper in `server/Application/Services/GrokLaunchArgs.cs` that maps violations to `ConflictException`. No configuration escape hatch. | New `GrokRulesArgvPolicyTests`: payload boundaries, flag forms, duplicates, platform/kind gating, contents absent from errors. |
| S2: composition and settlement | `AgentSessionLaunchComposer.cs`, `AgentTaskDispatcher.cs`, `AgentSessionService.cs`: early payload checks and final argv guard. Handle the dispatcher post-commit refusal immediately through existing failure/settlement services. Verify named, card-backed, profile, registry, cold-dispatch and relaunch paths. | New `GrokRulesLaunchRefusalTests`; extend `AgentSessionRuntimeTests` and `GrokDelegateDispatchTests` for no adapter call, no queued brief, terminal rows/reason. |
| S3: runner backstop | `SessionRunnerRuntime.cs` before session registration; new runner `GrokRulesLaunchException.cs`; explicit 409 mapping in `Program.cs`. Reuse/extract the existing `HerdrLaunchScript` whole-env-token argument resolution so effective arguments are validated before Herdr work. | New `GrokRulesRunnerRefusalTests` with `FakeHerdrServer`, isolated runner HTTP mapping, zero child/pane mutations and no script creation; adjust `HerdrLaunchShapeTests` semantics. |
| S4: align documentation and existing expectations | `docs/agent-kinds.md` section 2 and Grok section, `docs/herdr-sessions.md`, `docs/ai-agent-tui-configuration.md`; qualify claims about successful Grok rules launches/compaction. No generated `docs/cards/` edits. Audit existing Grok-positive launch tests named below. | Preserve meaningful non-Grok/provider/transport coverage; give bundled Windows Grok cases explicit refusal expectations without disabling the guard. |

Existing tests needing deliberate review: `DelegateBundleLaunchTests`, `DelegateLaunchArgvIntegrityTests`, `GrokDelegateDispatchTests`, `GrokDelegateEndToEndTests`, `CodexDelegateDispatchTests` (its Grok sibling assertions), `CardSpawnModelArgumentTests`, and any real-Grok canary that currently relies on delegate bundle launch. Do not mechanically replace their entire behavior with a refusal assertion: isolate pure composition/model assertions where appropriate and keep runnable supported-lane coverage. TestDesign should list the exact affected cases before Code starts.

## Verification design

TestDesign remains a separate stage. These are the required behavioral oracles it must turn into executable cases and a class-filtered run list.

| ID | Required evidence |
|---|---|
| V-1 | Compose the actual embedded `orchestrator` attachment plus reply style and a custom append containing spaces, LF/CRLF, quotes, backticks and a unique sentinel. Exercise named Grok Herdr and a cold Grok delegate through production composition. Assert the specific refusal and **zero adapter/runner launch calls**, not merely a string missing from a helper's output. Removing S2 makes this test red. |
| V-2 | Supply raw unsafe `--rules` through a profile and registry definition, bypassing composition. Exercise fresh and resume through `AgentSessionService`; assert no adapter start. Removing the runtime guard makes it red. |
| V-3 | Send unsafe Grok requests directly to an isolated Windows runner on both backend names. Include an ordinary payload, equals form, alias, and `$env:RULES` resolving to the sentinel payload. Assert 409 `grok_rules_argv_unsafe`, no registered session/host/script, and no Herdr allocation, send-text, close, or detection polling. The test must run without a reachable real Herdr instance. Removing S3 makes it red. |
| V-4 | Test CR-only, LF-only, CRLF, NUL, 4,096 and 4,097 UTF-16 units (include supplementary characters), missing value and duplicate flags. A short single-line rules string with spaces remains byte-identical; the existing total budget still rejects an over-budget launch independently. |
| V-5 | A cold-dispatch refusal after the claim transaction leaves a failed task/session with the specific reason; no queued brief, watchdog wait, or automatic provider/backend retry. Existing running/warm sessions receive no kill or rewrite. |
| R-1 | Claude/Codex and non-Grok Raw/profile arguments retain existing behavior, including multiline payloads. Non-Windows Grok policy branch remains unchanged. The model and native-session identity tests retain meaningful independent assertions. |
| R-2 | Existing Herdr env application/redaction, native-session resume guard, idle-pane timeout retention, and generic script escaping tests still pass. |

The central acceptance test is **no process launch at all for the unsafe composition**. It therefore proves that the composed payload cannot reach a native `CreateProcess` argument under the chosen refusal behavior. A fake Herdr server reporting `agent=grok`, a script string preserving LF, or a healthy HTTP runner is not a successful full-rules-delivery oracle.

No live canary is necessary to prove refusal. A later successful file transport must instead capture the complete composed sentinel at the real Grok request boundary and observe real Herdr detection before the timeout. Use `RealCliStubEnv.ForGrok` and the established dual-hit stub oracle, never hand-built provider redirects. Do not reinterpret a refused/skipped launch as that canary passing.

Run TUnit with `dotnet run --project tests/<Project> --property:OutputPath=bin-card0382/ -- --treenode-filter "/*/*/<Class>/*"`, using the exact new/touched classes listed by TestDesign. Run server and runner projects sequentially; also never overlap `Antiphon.Tests` with `Antiphon.Agents.Pty.Tests`. Process-spawning classes require their assembly's `ParallelLimiter<ProcessSpawnLimit>`; real-Program fixtures must use the established production-runner guard or an isolated random runner. No namespace-wide/full-suite default, production restart, or deployment is part of Plan/TestDesign.

## Plan validation and handoff

Plan-stage checks: full card/related-card reads, source trace, installed Grok version/help, artifact link/diff checks. No tests, builds, model calls, or headed launches were run. Runtime implementation is entirely outstanding.

Land this plan's commit through the normal task landing operation before creating the next stage worktree. TestDesign must preserve D-6's availability/acceptance distinction, enumerate affected existing Grok-positive tests, and make V-1/V-2/V-3 red when their respective guard is removed. Code must implement and verify S1-S4 as one mitigation; do not deploy only the composition half without the runner backstop.
