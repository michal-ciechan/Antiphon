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

TestDesign task `2dbc8291`, based on `a28fa3b9`. This appendix preserves D-1 through
D-11 and S1 through S5. Named new methods are implementation requirements, not
executed tests. No product build, container, provider turn or mutation ran during
TestDesign. The requested branch was already held by its Plan worktree; the
appendix is delivered on `feat/card-task-2dbc8291` at the requested base.

### Inspection

- `tests/Antiphon.PtyHost.Tests/PtyHostLauncherTests.cs`,
  `ShadowCopyStoreTests.cs`, and `PipeTestClient.cs`: bodies and local helpers read.
  Existing launcher methods skip Linux; shadow tests use synthetic Windows assets.
  S1 therefore needs a Linux-native fixture, not removal of the Windows skips.
- `tests/Antiphon.SessionRunner.Tests/LocalHttpRunner.cs` and
  `RunnerStartupReadinessTests.cs`: bodies read. The executable, adoption blocker
  and child are Windows-specific. Reuse random-port ownership and joined teardown,
  not the executable name or Herdr blocker, for the new Linux fixture.
- `tests/Antiphon.Tests/Agents/SessionRunnerGenerationWireTests.cs`: bodies and
  HTTP stub read. Generation echo, missing capability, conditional-input unknown
  and no raw-kill fallback are existing local-wire obligations.
- `tests/Antiphon.Tests/Application/AgentSessionLaunchQueueOwnershipTests.cs`:
  bodies, `OwnershipFixture` and `OneAdapterFactory` read. Existing accepted
  generation capture and obsolete queued launch tests do not carry remote owner.
- `tests/Antiphon.Tests/Application/SessionReconciliationServiceTests.cs`:
  stale exit/resume/readoption and stale-list bodies, `BuildService`,
  `SeedWorkingAgentWithSessionAsync` and `FakeRunnerClient` read. Its single-runner
  fake and shared-store helpers need explicit owner inventories and an isolated DB.
- `tests/Antiphon.Tests/Application/SessionRunnerEventPumpTests.cs` and
  `SessionDeliveryProfileTests.cs`: bodies and helpers read. The former deliberately
  demonstrates a blocked local pump; do not invert that historical assertion while
  testing the new receive pump. The latter currently resolves PtyHost globally.
- `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs`: service graph,
  creation, default adapter and transcript callback read;
  `QueuedReceiptAssertions.cs` read in full. Both can write transcript rows directly
  from fake submission. Neither alone proves the new socket/event persistence path.
- `GrokRulesQueueBarrierTests.cs`, `GrokRulesReceiptTests.cs`: bodies read;
  `SessionMessageQueueInterruptedAttemptTests.cs`: initial delivered, late-confirm,
  Enter-only, retype, busy and recovery-window bodies read. Existing queue status is
  `Sent`; `DeliveryVerdict.Delivered`/`LateConfirmed` and complete recipient evidence
  are separate checks. There is no `QueuedMessageStatus.Delivered`.
- `TestDbFixture.cs` isolation API and `ProductionRunnerGuard.cs` bodies read.
  `CreateIsolatedSchemaAsync` now clones a database despite its historical name.
  Every context in a new fixture must use the returned connection, including fault
  injectors, restarts and verification queries; static `BridgeQueueHarness.CreateContext`
  would silently inspect the default store.

The required setup is absent at this base: no phone-home peer/fixture, Linux launcher
fixture, image or acceptance script exists. Code supplies these within S1-S5.
New fixtures retain the assembly-local process limiter, own every child and socket,
and await shutdown. A test booting real server `Program` disables check interpreter,
diagnose, output distiller and Hangfire, uses the cloned DB and a refusing local
runner (or a fixture-owned random port), and never calls production 17204.
Use fake time only for lease/ticket tests. Queue deadlines use the real clock or
an offset over it; a frozen `TimeProvider` with real timers is not valid queue setup.

### Delivery inventory

Durable identity is `(AgentSession.Id, RunnerId, RunnerStoreId, StartedAt)` for a
launch/session, extended by `SessionQueuedMessage.Id`, immutable submitted body and
`LastDeliveryBaselineSequence`/`LastDeliveryGeneration` for input. A connection
epoch and request ID correlate transient RPCs; neither replaces that durable tuple.
Transcript UUID/sequence identifies a persisted recipient record, not a command ack.

| Path | Producer -> destination | Persistence and recovery | Observable receipt |
|---|---|---|---|
| Standing launch | `AgentControlService` -> launch queue -> bound client -> socket dispatcher -> `SessionRunnerRuntime` | Commit all owner fields and accepted generation before enqueue. Rollback sends nothing. Recreate server DI after the commit/enqueue cut and resolve the same owner. An unanswered sent launch remains reserved until that owner's fresh inventory resolves it. | Same-generation runner session and runtime attachment; this is launch receipt only, never prompt delivery. |
| Rules initialization | Composer/rules payload -> runner rules file -> rules refresh queue -> Grok | Persist expected generation/hash/count and runner receipt. Inject receipt-save and refresh-enqueue failure separately; retry must converge on the same refresh identity. Runner path is Linux-readable; server must not stat it. | Complete refresh UserPrompt, matching rules acknowledgement and successful TurnEnd persist before ordinary queued work is released. A file receipt alone does not release it. |
| Ordinary input | Normal queue API -> durable message -> queue worker -> bound adapter/client -> socket -> recipient composer | Failed DB insert has no send. Committed row survives lost wakeup. Before-send cancellation writes nothing; after-send disconnect/timeout is unknown, with no transport replay. Queue recovery pulls transcript first, then uses same-generation composer evidence for Enter-only; unavailable deferral spends no attempt. | Complete normalized submitted body in this session's UserPrompt beyond its attempt floor, with matching generation/identity, plus queue verdict. The real canary additionally requires nonce-bearing assistant text and TurnEnd. |
| Transcript/live events | Runner tailer/event hub -> bounded subscriber -> socket -> runtime -> PostgreSQL -> queue receipt reader | Runner transcript is the source for reconnect. Subscribe before inventory/catch-up; commit backfill before releasing live events. Disconnect on overflow, recover from transcript; replayed UUIDs are deduplicated. Crash before DB save replays; crash after save before verdict late-confirms without typing. | Query persisted recipient entries through the server transcript API/DB and prove content, order and uniqueness. Sent, request acceptance, socket ack and screen PONG are intermediate observations only. |

Controlled peers may model provider transcript records only after observing the bytes
actually submitted through the real queue and socket. They must send those records
through production event/catch-up ingestion, never insert the expected recipient rows
directly. This proves routing, queue recovery and persistence, but not POSIX PTY or
Grok acceptance. Native benign-child tests prove the Linux process/pipe boundary but
not provider auth or transcript format. Only the opt-in real Grok turn closes both
remaining boundaries. Ordinary Review rejects evidence stopping before the recipient.

Additional bodies read after the inspection checkpoint:
`AgentControlServiceIntegrationTests.Legacy_only_provider_starts_unprofiled_agent_through_configured_registry`
and `Registered_profile_resolver_without_default_preserves_legacy_claude_launch_arguments`,
with `BuildHarness`; `AgentSessionLaunchFailureTests` Grok startup recovery bodies
with `LaunchFixture.CreateCoreAsync`, `WireDelivery` and adapter factories;
`AgentLaunchSpecTests` and `SessionRunnerCapabilityGateTests` in full;
`ZombieCensusServiceTests` normal/historical classification and `World.CreateAsync`
with fake census/runner; `ParentDeathSpikeTests` in full;
`HostSessionPipeTests` launch/manifest/reattach bodies with `HostHarness` in full;
`PtyHostAdoptionTests.Running_session_survives_runner_restart_with_buffer_and_input_intact`
with launch/wait helpers; `GrokTranscriptTailerTests.Real_turn_rows_normalize_to_UserPrompt_ToolCall_coalesced_text_and_TurnEnd`
with the literal turn rows, `TempUpdatesPath`, `AppendRowsAsync`, `PollForEntriesAsync`
and `NormalizeFixture`. Launcher and PtyHost entrypoint bodies were also inspected.
Native fixtures must not copy the Windows `cmd.exe` child, swallowed cleanup failure,
or direct DB transcript insertion from these helpers.

Inspection-to-boundary index: launcher/shadow/pipe -> V-1, R-1;
startup/HTTP/wire -> V-2, R-2; control/launch/DB -> V-3, R-3;
reconciliation/census -> V-4, R-4; event/queue/rules -> V-5/V-6, R-5/R-6;
tailer/adoption -> V-1/V-7, R-1/R-7; profile -> V-8, R-8.
The existing `AgentServiceIntegrationTests`, `AttentionServiceTests` and
`SessionHealthTests` are not selected for edits by this appendix. Their affected
owner-list behavior is exercised by the new R-4 service-consumer method below;
Code must inspect their bodies before adding cases there if its final diff requires
such edits. This avoids naming invented cases in unread fixtures.

### Proves it works now

All methods below are **new**, except the explicitly named existing regression
selection. File = class name plus `.cs` in the S1-S5 location. `PhoneHomeConnectionTests`
is under `Antiphon.Tests/Agents`; all other server PhoneHome classes are under
`Antiphon.Tests/Application`. Runner classes are under `Antiphon.SessionRunner.Tests`;
native launcher/shadow tests are under `Antiphon.PtyHost.Tests`.

- V-1: Native launch/adoption | Linux process/pipe integration |
  `LinuxPtyHostLauncherTests.Shadow_host_exchanges_bytes_and_exits`,
  `LinuxPtyHostLauncherTests.Detach_creates_a_new_session`,
  `LinuxPtyHostLauncherTests.Intermediary_pipes_reach_eof_while_host_lives`,
  `LinuxPtyHostLauncherTests.Canceled_launch_leaves_no_owned_host`, and
  `LinuxPhoneHomeRunnerTests.Restart_adopts_same_store_session_and_generation` |
  real extensionless apphost from the shadow directory, actual `libporta_pty.so`
  loading, Unix pipe hello, benign `/bin/cat` byte round-trip followed by controlled
  exit, same-user runner restart retaining PID/session/generation and later input.
  Assert `getsid(hostPid) == hostPid`, inherited fds 0/1/2 resolve to `/dev/null`,
  intermediary exit and both redirected streams finish within 5 seconds while host
  stays alive. A fixture-owned canceled launch is gone within 5 seconds, before its
  60-second launch timeout. After teardown every recorded owned PID has exited.
  Non-Linux skip is allowed only in the Windows lane; Linux must execute all five.
- V-2: Outbound connection | real loopback Kestrel/WebSocket integration |
  `PhoneHomeConnectionTests.Register_connect_and_correlate_out_of_order_results`,
  `PhoneHomeConnectionServiceTests.Adoption_precedes_registration`,
  `PhoneHomeConnectionTests.Held_launch_does_not_block_heartbeat_or_reads` |
  runner initiates register/connect after adoption; two out-of-order read replies
  complete their own requests, sequential mutations retain order, and a held launch
  cannot starve heartbeat/cancel/transcript requests. No runner port is published.
- V-3: Standing start | migrated PostgreSQL plus real control/launch services |
  `PhoneHomeStandingLaunchTests.Start_commits_binding_before_remote_launch`,
  `PhoneHomeSessionRoutingTests.Restart_and_pin_change_keep_persisted_owner` |
  launch observer uses an independent DbContext to see all three owner fields and
  accepted generation before first frame. Restart service provider against the same
  DB, change/disable the current pin, then get/attach/reattach/input/resize/stop the
  old conversation: only its persisted owner is called. Null legacy binding stays
  local; unknown session is not-found, never a local request.
- V-4: Recovery isolation | two controlled peers, cloned DB |
  `PhoneHomeReconciliationTests.Unavailable_owner_does_not_close_rows_or_block_local_scan`,
  `PhoneHomeReconciliationTests.Every_pass_uses_only_its_owner_partition`,
  `PhoneHomeReconciliationTests.List_consumers_keep_remote_unknown_and_local_pids_separate` |
  local legacy plus remote rows have overlapping PID numbers; available-empty,
  available-running and unavailable are distinct. Local reconciliation advances
  while offline remote rows remain unchanged. Failure/readoption/agent status,
  resume/stop, AgentService, supervisor, health, attention, dispatcher and zombie
  census are exercised through their actual public service entry points.
- V-5: Ordinary queued turn | real queue, rules service, bound production adapter,
  socket, controlled recipient and runtime persistence |
  `PhoneHomeQueuedTurnTests.Queue_reaches_recipient_when_busy_or_already_idle`
  with `busy=false,true` |
  enqueue through `SessionMessageQueueService.EnqueueAsync`, not `SeedPendingMessageAsync`.
  For busy, emit a prior open turn through the peer; assert no ordinary input,
  no receipt and Pending before emitting its TurnEnd. For already-idle, no extra
  wakeup is necessary. Both arms end with one complete recipient UserPrompt for
  the transmitted short nonce body, above the recorded floor, and a verified queue
  verdict. Use the production Grok join-safe transform, not hand-normalized output.
- V-6: Every handoff survives its cut | same integration fixture |
  `PhoneHomeStandingLaunchTests.Launch_handoff_cuts_preserve_owner_and_reservation`,
  `PhoneHomeQueuedTurnTests.Rules_handoff_cuts_recover_before_ordinary_work`,
  `PhoneHomeQueuedTurnTests.Queue_handoff_cuts_recover_to_recipient`,
  `PhoneHomeEventPumpTests.Persistence_cuts_recover_without_retyping` |
  run every cut defined below. Recreate queue/runtime/directory/service scopes
  against the same DB and peer transcript; no callback from the discarded graph
  may finish the recovery. Assert receipt on the resumed path, not just row survival.
- V-7: One live turn | opt-in isolated container/server acceptance |
  `pwsh -NoProfile -File scripts/verify-phone-home-grok.ps1 -ConfigurationFile .antiphon/card0490-live.json -EvidenceRoot .antiphon/card0490-live-evidence`
  (new S5 harness contract) |
  exactly one pinned standing Grok session, normal rules initialization, then one
  short queued UI prompt: `Reply with PHONE_HOME_OK_<nonce> and do not use tools.`
  Require source SHA, image digest, Grok 1.0.34, runner/store/boot/epoch, session ID,
  accepted generation, queue ID, attempt floor, submitted-body hash, UserPrompt
  UUID/sequence/text hash, nonce-bearing AssistantText and successful TurnEnd.
  The UserPrompt must be complete, later than the attempt floor, and obtained from
  the server transcript API backed by production persistence. Record the actual
  Linux `updates.jsonl` relative path and a sanitized three-kind fixture if it differs
  from existing evidence. Missing auth, unavailable provider or an all-skipped test
  is outstanding acceptance. Stop only this session by its captured generation and
  its task-owned container; retain mounted OAuth/state. Do not restart AppHost.
- V-8: Local compatibility and delivery profile | Windows Unit + selected integration |
  `SessionDeliveryProfileTests.Phone_home_Grok_never_uses_local_modern_evidence`,
  plus the existing classes selected under Cost |
  local HTTP routes/SSE names and Windows spawn stay unchanged; phone-home uses the
  conservative profile, Grok brief inline limit remains zero after its transform,
  and local ModernConPty capability is neither borrowed nor downgraded remotely.

The live configuration file contains only isolated server/DB endpoint and fixture
paths, runner/standing-agent IDs, image identity, workspace/state/secret-file paths
and the approved OAuth mount path; it must not contain secret values. The harness
validates the isolated server address, forbids production service ports, checks no
host-published runner port, verifies `/work` is read-only and `/state` is writable by
the image UID, and validates the pinned image input. It creates no broker/channel.
It captures neither environment dumps nor credential file contents. Missing OAuth
must return the existing sign-in refusal with no API-key fallback. These are setup
and deployment checks, not a new credential management or image lifecycle feature.

### Guards the regression

Each exact PC method below is also an ordinary regression test run by Code on the
unmutated implementation. No mutation is required during Code or ordinary Review.

- R-1: Linux closure, execute mode, detach, stdio and cleanup | V-1 methods plus
  `ShadowCopyStoreTests.Linux_closure_keeps_apphost_and_native_library` and
  `ShadowCopyStoreTests.Linux_copy_preserves_execute_mode` | compare source/shadow
  bytes and Unix mode, then execute the shadow host. Preserve Windows
  `PtyHostLauncherTests`, `HostSessionPipeTests`, `ParentDeathSpikeTests` unchanged.
- R-2: Authentication, tickets, arbitration, fencing, bounded transport and progress |
  `PhoneHomeConnectionTests` and `PhoneHomeConnectionServiceTests` methods in PCs |
  bad credentials at both endpoints; wrong runner/store/boot, expired and reused
  tickets; competing unexpired boot versus expired same-store adoption; stale result
  versus current result with the same request ID; timeout/cancellation before and
  after send. Record received frames and pending waiters, never only status codes.
- R-3: Durable owner, launch transaction, exact scope/capacity/path/generation |
  `PhoneHomeStandingLaunchTests`, `PhoneHomeSessionRoutingTests`,
  `PhoneHomeCommandDispatcherTests` methods in PCs | each invalid admission arm
  leaves reservation/session/task/worktree counts unchanged and process-start count
  zero. Full Cartesian products are unnecessary where a single common predicate
  rejects each arm; exercise the accepted baseline plus one changed dimension at a
  time and the race combinations below.
- R-4: Wrong owner cannot change DB state or local PID decisions |
  V-4 methods and `PhoneHomeReconciliationTests.Disconnect_after_list_blocks_absence_write` |
  exercise Starting inside/outside grace, Running, Failed/readoptable and Stopped;
  valid/stale generation; matching/mismatching owner; unavailable/available-empty;
  disconnect/new epoch between list and get/save. Successful matching-owner control
  must advance, so an implementation that disables all reconciliation cannot pass.
- R-5: Receipt cannot be inferred | `PhoneHomeQueuedTurnTests.Only_complete_matching_UserPrompt_confirms`
  and `Receipt_must_be_after_attempt_floor` | hold the queue at confirm, return input
  success/screen PONG/TurnEnd/AssistantText without UserPrompt, then wrong-session,
  unrelated, truncated-head, truncated-tail and stale identical prompts. None is a
  verified receipt. Finally emit the fresh whole prompt and require one verified
  receipt. Existing degraded screen-only semantics may persist; `Sent` or a degraded
  `Delivered` enum alone must not satisfy this test's recipient predicate.
- R-6: Recovery cannot duplicate or lose the turn | V-6 plus
  `PhoneHomeQueuedTurnTests.Offline_deferral_does_not_spend_attempts`,
  `PhoneHomeQueuedTurnTests.Rules_receipt_is_owner_bound_and_remote_readable`,
  `PhoneHomeQueuedTurnTests.Rules_barrier_requires_prompt_ack_and_successful_end`,
  `PhoneHomeEventPumpTests.Catchup_commits_before_live_release`,
  `PhoneHomeEventPumpTests.Replayed_uuid_persists_once` |
  pull transcript before writes; late-confirm adds no input; same-generation whole
  composer gets Enter only; changed generation or unreadable snapshot never gets
  that Enter; the original complete body is eventually received once on the valid
  recovery arm. No automatic transport replay is permitted on any arm.
- R-7: Native transcript/resume identity | V-1 adoption and V-7 plus
  `PhoneHomeStandingLaunchTests.Resume_never_probes_host_history_or_starts_fresh` |
  existing Linux history retains strict `--resume` and binding; absent history gives
  a visible refusal with zero fresh launch. Read a Linux path using runner APIs,
  never `Directory.Exists` on the Windows server. Preserve existing tailer turn,
  half-line, replay UUID and exit-without-synthetic-TurnEnd cases.
- R-8: Local separation | V-8 and
  `PhoneHomeStandingLaunchTests.Projection_keeps_identity_rules_and_local_definition` |
  compare unpinned launch spec before/after feature enablement, reserved identities,
  args, model and rules bytes; only the pinned standard `grok.exe` projection gets
  Linux exe/cwd/home/callback and zero memory limit. Reject custom wrappers/profiles.

Handoff cut matrix (V-6):

| Exact method | Cuts and decisive assertions |
|---|---|
| `Launch_handoff_cuts_preserve_owner_and_reservation` | Fail DB commit: zero enqueue/launch. Commit then fail enqueue or destroy service graph before drain: durable Starting row is visible; fresh owner inventory resolves it, never local fallback. If authoritative inventory proves no launch, preserve the existing visible failed/start-retry contract; do not invent an automatic second provider start. Sent launch with lost response: replacement is blocked until list/get resolves its original generation, then adopt the single observed launch. |
| `Rules_handoff_cuts_recover_before_ordinary_work` | Rules file written but receipt save fails; receipt committed but refresh insert transaction fails; refresh row committed but wakeup lost; submitted rules prompt before transcript save; full rules turn persisted before Ready state save. Restart at each cut. Retry yields one keyed refresh obligation, valid runner receipt and actual refresh recipient evidence; ordinary nonce input stays absent until the barrier opens. |
| `Queue_handoff_cuts_recover_to_recipient` | Enqueue insert fails: zero send, explicit producer error, then a fresh successful enqueue goes end-to-end. Row committed/wakeup lost; Sent committed before socket send; body received with response lost; body held before Enter; submitted prompt recorded while socket down. Run every recoverable cut for both busy and already-idle recipient. On recover/restart, exact queue ID/body/floor remains traceable and one complete UserPrompt eventually persists. Failed insert has no accepted durable request to recover. |
| `Persistence_cuts_recover_without_retyping` | Runner transcript before event enqueue; bounded subscriber enqueue fails; socket frame before DB save; DB save fails; DB save succeeds before queue verdict update. Reconnect/catch-up against the same runner transcript; assert one ordered UUID set, full UserPrompt and late-confirm with zero additional prompt writes. A persisting DB outage remains unavailable/deferred, never Ready or accepted. |

Transport boundary matrix (R-2/R-6): test actual serialized UTF-8 envelope bytes at
limit-1, limit, limit+1, including multi-byte text and fragmented WebSocket messages.
Use a small configured cap for exhaustive cases and one real 16 MiB default-boundary
case. Apply the shared framing guard on both peers. Do the same for request counts
31/32/33, live event count 1023/1024/1025 and pending event bytes 16 MiB-1/equal/+1;
the first exceeded limit wins, so test count-first and bytes-first separately.
Zero/negative options fail startup. Serialize default runner buffer/snapshot bounds
and maximum configured Grok rules payload, including JSON escaping; assert they fit
or return a typed complete-read failure. A too-large transcript produces no partial
success, does not become receipt, and does not cause an endless reconnect/read loop.
Hold command execution and event consumption independently to prove boundedness and
continued receive progress. Successful below-limit controls must still exchange
whole payloads. Do not allocate an unbounded buffer before checking fragments.

Scope boundaries (R-3): feature disabled; wrong agent ID; unnamed/pool/AlwaysOn;
card start; delegated Shared/Worktree/OnAgent/SourceLanding task; wrong kind/backend;
custom wrapper/profile; arbitrary RPC/kill-all/custody. Run each independently with
zero side effects and the one permitted baseline. Race two starts against capacity
one and repeat with one adopted session occupying it. Exact host root is accepted;
sibling prefix, descendant worktree, `..`, alternate drive and raw POSIX server cwd
are refused. Equivalent canonical Windows root spelling follows existing host
canonicalization, not POSIX canonicalization on Windows. Exercise capacity during
startup adoption and same-boot reconnect as well as fresh registration.
