# CARD-0490: one Linux Grok session on a phone-home runner

Plan task `c12f1fd5`, completed 2026-09-20 (inspection began 2026-09-19). Baseline:
investigation task `da82967d`, commit
`b7f8903f`, [investigation](../../investigations/2026-09-19-card-0490-phone-home-runner.md).
This is the first slice of CARD-0490, not completion of its later image/lifecycle work.

The deliverable is one named, cardless, persistent Grok session in a desktop Linux
container. The runner registers with Antiphon, opens the outbound connection used
for dispatch, and completes one queued turn with a matching complete `UserPrompt`,
assistant reply and `TurnEnd` in Antiphon's transcript. Local Windows sessions keep
using `SessionRunner:BaseUrl` and their existing HTTP/SSE contract.

TestDesign is a separate next stage. The test names and PC seeds below establish
the intended seams; they are proposed tests, not claims of implemented or executed
coverage. TestDesign must append the executable verification design before Code.

## Ground truth

Paths and symbols below were checked at `b7f8903f`; published Linux artifacts,
container networking and OAuth persistence are evidence inherited from the
investigation, not experiments repeated by this Plan task.

| Card assumption / requirement | What this code does | Consequence for this slice |
|---|---|---|
| A runner can phone home | `server/Program.cs:262` registers one `SessionRunnerHttpClient`; its constructor fixes `BaseAddress`; `StartAsync` posts to that URL. `SessionRunnerEventPump` opens runner `/events`. No runner registration exists. | Add a genuine runner-initiated control channel. Changing BaseUrl or publishing a container port is insufficient. |
| Sessions can be assigned to a second runner | `AgentSession` has `StandingAgentId`, `SessionBackend`, cwd and generation, but no runner ownership. `AgentProtocolAdapterFactory.Create(kind)` injects the same client into every adapter. | Persist ownership before launch; bind adapters and later session operations to it. PtyHost/Herdr is a backend, not a runner identity. |
| Runner absence means a session is gone | `SessionReconciliationService.ScanAsync` fetches one list and compares it with all live DB sessions; its absence arm marks a row Failed. Several other consumers also read one list. | Reconcile independently per owner. An unavailable owner's list is unknown, never an empty list. |
| A linux-x64 publish is enough to launch | Investigation published `Antiphon.PtyHost` and `libporta_pty.so` from Porta.Pty 1.0.7. `PtyHostLauncher.HostExeName` is `.exe`; `PtyHost/Program.cs --spawn` always calls `Win32ProcessSpawner`. | Port apphost selection and detached spawn. A successful publish alone is not native execution evidence. |
| Fixing the launcher filename is enough | `ShadowCopyStore.TryBuildHostClosure` explicitly includes `Antiphon.PtyHost.exe`, not the extensionless apphost. | Include the Linux apphost in the dependency closure and verify executable permissions/native assets in the shadow copy. |
| Linux can use the existing Grok definition | `server/appsettings.json` defines `grok.exe`. `AgentSessionLaunchComposer` injects the global localhost `ANTIPHON_API`. | Apply a narrowly scoped container launch projection; leave the Windows definition and origin intact. |
| Put `/work` on the standing agent | `AgentControlService.StartInteractiveSessionAsync` calls Windows `Path.GetFullPath` and `Directory.Exists` on the agent cwd. Other host services use that cwd. | Keep the host cwd; persist a separate runner cwd from one explicit bind-mount mapping. Never interpret `/work` through Windows path APIs. |
| Host worktrees can be mounted implicitly | Dispatcher/worktree creation uses Windows paths. No container worktree protocol exists. | Reject delegated/card/worktree/SourceLanding use of the pinned agent in this slice; launch only the named cardless standing session. |
| The Linux PTY ceiling fix must land first | `PtyBackendPolicy` falls back to InboxConhost on Linux; Grok `PtyDeliveryCeilings.ForAgentKind` already sets `BriefInlineMaxBytes=0`. | CARD-0038 is parallel work, not this slice's dependency. Keep conservative Grok delivery and existing spill/receipt behavior. Do not claim a measured POSIX paste ceiling. |
| Linux must implement Windows custody | `SessionRunnerRuntime.VerificationCustodyBackend` already returns null off Windows. Memory-limit Job Object usage is conditional. | Keep memory limit zero, reject verification bindings, do not advertise `windows-job-v1`. No cgroup/custody project. |
| Existing transcript handling is enough evidence | `GrokTranscriptTailer.ResolveUpdatesPath` derives `GROK_HOME/sessions/<escaped-full-cwd>/<id>/updates.jsonl` on the runner. Linux Grok 1.0.34's path was not measured by this investigation. | Runner and Grok share Linux paths; prove actual native transcript binding in the container and collect a small sanitized fixture if the format/path differs. |
| Container callbacks and auth are unresolved | CARD-0575 measured `http://host.docker.internal:17202` and mounted `GROK_HOME` surviving replacement/rebuild. Its exec wrapper was superseded, not shipped. | Reuse those facts. Never bake OAuth into an image; do not recreate the docker-exec architecture. |
| A screen or successful transport response proves the turn | Queue, Grok rules initialization and runtime invariants require transcript evidence; adapters use snapshot/quiet only for readiness and submission. | Register/start/acknowledgement/visible PONG are intermediate evidence, not acceptance. |

## Decisions

These are implementation choices within the brief, not pending operator questions.
Deployment-specific IDs, paths and credentials are configuration inputs. Feature
enablement defaults to false; the existing installation therefore keeps its routing.

### D-1: one opt-in target, no fleet scheduler

Add typed server `PhoneHomeRunnerSettings` for one allowed runner ID, one standing
agent GUID, one exact host workspace root, runner workspace `/work`, child
`GROK_HOME`, callback origin and authentication secret. Add runner-side typed
`PhoneHomeSettings` for its ID, server origin and mounted secret path. Validate
enabled configuration at startup. Use `IOptions<T>`, with configuration binding
only in composition roots.

The selected agent is named, non-pool, cardless, Grok/PtyHost, manually started and
`AlwaysOn=false` for this first acceptance. A named persistent session is sufficient;
automatic restart/spend policy is not part of this deliverable. Registration can
serve only this agent with capacity one. No Agent CRUD/UI field, runner CRUD UI,
placement algorithm or durable runner-fleet table is needed. Rejected: repointing
the global BaseUrl, adding a new SessionBackend enum value, and automatic assignment
of arbitrary Grok work to the container.

### D-2: authenticated registration plus one outbound WebSocket

After local startup/adoption has completed, the runner posts to
`/api/session-runners/register`, then opens
`/api/session-runners/{runnerId}/connect` as an outbound WebSocket. All control,
responses, heartbeat and live events use that socket. No host-published runner port
is required. The registration has protocol version, runner ID, process boot ID,
`RunnerStoreId`, platform, capacity one and the existing capabilities DTO. An ID is
a routing name, not authentication and not Windows custody identity.

Use a per-runner pre-provisioned secret: server user-secrets/untracked configuration,
runner read-only secret file. Authenticate both endpoints; registration returns a
short-lived, single-use connection ticket bound to runner/store/boot. Never put a
secret in a URL, log, transcript, child environment or registration read model.
The local desktop origin is the brief's measured HTTP endpoint; remote deployment,
TLS provisioning and remote/server2 enrollment remain excluded.

Rejected: published HTTP as the primary transport (does not invert reachability),
docker exec, an external broker, and heartbeat/poll/result/event endpoints. Polling
is viable, but introduces more acknowledgement surfaces for this one bidirectional
session. Use built-in WebSocket support, not another transport service.

### D-3: small transport envelopes around unchanged session contracts

Put phone-home envelope records in `Antiphon.SessionRunner.Contracts`. Use a fixed
operation enum and existing request/result bodies; no arbitrary URL, shell string
or user-selected executable RPC. Supported operations are capabilities, health,
list/get, launch, buffer/snapshot/transcript, input, conditional input, clear buffer,
resize and generation-conditional kill. Expose the existing explicit session stop
behavior through generation-conditional kill for the container. Reject Herdr,
kill-all, verification custody and unknown operations with typed errors.

Each request/result carries connection epoch and request ID. A command naming a
session must match the persisted runner/store owner; existing launch generation,
generation echo, conditional-input and kill-generation DTOs remain intact.
Responses preserve existing Problem Details codes. Reuse/extract the existing
HTTP client's DTO mapping and capability-validation logic rather than creating a
second set of launch semantics. Local REST DTOs, routes and SSE event names do not
change. Runner dispatch calls `SessionRunnerRuntime` directly through a narrow
dispatcher sharing route error mapping; it does not HTTP-proxy arbitrary targets.

Use a single socket writer and one receive pump that correlates responses without
waiting for launch execution. Run runtime commands outside that receive pump so
heartbeat, event receipt and cancellation continue during launch. Mutating commands
for the single session are ordered; read commands cannot block behind an idle
event consumer. Default limits are 32 in-flight requests, 16 MiB per complete UTF-8
JSON message (including fragmented WebSocket messages), and 1,024 events / 16 MiB
of pending live events, whichever limit is reached first. Make these typed,
positive configuration values; bound both peers before allocating the whole body. Oversize
or overflow closes the channel for recovery with an explicit reason, never silently
truncates a transcript or turns dropped events into delivery proof. TestDesign must
test these limits against configured buffer/rules sizes. A transcript larger than
the message limit is a visible unsupported read requiring an explicit configuration
change, not a successful partial transcript or endless reconnect retry.

### D-4: transient channel, durable session ownership

Add nullable `RunnerId`, `RunnerStoreId` and `RunnerCwd` to `AgentSession`, configured
and migrated through EF CLI. All legacy rows have null runner fields and mean the
existing local HTTP runner. New container rows persist all three with the accepted
`StartedAt` before launch enqueue. Constrain the three fields to all-null or
all-present; runner ID is a bounded stable string, store identity a GUID and runner
cwd a POSIX path. A transaction rollback produces no command.
The owning runner/store never changes for an existing conversation. A changed pin
or missing configuration cannot move an old conversation onto Windows.

Keep registration/liveness/request waiters in a DI-owned in-memory directory.
After server restart, runner registration rebuilds availability; DB ownership still
routes the session. `RunnerStoreId` must survive runner restart in the mounted state
directory. A different store under the same runner name cannot adopt a live binding;
report a store mismatch and require explicit recovery. Do not infer ownership from
PID, cwd, agent name or whichever connection was most recent.

Rejected: an in-memory session map, storing only the agent's current pin, and a
durable distributed command queue. These respectively lose ownership on restart,
retarget live sessions, or duplicate the existing session/message queue machinery.

### D-5: unavailable means unavailable; no transport replay of writes

Use runner-originated application heartbeats every 15 seconds and a 90-second
lease, measured by the server's injected `TimeProvider`. A disconnected socket is
immediately unavailable for new work; heartbeat expiry also removes dispatch
eligibility. Neither event kills the child nor declares all its sessions dead.
Reconnect uses bounded backoff. An unreachable home emits a bounded diagnostic and
never launches autonomous work.

Each accepted connection gets a new epoch. Same-boot reconnect fences the previous
socket; a competing boot cannot replace an unexpired owner. After expiry a new boot
with the same store can register after adoption. Stale tickets, results and events
cannot affect the new epoch. Server restart requires fresh registration.

Do not replay mutating RPCs after disconnect, timeout or caller cancellation. An
unsent command is canceled; a sent command without a result has an unknown outcome.
Preserve the existing generation reservation and block a replacement launch until
that runner's fresh list/get resolves it. Input recovery stays with the real queue's
transcript/snapshot/generation logic, with backend-unavailable deferral uncharged.
A transport acknowledgement is never a delivered message. No retry timer presses
Enter or types a prompt again on its own.

### D-6: explicitly bound clients and owner-scoped inventory

Add an application I/O seam `ISessionRunnerDirectory`: resolve a client for a
runner binding, look up a session owner, and obtain inventory as per-runner
`Available(sessions)` or `Unavailable(reason)` results. The directory always
includes the configured local runner and any persisted remote owner, even offline.
Bound clients implement the existing `ISessionRunnerClient` contract.

Keep a routing facade for session-ID methods used by runtime/queue/rules services;
it reads the persisted binding and never falls back to local on remote failure.
An unknown session ID returns not-found; only a known legacy/null binding means
local. A remote stop captures the persisted accepted generation before sending
kill-generation; it never fetches a later generation to rescue a mismatch.
Factory-created adapters receive a bound client via an additive factory overload
using runner identity. Bind all launch, attach and re-attach sites, not just the
first start. Sessionless capability reads and lists are explicitly target-bound;
the legacy no-target methods retain local-only meaning, never a partial fleet list.

Reconciliation runs each pass on the matching DB partition and a successful owner
inventory. Preserve starting grace and existing generation guards. Recheck the
owner's availability/epoch before an absence write so a disconnect between list and
get cannot become authoritative absence. Failure/readoption/agent-status passes
must obey the same partition, including failed rows considered for readoption.
One unavailable runner must not suppress the local runner's reconciliation.

Other list consumers require a bounded audit: `AgentControlService` resume/stop,
`AgentService`, `AgentSupervisorService`, `SessionHealthService`, `AttentionService`,
`AgentTaskDispatcher`, and `ZombieCensusService`. Display-only merged views retain
per-owner unknown status. Dispatcher/pool/local PID census exclude the pinned
container agent and its rows. Linux PIDs must never be passed to Windows process
inspection or termination. Herdr and SourceLanding stay explicitly local-only.

### D-7: consume events through the existing runtime and transcript persistence

Wrap existing runner events with runner/store/epoch provenance, validate that
provenance against DB ownership before handing them to `AgentSessionRuntime`, and
retain existing generation checks on generation-bearing events. The local event
pump continues independently. Do not introduce a second transcript database or a
host-side transcript tailer for the container.

On connection, subscribe to live runner events before taking the complete inventory
and transcript catch-up. Buffer live frames while catch-up commits; then release
them through existing sequence/UUID deduplication. On buffer overflow, disconnect
and repeat catch-up; do not report Ready on partial recovery. State-changing event
processing must not await a command response on the socket receive loop. Terminal
output may be reconstructed from the existing buffer/snapshot APIs; transcript
receipt must be reconstructed from the actual runner transcript, not screen output.
`SessionRunnerEventHub` currently allocates an unbounded channel per subscriber.
Add a bounded subscription specifically for the phone-home consumer with explicit
overflow notification; limiting only the socket writer would leave that upstream
queue unbounded. Keep existing local subscribers' contract intact in this slice.

### D-8: one exact workspace mapping and a narrow Linux Grok launch projection

Keep `Agent.WorkingDirectory` and `AgentSession.Cwd` as the canonical Windows host
path, preserving workspace validation and host tooling. The pin permits exactly
that root, not arbitrary descendant worktrees. Persist `/work` separately as
`RunnerCwd`. At the final runtime launch funnel, project only that binding to:

- executable `grok` (or the fixed image-owned absolute path), translating only the
  standard registry `grok.exe` definition; reject custom Windows wrappers/profiles;
- cwd `/work`, verified as an existing directory by the Linux runner;
- `GROK_HOME=/state/grok` and `ANTIPHON_API=http://host.docker.internal:17202`;
- PtyHost, Grok transcript format and memory limit zero.

Leave args, model selection, full Grok rules payload, session identity and reserved
`ANTIPHON_*` identities intact. The callback override is a trusted transport
projection after environment composition, not a permitted arbitrary user override.
The container runner also refuses launch payloads outside its one-Grok/cwd/capacity
policy. Validate capacity against adopted sessions as well as new reservations.

Rules files are written by the runner under its Linux state path and acknowledged
through the existing rules queue barrier. Any generated boot/rules pointer must
name a Linux-readable file or the worker-reachable HTTP origin. Do not translate
arbitrary prompt text or mount the host's entire filesystem. The canary uses a
small ordinary queued prompt after rules initialization; it is not a task brief.
Delegated tasks, OnAgent tasks, card-backed starts, Worktree and SourceLanding are
explicit refusals for this pinned agent before reservation/spawn. Native resume,
if invoked, stays on the same runner/store and retains strict `--resume`; absent
Linux history must never be inferred from a Windows filesystem probe or cause an
implicit fresh conversation.

### D-9: port the detached host, keep its existing ownership boundary

Select `Antiphon.PtyHost.exe` on Windows and `Antiphon.PtyHost` on Linux. Preserve
the intermediary/PID handshake. Keep `Win32ProcessSpawner` unchanged for Windows.
On Linux the intermediary starts the apphost with a private detach flag through
`ProcessStartInfo.ArgumentList`; the child creates a new POSIX session with
`setsid`, redirects stdin/stdout/stderr to `/dev/null`, then enters the existing
host/pipe loop. Fail the child on detach/setup failure. Do not fork a running
managed runtime and do not compose shell commands. The intermediary emits only
the actual host PID and exits; no grandchild may hold its redirected pipe open.

Retain launch timeout, canceled-launch cleanup, named-pipe hello and manifest
adoption. Test real Unix named pipes under the image's same user. Shadow copying
must retain apphost execute permission and `libporta_pty.so` with the dependency
closure. Add Linux-specific tests rather than removing every Windows skip.

Container init reaps detached children; container exit may terminate its processes.
Surviving a runner process restart inside the same container is distinct from
surviving container replacement. This plan promises no Windows Job Object custody,
cgroup accounting or live child survival across replacement.

### D-10: a dedicated runner image and opt-in compose service

Add `docker/session-runner-grok/Dockerfile` and `docker-compose.runner-grok.yml` as
an explicit compose companion, not a replacement for `docker-compose.dev.yml`.
Publish runner plus PtyHost for linux-x64, include the measured Grok version 1.0.34,
and pin the distribution input/digest used by the build. Runner entrypoint is
`dotnet Antiphon.SessionRunner.dll`, with compose `init: true`. Confirm both
runtime apphosts and native assets in the final image. Use Linux overrides for log,
manifest, shadow and custody-store roots; do not inherit `C:\logs\...` in Linux.

Mount the exact host workspace at `/work` read-only for this trivial canary;
mount persistent state including `GROK_HOME` and runner store identity at `/state`;
mount the registration secret separately read-only. Use one consistent container
UID and validate mount access without reading credentials into logs. Provider auth
is the already authorized mounted OAuth store. A missing/unusable auth store is a
sign-in refusal, not a fallback to metered API-key auth.

No Docker socket is required to run this session. The prior host-socket choice
remains the decision for future nested Docker work, which this slice does not
exercise. No `docker exec` launch lane, DinD, live broker, published runner port,
AppHost migration, prune service or automated container/image replacement.

### D-11: keep scope failures visible

Connection/auth/version/capacity/unsupported-target failures use bounded typed
codes and existing Problem Details mapping. Expose non-secret registration state,
runner/store/build identity, last heartbeat and availability through a read-only
runner status endpoint. Include runner binding in existing session diagnostics;
this needs no new UI screen. Home outage never quietly selects Windows.

Do not alter CARD-0038's backend enum/ceiling policy here. For a phone-home Grok
session, `SessionDeliveryProfile` selects the conservative existing profile before
the Grok join-safe/spill transform; the local `PtyDeliveryProfile` still probes only
the local runner. Thus Linux's current misleading Inbox label cannot downgrade
unrelated Windows sessions or borrow their ModernConPty evidence.

## Implementation slices

Commit/push each slice before a long verification run. Names marked **new** are
proposed files. TestDesign may refine test methods after reading their nearest
fixtures; architectural scope and acceptance do not expand with the matrix.

### S1: native Linux launch seam

Files: `src/Antiphon.PtyHost.Client/PtyHostLauncher.cs`, `ShadowCopyStore.cs`;
`src/Antiphon.PtyHost/Program.cs`, **new** `PosixProcessSpawner.cs`;
`src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj` only if explicit publish
closure inclusion is needed. Leave the Windows spawner behavior intact.

Tests: extend `tests/Antiphon.PtyHost.Tests/ShadowCopyStoreTests.cs`; add
**new** `LinuxPtyHostLauncherTests.cs` in that project. Launch a real Linux host,
connect its pipe, start a benign child, exchange PTY bytes, observe child exit,
and verify intermediary exit and cleanup. Retain existing `PtyHostLauncherTests`,
`HostSessionPipeTests` and `ParentDeathSpikeTests` on Windows. Gate: an actual
non-skipped Linux method, not just a publish or an all-skipped suite.

### S2: phone-home connection and bounded runtime RPC

Files: **new** `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`;
**new** runner `PhoneHomeSettings.cs`, `PhoneHomeConnectionService.cs`,
`PhoneHomeCommandDispatcher.cs`; runner `Program.cs`, capability construction
extracted from its existing route if necessary. **New** server
`Application/Settings/PhoneHomeRunnerSettings.cs`,
`Application/Interfaces/ISessionRunnerDirectory.cs`,
`Api/Endpoints/SessionRunnerEndpoints.cs`, and infrastructure
`Agents/SessionRunner/PhoneHomeRunnerDirectory.cs`, `PhoneHomeRunnerClient.cs`,
`RunnerContractMapper.cs`. Wire DI/endpoints in `server/Program.cs`; share codecs
and validation with `SessionRunnerHttpClient.cs` without changing the local wire.

Tests: **new** `tests/Antiphon.Tests/Agents/PhoneHomeConnectionTests.cs` with an
isolated in-process server/socket peer; **new**
`tests/Antiphon.SessionRunner.Tests/PhoneHomeCommandDispatcherTests.cs` and
`PhoneHomeConnectionServiceTests.cs`. Cover register/ready/connect/heartbeat,
two-way request correlation, unsupported operations, bounded disconnect behavior,
duplicate boot/epoch and no automatic mutating-command replay. Use fake time for
15/90-second lease tests. No fixture points at production 17204.

### S3: persisted binding and one standing launch

Files: `server/Domain/Entities/AgentSession.cs`,
`server/Infrastructure/Data/AppDbContext.cs`, CLI-generated
`server/Migrations/*_AddSessionRunnerBinding.cs` and designer, and
`server/Migrations/AppDbContextModelSnapshot.cs`; **new**
`Application/Services/PhoneHomeLaunchPolicy.cs`; `AgentControlService.cs`,
`AgentSessionLaunchComposer.cs`, `AgentSessionLaunchQueue.cs` as needed to carry
the committed binding, `AgentSessionService.cs`,
`Application/Interfaces/IAgentProtocolAdapterFactory.cs`,
`Infrastructure/Agents/Pty/AgentProtocolAdapterFactory.cs`; **new**
`Infrastructure/Agents/SessionRunner/RoutingSessionRunnerClient.cs`.
`AgentTaskDispatcher.cs`/task admission and card start must refuse the pinned agent
without creating a worktree. Generate, do not hand-author, the migration.

Tests: **new** `tests/Antiphon.Tests/Application/PhoneHomeStandingLaunchTests.cs`
and `PhoneHomeSessionRoutingTests.cs`; extend
`AgentSessionLaunchQueueOwnershipTests.cs`, `AgentSessionLaunchFailureTests.cs`,
`AgentControlServiceIntegrationTests.cs` and
`tests/Antiphon.Tests/Agents/AgentLaunchSpecTests.cs` for local
compatibility. Pin reservation rollback, late queue work, restart re-resolution,
mapping, refusal paths and remote failure with zero calls to the local fake client.

### S4: per-owner recovery, events and delivery

Files: `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs` (the event hub and bounded
phone-home subscription), `server/Application/Services/SessionReconciliationService.cs`,
`Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs`,
`Application/Services/AgentSessionRuntime.cs`, `SessionDeliveryProfile.cs`;
the list consumers named in D-6, plus session-specific
`DiagnosticsBundleService.cs` and `GrokRulesRefreshService.cs` where targetless
capability calls need binding. Prefer routing at the shared seam to editing every
session-ID caller. Local Herdr/custody operations retain their existing clients.

Tests: **new** `PhoneHomeReconciliationTests.cs`, `PhoneHomeEventPumpTests.cs`,
`PhoneHomeQueuedTurnTests.cs` under `tests/Antiphon.Tests/Application`;
extend `SessionReconciliationServiceTests.cs`, `SessionRunnerEventPumpTests.cs`,
`SessionRunnerGenerationWireTests.cs`, `SessionRunnerCapabilityGateTests.cs` and
the existing queue/rules integration classes selected by TestDesign.
Also cover affected list consumers in `AgentServiceIntegrationTests.cs`,
`AttentionServiceTests.cs`, `SessionHealthTests.cs`, `SessionDeliveryProfileTests.cs`
and `tests/Antiphon.Tests/Infrastructure/ZombieCensusServiceTests.cs` as applicable.
Use two runner peers with different ownership, overlapping process IDs and one
unavailable peer. Exercise a real message queue/rules barrier, busy-to-idle
recipient, reconnect catch-up and transcript persistence. A fake runner proves
server routing/receipt behavior; it does not prove Linux Grok or POSIX PTY delivery.

### S5: image, fixture and one real turn

Files: image/compose from D-10; **new**
`scripts/verify-phone-home-grok.ps1` as an opt-in foreground acceptance harness;
**new** Linux test fixture in `tests/Antiphon.SessionRunner.Tests` with isolated
state/workspace and no production runner dependency. Update `docs/agent-kinds.md`,
`docs/session-runtime-invariants.md`, `docs/testing-and-build.md` and
`docs/bootstrap.md` with only the new supported lane and run commands.
If native path evidence requires it, narrowly adjust `GrokTranscriptTailer.cs` and
add a sanitized linux-1.0.34 fixture to `GrokTranscriptTailerTests.cs`; do not import
unrelated CARD-0038 portability repairs.

The harness records source/image/Grok version, runner/store/boot/epoch identity,
non-secret status, session ID/generation and queue/transcript correlation. It uses
isolated server/database/runner fixtures for ordinary automated verification;
production activation, if later commissioned, follows the canonical main-checkout
restart and `/api/version` contract after ordinary Review and land. No restart or
deployment is part of this Plan task.

## Guard and positive-control seeds for TestDesign

These are the bounded safety obligations to refine into exact methods and compiling
mutations. Every independently bypassable guard must be inventoried by TestDesign;
the seed table is not a substitute for its mandatory one-to-one G/PC mapping.
Avoid importing the superseded CARD-0575 exec-wrapper provenance matrix.

| Seed | Guard / decisive observable | Positive-control defect to exercise |
|---|---|---|
| G-1 / PC-1 | Only the configured authenticated runner may register/connect. | Bypass authentication; an invalid credential is accepted and the endpoint test fails. |
| G-2 / PC-2 | Registration becomes usable only after adoption/readiness and complete catch-up. | Publish Ready before the barrier; a held startup/recovery fixture observes premature dispatch. |
| G-3 / PC-3 | Expired/disconnected targets reject new dispatch; outage is not process death. | Treat stale registration as eligible; fake time proves a launch reaches an offline target. |
| G-4 / PC-4 | Replies/events from a replaced epoch cannot complete current work. | Remove epoch check; old peer completes the new waiter or feeds its runtime. |
| G-5 / PC-5 | Sent mutating work is never replayed by reconnect. | Requeue an unanswered input; the peer receives two writes before queue recovery authorizes anything. |
| G-6 / PC-6 | Persisted runner/store routes a conversation after restart; no local fallback. | Use current config/default client for a bound session; local fake records forbidden I/O. |
| G-7 / PC-7 | Only authoritative owner inventory may close/readopt that owner's session. | Flatten unavailable to empty or remove owner predicate; a second runner's row changes. |
| G-8 / PC-8 | Scope/capacity gate permits one cardless Grok/PtyHost session only. | Bypass the admission predicate; unsupported task/kind/backend or a second launch creates a process. Split independent variants in TestDesign. |
| G-9 / PC-9 | Exact host root maps to runner cwd; other paths are refused. | Use a prefix match or forward Windows cwd; a sibling/worktree escapes the mapping or Linux receives the wrong cwd. |
| G-10 / PC-10 | Generation echo and conditional kill still protect the selected owner's generation. | Drop expected generation in the new client; a stale kill affects the replacement session. |
| G-11 / PC-11 | Linux apphost/native assets and execute permissions survive shadow copying. | Omit the extensionless apphost from the closure; the native launch assertion fails after a successful build. |
| G-12 / PC-12 | Linux host detaches and releases inherited intermediary pipes. | Skip the detach/stdio setup; bounded parent-exit/pipe serviceability assertion fails. Split independently load-bearing steps in TestDesign. |
| G-13 / PC-13 | Delivery requires the complete matching transcript UserPrompt past the floor. | Accept command result or screen text as receipt; a peer with no matching UserPrompt incorrectly becomes Delivered. |
| G-14 / PC-14 | Reconnect backfills transcript before live tail and deduplicates repeats. | Skip catch-up or release live events first; the persisted turn has a gap or incorrect order. |
| G-15 / PC-15 | Container capabilities/ceilings never borrow local modern/custody evidence. | Resolve the local profile/client for the container; target capability/ceiling assertions detect the cross-target decision. |

Additional ordinary checks: wrong protocol version, missing mount/auth, home
unreachable, typed remote errors, cancellation before/after send, secret redaction,
local HTTP/SSE regressions, rules receipt path and initialization, native strict
resume refusal, output limits and event-buffer recovery. TestDesign must distinguish
runtime safety guards from configuration/diagnostic checks and price each actual PC.

## Acceptance and remaining measurements

Run the acceptance through the ordinary standing start and queue APIs, not by
launching Grok directly from the harness:

1. Start the container without a published runner port. Observe its authenticated
   registration and outbound socket at the server, build identity, Grok-only policy,
   completed readiness and heartbeat. Confirm local Windows target remains separate.
2. Start the explicitly pinned standing agent. Read the DB/API owner and accepted
   generation, runner session PID/host PID, and the Linux-side cwd/auth-home/rules
   receipt paths. Verify Linux transcript bind against the actual `updates.jsonl`.
3. Wait for any normal Grok rules initialization receipt. Enqueue a unique short
   prompt through the existing queue. Require the complete corresponding UserPrompt
   beyond its pre-send transcript floor, queue Delivered, assistant reply containing
   the requested nonce and a TurnEnd. Capture IDs/sequences, not credentials.
4. With that row still owned by the container, verify one local-target control
   operation or isolated local fixture and owner-scoped reconciliation leave it
   intact. Verify home/channel outage using deterministic fixtures; a live outage
   exercise is not permission to interrupt the shared stack.
5. Stop only the canary session through its owner/generation and stop the harness's
   container. Retain the persistent auth/state mount. Remove only task-owned test
   outputs; do not prune other containers/images or delete volumes. This is test
   cleanup, not the deferred container lifecycle feature.

The unresolved measurements are Unix pipe behavior under the chosen image user,
POSIX Grok composer/submit behavior, exact Linux transcript path encoding, and the
real authenticated queued turn. S1 and S5 supply those measurements. If one fails,
record the specific boundary and smallest reproduction; do not weaken receipt
assertions, widen timeouts or substitute a headless PONG. Authentication/provider
unavailability leaves real-turn acceptance outstanding even if all fixtures pass.

## Verification handoff and boundaries

TestDesign must read the nearest fixtures, append `## Verification design` with
delivery producer/destination/persistence/recovery/receipt inventory, exact V/R
methods, full guard/PC mapping, and separate numeric Code/Mutation cost floors.
The normal Code profile is Unit plus named affected integrations and the mandatory
non-skipped Linux/native and real-turn checks; no client/browser suite is needed
for a feature without UI changes. Preserve Windows PtyHost regression coverage.

Use TUnit via `dotnet run --project tests/<Project>`, isolated
`--property:OutputPath=bin-card0490/` (forward slash), fresh TRX directories, and
nonzero executed method counts. Respect each assembly's process limiter; do not
co-schedule Antiphon.Tests and Antiphon.Agents.Pty.Tests. Build/commit before long
runs; freeze source until completion; remove only verified task-owned alternate
outputs. No daemon restart or production 17204 fixture. Ordinary Review precedes
land; method-scoped PCs run later from the commissioned SourceLanding snapshot,
with restoration evidence outside it and no snapshot commit/push.

Explicitly excluded: docker-exec wrapper, all-provider Linux support, full
CARD-0038 subsystem/skip audit, in-container worktrees, SourceLanding/custody,
Herdr/remote control, remote/server2 deployment, fleet scheduling, automatic spend
recovery, image pruning and container lifecycle automation. Failure to obtain the
one real transcript-confirmed turn is not grounds to claim this slice complete.

Plan-only validation: source/owner inspection and document checks. No build,
native experiment, live container change or product test was run by this task.
The requested checkout of `feat/card-task-da82967d` was refused because it is
already checked out in its investigation worktree; this task's own branch uses
the exact requested `b7f8903f` base instead.

Next stage: **test-design**. Append verification to this artifact; do not implement
or broaden the feature before that stage completes.

## Verification design

TestDesign task `59550e71`, 2026-09-20, inspected at `a28fa3b9`. Everything above
this heading is preserved. The requested Plan branch is occupied by its original
worktree; this amendment uses `feat/card-task-59550e71` at the exact requested base.
The methods below are implementation requirements, not existing or passing tests.

**Disposition: return to Plan for the native mutation execution seam described
below. Do not dispatch Code from this amendment yet.** The product scope remains
S1-S5: one pinned, cardless Grok conversation, Linux desktop container, one ordinary
queued turn. The 15 seeds expand to 57 individually mapped boundary controls;
none imports the CARD-0575 exec-wrapper matrix or adds a supported product lane.
Separate endpoint gates, transport peers, event versus response gates, native
setup operations and durable recovery handoffs cannot share a positive control.

### Inspection

Paths in this table are relative to the repository. A named method or range of
helpers means those bodies were read; it does not claim the entire large class
was read. Unchanged compatibility classes need no new case just to count coverage.

| Test/fixture bodies read | Boundaries -> verification |
|---|---|
| `tests/Antiphon.PtyHost.Tests/ShadowCopyStoreTests.cs`, `PtyHostLauncherTests.cs`, `HostSessionPipeTests.cs`, `ParentDeathSpikeTests.cs`; `HostHarness.cs`, `PipeTestClient.cs` | Closure, real launch, cancellation, intermediary pipe EOF, parent death, manifest and child exit -> V-1, R-7; Windows skips and `cmd.exe` helpers cannot be reused as Linux execution evidence. |
| `tests/Antiphon.SessionRunner.Tests/LocalHttpRunner.cs`, `RunnerStartupReadinessTests.cs`, `RunnerCapabilitiesTests.cs`, `GrokRulesFileLaunchTests.cs` | Random-port child, adoption barrier, capabilities, pre-spawn rules file and refused payload -> V-2, V-3, V-9. `LocalHttpRunner` hard-codes `.exe` and `modern`; new Linux fixture must replace those assumptions. |
| `tests/Antiphon.Tests/Agents/SessionRunnerGenerationWireTests.cs`, `SessionRunnerCapabilityGateTests.cs`, `AgentLaunchSpecTests.cs` | HTTP codecs, echo, conditional input, kill-generation, local defaults -> V-3, R-5, R-10. |
| `tests/Antiphon.Tests/Application/AgentSessionLaunchQueueOwnershipTests.cs` including `OwnershipFixture`; `AgentSessionLaunchFailureTests.cs` three standing Grok resume methods and `LaunchFixture.CreateCoreAsync`/`LaunchInteractiveAsync`; `AgentControlServiceIntegrationTests.Legacy_only_provider_starts_unprofiled_agent_through_configured_registry` and `BuildHarness` | Reservation generation, worker ownership, fresh DbContext after seeding, strict resume and local launch -> V-4, V-5, R-3. |
| `SessionReconciliationServiceTests` initial absence/exit cases, `BuildService`, runner factories; `SessionRunnerEventPumpTests.cs` including held event-bus/queue probe and stale typed exit | Authoritative absence versus unreachable, independent owner partitions, pump blockage, reconnect -> V-6, V-7, R-4, R-6. The S0 probe currently asserts that a blocked inline local pump blocks persistence; do not silently invert that unrelated local test. |
| `SessionMessageQueueGrokPtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path`, `Multiline_delivery_is_transcript_confirmed_through_the_real_grok_tailer`, `PumpRunnerTranscriptAsync`, `WaitForRawAsync`; `GrokRulesQueueBarrierTests.cs` | Screen-only versus transcript verdict; real queue, busy/idle, rules barrier -> V-8, V-9, R-8. The old pump inserts DB rows itself and is not the new phone-home event-persistence oracle. |
| `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs`, `QueuedReceiptAssertions.cs`, `SessionQueueTranscriptPump.cs`, `TestDbFixture.cs`, `AntiphonWebAppFactory.cs`, `ProductionRunnerGuard.cs`, `DelegationTestServices.cs` | Isolated cloned database, real queue dependencies, process safety, transcript substitution limits -> every server integration; receipt helpers' fake `OnSubmitted` DB insert is deliberately not used for phone-home acceptance. |
| `CodexStartupDeliveryWorker` settings, `RunAsync`, `CrashAsync`, child provider and save interceptor; `PostLandMutationDeliveryWorker.cs` | Inherited child crash cuts, parent-owned DB, retained peer and awaited stdout/stderr -> V-8's new crash worker. The existing worker's pipe RPC is not a substitute for the new WebSocket. |
| `SessionDeliveryProfileTests.cs`; `ZombieCensusServiceTests.Service_runner_or_census_failure_is_thrown_and_kill_is_never_called`, `Service_db_projection_failure_is_thrown_with_no_mutation`, `Isolated_schema_pool_expiry_is_a_candidate_and_does_not_call_kill`, `World`, census/runner fakes | Target-specific ceilings, unknown owner, overlapping PID numbers and local process exclusion -> V-10, R-9. |
| `GrokTranscriptTailerTests` real turn constants and `Real_turn_rows_normalize_to_UserPrompt_ToolCall_coalesced_text_and_TurnEnd`, path-resolution/location cases, temp/append/poll/cleanup helpers | Actual tailer, deterministic path, full prompt, assistant/TurnEnd correlation -> V-11. Existing captured shapes are 1.0.5/1.0.13, not measured Linux 1.0.34. |

Owner/source inspection additionally covered the project constitution; testing/build
fast lane, clocks, isolation, PC and delivery contracts; orchestration SourceLanding
contract; session generation, queue, standing continuity and Grok rules invariants;
ADR 0002; Grok launch/auth/transcript owner section; launcher, shadow closure,
PtyHost entrypoint, reconciliation absence/resume paths and custody capability.

**In-scope unverifiable seam, F-1.** D-9 needs native Linux PCs 26-32. The current
SourceLanding contract in `docs/orchestration-loop.md` requires local inherited
execution and forbids snapshot access by an external executor, broker, remote
service or pre-existing process. `SessionRunnerRuntime.VerificationCustodyBackend`
returns `windows-job-v1` only on Windows ModernConPty, otherwise null;
`AgentTaskService` SourceLanding admission calls `RequireSupportAsync`. D-8/D-10
also explicitly refuse SourceLanding on the pinned container. Thus neither
commissioning the Mutation delegate on this Linux runner nor handing its managed
snapshot to the desktop Docker daemon supplies a permitted native PC lane.
An ordinary Code-stage Linux test is allowed but does not resolve the later PC
execution contract. A publish, text inspection or all-skipped Windows invocation
cannot turn the seven native controls green.

Plan must specify a compliant way to exercise the native guards after land, or
explicitly commission a different native verification contract through its owner.
A narrowly testable process-I/O seam may cover decisions portably, but must state
which native consequences still need measured execution. This amendment does not
authorize a custody exception, Linux custody implementation, external executor or
Docker access to a SourceLanding snapshot. This is a verification prerequisite for
the present slice, not a reason to add remote hosts or cgroups to CARD-0490.

**Missing setup assigned to Code after F-1 is resolved.** Add a shared
`PhoneHomeTestHost`/scripted peer under `tests/Antiphon.Tests/TestHelpers`, a
`PhoneHomeDeliveryWorker` using the inspected inherited-child pattern, and the
Linux fixture already proposed by S5. Use an isolated cloned database per world;
thread its connection through every DbContext, interceptor and recovery process.
Use the real endpoint, directory/client, adapter factory, runtime persistence,
queue, rules service and phone-home connection service. Replace only the remote
runtime's process boundary for deterministic server tests. Disable provisioners,
Hangfire, live messaging and OS census as in `AntiphonWebAppFactory`; keep a
recording/refusing local runner. Enable only the pump under test. Use random ports,
never 17204. Per-assembly process limiters apply to every new spawning class.

Provide deterministic gates at adoption completion, inventory return, transcript
commit, before/after socket send, after peer execution and before result, and queue
save/wakeup and launch-queue admission. Hold the launch worker before its first
action so an early enqueue is observable even if its DB read would find no row.
Use interceptors and owned peer/socket gates; no sleeps as ordering
proof. Use fake time only for directory lease/ticket tests. Queue integration uses
real time or an offset clock, not a frozen whole-server TimeProvider.

The Linux fixture uses the same UID, native apphost/Porta assets and runtime image
as the service; starts benign `/bin/cat` and a fixed test executable through
ArgumentList; records host/intermediary PIDs and `/proc` session/fd facts; owns and
awaits every child. Assert Unix pipe serviceability and no owned live residue.
Do not change unrelated Windows skips. Missing image/native runtime is a failed
required setup, not a skipped acceptance.

### Delivery inventory

Durable conversation identity is `(AgentSession.Id, RunnerId, RunnerStoreId,
accepted StartedAt)`; the generation is PostgreSQL-microsecond normalized. Queue
identity adds `SessionQueuedMessage.Id`, its immutable intended body and
`LastDeliveryGeneration`/`LastDeliveryBaselineSequence`/attempt timestamp. Wire
`(epoch, requestId)` is correlation only, never durable delivery identity. Transcript
receipt adds the actual native session identity, event UUID and persisted sequence.
Rules use session, rules generation, SHA256 and byte count, plus the queued read's
own complete UserPrompt and validated ACK. Do not invent a second outbox here.

| Path: producer -> destination | Persistence boundary and recovery | Observable receipt / test |
|---|---|---|
| Runner startup -> registration/connect -> server directory | Mounted RunnerStoreId survives process restart; ticket/lease/epoch are transient. Crash after register but before connect retries registration; consumed/expired tickets are not reused. Server restart requires fresh registration, adoption and catch-up. | Ready only after full recovery, then a successful target-bound get. This is availability, not session-input delivery. V-2; PCs 1-8, 49-52. |
| Standing Start -> DB reservation -> launch queue -> WebSocket launch -> runner manifest/host | Owner triple and accepted generation commit before enqueue. Rollback sends nothing. Enqueue failure or crash before send leaves an owned, visible reservation; reconcile a fresh owner inventory before an explicit retry. After execution/lost result do not replay or reserve a replacement until list/get resolves the old generation. Existing authoritative absence can visibly fail the reservation; automatic re-launch is not required. | Matching owner/generation Running inventory plus actual native pipe readiness; the next queued turn supplies recipient evidence. V-4/V-5/V-11; PCs 10-13, 20-24. |
| Full composed rules -> launch payload -> runner rules file -> queue initialization read -> Grok | Runner persists full file/receipt before launch. Server persists expected metadata and initialization state; crash before/after receipt import or queue wakeup recovers from those records and native transcript. Wrong owner/generation/hash cannot open the barrier. | Complete rules-read UserPrompt and validated matching ACK before ordinary work is typed; file receipt alone is insufficient. V-9/V-11; PCs 21, 35. |
| Ordinary queue producer -> durable queue row -> flush -> adapter/routing client -> socket -> Grok composer/submit | Before queue commit failure means no accepted obligation/no input; an explicit retry creates the one successful row. After commit, loss of wakeup is found by existing queue recovery. Sent/unknown attempts retain their floor/generation; disconnect is uncharged and transport never resends writes. Only the real queue may authorize later composer recovery. | Complete matching UserPrompt beyond the attempt floor in the destination's transcript and queue `DeliveryVerdict.Delivered`. `Status=Sent`, 202, RPC success and visible nonce alone fail acceptance. V-8; PCs 9, 25, 33-35, 46. |
| Grok updates.jsonl -> tailer -> bounded event subscription -> socket -> server runtime -> TranscriptEntries | Native transcript/runner transcript remains the recovery source. Subscribe before inventory/catch-up; commit catch-up before releasing buffered live frames. Crash before/after event enqueue, overflow or failed DB commit re-catches up and deduplicates; never acknowledge a partial history as Ready. | Exact ordered persisted UUIDs and complete prompt; assistant nonce and matching TurnEnd for the real turn. V-7/V-8/V-11; PCs 4, 8, 33-34, 36-37, 39-40, 42-44, 51-52. |
| Runtime transcript -> queue confirmation/save | Crash after transcript commit but before verdict save must late-confirm the same row on recovery, with no body/Enter replay. A failed verdict save is retried from transcript evidence, not transport success. | Same queue ID Delivered, exactly one matching native UserPrompt, no duplicate submission. V-8; PCs 9, 33-34, 37, 46. |
| Resize/clear/conditional-input/stop -> owner-bound RPC -> runtime -> result/events | Reads may be retried; mutations are not transport-replayed. Lost result is unknown. Stop captures the persisted generation and uses kill-generation; fresh get cannot upgrade that token. | Target state/generation from fresh inventory and unchanged replacement where mismatched. Stop is not input receipt. V-3/V-5; PCs 9, 23-25, 45. |

`PhoneHomeQueuedTurnTests.Queued_turn_survives_every_delivery_handoff` is a real
producer-to-recipient test, parameterized with `busy=false,true` and the following
13 cut values (26 executions). Start one pinned standing session through the
ordinary start service/API; complete its real rules barrier through the peer;
enqueue using `SessionMessageQueueService.EnqueueAsync(WhenIdle)`. The peer emits
only what its recorded input actually submitted, through the real socket and
runtime persistence. Never insert the confirming UserPrompt directly in SQL.

| Cut | Injected failure and required recovery |
|---|---|
| `queue-save-fails` | Throw on the queue insert; no committed row/input/receipt; remove failure and explicitly enqueue once, then obtain receipt. |
| `queue-committed` | Kill the owned server worker after row commit, before wakeup; recreate it against the same DB, recover and receive once. |
| `attempt-committed` | Crash after Sent/floor/generation commit, before wire write; queue recovery eventually submits once, no invented receipt. |
| `socket-enqueue-fails` | Fail write admission before send; durable work remains recoverable and attempts remain uncharged for backend unavailability. |
| `input-executed` | Peer records submit and appends its native transcript, then drops socket before result; recover by transcript, one submit. |
| `result-only` | Return success/redraw, withhold transcript; no Delivered. Release genuine transcript and finish once. |
| `tailer-before-enqueue` | Retain native transcript, fail event-hub publication/disconnect; reconnect catch-up obtains it. |
| `event-enqueued` | Abort connection after event admission, before socket send; catch-up supplies missing entries. |
| `event-received` | Crash server after frame receipt, before DB commit; replay from runner history. |
| `transcript-save-fails` | Throw before transcript save; no confirming DB row; recovery persists complete batch once. |
| `transcript-committed` | Crash before queue verdict save; late-confirm without input or Enter. |
| `verdict-save-fails` | Throw on Delivered save after transcript commit; recreated queue confirms same ID without another submission. |
| `overflow` | Hold catch-up, overflow the phone-home event path; no Ready on partial history; reconnect and persist the whole turn exactly once. |

For busy variants first persist real peer AssistantText activity, enqueue and run
the flush: zero input and zero matching prompts. Release the peer's TurnEnd, run
the same recovery entry point, then take the cut at the specified handoff. The
already eligible variants must deliver without needing a later unrelated event.
After every durable cut, finish all the way to recipient transcript and Delivered;
checking only the recovered row is a failed test design. For enqueue failure before
durability, the explicit successful retry is the receipt-bearing request, not a
claim that the failed request was accepted. Use a stopped/killed inherited server
child for the three commit-crash cuts and event-received cut; service recreation
alone may supplement, but may not replace those process-crash executions.

Rules have the smaller `PhoneHomeQueuedTurnTests.Rules_recovery_preserves_the_barrier`
matrix: `file-written`, `receipt-import-fails`, `read-enqueued-before-wakeup`,
`read-submitted-before-ack`, `ack-received-before-save`. Each ends with the real
rules ACK and a separately queued ordinary prompt receipt. Reconnect alone must
not retype a transcript-confirmed rules read while its ACK is pending.

Substitutes: a scripted runtime proves transport, routing, persistence, queue
recovery and matcher behavior, not POSIX PTY/Grok behavior. A real tailer over a
sanitized fixture proves normalization, not native auth or submission. A fake
clock proves lease boundaries, not network liveness. A native benign child proves
detachment/pipes, not Grok receipt. Only V-11 proves the actual authenticated Linux
Grok queued turn. Review must reject evidence stopping before the recipient.

### Proves it works now

Class shorthand below is only for compact tables: `C=PhoneHomeConnectionTests`
(`tests/Antiphon.Tests/Agents`), `S=PhoneHomeStandingLaunchTests`,
`T=PhoneHomeSessionRoutingTests`, `R=PhoneHomeReconciliationTests`,
`E=PhoneHomeEventPumpTests`, `Q=PhoneHomeQueuedTurnTests` (all five under
`tests/Antiphon.Tests/Application`); `D=PhoneHomeCommandDispatcherTests`,
`H=PhoneHomeConnectionServiceTests`, `L=LinuxPhoneHomeRunnerTests` (all under
`tests/Antiphon.SessionRunner.Tests`); `N=LinuxPtyHostLauncherTests` and
`X=ShadowCopyStoreTests` (`tests/Antiphon.PtyHost.Tests`). Full method names in the
PC table are also ordinary regression tests; Code implements and runs every one.

| ID | Behaviour / layer | Exact test or command | Expected |
|---|---|---|---|
| V-1 | Native detached Linux host / process | `N.Native_host_exchanges_bytes_and_reaps`; all N methods in PCs 29-32; X methods in PCs 26-28 | Non-skipped Linux execution; apphost from shadow, hello/session generation, `/bin/cat` nonce round trip, child exit, manifest cleanup, no inherited stdio pipe preventing intermediary EOF. |
| V-2 | Connection lifecycle / endpoint + runner service | C and H methods in PCs 1-7, 49-50; `C.Registration_and_status_are_typed_and_versioned`; `H.Home_outage_reconnects_without_autonomous_launch` | Actual runner-originated register/socket; unknown protocol refused; heartbeat at 15s; 89.999s eligible, exactly 90s unavailable; disconnect immediate; restart/store continuity; typed status without secrets. |
| V-3 | Bounded RPC / socket + dispatcher | `D.Supported_operations_preserve_existing_contracts`; PCs 18, 23-25, 39-45 | Every D-3 allowed enum round trips existing DTO/error code; unknown enum, Herdr, kill-all and custody rejected before runtime effects. Launch held while heartbeat/read/result receipt progresses; writes in order. |
| V-4 | Persisted standing launch / PostgreSQL + ordinary start | S methods in PCs 11-12, 16-17, 20-21; `S.Migration_preserves_legacy_rows_and_owner_binding`; `S.Accepted_launch_reaches_its_persisted_owner` | CLI-generated migration up from previous migration with a legacy row; all-null local and all-present remote survive fresh context. Accepted row is committed before first socket launch; local fake receives none. |
| V-5 | Restart/routing/unknown outcome / integration | T methods in PCs 10, 13, 22-25; `T.All_session_operations_use_persisted_owner` | Cover launch, attach, reattach, get, transcript/snapshot/buffer, input, clear, resize and stop; pin changed/disabled/missing cannot move session. Unknown ID is not-found; legacy-null still local. Restart never replaces an unresolved generation. |
| V-6 | Owner-scoped reconciliation / integration | R methods in PCs 14-15, 47, 53; `R.Local_partition_progresses_while_remote_is_unavailable`; `R.List_consumers_preserve_remote_unknown` | Owner unavailable leaves its Running/Starting/Failed rows and agent state alone; local authoritative absent/exit still progresses. Starting grace at just-before/exact expiry and failed-row readoption keep generation guards. |
| V-7 | Events/catch-up / real socket + persistence | E methods in PCs 4, 8, 36-37, 43-44, 51-52 | Subscription established first; held catch-up admits live events but cannot expose Ready; UUID set and order exact after disconnect/server restart/overflow; wrong owner/epoch/generation changes nothing. |
| V-8 | Ordinary queued prompt / real queue + crash worker | `Q.Queued_turn_survives_every_delivery_handoff` (26 cases); Q methods in PCs 33-34, 46 | Matching complete UserPrompt beyond floor and Delivered for each successful obligation; busy recipient held; no transport replay; attempts unchanged during unavailability; duplicate native submit count zero. |
| V-9 | Rules delivery / real rules service + queue | `Q.Rules_recovery_preserves_the_barrier` (5 cases); `Q.Ordinary_input_waits_for_matching_rules_ack` | Correct Linux-readable file or worker-reachable pointer; ordinary body waits for matching session/generation/hash ACK, then reaches recipient. ACK/text from another owner or old rules generation cannot unblock. |
| V-10 | Target isolation / profile and list consumers | `T.Remote_profile_never_borrows_local_capabilities`; `R.Remote_pids_never_enter_local_process_census`; `R.List_consumers_preserve_remote_unknown` | Remote Grok conservative ceiling before join/spill, local ModernConPty unchanged, no Windows custody advertised; overlapping numeric PIDs cannot inspect/kill a Windows process on behalf of Linux. |
| V-11 | One real queued turn / native acceptance | New `scripts/verify-phone-home-grok.ps1 -ProfilePath .antiphon/card0490-live.json -EvidenceRoot .antiphon/card0490-live-evidence`; `L.Runner_restart_adopts_same_store_and_generation`; `L.Native_grok_path_matches_tailer` | Foreground isolated host/container, Grok 1.0.34 OAuth, no published runner port. Full complete UserPrompt, assistant nonce and TurnEnd from actual updates.jsonl persisted through phone-home; queue Delivered; owner/generation stop and cleanup. Real-turn failure remains outstanding. |
| V-12 | Local compatibility / Unit + named integrations | Commands below, including existing generation/capability wire tests, launch ownership, rules barrier, profile, reconciliation/event-pump and Windows PtyHost tests | No changed HTTP/SSE contract, global BaseUrl, Windows executable or backend ceiling. No broad client/browser run. |

V-6's consumer method uses table data for the D-6 callers: AgentControl resume/stop,
AgentService, AgentSupervisorService, SessionHealthService, AttentionService,
AgentTaskDispatcher and ZombieCensusService, plus diagnostics/rules capability
lookups. Drive their actual public methods with the shared isolated world and
directory; no source-text assertions. Pinned agent cannot enter pool/dispatcher;
display reads preserve unknown. This single-session audit is not fleet scheduling.

V-3 limit cases measure **serialized complete JSON UTF-8 bytes**, not string length:
configured `M-1/M/M+1` for both peers, fragmented across a multibyte scalar and many
small frames. At `M` succeeds, at `M+1` closes/refuses with a bounded typed reason,
no partial transcript and no unbounded retry. Test positive settings validation at
0/-1 as configuration checks. At default M=16,777,216, serialize the actual default
262,144-byte full rules payload and 262,144-char runner replay snapshot (and server
524,288-char replay setting) using worst JSON-escaped content; each must fit or
fail visibly before send. Also test a deliberately smaller configured M, and an
oversized full transcript: explicit unsupported read, never truncated success or
an endless reconnect loop. In-flight 31/32/33; event count 1023/1024/1025; pending
bytes 16MiB-1/16MiB/16MiB+1. Test count overflow while bytes are below their cap and
byte overflow while count is below its cap. Upstream hub and server catch-up buffer
are exercised independently; local event subscribers retain their old behavior.

The live profile is untracked configuration, containing isolated server/database
settings, exact host workspace, state/auth mount path, secret-file path, image tag
and pinned standing-agent ID; it contains no credential value. The harness creates
its own disposable server/database and compose project with callback origin using
that isolated server's actual port, overriding the deployment's 17202 example.
It must not target the shared stack. Missing profile/auth is an explicit setup
failure. It must verify image digest/Grok version, mount access/UID, runner/store/
boot/epoch, no exposed runner port and no Docker socket. It starts via the normal
standing API, waits for rules, captures the pre-send floor and queues exactly one
small ordinary nonce prompt. Record body/hash, queue ID, native path, transcript
UUIDs/sequences and generation, with sensitive text excluded. Retain auth/state,
stop only the owned session/container, await cleanup and preserve failure evidence.

### Guards the regression

| ID | Regression | Test and decisive assertion |
|---|---|---|
| R-1 | Unauthenticated, stale or unready dispatch | C/H methods PC-1 through PC-7, PC-49/50: no eligible connection, command or waiter completion until matching gates pass. |
| R-2 | Duplicate writes, deadlock or unbounded transport | Methods PC-9, PC-39 through PC-45: one mutation, bounded byte/count totals and independent heartbeat/read progress. |
| R-3 | Lost/retargeted standing launch or broader admission | S/T/D methods PC-10 through PC-13, PC-16 through PC-22: committed owner exact, no local calls/worktree/process before allowed admission, capacity <=1, strict resume. |
| R-4 | Unavailable or wrong owner becomes absence/readoption | R methods PC-14/15/47/53: unchanged remote row/agent and zero remote-PID Windows probe, while local partition changes as expected. |
| R-5 | New client loses generation protection | Methods PC-23 through PC-25: reject missing/mismatched echo, preserved expected token, replacement survives and raw fallback count zero. |
| R-6 | Stale/missing/duplicate transcript | E methods PC-4/8/36/37/43/44/51/52: exact persisted UUID set/order, wrong provenance has zero effects, catch-up commit before Ready. |
| R-7 | Linux shadow/detach/cancellation regression | X/N methods PC-26 through PC-32 and V-1: files/mode present, native session identity/stdio detachment, error stops host, canceled launch leaves no live owned host. |
| R-8 | Delivery accepted without recipient or rules proof | Q methods PC-33 through PC-35/46 and V-8/V-9 matrices: no Delivered until exact complete prompt beyond floor; no early ordinary input; backend deferral uncharged. |
| R-9 | Container borrows host evidence or leaks credentials | T method PC-38, S method PC-48 and methods PC-54..57: conservative remote profile, local unchanged; synthetic credential absent from child environment, URLs, logs, public DTOs and transcript. |
| R-10 | Regress existing local operation | Existing `SessionRunnerGenerationWireTests`, `SessionRunnerCapabilityGateTests`, `AgentSessionLaunchQueueOwnershipTests`, `AgentSessionLaunchFailureTests`, `GrokRulesQueueBarrierTests`, `SessionDeliveryProfileTests`, `SessionReconciliationServiceTests`, `SessionRunnerEventPumpTests`, `AgentControlServiceIntegrationTests`, `ZombieCensusServiceTests` and Windows PtyHost classes. Preserve existing decisive assertions; V-12 runs these classes. |

Use these commands after Code has added the named tests. Commit each slice before
long runs. Build each project once, then use `--no-build` on ordinary repetitions.
Results directories must be new for each invocation; the examples use one planned
run label. Review checks expanded method names/nonzero counts in fresh TRX and the
duration tripwire. Unit is separate from integration selection.

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-card0490/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-card0490/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c490-unit-01
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-card0490/ -- --treenode-filter '/*/*/(PhoneHomeConnectionTests*)|(PhoneHomeStandingLaunchTests*)|(PhoneHomeSessionRoutingTests*)|(PhoneHomeReconciliationTests*)|(PhoneHomeEventPumpTests*)|(PhoneHomeQueuedTurnTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c490-server-01
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-card0490/ -- --treenode-filter '/*/*/(SessionRunnerGenerationWireTests*)|(SessionRunnerCapabilityGateTests*)|(AgentSessionLaunchQueueOwnershipTests*)|(AgentSessionLaunchFailureTests*)|(GrokRulesQueueBarrierTests*)|(SessionDeliveryProfileTests*)|(SessionReconciliationServiceTests*)|(SessionRunnerEventPumpTests*)|(AgentControlServiceIntegrationTests*)|(ZombieCensusServiceTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c490-local-01
dotnet build tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-card0490/ --nologo
dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-card0490/ -- --treenode-filter '/*/*/(PhoneHomeCommandDispatcherTests*)|(PhoneHomeConnectionServiceTests*)|(RunnerCapabilitiesTests*)|(GrokRulesFileLaunchTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c490-runner-01
dotnet build tests/Antiphon.PtyHost.Tests --property:OutputPath=bin-card0490/ --nologo
dotnet run --project tests/Antiphon.PtyHost.Tests --no-build --property:OutputPath=bin-card0490/ -- --treenode-filter '/*/*/(ShadowCopyStoreTests*)|(PtyHostLauncherTests*)|(HostSessionPipeTests*)|(ParentDeathSpikeTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c490-windows-01
```

On the **Code-stage** Linux fixture, execute the same project's native tests with
the pinned SDK, real native assets, and actual Linux runtime (not cross-publish
alone). These are native execution commands, not authorization to use a container
from the later managed SourceLanding snapshot:

```text
dotnet run --project tests/Antiphon.PtyHost.Tests --property:OutputPath=bin-card0490/ -- --treenode-filter "/*/*/(LinuxPtyHostLauncherTests*)|(ShadowCopyStoreTests*)/*" --report-trx --report-trx-filename run.trx --results-directory .antiphon/c490-linux-host-01
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-card0490/ -- --treenode-filter "/*/*/LinuxPhoneHomeRunnerTests/*" --report-trx --report-trx-filename run.trx --results-directory .antiphon/c490-linux-runner-01
```

If Code changes `GrokTranscriptTailer`, also run its entire class in that native
lane and on Windows, using a small sanitized Linux 1.0.34 capture. Re-run affected
classes after fixes; establish any inherited red with the exact failing method
at the base commit. Do not widen timeouts or loosen assertions. TestDesign ran no
build/product test; these commands describe the future required evidence.

### Guard inventory

This is the safety inventory for the new or changed seams, including the native
controls whose execution lane is unresolved. No guard is excluded as untested.
Functional compatibility/configuration observations (version prose, status layout,
positive numeric settings) do not add safety controls; their refusal paths still
receive ordinary tests. Inherited unrelated lifecycle guards remain outside this
slice. Each row maps to one distinct PC with the same numeric suffix.

| Guard | Plan reference and safety-critical invariant | Control |
|---|---|---|
| G-1 | D-2: registration authenticates configured runner | PC-1 |
| G-2 | D-2: connect authenticates independently of registration | PC-2 |
| G-3 | D-2: runner cannot register before adoption completes | PC-3 |
| G-4 | D-7: server cannot dispatch before catch-up commits | PC-4 |
| G-5 | D-5: disconnect/expired lease removes eligibility, no child kill | PC-5 |
| G-6 | D-5: competing boot cannot replace an unexpired owner | PC-6 |
| G-7 | D-3/D-5: stale reply epoch cannot complete a current waiter | PC-7 |
| G-8 | D-7: event session owner must match runner/store provenance | PC-8 |
| G-9 | D-5: no transport replay of a sent/unknown mutation | PC-9 |
| G-10 | D-4/D-6: persisted owner routes every later operation without local fallback | PC-10 |
| G-11 | D-4: binding triple is all-null or all-present | PC-11 |
| G-12 | D-4: reservation commit precedes launch enqueue | PC-12 |
| G-13 | D-5: unresolved launch cannot acquire a replacement generation | PC-13 |
| G-14 | D-6: reconciliation mutations are restricted to inventory's owner partition | PC-14 |
| G-15 | D-6: availability/epoch rechecked before committing absence | PC-15 |
| G-16 | D-1/D-8: task/card/worktree admission refuses the pinned standing agent | PC-16 |
| G-17 | D-1/D-8: server only admits the configured non-pool Grok/PtyHost shape | PC-17 |
| G-18 | D-3/D-8: runner independently refuses out-of-policy launch and unsupported operations | PC-18 |
| G-19 | D-8: capacity includes adopted sessions and concurrent reservations | PC-19 |
| G-20 | D-8: exact canonical host root, never a prefix/worktree match | PC-20 |
| G-21 | D-8: launch projection preserves identity/rules and supplies Linux cwd/exe/callback/home | PC-21 |
| G-22 | D-8: remote native resume never becomes implicit fresh | PC-22 |
| G-23 | D-3: launch generation echo remains mandatory | PC-23 |
| G-24 | D-6: remote stop uses captured generation, never upgrades/falls back | PC-24 |
| G-25 | D-3: conditional input retains generation/sequence fences, no raw fallback | PC-25 |
| G-26 | D-9: extensionless apphost is in shadow dependency closure | PC-26 |
| G-27 | D-9: Linux native shared library is in shadow dependency closure | PC-27 |
| G-28 | D-9: shadow apphost keeps executable mode | PC-28 |
| G-29 | D-9: Linux host creates its own POSIX session | PC-29 |
| G-30 | D-9: Linux host releases inherited stdio pipes | PC-30 |
| G-31 | D-9: detach/setup failure prevents host serviceability | PC-31 |
| G-32 | D-9: cancellation after spawn leaves no live owned host | PC-32 |
| G-33 | D-7/acceptance: complete matching UserPrompt is required for delivery | PC-33 |
| G-34 | D-5/acceptance: old transcript below attempt floor cannot confirm new input | PC-34 |
| G-35 | D-8: matching rules acknowledgment precedes ordinary input | PC-35 |
| G-36 | D-7: subscribe/catch-up/live ordering leaves no transcript gap | PC-36 |
| G-37 | D-7: replayed UUID/sequence cannot persist duplicate transcript | PC-37 |
| G-38 | D-11: remote conservative delivery profile cannot borrow local ModernConPty evidence | PC-38 |
| G-39 | D-3: server receive path bounds complete fragmented UTF-8 messages | PC-39 |
| G-40 | D-3: runner receive path independently bounds those messages | PC-40 |
| G-41 | D-3: in-flight admission is bounded | PC-41 |
| G-42 | D-7: phone-home upstream event subscription is bounded with explicit overflow | PC-42 |
| G-43 | D-3/D-7: server pending-live buffer is bounded and overflow invalidates recovery | PC-43 |
| G-44 | D-3/D-7: receive pump cannot wait on command execution/event side effects | PC-44 |
| G-45 | D-3: mutating commands execute in accepted order | PC-45 |
| G-46 | D-5: backend-unavailable queue deferral consumes no delivery attempt | PC-46 |
| G-47 | D-6: Linux session PIDs are excluded from Windows process action | PC-47 |
| G-48 | D-2/D-8: registration credential is not composed into the child environment | PC-48 |
| G-49 | D-2: ticket consumption checks expiry, one-use and runner/store/boot binding | PC-49 |
| G-50 | D-4: same runner name with a different store cannot adopt a persisted owner | PC-50 |
| G-51 | D-5/D-7: old-epoch event cannot reach the current runtime | PC-51 |
| G-52 | D-7: generation-bearing exit cannot close a newer generation | PC-52 |
| G-53 | D-6: Unavailable inventory never becomes authoritative empty/absence | PC-53 |
| G-54 | D-2: outbound register/connect URLs never contain the credential | PC-54 |
| G-55 | D-2/D-11: success/refusal/reconnect diagnostics never log the credential | PC-55 |
| G-56 | D-2/D-11: public registration/status read models never expose the credential | PC-56 |
| G-57 | D-2/D-7: transcript persistence never incorporates registration credentials | PC-57 |

### Positive controls

Each row specifies a small compiling production defect, its exact method and
decisive assertion. The defect must enter the production code used by the method;
do not mutate the test, fake receipt or assertion. Where a method takes data rows,
its precise method filter runs those rows; record every expected failing row.
Controls 26-32 have defined native experiments but **no authorized post-land lane
yet (F-1)**. This is an explicit failed readiness gate, not an executable-PC claim.

| PC | Break corresponding guard by this compiling defect | Exact method expected red | Decisive assertion |
|---|---|---|---|
| PC-1 | Registration credential validator returns success for invalid credential | `C.Register_rejects_wrong_credential_or_runner` | Invalid request non-success; directory has zero registrations. |
| PC-2 | Skip credential check only in connect endpoint | `C.Connect_authenticates_even_with_valid_ticket` | Valid ticket plus invalid credential cannot upgrade; no eligible socket. |
| PC-3 | Start phone-home registration before awaiting adoption | `H.Adoption_precedes_registration` | Register call count zero while adoption gate is held. |
| PC-4 | Set Ready before catch-up transaction completes | `E.Catchup_commit_precedes_dispatch` | Target unavailable and launch count zero while commit held. |
| PC-5 | Availability ignores lease/disconnect state | `C.Expired_or_disconnected_owner_rejects_dispatch` | At expiry/disconnect no command accepted and no kill. |
| PC-6 | Allow an unexpired owner's replacement by a different boot ID | `C.Competing_boot_cannot_replace_live_owner` | Original boot/epoch remains current; competing connect refused. |
| PC-7 | Remove response epoch comparison | `C.Old_epoch_cannot_complete_current_request` | Old result with reused request ID leaves current waiter incomplete; new result alone completes it. |
| PC-8 | Bypass DB runner/store owner check before handing event to runtime | `E.Foreign_owner_event_has_no_effect` | Foreign prompt UUID absent and row/agent unchanged despite matching session ID. |
| PC-9 | Requeue one sent unanswered input during reconnect | `C.Reconnect_never_replays_mutating_commands` | Input/launch/kill/clear/resize execution count stays one after reconnect before queue recovery. |
| PC-10 | Resolve a remote persisted binding to local/default client | `T.Restart_and_pin_changes_preserve_owner` | Exact remote owner receives all operations; local fake call count zero through outage and pin change. |
| PC-11 | Remove the binding all-or-none database constraint from generated migration/model | `S.Partial_bindings_are_rejected_by_database` | Each of six partially-null combinations fails save; all-null/all-present controls succeed. |
| PC-12 | Enqueue launch before reservation transaction commit | `S.Rollback_never_enqueues_launch` | Held/failed commit has zero queue admissions with worker held; rollback leaves no row/peer command; released successful commit admits once. |
| PC-13 | Permit replacement reservation while prior send outcome is unresolved | `T.Unknown_launch_waits_for_owner_inventory` | Generation unchanged and second launch count zero until fresh matching inventory resolves first. |
| PC-14 | Remove owner predicate from a reconciliation partition query | `R.Inventory_never_mutates_another_owner` | Other owner's live/failed rows and agent state unchanged for absence, exit and readoption cases. |
| PC-15 | Omit final epoch/availability recheck after list/get | `R.Disconnect_between_list_and_absence_write_preserves_row` | Remote row remains Running when owner disconnects at the held write boundary. |
| PC-16 | Bypass pinned-agent task/card admission refusal | `S.Pinned_agent_refuses_task_card_and_worktree_entrypoints` | Delegated/OnAgent/card/Worktree/SourceLanding requests create zero reservations/worktrees/launches. |
| PC-17 | Return allowed from server launch-shape predicate | `S.Only_configured_cardless_grok_shape_is_admitted` | Wrong agent, pool, AlwaysOn, kind/backend/custom wrapper refused before reservation; configured shape succeeds. |
| PC-18 | Return allowed from runner dispatch-policy predicate | `D.Runner_refuses_unsupported_scope_before_effects` | Forged non-Grok/cwd/custody/Herdr/kill-all/unknown operation causes zero runtime effects and typed refusal. |
| PC-19 | Omit active/adopted occupancy from capacity reservation decision | `D.Adopted_and_concurrent_sessions_consume_capacity` | One adopted live session rejects new launch; two concurrent starts produce exactly one accepted reservation. |
| PC-20 | Replace exact normalized root equality with StartsWith | `S.Workspace_mapping_is_exact` | Sibling-prefix and descendant/worktree roots refused, accepted root maps only to `/work`. |
| PC-21 | Omit the trusted remote projection before launch | `S.Remote_projection_preserves_identity_and_rules` | Wire cwd `/work`, exe `grok`, Linux home/callback, memory=0; host Cwd unchanged; full rules/hash/args/IDs preserved. |
| PC-22 | Rewrite resume to session-id after native-history missing result | `T.Missing_remote_history_never_starts_fresh` | `--resume` preserved, no second fresh launch, continuity hold names missing history. |
| PC-23 | Skip echoed-generation validation in shared mapper | `T.Launch_rejects_missing_or_wrong_generation_echo` | Missing/wrong echo gives generation refusal, never successful Running launch. |
| PC-24 | Replace captured stop generation with latest runner get generation | `T.Stop_cannot_kill_a_replacement_generation` | Replacement survives, wire token equals captured old generation, unconditional kill count zero. |
| PC-25 | Send raw input when conditional result is unsupported/mismatch/unknown | `T.Conditional_input_never_falls_back_to_raw_input` | Zero raw writes; original expected generation/sequence on wire; replacement receives no input. |
| PC-26 | Remove `Antiphon.PtyHost` from deps closure seed | `X.Linux_apphost_survives_dependency_closure` | Extensionless copied apphost exists with exact bytes. |
| PC-27 | Stop adding `.so` assets to closure | `X.Linux_native_asset_survives_dependency_closure` | Copied `libporta_pty.so` exists with exact bytes. |
| PC-28 | Clear executable bits on copied apphost after copy | `X.Linux_shadow_copy_preserves_execute_mode` | Execute bits equal source mode before launch. |
| PC-29 | Skip `setsid` but continue normal host startup | `N.Host_has_its_own_posix_session` | `/proc`/getsid observation: host session ID equals host PID and differs from intermediary's session. |
| PC-30 | Skip stdio redirection to `/dev/null` | `N.Intermediary_pipes_close_while_host_stays_serviceable` | Intermediary stdout/stderr EOF within bound while host still answers hello/status; inherited fds absent. |
| PC-31 | Treat failed native detach/setup return as success | `N.Detach_failure_never_enters_host_loop` | Inject syscall failure before server loop; nonzero child exit and pipe never serviceable. Use an internal native-call I/O test seam, not a production environment bypass. |
| PC-32 | Return from canceled launcher catch without spawned-host cleanup | `N.Cancel_after_spawn_leaves_no_live_host` | Owned host set empty within 5s, well before its 60s launch timeout. |
| PC-33 | Treat successful input result as Delivered before transcript matcher | `Q.Ack_or_partial_prompt_never_confirms_delivery` | No Delivered for ACK/redraw, prefix-only, wrong session or missing UserPrompt; only full actual body confirms. |
| PC-34 | Remove transcript sequence/time-floor predicate from confirmation lookup | `Q.Old_matching_prompt_cannot_confirm_new_attempt` | Matching body at/below floor or old timestamp does not confirm; new complete row beyond floor does. |
| PC-35 | Treat remote rules receipt/file write as initialization ACK | `Q.Ordinary_input_waits_for_matching_rules_ack` | Ordinary input count zero until valid receipt generation/hash ACK and rules-read prompt; incorrect ACK leaves barrier closed. |
| PC-36 | Subscribe to live events only after inventory/transcript read | `E.Subscribe_before_catchup_prevents_a_gap` | Event produced between snapshot cut and live release persists exactly once with complete ordered UUID set. |
| PC-37 | Bypass transcript UUID/sequence deduplication on live replay | `E.Catchup_and_live_duplicates_persist_once` | Per-session ordered UUID list contains each prompt/reply/end exactly once after replay and server recreation. |
| PC-38 | Resolve remote profile from process-wide local Pty profile | `T.Remote_profile_never_borrows_local_capabilities` | Remote conservative byte ceilings before Grok transform; local Modern profile and capability probe target unchanged. |
| PC-39 | Remove accumulated-byte bound in server WebSocket reader | `C.Server_rejects_oversized_fragmented_message` | M+1 frame total refused before body dispatch; no partial transcript/Ready; M succeeds. |
| PC-40 | Remove accumulated-byte bound in runner WebSocket reader | `H.Runner_rejects_oversized_fragmented_message` | M+1 request never reaches dispatcher; bounded close; M succeeds. |
| PC-41 | Bypass request admission permit acquisition | `C.Inflight_requests_are_bounded` | 33rd request not written while 32 held; cancellation releases one permit without replay. |
| PC-42 | Use the old unbounded subscription for phone-home | `H.Upstream_event_overflow_is_explicit` | Independently exceeded count/bytes triggers overflow and bounded occupancy, socket unavailable until recovery. |
| PC-43 | Keep accepting live frames after pending catch-up bound reached | `E.Pending_live_overflow_cannot_publish_ready` | Overflow closes recovery, Ready false and buffer count/bytes bounded; subsequent catch-up is whole. |
| PC-44 | Await runtime command/event-side-effect completion inline on receive pump | `E.Held_launch_and_event_consumer_do_not_block_responses` | Held launch/consumer remain held while heartbeat and target-bound read response complete. |
| PC-45 | Dispatch session mutations concurrently rather than through ordered lane | `D.Session_mutations_execute_in_order` | Second mutation does not enter runtime until first released; final execution order equals accepted order. |
| PC-46 | Charge queue DeliveryAttempts for backend-unavailable deferral | `Q.Unavailable_owner_defers_without_charging` | Attempts/timestamp/floor unchanged and no input while unavailable; later genuine receipt completes same row. |
| PC-47 | Include remote-bound sessions in local PID census projection | `R.Remote_pids_never_enter_local_process_census` | No Windows PID action attributable to remote row despite numeric collision; local control remains functional. |
| PC-48 | Copy the synthetic registration credential into composed launch environment | `S.Registration_secret_never_reaches_child` | Child-visible environment contains neither credential key nor sentinel value; required public session IDs retained. |
| PC-49 | Accept consumed/expired/mismatched connection ticket | `C.Ticket_is_single_use_and_identity_bound` | Only first unexpired matching runner/store/boot upgrade succeeds; all other data rows refuse. |
| PC-50 | Accept a different store for an already bound runner name | `C.Store_change_cannot_adopt_persisted_session` | Persisted store unchanged; typed store mismatch and no dispatch to new store. |
| PC-51 | Remove event epoch comparison only | `E.Old_epoch_event_has_no_effect` | Old-epoch prompt UUID absent after replacement; current epoch prompt persists. |
| PC-52 | Drop accepted generation from event handling and use current row generation | `E.Old_generation_exit_cannot_close_replacement` | Replacement Running/generation unchanged after stale exit; matching exit still closes correct row. |
| PC-53 | Convert Unavailable inventory to Available(empty) | `R.Unavailable_owner_is_not_absence` | Its live/failed rows and agent state unchanged, zero kills; available local absence still reconciles. |
| PC-54 | Append configured credential as a connect URL query value | `H.Credentials_never_appear_in_request_urls` | Captured registration/connect request URIs contain neither synthetic secret nor its escaped form. |
| PC-55 | Add configured credential to the registration success diagnostic | `C.Diagnostics_never_include_credentials` | Captured formatted logs/scopes from success, refusal and reconnect contain no synthetic credential; diagnostic code still present. |
| PC-56 | Add a credential field to the public runner status response | `C.Public_runner_models_never_include_credentials` | Serialized registration/status responses contain no synthetic secret or credential property, while non-secret owner/build identity is present. |
| PC-57 | Append configured registration credential when persisting incoming transcript text | `E.Transcript_never_contains_registration_credentials` | Persisted prompt is exactly the peer's submitted body and contains no synthetic credential in any entry. |

Credential controls use only synthetic sentinels, including successful and rejected
calls. No real credential is read into test output. Child environment, request URL,
logs, public DTOs and transcript are independent disclosure boundaries, so each
has its own PC; status formatting itself adds no guard. No image may contain OAuth
or a registration secret; V-11 inspects the image build inputs and mount layout.

All PCs run **after ordinary Code V/R, ordinary Review and confirmed land**.
Mutation records break, exact red assertion, restore and green at landed SHA;
Code does not execute PCs as a substitute for ordinary tests. For example:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0490-pc/ -- --treenode-filter '/*/*/PhoneHomeQueuedTurnTests/Ack_or_partial_prompt_never_confirms_delivery' --report-trx --report-trx-filename run.trx --results-directory $pcEvidenceDirectory
```

The commissioner supplies the external evidence root; make `$pcEvidenceDirectory`
a fresh child for that PC and phase. Every other PC uses its table's exact expanded
class/method and owning project. Run red and restored green with the **same**
method filter; build failure, setup failure, zero tests or native skip is not red.
Rebuild after restoration with refreshed timestamps. Avoid batching until Code's
final files show mutations affect different files/methods; the cost below assumes
serial independent cycles. Never commit/push from a SourceLanding snapshot.

### Out of scope

- Full CARD-0038 portability/ceiling work, other providers, worktrees, multiple
  container agents, fleet scheduling, remote/server2 or TLS provisioning, Herdr,
  SourceLanding/cgroups/custody on Linux, automated restart/spend, image pruning
  and container replacement lifecycle. These stay follow-up work; no extra guard
  families or live turns are added for them.
- Channel replies, delegate completion notifications and task-result outboxes:
  this card's producer is an ordinary standing-session queued prompt. It adds no
  channel/outcome-delivery producer. Task/card refusals are tested before effects.
- Browser/client suites: no client/UI change. Whole Antiphon.Tests assembly:
  the plan's Unit plus named affected integration profile governs. Run additional
  classes only for actual changed paths; investigate inherited failures by method.
- Exhaustive Cartesian product of auth, transport faults and every operation:
  auth tests gate before dispatch; RPC codec table covers each operation; all
  mutation operations are covered by no-replay data rows; delivery cuts cover both
  recipient states. Boundary combinations that matter are explicitly retained.
- Repeating the real model turn per PC: costs spend and cannot deterministically
  control the recovery boundary. One real turn calibrates the deterministic peers;
  portable/native PCs still prove their named guards. No fake turn replaces V-11.

### Cost

All figures are **estimated foreground minutes**, not measurements. They include
fresh process/database startup in scoped runs; they exclude coding, review analysis,
provider sign-in repair and waiting for human configuration. Code must replace
estimates with measured counts/times at its committed SHA. F-1 currently makes the
complete post-land floor unavailable, not zero.

| Ordinary Code floor | Minutes |
|---|---:|
| Windows restore/build, isolated DB migration/setup | 8 |
| Linux image/native build and isolated acceptance host setup | 12 |
| Unit lane | 5 |
| New server connection/routing/reconciliation/event tests | 12 |
| New runner dispatcher/connection and existing runner compatibility | 6 |
| Real queue/rules crash and failure matrices (26 + 5 cases) | 12 |
| Named existing server compatibility integrations | 14 |
| Windows PtyHost regression classes | 6 |
| Required Linux native methods | 6 |
| One real Grok rules initialization + queued turn + owned cleanup | 12 |
| **Setup/build 20 + ordinary V/R 73 = Code floor** | **93** |

| Mutation floor | Count x per-control minutes | Minutes |
|---|---|---:|
| Landed snapshot/evidence inventory, initial build/DB and native setup allowance | one setup | 12 |
| Portable controls (PC-1..25 and PC-33..57) | 50 x (break 0.15 + red build/run 0.95 + restore 0.15 + green build/run 0.95) | 110.0 |
| Native controls PC-26..32, execution lane unresolved under F-1 | 7 x (break 0.20 + red build/run 1.80 + restore 0.20 + green build/run 1.80) | 28.0 |
| **Every PC red/restore/green plus Mutation setup** | 57 controls | **150.0** |
| **Total verification floor: Code setup/build + V/R + Mutation setup + every PC cycle** | 20 + 73 + 12 + 110.0 + 28.0 | **243.0** |

No PC execution cost is hidden in ordinary Code verification. The native 28 minutes
is a resource estimate conditional on resolving F-1, not a promised run on the
current Windows snapshot. Reused outputs avoid an estimated 8 redundant one-minute
ordinary builds (8 minutes). Method-scoped PCs use 120.2 minutes of red/green
build/run work; a conservative estimated 3 minutes per whole-class phase would
cost 57 x 2 x 3 = 342 minutes, so the projected scoped saving is 221.8 minutes.
These are planning comparisons, not measured savings. Zero live-canary savings
are claimed: the one real receipt is mandatory. No sharding or overlap with
Antiphon.Agents.Pty.Tests is assumed.

**Handoff audit:** nearest bodies/helpers read before naming new cases;
guards=57, mapped=57, missing mappings=0, duplicate PC mappings=0. Every row has a
specific compiling defect and decisive method/assertion; all 57 are pending
implementation. Fifty have a portable inherited-process execution design;
seven native controls lack a permitted SourceLanding execution lane. Therefore
the mandatory **all PCs executable** gate is not met and next is **plan**, not
code. Resolve only F-1, then return to TestDesign to validate that seam and its
cost; do not broaden the product slice or silently waive native controls.
