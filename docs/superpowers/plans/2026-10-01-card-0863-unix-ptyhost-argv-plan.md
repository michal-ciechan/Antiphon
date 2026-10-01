# CARD-0863: Unix PtyHost argv fidelity and NUL refusal

Date: 2026-10-01. Stage: Plan complete; TestDesign follows. Baseline: `origin/master` at `fccef27eb03f2522f2a40574dc1c764335d4a490`. The assigned branch `feat/card-task-fede62ef` starts at that same commit. Read-only `git ls-remote origin refs/heads/master refs/heads/feat/card-task-fede62ef` confirmed both refs at that SHA during Plan. No rebase, merge, build, test, provider launch, deployment or live configuration change was performed. This task changes only this plan.

## Outcome and scope

Pass each Unix PtyHost argument to the native child unchanged, including newlines, quotes, backslashes, empty arguments and Unicode. Refuse an embedded NUL before a child can execute a truncated argument, using the stable reason `pty_argv_nul`. Remove the two CARD-0863 skip statements that currently gate eight Linux results in CARD-0801's CP-6. The existing native-child argv assertions remain acceptance tests; they must pass on Linux with no CARD-0863 skips.

Windows keeps its existing inbox pre-escape/verbatim workaround, modern ConPTY composition, and Windows Grok raw-rules refusal policy. This is an argv transport repair, not a new Unix prohibition on multiline Grok rules. The brief's refusal-shaped test names are historical: their current Unix bodies explicitly expect a successful launch and exact native argv, except for NUL.

The implementation includes the low-level Pty runner and an early SessionRunner admission guard. A low-level-only NUL check would prevent the provider child but would still allow a session registration and a detached host attempt first. The early guard preserves the card's refusal-before-registration contract; the low-level guard protects direct callers and the final containment-rewritten vector. Named refusal also crosses the runner's HTTP and phone-home error boundaries.

No changes to Grok readiness, adoption/receipt validation, prompt delivery, transcript classification, provider homes, terminal input encoding, external packages, host cleanup, checkpoint tooling, or the server's Grok policy. No Porta fork and no replacement native PTY backend. macOS shares the non-Windows argument path, but Linux and Windows are the qualified platforms in this plan; no macOS execution is claimed.

## Evidence and limits

The plan shape follows CARD-0826's daily-host-cleanup plan and CARD-0849's shared-runner-caches plan. Owners consulted: `docs/project-context.md`, `docs/testing-and-build.md` (checkpoint manifest, combined filters, build slots and mutation execution), `docs/orchestration-loop.md` (delegates, source scope and collision policy), `docs/ops-http.md`, `docs/session-runtime-invariants.md`, and ADR 0002. Plan is a delegate and reads its own sources; it dispatches no agents.

Measurements were read-only: `git status --short`, `git branch --show-current`, `git rev-parse HEAD origin/master`, `git ls-remote`, `rg`, bounded source reads, and a Node source census of `[Test]` methods plus their `[Arguments]` expansion. Full cards 0882, 0871, 0865 and 0778, and the flake cards listed below, were retrieved with `pwsh -NoProfile -File scripts/card.ps1 get CARD-<number> -Board Antiphon`. Card reports are historical runtime evidence, not fresh reproductions. Local builds/tests executed in Plan: zero on Linux and zero on Windows.

| Source observation | Consequence / limit |
|---|---|
| `src/Antiphon.Agents.Pty/PtyAgentRunner.cs`, `LaunchCoreAsync`: the inbox pre-escape block checks the backend and `VerbatimCommandLine`, but does not check the OS. It maps every argument through `ModernConPtyConnection.EscapeArgument` before `PtyProvider.SpawnAsync`. | The Windows CRT workaround reaches Unix. Guarding that block with `OperatingSystem.IsWindows()` is the minimal quoting correction. Do not change the escaping function itself. |
| `LaunchArgvGuard.VerifyOrThrow` catches `PlatformNotSupportedException` from its Windows parser and returns. | On Unix this guard does not detect the already-escaped vector. A successful call to this helper is not Unix fidelity evidence. |
| `Antiphon.Agents.Pty.csproj` pins Porta.Pty 1.0.7. CARD-0863 and CARD-0801 record literal added quotes and NUL truncation in the native child. | The repository-side escaping is directly observed. This Plan did not execute or decompile Porta's native implementation; the exact marshalling instruction responsible for truncation is not newly measured. Child echo tests, not a model of Porta, decide acceptance. |
| `PtyAgentRunner` rewrites tracked Unix launches with `IPtyCustodyContainment.Place` before the same escaping/spawn path. | Validate both the caller's vector and the final placed vector. Preserve the containment shim, separator, start-intent/tracking order and existing custody ownership rules. |
| `SessionRunnerRuntime.StartAsync` can prepare custody before `StartCoreAsync`; the latter materializes rules and registers `RunnerSession` before starting its host. | Validate Unix PtyHost input after the existing platform guard and before launch-lock/custody work. Keep a low-level check immediately before native spawn as well. |
| `GrokRulesArgvPolicy.ValidateArgv` explicitly returns no violation when `isWindows` is false. Both server `GrokLaunchArgs` and runner `EnsureGrokRulesArgvSafe` pass the host OS. | Unix multiline acceptance is current deliberate policy. Extending the Windows line-break restriction to Unix would contradict the existing detecting tests. |
| `tests/Shared/HerdrTestProcess.cs`, `CreateOwnedUnixArgvChild`, writes shell `"$@"` as NUL-delimited bytes to the capture file. | Existing acceptance observes a real child's argv. It is not a snapshot of the options supplied to Porta. The test's current `RemoveEmptyEntries` decoding is sufficient for its existing nonempty inputs, but must not be copied into new empty-argument coverage. |
| `HostSession` maps a low-level launch exception to `launchFailed` with its message; `SessionReadLaunchRoutes` currently has no Pty argv catch and phone-home folds `InvalidOperationException` into an unsupported-target error. | Add a distinct Unix NUL exception and explicit runner transport mappings. Leave existing Windows `PtyLaunchArgvException` behavior unchanged. |

### Recount of the removed gates

The source census inspected all method attributes and confirmed no method data sources, matrices or class-level skip attributes in the following CP-6 classes. Loop iterations inside a test are not separate TUnit results.

| Class in `tests/Antiphon.SessionRunner.Tests` | Source methods | Expanded results | Current Linux skipped results | Target Linux / Windows executed |
|---|---:|---:|---:|---:|
| `GrokRulesFileLaunchTests` | 6 | 21 = 12 + 1 + 1 + 5 + 1 + 1 | 7 | 21 / 21 |
| `GrokRulesRunnerRefusalTests` | 11 | 11 | 1 | 11 / 11 |
| `GrokRulesStoreFailureTests` | 1 | 6 argument rows | 0 | 6 / 6 |
| **CP-6 selection** | **18** | **38** | **8 = 7 + 1** | **38 / 38** |

Current Linux non-skipped selection is therefore `38 - 8 = 30`, matching the historical CARD-0801 plan; this is source arithmetic, not a new passing run. Exactly these gates are removed:

1. `GrokRulesFileLaunchTests.Unsafe_final_runner_boundary_has_zero_effects_even_without_server_validation`: `alias`, `crlf`, `duplicate_first`, `duplicate_second`, `equals`, `lf`, `nul`.
2. `GrokRulesRunnerRefusalTests.Pty_host_grok_multiline_rules_are_refused_before_a_session_is_registered`.

The first method has twelve source arguments: `cr`, `lf`, `crlf`, `nul`, `alias`, `equals`, `missing`, `duplicate_first`, `duplicate_second`, `env`, `braced_env`, `env_flag`. Its internal Herdr/PtyHost loop does not double the twelve-result count. On Linux, only its native PtyHost NUL arm is a refusal; the fake-Herdr arm is script-construction evidence and is outside this native-exec fix. Preserve the other platform assertions and the method names so historical evidence stays comparable. No new replacement Linux gate is allowed for these eight results.

### Related-card disposition

| Card | Shared cause? | Disposition |
|---|---|---|
| CARD-0882 | **Not the native quoting defect.** Both named-agent tests demand a Windows-only policy code before any native launch. The test seeds `cmd.exe` on every OS; on Linux the multiline policy returns no violation and some later preflight produces generic `conflict`. | Leave both server tests and policy to CARD-0882. Preserve Windows execution in CP-6 below. Recommend host-aware fixtures/assertions there, consistent with verbatim Unix multiline support, rather than globally forbidding line breaks. This Plan has not reproduced the first generic conflict message and does not claim to know it; its owner must capture that exact message before changing the fixture. Source-path evidence already shows why changing `PtyAgentRunner` cannot make a pre-launch Windows policy fire on Linux. |
| CARD-0871 | **No established shared cause.** The failing case starts successfully, disconnects runtime A, changes the rules receipt, and adopts with runtime B; both host and child were observed dead. That is a lifetime/adoption boundary, not an argv comparison. | Leave its Linux gate and original Windows assertion intact. Source recount: one test method, four `[Arguments]`, one Linux skip (`herdr=false, corrupt=true`); the other three are not evidence that this card fixes the fourth. Do not include an adoption repair or claim its gate is removed. |
| CARD-0865 | The named-seat `Unsafe_grok_rules_on_the_named_seat_refuse_before_any_pane_and_leave_the_streak_untouched` uses **Herdr** and expects Windows's multiline refusal on Linux. It shares CARD-0882's policy-expectation issue, not Unix PtyHost marshalling. The card's older claim of the same quotes/NUL cause is not supported by that test body. Native prompt/history cases could be affected by launch arguments, but their actual causes remain unproved. | Leave all its gates and repairs to that card. Source recount of its Linux gates: eight standing-history argument rows, two always-on rows, two Grok unavailable-store rows, and one unsafe named-seat row, totaling thirteen. The live card's update reports shared Windows failures in the first ten; these are historical results, not newly derived passes. No promise that this fix repairs them. CARD-0878 adds a separate gate in the same class. |
| CARD-0778 | Readiness runs after launch and owns `GrokStartupReadiness.cs`, adapters, capture fixtures and FakeGrok. None of those files is edited here. | No functional dependency on its readiness algorithm. **Code must nevertheless wait for its land while its Code work occupies the mapped `pty` area**, under the standing same-area rule. See collision table. TestDesign can proceed now. |

## Design decisions

### D-1: distinguish a Windows command line from a Unix argument vector

Add the Windows OS condition to the existing inbox pre-escape branch in `PtyAgentRunner.LaunchCoreAsync`. On Unix hand Porta the original argument strings, or the exact vector returned by containment, with no CRT quoting, joining, splitting or shell evaluation. Keep Windows modern and inbox branches otherwise identical. Preserve all existing backend resolution/fallback behavior.

Rejected: stripping leading/trailing quotes from arguments (destroys intentional quotes); a Unix shell-quoting function (there is no shell command string to quote); dropping newlines; changing `EscapeArgument`; changing or forking Porta; bypassing the tracked containment shim; disabling CARD-0101's Windows guard.

### D-2: one pure NUL validator, two production boundaries

Add `src/Antiphon.Agents.Pty/UnixPtyArgvGuard.cs`, containing a pure validator over executable plus argument vector and a public `UnixPtyArgvException : ArgumentException`. Expose `Code = "pty_argv_nul"`, `Reason = "nul"`, and `ArgumentIndex`, with index 0 denoting the executable and indices 1..N denoting child arguments. The diagnostic is fixed metadata, for example `pty_argv_nul: NUL in argv[2]`; never include executable text, an argument excerpt, environment values, or the whole command line. Scan every argument, including flags, duplicates and entries after `--`. Reject a NUL at the start, middle or end; never trim, replace or truncate it. Empty strings and every non-NUL control character remain unchanged.

Call the validator only on non-Windows native Pty launches:

- At `SessionRunnerRuntime.StartAsync`, after the existing required-platform guard, before the launch lock/custody preparation. Apply to null/`PtyHost` backend, not Herdr or unknown backends. This prevents rules files, manifests, host processes, custody attempts and session registrations for invalid input. Platform mismatch keeps its existing precedence.
- In `PtyAgentRunner.LaunchCoreAsync`, before containment placement for the original vector, and again after placement for the final `options.App`/`options.CommandLine`, before `RecordStartIntent` and `PtyProvider.SpawnAsync`. Direct callers are protected without going through SessionRunner. A rejected tracked call still obeys the existing consumed-attempt behavior; do not make a tracked runner reusable.

The pure validator does not normalize or retain the inputs and does not read a clock. Cwd/environment validation and a general cross-backend argv policy are outside this change. The direct-host process may already exist when the low-level check runs; the guarantee there is no provider child. The public runner preflight additionally guarantees no host launch/registration caused by the rejected request.

Rejected: Grok-only checking (any executable can receive a truncated argument); trusting native string marshalling to refuse; checking only before containment; checking only inside the detached host; reusing Grok's Windows rules error for a generic Unix native limitation; including offending values in diagnostic messages.

### D-3: preserve the named refusal across runner transports

`SessionReadLaunchRoutes` catches `UnixPtyArgvException` on both ordinary and platform-constrained launch routes and returns HTTP 409 ProblemDetails with `type` and `title` equal to `pty_argv_nul`, and the sanitized exception message as detail. `PhoneHomeCommandDispatcher` catches this exception before the general `ArgumentException` arm, returning status 409 with `ErrorCode=pty_argv_nul` and the same detail. Use the existing frame/result helpers. No new route, wire field, server exception hierarchy, migration or general exception remapping is needed.

Rejected: an HTTP 500 for invalid argv; phone-home's generic unsupported-target code; payload-bearing diagnostics; mapping every `PtyLaunchArgvException` differently and thereby changing Windows behavior. The existing detached-host `launchFailed` envelope stays compatible and carries the sanitized reason if a direct low-level caller bypasses runner admission.

### D-4: let a native child judge fidelity

Add a small owned Node probe under the Pty tests' already-copied `probes/` directory. It writes `process.argv.slice(2)` as JSON to a fixture-owned capture, atomically completes the capture, emits `ARGV_CAPTURED`, and stays alive until its owner tears it down. Pass the capture path through a dedicated environment value; user test arguments are never interpolated into source or a shell command. Resolve the existing local Node installation; no package download, provider binary or network call. JSON preserves empty arguments, quotes, Unicode and line endings independently of PTY output rendering.

The existing SessionRunner tests continue to use their owned shell child. New application-composition coverage can reuse `HerdrTestProcess.CreateOwnedUnixArgvChild`, already linked into `Antiphon.Tests`; compare complete capture bytes including separators, not `RemoveEmptyEntries`. This avoids changing shared provider fixtures or build staging. Missing Node/host apphost is an explicit fixture prerequisite failure, not a skip counted as success.

Use owned temp roots, assembly-local `ParallelLimiter<ProcessSpawnLimit>`, and `finally` teardown that kills and awaits only the fixture's child/host before cleanup. No test boots real server Program or contacts runner 17204. Native readiness is a completed capture/marker, not a quiet-period heuristic. Bounded wall-clock waits protect the harness only; they are never the intended assertion for a positive control. No production clock seam is required for argument validation. Any test-only delay/retry policy introduced in TestDesign must use an injected `FakeTimeProvider`; real native I/O waits retain cancellation and are not asserted by elapsed milliseconds.

Rejected: options-array self-comparison; an in-memory fake spawn as the sole argv detector; shell `eval`; `.NET` command-line parsing as the only Windows oracle; treating a timeout or compile error as a successful red test; broad provider/readiness runs to test argument transport.

## Exact implementation footprint and slices

All paths below are the future Code footprint; this dispatch edits only this Markdown file. Helpers remain in their named files. No project-file or area-map edit is planned: existing wildcard source inclusion and `probes/**` content staging suffice.

| Slice | Exact files | Test-first sequence |
|---|---|---|
| S1: direct native argv and guard | Modify `src/Antiphon.Agents.Pty/PtyAgentRunner.cs`; **new** `src/Antiphon.Agents.Pty/UnixPtyArgvGuard.cs`; **new** `tests/Antiphon.Agents.Pty.Tests/UnixPtyArgvTests.cs`, `tests/Antiphon.Agents.Pty.Tests/WindowsPtyArgvNativeTests.cs`, `tests/Antiphon.Agents.Pty.Tests/probes/argv-echo.js` | Add the probe and named tests first, with a compiling no-op validator seam where needed. Commit/push; run CP-1's test-first red round. Require native argv inequality or the named exception assertion to fail. Implement the Windows-only escape condition and both low-level validation points; commit/push and run CP-1 green. CP-4 qualifies Windows on the same final implementation. |
| S2: runner admission, wire refusal and ungating | Modify `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`, `src/Antiphon.SessionRunner/SessionReadLaunchRoutes.cs`, `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`, `tests/Antiphon.SessionRunner.Tests/GrokRulesFileLaunchTests.cs`, `tests/Antiphon.SessionRunner.Tests/GrokRulesRunnerRefusalTests.cs`; **new** `tests/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests.cs` | Remove precisely the two CARD-0863 gate statements; strengthen the existing native NUL arm from any exception to the named code/reason and zero registration. Add admission/transport tests before those production edits. Commit/push and run CP-2 red, implement, then commit/push and run CP-2 green. Windows CP-5 runs the unchanged platform branches. |
| S3: real launch composition and final qualification | **new** `tests/Antiphon.Tests/Application/UnixDelegateLaunchArgvTests.cs`; update this plan with frozen TestDesign roster and later checkpoint evidence | Compose representative real delegate args through existing dispatcher helpers, production session-identity/remote-control composition, and the owned native child. This is a regression test of S1, whose native quoting red is already recorded; do not temporarily undo the fix in Code to manufacture another red. Run CP-3 after the committed tests. Later Mutation provides its specified red/green. Run Windows CP-4..CP-6 against the completed Code SHA. |

**Scope:** `src/Antiphon.Agents.Pty/PtyAgentRunner.cs,src/Antiphon.Agents.Pty/UnixPtyArgvGuard.cs,src/Antiphon.SessionRunner/SessionRunnerRuntime.cs,src/Antiphon.SessionRunner/SessionReadLaunchRoutes.cs,src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs,tests/Antiphon.Agents.Pty.Tests/UnixPtyArgvTests.cs,tests/Antiphon.Agents.Pty.Tests/WindowsPtyArgvNativeTests.cs,tests/Antiphon.Agents.Pty.Tests/probes/argv-echo.js,tests/Antiphon.SessionRunner.Tests/GrokRulesFileLaunchTests.cs,tests/Antiphon.SessionRunner.Tests/GrokRulesRunnerRefusalTests.cs,tests/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests.cs,tests/Antiphon.Tests/Application/UnixDelegateLaunchArgvTests.cs,docs/superpowers/plans/2026-10-01-card-0863-unix-ptyhost-argv-plan.md`.

| Other work | Exact overlap / area relationship | Ordering |
|---|---|---|
| CARD-0835 | None with its checkpoint tools/scripts, Review/land services, `AgentTaskReplyService`, `AgentTaskService`, `LandApproval`, migration/snapshot or `docs/testing-and-build.md`. Driver is consumed unchanged. | No source dependency or mandatory wait. Respect its commit-before-checkpoint requirement. |
| CARD-0778 | No exact overlap: no edit to `GrokStartupReadiness.cs`, its `Agents/` tests/fixtures, adapters, FakeGrok, or invariant docs. Both map to `pty` in `antiphon.areas.json`. | While its Code task occupies that area, wait for its confirmed land before CARD-0863 Code. Recheck effective occupancy at dispatch; do not treat separate files or worktrees as an override of the same-area rule. No need for a real Grok readiness launch here. |
| CARD-0788 | No exact file overlap. It edits dispatcher/land/base-selection services and different application tests; this plan reads its dispatcher composition as a consumer. | No functional land dependency. Its broad declared `tests/Antiphon.Tests/Application/**` scope intersects the new composition test; serialize that slice if it is in flight under that scope. |
| CARD-0505 | No exact file overlap; different settings/services/application tests. | No functional dependency. Recheck actual active scope before Code; do not rewrite dispatch helpers to accommodate a changed test harness without amending scope. |
| CARD-0822 | No exact file overlap; its launch-environment and instruction-bundle changes affect inputs to composition tests. | No mandatory wait solely for this held card. Preserve its changes and rerun affected composition evidence if it lands into the tested source. |
| CARD-0826 | **Exact overlap: `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`.** Its plan adds cleanup dispatch; ours adds one typed error mapping. `SessionRunnerRuntime.cs` is not in its supplied exact footprint. Both also occupy the mapped runner area. | Never overlap these Code tasks. Since 0826 is held Code-ready, caller may schedule 0863 first; if 0826 starts first, wait for its land and preserve the new dispatch arms. No dependency on implementing cleanup. |

The collision assessment uses committed plans plus the brief's held/in-flight classification, not a fresh fleet occupancy claim. Immediately before Code the orchestrator reads `/api/agent-tasks/pipeline`, `/api/session-runners`, `/api/runner-defaults` and `/api/hosts`, and compares actual scopes. No host budget change or dispatch is authorized by this Plan.
