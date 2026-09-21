# CARD-0590: self-contained Docker stack on server2

Date: 2026-09-21. Stage: Plan; verification remains a separate TestDesign dispatch.
Baseline: `723ac9534fc3fce49b378e287da08b7b11095716`.

## Outcome and scope

Deliver a fresh Antiphon installation on server2 with the server (including the built client),
session-runner and PostgreSQL in containers. Its application connections, state, workspaces and
test execution must not depend on the Windows desktop. Reuse CARD-0490's Linux runner/PtyHost
packaging. Replace the broken root server Dockerfile and deployment Compose file.

Deliver the test capability in two increments: **Small** = image builds, client lint/build/Vitest
and messaging tests; **Medium** = the audited, non-spawning majority of `Antiphon.Tests`, including
portable database/HTTP tests. Both increments are in this plan; Small is the first independently
reviewable checkpoint, not permission to close the card without accounting for Medium.

This is a new database, not migration of the desktop installation. E2E/Chromium, the three native
test assemblies, full nightly parity, live messaging gateways, provider credential provisioning,
Windows stack activation and general Linux product parity remain outside this card. A successful
container build is not evidence that a terminal session launches.

Sources: full live descriptions of CARD-0590, CARD-0587, CARD-0588 and CARD-0490 read on this date;
repository files below; owners `docs/bootstrap.md`, `docs/testing-and-build.md`,
`docs/project-context.md`, `docs/agent-credentials.md`, `docs/session-runtime-invariants.md` and
`docs/orchestration-loop.md`. No build, test or server2 deployment was performed in Plan.

## Ground truth

| Card assumption / claim | Code and recorded evidence at the baseline | Design consequence |
|---|---|---|
| A full server image already exists | Root `Dockerfile` copies only `server/*.csproj` before restore. The server references four sibling projects that are absent. | Replace its restore/publish layout; reproduce the old build failure in Code before fixing CARD-0587. |
| Copying the server folder is sufficient | `server/Antiphon.Server.csproj` references `src/Antiphon.Agents.Pty`, `Antiphon.Messaging`, `Antiphon.Messaging.Client`, `Antiphon.SessionRunner.Contracts`. The recursive project-reference walk at this SHA adds no other server projects. | Preserve the repository-relative directory layout and copy the complete `src` tree, root build inputs and server. Do not maintain four isolated project-copy exceptions that drift. |
| SDK 9 matches the application | The TFM is net9.0, but `global.json` selects SDK 10.0.204 with `latestMinor`; root Dockerfile still uses SDK 9. CARD-0490 uses SDK 10/runtime 9. | Build with a compatible SDK 10 image; execute with ASP.NET runtime 9. No TFM migration. |
| A server assembly is the complete web product | `Program.cs` serves static files and `index.html`; bundles are embedded from `server/Bundles/**/*.md`. | Include built `client/dist` and the bundle sources at build time; check both artifacts in the built image. |
| Existing Compose is a full deployment | `docker-compose.yml` has only server + Postgres, no readiness condition, no runner override or persistent app state. | Add runner, health ordering, application state and explicit Linux configuration. |
| CARD-0490 proves all Linux runtime behavior | Its Dockerfile publishes runner and PtyHost with `-r linux-x64`, then copies the host apphost and `libporta_pty.so`. However `docs/testing-and-build.md` lines 145-153 records V-7 blocked on runner-to-host named-pipe connection. | Reuse packaging, but require a real Linux session smoke. Do not claim the recorded V-7 failure is fixed or redesign its protocol here. |
| Porta is the default everywhere | `PtyBackendPolicy` defaults to the Porta/inbox path, but the runner's shipped `appsettings.json` explicitly requests `modern` and enables Herdr. | Set `SessionRunner__PtyBackend=inbox`, `ANTIPHON_PTY_BACKEND=inbox`, and `SessionRunner__Herdr__Enabled=false` in Linux Compose. |
| Only four server paths need changes | Server settings also contain Windows CLI names, a loopback runner URL and automatically enabled specialist provisions; runner settings contain `C:\\logs\\...`. | Override both processes and disable unattended specialist starts for a fresh test stack. Leave Windows defaults intact. |
| Tests fit the existing build context | Root `.dockerignore` excludes `tests/` and PowerShell files. `Antiphon.Tests.csproj` links sources/fixtures from other test projects and embeds a JSON file under `docs/superpowers/plans`. Messaging tests reference `tools` and `samples`. | A dedicated test Dockerfile and Dockerfile-specific ignore file must include the full test dependency/source-inspection context. Simply allowing one test directory is insufficient. |
| Linux CI proves current Docker commands work | `.github/workflows/ci.yml` declares Ubuntu client/messaging jobs and separate Windows native jobs, but still selects SDK 9 and Node 20; no run results were read. | Treat it as evidence of intended OS split, not a current green result. Use current repository build requirements in the new target. |
| Roughly 20 fake executable lookups need treatment | Case-sensitive search found 52 `fakeclaude.exe`/`fakegrok.exe` occurrences in 18 files. Some are diagnostic text or intentionally Windows-native contracts. `IsolatedSessionRunner.StartProcess` hardcodes the runner `.exe`. | Fix apphost selection at real portable lookup sites, not indiscriminate text replacement. Inventory deferred native-only sites. |
| Four Linux failures belong here | CARD-0588 is Backlog and names `FakeGrokContractTests`, `LandDeliveryFixture`, `OutputDistillationApplyCanaryTests`, and `WorktreeLockDiagnosticsWindowsTests`. | CARD-0588 owns their platform skips. Exclude their native classes from this Linux target until/after that work; skips do not count as Linux coverage. |
| About 620 of 781 files form a portable suite | Current source census is 789 C# files, 183 with a process-limiter/process-start marker. File counts are not test-class counts, and indirect helpers can spawn. | TestDesign must supply a class-level audited roster. The rough remaining 606 files are a sizing hint, not an execution claim. |
| Unit means portable and non-spawning | `TestLaneCategoryGuardTests` guarantees Unit xor Integration, not OS/process safety. `ProcessSpawnLimit` is assembly-local. | Use a separate explicit Linux roster; do not reinterpret existing categories or drop integration tests wholesale. |
| Compose Postgres suffices for backend tests | `TestDbFixtureLifecycle` creates its own `postgres:16-alpine` Testcontainer, then migrates it; it does not consume the app connection string. | Test containers receive a Docker socket; application and test DBs stay separate. Keep existing schema/database isolation. |
| Messaging tests need no Docker | `InboxConsumerServiceTests` and `KafkaConsumerGroupObservationTests` use Redpanda Testcontainers and skip unless `ANTIPHON_BROKER_TESTS=1`. | Small's messaging check explicitly enables these tests and supplies the socket; report any other opt-in exclusions. |
| New installation can reuse desktop secrets/state | Data-protection readiness checks Unix ownership, an external key-ring path and a supported protector; Auto without a certificate is not Linux managed-secret readiness. | Persist a private key ring; offer explicit X509 configuration using mounted files. Baseline smoke uses no provider secrets; never weaken readiness or bake credentials. |
| Health proves the correct image is active | `Directory.Build.props` stamps SourceRevisionId from Git or `unknown`; `.git` must not be sent to the image build. | Pass an explicit full revision build argument into publish and compare `/api/version` to the chosen source SHA during acceptance. |

## Decisions

These are implementation decisions within the brief, not requests for a new product decision.

- **D-1 — Compose services, not one all-in-one process image.** Replace root `docker-compose.yml`
  with server + runner + Postgres on a project-scoped bridge. Keep service key `antiphon` for
  compatibility, add `session-runner`. Three independently restarted services preserve database
  durability and match existing process boundaries. Reject supervisord, bundled Postgres inside
  the server image, and reuse of desktop services.
- **D-2 — Direct HTTP inside the stack.** Server points at `http://session-runner:8080`;
  `PhoneHomeRunner__Enabled=false` and runner `PhoneHome__Enabled=false`. The existing local runner
  route already supports this. Reject phone-home to the desktop, host-network app services and a
  new transport. CARD-0490 remains the remote protocol/lifecycle owner.
- **D-3 — Linux amd64 only for this delivery.** Build server, runner and PtyHost with SDK 10 and
  `dotnet publish -c Release -r linux-x64 --self-contained false`. Runtime remains ASP.NET 9.
  Server2 architecture is a deployment preflight, not an assumed observed fact. Reject Alpine
  for the .NET native runtime and arm64 expansion until native asset qualification exists.
- **D-4 — Keep the proven runner Dockerfile.** Compose builds
  `docker/session-runner-grok/Dockerfile`; make only shared image changes needed by both uses
  (revision stamping, verified native payload, state ownership, required health utility).
  Retain Grok 1.0.34 packaging and its no-baked-auth contract. Do not fork a second near-identical
  runner Dockerfile or change `docker-compose.runner-grok.yml`'s phone-home behavior.
- **D-5 — Dedicated test image, separate from runtime.** Add `docker/tests/Dockerfile` and
  `docker/tests/Dockerfile.dockerignore`, based on SDK 10 with runtime 9, Node 22 meeting the locked
  Vite engine minimum, npm, Git and PowerShell 7. Assert tool versions while building. Keep root
  runtime context's intentional test exclusion. The test image includes tests, scripts, fixture
  JSON/Markdown, `Antiphon.sln`, root build files, `src`, `server`, `client`, `tools`, `samples` and
  source files inspected by selected guards; no host build outputs or credentials.
- **D-6 — Host Docker socket only in explicit test services.** Use sibling Testcontainers on
  server2's Docker Engine, mount `/var/run/docker.sock` into the test container, retain Ryuk, and
  preflight socket permissions and mapped-port reachability. No DinD, privileged service, global
  Docker pruning or app-service socket mounts. The test service is trusted build/test code with
  host-daemon authority; it is separate from agents executing application tasks. Docker Desktop
  has its own optional override for socket source and `TESTCONTAINERS_HOST_OVERRIDE`, never a
  desktop dependency in server2's base configuration. This follows the
  [Testcontainers sibling-container model](https://dotnet.testcontainers.org/dind/).
- **D-7 — Admit tests by reviewed class roster.** Add `tests/linux-test-roster.json`: assembly,
  fully qualified class, included lane/shard or excluded reason/owner. Every discovered class is
  accounted for. Unknown, duplicate, stale, included-native or included-spawning entries fail
  validation. Exclusions must name a concrete boundary, not merely a red result. TestDesign
  supplies the initial roster as a plan appendix or companion artifact under `docs`, with exact
  filters before Code; Code may not silently shrink it.
  Add no attributes to hundreds of classes and do not change nightly's execution policy.
- **D-8 — Baseline native smoke is credential-free.** Use a Raw `/bin/sh` agent with a unique
  marker and bounded input/output/exit checks. This proves actual runner/PtyHost launch and
  communication, not model replies or UserPrompt transcript delivery. An authenticated Grok
  canary is optional, separately provisioned under CARD-0575; no paid-model dependency for green.
- **D-9 — Fresh, inactive automation by default.** Disable specialist provisioning, schedules,
  Hangfire jobs that depend on Windows census, and external channel consumers in this test
  deployment. Keep normal explicit API operations and direct runner events enabled. This avoids
  boot-time attempts to launch desktop `.exe` programs or contact live services. Do not alter
  global production defaults or declare every agent provider installed.
- **D-10 — Durable local state with matching paths.** Use project-scoped named volumes for
  Postgres, server state, runner state and workspace; mount workspace at `/work` in both app
  containers. Containers run as the same explicitly recorded non-root app UID/GID; initialize
  only their owned volume directories before dropping privileges. Reject root app processes and
  host-wide `safe.directory=*`. Existing foreign-owned volumes must fail with a named preflight
  error, not trigger a recursive chown of an operator checkout.
- **D-11 — Health ordering and private boundaries.** Publish only the server's port, default
  `127.0.0.1:5000:8080`; allow an explicit server2 bind-address/port override. Runner and Postgres
  stay unpublished. Server depends on healthy Postgres and runner; install/use an actual HTTP
  health probe in each .NET image. Bare `depends_on` only orders process creation, so use
  `condition: service_healthy` as documented by
  [Docker Compose](https://docs.docker.com/compose/how-tos/startup-order/).
- **D-12 — Preserve the card boundaries.** S1 absorbs CARD-0587 unless it lands first; CARD-0588
  retains its four skip changes; CARD-0038/CARD-0490 own native runtime repair. E2E gets only the
  cheap apphost-name preparation described in S4, with no Chromium/E2E acceptance promise.
  Do not change remote execution, skip assertions, retries or timeouts to make this card green.

## Runtime and build design

Root `Dockerfile` uses a Node client stage, a repository-layout SDK server stage, and an ASP.NET 9
runtime stage. Copy root MSBuild/NuGet inputs actually present (including `global.json`,
`Directory.Build.props` and `src/Messaging.Pack.props`) before publish; whole `server`/`src`
directory copies are acceptable for the first correct version. Prefer correctness over a fragile
project-only cache optimization. `dotnet publish server/Antiphon.Server.csproj ...` runs from
`/src`; the relative project references must resolve there. Copy only publish output and client
`dist` to `/app`; expose/listen on 8080 consistently. Include Git, CA roots and the health probe
needed for supported server operations. The SDK and test tree do not ship in the server image.

Pass `SOURCE_REVISION` as a full SHA from the invoking checkout, use
`-p:SourceRevisionId=<sha>` on server/runner/host publishes, and stamp the OCI revision label.
No build step runs Git against an absent `.git` and then treats `unknown` as acceptance evidence.
The runner retains the explicit host apphost plus `libporta_pty.so` copy and verifies execute
permission; check all referenced host `.dll`/`.deps.json`/`.runtimeconfig.json` payloads exist.

The build-context policy must exclude `.git` **files and directories** (linked worktrees have a
pointer file), `.antiphon`, logs, workspace, `bin`, `bin-*`, `obj`, node_modules, local env/config
overrides, provider homes and auth/certificate files. Keep committed fixture files that happen
to discuss secrets; deny actual stores, not arbitrary test text. Root and test-specific ignore
files each carry the protections they need because the latter supersedes the former. Validate
effective context contents with harmless sentinel fixtures, not actual credential files. Keep
embedded server Markdown and linked test resources; a successful restore alone cannot catch a
missing embedded bundle.

Reproduce the old CARD-0587 build from an owned clean export of tracked files at the baseline,
not the developer's working directory with its ignored local state. Record the actual failing
step; do not assume it reaches restore if an earlier client/SDK failure occurs. The new-image
qualification uses the same clean-source principle plus explicit revision injection.

### Required environment and mounts

| Service | Setting / path | Value and reason |
|---|---|---|
| server | `ASPNETCORE_URLS` | `http://+:8080` |
| server | `ConnectionStrings__DefaultConnection` | `Host=postgres;Port=5432;Database=antiphon;Username=antiphon;Password=<deployment value>`; required external env value, no checked-in password or resolved-config logging |
| server | `SessionRunner__Enabled`, `SessionRunner__BaseUrl` | `true`, `http://session-runner:8080` |
| server | `PhoneHomeRunner__Enabled` | `false` |
| server | `Git__WorkspacePath`, `Git__WorktreeBasePath` | `/work/repos`, `/work/worktrees`; workspace volume mounted read/write in both processes |
| server | `AgentSessions__SessionLogPath`, `ZombieCensus__SessionLogPath` | `/runner-state/session-runner`; runner-state volume mounted read-only here, read/write as `/state` in runner |
| server | `Serilog__LogPath`, `AgentTui__KeyRingPath` | `/state/logs`, `/state/keyring`; key ring outside `/app`, owner-only permissions |
| server | `Delegation__CheckInterpreterWorkingDirectory`, `Delegation__DiagnoseWorkingDirectory` | `/state/check-interpreter`, `/state/diagnose` |
| server | `Delegation__CheckInterpreterEnabled`, `Delegation__DiagnoseEnabled`, `Delegation__OutputDistillerEnabled` | `false` in baseline stack; explicit later provisioning enables these |
| server | `Hangfire__ServerEnabled`, `ZombieCensus__Enabled`, `WorktreeResidue__Enabled`, `Schedules__Enabled`, `ChannelBridge__Enabled`, `Digest__Enabled` | `false` in baseline stack; no live gateway or Windows WMI dependency |
| server | `Agents__Definitions__grok__Exe` | `/usr/local/bin/grok`, executed by the runner, and default definition `grok`; no OAuth state in image |
| server | additional `raw-sh` definition | `Kind=Raw`, `Exe=/bin/sh`, no arguments for the interactive smoke; existing unsupported provider definitions do not become supported by renaming them |
| runner | `ASPNETCORE_URLS`, `PhoneHome__Enabled` | `http://+:8080`, `false` |
| runner | `SessionRunner__SessionLogPath`, `SessionRunner__PtyHostDir`, `Serilog__LogPath` | `/state/session-runner`, `/state/pty-hosts`, `/state/logs`; Linux filesystem volume retains execute bits |
| runner | `SessionRunner__PtyBackend`, `ANTIPHON_PTY_BACKEND`, `SessionRunner__Herdr__Enabled` | `inbox`, `inbox`, `false`; no Herdr/modern-ConPTY fallback dependency |
| runner | `GROK_HOME` | `/state/grok`; starts empty, authentication is optional explicit provisioning |
| postgres | database/user/password and data mount | required deployment password; project-owned `pgdata:/var/lib/postgresql/data`; no published port |

Optional Linux managed-secret configuration uses `AgentTui__KeyProtection__Mode=X509Certificate`
and mounted certificate/private-key paths under `/run/secrets` with existing custody checks.
Auto without a supported protector remains unavailable for managed-secret operations. Do not
claim a persistent directory alone makes this feature ready. Never import the Windows database,
DPAPI material or primary provider home as implicit setup.

`init: true` and normal Compose stop semantics own the runner's container processes. Persisted
runner records survive recreation, but running PTY processes do not survive container removal.
The runbook distinguishes restarting the runner process from replacing its container and requires
draining any non-test sessions before replacement. Do not promise cross-container process adoption.

## Test target design

Add `docker-compose.test.yml` with opt-in test services and a stable foreground entry point
`scripts/test-docker.ps1`. It builds the test image once for the selected source revision, then
runs named groups with `docker compose run --rm --no-deps`. No application service is required
for Small/Medium tests; no application connection string or provider credential is inherited.
The runtime acceptance smoke is separate and starts its own isolated Compose project.

The test container owns its source/build paths and contains a complete clean source tree. Tests
that inspect repository files find `Antiphon.sln`. Use one isolated `bin-c590/` output per producer
project and `dotnet run --project ... --no-build --property:OutputPath=bin-c590/ -- ...` after the
named build. Preserve linked fixture layout. Do not compile the entire solution/AppHost merely
to execute two test projects. Client tests run through `pwsh -File scripts/test-client.ps1` and
emit its JSON results; do not replace it with a pipeline that loses the test process exit code.

The public group interface is `-Group small|backend|all`; internal exact shards come from the
reviewed roster/checkpoint manifest, not free-form shell interpolation. Small includes npm lint,
client build, full client tests, messaging build and messaging tests with broker opt-in. This
assembly-wide messaging/client run is intentional qualification of new image/tool/native-library
boundaries, not the ordinary policy for a narrow source change. Backend includes portable pure
logic, database and in-process HTTP classes, with exact combined class filters chunked to keep
foreground runs bounded. Preserve the existing `ProductionRunnerGuard` dead URL and refusing client.

For each group/shard export fresh TRX or Vitest JSON plus a machine-readable summary: source SHA,
image IDs, selected roster hash, commands, exit codes, expected/executed/passed/failed/skipped
counts and elapsed time. Missing results, zero executed tests, lost classes, unexpected skips and
an exit/result mismatch are failures. Discovery/list-tests is only inventory, never execution.
Results must be copied to the caller-selected evidence directory before removing the test
container; do not mount the whole developer checkout read/write to collect reports.

Default server2 uses local Docker Engine discovery of the mapped Testcontainers endpoints.
Preflight performs an actual disposable DB connection, not just `docker version`. If that
topology cannot reach mapped ports, fail with the observed endpoint and require an explicit
tested host override; do not silently use localhost or the desktop. Keep test-specific containers
and resources under the existing Testcontainers owner/reaper. Socket absence/denial is a clear
preflight failure, never a reason to skip DB or broker tests and report green.

### Portability triage

1. Preserve the baseline `.exe` failure evidence, then make E2E's `IsolatedSessionRunner` choose
   `Antiphon.SessionRunner.exe` on Windows and `Antiphon.SessionRunner` elsewhere, including its
   error message. Its copied apphost must actually exist and be executable. This is preparation;
   browser E2E remains excluded.
2. Introduce a small shared test apphost-path helper for portable fake lookups, resolving sibling
   output `fakeclaude/fakeclaude[.exe]` and `fakegrok/fakegrok[.exe]`. Retain producer output lookup
   and copy rules in project files. Test with existing/missing files and platform choices.
   Do not change expected literal Windows command lines or platform-specific fake contracts.
3. The 18-file search inventory includes 10 `Antiphon.Tests` files (two helpers and eight
   application test files), two E2E files and six `Antiphon.Agents.Pty.Tests` files. TestDesign must
   map each actual lookup to portable fix or native-only exclusion; occurrences are not 52 fixes.
   Reclassifying a fixture as portable requires inspecting its helpers and child-launch behavior.
4. CARD-0588 handles its four assertions/skips independently. This target never relies on those
   classes silently returning early; excluded class names and their owner remain in the roster.
5. Run the frozen Medium roster once on Linux. Triage failures against the same base test on
   Linux before attributing them to new source. Small path/tool/config repairs fit this card;
   Windows runtime redesign does not. Any inability to reach the non-spawning majority is a
   reported dependency/decision with measured class counts, not a smaller renamed success.

## Implementation slices

Each slice is committed and pushed with its real verification state before long runs. TestDesign
will append executable V/R cases, exact class roster and the closed checkpoint table; the following
names are the required behavioral coverage and proposed test ownership, not a completed test manifest.

| Slice | Files | Work and exit evidence |
|---|---|---|
| **S1 — server image / CARD-0587** | `Dockerfile`, `.dockerignore`; new `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs` | Reproduce old root build; fix repository layout, SDK/RID, static client, bundles and SHA stamp. Actual clean-context image build; image artifact inspection for DLLs, bundle resources, client assets and no test payload. Contract tests guard copy graph and context policy. If CARD-0587 lands first, consume that commit and only add missing requirements. |
| **S2 — three-service deployment** | `docker-compose.yml`, `docker/session-runner-grok/Dockerfile`; new `docker/stack.env.example` with placeholders; extend `DockerStackContractTests.cs` | Direct internal runner route, health ordering, Linux env, non-root state initialization, private services and persistent volumes. Preserve phone-home image use. Parse/render Compose with dummy values, then start a fresh project and prove server/static assets/version/DB + runner capabilities. |
| **S3 — Small test target** | new `docker/tests/Dockerfile`, `docker/tests/Dockerfile.dockerignore`, `docker-compose.test.yml`, `scripts/test-docker.ps1`, `scripts/test-docker-container.ps1`; new `tests/Antiphon.Tests/Scripts/DockerTestCommandTests.cs` | Build a complete test context, run client lint/build/Vitest and messaging with opt-in broker tests; validate socket preflight, nonzero execution and failure propagation. Tests drive a fake command boundary with actual result fixtures, not string self-comparison. |
| **S4 — portable apphost lookup** | new `tests/Shared/TestAppHostPath.cs`; csproj links only where consumed; `tests/Antiphon.E2E/Fixtures/IsolatedSessionRunner.cs`; audited portable lookup sites from `rg -l -F -e fakeclaude.exe -e fakegrok.exe tests`; new `tests/Antiphon.Tests/TestHelpers/TestAppHostPathTests.cs` | Centralize OS suffix and missing-file diagnostics where portability is intended. Prove both platform filename branches with fake files plus native Linux apphost existence. Keep native-only contracts and CARD-0588 files out of the edit set. Exact consumer inventory is a TestDesign prerequisite. |
| **S5 — Medium roster and execution** | new `tests/linux-test-roster.json`, `tests/Antiphon.Tests/TestHelpers/LinuxTestRosterTests.cs`; test wrapper and manifest tooling from S3 | Complete class accounting, audited included classes/shards, explicit native/process/opt-in exclusions; run portable majority with real DB fixtures. Existing `TestDbFixtureIsolationTests`, `ProductionRunnerGuardTests` and selected `HealthEndpointTests` are required coverage anchors. Do not change their production safety guards. |
| **S6 — stack acceptance and operations** | new `scripts/verify-docker-stack.ps1`, `tests/Antiphon.Tests/Scripts/DockerStackSmokeCommandTests.cs`, `docs/docker-stack.md`; update `docs/bootstrap.md` and `docs/testing-and-build.md` with links | Foreground isolated smoke with actual Raw session, identity/path checks, DB persistence across recreation and owned-resource cleanup. Run the same committed artifacts on server2 with desktop-independent endpoints. Document build/start/test/stop, retained volumes, per-project cleanup and optional provider setup. No restart/deploy of the desktop stack. |

S1 -> S2 and S3; S3 -> S4 -> S5; S2 + S5 -> S6. Small completes after S1-S3;
overall card acceptance also needs Medium accounting/results and S6. No new test framework,
generic container scheduler, global category migration or platform abstraction in production code.

## Guard and positive-control requirements for TestDesign

The brief asks for PCs/guards but does not fold TestDesign into Plan. The following inventory is
mandatory input to that stage. It must turn each row into a named, method-scoped red/restore/green
control and supply the precise mutation/assertion before Code is dispatched. Ordinary Docker
build/smoke evidence supplements these guards; source inspection alone cannot prove runtime behavior.

| Guard | Required failure injection / proposed test seam |
|---|---|
| G-1 / PC-1: complete server project graph in build context | Remove a needed source COPY/include; `DockerStackContractTests.Server_project_graph_is_available` rejects the missing referenced project. Ordinary root Docker build confirms the real graph works. |
| G-2 / PC-2: shipped client and embedded instructions | Remove client-copy or bundle-context inclusion in separate variants; `Runtime_payload_includes_client_and_bundles` must name the missing artifact. Inspect actual publish output in ordinary acceptance. |
| G-3 / PC-3: native host payload | Remove the Linux host or native-library copy in separate variants; `Runner_payload_contains_linux_host_and_native_library` fails. Actual session smoke remains mandatory. |
| G-4 / PC-4: revision identity | Drop/replace revision propagation; `Published_revision_matches_requested_source` rejects unknown/mismatched server response, using response fixtures at the wrapper boundary plus live ordinary `/api/version`. |
| G-5 / PC-5: no desktop application route | Replace internal runner/DB host with localhost/desktop address; `Stack_routes_stay_inside_compose` fails on resolved configuration. |
| G-6 / PC-6: no public runner/database port | Add a runner or DB published port (separate variants); `Only_server_has_a_published_port` rejects each. |
| G-7 / PC-7: socket confined to test services | Mount the socket into server or runner (separate variants); `Docker_socket_is_test_only` rejects each. |
| G-8 / PC-8: context custody | Remove a required deny rule for a worktree `.git` pointer, `.antiphon`, a local env/auth file, or alternate output (separate variants); context-sentinel tests catch each leak in both context policies. Never use actual secrets. |
| G-9 / PC-9: tests cannot silently disappear | Drop a required class/result file or return zero execution (separate variants); `DockerTestCommandTests.Missing_or_empty_execution_fails` rejects it. |
| G-10 / PC-10: original test exit code reaches caller | Make the command/result adapter hide nonzero status; `Nonzero_test_exit_is_preserved` must fail even with plausible success output. |
| G-11 / PC-11: roster stays portable and exhaustive | Add an unclassified class, include a known spawner/native class, or drop a discovered class (separate variants); `LinuxTestRosterTests` rejects each without launching a process. |
| G-12 / PC-12: test environment never reaches app runner | Remove the dead-runner/refusing-client requirement from the command boundary; `Test_environment_refuses_application_runner` fails. Existing `ProductionRunnerGuardTests` still execute in the ordinary backend lane. |
| G-13 / PC-13: teardown acts only on its owned Compose project | Substitute a foreign project label or teardown target; `DockerStackSmokeCommandTests.Foreign_project_cleanup_is_refused` proves no delete command was emitted. |
| G-14 / PC-14: failed session smoke cannot become healthy-stack success | Supply health=green but missing native marker/exit; `Healthy_containers_without_session_evidence_fail` rejects it. |
| G-15 / PC-15: Linux state settings are usable | Remove a required absolute path, inject a Windows path, enable Herdr/modern, or lose non-root writable state (separate variants); resolved configuration and state preflight tests identify the defect. |
| G-16 / PC-16: missing Docker is not a skipped test success | Deny socket/preflight connection while result fixtures report skipped DB/broker tests; `Unavailable_test_daemon_fails_before_execution` rejects the run. |

TestDesign must split independently bypassable guards into distinct PCs where these grouped
requirements need it; do not treat this preliminary 16-row inventory as permission to omit a
variant. It also supplies normal Windows/Linux filename tests for S4 and regression cases for
retained state/startup order. This work changes packaging and harnesses, not asynchronous delivery
protocols; the delivery inventory should identify the existing native session path exercised by
the smoke and explicitly distinguish terminal output from provider transcript confirmation.

Mutation runs after ordinary Review and land under the existing SourceLanding rules. Do not hand
the immutable snapshot to a remote Docker daemon/server2 or a standing local daemon. Design PC
methods as local inherited test processes with fake command/result boundaries and file/config
guards; they must not initialize Testcontainers. Real Docker runs belong to ordinary Code/Review
qualification of committed source, not an unauthorized sourced-mutation executor. If a necessary
control cannot fit that custody model, TestDesign returns the unverified boundary for a decision
instead of silently downgrading its evidence.

## Acceptance, dependencies and handoff

Required ordinary acceptance outcomes:

1. Fresh clean-context server and runner image builds succeed with a recorded source SHA;
   server includes current static assets/instructions, runner includes executable Linux natives.
2. A named Compose project starts all three services, migrations complete, UI/API answer and
   `/api/version` reports the intended SHA. No application endpoint points to the desktop.
3. A credential-free Raw session traverses server -> runner -> PtyHost, returns its unique output
   marker after explicit input, and terminates. Health-only success and fake transport receipts
   do not meet this criterion. Record process/session identity and teardown result.
4. A DB/API-created sentinel survives server/Postgres container recreation with the same volumes;
   workspace writes made in either app process are visible at the same absolute path in the other.
5. Small and Medium execute their frozen rosters with nonzero counts, zero unexplained failures,
   explicit exclusions/skips, fresh exported evidence, preserved failure exit status and no live
   provider/broker usage. Record actual majority coverage by classes, not the historic file estimate.
6. Server2 runs the same entry points using only server2 Docker/state/network resources; no SSH or
   service call back to the desktop. An operator's local CLI may initiate deployment, but application
   runtime must remain independent after that CLI exits.
7. Normal `down` removes only the selected project's containers/network and preserves app volumes.
   A smoke's uniquely named disposable volumes may be removed only after ownership verification;
   reusable deployment volumes require a separate explicit destroy operation. No `system prune`,
   no deletion of `antiphon_pgdata`, no other project's resources. Testcontainers clean up their own
   temporary DB/broker resources; image cleanup targets only task-owned tags/IDs.

The next stage is **TestDesign**, not Code. It must read the touched fixtures/helpers, freeze the
portable roster and lookup-site inventory, append `## Verification design` with Inspection,
Delivery inventory, V/R, complete guard/PC mapping, exclusions, the `### Checkpoints` closed list
and numeric ordinary/Mutation costs. Group build/test invocations to reuse frozen outputs.
Separate Small and Medium checkpoint rows and split large backend groups into bounded exact
class filters. Include the Windows regression tests relevant to the shared helper only; never
schedule the three native assemblies as a Linux full-suite requirement.

Coordination facts at Plan time: CARD-0587 and CARD-0588 are Backlog; CARD-0490 is Review and its
packaging is already in this checkout. Re-read their landing/status before touching shared files.
The Linux pipe-connect failure is recorded inherited evidence, not a newly reproduced defect.
Do not close this card as working end to end if it reproduces; report the exact launch/connect
failure to the native owner while retaining completed image/test slices. No fresh investigation
dispatch is needed to write this plan.

Execution preconditions to record in TestDesign/Code: server2 Docker Engine/Compose versions and
amd64 architecture, available resources, owned deployment/evidence paths, explicit Docker context,
required deployment password supplied without logging, and the latest native-blocker disposition.
They were not probed in Plan. Missing server2 access blocks that acceptance row only; it must not
be reported as a passed deployment or prevent preparing the concrete images/tests locally.

Estimated implementation sizing (not a checkpoint budget): Small roughly 2-4 hours including clean
image builds and harness work; Medium another 3-6 hours including class census and one Linux pass;
native runtime repairs excluded. TestDesign replaces these planning ranges with measured/estimated
per-checkpoint minutes and separate PC cost before Code. Existing evidence is insufficient to
claim a current test count or green Linux baseline.
