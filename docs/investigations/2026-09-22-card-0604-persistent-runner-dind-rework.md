# CARD-0604: rework CARD-0590's server2 stack into the persistent-runner DinD shape

Date: 2026-09-22. Stage: Investigate. Worktree `card-task-51bcc470`, base `d0ab05f7`.
Evidence: live server2 measurements, stored CARD-0590 run evidence under
`/work/test-evidence/3537de6b7be4`, and repository source at this SHA. No source was changed.

## Outcome

The rework is technically feasible on server2 and classic Docker-in-Docker was **measured
working there today**. But the card's acceptance sentence — "a session inside that runner stands
up its own nested throwaway stack" — is blocked by something that has nothing to do with Docker
topology: **no session can launch in the Linux runner at all**. `POST /api/sessions` against the
containerised stack returns 500 because the runner cannot connect to its pty-host socket. That is
CARD-0594, still `Backlog`. Topology work can proceed in parallel, but the session-driven
acceptance gate cannot be demonstrated until CARD-0594 lands. That sequencing is the caller's call.

Separately, the card's context line "server2 has no persistent session-runner container running
today (confirmed live 2026-09-22)" is not quite what is there: a session-runner container **is**
up and healthy. It is not *persistent by design* — see §1.

## Measured environment (server2, 2026-09-22)

| Fact | Value | Why it matters |
|---|---|---|
| OS | Ubuntu 18.04.1 LTS (Bionic) | EOL standard support; constrains sysbox/rootless |
| Kernel | `4.15.0-213-generic` | Below any sysbox minimum; no idmapped mounts |
| Docker | 24.0.2, Compose v2.18.1 | Host daemon |
| Runtimes | `runc`, `io.containerd.runc.v2` only; default `runc` | **No sysbox installed** |
| cgroups | v1 (`stat -fc %T /sys/fs/cgroup` = tmpfs, `CgroupVersion=1`, driver `cgroupfs`) | Rootless dind loses resource limits |
| `shiftfs` | not loaded | sysbox prerequisite absent |
| `unprivileged_userns_clone` | `1`; `max_user_namespaces=515400`; `/dev/fuse` present | Rootless dind is *startable* |
| Security | apparmor + seccomp builtin; `LiveRestoreEnabled=false` | Daemon restart drops all containers |
| Disk | `/` 213G, 149G used, **56G free** | Nested image store is a second copy |
| Memory / CPU | 125 GB, 24 cores | Ample |
| Uptime | **182 days**; ~30 long-lived containers incl. `am-service`, `traefik`, `cloudflared`, `windmill`, `portainer` | A reboot for a kernel upgrade is high blast radius |
| docker socket | gid 129 (`docker`), `mc` is a member | Matches `DOCKER_SOCKET_GID=129` in `c590-remote.sh:85` |

## 1. What makes the runner "persistent" — and what is actually there

A session-runner container **is** running: `c5903537de6b7be4-session-runner-1`, image
`antiphon-c590-3537de6b7be4-session-testing`, `Up 12 hours (healthy)`, alongside
`-antiphon-1`, `-postgres-1` and an exited `-state-init-1`. It is the leftover parent stack from
the last CARD-0590 evidence run, not a deployment. Five concrete gaps:

1. **Run-scoped compose project.** `scripts/c590-remote.sh:13` sets `PROJECT="c590${RUN}"`, so every
   run creates a *new* project and a *new* volume set (`c5903537de6b7be4_pgdata`,
   `_server-state`, `_runner-state`, `_work`, `_nodemodules` are what exist today). A persistent
   runner needs a fixed project name and fixed volume names.
2. **No restart policy.** `docker inspect -f '{{.HostConfig.RestartPolicy.Name}}'` is **empty** for
   all three containers, and `LiveRestoreEnabled=false`. A daemon restart or host reboot silently
   loses the stack. `docker-compose.yml` sets `restart` only on `state-init` (`"no"`).
3. **No supervision.** `systemctl list-units | grep -iE 'antiphon|c590'` → none. No `/etc/docker/daemon.json`.
4. **Lifecycle owned by the desktop, per case.** `scripts/c590-real.ps1:203-217` scp's
   `c590-remote.sh` to server2 and ssh-runs one case; `ensure_parent()`
   (`c590-remote.sh:223-237`) brings the stack up as a side effect of whichever case ran first.
   Nothing tears it down — `normal-down` / `global-cleanup` exist in
   `scripts/verify-docker-stack.ps1:91-98` only as stubbed `Invoke-C590` recordings. That is why a
   12-hour-old stack is still up.
5. **Run-scoped image tags.** `tag()` (`c590-remote.sh:16`) produces
   `antiphon-c590-<run>-session-testing`. A deployment needs a stable tag plus an explicit
   revision label (the label machinery already exists: `require_image_sha`, `c590-remote.sh:169`).

Also unaddressed for a runner that is meant to run *real* Grok/Codex sessions: the image
deliberately bakes no credentials (CARD-0575; asserted by `case_testing_payload`), `GROK_HOME` is
an empty `/state/grok`, and nothing in the repo provisions a Grok session on server2.

## 2. Getting a nested daemon — options measured

### Blocking prerequisite (independent of the option chosen)

Stored evidence, `parent-native-and-command-session`:
`{"accepted": false, "diagnosis": "UnhandledExit 1", "exit": 1}`; `session.json` is a raw
`500 Internal Server Error`. Server log:

```
Antiphon.Server.Application.Exceptions... System.Net.Http.HttpRequestException: Session runner returned 500: Internal Server Error
System.InvalidOperationException: Runner terminal session not started.
```

Runner log (same stack, 23:49:36):

```
System.TimeoutException: Could not connect to pty-host pipe '/tmp/antiphon-pty-0e368a64f3084f21a22911da10d1d830' within 00:00:15.
  at Antiphon.PtyHost.Client.PtyHostClient.ConnectAsync(...) in /src/src/Antiphon.PtyHost.Client/PtyHostClient.cs:line 82
  at Antiphon.SessionRunner.SessionRunnerRuntime.RunnerSession.StartAsync(...) in /src/src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:line 2070
```

This is exactly the V-7 failure recorded at `docs/testing-and-build.md:159`, owned by **CARD-0594
(`Backlog`)**. The runner is on `inbox`/`InboxConhost` as configured (runner log line 2).
No Docker topology fixes it.

### Option 1 — `dockerd` inside the persistent session-runner container (recommended)

Literal reading of the card: the runner container runs its own daemon; nested stacks live in it.

- **Proven on server2.** `docker run -d --rm --privileged docker:27-dind` came up and reported
  `27.5.1 storage=overlay2 cgroup=1`. Overlay2-on-overlay2 worked; cgroup v1 was not an obstacle.
- **Image cost is small.** The `session-testing` stage already downloads
  `docker-27.5.1.tgz` (`docker/session-runner-grok/Dockerfile:64`) and extracts only `docker/docker`.
  `tar -tz` on that exact tarball lists `runc containerd docker-init dockerd
  containerd-shim-runc-v2 docker-proxy docker ctr` — the whole daemon is already in the file being
  fetched. Add `iptables`/`iproute2` from apt.
- **Requires `privileged: true`** on the runner service (or CAP_SYS_ADMIN + apparmor/seccomp
  unconfined, which is the same risk). This directly contradicts landed plan decision **D-6**
  ("Reject DinD, privileged services") and `docs/docker-stack.md:5`; Plan must supersede both.
- **Breaks the non-root shape.** `dockerd` needs root, the runner process is `USER 1654:1654`
  (`Dockerfile`/`session-runner-grok/Dockerfile:75`, compose `user: "1654:1654"`). An entrypoint
  that starts `dockerd` then drops to 1654 is needed. `DockerStackContractTests.Applications_share_nonroot_identity`
  (reads compose `user:`) would fail if `user:` is changed to root, and D-1 rejects supervisord.
- **`/var/lib/docker` must be a named volume** — for image-layer reuse across restarts more than
  for correctness. The nested stack rebuilds server (SDK 10 + node 22) and test images.
- **Testcontainers works unmodified.** `TestDbFixtureLifecycle.cs:28-41` uses
  `container.Real.GetConnectionString()`, whose host comes from the Docker endpoint. With a
  unix-socket daemon in the same network namespace, `localhost:<mapped>` is correct. See §5.
- **Pruning becomes safe.** The plan forbids global pruning because the host daemon is shared with
  `am-service`, `traefik`, `windmill` etc. A nested daemon is scoped, so `docker system prune` inside
  it is safe — which matters given only 56G free.

### Option 2 — privileged `dind` sidecar service, runner talks TCP

`docker:27-dind` as a separate compose service; the runner keeps `USER 1654`, stays unprivileged
and keeps only the CLI; `DOCKER_HOST=tcp://dind:2376` with TLS.

- Smallest change to the runner image and no non-root regression; privilege is confined to a
  purpose-built upstream image; the nested daemon can be reset independently.
- Weaker match to "inside itself" — the daemon is a *host-daemon peer* of the runner, so the
  nesting is a trust-boundary claim rather than a namespace one.
- Testcontainers host resolves to `dind`, which is correct **only if the test process runs in the
  runner container**. Today `run_dotnet` (`c590-remote.sh:500`) spawns tests in a separate
  container; if that container is started on the dind daemon, its `localhost` is itself and the
  host must be set explicitly. Option 1 avoids the whole question.

### Option 3 — sysbox (unprivileged nested Docker) — reject for this card

`docker info` Runtimes shows only `runc`/`io.containerd.runc.v2`; `sysbox-runc` is not installed;
`shiftfs` is not loaded; kernel is 4.15 on Ubuntu 18.04. Sysbox needs a substantially newer kernel.
Adopting it means a kernel upgrade and a reboot of a host at 182 days uptime running the live
Telegram gateway, traefik, cloudflared and windmill. Out of proportion to this card; file separately
if the security posture is wanted later.

### Option 4 — rootless `dockerd` inside the runner — note as a follow-up

Prerequisites are present (`unprivileged_userns_clone=1`, `/dev/fuse`), but on cgroup v1 +
kernel 4.15 it loses resource limits and falls back to fuse-overlayfs/vfs for storage, which is the
worst case for an image that rebuilds .NET + Node layers. Still needs seccomp/apparmor relaxation.
Not measured here. Revisit when server2 is on a modern kernel with cgroup v2.

### Option 0 — keep the host socket (what landed)

`docker-compose.session-testing.yml:12-13` mounts `/var/run/docker.sock` into the runner with
`group_add: ${DOCKER_SOCKET_GID}`. This is the shape the card rejects. Recording it only so Plan
can state explicitly that it is being superseded.

## 3. Does the git-credential / gh-access smoke still hold when nested?

**It passed on the sibling shape and the pass is real**, but it does **not** port unchanged. Two
high-severity mechanisms, both proven.

Current pass (stored evidence, 2026-09-21 22:43):
`/work/test-evidence/3537de6b7be4/git-credential-smoke/c590-result.json` =
`{"accepted": true, "diagnosis": "", "exit": 0}`; `branch.txt` =
`throwaway/c590-credential-smoke-3537de6b7be4`; `pushed-sha.txt` =
`23af7eb0952438d9a36b56eecfdffda4a672e721`. Independently confirmed against GitHub:
`git ls-remote --heads origin 'refs/heads/throwaway/*'` returns that exact SHA and ref.

Mechanism today: `scripts/c590-real.ps1:182-193` takes the **desktop's** `gh auth token`, scp's it to
`server2:/home/mc/antiphon-c590/secrets/gh-token`, and deletes it in `finally` (line 229) — which is
why that file is absent from the secrets dir now (only `stack.env` remains). `case_git_smoke`
(`c590-remote.sh:799-841`) then runs `docker run --user 0` on the **host** daemon, binding
`$ROOT/secrets/gh-token` and `$ROOT/askpass` as **host paths**, with `GIT_ASKPASS`, and does
clone → branch → commit → push.

| Risk | Severity | Evidence |
|---|---|---|
| **Bind-source namespace.** Both binds are host paths. Under a nested daemon a bind source resolves inside the *runner/dind* filesystem. Measured: with `/tmp/c604-host-secret.txt` holding `HOSTSECRET` on the host and `INNERSECRET` inside the dind container, `docker run -v /tmp/c604-host-secret.txt:/s:ro alpine cat /s` from inside dind printed **`INNERSECRET`**. Worse, a **missing** source is silently created as an empty **directory** (`stat -c %F` = `directory`), so `cat /run/secrets/gh-token` fails with EISDIR, `GIT_ASKPASS` emits nothing, and `git push` fails as an *auth* error rather than a clear "token missing" refusal. | **High** | measured 2026-09-22 |
| **Credential delivery.** The token is desktop-sourced per case and destroyed after. A *persistent* runner running tests unattended has no token unless one is provisioned on server2. Nothing in the repo does that. `docs/agent-credentials.md` owns the custody decision; "gh access parity with the Grok CLI" implies a standing server2 credential, which is a new decision, not a mechanical port. | **High** | `c590-real.ps1:179-231`, `secrets/` listing |
| **Log scrubbing.** `scrub_file` (`c590-remote.sh:18-21`) redacts only `gho_[A-Za-z0-9_]+` and `POSTGRES_PASSWORD=`. `gh auth token` can also return `ghp_`/`ghu_`/`ghs_`, which are **not** matched — a gap that exists today and gets worse when logs are produced inside a nested daemon and copied out, because the scrub must then run on the copied artifacts before they leave the runner. | **Medium** | `c590-remote.sh:20` |
| **Remote-branch residue.** Each smoke pushes a real `throwaway/c590-credential-smoke-<run>` branch and never deletes it. One exists now; a persistent runner running this repeatedly accumulates them. | **Low** | `git ls-remote` |
| **Nested egress.** No regression. Measured from a container **on the nested daemon**: `git clone --depth 1 https://github.com/michal-ciechan/Antiphon.git` → `NESTED_GIT_CLONE_OK`; `wget https://api.github.com` → `NESTED_TLS_OK`. `docker0` MTU is 1500 at both the host and the nested layer, so the classic DinD MTU/TLS-stall failure does not apply here. | **None observed** | measured 2026-09-22 |

## 4. Mutation custody — no change requested, none implied

CARD-0598 (`Backlog`) owns Linux SourceLanding custody; the landed guards stay. `SessionRunnerRuntime`
advertises `VerificationCustodyV1`/`windows-job-v1` only on Windows modern ConPTY, and
`RunnerCustodyLedger.PrepareStart` independently refuses a verification-bound non-Windows launch.
`case_testing_payload` (`c590-remote.sh:324`) asserts the built image does **not** contain the string
`VerificationCustodyV1`, and `verify-docker-stack.ps1`'s `linux-custody` case refuses if it is
advertised. A nested daemon does not touch any of this: the daemon is still pre-existing relative to
any would-be Mutation process, so candidate (b) stays rejected (plan D-14). Nothing in this rework
should relax those guards, and the `linux-custody` / `testing-payload` assertions should be kept
verbatim as the regression fence.

## 5. Second-order finding: Testcontainers mapped-port reachability is unvalidated today, and DinD fixes it

`TestDbFixtureLifecycle.CreateAsync` builds `postgres:16-alpine` and reads
`container.Real.GetConnectionString()`. There is **no** `TESTCONTAINERS_*` handling anywhere in the
repository (`grep -rn TESTCONTAINERS .` → no hits). On the current sibling shape the test process
runs in a container (`run_dotnet`, `c590-remote.sh:500`) with the **host** socket mounted, so
Testcontainers' mapped ports land on server2's host loopback while the test process's `localhost` is
its own container — the endpoint the plan required to be preflighted
("Preflight actual mapped-port reachability", plan D-6 / §"Session-created throwaway stack").
That preflight was never exercised: `dotnet-filter` / `messaging-tests` have **no evidence directory**
in the run, and `client-lint` and `client-tests` both recorded
`{"accepted": false, ...}`. So the sibling shape's Testcontainers path is unproven in either
direction. Option 1 (daemon in the same network namespace as the test process) makes
`localhost:<mapped>` correct by construction — an independent technical argument for the DinD
direction, not just conformance to the original instruction.

## 6. What Plan has to decide

1. **Sequencing against CARD-0594.** Either take it as a hard dependency and land it first, or scope
   CARD-0604 to topology + a non-session entry point and defer the session-driven acceptance gate.
   The card's acceptance sentence cannot be demonstrated otherwise.
2. **Option 1 vs Option 2** — i.e. whether the runner itself goes privileged and root-plus-drop
   (literal "inside itself", Testcontainers works unmodified), or a privileged `dind` sidecar keeps
   the runner unprivileged and non-root. Recommendation: **Option 1**.
3. **Superseding plan D-6 and `docs/docker-stack.md`.** Both explicitly reject DinD and privileged
   services. The card's ask overrides them; the supersession must be written down, and
   `DockerStackContractTests.Applications_share_nonroot_identity` /
   `Testing_services_are_unprivileged` / `Testing_runner_has_explicit_socket` re-aimed rather than
   deleted.
4. **gh credential custody on server2** for a persistent runner (see §3), with
   `docs/agent-credentials.md` as owner.
5. **Disk budget** for the nested image store (56G free; a nested build of the server + test images
   is multi-GB) and a nested-only prune policy.

## Remaining uncertainties

- The 500 at `POST /api/sessions` was reproduced from **stored** logs in the currently-running stack,
  not re-triggered by me. The two failure shapes in the log are `ConflictException: Worktree path
  already exists: /work/worktrees/card-CARD-0001` (a state-residue conflict, 23:48:36) and the
  pty-pipe timeout (23:49:36). Both must clear; only the second is CARD-0594.
- The DinD probe used upstream `docker:27-dind`, not the `session-testing` image with `dockerd`
  added. The tarball payload is confirmed to contain `dockerd`, but the Debian-based image has not
  been built and booted with it (apt `iptables` dependency unverified in practice).
- Whether `overlay2`-on-`overlay2` stays healthy for large multi-GB builds was not load-tested; the
  probe only started the daemon and pulled alpine. A named `/var/lib/docker` volume sidesteps this.
- `client-lint` and `client-tests` are red in the stored evidence and were not investigated; they are
  CARD-0590's, not this card's, but they mean the current shape has never run a full green pass.
- Server log noise: an rdkafka producer retries `localhost:19092` every 30s despite
  `ChannelBridge__Enabled=false`, flooding the container log (~580k lines). Not this card, but it
  makes the stack's logs nearly unreadable for diagnosis.

## Not done, noted

- Fix idea (one line, not designed here): extract the full `dockerd`/`containerd`/`runc` set from the
  tarball the `session-testing` stage already downloads, run the daemon on a named
  `/var/lib/docker` volume in a privileged persistent runner with a fixed compose project and
  `restart: unless-stopped`, and deliver the gh token into the runner's own filesystem rather than a
  host path.
- Probe cleanup: the `c604probe` container was `docker rm -f`'d and the `docker:27-dind` image
  removed from server2; `/tmp/c604-host-secret.txt` deleted. Verified zero `c604probe` containers
  remain. No other server2 state was modified.
