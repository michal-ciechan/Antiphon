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

### Coverage roster and decisive assertions

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

## TestDesign pass (task 3895b67b)

Written on the server2 runner against the CARD-0710 repair tip `origin/feat/card-task-7fa08907`
(`b4a91c4e`, repair of task `8752034b`'s `fdf3778a`; `5f39210c` is **not** on `origin/master`
at `a1492bd9`, so R1 stays blocked on 0710 landing). This section pins every roster row to a
file, a checkpoint and a red mechanism, adds the rows the roster was missing, and maps every
safety-critical guard to one positive control. The roster above stays the assertion source; where
a row's assertion changes, the amendment below wins.

### Inspection

Bodies read (0710 repair tip unless noted):

- `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs`: `StartAsync(clock:, connectionString:,
  configured:)` (a `configured` map keeps its own `LeaseSeconds`/`TicketTtlSeconds`; only
  `OperatorTokenPath` and `Limits` are filled in), `RegisterAsync(... runnerId:)` calls
  `EnsureSuccessStatusCode` (a refused registration throws `HttpRequestException`, so V-11 must
  post the raw request), `ConnectPeerAsync(runnerId:, storeId:, secret:)`, `WaitLiveAsync(runnerId:)`,
  `PostOperatorAsync(path, body, token, proxied:)`; the directory is built with an
  `EmptyScopeFactory` when there is no connection string. `PhoneHomeScriptedPeer`: `Launches`
  (Launch and LaunchPlatformConstrained), `Inputs`, `RequestCount(op)`, `Sessions` (List reply),
  `Reply` hook, `SilentFor`, `EmitTranscriptAsync`; the default reply for an unlisted operation
  is `{ ok = true }` (WorkspaceMirror/WorkspaceRemove included).
- `MultiRunnerDirectoryTests.Pair/Entry` (private; `MaxCapacity 4`, `AllowDelegatedTasks` when
  unpinned) — copied, not shared.
- `PhoneHomeLaunchTransportTests.LaunchWorld` (drives `AgentSessionService.LaunchInteractiveAsync`
  directly, not the dispatcher), `TaskPlatformDispatchTests.CreateDispatcher` (real
  `AgentTaskDispatcher` over a `PhoneHomeTestHost` directory with a recording
  `IAgentTaskLaunchSink`), `DispatcherRemotePrepStarvationTests.Configure` (real dispatcher inside
  `BridgeQueueHarness`, `FakeTimeProvider`, `TickAsync`, transcript receipts inserted from the
  adapter's `OnSubmitted`), `DefaultRunnerCreateTests` (placement read back from the saved row and
  its Created/Warning events).
- `server/.../PhoneHomeRunnerDirectory.cs` `Resolve` (:85-98), `ValidateTicket` (:313-322),
  `Status` (:459), `RoutingSessionRunnerClient.Route` (:71-80), `RunnerScopedSessionRunnerClient.Current` (:30).
- On master (unchanged by 0710): `src/Antiphon.SessionRunner/BuildSlotBroker.cs` (already takes
  `IProcessLivenessProbe` and `TimeProvider`; `Sweep`/`SweepLocked`), `scripts/lib/build-slot.ps1`
  (`C589_SLOT_SHIM` replaces `Invoke-AntiphonBuildSlotHttp`), `BuildSlotScriptTests` (7).

Missing setup the Code stage must build (recorded, not assumed):

- **MS-1** `tests/Antiphon.Tests/TestHelpers/RollingRunnerSettings.cs`: `Pair(secretA, secretB,
  leaseSeconds = 90)` for `server2`/`server2-temp` with D-2's values (`MaxCapacity 10`), used by
  `PhoneHomeConnectionTests`, `PhoneHomeRollingRunnerTests` and `RunnerRetireJobTests`.
- **MS-2** `RollingWorld` is `BridgeQueueHarness` (real `SessionMessageQueueService`,
  `AgentSessionLaunchQueue`, `AgentSessionService`) + `DispatcherRemotePrepStarvationTests.Configure`'s
  dispatcher graph + `LaunchWorld`'s real `AgentProtocolAdapterFactory(directory: host.Directory)`
  + `DefaultRunnerKit`'s policy/defaults registrations with the real directory, one
  `FakeTimeProvider` shared by host, harness and (R3) job. If the harness binds a recording
  `IAgentTaskLaunchSink`, `RollingWorld` binds `AgentSessionLaunchQueue` instead: a Launch frame
  must be reached through the real queue, never through the sink.
- **MS-3** Both peers script `Reply` for `WorkspaceMirror` with the result `RemoteWorkspaceService`
  parses (copy the shape from the existing mirror-success test at S1); the `{ ok = true }` default
  is not a mirror result.
- **MS-4** A receipt hook: on a peer `Input` frame for a desktop-bound session, `RollingWorld`
  inserts the matching `UserPrompt` transcript entry (`BridgeQueueHarness.InsertEntryAsync`) with
  the exact submitted text — the substitute declared in the delivery inventory.

Boundaries → rows: two ids equal/distinct secrets → V-3, V-34; configured-offline vs unknown vs
desktop alias → V-1; ticket for id B on id A's route → V-34; drain with/without/ineligible
redirect × default/explicit/SourceLanding → V-6, V-7, V-8, V-35; target idle vs at capacity →
V-7, V-36; crash between rebind and launch → V-37; WorkspaceRemove failure → V-38; retire window
`RetireMinDrainSeconds` 59/60 s and `RetireIdleSeconds` 119/120 s → V-16; busy answer → V-17;
lease 89/90 s → V-19; renew grace 89/90 s → V-24. Excluded: a desktop restart in the middle of
V-23 (one process; covered by V-4's rebuilt directory, see Risks).

### Delivery inventory

Durable identity joins each hop. "Receipt" is the evidence a test reads; a request frame, a queue
insert, an event or an ack is never counted as delivery.

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt | Identity | Tests |
|---|---|---|---|---|---|---|---|
| DP-1 new task on the rolling target | `AgentTaskService.CreateAsync` placement → `AgentTaskDispatcher.TickAsync` claim | `server2-temp` peer Launch → session starts | `AgentTasks.RunnerId` at create; `AgentSessions.RunnerId/RunnerStoreId` at claim | next tick re-claims a Queued row; `LaunchTransport` retries a lost launch (existing) | peer B `Launches` + session row `RunnerStoreId == storeB` + the brief's `UserPrompt` transcript for that session (MS-4) | task id → session id | V-2, V-23 |
| DP-2 message to a session on a draining runner | `SessionMessageQueueService` (real queue) | `server2` peer Input → agent turn | `SessionQueuedMessages` row | queue re-delivers until a `UserPrompt` receipt (existing) | matching complete `UserPrompt` transcript for S_A; queue row delivered | session id + message id | V-2, V-5, V-23 |
| DP-3 drain redirect rebind | `AgentTaskDispatcher` tick (`RemoteHoldForAsync`) | redirect target Launch | `AgentTasks.RunnerId` rewrite + `drain_redirect` event in one `SaveChanges` | crash after the save: the next tick launches on the target without a second rebind or remove (V-37); remove failure: rebind still proceeds (V-38) | target peer `Launches` + session `RunnerStoreId` + brief `UserPrompt` | task id | V-7, V-36, V-37, V-38 |
| DP-4 drain state | `POST .../drain` → `RunnerStateService` | `PhoneHomeRunnerDirectory.ApplyState` gate | `SessionRunnerStates` row (written before `ApplyState`) | `RunnerStateLoader` at startup (V-4) | `ResolveForNewWork` refusal + status `draining:true` from a rebuilt directory | runner id | V-4, V-9, V-10 |
| DP-5 idle retire | `RunnerRetireJob` | runner process exit | `IdleObservedAt`, then `RetiredAt` | a job crash after send and before stamp: the next run finds the lease expired and stamps `idle_disconnected` (V-19) | `RunnerRetireResult` + the runner's `StopApplication` (V-15) + live `temp-down.txt` (L-3) | runner id + `ProcessBootId` | V-14..V-19, V-23, L-3 |
| DP-6 build-slot renew | wrapper renewer (`Start-ThreadJob`) | broker lease | broker memory (`LastRenewedAt`) | a missed renew inside `RenewGraceSeconds` is harmless; outside it the lease is reaped (V-24) | 204 + `LastRenewedAt` moved | lease id | V-24..V-28 |

Substitutes and what they cannot prove: the scripted peers stand in for two real runners (no
pty, no process exit — L-1/L-3 prove those); MS-4's inserted `UserPrompt` stands in for the
runner transcript crossing phone-home (proven by the existing `PhoneHomeEventPumpTests` and live by
L-1's settled task), so DP-1/DP-2 tests prove routing to the right recipient plus the queue's
receipt handling, not the runner's own transcript capture; V-15's recording lifetime cannot prove
the container exits (L-3's `temp-down.txt` does). Busy recipient / already eligible for DP-2:
V-5 delivers to an idle S_A; V-39 queues while S_A is busy (a `TurnEnd` not yet written) and
proves delivery after the turn ends under the drain.

### Proves it works now (pins)

One row per roster ID: file, checkpoint, and the production line whose removal turns it red.
Files are under `tests/Antiphon.Tests/` unless marked `SR` (`tests/Antiphon.SessionRunner.Tests/`).

#### R1 (green pins on top of CARD-0710; red only through the PC rows)

| ID | File :: method | CP | Red mechanism (production line) |
|---|---|---|---|
| V-1 | `Agents/PhoneHomeConnectionTests.cs` :: `Unknown_runner_status_is_404_and_carries_no_live_runner_identity` | CP-1 | `PhoneHomeRunnerDirectory.Status` keyed lookup (:459): PC-2 answers the first live slot → red at `server2-temp` `runnerStoreId.ShouldBeNull()`; PC-3 answers a not-available DTO for an unknown id → red at `StatusCode.ShouldBe(404)` |
| V-2 | `Application/PhoneHomeRollingRunnerTests.cs` :: `Input_to_a_session_on_server2_reaches_server2_while_a_new_launch_goes_to_server2_temp` | CP-1 | `RoutingSessionRunnerClient.Route` `Resolve(remote.Owner.RunnerId)` (:78) → PC-1; `RunnerScopedSessionRunnerClient.Current` `Resolve(RunnerId)` (:30) → PC-4; `Resolve` keyed `SnapshotLive(runnerId)` (:89) → PC-5 |
| V-3 | `Application/PhoneHomeRunnerSettingsValidatorTests.cs` :: `Two_entries_with_the_same_secret_and_host_root_validate` | CP-2 | `PhoneHomeRunnerSettingsValidator.ValidateMapped` admits equal secret values → PC-6 |
| V-34 (new) | `Agents/PhoneHomeConnectionTests.cs` :: `Equal_secrets_do_not_let_a_server2_temp_ticket_connect_as_server2` | CP-1 | `ValidateTicket` `string.Equals(ticket.RunnerId, runnerId)` (:317) → PC-7 |

Amendments (R1):

- **V-1** resolves the D-4/roster conflict in favour of D-2: `server2-temp` is configured
  permanently, so before registration its status is **200** `available:false,
  dispatchEligible:false` with `runnerStoreId`, `processBootId` and `buildVersion` all null
  (CARD-0729's false pass was server2's identity under the temp id); the **404** arm uses an
  unconfigured id (`server2-other`). D-4's first sentence is superseded by this row; the scripts'
  "non-200 is not eligible" rule is unchanged. If the unknown-id arm is not 404 at S1 (the
  `NotFoundException` → `ExceptionMiddleware` mapping), R1 gains a production fix and a red
  commit; say so in the S1 commit.
- **V-2** decisive assertions, in order: (1) placement — `CreateAsync` with the global default
  `server2-temp` saves `RunnerId == "server2-temp"` and a Created event containing
  `selected=server2-temp`; (2) launch through the real queue (MS-2) — ticks (at most 5, each
  followed by a 3 s bounded wait on `peerB.Launches`) until `peerB.Launches.Count == 1`;
  `peerA.Launches.Count == 0`; the session row `RunnerId == "server2-temp"`, `RunnerStoreId ==
  storeB`; (3) message — a queued message for S_A (`RunnerId == "server2"`) through
  `SessionMessageQueueService`: `peerA.Inputs` has one frame whose text equals the message,
  `peerB.Inputs` is empty, and the queue row is delivered on the MS-4 `UserPrompt` receipt; (4)
  `RoutingSessionRunnerClient.SendInputAsync(S_A, "x")` adds one more `peerA` input and none on B.
  The test uses **distinct** secrets; V-34 covers equal ones.
- **V-3** drops the "duplicate id fails" clause (0710's `MultiRunnerDirectoryTests` owns id
  uniqueness; the `Runners` dictionary is case-insensitive, so the arm cannot be built from
  configuration); it asserts zero failures and `KnownRunnerIds` equal to `server2, server2-temp`.
- **V-34** Pair with equal secrets; `RegisterAsync(runnerId: "server2-temp")`; the raw WebSocket
  connect to `/api/session-runners/server2/connect` with that ticket fails (the upgrade is refused;
  `host.Directory.SnapshotLive("server2")` stays null and `SnapshotLive("server2-temp")` stays
  null); a peer then connects to `server2` with its own ticket and becomes live.

#### R2 (red first in `S3-tests`/`S4-tests`, CP-3; green in CP-4..CP-8)

"Setup-red" means the red commit fails before the decisive assertion (the drain route is 404);
such a row is a pin whose decisive red is its PC, not a stub. Rows marked **decisive** fail at the
named assertion in CP-3 because the stub gate/field exists and answers today's behaviour.

| ID | File :: method | CP | Red in CP-3 | Red mechanism (production line) |
|---|---|---|---|---|
| V-4 | `Application/PhoneHomeRollingRunnerTests.cs` :: `Drain_requires_the_operator_token_persists_the_state_and_survives_a_directory_rebuild` | CP-3/4 | setup (404) | `OperatorCredential.Require` in the drain handler → PC-24; `RunnerStateService` save before `ApplyState` → PC-23; `RunnerStateLoader` `ApplyState` per row → PC-22 |
| V-5 | same file :: `A_draining_runner_still_serves_input_transcript_kill_and_release_for_its_sessions` | CP-3/4 | setup (404) | `Resolve` carries no drain check → PC-9 |
| V-6 | same file :: `Default_placement_follows_the_drain_redirect_and_falls_back_without_one` | CP-3/4 | decisive (binds `server2`) | `DefaultRunnerRoutingPolicy.Decide` uses `ResolveForNewWork` → PC-14; `TryKindDefault` gate → PC-15; redirect-eligibility check → PC-13 |
| V-7 | same file :: `A_queued_task_bound_to_a_draining_runner_is_rebound_to_the_redirect_before_claim` | CP-3/4 | setup (404) | rebind in `RemoteHoldForAsync` (`AgentTaskDispatcher.cs:936`): no-session predicate → PC-16; `WorkspaceRemove` call → PC-19; `RemoteWorktreePath` clear → PC-20; prep-backoff reset → PC-21 |
| V-8 | same file :: `A_queued_task_on_a_draining_runner_without_an_eligible_redirect_is_held` | CP-3/4 | setup (404) | claim gate `ResolveForNewWork` in `RemoteHoldForAsync` → PC-10; rebind eligibility → PC-18 |
| V-9 | `Agents/PhoneHomeConnectionTests.cs` :: `Draining_changes_neither_dispatch_eligibility_nor_capacity` | CP-3/4 | decisive (`ResolveForNewWork` default body resolves) | directory gate → PC-8; `DispatchEligible` untouched by `ApplyState` → PC-36; `AcceptingNewWork` formula → PC-32 |
| V-10 | `Application/PhoneHomeRollingRunnerTests.cs` :: `Clear_drain_restores_new_work_and_resets_the_retire_fields` | CP-3/4 | setup (404) | `drain/clear` token → PC-25 |
| V-11 | `Agents/PhoneHomeConnectionTests.cs` :: `A_retired_runner_id_cannot_register_until_its_drain_is_cleared` | CP-3/4 | decisive (ticket issued; state seeded through `ApplyState`) | `Register` retired refusal → PC-31 |
| V-12 | `Application/PhoneHomeStandingLaunchTests.cs` :: `Standing_agent_start_on_a_draining_runner_is_refused` | CP-3/4 | decisive (drain applied through `ApplyState`; launches) | `AgentControlService` standing start gate → PC-12 |
| V-13 | `Application/PhoneHomeRollingRunnerTests.cs` :: `Status_reports_sessions_queued_tasks_and_runner_sessions_per_runner` | CP-3/4 | decisive (fields null) | `Sessions` counts every non-terminal status → PC-33; `QueuedTasks` → PC-34; `RunnerSessions` → PC-35 |
| V-35 (new) | same file :: `A_source_landing_mutation_for_a_draining_runner_is_admitted_held_and_never_moved` | CP-3/4 | setup (404) | SourceLanding exclusion in the rebind → PC-17; `SourceLandingAdmission.RequireSupportAsync` keeps `Resolve` → PC-37 |
| V-36 (new) | same file :: `A_rebound_task_waits_for_capacity_on_the_redirect_target` | CP-3/4 | setup (404) | target `DeclaredCapacity` still applied after a rebind → PC-38 |
| V-37 (new) | same file :: `A_rebind_survives_a_failed_target_mirror_and_a_fresh_dispatcher_without_a_second_rebind` | CP-3/4 | setup (404) | rebind persisted before any target request → PC-40 |
| V-38 (new) | same file :: `A_failed_workspace_remove_on_the_draining_runner_does_not_block_the_rebind` | CP-3/4 | setup (404) | remove failure caught and logged → PC-39 |
| V-39 (new) | same file :: `A_message_queued_while_a_session_on_a_draining_runner_is_busy_is_delivered_after_its_turn` | CP-3/4 | setup (404) | `Resolve` carries no drain check (G-9; second test for PC-9) |
| V-40 (new) | same file :: `Remote_prep_refuses_a_runner_that_began_draining_after_the_claim_check` | CP-3/4 | decisive (mirror requested) | `PrepareRemoteWorkspaceAsync` (`AgentTaskDispatcher.cs:5533`) uses `ResolveForNewWork` → PC-11 |
| V-41 (new) | same file :: `Drain_redirect_validation_refuses_unknown_disabled_desktop_self_and_draining_targets` | CP-3/4 | setup (404) | `RunnerStateService` redirect checks → PC-26..PC-30 |
| V-33a | `Scripts/RunnerDrainScriptTests.cs` :: `Script_is_ascii_offers_the_four_verbs_and_sends_the_token_without_printing_it` (three verbs in R2) | CP-8 | decisive (file missing) | the no-print rule → PC-41 |

Amendments (R2):

- **Seeding a drain without the route.** Rows marked decisive seed the drain with
  `host.Directory.ApplyState(id, new RunnerState(Draining: true, ...))` (the S4-tests stub accepts
  and stores it; the gate is today's), so they fail at behaviour, not at a 404. Route rows (V-4,
  V-10, V-41) must go through the operator route.
- **V-4** adds: the 403 arm posts `proxied: true` and asserts no `SessionRunnerStates` row; the
  rebuilt directory is a second `PhoneHomeRunnerDirectory` over the same schema with
  `RunnerStateLoader.StartAsync` run once.
- **V-5** also reads `peerA.RequestCount(PhoneHomeOperation.List) >= 1` after
  `GetInventoryAsync("server2")`, and the queued-message arm of DP-2: a message to S_A through
  `SessionMessageQueueService` reaches `peerA.Inputs` and is delivered on its MS-4 `UserPrompt`.
- **V-6** arms, each a fresh create read back through a fresh context: (a) global `server2`,
  drained → `server2-temp` (eligible): `RunnerId == "server2-temp"`, Created contains
  `source=default` and `reason=drain_redirect:server2`, no Warning event; (b) kind default
  `Raw → server2`, same drain: `source=kind-default ... reason=drain_redirect:server2`; (c) drained
  with a redirect to an offline `server2-temp`: `RunnerId == null`, `reason=runner_draining`, one
  Warning; (d) drained without redirect: same as (c); (e) explicit `RunnerId: "server2"` create:
  `RunnerId == "server2"`, `RunnerSelectionSource == ExplicitRemote`.
- **V-7** seeds three tasks bound `server2`: T1 Queued default-placed with `RemoteWorktreePath`;
  T2 Queued explicit (`ExplicitRemote`) with no path; T3 Dispatched with a Running session S_A.
  One tick after the drain → `server2-temp`: T1 and T2 `RunnerId == "server2-temp"` and their
  `RunnerSelectionSource` unchanged; T3 `RunnerId == "server2"`; `peerA.RequestCount(WorkspaceRemove)
  == 1` (T1 only); T1 `RemoteWorktreePath` was cleared and then re-prepared on B
  (`peerB.RequestCount(WorkspaceMirror) >= 1`); `peerA.Launches` empty; `peerB.Launches.Count == 2`
  after at most 5 ticks; one event per moved task containing
  `drain_redirect from=server2 to=server2-temp`. T1 is seeded with `RemotePrepFailures = 2` and
  `DispatchNotBeforeAt = now + 10 min`; the launch on B within the 5 ticks (clock not advanced past
  it) is PC-21's decisive assertion.
- **V-8** asserts the Held detail starts with `DispatchHoldDetails.RunnerDraining` and contains
  `server2`, `peerA.Launches` and `peerA.RequestCount(WorkspaceMirror)` are 0, `RunnerId ==
  "server2"`, and exactly one Held event after two ticks (dedup).
- **V-9** keeps the roster assertions and adds `status.acceptingNewWork == false` while
  `dispatchEligible == true`.
- **V-10** adds a 403 arm for `drain/clear` without the token (row still draining).
- **V-11** posts the registration raw (`RegisterAsync` throws on non-2xx) and asserts 409 with
  code `phone_home_runner_retired`; the seeded state has `RetiredAt`.
- **V-13** seeds one Running and one Starting row bound `server2` (`sessions == 2`), one Queued
  unlaunched task (`queuedTasks == 1`), and peer A lists one Running and one Exited session
  (`runnerSessions == 1`: the count of non-`Exited` sessions, which is what `drain-old` waits on).
- **V-35**: create a SourceLanding Mutation with explicit `RunnerId: "server2"` after the drain →
  201 (admitted); one tick: `RunnerId == "server2"`, held `RunnerDraining`, no
  `drain_redirect` event, `peerB` untouched.
- **V-36**: `server2-temp` entry `MaxCapacity 1` and one Running row bound to it; drain `server2`
  → `server2-temp`; tick: T1 rebound (`RunnerId == "server2-temp"`) and held on capacity
  (`peerB.Launches.Count == 0`); the Running row goes terminal; tick: `peerB.Launches.Count == 1`.
- **V-37**: peer B's first `WorkspaceMirror` answers an Error frame; tick 1 rebinds (event 1,
  remove 1) and records the prep failure; a **new** dispatcher scope after the clock passes the
  backoff: launch on B; totals `drain_redirect` events == 1 and `peerA.RequestCount(WorkspaceRemove) == 1`.
- **V-38**: peer A answers `WorkspaceRemove` with an Error frame; tick: `RunnerId ==
  "server2-temp"`, `RemoteWorktreePath` cleared, one Warning log naming the old path, launch on B.
- **V-39**: S_A busy (a `UserPrompt` without `TurnEnd` in the transcript), drain `server2`, queue a
  message for S_A: no `peerA` input while busy; insert `TurnEnd`; the queue delivers:
  `peerA.Inputs` has the text and the MS-4 `UserPrompt` closes the queue row delivered.
- **V-40** uses MS-5 (`DrainAfterClaimCheckDirectory`, a test decorator over `host.Directory`
  that forwards every member and applies the drain to the inner directory right after the
  first `ResolveForNewWork("server2")` call returns): one tick for a Queued task bound `server2`
  without a path and without a redirect → `peerA.RequestCount(WorkspaceMirror) == 0`, task
  `Queued` (not Failed), a Held/Requeued event naming the drain.
- **V-41**: a third entry `server2-off` (`Enabled: false`); `POST .../server2/drain` with
  `redirectTo` = `nope` (unknown), `server2-off` (disabled), `desktop`, `server2` (self), and
  `server2-temp` while `server2-temp` is itself draining → each 409 `phone_home_redirect_invalid`
  and the `server2` row is absent/unchanged; an empty and a 201-character reason → 400.

MS-5 is added to the missing-setup list: `tests/Antiphon.Tests/TestHelpers/DrainAfterClaimCheckDirectory.cs`.

#### R3 (red first in `S6-tests`..`S9-tests`; live rows on the desktop lane)

Seams R3 adds (missing setup, compiled in the red commits):

- **MS-6** `PhoneHomeCommandDispatcher` gains optional `IHostApplicationLifetime? lifetime = null`
  and `TimeProvider? time = null` constructor parameters (the 34 existing constructions compile
  unchanged); the 250 ms stop delay is `time.Delay`-based. Tests pass a `RecordingLifetime`
  (records `StopApplication` calls with the fake-clock instant) and a `FakeTimeProvider`; the
  fake runtime's reply writer records the instant each reply frame is written.
- **MS-7** `RunnerRetireJob` is constructed directly: `(IServiceScopeFactory, ISessionRunnerDirectory,
  IOptions<PhoneHomeRunnerSettings>, TimeProvider, ILogger<RunnerRetireJob>)` with `RunAsync(ct)`;
  `RunnerRetireJobTests` share one `FakeTimeProvider` between `PhoneHomeTestHost.StartAsync(clock:)`
  and the job. **Lease hazard:** `RetireIdleSeconds` (120) exceeds the default `LeaseSeconds` (90),
  so a live-peer arm built with the default lease would take the `idle_disconnected` branch after
  the clock advance; live-peer arms use MS-1 `Pair(..., leaseSeconds: 3600)`, V-19 uses 90.
- **MS-8** `PhoneHomeScriptedPeer` gains `Retires` (request frames of operation 27, decoded
  `RunnerRetireRequest`) and a default `RunnerRetireResult` reply; a test can script
  `phone_home_runner_busy` through `Reply`.
- **MS-9** `BuildSlotBroker` already takes `IProcessLivenessProbe` and `TimeProvider`; S8 adds
  `Renew(leaseId)` and the `HolderLiveness`/`RenewGraceSeconds`/`RenewEverySeconds` settings.
  The wrapper renewer runs in `Start-ThreadJob`, whose runspace does not inherit functions: the
  renewer must import `scripts/lib/build-slot.ps1` (or receive the endpoint and shim path as
  arguments) so `C589_SLOT_SHIM` also answers the renew calls; the shim appends
  `{"method","uri","at"}` lines to its log.

| ID | File :: method | CP | Red in the red CP | Red mechanism (production line) |
|---|---|---|---|---|
| V-14 | SR `PhoneHomeCommandDispatcherTests.cs` :: `Retire_refuses_while_sessions_are_owned_unless_forced` | CP-9/10 | decisive (`phone_home_unsupported_operation`) | busy refusal → PC-42; forced `KillAllAsync` → PC-43 |
| V-15 | same :: `Retire_reply_is_written_before_the_host_stops` | CP-9/10 | decisive (never stopped) | stop scheduled after the reply write → PC-44 |
| V-16 | `Application/RunnerRetireJobTests.cs` :: `Draining_runner_with_retire_when_idle_is_retired_after_the_idle_window` | CP-11/12 | decisive (stub `RunAsync` throws) | min-drain check → PC-50; two-observation window → PC-51 |
| V-17 | same :: `Live_session_queued_task_or_busy_answer_keeps_the_runner_draining` | CP-11/12 | decisive | bound-row check → PC-47; queued check → PC-48; inventory check → PC-49; busy clears `IdleObservedAt` → PC-53; `RetiredAt` only on a result → PC-56 |
| V-18 | same :: `A_drain_without_retire_when_idle_is_never_retired` | CP-11/12 | decisive | `RetireWhenIdle` filter → PC-46; `Draining` filter → PC-45 |
| V-19 | same :: `Disconnected_draining_runner_with_no_bound_rows_is_marked_retired_without_a_send` | CP-11/12 | decisive | lease-expired condition → PC-55; no-bound-rows condition → PC-54 |
| V-20 | `Application/PhoneHomeRollingRunnerTests.cs` :: `Forced_retire_requires_the_operator_token_and_the_runner_id_confirmation` | CP-11/12 | setup (404) | token → PC-57; confirmation → PC-58 |
| V-21 | same :: `Forced_retire_fails_bound_sessions_writes_an_incident_and_sends_a_forced_retire` | CP-11/12 | setup (404) | row failure → PC-60; incident → PC-61 |
| V-22 | same :: `Forced_retire_of_a_runner_that_is_not_draining_is_refused` | CP-11/12 | setup (404) | draining precondition → PC-59 |
| V-23 | same :: `Rolling_upgrade_moves_new_launches_to_server2_temp_keeps_server2_reachable_and_retires_server2_temp_when_idle` | CP-11/12 | decisive at the retire step (job stub) | integration pin; red via PC-51 and PC-9 (it is the brief's two-peer acceptance, not a separate guard) |
| V-24 | SR `BuildSlotBrokerTests.cs` :: `Renew_mode_reaps_a_lease_not_renewed_within_the_grace_and_ignores_pid_liveness` | CP-13/14 | decisive (reaped by pid) | renew mode skips pid liveness → PC-63; grace reap → PC-64 |
| V-25 | same :: `Renew_extends_a_held_lease_and_an_unknown_lease_answers_false` | CP-13/14 | decisive (stub) | `LastRenewedAt` update → PC-65 |
| V-26 | SR `BuildSlotEndpointTests.cs` :: `Post_renew_answers_204_for_a_held_lease_and_404_otherwise` | CP-13/14 | decisive (404 both) | unknown → 404 → PC-67 |
| V-27 | same :: `Build_slots_only_host_serves_health_and_build_slots_and_nothing_else` | CP-13/14 | decisive (`/sessions` 200) | `BuildSlotsOnly` branch → PC-68 |
| V-28 | `Scripts/BuildSlotScriptTests.cs` :: `Wrapper_renews_a_renew_mode_grant_while_the_command_runs` | CP-15/16 | decisive (0 renews) | renew only when asked → PC-69; renewer started → PC-70; renewer stopped before release → PC-71 |
| V-29 | `Infrastructure/DockerStackContractTests.cs` :: the three methods the roster names (+ the three slot-aware updates) | CP-17/18 | decisive (text) | override `restart: "no"` → PC-72; no `runner-state` in the override → PC-77; broker unprivileged → PC-78 |
| V-30 | `Infrastructure/DindRunnerContractTests.cs` :: `Temp_override_keeps_the_base_healthcheck_and_secret_path` (new) + the two named existing methods | CP-17/18 | decisive (override file missing: the test reads it and fails at `File.Exists`) | → PC-80 |
| V-31 | `Scripts/RemoteScriptContractTests.cs` :: the four methods the roster names | CP-17/18 | decisive (text) | no `--remove-orphans` → PC-73; keep set → PC-74; retired evidence → PC-75; `down -v` scoped to the temp project → PC-76; Grok store from `docker volume inspect` → PC-79 |
| V-32 | `scripts/test-deploy-server2.ps1` T-1..T-6 | CP-19/20 | decisive (script missing) | eligibility wait → PC-81, PC-86; busy bound → PC-82; token precondition → PC-84; no-print → PC-85 |
| V-33 | `Scripts/RunnerDrainScriptTests.cs` (retire verb added) | CP-17/18 | decisive (verb absent) | PC-41 (R2) |
| L-1 | `deploy-server2.ps1 -Rolling -Phase deploy-temp` | CP-22 | live | red = nonzero exit or a missing evidence file |
| L-2 | six `provider-auth` reads | CP-23 | live | **cannot go red by any code change**: an observation of the accepted login-contention risk; flagged as a stub in the PC sense, kept as risk evidence, guards none |
| L-3 | `-Phase drain-old` .. `retire-temp` + log grep | CP-24 | live | red = nonzero exit, missing `retiredAt`, or a lock hit |

Amendments (R3):

- **V-15**: at fake `t0` the reply is recorded; `StopApplication` count is 0 at `t0 + 249 ms` and
  1 at `t0 + 250 ms`, and its recorded instant is after the reply's. No wall-clock wait.
- **V-16** boundary arms, one `FakeTimeProvider`: drain at `t0`; run at `t0+59 s` → `IdleObservedAt
  == null`; run at `t0+60 s` → stamped, `peerB.Retires.Count == 0`; run at `+119 s` after the
  stamp → still 0; run at `+120 s` → 1 with `Force == false`, `RetiredAt` set, `RetireReason ==
  "idle"`; a third run at `+240 s` leaves `Retires.Count == 1`.
- **V-17** arms: (a) peer B lists S_B `Running` → no stamp, no send; (b) a `Starting` desktop row
  bound `server2-temp` while peer B lists nothing → no stamp; (c) a Queued unlaunched task bound →
  no send, a log entry naming its id; (d) idle but peer B answers `phone_home_runner_busy` →
  `RetiredAt == null` and `IdleObservedAt == null` after that run.
- **V-18** arms: `server2` draining with `RetireWhenIdle == false` idle for two windows → no send;
  a seeded row with `RetireWhenIdle == true`, `Draining == false` → no send.
- **V-19** arms: peer B aborted with no bound rows, `LeaseSeconds 90`: at `+89 s` after the abort
  `RetiredAt == null`; at `+90 s` `RetireReason == "idle_disconnected"`, `Retires` empty; a second
  world with a bound Running row stays draining past the lease.
- **V-21** adds a second world: peer B aborted (disconnected) with S_B bound → the route answers
  200, S_B `Failed`, `RetireReason` starts with `forced:`, no frame sent.
- **V-23** keeps the roster text and adds, at the drain of `server2`, the DP-2 message arm (a
  queued message to S_A delivered on its MS-4 `UserPrompt` while `server2` drains) and asserts the
  job's first run sends nothing (the two-observation window) before the second run's retire.
- **V-24** boundaries: grant at `t0` with the pid probe answering dead; `t0+10 s` held;
  `Renew` at `t0+60 s`; `t0+149 s` held; `t0+150 s` reaped (grace 90 s after the last renew).
- **V-28** adds: no renew line after the release line in the shim log.
- **V-30** new method (the roster's two existing methods only read the base file and cannot go red
  on the override): the override text contains no `healthcheck:` and no `PhoneHome__SecretPath`.
- **V-32 T-1** also asserts both drain bodies from `trace.jsonl`: `server2` with
  `redirectTo:"server2-temp", retireWhenIdle:false`; `server2-temp` with `redirectTo:"server2",
  retireWhenIdle:true`.
- **V-32 T-2** has two arms: (a) temp status 404 → `TempRunnerNotEligible`, exit 2, no drain in
  `trace.jsonl`; (b) 200 with `acceptingNewWork:false` until the bound → same.

### Guards the regression

- R-1 (all rounds): the regression classes listed under the roster run in the round's green
  checkpoints (CP-1/2, CP-4/5/6, CP-10/12/14/16/18/21); decisive assertion = each class executes
  with 0 failed and the `Min` floor is met.

### Guard inventory

- G-1: D-3/row 4 — a per-session call resolves the session owner's runner, never a default | PC-1
- G-2: D-3 — an adapter/launch client resolves its task's `RunnerId` | PC-4
- G-3: 0710 D-6 — `Resolve(id)` returns that id's socket only | PC-5
- G-4: D-4/CARD-0729 — `Status(id)` reads that id's slot only | PC-2
- G-5: D-4 — an unconfigured id is `NotFound` (404), never a default answer | PC-3
- G-6: D-2 — equal secret values across entries validate | PC-6
- G-7: D-2 — with equal secrets a ticket is bound to the id that registered it | PC-7

R2 (G-n maps to PC-n from here on):

- G-8: D-7 — `ResolveForNewWork` refuses a draining or retired slot | PC-8
- G-9: D-7 — `Resolve` carries no drain check (sessions keep every call) | PC-9
- G-10: D-7 — the dispatcher's claim gate (`RemoteHoldForAsync`) uses `ResolveForNewWork` | PC-10
- G-11: D-7 — remote prep (`PrepareRemoteWorkspaceAsync`) uses `ResolveForNewWork` | PC-11
- G-12: D-7 — a standing runner-bound agent start is refused on a draining runner | PC-12
- G-13: D-8 — placement picks the redirect only when it is eligible and not draining | PC-13
- G-14: D-8 — `Decide` never binds a draining global default | PC-14
- G-15: D-8 — `TryKindDefault` never binds a draining kind default | PC-15
- G-16: D-8 — a task with a session is never rebound | PC-16
- G-17: D-8 — a SourceLanding task is never rebound | PC-17
- G-18: D-8 — a rebind happens only to an eligible redirect (else hold) | PC-18
- G-19: D-8 — the old mirror is removed through the draining runner | PC-19
- G-20: D-8 — the rebind clears `RemoteWorktreePath` so the target prepares its own | PC-20
- G-21: D-8 — the rebind resets `RemotePrepFailures`/`DispatchNotBeforeAt` | PC-21
- G-22: D-6 — `RunnerStateLoader` applies every row at startup | PC-22
- G-23: D-6 — the drain route persists the row (not directory memory only) | PC-23
- G-24: D-9 — `drain` requires the operator token | PC-24
- G-25: D-9 — `drain/clear` requires the operator token | PC-25
- G-26: D-9 — redirect to an unknown id refused | PC-26
- G-27: D-9 — redirect to a disabled entry refused | PC-27
- G-28: D-9 — redirect to the desktop refused | PC-28
- G-29: D-9 — redirect to the runner itself refused | PC-29
- G-30: D-9 — redirect to a draining runner refused | PC-30
- G-31: D-11 — a retired id cannot register until cleared | PC-31
- G-32: D-10 — `AcceptingNewWork = DispatchEligible && !Draining && RetiredAt == null` | PC-32
- G-33: D-10 — `Sessions` counts every non-terminal bound row | PC-33
- G-34: D-10 — `QueuedTasks` counts Queued unlaunched bound tasks | PC-34
- G-35: D-10 — `RunnerSessions` counts the runner's non-`Exited` sessions | PC-35
- G-36: D-7/D-10 — a drain never changes `DispatchEligible` | PC-36
- G-37: D-7 — SourceLanding admission keeps `Resolve` (admitted, then held) | PC-37
- G-38: D-8 — a rebound task still passes the target's capacity gate | PC-38
- G-39: D-8 — a failed `WorkspaceRemove` does not block the rebind | PC-39
- G-40: D-8 — the rebind is persisted before any request to the target | PC-40
- G-41: D-17 — `runner-drain.ps1` never prints the operator token | PC-41

R3:

- G-42: D-12 — the runner refuses a non-forced retire while it owns sessions | PC-42
- G-43: D-12 — a forced retire kills every session first | PC-43
- G-44: D-12 — the host stops only after the reply frame is written | PC-44
- G-45: D-13 — the job considers only `Draining` rows | PC-45
- G-46: D-13 — the job considers only `RetireWhenIdle` rows (server2 is never retired) | PC-46
- G-47: D-13 — idle requires no non-terminal bound session row | PC-47
- G-48: D-13 — idle requires no Queued unlaunched bound task | PC-48
- G-49: D-13 — idle requires runner inventory with no non-`Exited` session | PC-49
- G-50: D-13 — idle requires `now >= DrainedAt + RetireMinDrainSeconds` | PC-50
- G-51: D-13 — the retire is sent only on a second idle observation `RetireIdleSeconds` after the first | PC-51
- G-52: D-13 — a row with `RetiredAt` is never sent a second retire | PC-52
- G-53: D-13 — a `phone_home_runner_busy` answer clears `IdleObservedAt` | PC-53
- G-54: D-13 — `idle_disconnected` requires no bound rows | PC-54
- G-55: D-13 — `idle_disconnected` requires the lease to have expired (a reconnecting runner is not retired, which G-31 would make permanent) | PC-55
- G-56: D-13 — `RetiredAt` is stamped only on a `RunnerRetireResult` | PC-56
- G-57: D-14 — forced retire requires the operator token | PC-57
- G-58: D-14 — forced retire requires `confirmRunnerId` equal to the path id | PC-58
- G-59: D-14 — forced retire requires the runner to be draining | PC-59
- G-60: D-14 — forced retire fails every non-terminal bound row with `SystemRequest` | PC-60
- G-61: D-14 — forced retire writes one `RunnerForceRetired` incident | PC-61
- G-62: D-14 — forced retire stamps `RetiredAt` when the runner is disconnected too | PC-62
- G-63: D-16 — renew mode ignores pid liveness | PC-63
- G-64: D-16 — renew mode reaps a lease not renewed within `RenewGraceSeconds` | PC-64
- G-65: D-16 — `Renew` moves `LastRenewedAt` | PC-65
- G-66: D-16/D-19 — `HolderLiveness` defaults to `pid` (desktop broker unchanged) | PC-66
- G-67: D-16 — renew of an unknown lease is 404 | PC-67
- G-68: D-16 — a `BuildSlotsOnly` host maps no phone-home or session routes | PC-68
- G-69: D-16 — the wrapper renews only when the grant carries `RenewEverySeconds` | PC-69
- G-70: D-16 — the wrapper renews while the command runs | PC-70
- G-71: D-16 — the renewer stops before the release | PC-71
- G-72: D-15 — the temp override sets `restart: "no"` | PC-72
- G-73: D-17 — `deploy-temp-runner` never runs `--remove-orphans` | PC-73
- G-74: D-17 — `deploy-parent` keeps the temp's and the broker's tags | PC-74
- G-75: D-17 — `retire-temp-runner` refuses without `tempRetiredAt` | PC-75
- G-76: D-17 — `down -v` is scoped to `antiphon-runner-temp` | PC-76
- G-77: D-15 — the override never mounts `runner-state` (no shared store identity) | PC-77
- G-78: D-16 — the broker is unprivileged | PC-78
- G-79: D-15 — the Grok store path comes from `docker volume inspect` | PC-79
- G-80: D-15 — the override keeps the base healthcheck and secret path | PC-80
- G-81: D-17 — `deploy-temp` proceeds only on `acceptingNewWork:true` with the new `buildVersion` | PC-81
- G-82: D-17 — `drain-old` never reaches `deploy-parent` while the old runner is busy | PC-82
- G-83: D-13/D-17 — the wrapper drains `server2` with `retireWhenIdle:false` and `server2-temp` with `true` | PC-83
- G-84: D-17 — a missing token file exits before any case runs | PC-84
- G-85: D-17 — the wrapper never prints the token | PC-85
- G-86: D-4 — a 404 status is not eligible in the wrapper | PC-86

Totals: guards = 86, mapped = 86, missing = 0, duplicate PC maps = 0 (G-1..G-86 ↔ PC-1..PC-86;
R1 maps G-2→PC-4, G-3→PC-5, G-4→PC-2, G-5→PC-3, a permutation). Not guards: V-23 (integration
pin over G-9/G-51), L-2 (observation), T-4 (idempotent re-run convenience).

### Positive controls

Mutation runs each PC method-scoped (`--treenode-filter "/*/*/<Class>/<Method>"`): break, red at
the named assertion, restore, green. Zero executed or a build error is not red.

- PC-1: break G-1 by `SessionRunnerBinding.Remote remote => _directory.Resolve(null)` in
  `RoutingSessionRunnerClient.Route`; expect `PhoneHomeRollingRunnerTests.Input_to_a_session_on_server2_reaches_server2_while_a_new_launch_goes_to_server2_temp`
  red at `peerA.Inputs.Count.ShouldBe(...)` (step 4).
- PC-2: break G-4 by making `Status(runnerId)` read the first slot with a live connection
  (`_slots.Values.First(s => s.Live is not null)`) when the requested slot has none; expect
  `PhoneHomeConnectionTests.Unknown_runner_status_is_404_and_carries_no_live_runner_identity`
  red at `server2-temp` `runnerStoreId.ShouldBeNull()`.
- PC-3: break G-5 by returning the desktop not-available DTO instead of `throw new
  NotFoundException(...)` for an unmapped id in `Status`; expect the same method red at
  `StatusCode.ShouldBe(HttpStatusCode.NotFound)`.
- PC-4: break G-2 by `private ISessionRunnerClient Current => _directory.Resolve(_directory.KnownRunnerIds.First(id => id != "desktop"))`
  in `RunnerScopedSessionRunnerClient`; expect V-2's method red at `peerB.Launches.Count.ShouldBe(1)`.
- PC-5: break G-3 by replacing `var live = SnapshotLive(runnerId);` with `var live = SnapshotLive();`
  (the first live slot) in `Resolve`; expect V-2's method red at
  `peerB.Launches.Count.ShouldBe(1)` or `peerA.Inputs` (whichever connected first; the test
  connects A first, so the launch assertion is the decisive one).
- PC-6: break G-6 by adding `if (secrets.Distinct().Count() != secrets.Count) failures.Add("duplicate secret")`
  in `ValidateMapped`; expect `PhoneHomeRunnerSettingsValidatorTests.Two_entries_with_the_same_secret_and_host_root_validate`
  red at `result.Failed.ShouldBeFalse()`.
- PC-7: break G-7 by deleting `|| !string.Equals(ticket.RunnerId, runnerId, StringComparison.Ordinal)`
  from `ValidateTicket`; expect `PhoneHomeConnectionTests.Equal_secrets_do_not_let_a_server2_temp_ticket_connect_as_server2`
  red at `SnapshotLive("server2").ShouldBeNull()`.

R2 (class prefix `PHRR` = `PhoneHomeRollingRunnerTests`, `PHC` = `PhoneHomeConnectionTests`):

- PC-8: `ResolveForNewWork` body → `return Resolve(runnerId);`; `PHC.Draining_changes_neither_dispatch_eligibility_nor_capacity` red at `Should.Throw<ServiceUnavailableException>(() => ResolveForNewWork("server2"))`.
- PC-9: add `if (SlotState(runnerId).Draining) throw new ServiceUnavailableException(..., RunnerDraining);` to `Resolve`; `PHRR.A_draining_runner_still_serves_input_transcript_kill_and_release_for_its_sessions` red at `peerA.RequestCount(Input) >= 1` (the call throws first).
- PC-10: `RemoteHoldForAsync` calls `_directory.Resolve(task.RunnerId)` instead of `ResolveForNewWork`, and the rebind branch is skipped when no redirect; `PHRR.A_queued_task_on_a_draining_runner_without_an_eligible_redirect_is_held` red at `peerA.Launches.ShouldBeEmpty()`.
- PC-11: `PrepareRemoteWorkspaceAsync` calls `Resolve`; `PHRR.Remote_prep_refuses_a_runner_that_began_draining_after_the_claim_check` red at `peerA.RequestCount(WorkspaceMirror).ShouldBe(0)`.
- PC-12: `AgentControlService` standing start calls `Resolve`; `PhoneHomeStandingLaunchTests.Standing_agent_start_on_a_draining_runner_is_refused` red at the 409 status assertion.
- PC-13: drop `&& !target.Draining && target.DispatchEligible` from the redirect choice in `DefaultRunnerRoutingPolicy`; `PHRR.Default_placement_follows_the_drain_redirect_and_falls_back_without_one` red at arm (c) `RunnerId.ShouldBeNull()`.
- PC-14: `Decide` checks the global default with `Resolve`; same method red at arm (a) `RunnerId.ShouldBe("server2-temp")`.
- PC-15: `TryKindDefault` checks with `Resolve`; same method red at arm (b) `RunnerId.ShouldBe("server2-temp")`.
- PC-16: drop `task.AgentSessionId == null` from the rebind predicate; `PHRR.A_queued_task_bound_to_a_draining_runner_is_rebound_to_the_redirect_before_claim` red at T3 `RunnerId.ShouldBe("server2")`.
- PC-17: drop the SourceLanding exclusion from the rebind predicate; `PHRR.A_source_landing_mutation_for_a_draining_runner_is_admitted_held_and_never_moved` red at `RunnerId.ShouldBe("server2")`.
- PC-18: rebind whenever `RedirectTo != null` (no eligibility check); `PHRR.A_queued_task_on_a_draining_runner_without_an_eligible_redirect_is_held` red at the offline-redirect arm `RunnerId.ShouldBe("server2")`.
- PC-19: delete the `WorkspaceRemove` call in the rebind; V-7's method red at `peerA.RequestCount(WorkspaceRemove).ShouldBe(1)`.
- PC-20: delete `task.RemoteWorktreePath = null` in the rebind; V-7's method red at `peerB.RequestCount(WorkspaceMirror).ShouldBeGreaterThanOrEqualTo(1)`.
- PC-21: delete the `RemotePrepFailures`/`DispatchNotBeforeAt` reset; V-7's method red at `peerB.Launches.Count.ShouldBe(2)`.
- PC-22: `RunnerStateLoader.StartAsync` returns without calling `ApplyState`; V-4's method red at the rebuilt directory's `Should.Throw` on `ResolveForNewWork("server2")`.
- PC-23: the drain handler calls `directory.ApplyState` and skips `SaveChangesAsync`; V-4's method red at the `SessionRunnerStates` row read (`ShouldNotBeNull`).
- PC-24: remove `OperatorCredential.Require` from the drain handler; V-4's method red at `StatusCode.ShouldBe(Forbidden)`.
- PC-25: remove it from the `drain/clear` handler; `PHRR.Clear_drain_restores_new_work_and_resets_the_retire_fields` red at the clear-without-token `ShouldBe(Forbidden)`.
- PC-26..PC-30: delete, one at a time, the unknown / disabled / desktop / self / draining-target check in `RunnerStateService`'s redirect validation; `PHRR.Drain_redirect_validation_refuses_unknown_disabled_desktop_self_and_draining_targets` red at that arm's `StatusCode.ShouldBe(Conflict)` (five separate cycles; each arm asserts its own status before the next arm runs, and the test names the arm in the Shouldly message).
- PC-31: delete the `RetiredAt` refusal in `Register`; `PHC.A_retired_runner_id_cannot_register_until_its_drain_is_cleared` red at `StatusCode.ShouldBe(Conflict)`.
- PC-32: `AcceptingNewWork = DispatchEligible`; V-9's method red at `acceptingNewWork.ShouldBeFalse()`.
- PC-33: `Sessions` counts `Status == Running` only; `PHRR.Status_reports_sessions_queued_tasks_and_runner_sessions_per_runner` red at `sessions.ShouldBe(2)`.
- PC-34: `QueuedTasks` returns 0; same method red at `queuedTasks.ShouldBe(1)`.
- PC-35: `RunnerSessions` counts every listed session; same method red at `runnerSessions.ShouldBe(1)`.
- PC-36: `ApplyState` sets the live connection's `DispatchEligible = !state.Draining`; V-9's method red at `dispatchEligible.ShouldBeTrue()`.
- PC-37: `SourceLandingAdmission.RequireSupportAsync` calls `ResolveForNewWork`; V-35's method red at the create `StatusCode.ShouldBe(Created)`.
- PC-38: after a rebind the tick skips `DeclaredCapacity` for that task; `PHRR.A_rebound_task_waits_for_capacity_on_the_redirect_target` red at `peerB.Launches.Count.ShouldBe(0)`.
- PC-39: rethrow the `WorkspaceRemove` failure; `PHRR.A_failed_workspace_remove_on_the_draining_runner_does_not_block_the_rebind` red at `RunnerId.ShouldBe("server2-temp")`.
- PC-40: move the rebind's `SaveChangesAsync` after the target mirror request; `PHRR.A_rebind_survives_a_failed_target_mirror_and_a_fresh_dispatcher_without_a_second_rebind` red at `peerA.RequestCount(WorkspaceRemove).ShouldBe(1)` (the lost rebind repeats).
- PC-41: add `Write-Host "token: $token"` after the token read in `scripts/runner-drain.ps1`; `RunnerDrainScriptTests.Script_is_ascii_offers_the_four_verbs_and_sends_the_token_without_printing_it` red at the no-print assertion.

R3 (`CDT` = SR `PhoneHomeCommandDispatcherTests`, `RRJ` = `RunnerRetireJobTests`, `BSB` = SR
`BuildSlotBrokerTests`, `BSE` = SR `BuildSlotEndpointTests`, `BSS` = `BuildSlotScriptTests`,
`DSC` = `DockerStackContractTests`, `RSC` = `RemoteScriptContractTests`):

- PC-42: drop the `OwnedSessionCount > 0 && !Force` refusal; `CDT.Retire_refuses_while_sessions_are_owned_unless_forced` red at the `phone_home_runner_busy` error-code assertion.
- PC-43: skip `KillAllAsync` on a forced retire; same method red at `runtime.KillAllCalls.ShouldBe(1)`.
- PC-44: call `StopApplication()` before returning the reply (no delay); `CDT.Retire_reply_is_written_before_the_host_stops` red at the `t0 + 249 ms` count `ShouldBe(0)`.
- PC-45: drop `Draining` from the job's row filter; `RRJ.A_drain_without_retire_when_idle_is_never_retired` red at the not-draining arm `Retires.ShouldBeEmpty()`.
- PC-46: drop `RetireWhenIdle` from the filter; same method red at the `server2` arm `Retires.ShouldBeEmpty()`.
- PC-47: drop the non-terminal bound-row check; `RRJ.Live_session_queued_task_or_busy_answer_keeps_the_runner_draining` red at arm (b) `IdleObservedAt.ShouldBeNull()`.
- PC-48: drop the Queued-task check; same method red at arm (c) `Retires.ShouldBeEmpty()`.
- PC-49: drop the inventory check; same method red at arm (a) `IdleObservedAt.ShouldBeNull()`.
- PC-50: drop the `RetireMinDrainSeconds` comparison; `RRJ.Draining_runner_with_retire_when_idle_is_retired_after_the_idle_window` red at the `t0+59 s` `IdleObservedAt.ShouldBeNull()`.
- PC-51: compare `now >= IdleObservedAt` instead of `now >= IdleObservedAt + RetireIdleSeconds`; same method red at the `+119 s` `Retires.Count.ShouldBe(0)`.
- PC-52: drop `RetiredAt == null` from the filter; same method red at the extra third run `Retires.Count.ShouldBe(1)` (V-16 adds that run).
- PC-53: leave `IdleObservedAt` set on a busy answer; V-17's method red at arm (d) `IdleObservedAt.ShouldBeNull()`.
- PC-54: stamp `idle_disconnected` without the bound-row check; `RRJ.Disconnected_draining_runner_with_no_bound_rows_is_marked_retired_without_a_send` red at the bound-row world's `RetiredAt.ShouldBeNull()`.
- PC-55: treat `SnapshotLive(id) == null` as expired; same method red at the `+89 s` `RetiredAt.ShouldBeNull()`.
- PC-56: stamp `RetiredAt` before the send and ignore the answer; V-17's method red at arm (d) `RetiredAt.ShouldBeNull()`.
- PC-57: remove `OperatorCredential.Require` from the retire handler; `PHRR.Forced_retire_requires_the_operator_token_and_the_runner_id_confirmation` red at `ShouldBe(Forbidden)`.
- PC-58: skip the `confirmRunnerId` comparison; same method red at `ShouldBe(BadRequest)`.
- PC-59: skip the draining precondition; `PHRR.Forced_retire_of_a_runner_that_is_not_draining_is_refused` red at `ShouldBe(Conflict)`.
- PC-60: skip failing bound rows; `PHRR.Forced_retire_fails_bound_sessions_writes_an_incident_and_sends_a_forced_retire` red at S_B `Status.ShouldBe(Failed)`.
- PC-61: skip the incident; same method red at the incident count `ShouldBe(1)`.
- PC-62: stamp `RetiredAt` only after a successful send; same method red at the disconnected-world arm `RetireReason.ShouldStartWith("forced:")` (V-21 adds that arm: peer B aborted, route 200).
- PC-63: in renew mode still consult `_liveness`; `BSB.Renew_mode_reaps_a_lease_not_renewed_within_the_grace_and_ignores_pid_liveness` red at the `t0+10 s` held assertion.
- PC-64: never reap in renew mode (TTL only); same method red at the `t0+150 s` reaped assertion.
- PC-65: `Renew` returns true without touching `LastRenewedAt`; `BSB.Renew_extends_a_held_lease_and_an_unknown_lease_answers_false` red at `LastRenewedAt.ShouldBeGreaterThan(before)`.
- PC-66: default `HolderLiveness` to `renew`; existing `BSB.A_dead_holder_is_reaped_on_the_next_acquire` red at its reaped assertion.
- PC-67: answer 204 for any lease id; `BSE.Post_renew_answers_204_for_a_held_lease_and_404_otherwise` red at `ShouldBe(NotFound)`.
- PC-68: map session routes regardless of `BuildSlotsOnly`; `BSE.Build_slots_only_host_serves_health_and_build_slots_and_nothing_else` red at `/sessions` `ShouldBe(NotFound)`.
- PC-69: start the renewer for every grant; `BSS.Wrapper_renews_a_renew_mode_grant_while_the_command_runs` red at the pid-mode arm renew count `0`.
- PC-70: never start the renewer; same method red at renew count `>= 2`.
- PC-71: do not stop the thread job before `Exit-AntiphonBuildSlot`; same method red at "no renew after release" (the command shim sleeps 1 s after the release point is recorded, so a leaked renewer logs one).
- PC-72: override `restart: unless-stopped`; `DSC.Temp_override_changes_only_runner_id_restart_broker_url_network_and_grok_store` red at the `restart: "no"` assertion.
- PC-73: add `--remove-orphans` to the temp `up`; `RSC.Temp_deploy_never_removes_orphans_and_resolves_the_grok_store_from_the_volume` red at `ShouldNotContain("--remove-orphans")`.
- PC-74: drop the temp sha from the keep set; `RSC.Deploy_parent_keeps_the_temp_and_broker_tags` red at its keep-set assertion.
- PC-75: drop the `TempRunnerNotRetired` refusal; `RSC.Retire_temp_runner_requires_the_retired_evidence` red at `ShouldContain("TempRunnerNotRetired")`.
- PC-76: `down -v` without `-p antiphon-runner-temp`; same method red at the project-scoped `down -v` assertion.
- PC-77: add `runner-state:/state` to the override's volumes; `DSC.Temp_override_changes_only_...` red at "no `runner-state` mount".
- PC-78: add `privileged: true` to `build-slots`; `DSC.Server2_broker_is_unprivileged_pinned_by_its_own_tag_and_on_the_external_network` red at the no-`privileged` assertion.
- PC-79: hard-code `RUNNER_GROK_STORE_DIR=/var/lib/docker/volumes/antiphon-runner_runner-state/_data/grok`; `RSC.Temp_deploy_never_removes_orphans_...` red at `ShouldContain("docker volume inspect")`.
- PC-80: add a `healthcheck:` block to the override; `DindRunnerContractTests.Temp_override_keeps_the_base_healthcheck_and_secret_path` red at `ShouldNotContain("healthcheck:")`.
- PC-81: accept the temp on any 200; `test-deploy-server2.ps1` T-2 arm (b) FAIL (a drain appears in `trace.jsonl`).
- PC-82: go on to `deploy-parent` when `-WaitIdleMinutes` elapses; T-3 FAIL (`deploy-parent` in the trace).
- PC-83: post `retireWhenIdle: true` for `server2`; T-1 FAIL at the drain-body assertion (T-1 asserts both drain bodies).
- PC-84: read the token lazily at the first POST; T-5 FAIL (`deploy-temp-runner` in the trace).
- PC-85: `Write-Output` the token in the verbose line; T-6 FAIL (sentinel found in output).
- PC-86: treat a 404 status as eligible; T-2 arm (a) FAIL.

Non-TUnit PCs (81..86) run the whole `test-deploy-server2.ps1` (one command, ~1 min) and read the
named case's `FAIL`; every other PC is one method-scoped TUnit filter.

### Out of scope

- A desktop restart in the middle of the rolling flow: V-23 is one process; the restart half is
  V-4's rebuilt directory plus the pump's existing recovery coverage (Risks).
- The runner's own transcript capture crossing phone-home: MS-4 substitutes an inserted
  `UserPrompt`; the real path is `PhoneHomeEventPumpTests` (regression) and L-1's settled task.
- Two nested dockerd daemons on kernel 4.15, Grok/Codex token rotation: live only (L-1, L-2); no
  deterministic test exists for either.
- Hangfire scheduling of `antiphon:runner-retire`: the job runs through its class; that Hangfire
  fires it is L-3's `retiredAt` "set by the job".
- Migration content beyond `has-pending-model-changes` (CP-7); the row round-trip is V-4.
- The desktop runner on 17204 (follow-up card).

Plan rows flagged by this pass: **L-2 cannot go red** (observation, not a guard); the roster's
V-30 methods could not go red on the override (a new method added); V-3's duplicate-id clause
could not be built from configuration (dropped); V-5's red-commit mechanism ("the stub refuses
`Resolve` too") was contrived (replaced by setup-red + PC-9); D-4's "server2-temp status is 404"
contradicts D-2 (resolved in V-1). No other row is a stub: every V row has a PC or is an
integration pin over named PCs (V-23).

### TestDesign cost

- Ordinary V/R floor (Code) = the checkpoint `EstimatedMinutes`: R1 CP-1..CP-2 = 6 + 5 = **11**;
  R2 CP-3..CP-8 = 7 + 7 + 7 + 6 + 2 + 3 = **32**; R3 CP-9..CP-21 = **53**; ordinary total **96**
  minutes (estimated), plus the live rows CP-22..CP-24 = 25 + 35 + 30 = **90** minutes on the
  desktop lane (estimated; plus the old runner's drain time).
- PC floor (Mutation, estimated): 86 PCs = 71 in `Antiphon.Tests`, 9 in
  `Antiphon.SessionRunner.Tests`, 6 in `test-deploy-server2.ps1`. One unbatched TUnit cycle on
  server2 ≈ 3 min incremental build + 1 min red run + 3 min restore build + 1 min green run = 8
  min → 80 × 8 + 6 × 3 = **658** min unbatched. Batched by the rule (independent files and
  methods only): the largest same-file groups are `AgentTaskDispatcher.cs` (11: PC-10, 11, 16..21,
  38..40) and `RunnerRetireJob.cs`/`RunnerRetireService.cs` (12: PC-45..PC-56), so `Antiphon.Tests`
  needs about 20 batches × 10 min = 200 min; SessionRunner 4 batches × 8 = 32 min; scripts 6 × 3 =
  18 min; setup builds 2 × 6 = 12 min → **262** min (estimated), saving about **396** min. With
  three shards (same branch tip, detached worktrees; `Antiphon.Tests` shards need per-test schema
  isolation) the wall clock is about **95** min.
- Total (estimated) = Code 96 + live 90 + Mutation 262 = **448** agent-minutes.

Before handoff: bodies read (listed under Inspection); guards = 86, mapped = 86, missing = 0,
duplicate PC maps = 0; every PC names a compiling defect, one method and one assertion (PC-81..86
name a script case); numeric cost above; R1 is green pins only (no production change) with red
proven by PC-1..PC-7, and R1 Code stays blocked until CARD-0710's `5f39210c` is on master.

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
tables: R1 **11** minutes, R2 **32** minutes (TestDesign), R3 **53** minutes (plus L-1..L-3, about 60 minutes of
desktop wall clock dominated by the 30-minute overlap wait and however long the old runner's last
task takes). Suggested `-ExpectAbout`: R1 200 minutes, R2 480 minutes, R3 760 minutes.

### Checkpoints

The closed lists for Code, one table per round. `Sn-tests` means the slice's compiling red tests
are committed before its production change; `Sn` means the production change is committed.
Filters use the CARD-0403 combined-class syntax; the backslashes before `|` are Markdown escaping
only. Red rows require the named assertion failures, not any failure.

#### R1 checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c727-r1/` | pins-green | `/*/*/(PhoneHomeRollingRunnerTests*)\|(PhoneHomeConnectionTests*)\|(MultiRunnerDirectoryTests*)\|(MultiRunnerRecoveryTests*)\|(MultiRunnerProjectionTests*)\|(RunnerCatalogueTests*)\|(PhoneHomeSessionRoutingTests*)\|(PhoneHomeLaunchTransportTests*)/*` | V-1, V-2, V-34; multi-runner, routing and launch regressions | all listed classes, 0 failed; 3 new methods, 0 skipped | 56 | 6 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c727-r1/` | config-docs-green | `/*/*/(PhoneHomeRunnerSettingsValidatorTests*)\|(DockerStackDocumentationTests*)/*` | V-3; docs contract | all listed, 0 failed/skipped; 1 new method | 17 | 5 |

R1 floor = 6 + 5 = **11** minutes.

#### R2 checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-3 | S3-tests, S4-tests | `tests/Antiphon.Tests -> bin-c727-r2/` | drain-red | `/*/*/(PhoneHomeRollingRunnerTests*)\|(PhoneHomeConnectionTests*)\|(PhoneHomeStandingLaunchTests*)/*` | V-4 to V-13, V-35 to V-41 | 17 new + existing executed; all 17 new fail: V-6, V-9, V-11, V-12, V-13, V-40 at their decisive assertions (drain seeded through the `ApplyState` stub), the other 11 at the drain/clear route (404, setup-red pins); existing methods pass | 53 | 7 |
| CP-4 | S3, S4 | `tests/Antiphon.Tests -> bin-c727-r2/` | drain-green | `/*/*/(PhoneHomeRollingRunnerTests*)\|(PhoneHomeConnectionTests*)\|(PhoneHomeStandingLaunchTests*)\|(RunnerSlotEndpointTests*)\|(OperatorShutdownEndpointTests*)\|(RunnerCatalogueTests*)/*` | V-4 to V-13, V-35 to V-41; operator-route, catalogue and connection regressions | all listed, 0 failed/skipped; 17 new methods | 68 | 7 |
| CP-5 | S4 | CP-4 | placement-green | `/*/*/(DefaultRunnerCreateTests*)\|(DefaultRunnerEligibilityTests*)\|(DefaultRunnerPinTests*)\|(DefaultRunnerRerouteTests*)\|(RunnerDefaultTests*)\|(TaskPlatformDispatchTests*)\|(TaskPlatformPlacementTests*)\|(DispatcherRemotePrepStarvationTests*)/*` | D-7, D-8 regressions on placement and dispatch | all listed classes, 0 failed (the starvation method expands to 4 results) | 63 | 7 |
| CP-6 | S4 | CP-4 | reconcile-green | `/*/*/(PhoneHomeReconciliationTests*)\|(SessionReconciliationServiceTests*)\|(PhoneHomePendingInventoryTests*)\|(PhoneHomeRecoveryEligibilityTests*)\|(MultiRunnerRecoveryTests*)\|(PhoneHomeEventPumpTests*)/*` | D-7 regressions on inventory and recovery | all listed classes, 0 failed (57 reconciliation methods expand to more results) | 87 | 6 |
| CP-7 | S3 | n/a | migration-clean | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c727-ef -- dotnet ef migrations has-pending-model-changes --project server` | D-6 | exit 0, "No changes have been made to the model" | n/a | 2 |
| CP-8 | S5 | `tests/Antiphon.Tests -> bin-c727-r2/` | drain-script-green | `/*/*/RunnerDrainScriptTests*/*` | V-33 (three verbs) | 1 executed, 0 failed | 1 | 3 |

R2 floor = 7 + 7 + 7 + 6 + 2 + 3 = **32** minutes.

#### R3 checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-9 | S6-tests | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | retire-op-red | `/*/*/PhoneHomeCommandDispatcherTests*/*` | V-14, V-15 | 2 new + 34 existing executed; V-14, V-15 fail at the unsupported-operation answer | 36 | 4 |
| CP-10 | S6 | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | retire-op-green | `/*/*/(PhoneHomeCommandDispatcherTests*)\|(PhoneHomeConnectionServiceTests*)/*` | V-14, V-15; dispatcher and connection regressions | all listed, 0 failed/skipped | 46 | 5 |
| CP-11 | S7-tests | `tests/Antiphon.Tests -> bin-c727-r3/` | retire-job-red | `/*/*/(RunnerRetireJobTests*)\|(PhoneHomeRollingRunnerTests*)/*` | V-16 to V-23 | 8 new + existing executed; V-16..V-19, V-23 fail at their decisive assertions (job stub), V-20..V-22 at the retire route (404, setup-red); existing pass | 23 | 5 |
| CP-12 | S7 | `tests/Antiphon.Tests -> bin-c727-r3/` | retire-job-green | `/*/*/(RunnerRetireJobTests*)\|(PhoneHomeRollingRunnerTests*)\|(RunnerSlotEndpointTests*)\|(PhoneHomeConnectionTests*)\|(MultiRunnerDirectoryTests*)/*` | V-16 to V-23; slot, connection and directory regressions | all listed, 0 failed; 8 new methods, 0 skipped | 61 | 6 |
| CP-13 | S8-tests | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | broker-red | `/*/*/(BuildSlotBrokerTests*)\|(BuildSlotEndpointTests*)/*` | V-24 to V-27 | 4 new + 15 existing executed; V-24 to V-27 fail at their reap/renew/route/mode assertions | 19 | 5 |
| CP-14 | S8 | `tests/Antiphon.SessionRunner.Tests -> bin-c727-r3s/` | broker-green | `/*/*/(BuildSlotBrokerTests*)\|(BuildSlotEndpointTests*)\|(BuildSlotSettingsTests*)/*` | V-24 to V-27; broker regressions | all listed, 0 failed/skipped | 19 | 4 |
| CP-15 | S8-tests | `tests/Antiphon.Tests -> bin-c727-r3/` | wrapper-red | `/*/*/BuildSlotScriptTests*/*` | V-28 | 1 new + 7 existing executed; V-28 fails at the renew count | 8 | 4 |
| CP-16 | S8 | `tests/Antiphon.Tests -> bin-c727-r3/` | wrapper-green | `/*/*/BuildSlotScriptTests*/*` | V-28; wrapper regressions | all listed, 0 failed/skipped | 8 | 3 |
| CP-17 | S9-tests | `tests/Antiphon.Tests -> bin-c727-r3/` | compose-red | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)\|(RemoteScriptContractTests*)\|(RunnerDrainScriptTests*)/*` | V-29 to V-31, V-33 | new and updated methods (incl. the new V-30 method) fail at their text assertions; unrelated methods pass | 163 | 5 |
| CP-18 | S9 | `tests/Antiphon.Tests -> bin-c727-r3/` | compose-green | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)\|(RemoteScriptContractTests*)\|(RunnerDrainScriptTests*)\|(DockerStackDocumentationTests*)/*` | V-29 to V-31, V-33; compose, entrypoint, script and docs contracts | all listed, 0 failed/skipped | 173 | 5 |
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
