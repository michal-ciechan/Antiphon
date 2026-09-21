# CARD-0590: self-contained Docker stack on server2

Date: 2026-09-21. Stage: Plan; verification remains a separate TestDesign dispatch.
Source baseline: `723ac9534fc3fce49b378e287da08b7b11095716`.
Continuation `e7f50f48` incorporates TestDesign commit
`1d6a213a65a32b6c04861e17f5d8f4e644171fe7` (source unchanged). **Return to TestDesign**;
the historical rejected assessment below is not an executable verification manifest.
Continuation `04b55418` amends S6 against TestDesign tip
`7a18aca2358c5a4cf6fbf07d54f33f220be94ebd` on `feat/card-task-806c2e25`.
The ordinary observation/cut contract below supersedes that tip's missing-seam assessment.
The amendment is on `feat/card-task-04b55418` because the source branch is checked out in
another worktree. Production source is unchanged. **Next: test-design**, including the full
portable roster, independent guard/PC mapping, checkpoints and costs; Code is not admitted yet.

## Outcome and scope

Deliver a fresh Antiphon installation on server2 with the server (including the built client),
session-runner and PostgreSQL in containers. Its application connections, state, workspaces and
test execution must not depend on the Windows desktop. Reuse CARD-0490's Linux runner/PtyHost
packaging. Replace the broken root server Dockerfile and deployment Compose file.

The selected topology is brief candidate **(a)**: an explicitly enabled ordinary testing runner
on server2 can launch a session that creates, tests and removes its own throwaway stack using
server2 Docker/Testcontainers. This delivers the session-created-stack objective for ordinary
V/R and general testing. **It does not run a SourceLanding Mutation battery.** Post-land Mutation
continues on the supported Windows custody path with only local inherited test processes.
Linux SourceLanding custody is tracked separately as **CARD-0598**; CARD-0594's pipe fix is an
independent dependency. Neither container health nor ordinary Docker evidence is PC-clean proof.

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
| Giving only test services Docker access satisfies a session-created stack | Original D-6 forbade runner socket access; the reused runner image has no Docker CLI/Compose or PowerShell. | Add an explicit ordinary-session-testing image target/Compose override with those tools and socket authority. A host CLI invocation alone does not satisfy acceptance. |
| CARD-0594 unlocks Linux Mutation | `SourceLandingAdmission.RequireSupportAsync` requires `VerificationCustodyV1`, `windows-job-v1`, and a runner-store ID. `SessionRunnerRuntime.VerificationCustodyBackend` and `Program.cs` advertise this only on Windows modern ConPTY. | CARD-0598 must supply a separately qualified Linux backend before Linux SourceLanding can be admitted. Keep all refusals in CARD-0590. |
| Changing the admission capability is sufficient | `RunnerCustodyLedger.PrepareStart` independently reserves the binding, writes `unsupported.json` and refuses non-Windows/non-modern verification launches. | Admission and native launch must be implemented together by CARD-0598; no advertised-only capability. |
| A fresh Docker container is a fresh inherited executor | The current native seam `IPtyCustodyNative` uses Windows job membership and active-process accounting. `VerificationHostIdentity.ContainerId` identifies that original native custody container, not a Docker ID. Docker-launched siblings are controlled by the standing daemon. | Per-dispatch Docker freshness, labels, setsid and root exit do not prove inherited custody. Candidate (b) alone is rejected. |
| A Raw shell response proves delivery/custody | Raw has no provider UserPrompt transcript or verification binding. `HostCustodyJournal` requires sealed descendant-zero accounting and drained output before producing receipt bytes. | Keep Raw native launch as one gate, add a distinct complete-UserPrompt queue probe, and make no SourceLanding claim from either. |
| Runner filesystem paths can be bind-mounted into sibling containers | Docker resolves bind sources on its daemon host, not inside the CLI container. `/work` is a named-volume mount in the runner. | Send clean build contexts through Docker's client, use named volumes for child state and `docker cp` for evidence. Do not bind the runner's `/work/...` as a host path. |
| An idle message POST returns the submitted queue identity | `SessionEndpoints` passes body/mode only. `EnqueueAsync` commits, then may deliver inline. `BuildQueueDtoAsync` selects Pending only; `C475_AlreadyIdleWhenIdleHasRecipientReceipt` explicitly expects an empty response. | Keep that contract. Observe the owned database across all statuses, separately from POST/GET. |
| A returned pending row supplies the attempt floor/generation | `QueuedMessageDto` omits them. `SessionQueuedMessage.LastDeliveryBaselineSequence` and `LastDeliveryGeneration` are committed with each typed attempt; a never-attempted Pending row legitimately has neither. | Export the actual committed tuple, preserving nulls and each attempt separately. Never substitute a pre-POST transcript maximum. |
| Landing failpoints also cut ordinary messages | `afterLandQueueInsert` and `queue-before-typing`/`queue-before-verdict` are gated by `SourceLandNotificationId`; ordinary UI POST has no notification binding. | Do not reuse those hooks or manufacture a task/notification. Use test-host DI interceptors and a forwarding runner client. |
| New production queue hooks are necessary | `C475_QueueCommitAndTransportRecovery` already uses `SaveChangesInterceptor` and a forwarding `ISessionRunnerClient`. Ordinary Ui enqueue has no completion transaction; its attempt save precedes input, and verdict save follows `DeliverAsync`. | Existing EF and runner interfaces can expose the required cuts without changing server source or public DTOs. Prove commit visibility from a second connection before reporting a reached cut. |
| One failed transcript save guarantees no receipt is stored | `AgentSessionRuntime.PersistTranscriptAsync` retries a failed batch individually and can persist a stub; both SSE and catch-up pulls can ingest. | Gate every route for the selected native UUID. The save-failure fixture must also cover individual/stub retries until explicitly disarmed. |
| Native transcript sequence equals the persisted attempt floor domain | Runtime deduplicates by UUID/kind and can rebase runner sequences when saving. `TranscriptEntry` has no generation column. | Compare the floor with server-stored sequence, join native and stored records by UUID/kind/body, and independently verify the unchanged accepted generation. |
| Canceling a blocked delivery is a hard crash | Queue cancellation/transport exception handlers revert Sent rows. A container replacement also says nothing about the surviving recipient PTY. | Hard-cut only the owned fixture-server process at an exported barrier, retain DB and runner, and verify identity before recovery. Test graceful failure separately. |

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
  runner Dockerfile or change `docker-compose.runner-grok.yml`'s phone-home behavior. Add an
  opt-in socket-free `receipt-probe` target from the shared runtime base, then `session-testing`
  from that target; keep ordinary runtime as the default/final target so existing builds do
  not acquire test tools or FakeGrok.
- **D-5 — Dedicated test image, separate from runtime.** Add `docker/tests/Dockerfile` and
  `docker/tests/Dockerfile.dockerignore`, based on SDK 10 with runtime 9, Node 22 meeting the locked
  Vite engine minimum, npm, Git and PowerShell 7. Assert tool versions while building. Keep root
  runtime context's intentional test exclusion. The test image includes tests, scripts, fixture
  JSON/Markdown, `Antiphon.sln`, root build files, `src`, `server`, `client`, `tools`, `samples` and
  source files inspected by selected guards; no host build outputs or credentials.
- **D-6 — Explicit ordinary-session Docker authority.** Base Compose gives neither application
  service a Docker socket. Add `docker-compose.session-testing.yml`, selecting the runner image's
  `session-testing` target and mounting `/var/run/docker.sock` into that runner. Its launched
  ordinary sessions can invoke Docker/Compose and the S3 scripts; test containers also receive
  the socket for sibling Testcontainers, with Ryuk retained. The server never receives it.
  This is a trusted test installation with host-daemon authority, not per-session security
  isolation. Record/validate the socket GID and supply that supplemental group to the non-root
  runner/test service; do not chmod the socket world-writable. Preflight actual mapped-port
  reachability. Reject DinD, privileged services, global pruning and any SourceLanding snapshot
  access. Desktop-specific overrides remain optional. This uses the
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
  communication. Also launch a Raw test-command session that executes S3/S6 in the runner, and
  require a separate queue-to-transcript probe with a native Linux FakeGrok in the test-only
  image. The latter records the whole matching UserPrompt after its attempt floor; shell output
  cannot substitute. These are distinct acceptance gates, none proves verification custody.
  FakeGrok's real Linux launch/terminal mode must be qualified, not assumed from its net9.0 TFM.
  An authenticated Grok canary remains optional under CARD-0575; no paid model is required.
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
  retains its four skip changes; CARD-0594 owns the recorded Unix pipe/native acceptance repair,
  CARD-0598 owns Linux custody, and CARD-0038/CARD-0490 retain their broader platform/protocol scope.
  E2E gets only the cheap apphost-name preparation described in S4, with no Chromium/E2E
  acceptance promise.
  Do not change remote execution, skip assertions, retries or timeouts to make this card green.
- **D-13 — Choose candidate (a), not a custody exception.** Ordinary server2 testing and
  SourceLanding Mutation are separate execution paths. A persistent server2 runner used as an
  external executor for a sourced snapshot violates the current local-inherited-execution rule;
  it is not made acceptable by starting a new session. The rule does not forbid the established
  standing runner *control plane* itself: today's Windows runner launches a fresh bound worker
  under a native original observer. Server2 currently lacks that supported producer. Do not
  reinterpret a standing session as the required fresh Worker/Mutation/Worktree.
- **D-14 — Reject candidate (b) as a shortcut.** A per-Mutation Docker container still delegates
  execution to a pre-existing daemon and supplies neither supported admission nor native
  descendant receipts. Fresh task/worktree is necessary but insufficient. Candidate (c), a local
  Linux observer with proven non-escaping containment and complete receipts, belongs to CARD-0598.
  Even that backend would not automatically authorize Docker siblings; any such extension needs
  its own custody/contract review. Do not rename a Linux backend `windows-job-v1` or remove guards.
- **D-15 — Keep session orchestration concrete and foreground.** S3/S6 provide a committed
  script entry point a launched test session executes, with a persisted run manifest, fresh child
  project and evidence export. No new scheduler, result broker or background worker is introduced.
  The caller awaits the session result. Interruption is incomplete evidence with named residue,
  not success, and cleanup is an explicit retry against the recorded owner.
- **D-16 — Preserve delivery semantics without inventing Mutation delivery.** The ordinary
  canary sends the completed run's sanitized summary through the existing server message queue
  to an isolated native FakeGrok recipient. Correlate its message/session/generation and full
  received body. Do not alter production completion/outbox semantics or claim a SourceLanding
  result was delivered. TestDesign replaces the rejected Linux-Mutation handoff obligation with
  this ordinary probe and keeps the existing queue's relevant recovery regressions.
- **D-17 — Observe the owned database; preserve the public API.** A test-only observer discovers
  the exact ordinary Ui row in `SessionQueuedMessages`, including Sent/Canceled, using the
  frozen recipient, queue high-water mark and unique full body. Read the actual attempt tuple
  and stored/native receipt, then export them. Reject a submitted-row API extension, arbitrary
  SQL/connection-string arguments, Mode.Now, and artificially busy-only coverage: none is needed
  to fix test observation, and the latter two lose required queue behavior.
- **D-18 — A test-only host owns cuts through existing interfaces.** Add a small console fixture
  project using `WebApplicationFactory<Program>` with real Kestrel, following the existing E2E
  factory's host construction. Replace only test instrumentation via DI: EF save interceptor,
  transparent runner decorator, and a selected-response barrier. No new production failpoint,
  settings flag, endpoint, migration or change to Program/queue/runtime is planned. Reject
  extending `LandDeliveryBoundary` with fictitious task IDs and test code in runtime images.
- **D-19 — Prove committed cuts, then kill only their owner.** File barriers carry run/case,
  database/session/row/attempt and host-incarnation identities. A second connection confirms
  commits before a barrier is ready. The foreground controller explicitly releases, injects
  the named failure, or hard-kills the owned fixture server; elapsed time never releases a cut.
  Reject throwing an exception and suppressing the queue's revert as a substitute for a crash.
- **D-20 — Retain both native and server evidence.** A receipt is the complete native FakeGrok
  UserPrompt plus its matching persisted UUID/body after the committed server sequence floor,
  in the same accepted generation. Record each attempt rather than overwriting its floor.
  Reject screen-only success, a favorable verdict alone, prefix matching, synthetic transcript
  writes and treating runner sequence numbers as server sequence numbers.
- **D-21 — Instrumented recovery and stock-image acceptance are separate required runs.** Run
  no-cut idle/busy delivery against the stock server plus read-only observer, then the recovery
  cuts against the fixture host built from the same SHA. Add a socket-free `receipt-probe`
  runner target containing FakeGrok; `session-testing` may derive from it and add Docker tools.
  Only the parent test-session runner gets Docker authority. Reject requiring the fixture host
  or Docker tools in the deployment image, or claiming a fixture-host pass qualifies that image.
- **D-22 — Resume observation, never blind resubmission.** Persist expectation before POST and
  recover the row independently of its response. Unknown acknowledgements keep delivery
  incomplete until the original request's owner has ended and database/runner evidence is read.
  Zero rows is not immediate permission to POST again; duplicates/identity drift refuse success.
  Explicit retry after proved insert refusal is a separately recorded submission. Existing queue
  recovery reuses the row. This does not introduce an idempotency key or an exactly-once promise.

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
| runner, ordinary testing override only | socket, tools and group | `/var/run/docker.sock`, explicit socket supplemental GID, Docker CLI + Compose plugin, PowerShell 7, Git and archive tools; scripts available from the committed `/work/repos` checkout |
| runner, receipt-probe/session-testing targets only | FakeGrok | Publish `src/Antiphon.FakeGrok` as executable linux-x64 apphost with its complete runtime payload under `/opt/antiphon-tests/fakegrok`; no credentials; test definition only; receipt-probe has no Docker tools/socket |
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

### Session-created throwaway stack (ordinary testing only)

Use the base three-service stack plus the explicit session-testing override as the parent.
The parent remains on server2 after the initiating desktop CLI exits. Ordinary sessions in its
runner can execute `pwsh -NoProfile -File scripts/test-docker.ps1 -Group all -ThrowawayStack`
from a committed checkout in `/work/repos`. `-ThrowawayStack` is a proposed S3 entry point, not
an existing command. It runs foreground and delegates stack operations to the S6 helper.
Qualify this invocation through a real server-launched Raw command session, not `docker exec`
or a host-side script pretending to be that session. The interactive Raw challenge remains a
separate native-input check. General agent sessions can use the same foreground entry point.
Keep the S6 wrapper modes explicit: parent qualification launches/awaits the session; the
session's S3 command invokes S6 only in child-probe mode against its recorded child project.
Child-probe mode must never start another parent qualification or recursively launch S3.

The helper's required behavior:

1. Read the session ID/accepted generation from the launch fixture and freeze a random run ID,
   committed source SHA, tool/image identities and exact Compose files. Write a manifest under
   the parent's owned `/work/test-evidence/<run-id>` before creating resources. Parent project
   identity and child project identity must differ. Do not accept an arbitrary cleanup prefix.
2. Export tracked files at the selected SHA into an owned clean context, excluding all local
   state. Stream/build that context through the Docker client; stamp the SHA explicitly. Source
   exports are ordinary committed source, never a sourced Mutation snapshot or its copied bytes.
   Test image includes the clean sources; SDK/Node remain there, not in the runtime target.
   Admit source only from the configured ordinary checkout root under `/work/repos`, outside
   managed verification trees. For a task-bound session inspect its authoritative task record
   and reject `sourceLandingOperationId` before any Docker call; an unreadable task binding is a
   refusal, not an ordinary classification. The isolated Raw acceptance session is explicitly
   ordinary/unbound. These checks prevent accidental misuse; host socket authority is not a
   sandbox, and a copied snapshot remains prohibited regardless of filename or clean Git state.
3. Start a fresh child `c590-<run-id>` Compose stack using its own named state/workspace volumes
   and network. Use a throwaway override with no host-published ports, unique image tags and
   run-owner labels. The child uses the base runtime runner, without recursive session-testing
   socket access. Run a short-lived probe on the child's network for UI/API/version/native
   checks. Test services get only their explicitly required socket and volumes.
4. Execute Small and the frozen Medium roster from the test image. Ordinary DB/broker tests
   launch their own Testcontainers, separate from the child's application DB. Record both
   lifecycles and require real DB/broker connections. The session waits for every command and
   preserves failing exit codes. No test configuration targets the production runner or desktop.
5. Keep test/probe containers until their reports have been copied out using `docker cp` into
   the parent's evidence directory. Hash artifacts and atomically record counts, image IDs,
   session/generation, child resources and outcomes in the run manifest before removal.
6. In `finally`, inspect exact recorded resource IDs and owner labels before removing the
   disposable child containers/network/volumes and owned test resources. Never apply child
   teardown to the parent or another run. Preserve evidence and a resource list after any
   failed/uncertain cleanup; a retry rechecks those same identities. Ordinary deployment `down`
   still preserves its reusable volumes. No global prune, automatic orphan sweep or adoption of
   an unrecognized project. Testcontainers keep their normal owned disposal/Ryuk behavior.

Sibling containers cannot bind a path from the runner's mount namespace as though it were a
host path: [Docker bind mounts](https://docs.docker.com/engine/storage/bind-mounts/) resolve on
the daemon host. Named child volumes, transmitted clean build contexts and copied-out reports
avoid that mismatch. Do not make the parent checkout a broad read/write bind to collect results.
The sibling Testcontainers model is documented in
[Testcontainers for .NET](https://dotnet.testcontainers.org/dind/); the actual server2 endpoint
and permission preflight still decides whether this deployment works.

### Separate native receipt probe

Package native FakeGrok in the test-only `receipt-probe` runner target, also inherited by
`session-testing`, retaining production Grok's defaults. `receipt-probe` has no Docker tools or
socket; this allows the disposable recipient stack to use FakeGrok without recursive authority.
Use an isolated test definition with `Agents__GrokCredentialProbeEnabled=false` only in this
credential-free test override, isolated `GROK_HOME`, and enabled transcript confirmation.
Reuse the supported FakeGrok transcript format, not the Windows-only LandDelivery fixture.
If needed, a test-only Linux launch wrapper sets the PTY's raw mode and then `exec`s FakeGrok;
its executable/terminal/transcript behavior is a real Linux qualification row, not a fake-file
assertion. No general native-runtime rewrite or production credential-probe bypass fits here.

After the session-created run completes, the verifier submits its exact sanitized summary body
(head/tail markers, run ID, SHA, outcome and evidence digest) through
`POST /api/sessions/{recipient}/messages`. Read the recipient's actual transcript and require
the complete matching UserPrompt after that attempt's floor in the same accepted generation.
Never write transcript files to manufacture the receipt. Persist expected body, queue identity,
attempt floor and observed transcript sequence. Cover eligible and busy-then-eligible recipients
through the real queue; TestDesign must identify relevant enqueue/receipt-persistence recovery
cuts and the existing regression owners. A delivered failure report is still a failed test run.

The foreground verifier owns this acceptance probe, not a new durable completion service. It
writes `result-ready` and the immutable expectation before posting, then obtains the queue
identity from the owned database observer below, independently of the pending-only response.
On crash/unknown POST acknowledgement, record incomplete delivery; inspect existing queue and
transcript before any explicit retry. Do not promise automatic exactly-once submission across
that gap or blindly post again. Existing queue rows retain their identity during native queue
recovery. No parent task settlement/SourceLanding notification is synthesized for this probe.

### S6 ordinary observation and recovery-cut fixture (04b55418)

This is the selected implementation design, not an executed verification manifest. It closes
Q-2/Q-3/Q-5/Q-6's design gaps in the historical TestDesign v2 assessment. TestDesign still owns
the complete roster, exact tests/PCs, checkpoint commands and cost estimates. All nine acceptance
outcomes and the ordinary server2 / Windows-local Mutation boundary remain required.

**Placement and execution.** Add `tests/Antiphon.DockerStack.Fixture/` as a test-only console
project with `serve` and `observe` modes. `serve` boots the real server Program through a local
Kestrel WebApplicationFactory; `observe` never boots Program or a runner. Use the existing
`AntiphonAppFixture.KestrelWebApplicationFactory.CreateHost` technique, with an explicit
container content root/web root and internal listen address. Do not import its Windows Raw
definition, mock executor, disabled health registrations, desktop endpoints or whole E2E
assembly. Normal migration, routes, queue service, runtime transcript persistence, delivery
settings and direct runner implementation remain real. Health/version checks remain enabled.

Publish this executable in a named `delivery-fixture` target of `docker/tests/Dockerfile`,
including the server payload, settings, static client and embedded bundles from the same clean
source/publish inputs. It is absent from the root server image and default runner image.
`docker-compose.delivery-fixture.yml` is a test-only override for the server entry point/image,
socket-free `receipt-probe` runner, private control/evidence volume and observation service.
No HTTP fault/admin route is added. Public message requests have only ordinary Body/WhenIdle.
Runtime image inspection must prove fixture dependencies and FakeGrok are absent there.

The S6 foreground controller launches a dedicated disposable receipt stack per case, such as
`c590-q-<run>-<case>`, distinct from both the parent and the Small/Medium child. Record all its
container/network/volume IDs before use; use the existing S6 ownership/export/cleanup rules.
This avoids interrupting the parent session that runs the controller. The no-cut cases use the
stock server image with the observer and test runner; cut cases substitute the fixture host.
All receipt cases carry the original run's already exported sanitized result, not a fabricated
successful Small/Medium result. A complete failed-run summary still reports a failed run.

The fixture's services receive an immutable case identity file via their private named volume.
No caller-supplied database URI, raw SQL, server origin, PID or cleanup prefix is accepted.
The controller derives network endpoints from exact inspected Compose resource IDs and labels,
then verifies `/api/version`, DB identity and runner/session generation before arming. An
observer has SELECT-only credentials on `AgentSessions`, `SessionQueuedMessages` and
`TranscriptEntries` in this throwaway application's DB. Provision the role after migrations
with a test-only initialization helper; no schema migration or production role is introduced.
Credentials come from a private file and never appear in commands, manifests or logs.
Connection identity is recorded without credentials. Use parameterized, read-only queries in
short repeatable-read transactions, no pooled transaction held while a barrier waits.
The DB and runner publish no host port; the observer has no Docker socket. Docker access belongs
only to the already authorized parent controller. Missing/mismatched owner metadata refuses
observation and control; an unknown connection is never treated as this stack's database.

**Immutable expectation and lookup.** Before a POST, atomically export `expectation.json`:
schema version, run/case/submission nonce, source SHA, result/evidence digest, parent and receipt
project/resource identities, recipient session ID, normalized accepted StartedAt, runner identity,
definition/Cwd, exact UTF-8/LF body and SHA-256, mode WhenIdle, and the recipient's committed
queue sequence high-water mark. The full bounded body contains unique head/tail/run/case/SHA
markers and fits below the real spill/inline ceiling. Do not relax production spill behavior.
Use a dedicated Ui recipient with no other producers. Bootstrap it through a real native prompt
and completed turn before the measured message, so the attempt baseline is observable; never
seed transcript entries. Busy cases use FakeGrok's existing busy gate and real turn-end release.

The observer finds rows by exact `AgentSessionId`, `Sequence > prePostQueueSequence` and exact
trimmed `Body`, across **all statuses**, then checks Origin=Ui and no source task, notification,
schedule or maintenance binding. Body comparison and hash are both checked; do not use LIKE,
contains, newest-row selection or Sent as a receipt. Zero matches means not-yet-observed; more
than one means ambiguous/duplicate and fails. The nonce only finds a candidate: the committed
database row supplies its identity. Freeze the one `Id` once found and use it thereafter,
rechecking the immutable fields instead of discovering a replacement row.

Export `row-observed.json` with Id, Sequence, status, CreatedAt/SentAt, DeliveryAttempts,
LastDeliveryStartedAt, LastDeliveryBaselineSequence, LastDeliveryGeneration, DeliveryVerdict
and DeliveryVerdictAt, plus the same snapshot's session StartedAt. A never-attempted Pending
row has attempts=0 and nullable floor/generation; keep these null and label it awaiting attempt.
For an attempted row require a non-null floor and the expected generation before qualifying
this canary. No wall-clock/pre-POST floor substitution. The plain already-idle run must observe
one attempt even though POST's pending list is empty.

Each committed attempt is a separate immutable `attempt-<n>.json`, keyed by queue ID,
DeliveryAttempts, LastDeliveryStartedAt, generation and floor. In the instrumented host the
save interceptor snapshots every selected insert/attempt commit before delivery can continue,
even if no cut is armed. Use per-context captured change metadata in SavingChangesAsync;
after a successful save entity state is no longer Added/Modified. Confirm the snapshot through
a fresh read-only connection before atomic export. Never read a shared tracked context or query
the pending-only API as evidence. Stock no-cut observation is external and requires attempts=1;
unexpected retries are reported, not retroactively labelled as observed first attempts.
Enter-only recovery retains the original tuple; a fresh typing retry may commit a new floor,
so preserve both original and new snapshots rather than asserting all retries keep one floor.

**Receipt and restart join.** Pull the real runner's `/sessions/{id}/transcript` from the private
network and independently read the stored transcript rows. Persist native UUID/kind/body and
runner sequence, plus stored UUID/kind/body and **server** sequence. Match the entire expected
UserPrompt using only the specified LF normalization/outer trim; no fragment or containment
acceptance. Require exactly one matching native prompt for the measured submission, one stored
UUID/kind, and a stored sequence strictly above its committed attempt floor. A complete native
prompt alone while persistence is withheld is `native-received/server-pending`, not success.
Delivered/LateConfirmed is supporting queue evidence, not a substitute for these records.

`TranscriptEntry` carries no generation token. Bind receipt evidence through the captured
runner session/accepted generation and native transcript identity, checking runner
AcceptedStartedAt and DB StartedAt before and after observation against the attempt generation
at PostgreSQL microsecond precision. Preserve original launch/transcript identity across a
server-only restart; never annotate an arbitrary old row with the current generation. A changed
or unavailable generation blocks acceptance of the old manifest. TestDesign includes changed
generation, old equal-body prompt at/below floor, prefix-only/missing-tail, wrong run/SHA/digest,
duplicate UUID/body and complete failure-summary rejection/handling cases. Local synthetic
records may test the validator, but never qualify actual native delivery.

The observer can restart from expectation and frozen row/attempt files with no in-memory
callback or HTTP response. Read the original database and runner before acting on absence.
On lost POST acknowledgement, wait for the recorded request owner to finish or be terminated,
then observe; never automatically re-POST. If no row exists after an unknown acknowledgement,
record incomplete/unknown until that request has been ruled out. Only a definite insert-failure
case may explicitly retry after no-row/no-input/native-no-prompt evidence. Record the retry as
a new submission attempt. A verifier crash after receipt simply repeats read-only validation
and atomically finishes the same manifest; it sends no message. Preserve expected body,
observations, reached cuts, command outcomes and artifact hashes before disposing the stack.

**Barrier mechanism.** Add `OrdinaryDeliverySaveInterceptor`,
`OrdinaryDeliveryRunnerClient` and `OrdinaryDeliveryResponseBarrier` only in the fixture project.
The host installs them through ConfigureWebHost/ConfigureServices, preserving the real Npgsql
provider and transparently decorating the configured ISessionRunnerClient. Resolve/capture
the inner registration once without recursive DI. Forward every untargeted method, session,
event and byte unchanged. Decorator input records distinguish requested, forwarded and returned
writes; a transport ACK is not proof of recipient receipt. Instance-local case state is shared
across scopes, never static/global or keyed only by a reused filename.
At this baseline `SessionRunnerEventPump` resolves ISessionRunnerClient for SSE and
AgentSessionRuntime uses it for catch-up and normal input; instrument that outer registration,
not only SessionRunnerHttpClient. The response barrier must buffer start/flush as well as body
(`IHttpResponseBodyFeature`), so even headers cannot acknowledge the armed POST prematurely.

SavingChangesAsync captures only the selected ordinary row/transcript UUID. SavedChangesAsync
emits committed insert/attempt barriers only after a second connection sees the intended state;
an open outer transaction or invisible commit refuses the cut. This relies on the inspected Ui
WhenIdle path's transaction boundaries, not on a general assumption that SaveChanges commits
every caller's transaction. Guard that contract with an ordinary database test. Failed saves
emit no commit-ready receipt and clear per-context pending observation metadata.

Controls are atomically written files, not env toggles polled by production. An arm names the
exact case/submission, cut, row (or pre-insert selector), attempt and expected generation.
The fixture exports a reached record with those identities, host boot nonce/PID/start time,
container ID and independent durable observation digest, then waits for its exact release.
Consumed cut/arm IDs persist across server restart; stale/replayed releases cannot free a new
case, host or attempt. Fixture stop/deadline causes incomplete/failure, never implicit release.
The controller records a pending action before issuing it and reconciles resource state after
an interrupted action. It issues SIGKILL only to the exact recorded fixture-server container,
with automatic restart disabled; await exit, export logs, disarm the consumed cut and start the
same server against its original volumes. Do not kill/recreate Postgres, runner or recipient.
Catchable exceptions/cancellation exercise graceful failure, not this crash state. No manual
row status/age/generation/verdict edits, transcript edits, fake receipts or widened timeouts.

After restart verify the original DB identity, runner/container/session and accepted generation,
then let the existing startup attachment, catch-up and stranded-queue recovery run. Preserve
default age/verification windows and budget the actual wait in TestDesign. Do not add a flush
endpoint, shift the whole server clock or synthesize a turn-end to speed recovery. If native
reattachment fails (including CARD-0594), record the actual dependency failure and residue; do
not relaunch a new recipient and count it as recovery of the original generation.

| Cut / handoff | Instrumentation and reached-state proof | Release/recovery contract |
|---|---|---|
| `insert-refused` / Q-2 | SavingChangesAsync throws the named injected failure before the selected Added queue row's save. Independent query sees zero matching rows; no forwarded body/Enter and no native prompt. | Await failed POST. Disarm, dispose failed context through normal handling, verify absence, then explicitly submit again. This is a definite refusal, not a crash or auto retry. |
| `insert-committed` / Q-3 | SavedChangesAsync waits after the selected Ui insert is independently visible: Pending, attempts=0, null floor/generation, no input. It runs before EnqueueAsync can evaluate inline flush. | Hard-cut server, retain row, restart; normal eligible/stranded recovery eventually types the same Id. Also run busy then real turn-end without a crash. |
| `attempt-committed` / Q-3 | SavedChangesAsync waits after Sent + attempts=1 + original floor/generation + null verdict are independently visible, before any runner input. | Hard-cut/restart; original charge survives. Recovery reuses Id and a fresh body attempt charges once more, with its own recorded floor. |
| `body-before-enter` / Q-4 | Forwarding client has returned from the real body write and intercepts the separate CR before forwarding it. Read actual composer evidence and confirm no complete native prompt. Preserve bracketed multiline paste and LF. | Hard-cut/restart in the same generation. Existing whole-composer recovery sends only Enter, retains attempt/floor and yields the whole prompt once; any second body is failure. |
| `recipient-before-ingestion` / Q-5 | Decorator holds the selected native UserPrompt from BOTH StreamEventsAsync and GetTranscriptAsync before handing it to runtime; independent observer bypasses that decorator and pulls the real runner. No matching stored row. Hold all concurrent paths for that UUID; do not return a fabricated empty transcript. | After evidence export, hard-cut server and restart disarmed. Native record remains; normal catch-up persists it, late-confirms the original row and sends no further body/Enter. |
| `transcript-save-fails` / Q-5 | EF interceptor refuses saves for the actual native UUID/session, including batch, individual and persist-stub attempts. Keep the fault armed until absent stored UUID and actual native receipt are exported. Match UUID even when a fallback changes kind/body. | Exercise the real persistence failure handling, then disarm and permit catch-up (also cover server restart). Require one complete stored prompt, no accepted stub and zero duplicate input. One failed batch alone does not qualify this cut. |
| `receipt-before-verdict` / Q-5 | SavingChangesAsync holds the selected queue verdict transition to Delivered before save. Independent reads show the complete native/stored prompt and Sent/null verdict with original attempt tuple. No source notification is involved. | Hard-cut/restart. Recovery late-confirms same row with no additional writes; the stored receipt and original floor survive. |
| `response-before-client` / Q-2/Q-6 | Test-host middleware buffers only the armed ordinary POST response after the endpoint returns, preserving status/headers/body. Export request-completed and discovered row before any response bytes leave the host. | Hard-cut server to make acknowledgement deterministically unknown. Restart/observe the original row and receipt; do not submit again. Default middleware remains pass-through. |
| `receipt-before-manifest` / Q-6 | Controller durably exports expectation, row, attempt and actual validated receipt, then its own supervised worker stops before replacing the final acknowledgement manifest. | Fresh observer/controller resumes from those files, revalidates against DB/runner, writes the same manifest with zero POSTs. Controller interruption never kills its parent task session. |

For the last cut, `verify-docker-stack.ps1` exposes internal `-ReceiptWorker` and
`-ResumeManifest <owned-path>` modes. Its foreground supervisor launches and awaits the
one worker, records/reconciles its exact exit and resumes it explicitly. It does not stop the
Raw session running Small/Medium or launch another qualification recursively. Resume validates
the recorded project/container IDs rather than accepting arbitrary targets from a command line.

Each cut is independently armed and exported. A not-reached cut, timeout, host build error,
wrong identity, missing native prompt or fixture failure is incomplete/failed evidence, never a
successful crash test. Successful recovery is required for supported happy recovery arms;
negative inputs/refusals must produce their specified failure rather than silently skipping a
case. An insert-ready record has no attempt tuple; an attempt-ready record must have one.
Stock-image idle/busy runs must demonstrate no response/cut middleware dependency.

The local validator/barrier/command tests use files, value records, controlled DB/runner
substitutes and inherited child processes as appropriate. Database and real native cases are
ordinary V/R. Sourced PCs must select DB-free local methods only: no fixture `serve` mode,
Docker, Testcontainers, standing executor or server2 access. TestDesign must split validation
guards from actual I/O qualification honestly; a simulated response cannot prove a commit or a
native prompt. New instrumentation must also prove unrelated rows/sessions pass through,
invalid/stale arms do not activate, committed records come only after success, and normal
runtime images contain none of the fixture host. This amends G-18 and adds observation/barrier
guard obligations; it is not the full independent guard inventory or PC battery.

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
| **S2 — three-service deployment and explicit test-session authority** | `docker-compose.yml`, `docker/session-runner-grok/Dockerfile`; new `docker-compose.session-testing.yml`, `docker/stack.env.example` with placeholders; extend `DockerStackContractTests.cs` | Direct internal runner route, health ordering, Linux env, non-root state initialization, private services and persistent volumes. Add opt-in runner tools/socket/GID and test-only native FakeGrok target while preserving default phone-home image use. Parse base/override separately; prove effective mounts, tools and capability refusals. |
| **S3 — Small test target and session entry point** | new `docker/tests/Dockerfile`, `docker/tests/Dockerfile.dockerignore`, `docker-compose.test.yml`, `scripts/test-docker.ps1`, `scripts/test-docker-container.ps1`; new `tests/Antiphon.Tests/Scripts/DockerTestCommandTests.cs` | Build complete test context; provide foreground `-ThrowawayStack` entry point and persisted run manifest; run client lint/build/Vitest and messaging with broker opt-in; validate socket preflight, context transfer, evidence export, nonzero execution and exit propagation. Local command-boundary tests must reject snapshot inputs and wrong source/resource identity without invoking Docker. |
| **S4 — portable apphost lookup** | new `tests/Shared/TestAppHostPath.cs`; csproj links only where consumed; `tests/Antiphon.E2E/Fixtures/IsolatedSessionRunner.cs`; audited portable lookup sites from `rg -l -F -e fakeclaude.exe -e fakegrok.exe tests`; new `tests/Antiphon.Tests/TestHelpers/TestAppHostPathTests.cs` | Centralize OS suffix and missing-file diagnostics where portability is intended. Prove both platform filename branches with fake files plus native Linux apphost existence. Keep native-only contracts and CARD-0588 files out of the edit set. Exact consumer inventory is a TestDesign prerequisite. |
| **S5 — Medium roster and execution** | new `tests/linux-test-roster.json`, `tests/Antiphon.Tests/TestHelpers/LinuxTestRosterTests.cs`; test wrapper and manifest tooling from S3 | Complete class accounting, audited included classes/shards, explicit native/process/opt-in exclusions; run portable majority with real DB fixtures. Existing `TestDbFixtureIsolationTests`, `ProductionRunnerGuardTests` and selected `HealthEndpointTests` are required coverage anchors. Do not change their production safety guards. |
| **S6 — session-created stack acceptance and operations** | new `scripts/verify-docker-stack.ps1`, `docker-compose.throwaway.yml`, test-only `docker/session-testing/fakegrok-linux.sh` if terminal setup is needed, `tests/Antiphon.Tests/Scripts/DockerStackSmokeCommandTests.cs`, `docs/docker-stack.md`; update `docs/bootstrap.md` and `docs/testing-and-build.md` with links | Keep interactive Raw smoke. Prove a separate server-launched test-command session creates a child stack, runs S3/S5, exports evidence and cleans only owned resources. Add distinct native FakeGrok complete-UserPrompt queue probe, failed/partial-receipt and interrupted-cleanup guards. Prove DB persistence and same-path workspace visibility; run identical entry points on server2. Document ordinary testing versus Windows Mutation and CARD-0594/0598 dependencies. |

S6 is further divided into the following commit-sized parts. All named new methods/classes are
design ownership for TestDesign to freeze, not already-existing tests or authorized CP commands.

| Part | Files | Tests and exit evidence |
|---|---|---|
| **S6a — owned observation and receipt contracts** | new `tests/Antiphon.DockerStack.Fixture/Antiphon.DockerStack.Fixture.csproj`, `Program.cs`, `DeliveryCaseIdentity.cs`, `QueueObservationReader.cs`, `DeliveryEvidenceValidator.cs`; new `tests/Antiphon.Tests/Infrastructure/DockerDeliveryObservationTests.cs`; `tests/Antiphon.Tests/Antiphon.Tests.csproj` project reference | Local validator tests for owner/row uniqueness, status-independent lookup result validation, nullable/no-attempt versus committed-attempt tuples, generation/sequence domains and full-body/UUID acceptance. Separate ordinary DB cases in `DockerDeliveryDatabaseTests.cs` prove real SELECT-only access, Sent discovery, coherent snapshots and unique lookup against PostgreSQL. PCs select only DB-free methods. |
| **S6b — fixture host and independent cuts** | new fixture `DeliveryFixtureHost.cs`, `OrdinaryDeliverySaveInterceptor.cs`, `OrdinaryDeliveryRunnerClient.cs`, `OrdinaryDeliveryResponseBarrier.cs`, `DeliveryFileBarrier.cs`; new `tests/Antiphon.Tests/Infrastructure/DockerDeliveryBarrierTests.cs`, `DockerDeliveryDatabaseTests.cs` | Local methods prove target-only forwarding, stale/foreign release refusal, persisted one-shot arms, input ordering and no commit receipt after a failed save. Ordinary DB/HTTP tests prove insert/attempt second-connection visibility, verdict hold, batch/individual/stub failure coverage, SSE/pull gate convergence and post-endpoint/pre-response hold. Keep `SessionQueueReceiptPlumbingTests` and existing busy/idle/interrupted-attempt regression owners in TestDesign's ordinary inventory. |
| **S6c — package and drive isolated receipt stacks** | `docker/tests/Dockerfile`, `docker/tests/Dockerfile.dockerignore`, `docker/session-runner-grok/Dockerfile`; new `docker-compose.delivery-fixture.yml`, `tests/Antiphon.DockerStack.Fixture/provision-observer.sql`; `scripts/verify-docker-stack.ps1`; `DockerStackContractTests.cs`, `DockerStackSmokeCommandTests.cs` | Script cases prove expectation-before-POST, owner-bound observer invocation, action/reached/export ordering, exact server-only kill/start, unknown-ack no-rePOST and verifier resume with zero POST. Stock-image idle/busy real FakeGrok receipts plus every cut use actual PostgreSQL/runner and exported evidence. Inspect all final image targets and effective Compose mounts. No fixture source/payload in production images and no socket in receipt server/runner/observer. |
| **S6d — recovery evidence and operations** | new `tests/Antiphon.Tests/Scripts/DockerDeliveryRecoveryCommandTests.cs`; S6 script/fixture files above; `docs/docker-stack.md`, `docs/bootstrap.md`, `docs/testing-and-build.md` | Real Linux per-cut case manifest inventory; same row, charge/floor/generation, native+stored receipt and expected additional body/Enter counts. Local command cases guard interrupted export/cleanup, duplicate/changed-identity rejection and failure-summary exit status. Record CARD-0594 block if native recovery cannot run; never replace with synthetic receipt evidence. |

S6a -> S6b -> S6c -> S6d. S6a/S6b local authoring can proceed once TestDesign accepts the full
manifest; native S6c/S6d additionally depend on S2/S3/S5 packaging and native qualification.
The amendment plans **no edits** to `server/Program.cs`, `SessionEndpoints.cs`, queue/runtime
services, DTOs/entities/migrations, `LandDeliveryBoundary`, or the existing E2E landing fixture.
If Code proves an existing interception point cannot expose a required cut, return the exact
unreachable boundary/evidence to Plan instead of silently widening the production surface.

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
| G-7 / PC-7: explicit ordinary-test socket authority | Mount socket into server/base runner, omit it from the explicit test-session override, omit socket GID preflight or allow sourced input (distinct guards); base/override/command tests must reject each. The enabled test runner's socket is now required, not a forbidden mount. |
| G-8 / PC-8: context custody | Remove a required deny rule for a worktree `.git` pointer, `.antiphon`, a local env/auth file, or alternate output (separate variants); context-sentinel tests catch each leak in both context policies. Never use actual secrets. |
| G-9 / PC-9: tests cannot silently disappear | Drop a required class/result file or return zero execution (separate variants); `DockerTestCommandTests.Missing_or_empty_execution_fails` rejects it. |
| G-10 / PC-10: original test exit code reaches caller | Make the command/result adapter hide nonzero status; `Nonzero_test_exit_is_preserved` must fail even with plausible success output. |
| G-11 / PC-11: roster stays portable and exhaustive | Add an unclassified class, include a known spawner/native class, or drop a discovered class (separate variants); `LinuxTestRosterTests` rejects each without launching a process. |
| G-12 / PC-12: test environment never reaches app runner | Remove the dead-runner/refusing-client requirement from the command boundary; `Test_environment_refuses_application_runner` fails. Existing `ProductionRunnerGuardTests` still execute in the ordinary backend lane. |
| G-13 / PC-13: teardown acts only on its owned Compose project | Substitute a foreign project label or teardown target; `DockerStackSmokeCommandTests.Foreign_project_cleanup_is_refused` proves no delete command was emitted. |
| G-14 / PC-14: native/session/receipt evidence cannot become health-only success | Separately remove Raw challenge/exit, session-created run identity, or complete native UserPrompt/attempt-floor match; `DockerStackSmokeCommandTests` rejects each. A Raw marker is never accepted as a transcript receipt. |
| G-15 / PC-15: Linux state settings are usable | Remove a required absolute path, inject a Windows path, enable Herdr/modern, or lose non-root writable state (separate variants); resolved configuration and state preflight tests identify the defect. |
| G-16 / PC-16: missing Docker is not a skipped test success | Deny socket/preflight connection while result fixtures report skipped DB/broker tests; `Unavailable_test_daemon_fails_before_execution` rejects the run. |
| G-17 / PC-17: child source/state/evidence ownership | Independently inject a parent-as-child project, wrong SHA, foreign volume/container ID, caller-only bind path or missing exported evidence; local command-boundary guards fail before unauthorized create/delete or successful result. Real sibling execution remains ordinary V/R. |
| G-18 / PC-18: interruption cannot become successful delivery/cleanup | Drop result-ready manifest, acknowledge without full UserPrompt, or suppress recorded cleanup residue; exact-method local guards must reject each. Required real queue recovery cuts are ordinary acceptance/regression, never Docker PCs. |

TestDesign must split independently bypassable guards into distinct PCs where these grouped
requirements need it; do not treat this preliminary 18-row inventory as permission to omit a
variant. It also supplies normal Windows/Linux filename tests for S4 and regression cases for
retained state/startup order. This work changes packaging and harnesses, not asynchronous delivery
protocols; the delivery inventory must cover both the foreground session-created run and the
separate existing-queue native receipt probe, with their persistence and interruption owners.

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
   do not meet this criterion. Record process/session identity and teardown result. Independently
   qualify the native FakeGrok queue probe: full summary UserPrompt after the attempt floor in
   the expected generation, including busy-then-eligible delivery. Record its queue/transcript
   evidence separately. Neither gate is a SourceLanding custody receipt.
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
8. A session genuinely launched by the parent server inside its server2 runner invokes the
   foreground entry point, creates a separately named throwaway server/runner/Postgres stack,
   executes Small + Medium with Testcontainers, exports fresh results and removes its owned
   disposable resources. Record parent session/generation, child project/resource IDs, source
   SHA and manifest hash. Test denial/missing Docker and interruption without a false success.
   A host-side Docker smoke does not meet this session-created-stack gate.
9. Base Compose remains socket-free for app services; explicit ordinary-session-testing override
   enables only its intended runner socket and tools. Linux still omits VerificationCustodyV1
   and refuses sourced verification; existing Windows admission/launch/cleanup behavior remains
   intact. Do not execute Linux SourceLanding to generate a green result for this card.

The next stage is **TestDesign**, not Code. It must read the touched fixtures/helpers, freeze the
portable roster and lookup-site inventory, append `## Verification design` with Inspection,
Delivery inventory, V/R, complete guard/PC mapping, exclusions, the `### Checkpoints` closed list
and numeric ordinary/Mutation costs. Group build/test invocations to reuse frozen outputs.
Separate Small and Medium checkpoint rows and split large backend groups into bounded exact
class filters. Include the Windows regression tests relevant to the shared helper only; never
schedule the three native assemblies as a Linux full-suite requirement.

Coordination facts at Plan time: CARD-0587 and CARD-0588 are Backlog; CARD-0490 is Review and its
packaging is already in this checkout. Re-read their landing/status before touching shared files.
Continuation re-read on 2026-09-21: CARD-0587/0588/0594 remain Backlog. New CARD-0598 is Backlog;
its ID is `af15648d-fd85-4fe5-aa02-8ac893a5dba4`, on the Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`, linked to CARD-0590
`5e25f062-c57f-426c-9bd3-6b6ad5d2b9a3` by stable finding key
`card-0590:linux-source-landing-custody`. No dependency task was spawned.
The Linux pipe-connect failure is recorded inherited evidence, not a newly reproduced defect.
Do not close this card as working end to end if it reproduces; report the exact launch/connect
failure to the native owner while retaining completed image/test slices. No fresh investigation
dispatch is needed to write this plan.

Execution preconditions to record in TestDesign/Code: server2 Docker Engine/Compose versions and
amd64 architecture, available resources, owned deployment/evidence paths, explicit Docker context,
required deployment password supplied without logging, and the latest native-blocker disposition.
They were not probed in Plan. Missing server2 access blocks that acceptance row only; it must not
be reported as a passed deployment or prevent preparing the concrete images/tests locally.

Original planning ranges were Small 2-4 hours and Medium 3-6 hours, excluding native repair.
They predate the session-created-stack and native receipt gates and are not a budget for the
revised scope. TestDesign must estimate the added image/tool/manifest/receipt work and provide
per-checkpoint minutes and separate PC cost before Code. No current test count, complete cost
or green Linux baseline is claimed.

### Dependency disposition

The governing [Mutation contract](../../orchestration-loop.md#code-ordinary-review-land-then-mutation-card-0478)
says: "Use local inherited execution only; never give snapshot access to an external executor,
broker, remote service or pre-existing process." It also requires "a fresh Worker/Mutation/Worktree,
no standing pin, OnAgent, Shared, ReadOnly or merge target." Freshness and execution custody are
independent requirements; satisfying the former does not waive the latter.

| Work | Owner / disposition | Blocks CARD-0590 completion? |
|---|---|---|
| Broken root server image | S1 / CARD-0587; consume its landing if first | Yes, working image required. |
| Four native-only skip corrections | CARD-0588; keep classes explicitly excluded | No, provided excluded classes are honestly accounted for. |
| Unix runner-to-PtyHost connection and recorded live/native qualification | CARD-0594; no timeout or transport redesign here | Yes if reproduced by required native gates; do not drop those gates. |
| Linux SourceLanding admission, native containment, receipts and cleanup | CARD-0598, separate from CARD-0594 | No for the selected ordinary server2 scope; yes before any future Linux Mutation claim. |
| Docker-created executors during SourceLanding | Unapproved under current custody contract; candidate (b) rejected | Not part of CARD-0590. CARD-0598 does not itself grant daemon snapshot access. |
| Post-land PCs for CARD-0590 | Existing supported Windows SourceLanding path; local inherited methods only | Companion remains open until that battery/discovery is complete; ordinary Docker evidence is never relabelled PC evidence. |

This is the scope reconciliation expressly commissioned by continuation `e7f50f48`, not an
unstated default requiring a decision stage. The two objectives are met by different execution
paths: self-contained session-created stacks for ordinary testing, and custody-compliant local
Mutation. Running that same Docker workflow *inside* SourceLanding remains unsupported, not
quietly accepted or hidden as a skip.

## Historical TestDesign rejection (09cc47e1)

The remainder of this assessment through its historical Handoff records what task `09cc47e1`
found at `5904ce74`; references to "present design", old D-6, seven outcomes and next: plan are
historical. The revised decisions and acceptance above resolve those design conflicts. Keep the
inspected fixture inventory and negative evidence; do not use this rejected section as a current
`## Verification design` or a Code checkpoint manifest. A fresh TestDesign section is still due.

TestDesign assessment, 2026-09-21, task `09cc47e1`, inspected plan commit
`5904ce74e901fa86c38673cf368205f282e5fd7b`: **return to Plan; not a Code checkpoint manifest**.
The implementation design above is preserved. The commissioning brief requires a persistent
server2 runner whose launched session can create and remove a throwaway Antiphon stack through
Testcontainers **during a SourceLanding Mutation battery**. The present design cannot deliver
that use case, even if all its image, Small, Medium and Raw-shell checks pass.

There are three separate boundaries to resolve:

1. **Execution placement contradicts the brief.** D-6 permits the Docker socket only in a
   separate test service and explicitly excludes application-task agents. S3's foreground
   `docker compose run` entry point is not a session-launched stack. The Mutation paragraph
   also correctly prohibits giving the sourced snapshot to a standing Docker daemon, including
   a local one. Adding a socket mount to the runner would violate both decisions; it is not a
   routine test-fixture adjustment. Plan must specify a custody-compatible execution topology
   or explicitly return the scope change to the caller. Do not bypass the SourceLanding rule.
2. **Linux SourceLanding custody is unsupported independently of the pipe handshake.**
   `server/Application/Services/SourceLandingAdmission.cs:52` requires the feature
   `VerificationCustodyV1`, backend `windows-job-v1` and a nonempty runner-store identity.
   `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:46` advertises that backend only on
   Windows with modern ConPTY. `src/Antiphon.SessionRunner/Program.cs:205` withholds the feature
   otherwise. Independently, `RunnerCustodyLedger.PrepareStart` at
   `src/Antiphon.SessionRunner/RunnerCustodyLedger.cs:125` persists an unsupported record and
   throws `verification_custody_unsupported_backend` for a verification-bound non-Windows
   launch. D-3's Linux image and the required inbox backend cannot pass these checks. Removing
   the checks or fabricating a Windows receipt is not a fix. The plan needs a named custody
   dependency with descendant accounting, sealing, durable receipts and recovery, separate
   from CARD-0594's runner-to-PtyHost Unix pipe dependency.
3. **The acceptance path stops short of the requested mutation use case.** D-8/S6's Raw shell
   has no verification binding and produces no provider UserPrompt receipt. It remains a
   necessary native-launch gate, but cannot establish SourceLanding admission, child-container
   custody, mutation result delivery or cleanup authorization. The current acceptance list has
   no session-created throwaway stack or recovery/receipt test for that use case.

These are source/design findings, not newly reproduced Linux failures. No source code, runtime
configuration, platform skip, test timeout or shared service was changed. Do not remove the
native-session acceptance gate to make the packaging increment appear complete. CARD-0587
continues to own the root-image overlap; CARD-0588 retains its four skip changes. The caller's
CARD-0594 dependency does not by itself resolve the independently observed custody refusal.

### Inspection

Bodies read and their verification consequences:

| Tests, fixtures or helpers inspected | Boundary and disposition |
|---|---|
| `tests/Antiphon.Tests/Scripts/VerifyPhoneHomeGrokScriptTests.cs` and `Application/DelegateScriptRunner.cs` | Nearest script-test fixture for S3/S6. Real inherited `pwsh` children and command arguments can be observed. Its server2 test deliberately uses a desktop Tailscale origin, so it cannot be reused as desktop-independent acceptance. Source-string assertions alone do not prove container/session behavior. |
| `tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs`, `TestDbFixtureLifecycle.cs`, `TestDbFixtureIsolationTests.cs` | DB bootstrap is lazy, uses a real PostgreSQL Testcontainer and creates separate cloned databases, not SearchPath schemas. The clone, migration, concurrent-isolation and disposal bodies were read. Ordinary Medium may use this fixture; a SourceLanding PC must not initialize it against a standing daemon. |
| `TestHelpers/ProductionRunnerGuard.cs`, including both `ProductionRunnerGuardTests` methods, and `AntiphonWebAppFactory.cs` | The dead URL and refusing runner client are independent protections. The factory boots real Program with an isolated database and disables process-producing maintenance. Keep both protections; HTTP tests are not native-session evidence. |
| `Application/HealthEndpointTests.cs` | Both methods check a non-unknown 40-character version and the in-process stamp. The health response does not establish version, session delivery or custody. A clean image/test context without `.git` needs explicit revision injection. |
| `TestHelpers/TestLaneCategoryGuardTests.cs` | Its source scan checks Unit xor Integration only. It is not a portable-class roster or a transitive process-spawn audit. No majority claim follows from this guard or the 789-file estimate. |
| `Application/GrokDelegateDispatchTests.cs`: the staged-FakeGrok specification test, `SpecOf`, `CreateHarness`, `CreateDispatchHarness`, `BuildHarness`, and warm-session setup | The `.exe` lookup at line 267 resolves a real staged file but does not launch it. This is a portable lookup candidate. The warm fixture also contains a Windows-shaped rules path; replacing the executable suffix alone does not certify the whole class on Linux. |
| `tests/Antiphon.E2E/Fixtures/IsolatedSessionRunner.cs`: construction, start/readiness, `StartProcess`, output drain/stop entry points | `StartProcess` hardcodes the runner apphost `.exe` and missing-file diagnostic. Naming preparation is separate from the fixture's process lifecycle and from browser acceptance. |
| `tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs`: configuration and `FileBoundary.ReachedAsync`; `LandDeliveryFixture.cs`: initialization | Existing delivery cuts include terminal commit, before enqueue, queue insertion, before typing/verdict and before receipt save. This fixture uses native FakeGrok, modern delivery configuration and isolated real services; it is not an available Linux substitute merely by renaming the apphost. CARD-0588 owns its platform treatment. |
| `tests/Antiphon.SessionRunner.Tests/RunnerCustodyTests.cs`, including nested `CustodyFixture` | The native receipt/restart/orphan tests require Windows modern ConPTY and skip before setup elsewhere. They cannot be counted as Linux acceptance. The fixture reads actual producer receipts; container exit or a runner status is insufficient. |
| `RunnerCustodyLedger` body, runtime capability property, runner capability route, and `SourceLandingAdmission.RequireSupportAsync` | Independently enforced admission, capability and launch boundaries establish the custody seam above. A test that merely turns off one check would leave the others closed. |
| Root `Dockerfile`, `.dockerignore`, `docker-compose.yml`, `docker/session-runner-grok/Dockerfile`, `docker-compose.runner-grok.yml`, and `tests/Antiphon.Tests/Antiphon.Tests.csproj` | Actual copy graph, linked fixtures, embedded resource, apphost staging and old phone-home placement inspected. Packaging success is a separate claim from supported SourceLanding execution. |

Required setup has not been observed: server2 Docker/Compose versions, amd64 architecture,
owned deployment/evidence paths, Docker context, socket authority and mapped-port reachability,
available resources, deployment-secret injection and current CARD-0594 disposition. These are
execution preconditions, not passed checks. No server2 access or deployment was attempted.

#### Frozen apphost occurrence inventory

At the inspected commit, `rg -n -F -e fakeclaude.exe -e fakegrok.exe tests` yields **52 lines
in 18 files**, containing **20 Path.Combine lookup/assertion sites**. Comments and missing-file
messages account for the other lines. The table freezes this inventory, not a portable-class
admission decision. Line numbers refer to that commit. All paths are under `tests/`.

| File | Matching lines | Actual lookup lines | S4 disposition |
|---|---:|---|---|
| `Antiphon.Tests/Application/GrokDelegateDispatchTests.cs` | 2 | 267 | Portable helper consumer for the specification test; preserve the rest of the class and audit its Windows-shaped fixture data before whole-class admission. |
| `Antiphon.Tests/TestHelpers/RemoteControlPtyLane.cs` | 3 | 55 | Retain native Windows ConPTY lane; excluded from non-spawning Medium. |
| `Antiphon.Tests/TestHelpers/HerdrLabelFollowHttpFixture.cs` | 1 | 148 | Retain native Herdr/runner fixture; excluded from non-spawning Medium. |
| `Antiphon.Tests/Application/DelegationBriefCeilingPtyTests.cs` | 3 | 48 | Retain native ConPTY contract. |
| `Antiphon.Tests/Application/GrokDelegateEndToEndTests.cs` | 7 | 69, 72 | Retain native process/ConPTY contract. |
| `Antiphon.Tests/Application/GrokSignInRuntimeTests.cs` | 2 | 37 | Retain native modern-ConPTY contract. |
| `Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.cs` | 4 | 50, 53 | Retain mixed Herdr/native fixture; no class-wide non-spawning claim. |
| `Antiphon.Tests/Application/SessionMessageQueueGrokPtyIntegrationTests.cs` | 5 | 33 | Retain native producer-to-recipient coverage; it does not become Linux evidence. |
| `Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs` | 9 | 41 | Retain native producer-to-recipient coverage. |
| `Antiphon.Tests/Application/SessionQueueReceiptPlumbingTests.cs` | 1 | 27 | Retain native ConPTY receipt fixture. |
| `Antiphon.E2E/Fixtures/LandDeliveryOptions.cs` | 1 | 26 | Retain native delivery fixture configuration; do not duplicate CARD-0588. |
| `Antiphon.E2E/Fixtures/LandDeliveryFixture.cs` | 1 | 59 | Staged-native assertion; CARD-0588 owns platform treatment. |
| `Antiphon.Agents.Pty.Tests/ClaudeSubmitContractTests.cs` | 2 | 70 | Native assembly excluded from this Linux target. |
| `Antiphon.Agents.Pty.Tests/ClaudeVerifiedDeliveryTests.cs` | 2 | 34 | Native assembly excluded. |
| `Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs` | 2 | 31 | Native assembly excluded. |
| `Antiphon.Agents.Pty.Tests/FakeGrokContractTests.cs` | 2 | 50 | Native assembly excluded; CARD-0588 owns the skip correction. |
| `Antiphon.Agents.Pty.Tests/FakeVsRealClipParityTests.cs` | 3 | 72 | Native/real-provider canary excluded. |
| `Antiphon.Agents.Pty.Tests/PtyLargeWriteTests.cs` | 2 | 38 | Native assembly excluded. |

Separately, `Antiphon.E2E/Fixtures/IsolatedSessionRunner.cs:131,135` contains one runner lookup
and its diagnostic, absent from the fake-name search. S4 prepares the OS suffix there without
claiming Linux E2E acceptance. The shared-helper boundary must cover Windows/Linux crossed with
existing/missing files, correct sibling producer directories and diagnostic filename. Native
Linux execute permission requires ordinary Linux evidence; a fake file on Windows cannot prove it.

The complete portable class roster is **not frozen or certified** by this assessment. No JSON
roster was manufactured from file counts, category labels or absence of direct Process.Start
text. The required resumed TestDesign must account for every discovered class, its helper/base
dependencies, exact shard, exclusion owner and class-count denominator. The three required
anchors remain `TestDbFixtureIsolationTests`, `ProductionRunnerGuardTests` and
`HealthEndpointTests`; they are not a replacement for the Medium majority.

### Delivery inventory

The six existing slices change packaging/harnesses, not a new asynchronous notification
protocol. Their only native smoke is server launch/input -> internal HTTP runner -> PtyHost ->
Raw shell. Correlate the smoke run nonce, session ID, accepted generation and observed child
identity; keep runner state on its owned volume and export input/output/exit evidence before
cleanup. After container removal a live PTY is gone; persisted session records do not prove
cross-container adoption. A shell's response to a post-launch unique challenge proves that input
was executed in that shell. It is **not** a matching complete UserPrompt receipt.

The brief's SourceLanding path adds a different qualification obligation: published operation
and exact landed SHA -> managed task/creation -> verification-bound session generation -> owned
test executor and stack -> result and native custody receipt -> caller and authorized cleanup.
The durable join must include operation ID, landed SHA, task ID, creation ID, execution ID,
runner store, session/generation and original observer/container identity. The current plan has
no compatible Linux producer for that custody receipt and no session-to-executor handoff design.

Plan must identify each persistence/handoff and recovery owner before TestDesign can enumerate
real-queue tests: recipient already eligible; recipient busy then eligible; crash after durable
outcome but before enqueue; enqueue failure; crash after enqueue before send; and crash after
recipient receipt before its acknowledgement is saved. Recovery must retain the same durable
notification identity. A session result is delivered only when the destination transcript
contains the complete matching UserPrompt after the attempt floor. Request acceptance, an insert,
an event, Sent, transport ACK, shell marker and container health cannot substitute.

Existing LandDelivery boundary hooks illustrate the cuts but do not establish this Linux path.
Fake command results can test script decisions and failure propagation; they cannot prove
Docker execution, child custody or recipient delivery. Source/file contract tests can detect
removed packaging declarations; they cannot prove effective Docker context filtering or the
published runtime payload. Ordinary real-container evidence remains required for those claims.

### Proves it works now

No product V-case is claimed green. The source inspection establishes why the brief's path
cannot currently qualify. The revised plan must preserve all seven ordinary acceptance outcomes
above and add the session-created stack/custody/recipient acceptance path, or obtain an explicit
scope revision. The Raw-shell gate remains required even when all containers are healthy.

### Guards the regression

No completed R-case or current Linux baseline is claimed. Existing tests named in Inspection
are inspected anchors, not executed evidence. Preserve the native tests and production-runner
fences. Do not count a native skip as portable coverage or widen timeouts to hide the pipe failure.

### Guard inventory

The earlier 16 G/PC rows remain preliminary **groups**, not a completed one-to-one inventory.
They include independently bypassable guards (client/bundles, host/native library, server/runner
socket mounts, each context policy, evidence file/class/count, path/backend/permissions) that
still require separate PCs. They omit the brief's Linux custody/executor handoffs entirely.
Consequently the required `guards=N, mapped=N, missing=0, duplicate PC mappings=0` acceptance
audit has **not passed**. Issuing a zero-missing claim here would be false.

### Positive controls

No executable PC battery is commissioned by this rejected design. The plan's local inherited
process rule remains binding. The revised topology must make each PC's exact-method
break/red/restore/green cycle executable without handing the sourced snapshot to server2, a
standing daemon or an external executor. Code authors the tests and runs ordinary V/R;
ordinary Review judges before land; Mutation runs the PCs after land. The native custody guards
must remain intact while their supported Linux implementation/dependency is designed.

### Out of scope

- Rewriting D-6 or implementing a Linux custody backend in TestDesign: these alter the fix
  design and need a Plan continuation with explicit ownership.
- CARD-0594's native pipe repair, CARD-0588's four skips, full nightly/native assembly parity,
  browser E2E, live providers and desktop deployment: unchanged exclusions/dependencies.
- Certifying an unaudited Medium roster or pricing an unspecified executor as executable work:
  neither would be a reviewable Code handoff.

### Checkpoints

No Code checkpoint is issued. The closed list is deliberately empty because this is a rejection
at a required verification seam, not acceptance of zero-test implementation. Plan must resolve
the three boundaries above, then TestDesign must supply the complete class roster, split guard
inventory and populated checkpoint table before `next: code` is permitted.

| CP | After | Build | Group | Filter | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|

### Cost

- **This rejected dispatch:** 0 builds, 0 product test runs, 0 PC cycles. Ordinary Code floor
  issued = sum of the empty checkpoint table = **0 minutes**; Mutation floor issued =
  **0 minutes**; issued setup/build + V/R + PC total = **0 minutes**. These numbers mean no
  execution has been commissioned, not that the requested feature costs zero to verify.
- **Replanning allowance (estimated): 60 minutes** to define/assign the Linux custody and
  session-owned executor dependencies and update acceptance. This excludes implementation,
  the full roster audit and resumed TestDesign. It is not a substitute Code budget.
- **Measured execution savings: 0 minutes.** No equivalent complete battery was run or timed;
  a percentage saving or complete ordinary/PC cost would be unsupported. Avoiding a premature
  Code dispatch is the purpose of the rejection, not a measured optimization claim.

Handoff: `next: plan`. Retain this negative finding and frozen lookup inventory, reconcile the
SourceLanding/session-created-stack objective with D-6 and the existing custody contract, name
the independent Linux custody dependency, then return the amended plan to TestDesign for the
full portable roster, V/R, one-to-one PCs, exact checkpoints and numeric execution floors.

## Historical TestDesign handoff (e7f50f48)

The three rejected design boundaries are now resolved in scope, not claimed green in execution:
D-6 provides a concrete ordinary-session Docker path; D-13/D-14 preserve SourceLanding custody
and assign the independent Linux work to CARD-0598; D-8/D-16 and acceptance 3/8 keep distinct
native execution, session-created stack and full UserPrompt evidence. This Plan dispatch ran
**0 builds, 0 product tests and 0 PC cycles**. It verified the custody source and live card
boundaries only. No server2 setup or native behavior has been measured here.

TestDesign must now:

1. Retain the frozen lookup occurrence inventory, inspect the new target/ordinary-source/command
   seams to be implemented and freeze the complete class roster with transitive helper audit,
   denominator and exact shards. Do not infer portability from absence of direct Process.Start.
2. Author the current `## Verification design` and closed `### Checkpoints` table covering all
   nine ordinary acceptance outcomes, including base and opt-in Compose, actual session-origin
   child-stack execution, complete native FakeGrok receipt and explicit native dependencies.
   Distinguish container build/probe commands from each exact TUnit filter. Preserve production
   runner guards and the original Small/Medium scope; no Linux native-suite parity promise.
3. Inventory foreground manifest/result/export/cleanup cuts and real queue receipt cuts using
   the identities and interruption rules above. No new automatic notification recovery is
   promised. Resolve exactly which existing queue regressions run and which real Linux canary
   cuts are added; do not revive the rejected Linux SourceLanding executor handoff.
4. Split G-1..G-18 into independently bypassable, one-to-one method-scoped guards/PCs, with
   compiling mutations, intended red assertions and restore/green commands. In particular replace
   old PC-7's runner-socket prohibition; enforce base versus explicit testing override separately.
   Keep actual image/native/DB/container behavior as ordinary V/R, and use only honest local
   file/config/result/command seams for Windows sourced PCs. No PC may initialize Testcontainers,
   use Docker/SSH/server2, or hand source to a daemon. A local guard must not claim to prove the
   runtime behavior it only checks declaratively. Report any uncovered runtime boundary rather
   than inventing a custody exception.
5. Name and verify the selected PC methods' build dependency closure for local execution. They
   must avoid DB initialization and shared compilation/build-server execution outside inherited
   custody; preserve the established isolated-build procedure and CARD-0578 dependency if it
   affects that closure. Do not run whole native assemblies or DB suites just to test scripts.
6. Provide revised numeric ordinary and Mutation floors plus setup/authoring estimates. The old
   rejection's empty table and zero issued minutes are historical, not a zero-cost Code budget.
   Return `next: code` only after those artifacts are complete; if a required native path cannot
   be verified, name its dependency without weakening the acceptance gate.

Next: **test-design**. The Linux custody card is a future capability dependency; it does not
block designing or implementing this ordinary server2 stack. CARD-0594 can still block actual
native acceptance and therefore CARD-0590 closure. No production custody/source/cleanup files
are in CARD-0590's implementation edit set.

## Historical TestDesign v2 rejection (806c2e25)

TestDesign v2, task `806c2e25`, inspected source and plan at
`a9ca1664b0857ab13e8c1e671f75db6cc30432e9`. **Return to Plan for the S6 observation seam;
this is not an accepted Code verification manifest.** The ordinary-testing versus
SourceLanding scope reconciliation is accepted and is not reopened here. No Linux Mutation
qualification is requested. The fix design above is unchanged.

Historical record: the missing-seam conclusions below describe the pre-04b55418 plan. D-17..D-22
and the current S6 fixture contract supersede them; retain the inspected facts and unfinished
roster/guard/checkpoint obligations. Follow the current handoff at the end of this document.

S6 requires the verifier to persist the **returned queue identity**, its actual attempt floor
and accepted generation, then prove recovery through the existing queue. The specified public
API cannot supply that record for an already eligible recipient:

1. `server/Api/Endpoints/SessionEndpoints.cs:91-97` passes only body and mode to
   `EnqueueAsync`; it does not capture the service's `onCreated` callback.
2. `server/Application/Services/SessionMessageQueueService.cs:465-479` saves the row,
   invokes that internal callback, and can deliver the row **before returning**.
3. `BuildQueueDtoAsync` at lines 4417-4440 selects **Pending only**. Both the POST response
   and subsequent GET therefore omit an immediately delivered row. `SessionQueueDto` has
   no submitted-row identity. `QueuedMessageDto` also omits
   `LastDeliveryBaselineSequence` and `LastDeliveryGeneration`.
4. This is deliberate existing behavior, not a hypothetical race:
   `SessionQueueReceiptPlumbingTests.C475_AlreadyIdleWhenIdleHasRecipientReceipt` asserts
   `dto.Messages.ShouldBeEmpty()`, proves the whole native recipient UserPrompt, and then
   obtains the queue row and baseline through its **private database fixture**. The planned
   Docker script has no corresponding observation contract.
5. Existing `queue-before-typing` and `queue-before-verdict` hooks are reached only for
   rows with `SourceLandNotificationId != null` (service lines 1900-1902 and 1939-1941).
   The public ordinary message POST creates no such binding. `afterLandQueueInsert` is
   similarly conditional at lines 466-467. Configuring `LandDeliveryBoundary` alone cannot
   cut this probe at the named persistence boundaries.

The native complete-UserPrompt gate itself remains valid. What is missing is a realizable
way to observe and interrupt its durable queue handoffs, particularly on the already-idle arm.
Using only a run nonce/body digest would not satisfy the plan's queue-row identity requirement.
Keeping every recipient busy to retain its DTO would exclude a mandatory boundary combination.
`Mode:Now` has no durable queue row and does not solve the requirement.

**Required Plan amendment:** choose and own the S6 observation/fault fixture and its edit set.
Prefer a test-only observer of the owned stack's PostgreSQL rows, with a separate deterministic
cut mechanism, over widening the public message API solely for this canary. Define how the
observer discovers the unique ordinary row across Pending/Sent, reads its committed generation
and baseline, exports evidence, and reconnects after verifier/server restart. Bind it to the
recorded project/database/session/run, give it no production database target, and never let it
write a recipient transcript. Specify independently how insert failure, committed-attempt
before typing, and complete-receipt before verdict-save are held/released. Existing notification
hooks cannot be treated as generic hooks. If an API change is chosen instead, add its owner,
response contract and ordinary regression scope explicitly. This is a Plan continuation, not a
new operator choice about the already settled topology.

### Inspection

Bodies read during this dispatch (not inherited claims of inspection):

| Test/fixture/helper bodies | Boundaries and disposition |
|---|---|
| `SessionMessageQueueServiceTests.When_idle_message_is_held_while_the_agent_is_working`, `When_idle_and_agent_is_idle_the_message_is_delivered_right_away`, `Turn_end_flushes_the_oldest_queued_message`, `CreateHarnessAsync`, transcript insertion and disposal helpers | Busy/eligible DTO distinction -> V-1, R-1. Its adapter manufactures transcript entries; it cannot prove a Linux recipient. |
| `SessionQueueReceiptPlumbingTests.C475_AlreadyIdleWhenIdleHasRecipientReceipt`, all six arms of `C475_QueueCommitAndTransportRecovery`, `PtyWorld.StartAsync`, `RowsAsync`, `RecreateQueue`, `WaitForReceiptAsync`, `ForwardingClient`, `InsertFault`; all five arms of `C475_PumpPersistsCompleteLinesOnce` | Native recipient evidence plus private row/floor observation -> V-2, R-2/R-3. Native arms are Windows/FakeClaude; pump-only arms write synthetic files. Neither is the missing ordinary Linux fixture. |
| `LandDeliveryOptions.Configure`, `ConfigureServices`, `FileBoundary.ReachedAsync` | Existing commit/typing/verdict/receipt cuts belong to landing notifications. The public ordinary POST cannot reach those queue hooks; exclusion from S6 canary reuse. |
| `VerifyPhoneHomeGrokScriptTests` and `DelegateScriptRunner` | Nearest server2 script fixture; its desktop-Tailscale route conflicts with desktop-independent S6, so do not copy that acceptance topology. Local inherited PowerShell command tests remain a usable pattern. |
| `RunCheckpointScriptTests`, `ScriptHarness.RunHarnessCaseAsync` | Exact exit, named PASS inventory, zero execution, missing class, fresh-results and build-failure boundaries. These are command/result substitutes, not Docker/queue execution. Nearest fixture for new S3/S6 script tests. |
| `TestDbFixture`, `TestDbFixtureLifecycle` and all six `TestDbFixtureIsolationTests` methods | Lazy container initialization, migrate-once database clones, independent rows, four concurrent clones and disposal. Required ordinary Medium anchor; never initialize this fixture in sourced PCs. |
| `ProductionRunnerGuard`, both `ProductionRunnerGuardTests` methods, `RefusingSessionRunnerClient`, `AntiphonWebAppFactory` | Dead runner URL and refusing client are independent protections. Ordinary HTTP acceptance may use this factory; native session evidence may not. |
| `HealthEndpointTests` (both methods) | Non-unknown full SHA and `land-v2` are separate from health. Source-export builds must inject the revision; health cannot discharge native input or receipt. |
| `TestLaneCategoryGuardTests`, `TestClassificationMetadata`, `TestClassificationGuardTests` | Category/Slow metadata is not process or OS qualification. Partial declarations must merge into one class; compiled discovery still needs reconciliation. |
| `GrokDelegateDispatchTests.the_spec_a_grok_dispatch_builds_would_spawn_the_real_fakegrok_binary`, `SpecOf`, `CreateHarness`, `CreateDispatchHarness`, `BuildHarness`, `SeedWarmAgentAsync` | Actual S4 portable lookup candidate, but its dispatcher construction resolves a real DB context, and warm setup contains a Windows rules-receipt path. It is not a DB-free PC target. Whole-class Linux admission still needs its transitive audit. |
| `IsolatedSessionRunner` constructor/start/readiness, `StartProcess`, output and stop entry points | Suffix preparation is separate from E2E lifecycle acceptance. Retain Windows native fixture ownership and CARD-0588 boundaries. |
| `TestDbFixture.InitializeAsync`, `LandQueueRaceWorker.DispatchWorkerIfRequested`, `PtyBackendEnvGuard.ClearInheritedPtyBackend` | Method filters do not suppress assembly hooks. Clear inherited worker markers before a PC process; otherwise discovery can enter a DB worker before the selected test. |
| `Antiphon.Tests.csproj`, root build props/targets, server/runner/PtyHost/Pty project references; root Dockerfile/ignore/Compose and runner Dockerfile | PC builds include referenced runtime/fake projects and producer-copy targets even when selected tests do not launch them. Preserve source layout and isolate build outputs. |
| `FakeGrok` raw-console setup, main input loop, busy gate and UserPrompt append paths | Linux raw console setup is not implemented by the Windows-only helper; the planned test-only terminal wrapper remains necessary to qualify. The existing busy gate uses `[c467-busy]`, writes its held marker, and produces a real turn-end on release. Do not replace receipt with that marker. |
| Message endpoint handlers, queue DTOs, `EnqueueAsync`, attempt commit, typing/verdict hooks and `BuildQueueDtoAsync` | The S6 observation defect above -> V-1/V-2 and delivery cuts Q-2 through Q-6. |

Missing execution setup remains explicit: server2 amd64/Docker/Compose/tool versions, socket
GID and actual sibling mapped-port reachability, owned deployment/evidence roots, capacity,
password injection, image identities, and native launch qualification. None was observed or
claimed green here. Live read-only card checks on 2026-09-21 found CARD-0578, CARD-0587,
CARD-0588, CARD-0594 and CARD-0598 still Backlog. CARD-0578 records a SourceLanding isolated-build
failure before mutation; a future battery must establish its own baseline executable and logs,
not assume that dependency is solved by an ordinary build.

The frozen fake-apphost occurrence census was reproduced unchanged: **52 matching lines,
18 files, 20 `Path.Combine` lookup/assertion sites**. The preceding frozen table remains the S4
disposition inventory. The additional runner apphost lookup remains separate.

A Roslyn syntax census of the **789 tracked Antiphon.Tests C# files plus seven explicitly linked
sources** found **742 declarations containing direct `[Test]` methods**, merging to **698 fully
qualified class names** and **7,038 direct test-method declarations**. Sixteen names have multiple
partial declarations. These are source counts, not expanded TUnit cases or an audited portable
roster. This dispatch does not label all non-spawners portable from those numbers. In particular,
the 52-match occurrence inventory cannot provide a class denominator. No `linux-test-roster.json`
is issued, and the full Medium class/helper audit remains required after the S6 amendment.

### Delivery inventory

The foreground run's durable join is `(source SHA, run ID, parent project, parent session,
accepted generation, child project, recorded resource IDs, artifact digests)`. Its recipient
probe adds `(recipient session, accepted generation, immutable summary body/digest, queue row
ID, committed attempt floor, matching transcript sequence)`. A successful test result and its
delivery are separate outcomes; a completely received failure summary remains a failed run.

| Handoff | Producer -> destination | Persistence and recovery | Observable receipt / gap |
|---|---|---|---|
| F-1 | Parent server launch -> Raw command session -> S3 foreground entry point | Manifest before resource creation; retain parent session/generation and source SHA. A crash before manifest means no child-resource authority; never scan/adopt by prefix. | Actual command-session input/output/exit plus manifest linkage. A host `docker exec` invocation is excluded. |
| F-2 | S3 clean context -> Docker daemon -> child/test containers | Freeze SHA/image/project/resource identities before consuming results. Crash after creation retains explicit known/unknown residue; retry inspects recorded identities. | Real child UI/API/version/native probes and test results. Image build/container creation is not session delivery. |
| F-3 | Test containers -> copied reports -> `result-ready` manifest | Export and hash fresh TRX/Vitest reports before removal. Crash/export failure before atomic manifest replacement is incomplete; no result-ready inference from an exited container. | All expected classes and nonzero execution, actual exit status and matching artifact hashes. |
| F-4 | Foreground cleanup -> owned child resources | Recheck exact IDs/labels; preserve reusable parent volumes. Crash mid-cleanup preserves manifest and residue; explicit retry only. | Resource absence plus recorded successful scoped cleanup; neither a `down` request nor session exit proves absence. |
| Q-1 | Result-ready manifest -> message POST | Persist exact sanitized body and pre-POST observation before submission. Crash before POST or definite enqueue refusal retains result-ready/incomplete-delivery. | No recipient receipt until actual complete UserPrompt; explicit retry only after inspection. |
| Q-2 | Message POST -> committed ordinary queue row | Insert failure must leave no row/no typing. An eligible row can become Sent before response. Unknown POST acknowledgement must never trigger blind re-POST. | **Gap:** public POST/GET cannot reveal the completed row identity, floor or generation. A nonce-bearing transcript alone cannot establish queue-row recovery identity. |
| Q-3 | Pending row -> committed delivery attempt -> native typing | Busy recipient stays pending; eligible recipient takes immediate path. Crash after insert/before flush and after attempt commit/before typing must recover the same row. | **Gap:** planned fixture cannot deterministically hold ordinary rows at these internal cuts or export their committed floor/generation. |
| Q-4 | Typed body -> Enter -> native provider UserPrompt | Body-before-Enter cut distinguishes held composer from receipt. Complete prompt after original attempt floor is required; retry uses existing queue recovery. | Exact whole body including head/tail/run/SHA/outcome/digest in real FakeGrok transcript. Screen, transport ACK and `Sent` are excluded. |
| Q-5 | Provider transcript -> persisted server transcript -> queue verdict | Cut before transcript ingestion/save and after complete receipt/before verdict save. Recovery must retain row/floor and avoid typing a second body after confirmed receipt. | **Gap:** landing-only hooks do not cut this ordinary POST. Existing Windows private fixtures prove their own recovery path only. |
| Q-6 | Complete receipt -> verifier manifest acknowledgement | Crash before acknowledgement leaves exported original expectation/identity; restart inspects existing row/transcript and completes the same manifest without POST. | Complete matching UserPrompt in the recorded generation after the committed floor; public pending-only GET cannot supply the full inspection record. |

Mandatory combinations after the seam is specified: already eligible; busy then real turn-end;
insert refusal; acknowledgement lost after committed POST; pending-before-flush;
attempt-before-typing; body-before-Enter; recipient-before-ingestion;
transcript-save failure; receipt-before-verdict; receipt-before-manifest-save.
Each must terminate in the matching complete native UserPrompt or an explicit incomplete/failure
record with residue. The failure branches must not be counted as successful delivery. Cover a
changed generation, an old equal-body prompt at/below the floor, a prefix-only prompt, missing
tail, wrong run/SHA/digest, and a complete failure summary. Successful Raw execution and complete
FakeGrok receipt remain distinct gates. Container replacement cannot imply PTY adoption.

Local Windows script/result fixtures can prove refusal, identity validation, export ordering,
exit propagation and transcript **validation logic**. They cannot prove native input, Linux
execute permissions, actual Docker context filtering, daemon resource cleanup or recipient
delivery. Synthetic transcript pump tests cannot prove a provider received anything. No such
substitute discharges F-1, the native part of F-2, or Q-4/Q-5.

### Proves it works now

These are inspected evidence anchors for the rejection, **not executed green results**:

- V-1: busy/eligible response distinction | service + real DB, fake adapter |
  `SessionMessageQueueServiceTests.When_idle_message_is_held_while_the_agent_is_working`
  and `When_idle_and_agent_is_idle_the_message_is_delivered_right_away` |
  pending count one versus empty response after immediate delivery; no public returned sent-row ID.
- V-2: native recipient receipt does not imply a returned row ID | Windows real queue/PTY |
  `SessionQueueReceiptPlumbingTests.C475_AlreadyIdleWhenIdleHasRecipientReceipt` |
  empty response, exact file and DB UserPrompt, then private DB read supplies row and floor one.

All nine product acceptance outcomes above remain required. No image build, Linux native smoke,
server2 installation, Small/Medium execution or session-created stack was performed here.

### Guards the regression

- R-1: preserve immediate idle delivery rather than forcing an artificial busy interval to
  retain an API DTO | `SessionMessageQueueServiceTests.When_idle_and_agent_is_idle_the_message_is_delivered_right_away`;
  decisive existing assertions are empty pending response and submitted body.
- R-2: preserve durable queue recovery with recipient evidence |
  `SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`;
  six named cuts, same row ID, expected attempt count/floor, whole single file/DB UserPrompt,
  Enter-only or zero further writes as appropriate. Its Windows fixture is an ordinary
  regression owner, not a sourced PC or Linux canary substitute.
- R-3: preserve complete-line and UUID persistence |
  `SessionQueueReceiptPlumbingTests.C475_PumpPersistsCompleteLinesOnce`;
  partial-line/read-failure/save-failure/restart-after-commit/seeded-sequence arms assert one
  row with the original UUID/body and no other-session rows. Synthetic-file evidence only.

### Guard inventory

No complete implementation guard inventory is accepted by this rejection. The earlier
G-1..G-18 remain planning groups and must still be split independently before Code. In
particular Q-2/Q-3/Q-5/Q-6 require the observation/cut fixture before guard mutations can be
connected to decisive recipient assertions. Reporting `guards=N, mapped=N, missing=0` would
be false. **Audit disposition: failed admission to Code; zero accepted complete G/PC mappings.**
This is not a claim that the feature has zero safety guards, nor permission to omit their PCs.

### Positive controls

No executable post-land battery is commissioned here. The revised plan must give every split
guard its own exact method and compiling defect. Code authors tests and runs ordinary V/R;
Review judges before land; Mutation runs method-scoped break/red/restore/fresh-build/green on
Windows after land. No PC may initialize Testcontainers or invoke Docker, SSH or server2.

The inspected local build closure is `Antiphon.Tests` -> server, runner, FakeLlmApi,
NightlyWatchdog, messaging test helpers, CustodyTestChild, FakeClaude and FakeGrok; server/runner
bring Pty, PtyHost, PtyHost.Client, PtyHost.Protocol, messaging and runner contracts. The
project's MSBuild `GetTargetPath`/copy targets stage native/fake outputs but do not launch them.
No solution/AppHost build is needed for the proposed script/helper methods. Run inherited builds
with `MSBUILDDISABLENODEREUSE=1`, `UseSharedCompilation=false`, a forward-slash isolated
`OutputPath`, and build-server reuse disabled. Verify a fresh executable/log at the actual L;
CARD-0578 remains a known infrastructure dependency, not a waived failure.

Clear `ANTIPHON_C467_QUEUE_WORKER`, `ANTIPHON_C574_STARTUP_WORKER` and
`ANTIPHON_C478_DELIVERY_WORKER` before test launch (the respective constants in
`LandQueueRaceWorker`, `CodexStartupDeliveryWorker` and `PostLandMutationDeliveryWorker`).
Keep `ANTIPHON_C476_PROBE` under the test launcher so its lifecycle evidence
can assert `state=never-requested`, `create=0` and no container ID for local PC runs. Ordinary DB
and native queue tests above cannot be included in that PC process. This setup does not itself
prove inherited build custody or replace the native SourceLanding receipt.

### Out of scope

- Changing message API behavior, adding a DB observer or adding generic queue failpoints in
  this TestDesign dispatch. Their selection changes the currently specified S6 seam and belongs
  in the Plan amendment before the final verification manifest is frozen.
- Linux SourceLanding/remote Mutation: CARD-0598 owns that independent future capability.
  Do not reopen it to solve an ordinary message-observation defect.
- CARD-0587's root-image overlap and CARD-0588's four platform corrections retain their owners.
  CARD-0594 remains the native pipe/qualification dependency; a repeated failure blocks native
  acceptance, not permission to replace it with health-only checks.
- Chromium/browser E2E, native-assembly Linux parity, live providers and live messaging brokers
  remain excluded by the reconciled plan. Small and the non-spawning Medium majority are still
  the requested ordinary scope; neither has been silently reduced.

### Checkpoints

No Code checkpoint is issued: the required ordinary scope cannot be represented as a complete
executable closed list while the S6 observation/cut seam is absent. V-1/V-2 and R-1..R-3 above
identify existing bodies for Plan/TestDesign review, not additional authorized Code runs.
The empty table is a rejection gate, not a zero-test implementation profile.

| CP | After | Build | Group | Filter | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|

### Cost

- This dispatch performed **0 builds, 0 product test runs, 0 PC cycles**. It performed source
  inspection, a foreground Roslyn syntax census and read-only card checks.
- Issued ordinary Code V/R floor = sum of the empty CP table = **0 minutes**; issued Mutation
  floor = **0 minutes**; issued setup/build + V/R + PC execution total = **0 minutes**. These
  are explicitly **uncommissioned** floors, not a budget for the feature. No filter is issued.
- Estimated prerequisite work: **40 minutes** for a focused S6 Plan amendment (20 for owned
  observation contract, 20 for deterministic cut/receipt recovery ownership), then **180 minutes**
  for resumed TestDesign's transitive class audit, full guard split, exact checkpoint manifest
  and priced PC build/test closure. Estimated prerequisite total = **220 minutes**, excluding
  implementation and product/PC execution. Actual full Code/Mutation floors must be supplied
  by that completed design; guessing them here would conceal the unresolved fixture.
- Measured execution savings = **0 minutes**. No equivalent full battery was executed or timed.
  Zero commissioned execution is justified by the required return to Plan, not a claimed speedup.

Historical handoff: **next: plan**. Amend only S6's ordinary queue observation and deterministic cut seam,
including its edit set and evidence transport; preserve all nine acceptance outcomes and the
resolved ordinary/Windows-Mutation split. Then resume TestDesign to freeze the complete portable
class roster, independent G/PC mappings, exact checkpoints and numeric product execution floors.

## Current TestDesign handoff (04b55418)

The selected seam is now specified: test-only SELECT observer, native/stored UUID join,
Kestrel fixture host using existing EF/runner interfaces, durable file barriers and server-only
crash/restart. The API and production source edit set remain unchanged. D-17..D-22 and the S6
ordinary observation section supersede the historical missing-seam assessment. This is a
completed Plan amendment, not a green implementation or an executable verification manifest.

TestDesign must now:

1. Freeze the complete portable class/helper roster and S4 lookup inventory, preserving Small,
   Medium, all nine acceptance outcomes and the already-decided Windows-local Mutation limit.
2. Read the cited save, verdict, runtime fallback/dedup, forwarding, Kestrel and generation
   bodies; turn S6a..S6d into exact test ownership and independent guards. Account separately
   for stock-image behavior, fixture correctness, real recovery and local validation.
3. Map every Q-1..Q-6 boundary/mandatory combination to the selected cut or existing regression.
   Include all concurrent ingest paths, failed-save fallback, invisible/open-transaction refusal,
   stale release/restart, unknown acknowledgement, changed generation, retained versus replaced
   attempt floors, wrong/duplicate/partial receipt and explicit failure-summary outcomes.
4. Supply every guard's decisive assertion and method-scoped compiling PC variant. Keep local
   PC test setup free of DB startup, Docker, Testcontainers, server2 and external executors,
   including assembly hooks. Real DB/native cuts remain ordinary V/R, never substituted PCs.
5. Append a current `## Verification design` and closed `### Checkpoints` table with exact
   commands/filters, expected cases/nonzero counts and full ordinary scope. Include fixture
   publication/image isolation, actual default recovery-window costs and native dependency
   disposition; do not adopt either historical empty checkpoint table as executable scope.
6. Price setup/authoring, isolated build closure, ordinary runs and Windows post-land PC
   cycles separately. No estimated execution saving or zero-minute accepted floor is implied.

Plan validation at `04b55418`: source inspection and Markdown/diff checks only; **0 builds,
0 product test runs, 0 PC cycles**. No runtime image, native Linux behavior or server2 setup
was claimed verified. CARD-0594 and CARD-0598 boundaries are preserved, not re-decided.

## Verification design

Current TestDesign v3, task `062bfba3`, against Plan
`ceff11d83eee33b1ebbd45dee64a1821cd753dde`. This appended section is the executable
verification manifest and supersedes both historical rejection manifests, their V/R/G/PC
numbers, empty checkpoint tables and the preceding TestDesign handoff. It does not replace
the fix design. D-17â€“D-22 and S6aâ€“S6d are accepted. **Next: Code.** All results below are
requirements, not claims of executed product tests. Small plus Medium is the ordinary test
tier; no Linux SourceLanding or custody exception is introduced.

### Inspection

The companion [frozen class roster](2026-09-21-card-0590-linux-test-roster.json) is part of
this manifest. It contains **698 existing backend classes: 503 included (72.06%), 195
excluded**, all **24 messaging classes**, and **nine explicitly planned classes**. The
included backend classes declare 4,657 test methods; all backend classes declare 7,038.
Messaging declares 206 methods. These are source-method lower bounds, not parameter-expanded
execution counts. The census traversed bodies in 789 tracked backend C# files and seven
linked sources, merged partial declarations and inspected helper references rather than
equating filenames/categories with classes. Embedded C# strings in classification-policy
tests are not discovery classes. No actual inherited-test declaration adds a baseline class.

The roster records source files/digests, fully qualified identity, helper closure, lane,
exact shard, and exclusion boundary/owner for every baseline class. Code copies this
accounting into `tests/linux-test-roster.json`, adds the nine declared classes with their
explicit checkpoint lanes, and reconciles against compiled `TestClassificationMetadata.Read`
for each assembly before running. Additional discovered classes, stale names, duplicates,
unexpected skipped cases and unexecuted selected methods fail admission/evidence. A baseline
class cannot be removed or reclassified just because Linux is red. A changed helper closure
requires an explicit roster amendment and review. The planned command tests may spawn only
inherited local PowerShell children and are outside the non-spawning Medium shards.

| Bodies/fixtures read or syntax-audited | Boundary â†’ evidence |
|---|---|
| Root Dockerfile/ignore/Compose, runner Dockerfile, global/root build inputs, server/runner/PtyHost and both test csprojs | Full publish graph, SDK10/runtime9, linked files, tools/samples, embedded bundles and stage/context isolation â†’ V-1/V-3, R-1. No baseline image success inferred. |
| `VerifyPhoneHomeGrokScriptTests` and `DelegateScriptRunner` process/argv/exit/result bodies; `scripts/test-client.ps1` | Nearest script fixtures for new command classes. Inherited process, exact argv, real exit, source-root resolution, fake credential sentinels â†’ R-2/R-5/R-10. No primary credential store or real messaging call used. |
| `GrokDelegateDispatchTests.the_spec_a_grok_dispatch_builds_would_spawn_the_real_fakegrok_binary`, `SpecOf`, `TaskFor`, `CreateHarness`, `BuildHarness`, `SeedWarmAgentAsync`; `IsolatedSessionRunner.StartProcess`/readiness/stop | S4 staged-path consumer and diagnostic, no native process in the specification method. Whole Grok class excluded: warm fixture uses `C:\runner\instructions` receipts; the exact specification method is separately selected on Windows and Linux â†’ V-5/R-3/R-10. |
| `TestDbFixture`, `TestDbFixtureLifecycle`/`TestDbOperations`, `TestDbFixtureIsolationTests`, `TransactionalTestBase`, assembly worker hooks | Lazy PostgreSQL; real migrated/cloned databases, rollback and disposal â†’ V-4/R-4. Worker markers must be cleared. Method selection alone does not suppress assembly hooks. No DB fixture is activated by a PC. |
| `ProductionRunnerGuard` and both test bodies, refusing client, `AntiphonWebAppFactory`, `MockedFileSystemWebAppFactory`, both `HealthEndpointTests` methods | Independent dead URL and refusing client, per-factory database, startup disablement, SHA and land-v2 â†’ V-4/R-2. These factories clear health probes: they cannot prove stock image health or native delivery. |
| `TestClassificationMetadata`, `TestClassificationGuardTests`, `TestLaneCategoryGuardTests`, syntax bodies/attributes and helper closure in the roster | Unit/Integration and Slow are not portability evidence. Unknown class and class-prefix overmatch rejected â†’ R-4. `Application.SpecialistToolPolicyTests` is namespace-qualified because `Agents.SpecialistToolPolicyTests` is native. |
| `BridgeQueueHarness` construction/adapter registration/seed/disposal, `FakeAgentProtocolAdapter`, `RemoteControlRecoveryHarness`, scripted/recording/refusing runner clients | Real queue/DB with simulated receipt generation, no native child â†’ V-4/R-8. Synthetic UserPrompt cannot qualify S6. |
| `ControlledLandingGit` construction/command boundary, `LandingProtocolHarness` DI/save/transaction fault seams, `DelegationTestServices`; `AgentTaskLandRequestTests` null-column branch | Controlled Git/refusal paths are admitted, real Git/cleanup/verification children excluded. Merely constructing a service that can spawn is distinguished from invoking its process path â†’ V-4/R-4. |
| `StandingRecoveryFixture`, `AgentControlServiceIntegrationTests` launch harness, `OutputDistillationHarness`, `HerdrLabelFollowDbFixture`, `HerdrDisposalHttpFixture` and linked `HerdrPaneDisposalFixture`/`FakeHerdrServer`, `PhoneHomeTestHost` | Fake adapters/worktree managers, in-process HTTP/named-pipe peers and owned DBs. No live Herdr/runner or installed provider required â†’ V-4. Windows-looking wire/DTO strings alone do not exclude a class. |
| `WorkspaceHookRunnerTests` script creation/RunAsync/timeout; native/ProcessSpawnLimit-bearing closures | Indirect shell launch is an explicit exclusion even without Process.Start in the test. Native, real Git, script-child and opt-in closures are accounted individually in the roster â†’ R-4, Windows original lanes remain owners. |
| `SessionMessageQueueServiceTests` busy/idle, separate CR, multiline, CreateHarness; `SessionMessageQueueInterruptedAttemptTests` late-confirm/Enter-only/retype; generation/composer tests in `SessionMessageQueueWedgedHeadTests` | Real service behavior and generation/charge/floor boundaries â†’ R-8; service fixtures shorten clocks and seed records, so do not replace Linux crash acceptance. |
| `SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`, idle/pump/multiline methods; `PtyWorld.StartAsync`, `ForwardingClient`, `InsertFault`, transcript pump | Existing Windows native receipt regression â†’ R-9. Six recovery arguments, five pump arguments, idle and multiline = 13 expanded cases. Exception-plus-failed-revert and shifted clock are not hard-crash evidence. |
| `AgentSessionRuntime.PersistTranscriptAsync`/individual retry/stub handling/UUID dedup/rebase; queue attempt save, input and verdict; `SessionHealthHostedService` and `DeliveryVerificationSettings` | S6b must gate batch, individual and stub, both SSE and pull, and compare stored sequence domain â†’ V-9/V-10/R-6/R-7. Default interrupted age 30+20+30=80 seconds; stranded age 60 seconds; sweep cadence 60 seconds; recovery window 60 minutes. |
| `AntiphonAppFixture.KestrelWebApplicationFactory.CreateHost` and overrides | Nearest real-Kestrel fixture for the new console host: retain only hosting technique, not E2E mock/Windows settings or removed health checks â†’ V-10/R-7. |
| FakeGrok console setup/input/busy gate/UserPrompt append bodies | Existing raw console setup is Windows-only; Linux wrapper qualification and real complete native prompt required â†’ V-6/V-8/V-9. `[c467-busy]` and its real release turn are control evidence, never receipt. |
| Messaging csproj, broker setup/disposal in `InboxConsumerServiceTests` and `KafkaConsumerGroupObservationTests`, live-chat fake/real split, gateway fake-server fixtures | Small enables `ANTIPHON_BROKER_TESTS=1`, supplies Docker and requires real Redpanda connections; fake Telegram/Slack legs run, live credential legs are deliberately absent â†’ V-3/R-2. |

S4 retains the earlier **52 matching lines / 18 files / 20 fake lookup sites** table unchanged.
Only `GrokDelegateDispatchTests.cs:267` consumes the new fake path helper. The separate E2E
runner lookup/diagnostic also consumes it. The other sites retain their native owners; no
bulk `.exe` replacement. Helper cases cross Windows/Linux Ã— fakeclaude/fakegrok/runner Ã—
existing/missing Ã— correct/wrong sibling directory. New files use the nearest fixtures above.

Missing execution setup is explicit, not waived: server2 amd64 and Docker/Compose versions,
socket GID/permissions, actual sibling mapped-port DB connectivity, owned ordinary checkout
under `/work/repos`, private password/observer files, non-root volume ownership, disk/capacity,
chosen source SHA/image IDs and native Linux qualification. Code records these before Docker
acceptance; an unavailable prerequisite is a failed/blocked checkpoint. CARD-0594 can block
native launch/recovery; CARD-0598 remains outside this feature. CARD-0578 can block the later
Windows isolated PC build and must be reported at actual L. None is assumed fixed here.

### Delivery inventory

Run join: `(source SHA, run ID, parent project/resources, parent session, accepted generation,
child project/resources, roster hash, result artifact digests)`. Receipt join adds `(case and
submission nonce, recipient session, runner/store and accepted StartedAt, immutable normalized
body/hash, queue high-water and Id, attempt number/start/generation/server floor, native UUID,
stored UUID/kind/body/server sequence)`. PostgreSQL microsecond precision applies to generations.
Each typing attempt is separately persisted; Enter-only recovery retains the original tuple.

| Path | Producer â†’ destination | Persistence boundary and recovery | Observable receipt / tests |
|---|---|---|---|
| F-1 | Parent server â†’ Raw command session â†’ S3 foreground controller | Initial run manifest before any Docker operation, bound to actual accepted launch. Interrupt before manifest gives no child-resource cleanup authority. | Input/output/terminal exit plus session/run join; V-6/V-7, R-5. A host docker exec does not qualify. |
| F-2 | Controller clean source â†’ daemon â†’ child/test containers | Source/image IDs and exact project/resource ownership recorded. Unknown create result retained as unresolved action, not adopted by prefix. | Real child UI/API/version/native evidence and complete executed roster; V-1â€“V-4/V-7/V-13, R-1/R-2/R-5. |
| F-3 | Test processes â†’ copied reports â†’ result-ready | All reports copied/hashed before atomic manifest replacement and before removal. Export failure/crash retains incomplete outcome/evidence/resources. | Fresh TRX/Vitest results, per-class executed set, original exit and digests; V-3/V-4/V-11, R-2/R-5. |
| F-4 | Foreground cleanup â†’ owned resources | Action intent then fresh exact ID+label inspection, delete, observed absence. Interrupted cleanup resumes same manifest, rechecks identities; parent/reusable volumes retained. | Actual owned resource absence and retained foreign sentinels; V-2/V-7/V-11, R-5. A down request alone is insufficient. |
| Q-1 | Result-ready â†’ immutable expectation â†’ ordinary Ui POST | Body/hash/queue high-water persisted before POST. Pre-POST crash resumes original expectation. Definite refusal and unknown ack remain different states. | No delivery claim before complete native+stored receipt; R-5/R-6, V-8/V-9. |
| Q-2 | POST â†’ committed ordinary queue row | All-status parameterized SELECT; freeze exactly one unbound Ui Id. Insert-refused leaves zero rows/input/prompts; response-before-client produces unknown ack. Owner must end before absence can be decided. | Stock eligible POST may return empty; observer still finds Sent row/attempt=1. Same Id after unknown ack; no automatic re-POST; V-8/V-9/V-10, R-6/R-7. |
| Q-3 | Pending â†’ committed attempt â†’ typing | Independently visible insert has attempts=0/null tuple. Attempt has Sent/1/actual floor+generation, no verdict. Export per-context committed state before hard-cutting fixture server. | Recovery of insert uses same row; fresh typing after attempt cut charges exactly once more and appends tuple; V-9/V-10, R-6â€“R-9. |
| Q-4 | Body â†’ separate Enter â†’ FakeGrok | Body-before-enter barrier records actual forwarded/returned bytes and composer. Hard server crash keeps runner/PTY/generation. | Whole native UserPrompt once. Enter-only sends no second body and retains attempt/floor; V-9, R-6â€“R-9. |
| Q-5 | Native provider record â†’ server persistence â†’ verdict | Gate selected UUID in both SSE/pull; fail batch/individual/stub saves while armed; hold verdict before save. Hard restart keeps DB+runner. No manual transcript/row/age edits. | Same UUID/kind/full body stored above server floor, same generation. Native-only = server-pending. Receipt recovery has zero further body/Enter; V-9/V-10, R-6â€“R-9. |
| Q-6 | Validated receipt â†’ final acknowledgement manifest | Export actual receipt first; supervised receipt worker stops before final replace. Resume independently reads original files/DB/runner, never POSTs. | Same manifest/row/attempt/receipt; final durable acknowledgement only after revalidation; V-9/V-11, R-5â€“R-7. |

Native cases below use one dedicated receipt stack per case, separated from parent and test
child. Reuse the actual exported run result for success cases. The failed-summary case runs
a deliberately failing owned command (exit 23) and delivers that real failure. Case controller
uses the test host only for cuts; `stock-idle`/`stock-busy` use the stock server image. All use
the socket-free receipt runner/observer. Bootstrap every recipient with a genuine completed
native turn. Make the test recipient always-on through normal configuration so the existing
stranded sweep applies; keep normal session health watch enabled. Do not manufacture TurnEnd.

| Exact case | Cut/recovery and required final evidence | Covers |
|---|---|---|
| `stock-idle` | Already eligible, empty pending response allowed, one queue row/attempt and exactly one native+stored whole prompt. | V-8, Q-2/Q-4/Q-5 |
| `stock-busy` | Real busy gate; Pending/0/null tuple/no measured input; release causes real TurnEnd, then same row and complete receipt once. | V-8, Q-2/Q-3 |
| `insert-refused` | Named pre-save failure; failed request ends, zero rows/body/Enter/native prompt; explicit new submission recorded, then complete receipt. | V-9, Q-1/Q-2 |
| `insert-committed-idle` | Independent Pending/0/null observation before inline flush; SIGKILL only server, restart against original state; same row becomes attempt=1, receipt once. | V-9, Q-3 |
| `insert-committed-busy` | Same independently visible insert while busy; release barrier, then real busy turn-end; no crash needed for this boundary combination. | V-9, Q-3 |
| `attempt-committed` | Independent Sent/1/floor/generation/no input; server crash/restart; same Id, exactly attempt=2, two immutable tuples, body+Enter once after recovery, receipt once. | V-9, Q-3 |
| `body-before-enter` | Body write returned, CR not forwarded, no native prompt; server crash/restart; only one additional CR, original tuple retained, complete receipt once. | V-9, Q-4 |
| `recipient-before-ingestion` | Independent native receipt and no stored UUID while both ingestion paths held; crash/restart disarmed; same UUID stored and late-confirmed, zero additional input. | V-9, Q-5 |
| `transcript-save-fails-release` | Real nontransient DbUpdateException forces batch/individual/stub refusal by UUID, exported native receipt/stored absence; disarm and normal catch-up, one full stored row, zero duplicate input. | V-9, Q-5 |
| `transcript-save-fails-restart` | Same fault coverage, then server-only hard cut/restart disarmed; original generation/row/floor survives and receipt is persisted once. | V-9, Q-5 |
| `receipt-before-verdict` | Native+stored complete receipt present; committed Sent/null verdict/original tuple; server crash/restart late-confirms with zero further body/Enter. | V-9, Q-5 |
| `response-before-client` | Endpoint complete and row exported; buffered Start/headers/body/flush all withheld. Kill server, observe unknown ack; same row/receipt, total POST count one. | V-9, Q-2/Q-6 |
| `receipt-before-manifest` | Original receipt exported; worker stops before final manifest replace. Await worker, resume observer with original files; zero resume POST, same row/receipt/manifest. | V-9, Q-6 |
| `changed-generation` | After recording an attempt, explicitly replace only this negative-case recipient generation through the supported API; old manifest is refused even if old body remains. Not counted as successful recovery. | V-9, R-6 |
| `failure-summary` | Actual run exit 23; whole native+stored failure summary delivered; delivery confirmed, overall run still failed. | V-9/V-11 |

Stock busy/eligible plus one independent cut at each handoff is the required native matrix.
Full busyÃ—every-cut Cartesian expansion adds no distinct branch after attempt acquisition:
busy is covered before insertion/eligibility and the controlled attempt starts only when idle.
Partial/prefix/missing-tail, wrong run/case/SHA/outcome/digest, old equal-body at/below floor,
native versus stored sequence crossing, same/different/missing generation, duplicate row/UUID/body,
null/no-attempt versus committed tuple, stale/replayed arm/release/host, open/invisible transaction,
and unknown-ack zero-row cases are separate local/DB boundary matrices under R-5â€“R-7/V-10.
Every rejection test also supplies a valid near-neighbor that must be accepted/forwarded so
an always-refuse implementation fails. Native evidence is still required for all happy recovery arms.

Substitutes are explicit: config/ignore parsing and harmless sentinel files prove local policy,
not Docker's actual context semantics; recorded command adapters prove selected argv/order/status,
not daemon ownership or process exit; controlled DB callbacks prove validator/interceptor selection,
not commit visibility; synthetic transcript records prove validator rejection, not provider receipt.
Ordinary PostgreSQL tests prove transactions/permissions and actual runtime fallback paths, while
ordinary native Linux cases prove the provider and stock/fixture transport. Windows queue fixtures
prove their existing native path only. No request, inserted row, event, Sent/verdict or transport ACK
is delivery evidence. No substitute discharges the complete native+stored UserPrompt join.

### Proves it works now

- V-1: clean old-build reproduction and fixed images | Docker | CP image rows | old build failure accurately captured, new server/client/bundles and executable runner payload with full SHA; actual context sentinels excluded.
- V-2: private fresh deployment and durable state | server2 Docker/API | deployment-state checkpoint | three healthy services, migrations/UI/API/version correct, DB sentinel survives recreation with same volumes, bidirectional same-path workspace, normal down retains volumes.
- V-3: Small | real Linux tooling/client/messaging | client and messaging checkpoints | lint/build, full Vitest JSON and all 24 messaging classes/â‰¥206 methods, broker opt-in exercised, no unexplained skip/failure or lost exit.
- V-4: Medium | real Linux TUnit/PostgreSQL/HTTP | 15 frozen shard checkpoints | 503 classes/â‰¥4,657 methods executed and all discovered classes accounted; required DB isolation/production-runner/version anchors present.
- V-5: portable apphost resolution | local files plus Linux staged payload | helper/Grok-spec/E2E compile checkpoints | both platform branches, existing/missing and sibling producer paths, staged Linux executable; no E2E parity claim.
- V-6: native Raw launch/input/output/exit | actual Linux runner/PtyHost | raw-session checkpoint | unique input/output marker and bounded terminal exit, recorded session/generation; dependency failure remains red.
- V-7: session-created child stack | actual server-launched Raw command session | session envelope, child-build/test/export/cleanup checkpoints | all subordinate CP results linked to parent session, distinct child resources and one source/result manifest, original exit preserved.
- V-8: stock image delivery | real queue/DB/FakeGrok | stock-idle and stock-busy checkpoints | whole single native UserPrompt plus matching persisted UUID/body above committed floor in original generation.
- V-9: recovery cuts | real fixture server/DB/FakeGrok | all 13 remaining native cases | each declared barrier reached independently; required same-row/charge/tuple/write counts and receipt, or exact negative-case refusal.
- V-10: observation/fixture database semantics | real PostgreSQL, real Program/Kestrel with scripted isolated runner | `DockerDeliveryDatabaseTests` | 16 methods below, all executed, no production runner, no fake commit visibility.
- V-11: failure/export/cleanup interruption | ordinary real daemon plus local boundary controls | interrupted-export/denied-socket/failure-summary and command checkpoints | no false result-ready/delivery/cleanup success; retained evidence/residue and explicit recovery.
- V-12: ordinary/custody separation | effective images/Compose/capability response and local scripts | packaging/command/native capability gates | base/receipt services socket-free, only explicit parent runner/test services authorized; sourced/unreadable/outside-root input refused before Docker; Linux does not advertise custody.
- V-13: independent server2 installation | same ordinary entry points | final deployment handoff checkpoint | container IDs, endpoint/route checks and an observed API+session operation after initiating desktop CLI disconnect; no app call back to desktop.

V-10's exact new methods, all in `DockerDeliveryDatabaseTests`:
`Observer_finds_sent_row_after_empty_post_response`, `Observer_role_refuses_writes`,
`Observer_snapshot_is_coherent`, `Observer_lookup_rejects_duplicate_rows`,
`Insert_reached_is_visible_from_second_connection`, `Attempt_reached_is_visible_from_second_connection`,
`Open_outer_transaction_never_reaches_committed_cut`, `Failed_save_emits_no_reached_record`,
`Concurrent_context_saves_do_not_cross_observe`, `Verdict_hold_leaves_committed_verdict_null`,
`Runtime_batch_individual_and_stub_saves_remain_faulted`, `Sse_and_pull_join_same_receipt_gate`,
`Buffered_response_waits_after_endpoint_completion`, `Observe_mode_does_not_boot_Program`,
`Serve_keeps_migrations_health_and_real_runner`, `Restarted_observer_uses_frozen_identity`.
Use separate Npgsql connections and owned isolated databases/roles, not a tracked EF read.
The runtime-fallback method must invoke actual `AgentSessionRuntime` and observe all three
save paths, including stub-kind mutation, rather than merely calling the interceptor three times.

### Guards the regression

- R-1: stage/context/config/native-payload/privacy regression | all `DockerStackContractTests` methods named by the PCs; actual files are inputs, assertions name missing artifact/incorrect effective setting; real image inspections remain V-1/V-12.
- R-2: silent test loss, status hiding, external-runner/live-credential access, or sourced input | named `DockerTestCommandTests` methods; capture actual script execution via inherited child/recording command boundary, assert no forbidden command and exact result/exit.
- R-3: suffix/producer/diagnostic regression | named `TestAppHostPathTests` methods, both OS choices and real staged Linux file; missing path is a failure rather than a successful skip.
- R-4: class roster drift/unsafe admission | named `LinuxTestRosterTests`, compiled identity reconciliation and every frozen shard's executed class/method comparison; real DB-isolation and production-runner guards remain included.
- R-5: owner/manifest/cleanup/native-gate/result-delivery regression | named `DockerStackSmokeCommandTests` and `DockerDeliveryRecoveryCommandTests`; decisive command trace, durable manifest state and failed-run status checks.
- R-6: weak observation or false receipt | named `DockerDeliveryObservationTests`; full native+stored join and explicit rejection codes, with valid neighbors accepted.
- R-7: fixture falsely claims a reached cut or alters unrelated traffic | named `DockerDeliveryBarrierTests`, plus V-10's DB methods; independent visibility, no response bytes before release, all fallback/ingestion paths, persisted one-shot identity.
- R-8: existing queue behavior | full `SessionMessageQueueServiceTests`, `SessionMessageQueueInterruptedAttemptTests`, `SessionMessageQueueWedgedHeadTests` in their Medium shards; busy holds, idle immediate delivery, separate CR/paste, same-generation Enter-only, changed-generation retype, durable charge/floor and late-confirm assertions.
- R-9: existing Windows recipient/persistence regression | exact four-method `SessionQueueReceiptPlumbingTests` selection in CP-2, 13 expanded cases; original native and DB UserPrompt, row/attempt/floor and write-count assertions remain intact.
- R-10: unchanged Windows/helper/phone-home contract | exact Grok staged-spec method, `VerifyPhoneHomeGrokScriptTests.Compose_and_dockerfile_do_not_hardcode_server_origin`, and E2E compile/payload check; original Grok version, no baked auth/origin, OS apphost consumer preserved.

### Guard inventory

Each row below is a scoped guard protecting this packaging/verification/delivery harness;
its PC number is unique. Existing production queue/custody implementation is not changed:
R-8/R-9 preserve its behavior, and this manifest inventories every new/changed acceptance,
configuration, observation, barrier and recovery guard. A shared tuple check is tested with
each field independently wrong. Independent stages, callback paths, receipt domains and
context policy entries have separate PCs. No untested guard is waived.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | S1/D-3: Server project-reference closure reaches publish | PC-1 |
| G-2 | S1: Static client is in the final server stage | PC-2 |
| G-3 | S1: Embedded bundle sources survive context filtering | PC-3 |
| G-4 | S1/D-3: SDK matches global.json | PC-4 |
| G-5 | S1-S2: net9 linux-x64 runtime contract | PC-5 |
| G-6 | S2/D-4: Linux PtyHost apphost is staged | PC-6 |
| G-7 | S2/D-4: Linux native library is staged | PC-7 |
| G-8 | S2/D-4: Host managed runtime payload is staged | PC-8 |
| G-9 | S2/D-4: Host execute permission is required | PC-9 |
| G-10 | S1/S2/S6c: server publish propagates full revision | PC-10 |
| G-11 | S1/S2/S6c: runner publish propagates full revision | PC-11 |
| G-12 | S1/S2/S6c: fixture publish propagates full revision | PC-12 |
| G-13 | S6: Observed version must equal chosen source | PC-13 |
| G-14 | S2/D-2: Runner route stays inside Compose | PC-14 |
| G-15 | S2/D-2: Application database stays inside Compose | PC-15 |
| G-16 | S2/D-2: antiphon phone-home disabled | PC-16 |
| G-17 | S2/D-2: session-runner phone-home disabled | PC-17 |
| G-18 | S2/D-11: session-runner has no published port | PC-18 |
| G-19 | S2/D-11: postgres has no published port | PC-19 |
| G-20 | S2/D-11: Default server bind is loopback | PC-20 |
| G-21 | S2/D-11: Server waits for healthy postgres | PC-21 |
| G-22 | S2/D-11: Server waits for healthy session-runner | PC-22 |
| G-23 | S2/D-11: antiphon has an executable health probe | PC-23 |
| G-24 | S2/D-11: session-runner has an executable health probe | PC-24 |
| G-25 | S2/D-6: Base antiphon has no socket | PC-25 |
| G-26 | S2/D-6: Base session-runner has no socket | PC-26 |
| G-27 | S2/D-6: Testing override gives only intended runner socket | PC-27 |
| G-28 | S2/D-6: Socket GID is checked before launch | PC-28 |
| G-29 | S2/D-6: No privileged/socket chmod fallback | PC-29 |
| G-30 | S2/D-10: Apps execute as non-root shared identity | PC-30 |
| G-31 | S2/D-10: Foreign state is not recursively adopted | PC-31 |
| G-32 | S2/D-10: State/work paths are absolute Linux paths | PC-32 |
| G-33 | S2/D-10: Same workspace path is shared | PC-33 |
| G-34 | S2/D-10: Database and app state use persistent named volumes | PC-34 |
| G-35 | S2/D-4: Linux backend remains inbox/Porta | PC-35 |
| G-36 | S2/D-4: Linux Herdr remains disabled | PC-36 |
| G-37 | S2/D-9: Fresh stack disables unattended automation | PC-37 |
| G-38 | S2: Unsupported Linux secret protector is not called ready | PC-38 |
| G-39 | S2: Keyring is private and outside app payload | PC-39 |
| G-40 | S1/S3/context: Runtime context denies .git | PC-40 |
| G-41 | S1/S3/context: Runtime context denies .antiphon/case.json | PC-41 |
| G-42 | S1/S3/context: Runtime context denies client/.env.local | PC-42 |
| G-43 | S1/S3/context: Runtime context denies scratch/auth.json | PC-43 |
| G-44 | S1/S3/context: Runtime context denies .grok/config.toml | PC-44 |
| G-45 | S1/S3/context: Runtime context denies scratch/key.pfx | PC-45 |
| G-46 | S1/S3/context: Runtime context denies server/bin/x.dll | PC-46 |
| G-47 | S1/S3/context: Runtime context denies server/bin-pc/x.dll | PC-47 |
| G-48 | S1/S3/context: Runtime context denies server/obj/x | PC-48 |
| G-49 | S1/S3/context: Runtime context denies client/node_modules/x | PC-49 |
| G-50 | S1/S3/context: Runtime context denies workspace/owned.txt | PC-50 |
| G-51 | S1/S3/context: Runtime context denies logs/test.log | PC-51 |
| G-52 | S1/S3/context: Tests context denies .git | PC-52 |
| G-53 | S1/S3/context: Tests context denies .antiphon/case.json | PC-53 |
| G-54 | S1/S3/context: Tests context denies client/.env.local | PC-54 |
| G-55 | S1/S3/context: Tests context denies scratch/auth.json | PC-55 |
| G-56 | S1/S3/context: Tests context denies .grok/config.toml | PC-56 |
| G-57 | S1/S3/context: Tests context denies scratch/key.pfx | PC-57 |
| G-58 | S1/S3/context: Tests context denies server/bin/x.dll | PC-58 |
| G-59 | S1/S3/context: Tests context denies server/bin-pc/x.dll | PC-59 |
| G-60 | S1/S3/context: Tests context denies server/obj/x | PC-60 |
| G-61 | S1/S3/context: Tests context denies client/node_modules/x | PC-61 |
| G-62 | S1/S3/context: Tests context denies workspace/owned.txt | PC-62 |
| G-63 | S1/S3/context: Tests context denies logs/test.log | PC-63 |
| G-64 | S3/D-5: Required linked test and fixture context retained | PC-64 |
| G-65 | S3/D-5: Test target has required runtime/tools | PC-65 |
| G-66 | S3: Missing Docker cannot be skip-success | PC-66 |
| G-67 | S3: Mapped Testcontainers endpoint must connect | PC-67 |
| G-68 | S3: Broker tests are explicitly enabled | PC-68 |
| G-69 | S3: Live gateway/provider credentials never inherited | PC-69 |
| G-70 | S3: Nonzero command status reaches caller | PC-70 |
| G-71 | S3: Absent report cannot prove execution | PC-71 |
| G-72 | S3: Zero execution cannot pass | PC-72 |
| G-73 | S3: Every selected class must execute | PC-73 |
| G-74 | S3: Skipped selected cases cannot pass | PC-74 |
| G-75 | S3: Report and exit must agree | PC-75 |
| G-76 | S3: Reports exported before removal | PC-76 |
| G-77 | S3: Report digest is validated | PC-77 |
| G-78 | S3: Test commands preserve dead runner override | PC-78 |
| G-79 | S3: Tests retain refusing-client factory | PC-79 |
| G-80 | S3/D-15: Command group cannot inject arbitrary shell | PC-80 |
| G-81 | S4: Windows apphost suffix preserved | PC-81 |
| G-82 | S4: Linux apphost has no exe suffix | PC-82 |
| G-83 | S4: Producer sibling output used | PC-83 |
| G-84 | S4: Missing apphost is a named failure | PC-84 |
| G-85 | S4: E2E runner consumes portable lookup | PC-85 |
| G-86 | S5: Unknown class fails admission | PC-86 |
| G-87 | S5: Duplicate class fails admission | PC-87 |
| G-88 | S5: Stale roster entry fails admission | PC-88 |
| G-89 | S5: Native class cannot be included | PC-89 |
| G-90 | S5: Spawner/helper closure cannot be included | PC-90 |
| G-91 | S5: Exclusion requires reason and owner | PC-91 |
| G-92 | S3/S6/D-13: SourceLanding task is refused before Docker | PC-92 |
| G-93 | S3/S6/D-13: Unreadable task is never classified ordinary | PC-93 |
| G-94 | S3/S6/D-13: Managed verification paths are refused | PC-94 |
| G-95 | S3/S6/D-15: Source must be owned ordinary checkout | PC-95 |
| G-96 | S6: Parent and child projects differ | PC-96 |
| G-97 | S6: Run manifest precedes resource creation | PC-97 |
| G-98 | S6: Child context source is frozen SHA | PC-98 |
| G-99 | S6: Container binds are in daemon namespace | PC-99 |
| G-100 | S6: Resource IDs must match recorded owner | PC-100 |
| G-101 | S6: Resource labels must match recorded owner | PC-101 |
| G-102 | S6: Reusable deployment volumes survive normal down | PC-102 |
| G-103 | S6: Cleanup residue prevents complete success | PC-103 |
| G-104 | S6: Interrupted action resumes against same identities | PC-104 |
| G-105 | S6: No global prune/adoption by prefix | PC-105 |
| G-106 | S6/D-8: Raw challenge includes input and output identity | PC-106 |
| G-107 | S6/D-8: Raw completion requires bounded exit | PC-107 |
| G-108 | S6/D-15: Test command must originate in launched session | PC-108 |
| G-109 | S6/D-15: Child mode cannot recurse into parent qualification | PC-109 |
| G-110 | S6/D-21: Default runtime excludes fixture/FakeGrok payload | PC-110 |
| G-111 | S6/D-21: Stock server excludes fixture dependencies | PC-111 |
| G-112 | S6/D-21: Receipt stack gets no Docker authority | PC-112 |
| G-113 | S6/D-13: Linux capability refusal remains required | PC-113 |
| G-114 | S6a/D-17: Observer accepts only immutable owned configuration | PC-114 |
| G-115 | S6a/D-17: Observer cannot accept arbitrary connection/SQL | PC-115 |
| G-116 | S6a/D-17: Observer role is SELECT-only | PC-116 |
| G-117 | S6a/D-17: Query parameters bind session | PC-117 |
| G-118 | S6a/D-17: Query uses committed queue high-water | PC-118 |
| G-119 | S6a/D-17: Query matches full body | PC-119 |
| G-120 | S6a/D-17: Sent and Canceled are observable | PC-120 |
| G-121 | S6a/D-17: Only unbound Ui origin accepted | PC-121 |
| G-122 | S6a/D-17: Multiple matching queue rows are ambiguous | PC-122 |
| G-123 | S6a/D-22: Frozen queue identity cannot be replaced | PC-123 |
| G-124 | S6a/D-20: Never-attempted row retains null tuple | PC-124 |
| G-125 | S6a/D-20: Attempted row requires committed floor | PC-125 |
| G-126 | S6a/D-20: Attempts are append-only evidence | PC-126 |
| G-127 | S6a/D-20: Complete native UserPrompt is mandatory | PC-127 |
| G-128 | S6a/D-20: Expected body hash is checked independently | PC-128 |
| G-129 | S6a/D-20: Summary markers match run/case/SHA/outcome/evidence | PC-129 |
| G-130 | S6a/D-20: Stored UUID/kind/body joins native receipt | PC-130 |
| G-131 | S6a/D-20: Stored sequence must be strictly above floor | PC-131 |
| G-132 | S6a/D-20: Sequence domain is server, never native | PC-132 |
| G-133 | S6a/D-20: Native duplicates invalidate canary | PC-133 |
| G-134 | S6a/D-20: Stored duplicate UUID/kind invalidates evidence | PC-134 |
| G-135 | S6a/D-20: Native-only receipt is server-pending | PC-135 |
| G-136 | S6a/D-20: Generation checked before observation | PC-136 |
| G-137 | S6a/D-20: Generation checked after observation | PC-137 |
| G-138 | S6a/D-20: Missing generation is not inferred | PC-138 |
| G-139 | S6a/D-20: Generation normalization is microsecond equality | PC-139 |
| G-140 | S6b/D-18: Decorator preserves untargeted runner operations | PC-140 |
| G-141 | S6b/D-18: SSE path withholds selected native UUID | PC-141 |
| G-142 | S6b/D-18: Pull path withholds same selected UUID | PC-142 |
| G-143 | S6b/D-18: Body write completes before Enter cut | PC-143 |
| G-144 | S6b/D-18: Multiline forwarding is byte-preserving | PC-144 |
| G-145 | S6b/D-19: Metadata captured before EF state reset | PC-145 |
| G-146 | S6b/D-19: Failed save emits no committed cut | PC-146 |
| G-147 | S6b/D-19: Commit requires independent visible observation | PC-147 |
| G-148 | S6b/D-19: Open outer transaction refuses committed cut | PC-148 |
| G-149 | S6b/D-19: Context-local pending metadata cannot leak | PC-149 |
| G-150 | S6b/D-18: Batch transcript failure covers selected UUID | PC-150 |
| G-151 | S6b/D-18: Individual retry remains faulted | PC-151 |
| G-152 | S6b/D-18: Stub retry remains faulted by UUID | PC-152 |
| G-153 | S6b/D-18: Selected verdict is held before save | PC-153 |
| G-154 | S6b/D-19: Arm identity includes submission/row/attempt | PC-154 |
| G-155 | S6b/D-19: Release binds exact host incarnation | PC-155 |
| G-156 | S6b/D-19: Consumed arm persists across host restart | PC-156 |
| G-157 | S6b/D-19: Deadline/cancel does not release cut | PC-157 |
| G-158 | S6b/D-18: Response headers cannot acknowledge held POST | PC-158 |
| G-159 | S6b/D-18: Response body/flush cannot acknowledge held POST | PC-159 |
| G-160 | S6b/D-18: Unarmed responses pass status/header/body | PC-160 |
| G-161 | S6b/D-18: DI decorates real registration once | PC-161 |
| G-162 | S6c/D-22: Immutable expectation precedes POST | PC-162 |
| G-163 | S6c/D-22: Unknown acknowledgement never retries POST | PC-163 |
| G-164 | S6c/D-22: Zero rows while request alive is unknown | PC-164 |
| G-165 | S6c/D-22: Explicit refusal retry needs no-row/no-input/no-prompt | PC-165 |
| G-166 | S6c/D-19: Reached evidence exported before crash action | PC-166 |
| G-167 | S6c/D-19: Only exact fixture server is killed | PC-167 |
| G-168 | S6c/D-19: Crash action intent precedes command | PC-168 |
| G-169 | S6c/D-19: Hard exit is awaited before restart | PC-169 |
| G-170 | S6d/D-20: Restart retains DB/runner/recipient identity | PC-170 |
| G-171 | S6d/D-20: Retype charge and tuple are preserved | PC-171 |
| G-172 | S6d/D-20: Enter-only recovery never retypes body | PC-172 |
| G-173 | S6d/D-20: Receipt recovery sends no further input | PC-173 |
| G-174 | S6d/D-22: Manifest resume does no POST | PC-174 |
| G-175 | S6d: Complete failure summary preserves failed run | PC-175 |
| G-176 | S6d: Not-reached cut is never a passing case | PC-176 |
| G-177 | S6c/D-21: Stock idle/busy cannot be replaced by fixture pass | PC-177 |
| G-178 | S6d: Export interruption cannot create result-ready | PC-178 |
| G-179 | S6d: No body submission uses Now to bypass queue | PC-179 |
| G-180 | S6a/D-17: Observation uses a coherent read-only snapshot | PC-180 |
| G-181 | S6b/D-19: Barrier records are atomic and integrity checked | PC-181 |
| G-182 | S6c/D-21: Receipt runner target excludes Docker tools | PC-182 |
| G-183 | S6a/D-20: Equal-body wrong transcript kind is refused | PC-183 |
| G-184 | S6a/D-20: Stored full body is independently checked | PC-184 |
| G-185 | S6d/D-20: Enter-only recovery retains original attempt tuple | PC-185 |
| G-186 | S6d/D-20: New typing attempt has its own committed floor | PC-186 |
| G-187 | S6c/D-19: Restart is controlled, not automatic container restart | PC-187 |
| G-188 | S2/D-9: Fresh stack disables Delegation__DiagnoseEnabled | PC-188 |
| G-189 | S2/D-9: Fresh stack disables Delegation__OutputDistillerEnabled | PC-189 |
| G-190 | S2/D-9: Fresh stack disables Hangfire__ServerEnabled | PC-190 |
| G-191 | S2/D-9: Fresh stack disables ZombieCensus__Enabled | PC-191 |
| G-192 | S2/D-9: Fresh stack disables WorktreeResidue__Enabled | PC-192 |
| G-193 | S2/D-9: Fresh stack disables Schedules__Enabled | PC-193 |
| G-194 | S2/D-9: Fresh stack disables ChannelBridge__Enabled | PC-194 |
| G-195 | S2/D-9: Fresh stack disables Digest__Enabled | PC-195 |
| G-196 | S6c/D-21: Receipt server is independently socket-free | PC-196 |
| G-197 | S6c/D-21: Receipt runner is independently socket-free | PC-197 |
| G-198 | S1/S3/context: Runtime context denies private PEM material | PC-198 |
| G-199 | S1/S3/context: Tests context denies private PEM material | PC-199 |
| G-200 | S6a/D-20: Summary remains below actual inline/spill ceiling | PC-200 |
| G-201 | S6d/D-22: Resume file belongs to owned evidence root | PC-201 |
| G-202 | S6b/D-19: Release binds complete case/row/attempt tuple | PC-202 |
| G-203 | S6c/D-17: Private credentials never enter argv or evidence | PC-203 |
| G-204 | S3: Old results cannot masquerade as this run | PC-204 |
| G-205 | S6a/D-17: Frozen queue immutable fields are rechecked | PC-205 |

### Positive controls

The following is the **pending post-land battery**, not a claim of reds executed in
TestDesign. Each row breaks exactly its G-n with the stated syntactically valid defect,
expects the named test method to fail at the stated assertion, then restores and expects
that same method green. `Class.Method` below expands to the precise method filter
`/*/*/Class/Method`; no class/suite filter is permitted in a PC cycle. Argument matrices
inside that single method all run, and the decisive faulty variant must fail. Do not edit
the test or its expected value to manufacture red. Missing test/build/fixture/zero-count
failure is not a red control.

Fixture target filenames without a directory mean
`tests/Antiphon.DockerStack.Fixture/<filename>`; `fixture Program.cs` means that project's
Program. Script controls invoke the actual entry/function using a recording command boundary
under an inherited local PowerShell child; no call falls through to real Docker/SSH/HTTP.
`Assert-LinuxTestRoster` is the S5 validator in `scripts/test-docker-container.ps1`, also
invoked locally by `LinuxTestRosterTests`. Local observation/interceptor tests construct
only values, files, recording connections/callbacks and response features. They never run
fixture `serve`, WAF Program, PostgreSQL or Testcontainers. Their separate real-I/O
qualification is V-1/V-8/V-9/V-10. New script tests carry `ParallelLimiter<ProcessSpawnLimit>`.

Before every PC launch clear `ANTIPHON_C467_QUEUE_WORKER`, `ANTIPHON_C574_STARTUP_WORKER`
and `ANTIPHON_C478_DELIVERY_WORKER`. Set `ANTIPHON_C476_PROBE` to this command's externally
owned evidence root and require lifecycle `state=never-requested`, `create=0`, no containerId.
Preserve the eager production-runner/Pty guards. Reject a DB-requested PC even if its
selected assertion passes. Mutation runs on the Windows managed SourceLanding snapshot at
exact L using only local inherited children, external evidence and source restoration;
never commit/push, create another worktree, deploy, or expose that snapshot to server2,
Docker, a broker, a standing executor or remote service. CARD-0598 owns future Linux custody.

Use one initial discovery/green qualification of all named PC methods, then sequential
method-scoped break/red/restore/fresh-build/green cycles. Builds are local inherited
`dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c590-pc/`
with `--disable-build-servers -p:UseSharedCompilation=false` and
`MSBUILDDISABLENODEREUSE=1`; runs use `dotnet run --project tests/Antiphon.Tests --no-build
--property:OutputPath=bin-c590-pc/ -- --treenode-filter $filter` with fresh TRX
paths, where `$filter` is the selected row's exact Class/Method expansion defined above.
Restore timestamps and prove fresh producer binaries. Keep full
per-PC intended assertion, counts, tested L, mutation/restoration hashes and native custody
records externally. No batched shared-file controls are assumed in the price below.

Code writes tests and runs ordinary V/R; separate Review judges implementation/evidence and
pending PCs before land. Mutation runs only after confirmed land and explicit SourceLanding
commissioning. Failures needing source/test repairs return through Code/Review/new land.

| PC | Compiling defect in guard target | Exact method | Expected red assertion |
|---|---|---|---|
| PC-1 | `Dockerfile`: Delete the server-build COPY of src. | `DockerStackContractTests.Server_project_graph_is_available` | missing referenced project list contains Antiphon.Agents.Pty.csproj. |
| PC-2 | `Dockerfile`: Delete final COPY of client/dist. | `DockerStackContractTests.Runtime_contains_built_client` | final-stage payload lacks wwwroot/index.html. |
| PC-3 | `.dockerignore`: Append server/Bundles/** exclusion. | `DockerStackContractTests.Runtime_contains_instruction_bundles` | required-context list contains the excluded bundle. |
| PC-4 | `Dockerfile`: Change sdk:10.0 to sdk:9.0. | `DockerStackContractTests.Sdk_satisfies_repository_pin` | SDK major compatibility is false. |
| PC-5 | `Dockerfile`: Replace publish RID linux-x64 with win-x64. | `DockerStackContractTests.Runtime_and_rid_match_linux_amd64` | publish RID equals linux-x64. |
| PC-6 | `docker/session-runner-grok/Dockerfile`: Delete PtyHost apphost COPY. | `DockerStackContractTests.Runner_contains_linux_host` | native manifest missing Antiphon.PtyHost. |
| PC-7 | `docker/session-runner-grok/Dockerfile`: Delete libporta_pty.so COPY. | `DockerStackContractTests.Runner_contains_porta_library` | native manifest missing libporta_pty.so. |
| PC-8 | `docker/session-runner-grok/Dockerfile`: Omit host .runtimeconfig.json from copied publish payload. | `DockerStackContractTests.Runner_contains_host_runtime_payload` | native manifest missing Antiphon.PtyHost.runtimeconfig.json. |
| PC-9 | `docker/session-runner-grok/Dockerfile`: Remove executable-bit check from the stage. | `DockerStackContractTests.Runner_requires_executable_host` | required executable validation absent. |
| PC-10 | `Dockerfile`: Replace SourceRevisionId build property with unknown. | `DockerStackContractTests.Server_publish_has_revision` | publish revision source equals SOURCE_REVISION. |
| PC-11 | `docker/session-runner-grok/Dockerfile`: Replace SourceRevisionId build property with unknown. | `DockerStackContractTests.Runner_publish_has_revision` | publish revision source equals SOURCE_REVISION. |
| PC-12 | `docker/tests/Dockerfile`: Replace SourceRevisionId build property with unknown. | `DockerStackContractTests.Fixture_publish_has_revision` | publish revision source equals SOURCE_REVISION. |
| PC-13 | `scripts/verify-docker-stack.ps1`: Remove observed-versus-expected version equality check. | `DockerStackSmokeCommandTests.Wrong_live_revision_is_refused` | wrong SHA exits nonzero and manifest.accepted is false. |
| PC-14 | `docker-compose.yml`: Set SessionRunner__BaseUrl to http://127.0.0.1:17204. | `DockerStackContractTests.Runner_route_is_internal` | runner host equals session-runner. |
| PC-15 | `docker-compose.yml`: Set DB Host to desktop-ktlkpif. | `DockerStackContractTests.Database_route_is_internal` | database host equals postgres. |
| PC-16 | `docker-compose.yml`: Enable its PhoneHome/PhoneHomeRunner setting. | `DockerStackContractTests.antiphon_phone_home_is_disabled` | effective phone-home enabled is false. |
| PC-17 | `docker-compose.yml`: Enable its PhoneHome/PhoneHomeRunner setting. | `DockerStackContractTests.session_runner_phone_home_is_disabled` | effective phone-home enabled is false. |
| PC-18 | `docker-compose.yml`: Add ports: [18080:8080] to selected service. | `DockerStackContractTests.session_runner_port_is_private` | selected service published ports is empty. |
| PC-19 | `docker-compose.yml`: Add ports: [18080:8080] to selected service. | `DockerStackContractTests.postgres_port_is_private` | selected service published ports is empty. |
| PC-20 | `docker-compose.yml`: Change default bind address to 0.0.0.0. | `DockerStackContractTests.Default_server_bind_is_loopback` | default bind equals 127.0.0.1. |
| PC-21 | `docker-compose.yml`: Change its dependency condition to service_started. | `DockerStackContractTests.postgres_must_be_healthy` | dependency condition equals service_healthy. |
| PC-22 | `docker-compose.yml`: Change its dependency condition to service_started. | `DockerStackContractTests.session_runner_must_be_healthy` | dependency condition equals service_healthy. |
| PC-23 | `docker-compose.yml`: Replace healthcheck command with a missing-probe executable. | `DockerStackContractTests.antiphon_health_probe_exists` | health executable belongs to stage-installed tool manifest. |
| PC-24 | `docker-compose.yml`: Replace healthcheck command with a missing-probe executable. | `DockerStackContractTests.session_runner_health_probe_exists` | health executable belongs to stage-installed tool manifest. |
| PC-25 | `docker-compose.yml`: Add /var/run/docker.sock mount to selected service. | `DockerStackContractTests.antiphon_base_has_no_socket` | base service Docker mounts is empty. |
| PC-26 | `docker-compose.yml`: Add /var/run/docker.sock mount to selected service. | `DockerStackContractTests.session_runner_base_has_no_socket` | base service Docker mounts is empty. |
| PC-27 | `docker-compose.session-testing.yml`: Delete runner socket mount. | `DockerStackContractTests.Testing_runner_has_explicit_socket` | effective test runner has exactly one socket mount. |
| PC-28 | `scripts/test-docker.ps1`: Bypass socket-GID comparison. | `DockerTestCommandTests.Socket_gid_mismatch_refuses` | zero Docker run commands and SocketGroupMismatch. |
| PC-29 | `docker-compose.test.yml`: Add privileged: true. | `DockerStackContractTests.Testing_services_are_unprivileged` | effective test service privileged is false. |
| PC-30 | `docker-compose.yml`: Set runner user to 0:0. | `DockerStackContractTests.Applications_share_nonroot_identity` | UID is nonzero and equals server UID. |
| PC-31 | `scripts/verify-docker-stack.ps1`: Replace foreign-owner refusal with acceptance. | `DockerStackSmokeCommandTests.Foreign_state_owner_refuses` | zero chown commands and zero startup commands. |
| PC-32 | `docker-compose.yml`: Replace server workspace with C:\work. | `DockerStackContractTests.State_paths_are_linux_absolute` | invalid path list names Git__WorkspacePath. |
| PC-33 | `docker-compose.yml`: Mount runner workspace at /other. | `DockerStackContractTests.Workspace_mount_paths_match` | both app workspace destinations equal /work. |
| PC-34 | `docker-compose.yml`: Remove pgdata volume mapping. | `DockerStackContractTests.Reusable_state_has_named_volumes` | required state mount list names pgdata. |
| PC-35 | `docker-compose.yml`: Set SessionRunner__PtyBackend to modern. | `DockerStackContractTests.Linux_backend_is_inbox` | both effective backend selectors equal inbox. |
| PC-36 | `docker-compose.yml`: Set SessionRunner__Herdr__Enabled=true. | `DockerStackContractTests.Linux_herdr_is_disabled` | effective Herdr enabled is false. |
| PC-37 | `docker-compose.yml`: Set Delegation__CheckInterpreterEnabled=true. | `DockerStackContractTests.Fresh_stack_is_inactive` | enabled unattended settings is empty. |
| PC-38 | `scripts/verify-docker-stack.ps1`: Treat key-directory existence as managed-secret readiness. | `DockerStackSmokeCommandTests.Auto_keyring_without_protector_is_unavailable` | reported managedSecretReady is false. |
| PC-39 | `docker-compose.yml`: Move keyring path to /app/keys. | `DockerStackContractTests.Keyring_is_private_external_state` | keyring path belongs to owned external state. |
| PC-40 | `.dockerignore`: Append a final !.git re-include rule. | `DockerStackContractTests.Runtime_context_denies_GitPointer` | effective-context sentinel .git is absent. |
| PC-41 | `.dockerignore`: Append a final !.antiphon/case.json re-include rule. | `DockerStackContractTests.Runtime_context_denies_AgentState` | effective-context sentinel .antiphon/case.json is absent. |
| PC-42 | `.dockerignore`: Append a final !client/.env.local re-include rule. | `DockerStackContractTests.Runtime_context_denies_Environment` | effective-context sentinel client/.env.local is absent. |
| PC-43 | `.dockerignore`: Append a final !scratch/auth.json re-include rule. | `DockerStackContractTests.Runtime_context_denies_Auth` | effective-context sentinel scratch/auth.json is absent. |
| PC-44 | `.dockerignore`: Append a final !.grok/config.toml re-include rule. | `DockerStackContractTests.Runtime_context_denies_ProviderHome` | effective-context sentinel .grok/config.toml is absent. |
| PC-45 | `.dockerignore`: Append a final !scratch/key.pfx re-include rule. | `DockerStackContractTests.Runtime_context_denies_Certificate` | effective-context sentinel scratch/key.pfx is absent. |
| PC-46 | `.dockerignore`: Append a final !server/bin/x.dll re-include rule. | `DockerStackContractTests.Runtime_context_denies_Bin` | effective-context sentinel server/bin/x.dll is absent. |
| PC-47 | `.dockerignore`: Append a final !server/bin-pc/x.dll re-include rule. | `DockerStackContractTests.Runtime_context_denies_AlternateBin` | effective-context sentinel server/bin-pc/x.dll is absent. |
| PC-48 | `.dockerignore`: Append a final !server/obj/x re-include rule. | `DockerStackContractTests.Runtime_context_denies_Obj` | effective-context sentinel server/obj/x is absent. |
| PC-49 | `.dockerignore`: Append a final !client/node_modules/x re-include rule. | `DockerStackContractTests.Runtime_context_denies_NodeModules` | effective-context sentinel client/node_modules/x is absent. |
| PC-50 | `.dockerignore`: Append a final !workspace/owned.txt re-include rule. | `DockerStackContractTests.Runtime_context_denies_Workspace` | effective-context sentinel workspace/owned.txt is absent. |
| PC-51 | `.dockerignore`: Append a final !logs/test.log re-include rule. | `DockerStackContractTests.Runtime_context_denies_Logs` | effective-context sentinel logs/test.log is absent. |
| PC-52 | `docker/tests/Dockerfile.dockerignore`: Append a final !.git re-include rule. | `DockerStackContractTests.Tests_context_denies_GitPointer` | effective-context sentinel .git is absent. |
| PC-53 | `docker/tests/Dockerfile.dockerignore`: Append a final !.antiphon/case.json re-include rule. | `DockerStackContractTests.Tests_context_denies_AgentState` | effective-context sentinel .antiphon/case.json is absent. |
| PC-54 | `docker/tests/Dockerfile.dockerignore`: Append a final !client/.env.local re-include rule. | `DockerStackContractTests.Tests_context_denies_Environment` | effective-context sentinel client/.env.local is absent. |
| PC-55 | `docker/tests/Dockerfile.dockerignore`: Append a final !scratch/auth.json re-include rule. | `DockerStackContractTests.Tests_context_denies_Auth` | effective-context sentinel scratch/auth.json is absent. |
| PC-56 | `docker/tests/Dockerfile.dockerignore`: Append a final !.grok/config.toml re-include rule. | `DockerStackContractTests.Tests_context_denies_ProviderHome` | effective-context sentinel .grok/config.toml is absent. |
| PC-57 | `docker/tests/Dockerfile.dockerignore`: Append a final !scratch/key.pfx re-include rule. | `DockerStackContractTests.Tests_context_denies_Certificate` | effective-context sentinel scratch/key.pfx is absent. |
| PC-58 | `docker/tests/Dockerfile.dockerignore`: Append a final !server/bin/x.dll re-include rule. | `DockerStackContractTests.Tests_context_denies_Bin` | effective-context sentinel server/bin/x.dll is absent. |
| PC-59 | `docker/tests/Dockerfile.dockerignore`: Append a final !server/bin-pc/x.dll re-include rule. | `DockerStackContractTests.Tests_context_denies_AlternateBin` | effective-context sentinel server/bin-pc/x.dll is absent. |
| PC-60 | `docker/tests/Dockerfile.dockerignore`: Append a final !server/obj/x re-include rule. | `DockerStackContractTests.Tests_context_denies_Obj` | effective-context sentinel server/obj/x is absent. |
| PC-61 | `docker/tests/Dockerfile.dockerignore`: Append a final !client/node_modules/x re-include rule. | `DockerStackContractTests.Tests_context_denies_NodeModules` | effective-context sentinel client/node_modules/x is absent. |
| PC-62 | `docker/tests/Dockerfile.dockerignore`: Append a final !workspace/owned.txt re-include rule. | `DockerStackContractTests.Tests_context_denies_Workspace` | effective-context sentinel workspace/owned.txt is absent. |
| PC-63 | `docker/tests/Dockerfile.dockerignore`: Append a final !logs/test.log re-include rule. | `DockerStackContractTests.Tests_context_denies_Logs` | effective-context sentinel logs/test.log is absent. |
| PC-64 | `docker/tests/Dockerfile.dockerignore`: Append tests/Shared/** exclusion. | `DockerStackContractTests.Test_context_retains_linked_sources` | required-context list includes TestClassificationMetadata.cs. |
| PC-65 | `docker/tests/Dockerfile`: Remove runtime-9 installation/copy. | `DockerStackContractTests.Test_target_has_supported_tools` | SDK10/runtime9/Node22/pwsh/Git tool contract lacks runtime9. |
| PC-66 | `scripts/test-docker.ps1`: Continue after missing-socket preflight. | `DockerTestCommandTests.Missing_socket_fails_before_execution` | nonzero exit and zero test-container starts. |
| PC-67 | `scripts/test-docker.ps1`: Ignore failed disposable database connection. | `DockerTestCommandTests.Unreachable_mapped_database_fails` | nonzero exit and zero test commands. |
| PC-68 | `scripts/test-docker-container.ps1`: Unset ANTIPHON_BROKER_TESTS in messaging launch. | `DockerTestCommandTests.Broker_lane_is_enabled` | recorded child environment has ANTIPHON_BROKER_TESTS=1. |
| PC-69 | `scripts/test-docker-container.ps1`: Pass through ANTIPHON_TG_TEST_TOKEN from harmless sentinel environment. | `DockerTestCommandTests.Live_credentials_are_removed` | captured child environment omits live token. |
| PC-70 | `scripts/test-docker.ps1`: Set captured child exit code to zero. | `DockerTestCommandTests.Nonzero_test_exit_is_preserved` | exit remains injected 23 even with plausible success report. |
| PC-71 | `scripts/test-docker.ps1`: Bypass expected-report existence check. | `DockerTestCommandTests.Missing_report_fails` | nonzero exit and no accepted result-ready manifest. |
| PC-72 | `scripts/test-docker.ps1`: Allow executed=0. | `DockerTestCommandTests.Zero_execution_fails` | nonzero exit and ZeroExecuted diagnosis. |
| PC-73 | `scripts/test-docker.ps1`: Skip executed-class-set comparison. | `DockerTestCommandTests.Missing_class_fails` | nonzero exit naming missing selected class. |
| PC-74 | `scripts/test-docker.ps1`: Ignore skipped counter. | `DockerTestCommandTests.Unexpected_skip_fails` | nonzero exit and UnexpectedSkip diagnosis. |
| PC-75 | `scripts/test-docker.ps1`: Trust exit zero despite report failed=1. | `DockerTestCommandTests.Exit_result_disagreement_fails` | nonzero exit and ResultExitMismatch diagnosis. |
| PC-76 | `scripts/test-docker.ps1`: Emit container removal before docker cp. | `DockerTestCommandTests.Export_precedes_container_removal` | first remove trace index is greater than export/hash/commit indices. |
| PC-77 | `scripts/test-docker.ps1`: Bypass copied-artifact digest equality. | `DockerTestCommandTests.Wrong_artifact_digest_fails` | nonzero exit and no result-ready acceptance. |
| PC-78 | `scripts/test-docker-container.ps1`: Pass application runner URL to test host. | `DockerTestCommandTests.Test_environment_refuses_application_runner` | captured SessionRunner__BaseUrl equals http://127.0.0.1:1. |
| PC-79 | `tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs`: Delete replacement registration of ISessionRunnerClient. | `DockerStackContractTests.Http_test_factory_keeps_refusing_client` | source contract includes RemoveAll and RefusingSessionRunnerClient registration. |
| PC-80 | `scripts/test-docker.ps1`: Remove named-group allowlist check. | `DockerTestCommandTests.Unknown_group_is_refused` | unknown group exits before command invocation. |
| PC-81 | `tests/Shared/TestAppHostPath.cs`: Return unsuffixed name for Windows. | `TestAppHostPathTests.Windows_path_has_exe_suffix` | resolved fakegrok path ends in fakegrok.exe. |
| PC-82 | `tests/Shared/TestAppHostPath.cs`: Always append .exe. | `TestAppHostPathTests.Linux_path_has_no_exe_suffix` | resolved fakegrok path ends in fakegrok without .exe. |
| PC-83 | `tests/Shared/TestAppHostPath.cs`: Resolve directly from baseDirectory instead of fakegrok subdirectory. | `TestAppHostPathTests.Sibling_producer_directory_is_used` | resolved path equals staged sibling path. |
| PC-84 | `tests/Shared/TestAppHostPath.cs`: Return missing candidate without checking existence. | `TestAppHostPathTests.Missing_apphost_names_attempted_file` | FileNotFoundException.FileName equals exact attempted path. |
| PC-85 | `tests/Antiphon.E2E/Fixtures/IsolatedSessionRunner.cs`: Restore literal Antiphon.SessionRunner.exe lookup. | `TestAppHostPathTests.E2e_runner_uses_shared_resolution` | source consumer selects TestAppHostPath for runner and diagnostic. |
| PC-86 | `scripts/test-docker-container.ps1 / Assert-LinuxTestRoster`: Ignore discovered classes absent from roster. | `LinuxTestRosterTests.Unknown_class_is_refused` | validation errors name unknown class. |
| PC-87 | `scripts/test-docker-container.ps1 / Assert-LinuxTestRoster`: Replace duplicate error with first-row selection. | `LinuxTestRosterTests.Duplicate_class_is_refused` | validation errors name duplicate class. |
| PC-88 | `scripts/test-docker-container.ps1 / Assert-LinuxTestRoster`: Ignore roster entries absent from discovery. | `LinuxTestRosterTests.Stale_class_is_refused` | validation errors name stale class. |
| PC-89 | `scripts/test-docker-container.ps1 / Assert-LinuxTestRoster`: Bypass native-boundary check. | `LinuxTestRosterTests.Native_class_is_refused` | known native class has IncludedNative error. |
| PC-90 | `scripts/test-docker-container.ps1 / Assert-LinuxTestRoster`: Do not propagate helper spawning evidence. | `LinuxTestRosterTests.Indirect_spawner_is_refused` | WorkspaceHookRunnerTests has IncludedSpawner error. |
| PC-91 | `scripts/test-docker-container.ps1 / Assert-LinuxTestRoster`: Allow empty exclusion reason/owner. | `LinuxTestRosterTests.Unowned_exclusion_is_refused` | validation errors include MissingExclusionOwner. |
| PC-92 | `scripts/test-docker.ps1`: Bypass sourceLandingOperationId check. | `DockerTestCommandTests.Sourced_task_is_refused` | zero Docker calls for sourced task. |
| PC-93 | `scripts/test-docker.ps1`: Treat task lookup failure as unbound ordinary. | `DockerTestCommandTests.Unreadable_task_binding_is_refused` | zero Docker calls and TaskBindingUnavailable. |
| PC-94 | `scripts/test-docker.ps1`: Bypass canonical verification-root exclusion. | `DockerTestCommandTests.Verification_path_is_refused` | zero Docker calls for managed snapshot path. |
| PC-95 | `scripts/test-docker.ps1`: Use string prefix without canonical root-boundary check. | `DockerTestCommandTests.Outside_checkout_root_is_refused` | zero Docker calls for sibling-prefix path and symlink escape. |
| PC-96 | `scripts/verify-docker-stack.ps1`: Remove parent/child inequality check. | `DockerStackSmokeCommandTests.Parent_as_child_is_refused` | zero create/delete commands for parent-as-child. |
| PC-97 | `scripts/verify-docker-stack.ps1`: Create child before atomic initial manifest write. | `DockerStackSmokeCommandTests.Manifest_precedes_create` | first create occurs after durable manifest write. |
| PC-98 | `scripts/verify-docker-stack.ps1`: Accept export SHA different from selected SHA. | `DockerStackSmokeCommandTests.Wrong_context_source_is_refused` | zero image build commands and SourceMismatch. |
| PC-99 | `scripts/verify-docker-stack.ps1`: Emit /work/test-evidence runner path as sibling bind source. | `DockerStackSmokeCommandTests.Runner_local_bind_source_is_refused` | command plan has no runner-local host bind. |
| PC-100 | `scripts/verify-docker-stack.ps1`: Ignore inspected container ID mismatch. | `DockerStackSmokeCommandTests.Foreign_resource_id_is_refused` | zero destructive commands. |
| PC-101 | `scripts/verify-docker-stack.ps1`: Ignore inspected run label mismatch. | `DockerStackSmokeCommandTests.Foreign_resource_label_is_refused` | zero destructive commands. |
| PC-102 | `scripts/verify-docker-stack.ps1`: Add --volumes to ordinary deployment down. | `DockerStackSmokeCommandTests.Normal_down_retains_volumes` | normal-down argv excludes --volumes/-v. |
| PC-103 | `scripts/verify-docker-stack.ps1`: Mark cleanup complete despite inspected surviving owned container. | `DockerDeliveryRecoveryCommandTests.Cleanup_failure_preserves_residue` | nonzero/incomplete with surviving ID in manifest. |
| PC-104 | `scripts/verify-docker-stack.ps1`: Resume removal without fresh inspect. | `DockerDeliveryRecoveryCommandTests.Interrupted_cleanup_rechecks_identity` | zero deletes after replacement-ID observation. |
| PC-105 | `scripts/verify-docker-stack.ps1`: Add docker system prune to cleanup plan. | `DockerStackSmokeCommandTests.Global_cleanup_is_forbidden` | trace contains no global cleanup command. |
| PC-106 | `scripts/verify-docker-stack.ps1`: Accept health with missing marker response. | `DockerStackSmokeCommandTests.Raw_challenge_is_required` | acceptance false with RawChallengeMissing. |
| PC-107 | `scripts/verify-docker-stack.ps1`: Accept matching marker with no terminal/exit evidence. | `DockerStackSmokeCommandTests.Raw_exit_is_required` | acceptance false with RawExitMissing. |
| PC-108 | `scripts/verify-docker-stack.ps1`: Accept host-only command result without session/generation join. | `DockerStackSmokeCommandTests.Session_created_run_is_required` | acceptance false with SessionOriginMissing. |
| PC-109 | `scripts/verify-docker-stack.ps1`: Invoke parent qualification from child-probe mode. | `DockerStackSmokeCommandTests.Child_probe_cannot_launch_parent` | zero parent-launch commands from child invocation. |
| PC-110 | `docker/session-runner-grok/Dockerfile`: Make final/default target inherit receipt-probe. | `DockerStackContractTests.Default_runtime_excludes_test_payload` | default target dependency closure has no fakegrok/test tools. |
| PC-111 | `Dockerfile`: COPY fixture publish payload into final server stage. | `DockerStackContractTests.Stock_server_excludes_fixture` | final payload has no DockerStack.Fixture/Mvc.Testing. |
| PC-112 | `docker-compose.delivery-fixture.yml`: Mount socket into observer. | `DockerStackContractTests.Receipt_stack_is_socket_free` | all receipt server/runner/observer Docker mounts empty. |
| PC-113 | `scripts/verify-docker-stack.ps1`: Accept VerificationCustodyV1 in Linux capabilities. | `DockerStackSmokeCommandTests.Linux_custody_advertisement_is_refused` | acceptance false with UnsupportedCustodyAdvertised. |
| PC-114 | `DeliveryCaseIdentity.cs`: Skip project/resource owner equality. | `DockerDeliveryObservationTests.Foreign_observer_owner_is_refused` | Observe refuses with OwnerMismatch before any query. |
| PC-115 | `fixture Program.cs observe parsing`: Accept a --connection-string argument. | `DockerDeliveryObservationTests.Observer_arguments_are_closed` | argument rejected before reader construction. |
| PC-116 | `provision-observer.sql`: Add INSERT grant on SessionQueuedMessages. | `DockerDeliveryObservationTests.Observer_grants_are_select_only` | grant operation set equals SELECT on only three named tables. |
| PC-117 | `QueueObservationReader.cs`: Remove AgentSessionId predicate. | `DockerDeliveryObservationTests.Query_is_bound_to_session` | recorded query requires recipient parameter; foreign-session candidate rejected. |
| PC-118 | `QueueObservationReader.cs`: Remove Sequence > high-water predicate. | `DockerDeliveryObservationTests.Query_excludes_preexisting_rows` | old equal-body row cannot be selected. |
| PC-119 | `QueueObservationReader.cs`: Use LIKE/Contains body matching. | `DockerDeliveryObservationTests.Query_requires_exact_body` | prefix/suffix candidate rejected; exact body selected. |
| PC-120 | `QueueObservationReader.cs`: Add Status=Pending predicate. | `DockerDeliveryObservationTests.Query_is_status_independent` | recorded query contains no status restriction; Sent row remains observable. |
| PC-121 | `DeliveryEvidenceValidator.cs`: Remove ordinary-origin/binding check. | `DockerDeliveryObservationTests.Non_ui_or_bound_row_is_refused` | system/task/notification/schedule/maintenance variants refused. |
| PC-122 | `DeliveryEvidenceValidator.cs`: Take newest of duplicate candidates. | `DockerDeliveryObservationTests.Duplicate_queue_rows_are_refused` | AmbiguousQueueRows; no selected row. |
| PC-123 | `DeliveryEvidenceValidator.cs`: Accept candidate with a different Id after freeze. | `DockerDeliveryObservationTests.Observed_row_cannot_be_replaced` | RowIdentityChanged; original Id retained. |
| PC-124 | `DeliveryEvidenceValidator.cs`: Synthesize floor=0 and generation from session. | `DockerDeliveryObservationTests.Pending_without_attempt_keeps_nulls` | attempt file absent; exported floor/generation both null. |
| PC-125 | `DeliveryEvidenceValidator.cs`: Accept null floor for attempts=1. | `DockerDeliveryObservationTests.Attempt_requires_committed_floor` | MissingAttemptFloor rejection. |
| PC-126 | `DeliveryEvidenceValidator.cs`: Replace attempt-1 snapshot with attempt-2. | `DockerDeliveryObservationTests.Retype_appends_attempt_instead_of_overwrite` | both distinct tuples retained with original floor unchanged. |
| PC-127 | `DeliveryEvidenceValidator.cs`: Use StartsWith instead of full normalized equality. | `DockerDeliveryObservationTests.Native_prompt_must_be_complete` | prefix/missing-tail records rejected. |
| PC-128 | `DeliveryEvidenceValidator.cs`: Skip UTF8 body SHA256 verification. | `DockerDeliveryObservationTests.Expected_body_digest_must_match` | BodyDigestMismatch before receipt acceptance. |
| PC-129 | `DeliveryEvidenceValidator.cs`: Skip parsed summary identity equality. | `DockerDeliveryObservationTests.Summary_identity_must_match` | each wrong marker variant rejected despite recomputed valid body hash. |
| PC-130 | `DeliveryEvidenceValidator.cs`: Accept equal text with different UUID. | `DockerDeliveryObservationTests.Stored_receipt_must_join_native_uuid` | ReceiptJoinMismatch. |
| PC-131 | `DeliveryEvidenceValidator.cs`: Change > floor to >= floor. | `DockerDeliveryObservationTests.Stored_receipt_must_exceed_attempt_floor` | equal-floor old prompt rejected. |
| PC-132 | `DeliveryEvidenceValidator.cs`: Compare native.Sequence to floor instead of stored.Sequence. | `DockerDeliveryObservationTests.Native_sequence_cannot_satisfy_server_floor` | native=100, stored=4, floor=5 is rejected. |
| PC-133 | `DeliveryEvidenceValidator.cs`: Take first native matching record. | `DockerDeliveryObservationTests.Duplicate_native_prompts_are_refused` | two same-body native UUIDs rejected. |
| PC-134 | `DeliveryEvidenceValidator.cs`: Take first stored UUID/kind record. | `DockerDeliveryObservationTests.Duplicate_stored_receipts_are_refused` | duplicate stored receipt rejected. |
| PC-135 | `DeliveryEvidenceValidator.cs`: Accept native receipt with empty persisted list. | `DockerDeliveryObservationTests.Native_only_is_not_delivery_success` | state equals native-received/server-pending; accepted=false. |
| PC-136 | `DeliveryEvidenceValidator.cs`: Skip initial accepted-generation comparison. | `DockerDeliveryObservationTests.Initial_generation_mismatch_is_refused` | GenerationMismatch for old manifest. |
| PC-137 | `DeliveryEvidenceValidator.cs`: Skip final accepted-generation comparison. | `DockerDeliveryObservationTests.Generation_change_during_read_is_refused` | GenerationChanged even with matching body/UUID. |
| PC-138 | `DeliveryEvidenceValidator.cs`: Fill missing runner generation from database. | `DockerDeliveryObservationTests.Missing_generation_is_refused` | GenerationUnavailable. |
| PC-139 | `DeliveryEvidenceValidator.cs`: Compare rounded milliseconds. | `DockerDeliveryObservationTests.Generation_uses_postgres_precision` | one-microsecond difference rejected; submicrosecond representation normalized. |
| PC-140 | `OrdinaryDeliveryRunnerClient.cs`: Return without forwarding SendInput for foreign session. | `DockerDeliveryBarrierTests.Untargeted_runner_calls_are_forwarded` | inner trace includes identical foreign input once. |
| PC-141 | `OrdinaryDeliveryRunnerClient.cs`: Yield selected SSE prompt while armed. | `DockerDeliveryBarrierTests.Target_sse_receipt_waits_for_release` | no selected event escapes before exact release. |
| PC-142 | `OrdinaryDeliveryRunnerClient.cs`: Return selected transcript while armed. | `DockerDeliveryBarrierTests.Target_pull_receipt_waits_for_release` | pull remains incomplete until release; foreign transcript passes. |
| PC-143 | `OrdinaryDeliveryRunnerClient.cs`: Forward CR before checking armed cut. | `DockerDeliveryBarrierTests.Body_is_forwarded_before_enter_cut` | before-release inner trace contains body only, no CR. |
| PC-144 | `OrdinaryDeliveryRunnerClient.cs`: Strip bracketed paste markers. | `DockerDeliveryBarrierTests.Multiline_bytes_are_unchanged` | inner payload equals LF bracketed body and separate CR. |
| PC-145 | `OrdinaryDeliverySaveInterceptor.cs`: Read Added entries only in SavedChangesAsync. | `DockerDeliveryBarrierTests.Save_metadata_is_captured_before_accept` | one insert snapshot emitted after simulated EF AcceptAllChanges. |
| PC-146 | `OrdinaryDeliverySaveInterceptor.cs`: Emit reached record from SaveChangesFailedAsync. | `DockerDeliveryBarrierTests.Failed_save_never_emits_commit_ready` | reached records is empty after injected DbUpdateException. |
| PC-147 | `OrdinaryDeliverySaveInterceptor.cs`: Trust tracked context instead of second-read result. | `DockerDeliveryBarrierTests.Invisible_commit_cannot_reach_cut` | no reached record for independent read returning absent. |
| PC-148 | `OrdinaryDeliverySaveInterceptor.cs`: Ignore CurrentTransaction in commit qualification. | `DockerDeliveryBarrierTests.Open_transaction_cannot_reach_cut` | OpenTransaction rejection and no commit-ready file. |
| PC-149 | `OrdinaryDeliverySaveInterceptor.cs`: Reuse last selected snapshot for an unrelated context save. | `DockerDeliveryBarrierTests.Save_metadata_cannot_cross_contexts` | foreign context emits no selected reached record. |
| PC-150 | `OrdinaryDeliverySaveInterceptor.cs`: Exclude multi-entry batch from selected-UUID fault. | `DockerDeliveryBarrierTests.Transcript_batch_is_refused` | batch SavingChanges throws named DbUpdateException. |
| PC-151 | `OrdinaryDeliverySaveInterceptor.cs`: Disarm after first batch failure. | `DockerDeliveryBarrierTests.Transcript_individual_retry_is_refused` | second individual SavingChanges throws same named fault. |
| PC-152 | `OrdinaryDeliverySaveInterceptor.cs`: Require UserPrompt kind as well as UUID for fault selection. | `DockerDeliveryBarrierTests.Transcript_stub_retry_is_refused` | stub-kind save for selected UUID throws; unrelated UUID allowed. |
| PC-153 | `OrdinaryDeliverySaveInterceptor.cs`: Hold Delivered only after successful save. | `DockerDeliveryBarrierTests.Verdict_is_held_before_commit` | save delegate call count zero while verdict barrier held. |
| PC-154 | `DeliveryFileBarrier.cs`: Compare only cut name in arm matching. | `DockerDeliveryBarrierTests.Foreign_arm_is_not_activated` | cross-case/row/attempt arms produce no reached file. |
| PC-155 | `DeliveryFileBarrier.cs`: Ignore host boot nonce in release matching. | `DockerDeliveryBarrierTests.Old_host_release_cannot_unblock` | blocked completion remains pending after prior-host release. |
| PC-156 | `DeliveryFileBarrier.cs`: Do not persist consumed cut ID. | `DockerDeliveryBarrierTests.Consumed_arm_cannot_rearm_after_restart` | recreated barrier refuses same arm after restart. |
| PC-157 | `DeliveryFileBarrier.cs`: Return success when wait cancellation fires. | `DockerDeliveryBarrierTests.Deadline_is_incomplete_not_release` | Incomplete result and zero downstream operations. |
| PC-158 | `OrdinaryDeliveryResponseBarrier.cs`: Forward StartAsync while armed. | `DockerDeliveryBarrierTests.Held_response_cannot_start_headers` | underlying response HasStarted is false until release. |
| PC-159 | `OrdinaryDeliveryResponseBarrier.cs`: Forward FlushAsync while armed. | `DockerDeliveryBarrierTests.Held_response_cannot_flush_body` | underlying body length/flush count remains zero. |
| PC-160 | `OrdinaryDeliveryResponseBarrier.cs`: Drop response body on unarmed path. | `DockerDeliveryBarrierTests.Unarmed_response_is_unchanged` | exact original status/header/body received. |
| PC-161 | `DeliveryFixtureHost.cs`: Keep the captured inner registration but omit the outer decorator registration. | `DockerDeliveryBarrierTests.Runner_registration_is_decorated_once` | resolved graph has exactly one decorator with original recording inner. |
| PC-162 | `scripts/verify-docker-stack.ps1`: Post before expectation atomic replacement. | `DockerDeliveryRecoveryCommandTests.Expectation_is_durable_before_post` | POST index follows committed expectation with exact body hash. |
| PC-163 | `scripts/verify-docker-stack.ps1`: Retry POST when response is lost. | `DockerDeliveryRecoveryCommandTests.Unknown_ack_never_reposts` | post count remains one across observation/resume. |
| PC-164 | `scripts/verify-docker-stack.ps1`: Treat zero matches as definite refusal. | `DockerDeliveryRecoveryCommandTests.Live_request_absence_is_not_refusal` | incomplete state and no second POST. |
| PC-165 | `scripts/verify-docker-stack.ps1`: Retry definite failure without checking native/input evidence. | `DockerDeliveryRecoveryCommandTests.Refused_insert_retry_requires_absence` | no second POST if any body/Enter/native prompt exists. |
| PC-166 | `scripts/verify-docker-stack.ps1`: Issue kill before reached digest export. | `DockerDeliveryRecoveryCommandTests.Crash_requires_exported_reached_record` | kill index follows independent observation and reached export. |
| PC-167 | `scripts/verify-docker-stack.ps1`: Use runner container ID for kill. | `DockerDeliveryRecoveryCommandTests.Crash_target_is_only_fixture_server` | kill target equals recorded fixture-server ID; runner/db untouched. |
| PC-168 | `scripts/verify-docker-stack.ps1`: Write pending action after kill command. | `DockerDeliveryRecoveryCommandTests.Crash_action_intent_is_durable` | intent commit precedes kill; interrupted action remains reconcilable. |
| PC-169 | `scripts/verify-docker-stack.ps1`: Start fixture before observed exit. | `DockerDeliveryRecoveryCommandTests.Server_exit_is_awaited` | restart index follows exact container exited observation. |
| PC-170 | `scripts/verify-docker-stack.ps1`: Ignore runner container identity drift after restart. | `DockerDeliveryRecoveryCommandTests.Restart_identity_change_is_refused` | acceptance false; no adoption or replacement recipient launch. |
| PC-171 | `scripts/verify-docker-stack.ps1`: Accept attempts=1 after fresh body retry. | `DockerDeliveryRecoveryCommandTests.Retype_recovery_charges_once` | attempt-committed case requires attempts=2 and both floor files. |
| PC-172 | `scripts/verify-docker-stack.ps1`: Ignore extra forwarded body in body-before-enter case. | `DockerDeliveryRecoveryCommandTests.Enter_only_recovery_forbids_second_body` | duplicate body rejects recovery even if final transcript matches. |
| PC-173 | `scripts/verify-docker-stack.ps1`: Allow one extra CR after preexisting complete receipt. | `DockerDeliveryRecoveryCommandTests.Received_recovery_requires_zero_writes` | additional forwarded body and Enter counts both zero. |
| PC-174 | `scripts/verify-docker-stack.ps1`: Submit summary again in ResumeManifest. | `DockerDeliveryRecoveryCommandTests.Receipt_manifest_resume_is_read_only` | resume POST count equals zero; same manifest identity. |
| PC-175 | `scripts/verify-docker-stack.ps1`: Set run success from receipt acceptance alone. | `DockerDeliveryRecoveryCommandTests.Received_failure_summary_remains_failure` | delivery=confirmed but runExit is original nonzero. |
| PC-176 | `scripts/verify-docker-stack.ps1`: Accept case on health despite no reached record. | `DockerDeliveryRecoveryCommandTests.Missing_reached_cut_fails` | case accepted=false and CutNotReached. |
| PC-177 | `scripts/verify-docker-stack.ps1`: Omit stock-busy requirement from final manifest validation. | `DockerDeliveryRecoveryCommandTests.Stock_receipts_are_independent_gates` | missing stock-busy rejects overall acceptance. |
| PC-178 | `scripts/verify-docker-stack.ps1`: Commit result-ready after only first report copy. | `DockerDeliveryRecoveryCommandTests.Interrupted_export_is_incomplete` | no result-ready acknowledgement and evidence residue retained. |
| PC-179 | `scripts/verify-docker-stack.ps1`: POST mode Now. | `DockerDeliveryRecoveryCommandTests.Receipt_probe_requires_when_idle` | captured request mode equals WhenIdle. |
| PC-180 | `QueueObservationReader.cs`: Request ReadCommitted instead of RepeatableRead/read-only transaction. | `DockerDeliveryObservationTests.Observation_transaction_is_read_only_repeatable` | recorded transaction isolation equals RepeatableRead and readOnly=true. |
| PC-181 | `DeliveryFileBarrier.cs`: Accept reached record with invalid observation digest. | `DockerDeliveryBarrierTests.Torn_reached_record_is_refused` | no release/kill authorization for truncated or digest-mismatched record. |
| PC-182 | `docker/session-runner-grok/Dockerfile`: Derive receipt-probe from session-testing tool layer. | `DockerStackContractTests.Receipt_runner_has_no_docker_tools` | receipt-probe tool closure contains no docker/compose socket authority. |
| PC-183 | `DeliveryEvidenceValidator.cs`: Accept AssistantText as native candidate. | `DockerDeliveryObservationTests.Receipt_kind_must_be_user_prompt` | wrong-kind record rejected despite exact body. |
| PC-184 | `DeliveryEvidenceValidator.cs`: Skip stored body equality after UUID join. | `DockerDeliveryObservationTests.Stored_body_must_be_complete` | matching UUID with truncated stored text rejected. |
| PC-185 | `scripts/verify-docker-stack.ps1`: Ignore replacement floor/generation on Enter-only outcome. | `DockerDeliveryRecoveryCommandTests.Enter_only_recovery_keeps_tuple` | body-before-enter requires original attempt=1/floor/start/generation. |
| PC-186 | `scripts/verify-docker-stack.ps1`: Accept attempt=2 without exported attempt-2 snapshot. | `DockerDeliveryRecoveryCommandTests.Retype_requires_second_committed_tuple` | recovery rejected until second committed tuple is present. |
| PC-187 | `docker-compose.delivery-fixture.yml`: Set restart: always on fixture server. | `DockerStackContractTests.Fixture_restart_policy_is_disabled` | restart policy equals no. |
| PC-188 | `docker-compose.yml`: Set Delegation__DiagnoseEnabled=true. | `DockerStackContractTests.Fresh_Delegation__DiagnoseEnabled_is_disabled` | effective Delegation__DiagnoseEnabled equals false. |
| PC-189 | `docker-compose.yml`: Set Delegation__OutputDistillerEnabled=true. | `DockerStackContractTests.Fresh_Delegation__OutputDistillerEnabled_is_disabled` | effective Delegation__OutputDistillerEnabled equals false. |
| PC-190 | `docker-compose.yml`: Set Hangfire__ServerEnabled=true. | `DockerStackContractTests.Fresh_Hangfire__ServerEnabled_is_disabled` | effective Hangfire__ServerEnabled equals false. |
| PC-191 | `docker-compose.yml`: Set ZombieCensus__Enabled=true. | `DockerStackContractTests.Fresh_ZombieCensus__Enabled_is_disabled` | effective ZombieCensus__Enabled equals false. |
| PC-192 | `docker-compose.yml`: Set WorktreeResidue__Enabled=true. | `DockerStackContractTests.Fresh_WorktreeResidue__Enabled_is_disabled` | effective WorktreeResidue__Enabled equals false. |
| PC-193 | `docker-compose.yml`: Set Schedules__Enabled=true. | `DockerStackContractTests.Fresh_Schedules__Enabled_is_disabled` | effective Schedules__Enabled equals false. |
| PC-194 | `docker-compose.yml`: Set ChannelBridge__Enabled=true. | `DockerStackContractTests.Fresh_ChannelBridge__Enabled_is_disabled` | effective ChannelBridge__Enabled equals false. |
| PC-195 | `docker-compose.yml`: Set Digest__Enabled=true. | `DockerStackContractTests.Fresh_Digest__Enabled_is_disabled` | effective Digest__Enabled equals false. |
| PC-196 | `docker-compose.delivery-fixture.yml`: Mount /var/run/docker.sock into server. | `DockerStackContractTests.Receipt_server_has_no_socket` | receipt server Docker mounts is empty. |
| PC-197 | `docker-compose.delivery-fixture.yml`: Mount /var/run/docker.sock into runner. | `DockerStackContractTests.Receipt_runner_has_no_socket` | receipt runner Docker mounts is empty. |
| PC-198 | `.dockerignore`: Append final !scratch/key.pem re-include rule. | `DockerStackContractTests.Runtime_context_denies_Pem` | effective-context sentinel scratch/key.pem is absent. |
| PC-199 | `docker/tests/Dockerfile.dockerignore`: Append final !scratch/key.pem re-include rule. | `DockerStackContractTests.Tests_context_denies_Pem` | effective-context sentinel scratch/key.pem is absent. |
| PC-200 | `DeliveryCaseIdentity.cs`: Skip inline ceiling validation. | `DockerDeliveryObservationTests.Oversized_summary_is_refused` | oversized summary refused before POST; exact legal boundary accepted. |
| PC-201 | `scripts/verify-docker-stack.ps1`: Skip canonical evidence-root containment check. | `DockerDeliveryRecoveryCommandTests.Foreign_resume_manifest_path_is_refused` | sibling-prefix/traversal/symlink path refused before observation or command. |
| PC-202 | `DeliveryFileBarrier.cs`: Compare host boot nonce but omit attempt in release equality. | `DockerDeliveryBarrierTests.Cross_attempt_release_cannot_unblock` | same-host wrong-attempt release leaves cut blocked; exact release unblocks. |
| PC-203 | `scripts/verify-docker-stack.ps1`: Include observer password in an exported connection diagnostic. | `DockerDeliveryRecoveryCommandTests.Private_credential_is_not_exported` | harmless secret sentinel absent from argv/logs/manifest and exported evidence. |
| PC-204 | `scripts/test-docker.ps1`: Accept existing report whose run identity differs. | `DockerTestCommandTests.Stale_report_is_refused` | nonzero exit and StaleResult; no result-ready acceptance. |
| PC-205 | `DeliveryEvidenceValidator.cs`: Only compare Id, omitting frozen body/sequence/session equality. | `DockerDeliveryObservationTests.Frozen_row_field_change_is_refused` | same-Id changed body/sequence/session candidate rejected. |

### Out of scope

- The 195 explicitly excluded backend classes retain their named native/process/opt-in
  boundaries in the roster. Exclusion is whole-class, including partial files and transitive
  helper calls. The one S4 Grok specification method is separately required on both platforms.
  Existing original suite ownership and nightly policy are unchanged.
- Linux full runs of Antiphon.Agents.Pty.Tests, Antiphon.SessionRunner.Tests and
  Antiphon.PtyHost.Tests; Chromium/browser E2E. E2E receives only compile/staged-apphost checks.
  The four CARD-0588 platform fixes retain their owner and are not counted as Linux coverage.
- Live Telegram/Slack/provider credentials and real paid model turns. Small runs fake gateway
  conformance and isolated Redpanda; no live-token leg is reported covered.
- Linux SourceLanding custody/remote PCs (CARD-0598), native pipe repair (CARD-0594), desktop
  deployment/activation, database migration from Windows, arm64, and full nightly parity.
- No rewrite of production queue/outbox/runtime/DTOs or new public failpoint/admin routes.
  If the specified interfaces cannot expose a cut, Code returns that demonstrated boundary to
  Plan. Health/transport substitutes cannot close the native acceptance rows.

### Checkpoints

This is the closed **Final/Full ordinary** list. `After=all` means S1–S5 and S6a–S6d
committed at the same frozen implementation SHA. Small rows precede Medium; both are required.
Slices may be authored/committed separately, but no unlisted interim build/suite is silently
added. Any necessary red repair or extra diagnostic is reported against its CP and commit.
No full Antiphon.Tests/solution/native-assembly run is additionally commissioned.

The image/client rows use the testing guide's non-TUnit exact-command form. Their Build cell
names one isolated Docker image or client output instead of a .NET bin directory; this is the
explicit build-kind extension for this Docker card, not permission for implicit additional
builds. A `CP-n` cell reuses that build with no build operation. All rows share `After=all`.
Runtime/fixture/native image publication has separate rows and separate image IDs. A row runs
one build and one named check/filter; the controller must not rebuild every time it probes.

Freeze once: `$sha = git rev-parse HEAD`, a random `$runId`, an owned clean context and private
`$manifest` under the recorded evidence root. Windows evidence is under this task's owned
root; server2 evidence is `/work/test-evidence/$runId`. Record these concrete values before
execution. Tags are `antiphon-c590-$runId-{server,runner,session-testing,test-runner,
receipt-probe,delivery-fixture,child-server,child-runner}`. The baseline context is a separate
tracked-file export of `723ac9534fc3fce49b378e287da08b7b11095716` and never the local checkout.
Builds inject full `SOURCE_REVISION=$sha`; .NET builds use isolated `bin-c590/`, Release,
`SourceRevisionId=$sha`, disabled build-server/shared compilation reuse. E2E uses
`bin-c590-e2e/`. No `dotnet test` or AppHost build. Dispose owned alternate outputs only after
all foreground commands exit, checking resolved paths remain in the owned checkout.

Code freezes these **internal verification selectors** in the existing planned scripts:
`verify-docker-stack.ps1` with `-Case` and `-Manifest $manifest` selects only the literal cases
in this table; `test-docker-container.ps1` with `-Checkpoint` and `-Manifest $manifest` selects the
literal build/filter from this manifest, never arbitrary shell input. These are proposed
selectors to implement, not currently available commands. Public `test-docker.ps1 -Group
small|backend|all` stays as designed. A caller supplies no raw SQL/connection string/PID or
cleanup target through these selectors. The controller derives those from owned identities.

CP-6–CP-9 bootstrap the owned parent on server2. CP-10 launches and awaits the actual Raw
test-command session running `pwsh -NoProfile -File scripts/test-docker.ps1 -Group all
-ThrowawayStack`; that foreground session drives CP-11–CP-56, recording each row separately.
CP-10's envelope has no second suite/build hidden inside its cost: all child work is charged
to its subordinate rows. The child-driver performs each named build once from its own clean
source export and joins commands/results to its accepted session generation. CP-57 performs
final independent server2 observation after the initiating desktop CLI has disconnected.
Never use host-side docker exec as CP-10's session origin.

For a TUnit row use `scripts/run-checkpoint.ps1` where available, or its identical result
line inside the Linux container: exact filter, fresh TRX, actual class/method roster and
`executed/passed/failed/skipped` counts. The row's minimum is only a floor: every named class
and every discovered selected method/argument case must execute, with zero unexplained skips.
For Medium the exact combined filters below are also stored in the JSON roster; each operand
has parentheses and suffix wildcard as required by pinned TUnit. Reject any unexpected
executed class. Source counts do not excuse lost parameterized cases. Image/script rows emit
one named case receipt per selector, retain their real child exit, and carry no TUnit claim.
For a negative case, the verifier succeeds only when it asserts the exact expected refusal
and preserves the failed nested run's original nonzero status. This never relabels that run
as successful; it is a successful test of the failure behavior.

CP-55/CP-56 each build one disposable context-probe image from the same owned clean export,
using a temporary Dockerfile containing `COPY . /context` and the selected real ignore policy
copied byte-for-byte as its Dockerfile-specific ignore. Add only harmless fixture sentinels
for every context PC, including a top-level .git pointer and a nested .git directory. Export
the resulting /context inventory and require every sentinel absent and every needed source
present. This qualifies the actual Docker ignore engine independently of the local matcher.
No actual provider/auth/certificate material is present. Remove only the recorded probe image
and container after exporting the inventory. These two builds are ordinary session-owned
Docker work, never part of Windows SourceLanding PCs.

| CP | After | Build | Group | Filter | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c590/ (Windows) | local-guards | `/*/*/(DockerStackContractTests*)\|(DockerTestCommandTests*)\|(TestAppHostPathTests*)\|(LinuxTestRosterTests*)\|(DockerStackSmokeCommandTests*)\|(DockerDeliveryObservationTests*)\|(DockerDeliveryBarrierTests*)\|(DockerDeliveryRecoveryCommandTests*)/*` | R-1–R-7, V-5/V-11/V-12 | all 205 named PC methods ordinary green; >=205 executed, 0 failed/skipped | 12 |
| CP-2 | all | CP-1 | windows-queue | `/*/*/SessionQueueReceiptPlumbingTests/(C475_QueueCommitAndTransportRecovery*)\|(C475_AlreadyIdleWhenIdleHasRecipientReceipt*)\|(C475_PumpPersistsCompleteLinesOnce*)\|(C475_MultilineWritesKeepPasteMarkers*)` | R-9 | 13 expanded cases, 0 failed/skipped | 10 |
| CP-3 | all | CP-1 | windows-apphost | `/*/*/GrokDelegateDispatchTests/the_spec_a_grok_dispatch_builds_would_spawn_the_real_fakegrok_binary` | R-3/R-10, V-5 | 1 executed, 0 failed/skipped | 3 |
| CP-4 | all | CP-1 | phone-home-contract | `/*/*/VerifyPhoneHomeGrokScriptTests/Compose_and_dockerfile_do_not_hardcode_server_origin` | R-10 | 1 executed, 0 failed/skipped | 1 |
| CP-5 | all | tests/Antiphon.E2E -> bin-c590-e2e/ (Windows) | e2e-consumer-compile | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case e2e-staged-apphost -Manifest $manifest` | V-5, R-3/R-10 | build succeeds; staged Windows runner apphost and shared consumer verified; 1 case | 5 |
| CP-6 | all | baseline Dockerfile -> unique baseline image tag | old-root-build | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case baseline-build-failure -Manifest $manifest` | V-1 | captured actual old failing build step; no new-image claim, 1 case | 10 |
| CP-7 | all | Dockerfile -> server image | server-payload | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case server-image-payload -Manifest $manifest` | V-1, R-1 | build plus real DLL/client/bundle/context/SHA checks; 1 case | 14 |
| CP-8 | all | docker/session-runner-grok/Dockerfile --target runtime -> runner image | runner-payload | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case runner-image-payload -Manifest $manifest` | V-1/V-12, R-1/R-10 | default runtime/native/execute/no-test-payload checks; 1 case | 12 |
| CP-9 | all | docker/session-runner-grok/Dockerfile --target session-testing -> session-testing image | testing-runner-payload | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case testing-runner-payload -Manifest $manifest` | V-1/V-12, R-1 | non-root tools/socket-GID policy/capability refusal; 1 case | 10 |
| CP-10 | all | CP-9 | raw-and-session-envelope | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case parent-native-and-command-session -Manifest $manifest` | V-6/V-7/V-12 | Raw challenge+exit; actual command-session launch, awaited with subordinate CP receipts | 8 |
| CP-11 | all | Dockerfile from session clean context -> child-server image | child-server-build | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case child-server-image-payload -Manifest $manifest` | V-1/V-7 | session-owned source export/SHA/image ID recorded; 1 case | 5 |
| CP-12 | all | docker/session-runner-grok/Dockerfile --target runtime -> child-runner image | child-runner-build | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case child-runner-image-payload -Manifest $manifest` | V-1/V-7/V-12 | session builds runner; no recursive socket; 1 case | 4 |
| CP-13 | all | docker/tests/Dockerfile --target test-runner -> test-runner image | test-image-build | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case test-image-context-and-tools -Manifest $manifest` | V-1/V-3/V-4, R-1 | linked sources/resources/tools/samples and safe actual context; 1 case | 14 |
| CP-14 | all | docker/session-runner-grok/Dockerfile --target receipt-probe -> receipt-probe image | receipt-runner-build | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case receipt-runner-image-payload -Manifest $manifest` | V-1/V-8/V-12, R-1 | native FakeGrok payload/terminal wrapper; no Docker tools/socket; 1 case | 4 |
| CP-15 | all | docker/tests/Dockerfile --target delivery-fixture -> delivery-fixture image | fixture-publish | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case fixture-image-payload -Manifest $manifest` | V-1/V-9/V-10, R-1/R-7 | real publish payload/health/client/bundles/same SHA; test-only image; 1 case | 10 |
| CP-16 | all | CP-11 | deployment-state | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case deployment-state -Manifest $manifest` | V-2/V-7/V-12 | fresh migrations/UI/API/version; DB recreation/workspace/volume retention; 1 case | 8 |
| CP-17 | all | client npm ci + npm run build -> isolated /src/client/dist | small-client-lint | `npm --prefix client run lint` | V-3 | build and lint exit 0, no warnings; 1 case | 5 |
| CP-18 | all | CP-17 | small-client-tests | `pwsh -NoProfile -File scripts/test-client.ps1 -JsonResultPath /evidence/client.json` | V-3, R-2 | all 102 frozen Vitest files execute; >=102 tests, 0 failed/unexplained skipped | 8 |
| CP-19 | all | tests/Antiphon.Messaging.Tests -> bin-c590/ (Linux) | small-messaging | `/*/*/*/*` | V-3, R-2 | all 24 classes, >=206 methods; broker opt-in; 0 failed/unexplained skipped | 8 |
| CP-20 | all | tests/Antiphon.Tests -> bin-c590/ (Linux) | linux-roster-fixture | `/*/*/(TestAppHostPathTests*)\|(LinuxTestRosterTests*)\|(DockerDeliveryDatabaseTests*)/*` | V-4/V-5/V-10, R-3/R-4/R-6/R-7 | all 3 classes; 5 helper + 6 roster + 16 DB methods = >=27; 0 failed/skipped | 12 |
| CP-21 | all | CP-20 | linux-staged-apphost | `/*/*/GrokDelegateDispatchTests/the_spec_a_grok_dispatch_builds_would_spawn_the_real_fakegrok_binary` | V-5, R-3 | 1 executed against Linux staged apphost, 0 failed/skipped | 3 |
| CP-22 | all | CP-20 | medium-local-01 | `/*/*/(TestClassificationGuardTests*)\|(AgentTuiOperationCoordinatorTests*)\|(DollarEnvArgTests*)\|(RunnerProcessProbeRedactionTests*)\|(AgentLaunchSpecTests*)\|(AgentProtocolAdapterFactoryTests*)\|(AgentRegistryTests*)\|(AgentSessionSettingsTests*)\|(CodexResponseAnalyzerTests*)\|(CodexWorkingIndicatorTests*)\|(GrokAdapterTests*)\|(GrokCredentialStoreTests*)\|(GrokModelListParserTests*)\|(GrokSignInPromptDetectorTests*)\|(GrokTrustPromptDetectorTests*)\|(OpenCodeAdapterTests*)\|(RunnerClaudeAdapterEffortPromptTests*)\|(RunnerClaudeAdapterTrustPromptTests*)\|(RunnerClaudeAdapterVerifiedPromptTests*)\|(RunnerCodexAdapterReadyTests*)\|(RunnerCodexAdapterSubmitConfirmTests*)\|(RunnerCodexAdapterTurnCompleteTests*)\|(RunnerGrokAdapterSignInPromptTests*)\|(RunnerGrokAdapterTrustPromptTests*)\|(RunnerGrokAdapterTurnCompleteTests*)\|(RunnerTerminalSessionGenerationTests*)\|(SessionRunnerCapabilityGateTests*)\|(SessionRunnerGenerationWireTests*)\|(SessionRunnerHttpClientHerdrWireTests*)\|(TranscriptNormalizerTests*)\|(TranscriptTurnBaselineTests*)\|(AgentLaunchEnvTests*)\|(ApiKeyPlaceholderTests*)\|(ApiKeyProtectorTests*)\|(AgentContextContractTests*)\|(AgentExecutableResolverTests*)\|(AgentPinPathTests*)\|(AgentPresetsTests*)\|(AgentTaskLandVerificationEvidenceTests*)\|(AgentTaskLandingStateTests*)/*` | V-4, R-4 | all 40 frozen classes, >=286 source methods expanded; 0 failed/skipped | 8 |
| CP-23 | all | CP-20 | medium-local-02 | `/*/*/(AgentTaskLivenessTests*)\|(ApiErrorClassifierTests*)\|(ApiErrorRetryScheduleTests*)\|(AreaMapContractTests*)\|(AreaMapLoaderTests*)\|(AwayDigestFormatterTests*)\|(BlockedQuestionTests*)\|(BoardServiceNeedsHumanReviewTests*)\|(BootWedgeContractTests*)\|(CapacityRecoveryPolicyTests*)\|(CardAliasNormalizationTests*)\|(CardFilePolicyTests*)\|(CardFilePrivacyDocumentationTests*)\|(CardIdentifierAllocatorTests*)\|(CardRankingOrderTests*)\|(CardRankingTests*)\|(CardTaskFileRendererTests*)\|(ChannelContractsTests*)\|(ChannelInboundDebouncerTests*)\|(ChannelMachineTurnMatchTests*)\|(CheckInterpretationValidatorTests*)\|(CheckScheduleBackoffTests*)\|(CheckScheduleElapsedTimesTests*)\|(CheckScheduleRoundingTests*)\|(CheckSpecialistLaunchPolicyTests*)\|(CheckpointManifestDocumentationTests*)\|(ClaudeLaunchArgsTests*)\|(CodexLaunchArgsTests*)\|(ColumnTextTests*)\|(CommitOnSettleDocumentationTests*)\|(CommitOnSettlePolicyResolverTests*)\|(CommitRecoveryObligationsTests*)\|(CompletionHeaderOverlapTests*)\|(ComplexityRoutingComposeTests*)\|(ContextCompactionSettingsTests*)\|(DelegateBindRefusalRecoveryTests*)\|(DelegationAllowedRootsFileTests*)\|(DelegationCapabilityContractTests*)\|(DelegationCostTests*)\|(DelegationDenyHookPolicyTests*)/*` | V-4, R-4 | all 40 frozen classes, >=256 source methods expanded; 0 failed/skipped | 8 |
| CP-24 | all | CP-20 | medium-local-03 | `/*/*/(DelegationKindPricingTests*)\|(DelegationPoolDefaultsTests*)\|(DelegationQuestionDetectionTests*)\|(DelegationReportFormatterTests*)\|(DelegationScopeLeaseTests*)\|(DelegationWorkspaceBoundaryTests*)\|(DeliveryBackendCeilingsTests*)\|(DiagnosisTests*)\|(DiagnosticsRedactorTests*)\|(DirectoryBrowseServiceTests*)\|(DirectoryMatcherTests*)\|(ExternalTrackerSyncImportanceProvenanceTests*)\|(GitHubIssuesTrackerWriteTests*)\|(GitIndexLockTests*)\|(GrokDeliveryShapeTests*)\|(GrokLaunchArgsTests*)\|(GrokNativeSessionResumeTests*)\|(GrokQuestionPopupTests*)\|(GrokRulesLiveEvidenceTests*)\|(GrokRulesTransportCompatibilityTests*)\|(HerdrAgentKindMapTests*)\|(HerdrLaunchContextTitleTests*)\|(HerdrStatusDisagreementTests*)\|(InstructionBundleTests*)\|(InstructionFileStampTests*)\|(InterimVerificationReadinessTests*)\|(InternalDecisionPolicyTests*)\|(IssueTrackerAdapterTests*)\|(IssueTrackerConfigParserTests*)\|(LandFailureDiagnosticTests*)\|(LaunchModelArgumentAppenderTests*)\|(ModelAliasTests*)\|(ModelLevelAliasDisplayTests*)\|(MutationRoleContractTests*)\|(OrchestratorInvestigationDetectorTests*)\|(OrchestratorSettingsTests*)\|(OutputDistillationGateTests*)\|(OutputDistillationPolicyTests*)\|(OutputDistillationQueueTests*)\|(PipelineHandoffParseTests*)/*` | V-4, R-4 | all 40 frozen classes, >=410 source methods expanded; 0 failed/skipped | 8 |
| CP-25 | all | CP-20 | medium-local-04 | `/*/*/(PolicyRefreshDeltaTests*)\|(PostLandMutationContractTests*)\|(PostLandMutationPublicationTests*)\|(PostLandMutationReceiptPolicyTests*)\|(ProviderCapacityNoticeTests*)\|(ProviderContractCatalogTests*)\|(PtyInlineCeilingTests*)\|(RemoteControlMenuScreenTests*)\|(RemoteControlPolicyTests*)\|(RepairSourceDocumentationTests*)\|(RestartFailureClassificationTests*)\|(ReviewEvidenceParserTests*)\|(RoutingCandidateParseTests*)\|(ScheduleRecurrenceTests*)\|(ScopeDriftTests*)\|(ScopeOverlapPolicyTests*)\|(ScopeResolverAreaTests*)\|(ScopedVerificationInstructionTests*)\|(SessionContextUsageTests*)\|(SessionReAdoptionStateTests*)\|(SessionRunnerSettingsTests*)\|(SharedWriterLeaseProjectionTests*)\|(SiblingWarningReducerTests*)\|(SlashCommandCatalogServiceTests*)\|(SlashCommandParserTests*)\|(SpecialistAttemptEvidenceTests*)\|(SpecialistFailurePolicyTests*)\|(SpecialistRoleContractTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(StandingSpecialistHealthPolicyTests*)\|(SubagentNotificationTests*)\|(SubscriptionQuotaGateTests*)\|(SubscriptionUsageParserTests*)\|(TaskCompletionProgressPolicyTests*)\|(TierReasoningEffortAgreementTests*)\|(TrackerSyncSummaryFormatterTests*)\|(TranscriptLocalCommandEchoTests*)\|(TypedBodySpillTests*)\|(UnmarkedWaitingContractTests*)\|(UsageLimitWallParserTests*)/*` | V-4, R-4 | all 40 frozen classes, >=379 source methods expanded; 0 failed/skipped | 8 |
| CP-26 | all | CP-20 | medium-local-05 | `/*/*/(VerificationRoundBriefTests*)\|(VerificationRoundInstructionTests*)\|(WorkflowEngineParsingTests*)\|(WorktreeCleanupPresentationTests*)\|(WorktreeListParsingTests*)\|(CachingChatClientTests*)\|(CardStateMachineTests*)\|(RunAttemptStateMachineTests*)\|(WorkflowStateMachineTests*)\|(AppHostBrokerSourceGuardTests*)\|(LandingIdentityControlTests*)\|(LandingRemovalPolicyControlTests*)\|(LogRetentionTests*)\|(WorktreeDeleteAccessProbeTests*)\|(WorktreeManagerSafetyTests*)\|(WorktreeManagerTests*)\|(WorktreeRemovalDefaultTests*)\|(NightlyWatchdogCoreTests*)\|(DelegationHarnessCensusTests*)\|(ProductionRunnerGuardTests*)\|(PtyBackendEnvGuardTests*)\|(SlowTestTripwireTests*)\|(TestLaneCategoryGuardTests*)/*` | V-4, R-4 | all 23 frozen classes, >=157 source methods expanded; 0 failed/skipped | 8 |
| CP-27 | all | CP-20 | medium-database-01 | `/*/*/(AgentTuiDiscoveryTests*)\|(AgentTuiLaunchResolverTests*)\|(AgentTuiModelArgumentMigrationTests*)\|(AgentTuiSecretMigratorTests*)\|(AgentTuiPersistenceTests*)\|(AgentTuiProfileConcurrencyTests*)\|(AgentTuiProfileImporterBackfillTests*)\|(AgentTuiProfileServiceTests*)\|(ApiKeyEnvResolverTests*)\|(ApiKeyLaunchPathTests*)\|(ApiKeyStoreTests*)\|(LaunchEnvLayersIntegrationTests*)\|(AgentBundleAttachmentTests*)\|(AgentChannelServiceIntegrationTests*)\|(AgentControlServiceIntegrationTests*)\|(AgentCreateRaceTests*)\|(AgentCreateSupervisionTests*)\|(AgentNamePinTests*)\|(AgentPinnedInstructionServiceTests*)\|(AgentRemoteControlGateTests*)\|(AgentReplyStyleTests*)\|(AgentServiceIntegrationTests*)\|(AgentSessionBackendTests*)\|(AgentSessionInterruptedLaunchResumeTests*)\|(AgentSessionLaunchFailureTests*)\|(AgentSessionLaunchQueueOwnershipTests*)\|(AgentStartRecoveryTests*)\|(AgentSupervisionTests*)\|(AgentSystemPromptLaunchTests*)\|(AgentTaskAgentKindTests*)\|(AgentTaskAnswerTests*)/*` | V-4, R-4 | all 31 frozen classes, >=489 source methods expanded; 0 failed/skipped | 10 |
| CP-28 | all | CP-20 | medium-database-02 | `/*/*/(AgentTaskAutoTitleTests*)\|(AgentTaskCallerResolutionTests*)\|(AgentTaskCardBindingTests*)\|(AgentTaskCatchUpSettlementTests*)\|(AgentTaskCheckInterpreterTests*)\|(AgentTaskCheckRemapTests*)\|(AgentTaskCheckScheduleTests*)\|(AgentTaskConcurrencyLimitTests*)\|(AgentTaskDetailBlockedContextTests*)\|(AgentTaskInternalDecisionLifecycleTests*)\|(AgentTaskInternalDecisionMigrationTests*)\|(AgentTaskLandAdmissionControlledTests*)\|(AgentTaskLandApprovalPersistenceTests*)\|(AgentTaskLandApprovalRequestTests*)\|(AgentTaskLandBoundaryControlledTests*)\|(AgentTaskLandConcurrencyControlledTests*)\|(AgentTaskLandFailureDiagnosticTests*)\|(AgentTaskLandMonitoringTests*)\|(AgentTaskLandReceiptTests*)\|(AgentTaskLandRequestTests*)\|(AgentTaskLandSourcePersistenceTests*)\|(AgentTaskLandSweepTests*)\|(AgentTaskLandingPersistenceTests*)\|(AgentTaskListStatusFilterTests*)\|(AgentTaskPipelineStatusTests*)\|(AgentTaskPoolTests*)\|(AgentTaskProjectScopeTests*)\|(AgentTaskRefineTests*)\|(AgentTaskReplyOverlayTests*)\|(AgentTaskReuseEnqueueTests*)\|(AgentTaskReviewEvidenceTests*)\|(AgentTaskScopedListTests*)\|(AgentTaskSettlementRaceTests*)\|(AgentTaskStallEscalationTests*)\|(AgentWorkspaceProvisionerTests*)\|(AlertPersistenceTests*)\|(AlertRoutingTests*)\|(ApiErrorRecoveryServiceTests*)\|(AppHostWatchdogStateAttentionServiceTests*)/*` | V-4, R-4 | all 39 frozen classes, >=430 source methods expanded; 0 failed/skipped | 10 |
| CP-29 | all | CP-20 | medium-database-03 | `/*/*/(AttentionServiceTests*)\|(AwayDigestNotifierTests*)\|(AwayDigestProjectionTests*)\|(BlockedTaskNotifierTests*)\|(BoardProjectArchiveTests*)\|(BoardServiceIntegrationTests*)\|(BootLivenessProbeScopeTests*)\|(BootReplyWatchTests*)\|(BootReplyWatchdogTests*)\|(CapacityRecoveryAcceptanceTests*)\|(CapacityRecoveryAttentionTests*)\|(CapacityRecoveryCompatibilityTests*)\|(CapacityRecoveryGrantLivenessTests*)\|(CapacityRecoveryPersistenceTests*)\|(CapacityRecoveryQueueTests*)\|(CapacityRecoveryRefusalTests*)\|(CapacityRecoverySupervisionTests*)\|(CapacityRecoveryTaskTests*)\|(CapacityWaitOrphanSweepTests*)\|(CardCorrectionIntegrationTests*)\|(CardDiagnosisApplyTests*)\|(CardDiagnosisSweepTests*)\|(CardFilePrivacyMigrationTests*)\|(CardServiceTrackerPushTests*)\|(CardSpawnModelArgumentTests*)\|(CardWorkTransitionServiceTests*)\|(ChannelBatchingTests*)\|(ChannelBridgeTests*)\|(ChannelFollowUpAttachmentTests*)\|(ChannelIngressIncidentTests*)\|(ChannelMachineTurnTextTests*)\|(ChannelReplyDurabilityTests*)\|(ChatChannelServiceTests*)\|(CheckInterpreterProvisionerTests*)\|(CheckNoteDeliveryHandoffTests*)/*` | V-4, R-4 | all 35 frozen classes, >=495 source methods expanded; 0 failed/skipped | 10 |
| CP-30 | all | CP-20 | medium-database-04 | `/*/*/(CodexDelegateDispatchTests*)\|(CommitOnSettlePolicyTests*)\|(CommitRecoveryObligationsLoaderTests*)\|(CompactionRecoveryTests*)\|(ComplexityAttentionTests*)\|(ComplexityChainRoleTests*)\|(ComplexityChainServiceTests*)\|(ComplexityCreateTests*)\|(ComplexityDispatcherTests*)\|(ComplexityRoutingWalkTests*)\|(ComplexityWallRerouteTests*)\|(ContextCompactionAgentTests*)\|(ContextCompactionSweepTests*)\|(DataRetentionServiceTests*)\|(DecisionCardNotifierTests*)\|(DelegateBundleLaunchTests*)\|(DelegationCapabilityTests*)\|(DelegationCostBackfillTests*)\|(DelegationRetryEventKindTests*)\|(DiagnoseProvisionerTests*)\|(DiagnosticsBundleServiceTests*)\|(DispatchHeldAttentionTests*)\|(DispatchHoldVisibilityTests*)\|(ExternalTrackerSyncIdentifierTests*)\|(ExternalTrackerSyncLandingColumnTests*)\|(GrokCredentialProbeDispatcherTests*)\|(GrokRulesChannelTests*)\|(GrokRulesCompactionRecoveryTests*)\|(GrokRulesCompactionTests*)\|(GrokRulesCompositionTests*)\|(GrokRulesFailureTests*)\|(GrokRulesInitializationTests*)\|(GrokRulesLaunchRefusalTests*)\|(GrokRulesQueueBarrierTests*)\|(GrokRulesReadyOrderingTests*)\|(GrokRulesReceiptTests*)\|(GrokRulesReplayMatrixTests*)\|(GrokRulesResumeMigrationTests*)\|(GrokRulesTransactionTests*)\|(GrokSignInIncidentTests*)/*` | V-4, R-4 | all 40 frozen classes, >=270 source methods expanded; 0 failed/skipped | 10 |
| CP-31 | all | CP-20 | medium-database-05 | `/*/*/(HerdrLabelFollowConcurrencyTests*)\|(HerdrLabelFollowTests*)\|(HerdrLaunchContextResolverTests*)\|(HerdrPlacementPreflightTests*)\|(HerdrPlacementSettingsTests*)\|(HerdrSupervisionAttentionTests*)\|(HerdrSupervisionBackoffTests*)\|(HerdrSupervisionFailureEvidenceTests*)\|(IncidentPageNotifierTests*)\|(LandingProtocolGuardTests*)\|(LandingProtocolHarnessTests*)\|(ModelAvailabilityCreateTests*)\|(ModelAvailabilityDispatcherTests*)\|(ModelAvailabilityManualTests*)\|(ModelAvailabilityTests*)\|(MutationAdmissionTests*)\|(NamedCodexAgentLaunchTests*)\|(OrchestratorInvestigationSweepTests*)\|(OrchestratorServiceIntegrationTests*)\|(OrchestratorStateProjectionTests*)\|(OrchestratorTrackerCadenceTests*)\|(OutputDistillationAdmissionTests*)\|(OutputDistillationCleanupTests*)\|(OutputDistillationDeadlineTests*)\|(OutputDistillationMigrationTests*)\|(OutputDistillationTests*)\|(OutputDistillerProvisionerTests*)\|(ParkedMessageSweepServiceTests*)\|(PhoneHomeReconciliationTests*)\|(PinnedAgentKindTests*)\|(PinnedCodexProfileDispatchLaunchTests*)\|(PinnedProfileLaunchSpecTests*)\|(PolicyRefreshServiceTests*)\|(PolledCompletionNoteShrinkTests*)\|(PostLandMutationWorkflowTests*)\|(ProjectDeletionTests*)\|(ProjectServiceTests*)\|(ProviderSignInRequiredCreateTests*)\|(QueuedInputWatchdogTests*)\|(ReceiptFailureDeliveryTests*)/*` | V-4, R-4 | all 40 frozen classes, >=346 source methods expanded; 0 failed/skipped | 10 |
| CP-32 | all | CP-20 | medium-database-06 | `/*/*/(RemoteControlMaintenanceQueueTests*)\|(RemoteControlModalAttentionTests*)\|(RemoteControlModalPersistenceTests*)\|(RemoteControlModalWatchTests*)\|(RemoteControlRecoveryTests*)\|(ReviewReplyDispatcherTests*)\|(RoutingPinCandidateCreateTests*)\|(RoutingPinCandidateDispatchTests*)\|(RoutingPinCandidateTests*)\|(RoutingPinCreateTests*)\|(RoutingPinDispatcherTests*)\|(RoutingPinServiceTests*)\|(RunAttemptStallDetectorTests*)\|(ScheduleCardActionTests*)\|(ScheduleSweepTests*)\|(SessionContextUsagePersistenceTests*)\|(SessionDeliveryProfileTests*)\|(SessionFinishedDuplicateTests*)\|(SessionGenerationDeliveryOverlapTests*)\|(SessionHealthTests*)\|(SessionMessageQueueBootWedgeTests*)\|(SessionMessageQueueDeliveryVerificationTests*)\|(SessionMessageQueueInterruptedAttemptTests*)\|(SessionMessageQueueServiceTests*)\|(SessionMessageQueueSpillTests*)\|(SessionMessageQueueSupervisionTests*)\|(SessionMessageQueueWedgedHeadTests*)\|(SessionReconciliationServiceTests*)\|(SessionTerminationSourcePersistenceTests*)\|(SpecialistHealthAttentionTests*)\|(SpecialistInputTransportTests*)\|(SpecialistPublicationTests*)\|(SpecialistQualificationTests*)\|(SpecialistStartIntentTests*)\|(SpecialistTaskRunnerDeadlineTests*)\|(SpecialistToolPolicyLaunchTests*)\|(StageOutcomeBackfillTests*)\|(StageOutcomeSummaryTests*)\|(StandingContinuityAttentionTests*)/*` | V-4, R-4/R-8 | all 39 frozen classes, >=496 source methods expanded; 0 failed/skipped | 10 |
| CP-33 | all | CP-20 | medium-database-07 | `/*/*/(StandingContinuityRecoveryTests*)\|(StandingRestartAccountingTests*)\|(StandingSessionOwnershipTests*)\|(StandingSessionQueueSwitchTests*)\|(StandingSessionSelectionTests*)\|(StandingSessionSwitchConcurrencyTests*)\|(StandingSpecialistSeatTests*)\|(SubscriptionQuotaGateDispatchTests*)\|(SubscriptionUsageMonitorTests*)\|(TaskDeadlinePolicyTests*)\|(TaskProgressPolicyTests*)\|(TaskProgressStallSweepTests*)\|(TrackerBidirectionalSyncTests*)\|(TrackerCardStatePushServiceTests*)\|(TrackerSyncNotifierTests*)\|(TrackerTokenResolverTests*)\|(TranscriptBindingIncidentTests*)\|(TranscriptPromptSpanTests*)\|(WallRerouteDispatchTests*)\|(WatchdogServiceTests*)\|(WorkflowDefinitionLoaderTests*)\|(WorkflowEngineTests*)\|(WorkflowTrackerActivationTests*)\|(WorktreeCleanupJournalTests*)\|(WorktreeHealthServiceTests*)\|(DatabaseSeederTests*)\|(KanbanPersistenceTests*)\|(ZombieCensusServiceTests*)\|(CommitOnSettleMigrationTests*)\|(SmokeTests*)\|(ControlledLandingGitTests*)\|(DelegationTestServicesTests*)\|(TestDbFixtureIsolationTests*)\|(TestDbFixtureLifecycleTests*)/*` | V-4, R-4 | all 34 frozen classes, >=323 source methods expanded; 0 failed/skipped | 10 |
| CP-34 | all | CP-20 | medium-http-01 | `/*/*/(AgentTuiApiTests*)\|(HerdrPaneDisposalHttpWireTests*)\|(PhoneHomeConnectionTests*)\|(ApiKeyApiTests*)\|(DelegationCapabilityApiTests*)\|(AgentModelLevelBindTests*)\|(AgentPinnedInstructionEndpointTests*)\|(AgentReplyStyleEndpointTests*)\|(AgentTaskDispatcherWiringTests*)\|(AgentTaskListEndpointTests*)\|(AgentTaskPipelineEndpointTests*)\|(AgentTaskRoleBindingTests*)\|(AgentTaskScopedListEndpointTests*)\|(AttentionApiTests*)\|(AuditArchiveEndpointTests*)\|(BoardCardOrderApiTests*)\|(BoardCardOrderIntegrationTests*)\|(BoardProjectArchiveApiTests*)\|(CardAliasApiTests*)\|(CardCommentApiTests*)\|(CardCorrectionApiTests*)\|(CardFilePolicyApiTests*)\|(CardFileSyncDisabledEndpointTests*)\|(CardFileSyncEndpointTests*)\|(CardIdentifierResolutionTests*)\|(CardPrivateNotesApiTests*)\|(CardReorderApiTests*)\|(CardReorderIntegrationTests*)\|(CardThreadEndpointTests*)\|(CardTrackerPushApiTests*)\|(ChannelConsumerIdentityEndpointTests*)\|(ChannelPreamblePresetEndpointTests*)\|(ComplexityChainHttpTests*)\|(ComplexityChainRoleHttpTests*)\|(DiagnosisEndpointTests*)\|(DiagnosticsBundleEndpointTests*)\|(DistillationEndpointTests*)\|(FileSystemBrowseApiTests*)\|(FileSystemBrowseMockedTests*)\|(HealthEndpointTests*)/*` | V-4, R-4 | all 40 frozen classes, >=194 source methods expanded; 0 failed/skipped | 10 |
| CP-35 | all | CP-20 | medium-http-02 | `/*/*/(HerdrPaneDisposalApplicationTests*)\|(HerdrPaneDisposalEndpointTests*)\|(HomeTaskServiceIntegrationTests*)\|(ModelAvailabilityHttpTests*)\|(MutationPipelineTests*)\|(PhoneHomeEventPumpTests*)\|(PhoneHomeQueuedTurnTests*)\|(PhoneHomeSessionRoutingTests*)\|(PhoneHomeStandingLaunchTests*)\|(PolicyRefreshEndpointTests*)\|(ProductionRunnerIsolationTests*)\|(ResponseCompressionIntegrationTests*)\|(ScheduleEndpointsTests*)\|(StageOutcomeFindingEndpointTests*)\|(StandingSessionRecoveryHttpTests*)\|(StandingSpecialistRoutingHttpTests*)\|(StandingSpecialistRoutingMigrationTests*)\|(SubscriptionUsageHttpTests*)\|(TrackerSyncEndpointTests*)\|(HangfireStartupSafetyTests*)\|(ProgramStartupConcurrencyTests*)/*` | V-4, R-4 | all 21 frozen classes, >=122 source methods expanded; 0 failed/skipped | 10 |
| CP-36 | all | CP-20 | medium-database-policy | `/*/Antiphon.Tests.Application/SpecialistToolPolicyTests/*` | V-4, R-4 | all 1 frozen classes, >=4 source methods expanded; 0 failed/skipped | 4 |
| CP-37 | all | CP-13 | session-export-cleanup | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case session-result-export-and-child-cleanup -Manifest $manifest` | V-7/V-11, R-2/R-5 | all subordinate results/hash/exit linked; exact child gone, parent preserved; 1 case | 4 |
| CP-38 | all | CP-9 | session-denied-socket | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case session-denied-socket -Manifest $manifest` | V-7/V-11/V-12, R-2/R-5 | real socket-denied test session fails before child create; 1 negative case | 4 |
| CP-39 | all | CP-13 | interrupted-export-cleanup | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case interrupted-export-cleanup -Manifest $manifest` | V-11, R-5 | interrupted copy/action retains residue; explicit identity-checked resume; 1 case | 6 |
| CP-40 | all | CP-14 | receipt-stock-idle | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case stock-idle -Manifest $manifest` | V-8 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 4 |
| CP-41 | all | CP-14 | receipt-stock-busy | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case stock-busy -Manifest $manifest` | V-8 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 4 |
| CP-42 | all | CP-15 | receipt-insert-refused | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case insert-refused -Manifest $manifest` | V-9/V-11 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 6 |
| CP-43 | all | CP-15 | receipt-insert-committed-idle | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case insert-committed-idle -Manifest $manifest` | V-9/V-11 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 6 |
| CP-44 | all | CP-15 | receipt-insert-committed-busy | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case insert-committed-busy -Manifest $manifest` | V-9/V-11 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 6 |
| CP-45 | all | CP-15 | receipt-attempt-committed | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case attempt-committed -Manifest $manifest` | V-9/V-11 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 6 |
| CP-46 | all | CP-15 | receipt-body-before-enter | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case body-before-enter -Manifest $manifest` | V-9/V-11 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 6 |
| CP-47 | all | CP-15 | receipt-recipient-before-ingestion | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case recipient-before-ingestion -Manifest $manifest` | V-9/V-11 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 6 |
| CP-48 | all | CP-15 | receipt-transcript-save-fails-release | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case transcript-save-fails-release -Manifest $manifest` | V-9/V-11 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 6 |
| CP-49 | all | CP-15 | receipt-transcript-save-fails-restart | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case transcript-save-fails-restart -Manifest $manifest` | V-9/V-11 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 6 |
| CP-50 | all | CP-15 | receipt-receipt-before-verdict | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case receipt-before-verdict -Manifest $manifest` | V-9/V-11 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 6 |
| CP-51 | all | CP-15 | receipt-response-before-client | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case response-before-client -Manifest $manifest` | V-9/V-11 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 6 |
| CP-52 | all | CP-15 | receipt-receipt-before-manifest | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case receipt-before-manifest -Manifest $manifest` | V-9/V-11 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 6 |
| CP-53 | all | CP-15 | receipt-changed-generation | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case changed-generation -Manifest $manifest` | V-9/V-11 | 1 exact GenerationMismatch refusal; no recovered-delivery claim | 6 |
| CP-54 | all | CP-15 | receipt-failure-summary | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case failure-summary -Manifest $manifest` | V-9/V-11 | 1 reached case with complete native+stored receipt and declared write/tuple counts | 6 |
| CP-55 | all | temporary context-probe Dockerfile + runtime ignore -> owned probe image | runtime-context-engine | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case runtime-context-engine -Manifest $manifest` | V-1, R-1 | actual Docker context excludes every runtime sentinel; required sources remain; 1 case | 4 |
| CP-56 | all | temporary context-probe Dockerfile + test ignore -> owned probe image | test-context-engine | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case test-context-engine -Manifest $manifest` | V-1/V-3/V-4, R-1 | actual Docker context excludes every test sentinel; linked sources remain; 1 case | 4 |
| CP-57 | all | CP-7 | server2-handoff | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case server2-independent-handoff -Manifest $manifest` | V-2/V-7/V-13 | after initiating desktop CLI exit: same server2 stack API+session operation, complete manifest/evidence/residue inventory; 1 case | 6 |

Native timing allowance is real wall time: 60-second Pending stranded threshold; an interrupted
Sent attempt needs 80 seconds (30 confirm + 20 grace + 30 clock tolerance), then up to the
60-second sweep cadence, within the 60-minute eligibility window. Six minutes per cut includes
bootstrap, reached/export/kill, 140-second worst initial recovery wait, receipt/export and cleanup.
Healthy no-cut cases receive four minutes. A declared not-reached timeout is failed evidence,
not an implicit release; preserve the production settings and log actual elapsed times. Polls
wait only on recorded state, not arbitrary sleep-based guesses. The before-ingestion gate blocks
both runtime routes; the save-failure case invokes real fallback before disarming and must export
its barrier promptly without relaxing the server's windows.

CP-20 reuses one Linux backend build for the 15 Medium shards and apphost check. Each fresh test
host can start its own owned PostgreSQL fixture; their startup/teardown is included in that
row's estimate. Run serially; no co-scheduling native assemblies or sharing a database between
independent test hosts. CP-1 is reused by the three Windows regressions. All owner labels, image
IDs, roster hash, actual filters and counts are copied into final evidence before removal.
CP-1 through CP-57 union covers V-1–V-13 and R-1–R-10; ordinary scope has no empty row.

### Cost

All following minutes are **estimates**, not measured green execution. This TestDesign ran
0 product builds, 0 product tests and 0 PC cycles; it used read-only source/census checks.

- **Code ordinary V/R floor = 414 minutes**, the sum of all 57 CP Min cells.
  This already includes each named image/client/.NET build, fresh test-host DB startup,
  all 15 Medium exact filters (134 minutes), Small, Windows regressions, native cut waits,
  evidence export and scoped cleanup. The CP table/JSON give the exact filters and minutes.
- Code setup/preflight allowance = **45 minutes** (server2 identity/tools/socket/network/private
  files/owned volumes and manifest). Implementation plus authoring allowance = **1,200 minutes**
  (packaging/commands 300, fixture/observer/cuts 420, 205 guard methods and DB matrices 360,
  documentation/roster/discovery integration 120). Estimated Code commissioning floor =
  **1659 minutes**. Authoring is not hidden in the execution floor.
- **Mutation PC floor = 826 minutes**: **6 minutes** initial local inherited isolated build,
  discovery and DB-free baseline, plus **205 × 4 = 820 minutes**. Each PC-1–PC-205 receives
  0.5 minute apply/restore verification, 1.5 minutes for the mutated incremental build + 0.25
  exact-method red run, 1.5 minutes fresh restored build + 0.25 same-method green run.
  Filter for PC-n is exactly `/*/*/` followed by its table's Class and Method segments;
  all four minutes are per control, not per class. No remote/native/DB battery is hidden here.
- **Combined setup/build + ordinary V/R + every PC break/red/restore/green =
  1285 minutes** (45 + 414 + 826), excluding authoring and separate Review.
  Including the stated authoring allowance = **2485 minutes**. Retry/repair
  time after a real failure is additional and reported, never converted into a longer timeout.
- Build reuse avoids **19 extra backend builds** compared with rebuilding for each of the
  3 reused Windows and 16 reused Linux test rows; at an estimated 3 minutes each that is
  **57 minutes estimated saved**. This is build scheduling savings, not reduced test coverage.
  The repeated child runtime builds are explicitly priced because the actual session-created
  source export/build is a separate acceptance obligation. **Measured savings = 0 minutes**:
  no equivalent before/after product battery ran here. **PC batching savings = 0 minutes**:
  most controls share scripts/fixture files, so no independence discount is assumed.

Handoff audit: bodies/nearest fixtures read; **guards=205, mapped=205, missing=0,
duplicate PC mappings=0**. All 205 controls have distinct exact methods, syntactically valid
defects, decisive assertions and DB-free Windows-local execution ownership. The roster has
722 baseline assembly/class entries plus 9 planned entries, no duplicate baseline identity,
and 503 included backend classes partitioned once across 15 explicit filters. Code must
materialize and execute this design; no unimplemented method is represented as already green.
Native setup/dependency failures remain acceptance failures, not grounds to reopen the agreed
receipt or SourceLanding boundaries. **Next: Code**, then ordinary Review, land, and the
separately commissioned Windows-local post-land Mutation battery.
