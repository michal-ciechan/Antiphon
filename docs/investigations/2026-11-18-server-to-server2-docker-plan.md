# Moving the Antiphon server into Docker on server2, and the desktop as a phone-home runner (2026-11-18)

Investigate task `b56e40f0`. Read-only, desktop worktree. Master read:
`997f9b8d9ffc4ba870d2fd56caf7c03781591624` (= `origin/master` at the time of reading).

What I ran: file reads in this worktree, `git log origin/master --grep`, and `scripts/card.ps1 search`/`get`
against the Antiphon board. No build, no test, no docker command, no SSH, no SQL, no restart, no
setting change, no secret read. The "Postgres moved to server2 today" fact
(`antiphon-server2-postgres-postgres-1`, 16.14) comes from the brief; I did not observe it.

## Verdict

**Most of this is already designed and built.** The server-to-server2 move is CARD-1744 ("topology
B"). It has a full cutover plan
([2026-11-08-card-1744-cutover-plan.md](../superpowers/plans/2026-11-08-card-1744-cutover-plan.md),
decisions D-1 to D-9, runbook phases 0-4, rollback R-1 to R-3), and four of its five code slices are on
master: the production compose, the deploy script, the SHA restart gate, and the docs. The window has
**never been run**. What remains is mostly:
- **Window 1 aftermath:** the database move ran today (per the brief), so P-1/P-2 are now
  close to true. They still need the 24 h quiet period (D-1) and the four defects listed in the
  2026-11-18 runbook note.
- **Board state:** three blockers are still in Review (CARD-1406, CARD-1776, CARD-1778), even though
  their code has landed.
- **Three gaps this investigation found** (G-1 to G-3 below). The most important: the production
  compose has **no phone-home entry for a desktop Windows runner**, so plan D-9 cannot work as it is
  written.
- **The operator-only window itself.**

**Should the desktop become a phone-home runner first? Yes. Run it as a second, parallel Windows
runner (`desktop-win`) against today's desktop server. Do not replace the direct runner yet.**
- It is the only way the desktop can serve sessions once the server is on server2. A server on
  server2 cannot use the desktop's `127.0.0.1:17204` direct runner. Dialling `100.x:17204` over the
  tailnet does not fix that either: the server reads the direct runner's session-log files from
  its own disk (plan D-5).
- It is independent of the server move in code, and it already works for a laptop
  (`scripts/start-windows-phone-home-runner.ps1`).
- Proving it now removes the biggest Windows-side unknown before the cutover window, at no risk to
  production: rollback is Ctrl+C plus removing one config entry.
- It is not a hard prerequisite, but it should precede the window. The direct runner must stay until
  cutover: standing agents, specialists and Herdr cannot run over phone-home while the server is
  on the desktop (`PhoneHomeLaunchPolicy.cs:159-199`).

## 0. What already exists (state at master `997f9b8d9`)

### Docs and plans

| Item | Where | State |
|---|---|---|
| Linux production contract (topology A/B, migration order, services table) | `docs/docker-stack.md:1380-1600`; decision `docs/investigations/2026-11-07-decide-card-1221-linux-docker-production-contract.md` | CARD-1221 Done, CARD-1741 Done |
| Cutover plan (two windows; keep the public origin `antiphon.desktop.codeperf.net` and repoint DNS; sidecar direct runner for specialists and always-on agents; fresh X.509 key ring; Linux land clones; desktop fails closed) | `docs/superpowers/plans/2026-11-08-card-1744-cutover-plan.md` | Plan written 2026-11-08. Window never run |
| Topology B operations section | `docs/docker-stack.md:2150-2203` ("Production server on the server2 host (CARD-1744)") | CARD-1779 Done. It says itself that it "does not record that the window has run" |
| Native Windows phone-home runner (laptop recipe) | `docs/docker-stack.md:2205-2240` | Documented. A Windows phone-home SourceLanding Mutation has not been exercised end to end (`:2236-2240`) |

### Compose files, scripts and code

| Item | Where | State |
|---|---|---|
| Production compose | `docker-compose.server2-server.yml`: project `antiphon-server2-server`; services `server-state-init`, `server` (`127.0.0.1:17400:8080`), `server-session-runner` (sidecar, Herdr off, inbox pty backend); external network `antiphon-server2` to `Host=postgres` | CARD-1775 Done. Never started (UNOBSERVED on the host) |
| Production deploy | `scripts/deploy-server2-server.ps1`: `-WhatIf` by default, `-Execute` host-only, SHA and HEAD refusals (CARD-1788) | On master |
| SHA restart gate | `scripts/restart-linux-server.ps1`: exits 3 when unconfigured; config gives `baseUrl`/`container`/`lockPath` | CARD-1745 Done. Topology A leaves it off |
| Staging compose (scratch Postgres `staging-postgres`, `127.0.0.1:17420`, SessionRunner and PhoneHome off) | `docker-compose.server2-staging-server.yml`, `scripts/deploy-server2-staging-server.ps1` | CARD-1742 Done. Whether it was ever started (P-3) is UNOBSERVED |
| `deploy-server2.ps1` requires `ANTIPHON_API` | `docs/docker-stack.md` "What deploy-server2.ps1 deploys" | CARD-1777 Done |
| Hangfire Postgres storage and lease | `docs/docker-stack.md` "One database writer" | CARD-1743 Done. CARD-1768 and CARD-1774 are on the board as Review; both have commits on master (latest 663faf96f, 2026-11-08) |
| Linux land credential and `/work/repos` mirrors | CARD-1776 | Review on the board, 13 commits on master |
| Re-home always-on agents and specialists to the sidecar | CARD-1778, plan `2026-11-08-card-1778-rehome-agents-to-sidecar.md` | Review on the board, 7 commits on master |
| Broker probe and bridge | CARD-1751 (InProgress, 0 commits), CARD-1752 (Review, 3 commits), CARD-1746 (Review, decision doc) | Bridge off at cutover unless CARD-1751 passes |
| Desktop maintenance marker | `scripts/set-apphost-maintenance.ps1` | Exists |

### Board search results

Board searches ran today with `card.ps1 search ... -Board Antiphon -All`.

| Phrase | Hits (status) |
|---|---|
| `CARD-1744` | 12 hits: CARD-1744 Review; CARD-1752 Review; CARD-1777 Done; CARD-1775 Done; CARD-1779 Done; CARD-1745 Done; CARD-1804 Done; CARD-1778 Review; CARD-1774 Review; CARD-1776 Review; CARD-1768 Review; CARD-1221 Done |
| `linux server` | 22 hits. The relevant ones not listed above: CARD-1743 Done; CARD-1746 Review; CARD-1222 Done; CARD-1223 Done; CARD-1704 Review (LibGit2Sharp); CARD-1023 Review. The rest are unrelated test-health cards |
| `phone-home` | 86 hits. The relevant ones not listed above: CARD-1751 InProgress; CARD-1742 Done; CARD-0604 Done; CARD-0628 InProgress (Claude CLI on server2); CARD-1964 Review (pump stall); CARD-1892 Review; CARD-1960 Review |
| `server on server2` | 1 hit: CARD-0185 Done (unrelated) |
| `restart-linux-server` | 1 hit: CARD-1779 Done |

### CARD-1744 blockers and a text conflict

CARD-1744's blockers, from `card.ps1 get`:

| Blocker | Status |
|---|---|
| CARD-1742 | Done |
| CARD-1743 | Done |
| CARD-1775 | Done |
| CARD-1777 | Done |
| CARD-1406 | Review |
| CARD-1776 | Review |
| CARD-1778 | Review |

CARD-1744 itself blocks CARD-1752.

The card text says: "Does not decommission the Windows session runner. Herdr and Windows-platform
tasks stay there". That **conflicts with plan D-9**, under which Herdr ends at cutover and Windows work
moves to a phone-home `desktop-win`. Decision O-3 below resolves it.

## 1. Inventory: what the server needs at runtime

Disposition key: **as is** = the production compose already handles it; **change** = needs code,
compose or script work; **decision** = the operator chooses.

### Database, keys and secrets

| Need | Desktop today | Server2 container | Disposition |
|---|---|---|---|
| Postgres connection string | AppHost user-secret `ConnectionStrings:DefaultConnection` (`aspire-antiphon-apphost`), which now points at server2 (brief) | `Host=postgres;Port=5432;...` on the external network `antiphon-server2`. The password comes from the 0600 env file | As is. After cutover, remove the desktop user-secret (D-6) |
| Agent TUI key ring | `%LOCALAPPDATA%\Antiphon\DataProtection-Keys`, protected by DPAPI | `/state/keyring` with an X.509 installation certificate | **Change, operator.** The DPAPI ring cannot be read on Linux. Generate the certificate (runbook 0.5) and re-enter the one `ApiKeys` row (2.5). Any TUI secret written after 2026-11-08 must be re-entered too: the plan counted 0 `AgentTuiSecrets` on 2026-11-08, and the count was not re-read today (UNOBSERVED) |
| Operator token | `%LOCALAPPDATA%\Antiphon\operator-token` | `OPERATOR_TOKEN_FILE`, copied to `/state/operator-token` | Change, operator: copy without display (runbook 2.3) |
| Phone-home secrets | `antiphon-server` user-secrets, one `PhoneHomeRunner:Runners:server2:*` entry | Env `PHONE_HOME_SHARED_SECRET` (server2 only) | As is for server2. **Change for `desktop-win`** (G-1) |
| GitHub push credential (land) | Desktop git credential, paths `C:/src/<project>` | CARD-1776 credential helper and `/work/repos/<name>`. `Project.LocalRepositoryPath` is rewritten at the window (2.6) | Change: code is on master, card in Review. The window step is operator-only |
| Claude, Codex and Grok homes for the sidecar | `~/.claude`, `~/.codex` on the desktop | `/state/claude`, `/state/codex`, `/state/grok` on `runner-state`. The operator runs `claude auth login` once on the sidecar | Change, operator (CARD-1778 runbook) |

### Files, services and platform-specific features

| Need | Desktop today | Server2 container | Disposition |
|---|---|---|---|
| Worktrees and repos | `C:\Antiphon\worktrees`, `C:\src\*` | `/work/worktrees`, `/work/repos` (`Git__*`) | As is for new work. Legacy release of desktop worktrees stops (accepted in the plan) |
| Session logs and transcripts | The direct runner and the server share a disk | The sidecar and the server share `runner-state` (`AgentSessions__SessionLogPath=/runner-state/session-runner`) | As is (this is why D-5 uses a sidecar) |
| Logs | `logs/`, `C:\logs\antiphon\*` | `/state/logs`, `/work/specialists/*` | As is. Update `docs/logs.md` (CARD-1779 did part of this) |
| Hangfire | InMemory, one worker | `HANGFIRE_STORAGE`, default InMemory | **Decision (D-3).** CARD-1768 and CARD-1774 commits are on master, so PostgreSql storage is now eligible if their Review settles |
| Zombie census, RC watch, memory caps | Windows-only | Off (`ZombieCensus__Enabled=false`) | Supported omission (CARD-1222, CARD-1223) |
| Redpanda / ChannelBridge | The server2 broker. The desktop AppHost forces the bridge on | `CHANNEL_BRIDGE_ENABLED` default false | **Decision.** Family replies pause until CARD-1751 and CARD-1752 run (runbook 3.3) |
| fake-gateway | AppHost Windows exe, dev/test only | Not carried | Stays on the desktop (rollback only) |
| Client bundle (17203) | Vite or serve, behind desktop Caddy | Embedded `wwwroot` served by Kestrel on 17400 | As is. The desktop client becomes the rollback UI |
| Public origin and vhost | Cloudflare to desktop Caddy to `:17203`, which proxies `/api` | Same name, repointed at server2 ingress, then `127.0.0.1:17400` | **Operator** (P-11, 3.1). WebSockets must pass through. Untested |
| Direct runner at `localhost:17204` | AppHost `session-runner` daemon (`Antiphon.AppHost/Program.cs:33-37`, `:69`); `server/appsettings.json:75` | `SessionRunner__BaseUrl=http://server-session-runner:8080` (Linux sidecar) | As is. **Runner id `local` becomes a Linux runner** (G-2) |
| Scheduled Tasks | `Antiphon AppHost`, `Antiphon AppHost Watchdog`, its State Observer, `Antiphon Session Runner` (`scripts/install-autostart.ps1:83-86`) | `restart: unless-stopped` plus the CARD-1745 gate | Change, operator. Disable the AppHost and watchdog tasks (D-6). The session-runner task needs a decision (G-3) |
| Scripts that default to `http://localhost:17202` | 118 files under scripts, docs, skills and AGENTS.md. Examples: `scripts/card.ps1:236`, `scripts/delegate.ps1:414` | Nothing listens on 17202 after cutover | **Change** (G-4). Set `ANTIPHON_API=https://antiphon.desktop.codeperf.net` machine-wide on the desktop at the window, then retire the defaults in a follow-up |
| Windows qualification supervisor | `scripts/qualification/*.ps1` | n/a | **Unaffected.** No `/api/`, `ANTIPHON_API`, 17202 or AppHost reference was found by grep. It needs only the desktop build-slot broker (G-3) |

## 2. The desktop as a phone-home runner

### How it would be reached

The desktop runner **dials out**: the phone-home shape that server2 and the laptop already use.

- It registers at `POST /api/session-runners/register` and holds a WebSocket at
  `/api/session-runners/<id>/connect` (`docs/docker-stack.md:2207-2211`).
- It opens no inbound port.
- The alternative, the server dialling `100.x:17204`, is rejected for three reasons:
  - The server reads the direct runner's session-log files from its own disk (plan D-5).
  - The desktop runner binds localhost only.
  - Desktop `:17202` timed out from server2 on 2026-10-21. Tailnet reachability of `:17204` from server2 was not measured (UNOBSERVED).

### What changes (from `start-windows-phone-home-runner.ps1` and docs `:2213-2234`)

- **Runner id:** a non-reserved name. `local` and `desktop` are refused
  (`start-windows-phone-home-runner.ps1:39`). The plan uses **`desktop-win`**.
- **Server-side entry:** `PhoneHomeRunner:Runners:desktop-win:*`, with the fields below.
  - **Before cutover:** add it to the desktop `antiphon-server` user-secrets, then restart AppHost.
  - **After cutover:** it must be in `docker-compose.server2-server.yml`. Today that file has only
    `server2` entries, and the compose "is the closed list" (`docs/docker-stack.md:2164`) → **G-1**.

  | Field | Value |
  |---|---|
  | `Enabled` | true |
  | `DisplayName` | as chosen |
  | `AllowDelegatedTasks` | true |
  | `HostWorkspaceRoot` | `C:\src\Antiphon` before cutover; after cutover, what the server uses for the workspace mirror (UNOBSERVED for the Linux server) |
  | `RunnerWorkspace` | `C:/Antiphon/runner/work` |
  | `RunnerRepository` | `C:/Antiphon/runner/work/repos/antiphon` |
  | `MaxCapacity` | 1-2 |
  | `ChildClaudeHome`, `ChildCodexHome`, `ChildGrokHome` | as printed by the start script |
  | `RawExeAllowList:0` | as printed by the start script |
  | `CallbackOrigin` | `https://antiphon.desktop.codeperf.net` |
  | `SharedSecret` | the generated secret |
- **Secret file:** `C:\Antiphon\runner\secrets\phone-home`.
- **Port:** the script defaults to `-Port 17214`, so it can run beside the direct runner on 17204.
  At cutover, decide whether it takes 17204 (G-3).
- **Workspace:** it has its own clone under `C:\Antiphon\runner\work`, separate from
  `C:\Antiphon\worktrees`. Workspace slots are not used on the desktop, as the brief asked.
- **Build slots:**
  - The desktop build-slot broker is the local session runner's `/build-slots` (`scripts/build-slot.ps1:8`).
  - I did not trace how the script resolves the runner URL (UNOBSERVED).
  - The Windows qualification and every desktop build lease through it.
  - Whatever runner process remains on the desktop must keep serving `/build-slots` where `build-slot.ps1` looks → G-3.
- **Host budget:** `desktop-win` becomes its own host id. Today's `local` cap applies to the direct runner.
  Desktop seats therefore become local cap + `desktop-win` cap. Keep `MaxCapacity` at 1-2 while a Windows Full run is live.

### What breaks, or does not carry over

1. **Always-on agents, specialists and pinned named agents are refused on phone-home.**
   `phone_home_pool_refused` (`PhoneHomeLaunchPolicy.cs:159-160`, `:185-186`, `:198-199`). That is why the
   direct runner stays until cutover and the sidecar takes those roles after it (D-5, CARD-1778).
2. **Herdr is refused** (409 `UnsupportedTarget`). Herdr panes end at cutover (D-9). This conflicts with
   the CARD-1744 card text → decision O-3.
3. **Windows-platform tasks.**
   - `ClassifyRequiredPlatformAsync` (`AgentTaskDispatcher.cs:6795-6826`) checks the platform of
     **the task's runner** (`task.RunnerId`, null = `local`). It blocks with `runner_platform_mismatch` on a known opposite platform.
   - It does not reroute a Windows task to a Windows runner. `DefaultRunnerRoutingPolicy.cs` has no platform term.
   - After cutover `local` is the Linux sidecar, so every Windows-pinned task that does not name
     `-Runner desktop-win` blocks → **G-2**.
4. **Windows phone-home SourceLanding Mutation** has not been exercised (`docs/docker-stack.md:2236-2240`).
5. **No autostart.**
   - The script runs in the foreground and must start from a normal terminal; an agent shell's Job Object would kill its pty hosts.
   - The existing `Antiphon Session Runner` Scheduled Task and `restart-session-runner.ps1` supervise the **direct** runner (the Debug exe path).
   - Nothing yet supervises a phone-home runner on Windows → G-3.
6. **The default runner token `desktop`** (`RunnerPlatformWire.DesktopId`, `DefaultRunnerRoutingPolicy.cs:116`, `:150`, `:195`) means "the direct runner".
   After cutover it means the Linux sidecar. Anyone who sets a runner default of `desktop` expecting Windows gets Linux → part of G-2.
7. **Host load mapping.** `AgentTaskPipelineStatusService.cs:287` maps `local` to the `desktop` host-load series.
   After cutover the sidecar's seats would be shown against desktop load → part of G-2.
8. **The hairpin (desktop to public origin to desktop) works today for the laptop recipe.**
   For the desktop itself, the route is Caddy, then Vite `/api`, then 17202 on the same host. Not observed for a same-host runner (UNOBSERVED). It is likely fine.

### Prerequisite or independent?

**Independent in code, but a de-risking prerequisite in sequence.** The server move does not need
`desktop-win` to start. Without it, though, every Windows-platform task and the
SourceLanding-on-desktop path stop at cutover. That includes Windows rc repairs and anything pinned
`-Platform Windows`. **Recommendation:** run `desktop-win` now, beside the direct runner, against the
desktop server. Route a few Windows-platform Debug tasks to it with `-Runner desktop-win`, and file
G-1/G-2/G-3 before the window.

## 3. Staged plan (slices)

| # | Slice | Goal | Files | Witness | Effort | Risk | Who |
|---|---|---|---|---|---|---|---|
| 0 | Settle the board | Move CARD-1406, CARD-1776, CARD-1778, CARD-1768 and CARD-1774 to their true state: code is on master, and each card's Review either settles or names its residual | board only | `card.ps1 get` per card | 30 min | none | orchestrator |
| 1 | `desktop-win` beside the direct runner (pre-cutover) | Prove the Windows phone-home lane on the desktop | user-secrets entry (operator), `C:\Antiphon\runner\secrets\phone-home`, `start-windows-phone-home-runner.ps1 -RunnerId desktop-win -Capacity 1` | `GET /api/session-runners/desktop-win/status`: available and `dispatchEligible`. Two Windows Debug tasks dispatched with `-Runner desktop-win` complete. One land from such a task succeeds | 1-2 h, plus one AppHost restart | Low. Same-host CPU during a Full run: schedule the restart after the run (memory: an AppHost restart during a live run caused load flakes) | operator (secret, restart), orchestrator (tasks) |
| 2 | G-1: `desktop-win` entry in the production compose | After cutover, the Linux server accepts the desktop runner | `docker-compose.server2-server.yml`, `Server2ProductionServerContractTests`, docs `:2164` | Contract test pins the `PhoneHomeRunner__Runners__desktop-win__*` keys with Windows drive-rooted paths, plus a new env key `PHONE_HOME_DESKTOP_WIN_SECRET`. `-WhatIf` refuses when that key is missing | 2-3 h | Low | Code (Linux) |
| 3 | G-2: Windows routing after `local` turns Linux | A Windows-pinned task with no runner goes to a Windows phone-home runner or holds with a clear reason, never silently to the Linux sidecar. Fix the `desktop` token meaning and the `local` to desktop host-load label | `DefaultRunnerRoutingPolicy.cs`, `AgentTaskDispatcher.cs:6795-6826`, `AgentTaskPipelineStatusService.cs:287`, `RunnerPlatformWire.cs` | Unit: a Windows task with `local`=Linux selects `desktop-win` when eligible, else holds with `runner_platform_mismatch` and the name of the eligible Windows runner | 0.5-1 day | Medium (routing) | Plan, then Code |
| 4 | G-3: supervise `desktop-win` and keep build slots | Autostart, restart and build-slot continuity for the phone-home desktop runner | `start-windows-phone-home-runner.ps1` (`-Port 17204` option or broker discovery), `install-autostart.ps1`, `restart-session-runner.ps1`, `build-slot.ps1` | Script tests: the task registers, restart adopts the phone-home process, and `build-slot.ps1` leases from it | 0.5-1 day | Medium (Windows qualification depends on build slots) | Plan, then Code (Windows) |
| 5 | Staging dress rehearsal | Start `antiphon-server2-staging` (scratch Postgres) on the current master or rc tip, and probe `/health` and `/api/version` (P-3). Optionally restore a scratch copy of today's database into `staging-postgres` and repeat runbook phase 2 against staging: key ring certificate, API key PUT, project-path PUT | `deploy-server2-staging-server.ps1 -Execute` (host) | `curl 127.0.0.1:17420/api/version` equals the built SHA. Migrations apply on a copy of the production schema | 1-2 h | None to production | operator (host `-Execute`) |
| 6 | Ingress route (P-11) | A test hostname on server2 reaches `127.0.0.1:17400` (or 17420 for the rehearsal) with WebSocket upgrade | traefik or cloudflared on server2 (operator) | `curl` plus a WebSocket upgrade to `/hubs/antiphon` and `/api/session-runners/<id>/connect` | 1 h | Low | operator |
| 7 | G-4: client defaults | Desktop scripts keep working when 17202 is dead | Set the machine-level `ANTIPHON_API` at the window. Follow-up: `card.ps1:236`, `delegate.ps1:414` and others resolve a configured origin (one shared helper) | `card.ps1 list` with AppHost stopped and `ANTIPHON_API` set | env: 5 min. Helper: 0.5 day | Low | operator, then Code |
| 8 | CARD-1744 window | Runbook phases 0-4 from the plan, plus: start `desktop-win` against the new origin (3.4), and set `ANTIPHON_API` (G-4) | plan runbook | Plan phase 4 post-checks, plus one Windows task on `desktop-win` | 45-60 min downtime | High (operator window) | operator |
| 9 | Docs and skills sweep after the window | Swap the front doors | `AGENTS.md` ("Production server on the server2 host"), `docs/apphost-runbook.md`, `docs/bootstrap.md`, `docs/ops-http.md`, `docs/logs.md`, `docs/session-runtime-invariants.md` (the direct runner is the sidecar), `docs/agent-kinds.md`, `.claude/skills/antiphon-status-update`, `.claude/skills/antiphon-rc-qualification`, `antiphon-orchestrator`, and memory notes that say AppHost = production | `DockerStackDocumentationTests` | 0.5 day | Low | Docs |

### Cutover shape

This is the same pattern as today's Postgres window.

- **Window script:** the plan's runbook is manual tables. For parity with `cutover-postgres-server2.ps1`, a `cutover-server-server2.ps1` would script phases 1-2 and R-1/R-2. That is a new card (optional, slice 8a).
  - Its STOP checks would be: lands drained, `pg_stat_activity` has no desktop client, health and SHA on 17400, and the runner list.
  - Its rollback would skip the content fingerprint. Today's runbook note found that the Postgres script's fingerprint-first rollback refuses exactly when the target is down; do not copy that.
- **Drill:** slice 5, plus one R-2 rehearsal on staging (start, stop, restart AppHost) to measure the rollback time. The plan's 15-minute figure is a central, not a measurement.
- **Rollback:** plan §3.7, R-1 to R-3, back to the desktop AppHost on the **same** server2 database. No data moves, so nothing is lost except the in-memory Hangfire queue and the Linux-protected API-key ciphertext, which must be re-entered.

### What changes for desktop roles

- **Windows qualification supervisor:** unchanged, because it has no API dependency. After the window, run it with `ANTIPHON_API` pointing at the public origin, since some of its helpers call the card and delegate scripts (UNOBSERVED which ones). It must keep a build-slot broker on the desktop (G-3).
- **`restart-apphost.ps1`:** becomes the rollback path only. `restart-linux-server.ps1` on the host (or `deploy-server2-server.ps1 -Execute` for a new image) becomes the production restart. This is already written down in `AGENTS.md` and `docs/docker-stack.md:2154`. Its "exit 0 requires `/api/version` = HEAD" check moves to the gate.
- **Desktop dashboard (17205):** goes away with AppHost. Its replacements are `docker logs`, `/state/logs` and `scripts/logs.ps1`; `docs/logs.md` needs the container paths (CARD-1779 did part of this, unverified for completeness).
- **`restart-session-runner.ps1`:** restarts the desktop phone-home runner (G-3), no longer the production direct runner.

## 4. Risks and unknowns

### Risks

- **Two writers.** A second server on the production database is forbidden by the contract
  (`docs/docker-stack.md:1388-1394`). This forbids running the production compose against the server2
  production database in "shadow mode":
  - migrations run at startup;
  - Hangfire InMemory has no lock;
  - schedules, retention and the dispatcher all write.
  The only safe shadow is staging with a scratch restore (slice 5).
- **Windows lane loss at cutover** if G-1, G-2 or G-3 are not done (§2).
- **Herdr loss** (decision O-3).
- **Land path:** CARD-1776 is still in Review. The post-check "one land from `/work/repos/antiphon`" is the only proof.
- **Ingress and WebSocket:** untested (P-11).
- **Database defects from today's runbook note:**
  - The daily snapshot still targets the desktop `antiphon-postgres`.
  - `restart-apphost.ps1` restarts the old desktop Postgres through `dev-aspire.ps1`.
  If either is still unfixed at window 2, the desktop could have a running stale Postgres.

### Unknowns (UNOBSERVED)

- The host state of server2: whether the `antiphon-server2-server` or staging projects exist or were ever started, whether certificate files exist, and ingress config.
- The live user-secrets entries and the current `AgentTuiSecrets` and `ApiKeys` counts.
- How `build-slot.ps1` resolves the broker URL.
- Whether the same-host hairpin works for a desktop phone-home runner.
- Whether `HostWorkspaceRoot` for `desktop-win` makes sense when the server is Linux, i.e. how the workspace mirror source is reached.
- Tailnet reachability of desktop ports from server2.
- Whether the brief's statement that window 1 completed is final: was the CARD-1408 monitor run with exit 0, and does the 24 h clock start today?

### Order to do things

1. Slice 0 (board).
2. Slice 1 (`desktop-win` beside the direct runner), after today's Windows Full run ends.
3. Slices 2, 3 and 4 in parallel as Plan or Code.
4. Slices 5 and 6 (operator rehearsal and ingress), after window 1's 24 h quiet period.
5. Slice 7: set the env.
6. Slice 8 (the window).
7. Slice 9 (the docs sweep).

### Minimum viable first slice

**Slice 1: run `desktop-win` beside the direct runner, against today's desktop server.**
- It is safe: an additive config entry and a foreground process; rollback is Ctrl+C plus removing the entry.
- It proves the one piece of topology B that does not exist anywhere yet: Windows sessions reaching a server that is not on the same machine's disk.
- The parallel safe proof of the server image is slice 5: staging on scratch Postgres.
- **Not safe:** any second server pointed at `antiphon-server2-postgres-postgres-1`.

## 5. Card texts (not filed)

**N-1. Phone-home runner `desktop-win` beside the direct runner on the desktop (CARD-1744 pre-step)**
Run `start-windows-phone-home-runner.ps1 -RunnerId desktop-win -Capacity 1` against the current desktop server.
The operator adds a `PhoneHomeRunner:Runners:desktop-win` user-secrets entry and the secret file, then restarts AppHost outside a live Windows Full run.
Witness: `/api/session-runners/desktop-win/status` is available, two Windows Debug tasks complete with `-Runner desktop-win`, and one lands.
Direct runner `local` on 17204 is unchanged; standing agents, specialists and Herdr stay on it.
Rollback: stop the script and remove the entry. Record the CPU and seat impact during the run.

**N-2. Production compose: add the `desktop-win` phone-home entry (G-1, CARD-1744)**
`docker-compose.server2-server.yml` declares only `PhoneHomeRunner__Runners__server2__*`, and the compose is the closed key list.
Add `desktop-win` with drive-rooted forward-slash paths, `MaxCapacity`, child homes, and the secret from a new required env key.
Extend `Server2ProductionServerContractTests` red-first. `deploy-server2-server.ps1 -WhatIf` must refuse when the key is missing.
Docs: `docker-stack.md` env key list and the D-9 note.
Blocks CARD-1744.

**N-3. Windows-platform routing when the direct runner is Linux (G-2, CARD-1744)**
`ClassifyRequiredPlatformAsync` (`AgentTaskDispatcher.cs:6795`) checks only the task's own runner, and default routing has no platform term.
After cutover `local` is the Linux sidecar, so Windows-pinned tasks with no runner block with `runner_platform_mismatch`.
Route them to an eligible Windows phone-home runner, or hold naming it. Fix the `desktop` default-token meaning (`RunnerPlatformWire.DesktopId`) and the `local` to `desktop` host-load label (`AgentTaskPipelineStatusService.cs:287`).
Plan first. Unit witnesses for select, hold and mismatch. Blocks CARD-1744.

**N-4. Supervise the desktop phone-home runner and keep desktop build slots (G-3, CARD-1744)**
After cutover the direct runner on 17204 is gone. The Windows qualification and every desktop build lease from that runner's `/build-slots`.
Give `desktop-win` autostart (`install-autostart.ps1`), restart and adoption (`restart-session-runner.ps1`), and a build-slot broker at the address `build-slot.ps1` uses (for example `-Port 17204`, or discovery).
Witness: a script test plus one leased `dotnet build` through the phone-home runner.
Plan first (Windows). Blocks CARD-1744.

**N-5. Desktop clients after 17202 goes away (G-4)**
Up to 118 files under scripts, docs and skills default to `http://localhost:17202` (for example `card.ps1:236`, `delegate.ps1:414`).
At the window, set the machine-level `ANTIPHON_API=https://antiphon.desktop.codeperf.net` on the desktop and server2 hosts.
Follow-up: one shared origin resolver, with the default from a desktop config file, never a hard-coded port.
Witness: `card.ps1 list` with AppHost stopped. Not a blocker if the env is set in the window.

**N-6 (optional). `cutover-server-server2.ps1` window script (CARD-1744 phases 1-2, R-1/R-2)**
Script the plan's manual phases with STOP checks: lands drained, no desktop client in `pg_stat_activity`, health and SHA on 17400, sidecar listed.
`-WhatIf` by default; `-Execute` is operator-only.
The rollback does not depend on reading the target database: avoid the CARD-1407 fingerprint-first refusal noted in `2026-11-18-postgres-move-runbook.md`.
Script tests in the `test-cutover-postgres-server2.ps1` style.

## Operator decisions

- **O-1:** Hangfire storage at cutover: PostgreSql (CARD-1768 and CARD-1774 commits are on master), or InMemory. Default: PostgreSql if both Reviews settle clean; otherwise InMemory.
- **O-2:** ChannelBridge at cutover. Default: off until CARD-1751 passes; family replies pause.
- **O-3:** Herdr ends at cutover (plan D-9) versus the CARD-1744 card text ("Herdr and Windows-platform tasks stay there"). Default: D-9. Herdr is impossible across hosts, so update the card text.
- **O-4:** `desktop-win` port: 17214 beside the direct runner now; take over 17204 at cutover for build-slot continuity. Default: yes.

## Not done, noted

- No host, SSH, docker or database reads were made, by the brief's limits. Everything about server2 host state is UNOBSERVED.
- Fix ideas appear only as slice goals; nothing was designed past card level.
