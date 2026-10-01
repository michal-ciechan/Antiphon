# CARD-0866: disposal preview redaction and standing-lock evidence

Date: 2026-10-01. Stage: Plan complete; TestDesign follows. Baseline: `origin/master` at `fccef27eb03f2522f2a40574dc1c764335d4a490`; assigned branch `feat/card-task-43e89e4e` starts there. Read-only `git ls-remote` confirmed both remote refs at that SHA. This artifact is the only repository change; no implementation, tests, builds, provider launches or live host changes are performed in Plan.

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

The exact test is `HerdrPaneDisposalApplicationTests.C461_G049_Standing_execution_lock` at lines 64-84, gated at 66-67. Reaching `BeforeClose` at line 78 already proves disposal passed its inventory/refusal checks. Its inspector is the same complete fake, so the production Linux empty census cannot cause this failure.

`StandingRecoveryFixture.cs:26` builds the standing harness with its default registry. `AgentControlServiceIntegrationTests.cs:1762` specifies `Path.Combine(Environment.SystemDirectory, "cmd.exe")`. `StandingRecoveryFixture.StartAsync` (`:47`) returns the real asynchronous Start operation. `AgentControlService.cs:476` calls `EnsureSpawnable` before `:519-523` obtains the ordered singleton queue locks. `AgentExecutableResolver.cs:94-98` throws when `cmd.exe` cannot resolve. The read-only host measurement above confirms that condition. The existing `Task.Delay(100); start.IsCompleted.ShouldBeFalse()` hides the useful fault behind an incidental assertion. A fresh test run must capture/await an early fault to verify its exact signature; this Plan did not execute it.

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

Use `StandingRecoveryFixture`'s existing `configureServices` hook to override only this test's fake `AgentRegistrySettings`: Windows uses existing system `cmd.exe`, Unix uses the measured absolute `/bin/sh`. Preserve definition `fake` and kind `ClaudeCode`; supply a `FakeAgentProtocolAdapter` rather than the current empty adapter queue. No executable is launched by this configuration: the fake adapter factory owns dispatch. Do not edit the shared harness or weaken `EnsureSpawnable`.

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

No change to project files is needed for C# wildcard inclusion. Keep helper code for the new runner tests in its new test file. Reuse the existing logger helper, fake transport, standing fixture injection hook and fake runner ListOverride; their files are read-only dependencies. The server disposal/ownership classes, `AgentControlService`, shared standing harness, inspector, locator and receipt-store implementations are inspected but not edited.

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

### Inspection and recounted source roster

The following was recounted from baseline source `[Test]` and `[Arguments]`, with a separate scan for dynamic data sources, matrix attributes and OS/skip branches. No additional data-source expansion was found in these selected classes. Direct calls from one test to another do not add separately reported results.

| Existing class | Source methods | Expanded results | Current Linux / Windows skipped results | Final Linux / Windows selection |
|---|---:|---:|---:|---|
| `HerdrPaneDisposalEndpointTests` | 8 | 14 = 4 + 3 + 2 + 1 + 1 + 1 + 1 + 1 | 1 / 0 (G106 only) | Linux all 14; Windows G106 only, 1 |
| `HerdrPaneDisposalApplicationTests` | 13 | 14 = 12 single cases + 2 argument cases | 1 / 0 (G049 only) | Linux all 14; Windows G049 only, 1 |
| `HerdrPaneDisposalHttpWireTests` | 7 | 9 = 3 in HttpWire file + 6 in ExecutionWire file | 0 / 0 | Linux all 9; Windows not selected |
| `HerdrPaneDisposalIdentityTests` | 22 | 35 | 0 / 0 | Linux all 35; Windows not selected |

`HerdrPaneDisposalExecutionWireTests.cs` is a **partial of HerdrPaneDisposalHttpWireTests**, not a separate class. The 35 identity results expand from `3,1,1,2,3,2,2,2,2,2,1,1,1,3,1,1,1,1,2,1,1,1` in source order. Thus CP-2's existing selection is 37 results, currently 35 non-skipped plus two Linux skips; this is source arithmetic, not a runtime pass claim. The brief's historical 51-result CP-11 is a wider selection and is not copied as our denominator. Exactly two skip statements are removed; no CARD-0864 skip is part of this acceptance or counted as repaired.

New `HerdrPaneDisposalRedactionTests` is an Integration fixture class with **six proposed methods / fifteen proposed argument-expanded results**. These are design targets enumerated below, not counts of tests already in source. TestDesign must freeze them, and Code must recount implemented attributes plus fresh TRX before claiming completion. Loops over fields, outcomes and JSON surfaces do not multiply execution counts.

### Proves it works now

| ID | Proposed or existing named test | Cases and named assertions |
|---|---|---|
| V-1 | New `HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels` | Seven `[Arguments]`: `C:\secret-home\repo`, `\\host\secret-home\repo`, `/secret-home/repo`, `folder\secret-home`, `folder/secret-home`, `C:secret-home`, `C:/secret-home\repo`. Embed each inside `work "<path with optional spaces>" selected` to exercise whole-value redaction. Set all three wire labels via fake workspace/tab/pane objects; separately taint claim display fields and BackendVersion through Transform. Assert each affected field equals `[redacted]` and serialized preview `ShouldNotContain("secret-home")` (`path-label-excluded`), including ineligible/incomplete inventory. The output must retain IDs, refusal code and nullability. |
| V-2 | New `HerdrPaneDisposalRedactionTests.Preview_projects_process_basenames_without_changing_identity` | Four `[Arguments]`: `windows`, `unix`, `unc`, `mixed`. After fake inspection, inject path-bearing Shell/Foreground/Affected process names through Transform, with distinct synthetic shell/foreground/background directory sentinels. Assert every exposed executable is the expected leaf and each sentinel is absent (`shell-path-excluded`, `foreground-path-excluded`, `affected-path-excluded`). Assert PIDs, parent PIDs, timestamps and native UUID arrays are unchanged; a rejected inventory stays rejected. Distinct sentinels make independently omitted projections detectable. |
| V-3 | New `HerdrPaneDisposalRedactionTests.Preview_and_stored_review_share_redaction` | One result, with internal Closed/Refused/Unknown fixture scenarios. Use tainted labels and valid identity; no captured locator files. Assert preview POST-equivalent service result and `GetPreview` exclusion (`stored-preview-path-excluded`), exact expected outcome/close count, serialized receipt/status exclusion, and actual on-disk JSON's `reviewed` exclusion (`durable-review-path-excluded`). Recreate the service for status; Unknown must remain read-only with the fake process Alive unknown. Include a synthetic reason canary and verify it is not persisted; fingerprint equality/idempotence remain intact. |
| V-4 | New `HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls` | One result. Preserve plain labels (including Unicode), `pwsh.exe`/`grok.exe`, null optional fields, known claim metadata and safe backend version; assert an ordinary eligible preview stays eligible with identical planned PIDs (`safe-evidence-preserved`). Also assert control-character/empty-leaf cases redact safely in internal subcases. Do not make safe-output self-comparisons to the new helper. |
| V-5 | New `HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity` | One result. Inject a raw path as Shell.ExecutableName and a valid matching agent/claim. Baseline `IsShell` rejects the raw value. The returned name may be safe, but `Eligible.ShouldBeFalse()` and the exact IdentityUnproven blocker must remain (`raw-identity-still-refused`); execution must dispatch zero closes. This detects projecting the raw observation before classification. |
| V-6 | New `HerdrPaneDisposalRedactionTests.Structured_logs_omit_display_paths_and_reason` | One result. Capture the production logger using the existing ListLogger. Seed label/process/cwd/argv/reason sentinels; execute an eligible tainted-label fixture and an inspection/close error with a sentinel exception message. Assert fixed outcome/code, operation ID logged, and all synthetic path/reason/argv canaries absent (`disposal-log-payload-excluded`). The exception must complete into a typed result; timeout is not evidence. |
| V-7 | Existing `HerdrPaneDisposalEndpointTests.C461_G106_Preview_response_redaction` | One result on each OS. Remove its Linux skip; retain original executable/cwd/argv canaries, add path-bearing wire labels, assert HTTP 200, decoded labels redacted, `json.ShouldNotContain("secret-home")` and `ShouldNotContain("secret-canary")`, safe `grok.exe`, complete and eligible fixture evidence. Assert exclusions before later eligibility assertions so the bypass mutation is attributed to the named exclusion. |
| V-8 | Existing `HerdrPaneDisposalApplicationTests.C461_G049_Standing_execution_lock` | One result on each OS. D-3 fixture override, actual queue gate probe (`standing-lock-held`), preflight signal or an explicitly surfaced early fault, no premature launch, successful release/completion, one close and reacquirable gate (`standing-lock-released`). Assert fake adapter Started and the expected session ID after draining, so an empty fake queue cannot masquerade as successful execution. |

For new previews set `h.Clock.Now` to an explicit fixed UTC instant; their two-minute expiry and receipt times therefore need no sleeps. Existing identity/guard tests retain their existing deterministic process timestamps. Use random loopback sockets and the standard isolated test database via StandingRecoveryFixture. No native process is needed in this roster; keep the existing assembly-local ProcessSpawnLimit if future fixture changes add process I/O, and do not introduce unbounded background work. No test targets runner 17204, boots real server Program, launches a provider, or cleans operator files.

### Guards the regression

- **R-1:** all 35 existing identity results keep exact UUID/process-incarnation, live/conflicting claim, shell/family, complete inventory, descendant and pending input/launch refusals. The redactor cannot become an authorization normalizer.
- **R-2:** all 14 application results retain persisted Stop, no active/queued launch, no implicit Stop, unchanged history/owner rows, no transaction over RPC and explicit-only disposal.
- **R-3:** all 14 endpoint and 9 wire results retain target/reason validation, capability/guard-mode gates, HTTP/status/error receipt shapes, dropped-response reconciliation and exact `pane.close` payload. Existing G116's delegate call does not seed a canary; V-3/V-6 supply actual adversarial evidence instead of crediting that absence assertion as detection.
- **R-4:** Windows reruns the fifteen new display-projection results and exactly G106/G049. This specifically checks the host-separator and executable-preflight assumptions that differed in historical W-1, without paying for unrelated Windows provider/native suites.

### Positive controls

After ordinary Review and confirmed land, commission a separate SourceLanding Mutation task using the documented custody workflow. Every PC below has an exact-method baseline, intended assertion red and restored green; each phase uses the unchanged external copy of `scripts/run-checkpoint.ps1` and its build-slot library. Build errors, harness timeouts, missing prerequisites and zero tests are not reds. Preserve the first failure and restore timestamps before rebuilding. These are ten independent variants, run separately where they share a file/method; none is executed by this Plan or implicitly required of ordinary Code.

| PC | Concrete mutation | Named detecting test and required red |
|---|---|---|
| PC-1 | Bypass the production redactor call entirely at preview construction. | V-7 `C461_G106_Preview_response_redaction`: `json.ShouldNotContain("secret-home")` fails on the seeded labels on either OS, even though the corrected fake returns a basename. |
| PC-2 | Remove backslash and drive-prefix recognition from display-text redaction, retaining `/` recognition. | V-1 `Preview_redacts_path_labels`: Windows/UNC/drive-relative cases fail `path-label-excluded`; Unix remains a positive comparison. Seven method-selected results. |
| PC-3 | Omit projection only for Shell.ExecutableName. | V-2 `Preview_projects_process_basenames_without_changing_identity`: `shell-path-excluded` fails; four results. |
| PC-4 | Omit projection only for Foreground. | V-2: `foreground-path-excluded` fails; four results. |
| PC-5 | Omit projection only for AffectedProcesses. | V-2: `affected-path-excluded` fails; four results. |
| PC-6 | Cache the raw constructed preview in Review and redact only the return value. | V-3 `Preview_and_stored_review_share_redaction`: `stored-preview-path-excluded` and/or `durable-review-path-excluded` fails while initial preview output remains sanitized; one result. |
| PC-7 | Normalize Shell/Foreground/Affected executable names in the observation before `_identity.Refusal` instead of only in the output DTO. | V-5 `Redaction_does_not_promote_unproven_process_identity`: `Eligible.ShouldBeFalse()` fails (`raw-identity-still-refused`); one result. |
| PC-8 | Add logging of `request.Reason` in ExecuteAsync. | V-6 `Structured_logs_omit_display_paths_and_reason`: synthetic reason `ShouldNotContain` fails (`disposal-log-payload-excluded`); one result. |
| PC-9 | Omit `ownership.AcquireAsync` in server ExecuteAsync for a new operation. | V-8 `C461_G049_Standing_execution_lock`: nonblocking queue probe returns true and fails `standing-lock-held`; one result, no timing race needed. |
| PC-10 | Acquire the ownership lease but dispose it immediately before runner disposal I/O. | V-8: the same `standing-lock-held` assertion fails while the backend is paused; one result. This distinguishes holding through RPC from acquisition alone. |

Use `/*/*/<Class>*/<Method>*`, with the complete method names above. V-2's short references in PC-4/5 mean exactly the full method in PC-3, not a class-wide run. Linux is sufficient for the ten mutants because all inputs include both separator styles; ordinary Windows receipts remain independently required. A mutation that fails only a teardown wait or yields a different fault must be investigated, not credited.

### Test-first order and known flakes

1. Freeze TestDesign's roster and assert the two gate locations. Add S1 fixtures/tests and commit/push. Run CP-1/CP-2 once as the explicitly declared test-first red round before S2; this is the sole planned preparatory exception to the table's final `After` boundary. Require path exclusion reds in V-1/V-2/V-3/V-7 against compiling production code. Preserve the G049 historical signature separately if reproduced; its fixture repair should make the direct ownership evidence green.
2. Implement S2, commit/push, execute final Linux CP-1/CP-2. Fix actual failures and rerun only the affected exact row, reporting each rerun.
3. At the completed source SHA execute Windows CP-3/CP-4. Do not infer Windows success from portable source or Linux fake evidence.
4. No routine repetition battery. If a new nondeterministic failure justifies stress, cap additional new-test repetitions at **three normal and two loaded** rounds, using the exact method filters and recording each reason and count. These are maxima, not a required tax. Do not create synthetic host load, broaden suites or repeat already-green rows without an unresolved concern.

Known flakes considered and excluded from this closed selection: CARD-0791 (Codex snapshot/Claude effort readiness timing); CARD-0794 (Codex readiness and multiline broken-pipe/hang); CARD-0818 (`CheckpointExecutorLogTests.concurrent_callbacks_append_each_line_once_without_overlap`); CARD-0820 (checkpoint temp contention/cleanup plus `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget`); CARD-0828 (checkpoint owner/executor lifetime races); CARD-0848 (`DetachedLauncherTests.executor_survives_its_starter`); CARD-0878 (`HerdrAlwaysOnChannelParityTests.Standing_native_wire_missing_target_never_creates(ClaudeCode, PtyHost, False)`); CARD-0879 (named AlwaysOn Herdr DetectTimeout/PaneClosed race). Also excluded: `ScaledTimeProviderTests.Speed_10` (CARD-0757) and `HttpResilienceRegistrationTests.Runner_list_and_git_connectivity_keep_their_short_deadlines` (CARD-0751). This list comes from repository planning evidence, not fresh reproduction here. None excuses a failure in the selected rows or permits another skip. Keep first-failure signatures and source SHA if inherited failures appear.

### Checkpoints

Closed ordinary list; one isolated build and one exact TUnit filter per row. Linux/server2 runs CP-1/CP-2; Windows runs only CP-3/CP-4. Opposite-OS rows are not commissioned and count as zero selected, not passing skips. Every selected final row requires zero failed and zero skipped results. Proposed new counts are the explicit V-1..V-6 roster above; implemented source/TRX must match before final acceptance. The `Environment` setting suppresses the unrelated inherited orphan sweep and serializes the test host; it is not a new product setting.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c866-linux-runner/` | linux-disposal-projection | `/*/*/(HerdrPaneDisposalRedactionTests*)\|(HerdrPaneDisposalIdentityTests*)/*` | V-1..V-6, R-1 | Linux 50 executed = 15 new + 35 existing, 0 failed/skipped; Windows 0 selected | 50 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c866-linux-server/` | linux-disposal-server | `/*/*/(HerdrPaneDisposalEndpointTests*)\|(HerdrPaneDisposalApplicationTests*)\|(HerdrPaneDisposalHttpWireTests*)/*` | V-7, V-8, R-2, R-3 | Linux 37 executed = 14 + 14 + 9, 0 failed/skipped; Windows 0 selected | 37 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c866-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c866-windows-runner/` | windows-disposal-projection | `/*/*/HerdrPaneDisposalRedactionTests*/*` | V-1..V-6, R-4 | Windows 15 executed, 0 failed/skipped; Linux 0 selected | 15 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S2 | `tests/Antiphon.Tests -> bin-c866-windows-server/` | windows-disposal-regressions | `/*/*/(HerdrPaneDisposalEndpointTests*)\|(HerdrPaneDisposalApplicationTests*)/(C461_G106_Preview_response_redaction*)\|(C461_G049_Standing_execution_lock*)` | V-7, V-8, R-4 | Windows 2 executed, 0 failed/skipped; Linux 0 selected | 2 | 7 | true | `C804_ORPHAN_SWEEP_ROOT=c866-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

## Execution, activation and rollback

This brief requires **`scripts/run-checkpoint.ps1` directly**: it self-leases, and the checkpoint tool's task ownership must not be assumed verified (CARD-0853). Do not wrap it in build-slot again. Commit/push before each run, including red rounds (CARD-0835). No checkpoints run in Plan. Any separate build/test command needs a stated reason and a host build slot; slot timeout exit 4 is reported, never bypassed.

The driver uses one isolated `dotnet build` then TUnit through `dotnet run --project ... --no-build`, never `dotnet test`. Use its OutputPath flag with forward slash, keep the Linux default UseAppHost=false and preserve a fresh results directory per red/green/rerun. Table pipes are Markdown-escaped as `\|`; **remove the Markdown backslash in actual quoted shell filters**. Class operands retain trailing `*`. Both class- and method-segment unions use parenthesized operands, never OR between full paths. Verify actual executed names in fresh TRX, especially CP-4's exact two-method intersection.

Future commands, from the committed Code checkout, sequentially (PowerShell syntax):

```powershell
$env:TUNIT_MAX_PARALLEL_TESTS = '1'
$env:C804_ORPHAN_SWEEP_ROOT = 'c866-disabled'
# Linux/server2; use a different ResultsRoot for the declared preparatory red round.
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.SessionRunner.Tests -OutputPath bin-c866-linux-runner/ -Filter '/*/*/(HerdrPaneDisposalRedactionTests*)|(HerdrPaneDisposalIdentityTests*)/*' -MinExecuted 50 -Expect 'HerdrPaneDisposalRedactionTests,HerdrPaneDisposalIdentityTests' -ResultsRoot .antiphon/c866-linux-green
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-2 -Project tests/Antiphon.Tests -OutputPath bin-c866-linux-server/ -Filter '/*/*/(HerdrPaneDisposalEndpointTests*)|(HerdrPaneDisposalApplicationTests*)|(HerdrPaneDisposalHttpWireTests*)/*' -MinExecuted 37 -Expect 'HerdrPaneDisposalEndpointTests,HerdrPaneDisposalApplicationTests,HerdrPaneDisposalHttpWireTests' -ResultsRoot .antiphon/c866-linux-green
# Windows only, same completed Code SHA.
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-3 -Project tests/Antiphon.SessionRunner.Tests -OutputPath bin-c866-windows-runner/ -Filter '/*/*/HerdrPaneDisposalRedactionTests*/*' -MinExecuted 15 -Expect HerdrPaneDisposalRedactionTests -ResultsRoot .antiphon/c866-windows-green
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-4 -Project tests/Antiphon.Tests -OutputPath bin-c866-windows-server/ -Filter '/*/*/(HerdrPaneDisposalEndpointTests*)|(HerdrPaneDisposalApplicationTests*)/(C461_G106_Preview_response_redaction*)|(C461_G049_Standing_execution_lock*)' -MinExecuted 2 -Expect 'C461_G106_Preview_response_redaction,C461_G049_Standing_execution_lock' -ResultsRoot .antiphon/c866-windows-green
```

Report `CHECKPOINT CP-n commit=<sha> build=<ok|failed> filter=<exact> executed=N passed=N failed=N skipped=N trx=<fresh-path> reruns=k`, OS, expected/actual roster and the first failure for every red/retry. A floor alone is insufficient. Final arithmetic is Linux `50 + 37 = 87`, Windows `15 + 2 = 17`, **104 OS executions**, with two existing formerly gated rows now required. Windows evidence remains pending if its host is unavailable; no claim of full acceptance from this inaccessible desktop mirror. No full Unit, Herdr parity, native disposal, provider or browser suite is commissioned.

Activation is a later separately authorized operation after ordinary Review and confirmed publication. No live pane must be closed to validate redaction. Deploy/restart the runner via the canonical runbook and owning checkout, confirm the actual running build identity, and check a preview only against an explicitly owned fixture if commissioned. Runner activation is what changes this production projection; a passing server health check or a land alone does not prove it. Existing in-memory previews are discarded by restart. Existing durable records are retained unchanged: no retroactive cleanup/scrub is authorized by this card. Report that limitation if old receipts remain readable.

Rollback is a new forward commit reverting the projection/fixture changes as appropriate, never reset/rebase/force-push. Restoring old behavior reopens the known label exposure; restore the two CARD-0866 gates if the old Linux failures return and keep the card/evidence visible. Use normal ownership-aware restart/deploy procedures for any separately commissioned activation rollback. No schema, settings, provider state or operator locator files require migration or deletion.

### Cost

Estimates, not measured runtime: final ordinary checkpoint floor is **26 minutes** (`6 + 8 + 5 + 7`), including four isolated builds. Linux is 14 minutes and the focused Windows lane 12 minutes. The declared Linux test-first red rounds add 14 minutes, for a planned 40-minute verification budget before justified repairs or slot waits. Implementation estimate is 60-90 minutes after TestDesign, so Code budgeting is 100-130 minutes excluding waits and external Windows scheduling.

The ten later Mutation variants have thirty method-scoped phase invocations (baseline/red/restored green); budget 3 minutes per isolated phase plus 10 minutes for custody/restoration/reporting, **100 minutes** separately. No unmeasured repeat battery, full-suite sweep or native provider exercise is hidden in these estimates. Optional justified repeats remain capped at three normal/two loaded for new tests, and their actual cost must be reported separately.

TestDesign handoff: freeze the proposed assertion names/data rows, verify the existing fixture injection and logger hooks, recount source after dependencies land, and retain the scoped Windows receipts and runner/herdr collision gates. No operator decision is needed for the selected conservative whole-label masking policy; ordinary review can assess the explicit UI tradeoff that labels containing slashes are masked in disposal evidence.

### Plan validation

`git diff --check` passed. A read-only Node check verified the checkpoint table's eleven-column shape and required header order, four distinct isolated outputs, Min values 50/37/15/2, per-OS totals 87/17, total 104 and 26-minute estimate sum; it also counted ten prescribed mutation rows. Existing test census was taken directly from the five named source files, including both partial wire files; the proposed six-method roster expands as 7 + 4 + 1 + 1 + 1 + 1 = 15. This is static validation, not execution of the .NET checkpoint importer, builds, tests or mutations. Actual Plan execution totals remain Linux 0 / Windows 0. Only this Markdown file is changed and published on the assigned fast-forward branch.
