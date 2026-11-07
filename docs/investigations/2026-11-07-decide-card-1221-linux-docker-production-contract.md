# CARD-1221 decision: the supported full Linux/Docker production contract

Date: 2026-11-07. Investigate task `2aecd6c1`. Source read: `5d5973be2cd867bf592cd0363240e728f64d3b28` on `feat/card-task-2aecd6c1`. Documents only. No product code, image build, deploy, or board move of CARD-1221.

The 2026-10-13 readiness report is not in this checkout. It was read from commit `d7648f8e9532faa1b4ba8e094db8d7cce5969f8a` (`docs/investigations/2026-10-13-card-0038-linux-docker-readiness.md` on `origin/feat/card-task-2007037f`). Line numbers below are this checkout unless a citation names that commit. Live runner capabilities were not re-probed.

## Verdict

**Topology A is the supported production deployment. Topology B is the accepted target. Topology C is unsupported.** The root `docker-compose.yml` stack is a supported Linux application core with an explicit exclusion list. It is not production, and it does not prove the desktop control plane.

"Supported on Linux" means the current code can run that service on a Linux server without a Windows API. It does not mean the base compose file turns the service on.

## Three stacks

| Stack | What it is | Production? |
|---|---|---|
| Desktop control plane | Aspire AppHost plus `docker-compose.dev.yml` Postgres (`antiphon-postgres`, port 17280, volume `antiphon_pgdata`) and Redpanda (`:19092`). `docs/bootstrap.md` sections 4 and 6. | Yes. This is production today. |
| Persistent server2 runner | `docker-compose.server2-runner.yml`, project `antiphon-runner`. Phone-home only. Comment at lines 1-3 and `docs/docker-stack.md:1223`: no standing Antiphon server or Postgres. | Yes, as an execution runner. Not a control plane. |
| Self-contained application core | Root `docker-compose.yml`: `state-init`, `antiphon`, `session-runner`, `postgres`. Shipped under CARD-0590 (Done). | No. Test and nested throwaway stack. |

`deploy-server2.ps1:1` says "Run from the desktop after Review, never on server2." Its default API is `http://localhost:17202` (`deploy-server2.ps1:44`). Rolling phases call that API and SSH to the host. They assume the desktop process owns runner state.

## Services the base compose turns off

Root `docker-compose.yml:41-49` (and `:46`, which the 2026-10-13 list omitted).

| Service | Base compose | Desktop today | Disposition | Blocker | Smallest card |
|---|---|---|---|---|---|
| Check interpreter, diagnose, output distiller | `Delegation__CheckInterpreterEnabled`, `DiagnoseEnabled`, `OutputDistillerEnabled` false (`docker-compose.yml:41-43`) | On. Hosted services no-op unless the flag is true (`AgentTaskCheckHostedService.cs:84`, `DiagnoseHostedService.cs:36`, `OutputDistillationHostedService.cs:36`). Code defaults are true (`DelegationSettings.cs:813`, `:877`, `:959`). | Supported on Linux | Tracked working directories are `C:\logs\antiphon\check-interpreter` and `C:\logs\antiphon\diagnose` (`server/appsettings.json:362-363`). The Linux stack must override them, as the root compose does at `:39-40`. | CARD-1742 (staging compose). No port card. |
| Hangfire worker | `Hangfire__ServerEnabled` false (`:44`) | On (`server/appsettings.json:185-187`). Storage is always in-memory (`server/Program.cs:990-994`). Jobs register only when the worker is on (`:1169-1205`). | Supported on Linux as a process. Durable schedules are not supported on either host. | A container recreate drops recurring registrations. Census must stay unregistered or the WMI job throws. | CARD-1743. Do not enable the worker against the production database from a second server. |
| Zombie census | `ZombieCensus__Enabled` false (`:45`) | On (`server/appsettings.json:208-209`). `Program.cs:622` always registers `WindowsZombieProcessCensus`. `SnapshotAsync` throws off Windows (`WindowsZombieProcessCensus.cs:17-18`). The job is added only when both the worker and `census.Enabled` are true (`Program.cs:1172-1173`). | Not supported | WMI snapshot. Enabling the flag on Linux throws. | CARD-1222 (Backlog). Stays off in every Linux compose until that card lands. |
| Worktree residue | `WorktreeResidue__Enabled` false (`:46`) | On (`server/appsettings.json:218-219`). Sweep path compare is platform-aware (`WorktreeResidueSweepService.cs:755-758`). No WMI call. | Supported on Linux | Needs the Hangfire worker, and must sweep only worktrees that server can see. | CARD-1742 for a scratch server. Not a second worker on the production database. |
| Schedules | `Schedules__Enabled` false (`:47`) | On (`server/appsettings.json:413-414`). `ScheduleSweepHostedService.cs:35-38` returns when disabled. This sweep is not a Hangfire job. | Supported on Linux | None in code. Off in the application core by configuration. | CARD-1742. |
| Channel bridge | `ChannelBridge__Enabled` false (`:48`) | Forced on by AppHost (`Antiphon.AppHost/Program.cs:73`). Tracked `server/appsettings.json:391-392` is false. Hosted service starts only when enabled (`Program.cs:878-880`). | Supported on Linux once a broker is reachable. Not supported inside the four-service stack. | No broker in root compose. 2026-10-21 did not measure port 19092. Desktop `:17202` from server2 timed out that day (`docs/investigations/2026-10-21-card-1401-postgres-server2-and-snapshots.md:64`). | CARD-1746. |
| Digest | `Digest__Enabled` false (`:49`) | Tracked config off (`server/appsettings.json:403-404`). `AwayDigestHostedService.cs:17` returns when disabled. | Supported on Linux as a flag. Not part of the required production set. | None. Matches the desktop tracked default. | None. Stays off. |
| Messaging broker | Not in the four-service file | `docker-compose.dev.yml:48-75`, Redpanda, host `19092`. AppHost forwards `AntiphonMessaging:BootstrapServers` only when that value is set (`Antiphon.AppHost/Program.cs:63-85`). | Supported on Linux as a container. Placement is undecided. | Reachability between server2 and the desktop gateway hosts is unmeasured for 19092. | CARD-1746. |
| Messaging gateway | Not in any compose file | Fake gateway is a Windows exe (`Antiphon.AppHost/Program.cs:45-52`), dev/test only. Real gateways are separate processes (`docs/bootstrap.md:288-291`). | Gateway processes: supported only on the desktop (their current hosts). Not part of the Linux server image. | A Linux control plane still needs those processes to reach the broker. | CARD-1746 places the broker. It does not move gateway processes. |

Desktop legacy release is not a compose flag. It is on and executing (`server/appsettings.json:230-232`, `WorktreeLegacyReleaseSettings.cs:8-21`). Hangfire registers it whenever the worker is on and the flag is true (`Program.cs:1187-1190`). A Linux server with the worker on must set `WorktreeLegacyRelease__Enabled` false until cutover, or it becomes a second releaser. Runner worktree retention is also on, for runner `server2` (`server/appsettings.json:243-247`). Same single-worker rule.

## Section 2 features

From the 2026-10-13 report, checked against this checkout.

| Feature | Disposition | Blocker | Smallest card |
|---|---|---|---|
| Zombie / OS census | Not supported | Same WMI throw as the compose row. | CARD-1222 |
| Claude RC watch | Supported only on the desktop control plane | `Program.cs:655-656` registers `WindowsRcBridgeProbe`. `GetExtendedTcpTable` is the evidence source (`WindowsRcBridgeProbe.cs:57-62`). Any failure returns 0 (`:90-94`), so a Linux server reports no connections and does not prove the watch. A zero is not evidence. | CARD-1223 (Backlog) |
| Herdr disposal | Supported only on the desktop Windows runner | Locator calls `CreateFileW` (`HerdrDisposalLocatorStore.cs:72`). The inspector returns incomplete off Windows (`HerdrDisposalProcessInspector.cs:23-24`). Both Linux composes set `SessionRunner__Herdr__Enabled` false (`docker-compose.yml:89`, `docker-compose.server2-runner.yml:96`). Phone-home projection sets `Herdr = null` (`PhoneHomeLaunchPolicy.cs:271`). | CARD-0864 (disposal), CARD-0872 (peer uid). Herdr stays off on Linux runners. Topology B keeps the Windows runner for Herdr sessions. |
| Positive per-session memory caps | Not supported | `WindowsJobObject.AssignMemoryLimitedJob` throws off Windows (`WindowsJobObject.cs:34-35`). Phone-home projection sets `MemoryLimitMb = 0` (`PhoneHomeLaunchPolicy.cs:269`). The server2 runner comment says there is no cgroup memory cap (`docker-compose.server2-runner.yml:68`). Custody cgroups are descendant containment, not a memory limit (`LinuxCgroupCustodyProbe.cs:6-16`). Host kernel cited as 4.15 (`docs/docker-stack.md:1221`), and the custody probe advertises only cgroup v1 (`LinuxCgroupCustodyProbe.cs:28-31`). | None. Parity is explicitly not required. CARD-0038 allowed this alternative. A positive cap on a phone-home launch is discarded; a direct Linux launch that asks for one throws. Do not configure a positive cap and expect containment. |
| SourceLanding custody | Not supported on the live contract | The Linux backend exists (`VerificationCustody.cs:29`, probe at `LinuxCgroupCustodyProbe.cs:24-38`) and stays unadvertised unless delegated root, cgroup v1, the custody root, `NoNewPrivs=0`, and both sudo helpers succeed. On 2026-10-13 the server2 runner reported `verificationCustodyBackend=null` (readiness report). That observation was not repeated here. CARD-0604 is in Review. CARD-0634 (Backlog) still records the native containment and egress gap. | CARD-0634, after CARD-0604. Ordinary sessions do not need it. |
| Orchestrator deny hook | Not supported | The hook command is `powershell` (`DelegationWorktreeService.cs:505-517`). The comment at `:502-503` says Windows PowerShell 5.1 on purpose. The Linux runner image installs `pwsh`, not `powershell`. | CARD-1225 (Backlog). An orchestrator session on Linux that arms this hook does not get the deny behavior. |
| Delivery ceilings | Supported on Linux at the inbox numbers | `CeilingsFor` maps anything other than `ModernConPty`, including `UnixPty`, to inbox ceilings (`DelegationSettings.cs:292-299`): brief 900 bytes (`:160`), reply 3,000 chars (`:116`), single chunk 1,024 bytes (`:202`). Runner-bound input forces that inbox profile (`AgentTaskReplyService.cs:1096-1098`). | CARD-1224 (Backlog) measures and may raise them. No new number is set here. |
| Windows-only deployment pieces | Supported only on the desktop control plane | AppHost exe paths (`Antiphon.AppHost/Program.cs:34` and `:47`). Scheduled Tasks via `scripts/install-autostart.ps1` (`docs/bootstrap.md:251-259`). `deploy-server2.ps1` default API and "run from the desktop" (`:1`, `:44`). | CARD-1744 retargets the API when the control plane moves. CARD-1745 replaces the AppHost SHA gate. The Windows runner and Scheduled Tasks stay for the desktop rollback path and for Windows-platform work. |

CARD-1226 is a Windows CI assertion fix. It is not a deployment feature.

## Topology options

### A — today, and the supported production contract

server2 runs the phone-home runner only. The desktop runs AppHost (server, built client, Windows session runner, fake gateway, storybook), Postgres, and Redpanda. The public origin `https://antiphon.desktop.codeperf.net` fronts that desktop server (CARD-1401 investigation, 2026-10-21). Production Postgres has not moved: CARD-1401 is in Review, CARD-1406 is Backlog. CARD-1405, CARD-1407, and CARD-1408 are Done as scripts only (`scripts/postgres-snapshot.ps1`, `scripts/cutover-postgres-server2.ps1`).

### A2 — Postgres on server2, control plane stays

CARD-1401's decision, still not executed. Host-daemon Postgres (CARD-1406): Tailscale bind, not `0.0.0.0`, not port 5433, not Alpine, not nested dockerd, `pg_stat_statements` preload, new password. The desktop server keeps running and changes its connection string. AppHost, autostart, and `deploy-server2.ps1` stay. This is not topology B.

### B — accepted target

server2 host daemon runs the server, Postgres, and the phone-home runner. The desktop remains a browser client and the Windows session runner (Herdr, Windows-platform tasks, rollback AppHost). The image's embedded client is `Dockerfile:67-70` (`wwwroot/index.html`). Aspire is not the production supervisor after cutover.

B is not authorized by this card. The ordered cards below are the only path.

### C — both live, with failover

Unsupported. No card.

One database writer. Hangfire has one in-memory worker and no leader election. Phone-home has one `PhoneHome__ServerOrigin` (`docker-compose.server2-runner.yml:54`). Runner capacity, custody, and the data-protection key ring are single-server state. Two servers on one database would double-fire retention and legacy release. A hot spare that is not opening the database and not registering Hangfire is a future idea, not this contract.

## Migration

1. Stay on A. Keep dispatching to the server2 runner. Rolling runner deploys keep using the desktop API.
2. A2. CARD-1406, then the existing CARD-1407 window. Desktop server, new connection string, desktop volume kept.
3. Staging server. CARD-1742 against a scratch database, or against CARD-1406's service only after that card is Done. Phone-home origin unchanged. AppHost still production.
4. Durable Hangfire. CARD-1743, still one worker.
5. Restart gate. CARD-1745 against the staging container.
6. Broker. CARD-1746. Measure 19092 before choosing a side.
7. Cutover. CARD-1744. Stop the desktop server first. One writer. Move origin, vhost, deploy-script API, and the key ring. Leave AppHost installed.
8. Owner doc. CARD-1741 can land as soon as this decision is accepted. It copies these dispositions. It does not change them.

## Risks

- A second server on the production database. Forbidden at every step before CARD-1744, and forbidden after it too.
- Production Postgres or the production server created by the runner's nested dockerd. Forbidden. CARD-1406 and CARD-1742 are host-daemon projects.
- Enabling census on Linux. The job throws.
- A Linux Hangfire worker left with `WorktreeLegacyRelease:Enabled` true. It files releases.
- Phone-home silently dropping a positive memory cap. Unsupported means do not set one.
- RC watch returning 0 on Linux and being read as "no Claude connections."
- Deny hook naming `powershell` on a Linux orchestrator session.
- SourceLanding admitted when the runner advertises no backend. The probe is supposed to prevent that. CARD-0634 is still open, so SourceLanding on server2 is outside this contract.
- PID reuse after a container recreate (CARD-0829, CARD-0668). Both Backlog. A runner recreate during B is the same risk as today.
- Public vhost and DNS. CARD-1744 stops for the operator. It does not edit Cloudflare.
- Desktop `:17202` was unreachable from server2 on 2026-10-21, while `:443` and `:17280` were open. After B the desktop must reach the server2 origin. That path was not re-measured here.
- Root `docker-compose.yml` Postgres has no `pg_stat_statements` preload. CARD-1406's host service does. Do not promote the test Postgres command into production.

## Rollback

| Step | Rollback |
|---|---|
| A2 | CARD-1407 points the desktop connection string back at `localhost:17280`. `antiphon_pgdata` stays. |
| Staging server | Remove that compose project. Desktop production is untouched. |
| Cutover | Point phone-home origin and the public vhost back at the desktop, start AppHost, stop the Linux server. Do not delete `antiphon_pgdata`. Do not run both servers. |

Runner image rollback stays the existing rolling procedure (`docs/docker-stack.md` staged rollout). A control-plane move does not replace those phases. It changes which API they call (CARD-1744).

## Postgres data move

Owned by CARD-1401. Not redone here.

- Size on 2026-10-21: 1,265,417,239 bytes. CARD-1406 repeats that figure and the 30-minute window's 2 GiB / 10-minute dump-copy-restore envelope from the parent plan.
- Snapshot script: CARD-1405, `scripts/postgres-snapshot.ps1`. Encrypt before anything syncs. The password and the age key are not printed.
- Host service and scratch drill: CARD-1406. Still Backlog. Volume name `antiphon-server2-postgres_pgdata`. Not `antiphon_pgdata`, not `dind-data`, not Windmill.
- Cutover script: CARD-1407, Done, `scripts/cutover-postgres-server2.ps1`. Operator runs it. This task did not.
- Monitor: CARD-1408, Done.
- New cluster password. Not the committed `antiphon_dev` value in `docker-compose.dev.yml:31`.
- server2 host ports 5432 were refused on 2026-10-21 (Tailscale `100.93.77.126` and LAN `192.168.4.34`). Port 5433 is Windmill and stays Windmill.

A2 does not wait for B. B's cutover waits for A2.

## Secrets and custody

| Material | Where it lives today | Move rule |
|---|---|---|
| Postgres password | Desktop compose and appsettings for dev. Production override is a user secret. | New password in an untracked `0600` env file on the server2 host (CARD-1406). Never the image, never a log. |
| Phone-home secret, deploy key | File secrets interpolated by `docker-compose.server2-runner.yml:146-148`. Entrypoint stages the phone-home secret to `/run/antiphon/phone-home`. | Stay file mounts. The runner already has them. The server move does not copy them into the server container. |
| Claude OAuth token, Codex home, GitHub token dir, git identity | Host files, read-only mounts (`docker-compose.server2-runner.yml:133-145`). `docs/docker-stack.md:1223` and `docs/agent-credentials.md`. | Unchanged by the control-plane move. They belong to the runner. |
| Agent TUI data-protection key ring | Empty `AgentTui:KeyRingPath` resolves to `%LOCALAPPDATA%\Antiphon\DataProtection-Keys` on Windows and to the Unix data root on Linux (`AgentTuiSettings.cs:40-54`). Root compose sets `/state/keyring` (`docker-compose.yml:38`). | CARD-1744 copies or re-protects the ring before the Linux server serves. A new empty ring cannot read existing protected secrets. |
| AppHost user-secrets (`aspire-antiphon-apphost`) | Desktop only. Connection string and optional broker override. | Not inside the Linux image. The staging and production server compose take env or mounted files. |
| Operator token | Owner-only file read by `deploy-server2.ps1` (`:50-52`). | Still not printed. The script's API base changes in CARD-1744. The token file can stay on the desktop if the script keeps running there. |

## AppHost and Aspire

AppHost is the Windows production supervisor: Postgres by connection string only (`Antiphon.AppHost/Program.cs:19-25`), session runner exe (`:32-39`), fake gateway exe (`:45-52`), server project with the channel bridge forced on (`:66-82`), npm client and Storybook (`:92-103`). It is not a prerequisite for the root Docker server. The 2026-10-13 report's conclusion still holds.

Under B, Aspire does not supervise the production server. The server container's restart policy plus CARD-1745's SHA check replace `restart-apphost.ps1` for that process. AppHost remains installed so rollback can start the desktop server, the Windows runner, and the fake gateway. Scheduled Tasks (`docs/bootstrap.md:251-259`) stay the desktop autostart. They are not reimplemented as Linux cron in this contract.

The embedded `wwwroot` client is the B UI. The desktop Vite/serve client remains the rollback UI.

## Staged rollout script

`scripts/deploy-server2.ps1` and the named phases in `docs/docker-stack.md:6-12` keep deploying the **runner**. They do not deploy the server or Postgres. Through A and A2 they stay as they are: desktop checkout, API `localhost:17202`, SSH to server2, one accepting runner.

CARD-1744 changes the default API to the server that owns runner rows. It does not collapse the phases, does not add `-Phase all`, and does not move the script onto the host it is mutating. Census, drain, and retire keep their stop gates. A control-plane cutover is not a rolling runner phase. Do not drain the only accepting runner in order to move the server.

Host jq, volume recycle, and temp retirement (`docs/docker-stack.md` gates) are runner-host operations. They are unchanged by where Postgres sits.

## Recommendation

Accept A as production, A2 as the next database step already on the board, and B as the target. Reject C. Reject memory-cap parity.

Do not file another generic port card. Existing owners stay the owners:

- CARD-1406 then CARD-1407 for the database move. CARD-1401 stays the parent.
- CARD-1222 census, CARD-1223 RC watch, CARD-1224 ceilings, CARD-1225 deny hook.
- CARD-0864 and CARD-0872 Herdr, which remains desktop-only.
- CARD-0604 and CARD-0634 SourceLanding, which stays outside the supported contract until qualification.
- CARD-0829 and CARD-0668 recreate identity. Not topology work.

New Backlog cards, in order. Children are High or Normal so they do not outrank CARD-1221 (Critical/Now). Searches for the distinctive titles returned no earlier card.

| Order | Card | Importance / urgency | Slice |
|---|---|---|---|
| 1 | CARD-1741 | High / Soon | Transcribe this contract into `docs/docker-stack.md`. |
| 2 | CARD-1406 (existing) | Normal / Normal | Host Postgres and scratch restore. Blocks the cutover. |
| 3 | CARD-1742 | High / Soon, platform Linux | Staging server compose. Scratch database. No cutover. |
| 4 | CARD-1743 | High / Normal | Hangfire jobs survive a restart. Blocks the cutover. |
| 5 | CARD-1745 | Normal / Normal, platform Linux | SHA restart gate. Blocked by CARD-1742. |
| 6 | CARD-1746 | Normal / Normal | Measure and place the broker, then enable the bridge. Blocked by CARD-1742. |
| 7 | CARD-1744 | High / Normal | Cutover. Blocked by CARD-1742, CARD-1406, and CARD-1743. |

CARD-1744 is also the card that retires AppHost from the production path without uninstalling it.

## Remaining uncertainties

- No fresh image smoke and no live `/capabilities` read in this task. SourceLanding's null advertisement is the 2026-10-13 observation.
- Broker port 19092 was not measured in either direction. CARD-1746 measures it.
- Desktop reachability of a future server2 HTTPS origin was not measured. The 2026-10-21 reverse path (`:443` open, `:17202` timeout) is not a substitute.
- Whether the operator has already run the CARD-1407 window is not visible from this checkout. The board still has CARD-1401 in Review and CARD-1406 in Backlog, so this decision treats the database as still on the desktop.
- Digest and the real gateway processes may have machine-local overrides this repo does not track. Tracked config is the contract used here.

## Not done, noted

Possible later implementation only: a cgroup v1 `memory.limit_in_bytes` cap on kernel 4.15, if a future decision reverses the unsupported memory-cap disposition.

--- next stage ---
next: decide
handoff: Accept topology A as production and topology B as the target (C and memory-cap parity unsupported). Next executable slice is CARD-1406, then CARD-1742; cutover is CARD-1744 and stays blocked.
artifact: docs/investigations/2026-11-07-decide-card-1221-linux-docker-production-contract.md
