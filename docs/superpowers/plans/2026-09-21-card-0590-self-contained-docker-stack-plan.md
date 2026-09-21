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
