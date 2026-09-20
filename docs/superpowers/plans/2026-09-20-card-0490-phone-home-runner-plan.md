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

### D-12: PC-28 through PC-31 use a fresh, inherited QEMU/TCG process

Plan amendment `3f77b203` (2026-09-20, base `2be56d1a`): keep the Mutation
worker and SourceLanding binding on the existing Windows `windows-job-v1` lane.
For these four controls only, its local test harness starts a new
`qemu-system-x86_64.exe` child using full-system TCG emulation, boots an offline
Linux test disk, executes one exact method, collects its results, and joins QEMU.
Linux executes the real ELF apphost, kernel `setsid`, descriptors and Unix modes;
this is a real Linux guest on emulated hardware, not a mocked POSIX API.

The reason for TCG is custody: guest CPU/device execution lives in the inherited
QEMU process. Do not enable WHPX/KVM, vhost-user, external device backends, daemon
mode or a VM manager. A fresh boot has no saved execution state. Guest detach
cannot outlive that process. Keep the existing Windows job's descendant accounting
and drained-output receipt; neither a guest success marker nor QEMU exit replaces
the task's native receipt. This is an inference from the inspected custody code
and QEMU's documented execution model, to qualify with the ordinary harness checks
below before land; it is not a claim that a sourced run has already succeeded.

Rejected: a new container through Docker Desktop/Testcontainers (the daemon owns
its execution); a fresh `docker.exe`/`wsl.exe` client (client ancestry does not contain
the guest); DinD started by an existing daemon (same ownership gap); a Linux
phone-home Mutation worker (expands product admission/custody); an unsourced clone,
Windows substitute, skipped test or pre-land green (does not prove sourced PCs).
A fresh Linux daemon would itself need an inherited Linux execution host here;
QEMU already supplies that host, so adding Docker inside it buys nothing for four
PtyHost tests. Product D-1 through D-11 and S1 through S5 remain in force.

The exact harness, qualification, evidence and revised costs are in the
[native-PC execution amendment](#native-pc-execution-amendment-3f77b203).
This decision needs no SourceLanding API, capability, admission or cleanup change.

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
| Command outcomes | Bound client request -> runner runtime -> epoch/request-correlated caller waiter | Waiters are transient, session owner/generation is durable. Unsent cancellation is no-op; crash after execution before reply yields unknown, never automatic write retry. Reads may be freshly requested; launch/input recovery is as above; stop is retried only explicitly against the captured generation. | Calling service receives the matching complete DTO or typed failure. Conditional input/kill requires its generation-qualified result; neither is evidence of prompt receipt. Test each supported operation and the reply-loss cut. |
| Registration/availability | Adopted runner -> authenticated server directory/status reader | Availability and single-use ticket are transient; mounted RunnerStoreId and DB ownership persist. Server restart requires fresh registration; runner restart keeps store ID, changes boot and re-adopts before registration. | Authenticated socket plus completed inventory/transcript recovery and current lease make status Ready; registration acceptance/heartbeat alone cannot make session input delivered. |

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
Also read the existing `SessionMessageQueueDeliveryVerificationTests` transcript
and degraded `LastDelivery` receipt methods, runner `SessionRunnerSettings`,
`GrokRulesSettings`/`GrokRulesTransport` and `ShadowCopyStore` closure/copy bodies.

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
  Add `PhoneHomeConnectionTests.Supported_operations_preserve_contracts` for every
  D-3 operation with exact DTO/body/result or typed Problem Details parity against
  the shared mapper, and `Default_buffers_and_rules_fit_full_envelopes` for the
  actual configured-size serialization check below.
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
  In PC assertions `verifiedReceipt` means the production receipt claims
  `ConfirmedBy == DeliveryConfirmedBy.Transcript` and `Degraded == false`.
  Separately compare the persisted complete body and floor without calling the
  production matcher being mutated; this independent assertion prevents a lying
  production receipt from becoming the test's own oracle.
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
Zero/negative options fail startup. The inspected defaults are 262144 runner replay
characters and 262144 UTF-8 rules-file bytes. Serialize full buffer/snapshot and
rules envelopes at those defaults, including maximum JSON escaping and metadata;
require each to fit the 16 MiB default and preserve exact bytes after decoding.
Repeat with deliberately larger configured limits and require a typed complete-read
failure if the full serialized envelope exceeds the transport limit. A too-large transcript produces no partial
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

### Guard inventory

This appendix assigns fresh sequential G/PC numbers; the earlier table remains a
seed table. There are **46 independently bypassable checks**, not 46 implementation
features. Splits are confined to the existing D/S requirements: both readiness
barriers, transient fencing versus durable ownership, the two admission sites,
generation operations, native closure/detach, transcript receipt/recovery and the
explicit transport bounds. No fleet, worktree or container lifecycle guard is added.
A common predicate may cover a data matrix; separate call sites or recovery gates
must not be hidden behind one positive control. No safety-critical guard is excluded.

| Guard | Plan reference and invariant | Control |
|---|---|---|
| G-1 | D-2: shared credential/allowed-runner authentication applies to register and connect. | PC-1 |
| G-2 | D-2/D-5: connection ticket is unexpired, single-use and bound to runner/store/boot. | PC-2 |
| G-3 | D-2: runner finishes adoption before registering. | PC-3 |
| G-4 | D-7: directory dispatch eligibility waits for complete recovery. | PC-4 |
| G-5 | D-5: socket disconnect or expired heartbeat forbids new work without killing children. | PC-5 |
| G-6 | D-4/D-5: competing unexpired boot and incompatible store cannot replace the owner. | PC-6 |
| G-7 | D-3/D-5: stale-epoch response cannot complete a current waiter. | PC-7 |
| G-8 | D-7: event runner/store/epoch provenance must match the DB session owner. | PC-8 |
| G-9 | D-5: reconnect/cancel/timeout never replays a sent mutating RPC. | PC-9 |
| G-10 | D-5: unresolved sent launch keeps its generation reserved until fresh owner evidence. | PC-10 |
| G-11 | D-4: owner fields are all-null legacy or all-present valid binding. | PC-11 |
| G-12 | D-4: transaction commit precedes launch enqueue; rollback has no command. | PC-12 |
| G-13 | D-6: bound operations use persisted owner, never current pin/local fallback. | PC-13 |
| G-14 | D-6: unavailable inventory is unknown, not authoritative absence. | PC-14 |
| G-15 | D-6: all reconciliation passes act only on their owner partition. | PC-15 |
| G-16 | D-6: owner availability/epoch is rechecked before absence writes. | PC-16 |
| G-17 | D-1/D-8: server admission excludes card/task/pool/wrong-kind/backend/profile starts before side effects. | PC-17 |
| G-18 | D-3/D-8: runner admission rejects unsupported operations and non-Grok/cwd/custody payloads independently of server policy. | PC-18 |
| G-19 | D-8: capacity one includes adopted sessions and concurrent reservations. | PC-19 |
| G-20 | D-8: exactly the configured canonical host root maps to `/work`. | PC-20 |
| G-21 | D-8: trusted projection changes only Linux transport fields and retains full rules/reserved identities/local definition. | PC-21 |
| G-22 | D-8: native resume stays strict and same-owner; missing history cannot become fresh launch. | PC-22 |
| G-23 | D-3/D-6: launch accepts only its generation echo. | PC-23 |
| G-24 | D-3: conditional input retains expected generation/sequence and never falls back to raw input. | PC-24 |
| G-25 | D-6: stop uses captured generation, never a newly fetched generation or unconditional kill. | PC-25 |
| G-26 | D-9: dependency closure includes the extensionless Linux apphost. | PC-26 |
| G-27 | D-9: dependency closure includes Linux native PTY assets. | PC-27 |
| G-28 | D-9: shadow copy preserves executable mode. | PC-28 |
| G-29 | D-9: POSIX child creates a new session and fails on detach error. | PC-29 |
| G-30 | D-9: detached child releases all intermediary stdio handles. | PC-30 |
| G-31 | D-9: canceled launch cleans up the actual spawned host. | PC-31 |
| G-32 | D-7/acceptance: receipt requires the complete matching recipient UserPrompt. | PC-32 |
| G-33 | D-5/acceptance: receipt must clear the attempt's transcript floor. | PC-33 |
| G-34 | D-8: ordinary work waits for rules prompt, matching acknowledgement and successful end. | PC-34 |
| G-35 | D-7: live events wait until transcript catch-up commits. | PC-35 |
| G-36 | D-7: replayed transcript UUID/sequence does not duplicate persisted receipt. | PC-36 |
| G-37 | D-7: runner event-hub subscription is bounded and overflow is explicit. | PC-37 |
| G-38 | D-7: server catch-up live buffer is bounded and overflow restarts recovery. | PC-38 |
| G-39 | D-3: shared complete-message UTF-8 bound applies before whole-body allocation on both peers. | PC-39 |
| G-40 | D-3: in-flight request admission is bounded. | PC-40 |
| G-41 | D-3/D-7: runtime/event handlers cannot block the socket response receive loop. | PC-41 |
| G-42 | D-11: remote delivery never borrows local modern PTY ceilings. | PC-42 |
| G-43 | D-6: Linux rows/PIDs never reach local OS census or termination decisions. | PC-43 |
| G-44 | D-5: backend-unavailable queue deferral is uncharged and non-destructive. | PC-44 |
| G-45 | D-8: rules file receipt is validated against the bound owner and expected generation/hash/count before refresh enqueue. | PC-45 |
| G-46 | D-2/D-8: registration secret cannot cross child-environment or public diagnostic boundaries. | PC-46 |

### Positive controls

These are compiling production mutations, not edits to assertions, test fakes or
skip attributes. Code creates the exact methods and named observations specified
here. On the unmodified code each must exercise both refusal and successful recovery
where applicable. Mutation applies one defect, builds, runs only its exact method,
records the indicated assertion red, restores the exact source, forces rebuild and
runs the same method green. A failure before the indicated assertion (including
native launch/fixture setup), zero tests, timeout outside the asserted deadline or
an all-skipped result is not PC evidence. For negative tests, hold every unrelated
gate open; for overflow tests, raise downstream limits so another guard cannot mask
the mutation. Keep a per-PC record of break, red, restore and green after land.

Project abbreviations in this table are command inputs, not wildcard filters:
`T = tests/Antiphon.Tests`, `R = tests/Antiphon.SessionRunner.Tests`,
`P = tests/Antiphon.PtyHost.Tests`. Use the table's exact project/class/method;
for example PC-1 is
`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0490-pc/ -- --treenode-filter '/*/*/PhoneHomeConnectionTests/Authentication_is_required_at_both_endpoints'`.
Do not widen to the class. Fresh TRX and
external evidence paths are additionally required by the commissioning brief.

| Control | Compiling defect, exact method and expected red assertion |
|---|---|
| PC-1 | Bypass the shared registration credential validator. T `PhoneHomeConnectionTests.Authentication_is_required_at_both_endpoints`: `acceptedInvalidCredential.ShouldBeFalse()` fails for register and connect arms; each arm uses the configured runner ID. |
| PC-2 | Return valid from the connection-ticket consumption validator without binding/expiry/used checks. T `PhoneHomeConnectionTests.Tickets_are_bound_expiring_and_single_use`: `invalidTicketConnected.ShouldBeFalse()` fails; valid-secret arms independently vary runner, store, boot, expiry and reuse. |
| PC-3 | Move registration before awaiting runner adoption. R `PhoneHomeConnectionServiceTests.Adoption_precedes_registration`: with adoption held, `registrationRequests.Count.ShouldBe(0)` fails. Release adoption and require exactly one valid registration. |
| PC-4 | Mark directory Ready immediately after socket authentication. T `PhoneHomeConnectionTests.Recovery_barrier_withholds_dispatch`: hold transcript commit, assert `dispatchEligible.ShouldBeFalse()`; mutation makes it true. |
| PC-5 | Remove the connection-liveness eligibility predicate. T `PhoneHomeConnectionTests.Disconnect_and_lease_expiry_refuse_new_work`: fake-time 89s/90s/91s and explicit disconnect arms make `newLaunchFrames.Count.ShouldBe(0)` fail. Child/DB remain alive in the correct build. |
| PC-6 | Accept every authenticated boot/store in connection arbitration. T `PhoneHomeConnectionTests.Live_boot_and_store_identity_cannot_be_replaced`: `replacementAccepted.ShouldBeFalse()` fails for an unexpired competing boot and for wrong store after expiry. Same-store expired boot and same-boot reconnect must succeed. |
| PC-7 | Remove epoch comparison when matching a response to its request waiter. T `PhoneHomeConnectionTests.Old_epoch_reply_cannot_complete_current_request`: reused request ID, old reply first, `currentWaiter.IsCompleted.ShouldBeFalse()` fails; new reply then succeeds. |
| PC-8 | Skip event provenance validation before runtime ingestion. T `PhoneHomeEventPumpTests.Foreign_owner_or_epoch_events_never_reach_runtime`: valid generation but wrong runner/store/epoch arms make `persistedForeignEntries.ShouldBeEmpty()` fail. A current owner event must persist. |
| PC-9 | Copy a sent unresolved mutating request into reconnect's outbound work. T `PhoneHomeConnectionTests.Unanswered_mutation_is_not_replayed`: `peerInputFrames.Count.ShouldBe(1)` fails before any queue recovery is enabled; repeat disconnect/timeout/after-send cancel. |
| PC-10 | Release the accepted launch reservation when its response is lost. T `PhoneHomeStandingLaunchTests.Unknown_launch_blocks_replacement_until_owner_probe`: held owner probe plus second start makes `replacementLaunchFrames.Count.ShouldBe(0)` fail. Original owner list resolves the reservation without duplicate spawn. |
| PC-11 | Remove the new owner all-or-none check constraint from model/migration in a disposable test migration build. T `PhoneHomeStandingLaunchTests.Binding_constraint_rejects_partial_owner`: apply migrations to a new empty task-owned database (not a clone already containing this constraint); direct SQL partial update succeeds, so `partialBindingRejected.ShouldBeTrue()` fails. Test all six partial null combinations and legacy/all-present success. Restore generated files/snapshot before green. |
| PC-12 | Enqueue launch before awaiting the binding transaction commit. T `PhoneHomeStandingLaunchTests.Start_commits_binding_before_remote_launch`: gate commit/force rollback, observe from independent connection, `commandsBeforeCommit.ShouldBeEmpty()` fails. |
| PC-13 | Route a persisted non-null binding through the local default client. T `PhoneHomeSessionRoutingTests.Restart_and_pin_change_keep_persisted_owner`: `localCallsForBoundSession.ShouldBeEmpty()` fails across session operations; do not rely on the local fake throwing in fixture setup. |
| PC-14 | Convert unavailable inventory into `Available([])`. T `PhoneHomeReconciliationTests.Unavailable_owner_does_not_close_rows_or_block_local_scan`: `remoteStatus.ShouldBe(originalRemoteStatus)` fails after grace while local control advances. |
| PC-15 | Remove the owner partition predicate from reconciliation's shared DB selection. T `PhoneHomeReconciliationTests.Every_pass_uses_only_its_owner_partition`: `otherOwnerChanges.ShouldBeEmpty()` fails in failure/readoption/agent-status arms with valid current generations. |
| PC-16 | Omit the availability/epoch recheck immediately before an absence update. T `PhoneHomeReconciliationTests.Disconnect_after_list_blocks_absence_write`: disconnect/change epoch after valid list and missing get, `row.Status.ShouldBe(SessionStatus.Running)` fails. |
| PC-17 | Return allow from the server phone-home admission policy. T `PhoneHomeStandingLaunchTests.Unsupported_start_is_refused_before_reservation`: each R-3 server arm makes `reservationCountDelta.ShouldBe(0)` fail; an injected launch recorder keeps an invalid process from actually spawning. |
| PC-18 | Skip the runner dispatch allow-list/payload validation. R `PhoneHomeCommandDispatcherTests.Unsupported_operation_or_launch_never_enters_runtime`: with server absent, `runtimeMutations.ShouldBeEmpty()` fails for wrong kind/cwd, Herdr, kill-all, unknown operation and verification binding. Also assert capability DTO never advertises Windows custody. |
| PC-19 | Remove the capacity-one reservation check. R `PhoneHomeCommandDispatcherTests.Capacity_counts_adopted_and_concurrent_launches`: adopted occupant and simultaneous starts make `maxOwnedSessions.ShouldBe(1)` fail. |
| PC-20 | Replace exact canonical-root equality with prefix matching. T `PhoneHomeStandingLaunchTests.Only_exact_host_root_maps_to_runner_cwd`: sibling/descendant case makes `outOfRootAccepted.ShouldBeFalse()` fail; baseline `/work` projection succeeds. |
| PC-21 | Skip the trusted final Linux launch projection. T `PhoneHomeStandingLaunchTests.Projection_keeps_identity_rules_and_local_definition`: `remoteSpec.Cwd.ShouldBe("/work")` fails; same method additionally checks executable, callback, home, zero memory, byte-identical rules/args/reserved IDs and unchanged local spec. |
| PC-22 | On missing remote native history, clear resume arguments and take fresh launch. T `PhoneHomeStandingLaunchTests.Resume_never_probes_host_history_or_starts_fresh`: `freshLaunches.ShouldBeEmpty()` fails. Host filesystem probe recorder must stay empty in both builds. |
| PC-23 | Remove generation echo validation from the phone-home response mapper. T `PhoneHomeSessionRoutingTests.Launch_requires_matching_generation_echo`: null/mismatched echo makes `launchAccepted.ShouldBeFalse()` fail; exact echo succeeds. |
| PC-24 | Send raw input after a conditional-input refusal. T `PhoneHomeSessionRoutingTests.Conditional_input_keeps_generation_and_has_no_raw_fallback`: stale-generation/sequence/unsupported/lost-result cases make `rawInputFrames.ShouldBeEmpty()` fail. |
| PC-25 | In remote stop, fetch current runner generation and use it instead of the captured accepted generation. T `PhoneHomeSessionRoutingTests.Stop_never_recaptures_replacement_generation`: replace generation after capture; `replacementKilled.ShouldBeFalse()` fails. |
| PC-26 | Omit extensionless `Antiphon.PtyHost` from `ShadowCopyStore` closure. P `ShadowCopyStoreTests.Linux_closure_keeps_apphost_and_native_library`: `File.Exists(shadowApphost).ShouldBeTrue()` fails after a successful build; minimal deps fixture requires the closure path. |
| PC-27 | Exclude `libporta_pty.so` from that closure. P `ShadowCopyStoreTests.Linux_closure_keeps_apphost_and_native_library`: `File.Exists(shadowNativeLibrary).ShouldBeTrue()` fails. Run separately from PC-26; restore/build between them. |
| PC-28 | Set copied apphost Unix mode to read/write without execute bits. P `ShadowCopyStoreTests.Linux_copy_preserves_execute_mode`: `shadowMode.ShouldBe(sourceMode)` fails on Linux before launch. |
| PC-29 | Replace the POSIX `setsid` call with a successful no-op, keeping error plumbing compilable. P `LinuxPtyHostLauncherTests.Detach_creates_a_new_session`: `hostSessionId.ShouldBe(hostPid)` fails; use a parent whose session ID differs. Separate ordinary fault-injection arm makes a native detach error exit visibly without pipe readiness. |
| PC-30 | Remove child stdio redirection to `/dev/null`. P `LinuxPtyHostLauncherTests.Intermediary_pipes_reach_eof_while_host_lives`: `stdoutAndStderrCompletedWithinFiveSeconds.ShouldBeTrue()` fails while owned host remains alive; also inspect all three descriptors. Teardown uses captured PID/pipe, not the stuck launcher task. |
| PC-31 | Skip `TryKillSpawnedHostAsync` in canceled-launch handling. P `LinuxPtyHostLauncherTests.Canceled_launch_leaves_no_owned_host`: `ownedHostsAfterFiveSeconds.ShouldBeEmpty()` fails, well before launch TTL. Fixture finally kills/joins only its captured process identities. |
| PC-32 | Make the queue's complete-prompt matcher accept a matching head without its tail. T `PhoneHomeQueuedTurnTests.Only_complete_matching_UserPrompt_confirms`: peer emits truncated record above floor, `verifiedReceipt.ShouldBeFalse()` fails. Do not mutate the test's independently recomputed full-body assertion. |
| PC-33 | Drop the sequence-floor check in receipt confirmation. T `PhoneHomeQueuedTurnTests.Receipt_must_be_after_attempt_floor`: full matching stale sequence with otherwise fresh timestamp makes `verifiedReceipt.ShouldBeFalse()` fail. Fresh complete above-floor control passes. |
| PC-34 | Bypass the Grok rules barrier in ordinary queue delivery. T `PhoneHomeQueuedTurnTests.Rules_barrier_requires_prompt_ack_and_successful_end`: emit file receipt only, then partial/incorrect acknowledgement, then failed end; `ordinaryInputFrames.ShouldBeEmpty()` fails before the valid full rules turn. |
| PC-35 | Release buffered live transcript events before backfill commit. T `PhoneHomeEventPumpTests.Catchup_commits_before_live_release`: hold earlier prompt commit and send later end; `liveProcessedBeforeCatchup.ShouldBeFalse()` fails. After release assert complete ordered prompt/text/end in PostgreSQL. |
| PC-36 | On replay, assign fresh UUIDs and new sequences before existing deduplication. T `PhoneHomeEventPumpTests.Replayed_uuid_persists_once`: `persistedPromptCount.ShouldBe(1)` fails with two catch-ups of one observed submission. This avoids a mutation masked by the DB unique constraint. |
| PC-37 | Use an unbounded channel for the phone-home event-hub subscriber. R `PhoneHomeConnectionServiceTests.Hub_overflow_disconnects_for_recovery`: stall downstream consumption and exceed count/bytes independently; `overflowNotified.ShouldBeTrue()` fails. Existing local subscriber behavior is a control. |
| PC-38 | Disable server pending-live-event limit enforcement during catch-up. T `PhoneHomeEventPumpTests.Live_buffer_overflow_withholds_ready_and_recovers`: hold DB catch-up, flood within per-frame limit; `closedForOverflow.ShouldBeTrue()` fails, followed by whole transcript recovery in the restored build. |
| PC-39 | Disable the shared cumulative UTF-8 message-size check. T `PhoneHomeConnectionTests.Fragmented_message_limit_is_enforced_on_both_peers`: default and small-cap peer arms make `oversizedMessageAccepted.ShouldBeFalse()` fail; each peer's test reads complete response/error and verifies no partial transcript acceptance. |
| PC-40 | Bypass in-flight admission when all 32 slots are occupied. T `PhoneHomeConnectionTests.Request_limit_refuses_the_thirty_third_request`: `peerOutstandingRequests.ShouldBe(32)` fails; releasing one permits exactly one later request. |
| PC-41 | Await runtime command execution inline in the socket receive pump. R `PhoneHomeConnectionServiceTests.Held_command_does_not_block_receive_progress`: held launch plus a later heartbeat/cancel/read response makes `receiveProgressBeforeLaunchRelease.ShouldBeTrue()` fail at the bounded barrier observation. |
| PC-42 | Resolve phone-home delivery from local `PtyDeliveryProfile`. T `SessionDeliveryProfileTests.Phone_home_Grok_never_uses_local_modern_evidence`: forced local ModernConPty makes `remoteSingleWriteMaxBytes.ShouldBe(1024)` fail; local profile remains modern. |
| PC-43 | Remove the remote-owner exclusion before local census classification. T `PhoneHomeReconciliationTests.List_consumers_keep_remote_unknown_and_local_pids_separate`: overlapping numeric PIDs make `localDecisionsAttributedToRemote.ShouldBeEmpty()` fail. No real OS kill is used. |
| PC-44 | Charge an attempt when bound runner is unavailable before input. T `PhoneHomeQueuedTurnTests.Offline_deferral_does_not_spend_attempts`: `attemptsAfter.ShouldBe(attemptsBefore)` fails; repeat reconnect then require complete recipient evidence once. |
| PC-45 | Accept runner rules receipt without checking expected rules generation/hash/count. T `PhoneHomeQueuedTurnTests.Rules_receipt_is_owner_bound_and_remote_readable`: wrong generation/hash/count makes `refreshRowsBeforeValidReceipt.ShouldBeEmpty()` fail. Valid `/state/...` receipt passes without host filesystem access and ends in recipient rules evidence. |
| PC-46 | Copy the runner registration secret into the composed child environment. T `PhoneHomeStandingLaunchTests.Secrets_never_cross_child_or_status_boundary`: `childEnvironmentValues.ShouldNotContain(secretSentinel)` fails; ordinary arms also inspect URL/query, captured logs, status DTO and transcript for the sentinel. Use an ephemeral test secret, never a mounted credential. |

G-26/G-27 deliberately have separate PCs targeting distinct assertions in one method.
There is no duplicate G-to-PC mapping. Parameterized cases of one common guard do
not justify class-wide mutation runs. The separate native error/setup tests, version
mismatch, invalid configuration, mount permission and auth-refusal checks remain
ordinary V/R: they do not replace any guard's red/restore/green cycle.

### Out of scope

- No fleet placement, agent CRUD/UI, worktrees, delegated execution, remote hosts,
  SourceLanding product support on Linux, Herdr, custody/cgroup accounting, automatic
  spend/relaunch, image pruning or container replacement. Unsupported entry points
  are negative admission tests, not implementations of those features.
- No general Linux provider or CARD-0038 ceiling/skip audit. Real-model work is one
  Grok standing session and one ordinary queued canary turn after normal rules
  initialization. Busy/recovery permutations use deterministic peers, not more
  billed live turns.
- No client/browser suite, broker or external-channel delivery: no such path changes.
- No guarantee that a detached host survives container replacement. V-1 tests runner
  process restart inside one container/native environment, with init/reaping intact.
- No live production outage, deployment or daemon restart. Fixture crashes affect
  only processes owned by that test. No broad PID enumeration is cleanup authority.
- No full product migration of SourceLanding custody to Linux within this card.
  The native-PC execution seam below must be resolved without silently adding it.

### Cost

All figures below are **estimates**, not measured performance or claimed passing
counts. The full-suite historical 25.5-minute figure is not used. Actual Code and
Mutation reports replace estimates with elapsed times, expanded TRX counts and
failure names. No test run occurred in this TestDesign task.

Ordinary Code floor, sequential on one worker:

| Item | Exact selection / operation | Minutes |
|---|---|---:|
| Setup | Isolated PostgreSQL clone, fixture roots, secret file, approved existing OAuth mount and image prerequisites | 8 |
| Build | Windows builds of T/R/P into `bin-card0490/` | 6 |
| Linux image/build | linux-x64 runner/host publish plus test SDK target with same runtime/native assets, image pinned to Grok 1.0.34 | 12 |
| Unit | T filter `/*/*/*/*[Category=Unit]` | 3 |
| New server integrations | T classes `PhoneHomeConnectionTests`, `PhoneHomeStandingLaunchTests`, `PhoneHomeSessionRoutingTests`, `PhoneHomeReconciliationTests`, `PhoneHomeEventPumpTests`, `PhoneHomeQueuedTurnTests` | 12 |
| Existing server regressions | T classes `AgentSessionLaunchQueueOwnershipTests`, `AgentSessionLaunchFailureTests`, `AgentControlServiceIntegrationTests`, `SessionReconciliationServiceTests`, `SessionRunnerEventPumpTests`, `SessionRunnerGenerationWireTests`, `SessionRunnerCapabilityGateTests`, `GrokRulesQueueBarrierTests`, `GrokRulesReceiptTests`, `SessionMessageQueueInterruptedAttemptTests`, `SessionDeliveryProfileTests`, `ZombieCensusServiceTests` | 18 |
| Runner integrations | R classes `PhoneHomeCommandDispatcherTests`, `PhoneHomeConnectionServiceTests`, `GrokTranscriptTailerTests`; Windows `PtyHostAdoptionTests` | 6 |
| Windows native regressions | P classes `PtyHostLauncherTests`, `HostSessionPipeTests`, `ParentDeathSpikeTests`, `ShadowCopyStoreTests` | 6 |
| Linux native ordinary | P `LinuxPtyHostLauncherTests`, Linux shadow methods; R `LinuxPhoneHomeRunnerTests` | 6 |
| Real canary | V-7 harness, one queued Grok turn plus normal rules initialization and evidence inspection | 8 |
| Cleanup/evidence | Verify TRX selection/counts, sanitize artifact, join owned children and remove inventoried alternate outputs | 3 |
| **Code total** | **Setup/build 26 + ordinary V/R 59 + cleanup 3** | **88** |

Build once per platform/project graph, then use `--no-build` for ordinary selections.
Example concrete command:

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-card0490/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-card0490/ -- --treenode-filter '/*/*/(PhoneHomeConnectionTests*)|(PhoneHomeStandingLaunchTests*)|(PhoneHomeSessionRoutingTests*)|(PhoneHomeReconciliationTests*)|(PhoneHomeEventPumpTests*)|(PhoneHomeQueuedTurnTests*)/*' --report-trx --report-trx-filename phone-home.trx --results-directory .antiphon/card0490-v-new
```

Run the other table rows with a single exact class selector per class, or the same
parenthesized TUnit OR syntax, and a fresh result directory per invocation. Code must
show every expected class/method in the resulting nonzero TRX, including native
Linux methods with no skips. `AgentLaunchSpecTests` is included in Unit. The existing
tailer replay/half-line/exit methods are included by its class selection. Ordinary
Linux execution may use the task-owned SDK image/test target from S5; this permission
does not extend to handing a SourceLanding snapshot to Docker during Mutation.

Mutation floor (no broad suites and no additional live-model turn):

| Item | Included work | Minutes |
|---|---|---:|
| Baseline/setup | Obtain commissioned snapshot/evidence root; build and establish baseline method greens | 8 |
| 42 managed controls | PC-1 through PC-27 and PC-32 through PC-46: 42 x (mutant build 0.75 + exact red 0.25 + restore/rebuild 0.75 + exact green 0.25) | 84 |
| 4 native controls | PC-28 through PC-31: 4 x (mutant build 1 + exact red 1 + restore/rebuild 1 + exact green 1) | 16 |
| Evidence/restoration | Per-PC assertion/TRX review, exact source/index restoration, output inventory and external restoration record | 5 |
| **Mutation total** | **8 + 84 + 16 + 5; includes every PC red/restore/green** | **113** |

**Total verification floor = 88 Code + 113 Mutation = 201 estimated minutes.**
Ordinary Review is a separate caller-commissioned activity; its revalidation time is
not hidden inside these Code/Mutation floors. SourceLanding platform commissioning
is not priced as a software implementation: it is the unresolved execution gate
below, not assumed zero-cost work.

Savings: method-scoped PC red/green executions cost 29 minutes of the table
(42 x 0.5 + 4 x 2). Re-running the estimated 12-minute new-server integration battery
for both phases of 46 PCs would cost 1104 minutes and still miss Linux-specific
proof; method scoping avoids **1075 estimated execution minutes** relative to that
explicit wasteful comparator. Build/restore cost remains paid. No savings are
claimed for batching or parallel shards (0 minutes credited); same-file and
cross-process interference risks make the serial floor reviewable. Unit plus named
affected integrations avoids an unmeasured full assembly run; no fabricated numeric
full-suite saving is claimed. One real turn is mandatory, so live-turn saving is zero.

### Handoff gate and in-scope plan correction

The test design now has bodies read; **guards=46, mapped=46, missing=0,
duplicate PC mappings=0**. All controls have precise methods, compiling defects,
decisive red assertions and priced restoration/green runs. **The Code handoff gate
is not satisfied: PC-28 through PC-31 have no permitted native execution lane under
the current post-land commissioning contract.** The other 42 are designed for the
ordinary local test host; the Linux closure fixtures for PC-26/27 must remain
platform-neutral so they assert file retention on that host without executing ELF.

This is an **in-scope verification seam**, not a request to expand the product.
The plan requires post-land SourceLanding Mutation, and the repository's
`docs/orchestration-loop.md` SourceLanding contract requires local inherited
execution and forbids giving the snapshot to an external executor, broker, remote
service or pre-existing process. The planned desktop Linux container is launched
by a pre-existing Docker daemon. A Windows sourced snapshot cannot be mounted or
copied into that daemon to run the native PCs under that contract. A Windows-only
test, an all-skipped Linux test, a build, an ordinary pre-land native green, or an
unsourced post-land checkout does not prove the requested sourced red/green cycle.

**Next: Plan**, limited to resolving and documenting an authorized native-PC
execution mechanism consistent with local inherited custody, or obtaining an
explicit commissioning-contract decision. Do not silently add Linux SourceLanding
product support, use WSL/Docker as an unreviewed escape, waive these four controls,
or mark the design executable end-to-end. After that seam is resolved, TestDesign
checks only the execution amendment and native cost before `next: code`; all V/R
and guard definitions above remain the bounded first-slice design. The later
multi-agent/worktree/remote-host features remain follow-up-card scope.

## Native-PC execution amendment (3f77b203)

This section supersedes the preceding unresolved native execution gate and its
native cost assumptions. The TestDesign appendix above, including all 46 guard
rows, all 46 PC rows, their assertions and V/R selections, is retained unchanged.
Next is a narrow **TestDesign** check of this amendment and its costs, then Code.
The executable harness described here is Code work, not an existing command.

### Ground truth for the execution decision

| Assumption | Inspected behavior at `2be56d1a` | Decision / limit |
|---|---|---|
| Fresh Testcontainers means a fresh inherited executor. | `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs`, `TestDbOperations.CreateAsync/StartAsync/DisposeOwnedAsync`, creates and disposes `postgres:16-alpine` through Testcontainers. It has no repository bind/resource mapping and does not spawn a daemon. | Precedent for a task-owned data dependency, not for moving snapshot execution to its daemon. Retain the existing DB fixture for the other PCs; no DB is needed by these four. |
| Existing SourceLanding runs establish a Docker exception. | No such exception or execution recipe was found in the custody owners, CARD-0478 records or fixture implementation. The dated CARD-0552 investigation recorded zero admitted/completed sourced Mutations on this board as of September 17. That is historical evidence, not a current fleet census. | Do not invent a precedent or infer permission from earlier unsourced batteries. |
| A new Linux runner can own this Mutation. | `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`, `VerificationCustodyBackend`, advertises custody only for Windows ModernConPty. The SourceLanding owner requires fresh Worker/Mutation/Worktree and inherited execution. | Keep the existing Windows worker/binding. No Linux SourceLanding feature. |
| A child that detaches can leave the Windows task's custody. | `ModernConPtyConnection.Spawn` checks atomic job membership before resume and requires exactly `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, without either breakaway flag. `PtyAgentRunner.SealAndObserveCustodyAsync` reads the original job, then drains and rechecks. `PtyCustodyTests.C478_G198_NonemptyJob` tests descendants surviving root exit. | Spawn the entire Linux machine as one inherited Windows process; qualify that exact process under this job. Guest PIDs are diagnostics, not Windows cleanup authority. |
| Docker/native tool prerequisites are already sufficient. | No `qemu-system-x86_64.exe` or `qemu-img.exe` resolved on this task's PATH. No QEMU fixture or pinned guest asset is present in the inspected plan/base. | Code must provision and record the two tools and source-free guest asset, then qualify them; this Plan has not run them. |
| PC-28 can run on a host-mounted Windows directory. | The assertion compares real Unix mode bits; the other three methods depend on Linux sessions, descriptors and process cleanup. | Build, shadow-copy and run on the guest's ext4 filesystem, never on FAT/NTFS/9p. FAT is only a read-only archive transport. |

Testcontainers explicitly connects to a Docker-compatible runtime/endpoint; it
does not make that daemon a descendant of the test process.
[Testcontainers configuration](https://dotnet.testcontainers.org/custom_configuration/).
QEMU documents TCG on Windows and full guest CPU/memory/device emulation, while
also identifying optional external offloads that this recipe excludes.
[QEMU execution model](https://www.qemu.org/docs/master/system/introduction.html).

### Bounded additions to S1/S5

All paths below marked new are test infrastructure, shipped and reviewed with the
original Code task before its land. No product runner or custody source is changed
to permit this lane.

| Slice | Files | Required ordinary checks |
|---|---|---|
| S1-E: reproducible native inputs | **New** `tests/fixtures/card0490-linux/README.md`, `guest-init.sh`, `run-one.sh`, `assets.lock.json`; **new** `scripts/prepare-card0490-linux-assets.ps1`; `.gitattributes` for just these shell files | Pin QEMU binary bundle, boot disk, distribution/kernel, SDK `10.0.204`, net9 runtime, native libraries and offline NuGet inputs by version/hash. Boot disk contains tools/dependencies and the pinned bootstrap unit only, no application source, compiled Antiphon assemblies, credentials or saved VM memory. Align guest distribution/user/native dependencies with S5's Linux image and record the comparison. Pin shell inputs to LF so packaging preserves working-file bytes. Preparation runs before sourced commissioning. |
| S1-E: foreground host wrapper | **New** `scripts/test-card0490-native.ps1` | One PC/phase per invocation; literal allow-list of the four method/project pairs; hashes and exact source inventory; immutable input staging; inherited `ProcessStartInfo.ArgumentList` launches; concurrent stdout/stderr drain; join on every exit. A private asset profile supplies absolute paths matching the tracked lock. No arbitrary command endpoint. |
| S1-E: wrapper and guest qualification | **New** `tests/Antiphon.PtyHost.Tests/Card0490NativeExecutionTests.cs`; **new** `tests/Antiphon.Agents.Pty.Tests/Card0490NativeCustodyTests.cs` | `Wrong_method_or_input_digest_is_rejected`, `Missing_or_skipped_or_wrong_method_result_is_rejected`, `Exact_method_result_survives_guest_shutdown`; custody `Qemu_remains_accounted_after_wrapper_exit` and `Guest_shutdown_allows_original_job_zero_and_drain`. Use the existing custody test helpers and assembly-local process limiter. |
| S5-E: operational recipe | `docs/testing-and-build.md` and fixture README above | Document only CARD-0490's four-control recipe, asset preparation, exact invocation, result inspection and restoration. Preserve the general SourceLanding prohibitions and D-10's product compose lane. |

The new methods validate the execution fixture, not additional phone-home product
guards. No G/PC renumbering or added product behavior is intended. TestDesign must
check these ordinary fixture checks without reopening the other 42 controls.

### Exact local execution contract

1. **Commission and bind.** The caller uses the existing post-land companion,
   confirmed O/L and fresh Worker/Mutation/Worktree. Before any mutant the delegate
   validates HEAD=L, creation ID, clean tracked/index bytes, launch binding and
   external evidence root E from its brief. No new worktree, branch or Linux agent
   is created. Check the prequalified asset lock and source-free disk hashes.
   Missing assets/custody are an infrastructure refusal, not permission to fall
   back to Docker/WSL. Do not install or download prerequisites in the snapshot.
2. **Freeze and package one phase.** Under E, create
   `card0490-native/<PC>/<baseline|red|green>/<run-id>/` with `input`, logs and
   a task-owned overlay. Package the current tracked working-file bytes, not just
   `git archive HEAD` (which would lose the mutant), using the Git tracked-path
   inventory. Exclude `.git`, ignored outputs and untracked files; reject path
   escapes/reparse points. Preserve Git executable modes in `source.tar`; source
   and entrypoint hashes, L/O/task/creation, PC, phase and exact method go in
   `manifest.json`. Record the intended production diff separately. Those two
   ASCII-named files are the entire immutable input directory. Check per-file
   hashes again before/after the run. Do not edit snapshot or staging while it runs.
3. **Start a new machine as a child.** The foreground wrapper starts and awaits
   `qemu-img.exe create -f qcow2 -F qcow2 -b <absolute-base> <absolute-overlay>`.
   Then it starts `qemu-system-x86_64.exe` directly with `UseShellExecute=false`,
   no breakaway/elevation/service dispatch, redirected streams, and these fixed
   argument/value pairs (paths are separate arguments or JSON strings, never shell
   interpolation):

   ```text
   -no-user-config -nodefaults
   -machine q35 -accel tcg,thread=multi -cpu max -smp 2 -m 4096
   -display none -monitor none -nic none -no-reboot
   -chardev stdio,id=pcio,signal=off -serial chardev:pcio
   -blockdev <root-json> -device virtio-blk-pci,drive=pcroot,bootindex=1
   -blockdev <input-json> -device virtio-blk-pci,drive=pcinput
   ```

   `root-json` is the serialized object
   `{"driver":"qcow2","node-name":"pcroot","file":{"driver":"file","filename":"<absolute-overlay>"}}`.
   `input-json` is
   `{"driver":"vvfat","node-name":"pcinput","dir":"<absolute-input>","fat-type":32,"label":"C0490INPUT","rw":false,"read-only":true}`.
   The pinned boot disk boots via its installed BIOS bootloader. Use no
   `-daemonize`, `-incoming`, `-loadvm`, networking, shared host filesystem, socket,
   disk service, vhost backend, external emulator helper or accelerator fallback.
   QEMU and qemu-img are fresh locally inherited processes; a pre-existing **file**
   containing an inert toolchain is not a pre-existing executor.

   These option families and read-only FAT are documented by
   [QEMU invocation](https://www.qemu.org/docs/master/system/invocation.html),
   [block options](https://www.qemu.org/docs/master/interop/qemu-qmp-ref.html),
   [disk images](https://www.qemu.org/docs/master/system/images.html#virtual-fat-disk-images)
   and [qemu-img](https://www.qemu.org/docs/master/tools/qemu-img.html).
   Code pins a tested QEMU release instead of depending on the moving master docs.
4. **Run only the selected method.** Each boot gets a fresh init/reaper and guest
   boot ID. Its one-shot fixture unit mounts `C0490INPUT` read-only, verifies hashes,
   and extracts onto a new ext4 directory `/work/card0490`. It runs the packaged
   `run-one.sh` under the S5-equivalent non-root UID with isolated HOME/TMPDIR/state.
   Use offline NuGet inputs; missing packages refuse the run. Build fresh Linux
   binaries in the guest from those bytes, including the real apphost/native PTY
   closure. Do not reuse a Windows publish, precompiled application or prior
   phase's `bin`/`obj`. Pass the validated manifest L as MSBuild `SourceRevisionId`
   (the existing `Directory.Build.props` otherwise falls back to unknown without
   Git metadata); the per-file hashes additionally identify the mutant. The guest
   checkout is disposable test input without Git
   metadata, not another managed/unbound SourceLanding worktree.

   Example PC-29 invocation inside that directory (manifest selects an allow-listed
   literal filter, not shell text):

   ```sh
   dotnet restore tests/Antiphon.PtyHost.Tests --source /opt/nuget-offline
   dotnet run --project tests/Antiphon.PtyHost.Tests --no-restore --property:SourceRevisionId="$landed_sha" --property:OutputPath=bin-card0490-native/ -- --treenode-filter '/*/*/LinuxPtyHostLauncherTests/Detach_creates_a_new_session' --report-trx --report-trx-filename result.trx --results-directory /results
   ```

   The remaining filters are exactly `/*/*/ShadowCopyStoreTests/Linux_copy_preserves_execute_mode`,
   `/*/*/LinuxPtyHostLauncherTests/Intermediary_pipes_reach_eof_while_host_lives`, and
   `/*/*/LinuxPtyHostLauncherTests/Canceled_launch_leaves_no_owned_host` for PC-28,
   PC-30 and PC-31. No class/assembly selector or all-PC batch is allowed.
5. **Return evidence and stop.** The unit retains the native `dotnet` exit code,
   TRX, build/test logs, actual selected case/count/outcome, kernel/SDK/native asset
   identity, boot ID, and source/test/output hashes. It emits these over the
   process-owned serial stream as nonce/length/hash-delimited base64 file frames;
   the wrapper records raw streams and validates complete frames into E. Guest
   shutdown happens only after the method's `finally` has joined its captured
   host/child identities. Init reaps orphans. The wrapper waits for QEMU exit and
   both stream drains before returning. QEMU exit zero alone is never test success.
   Missing/truncated evidence, boot/build failure, zero cases, skip, wrong method,
   or a failure other than the prescribed assertion is infrastructure/invalid-PC
   evidence. On cancellation the wrapper stops only its owned QEMU handle and
   joins/drains it; it reports incomplete, never an inferred red or green.
6. **Repeat with restoration.** Run baseline green first for each exact method;
   apply its one planned mutation in the sourced tree; export/build/run red in a
   new VM; restore exact tracked/index bytes and refresh timestamps; export/build/
   run green in another new VM. PC-28 through PC-31 run serially. Restored-green
   input hashes must equal baseline's; only the prescribed production files may
   differ for red. The checked-in test/harness bytes stay unchanged. Preserve the
   PC-specific assertion from the matrix, including its existing five-second
   deadlines. TCG slowness never authorizes a wider assertion or a substitute
   timeout failure. Boot/build budgets are separate from assertion deadlines.
7. **Restore and retain.** Delete only exactly inventoried, joined phase overlays
   and disposable input archives after retaining manifests/diffs/hashes/results.
   Keep retained files under E; never publish source to an external executor.
   Restore the sole managed snapshot, remove its precisely inventoried alternate
   outputs, and write the existing restoration record. No commit/push or runtime
   receipt fabrication. The caller later seals the full task launch set and uses
   the existing original Windows job's zero/drained receipt for CleanupVerification.
   An unknown or surviving process remains residue; guest evidence cannot waive it.

The proposed host command is:

```powershell
pwsh -NoProfile -File scripts/test-card0490-native.ps1 -SnapshotRoot '<managed-worktree>' -BindingFile '<external-commissioning-json>' -AssetProfile '<prequalified-local-profile>' -Pc PC-29 -Phase red -EvidenceRoot '<assigned-E>'
```

`BindingFile` records O/L/task/creation and the accepted execution binding from the
commissioning evidence; it is an input cross-check, not newly minted runtime proof.
The wrapper does not perform mutations, restore files, commit, dispatch agents or
operate a production session. The Mutation delegate owns those separate phase steps.
For pre-land Code/Review, a separate explicit `-Ordinary` parameter set omits
`BindingFile`, permits only `-Phase baseline`, and records the ordinary source SHA
with no sourced/custody claim. It uses identical packaging, QEMU arguments, build
and result validation. There is no automatic downgrade from a failed sourced
preflight to this mode; ordinary output cannot satisfy the companion's PC evidence.

### Verification design addition: qualify the execution boundary before land

Ordinary Code must demonstrate the same QEMU/toolchain/profile recipe first on its
ordinary worktree. Run each of the four exact methods green with real Linux cases,
and retain expanded counts, original deadlines, image/input hashes and timings.
The S5 Docker canary and Linux adoption tests still run as designed; a QEMU native
green does not replace the one real Grok transcript-confirmed turn.

Use the existing Windows custody fixture for the new QEMU-specific ordinary tests:
launch a wrapper under `PtyAgentRunner.StartTrackedAsync`/ModernConPty, have its
fresh QEMU guest reach a pipe/serial barrier, then let that wrapper exit while
QEMU remains alive. The original job must still report nonzero and not drained.
Release the guest through a test-owned inherited pipe, await QEMU shutdown, and
require the same job to reach zero with drained output. Exercise abrupt owned
QEMU stop too, with no success verdict from partial results. This proves containment
of this dependency without extending custody into Linux or forging a SourceLanding
receipt. Run the Pty assembly sequentially with the other process-spawning suites.

The fixture qualification also exercises a wrong method, tampered source digest,
missing result, skipped result and truncated final frame: each must be rejected.
Use fixture outputs for these negative harness cases, not production mutations.
The canonical PC-28 through PC-31 assertions remain the only four product controls.
Code and Review must reject an unqualified helper or a silent accelerator/runtime
fallback before land. Actual PCs remain pending until the post-land sourced run.

### Revised native cost and handoff

These are explicit **unmeasured estimates** replacing the native assumptions in
the frozen Cost section. TCG is slower than the original container estimate; Code
must measure the exact recipe and TestDesign/Review must retain any higher floor.
Provisioning packages/binaries is assumed available without operator credentials;
asset download delay and implementation authoring remain separately reported.

| Stage addition/replacement | Calculation | Minutes |
|---|---|---:|
| Code: pin/prepare source-free tools, guest and offline packages | Additional setup beyond the existing S5 image | 20 |
| Code: four exact greens plus wrapper/custody qualification | 24 native baseline minutes (as below) + 16 harness/custody minutes, additional to existing V/R | 40 |
| **Revised Code floor** | **88 + 20 + 40** | **148** |
| Mutation: native asset/source/identity preflight | Additional to the existing 8-minute managed setup | 12 |
| Mutation: four native baseline greens, fresh VM each | 4 x (boot 1 + build 3 + method 1 + drain/export 1) | 24 |
| Mutation: four native red/restore/green cycles | 4 x 2 x (boot 2 + fresh build 4 + method 1 + drain/export 1); replaces original 16 | 64 |
| **Revised Mutation floor** | **8 existing setup + 84 managed PCs + 12 + 24 + 64 + 5 restoration** | **197** |
| **Revised combined floor** | **148 Code + 197 Mutation; ordinary Review and authoring separate** | **345** |

No parallelization saving is assumed. If TCG cannot keep an unmutated native test
inside its existing five-second assertion, record the measured failing boundary
and return it for an execution-plan correction; do not weaken the test or claim
the native-PC obligation is satisfied. All 46 PCs remain mandatory.

Amendment validation: inspected owners/source and QEMU/Testcontainers primary
documentation; checked the unchanged verification appendix and one-to-one 46/46
mapping. No build, VM, container, provider turn, custody qualification or PC was
executed by this Plan task. The requested branch checkout was refused because
`feat/card-task-2dbc8291` was already held by its own worktree; this amendment uses
the confirmed `2be56d1a` base in this task's worktree and publishes to that requested
remote branch without disturbing the other worktree.

**Next: test-design.** Check only D-12, S1-E/S5-E, the inherited-QEMU qualification,
source/evidence binding and revised native costs. Then hand Code this exact recipe
and the unchanged 46-control matrix. No commissioning-contract exception or Linux
SourceLanding product work is requested.

## Verification design: D-12 validation (9b33195a)

**Disposition: the local QEMU/TCG approach is admissible in principle, but the
amendment is not yet ready for Code.** This is a focused review of `a67f0636`, not
a new review of D-1 through D-11 or the original 46-control matrix. All text above
this section is preserved. The requested checkout was attempted first after reading
the brief; Git refused because `feat/card-task-3f77b203` belongs to another worktree.
This task instead checked out its own `feat/card-task-9b33195a` at `a67f0636` and
confirmed that exact HEAD before inspection.

Two findings prevent the requested Code handoff:

1. **F-D12-1: the ordinary custody qualification has an unspecified release
   channel after root exit.** The new test must let its tracked wrapper exit while
   QEMU remains alive, observe the original job as nonzero/not drained, then release
   the guest through a test-owned inherited pipe. The specified QEMU serial endpoint
   is the wrapper's redirected stdio. `ModernConPtyConnection.Spawn` passes
   `bInheritHandles=false`; `PtyAgentRunner.HandleExit` closes tracked input, and
   `SealAndObserveCustodyAsync` also seals input before querying the job. Therefore
   `runner.WriteAsync` cannot be that release channel, and an arbitrary fixture pipe
   does not reach the tracked wrapper merely by existing before `StartTrackedAsync`.
   The nearest fixture uses Windows named events opened by its child, not an
   inherited serial pipe. D-12 needs an explicit ownership/handle-transfer sequence
   identifying the surviving reader, writer and drain owner, with joined teardown
   after both normal release and abrupt QEMU stop. This is missing fixture design,
   not proof that inherited QEMU is impossible. No product custody change or
   commissioning exception is required or authorized by this finding.
2. **F-D12-2: the new evidence and execution guards have ordinary negative cases
   but no positive-control design.** The stage contract says, "Every guard that
   protects a safety-critical assertion gets a PC-n positive control." A helper
   which accepts the wrong source, wrong method, partial results or unjoined
   execution can falsely certify PC-28 through PC-31. Calling it test infrastructure
   does not remove that obligation. The amendment expressly supplies only ordinary
   fixture-output checks and preserves the four product mutations. Keep the existing
   46 rows unchanged, but specify separate amendment controls, exact methods,
   compiling defects, intended red assertions and restore/green costs for the
   independently bypassable helper guards. An ordinary corrupt-input test is not
   itself evidence that disabling its validator turns that test red.

### Inspection

- `ShadowCopyStoreTests.cs` in full, including `CreateFixture` and cleanup;
  `PtyHostLauncherTests.cs` in full, including cancellation, PID discovery and
  teardown; `PipeTestClient.cs` in full | real modes, detach, pipe EOF and canceled
  launch -> inherited PC-28 through PC-31 and V-D12-1/R-D12-1 below. The existing
  launcher tests skip Linux and cannot qualify these native controls.
- `PtyCustodyTests.cs` in full, including `C478_G198_NonemptyJob`,
  `Output_drain_cancellation_never_returns_an_exit_observation`,
  `Tracked_runner_seals_drains_and_observes_original_job`, `NativeProbe`, `Journal`,
  `WaitCountAsync` and `RequireModern`; `Antiphon.CustodyTestChild/Program.cs` in
  full | root exit, live descendants, real original-job queries, output drain,
  named-event release and joined cleanup -> V-D12-3/R-D12-3 and F-D12-1. Do not
  carry the existing child's 60-second self-release into a guest boot/build barrier.
- `PtyAgentRunner.LaunchCoreAsync` launch/exit handling and
  `SealAndObserveCustodyAsync`/`DrainCustodyOutputAsync`/`WriteCoreAsync`;
  `ModernConPtyConnection.Spawn` creation, containment and resume bodies |
  inherited local execution is supported; arbitrary pipe inheritance and input
  after root exit are not supported by those APIs -> F-D12-1.
- `ShadowCopyStore` copy/closure and `PtyHostLauncher` launch/cancel bodies;
  `Directory.Build.props`, `global.json`, `.gitattributes`, both affected test
  project files and their `ProcessSpawnLimit.cs` bodies | ELF closure, explicit
  `SourceRevisionId`, SDK 10.0.204, net9 runtime, LF scripts, producer-owned output
  and sequential native test execution -> V-D12-1/R-D12-1.
- Nearest available script/result fixture bodies:
  `tests/Antiphon.SessionRunner.Tests/Fixtures/RunnerRestart/platform.ps1` and
  `scripts/fixtures/nightly/c487-probe/Probe.cs`, `Probe.csproj`, with the beginning
  and test-definition records of `all.trx` | injected platform failures and actual
  expanded TUnit result identity -> V-D12-2/R-D12-2. Neither fixture supplies a
  QEMU boot disk, serial exporter, wrapper, or a custody release channel.
- `docs/testing-and-build.md` mutation/restoration and filter/output rules;
  `docs/orchestration-loop.md` SourceLanding and cleanup contract; native custody
  portions of the runtime/ConPTY owners | no snapshot execution by a pre-existing
  daemon; no forged receipt or worker-led snapshot cleanup -> V-D12-3/R-D12-3.

No D-12 script, guest asset lock, guest init/exporter or new qualification test
exists at this base; neither QEMU executable resolves on this task's PATH. This is
recorded missing Code setup, not an executed-test failure. The absent fixture
ownership sequence in F-D12-1 is the design gap to settle before that Code work.

The documented command families are consistent with QEMU's primary documentation:
TCG is available on Windows and emulates the machine; optional external device
offloads must remain excluded. This supports, but does not experimentally prove,
the custody inference. [QEMU execution model](https://www.qemu.org/docs/master/system/introduction.html).
The proposed stdio chardev and explicit accelerator selection have documented
forms. [QEMU invocation](https://www.qemu.org/docs/master/system/invocation.html).
The vvfat object supports `dir`, FAT32, the nine-byte label and `rw=false`;
`qemu-img create -f qcow2 -F qcow2 -b` supports the new overlay. ASCII transport
filenames and an unchanged host input directory are appropriate restrictions.
[QMP block options](https://www.qemu.org/docs/master/interop/qemu-qmp-ref.html#object-BlockdevOptionsVVFAT),
[qemu-img](https://www.qemu.org/docs/master/tools/qemu-img.html),
[virtual FAT restrictions](https://www.qemu.org/docs/master/system/images.html#virtual-fat-disk-images).
These moving documents are not a qualified binary/version/hash or measured boot.

### Delivery inventory

D-12 adds evidence delivery, not a new session-input or business-queue path. Its
durable identity must connect `(O, L, task, creation, PC, phase, run ID,
manifest/source digest, exact method)` from manifest to the accepted files under E.
The serial nonce must be bound to that same run, not merely appear on a frame.
Guest boot ID and asset hashes corroborate the execution; they do not replace
source identity or the original Windows job's receipt.

| Producer -> destination | Persistence boundary | Recovery and decisive recipient evidence |
|---|---|---|
| Tracked working-file packager -> fresh guest | Immutable input under E; guest extraction to new ext4 state | Refuse changed/foreign inputs before accepting execution; retain manifest/diff/hash evidence. Guest-reported per-file inventory must match the phase's input, including the intended mutant rather than HEAD-only archive bytes. |
| Guest test/exporter -> wrapper through QEMU stdio | Guest TRX/log files are disposable until complete, bound frames have been validated and written under E | Guest crash before export, mid-frame truncation, wrapper write failure and wrapper crash before persistence all leave an incomplete run. A new phase attempt uses a new run directory; it cannot borrow the old attempt's TRX or infer a verdict from QEMU exit. |
| Wrapper -> evidence reader/Mutation report | Complete matching files under E, followed by process exit and both stream drains | Read the persisted files back after QEMU shutdown. Require actual selected method, every expected case, outcome/decisive assertion and hashes, with joined execution. A valid frame before a failed evidence write, or persisted files before an unresolved join, is not an accepted completed run. |
| Original Windows job -> existing cleanup authority | Existing native zero/drained receipt for the sealed launch set | No new receipt format/path is introduced. Unknown or surviving execution remains residue. Guest shutdown, wrapper status and file restoration cannot substitute for the original receipt. |

V-D12-2 must exercise the real guest/exporter/stdio/parser/file-reader path with
both an immediately reading recipient and a deliberately backpressured recipient,
then inspect persisted complete files after drain. Fixture-supplied TRX/frame bytes
cover rejection logic only; they cannot prove actual Linux execution, serial
delivery, custody or exact source use. Inject a failure at each persistence/handoff
cut above, including after complete-frame parsing but before E write and after E
write but before join. None may produce accepted red/green evidence. F-D12-2 records
that these delivery/recovery validators need their own controls.

The original real queue/socket/catch-up tests and busy/already-eligible recipient
matrix remain unchanged. A QEMU native pass supplies no UserPrompt evidence; the
matching complete recipient UserPrompt and real Grok turn remain separate required
acceptance. This review does not re-audit those previous TestDesign bodies.

### Proves it works now

These are ordinary Code obligations, not results obtained by this TestDesign task.

- V-D12-1: actual Linux execution of each original native method | P, fresh QEMU
  guest | D-12's `-Ordinary -Phase baseline` path, selecting PC-28, PC-29, PC-30 and
  PC-31 separately | four non-skipped exact-method results, real ELF/native assets,
  ext4 modes and the original assertions/deadlines; retain actual expanded counts.
  The guest restore/run command and four literal filters above are retained.
- V-D12-2: wrapper acceptance and recipient evidence | P plus the ordinary QEMU
  fixture | `Card0490NativeExecutionTests.Wrong_method_or_input_digest_is_rejected`,
  `Missing_or_skipped_or_wrong_method_result_is_rejected`, and
  `Exact_method_result_survives_guest_shutdown` | rejected invalid inputs/results;
  complete source-bound persisted evidence on both immediate and backpressured
  reads; invalid/incomplete rather than success at every handoff failure above.
- V-D12-3: real custody on the selected dependency | A, sequential Windows ModernConPty
  fixture | `Card0490NativeCustodyTests.Qemu_remains_accounted_after_wrapper_exit`
  and `Guest_shutdown_allows_original_job_zero_and_drain` | original job remains
  nonzero/not drained while QEMU survives root exit, then becomes zero/drained
  after explicitly owned release and shutdown. Abrupt owned stop must join but
  reject partial test evidence. F-D12-1 prevents declaring this recipe executable.

Here P is `tests/Antiphon.PtyHost.Tests`; A is
`tests/Antiphon.Agents.Pty.Tests`. Ordinary host invocations use `dotnet run`,
`--property:OutputPath=bin-card0490/`, one listed class filter and fresh TRX output;
P and A run sequentially with their assembly-local process limiter. Required native
qualification must fail qualification on a skip, even though `RequireModern` in
the nearest existing fixture normally uses a skip for missing prerequisites.

### Guards the regression

- R-D12-1: source/mode identity and phase isolation | V-D12-1 plus the native phase
  manifests | baseline and restored-green input inventories match exactly; only
  the declared production diff differs for red; test/harness bytes remain fixed
  during PC-28 through PC-31; no host-mounted build, prior phase output or timeout
  substitution can satisfy their original assertions.
- R-D12-2: false red/green from bad evidence | V-D12-2 | separately exercise wrong
  method, source digest, run identity, frame length/hash, missing TRX, zero cases,
  skipped cases, wrong assertion, final-frame truncation, write failure and pending
  stream drain. Keep all unrelated fields valid in each rejection case; combine
  backpressure with truncation/write failure as well as with a valid result.
- R-D12-3: root exit mistaken for cleanup authority | V-D12-3 | capture the original
  job identity, observe real nonzero accounting after root exit, reject partial
  evidence after cancellation, retain unknown residue and join every owned helper.
  The release cannot depend on reopening the sealed tracked input gate.

### Guard inventory

The original product inventory remains **guards=46, mapped=46, missing=0,
duplicate PC mappings=0**. In this amendment's native subset, G-28 -> PC-28,
G-29 -> PC-29, G-30 -> PC-30 and G-31 -> PC-31 remain unchanged. This is not a claim
that every guard introduced by D-12 is covered by those four mappings.

The amendment also promises the following independently bypassable safety checks
without assigning additional controls. They are inventoried here as findings,
not silently excluded or folded into a product PC:

| Missing amendment control | Plan boundary requiring a distinct PC |
|---|---|
| M-1 | Exact method/project allow-list before execution. |
| M-2 | Commissioned identity/source binding accepted before sourced execution; independently varied tuple fields. |
| M-3 | Failed sourced preflight cannot downgrade to ordinary mode. |
| M-4 | Source/input/evidence path containment and reparse rejection. |
| M-5 | Current tracked working bytes, including the mutant, rather than HEAD-only or untracked/output inputs. |
| M-6 | Input hashes checked before launch. |
| M-7 | Input hashes checked again after execution. |
| M-8 | Pinned executable/toolchain/disk identity accepted before launch. |
| M-9 | Fresh locally inherited launch with the fixed TCG/offline/no-external-backend argument contract. |
| M-10 | Fresh ext4 extraction/build with no previous application output or phase state. |
| M-11 | Result run/source identity matches the accepted manifest. |
| M-12 | Complete frame length before result acceptance. |
| M-13 | Correct frame digest before result acceptance. |
| M-14 | Actual result method matches the selected exact method. |
| M-15 | Nonzero expected cases and no missing/skipped cases. |
| M-16 | Red comes from the prescribed assertion, not a build/fixture/other failure. |
| M-17 | Evidence persistence failure cannot return an accepted result. |
| M-18 | Process exit and both stream drains precede completed-run acceptance. |
| M-19 | Cancellation stops/joins only owned execution and cannot certify partial results. |
| M-20 | Output deletion is restricted to inventoried, joined owned artifacts. |
| M-21 | Restored-green source equality and unchanged test/harness bytes across a product PC cycle. |

This is a minimum of **21 additional unmapped amendment guards**; M-2/M-4/M-8/M-9
must be split further if their implementation uses independently bypassable checks
or call sites. No waiver is justified. The amended design therefore fails the
required missing=0 audit despite retaining the original 46/46 matrix. Assigning
PC numbers without a defect, exact red method and executable fixture would not
repair that failure.

### Positive controls

The four native product controls remain exactly the previously specified ones:

- PC-28 breaks G-28 by clearing the copied apphost's execute bits; expect
  `ShadowCopyStoreTests.Linux_copy_preserves_execute_mode` red at
  `shadowMode.ShouldBe(sourceMode)`.
- PC-29 breaks G-29 by replacing `setsid` with a successful no-op; expect
  `LinuxPtyHostLauncherTests.Detach_creates_a_new_session` red at
  `hostSessionId.ShouldBe(hostPid)`, with a distinct parent session ID.
- PC-30 breaks G-30 by omitting child stdio redirection; expect
  `LinuxPtyHostLauncherTests.Intermediary_pipes_reach_eof_while_host_lives` red at
  `stdoutAndStderrCompletedWithinFiveSeconds.ShouldBeTrue()` while the captured
  host still lives.
- PC-31 breaks G-31 by skipping canceled-launch host cleanup; expect
  `LinuxPtyHostLauncherTests.Canceled_launch_leaves_no_owned_host` red at
  `ownedHostsAfterFiveSeconds.ShouldBeEmpty()`, before the launch TTL.

Each uses its literal method filter from D-12 and a fresh VM for baseline, red and
restored green. Code implements tests and runs ordinary V/R; ordinary Review
judges them before land; SourceLanding Mutation records break, intended assertion
red, exact restore and fresh green after land. None ran here. The 21 amendment
control omissions above are F-D12-2, not covered by repeating these four recipes.

### Out of scope

- Product D-1 through D-11, the other 42 controls, queue behavior and Grok canary
  redesign: explicitly excluded by this dispatch; their existing requirements stay.
- Product Linux SourceLanding support, new custody authority or exceptions allowing
  Docker/WSL/remote snapshot execution: unnecessary to the proposed local QEMU lane.
- Building assets, launching QEMU, running product tests or actual PCs: Code and
  post-land Mutation work; the current task validates the design and records gaps.
- A process-list-only substitute for native job accounting, synthetic-only result
  delivery or widened five-second assertions: excluded because each loses the
  decisive evidence that the amendment is meant to supply.

### Cost

The amendment's arithmetic is correct, and all these figures remain **estimated**:

| Priced obligation | Minutes |
|---|---:|
| Code existing setup/build 26 + new asset preparation 20 | 46 |
| Code existing V/R 59 + four QEMU greens 24 + ordinary helper/custody qualification 16 | 99 |
| Code cleanup/evidence | 3 |
| **Priced Code floor** | **148** |
| Mutation existing baseline/setup 8 + new preflight 12 + four native baselines 24 | 44 |
| Mutation 42 managed red/restore/green cycles | 84 |
| Mutation four native red/restore/green cycles: 4 x 2 x (2 boot + 4 build + 1 method + 1 export/drain) | 64 |
| Mutation restoration/evidence | 5 |
| **Priced Mutation floor** | **197** |
| **Priced combined subtotal** | **345** |

Thus 148 + 197 = 345, and the replacement adds 144 minutes to the prior 201-minute
estimate: 60 Code and 84 Mutation. The native red/green row pays for all eight
fresh boots/builds, rather than merely eight one-minute test invocations. Do not
replace its slower red/green allowance with the six-minute baseline allowance.

**345 is not approved as the complete verification floor.** It prices the original
46 product controls and ordinary helper qualification, but zero red/restore/green
cycles for the minimum 21 new helper guards. Zero cost for mandatory cycles is
unjustified. F-D12-2 requires an explicit additional numeric allocation once those
executable controls are designed; this review does not fabricate durations for an
undefined control battery. Ordinary Review, authoring and asset-download delay
remain separately reported, as D-12 states.

Savings remain 0 minutes from parallelization and 0 from omitting required native
methods or the real provider turn. The original method-only comparison still
avoids 1,075 estimated execution minutes (1,104 broad executions minus 29 exact
method minutes); it does not pay for boot/build/export or the missing helper
controls. Actual TCG timings may only raise the floor until supported by a measured
qualification; a deadline failure must return for plan correction.

**Pre-handoff audit:** relevant bodies and nearest fixtures read; original
guards=46, mapped=46, missing=0, duplicate PC mappings=0, unchanged. Amendment
audit: at least 21 additional guards unmapped; the post-root-exit release sequence
is not specified; full numeric floor is therefore not complete. No executable-PC
or passing-runtime claim is made. **Next: Plan**, limited to F-D12-1's concrete
fixture ownership sequence and F-D12-2's helper-guard coverage/cost amendment,
followed by focused TestDesign validation. This is an unverifiable-seam return,
not an operator permission question and not a request to redo the original matrix.
