# CARD-0604: persistent server2 runner with a nested Docker daemon

Date: 2026-09-22. Stage: Plan, with TestDesign folded (D-11). Plan task `3b9af4e6`.
Source baseline: `e70f7e0d` (master; CARD-0594's three launch fixes landed and confirmed live).
Investigation: [2026-09-22-card-0604-persistent-runner-dind-rework.md](../../investigations/2026-09-22-card-0604-persistent-runner-dind-rework.md)
(task `51bcc470`, carried onto this branch verbatim from `77647ce0`).
Supersedes: `docs/docker-stack.md` paragraph 2 (the socket-mount model) and CARD-0590 plan
decision D-6 (sibling containers, "reject DinD, privileged services"). Both are re-aimed in S6,
not deleted.
**Next: decide** (D-13 lists the defaults; D-2 and D-8 are the operator's to confirm), then Code
directly: the verification design and checkpoint table are in this document.

## Outcome and scope

Turn CARD-0590's run-scoped sibling stack on server2 into the shape the card directs:

1. A **persistent** session-runner container on server2 (fixed Compose project, restart policy,
   stable image tags, owned named volumes) that runs its **own `dockerd`** inside itself
   (privileged, operator decision 2).
2. A session launched in that runner by the server2 control plane runs the ordinary test entry
   point, which stands up a **nested throwaway Antiphon stack** (server + runner + Postgres) and
   the Testcontainers fixtures on that nested daemon, exports evidence, and tears down only what it
   created. Mapped ports land on the runner's own loopback, so Testcontainers works unmodified.
3. A durable, repo-scoped GitHub push credential for that unattended runner (operator decision 3,
   designed here as D-8 and brought back for review before Code).
4. The image packaging the nested daemon needs beyond the tarball the Dockerfile already fetches
   (D-4), measured against server2's kernel 4.15 / cgroup v1 / legacy-iptables host.

Out of scope: SourceLanding Mutation on Linux (CARD-0598; the `linux-custody` and
`testing-runner-payload` fences are kept verbatim, D-12), the CARD-0590 Medium roster beyond the
one database-fixture shard this plan runs to prove the Testcontainers path (the full roster keeps
its CARD-0590 ownership and now has a working lane), CARD-0590's receipt/recovery cut cases
(S6c/S6d), Grok credential provisioning on server2 (CARD-0575 no-baked-auth stands; the
acceptance session is Raw `/bin/sh`), the `RequireLinux()` test lane (CARD-0605; this runner is
its natural host), sysbox/rootless (investigation Options 3 and 4), and any change to
`PtyHost`/launcher code (CARD-0594 is landed; this plan only re-runs its live confirmation).

No build, test, container or push of product code ran in this Plan. Server2 was read
(read-only `docker ps`/`volume ls`/`info`/`df`, `iptables --version`, `uname -r`) on 2026-09-22
to refresh the investigation's measurements; nothing on server2 was changed.

## Ground truth

| Card / brief assumption | What the code and the hosts say at `e70f7e0d` | Consequence |
|---|---|---|
| CARD-0594 is landed, so sessions launch in the Linux runner. | The three fixes are on master (`6c525b62`, `ad6cf8bd`, `27233eca`). The stack still up on server2 (`c5903537de6b7be4-*`, 17-18 h old on 2026-09-22) was built from `feat/card-task-dae3ad6b`, before those commits; no image on server2 contains the fix and `parent-native-and-command-session` has not been re-run since. | CP-5 rebuilds from HEAD; CP-7 is the first live re-run of the Raw session and is the CARD-0594 confirmation on server2, before any nested work is attempted. |
| `docker-27.5.1.tgz` at `docker/session-runner-grok/Dockerfile:64` is already downloaded. | Yes, but `tar ... --strip-components=1 docker/docker` extracts only the CLI. The investigation's `tar -tz` shows the same tarball carries `dockerd containerd containerd-shim-runc-v2 runc docker-init docker-proxy ctr`. The image has no `iptables`, no `iproute2`, no `openssh-client`, no `setpriv` use, no `/etc/docker/daemon.json`. | D-4: extract the whole engine from the same pinned tarball; add `iptables` (legacy pinned), `iproute2`, `openssh-client`; add `daemon.json` and the entrypoint. No second engine source. |
| server2 can run privileged DinD today. | Measured by the investigation with `docker:27-dind` (Alpine, whose entrypoint probes and falls back to `iptables-legacy`). Re-read 2026-09-22: Docker 24.0.2, `CgroupVersion=1`, `overlay2`, runtimes `runc` only, kernel `4.15.0-213-generic`, host `iptables v1.6.1` (Ubuntu 18.04 ships the legacy backend only). The runner image is Debian 12, whose `iptables` package defaults to the nft backend. | D-4 pins `iptables-legacy` via `update-alternatives` at build time and the entrypoint self-checks `iptables -nL` before starting `dockerd`, refusing with `IptablesUnusable`. Measured, not assumed, in CP-4 (Docker Desktop, cgroup v2, nft-capable kernel) and CP-5 (server2). |
| The sibling shape's Testcontainers path is broken by a localhost/netns split. | Confirmed: `grep -rn TESTCONTAINERS` finds nothing; `TestDbOperations.StartAsync` (`tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:36-43`) takes `container.Real.GetConnectionString()` verbatim; on the sibling shape `run_dotnet` (`scripts/c590-remote.sh:500-523`) ran the tests in a separate container with the host socket, so `localhost:<mapped>` pointed at the test container, not server2's loopback. Never exercised: no `dotnet-filter`/`messaging-tests` evidence directory in run `3537de6b7be4`. | D-5: no `TESTCONTAINERS_*` variable is added. The daemon shares the test process's network namespace by construction (D-1, D-6). V-6 proves it locally with a mapped port curled from `127.0.0.1` as uid 1654; V-18 proves it on server2 with real Postgres-backed tests. |
| A session in today's runner can already run `test-docker.ps1 -ThrowawayStack`. | It cannot. `scripts/c590-remote.sh` `ensure_dirs` (47-57) uses `sudo -n`, `write_result` (23-45) uses `python3`, `ensure_checkout` (59-73) clones a hard-coded branch, and every case builds and composes against the daemon it finds. The `session-testing` image has neither `sudo` nor `python3`, so the in-session path dies in `ensure_dirs` with `UnhandledExit`. Stored evidence agrees: `client-lint`/`client-tests` are `accepted: false`, the dotnet rows have no directory. | S3 makes the script lane-aware (host lane over SSH versus nested lane inside the runner), drops `sudo`/`python3` from the nested lane, and takes the branch from the bridge. |
| "There is no persistent standing runner." | A runner container is up but persistence is absent by design: no `RestartPolicy` on any of the three containers, project `c590<run>` and tags `antiphon-c590-<run>-*` are per run (`c590-remote.sh:13,16`), no unit or supervisor, and the desktop `finally` never tears it down. Live 2026-09-22: containers `c5903537de6b7be4-{antiphon,session-runner,postgres}-1` Up, `state-init` exited, seven `c5903537de6b7be4_*` volumes, eight `antiphon-c590-3537de6b7be4-*` images (about 9 GB). | S2/S4: fixed project `antiphon`, `restart: unless-stopped` for the three services, sha-tagged images plus the OCI revision label, and D-9 retires the run-scoped leftovers with a recorded inventory. |
| The git-credential smoke ports to the nested daemon. | It does not, for two measured reasons (investigation §3): nested bind sources resolve in the runner's filesystem, and a missing source silently becomes an empty directory (EISDIR, then an auth error). The token is desktop-sourced per run (`scripts/c590-real.ps1:181-194`) and removed in `finally` (228-231). Live 2026-09-22: `/home/mc/antiphon-c590/secrets/` holds only `stack.env`. | D-8: the credential lives on server2, reaches the runner as a Compose file secret, is materialised by the root entrypoint into a uid-1654 tmpfs, and git is wired through baked non-secret config. The smoke runs in the session's own shell, not through a nested bind. |
| Only `gho_` tokens are scrubbed from logs. | `scrub_file` (`c590-remote.sh:18-21`) redacts `gho_[A-Za-z0-9_]+` and `POSTGRES_PASSWORD=`; `ghp_`/`ghu_`/`ghs_`/`ghr_`/`github_pat_` pass through. With an SSH deploy key no token is ever present, but the scrub still runs on copied-out logs. | S3 widens the pattern (G-14). |
| The runner is non-root and CARD-0590 D-1 rejects supervisord. | `USER 1654:1654` closes every stage (`Dockerfile:47,57,75`); base Compose `user: "1654:1654"`; `DockerStackContractTests.Applications_share_nonroot_identity` reads base Compose only. `dockerd` needs root. | D-3: the `session-testing` stage ends `USER 0:0` with a fail-together entrypoint that starts `dockerd` as root and runs the runner as 1654 via `setpriv`; the base file stays 1654 so the existing test still holds; a new test guards the drop. |
| Disk on server2 is 56 GB free. | 55 GB free on 2026-09-22 (`/` 213 GB, 149 GB used). The nested store will hold sdk:10, aspnet:9, node:22, postgres:16, redpanda, ryuk and the built child images: about 8-10 GB. | D-9: `dind-data` is a named volume; prune inside the nested daemon is permitted; host prune stays forbidden; the c590 leftovers (about 9 GB) are retired first. |
| Compose on server2 supports what this plan uses. | Compose v2.18.1: `privileged`, `secrets` with `file:`, `tmpfs`, `stop_grace_period`, `restart`, `--remove-orphans`. Docker 24 supports `host-gateway`. | No Compose upgrade. |
| SourceLanding custody is untouched. | `case_testing_payload` (`c590-remote.sh:313-331`) asserts `! grep -a -q VerificationCustodyV1 /app/Antiphon.SessionRunner.dll`; `verify-docker-stack.ps1` `linux-custody` refuses an advertised `VerificationCustodyV1`; `RunnerCustodyLedger.PrepareStart` refuses non-Windows verification launches (CARD-0598 text). A nested daemon is still a pre-existing daemon relative to any Mutation process. | D-12: both assertions kept verbatim as the fence; nothing here relaxes admission. |
| "Nested throwaway Antiphon stack (server + Postgres)". | The base `docker-compose.yml` defines server, runner and Postgres, and the server's `depends_on` needs a healthy runner. CARD-0590's child cases build `child-server` and `child-runner`. | D-7: the nested child is the unmodified base file (three services) on the nested daemon; the child runner is the socket-free `runtime` target, so nesting stops at depth one. |

## Decisions

### D-1. `dockerd` runs inside the persistent runner container, privileged, root-then-drop

Operator decision 2, recorded: the `session-testing` runner service is `privileged: true`, starts
as root, launches `dockerd` on `unix:///var/run/docker.sock` inside its own mount and network
namespaces, then runs `Antiphon.SessionRunner` as uid/gid 1654 with the socket group set to
1654. Everything a session starts through Docker (Testcontainers Postgres/Redpanda/Ryuk, the
nested child stack, nested builds) lives on that daemon; the host daemon never sees it.

Rejected (all already measured or rejected by the investigation and the operator): a
`docker:27-dind` sidecar over TCP (the daemon is a host-daemon peer, and the test process's
localhost is still not the daemon's host); sysbox (kernel 4.15, needs an upgrade and a reboot of a
182-day host running the live gateway); rootless `dockerd` (cgroup v1 plus kernel 4.15 loses
resource limits and overlay2); keeping the host socket (the shape the card rejects, and the
source of the netns split).

### D-2. The control plane stays on server2: a persistent server + Postgres alongside the runner

The persistent Compose project `antiphon` is the three-service base stack plus the
`session-testing` and `persistent` overrides. The server2 server launches sessions into the
persistent runner over the internal route (`http://session-runner:8080`, CARD-0590 D-2). The
throwaway stack the card describes is the **nested** one a session creates; the persistent
server and Postgres are the deployment, not the ephemeral thing next to the runner.

Rejected:

- **Phone-home to the desktop production server** (`docker-compose.runner-grok.yml` shape,
  CARD-0490). CARD-0590's card records the operator's explicit choice of a fully self-contained
  stack with zero dependency on the Windows desktop over that design; it would also make the
  desktop's production server the launcher of sessions on a privileged host. It remains available
  as a later override if the operator wants the production board to dispatch to server2.
- **A runner-only persistent container.** Nothing could launch a session into it; the acceptance
  sentence needs a server.

This is a stated default (D-13) because the card does not name the control plane.

### D-3. A fail-together entrypoint, not supervisord and not `exec`

`docker/session-runner-grok/dind-entrypoint.sh` (bash, ASCII, `set -euo pipefail`), run as root
under `init: true` (tini is pid 1):

1. Refuse unless root and privileged (`CapEff` in `/proc/self/status` is the full set): exit 3,
   diagnosis `NotPrivileged`/`NotRoot` on stderr.
2. Deploy key (D-8): `ANTIPHON_DEPLOY_KEY_SOURCE` (default `/run/secrets/antiphon-deploy-key`)
   must be a regular, non-empty file; a directory (the Compose missing-bind shape) or an absent
   file is exit 3 `DeployKeyMissing`. `install -m 0400 -o 1654 -g 1654` it to
   `/run/antiphon/deploy-key` on the `/run/antiphon` tmpfs (`chmod 0700`, owned 1654). Never
   print or hash the key.
3. cgroup v2 nesting (Docker Desktop only; a no-op on server2's v1): if
   `/sys/fs/cgroup/cgroup.controllers` exists, move this process into `/sys/fs/cgroup/init` and
   enable `+cpu +memory +pids +io` in `cgroup.subtree_control` (the upstream `dind` helper's
   block, six lines, attributed in a comment).
4. `iptables -nL >/dev/null` or exit 3 `IptablesUnusable`.
5. Start `dockerd --config-file /etc/docker/daemon.json` in the background, logging to
   `/state/logs/dockerd.log`; wait up to 30 s for `docker info` to answer, else exit 3
   `NestedDaemonUnavailable`.
6. `export HOME=/home/app`; start the runner as
   `setpriv --reuid=1654 --regid=1654 --clear-groups "$@"` in the background (`"$@"` is the
   image `CMD`, `dotnet Antiphon.SessionRunner.dll`).
7. `trap` TERM/INT to forward TERM to both children; `wait -n`; record which child exited and
   its status; TERM the other, wait for it, exit with the first status. The container is the
   unit: a dead daemon takes the runner down and `restart: unless-stopped` brings both back.

Rejected: supervisord (CARD-0590 D-1's rejection stands; it also hides a dead daemon behind a
healthy container); `exec` into the runner after forking `dockerd` (the daemon becomes an
unsupervised child of the runner; a dead daemon leaves a healthy-looking runner whose every
`docker` call fails); starting `dockerd` from a Compose sidecar (D-1).

### D-4. Engine packaging: the pinned tarball, whole, plus legacy iptables, on a named volume

In the `session-testing` stage (`docker/session-runner-grok/Dockerfile`, the same `RUN` that
today fetches the CLI):

- Extract from `docker-27.5.1.tgz`: `docker dockerd containerd containerd-shim-runc-v2 runc
  docker-init docker-proxy ctr` into `/usr/local/bin` (one `curl | tar` as today, eight paths
  instead of one).
- `apt-get install --no-install-recommends iptables iproute2 openssh-client` (`util-linux`,
  which provides `setpriv`, is already in the base), then
  `update-alternatives --set iptables /usr/sbin/iptables-legacy` and the same for `ip6tables`.
  Reason: server2's kernel is 4.15 and its own iptables is 1.6.1 legacy; the nft backend of
  Debian 12's iptables 1.8.9 is unreliable on kernels that old, and mixing backends across the
  two layers is the classic DinD failure. Pinned at build so every host runs the same backend;
  the entrypoint check (D-3 step 4) turns a wrong guess into a named refusal, not a hang.
- `/etc/docker/daemon.json` (new file `docker/session-runner-grok/daemon.json`): `hosts`
  `unix:///var/run/docker.sock`, `group` `app` (gid 1654), `data-root` `/var/lib/docker`,
  `storage-driver` `overlay2`, `bip` `10.200.0.1/24`, `default-address-pools`
  `[{base: 10.201.0.0/16, size: 24}]` (never overlapping the host's 172.16/12 pools the runner is
  attached to), `log-level` `warn`, `live-restore` `false`, `iptables` `true`.
- `/var/lib/docker` is the named volume `dind-data` (host ext4, not overlay-on-overlay). The
  nested image store survives container recreation and daemon restarts.
- Toolchain for in-runner builds and tests (D-6): `COPY --from=build /usr/share/dotnet
  /usr/share/dotnet` (the `build` stage is `sdk:10.0`; merging onto the runtime-9 tree gives SDK
  10 plus runtimes 9 and 10) and `COPY --from=node22 /usr/local /usr/local` (new
  `FROM node:22-bookworm AS node22`, the recipe `docker/tests/Dockerfile:5,13` already uses).
  Build-time assertions: `dockerd --version`, `runc --version`, `iptables --version` contains
  `legacy`, `dotnet --list-sdks` has a `10.` line, `dotnet --list-runtimes` has both
  `Microsoft.AspNetCore.App 9.` and `10.`, `node --version`, `ssh -V`.
- Stage ends `ENTRYPOINT ["/usr/local/bin/antiphon-dind-entrypoint.sh"]`,
  `CMD ["dotnet", "Antiphon.SessionRunner.dll"]`, `USER 0:0`. `runtime` (default/final) and
  `receipt-probe` are untouched: their closures gain no engine, no SDK, no Node (R-7).

Rejected: Docker's apt repository (`docker-ce`): a second engine source and a second pin;
`--iptables=false`: nested containers lose NAT, and the measured `NESTED_GIT_CLONE_OK` egress
depends on it; runtime probe-and-switch of the iptables backend like `docker:dind`: it hides
which backend is in play; `/var/lib/docker` in the writable layer: overlay-on-overlay was only
smoke-tested and every image would be re-pulled after recreation; a separate test image as the
only test lane (D-6).

### D-5. Testcontainers is unmodified; the preflight refuses a sibling daemon

No `TESTCONTAINERS_*` variable, no `.testcontainers.properties`. Inside the runner the default
endpoint is `unix:///var/run/docker.sock` (the nested daemon, group 1654) and
`GetConnectionString()`'s host is `localhost`, which is the runner's own loopback where the nested
daemon binds mapped ports. Ryuk starts on the nested daemon and its socket bind resolves inside
the runner, correctly.

`scripts/test-docker.ps1` gains one preflight before any command: the daemon's
`docker info --format '{{.Name}}'` must equal the process's own `hostname`; a mismatch is
`SiblingDaemonRefused` (exit 2, no command emitted), an unreachable daemon is
`NestedDaemonUnavailable`. That single comparison is the difference between the rejected shape
(`Name` = `server2`) and this one (`Name` = the runner container's hostname).

Rejected: `TESTCONTAINERS_HOST_OVERRIDE` on the sibling shape (it papers over the split and the
card rejects that shape).

### D-6. Sessions run tests directly in the runner; the sibling test container is retired as a lane

The session's shell builds and runs the roster in `/work/repos/antiphon` with
`scripts/run-checkpoint.ps1` and `--property:OutputPath=bin-c604/`, NuGet at `/work/.nuget`,
npm in the checkout's `client/node_modules`; `SessionRunner__BaseUrl=http://127.0.0.1:1` and
`SessionRunner__Enabled=false` stay (CARD-0590's refusing-client guard). This is what "mirror a
real session" means: an agent in a worktree running the test entry point, with Docker underneath
it. `docker-compose.test.yml` and `docker/tests/Dockerfile` are kept for the nested daemon
(`delivery-fixture` still builds there for CARD-0590's receipt cases) and documented as
nested-only; the `tests` service keeps `privileged: false` and its socket line now names the
nested socket.

Rejected: a test container on the nested daemon with `network_mode: host` (works, but adds a
second lane whose only purpose is to avoid putting the SDK in the opt-in image, and real agent
sessions need the SDK anyway).

### D-7. The nested child is the unmodified base stack, depth one

Inside the session, project `c604<run>` on the nested daemon is `docker-compose.yml` plus a
script-written `compose.child.yml` (sha-tagged child images, labels `c604-run=<run>`,
`c604-owner=child`, a per-run `ANTIPHON_BIND_PORT` on the runner's loopback). The child runner is
the `runtime` target: no engine, no socket, no SDK, so the nesting cannot recurse (V-15 live
assertion `test ! -e /usr/local/bin/docker` in the child runner, kept from CARD-0590).

### D-8. GitHub credential custody: a repo-scoped write deploy key that lives on server2

**Proposed for operator review (card decision 3).**

- **Generate on server2, never copy the private half.** The host-lane `deploy-parent` case runs
  `ssh-keygen -t ed25519 -N '' -C antiphon-server2-runner -f /home/mc/antiphon-server2/secrets/deploy_key`
  when the file is absent (mode 0600, owner `mc`). The public half is exported with the evidence.
- **Register from the desktop bridge, idempotently.** `scripts/c590-real.ps1`, after
  `deploy-parent` returns, runs `gh repo deploy-key list --json title` and, if
  `antiphon-server2-runner` is absent, `gh repo deploy-key add <pub> --allow-write --title
  antiphon-server2-runner`. The desktop's `gh` login is already the credential that
  authorises this; nothing of it travels to server2.
- **Deliver as a Compose file secret.** `docker-compose.session-testing.yml`: `secrets:
  antiphon-deploy-key: file: ${ANTIPHON_DEPLOY_KEY_FILE:?ANTIPHON_DEPLOY_KEY_FILE is required}`
  and the runner service lists it; `stack.env` carries the path. A missing variable refuses
  `up`; a missing file is caught by D-3 step 2 (`DeployKeyMissing`), which is the exact failure
  the investigation showed becomes EISDIR otherwise.
- **Materialise for the app uid** (D-3 step 2): `/run/antiphon/deploy-key`, 0400, uid 1654, on
  a tmpfs. Every session (uid 1654) can read it; sessions are trusted, as on the desktop.
- **Wire git through baked non-secret config**, so it works in any session regardless of how
  the pty child's environment is composed: `/etc/gitconfig` (new
  `docker/session-runner-grok/gitconfig`) sets `core.sshCommand = ssh -F /etc/antiphon/ssh_config`
  and `url."git@github.com:".pushInsteadOf = https://github.com/`; `/etc/antiphon/ssh_config`
  (new file) pins `Host github.com` to `HostName ssh.github.com`, `Port 443` (TLS egress on 443
  is measured; port 22 is not), `User git`, `IdentityFile /run/antiphon/deploy-key`,
  `IdentitiesOnly yes`, `BatchMode yes`, `StrictHostKeyChecking yes`,
  `UserKnownHostsFile /etc/antiphon/github_known_hosts` (new file with GitHub's published
  ed25519/ecdsa/rsa host keys, which `ssh.github.com` also presents). Fetches stay anonymous
  HTTPS; only pushes go over SSH.
- **Smoke** (`git-credential-smoke`, nested lane, S3): in the session's shell, clone anonymously,
  branch `throwaway/c604-credential-smoke-<run>`, commit, `git push origin HEAD:<branch>`
  (rewritten to SSH), `git rev-parse HEAD`, then `git push origin --delete <branch>` so no
  residue accumulates (investigation §3, low). Record `pushed-sha.txt`, `branch.txt`,
  `deleted.txt`, and the `ssh -T git@github.com` banner (`successfully authenticated`, exit 1).
- **Custody paragraph** in `docs/agent-credentials.md` §5 next to the nightly watchdog's: names
  and locations only, no values; the key never enters Antiphon's stores; revocation is deleting
  the deploy key on GitHub, rotation is regenerating and re-registering.

Rejected:

- **Fine-grained PAT** (Contents: write on one repo): expires within a year, so an unattended
  runner breaks on a date nobody is watching; acts as a person's account; its only advantage
  (`gh` API access) is unused, the image has no `gh`.
- **Antiphon API-key store** (`{{key:NAME}}`, `docs/agent-credentials.md` §3): the right future
  home, but CARD-0590 recorded that Linux managed secrets need the X509 key-ring protector
  (`AgentTui__KeyProtection__Mode=X509Certificate`), which server2 does not have; it would also
  deliver the value only to server-launched sessions, not to the runner's own git.
- **GitHub App installation token**: needs a minting service; disproportionate.
- **Desktop-sourced per-run token** (today): no unattended story; the finding that raised this.
- **Baking anything into the image** (CARD-0575) or a raw host bind mount (silently a directory
  when missing).

### D-9. Cleanup and disk policy

- Prune **inside** the nested daemon is permitted and documented (`docker system prune` there
  touches only this runner's store). Prune on the **host** daemon stays forbidden (CARD-0590
  acceptance 7; `am-service`, `traefik`, `windmill`, `schoolrevision-*` share it).
- `dind-data` is the one volume an operator may wipe to reclaim space
  (`docker compose -p antiphon stop session-runner && docker volume rm antiphon_dind-data`);
  `pgdata`, `server-state`, `runner-state`, `work` keep CARD-0590's explicit-destroy rule.
- `deploy-parent` retires the CARD-0590 run-scoped leftovers once: every Compose project
  matching `^c590[0-9a-f]{12}$` (`down -v --remove-orphans`) and every image tagged
  `antiphon-c590-*`, after writing the inventory (`docker ps -a`, `volume ls`, `images`) to the
  evidence directory. Nothing else on the host is touched (V-12 asserts the foreign containers
  are still Up afterwards). This is a stated default (D-13); the stack is the investigation's
  "leftover parent stack ... not a deployment".

### D-10. Supersession, re-aimed not deleted

- `docs/docker-stack.md`: paragraph 2 rewritten (S6). The sentence "`docker-compose.session-testing.yml`
  is the only application override that mounts the socket" becomes: no application service ever
  mounts a socket; the testing runner is privileged and owns a nested daemon; `DOCKER_SOCKET_GID`
  is gone.
- CARD-0590 plan D-6: a one-line annotation appended in place ("Superseded 2026-09-22 by
  CARD-0604 D-1/D-4/D-5: the ordinary testing runner owns a nested daemon; the host socket mount,
  the sibling model and the reject-DinD clause are retired"). The historical text stays.
- Contract tests: `Testing_runner_has_explicit_socket` becomes `Testing_runner_owns_its_daemon`
  (override has `privileged: true` and `dind-data:/var/lib/docker`; zero `docker.sock` lines in
  base plus override); `Testing_services_are_unprivileged` is kept for `docker-compose.test.yml`
  and joined by `Only_the_testing_runner_is_privileged` (exactly one `privileged: true` across the
  base file and every override, in the session-testing runner). `Applications_share_nonroot_identity`
  is unchanged (base file) and joined by `Testing_runner_starts_as_root_and_drops`.
- `docs/testing-and-build.md:77` gains one sentence naming the nested daemon and the local
  harness.

### D-11. Verification is folded; local Docker Desktop proves the runtime, server2 proves the acceptance

The brief asks for the checkpoint table. `## Verification design` below is the executable
manifest. A new harness `scripts/verify-card0604-dind-runner.ps1` (pattern:
`scripts/verify-card0594-linux-launch.ps1`) boots the privileged image on Docker Desktop and
grades D-1, D-3, D-4, D-5 and D-8's plumbing without server2 or GitHub; the server2 rows then run
the real deployment, the real session and the real push.

### D-12. Mutation custody is untouched

`case_testing_payload`'s `! grep -a -q VerificationCustodyV1` and `verify-docker-stack.ps1`'s
`linux-custody` refusal are kept verbatim and re-run (CP-6, R-5). The nested daemon is
pre-existing relative to any Mutation process, so CARD-0590 D-13/D-14 and CARD-0598 stand.

### D-13. Defaults this plan is written under

- D-2 control plane: server2's own persistent server + Postgres (not phone-home).
- D-8 credential: ed25519 deploy key, title `antiphon-server2-runner`, write access, on
  `michal-ciechan/Antiphon` only; SSH over `ssh.github.com:443`.
- D-9: retire the `c590*` run-scoped projects and images during `deploy-parent`.
- Persistent project name `antiphon`; server2 root `/home/mc/antiphon-server2` with
  `secrets/stack.env`, `secrets/deploy_key`, `compose.runtime.yml`; host image tags
  `antiphon-server2/server:<sha12>` and `antiphon-server2/session-testing:<sha12>` plus the OCI
  revision label (`require_image_sha` unchanged).
- Nested pools `10.200.0.1/24` and `10.201.0.0/16`; `stop_grace_period: 90s`; runner
  healthcheck `curl /health && docker info`, interval 10 s, start period 60 s.
- Nested child project `c604<run>`; evidence `/work/test-evidence/<run>/<case>/` on the `work`
  volume, copied to the host path of the same name for the desktop bridge's `scp`.
- Nested roster for this card: `-Group small` (client lint/build/tests, messaging with broker)
  plus one `dotnet-filter` shard `db-fixture`:
  `/*/*/(TestDbFixtureLifecycleTests*)|(TestDbFixtureIsolationTests*)|(ProductionRunnerGuardTests*)/*`.
- The bridge passes `C604_BRANCH` (current branch name) for the checkout and the acceptance
  project's `baseBranch`; after land it is `master`.
- Script names keep their `c590` prefix (lineage; renaming is churn with no behaviour).
- Harness port default 18298, refused if in 17202-17205; harness image tag
  `antiphon-session-testing:c604-<sha12>`.
- The persistent server keeps CARD-0590 D-9's inactive-automation flags; a real Grok session on
  server2 is CARD-0575's provisioning question, not this card's.

## Target shape

```
server2 host daemon (24.0.2)                         Compose project "antiphon" (fixed)
  antiphon-postgres-1        postgres:16              restart: unless-stopped, pgdata
  antiphon-antiphon-1        server image <sha>        restart: unless-stopped, 127.0.0.1:5000
  antiphon-session-runner-1  session-testing <sha>     privileged, user 0:0, restart: unless-stopped
      tini -> dind-entrypoint.sh (root)
                 |- dockerd  (unix socket, group 1654, data-root = volume dind-data)
                 |    nested daemon: Testcontainers postgres/redpanda/ryuk,
                 |    child project c604<run>: server + runner(runtime) + postgres,
                 |    nested builds of the child images from /work/repos/antiphon
                 `- setpriv 1654 -> dotnet Antiphon.SessionRunner.dll
                          `- pty-host -> session shell (uid 1654):
                               pwsh scripts/test-docker.ps1 -Group small -ThrowawayStack
                               docker ... (nested), dotnet run ..., npm ..., git push (deploy key)
      /run/antiphon/deploy-key  (tmpfs, 0400, 1654; from Compose file secret)
      /work (volume, shared with the server), /state (runner-state volume)
```

## Slices

Order: S1 -> S2 -> S5 -> S3 -> S4 -> S6. S5 runs before S3/S4 so the image and entrypoint are
proven locally before the server2 scripts depend on them. Each slice is committed and pushed
before its checkpoint runs.

### S1. Image, entrypoint and baked config

Files: `docker/session-runner-grok/Dockerfile` (D-4 changes to the `session-testing` stage only;
new `node22` stage at the top), new `docker/session-runner-grok/dind-entrypoint.sh` (D-3),
`docker/session-runner-grok/daemon.json`, `docker/session-runner-grok/ssh_config`,
`docker/session-runner-grok/gitconfig`, `docker/session-runner-grok/github_known_hosts`.
Tests: `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs` (extend
`Default_runtime_excludes_test_payload` with `dockerd`; new `Testing_stage_ships_the_engine`,
`Testing_stage_pins_legacy_iptables`, `Testing_stage_has_build_toolchain`,
`Testing_stage_entrypoint_is_the_dind_script`); new
`tests/Antiphon.Tests/Infrastructure/DindRunnerContractTests.cs` (text guards over the four new
files: `Entrypoint_refuses_missing_deploy_key`, `Entrypoint_drops_to_app_uid`,
`Entrypoint_exits_when_either_process_exits`, `Entrypoint_checks_iptables_before_dockerd`,
`Entrypoint_never_prints_the_key`, `Nested_daemon_uses_private_address_pools`,
`Nested_daemon_socket_group_is_app`, `Ssh_config_pins_identity_and_known_hosts`,
`Ssh_config_uses_port_443`, `Gitconfig_pushes_over_ssh_only`,
`Known_hosts_carry_github_keys`). Each reads through `DockerStackDocuments.Read`.

### S2. Compose: the testing runner owns its daemon; the persistent override

Files: `docker-compose.session-testing.yml` (rewrite: `target: session-testing`, `user: "0:0"`,
`privileged: true`, `stop_grace_period: 90s`, `tmpfs: [/run/antiphon]`, `volumes:
dind-data:/var/lib/docker`, `secrets: [antiphon-deploy-key]`, env
`ANTIPHON_DEPLOY_KEY_SOURCE`, `Agents__GrokCredentialProbeEnabled: "false"`, healthcheck with
`docker info`; top-level `secrets` with `file: ${ANTIPHON_DEPLOY_KEY_FILE:?...}` and `volumes:
dind-data`; no `group_add`, no `docker.sock`), new `docker-compose.persistent.yml`
(`restart: unless-stopped` for `antiphon`, `session-runner`, `postgres`),
`docker-compose.test.yml` (comment: nested-only; unchanged otherwise), `docker/stack.env.example`
(drop `DOCKER_SOCKET_GID`, add `COMPOSE_PROJECT_NAME=antiphon` and
`ANTIPHON_DEPLOY_KEY_FILE=/home/mc/antiphon-server2/secrets/deploy_key`).
Tests: `DockerStackContractTests` re-aim per D-10 plus `Testing_runner_starts_as_root_and_drops`,
`Deploy_key_secret_is_required`, `Testing_runner_has_nested_store_volume`,
`Persistent_services_restart_unless_stopped`, `Persistent_override_leaves_state_init_alone`,
`Stack_env_example_has_no_socket_gid`.

### S3. The nested lane: the script a session actually runs

Files: `scripts/c590-remote.sh` (lane detection `docker info Name` versus `hostname`; per-case
lane declaration and `WrongLane` refusal; `ensure_dirs` without `sudo` in the nested lane;
`write_result` in shell, no `python3`; `PROJECT` = `antiphon` host / `c604${RUN}` nested;
`ensure_checkout` takes `C604_BRANCH`; `run_dotnet` and `run_client` run in-shell (D-6);
`deployment-state` runs against the nested child (D-7) with `compose.child.yml`;
`case_git_smoke` per D-8 with branch deletion; `case_throwaway` items = `child-server-image-payload`,
`child-runner-image-payload`, `deployment-state`, `client-lint`, `client-tests`, `messaging-tests`,
`dotnet-filter` (db-fixture), `git-credential-smoke`, `session-result-export-and-child-cleanup`;
`scrub_file` pattern `gh[pousr]_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+`), `scripts/test-docker.ps1`
(D-5 preflight from the manifest: `daemon.present`, `daemon.name`, `daemon.hostname`; live mode
fills them), `scripts/test-docker-container.ps1` (in-shell command shape).
Tests: `tests/Antiphon.Tests/Scripts/DockerTestCommandTests.cs` new `Sibling_daemon_is_refused`,
`Missing_nested_daemon_is_refused`; `tests/Antiphon.Tests/Scripts/C590Harness.cs` `Happy()`
gains `daemon` (`present: true`, `name: "runner-abc"`, `hostname: "runner-abc"`); new
`tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` text guards over `c590-remote.sh`:
`Nested_lane_never_uses_sudo_or_python`, `Scrub_covers_github_token_prefixes`,
`Smoke_deletes_its_branch`, `Child_project_is_run_scoped_and_distinct`.

### S4. The host lane: deploy, launch, observe

Files: `scripts/c590-remote.sh` host-lane cases `deploy-parent` (D-8 key generation, D-9
inventory and retirement, image builds with sha tags, `compose -p antiphon ... up -d --no-build
--remove-orphans` over base + session-testing + persistent + `compose.runtime.yml`, health wait,
`/api/version`, restart-policy and nested-daemon assertions, no-host-socket assertion from
`docker inspect`), `session-nested-stack` (creates project/board/card as `case_parent_session`
does, launches the Raw session, sends `cd /work/repos/antiphon && C604_RUN=<run> pwsh -NoProfile
-File scripts/test-docker.ps1 -Group small -ThrowawayStack; echo C604_EXIT=$?`, records
`session-id.txt` and `launched-at.txt`, and returns at once with `accepted: true`, diagnosis
`SessionLaunched`), `await-nested-stack` (reads `session-id.txt`, polls the transcript for
`C604_EXIT=<n>` for at most 8 minutes per invocation; still running is exit 2 with diagnosis
`SessionStillRunning` and no evidence copy, a dead session without the marker is
`SessionIncomplete` with the partial evidence copied, the marker copies `/work/test-evidence/<run>`
out of the runner with `docker cp`, records the nested `docker ps -a --filter
label=c604-run=<run>` and the host `docker ps -a` grep, and accepts only when every subordinate
`c590-result.json` is `accepted: true` and `n` is 0), `nested-residue`, `persistent-restart` (`compose stop`, `compose up -d`,
health, `/api/version`, nested `docker images -q` retained, DB marker retained),
`session-git-smoke` (launches a Raw session running the nested `git-credential-smoke` case and
awaits it), `parent-native-and-command-session` (unchanged logic, `C604_BRANCH`);
`scripts/c590-real.ps1` (live case list; `C604_BRANCH` export; deploy-key registration after
`deploy-parent`; remove the `gh auth token` copy and its `finally`; evidence copy unchanged);
`scripts/verify-docker-stack.ps1` (stub cases `deploy-parent`: `deployKeyPresent`,
`restartPolicy`, `daemonName`/`runnerHostname`; `session-nested-stack`: `sessionOrigin`,
`hostResidue`, `nestedResidue`; `persistent-restart`: `imagesRetained`, `versionBefore/After`;
`await-nested-stack`: `sessionState` (`running`/`exited`/`missing`), `markerSeen`,
`subordinateAccepted`; `session-git-smoke`: `branchDeleted`, `pushedSha`).
Tests: `tests/Antiphon.Tests/Scripts/DockerStackSmokeCommandTests.cs` new
`Missing_deploy_key_refuses_deploy`, `Non_persistent_policy_refuses_deploy`,
`Sibling_daemon_refuses_deploy`, `Host_residue_refuses_nested_stack_case`,
`Nested_residue_refuses_nested_stack_case`, `Session_origin_is_required_for_nested_stack`,
`Still_running_session_is_not_a_result` (manifest `sessionState: running` -> exit 2,
`SessionStillRunning`, no `result-ready`), `Dead_session_without_marker_is_incomplete`,
`Subordinate_failure_refuses_acceptance`, `Lost_nested_store_refuses_restart_case`,
`Undeleted_smoke_branch_is_refused`.

### S5. Local harness

File: new `scripts/verify-card0604-dind-runner.ps1` (pwsh 7, ASCII, Docker Desktop only;
`-Image`, `-Build`, `-Port` default 18298 refused in 17202-17205, `-EvidenceRoot
.antiphon/c604-harness`; one container `c604-<stamp>`, one volume `c604-<stamp>-dind`, one
throwaway key pair generated into the evidence directory and registered nowhere). Steps are
`### Local harness` below. Prints `C604 HARNESS: image=... nested=... egress=... loopback=...
launchMs=... key=... restart=... refusal=...` and `C604 HARNESS EXIT CODE: n` (0 all
observations match, 1 a mismatch, 2 setup failure).
Tests: new `tests/Antiphon.Tests/Scripts/VerifyDindRunnerScriptTests.cs` (pattern:
`VerifyPhoneHomeGrokScriptTests`): `Production_port_is_refused`, `Image_is_required`,
`Compose_and_dockerfile_do_not_hardcode_a_key_path_value` (the only key path is the
`/run/antiphon` tmpfs), `Harness_names_only_its_own_container`.

### S6. Docs and supersession

Files: `docs/docker-stack.md` (paragraph 2 per D-10; add the nested-daemon operations: restart,
upgrade, nested prune, `dind-data` wipe, the four exit-3 refusals, `deploy-parent` as the
deployment entry, evidence paths), `docs/agent-credentials.md` §5 (D-8 custody paragraph),
`docs/testing-and-build.md:77` (one sentence: nested daemon, `verify-card0604-dind-runner.ps1`),
`docs/superpowers/plans/2026-09-21-card-0590-self-contained-docker-stack-plan.md` D-6 (one
appended annotation line). `AGENTS.md`'s local-stack triggers gain no line: the runner is
server2-only and `docker-stack.md` owns it.
Tests: new `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs`:
`Docker_stack_doc_names_the_nested_daemon` (mentions `dind-data`, `privileged`, `deploy_key`,
does not contain "only application override that mounts the socket"),
`Credentials_doc_names_the_deploy_key_custody`, `Card_0590_D6_is_annotated_superseded`.

## Verification design

TestDesign folded (D-11). Bodies read: `docker/session-runner-grok/Dockerfile` (whole),
`docker-compose.yml`, `docker-compose.session-testing.yml`, `docker-compose.test.yml`,
`docker-compose.delivery-fixture.yml`, `docker-compose.runner-grok.yml`, `docker/tests/Dockerfile`,
`docker/stack/init-state.sh`, `docker/stack.env.example`, `.dockerignore`, `scripts/c590-remote.sh`
(whole), `scripts/c590-real.ps1` (whole), `scripts/verify-docker-stack.ps1` (whole),
`scripts/test-docker.ps1` (whole), `scripts/c590-command.ps1`, `scripts/run-checkpoint.ps1:1-40`,
`scripts/verify-card0594-linux-launch.ps1:1-60`, `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs`
(whole), `DockerStackDocuments.cs` (whole), `tests/Antiphon.Tests/Scripts/DockerTestCommandTests.cs:1-80`
and method list, `DockerStackSmokeCommandTests.cs` method list, `C590Harness.cs:59-78`,
`tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:1-120`, `docs/docker-stack.md`,
`docs/agent-credentials.md` (whole), `docs/testing-and-build.md:60-165`, CARD-0590 plan
lines 82-380, 618-790, 1980-2140, CARD-0594 plan (whole), CARD-0598/0605 card text.

### Inspection

- `DockerStackContractTests` | `[Category("Unit")]`, pure file reads, 82 methods today; the two
  re-aimed methods and about 14 new ones stay file-only; no OS skip.
- `DockerTestCommandTests` / `DockerStackSmokeCommandTests` | `[Category("Integration")]`,
  `[ParallelLimiter<ProcessSpawnLimit>]`, each spawns `pwsh` with `ANTIPHON_C590_STUB`; the new
  manifest keys default to the happy shape in `C590Harness.Happy()` so every existing method keeps
  its exit and diagnosis.
- `TestDbOperations.StartAsync` | boundary: the connection string's host is whatever
  Testcontainers resolves; nothing in this plan edits it (D-5); V-18 executes it for real.
- `case_testing_payload` | keeps `! grep VerificationCustodyV1` and `id -u` = 1654 under
  `--user 1654:1654`; the image's default user becomes root (D-4), which that case never relies on.
- `Applications_share_nonroot_identity` | reads base Compose only; the override's `user: "0:0"`
  is asserted by the new drop test, not by this one.
- `Default_runtime_excludes_test_payload` | closure of the last stage (`runtime` -> `runtime-base`);
  the `build`, `receipt-probe` and `session-testing` stages are outside it, so the engine, SDK and
  Node land only where D-4 puts them.
- Missing setup recorded: Docker Desktop must be up for CP-4 (use the `docker-desktop` skill);
  the desktop `gh` login must have admin on the repo for `deploy-key add` (CP-5); server2 SSH via
  the existing `mc@server2` alias; no Linux TUnit lane on this host (CARD-0605).

### Delivery inventory

The session-launched run (CP-8, CP-11) is asynchronous relative to the host lane: the host
posts input and returns (`session-nested-stack`, `SessionLaunched`); the session runs for up to
75 minutes; `await-nested-stack` polls the transcript for `C604_EXIT=<n>` in bounded calls of at
most 8 minutes each, at least 60 s apart, and only the call that sees the marker reads the
evidence the session wrote. Owners: `session-id.txt` written before the input is posted, the
session's own `c590-result.json` per case (written before the session prints the marker), the
host lane's copy (`docker cp`) recorded after the marker, and the residue inventory read after
the copy. `SessionStillRunning` is not a result and writes no `result-ready`. Interruption
(session dies, the 75-minute cap measured from `launched-at.txt` expires) is `SessionIncomplete`
with the partial evidence kept, never success. No production delivery path (queue, outbox, receipts) is involved; the Raw
session's input goes through `POST /api/sessions/{id}/input` exactly as `case_parent_session`
does today. Substitutes: none claimed; every server2 row is the real stack.

### Proves it works now

- V-1: the `session-testing` image carries the whole engine, legacy iptables, iproute2,
  openssh-client, SDK 10 with runtimes 9 and 10, Node 22, pwsh, the entrypoint, `daemon.json`,
  ssh/git config and known_hosts, and still no `VerificationCustodyV1` | text (S1 tests, CP-1) and
  live payload probe (`testing-runner-payload` extended, CP-6) | every `test -x`/`grep` passes;
  `iptables --version` prints `legacy`.
- V-2: only the testing runner is privileged; no application service mounts a socket; the
  secret is required; the persistent override restarts the three services | `DockerStackContractTests`
  | CP-1.
- V-3: the command boundaries refuse a sibling daemon, a missing nested daemon, a missing deploy
  key, a non-persistent policy, host residue, nested residue, a lost nested store and an undeleted
  smoke branch, each before emitting a command | `DockerTestCommandTests`,
  `DockerStackSmokeCommandTests` | CP-2 | nonzero exit, named diagnosis, empty command sink.
- V-4: the image boots privileged and the nested daemon's `Name` equals the container's hostname
  | local harness | CP-4 | `nested=yes`.
- V-5: nested pull, NAT and TLS egress work | harness step 5 | CP-4 | `egress=yes`
  (`api.github.com/zen` returned text from a nested `alpine`).
- V-6: a nested container's mapped port answers on the runner's own `127.0.0.1` for uid 1654 |
  harness step 6 | CP-4 | `loopback=200`.
- V-7: a session launches on the DinD runner in under 5 s | harness step 7 (`POST /sessions`,
  as CARD-0594's harness) | CP-4 | `launchMs` under 5000, HTTP 2xx.
- V-8: the deploy key is `0400 1654` on the tmpfs and `ssh -G github.com` resolves the baked
  config without network | harness step 8 | CP-4 | `key=ok`.
- V-9: killing `dockerd` exits the container; the restart policy brings it back healthy with the
  nested store retained | harness step 9 | CP-4 | `restart=ok` (RestartCount incremented, health
  back, `nginx:alpine` still present in the nested store).
- V-10: a missing key and a non-privileged run each refuse with exit 3 and the named diagnosis |
  harness step 10 | CP-4 | `refusal=ok`.
- V-11: server2 project `antiphon` is up, three services `unless-stopped`, `/api/version` equals
  the sha, nested daemon `Name` equals the runner's hostname, the runner has no host socket mount,
  the deploy key is registered on GitHub under the expected title | `deploy-parent` | CP-5.
- V-12: the `c590*` leftovers are retired with an inventory and `am-service`,
  `schoolrevision-*`, `antiphon-messaging_*` are untouched | `deploy-parent` | CP-5.
- V-13: a Raw session launches in the persistent runner and echoes `C590_RAW_OK` within 60 s
  (the CARD-0594 live confirmation on server2) | `parent-native-and-command-session` | CP-7.
- V-14: a server-launched session runs `test-docker.ps1 -Group small -ThrowawayStack` and the
  nested preflight accepts | `session-nested-stack` | CP-8 | `daemon.name` = hostname recorded.
- V-15: the child images build on the nested daemon from `/work/repos/antiphon` with the sha
  label; the child runner has no engine | subordinate `child-server-image-payload`,
  `child-runner-image-payload` | CP-8.
- V-16: the nested child stack `c604<run>` reaches health, reports the sha, keeps a marker across
  `stop`/`up`, and is removed with `down -v` | subordinate `deployment-state` | CP-8.
- V-17: Small executes in-runner: client lint/build, all frozen Vitest files (>= 102 tests, 0
  failed), messaging (24 classes, >= 206 methods, Redpanda on the nested daemon) | subordinate
  `client-lint`, `client-tests`, `messaging-tests` | CP-8.
- V-18: the `db-fixture` shard executes in-runner against Testcontainers Postgres reached via
  `localhost`, all three classes, 0 failed | subordinate `dotnet-filter` | CP-8 | fresh TRX,
  `CHECKPOINT db-fixture ... failed=0`.
- V-19: evidence is under `/work/test-evidence/<run>/<case>/` on the volume and copied to the
  host path | `session-nested-stack` | CP-8.
- V-20: after the run, no `c604` containers, networks or volumes on the nested daemon and none
  ever on the host daemon; the parent is healthy | `nested-residue` | CP-9.
- V-21: `compose stop` then `up -d` keeps the container names, brings the nested daemon back with
  its images, keeps the DB marker and `/api/version` | `persistent-restart` | CP-10.
- V-22: an in-session push over the deploy key lands `throwaway/c604-credential-smoke-<run>` on
  GitHub, the branch is deleted afterwards, and the copied logs contain no key or token bytes |
  `session-git-smoke` | CP-11 | `pushed-sha.txt` 40 hex, `deleted.txt` = `yes`,
  `git ls-remote` from the desktop no longer lists the branch.
- V-23: after the desktop CLI disconnects, the stack answers and the evidence index is complete |
  `server2-independent-handoff` | CP-12.

### Guards the regression

- R-1: the 80 unchanged `DockerStackContractTests` methods keep their assertions | CP-1.
- R-2: the 20 `DockerTestCommandTests` and 16 `DockerStackSmokeCommandTests` methods keep their
  exits and diagnoses with the extended happy manifest | CP-2.
- R-3: harness argument and port refusals | `VerifyDindRunnerScriptTests` | CP-3.
- R-4: nested cleanup acts only on `c604<run>` (existing `foreign-id`/`foreign-label`/`parent-child`
  stub guards, unchanged) | CP-2 and live in CP-9.
- R-5: `testing-runner-payload` keeps `! grep VerificationCustodyV1` and non-root `id -u`;
  `linux-custody` stub unchanged | CP-6, CP-2.
- R-6: docs name the new shape and the old sentence is gone | `DockerStackDocumentationTests` |
  CP-13.
- R-7: `Default_runtime_excludes_test_payload` (now also `dockerd`) and
  `Receipt_runner_has_no_docker_tools`: the runtime and receipt images gain no engine | CP-1.

### Guard inventory

- G-1: privileged only on the testing runner | PC-1
- G-2: no socket in application services | PC-2
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

guards=17, mapped=17, missing=0, duplicate PC mappings=0. The child runner's socket-freedom is
live evidence (V-15), not a PC: its only text seam is a heredoc inside `c590-remote.sh`.

### Positive controls

Mutation runs these after land, method-scoped, red then restore then green, Windows local
inherited only (no Docker, no server2); Code implements the tests and runs V/R only.

- PC-1: delete `privileged: true` from `docker-compose.session-testing.yml`; expect
  `DockerStackContractTests.Only_the_testing_runner_is_privileged` red at the count assertion.
- PC-2: add `- /var/run/docker.sock:/var/run/docker.sock` under the override's `volumes`; expect
  `DockerStackContractTests.Testing_runner_owns_its_daemon` red at `sockets.ShouldBe(0)`.
- PC-3: in `dind-entrypoint.sh` replace `setpriv --reuid=1654 --regid=1654` with a bare `"$@"`;
  expect `DindRunnerContractTests.Entrypoint_drops_to_app_uid` red.
- PC-4: delete the `wait -n` line and the kill-the-other block; expect
  `DindRunnerContractTests.Entrypoint_exits_when_either_process_exits` red.
- PC-5: delete the `[ -f ... ] && [ -s ... ]` check before `install`; expect
  `DindRunnerContractTests.Entrypoint_refuses_missing_deploy_key` red.
- PC-6: delete the `update-alternatives --set iptables` line in the Dockerfile; expect
  `DockerStackContractTests.Testing_stage_pins_legacy_iptables` red.
- PC-7: move the engine `tar` extraction line from the `session-testing` stage into
  `runtime-base`; expect `DockerStackContractTests.Default_runtime_excludes_test_payload` red at
  the `dockerd` assertion.
- PC-8: delete `default-address-pools` from `daemon.json`; expect
  `DindRunnerContractTests.Nested_daemon_uses_private_address_pools` red.
- PC-9: in `scripts/test-docker.ps1` delete the `daemon.name -ne daemon.hostname` refusal;
  expect `DockerTestCommandTests.Sibling_daemon_is_refused` red at `Diagnosis(run).ShouldBe("SiblingDaemonRefused")`.
- PC-10: delete `restart: unless-stopped` under `session-runner` in
  `docker-compose.persistent.yml`; expect `DockerStackContractTests.Persistent_services_restart_unless_stopped` red.
- PC-11: in `scripts/verify-docker-stack.ps1` `deploy-parent`, delete the `deployKeyPresent`
  refusal; expect `DockerStackSmokeCommandTests.Missing_deploy_key_refuses_deploy` red.
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
- PC-17: change `${ANTIPHON_DEPLOY_KEY_FILE:?...}` to `${ANTIPHON_DEPLOY_KEY_FILE:-}` in the
  override; expect `DockerStackContractTests.Deploy_key_secret_is_required` red.

Filters: `--treenode-filter "/*/*/<Class>/<method>"`; restore and rebuild before each green.

### Local harness

`scripts/verify-card0604-dind-runner.ps1` performs, in order, recording each command's output
under `.antiphon/c604-harness/<stamp>/`:

1. Optional `-Build`: `docker build -f docker/session-runner-grok/Dockerfile --target session-testing --build-arg SOURCE_REVISION=<HEAD sha> -t <image> .`
2. `ssh-keygen -t ed25519 -N "" -f <evidence>/throwaway_key` (never registered; exercises the
   secret plumbing only); `docker volume create c604-<stamp>-dind`.
3. `docker run -d --privileged --init --restart unless-stopped --name c604-<stamp> -p <port>:8080
   -v c604-<stamp>-dind:/var/lib/docker --tmpfs /run/antiphon
   -v <evidence>/throwaway_key:/run/secrets/antiphon-deploy-key:ro
   -e ANTIPHON_DEPLOY_KEY_SOURCE=/run/secrets/antiphon-deploy-key
   -e SessionRunner__SessionLogPath=/tmp/state/session-runner -e SessionRunner__PtyHostDir=/tmp/antiphon-pty-hosts
   -e Serilog__LogPath=/tmp/state/runner-logs -e SessionRunner__PtyBackend=inbox -e ANTIPHON_PTY_BACKEND=inbox
   -e PhoneHome__Enabled=false -e SessionRunner__Herdr__Enabled=false <image>`; wait for
   `GET /health` (90 s cap).
4. `docker exec c docker info --format '{{.Name}} {{.Driver}} {{.CgroupVersion}}'` and
   `docker exec c hostname`; `nested=yes` when the names match.
5. `docker exec c docker run --rm alpine:3.20 wget -qO- https://api.github.com/zen`; `egress=yes`
   when non-empty.
6. `docker exec -u 1654:1654 c sh -c 'id=$(docker run -d -p 127.0.0.1::80 nginx:alpine); p=$(docker port $id 80 | sed s/.*://); curl -fsS -o /dev/null -w %{http_code} http://127.0.0.1:$p/; docker rm -f $id >/dev/null'`;
   `loopback=<code>`.
7. `POST /sessions` `{"sessionId":"<guid>","exe":"/bin/bash","args":[],"env":{},"cwd":"/tmp","cols":120,"rows":30}`;
   record status and `launchMs`.
8. `docker exec -u 1654:1654 c stat -c '%u %a' /run/antiphon/deploy-key` (`1654 400`) and
   `docker exec -u 1654:1654 c ssh -F /etc/antiphon/ssh_config -G github.com` (contains
   `identityfile /run/antiphon/deploy-key`, `hostname ssh.github.com`, `port 443`,
   `identitiesonly yes`); `key=ok`.
9. Read `RestartCount`; `docker exec c sh -c 'kill -TERM $(pidof dockerd)'`; wait up to 60 s for
   `RestartCount` to increment and `/health` to answer; `docker exec c docker images -q
   nginx:alpine` non-empty; `restart=ok`.
10. `docker run --rm --privileged --init <image>` with no key mount -> exit 3, logs contain
    `DeployKeyMissing`; `docker run --rm --init <image>` (not privileged, with the key) -> exit 3,
    logs contain `NotPrivileged`; `refusal=ok`.
11. `docker rm -f c604-<stamp>`; `docker volume rm c604-<stamp>-dind`; delete the throwaway key;
    print the result line and `C604 HARNESS EXIT CODE: <n>`.

On Docker Desktop the daemon is cgroup v2, so step 3 exercises D-3 step 3; server2 (v1)
exercises the other branch in CP-5. Both hosts run the same image.

### Server2 cases

All through `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case <name> -Manifest
$manifest` with `ANTIPHON_C590_STUB` unset, which scp's `c590-remote.sh` and runs the host lane
over SSH as `mc`. Freeze `$sha = git rev-parse HEAD`, a fresh `$runId`, and `C604_BRANCH`
before CP-5; every server2 row shares them. Evidence lands under `.antiphon/c590-server2/evidence-<case>/`
on the desktop and `/work/test-evidence/<run>/<case>/` on server2. The ordering CP-5 -> CP-7 ->
CP-8 is mandatory: no nested work before the Raw session is proven.

### Out of scope

- Full CARD-0590 Medium roster shards (`medium-local-*`, `medium-database-*`, `medium-http-*`):
  the lane now exists; running them is CARD-0590's acceptance, through the same
  `-ThrowawayStack` entry with `-Group backend`.
- CARD-0590 receipt/recovery cut cases (S6c/S6d) and the receipt-probe image on the nested daemon.
- `LinuxPtyHostLauncherTests` and the other `RequireLinux()` classes (CARD-0605; noted as a
  follow-up below).
- The live V-7 Grok turn (CARD-0594 follow-up) and any Grok credential on server2 (CARD-0575).
- Linux SourceLanding custody (CARD-0598).
- A phone-home override for the persistent runner (D-2, rejected default).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c604/` (Windows) | contract-guards | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)/*` | V-1 (text), V-2, R-1, R-7 | all listed, 0 failed (>= 100 executed) | 12 |
| CP-2 | S3-S4 | CP-1 | command-guards | `/*/*/(DockerTestCommandTests*)\|(DockerStackSmokeCommandTests*)\|(RemoteScriptContractTests*)/*` | V-3, R-2, R-4, R-5 | all listed, 0 failed (>= 50 executed) | 8 |
| CP-3 | S5 | CP-1 | harness-contract | `/*/*/VerifyDindRunnerScriptTests/*` | R-3 | all listed, 0 failed (4 executed) | 3 |
| CP-4 | S1, S2, S5 | Docker Desktop image from HEAD (`-Build`, target `session-testing`) | dind-local | `pwsh -NoProfile -File scripts/verify-card0604-dind-runner.ps1 -Image antiphon-session-testing:c604-<sha12> -Build` | V-4, V-5, V-6, V-7, V-8, V-9, V-10 | `C604 HARNESS EXIT CODE: 0` | 25 |
| CP-5 | all | server2 host daemon: server + session-testing images at HEAD | server2-deploy | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case deploy-parent -Manifest $manifest` | V-11, V-12 | 1 case accepted; inventory, `.pub`, `/api/version` in evidence | 30 |
| CP-6 | all | CP-5 | server2-testing-payload | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case testing-runner-payload -Manifest $manifest` | V-1 (live), R-5 | 1 case accepted | 4 |
| CP-7 | all | CP-5 | server2-raw-session | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case parent-native-and-command-session -Manifest $manifest` | V-13 | 1 case accepted; marker within 60 s | 5 |
| CP-8 | all | CP-5 | server2-session-nested-stack | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case session-nested-stack -Manifest $manifest` then `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case await-nested-stack -Manifest $manifest` repeated until its diagnosis is not `SessionStillRunning` (each call under 9 minutes, at least 60 s apart; count the calls) | V-14, V-15, V-16, V-17, V-18, V-19 | launch accepted; final await accepted plus 9 subordinate `c590-result.json` receipts, each `accepted: true`; `db-fixture` TRX `failed=0` | 75 |
| CP-9 | all | CP-5 | server2-nested-residue | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case nested-residue -Manifest $manifest` | V-20, R-4 | 1 case accepted | 3 |
| CP-10 | all | CP-5 | server2-persistent-restart | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case persistent-restart -Manifest $manifest` | V-21 | 1 case accepted | 8 |
| CP-11 | all | CP-5 | server2-session-git-smoke | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case session-git-smoke -Manifest $manifest` | V-22 | 1 case accepted; branch pushed then deleted | 6 |
| CP-12 | all | CP-5 | server2-handoff | `pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case server2-independent-handoff -Manifest $manifest` | V-23 | 1 case accepted | 4 |
| CP-13 | S6 | CP-1 | docs-guards | `/*/*/DockerStackDocumentationTests/*` | R-6 | all listed, 0 failed (3 executed) | 2 |

Rules for this table: CP-1 through CP-3 and CP-13 are `run-checkpoint.ps1` rows into
`.antiphon/c604-checkpoints/`; CP-4 through CP-12 are the non-TUnit exact-command form, one case
receipt each. CP-5 is the only row that changes server2's standing state and must be run once
per frozen sha; a red server2 row is fixed and rerun as the same row. If CP-1's combined class
filter does not select on the pinned TUnit, split it into two rows sharing the build. Delete
every `bin-c604` directory, the harness container and volume, and no server2 resource other than
the run's `c604<run>` residue, before settling. `Antiphon.Tests` rows here are file/script-only
and need no Postgres.

### Cost

Estimated, not measured; nothing ran in this Plan.

| Ordinary Code floor | Minutes |
|---|---:|
| CP-1 contract-guards (build Antiphon.Tests into `bin-c604/` + 2 classes) | 12 |
| CP-2 command-guards (no build, 3 classes spawning pwsh) | 8 |
| CP-3 harness-contract | 3 |
| CP-4 dind-local (image build about 12 + steps) | 25 |
| CP-5 server2-deploy (two image builds on server2 + leftovers retirement + key registration) | 30 |
| CP-6 testing-payload | 4 |
| CP-7 raw-session | 5 |
| CP-8 session-nested-stack (nested pulls and builds about 25, Small about 20, db-fixture about 12, smoke and cleanup) | 75 |
| CP-9 nested-residue | 3 |
| CP-10 persistent-restart | 8 |
| CP-11 session-git-smoke | 6 |
| CP-12 handoff | 4 |
| CP-13 docs-guards | 2 |
| **Ordinary V/R** | **185** |

Authoring: S1 120, S2 45, S3 150, S4 150, S5 120, S6 60 = 645. `-ExpectAbout 840`, reported as
a band (700-1000) because CP-8's nested pulls on server2 are the least predictable item.

Separate post-land Mutation floor: PC-1 through PC-17 are Windows text/script tests; each is
0.5 apply/restore + 1.5 incremental build + 0.25 red + 1.5 restored build + 0.25 green = 4
minutes, total 68, plus 6 setup = **74**, estimated.

Handoff audit: bodies read; guards=17, mapped=17, missing=0, duplicate PC mappings=0; every PC
is a compiling text or script defect with an exact method and assertion; numeric Cost above.

## Risks

- **Legacy iptables on Docker Desktop.** The desktop's kernel is nft-capable; `iptables-legacy`
  still works there because the `ip_tables` module is present in Docker Desktop's VM. If step 4 of
  the entrypoint fails on the desktop only, CP-4 records `IptablesUnusable` and Code adds the
  `DOCKER_IPTABLES_LEGACY`-style opt-out for the desktop only; server2 stays pinned.
- **`ssh.github.com:443` egress from inside the runner.** TLS to `api.github.com` was measured
  from a nested container; an SSH handshake on 443 was not. CP-11 is the measurement; the fallback
  is port 22 in `ssh_config`, which changes one line and one known_hosts entry.
- **Compose secret file ownership.** The bind-mounted secret is owned by `mc` on the host; the
  entrypoint copies it as root, so ownership never matters to the runner. If Compose v2.18 refuses
  a file secret shape, the fallback is a read-only bind mount to the same source path, with D-3
  step 2 still the guard against the missing-file directory.
- **Session environment for git.** Whether a pty child inherits the runner's process environment
  was not established from the code (the Linux spawn path composes its own environment). D-8
  therefore relies on `/etc/gitconfig` and `/etc/antiphon/ssh_config`, which no environment can
  drop; CP-11 proves it in a real session.
- **Nested store growth.** The first CP-8 pulls about 4 GB of base images into `dind-data`;
  reruns reuse them. The nested prune is the operator lever (D-9). Host free space is checked
  and recorded by `deploy-parent` (refuse below 15 GB, `DiskLow`).
- **`stop_grace_period` and nested containers.** On `docker stop`, `dockerd` stops its
  containers before exiting; a stuck nested container is killed at 90 s. A session mid-run is
  lost, which is the documented "restarting the runner process versus replacing its container"
  rule from CARD-0590.
- **Host daemon restart.** `LiveRestoreEnabled=false` on server2: a `systemctl restart docker`
  still drops everything; `restart: unless-stopped` brings the project back afterwards. Not this
  card's to change, documented in S6.
- **Compose `tmpfs` short syntax has no mode/uid options**; the entrypoint sets them. If a
  Compose version mounts `/run/antiphon` with `noexec`/`nosuid` defaults, that is fine for a
  key file.
- **The 75-minute CP-8 run is several tool windows long** (10-minute foreground cap, and
  delegates never background a run). That is why S4 splits launch from await: every
  `await-nested-stack` call is bounded at 8 minutes and returns `SessionStillRunning` as a
  retryable non-result, so Code loops foreground calls, spaced at least 60 s apart, and reads or
  investigates between them. The session itself runs unattended in the runner regardless of the
  desktop's polling; a dropped SSH does not kill it.

## Not done, noted

- **CARD-0605**: the persistent runner is a Linux execution lane; `LinuxPtyHostLauncherTests`
  and the other `RequireLinux()` classes can run in-session through the same
  `dotnet-filter` shape. One roster addition, not this card.
- **CARD-0590 roster**: `-Group backend` through the nested lane, to replace the never-green
  sibling evidence; also the receipt/recovery cases on the nested daemon.
- **`gh` in the image** if a future case needs the GitHub API from a session; then D-8's rejected
  PAT or the Antiphon key store (once the Linux X509 protector exists) is the credential, not the
  deploy key.
- **Phone-home override** for dispatching production-board work to server2 (D-2 rejected
  default); `docker-compose.runner-grok.yml` is the starting point.
- **The rdkafka log flood** (`localhost:19092` every 30 s with `ChannelBridge__Enabled=false`)
  the investigation noted makes the server log unreadable; a separate small card.
- **`c590` naming**: scripts keep the prefix; a rename to `stack-*` is cosmetic and separate.
- **Existing `throwaway/c590-credential-smoke-3537de6b7be4` branch** on GitHub from the
  CARD-0590 run: delete by hand or let the first CP-11 delete only its own branch; not automated
  here.
