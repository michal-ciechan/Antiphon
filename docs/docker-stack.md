# Self-contained Docker stack (CARD-0590)

## Shared server2 runner caches (CARD-0849)

`docker-compose.server2-runner.yml` declares three external local Docker volumes shared by
the `antiphon-runner` and `antiphon-runner-temp` Compose projects:

| Volume | Container path | Purpose |
|---|---|---|
| `antiphon-runner-cache-nuget-packages` | `/home/app/.nuget/packages` | NuGet global packages |
| `antiphon-runner-cache-nuget-scratch` | `/var/cache/antiphon/nuget-scratch` | NuGet cross-process filesystem locks |
| `antiphon-runner-cache-npm-content` | `/home/app/.npm/_cacache` | npm package content |

Each mount uses `volume.nocopy: true`. The runner sets `NUGET_PACKAGES`, `NUGET_SCRATCH`
and `NPM_CONFIG_CACHE` to those paths. The shared scratch path is part of the package-cache
contract: a process must use the shared package and scratch pair together, or a fully
private pair. Worktrees, `obj`, `bin`, `node_modules`, `/state`, `/work`, `/tmp`, provider
homes, configuration and credentials remain outside these caches. In particular, each
runner project's `runner-tmp` volume stays private and `/tmp` stays mode 1777.

The host helper accepts only these names, the local driver, empty driver options and
labels `io.antiphon.owner=server2-runner`, `io.antiphon.cache-schema=1` and the matching
cache role. It initializes roots as uid/gid 1654 at mode 0700 and probes create, rename
and delete as that uid. A foreign volume, unsafe root or unmarked content is a refusal.
The maintenance lock is `/home/mc/antiphon-server2/locks/cache-maintenance.lock`.
These external volumes survive `docker compose down -v`; that command still removes the
temp project's private work, state, dind and tmp volumes.
The image creates `/home/app/.nuget` and `/home/app/.npm` for uid 1654 before Docker
mounts their child cache volumes. This lets an offline restore create NuGet's home files.

### Volumes-only cold first seed

When the three external volumes have not been populated, the operator can seed them
while the standing `server2` runner remains accepting work:

```powershell
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed -Cold
```

Run this only from the trusted desktop checkout at the reviewed full
`C849_DEPLOY_SHA`. `-Cold` is valid only for Seed and cannot be combined with
`-SavedDonor`. The host maintenance lock protects this supported lane. Before
any volume is created, it checks the retired temp status, one identified main
container, every container's mounts including stopped containers and bind
ancestors, and all three volume roots. Existing roots must be correctly
labelled, owned by uid/gid 1654, mode 0700 and empty, including hidden files.
The host repeats these checks around initialization, each uid-1654 canary
create/rename/delete probe, and marker publication. A failed probe removes only
its owned helper and canary; unaccepted empty volumes may remain for diagnosis.
There is no package copy, restore, apphost build, smoke or build-slot lease.

Success writes a distinct `schema=2`, `kind=cold` marker and reports
`C849_SEED kind=cold ready=true writable=3`. It carries the reviewed source SHA,
inspected image identity and the three fixed volume names, with no payload hash
or recovery directory. Normal `deploy-temp` reuses this marker without a donor
and verifies shared/private mounts, `/tmp` mode and uid writability before temp
admission. `Both` and `Retired` accept cold receipts without apphost smoke or
package hash; a valid cold marker remains usable as ordinary builds populate
the caches. Their full-marker receipts retain the prior smoke/hash checks.
Reset, Prune, and explicit saved/live donor maintenance require a full marker;
they refuse a cold marker with `CacheFullSeedRequired`. An absent marker never
starts cold creation implicitly. The standing runner's drain/zero gate for
redeploy-old remains unchanged. After temp verification, complete a canary Plan
task on temp before the explicit `drain-old` phase.

Package contents are best effort. Cache misses may make the first builds slower,
but missing package payload does not block admission; wrong mounts or unwritable
roots do. An optional pre-redeploy snapshot of old package/npm trees can be
saved after drain-old and before redeploy-old. It is not a Seed source or a
rollout gate. Keep the existing saved archive untouched.

### Legacy full seed and maintenance

For a full-marker seed, refresh both runner statuses and cache inventory. Wait
for the already drained temp runner to reach fresh non-null zero `sessions`,
`runnerSessions` and `queuedTasks`. Use the explicit Seed front door from the reviewed
desktop checkout with `C849_DEPLOY_SHA` set to its full landed SHA. The host helper stops
only that idle donor container, copies the NuGet packages and npm content from its
writable layer, checks complete net9 host/reference packages, and starts the same old
container again under its drain after the disconnected apphost probe and recovery copy.
Do not replace or retire that donor when Seed has refused or before its reconnect with
fresh zero counters is recorded. No `/tmp` contents or NuGet scratch locks are copied.
When the old temp container has already been retired, import the operator's saved cache
archive explicitly. From the reviewed, landed desktop checkout, set `C849_DEPLOY_SHA`
to that checkout's full SHA and run:

```powershell
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed -SavedDonor /home/mc/runner-cache-donor/temp-runner-cache.tar
```

`-SavedDonor` is an absolute path on server2, never a desktop file. A directory is also
accepted. Its cache roots may be `packages` and `npm`, `.nuget/packages` and
`.npm/_cacache`, or `home/app/.nuget/packages` and `home/app/.npm/_cacache`. The
archive is inspected before extraction; links, traversal, other entries and oversized
content refuse. Both runner statuses must be drained with zero counters (only the
retired temp's disconnected `runnerSessions` may be null), the temp project must have
no containers, all cache volumes must have no attached containers, and the build broker
must be idle. Seed checks the volume identities and labels, the complete net9 host and
reference packages, npm integrity and a leased uid-1654 apphost build before retaining
the recovery copy and publishing the ready marker. A rerun verifies that marker and its
payload. The saved archive stays untouched.

If the archive lives elsewhere on server2, replace the example `-SavedDonor` value
with its actual absolute, canonical host path; the host user must be able to read it.
Do not copy the archive into the checkout. A successful seed keeps a recovery tree of
about 2.8 GB at the `recovery=` path in
`/home/mc/antiphon-server2/cache/seed-accepted`. After the shared cache and rollback
window are accepted, inspect that exact path and remove only that recovery directory
with `rm -rf -- /home/mc/antiphon-server2/cache/recovery-<run-id>`. A refused seed
normally removes its own `stage-<run-id>-<suffix>` directory; inspect the cache
directory for any leftover stage from an interrupted run and remove only that exact
stage path after confirming no Seed is running. Older root-owned stages may need a
scoped `sudo chown -R mc:mc -- <exact-stage-path>` first. Keep the saved archive until
the recovery copy is no longer needed.

If the import refuses after copying into an unmarked volume, leave both drains held and
use the Reset command below only after confirming all cache consumers are detached;
then correct the source and repeat Seed. To roll back a completed import, drain both
runners, retain the saved archive and recovery copy, and switch to the prior image only
after its cache mount and apphost smoke checks pass. When there is no saved donor, warm
a temporary runner in its private cache, drain it to zero, then use Seed without
`-SavedDonor` before deploying either runner against the shared volumes.
After temp retirement, an accepted full ready marker and verified volume payload allow the
next `deploy-temp` to reuse the caches when the status is retired, unavailable and not
dispatch eligible, its bound sessions and queue are zero, and the host confirms the
temp project has no containers. The absent live connection may make only
`runnerSessions` null. If a donor container exists, Seed still requires its drained
zero-counter status and reconnect receipt.

An interrupted first Seed may leave imported content without a ready marker. For that
specific state, drain both runners and wait for zero `sessions`, `runnerSessions` and
`queuedTasks`; keep the broker idle and detach every container, including stopped
containers, from all three cache volumes. From the trusted desktop checkout at the
reviewed SHA, run exactly:

```powershell
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Reset -Sha <landed-sha>
```

Reset holds the cache maintenance lock, validates all three volume names, labels,
drivers, root owners and modes, rechecks that no container uses them, and clears only
their contents. It refuses an accepted marker. Re-run `-Case Seed` after Reset. A
failed Seed removes its temporary staging directory; retained recovery generations
remain for operator inspection.

The rolling sequence remains explicit: CP-3 Fixture and inventory, Seed, `deploy-temp`,
`drain-old`, `redeploy-old`, CP-4 Both, `drain-temp`, `retire-temp`, then CP-5 Retired.
The deploy wrapper verifies the real mounts and a uid-1654 net9 apphost restore, build
and executable run before clearing either drain, even on a same-SHA rerun. A failed
verification leaves the affected runner held. CP-3/4/5 are trusted desktop and host
operations at those gates; repo tests alone do not establish live acceptance.

Run the following from the trusted desktop checkout after setting `C849_DEPLOY_SHA` to
the full reviewed, landed SHA. The front door requires checkout HEAD to equal it and
prints the local receipt directory under `.antiphon/c849-<run-id>/`. It does not start
a rolling phase or clear admission on its own.

| Gate | Command | Required receipt summary |
|---|---|---|
| Before seed/deploy, CP-3 | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture` | `C849_FIXTURE groups=9 controls=26 expectedRed=26 inventories=2 failures=0 productionMutations=0` |
| Drained temp donor, before recreation | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed` | `C849_SEED donor=server2-temp ready=true smoke=passed recovery=retained` |
| Retired temp, saved donor available | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed -SavedDonor /home/mc/runner-cache-donor/temp-runner-cache.tar` | `C849_SEED donor=saved ready=true smoke=passed recovery=retained` |
| After redeploy-old, before drain-temp, CP-4 | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both` | `C849_BOTH runners=2 smokes=2 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0` |
| After retire-temp, CP-5 | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Retired` | `C849_RETIRED externalVolumes=3 tempPrivateVolumes=0 mainTmpRetained=true smokes=1 rollback=retained failures=0` |

`-Case Inventory` refreshes two read-only status and mount receipts. The Fixture case
uses run-scoped Docker resources and records `PASS F-1` through `PASS F-9` and all
`CONTROL PC-01` through `PC-26` expected refusals. It reads production only for the
two inventories and a validated copy of the donor's net9 host/reference package.
Its temporary containers, networks and volumes are removed by exact recorded names;
the production cache names are outside its namespace. A nonzero case or missing
receipt leaves the checkpoint pending. CP-3/4/5 are operational gates after Review,
not Code-stage repo tests.

Each front-door invocation copies filtered host evidence into its printed local
directory. The fixture summary contains source/image identity, group/control counts
and no package data. Both stores separate status, mount, seed-hash and apphost smoke
receipts for each runner. Keep those receipts with the rollout record. The full
`docker inspect` output, environment, cache archive, package metadata contents and
provider credentials do not belong in that record.

The initial size budgets are 10 GiB packages, 2 GiB npm content and 256 MiB scratch.
Review `du -s -B1` for each at every deployment, before retirement and at least weekly;
warn at 80%, and require maintenance above a budget or below 20 GiB backing-space
headroom. These are review limits, not Docker quotas. Cleanup starts with
`pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case PrunePreview` and only
then `-Case Prune -Preview <receipt-path>`. Both runners must remain drained with zero
counters, no cache consumers and no active host build leases. Preserve volume roots and
one bounded recovery generation. Refill required packages under a build lease and pass
the disconnected smoke before admission; never clear all NuGet locals, prune Docker
volumes, or remove a mount root. Preserve the recovery generation for seven days after
successful activation, then remove only its recorded path during maintenance.

For rollback, keep the previous runner and state-init image IDs under explicit rollback
tags before the rollout. Drain before replacing either container and verify the image
SHA, mounts, private `/tmp` mode and apphost before clearing its drain. Keep shared
packages and shared scratch together when a prior image can use them. If the mount design
is suspect, drain every consumer and use a private package/scratch pair seeded from the
saved recovery copy. Retain external caches for diagnosis; never force-stop a live task
to meet a rollout estimate.

**No application service ever mounts a Docker socket.** Superseded 2026-09-22 by CARD-0604: the sibling-socket lane is retired, and with it `docker-compose.session-testing.yml` and `DOCKER_SOCKET_GID`. The netns split it caused (a mapped port lands in the HOST's namespace while the test process reads its own `localhost`) is why Testcontainers never worked on that shape.

The root `Dockerfile` publishes the server for `linux-x64` with SDK 10 and the ASP.NET 9 runtime. `docker/session-runner-grok/Dockerfile` keeps the phone-home runtime as its default target. `receipt-probe` adds FakeGrok and no Docker tooling at all. `session-testing` is the **persistent server2 runner**: it carries the whole Docker engine from one pinned tarball (`dockerd`, `containerd`, `runc`, the shims, `docker-proxy`, `ctr`), `iptables` pinned to the legacy backend (server2 is kernel 4.15 with iptables 1.6.1), `iproute2`, `openssh-client`, SDK 10 with runtimes 9 and 10, Node 22 and PowerShell. `runtime-base` (and so every runner target) carries Grok 1.0.40, Claude Code 2.1.280, and codex-cli 0.160.0. Claude Code is pinned by SHA-256 `1e08503dbdf3c2cb0d706d32f3408277388d1c76ef108673e8fe42c1b322925b` and verified with `sha256sum -c` before install (CARD-0628); Codex is pinned by npm's platform-package SHA-512 and verified before unpacking (CARD-0904). No credential for either is ever baked. Every runner target needs **minimum Git 2.43**: `LandingGit`, `GuardedWorktreeRemoval` and `GuardedVerificationRemoval` run `git show-ref --exists` (new in 2.43) to tell a missing ref from a lookup error, and landing inspection fails on anything older. Debian bookworm ships 2.39.5 and bookworm-backports carries no git, so the `git-build` stage compiles Git 2.47.3 from the kernel.org tarball pinned by SHA-256 `c073471530e92b716641ea2b381fcd0ece53eea9a76a9c5415f93f89e870dd5f` (verified with `sha256sum -c` before unpacking), with `sysconfdir=/etc` so the baked `/etc/gitconfig` stays in force; `runtime-base` copies it to `/usr/local`, installs no apt git, and fails the build when `git --version` is below `GIT_MINIMUM_VERSION` (CARD-0661). Raising the pin means a new version/SHA-256 pair, never dropping the check. It starts as root under tini so `docker/session-runner-grok/dind-entrypoint.sh` can run its own `dockerd`, then drops the runner to uid 1654 with the `docker-nested` group (gid 1656). A dead daemon takes the container down; `restart: unless-stopped` brings both back.

`docker-compose.server2-runner.yml` (project `antiphon-runner`) is the persistent deployment and the only standing Antiphon process on server2 — no server, no Postgres. It is the one privileged service in the repository, it owns the `dind-data` volume as the nested daemon's store, and it requires both file secrets (`ANTIPHON_DEPLOY_KEY_FILE`, `PHONE_HOME_SECRET_FILE`) by interpolation. It also requires `CLAUDE_OAUTH_TOKEN_FILE`, the Claude setup-token file on the server2 host (0600). A deploy leaves that file alone unless `-RefreshClaudeToken` is passed; the host lane creates it empty when it is missing. It is mounted read-only at `/run/antiphon/claude-oauth-token`; `dind-entrypoint.sh` exports `CLAUDE_CODE_OAUTH_TOKEN` from it into the runner alone. No compose file lists that variable under `environment:`, where `docker inspect` would print it (CARD-0628). The throwaway stack a session stands up is the **nested** child: the unmodified base `docker-compose.yml` on the runner's own daemon, in a run-scoped project, torn down with `down -v`.

The runner mounts the named `runner-tmp` volume at `/tmp` (CARD-0827). The persistent `antiphon-runner` and temporary `antiphon-runner-temp` Compose projects each get their own volume; `compose_temp down -v` in `scripts/c590-remote.sh` removes the temporary project's volume at retirement. An existing container's writable-layer `/tmp` is not migrated when this mount is introduced. Keep cleanup restricted to an explicit allow-list of disposable families: `/tmp/antiphon-pty-hosts` is runner state and must never be swept by an `antiphon-*` name pattern. Its `bin/<stamp>` shadow copies are reused when the published host's content hash matches and the host executable and deps.json are present. If either file is missing, `ShadowCopyStore.EnsureCurrent` stages a new generation and `PtyHostLauncher` refreshes its cached path before the next launch; the damaged generation remains until normal cleanup. The runner's startup adoption sweep checks persisted manifests against host liveness, records dead hosts as exited, and removes invalid manifests; `AuditCleanupService` prunes unreferenced shadow copies and old logs. These paths tolerate stale generations, logs, and manifests after a recreate. Protect `/tmp/antiphon-pty-hosts` from external cleanup even though the launch path can repair a missing host executable or deps.json.

`PhoneHome__Capacity` on that compose file is `"10"` (CARD-0653): ten concurrent sessions on server2 (24 cores, ~98 GB RAM). The server refuses a registration or capacity push above that runner’s `PhoneHomeRunner:Runners:<id>:MaxCapacity` (default 10); legacy single-runner configuration uses `PhoneHomeRunner:MaxCapacity`. The runner counts a seat only while the session is live or occupied; exited and vanished records do not fill it. A runner-bound task stays Queued, with one deduplicated Held trace, when those seats are full, and it is checked before remote prep. Those tasks do not consume `Delegation:MaxConcurrentTasks`. `PUT /api/session-runners/{runnerId}/capacity` changes a live runner's declared value with a reason and persists it at `PhoneHome:CapacityStatePath` (default `/state/runner-capacity`); that persisted value takes precedence over the compose default at restart. Heartbeats report the applied value so the server catches up if a push confirmation times out. `PUT /api/hosts/{hostId}/budget` separately sets a server-side admission limit; effective runner admission is the minimum of that budget and the runner declaration. Lowering either limit holds new work without killing running sessions. `GET /api/session-runners/{runnerId}/slots` lists the seats the runner remembers (state, age, whether the seat counts, custody). `POST /api/session-runners/{runnerId}/slots/{sessionId}/release` and `POST /api/session-runners/{runnerId}/slots/release-orphans` each require a reason and the operator token (the `X-Antiphon-Operator-Token` header; a loopback address is not a credential because the public vhost reaches Kestrel through Caddy and Vite), kill the process tree, and delete the durable manifest so a restart does not adopt the session again. `scripts/runner-slots.ps1` calls those routes.

The same compose file sets `SessionRunner__HostStats__Volumes__0: /work` and
`SessionRunner__HostStats__Volumes__1: /state` (CARD-0718). The runner samples those volumes
every five seconds and serves `/host-stats` locally and over phone-home. Its 30-minute series
is in memory and resets on runner restart; the server's `/api/hosts/stats` cache is likewise
not a durable history. On Linux these are the worktree and state volume views, even when they
share one underlying filesystem.

The same compose file sets the server2 **host build budget** (CARD-0589): `SessionRunner__BuildSlots__MaxConcurrent: "4"`, `SessionRunner__BuildSlots__MaxCpuCount: "6"`, `SessionRunner__BuildSlots__MinAvailableMemoryMb: "16384"`. Ten seats share four concurrent build/test driver leases at `-maxcpucount:6` each, and no lease is granted below 16 GB available (24 cores, no cgroup memory cap, other services on the host). Seats are counted first, then slots, then the memory floor: a free seat can still queue its builds, and a free slot never admits a session. To change the budget, edit those values and redeploy the runner; `SessionRunner__BuildSlots__Enabled: "false"` makes every acquire answer unlimited. Check it after a deploy with `docker exec antiphon-runner-session-runner-1 curl -fsS http://127.0.0.1:8080/build-slots` (200, `budget` equal to the configured value). Delegates in the container reach it at that address, the default of `scripts/lib/build-slot.ps1` on Linux (`ANTIPHON_BUILD_SLOTS_URL` overrides it). Contract, wait lines and exit codes: [testing-and-build.md](testing-and-build.md), Build slots (CARD-0589).

### server2 runner startup identity files (CARD-0631)

Everything the persistent runner boots with that is *who it is*, rather than *what it runs*, lives on server2 under `/home/mc/antiphon-server2/secrets/`, never in the image: files owned by `mc`, plus the Codex home, a directory owned by the runner uid 1654 (CARD-0660). `deploy-parent` (`scripts/c590-remote.sh`, host lane) creates each missing file or directory and records only presence or non-secret values as evidence; `docker/stack.env.example` lists the compose variables.

| File on server2 | Secret? | Created / delivered by | Compose variable → mount in the runner | How the runner uses it |
|---|---|---|---|---|
| `secrets/deploy_key` (+ `.pub`) | **Yes** (private key, 0600) | `deploy-parent`, `ssh-keygen` once; the public half is registered on GitHub by the operator | `ANTIPHON_DEPLOY_KEY_FILE` → compose secret `/run/secrets/antiphon-deploy-key` | `dind-entrypoint.sh` installs it at `/run/antiphon/deploy-key` (0400 uid 1654, tmpfs); `ssh_config` names it for pushes only |
| `secrets/phone-home` | **Yes** (0600) | `deploy-parent`, `openssl rand -hex 32` once | `PHONE_HOME_SECRET_FILE` → compose secret `/run/secrets/phone-home` | Staged at `/run/antiphon/phone-home` (0400 uid 1654); `PhoneHome__SecretPath` |
| `secrets/claude_oauth_token` | **Yes** (0600, may be empty) | `deploy-parent` leaves an existing file unchanged. `-RefreshClaudeToken` (or manifest `refreshClaudeToken`, or `ANTIPHON_REFRESH_CLAUDE_TOKEN=1`) streams vault item `antiphon/server2/claude-oauth-token` over SSH stdin. The host lane creates the file empty when it is missing. A deploy does not need `BW_SESSION` | `CLAUDE_OAUTH_TOKEN_FILE` → `/run/antiphon/claude-oauth-token:ro` | Exported as `CLAUDE_CODE_OAUTH_TOKEN` into the runner alone; empty means Claude reports signed out |
| `secrets/gitconfig` | No (0644) | `deploy-parent` creates it **only when missing**, from `RUNNER_GIT_USER_NAME`/`RUNNER_GIT_USER_EMAIL` in `stack.env` (defaults `antiphon-server2-runner` / `antiphon-server2-runner@users.noreply.github.com`); an existing file is never rewritten, so edit the file itself to change the identity | `RUNNER_GIT_IDENTITY_FILE` → `/run/antiphon/gitconfig:ro` | `GIT_CONFIG_GLOBAL` names it for uid 1654 (entrypoint export, and compose `environment:` so `docker exec` sees the same). A missing, empty or incomplete file is `GitIdentityMissing`/`GitIdentityUnusable` at boot |
| `secrets/codex/` (a directory) | **Yes** (holds Codex's ChatGPT sign-in; directory 0700, owner uid 1654) | `deploy-parent` (`ensure_runner_codex_home`) creates it **empty** when missing (`sudo -n install -d -o 1654 -g 1654 -m 0700`), re-asserts owner and mode on the directory alone, refuses a symlink or non-directory (`CodexHomePathIsSymlink`, `CodexHomePathIsNotDirectory`), never reads, lists or copies it, and records only `codex-home-present.txt` / `codex-auth-present.txt` (`WARN CodexAuthAbsent` when there is no sign-in). The sign-in itself is the operator's: `codex login --device-auth` in the runner, or the one-time migration below | `RUNNER_CODEX_HOME_DIR` → `/state/codex` read-write in `session-runner`, and `/codex-home` in `state-init` | `CODEX_HOME` (the CLI and transcript tailer) and `PhoneHome__CodexHome` (the auth probe). `state-init` seeds the non-secret `config.toml` only when absent and owns the directory, never its contents. Only the server2 `state-init` sets `CODEX_HOME_REQUIRED=1`, so a missing mount refuses `CodexHomeNotMounted` (exit 43) there; the base `docker-compose.yml` (and the nested child built from it) has no Codex home and its `state-init` skips it. A directory, not a single-file mount, because Codex rewrites its credential file by rename on every token refresh, which a file bind mount cannot survive |

Grok is **not** a file of this kind: its OAuth store is `GROK_HOME/auth.json` on the `runner-state` volume (`/state/grok`), made once by an interactive `grok login` inside the container (see `docs/agent-credentials.md`). The Claude interactive-login fallback store `/state/claude` is likewise volume state.

**Codex home: one-time migration of the pre-amendment sign-in (CARD-0660).** Before the host directory existed, the ChatGPT sign-in was made on the `runner-state` volume and now sits at `/state/codex/auth.json` there (the runner's view; `state-init` sees it as `/runner-state/codex/auth.json`). The new runner mounts the host directory *over* the volume's `/state/codex`, so that copy is no longer visible inside the runner; it is still visible in the exited `state-init` container, which sees the volume at `/runner-state` and the host directory at `/codex-home`. After a `deploy-parent` of this revision (which creates `secrets/codex/` and brings the new runner up), run on server2 as `mc`, from any directory. The file streams from one container to the other through a pipe: it lands nowhere on the host except the Codex home itself, never passes through a terminal, and nothing prints it. Only do this while `/state/codex/auth.json` is absent in the runner; a later `docker cp` would replace a newer sign-in.

```sh
i=antiphon-runner-state-init-1
c=antiphon-runner-session-runner-1
docker cp "$i:/runner-state/codex/auth.json" - | docker cp - "$c:/state/codex/"
docker exec -u 0:0 "$c" chown 1654:1654 /state/codex/auth.json
docker exec -u 0:0 "$c" chmod 0600 /state/codex/auth.json
docker exec -u 1654:1654 "$c" stat -c '%u:%g %a %F' /state/codex/auth.json
```

The last line must print `1654:1654 600 regular file` (metadata only). The second `docker cp` takes no `-a`: that resolves the archive's owner by name inside the runner, whose user is `0:0`, and fails (`getent unable to find entry "0:0"`); the `chown` and `chmod` that follow set owner and mode. The copy lands in `/home/mc/antiphon-server2/secrets/codex/auth.json` on the host. If the first `docker cp` reports no such file, stop: the sign-in is not where this migration expects it, and a fresh `codex login --device-auth` in the runner is the alternative. The old copy stays on the volume, invisible to the runner; removing it is a separate operator decision.

**Why `GIT_CONFIG_GLOBAL`, not an `[include]` from `/etc/gitconfig`.** The global level outranks the image's system `/etc/gitconfig` (which keeps only `core.sshCommand` and `pushInsteadOf`), and it *replaces* `~/.gitconfig` and the XDG file, so an identity left in a home directory or on a volume by an earlier stopgap cannot shadow the mount. Only a repository's own config outranks it; `deploy-parent` removes a `user.name`/`user.email` it finds in the runner checkout's local config and then proves `git config --show-origin --get user.email` resolves to `file:/run/antiphon/gitconfig`, both in `/` and inside the checkout (`GitIdentityNotEffective`, `GitIdentityOverrideNotRemoved`). The mount is read-only, so a session cannot rewrite the identity later commits use.

### Runner checkout: lazy clone and deploy verification (CARD-0631)

CARD-0812 mirrors each admitted task repository through its own blobless checkout beside the
primary, for example `/work/repos/markdown-package`, while task worktrees remain flat under
`/work/worktrees/`. The desktop worktree's `origin` supplies the repository identity. The runner
admits identities under `PhoneHome:AllowedCloneSources` (default
`https://github.com/michal-ciechan/`), verifies an existing checkout's origin, and probes
secondary push access with a receive-pack dry-run before making a mirror. Typed 409 refusals are
`phone_home_repository_not_admitted`, `phone_home_repository_mismatch`, and
`phone_home_repository_push_unauthorized`; the last calls for a server2 push credential. The
probe defaults on through `PhoneHome:ProbeSecondaryRepositoryPushAccess`. A legacy request with
no `repository` still uses the primary checkout. `deploy-parent` seeds and verifies only the
primary checkout; secondary checkouts are lazily cloned and never seeded or verified there.

The runner's repository (`PhoneHome__RunnerRepository`, default `/work/repos/antiphon` on the `work` volume) is created lazily by the runner on the first workspace mirror: an anonymous `git clone --filter=blob:none --no-checkout https://github.com/michal-ciechan/Antiphon.git` as uid 1654. Fetches stay anonymous HTTPS; only pushes go over SSH with the deploy key.

`deploy-parent` **seeds a fresh volume, then verifies** that checkout. Before `compose up` starts the runner (so no lazy mirror can be in flight), it runs `state-init` once and then a one-off `session-runner` container as uid 1654 that clones anonymously with the same command into the configured repository, only when that destination is absent or empty (`runner-checkout-seed.txt` records `seeded`, `present` or `occupied`; failures refuse `StateInitFailed` / `RunnerCheckoutSeedFailed`). The lazy clone cannot bootstrap a first deploy on its own, because phone-home may still be disabled on the server at that gate. Verification follows the health and phone-home probes and precedes any image retirement or acceptance. Every probe is `docker exec -u 1654:1654` inside the runner, against the configured repository, never the host's identically named `/work/repos/antiphon` checkout and never a child volume. Named refusals: `RunnerCheckoutMissing` (no `.git`), `RunnerCheckoutInvalid` (not a worktree rooted at that path), `RunnerCheckoutOriginMismatch` (origin is not the anonymous HTTPS URL), `RunnerCheckoutFetchFailed` (`GIT_TERMINAL_PROMPT=0 timeout --kill-after=5s 120s git fetch --no-tags origin <branch>` failed or timed out). The receipt is `runner-checkout.txt`: path, origin, branch and `FETCH_HEAD` SHA.

**Fresh-volume bootstrap.** An empty `work` volume is seeded by the first `deploy-parent` itself, as above. The seed never replaces an existing or occupied destination, so `RunnerCheckoutMissing` still refuses a non-empty directory with no `.git`; the images are then left unretired. Never accept a Missing verdict as green.

**Identity file before a restart.** `persistent-restart` ensures `~/antiphon-server2/secrets/gitconfig` (created when missing, refused when a directory, a symlink or incomplete) *before* it stops the runner, so a runner deployed before the identity mount existed is never stopped into a start that cannot bind it. A symlink at that path refuses `GitIdentityPathIsSymlink` before anything touches it; only a file the deploy creates is set to 0644, and an operator's existing file keeps its mode.

Base `docker-compose.yml` publishes only the server, on `127.0.0.1:5000` unless `ANTIPHON_BIND_ADDRESS` is set. The runner and Postgres have no published ports. `docker-compose.test.yml` is nested-only: it is composed inside a session on the nested daemon, never on the server2 host daemon.

`scripts/test-docker.ps1 -Group small|backend|all` is the foreground test entry. Its preflight refuses a daemon that is not the runner's own: `docker info` `Name` must equal the process's `hostname`, or the run is `SiblingDaemonRefused`; an unreachable daemon is `NestedDaemonUnavailable`. `scripts/verify-docker-stack.ps1 -Case <literal> -Manifest <path>` runs one checkpoint. `scripts/verify-card0604-dind-runner.ps1` boots the same image on Docker Desktop and grades the nested daemon, egress, loopback, launch, key custody, restart and the three exit-3 refusals without server2 or GitHub.

SourceLanding Mutation runs on Windows (`windows-job-v1`) or on the persistent server2 runner (`linux-cgroup-v1`, CARD-0604 D-17). The Linux backend is a root-owned cgroup the tracked child is placed in before it exists, entered through `sudo -n /usr/local/bin/antiphon-custody-enter <execution-id> -- <exe> <args>` and terminated through `antiphon-custody-kill <execution-id>`; `sudoers.d/antiphon-custody` grants uid 1654 those two commands and nothing else, and the shim execs the child under `no_new_privs`, which makes sudo and every setuid binary inert for the whole tree. The runner advertises `linux-cgroup-v1` only when `LinuxCgroupCustodyProbe` passes at startup, and never advertises `windows-job-v1`. This mechanism is entirely container-internal: the session-runner container is already privileged (it runs its own dockerd), and this constrains the uid-1654 process inside it. It does not touch server2's host sudoers. An ordinary Linux image run is still not a custody receipt -- only a tracked execution in that cgroup produces one.

The class accounting checked into `tests/linux-test-roster.json` is the executable copy of the frozen plan roster. Do not drop a class because Linux is red.

Windows CP-1 through CP-5 passed on `3569ed7cd520e3a76bafc0643feb1da0b42c73ca`. Live cases run on server2 through `scripts/c590-remote.sh`, invoked by `scripts/verify-docker-stack.ps1` when `ANTIPHON_C590_STUB` is unset. Evidence for a run is `/work/test-evidence/<run-id>/<case>/`. Stub mode still answers `UnknownCase` for a case the local guards do not implement.
PowerShell 7's `ConvertFrom-Json` returns an ISO-8601-looking manifest `tempRetiredAt` as `[datetime]`, so `Invoke-C590LiveCase` reformats it with `ToUniversalTime().ToString('o')` before validating; a case must never cast such a field directly to `[string]` (CARD-0780).
