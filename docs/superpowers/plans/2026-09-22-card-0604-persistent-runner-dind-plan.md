# CARD-0604: persistent server2 runner with a nested Docker daemon, phoning home to the production board

Date: 2026-09-22. Stage: Plan v2 (revision of `f6b7f015`, task `3b9af4e6`), with TestDesign folded
(D-11). Plan v2 task `3414bfc4`. Source baseline: `e70f7e0d` (master; CARD-0594's three launch fixes
landed and confirmed live; `origin/master` has advanced only by the investigation commit `5ace6799`).
Investigation: [2026-09-22-card-0604-persistent-runner-dind-rework.md](../../investigations/2026-09-22-card-0604-persistent-runner-dind-rework.md).
Supersedes: `docs/docker-stack.md` paragraphs 2-3, CARD-0590 plan decisions D-6, D-13 (the "server2
lacks that supported producer" clause) and the `linux-custody`/`testing-payload` fences, all re-aimed
in S6/S12, not deleted. CARD-0598 is folded into this card (D-12, D-19); its closure is stated in
§"CARD-0598 disposition".

**Operator decisions applied in v2** (task `3414bfc4` brief, 2026-09-22):

- **D-2 changed**: the control plane is the desktop's production Antiphon board. Server2 tasks are
  dispatched from and visible on the board the operator already watches. Server2 keeps no standing
  server or Postgres; the only server + Postgres on server2 is the nested throwaway child a session
  creates. The CARD-0490 phone-home wiring is reused and widened (D-2, D-14, D-15).
- **D-8 confirmed** as planned (repo-scoped write deploy key generated and held only on server2,
  Compose file secret, 0400/uid 1654 on tmpfs, baked `/etc/gitconfig` and pinned ssh config over
  `ssh.github.com:443`).
- **D-9 confirmed** as planned (`deploy-parent` retires the `c590*` leftovers with an inventory).
- **New scope**: SourceLanding Mutation runs local-inherited inside the server2 session runner with a
  Linux custody backend that meets CARD-0598's acceptance list (D-12, D-17, D-18, D-19). The
  operator's reasoning, preserved verbatim: "the custody rule's actual concern is process lineage and
  attestation ('local inherited execution... never give snapshot access to an external executor,
  broker, remote service or pre-existing process'), not which physical/virtual host runs it -- a
  session-runner container doing its own isolated checkout/build/test satisfies that shape regardless
  of host, PROVIDED the same hashed-evidence/exact-SHA/restoration guarantees are actually built and
  verified there."

**Next: decide** (D-13 lists the defaults; D-14, D-16 and D-17 are the ones that change money or
production state), then Code directly: the verification design and both checkpoint tables are in
this document. The work is two Code cuts (D-14); Cut B cannot start before Cut A is landed and
confirmed live on server2.

## Outcome and scope

1. A **persistent** session-runner container on server2 (fixed Compose project, restart policy,
   sha-tagged image, owned named volumes) that runs its **own `dockerd`** inside itself (privileged,
   operator decision 2, D-1) and **phones home to the desktop production server** over the Caddy
   front door (D-2). It is the only standing Antiphon process on server2.
2. A session launched in that runner **from the production board** runs the ordinary test entry
   point, which stands up a **nested throwaway Antiphon stack** (server + runner + Postgres) plus the
   Testcontainers fixtures on the nested daemon, exports evidence, and tears down only what it
   created. Mapped ports land on the runner's own loopback, so Testcontainers works unmodified (D-5).
3. **Delegated Worktree tasks** (`delegate.ps1 -Runner server2`) run on that runner as Grok
   sessions in a **mirror worktree** synced through GitHub, settle on the production board, and land
   through the unchanged desktop land protocol (D-15).
4. A durable, repo-scoped GitHub push credential for the unattended runner (D-8, confirmed).
5. Engine packaging beyond the tarball the Dockerfile already fetches (D-4), measured against
   server2's kernel 4.15 / cgroup v1 / legacy-iptables host, and on Docker Desktop (cgroup v2).
6. **Cut B**: a Linux custody backend (`linux-cgroup-v1`, D-17) so a SourceLanding Mutation task
   dispatched with `-Runner server2` is created, launched, observed, receipted, restored and cleaned
   with the same hashed-evidence / exact-SHA / descendant-zero guarantees as the Windows
   `windows-job-v1` path (D-12, D-18, D-19). This absorbs CARD-0598.

Out of scope: the CARD-0590 Medium roster beyond the one database-fixture shard that proves the
Testcontainers path (the roster keeps its CARD-0590 ownership and now has a working lane),
CARD-0590's receipt/recovery cut cases (S6c/S6d), Claude Code or Codex CLIs in the image (Grok is
the only agent kind the runner admits, D-16), the `RequireLinux()` classes other than the custody
and launcher classes this plan runs in-runner (CARD-0605 keeps the roster question), sysbox/rootless
(investigation Options 3 and 4), a fleet scheduler or a second phone-home runner (CARD-0490 D-1
stands: one allowed runner id, now `server2`), Shared-workspace tasks on server2 (refused, D-15),
`PtyHost` launcher changes (CARD-0594 is landed; CP-7 only re-runs its live confirmation), and any
Docker-sibling custody (CARD-0598's boundary and CARD-0590 D-14 stand: the nested daemon never
executes, reads or builds a sourced snapshot, D-18).

No build, test, container or push of product code ran in this Plan. Server2 was read on 2026-09-22
(read-only `docker ps`/`volume ls`/`info`/`df`, `iptables --version`, `uname -r`, `tailscale ip`,
three `curl` probes and one `ssh -T` handshake against GitHub); nothing on server2 was changed. The
production server was probed read-only (`GET /api/version`, `GET /api/session-runners/x/status`,
one `POST /api/session-runners/register` that returned `phone_home_disabled`); nothing was changed.

## Ground truth

| Card / brief assumption | What the code and the hosts say at `e70f7e0d` (measured 2026-09-22) | Consequence |
|---|---|---|
| CARD-0594 is landed, so sessions launch in the Linux runner. | The three fixes are on master (`6c525b62`, `ad6cf8bd`, `27233eca`). The stack still up on server2 (`c5903537de6b7be4-*`) was built before those commits; no image on server2 contains the fix and `parent-native-and-command-session` has not been re-run since. | CP-5 rebuilds from HEAD; CP-7 is the first live re-run of the Raw session and the CARD-0594 confirmation on server2, before any nested work. |
| "Phone-home wiring CARD-0490 built can be reused for this runner." | It exists and is narrower than the brief assumes. `PhoneHomeLaunchPolicy.RefuseUnsupportedStart` (`server/Application/Services/PhoneHomeLaunchPolicy.cs:24-57`) refuses card starts, delegated tasks, worktrees, SourceLanding and OnAgent for the pinned agent; `Project` (`:70-90`) nulls `VerificationBinding`, forces `Cwd = /work` and exe `grok`; `PhoneHomeRunnerSettings` pins one `AllowedRunnerId` (`grok-linux`) and one `StandingAgentId`; `PhoneHomeRunnerDirectory` holds one live connection and `Register` refuses `Capacity != 1`; the runner's `PhoneHomeSettings.Validate` throws unless `Capacity == 1`; `PhoneHomeCommandDispatcher.RejectUnsupportedLaunch` refuses any exe but `grok`, any cwd but `AllowedCwd`, and any `VerificationBinding`; `PhoneHomeOperation` has 13 members and none for custody or workspace. `AgentTaskDispatcher` builds task sessions with no runner fields (`:3663-3679`); only `AgentControlService` (`:648-650`) sets `RunnerId/RunnerStoreId/RunnerCwd`, and only for the pinned agent. | D-2/D-14/D-15 widen the policy from "one pinned cardless Grok agent" to "one runner-bound pool": runner-bound named agents (Grok or Raw) and runner-bound delegated Worktree tasks. Capacity becomes a bounded setting. `Project` keeps `VerificationBinding` for Cut B. |
| The production board can dispatch to a phone-home runner today. | Production has `PhoneHomeRunner:Enabled=false`: `POST /api/session-runners/register` through `https://antiphon.desktop.codeperf.net` returned 409 `phone_home_disabled`; `GET /api/session-runners/grok-linux/status` reports no store, no boot, `available:false`. CARD-0490's V-7 ran against an isolated server, never production. | S7 adds the production enablement step (user-secrets: `Enabled`, `AllowedRunnerId=server2`, `AllowDelegatedTasks`, `SharedSecret`, `CallbackOrigin`; then `restart-apphost.ps1` from the main checkout) as CP-6a, an operator-visible production change. |
| Server2 can reach the desktop server. | Server2 is on the tailnet (`100.93.77.126`). `http://100.79.51.37:17202/api/version` from server2: connection refused (curl `000`; Windows firewall or Kestrel binding). `https://antiphon.desktop.codeperf.net/api/version` from server2: 200, sha `e70f7e0d`, `land-v2`. A WebSocket-upgrade `GET /api/session-runners/x/connect` through Caddy reached Kestrel (409 invalid ticket), so Caddy passes the upgrade. | D-2: `PhoneHome__ServerOrigin` and `CallbackOrigin` are `https://antiphon.desktop.codeperf.net`; `wss` follows from the scheme in `PhoneHomeConnectionService.RunConnectionAsync`. No firewall or Caddy change. The in-runner (nested netns, NAT) path is measured in CP-5. |
| The registration store id is the custody store id. | No. Registration sends `PhoneHomeStoreIdentity.LoadOrCreate(StoreIdPath)` (`/state/runner-store-id`) and `Capabilities: null` (`PhoneHomeConnectionService.cs:79-93`). `SourceLandingAdmission.RequireSupportAsync` reads `capabilities.RunnerStoreId` = `SessionRunnerRuntime.RunnerStoreId` = `VerificationCustodyStore.StoreId` under `SessionLogPath/verification-custody` (`SessionRunnerRuntime.cs:48,104`). Two identities. | D-17: on a custody-capable runner the registration's `RunnerStoreId` **is** the custody store id (one identity, anchored by the custody store's `.identity.json`); `StoreIdPath` is only used when custody is absent. Registration carries the capabilities DTO. |
| `SourceLandingAdmission` checks the runner that will run the task. | It checks the DI-local `ISessionRunnerClient` and pins `windows-job-v1` (`SourceLandingAdmission.cs:52-60`). `VerificationExecutionService.ReserveAsync` (`:27`) binds `RunnerStoreId` from that same local read; `VerificationCleanupService` reads custody through the local client (`:16,58`). The backend literal is pinned in six places: `VerificationCustody.cs:12`, `SourceLandingAdmission.cs:56`, `VerificationExecutionService.cs:71`, `VerificationCustodyStore.cs:239`, `WorktreeRemovalEvidence.cs:73`, `RunnerCustodyLedger.PrepareStart` (Windows + ModernConPty only). Receipt validation accepts only `JobObjectBasicAccountingInformation` / `sealed-before-native-start-intent` (`VerificationCustodyStore.cs:206-214`, `VerificationReceiptPolicy.cs:22-27`). | D-19: a `VerificationCustodyBackends` set replaces the six literals; admission, reservation and cleanup resolve the task's runner through `ISessionRunnerDirectory`; receipts are validated against the binding's backend. Nothing is relaxed: a binding, capability or receipt naming a backend the runner does not own is refused. |
| The Linux pty-host could track a launch if the runner let it. | `PtyAgentRunner.LaunchCoreAsync` spawns `ModernConPtyConnection` (Windows) or `PortaPtySession` (`:163-165`); the custody journal is only threaded into the ConPTY path; `IPtyCustodyNative` is Win32 job accounting; `HostSession.GetHelloAck` advertises `verificationCustodyV1` only for ModernConPty (`:295-300`); `HostCustodyJournal` writes `ObservationMethod = "JobObjectBasicAccountingInformation"`. `RunnerCustodyLedger.PrepareStart` writes `unsupported.json` and refuses on non-Windows. | D-17: a Linux containment seam (`IPtyCustodyContainment`: Windows job today, Linux cgroup new) behind the same journal, with observation method `CgroupProcsEmpty`; the ledger and hello gate on the runner's probe, not on the OS name. |
| Managed creation and cleanup can be pointed at server2. | Both are desktop-filesystem operations: `DelegationWorktreeService.CreateForTaskAsync` -> `WorktreeManager.CreateVerificationAsync` (`server/Infrastructure/Git/WorktreeManager.cs`), `ValidateVerificationAsync` (`DelegationWorktreeService.cs:363-408`) runs desktop git; `VerificationCleanupService.CleanupAsync` uses `IWorktreeManager.TryRemoveAsync`; `WorktreeRemovalEvidence.ReadVerificationAsync` reads `<common>/antiphon/verification/<O>/<task>/restoration.json` from the desktop. `task.WorktreePath` has about 190 consumer sites across 20 server files (`AgentTaskDispatcher` 25, `TaskWorktreeRetirementService` 20, `AgentTaskLandingProtocol` 14, `AgentTaskLandService` 14, ...). | D-15: ordinary remote tasks keep a **desktop** worktree as the canonical record and run in a runner **mirror** synced through origin, so none of the 190 sites change. D-19: only Mutation tasks use runner-managed creation, through a bounded seam in the eight custody files. |
| Brief and prompt spill files reach a remote session. | `SessionMessageQueueService.SpillQueueBodyAsync` writes through `TypedBodySpill` with `File.WriteAllText` under the session's `Cwd` (`TypedBodySpill.cs:52`), i.e. the desktop path. A remote session's cwd is the runner mirror. | S8: for a remote session the spill is shipped inside the `Input` operation and written by the runner into `RunnerCwd`; the desktop never writes it (G-21). |
| `docker-27.5.1.tgz` at `docker/session-runner-grok/Dockerfile:64` is already downloaded. | Yes, but `tar ... --strip-components=1 docker/docker` extracts only the CLI; the same tarball carries `dockerd containerd containerd-shim-runc-v2 runc docker-init docker-proxy ctr`. The image has no `iptables`, `iproute2`, `openssh-client`, `sudo`, no `setpriv` use, no `daemon.json`. It installs Grok 1.0.40 only (no Claude, no Codex). | D-4: extract the whole engine from the same pinned tarball; add `iptables` (legacy pinned), `iproute2`, `openssh-client`; Cut B adds `sudo` and two root-owned helpers (D-17). No second engine source. |
| server2 can run privileged DinD today. | Measured with `docker:27-dind`: Docker 24.0.2, `CgroupVersion=1`, `overlay2`, runtimes `runc` only, kernel `4.15.0-213-generic`, host `iptables v1.6.1` legacy. Debian 12's `iptables` defaults to nft. `ssh -p 443 git@ssh.github.com` from server2's host completes the host-key exchange. | D-4 pins `iptables-legacy` at build; the entrypoint self-checks before `dockerd`. cgroup v1 on server2, v2 on Docker Desktop: D-3 step 3 and D-17 branch on `/sys/fs/cgroup/cgroup.controllers`. |
| The sibling shape's Testcontainers path is broken by a localhost/netns split. | Confirmed: no `TESTCONTAINERS_*` anywhere; `TestDbOperations.StartAsync` takes `GetConnectionString()` verbatim; `run_dotnet` ran tests in a separate container with the host socket. Never exercised (no `dotnet-filter`/`messaging-tests` evidence). | D-5: no override variable; the daemon shares the test process's netns by construction; V-6 locally, V-18 on server2. |
| A session in today's runner can already run `test-docker.ps1 -ThrowawayStack`. | It cannot: `c590-remote.sh` `ensure_dirs` uses `sudo -n`, `write_result` uses `python3`, `ensure_checkout` clones a hard-coded branch; the image has neither `sudo` nor `python3`. | S3 makes the script lane-aware; the nested lane drops `sudo`/`python3` (the Cut B `sudo` is for the custody helpers only and is never used by the nested lane). |
| "There is no persistent standing runner." | A runner container is up but persistence is absent by design (no `RestartPolicy`, per-run project `c590<run>`, per-run tags, no unit). Live: `c5903537de6b7be4-{antiphon,session-runner,postgres}-1`, seven volumes, eight images (about 9 GB). | S2/S4: fixed project `antiphon-runner` with only the runner and `state-init`; `restart: unless-stopped`; sha-tagged image plus OCI revision label; D-9 retires the leftovers. |
| The git-credential smoke ports to the nested daemon. | It does not (nested bind sources resolve in the runner's filesystem; a missing source becomes an empty directory). The token is desktop-sourced per run. | D-8 (confirmed): key on server2, Compose file secret, root entrypoint materialises it for uid 1654; smoke runs in the session's own shell. |
| Only `gho_` tokens are scrubbed from logs. | `scrub_file` redacts `gho_[A-Za-z0-9_]+` and `POSTGRES_PASSWORD=` only. | S3 widens the pattern (G-14). |
| The runner is non-root and CARD-0590 D-1 rejects supervisord. | `USER 1654:1654` closes every stage; base Compose `user: "1654:1654"`; `Applications_share_nonroot_identity` reads base Compose only. `dockerd` needs root. | D-3: the `session-testing` stage ends `USER 0:0` with a fail-together entrypoint (root `dockerd`, runner as 1654 via `setpriv`); base file stays 1654. |
| Disk on server2 is 56 GB free. | 55 GB free (`/` 213 GB, 149 GB used). The nested store will hold sdk:10, aspnet:9, node:22, postgres:16, redpanda, ryuk and the child images: 8-10 GB. | D-9: `dind-data` named volume; nested prune permitted; host prune forbidden; `c590*` leftovers retired first. |
| Compose on server2 supports what this plan uses. | Compose v2.18.1: `privileged`, `secrets` `file:`, `tmpfs`, `stop_grace_period`, `restart`, `--remove-orphans`. Docker 24 supports `host-gateway`. | No Compose upgrade. |
| SourceLanding custody is untouched (v1 D-12). | v1 fences: `case_testing_payload` asserts `! grep -a -q VerificationCustodyV1 /app/Antiphon.SessionRunner.dll`; `verify-docker-stack.ps1` `linux-custody` refuses an advertised `VerificationCustodyV1`; `docs/docker-stack.md` says "SourceLanding Mutation stays on the Windows local path (CARD-0598)". CARD-0598 (Backlog) names the acceptance: "real Linux method-scoped native evidence for admission and launch refusal/success; root-exit with live descendants; original observer/store restart or loss; generation/identity mismatch; irreversible seal; exact persisted and imported receipts; drained output and zero descendants; complete attempt-set and guarded cleanup", and the boundary "Fresh Docker containers started by a standing daemon are NOT automatically local inherited execution ... removing Windows checks or reporting a fabricated windows-job-v1 backend is forbidden". | v2 D-12 replaces v1 D-12: the fences are **re-aimed** (S12), not kept: the persistent runner advertises `verificationCustodyV1` with backend `linux-cgroup-v1`, never `windows-job-v1`; the `runtime` and `receipt-probe` targets keep the negative assertion (G-40). CARD-0598's acceptance list is carried as R-8 to R-15 and V-28 to V-33. |
| "Nested throwaway Antiphon stack (server + Postgres)". | The base `docker-compose.yml` defines server, runner and Postgres; CARD-0590's child cases build `child-server` and `child-runner`. `DockerStackContractTests` asserts base Compose has `PhoneHomeRunner__Enabled: false` and `PhoneHome__Enabled: false` (`:94,98`). | D-7: the nested child is the unmodified base file (three services) on the nested daemon; the child runner is the socket-free `runtime` target. The persistent runner's `PhoneHome__Enabled: "true"` lives in the standalone server2 file (S2), so both base assertions hold. |
| Mutation PCs need Postgres or Docker. | Every PC in this plan is a text/script/unit test in `Antiphon.Tests` needing no Postgres; the Mutation battery needs git, the .NET SDK and pwsh only. | D-18: a tracked (Mutation) session has no path to the nested daemon at all (socket group separation), which is the enforced form of "the snapshot never reaches Docker". |

## Decisions

### D-1. `dockerd` runs inside the persistent runner container, privileged, root-then-drop

Operator decision 2, recorded: the `session-testing` runner service is `privileged: true`, starts
as root, launches `dockerd` on `unix:///var/run/docker.sock` inside its own mount and network
namespaces, then runs `Antiphon.SessionRunner` as uid/gid 1654 with the socket group set to a
dedicated `docker-nested` group (gid 1656, D-18). Everything a session starts through Docker
(Testcontainers Postgres/Redpanda/Ryuk, the nested child stack, nested builds) lives on that
daemon; the host daemon never sees it.

Rejected (already measured or rejected by the investigation and the operator): a `docker:27-dind`
sidecar over TCP; sysbox (kernel 4.15); rootless `dockerd` (cgroup v1 plus kernel 4.15); keeping
the host socket (the shape the card rejects, and the source of the netns split).

### D-2. The control plane is the desktop production server; server2 runs only the runner

**Changed by the operator (v1 rejected this).** The persistent Compose project on server2 is
`antiphon-runner`: the `session-testing` runner service and `state-init`, nothing else. The runner
registers with the production server at `https://antiphon.desktop.codeperf.net` (measured reachable
from server2; the direct Tailscale `:17202` path is refused) using the CARD-0490 protocol:
`POST /api/session-runners/register` with the shared secret, then the outbound `wss`
`/api/session-runners/server2/connect`. Sessions are launched by the production server through
that socket; their `AgentSession` rows, agents and tasks are ordinary production rows, visible on
the board and in the session pages with the runner binding in diagnostics (CARD-0490 D-11).

Widening, in order of who may launch (D-14 carries the code shape):

- **Runner-bound named agents** (operator-created, `runnerId: server2`, kind Grok or Raw): the
  CARD-0490 pinned-agent lane generalised. Raw is admitted only with an exe from the image-owned
  allow-list (`/bin/sh`, `/bin/bash`, `/usr/local/bin/pwsh`); this is what the server2 acceptance
  cases use (CARD-0590's acceptance session was Raw `/bin/sh`).
- **Runner-bound delegated Worktree tasks** (`delegate.ps1 -Runner server2`, kind Grok): pool
  delegates created by the dispatcher with `Agent.RunnerId = server2`, running in a mirror worktree
  (D-15). Card-backed starts, OnAgent, Shared, ReadOnly and pinned processes stay refused.
- **SourceLanding Mutation tasks** with `-Runner server2`: Cut B only (D-19).

The throwaway stack the card describes is the **nested** one a session creates (D-7). Server2's own
server + Postgres from v1 are gone: the operator's reading that they were "only needed as the nested
throwaway stack's target" is correct, and the nested child already carries them.

Rejected: a server2-local server + Postgres (v1 D-2; two boards to watch, and the operator wants
one); the Tailscale direct origin (refused by the desktop, measured); a Caddy vhost or firewall
change for an isolated acceptance server (outward-facing state change; the acceptance runs against
production instead, under one dedicated production project, D-13); a second allowed runner id
(CARD-0490 D-1's no-fleet rule stands; `AllowedRunnerId` becomes `server2` in production, and the
`grok-linux` canary keeps working against its own isolated server through
`verify-phone-home-grok.ps1`).

### D-3. A fail-together entrypoint, not supervisord and not `exec`

`docker/session-runner-grok/dind-entrypoint.sh` (bash, ASCII, `set -euo pipefail`), run as root
under `init: true` (tini is pid 1):

1. Refuse unless root and privileged (`CapEff` in `/proc/self/status` is the full set): exit 3,
   `NotPrivileged`/`NotRoot` on stderr.
2. Deploy key (D-8): `ANTIPHON_DEPLOY_KEY_SOURCE` (default `/run/secrets/antiphon-deploy-key`) must
   be a regular non-empty file; a directory or absent file is exit 3 `DeployKeyMissing`.
   `install -m 0400 -o 1654 -g 1654` to `/run/antiphon/deploy-key` on the `/run/antiphon` tmpfs
   (0700, owned 1654). Phone-home secret: `PhoneHome__SecretPath` (default
   `/run/secrets/phone-home`) must likewise be a regular non-empty file, else exit 3
   `PhoneHomeSecretMissing`; it is read by the runner process directly (as today), never copied.
   Never print or hash either.
3. cgroup preparation: if `/sys/fs/cgroup/cgroup.controllers` exists (v2, Docker Desktop), move
   this process into `/sys/fs/cgroup/init` and enable `+cpu +memory +pids +io` in
   `cgroup.subtree_control` (the upstream `dind` helper's block, attributed). On both v1 and v2
   create the custody root (D-17): v2 `/sys/fs/cgroup/antiphon-custody` with `+pids` in its
   `cgroup.subtree_control`; v1 `/sys/fs/cgroup/pids/antiphon-custody` and
   `/sys/fs/cgroup/freezer/antiphon-custody`. Root-owned, 0755; nothing is chowned to 1654.
4. `iptables -nL >/dev/null` or exit 3 `IptablesUnusable`.
5. Start `dockerd --config-file /etc/docker/daemon.json` in the background, logging to
   `/state/logs/dockerd.log`; wait up to 30 s for `docker info`, else exit 3
   `NestedDaemonUnavailable`.
6. `export HOME=/home/app`; start the runner as
   `setpriv --reuid=1654 --regid=1654 --groups=1656 "$@"` in the background (`"$@"` is the image
   `CMD`, `dotnet Antiphon.SessionRunner.dll`). The supplementary group 1656 is the nested socket
   group (D-18); it is inherited by ordinary sessions and cleared for tracked ones.
7. `trap` TERM/INT to forward TERM to both children; `wait -n`; record which child exited and its
   status; TERM the other, wait, exit with the first status. The container is the unit: a dead
   daemon takes the runner down and `restart: unless-stopped` brings both back.

Rejected: supervisord (CARD-0590 D-1's rejection stands); `exec` into the runner after forking
`dockerd` (a dead daemon leaves a healthy-looking runner); a Compose sidecar (D-1).

### D-4. Engine packaging: the pinned tarball, whole, plus legacy iptables, on a named volume

In the `session-testing` stage (`docker/session-runner-grok/Dockerfile`, the same `RUN` that today
fetches the CLI):

- Extract from `docker-27.5.1.tgz`: `docker dockerd containerd containerd-shim-runc-v2 runc
  docker-init docker-proxy ctr` into `/usr/local/bin` (one `curl | tar`, eight paths).
- `apt-get install --no-install-recommends iptables iproute2 openssh-client sudo` (`util-linux`,
  which provides `setpriv`, is in the base), then `update-alternatives --set iptables
  /usr/sbin/iptables-legacy` and the same for `ip6tables`. Reason: server2's kernel is 4.15 with
  legacy iptables 1.6.1; mixing backends across the two layers is the classic DinD failure. The
  entrypoint check (D-3 step 4) turns a wrong guess into a named refusal, not a hang. `sudo` is
  installed here so the stage has one apt layer; its only rule is D-17's.
- `groupadd -g 1656 docker-nested`; `/etc/docker/daemon.json` (new file): `hosts`
  `unix:///var/run/docker.sock`, `group` `docker-nested`, `data-root` `/var/lib/docker`,
  `storage-driver` `overlay2`, `bip` `10.200.0.1/24`, `default-address-pools`
  `[{base: 10.201.0.0/16, size: 24}]`, `log-level` `warn`, `live-restore` `false`, `iptables` `true`.
- `/var/lib/docker` is the named volume `dind-data` (host ext4, not overlay-on-overlay).
- Toolchain for in-runner builds and tests (D-6): `COPY --from=build /usr/share/dotnet
  /usr/share/dotnet` (SDK 10 plus runtimes 9 and 10) and `COPY --from=node22 /usr/local /usr/local`
  (new `FROM node:22-bookworm AS node22`). Build-time assertions: `dockerd --version`,
  `runc --version`, `iptables --version` contains `legacy`, `dotnet --list-sdks` has a `10.` line,
  `dotnet --list-runtimes` has `Microsoft.AspNetCore.App 9.` and `10.`, `node --version`, `ssh -V`,
  `sudo --version`, `getent group docker-nested`.
- Stage ends `ENTRYPOINT ["/usr/local/bin/antiphon-dind-entrypoint.sh"]`,
  `CMD ["dotnet", "Antiphon.SessionRunner.dll"]`, `USER 0:0`. `runtime` (default) and
  `receipt-probe` are untouched: no engine, no SDK, no Node, no `sudo`, no helpers (R-7, G-40).

Rejected: Docker's apt repository (a second engine source); `--iptables=false`; runtime
probe-and-switch of the iptables backend; `/var/lib/docker` in the writable layer; a separate test
image as the only test lane (D-6).

### D-5. Testcontainers is unmodified; the preflight refuses a sibling daemon

No `TESTCONTAINERS_*` variable, no `.testcontainers.properties`. Inside the runner the default
endpoint is `unix:///var/run/docker.sock` (the nested daemon, group `docker-nested`) and
`GetConnectionString()`'s host is `localhost`, the runner's own loopback. Ryuk starts on the nested
daemon and its socket bind resolves inside the runner.

`scripts/test-docker.ps1` gains one preflight: the daemon's `docker info --format '{{.Name}}'` must
equal the process's own `hostname`; a mismatch is `SiblingDaemonRefused` (exit 2, no command
emitted); an unreachable daemon (including the tracked-session case, D-18) is
`NestedDaemonUnavailable`.

Rejected: `TESTCONTAINERS_HOST_OVERRIDE` on the sibling shape.

### D-6. Sessions run tests directly in the runner; the sibling test container is retired as a lane

The session's shell builds and runs the roster in its worktree with `scripts/run-checkpoint.ps1`
and `--property:OutputPath=bin-c604/`, NuGet at `/work/.nuget`, npm in the checkout's
`client/node_modules`; `SessionRunner__BaseUrl=http://127.0.0.1:1` and `SessionRunner__Enabled=false`
stay for the nested child (CARD-0590's refusing-client guard). `docker-compose.test.yml` and
`docker/tests/Dockerfile` are kept for the nested daemon (`delivery-fixture` still builds there)
and documented as nested-only.

Rejected: a test container on the nested daemon with `network_mode: host`.

### D-7. The nested child is the unmodified base stack, depth one

Inside the session, project `c604<run>` on the nested daemon is `docker-compose.yml` plus a
script-written `compose.child.yml` (sha-tagged child images, labels `c604-run=<run>`,
`c604-owner=child`, a per-run `ANTIPHON_BIND_PORT` on the runner's loopback). The child runner is
the `runtime` target: no engine, no socket, no SDK, no custody helpers (V-15).

### D-8. GitHub credential custody: a repo-scoped write deploy key that lives on server2

**Confirmed by the operator.** Unchanged from v1:

- **Generate on server2, never copy the private half.** `deploy-parent` runs
  `ssh-keygen -t ed25519 -N '' -C antiphon-server2-runner -f /home/mc/antiphon-server2/secrets/deploy_key`
  when absent (0600, owner `mc`). The public half is exported with the evidence.
- **Register from the desktop bridge, idempotently.** `scripts/c590-real.ps1`, after
  `deploy-parent`, runs `gh repo deploy-key list --json title` and, if `antiphon-server2-runner` is
  absent, `gh repo deploy-key add <pub> --allow-write --title antiphon-server2-runner`.
- **Deliver as a Compose file secret.** `docker-compose.server2-runner.yml`: `secrets:
  antiphon-deploy-key: file: ${ANTIPHON_DEPLOY_KEY_FILE:?ANTIPHON_DEPLOY_KEY_FILE is required}`;
  a missing variable refuses `up`; a missing file is D-3 `DeployKeyMissing`. The phone-home secret
  is a second file secret, `phone-home: file: ${PHONE_HOME_SECRET_FILE:?...}`, generated once on
  server2 by `deploy-parent` (`openssl rand -hex 32`, 0600, owner `mc`) and entered into the
  production user-secrets by the operator (S7 step; the value never crosses the SSH bridge into
  the desktop scripts or evidence).
- **Materialise for the app uid** (D-3 step 2): `/run/antiphon/deploy-key`, 0400, uid 1654, tmpfs.
- **Wire git through baked non-secret config**: `/etc/gitconfig` (`core.sshCommand = ssh -F
  /etc/antiphon/ssh_config`, `url."git@github.com:".pushInsteadOf = https://github.com/`);
  `/etc/antiphon/ssh_config` (`Host github.com` -> `HostName ssh.github.com`, `Port 443`, `User git`,
  `IdentityFile /run/antiphon/deploy-key`, `IdentitiesOnly yes`, `BatchMode yes`,
  `StrictHostKeyChecking yes`, `UserKnownHostsFile /etc/antiphon/github_known_hosts`). Fetches stay
  anonymous HTTPS; only pushes go over SSH.
- **Smoke** (`git-credential-smoke`, nested lane, S3): clone anonymously, branch
  `throwaway/c604-credential-smoke-<run>`, commit, push, `rev-parse`, delete the branch; record
  `pushed-sha.txt`, `branch.txt`, `deleted.txt` and the `ssh -T` banner.
- **Custody paragraph** in `docs/agent-credentials.md` §5, next to the nightly watchdog's: names
  and locations only, for both the deploy key and the phone-home secret.

Rejected (unchanged): fine-grained PAT, the Antiphon key store (no Linux X509 protector), a GitHub
App token, the desktop-sourced per-run token, baking, a raw bind mount.

### D-9. Cleanup and disk policy

**Confirmed by the operator.** Prune **inside** the nested daemon is permitted; prune on the **host**
daemon stays forbidden (`am-service`, `traefik`, `windmill`, `schoolrevision-*` share it).
`dind-data` is the one volume an operator may wipe; `runner-state` and `work` keep CARD-0590's
explicit-destroy rule (`runner-state` now also holds the custody store and `runner-store-id`, so
wiping it breaks every open verification execution: D-17). `deploy-parent` retires every Compose
project matching `^c590[0-9a-f]{12}$` (`down -v --remove-orphans`) and every image tagged
`antiphon-c590-*`, after writing the inventory (`docker ps -a`, `volume ls`, `images`) to the
evidence directory; V-12 asserts the foreign containers are still Up afterwards. `pgdata` and
`server-state` volumes of the retired projects go with them (they were run-scoped).

### D-10. Supersession, re-aimed not deleted

- `docs/docker-stack.md`: paragraphs 2-3 rewritten (S6): no application service ever mounts a
  socket; the persistent runner is privileged and owns a nested daemon; `DOCKER_SOCKET_GID` is gone;
  `docker-compose.session-testing.yml` is retired (its only purpose was the sibling lane) and
  `docker-compose.server2-runner.yml` is the persistent deployment; the sentence "SourceLanding
  Mutation stays on the Windows local path (CARD-0598)" becomes "SourceLanding Mutation runs on
  Windows (`windows-job-v1`) or on the server2 runner (`linux-cgroup-v1`, CARD-0604 Cut B); a Linux
  image run is still not a custody receipt" (Cut B, S12).
- CARD-0590 plan D-6 and D-13: one-line annotations appended in place ("Superseded 2026-09-22 by
  CARD-0604 ..."). D-14 stays true and is cited.
- Contract tests: `Testing_runner_has_explicit_socket` becomes `Server2_runner_owns_its_daemon`
  (the server2 file has `privileged: true` and `dind-data:/var/lib/docker`; zero `docker.sock` lines
  across every Compose file); `Testing_services_are_unprivileged` is kept for
  `docker-compose.test.yml`; `Only_the_server2_runner_is_privileged` (exactly one `privileged: true`
  across all Compose files); `Applications_share_nonroot_identity` unchanged (base file) plus
  `Server2_runner_starts_as_root_and_drops`.
- `docs/testing-and-build.md:77` gains one sentence naming the nested daemon and the local harness;
  §"CARD-0490 phone-home runner" gains one paragraph naming the production `server2` runner.
- `AGENTS.md`: no new line (the runner is server2-only and `docker-stack.md` owns it).

### D-11. Verification is folded; local Docker Desktop proves the runtime, server2 proves the acceptance

`## Verification design` below is the executable manifest for both cuts. A new harness
`scripts/verify-card0604-dind-runner.ps1` (pattern: `scripts/verify-card0594-linux-launch.ps1`)
boots the privileged image on Docker Desktop and grades D-1, D-3, D-4, D-5, D-8's plumbing and, in
Cut B, D-17's containment (steps 12-15) without server2 or GitHub; the server2 rows then run the
real deployment, the real sessions, the real push and the real Mutation.

### D-12. Mutation custody moves to server2 as a real backend, never as an exception

**Replaces v1 D-12 ("untouched").** The operator's reading is adopted: the custody rule constrains
process lineage and attestation, not the host. The persistent runner becomes a supported producer
by shipping a Linux custody backend with the full contract (D-17 containment, D-18 fences, D-19
server neutrality), proven by CARD-0598's own acceptance list (R-8 to R-15, V-28 to V-33). What is
**not** adopted: trusting server2 because it is a fresh container; running Mutation through the
nested daemon or Testcontainers (CARD-0590 D-14 and CARD-0598's boundary stand, D-18); relabelling
anything `windows-job-v1`; removing a Windows check.

Why it is the same shape as Windows: on Windows the runner (a standing process) launches a fresh
pty-host per session, the host places the child in a kill-on-close, no-breakaway job before it runs,
observes job accounting to zero and seals a receipt. On server2 the runner (a standing container
process) launches a fresh pty-host per session, the host places the child in a root-owned cgroup it
cannot leave before it runs, observes `cgroup.procs` to empty and seals a receipt. In both, the
snapshot is created by managed creation at exact L, executed only by direct descendants of the host,
and cleaned only after the sealed receipt and the restoration record agree.

### D-13. Defaults this plan is written under

- D-2 control plane: production desktop server via `https://antiphon.desktop.codeperf.net`;
  production `PhoneHomeRunner` settings `Enabled=true`, `AllowedRunnerId=server2`,
  `AllowDelegatedTasks=true`, `HostWorkspaceRoot=C:\src\Antiphon`, `RunnerWorkspace=/work`,
  `RunnerRepository=/work/repos/antiphon`, `CallbackOrigin=https://antiphon.desktop.codeperf.net`,
  `SharedSecret` from server2's generated file, `StandingAgentId` unset (optional when
  `AllowDelegatedTasks`). Runner: `PhoneHome__RunnerId=server2`, `Capacity=2`.
- One dedicated production project/board `Antiphon server2 runner` owns the acceptance agents and
  cards (created once by `deploy-parent`'s desktop half, never deleted by the scripts; agents the
  cases create are deleted by the cases).
- D-8: ed25519 deploy key titled `antiphon-server2-runner`, write access, `michal-ciechan/Antiphon`
  only; SSH over `ssh.github.com:443`.
- D-9: retire `c590*` projects and images during `deploy-parent`.
- Persistent project `antiphon-runner`; server2 root `/home/mc/antiphon-server2` with
  `secrets/stack.env`, `secrets/deploy_key`, `secrets/phone-home`; image tag
  `antiphon-server2/session-testing:<sha12>` plus the OCI revision label.
- Nested pools `10.200.0.1/24` and `10.201.0.0/16`; `stop_grace_period: 90s`; runner healthcheck
  `curl /health && docker info`, interval 10 s, start period 60 s.
- Nested child project `c604<run>`; evidence `/work/test-evidence/<run>/<case>/`, copied to the
  host path for the desktop bridge's `scp`.
- Nested roster for this card: `-Group small` plus the `db-fixture` shard
  `/*/*/(TestDbFixtureLifecycleTests*)|(TestDbFixtureIsolationTests*)|(ProductionRunnerGuardTests*)/*`;
  Cut B adds the `linux-custody` shard (V-30).
- The bridge passes `C604_BRANCH` for the runner's repository checkout; after land it is `master`.
- Script names keep their `c590` prefix. Harness port default 18298, refused in 17202-17205;
  harness image tag `antiphon-session-testing:c604-<sha12>`.
- D-16: Grok is the only agent kind admitted on the runner; its OAuth store is provisioned once by
  the operator inside the persistent runner (never copied from the desktop).
- D-17: custody helpers via `sudo` with one sudoers file; cgroup names are the execution's
  `ContainerId` GUID; observation method `CgroupProcsEmpty`; kill = freeze, SIGKILL every listed
  pid, thaw, re-read until empty (v1) or `cgroup.kill` then re-read (v2).
- D-19: the custody backend set is `{windows-job-v1, linux-cgroup-v1}`; the contract version stays 1.

### D-14. Two Code cuts, one plan

Cut A (S1-S8) delivers the persistent runner, the nested lane, the deploy key, production
phone-home and delegated Grok tasks on server2. Cut B (S9-S12) delivers the Linux custody backend
and server2-hosted Mutation. Each cut is its own Code -> Review -> land -> post-land Mutation
sequence; Cut B's Code brief starts from Cut A's landed L and its CP-5/CP-6a evidence. Reason: Cut
A is independently valuable and observable (board-dispatched sessions on server2), Cut B changes
the custody contract and deserves its own Review; a single 1,800-minute Code task would violate the
ten-minute foreground rhythm many times over with nothing landed in between.

Rejected: one cut (cost and review surface); Cut B first (nothing to run it on).

### D-15. Ordinary remote tasks run in a mirror worktree; the desktop worktree stays canonical

For `delegate.ps1 -Runner server2` (Workspace `Worktree`, kind Grok, roles other than Mutation):

- The dispatcher creates the **desktop** worktree exactly as today (`CreateForTaskAsync`, branch
  `feat/card-task-<short>`, base ref decision recorded) and pushes the branch to origin
  (`git push -u origin <branch>`; a push failure keeps the task Queued with a warning event, no
  fallback).
- Before launch it asks the runner to **mirror** it: new phone-home operation `WorkspaceMirror`
  `{branch, sha, name}` -> the runner runs `git -C /work/repos/antiphon fetch origin <branch>` and
  `git worktree add /work/worktrees/<name> <sha>` on branch `<branch>` (refusing when the fetched tip
  is not `<sha>`, G-27), as direct children of the runner process, uid 1654, and returns the POSIX
  path. `AgentSession.RunnerCwd` is that path; `AgentSession.Cwd` and `task.WorktreePath` stay the
  desktop path (CARD-0490 D-8's split), so the ~190 desktop consumers are untouched.
- The session runs there; the brief and every spilled body are shipped by the `Input` operation
  and written by the runner into `RunnerCwd` (G-21); `ANTIPHON_API` is `CallbackOrigin`; the
  session commits and pushes over the deploy key.
- At settlement the dispatcher **syncs** the desktop worktree: `git fetch origin <branch>` and
  `git merge --ff-only FETCH_HEAD` in the desktop worktree (a non-fast-forward or dirty desktop
  tree is a settlement warning event and the task keeps its report; never a reset, G-24). Landing,
  retirement, residue and progress accounting then run unchanged on the desktop worktree.
- The mirror is removed by `WorkspaceRemove` at retirement (best effort, residue recorded on the
  task as `remoteWorktreeResidue`; a runner-side sweep lists mirrors older than 14 days with no
  live session for the operator, never deletes on its own).

Rejected: a runner-only worktree with a POSIX `task.WorktreePath` (the 190-site audit and a second
land protocol); Shared workspace on the runner (no canonical desktop record); mirroring by `scp`
or `docker cp` (bypasses the branch as the unit of exchange).

### D-16. Grok is the runner's agent, and its store is provisioned once on server2

The image carries Grok 1.0.40 and nothing else; `PhoneHomeLaunchPolicy` keeps `kind == Grok` for
tasks (Raw is admitted for named agents only, D-2). Grok's OAuth store lives at `/state/grok` on the
`runner-state` volume and is created by the operator once, interactively, inside the persistent
runner (`docker exec -it -u 1654:1654 antiphon-runner-session-runner-1 grok login`), never copied
from the desktop and never baked (CARD-0575, CARD-0324). A missing or expired store is the
existing 409 `provider_sign_in_required`; CP-12 records it and the row stays red until the operator
logs in. This is a stated default: the operator may instead accept a metered `XAI_API_KEY` in the
runner environment, which moves spend off SuperGrok (`docs/agent-credentials.md` §3).

Rejected: copying the desktop `auth.json` (the CARD-0490 harness does it into a throwaway copy for a
canary; a persistent runner would hold a second live session store); Claude/Codex in the image (out
of scope; their homes have the same custody question).

### D-17. Linux custody backend `linux-cgroup-v1`: a root-owned cgroup the child cannot leave

**Containment.** Each tracked execution gets a cgroup named by the execution's `ContainerId` GUID
under the custody root the entrypoint created (D-3 step 3): v2 `/sys/fs/cgroup/antiphon-custody/<id>/tree`,
v1 the same path under both `pids` and `freezer`. The cgroup and its files are root-owned. The
session's process tree lives in `tree`; the observing pty-host lives outside it.

**Placement before execution, race-free.** The pty-host (uid 1654) launches the tracked child not
as `<exe> <args>` but as `sudo -n /usr/local/bin/antiphon-custody-enter <id> -- <exe> <args>`.
`antiphon-custody-enter` (root-owned 0755 bash, ASCII) validates `<id>` is a GUID and the cgroup
exists and is empty, writes its own pid into `tree/cgroup.procs` (both hierarchies on v1), then
`exec setpriv --reuid=1654 --regid=1654 --clear-groups --no-new-privs -- <exe> <args>`. The child
and every descendant inherit `tree` at fork; nothing in `tree` can write a root-owned
`cgroup.procs`, and `no_new_privs` makes `sudo` and every setuid binary inert for the whole tree,
so the tree cannot regain the right. Reparenting, `setsid`, double-fork and `nohup` do not change
cgroup membership (measured in V-28 before the backend is accepted). The `--clear-groups` drops the
`docker-nested` supplementary group (D-18).

**Observation.** The host reads `tree/cgroup.procs` (world-readable). Descendant-zero is an empty
list. The receipt's `ObservationMethod` is `CgroupProcsEmpty`, `ActiveProcesses` the count at the
final read (0), `RootPid`/`RootStartTimeUtc` the child's pid and `/proc/<pid>/stat` start time
against `btime` (the same pid the shim exec'd into, recorded by `RecordTracking`).

**Termination at seal.** `sudo -n /usr/local/bin/antiphon-custody-kill <id>`: v2 writes 1 to
`tree/cgroup.kill` (kernel 5.14+, Docker Desktop) then re-reads until empty; v1 writes `FROZEN` to
`freezer.state`, SIGKILLs every pid listed, writes `THAWED`, re-reads until empty (bounded 30 s,
then the observation stays `Draining`, never a fabricated zero). The helper validates `<id>` and
refuses any path outside the custody root (G-39). `sudoers.d/antiphon-custody` grants uid 1654
exactly these two commands, `NOPASSWD`, `env_reset` (G-35).

**Identity.** `VerificationHostIdentity.ContainerId` = the cgroup GUID; `HostInstanceId`,
`HostPid`, `HostStartTimeUtc` as today. The registration's `RunnerStoreId` is the custody store id
(one identity); `PhoneHome__StoreIdPath` is only consulted when the runner has no custody backend.
The store root stays `SessionLogPath/verification-custody` = `/state/session-runner/verification-custody`
on the `runner-state` volume, outside every snapshot (`RequireOutsideSnapshot` unchanged).

**Advertisement.** `SessionRunnerRuntime.VerificationCustodyBackend` returns `linux-cgroup-v1`
only when `LinuxCgroupCustodyProbe` passes at startup: the custody root exists for the detected
cgroup version, `sudo -n antiphon-custody-enter --probe` and `antiphon-custody-kill --probe`
succeed (create and remove an empty probe cgroup), and `/proc/self/status` shows
`NoNewPrivs: 0` for the runner. Otherwise `null` and nothing is advertised (G-28). The pty-host's
hello advertises `verificationCustodyV1` when launched with `--custody-store` and
`--custody-backend linux-cgroup-v1` (new launcher argument carrying the runner's probe result).

**Recovery.** Unchanged in shape: reservations, intents, `host-identity.json`, `tracking.json`,
seals and receipts are the same files; `RecoverManifests` and `ValidateRecovery` are
platform-neutral already. A lost host leaves `tree` populated or empty on disk; `ReadUnavailable`
reports `Unknown` and cleanup records residue (R-11). A runner restart inside the same container
re-adopts through the manifest (CARD-0594's `Restart_adopts_same_store_session_and_generation`
lane); container replacement kills the tree (tini reaps) and the execution is `Unknown` with
residue, which is the documented "restarting the runner process versus replacing its container"
rule.

Rejected: same-uid cgroups without the root-owned shim (the tree could `echo $$ > cgroup.procs`
out of its own accounting; CARD-0598 asks for non-escaping containment, and Windows jobs deny
breakaway in the kernel); user namespaces with subordinate uids (worktree file ownership crosses
uid maps; rootless-container complexity); a root pty-host (a privileged observer holding the pty
for the whole session); PID-namespace-only containment (no accounting file, and `unshare` under
the default seccomp profile is only available because the container is privileged); PID-tree
enumeration (CARD-0598: not authority); a per-Mutation Docker container (CARD-0590 D-14).

### D-18. Fences: a tracked session cannot reach the nested daemon, and the daemon never sees a snapshot

The nested socket's group is `docker-nested` (1656), not the app group. Ordinary sessions inherit
1656 from the runner (D-3 step 6). The custody shim runs the tracked child with `--clear-groups`,
so a Mutation session's `docker info` fails with permission denied and `test-docker.ps1` refuses
with `NestedDaemonUnavailable` (V-32). The Mutation battery never needs Docker (Ground truth), so
this costs nothing and turns CARD-0598's "do not give the snapshot to Docker/Testcontainers" into an
enforced property rather than a brief rule. The nested daemon still builds child images from the
ordinary checkout `/work/repos/antiphon` and mirror worktrees for ordinary sessions, which are not
sourced snapshots.

### D-19. Server-side custody becomes backend-neutral and runner-aware, without loosening

- `VerificationCustodyBackends` (Contracts): `WindowsJob = "windows-job-v1"`,
  `LinuxCgroup = "linux-cgroup-v1"`, `IsSupported(string)`, `ObservationMethodFor(backend)`. The
  six literal pins become membership checks **plus** an equality check against the backend the
  runner advertised when the execution was reserved (the binding records it; a runner refuses a
  binding whose backend is not its own, G-29).
- `SourceLandingAdmission.RequireSupportAsync(string? runnerId)` resolves the client through
  `ISessionRunnerDirectory.Resolve(runnerId)` and returns `(RunnerStoreId, Backend)`; an unavailable
  remote runner is the existing `phone_home_unavailable` 503, never a fallback to local (G-31).
- `VerificationExecutionService.ReserveAsync` takes the task's runner id; the binding's `Backend`
  and `RunnerStoreId` come from that runner. `PrepareLaunchAsync` compares `spec.Cwd` with
  `Creation.WorktreePath` as opaque ordinal strings (already the case).
- `VerificationCleanupService` reads custody through `directory.Resolve(task.RunnerId)` (G-33) and,
  for a remote task, performs the guarded verification removal through the remote workspace seam
  (below) instead of `IWorktreeManager.TryRemoveAsync`.
- **Remote managed creation for Mutation** (`task.RunnerId != null && SourceLandingOperationId != null`):
  `DelegationWorktreeService.CreateForTaskAsync` calls the runner's `VerificationWorkspaceCreate`
  `{identifier, sha, branch}` instead of the desktop `WorktreeManager`; the runner (direct child git,
  uid 1654) fetches origin, refuses unless `<sha>` is reachable from `origin/master` at that moment
  (exact published SHA), creates `/work/worktrees/<identifier>` on branch `feat/card-task-<short>`
  at `<sha>` with the schema-2 metadata (`CreationId`, `CreationComplete`, `GitDirectory`) mirrored
  from `WorktreeManager`, and returns `VerificationCreationCoordinates` with POSIX paths rooted
  under `RunnerRepository` (G-32). `ValidateVerificationAsync` for a remote task runs the same
  checks (registration exactly one, not locked/prunable, no sequencer, `HEAD == L`, symbolic ref,
  clean tracked/index) as `VerificationWorkspaceValidate` on the runner and compares the returned
  coordinates ordinally with `VerificationCreationJson`. For these tasks `task.WorktreePath` is the
  POSIX path and `task.RunnerId` is set; the eight custody files branch on `RunnerId` and every
  other consumer is excluded already (`ReadRetirementAsync` returns null for Mutation; landing has
  no merge target; residue sweeps skip `SourceLandingOperationId != null`).
- **Remote evidence and restoration**: the external root is
  `/work/repos/antiphon/.git/antiphon/verification/<O:N>/<task:N>/` on the runner (the
  common-Git-dir rule, unchanged); `WorktreeRemovalEvidence.ReadVerificationAsync` for a remote
  task reads `restoration.json`, the worktree metadata and the git state through
  `VerificationWorkspaceReadRestoration`/`VerificationWorkspaceInspect` and applies the identical
  rules (schema 1, `Restored`, `Source`, `CreationId`, `ReportSha256` = SHA256 of the stored task
  Result, exact `Outputs`); the desktop filesystem is never consulted for a remote task (G-38).
  `VerificationWorkspaceRemove` performs the guarded removal (no force, no recursion, unknown files
  and dirty trees refuse, exact listed outputs only) and returns `WorktreeRemoval`.
- The phone-home envelope gains `ReadCustody {binding, seal}`, `WorkspaceMirror`,
  `WorkspaceRemove`, `VerificationWorkspaceCreate`, `VerificationWorkspaceValidate`,
  `VerificationWorkspaceInspect`, `VerificationWorkspaceReadRestoration`,
  `VerificationWorkspaceRemove`; `Launch` admits a `VerificationBinding` only when the runner
  advertises custody and the binding's backend and store are its own (G-37);
  `PhoneHomeRunnerClient.ReadVerificationCustodyAsync` maps `ReadCustody`. All are typed operations
  with fixed bodies; no shell strings cross the socket (CARD-0490 D-3).

Rejected: keeping admission on the local client and letting the remote runner refuse at launch (a
reserved execution row with no producer); a "linux" flag that skips receipt validation; moving the
evidence root onto the desktop (the receipt and restoration must be where the producer wrote them).

## Target shape

```
desktop (production)                                   server2 host daemon (24.0.2)
  Antiphon.Server :17202  <-- Caddy https://antiphon.desktop.codeperf.net
    PhoneHomeRunnerDirectory (server2, capacity 2)        Compose project "antiphon-runner" (fixed)
    board: tasks with runnerId=server2, runner-bound agents  antiphon-runner-session-runner-1
    ISessionRunnerDirectory.Resolve("server2")            session-testing <sha>, privileged, user 0:0
    SourceLandingAdmission -> remote capabilities         restart: unless-stopped
    remote workspace seam (mirror / verification)          tini -> dind-entrypoint.sh (root)
                                                             |- dockerd (socket group docker-nested,
                                                             |    data-root = volume dind-data)
                                                             |    nested: Testcontainers, child c604<run>,
                                                             |    nested child image builds
                                                             `- setpriv 1654 +group 1656 -> SessionRunner
                                                                  phone-home wss -> desktop (register, connect)
                                                                  RunnerStoreId = custody store id
                                                                  |- pty-host -> ordinary session (1654, +1656)
                                                                  |     cwd /work/worktrees/<mirror>
                                                                  |     pwsh scripts/test-docker.ps1 -ThrowawayStack
                                                                  |     git push (deploy key)
                                                                  `- pty-host -> tracked session
                                                                        sudo antiphon-custody-enter <id> -- grok ...
                                                                        cgroup antiphon-custody/<id>/tree (root-owned)
                                                                        child: 1654, no groups, no_new_privs
                                                                        cwd /work/worktrees/task-<short> at exact L
                                                             /run/antiphon/deploy-key (tmpfs, 0400, 1654)
                                                             /run/secrets/phone-home (file secret, read by runner)
                                                             /work (volume), /state (runner-state: custody store,
                                                                grok home, runner-store-id), dind-data (volume)
```

## Slices

Order: Cut A = S1 -> S2 -> S5 -> S3 -> S7 -> S8 -> S4 -> S6; Cut B = S9 -> S10 -> S11 -> S12. S5
runs before S3/S4 so the image and entrypoint are proven locally before the server2 scripts depend
on them; S7/S8 (production phone-home and remote tasks) precede S4 because the host lane's session
cases now launch through the production server. Each slice is committed and pushed before its
checkpoint runs.

### S1. Image, entrypoint and baked config

Files: `docker/session-runner-grok/Dockerfile` (D-4 changes to the `session-testing` stage only;
new `node22` stage), new `docker/session-runner-grok/dind-entrypoint.sh` (D-3),
`docker/session-runner-grok/daemon.json`, `docker/session-runner-grok/ssh_config`,
`docker/session-runner-grok/gitconfig`, `docker/session-runner-grok/github_known_hosts`.
Tests: `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs` (extend
`Default_runtime_excludes_test_payload` with `dockerd`; new `Testing_stage_ships_the_engine`,
`Testing_stage_pins_legacy_iptables`, `Testing_stage_has_build_toolchain`,
`Testing_stage_entrypoint_is_the_dind_script`, `Testing_stage_creates_docker_nested_group`); new
`tests/Antiphon.Tests/Infrastructure/DindRunnerContractTests.cs` (text guards over the new files:
`Entrypoint_refuses_missing_deploy_key`, `Entrypoint_refuses_missing_phone_home_secret`,
`Entrypoint_drops_to_app_uid`, `Entrypoint_exits_when_either_process_exits`,
`Entrypoint_checks_iptables_before_dockerd`, `Entrypoint_never_prints_the_key`,
`Entrypoint_prepares_custody_root_on_both_cgroup_versions`,
`Nested_daemon_uses_private_address_pools`, `Nested_daemon_socket_group_is_docker_nested`,
`Ssh_config_pins_identity_and_known_hosts`, `Ssh_config_uses_port_443`,
`Gitconfig_pushes_over_ssh_only`, `Known_hosts_carry_github_keys`). Each reads through
`DockerStackDocuments.Read`.

### S2. Compose: the standalone server2 runner file; the sibling override retired

Files: new `docker-compose.server2-runner.yml` (project name `antiphon-runner`; services
`state-init` (as base) and `session-runner`: `build.target: session-testing`, `image:
antiphon-server2/session-testing:${SOURCE_SHA12:?}`, `user: "0:0"`, `privileged: true`, `init: true`,
`restart: unless-stopped`, `stop_grace_period: 90s`, `tmpfs: [/run/antiphon]`, volumes
`work:/work`, `runner-state:/state`, `dind-data:/var/lib/docker`, secrets `antiphon-deploy-key`,
`phone-home`, env `PhoneHome__Enabled: "true"`, `PhoneHome__RunnerId: server2`,
`PhoneHome__ServerOrigin: ${PHONE_HOME_SERVER_ORIGIN:?...}`, `PhoneHome__SecretPath:
/run/secrets/phone-home`, `PhoneHome__StoreIdPath: /state/runner-store-id`, `PhoneHome__AllowedCwd:
/work`, `PhoneHome__Capacity: "2"`, `PhoneHome__GrokHome: /state/grok`, `GROK_HOME: /state/grok`,
`SessionRunner__SessionLogPath: /state/session-runner`, `SessionRunner__PtyHostDir:
/tmp/antiphon-pty-hosts`, `Serilog__LogPath: /state/runner-logs`, `ANTIPHON_DEPLOY_KEY_SOURCE`,
`Agents__GrokCredentialProbeEnabled: "false"`; healthcheck with `docker info`; top-level `secrets`
with `file: ${ANTIPHON_DEPLOY_KEY_FILE:?...}` and `file: ${PHONE_HOME_SECRET_FILE:?...}`; volumes;
no `group_add`, no `docker.sock`), delete `docker-compose.session-testing.yml`,
`docker-compose.test.yml` (comment: nested-only), `docker/stack.env.example` (drop
`DOCKER_SOCKET_GID`; add `COMPOSE_PROJECT_NAME=antiphon-runner`, `ANTIPHON_DEPLOY_KEY_FILE`,
`PHONE_HOME_SECRET_FILE`, `PHONE_HOME_SERVER_ORIGIN=https://antiphon.desktop.codeperf.net`).
Tests: `DockerStackContractTests` re-aim per D-10 plus `Server2_runner_starts_as_root_and_drops`,
`Deploy_key_secret_is_required`, `Server2_runner_requires_phone_home_origin_and_secret`,
`Server2_runner_has_nested_store_volume`, `Server2_runner_restarts_unless_stopped`,
`Server2_file_defines_only_runner_and_state_init`, `Stack_env_example_has_no_socket_gid`,
`Base_compose_keeps_phone_home_disabled` (the existing lines 94/98 kept as an explicit method).

### S3. The nested lane: the script a session actually runs

Files: `scripts/c590-remote.sh` (lane detection `docker info Name` versus `hostname`; per-case lane
declaration and `WrongLane` refusal; `ensure_dirs` without `sudo` in the nested lane; `write_result`
in shell; `PROJECT` = `antiphon-runner` host / `c604${RUN}` nested; `ensure_checkout` takes
`C604_BRANCH`; `run_dotnet` and `run_client` in-shell (D-6); `deployment-state` against the nested
child (D-7) with `compose.child.yml`; `case_git_smoke` per D-8; `case_throwaway` items =
`child-server-image-payload`, `child-runner-image-payload`, `deployment-state`, `client-lint`,
`client-tests`, `messaging-tests`, `dotnet-filter` (db-fixture), `git-credential-smoke`,
`session-result-export-and-child-cleanup`; `scrub_file` pattern
`gh[pousr]_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+`), `scripts/test-docker.ps1` (D-5 preflight from
the manifest: `daemon.present`, `daemon.name`, `daemon.hostname`), `scripts/test-docker-container.ps1`.
Tests: `tests/Antiphon.Tests/Scripts/DockerTestCommandTests.cs` new `Sibling_daemon_is_refused`,
`Missing_nested_daemon_is_refused`; `tests/Antiphon.Tests/Scripts/C590Harness.cs` `Happy()` gains
`daemon`; new `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`:
`Nested_lane_never_uses_sudo_or_python`, `Scrub_covers_github_token_prefixes`,
`Smoke_deletes_its_branch`, `Child_project_is_run_scoped_and_distinct`.

### S4. The host lane: deploy, launch through production, observe

Files: `scripts/c590-remote.sh` host-lane cases `deploy-parent` (D-8 key and phone-home secret
generation, D-9 inventory and retirement, image build with sha tag, `compose -p antiphon-runner
-f docker-compose.server2-runner.yml up -d --no-build --remove-orphans`, health wait, restart-policy
and nested-daemon assertions, no-host-socket assertion, `curl https://antiphon.desktop.codeperf.net/api/session-runners/server2/status`
from the host recording `dispatchEligible`), `custody-containment` (Cut B, V-28 on v1),
`nested-residue`, `persistent-restart` (`compose stop`, `up -d`, health, nested images retained,
registration re-established with the same `runnerStoreId`); `scripts/c590-real.ps1` (live case
list; `C604_BRANCH`; deploy-key registration after `deploy-parent`; **the session cases move to the
desktop half**: `session-nested-stack`, `await-nested-stack`, `session-git-smoke`,
`parent-native-and-command-session` create or reuse the runner-bound Raw agent in the dedicated
production project through `POST /api/agents` `{kind: Raw, runnerId: server2, exe: /bin/sh}`,
start it, post input through `POST /api/sessions/{id}/input`, poll the transcript for the
`C604_EXIT=<n>` marker in bounded calls, and copy evidence with `docker cp` over SSH; the
production project/board ids are read from `.antiphon/c604-production.json`, created once by
`deploy-parent`'s desktop half); `scripts/verify-docker-stack.ps1` (stub cases with the manifest
keys `deployKeyPresent`, `phoneHomeSecretPresent`, `restartPolicy`, `daemonName`/`runnerHostname`,
`dispatchEligible`, `sessionOrigin`, `hostResidue`, `nestedResidue`, `imagesRetained`,
`storeIdBefore/After`, `sessionState`, `markerSeen`, `subordinateAccepted`, `branchDeleted`,
`pushedSha`).
Tests: `tests/Antiphon.Tests/Scripts/DockerStackSmokeCommandTests.cs` new
`Missing_deploy_key_refuses_deploy`, `Missing_phone_home_secret_refuses_deploy`,
`Non_persistent_policy_refuses_deploy`, `Sibling_daemon_refuses_deploy`,
`Not_dispatch_eligible_refuses_deploy`, `Host_residue_refuses_nested_stack_case`,
`Nested_residue_refuses_nested_stack_case`, `Session_origin_is_required_for_nested_stack`,
`Still_running_session_is_not_a_result`, `Dead_session_without_marker_is_incomplete`,
`Subordinate_failure_refuses_acceptance`, `Lost_nested_store_refuses_restart_case`,
`Changed_store_id_refuses_restart_case`, `Undeleted_smoke_branch_is_refused`.

### S5. Local harness

File: new `scripts/verify-card0604-dind-runner.ps1` (pwsh 7, ASCII, Docker Desktop only; `-Image`,
`-Build`, `-Port` default 18298 refused in 17202-17205, `-EvidenceRoot .antiphon/c604-harness`,
`-Containment` (Cut B steps 12-15); one container `c604-<stamp>`, one volume `c604-<stamp>-dind`, a
throwaway key pair and a throwaway phone-home secret in the evidence directory, registered nowhere;
`PhoneHome__Enabled=false` in the harness container). Steps in `### Local harness`. Prints
`C604 HARNESS: image=... nested=... egress=... loopback=... launchMs=... key=... restart=...
refusal=... containment=...` and `C604 HARNESS EXIT CODE: n`.
Tests: new `tests/Antiphon.Tests/Scripts/VerifyDindRunnerScriptTests.cs`:
`Production_port_is_refused`, `Image_is_required`,
`Compose_and_dockerfile_do_not_hardcode_a_key_path_value`, `Harness_names_only_its_own_container`.

### S6. Docs and supersession (Cut A part)

Files: `docs/docker-stack.md` (D-10), `docs/agent-credentials.md` §5 (D-8 custody paragraph: deploy
key and phone-home secret), `docs/testing-and-build.md:77` and §"CARD-0490 phone-home runner"
(production `server2` runner paragraph; `delegate.ps1 -Runner`), CARD-0590 plan D-6 annotation,
`docs/ops-http.md` (one row: `GET /api/session-runners/{runnerId}/status`, and `runnerId` on
`POST /api/agents` and `POST /api/agent-tasks`), `docs/agent-kinds.md` (the phone-home paragraph
names the production runner and Raw admission).
Tests: new `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs`:
`Docker_stack_doc_names_the_nested_daemon`, `Credentials_doc_names_the_deploy_key_custody`,
`Card_0590_D6_is_annotated_superseded`, `Ops_http_names_runner_status_and_runner_id`.

### S7. Production phone-home: runner-bound agents and settings

Files: `server/Application/Settings/PhoneHomeRunnerSettings.cs` (+`AllowDelegatedTasks`,
`RunnerRepository`, `RawExeAllowList` default `[/bin/sh, /bin/bash, /usr/local/bin/pwsh]`,
`MaxCapacity` default 8; `StandingAgentId` optional when `AllowDelegatedTasks`),
`PhoneHomeRunnerSettingsValidator`, `PhoneHomeLaunchPolicy` (`IsRunnerBound(Agent)` replaces
`IsPinnedAgent` at every call site; `RefuseUnsupportedStart` admits delegated Worktree tasks and
Raw named agents per D-2, still refuses card starts, OnAgent, Shared, ReadOnly, pins, Herdr,
custom wrappers; `Project` maps exe from the allow-list or `grok`, cwd from `RunnerCwd`, keeps
`VerificationBinding`), `server/Domain/Entities/Agent.cs` (+`RunnerId`, EF migration
`AddAgentRunnerId`), `AgentControlService` (named runner-bound agents; `RunnerCwd` = `/work` for
named agents), `PhoneHomeRunnerDirectory.Register` (capacity `1..MaxCapacity`; the connection
records capacity and the runtime rejects launches above it), `PhoneHomeContracts` (registration
carries `Capabilities`), `src/Antiphon.SessionRunner/PhoneHomeSettings.cs` (`Capacity` positive),
`PhoneHomeCommandDispatcher.RejectUnsupportedLaunch` (exe allow-list plus `grok`; cwd equal to
`AllowedCwd` or under `AllowedCwd/worktrees/`), `PhoneHomeConnectionService` (sends the
capabilities DTO; store id per D-17 when custody is present, else `StoreIdPath`),
`server/Api/Endpoints/AgentsEndpoints` (`runnerId` on create), `scripts/verify-phone-home-grok.ps1`
(unchanged behaviour; its settings block gains `AllowDelegatedTasks=false`). Production enablement
is an operator step recorded in `docs/testing-and-build.md` and executed as CP-6a: set the
user-secrets, `git pull --rebase` in the main checkout, `restart-apphost.ps1`, confirm
`GET /api/version` and `GET /api/session-runners/server2/status` `dispatchEligible: true` after
the runner reconnects.
Tests: `PhoneHomeStandingLaunchTests` (existing methods keep their outcomes for the pinned agent;
new `Runner_bound_raw_agent_projects_allow_listed_exe_only`,
`Card_start_and_onagent_stay_refused_for_runner_bound_agent`), new
`tests/Antiphon.Tests/Application/PhoneHomeDirectoryTests.cs` (`Capacity_above_bound_is_refused`,
`Registration_carries_capabilities`), `PhoneHomeCommandDispatcherTests` (new
`Cwd_outside_workspace_is_refused`, `Raw_exe_outside_allow_list_is_refused`,
`Capacity_is_the_configured_value`), `PhoneHomeRunnerSettingsValidatorTests` (new file:
`Standing_agent_optional_only_with_delegated_tasks`).

### S8. Delegated Worktree tasks on the runner: routing, mirror, spill, sync

Files: `server/Domain/Entities/AgentTask.cs` (+`RunnerId`, `RemoteWorktreePath`,
`RemoteWorktreeResidue`; migration `AddAgentTaskRunnerId`), `AgentTaskCreateRequest` (+`RunnerId`;
`AgentTaskService` validates: known runner id, Workspace Worktree, kind Grok (or unset -> Grok),
no pin/OnAgent/Shared/ReadOnly, `AllowDelegatedTasks`), `AgentTaskDispatcher` (for `RunnerId`:
resolve through the directory before claiming, keep Queued with a `RunnerUnavailable` warning event
when unavailable; desktop worktree as today then `git push -u origin <branch>`; `WorkspaceMirror`;
pool delegate with `RunnerId`; session with `RunnerId/RunnerStoreId/RunnerCwd`; launch through
`directory.Resolve`), new `server/Application/Services/RemoteWorkspaceService.cs` (the
`WorkspaceMirror`/`WorkspaceRemove` client and the settlement `ff-only` sync, G-24),
`SessionMessageQueueService`/`TypedBodySpill` (remote sessions: spill content travels in the
`Input` payload `{spill: {relativePath, body}}`, desktop write skipped, G-21), `TaskWorktreeRetirementService`
(after the desktop removal, `WorkspaceRemove` for `RemoteWorktreePath`, residue recorded),
`PhoneHomeContracts` (`WorkspaceMirror = 14`, `WorkspaceRemove = 15`, `Input` body extension),
`PhoneHomeRunnerClient`, runner: new `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs`
(git as direct children via `ProcessStartInfo.ArgumentList`, uid 1654, under
`PhoneHome__AllowedCwd/worktrees`; refuses names outside `task-[0-9a-f]{8}` and shas that are not
40 hex; G-27), `PhoneHomeCommandDispatcher` (the two ops and the spill write inside `RunnerCwd`
only), `scripts/delegate.ps1` (`-Runner <id>`; refused with `-Shared`, `-OnAgent`, `-Agent`,
`-ReadOnly`).
Tests: new `tests/Antiphon.Tests/Application/PhoneHomeTaskRoutingTests.cs`
(`Unavailable_runner_never_launches_locally`, `Task_session_commits_runner_owner_before_launch`,
`Shared_workspace_is_refused`, `Push_failure_keeps_task_queued`), new
`tests/Antiphon.Tests/Application/RemoteWorktreeMirrorTests.cs`
(`Non_fast_forward_sync_is_refused`, `Sync_fast_forwards_desktop_worktree`,
`Retirement_records_remote_residue`), new `tests/Antiphon.Tests/Application/PhoneHomeSpillTests.cs`
(`Remote_spill_never_writes_desktop_file`, `Remote_spill_travels_in_input_payload`), new
`tests/Antiphon.SessionRunner.Tests/RunnerWorkspaceServiceTests.cs` (scratch git repo, platform
neutral: `Mirror_refuses_sha_mismatch`, `Mirror_creates_worktree_on_branch_at_sha`,
`Remove_refuses_dirty_tree`, `Spill_write_stays_inside_runner_cwd`), `PhoneHomeCommandDispatcherTests`
(`Workspace_ops_are_admitted_only_under_allowed_cwd`), `tests/Antiphon.Tests/Scripts/DelegateScriptTests`
(existing class if present, else new: `Runner_switch_refuses_shared_and_pins`).

### S9. Cut B: custody helpers, sudoers, containment measurements

Files: `docker/session-runner-grok/Dockerfile` (`sudo` already in D-4's apt line; `COPY` the two
helpers 0755 root; `COPY docker/session-runner-grok/sudoers-antiphon-custody
/etc/sudoers.d/antiphon-custody` 0440; build-time `visudo -c`), new
`docker/session-runner-grok/antiphon-custody-enter.sh`, `antiphon-custody-kill.sh`,
`sudoers-antiphon-custody`; `dind-entrypoint.sh` step 3 custody root; `scripts/verify-card0604-dind-runner.ps1`
steps 12-15 (`-Containment`); `scripts/c590-remote.sh` host case `custody-containment` (the same
four measurements executed inside the persistent runner on server2 via `docker exec -u 1654`).
Tests: new `tests/Antiphon.Tests/Infrastructure/CustodyHelperContractTests.cs` (text guards:
`Shim_sets_no_new_privs`, `Shim_clears_groups`, `Shim_validates_guid_and_empty_cgroup`,
`Sudoers_names_only_the_helpers`, `Kill_helper_validates_execution_path`,
`Kill_helper_never_kills_outside_tree`, `Helpers_branch_on_cgroup_version`);
`DockerStackContractTests.Default_runtime_excludes_custody_helpers` (G-40, extends R-7).

### S10. Cut B: the Linux custody backend in pty-host and runner

Files: `src/Antiphon.SessionRunner.Contracts/VerificationCustody.cs` (+`VerificationCustodyBackends`;
`Backend` default removed from the record, always explicit), `src/Antiphon.Agents.Pty/IPtyCustodyContainment.cs`
(new seam: `Place(childLaunch) -> launch args`, `ReadActive() -> (count, pids)`, `Terminate()`,
`ContainerId`; `WindowsJobContainment` wraps today's `IPtyCustodyNative` job path unchanged;
`LinuxCgroupContainment` (Linux only) creates nothing itself: it rewrites the launch to
`sudo -n antiphon-custody-enter <id> -- <exe> <args>`, reads `tree/cgroup.procs`, terminates
through `antiphon-custody-kill`), `PtyAgentRunner` (tracked launch on Linux through Porta with the
rewritten argv; `SealAndObserveCustodyAsync` and `CustodyTermination` via the seam),
`src/Antiphon.PtyHost/HostCustodyJournal.cs` (`ObservationMethod` from
`VerificationCustodyBackends.ObservationMethodFor(binding.Backend)`; `RecordTracking` reads
`/proc/<pid>/stat` start time on Linux), `HostSession` (`--custody-backend` option; hello
advertises when the backend is set; refuses a binding whose backend differs), `PtyHostOptions`,
`src/Antiphon.PtyHost.Client/PtyHostLauncher.cs` (passes `--custody-backend`), new
`src/Antiphon.SessionRunner/LinuxCgroupCustodyProbe.cs` (D-17 advertisement), `SessionRunnerRuntime`
(`VerificationCustodyBackend` from the probe on Linux, unchanged on Windows; `RunnerStoreId`),
`RunnerCustodyLedger.PrepareStart` (accepts when the runtime's backend is non-null and equals
`binding.Backend`; still writes `unsupported.json` and refuses otherwise),
`src/Antiphon.PtyHost.Protocol/VerificationCustodyStore.cs` (`ValidateBinding` membership plus
`expectedBackend`; `ValidateReceipt` method per backend), `PhoneHomeConnectionService` (store id
per D-17), `PhoneHomeCommandDispatcher` (`ReadCustody = 16`; `Launch` admits a binding when the
runtime advertises custody and `binding.Backend`/`RunnerStoreId` match, G-37).
Tests (Windows-executing, platform neutral): new `tests/Antiphon.SessionRunner.Tests/RunnerCustodyLedgerBackendTests.cs`
(`Foreign_backend_binding_is_refused`, `Linux_backend_accepted_only_when_probe_passed`,
`Windows_path_unchanged`), new `tests/Antiphon.SessionRunner.Tests/LinuxCustodyProbeTests.cs`
(fake filesystem root: `Backend_is_null_without_delegated_root`, `Backend_is_null_when_helper_probe_fails`,
`Backend_is_linux_cgroup_when_all_pass`), new `tests/Antiphon.PtyHost.Tests/CustodyReceiptBackendTests.cs`
(`Cgroup_method_requires_linux_backend`, `Job_method_requires_windows_backend`,
`Receipt_bytes_round_trip_for_both_backends`), `PhoneHomeCommandDispatcherTests`
(`Binding_without_custody_backend_is_refused`, `Binding_with_foreign_store_is_refused`).
Tests (Linux-executing, in-runner shard `linux-custody`, V-30): new
`tests/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests.cs` (`RequireLinux()`, needs the delegated
custody root: `Tracked_launch_places_child_in_tree`, `Double_fork_and_setsid_stay_in_tree`,
`Root_exit_with_live_descendants_is_draining_until_seal`, `Seal_terminates_and_observes_zero`,
`Child_cannot_sudo_or_move_cgroup`, `Lost_host_leaves_unknown_not_receipt`,
`Generation_mismatch_is_refused`, `Second_seal_is_idempotent`), plus the existing
`LinuxPtyHostLauncherTests` and `LinuxPhoneHomeRunnerTests` (CARD-0605's lane, now real).

### S11. Cut B: server neutrality and the remote verification workspace

Files: `SourceLandingAdmission` (`RequireSupportAsync(string? runnerId)` via
`ISessionRunnerDirectory`; membership + equality), `VerificationExecutionService` (runner-aware
reservation; backend from the resolved runner), `VerificationCleanupService` (custody via the
task's runner; remote removal through the seam), `WorktreeRemovalEvidence` (remote branch; backend
membership), `VerificationReceiptPolicy` (method per backend), `DelegationWorktreeService`
(`CreateForTaskAsync`/`ValidateVerificationAsync` remote branch), `AgentTaskService` (Mutation with
`RunnerId` admitted when `AllowDelegatedTasks`; admission checks the remote runner),
`AgentTaskDispatcher` (Mutation tasks with `RunnerId`: no mirror, managed creation through the
seam, launch with the binding through the resolved runner), new
`server/Application/Interfaces/IVerificationWorkspace.cs` + `LocalVerificationWorkspace` (wraps
today's `IWorktreeManager`/`ILandingGit` calls) + `RemoteVerificationWorkspace` (phone-home ops),
`PhoneHomeContracts` (`VerificationWorkspaceCreate = 17`, `Validate = 18`, `Inspect = 19`,
`ReadRestoration = 20`, `Remove = 21`), `PhoneHomeRunnerClient`, runner `RunnerWorkspaceService`
(the verification ops: schema-2 metadata, exact-L check against `origin/master`, guarded removal
mirroring `GuardedVerificationRemoval`'s rules), `PhoneHomeLaunchPolicy.Project` (keeps the
binding), `scripts/delegate.ps1` (`-Runner` with `-SourceLanding`).
Tests: new `tests/Antiphon.Tests/Application/SourceLandingAdmissionTests.cs`
(`Remote_task_checks_remote_runner_capabilities`, `Unavailable_remote_runner_is_503_not_local`,
`Foreign_backend_capability_is_refused`), new `VerificationCleanupServiceTests.cs`
(`Remote_task_custody_read_uses_bound_runner`, `Remote_removal_goes_through_workspace_seam`),
new `RemoteVerificationWorkspaceTests.cs` (`Coordinates_outside_runner_repository_are_refused`,
`Restoration_read_never_touches_desktop_fs`, `Validate_compares_coordinates_ordinally`),
`VerificationReceiptPolicyTests` (new or extended: `Cgroup_method_with_windows_backend_is_invalid`,
`Imported_receipt_must_match_binding_backend`), `RunnerWorkspaceServiceTests`
(`Verification_create_refuses_sha_not_on_origin_master`, `Verification_create_writes_schema_2_metadata`,
`Verification_remove_refuses_unknown_files`).

### S12. Cut B: fences re-aimed, docs, CARD-0598 closure

Files: `scripts/c590-remote.sh` `case_testing_payload` (the `! grep VerificationCustodyV1` line
becomes: the `runtime` and `receipt-probe` images must not contain the helpers or `sudo`; the
`session-testing` image must advertise `linux-cgroup-v1` from `/capabilities` and never the
string `windows-job-v1` in its advertisement), `scripts/verify-docker-stack.ps1` `linux-custody`
(expects `verificationCustodyV1` with backend `linux-cgroup-v1` on the persistent runner; refuses
`windows-job-v1` or a missing store id), `docs/docker-stack.md` (D-10 custody sentence),
`docs/orchestration-loop.md` (one sentence after "Use local inherited execution only ...": the
server2 runner is a supported producer for `-Runner server2` Mutation tasks; the nested daemon is
still never an executor), `docs/testing-and-build.md` §"Mutation-stage positive-control execution"
(one paragraph: Linux lane, helpers, what `provider_sign_in_required` means for a Mutation on
server2), `docs/agent-credentials.md` (the Grok store on `runner-state`), CARD-0590 plan D-13
annotation, CARD-0598: **close as duplicate-of-CARD-0604** once this plan lands (§ below).
Tests: `DockerStackDocumentationTests` (`Docs_name_linux_cgroup_backend`,
`Orchestration_loop_names_server2_producer`), `RemoteScriptContractTests`
(`Testing_payload_expects_linux_backend_never_windows`).

## CARD-0598 disposition

Fold and close. This plan carries CARD-0598's "Required work" and "Acceptance and boundaries"
verbatim as: D-17 (the delegated-cgroup observer it named as the example, with the measured
escape behaviours as V-28), D-18 (its "do not give the snapshot to Docker/Testcontainers"
boundary, enforced), D-19 (admission/launch/receipt/recovery/cleanup as one coordinated change,
Windows behaviour preserved, no fabricated `windows-job-v1`), R-8 to R-15 and V-28 to V-33 (its
acceptance list, method-scoped on real Linux), and CP-18 ("commission new supported SourceLanding
PCs against the actual landed SHA"). Once this plan is landed on master, close CARD-0598 as
duplicate-of-CARD-0604 with a pointer to this file; its stable finding key
`card-0590:linux-source-landing-custody` is satisfied by Cut B's land, not by this plan.

## Verification design

TestDesign folded (D-11). Bodies read for v2 in addition to v1's list:
`server/Application/Services/PhoneHomeLaunchPolicy.cs`, `PhoneHomeRunnerSettings.cs`,
`server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs` (whole),
`PhoneHomeLiveConnection.cs:1-60`, `PhoneHomeRunnerClient.cs` (grep), `server/Api/Endpoints/SessionRunnerEndpoints.cs:13-60`,
`server/Application/Services/AgentControlService.cs:636-662,1295-1310`, `AgentTaskDispatcher.cs:3572-3700`,
`AgentTaskService.cs:1090-1120`, `SourceLandingAdmission.cs` (whole), `VerificationExecutionService.cs`
(whole), `VerificationCleanupService.cs` (whole), `VerificationReceiptPolicy.cs` (whole),
`server/Infrastructure/Data/WorktreeRemovalEvidence.cs:40-140`, `DelegationWorktreeService.cs:265-300,360-412`,
`server/Infrastructure/Git/WorktreeManager.cs` (`CreateVerificationAsync`, `ReadVerificationCreationAsync`),
`src/Antiphon.SessionRunner/RunnerCustodyLedger.cs` (whole), `SessionRunnerRuntime.cs:40-110,215-270,340-360`,
`Program.cs:190-215`, `PhoneHomeSettings.cs`, `PhoneHomeConnectionService.cs:70-110`,
`PhoneHomeCommandDispatcher.cs` (`LaunchAsync`, `RejectUnsupportedLaunch`), `PhoneHomeStoreIdentity.cs`,
`src/Antiphon.SessionRunner.Contracts/VerificationCustody.cs`, `PhoneHomeContracts.cs:20-130`,
`src/Antiphon.PtyHost.Protocol/VerificationCustodyStore.cs:157-260`, `src/Antiphon.PtyHost/HostCustodyJournal.cs`
(whole), `HostSession.cs:95-170,290-301`, `src/Antiphon.Agents.Pty/IPtyCustodyNative.cs`,
`IPtyCustodyJournal.cs`, `PtyAgentRunner.cs:78-200`, `src/Antiphon.PtyHost.Client/PtyHostLauncher.cs:55-95`,
`docs/orchestration-loop.md:1190-1295`, `docs/testing-and-build.md:153-176,222-245,395-440`,
`docs/agent-credentials.md:166-196`, CARD-0490 plan D-1 to D-11, CARD-0590 plan D-13/D-14,
CARD-0598/0604/0575 card text, `tests/fixtures/card0490-linux/README.md`.

### Inspection

- `DockerStackContractTests` | `[Category("Unit")]`, pure file reads; the re-aimed methods and the
  new ones stay file-only; no OS skip.
- `DockerTestCommandTests` / `DockerStackSmokeCommandTests` | `[Category("Integration")]`,
  `[ParallelLimiter<ProcessSpawnLimit>]`, spawn `pwsh` with `ANTIPHON_C590_STUB`; new manifest keys
  default to the happy shape in `C590Harness.Happy()`.
- `PhoneHomeStandingLaunchTests`, `PhoneHomeSessionRoutingTests` | existing seams
  (`PhoneHomeTestHost`); the pinned-agent methods keep their outcomes (R-2a).
- `PhoneHomeCommandDispatcherTests` | runner-side, no socket; `RejectUnsupportedLaunch` is
  `internal` and exercised directly.
- `RunnerCustodyLedger` / `VerificationCustodyStore` | file-backed with `IVerificationCustodyFiles`;
  backend tests inject the runtime's backend string.
- `LinuxCgroupCustodyTests` | `RequireLinux()`; needs the entrypoint-prepared custody root and the
  helpers, so it executes only inside the persistent runner (V-30) and in the harness container
  (CP-14 runs the same shard through `docker exec` after step 15).
- `TestDbOperations.StartAsync` | boundary unchanged (D-5); V-18 executes it for real.
- Missing setup recorded: Docker Desktop up for CP-4/CP-14 (`docker-desktop` skill); desktop `gh`
  login with admin on the repo (CP-5); `mc@server2` SSH; production user-secrets access and the
  `restart-apphost.ps1` runbook for CP-6a; the operator's one-time `grok login` inside the runner
  before CP-12 and CP-18 (D-16).

### Delivery inventory

Session-launched rows (CP-8, CP-11, CP-12, CP-18) are asynchronous relative to the desktop script:
the desktop posts input through the **production** server (`POST /api/sessions/{id}/input`, exactly
the queue every other session uses) and returns; the session runs for up to 75 minutes;
`await-*` cases poll the transcript through `GET /api/sessions/{id}/transcript` in bounded calls
of at most 8 minutes each, at least 60 s apart, and only the call that sees `C604_EXIT=<n>` reads
the evidence the session wrote. Owners: `session-id.txt` before input; the session's own
`c590-result.json` per case before the marker; the host lane's `docker cp` copy after the marker;
the residue inventory after the copy. `SessionStillRunning` writes no `result-ready`. Interruption
(session dies, 75-minute cap) is `SessionIncomplete` with partial evidence kept. For the delegated
task rows (CP-12, CP-18) the receipt is the task's settlement on the board (`GET /api/agent-tasks/{id}`
`status` terminal, `result` non-empty) plus the transcript-confirmed `UserPrompt` of the brief
(AGENTS.md's delivery verdict), never the runner's screen. Substitutes: none; every server2 row is
the real runner registered with the real production server.

### Proves it works now

Cut A (V-1 to V-27):

- V-1: the `session-testing` image carries the whole engine, legacy iptables, iproute2,
  openssh-client, sudo, SDK 10 with runtimes 9 and 10, Node 22, pwsh, the entrypoint, `daemon.json`,
  ssh/git config, known_hosts, the `docker-nested` group | text (S1, CP-1) and live payload probe
  (`testing-runner-payload`, CP-6) | every `test -x`/`grep`/`getent` passes.
- V-2: only the server2 runner is privileged; no Compose file mounts a socket; both secrets are
  required; the runner restarts `unless-stopped`; base Compose keeps phone-home disabled |
  `DockerStackContractTests` | CP-1.
- V-3: the command boundaries refuse a sibling daemon, a missing nested daemon, a missing deploy key
  or phone-home secret, a non-persistent policy, a runner that is not dispatch-eligible, host
  residue, nested residue, a lost or changed nested store, and an undeleted smoke branch, each
  before emitting a command | `DockerTestCommandTests`, `DockerStackSmokeCommandTests` | CP-2.
- V-4: the image boots privileged and the nested daemon's `Name` equals the container's hostname |
  harness | CP-4 | `nested=yes`.
- V-5: nested pull, NAT and TLS egress work | harness step 5 | CP-4 | `egress=yes`.
- V-6: a nested container's mapped port answers on the runner's own `127.0.0.1` for uid 1654 |
  harness step 6 | CP-4 | `loopback=200`.
- V-7: a session launches on the DinD runner in under 5 s (`POST /sessions`, as CARD-0594's
  harness) | harness step 7 | CP-4.
- V-8: the deploy key is `0400 1654` on the tmpfs; `ssh -G github.com` resolves the baked config |
  harness step 8 | CP-4 | `key=ok`.
- V-9: killing `dockerd` exits the container; the restart policy brings it back with the nested
  store retained | harness step 9 | CP-4 | `restart=ok`.
- V-10: a missing key, a missing phone-home secret and a non-privileged run each refuse with exit 3
  and the named diagnosis | harness step 10 | CP-4 | `refusal=ok`.
- V-11: server2 project `antiphon-runner` is up with one privileged runner and no server or
  Postgres on the host daemon, nested daemon `Name` equals the runner's hostname, no host socket
  mount, the deploy key is registered on GitHub under the expected title, the phone-home secret file
  exists 0600 | `deploy-parent` | CP-5.
- V-12: the `c590*` leftovers are retired with an inventory; `am-service`, `schoolrevision-*`,
  `antiphon-messaging_*` untouched | `deploy-parent` | CP-5.
- V-13: the runner registers with production over `wss://antiphon.desktop.codeperf.net` and
  `GET /api/session-runners/server2/status` reports `available: true`, `dispatchEligible: true`,
  `platform: linux`, the custody-store-derived `runnerStoreId` | `deploy-parent` after CP-6a |
  CP-6a | the same status from the desktop and from server2's host.
- V-14: a runner-bound Raw agent created on the production board launches in the persistent runner
  and echoes `C590_RAW_OK` within 60 s (the CARD-0594 live confirmation on server2, through the real
  control plane) | `parent-native-and-command-session` | CP-7.
- V-15: a board-launched session runs `test-docker.ps1 -Group small -ThrowawayStack` and the
  nested preflight accepts | `session-nested-stack` | CP-8 | `daemon.name` = hostname recorded.
- V-16: the child images build on the nested daemon from `/work/repos/antiphon` with the sha label;
  the child runner has no engine and no custody helpers | subordinate `child-*-image-payload` | CP-8.
- V-17: the nested child stack `c604<run>` reaches health, reports the sha, keeps a marker across
  `stop`/`up`, is removed with `down -v` | subordinate `deployment-state` | CP-8.
- V-18: Small executes in-runner (client lint/build, all frozen Vitest files >= 102 tests 0 failed,
  messaging 24 classes >= 206 methods on nested Redpanda) and the `db-fixture` shard executes
  against Testcontainers Postgres via `localhost`, 0 failed | subordinate cases | CP-8 | fresh TRX.
- V-19: evidence is under `/work/test-evidence/<run>/<case>/` and copied to the desktop |
  `session-nested-stack` | CP-8.
- V-20: after the run, no `c604` containers, networks or volumes on the nested daemon and none ever
  on the host daemon; the runner is healthy | `nested-residue` | CP-9.
- V-21: `compose stop` then `up -d` keeps the container name, brings the nested daemon back with its
  images, re-registers with the **same** `runnerStoreId` and becomes dispatch-eligible again |
  `persistent-restart` | CP-10.
- V-22: an in-session push over the deploy key lands `throwaway/c604-credential-smoke-<run>`, the
  branch is deleted afterwards, and the copied logs contain no key or token bytes |
  `session-git-smoke` | CP-11.
- V-23: after the desktop CLI disconnects, the runner stays registered and the evidence index is
  complete | `server2-independent-handoff` | CP-13.
- V-24: `delegate.ps1 -Runner server2 -Role Custom -Worktree -Card <acceptance card>` with a
  doc-only goal ("append one line to `docs/cards/…`-free file `.antiphon/c604-roundtrip.md`, commit,
  push") creates the desktop worktree, pushes the branch, mirrors it on the runner (POSIX path
  recorded on the session as `runnerCwd`), the Grok session receives the brief as a spilled file
  inside the mirror (V-25), commits and pushes, settles `Succeeded` on the board, and the desktop
  worktree fast-forwards to the pushed commit | `remote-task-roundtrip` | CP-12 | task `status`,
  `result`, `runnerId`, desktop `git log -1` = pushed sha, transcript `UserPrompt` of the brief.
- V-25: the spilled brief exists in the mirror at the relative path the prompt names and does not
  exist in the desktop worktree | `remote-task-roundtrip` | CP-12.
- V-26: `-Land <task>` of that task lands through the unchanged desktop protocol | `remote-task-roundtrip`
  | CP-12 | `HasPublication`.
- V-27: the mirror is removed at retirement with no residue, and a task with a foreign
  `RunnerId` is refused at create (422) | `remote-task-roundtrip`, `PhoneHomeTaskRoutingTests` |
  CP-12, CP-2a.

Cut B (V-28 to V-33):

- V-28: containment measured on both cgroup versions before the backend is trusted: (a) a child
  launched through `antiphon-custody-enter` that double-forks, `setsid`s, `nohup`s and daemonises
  leaves every descendant in `tree/cgroup.procs` (six pids expected from the probe script); (b) the
  child cannot `sudo -n true` (exit 1, `no new privileges`) nor write any `cgroup.procs` (EACCES);
  (c) `antiphon-custody-kill` empties `tree` within 5 s and the observer is never listed in
  `tree`; (d) a reparented orphan (parent exited) is still listed | harness steps 12-15 (v2) and
  `custody-containment` (v1, server2) | CP-14, CP-15 | `containment=ok` on both.
- V-29: the persistent runner advertises `verificationCustodyV1` with
  `verificationCustodyBackend: linux-cgroup-v1` and a `runnerStoreId` equal to the registration's,
  stable across `persistent-restart` | `deploy-parent`, `linux-custody`, `persistent-restart` |
  CP-16.
- V-30: the `linux-custody` shard runs in-runner: `LinuxCgroupCustodyTests` (8 methods),
  `LinuxPtyHostLauncherTests`, `LinuxPhoneHomeRunnerTests`, all executed, 0 failed, fresh TRX |
  subordinate `dotnet-filter linux-custody` | CP-17.
- V-31: a SourceLanding Mutation task `delegate.ps1 -Role Mutation -Runner server2 -Worktree
  -SourceLanding <O>` against Cut B's landed L: remote managed creation at exact L (branch,
  schema-2 metadata, `HEAD == L`, clean); the binding is reserved with backend `linux-cgroup-v1`
  and the runner's store; the runner accepts the tracked launch; the session runs a fixture PC
  battery (PC-1, PC-9, PC-29 from this plan, method-scoped, red-restore-green); writes
  `restoration.json` at the runner evidence root; settles; `-CleanupVerification <task>` imports
  the receipt from the runner (`Exited`, `activeProcesses: 0`, `outputDrained: true`,
  `observationMethod: CgroupProcsEmpty`, `receiptDigest` recorded) and removes the remote worktree
  with `residue: null` | `mutation-roundtrip` | CP-18 | task row, `VerificationExecutions` row,
  the runner's `accepted-receipt.json` bytes equal to the imported bytes.
- V-32: inside that tracked session `docker info` fails with permission denied and
  `test-docker.ps1` refuses `NestedDaemonUnavailable`; `id -G` shows no 1656 | `mutation-roundtrip`
  (recorded in the session's evidence) | CP-18.
- V-33: cross-backend and identity refusals live: a `windows-job-v1` binding sent to the runner's
  `Launch` is refused `verification_custody_invalid_binding`; a binding with a foreign store is
  refused; a second `Launch` for a sealed execution is refused `verification_custody_sealed` |
  `linux-custody` case (three direct `Launch` frames through the production directory's test
  endpoint is not available, so through `PhoneHomeCommandDispatcherTests` locally and the runner's
  HTTP `/sessions` inside the container via `docker exec curl`) | CP-16, CP-19.

### Guards the regression

- R-1: the 80 unchanged `DockerStackContractTests` methods keep their assertions | CP-1.
- R-2: the 20 `DockerTestCommandTests` and 16 `DockerStackSmokeCommandTests` methods keep their
  exits and diagnoses with the extended happy manifest | CP-2.
- R-2a: the CARD-0490 pinned-agent methods (`PhoneHomeStandingLaunchTests`,
  `PhoneHomeSessionRoutingTests`, `LinuxPhoneHomeRunnerTests`, `PhoneHomeCommandDispatcherTests`)
  keep their outcomes; `verify-phone-home-grok.ps1` still refuses production ports
  (`VerifyPhoneHomeGrokScriptTests`) | CP-2a.
- R-3: harness argument and port refusals | `VerifyDindRunnerScriptTests` | CP-3.
- R-4: nested cleanup acts only on `c604<run>` | CP-2 and live in CP-9.
- R-5: the `runtime` and `receipt-probe` targets never advertise custody, never carry the engine,
  `sudo` or the helpers; `linux-custody` refuses a `windows-job-v1` advertisement | CP-6, CP-16.
- R-6: docs name the new shape; the old sentences are gone | `DockerStackDocumentationTests` |
  CP-13, CP-20.
- R-7: `Default_runtime_excludes_test_payload` (+`dockerd`, +helpers) and
  `Receipt_runner_has_no_docker_tools` | CP-1.
- R-8 (CARD-0598): admission and launch refusal/success are method-scoped native evidence on real
  Linux | V-30, V-33 | CP-17, CP-19.
- R-9 (CARD-0598): root exit with live descendants is `Draining`, never `Exited`, until the seal
  observes zero | `Root_exit_with_live_descendants_is_draining_until_seal` | CP-17.
- R-10 (CARD-0598): original observer/store restart or loss yields `Unknown` with residue, never a
  receipt | `Lost_host_leaves_unknown_not_receipt`, `Changed_store_id_refuses_restart_case` | CP-17,
  CP-2.
- R-11 (CARD-0598): generation/identity mismatch is refused at every boundary | ledger, host,
  dispatcher tests | CP-19.
- R-12 (CARD-0598): the seal is irreversible; a sealed execution cannot relaunch | `Second_seal_is_idempotent`,
  `Sealed_execution_refuses_relaunch` | CP-17, CP-19.
- R-13 (CARD-0598): receipts are exact persisted bytes, imported unchanged with a digest |
  `Receipt_bytes_round_trip_for_both_backends`, V-31 | CP-19, CP-18.
- R-14 (CARD-0598): drained output and zero descendants are both required for `Exited` |
  `CustodyReceiptBackendTests`, V-31 | CP-19, CP-18.
- R-15 (CARD-0598): complete attempt-set sealing and guarded cleanup apply to remote tasks |
  `VerificationCleanupServiceTests`, V-31 | CP-19, CP-18.
- R-16: Windows custody is byte-for-byte unchanged in behaviour: the Windows ledger, store and
  receipt tests keep passing with `windows-job-v1` explicit | CP-19.

### Guard inventory

- G-1: privileged only on the server2 runner | PC-1
- G-2: no socket in any Compose file | PC-2
- G-3: entrypoint drops to uid 1654 | PC-3
- G-4: fail-together | PC-4
- G-5: missing deploy key refused | PC-5
- G-6: legacy iptables pinned | PC-6
- G-7: runtime/receipt images gain no engine | PC-7
- G-8: private nested address pools | PC-8
- G-9: sibling daemon refused by the session entry | PC-9
- G-10: persistence policy on the runner | PC-10
- G-11: deploy refuses without the key file | PC-11
- G-12: host residue refuses the nested-stack case | PC-12
- G-13: undeleted smoke branch refused | PC-13
- G-14: scrub covers every GitHub token prefix | PC-14
- G-15: harness refuses production ports | PC-15
- G-16: ssh config pins identity and known hosts | PC-16
- G-17: deploy-key secret interpolation is required | PC-17
- G-18: runner-bound task refuses Shared workspace | PC-18
- G-19: unavailable runner never launches locally | PC-19
- G-20: task session commits all three runner fields before launch | PC-20
- G-21: remote spill never writes the desktop file | PC-21
- G-22: runner refuses a cwd outside its workspace | PC-22
- G-23: Raw exe allow-list on the runner | PC-23
- G-24: settlement sync refuses non-fast-forward | PC-24
- G-25: server2 file requires phone-home origin and secret | PC-25
- G-26: registration capacity bound | PC-26
- G-27: mirror refuses a sha mismatch | PC-27
- G-28: custody advertised only when the probe passes | PC-28
- G-29: foreign-backend binding refused by the ledger | PC-29
- G-30: receipt method must match the backend | PC-30
- G-31: admission checks the task's runner, not local | PC-31
- G-32: remote creation coordinates rooted under the runner repository | PC-32
- G-33: remote cleanup reads custody from the bound runner | PC-33
- G-34: shim sets `no_new_privs` | PC-34
- G-35: sudoers names only the two helpers | PC-35
- G-36: nested socket group is `docker-nested`, not the app group | PC-36
- G-37: runner admits a binding only with an advertised, matching backend | PC-37
- G-38: remote restoration read never touches the desktop filesystem | PC-38
- G-39: kill helper validates the execution path | PC-39
- G-40: runtime/receipt images carry no custody helpers or sudo | PC-40
- G-41: runner adopts the server-minted epoch (minted at Register, carried on the ticket) | PC-41

guards=41, mapped=41, missing=0, duplicate PC mappings=0. The child runner's socket-freedom (V-16)
and the live containment measurements (V-28) are live evidence, not PCs: their only seams are
heredocs and shell.

### Positive controls

Mutation runs these after each cut's land, method-scoped, red then restore then green, Windows
local inherited (Cut A: PC-1 to PC-27; Cut B: PC-28 to PC-40 plus the fixture re-run of PC-1,
PC-9, PC-29 on server2 as V-31's battery). Code implements the tests and runs V/R only.

- PC-1: delete `privileged: true` from `docker-compose.server2-runner.yml`; expect
  `DockerStackContractTests.Only_the_server2_runner_is_privileged` red at the count assertion.
- PC-2: add `- /var/run/docker.sock:/var/run/docker.sock` under the server2 runner's `volumes`;
  expect `DockerStackContractTests.Server2_runner_owns_its_daemon` red at `sockets.ShouldBe(0)`.
- PC-3: in `dind-entrypoint.sh` replace `setpriv --reuid=1654 --regid=1654` with a bare `"$@"`;
  expect `DindRunnerContractTests.Entrypoint_drops_to_app_uid` red.
- PC-4: delete the `wait -n` line and the kill-the-other block; expect
  `DindRunnerContractTests.Entrypoint_exits_when_either_process_exits` red.
- PC-5: delete the `[ -f ... ] && [ -s ... ]` check before `install`; expect
  `DindRunnerContractTests.Entrypoint_refuses_missing_deploy_key` red.
- PC-6: delete the `update-alternatives --set iptables` line; expect
  `DockerStackContractTests.Testing_stage_pins_legacy_iptables` red.
- PC-7: move the engine `tar` extraction into `runtime-base`; expect
  `DockerStackContractTests.Default_runtime_excludes_test_payload` red at the `dockerd` assertion.
- PC-8: delete `default-address-pools` from `daemon.json`; expect
  `DindRunnerContractTests.Nested_daemon_uses_private_address_pools` red.
- PC-9: in `scripts/test-docker.ps1` delete the `daemon.name -ne daemon.hostname` refusal; expect
  `DockerTestCommandTests.Sibling_daemon_is_refused` red at `Diagnosis(run).ShouldBe("SiblingDaemonRefused")`.
- PC-10: delete `restart: unless-stopped` under `session-runner` in the server2 file; expect
  `DockerStackContractTests.Server2_runner_restarts_unless_stopped` red.
- PC-11: in `verify-docker-stack.ps1` `deploy-parent` delete the `deployKeyPresent` refusal; expect
  `DockerStackSmokeCommandTests.Missing_deploy_key_refuses_deploy` red.
- PC-12: delete the `hostResidue` refusal in `session-nested-stack`; expect
  `DockerStackSmokeCommandTests.Host_residue_refuses_nested_stack_case` red.
- PC-13: delete the `branchDeleted` refusal in `session-git-smoke`; expect
  `DockerStackSmokeCommandTests.Undeleted_smoke_branch_is_refused` red.
- PC-14: in `c590-remote.sh` `scrub_file` revert the pattern to `gho_`; expect
  `RemoteScriptContractTests.Scrub_covers_github_token_prefixes` red.
- PC-15: delete the forbidden-port check in `verify-card0604-dind-runner.ps1`; expect
  `VerifyDindRunnerScriptTests.Production_port_is_refused` red.
- PC-16: delete `IdentitiesOnly yes` from `ssh_config`; expect
  `DindRunnerContractTests.Ssh_config_pins_identity_and_known_hosts` red.
- PC-17: change `${ANTIPHON_DEPLOY_KEY_FILE:?...}` to `${ANTIPHON_DEPLOY_KEY_FILE:-}`; expect
  `DockerStackContractTests.Deploy_key_secret_is_required` red.
- PC-18: in `AgentTaskService` delete the `Workspace != Worktree` refusal for `RunnerId` requests;
  expect `PhoneHomeTaskRoutingTests.Shared_workspace_is_refused` red.
- PC-19: in `AgentTaskDispatcher` replace the unavailable-runner `return NotClaimed` with
  `client = _directory.Local`; expect `PhoneHomeTaskRoutingTests.Unavailable_runner_never_launches_locally`
  red at the launch-target assertion.
- PC-20: delete the `RunnerCwd =` assignment on the task session; expect
  `PhoneHomeTaskRoutingTests.Task_session_commits_runner_owner_before_launch` red (the DB constraint
  rejects the partial owner).
- PC-21: in `SessionMessageQueueService` remove the remote branch so `TypedBodySpill` always writes;
  expect `PhoneHomeSpillTests.Remote_spill_never_writes_desktop_file` red at `File.Exists`.
- PC-22: in `RejectUnsupportedLaunch` replace the `AllowedCwd/worktrees/` prefix check with `true`;
  expect `PhoneHomeCommandDispatcherTests.Cwd_outside_workspace_is_refused` red.
- PC-23: add `/usr/bin/env` to the runner's exe check without the allow-list; expect
  `PhoneHomeCommandDispatcherTests.Raw_exe_outside_allow_list_is_refused` red.
- PC-24: in `RemoteWorkspaceService` change `merge --ff-only` to `merge`; expect
  `RemoteWorktreeMirrorTests.Non_fast_forward_sync_is_refused` red.
- PC-25: change `${PHONE_HOME_SERVER_ORIGIN:?...}` to `${PHONE_HOME_SERVER_ORIGIN:-}`; expect
  `DockerStackContractTests.Server2_runner_requires_phone_home_origin_and_secret` red.
- PC-26: in `PhoneHomeRunnerDirectory.Register` delete the `> MaxCapacity` refusal; expect
  `PhoneHomeDirectoryTests.Capacity_above_bound_is_refused` red.
- PC-27: in `RunnerWorkspaceService.MirrorAsync` delete the fetched-tip equality check; expect
  `RunnerWorkspaceServiceTests.Mirror_refuses_sha_mismatch` red.
- PC-28: in `LinuxCgroupCustodyProbe` return the backend when the root is absent; expect
  `LinuxCustodyProbeTests.Backend_is_null_without_delegated_root` red.
- PC-29: in `RunnerCustodyLedger.PrepareStart` replace `binding.Backend == runtimeBackend` with
  `IsSupported(binding.Backend)`; expect `RunnerCustodyLedgerBackendTests.Foreign_backend_binding_is_refused`
  red.
- PC-30: in `VerificationReceiptPolicy.Validate` accept either method for any backend; expect
  `VerificationReceiptPolicyTests.Cgroup_method_with_windows_backend_is_invalid` red.
- PC-31: in `SourceLandingAdmission.RequireSupportAsync` ignore `runnerId` and use `directory.Local`;
  expect `SourceLandingAdmissionTests.Remote_task_checks_remote_runner_capabilities` red.
- PC-32: in `RemoteVerificationWorkspace` delete the `RunnerRepository` prefix check on the returned
  coordinates; expect `RemoteVerificationWorkspaceTests.Coordinates_outside_runner_repository_are_refused`
  red.
- PC-33: in `VerificationCleanupService` read custody through `directory.Local` for every task;
  expect `VerificationCleanupServiceTests.Remote_task_custody_read_uses_bound_runner` red.
- PC-34: delete `--no-new-privs` from `antiphon-custody-enter.sh`; expect
  `CustodyHelperContractTests.Shim_sets_no_new_privs` red.
- PC-35: add a third `NOPASSWD` command line to `sudoers-antiphon-custody`; expect
  `CustodyHelperContractTests.Sudoers_names_only_the_helpers` red.
- PC-36: change `"group": "docker-nested"` to `"group": "app"` in `daemon.json`; expect
  `DindRunnerContractTests.Nested_daemon_socket_group_is_docker_nested` red.
- PC-37: in `PhoneHomeCommandDispatcher.RejectUnsupportedLaunch` delete the
  `VerificationCustodyBackend is null` refusal; expect
  `PhoneHomeCommandDispatcherTests.Binding_without_custody_backend_is_refused` red.
- PC-38: in `WorktreeRemovalEvidence` remote branch fall through to the desktop `File.ReadAllBytes`;
  expect `RemoteVerificationWorkspaceTests.Restoration_read_never_touches_desktop_fs` red.
- PC-39: delete the GUID/`realpath` prefix validation in `antiphon-custody-kill.sh`; expect
  `CustodyHelperContractTests.Kill_helper_validates_execution_path` red.
- PC-40: `COPY` the helpers into `runtime-base`; expect
  `DockerStackContractTests.Default_runtime_excludes_custody_helpers` red.
- PC-41: in `PhoneHomeConnectionService`, restore the local `Interlocked.Increment(ref _epoch)` in
  place of the registration-supplied epoch; expect
  `PhoneHomeEpochAgreementTests.A_real_runner_heartbeat_lands_when_the_server_counter_is_ahead` red
  (already measured RED 1/0/1, GREEN 2/2/0 at `52bda1e8`).

Filters: `--treenode-filter "/*/*/<Class>/<method>"`; restore and rebuild before each green.
`Antiphon.SessionRunner.Tests` and `Antiphon.PtyHost.Tests` rows build their own project.

### Local harness

`scripts/verify-card0604-dind-runner.ps1` performs, in order, recording each command's output
under `.antiphon/c604-harness/<stamp>/`:

1. Optional `-Build`: `docker build -f docker/session-runner-grok/Dockerfile --target session-testing --build-arg SOURCE_REVISION=<HEAD sha> -t <image> .`
2. `ssh-keygen -t ed25519 -N "" -f <evidence>/throwaway_key`; `openssl rand -hex 32 >
   <evidence>/throwaway_secret`; `docker volume create c604-<stamp>-dind`.
3. `docker run -d --privileged --init --restart unless-stopped --name c604-<stamp> -p <port>:8080
   -v c604-<stamp>-dind:/var/lib/docker --tmpfs /run/antiphon
   -v <evidence>/throwaway_key:/run/secrets/antiphon-deploy-key:ro
   -v <evidence>/throwaway_secret:/run/secrets/phone-home:ro
   -e ANTIPHON_DEPLOY_KEY_SOURCE=/run/secrets/antiphon-deploy-key -e PhoneHome__SecretPath=/run/secrets/phone-home
   -e SessionRunner__SessionLogPath=/tmp/state/session-runner -e SessionRunner__PtyHostDir=/tmp/antiphon-pty-hosts
   -e Serilog__LogPath=/tmp/state/runner-logs -e PhoneHome__Enabled=false -e SessionRunner__Herdr__Enabled=false <image>`;
   wait for `GET /health` (90 s cap).
4. `docker exec c docker info --format '{{.Name}} {{.Driver}} {{.CgroupVersion}}'` and
   `docker exec c hostname`; `nested=yes` when the names match.
5. `docker exec c docker run --rm alpine:3.20 wget -qO- https://api.github.com/zen`; `egress=yes`.
6. `docker exec -u 1654:1654 c sh -c 'id=$(docker run -d -p 127.0.0.1::80 nginx:alpine); p=$(docker port $id 80 | sed s/.*://); curl -fsS -o /dev/null -w %{http_code} http://127.0.0.1:$p/; docker rm -f $id >/dev/null'`
   (uid 1654 with group 1656 inherited); `loopback=<code>`.
7. `POST /sessions` `{"sessionId":"<guid>","exe":"/bin/bash","args":[],"env":{},"cwd":"/tmp","cols":120,"rows":30}`;
   record status and `launchMs`.
8. `docker exec -u 1654:1654 c stat -c '%u %a' /run/antiphon/deploy-key` (`1654 400`) and
   `ssh -F /etc/antiphon/ssh_config -G github.com` (identityfile, hostname `ssh.github.com`, port
   443, identitiesonly); `key=ok`.
9. Read `RestartCount`; `docker exec c sh -c 'kill -TERM $(pidof dockerd)'`; wait up to 60 s for
   `RestartCount` to increment and `/health` to answer; `docker exec c docker images -q
   nginx:alpine` non-empty; `restart=ok`.
10. `docker run --rm --privileged --init <image>` with no key mount -> exit 3 `DeployKeyMissing`;
    with the key but no secret -> exit 3 `PhoneHomeSecretMissing`; `docker run --rm --init <image>`
    (not privileged, both files) -> exit 3 `NotPrivileged`; `refusal=ok`.
11. (Cut A end) `docker rm -f c604-<stamp>`; `docker volume rm c604-<stamp>-dind`; delete the
    throwaway files; print the result line and `C604 HARNESS EXIT CODE: <n>`.

With `-Containment` (Cut B), before step 11:

12. `docker exec -u 1654:1654 c sudo -n /usr/local/bin/antiphon-custody-enter --probe` and
    `... antiphon-custody-kill --probe` both exit 0; `GET /capabilities` shows
    `verificationCustodyBackend: linux-cgroup-v1` and a `runnerStoreId`.
13. `docker exec -u 1654:1654 c sudo -n antiphon-custody-enter <guid> -- /opt/antiphon-tests/custody-probe.sh`
    (a script shipped in the `session-testing` stage that forks a `sleep 300` in a `setsid`
    subshell, a double-forked `sleep 300`, a `nohup sleep 300 &`, then exits); after 1 s
    `cat /sys/fs/cgroup/antiphon-custody/<guid>/tree/cgroup.procs` lists the surviving sleeps (v2
    path; v1 path on server2) and does not list the pty-host or the runner pid.
14. `docker exec -u 1654:1654 c sudo -n antiphon-custody-enter <guid2> -- sh -c 'sudo -n true; echo sudo=$?; echo $$ > /sys/fs/cgroup/antiphon-custody/<guid2>/../cgroup.procs; echo move=$?'`
    records `sudo=1` and `move=1` (EACCES).
15. `docker exec -u 1654:1654 c sudo -n antiphon-custody-kill <guid>`; within 5 s `cgroup.procs`
    is empty; the helper refuses `../../init` with exit 3 `BadExecutionId`; `containment=ok`.
16. `docker exec -u 1654:1654 c` runs the `linux-custody` shard (`dotnet run --project
    tests/Antiphon.PtyHost.Tests --property:OutputPath=bin-c604/ -- --treenode-filter
    "/*/*/(LinuxCgroupCustodyTests*)|(LinuxPtyHostLauncherTests*)/*"`) from a checkout mounted at
    `/work/repos/antiphon` (`-Checkout <path>`); all executed, 0 failed.

On Docker Desktop the daemon is cgroup v2, so steps 3 and 12-15 exercise the v2 branches; server2
(v1) exercises the v1 branches in CP-5 and CP-15. Both hosts run the same image.

### Server2 cases

Host-lane cases run through `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case <name>
-Manifest $manifest` with `ANTIPHON_C590_STUB` unset (scp `c590-remote.sh`, run over SSH as
`mc`). Session cases run from the desktop against the production API with `ANTIPHON_API=https://antiphon.desktop.codeperf.net`
or `http://localhost:17202` (same server) and the operator's own credentials, inside the dedicated
production project. Freeze `$sha = git rev-parse HEAD`, a fresh `$runId`, and `C604_BRANCH`
before CP-5; every server2 row shares them. Evidence lands under `.antiphon/c590-server2/evidence-<case>/`
on the desktop and `/work/test-evidence/<run>/<case>/` on server2. Ordering CP-5 -> CP-6a -> CP-7
-> CP-8 is mandatory: no nested work before the runner is registered and the Raw session is proven.

### Out of scope

- Full CARD-0590 Medium roster shards: the lane exists; running them is CARD-0590's acceptance.
- CARD-0590 receipt/recovery cut cases (S6c/S6d).
- `RequireLinux()` classes beyond the custody and launcher classes (CARD-0605).
- The live Grok V-7 turn of CARD-0490 (its harness keeps working against its isolated server).
- Claude/Codex on the runner; a second phone-home runner; Shared-workspace remote tasks.
- Docker-sibling custody of any kind (CARD-0590 D-14).

### Checkpoints

Cut A:

| CP | After | Build | Group | Filter | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c604/` (Windows) | contract-guards | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)/*` | V-1 (text), V-2, R-1, R-7 | all listed, 0 failed (>= 105 executed) | 12 |
| CP-2 | S3-S4 | CP-1 | command-guards | `/*/*/(DockerTestCommandTests*)\|(DockerStackSmokeCommandTests*)\|(RemoteScriptContractTests*)/*` | V-3, R-2, R-4, R-5 | all listed, 0 failed (>= 55 executed) | 8 |
| CP-2a | S7-S8 | CP-1 | phone-home-guards | `/*/*/(PhoneHomeStandingLaunchTests*)\|(PhoneHomeSessionRoutingTests*)\|(PhoneHomeDirectoryTests*)\|(PhoneHomeTaskRoutingTests*)\|(RemoteWorktreeMirrorTests*)\|(PhoneHomeSpillTests*)\|(PhoneHomeRunnerSettingsValidatorTests*)\|(VerifyPhoneHomeGrokScriptTests*)/*` | V-27 (create refusal), R-2a, G-18..G-21, G-24..G-26 | all listed, 0 failed (>= 30 executed) | 10 |
| CP-2b | S7-S8 | `tests/Antiphon.SessionRunner.Tests -> bin-c604/` | runner-guards | `/*/*/(PhoneHomeCommandDispatcherTests*)\|(RunnerWorkspaceServiceTests*)\|(PhoneHomeConnectionServiceTests*)/*` | G-22, G-23, G-27, R-2a | all listed, 0 failed (>= 12 executed) | 8 |
| CP-3 | S5 | CP-1 | harness-contract | `/*/*/VerifyDindRunnerScriptTests/*` | R-3 | 4 executed, 0 failed | 3 |
| CP-4 | S1, S2, S5 | Docker Desktop image from HEAD | dind-local | `pwsh -NoProfile -File scripts/verify-card0604-dind-runner.ps1 -Image antiphon-session-testing:c604-<sha12> -Build` | V-4..V-10 | `C604 HARNESS EXIT CODE: 0` | 25 |
| CP-5 | all A | server2 host daemon: session-testing image at HEAD | server2-deploy | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case deploy-parent -Manifest $manifest` | V-11, V-12 | 1 case accepted; inventory, `.pub`, secret presence, status probe in evidence | 30 |
| CP-6 | all A | CP-5 | server2-testing-payload | `... -Case testing-runner-payload ...` | V-1 (live), R-5 | 1 case accepted | 4 |
| CP-6a | S7 | production server at HEAD (`git pull --rebase` in `C:\src\Antiphon`, user-secrets, `pwsh -NoProfile -File scripts/restart-apphost.ps1`) | production-enable | `curl https://antiphon.desktop.codeperf.net/api/version` = HEAD; `curl .../api/session-runners/server2/status` | V-13 | `dispatchEligible: true` within 120 s of the restart; version sha = HEAD | 15 |
| CP-6b | S7 | `tests/Antiphon.Tests -> bin-c604/` (Windows) | epoch-agreement-guard | `/*/*/PhoneHomeEpochAgreementTests/*` | G-41 | 2 executed, 0 failed | 4 |
| CP-7 | all A | CP-6a | server2-raw-session | `pwsh -NoProfile -File scripts/c590-real.ps1 -Case parent-native-and-command-session` | V-14 | 1 case accepted; marker within 60 s | 5 |
| CP-8 | all A | CP-6a | server2-session-nested-stack | `... -Case session-nested-stack` then `... -Case await-nested-stack` repeated until not `SessionStillRunning` (each call under 9 minutes, at least 60 s apart; count the calls) | V-15..V-19 | launch accepted; final await accepted plus 9 subordinate receipts `accepted: true`; `db-fixture` TRX `failed=0` | 75 |
| CP-9 | all A | CP-5 | server2-nested-residue | `... verify-docker-stack.ps1 -Case nested-residue ...` | V-20, R-4 | 1 case accepted | 3 |
| CP-10 | all A | CP-5 | server2-persistent-restart | `... -Case persistent-restart ...` | V-21 | 1 case accepted; same `runnerStoreId` | 8 |
| CP-11 | all A | CP-6a | server2-session-git-smoke | `... c590-real.ps1 -Case session-git-smoke` | V-22 | 1 case accepted; branch pushed then deleted | 6 |
| CP-12 | all A | CP-6a, D-16 Grok store provisioned | server2-remote-task | `... c590-real.ps1 -Case remote-task-roundtrip` (dispatches `delegate.ps1 -Runner server2 ...`, awaits settlement in bounded calls, lands, verifies retirement) | V-24..V-27 | task `Succeeded`, desktop worktree at pushed sha, land `HasPublication`, mirror removed; or the row records `provider_sign_in_required` and stays red | 40 |
| CP-13 | all A | CP-5 | server2-handoff | `... -Case server2-independent-handoff ...` | V-23 | 1 case accepted | 4 |
| CP-13a | S6 | CP-1 | docs-guards | `/*/*/DockerStackDocumentationTests/*` | R-6 | 4 executed, 0 failed | 2 |

Cut B:

| CP | After | Build | Group | Filter | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|
| CP-14 | S9-S10 | Docker Desktop image from Cut B HEAD | containment-local | `pwsh -NoProfile -File scripts/verify-card0604-dind-runner.ps1 -Image antiphon-session-testing:c604-<sha12> -Build -Containment -Checkout <path>` | V-28 (v2), V-30 (local) | `containment=ok`; shard 0 failed | 40 |
| CP-15 | S9 | server2 image at Cut B HEAD (`deploy-parent` rerun) | containment-server2 | `... verify-docker-stack.ps1 -Case custody-containment ...` | V-28 (v1) | 1 case accepted | 8 |
| CP-16 | all B | CP-15 | server2-custody-advertised | `... -Case linux-custody ...` then `... -Case persistent-restart ...` | V-29, V-33 (live), R-5 | both accepted; backend `linux-cgroup-v1`; store id stable | 12 |
| CP-17 | S10 | CP-15 | server2-linux-custody-shard | `... c590-real.ps1 -Case session-linux-custody` (a Raw session running the `linux-custody` `dotnet-filter` shard) plus `await` | V-30, R-8..R-12 | shard receipt `accepted: true`, all listed methods executed, 0 failed | 30 |
| CP-18 | all B, landed | production at Cut B L, Grok store provisioned | server2-mutation-roundtrip | `... c590-real.ps1 -Case mutation-roundtrip` (commissions a SourceLanding Mutation with `-Runner server2` against L with the fixture PC set, awaits settlement, runs `-CleanupVerification`) | V-31, V-32, R-13..R-15 | task settled with the fixture battery green; receipt imported `Exited`/`CgroupProcsEmpty`/0; cleanup `residue: null`; `docker info` denied inside | 60 |
| CP-19 | S10-S11 | `Antiphon.Tests`, `Antiphon.SessionRunner.Tests`, `Antiphon.PtyHost.Tests` -> `bin-c604/` | custody-guards | `/*/*/(SourceLandingAdmissionTests*)\|(VerificationCleanupServiceTests*)\|(RemoteVerificationWorkspaceTests*)\|(VerificationReceiptPolicyTests*)\|(CustodyHelperContractTests*)/*`; `/*/*/(RunnerCustodyLedgerBackendTests*)\|(LinuxCustodyProbeTests*)\|(PhoneHomeCommandDispatcherTests*)\|(RunnerWorkspaceServiceTests*)/*`; `/*/*/CustodyReceiptBackendTests/*` | V-33 (local), R-11, R-12, R-16, G-28..G-40 | all listed, 0 failed (>= 40 executed across the three) | 20 |
| CP-20 | S12 | CP-19 | docs-and-fence-guards | `/*/*/(DockerStackDocumentationTests*)\|(RemoteScriptContractTests*)/*` | R-6 | all listed, 0 failed | 3 |

#### CP-6a result (2026-09-23, first run) - RED, V-13 not met

Production enablement itself is **done and live**: the ten `PhoneHomeRunner:*` keys are set in the
desktop `antiphon-server` user-secrets store (NOT the AppHost store - the AppHost forwards only
`AntiphonMessaging:BootstrapServers`, so `PhoneHomeRunner:*` there would have been inert), the
`SharedSecret` matches server2's `secrets/phone-home` by digest, and `restart-apphost.ps1` returned
exit 0 at `a808bcfc` with `/api/version` equal to HEAD. Server2 now completes the **registration**
leg: `POST /api/session-runners/register` returns 200 and the directory records
`runnerStoreId=f519bd08-...`, `processBootId` per container boot, where before enablement it was a
15 s 409 `phone_home_disabled` loop.

V-13 still fails at the **connect** leg: `available`/`dispatchEligible` stay false. Root cause is
outside `PhoneHomeRunner` settings - `antiphon.desktop.codeperf.net` reverse-proxies to
`host.docker.internal:17203` (the Vite client), not 17202, and `client/vite.config.ts` set `ws: true`
on `/hubs` only. `GET /api/session-runners/{id}/connect` therefore reached Kestrel stripped of its
hop-by-hop upgrade headers and was refused 409 `phone_home_websocket_required` ("WebSocket upgrade
is required"), while the same request straight to `http://localhost:17202` returned 409
`phone_home_invalid_ticket` - i.e. the upgrade was recognised. D-2's "Caddy passes the upgrade"
measurement was taken against a path that does not include 17203.

Fix committed as `8ef1c647` (`ws: true` on the `/api` proxy, plus
`client/src/viteProxyConfig.test.ts`, 6/6). A scratch Vite on 17293 carrying the patched config
returned 409 `phone_home_invalid_ticket` for the same upgrade, against 17203's refusal - so the
one-line change is the whole gap. **CP-6a must be re-run after that commit lands** and the main
checkout's client is rebuilt/restarted; only then is V-13 answerable.

#### CP-6a result (2026-09-23, second run) - STILL RED on V-13; new root cause found and fixed

The 17203 proxy gap from the first run is **closed and live**: `8724e763` is in the running server
(`/api/version` = `19b380d3` = main-checkout HEAD), and a WebSocket upgrade through
`https://antiphon.desktop.codeperf.net/api/session-runners/server2/connect` now reaches Kestrel
intact - it is refused on the *ticket*, not with `phone_home_websocket_required`. server2's runner
completes the handshake: the server holds a live connection with `platform: linux`,
`runnerStoreId=f519bd08-e53a-47d1-adb1-2ab33475446f` and a per-boot `processBootId`.

V-13 still fails, on a **second, independent defect** underneath the first. `available` decays to
false within the 90 s lease and `dispatchEligible` is never true, because **the two sides numbered
the connection epoch independently**. Every frame carries an `Epoch`, and BOTH receive loops
`continue` past any frame whose epoch does not match their own:
`PhoneHomeLiveConnection.ReceiveLoopAsync` and `PhoneHomeConnectionService.ReceiveLoopAsync`. The
server counted accepts since *server* boot (`++_epoch` in `AcceptConnect`); the runner counted
successful connects since *runner* boot (`Interlocked.Increment(ref _epoch)` after `ConnectAsync`).
Nothing on the wire carried the server's number to the runner. The counters therefore agreed only
when each process happened to have made the same number of connections since its own boot, and a
restart of either one alone desynchronised them for good.

The failure is completely silent. The socket opens and stays open, nothing is logged on either
side, and:
- heartbeats are discarded, so `LastHeartbeatUtc` freezes at the accept instant and `available`
  decays on the lease;
- the recovery pump's catch-up `List` request is discarded by the runner, so `MarkRecovered` never
  runs and `dispatchEligible` stays false forever;
- the runner never errors, so it never reconnects - it simply sits there looking healthy.

Measured live against the production server on 2026-09-23, same code path, only the stamped epoch
differing (server counter at 9 / 11, a freshly-booted runner stamping 1):

| epoch stamped | available | dispatchEligible | lastHeartbeatUtc |
|---|---|---|---|
| `1` (what the deployed runner sends) | true, decaying | **false** | frozen at accept |
| the server's own epoch | true | **true** | advancing |

Ruled out on the way: Caddy (`reverse_proxy host.docker.internal:17203`, WebSocket-native) and the
Vite proxy. A scratch echo server behind both `vite preview` (the mode 17203 actually serves in)
and `vite` dev round-tripped WebSocket *frames*, and the defect reproduces identically straight to
Kestrel on 17202 with Caddy and Vite out of the path entirely. server2's own `/proc/net/dev`
confirmed the runner really does send a heartbeat every 15 s and that the desktop TCP-ACKs them -
they were being dropped in the server's receive loop, not lost in transit.

Fix: `9acac1c4`. The epoch is minted in `Register` (not `AcceptConnect`), carried on the ticket,
returned to the runner as `PhoneHomeRegistrationResponse.Epoch`, and adopted by the runner in place
of its local counter. The server stays the single authority and every existing epoch check keeps
its meaning instead of holding by coincidence.

Why no test caught it: `PhoneHomeTestHost.ConnectPeerAsync` sets `peer.Epoch = live.Epoch`, reading
the server's epoch through an in-process back door the real runner does not have, so every scripted
peer agreed with the server by construction. `52bda1e8` adds
`tests/Antiphon.Tests/Agents/PhoneHomeEpochAgreementTests.cs`, which drives the real
`PhoneHomeConnectionService` over a real socket against a server whose counter has been advanced
past a fresh runner's, with a non-vacuity guard (`live.Epoch > 1`) so it cannot pass by coincidence.
Red/restore/green recorded against the runner's stamping line: RED 1/0/1, GREEN 2/2/0.

**CP-6a stays RED and is NOT answerable from a Code stage.** V-13 needs the fix on BOTH sides, and
the two sides ship separately:
1. land `9acac1c4`+`52bda1e8`, `git pull --rebase` in `C:\src\Antiphon`, `restart-apphost.ps1`;
2. rebuild and redeploy server2's runner image at that sha - `c590-real.ps1 -Case deploy-parent`
   (CP-5), because the deployed image `antiphon-server2/session-testing:965703e65aa4` still carries
   the old runner-local counter;
3. then re-run CP-6a.
Until step 2, server2 keeps stamping `1` and V-13 cannot go green no matter what the server does.

#### CP-6a result (2026-09-23, third run) - GREEN. V-13 MET.

The epoch fix (`9acac1c4`+`52bda1e8`) landed as `6d90c6fc` on `master` and is live on production
(`GET /api/version` = `6d90c6fcf46e214721a665d94f5cddb61d75804a`). Step 2 of the second run's
handoff - rebuilding server2's runner image so BOTH sides carry the fix - is what this run did,
and V-13 went green immediately afterwards.

**CP-5 rerun at `6d90c6fc`** (`pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case
deploy-parent -Manifest .antiphon/c604-checkpoints/manifest-cp5.json`): `accepted: true`,
`exit 0`, diagnosis empty. The remote checkout detached to `6d90c6fc`,
`antiphon-server2/{server,session-testing}:6d90c6fcf46e` built, `compose up -d --no-build`
replaced the runner, and D-5 retired the superseded `965703e65aa4` pair. The D-2 probes all pass
rather than being excused: `phone-home-secret-path` readable as uid 1654
(`phone-home-readable=true`), server state `enabled`, and **`phone-home-failures = 0`** in the
45 s window - where the enablement round had a 15 s failure loop. V-11/V-12 re-confirmed at the
new sha: `restart-policy=unless-stopped`, `privileged=true`, no `docker.sock` in `mounts.txt`,
nested `docker info` `Name` = container hostname (`649394c67e04`), `services` = `session-runner`
only, and all 32 foreign neighbours (`am-service`, `schoolrevision-*`, `windmill-*`, `gym-stat-*`,
`traefik-*`, `antiphon-messaging_*`) still up in `foreign-after.txt`.

**V-13, measured.** 12 samples 30 s apart from the desktop over 5 m 33 s - 3.7x the 90 s lease,
the window in which the old build's `available` decayed:

| field | every sample, 13:05:56 -> 13:11:29 UTC |
|---|---|
| `available` | `true` (never decayed) |
| `dispatchEligible` | **`true`** (was permanently false) |
| `platform` | `linux` |
| `runnerStoreId` | `f519bd08-e53a-47d1-adb1-2ab33475446f` - the custody-store id, **unchanged across the image replacement** |
| `lastHeartbeatUtc` | advancing on a clean 30 s cadence: `13:05:50 -> 13:06:20 -> 13:06:50 -> ... -> 13:11:20`, never frozen |
| `buildVersion` | `6d90c6fcf46e214721a665d94f5cddb61d75804a` |
| `disconnectReason` | `null` |
| `epoch` / `processBootId` | constant at `2` / `7edbedda-...` - ONE connection held for the whole window, not a reconnect loop wearing a healthy face |

The constant `epoch`/`processBootId` is the non-vacuity guard on this measurement: a runner that
was silently dropping frames and reconnecting would churn both. It did neither, and
`docker logs --since 15m` on the runner matched `reconnect|Unauthorized|epoch|error|exception`
**0 times**.

V-13's second leg - "the same status from the desktop and from server2's host" - holds: three
probes run from server2's own shell (13:11:37, 13:12:12, 13:12:47 UTC) return byte-identical
fields with the heartbeat still advancing. Container:
`antiphon-runner-session-runner-1  antiphon-server2/session-testing:6d90c6fcf46e  Up 8 minutes (healthy)`.

**CP-6b** was added to the table in this run. `52bda1e8` created
`PhoneHomeEpochAgreementTests` but the manifest carried no row for it, so the guard on the very
defect CP-6a proves had no checkpoint. G-41 is that guard: the real `PhoneHomeConnectionService`
over a real socket against a server whose counter has been advanced past a fresh runner's, with
the `live.Epoch > 1` non-vacuity assertion.

CP-6a is closed. CP-7, CP-8, CP-11 and CP-12, all of which were blocked on `dispatchEligible`,
are now unblocked.

Rules: TUnit rows are `run-checkpoint.ps1` rows into `.antiphon/c604-checkpoints/`; the others are
the non-TUnit exact-command form, one case receipt each. CP-5, CP-6a and CP-15 change standing
state (server2, production) and run once per frozen sha; a red server2 row is fixed and rerun as
the same row. CP-18 runs only after Cut B is landed and CP-6a re-run at L (the Mutation targets L).
If a combined class filter does not select on the pinned TUnit, split the row sharing the build.
Delete every `bin-c604` directory, the harness container and volume, and no server2 resource
other than the run's `c604<run>` residue, before settling. `Antiphon.Tests` rows here need no
Postgres.

### Cost

Estimated, not measured; nothing ran in this Plan.

| Cut A ordinary V/R | Minutes |
|---|---:|
| CP-1 contract-guards | 12 |
| CP-2 command-guards | 8 |
| CP-2a phone-home guards | 10 |
| CP-2b runner guards (second project build) | 8 |
| CP-3 harness-contract | 3 |
| CP-4 dind-local | 25 |
| CP-5 server2-deploy | 30 |
| CP-6 testing-payload | 4 |
| CP-6a production enable (pull, secrets, restart, confirm) | 15 |
| CP-7 raw-session | 5 |
| CP-8 session-nested-stack | 75 |
| CP-9 nested-residue | 3 |
| CP-10 persistent-restart | 8 |
| CP-11 session-git-smoke | 6 |
| CP-12 remote-task-roundtrip | 40 |
| CP-13 handoff | 4 |
| CP-13a docs-guards | 2 |
| **Cut A V/R** | **258** |

Cut A authoring: S1 120, S2 50, S3 150, S4 170, S5 120, S6 60, S7 180, S8 240 = 1,090.
`-ExpectAbout 1350`, reported as a band (1,150-1,600): CP-8's nested pulls and the first
production registration are the least predictable items.

| Cut B ordinary V/R | Minutes |
|---|---:|
| CP-14 containment-local (image rebuild + shard) | 40 |
| CP-15 containment-server2 (deploy rerun + case) | 8 + 30 deploy |
| CP-16 custody advertised + restart | 12 |
| CP-17 linux-custody shard in-runner | 30 |
| CP-18 mutation-roundtrip (post-land) | 60 |
| CP-19 custody-guards (three builds) | 20 |
| CP-20 docs guards | 3 |
| **Cut B V/R** | **203** |

Cut B authoring: S9 150, S10 300, S11 300, S12 60 = 810. `-ExpectAbout 1010`, band (900-1,300).

Post-land Mutation floors (Windows, method-scoped): Cut A PC-1..PC-27 = 27 x 4 + 6 = **114**;
Cut B PC-28..PC-41 = 14 x 4 + 6 = **62**, plus CP-18's server2 fixture battery (PC-1, PC-9, PC-29:
3 x 6 in-runner build cycles = 18, inside CP-18's 60).

Handoff audit: bodies read; guards=41, mapped=41, missing=0, duplicate PC mappings=0; every PC is
a compiling text, script or unit defect with an exact method and assertion; numeric Cost above.

## Risks

- **Caddy and long-lived WebSockets.** The upgrade reaches Kestrel (measured); idle timeouts on the
  Caddy side are not measured. The 15 s heartbeat (CARD-0490 D-5) is well inside any default; if
  Caddy closes at 60 s idle anyway, CP-6a shows reconnect churn in the runner log and the fix is a
  `reverse_proxy` stream timeout on the existing vhost (proxy skill), a one-line change recorded in
  the plan amendment.
- **Production enablement is a real change to the running server.** CP-6a follows the
  `restart-apphost.ps1` runbook from the main checkout after `git pull --rebase`; the exit-5
  sha check protects against serving stale code (CARD-0358). The user-secrets values are set by the
  operator, never by a script.
- **Grok on server2 needs a login.** D-16 is an operator step; CP-12 and CP-18 record
  `provider_sign_in_required` honestly when it has not happened. No metered fallback.
- **Legacy iptables on Docker Desktop.** If step 4 of the entrypoint fails on the desktop only, CP-4
  records `IptablesUnusable` and Code adds a desktop-only opt-out; server2 stays pinned.
- **`ssh.github.com:443` from inside the nested netns.** Measured from server2's host, not from a
  nested container; CP-11 is the measurement; fallback is port 22 (one line, one known_hosts entry).
- **cgroup v1 delegation inside a privileged container.** `/sys/fs/cgroup/pids` and `freezer` are
  mounted rw in privileged mode and the entrypoint runs as root, so creating `antiphon-custody`
  should work; if the mount is read-only on server2's Docker 24, the fallback is `mount -o remount,rw`
  of those two mounts in the entrypoint (privileged) and V-28 records which branch ran.
- **`cgroup.kill` needs kernel 5.14.** Docker Desktop's kernel is 6.x; server2's 4.15 uses the v1
  freezer branch. The helper branches on the file's presence, not the version string.
- **`sudo` under a pty.** `sudo -n` with `NOPASSWD` needs no tty; if sudo's `use_pty` default
  interferes with Porta's pty, the sudoers file sets `Defaults!/usr/local/bin/antiphon-custody-enter !use_pty`.
- **Same-uid observer.** The pty-host and the child are both 1654; the containment relies on the
  root-owned cgroup and `no_new_privs`, both measured in V-28 before any Mutation is admitted.
- **Nested store growth.** First CP-8 pulls about 4 GB into `dind-data`; `deploy-parent` refuses
  below 15 GB free (`DiskLow`).
- **`stop_grace_period` and nested containers.** A session mid-run is lost on `docker stop`; a
  tracked session becomes `Unknown` with residue, never a receipt.
- **Host daemon restart.** `LiveRestoreEnabled=false` on server2; `restart: unless-stopped` brings
  the project back; open tracked executions become `Unknown`.
- **The long rows are several tool windows long.** S4 splits launch from await; every await call is
  bounded at 8 minutes and returns a retryable non-result; the session runs unattended regardless.
- **Two cuts, one document.** Cut B's brief must start from Cut A's landed L and CP-6a evidence;
  the plan says so in D-14, and the `--- next stage ---` handoff for Cut B is written by Cut A's
  Review, not assumed here.

## Not done, noted

- **CARD-0605**: the persistent runner is the Linux execution lane; V-30 runs the custody and
  launcher classes there. Extending to every `RequireLinux()` class is a roster addition on that
  card.
- **CARD-0590 roster**: `-Group backend` through the nested lane; receipt/recovery cases on the
  nested daemon.
- **`gh` in the image** if a future case needs the GitHub API from a session.
- **A second runner id / fleet placement**: not this card (CARD-0490 D-1).
- **The rdkafka log flood** (`localhost:19092` every 30 s) the investigation noted; a separate
  small card.
- **`c590` naming**: cosmetic rename to `stack-*` is separate.
- **Existing `throwaway/c590-credential-smoke-3537de6b7be4` branch** on GitHub: delete by hand.
- **Claude/Codex on server2**: the same D-16 custody question, one card each when wanted.
