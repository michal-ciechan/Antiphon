# CARD-0727: rolling session-runner upgrade with multiple runner ids, server2 first

Date: 2026-09-25. Stage: Plan, revision 2 (task `f908a771`, written on the server2 Linux runner;
revision 1 was task `00f66c8f` at `25e41f35`). Source inspected: `origin/master` at `7e912b7b`
(this worktree is `25e41f35`, master plus the two CARD-0727 docs commits) and the **CARD-0710
Code branch** `origin/feat/card-task-8752034b` at `fdf3778a` (task `be035022` succeeded; task
`8752034b`, "Final review + land-if-clean", was Dispatched at 21:30). Investigation:
[2026-09-25-card-0727-rolling-runner-upgrade.md](../../investigations/2026-09-25-card-0727-rolling-runner-upgrade.md)
(task b69ddb64). Task `dad7cd6a` (Custom, canceled after its Phase A) scoped the manual
`server2-temp` deploy and found CARD-0729; its findings are quoted where they bind. Neighbouring
plans: [CARD-0710 platform placement](2026-09-25-card-0710-task-platform-placement-plan.md)
(D-6 runner collection, D-9 runtime runner defaults), [CARD-0716 graceful restart](2026-09-25-card-0716-graceful-restart-phone-home-plan.md)
(operator token route shape), [CARD-0679 launch loss](2026-09-24-card-0679-phone-home-launch-loss-plan.md)
(runner-scoped client, per-connection inventory). Next stage: **TestDesign**, which owns the final
roster; the `## Verification design` below is the plan's closed list and red-first mechanism for it.

**What this revision replaces.** The operator decided to keep ONE live connection per runner id.
Revision 1's same-id multi-boot design (a `ProcessBootId`-keyed directory, `Standby`/`Accepting`
boots, `MaxLiveBoots`, a `RunnerBoots` table, per-boot session stamping and routing) is dropped
entirely. The rolling upgrade uses **multiple runner ids**: a temporary `server2-temp` runner in
its own compose project, a per-runner Draining flag, and a Hangfire retire.

No code, configuration, test or deployment changed during Plan. No live registration was sent.
The caller's standing authority ("make multiple runners top priority; the rolling upgrade should
use the same mechanism") covers everything designed here; the one thing it does not settle is
named in the report (the R1 dependency on CARD-0710 landing).

## Outcome and scope

The operator's workflow, which this plan automates end to end:

1. Deploy `server2-temp` on the new image, as its own runner id and its own compose project.
2. Queue work there (`-Runner server2-temp`) to prove the new image.
3. Drain `server2`, wait until it is idle, redeploy it, clear its drain.
4. Drain `server2-temp` and retire it once it is idle.

Three rounds, server2 only:

- **R1** (server config, tests, docs): the multi-runner mechanism is CARD-0710 D-6 as it lands
  (`PhoneHomeRunner:Runners`, one `RunnerSlot` per id). R1 adds the `server2-temp` entry, this
  card's own two-id routing pin on the real dispatcher graph, and the CARD-0729 HTTP contract
  (an unknown id is 404 and never eligible).
- **R2** (server): a durable per-runner Draining state with a redirect; a draining runner takes no
  new claims, still serves every call for its sessions, and unlaunched work bound to it moves to
  the redirect target so callers never change `-Runner` by hand.
- **R3** (server, runner, compose, scripts): the `Retire` operation, the `RunnerRetireJob`
  Hangfire job, the forced retire behind a confirmation, the temp compose project, the shared
  build-slot broker with lease renewal, `deploy-server2.ps1 -Rolling`, `runner-drain.ps1`, and the
  live proof on server2.

Out of scope, kept for the follow-up card at the end: the desktop runner on 17204 and
`restart-session-runner.ps1 -Rolling`; a runtime-editable runner catalogue (adding a runner id is
still a settings change plus a server restart); rollback automation (the manual rollback is
documented in D-18).

## Ground truth

What the card, the brief and task dad7cd6a assume against what the code does. "master" is
`7e912b7b`; "0710 tip" is `fdf3778a` on `origin/feat/card-task-8752034b`. Lines are from those
commits.

| # | The card, brief or dad7cd6a assumes | What the code does |
|---|---|---|
| 1 | `PhoneHomeRunnerSettings.AllowedRunnerId` must become a set (`AllowedRunnerIds`). | On master it is one string (`PhoneHomeRunnerSettings.cs:8`) and `Register` refuses every other id with `phone_home_runner_mismatch` (`PhoneHomeRunnerDirectory.cs:175`). On the 0710 tip it is a **map**: `PhoneHomeRunner:Runners` of `PhoneHomeRunnerEntry` (own `SharedSecret`, `HostWorkspaceRoot`, homes, `AllowDelegatedTasks`, `MaxCapacity`, `CallbackOrigin`; `PhoneHomeRunnerSettings.cs:118-144`), `PhoneHomeRunnerCatalog.Resolve/Configured/FromLegacy` normalise the legacy singleton into one entry (`:148-208`), and `ValidateMapped` requires unique ids and unique pins but allows two entries with the same secret value (`:300-345`). The brief's set is superseded by the map; a second shape would be the "string array only" alternative 0710 rejected. |
| 2 | Live connection, store/boot/epoch, pending inventory, disconnect record, seats and Status must be kept per runner id. | 0710 tip: `PhoneHomeRunnerDirectory` keeps a `Dictionary<string, RunnerSlot>` (`:28`, class at `:583-599`) with `Live`, `LastRecovered`, `StoreId`, `BootId`, `LeaseUntil`, `Epoch`, `Reconnects`, `LastDisconnect`, registered platform, capabilities and capacity; `Register` and `AcceptConnect` act on the requested id's slot (`:195-296`); `AuthenticateSecret(runnerId, secret)` checks that entry's secret (`:182-193`); `GetLiveStoreId(runnerId)` replaces `LiveStoreId` (`:77`; interface `ISessionRunnerDirectory.cs`); `LiveRemoteSessionIds`/`UnknownRemoteSessionIds` are unions over slots (`:372-448`); `PendingRunnerSessionInventory` is keyed by id; `PhoneHomeRecoveryPump` runs one `Cycle` per runner with its own pump, catch-up retry and refresh (`RunOneAsync`, `Cycle` class). `MultiRunnerDirectoryTests` (7), `MultiRunnerRecoveryTests` (7) and `MultiRunnerProjectionTests` (4) pin it. |
| 3 | CARD-0729: `Status(runnerId)` ignores its argument; `GET /api/session-runners/server2-temp/status` is a false pass. | True on master (`Status` reads the single `_live`, `:395-435`). On the 0710 tip `Status` throws `NotFoundException("SessionRunner", id)` for an id not in the map (`:459-468`) and answers a not-available DTO with `DisconnectReason: "desktop"` for the desktop alias; `MultiRunnerDirectoryTests.Inventory_status_capacity_and_store_are_keyed` asserts the throw at the directory. The HTTP contract of the route (`SessionRunnerEndpoints.cs:47`, mapped through `ExceptionMiddleware`) is not pinned, and no script treats a non-200 as "not eligible": `c590-remote.sh` `deploy-parent` only records `status.json` (`:1379`). |
| 4 | Sessions already record `RunnerId`; per-session calls route by it. | `AgentSessions.RunnerId/RunnerStoreId/RunnerCwd` all-or-none (`AppDbContext.cs:1139`); `RoutingSessionRunnerClient.Route` resolves `owner.RunnerId` (`RoutingSessionRunnerClient.cs:85`); every adapter's `RunnerScopedSessionRunnerClient.Current` is `Resolve(RunnerId)` (`RunnerScopedSessionRunnerClient.cs:30`); `AgentSessionRuntime.EnsureInputTransportAvailableAsync` resolves the binding's runner (`AgentSessionRuntime.cs:1366-1375`). On the 0710 tip `Resolve(runnerId)` returns that id's own socket (`:85-97`). Unchanged by this card. |
| 5 | A "draining" state exists, or can reuse `DispatchEligible`. | None. `DispatchEligible` is the only gate; `Resolve` refuses every call while it is false (`:90-91`) and the connection refuses Launch, Input, ConditionalInput, KillGeneration, ClearBuffer and Resize on an ineligible socket (`PhoneHomeLiveConnection.cs:306-309`). The only server-side "Drain" words are unrelated (alert throttle, channel bridge, card-file policy). |
| 6 | New work can be routed off a draining runner through the CARD-0710 runtime default runner. | 0710 tip: `RunnerRoutingSettings` (fixed key `fleet`, `GlobalRunnerId`, `RunnerKindDefaults`, append-only `RunnerRoutingRevisions`; `RunnerRoutingSettings.cs`), `RunnerDefaultSettingsService` (`GET`/`PUT /api/runner-defaults`, `EnsureInitializedAsync` imports `Delegation:DefaultRunnerId` once), and `DefaultRunnerRoutingPolicy.ApplySnapshot` plus `TryKindDefault` (`DefaultRunnerRoutingPolicy.cs`, diff hunks at `Decide`). A default that is not dispatch-eligible falls back to the **desktop** with `runner_not_dispatch_eligible`; there is no notion of "try another remote". The dispatcher never re-reads defaults for a queued task (0710 D-10), so a runtime edit does not move queued work. |
| 7 | Explicit `-Runner x` tasks can be moved by the server. | Today never: `RunnerRequestSource.ExplicitRemote` is persisted and "never replaced or fallen back" (`DefaultRunnerRoutingPolicy.cs:20-22`, 0710 D-4/D-10). The dispatcher's `RemoteHoldForAsync` holds a task whose runner is unavailable (`AgentTaskDispatcher.cs:917-945`) and `CountRunnerOccupancyAsync` counts non-terminal rows plus prepared-but-unlaunched tasks plus in-flight mirrors per id (`:948-966`). 0710 adds `RunnerSelectionSource` on the task. |
| 8 | Retire can be automatic. | The runner has no stop operation; the closest is `POST /sessions/kill-all` (`src/Antiphon.SessionRunner/Program.cs:346`). `PhoneHomeOperation` ends at `ObserveCompaction = 25` on master and `LaunchPlatformConstrained = 26` on the 0710 tip. `dind-entrypoint.sh` exits when either child exits (`docker/session-runner-grok/dind-entrypoint.sh:165-190`) and the container is `restart: unless-stopped` (`docker-compose.server2-runner.yml:46`, pinned by `DockerStackContractTests.Server2_runner_restarts_unless_stopped` and `c590-remote.sh:1347-1350`). Hangfire runs on the desktop (`HangfireConfiguration.AddOrUpdateRunnerSlotReconcileJob`, `server/Program.cs:916`; job at `RunnerSlotReconcileJob.cs`). |
| 9 | Two containers on one host, separate state. | The compose project name is fixed `antiphon-runner` (`docker-compose.server2-runner.yml:6`; `c590-remote.sh:23`), with named volumes `work`, `runner-state`, `dind-data` (`:132-136`), `/tmp` container-local, the Codex home a host bind (`RUNNER_CODEX_HOME_DIR` → `/state/codex`), the Claude token a read-only file bind, the git identity a read-only file bind, and the deploy key and phone-home secret compose file secrets. A second project name gives its own three volumes for free; a runner-state volume produces a fresh store id on first boot (`PhoneHomeStoreIdentity.LoadOrCreate`) and its own `launch-generations` (`PhoneHomeSettings.ResolvedLaunchGenerationsPath`). `deploy-parent` rewrites the single `stack.env`, runs `up -d --no-build --remove-orphans` (`:1257`) and retires every image tag but the deployed sha12 (`retire_superseded_server2_images`, `:990-1004`). `DockerStackContractTests.Server2_file_defines_only_runner_and_state_init` pins the block list (`:237-246`). Compose on server2 is 2.18 (no volume subpath; `c590-remote.sh:1362`). |
| 10 | Login stores can be shared. | dad7cd6a: share only the login stores, not `runner-state` (it holds the store id and the session records; a second runner using it would take server2's identity and try to adopt its sessions). Claude: the token file bind, read-only, already a host file. Codex: `secrets/codex/` host directory, read-write bind. Grok: the store lives on server2's `runner-state` volume at `grok/`; the only way to share it is a host-path bind of that volume's mountpoint subdirectory. Codex and Grok rotate tokens on refresh; two runners can occasionally sign each other out (accepted by the operator). |
| 11 | Build slots can be shared. | `BuildSlotBroker` is process memory with a pid liveness sweep and a 90-minute TTL (`BuildSlotBroker.cs:139-172`, `BuildSlotSettings.cs:25`); `Enabled:false` answers unlimited (`:61`). The wrapper's Linux default is `http://127.0.0.1:8080/build-slots` and `ANTIPHON_BUILD_SLOTS_URL` overrides it (`scripts/lib/build-slot.ps1:26-29`); it never renews. A pid seen from another container's namespace is a different process. Today's server2 container has no `/build-slots` at all (dad7cd6a), so its agents already run unleased. |
| 12 | The shared checkout is a lock risk. | Only when two runners share one `work` volume. Two compose projects have two `work` volumes and two checkouts; `RunnerWorkspaceService.MirrorAsync` reads `FETCH_HEAD` inside one process only (`RunnerWorkspaceService.cs:93-108`). Revision 1's cross-process mirror lock (its D-16) is therefore dropped; the intra-process `FETCH_HEAD` read is pre-existing and not this card's. |
| 13 | The test host can script two runners. | 0710 tip: `PhoneHomeTestHost.StartAsync(configured:)` takes a full `PhoneHomeRunnerSettings` (the map), `RegisterAsync(runnerId:, storeId:, secret:, platform:, capabilities:)` and `ConnectPeerAsync(runnerId:, storeId:, secret:)` connect a peer to a named id's route, `WaitLiveAsync(runnerId:)`; `MultiRunnerDirectoryTests.Pair(secretA, secretB)` is the two-entry fixture. `PhoneHomeLaunchTransportTests.LaunchWorld` seeds a remote-bound row in an isolated schema with the real dispatcher graph (`:314-420`). `PhoneHomeScriptedPeer` records `Launches`, `Inputs`, `RequestCount(op)`, `Sessions`. |
| 14 | Host-lane cases are routed automatically. | A live case reaches server2 only when it is in `$script:C590LiveCases` (`scripts/c590-real.ps1:10-40`) and has a stub arm in `scripts/verify-docker-stack.ps1` (`:170-232`); `Invoke-C590LiveCase` scps `c590-remote.sh` and runs one `C590_CASE` over SSH with `C604_SERVER_ORIGIN` (`:207-300`); `RemoteScriptContractTests` (25) enforce routing and refusals. The operator token stays on the desktop (`Operator:TokenPath`); `runner-slots.ps1` is the token-helper shape (`scripts/runner-slots.ps1:35-47`). |
| 15 | Warm pooled processes on the old runner must be released. | Runner-bound tasks are Worktree only (`PhoneHomeLaunchPolicy.RefuseUnsupportedStart`) and are never pooled warm (`AgentTaskReplyService.cs:2053` requires Shared); reuse matches `Agent.RunnerId` (`AgentTaskDispatcher.cs:5991-5994`). Nothing to release on server2. |

## Decisions

### R1: multiple runner ids are CARD-0710's mechanism

**D-1. The mechanism is `PhoneHomeRunner:Runners` as CARD-0710 lands it; this card adds no second
shape.** `AllowedRunnerId` stays as the legacy import key that `PhoneHomeRunnerCatalog.FromLegacy`
normalises; the production configuration switches to the map (D-2). Why: the map already keys
connection, store, boot, epoch, disconnect record, capacity, inventory, pump and secret by id,
which is the whole of the brief's R1 list; a parallel `AllowedRunnerIds` set would double every
routing, validation and status path and is the "string array only" alternative 0710 D-6 rejected.
Rejected: implementing R1 on master ahead of 0710 (two directories in flight on the same file;
0710's Code succeeded and is in Review). Dependency: R1's Code dispatch starts from a master that
contains 0710's `5f39210c` ("key phone-home runners"); until task `8752034b` lands, R1 is blocked,
not re-planned.

**D-2. `server2-temp` is a second map entry with the same values and the same secret.**
`PhoneHomeRunner:Runners:server2` carries today's singleton values; `Runners:server2-temp`
carries identical `HostWorkspaceRoot`, `RunnerWorkspace`, `RunnerRepository`, `RawExeAllowList`,
`MaxCapacity` 10, child homes, probe flags, `CallbackOrigin`, `AllowDelegatedTasks: true`,
`DisplayName` "server2 (temp)", and the same `SharedSecret` value (the temp container mounts
server2's `secrets/phone-home` file, dad7cd6a; the validator admits duplicate secret values, row
1). The entry stays configured permanently: offline it costs nothing and shows as unavailable in
the catalogue; adding it per upgrade would need a server restart each time. Activation is one
user-secrets edit in the `antiphon-server` store plus `restart-apphost.ps1`, written into
`docs/testing-and-build.md`'s enablement steps (which today say `AllowedRunnerId=server2` and are
pinned by `DockerStackDocumentationTests.Testing_doc_names_the_production_enablement_steps`).
Rejected: a distinct secret per entry (a second file and a second user-secret for no custody gain
on one host with one owner); enabling the temp entry only during upgrades.

**D-3. Per-session routing needs no code; R1 pins it with this card's own test.** Rows 4 and 13:
every session call already follows `RunnerId`, and on the 0710 tip that reaches the id's own socket.
R1 builds the `RollingWorld` harness (two entries `server2` and `server2-temp`, isolated schema,
`BridgeQueueHarness` with the real `AgentProtocolAdapterFactory` and `AgentTaskDispatcher` bound to
`host.Directory`, a seeded `RunnerRoutingSettings` row) and asserts: a new default-placed task
launches on the `server2-temp` peer only while an input to a session bound to `server2` reaches the
`server2` peer only (V-2). R2 and R3 reuse the harness. Why a test of already-green behaviour: it is
the card's acceptance ("a message sent to a session on the old generation still arrives") and the
base every later round mutates; its red is proven by the post-land Mutation controls PC-1/PC-2,
not by a red commit (there is no R1 production change to precede).

**D-4. CARD-0729 is closed by the HTTP contract plus every script caller.** R1 pins
`GET /api/session-runners/server2-temp/status` before any registration as **404** with no
`runnerStoreId`, `processBootId` or `buildVersion` in the body, `GET .../server2/status` unchanged,
and `GET .../desktop/status` as `available:false, dispatchEligible:false` (V-1). Every script this
card adds treats a non-200 status as "not eligible" (`deploy-server2.ps1`, `runner-drain.ps1`);
`docs/ops-http.md` says so. CARD-0729 is moved to Done when R1 lands (the orchestrator's move).
Rejected: a 200 with `dispatchEligible:false` and `reason: unknown_runner` (the 0710 tip already
chose 404; a second shape for the same answer).

**D-5. One live connection per runner id stays the rule.** `PhoneHomeConnectionTests.Live_boot_and_store_identity_cannot_be_replaced`
is unchanged; no `MaxLiveBoots`, no boot table, no per-boot session column. A runner restart is
still the same id with a new `ProcessBootId` after its lease expires. This is the operator's
decision and the reason the temp runner has its own id.

### R2: a per-runner Draining state with a redirect

**D-6. Draining is a durable per-runner row, mirrored into the directory.** Entity
`SessionRunnerState` (`RunnerId varchar(64)` PK, `Draining bool`, `DrainedAt?`, `DrainReason
varchar(200)?`, `RedirectTo varchar(64)?`, `RetireWhenIdle bool`, `IdleObservedAt?`, `RetiredAt?`,
`RetireReason varchar(200)?`, `UpdatedAt`, `UpdatedByTaskId?`), migration `AddSessionRunnerStates`
(`dotnet ef migrations add AddSessionRunnerStates --project server`, checked with
`has-pending-model-changes`). `RunnerStateService` (scoped) writes the row and pushes the new
state into `PhoneHomeRunnerDirectory.ApplyState(runnerId, state)`; the directory keeps a
`RunnerState` copy per `RunnerSlot` for its synchronous gates; `RunnerStateLoader` (hosted service
registered before `PhoneHomeRecoveryPump`) loads every row at startup, and hosts without a database
(the test host with no connection string) start empty. Why durable: the desktop restarts during
an upgrade window and must not un-drain a runner mid-drain; the retire job needs the draining set;
the row is the audit (who drained, why, when idle, when retired). Rejected: directory memory only
(a restart un-drains); a flag in the `Runners` config (a restart per upgrade); a
`RunnerRoutingRevisions` entry (wrong resource; that is the placement preference, not a runner's
state); a registration-request flag (the runner does not decide placement).

**D-7. Draining gates new work only; `Resolve` is untouched.** `ISessionRunnerDirectory` gains
`ResolveForNewWork(string? runnerId)` (default body: `Resolve`), which in the directory is
`Resolve` plus a `ServiceUnavailableException` with code `phone_home_runner_draining` when the slot
is draining or retired. `Resolve` keeps serving everything else: input, buffer, transcript,
snapshot, kill, kill-generation, release, compaction stop, inventory, slots, provider probes,
verification custody, mirror removal. Callers moved to the new gate: `DefaultRunnerRoutingPolicy`
(`Decide` and `TryKindDefault`, with D-8's redirect), `AgentTaskDispatcher.RemoteHoldForAsync`
(before `DeclaredCapacity`) and `PrepareRemoteWorkspaceAsync`, and the standing-agent start in
`AgentControlService` (a named runner-bound agent on a draining runner is refused 409
`phone_home_runner_draining`). `SourceLandingAdmission.RequireSupportAsync` keeps `Resolve`: an
explicit SourceLanding Mutation for a draining runner is admitted and held at dispatch until the
drain clears, because its snapshot lives on that runner and cannot be redirected. Nothing changes
in `PhoneHomeLiveConnection`'s `DispatchEligible` gate, `DeclaredCapacity` or
`CountRunnerOccupancyAsync`. Rejected: flipping `DispatchEligible` (it would refuse messages, and
0710's default policy would fall to the desktop before any redirect).

**D-8. A drain carries a redirect (a one-hop alias) and moves unlaunched work.**
`POST /api/session-runners/{id}/drain { "reason", "redirectTo"?, "retireWhenIdle" }`.

- *Placement.* When the kind or global runtime default names a draining runner whose `RedirectTo`
  is configured, delegated-tasks-enabled, dispatch-eligible and not itself draining, the policy
  selects the redirect (`source=default` or `kind-default`, `reason=drain_redirect:<from>`, no
  warning). Otherwise today's fallback with `reason=runner_draining` (warn). An explicit
  `-Runner server2-temp` create is admitted as today (row 7) and stays explicit.
- *Dispatch.* A Queued task bound to a draining runner with no session and no SourceLanding
  operation is **rebound** at its next tick when the drain has an eligible redirect: `RunnerId`
  becomes the target; a task event `runner drain_redirect from=<a> to=<b> reason=<drain reason>`
  is written (the requested id and `RunnerSelectionSource` are kept); a recorded
  `RemoteWorktreePath` is removed through the draining runner (`WorkspaceRemove`, best effort,
  logged on failure) and cleared, with `RemotePrepFailures`/`DispatchNotBeforeAt` reset so the
  mirror is prepared again on the target; the tick then runs the ordinary gates on the new runner,
  including 0710's platform recheck. Without a redirect, or with an ineligible one, the task is
  held `RunnerDraining` ("Held: runner 'server2' is draining (<reason>); redirect 'x' is not
  accepting work"), one deduplicated trace like the other holds.
- *Explicit pins move too.* Step 4 drains `server2-temp` with `redirectTo: server2`; the tasks the
  operator queued with `-Runner server2-temp` to prove the image are exactly the ones that must
  move, or the retire never converges. The requested id stays in the audit.

Why a redirect on the drain rather than a runtime-defaults edit: the operator is already at the
drain step; a PUT to `/api/runner-defaults` is a Human revision per upgrade, is never re-read for
queued tasks (row 6), and leaves `server2` a dead explicit target. Why not a persistent runner
group in the `Runners` map: a group with fixed membership cannot say "temporarily not this
member", and editing the map is a restart. Rejected: holding explicit tasks until the drained
runner returns (server2-temp's retirement would wait forever on a task pinned to it); re-reading
runtime defaults at dispatch (0710 D-10 forbids it, and it would move work for reasons unrelated to
a drain).

**D-9. Routes and codes.** `POST .../drain` and `POST .../drain/clear { "reason" }` (and R3's
`POST .../retire`) are operator-token routes in `SessionRunnerEndpoints` using
`OperatorCredential.Require` (CARD-0716 shape): 403 `operator_token_required` without the header
whatever the address; 404 for an id not in the map or the desktop alias; 400 for an empty or
over-200-character reason; 409 `phone_home_redirect_invalid` when `redirectTo` is unknown, the
desktop, the runner itself, disabled, or itself draining. `drain` is idempotent (a re-POST updates
reason, redirect and `retireWhenIdle`). `clear` resets `Draining`, `RedirectTo`, `RetireWhenIdle`,
`IdleObservedAt`, `RetiredAt` and `RetireReason`, which is how a retired `server2-temp` becomes
registrable again for the next upgrade. New `PhoneHomeProblemTypes`: `RunnerDraining`
(`phone_home_runner_draining`), `RunnerRetired` (`phone_home_runner_retired`), `RedirectInvalid`
(`phone_home_redirect_invalid`), `RunnerBusy` (`phone_home_runner_busy`, R3),
`RunnerNotDraining` (`phone_home_runner_not_draining`, R3).

**D-10. Status and catalogue say "accepting new work" separately from "eligible".**
`PhoneHomeRunnerStatusDto` gains (all defaulted, additive): `AcceptingNewWork` (=
`DispatchEligible && !Draining && RetiredAt == null`), `Draining`, `DrainedAt`, `DrainReason`,
`RedirectTo`, `RetireWhenIdle`, `IdleObservedAt`, `RetiredAt`, `RetireReason`, `Sessions`
(non-terminal desktop rows bound to the id; null without a database), `QueuedTasks` (Queued rows
bound with no session), `RunnerSessions` (the connection's last List count, null before one).
`DispatchEligible` keeps its meaning (a recovered connection), so 0710's policy fallback and
`deploy-parent`'s wait are unchanged. `SessionRunnerCatalogueEntryDto` gains `draining` and
`acceptingNewWork`, and `unavailableReason` is `"draining"` for an eligible draining runner.
`GET .../slots` is unchanged. Rejected: reporting a draining runner as not eligible (scripts and
the policy would misread a healthy runner).

**D-11. A retired runner id cannot register until its drain is cleared.** `Register` refuses a
slot whose state has `RetiredAt` with `phone_home_runner_retired`. Why in R2: the rule must exist
when `RetiredAt` first appears (R3), and an operator `docker start` of an exited temp container, or
the next `up` before `clear`, must not put a retired id back into service silently.

### R3: retire, the temp project, the shared broker, the deploy

**D-12. `Retire` is a phone-home operation the runner executes.** `PhoneHomeOperation.Retire = 27`
(26 is 0710's `LaunchPlatformConstrained`), `RunnerRetireRequest(bool Force, string Reason)`,
`RunnerRetireResult(Guid ProcessBootId, int KilledSessions, DateTime StoppingAtUtc)`. The runner's
dispatcher refuses a non-forced retire while `OwnedSessionCount > 0` with `phone_home_runner_busy`
(count in the message); a forced retire runs `KillAllAsync(5 s)` first; on acceptance it schedules
`IHostApplicationLifetime.StopApplication()` 250 ms after the reply is written so the frame
flushes, and `PhoneHomeConnectionService` closes the socket with description `retiring`. The
runner exits 0, `dind-entrypoint.sh` stops dockerd and the container exits; the temp project's
`restart: "no"` (D-15) keeps it exited. Rejected: `docker stop` from the desktop (no host access; the
card wants the instance to exit by itself); a close frame as the signal (not acknowledged, not
typed).

**D-13. `RunnerRetireJob` retires an idle draining runner that asked for it.** Hangfire recurring
job `antiphon:runner-retire` on `PhoneHomeRunner:RunnerRetireCron` (default `* * * * *`),
registered beside `RunnerSlotReconcileJob`, `[AutomaticRetry(Attempts = 0)]`. For each state row
with `Draining && RetireWhenIdle && RetiredAt == null`: idle when the database has no
non-terminal row bound to the id **and** no Queued unlaunched task bound to it (the redirect
should have moved them; a remainder is logged with the task ids) **and** the runner's inventory is
`Available` with no non-`Exited` session **and** `now >= DrainedAt + RetireMinDrainSeconds` (60).
The first idle observation stamps `IdleObservedAt`; a later run at least `RetireIdleSeconds`
(120) after it sends `Retire(force: false)`; success stamps `RetiredAt` with reason `idle`; a
`phone_home_runner_busy` answer clears `IdleObservedAt`; a runner with no live connection (lease
expired) and no bound rows is stamped `RetiredAt` with reason `idle_disconnected` without a send,
which closes a crashed temp runner's book. A drain with `retireWhenIdle: false` (server2 in step 3,
which the script redeploys) is never retired by the job. Rejected: retiring on the first idle
observation (a launch claimed just before the drain may still be `Starting`); a job that runs
`docker compose down` (Hangfire has no server2 access).

**D-14. Forced retire needs the operator token and the runner id as confirmation.**
`POST /api/session-runners/{id}/retire { "reason", "confirmRunnerId" }`: 403 without the token;
400 when `confirmRunnerId` differs from the path or the reason is empty or over 200 characters; 409
`phone_home_runner_not_draining` unless the runner is draining. It fails every non-terminal row
bound to the id (`FailureReason` "runner <id> retired by operator: <reason>", `SessionTermination`
`SystemRequest`), writes a `RunnerForceRetired` incident (`AgentIncidentKind`, next value after
`RunnerSlotForceReleased = 74`), sends `Retire(force: true)` when connected, and stamps `RetiredAt`
with `forced:<reason>` either way. Rejected: a `?force=true` flag alone (the card asks for an
explicit confirmation).

**D-15. The temp runner is the same compose file in a second project with an override.** Project
`antiphon-runner-temp`; `docker-compose.server2-runner.temp.yml` overrides `session-runner`:
`PhoneHome__RunnerId: server2-temp`, `restart: "no"`, `ANTIPHON_BUILD_SLOTS_URL:
http://build-slots:8080/build-slots`, `networks: [default, antiphon-build-slots]`, and one extra
volume `${RUNNER_GROK_STORE_DIR:?}:/state/grok` (the Grok login store shared by binding server2's
`runner-state` volume's `grok/` subdirectory; the deploy resolves the path from `docker volume
inspect -f '{{.Mountpoint}}' antiphon-runner_runner-state`, never a hard-coded
`/var/lib/docker/...`; Compose 2.18 has no subpath). Everything else comes from the base file with
the same secret and identity files (`ANTIPHON_DEPLOY_KEY_FILE`, `PHONE_HOME_SECRET_FILE`,
`CLAUDE_OAUTH_TOKEN_FILE`, `RUNNER_GIT_IDENTITY_FILE`, `RUNNER_CODEX_HOME_DIR`) so the Claude token
(read-only file), Codex home (read-write directory) and phone-home secret are shared, and its own
`work`, `runner-state` (fresh store id, own launch generations, own Claude config dir seeded by
`state-init`, own session records) and `dind-data` volumes plus its own `/tmp`. `stack.temp.env`
(`COMPOSE_PROJECT_NAME=antiphon-runner-temp`, `SOURCE_SHA12`, the same file paths,
`RUNNER_GROK_STORE_DIR`) is written by the deploy case and never touches `stack.env`.
`PhoneHome__Capacity` stays 10 in both. Rejected: two services in one project (revision 1's D-12;
per-project volumes are what keep `/work`, the store identity and the nested store apart with no
new code); `--scale`; copying the Grok or Codex store (a credential-custody change, dad7cd6a);
sharing `runner-state` (row 10).

**D-16. One build-slot broker, reached by both projects over an external network.** Service
`build-slots` in `docker-compose.server2-runner.yml` (profile `broker`, image
`antiphon-server2/session-testing:${BUILD_SLOTS_SHA12}` pinned separately so a runner deploy never
recreates it, unprivileged, `user: 1654:1654`, `SessionRunner__BuildSlotsOnly: "true"`, today's
budget values, `restart: unless-stopped`, health `curl -fsS http://127.0.0.1:8080/build-slots`,
attached to the external network `antiphon-build-slots`, created by the deploy when absent).
`session-runner` in the base file gains `ANTIPHON_BUILD_SLOTS_URL: http://build-slots:8080/build-slots`
and joins the network, so after its redeploy server2 uses the broker too. Runner changes carried
over from revision 1: `SessionRunner:BuildSlotsOnly` maps `/health` and the build-slot routes and
registers no phone-home, pty adoption or session routes; `BuildSlotSettings.HolderLiveness`
(`pid` default, `renew` for the broker service) makes the sweep reap a lease not renewed within
`RenewGraceSeconds` (90) instead of consulting pid liveness (a pid in another container is
meaningless), the TTL still applies; `POST /build-slots/{leaseId}/renew` (204 held, 404 unknown);
the grant carries `RenewEverySeconds` (null in pid mode) and `scripts/lib/build-slot.ps1` renews
from a `Start-ThreadJob` while the foreground command runs, only when the grant asks, so the
desktop broker's behaviour is byte-for-byte today's. During the first overlap the old server2
container (no `/build-slots`, wrapper default loopback, runs unleased today) stays unleased; that
is the status quo, not a regression. Rejected: `MaxConcurrent 2` per runner (dad7cd6a's stopgap;
halves the budget permanently and is still two budgets); `Enabled:false` (unlimited, row 11);
`pid: "service:build-slots"` (a broker restart would kill both runners); a broker on the desktop
(the budget is server2's).

**D-17. `deploy-server2.ps1 -Rolling` drives the four steps; the host lane gets three cases.**

Host lane (`scripts/c590-remote.sh`), all joining `$script:C590LiveCases` with stub arms in
`verify-docker-stack.ps1`:

- `deploy-temp-runner`: the same secret and identity preconditions as `deploy-parent`; refuses
  `NestedStoreDiskLow` below 20 GB free on the volume filesystem; builds both images at `$SHA` as
  `deploy-parent` does; ensures the `antiphon-build-slots` network and the broker (`compose_host
  --profile broker up -d --no-build build-slots`, writing `BUILD_SLOTS_SHA12` into `stack.env`
  only when absent); resolves `RUNNER_GROK_STORE_DIR`; writes `stack.temp.env`; seeds the temp
  `work` checkout (`seed_runner_checkout` parameterised by project); `docker compose -p
  antiphon-runner-temp -f <base> -f <temp override> --env-file stack.temp.env up -d --no-build`
  and **never** `--remove-orphans`; waits for health; runs the `deploy-parent` probes against the
  temp container (secret readable as uid 1654, phone-home window, `docker info` name equals the
  container hostname, a nested `docker run --rm busybox true`, git identity, checkout); records
  `bridge-nf.txt` from both containers; writes `temp-container.txt`; retires no images. It does not
  drain or promote: the operator token lives on the desktop.
- `deploy-parent` amendments: keep the temp's and the broker's tags in
  `retire_superseded_server2_images` (they are the same sha12 as the new deploy in the normal
  flow, but a re-run at a later sha must not delete a live temp's image); never `--remove-orphans`
  across projects (project-scoped already); join the network and set the broker URL (D-16).
- `retire-temp-runner`: takes `tempRetiredAt` from the manifest (the desktop read it from status)
  and refuses `TempRunnerNotRetired` when absent; `docker compose -p antiphon-runner-temp down -v`;
  leaves images and the broker alone; writes `temp-down.txt`.

Desktop wrapper `scripts/deploy-server2.ps1 -Rolling -Sha <40 hex> [-Phase all|deploy-temp|
drain-old|redeploy-old|drain-temp|retire-temp] [-WaitIdleMinutes 480]` (ASCII, pwsh 7; the token
from the owner-only file through the `runner-slots.ps1` helper, never printed):

1. `deploy-temp`: `verify-docker-stack.ps1 -Case deploy-temp-runner`; wait until
   `GET /api/session-runners/server2-temp/status` is 200 with `acceptingNewWork: true` and
   `buildVersion` equal to the sha (five minutes; a 404 is not eligible, D-4).
2. `drain-old`: `POST .../server2/drain { reason, redirectTo: "server2-temp", retireWhenIdle: false }`;
   poll every 30 s until `sessions == 0 && runnerSessions == 0 && queuedTasks == 0`, bounded by
   `-WaitIdleMinutes` (exit 2 `OldRunnerStillBusy` leaves the drain in place for a re-run).
3. `redeploy-old`: `verify-docker-stack.ps1 -Case deploy-parent` (the existing case; server2 is
   idle, so recreating its container interrupts nothing); wait for `dispatchEligible`; then
   `POST .../server2/drain/clear { reason }`; wait for `acceptingNewWork: true`.
4. `drain-temp`: `POST .../server2-temp/drain { reason, redirectTo: "server2", retireWhenIdle: true }`;
   poll until `retiredAt` is set (bounded by `-WaitIdleMinutes`; Hangfire does the retire).
5. `retire-temp`: `verify-docker-stack.ps1 -Case retire-temp-runner` with `tempRetiredAt`.

Each phase reads the server's status first and is idempotent, so a wrapper killed mid-wait is
re-run with the same arguments. A failed phase exits 2 with the diagnosis and touches nothing
later. `scripts/runner-drain.ps1 status|drain|clear|retire [-RunnerId] [-RedirectTo]
[-RetireWhenIdle] [-Reason] [-Confirm]` is the manual surface for the same routes.
`scripts/test-deploy-server2.ps1` (C495 fixture: seamed `verify-docker-stack.ps1`, fake HTTP,
sentinel token, `trace.jsonl`) covers T-1..T-6 (V-32). Rejected: streaming the operator token to
server2 (a custody expansion); one monolithic case that drains from server2 (the token stays on
the desktop); a wrapper that waits with no bound (the old runner can host hours-long tasks; the
bound is the operator's, and the state survives on the server).

**D-18. Manual rollback is documented, not automated.** If the temp image proves bad in step 2:
`runner-drain.ps1 drain -RunnerId server2-temp -RedirectTo server2` (or `-Retire` forced) and, if
server2 was already drained, `runner-drain.ps1 clear -RunnerId server2`; the old server2 container
is untouched until step 3. After step 3 the old image is gone (deploy-parent retires it), so
rolling back means deploying the previous sha through the same rolling flow.

### Cross-round

**D-19. Settings and defaults.** `PhoneHomeRunnerSettings`: `RunnerRetireCron` (`* * * * *`),
`RetireMinDrainSeconds` (60), `RetireIdleSeconds` (120), validated when enabled.
`BuildSlotSettings`: `HolderLiveness` (`pid`), `RenewGraceSeconds` (90), `RenewEverySeconds` (20);
`SessionRunner:BuildSlotsOnly` (false). All additive with today's behaviour as the default.

**D-20. Docs.** `docs/session-runtime-invariants.md`: three invariants (a draining runner takes no
new claims and still serves every call for its sessions; a drain's redirect moves unlaunched work,
never a session; a retired runner id is refused until its drain is cleared). `docs/ops-http.md`:
the two-entry `Runners` configuration, the status fields, `drain`/`drain/clear`/`retire`, the
problem codes, `runner-drain.ps1`, "404 status is not eligible" (CARD-0729). `docs/docker-stack.md`:
the temp project and override, the broker and network, `deploy-server2.ps1 -Rolling`, what the
flow leaves behind (nothing from the temp; the broker stays). `docs/testing-and-build.md`: the
`Runners` map in the enablement steps (D-2) and build-slot renew mode. `docs/agent-credentials.md`:
the temp runner shares the login stores and how (row 10). `docs/bootstrap.md`: one pointer that the
server2 deploy is rolling. `docker/stack.env.example`: `BUILD_SLOTS_SHA12`, `RUNNER_GROK_STORE_DIR`
and the temp env file.

## Implementation rounds and slices

Every slice commits its red tests first (`Sn-tests`), then the production change (`Sn`), with the
real checkpoint outcome in each message. Server slices and runner slices are separate commits.
R1 has no red commit (D-3); its rows are green pins.

### R1 (server config, tests, docs; one Code dispatch, after CARD-0710 lands)

| Slice | Files | Tests |
|---|---|---|
| S1: `RollingWorld` and the two-id pins (~2 h) | new `tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerTests.cs` with `RollingWorld` (copied from `PhoneHomeLaunchTransportTests.LaunchWorld`: isolated schema, `PhoneHomeTestHost.StartAsync(connectionString:, configured: Pair("server2", "server2-temp"))`, two peers via `ConnectPeerAsync(runnerId:, storeId:, secret:)`, `BridgeQueueHarness` with the real adapter factory and dispatcher bound to `host.Directory`, `RunnerDefaultSettingsService.EnsureInitializedAsync` plus a `PutAsync` setting `globalRunnerId`, `AgentKind.Raw`); `PhoneHomeConnectionTests` (V-1 HTTP contract; if the route answers anything but 404 the fix is in `SessionRunnerEndpoints`/`ExceptionMiddleware`) | V-1, V-2, V-3 |
| S2: configuration and docs (~1 h) | `docs/testing-and-build.md` enablement steps (the `Runners` map with both entries), `docs/ops-http.md` (`Runners`, CARD-0729 rule), `docs/agent-credentials.md` (shared secret across entries), `server/appsettings.json` comment only, `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs` (`Testing_doc_names_the_production_enablement_steps` expects the map form) | V-3 (doc contract), `DockerStackDocumentationTests` regression |

Activation after R1 lands (operator, main checkout): user-secrets `PhoneHomeRunner:Runners:server2:*`
and `PhoneHomeRunner:Runners:server2-temp:*` per D-2, `restart-apphost.ps1`, confirm
`GET /api/session-runners` lists `desktop`, `server2` (eligible) and `server2-temp` (unavailable).

### R2 (server; one Code dispatch)

| Slice | Files | Tests |
|---|---|---|
| S3: the state row (~2 h) | `server/Domain/Entities/SessionRunnerState.cs`, `AppDbContext.cs`, migration `AddSessionRunnerStates`, `server/Application/Services/RunnerStateService.cs` (read/write, push to the directory), `RunnerStateLoader.cs` (hosted, before the pump), `Program.cs` registrations; `PhoneHomeTestHost` registers the service when a connection string is present and an in-memory `IRunnerStateStore` otherwise | V-4 (persistence half), V-11 |
| S4: the drain gate, redirect, routes, status (~5 h) | `ISessionRunnerDirectory.cs` (`ResolveForNewWork`), `PhoneHomeRunnerDirectory.cs` (`RunnerSlot.State`, `ApplyState`, gate, `Register` retired refusal, `Status` fields incl. `Sessions`/`QueuedTasks`/`RunnerSessions`), `PhoneHomeContracts.cs` (DTO fields, problem codes), `SessionRunnerCatalogue.cs` (+DTO), `DefaultRunnerRoutingPolicy.cs` (D-8 placement), `AgentTaskDispatcher.cs` (`RemoteHoldForAsync` gate + rebind, `HoldKind.RunnerDraining`, `DispatchHoldDetails.RunnerDraining`, `PrepareRemoteWorkspaceAsync` gate), `AgentControlService.cs` (standing start gate), `SessionRunnerEndpoints.cs` (`drain`, `drain/clear`), `RunnerStateService.cs` (validation, D-9) | V-4..V-10, V-12, V-13 |
| S5: docs for R2 (~30 min) | `docs/session-runtime-invariants.md` (first two invariants), `docs/ops-http.md` (routes, status fields), `scripts/runner-drain.ps1` `status|drain|clear` verbs (retire verb lands in R3) | `RunnerDrainScriptTests` V-33 (first three verbs; the retire verb is added in R3 without changing the contract) |

### R3 (server, runner, compose, scripts; one Code dispatch, desktop lane for the live rows)

| Slice | Files | Tests |
|---|---|---|
| S6: `Retire` on the runner (~1.5 h) | `PhoneHomeContracts.cs` (operation 27, records, `retiring` close reason), `PhoneHomeCommandDispatcher.cs` (D-12, `IHostApplicationLifetime` injected), `PhoneHomeConnectionService.cs`, `PhoneHomeRunnerClient.cs` (`RetireAsync`), `PhoneHomeScriptedPeer` (`Retires`, default `RunnerRetireResult`) | `PhoneHomeCommandDispatcherTests` V-14, V-15 |
| S7: retire job, forced retire (~3 h) | new `RunnerRetireJob.cs` (+ `HangfireConfiguration.AddOrUpdateRunnerRetireJob`, `Program.cs`), `RunnerRetireService.cs` (idle check, row failure, incident; shared by job and route), `PhoneHomeRunnerSettings.cs` + validator (D-19), `SessionRunnerEndpoints.cs` (`retire`, D-14), `AgentIncidentKind` (`RunnerForceRetired`), `PhoneHomeRunnerDirectory.cs` (retire fields already in the DTO; `Register` refusal from R2) | new `tests/Antiphon.Tests/Application/RunnerRetireJobTests.cs` V-16..V-19; `PhoneHomeRollingRunnerTests` V-20..V-23 |
| S8: shared broker (~3 h, runner + wrapper) | `BuildSlotSettings.cs`, `BuildSlotBroker.cs` (`Renew`, renew-mode sweep, `LastRenewedAt`), `BuildSlotRoutes.cs` (`POST /build-slots/{leaseId}/renew`), `BuildSlotContracts.cs` (`RenewEverySeconds`), `src/Antiphon.SessionRunner/Program.cs` (`BuildSlotsOnly`), `scripts/lib/build-slot.ps1` (renewer; ASCII) | `BuildSlotBrokerTests` V-24, V-25; `BuildSlotEndpointTests` V-26, V-27; `BuildSlotScriptTests` V-28 |
| S9: compose, cases, wrapper, contract tests (~4 h) | `docker-compose.server2-runner.yml` (broker service, network, broker URL), new `docker-compose.server2-runner.temp.yml`, `docker/stack.env.example`, `scripts/c590-remote.sh` (`case_deploy_temp_runner`, `case_retire_temp_runner`, `deploy-parent` amendments, `compose_temp`, `temp_runner_container`, keep set), `scripts/c590-real.ps1` (roster), `scripts/verify-docker-stack.ps1` (stub arms), new `scripts/deploy-server2.ps1`, new `scripts/test-deploy-server2.ps1`, `scripts/runner-drain.ps1` (`retire` verb) | `DockerStackContractTests` V-29, `DindRunnerContractTests` V-30, `RemoteScriptContractTests` V-31, `test-deploy-server2.ps1` T-1..T-6 (V-32), `RunnerDrainScriptTests` V-33 |
| S10: docs for R3 (~45 min) | `docs/docker-stack.md`, `docs/ops-http.md`, `docs/testing-and-build.md` (build slots), `docs/session-runtime-invariants.md` (third invariant), `docs/agent-credentials.md`, `docs/bootstrap.md` pointer | `DockerStackDocumentationTests` regression |
| L: live proof on server2 (desktop lane, after R1 to R3 are active on the desktop server) | none | L-1..L-3 |

## Risks

**Two nested dockerd processes on server2's kernel 4.15 (cgroup v1, legacy iptables).** Each
project's runner is privileged with its own network namespace, its own `dind-data` and its own
`docker0`; they share the host cgroup v1 hierarchy (each nested daemon creates groups under its own
container's path), the `antiphon-custody` root (executions are unique ids), the conntrack table and
the `bridge-nf-call-iptables` sysctl, which on this kernel may be global. The investigation did not
start two. Mitigation: `deploy-temp-runner` runs `docker info` in both containers, a nested
`docker run --rm busybox true` in the temp one, and records `bridge-nf.txt` from both; a
`SiblingDaemonRefused` or a failed nested run stops the case before any drain (L-1). The temp's
nested store starts empty (images re-pull on first use); `NestedStoreDiskLow` refuses below 20 GB.

**Codex and Grok login contention.** Both runners' children share the Codex host directory and,
through the volume-subdirectory bind, server2's Grok store; both CLIs rewrite their credential
files by rename on refresh, so a concurrent refresh with rotation can sign one process out. The
operator accepted this (dad7cd6a). Mitigation: L-2 probes `provider-auth` for `claude`, `codex` and
`grok` on both ids at the start of the overlap and again after at least 30 minutes and requires
`loggedIn` unchanged; no code change. A sign-out observed is a follow-up card (a per-runner login
copy is a credential-custody change).

**The Grok store bind depends on the volume's mountpoint.** Docker performs the bind as root, so
the compose user needs no access, but the path is resolved at deploy time from `docker volume
inspect`, never hard-coded, and the case refuses `GrokStoreUnresolved` when the volume or its
`grok/` directory is absent. If server2's `runner-state` volume is ever recreated, the temp's bind
follows the new mountpoint on the next deploy.

**Git lock contention on a shared checkout.** Not present: each project has its own `work`
volume and checkout (row 12). The cost is one more clone per temp deploy (`seed_runner_checkout`
on an empty volume; minutes). Revision 1's mirror lock is dropped; L-3 keeps the overlap-window
log grep (`index.lock`, `packed-refs.lock`, `Mirror fetch failed`) on both containers as a
zero-hit proof that nothing else shares a git dir.

**Broker lease loss.** The broker holds leases in memory; its restart drops them and briefly
admits a full budget of new builds beside the running ones. Bounded by the memory floor; the
broker tag is pinned separately so a rolling runner deploy never restarts it.

**A queued task rebound under a mirror.** D-8 removes the old mirror through the draining runner
before clearing `RemoteWorktreePath`; a removal that fails (the runner is gone) leaves an orphan
worktree on that runner's `work` volume. For server2-temp the volume is deleted at retirement; for
server2 the desktop `WorktreeResidueJob` inventory and the next `deploy-parent` seed leave it
visible, and the rebind event names the old path. V-7 pins the removal call.

**A server restart during a drain.** The state is a row (D-6) and is loaded before the pump marks
anything recovered; Hangfire's job is recurring. V-4 pins the rebuilt-directory half; V-23 runs the
whole flow in one process only, so the restart case is the row test plus the pump's existing
recovery coverage.

**The desktop-only step in a server2-placed pipeline.** The live rows and the wrapper run from the
desktop (the c590 bridge SSHes to server2; the operator token is there). A server2-placed Code
task reports them not run; the orchestrator dispatches them to a desktop task after R1 to R3 are
active on the desktop server.

## Verification design

### Harness and red-first discipline

- Desktop transport tests use `PhoneHomeTestHost.StartAsync(configured: Pair(...))` with two
  entries (`server2`, `server2-temp`; secrets may be equal, D-2, and one test uses distinct ones to
  show nothing depends on equality) and `PhoneHomeScriptedPeer` through
  `ConnectPeerAsync(runnerId:, storeId:, secret:)`. The peer gains `Retires` (frames of
  operation 27) and a `RunnerRetireResult` default reply (S6).
- DB-backed rolling tests use `RollingWorld` (S1): isolated schema, `BridgeQueueHarness` with the
  real `AgentProtocolAdapterFactory` and `AgentTaskDispatcher` bound to `host.Directory`, the
  runtime-defaults row written through `RunnerDefaultSettingsService`, `AgentKind.Raw`, a
  `FakeTimeProvider`. The retire job runs through its class, never through Hangfire.
- Operator routes use `PostOperatorAsync(path, body, token)` with the token from
  `host.OperatorTokenPath` (CARD-0653/0716 shape); the 403 case posts with `proxied: true`.
- Runner tests use `PhoneHomeCommandDispatcherTests`' fake runtime with a recording
  `IHostApplicationLifetime`; broker tests use `BuildSlotBrokerTests`' fake liveness, fake memory
  and `FakeTimeProvider`; the wrapper uses `BuildSlotScriptTests`' `C589_SLOT_SHIM` and
  `C589_COMMAND_SHIM` seams (the shim records renew calls with timestamps).
- Contract tests read the compose files, the entrypoint and the scripts as text (existing
  classes). Script fixtures follow the C495 pattern (temp root, seams, `trace.jsonl`, sentinel
  token); nothing contacts 172xx, Docker or server2 except the L rows.
- Red first (R2, R3): each `Sn-tests` commit compiles (schema, DTO members, interface members
  with default bodies and stub classes throwing `NotImplementedException` are allowed in the red
  commit; behaviour is not), fails at the assertion the roster names, and is followed by the green
  row on the same filter. R1's rows are green pins (D-3). No wall-clock wait in a test above 3 s.
  No timeout widened, no assertion loosened; a failure not explained by the slice is re-run alone
  at the base commit and reported as inherited or owned.

### Coverage roster and decisive assertions (Plan draft; superseded by the TestDesign roster in `## Verification design (TestDesign, task 700c3a06)` below)

| ID | Class.Method | Assertion / red mechanism |
|---|---|---|
| V-1 | PhoneHomeConnectionTests.Unknown_runner_status_is_404_and_carries_no_live_runner_identity | Host with `server2` and `server2-temp` configured, `server2` connected and recovered: `GET /api/session-runners/server2-temp/status` is 200 `available:false, dispatchEligible:false` (configured, offline); `GET .../server2-other/status` is **404** and its JSON has no `runnerStoreId`, `processBootId` or `buildVersion`; `GET .../desktop/status` is 200 with both flags false; `GET .../server2/status` is `dispatchEligible:true` with its own store id. Green pin; Mutation PC-2 (Status returns the first slot) turns it red. |
| V-2 | PhoneHomeRollingRunnerTests.Input_to_a_session_on_server2_reaches_server2_while_a_new_launch_goes_to_server2_temp | Peer A (`server2`) hosts row S_A (Running, bound `server2`); peer B (`server2-temp`) connected and recovered; runtime global default `server2-temp`; a new default-placed task is claimed: `peerB.Launches.Count == 1`, `peerA.Launches.Count == 0`, the row's `RunnerId == "server2-temp"` and `RunnerStoreId == storeB`; `RoutingSessionRunnerClient.SendInputAsync(S_A, "x")` and the adapter's `SendInputAsync`: `peerA.Inputs.Count == 2`, `peerB.Inputs.Count == 0`. Green pin; PC-1 (Route resolves the default runner) turns it red. |
| V-3 | PhoneHomeRunnerSettingsValidatorTests.Two_entries_with_the_same_secret_and_host_root_validate | The D-2 configuration validates with zero failures; `KnownRunnerIds` lists both; a duplicate id (case-insensitive) fails. Green pin. |
| V-4 | PhoneHomeRollingRunnerTests.Drain_requires_the_operator_token_persists_the_state_and_survives_a_directory_rebuild | `POST .../server2/drain` without token 403, no row; with token 200; `SessionRunnerStates` row has `Draining`, `DrainedAt`, `RedirectTo == "server2-temp"`; status shows `draining:true, acceptingNewWork:false, dispatchEligible:true`; a new `PhoneHomeRunnerDirectory` fed by `RunnerStateLoader` over the same schema reports `ResolveForNewWork("server2")` refused `phone_home_runner_draining`. Red after S3-tests: 404. |
| V-5 | PhoneHomeRollingRunnerTests.A_draining_runner_still_serves_input_transcript_kill_and_release_for_its_sessions | After the drain: `SendInput`, `GetTranscript`, `KillGeneration`, `ReleaseSlot` for S_A: `peerA.RequestCount(op) >= 1` each and `peerB.RequestCount(op) == 0`; `GetInventoryAsync("server2")` is `Available`. Red: the S4-tests stub gate refuses `Resolve` too (the stub throws from both), so the first call fails. |
| V-6 | PhoneHomeRollingRunnerTests.Default_placement_follows_the_drain_redirect_and_falls_back_without_one | Global default `server2`, drained with redirect `server2-temp` (eligible): create binds `server2-temp`, Created audit contains `reason=drain_redirect:server2`, no warning; drained without redirect: binds null (desktop) with `reason=runner_draining` and one warning; explicit `-Runner server2` create still binds `server2`. Red: binds `server2` / desktop with `runner_not_dispatch_eligible`. |
| V-7 | PhoneHomeRollingRunnerTests.A_queued_task_bound_to_a_draining_runner_is_rebound_to_the_redirect_before_claim | Task Queued on `server2` with `RemoteWorktreePath` set; drain `server2` → `server2-temp`; one dispatch tick: `peerA.RequestCount(WorkspaceRemove) == 1`, task `RunnerId == "server2-temp"`, `RemoteWorktreePath` re-prepared and the launch reaches `peerB` only; a task event contains `drain_redirect from=server2 to=server2-temp`. Red: held `RunnerUnavailable` or launched on A. |
| V-8 | PhoneHomeRollingRunnerTests.A_queued_task_on_a_draining_runner_without_an_eligible_redirect_is_held | Drain without redirect: `Held: runner 'server2' is draining`; with redirect to an offline `server2-temp`: held naming the redirect; nothing launched, `RunnerId` unchanged. Red: launched on A. |
| V-9 | PhoneHomeConnectionTests.Draining_changes_neither_dispatch_eligibility_nor_capacity | `DeclaredCapacity("server2")` unchanged; `Resolve("server2")` returns a client; `ResolveForNewWork("server2")` throws `phone_home_runner_draining`; catalogue row `acceptingNewWork:false, dispatchEligible:true, unavailableReason:"draining"`. Red: `ResolveForNewWork` default body resolves. |
| V-10 | PhoneHomeRollingRunnerTests.Clear_drain_restores_new_work_and_resets_the_retire_fields | After `drain/clear`: row `Draining == false`, `RedirectTo == null`, `RetiredAt == null`; a new default-placed task binds `server2`; status `acceptingNewWork:true`. Red: 404. |
| V-11 | PhoneHomeConnectionTests.A_retired_runner_id_cannot_register_until_its_drain_is_cleared | State row with `RetiredAt` for `server2-temp`: `RegisterAsync(runnerId: "server2-temp")` fails 409 `phone_home_runner_retired`; after `drain/clear` a ticket is issued. Red: ticket issued. |
| V-12 | PhoneHomeStandingLaunchTests.Standing_agent_start_on_a_draining_runner_is_refused | The existing standing-launch harness with the runner drained: start is 409 `phone_home_runner_draining`, no Launch frame; after clear it launches. Red: launches. |
| V-13 | PhoneHomeRollingRunnerTests.Status_reports_sessions_queued_tasks_and_runner_sessions_per_runner | One Running row bound `server2`, one Queued task bound `server2`, peer A listing one session: `server2` status `sessions == 1, queuedTasks == 1, runnerSessions == 1`; `server2-temp` status zeros; after a drain `drainedAt` and `drainReason` set. Red: fields absent (null). |
| V-14 | PhoneHomeCommandDispatcherTests.Retire_refuses_while_sessions_are_owned_unless_forced | `OwnedSessionCount = 1`: `Retire(force:false)` is 409 `phone_home_runner_busy` naming 1; `Retire(force:true)` calls `KillAllAsync` once and answers `RunnerRetireResult` with `KilledSessions == 1`. Today: `phone_home_unsupported_operation`. |
| V-15 | PhoneHomeCommandDispatcherTests.Retire_reply_is_written_before_the_host_stops | Recording lifetime: `StopApplication` is called after the reply task completed and within 1 s. Today: never called. |
| V-16 | RunnerRetireJobTests.Draining_runner_with_retire_when_idle_is_retired_after_the_idle_window | Fake clock; `server2-temp` draining with `RetireWhenIdle`, no bound rows, peer B lists nothing: run 1 stamps `IdleObservedAt`, sends nothing; advance `RetireIdleSeconds`; run 2: `peerB.Retires.Count == 1` with `force == false`, row `RetiredAt` set, reason `idle`. Red: class stub. |
| V-17 | RunnerRetireJobTests.Live_session_queued_task_or_busy_answer_keeps_the_runner_draining | Peer B lists S_B: no send; a Queued task bound: no send, log names it; peer B lists nothing but replies `phone_home_runner_busy`: `RetiredAt` null, `IdleObservedAt` cleared. |
| V-18 | RunnerRetireJobTests.A_drain_without_retire_when_idle_is_never_retired | `server2` draining, `RetireWhenIdle == false`, idle for two windows: `Retires` empty, `RetiredAt` null. |
| V-19 | RunnerRetireJobTests.Disconnected_draining_runner_with_no_bound_rows_is_marked_retired_without_a_send | Peer B aborted, lease expired on the fake clock: row `RetiredAt` with reason `idle_disconnected`; a disconnected runner with a bound Running row stays draining. |
| V-20 | PhoneHomeRollingRunnerTests.Forced_retire_requires_the_operator_token_and_the_runner_id_confirmation | 403 without token; 400 with a wrong `confirmRunnerId`; 409 `phone_home_runner_not_draining` when not draining; `Retires` empty in all three. Today: 404. |
| V-21 | PhoneHomeRollingRunnerTests.Forced_retire_fails_bound_sessions_writes_an_incident_and_sends_a_forced_retire | S_B `Failed`, `FailureReason` contains the runner id and the reason, `TerminationSource == SystemRequest`; `peerB.Retires` has one frame with `force == true`; one `RunnerForceRetired` incident; row `RetiredAt` with `forced:` prefix. Today: 404. |
| V-22 | PhoneHomeRollingRunnerTests.Forced_retire_of_a_runner_that_is_not_draining_is_refused | 409 `phone_home_runner_not_draining`; nothing sent; no row change. Today: 404. |
| V-23 | PhoneHomeRollingRunnerTests.Rolling_upgrade_moves_new_launches_to_server2_temp_keeps_server2_reachable_and_retires_server2_temp_when_idle | The brief's two-peer integration test, one method: peer A (`server2`) hosts S_A; peer B (`server2-temp`) connects and recovers; drain `server2` → `server2-temp` (`retireWhenIdle:false`); a new task launches on B only (`RunnerId == "server2-temp"`); input to S_A reaches A only; peer A emits S_A's exit; clear `server2`'s drain and a further new task launches on A; drain `server2-temp` → `server2` (`retireWhenIdle:true`); peer B emits S_B's exit and clears `Sessions`; advance the clock; two job runs; `peerB.Retires.Count == 1`; status: `server2-temp` `retiredAt` set, `server2` `acceptingNewWork:true`; `RegisterAsync(runnerId: "server2-temp")` is 409 until `drain/clear`. Red after R2: the retire step (job stub). |
| V-24 | BuildSlotBrokerTests.Renew_mode_reaps_a_lease_not_renewed_within_the_grace_and_ignores_pid_liveness | `HolderLiveness = renew`, fake liveness says dead; lease held after 10 s; reaped after `RenewGraceSeconds`; `Renew` inside the grace keeps it. Red: reaped at once by pid liveness. |
| V-25 | BuildSlotBrokerTests.Renew_extends_a_held_lease_and_an_unknown_lease_answers_false | `Renew(id)` true and `LastRenewedAt` moves; unknown id false. Red: method stub. |
| V-26 | BuildSlotEndpointTests.Post_renew_answers_204_for_a_held_lease_and_404_otherwise | Route contract. Today: 404 for both. |
| V-27 | BuildSlotEndpointTests.Build_slots_only_host_serves_health_and_build_slots_and_nothing_else | Host with `SessionRunner:BuildSlotsOnly=true`: `/build-slots` 200, `/health` 200, `/sessions` 404, `/capabilities` 404, no `PhoneHomeConnectionService` in services. Red: `/sessions` 200. |
| V-28 | BuildSlotScriptTests.Wrapper_renews_a_renew_mode_grant_while_the_command_runs | Shim grant `renewEverySeconds: 1`, command shim sleeps 3 s: shim log has >= 2 renew calls before the release; a pid-mode grant: 0 renew calls. Red: 0 renew calls. |
| V-29 | DockerStackContractTests.Server2_file_defines_runner_state_init_and_broker (replaces `Server2_file_defines_only_runner_and_state_init`) + Temp_override_changes_only_runner_id_restart_broker_url_network_and_grok_store + Server2_broker_is_unprivileged_pinned_by_its_own_tag_and_on_the_external_network; slot-aware updates of `Only_the_server2_runner_is_privileged`, `Server2_services_name_their_images`, `Compose_never_lists_the_claude_token_under_environment` | Base block list is exactly `state-init, session-runner, build-slots, antiphon-deploy-key, phone-home, work, runner-state, dind-data` plus the `networks` block; the override file defines only `session-runner` with `PhoneHome__RunnerId: server2-temp`, `restart: "no"`, `ANTIPHON_BUILD_SLOTS_URL`, the network and `${RUNNER_GROK_STORE_DIR:?}:/state/grok`, and no `runner-state`/`dind-data`/`work` mount changes; the broker has no `privileged`, `SessionRunner__BuildSlotsOnly: "true"`, image tag `${BUILD_SLOTS_SHA12`, `restart: unless-stopped`. Red: today's files. |
| V-30 | DindRunnerContractTests.Server2_compose_reads_the_staged_phone_home_secret / Server2_healthcheck_covers_phone_home_secret_readability | The base file still carries them and the override does not override `healthcheck` or `PhoneHome__SecretPath`. Red: override text. |
| V-31 | RemoteScriptContractTests.Rolling_cases_are_routed_and_have_stub_boundaries + Temp_deploy_never_removes_orphans_and_resolves_the_grok_store_from_the_volume + Deploy_parent_keeps_the_temp_and_broker_tags + Retire_temp_runner_requires_the_retired_evidence | Roster has `'deploy-temp-runner'` and `'retire-temp-runner'`; stub arms exist; `case_deploy_temp_runner` contains `-p antiphon-runner-temp`, `docker volume inspect`, `NestedStoreDiskLow`, `GrokStoreUnresolved`, no `--remove-orphans`; keep set includes `BUILD_SLOTS_SHA12` and the temp env's sha; `case_retire_temp_runner` refuses `TempRunnerNotRetired` and runs `down -v` only for `antiphon-runner-temp`. Red: text absent. |
| V-32 | scripts/test-deploy-server2.ps1 T-1..T-6 | T-1 happy path: trace order `deploy-temp-runner`, `drain server2`, `deploy-parent`, `clear server2`, `drain server2-temp`, `retire-temp-runner`; T-2 a 404 temp status is `TempRunnerNotEligible` exit 2 with no drain posted; T-3 old runner still busy at the bound exits 2 with the drain left in place and no `deploy-parent`; T-4 `-Phase drain-temp` re-run after a kill posts nothing when status already says draining and waits for `retiredAt`; T-5 missing token file exits 2 before any case; T-6 the sentinel token never appears in output and the script is ASCII. Red: script missing. |
| V-33 | RunnerDrainScriptTests.Script_is_ascii_offers_the_four_verbs_and_sends_the_token_without_printing_it | Text contract on `scripts/runner-drain.ps1` (`status|drain|clear|retire`, `X-Antiphon-Operator-Token`, no `Write-Host` of the token, 404 reported as not eligible). Red: file missing. |
| L-1 | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase deploy-temp` (desktop main checkout) | Evidence: `docker-info-old.txt`/`docker-info-temp.txt` daemon names equal each container's hostname, `nested-run-temp.txt` exit 0, `bridge-nf.txt` from both, `GET /api/session-runners` showing both ids eligible, `status-temp.json` with `acceptingNewWork:true` and the new `buildVersion`. Then the operator's step 2 (`delegate.ps1 -Runner server2-temp -Worktree ...`) settles one real task there. |
| L-2 | `GET /api/session-runners/{server2,server2-temp}/provider-auth/{claude,codex,grok}` at L-1 and >= 30 min later | Six `loggedIn` values unchanged (evidence `provider-auth-overlap.json`). |
| L-3 | `-Phase drain-old` through `retire-temp` on the same run, plus the overlap log grep | `status-old-idle.json` (`sessions == 0`), `deploy-parent` evidence at the new sha, `status-old-after-clear.json` `acceptingNewWork:true`, `status-temp-retired.json` with `retiredAt`, `temp-down.txt`; zero hits for `index.lock`, `packed-refs.lock`, `Mirror fetch failed` on either container during the window. |

Regression classes executed in the green rows, counts at the 0710 tip (`[Test]` attributes;
Code reads the fresh TRX if a class has grown): `PhoneHomeConnectionTests` 19 (23 on master by
the same count; the tip reformatted attributes), `PhoneHomeDirectoryTests` 7,
`MultiRunnerDirectoryTests` 7, `MultiRunnerRecoveryTests` 7, `MultiRunnerProjectionTests` 4,
`RunnerCatalogueTests` 4, `PhoneHomeSessionRoutingTests` 5, `PhoneHomeLaunchTransportTests` 7,
`PhoneHomeEpochAgreementTests` 2, `PhoneHomeEventPumpTests` 8, `PhoneHomeRecoveryEligibilityTests`
3, `PhoneHomePendingInventoryTests` 8, `PhoneHomeReconciliationTests` 4,
`SessionReconciliationServiceTests` 57 methods (argument-expanded), `DispatcherRemotePrepStarvationTests`
1 method (4 results), `RunnerSlotEndpointTests` 8, `OperatorShutdownEndpointTests` 3,
`DefaultRunnerCreateTests` 7, `DefaultRunnerEligibilityTests` 3, `DefaultRunnerPinTests` 6,
`DefaultRunnerRerouteTests` 6, `RunnerDefaultTests` 23, `TaskPlatformDispatchTests` 6,
`TaskPlatformPlacementTests` 8, `PhoneHomeStandingLaunchTests` (count from the TRX),
`PhoneHomeRunnerSettingsValidatorTests` 6, `DockerStackContractTests` 108, `DindRunnerContractTests`
22, `RemoteScriptContractTests` 25, `DockerStackDocumentationTests` 10, `BuildSlotBrokerTests` 11,
`BuildSlotEndpointTests` 4, `BuildSlotScriptTests` 7, `PhoneHomeCommandDispatcherTests` 34,
`PhoneHomeConnectionServiceTests` 10.

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

R1: two slices, about 3 hours of authoring (after CARD-0710 lands); R2: three slices, about 7.5
hours; R3: five slices, about 12 hours plus the live rows. Ordinary checkpoint floors from the
tables: R1 **11** minutes, R2 **30** minutes, R3 **53** minutes (plus L-1..L-3, about 60 minutes of
desktop wall clock dominated by the 30-minute overlap wait and however long the old runner's last
task takes). Suggested `-ExpectAbout`: R1 200 minutes, R2 480 minutes, R3 760 minutes.

### Checkpoints (Plan draft; superseded by the TestDesign tables below)

The closed lists for Code, one table per round. `Sn-tests` means the slice's compiling red tests
are committed before its production change; `Sn` means the production change is committed.
Filters use the CARD-0403 combined-class syntax; the backslashes before `|` are Markdown escaping
only. Red rows require the named assertion failures, not any failure.

#### R1 checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c727-r1/` | pins-green | `/*/*/(PhoneHomeRollingRunnerTests*)\|(PhoneHomeConnectionTests*)\|(MultiRunnerDirectoryTests*)\|(MultiRunnerRecoveryTests*)\|(MultiRunnerProjectionTests*)\|(RunnerCatalogueTests*)\|(PhoneHomeSessionRoutingTests*)\|(PhoneHomeLaunchTransportTests*)/*` | V-1, V-2; multi-runner, routing and launch regressions | all listed classes, 0 failed; 2 new methods, 0 skipped | 55 | 6 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c727-r1/` | config-docs-green | `/*/*/(PhoneHomeRunnerSettingsValidatorTests*)\|(DockerStackDocumentationTests*)/*` | V-3; docs contract | all listed, 0 failed/skipped; 1 new method | 17 | 5 |

R1 floor = 6 + 5 = **11** minutes.

#### R2 checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-3 | S3-tests, S4-tests | `tests/Antiphon.Tests -> bin-c727-r2/` | drain-red | `/*/*/(PhoneHomeRollingRunnerTests*)\|(PhoneHomeConnectionTests*)\|(PhoneHomeStandingLaunchTests*)/*` | V-4 to V-13 | 10 new + existing executed; the 10 new fail at their route/gate/rebind/status assertions | 31 | 6 |
| CP-4 | S3, S4 | `tests/Antiphon.Tests -> bin-c727-r2/` | drain-green | `/*/*/(PhoneHomeRollingRunnerTests*)\|(PhoneHomeConnectionTests*)\|(PhoneHomeStandingLaunchTests*)\|(RunnerSlotEndpointTests*)\|(OperatorShutdownEndpointTests*)\|(RunnerCatalogueTests*)/*` | V-4 to V-13; operator-route, catalogue and connection regressions | all listed, 0 failed/skipped | 46 | 6 |
| CP-5 | S4 | CP-4 | placement-green | `/*/*/(DefaultRunnerCreateTests*)\|(DefaultRunnerEligibilityTests*)\|(DefaultRunnerPinTests*)\|(DefaultRunnerRerouteTests*)\|(RunnerDefaultTests*)\|(TaskPlatformDispatchTests*)\|(TaskPlatformPlacementTests*)\|(DispatcherRemotePrepStarvationTests*)/*` | D-7, D-8 regressions on placement and dispatch | all listed classes, 0 failed (the starvation method expands to 4 results) | 63 | 7 |
| CP-6 | S4 | CP-4 | reconcile-green | `/*/*/(PhoneHomeReconciliationTests*)\|(SessionReconciliationServiceTests*)\|(PhoneHomePendingInventoryTests*)\|(PhoneHomeRecoveryEligibilityTests*)\|(MultiRunnerRecoveryTests*)\|(PhoneHomeEventPumpTests*)/*` | D-7 regressions on inventory and recovery | all listed classes, 0 failed (57 reconciliation methods expand to more results) | 87 | 6 |
| CP-7 | S3 | n/a | migration-clean | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c727-ef -- dotnet ef migrations has-pending-model-changes --project server` | D-6 | exit 0, "No changes have been made to the model" | n/a | 2 |
| CP-8 | S5 | `tests/Antiphon.Tests -> bin-c727-r2/` | drain-script-green | `/*/*/RunnerDrainScriptTests*/*` | V-33 (three verbs) | 1 executed, 0 failed | 1 | 3 |

R2 floor = 6 + 6 + 7 + 6 + 2 + 3 = **30** minutes.

#### R3 checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-9 | S6-tests | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | retire-op-red | `/*/*/PhoneHomeCommandDispatcherTests*/*` | V-14, V-15 | 2 new + 34 existing executed; V-14, V-15 fail at the unsupported-operation answer | 36 | 4 |
| CP-10 | S6 | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | retire-op-green | `/*/*/(PhoneHomeCommandDispatcherTests*)\|(PhoneHomeConnectionServiceTests*)/*` | V-14, V-15; dispatcher and connection regressions | all listed, 0 failed/skipped | 46 | 5 |
| CP-11 | S7-tests | `tests/Antiphon.Tests -> bin-c727-r3/` | retire-job-red | `/*/*/(RunnerRetireJobTests*)\|(PhoneHomeRollingRunnerTests*)/*` | V-16 to V-23 | 8 new + existing executed; the 8 new fail at their send/row/route/status assertions | 16 | 5 |
| CP-12 | S7 | `tests/Antiphon.Tests -> bin-c727-r3/` | retire-job-green | `/*/*/(RunnerRetireJobTests*)\|(PhoneHomeRollingRunnerTests*)\|(RunnerSlotEndpointTests*)\|(PhoneHomeConnectionTests*)\|(MultiRunnerDirectoryTests*)/*` | V-16 to V-23; slot, connection and directory regressions | all listed, 0 failed; 8 new methods, 0 skipped | 53 | 6 |
| CP-13 | S8-tests | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | broker-red | `/*/*/(BuildSlotBrokerTests*)\|(BuildSlotEndpointTests*)/*` | V-24 to V-27 | 4 new + 15 existing executed; V-24 to V-27 fail at their reap/renew/route/mode assertions | 19 | 5 |
| CP-14 | S8 | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | broker-green | `/*/*/(BuildSlotBrokerTests*)\|(BuildSlotEndpointTests*)\|(BuildSlotSettingsTests*)/*` | V-24 to V-27; broker regressions | all listed, 0 failed/skipped | 19 | 4 |
| CP-15 | S8-tests | `tests/Antiphon.Tests -> bin-c727-r3/` | wrapper-red | `/*/*/BuildSlotScriptTests*/*` | V-28 | 1 new + 7 existing executed; V-28 fails at the renew count | 8 | 4 |
| CP-16 | S8 | `tests/Antiphon.Tests -> bin-c727-r3/` | wrapper-green | `/*/*/BuildSlotScriptTests*/*` | V-28; wrapper regressions | all listed, 0 failed/skipped | 8 | 3 |
| CP-17 | S9-tests | `tests/Antiphon.Tests -> bin-c727-r3/` | compose-red | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)\|(RemoteScriptContractTests*)\|(RunnerDrainScriptTests*)/*` | V-29 to V-31, V-33 | new and updated methods fail at their text assertions; unrelated methods pass | 162 | 5 |
| CP-18 | S9 | `tests/Antiphon.Tests -> bin-c727-r3/` | compose-green | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)\|(RemoteScriptContractTests*)\|(RunnerDrainScriptTests*)\|(DockerStackDocumentationTests*)/*` | V-29 to V-31, V-33; compose, entrypoint, script and docs contracts | all listed, 0 failed/skipped | 172 | 5 |
| CP-19 | S9-tests | n/a | deploy-script-red | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | V-32 | exit 1: the script under test is missing (T-1 to T-6 FAIL) | n/a | 1 |
| CP-20 | S9 | n/a | deploy-script-green | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | V-32 | 6 cases, every `PASS`, exit 0 | n/a | 2 |
| CP-21 | S10 | `tests/Antiphon.Tests -> bin-c727-r3/` | docs-green | `/*/*/DockerStackDocumentationTests*/*` | D-20 | all listed, 0 failed/skipped | 10 | 4 |
| CP-22 | R1 to R3 active on the desktop + S9 landed | n/a | live-deploy-temp | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase deploy-temp` (desktop main checkout) | L-1 | exit 0; evidence files as L-1 names; one real `-Runner server2-temp` task settles | n/a | 25 |
| CP-23 | CP-22 | n/a | live-auth-overlap | the six `provider-auth` reads at L-1 and >= 30 min later | L-2 | `loggedIn` unchanged for claude, codex, grok on both ids | n/a | 35 |
| CP-24 | CP-23 | n/a | live-drain-redeploy-retire | `-Phase drain-old`, `redeploy-old`, `drain-temp`, `retire-temp` on the same run, plus the log grep | L-3 | exit 0 each; `retiredAt` set by the job; `temp-down.txt`; zero lock hits | n/a | 30 |

R3 ordinary floor = 4 + 5 + 5 + 6 + 5 + 4 + 4 + 3 + 5 + 5 + 1 + 2 + 4 = **53** minutes; the live
rows add about 90 minutes on the desktop lane plus the old runner's drain time, and are reported
not run by a server2-placed task.

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
  that follows it; the D-6 state row extended to local runners so the same drain/retire verbs
  apply.
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

- D-1: R1 is dispatched only from a master containing CARD-0710's `5f39210c`; the `Runners` map is
  the mechanism, and no `AllowedRunnerIds` set is added.
- D-2: `server2-temp` reuses server2's secret value and stays configured permanently.
- D-3: R1 has no red commit; its rows are green pins whose red is proven by Mutation PC-1
  (`RoutingSessionRunnerClient.Route` resolves the runtime default instead of the owner) and PC-2
  (`Status` answers from the first slot for any id).
- D-7: `Resolve` is untouched; `ResolveForNewWork` is the only new gate, used by the default
  policy, the dispatcher's claim and remote-prep gates, and the standing-agent start.
- D-8: a drain's redirect moves unlaunched tasks including explicit pins; a SourceLanding task is
  never moved; a drain without an eligible redirect holds.
- D-10: `dispatchEligible` keeps its meaning; `acceptingNewWork` is the new flag scripts wait on.
- D-13: only a drain with `retireWhenIdle: true` is retired by Hangfire; server2 is redeployed by
  the script, never retired.
- D-15: the temp runner has `restart: "no"`; the base file keeps `unless-stopped`.
- D-16: the broker uses renewal, not pid liveness, only when configured so; the desktop broker and
  wrapper behaviour is unchanged; the old server2 container stays unleased until its redeploy.
- Operation number: `Retire = 27`.

## Verification design (TestDesign, task 700c3a06)

Date: 2026-09-25. Stage: TestDesign, written on the server2 Linux runner against `origin/master`
`aa1b42d6` (this plan landed) and the CARD-0710 Code branch `origin/feat/card-task-8752034b` at
`fdf3778a` (114 commits behind master; repair task `7fa08907` starts there). Nothing below changes
the fix design (D-1 to D-20). This section **supersedes** the Plan's `### Coverage roster and
decisive assertions` and the three `### Checkpoints` tables above: every V/L row is pinned to a
file, a method, the decisive assertion, the production line that turns it red and the checkpoint
that runs it. Where the Plan's row could not go red as written, the row is re-scoped here and the
change is listed under *Stub flags*. No production code was changed; no test was run.

### Inspection

Bodies read at `fdf3778a` (0710 tip) unless marked *master*:

- `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs` (0710 and *master*): 0710 adds
  `StartAsync(configured:)`, `RegisterAsync(runnerId:, storeId:, secret:, platform:,
  capabilities:)`, `ConnectPeerAsync(runnerId:, storeId:, secret:)`, `WaitLiveAsync(runnerId:)`;
  master (CARD-0716) adds `StartAsync(shutdownTimeout:)`, `MapOperatorEndpoints`,
  `MapVersionEndpoints`, `OperatorShutdownCoordinator`, `ILaunchDrain`, a `TimeProvider.System`
  singleton and the peer's `CloseObserved`. `PostOperatorAsync(path, body, token, proxied)` and
  `OperatorTokenPath` exist on both. Boundary: R1 compiles only against the **union**; the 0710
  repair rebase owns it (see *Missing setup* M-1). The peer's `Launches` records
  `LaunchPlatformConstrained` on 0710 only; `Inputs`, `RequestCount(op)`, `Sessions`,
  `Transcripts`, `Reply`, `SilentFor`, `EmitAsync` on both. No `Retires`; default reply for
  `WorkspaceMirror`/`WorkspaceRemove` is `{ ok = true }`, which is not a
  `PhoneHomeWorkspaceMirrorResponse` (-> M-4).
- `tests/Antiphon.Tests/Application/MultiRunnerDirectoryTests.cs`: `Pair(secretA, secretB)` and
  `Entry(display, secret, pin)` are **private**; ids are `runner-a`/`runner-b`; `MaxCapacity = 4`;
  `host.Capacity = 2` before connecting two peers; `MarkRecovered(live)` makes a slot eligible
  without the pump; `Status("runner-c")` throws `NotFoundException` (-> V-1 boundary at HTTP).
- `tests/Antiphon.Tests/Application/PhoneHomeLaunchTransportTests.cs` `LaunchWorld`: isolated
  schema (`TestDbFixture.CreateIsolatedSchemaAsync`), `BridgeQueueHarness.CreateAsync` with the
  real `AgentProtocolAdapterFactory(..., directory: host.Directory)`, `AgentSessionService
  .LaunchInteractiveAsync`, `FakeTimeProvider` on the harness; `ReconnectAsync` waits for
  `SnapshotLive()` to change. Boundary: it launches through the service, not the dispatcher.
- `tests/Antiphon.Tests/Application/DispatcherRemotePrepStarvationTests.cs`: the dispatcher
  inside `BridgeQueueHarness` (`Configure`: `AddDelegationWorktreeGraph`, `PushGit` fake
  `ILandingGit`, `PhoneHomeLaunchPolicy`, `ISessionRunnerDirectory = host.Directory`,
  `RemoteWorkspaceService`, `RemoteWorkspacePreparer`, scoped `AgentTaskService` and
  `AgentTaskDispatcher`), `TickAsync`, `RemoteWorkspacePreparer.WhenIdleAsync`,
  `AgentSessionLaunchQueue.WaitForIdleAsync`. This is the RollingWorld base (-> H-1).
- `tests/Antiphon.Tests/Application/TaskPlatformDispatchTests.cs`: `SeedAsync` presets
  `WorktreePath`/`WorktreeBranch` so a Worktree task dispatches without git; `CreateDispatcher`
  with `RecordingSink : IAgentTaskLaunchSink` (a sink swallows the launch; RollingWorld must
  **not** register a sink so the claim reaches `AgentSessionLaunchQueue` -> peer). Boundary ->
  V-2 launch evidence is the peer frame, never the sink.
- `tests/Antiphon.Tests/Application/DefaultRunnerCreateTests.cs` `DefaultRunnerKit`: `Service(db,
  runnerDefaults:)`, `realDirectory:`, `ReadAsync` (Created audit + Warnings), Created segment
  `runner source=... reason=...`. Its `PhoneHome` is the legacy singleton (`required init`), so
  a two-id create test cannot use it (-> V-6 uses RollingWorld's `AgentTaskService`).
- `tests/Antiphon.Tests/Application/PhoneHomeStandingLaunchTests.cs`: `BuildLaunchHarness(schema,
  host, workspace, agentId, adapters, insertFault, gate)`, `harness.Control.StartAsync(agentId,
  new StartAgentRequest(Fresh: true))`, `SeedPinnedAgentAsync`; 14 `[Test]`.
- `tests/Antiphon.Tests/Agents/PhoneHomeConnectionTests.cs`: 19 on 0710, **23 on master**;
  `Recovery_barrier_withholds_dispatch` / `Disconnect_and_lease_expiry_refuse_new_work` show the
  `Resolve` refusal shape; `PostRegister`-style raw register lives in MultiRunnerDirectoryTests.
- *master* `tests/Antiphon.Tests/Api/OperatorShutdownEndpointTests.cs` (3): 403 body contains
  `operator_token_required`; token via `OperatorTokenFile.ReadOrCreate(host.OperatorTokenPath)`.
- *master* `tests/Antiphon.SessionRunner.Tests/BuildSlotBrokerTests.cs` (11): `Fixture(maxConcurrent,
  maxCpuCount, floorMb, ttlMinutes, retryAfterMs, waiterSilenceMs, enabled)`, `FakeLiveness`
  (`Start/Kill/IsAlive`), `FakeMemory`, `FakeTimeProvider Time`, `ListLogger`. No
  `holderLiveness`/`renewGraceSeconds` parameter (-> M-8). `BuildSlotEndpointTests` (4) uses
  `BuildSlotTestHost.StartAsync(...)`, a mini host mapping `MapBuildSlotRoutes()` only; it does
  **not** boot `Program` (-> V-27, M-9). `BuildSlotSettingsTests` (2), `BuildSlotEndToEndTests` (2).
- *master* `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs` (7) -> `ScriptHarness
  .RunHarnessCaseAsync("test-build-slot.ps1", "C589", case, rows, required)`; requires `C487 HARNESS
  EXIT CODE: 0`, `C487: N passed, 0 failed, N rows`, no `FAIL ` line. `scripts/test-build-slot.ps1`
  seams `C589_SLOT_SHIM`, `C589_SLOT_LOG`, `C589_SLOT_SCRIPT`, `C589_COMMAND_SHIM`;
  `scripts/fixtures/c589-slot-shim.ps1` answers `POST` and `DELETE` only (-> M-10).
- *master* `tests/Antiphon.SessionRunner.Tests/PhoneHomeCommandDispatcherTests.cs` (34):
  `new PhoneHomeCommandDispatcher(runtime, settings, authProbe?, logger?)`; `RecordingRuntime :
  IPhoneHomeRuntimeSurface` (`Owned`, `Mutations`, `MarkExited`, `ReleaseSlotAsync`); an unknown
  operation answers `PhoneHomeFrameKind.Error` with `phone_home_unsupported_operation`
  (`PhoneHomeCommandDispatcher.cs:269`). `KillAllAsync(TimeSpan, ct)` exists on
  `SessionRunnerRuntime` (`:1088`) but **not** on `IPhoneHomeRuntimeSurface` (-> M-7).
- *master* `tests/Antiphon.SessionRunner.Tests/PhoneHomeConnectionServiceTests.cs` (10): close
  description is not observed by any test; `PhoneHomeConnectionService.cs:164` closes with
  `"disconnect"` or the overflow code (-> Out of scope O-2).
- *master* `RunnerSlotReconcileJob.cs`, `HangfireConfiguration.AddOrUpdateRunnerSlotReconcileJob`
  (`server/Infrastructure/Agents/HangfireConfiguration.cs:27`, `AddOrUpdate` + `Trigger`),
  `RunnerSlotEndpointTests.cs:160` constructs the job directly and calls `ExecuteAsync`;
  `HangfireStartupSafetyTests` (12) registers a job on `InMemoryStorage` and reads
  `GetRecurringJobs()` (model for V-16b). `server/Program.cs:943` registers the reconcile job
  only when `phoneHome.Enabled`. `AgentIncidentKind` ends at `DelegateReleaseUnresolved = 76`
  (the Plan said "next after 74": **`RunnerForceRetired = 77`**).
- *master* `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs` (108):
  `ComposeFiles()` globs **`docker-compose*.yml`** at the repo root, so the new temp override is
  swept by `Only_the_server2_runner_is_privileged`, `Compose_never_lists_the_claude_token_under_
  environment` and the socket test without any edit; `Server2_file_defines_only_runner_and_state_
  init` collects every two-space key ending in `:`, so a top-level `networks:` block contributes
  **its child key `antiphon-build-slots`** to the list (-> V-29 expected list);
  `Server2_services_name_their_images` iterates `state-init, session-runner`;
  `Server2_runner_restarts_unless_stopped` reads the base `session-runner` block only.
  `DindRunnerContractTests` (22) `Server2_healthcheck_covers_phone_home_secret_readability` uses
  `SingleOrDefault(test:)` on the base runner block. `RemoteScriptContractTests` (25): `Remote()`,
  `Block(text, function)` (a `name() {` ... `}` body), `Executable`, the roster/stub-arm routing
  test reads `scripts/c590-real.ps1` and `scripts/verify-docker-stack.ps1`, and requires the
  `case) case_fn` dispatch line. `DockerStackDocumentationTests` (10) `Testing_doc_names_the_
  production_enablement_steps` asserts the literal `AllowedRunnerId=server2` (-> S2 edits it).
- *master* `scripts/test-apphost-graceful-stop.ps1` (CARD-0716; `-Case`, `Write-Pass/Write-Fail`,
  `seams.json`, `trace.jsonl`, `$script:Sentinel`): the C495 harness shape; a new
  `scripts/test-*.ps1` gets a `scriptCensus` row in `tests/test-execution-policy.json`
  (`disposition: unattended`) and a TUnit wrapper class (-> V-32, M-11). `ScriptHarness
  .RunHarnessCaseAsync` passes `-Case` and `-ResultsDirectory`.
- Production seams (0710): `PhoneHomeRunnerDirectory.cs` `Resolve` (`:85-98`), `Register`
  (`:195-260`, `RunnerMismatch` at `:200`), `Status` (`:459-507`, `NotFoundException` at `:468`),
  `DescribeAsync` (`:509-550`), `RunnerSlot` (`:583-599`), `SnapshotOf` flips `DispatchEligible`
  on lease expiry (`:575`); `SessionRunnerEndpoints.cs` status route `:47`, `RequireOperator` ->
  `OperatorCredential.Require` (403 `operator_token_required`); `ExceptionMiddleware` maps
  `HttpException.StatusCode` (`NotFoundException` -> 404); `RoutingSessionRunnerClient.Route`
  (`:85-97`); `RunnerScopedSessionRunnerClient.Current` (`:30`);
  `AgentProtocolAdapterFactory.Create(kind, runnerId)` -> `RemoteClient` (`:37-70`);
  `DefaultRunnerRoutingPolicy.Decide` (`_runners.Resolve(configured)` at `:205`) and
  `TryKindDefault` (`:262`); `AgentTaskService.cs:1214-1217` applies the runtime snapshot;
  `AgentTaskDispatcher.RemoteHoldForAsync` (`:936-967`, `Resolve` at `:950`),
  `CountRunnerOccupancyAsync` (`:969`), `PrepareRemoteWorkspaceAsync` (`:5532`), session
  stamping `RunnerId`/`RunnerStoreId = _runners.GetLiveStoreId(claimed.RunnerId)` (`:4455-4457`),
  launch handoff `_launchQueue.EnqueueInteractiveSession` when no sink (`:4536-4539`);
  `AgentControlService.cs:649-653` binds the standing session's runner (the D-7 gate goes before
  `_db.AgentSessions.Add(session)`); `SourceLandingAdmission.RequireSupportAsync` (`:59-61`) keeps
  `Resolve`; `PhoneHomeLiveConnection.cs:303-306` `DispatchEligible` gate;
  `RemoteWorkspaceService.RemoveMirrorAsync` (`:523-537`, best effort);
  `RemoteWorkspacePreparer` sets `RemoteWorktreePath`/resets backoff (`:139-141`);
  `RunnerDefaultSettingsService(db, settings, clock, events?, runners?)`,
  `EnsureInitializedAsync` imports `Delegation:DefaultRunnerId`, `PutAsync(PutRunnerDefaultsRequest(
  ExpectedRevision, GlobalRunnerId, KindDefaults, Reason, Provenance), callerTaskId, ct)`;
  `PhoneHomeRunnerSettingsRules.ValidateMapped` (`seen` is `OrdinalIgnoreCase`, `:294`);
  `PhoneHomeOperation` ends at `LaunchPlatformConstrained = 26`; `PhoneHomeRunnerStatusDto`
  (15 members, all trailing ones defaulted); `SessionRunnerCatalogueEntryDto` (13 members,
  `UnavailableReason`); *master* `BuildSlotBroker.SweepLocked` (`:162-188`: TTL then
  `_liveness.IsAlive`), `Release` (`:124`), `BuildSlotRoutes` (`POST`/`DELETE`/`GET /build-slots`),
  `src/Antiphon.SessionRunner/Program.cs` (top-level statements, **no `partial class Program`**,
  `MapBuildSlotRoutes()` at `:230`, `/sessions` at `:228`, `/capabilities` at `:205`,
  `AddHostedService<PhoneHomeConnectionService>()` at `:60`; the test project has no
  `Microsoft.AspNetCore.Mvc.Testing` reference).
- Boundaries mapped: unknown id vs configured-offline id vs desktop alias (V-1); same secret vs
  distinct secrets (V-3 + H-1 uses distinct); redirect = self / desktop / unknown / draining /
  offline (V-4b, V-6c, V-8b); reason length 200 vs 201 and empty (V-4b, V-22); `confirmRunnerId`
  case (V-22); `RetireMinDrainSeconds` at 59 s vs 60 s and `RetireIdleSeconds` at 119 s vs
  120 s (V-16); lease live vs expired with and without bound rows (V-19); grace at 89 s vs
  90 s (V-24); `renewEverySeconds` present vs absent (V-28); Queued+no session vs Dispatched
  (inherent: the rebind sweep is `Queued && AgentSessionId == null`, V-7 arm c pins the
  Dispatched row stays). Excluded boundaries are in *Out of scope*.

### Harness: `RollingWorld` and missing setup

**H-1 `RollingWorld`** (`tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerTests.cs`, S1):

- `CreateAsync(globalDefault: "server2" | "server2-temp" | null, distinctSecrets: false)`:
  `IsolatedTestSchema`; `FakeTimeProvider Clock` shared by **host and harness**
  (`PhoneHomeTestHost.StartAsync(Clock, connectionString: schema, configured: RollingPair(...))`);
  `host.Capacity = 2`; peers `A = ConnectPeerAsync(runnerId: "server2", storeId: StoreA, secret:
  SecretA)`, `B = ConnectPeerAsync(runnerId: "server2-temp", storeId: StoreB, secret: SecretB)`
  (equal by default, D-2; `distinctSecrets: true` for one V-23 arm), both `MarkRecovered`;
  `BridgeQueueHarness.CreateAsync` with `AlwaysOn = false`, `ConnectionString`, `TimeProvider =
  Clock`, `Delegation = { MaxConcurrentTasks = 32, AllowedRoots = ["/", "C:\\"],
  DefaultRunnerId = globalDefault }`, `ConfigureServices` = DispatcherRemotePrepStarvationTests'
  `Configure` **minus** `AdapterSlot` **plus** `AgentProtocolAdapterFactory(Options.Create(new
  AgentRegistrySettings()), sp.GetRequiredService<ISessionRunnerClient>(), directory:
  host.Directory)` (LaunchWorld's), `IOptions<PhoneHomeRunnerSettings> = RollingPair(...)` (the
  **map**, so `PhoneHomeLaunchPolicy.IsRunnerBound("server2-temp")` is true), scoped
  `RunnerDefaultSettingsService`, and from S3 `RunnerStateService`/`RunnerStateLoader`. No
  `IAgentTaskLaunchSink` is registered. After build: `RunnerDefaultSettingsService
  .EnsureInitializedAsync` (imports `globalDefault`).
- `RollingPair(idA, idB, secretA, secretB)` -> new `TestHelpers/PhoneHomeRunnerPairs.cs`
  (`internal static`), the `MultiRunnerDirectoryTests.Entry` shape with `MaxCapacity = 10`,
  `HostWorkspaceRoot = DelegateScriptRunner.RepoRoot`, `AllowDelegatedTasks = true`,
  `DisplayName = "server2" / "server2 (temp)"` (M-2).
- `SeedRunningSessionAsync(runnerId, storeId)` -> `AgentSession` Running with
  `RunnerId/RunnerStoreId/RunnerCwd = "/work"`, plus its owning `Agent` and `AgentTask`
  Dispatched (LaunchWorld's `SeedAsync` shape); peer `Sessions.Add(RunnerSessionDto(id, ...,
  "Running"))`. Returns `S_A`/`S_B`.
- `CreateDefaultPlacedTaskAsync(kind = ClaudeCode)` -> `AgentTaskService.CreateAsync(new
  CreateAgentTaskRequest("c727 ...", Role: Code, AgentKind: kind, Workspace: Worktree),
  new AgentTaskService.Caller(null, null, DelegateScriptRunner.RepoRoot))` through the harness
  scope (real `DefaultRunnerRoutingPolicy` + snapshot), then `ExecuteUpdate` `WorktreePath =
  <temp dir>`, `WorktreeBranch = "feat/c727-" + id[..8]` so dispatch needs no git (the
  TaskPlatformDispatchTests shape). `SeedQueuedRemoteTaskAsync(runnerId, remoteWorktreePath?,
  sourceLanding: false)` seeds the row directly (V-7/V-8/V-23 arms).
- `DispatchCycleAsync()` = `TickAsync`; `RemoteWorkspacePreparer.WhenIdleAsync()`; `TickAsync`;
  `AgentSessionLaunchQueue.WaitForIdleAsync(15 s)`; bounded by 20 s wall clock. A remote task
  needs two ticks (prep then claim), so "one tick" in the Plan's V-7 means one cycle.
- Peers script `Reply` for `WorkspaceMirror` -> `PhoneHomeWorkspaceMirrorResponse("/work/mirrors/"
  + name)` and `WorkspaceRemove` -> `PhoneHomeWorkspaceRemoveResponse(true, null)` (M-4); from
  S6 the peer's `DefaultReply` answers `Retire` with `RunnerRetireResult(bootId, 0, now)` and
  records `Retires` (M-5).
- Operator calls: `host.PostOperatorAsync("/api/session-runners/{id}/drain", new { reason,
  redirectTo, retireWhenIdle }, token)`; `token = OperatorTokenFile.ReadOrCreate(host
  .OperatorTokenPath)`; the 403 arm posts `token: null, proxied: true`.
- `ReadStateAsync(id)` reads `SessionRunnerStates` through a fresh context;
  `StatusAsync(id)` = `GET /api/session-runners/{id}/status` as `JsonElement`;
  `ReadTaskEventsAsync(taskId, type)`.
- `RebuildDirectoryAsync()` (V-4): `new PhoneHomeRunnerDirectory(host.Local, settings,
  scopeFactory over the same schema, Clock)` fed by `RunnerStateLoader.LoadAsync(directory)`.
- Job: `new RunnerRetireJob(host.Directory, db, stateService, Options.Create(settings), Clock,
  NullLogger)`; `ExecuteAsync(ct)` returns the number retired. Never through Hangfire.

**Missing setup** (record; each is a Code deliverable in the named slice):

- M-1 (0710 repair, before R1): `PhoneHomeTestHost` must carry **both** the 0710 members
  (`configured:`, `runnerId:`/`storeId:`/`secret:`/`platform:`/`capabilities:`,
  `WaitLiveAsync(runnerId:)`) and master's CARD-0716 members. R1's CP-1 build is the check.
- M-2 (S1): `TestHelpers/PhoneHomeRunnerPairs.cs` (`Pair`/`Entry` are private in
  `MultiRunnerDirectoryTests`).
- M-3 (S1): `PhoneHomeTestHost.StartAsync(configured:)` with `connectionString` and a clock
  (the three-argument call is new; today's callers pass at most two).
- M-4 (S1): scripted `WorkspaceMirror`/`WorkspaceRemove` replies (peer default is `{ ok }`).
- M-5 (S6): `PhoneHomeScriptedPeer.Retires` + `RunnerRetireResult` default reply.
- M-6 (S3): `PhoneHomeTestHost` registers `RunnerStateService`/`RunnerStateLoader` when a
  connection string is present, an in-memory `IRunnerStateStore` otherwise; `ApplyState(runnerId,
  RunnerState)` on the directory for the DB-less classes (V-9, V-11, V-12).
- M-7 (S6): `IPhoneHomeRuntimeSurface.KillAllAsync(TimeSpan, ct)` (default: refuse) and a
  `RecordingRuntime` implementation recording `"kill-all"`; a `RecordingLifetime :
  IHostApplicationLifetime` with `StoppedAt` (Stopwatch ticks) in the test file;
  `PhoneHomeCommandDispatcher` ctor gains `IHostApplicationLifetime? lifetime = null`.
- M-8 (S8): `BuildSlotBrokerTests.Fixture(holderLiveness:, renewGraceSeconds:)`.
- M-9 (S8): `public partial class Program;` appended to `src/Antiphon.SessionRunner/Program.cs`
  and `Microsoft.AspNetCore.Mvc.Testing` in `Antiphon.SessionRunner.Tests.csproj`, so V-27 boots
  the **real** runner `Program` with `SessionRunner:BuildSlotsOnly=true`, `PhoneHome:Enabled=false`
  through `WebApplicationFactory<Program>` (TestServer, no port, never 17204). If the top-level
  program cannot boot under TestServer, V-27 downgrades to a route-map pin (`SessionRunnerRouteMap
  .For(buildSlotsOnly)`) **and** L-1 records `broker-routes.txt` (`curl` of `/sessions` -> 404,
  `/build-slots` -> 200 from the broker container); Code states which.
- M-10 (S8): `scripts/fixtures/c589-slot-shim.ps1` gains a `renew` arm (`POST .../{id}/renew` ->
  `SLOT RENEW <id>` log row, 204) and `C589_SLOT_SCRIPT` grants may carry `renewEverySeconds`.
- M-11 (S9): `tests/test-execution-policy.json` `scriptCensus` row for
  `scripts/test-deploy-server2.ps1` (`unattended`); `DeployServer2ScriptTests` wrapper class;
  harness seams `C727_VERIFY_SHIM` (stands in for `verify-docker-stack.ps1`), `C727_HTTP_SHIM`
  (scripted status/drain/clear answers per phase from `seams.json`), `C727_POLL_SECONDS=0`,
  `ANTIPHON_OPERATOR_TOKEN_FILE` -> a temp file holding `$script:Sentinel`.
- M-12 (S7): `RunnerRetireJob(directory, db, stateService, IOptions<PhoneHomeRunnerSettings>,
  TimeProvider, logger)` takes the clock as a constructor argument (Hangfire resolves
  `TimeProvider` from DI; tests pass the `FakeTimeProvider`).

### Delivery inventory

Each new or changed asynchronous path, joined by its durable identity. A request, a queue insert,
an event, a flag or an ack is never the delivery proof; the recipient's evidence is named.

| # | Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt | Durable identity | Rows |
|---|---|---|---|---|---|---|---|---|
| P-1 | New task -> runner launch | `AgentTaskDispatcher.TickAsync` claim -> `AgentSessionLaunchQueue` -> `AgentSessionService.LaunchInteractiveAsync` -> `RunnerScopedSessionRunnerClient` | the bound runner's socket (peer B) | `AgentSessions` row (`RunnerId`, `RunnerStoreId`, `RunnerCwd`) committed with the claim before the frame | CARD-0679 launch-transport retries (regression class in CP-1); remote-prep backoff (`DispatchNotBeforeAt`) | peer `Launches` frame whose `RunnerLaunchRequest.SessionId` equals the row id, **and** the row `RunnerId`/`RunnerStoreId` | session id + `AcceptedStartedAt` generation | V-2, V-6, V-7, V-23 |
| P-2 | Input to a bound session | `RoutingSessionRunnerClient.SendInputAsync` (binding) and the adapter's `RunnerScopedSessionRunnerClient` | the owning runner's socket (peer A) | `AgentSessions` binding (all-or-none) | connection re-resolve on every call (CARD-0679 D-6) | peer `Inputs` frame for the session id | session id | V-2, V-5, V-23 |
| P-3 | Drain redirect rebind | dispatcher tick, before claim | task row + `WorkspaceRemove` to the **old** runner + P-1 on the **new** runner | `AgentTasks.RunnerId`, `RemoteWorktreePath = null`, `RemotePrepFailures = 0`, `DispatchNotBeforeAt = null`, event `runner drain_redirect from=.. to=..` | next tick (idempotent: a rebound row is no longer bound to a draining runner) | peer A `RequestCount(WorkspaceRemove) == 1`; peer B `Launches == 1`; row `RunnerId` | task id | V-7, V-8, V-23 |
| P-4 | Retire | `RunnerRetireJob.ExecuteAsync` (cron) / `POST .../retire` | runner process exit through the `Retire` frame | `SessionRunnerStates.RetiredAt/RetireReason`; the runner's own exit (container `restart: "no"`) | next cron run; a `phone_home_runner_busy` answer clears `IdleObservedAt`; a lease expiry with no rows closes the book without a send | peer B `Retires` frame with the expected `Force`; row `RetiredAt`; **live**: `status-temp-retired.json` `retiredAt` and `temp-down.txt` (L-3) | runner id + `ProcessBootId` in the result | V-16 to V-23, L-3 |
| P-5 | Broker lease renewal | `scripts/lib/build-slot.ps1` renewer thread | broker `POST /build-slots/{id}/renew` | broker memory (`LastRenewedAt`); reaped at `RenewGraceSeconds` | the sweep itself; a dead wrapper is reaped, never revoked while renewing | shim log `SLOT RENEW <id>` rows before the `SLOT DELETE`; `List().Leases[..].LastRenewedAt` | lease id | V-24 to V-28 |
| P-6 | Retire job registration | `Program.cs` startup -> `HangfireConfiguration.AddOrUpdateRunnerRetireJob` | Hangfire storage | recurring job row | `AddOrUpdate` on every start | `GetRecurringJobs()` has `antiphon:runner-retire` with the cron | job id | V-16b |

Producer-to-recipient through the **real queue**: P-1/P-3 run the real `AgentTaskDispatcher`,
`AgentSessionLaunchQueue` and `AgentProtocolAdapterFactory` against two real phone-home sockets
(H-1). Arms: *busy recipient* = V-8b (redirect target `server2-temp` at `DeclaredCapacity`:
held `RunnerAtCapacity`, nothing launched, `RunnerId` already rebound); *one already eligible* =
V-2 and V-6a; *crash / enqueue-failure recovery at each handoff* = V-7c (peer A `SilentFor
(WorkspaceRemove)`: the rebind still completes, one Warning names the old path, the launch
reaches B), V-23 arm (peer B aborted under its first `Launch`: CARD-0679 pre-ack re-send on
the replacement connection, `Launches` on the replacement == 1) and the CP-1/CP-4 regression
classes (`PhoneHomeLaunchTransportTests`, `MultiRunnerRecoveryTests`).

Declared substitutes: (a) a `PhoneHomeScriptedPeer` frame proves the **transport** reached the
right runner's socket; it is not a transcript and proves no UserPrompt was recorded by a real
agent; (b) the retire evidence in tests is the peer frame plus the state row; the process exit and
the container stopping are only proven by L-3. The transcript-confirmed proof that a session on
the new generation takes work is **L-1**: one real `delegate.ps1 -Runner server2-temp -Worktree`
task settles and its session transcript, read through the ops HTTP surface, shows the
`UserPrompt` row carrying the brief (`provider-auth`, `status.json` and the settlement receipt are
not that proof). A design stopping at "the frame was written" is rejected here: every P row above
ends at the recipient (frame observed **on the peer** + persisted row), and the live rows end at
the runner's own exit and the transcript.

### Proves it works now

Layer key: HTTP = `PhoneHomeTestHost` route; Dir = directory in memory; World = `RollingWorld`
(DB + dispatcher + two peers); Runner = `Antiphon.SessionRunner.Tests`; Text = file contract;
Script = pwsh harness; Live = server2. "Red" names the production line whose absence or change
turns the row red **at the decisive assertion**; R1 rows are green pins whose red is a PC.

| ID | Behaviour | Layer | File / method | Decisive assertion (first to fail) | Red mechanism | CP |
|---|---|---|---|---|---|---|
| V-1 | An unknown runner id is 404 with no live identity; a configured offline id is 200 not available; desktop is 200 both false; the connected id is eligible with its own store | HTTP | `tests/Antiphon.Tests/Agents/PhoneHomeConnectionTests.cs` `Unknown_runner_status_is_404_and_carries_no_live_runner_identity` | `((int)other.StatusCode).ShouldBe(404)` for `GET /api/session-runners/server2-other/status`; then body JSON has no `runnerStoreId`/`processBootId`/`buildVersion` properties; `server2-temp` -> 200 `available:false, dispatchEligible:false`; `desktop` -> 200 both false; `server2` -> `dispatchEligible:true`, `runnerStoreId == StoreA` | green pin; `PhoneHomeRunnerDirectory.Status` `:468` throw (PC-4) | CP-1 |
| V-2 | A default-placed task launches on `server2-temp` only while input to a session bound to `server2` reaches `server2` only | World | `PhoneHomeRollingRunnerTests.Input_to_a_session_on_server2_reaches_server2_while_a_new_launch_goes_to_server2_temp` | after `DispatchCycleAsync`: `peerB.Launches.Count.ShouldBe(1)`, `peerA.Launches.Count.ShouldBe(0)`, row `RunnerId.ShouldBe("server2-temp")`, `RunnerStoreId.ShouldBe(StoreB)`; then `RoutingSessionRunnerClient.SendInputAsync(S_A, "x")` and `AgentProtocolAdapterFactory.Create(Raw, "server2").SendInputAsync(S_A, "y")`: `peerA.Inputs.Count.ShouldBe(2)`, **`peerB.Inputs.Count.ShouldBe(0)`** | green pin; `RoutingSessionRunnerClient.Route` `:94` (PC-1), `RunnerScopedSessionRunnerClient.Current` `:30` (PC-2), `AgentProtocolAdapterFactory.RemoteClient` `:66-69` (PC-3) | CP-1 |
| V-3 | The D-2 map (two entries, same secret, same host root) validates; both ids are known; a case-variant duplicate id fails | Dir | `tests/Antiphon.Tests/Application/PhoneHomeRunnerSettingsValidatorTests.cs` `Two_entries_with_the_same_secret_and_host_root_validate` | `PhoneHomeRunnerSettingsRules.Validate(RollingPair(...)).ShouldBeEmpty()`; `new PhoneHomeRunnerDirectory(local, Options.Create(pair), NoScope, TimeProvider.System).KnownRunnerIds.ShouldContain("server2")`/`("server2-temp")`; a map built with `StringComparer.Ordinal` holding `server2` and `SERVER2` -> `failures.ShouldContain(f => f.Contains("duplicated"))` | green pin; `ValidateMapped` `:294-312` (PC-5) | CP-2 |
| V-4 | Drain needs the token, persists, shows in status, survives a directory rebuild | World | `PhoneHomeRollingRunnerTests.Drain_requires_the_operator_token_persists_the_state_and_survives_a_directory_rebuild` | `((int)noToken.StatusCode).ShouldBe(403)` and `body.ShouldContain("operator_token_required")`, `ReadStateAsync("server2").ShouldBeNull()`; with token 200; row `Draining == true`, `DrainedAt` within Clock, `RedirectTo == "server2-temp"`, `DrainReason == reason`; status `draining:true`, **`acceptingNewWork:false`**, `dispatchEligible:true`; `RebuildDirectoryAsync()` then `Should.Throw<ServiceUnavailableException>(() => rebuilt.ResolveForNewWork("server2")).Code.ShouldBe("phone_home_runner_draining")` | S3/S4-tests: route absent -> 404 at the first `ShouldBe(403)`; post-land PC-15, PC-19, PC-8 | CP-3 red, CP-4 green |
| V-4b | A redirect must name another configured, enabled, non-draining runner; the reason is bounded | HTTP+World | `PhoneHomeRollingRunnerTests.Drain_redirect_must_name_another_configured_non_draining_runner` | `redirectTo: "server2"` (self) -> `((int)r.StatusCode).ShouldBe(409)` and code `phone_home_redirect_invalid`; `"desktop"` -> 409; `"nobody"` -> 409; to `server2-temp` while it is draining -> 409; empty reason -> 400; 201-char reason -> 400; 200-char reason -> 200; re-POST with a new reason -> 200 and the row updated (idempotent) | S4-tests: 404 at the first `ShouldBe(409)`; PC-18 | CP-3, CP-4 |
| V-5 | A draining runner still serves its sessions | World | `PhoneHomeRollingRunnerTests.A_draining_runner_still_serves_input_transcript_kill_and_release_for_its_sessions` | after the drain, via `RoutingSessionRunnerClient` for `S_A`: `SendInputAsync`, `GetTranscriptAsync`, `KillGenerationAsync(S_A, gen)`, `ReleaseSlotAsync`: **`peerA.RequestCount(Input).ShouldBe(1)`**, then Transcript/KillGeneration/ReleaseSlot each `>= 1`, and `peerB.RequestCount(op).ShouldBe(0)` for all four; `(await host.Directory.GetInventoryAsync("server2", ct)).ShouldBeOfType<RunnerInventory.Available>()` | Code red: the drain POST is 404 (S3/S4-tests); the guard itself is PC-7 (`Resolve` `:85-98` refuses when draining) | CP-3, CP-4 |
| V-6 | Create-time placement follows an eligible redirect, falls back with `runner_draining`, never moves an explicit pin | World | `PhoneHomeRollingRunnerTests.Default_placement_follows_the_drain_redirect_and_falls_back_without_one` | arm a (global default `server2`, drained -> `server2-temp` eligible): **`saved.Task.RunnerId.ShouldBe("server2-temp")`**, Created contains `reason=drain_redirect:server2`, `Warnings.ShouldBeEmpty()`; arm b (drained, no redirect): `RunnerId.ShouldBeNull()`, Created `reason=runner_draining`, one Warning; arm c (redirect to `server2-temp` whose peer was disposed and lease expired on Clock): `RunnerId.ShouldBeNull()`, `reason=runner_draining`; arm d (`RunnerId: "server2"` explicit while draining): `RunnerId.ShouldBe("server2")`, `source=explicit` | S4-tests: `DefaultRunnerRoutingPolicy.Decide` `:205` still `Resolve`s the draining runner -> arm a gets `"server2"`; PC-9 | CP-3, CP-4 |
| V-7 | A Queued task bound to a draining runner is rebound before claim, its old mirror removed through the old runner; SourceLanding and Dispatched rows never move | World | `PhoneHomeRollingRunnerTests.A_queued_task_bound_to_a_draining_runner_is_rebound_to_the_redirect_before_claim` | arm a (Queued on `server2`, `RemoteWorktreePath = "/work/mirrors/old"`): after one `DispatchCycleAsync`: **`task.RunnerId.ShouldBe("server2-temp")`**, `peerA.RequestCount(WorkspaceRemove).ShouldBe(1)` with payload path `/work/mirrors/old`, `RemoteWorktreePath.ShouldStartWith("/work/mirrors/")` and `ShouldNotBe("/work/mirrors/old")`, `peerB.Launches.Count.ShouldBe(1)`, `peerA.Launches.Count.ShouldBe(0)`, one task event Detail containing `drain_redirect from=server2 to=server2-temp`, `RemotePrepFailures == 0`; arm b (`SourceLandingOperationId = Guid`, `WorktreePath = "/work/snap"`): `RunnerId.ShouldBe("server2")`, `peerA.RequestCount(WorkspaceRemove)` unchanged, a Held event `Held: runner 'server2' is draining`; arm c (peer A `SilentFor(WorkspaceRemove)`): rebound anyway, one Warning naming `/work/mirrors/old`, launch on B; arm d (a Dispatched row with a session on `server2`): untouched | S4-tests: `RemoteHoldForAsync` `:936-967` has no rebind -> arm a gets `"server2"` (claimed and launched on A); PC-10, PC-11 | CP-3, CP-4 |
| V-8 | Without an eligible redirect the task is held, never launched | World | `PhoneHomeRollingRunnerTests.A_queued_task_on_a_draining_runner_without_an_eligible_redirect_is_held` | arm a (no redirect): **`peerA.Launches.ShouldBeEmpty()`**, `task.Status.ShouldBe(Queued)`, `RunnerId.ShouldBe("server2")`, Held Detail `ShouldStartWith("Held: runner 'server2' is draining")`, exactly one Held row after two cycles (deduplicated); arm b (redirect `server2-temp` at capacity: `host.Capacity = 1` and one Running row bound to it): rebound `RunnerId == "server2-temp"`, then held `RunnerAtCapacity`, `peerB.Launches.ShouldBeEmpty()`; arm c (redirect to an offline `server2-temp`): held naming `redirect 'server2-temp' is not accepting work`, `RunnerId` unchanged | S4-tests: launched on A (`Resolve` passes) -> arm a red; PC-12 | CP-3, CP-4 |
| V-9 | Draining changes neither eligibility nor capacity; only `ResolveForNewWork` refuses; the catalogue says so | Dir+HTTP | `PhoneHomeConnectionTests.Draining_changes_neither_dispatch_eligibility_nor_capacity` | `host.Directory.ApplyState("server2", draining)`; `DeclaredCapacity("server2").ShouldBe(2)`; `Resolve("server2").ShouldBeOfType<PhoneHomeRunnerClient>()`; **`Should.Throw<ServiceUnavailableException>(() => host.Directory.ResolveForNewWork("server2")).Code.ShouldBe("phone_home_runner_draining")`**; `GET /api/session-runners` entry `server2`: `acceptingNewWork == false`, `dispatchEligible == true`, `unavailableReason == "draining"`; `server2-temp` entry `acceptingNewWork == true` | S4-tests: `ISessionRunnerDirectory.ResolveForNewWork` default body is `Resolve` -> no throw; PC-6, PC-20 | CP-3, CP-4 |
| V-10 | Clear needs the token, restores new work, resets the retire fields | World | `PhoneHomeRollingRunnerTests.Clear_drain_restores_new_work_and_resets_the_retire_fields` | `POST .../drain/clear` without token -> `ShouldBe(403)`; with token after a drain whose row was given `RetiredAt`/`RetireReason`/`IdleObservedAt`/`RetireWhenIdle` by direct update: **`row.Draining.ShouldBeFalse()`**, `RedirectTo.ShouldBeNull()`, `RetireWhenIdle.ShouldBeFalse()`, `IdleObservedAt.ShouldBeNull()`, `RetiredAt.ShouldBeNull()`, `RetireReason.ShouldBeNull()`; a new default-placed task binds `server2`; status `acceptingNewWork:true`; clear on an id that is not draining -> 200 no-op | S4-tests: 404; PC-16, PC-21 | CP-3, CP-4 |
| V-11 | A retired id cannot register until cleared | HTTP | `PhoneHomeConnectionTests.A_retired_runner_id_cannot_register_until_its_drain_is_cleared` | `host.Directory.ApplyState("server2-temp", retired)`; raw `POST /api/session-runners/register` for `server2-temp`: **`((int)refused.StatusCode).ShouldBe(409)`** and body contains `phone_home_runner_retired`; `SnapshotLive("server2-temp").ShouldBeNull()`; `ApplyState(cleared)` -> 200 with a ticket; `server2` registers throughout | S4-tests: `Register` `:195-260` has no `RetiredAt` check -> 200 ticket; PC-13 | CP-3, CP-4 |
| V-12 | A standing agent start on a draining runner is refused before any launch | World-like | `PhoneHomeStandingLaunchTests.Standing_agent_start_on_a_draining_runner_is_refused` | `host.Directory.ApplyState("grok-linux", draining)`; `BuildLaunchHarness(...)`; **`(await Should.ThrowAsync<ConflictException>(() => harness.Control.StartAsync(agentId, new StartAgentRequest(Fresh: true), ct))).Code.ShouldBe("phone_home_runner_draining")`**; `peer.Launches.Count.ShouldBe(launchesBefore)`; no `AgentSessions` row for the agent; `ApplyState(cleared)` then the start returns a `PersistentSessionId` and `harness.LaunchQueue.Owns(sessionId)` | S4-tests: `AgentControlService.cs:625` has no gate -> the start returns and a session row exists; PC-14 | CP-3, CP-4 |
| V-13 | Status reports per-runner sessions, queued tasks and runner sessions, plus the drain stamps | World | `PhoneHomeRollingRunnerTests.Status_reports_sessions_queued_tasks_and_runner_sessions_per_runner` | one Running row + one Queued unlaunched task bound to `server2`, peer A listing one session, `GetInventoryAsync("server2")` once: **`server2.GetProperty("sessions").GetInt32().ShouldBe(1)`**, `queuedTasks == 1`, `runnerSessions == 1`; `server2-temp`: `0, 0` and `runnerSessions` null before any List; after a drain `drainedAt` set and `drainReason == reason` | S3/S4-tests: the DTO members exist (allowed in the red commit) but stay null -> `GetInt32()` on null fails at the first assertion | CP-3, CP-4 |
| V-14 | The runner refuses an unforced retire while sessions are owned; a forced retire kills first | Runner | `tests/Antiphon.SessionRunner.Tests/PhoneHomeCommandDispatcherTests.cs` `Retire_refuses_while_sessions_are_owned_unless_forced` | `runtime.Owned = 1`; `Retire(force:false)`: **`busy.ErrorCode.ShouldBe(PhoneHomeProblemTypes.RunnerBusy)`**, `busy.StatusCode.ShouldBe(409)`, `busy.ErrorDetail.ShouldContain("1")`, `runtime.Mutations.ShouldBeEmpty()`; `Retire(force:true)`: `runtime.Mutations.ShouldContain("kill-all")`, result `KilledSessions.ShouldBe(1)`, `ProcessBootId.ShouldBe(runtime.BootId)` | S6-tests: `:269` answers `phone_home_unsupported_operation` -> the first assertion; PC-22, PC-23 | CP-9 red, CP-10 green |
| V-15 | The reply is written before the host stops, and the stop follows within one second | Runner | `PhoneHomeCommandDispatcherTests.Retire_reply_is_written_before_the_host_stops` | `RecordingLifetime`; `var reply = await dispatcher.DispatchAsync(Retire(force:false))` with `Owned = 0`: `reply.Kind.ShouldBe(Result)`; **`lifetime.Stopped.ShouldBeFalse()`** at return; `lifetime.StoppedWithin(TimeSpan.FromSeconds(1)).ShouldBeTrue()`; `Retire` while `Owned = 1` unforced: `Stopped` stays false for 500 ms | S6-tests: `StopApplication` never called -> `StoppedWithin` false; PC-24 | CP-9, CP-10 |
| V-16 | The job stamps idle first and retires only on a later run past the idle window | World+Job | `tests/Antiphon.Tests/Application/RunnerRetireJobTests.cs` `Draining_runner_with_retire_when_idle_is_retired_after_the_idle_window` | drain `server2-temp` (`retireWhenIdle:true`) at T0; run at T0+59 s: `IdleObservedAt.ShouldBeNull()`, `Retires.ShouldBeEmpty()`; run at T0+60 s: `IdleObservedAt.ShouldBe(T0+60 s)`, **`peerB.Retires.ShouldBeEmpty()`**, `RetiredAt.ShouldBeNull()`; run at +119 s after that: still empty; run at +120 s: `peerB.Retires.Count.ShouldBe(1)`, frame `Force.ShouldBeFalse()`, `RetiredAt.ShouldBe(Clock now)`, `RetireReason.ShouldBe("idle")`, return value 1; a fifth run sends nothing more | S7-tests: `RunnerRetireJob.ExecuteAsync` stub returns 0 and writes nothing -> the +120 s `Count.ShouldBe(1)` fails (the earlier `ShouldBeEmpty` pass on a no-op); PC-25, PC-49 | CP-11 red, CP-12 green |
| V-16b | The retire job is registered with its cron and triggered at startup like the reconcile job | Unit | `tests/Antiphon.Tests/Infrastructure/HangfireStartupSafetyTests.cs` `Recurring_retire_job_is_re_added_with_its_cron` | `InMemoryStorage` + `RecurringJobManager`; `HangfireConfiguration.AddOrUpdateRunnerRetireJob(manager, new PhoneHomeRunnerSettings())`; **`connection.GetRecurringJobs().ShouldHaveSingleItem().Id.ShouldBe("antiphon:runner-retire")`**, `Cron.ShouldBe("* * * * *")` | S7-tests: stub method registers nothing -> `ShouldHaveSingleItem` fails; PC-48 | CP-11, CP-12 |
| V-17 | A live row, a queued task, a listed runner session or a busy answer each keep the runner draining | World+Job | `RunnerRetireJobTests.Live_session_queued_task_or_busy_answer_keeps_the_runner_draining` | four arms on fresh worlds, each past both windows: a) Running row bound to `server2-temp`: **`peerB.Retires.ShouldBeEmpty()`**, `RetiredAt.ShouldBeNull()`; b) Queued unlaunched task bound: empty, and `host.Logs.Entries` has one Warning naming the task id; c) peer B `Sessions` holds one `Running`: empty; d) peer B replies `Retire` with error `phone_home_runner_busy`: `Retires.Count == 1`, `RetiredAt.ShouldBeNull()`, `IdleObservedAt.ShouldBeNull()` (was set) | S7-tests: no-op stub -> arms a-c pass vacuously and arm d fails at `Retires.Count.ShouldBe(1)`; the guards are PC-26 to PC-29 | CP-11, CP-12 |
| V-18 | `retireWhenIdle:false` is never retired | World+Job | `RunnerRetireJobTests.A_drain_without_retire_when_idle_is_never_retired` | `server2` drained with `retireWhenIdle:false`, idle through three windows: **`peerA.Retires.ShouldBeEmpty()`**, `RetiredAt.ShouldBeNull()`, `IdleObservedAt.ShouldBeNull()`, return 0 | S7-tests: passes vacuously (declared: its red is PC-30 only) | CP-11, CP-12 |
| V-19 | A disconnected, expired, idle runner is marked retired without a send; with bound rows it stays draining | World+Job | `RunnerRetireJobTests.Disconnected_draining_runner_with_no_bound_rows_is_marked_retired_without_a_send` | arm a: `peerB.Socket.Abort()`, `Clock.Advance(LeaseSeconds + 1 s)` past both windows: **`RetiredAt.ShouldNotBeNull()`**, `RetireReason.ShouldBe("idle_disconnected")`, `peerB.Retires.ShouldBeEmpty()`; arm b: same with one Running row bound: `RetiredAt.ShouldBeNull()`; arm c: peer aborted but lease still live: `RetiredAt.ShouldBeNull()` (no send possible, no stamp) | S7-tests: no-op stub -> arm a fails at `ShouldNotBeNull`; PC-31, PC-49 | CP-11, CP-12 |
| V-20 | Forced retire needs the token, the confirmation and a draining runner | World | `PhoneHomeRollingRunnerTests.Forced_retire_requires_the_operator_token_and_the_runner_id_confirmation` | `POST .../server2-temp/retire` without token -> **`ShouldBe(403)`**; wrong `confirmRunnerId` -> 400; not draining -> 409 `phone_home_runner_not_draining`; `peerB.Retires.ShouldBeEmpty()` after all three; no state row change | S7-tests: 404; PC-17, PC-32, PC-33 | CP-11, CP-12 |
| V-21 | Forced retire fails bound rows, writes the incident, sends `force:true`, stamps `forced:` | World | `PhoneHomeRollingRunnerTests.Forced_retire_fails_bound_sessions_writes_an_incident_and_sends_a_forced_retire` | `S_B` Running + a Dispatched task; drain then retire with the token and `confirmRunnerId`: **`S_B.Status.ShouldBe(SessionStatus.Failed)`**, `FailureReason.ShouldContain("server2-temp")` and the reason, `TerminationSource.ShouldBe(SystemRequest)`; `peerB.Retires.Count.ShouldBe(1)` and `Force.ShouldBeTrue()`; `AgentIncidents.Count(k == RunnerForceRetired).ShouldBe(1)`; row `RetiredAt` set, `RetireReason.ShouldStartWith("forced:")`; peer B disconnected afterwards -> `RetiredAt` still set (stamped either way) | S7-tests: 404; PC-34, PC-35 | CP-11, CP-12 |
| V-22 | Forced-retire boundaries: reason 200 vs 201, `confirmRunnerId` exact case, unknown id | World | `PhoneHomeRollingRunnerTests.Forced_retire_boundaries_reason_length_confirmation_case_and_unknown_id` | 201-char reason -> **`ShouldBe(400)`**; 200-char reason -> 200; `confirmRunnerId: "Server2-Temp"` -> 400; `.../nobody/retire` -> 404; `.../desktop/retire` -> 404; `Retires.Count.ShouldBe(1)` (only the 200 arm sent) | S7-tests: 404 at the first `ShouldBe(400)`; PC-33 | CP-11, CP-12 |
| V-23 | The whole rolling flow in one process | World+Job | `PhoneHomeRollingRunnerTests.Rolling_upgrade_moves_new_launches_to_server2_temp_keeps_server2_reachable_and_retires_server2_temp_when_idle` | global default `server2`; `S_A` on A; drain `server2 -> server2-temp` (`retireWhenIdle:false`); task 1 -> `peerB.Launches == 1`, `RunnerId == "server2-temp"`; input to `S_A` -> `peerA.Inputs == 1`, `peerB.Inputs == 0`; peer A emits `S_A` exit (session event) and the row leaves Running; `drain/clear server2`; task 2 -> `peerA.Launches == 1`; drain `server2-temp -> server2` (`retireWhenIdle:true`); peer B marks `S_B` Exited and clears `Sessions`; task 1's row set Succeeded; `Clock.Advance(60 s)`, run, `Clock.Advance(120 s)`, run: **`peerB.Retires.Count.ShouldBe(1)`**; status `server2-temp.retiredAt` set, `server2.acceptingNewWork == true`; raw register `server2-temp` -> 409 until `drain/clear`; then 200. One arm with `distinctSecrets: true` repeats the first two steps only | after R2 (S7-tests): the retire step (no-op job) -> `Count.ShouldBe(1)` fails; every earlier step is R2's green | CP-11, CP-12 |
| V-24 | Renew mode reaps a lease not renewed within the grace and ignores pid liveness | Runner | `BuildSlotBrokerTests.Renew_mode_reaps_a_lease_not_renewed_within_the_grace_and_ignores_pid_liveness` | `Fixture(holderLiveness: "renew", renewGraceSeconds: 90)`; grant; `Liveness.Kill(pid)`; `Time.Advance(10 s)`; `Sweep().ShouldBe(0)`; **`Broker.List().Leases.Count.ShouldBe(1)`**; `Time.Advance(79 s)` (89 s): `Sweep().ShouldBe(0)`; `Renew(id).ShouldBeTrue()`; `Time.Advance(89 s)`: 0 reaped; `Time.Advance(2 s)`: `Sweep().ShouldBe(1)`, `Leases.ShouldBeEmpty()`, log names `renew grace`; pid mode `Fixture()` with the dead pid: `Sweep().ShouldBe(1)` at once (unchanged) | S8-tests: `SweepLocked` `:170` consults pid -> reaped at once -> `Count.ShouldBe(1)` gets 0; PC-36, PC-37 | CP-13 red, CP-14 green |
| V-25 | Renew extends a held lease and answers false for an unknown one | Runner | `BuildSlotBrokerTests.Renew_extends_a_held_lease_and_an_unknown_lease_answers_false` | **`Broker.Renew(grant.LeaseId).ShouldBeTrue()`**; `List().Leases.Single().LastRenewedAt.ShouldBe(Time.GetUtcNow().UtcDateTime)` after `Time.Advance(5 s)`; `Renew(Guid.NewGuid()).ShouldBeFalse()`; after `Release`, `Renew(id).ShouldBeFalse()`; TTL still reaps a renewed lease at `LeaseTtlMinutes` | S8-tests: `Renew` stub returns false; PC-38 | CP-13, CP-14 |
| V-25b | Settings validate the holder-liveness mode and grace | Runner | `BuildSlotSettingsTests.Holder_liveness_must_be_pid_or_renew_and_the_grace_positive` | `HolderLiveness = "renew"` validates; `"pids"` -> `Should.Throw<InvalidOperationException>` naming `HolderLiveness`; `RenewGraceSeconds = 0` -> throws; defaults `pid`, 90, 20 | S8-tests: no validation -> no throw | CP-14 |
| V-26 | `POST /build-slots/{id}/renew` is 204 for a held lease and 404 otherwise; the grant carries `renewEverySeconds` only in renew mode | Runner | `BuildSlotEndpointTests.Post_renew_answers_204_for_a_held_lease_and_404_otherwise` | grant on a renew-mode host: body `renewEverySeconds == 20`; **`(await Http.PostAsync($"build-slots/{id}/renew", null)).StatusCode.ShouldBe(NoContent)`**; unknown id -> 404 with `type == BuildSlotProblemTypes.Unknown`; pid-mode host: `renewEverySeconds` is null | S8-tests: route absent -> 404 at `NoContent` | CP-13, CP-14 |
| V-27 | A `BuildSlotsOnly` runner serves `/health` and `/build-slots` and nothing else | Runner (real Program) | `tests/Antiphon.SessionRunner.Tests/BuildSlotsOnlyHostTests.cs` `Build_slots_only_host_serves_health_and_build_slots_and_nothing_else` | `WebApplicationFactory<Program>` with `SessionRunner:BuildSlotsOnly=true`, `PhoneHome:Enabled=false`, `SessionRunner:BuildSlots:Enabled=true`: `GET /build-slots` 200; `GET /health` 200; **`GET /sessions` -> 404**; `GET /capabilities` -> 404; `services.GetServices<IHostedService>().ShouldNotContain(s => s is PhoneHomeConnectionService)` | S8-tests: `Program.cs:228` maps `/sessions` unconditionally -> 200; PC-39. Needs M-9; fallback declared there | CP-13, CP-14 |
| V-28 | The wrapper renews a renew-mode grant while the command runs and never renews a pid-mode grant | Script | `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs` `C589_WrapperRenewsRenewModeGrant` -> `scripts/test-build-slot.ps1` `Test-C589_WrapperRenewsRenewModeGrant` | PASS rows: `renews at least twice before release` (shim log `SLOT RENEW` count >= 2 and all before `SLOT DELETE`), `pid-mode grant is never renewed` (0 `SLOT RENEW`), `renewer stops after release` (no `SLOT RENEW` after `DELETE`), `exit code propagates`; `RunHarnessCaseAsync(..., 4, ...)` -> **`passed.Count.ShouldBe(4)`** | S8-tests: no renewer -> `FAIL ... renews at least twice` -> the wrapper's `lines.ShouldNotContain(FAIL)`; PC-40, PC-41 | CP-15 red, CP-16 green |
| V-29 | Compose contracts: base block list with the broker and network; override changes only the named keys; broker unprivileged and pinned by its own tag | Text | `DockerStackContractTests.Server2_file_defines_runner_state_init_and_broker` (replaces `..._only_runner_and_state_init`), `Temp_override_changes_only_runner_id_restart_broker_url_network_and_grok_store`, `Server2_broker_is_unprivileged_pinned_by_its_own_tag_and_on_the_external_network`; `Server2_services_name_their_images` iterates `state-init, session-runner, build-slots` | block list **`ShouldBe(["state-init", "session-runner", "build-slots", "antiphon-deploy-key", "phone-home", "work", "runner-state", "dind-data", "antiphon-build-slots"])`**; override file's only service is `session-runner` with `PhoneHome__RunnerId: server2-temp`, `restart: "no"`, `ANTIPHON_BUILD_SLOTS_URL: http://build-slots:8080/build-slots`, `networks` listing `antiphon-build-slots`, one volume `${RUNNER_GROK_STORE_DIR:?}` -> `/state/grok`, and **no** `work:`/`runner-state:`/`dind-data:`/`privileged`/`healthcheck`/`PhoneHome__SecretPath`; broker block: no `privileged`, `user: "1654:1654"`, `SessionRunner__BuildSlotsOnly: "true"`, `image:` contains `${BUILD_SLOTS_SHA12`, `restart: unless-stopped`, `profiles` contains `broker`; base `session-runner` `ANTIPHON_BUILD_SLOTS_URL` set and `restart: unless-stopped` kept | S9-tests: today's files -> the block list assertion; PC-42 | CP-17 red, CP-18 green |
| V-30 | The override does not override the secret staging or the healthcheck | Text | `DindRunnerContractTests.Temp_override_keeps_the_base_secret_staging_and_healthcheck` | override `session-runner` block: `ShouldNotContain("healthcheck")`, `ShouldNotContain("PhoneHome__SecretPath")`, `ShouldNotContain("ANTIPHON_PHONE_HOME_SECRET_SOURCE")`; base assertions of `Server2_compose_reads_the_staged_phone_home_secret` and `Server2_healthcheck_covers_phone_home_secret_readability` still hold (unchanged methods) | S9-tests: override file missing -> `File.ReadAllText` throws at the first read (named: the override read is the first statement) | CP-17, CP-18 |
| V-31 | Host-lane cases are routed, bounded and safe | Text | `RemoteScriptContractTests.Rolling_cases_are_routed_and_have_stub_boundaries`, `Temp_deploy_never_removes_orphans_and_resolves_the_grok_store_from_the_volume`, `Deploy_parent_keeps_the_temp_and_broker_tags`, `Retire_temp_runner_requires_the_retired_evidence` | `c590-real.ps1` contains `'deploy-temp-runner'` and `'retire-temp-runner'`; `verify-docker-stack.ps1` has `'deploy-temp-runner' {` with `NestedStoreDiskLow`, `GrokStoreUnresolved`, and `'retire-temp-runner' {` with `TempRunnerNotRetired`; `Remote()` contains `deploy-temp-runner) case_deploy_temp_runner` and `retire-temp-runner) case_retire_temp_runner`; `Block(remote, "case_deploy_temp_runner")` contains `-p antiphon-runner-temp`, `docker volume inspect`, `NestedStoreDiskLow`, `GrokStoreUnresolved`, `stack.temp.env`, and **`ShouldNotContain("--remove-orphans")`**; `Block(remote, "retire_superseded_server2_images")` keeps `BUILD_SLOTS_SHA12` and the temp env sha; `Block(remote, "case_retire_temp_runner")` contains `TempRunnerNotRetired`, `down -v`, `-p antiphon-runner-temp`, and every `down` line names `-p antiphon-runner-temp` | S9-tests: text absent -> `Block` fails "function ... is missing"; PC-43 | CP-17, CP-18 |
| V-32 | `deploy-server2.ps1 -Rolling` drives the phases idempotently and safely | Script (TUnit-wrapped) | `tests/Antiphon.Tests/Scripts/DeployServer2ScriptTests.cs` methods `T1_happy_path_runs_the_phases_in_order`, `T2_a_404_temp_status_is_not_eligible_and_posts_no_drain`, `T3_old_runner_still_busy_exits_2_and_leaves_the_drain`, `T4_drain_temp_rerun_posts_nothing_when_already_draining`, `T5_missing_token_file_exits_2_before_any_case`, `T6_sentinel_never_printed_and_script_is_ascii` -> `scripts/test-deploy-server2.ps1` cases `T1`..`T6` | T1: `trace.jsonl` kinds in order `case:deploy-temp-runner`, `http:POST /api/session-runners/server2/drain`, `case:deploy-parent`, `http:POST .../server2/drain/clear`, `http:POST .../server2-temp/drain`, `case:retire-temp-runner`, exit 0; T2: scripted 404 for `server2-temp/status` -> exit 2, output `TempRunnerNotEligible`, **no `http:POST ... /drain` row**; T3: `sessions: 1` for `-WaitIdleMinutes 0` -> exit 2 `OldRunnerStillBusy`, no `case:deploy-parent`, no `drain/clear`; T4: status already `draining: true` for `server2-temp` -> no POST, waits for `retiredAt`, exit 0; T5: no token file -> exit 2 before any `case:` row; T6: stdout/stderr and `trace.jsonl` never contain the sentinel, `deploy-server2.ps1` and the harness are ASCII | S9-tests: the harness exists, `deploy-server2.ps1` does not -> every case `FAIL deploy-server2.ps1 missing` -> `process.ExitCode.ShouldBe(0, output)`; PC-44, PC-45, PC-46 | CP-19 red, CP-20 green |
| V-33a | `runner-drain.ps1` (R2): ASCII, three verbs, token sent and never printed, 404 is "not eligible" | Text | `tests/Antiphon.Tests/Scripts/RunnerDrainScriptTests.cs` `Script_is_ascii_offers_the_verbs_and_sends_the_token_without_printing_it` | text of `scripts/runner-drain.ps1`: all chars < 128; `ValidateSet(` contains `'status', 'drain', 'clear'`; contains `X-Antiphon-Operator-Token`; **no line matching `Write-Host.*\$token`** or `Write-Output.*\$token`; contains `not eligible` in the 404 branch; `ANTIPHON_OPERATOR_TOKEN_FILE` | S5: file missing -> `File.ReadAllText` throws at the first statement; PC-47 | CP-8 |
| V-33b | `runner-drain.ps1` (R3): `retire` verb with `-Confirm` and `confirmRunnerId` | Text | same method, assertion extended | `ValidateSet` contains `'retire'`; contains `confirmRunnerId`; `-Confirm` required for `retire` (`if ($Verb -eq 'retire' -and -not $Confirm)` text) | S9-tests: the R2 script has no `retire` -> `ShouldContain("'retire'")` | CP-17, CP-18 |
| L-1 | Live: temp runner deploys beside server2 on the new image and takes a real task | Live (desktop) | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase deploy-temp` (main checkout) | evidence root `.antiphon/c727/live/`: `docker-info-old.txt`/`docker-info-temp.txt` daemon names equal each container hostname; `nested-run-temp.txt` exit 0; `bridge-nf.txt` from both; `broker-routes.txt` (`/build-slots` 200, `/sessions` 404 from the broker container); `catalogue.json` (`GET /api/session-runners`) with `server2` and `server2-temp` `dispatchEligible:true, acceptingNewWork:true`; `status-temp.json` `buildVersion == sha`; then one `delegate.ps1 -Runner server2-temp -Worktree` task settles `Succeeded` and `transcript-temp.json` (its session transcript via the ops HTTP surface) holds the `UserPrompt` row carrying the brief | n/a (live) | CP-22 |
| L-2 | Live: shared login stores stay signed in across the overlap | Live | six `GET /api/session-runners/{server2,server2-temp}/provider-auth/{claude,codex,grok}` at L-1 and >= 30 min later | `provider-auth-overlap.json`: six `loggedIn` values unchanged | n/a | CP-23 |
| L-3 | Live: drain, redeploy, drain-temp, retire-temp; no git lock contention | Live | `-Phase drain-old`, `redeploy-old`, `drain-temp`, `retire-temp` on the same run + the log grep | `status-old-idle.json` `sessions == 0 && runnerSessions == 0 && queuedTasks == 0`; `deploy-parent` evidence at the new sha; `status-old-after-clear.json` `acceptingNewWork:true`; `status-temp-retired.json` `retiredAt` set by the job (Hangfire log line `antiphon:runner-retire`); `temp-down.txt`; `docker ps -a` shows no `antiphon-runner-temp` container and the base project's three volumes intact; zero hits for `index.lock`, `packed-refs.lock`, `Mirror fetch failed` in both containers' logs over the window | n/a | CP-24 |

R1 check: V-1, V-2, V-3 are **green pins** on top of CARD-0710; none has a red commit and none
needs one (no R1 production change). Their reds are PC-1 to PC-5, executed by Mutation after
the R1 land. CP-1 and CP-2 are green-only rows.

### Guards the regression

| ID | Regression guarded | Test and decisive assertion | CP |
|---|---|---|---|
| R-1 | 0710's per-id connection, ticket, store, inventory and recovery keep working with the two-entry map used by RollingWorld | `MultiRunnerDirectoryTests` (7), `MultiRunnerRecoveryTests` (7), `MultiRunnerProjectionTests` (4): `Two_connections_keep_distinct_owners` `liveA.ShouldNotBeSameAs(liveB)` | CP-1, CP-12 |
| R-2 | Launch-transport retries and session routing are unchanged by the adapter-factory pin | `PhoneHomeLaunchTransportTests` (7) `peerB.Launches.Count.ShouldBe(1)` in `Loss_before_the_ack_requeues_the_launch_within_the_bound`; `PhoneHomeSessionRoutingTests` (5) | CP-1 |
| R-3 | Connection lifecycle, lease, epoch and CARD-0716 close handshake | `PhoneHomeConnectionTests` (23 existing) `Live_boot_and_store_identity_cannot_be_replaced` (D-5) | CP-1, CP-4, CP-12 |
| R-4 | Catalogue and status shapes for existing readers | `RunnerCatalogueTests` (4), `RunnerSlotEndpointTests` (8), `OperatorShutdownEndpointTests` (3): 403 body `operator_token_required` unchanged for the shutdown route | CP-4, CP-12 |
| R-5 | Create-time placement, pins, reroutes and platform placement are unchanged when no runner is draining | `DefaultRunnerCreateTests` (7) `Eligible_worktree_uses_default` `reason=eligible`; `DefaultRunnerEligibilityTests` (3), `DefaultRunnerPinTests` (6), `DefaultRunnerRerouteTests` (6), `RunnerDefault*` (27: `RunnerDefaultGuidanceTests` 4, `RunnerDefaultMigrationTests`/`RunnerDefaultPlacementTests`/`RunnerDefaultSettingsTests`/`RunnerDefaultsWireTests` 23), `TaskPlatformDispatchTests` (6), `TaskPlatformPlacementTests` (8), `DispatcherRemotePrepStarvationTests` (1 method, 4 results) `remotes.Count.ShouldBe(3)` all Queued | CP-5 |
| R-6 | Inventory, reconciliation and recovery ignore the drain flag | `PhoneHomeReconciliationTests` (4), `SessionReconciliationServiceTests` (57 methods), `PhoneHomePendingInventoryTests` (8), `PhoneHomeRecoveryEligibilityTests` (3), `MultiRunnerRecoveryTests` (7), `PhoneHomeEventPumpTests` (8) | CP-6 |
| R-7 | The runner's other operations, connection loops and overflow handling | `PhoneHomeCommandDispatcherTests` (34) `Unsupported_operation_or_launch_never_enters_runtime` still answers `Error` for `(PhoneHomeOperation)999`; `PhoneHomeConnectionServiceTests` (10) | CP-9, CP-10 |
| R-8 | Pid-mode broker behaviour is byte-for-byte today's | `BuildSlotBrokerTests` (11) `A_dead_holder_is_reaped_on_the_next_acquire`; `BuildSlotEndpointTests` (4); `BuildSlotSettingsTests` (2); wrapper `BuildSlotScriptTests` (7) `C589_WrapperRunsUnderLease` | CP-13 to CP-16 |
| R-9 | Every compose, entrypoint, remote-script and docs contract not touched by this card | `DockerStackContractTests` (105 untouched), `DindRunnerContractTests` (22), `RemoteScriptContractTests` (25), `DockerStackDocumentationTests` (10) | CP-17, CP-18, CP-21 |
| R-10 | Hangfire startup safety (no worker in tests, census/residue jobs) | `HangfireStartupSafetyTests` (12) | CP-11, CP-12 |
| R-11 | The settings validator's existing rules | `PhoneHomeRunnerSettingsValidatorTests` (6) | CP-2 |
| R-12 | Slot release and reconcile through the same operator surface | `RunnerSlotEndpointTests` (8) `(await job.ExecuteAsync(...)).ShouldBe(1)` | CP-4, CP-12 |

### Guard inventory

Safety-critical = a wrong-runner delivery, work admitted on a draining or retired runner, a
premature or wrong retire (kills or strands sessions), a token exposed or not required, a false
eligibility answer, or a deploy step that could remove the wrong project's state. Independently
bypassable checks are split; each maps to one distinct PC.

| G | Plan ref + guard | PC |
|---|---|---|
| G-1 | D-3: session input routes by the session's **own** binding (`RoutingSessionRunnerClient.Route`) | PC-1 |
| G-2 | D-3/CARD-0679 D-6: the adapter's scoped client resolves its **own** runner id (`RunnerScopedSessionRunnerClient.Current`) | PC-2 |
| G-3 | D-3: a launch reaches the task's bound runner (`AgentProtocolAdapterFactory.RemoteClient(runnerId)`) | PC-3 |
| G-4 | D-4/CARD-0729: an unknown id is 404 and never another slot's identity (`Status`) | PC-4 |
| G-5 | D-2: two entries with one secret validate (`ValidateMapped` admits duplicate secrets) | PC-5 |
| G-6 | D-7: `ResolveForNewWork` refuses a draining runner | PC-6 |
| G-7 | D-7: `Resolve` never refuses for draining (sessions keep their transport) | PC-7 |
| G-8 | D-6: the drain row is loaded into a rebuilt directory before anything is recovered (`RunnerStateLoader`) | PC-8 |
| G-9 | D-8: placement selects the redirect only when it is eligible and not draining | PC-9 |
| G-10 | D-8: rebind never moves a SourceLanding task | PC-10 |
| G-11 | D-8: rebind removes the old mirror through the draining runner before clearing the path | PC-11 |
| G-12 | D-8: without an eligible redirect the task holds; nothing launches on the draining runner | PC-12 |
| G-13 | D-11: a retired id cannot register (`Register`) | PC-13 |
| G-14 | D-7: a standing-agent start on a draining runner is refused before the session row | PC-14 |
| G-15 | D-9: `drain` requires the operator token | PC-15 |
| G-16 | D-9: `drain/clear` requires the operator token | PC-16 |
| G-17 | D-14: `retire` requires the operator token | PC-17 |
| G-18 | D-9: `redirectTo` self/desktop/unknown/draining is 409 | PC-18 |
| G-19 | D-10: `acceptingNewWork` is false while draining | PC-19 |
| G-20 | D-10: `dispatchEligible` is unchanged by draining (policy and scripts keep reading a healthy runner) | PC-20 |
| G-21 | D-9: `clear` resets `RetiredAt`/`RetireReason`/`IdleObservedAt`/`RetireWhenIdle`/`RedirectTo` | PC-21 |
| G-22 | D-12: unforced `Retire` refuses while `OwnedSessionCount > 0` | PC-22 |
| G-23 | D-12: forced `Retire` runs `KillAllAsync` before stopping | PC-23 |
| G-24 | D-12: the reply is written before `StopApplication` | PC-24 |
| G-25 | D-13: the job needs two observations >= `RetireIdleSeconds` apart before sending | PC-25 |
| G-26 | D-13: no retire while a non-terminal desktop row is bound | PC-26 |
| G-27 | D-13: no retire while a Queued unlaunched task is bound | PC-27 |
| G-28 | D-13: no retire while the runner lists a non-Exited session | PC-28 |
| G-29 | D-13: a `phone_home_runner_busy` answer clears `IdleObservedAt` | PC-29 |
| G-30 | D-13: `retireWhenIdle:false` is never retired by the job | PC-30 |
| G-31 | D-13: a disconnected runner with bound rows stays draining | PC-31 |
| G-32 | D-14: `confirmRunnerId` must equal the path id | PC-32 |
| G-33 | D-14: forced retire is refused unless draining | PC-33 |
| G-34 | D-14: the forced retire frame carries `force:true` | PC-34 |
| G-35 | D-14: forced retire fails the bound rows with `SystemRequest` and the incident | PC-35 |
| G-36 | D-16: renew mode ignores pid liveness | PC-36 |
| G-37 | D-16: renew mode reaps at `RenewGraceSeconds` | PC-37 |
| G-38 | D-16: `Renew` extends only a held lease | PC-38 |
| G-39 | D-16: a `BuildSlotsOnly` host maps no session or phone-home surface | PC-39 |
| G-40 | D-16: the wrapper never renews a pid-mode grant (desktop behaviour unchanged) | PC-40 |
| G-41 | D-16: the wrapper renews a renew-mode grant while the command runs | PC-41 |
| G-42 | D-15: the temp override is `restart: "no"` (a retired container never restarts itself) | PC-42 |
| G-43 | D-17: `retire-temp-runner` runs `down -v` only for `antiphon-runner-temp` | PC-43 |
| G-44 | D-4/D-17: the wrapper treats a non-200 status as not eligible and posts no drain | PC-44 |
| G-45 | D-17: the wrapper never prints the operator token | PC-45 |
| G-46 | D-17: a busy old runner exits 2 with the drain left in place and no `deploy-parent` | PC-46 |
| G-47 | D-17: `runner-drain.ps1` never prints the token | PC-47 |
| G-48 | D-13: the retire job is registered with its cron | PC-48 |
| G-49 | D-13: `idle_disconnected` is stamped only when the lease has expired (never for a live but momentarily silent runner) | PC-49 |

guards = 49, mapped = 49, missing = 0, duplicate PC maps = 0. Not safety-critical and therefore
without a PC (text pins only): `--remove-orphans` (Compose orphan removal is project-scoped;
V-31 pins it as hygiene), `retire_superseded_server2_images` keep set (image loss is a re-build,
not a state loss; V-31), the `retiring` close description (O-2), doc wording (CP-21).

### Positive controls

Mutation runs each after the round's land, method-scoped
(`--treenode-filter "/*/*/<Class>/<Method>"`), red -> restore -> green, refreshing restored
timestamps; Code runs the V/R rows; Review judges the design before land. `Antiphon.Tests` PCs
run `tests/Antiphon.Tests`; `Antiphon.SessionRunner.Tests` PCs run that project. Batches may
combine PCs that touch different files **and** different methods; PCs sharing a file run alone.

| PC | Break (compiling defect) | Expect red | Method filter (`/*/*/Class/Method`) |
|---|---|---|---|
| PC-1 | `RoutingSessionRunnerClient.Route`: `Remote remote => _directory.Resolve(_directory.KnownRunnerIds[^1])` | V-2 `peerB.Inputs.Count.ShouldBe(0)` (1) | `PhoneHomeRollingRunnerTests/Input_to_a_session_on_server2_reaches_server2_while_a_new_launch_goes_to_server2_temp` |
| PC-2 | `RunnerScopedSessionRunnerClient.Current => _directory.Resolve(_directory.KnownRunnerIds[^1])` | V-2 same assertion | same |
| PC-3 | `AgentProtocolAdapterFactory.Create`: `RemoteClient(_directory, _directory.KnownRunnerIds[1])` | V-2 `peerA.Launches.Count.ShouldBe(0)` (1) | same |
| PC-4 | `PhoneHomeRunnerDirectory.Status`: replace the `:468` throw with `slot = _slots.Values.First();` | V-1 `ShouldBe(404)` gets 200 | `PhoneHomeConnectionTests/Unknown_runner_status_is_404_and_carries_no_live_runner_identity` |
| PC-5 | `ValidateMapped`: add `var secrets = new HashSet<string>();` and `if (!secrets.Add(entry.SharedSecret)) failures.Add("dup secret");` | V-3 `failures.ShouldBeEmpty()` | `PhoneHomeRunnerSettingsValidatorTests/Two_entries_with_the_same_secret_and_host_root_validate` |
| PC-6 | `ResolveForNewWork`: drop the `State.Draining || RetiredAt != null` check (return `Resolve`) | V-9 `Should.Throw<ServiceUnavailableException>` | `PhoneHomeConnectionTests/Draining_changes_neither_dispatch_eligibility_nor_capacity` |
| PC-7 | `Resolve`: add `if (slot.State.Draining) throw new ServiceUnavailableException(..., "phone_home_runner_draining");` | V-5 `peerA.RequestCount(Input).ShouldBe(1)` (throws) | `PhoneHomeRollingRunnerTests/A_draining_runner_still_serves_input_transcript_kill_and_release_for_its_sessions` |
| PC-8 | `RunnerStateLoader`: query `.Where(s => false)` | V-4 rebuilt `ResolveForNewWork` does not throw | `PhoneHomeRollingRunnerTests/Drain_requires_the_operator_token_persists_the_state_and_survives_a_directory_rebuild` |
| PC-9 | `DefaultRunnerRoutingPolicy.Decide`: select `RedirectTo` without `ResolveForNewWork(redirect)` | V-6 arm c `RunnerId.ShouldBeNull()` gets `server2-temp` | `PhoneHomeRollingRunnerTests/Default_placement_follows_the_drain_redirect_and_falls_back_without_one` |
| PC-10 | rebind filter: remove `&& task.SourceLandingOperationId is null` | V-7 arm b `sourced.RunnerId.ShouldBe("server2")` | `PhoneHomeRollingRunnerTests/A_queued_task_bound_to_a_draining_runner_is_rebound_to_the_redirect_before_claim` |
| PC-11 | rebind: skip `RemoveMirrorAsync` (clear the path only) | V-7 `peerA.RequestCount(WorkspaceRemove).ShouldBe(1)` (0) | same |
| PC-12 | `RemoteHoldForAsync`: `return null` when draining and no eligible redirect | V-8 `peerA.Launches.ShouldBeEmpty()` | `PhoneHomeRollingRunnerTests/A_queued_task_on_a_draining_runner_without_an_eligible_redirect_is_held` |
| PC-13 | `Register`: remove the `RetiredAt` refusal | V-11 `ShouldBe(409)` gets 200 | `PhoneHomeConnectionTests/A_retired_runner_id_cannot_register_until_its_drain_is_cleared` |
| PC-14 | `AgentControlService`: remove the `ResolveForNewWork(bound)` gate | V-12 `Should.ThrowAsync<ConflictException>` | `PhoneHomeStandingLaunchTests/Standing_agent_start_on_a_draining_runner_is_refused` |
| PC-15 | drain route: delete `RequireOperator(http, settings.Value)` | V-4 `ShouldBe(403)` gets 200 | as PC-8 |
| PC-16 | clear route: delete `RequireOperator` | V-10 `ShouldBe(403)` | `PhoneHomeRollingRunnerTests/Clear_drain_restores_new_work_and_resets_the_retire_fields` |
| PC-17 | retire route: delete `RequireOperator` | V-20 `ShouldBe(403)` | `PhoneHomeRollingRunnerTests/Forced_retire_requires_the_operator_token_and_the_runner_id_confirmation` |
| PC-18 | `RunnerStateService.ValidateRedirect`: `return;` | V-4b self -> `ShouldBe(409)` gets 200 | `PhoneHomeRollingRunnerTests/Drain_redirect_must_name_another_configured_non_draining_runner` |
| PC-19 | `Status`: `AcceptingNewWork = dispatchEligible` (drop `!Draining`) | V-4 `acceptingNewWork:false` | as PC-8 |
| PC-20 | `Status`: `DispatchEligible = live is { DispatchEligible: true } && available && !slot.State.Draining` | V-9 `dispatchEligible == true` | as PC-6 |
| PC-21 | clear: do not null `RetiredAt` | V-10 `RetiredAt.ShouldBeNull()` | as PC-16 |
| PC-22 | `PhoneHomeCommandDispatcher` Retire: drop `if (!force && _runtime.OwnedSessionCount > 0)` | V-14 `ErrorCode.ShouldBe(RunnerBusy)` | `PhoneHomeCommandDispatcherTests/Retire_refuses_while_sessions_are_owned_unless_forced` |
| PC-23 | Retire: skip `KillAllAsync` when forced | V-14 `Mutations.ShouldContain("kill-all")` | same |
| PC-24 | Retire: call `_lifetime.StopApplication()` before building the reply (no delay) | V-15 `lifetime.Stopped.ShouldBeFalse()` at return | `PhoneHomeCommandDispatcherTests/Retire_reply_is_written_before_the_host_stops` |
| PC-25 | `RunnerRetireJob`: send on the run that first observes idle | V-16 `peerB.Retires.ShouldBeEmpty()` after the +60 s run | `RunnerRetireJobTests/Draining_runner_with_retire_when_idle_is_retired_after_the_idle_window` |
| PC-26 | job: drop the non-terminal-row count | V-17 arm a `Retires.ShouldBeEmpty()` | `RunnerRetireJobTests/Live_session_queued_task_or_busy_answer_keeps_the_runner_draining` |
| PC-27 | job: drop the Queued-task count | V-17 arm b | same |
| PC-28 | job: treat `RunnerInventory.Available` with sessions as idle | V-17 arm c | same |
| PC-29 | job: keep `IdleObservedAt` on a busy answer | V-17 arm d `IdleObservedAt.ShouldBeNull()` | same |
| PC-30 | job: select rows regardless of `RetireWhenIdle` | V-18 `Retires.ShouldBeEmpty()` | `RunnerRetireJobTests/A_drain_without_retire_when_idle_is_never_retired` |
| PC-31 | job: stamp `idle_disconnected` without the bound-row check | V-19 arm b `RetiredAt.ShouldBeNull()` | `RunnerRetireJobTests/Disconnected_draining_runner_with_no_bound_rows_is_marked_retired_without_a_send` |
| PC-32 | retire route: drop the `confirmRunnerId` comparison | V-20 `ShouldBe(400)` gets 200 | as PC-17 |
| PC-33 | retire route: drop the draining check | V-22 `.../retire` on a non-draining id 409 -> 200 | `PhoneHomeRollingRunnerTests/Forced_retire_boundaries_reason_length_confirmation_case_and_unknown_id` |
| PC-34 | `RunnerRetireService.ForceAsync`: send `new RunnerRetireRequest(false, reason)` | V-21 `Force.ShouldBeTrue()` | `PhoneHomeRollingRunnerTests/Forced_retire_fails_bound_sessions_writes_an_incident_and_sends_a_forced_retire` |
| PC-35 | `ForceAsync`: skip failing the bound rows | V-21 `S_B.Status.ShouldBe(Failed)` | same |
| PC-36 | `SweepLocked` renew mode: keep `else if (!_liveness.IsAlive(...))` | V-24 `Leases.Count.ShouldBe(1)` at 10 s (0) | `BuildSlotBrokerTests/Renew_mode_reaps_a_lease_not_renewed_within_the_grace_and_ignores_pid_liveness` |
| PC-37 | `SweepLocked` renew mode: never reap on grace | V-24 `Sweep().ShouldBe(1)` at 91 s (0) | same |
| PC-38 | `Renew`: `return true` for an unknown id | V-25 `Renew(unknown).ShouldBeFalse()` | `BuildSlotBrokerTests/Renew_extends_a_held_lease_and_an_unknown_lease_answers_false` |
| PC-39 | `Program.cs`: map `/sessions` outside the `!buildSlotsOnly` branch | V-27 `/sessions` 200 | `BuildSlotsOnlyHostTests/Build_slots_only_host_serves_health_and_build_slots_and_nothing_else` |
| PC-40 | `scripts/lib/build-slot.ps1`: start the renewer whenever a lease is held (ignore `renewEverySeconds`) | V-28 `pid-mode grant is never renewed` FAIL | `BuildSlotScriptTests/C589_WrapperRenewsRenewModeGrant` |
| PC-41 | wrapper: never start the renewer | V-28 `renews at least twice` FAIL | same |
| PC-42 | `docker-compose.server2-runner.temp.yml`: `restart: unless-stopped` | V-29 `restart: "no"` assertion | `DockerStackContractTests/Temp_override_changes_only_runner_id_restart_broker_url_network_and_grok_store` |
| PC-43 | `case_retire_temp_runner`: `compose_host down -v` without `-p antiphon-runner-temp` | V-31 every `down` line names the temp project | `RemoteScriptContractTests/Retire_temp_runner_requires_the_retired_evidence` |
| PC-44 | `deploy-server2.ps1`: treat a 404 status as eligible | T2 `no drain posted` FAIL | `DeployServer2ScriptTests/T2_a_404_temp_status_is_not_eligible_and_posts_no_drain` |
| PC-45 | wrapper: `Write-Host "token=$token"` in the token helper | T6 sentinel found in output | `DeployServer2ScriptTests/T6_sentinel_never_printed_and_script_is_ascii` |
| PC-46 | wrapper: run `deploy-parent` when the idle wait times out | T3 `no deploy-parent` FAIL | `DeployServer2ScriptTests/T3_old_runner_still_busy_exits_2_and_leaves_the_drain` |
| PC-47 | `runner-drain.ps1`: add `Write-Host $token` after reading the file | V-33a regex assertion | `RunnerDrainScriptTests/Script_is_ascii_offers_the_verbs_and_sends_the_token_without_printing_it` |
| PC-48 | `HangfireConfiguration.AddOrUpdateRunnerRetireJob`: body `{ }` | V-16b `ShouldHaveSingleItem()` | `HangfireStartupSafetyTests/Recurring_retire_job_is_re_added_with_its_cron` |
| PC-49 | job: stamp `idle_disconnected` when `SnapshotLive(id)` is null **or** the lease is live | V-19 arm c `RetiredAt.ShouldBeNull()` | as PC-31 |

Every PC compiles (a text PC edits a script or compose file), each breaks exactly one guard, and
each names the method that goes red at the stated assertion. Zero-test runs, fixture errors and
build failures are not red.

### Stub flags (rows that could not go red as the Plan wrote them)

- Plan V-5 "the S4-tests stub gate refuses `Resolve` too": an artificial red. Re-scoped: Code
  red is the drain route's 404; the guard is PC-7.
- Plan V-16 "Red: class stub": a throwing stub fails before the assertion. Re-scoped: the
  S7-tests stub is a **no-op** returning 0, so V-16/V-19/V-23 go red at their named assertions,
  and V-17/V-18 are declared vacuous-green in Code with their reds in PC-26 to PC-31.
- Plan V-22 duplicated V-20's 409 arm (no independent red). Re-scoped to the boundary set.
- Plan V-33 named four verbs in R2, which cannot be green in R2. Split into V-33a/V-33b.
- Plan V-32 was a non-TUnit row (no `Min`). Now `DeployServer2ScriptTests` (6 methods).
- Plan V-27 needs a real `Program` boot the runner test project cannot do today (M-9).
- Plan CP-5 named `RunnerDefaultTests`, a class that does not exist; the filter is now
  `(RunnerDefault*)` (27 results).
- Plan D-14 "next value after `RunnerSlotForceReleased = 74`" is wrong: `RunnerForceRetired = 77`.
- Plan V-29's expected block list omitted the `networks` child key `antiphon-build-slots`.
- Plan V-7 "one dispatch tick" is one `DispatchCycleAsync` (a remote task needs prep then claim).

### Out of scope

- O-1 A server restart mid-drain end to end: covered by V-4's rebuilt-directory half plus the
  pump's existing recovery classes (CP-6); a full process restart is not scriptable in TUnit.
- O-2 The `retiring` WebSocket close description (D-12): observability only; nothing keys on
  it (the server stamps `RetiredAt` from the result frame). L-3 records the desktop log line.
- O-3 `--remove-orphans` and image keep-set as PCs: project-scoped hygiene, pinned by text (V-31).
- O-4 Codex/Grok token-rotation contention: accepted by the operator; L-2 observes, no test.
- O-5 Two nested dockerd on one kernel: L-1 evidence only; no unit seam.
- O-6 Desktop runner / 17204 rolling restart: the follow-up card.
- O-7 A drain redirect chain (`a -> b -> c`): D-8 is one hop; V-4b refuses a draining target.
- O-8 Reason lengths between 1 and 199 and non-ASCII reasons: the bound is the contract; one
  inside value (the ordinary reason) and both edges (200, 201, empty) are covered.
- O-9 `RetireMinDrainSeconds`/`RetireIdleSeconds` at 0: the validator refuses (D-19 "validated
  when enabled"); the enabled path is not a boundary of the job.

### Checkpoints

One isolated build and one exact filter per row; the union of `Covers` is the whole ordinary
scope (V-1 to V-33b and R-1 to R-12; L rows are live). `Min` = `-MinExecuted` (TUnit executed
results, post-merge class sizes above); `EstimatedMinutes` = wall clock including the build.
Filters use the CARD-0403 combined-class syntax (backslashes before `|` are Markdown escaping).
Results roots `.antiphon/c727/CP-n-<sha>`; outputs `bin-c727-rN/`, deleted after the round.
Red rows require the **named** assertion failures.

#### R1 checkpoints (after CARD-0710 lands; green pins only)

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c727-r1/` | pins-green | `/*/*/(PhoneHomeRollingRunnerTests*)\|(PhoneHomeConnectionTests*)\|(MultiRunnerDirectoryTests*)\|(MultiRunnerRecoveryTests*)\|(MultiRunnerProjectionTests*)\|(RunnerCatalogueTests*)\|(PhoneHomeSessionRoutingTests*)\|(PhoneHomeLaunchTransportTests*)/*` | V-1, V-2, R-1, R-2, R-3, R-4 (catalogue) | all listed, 0 failed/skipped; `-Expect PhoneHomeRollingRunnerTests,PhoneHomeConnectionTests,MultiRunnerDirectoryTests,MultiRunnerRecoveryTests,MultiRunnerProjectionTests,RunnerCatalogueTests,PhoneHomeSessionRoutingTests,PhoneHomeLaunchTransportTests` | 59 | 6 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c727-r1/` | config-docs-green | `/*/*/(PhoneHomeRunnerSettingsValidatorTests*)\|(DockerStackDocumentationTests*)/*` | V-3, R-11, the S2 doc contract | all listed, 0 failed/skipped; 1 new method | 17 | 5 |

R1 floor = 6 + 5 = **11** minutes.

#### R2 checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-3 | S3-tests, S4-tests | `tests/Antiphon.Tests -> bin-c727-r2/` | drain-red | `/*/*/(PhoneHomeRollingRunnerTests*)\|(PhoneHomeConnectionTests*)\|(PhoneHomeStandingLaunchTests*)/*` | V-4, V-4b, V-5 to V-13 | 50 executed; exactly the 11 new methods fail, each at its named assertion; every pre-existing method passes | 50 | 6 |
| CP-4 | S3, S4 | `tests/Antiphon.Tests -> bin-c727-r2/` | drain-green | `/*/*/(PhoneHomeRollingRunnerTests*)\|(PhoneHomeConnectionTests*)\|(PhoneHomeStandingLaunchTests*)\|(RunnerSlotEndpointTests*)\|(OperatorShutdownEndpointTests*)\|(RunnerCatalogueTests*)/*` | V-4 to V-13, R-3, R-4, R-12 | all listed, 0 failed/skipped | 65 | 6 |
| CP-5 | S4 | CP-4 | placement-green | `/*/*/(DefaultRunnerCreateTests*)\|(DefaultRunnerEligibilityTests*)\|(DefaultRunnerPinTests*)\|(DefaultRunnerRerouteTests*)\|(RunnerDefault*)\|(TaskPlatformDispatchTests*)\|(TaskPlatformPlacementTests*)\|(DispatcherRemotePrepStarvationTests*)/*` | R-5 | all listed classes, 0 failed (`RunnerDefault*` expands to 5 classes; the starvation method to 4 results) | 67 | 7 |
| CP-6 | S4 | CP-4 | reconcile-green | `/*/*/(PhoneHomeReconciliationTests*)\|(SessionReconciliationServiceTests*)\|(PhoneHomePendingInventoryTests*)\|(PhoneHomeRecoveryEligibilityTests*)\|(MultiRunnerRecoveryTests*)\|(PhoneHomeEventPumpTests*)/*` | R-6 | all listed classes, 0 failed (57 reconciliation methods expand to more results) | 87 | 6 |
| CP-7 | S3 | n/a | migration-clean | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c727-ef -- dotnet ef migrations has-pending-model-changes --project server` | D-6 | exit 0, "No changes have been made to the model" | n/a | 2 |
| CP-8 | S5 | `tests/Antiphon.Tests -> bin-c727-r2/` | drain-script-green | `/*/*/RunnerDrainScriptTests*/*` | V-33a | 1 executed, 0 failed | 1 | 3 |

R2 floor = 6 + 6 + 7 + 6 + 2 + 3 = **30** minutes.

#### R3 checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-9 | S6-tests | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | retire-op-red | `/*/*/PhoneHomeCommandDispatcherTests*/*` | V-14, V-15 | 36 executed; V-14 fails at `ErrorCode.ShouldBe(RunnerBusy)`, V-15 at `StoppedWithin(1 s)`; 34 pass | 36 | 4 |
| CP-10 | S6 | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | retire-op-green | `/*/*/(PhoneHomeCommandDispatcherTests*)\|(PhoneHomeConnectionServiceTests*)/*` | V-14, V-15, R-7 | all listed, 0 failed/skipped | 46 | 5 |
| CP-11 | S7-tests | `tests/Antiphon.Tests -> bin-c727-r3/` | retire-job-red | `/*/*/(RunnerRetireJobTests*)\|(PhoneHomeRollingRunnerTests*)\|(HangfireStartupSafetyTests*)/*` | V-16, V-16b, V-17 to V-23 | 30 executed; V-16, V-16b, V-19, V-20, V-21, V-22, V-23 fail at their named assertions; V-17 arm d fails; V-18 passes vacuously (declared); every R2 method passes | 30 | 5 |
| CP-12 | S7 | `tests/Antiphon.Tests -> bin-c727-r3/` | retire-job-green | `/*/*/(RunnerRetireJobTests*)\|(PhoneHomeRollingRunnerTests*)\|(HangfireStartupSafetyTests*)\|(RunnerSlotEndpointTests*)\|(PhoneHomeConnectionTests*)\|(MultiRunnerDirectoryTests*)/*` | V-16 to V-23, R-1, R-3, R-10, R-12 | all listed, 0 failed/skipped | 71 | 6 |
| CP-13 | S8-tests | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | broker-red | `/*/*/(BuildSlotBrokerTests*)\|(BuildSlotEndpointTests*)\|(BuildSlotsOnlyHostTests*)/*` | V-24 to V-27 | 19 executed; V-24 to V-27 fail at their named assertions; 15 pass | 19 | 5 |
| CP-14 | S8 | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | broker-green | `/*/*/(BuildSlotBrokerTests*)\|(BuildSlotEndpointTests*)\|(BuildSlotsOnlyHostTests*)\|(BuildSlotSettingsTests*)/*` | V-24 to V-27, V-25b, R-8 | all listed, 0 failed/skipped | 22 | 4 |
| CP-15 | S8-tests | `tests/Antiphon.Tests -> bin-c727-r3/` | wrapper-red | `/*/*/BuildSlotScriptTests*/*` | V-28 | 8 executed; `C589_WrapperRenewsRenewModeGrant` fails at the `FAIL renews at least twice` line; 7 pass | 8 | 4 |
| CP-16 | S8 | `tests/Antiphon.Tests -> bin-c727-r3/` | wrapper-green | `/*/*/BuildSlotScriptTests*/*` | V-28, R-8 | all listed, 0 failed/skipped | 8 | 3 |
| CP-17 | S9-tests | `tests/Antiphon.Tests -> bin-c727-r3/` | compose-red | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)\|(RemoteScriptContractTests*)\|(RunnerDrainScriptTests*)/*` | V-29, V-30, V-31, V-33b | 163 executed; the 3 new/renamed compose methods, `Server2_services_name_their_images`, the Dind method, the 4 remote methods and the drain-script method fail at their text assertions; all others pass | 163 | 5 |
| CP-18 | S9 | `tests/Antiphon.Tests -> bin-c727-r3/` | compose-green | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)\|(RemoteScriptContractTests*)\|(RunnerDrainScriptTests*)\|(DockerStackDocumentationTests*)/*` | V-29 to V-31, V-33b, R-9 | all listed, 0 failed/skipped | 173 | 5 |
| CP-19 | S9-tests | CP-17 | deploy-script-red | `/*/*/DeployServer2ScriptTests*/*` | V-32 | 6 executed, 6 failed at `process.ExitCode.ShouldBe(0)` with `FAIL deploy-server2.ps1 missing` in the output | 6 | 2 |
| CP-20 | S9 | CP-18 | deploy-script-green | `/*/*/DeployServer2ScriptTests*/*` | V-32 | 6 executed, 0 failed | 6 | 3 |
| CP-21 | S10 | `tests/Antiphon.Tests -> bin-c727-r3/` | docs-green | `/*/*/DockerStackDocumentationTests*/*` | D-20 (`Ops_http_doc_names_drain_retire_and_the_404_rule`, `Invariants_doc_names_the_three_rolling_invariants`), R-9 | 12 executed, 0 failed/skipped | 12 | 4 |
| CP-22 | R1 to R3 active on the desktop + S9 landed | n/a | live-deploy-temp | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase deploy-temp` (desktop main checkout) | L-1 | exit 0; the L-1 evidence files incl. `transcript-temp.json` with the `UserPrompt` row | n/a | 25 |
| CP-23 | CP-22 | n/a | live-auth-overlap | the six `provider-auth` reads at L-1 and >= 30 min later | L-2 | `loggedIn` unchanged for claude, codex, grok on both ids | n/a | 35 |
| CP-24 | CP-23 | n/a | live-drain-redeploy-retire | `-Phase drain-old`, `redeploy-old`, `drain-temp`, `retire-temp` on the same run, plus the log grep | L-3 | exit 0 each; `retiredAt` set by the job; `temp-down.txt`; base volumes intact; zero lock hits | n/a | 30 |

R3 ordinary floor = 4 + 5 + 5 + 6 + 5 + 4 + 4 + 3 + 5 + 5 + 2 + 3 + 4 = **55** minutes; the live
rows add about 90 minutes on the desktop lane plus the old runner's drain time and are reported
not run by a server2-placed task.

### Cost

- Ordinary V/R floor (Code) = sum of `EstimatedMinutes`: R1 **11**, R2 **30**, R3 **55** (filters
  and minutes per row above). Estimated. Live rows: **90** desktop minutes plus drain time.
- PC floor (Mutation), estimated on server2 (incremental build ~2.5 min, one method run ~0.5 min,
  red + restore + green = one build and one run each way, ~6 min per batch of independent PCs):
  R1: PC-1..PC-5 touch five files -> 1 batch, **6** min. R2: PC-6..PC-21 (16 PCs) -> shared files
  force separate batches for `PhoneHomeRunnerDirectory.cs` (PC-6, PC-7, PC-13, PC-19, PC-20),
  `AgentTaskDispatcher.cs` (PC-10, PC-11, PC-12), `SessionRunnerEndpoints.cs` (PC-15, PC-16) and
  `RunnerStateService.cs` (PC-18, PC-21) -> 5 batches, **30** min. R3: PC-22..PC-49 (28 PCs) ->
  `PhoneHomeCommandDispatcher.cs` (PC-22, PC-23, PC-24), `RunnerRetireJob.cs` (PC-25..PC-31,
  PC-49), the retire route/service (PC-17, PC-32..PC-35), `BuildSlotBroker.cs` (PC-36..PC-38),
  the wrapper (PC-40, PC-41), plus singletons -> 9 batches, **54** min (12 of them
  `Antiphon.SessionRunner.Tests`). PC total **90** min serial; two shards off the same tip
  (>15 rows, allowed) bring R3 to ~30 wall-clock minutes.
- Total (estimated) = setup/build + V/R + PC: R1 11 + 6 = **17**; R2 30 + 30 = **60**;
  R3 55 + 54 = **109**; live **90**. Savings: batching 49 PCs into 15 batches instead of 49
  serial cycles (49 x 6 = 294 min) saves ~204 minutes; reusing CP-4/CP-17/CP-18 outputs for
  CP-5, CP-6, CP-19, CP-20 saves four builds (~10 min).

Before handoff: bodies read (listed under *Inspection*); guards = 49, mapped = 49, missing = 0,
duplicate PC maps = 0; every PC executable by a method filter; Cost numeric. Next: Code (R1) once
CARD-0710 lands with M-1 satisfied; R2 and R3 Code follow as separate dispatches; Mutation after
each land.
