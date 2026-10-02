# CARD-0863: Unix PtyHost argv fidelity and NUL refusal

Date: 2026-10-01. Stage: **TestDesign complete; Code waits for CARD-0778 land**. Plan baseline: `origin/master` at `fccef27eb03f2522f2a40574dc1c764335d4a490`; TestDesign source: `f53a3da88b1e8a794a54c0c3ab9c8b473c746da1`, on `feat/card-task-4b33c4d0`. This branch carries the unlanded Plan from `feat/card-task-fede62ef`. No rebase, merge, build, test, provider launch, deployment or live configuration change was performed in either documentation stage. TestDesign changes only this plan.

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

The existing SessionRunner tests and new application-composition coverage use the owned shell-child pattern from `HerdrTestProcess.CreateOwnedUnixArgvChild`, with local helpers in the named test files adding atomic capture completion. Compare complete capture bytes including separators, not `RemoveEmptyEntries`. This avoids changing shared provider fixtures or build staging. Missing Node/host apphost is an explicit prerequisite failure for native-fidelity tests, not a skip counted as success; V-5 deliberately uses an absent host image as a downstream admission tripwire and makes no native-fidelity claim.

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
| CARD-0881 Plan | Live card scopes the effective-settings read surface and final policy/bundle documentation, outside this card's named files. Its final plan is not in this checkout. | No present production collision established; recheck its frozen footprint before Code. Bundle changes can change V-8's input and R-4's catalog census, so recompute expectations from actual composition. |
| CARD-0883 Plan | Live card names `AgentTaskLandSourceResolver.cs`, landing request recovery and its application tests, outside the exact argv footprint. Its final plan is not in this checkout. | No native dependency; a future broad Application test scope must be checked before S3. Do not claim an unobserved final scope is disjoint. |
| CARD-0866 Plan | Live card names disposal preview/redaction and standing-execution locking (`HerdrPaneDisposalEndpointTests` / `HerdrPaneDisposalApplicationTests`), not argv. Its final plan is not in this checkout; a runner-side disposal implementation could share mapped `runner`. | Recheck final scope; if its Code occupies `runner`, defer under the same-area rule even if exact files differ. No repair of its two Linux gates here. |

TestDesign checked the committed 0778/0835/0788/0505/0822/0826 footprints and `antiphon.areas.json`, plus the live cards for the three still-running Plans. A board-scoped read of `/api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&status=Queued,Dispatched,Working,Blocked` on 2026-10-01 confirmed 0778 Code `8d5aa4fc` and 0835 Code `9a1c17e8` Dispatched, with 0881/0883/0866 Plans Dispatched. Held 0788/0505/0822/0826 remain the brief's scheduling classification, not newly observed active tasks. Our exact footprint is the 13 paths above, mapping to **pty, runner, docs**, plus the explicitly named Application test. No EF snapshot, AgentTaskService, scripts/delegate.ps1, tools/Antiphon.Checkpoints, GrokStartupReadiness.cs or tests/Antiphon.Tests/Agents file is edited. Immediately before Code the orchestrator reads `/api/agent-tasks/pipeline`, `/api/session-runners`, `/api/runner-defaults` and `/api/hosts`, then each candidate's actual scope; null/omitted scope is not proof of no collision. No host budget change or dispatch is authorized by this document. This TestDesign does not wait for 0778; **Code does**.

## Verification design

### Inspection

Frozen by TestDesign on 2026-10-01 against the source SHA above. `card.ps1 get CARD-0863 -Board Antiphon` confirmed the title **Unix PtyHost passes literal quotes around multiline argv and truncates arguments at NUL**. Read-only card retrieval also covered 0882, 0871, 0865 and every collision-table card. Source census and Markdown manifest checks are static evidence; **no runtime or mutation success is claimed**.

| Bodies / fixtures inspected | Boundary and coverage |
|---|---|
| `PtyAgentRunner.StartCoreAsync/LaunchCoreAsync`, `LaunchArgvGuardTests`, `ModernConPtyCommandLineTests`, `LinuxCgroupContainmentTests`, Pty test project content staging | Original vector, containment rewrite, final spawn, consumed tracked attempt and Windows escaping: V-1..V-4, V-9, V-10; R-2/R-3. |
| `SessionRunnerRuntime.StartAsync/StartCoreAsync`, `RunnerCustodyLedger.PrepareStart`, `RunnerCustodyTests.CustodyFixture`, `LinuxCustodyProbeTests` | Platform guard precedes custody; custody can reserve before backend refusal; registration counter survives failed launches. V-5/V-11 must inspect these effects, not just the final empty session list. |
| Both `SessionReadLaunchRoutes` handlers, `HostStatsTestHost`, `PhoneHomeCommandDispatcher.LaunchAsync/RejectUnsupportedLaunch`, `PhoneHomeCommandDispatcherTests` | Real route binding and real dispatcher catch ordering: V-6/V-7. Raw executable allowlist avoids a provider identity; phone-home generation watermark precedes runtime admission. |
| All bodies in the three CARD-0801 CP-6 classes, `HerdrTestProcess.CreateOwnedUnixArgvChild`, `TestSessionTeardown` | R-1's exact platform branches; shell capture currently becomes visible before its write completes. Fix capture completion locally in the two touched classes; do not alter shared fixtures. |
| `DelegateLaunchArgvIntegrityTests`, `GrokRulesLaunchRefusalTests` including their composition/harness helpers | V-8 calls the real bundle/identity/remote-control composition. The reusable harness resolves PostgreSQL; budget its established fixture startup, dispose the provider, and never invoke named-agent Start. R-4 remains Windows-only. |
| `GrokRulesAdoptionTests`, CARD-0865/0878 gate conditions and named-seat body in the three `HerdrAlwaysOnChannelParityTests` partials | Excluded adoption, queued-prompt receipt and Windows-policy expectations; no shared native-argv assertion that this fix can certify. |

TestDesign corrects the proposed roster in place: adds the separately detectable pre-containment/consumed-attempt boundary (V-10), final executable NUL (V-4), the second phone-home operation (V-7), and platform/backend precedence (V-11); separates both HTTP catches; replaces vague custody/phone-home setup; freezes capture completion and repeat limits. Production footprint and D-1..D-3 stay as planned. New-result count changes from the proposed 26 to **33**, with the derived checkpoint floors below.

#### Source census

TestDesign recounted `[Test]` and every `[Arguments]` expansion from the eight exact existing files using a Node source census, then inspected OS gates and internal loops. No method/class data source or matrix changes the counts. Recount after any dependency lands. New methods below do not exist yet: their **explicit frozen attributes** are design counts, to be reconciled with implemented source and fresh TRX before Code claims green.

| Existing class | Methods / expanded results | Lane / reason |
|---|---:|---|
| `LinuxCgroupContainmentTests` | 7 / 7 | Linux CP-1: keep the real shim/separator contract while changing the later spawn boundary. Its operations are fakes and require no privileged cgroup access. |
| `LaunchArgvGuardTests` | 10 / 10 | Windows CP-4: real `CommandLineToArgvW` guard and Porta formatter pin. Do not run the full class on Linux: several tests require shell32 and others would pass vacuously through the unsupported-parser return. |
| `ModernConPtyCommandLineTests` | 7 / 7 | Windows CP-4: real Windows parser, embedded quotes, empty/whitespace arguments and backslash rules. |
| `DelegateLaunchArgvIntegrityTests` | 7 / 7 | Windows CP-6: production bundle/identity composition through both command-line formatters. Its internal kind/role loops remain seven TUnit results. |
| `GrokRulesLaunchRefusalTests` | 5 / 5 | Windows CP-6: includes both named-agent refusal tests from CARD-0882 plus the three existing composition/budget controls. Linux's two inherited failures are not disguised as passes or repaired here. |
| CP-6 source classes from CARD-0801 | 18 / 38 | Linux CP-2 and Windows CP-5: see the exact 21 + 11 + 6 census above. |

Use an attribute census over the exact named files, check for additional `[MethodDataSource]`, `[ClassDataSource]`, `[Matrix]` and skip conditions, and then compare the executable TRX roster. `--list-tests` is not scoped execution evidence on this runner. The actual executed names and per-case outcomes must agree with the census; exit zero and `Min` alone are insufficient.

#### Exact retained method roster

The following names were generated from the inspected source attributes; a line with no argument suffix contributes one result. These methods plus V-1..V-11 are the closed roster; internal loops never multiply it.

```text
LinuxCgroupContainmentTests
  Place_prefixes_the_root_owned_shim_with_a_separator
  Place_keeps_a_flag_shaped_child_argument_on_the_child_side
  Read_active_reports_the_trees_own_population
  An_unreadable_tree_throws_rather_than_reporting_zero
  Terminate_goes_through_the_kill_helper_and_reports_the_outcome
  An_empty_container_id_is_refused
  The_helper_paths_are_absolute_and_image_owned

LaunchArgvGuardTests
  The_corrected_escaping_passes_the_guard
  The_old_doubling_rule_is_caught_before_the_process_is_created
  A_truncated_bundle_is_caught_by_LENGTH_not_by_presence
  An_argument_lost_off_the_end_is_reported_as_missing_rather_than_as_a_mismatch
  A_correct_launch_with_no_special_characters_passes
  An_app_path_with_spaces_still_round_trips
  The_porta_formatter_replica_matches_the_real_porta_assembly
  The_inbox_backends_pre_escaped_verbatim_line_round_trips
  Portas_format_does_not_round_trip_the_shape_that_shredded_production
  ParseArgv_is_the_real_parser

ModernConPtyCommandLineTests
  An_embedded_quote_survives_as_one_argument
  A_trailing_backslash_is_not_swallowed_by_the_closing_quote
  A_backslash_immediately_before_a_quote_is_doubled
  A_backslash_not_before_a_quote_is_literal
  Plain_and_whitespace_arguments_round_trip
  An_argument_with_no_special_characters_is_not_quoted_at_all
  The_old_doubling_rule_would_have_shredded_the_embedded_quote_case

DelegateLaunchArgvIntegrityTests
  Every_dispatched_launch_round_trips_through_both_backends
  Every_codex_launch_fits_both_launcher_hops_at_production_budget
  Every_dispatched_launch_round_trips_with_role_defaults_alone
  Every_bundle_in_the_catalog_round_trips_as_a_single_argument
  The_hostile_seed_round_trips_through_both_backends
  The_old_porta_composition_still_fails_the_hostile_seed
  At_least_one_shipped_bundle_still_shreds_under_the_old_porta_rule

GrokRulesLaunchRefusalTests
  Named_grok_herdr_agent_with_unsafe_raw_rules_is_refused_before_any_session_exists
  Named_grok_pty_host_agent_with_unsafe_raw_rules_is_refused_before_any_session_exists
  Cold_grok_delegate_keeps_full_composed_bundles_in_typed_payload
  Named_grok_agent_with_a_single_line_append_composes_the_rendered_line_byte_identical
  Over_budget_single_line_composition_still_throws_invalid_operation

GrokRulesFileLaunchTests
  Unsafe_final_runner_boundary_has_zero_effects_even_without_server_validation ["cr"; "lf"; "crlf"; "nul"; "alias"; "equals"; "missing"; "duplicate_first"; "duplicate_second"; "env"; "braced_env"; "env_flag"]
  Herdr_receipt_is_durable_before_first_request_and_before_typing
  Launch_failure_retains_pre_spawn_receipt_in_existing_manifest
  Invalid_payload_refuses_before_session_registration_or_disk_effects ["nul"; "unresolved_key"; "too_large"; "wrong_kind"; "invalid_unicode"]
  Explicit_rules_conflict_and_unsafe_source_precedence_have_no_effects
  Actual_argv_budget_includes_generated_bootstrap_before_materialization

GrokRulesRunnerRefusalTests
  Herdr_grok_multiline_rules_are_refused_before_herdr_is_contacted
  Herdr_grok_rules_equals_form_is_refused
  Herdr_grok_append_system_prompt_alias_is_refused
  Herdr_env_token_resolving_to_multiline_rules_is_refused
  Herdr_env_supplied_rules_flag_is_refused
  Herdr_grok_oversized_rules_are_refused
  Herdr_grok_wrapper_launch_with_multiline_rules_is_refused_by_kind_not_by_exe_name
  Pty_host_grok_multiline_rules_are_refused_before_a_session_is_registered
  Herdr_grok_single_line_rules_proceed_to_herdr_unchanged
  Herdr_claude_multiline_append_is_not_refused
  Grok_rules_refusal_maps_to_409_with_its_code

GrokRulesStoreFailureTests
  Storage_failure_refuses_before_any_child_or_pane_effect [false, "temp_write"; true, "temp_write"; false, "replace"; true, "replace"; false, "metadata"; true, "metadata"]

```

Static validation at TestDesign completion: the six table rows passed schema/escaped-pipe parsing, exact command/filter/output/Min agreement, and trailing-class-wildcard checks. Source recount: 54 existing methods / 74 expanded results. Planned new roster: 11 methods / 33 results; Linux 76, Windows 69, total 145. Guard/control mapping: 16 unique pairs. `git diff --check` passed. No compiled manifest import, build, test or PC was run.

### Delivery inventory

This changes launch argv and synchronous refusal propagation; it adds no async work queue or prompt-delivery/recovery path. Busy-recipient, enqueue-failure and crash replay tests are therefore out of scope. Do not infer UserPrompt delivery from launch success.

| Producer -> recipient | Identity / persistence / receipt | Limit |
|---|---|---|
| Dispatcher composition -> real Pty runner -> native echo child (V-1/V-3/V-8/V-9) | Fixture-owned capture path and sentinel; child atomically renames complete JSON or NUL-delimited bytes; parent compares the entire vector. Tracked path additionally records ordered start-intent/PID calls. | Native child receipt proves argv only. Node is the real installed native executable; the owned script is its echo program. No provider or parser-only substitute qualifies. |
| HTTP launch caller -> real route -> runtime refusal -> HTTP client (V-6) | Fresh session ID; completed response with 409/type/title/detail; registration counter and log-root snapshot. | No accepted session and no prompt receipt; no new persistence/recovery behavior. |
| Phone-home request frame -> real dispatcher -> real runtime -> response frame (V-7) | Request ID/operation and fixed normalized accepted generation; frame carries matching request ID, 409 and `pty_argv_nul`. | Existing launch-generation watermark is written **before** runtime admission. Assert it is retained; do not promise zero phone-home disk effects or retry the same generation as a new launch. No WebSocket delivery/reconnect change. |

Native capture must have an independent completion condition: write to a sibling temporary file, close, rename to the final capture, then emit the marker. For the existing shell child in R-1 and the new V-8 helper, write a local fixture script using `printf '%s\0' "$@"` and an atomic rename; keep it inside the named test files, with no `eval` or argv interpolation. Reading mere file existence during the old non-atomic write is not deterministic. Compare all bytes including the final separator; never discard empty entries. Start Node with the simple relative probe filename and its directory as cwd, so an argv-quoting mutant cannot corrupt the **probe path** and turn the detector into a missing-script timeout. Capture destination travels in environment, never argv.

Harness deadlines are cancellation/cleanup bounds only (native capture and HTTP: 20 seconds; owned exit: 15 seconds); a deadline failure is a harness failure, never a credited PC red. Do not assert real elapsed time or introduce margins below two seconds. Poll intervals are not correctness margins. Use a fixed `FakeTimeProvider` for the phone-home clock; any new retry/settling logic must use a controlled clock. Keep assembly-local process limiters and serial checkpoint rows. Add the limiter to `GrokRulesRunnerRefusalTests`, whose source currently has only `NotInParallel("HerdrLaunchShape")`, when ungating its native launch. New classes declare `Category("Integration")`. Native fixtures retain the actual owned PID/host identity and kill/await that child in `finally`; a fake containment `Terminate` success alone is not teardown evidence. Use `TestSessionTeardown.KillAndAwaitHostExitAsync` for successful SessionRunner-owned hosts, then await the retained owned host process if its fallback kill was needed (the existing helper does not await after that fallback). Dispose the isolated HTTP app/runtime even after assertion failure.

### Proves it works now

All native tests own their child, capture and teardown. The planned classes have one OS applicability gate at entry where needed; the opposite OS does not select that class in this manifest. A selected-platform gate, missing prerequisite or unexpected skipped result fails acceptance.

| ID | Frozen new test and exact result roster | Production assertion |
|---|---|---|
| V-1 | `UnixPtyArgvTests.Native_argv_is_verbatim`: nine `[Arguments]` values `plain`, `empty`, `space_tab`, `cr`, `lf`, `crlf`, `quotes_backslashes`, `unicode`, `literal_shell` | Real Node child receives exactly the expected argument array and count. Surround each payload with ordinary arguments and a sentinel; `empty` also has a final empty element after that sentinel. Pin payloads: `plain=value`, `space_tab=" a\tb "`, `cr="a\rb"`, `lf="a\nb"`, `crlf="a\r\nb"`, `unicode="é中😀"`; quote/backslash vector includes `"edge"`, `a\\"b`, and a trailing backslash; shell literals include `$HOME`, `$(printf sentinel)`, `;`, `*`. In `cr`, append a separate string containing U+0001..U+001F and U+007F (no NUL), proving other controls are not normalized. Compare ordinal strings, exact count and the unchanged caller vector after Start, only after complete capture. |
| V-2 | `UnixPtyArgvTests.Nul_is_refused_before_native_spawn`: four values `exe`, `first`, `middle`, `last` | First call the pure validator, then separately invoke real `PtyAgentRunner.StartAsync`; require `UnixPtyArgvException`, code `pty_argv_nul`, reason `nul` and exact index at each boundary. `exe`: valid Node path + NUL + sentinel, index 0; `first`: NUL + sentinel in first argument, index 1; `middle`: duplicate flag vector `[probe, --rules, safe, --rules, sentinel+NUL+tail, end]`, index 5; `last`: `[probe, --, --flag+sentinel+NUL]`, index 3. This covers NUL at beginning/middle/end, duplicate values, flag-shaped data and scanning past `--`. Require runner PID null and no capture. Assert fixed diagnostic metadata, no synthetic sentinel, executable text or suffix. Pure validation comes first so a scan-bypass mutant fails immediately without executing malformed native argv. |
| V-3 | `UnixPtyArgvTests.Tracked_argv_is_verbatim_after_containment`: one result | A test-owned recording `IPtyCustodyContainment` places the launch onto the owned native probe without sudo or a production cgroup. Drive `StartTrackedAsync`; assert the original vector passed to `Place`, the exact final native vector after the test wrapper's sentinel argument, and journal start-intent before tracked PID. This covers real spawn with a fake containment boundary, not privileged isolation qualification. |
| V-4 | `UnixPtyArgvTests.Containment_introduced_nul_is_refused_before_start_intent`: two `[Arguments]` values `exe`, `arg` | Original vector is valid. Recording containment returns either a NUL-bearing executable (index 0) or final argument (index 3). Its fake journal records then throws a fixture tripwire exception on `RecordStartIntent`; correct code never calls it. Capture outcome, assert start-intent/tracking counts zero **before** checking named exception metadata, PID and capture absence. Removing the final guard therefore fails a zero-count assertion before native spawn, without requiring a successful invalid exec. |
| V-5 | `UnixPtyArgvAdmissionTests.Nul_request_is_refused_before_effects`: four values `plain`, `grok`, `grok_payload`, `tracked` | `plain`: null backend and NUL executable; `grok`: explicit `SessionBackends.PtyHost` (`pty-host`), Grok format and NUL raw rule; `grok_payload`: case-insensitive `PTY-HOST`, valid typed payload plus an unrelated NUL raw argument (no explicit-rules conflict); `tracked`: valid Linux binding, NUL argument. Real runtime; assert registration counter unchanged **before** exception type, empty list/failed lookup, no new rules/manifest/capture. Untracked cases deliberately use an absent `PtyHostSourceDir` as a downstream tripwire: a guard-bypass mutant reaches registration then fails promptly before host spawn. Tracked setup initializes only runner-store identity and a valid binding, snapshots those setup files, and supplies the existing `CustodyEnvironment` seam reporting an unavailable root; any `PrepareStart` writes a reservation/unsupported record and fails without sudo or native launch. Assert no new reservation, start intent or unsupported record relative to that snapshot. Never assert the precreated custody root is absent. All cases require named NUL metadata and sanitized detail. |
| V-6 | `UnixPtyArgvAdmissionTests.Nul_refusal_is_named_on_both_launch_routes`: two values `ordinary`, `platform_constrained` | Host `SessionReadLaunchRoutes` on isolated random loopback with a real isolated runtime; do not boot real Program. POST a NUL-bearing JSON argument (escaped on wire). Constrained request names Linux. Assert HTTP 409, ProblemDetails type/title `pty_argv_nul`, sanitized detail, zero registrations and no launch artifacts. Await the server response, not a time budget. |
| V-7 | `UnixPtyArgvAdmissionTests.Phone_home_nul_refusal_is_named`: two `[Arguments]` values `launch`, `platform_constrained` | Real `PhoneHomeCommandDispatcher` plus `PhoneHomeRuntimeAdapter` wrapping the isolated runtime. Configure `AllowedCwd` to the fixture root, `RawExeAllowList` to the owned echo executable, capacity 1 with zero sessions, fixture-local capacity/generation paths, memory 0, no Herdr/verification binding/transcript format. Use distinct session/request IDs and a fixed normalized UTC accepted generation; constrained operation sets `RequiredPlatform=linux`. Assert matching response request ID, error-frame kind, status 409, `ErrorCode=pty_argv_nul`, sanitized detail, zero registrations and retained generation watermark. No auth probe runs for the raw executable. A generic admission/auth/capacity error cannot satisfy the named code assertion. |
| V-8 | `UnixDelegateLaunchArgvTests.Composed_delegate_argv_reaches_native_child`: two values `Investigate`, `Code`, for a ClaudeCode Worker with role-default bundles | Reuse the existing delegate harness in `GrokRulesLaunchRefusalTests` without invoking its named-agent start. Call actual `AgentTaskDispatcher.BuildLaunchSpec`, `AgentSessionService.BuildSessionIdentityArgs` and `ClaudeRemoteControlLaunchArgs.ApplyOff`, as `DelegateLaunchArgvIntegrityTests` does. Substitute only the executable with the owned argv child. Require exact full NUL-delimited UTF-8 bytes, entire appended bundle length/content and session ID at its intended position. Neither a contains-only bundle check nor a hand-copied bundle qualifies. No adapter readiness or real provider is involved. |
| V-9 | `WindowsPtyArgvNativeTests.Windows_backends_keep_exact_native_argv`: two values `inbox`, `modern` | Launch the owned Node argv probe through the actual `PtyAgentRunner` with each backend override. Assert resolved backend equals requested backend (modern fallback is a prerequisite failure), then exact native argv/count for a single vector containing multiline quoted text, empty arguments, a spaced path with trailing backslash and trailing identity arguments. No Windows NUL behavior is changed or redefined. |
| V-10 | `UnixPtyArgvTests.Original_nul_is_refused_before_containment_and_consumes_attempt`: one `[Test]` | Invalid original argument; recording containment increments its call count then throws a fixture tripwire. Assert Place count zero **first**, zero journal writes, named NUL exception. Retry valid argv on the same tracked runner and require the existing `InvalidOperationException` consumed-attempt refusal, still zero Place calls. This isolates the original-vector guard from the final-vector guard; the tripwire prevents native execution under either mutant. |
| V-11 | `UnixPtyArgvAdmissionTests.Platform_and_backend_boundaries_keep_existing_verdicts`: four `[Arguments]` values `platform_mismatch`, `platform_invalid`, `unknown_backend`, `herdr_nul` | All requests contain NUL. Linux mismatch (`windows`) and invalid (`bsd`) require the existing platform exception/code and no runtime effects; unknown backend requires the existing unsupported-backend `ArgumentException`, **not** `UnixPtyArgvException`; Herdr uses the existing Grok FakeHerdr setup with a raw NUL rule and must reach its script-construction assertion with exact input preserved and no native capture. This is fake script evidence only; it must never execute NUL via a real shell. |

Frozen new-source arithmetic: Unix Pty class has five methods / `9 + 4 + 1 + 2 + 1 = 17` results; Unix admission has four methods / `4 + 2 + 2 + 4 = 12`; Unix delegate one method / 2; Windows native one method / 2. Total **11 methods / 33 planned results**. All argument rows above are literal `[Arguments]`, not hidden data-source expansion. All process-spawning classes use their own assembly's existing limiter; do not introduce a shared cross-assembly limiter or run the large test projects concurrently.

**Red-first order:** S1 commits the probe, V-1/V-2/V-3/V-4/V-10 and the compiling validator/exception seam before the fix, then CP-1 red; implement D-1/D-2, commit/push, CP-1 green. S2 commits V-5/V-6/V-7/V-11 and the eight ungated R-1 rows, then CP-2 red; implement admission/mappings, commit/push, CP-2 green. A test-first route response may be 500 or a different refusal before its catch exists; require its status/code assertion red, never a parser exception from trying to read an empty 500 body. S3 adds V-8 and runs CP-3; no deliberate revert of S1 during Code. CP-4..CP-6 qualify the final Code SHA on Windows. Source-order listing is an authoring order, not a dependency on TUnit execution order.

**Repeat budget:** one normal final run per CP is the default; zero loaded repeats are required. If instability warrants repeats of the new methods, cap them at **three normal and two loaded repeats total per method**, report why and stop once the evidence is resolved. These are ceilings, not five compulsory rounds; do not repeat whole classes/suites for stability. Test-first red/fix runs and later commissioned PC baseline/red/restored-green have their own labeled evidence, not hidden repeat rounds.

### Guards the regression

| ID | Existing detector / retained invariant |
|---|---|
| R-1 | All thirty-eight CARD-0801 CP-6 results. Remove only CARD-0863's gates and require exact native argv in the seven accepted Unix multiline cases plus the remaining ungated inputs. The NUL variant is a named refusal before native launch; its fake-Herdr loop remains the current script-generation contract. |
| R-2 | `LinuxCgroupContainmentTests`, all seven methods: shim path, `--`, child flag placement, population, unreadable-state refusal, termination reporting and nonempty container identity. Together with V-3/V-4, these prevent the argv fix from bypassing containment. |
| R-3 | Windows `LaunchArgvGuardTests` and `ModernConPtyCommandLineTests`, ten plus seven results: the CRT round trip and the old incorrect Porta formatter remain detected. V-9 additionally proves the production runner still uses the correction. |
| R-4 | Windows `DelegateLaunchArgvIntegrityTests` and `GrokRulesLaunchRefusalTests`, seven plus five results: shipped bundle/identity args and Windows named-agent rules refusal remain unchanged. |

### Guard inventory

Each independently bypassable guard in the touched path has its own control. Existing unrelated custody recovery, Grok policy and readiness guards are retained regression scope, not new obligations for this argv repair.

| Guard | Plan invariant | Distinct control |
|---|---|---|
| G-1 | D-1 Unix arguments are never CRT pre-escaped | PC-1 |
| G-2 | D-2 executable NUL scan | PC-2 |
| G-3 | D-2 complete argument scan, including duplicates/flags/after `--` | PC-3 |
| G-4 | D-2 original vector checked before containment | PC-4 |
| G-5 | D-2 final executable/vector checked before journal/spawn | PC-5 |
| G-6 | D-2 runner admission precedes registration/materialization/custody | PC-6 |
| G-7 | D-2 diagnostics contain metadata only | PC-7 |
| G-8 | D-3 ordinary HTTP catch preserves the named refusal | PC-8 |
| G-9 | D-3 constrained HTTP catch preserves the named refusal | PC-9 |
| G-10 | D-3 phone-home catch precedes general ArgumentException mapping | PC-10 |
| G-11 | D-1 Windows inbox retains pre-escape plus verbatim | PC-11 |
| G-12 | D-1 tracked spawn uses the exact placed vector | PC-12 |
| G-13 | D-2 rejected tracked attempt remains consumed | PC-13 |
| G-14 | D-2 platform validation keeps precedence | PC-14 |
| G-15 | D-2 early guard applies only to native PtyHost backends | PC-15 |
| G-16 | D-1/D-4 complete composed argument length reaches the child | PC-16 |

Inventory audit: **guards=16, mapped=16, missing=0, duplicate PC maps=0**. Each control below has a compiling mutation and a reachable assertion checked against the source/harness ordering. Executability is a static design assessment; post-land Mutation must still record actual red/restored-green results.

### Positive controls

Ordinary Code records its test-first red rounds and final green checkpoints. After ordinary Review and confirmed land, a separately commissioned Mutation task uses SourceLanding custody and exact-method baseline/red/restored-green runs. Follow `docs/testing-and-build.md`; copy the unchanged checkpoint driver and its required library outside the mutable snapshot before mutation. No mutation is executed in this Plan, and no real provider is needed. Every expected red below is a named assertion failure, never a build error, harness timeout, missing fixture or zero-test run.

| PC | Concrete production mutation | Named detecting test and expected assertion |
|---|---|---|
| PC-1 | Remove only the new Windows OS condition from the inbox pre-escape branch. | V-1 `UnixPtyArgvTests.Native_argv_is_verbatim` (9): `lf`/`crlf` and quote/empty/space rows reach complete capture and fail exact-array equality. `plain`/`unicode` are comparison rows; the `cr` row also carries other controls including LF; do not demand every parameter red. Simple relative probe filename survives the mutant. |
| PC-2 | In `UnixPtyArgvGuard`, bypass only the executable scan. | V-2 `UnixPtyArgvTests.Nul_is_refused_before_native_spawn` (4): `exe` fails the first pure-validator expected-exception assertion before any native call. Other arguments stay guarded. |
| PC-3 | In that validator, bypass only the argument loop. | Same named V-2 method (4): `first`, `middle`, `last` fail the pure expected-exception assertion, while `exe` remains green. |
| PC-4 | Remove only the pre-containment validator call in `LaunchCoreAsync`. | V-10 `UnixPtyArgvTests.Original_nul_is_refused_before_containment_and_consumes_attempt` (1): Place increments then trips; the first assertion expects zero Place calls and sees one. The final guard never hides it and no child spawns. |
| PC-5 | Remove only the final validator call before RecordStartIntent/spawn. | V-4 `UnixPtyArgvTests.Containment_introduced_nul_is_refused_before_start_intent` (2): both valid-original rows reach the journal tripwire; zero-start-intent assertion sees one. Native spawn is never attempted. |
| PC-6 | Remove only SessionRunner's early guard, keeping both low-level calls. | V-5 `UnixPtyArgvAdmissionTests.Nul_request_is_refused_before_effects` (4): three untracked rows fail the monotonic registration/no-rules-effects assertion before checking the downstream missing-host exception; tracked row fails the no-new-reservation assertion before the downstream unsupported-custody exception. No privileged setup or detached host is needed. |
| PC-7 | Append the offending value to the validator's fixed diagnostic. | V-2 `UnixPtyArgvTests.Nul_is_refused_before_native_spawn` (4): synthetic-sentinel exclusion fails with the correct exception type/code. Never use real payloads or credentials. |
| PC-8 | Delete the new Unix exception catch from ordinary HTTP route only. | V-6 `UnixPtyArgvAdmissionTests.Nul_refusal_is_named_on_both_launch_routes` (2): ordinary row gets completed HTTP 500 and fails status==409 **before** JSON parsing; constrained row remains green. |
| PC-9 | Delete that catch from constrained HTTP route only. | Same V-6 method (2): constrained row fails status==409; ordinary remains green. Removing one catch cannot qualify the other. |
| PC-10 | Delete the Unix exception catch from `PhoneHomeCommandDispatcher`. | V-7 `UnixPtyArgvAdmissionTests.Phone_home_nul_refusal_is_named` (2): both operations complete as generic status 400/unsupported-target and fail status/code assertions. |
| PC-11 | Bypass the inbox pre-escape/verbatim block on Windows; keep modern composition. | V-9 `WindowsPtyArgvNativeTests.Windows_backends_keep_exact_native_argv` (2), **Windows only**: inbox capture mismatches quoted/multiline payload, modern stays green. Probe filename is plain and cwd is separate, so the echo program still runs. |
| PC-12 | In the containment block, assign `options.CommandLine` the original `commandLine` instead of `placed.CommandLine`. | V-3 `UnixPtyArgvTests.Tracked_argv_is_verbatim_after_containment` (1): recording containment keeps Node app/probe filename unchanged and inserts `placed-sentinel` immediately after the filename. Child capture completes but lacks that element; final-vector equality fails. The same test asserts the ordered journal calls, never treats a fabricated PID as real containment qualification. |
| PC-13 | Set `_trackedLaunchAttempted = false` instead of latching the tracked attempt. | V-10 `UnixPtyArgvTests.Original_nul_is_refused_before_containment_and_consumes_attempt` (1): first refusal is correct; valid retry reaches the Place tripwire and fails the expected consumed-attempt `InvalidOperationException` assertion. Tripwire must be a distinct test exception type. |
| PC-14 | Move the new runtime guard immediately before `RunnerPlatformLaunchGuard.RefuseBeforeLaunch`. | V-11 `UnixPtyArgvAdmissionTests.Platform_and_backend_boundaries_keep_existing_verdicts` (4): mismatch/invalid rows fail their platform-exception/code assertion because Unix NUL refusal wins. |
| PC-15 | Remove only the native-backend predicate from the new runtime guard, retaining its non-Windows predicate. | Same V-11 method (4): unknown-backend and Herdr rows fail their existing-verdict assertions; Herdr cannot reach fake script capture. No real shell executes NUL. |
| PC-16 | Immediately before Unix native spawn, replace each final argument longer than 1024 characters with its first 1024 characters; leave smaller arguments intact. | V-8 `UnixDelegateLaunchArgvTests.Composed_delegate_argv_reaches_native_child` (2): assert composed append exceeds 1024 as setup, then child completes capture and full byte/append-length equality fails. This tests real shipped content, not a hand-copied expected prompt. |

Positive controls use each method's prefix with trailing `*`, for example `/*/Antiphon.Agents.Pty.Tests/UnixPtyArgvTests*/Native_argv_is_verbatim*`. Keep baseline/red/restored-green output and results roots separate. For parameterized methods, select the method prefix and inspect all argument results rather than filtering a literal display suffix. Restore changed source with a refreshed timestamp and rebuild before restored green. Any further source edit invalidates affected checkpoint evidence.

### Out of scope

No requirement in the corrected roster depends on an untestable timing margin. Native scheduling/OS availability cannot be made deterministic; a missing Node/PtyHost/modern ConPTY prerequisite, deadline, hang, compilation failure or zero selected tests is explicitly **unqualified evidence**, not a red control. Deterministic tripwires above replace the proposed controls that would otherwise rely on a later native NUL failure. Backend cgroup containment itself, macOS qualification, provider readiness, delivery, adoption and standing recovery remain separate work.

The eight removed gate results have OS-specific **real assertions**: Linux's six multiline native rows plus the named multiline method capture exact native argv, while the NUL row refuses before native spawn; Windows executes all eight existing named policy-refusal assertions. Requiring a Windows child to receive the forbidden raw Grok rules would contradict its policy. CP-4 separately proves native Windows argv for admissible vectors with the real Node binary. CP-5 is retained Windows policy qualification, not a claim those refused requests spawned children.

#### Known flakes

Do not run a whole Unit lane, whole `Agents` namespace or whole Herdr parity class for this repair. Their cost and unrelated failures do not improve the named argv evidence. The following known failures were considered and are outside the closed filters; they remain reportable if accidentally selected, never silently ignored:

- CARD-0791: `RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision` and `RunnerClaudeAdapterEffortPromptTests.Attach_without_launch_effort_preserves_current`, tight wall-clock readiness windows.
- CARD-0794: the repeated Codex readiness flake and `RunnerMultilinePromptDeliveryTests.The_pty_receives_the_marker_line_of_a_multiline_prompt`, Linux broken-pipe/hang behavior. Prompt input is a different path from launch argv.
- CARD-0818: `CheckpointExecutorLogTests.concurrent_callbacks_append_each_line_once_without_overlap`, producer/disposal deadlock on a failed wait.
- CARD-0820: checkpoint temp-root contention, `EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open`, build-slot/ownership timing, file-in-use cleanup, and `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget`.
- CARD-0828: `CheckpointTaskOwnershipTests.uncertainty_lets_the_running_row_finish_and_marks_pending_owner_unverified` and `late_settlement_after_owner_unverified_cancels_the_running_row`, leaked owner/executor lifetime under failed waits.
- CARD-0848: `DetachedLauncherTests.executor_survives_its_starter`, fixed marker window under host load.
- CARD-0878: `HerdrAlwaysOnChannelParityTests.Standing_native_wire_missing_target_never_creates(ClaudeCode, PtyHost, False)`, output/exit classification race; leave its gate alone.
- CARD-0879: `HerdrAlwaysOnChannelParityTests.Named_AlwaysOn_herdr_agent_holds_after_three_failures_keeps_its_timeout_shell_and_resumes_only_on_explicit_retry`, Windows DetectTimeout versus PaneClosed race.
- CARD-0757: `ScaledTimeProviderTests.Speed_10`; CARD-0751: `HttpResilienceRegistrationTests.Runner_list_and_git_connectivity_keep_their_short_deadlines`.

No new test relies on those real-time margins. If an affected acceptance method fails, preserve the first failure and classify it before any justified, method-scoped rerun. A known-flake label does not waive the exact roster, allow an unleased run or permit a new skip. CARD-0882's two Linux server rows and CARD-0871's adoption row are disclosed excluded work, not flaky passes.

### Checkpoints

Closed list of ordinary builds and test filters. Each row owns one isolated project build and one exact filter. **Linux/server2 runs CP-1..CP-3; Windows runs CP-4..CP-6 only**, against the same completed implementation SHA. The opposite-OS entries are not selected, rather than executed and credited as skips. Windows is scoped to the actual Windows parser/backend and retained Windows policy checks. All selected rows require zero failures and zero skipped results. No test/build ran in Plan.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Agents.Pty.Tests -> bin-c863-unix-pty/` | unix-native-argv | `/*/Antiphon.Agents.Pty.Tests/(UnixPtyArgvTests*)\|(LinuxCgroupContainmentTests*)/*` | V-1..V-4, V-10, R-2 | Linux: 24 executed (17 new + 7 existing), 0 failed/skipped; Windows: not selected | 24 | 5 | true |
| CP-2 | S1-S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c863-unix-runner/` | unix-runner-argv | `/*/Antiphon.SessionRunner.Tests/(UnixPtyArgvAdmissionTests*)\|(GrokRulesFileLaunchTests*)\|(GrokRulesRunnerRefusalTests*)\|(GrokRulesStoreFailureTests*)/*` | V-5..V-7, V-11, R-1 | Linux: 50 executed (12 new + 21 + 11 + 6 existing), 0 failed/skipped; Windows: not selected | 50 | 8 | true |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c863-unix-compose/` | unix-composed-argv | `/*/Antiphon.Tests.Application/UnixDelegateLaunchArgvTests*/*` | V-8 | Linux: 2 executed, 0 failed/skipped; Windows: not selected | 2 | 7 | true |
| CP-4 | S1-S3 | `tests/Antiphon.Agents.Pty.Tests -> bin-c863-win-pty/` | windows-native-argv | `/*/Antiphon.Agents.Pty.Tests/(WindowsPtyArgvNativeTests*)\|(LaunchArgvGuardTests*)\|(ModernConPtyCommandLineTests*)/*` | V-9, R-3 | Windows: 19 executed (2 new + 10 + 7 existing), 0 failed/skipped; Linux: not selected | 19 | 5 | true |
| CP-5 | S1-S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c863-win-runner/` | windows-rules-argv | `/*/Antiphon.SessionRunner.Tests/(GrokRulesFileLaunchTests*)\|(GrokRulesRunnerRefusalTests*)\|(GrokRulesStoreFailureTests*)/*` | R-1 Windows | Windows: 38 executed (21 + 11 + 6), 0 failed/skipped; Linux: not selected | 38 | 8 | true |
| CP-6 | S1-S3 | `tests/Antiphon.Tests -> bin-c863-win-compose/` | windows-composed-argv | `/*/Antiphon.Tests.Application/(DelegateLaunchArgvIntegrityTests*)\|(GrokRulesLaunchRefusalTests*)/*` | R-4 | Windows: 12 executed (7 + 5), 0 failed/skipped; Linux: not selected | 12 | 7 | true |

## Execution, activation and rollback

TestDesign has frozen method attributes, fixture readiness/teardown and exact named assertions in this plan, with a fresh source census. Code must reconcile the implemented attributes and fresh TRX with that roster; it may not broaden production scope or run live providers. After the collision gate clears, Code implements S1, S2, S3 test-first as described. Commit and push before each checkpoint invocation (including red rounds); never rebase/amend/reset a pushed commit. Native quoting and NUL failures must be named red assertions against compiling code, not merely the historical card report.

This brief explicitly requires **`scripts/run-checkpoint.ps1` directly** because CARD-0853's checkpoint-tool owner verification cannot be assumed in this dispatch. It self-leases; do not nest it inside `build-slot.ps1`. The following are future green-round commands, not Plan measurements. Execute each row serially after its required commits. TUnit runs through the driver's `dotnet run --project ... --no-build`, never `dotnet test`. Keep the Linux default `UseAppHost=false`; existing staging supplies the SessionRunner native PtyHost. Do not add a hand-quoted OutputPath or rebuild a project independently.

Markdown table filters escape pipes as `\|`. **Remove that Markdown backslash in actual shell arguments** and quote the complete filter, as below; otherwise the driver receives a different filter. The single quoted strings work in both sh and PowerShell.

```sh
# Linux/server2, sequential; use a new ResultsRoot for each red/fix/rerun round.
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-c863-unix-pty/ -Filter '/*/Antiphon.Agents.Pty.Tests/(UnixPtyArgvTests*)|(LinuxCgroupContainmentTests*)/*' -MinExecuted 24 -Expect 'UnixPtyArgvTests,LinuxCgroupContainmentTests' -ResultsRoot .antiphon/c863-linux-green
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-2 -Project tests/Antiphon.SessionRunner.Tests -OutputPath bin-c863-unix-runner/ -Filter '/*/Antiphon.SessionRunner.Tests/(UnixPtyArgvAdmissionTests*)|(GrokRulesFileLaunchTests*)|(GrokRulesRunnerRefusalTests*)|(GrokRulesStoreFailureTests*)/*' -MinExecuted 50 -Expect 'UnixPtyArgvAdmissionTests,GrokRulesFileLaunchTests,GrokRulesRunnerRefusalTests,GrokRulesStoreFailureTests' -ResultsRoot .antiphon/c863-linux-green
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-3 -Project tests/Antiphon.Tests -OutputPath bin-c863-unix-compose/ -Filter '/*/Antiphon.Tests.Application/UnixDelegateLaunchArgvTests*/*' -MinExecuted 2 -Expect UnixDelegateLaunchArgvTests -ResultsRoot .antiphon/c863-linux-green

# Windows only, final Code SHA, sequential.
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-4 -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-c863-win-pty/ -Filter '/*/Antiphon.Agents.Pty.Tests/(WindowsPtyArgvNativeTests*)|(LaunchArgvGuardTests*)|(ModernConPtyCommandLineTests*)/*' -MinExecuted 19 -Expect 'WindowsPtyArgvNativeTests,LaunchArgvGuardTests,ModernConPtyCommandLineTests' -ResultsRoot .antiphon/c863-windows-green
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-5 -Project tests/Antiphon.SessionRunner.Tests -OutputPath bin-c863-win-runner/ -Filter '/*/Antiphon.SessionRunner.Tests/(GrokRulesFileLaunchTests*)|(GrokRulesRunnerRefusalTests*)|(GrokRulesStoreFailureTests*)/*' -MinExecuted 38 -Expect 'GrokRulesFileLaunchTests,GrokRulesRunnerRefusalTests,GrokRulesStoreFailureTests' -ResultsRoot .antiphon/c863-windows-green
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-6 -Project tests/Antiphon.Tests -OutputPath bin-c863-win-compose/ -Filter '/*/Antiphon.Tests.Application/(DelegateLaunchArgvIntegrityTests*)|(GrokRulesLaunchRefusalTests*)/*' -MinExecuted 12 -Expect 'DelegateLaunchArgvIntegrityTests,GrokRulesLaunchRefusalTests' -ResultsRoot .antiphon/c863-windows-green
```

Report each row as `CHECKPOINT CP-n commit=<sha> build=<ok|failed> filter=<exact-filter> executed=N passed=N failed=N skipped=N trx=<path>`, including `reruns=k` and the reason for every additional invocation. Record the test-first red rounds separately from final green. Retain first failures and the fresh roster; a missing row, wrong per-class count, new skip, or zero-test run is not complete. Commit/push each substantial slice and verify its tip with `git ls-remote`. If the slot gate returns exit 4, report the slot timeout; never bypass leasing. If the caller later supplies a verified checkpoint-tool owner and explicitly changes this execution instruction, retain the same closed rows and wait while tool exit is 75, with short polling and liveness updates; do not start a second run.

Selected final arithmetic is Linux `24 + 50 + 2 = 76` and Windows `19 + 38 + 12 = 69` results, **145 OS executions** in total. The source census has 74 existing plus 33 new = 107 distinct expanded results; the same 38 runner results execute on both OSes (107 + 38 = 145). Both platform receipts are required before claiming Windows behavior unchanged. A pending Windows lane is reported pending and handed to a task pinned to that OS, never replaced by Linux parser-model tests.

Activation is a later, separately authorized operation after Review/land. Publication alone does not update the running runner or its detached host images. Follow the canonical deployment/restart owner, drain/ownership rules and main-checkout restrictions; do not restart from this linked worktree. Record the published SHA, verify the runner build version and the new launch's PtyHost image identity, and perform only an owned local echo-child smoke if commissioned. Existing adopted/live hosts retain their original image and must not be killed merely to activate this fix. No provider readiness or transcript-delivery acceptance is inferred from an argv echo.

Rollback uses a new forward commit reverting this card's production changes together with restoring the two CARD-0863 gates if the defect returns. Never reset or force-push the task branch. Keep the original problem and all evidence visible; rollback reintroduces a known truncation hazard and is not a claim of safe Unix argv. Deployment rollback follows the same ownership/drain front door and changes only future launches. The native child fixtures require no external state rollback, package changes or database migration.

### Cost

Ordinary final checkpoint floor is **40 minutes**, the sum `5 + 8 + 7 + 5 + 8 + 7`: Linux/server2 20, scoped Windows 20. These are estimates including each row's isolated build, not measured runtimes. CP-1/CP-2 test-first red rounds add an estimated **13 minutes** using the same rows. Authoring is estimated at 60–90 minutes; Code budget **113–143 minutes**, before slot waits or justified fixes. No mandatory normal/loaded repeats are added.

Post-land Mutation floor is separately **108 minutes**: 6 minutes external-driver/evidence setup, plus baseline/red/restored-green for each method-scoped control. Eight Unix Pty controls (PC-1/2/3/4/5/7/12/13) cost `8 × 3 × 2 = 48`; six runner controls (PC-6/8/9/10/14/15) cost `6 × 3 × 2 = 36`; Windows PC-11 costs `1 × 3 × 3 = 9`; application PC-16 costs `1 × 3 × 3 = 9`. Each invocation uses the detector's fully named method prefix with trailing `*`, the class prefix with trailing `*`, its corresponding project, and fresh evidence. Estimates assume scoped incremental rebuilds, not full-suite runs. Native prerequisite failure remains unqualified and is not budgeted as a red. Controls sharing a source method are sequential; baseline reuse is allowed only for the same clean source/filter/OS and must be recorded rather than claimed as another execution.

Ordinary plus Mutation verification floors total **148 minutes**; including Code authoring/test-first work yields **221–251 minutes across separate stages**, before queue waits. Pending Mutation does not block ordinary Review. Measured savings here are zero (no runs were profiled); avoiding compulsory repeat rounds preserves the 40-minute ordinary floor instead of multiplying it. No full Unit sweep, tool bootstrap, real provider, deployment or unrelated flake isolation is included.

Next stage: **Code**, after CARD-0778's confirmed land and a fresh source-area collision check. Implement S1-S3 against this frozen roster; preserve the scoped Windows lane. TestDesign ran only static census/manifest checks, with zero builds/tests/mutations on either OS.

## Code evidence, 2026-10-02 (task 50a6b1fb)

The Code start ref was d81ff99ce3e3bc95a571e778081b19a68c009a17. The required baseline diff from fccef27e to this ref found exactly one changed cited file: tests/Antiphon.Tests/Application/GrokRulesLaunchRefusalTests.cs gained a CARD-0882 Linux gate for its two named-agent Windows-policy rows and strengthened three Windows assertion labels. The cited Pty runner, SessionRunner runtime/routes/dispatcher, Pty tests, Grok runner tests, and shared Herdr fixture had no baseline drift. CARD-0778 was already landed. This Code slice neither changed nor credited the CARD-0882 gate.

The 13-path footprint is the planned five production paths, seven test/probe paths, and this plan. The PhoneHomeCommandDispatcher change is one typed catch. No server, migration, checkpoint tool, bundle, provider, or deployment file changed. Windows keeps the original escape branch because the new condition only excludes non-Windows. The original and placed vectors are validated before native spawn, and the early runtime guard runs after platform validation and before launch-lock/custody work. Unknown backend and Herdr stay on their existing paths.

| Evidence | Commit / result | Fresh TRX |
|---|---|---|
| CP-1 test-first red | d2c4b85cbee4789e098db15bf5579ae9dca7ea3e; build ok, 24 executed, 7 pass, 17 fail, 0 skip. Named native-argv-exact, pure NUL exception, and pre/post-containment assertions failed. The earlier f215a4be attempt was a compile error and is not credited as red. | .antiphon/c863-linux-red-2/CP-1-20261002-112025-9daa/run.trx |
| CP-1 green | 52c90ccaa37826731e114dd56d0e0bd6fd607ffd; build ok, 24/24 pass, 0 fail/skip; UnixPtyArgvTests 17, LinuxCgroupContainmentTests 7. | .antiphon/c863-linux-green/CP-1-20261002-112151-8641/run.trx |
| CP-2 test-first red | b56b2b643521707cfa833373c2115aa914bc1ec6; build ok, 50 executed, 41 pass, 9 fail, 0 skip. Named registration, disk-effect, HTTP status, phone-home code and pre-registration NUL assertions failed. | .antiphon/c863-linux-red/CP-2-20261002-112758-565d/run.trx |
| CP-2 green | 405ec6a5981f5ee215bda7cdbfb2a95e1368dcd2; build ok, 50/50 pass, 0 fail/skip; GrokRulesFileLaunchTests 21, GrokRulesRunnerRefusalTests 11, GrokRulesStoreFailureTests 6, UnixPtyArgvAdmissionTests 12. Rerun 1 after owned-host teardown and custody fixture improvements; earlier fa0536b2 green was 50/50. | .antiphon/c863-linux-green-final/CP-2-20261002-114804-887b/run.trx |
| CP-3 green | 392da64bad50395c242a0bdcdefc62656104b0d2; build ok, 2/2 pass, 0 fail/skip; Investigate and Code real composition, owned native child. Two earlier attempts failed compilation on the new test limiter namespace and are not credited as red. | .antiphon/c863-linux-green-3/CP-3-20261002-114046-2bdc/run.trx |

The exact Linux filters were the frozen CP-1, CP-2, CP-3 filters above, with literal pipes. The three final green TRX files contain the expected class counts, 76 executed, 76 passed, zero failed or skipped. CP-4..CP-6 (69 Windows executions), Windows native backend behavior, and all PC-1..PC-16 method-scoped SourceLanding mutations remain pending on their designated later lanes. No real provider was launched. The dispatch's Final verification profile explicitly requires one ordinary Unit lane; this supersedes the older plan exclusion at line 338. Its result is recorded in the task report after it finishes.

### Requirement trace against the frozen plan

The plan line numbers below refer to the 401-line TestDesign text above this appendix. File:line points to the test assertion (or explicit test fixture invariant); Yes means implemented and exercised on Linux. Windows-only assertions are implemented but marked Pending execution. Historical observations, rejected alternatives, future deployment and post-land Mutation instructions are tracked in the evidence and pending statement above rather than treated as new Code requirements.

| Plan line | Requirement | Test and assertion label at file:line | Yes/no |
|---:|---|---|---|
| 7 | Unix args preserve newlines, quotes, slash, empty, Unicode, and literal shell text | V-1 native-argv-exact, UnixPtyArgvTests.cs:50 | Yes |
| 7 | NUL refuses before truncated native exec with stable code | V-2 named-nul-code / nul-no-native-spawn, UnixPtyArgvTests.cs:144 and :78 | Yes |
| 7, 43-48 | Remove exactly two gates, execute all eight Linux rows | R-1 native-argv-exact-bytes / native-nul-before-registration, GrokRulesFileLaunchTests.cs:103 and :79; GrokRulesRunnerRefusalTests.cs:181 | Yes |
| 9, 63 | Windows escape and modern branch unchanged; Unix no CRT quoting | V-1 native-argv-exact, UnixPtyArgvTests.cs:50; V-9 windows-native-argv-exact, WindowsPtyArgvNativeTests.cs:38 | Pending Windows execution |
| 9, 48 | Unix multiline Grok remains admitted; Windows raw rules still refuse | R-1 native-argv-exact-bytes, GrokRulesRunnerRefusalTests.cs:181; Windows refusal, GrokRulesRunnerRefusalTests.cs:159 | Pending Windows execution |
| 11, 73 | Early runtime refusal before registration and custody | V-5 nul-before-registration / nul-before-disk-effects, UnixPtyArgvAdmissionTests.cs:52 and :55 | Yes |
| 11, 26, 75 | Direct guard scans original and placed vectors before journal/spawn | V-10 original-nul-before-placement, UnixPtyArgvTests.cs:134; V-4 final-nul-before-start-intent, UnixPtyArgvTests.cs:117 | Yes |
| 11, 30, 80-84 | Named refusal crosses ordinary HTTP, constrained HTTP and phone-home | V-6 http-nul-status/type, UnixPtyArgvAdmissionTests.cs:82 and :84; V-7 phone-home-nul-code, UnixPtyArgvAdmissionTests.cs:117 | Yes |
| 26, 63 | Tracked containment rewrite and start-intent/tracking order preserved | V-3 tracked-native-argv-exact / tracked-journal-order, UnixPtyArgvTests.cs:97 and :98 | Yes |
| 29, 48, 243 | Native capture compares complete bytes including separators and completes atomically | R-1 native-argv-exact-bytes, GrokRulesFileLaunchTests.cs:103 and GrokRulesRunnerRefusalTests.cs:181; V-8 composed-native-argv-exact-bytes, UnixDelegateLaunchArgvTests.cs:55 | Yes |
| 48, 54 | Existing platform assertions and historical test names preserved; CARD-0882 remains separate | R-1 Windows refusal, GrokRulesFileLaunchTests.cs:68; CARD-0882 Windows code, GrokRulesLaunchRefusalTests.cs:134 | Pending Windows execution |
| 63 | Caller vector is not normalized or mutated | V-1 caller-vector-unchanged, UnixPtyArgvTests.cs:51 | Yes |
| 69 | Exception has code, reason, and 0-based executable / 1-based argument index | V-2 named-nul-code / nul-index, UnixPtyArgvTests.cs:144 and :146 | Yes |
| 69 | Diagnostic excludes executable, arguments, and synthetic sentinel | V-2 nul-sanitized / nul-no-executable, UnixPtyArgvTests.cs:148 and :149 | Yes |
| 69 | Scan start, middle, end, duplicate flags and past terminator | V-2 four argument rows and nul-index, UnixPtyArgvTests.cs:60-78 and :146 | Yes |
| 69 | Empty and non-NUL controls remain unchanged | V-1 empty/cr/native-argv-exact, UnixPtyArgvTests.cs:28-50 | Yes |
| 73 | Platform mismatch keeps precedence | V-11 platform-precedence, UnixPtyArgvAdmissionTests.cs:166-167 | Yes |
| 73 | Unknown backend and Herdr are excluded from native NUL guard | V-11 unknown-backend-keeps-existing-verdict / herdr-script-preserves-nul, UnixPtyArgvAdmissionTests.cs:162 and :152 | Yes |
| 75 | Tracked launch still consumes a rejected attempt | V-10 tracked-attempt-consumed, UnixPtyArgvTests.cs:139 | Yes |
| 82 | HTTP returns 409 with type/title/detail, without private value | V-6 http-nul-status/type/title/sanitized, UnixPtyArgvAdmissionTests.cs:82-86 | Yes |
| 84, 240 | Phone-home retains accepted generation and request identity | V-7 phone-home-retains-watermark / RequestId, UnixPtyArgvAdmissionTests.cs:122 and :115 | Yes |
| 88 | Owned Node child captures process.argv as JSON, atomically | V-1 native-argv-exact, UnixPtyArgvTests.cs:50; probe fixture, probes/argv-echo.js:5-8 | Yes |
| 88, 92 | No provider binary/network; owned process is killed and awaited | V-1 fixture teardown, UnixPtyArgvTests.cs:217-231; V-8 owned teardown, UnixDelegateLaunchArgvTests.cs:62-72 | Yes |
| 92, 245 | Native deadline bounds harness, never establishes success by time alone | V-1 CaptureAsync completion, UnixPtyArgvTests.cs:203-212; V-8 capture, UnixDelegateLaunchArgvTests.cs:48-55 | Yes |
| 92, 245 | Process-spawning classes carry assembly-local limiter | V-1 / V-5 / V-8 / V-9 class attributes, UnixPtyArgvTests.cs:10; UnixPtyArgvAdmissionTests.cs:21; UnixDelegateLaunchArgvTests.cs:13; WindowsPtyArgvNativeTests.cs:10 | Yes |
| 92, 245 | Owned hosts are killed and awaited in finally | R-1 teardown, GrokRulesFileLaunchTests.cs:109-113; GrokRulesRunnerRefusalTests.cs:184-189 | Yes |
| 243 | Shell child uses quoted argv and atomic rename, no eval | R-1 fixture script, GrokRulesFileLaunchTests.cs:253; V-8 fixture, UnixDelegateLaunchArgvTests.cs:26 | Yes |
| 253 | V-1 nine literal rows compare full native array and count | V-1 native-argv-exact, UnixPtyArgvTests.cs:15-50 | Yes |
| 254 | V-2 four rows validate pure and runner refusal, PID/capture absent | V-2 named-nul-code / nul-no-native-spawn, UnixPtyArgvTests.cs:58-79 | Yes |
| 255 | V-3 placement preserves original and placed final argv | V-3 tracked-native-argv-exact, UnixPtyArgvTests.cs:83-99 | Yes |
| 256 | V-4 final executable/argument NUL before journal | V-4 final-nul-before-start-intent, UnixPtyArgvTests.cs:103-121 | Yes |
| 257 | V-5 four runtime shapes have zero registration/disk effects | V-5 nul-before-registration / nul-before-disk-effects, UnixPtyArgvAdmissionTests.cs:24-57 | Yes |
| 258 | V-6 both real isolated routes name 409 without registration | V-6 http-nul-status / http-no-registration, UnixPtyArgvAdmissionTests.cs:62-88 | Yes |
| 259 | V-7 both operations return named frame and retained watermark | V-7 phone-home-nul-code / phone-home-retains-watermark, UnixPtyArgvAdmissionTests.cs:93-122 | Yes |
| 260 | V-8 two role-default compositions compare full child bytes and identity position | V-8 composed-native-argv-exact-bytes / composed-identity-position, UnixDelegateLaunchArgvTests.cs:40 and :55 | Yes |
| 261 | V-9 two Windows backend native captures, no fallback | V-9 backend-no-fallback / windows-native-argv-exact, WindowsPtyArgvNativeTests.cs:30 and :38 | Pending Windows execution |
| 262 | V-10 pre-placement refusal and consumed attempt | V-10 original-nul-before-placement / tracked-attempt-consumed, UnixPtyArgvTests.cs:134 and :139 | Yes |
| 263 | V-11 platform/backend/Herdr verdicts stay intact | V-11 platform-precedence / unknown-backend-keeps-existing-verdict / herdr-script-preserves-nul, UnixPtyArgvAdmissionTests.cs:152-167 | Yes |
| 275 | R-1 all 38 source results, seven ungated Linux cases | R-1 native-argv-exact-bytes / native-nul-code, GrokRulesFileLaunchTests.cs:75 and :103; CP-2 roster above | Yes |
| 276 | R-2 seven containment contracts remain intact | R-2 Place_prefixes_the_root_owned_shim_with_a_separator, LinuxCgroupContainmentTests.cs:27; CP-1 roster above | Yes |
| 277-278, 334 | R-3/R-4 Windows parser, Grok policy and shipped bundles unchanged | R-3/R-4 exact Windows CP-4/CP-6 rows in frozen manifest; V-9 WindowsPtyArgvNativeTests.cs:30-38 | Pending Windows execution |
| 354-387 | Exact Linux checkpoint roster and zero skips | CP-1/2/3 TRX and per-class counts above; run-checkpoint.ps1 receipt in each checkpoint directory | Yes |
