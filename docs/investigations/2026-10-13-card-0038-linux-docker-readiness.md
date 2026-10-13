# CARD-0038: Linux and Docker readiness, age-lane investigation

Date: 2026-10-13 UTC. Investigate task `2007037f-ac81-4e98-882c-05053d5413e8`.
Source inspected: `00fa6b884a5f127982cff26a677aae74e6666650`, branch
`feat/card-task-2007037f`. Read-only investigation; the only repository change is
this report. **No builds, tests, provider turns, deployments or board writes ran.**

## Verdict and recommended disposition

**Ordinary Linux PTY sessions and a self-contained Linux Docker stack are implemented.
Full Linux deployment/test parity is not complete. Split CARD-0038 into the remaining
concrete owners below; do not close it as wholly done.** Its opening assertion that
Antiphon is Windows-only is obsolete. Its specific backend classification bug is
fixed, but its separate request for measured Linux delivery ceilings remains open.

Confirmed here means a mechanism reconstructed from current source plus named stored
card/test evidence or a direct read-only observation. It does not mean a fresh test
run at this source SHA. Historical red counts are retained with their assembly and
source identities; they are not current-master failure counts.

The caller must decide whether “Linux ready” means the working PtyHost/container
deployment, or every optional desktop feature and capped session behavior too.
Existing cards already own most remaining work. New work is marked **needs a card**,
not silently assigned to an unrelated card. This stage designs or implements no fix.

## Card and live evidence

`pwsh -NoProfile -File scripts/card.ps1 get CARD-0038 -Board Antiphon` returned card
`d595dddf-8632-403b-a229-1e0716f00d36`, initially Backlog, four revisions, with the
original Linux/Docker inventory. A later search returned InProgress; this task made
no move. `history CARD-0038 -Board Antiphon` returned only four priority/content
revisions: three on September 2 and the September 6 Normal-priority policy. There
was no recorded technical closure in those revisions. The original text explicitly
allows either Linux memory containment **or an explicit unsupported path**, and
explicitly forbids guessing Linux paste ceilings.

Read-only HTTP observations, 2026-10-13 about 16:49–16:50 UTC:

| Source | Observed evidence | What it establishes |
|---|---|---|
| Server `GET /api/version`, 16:49:46.553Z | `7f8448798955a24427bc2279138bf9fb64bc4195`, `land-v2` | Running server differs from inspected source; no activation inference from HEAD |
| `GET /api/session-runners`, 16:49:46.652Z | desktop: `platform=windows`, available/eligible; server2: `platform=linux`, available/eligible, accepting, capacity 10, occupied 7; temp offline | A live Linux execution lane exists alongside Windows |
| `GET /api/session-runners/server2/status`, 16:49:46.750Z | `buildVersion=7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, heartbeat 16:49:40.207Z, 7 runner sessions, 2 queued tasks, not draining | Ordinary runner admission is live; deployed runner also differs from inspected source |
| Assigned container `GET http://127.0.0.1:8080/capabilities` | same runner build SHA; `platform=linux`; `ptyBackend=UnixPty`, requested `inbox`, `fellBack=false`, deprecated false; `sessionBackends=[pty-host]`; `verificationCustodyBackend=null`; no `verificationCustodyV1` feature | Linux does not use ConPTY; neither Herdr nor SourceLanding custody is advertised here |
| Task-detail DB projection `GET /api/agent-tasks/2007037f-ac81-4e98-882c-05053d5413e8` | `summary.runnerId=server2`, kind Codex, role Investigate; session `ea058bd7-ddd1-4a5d-b60c-ad63334425eb`, Running | Binds this investigation to a Linux-runner session |
| Server-normalized `GET /api/sessions/ea058bd7-ddd1-4a5d-b60c-ad63334425eb/transcript?since=0` | last sequence 4; sequence 1 `UserPrompt`, 700 characters, contains exact inbox path `c57359b6-f047-4730-9184-37edbfe02e04.md` | Real recipient prompt ingestion, not merely health or a terminal redraw; this small pointer is **not** a large-paste measurement |

The catalogue's phone-home runner store and loopback capabilities' custody store
were different IDs; they are not treated as interchangeable identity proof. The
observations are endpoint-scoped. No provider credential stores were opened.

## 1. PTY/session inventory against the original card

| Original concern | Current mechanism and evidence | Status |
|---|---|---|
| Linux incorrectly resolves `InboxConhost` | `src/Antiphon.Agents.Pty/PtyBackend.cs:20` declares `UnixPty`; `:90` returns it before Windows selectors or redistributable discovery. `PtyAgentRunner.cs:204` uses Porta unless ModernConPty was selected. `Antiphon.Agents.Pty.csproj:12` pins Porta.Pty 1.0.7. Live capabilities above independently report UnixPty. | **Done: backend classification** |
| Linux ceilings must be measured separately | `server/Application/Settings/DelegationSettings.cs:291` has only a ModernConPty arm; the default maps UnixPty to `DeliveryBackend.InboxConhost` and the old values (`:159` brief 900 bytes, `:201` tripwire 1,024 bytes). `AgentTaskReplyService.cs:1059` explicitly chooses InboxConhost ceilings for runner-bound input. `tests/Antiphon.Agents.Pty.Tests/PtyPasteMarkerExperiments.cs:250` still requires the Windows redistributable; `ClSession.cs:13` requires Windows. | **Not done: measurement/profile**, not evidence that Linux loses bytes |
| Win32 spawner/STARTF flags | `src/Antiphon.PtyHost/Program.cs:11` selects Win32 only on Windows, otherwise `PosixProcessSpawner`. POSIX implementation `:31` starts with an argument vector, `:67` calls setsid, `:80` redirects stdio, `:89` closes inherited aliases. Windows-only APIs remain in their selected implementations. | **Done for ordinary Linux native launch** |
| Linux launch/pipe semantics | `PtyHostServer.cs:34` applies CurrentUserOnly only on Windows; `:52` disposes cancelled listeners. Stored CARD-0594 investigation `docs/investigations/2026-09-22-card-0594-linux-pty-host-launch-deadlock.md:66` reconstructs the old 30-second EOF wait + 15-second connection timeout, with leaked fds 6/7 at `:83`. Current source closes those aliases. CARD-0490 and CARD-0594 are Done. Current task's UserPrompt is additional operational evidence. | **Original launch blocker fixed** |
| WindowsJobObject memory caps | `PtyAgentRunner.cs:224` still calls `WindowsJobObject.AssignMemoryLimitedJob` for a positive cap, after spawning. `WindowsJobObject.cs:34` explicitly throws off Windows. Default is zero (`server/Application/Settings/AgentSessionSettings.cs:13`); phone-home projection sets zero (`PhoneHomeLaunchPolicy.cs:268`). Linux custody cgroups provide descendant containment, not the session memory-cap API. | **Explicit unsupported path exists**, satisfying that alternative in the card; capped-session parity absent, with different direct/phone-home behavior |
| Shadow copies | `src/Antiphon.PtyHost.Client/PtyHostLauncher.cs:13` chooses the extensionless host; `ShadowCopyStore.cs:173` includes it, `:188` includes libporta_pty.so, `:251` preserves Unix modes. Runner Dockerfile `:39` publishes both Linux runner and host, `:161` copies the native library. | **Portable; retaining shadow copies is not a Linux blocker** |
| PowerShell shell-outs | `WorkspaceHookRunner.cs:121` selects powershell.exe or /bin/sh. RawPtyAdapter `:43` passes its configured launch spec, rather than requiring PowerShell. AgentControlService and SessionRunnerRuntime scan found platform-aware policies, not unconditional powershell.exe launch requirements. Persistent image installs runnable pwsh (`docker/session-runner-grok/Dockerfile:207`). A separate orchestrator deny hook still names `powershell` (`DelegationWorktreeService.cs:429`); this container resolves pwsh but no powershell executable. | **Mostly portable; deny-hook interpreter remains a concrete gap** |
| Transcript root/cwd encoding/watchers | `src/Antiphon.SessionRunner/TranscriptTailer.cs:1135` uses CLAUDE_CONFIG_DIR or UserProfile/.claude/projects; `:579` searches all project directories for the exact session filename, avoiding an encoded-cwd formula. It polls files (`:38`, `:556`), so the original FileSystemWatcher concern does not describe this implementation. `PhoneHomeLaunchPolicy.cs:249` projects the Linux Claude root. Codex transcript ingestion is demonstrated by the stored UserPrompt above. | **Portable mechanism; no fresh Claude Linux binding/envelope acceptance claimed** |
| Linux SourceLanding | `SessionRunnerRuntime.cs:75` selects probe-qualified Linux custody; `LinuxCgroupCustodyProbe.cs:24` requires delegated root, NoNewPrivs=0 and both helper probes. `SourceLandingAdmission.cs:61` requires the feature, supported backend and store. It no longer hardcodes windows-job-v1. Live runner reports no custody. | **Source implemented; live support/native qualification incomplete**, CARD-0604/CARD-0634, CARD-0598 folded dependency |
| Herdr | `HerdrTransport.cs:38` has Unix sockets and `HerdrPeerIdentity.cs:46` reads Linux SO_PEERCRED. `HerdrDisposalLocatorStore.cs:72` still unconditionally calls CreateFileW; `HerdrDisposalProcessInspector.cs:23` returns incomplete off Windows. | **Transport portable, disposal not portable**, CARD-0864 |

A grep hit on ConPTY/kernel32 is not itself a Linux blocker: the backend selector,
Win32/Posix spawner selection, Windows host-stat probe and command-line guards
select implementations by platform. The unguarded cap call and Herdr locator call
above are the material exceptions. CARD-1022's remaining legacy Windows-backend
retirement is independent of making Linux use Porta.

## 2. What Docker runs, and what still needs Windows

Three different topologies must not be conflated:

1. **Desktop development/control plane:** `docker-compose.dev.yml:16` provides
   Postgres and `:48` Redpanda. Aspire owns the server/client and supervised desktop
   processes. `Antiphon.AppHost/Program.cs:28` and `:41` hardcode SessionRunner.exe
   and FakeGateway.exe. `scripts/install-autostart.ps1:149` falls back to
   powershell.exe, and its later sections register Scheduled Tasks. These are
   Windows deployment procedures, not prerequisites for the root Docker server.
2. **Persistent server2:** `docker-compose.server2-runner.yml:95` disables Herdr;
   `:107` mounts work/state, `:109` the nested Docker store, `:110` persistent /tmp.
   `docs/docker-stack.md:771` explicitly states no standing Antiphon server or
   Postgres there: the runner phones home to the production control plane.
   `:769` describes privileged DinD with tini, dockerd supervision, SDK, Node,
   PowerShell and provider CLIs. Rolling named phases and stop gates are at `:109`.
3. **Self-contained application/test stack:** `docker-compose.yml:17` server,
   `:72` separate runner, `:103` Postgres, and state-init. `Dockerfile:19` publishes
   linux-x64 server with referenced src projects (`:16`); `:33` embeds built React.
   Work and state are volume-backed; server reaches runner by internal HTTP, not
   the Windows desktop. CARD-0590's closure records shipped `f17b2ca4` and the later
   topology replacement, with Cut A landed `e992897c`. This is implemented and has
   stored acceptance, but no fresh image build/smoke at the inspected SHA ran here.

The Linux runner image installs Linux Grok, Claude and Codex executables; it is not
trying to run claude.exe through Wine. Claude installation/hash checks are at
`docker/session-runner-grok/Dockerfile:113`; Codex at `:138`. Credentials are
runtime-mounted or volume state: `docker-compose.server2-runner.yml:81` Claude
root, `:88` Codex root, `:132` read-only Claude token file, `:144` Codex home.
`docs/docker-stack.md:788` owns provisioning. Authentication availability was not
re-probed here and is not inferred from binaries; CARD-0628/CARD-0638 retain proof
obligations. CARD-0660 owns the shipped Codex runner support.

**Concrete Linux-only deployment limits, with their affected feature:**

- **Full production services are disabled in base Compose.**
  `docker-compose.yml:41` disables interpreters/distiller; `:44` Hangfire worker;
  `:45` census; `:47` schedules; `:48` channel bridge; `:49` digest. No messaging
  broker/gateway is included in this four-service application stack. It can run the
  application without Windows, but it is not a configuration proving every production
  capability. Choosing a supported full production deployment remains **needs a card**.
- **Linux server OS census cannot work with the registered implementation.**
  `server/Program.cs:576` registers WindowsZombieProcessCensus unconditionally;
  its `SnapshotAsync` at `WindowsZombieProcessCensus.cs:17` throws off Windows.
  `ZombieCensusService.cs:54` calls that seam. Base Compose disabling census is
  consistent with this limitation. **Needs a card** for Linux census parity.
- **Claude RC supervision has a Windows-only evidence source.**
  `server/Program.cs:599` registers WindowsRcBridgeProbe; `WindowsRcBridgeProbe.cs:57`
  calls GetExtendedTcpTable; `:90` catches failure and returns zero. On Linux this
  cannot prove the connection count. `SessionHealthService.cs:197` consumes it.
  Conditional feature limitation, not proof that Linux server startup fails.
  **Needs a card** if this watch is supported on a Linux control plane.
- **Herdr disposal needs Windows**, as reconstructed above. The shipped Linux
  runner disables Herdr; a full Unix Herdr parity claim must account for CARD-0864.
  Additional Unix peer/read hardening remains CARD-0872: current
  `HerdrPeerIdentity.cs:50` uses peer PID without comparing UID.
- **Positive per-session memory caps lack Linux parity**, as above; supported
  uncapped sessions work. Explicit refusal meets the card's allowed alternative,
  while phone-home discards the cap. Whether parity is required is a caller decision.
- **SourceLanding on the live Linux runner is unavailable**, despite implemented
  cgroup support. An ordinary successful session or Docker health check supplies
  no sealed descendant-zero receipt. CARD-0604/CARD-0634 own this gap.
- **The orchestrator deny-hook interpreter is Windows-specific.**
  `DelegationWorktreeService.cs:532` writes the hook constant containing powershell.
  This is conditional on installing that hook; it does not explain a generic launch
  failure. **Needs a card** for its Linux supported/unsupported contract.
- **Desktop rollout/bootstrap tooling is not a Linux-host deployment contract.**
  `docs/docker-stack.md:5` requires the canonical desktop checkout for staged
  server2 rollout. AppHost's .exe paths above remain Windows-specific. A Linux-only
  installation can use the Docker entrypoints, but replacing all desktop control
  plane operations has no acceptance evidence in this investigation. Included in
  the full-production deployment decision, not a second generic “port everything” card.

The Windows runner remains necessary for Windows-required tasks and native
Windows verification, not for ordinary Linux sessions. Platform dispatch is
implemented: `AgentTaskService.cs:1690` matches requirements against descriptors,
`AgentTaskDispatcher.cs:6323` checks fresh platform evidence, and
`src/Antiphon.SessionRunner/RunnerPlatformLaunchGuard.cs:14` independently refuses
a mismatch before launch. CARD-0710 is Done, and live runners advertise
`required-platform-v1`. Keeping a Windows verification lane is intentional parity.

## 3. CI and test portability

**None of the three native test projects is entirely Windows-only.** Plain net9.0
targets and mixed managed/native classes coexist. CARD-0590's old description
calling PtyHost/SessionRunner/Agents.Pty “Windows forever” is overbroad against
current source.

| Project/lane | Windows-specific surface | Linux/Unix-specific surface and portable remainder |
|---|---|---|
| Antiphon.Agents.Pty.Tests | PtyAgentRunnerTests `:19`, PtyBackendContractTests `:19`, PtyStressTests `:17`, PtyLargeWriteTests `:42`, PtyKillProcessTreeTests `:28`, PtyInputChunkingTests `:32`, PtyBracketedPasteContractTests `:39`, PtyInputLossExperiments `:26`, PtyPasteMarkerExperiments `:48`, ModernPtyDa1Tests `:48`, FakeClaudeContractTests `:35`, FakeGrokContractTests `:27`, ClaudeDetectorsTests `:16`, WaitForQuietAfterVisibleTests `:16`; shell32/native argv classes LaunchArgvGuardTests `:36`, ModernConPtyCommandLineTests `:30`, CommandLineLengthTests `:24`, WindowsPtyArgvNativeTests `:20`; real Claude/Grok/Codex helpers ClSession `:13`, GkSession `:19`, CxSession `:35` require Windows. These references are under tests/Antiphon.Agents.Pty.Tests. | UnixPtyArgvTests `:157` skips Windows and executes native Unix argv; LinuxCgroupContainmentTests exercises the Linux launch-vector seam. Managed policy, screen, parsing and UTF-8 tests are not intrinsically Windows-only. |
| Antiphon.PtyHost.Tests | PtyHostLauncherTests `:21`, HostSessionPipeTests `:15`, ParentDeathSpikeTests `:20`, HostCustodyTests `:317` gate native Windows. | LinuxPtyHostLauncherTests `:20`, Card0490NativeGuestGuardTests `:11`, LinuxCgroupCustodyTests `:42` are Linux-only; custody also requires helpers/delegated root. ShadowCopyStoreTests `:275` has a Linux mode-bit row. Other protocol/source/managed rows are portable. |
| Antiphon.SessionRunner.Tests | C1022BackendLaunchTests `:30`, GrokRulesHttpAcceptanceTests `:23`, CodexCommandLengthHttpAcceptanceTests `:23`, RunnerCustodyTests `:248` explicitly gate Windows. CodexWindowsLaunchPolicyTests `:15` calls the real OS-sensitive policy without a Linux skip; RunnerCustodyLedgerBackendTests `:49` expects Windows refusal of Linux support. | LinuxPhoneHomeRunnerTests `:13`, HostStatsProbeLiveTests `:16`, CompletedCardWorkspaceS5Tests `:553` gate Linux native witnesses. AgentPinWorkspaceStoreTests `:19` and `:54` **assert Linux** instead of skipping; they belong to Integration but the CI job selects the whole project. HerdrPaneDisposalGuardTests `:14` skips Linux for CARD-0864; GrokRulesAdoptionTests `:24` skips the specific CARD-0871 case. Managed contract and transcript classes also exist. |
| Antiphon.Tests | Native file-sharing/junction/handle methods are Windows-specific, e.g. WorktreeLockDiagnosticsWindowsTests `:18`, RepositoryMutationLeaseTests `:21`, EvidenceFolderTests `:15`, ScriptHarnessSharingTests `:40`, LandWorkspaceTests `:163`, StartRefGitDirectoryCleanupTests `:17`, and DelegateLaunchArgvIntegrityTests. Not a whole-project platform restriction. | CARD-1210 froze 57 new native-Linux method identities / 68 expanded results in `Scripts/WindowsUnitSkipRoster.cs:13`; audited at `WindowsUnitPlatformAdmissionTests.cs:52`. HostJqPrerequisiteScriptTests, native JqRunnerImageContractTests, GrokSha256PinTests, two CodexRootExecContractTests rows and Linux host/recycle rows require Linux. Source inspection/wrapper/storage rows stay portable (`WindowsUnitSkipRoster.cs:73`); symlink privilege is a capability condition, not an OS-wide skip. |
| Antiphon.E2E | LandDeliveryFixture `:59`, OutputDistillationApplyCanaryTests `:46`, headed ClaudeHarness `:349` require Windows; the first two still assert it. | ChannelOutboundIsolatedTests `:58` and its Composed part `:39` are Linux-only. `Fixtures/IsolatedSessionRunner.cs:132` now resolves the platform apphost and falls back to the DLL: its former hardcoded .exe blocker is fixed. Browser/Docker/provider prerequisites are separate from OS portability. |
| Antiphon.Messaging.Tests / client | No Windows-only project restriction established. | Checked-in Ubuntu CI runs messaging and client lint/build/Vitest; this does not prove all backend/native tests. |
| Antiphon.MarkdownPdf.Tests | No whole-project Windows restriction. MarkdownPdfCommandTests `:60` and MarkdownPdfRendererTests `:142`/`:177` return before Unix filesystem witnesses on Windows; these returns are not counted skips. | MarkdownPdfRealBrowserTests `:23` requires headed opt-in and `:25` explicitly skips off Linux for the Docker browser fixture. Other fake-browser/renderer/command tests select platform executables. This project has no explicit CI step in ci.yml. |

These are source-level classifications, not discovered/executed case counts. Some
classes mix platforms at method level. Source grep cannot establish that every
test in an otherwise portable class works on Linux.
Executables under tests such as DockerStack.Fixture, CustodyTestChild and the
Checkpoint lifecycle/repeat/filter hosts are fixture programs, not additional
standalone test suites. Their publication is not test-execution evidence.

Checked-in CI, `.github/workflows/ci.yml:12`, runs Ubuntu client and messaging
(`:23`–`:36`); the Windows job (`:37`) runs whole PtyHost and SessionRunner projects
(`:44`–`:47`). It has **no Linux PtyHost/SessionRunner/Agents.Pty lane and no
Antiphon.Tests lane**. CARD-0605 owns native Linux recurring coverage. Its old
“never executed, ever” wording is not a claim about current ad-hoc checkpoint
work: the live Linux runner and stored Linux review evidence now exist. CARD-1114
separately owns recurring Windows audit/argv coverage. No live Actions or Windmill
run history was sampled here; green automation is not claimed from YAML or Done cards.

Both CI jobs provision SDK 9 (`ci.yml:22`, `:43`), while `global.json:3` requires
10.0.204 with latestMinor. SDK 9 alone cannot satisfy that requirement. Hosted
images might already contain SDK 10, so this is an unqualified prerequisite,
**not a newly reproduced CI failure**. CARD-0914 explicitly owns global.json,
CI/nightly SDK and runtime alignment. Linux server images likewise need the
landing Git floor documented in `docs/docker-stack.md:769`; the runner pins Git
2.47.3, while root `Dockerfile:27` installs apt git without that minimum check.
The root server image's actual Git version was not measured: no current failure
is attributed to it.

### What the cited 89 Windows and 21 Linux failures actually mean

- **CARD-1210 is now Done**, not an outstanding 89-failure repair. Its closure at
  inspected source `00fa6b884` records five landed slices: platform admission,
  repository LF policy, Windows symlink capability, shared payload preflight and
  bounded spawning-class deadlines. The original 89 distinct Windows failing
  methods / 100 failed results and two 15-minute no-TRX runs were inherited
  measurements. Closure retains exact excluded reds under CARD-1216/CARD-1218;
  all PC-1..46 remain pending under the operator's Mutation pause. No claim of
  current full-Unit green follows from the closure.
- **CARD-1212 remains Backlog.** Crucially, its 378-result run was the
  **Antiphon.SessionRunner.Tests Unit category**, not Antiphon.Tests nor the
  full repository. `docs/superpowers/reviews/2026-10-13-card-1032-s4-code-evidence.md:43`
  identifies the assembly/scope; `:65` records 378 executed, 357 passed, 21
  failed, zero skipped at `fae7ac59eb757e3e97587cb6cf745949afabdcee`. Exact rerun at
  base `8a833b488387a69102bb7da2f455ec2cdccbbf8f` executed 21: 20 failed, one passed.
  Nineteen results were CodexWindowsLaunchPolicyTests variants; one was
  RunnerCustodyLedgerBackendTests.Linux_backend_accepted_only_when_probe_passed.
  PhoneHomeConnectionServiceTests.Websocket_connect_that_hangs_ends_within_the_connect_timeout
  passed at base and on its isolated implementation rerun (`:120`). Stored receipt
  and exact method roster are at `:108`, `:112`, `:127`.
- The mechanism of those Windows-policy reds is source-reconstructable:
  `src/Antiphon.SessionRunner/CodexWindowsLaunchPolicy.cs:41` returns unchanged
  requests off Windows, while `CodexWindowsLaunchPolicyTests.cs:23` expects the
  Windows rewrite. The ledger test at `:63` expects an exception where current
  `RunnerCustodyLedger.cs:146` accepts Linux platform support. CARD-0821 and
  CARD-0690 describe older overlapping runner-lane failure/hang evidence;
  CARD-0868 localizes prior stalls in CodexLaunchRefusalTests, GrokRulesStoreFailureTests
  and GrokRulesFileLaunchTests. Consolidate the shared 19+1 cluster under CARD-1212;
  retain distinct historical hangs until narrowly rechecked. A completed 378-case
  run does not prove those older, differently selected runs cannot hang.

### Old follow-up cards that should not create duplicate port work

- **CARD-0587:** original missing-src Dockerfile mechanism is absent now:
  `Dockerfile:16` copies src and `:19` publishes the server. CARD-0590 records
  shipment. Reconcile the stale card; no new root-image rebuild was run here.
- **CARD-0588:** partly satisfied. FakeGrokContractTests now skips off Windows
  (`:27`); WorktreeLockDiagnosticsWindowsTests `:18`, LandDeliveryFixture `:59`
  and OutputDistillationApplyCanaryTests `:46` still assert Windows. Keep only
  the three residual fixture/admission decisions; do not repeat four fixes.
- **CARD-0598:** current code supports linux-cgroup-v1; its original Windows-only
  admission description is stale. CARD-0604 explicitly folds it into Cut B and
  directs duplicate closure after Cut B lands. Native qualification remains
  CARD-0604/CARD-0634, rather than a second independent backend implementation.
- **CARD-0602:** still concrete: `tests/Antiphon.DockerStack.Fixture/Program.cs:66`
  Serve prints “serve” and returns 0; Observe at `:17` validates arguments/file
  presence and prints “observe”. It does not exercise the intended queue/recovery
  machinery. An image containing this executable is not delivery acceptance.

## 4. Ranked, de-duplicated remaining work

Ranks below are investigation order by consequence/dependency, not changes to
operator priority. “Historical” requires a targeted current-source recheck before
repair; no failure is invented from an open card alone.

| Rank | Concrete remaining outcome | Existing owner / status at read | Evidence and boundary |
|---|---|---|---|
| 1 | Resolve container-recreate process adoption identity | CARD-0829 Backlog; related identity concern CARD-0668 Backlog | Current `ProcessLivenessProbe.cs:33` permits a two-minute start-time tolerance; `SessionRunnerRuntime.cs:1976` probes manifest PID and `:2005` kills an untracked PID after failed adoption. CARD-0829 reconstructs foreign-PID collision after persistent /tmp. No collision or kill was induced. CARD-0668 is separately stored Linux reader-identity evidence; do not assume the two mechanisms are identical. |
| 2 | Qualify Linux SourceLanding support on the actual deployed runner | CARD-0604 Review + CARD-0634 Backlog; reconcile folded CARD-0598 | Live backend null; source probe requirements above; CARD-0634 records native containment/egress evidence gaps. This is unavailable now, not an ordinary PTY blocker. |
| 3 | Resolve the bounded Linux runner Unit failure cluster and distinguish older hangs | CARD-1212 Backlog; overlaps CARD-0821/CARD-0690; CARD-0868 for distinct classes | 19 Windows-policy results + one ledger result + one timeout, source/stored receipts above. One shared triage owner, not three independent 19-test repairs. |
| 4 | Establish the full Linux production deployment feature contract | **Needs a card** | Base Compose disables the named services and omits broker/gateway; desktop control plane/rollout remains the operating deployment. Core self-contained application already exists. Deciding full services/operations belongs in Decide, not a new container implementation here. |
| 5 | Establish Linux OS-census support or its supported omission | **Needs a card**; CARD-0298 is Done, Windows implementation owner | Unconditional DI + WMI throw, Program `:576`, WindowsZombieProcessCensus `:17`. Separate from generic deployment configuration. |
| 6 | Establish Linux RC-watch evidence/support contract | **Needs a card** | Windows probe registration/caught native failure, Program `:599`, WindowsRcBridgeProbe `:57`/`:90`. Conditional Claude RC feature, not startup. |
| 7 | Resolve Unix Herdr disposal parity and remaining Unix peer/read contract | CARD-0864 Backlog; CARD-0872 Backlog for separate hardening | Unconditional CreateFileW and incomplete Unix inspector; stored CARD-0864 23 failed rows; peer PID without UID validation above. Herdr is off in live Linux runner. |
| 8 | Measure the production Linux delivery envelope and record the resulting supported ceilings | **Needs a card** split from CARD-0038 | Unix classification done but CeilingsFor/runner-bound policy still use inbox values; existing envelope instrument is Windows-only. CARD-0037 Done is Windows transport, CARD-0962 is argv activation, neither supplies Linux paste measurements. |
| 9 | Establish Linux orchestrator deny-hook execution contract | **Needs a card** | DenyHookSettingsJson names powershell; Linux image installs pwsh; absence observed. No hook was executed or repaired. |
| 10 | Resolve residual platform assertions and ensure native test execution lanes | CARD-0588 for three Windows fixtures; CARD-0605 for Linux lane; CARD-1114 for Windows audits; **needs a card** for AgentPinWorkspaceStoreTests' two Linux assertions in whole-project Windows CI | Source table and CI selection above. These are distinct admission/automation outcomes, not whole-project Windows-only declarations. |
| 11 | Complete real Docker queue/recovery fixture evidence and remove known fixture reds | CARD-0602 Backlog; CARD-1216/CARD-1218 Backlog | No-op fixture source; five Linux/three additional Windows base-red methods stored on existing cards. CARD-0590 Done does not discharge this acceptance. |
| 12 | Recheck remaining Linux native delivery/adoption and standing-history evidence | CARD-0794 multiline delivery only; CARD-0871 rules adoption; CARD-0878 intermittent native missing target; CARD-0865 standing/channel parity | Current GrokRulesAdoptionTests `:24` retains CARD-0871 skip. CARD-0865 now documents ten failures on Windows too; do not call all thirteen Linux defects. Historical evidence, no new reruns here. |
| 13 | Recheck Linux card-file/launch-refresh fixture portability | CARD-0713 and CARD-0787 Backlog | AgentSystemPromptLaunchTests `:769` and PolicyRefreshServiceTests `:627` still use cmd.exe. CARD-0713 records missing Unix hook modes/junction and remote Windows-path display cases; avoid hiding real display behavior by only changing fixture data. |
| 14 | Qualify the installed SDK/runtime/Git prerequisites and outstanding activation evidence | CARD-0914 for SDK/CI alignment; CARD-0962 for Linux argv rollout smoke; CARD-0628/CARD-0638 for Claude proof | Current inspected/deployed SHAs differ; CI provisioning doesn't establish SDK10; root-server Git floor unmeasured. This does not authorize an upgrade/rollout in this task. |
| 15 | Decide whether positive session memory caps require Linux parity | **Needs a card only if parity is required** | Direct Linux launch throws explicitly; remote projection zeroes the requested cap. Original card allowed explicit unsupported behavior. Do not confuse verification cgroup custody with memory-limit enforcement. |

Board searches used `scripts/card.ps1 search <phrase> -Board Antiphon -All`,
including Linux (217 matches), Docker, portability (8), ceiling (75), JobObject
(9), WMI (5), WindowsRcBridgeProbe (only CARD-0038), memory limit (0),
orchestrator hook (0), deny hook (0), AgentPinWorkspaceStoreTests (0), and
Linux-only deployment (0). The broad CI query returned 864 with explicit page-2
continuation. Full Linux/CI results were retained locally while inspecting the
relevant open titles; selected hits were read with `get`, not treated as full
descriptions from previews. “Needs a card” means no concrete existing owner was
identified in these enumerations; it is not an assertion that every possible
synonym is absent from the board.

## Remaining uncertainties and what resolves them

1. Exact production-coverage boundary of “Linux ready”: caller decision about
   optional Herdr/RC/census/memory-cap parity versus the working uncapped PtyHost
   stack. This is why the handoff is Decide.
2. Current source is newer than both running binaries. Source readiness and live
   activation are separate. Existing rollout owners must supply SHA-bound
   capabilities and owned acceptance evidence; this task did not restart anything.
3. The null live custody advertisement has several possible probe causes
   (root/helpers/NoNewPrivs). No startup log or helper execution was sampled, so the
   failed prerequisite is not identified here. CARD-0604/CARD-0634 need that evidence.
4. Large Unix paste losslessness and Claude Linux binding are not established by
   the 700-character Codex pointer receipt. Required evidence is the production
   Unix path's envelope/binding transcript, tied to actual versions; no ceiling
   value is recommended here.
5. Historical red cards may have additional fixes since their measurements.
   The stored receipts and current skips/assertions justify scoped rechecks,
   not repeating old assembly runs or reporting old numbers as current failures.
6. No fresh full-stack image smoke, live CI/nightly history, root-image Git version,
   provider-auth probe, memory-cap launch or foreign-PID scenario was run.
   Those omissions follow the explicit no-build/no-test/no-mutation brief.

## Not done, noted

Possible fix direction only: give each confirmed Linux feature/admission gap an explicit supported-platform contract; implementation choices and measured ceiling values are deferred to its owner.

--- next stage ---
next: decide
handoff: Split CARD-0038 using the ranked owner table: ordinary Unix PTY and Docker core are shipped, but live Linux custody, optional feature parity, paste measurements and recurring native coverage remain. Reconcile stale CARD-0587/0588/0598 and merge CARD-1212's overlap with 0821/0690; decide the full-production Linux boundary before commissioning new cards.
artifact: docs/investigations/2026-10-13-card-0038-linux-docker-readiness.md
