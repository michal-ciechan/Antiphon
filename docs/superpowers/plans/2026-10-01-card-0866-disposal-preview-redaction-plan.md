# CARD-0866: disposal preview redaction and standing-lock evidence

Date: 2026-10-01. Stage: TestDesign complete; Code follows after collision admission. Plan baseline: `fccef27eb03f2522f2a40574dc1c764335d4a490`; Plan landed at `6f55b829405b349c0ae31d5f58ae9afff99cc8b5`, the TestDesign source/branch base (`feat/card-task-502c5876`). This artifact is the only repository change; neither planning stage runs builds/tests, provider launches or live host changes.

## Outcome and scope

**There is a real, cross-platform redaction gap: operator-controlled workspace/tab/pane labels flow verbatim into disposal previews and the persisted reviewed preview. A label containing a Windows or Unix path can therefore expose that path on Windows as well as Linux.** This is a source-proven reachable flow, not a claim that a production secret was observed. Separately, the reported G106 `secret-home` failure is a fixture artifact: an injected fake inspector uses host-dependent `Path.GetFileName` on a Windows path. The production inspector is not called by that test. Fix the fixture and add explicit projection redaction at the production preview boundary; merely replacing the canary with a Linux path would conceal the boundary gap.

G049 is also a fixture defect, not evidence of a Linux semaphore failure: the standing harness selects `cmd.exe`, which is absent on this Linux host. Production Start validates that executable before acquiring its queue locks; a faulted Start task satisfies `IsCompleted == true`. Repair this test's local configuration and replace its 100 ms timing inference with a direct lock probe plus an observed Start preflight. Keep production ownership and Start lock ordering unchanged.

Acceptance: remove exactly the two CARD-0866 Linux skips; `HerdrPaneDisposalEndpointTests.C461_G106_Preview_response_redaction` and `HerdrPaneDisposalApplicationTests.C461_G049_Standing_execution_lock` execute and pass on Linux and Windows. New tests must detect bypassed production redaction at named assertions, and cover preview POST, stored-preview GET, durable reviewed evidence, receipt/status and structured disposal logs. Safe executable basenames, IDs, process incarnation facts and diagnostic reason codes remain useful. Redaction must never turn an unproven process into authority to close it.

CARD-0864 owns native Unix census and conditional locator deletion. This plan does not make real Unix pane disposal operational and does not remove its gates. No DTO schema, database, shared launch resolver, provider readiness, checkpoint tooling, client or deployment change is included.

## Evidence and limits

Owners read: `docs/project-context.md`; the checkpoint manifest, combined-filter, build-slot and mutation rules in `docs/testing-and-build.md`; delegation/scope rules in `docs/orchestration-loop.md`; disposal/standing ownership in `docs/herdr-sessions.md`; relevant session invariants and `docs/ops-http.md`. Templates: CARD-0826 daily-host-cleanup plan at baseline and CARD-0863 Unix argv plan from `origin/feat/card-task-fede62ef`. This Plan is a delegate; it reads its own sources and dispatches no agents.

Measurements use only `git status`, `git rev-parse`, `git show`, `git ls-remote`, `rg`, bounded line-numbered source reads, a Node source census and read-only PowerShell path/command lookup. Adjacent CARD-0864/0881/0883 descriptions were read with `scripts/card.ps1 get ... -Board Antiphon`. Historical Windows W-1 success is supplied by the brief (CARD-0801 rerun 67952db8 at fe6b3631); it is not a fresh Windows execution here. Plan executed zero tests/builds on either OS.

### Trace of the reported path fragment

| Baseline file:line | Observation and implication |
|---|---|
| `tests/Antiphon.Tests/Application/HerdrPaneDisposalEndpointTests.cs:45` | G106 seeds a foreground `C:\secret-home\grok.exe`, cwd `C:\secret-home`, and argv `secret-canary`; it asserts both canaries absent from HTTP JSON. Its Linux skip is at lines 47-48. |
| `tests/Antiphon.Tests/TestHelpers/HerdrDisposalHttpFixture.cs:22` and `:32` | The HTTP chain uses `HerdrPaneDisposalFixture`, then mounts that exact service in a runner app and a server app on random loopback ports. It does not boot real Program. |
| `tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalFixture.cs:44` and `:82` | `RecreateService` injects `TestProcesses`, not `HerdrDisposalProcessInspector`. At line 92 the fake computes `ExecutableName = Path.GetFileName(f.Name)`, then includes the same record in `Foreground` and `Affected`. |
| `src/Antiphon.SessionRunner/HerdrDisposalBackend.cs:41` | `pane.process_info` is passed to the injected inspector. No alternate cwd/claim/locator or exception path is needed to explain G106. |
| `src/Antiphon.SessionRunner/HerdrPaneDisposalService.cs:51` and `:54` | Classification precedes construction, but even refused previews copy `o.Foreground` and `o.Affected` directly. `Clone` at line 235 serializes/deserializes; it is not a redactor. Thus both `foreground[].executableName` and `affectedProcesses[].executableName` carry the Linux fixture's Windows path. |
| `server/Application/Services/HerdrPaneDisposalService.cs:15` | The server forwards the runner preview; the ownership refusal only changes eligibility/blockers/preview ID. It does not remove process text. |
| `src/Antiphon.SessionRunner/HerdrDisposalProcessInspector.cs:23`, `:66` | The real inspector returns an empty/incomplete snapshot off Windows. Windows uses `Process.ProcessName`, not Herdr's raw `name`. The fake's path is not evidence of a real Windows process-name leak. |
| `src/Antiphon.SessionRunner/HerdrDisposalLocatorStore.cs:17`, `:39`, `:46`, `:52`, `:72` | Read returns selected claim metadata and hashes, not cwd/raw file paths. Native `CreateFileW` is in deletion, not G106 preview. Its Linux defect belongs to CARD-0864. |

Read-only measurement on this host:

```powershell
[IO.Path]::GetFileName('C:\secret-home\grok.exe')
# C:\secret-home\grok.exe
$exe = [IO.Path]::Combine([Environment]::SystemDirectory, 'cmd.exe')
# cmd.exe
[bool](Get-Command cmd.exe -ErrorAction SilentlyContinue)
# False
[IO.File]::Exists('/bin/sh')
# True
```

This measures framework path behavior and fixture executable availability only. No provider, test host or production disposal was invoked. `secret-canary` never enters the process DTO: the fake extracts only UUIDs through `HerdrDisposalIdentity.NativeIds` (`HerdrDisposalIdentity.cs:99`); raw argv and cwd are absent from `HerdrPaneDisposalProcess` (`HerdrPaneDisposalContracts.cs:12`).

### Real production exposure, separate from the fixture artifact

`HerdrApiModels.cs:13`, `:24`, `:35` model labels as unrestricted strings. Real `HerdrClient.PaneRenameAsync`/`TabRenameAsync` (`HerdrClient.cs:242`, `:251`) accept labels. `HerdrDisposalBackend.cs:29-32` reads workspace/tab labels and returns the pane label via `Pane`; the production preview builder (`HerdrPaneDisposalService.cs:56`) copies all three without filtering on every OS, before any eligibility-based refusal. A path embedded in a label such as `work C:\secret-home\repo` therefore reaches the preview even when the real Linux inspector refuses its incomplete process inventory.

Execution embeds `p` as `Reviewed` in the durable operation (`HerdrPaneDisposalService.cs:97-101`); `HerdrDisposalReceiptStore.cs:41` serializes the whole operation. Even an ineligible request saves a Refused operation at line 101. The public receipt DTO itself has no label/process/argv fields (`HerdrPaneDisposalContracts.cs:36`). Structured disposal logs at `HerdrPaneDisposalService.cs:207` currently log only IDs, outcome and code; no leak through that template was found. Route error handlers return fixed problem text (`HerdrPaneDisposalRoutes.cs:35-48`); this is not an exception-message explanation for G106. Claim origin/agent-kind strings are copied from persisted locators, so their display projection also needs the same path-text rule; identity evaluation still uses the original claim.

Concrete future reproduction, on an isolated fake-backed fixture only:

1. Remove G106's gate, commit the test change, run the exact CP-2 filter below on Linux with the original fixture. The named `json.ShouldNotContain("secret-home")` assertion is the expected historical red; inspect the two executable-name fields. Do not describe that result as observed by this Plan.
2. For a production-boundary reproduction on either OS, seed the three real wire label fields with synthetic Windows/Unix paths; leave process evidence valid or deliberately incomplete. POST preview, then GET its stored preview. Assert the path sentinel absent. At baseline the label assertion fails independently of the fake's basename behavior.
3. Execute that reviewed fixture with no locator files, then inspect its private `herdr/disposals/<operation-id>.json`: the `reviewed` labels reproduce the persistence exposure. A refused preview also takes the durable path. Use synthetic values only and no operator pane.

### Standing lock diagnosis

The exact test is `HerdrPaneDisposalApplicationTests.C461_G049_Standing_execution_lock` at lines 64-84, gated at 66-67. Reaching `BeforeClose` already proves disposal passed its inventory/refusal checks. Its inspector is the same complete fake, so the production Linux empty census cannot cause this failure.

`StandingRecoveryFixture.cs:26` builds the standing harness with its default registry. `AgentControlServiceIntegrationTests.cs:1762` specifies `Path.Combine(Environment.SystemDirectory, "cmd.exe")`. `StandingRecoveryFixture.StartAsync` (`:47`) returns the real asynchronous Start operation. `AgentControlService.cs:476` calls `EnsureSpawnable` before `:519-523` obtains the ordered singleton queue locks. `AgentExecutableResolver.cs:94-98` throws when `cmd.exe` cannot resolve. The read-only host measurement above confirms that condition. The existing `Task.Delay(100); start.IsCompleted.ShouldBeFalse()` hides the useful fault behind an incidental assertion. If a historical reproduction is separately commissioned, capture/await the early fault to verify its exact signature; neither planning stage executed it, and the frozen Code roster does not require an extra reproduction run.

The production disposal service holds `ownership.AcquireAsync` across `DisposeHerdrPaneAsync` (`server/Application/Services/HerdrPaneDisposalService.cs:38-40`). `HerdrPaneDisposalOwnership.cs:23-28` takes `queue.GetLock(id)` and its lease releases at `:43-50`. These are the same locks Start takes. There is no OS branch in either lock path and no evidence supporting a production lock rewrite.

## Design decisions

### D-1: explicit, conservative display projection

Add an internal concrete `HerdrDisposalRedactor` in SessionRunner, used once when constructing the preview **after classification on the raw observation**. Store and return that redacted preview, so POST, stored-preview GET and new durable Reviewed records share one policy. Keep the raw observation stamp private for exact execution revalidation. Redaction does not change PID, parent PID, creation time, native UUIDs, claim session IDs, completeness, eligibility, blockers, planned PIDs, pane/workspace/tab/terminal/backend instance identity or expiry.

For workspace/tab/pane labels and claim Source/Origin/AgentKind display text, replace the **entire value** with a fixed `[redacted]` marker if it contains `/`, `\`, a drive-prefix pattern (`[A-Za-z]:`, including drive-relative paths), or control characters. Preserve null and safe plain text. The conservative whole-value rule handles quoted paths, spaces, UNC, mixed separators, relative paths, and embedded paths without guessing where a fragment ends. Basenames such as `grok.exe` are allowed process evidence: for Shell/Foreground/AffectedProcesses independently, extract the leaf using both separators on every OS, strip a drive prefix, and return null for an empty/unsafe leaf. Do not use host-dependent `Path.GetFileName` or filesystem existence as the policy. Apply the same display-text rule to BackendVersion, which is descriptive text, while keeping backend identity opaque and unchanged.

This is a path-data contract, not detection of arbitrary secrets pasted into otherwise plain labels. No raw argv/cwd/provider-home fields are added. Existing typed identities and reason-code vocabularies stay outside display redaction. No response-body regex or recursive mutation of arbitrary IDs is introduced.

Rejected: fixture-only native paths (leaves real label exposure); Windows-only redaction (cross-platform strings exist); basename-only label trimming (retains private directory names); surgical substring regex (quoted/spaced/mixed paths are ambiguous); redacting raw observations before classification (could manufacture eligibility); changing durable schema or scrubbing old receipts in place (unnecessary migration and outside authorized custody). Existing stored evidence is not retroactively rewritten; new evidence is protected after activation.

### D-2: faithful fixture plus adversarial boundary coverage

Fix only `HerdrPaneDisposalFixture.TestProcesses` basename emulation to understand both separators independently of the production redactor. This models the real inspector's basename contract and makes the original mixed-OS fixture useful on both hosts. Retain the original Windows-style G106 executable/cwd/argv input and add tainted wire labels, safe basename assertions and an eligible-preview assertion. Removing the production redactor must still fail G106 on its named `secret-home` exclusion; the fixture must not pre-redact those labels.

New runner tests inject path-bearing process DTOs through `Backend.Transform` **after** fake inspection to test production output redaction without relying on fixture normalization. They explicitly keep raw identity refusal. Rejected: importing the production redactor into the fixture (self-comparison can mask a bypass); stripping assertions or accepting Linux-only skips; using real OS PIDs/providers for text projection.

### D-3: repair G049 locally and prove ownership without a sleep

Use `StandingRecoveryFixture`'s existing `configureServices` hook to override only this test's fake `AgentRegistrySettings`: both OSes use the absolute, existing `Environment.ProcessPath` of the running test host. TestDesign replaces the Plan's cmd.exe versus /bin/sh choice so no OS-specific shell is required. Preserve definition `fake` and kind `ClaudeCode`; supply a `FakeAgentProtocolAdapter` rather than the current empty adapter queue. No executable is launched by this configuration: the fake adapter factory owns dispatch. Do not edit the shared harness or weaken `EnsureSpawnable`.

At the backend close barrier, probe the actual `SessionMessageQueueService.GetLock(f.B.Id)` with `WaitAsync(0)` and assert false; if it unexpectedly succeeds, release the probe in `finally` before asserting. Install the existing `FakeSessionRunnerClient.ListOverride` hook immediately before Start to signal that preflight has been reached. Race that signal against Start completion, and await/report a premature fault rather than treating it as a lock result. The hook also probes the lock while disposal is paused; return an empty runner list. Assert no launch ownership and no completed Start while the close barrier is held, then release it in `finally`, await successful disposal/Start, and drain the fake launch queue. Assert the lock is acquirable again after completion, releasing the probe.

The decisive held-lock assertion is the nonblocking semaphore probe, independent of scheduler progress. TCS barriers use `RunContinuationsAsynchronously`; bounded waits exist only as deadlock diagnostics, never expected mutation reds. Enclose barrier arrival, every probe and all Start assertions in the release `try/finally`, and drain/observe both pending tasks even on assertion failure. A probe that unexpectedly acquires the gate must always release it. Do not let a primary assertion failure become a teardown hang. No real-time margin or fake-clock advance can prove a semaphore is held. The fixture's `Clock.Now` is pinned for new preview/receipt tests; no expiry test sleeps. Rejected: extending the 100 ms delay, swallowing Start exceptions, removing the blocked assertion, moving production locks or adding a new lock seam when the singleton queue and runner hook already exist.

### D-4: separate portability ownership and minimize collisions

Keep CARD-0864's native inspector/locator APIs and existing Linux gates untouched. Our new fixtures have no captured locator files, so receipt persistence/cleanup is portable without calling `CreateFileW`. Do not claim a successful fixture execution qualifies real Unix disposal. Prefer server2 for all portable rows; commission Windows only for the focused cross-OS redaction and the two formerly gated tests.

Rejected: coupling this repair to an unchosen Unix deletion algorithm or broadly ungating CARD-0864; modifying shared fake transport, standing harness, launch code or checkpoint tooling to solve local test setup.

## Exact implementation footprint and slices

Only this plan changes in Plan/TestDesign. The future Code footprint is closed:

| Slice | Exact files | Test-first work |
|---|---|---|
| S1: fixtures and detecting contracts | Modify `tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalFixture.cs`, `tests/Antiphon.Tests/Application/HerdrPaneDisposalEndpointTests.cs`, `tests/Antiphon.Tests/Application/HerdrPaneDisposalApplicationTests.cs`; add `tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalRedactionTests.cs` | Add the roster below against the existing service; locally correct fake basenames and G049 registry/adapter/barriers. Remove only the two CARD-0866 gates. G106 keeps its original sentinels and gains tainted labels. Commit/push all compiling tests; CP-1/CP-2 red rounds must show the named path-exclusion failures. G049 should already pass with its local fixture repair; no production lock change is proposed. |
| S2: production projection and documentation | Add `src/Antiphon.SessionRunner/HerdrDisposalRedactor.cs`; modify `src/Antiphon.SessionRunner/HerdrPaneDisposalService.cs`, `docs/herdr-sessions.md`, and this plan | Add projection after raw classification and before preview caching/persistence. Document path-label masking and the distinction from CARD-0864's platform support. Commit/push, run final CP-1/CP-2, then scoped Windows CP-3/CP-4 at the same source SHA. |

No change to project files is needed for C# wildcard inclusion. Keep helper code for the new runner tests in its new test file, including minimal runner HTTP hosting and a logger that records structured state/exception data (the existing ListLogger records formatted strings only). Reuse the fake transport, standing fixture injection hook and fake runner ListOverride; their files are read-only dependencies. The server disposal/ownership classes, `AgentControlService`, shared standing harness, inspector, locator and receipt-store implementations are inspected but not edited.

**Scope:** `src/Antiphon.SessionRunner/HerdrDisposalRedactor.cs,src/Antiphon.SessionRunner/HerdrPaneDisposalService.cs,tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalFixture.cs,tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalRedactionTests.cs,tests/Antiphon.Tests/Application/HerdrPaneDisposalEndpointTests.cs,tests/Antiphon.Tests/Application/HerdrPaneDisposalApplicationTests.cs,docs/herdr-sessions.md,docs/superpowers/plans/2026-10-01-card-0866-disposal-preview-redaction-plan.md`.

| Other work | Exact overlap / mapped area | Code ordering |
|---|---|---|
| CARD-0864 | Its named native files `HerdrDisposalLocatorStore.cs` and `HerdrDisposalProcessInspector.cs` are not edited here. Its ungating work is in the same disposal tests and may share `HerdrPaneDisposalFixture.cs`; no finalized 0864 plan was available. Both occupy `herdr` and `runner`. | **Serialize Code.** Prefer 0866 first so 0864 inherits platform-independent fixtures and display projection; if 0864 is admitted first, wait for land and re-read/recount. There is no functional dependency on Unix deletion for these new fixture rows. |
| CARD-0835 Code | No exact overlap with checkpoint tools, scripts, Review/land services, AgentTaskReplyService/AgentTaskService, LandApproval, EF artifacts or testing documentation. | No source-based wait. Consume the unchanged driver and commit-before-run rule. |
| CARD-0778 Code | No edit to Grok readiness, `tests/Antiphon.Tests/Agents` or shared agent fixtures, nor to `src/Antiphon.Agents.Pty/GrokStartupReadiness.cs`; this task does not enter mapped `pty`. | No wait from the supplied footprint. A later expanded runner scope must be rechecked. |
| CARD-0881 Plan | No overlap with effective-settings services, API, bundles, AGENTS, orchestration/ops docs or policy tests described on the card. | TestDesign can proceed; no dependency on that endpoint. Use the current settings front doors when dispatching. |
| CARD-0883 Plan | No overlap with source recovery, landing request concurrency, diagnostics or landing tests described on the card. | No source-based wait. No land mechanics are changed here. |
| CARD-0863 Plan | No exact file overlap with its argv/runtime/routes/phone-home changes or argv tests. Both occupy mapped `runner`. | **Serialize their Code while either occupies runner.** Plans/TestDesign may proceed independently. |
| Held CARD-0788 | No exact implementation file overlap. Its declared `tests/Antiphon.Tests/Application/**` scope intersects our two application test files. | **Wait if that Code is in flight under the broad scope**; otherwise caller may commission 0866 first. No land dependency while it remains held. |
| Held CARD-0505 / CARD-0822 | No exact overlap with their settings/dispatch/schema/instruction files or named tests. No change to their shared services is needed by the local G049 override. | No source-based dependency; recheck effective active scopes before admission. |
| Held CARD-0826 | No exact overlap with cleanup services, runner command/dispatch/packaging files, checkpoint extraction or its docs. Both occupy mapped `runner`. | **Serialize Code when admitted.** No requirement to implement cleanup before this repair. |

This table compares the supplied in-flight/held list, committed plans, read-only adjacent-card descriptions and `antiphon.areas.json`; it is not a live occupancy claim. Before Code the orchestrator must read `/api/agent-tasks/pipeline`, `/api/session-runners`, `/api/runner-defaults`, `/api/hosts` and compare actual scopes. Do not alter budgets, broaden the area map or override a same-area collision. Plan itself has only a documentation footprint and does not need to wait.

## Verification design

TestDesign frozen on 2026-10-01 at source `6f55b829405b349c0ae31d5f58ae9afff99cc8b5`. `card.ps1 get CARD-0866 -Board Antiphon` confirmed the disposal preview/standing-lock card, High/Soon, and the Plan finding superseding the earlier Linux-only interpretation. This stage changes only this plan. No build, test, provider, native process or live disposal was run.

Corrections to the proposed roster: use the current test-host executable for G049 preflight, not either OS shell; apply every display case to actual preview responses **and** durable `reviewed` evidence; capture structured logger state/exception as well as rendered messages; add explicit raw claim and execution-stamp witnesses. The six new method names remain, but V-1 grows from seven to thirteen argument results. Final new count is **21**, not 15. The ten original PCs remain with their numbering; PC-11..13 cover over-redaction, raw execution stamps and exact raw claim matching that the original PCs did not independently detect.

### Inspection

Bodies read, rather than inferred from method names:

| Boundary / source read | Coverage or deliberate exclusion |
|---|---|
| `HerdrPaneDisposalFixture.cs`, `FakeHerdrServer.cs`, `HerdrDisposalBackend.cs`, `HerdrDisposalIdentity.cs`, `HerdrPaneDisposalContracts.cs` | V-1/2/4/5/7: wire labels remain adversarial; fake basename normalization is independent of production projection; raw facts classify before display projection. |
| Runner `HerdrPaneDisposalService.cs`, `HerdrPaneDisposalRoutes.cs`, `HerdrDisposalReceiptStore.cs`, `ListLogger.cs` | V-1..6: construction/cache/HTTP/disk/restart/log surfaces. `ListLogger` alone omits structured state and exception contents; V-6 adds a local recording logger in the new test file. |
| All five selected existing test source files listed in the census below, `HerdrDisposalHttpFixture.cs` | R-1..3, V-7: real server-to-runner routes over random loopback sockets, fake Herdr/inspector. ExecutionWire is a partial class, not another filter operand. |
| `StandingRecoveryFixture.cs`, `AgentControlServiceIntegrationTests.BuildHarness`, `AgentExecutableResolver.cs`, `AgentControlService.StartInteractiveSessionAsync` lock/preflight path, `FakeAgentProtocolAdapter.cs`, `FakeSessionRunnerClient.cs` | V-8: local options override, real queue singleton and Start path, fake adapter launch, observable runner preflight. Shared helpers are read-only. |
| Server `HerdrPaneDisposalService.cs`, `server/Infrastructure/Agents/SessionRunner/HerdrPaneDisposalOwnership.cs` | V-8, PC-9/10: Acquire and lease lifetime through runner I/O; do not change production lock ordering. |
| Locator/inspector, the four disposal test files containing CARD-0864 gates; adjacent committed plans and `antiphon.areas.json` | Native Unix census/deletion remain CARD-0864. No gated native row enters this roster. |

### Recounted source roster

Read-only Node census counted `[Test]` methods and their preceding `[Arguments]`, then inspected helper calls and OS gates. No Matrix, dynamic data-source or Repeat expansion exists in these selected classes. Internal calls/loops do not add TUnit results. These counts describe source, not observed passes.

| Existing class | Methods | Expanded results | Baseline Linux skips / Windows skips | Final Linux selection / focused Windows selection |
|---|---:|---:|---:|---:|
| `HerdrPaneDisposalEndpointTests` | 8 | 14 | 1 / 0 | 14 / 1 (G106) |
| `HerdrPaneDisposalApplicationTests` | 13 | 14 | 1 / 0 | 14 / 1 (G049) |
| `HerdrPaneDisposalHttpWireTests` | 7 | 9 | 0 / 0 | 9 / 0 |
| `HerdrPaneDisposalIdentityTests` | 22 | 35 | 0 / 0 | 35 / 0 |

Exact existing names and result multiplicities (omitted multiplicity means **one**):

- Endpoint: `C461_G001_Exact_pane_target` (4), `C461_G002_Full_expected_identity` (3), `C461_G009_Reason_required` (2), `C461_G010_No_force_bypass`, `C461_G013_Runner_feature_gate`, `C461_G106_Preview_response_redaction`, `C461_G116_Receipt_response_redaction`, `Routes_validate_preview_execute_and_status`.
- Application: `C461_G047_Persisted_stop_intent`, `Ownership_refused_preview_cannot_be_reused_after_Stop`, `C461_G048_No_launch_generation`, `C461_G052_No_implicit_stop`, `C461_G105_No_false_PaneLeftOpen_incident`, `Stopped_current_target_is_eligible_after_launch_release`, `C461_G049_Standing_execution_lock`, `C461_G050_No_transaction_over_rpc`, `C461_G051_Replacement_owner_untouched`, `C461_G102_No_synthetic_session`, `C461_G104_Preserve_exit_history`, `Disposes_detached_exited_and_rowless_targets` (2), `C461_G111_Explicit_disposal_only`.
- Wire, `tests/Antiphon.Tests/Agents/HerdrPaneDisposalHttpWireTests.cs`: `Refusal_and_durable_status_round_trip_through_both_route_families`, `Old_runner_refuses_before_any_disposal_request`, `Invalid_prefixes_missing_panes_and_unknown_operations_keep_typed_status`. Its partial `HerdrPaneDisposalExecutionWireTests.cs`: `All_disposal_outcomes_round_trip` (3), `C461_G015_Distinct_guard_wire`, `C461_G109_Typed_status_receipt`, `Server_runner_status_round_trip_survives_lost_post_reply`.
- Identity: `Accepts_only_current_positive_process_evidence` (3), `C461_G003_Both_expected_identities`, `C461_G016_Positive_association`, `C461_G017_Contradictory_claims` (2), `C461_G018_Complete_inventory` (3), `C461_G019_Verified_shell` (2), `C461_G020_Valid_process_ids` (2), `C461_G021_Readable_creation_identity` (2), `C461_G022_Exact_recorded_child_identity` (2), `C461_G023_Legacy_sidecar_is_not_exact` (2), `C461_G024_Native_source_antiphon`, `C461_G025_Native_incarnation_binding`, `C461_G026_Conflicting_native_facts`, `C461_G027_Supported_kind` (3), `C461_G028_Executable_family`, `C461_G029_Exact_native_uuid`, `C461_G030_Codex_positive_identity`, `C461_G031_Single_agent_root`, `C461_G032_Complete_affected_tree` (2), `C461_G033_No_pending_input`, `C461_G034_No_pending_backend_launch`, `C461_G035_Token_is_not_process_authority`.

The server denominator is `14 + 14 + 9 = 37`, currently 35 non-skipped plus two Linux skips. Remove only Endpoint lines 47-48 and Application lines 66-67 at this baseline. The identity expansion is `3+1+1+2+3+2+2+2+2+2+1+1+1+3+1+1+1+1+2+1+1+1 = 35`.

CARD-0864's **23** excluded Linux results were also traced through `SkipLocatorOnLinux` and its callers: eight `Disposes_each_supported_evidence_shape` sidecar/last-pane cases; G097/G098 two each; G012/G055/G056/G063/G067(true)/G099/G100/G101/G107, `Cleanup_is_conditional_and_census_is_observable`, and `Protocol20_preview_is_read_only_and_redacts_process_arguments` one each (`8+4+11=23`). They live in `HerdrPaneDisposalGuardTests.cs`/`HerdrPaneDisposalServiceTests.cs` (partial `HerdrPaneDisposalServiceTests`), `HerdrPaneDisposalStopRegressionTests.cs`, and `HerdrPaneDisposalConcurrencyTests.cs`. Their helper reuse does not add executions here; the identity class's G023 writes locator evidence but never executes deletion.

### Delivery inventory

No message, transcript, provider-delivery or notification queue is changed. UserPrompt evidence is therefore not a substitute needed for these HTTP results. The only queue exercised is the existing standing launch queue in V-8; receipt there means the fake adapter's `StartedSessionId`, not enqueue acceptance.

| Producer -> destination | Identity and persistence | Recovery / observable receipt |
|---|---|---|
| Fake Herdr wire fields + fake inspector -> real runner preview projection -> HTTP caller | `PreviewId`, pane/terminal IDs, expected UUIDs; private in-memory Review; no file on preview | V-1/2/4 assert POST body, then runner GET `/herdr/pane-disposals/previews/{id}`. V-7 traverses server and runner HTTP. |
| Reviewed preview -> execution -> real receipt store | New `OperationId`, request fingerprint, `PreviewId`; actual JSON file contains `reviewed` | V-1..5 read `<fixture SessionLogPath>/herdr/disposals/{operationId:N}.json`, after execution/refusal. Absence of a file is a failure, never redaction evidence. |
| Closed/Refused/Unknown operation -> status/recreated service | Same operation ID and fingerprint on disk; fresh service has no in-memory preview | V-3 proves exact typed status and unchanged close count after service recreation and an identical retry. Unknown plus indeterminate process liveness remains Unknown. No destruction replay. |
| Server execute -> ownership lease -> runner close; competing standing Start -> real launch queue -> fake adapter | Claim/current session `f.B.Id`; real singleton queue semaphore and persisted suspended state | V-8 verifies held gate while close is paused, successful release, queue drain, fake adapter Started/session ID and available gate. No new queue/recovery policy; unchanged enqueue/crash recovery is outside this redaction card. |

### Frozen redaction matrix and fixture contract

Implement six new methods in `tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalRedactionTests.cs`, `[Category("Integration")]`. All test-local helpers stay in that file. For V-1/2/4 mount **real runner disposal routes** in a minimal `WebApplication` bound to loopback port 0, injecting the fixture service; use the existing HTTP fixture's hosting pattern without starting Program. The runner test project already references `Microsoft.AspNetCore.App`. Assert actual POST and stored-preview GET bodies, then execute once and inspect real disk JSON. Thus every case reaches a preview response and persisted reviewed evidence; a service-only serialization comparison does not stand in for either.

Use `Clock.Now = 2026-10-01T00:00:00Z`, fixed process timestamps and synthetic UUIDs. Keep `Files=[]`: no sidecar/last-pane creation or native cleanup for the new cases. Wire labels are set on `Fake.Workspaces[0]`, its selected tab and pane; never pre-redact them. Transform only the claim descriptive fields and backend version, or the adversarial process records described below. Record raw identities before projection. After HTTP output, compare decoded field values as well as ASCII canary exclusion in JSON: Unicode/control JSON escaping must not produce a false green. Assertions name the surface, field and case key. Do not invoke the production redactor to calculate expected values.

V-1 `Preview_redacts_path_labels(string caseKey)` has exactly these **13 `[Arguments]` keys**, no data-source expansion. The Path column is a runtime string (backslashes literal); `long` is generated deterministically in memory.

| Key | Path/value | Expected display value |
|---|---|---|
| `windows` | `C:\secret-home\repo` | `[redacted]` |
| `posix` | `/secret-home/repo` | `[redacted]` |
| `windows-home-user` | `C:\Users\c866-user\.codex\secret-home` | `[redacted]` |
| `posix-home-user` | `/home/c866-user/.claude/secret-home` | `[redacted]` |
| `unc` | `\\c866-host\private-share\secret-home\repo` | `[redacted]` |
| `long` | `C:\Users\c866-user\` + 8192 `x` characters + `\secret-home\repo` | `[redacted]` |
| `unicode-windows` | `C:\Users\测试用户\秘密\secret-home` | `[redacted]` |
| `unicode-posix` | `/home/δοκιμή/秘密/secret-home` | `[redacted]` |
| `embedded-backslash` | `status folder\secret-home ready` | `[redacted]` |
| `embedded-slash` | `status folder/secret-home ready` | `[redacted]` |
| `drive-relative` | `C:secret-home` | `[redacted]` |
| `mixed` | `C:/Users/c866-user\secret-home/repo` | `[redacted]` |
| `control` | `control-secret` followed by CR, LF, TAB and U+0000 | `[redacted]` |

For each key, run internal raw and `work "<value>" selected` label forms and complete/incomplete inventory scenarios on fresh fixtures; these loops do **not** multiply the 13 TUnit results. Set WorkspaceLabel/TabLabel/PaneLabel, claim Source/Origin/AgentKind, BackendVersion to that value. Use an idle shell with token claim, so tainting claim AgentKind does not itself remove eligibility; the complete case must close once, the incomplete case must refuse IdentityUnproven and close zero times. Assert every listed field equals `[redacted]` in **POST, GET, and disk `reviewed`**; check `secret-home`, `c866-user`, `c866-host`, `private-share` or `control-secret` absent when present in the input. Verify IDs, protocol, incarnation facts, expiry, expected UUIDs, refusal code and planned PIDs against literal fixture facts. The 8192 length exceeds typical path limits without any filesystem access to that path.

V-2 `Preview_projects_process_basenames_without_changing_identity(string style)` has exactly **four `[Arguments]`**, `windows`, `posix`, `unc`, `mixed`. Use respectively `C:\<canary>\<leaf>`, `/<canary>/<leaf>`, `\\host\<canary>\<leaf>`, `C:/<canary>\nested/<leaf>`. After fake inspection inject distinct `shell-path-secret/pwsh.exe`, `foreground-path-secret/grok.exe`, `affected-path-secret/worker.exe` records through Transform. Make Affected contain coherent shell/agent copies plus the worker descendant; preserve positive PIDs, parents, creation times and native UUIDs. Shell's raw path deliberately makes this preview IdentityUnproven. Assert expected leaves and each field-specific canary exclusion in POST, GET and Refused disk `reviewed`; all raw numeric/UUID facts remain exact, Eligible=false, planned PIDs empty, close count zero. Never demand eligibility from path-bearing raw shell evidence.

V-4 `Redaction_preserves_safe_display_and_nulls` is **one result** with explicit internal cases: `grok.exe` (label equal to the process basename), empty label `""`, `Review α 日本語`, plain `c866-user`, null pane label, null claim Origin/AgentKind, null process names, empty foreground and null foreground on incomplete inventory. Empty workspace/tab labels stay empty; their wire types are non-nullable, so do not fabricate null there. Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged. Exercise raw exact-claim metadata in an eligible occupied subcase. On separate refused fixtures, inject process leaves `""`, `"C:"`, `"C:\secret-home\"`, `"/secret-home/"`, and `"bad\u0000.exe"` (actual U+0000); projected names are null in POST, GET and disk. Preserve optional nulls and empty collections distinctly. An eligible safe fixture retains its exact planned PID sequence and closes once. Expected outputs are literals, not helper self-comparisons.

**Boundary:** usernames/home directory names inside paths disappear with the entire display value. A standalone plain username or arbitrary secret in a separator-free label is intentionally preserved by D-1. A requirement to recognize arbitrary secrets/usernames cannot be met by this path policy; do not claim that coverage or silently introduce a secret classifier. Native OS census correctness and legacy on-disk scrubbing also cannot be proved by these fakes; they remain excluded rather than skipped acceptance.

### Proves it works now

| ID / exact method | Results | Decisive assertions and detecting witness |
|---|---:|---|
| V-1 `HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels` | 13 | Matrix above. `path-label-excluded` (JSON exclusion before equality), `display-field-masked`, `durable-review-path-excluded`, complete/incomplete verdict and close counts. PC-2 targets `windows`/`unc`/`drive-relative`; other-OS strings always run on both hosts. |
| V-2 `HerdrPaneDisposalRedactionTests.Preview_projects_process_basenames_without_changing_identity` | 4 | Matrix above. `shell-path-excluded`, `foreground-path-excluded`, `affected-path-excluded` asserted separately before equality, for each surface; `process-facts-preserved`. PC-3/4/5 each bypass only one collection/field. |
| V-3 `HerdrPaneDisposalRedactionTests.Preview_and_stored_review_share_redaction` | 1 | Three fresh fixtures internally: Closed, Refused (`Complete=false`), Unknown (`DropAfterClose=true`, `Processes.Alive=null`). Seed path labels and reason canary. Collect all surfaces before asserting so both cache and disk leaks are diagnosable. `stored-preview-path-excluded` and `durable-review-path-excluded`; exact outcomes/codes and closes 1/0/1; real disk `reviewed.PreviewId`, operation and fingerprint; serialized receipt/status omit canaries. Recreate service on the same root; same-request retry/status preserve outcome, fingerprint and count. For Unknown the terminal is absent but indeterminate original-process liveness prevents AlreadyAbsent. PC-6 leaves POST safe and leaks cache/disk. |
| V-4 `HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls` | 1 | Safe/empty/null and unsafe-leaf cases above; `safe-evidence-preserved` compares literal output, IDs, planned PIDs and eligibility; `unsafe-leaf-null`. PC-11 proves the positive preservation assertions can fail. |
| V-5 `HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity` | 1 | Three isolated scenarios below: raw shell refusal, exact claim authority, and same-leaf raw identity change during execution. PC-7/12/13. No expiry or scheduling race. |
| V-6 `HerdrPaneDisposalRedactionTests.Structured_logs_omit_display_paths_and_reason` | 1 | Inject logger through the runner service's existing internal constructor, passing fixture runtime/root/clock/backend and default real store. Capture formatted messages, structured state values and exception strings locally. Execute a tainted-label eligible fixture with `Reason="reason-secret"`; separately throw `IOException("exception-path-secret")` from BeforeInspect on the first execution reinspection (after preview), and from BeforeClose. Expect Closed/Refused/Unknown respectively, fixed codes and correlated OperationId; `disposal-log-payload-excluded` forbids reason/path/cwd/argv/exception canaries in every captured surface. PC-8 logs request.Reason and fails directly. |
| V-7 `HerdrPaneDisposalEndpointTests.C461_G106_Preview_response_redaction` | 1 existing | Remove Linux skip. Keep original Windows exe/cwd and argv canaries; add Windows, POSIX and UNC wire labels containing `secret-home`. HTTP 200, `json.ShouldNotContain("secret-home")` **before** label/eligibility assertions, and secret-canary exclusion; labels `[redacted]`, `grok.exe`, complete+eligible. Also inspect the fake's raw returned executable name and require `grok.exe`, so production projection cannot conceal a broken fixture. Use server execute/status and the fixture receipt file to assert new evidence stays clean after the HTTP traversal. PC-1 bypasses projection. |
| V-8 `HerdrPaneDisposalApplicationTests.C461_G049_Standing_execution_lock` | 1 existing | Remove Linux skip. The deterministic schedule below must prove held/released semaphore, successful Start and fake launch. PC-9/10 fail the immediate probe, before starting any competing Start. |

V-5 scenarios are frozen:

1. **Raw classifier:** idle fixture with matching token claim; Transform changes Shell and its Affected copy to `C:\identity-secret\pwsh.exe`. POST must expose `pwsh.exe`, while `Eligible.ShouldBeFalse()` and exact IdentityUnproven blocker remain (`raw-identity-still-refused`). Execute Refused, zero closes, safe disk review. Normalizing the observation before `_identity.Refusal` would make this fixture eligible and fail that assertion.
2. **Raw exact claim:** occupied Grok with `native:false`, matching ChildPid/time and AgentKind `grok`; tainted Source/Origin but no captured files. Unchanged raw claim must still make preview eligible and execution Closed. Negative companion fixture changes **only** raw AgentKind to `folder/grok`: response masks it; exact matching must now refuse IdentityUnproven with zero closes (`raw-claim-kind-not-normalized`). No native UUID fallback is present. This proves display projection cannot remove legitimate raw claim authority or manufacture a match from a path-like kind.
3. **Raw execution stamp:** idle shell remains `pwsh.exe`; Affected's copy of that PID has `C:\stamp-a\pwsh.exe`. This is accepted by the current classifier (it requires shell PID presence, not full record equality for an idle shell). Confirm eligible preview and displayed basename. On execution inspections only, change that raw Affected name to `C:\stamp-b\pwsh.exe`, changing no other fact. Both display leaves are identical. Exact raw Stamp must produce Refused/PaneChanged and zero closes (`raw-stamp-changed`), with no raw path persisted. Using projected process records in Stamp would close and fail this assertion. Freeze this fixture's baseline acceptance explicitly; if a separately landed identity tightening rejects it, re-design this witness before implementation rather than credit an unrelated refusal.

### Deterministic standing-lock schedule (V-8)

Use only `StandingRecoveryFixture(configureServices, fakeAdapter)`. Locally replace `IOptionsMonitor<AgentRegistrySettings>` with definition `fake`, Kind `ClaudeCode`, Exe **`Environment.ProcessPath`**, asserting non-null, absolute and existing first. It names the already-running test host (`dotnet` or apphost), is read only by EnsureSpawnable, and is never launched: the injected `QueueAdapterFactory` returns the supplied `FakeAgentProtocolAdapter`. No `cmd.exe`, `/bin/sh`, real shell, real Herdr pane or provider is needed. An unsupported null/missing ProcessPath is a setup failure, not a skip or lock verdict.

1. Seed stopped `f.B`, persisted suspended intent and matching claim. Resolve the exact `SessionMessageQueueService` singleton and `gate=GetLock(f.B.Id)`. Confirm preview eligible. Set BeforeClose to signal `entered`, then await `release`; both TCS use RunContinuationsAsynchronously.
2. Start disposal inside an outer try/finally that already owns `release`. Race entered against disposal completion; surface a premature response/fault. At entered, run `acquired=await gate.WaitAsync(0)`, release immediately if acquired, **then** assert acquired=false (`standing-lock-held`). PC-9/10 therefore fail without even creating the competing Start task; cleanup must still release close and drain disposal.
3. Set FakeSessionRunnerClient.ListOverride immediately before `f.StartAsync(new())`. In the hook probe the same gate, release any unexpectedly acquired permit, record the boolean, signal `preflight`, return an empty list. Do not throw assertions inside the hook (Start wraps some exceptions). Race preflight with Start completion; await/rethrow an early Start fault, otherwise assert the recorded probe false, `start.IsCompleted=false`, `LaunchQueue.Owns(f.B.Id)=false`, adapter.Started=false. The probe is the proof; the completion flag is supplementary, not a scheduler/timing assumption.
4. In finally always release close, observe disposal and any created Start task and drain launch work even after a primary assertion fails. Preserve that first failure; cleanup faults must not replace it. Bounded 15-second waits are deadlock diagnostics only; no sub-two-second timing margin, sleep or fake-clock advance proves a held semaphore.
5. On the green path require disposal HTTP success/Closed, exactly one close, successful Start, idle launch queue, adapter.Started=true and StartedSessionId=f.B.Id. Probe the now-free gate (`standing-lock-released`) and release that permit in finally. The supplied adapter removes the old empty-queue ambiguity.

A deliberately removed/early-disposed ownership lease reaches step 2 and fails a boolean assertion; neither mutant depends on a timeout, process availability or scheduler luck. A mutant that instead hangs or faults setup is not a detecting red.

### Guards the regression

- R-1: the 35 identity results retain exact UUID/process incarnation, shell/family, descendants, complete inventory, live/conflicting claim, pending input/launch refusals. V-5 specifically proves classification and execution use raw facts. Existing unchanged identity gates are exercised here; their entire historical mutation battery is not re-commissioned.
- R-2: the 14 application results preserve Stop intent, owner/session rows, no implicit Stop, no active/queued launch, no transaction spanning RPC and explicit-only disposal.
- R-3: the 14 endpoint and 9 wire results preserve validation, capabilities/guard mode, typed status, lost-reply reconciliation and exact pane.close payload. G116's indirect call contains no seeded path; do not credit its original canary absence as a detecting test. V-3/6/7 supply adversarial inputs.
- R-4: Windows runs the 21 new redaction results plus exactly G106/G049. These qualify cross-platform strings/fake basename/preflight behavior; no native/provider/whole-server Windows suite is selected.

### Guard inventory and positive controls

Thirteen scoped guard/control entries, each with one distinct PC; no missing or duplicate PC mapping. PC-1..10 retain the Plan's controls. PC-11..13 add coverage for the three uncovered contracts identified above. Existing unchanged disposal recovery/ownership policies outside these guards remain R-1..3, not new implementation guards. The seven display fields share D-1's single display-text projection rule; V-1 independently asserts every field on every surface.

Post-land SourceLanding Mutation only, one compiling variant at a time, restored before the next. Exact baseline/red/restored-green method filters are below; all selected method results execute, but **the intended named assertion** must be the failure. Compile/setup errors, timeout, hang, zero tests or teardown failures are never credited. Source inspection establishes reachable witnesses, not executed mutation success; execution evidence remains pending until Mutation.

| Guard / PC | Concrete mutation and production location | Exact detecting method / intended red |
|---|---|---|
| G-1 / PC-1: output boundary | Bypass the production redactor call at runner preview construction; retain the fixed fake inspector. | V-7 `C461_G106_Preview_response_redaction`: JSON `ShouldNotContain("secret-home")` fails on labels. |
| G-2 / PC-2: host-independent path recognition | Remove both backslash and drive-prefix recognition from display-text policy, retaining slash/control recognition. | V-1 `Preview_redacts_path_labels`: windows/UNC/drive-relative input fails `path-label-excluded`; POSIX stays a comparison. |
| G-3 / PC-3: shell projection | Omit only Shell.ExecutableName projection. | V-2 `Preview_projects_process_basenames_without_changing_identity`: `shell-path-excluded`. |
| G-4 / PC-4: foreground projection | Omit only Foreground projection. | V-2 `Preview_projects_process_basenames_without_changing_identity`: `foreground-path-excluded`. |
| G-5 / PC-5: affected projection | Omit only AffectedProcesses projection. | V-2 `Preview_projects_process_basenames_without_changing_identity`: `affected-path-excluded`. |
| G-6 / PC-6: cache/durable boundary | Store the unprojected constructed preview in Review and project only the returned POST value. | V-3 `Preview_and_stored_review_share_redaction`: `stored-preview-path-excluded` and disk `durable-review-path-excluded`, with POST safe. Collect both before assertion. |
| G-7 / PC-7: raw classification | Before `_identity.Refusal`, replace observation Shell/Foreground/Affected names with projected leaves. | V-5 `Redaction_does_not_promote_unproven_process_identity`: raw shell scenario `Eligible.ShouldBeFalse()` fails (`raw-identity-still-refused`). |
| G-8 / PC-8: log minimization | Add `_logger.LogInformation("Disposal reason {Reason}", request.Reason)` in runner ExecuteAsync. | V-6 `Structured_logs_omit_display_paths_and_reason`: `reason-secret` violates `disposal-log-payload-excluded`. |
| G-9 / PC-9: ownership acquisition | Delete the new-operation `await using var lease = await ownership.AcquireAsync(...)` statement in server ExecuteAsync. | V-8 `C461_G049_Standing_execution_lock`: step-2 probe acquired=true, `standing-lock-held`. |
| G-10 / PC-10: lease spans RPC | Keep acquisition, but explicitly DisposeAsync the lease before `runner.DisposeHerdrPaneAsync`; its existing Lease disposal is idempotent. | V-8 `C461_G049_Standing_execution_lock`: same immediate `standing-lock-held` assertion. |
| G-11 / PC-11: safe evidence preservation | Make the display-text policy return `[redacted]` for every non-null string, including safe and empty labels. | V-4 `Redaction_preserves_safe_display_and_nulls`: literal `grok.exe`/empty-label equality fails `safe-evidence-preserved`. |
| G-12 / PC-12: raw execution comparison | In runner Stamp, serialize projected executable names in Shell/Foreground/Affected instead of raw names; change both initial and recheck stamp construction consistently. | V-5 `Redaction_does_not_promote_unproven_process_identity`: same-leaf raw-change scenario closes instead of Refused/PaneChanged; `raw-stamp-changed`. |
| G-13 / PC-13: raw exact claim | In `HerdrDisposalIdentity.Refusal` exact-claim predicate replace `c.AgentKind == kind` with comparison of kind against a two-separator leaf of c.AgentKind. | V-5 `Redaction_does_not_promote_unproven_process_identity`: no-native `folder/grok` fixture becomes eligible; `raw-claim-kind-not-normalized`. |

Each PC has its own guard witness. PC-13 mutates the identity file only in the later mutation snapshot; it adds no Code footprint. If the implemented helper shape differs, translate the same compiling defect without changing its witness, invariant or selected method.

| PCs | Literal shell filter (no Markdown escape) | Selected results per phase |
|---|---|---:|
| PC-1 | `/*/*/HerdrPaneDisposalEndpointTests*/C461_G106_Preview_response_redaction*` | 1 |
| PC-2 | `/*/*/HerdrPaneDisposalRedactionTests*/Preview_redacts_path_labels*` | 13 |
| PC-3, PC-4, PC-5 (separate variants) | `/*/*/HerdrPaneDisposalRedactionTests*/Preview_projects_process_basenames_without_changing_identity*` | 4 each |
| PC-6 | `/*/*/HerdrPaneDisposalRedactionTests*/Preview_and_stored_review_share_redaction*` | 1 |
| PC-7, PC-12, PC-13 (separate variants) | `/*/*/HerdrPaneDisposalRedactionTests*/Redaction_does_not_promote_unproven_process_identity*` | 1 each |
| PC-8 | `/*/*/HerdrPaneDisposalRedactionTests*/Structured_logs_omit_display_paths_and_reason*` | 1 |
| PC-9, PC-10 (separate variants) | `/*/*/HerdrPaneDisposalApplicationTests*/C461_G049_Standing_execution_lock*` | 1 each |
| PC-11 | `/*/*/HerdrPaneDisposalRedactionTests*/Redaction_preserves_safe_display_and_nulls*` | 1 |

Linux suffices for the mutations, which contain both OS string styles. Use the external unchanged checkpoint driver/build-slot helpers under the documented SourceLanding custody; each phase gets a fresh result directory, actual method/argument outcomes, first assertion failure and restoration evidence. Refresh restored source timestamps before rebuilding. No tests run in TestDesign to manufacture mutation evidence.

### Test-first order, collision gate and out of scope

S1 authors the named redaction methods V-1/2/3/4/5/6, strengthens V-7, and repairs V-8 plus fake basename parsing independently of the new production helper. Commit/push this compiling slice, then CP-1 and CP-2 once as the declared **red-first** exception to final S1-S2 boundaries. V-1/2/3/7 must fail named path-exclusion assertions against current production. Safe/raw-safety/log and repaired-lock checks may already pass; that is expected, with their detecting variants reserved for Mutation. Do not run an extra unlisted baseline build just to rediscover the known cmd.exe fault. TUnit's intra-row method scheduling is not an execution-order contract; red-first means the S1 commit and rows precede S2.

S2 adds production projection after raw classification, caches only the projected preview, keeps the raw stamp, and documents the policy. Commit/push, then final CP-1/CP-2 on Linux and CP-3/CP-4 on Windows at the same source SHA. This is **four planned Linux row invocations** (two preparatory red, two final green) and two focused Windows invocations, with four distinct checkpoint rows total. Re-run only an affected row after actual changes/failure, with a fresh results root and recorded reason. No unfiltered full-suite acceptance.

The closed eight-path Code footprint remains the earlier Scope list: new runner redactor and redaction test file; runner preview service, existing disposal fixture, the two server test files, Herdr documentation and this plan. All HTTP/logger/test data helpers fit the new test file; G049 overrides remain local to its existing file. No shared harness, fake server/client/adapter, project, receipt store, identity classifier, locator or inspector production edits are authorized by this design.

**Collision refresh at 2026-10-01 10:30:46 UTC:** scoped `/api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703`, pipeline, runner catalogue/defaults and hosts all returned 200. CARD-0778 Code `8d5aa4fc` and CARD-0835 Code `9a1c17e8` were Dispatched; both task detail summaries had `scope:null`, so null is not evidence of unrestricted compatibility. CARD-0835 also had desktop Debug `61690d08`; CARD-0801 Code `3525d6b5` was Blocked. The latter's fake-server follow-up may change a read dependency and must be rechecked if resumed. CARD-0863 was now Code-ready from TestDesign; 0881/0883 were in Review on the card read. 0788/0505/0822/0826 remained unstarted ready/held work. No Code admission is performed in this stage, and this snapshot must be refreshed before dispatch.

- **0864:** serialize Code. Exact shared dependency `tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalFixture.cs` supplies both rosters; this card edits it. Our `src/Antiphon.SessionRunner/HerdrPaneDisposalService.cs` calls its locator implementation and consumes its inspector through the backend. Its identified edits/gates are in `src/Antiphon.SessionRunner/HerdrDisposalLocatorStore.cs`, `HerdrDisposalProcessInspector.cs`, and the four exact test files named in the 23-result census. Those are not this card's edit files; do not invent a finalized 0864 footprint. Shared `runner`/`herdr` areas require serialization even if 0864 leaves the fixture alone. If it lands first, re-read/recount before Code.
- **0863 / 0826:** no exact edit-file intersection with the supplied plans; both include `src/Antiphon.SessionRunner/**` changes and therefore collide in mapped `runner`. Serialize admitted Code. 0863 TestDesign/Plan can proceed independently; 0826 is not a functional prerequisite.
- **0788:** no exact named implementation overlap; its declared `tests/Antiphon.Tests/Application/**` scope intersects our `HerdrPaneDisposalEndpointTests.cs` and `HerdrPaneDisposalApplicationTests.cs`. Defer if that broad Code scope is active; no wait merely because the card is held.
- **0835:** checkpoint driver/tool, Review/land/EF work is disjoint from our eight edit files. It is **not an overlap/dependency**; use its updated driver when landed, retaining commit-before-run.
- **0778:** exact changed readiness/capture/adapter/registry/agent-test files and its two Grok application tests are disjoint; no mapped pty edit here. **0881 / 0883 / 0505 / 0822:** their settings, dispatch, land/source-recovery, schema and instruction footprints are disjoint. Recheck active actual scopes, particularly any newly broadened runner or application-test scope. Read-only use of registry/Start is not a reason to modify them.

Caller re-reads the current three settings routes plus hosts and scoped tasks, applies lower effective stage/host caps and serializes the above overlaps. Never change budgets, area mappings, or use a same-stage concurrency override to force this work through. Prefer server2; Windows is only CP-3/4.

Out of scope: CARD-0864 ungating/native Unix disposal; real process/pane/provider starts; retrospective receipt scrubbing; changing receipt schema, endpoint contracts, queue/recovery behavior or deployment. Static source cannot prove native OS behavior; focused Windows receipts remain required. No deterministic seam is missing for the frozen in-scope contracts.

Known flakes excluded from all filters: CARD-0791/0794 (provider readiness/snapshot/broken-pipe), CARD-0818 (`CheckpointExecutorLogTests.concurrent_callbacks_append_each_line_once_without_overlap`), CARD-0820 (temp contention/cleanup and `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget`), CARD-0828 (checkpoint ownership/executor races), CARD-0848 (`DetachedLauncherTests.executor_survives_its_starter`), CARD-0878 (HerdrAlwaysOnChannelParity missing-target case), CARD-0879 (AlwaysOn DetectTimeout/PaneClosed), `ScaledTimeProviderTests.Speed_10` (CARD-0757), `HttpResilienceRegistrationTests.Runner_list_and_git_connectivity_keep_their_short_deadlines` (CARD-0751). They cannot excuse a selected-row failure or another skip. No new real-time margin below two seconds; clocks are controlled where time is asserted.

**Repeat ceiling:** no routine repetition battery. Only an unresolved nondeterministic failure justifies extra new-test method runs, at most **three normal and two loaded repeats**; these are maxima, not a required tax. Record each reason, filter and count. Observe existing host load; do not generate synthetic load or repeat whole green rows without a reason.

### Checkpoints

Closed ordinary manifest; each row builds once into its own isolated output then runs exactly its literal filter through `dotnet run --project ... --no-build` (never `dotnet test`). Use direct `scripts/run-checkpoint.ps1` as this brief requires; it self-leases. Markdown `\|` becomes plain `|` in a quoted shell filter. Each class operand retains `*`. Opposite-OS rows are uncommissioned (zero selected), not passing skips. Expected final executed counts are exact, not just lower bounds; Code must recount implemented attributes and match fresh TRX names/arguments. New design count `13+4+1+1+1+1=21` is prospective, not a source/runtime result.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c866-linux-runner/` | linux-disposal-projection | `/*/*/(HerdrPaneDisposalRedactionTests*)\|(HerdrPaneDisposalIdentityTests*)/*` | V-1..V-6, R-1 | Linux 56 = 21 new + 35 existing, 0 failed/skipped; Windows 0 selected | 56 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c866-linux-server/` | linux-disposal-server | `/*/*/(HerdrPaneDisposalEndpointTests*)\|(HerdrPaneDisposalApplicationTests*)\|(HerdrPaneDisposalHttpWireTests*)/*` | V-7, V-8, R-2, R-3 | Linux 37 = 14 + 14 + 9, 0 failed/skipped; Windows 0 selected | 37 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c866-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c866-windows-runner/` | windows-disposal-projection | `/*/*/HerdrPaneDisposalRedactionTests*/*` | V-1..V-6, R-4 | Windows 21, 0 failed/skipped; Linux 0 selected | 21 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S2 | `tests/Antiphon.Tests -> bin-c866-windows-server/` | windows-disposal-regressions | `/*/*/(HerdrPaneDisposalEndpointTests*)\|(HerdrPaneDisposalApplicationTests*)/(C461_G106_Preview_response_redaction*)\|(C461_G049_Standing_execution_lock*)` | V-7, V-8, R-4 | Windows 2, 0 failed/skipped; Linux 0 selected | 2 | 7 | true | `C804_ORPHAN_SWEEP_ROOT=c866-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

## Execution, activation and rollback

This brief requires **`scripts/run-checkpoint.ps1` directly**: it self-leases, and the checkpoint tool's task ownership must not be assumed verified (CARD-0853). Do not wrap it in build-slot again. Commit/push before each run, including red rounds (CARD-0835). No checkpoints run in Plan. Any separate build/test command needs a stated reason and a host build slot; slot timeout exit 4 is reported, never bypassed.

The driver uses one isolated `dotnet build` then TUnit through `dotnet run --project ... --no-build`, never `dotnet test`. Use its OutputPath flag with forward slash, keep the Linux default UseAppHost=false and preserve a fresh results directory per red/green/rerun. Table pipes are Markdown-escaped as `\|`; **remove the Markdown backslash in actual quoted shell filters**. Class operands retain trailing `*`. Both class- and method-segment unions use parenthesized operands, never OR between full paths. Verify actual executed names in fresh TRX, especially CP-4's exact two-method intersection.

Future commands, from the committed Code checkout, sequentially (PowerShell syntax):

```powershell
$env:TUNIT_MAX_PARALLEL_TESTS = '1'
$env:C804_ORPHAN_SWEEP_ROOT = 'c866-disabled'
# Linux/server2; use a different ResultsRoot for the declared preparatory red round.
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.SessionRunner.Tests -OutputPath bin-c866-linux-runner/ -Filter '/*/*/(HerdrPaneDisposalRedactionTests*)|(HerdrPaneDisposalIdentityTests*)/*' -MinExecuted 56 -Expect 'HerdrPaneDisposalRedactionTests,HerdrPaneDisposalIdentityTests' -ResultsRoot .antiphon/c866-linux-green
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-2 -Project tests/Antiphon.Tests -OutputPath bin-c866-linux-server/ -Filter '/*/*/(HerdrPaneDisposalEndpointTests*)|(HerdrPaneDisposalApplicationTests*)|(HerdrPaneDisposalHttpWireTests*)/*' -MinExecuted 37 -Expect 'HerdrPaneDisposalEndpointTests,HerdrPaneDisposalApplicationTests,HerdrPaneDisposalHttpWireTests' -ResultsRoot .antiphon/c866-linux-green
# Windows only, same completed Code SHA.
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-3 -Project tests/Antiphon.SessionRunner.Tests -OutputPath bin-c866-windows-runner/ -Filter '/*/*/HerdrPaneDisposalRedactionTests*/*' -MinExecuted 21 -Expect HerdrPaneDisposalRedactionTests -ResultsRoot .antiphon/c866-windows-green
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-4 -Project tests/Antiphon.Tests -OutputPath bin-c866-windows-server/ -Filter '/*/*/(HerdrPaneDisposalEndpointTests*)|(HerdrPaneDisposalApplicationTests*)/(C461_G106_Preview_response_redaction*)|(C461_G049_Standing_execution_lock*)' -MinExecuted 2 -Expect 'C461_G106_Preview_response_redaction,C461_G049_Standing_execution_lock' -ResultsRoot .antiphon/c866-windows-green
```

Report `CHECKPOINT CP-n commit=<sha> build=<ok|failed> filter=<exact> executed=N passed=N failed=N skipped=N trx=<fresh-path> reruns=k`, OS, expected/actual roster and the first failure for every red/retry. A floor alone is insufficient. Final arithmetic is Linux `56 + 37 = 93`, Windows `21 + 2 = 23`, **116 OS executions**, with two existing formerly gated rows now required. Windows evidence remains pending if its host is unavailable; no claim of full acceptance from this inaccessible desktop mirror. No full Unit, Herdr parity, native disposal, provider or browser suite is commissioned.

Activation is a later separately authorized operation after ordinary Review and confirmed publication. No live pane must be closed to validate redaction. Deploy/restart the runner via the canonical runbook and owning checkout, confirm the actual running build identity, and check a preview only against an explicitly owned fixture if commissioned. Runner activation is what changes this production projection; a passing server health check or a land alone does not prove it. Existing in-memory previews are discarded by restart. Existing durable records are retained unchanged: no retroactive cleanup/scrub is authorized by this card. Report that limitation if old receipts remain readable.

Rollback is a new forward commit reverting the projection/fixture changes as appropriate, never reset/rebase/force-push. Restoring old behavior reopens the known label exposure; restore the two CARD-0866 gates if the old Linux failures return and keep the card/evidence visible. Use normal ownership-aware restart/deploy procedures for any separately commissioned activation rollback. No schema, settings, provider state or operator locator files require migration or deletion.

### Cost

Estimates, not measured runtime: final ordinary V/R floor is **30 minutes** (CP-1/2/3/4 = 8+8+7+7), including four isolated builds. Linux is 16 minutes and the focused Windows lane 14 minutes. The declared Linux test-first red rounds add 16 minutes, for **46 minutes** of planned Code verification before justified repairs or slot waits. Implementation estimate remains 60-90 minutes, so Code budgeting is 106-136 minutes, excluding external Windows scheduling and slot waits.

The thirteen later Mutation variants use the exact method filters and result floors in the PC filter table: 39 phase invocations (13 baseline/red/restored-green triples). Estimate 3 minutes per isolated build/phase plus 10 for custody/restoration/reporting: **127 minutes**, separate from ordinary Code. Original ten controls contribute 90 phase-minutes; PC-11..13 add 27 for independently uncovered preservation/raw-stamp/raw-claim faults. Total final ordinary + Mutation floor is **157 minutes**; including preparatory reds it is **173 minutes**, plus authoring (233-263 minutes across separate Code and Mutation dispatches, not one task). No routine repeat cost is hidden in these floors. Extra justified repeats stay within three normal/two loaded and are reported separately. Savings from avoiding full suites are not measured; none is claimed.

### TestDesign validation

Static census and manifest validation only: check exact five-source roster and OS gates, four eleven-column checkpoint rows, distinct outputs, escaped pipes, trailing class wildcards, Min 56/37/21/2, final Linux/Windows totals 93/23 (116), 30-minute final V/R floor, six new method targets expanding 13+4+1+1+1+1=21, thirteen one-to-one guard/control entries, and thirteen mutation variants/39 phase invocations. These are source/design counts, not TRX or executed mutation evidence. Code must recount after any prerequisite lands and after implementing attributes. TestDesign executes zero builds/tests on Linux and Windows; Windows receipts remain a future required row, not an inferred pass.

TestDesign changes only this plan, including the explicitly listed corrections to D-3, helper usage, counts/commands and Cost. No operator decision is needed for the conservative path-label masking contract. The caller must clear the collision gate and commission Code with this frozen Verification design, then separate ordinary Review before land and SourceLanding Mutation afterward.
