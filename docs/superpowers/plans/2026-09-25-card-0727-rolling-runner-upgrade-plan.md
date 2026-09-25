# CARD-0727: rolling session-runner upgrade, server2 first

Date: 2026-09-25. Stage: Plan (task `00f66c8f`, written on the server2 Linux runner). Source
inspected: `598a522f928778e34d785ba20a60b549ee46f399` (the task branch tip, which carries the
investigation); `origin/master` was `7e912b7b` and differs only by CARD-0717 commits under
`server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs` and its tests, none of
which touch the files below. Investigation:
[2026-09-25-card-0727-rolling-runner-upgrade.md](../../investigations/2026-09-25-card-0727-rolling-runner-upgrade.md)
(task b69ddb64). Neighbouring plans: [CARD-0716 graceful restart](2026-09-25-card-0716-graceful-restart-phone-home-plan.md)
(operator token route shape, `PhoneHomeScriptedPeer.CloseObserved`, restart-resume),
[CARD-0679 launch loss](2026-09-24-card-0679-phone-home-launch-loss-plan.md) (runner-scoped
client, per-connection inventory, launch-generation watermark),
[CARD-0633 remote prep](2026-09-23-card-0633-dispatcher-remote-prep-plan.md) (migration
checkpoint shape). Next stage: **TestDesign**, which owns the final roster; the
`## Verification design` below is the plan's closed list and red-first mechanism for it.

No code, configuration, test or deployment changed during Plan. No live registration was sent.
The caller's standing authority ("deploy a new image parallel to the current one, new work goes
there, kill the old one once it finishes") covers everything designed here; nothing below needed
a further go-ahead.

## Outcome and scope

After this card a server2 deploy starts the new runner build in a second container while the old
container keeps every session it hosts. The desktop directory holds both phone-home sockets under
the one runner id `server2`. New launches go to the boot the operator promoted; every call for an
existing session (input, buffer, transcript, kill, release, compaction stop) goes to the boot that
launched it. The old boot is marked draining, takes no new claims, and is retired by Hangfire
once it has no sessions. A forced retire exists behind an explicit confirmation. Status names each
boot and its remaining sessions. Build slots on server2 come from one broker both containers reach
through `ANTIPHON_BUILD_SLOTS_URL`.

Three rounds, server2 only:

- **R1** (server): the directory keeps one live connection per boot id; sessions record their boot;
  per-session routing follows the boot; inventory, reconciliation and capacity become per boot;
  the CARD-0679 watermark and the boot/store refusals stay meaningful.
- **R2** (server, runner, compose, scripts): a draining boot; two slot services in the server2
  compose file with their own nested store and `/tmp`, sharing `/work` and the credential homes; a
  standalone build-slot broker; `deploy-parent-rolling` in `scripts/c590-remote.sh` with a thin
  `scripts/deploy-server2.ps1 -Rolling` on the desktop; a cross-process lock on the shared checkout.
- **R3** (server, runner, scripts): the `Retire` operation; the `RunnerBootRetireJob` Hangfire job;
  the forced-retire route with confirmation; per-boot status; `scripts/runner-boots.ps1`.

Out of scope, named for the follow-up card (see the last section): the desktop runner on 17204,
`restart-session-runner.ps1 -Rolling`, warm-pool reuse on a draining desktop process, the E2E
random-port guard and `ProductionRunnerGuard`.

## Ground truth

What the card assumes against what the code does at `598a522f`. Lines are from that commit.

| # | The card or brief assumes | What the code does |
|---|---|---|
| 1 | Two instances of `server2` can register as one logical runner. | `PhoneHomeRunnerDirectory` keeps a single `_live` (`PhoneHomeRunnerDirectory.cs:26`). `Register` refuses a different boot id while the live lease is unexpired with `phone_home_boot_conflict` (`:187-198`), and `AcceptConnect` disposes whatever socket it replaces as `superseded` (`:242-246`). `PhoneHomeConnectionTests.Live_boot_and_store_identity_cannot_be_replaced` (`tests/Antiphon.Tests/Agents/PhoneHomeConnectionTests.cs:163-193`) pins the refusal. |
| 2 | A session knows which instance hosts it. | The binding is `RunnerId` + `RunnerStoreId` + `RunnerCwd`, all-or-none (`CK_AgentSessions_RunnerBinding_AllOrNone`, `AppDbContext.cs:1139`), stamped at claim from `_runners?.LiveStoreId` (`AgentTaskDispatcher.cs:4421-4423`) and, for standing agents, at `AgentControlService.cs:649-651`. There is no boot column. `SessionRunnerOwner` is `(RunnerId, RunnerStoreId, RunnerCwd)` (`ISessionRunnerDirectory.cs:6`). |
| 3 | Messages reach the instance that hosts the session. | `RoutingSessionRunnerClient.Route` calls `Resolve(owner.RunnerId)` (`RoutingSessionRunnerClient.cs:85`), and every adapter holds a `RunnerScopedSessionRunnerClient` whose `Current` is `Resolve(RunnerId)` on each call (`RunnerScopedSessionRunnerClient.cs:30`). Both reach whichever socket is current. `Resolve` also refuses every call while `DispatchEligible` is false (`PhoneHomeRunnerDirectory.cs:79-80`). |
| 4 | A "draining" state exists. | None. `DispatchEligible` is the only gate and it blocks input as well as launches (`PhoneHomeLiveConnection.cs:306-309` lists Launch, Input, ConditionalInput, KillGeneration, ClearBuffer, Resize). |
| 5 | Inventory and reconciliation tolerate two instances. | `GetInventoryAsync` lists the one live socket (`:149-154`). The recovery pump follows `SnapshotLive()` only (`PhoneHomeRecoveryPump.cs:83`), a List replaces that connection's cached inventory (`PhoneHomeLiveConnection.ReplaceKnownLiveSessions`), and `ReconcileSessionsAsync` fails a live row the owner's inventory omits (`SessionReconciliationService.cs:209-247`). Owner match is runner id + store id (`PhoneHomeRecoveryPump.OwnerMatchesAsync`, `:384-397`). |
| 6 | Capacity is per instance. | `DeclaredCapacity(runnerId)` is the one socket's `Capacity` (`:318-327`); `CountRunnerOccupancyAsync` counts every non-terminal row with that `RunnerId` plus prepared-but-unlaunched tasks (`AgentTaskDispatcher.cs:948-966`). The runner counts its own seats per process (`PhoneHomeCommandDispatcher.cs:360`). |
| 7 | The CARD-0679 watermark is per runner generation. | It is per session id, in `launch-generations` beside the store id on the state volume (`PhoneHomeSettings.cs:23-26`; `PhoneHomeCommandDispatcher.cs:344-356`). Two processes on the same volume share it; the store id file is shared the same way (`PhoneHomeStoreIdentity.cs`). |
| 8 | Retire can be automatic. | The runner has no stop operation; the closest are `/sessions/kill-all` (`src/Antiphon.SessionRunner/Program.cs:346`) and the server's own `OperatorShutdownCoordinator`. The container is `restart: unless-stopped` (`docker-compose.server2-runner.yml:46`, asserted by `DockerStackContractTests.Server2_runner_restarts_unless_stopped` and `c590-remote.sh:1348-1350`), and `dind-entrypoint.sh:172-185` exits when either child exits, so Docker starts it again. Hangfire runs on the desktop (`HangfireConfiguration.AddOrUpdateRunnerSlotReconcileJob`, cron `PhoneHomeRunner:SlotReconcileCron`). |
| 9 | Status shows each generation. | `PhoneHomeRunnerStatusDto` has one `ProcessBootId`, one `Epoch` (`PhoneHomeContracts.cs:310-325`); `Status(runnerId)` reads `SnapshotLive()` (`:395-435`). |
| 10 | Two containers can share the host. | Compose defines one `session-runner` (privileged, `init: true`, `dind-data:/var/lib/docker`, `/tmp/antiphon-pty-hosts` in the container's own `/tmp`, `work` and `runner-state` volumes, the Codex home bind at `/state/codex`) and `state-init`; `DockerStackContractTests.Server2_file_defines_only_runner_and_state_init` pins exactly those blocks (`:237-246`). `deploy-parent` runs `compose_host up -d --no-build --remove-orphans` (`c590-remote.sh:1257`) and retires every image tag but the deployed sha12 (`retire_superseded_server2_images`, `:990-1004`). |
| 11 | Build slots can be shared. | `BuildSlotBroker` is process memory with a pid liveness sweep (`BuildSlotBroker.cs:162-172`) and a 90-minute TTL (`BuildSlotSettings.cs:25`); `Enabled: false` answers unlimited (`:61-64`). The wrapper's default endpoint is loopback and `ANTIPHON_BUILD_SLOTS_URL` overrides it (`scripts/lib/build-slot.ps1:26-29`). The wrapper runs the command in the foreground and never renews (`scripts/lib/build-slot.ps1:91-158`). A pid seen from another container's pid namespace is a different process. |
| 12 | The shared checkout is safe for two writers. | `RunnerWorkspaceService.MirrorAsync` runs `git fetch origin <branch>` then reads `FETCH_HEAD` (`RunnerWorkspaceService.cs:93-108`) and `git worktree add`; removal is `git worktree remove --force` (`:175`). No process-level or file lock; `FETCH_HEAD` is one file per repository. |
| 13 | The cgroup custody root is per container. | `CUSTODY_ROOT=antiphon-custody` (`dind-entrypoint.sh:19`, `LinuxCgroupCustodyProbe.CustodyRootName`) is a fixed path under cgroup v1, which on server2 is the host hierarchy seen by both containers. Executions are keyed by execution id under it. |
| 14 | The test host can script two runners. | `PhoneHomeTestHost.ConnectPeerAsync(autoReply, bootId)` already takes a boot id and keeps one `StoreId` (`tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs:153-163`); `PhoneHomeScriptedPeer` records `Launches`, `Inputs`, `RequestCount(op)`, `Sessions` and `Transcripts` and answers List/Get/Input by default (`:304-460`). `PhoneHomeLaunchTransportTests.LaunchWorld` seeds a remote-bound row in an isolated schema with the real dispatcher graph (`:314-420`). |
| 15 | Host-lane cases are routed automatically. | A live case reaches server2 only when it is in `$script:C590LiveCases` (`scripts/c590-real.ps1:10-40`) and has a stub arm in `scripts/verify-docker-stack.ps1` (`:170-232`); `RemoteScriptContractTests.Custody_containment_is_routed_to_the_remote_and_not_left_pending` enforces it. The operator token stays on the desktop (`Operator:TokenPath`), and `c590-real.ps1` streams only the Claude token to server2. |

## Decisions

### R1: two live boots under one runner id

**D-1. The directory keys live connections by `ProcessBootId`.** `_live` becomes a
`Dictionary<Guid, BootState>` where a `BootState` holds the connection, its last recovered
connection, lease-until, and the role below. `SnapshotLive()` keeps its signature and returns the
*accepting* boot's connection, so `RunnerSlotService`, `Status` callers and existing tests keep
compiling; new `SnapshotBoot(bootId)` and `SnapshotBoots()` expose the rest. `AcceptConnect`
supersedes only a socket of the *same* boot. `Disconnect` and `MarkRecovered` act on the boot
whose connection they were given. Rejected: two directories or two runner ids (`server2`,
`server2-b`), because callers pass `-Runner server2` and the card requires that string to keep
working; a second `AllowedRunnerId` list would also double every routing, capacity and default
decision.

**D-2. Boot roles live in a `RunnerBoots` table, not in directory memory.** Entity `RunnerBoot`
(`RunnerId varchar(64)`, `BootId uuid`, `StoreId uuid`, `Role` enum `RunnerBootRole
{Standby=0, Accepting=1, Draining=2, Retired=3}`, `FirstSeenAt`, `LastSeenAt`, `PromotedAt?`,
`DrainedAt?`, `IdleObservedAt?`, `RetiredAt?`, `RetireReason varchar(200)?`, `BuildVersion
varchar(100)?`; key `(RunnerId, BootId)`, index `(RunnerId, Role)`). The directory reads and
writes it through an `IRunnerBootRegistry` (`DbRunnerBootRegistry` in production;
`InMemoryRunnerBootRegistry` for `PhoneHomeTestHost` without a connection string, the same
split `PendingRunnerSessionInventory` already needs). Why: the desktop restarts during an overlap;
both boots reconnect in arbitrary order and the server must still know which one is draining,
and Hangfire's retire needs the draining set and an audit (who promoted, when drained, when and
why retired). Rejected: a `Draining` flag in the registration request (a contract change and it
makes the runner the authority for a server decision); registration order as seniority (wrong
after a server restart); a boot start time in the registration (protocol version bump).

**D-3. Sessions record their boot.** New nullable `AgentSessions.RunnerBootId uuid` with index
`IX_AgentSessions_RunnerBootId`; migration `AddRunnerBoots` adds the column and the table in one
step, created with `dotnet ef migrations add AddRunnerBoots --project server` and checked with
`dotnet ef migrations has-pending-model-changes --project server`. The all-or-none check
constraint is unchanged: rows created before this migration are legitimately null. Stamped at
both binding sites (`AgentTaskDispatcher.cs:4421`, `AgentControlService.cs:650`) from the same
directory snapshot the claim gate used (`AcceptingBoot(runnerId)` returns `(BootId, Connection)`
so the claim never stamps a boot it did not check). `SessionRunnerOwner` gains
`Guid? RunnerBootId` and `GetBindingAsync` selects it. A legacy null row is attributed to the boot
that was `Accepting` at its `StartedAt` (derivable from `RunnerBoots.PromotedAt/DrainedAt`), which
for the first rolling deploy is today's container. Rejected: a heuristic "oldest live boot"
(ambiguous once the old boot is gone), and back-filling the column at migration time (there is no
boot row to back-fill from until the old container re-registers after the server deploy).

**D-4. Per-session calls follow the session's boot; new work follows the accepting boot.**
`ISessionRunnerDirectory` gains `ResolveBoot(string runnerId, Guid? bootId)`: the named boot's
client when its socket is open and it has recovered (draining is not a refusal), otherwise the
typed `phone_home_unavailable`; `bootId == null` resolves through the attribution rule in D-3.
`RoutingSessionRunnerClient.Route` and `AgentSessionRuntime.EnsureInputTransportAvailableAsync`
use it. `RunnerScopedSessionRunnerClient` becomes session-aware: every method that takes a session
id resolves that session's boot (binding cached per session id inside the client; the binding
never changes, CARD-0679 D-3, so a miss is re-read only after `OwnerCacheNegativeSeconds`), and the
launch-time methods (`GetCapabilitiesAsync`, `GetProviderAuthAsync`, `GetHealthAsync`, the
capability mismatch probes) keep `Resolve(RunnerId)`. `StartAsync(sessionId, spec)` is a
per-session call: the row is stamped before the launch (G-20), so a launch goes exactly where the
claim bound it. `Resolve(runnerId)` keeps its meaning of "the boot new work goes to", so
`DefaultRunnerRoutingPolicy`, the claim gate (`AgentTaskDispatcher.cs:929`, `:5463`),
`RemoteWorkspaceService.Remote` for mirrors, `SourceLandingAdmission` and the provider-auth
probes at create need no change. `VerificationWorkspaceDirectory` and `VerificationCleanupService`
resolve the *task's session boot* when the task has one (custody is container-local) and fall
back to `Resolve`. Rejected: keeping `Resolve(runnerId)` as the only entry and letting the
directory guess by session id (the directory would need the session id on every call, which the
interface does not carry).

**D-5. Which boot accepts, and when that changes.** R1 knows two roles. At
`MarkRecovered(boot)`: if the runner has no other live boot in role `Accepting`, this boot becomes
`Accepting` (persisted, `PromotedAt`); otherwise it becomes `Standby`. A `Standby` boot is
promoted automatically only when the accepting boot is gone (lease expired or socket closed), which
keeps today's crash-and-restart behaviour, where the only boot accepts without an operator step.
R2 adds `Draining` and `PromoteAsync`: `POST /api/session-runners/{runnerId}/boots/{bootId}/promote`
makes the named live, recovered boot `Accepting` and drains **every other live boot**, `Accepting`
or `Standby` (`DrainedAt`), so a boot that was promoted early by the fallback is still drained by
the operator's promote. `Draining` is sticky: it is never auto-promoted, and a `Standby` or newly
recovered boot is auto-promoted only when no live `Accepting` boot exists; the same `promote` verb
on a draining boot is the explicit rollback and drains the current accepting one. Why explicit
promotion: the deploy runs its probes (provider sign-in, `docker info`, custody, git identity,
checkout) *between* the new boot's eligibility and the switch, so a broken new build never
receives a launch. Rejected: newest eligible boot wins (a restarted desktop would flip roles by
reconnect order; a restarted-after-retire container would steal launches).

**D-6. Exactly when a boot conflict is still an error.** `Register` refuses:

1. `phone_home_store_mismatch`: the request's `RunnerStoreId` differs from the store id this
   directory remembers for the runner (`_liveStoreId`, set by the first registration and never
   cleared on disconnect). Unchanged. Two boots that do not share `/state/runner-store-id` are
   two runners, and the second is refused whatever its boot id.
2. `phone_home_boot_conflict`: a boot id not currently live when `PhoneHomeRunner:MaxLiveBoots`
   (default **2**) boots already hold unexpired leases. Unexpired means an open socket with a
   heartbeat inside `LeaseSeconds`, or a registration younger than `LeaseSeconds` that has not
   connected yet. A third generation must wait for a retire.
3. `phone_home_boot_conflict`: a boot id whose `RunnerBoots` row is `Retired`. A retired process
   does not come back; a real restart is a new `ProcessBootId` and is judged by rule 2.
4. `phone_home_invalid_ticket` (unchanged): a ticket presented for another boot or store.

Everything else is accepted: the same boot re-registering (reconnect; its old socket is
superseded), and a new boot with the same store id while fewer than `MaxLiveBoots` are live. The
message for rule 2 names the live boot ids so an operator can see which one to retire.

**D-7. Inventory is the union, and absence is judged per boot.** `GetInventoryAsync(runnerId)`
returns `Available(union of every live, recovered boot's List)` only when every boot that has
attributed non-terminal rows is live and recovered; otherwise `Unavailable`, so the reconciler
skips the cycle exactly as it does today for one unavailable runner. `LiveRemoteSessionIds()` is
the union of recovered boots' `KnownLiveSessions`; `UnknownRemoteSessionIds()` is the union of
each boot's unconfirmed entries plus, for a boot that is recovering, lease-expired or closed, its
last recovered inventory (the CARD-0679 rule, applied per boot). The recovery pump keeps one
`ConnectionState` per connection (it already does) and runs its cycle for every live boot rather
than `SnapshotLive()` only; catch-up and refresh are per connection. `OwnerMatchesAsync` also
requires the session's `RunnerBootId` to equal the connection's boot when the row has one; a
legacy null row matches any boot with the store id. A row is confirmed gone only by its own
boot's List omitting it, by an exit or kill event, or by its boot's `Retired` row (R3), never by
another boot's List. Rejected: failing rows absent from the accepting boot's List (that is the
CARD-0679 regression the investigation names).

**D-8. Capacity is per boot.** `DeclaredCapacity(runnerId)` is the accepting boot's declared
capacity; `CountRunnerOccupancyAsync` counts non-terminal rows whose attributed boot is the
accepting boot plus prepared-but-unlaunched tasks and in-flight mirrors for the runner (those will
bind to the accepting boot). The runner's own seat count is already per process. So the old
boot's sessions never consume the new boot's seats, and the host may briefly hold up to two
capacities of sessions; the build-slot memory floor and the shared broker (D-13) bound the builds
they start. Rejected: halving each boot's capacity during overlap (the old boot takes nothing new
anyway, so it would only idle seats).

**D-9. The watermark, store id and launch generations stay shared.** Both boots read
`/state/runner-store-id` and `/state/launch-generations` on the `runner-state` volume. A re-sent
Launch is refused by whichever boot receives it (per session id), which is what the fence needs.
`PhoneHomeStoreIdentity.LoadOrCreate` already writes by rename, and only the first boot ever
creates the file. No change; TestDesign pins it with a two-boot re-send.

**D-10. Status carries every boot from R1.** `PhoneHomeRunnerStatusDto` gains
`IReadOnlyList<PhoneHomeRunnerBootDto>? Boots` (default null): `BootId`, `Epoch`, `Role`,
`Available`, `DispatchEligible`, `LastHeartbeatUtc`, `BuildVersion`, `Capacity`, plus R3's
`Sessions`, `RunnerSessions`, `DrainedAt`, `IdleObservedAt`, `RetiredAt`, `RetireReason`. The
top-level fields keep describing the accepting boot (or, without one, the newest live boot), so
`c590-remote.sh`'s `"available": true` grep and the ops doc stay true. The provider-auth route
gains an optional `?bootId=` so a probe can be aimed at a specific boot.

### R2: draining, the second container, the shared broker

**D-11. Draining changes only where new claims bind.** R2 adds the role: a `Draining` boot is
excluded from `Resolve(runnerId)`, `DeclaredCapacity`, `AcceptingBoot` and default routing
exactly as a `Standby` boot already is in R1; `ResolveBoot` serves it for everything else, including a `StartAsync` for a session whose row was stamped with
it before the drain (the claim happened first; the launch belongs to that boot, and the retire
job sees it as a live row). Nothing new is added to the `DispatchEligible` gate. Why: a typed
launch refusal at the drain moment would fail a task the acceptance says must not be interrupted;
binding at claim removes the race instead of detecting it. `promote` and forced retire (R3) are
operator-token routes in `SessionRunnerEndpoints` using `OperatorCredential.Require`, the
CARD-0716 shape. New problem codes: `phone_home_boot_not_live` (promote of a boot that is not
recovered), `phone_home_boot_accepting` (forced retire of the accepting boot), `phone_home_boot_busy`
(a non-forced retire the runner refuses). Rejected: a runner-side drain flag (the runner does not
decide placement).

**D-12. Two slot services in `docker-compose.server2-runner.yml`.** `session-runner-a` and
`session-runner-b`, each under its own compose profile (`slot-a`, `slot-b`) so a plain `up`
starts neither, each `image: antiphon-server2/session-testing:${SLOT_A_SHA12:-unset}` (resp.
`SLOT_B_SHA12`), each with `ANTIPHON_RUNNER_SLOT: a|b` and `ANTIPHON_BUILD_SLOTS_URL:
http://build-slots:8080/build-slots` in `environment:`; everything else from today's service
through a YAML anchor. Own: `dind-data` (slot a keeps today's volume so its nested store
survives) and `dind-data-b`; `/tmp` is already container-local (`SessionRunner__PtyHostDir`,
`TMPDIR`) and stays unmounted, so no sibling can see the other's pty manifests. Shared:
`work`, `runner-state` (store id, launch generations, Claude and Grok homes, session logs),
the Codex home bind, the deploy key and phone-home secrets, the git identity, the Claude token
file. `restart: unless-stopped` stays on both slots; the drain step sets the old container to
`--restart=no` (D-15). `PhoneHome__Capacity` stays `"10"` per slot. The custody root stays the
shared `antiphon-custody` (row 13): executions are unique ids, the helpers and sudoers keep their
paths, and the residue probe is not part of the rolling case (it belongs to `custody-containment`
on an idle host). `state-init` is unchanged and stays a dependency of both slots. Rejected: a
second compose project (needs external volumes and an external network for the broker, and every
helper and contract test is anchored on `antiphon-runner`); `--scale session-runner=2` (replicas
share `dind-data`, which two dockerds corrupt); renaming the custody root per slot (touches the
runner constant, two helpers, sudoers and the probe for no functional gain).

**D-13. One broker, in its own container, reached through `ANTIPHON_BUILD_SLOTS_URL`.** Service
`build-slots` in the same compose file: the same runner image pinned by its own `BUILD_SLOTS_SHA12`
(so a rolling deploy never recreates it and never drops held leases), unprivileged, uid 1654,
`SessionRunner__BuildSlotsOnly: "true"`, today's budget values, profile `broker`,
`restart: unless-stopped`, health `curl -fsS http://127.0.0.1:8080/build-slots`. Runner change:
`Program.cs` in build-slots-only mode maps `/health` and the build-slot routes and registers no
phone-home, pty adoption or session routes. Liveness change: a broker in another container cannot
see a holder's pid, so `BuildSlotSettings.HolderLiveness` (`pid` default, `renew` for the broker
service) makes the sweep reap a lease not renewed within `RenewGraceSeconds` (default 90) instead
of consulting pid liveness; the TTL still applies. New route `POST /build-slots/{leaseId}/renew`
(204 held, 404 unknown); the grant carries `RenewEverySeconds` (null in pid mode) and
`scripts/lib/build-slot.ps1` renews on that interval from a `Start-ThreadJob` while the foreground
command runs, only when the grant asks for it, so the desktop broker's behaviour is byte-for-byte
today's. The in-container broker on each slot stays configured as today as the fallback for a
wrapper without the variable; the compose variable is what every pty child inherits. The first
rolling deploy starts from today's container, which has no `/build-slots` at all (the card's own
observation), so the overlap budget is the new boot's shared broker plus the old boot's unleased
runs, which is the status quo. Rejected: `pid: "service:build-slots"` on the slots (a shared pid
namespace whose init is the broker; restarting the broker would SIGKILL both runners); a
file-backed lease ledger on `runner-state` with per-container pid sweeps (no new service, no
renew loop, but not the shape the brief asks for and a crashed container's leases would sit until
the TTL); `BuildSlots__Enabled: false` on one side (unlimited, per row 11); moving the broker to
the desktop server (the budget is server2's).

**D-14. The deploy is a new host-lane case plus a thin desktop wrapper.** `c590-remote.sh` gains
`case_deploy_parent_rolling`: the same preconditions and evidence as `deploy-parent`; picks the
target slot (the slot whose container is absent or exited; a slot whose boot is `Standby` is
stopped and reused; a slot whose boot is `Draining` with attributed rows refuses
`TargetSlotDraining`; both slots accepting refuses `BothSlotsLive`); refuses `NestedStoreDiskLow`
below 20 GB free on the volume filesystem; builds both images as today; writes `SLOT_<T>_SHA12`
and, when absent, `BUILD_SLOTS_SHA12` into `stack.env` without touching the other slot's tag;
starts the broker if it is not running (`compose_host --profile broker up -d --no-build
build-slots`); runs `compose_host --profile slot-<t> up -d --no-build session-runner-<t>` and
**never** `--remove-orphans`; waits for health, then runs every `deploy-parent` probe against the
new container (secret readable, phone-home registered, `docker info` name equals the container
hostname, custody helpers, git identity, checkout verification); waits until
`GET /api/session-runners/server2/status` lists the new boot `dispatchEligible` in role
`Standby`, or `Accepting` if the fallback rule promoted it during the wait (five minutes, else
`RunnerNotEligible`; the desktop's promote drains the old boot in either case); writes `new-boot.txt` and
`old-container.txt`; retires image tags that are neither slot's nor the broker's. It does not
promote: the operator token lives on the desktop. A second case `rolling-drain-host` takes the
old container name from evidence and runs `docker update --restart=no` on it after the desktop
has confirmed promotion, writing `restart-policy-old.txt`. Both cases join `$script:C590LiveCases`
and get stub arms in `verify-docker-stack.ps1`. `scripts/deploy-server2.ps1 -Rolling` (desktop,
ASCII, pwsh 7) runs `verify-docker-stack.ps1 -Case deploy-parent-rolling`, posts `promote` for
the new boot with the operator token (the `runner-slots.ps1` token helper, never printed), polls
status until the old boot is `Draining` and the new one `Accepting` (two minutes), runs
`rolling-drain-host`, prints the boots table and exits 0; a failed promote leaves the new boot in
`Standby` and exits 2 without touching the old container's restart policy. Rejected: streaming
the operator token to server2 (a custody expansion for one POST); folding the restart-policy
change into the first case (a failed promote would leave the accepting container unable to
survive a crash).

**D-15. `docker update --restart=no` on the drained container.** With `unless-stopped`, the
retired runner's clean exit would be restarted into a new, useless boot. The change is a host
command and belongs to the drain step, after promotion. A drained container that crashes stays
down, which is right: its sessions died with it, and a fresh start would only be a third boot.
Rejected: `restart: on-failure` on the slots (not restarted after a host reboot, which the
persistent runner requires); a marker file the entrypoint reads (a restart loop); an entrypoint
call to the host socket (no socket is mounted, by design).

**D-16. The shared checkout gets a cross-process lock and stops reading `FETCH_HEAD`.**
`RunnerWorkspaceService` takes an exclusive `FileStream` (`FileShare.None`, which .NET maps to
`flock` on Linux) on `<repository>/.git/antiphon-mirror.lock` around fetch + verify + `worktree
add` and around `worktree remove`, waiting up to `MirrorLockWaitSeconds` (120) with a typed
`phone_home_unsupported_target` refusal naming the lock on timeout, and reads the fetched tip
from `refs/remotes/origin/<branch>` instead of `FETCH_HEAD` (per-ref updates are already locked
by git; `FETCH_HEAD` is one racy file). Why: an old boot removing mirrors while the new one adds
them is the one new cross-process writer this card introduces, and the `FETCH_HEAD` read is racy
even inside one process today. Rejected: serialising remote prep on the desktop (does not cover
retirement-time removal from the other process).

### R3: retire, forced retire, status

**D-17. `Retire` is a phone-home operation the runner executes.** `PhoneHomeOperation.Retire = 26`
with `RunnerRetireRequest(bool Force, string Reason)` and `RunnerRetireResult(Guid BootId, int
KilledSessions, DateTime StoppingAtUtc)`. The dispatcher refuses a non-forced retire while
`OwnedSessionCount > 0` with `phone_home_boot_busy` (count in the message); a forced retire runs
`KillAllAsync(5 s)` first; on acceptance it schedules `IHostApplicationLifetime.StopApplication()`
250 ms after the reply is written so the frame flushes, and the connection service ends the socket
with close description `retiring`. The runner exits 0, `dind-entrypoint.sh` stops dockerd and the
container exits; with D-15 it stays exited. Rejected: `docker stop` from the desktop (no socket
and no host access); a close frame as the signal (not acknowledged, not typed).

**D-18. `RunnerBootRetireJob` retires idle draining boots.** Hangfire recurring job
`antiphon:runner-boot-retire` on `PhoneHomeRunner:BootRetireCron` (default `* * * * *`),
registered beside `RunnerSlotReconcileJob`, `[AutomaticRetry(Attempts = 0)]`. For each
`RunnerBoots` row in `Draining`: the boot is idle when a fresh List from that boot has no
non-`Exited` session **and** the database has no non-terminal row attributed to it **and**
`DrainedAt + RetireMinDrainSeconds` (60) has passed. The first idle observation stamps
`IdleObservedAt`; a later run at least `RetireIdleSeconds` (120) after it sends
`Retire(force: false)`; success marks the row `Retired` with reason `idle`; a `phone_home_boot_busy`
answer clears `IdleObservedAt`; a boot whose lease has expired and that has no attributed live
rows is marked `Retired` with reason `idle_disconnected` without a send, which is what closes a
crashed old boot's book. `Standby` and `Accepting` boots are never retired by the job. Rejected:
retiring on the first idle observation (a launch claimed just before the drain may still be
`Starting`); retiring `Standby` boots (the deploy's new boot is standby until promoted).

**D-19. Forced retire needs the operator token and the boot id as confirmation.**
`POST /api/session-runners/{runnerId}/boots/{bootId}/retire` with `{ "reason": "...",
"confirmBootId": "<bootId>" }`: 403 `operator_token_required` without the token; 400 when
`confirmBootId` does not equal the path boot id or the reason is empty or over 200 characters;
409 `phone_home_boot_accepting` when the boot is the accepting one (promote another first). It
fails every non-terminal row attributed to the boot with `FailureReason` "runner boot <id> retired
by operator: <reason>" and `SessionTermination` `SystemRequest`, writes a `RunnerBootForceRetired`
incident, sends `Retire(force: true)` when the boot is connected, and marks the row `Retired` with
the reason either way. `scripts/runner-boots.ps1 list|promote|retire` (`retire -Force -Confirm
<bootId> -Reason "..."`) follows `runner-slots.ps1` (token from the owner-only file, never
printed). Rejected: a `?force=true` flag alone (the card asks for an explicit confirmation).

**D-20. Status per boot with remaining sessions.** R3 fills D-10's `Sessions` (non-terminal
rows attributed to the boot), `RunnerSessions` (the last List count, null before one), `DrainedAt`,
`IdleObservedAt`, `RetiredAt`, `RetireReason`. `GET /api/session-runners/{runnerId}/slots` adds
`bootId` per slot and lists the union. `runner-boots.ps1 list` prints one row per boot.

### Cross-round

**D-21. Settings and defaults.** `PhoneHomeRunnerSettings`: `MaxLiveBoots` (2, validated 1..4),
`BootRetireCron` (`* * * * *`), `RetireMinDrainSeconds` (60), `RetireIdleSeconds` (120), each
validated positive when enabled. `PhoneHomeSettings` (runner): `MirrorLockWaitSeconds` (120).
`BuildSlotSettings`: `HolderLiveness` (`pid`), `RenewGraceSeconds` (90), `RenewEverySeconds` (20,
sent to the client in renew mode); `SessionRunner:BuildSlotsOnly` (false). All are additive with
today's behaviour as the default.

**D-22. Docs.** `docs/session-runtime-invariants.md`: three invariants (a session's calls go to
the boot that launched it; a draining boot takes no new claims and still serves its sessions; a
boot is confirmed gone only by its own List, an exit or kill, or its retire). `docs/docker-stack.md`:
the two slots, the broker, `deploy-server2.ps1 -Rolling`, what the first rolling deploy leaves
behind (the exited old container, removed by the next deploy of that slot). `docs/ops-http.md`:
the status `boots` array, `promote`, `retire`, the two problem codes, `runner-boots.ps1`.
`docs/testing-and-build.md`: build slots renew mode and the shared broker on server2.
`docs/bootstrap.md` or `docs/apphost-runbook.md`: one pointer that the server2 deploy is now
rolling. `docker/stack.env.example`: `SLOT_A_SHA12`, `SLOT_B_SHA12`, `BUILD_SLOTS_SHA12`.

## Implementation rounds and slices

Every slice commits its red tests first (`Sn-tests`), then the production change (`Sn`), with the
real checkpoint outcome in each message. Server slices and runner slices are separate commits.

### R1 (server only, one Code dispatch)

| Slice | Files | Tests |
|---|---|---|
| S1: two live boots in the directory (~2 h) | `PhoneHomeRunnerDirectory.cs` (D-1 boot map, D-6 rules, D-5's `Standby`/`Accepting` roles and fallback promotion, `SnapshotBoot(s)`, `ResolveBoot`, `AcceptingBoot`, per-boot `Disconnect`/`MarkRecovered`, D-10 `Boots` in `Status`); `PhoneHomeContracts.cs` (`PhoneHomeRunnerBootDto`, `Boots`, `RunnerBootRole`); `PhoneHomeRunnerSettings.cs` + validator (`MaxLiveBoots`); `ISessionRunnerDirectory.cs` (`ResolveBoot`, `AcceptingBoot` with default bodies); `IRunnerBootRegistry` + `InMemoryRunnerBootRegistry` (D-2, memory first; the DB registry lands in S2); `SessionRunnerEndpoints.cs` connect route passes the boot to `Disconnect`; `PhoneHomeTestHost.cs` wires the in-memory registry | `PhoneHomeConnectionTests`: V-1, V-2, V-3, V-10 new; `Live_boot_and_store_identity_cannot_be_replaced` keeps its store-mismatch and same-boot halves and drops the boot-refusal half (moved to V-1/V-2) |
| S2: sessions carry their boot; routing, inventory, capacity per boot (~4 h) | `AgentSession.cs` + `AppDbContext.cs` + migration `AddRunnerBoots` (D-2 table, D-3 column, index; `has-pending-model-changes` clean); `RunnerBoot.cs`, `RunnerBootRole.cs`; `DbRunnerBootRegistry.cs` (+ `Program.cs` registration); `ISessionRunnerDirectory.cs` `SessionRunnerOwner.RunnerBootId`; `PhoneHomeRunnerDirectory.GetBindingAsync` (+ D-3 attribution for null); `RoutingSessionRunnerClient.cs`, `RunnerScopedSessionRunnerClient.cs` (D-4 session-aware), `AgentSessionRuntime.cs:1374`, `VerificationWorkspaceDirectory.cs`, `VerificationCleanupService.cs`; `AgentTaskDispatcher.cs` (`AcceptingBoot` in the claim gate, `RunnerBootId` stamp at `:4421`, D-8 occupancy) and `AgentControlService.cs:650`; `PhoneHomeRecoveryPump.cs` (per-boot cycles, D-7 owner match); `GetInventoryAsync`/`LiveRemoteSessionIds`/`UnknownRemoteSessionIds` unions; `SessionRunnerEndpoints.cs` provider-auth `?bootId=` | New `tests/Antiphon.Tests/Application/PhoneHomeRollingBootTests.cs` (V-4..V-9, V-11) on a `RollingWorld` copied from `PhoneHomeLaunchTransportTests.LaunchWorld` (isolated schema, `BridgeQueueHarness`, real dispatcher graph, `host.Directory`) with two peers (`ConnectPeerAsync(bootId: A)`, `ConnectPeerAsync(bootId: B)`); regressions listed in CP-4/CP-5 |
| S3: docs for R1 (~20 min) | `docs/session-runtime-invariants.md` (first invariant), `docs/ops-http.md` status row (`boots`) | none |

### R2 (server, runner, compose, scripts; one Code dispatch, desktop-placed for the live rows)

| Slice | Files | Tests |
|---|---|---|
| S4: draining and promote (~2 h) | `PhoneHomeRunnerDirectory.cs` (D-5 `Draining`, `PromoteAsync` draining every other live boot, sticky drain in the fallback rule, D-11 exclusions), `DbRunnerBootRegistry`/`InMemory` (`Promote`, `Drain`), `SessionRunnerEndpoints.cs` (`promote` route with `OperatorCredential.Require`), `PhoneHomeContracts.cs` (`BootNotLive`, `BootAccepting`, `BootBusy` codes), `DispatchHoldDetails` unchanged | `PhoneHomeRollingBootTests` V-12..V-16 |
| S5: shared broker (~3 h, runner + script) | `BuildSlotSettings.cs` (`HolderLiveness`, `RenewGraceSeconds`, `RenewEverySeconds`), `BuildSlotBroker.cs` (`Renew`, renew-mode sweep, `LastRenewedAt` on `Lease`), `BuildSlotRoutes.cs` (`POST /build-slots/{leaseId}/renew`), contracts (`BuildSlotGrant.RenewEverySeconds`), `Program.cs` (`SessionRunner:BuildSlotsOnly`), `scripts/lib/build-slot.ps1` (renewer thread job; ASCII), `scripts/build-slot.ps1` unchanged | `BuildSlotBrokerTests` V-17, V-18; `BuildSlotEndpointTests` V-19, V-20; `BuildSlotScriptTests` V-21 |
| S6: checkout lock (~1 h, runner) | `RunnerWorkspaceService.cs` (D-16 lock, `refs/remotes/origin/<branch>`), `PhoneHomeSettings.cs` (`MirrorLockWaitSeconds`) | `RunnerWorkspaceServiceTests` V-22 |
| S7: compose, deploy case, wrapper, contract tests (~3 h) | `docker-compose.server2-runner.yml` (D-12 slots, D-13 broker), `docker/stack.env.example`, `scripts/c590-remote.sh` (`case_deploy_parent_rolling`, `case_rolling_drain_host`, `slot_container`, keep-set in `retire_superseded_server2_images`), `scripts/c590-real.ps1` (roster), `scripts/verify-docker-stack.ps1` (stub arms), new `scripts/deploy-server2.ps1`, new `scripts/test-deploy-server2.ps1` (C495 fixture: seamed `verify-docker-stack.ps1`, token file with a sentinel, trace) | `DockerStackContractTests` V-23 (new and updated methods), `DindRunnerContractTests` V-24, `RemoteScriptContractTests` V-25, `test-deploy-server2.ps1` T-1..T-4 (V-26) |
| S8: docs for R2 (~30 min) | `docs/docker-stack.md`, `docs/testing-and-build.md` (build slots), `docs/session-runtime-invariants.md` (second invariant), `docs/bootstrap.md` pointer | `DockerStackDocumentationTests` regression |
| L: live proof on server2 (desktop lane, after R1 is active on the desktop) | none | L-1..L-3 in the R2 table |

### R3 (server, runner, scripts; one Code dispatch)

| Slice | Files | Tests |
|---|---|---|
| S9: `Retire` on the runner (~1.5 h) | `PhoneHomeContracts.cs` (operation 26, request/result records, `retiring` close reason), `PhoneHomeCommandDispatcher.cs` (D-17, `IHostApplicationLifetime` injected), `PhoneHomeConnectionService.cs` (close description), `PhoneHomeRunnerClient.cs` (`RetireAsync`) | `PhoneHomeCommandDispatcherTests` V-27, V-28 |
| S10: retire job, forced retire, status (~3 h) | new `RunnerBootRetireJob.cs` (+ `HangfireConfiguration.AddOrUpdateRunnerBootRetireJob`, `Program.cs`), `PhoneHomeRunnerSettings.cs` + validator (`BootRetireCron`, `RetireMinDrainSeconds`, `RetireIdleSeconds`), `SessionRunnerEndpoints.cs` (`retire` route, D-19), `RunnerBootRetireService.cs` (shared by job and route: idle check, row failure, incident), `PhoneHomeRunnerDirectory.Status` (D-20 fields), `RunnerSlotService.ListAsync` (`bootId`, union), `PhoneHomeContracts.cs` (`RunnerBootForceRetired` incident kind where incidents are enumerated) | new `tests/Antiphon.Tests/Application/RunnerBootRetireJobTests.cs` (V-29..V-32); `PhoneHomeRollingBootTests` V-33..V-37 |
| S11: `runner-boots.ps1` and docs (~1 h) | new `scripts/runner-boots.ps1`, `docs/ops-http.md`, `docs/docker-stack.md`, `docs/session-runtime-invariants.md` (third invariant) | new `tests/Antiphon.Tests/Scripts/RunnerBootsScriptTests.cs` (V-38: ASCII, verbs, token header, no token in output) |

## Risks

**Two nested dockerd processes on server2's kernel 4.15 (cgroup v1, legacy iptables).** Each
slot is privileged with its own network namespace, its own `/var/lib/docker` volume and its own
`docker0`, so the daemons do not share a graph or a bridge. What they do share: the host cgroup v1
hierarchy (no cgroup namespace on v1), where each nested daemon creates `docker/<id>` groups under
its own container's path (unique ids; this is how one nested daemon already runs), the
`antiphon-custody` root (row 13, unique execution ids), the conntrack table and the
`bridge-nf-call-iptables` sysctl, which on this kernel may be global rather than per namespace.
The investigation did not start two. Mitigation: L-1 runs `docker info` in both slots while both
run and a nested `docker run --rm busybox true` in the new slot, and records
`bridge-nf-call-iptables` from both; a `SiblingDaemonRefused` (daemon name not equal to the
container hostname) or a failed nested run stops the rolling case before promotion. The new
slot's nested store starts empty (images re-pull on first use); `NestedStoreDiskLow` refuses below
20 GB free.

**Codex home refresh contention.** Both boots' Codex children share `/state/codex` (one host
directory) and Codex rewrites `auth.json` by rename on refresh; readers never see a torn file,
but two concurrent refreshes with refresh-token rotation can leave one process with a revoked
token. This already exists across ten concurrent sessions in one container; the overlap adds
processes, not a new class. Grok's `/state/grok/auth.json` has the same shape; Claude uses the
setup token from the environment and refreshes nothing on disk. Mitigation: L-2 probes
`provider-auth` for `claude`, `codex` and `grok` on both boot ids at promotion and again after at
least 30 minutes of overlap and requires `loggedIn` unchanged; no code change in this card. If a
sign-out is observed, the follow-up is a per-boot Codex home copy at slot start, which is a
credential-custody change and its own card.

**Git lock contention on the shared `/work`.** D-16 covers the writers this card adds: the old
boot removes mirrors while the new boot fetches and adds them. Concurrent `worktree add` for
different branches touch different `.git/worktrees/<name>` entries and per-ref locks; the racy
piece is `FETCH_HEAD`, replaced by the tracking ref, and the lock serialises the rest with a
120 s bound. Mitigation and measurement: V-22 pins the lock; L-3 greps both containers' runner
logs during the overlap window for `index.lock`, `packed-refs.lock`, `Mirror fetch failed` and
`antiphon-mirror.lock` timeouts (expected zero hits) and diffs `git worktree list` before and
after. A lock timeout is a typed refusal that leaves the task Queued with the existing
`RemotePrepBackoff` hold, never a half-made mirror.

**Broker lease loss.** The broker holds leases in memory; a broker restart (its own upgrade, or
a crash) drops them and briefly admits up to a full budget of new builds beside the running ones.
Bounded by the memory floor; documented; the broker tag is pinned separately so a rolling runner
deploy never restarts it.

**Legacy rows without a boot.** Sessions launched before R1 is active have a null boot and are
attributed by D-3's rule. The first rolling deploy is the only time this matters; V-11 pins it.

**A server restart during overlap.** Roles come back from `RunnerBoots` (D-2); the pump recovers
each boot separately; the deploy wrapper polls status rather than assuming. V-9 pins it.

**A desktop-only step in a server2-placed pipeline.** The live rows L-1..L-3 and the wrapper
itself run from the desktop (the c590 bridge SSHes to server2). A server2-placed Code task reports
them not run; the orchestrator dispatches them to a desktop task after R1 is active on the desktop
server, or the new boot is refused `phone_home_boot_conflict` by the unchanged directory.

## Verification design

### Harness and red-first discipline

- Desktop transport tests use `PhoneHomeTestHost` and `PhoneHomeScriptedPeer` (CARD-0679 D-11,
  CARD-0716 `CloseObserved`). Two peers are `ConnectPeerAsync(bootId: bootA)` and
  `ConnectPeerAsync(bootId: bootB)` on the host's single `StoreId`; the peer gains
  `Retires` (frames of operation 26) and a `RunnerRetireResult` default reply.
- DB-backed rolling tests use a `RollingWorld` copied from `PhoneHomeLaunchTransportTests.LaunchWorld`
  (isolated schema, `BridgeQueueHarness` with the real `AgentProtocolAdapterFactory` and
  `AgentTaskDispatcher` graph bound to `host.Directory`, `AgentKind.Raw`), seeding rows with
  `RunnerBootId` where the test needs a pre-bound session. The retire job runs through its class
  with a `FakeTimeProvider`, never through Hangfire.
- Operator routes use `PostOperatorAsync(path, body, token)` with the token from
  `host.OperatorTokenPath` (CARD-0653/0716 shape).
- Runner tests use `PhoneHomeCommandDispatcherTests`' fake runtime with a recording
  `IHostApplicationLifetime`; broker tests use `BuildSlotBrokerTests`' fake liveness, fake memory
  and `FakeTimeProvider`; the wrapper uses `BuildSlotScriptTests`' `C589_SLOT_SHIM` and
  `C589_COMMAND_SHIM` seams (the shim records renew calls with timestamps).
- Contract tests read the compose file, the entrypoint and the scripts as text (existing classes).
  Script fixtures follow the C495 pattern (temp root, seams file, `trace.jsonl`, sentinel token);
  nothing contacts 172xx, Docker or server2 except the L rows.
- Red first: each `Sn-tests` commit compiles (schema, DTO members, interface members with
  default bodies and stub classes throwing `NotImplementedException` are allowed in the red
  commit; behaviour is not), fails at the assertion the roster names, and is followed by the
  green row on the same filter. No wall-clock wait in a test above 3 s. No timeout widened, no
  assertion loosened; a failure not explained by the slice is re-run alone at the base commit and
  reported as inherited or owned.

### Coverage roster and decisive assertions

| ID | Class.Method | Assertion / red mechanism |
|---|---|---|
| V-1 | PhoneHomeConnectionTests.Second_boot_with_the_same_store_is_accepted_while_the_first_stays_live | Register A, connect; `RegisterAsync(bootId: B)` returns a ticket; after B connects, `peerA.Socket.State == Open` after 500 ms and `Directory.SnapshotBoots().Count == 2`. Today: the second register throws `phone_home_boot_conflict`. |
| V-2 | PhoneHomeConnectionTests.Third_live_boot_and_a_retired_boot_are_refused_with_boot_conflict | With A and B live, `RegisterAsync(bootId: C)` fails 409 `phone_home_boot_conflict` whose message names A and B; a boot marked `Retired` through the registry is refused the same way; a different store id is still `phone_home_store_mismatch`. Today: fails at the B registration. |
| V-3 | PhoneHomeConnectionTests.Same_boot_reconnect_supersedes_only_its_own_socket | A reconnects (same boot id): `peerA1.CloseObserved` completes, `peerB.Socket` stays open, `Status().Boots` still lists two boots with one epoch bump on A. Today: B refused. |
| V-4 | PhoneHomeRollingBootTests.Launch_binds_the_session_to_the_accepting_boot_and_reaches_only_that_peer | peerB recovers first (Accepting), peerA second (Standby); a new runner-bound task is claimed; the row's `RunnerBootId == B`; `peerB.Launches.Count == 1`, `peerA.Launches.Count == 0`. Red after S1: the column stays null, so the boot assertion fails while the launch still reaches B. |
| V-5 | PhoneHomeRollingBootTests.Input_to_a_session_bound_to_boot_A_reaches_peer_A_while_B_is_accepting | Row S_A stamped A; B accepting; `RoutingSessionRunnerClient.SendInputAsync(S_A, "x")` and the adapter's `SendInputAsync`: `peerA.Inputs.Count == 1`, `peerB.Inputs.Count == 0`. Red after S1: both reach B. |
| V-6 | PhoneHomeRollingBootTests.Inventory_is_the_union_and_a_session_absent_from_the_accepting_boot_is_not_failed | peerA lists S_A Running, peerB lists nothing; `ScanAsync`; S_A stays `Running`; `GetInventoryAsync("grok-linux")` is `Available` with one session; with peerA `SilentFor(List)` it is `Unavailable`. Red after S1: S_A `Failed` "does not know this session". |
| V-7 | PhoneHomeRollingBootTests.Capacity_counts_only_sessions_bound_to_the_accepting_boot | Capacity 1 on both peers, one Running row bound to A; a new task is claimed (no `RunnerAtCapacity` hold); a second new task is held. Red after S1: the first is held. |
| V-8 | PhoneHomeRollingBootTests.Events_from_a_boot_apply_only_to_sessions_bound_to_it | peerB emits a transcript event for S_A (bound A): no transcript row written; peerA emits the same: written. Red after S1: both written (owner match ignores the boot). |
| V-9 | PhoneHomeRollingBootTests.Roles_survive_a_directory_rebuild_from_the_registry | B Accepting, A Standby in the registry; a new `PhoneHomeRunnerDirectory` over the same registry; peers reconnect A first, then B; `Status().Boots` roles are still B Accepting and A Standby; `AcceptingBoot()` is B. Red: without the persisted role the rebuilt directory promotes the first recovered boot, A. |
| V-10 | PhoneHomeConnectionTests.Status_names_every_boot_with_role_epoch_and_eligibility | Two boots: `Boots.Count == 2`, roles Accepting and Standby, each `Epoch` equals its peer's, top-level `ProcessBootId` is the accepting boot's. Today: `Boots` null. |
| V-11 | PhoneHomeRollingBootTests.Legacy_rows_without_a_boot_route_to_the_boot_accepting_at_their_start | A row with `RunnerBootId == null` and `StartedAt` before B's `FirstSeenAt`; B accepting; `SendInputAsync` reaches peerA. Red after S1: reaches B. |
| V-12 | PhoneHomeRollingBootTests.Promote_makes_the_named_boot_accepting_and_drains_the_other | A Accepting, B Standby; `POST .../boots/{B}/promote` with token: 200; registry rows B Accepting (`PromotedAt`), A Draining (`DrainedAt`); a new claim stamps B; a rebuilt directory over the registry keeps A Draining. Today: 404. |
| V-13 | PhoneHomeRollingBootTests.A_draining_boot_still_serves_input_transcript_kill_and_release_for_its_sessions | After promote, `SendInput`, `GetTranscript`, `KillGeneration`, `ReleaseSlot` for S_A: `peerA.RequestCount(op) >= 1` for each and `peerB.RequestCount(op) == 0`. Red: `PromoteAsync` stub throws (S4-tests). |
| V-14 | PhoneHomeRollingBootTests.A_launch_for_a_session_already_bound_to_a_draining_boot_still_goes_to_that_boot | Row stamped A before promote; promote B; the queued launch runs: `peerA.Launches.Count == 1`, no typed refusal, `peerB.Launches.Count == 0`. Red: goes to B. |
| V-15 | PhoneHomeRollingBootTests.Promote_without_the_operator_token_is_forbidden_and_changes_no_role | 403 `operator_token_required`; roles unchanged. Today: 404. |
| V-16 | PhoneHomeRollingBootTests.Only_a_draining_boot_live_means_no_accepting_boot | After promote, `peerB.Socket.Abort()`; a new claim is held `RunnerUnavailable`; A's role stays Draining; status top-level `dispatchEligible == false`; a third peer C then recovers and is auto-promoted. Red: `PromoteAsync` stub throws (S4-tests); once promote exists, R1's fallback would promote A. |
| V-17 | BuildSlotBrokerTests.Renew_mode_reaps_a_lease_not_renewed_within_the_grace_and_ignores_pid_liveness | `HolderLiveness = renew`, fake liveness says dead; lease held after 10 s; reaped after `RenewGraceSeconds`; `Renew` inside the grace keeps it. Red: reaped at once by pid liveness. |
| V-18 | BuildSlotBrokerTests.Renew_extends_a_held_lease_and_an_unknown_lease_answers_false | `Renew(id)` true and `LastRenewedAt` moves; unknown id false. Red: method stub. |
| V-19 | BuildSlotEndpointTests.Post_renew_answers_204_for_a_held_lease_and_404_otherwise | Route contract. Today: 404 for both. |
| V-20 | BuildSlotEndpointTests.Build_slots_only_host_serves_health_and_build_slots_and_nothing_else | Host with `SessionRunner:BuildSlotsOnly=true`: `/build-slots` 200, `/health` 200, `/sessions` 404, `/capabilities` 404, no `PhoneHomeConnectionService` in services. Red: `/sessions` 200. |
| V-21 | BuildSlotScriptTests.Wrapper_renews_a_renew_mode_grant_while_the_command_runs | Shim grant `renewEverySeconds: 1`, command shim sleeps 3 s: shim log has >= 2 renew calls before the release; a pid-mode grant: 0 renew calls. Red: 0 renew calls. |
| V-22 | RunnerWorkspaceServiceTests.Mirror_waits_for_the_repository_lock_and_reads_the_tracking_ref | Test holds `<repo>/.git/antiphon-mirror.lock` exclusively; `MirrorAsync` does not run git until the lock is released (fake git records start time); a stale `FETCH_HEAD` pointing at another sha does not fail verification. Red: git runs at once; the stale `FETCH_HEAD` refuses. |
| V-23 | DockerStackContractTests.Server2_file_defines_two_runner_slots_a_broker_and_per_slot_nested_stores (replaces `Server2_file_defines_only_runner_and_state_init`) + Server2_slots_share_work_state_and_credential_homes_but_not_dind_or_tmp + Server2_broker_is_unprivileged_pinned_by_its_own_tag_and_reached_by_dns; slot-aware updates of `Only_the_server2_runner_is_privileged`, `Server2_runner_restarts_unless_stopped`, `Server2_runner_capacity_is_ten`, `Server2_services_name_their_images`, `Server2_compose_projects_claude_config_dir`, `Compose_never_lists_the_claude_token_under_environment`, `Server2_runner_has_nested_store_volume` | Block list is exactly `state-init, session-runner-a, session-runner-b, build-slots, antiphon-deploy-key, phone-home, work, runner-state, dind-data, dind-data-b`; each slot has its own `dind-data*` destination `/var/lib/docker`, no `/tmp` mount, `ANTIPHON_BUILD_SLOTS_URL: http://build-slots:8080/build-slots`, `profiles`; the broker has no `privileged`, `SessionRunner__BuildSlotsOnly: "true"`, image tag `${BUILD_SLOTS_SHA12`. Red: today's file. |
| V-24 | DindRunnerContractTests.Server2_compose_reads_the_staged_phone_home_secret / Server2_healthcheck_covers_phone_home_secret_readability | Both slot blocks carry the staged secret path and the three-leg healthcheck. Red: only `session-runner` exists. |
| V-25 | RemoteScriptContractTests.Rolling_cases_are_routed_and_have_stub_boundaries + Rolling_deploy_never_removes_orphans_and_keeps_both_slot_tags_and_the_broker_tag + Rolling_drain_updates_the_restart_policy_only_from_evidence | Roster has `'deploy-parent-rolling'` and `'rolling-drain-host'`; stub arms exist; `case_deploy_parent_rolling` contains no `--remove-orphans`, contains `--profile slot-`, `NestedStoreDiskLow`, `RunnerNotEligible`, `TargetSlotDraining`; keep-set includes `SLOT_A_SHA12`, `SLOT_B_SHA12`, `BUILD_SLOTS_SHA12`; `case_rolling_drain_host` reads `old-container.txt` and runs `docker update --restart=no`. Red: text absent. |
| V-26 | scripts/test-deploy-server2.ps1 T-1..T-4 | T-1 happy path: trace order `deploy-parent-rolling`, `promote`, `rolling-drain-host`; T-2 promote 409 leaves no `rolling-drain-host` trace and exits 2; T-3 missing token file exits 2 before any case; T-4 the sentinel token never appears in output and the script is ASCII. Red: script missing. |
| L-1 | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling` (desktop main checkout, after R1 is active) | Evidence: `docker-info-a.txt`/`docker-info-b.txt` daemon names equal each container's hostname, `nested-run-b.txt` exit 0, `bridge-nf.txt` from both, `status-after-promote.json` with A Draining and B Accepting, `restart-policy-old.txt` = `no`. |
| L-2 | `GET /api/session-runners/server2/provider-auth/{claude,codex,grok}?bootId=<A or B>` at promotion and >= 30 min later | Six `loggedIn` values unchanged between the two reads (evidence `provider-auth-overlap.json`). |
| L-3 | overlap log grep on both containers + `git worktree list` diff | Zero hits for `index.lock`, `packed-refs.lock`, `Mirror fetch failed`, `antiphon-mirror.lock`; the worktree list differs only by mirrors the desktop created or retired in the window. |
| V-27 | PhoneHomeCommandDispatcherTests.Retire_refuses_while_sessions_are_owned_unless_forced | `OwnedSessionCount = 1`: `Retire(force:false)` is 409 `phone_home_boot_busy` naming 1; `Retire(force:true)` calls `KillAllAsync` once and answers `RunnerRetireResult` with `KilledSessions == 1`. Today: `phone_home_unsupported_operation`. |
| V-28 | PhoneHomeCommandDispatcherTests.Retire_reply_is_written_before_the_host_stops | Recording lifetime: `StopApplication` is called after the reply task completed and within 1 s. Today: never called. |
| V-29 | RunnerBootRetireJobTests.Draining_boot_with_no_sessions_is_retired_after_the_idle_window | Fake clock; A draining, `Sessions` empty, no rows: run 1 stamps `IdleObservedAt`, sends nothing; advance `RetireIdleSeconds`; run 2: `peerA.Retires.Count == 1` with `force == false`, row `Retired` reason `idle`. Red: class stub. |
| V-30 | RunnerBootRetireJobTests.Live_session_or_busy_answer_keeps_the_boot_draining | peerA lists S_A: no send; peerA lists nothing but replies `phone_home_boot_busy`: no `Retired`, `IdleObservedAt` cleared. |
| V-31 | RunnerBootRetireJobTests.Standby_and_accepting_boots_are_never_retired | Two runs over idle Standby and Accepting rows: `Retires` empty, roles unchanged. |
| V-32 | RunnerBootRetireJobTests.Disconnected_draining_boot_with_no_attributed_rows_is_marked_retired_without_a_send | peerA aborted, lease expired on the fake clock: row `Retired` reason `idle_disconnected`; a disconnected boot with an attributed Running row stays Draining. |
| V-33 | PhoneHomeRollingBootTests.Forced_retire_requires_the_operator_token_and_the_boot_id_confirmation | 403 without token; 400 with a wrong `confirmBootId`; `peerA.Retires` empty in both. Today: 404. |
| V-34 | PhoneHomeRollingBootTests.Forced_retire_fails_the_boot_sessions_with_the_reason_sends_a_forced_retire_and_writes_an_incident | S_A `Failed`, `FailureReason` contains the boot id and the reason, `TerminationSource == SystemRequest`; `peerA.Retires` has one frame with `force == true`; one `RunnerBootForceRetired` incident; row `Retired`. Today: 404. |
| V-35 | PhoneHomeRollingBootTests.Forced_retire_of_the_accepting_boot_is_refused | 409 `phone_home_boot_accepting`; nothing sent. Today: 404. |
| V-36 | PhoneHomeRollingBootTests.Status_lists_each_boot_with_its_remaining_sessions_and_retire_fields | A draining with one Running row: `Boots[A].Sessions == 1`, `RunnerSessions == 1`, `DrainedAt` set; after retire `RetiredAt` and `RetireReason` set; `GET .../slots` rows carry `bootId`. Red: fields absent. |
| V-37 | PhoneHomeRollingBootTests.Rolling_upgrade_moves_new_launches_to_B_keeps_A_reachable_and_retires_A_when_idle | The brief's integration test, one method: peerA hosts S_A (Running row bound A); peerB connects and recovers; promote B; a new task launches on B only; input to S_A reaches peerA only; peerA emits S_A's exit and clears `Sessions`; advance the clock; two job runs; `peerA.Retires.Count == 1`; status: A Retired, B Accepting with one session. Red after R2: the retire step (job stub). |
| V-38 | RunnerBootsScriptTests.Script_is_ascii_offers_the_three_verbs_and_sends_the_token_without_printing_it | Text contract on `scripts/runner-boots.ps1`. Red: file missing. |

Regression classes executed in the green rows, counts at `598a522f`: `PhoneHomeConnectionTests`
23, `PhoneHomeDirectoryTests` 7, `PhoneHomeReconciliationTests` 4, `PhoneHomeSessionRoutingTests`
5, `PhoneHomeLaunchTransportTests` 7, `PhoneHomeEpochAgreementTests` 2, `PhoneHomeEventPumpTests`
8, `PhoneHomeRecoveryEligibilityTests` 3, `PhoneHomePendingInventoryTests` 8,
`RunnerSlotEndpointTests` 8, `SessionReconciliationServiceTests` 57 methods (14 argument-expanded),
`DispatcherRemotePrepStarvationTests` 1 method (4 results), `DefaultRunnerCreateTests` 7,
`DefaultRunnerEligibilityTests` 3, `OperatorShutdownEndpointTests` 3, `DockerStackContractTests`
108, `DindRunnerContractTests` 22, `RemoteScriptContractTests` 25, `DockerStackDocumentationTests`
10, `BuildSlotBrokerTests` 11, `BuildSlotEndpointTests` 2, `BuildSlotScriptTests` 7,
`PhoneHomeCommandDispatcherTests` 34, `PhoneHomeConnectionServiceTests` 10,
`RunnerWorkspaceServiceTests` 14. Code reads the fresh TRX if a class has grown by then.

### Execution and evidence

Run each TUnit row with `pwsh -NoProfile -File scripts/run-checkpoint.ps1` and the exact filter,
`-MinExecuted` and comma-separated `-Expect`; results roots `.antiphon/c727/CP-n-<sha>`, fresh
per run. Build outputs are `bin-c727-rN/` (forward slash), one per round and assembly, removed
after the round's awaited runs. On server2 the script adds `UseAppHost=false` itself
(CARD-0671). Red rows return exit 1 with the named methods in `FAILED` lines; that exit is the
evidence. Green rows require zero failed and zero skipped among the new methods and every listed
regression class executed. Script and live rows are non-TUnit: Code produces the `CHECKPOINT`
line by hand with the harness's own counts and the command in `filter=`. The migration row is a
command whose success is an empty `has-pending-model-changes` answer. `Antiphon.Tests` and
`Antiphon.SessionRunner.Tests` never run concurrently. No row runs a namespace or the full
assembly. Source is frozen during a run.

### Cost

R1: three slices, about 6.5 hours of authoring; R2: five slices, about 9.5 hours plus the live
rows; R3: three slices, about 5.5 hours. Ordinary checkpoint floors from the tables: R1 **27**
minutes, R2 **43** minutes (plus L-1..L-3, about 60 minutes of wall clock dominated by the
30-minute overlap wait), R3 **24** minutes. Suggested `-ExpectAbout`: R1 450 minutes, R2 650
minutes, R3 400 minutes.

### Checkpoints

The closed lists for Code, one table per round. `Sn-tests` means the slice's compiling red tests
are committed before its production change; `Sn` means the production change is committed.
Filters use the CARD-0403 combined-class syntax; the backslashes before `|` are Markdown escaping
only. Red rows require the named assertion failures, not any failure.

#### R1 checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-tests | `tests/Antiphon.Tests -> bin-c727-r1/` | boots-red | `/*/*/PhoneHomeConnectionTests*/*` | V-1, V-2, V-3, V-10 | 4 new + 23 existing executed; V-1, V-2, V-3, V-10 fail at their register/socket/status assertions | 27 | 4 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c727-r1/` | boots-green | `/*/*/PhoneHomeConnectionTests*/*` | V-1, V-2, V-3, V-10; connection regressions | all listed, 0 failed/skipped | 27 | 4 |
| CP-3 | S2-tests | `tests/Antiphon.Tests -> bin-c727-r1/` | routing-red | `/*/*/(PhoneHomeRollingBootTests*)\|(PhoneHomeDirectoryTests*)/*` | V-4 to V-9, V-11 | 7 new + 7 existing executed; the 7 new fail at their boot/peer/inventory/capacity/role assertions | 14 | 5 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c727-r1/` | routing-green | `/*/*/(PhoneHomeRollingBootTests*)\|(PhoneHomeDirectoryTests*)\|(PhoneHomeSessionRoutingTests*)\|(PhoneHomeLaunchTransportTests*)\|(PhoneHomeEventPumpTests*)\|(PhoneHomeRecoveryEligibilityTests*)\|(PhoneHomePendingInventoryTests*)\|(PhoneHomeEpochAgreementTests*)/*` | V-4 to V-9, V-11; phone-home routing, launch, pump and inventory regressions | all listed classes, 0 failed; 7 new methods, 0 skipped | 47 | 6 |
| CP-5 | S2 | CP-4 | reconcile-capacity-green | `/*/*/(PhoneHomeReconciliationTests*)\|(SessionReconciliationServiceTests*)\|(DispatcherRemotePrepStarvationTests*)\|(RunnerSlotEndpointTests*)\|(DefaultRunnerCreateTests*)\|(DefaultRunnerEligibilityTests*)/*` | D-7, D-8 regressions | all listed classes, 0 failed (57 reconciliation methods expand to more results) | 83 | 6 |
| CP-6 | S2 | n/a | migration-clean | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c727-ef -- dotnet ef migrations has-pending-model-changes --project server` | D-2, D-3 | exit 0, "No changes have been made to the model" | n/a | 2 |

R1 floor = 4 + 4 + 5 + 6 + 6 + 2 = **27** minutes.

#### R2 checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-7 | S4-tests | `tests/Antiphon.Tests -> bin-c727-r2/` | drain-red | `/*/*/PhoneHomeRollingBootTests*/*` | V-12 to V-16 | 5 new + 7 existing executed; V-12 to V-16 fail at their route/role/peer assertions | 12 | 5 |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c727-r2/` | drain-green | `/*/*/(PhoneHomeRollingBootTests*)\|(RunnerSlotEndpointTests*)\|(OperatorShutdownEndpointTests*)\|(PhoneHomeConnectionTests*)/*` | V-12 to V-16; operator-route and connection regressions | all listed, 0 failed/skipped | 50 | 6 |
| CP-9 | S5-tests | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r2s/` | broker-red | `/*/*/(BuildSlotBrokerTests*)\|(BuildSlotEndpointTests*)/*` | V-17 to V-20 | 4 new + 13 existing executed; V-17 to V-20 fail at their reap/renew/route/mode assertions | 17 | 4 |
| CP-10 | S5 | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r2s/` | broker-green | `/*/*/(BuildSlotBrokerTests*)\|(BuildSlotEndpointTests*)\|(BuildSlotSettingsTests*)/*` | V-17 to V-20; broker regressions | all listed, 0 failed/skipped | 17 | 4 |
| CP-11 | S5-tests | CP-7 | wrapper-red | `/*/*/BuildSlotScriptTests*/*` | V-21 | 1 new + 7 existing executed; V-21 fails at the renew count | 8 | 3 |
| CP-12 | S5 | `tests/Antiphon.Tests -> bin-c727-r2/` | wrapper-green | `/*/*/BuildSlotScriptTests*/*` | V-21; wrapper regressions | all listed, 0 failed/skipped | 8 | 3 |
| CP-13 | S6-tests | CP-10 | mirror-lock-red | `/*/*/RunnerWorkspaceServiceTests*/*` | V-22 | 1 new + 14 existing executed; V-22 fails at the lock-wait assertion | 15 | 3 |
| CP-14 | S6 | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r2s/` | mirror-lock-green | `/*/*/RunnerWorkspaceServiceTests*/*` | V-22; mirror regressions | all listed, 0 failed/skipped | 15 | 3 |
| CP-15 | S7-tests | CP-12 | compose-red | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)\|(RemoteScriptContractTests*)/*` | V-23 to V-25 | new and updated methods fail at their text assertions; unrelated methods pass | 160 | 4 |
| CP-16 | S7 | `tests/Antiphon.Tests -> bin-c727-r2/` | compose-green | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)\|(RemoteScriptContractTests*)\|(DockerStackDocumentationTests*)/*` | V-23 to V-25; compose, entrypoint, script and docs contracts | all listed, 0 failed/skipped | 170 | 5 |
| CP-17 | S7-tests | n/a | wrapper-script-red | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | V-26 | exit 1: the script under test is missing (T-1 to T-4 FAIL) | n/a | 1 |
| CP-18 | S7 | n/a | wrapper-script-green | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | V-26 | 4 cases, every `PASS`, exit 0 | n/a | 2 |
| CP-19 | R1 active on the desktop + S7 landed | n/a | live-rolling | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling` (desktop main checkout) | L-1 | exit 0; evidence files as L-1 names; status A Draining, B Accepting | n/a | 20 |
| CP-20 | CP-19 | n/a | live-auth-overlap | the six `provider-auth` reads at promotion and >= 30 min later | L-2 | `loggedIn` unchanged for claude, codex, grok on both boots | n/a | 35 |
| CP-21 | CP-20 | n/a | live-git-window | log grep and `git worktree list` diff over the overlap window | L-3 | zero lock hits; worktree diff explained by desktop activity | n/a | 5 |

R2 ordinary floor = 5 + 6 + 4 + 4 + 3 + 3 + 3 + 3 + 4 + 5 + 1 + 2 = **43** minutes; the live rows
add about 60 minutes on the desktop lane and are reported not run by a server2-placed task.

#### R3 checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-22 | S9-tests | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | retire-op-red | `/*/*/PhoneHomeCommandDispatcherTests*/*` | V-27, V-28 | 2 new + 34 existing executed; V-27, V-28 fail at the unsupported-operation answer | 36 | 4 |
| CP-23 | S9 | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | retire-op-green | `/*/*/(PhoneHomeCommandDispatcherTests*)\|(PhoneHomeConnectionServiceTests*)/*` | V-27, V-28; dispatcher and connection regressions | all listed, 0 failed/skipped | 46 | 5 |
| CP-24 | S10-tests | `tests/Antiphon.Tests -> bin-c727-r3/` | retire-job-red | `/*/*/(RunnerBootRetireJobTests*)\|(PhoneHomeRollingBootTests*)/*` | V-29 to V-37 | 9 new + 12 existing executed; the 9 new fail at their send/role/route/status assertions | 21 | 5 |
| CP-25 | S10 | `tests/Antiphon.Tests -> bin-c727-r3/` | retire-job-green | `/*/*/(RunnerBootRetireJobTests*)\|(PhoneHomeRollingBootTests*)\|(RunnerSlotEndpointTests*)\|(PhoneHomeConnectionTests*)\|(PhoneHomeDirectoryTests*)/*` | V-29 to V-37; slot, connection and directory regressions | all listed, 0 failed; 9 new methods, 0 skipped | 63 | 6 |
| CP-26 | S11-tests | CP-24 | boots-script-red | `/*/*/RunnerBootsScriptTests*/*` | V-38 | 1 executed; fails because the script is missing | 1 | 1 |
| CP-27 | S11 | `tests/Antiphon.Tests -> bin-c727-r3/` | boots-script-green | `/*/*/(RunnerBootsScriptTests*)\|(DockerStackDocumentationTests*)/*` | V-38; docs contracts | all listed, 0 failed/skipped | 11 | 3 |

R3 floor = 4 + 5 + 5 + 6 + 1 + 3 = **24** minutes.

`Min` counts executed TUnit results from the class sizes above; the `Expect` tokens are the new
method names of the row plus one token per regression class. A DB-backed row needs the test
Postgres the assembly already uses (`TestDbFixture`); on server2 that is the nested child stack
the `dotnet-filter` case provides, as for CARD-0679 and CARD-0716.

## Follow-up card: desktop runner and 17204

File as a separate Backlog card ("Rolling desktop session-runner upgrade on 17204") with this
scope, not part of R1 to R3:

- A second Windows runner process on a port other than 17204 with its own `PtyHostManifestDir`
  (sharing today's directory would make it adopt the old process's hosts at startup through
  `AdoptOrphanedHostsAsync`, which moves the sessions instead of leaving them).
- A local session binding that records which base URL owns the session (today local rows have a
  null `RunnerId` and one `SessionRunner:BaseUrl` client), and a `RoutingSessionRunnerClient`
  that follows it; the D-2 registry extended to local boots so the same promote/drain/retire
  verbs apply.
- Warm-pool reuse must refuse a draining process (`AgentTaskReplyService.cs:2053-2061`,
  `AgentTaskDispatcher.cs:5991-5994` match `Agent.RunnerId` only).
- 17204 stays the old process's port until it retires; then either the new process rebinds 17204
  or the server's base URL moves. The AppHost health check (`Antiphon.AppHost/Program.cs:27-33`,
  `:64`), `scripts/restart-session-runner.ps1`, `scripts/session-runner-restart-health.ps1`,
  `scripts/lib/build-slot.ps1`'s Windows default, `verify-dev-stack.ps1` and
  `scripts/check-daemon-build.ps1` all name 17204 and need the same decision.
- `restart-session-runner.ps1 -Rolling` as the thin caller; the existing adoption restart stays
  the non-rolling path.
- Unchanged and out of scope for it too: the E2E random-port guard
  (`tests/Antiphon.E2E/ReleaseGateIsolationTests.cs:53`) and `ProductionRunnerGuard`.

## Defaults stated for TestDesign and Code

- D-6's `MaxLiveBoots` is 2; a third generation is refused rather than queued.
- Promotion is explicit (D-5); the only automatic promotion is a `Standby` or newly recovered
  boot when no live `Accepting` boot exists, and a `Draining` boot is never auto-promoted.
- R1 ships `Standby` and `Accepting`; `Draining`, `PromoteAsync` and the promote route are R2.
  R1's tests therefore order the peers (first recovered accepts) instead of promoting.
- The custody root stays shared (D-12); the rolling case skips the residue probe.
- The broker uses renewal, not pid liveness, only when configured so; the desktop broker and
  wrapper behaviour is unchanged.
- The first rolling deploy targets slot `b`; slot `a` inherits today's `dind-data` volume and
  today's container is removed by the next deploy of slot `a` after its boot is `Retired`.
