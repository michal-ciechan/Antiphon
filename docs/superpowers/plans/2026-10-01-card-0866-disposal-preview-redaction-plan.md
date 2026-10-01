# CARD-0866: disposal preview redaction and standing-lock evidence

Date: 2026-10-01. Stage: Plan in progress. Baseline: `origin/master` at `fccef27eb03f2522f2a40574dc1c764335d4a490`; assigned branch `feat/card-task-43e89e4e` starts there. Read-only `git ls-remote` confirmed both remote refs at that SHA. This artifact is the only repository change; no implementation, tests, builds, provider launches or live host changes are performed in Plan.

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

The decisive held-lock assertion is the nonblocking semaphore probe, independent of scheduler progress. TCS barriers use `RunContinuationsAsynchronously`; bounded waits exist only as deadlock diagnostics, never expected mutation reds. No real-time margin or fake-clock advance can prove a semaphore is held. The fixture's `Clock.Now` is pinned for new preview/receipt tests; no expiry test sleeps. Rejected: extending the 100 ms delay, swallowing Start exceptions, removing the blocked assertion, moving production locks or adding a new lock seam when the singleton queue and runner hook already exist.

### D-4: separate portability ownership and minimize collisions

Keep CARD-0864's native inspector/locator APIs and existing Linux gates untouched. Our new fixtures have no captured locator files, so receipt persistence/cleanup is portable without calling `CreateFileW`. Do not claim a successful fixture execution qualifies real Unix disposal. Prefer server2 for all portable rows; commission Windows only for the focused cross-OS redaction and the two formerly gated tests.

Rejected: coupling this repair to an unchosen Unix deletion algorithm or broadly ungating CARD-0864; modifying shared fake transport, standing harness, launch code or checkpoint tooling to solve local test setup.
