# CARD-0497: Codex command-line length fix

Plan complete; implementation and real-session qualification remain pending. Resolve the standard Windows npm Codex launcher to `node.exe <installed codex.js>` at the runner boundary, preserve the complete `developer_instructions` argument, and enforce a launcher-aware final command-line budget before creating a child. Do not ship the alternative launcher on `--version` evidence alone.

## Ground truth

| Assumption or question | Evidence and consequence |
|---|---|
| The AppHost restart broke Codex generally. | Investigation task `87782a55` found a successful fresh Frontier launch after the restart. Both incident PTY logs instead contain `The command line is too long.` The children exited code 1 within approximately 0.26 and 0.11 seconds. The three-minute task durations were settlement delays. |
| The task brief was too large. | The brief already travels by file. The failing launch composed `stage-test-design v4fe98b59` and `delegate-basics vb8805261`: 8,366 instruction characters, before other arguments and quoting. Changing brief delivery does not repair this bug. |
| The existing 30,000-character guard protects the child. | `InstructionBundleComposer.EnsureWithinCommandLineBudget` counts composed text and the caller's partial arguments using a Claude flag and three characters per argument. It does not count the fully resolved executable, all profile/model arguments, or actual quote expansion. It assumes the native Windows process limit, while `codex.cmd` crosses the smaller batch-command limit. |
| Only delegates need fixing. | `AgentTaskDispatcher.ComposeDelegateArgs` and `AgentSessionLaunchComposer.ComposeForAgentAsync` both emit `-c developer_instructions=<complete text>`. Both ultimately reach `SessionRunnerRuntime.StartCoreAsync`, including profile-based launches. |
| Direct Node is already proven. | Investigation measured installed CLI 0.153.4: `codex.cmd --version` succeeds at 6,700 synthetic instruction characters and fails at 8,366; `node codex.js --version` succeeds at 8,366. These are process probes, not interactive launch/delivery evidence. |
| The batch wrapper does essential provider setup. | Read-only inspection of this installation's npm shim shows Node selection (sibling `node.exe`, else PATH), npm boilerplate, and invocation of `node_modules\@openai\codex\bin\codex.js`. The JS entrypoint itself selects the platform executable, sets package-management environment, inherits stdio, forwards termination signals and mirrors exit status. Preserve that entrypoint rather than copying its platform-package logic. |
| Codex has a drop-in instruction-file flag. | Official configuration reference distinguishes additional `developer_instructions` from replacement `model_instructions_file`. Installed `--help` offers `-c key=value` and `-p <name>` loading `$CODEX_HOME/<name>.config.toml`; it does not advertise a developer-instruction file/stdin flag. A config-profile file is a possible native file channel, but is not a measured transparent per-launch overlay. |
| Existing Codex stub tests prove the proposed launcher. | `HeadedCodexGate` prefers a vendored native executable, and `CodexRealCliStubProxyCanaryTests.B_runner_launch_hits_stub_and_kills_clean` runs `exec`. Reusing either unchanged can miss both the npm wrapper and interactive TUI. |
| The runner never observed the exit. | `SessionReconciliationService` means the database still had a live row when the runner reported `Exited`. The investigation's host logs include launch, exit and acknowledgement. Why ordinary exit handling had not closed the rows is unproven and separate. |

Evidence: full investigation report via `pwsh -NoProfile -File scripts/delegate.ps1 -Status 87782a55`; raw logs `C:\logs\antiphon\session-runner\6624fee9971c4843a223fd48d63862ab.ansi.log` and `b572dac2d44148618e535f6f38d015c4.ansi.log`, with corresponding files under `pty-hosts\logs`. The investigation report is the evidence for these incidents; this Plan did not repeat its live reproduction.

Documentation checked on 2026-09-12: [OpenAI configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference) and [CLI/developer commands](https://learn.chatgpt.com/docs/developer-commands?surface=cli). The installed CLI help was also read with an isolated scratch `CODEX_HOME`; exit 0, no model turn. Documentation is not installed-version behavioral proof.

## Decisions

### D-1. Normalize the standard npm Codex shim once, inside the runner

Add a small Codex-specific Windows launch policy called in `SessionRunnerRuntime.StartCoreAsync` after ordinary request validation and before session registration, file writes, host creation or Herdr contact. Use the request's existing Codex identity (`TranscriptFormat` or Herdr kind), not an executable name alone. Return an effective request with Node as `Exe` and the absolute installed `codex.js` path prepended to the unchanged argument vector.

Resolve the shim using the effective launch PATH/cwd. Recognize only the expected npm package/entrypoint and forwarding-only wrapper shape; do not execute or bypass a customized wrapper. Resolve Node as the shim does: sibling `node.exe` first, then effective PATH. Missing dependencies fail explicitly, without falling back to the overflowing shim.

Existing `codex.cmd` definitions gain the fix without profile migrations. Already native/explicit direct-Node launches, other providers and non-Windows behavior remain intact. Custom wrappers are not automatically rewritten.

Herdr receives the same normalized request. Add Node to the Codex executable family if needed, retaining legacy cmd recognition and all recorded-PID, pane and foreign-process checks. A Node name alone does not prove ownership. No new `pwsh` intermediary.

### D-2. Keep native developer instructions; do not introduce file retrieval

The chosen fix removes the approximately 8 KiB cmd hop, not all Windows argument limits. It retains bundle ordering/stamps, named-agent preamble/style/append, model, reasoning effort, paste-burst setting, credentials, transcript identity and existing Codex compaction behavior.

Rejected alternatives for this card:

- **Lower only the budget:** rejects an ordinary TestDesign contract instead of repairing launch; 8,191 payload characters also excludes other arguments.
- **`model_instructions_file`:** changes built-in instruction semantics. `instructions` is also unsuitable; this repository measured it as inert.
- **`--profile` plus generated TOML:** a possible native file channel, but needs proof of profile precedence/composition and per-launch file/home lifecycle. Reconsider if Node qualification fails; do not modify shared user config or copy authentication.
- **Environment/`@path`/stdin:** no measured equivalent developer-instruction loader. Environment expansion inside cmd still hits its limit.
- **Grok-style file-read bootstrap or brief injection:** changes instructions into model-mediated retrieval/conversation and adds unnecessary receipt/refresh machinery.
- **Vendored `codex.exe` directly:** bypasses JS package/environment/lifecycle handling.

### D-3. Make the final guard authoritative and launcher-aware

Keep `Delegation:CommandLineBudgetChars` as a configurable ceiling, default 30,000 UTF-16 code units. A larger configured number must never raise a transport ceiling. Validate the fully resolved invocation after argument additions and the Herdr whole-argument environment expansion. Reuse the existing Windows CRT quoting implementation, exposing a small counting/serialization helper if necessary; do not introduce a second escaping algorithm or use `sum(length + 3)` as final proof.

For the normalized Node/native path, cap the serialized invocation at `min(configured budget, 30000)`, including executable, JS argument, spaces and quote/backslash expansion. Reject nonpositive budgets and NUL. Count UTF-16. Also bound the Node-to-native hop using the longest installed native Codex path in the recognized package layout; this only counts overhead, while JS still selects/launches the executable. An unknown layout is an explicit unsupported-launcher result, not permission to assume a short path. Test long installation paths on both hops.

For a remaining explicit Windows batch/cmd Codex launch, retain short custom-launch behavior but apply a conservative ceiling of `min(configured budget, 7000)` to the rendered invocation. The 1,191-character reserve below 8,191 is containment, not proof of arbitrary wrapper internals or environment expansion. Refuse oversized/unmeasurable shell forms with guidance to configure a native/recognized launcher; never treat 7,000 as a universally safe *payload* length. No generic shell parser is part of this fix.

Keep the composer check as an early estimate; correct its comments/diagnostics and make the runner authoritative. Do not lower Claude's budget, alter Grok transport or truncate content. Refusals name session, launcher, measured length and effective budget, without argv/instruction text.

### D-4. Surface deterministic launch refusals and correct the reconciliation wording

Return HTTP 409 Problem Details (`codex_command_line_too_long`, `codex_launcher_unavailable`/`codex_launcher_unsupported`) before process creation. Include the safe cause, transport/limit and remedy. Reuse `SessionRunnerHttpClient.ThrowForRunnerProblemAsync`, which already maps 409 detail/type to `ConflictException`; verify persistence and delegate failure visibility.

Change the reconciler's fallback to a factual message such as `Reconciliation found the runner exited while the database session was still live (ProcessExited, code 1).` Preserve a more specific failure reason already recorded for that launch generation. Keep status, termination-source and cleanup mappings unchanged.

Defer general post-spawn output transport across host manifests/events/reconciliation. This card surfaces its deterministic length/resolution causes; unknown failures retain exit reason/code and existing raw log evidence. Do not forward arbitrary output containing instructions or secrets.

### D-5. Qualify before adoption; retain worker authorization

TestDesign is separate. Node requires an interactive real-session gate; help/version, argv echo, `codex exec`, mocks and warm reuse are insufficient. A failed qualification returns to Plan with evidence, without silently substituting another transport.

The worker's Create-action 403 is not this defect. The caller owns fresh authenticated dispatch acceptance; delegates own isolated harness tests. No worker-permission bypass or production runner in tests.

## Implementation slices and test ownership

| Slice | Files and work | Tests to extend/add |
|---|---|---|
| S1: effective Codex invocation | New `src/Antiphon.SessionRunner/CodexWindowsLaunchPolicy.cs`; call from `SessionRunnerRuntime.cs`; bounded failure mapping in runner `Program.cs`. Normalize recognized npm shim for both pty-host and Herdr. Keep existing request wire shape. | New `tests/Antiphon.SessionRunner.Tests/CodexWindowsLaunchPolicyTests.cs`: standard vs customized shim, missing dependencies, PATH/sibling precedence, spaces/Unicode paths, idempotence, native/non-Codex/non-Windows behavior, and refusal before any child/session/pane side effect. |
| S2: final budget and producer regressions | Reuse quoting from `src/Antiphon.Agents.Pty/ModernConPtyConnection.cs` through a narrow shared helper; use it in S1. Correct early-guard documentation/diagnostics in `server/Application/Services/InstructionBundleComposer.cs`, `CodexLaunchArgs.cs`, callers `AgentTaskDispatcher.cs`/`AgentSessionLaunchComposer.cs`, and `server/Application/Settings/DelegationSettings.cs` only as needed. | New exact-budget tests; existing `LaunchArgvGuardTests`, `ModernConPtyConnectionCommandLineTests`, `InstructionBundleTests`, `CodexLaunchArgsTests`, `CodexDelegateDispatchTests`, `NamedCodexAgentLaunchTests`, `PinnedCodexProfileDispatchLaunchTests`, `DelegateLaunchArgvIntegrityTests`. Cover both server producers with full >8,191-character instructions, all tiers, profile/default/model overrides and final rejection after base args/quoting expand. |
| S3: lane compatibility and real TUI | `src/Antiphon.SessionRunner.Contracts/HerdrAgentKinds.cs` and narrow `HerdrPaneChild.cs` changes only if required for Node process recognition. New interactive canary using existing isolated runner/stub helpers, explicitly starting from the configured npm shim. | Extend `HerdrLaunchShapeTests`, `HerdrPaneChildKillTests`, `HerdrAdoptionSweepTests`; add `CodexCommandLengthSessionTests` alongside `CodexRealCliStubProxyCanaryTests`. Extend `CodexHerdrRealCliStubProxyCanaryTests` for the normalized launcher. Require real modern PTY, inbox fallback and Herdr evidence for the paths changed. |
| S4: error propagation and owner docs | `server/Application/Services/SessionReconciliationService.cs`; `AgentSessionRuntime.cs` only if preserving a specific reason needs the same adjustment. Reuse runner HTTP mapper. Update `docs/agent-kinds.md`, `docs/ai-agent-tui-configuration.md`, `docs/herdr-sessions.md` and relevant guard comments with final behavior and measured qualification. | `AgentSessionLaunchFailureTests`, `CodexDelegateDispatchTests`, `SessionReconciliationServiceTests`, and runner HTTP Problem Details tests. Verify session/task failure contains safe cause/length/limit, no prompt or secret echo, no child created, and reconciler fallback/preservation without changed status semantics. |

Keep implementation to these launch/error boundaries. No database migration, new instruction-file protocol, instruction shortening, model rerouting, client UI work, supervisor retry changes, timeout tuning or provider-compaction redesign.

## Acceptance gates for TestDesign

1. **Incident regression:** fixed 8,366-character multiline fixture plus current TestDesign composition; quotes, backslashes, Unicode, CR/LF and start/middle/end sentinels. Record serialized lengths. Legacy batch fails; normalized launch preserves the entire value. Add smaller control and serialized budget-minus-one/equal/plus-one cases, including quote-heavy expansion.
2. **Real interactive PTY:** installed Codex TUI through `SessionRunnerRuntime` -> detached pty-host -> modern ConPTY, starting from the npm shim. Use `RealCliStubEnv.ForCodex`, `FakeLlmApiServer`, isolated cwd/home/logs and synthetic credential. Normal adapter/queue submission must produce a stub request with the full developer block once, nonce and injected credential, and unchanged base instructions against a short control. Require this session's matching complete `UserPrompt`, scripted assistant reply and completion. Avoid the helper's native-exe preference and `exec` shortcut.
3. **Lifecycle/lane regression:** clean kill/dispose of Node and native Codex; pty-host adoption ownership retained. Real argv preservation and interactive turn on inbox fallback. Herdr real CLI stub launch/reply/cleanup plus foreign-pane protection. Unavailable/skipped gates stay pending.
4. **Failure visibility:** final length/dependency refusal reaches persisted session/task reason without content leakage or child creation; verify cleanup and reconciliation preservation for the same launch generation.
5. **Caller-owned acceptance:** after deployment, fresh Codex TestDesign dispatches at Frontier and Low, and a fresh named agent with >8,191 composed characters. Use dedicated validation cwd/home and authorized credentials. Prove new child launches (exclude pool reuse), matching boot `UserPrompt`, assistant response/report, full instruction availability and ordinary settlement/ownership. Record IDs, tier, bundle stamps, CLI version, launcher, deployed SHA and result; clean up only validation sessions through their owners.

TestDesign supplies V/R/PC rows and exact filters. Follow `docs/testing-and-build.md`: isolated `--property:OutputPath=bin-c0497/`, `dotnet run`, Unit lane plus affected named integrations, fresh TRX/nonzero counts, sequential process-spawning projects with assembly-local `ParallelLimiter<ProcessSpawnLimit>`. Set real CLI/stub/headed opt-ins explicitly; no production runner, copied auth or normal user Codex home.

## Delivery and remaining work

This dispatch changes only this plan. No implementation builds or test suites ran; one isolated CLI help probe exited 0. All real-session qualification above is pending.

Land this plan before dispatching TestDesign; that dispatch still needs a currently launchable provider chosen by the caller. After Code/Mutation, deploy from the canonical checkout using existing runbooks. Restart/update SessionRunner as well as the server, preserving live hosts; AppHost restart alone cannot load this fix. Verify running build identity and a new child's actual invocation, not health alone. This Plan restarts no shared service.

Separate follow-ups, without expanding this card: investigate the missed ordinary exit-to-database path; design safe bounded post-spawn diagnostics if needed. Worker delegation refusal is an existing authorization boundary, not a prerequisite change.

Next stage: **test-design**.
