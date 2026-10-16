# CARD-1313: change-aware test selection — evidence map

**Confirmed from pinned master source and stored qualification reports:** test selection is currently profile/suite/class based, without a baseline-to-candidate path selector. Provider implementations are spread across shared projects, and resource requirements are not represented by a complete set of TUnit categories. This is enough to commission Plan; it is not evidence that a particular reduced roster is safe or faster.

## Provenance and investigation boundary

- Date: 2026-10-16 UTC. Task: 70491a04. Card: Antiphon CARD-1313, id `99e86094-bbdb-4329-a13f-06eed945951e`, concurrency token observed `a484eabc-f52d-4c8f-878a-244a319d1fa7`, Backlog, revisions 0.
- Card title: “Change-aware test selection: path regexes decide which heavy suites (real TUI, browser, Docker) run [master-only]”. Read with `pwsh -NoProfile -File scripts/card.ps1 get CARD-1313 -Board Antiphon`; exit 0. The stored description requests a narrow selection, “no whole Unit lane”, two SHAs, reasons, unknown-path full selection, and CARD-1281 integration.
- Inspected source: `origin/master = 2d2b30364d1793aedd9dae725a68246e127e7dd0`, fetched 2026-10-16; subject “Merge rc/2026-10-15 into master (RC fixes, release line)”. Every source line below refers to this SHA, **not** the older task base `6e23e0b4c8d6b2ae12218c2c0058593dbf773530`.
- Master was exported using `git archive 2d2b30364d1793aedd9dae725a68246e127e7dd0` into a task-owned temporary directory for read-only inspection. The assigned branch `feat/card-task-70491a04` was not switched, rebased or merged. Only this report is a deliverable.
- **No builds, test runs, discovery runs, live providers, browsers, containers or database queries were executed.** The only application read was the card command. Counts below are file/source inventories or explicitly attributed historical results. No new pass, performance saving, runtime coverage or deployed state is claimed.
- The Investigate stage forbids fix design. The requested regexes below are **source-location queries and observed dependency edges**, not an approved exclusion policy; script design, always-run-core policy and implementation slices remain Plan work. Existing hook points and constraints are recorded so Plan does not need to rediscover them.

## 1. Confirmed mechanism and limits

1. `scripts/lib/nightly-tests-impl.ps1:150` accepts suites, one source SHA/ref, profile and expected SHA. It has no baseline SHA or changed-path input. The selected profile supplies required suites at `scripts/lib/nightly-tests-impl.ps1:218`; omitted required suites set `coverageComplete=false` at `scripts/lib/nightly-tests-impl.ps1:268`.
2. Class lists already reach `--treenode-filter` through `scripts/lib/nightly-tests-impl.ps1:120`. The filter appends `*` to each class operand (line 143), which can select suffix classes. Required expanded UIDs are reconciled against the full non-excluded suite discovery union at `scripts/lib/nightly-tests-impl.ps1:581`. Merely supplying fewer classes cannot legitimately turn an existing full RC into complete-green.
3. Nightly excludes `OptIn`/`Explicit`; RC uses explicit policy class/method dispositions instead: `scripts/lib/nightly-coverage.ps1:539`, `scripts/lib/nightly-coverage.ps1:559`. RC's required/excluded UID census and digest are produced at line 593. Most live provider classes are already excluded in `tests/test-execution-policy.json`; they are not the bulk of the automated RC obligation.
4. The provider split is by **files and classes**, not provider projects/folders. The shared reference graph is documented in sections 2–3. Native PTY, real vendor executable, paid/live model API, browser, database and Docker are separate dimensions. A fake process still exercises real PTY/native custody. A real vendor CLI pointed at FakeLlmApi is still a real TUI.
5. Metadata is not a resource taxonomy. `tests/Shared/TestClassificationMetadata.cs:17` defines Unit/Integration/Slow/OptIn, and lines 176–184 enforce Unit XOR Integration only for `Antiphon.Tests`. Slow is a cost registry, not exclusion. There is no equivalent complete Docker/Postgres/provider/Windows category census here. Body gates and shared fixtures are necessary evidence.

Remaining uncertainty: safe omission boundaries, current compiled expanded UID membership, per-class timing on this master SHA, and actual deployed release/train behavior are not established by a source read. Plan must not turn this static inventory into a green qualification receipt.

## 2. Every test project and test-support executable

The tracked tree has **45 .csproj files**, **16 under tests/**, and **11 with literal IsTestProject=true** across the repository. Seven are primary test assemblies, three are checkpoint child test hosts, and one is the nightly diagnostic fixture. All target net9.0. Client and shell tests are additional lanes.

References below are the literal project references, including build-only references; linked source and MSBuild staging are additional edges described afterwards. Test project paths are the project identity, not a statement that every method needs every resource.

| Project and source | Role/resources | Direct project dependencies |
| --- | --- | --- |
| `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj:4` | Shared terminal/parser/transport tests; offline recordings, fake processes, Node peers, native PTY and real Claude/Codex/Grok canaries. | `tests/Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj`<br>`src/Antiphon.Agents.Pty/Antiphon.Agents.Pty.csproj`<br>`tests/Antiphon.Card0490.NativeHarness/Antiphon.Card0490.NativeHarness.csproj`<br>`src/Antiphon.SessionRunner.Contracts/Antiphon.SessionRunner.Contracts.csproj`<br>`src/Antiphon.FakeClaude/Antiphon.FakeClaude.csproj`<br>`src/Antiphon.FakeGrok/Antiphon.FakeGrok.csproj`<br>`src/Antiphon.FakeLlmApi/Antiphon.FakeLlmApi.csproj` |
| `tests/Antiphon.Card0490.NativeHarness/Antiphon.Card0490.NativeHarness.csproj:4` | Support library for native/QEMU controls; IsTestProject not set. | None |
| `tests/Antiphon.ChannelOutbound.Probe/Antiphon.ChannelOutbound.Probe.csproj:4` | Server-referencing owned child probe; IsTestProject not set. | `server/Antiphon.Server.csproj` |
| `tests/Antiphon.Checkpoints.FilterHost/Antiphon.Checkpoints.FilterHost.csproj:4` | Child TUnit host for filter behavior; not an independent product suite. | None |
| `tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj:3` | Child TUnit lifecycle/custody host; links main test scope/sweep sources. | `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj`<br>`src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj` |
| `tests/Antiphon.Checkpoints.RepeatHost/Antiphon.Checkpoints.RepeatHost.csproj:3` | Child TUnit repeat/evidence host. | None |
| `tests/Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj:4` | Owned child-process fixture; IsTestProject not set. | None |
| `tests/Antiphon.DockerStack.Fixture/Antiphon.DockerStack.Fixture.csproj:5` | WebApplicationFactory server fixture executable; its name alone does not imply a Docker launch. | `server/Antiphon.Server.csproj` |
| `tests/Antiphon.E2E/Antiphon.E2E.csproj:4` | Real app host + disposable Postgres + isolated runner; 10 Playwright browser classes, API/CLI/delivery classes and gated real-provider cases. | `server/Antiphon.Server.csproj`<br>`src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj`<br>`src/Antiphon.Agents.Pty/Antiphon.Agents.Pty.csproj`<br>`src/Antiphon.FakeGrok/Antiphon.FakeGrok.csproj`<br>`src/Antiphon.Messaging.Gateway/Antiphon.Messaging.Gateway.csproj`<br>`src/Antiphon.Messaging.Slack/Antiphon.Messaging.Slack.csproj`<br>`tools/Antiphon.MarkdownPdf/Antiphon.MarkdownPdf.csproj` |
| `tests/Antiphon.MarkdownPdf.FakeBrowser/Antiphon.MarkdownPdf.FakeBrowser.csproj:4` | Fake browser child-process executable, not real Chromium. | None |
| `tests/Antiphon.MarkdownPdf.Tests/Antiphon.MarkdownPdf.Tests.csproj:3` | Renderer/CLI tests and MarkdownPdfRealBrowserTests (Linux Docker Chromium). Separate primary test assembly omitted by current release policy. | `tools/Antiphon.MarkdownPdf/Antiphon.MarkdownPdf.csproj`<br>`tests/Antiphon.MarkdownPdf.FakeBrowser/Antiphon.MarkdownPdf.FakeBrowser.csproj` |
| `tests/Antiphon.Messaging.Tests/Antiphon.Messaging.Tests.csproj:4` | Messaging/core/Telegram/Slack/client/gateway/service; fake APIs, disposable Redpanda and PostgreSQL cases. | `src/Antiphon.Messaging/Antiphon.Messaging.csproj`<br>`src/Antiphon.Messaging.Telegram/Antiphon.Messaging.Telegram.csproj`<br>`src/Antiphon.Messaging.Slack/Antiphon.Messaging.Slack.csproj`<br>`src/Antiphon.Messaging.Client/Antiphon.Messaging.Client.csproj`<br>`src/Antiphon.Messaging.Client.Testing/Antiphon.Messaging.Client.Testing.csproj`<br>`src/Antiphon.Messaging.Gateway/Antiphon.Messaging.Gateway.csproj`<br>`src/Antiphon.Messaging.Service/Antiphon.Messaging.Service.csproj`<br>`src/Antiphon.Messaging.FakeGateway/Antiphon.Messaging.FakeGateway.csproj`<br>`tools/Antiphon.Messaging.SchemaGen/Antiphon.Messaging.SchemaGen.csproj`<br>`samples/EchoGateway/EchoGateway.csproj` |
| `tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj:4` | Native PTY host/client/protocol and custody; Windows and Linux lanes, QEMU native controls. No provider-specific project. | `src/Antiphon.PtyHost/Antiphon.PtyHost.csproj`<br>`src/Antiphon.PtyHost.Client/Antiphon.PtyHost.Client.csproj`<br>`src/Antiphon.PtyHost.Protocol/Antiphon.PtyHost.Protocol.csproj`<br>`tests/Antiphon.Card0490.NativeHarness/Antiphon.Card0490.NativeHarness.csproj` |
| `tests/Antiphon.ScriptHarnessHost/Antiphon.ScriptHarnessHost.csproj:4` | Owned script-execution helper; IsTestProject not set. | None |
| `tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj:4` | Runner HTTP/runtime, transcript, launch, custody, workspace and Herdr. Fake peers and installed-Herdr/Grok cases; no PostgreSQL fixture reference found. | `src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj`<br>`tests/Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj`<br>`src/Antiphon.FakeLlmApi/Antiphon.FakeLlmApi.csproj` |
| `tests/Antiphon.Tests/Antiphon.Tests.csproj:4` | Main server/domain/application/infrastructure/script/checkpoint tests. Unit + Integration + Slow; real vendor canaries, PostgreSQL/Docker and native Windows cases coexist. | `server/Antiphon.Server.csproj`<br>`scripts/fixtures/nightly/owned-child/OwnedChild.csproj`<br>`src/Antiphon.Resilience/Antiphon.Resilience.csproj`<br>`src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj`<br>`tests/Antiphon.DockerStack.Fixture/Antiphon.DockerStack.Fixture.csproj`<br>`tests/Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj`<br>`tests/Antiphon.ScriptHarnessHost/Antiphon.ScriptHarnessHost.csproj`<br>`tests/Antiphon.ChannelOutbound.Probe/Antiphon.ChannelOutbound.Probe.csproj`<br>`src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj`<br>`src/Antiphon.FakeLlmApi/Antiphon.FakeLlmApi.csproj`<br>`src/Antiphon.NightlyWatchdog/Antiphon.NightlyWatchdog.csproj`<br>`tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj`<br>`tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj`<br>`tests/Antiphon.Checkpoints.FilterHost/Antiphon.Checkpoints.FilterHost.csproj`<br>`tests/Antiphon.Checkpoints.RepeatHost/Antiphon.Checkpoints.RepeatHost.csproj`<br>`src/Antiphon.Messaging.Client.Testing/Antiphon.Messaging.Client.Testing.csproj`<br>`src/Antiphon.Messaging.Gateway/Antiphon.Messaging.Gateway.csproj`<br>`src/Antiphon.Messaging.Slack/Antiphon.Messaging.Slack.csproj`<br>`src/Antiphon.FakeClaude/Antiphon.FakeClaude.csproj`<br>`src/Antiphon.FakeGrok/Antiphon.FakeGrok.csproj` |
| `scripts/fixtures/nightly/c487-probe/Probe.csproj:1` | TUnit diagnostic fixture (IsTestProject=true); outside tests/. | None |

Additional lanes: `client/package.json:1` and `client/vite.config.ts:118` define Vitest/jsdom (not a real browser); `scripts/test-client.ps1:1` is its wrapper. The explicit shell census is `tests/test-execution-policy.json:577`; it is not every `scripts/test-*.ps1` file.

Extra dependency edges that a project-reference-only map misses:

- `tests/Antiphon.Tests/Antiphon.Tests.csproj:36` links CodexStartupFixtures from Pty tests; lines 46–55 link SessionRunner fake Herdr/HTTP/disposal fixtures; lines 131–137 link effort-screen, fake Slack and Shared classification/progress/apphost helpers. Lines 149–165 stage native PtyHost/FakeClaude/FakeGrok.
- `tests/Antiphon.E2E/Antiphon.E2E.csproj:37` links Messaging fake Slack and Shared helpers; its native-host build targets are at lines 63–66. The browser fixture consumes `client/dist` at runtime, independently of .csproj references.
- `tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj:30` links HerdrTestProcess and TestOwnedPtyHost; lines 64–66 stage PtyHost. Checkpoint LifecycleHost links `Antiphon.Tests/Checkpoints` sources at `tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj:14`.
- Main server references shared Pty/Messaging/Client/Runner.Contracts/Resilience at `server/Antiphon.Server.csproj:71`. SessionRunner references Pty/Contracts/PtyHost/PtyHost.Client at `src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj:33`; PtyHost references Pty/Protocol at `src/Antiphon.PtyHost/Antiphon.PtyHost.csproj:20`. All provider lanes can be affected by shared transport changes.

### Source-reconstructed release project-universe mismatch

The current policy lists six test projects (`tests/test-execution-policy.json:491`). `scripts/lib/nightly-policy.ps1:101` recursively finds every literal IsTestProject=true except bin/obj/bin-*; it does **not** exclude child fixture projects. Its comparison at line 142 therefore finds these five missing paths on this pinned tree:

```text
scripts/fixtures/nightly/c487-probe/Probe.csproj
tests/Antiphon.Checkpoints.FilterHost/Antiphon.Checkpoints.FilterHost.csproj
tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj
tests/Antiphon.Checkpoints.RepeatHost/Antiphon.Checkpoints.RepeatHost.csproj
tests/Antiphon.MarkdownPdf.Tests/Antiphon.MarkdownPdf.Tests.csproj
```

The caller at `scripts/lib/nightly-tests-impl.ps1:237` returns exit 2/incomplete before test execution on an unmatched universe. This result is reconstructed by comparing stored project XML and policy JSON, not a fresh release invocation. How the deployed lane or manual release work handles it is unverified. Fixture hosts and the primary MarkdownPdf suite have different roles; blindly treating all five as ordinary suites would erase that distinction.

## 3. Provider layout and real external TUI inventory

**No, there is no project per TUI/provider, nor an adapter subfolder per provider.**

| Provider | Provider-specific source locations | Shared dependencies |
| --- | --- | --- |
| Claude | `server/Infrastructure/Agents/Pty/ClaudeAdapter.cs:1`; `server/Infrastructure/Agents/SessionRunner/RunnerClaudeAdapter.cs:1`; `src/Antiphon.Agents.Pty/Claude*.cs`; `src/Antiphon.SessionRunner/ClaudeAuthProbe.cs`; `server/Application/Services/Claude*.cs` | server + Agents.Pty + SessionRunner/Contracts + PtyHost stack; hooks/launch/config/delivery infrastructure |
| Codex | `server/Infrastructure/Agents/Pty/CodexAdapter.cs:1`; `server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs:1`; `src/Antiphon.Agents.Pty/Codex*.cs`; `src/Antiphon.SessionRunner/Codex*.cs`; `server/Application/Services/Codex*.cs` | Same shared stack; rollout/transcript/version/launch logic; linked CodexStartupFixtures |
| Grok | `server/Infrastructure/Agents/SessionRunner/RunnerGrokAdapter.cs:1`; `src/Antiphon.Agents.Pty/Grok*.cs`; `src/Antiphon.SessionRunner/Grok*.cs`; `src/Antiphon.SessionRunner/HerdrGrokResumeGuard.cs`; `src/Antiphon.SessionRunner.Contracts/Grok*.cs`; `server/Application/Services/Grok*.cs` | Same shared stack; rules transport and native transcript/session files |
| Fakes | `src/Antiphon.FakeClaude/`, `src/Antiphon.FakeGrok/`, `src/Antiphon.FakeLlmApi/` | Separate **fake** projects, not vendor production projects; no FakeCodex project found. FakeLlmApi has Claude/Codex/Grok stub endpoints. |

The table below names real-CLI classes explicitly. Namespace follows the file: `Antiphon.Agents.Pty.Tests`, `Antiphon.Tests.Agents`, `Antiphon.Tests.Application`, `Antiphon.SessionRunner.Tests`, or `Antiphon.E2E`. Categories are literal source annotations; Explicit and method mixes are recorded separately. “Live” means the vendor executable is exercised, not that every case spends a model turn.

Gates: `tests/Antiphon.Agents.Pty.Tests/ClSession.cs:11` (Windows + ANTIPHON_HEADED_TESTS); `tests/Antiphon.Agents.Pty.Tests/CxSession.cs:32` (Windows + ANTIPHON_CODEX_HEADED_TESTS + isolated Codex home); `tests/Antiphon.Agents.Pty.Tests/GkSession.cs:16` (Windows + headed flag + Grok executable). RealCliStubProxy has its own Windows/CLI/opt-in gate at `tests/Antiphon.Tests/Agents/RealCliStubGate.cs:18`. Real CLI plus local stub is deliberately not classified as fake TUI.

| Provider | Class source | Declared categories | Resource distinction | Source dependencies |
| --- | --- | --- | --- | --- |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeAppendSystemPromptCanaryTests.cs:27` | Headed, OptIn, HeadedCanary | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeCompactionCanaryTests.cs:34` | Headed, OptIn, HeadedCanary | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeComposerCaptureProbeTests.cs:26` | Card0027; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeComposerRenderCanaryTests.cs:39` | Headed, OptIn, HeadedCanary | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeDangerousTests.cs:17` | Headed, OptIn | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeHeadedTests.cs:14` | Headed, OptIn | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeHeadedTests.cs:203` | HeadedLong, OptIn | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeHookAdditionalContextCanaryTests.cs:39` | Headed, OptIn, HeadedCanary, Card0247; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeInputProbeCanaryTests.cs:41` | Headed, OptIn, HeadedCanary, Card0103; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeInteractionTests.cs:21` | Headed, OptIn | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeInterruptCanaryTests.cs:32` | Headed, OptIn, HeadedCanary | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeLocalCommandCanaryTests.cs:35` | Headed, OptIn, HeadedCanary | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeOverlayCanaryTests.cs:26` | Headed, OptIn, HeadedCanary, Card0137; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudePasteLossCanaryTests.cs:31` | Card0027; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs:31` | Headed, OptIn, HeadedCanary; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlMenuCanaryTests.cs:22` | Headed, OptIn, HeadedCanary; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeSignalCanaryTests.cs:34` | Headed, OptIn, HeadedCanary | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitConfirmCanaryTests.cs:46` | Headed, OptIn, HeadedCanary, Card0055; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractLiveTests.cs:16` | Pty, Headed, OptIn | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeTrustPromptCanaryTests.cs:33` | Headed, OptIn, HeadedCanary | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeTuiModeTests.cs:20` | Headed, OptIn | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/FakeVsRealClipParityTests.cs:49` | Card0028; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/ClaudeVerifiedDeliveryTests.cs:27` | Pty, Headed, OptIn | Mixed: four claude argument rows + fakeclaude/short. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/OrchestratorWorkspaceLayoutCanaryTests.cs:23` | Card0251; Explicit | Mixed: two live Claude methods; Codex/Grok rows offline. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Claude | `tests/Antiphon.Agents.Pty.Tests/PtyPasteMarkerExperiments.cs:25` | Card0030; Explicit | Mixed: real Claude methods plus Node-only experiments. | Pty/Claude*.cs + shared Pty runner/detectors/screen/input; ClSession |
| Codex | `tests/Antiphon.Agents.Pty.Tests/CodexCanaryTests.cs:45` | Headed, OptIn, HeadedCanary, Card0099; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Codex*.cs + shared PTY/screen; CxSession |
| Codex | `tests/Antiphon.Agents.Pty.Tests/CodexComposerCanaryTests.cs:77` | Headed, OptIn, HeadedCanary, Card0099; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Codex*.cs + shared PTY/screen; CxSession |
| Codex | `tests/Antiphon.Agents.Pty.Tests/CodexDoneDetectionCanaryTests.cs:45` | Headed, OptIn, HeadedCanary, Card0108; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Codex*.cs + shared PTY/screen; CxSession |
| Codex | `tests/Antiphon.Agents.Pty.Tests/CodexMcpBootProbeTests.cs:29` | Headed, OptIn, HeadedCanary, Card0195; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Codex*.cs + shared PTY/screen; CxSession |
| Codex | `tests/Antiphon.Agents.Pty.Tests/CodexOverlayCanaryTests.cs:25` | Headed, OptIn, HeadedCanary, Card0137; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Codex*.cs + shared PTY/screen; CxSession |
| Grok | `tests/Antiphon.Agents.Pty.Tests/GrokCanaryTests.cs:35` | Headed, OptIn, HeadedCanary, Card0080; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Grok*.cs + shared PTY/screen; GkSession; native-session stub also FakeLlmApi |
| Grok | `tests/Antiphon.Agents.Pty.Tests/GrokMidTurnInterruptCanaryTests.cs:22` | Headed, OptIn, HeadedCanary; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Grok*.cs + shared PTY/screen; GkSession; native-session stub also FakeLlmApi |
| Grok | `tests/Antiphon.Agents.Pty.Tests/GrokNativeSessionCanaryTests.cs:18` | Headed, OptIn | Real vendor executable; consult body for local command vs model turn. | Pty/Grok*.cs + shared PTY/screen; GkSession; native-session stub also FakeLlmApi |
| Grok | `tests/Antiphon.Agents.Pty.Tests/GrokQuestionPopupCanaryTests.cs:28` | Headed, OptIn, HeadedCanary, Card0241; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Grok*.cs + shared PTY/screen; GkSession; native-session stub also FakeLlmApi |
| Grok | `tests/Antiphon.Agents.Pty.Tests/GrokSignInCanaryTests.cs:20` | Headed, OptIn, HeadedCanary, Card0324; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Grok*.cs + shared PTY/screen; GkSession; native-session stub also FakeLlmApi |
| Grok | `tests/Antiphon.Agents.Pty.Tests/GrokSubmitWhileWorkingCanaryTests.cs:25` | Headed, OptIn, HeadedCanary, Card0159, Card0355; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Grok*.cs + shared PTY/screen; GkSession; native-session stub also FakeLlmApi |
| Grok | `tests/Antiphon.Agents.Pty.Tests/GrokUsageOverlayCanaryTests.cs:25` | Headed, OptIn, HeadedCanary, Card0137; Explicit | Real vendor executable; consult body for local command vs model turn. | Pty/Grok*.cs + shared PTY/screen; GkSession; native-session stub also FakeLlmApi |
| Claude + Grok | `tests/Antiphon.Tests/Agents/Card0168InteractiveProbeTests.cs:25` | RealCliStubProxy, Integration; Explicit | Real vendor CLI + local API stub. | server RunnerClaude/RunnerGrok adapters + shared Pty/runner + FakeLlmApi; two provider probe methods |
| Claude | `tests/Antiphon.Tests/Agents/ClaudeAdapterIntegrationTests.cs:17` | Headed, OptIn, Integration | Real vendor executable; consult body for local command vs model turn. | server Claude adapters/launch/delivery + Pty + runner; stub rows additionally FakeLlmApi; B-server/Herdr compositions add app/DB |
| Claude | `tests/Antiphon.Tests/Agents/ClaudeEffortPromptCanaryTests.cs:15` | Integration | Live method gate despite Integration-only metadata. | server Claude adapters/launch/delivery + Pty + runner; stub rows additionally FakeLlmApi; B-server/Herdr compositions add app/DB |
| Claude | `tests/Antiphon.Tests/Agents/ClaudeHerdrRealCliStubProxyCanaryTests.cs:30` | RealCliStubProxy, Integration; Explicit | Real vendor CLI + local API stub. | server Claude adapters/launch/delivery + Pty + runner; stub rows additionally FakeLlmApi; B-server/Herdr compositions add app/DB |
| Claude | `tests/Antiphon.Tests/Agents/ClaudeRealCliStubProxyCanaryTests.cs:27` | RealCliStubProxy, Integration; Explicit | Real vendor CLI + local API stub. | server Claude adapters/launch/delivery + Pty + runner; stub rows additionally FakeLlmApi; B-server/Herdr compositions add app/DB |
| Claude | `tests/Antiphon.Tests/Agents/SpecialistToolPolicyTests.cs:29` | Integration, RealCliStubProxy; Explicit | Real vendor executable; consult body for local command vs model turn. | server Claude adapters/launch/delivery + Pty + runner; stub rows additionally FakeLlmApi; B-server/Herdr compositions add app/DB |
| Claude | `tests/Antiphon.Tests/Application/SlashCommandMenuReconciliationTests.cs:33` | Headed, OptIn, Integration, Slow | Real vendor executable; consult body for local command vs model turn. | server Claude adapters/launch/delivery + Pty + runner; stub rows additionally FakeLlmApi; B-server/Herdr compositions add app/DB |
| Codex | `tests/Antiphon.Tests/Agents/CodexAdapterIntegrationTests.cs:20` | Headed, OptIn, Card0108, Integration | Real vendor executable; consult body for local command vs model turn. | server Codex adapters/launch/delivery + Pty + runner/Contracts; stub rows additionally FakeLlmApi; B-server/Herdr compositions add app/DB |
| Codex | `tests/Antiphon.Tests/Agents/CodexBootWedgeProbeTests.cs:27` | Headed, OptIn, RealCliStubProxy, Integration; Explicit | Real vendor CLI + local API stub. | server Codex adapters/launch/delivery + Pty + runner/Contracts; stub rows additionally FakeLlmApi; B-server/Herdr compositions add app/DB |
| Codex | `tests/Antiphon.Tests/Agents/CodexCommandLengthSessionTests.cs:24` | RealCliStubProxy, Integration; Explicit | Real vendor CLI + local API stub. | server Codex adapters/launch/delivery + Pty + runner/Contracts; stub rows additionally FakeLlmApi; B-server/Herdr compositions add app/DB |
| Codex | `tests/Antiphon.Tests/Agents/CodexHerdrRealCliStubProxyCanaryTests.cs:34` | RealCliStubProxy, Integration; Explicit | Real vendor CLI + local API stub. | server Codex adapters/launch/delivery + Pty + runner/Contracts; stub rows additionally FakeLlmApi; B-server/Herdr compositions add app/DB |
| Codex | `tests/Antiphon.Tests/Agents/CodexRealCliStubProxyCanaryTests.cs:25` | RealCliStubProxy, Integration; Explicit | Real vendor CLI + local API stub. | server Codex adapters/launch/delivery + Pty + runner/Contracts; stub rows additionally FakeLlmApi; B-server/Herdr compositions add app/DB |
| Grok | `tests/Antiphon.Tests/Agents/GrokHerdrRealCliStubProxyCanaryTests.cs:30` | RealCliStubProxy, Integration; Explicit | Real vendor CLI + local API stub. | server RunnerGrokAdapter + Application GrokRules* + runner Grok*/Herdr + Contracts + Pty; dispatch compositions add app/DB |
| Grok | `tests/Antiphon.Tests/Agents/GrokRealCliStubProxyCanaryTests.cs:27` | RealCliStubProxy, Integration; Explicit | Real vendor CLI + local API stub. | server RunnerGrokAdapter + Application GrokRules* + runner Grok*/Herdr + Contracts + Pty; dispatch compositions add app/DB |
| Grok | `tests/Antiphon.Tests/Application/GrokRulesAutoCompactionCalibrationTests.cs:21` | Integration; Explicit | Real vendor executable; consult body for local command vs model turn. | server RunnerGrokAdapter + Application GrokRules* + runner Grok*/Herdr + Contracts + Pty; dispatch compositions add app/DB |
| Grok | `tests/Antiphon.Tests/Application/GrokRulesCompactionAcceptanceTests.cs:23` | Integration; Explicit | Real vendor executable; consult body for local command vs model turn. | server RunnerGrokAdapter + Application GrokRules* + runner Grok*/Herdr + Contracts + Pty; dispatch compositions add app/DB |
| Grok | `tests/Antiphon.Tests/Application/GrokRulesDispatchAcceptanceTests.cs:27` | Integration; Explicit | Real vendor executable; consult body for local command vs model turn. | server RunnerGrokAdapter + Application GrokRules* + runner Grok*/Herdr + Contracts + Pty; dispatch compositions add app/DB |
| Grok | `tests/Antiphon.Tests/Application/GrokRulesHerdrAcceptanceTests.cs:25` | Integration; Explicit | Real vendor executable; consult body for local command vs model turn. | server RunnerGrokAdapter + Application GrokRules* + runner Grok*/Herdr + Contracts + Pty; dispatch compositions add app/DB |
| Grok | `tests/Antiphon.Tests/Application/GrokRulesLiveMappedDispatchTests.cs:28` | Integration; Explicit | Real vendor executable; consult body for local command vs model turn. | server RunnerGrokAdapter + Application GrokRules* + runner Grok*/Herdr + Contracts + Pty; dispatch compositions add app/DB |
| Grok | `tests/Antiphon.Tests/Application/GrokRulesNativeReadWireTests.cs:21` | Integration; Explicit | Real vendor executable; consult body for local command vs model turn. | server RunnerGrokAdapter + Application GrokRules* + runner Grok*/Herdr + Contracts + Pty; dispatch compositions add app/DB |
| Grok | `tests/Antiphon.Tests/Agents/GrokSizeCaptureTests.cs:16` | Integration, OptIn; Explicit | Real grok executable, zero model input; C861_CAPTURE=1; no explicit Windows OS gate. | src/Antiphon.Agents.Pty Grok detectors + native PTY; test capture files |
| Grok/Herdr | `tests/Antiphon.SessionRunner.Tests/HerdrGrokNativeSessionLiveTests.cs:21` | Headed, OptIn | Real vendor executable; consult body for local command vs model turn. | SessionRunner Herdr/runtime/Grok + Pty/native session + FakeLlmApi for stub cases |
| Claude | `tests/Antiphon.E2E/DelegationPipelineE2ETests.cs:39` | Headed, OptIn, HeadedCanary | Real vendor executable; consult body for local command vs model turn. | server delegation/delivery/distillation + runner/Pty + real app/Postgres, owned Git scratch; not browser UI tests |
| Claude | `tests/Antiphon.E2E/DelegationSequencingE2ETests.cs:41` | Headed, OptIn, HeadedCanary | Real vendor executable; consult body for local command vs model turn. | server delegation/delivery/distillation + runner/Pty + real app/Postgres, owned Git scratch; not browser UI tests |
| Claude | `tests/Antiphon.E2E/OutputDistillationApplyCanaryTests.cs:33` | Headed, OptIn, HeadedCanary; Explicit | Requires operator approval file naming ClaudeCode and the approved model alias. | server delegation/delivery/distillation + runner/Pty + real app/Postgres, owned Git scratch; not browser UI tests |

Important nearby cases that names or categories misclassify:

- `tests/Antiphon.SessionRunner.Tests/HerdrNamedTabPlacementLiveTests.cs:18` tests installed Herdr tab placement with a pwsh child, not a provider launch; its eligibility helper also checks for a Grok installation. `tests/Antiphon.SessionRunner.Tests/HerdrLabelFollowLiveTests.cs:15` exercises an already test-owned installed Herdr session and label following, without establishing a provider turn. `tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalGuardedLiveTests.cs:14` launches installed stock Herdr with isolated config and a dummy Codex child, not real Codex; its actual eligibility gate is at `tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalGuardedLiveTests.cs:105`. These are desktop/UI resource cases.
- `tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTestsPty.cs:26` explicitly drives **FakeGrok** through a real isolated runner/native PTY; a Windows headed opt-in inside it does not make the TUI vendor-real.
- `tests/Antiphon.Agents.Pty.Tests/PtyInputLossExperiments.cs:19` uses a Node stdin probe, not a provider. `tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractTests.cs:18` has only fakeclaude argument rows; its helper also serves the separately named live class.
- `tests/Antiphon.Agents.Pty.Tests/ClaudeGoldenReplayTests.cs:23` and `tests/Antiphon.Agents.Pty.Tests/ClaudeEffortDismissalReplayTests.cs:26` replay stored real output offline. Real-origin data does not imply a new external process or API call.
- `Headed`, `NotInParallel("Headed")` and `ParallelLimiter<ProcessSpawnLimit>` are not synonymous: the latter two control execution concurrency; many fake/native tests use them. They cannot be used as a real-provider selector.

Static policy drift requiring Plan attention: `ClaudeHeadedLongTests` and `GrokMidTurnInterruptCanaryTests` and `GrokSizeCaptureTests` have live gates but no exact RC exclusion rows; `HerdrPaneDisposalGuardedLiveTests` has an installed-Herdr body gate and no exact RC exclusion. The RC matcher compares exact simple/FQN class names (`scripts/lib/nightly-policy.ps1:408`), so a sibling/prefix entry does not cover them. This is a source-level mismatch, not a newly observed runtime failure. The existing ChannelOutbound opt-in debt is independently documented in `docs/investigations/2026-10-16-release-v2026.10.15.1-notes.md:33`.

## 4. Real browser/UI and E2E classes

Playwright Chromium is launched at `tests/Antiphon.E2E/Fixtures/PlaywrightFixture.cs:38`; each browser case creates a fresh context/page at line 62. AntiphonAppFixture creates PostgreSQL at `tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs:29` and owns its runner. Browser classes therefore depend on **client/** as well as server/runner/data, not just client paths. API-only classes can also opt into client/dist, but AntiphonAppFixture.UsePrebuiltFrontend defaults to false (line 44); inspect each fixture configuration.

| Class source | Resources actually represented | Source dependencies |
| --- | --- | --- |
| `tests/Antiphon.E2E/AgentE2ETests.cs:15` | Real Chromium + app/DB/isolated runner | client/src + client build/config; server/Api, Application, Domain, Infrastructure/Data, Migrations; runner/PtyHost shared fixture |
| `tests/Antiphon.E2E/BoardE2ETests.cs:25` | Real Chromium + app/DB/isolated runner | client/src + client build/config; server/Api, Application, Domain, Infrastructure/Data, Migrations; runner/PtyHost shared fixture |
| `tests/Antiphon.E2E/DirectoryAutocompleteE2ETests.cs:18` | Real Chromium + app/DB/isolated runner | client/src + client build/config; server/Api, Application, Domain, Infrastructure/Data, Migrations; runner/PtyHost shared fixture |
| `tests/Antiphon.E2E/DispatchConcurrencySettingsE2ETests.cs:19` | Real Chromium + app/DB/isolated runner | client/src + client build/config; server/Api, Application, Domain, Infrastructure/Data, Migrations; runner/PtyHost shared fixture |
| `tests/Antiphon.E2E/OrchestratorE2ETests.cs:18` | Real Chromium + app/DB/isolated runner | client/src + client build/config; server/Api, Application, Domain, Infrastructure/Data, Migrations; runner/PtyHost shared fixture |
| `tests/Antiphon.E2E/PipelineGlanceE2ETests.cs:17` | Real Chromium + app/DB/isolated runner | client/src + client build/config; server/Api, Application, Domain, Infrastructure/Data, Migrations; runner/PtyHost shared fixture |
| `tests/Antiphon.E2E/ProjectDeleteE2ETests.cs:26` | Real Chromium + app/DB/isolated runner | client/src + client build/config; server/Api, Application, Domain, Infrastructure/Data, Migrations; runner/PtyHost shared fixture |
| `tests/Antiphon.E2E/SmokeE2ETests.cs:14` | Real Chromium + app/DB/isolated runner | client/src + client build/config; server/Api, Application, Domain, Infrastructure/Data, Migrations; runner/PtyHost shared fixture |
| `tests/Antiphon.E2E/WorkflowDeleteTests.cs:22` | Real Chromium + app/DB/isolated runner | client/src + client build/config; server/Api, Application, Domain, Infrastructure/Data, Migrations; runner/PtyHost shared fixture |
| `tests/Antiphon.E2E/WorkflowOutputTests.cs:30` | Real Chromium + app/DB/isolated runner | client/src + client build/config; server/Api, Application, Domain, Infrastructure/Data, Migrations; runner/PtyHost shared fixture |
| `tests/Antiphon.E2E/CardCliE2ETests.cs:29` | API/CLI contracts + app/DB; no Playwright calls in these classes | server API/services/data; scripts/card.ps1 or schedule.ps1 where applicable; client contract fixtures for snapshots; release scripts |
| `tests/Antiphon.E2E/ChannelE2ETests.cs:15` | API/CLI contracts + app/DB; no Playwright calls in these classes | server API/services/data; scripts/card.ps1 or schedule.ps1 where applicable; client contract fixtures for snapshots; release scripts |
| `tests/Antiphon.E2E/ContractSnapshotTests.cs:31` | API/CLI contracts + app/DB; no Playwright calls in these classes | server API/services/data; scripts/card.ps1 or schedule.ps1 where applicable; client contract fixtures for snapshots; release scripts |
| `tests/Antiphon.E2E/ScheduleCliE2ETests.cs:18` | API/CLI contracts + app/DB; no Playwright calls in these classes | server API/services/data; scripts/card.ps1 or schedule.ps1 where applicable; client contract fixtures for snapshots; release scripts |
| `tests/Antiphon.E2E/ReleaseGateIsolationTests.cs:25` | API/CLI contracts + app/DB; no Playwright calls in these classes | server API/services/data; scripts/card.ps1 or schedule.ps1 where applicable; client contract fixtures for snapshots; release scripts |
| `tests/Antiphon.E2E/ReleaseGateReportDeliveryTests.cs:23` | API/CLI contracts + app/DB; no Playwright calls in these classes | server API/services/data; scripts/card.ps1 or schedule.ps1 where applicable; client contract fixtures for snapshots; release scripts |
| `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:15` | Native delivery + app/DB + Git + runner; Windows assertion in LandDeliveryFixture | server delegation/landing/reply queue/runtime; runner/PtyHost; fixtures/LandDeliveryFixture* and OwnedLandServerChild |
| `tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs:14` | Native delivery + app/DB + Git + runner; Windows assertion in LandDeliveryFixture | server delegation/landing/reply queue/runtime; runner/PtyHost; fixtures/LandDeliveryFixture* and OwnedLandServerChild |
| `tests/Antiphon.E2E/WorktreeRetirementDeliveryE2ETests.cs:16` | Native delivery + app/DB + Git + runner; Windows assertion in LandDeliveryFixture | server delegation/landing/reply queue/runtime; runner/PtyHost; fixtures/LandDeliveryFixture* and OwnedLandServerChild |
| `tests/Antiphon.E2E/LandDeliveryFixtureProbeTests.cs:8` | Native delivery + app/DB + Git + runner; Windows assertion in LandDeliveryFixture | server delegation/landing/reply queue/runtime; runner/PtyHost; fixtures/LandDeliveryFixture* and OwnedLandServerChild |
| `tests/Antiphon.E2E/ChannelOutboundIsolatedTests.cs:16` | FakeGrok outbound + app/Postgres + Redpanda + Docker browser/PDF composition; mixed methods | server outbound/files/delivery; Messaging/Gateway/Slack; FakeGrok; MarkdownPdf; Fixtures/Card0418Browser/Dockerfile |
| `tests/Antiphon.E2E/DelegationPipelineE2ETests.cs:39` | Real provider, no Playwright calls | See real-TUI table; OutputDistillationApply adds explicit approval and Windows gate |
| `tests/Antiphon.E2E/DelegationSequencingE2ETests.cs:41` | Real provider, no Playwright calls | See real-TUI table; OutputDistillationApply adds explicit approval and Windows gate |
| `tests/Antiphon.E2E/OutputDistillationApplyCanaryTests.cs:33` | Real provider, no Playwright calls | See real-TUI table; OutputDistillationApply adds explicit approval and Windows gate |
| `tests/Antiphon.E2E/OutputDistillationCanaryGuardTests.cs:9` | Fixture/safety contracts; not app browser journeys | test fixtures + runner cleanup or provider safety guard; some native prerequisites |
| `tests/Antiphon.E2E/Fixtures/IsolatedSessionRunnerTeardownTests.cs:10` | Fixture/safety contracts; not app browser journeys | test fixtures + runner cleanup or provider safety guard; some native prerequisites |
| `tests/Antiphon.E2E/Fixtures/PtyBackendEnvGuard.cs:31` | Fixture/safety contracts; not app browser journeys | test fixtures + runner cleanup or provider safety guard; some native prerequisites |

The shared classification guard is also linked into E2E; it is a metadata test, not a browser class. Outside E2E, `tests/Antiphon.MarkdownPdf.Tests/MarkdownPdfRealBrowserTests.cs:14` is a real Linux Docker browser lane with its own headed opt-in (lines 23–26); its renderer's Unit tests use a fake browser child. No real browser launch was found in the main, Pty or SessionRunner test projects beyond installed Herdr desktop operations.

## 5. PostgreSQL, Docker and platform evidence

| Resource/lane | Classes or resource criterion | Source dependency and evidence |
| --- | --- | --- |
| Main PostgreSQL | Integration classes using TestDbFixture, TransactionalTestBase or the shared harnesses; detailed source-witness inventory in Appendix A. Category Integration alone is neither sufficient nor necessary proof of a DB operation. | server/Infrastructure/Data, server/Migrations, Domain, Application, Resilience; `tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs:28` warms only selections reaching store; `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:29` creates real postgres:16-alpine. |
| Main fixture reachability | SharedStoreWarmup follows selected methods, hooks, generated methods and test-assembly call bodies (depth 3). | Existing IL-level dependency mechanism at `tests/Antiphon.Tests/TestHelpers/SharedStoreWarmup.cs:35`; source grep cannot replace it. |
| Independent main PostgreSQL | SmokeTests; AgentTuiPersistenceTests (method-specific extra container). | `tests/Antiphon.Tests/SmokeTests.cs:25`; `tests/Antiphon.Tests/AgentTui/AgentTuiPersistenceTests.cs:433` |
| Main Redpanda broker | ChannelOutboundRecoveryTests, ChannelOutboundUnifiedTransportTests, ChannelOutboundUnifiedCrashTests, ChannelOutboundComposedTransportTests (all Integration). | server channel/outbound/recovery + Messaging/Gateway/Slack; `tests/Antiphon.Tests/Application/ChannelOutboundRecoveryTests.cs:368` and corresponding builder sites in those classes. |
| Real Docker operations | RollingVolumeRecycleDockerTests; RetiredTempContainerDockerTests (Integration + Slow). | scripts/fixtures/c1008-recycle-real-cases.mjs, c994-retired-temp-real-cases.mjs, c1008-recycle-cleanup.mjs and rolling deployment scripts; `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleDockerTests.cs:13`; `tests/Antiphon.Tests/Scripts/RetiredTempContainerDockerTests.cs:13` |
| Docker command tests using fixtures | DockerTestCommandTests, DockerStackSmokeCommandTests, DockerDeliveryRecoveryCommandTests use C590Harness; DockerStackContractTests/DocumentationTests are Unit. | These names are not proof of real Docker. `tests/Antiphon.Tests/Scripts/DockerTestCommandTests.cs:17` calls the controlled harness. |
| E2E PostgreSQL and Docker | All AntiphonAppFixture/SharedApp/LandDeliveryFixture consumers in section 4; ChannelOutboundIsolatedTests.Composed additionally Redpanda and Docker browser/PDF. | server/app/DB + runner; `tests/Antiphon.E2E/ChannelOutboundIsolatedTests.Composed.cs:113` |
| Pty and SessionRunner DB/Docker | No TestDbFixture, PostgreSqlBuilder or RedpandaBuilder consumers found in these two projects. Native child process and PTY use remains. | Direct project graph and source scan; PhoneHomeCommandDispatcherTests mentions Docker in supplied path/container data, not a container launch. |
| Messaging resources | KafkaInboundCommitTests, KafkaOutboundPayloadTests, KafkaConsumerGroupObservationTests, InboxConsumerServiceTests use Redpanda; InboxConversationMigrationTests uses PostgreSQL. | src/Antiphon.Messaging*/ + disposable containers; `tests/Antiphon.Messaging.Tests/Service/InboxConversationMigrationTests.cs:19` |
| Windows provider and UI | All ClSession/CxSession/GkSession and RealCliStubGate consumers above; native Herdr install; OutputDistillationApplyCanaryTests. | Gates cited in section 3; no complete Windows category exists. |
| Windows native PTY | WindowsPtyArgvNativeTests, ModernConPtyCommandLineTests, ModernPtyDa1Tests; many PtyAgentRunnerTests, fake contract and paste/Node cases have method-level Windows gates. | src/Antiphon.Agents.Pty native backend + ConPTY redistributable + PtyHost; Appendix B records OS checks without treating a portable class as wholly Windows-only. |
| Windows runner | CodexCliVersionWindowsTests; method-level Windows launch/refusal/custody/Herdr tests. | src/Antiphon.SessionRunner, Contracts, PtyHost stack; Appendix B. |
| Windows main | AgentTuiSecretProtectorTests; DirectoryLinkFixtureWindowsTests; WorktreeLockDiagnosticsWindowsTests; AgentTaskLandRegistrationSafetyWindowsTests; AgentTaskLandHalfResetWindowsTests; ChannelReplyAttachmentReaderWindowsTests; CheckpointRecoveryWindowsTests; ScriptHarnessWindowsOwnershipTests; other per-method OS branches in Appendix B. | server DPAPI/filesystem/landing; HostCleanup; Checkpoints; scripts. Names are evidence pointers, not platform disposition for every method. |
| Windows E2E | AgentTaskLandDeliveryE2ETests, DispatchBaseWarningDeliveryE2ETests, WorktreeRetirementDeliveryE2ETests and LandDeliveryFixtureProbeTests use Windows-asserting LandDeliveryFixture. | `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:59`; Linux cannot count these as portable browser coverage. |

## 6. Path regex evidence map for Plan

These anchored regexes identify **observed source families** over slash-normalized repository paths. They do not authorize omission, define rule precedence, or constitute a shipped selector. The consumers are lower-bound impact evidence from references/fixtures/namespaces, not a proved complete call graph. A source family's filename pattern does not capture its shared dependencies.

| Source-location regex | Observed consumer classes/rosters | Evidence / boundary |
| --- | --- | --- |
| ^server/Infrastructure/Agents/(Pty/ClaudeAdapter\.cs\|SessionRunner/RunnerClaudeAdapter\.cs)$ | Main Claude real-TUI rows; adjacent offline adapter tests | Adapter classes and table 3; main project reference to server. |
| ^server/Infrastructure/Agents/(Pty/CodexAdapter\.cs\|SessionRunner/(RunnerCodexAdapter\|CodexStartupCaptureStore)\.cs)$ | Main Codex real-TUI rows; adapter readiness/raw-response offline tests | Shared transport/application dependencies remain. |
| ^server/Infrastructure/Agents/SessionRunner/(RunnerGrokAdapter\|GrokStartupCaptureStore)\.cs$ | Main Grok real-TUI rows; FakeGrok readiness integration | RunnerGrokAdapterReadyTestsPty is fake vendor TUI despite native PTY. |
| ^src/Antiphon\.Agents\.Pty/(Claude\|Codex\|Grok)[^/]*\.cs$ | Matching provider Pty real-TUI + replay/parser tests; main adapters and runner depend on same assembly | Provider-specific filenames, all in one shared project. |
| ^src/Antiphon\.SessionRunner/(Claude\|Codex\|Grok\|HerdrGrok)[^/]*\.cs$ | Provider runner tests; main composed real-TUI/stub tests; Herdr Grok live classes | Transcript/auth/launch probes are not confined to adapters. |
| ^server/Application/Services/(Claude\|Codex\|Grok)[^/]*\.cs$ | Main provider/dispatch/rules tests; real-TUI and relevant E2E compositions | Launch args and rules transport are upstream of native provider tests. |
| ^src/Antiphon\.(Agents\.Pty\|SessionRunner\|SessionRunner\.Contracts\|PtyHost(?:\.(?:Client\|Protocol))?)/ | All providers can depend on shared native transport; all main/Pty/runner/PtyHost/E2E consumers | Shared project-reference edges; provider-only narrowing unproved. |
| ^client/ | Ten browser classes in section 4; client Vitest; ContractSnapshotTests and bundle consumers | PlaywrightFixture + AntiphonAppFixture; not every E2E is a browser. |
| ^server/(Api\|Application\|Domain\|Infrastructure\|Migrations)/ | Main server/DB tests plus E2E app/API/browser/delivery and composed provider tests | server reference, TestDbFixture migration and E2E real Program; broad shared impact. |
| ^src/Antiphon\.Messaging(?:\.[^/]+)?/ | Messaging project; main outbound broker tests; E2E channel/outbound | Explicit project references and linked fake Slack source. |
| ^(tools/Antiphon\.MarkdownPdf/\|tests/Antiphon\.MarkdownPdf[^/]*/) | MarkdownPdf suite (including Docker browser) + E2E outbound PDF composition | Separate primary suite missing from policy projects. |
| ^(src/Antiphon\.Fake(?:Claude\|Grok\|LlmApi)/\|tests/Shared/) | Fake/native/stub tests across main/Pty/runner/E2E; linked classification guards | Shared test infrastructure is cross-project input. |
| ^(scripts/(?:lib/)?(?:nightly\|release)[^/]*\|scripts/(publish-release\|register-release-gates)\.ps1$\|tests/test-execution-policy\.json$) | Named Nightly/ReleaseGate test classes; E2E release evidence classes; suite/UID census | Gate/policy changes affect what results mean; not ordinary narrative docs. |
| ^(scripts/(deploy-server2[^/]*\|fixtures/c(?:1008\|994)[^/]*)\|docker-compose[^/]*\.ya?ml$\|.*Dockerfile[^/]*$) | Real Docker recycle/retirement and matching controlled command fixtures; Docker browser fixture where matched | Filename query; other shell dependencies need explicit inspection. |
| ^(Directory\.Build\.(props\|targets)\|Directory\.Packages\.props\|global\.json\|NuGet\.Config\|Antiphon\.sln)$\|\.(csproj\|props\|targets)$ | Cross-project build/discovery/resource provenance; complete inventory relevant | Project references alone omit linked sources/staged files. |
| ^tests/ | Owning project/classes plus cross-project Compile Include consumers | A test/helper edit can affect several projects; test-only is not no-impact. |

**Always-run core:** no narrow change-aware core exists in this source. The existing unconditional profile obligations are seven nightly suites and eight RC suites, not a suitable newly chosen small core. Existing named guards available for Plan to assess include `tests/Shared/TestClassificationGuardTests.cs:8`, `tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs:11`, `tests/Antiphon.Tests/ProcessSpawnLimitTests.cs:13`, `tests/Antiphon.Agents.Pty.Tests/ProviderOracleGateTests.cs:9`, `tests/Antiphon.Tests/Scripts/NightlyDiscoveryParserTests.cs:8`, `tests/Antiphon.Tests/Scripts/NightlyExecutionDiagnosticTests.cs:8` and `tests/Antiphon.Tests/Scripts/ReleaseGatePolicyTests.cs:19`. Their presence is not a recommendation to run all of them on every change; no costs were measured here. The brief specifically forbids substituting the whole Unit lane.

**Fail-safe requirement from the card:** unknown paths select everything. There is no implementation of this changed-path rule in the current entrypoints. “Everything” is ambiguous between the full eligible automated profile and all manual/live/other-OS lanes; the latter still have explicit environment/approval/platform gates. Unknown path cannot silently grant distiller approval, enable paid providers, or convert skipped/missing UIDs into passes. Empty diff, deleted/renamed files, invalid SHAs and a changed selection policy also need an explicit Plan contract, not assumptions derived from this table.

## 7. Existing release and CARD-1281 hook points

| Existing boundary | What is present now | Constraint on the later Plan |
| --- | --- | --- |
| `scripts/release-candidate.ps1:39`; line 144 | SourceRef defaults to master; cutter fetches source and pins FETCH_HEAD into candidate identity. | Two-SHA selection needs a trustworthy baseline as well as the pinned candidate; no baseline input exists here. |
| `scripts/release-cut.ps1:59`; line 78 | Cutter call then nightly-run with Profile=rc, ExpectedSha and CandidateId. release-cut exposes no SourceRef forwarding or selected roster parameter. | Do not assume the proposed CARD-1281 aggregate train is wired merely because the cutter accepts a ref. |
| `scripts/lib/nightly-policy.ps1:74`; line 305 | Hash-validated policy/profile, required suite and exclusion resolution. | New dispositions cannot be hidden behind an old policy hash. |
| `scripts/lib/nightly-tests-impl.ps1:120`; line 468 | Native argv already accepts explicit classes and supplies filter; chunk.classes is the existing class execution boundary. | Not a new runner requirement; class execution and full-profile coverage accounting are separate. |
| `scripts/lib/nightly-coverage.ps1:593`; `scripts/lib/nightly-tests-impl.ps1:581` | Expanded UID census plus required suite union check; stale/missing/zero/duplicate/skip evidence remains incomplete. | Omitted tests cannot disappear only from the runner side of the contract. |
| `scripts/lib/release-gate.ps1:529`; line 579 | Allowlisted release manifest and publication validation of candidate/green authority. | Selection authority must not be confused with verified test execution or publication permission. |
| `docs/superpowers/plans/2026-10-15-card-1281-pipeline-speed-plan.md:111` | Planned member immutable SHAs/patch ranges/selection/deferrals, separate TrainSelected admission, bounded-change eligibility. | This is an existing plan, not established deployment. |
| `docs/superpowers/plans/2026-10-15-card-1281-pipeline-speed-plan.md:123` | Planned target B, aggregate A, frozen membership manifest and exact candidate binding; step 5 runs existing RC census. | A selector baseline/candidate must correspond to the train identity, not an arbitrary previous successful task. |
| `docs/superpowers/plans/2026-10-15-card-1281-pipeline-speed-plan.md:150`; line 160 | Failed recomposition requires full required RC again; scheduled master and RC credit remain separate. | CARD-1313 would change a selection policy explicitly; it does not inherit permission to weaken that gate. |

No script interface or output schema has been designed here. The requested two-SHA input, selected rosters and matching-rule explanation are recorded card requirements. The smallest safe implementation and slice order are Plan decisions because the existing census/publication authority must agree with the selection.

## 8. Stored CARD-1287 roster and timing evidence

| Stored roster | Original compiled SHA | Stored executions | What it establishes |
| --- | --- | --- | --- |
| Linux: `docs/investigations/2026-10-15-card-1287-linux-unit-roster.txt:1`; paired filter at line 1 | f6310a452a911b45b0a0946feb9ac49dcc71bbfd | 373 classes; 4,516 passed / 0 failed / 0 skipped each; outer walls 2,534.527s and 2,433.533s. `docs/investigations/2026-10-15-card-1287-linux-unit.md:86` | Historical explicitly reduced Unit subset; 35 whole-class exclusions. Not a current-master discovery or heavy-TUI roster. |
| Windows: `docs/investigations/2026-10-15-card-1287-windows-unit-roster.txt:1`; paired filter at line 1 | cdb18eef5013bfa45336dfd304a022b489686bbc | 361 classes; 4,526 passed / 0 failed / 0 skipped each; outer walls 233.641s and 218.501s. `docs/investigations/2026-10-15-card-1287-windows-unit.md:152` | Historical reduced Unit subset; 48 whole-class exclusions. Does not clear Q1/Q2 or qualify later master. |
| Historical full Windows Unit | a30b7f4297400f010ce70188ac99ebe55be51931 | 5,591 cases: 5,108 passed, 94 failed, 389 skipped; outer 52m33.981s including 15s lease wait. `docs/investigations/2026-10-15-card-1287-windows-unit-lane-5ca376de.md:3` | Failed historical full lane; do not replace it with subset counts or infer the whole lane is cheap. |

Fresh **file inspection only**: Linux list 373, Windows list 361, intersection 343, Linux-only 30, Windows-only 18. Both filter files contain one literal expression. Their source-pinned qualification cannot transfer to this master SHA. Trailing wildcard operands and the excluded `RunnerGrokAdapterReadyTests` / `OutboundConversionManifestTests` prefix collisions are documented at `docs/investigations/2026-10-15-card-1287-linux-unit.md:7` and its exclusions table; exact expanded-UID reconciliation remains necessary.

Stored raw TRX paths and hashes are recorded in the cited documents (Linux final1/final2; Windows gate1/gate2). This investigation read the committed reports and roster/filter bytes, **not** those raw files or a fresh compiled assembly. Performance differences mix platforms, rosters, binaries and host load; they do not quantify savings from a provider path selector.

## 9. Risks and remaining evidence gaps

- A provider-only adapter change can be narrow; a shared runner, delivery, terminal parser, launch contract, hook or build input can affect all providers. Namespaced file ownership is not runtime dependency closure.
- Whole-class policy can drop required fake/offline rows alongside live rows (ClaudeVerifiedDeliveryTests, OrchestratorWorkspaceLayoutCanaryTests, PtyPasteMarkerExperiments). Argument rows and method identity are material.
- Browser-only client mapping misses server API/DTO/auth/workflow changes affecting those same browser journeys. A static project graph misses client/dist and linked/staged fixture dependencies.
- Docker is both a direct operation and an indirect prerequisite of PostgreSQL/Redpanda fixtures. Fake Docker command harnesses must not be counted as real Docker coverage. Database startup depends on selected method/hook reachability, not merely loading Antiphon.Tests.
- Current RC project universe and several live-gate exclusion gaps are reconstructed above. The historical manual release evidence is not proof that the current unattended entrypoint passes those preflights.
- Categories do not supply complete platform or resource classification. A class may contain both portable and Windows-only cases; an early return may report passed without native execution. Explicit selections still require the intended environment, owned resources and gates.
- The CARD-1287 rosters include quarantine/platform/provenance exclusions and are pinned to older binaries. They are evidence inputs, not an automatically approved always-run core or release-wide complete set.
- Unknown/new/moved/deleted paths, renamed classes, new projects, shared helpers, case/separator normalization and policy edits are omission risks. The card's fallback requirement is not implemented.
- No current-master build, discovery, test duration, database row or real provider transcript was requested or collected. Runtime admission, reachable methods and savings remain unmeasured. A new build/expanded-UID census and scoped commissioned verification would resolve these, after Plan establishes the required selection contract.

**Not done, noted:** Plan the requested two-SHA explainable selector, narrow always-run core, unknown-path fallback, release/train authority integration and implementation slices from this dependency evidence; no fix design, production/test change or execution was performed here.

## Appendix A. Source witnesses for PostgreSQL-related classes

This appendix records test-bearing files with a direct shared-store/container reference or a reference to an explicitly enumerated DB fixture helper (AntiphonWebAppFactory, BridgeQueueHarness, landing/phone-home/distillation/session-state and the corresponding owned-world fixtures). Unit-only files with only a helper reference are omitted. It is a conservative **source witness inventory**, not a compiled call-graph proof: comments, supplied connection strings, fake lifecycle seams and unused methods can match; partial-class attributes may be on another file. The existing SharedStoreWarmup IL scan is the stronger runtime mechanism. Each row cites its matching line and local declared categories; inspect the linked fixture before treating a whole class as requiring the database. Main test source depends on server/Application, Domain, Infrastructure/Data and Migrations through its server reference; relevant narrower namespaces are visible in the path.

| Source witness | Top-level class declarations | Local categories | Evidence type |
| --- | --- | --- | --- |
| `tests/Antiphon.E2E/AgentE2ETests.cs:17` | AgentE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:20` | AgentTaskLandDeliveryE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/BoardE2ETests.cs:34` | BoardE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/CardCliE2ETests.cs:38` | CardCliE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/ChannelE2ETests.cs:24` | ChannelE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/ChannelOutboundIsolatedTests.Composed.cs:113` | ChannelOutboundIsolatedTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.E2E/ChannelOutboundIsolatedTests.cs:24` | ChannelOutboundIsolatedTests | Headed, OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/ContractSnapshotTests.cs:21` | ContractSnapshotTests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/DelegationPipelineE2ETests.cs:75` | DelegationPipelineE2ETests | Headed, OptIn, HeadedCanary | E2E fixture reference |
| `tests/Antiphon.E2E/DelegationSequencingE2ETests.cs:50` | DelegationSequencingE2ETests | Headed, OptIn, HeadedCanary | E2E fixture reference |
| `tests/Antiphon.E2E/DirectoryAutocompleteE2ETests.cs:20` | DirectoryAutocompleteE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs:19` | DispatchBaseWarningDeliveryE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/DispatchConcurrencySettingsE2ETests.cs:25` | DispatchConcurrencySettingsE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/LandDeliveryFixtureProbeTests.cs:13` | LandDeliveryFixtureProbeTests | (none on this file) | E2E fixture reference |
| `tests/Antiphon.E2E/OrchestratorE2ETests.cs:27` | OrchestratorE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/OutputDistillationApplyCanaryTests.cs:48` | OutputDistillationApplyCanaryTests | Headed, OptIn, HeadedCanary | E2E fixture reference |
| `tests/Antiphon.E2E/PipelineGlanceE2ETests.cs:19` | PipelineGlanceE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/ProjectDeleteE2ETests.cs:35` | ProjectDeleteE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/ReleaseGateIsolationTests.cs:13` | ReleaseGateIsolationTests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/ReleaseGateReportDeliveryTests.cs:27` | ReleaseGateReportDeliveryTests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/ScheduleCliE2ETests.cs:28` | ScheduleCliE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/SmokeE2ETests.cs:10` | SmokeE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/WorkflowDeleteTests.cs:34` | WorkflowDeleteTests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/WorkflowOutputTests.cs:41` | WorkflowOutputTests | OptIn | E2E fixture reference |
| `tests/Antiphon.E2E/WorktreeRetirementDeliveryE2ETests.cs:24` | WorktreeRetirementDeliveryE2ETests | OptIn | E2E fixture reference |
| `tests/Antiphon.Tests/AgentTui/AgentTuiApiTests.cs:761` | AgentTuiApiTests, AgentTuiApiWebAppFactory, RecordingApiRunnerProcessProbe, RecordingApiSecretProtector | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/AgentTui/AgentTuiDiscoveryTests.cs:24` | AgentTuiDiscoveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/AgentTui/AgentTuiLaunchResolverTests.cs:28` | AgentTuiLaunchResolverTests, RecordingLaunchSecretProtector, OptionsMonitorStub | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/AgentTui/AgentTuiModelArgumentMigrationTests.cs:21` | AgentTuiModelArgumentMigrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/AgentTui/AgentTuiPersistenceTests.cs:433` | AgentTuiPersistenceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/AgentTui/AgentTuiProfileConcurrencyTests.cs:25` | AgentTuiProfileConcurrencyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/AgentTui/AgentTuiProfileImporterBackfillTests.cs:17` | AgentTuiProfileImporterBackfillTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/AgentTui/AgentTuiProfileServiceTests.cs:28` | AgentTuiProfileServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/AgentTui/AgentTuiSecretMigratorTests.cs:115` | AgentTuiSecretMigratorTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Agents/ClaudeHerdrRealCliStubProxyCanaryTests.cs:45` | ClaudeHerdrRealCliStubProxyCanaryTests | RealCliStubProxy, Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Agents/ClaudeRealCliStubProxyCanaryTests.cs:172` | ClaudeRealCliStubProxyCanaryTests | RealCliStubProxy, Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Agents/CodexBootWedgeProbeTests.cs:175` | CodexBootWedgeProbeTests | Headed, OptIn, RealCliStubProxy, Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Agents/CodexCommandLengthSessionTests.cs:229` | CodexCommandLengthSessionTests | RealCliStubProxy, Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Agents/CodexHerdrRealCliStubProxyCanaryTests.cs:191` | CodexHerdrRealCliStubProxyCanaryTests | RealCliStubProxy, Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Agents/CodexRealCliStubProxyCanaryTests.cs:93` | CodexRealCliStubProxyCanaryTests | RealCliStubProxy, Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Agents/GrokHerdrRealCliStubProxyCanaryTests.cs:155` | GrokHerdrRealCliStubProxyCanaryTests | RealCliStubProxy, Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Agents/GrokRealCliStubProxyCanaryTests.cs:154` | GrokRealCliStubProxyCanaryTests | RealCliStubProxy, Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Agents/PhoneHomeConnectionTests.cs:28` | PhoneHomeConnectionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Agents/PhoneHomeEpochAgreementTests.cs:35` | PhoneHomeEpochAgreementTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Agents/SpecialistToolPolicyTests.cs:82` | SpecialistToolPolicyTests | Integration, RealCliStubProxy | direct store/container symbol |
| `tests/Antiphon.Tests/Api/ExpectationHoldReleaseEndpointTests.cs:42` | ExpectationHoldReleaseEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Api/ExpectationStatusTests.cs:124` | ExpectationStatusTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Api/OperatorDashboardLoginTests.cs:19` | OperatorDashboardLoginTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Api/OperatorShutdownEndpointTests.cs:59` | OperatorShutdownEndpointTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Api/OrchestratorInstructionsEndpointTests.cs:19` | OrchestratorInstructionsEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Api/RolePolicyEndpointTests.cs:251` | RolePolicyEndpointTests, RolePolicyApiWebAppFactory, RolePolicyUnsupportedValuesWebAppFactory | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/ApiKeys/ApiKeyApiTests.cs:193` | ApiKeyApiTests, ApiKeyApiWebAppFactory | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/ApiKeys/ApiKeyEnvResolverTests.cs:283` | ApiKeyEnvResolverTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/ApiKeys/ApiKeyLaunchPathTests.cs:41` | ApiKeyLaunchPathTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/ApiKeys/ApiKeyStoreTests.cs:276` | ApiKeyStoreTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/ApiKeys/DelegationCapabilityApiTests.cs:15` | DelegationCapabilityApiTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/ApiKeys/LaunchEnvLayersIntegrationTests.cs:32` | LaunchEnvLayersIntegrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AdvisoryChangePolicyTests.Correction.cs:138` | AdvisoryChangePolicyTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AdvisoryChangePolicyTests.cs:69` | AdvisoryChangePolicyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentAttachHerdrTests.cs:534` | AgentAttachHerdrTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentBundleAttachmentTests.cs:226` | AgentBundleAttachmentTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentChannelServiceIntegrationTests.cs:268` | AgentChannelServiceIntegrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentControlServiceIntegrationTests.cs:111` | AgentControlServiceIntegrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentCreateRaceTests.cs:154` | AgentCreateRaceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentCreateSupervisionTests.cs:96` | AgentCreateSupervisionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentModelLevelBindTests.cs:21` | AgentModelLevelBindTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentNamePinTests.cs:277` | AgentNamePinTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentPinnedInstructionCompositionTests.cs:208` | AgentPinnedInstructionCompositionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentPinnedInstructionEndpointTests.cs:24` | AgentPinnedInstructionEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentPinnedInstructionServiceTests.cs:675` | AgentPinnedInstructionServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentRemoteControlGateTests.cs:233` | AgentRemoteControlGateTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentReplyStyleEndpointTests.cs:19` | AgentReplyStyleEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentReplyStyleTests.cs:253` | AgentReplyStyleTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentServiceIntegrationTests.cs:633` | AgentServiceIntegrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentSessionBackendTests.cs:560` | AgentSessionBackendTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentSessionInterruptedLaunchResumeTests.cs:326` | AgentSessionInterruptedLaunchResumeTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentSessionLaunchFailureTests.cs:2510` | AgentSessionLaunchFailureTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentSessionLaunchQueueOwnershipTests.cs:85` | AgentSessionLaunchQueueOwnershipTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentSessionRuntimeActivityTests.cs:22` | AgentSessionRuntimeActivityTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentSessionRuntimeTests.Persist.cs:129` | AgentSessionRuntimeTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentSessionRuntimeTests.cs:185` | AgentSessionRuntimeTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentSessionServiceIntegrationTests.cs:1282` | AgentSessionServiceIntegrationTests | GitIntegration, Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentStartRecoveryTests.cs:155` | AgentStartRecoveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentSupervisionTests.cs:539` | AgentSupervisionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentSystemPromptLaunchTests.cs:84` | AgentSystemPromptLaunchTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskAgentKindTests.cs:684` | AgentTaskAgentKindTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskAnswerTests.cs:21` | AgentTaskAnswerTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskAutoTitleTests.cs:351` | AgentTaskAutoTitleTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskCallerResolutionTests.cs:280` | AgentTaskCallerResolutionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskCancelReasonTests.cs:22` | AgentTaskCancelReasonTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskCardBindingTests.cs:33` | AgentTaskCardBindingTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskCatchUpSettlementTests.cs:232` | AgentTaskCatchUpSettlementTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskCheckInterpreterTests.cs:1089` | AgentTaskCheckInterpreterTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskCheckRemapTests.cs:90` | AgentTaskCheckRemapTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskCheckScheduleTests.cs:234` | AgentTaskCheckScheduleTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskCheckSweepTests.cs:978` | AgentTaskCheckSweepTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskCommitEndpointTests.cs:319` | AgentTaskCommitEndpointTests, CommitEndpointWebAppFactory, CommitChainBoundary | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskConcurrencyLimitTests.cs:56` | AgentTaskConcurrencyLimitTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskDeadSessionReconciliationTests.cs:744` | AgentTaskDeadSessionReconciliationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskDecisionQuestionApiTests.cs:21` | AgentTaskDecisionQuestionApiTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskDecisionQuestionIntegrationTests.cs:433` | AgentTaskDecisionQuestionIntegrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskDeliveryWatchdogTests.cs:2211` | AgentTaskDeliveryWatchdogTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskDetailBlockedContextTests.cs:259` | AgentTaskDetailBlockedContextTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.CardBranchNames.cs:31` | AgentTaskDispatchBaseGuardTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.SiblingWarnings.cs:456` | AgentTaskDispatchBaseGuardTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.cs:41` | AgentTaskDispatchBaseGuardTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskDispatchFailureTests.cs:29` | AgentTaskDispatchFailureTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskEffectiveModelChipTests.cs:66` | AgentTaskEffectiveModelChipTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskInputFallbackTests.cs:474` | AgentTaskInputFallbackTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskInputSpillTests.cs:24` | AgentTaskInputSpillTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskInternalDecisionLifecycleTests.cs:357` | AgentTaskInternalDecisionLifecycleTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskInternalDecisionMigrationTests.cs:29` | AgentTaskInternalDecisionMigrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandActiveSourceClaimTests.cs:44` | AgentTaskLandActiveSourceClaimTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandAdmissionControlledTests.cs:47` | AgentTaskLandAdmissionControlledTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandAdmissionTests.cs:32` | AgentTaskLandAdmissionTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandAdoptionTests.cs:23` | AgentTaskLandAdoptionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandApprovalPersistenceTests.cs:28` | AgentTaskLandApprovalPersistenceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandApprovalRecoveryTests.cs:22` | AgentTaskLandApprovalRecoveryTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandApprovalRequestTests.cs:29` | AgentTaskLandApprovalRequestTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandBareRepositoryCleanupTests.cs:239` | AgentTaskLandBareRepositoryCleanupTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandBoundaryControlledTests.cs:107` | AgentTaskLandBoundaryControlledTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandBoundaryTests.cs:23` | AgentTaskLandBoundaryTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandCheckpointMatrixTests.cs:19` | AgentTaskLandCheckpointMatrixTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandCleanupSafetyTests.cs:33` | AgentTaskLandCleanupSafetyTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandConcurrencyControlledTests.cs:31` | AgentTaskLandConcurrencyControlledTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandConcurrencyTests.cs:23` | AgentTaskLandConcurrencyTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandDispatchYieldTests.cs:262` | AgentTaskLandDispatchYieldTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandEnqueueTests.cs:16` | AgentTaskLandEnqueueTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandFailureDiagnosticTests.cs:29` | AgentTaskLandFailureDiagnosticTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:749` | AgentTaskLandHalfResetTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandHeldNotificationTests.cs:29` | AgentTaskLandHeldNotificationTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandHoldVisibilityTests.cs:33` | AgentTaskLandHoldVisibilityTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandIdentityMatrixTests.cs:18` | AgentTaskLandIdentityMatrixTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandIndexLockTests.cs:17` | AgentTaskLandIndexLockTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandLocalContainmentTests.cs:19` | AgentTaskLandLocalContainmentTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandMonitoringTests.cs:40` | AgentTaskLandMonitoringTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandNotificationPersistenceTests.H5.cs:16` | AgentTaskLandNotificationPersistenceTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandNotificationPersistenceTests.cs:96` | AgentTaskLandNotificationPersistenceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandNotificationRecoveryTests.H5.cs:27` | AgentTaskLandNotificationRecoveryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandNotificationRecoveryTests.cs:37` | AgentTaskLandNotificationRecoveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandPersistenceFailureTests.cs:28` | AgentTaskLandPersistenceFailureTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandPreparationIdentityTests.cs:19` | AgentTaskLandPreparationIdentityTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.Delivery.cs:34` | AgentTaskLandPublicationTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs:19` | AgentTaskLandPublicationTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandQueueAgingTests.cs:26` | AgentTaskLandQueueAgingTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandQueuedReceiptTests.cs:71` | AgentTaskLandQueuedReceiptTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandReceiptTests.Scan.cs:33` | AgentTaskLandReceiptTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandReceiptTests.cs:30` | AgentTaskLandReceiptTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandReceiptWatermarkSafetyTests.cs:93` | AgentTaskLandReceiptWatermarkSafetyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandReceiptWatermarkTests.cs:10` | AgentTaskLandReceiptWatermarkTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandRecoveryTests.cs:25` | AgentTaskLandRecoveryTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandRefusedRetryTests.cs:301` | AgentTaskLandRefusedRetryTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandRegistrationSafetyTests.cs:182` | AgentTaskLandRegistrationSafetyTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandRemoteSourceRetentionATests.cs:27` | AgentTaskLandRemoteSourceRetentionATests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandRemoteSourceRetentionBTests.cs:30` | AgentTaskLandRemoteSourceRetentionBTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandRemoteSourceRetentionCTests.cs:33` | AgentTaskLandRemoteSourceRetentionCTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandRemoteSourceRetentionDTests.cs:35` | AgentTaskLandRemoteSourceRetentionDTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandRemovalMatrixTests.cs:45` | AgentTaskLandRemovalMatrixTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandRequestTests.cs:43` | AgentTaskLandRequestTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandSourceFreshnessTests.cs:30` | AgentTaskLandSourceFreshnessTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandSourcePersistenceTests.cs:30` | AgentTaskLandSourcePersistenceTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandStageOutcomeTests.cs:36` | AgentTaskLandStageOutcomeTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandSweepTests.cs:29` | AgentTaskLandSweepTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLandTargetRaceFakeTests.cs:21` | AgentTaskLandTargetRaceFakeTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandTargetRaceTests.cs:23` | AgentTaskLandTargetRaceTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandVerifierTests.cs:39` | AgentTaskLandVerifierTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskLandingPersistenceTests.cs:20` | AgentTaskLandingPersistenceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskListEndpointTests.cs:27` | AgentTaskListEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskListStatusFilterTests.cs:182` | AgentTaskListStatusFilterTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskLocalMergeSafetyTests.cs:27` | AgentTaskLocalMergeSafetyTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskMidTurnRefineTests.cs:871` | AgentTaskMidTurnRefineTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskOverdueDeadlineTests.cs:948` | AgentTaskOverdueDeadlineTests, AgentTaskDispatcherWiringTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusC100Tests.cs:21` | AgentTaskPipelineStatusTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusC1076Tests.cs:33` | AgentTaskPipelineStatusTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusC557Tests.cs:115` | AgentTaskPipelineStatusTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs:36` | AgentTaskPipelineStatusTests, AgentTaskPipelineEndpointTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskPoolTests.cs:428` | AgentTaskPoolTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskProjectScopeTests.cs:28` | AgentTaskProjectScopeTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskRefineTests.cs:242` | AgentTaskRefineTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskReplyC547HoldTests.cs:58` | AgentTaskReplyIntegrationTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.ChannelOutboundRuntime.cs:35` | AgentTaskReplyIntegrationTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs:2501` | AgentTaskReplyIntegrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskReplyOverlayTests.cs:27` | AgentTaskReplyOverlayTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskReuseEnqueueTests.cs:241` | AgentTaskReuseEnqueueTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskReviewEvidenceTests.cs:21` | AgentTaskReviewEvidenceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskRoleBindingTests.cs:18` | AgentTaskRoleBindingTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskScopeOverlapCreateTests.cs:209` | AgentTaskScopeOverlapCreateTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskScopedListEndpointTests.cs:27` | AgentTaskScopedListEndpointTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentTaskScopedListTests.cs:17` | AgentTaskScopedListTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskServiceIntegrationTests.cs:1054` | AgentTaskServiceIntegrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskSettlementRaceTests.cs:305` | AgentTaskSettlementRaceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskStallEscalationTests.cs:232` | AgentTaskStallEscalationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskStandingAgentDispatchTests.cs:497` | AgentTaskStandingAgentDispatchTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskWorktreeBaseCreateTests.cs:37` | AgentTaskWorktreeBaseCreateTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskWorktreeBaseMigrationTests.cs:22` | AgentTaskWorktreeBaseMigrationTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskWorktreeBaseResolverTests.cs:34` | AgentTaskWorktreeBaseResolverTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskWorktreeContinuityTests.cs:35` | AgentTaskWorktreeContinuityTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AgentTaskWorktreeLockOutcomeTests.cs:44` | AgentTaskWorktreeLockOutcomeTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AgentWorkspaceProvisionerTests.cs:357` | AgentWorkspaceProvisionerTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AlertPersistenceTests.cs:197` | AlertPersistenceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AlertRoutingTests.cs:149` | AlertRoutingTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ApiErrorRecoveryServiceTests.cs:28` | ApiErrorRecoveryServiceTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AppHostWatchdogStateAttentionServiceTests.cs:245` | AppHostWatchdogStateAttentionServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AttentionServiceCommitRecoveryTests.cs:149` | AttentionServiceTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:331` | AttentionServiceTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AttentionServiceTests.cs:2559` | AttentionServiceTests, AttentionApiTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AuditArchiveEndpointTests.cs:109` | AuditArchiveEndpointTests, AuditArchiveWebAppFactory | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/AwayDigestNotifierTests.cs:240` | AwayDigestNotifierTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/AwayDigestProjectionTests.cs:241` | AwayDigestProjectionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/BlockedTaskNotifierTests.cs:182` | BlockedTaskNotifierTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/BlockedTaskParkDeliveryTests.cs:982` | BlockedTaskParkDeliveryTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/BlockedTaskParkReclaimTests.Unloadable.cs:177` | BlockedTaskParkReclaimTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/BlockedTaskParkReclaimTests.cs:39` | BlockedTaskParkReclaimTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/BlockedTaskParkReleaseTests.cs:25` | BlockedTaskParkReleaseTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/BlockedTaskParkReplyAdmissionTests.cs:29` | BlockedTaskParkReplyAdmissionTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/BlockedTaskParkResumeTests.cs:29` | BlockedTaskParkResumeTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/BlockedTaskParkStateTests.cs:20` | BlockedTaskParkStateTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/BlockedTaskSyncRecoveryTests.cs:38` | BlockedTaskSyncRecoveryTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/BoardCardOrderApiTests.cs:19` | BoardCardOrderApiTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/BoardCardOrderIntegrationTests.cs:17` | BoardCardOrderIntegrationTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/BoardProjectArchiveApiTests.cs:21` | BoardProjectArchiveApiTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/BoardProjectArchiveTests.cs:211` | BoardProjectArchiveTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/BoardServiceIntegrationTests.cs:469` | BoardServiceIntegrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/BootLivenessProbeScopeTests.cs:147` | BootLivenessProbeScopeTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/BootReplyWatchTests.cs:240` | BootReplyWatchTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/BootReplyWatchdogTests.cs:349` | BootReplyWatchdogTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/BootStallAttentionTests.cs:239` | BootStallAttentionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/BootStallDetectionTests.cs:76` | BootStallDetectionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/BootStallWorkingTickCharacterizationTests.cs:29` | BootStallWorkingTickCharacterizationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/BootWedgeRelaunchParityTests.cs:32` | BootWedgeRelaunchParityTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CapacityRecoveryAcceptanceTests.cs:25` | CapacityRecoveryAcceptanceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CapacityRecoveryAttentionTests.cs:23` | CapacityRecoveryAttentionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CapacityRecoveryCompatibilityTests.cs:21` | CapacityRecoveryCompatibilityTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CapacityRecoveryGrantLivenessTests.cs:23` | CapacityRecoveryGrantLivenessTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CapacityRecoveryPersistenceTests.cs:24` | CapacityRecoveryPersistenceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CapacityRecoveryQueueTests.cs:23` | CapacityRecoveryQueueTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CapacityRecoveryRefusalTests.cs:26` | CapacityRecoveryRefusalTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CapacityRecoverySupervisionTests.cs:24` | CapacityRecoverySupervisionTests, CapacityRecoverySupervisionVerifyExtensions | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CapacityRecoveryTaskTests.cs:24` | CapacityRecoveryTaskTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CapacityWaitOrphanSweepTests.cs:21` | CapacityWaitOrphanSweepTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardAliasApiTests.cs:24` | CardAliasApiTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardCleanupSchedulingSchemaTests.cs:33` | CardCleanupSchedulingSchemaTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardClosedAttentionTests.cs:115` | CardClosedAttentionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardCommentApiTests.cs:20` | CardCommentApiTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardCorrectionApiTests.cs:25` | CardCorrectionApiTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardCorrectionIntegrationTests.cs:1993` | CardCorrectionIntegrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardDiagnosisApplyTests.cs:288` | CardDiagnosisApplyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardDiagnosisSweepTests.cs:197` | CardDiagnosisSweepTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardDoneWorktreeCleanupTests.cs:320` | CardDoneWorktreeCleanupTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardFileBoardLookupEventTests.cs:40` | CardFileBoardLookupTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardFileBoardLookupRepairTests.cs:160` | CardFileBoardLookupTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardFileBoardLookupTests.cs:28` | CardFileBoardLookupTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardFileIgnoreAcceptanceTests.cs:26` | CardFileIgnoreAcceptanceTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardFileIgnoreTests.cs:21` | CardFileIgnoreTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardFilePrivacyBoundaryAcceptanceTests.cs:51` | CardFilePrivacyBoundaryAcceptanceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardFilePrivacyConcurrencyTests.cs:96` | CardFilePrivacyConcurrencyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardFilePrivacyGitTests.cs:24` | CardFilePrivacyGitTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardFilePrivacyHostedServiceTests.cs:35` | CardFilePrivacyHostedServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardFilePrivacyMigrationTests.cs:20` | CardFilePrivacyMigrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardFilePrivacyOwnershipAcceptanceTests.cs:23` | CardFilePrivacyOwnershipAcceptanceTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardFilePrivacyOwnershipTests.cs:22` | CardFilePrivacyOwnershipTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardFilePrivacyPathTests.cs:23` | CardFilePrivacyPathTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardFilePrivacyPersistenceTests.cs:23` | CardFilePrivacyPersistenceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardFilePrivacyRecoveryTests.cs:23` | CardFilePrivacyRecoveryTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardFilePrivacySyncAcceptanceTests.cs:70` | CardFilePrivacySyncAcceptanceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardFilePrivacySyncTests.cs:19` | CardFilePrivacySyncTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardFileSyncEndpointTests.cs:155` | CardFileSyncEndpointTests, CardFileSyncDisabledEndpointTests, CardFileSyncEndpointWebAppFactory, DisabledCardFileSyncEndpointWebAppFactory | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardIdentifierResolutionTests.cs:29` | CardIdentifierResolutionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardListPaginationApiTests.cs:20` | CardListPaginationApiTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardPrivateNotesBoundaryTests.cs:21` | CardPrivateNotesBoundaryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardRelationshipApiTests.cs:606` | CardRelationshipApiTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardRelationshipSchemaTests.cs:22` | CardRelationshipSchemaTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardReorderApiTests.cs:23` | CardReorderApiTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardReorderIntegrationTests.cs:20` | CardReorderIntegrationTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardReviewServiceIntegrationTests.cs:323` | CardReviewServiceIntegrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardSearchApiTests.cs:17` | CardSearchApiTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardServiceTrackerPushTests.cs:28` | CardServiceTrackerPushTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardSpawnModelArgumentTests.cs:28` | CardSpawnModelArgumentTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardTaskFileServiceTests.cs:480` | CardTaskFileServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardTaskSettlementTests.cs:211` | CardTaskSettlementTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardThreadEndpointTests.cs:27` | CardThreadEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardThreadServiceTests.cs:282` | CardThreadServiceTests, ThreadRepoExtensions | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardTrackerPushApiTests.cs:229` | CardTrackerPushApiTests, CardTrackerPushWebAppFactory | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CardVerificationPolicyTests.cs:38` | CardVerificationPolicyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardWorkTransitionServiceTests.cs:301` | CardWorkTransitionServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardWorktreeMirrorBindingTests.cs:25` | CardWorktreeMirrorBindingTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CardWorktreeMirrorCleanupTests.Recovery.cs:281` | CardWorktreeMirrorCleanupTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ChannelBatchingTests.cs:27` | ChannelBatchingTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ChannelBridgeTests.cs:52` | ChannelBridgeTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelConsumerIdentityEndpointTests.cs:52` | ChannelConsumerIdentityEndpointTests, ChannelConsumerIdentityWebAppFactory | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ChannelFollowUpAttachmentTests.cs:34` | ChannelFollowUpAttachmentTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ChannelInboundRecoveryTests.cs:41` | ChannelInboundRecoveryTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelIngressIncidentTests.cs:171` | ChannelIngressIncidentTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelMachineTurnTextTests.cs:28` | ChannelMachineTurnTextTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ChannelOutboundCaptureTests.cs:332` | ChannelOutboundCaptureTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundComposedTransportTests.cs:45` | ChannelOutboundComposedTransportTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundContractTests.cs:77` | ChannelOutboundContractTests | Unit | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundDeadlineMatrixTests.cs:83` | ChannelOutboundDeadlineTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundDeadlineTests.cs:32` | ChannelOutboundDeadlineTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundDeliveryTests.Identity.cs:28` | ChannelOutboundDeliveryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundDeliveryTests.Publication.cs:32` | ChannelOutboundDeliveryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundDeliveryTests.cs:36` | ChannelOutboundDeliveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundDiscoveryTests.cs:516` | ChannelOutboundDiscoveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundDispatchIntegrationTests.cs:29` | ChannelOutboundDispatchIntegrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundDurabilitySchemaTests.cs:22` | ChannelOutboundDurabilitySchemaTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundEndpointTests.cs:162` | ChannelOutboundEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ChannelOutboundFailureRecordingTests.cs:240` | ChannelOutboundFailureRecordingTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundGateTests.cs:262` | ChannelOutboundDeliveryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundMaterializationTests.cs:326` | ChannelOutboundMaterializationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundMetadataRepairTests.cs:226` | ChannelOutboundMetadataRepairTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundMigrationTests.cs:20` | ChannelOutboundMigrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundPolicyTests.cs:30` | ChannelOutboundPolicyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundRecoveryTests.cs:41` | ChannelOutboundRecoveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundRetentionTests.cs:277` | ChannelOutboundRetentionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundRetryPolicyTests.cs:559` | ChannelOutboundRetryPolicyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundSendShapeTests.cs:289` | ChannelOutboundDeliveryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundTrailingRecoveryTests.cs:295` | ChannelOutboundTrailingRecoveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundUnifiedCrashTests.cs:37` | ChannelOutboundUnifiedCrashTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundUnifiedPathTests.cs:189` | ChannelOutboundUnifiedPathTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelOutboundUnifiedTransportTests.cs:40` | ChannelOutboundUnifiedTransportTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelPreamblePresetEndpointTests.cs:11` | ChannelPreamblePresetEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ChannelPromptCorrelationTests.cs:50` | ChannelPromptCorrelationTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ChannelReplyDurabilityTests.cs:43` | ChannelReplyDurabilityTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ChannelReplyLateConfirmDispatchTests.cs:34` | ChannelReplyLateConfirmDispatchTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ChatChannelServiceTests.cs:27` | ChatChannelServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CheckCompactionAttentionTests.cs:22` | CheckCompactionAttentionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CheckCompactionAutomaticRestartTests.cs:24` | CheckCompactionAutomaticRestartTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CheckCompactionContinuationTests.cs:24` | CheckCompactionContinuationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CheckCompactionCrashTests.cs:22` | CheckCompactionCrashTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CheckCompactionReceiptTests.cs:52` | CheckCompactionReceiptTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CheckCompactionRecoveryFlowTests.cs:22` | CheckCompactionRecoveryFlowTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CheckCompactionRecoveryPersistenceTests.cs:21` | CheckCompactionRecoveryPersistenceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CheckInterpreterProvisionerTests.cs:289` | CheckInterpreterProvisionerTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CheckNoteDeliveryHandoffTests.H5.cs:23` | CheckNoteDeliveryHandoffTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CheckNoteDeliveryHandoffTests.Restart.cs:294` | CheckNoteDeliveryHandoffTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CheckNoteDeliveryHandoffTests.cs:97` | CheckNoteDeliveryHandoffTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs:350` | CheckpointSourceApprovalTests | Integration, Slow, C835 | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ClaudeCredentialProbeDispatcherTests.cs:24` | ClaudeCredentialProbeDispatcherTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ClosedCardSweepApiTests.cs:12` | ClosedCardSweepApiTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ClosedCardSweepTests.cs:99` | ClosedCardSweepTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CodexCliObservationGapTests.cs:14` | CodexCliObservationGapTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/CodexCliObservationTests.cs:221` | CodexCliObservationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CodexDelegateDispatchTests.cs:649` | CodexDelegateDispatchTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CodexPhoneHomeCreateTests.cs:35` | CodexPhoneHomeCreateTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CodexStartupDeliveryTests.cs:397` | CodexStartupDeliveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CommitOnSettlePolicyTests.cs:263` | CommitOnSettlePolicyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CommitRecoveryObligationsLoaderTests.cs:20` | CommitRecoveryObligationsLoaderTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/CompactionRecoveryTests.cs:27` | CompactionRecoveryTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ComplexityAttentionTests.cs:25` | ComplexityAttentionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ComplexityChainConcurrencyHttpTests.cs:234` | ComplexityChainConcurrencyHttpTests, ComplexityChainConcurrencyWebAppFactory | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ComplexityChainHttpTests.cs:142` | ComplexityChainHttpTests, ComplexityChainApiWebAppFactory | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ComplexityChainRaceTests.cs:35` | ComplexityChainRaceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ComplexityChainRoleTests.cs:28` | ComplexityChainRoleTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ComplexityChainServiceTests.cs:27` | ComplexityChainServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ComplexityCreateTests.cs:26` | ComplexityCreateTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ComplexityDispatcherTests.cs:29` | ComplexityDispatcherTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ComplexityEffectiveHttpTests.cs:213` | ComplexityEffectiveHttpTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ComplexityRoutingWalkTests.cs:24` | ComplexityRoutingWalkTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ComplexityWallRerouteTests.cs:28` | ComplexityWallRerouteTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ContextCompactionAgentTests.cs:116` | ContextCompactionAgentTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ContextCompactionSweepTests.cs:28` | ContextCompactionSweepTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ContinuationSettlementTests.cs:354` | ContinuationSettlementTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/DataRetentionServiceTests.cs:1691` | DataRetentionServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DecisionCardNotifierTests.cs:127` | DecisionCardNotifierTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DefaultRunnerCreateTests.cs:33` | DefaultRunnerCreateTests, DefaultRunnerKit | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DefaultRunnerEligibilityTests.cs:28` | DefaultRunnerEligibilityTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DefaultRunnerPinTests.cs:63` | DefaultRunnerPinTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DefaultRunnerRerouteTests.cs:36` | DefaultRunnerRerouteTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegateBranchDeliveryTests.cs:39` | DelegateBranchDeliveryTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegateBundleLaunchTests.cs:432` | DelegateBundleLaunchTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegateCheckProbeTests.cs:828` | DelegateCheckProbeTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegateLaunchArgvIntegrityTests.cs:597` | DelegateLaunchArgvIntegrityTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegateScriptWorktreeBaseTests.cs:34` | DelegateScriptWorktreeBaseTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationBriefCeilingPtyTests.cs:277` | DelegationBriefCeilingPtyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationBriefRecoveryTests.cs:26` | DelegationBriefRecoveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationCapabilityTests.cs:89` | DelegationCapabilityTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationCostBackfillTests.cs:180` | DelegationCostBackfillTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.AbsentEvidence.cs:67` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.AbsentLaunch.cs:33` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.AbsentLaunchRepair.cs:26` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.AbsentLaunchWhitelist.cs:26` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.BootStall.cs:50` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.BranchProtections.cs:25` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.BriefDurability.cs:21` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.BriefEnvelope.cs:31` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.BriefIdentity.cs:19` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.BriefInline.cs:40` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.BriefRace.cs:28` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.BriefRelease.cs:24` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.BriefSpill.cs:24` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.DeadSessionFailedReceipt.cs:31` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.DeadSessionGrace.cs:36` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.PreInputRefusal.cs:213` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.ResumeBookkeeping.cs:165` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.RunningRecovery.cs:24` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.Watchdog.cs:29` | DelegationDispatchRecoveryBoundaryTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.cs:57` | DelegationDispatchRecoveryBoundaryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationKindDisplayTests.cs:383` | ModelLevelAliasDisplayTests, DelegationRetryEventKindTests | Unit, Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationScopeHoldTests.cs:293` | DelegationScopeHoldTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DelegationWorktreeTests.cs:345` | DelegationWorktreeTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DiagnoseProvisionerTests.cs:251` | DiagnoseProvisionerTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DiagnosisEndpointTests.cs:24` | DiagnosisEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/DiagnosticsBundleEndpointTests.cs:12` | DiagnosticsBundleEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/DiagnosticsBundleServiceTests.cs:240` | DiagnosticsBundleServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DispatchBaseNotificationTests.Coverage.cs:35` | DispatchBaseNotificationTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DispatchBaseNotificationTests.SiblingWarnings.cs:22` | DispatchBaseNotificationTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DispatchBaseNotificationTests.cs:48` | DispatchBaseNotificationTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DispatchConcurrencyAdmissionTests.cs:43` | DispatchConcurrencyAdmissionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/DispatchConcurrencyDispatchTests.cs:585` | DispatchConcurrencyDispatchTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/DispatchConcurrencyMigrationTests.cs:24` | DispatchConcurrencyMigrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DispatchConcurrencySettingsTests.cs:462` | DispatchConcurrencySettingsTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DispatchConcurrencyWireTests.cs:29` | DispatchConcurrencyWireTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/DispatchHeldAttentionTests.cs:24` | DispatchHeldAttentionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DispatchHoldVisibilityTests.cs:24` | DispatchHoldVisibilityTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DispatcherRemotePrepStarvationTests.cs:37` | DispatcherRemotePrepStarvationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DispatcherSweepLifetimeRegistrationTests.cs:15` | DispatcherSweepLifetimeRegistrationTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/DispatcherSweepLifetimeTests.cs:179` | DispatcherSweepLifetimeTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/DistillationEndpointTests.cs:20` | DistillationEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/DurableRunnerSpillReceiptTests.cs:26` | DurableRunnerSpillReceiptTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/EffectiveSettingsPipelineTests.cs:38` | EffectiveSettingsPipelineTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ExpectationDebtTests.cs:21` | ExpectationDebtTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ExpectationDirectDeliveryTests.cs:26` | ExpectationDirectDeliveryTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ExpectationEpisodeTests.cs:24` | ExpectationEpisodeTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ExpectationEscalationTests.cs:191` | ExpectationEscalationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ExpectationLedgerTests.cs:31` | ExpectationLedgerTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ExpectationNoteDebtTests.cs:18` | ExpectationNoteDebtTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ExpectationObservationAdapterTests.cs:28` | ExpectationObservationAdapterTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ExpectationReceiptTests.cs:21` | ExpectationReceiptTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ExpectationResponseTests.cs:20` | ExpectationResponseTests, ExpectationResponseTestExtensions | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ExpectationSchedulingTests.cs:22` | ExpectationSchedulingTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ExpectationSnapshotTests.cs:21` | ExpectationSnapshotTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ExpectationWatchdogScenarioTests.cs:25` | ExpectationWatchdogScenarioTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ExternalTrackerSyncIdentifierTests.cs:218` | ExternalTrackerSyncIdentifierTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ExternalTrackerSyncLandingColumnTests.cs:186` | ExternalTrackerSyncLandingColumnTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/FileSystemBrowseApiTests.cs:19` | FileSystemBrowseApiTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/GrokCredentialProbeDispatcherTests.cs:28` | GrokCredentialProbeDispatcherTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/GrokDelegateDispatchTests.cs:850` | GrokDelegateDispatchTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/GrokDelegateEndToEndTests.cs:972` | GrokDelegateEndToEndTests, RecordingRunnerClient, DelegateTaskApiRelay | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/GrokRulesChannelTests.cs:24` | GrokRulesChannelTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/GrokRulesCompactionAcceptanceTests.cs:70` | GrokRulesCompactionAcceptanceTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/GrokRulesCompactionRecoveryTests.cs:50` | GrokRulesCompactionRecoveryTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/GrokRulesCompositionTests.cs:66` | GrokRulesCompositionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/GrokRulesDispatchAcceptanceTests.cs:215` | GrokRulesDispatchAcceptanceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/GrokRulesHerdrAcceptanceTests.cs:45` | GrokRulesHerdrAcceptanceTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/GrokRulesInitializationTests.cs:203` | GrokRulesInitializationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/GrokRulesLaunchRefusalTests.cs:212` | GrokRulesLaunchRefusalTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/GrokRulesLiveMappedDispatchTests.cs:171` | GrokRulesLiveMappedDispatchTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/GrokRulesQueueBarrierTests.cs:47` | GrokRulesQueueBarrierTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/GrokRulesReadyOrderingTests.cs:29` | GrokRulesReadyOrderingTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/GrokRulesReceiptTests.cs:39` | GrokRulesReceiptTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/GrokRulesResumeMigrationTests.cs:32` | GrokRulesResumeMigrationTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/GrokRulesTransactionTests.cs:56` | GrokRulesTransactionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/GrokSignInIncidentTests.cs:23` | GrokSignInIncidentTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/GrokSignInRuntimeTests.cs:175` | GrokSignInRuntimeTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/GrokStartupReadyOrderingTests.cs:30` | GrokStartupReadyOrderingTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HealthEndpointTests.cs:14` | HealthEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.cs:637` | HerdrAlwaysOnChannelParityTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HerdrLabelFollowConcurrencyTests.cs:112` | HerdrLabelFollowConcurrencyTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HerdrLabelFollowFlowTests.cs:22` | HerdrLabelFollowFlowTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/HerdrLabelFollowTests.cs:20` | HerdrLabelFollowTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/HerdrLabelFollowWireTests.cs:18` | HerdrLabelFollowWireTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/HerdrLaunchContextResolverTests.cs:68` | HerdrLaunchContextTitleTests, HerdrLaunchContextResolverTests | Unit, Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HerdrPaneDisposalApplicationTests.cs:27` | HerdrPaneDisposalApplicationTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/HerdrPlacementPreflightTests.cs:181` | HerdrPlacementPreflightTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HerdrPlacementSettingsTests.cs:25` | HerdrPlacementSettingsTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HerdrSupervisionAttentionTests.cs:41` | HerdrSupervisionAttentionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HerdrSupervisionBackoffTests.cs:644` | HerdrSupervisionBackoffTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HerdrSupervisionFailureEvidenceTests.cs:68` | HerdrSupervisionFailureEvidenceTests | Unit | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HomeTaskServiceIntegrationTests.cs:385` | HomeTaskServiceIntegrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HostBudgetAdmissionTests.cs:22` | DispatchHoldVisibilityTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HostBudgetPipelineTests.cs:17` | AgentTaskPipelineStatusTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HostBudgetServiceTests.cs:19` | HostBudgetServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HostCleanupMaintenanceTests.cs:24` | HostCleanupMaintenanceTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/HostCleanupOrchestrationTests.cs:19` | HostCleanupOrchestrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HostCleanupReportTests.cs:11` | HostCleanupReportTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/HostEndpointTests.cs:25` | HostEndpointTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HostOccupancySampleEndpointTests.cs:31` | HostOccupancySampleEndpointTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HostStatsEndpointTests.cs:59` | HostStatsEndpointTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/HostStatsPollServiceTests.cs:28` | HostStatsPollServiceTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/IncidentPageNotifierTests.cs:207` | IncidentPageNotifierTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/InterimVerificationLandGitTests.cs:29` | InterimVerificationLandGitTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/InterimVerificationLandGuardTests.cs:37` | InterimVerificationLandGuardTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/InterimVerificationPolicyTests.cs:417` | InterimVerificationPolicyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/LandEvidenceWarningDeliveryTests.cs:72` | LandEvidenceWarningDeliveryTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/LandRefusalDeliveryWitnessTests.cs:30` | LandRefusalDeliveryWitnessTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:151` | LandRequestWriteDiagnosticTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/LandingProtocolGuardTests.cs:43` | LandingProtocolGuardTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/LandingProtocolHarnessTests.cs:41` | LandingProtocolHarnessTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/LegacyCheckNotePublicationTests.H5.cs:61` | LegacyCheckNotePublicationTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/LegacyCheckNotePublicationTests.cs:19` | LegacyCheckNotePublicationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/MatrixDefaultComplexityCreateTests.cs:304` | MatrixDefaultComplexityCreateTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/MatrixDefaultComplexityRoutingTests.cs:36` | MatrixDefaultComplexityRoutingTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ModelAvailabilityCreateTests.cs:29` | ModelAvailabilityCreateTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ModelAvailabilityDispatcherTests.cs:28` | ModelAvailabilityDispatcherTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ModelAvailabilityHttpTests.cs:263` | ModelAvailabilityHttpTests, ModelAvailabilityApiWebAppFactory | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ModelAvailabilityManualTests.cs:329` | ModelAvailabilityManualTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ModelAvailabilityTests.cs:30` | ModelAvailabilityTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/MultiRunnerDirectoryTests.cs:32` | MultiRunnerDirectoryTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/MultiRunnerProjectionTests.cs:30` | MultiRunnerProjectionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/MultiRunnerRecoveryTests.cs:76` | MultiRunnerRecoveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/MutationAdmissionTests.cs:23` | MutationAdmissionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/MutationDispatchTests.cs:186` | MutationDispatchTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/MutationPipelineTests.cs:201` | MutationPipelineTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/NamedCodexAgentLaunchTests.cs:98` | NamedCodexAgentLaunchTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/OrchestratorInstructionsRefreshTests.cs:819` | OrchestratorInstructionsRefreshTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/OrchestratorInvestigationSweepTests.cs:93` | OrchestratorInvestigationSweepTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/OrchestratorServiceIntegrationTests.cs:1343` | OrchestratorServiceIntegrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/OrchestratorStateProjectionTests.cs:407` | OrchestratorStateProjectionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/OrchestratorTrackerCadenceTests.cs:130` | OrchestratorTrackerCadenceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/OrchestratorWorkspaceWarningTests.cs:182` | OrchestratorWorkspaceWarningTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/OutboundConversionManifestTestsPump.cs:26` | OutboundConversionManifestTestsPump | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/OutboundConversionTaskTests.cs:38` | OutboundConversionTaskTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/OutputDistillationAdmissionTests.cs:24` | OutputDistillationAdmissionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/OutputDistillationApplyRaceTests.cs:24` | OutputDistillationApplyRaceTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/OutputDistillationCleanupTests.cs:25` | OutputDistillationCleanupTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/OutputDistillationDeadlineTests.cs:21` | OutputDistillationDeadlineTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/OutputDistillationDeliveryTests.cs:44` | OutputDistillationDeliveryTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/OutputDistillationDispatchTests.cs:28` | OutputDistillationDispatchTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/OutputDistillationMigrationTests.cs:20` | OutputDistillationMigrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/OutputDistillationProducerTests.cs:49` | OutputDistillationProducerTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/OutputDistillationTests.cs:28` | OutputDistillationTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/OutputDistillerProvisionerTests.cs:214` | OutputDistillerProvisionerTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ParkedMessageSweepServiceTests.cs:252` | ParkedMessageSweepServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PendingSettlementDeliveryTests.cs:43` | PendingSettlementDeliveryTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PhoneHomeCompletedCardTransportTests.cs:33` | PhoneHomeCompletedCardTransportTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PhoneHomeDeferredKillTests.cs:35` | PhoneHomeDeferredKillTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeEventPumpTests.cs:28` | PhoneHomeEventPumpTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeImmediateSendTests.cs:351` | PhoneHomeImmediateSendTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeLaunchTransportTests.cs:332` | PhoneHomeLaunchTransportTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeMirrorJoinTests.cs:545` | PhoneHomeMirrorJoinTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeOutageMentionTests.cs:24` | PhoneHomeOutageMentionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PhoneHomePendingInventoryTests.cs:26` | PhoneHomePendingInventoryTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PhoneHomeQueuedTurnTests.cs:30` | PhoneHomeQueuedTurnTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeReconciliationTests.cs:23` | PhoneHomeReconciliationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeRecoveryEligibilityTests.cs:94` | PhoneHomeRecoveryEligibilityTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeRestartResumeTests.cs:90` | PhoneHomeRestartResumeTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerDrainTests.cs:32` | PhoneHomeRollingRunnerTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerTests.cs:345` | PhoneHomeRollingRunnerTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeRunnerRetirementCycleTests.cs:23` | PhoneHomeRunnerRetirementCycleTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeSchedulePreviewTests.cs:16` | PhoneHomeSchedulePreviewTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PhoneHomeSessionRoutingTests.cs:22` | PhoneHomeSessionRoutingTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeSpillTransportTests.cs:34` | PhoneHomeSpillTransportTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PhoneHomeStandingLaunchTests.cs:30` | PhoneHomeStandingLaunchTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeStrandedQueueTests.cs:38` | PhoneHomeStrandedQueueTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeTaskCreateTests.cs:24` | PhoneHomeTaskCreateTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeTaskDispatchProjectionTests.TaskToken.cs:26` | PhoneHomeTaskDispatchProjectionTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeTaskDispatchProjectionTests.cs:37` | PhoneHomeTaskDispatchProjectionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PhoneHomeTranscriptIncidentTests.cs:302` | PhoneHomeTranscriptIncidentTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PinnedAgentKindTests.cs:248` | PinnedAgentKindTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PinnedCodexProfileDispatchLaunchTests.cs:65` | PinnedCodexProfileDispatchLaunchTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PinnedProfileLaunchSpecTests.cs:31` | PinnedProfileLaunchSpecTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PipelineDefinitionEndpointTests.cs:16` | PipelineDefinitionEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PipelineDefinitionServiceTests.cs:31` | PipelineDefinitionServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PipelineHandoffPilotTests.cs:203` | PipelineHandoffPilotTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PipelineResolutionTests.cs:20` | PipelineResolutionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.Admission.cs:35` | PipelineSpeedIntegrationTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.Http.cs:293` | PipelineSpeedIntegrationTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.Wiring.cs:20` | PipelineSpeedIntegrationTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.cs:31` | PipelineSpeedIntegrationTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PolicyRefreshEndpointTests.cs:16` | PolicyRefreshEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PolicyRefreshServiceTests.cs:93` | PolicyRefreshServiceTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PolledCompletionNoteShrinkTests.cs:20` | PolledCompletionNoteShrinkTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PostLandMutationAdmissionTests.cs:30` | PostLandMutationAdmissionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PostLandMutationCustodyTests.cs:758` | PostLandMutationCustodyTests, PostLandMutationWorld | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PostLandMutationDeliveryTests.CompletionRecovery.cs:234` | PostLandMutationDeliveryTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/PostLandMutationDeliveryTests.cs:685` | PostLandMutationDeliveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PostLandMutationWorktreeTests.cs:477` | PostLandMutationWorktreeTests, MutationDispatchTestsCapture, ZeroProgressProbe | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ProductionRunnerIsolationTests.cs:26` | ProductionRunnerIsolationTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ProjectCascadeForeignKeyTests.ExpectationHolds.cs:41` | ProjectCascadeForeignKeyTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ProjectCascadeForeignKeyTests.HoldRaces.cs:23` | ProjectCascadeForeignKeyTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ProjectCascadeForeignKeyTests.cs:27` | ProjectCascadeForeignKeyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ProjectDeletionTests.cs:612` | ProjectDeletionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ProjectReadinessBatchTests.cs:82` | ProjectReadinessBatchTests | GitIntegration, Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ProjectReadinessTests.cs:679` | ProjectReadinessTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ProjectServiceTests.cs:57` | ProjectServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ProjectSetupServiceTests.cs:54` | ProjectSetupServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ProjectWorkerWorkspaceDefaultTests.cs:28` | ProjectWorkerWorkspaceDefaultTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ProviderQuotaRefusalAcceptanceTests.cs:35` | ProviderQuotaRefusalAcceptanceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ProviderSignInRequiredCreateTests.cs:121` | ProviderSignInRequiredCreateTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/PublishedCardCompletionTests.cs:247` | PublishedCardCompletionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/QueuedInputWatchdogTests.cs:153` | QueuedInputWatchdogTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/QuotaEvidenceWireTests.cs:37` | QuotaEvidenceWireTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ReadyGateFailureNoticeDeliveryTests.cs:373` | ReadyGateFailureNoticeDeliveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ReceiptFailureDeliveryTests.cs:66` | ReceiptFailureDeliveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ReceiptScanDestinationIntegrationTests.cs:9` | ReceiptScanDestinationIntegrationTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RemoteCompletionSpillTests.cs:35` | RemoteCompletionSpillTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RemoteControlMaintenanceQueueTests.cs:31` | RemoteControlMaintenanceQueueTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RemoteControlModalAttentionTests.cs:18` | RemoteControlModalAttentionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RemoteControlModalPersistenceTests.cs:24` | RemoteControlModalPersistenceTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RemoteControlModalWatchTests.cs:23` | RemoteControlModalWatchTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RemoteControlRecoveryPtyIntegrationTests.cs:124` | RemoteControlRecoveryPtyIntegrationTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RemoteControlRecoveryTests.cs:31` | RemoteControlRecoveryTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RemotePoolFollowUpAdmissionTests.cs:24` | RemotePoolFollowUpAdmissionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RemoteWorkspacePreparerTests.cs:844` | RemoteWorkspacePreparerTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RepairSourceDispatchTests.cs:893` | RepairSourceDispatchTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RepairSourceLandRefusalTests.cs:28` | RepairSourceLandRefusalTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RepairSourceRecoveryLandingTests.cs:29` | RepairSourceRecoveryLandingTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RepairSourceSettlementTests.cs:29` | RepairSourceSettlementTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RepositoryLeaseWaitersWiringTests.cs:21` | RepositoryLeaseWaitersWiringTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RequiredSingleCandidateRefusalTests.cs:159` | RequiredSingleCandidateRefusalTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ResponseCompressionIntegrationTests.cs:16` | ResponseCompressionIntegrationTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ReviewEvidenceConsistencyTests.cs:31` | ReviewEvidenceConsistencyTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ReviewEvidenceDeliveryTests.cs:436` | ReviewEvidenceDeliveryTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ReviewEvidenceRebindingTests.cs:345` | ReviewEvidenceRebindingTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ReviewEvidenceRecoveryEndpointTests.cs:28` | ReviewEvidenceRecoveryEndpointTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ReviewEvidenceRecoveryTests.cs:28` | ReviewEvidenceRecoveryTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ReviewEvidenceResettlementTests.cs:30` | ReviewEvidenceResettlementTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ReviewEvidenceSettlementTests.cs:42` | ReviewEvidenceSettlementTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ReviewLoopTests.cs:305` | ReviewLoopTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ReviewReplyDispatcherTests.cs:196` | ReviewReplyDispatcherTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RolePolicyInvalidValuesCreateTests.cs:86` | RolePolicyInvalidValuesCreateTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RolePolicyToolBundleDispatchTests.cs:53` | RolePolicyToolBundleDispatchTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RoutingExhaustedPlacementTests.cs:442` | RoutingExhaustedPlacementTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RoutingPinCandidateCreateTests.cs:26` | RoutingPinCandidateCreateTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RoutingPinCandidateDispatchTests.cs:27` | RoutingPinCandidateDispatchTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RoutingPinCandidateTests.cs:28` | RoutingPinCandidateTests, RoutingCandidateParseTests | Integration, Unit | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RoutingPinCreateTests.cs:30` | RoutingPinCreateTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RoutingPinDispatcherTests.cs:29` | RoutingPinDispatcherTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RoutingPinServiceTests.cs:29` | RoutingPinServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunAttemptStallDetectorTests.cs:66` | RunAttemptStallDetectorTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerAlarmAttentionTests.cs:25` | RunnerAlarmAttentionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerAlarmCoordinatorTests.cs:27` | RunnerAlarmCoordinatorTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerAlarmDeliveryTests.cs:255` | RunnerAlarmDeliveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerAlarmHostedServiceTests.cs:44` | RunnerAlarmHostedServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerAlarmNotifierTests.cs:29` | RunnerAlarmNotifierTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerAlarmWiringTests.cs:20` | RunnerAlarmWiringTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RunnerCapacityEndpointTests.cs:29` | RunnerCapacityEndpointTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerCatalogueTests.cs:72` | RunnerCatalogueTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerClaudeFinalLaunchArgvTests.cs:32` | RunnerClaudeFinalLaunchArgvTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerCodexCliEvidenceTests.cs:44` | RunnerCodexCliEvidenceTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RunnerDefaultTests.cs:43` | RunnerDefaultsWireTests, OutboundConversionPublicCreateTests, RunnerDefaultSettingsTests, RunnerDefaultPlacementTests, RunnerDefaultMigrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerEligibilityObserverTests.cs:25` | RunnerEligibilityObserverTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RunnerGrokCredentialProbeDispatcherTests.cs:31` | RunnerGrokCredentialProbeDispatcherTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerRetireJobTests.cs:136` | RunnerRetireJobTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerSeatLiveSeatWarmupTests.cs:4` | RunnerSeatLiveSeatWarmupTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RunnerSeatOrphanSweepTests.cs:26` | RunnerSeatOrphanSweepTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/RunnerSettlementSyncTests.cs:1039` | RunnerSettlementSyncTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerSlotEndpointTests.cs:33` | RunnerSlotEndpointTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/RunnerTaskSettlementTests.cs:29` | RunnerTaskSettlementTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ScheduleCardActionTests.cs:261` | ScheduleCardActionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/ScheduleEndpointsTests.cs:18` | ScheduleEndpointsTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/ScheduleSweepTests.cs:375` | ScheduleSweepTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SeatDesktopJoinTests.cs:21` | SeatDesktopJoinTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SeatOccupancyAttentionTests.cs:25` | SeatOccupancyAttentionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SeatOccupancySamplerTests.cs:542` | SeatOccupancySamplerTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionContextUsagePersistenceTests.cs:30` | SessionContextUsagePersistenceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionDeliveryProfileTests.cs:26` | SessionDeliveryProfileTests | Unit | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionFinishedDuplicateTests.cs:34` | SessionFinishedDuplicateTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SessionGenerationDeliveryOverlapTests.cs:23` | SessionGenerationDeliveryOverlapTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SessionGenerationExitTests.cs:129` | SessionGenerationExitTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionHealthTests.cs:376` | SessionHealthTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionMessageQueueBootWedgeTests.cs:31` | SessionMessageQueueBootWedgeTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SessionMessageQueueDeliveredSpillRecoveryTests.cs:23` | SessionMessageQueueDeliveredSpillRecoveryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionMessageQueueDeliveredSpillTests.cs:21` | SessionMessageQueueDeliveredSpillTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionMessageQueueDeliveryVerificationTests.C561.cs:207` | SessionMessageQueueDeliveryVerificationTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SessionMessageQueueDeliveryVerificationTests.cs:41` | SessionMessageQueueDeliveryVerificationTests | Integration, Slow, Card0355 | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SessionMessageQueueGrokPtyIntegrationTests.cs:36` | SessionMessageQueueGrokPtyIntegrationTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionMessageQueueInterruptedAttemptTests.cs:25` | SessionMessageQueueInterruptedAttemptTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SessionMessageQueuePhoneHomeDropTests.cs:30` | SessionMessageQueuePhoneHomeDropTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs:67` | SessionMessageQueuePtyIntegrationTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionMessageQueueServiceTests.cs:227` | SessionMessageQueueServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionMessageQueueSpillTests.cs:26` | SessionMessageQueueSpillTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SessionMessageQueueSupervisionTests.cs:24` | SessionMessageQueueSupervisionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SessionMessageQueueWedgedHeadTests.cs:24` | SessionMessageQueueWedgedHeadTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SessionQueueReceiptPlumbingTests.cs:176` | SessionQueueReceiptPlumbingTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionReconciliationServiceTests.cs:1482` | SessionReconciliationServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionRunnerEventPumpTests.cs:39` | SessionRunnerEventPumpTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionRuntimeActionDeferralTests.cs:30` | SessionRuntimeActionDeferralTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionRuntimeActionRecoveryTests.cs:32` | SessionRuntimeActionRecoveryTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionRuntimeActionRoutingTests.Finish.cs:893` | SessionRuntimeActionRoutingTests | (none on this file) | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionRuntimeActionRoutingTests.cs:45` | SessionRuntimeActionRoutingTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionRuntimeActionTests.cs:327` | SessionRuntimeActionTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionStateCacheReadTests.cs:63` | SessionStateCacheReadTests, SessionStateReadFixture | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionStateCommitTests.cs:242` | SessionStateCommitTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SessionStateProjectionTests.cs:27` | SessionStateProjectionTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SessionStateWarmupTests.cs:26` | SessionStateWarmupTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SessionTerminationSourcePersistenceTests.cs:39` | SessionTerminationSourcePersistenceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SettlementSyncDebtLandingTests.cs:25` | SettlementSyncDebtLandingTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SettlementSyncDebtSchemaTests.cs:27` | SettlementSyncDebtSchemaTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SettlementSyncRecoveryTests.cs:44` | SettlementSyncRecoveryTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SpecialistExecutionIdentityTests.cs:348` | SpecialistExecutionIdentityTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SpecialistHealthAttentionTests.cs:104` | StandingSpecialistHealthPolicyTests, SpecialistHealthAttentionTests | Unit, Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SpecialistInputTransportTests.cs:81` | SpecialistInputTransportTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SpecialistPublicationTests.cs:35` | SpecialistPublicationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SpecialistQualificationTests.cs:91` | SpecialistQualificationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SpecialistStartIntentTests.cs:39` | SpecialistStartIntentTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SpecialistTaskRunnerDeadlineTests.cs:24` | SpecialistTaskRunnerDeadlineTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SpecialistToolPolicyLaunchTests.cs:34` | SpecialistToolPolicyLaunchTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SpecialistToolPolicyTests.cs:21` | SpecialistToolPolicyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/StageOutcomeBackfillTests.cs:26` | StageOutcomeBackfillTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/StageOutcomeFindingEndpointTests.cs:23` | StageOutcomeFindingEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/StageOutcomeSummaryTests.cs:23` | StageOutcomeSummaryTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/StaleWorkingTranscriptWatchdogTests.cs:640` | StaleWorkingTranscriptWatchdogTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/StandingBootAttentionTests.cs:646` | StandingBootAttentionTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/StandingBootQueueWitnessTests.cs:64` | StandingBootQueueWitnessTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/StandingBootStatementBudgetTests.cs:156` | StandingBootStatementBudgetTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/StandingBootWatchdogTests.cs:52` | StandingBootWatchdogTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/StandingContinuityAttentionTests.cs:25` | StandingContinuityAttentionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/StandingContinuityRecoveryTests.cs:27` | StandingContinuityRecoveryTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/StandingSessionOwnershipTests.cs:28` | StandingSessionOwnershipTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/StandingSessionQueueSwitchTests.cs:25` | StandingSessionQueueSwitchTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/StandingSessionRecoveryHttpTests.cs:110` | StandingSessionRecoveryHttpTests, StandingRecoveryWebAppFactory | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/StandingSessionSelectionTests.cs:23` | StandingSessionSelectionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/StandingSessionSwitchConcurrencyTests.CommitGap.cs:24` | StandingSessionSwitchConcurrencyTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/StandingSessionSwitchConcurrencyTests.Delivery.cs:27` | StandingSessionSwitchConcurrencyTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/StandingSessionSwitchConcurrencyTests.Generation.cs:33` | StandingSessionSwitchConcurrencyTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/StandingSessionSwitchConcurrencyTests.Stop.cs:29` | StandingSessionSwitchConcurrencyTests | (none on this file) | shared DB helper reference |
| `tests/Antiphon.Tests/Application/StandingSessionSwitchConcurrencyTests.cs:25` | StandingSessionSwitchConcurrencyTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/StandingSpecialistRoutingHttpTests.cs:200` | StandingSpecialistRoutingHttpTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/StandingSpecialistRoutingMigrationTests.cs:20` | StandingSpecialistRoutingMigrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/StandingSpecialistSeatTests.cs:27` | StandingSpecialistSeatTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/StartRefRepairAdoptionTests.cs:123` | StartRefRepairAdoptionTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SubscriptionQuotaGateTests.cs:195` | SubscriptionQuotaGateTests, SubscriptionQuotaGateDispatchTests | Unit, Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/SubscriptionUsageHttpTests.cs:303` | SubscriptionUsageHttpTests, SubscriptionUsageApiWebAppFactory | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/SubscriptionUsageMonitorTests.cs:31` | SubscriptionUsageMonitorTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/TaskDeadlinePolicyTests.cs:646` | TaskDeadlinePolicyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TaskInputAttentionCostTests.cs:538` | TaskInputAttentionCostTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TaskInputReadFailureTests.cs:228` | TaskInputReadFailureTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TaskInputWeakArmBoundaryTests.cs:229` | TaskInputWeakArmBoundaryTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TaskParkPublicationTests.cs:200` | TaskParkPublicationTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/TaskPlatformDispatchTests.cs:36` | TaskPlatformDispatchTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TaskPlatformPersistenceTests.cs:25` | TaskPlatformPersistenceTests, CardPlatformTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TaskPlatformPlacementTests.cs:27` | TaskPlatformPlacementTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TaskPlatformWireTests.cs:31` | TaskPlatformWireTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TaskProgressPolicyFileArmTests.cs:103` | TaskProgressPolicyFileArmTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TaskProgressPolicyTests.cs:124` | TaskProgressPolicyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TaskProgressStallSweepTests.cs:395` | TaskProgressStallSweepTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TaskWorktreeRetirementTests.cs:534` | TaskWorktreeRetirementTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs:45` | TerminalRunnerSeatReleaseTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/TrackerBidirectionalSyncTests.cs:1581` | TrackerBidirectionalSyncTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TrackerCardStatePushServiceTests.cs:565` | TrackerCardStatePushServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TrackerSyncEndpointTests.cs:370` | TrackerSyncEndpointTests, TrackerSyncEndpointWebAppFactory, RecordingMessagingProducer, GatingBidirectionalTracker | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/TrackerSyncNotifierTests.cs:286` | TrackerSyncNotifierTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TrackerTokenResolverTests.cs:135` | TrackerTokenResolverTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TranscriptBindingIncidentTests.cs:35` | TranscriptBindingIncidentTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/TranscriptHotPathMigrationTests.cs:267` | TranscriptHotPathMigrationTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TranscriptHotPathQueryTests.cs:17` | TranscriptHotPathQueryTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/TranscriptPromptSpanTests.cs:269` | TranscriptPromptSpanTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/TranscriptWorkingStateQueryTests.cs:16` | TranscriptWorkingStateQueryTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/VerificationRoundDeliveryTests.cs:156` | VerificationRoundDeliveryTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/VerificationRoundDispatchTests.cs:28` | VerificationRoundDispatchTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/VerificationRoundSettlementTests.cs:135` | VerificationRoundSettlementTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WallRerouteDispatchTests.cs:31` | WallRerouteDispatchTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WatchdogServiceTests.cs:708` | WatchdogServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WindowsGrokRoutingPolicyTests.cs:47` | WindowsGrokRoutingPolicyTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorkerWorkspaceDefaultMigrationTests.cs:20` | WorkerWorkspaceDefaultMigrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorkerWorkspaceDefaultStorageTests.cs:42` | WorkerWorkspaceDefaultStorageTests | Unit | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorkflowDefinitionLoaderTests.cs:411` | WorkflowDefinitionLoaderTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorkflowEngineTests.cs:19` | WorkflowEngineTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorkflowTrackerActivationTests.cs:337` | WorkflowTrackerActivationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorkingTranscriptRecoveryTests.cs:570` | WorkingTranscriptRecoveryTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorktreeBaseSelectionTests.cs:129` | WorktreeBaseSelectionTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorktreeCleanupJournalTests.cs:223` | WorktreeCleanupJournalTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorktreeDefaultAdmissionTests.cs:31` | WorktreeDefaultAdmissionTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Application/WorktreeDefaultCallerTests.cs:37` | WorktreeDefaultCallerTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorktreeDefaultContinuationTests.cs:29` | WorktreeDefaultContinuationTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorktreeHealthServiceTests.cs:29` | WorktreeHealthServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorktreeLandingCleanupRetryTests.cs:365` | WorktreeLandingCleanupRetryTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorktreeResidueEndpointTests.cs:10` | WorktreeResidueEndpointTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/WorktreeResidueRecoveryTests.cs:166` | WorktreeResidueRecoveryTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Application/WorktreeResidueRegistrationTests.cs:57` | WorktreeResidueRegistrationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorktreeResidueSweepTests.cs:40` | WorktreeResidueSweepTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorktreeRetirementRaceTests.cs:999` | WorktreeRetirementRaceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Application/WorktreeStartRefDispatchTests.cs:29` | WorktreeStartRefDispatchTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Infrastructure/AppDbContextModelTests.cs:59` | AppDbContextModelTests | Unit | direct store/container symbol |
| `tests/Antiphon.Tests/Infrastructure/DatabaseSeederTests.cs:24` | DatabaseSeederTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Infrastructure/DockerDeliveryDatabaseTests.cs:196` | DockerDeliveryDatabaseTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Infrastructure/ExpectationWatchdogJobTests.cs:185` | ExpectationWatchdogJobTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Infrastructure/HangfireStartupSafetyTests.cs:30` | HangfireStartupSafetyTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Infrastructure/KanbanPersistenceTests.cs:324` | KanbanPersistenceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Infrastructure/LandingSourceFreshnessTests.cs:271` | LandingSourceFreshnessTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Infrastructure/OrchestratorInstructionsMigrationShapeTests.cs:41` | OrchestratorInstructionsMigrationShapeTests | Unit | direct store/container symbol |
| `tests/Antiphon.Tests/Infrastructure/PostgresPooledStateTests.cs:14` | PostgresPooledStateTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseOwnerTests.cs:281` | RepositoryMutationLeaseOwnerTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseTests.cs:315` | RepositoryMutationLeaseTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Infrastructure/Resilience/DatabaseReadResilienceTests.cs:28` | DatabaseReadResilienceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Infrastructure/Resilience/HttpResilienceRegistrationTests.cs:182` | HttpResilienceRegistrationTests | Unit | direct store/container symbol |
| `tests/Antiphon.Tests/Infrastructure/Resilience/ResilienceCircuitTests.cs:167` | ResilienceCircuitTests | Unit | direct store/container symbol |
| `tests/Antiphon.Tests/Infrastructure/WorktreeGuardedCleanupTests.cs:566` | WorktreeGuardedCleanupTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Infrastructure/WorktreeRemovalAuthorityTests.cs:52` | WorktreeRemovalAuthorityTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/Infrastructure/ZombieCensusServiceTests.cs:464` | ZombieCensusServiceTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/Migrations/CommitOnSettleMigrationTests.cs:45` | CommitOnSettleMigrationTests | Unit | direct store/container symbol |
| `tests/Antiphon.Tests/Migrations/PipelineDefinitionMigrationTests.cs:92` | PipelineDefinitionMigrationShapeTests, PipelineDefinitionMigrationTests | Unit, Integration | direct store/container symbol |
| `tests/Antiphon.Tests/ProgramStartupConcurrencyTests.cs:35` | ProgramStartupConcurrencyTests | Integration, Slow | shared DB helper reference |
| `tests/Antiphon.Tests/Scripts/MeasureSessionStateCacheScriptTests.cs:231` | MeasureSessionStateCacheScriptTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/SmokeTests.cs:27` | SmokeTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/TestHelpers/EntityScalarSnapshotConsumerGuardTests.cs:82` | EntityScalarSnapshotConsumerGuardTests | Unit | direct store/container symbol |
| `tests/Antiphon.Tests/TestHelpers/EntityScalarSnapshotTests.cs:171` | EntityScalarSnapshotTests | Unit | direct store/container symbol |
| `tests/Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs:112` | ProductionRunnerGuard, RefusingSessionRunnerClient, RefusingZombieProcessCensus, ProductionRunnerGuardTests | Integration | shared DB helper reference |
| `tests/Antiphon.Tests/TestHelpers/TestDbFixtureIsolationTests.cs:21` | TestDbFixtureIsolationTests | Integration | direct store/container symbol |
| `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLazyInitializationTests.cs:19` | TestDbFixtureLazyInitializationTests | Integration, Slow | direct store/container symbol |
| `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycleTests.cs:243` | ControlledTestDbOperations, TestDbFixtureLifecycleTests | Unit | direct store/container symbol |

## Appendix B. OS-sensitive source inventory

The table records test-bearing files with explicit Windows OS checks (OperatingSystem.IsWindows or RuntimeInformation/OSPlatform.Windows). **It is not a list of wholly Windows-only classes**: some rows assert a portable policy using synthetic inputs; others select different backend behavior. It excludes comments-only Windows mentions. Windows-required provider fixtures and installed Herdr gates without explicit OS checks are separately captured in section 3. This avoids pretending that a filename or grep establishes execution on an OS. Shared native source dependencies are the Pty/Runner/PtyHost stack; main filesystem/custody tests additionally depend on HostCleanup and Checkpoints as their project graph shows.

| OS-check source | Top-level class declarations | Local categories |
| --- | --- | --- |
| `tests/Antiphon.Agents.Pty.Tests/C1022TypedInputTests.cs:43` | C1022TypedInputTests | Integration |
| `tests/Antiphon.Agents.Pty.Tests/ClaudeDetectorsTests.cs:13` | ClaudeDetectorsTests | Pty |
| `tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractTests.cs:67` | ClaudeSubmitContractTests, ClaudeSubmitContractHarness | Pty |
| `tests/Antiphon.Agents.Pty.Tests/ClaudeVerifiedDeliveryTests.cs:31` | ClaudeVerifiedDeliveryTests | Pty, Headed, OptIn |
| `tests/Antiphon.Agents.Pty.Tests/CommandLineLengthTests.cs:23` | CommandLineLengthTests | Unit |
| `tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs:28` | FakeClaudeContractTests | Pty |
| `tests/Antiphon.Agents.Pty.Tests/FakeGrokContractTests.cs:21` | FakeGrokContractTests | Pty, Unit |
| `tests/Antiphon.Agents.Pty.Tests/GrokNativeSessionStoreTests.cs:30` | GrokNativeSessionStoreTests | Unit |
| `tests/Antiphon.Agents.Pty.Tests/LaunchArgvGuardTests.cs:35` | LaunchArgvGuardTests | (none on this file) |
| `tests/Antiphon.Agents.Pty.Tests/ModernConPtyCommandLineTests.cs:29` | ModernConPtyCommandLineTests | (none on this file) |
| `tests/Antiphon.Agents.Pty.Tests/ModernPtyDa1Tests.cs:47` | ModernPtyDa1Tests | Pty |
| `tests/Antiphon.Agents.Pty.Tests/PtyAgentRunnerTests.cs:16` | PtyAgentRunnerTests | Pty |
| `tests/Antiphon.Agents.Pty.Tests/PtyBackendContractTests.cs:21` | PtyBackendContractTests | Pty |
| `tests/Antiphon.Agents.Pty.Tests/PtyBackendEnvGuard.cs:45` | PtyBackendEnvGuard, PtyBackendEnvGuardTests | Pty |
| `tests/Antiphon.Agents.Pty.Tests/PtyBracketedPasteContractTests.cs:38` | PtyBracketedPasteContractTests | Pty |
| `tests/Antiphon.Agents.Pty.Tests/PtyCustodyTests.cs:537` | PtyCustodyTests | Pty |
| `tests/Antiphon.Agents.Pty.Tests/PtyInputChunkingTests.cs:31` | PtyInputChunkingTests | Pty |
| `tests/Antiphon.Agents.Pty.Tests/PtyInputLossExperiments.cs:25` | PtyInputLossExperiments | Card0027 |
| `tests/Antiphon.Agents.Pty.Tests/PtyKillProcessTreeTests.cs:30` | PtyKillProcessTreeTests | Pty |
| `tests/Antiphon.Agents.Pty.Tests/PtyLargeWriteTests.cs:35` | PtyLargeWriteTests | Pty |
| `tests/Antiphon.Agents.Pty.Tests/PtyPasteMarkerExperiments.cs:47` | PtyPasteMarkerExperiments | Card0030 |
| `tests/Antiphon.Agents.Pty.Tests/PtyStressTests.cs:14` | PtyStressTests | PtyStress |
| `tests/Antiphon.Agents.Pty.Tests/UnixPtyArgvTests.cs:157` | UnixPtyArgvTests | Integration |
| `tests/Antiphon.Agents.Pty.Tests/WaitForQuietAfterVisibleTests.cs:18` | WaitForQuietAfterVisibleTests | Pty |
| `tests/Antiphon.Agents.Pty.Tests/WindowsPtyArgvNativeTests.cs:20` | WindowsPtyArgvNativeTests | Integration |
| `tests/Antiphon.E2E/Fixtures/PtyBackendEnvGuard.cs:42` | PtyBackendEnvGuard, PtyBackendEnvGuardTests | (none on this file) |
| `tests/Antiphon.E2E/OutputDistillationApplyCanaryTests.cs:46` | OutputDistillationApplyCanaryTests | Headed, OptIn, HeadedCanary |
| `tests/Antiphon.SessionRunner.Tests/C1022BackendLaunchTests.cs:30` | C1022BackendLaunchTests | Integration |
| `tests/Antiphon.SessionRunner.Tests/ClaudeAuthProbeTests.cs:209` | ClaudeAuthProbeTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/CodexCliVersionProbeTests.cs:174` | CodexCliVersionProbeTests | Integration |
| `tests/Antiphon.SessionRunner.Tests/CodexCliVersionWindowsTests.cs:17` | CodexCliVersionWindowsTests | Integration |
| `tests/Antiphon.SessionRunner.Tests/CodexCommandLengthHttpAcceptanceTests.cs:22` | CodexCommandLengthHttpAcceptanceTests | Integration |
| `tests/Antiphon.SessionRunner.Tests/CodexLaunchRefusalTests.cs:44` | CodexLaunchRefusalTests | Unit |
| `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceInspectionR3Tests.cs:15` | CompletedCardWorkspaceRemovalTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceInspectionRepairTests.cs:74` | CompletedCardWorkspaceRemovalTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceS5Tests.cs:612` | CompletedCardWorkspaceRemovalTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/FakeHerdrServerListenerTests.cs:164` | FakeHerdrServerListenerTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/GrokRulesAdoptionTests.cs:39` | GrokRulesAdoptionTests | Integration |
| `tests/Antiphon.SessionRunner.Tests/GrokRulesFileLaunchTests.cs:39` | GrokRulesFileLaunchTests | Unit |
| `tests/Antiphon.SessionRunner.Tests/GrokRulesFileStoreTests.cs:78` | GrokRulesFileStoreTests | Unit |
| `tests/Antiphon.SessionRunner.Tests/GrokRulesHttpAcceptanceTests.cs:23` | GrokRulesHttpAcceptanceTests | Integration |
| `tests/Antiphon.SessionRunner.Tests/GrokRulesRunnerRefusalTests.cs:104` | GrokRulesRunnerRefusalTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/GrokTranscriptTailerTests.cs:558` | GrokTranscriptTailerTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/HerdrLaunchShapeTests.cs:538` | HerdrLaunchShapeTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/HerdrPaneChildKillTests.cs:128` | HerdrPaneChildKillTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/HerdrTransportTests.cs:74` | HerdrTransportTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/HostStatsProbeLiveTests.cs:19` | HostStatsProbeLiveTests | Integration |
| `tests/Antiphon.SessionRunner.Tests/PhoneHomeCommandDispatcherTests.cs:661` | PhoneHomeCommandDispatcherTests | Unit |
| `tests/Antiphon.SessionRunner.Tests/PtyBackendEnvGuard.cs:42` | PtyBackendEnvGuard, PtyBackendEnvGuardTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs:76` | TestSessionLogRoot, TestSessionTeardown, PtyHostLeakSweep, PtyHostLeakSweepTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/RunnerAbsenceEvidenceRuntimeTests.cs:271` | RunnerAbsenceEvidenceRuntimeTests, ProviderEffectForTestException, RuntimeWorld | Integration |
| `tests/Antiphon.SessionRunner.Tests/RunnerChildClaudeTokenInheritanceTests.cs:54` | RunnerChildClaudeTokenInheritanceTests | Integration |
| `tests/Antiphon.SessionRunner.Tests/RunnerCustodyLedgerBackendTests.cs:73` | RunnerCustodyLedgerBackendTests | Unit |
| `tests/Antiphon.SessionRunner.Tests/RunnerCustodyTests.cs:247` | RunnerCustodyTests | Integration |
| `tests/Antiphon.SessionRunner.Tests/RunnerPlatformContractTests.cs:17` | RunnerPlatformContractTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/RunnerPlatformLaunchTests.cs:81` | RunnerPlatformLaunchTests | (none on this file) |
| `tests/Antiphon.SessionRunner.Tests/RunnerStartupReadinessTests.cs:35` | RunnerStartupReadinessTests | Integration |
| `tests/Antiphon.SessionRunner.Tests/RunnerWorkspaceServiceTests.cs:749` | RunnerWorkspaceServiceTests | Integration |
| `tests/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests.cs:284` | UnixPtyArgvAdmissionTests | Integration |
| `tests/Antiphon.SessionRunner.Tests/WorkspaceParkPublicationTests.cs:394` | WorkspaceParkPublicationTests | Integration |
| `tests/Antiphon.SessionRunner.Tests/WorkspaceSourceVerificationWireTests.cs:33` | WorkspaceSourceVerificationWireTests | Integration |
| `tests/Antiphon.Tests/AgentTui/AgentTuiProfileServiceTests.cs:786` | AgentTuiProfileServiceTests | Integration |
| `tests/Antiphon.Tests/AgentTui/AgentTuiSecretProtectorTests.cs:665` | AgentTuiSecretProtectorTests | Unit |
| `tests/Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs:767` | RunnerProcessProbeTests | Integration, Slow |
| `tests/Antiphon.Tests/Agents/AgentRegistrySettingsTests.cs:91` | AgentRegistrySettingsTests | Unit |
| `tests/Antiphon.Tests/Agents/ClaudeAdapterLocalShellTests.cs:39` | ClaudeAdapterLocalShellTests | Pty, Integration, Slow |
| `tests/Antiphon.Tests/Agents/CodexAdapterLocalShellTests.cs:43` | CodexAdapterLocalShellTests | Pty, Integration, Slow |
| `tests/Antiphon.Tests/Agents/DirectSessionRunnerClientDisposalTests.cs:39` | DirectSessionRunnerClientDisposalTests, OwnedPtyWitness, OwnedPtyFixture | Integration |
| `tests/Antiphon.Tests/Agents/RawPtyAdapterTests.cs:25` | RawPtyAdapterTests | Pty, Integration, Slow |
| `tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTestsPty.cs:32` | RunnerGrokAdapterReadyTestsPty | Integration |
| `tests/Antiphon.Tests/Agents/TestOwnedPtyHostIdentityTests.cs:36` | TestOwnedPtyHostIdentityTests, TestOwnedPtyHostPolicyTests, OwnedPolicyFixture | Unit |
| `tests/Antiphon.Tests/Application/AgentControlServiceIntegrationTests.cs:31` | AgentControlServiceIntegrationTests | Integration |
| `tests/Antiphon.Tests/Application/AgentExecutableResolverTests.cs:34` | AgentExecutableResolverTests | Unit |
| `tests/Antiphon.Tests/Application/AgentPinPathTests.cs:26` | AgentPinPathTests | Unit |
| `tests/Antiphon.Tests/Application/AgentSessionLaunchFailureTests.cs:595` | AgentSessionLaunchFailureTests | Integration, Slow |
| `tests/Antiphon.Tests/Application/AgentSessionRuntimeTests.cs:116` | AgentSessionRuntimeTests | Integration, Slow |
| `tests/Antiphon.Tests/Application/AgentSessionServiceIntegrationTests.cs:1278` | AgentSessionServiceIntegrationTests | GitIntegration, Integration, Slow |
| `tests/Antiphon.Tests/Application/AgentTaskDecisionQuestionTests.cs:139` | AgentTaskDecisionQuestionTests | Unit |
| `tests/Antiphon.Tests/Application/AgentTaskDispatchFailureTests.cs:95` | AgentTaskDispatchFailureTests | Integration |
| `tests/Antiphon.Tests/Application/AgentTaskLandActiveSourceClaimTests.cs:73` | AgentTaskLandActiveSourceClaimTests | Integration, Slow |
| `tests/Antiphon.Tests/Application/AgentTaskLandApprovalRecoveryTests.cs:1539` | AgentTaskLandApprovalRecoveryTests | Integration |
| `tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:194` | AgentTaskLandHalfResetTests | Integration |
| `tests/Antiphon.Tests/Application/AgentTaskLandHalfResetWindowsTests.cs:191` | AgentTaskLandHalfResetWindowsTests | Integration |
| `tests/Antiphon.Tests/Application/AgentTaskLandIdentityMatrixTests.cs:85` | AgentTaskLandIdentityMatrixTests | Integration |
| `tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs:680` | AgentTaskLandPublicationTests | Integration, Slow |
| `tests/Antiphon.Tests/Application/AgentTaskLandRegistrationSafetyWindowsTests.cs:24` | AgentTaskLandRegistrationSafetyWindowsTests | Integration |
| `tests/Antiphon.Tests/Application/AgentTaskLandRemovalMatrixTests.cs:227` | AgentTaskLandRemovalMatrixTests | Integration, Slow |
| `tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs:2215` | AgentTaskReplyIntegrationTests | Integration |
| `tests/Antiphon.Tests/Application/CardSpawnModelArgumentTests.cs:275` | CardSpawnModelArgumentTests | Integration |
| `tests/Antiphon.Tests/Application/ChannelInboundRecoveryTests.cs:2805` | ChannelInboundRecoveryTests | Integration, Slow |
| `tests/Antiphon.Tests/Application/ChannelOutboundRecoveryTests.cs:553` | ChannelOutboundRecoveryTests | Integration |
| `tests/Antiphon.Tests/Application/ChannelOutboundStorageTests.cs:77` | ChannelOutboundStorageTests | Unit |
| `tests/Antiphon.Tests/Application/ChannelOutboundUnifiedTransportTests.cs:437` | ChannelOutboundUnifiedTransportTests | Integration |
| `tests/Antiphon.Tests/Application/ChannelReplyAttachmentReaderTests.cs:147` | ChannelReplyAttachmentReaderTests | Unit |
| `tests/Antiphon.Tests/Application/ClaudeRemoteControlLaunchArgsTests.cs:130` | ClaudeRemoteControlLaunchArgsTests | Unit |
| `tests/Antiphon.Tests/Application/DelegateLaunchArgvIntegrityTests.cs:58` | DelegateLaunchArgvIntegrityTests | Integration |
| `tests/Antiphon.Tests/Application/DelegateScriptCapabilityTests.cs:140` | DelegateScriptCapabilityTests | Integration |
| `tests/Antiphon.Tests/Application/DelegationBriefCeilingPtyTests.cs:45` | DelegationBriefCeilingPtyTests | Integration |
| `tests/Antiphon.Tests/Application/DelegationBriefRecoveryTests.cs:151` | DelegationBriefRecoveryTests | Integration |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.AbsentEvidence.cs:720` | DelegationDispatchRecoveryBoundaryTests | (none on this file) |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.AbsentLaunch.cs:61` | DelegationDispatchRecoveryBoundaryTests | (none on this file) |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.BriefRace.cs:59` | DelegationDispatchRecoveryBoundaryTests | (none on this file) |
| `tests/Antiphon.Tests/Application/DelegationUnitTests.cs:642` | DelegationWorkspaceBoundaryTests, DelegationReportFormatterTests, DelegationScopeLeaseTests, DelegationDenyHookPolicyTests, DelegationPoolDefaultsTests, DelegationQuestionDetectionTests, UnmarkedWaitingContractTests, BootWedgeContractTests, DelegationCostTests, DelegationKindPricingTests, PtyInlineCeilingTests, TranscriptLocalCommandEchoTests, SubagentNotificationTests, CheckScheduleBackoffTests, CheckScheduleRoundingTests, CheckScheduleElapsedTimesTests | Unit |
| `tests/Antiphon.Tests/Application/DelegationWorktreeTests.cs:1334` | DelegationWorktreeTests | Integration, Slow |
| `tests/Antiphon.Tests/Application/DeliverableBundleServiceTests.cs:75` | DeliverableBundleServiceTests | Integration |
| `tests/Antiphon.Tests/Application/DirectoryBrowseServiceTests.cs:55` | DirectoryBrowseServiceTests | Unit |
| `tests/Antiphon.Tests/Application/GrokDelegateEndToEndTests.cs:66` | GrokDelegateEndToEndTests, RecordingRunnerClient, DelegateTaskApiRelay | Integration |
| `tests/Antiphon.Tests/Application/GrokRulesAutoCompactionCalibrationTests.cs:27` | GrokRulesAutoCompactionCalibrationTests | Integration |
| `tests/Antiphon.Tests/Application/GrokRulesCompactionAcceptanceTests.cs:33` | GrokRulesCompactionAcceptanceTests | Integration |
| `tests/Antiphon.Tests/Application/GrokRulesDispatchAcceptanceTests.cs:33` | GrokRulesDispatchAcceptanceTests | Integration |
| `tests/Antiphon.Tests/Application/GrokRulesHerdrAcceptanceTests.cs:36` | GrokRulesHerdrAcceptanceTests | Integration |
| `tests/Antiphon.Tests/Application/GrokRulesLaunchRefusalTests.cs:102` | GrokRulesLaunchRefusalTests | Integration |
| `tests/Antiphon.Tests/Application/GrokRulesLiveMappedDispatchTests.cs:34` | GrokRulesLiveMappedDispatchTests | Integration |
| `tests/Antiphon.Tests/Application/GrokRulesNativeReadWireTests.cs:27` | GrokRulesNativeReadWireTests | Integration |
| `tests/Antiphon.Tests/Application/GrokRulesTransportCompatibilityTests.cs:36` | GrokRulesTransportCompatibilityTests | Unit |
| `tests/Antiphon.Tests/Application/GrokSignInRuntimeTests.cs:34` | GrokSignInRuntimeTests | Integration |
| `tests/Antiphon.Tests/Application/InstructionFileStampTests.cs:170` | InstructionFileStampTests | Unit |
| `tests/Antiphon.Tests/Application/InternalDecisionPolicyTests.cs:217` | InternalDecisionPolicyTests | Unit |
| `tests/Antiphon.Tests/Application/OrchestratorWorkspaceLayoutTests.cs:25` | OrchestratorWorkspaceLayoutTests | Card0251, Unit |
| `tests/Antiphon.Tests/Application/OutboundConversionManifestTests.cs:292` | OutboundConversionManifestTests | Unit |
| `tests/Antiphon.Tests/Application/PhoneHomeTaskRoutingTests.cs:161` | PhoneHomeTaskRoutingTests | Unit |
| `tests/Antiphon.Tests/Application/PostLandMutationCustodyTests.cs:100` | PostLandMutationCustodyTests, PostLandMutationWorld | Integration |
| `tests/Antiphon.Tests/Application/PtyDeliveryCeilingsTests.cs:330` | PtyDeliveryCeilingsTests | Unit |
| `tests/Antiphon.Tests/Application/RepositoryLeaseWaitersTests.cs:90` | RepositoryLeaseWaitersTests | Unit |
| `tests/Antiphon.Tests/Application/RunnerCodexCliEvidenceTests.cs:987` | RunnerCodexCliEvidenceTests | Integration |
| `tests/Antiphon.Tests/Application/SessionDeliveryProfileTests.cs:162` | SessionDeliveryProfileTests | Unit |
| `tests/Antiphon.Tests/Application/SessionGenerationExitTests.cs:664` | SessionGenerationExitTests | Integration |
| `tests/Antiphon.Tests/Application/SessionMessageQueueGrokPtyIntegrationTests.cs:126` | SessionMessageQueueGrokPtyIntegrationTests | Integration, Slow |
| `tests/Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs:38` | SessionMessageQueuePtyIntegrationTests | Integration, Slow |
| `tests/Antiphon.Tests/Application/SessionQueueReceiptPlumbingTests.cs:26` | SessionQueueReceiptPlumbingTests | Integration |
| `tests/Antiphon.Tests/Application/SlashCommandCatalogServiceTests.cs:24` | SlashCommandCatalogServiceTests | Unit |
| `tests/Antiphon.Tests/Application/SlashCommandMenuReconciliationTests.cs:272` | SlashCommandMenuReconciliationTests | Headed, OptIn, Integration, Slow |
| `tests/Antiphon.Tests/Application/TaskParkPublicationTests.cs:453` | TaskParkPublicationTests | Integration, Slow |
| `tests/Antiphon.Tests/Application/TaskProgressStallSweepTests.cs:795` | TaskProgressStallSweepTests | Integration |
| `tests/Antiphon.Tests/Application/UnixDelegateLaunchArgvTests.cs:21` | UnixDelegateLaunchArgvTests | Integration |
| `tests/Antiphon.Tests/Application/WorktreeDefaultAdmissionTests.cs:255` | WorktreeDefaultAdmissionTests | Integration, Slow |
| `tests/Antiphon.Tests/Application/WorktreeDefaultContinuationTests.cs:485` | WorktreeDefaultContinuationTests | Integration, Slow |
| `tests/Antiphon.Tests/Checkpoints/CheckpointBuildBindingPathTests.cs:36` | CheckpointBuildBindingPathTests | Unit |
| `tests/Antiphon.Tests/Checkpoints/CheckpointImportTests.cs:115` | CheckpointImportTests | Unit |
| `tests/Antiphon.Tests/Checkpoints/CheckpointOwnedOutputTests.cs:80` | CheckpointOwnedOutputTests | Unit |
| `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:340` | CheckpointRecoveryWindowsTests | Integration, Slow |
| `tests/Antiphon.Tests/Checkpoints/CheckpointShadowCustodyTests.cs:77` | CheckpointShadowCustodyTests | Integration |
| `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:145` | CheckpointSourceStateTests | Integration, C835 |
| `tests/Antiphon.Tests/Checkpoints/EvidenceFolderTests.cs:13` | EvidenceFolderTests | Unit |
| `tests/Antiphon.Tests/Checkpoints/TimeoutTests.cs:98` | TimeoutTests | Unit |
| `tests/Antiphon.Tests/Infrastructure/AmServiceDeployReadinessTests.cs:118` | AmServiceDeployReadinessTests | Integration |
| `tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:311` | LandRecoveryCheckoutTests | Integration |
| `tests/Antiphon.Tests/Infrastructure/LandRecoveryIgnoredObstructionTests.cs:100` | LandRecoveryIgnoredObstructionTests | Integration |
| `tests/Antiphon.Tests/Infrastructure/LandWorkspaceTests.cs:148` | LandWorkspaceTests | Integration |
| `tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs:622` | LandingGitTests | Integration |
| `tests/Antiphon.Tests/Infrastructure/LandingRemovalPolicyControlTests.cs:442` | LandingRemovalPolicyControlTests | Unit |
| `tests/Antiphon.Tests/Infrastructure/OperatorTokenFileTests.cs:31` | OperatorTokenFileTests | Unit |
| `tests/Antiphon.Tests/Infrastructure/RepositoryChildRecoveryTests.cs:245` | RepositoryChildRecoveryTests | Integration, Slow |
| `tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseTests.cs:21` | RepositoryMutationLeaseTests | Integration |
| `tests/Antiphon.Tests/Infrastructure/SettledWorktreeRemovalTests.cs:103` | SettledWorktreeRemovalTests | Integration |
| `tests/Antiphon.Tests/Infrastructure/StartRefGitDirectoryCleanupTests.cs:15` | StartRefGitDirectoryCleanupTests | Integration |
| `tests/Antiphon.Tests/Infrastructure/WorkspaceHookRunnerTests.cs:250` | WorkspaceHookRunnerTests | Integration, Slow |
| `tests/Antiphon.Tests/Infrastructure/WorktreeGuardedCleanupTests.cs:388` | WorktreeGuardedCleanupTests | Integration, Slow |
| `tests/Antiphon.Tests/Infrastructure/WorktreeLockDiagnosticsTests.cs:130` | WorktreeLockDiagnosticsTests | Unit |
| `tests/Antiphon.Tests/Infrastructure/WorktreeLockDiagnosticsWindowsTests.cs:18` | WorktreeLockDiagnosticsWindowsTests | Integration |
| `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs:137` | BuildSlotScriptTests | Integration |
| `tests/Antiphon.Tests/Scripts/C1008PlatformContractTests.cs:104` | C1008PlatformContractTests | Unit |
| `tests/Antiphon.Tests/Scripts/ClaudeTokenRefreshOptInTests.cs:113` | ClaudeTokenRefreshOptInTests | Integration |
| `tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs:31` | NightlyNativeOwnershipTests | Integration |
| `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:3955` | RemoteScriptContractTests | Unit |
| `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:101` | RollingVolumeRecycleScriptTests, C1008WrapperFixture, C1008HostFixture | Unit |
| `tests/Antiphon.Tests/Scripts/ScriptHarnessSharingTests.cs:39` | ScriptHarnessSharingTests | Integration |
| `tests/Antiphon.Tests/Scripts/WindowsUnitPlatformAdmissionTests.cs:431` | WindowsUnitPlatformAdmissionTests | Unit |
| `tests/Antiphon.Tests/TestHelpers/DirectoryLinkFixtureWindowsTests.cs:93` | DirectoryLinkFixtureWindowsTests, NativeJunctionScratch | Integration |
| `tests/Antiphon.Tests/TestHelpers/PtyBackendEnvGuard.cs:43` | PtyBackendEnvGuard, PtyBackendEnvGuardTests | Integration |
| `tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs:381` | TestClassificationPolicyTests | Unit, Slow, OptIn |

## Static reconstruction transcript and reproduction

The following are read-only repository inspections, not test commands:

```sh
git fetch origin master
git rev-parse origin/master
# 2d2b30364d1793aedd9dae725a68246e127e7dd0
git ls-tree -r --name-only 2d2b30364d1793aedd9dae725a68246e127e7dd0 -- tests scripts/fixtures/nightly
git show 2d2b30364d1793aedd9dae725a68246e127e7dd0:tests/test-execution-policy.json
git show 2d2b30364d1793aedd9dae725a68246e127e7dd0:scripts/lib/nightly-policy.ps1
git show 2d2b30364d1793aedd9dae725a68246e127e7dd0:scripts/lib/nightly-tests-impl.ps1
```

Source enumeration used every tracked .csproj, literal ProjectReference/Compile Include/IsTestProject XML, all .cs under tests, and the named CARD-1287 reports/filter/roster files. Read-only Node text enumeration produced: 45 total project files, 16 tests/ project files, 11 literal test projects, six policy projects, five missing policy paths; 373/361 stored roster lines with 343 common. Appendix A has 792 matching source files; Appendix B has 171. Those last two counts are reproducible lexical inventories, not test or UID counts. No output of a failed/truncated command was used to establish absence; the complete exported tree was enumerated locally.

--- next stage ---
next: plan
handoff: Use the pinned master project/resource inventory and release hook evidence to define an exact two-SHA selection contract, narrow core, conservative shared-impact/unknown-path behavior and scoped verification; reconcile existing policy-universe and live-gate gaps without inheriting old roster qualification.
artifact: docs/investigations/2026-10-16-card-1313-change-aware-selection.md
