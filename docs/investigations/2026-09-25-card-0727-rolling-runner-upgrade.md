# CARD-0727 — rolling session-runner upgrade (2026-09-25)

Investigate stage, task b69ddb64. Read-only: no code, config, or database change. Mechanism is reconstructed from the current tree. A second registration was not sent at the live desktop; `PhoneHomeConnectionTests.Live_boot_and_store_identity_cannot_be_replaced` already pins that refusal.

## Verdict

**Confirmed.** One logical runner id cannot have two live instances today. The desktop directory keeps a single phone-home socket. A second process boot is refused while that socket's lease is unexpired (`phone_home_boot_conflict`), and `AcceptConnect` disposes whatever socket it replaces. Every later message, buffer, transcript, stop, and release for that runner id is sent to the socket that is current, not to the process that hosts the session. A List from the replacement is treated as the inventory, and reconciliation then fails database sessions the replacement does not list.

There is no drain and no "not accepting launches" state. `DispatchEligible` means "this one socket has finished catch-up." While it is false, `Resolve` refuses every call, including messages to sessions already running.

Server2 and the desktop need different mechanics. Server2's work lives inside the container, so the old container has to stay up. The desktop runner already adopts detached pty-hosts into the new process before it listens; that moves the sessions and does not meet this card's acceptance (the old instance keeps the work until it is idle).

Not done, noted: no protocol, schema, or deploy change in this task.

## Questions for Investigate

### Can the registry hold two live instances for one runner id? What picks the launch target?

No. `PhoneHomeRunnerDirectory` stores one `PhoneHomeLiveConnection? _live` (`server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:26`). `KnownRunnerIds` is `local` plus the single `PhoneHomeRunner:AllowedRunnerId` (`:58-61`).

`Register` (`:171-220`) admits only `AllowedRunnerId`. While `_liveBootId` differs from the request and the live lease or `_liveLeaseUntil` has not expired, it throws `phone_home_boot_conflict` (`:187-198`). The same boot may register again (reconnect). After the lease expires, a different `RunnerStoreId` still throws `phone_home_store_mismatch` (`:200-202`). `_liveStoreId` is not cleared on disconnect (`Disconnect` at `:286-295` only drops `_live`). The test `Live_boot_and_store_identity_cannot_be_replaced` (`tests/Antiphon.Tests/Agents/PhoneHomeConnectionTests.cs:163-193`) asserts both refusals and that the same boot still gets a ticket. Lease default is 90 seconds (`src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs:19`).

`AcceptConnect` (`:233-256`), if a socket is already `_live`, records `superseded` and disposes it before installing the new one. Disposal closes the socket (`PhoneHomeLiveConnection` around `:520-574`).

Launch placement is that one socket:

- Create-time default routing calls `Resolve(configured)` and falls back to the desktop when the directory says unavailable (`server/Application/Services/DefaultRunnerRoutingPolicy.cs:186-199`). An explicit `-Runner server2` stores that string and is never rewritten (`:169-171`).
- The dispatcher claim gate calls the same `Resolve` (`server/Application/Services/AgentTaskDispatcher.cs:927-934`). Seat pressure counts every non-terminal `AgentSessions` row with that `RunnerId`, plus prepared-but-unlaunched tasks, against `DeclaredCapacity` of the one live socket (`:938-965`, capacity at directory `:318-326`).
- The session row is stamped `RunnerId`, `LiveStoreId`, and `RunnerCwd` together (`AgentTaskDispatcher.cs:4421-4423`). There is no boot id column. The check constraint is all-or-none on those three (`CK_AgentSessions_RunnerBinding_AllOrNone` in the model snapshot).

The runner side identifies itself with one `RunnerId` from config, one `ProcessBootId = Guid.NewGuid()` per process (`src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs:20`, sent at `:87-97`), and a store id loaded or created from `PhoneHome__StoreIdPath` (`PhoneHomeStoreIdentity.cs:5-19`). On server2 that path is `/state/runner-store-id` on the `runner-state` volume (`docker-compose.server2-runner.yml:61`, `:100`).

### Do session ids or the CARD-0679 watermark assume one instance?

Session ids are per launch. The CARD-0679 watermark is also per session id, not per runner generation. `PhoneHomeLaunchGenerationStore` records the newest `AcceptedStartedAt` this process accepted, on disk beside the store id (`launch-generations`, `src/Antiphon.SessionRunner/PhoneHomeSettings.cs:13-26`), and `LaunchAsync` refuses a re-send at or below that watermark (`src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs:344-367`). It also returns the already-running session when the same generation is retried (`:317-341`). Two processes that share that directory share the fence, which is what a re-send needs. Two processes that do not share it can each accept the same generation.

`AcceptedStartedAt` is also what reconciliation calls a generation (`SessionReconciliationService.GenerationMatches`, `:736-737`). It compares one session's start time to the runner row. It does not name which process hosts it.

What does assume one instance is the owner match and the inventory. Inbound events apply only when `RunnerId` and `RunnerStoreId` equal the connection that delivered them (`PhoneHomeRecoveryPump.OwnerMatchesAsync`, `:384-397`). A List replaces that connection's cached inventory and drops ids the List omitted (`PhoneHomeLiveConnection.ReplaceKnownLiveSessions`, `:190-194`). The directory publishes one recovered connection as the remote inventory (`UnknownRemoteSessionIds`, `:379-384`).

### Which actions follow the runner name, and which follow the instance?

Runner name, and then whichever socket is current:

- `RoutingSessionRunnerClient.Route` loads the session binding and, for a remote row, calls `Resolve(owner.RunnerId)` (`RoutingSessionRunnerClient.cs:78-87`). Local rows (null `RunnerId`) always use the single `SessionRunner:BaseUrl` client.
- Adapters hold a `RunnerScopedSessionRunnerClient` that calls `Resolve(RunnerId)` on every call (`RunnerScopedSessionRunnerClient.cs:9-30`), including input, buffer, transcript, kill, release, and kill-generation (`:82-112`). That was the CARD-0679 reconnect fix: follow the replacement. For two overlapping processes it sends the old session's traffic at the new process.
- `GetCapabilities`, `List`, the event stream, and build-slot acquire on that routing client are hard-wired to the local runner (`RoutingSessionRunnerClient.cs:15-28`). Remote list for reconciliation goes through `GetInventoryAsync` (`PhoneHomeRunnerDirectory.cs:149-154`), which is again the one live socket.

Nothing in the route reads `ProcessBootId`.

### What is tied to port 17204?

The desktop production runner is one HTTP listener:

- `Antiphon.AppHost/Program.cs:27-33` launches `Antiphon.SessionRunner.exe --urls http://localhost:17204` and health-checks that port. `:64` sets the server's `SessionRunner__BaseUrl` to the same URL.
- `server/appsettings.json:65-66` is `http://localhost:17204`.
- `scripts/restart-session-runner.ps1` restarts that daemon. `scripts/session-runner-restart-health.ps1:76-78` probes `http://127.0.0.1:17204/health` and names the 17204 owner. `-KillSessions` posts `http://127.0.0.1:17204/sessions/kill-all` (`:103`). `scripts/restart-apphost.ps1` leaves 17204 alone on purpose.
- `scripts/lib/build-slot.ps1:26-29`: Windows default `http://localhost:17204/build-slots`. `ANTIPHON_BUILD_SLOTS_URL` overrides it.
- `verify-dev-stack.ps1` and `scripts/check-daemon-build.ps1` treat 17204 as the runner.

E2E and unit hosts are already fenced off that port and should stay fenced:

- `tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs` starts `IsolatedSessionRunner` on a random port and points the host at `OwnedRunnerUrl`. `tests/Antiphon.E2E/ReleaseGateIsolationTests.cs:53` asserts the owned port is not 17204.
- `tests/Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs:40-66` forces `SessionRunner__BaseUrl` to `http://127.0.0.1:1` for every `Program` boot in that assembly.

Server2 publishes no host port (`docker-compose.server2-runner.yml:135`). Inside the container the runner listens on 8080, which is also the Linux build-slot default.

### Does CARD-0340 restart-resume interact with a drain?

Only on the desktop path, and only as it works today. In `SessionReconciliationService.ScanAsync` (`:116-132`) pass 1c `ResumeInterruptedLaunchesAsync` runs solely for the local inventory (`owner is null`). A remote runner's list is reconciled, and an absent remote session is failed (`:209-247`: "Session runner does not know this session…"), but an interrupted remote launch is not resumed. Pass 1c's comment (`:282-286`) is a `Starting` row the runner still serves and this server process does not own.

A server2 drain that keeps the old socket up does not trip pass 1c. A desktop restart that adopts pty-hosts does: the new process serves the same session ids, and pass 1c can resume `Starting` rows it finds `Running` with a matching generation (`:300-322`). That is the existing restart behaviour, not a drain.

## What already exists, and what is missing

| Piece | Today |
|---|---|
| Second live socket for one runner id | Missing. One `_live`; competing boot refused; successor disposes the predecessor. |
| Drain / not accepting launches | Missing. `DispatchEligible` gates every `Resolve`, including messages (`PhoneHomeRunnerDirectory.cs:79-84`). |
| Per-session instance address | Missing. Binding is runner id + store id + cwd. Routes use runner id only. |
| Launch generation watermark (CARD-0679) | Present, per session id on one process's state directory. It is not a runner generation. |
| Catch-up, inventory, reconciliation | Present for one connection. A replacement List drops sessions it does not contain, and reconciliation fails those rows. |
| Seat count | Present, one number for the runner id (`CountRunnerOccupancyAsync`). Old sessions would fill the new process's declared capacity. |
| Build-slot broker (CARD-0589) | Present, in memory inside one runner process (`BuildSlotBroker.cs:15-23`, singleton at `BuildSlotRoutes.cs:24`). Two processes are two budgets. `Enabled: false` answers unlimited (`BuildSlotBroker.cs:61-64`), so disabling the second broker is the wrong share. |
| Pooled warm processes | Desktop Shared delegates only (`AgentTaskReplyService.cs:2053-2061`). Reuse matches `Agent.RunnerId` (`AgentTaskDispatcher.cs:5991-5994`). Runner-bound tasks must be Worktree (`PhoneHomeLaunchPolicy.cs:125-126`) and are not pooled warm. |
| Server2 deploy | `scripts/c590-remote.sh` `deploy-parent` runs `compose up -d --no-build --remove-orphans` (`:1256`) for the one `session-runner` service. There is no `scripts/deploy-server2.ps1`. |
| Desktop restart | `scripts/restart-session-runner.ps1` stops this checkout's daemon and waits for 17204. The new process adopts surviving pty-hosts before it listens (`src/Antiphon.SessionRunner/Program.cs:180-196`, `SessionRunnerRuntime.cs:1179-1187`). |
| Retire when idle | Missing. Container `restart: unless-stopped` (`docker-compose.server2-runner.yml:46`; asserted at `c590-remote.sh:1347-1350`). If the runner process exits, `dind-entrypoint.sh:172-185` exits and Docker starts that container again. `docker stop` is a host command. Hangfire runs on the desktop and has no server2 docker socket. |

## What blocks two live instances on one host

**Server2, protocol.** The directory will not keep both sockets. Even after a server change, these host facts stay:

- Do not mount one `dind-data` into two running privileged containers. Each runs its own dockerd (`dind-entrypoint.sh:127`). Two daemons on one graph will corrupt it. Separate volumes are required. Each container has its own network namespace (compose does not set `network: host`), so their iptables are not the host's. Two privileged dockerd processes on kernel 4.15 were not started in this investigation; Plan should prove a second container with `docker info` before relying on it.
- Pty manifests are `/tmp/antiphon-pty-hosts` inside the container (`docker-compose.server2-runner.yml:87`). A sibling container does not see them and must not be pointed at them. Cross-container adoption is not available, which is what makes the old container the only place those sessions can finish.
- The `work` volume is one checkout (`/work/repos/antiphon`) plus per-task mirrors (`/work/worktrees/` + task id, `RemoteWorkspaceService`). Sharing it is how the new generation gets the repo. Two writers can lock that git dir. Mirror paths do not collide across tasks.
- Grok, Claude, and Codex homes live on `runner-state` (Codex is a host bind over `/state/codex`). Both generations need those logins. Two processes refreshing the Codex credential file is a residual race; the compose comment already says Codex rewrites that file by rename.
- The store-id file and `launch-generations` should be the same identity, read by both, with a single creator. A fresh store id never replaces the desktop's remembered `_liveStoreId`.

**Desktop.** One process can bind 17204. Local sessions carry no instance id, so the server has one HTTP client for all of them. A second listener is invisible until the server learns a second base URL and stamps it on the session. The new process must use its own manifest directory; sharing `PtyHostManifestDir` makes it adopt the old process's hosts at startup (`AdoptOrphanedHostsAsync`).

**Build slots, both hosts.** The broker is process memory plus a pid liveness check. The memory floor reads host `MemAvailable`, so both would see the same RAM, and each would still grant its own `MaxConcurrent` (server2 is 4, `docker-compose.server2-runner.yml:68`). Agents call loopback: Windows `localhost:17204`, Linux `127.0.0.1:8080` (`scripts/lib/build-slot.ps1:26-29`). A server2 agent runs inside its container, so it hits that container's broker unless `ANTIPHON_BUILD_SLOTS_URL` points elsewhere.

**Warm pool.** Server2 delegated tasks are Worktree and are killed or left for the janitor rather than pooled (`AgentTaskReplyService.cs:2053` requires Shared). Desktop Shared delegates on the old process are reused for any later Shared task with the same null `RunnerId`. A desktop drain has to record which process is accepting, or reuse keeps feeding the old one.

## Proposal for Plan

Smallest design that matches the acceptance (old generation keeps its sessions and still receives messages; new dispatches go to the new generation; the old instance exits when idle). Server2 first. Desktop is a later card.

### Round 1 — server keeps two phone-home sockets (about 4–6 days, one Code card)

Server-only. The runner already sends `ProcessBootId` and retries registration.

- Directory holds a connection per boot id. A second boot with the same store id is accepted while the first lease is live. `AcceptConnect` does not dispose the other boot.
- New session column: the boot id that accepted the launch. `Route` and `RunnerScopedSessionRunnerClient` resolve that boot. Launches, pool decisions, and `Resolve` for new work use only the boot marked accepting.
- Draining is a directory flag on a boot, not `DispatchEligible`. Messages, transcripts, stops, releases, and events stay on the draining boot's pump.
- Reconciliation and the cached inventory are the union of the connections. Absence from the accepting boot's List does not fail a session bound to the other boot.
- Occupancy and `DeclaredCapacity` are per boot. Old sessions do not consume the new process's seats.
- Tests: two scripted peers, one draining; a message to the old session hits the old peer; a new launch hits the new peer; reconciliation leaves the old row running.

### Round 2 — server2 overlap deploy (about 2–3 days)

The entry point is `scripts/c590-remote.sh` `deploy-parent`, not a `deploy-server2.ps1` (that file is not in the tree). Add a second compose service in a new project or a sibling service: own `dind-data`, own `/tmp`, same `PhoneHome__RunnerId=server2`, same store id and `launch-generations` (read the existing file; do not create a second id), shared `work` and credential homes.

Wait until the new boot is `dispatchEligible`, then mark the old boot draining. In that same host step, `docker update --restart=no` on the old container so a later process exit stays down.

Build slots: one broker, reached by `ANTIPHON_BUILD_SLOTS_URL` from both containers (compose DNS). A third long-lived service is the stable choice, because retiring the old runner must not take the budget with it, and `BuildSlots__Enabled=false` is unlimited. Prove two privileged dockerd side by side on server2 before calling this round done.

### Round 3 — retire (about 1–2 days, can sit inside round 2)

Hangfire on the desktop sends a retire on the draining boot once that boot's sessions are gone. The runner exits; the entrypoint exits; restart policy is already `no`, so the container stays stopped. A forced retire is a separate confirmed command and does interrupt live sessions. Status lists each boot and its remaining session count (extend `GET /api/session-runners/{runnerId}/status`, which today has one epoch).

### Round 4 — desktop, later, separate card (about 3–5 days)

Do not block server2 on this. A second Windows process on a port other than 17204, its own manifest directory, and a server-side local binding that records which base URL owns the session. 17204 stays the old process until that process is idle, then the new process takes 17204 or the server's base URL moves. Leave the E2E random-port guard and `ProductionRunnerGuard` on 17204. Warm-pool reuse must refuse the draining process. The existing `restart-session-runner.ps1` adoption path stays the non-rolling restart; it is not this card's acceptance.

`scripts/restart-session-runner.ps1 -Rolling` and a `deploy-server2.ps1 -Rolling` wrapper can be thin callers over rounds 2 and 4. They are not a substitute for the directory change.

## Remaining uncertainties

- Two privileged nested dockerd processes on server2's kernel were not run here.
- Codex (and Grok) credential refresh from two processes on one home directory was not reproduced.
- Git lock contention on the shared server2 checkout during overlap was not measured.
- Production `PhoneHomeRunner` enablement is outside this worktree's `appsettings.json` (`Enabled: false` at `server/appsettings.json:62-64`). The refusal behaviour cited above is the directory code and its test, which run whenever phone-home is enabled.
