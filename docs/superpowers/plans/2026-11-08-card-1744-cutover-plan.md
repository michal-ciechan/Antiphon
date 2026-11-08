# CARD-1744 plan: cut the production control plane over to the Linux server on server2

Date 2026-11-08. Plan task `2fafa091` (Frontier). Base `origin/master`
`80cb252b9ea548e47e2fdaa28cd775f76ed60261`. Documents only: no product code, nothing deployed,
started or restarted on any host. Parent decision:
[CARD-1221 contract](../../investigations/2026-11-07-decide-card-1221-linux-docker-production-contract.md),
transcribed in [docs/docker-stack.md](../../docker-stack.md#linux-docker-production-contract).
Broker decision: [CARD-1746](../../investigations/2026-11-07-decide-card-1746-broker-placement.md).
Database move: [CARD-1401 plan](2026-10-21-card-1401-postgres-to-server2-plan.md).

Live readings in this plan were taken 2026-11-08 05:30-05:50Z from the server2 phone-home runner
container (`6f03ea7dd3f9`), read-only: TCP connects, `GET` routes on the production API, and one
`BEGIN READ ONLY` session against the desktop Postgres. No password, token or key was printed.

## Plain-English summary

- Today the whole control plane is the desktop: Aspire AppHost runs the server, the desktop
  Postgres volume `antiphon_pgdata` holds the data, and `https://antiphon.desktop.codeperf.net`
  points at the desktop. server2 only runs the phone-home runner. Nothing has moved yet: the
  server2 host Postgres port 5432 is still refused, so the CARD-1407 database window has not run.
- Cutover is a two-window job, not one. Window 1 (already planned, CARD-1401/1406/1407) moves the
  database to server2 while the desktop server keeps running. Window 2 (this card) swaps the
  server process from the desktop to a container on server2. Window 2 moves no data, so rolling it
  back loses nothing.
- Keep the public name. Repointing `antiphon.desktop.codeperf.net` at server2 means the runner, the
  agents and every script that already use that origin keep working without a redeploy. The name
  can be renamed later as its own card.
- The card's "copy the key ring" step does not work: the desktop ring is wrapped with Windows DPAPI,
  which Linux cannot read. The production database holds zero managed TUI secrets and one API key
  row, so the Linux server gets a fresh ring protected by an installation certificate and the one key
  is re-entered through the write-only route.
- Two things the card does not mention would break orchestration on day one: standing always-on
  agents cannot run on a phone-home runner (the code refuses it), and the land operation pushes from
  the server process using each project's desktop checkout path. The production compose (CARD-1775) therefore
  carries a session-runner sidecar (the CARD-0590 shape), and CARD-1776 gives the server Linux
  clones and a push credential.
- Hangfire's durable storage (CARD-1743) is on master but opt-in. Its lock-lifecycle follow-ups
  (CARD-1768, CARD-1774 A-1/A-2) are not landed. Cut over on durable storage only if they are on the
  cutover tip; otherwise cut over in-memory, as today, and flip later with one container recreate.
- The desktop must fail closed after cutover: maintenance marker on, AppHost logon task disabled,
  and the AppHost connection-string user-secret removed, so a stray AppHost start cannot become a
  second writer. Rollback is the reverse, and the desktop Postgres volume is never deleted.
- Operator-only steps: the CARD-1406 drill and the CARD-1407 window, every `-Execute`, the key-ring
  certificate and the env files, DNS/vhost, the AppHost user-secrets and Scheduled Tasks, anything
  touching `antiphon_pgdata`, and tags/publish/promote.

## Recommended next action

Run the database move first: the operator rehearses the CARD-1406 scratch restore drill on the
server2 host, then books and runs the CARD-1407 window. Everything in this plan after the
preconditions table waits on that, and the two Code cards filed below (CARD-1775 production compose with
sidecar, CARD-1776 Linux land credential) can be built in parallel while it happens.

## Decisions

| Id | Decision | Why | Rejected |
|---|---|---|---|
| D-1 | Two windows. Window 1 is the existing CARD-1407 database move (topology A2). Window 2, this card, swaps only the server process; the database stays on server2 throughout. Window 2 starts no earlier than 24 h after the CARD-1408 monitor exits 0 for window 1. | Window 2 then has no dump, copy or restore, so its downtime is minutes and its rollback (stop the Linux server, start AppHost on the same database) loses no rows. The CARD-1407 script is built and tested for A2; reusing its phases 0-3 and diverging at phase 4 is an untested path. | One combined window (data move plus process move): longer downtime, the CARD-1401 phase 6 loss rule applies to every row written after the repoint, and the fail-closed tricks of CARD-1407 (user-secret, maintenance marker) would have to be re-derived for a different end state. |
| D-2 | Keep the public origin name `https://antiphon.desktop.codeperf.net`. The operator repoints DNS/vhost at the server2 host. `PHONE_HOME_SERVER_ORIGIN`, `Delegation:ApiBaseUrl`, `PhoneHomeRunner:Runners:server2:CallbackOrigin` and every `ANTIPHON_API` consumer keep the same value. | The runner reconnects to the same origin with its normal backoff and no container recreate, so live sessions survive the window. No orchestrator instruction, skill, memory or doc changes on the day. | A new name such as `antiphon.server2.codeperf.net`: forces a rolling runner redeploy for one env var (CARD-0934 phases, hours of drain), edits in every `ANTIPHON_API` consumer, and leaves the old name serving 502s from a stopped desktop. Rename later as a separate card once topology B is stable. |
| D-3 | `Hangfire:Storage=PostgreSql` at cutover only when CARD-1768 and CARD-1774 A-1/A-2 are on the cutover tip. Otherwise cut over with `InMemory` (today's desktop behaviour) and flip storage later by one container recreate through the CARD-1745 gate. | On master the lease is one `pg_try_advisory_lock` at startup with no re-check; CARD-1768 adds the tick that stops the host, and CARD-1774 A-1 says a short Postgres blip can then restart the only control plane. Recurring jobs are re-registered at every startup either way; durable storage adds queued jobs and schedule state, not the registrations. | Durable storage regardless of 1768/1774: the first Postgres hiccup after cutover becomes a server restart on the host that has no watchdog yet. |
| D-4 | Fresh Linux key ring at `/state/keyring`, protected with an installation X.509 certificate (`AgentTui:KeyProtection:Mode=X509Certificate`, PEM or PKCS#12 on the state volume, private key mode 0600, uid 1654, outside the content root). Re-enter the one `ApiKeys` row through `PUT /api/api-keys/{name}`. No ring copy. | `ResolveProtectionMode` picks DPAPI-current-user on Windows, so the desktop ring's key XML is wrapped with the desktop user's DPAPI and cannot be unwrapped on Linux. On Linux, `Auto` without a certificate resolves to "not ready": automatic key generation is disabled and every `Protect`/`Unprotect` throws. Production holds 0 `AgentTuiSecrets`, 1 `ApiKeys`, 4 `AgentTuiProfiles` (2026-11-08 read). | Copying `%LOCALAPPDATA%\Antiphon\DataProtection-Keys`: unreadable on Linux. A one-off export/re-protect tool: new code that handles secret plaintext, for one row. |
| D-5 | The production compose carries a `server-session-runner` sidecar (the root `docker-compose.yml` CARD-0590 shape: unprivileged grok image, shared `work` and `runner-state` volumes, `SessionRunner__BaseUrl http://server-session-runner:8080`, Herdr off). It hosts the three specialists and the always-on standing agents, re-homed to Linux checkouts under `/work/agents/<name>`. Delegated tasks keep going to the phone-home runner server2 (runner default `server2`). | `PhoneHomeLaunchPolicy` refuses `AlwaysOn` on every phone-home shape (`phone_home_pool_refused`), and specialists launch through the direct runner, so with `SessionRunner__Enabled=false` (the staging shape) there is no check interpreter, diagnose, distiller or standing orchestrator after cutover. The direct runner shares files with the server (`AgentSessions:SessionLogPath`, `ZombieCensus:SessionLogPath`, worktree roots), so it must be in the same compose project. | Direct runner on the desktop over the tailnet (`SessionRunner__BaseUrl=http://100.79.51.37:17204`): the server reads the runner's session log files from disk, which does not cross hosts; the runner also binds localhost and the desktop firewall timed out on 17202. Waiting for CARD-1695 (always-on over phone-home): High/Soon but unscheduled, and it would gate the operator's top priority. |
| D-6 | Desktop fails closed after cutover: `set-apphost-maintenance.ps1 -EnterIfAbsent` (marker, watchdog disabled), `Disable-ScheduledTask 'Antiphon AppHost'`, and remove the AppHost user-secret `ConnectionStrings:DefaultConnection` so the tracked `localhost:17280` string is back and nothing listens there. Rollback re-adds the secret and clears maintenance. | After window 1 the AppHost user-secret points at the server2 database. A logon or a watchdog fire would start a second server against it within minutes. With `InMemory` Hangfire nothing refuses a second worker. | Leaving AppHost installed as it is: relies on nobody logging on and on the watchdog staying disabled by hand. |
| D-7 | Production project `antiphon-server2-server`, service `server`, container `antiphon-server2-server-server-1`, published `127.0.0.1:17400:8080` on the server2 host, joined to the external network `antiphon-server2` so the connection string is `Host=postgres;Port=5432` (the CARD-1406 service alias). The CARD-1745 gate config on the host names this container and `http://127.0.0.1:17400`. | Loopback publish keeps the server behind the host ingress only; the gate refuses desktop ports 17200-17209 and names containing `runner`, `postgres`, `gateway` or `phone`. Same-host network avoids a tailnet hop and the published 5432. | Publishing on the tailnet IP (no consumer needs it). Reusing the staging port 17420 (staging and production must stay distinct projects). |
| D-8 | Land on Linux: the server image gets the runner's credential pattern (read-only token directory, `/usr/local/bin/antiphon-github-credential`, `GIT_CONFIG_GLOBAL`, push allow-list; CARD-0604), per-project clones under `/work/repos/<name>` on the production `work` volume, and `Project.LocalRepositoryPath` rewritten at cutover from a recorded before/after table. | `LandingGit` pushes from the server process (`git push <endpoint> <sha>:<ref>`), in the project's `LocalRepositoryPath`. Every project path today is a desktop Windows path (`C:/src/Antiphon`, `C:/src/gym-stat`, `C:\src\markdown-package`, ...). The server image has git but no credential. | Delegating land to the runner (not how `/land/v2` works). One SSH deploy key per repository (the existing PAT already covers the owner's repositories). |
| D-9 | The desktop Windows session runner continues, after cutover, as a Windows phone-home runner (`start-windows-phone-home-runner.ps1`, a non-reserved id such as `desktop-win`) for Windows-platform tasks. Herdr panes end at cutover. | The phone-home lane answers 409 `UnsupportedTarget` for Herdr, and the direct desktop runner cannot be reached or share files from a Linux server (D-5). CARD-0864/0872 keep Herdr desktop-only; the desktop is no longer the control plane. | Keeping a desktop direct runner for Herdr: impossible across hosts. Blocking cutover on Herdr parity: no card exists for it. |

## Ground truth

| The card or contract assumes | What the code or a reading says | Consequence |
|---|---|---|
| "Do not start until CARD-1406 has a verified scratch restore" and the staging compose is healthy | CARD-1406 code is on master (`0853f0266b`..`2e43ac0d8e`, board still Review). server2 `100.93.77.126:5432` and `192.168.4.34:5432` refused from the runner on 2026-11-08 05:4xZ, so the host Postgres is not deployed and the CARD-1407 window has not run. Staging `127.0.0.1:17420` is host-loopback and cannot be observed from the runner. | Window 1 (A2) is the first move. Precondition P-1/P-2. |
| "Hangfire jobs survive a restart" (CARD-1743, board InProgress) | Code on master (`ae578de950`, `6d590a5131`), opt-in `Hangfire:Storage=PostgreSql`, schema `hangfire`, advisory lock class 1743. Production database has no `hangfire` schema (InMemory today). CARD-1768 lifecycle is in Review on `feat/card-task-3a6056e7`, not on master; CARD-1774 A-1/A-2 open. | D-3. |
| "Move phone-home ServerOrigin (`PHONE_HOME_SERVER_ORIGIN`)" | The runner compose requires the variable; its value is the public origin. Changing it needs a runner container recreate, which kills live sessions unless done as CARD-0934 rolling phases. | D-2 keeps the value; the thing behind the name moves. |
| "Copy the key ring or document a re-protect" | Windows `Auto` resolves to DPAPI-current-user; Linux `Auto` resolves to not ready (`ConfigureKeyProtection` returns null, automatic key generation disabled). `DataProtectionApiKeyProtector` shares the same readiness and throws when not ready. Production: 0 `AgentTuiSecrets`, 1 `ApiKeys`, 4 `AgentTuiProfiles`. The staging compose sets no key-protection mode. | D-4. The staging server's protector is not ready today; fine for staging, not for production. |
| "`deploy-server2.ps1` default API `http://localhost:17202`" | Confirmed (`$env:ANTIPHON_API` else localhost; operator token from `%LOCALAPPDATA%\Antiphon\operator-token` or `ANTIPHON_OPERATOR_TOKEN_FILE`, never printed). After cutover the operator token is the Linux server's, created at its `Operator:TokenPath` on first start. | Slice S3 and runbook step 2.3. |
| "WorktreeLegacyRelease and RunnerWorktreeRetention: exactly one worker" | Both on in `server/appsettings.json`; legacy release files releases for desktop worktrees under `C:\Antiphon\worktrees`, which a Linux server cannot see. Retention names runner `server2`. | Linux server: legacy release off, retention on. Desktop legacy worktrees stop being released at cutover (accepted; see risks). |
| Card silent | `LandingGit` pushes from the server process in `Project.LocalRepositoryPath`; every project path is a desktop path; the server image has git but no credential helper. | D-8, slice S2, step 0.6 and 3.6. |
| Card silent | `PhoneHomeLaunchPolicy` refuses `AlwaysOn` at three admission points; specialists (check interpreter, diagnose, distiller) launch through the direct runner; staging has `SessionRunner__Enabled=false`. Seven always-on agents are Running today (Antiphon-Orchestrator, ClaudeBot-Antiphon, Gym Stat, markdown-package, school-revision, slides orchestrators, antiphon-check-interpreter) with desktop working directories. | D-5, slice S1 sidecar, slice S4 re-homing. |
| "Herdr and Windows-platform tasks stay on the desktop runner" | Herdr over phone-home: 409 `UnsupportedTarget`. The direct runner shares session log files with the server. `start-windows-phone-home-runner.ps1` reserves ids `local` and `desktop`. | D-9. |
| "Cloudflare fronts the desktop; DNS and Caddy are operator steps" | Desktop `100.79.51.37:443` open, `:17202` timeout, `:17280` open; server2 `100.93.77.126:443` open (traefik and cloudflared run on that host per the 2026-09-22 note), `:19092` open. The vhost path to a server2 loopback port is untested. | Operator-only, with a STOP rule in step 3.1. |
| Contract: database 1,265,417,239 bytes (2026-10-21) | 1,950,309,399 bytes (1860 MB) on 2026-11-08, PostgreSQL 16.14, `pg_stat_statements` 1.10. | Window 1's drill envelope (CARD-1401 D-16) must be re-measured at the new size before that window is booked. Not this card's window. |
| Contract: production serves the rc tip | `GET /api/version` = `d164e93ae47a79ed175beeb9367212f7a7d5d1d5`, an ancestor of `rc/2026-11-10` (tip `74a8004f40`), also on master. | The cutover tip is an rc tip that contains S1-S3; the desktop main checkout must be at the same tip for rollback (step 0.2). |
| Card: "Leave AppHost installed" | Scheduled Tasks `Antiphon AppHost` (1 minute after logon) and `Antiphon AppHost Watchdog` (every 2 minutes) would start it. | D-6. |
| CARD-1752: the current bridge member is the desktop server | 2026-11-07 reading: one off-network rdkafka member in `antiphon-server-bridge`, shape of AppHost with the bridge forced on. Family replies pause from step 1.6 until CARD-1752 enables the Linux bridge. | Step 3.3. |

## 1. End state: what cutover changes

| Component | Today (topology A) | After window 1 (A2) | After window 2 (topology B, this card) |
|---|---|---|---|
| Server process | Aspire AppHost `server` on the desktop, `:17202`, Development environment, bridge forced on | Same | Container `antiphon-server2-server-server-1` on the server2 host daemon, `127.0.0.1:17400`, `restart: unless-stopped`, supervised by the compose restart policy plus the CARD-1745 gate. AppHost installed, stopped, failed closed (D-6). |
| Database | Desktop `antiphon-postgres`, volume `antiphon_pgdata`, `localhost:17280` | server2 `antiphon-server2-postgres`, volume `antiphon-server2-postgres_pgdata`, tailnet 5432; desktop container stopped, volume retained | Unchanged from A2. Window 2 moves no data. |
| Hangfire | In-memory, one worker, desktop | Same | One worker in the Linux server. Storage per D-3. Census job absent (`ZombieCensus__Enabled=false`). Legacy release off. Retention on for runner `server2`. |
| Broker and ChannelBridge | server2 Redpanda `redpanda.66fe474a-...`; the desktop server is the single bridge member | Same | Same broker. Bridge member becomes the Linux server when CARD-1752 enables it (same window if CARD-1751 passed); otherwise off, replies paused. Fake gateway stays on the desktop broker. |
| Phone-home runner server2 | Phones home to the public origin, which is the desktop | Same | Same origin value; the origin now resolves to server2. Reconnects by itself. |
| Desktop Windows session runner | Direct runner `desktop` on `:17204` (Herdr, Windows-platform tasks, standing agents) | Same | Direct runner role ends. Optional Windows phone-home runner `desktop-win` for Windows-platform tasks (D-9). Herdr ends. |
| Specialists and always-on agents | Desktop direct runner, Herdr panes | Same | `server-session-runner` sidecar in the production project, Linux checkouts under `/work/agents/<name>`, no Herdr (D-5). |
| Public vhost / DNS | `antiphon.desktop.codeperf.net` -> desktop Caddy -> Vite `:17203` -> `/api` proxy -> `:17202` | Same | Same name -> server2 host ingress -> `127.0.0.1:17400` (Kestrel serves the embedded `wwwroot` client and `/api`). WebSockets must pass (phone-home connect, SignalR). |
| `deploy-server2.ps1` | Desktop checkout, API `localhost:17202`, SSH to server2 | Same | Desktop checkout, API from `ANTIPHON_API` (refuses when unset; slice S3), operator token = the Linux server's. Phases unchanged. |
| Agent TUI key ring | `%LOCALAPPDATA%\Antiphon\DataProtection-Keys`, DPAPI | Same | `/state/keyring` on `antiphon-server2-server_server-state`, X.509 installation certificate (D-4). One API key re-entered. |
| Land operation | Server process in `C:/src/<project>` with the desktop's git credential | Same | Server process in `/work/repos/<project>` with the CARD-0604 credential pattern (D-8). |
| Secrets custody | AppHost user-secrets, desktop files | Plus the CARD-1406 env file on server2 | Plus the production server env file on server2 (mode 0600, outside the repo): Postgres password, phone-home shared secrets per runner entry, Hangfire storage flag, public origin, bridge flag and bootstrap, token directory path. Nothing in the image or the repo. |

## 2. Preconditions

Every row is a stop until it is true. "Operator" means a human runs it on the named host.

| # | Precondition | Owner / card | Operator-only | Verify |
|---|---|---|---|---|
| P-1 | CARD-1406 host Postgres deployed on the server2 host and the scratch restore drill rehearsed at the current size (1.86 GB), timings recorded | Operator; CARD-1406 (`-Execute`, `-RestoreDrill`) | Yes | On the host: `docker ps --filter name=antiphon-server2-postgres` healthy; from the desktop a TCP connect to the tailnet address on 5432 succeeds; the drill record exists |
| P-2 | CARD-1407 window executed: the desktop server runs against server2 Postgres, CARD-1408 monitor exit 0, no rollback for 24 h | Operator; CARD-1407, CARD-1408 | Yes | `pg_stat_activity` on server2 shows the desktop client; desktop `antiphon-postgres` is stopped and `antiphon_pgdata` listed |
| P-3 | Staging server started once with `deploy-server2-staging-server.ps1 -Execute` and `/health`, `/api/version` observed (CARD-1742 drill) | Operator; CARD-1742 | Yes | `curl http://127.0.0.1:17420/api/version` on the host returns the built SHA |
| P-4 | Broker probe from the staging container sees cluster `redpanda.66fe474a-d399-4ea1-8cde-c606e98614a8` advertising `100.93.77.126:19092` | CARD-1751 (Code) | No | CARD-1751 report |
| P-5 | Production compose and deploy script exist with the sidecar, key-protection settings, Linux paths and refusals (slice S1) | CARD-1775 (Code) | No | CP-1, CP-2 green; `deploy-server2-server.ps1 -WhatIf` prints the host command without docker |
| P-6 | Server image push credential and clone layout (slice S2) | CARD-1776 (Code) | No | CP-4 green; on the host a one-shot `git ls-remote` through the helper succeeds for each project remote |
| P-7 | `deploy-server2.ps1` API default and token note (slice S3) | CARD-1777 (Code) | No | CP-3 green |
| P-8 | Hangfire choice made (D-3): either CARD-1768 + CARD-1774 A-1/A-2 landed on the cutover tip, or `HANGFIRE_STORAGE=InMemory` in the env file | CARD-1768, CARD-1774 (Code); operator picks | Partly | `git merge-base --is-ancestor` of both landing commits into the cutover tip, or the env file line |
| P-9 | Linux env file for the production server written on the host, mode 0600, outside the repo, with every key S1 lists; the server2 runner entry transcribed from the desktop `antiphon-server` user-secrets (names only in any report) | Operator | Yes | `deploy-server2-server.ps1 -WhatIf -EnvFile <path>` exit 0 |
| P-10 | Installation certificate for the key ring generated on the host into the production state volume, private key 0600 uid 1654 | Operator | Yes | `-WhatIf` reports the certificate files present; no key material printed |
| P-11 | Vhost route to `127.0.0.1:17400` on the server2 host prepared but not switched; WebSocket pass-through confirmed on a test hostname | Operator | Yes | `curl` of the test hostname returns the staging or production `/api/version`; a `wscat`-style upgrade to `/hubs/antiphon` is not 4xx |
| P-12 | Desktop main checkout at the cutover tip; `restart-apphost.ps1 -WhatIf -StopOnly` prints the stop plan | Operator | Yes | `git -C C:\src\Antiphon rev-parse HEAD` equals the tip; exit 0 |
| P-13 | Before/after table of `Project.LocalRepositoryPath` for every project and of `Agent.WorkingDirectory` for every always-on agent, saved in the window log | Operator (from `GET /api/projects`, `GET /api/agents`) | Yes | Table present |
| P-14 | Fresh encrypted snapshot of the server2 production database on the day; ciphertext retained on the desktop; size recorded | Operator; CARD-1405 script or the drill's `pg_dump` plus `age` line over SSH | Yes | File exists, `.dump.age`, size recorded |
| P-15 | Tags, publish, promote: none in this window | Human-only | Yes | n/a |

## 3. Runbook

Every step names where it runs, the command, the expected result, the STOP rule and the rollback
stage (R-n, section 3.7). "Host" is the server2 host shell as `mc`, never the runner container.
Commands never print a password, token or key.

### Phase 0. Pre-flight (ordinary days, no downtime)

| Step | Where | Command | Expected | STOP |
|---|---|---|---|---|
| 0.1 | Anywhere | `curl -s https://antiphon.desktop.codeperf.net/api/version` | The desktop's SHA; record it | Not 200 |
| 0.2 | Desktop | `git -C C:\src\Antiphon fetch origin` and `git rev-parse HEAD`; pick the cutover tip = current `rc/*` tip that contains S1-S3 (and 1768/1774 under D-3) | Recorded tip; desktop checkout at that tip | Desktop tip differs from the image tip (rollback would run a different migration set) |
| 0.3 | Host | `docker compose -p antiphon-server2-staging ps`; if down, `pwsh -NoProfile -File scripts/deploy-server2-staging-server.ps1 -Execute -EnvFile <staging env>` | `curl http://127.0.0.1:17420/api/version` = built SHA (P-3) | Staging unhealthy: fix staging first, nothing else is touched |
| 0.4 | Host | CARD-1751 probe from the staging container (P-4) | Same cluster id, same advertised host | Different id or timeout: CARD-1752 stays off at cutover |
| 0.5 | Host | Generate the installation certificate into the production state volume path S1 names (`openssl req -x509 -newkey rsa:4096 -days 3650 -nodes ...`), `chown 1654:1654`, `chmod 0600` the key | `-WhatIf` sees both files | Key readable by another uid |
| 0.6 | Host | Clone each landable project into the production `work` volume under `/work/repos/<name>` with a one-shot container of the server image, remotes set to the GitHub URLs, `git ls-remote` through the credential helper | One line per remote | Credential refused: fix custody (S2), do not continue |
| 0.7 | Host | `pwsh -NoProfile -File scripts/deploy-server2-server.ps1 -WhatIf -EnvFile <prod env>` | Prints `docker compose --env-file <env> -f docker-compose.server2-server.yml -p antiphon-server2-server up -d`, exit 0, no docker call | Any refusal (`volume-forbidden`, `port-forbidden`, `bind-any`, missing key) |
| 0.8 | Host | Prepare the ingress route (P-11), not switched | Test hostname answers | WebSocket upgrade refused |
| 0.9 | Desktop | Write the `desktop-win` shared secret file (`C:\Antiphon\runner\secrets\phone-home`, owner-only) if D-9 is wanted on the day; its entry is already in the Linux env file (P-9) | File present | n/a |
| 0.10 | Desktop | `pwsh -NoProfile -File scripts/postgres-cutover-monitor.ps1 Baseline -Execute -BaselinePath <log dir>\baseline.json -ProjectId d4ea7ae9-e769-474b-95b9-aa25fbc1303f` against the current origin | Baseline written | Exit 4 |
| 0.11 | Desktop | Announce the window; `scripts/keep-six.ps1 -Hold` for each standing orchestrator with `-Until` past the window | Holds listed by `GET /api/orchestrators/keep-six` | n/a |

### Phase 1. Freeze and stop the desktop server (downtime starts)

| Step | Where | Command | Expected | STOP | Rollback |
|---|---|---|---|---|---|
| 1.1 | Desktop | For each project with lands: `GET /api/agent-tasks?projectId=<id>&landPending=true` until `items` is empty | Empty | Not empty after 30 minutes: abort the window, clear holds | none needed |
| 1.2 | Desktop | `pwsh -NoProfile -File scripts/recover-repository-children.ps1 -Repository C:\src\Antiphon` (preview, no `-Execute`) | No uncertain children | Uncertain children | none needed |
| 1.3 | Desktop | Stop each always-on agent's session through the UI or the agents routes in `docs/ops-http.md`; record ids | `GET /api/agents` shows them not Running | A session that will not stop: leave it, note it (its process keeps the desktop seat; it cannot reach the server once stopped) | none needed |
| 1.4 | Desktop | `pwsh -NoProfile -File scripts/set-apphost-maintenance.ps1 -EnterIfAbsent`; `Disable-ScheduledTask -TaskName 'Antiphon AppHost'` | Marker created, watchdog Disabled, logon task Disabled | Exit 2 (marker already present: someone else is in maintenance; find out who) | R-1 |
| 1.5 | Desktop | `pwsh -NoProfile -File scripts/restart-apphost.ps1 -StopOnly` from `C:\src\Antiphon` | Exit 0; no listener on 17200, 17202, 17203, 17205, 17206, 17207, 17209; 17204 still the runner | Exit 3 or a relaunch | R-1 |
| 1.6 | Host | `docker exec antiphon-server2-postgres-postgres-1 psql -U antiphon -d antiphon -At -c "select client_addr, application_name, count(*) from pg_stat_activity where datname='antiphon' group by 1,2"` | No desktop tailnet client within 60 s | A desktop client persists: a second desktop process holds the database; find it, do not start the Linux server | R-1 |
| 1.7 | Desktop | `dotnet user-secrets remove ConnectionStrings:DefaultConnection --id aspire-antiphon-apphost` (record only that it existed) | Key absent | n/a | R-1 re-adds it |

### Phase 2. Start the Linux server

| Step | Where | Command | Expected | STOP | Rollback |
|---|---|---|---|---|---|
| 2.1 | Host | `pwsh -NoProfile -File scripts/deploy-server2-server.ps1 -Execute -EnvFile <prod env>` | `curl -fsS http://127.0.0.1:17400/health` 200 within 2 minutes; `curl -s http://127.0.0.1:17400/api/version` = cutover tip with `land-v2` | Health not 200 in 2 minutes; log line `Hangfire refused to start a second worker` (something still holds the lock: go back to 1.6); migration failure | R-2 |
| 2.2 | Host | `pwsh -NoProfile -File scripts/restart-linux-server.ps1 -Config <gate config> -ExpectedServerSha <tip>` (WhatIf probe) | Exit 0 | Exit 5 | R-2 |
| 2.3 | Desktop | Copy the operator token without display: `ssh server2 docker exec antiphon-server2-server-server-1 cat /state/operator-token \| Set-Content -NoNewline -LiteralPath $env:LOCALAPPDATA\Antiphon\operator-token` (PowerShell; file stays owner-only) | File non-empty | Empty file | R-2 (restore the previous token file from the window log location) |
| 2.4 | Host | `curl -s http://127.0.0.1:17400/api/session-runners` | The sidecar direct runner listed, platform `linux`, available; server2 absent or stale (it has not reconnected yet) | Sidecar unavailable | R-2 |
| 2.5 | Desktop | `PUT https://127.0.0.1-forwarded or host-local /api/api-keys/<name>` with the value from the vault, through the loopback port on the host (`ssh -L 17400:127.0.0.1:17400 server2`) | `GET /api/api-keys` lists the key with a new id | Protector "not ready": the certificate (0.5) is wrong; fix before 3.1 | R-2 |
| 2.6 | Desktop (tunnel) | `PUT /api/projects/{id}` for each landable project with `localRepositoryPath=/work/repos/<name>` (P-13 table) | `GET /api/projects` shows Linux paths | Validation error | R-2 restores the before column |

### Phase 3. Repoint the public origin and reconnect

| Step | Where | Command | Expected | STOP | Rollback |
|---|---|---|---|---|---|
| 3.1 | Operator | Switch DNS/tunnel/vhost for `antiphon.desktop.codeperf.net` to the server2 route (P-11) | From the desktop and from the runner: `curl -s https://antiphon.desktop.codeperf.net/api/version` = cutover tip, not the desktop SHA; `POST /hubs/antiphon/negotiate` answers | Still the desktop SHA after the TTL; 502; WebSocket upgrade fails | R-3 |
| 3.2 | Anywhere | `curl -s https://antiphon.desktop.codeperf.net/api/session-runners` every minute | `server2` available, `dispatchEligible` true, `occupied` equals the pre-window count, within 10 minutes | `Phone-home registration failed: reason=http_401` (secret) or `http_404` (entry) in the runner log | R-3 |
| 3.3 | Host | Only if 0.4 passed and CARD-1752 is ready: set the bridge flag and bootstrap in the env file, `docker compose ... up -d` (recreate), then `curl http://100.93.77.126:18090/health/inbound-unconsumed` | Group `antiphon-server-bridge` has exactly one member; state Ready | Two members or zero after 5 minutes: set the flag back to false and recreate | R-3 includes bridge off |
| 3.4 | Desktop | Optional D-9: `pwsh -NoProfile -File scripts/start-windows-phone-home-runner.ps1 -RunnerId desktop-win` from a normal terminal | `GET /api/session-runners/desktop-win/status` available | Registration refused: leave it off, Windows-platform tasks wait | stop the script |
| 3.5 | Desktop (vhost) | For each always-on agent: `PATCH /api/agents/{id}` working directory to `/work/agents/<name>` (P-13), clear Herdr labels, start | `GET /api/agents` shows each Running with a Linux working directory | Launch refused | R-3 restores the before column |
| 3.6 | Desktop (vhost) | `scripts/keep-six.ps1 -ClearHold` per orchestrator once its session is Running | Holds cleared | n/a | n/a |

### Phase 4. Post-checks (all through the public origin unless noted)

| Check | Pass |
|---|---|
| `GET /health`, `GET /api/version` | 200; SHA = cutover tip; `land-v2` and `operator-shutdown-v1` advertised |
| `GET /api/diagnostics/session-state` | `Pooling` true, `NoResetOnClose` true, `MaxAutoPrepare` 0, `Multiplexing` false |
| server2 `pg_stat_activity` (host) | Clients only from the production compose network; no desktop tailnet address |
| `GET /api/agent-tasks/pipeline?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f` | `runners` lists server2 eligible; `hosts` has `remaining` > 0 |
| One dispatch | A low-risk card named in the window log reaches server2 and shows on the board |
| One land | An eligible task lands through `/land/v2` from `/work/repos/antiphon`; receipt normal (proves D-8) |
| Specialists | `antiphon-check-interpreter`, `antiphon-diagnose`, `antiphon-output-distiller` Running on the sidecar |
| Hangfire | `scripts/hangfire-dashboard.ps1` with the new token shows the recurring jobs; `antiphon:zombie-census` absent; `antiphon:desktop-legacy-release` absent; retention present |
| Logs | Host: `docker logs antiphon-server2-server-server-1 --since 10m` has no repeated error; `/state/logs` growing |
| Window 1 bridge | If 3.3 ran: am-service `inbound-unconsumed` Ready with one member |

Then run `scripts/postgres-cutover-monitor.ps1 Watch -Execute -BaselinePath <baseline>` for 30 minutes
against the public origin. Exit 0 ends the window. Exit 2 (`ROLLBACK`) goes to R-3. Maintenance on
the desktop stays entered after the window: the marker and the disabled tasks are the end state
(D-6), so the state observer reports `maintenance=true` by design.

### 3.7 Rollback to the desktop AppHost, by stage

| Stage reached | Rollback | Expected | Data |
|---|---|---|---|
| R-1: after 1.4-1.7, Linux server not started | Re-add the AppHost user-secret `ConnectionStrings:DefaultConnection` (the server2 string); `restart-apphost.ps1 -ExpectedServerSha <tip>` from `C:\src\Antiphon`; `set-apphost-maintenance.ps1 -Clear`; `Enable-ScheduledTask 'Antiphon AppHost'`; clear keep-six holds | `GET /api/version` = tip; runner reconnects; always-on agents restart under the desktop supervisor | Nothing lost. The database never changed hands. |
| R-2: after 2.1-2.6, origin not switched | Host: `docker compose -p antiphon-server2-server stop`; confirm `pg_stat_activity` has no production-compose client; restore `Project.LocalRepositoryPath` from the before column (through the host tunnel before the stop, or after R-1 brings the desktop up); then R-1 | As R-1 | Nothing lost. Rows the Linux server wrote (its startup, the API key row, project paths) are on the same database. Replace the API key row's ciphertext again under the desktop ring only if an agent launch needs it (the Linux-protected row is unreadable to the desktop ring: re-enter it). In-memory Hangfire queue gone, expected. |
| R-3: after 3.1-3.6, origin switched | Switch the vhost back to the desktop first; if 3.3 ran, set the bridge flag false and recreate, confirm zero members; `docker compose -p antiphon-server2-server stop`; stop `desktop-win` if started; restore project paths and agent working directories from the before columns; then R-1 | Runner reconnects to the desktop (same origin); AppHost bridge rejoins as the only member; always-on agents restart on the desktop | Nothing lost at the database. Sessions launched on Linux are orphaned seats: `scripts/runner-slots.ps1 release-orphans` with a reason. Tasks dispatched during the window stay on the board and resume through the desktop. |
| Return to the desktop database | Not part of this card. CARD-1401 phase 6 applies, with its loss rule for rows written after window 1's repoint. | | |

Time to restore, central: vhost switch 1-5 minutes (TTL), Linux stop 30 s, AppHost restart 3-5
minutes (it builds), runner reconnect under 5 minutes. About 15 minutes; the first rehearsal
records the real number in the window log.

### 3.8 Data custody

| When | Source of truth | Desktop volume `antiphon_pgdata` | Snapshot and key | Lost on rollback |
|---|---|---|---|---|
| Today (A) | Desktop container | Writer | CARD-1405 `postgres-snapshot.ps1 -Execute`: `pg_dump -Fc` encrypted with `age` to `antiphon-<stamp>.dump.age`, 14 dailies; recipient and identity are files the operator owns (`-RecipientFile` / `-IdentityFile`), never printed, never in the repo | n/a |
| Window 1 (A2, CARD-1407) | Moves to server2 after phase 3 verify | Stopped, retained, read-only witness | The window dump stays on the desktop as the cutover witness | CARD-1401 phase 6 rule: rows written to server2 after the repoint |
| Window 2 (this card) | server2 throughout | Stopped, retained | P-14 day-of ciphertext retained on the desktop; dailies continue from the server2 container (`postgres-snapshot.ps1` already names `antiphon-server2-postgres-postgres-1` as a production container) | Nothing: both servers use the same database |
| After B | server2 | Retained until the operator says the copy is abandoned; this plan never deletes it | Dailies from server2; the identity file stays with the operator | n/a |

## 4. Risks

| Risk | Mechanism | Guard in this plan |
|---|---|---|
| Two writers | AppHost logon or watchdog start while the Linux server runs; a forgotten desktop process (1.6) | D-6 fail-closed; 1.6 STOP; Hangfire lease refuses a second worker only under PostgreSql storage |
| Hangfire lease | Master: one advisory lock, lost silently on a Postgres restart. CARD-1768: tick stops the host on any re-check failure (CARD-1774 A-1), own-backend false conflict (A-2) | D-3; `restart: unless-stopped` plus the CARD-1745 gate are the only supervisor; no watchdog on the host yet |
| Broker consumer group single member | One partition: a second `antiphon-server-bridge` member takes the stream | Bridge off until the desktop member is gone (1.5), CARD-1752 flips it, 3.3 checks exactly one member |
| Key ring | DPAPI ring unreadable on Linux; Linux `Auto` not ready; a missing certificate makes every protected read or write throw | D-4; 0.5, 2.5 and the STOP there; 1 API key row to re-enter |
| Specialists and standing agents | Refused on phone-home; no direct runner in the staging shape | D-5 sidecar; 3.5 re-homing; P-13 before table |
| Land path and credential | Desktop paths in `Project.LocalRepositoryPath`; no push credential in the server image | D-8, S2, 0.6, 2.6, post-check "one land" |
| Desktop legacy worktrees | Legacy release off on Linux; `C:\Antiphon\worktrees` accumulates | Accepted; file a card when the count matters (CARD-0459 lineage) |
| Herdr ends | Operator loses panes for standing agents | D-9; CARD-0864/0872 stay desktop-only |
| Vhost and hairpin | Untested route; WebSocket pass-through; runner hairpin to its own host through public DNS | P-11, 3.1 STOP, 3.2 STOP |
| Migrations forward-only | A newer image tip than the desktop checkout | 0.2 STOP |
| Database size | 1.86 GB vs the 1.2 GB the A2 envelope was computed on | P-1 re-measure; window 1 only |
| Time to restore | DNS TTL plus AppHost build | Rehearse R-3 once on the staging route before the window; record the number |
| Census, RC watch, deny hook, memory caps | Unsupported on Linux per the contract | Flags off; RC watch zero is not evidence; CARD-1222/1223/1225 |
| Ingress timeouts | CARD-1378 recorded Caddy 502s at 3 s upstream connect timeouts | Ingress connect timeout at least 10 s; note in P-11 |

## 5. Card breakdown

Duplicate search run 2026-11-08 (`card.ps1 search ... -All`) for: cutover, key ring,
PHONE_HOME_SERVER_ORIGIN, deploy-server2.ps1 default, production server compose, vhost, Caddy,
DataProtection, re-protect, Scheduled Task, AppHost rollback, server2 server image. Only CARD-1744
and its existing children matched the cutover topics; CARD-0114 (converge AgentTuiSecret) and
CARD-1695 (always-on on the server2 runner) are related, not duplicates.

| Card | Kind | Importance / urgency | Slice | Blocks |
|---|---|---|---|---|
| CARD-1406 (Review) | Operator | Critical/Now | Host Postgres `-Execute` and the restore drill at 1.86 GB (P-1) | CARD-1744 |
| CARD-1407 window + CARD-1408 monitor (Done scripts) | Operator | as parent CARD-1401 | Window 1 (P-2) | CARD-1744 |
| CARD-1742 (Done) | Operator | - | Staging `-Execute` drill (P-3) | CARD-1751 |
| CARD-1751 (Backlog) | Code | High/Soon | Broker probe from staging (P-4) | CARD-1752 |
| CARD-1752 (Backlog) | Code, run in the window | High/Normal | Bridge enable (3.3) | - |
| CARD-1768 (Review), CARD-1774 (Backlog) | Code | Normal/Soon, High/Soon | Lease lifecycle (P-8, D-3) | the PostgreSql choice |
| CARD-1745 (Review) | Code done; operator config | Normal/Normal | Gate config on the host (2.2) | - |
| CARD-1775 (new): production server compose with session-runner sidecar | Code, platform Linux | High/Soon | S1 | CARD-1744 |
| CARD-1776 (new): Linux land credential and project repository mirrors | Code, platform Linux | High/Soon | S2 | CARD-1744 |
| CARD-1777 (new): `deploy-server2.ps1` API default and operator token custody after the move | Code | Normal/Soon | S3 | CARD-1744 |
| CARD-1778 (new): re-home always-on agents and specialists to the Linux sidecar | Operator runbook plus Code if `Delegation:AllowedRoots` refuses `/work/agents` | High/Soon | S4 | CARD-1744 |
| CARD-1779 (new): docs for topology B operations | Docs | Normal/Normal | S5 | - |
| CARD-1744 itself | Operator window (phases 1-4) plus the plan | High/Normal | Runbook | CARD-1752 |

The five new cards were filed from this plan on 2026-11-08 with `card.ps1 new -DescriptionFile`:
CARD-1775 (N1), CARD-1776 (N2), CARD-1777 (N3), CARD-1778 (N4), CARD-1779 (N5). CARD-1775 to CARD-1778 block CARD-1744 (`card.ps1 block CARD-1744 -BlockedBy <id>`); CARD-1779 lands after the window.

## 6. Slices

| Slice | Files | Tests | Notes |
|---|---|---|---|
| S1 (CARD-1775) | `docker-compose.server2-server.yml` (project `antiphon-server2-server`; services `server-state-init`, `server`, `server-session-runner`; volumes `antiphon-server2-server_work`, `_server-state`, `_runner-state`; external network `antiphon-server2`; `127.0.0.1:17400:8080`; env: `Hangfire__ServerEnabled true`, `Hangfire__Storage ${HANGFIRE_STORAGE}`, `ZombieCensus__Enabled false`, `WorktreeResidue__Enabled true`, `WorktreeLegacyRelease__Enabled false`, `RunnerWorktreeRetention__Enabled true`, `Schedules__Enabled true`, `ChannelBridge__Enabled ${CHANNEL_BRIDGE_ENABLED:-false}`, `Digest__Enabled false`, `SessionRunner__Enabled true`, `SessionRunner__BaseUrl http://server-session-runner:8080`, `PhoneHomeRunner__Enabled true` plus `PhoneHomeRunner__Runners__server2__*` from env, `Delegation__ApiBaseUrl ${ANTIPHON_PUBLIC_ORIGIN}`, `AgentTui__KeyRingPath /state/keyring`, `AgentTui__KeyProtection__Mode X509Certificate`, `AgentTui__KeyProtection__CertificatePath /state/keyring-cert/cert.pem`, `...CertificatePrivateKeyPath /state/keyring-cert/key.pem`, `Operator__TokenPath /state/operator-token`, Linux specialist working directories); `scripts/deploy-server2-server.ps1` (`-WhatIf` default, `-Execute` host-only, env file 0600 outside the repo, refusals: `antiphon_pgdata`, `dind-data`, `17280`, `17202`, `17420`, `0.0.0.0`, a hard-coded `codeperf.net`, missing certificate files, `windmill`); one replacement paragraph in `docs/docker-stack.md` | New `tests/Antiphon.Tests/Infrastructure/Server2ProductionServerContractTests` (Unit): project name, network external, port 17400 loopback, sidecar present with Herdr off, every flag above, key-protection mode, refusals of the deploy script in `-WhatIf` without docker | Mirrors `Server2StagingServerContractTests`. No `-Execute` in CI. |
| S2 (CARD-1776) | `Dockerfile` (copy the runner's `antiphon-github-credential` helper and a `gitconfig` with `credential.helper`, pinned `github_known_hosts`), compose mounts `${RUNNER_GITHUB_TOKEN_DIR}:/run/antiphon/github-token:ro` and the push allow-list; `docs/agent-credentials.md` row for the server container; clone layout note `/work/repos/<name>` | New `tests/Antiphon.Tests/Infrastructure/ServerImagePushCredentialTests` (Unit): Dockerfile and compose text pins, allow-list present, no token env var, no `GH_TOKEN`; existing `DockerStackContractTests` still green | Token custody stays as CARD-0604 describes; same PAT directory, read-only |
| S3 (CARD-1777) | `scripts/deploy-server2.ps1` header and the `$api` resolution: `ANTIPHON_API` required, refuse with exit 2 and a message naming `docs/docker-stack.md` when unset (no `localhost:17202` default); operator token note; `docs/docker-stack.md` "What deploy-server2.ps1 deploys" paragraph | New `tests/Antiphon.Tests/Scripts/DeployServer2ApiDefaultTests` (Unit, `[ParallelLimiter<ProcessSpawnLimit>]`): unset refuses before any token read; set value is used; the `check-census` path unchanged. `LinuxServerRestartGateTests` re-run (gate config example) | Keeps every phase and stop gate; no `-Phase all` change |
| S4 (CARD-1778) | Runbook steps 1.3 and 3.5; if the sidecar refuses `/work/agents/<name>`: `server/appsettings` Linux `Delegation:AllowedRoots` via compose env; a `docs/docker-stack.md` paragraph on standing agents in topology B | Only if code changes: the existing allowed-roots tests for the touched class | CARD-1695 remains the phone-home always-on path; this slice does not touch `PhoneHomeLaunchPolicy` |
| S5 (CARD-1779) | `docs/docker-stack.md` new section "Production server on the server2 host (CARD-1744)"; `AGENTS.md` front door: `restart-linux-server.ps1` is the production restart, `restart-apphost.ps1` is rollback; `docs/logs.md` container log paths; `docs/apphost-runbook.md` maintenance-as-end-state note | `DockerStackDocumentationTests` if it pins section anchors | Land after the window, with the recorded numbers |

## Performance budget

No test exercises the cutover itself: phases 1-5 are operations on two hosts. Test cost is the
Unit contract rows below plus one spawn-lane class.

- Selection: four exact class filters (section Checkpoints), one isolated cold `Antiphon.Tests`
  build per committed group, native default output, `UseAppHost=false`. No namespace or assembly
  run; delivery, landing, leases and persistence are not touched by S1-S3.
- Measurements (provisional, from this runner's history, not a fresh p95): isolated cold
  `Antiphon.Tests` build 2-5 minutes under a build slot; `Server2StagingServerContractTests`
  (9 text tests) under 5 seconds; `LinuxServerRestartGateTests` (14 `pwsh` spawns on the strict
  lane, 300-second ScriptHarness deadline each) 1-2 minutes on a loaded server2; new text-pin
  classes under 10 seconds. Row timeouts stay at the importer's 15-minute floor, above 2x any of
  these; no timeout is widened.
- Phases reported separately: slot wait, build, test body. No container, database, host or
  migration starts in these rows; the Unit fixture's shared setup is not used by text-pin classes.
- Operations budget (not tests): window 2 downtime central 45-60 minutes including the 30-minute
  monitor (stop 1 min, start 2 min, vhost switch and TTL 5 min, runner reconnect up to 10 min,
  post-checks 10 min). Rollback central 15 minutes (section 3.7). Window 1's envelope is
  CARD-1401 D-16 and must be recomputed at 1.86 GB; that is P-1, not this budget.
- No selection, row, batch or run here is expected over 30 minutes; the first rehearsal that
  exceeds it gets a Debug task with its timing evidence.

## Verification design

Lane: Unit, Linux, any runner; no `-Runner`, no `-Platform`. Each row runs once per committed
slice group through the checkpoint tool; the Code dispatch for CARD-1775, 1776 and 1777 carries its row. Rows for
CARD-1775 and CARD-1776 are red-then-green: the contract class is committed before the compose or Dockerfile
change and must fail with assertion failures, not build errors.

### Checkpoints

totalTimeoutMinutes: 52

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | CARD-1775 red | tests/Antiphon.Tests -> bin-c1744-s1r/ | red-contract | /*/Antiphon.Tests.Infrastructure/Server2ProductionServerContractTests/* | S1 | >= 8 executed, assertion failures before the compose exists | 8 | 4 |
| CP-2 | CARD-1775 green | tests/Antiphon.Tests -> bin-c1744-s1g/ | compose-contracts | /*/Antiphon.Tests.Infrastructure/(Server2ProductionServerContractTests*)\|(Server2StagingServerContractTests*)\|(DockerStackContractTests*)/* | S1 | all listed, 0 failed | 40 | 6 |
| CP-3 | CARD-1777 | tests/Antiphon.Tests -> bin-c1744-s3/ | deploy-api-default | /*/Antiphon.Tests.Scripts/(DeployServer2ApiDefaultTests*)\|(LinuxServerRestartGateTests*)/* | S3 | all listed, 0 failed | 16 | 6 |
| CP-4 | CARD-1776 | tests/Antiphon.Tests -> bin-c1744-s2/ | push-credential | /*/Antiphon.Tests.Infrastructure/(ServerImagePushCredentialTests*)\|(DockerStackContractTests*)/* | S2 | all listed, 0 failed | 28 | 5 |

Suffix check: `Server2ProductionServerContractTests*` and `Server2StagingServerContractTests*` do
not prefix-collide; `DockerStackContractTests*` does not match `DockerStackDocumentationTests`.

### Sessions that wait for input

This plan adds no session that waits for input. The always-on agents re-homed in 3.5 keep today's
semantics: a Working or Blocked seat is released by its owner or by the operator, and with
`BlockedTaskParking:Enabled` false there is no automatic release deadline (CARD-1083).

## Not done, unverified

- The server2 host was not opened: no `docker ps`, no compose state, no ingress config. Port
  readings are from the runner container, which is not the host network namespace.
- The desktop `antiphon-server` and `aspire-antiphon-apphost` user-secrets were not read; key
  names for P-9 come from `PhoneHomeRunnerEntry` and the docs, not from the live store.
- The vhost chain (Cloudflare, Caddy on the desktop, traefik and cloudflared on server2) was not
  inspected. Step 3.1 is operator-only with a STOP.
- `Delegation:AllowedRoots` behaviour for `/work/agents/<name>` on the sidecar was not tested;
  S4 names the fallback.
- The `Antiphon` project has an empty `gitRepositoryUrl`; the Linux clone's remote comes from the
  desktop checkout's `origin`, recorded in P-13.
- Whether `Operator:TokenPath` inside the container resolves to a writable path for uid 1654 is
  pinned by S1's compose env, not verified here.

--- next stage ---
next: decide
handoff: Accept D-1..D-9 (two windows, keep the public origin, Hangfire storage conditional on 1768/1774, fresh X.509 key ring with one API key re-entered, session-runner sidecar for specialists and always-on agents, fail-closed desktop); then run CARD-1406 drill and the CARD-1407 window, and dispatch Code for CARD-1775 (production compose) and CARD-1776 (Linux land credential) in parallel.
artifact: docs/superpowers/plans/2026-11-08-card-1744-cutover-plan.md
