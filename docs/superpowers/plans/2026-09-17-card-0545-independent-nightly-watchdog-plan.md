# CARD-0545: independent nightly watchdog and recipient readback

Plan task `c9140e5d`, 2026-09-17, inspected checkout `f091e84d`. Card
`b1c1ed0b-2608-40e7-99f5-f6587e9ed416` (Backlog). Explicit activation prerequisite
for CARD-0544 (TD-F2) and the deferred CARD-0487 S4 qualification.

This plan authorizes no deployment, notification, schedule change, agent launch,
credential creation or spend. Every observation below is read-only (Tailscale HTTP,
SSH `mc@server2` inspection, local Docker listing, checked-in files). No Windmill
token was minted, no message was sent, no schedule was touched.

## Disposition in five lines

1. **An independent host exists and is usable: server2.** It is separate hardware
   from the Windows desktop, runs Windmill only as one Docker Compose project among
   many, and offers systemd on its host OS. The watchdog is a systemd-supervised
   host process there, outside Docker and outside Windmill's scheduler, workers,
   database and control plane (D-1).
2. **No Telegram Bot API path can read the recipient's view.** The Bot API has no
   history read and a bot never sees other bots' messages. Real recipient readback
   needs an operator-provisioned Telegram user session or a different destination
   technology (D-6, OQ-3). Until the operator provisions it, Deploy/qualification
   stays pending and CARD-0544 activation stays disabled. TestDesign and Code can
   proceed offline against a callable reader contract.
3. **The checked-in "independent" health monitor is not independent and cannot even
   run where it is tagged**: from a server2 worker it SSHes to `host.docker.internal`,
   which the server2 Windmill compose does not define; every evaluation it would do
   runs on Windows, and its notification enqueue is itself a Windmill job. It is
   retired by this plan (D-9), never registered per the 2026-09-16 census.
4. **Two latent defects block qualification regardless of the watchdog**: Windmill's
   `jobs/list` rows carry no `result`, so the production job adapter maps every real
   completed job to `unknown`; and the nightly wrapper emits no structured result
   line and `last-run.json` has no `localDueDate`. Both are repaired here (D-10).
5. **The morning triage owner, the authorized destination, the watchdog host
   authorization and the reader credential are the human's decisions** (OQ-1..OQ-6).
   None is invented here; defaults are stated only where a default is safe.

## Ground truth

| Card/plan assumption or question | Observed (2026-09-17, read-only) | Consequence |
|---|---|---|
| A host outside both the Windows execution host and Windmill's failure domain is available. | `server2` (Tailscale `100.93.77.126`, direct LAN `192.168.4.34`): Ubuntu 18.04.1, kernel 4.15, systemd 237, Docker 24.0.2, uptime 177 days, passwordless sudo for `mc`, `ufw` inactive, iptables INPUT ACCEPT, 71 GB free. Windmill is one compose project (`/home/mc/docker/windmill`: `windmill_server`, `windmill_worker` group `default`, `windmill_worker_native`, `windmill_db` published on `100.93.77.126:5433`). No user timers, no crontab, `Linger=no`. | A systemd **system** unit on the server2 host is outside Windmill's scheduler/worker/control-plane and outside Docker. It shares the server2 host (power, network, disk) with Windmill and with the Telegram gateway; that residual is named, not hidden (D-1, OQ-5). |
| server2 might be coupled to the same failure domain as the desktop. | The desktop's only Windmill presence is the `windmill-desktop-worker` container (Docker Desktop, tag `desktop`, exclusive) connected to server2's DB over Tailscale. Nothing on server2 depends on the desktop. | Windows-host loss is visible from server2 as: desktop worker stops pinging, `desktop`-tagged jobs stay queued, SSH-bridge jobs fail with exit 255. |
| A third always-on host could host a dead-man's switch. | Tailscale peers: desktop (online), server2 (online), laptop `mc-dell-xps2023` offline 2 days, NAS offline 461 days, iPhone. | No third always-on host. A watchdog-liveness alert that survives server2 loss needs an external service or the user's laptop when present (OQ-5). |
| Windmill's health task detects Windmill loss independently. | `scripts/windmill/antiphon-nightly-health.json` is tag `default` (server2 worker) but its content SSHes to `lndco@host.docker.internal`; the server2 Windmill compose has no `extra_hosts`/`host-gateway`, so that name does not resolve there. All evaluation is `C:\src\Antiphon\scripts\nightly-health.ps1` on Windows. Its production sink enqueues `POST /api/w/mc/jobs/run/p/u/lndcobra/telegram_notify` (a Windmill job). Schedule `on_failure: null`. | Not independent and not runnable as tagged. Retire the definition; the Windows-side script survives only as the local readiness evaluator (D-9). G-116's routing guard is preserved literally. |
| The nightly definitions are registered. | Census 2026-09-16 08:41Z (`docs/investigations/2026-09-16-card-0544-evidence/windmill-census.txt`): no `antiphon_nightly_*` script or schedule; zero retained nightly jobs; live schedules are four unrelated `u/lndcobra/*` jobs. Windmill reports `CE v1.700.2-3-gd0f23cc523` at `GET /api/version` (unauthenticated, reachable from the desktop over Tailscale HTTP in 13 ms). | Registration and readback are Deploy-stage work under S6 of this plan (acceptance 3). |
| Windmill `jobs/list` returns the job result the adapter reads. | OpenAPI (`/api/openapi.yaml`, served unauthenticated) `CompletedJob` list rows carry `id, created_at, started_at, completed_at, duration_ms, success, script_path, script_hash, schedule_path, job_kind, ...` and **no `result`, no `scheduled_for`**. `ConvertFrom-NightlyWindmillJob` reads `$Job.result` and maps its absence to `unknown`. | Every real completed job is `unknown`; readiness could never turn green. Repair: fetch `GET /api/w/mc/jobs_u/completed/get_result/{id}` for candidate scheduled rows and derive the due day from `result.localDueDate` (D-10). `C544_ProductionJobAdapter` fixture must model the two-call shape. |
| The wrapper returns a structured completion record. | `antiphon-nightly-tests.json` content only runs the SSH command; `nightly-run.ps1` prints timestamped `Write-Host` lines and exits; no JSON last line; `grep localDueDate scripts/lib/*.ps1` finds it only in the health evaluator. `docs/testing-and-build.md` already records "production jobs map to unknown until the S4 qualification task adds it". | The wrapper's last stdout line must be the JSON record and native state must carry `localDueDate` (D-10). |
| The Telegram gateway can prove receipt. | `am-service` (`/home/mc/antiphon-messaging`, containers `am-service`, `am-redpanda`, `am-postgres`, `am-console`) relays `channels.outbound` to Bot API `sendMessage`; its log line `[outbound] sent via telegram -> <message_id>` is the Telegram server's acceptance, i.e. sender-side. Bot API has no `getHistory`; `getUpdates` never carries a bot's own or another bot's messages. | Recipient readback requires an MTProto user session reading the destination's history (D-6) or a non-Telegram destination with native history read (OQ-3 alternatives). |
| Existing Windmill notify plumbing can be the watchdog transport. | `u/lndcobra/telegram_notify` (python, Windmill variable `u/lndcobra/telegram_bot_token`) exists per ClaudeBot memory (2026-06, not re-verified here: no token). | It lives in Windmill's failure domain; the watchdog sends through the Bot API directly with its own credential reference (D-5). |
| The readiness reader already accepts independent evidence. | `InterimVerificationReadinessReader` requires `recipientEvidenceIds` and `outageRecoveryEvidenceIds` (non-empty) in `interim-qualification-receipt.json` plus matching `Identity` in `last-monitor.json`; it has no notion of which watchdog produced them and no freshness check on the watchdog. `MonitorFreshMinutes=60` applies to `last-monitor.json` only. | Add `watchdogInstanceId` to receipt and monitor identity and make the local evaluator fold watchdog heartbeat freshness into `Healthy` (D-9). |
| A .NET watchdog can run natively on server2. | No `dotnet`, no `pwsh`, Python 3.6.9 on the host. glibc 2.27, OpenSSL 1.1.1, `libicuuc.so.60`, `/usr/share/zoneinfo/Europe/London` present. .NET 9 supported-OS notes list a glibc 2.23 floor and do **not** list Ubuntu 18.04. | Self-contained single-file `linux-x64` publish is expected to run; it is unsupported by Microsoft on 18.04, so the first Deploy step is a smoke run with a stated fallback (D-2). |
| A Telegram user-session client exists for .NET. | NuGet `WTelegramClient` 4.4.8 (2026-08-18), netstandard2.0/.NET 5+, user login (`LoginUserIfNeeded`) and full client API including `Messages_GetHistory`. | Reader implementation choice for D-6. |
| Windmill tokens can be least-privilege. | OpenAPI `NewToken` has `label`, `expiration`, `scopes[]`, `workspace_id`. The superadmin password is only an argon2 hash; tokens are created by API with an existing token or by DB insert. | Operator creates a labelled, scoped watchdog token (OQ-6); the plan never mints one. |
| The PS notification ledger and its nine CARD-0487 harness cases (G-118..G-126) are the production notification path. | They are a JSON ledger plus seam-injected sink/recipient view; production sink is the Windmill job above. | Production notification moves to the watchdog. The PS ledger functions and G-118..G-126 stay as harness-level guards of the local evaluator; the production Windmill sink is removed (D-9). CARD-0487 IDs untouched. |
| Port for a tailnet snapshot endpoint. | `ss -ltn` shows nothing on 17290 on server2; no firewall filtering. | `100.93.77.126:17290` is the default snapshot bind (D-9); Deploy re-verifies. |

## Decisions

### D-1: watchdog host and supervisor are server2's host systemd, not Windmill, not Docker

`antiphon-nightly-watchdog.service` (system unit, `User=mc`, `Restart=always`,
`RestartSec=30`, `WorkingDirectory=/home/mc/antiphon-watchdog`,
`EnvironmentFile=/home/mc/antiphon-watchdog/env`) runs a long-lived process with an
internal 10-minute tick. Long-lived rather than a `.timer` so the snapshot endpoint
(D-9) and reader session stay resident and systemd supervises crashes.

**Why:** the card requires a timer/process, durable ledger and alert transport that
remain usable when Windmill or the Windows SSH hop is unavailable. systemd on the host
does not depend on `docker.service` or any Windmill container; the unit declares
`After=network-online.target` only.

**Rejected:** (a) a Windmill job on a server2 worker: inside the failure domain the
card forbids; (b) the desktop worker or a Windows Scheduled Task: inside the Windows
host domain and AGENTS.md forbids the task; (c) a Docker container on server2: shares
the Docker daemon with Windmill and am-service; kept only as the fallback runtime if
the native .NET smoke run fails (D-2); (d) a GitHub Actions cron: Windmill is
Tailscale-only, so it would need new network exposure or a tailnet ephemeral node,
which is a spend and exposure decision the card does not authorize; (e) the laptop:
offline for two days at inspection, not always-on.

**Residual:** server2 power/network loss takes the watchdog and the Telegram gateway
down together and nobody is told. That is OQ-5's dead-man's switch; this plan does
not pretend server2-wide loss is detected.

### D-2: .NET 9 self-contained single-file binary; smoke-gated on Ubuntu 18.04

New project `src/Antiphon.NightlyWatchdog/` (net9.0, matching the 25 existing
net9.0 projects), published with
`dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true`.
Tests live in `tests/Antiphon.Tests` and reference the project directly, so the 13
transferred controls and the new ones run in-process on local inherited children
with fakes, never on Windmill or an external executor.

**Why:** one language for probe, ledger, transport, reader and tests; method-scoped
PCs on C# methods; `WTelegramClient` gives the MTProto reader without a second runtime.

**Rejected:** Python on the host (3.6.9: current Telethon needs 3.7+; stdlib-only
cannot do MTProto; PCs would cross a process boundary); PowerShell on server2 (not
installed; no MTProto client; the PS ledger's only production sink is the Windmill
job being retired); Node (no repo precedent for server code).

**Gate:** Deploy step 1 is `./Antiphon.NightlyWatchdog --self-check` on server2
(prints runtime, tz conversion for two DST dates, opens a temp SQLite ledger, binds
the snapshot port). Fallback if it fails on 18.04: run the same binary in
`mcr.microsoft.com/dotnet/runtime-deps:9.0` as a **separate** compose project
(`/home/mc/antiphon-watchdog/docker-compose.yml`, `restart: always`), which is still
outside Windmill's compose/scheduler but inside the Docker daemon; the qualification
artifact must state which shape was deployed.

### D-3: probe set, outage kinds and London due-day identity

Each tick (10 min) the watchdog probes, in order, and records one `probe` row each:

| Probe | Call | Outage kind opened (after condition) |
|---|---|---|
| Reachability | `GET /api/version` (unauthenticated) | `windmill-unreachable` after 2 consecutive failed ticks (connect error, timeout 15 s, 5xx) |
| Authentication | `GET /api/users/whoami` | `windmill-auth-failed` after 2 consecutive 401/403 |
| Registration | `GET /api/w/mc/scripts/get/p/u/lndcobra/antiphon_nightly_tests` | `script-missing`; `script-hash-drift` when `hash` differs from the configured expected hash |
| Schedule | `GET /api/w/mc/schedules/get/u/lndcobra/antiphon_nightly_tests` | `schedule-missing`; `schedule-disabled` (`enabled=false`) |
| Desktop worker | `GET /api/workers/list?ping_since=900` | `desktop-worker-missing` after 60 continuous minutes without a `worker_group=desktop` ping |
| Jobs | `GET /api/w/mc/jobs/list?script_path_exact=...&per_page=20` then `GET /api/w/mc/jobs_u/completed/get_result/{id}` for scheduled completed rows of the two due days in scope | see below |

Due day `D` is the Europe/London calendar date of the tick; `D-1` stays in scope until
08:00 London of `D`. Job-derived kinds for a due day:

- `start-overdue`: at/after 01:00 London (00:30 + 30 min grace) no scheduled job for
  the due day has started.
- `run-stalled`: the due day's job has been running longer than `RunBudgetHours`
  (default 6; a planning value, not permission to extend test deadlines).
- `windows-hop-failed`: completed `success=false` whose logs match the SSH classifier
  (`exit 255`, `Connection refused|timed out`, `Permission denied`, `no such identity`).
- `job-failed`: any other completed `success=false`.
- `result-missing`: completed `success=true` whose result lacks `nativeRunId` or
  `localDueDate`, or whose `localDueDate` is not the due day.
- `report-undelivered`: result has `reportDelivered=false` (the nightly board card was
  not filed, so nobody else will see the red).
- `deadline-missed`: at/after 08:00 London no completed success job with a matching
  result for the due day, and no other outage for that due day is already open
  (otherwise the existing outage's evidence is amended without a new notification).

`tests-red` and `coverage-incomplete` are **not** outages: the board card owns them.
They are recorded in the probe row and snapshot only.

**Closure (recovery):** `windmill-unreachable`, `windmill-auth-failed`, `script-*`,
`schedule-*` and `desktop-worker-missing` close after 2 consecutive clean ticks. Due-day
kinds close when a later scheduled run (the same due day if Windmill re-fires it, or
any later due day) completes `success=true` with a matching result. A due day's
obligation is never satisfied retroactively and no native run is ever invented
(card: "outage-before-run uses a stable outage/due-day identity").

### D-4: SQLite ledger on the server2 host with stable identities

`/home/mc/antiphon-watchdog/state/ledger.db` (WAL), tables `heartbeat`, `probes`,
`outages`, `notifications`, `attempts`, `receipts`. Identities:

- `outageId = nw:{namespace}:{dueDay}:{kind}:{epoch}`; `namespace` is `mc` in
  production and `mc/qual` for qualification instances; `epoch` starts at 1 and
  increments only when the same kind re-opens after a closure on the same due day.
  Non-day kinds use the London date of opening as `dueDay`.
- `nid` (notification id) is a ULID generated once and inserted, together with
  attempt 1 in state `pending`, in the same transaction as the outage row or the
  closure update, **before any network call**. Recovery notifications get their own
  `nid` with `linkedNid` = the failure `nid` and the same `outageId`.
- `bodySha256` of the exact produced text is stored with the notification.
- `receipts` has a unique index on `(nid, messageId)` so an import replays idempotently.

**Why:** the crash cuts (DL-5) need transactions across intent, attempt and receipt;
JSON files make the after-observation/before-persist cut racy. The ledger is off the
Windows host and outside Windmill's database.

**Rejected:** JSON ledger (weaker atomicity); Windmill's Postgres (failure domain);
`am-postgres` (Docker and a shared production database).

### D-5: transport is the Telegram Bot API called directly from the watchdog

`TelegramBotTransport.SendAsync(nid, attempt)` posts `sendMessage` with
`disable_web_page_preview=true`, plain text (no parse mode), to the configured
`ANTIPHON_WATCHDOG_DESTINATION_CHAT_ID`. Unset, non-numeric or differing from the
qualified destination recorded in the ledger's `heartbeat.destination` refuses with
`destination-unauthorized` and sends nothing (PC-86). The Telegram response
`message_id` is recorded on the attempt as **transport acceptance only**; state never
becomes `received` from it (PC-81).

Body contract (exact lines; the marker line is machine-parsed):

```
Antiphon nightly watchdog: FAILURE windows-hop-failed
due 2026-09-18 Europe/London; workspace mc; schedule u/lndcobra/antiphon_nightly_tests
job 01a0...-...: ssh exit 255 at 2026-09-18T00:31:07Z
outage nw:mc:2026-09-18:windows-hop-failed:1
notification 01J8...; attempt 1; sha none; run none; policy 3f9c...
#antiphon-nightly nid=01J8... oid=nw:mc:2026-09-18:windows-hop-failed:1 kind=failure attempt=1 due=2026-09-18 h=<first 16 hex of bodySha256 over the lines above>
```

Recovery bodies start `Antiphon nightly watchdog: RECOVERED <kind>` and carry
`kind=recovery link=<failure nid> failureReceived=<true|false>` in the marker.

**Rejected:** `u/lndcobra/telegram_notify` (Windmill domain); `channels.outbound` via
am-service (Docker plus Redpanda plus gateway hops; an outage alert must not wait on
them); e-mail (a different operational channel from the one the operator uses).

### D-6: recipient readback is an MTProto user session reading the destination history

`TelegramUserReader` (WTelegramClient, session file
`/home/mc/antiphon-watchdog/state/reader.session`, `api_id`/`api_hash` from the env
file) resolves the authorized peer and calls `Messages_GetHistory` (limit 50, then
paged back to the oldest pending attempt floor minus 24 h at most). It returns
`RecipientObservation { peerId, messageId, date, text }` rows. `ReceiptImporter`
imports a receipt only when **all** hold:

1. `peerId` equals the authorized destination (PC-95);
2. the marker's `nid` names a ledger notification and its `attempt` names an existing
   attempt of that notification (PC-82);
3. the marker's `oid`/`due`/`kind` equal the ledger row, and for run-bound kinds the
   `run=` field equals the ledger `nativeRunId` (PC-83);
4. `sha256(text)` equals `bodySha256` — the whole produced body, not the marker alone
   (PC-96);
5. `date >= attempt.startedAt - 120 s` for the attempt named in the marker (PC-97).

Only then `notifications.state = received`, `receivedAt`, and a `receipts` row.
Local JSON exports of receipts (snapshot, qualification artifact) are caches of this
import, never its source.

**Why:** the card requires the authorized recipient's own message/transcript view.
For Telegram that view exists only through a user account's history.

**Rejected:** Bot API `getUpdates` (cannot see own or other bots' messages); the
`sendMessage` response or am-service log (sender-side); an Antiphon session
transcript as the outage destination (on the Windows host that may be the thing
that is down; allowed later as an additional destination kind for non-outage
notices, out of scope here); Slack/e-mail readers (no configured workspace or Graph
app is known; would change the operator's channel).

**Whose account:** the reader session identity is OQ-3. Option A: the operator's own
account (exactly "the recipient's own view"; the session string is as powerful as the
account). Option B: a dedicated low-value Telegram account that is a member of a
dedicated group destination together with the operator (group history is identical
for every member, so it is still the destination's recipient-side view; needs a phone
number). The code is the same for both; only the authorized peer differs.

### D-7: reader-first retry; no second logical notification

On every tick, for each notification not `received`:

1. If the reader is available, read back first. A matching observation for **any**
   attempt of that `nid` imports the receipt and ends the retry.
2. If the last attempt has no transport acceptance (send threw, or the response was
   lost) and the reader showed nothing, send again as `attempt+1` with the same `nid`
   (the marker's `attempt` changes, the `nid` and `oid` do not).
3. If the last attempt was transport-accepted but is still unreceived after
   `ReceiptGraceMinutes` (default 30) and the reader is available and shows nothing,
   resend as `attempt+1` (Telegram accepted but the recipient view lacks it: a
   deleted message or a wrong destination; the mismatch is logged).
4. If the reader is **held** (session locked, MTProto unavailable, or the
   qualification hold control), never resend; keep the attempt pending until the hold
   clears or `ReaderHoldExpiryMinutes` (default 30) elapses, after which rule 2/3 apply.

"Busy" in the CARD-0544 vocabulary is the held reader; "eligible" is a reader that
answers in the same tick. Backoff between attempts is 2, 5, 10, 30 minutes, then
hourly, with the attempt count in the snapshot.

### D-8: recovery is a separate, correlated notification

On closure the same transaction that sets `outages.closedAt` inserts the recovery
notification (own `nid`, `linkedNid`, `outageId`) and its attempt 1. It is sent even
if the failure notification was never received; its body states
`failureReceived=<bool>` so the operator learns about the failed delivery. The
failure notification keeps its own receipt lifecycle (PC-85).

### D-9: snapshot endpoint, Windows-side readiness evaluator and Windmill definitions

The watchdog serves `GET http://100.93.77.126:17290/snapshot.json` (bind the Tailscale
IP only, read-only, no auth needed on the tailnet, 64 KB cap):

```json
{ "schemaVersion": 1, "instanceId": "wd-…", "version": "…", "configHash": "…",
  "namespace": "mc", "heartbeatAt": "…Z", "tickSeconds": 600,
  "windmill": { "reachable": true, "lastOkAt": "…Z", "version": "CE v1.700.2-3-…" },
  "desktopWorker": { "seen": true, "lastPingAt": "…Z" },
  "schedule": { "present": true, "enabled": true, "scriptHash": "…" },
  "openOutages": [ { "outageId": "…", "kind": "…", "dueDay": "…", "openedAt": "…Z", "failureNid": "…", "failureState": "sent" } ],
  "recentNotifications": [ { "nid": "…", "kind": "failure", "outageId": "…", "state": "received", "attempts": 1, "receivedAt": "…Z" } ],
  "lastDueDay": { "dueDay": "…", "jobId": "…", "status": "success", "nativeRunId": "…", "sha": "…" } }
```

`scripts/nightly-health.ps1` (the Windows-side **local evaluator**, not the monitor)
gains `Get-NightlyWatchdogSnapshot` (bounded `Invoke-WebRequest`, 10 s) and
`Test-NightlyWatchdogFreshness` (heartbeat age 0..20 min, `instanceId` non-empty,
`namespace` = `mc`). It adds reasons `watchdog-unreachable`, `watchdog-stale`,
`watchdog-malformed`, `watchdog-outage-open` to `Health.Reasons` (each makes
`Healthy=false`) and records `Identity.WatchdogInstanceId`, `WatchdogHeartbeatAt`
in `last-monitor.json`. The server's `InterimVerificationReadinessReader` requires a
new receipt field `watchdogInstanceId` (schemaVersion stays 1: no production receipt
exists yet) and `Identity.WatchdogInstanceId` equality (`monitor_watchdog_mismatch`),
so a replaced or unqualified watchdog cannot inherit readiness. A stopped watchdog
therefore fails readiness closed within 20 minutes; the card's "missing heartbeat"
guard is G-545-3 below.

Windmill definitions after this card (all under `scripts/windmill/`, registration
remains an operator step):

| Definition | Change |
|---|---|
| `antiphon-nightly-tests.json` + schedule | Keep path/tag/cron. Content forwards the SSH exit code and relies on the ps1's final JSON line as the job result (D-10). |
| `antiphon-nightly-health.json` + schedule | **Deleted.** Never registered; not runnable from a server2 worker; superseded by the watchdog. README records why. |
| `antiphon-nightly-readiness.json` + schedule (new) | Tag `desktop`, `0 */30 * * * *`, Europe/London. SSH bridge runs `nightly-health.ps1 -RepositoryPath C:\src\Antiphon -ProjectId <guid>`; expected hashes and the watchdog URL come from `C:\Antiphon\nightly\readiness-config.json` written at qualification; `WINDMILL_TOKEN_FILE` points at an operator-placed token file. This job being on the desktop is correct: if Windows is down the Antiphon server that consumes readiness is down too, and if Windmill is down readiness goes stale and fails closed. |

`Test-NightlyMonitorRouting` (G-116) still throws for a desktop-tagged **monitor**;
the readiness job is not passed through it, so CARD-0487's guard keeps its meaning.
The production Windmill notification sink (`EnqueueNotification`, `NotifyPath`) is
removed from `New-NightlyProductionWindmillApi`; the PS ledger functions and the
seam-injected sink/recipient view remain so G-118..G-126 keep exercising the local
evaluator's ledger logic unchanged.

**Rejected for snapshot delivery:** SSH push from server2 into the Windows host (needs
an administrator-authorized key on Windows held on server2: larger blast radius than a
read-only tailnet listener); a Redpanda topic (Docker plus a Kafka client under
Windows PowerShell 5.1); Tailscale Serve (couples to tailscaled version/config); a
static-file container behind traefik (Docker again); making the readiness reader
itself fetch over the network (D-7 of CARD-0544 forbids network in admission).

### D-10: wrapper result line and job-adapter repair are prerequisites owned here

- `nightly-run.ps1`/`nightly-run-impl.ps1`: add `localDueDate` (London date of the run
  start) to `last-run.json` and `last-complete-green.json`; print, as the **last**
  stdout line, one compact JSON object
  `{"nativeRunId","sha","ref","trigger","localDueDate","policyHash","coverageComplete","testsPassed","reportDelivered","exitCode","summaryPath"}`
  on every exit path including refusals (`exitCode` non-zero, other fields empty).
  Windmill takes a bash script's last stdout line as its result; a failed SSH hop
  produces no line and a failed job, which is the intended `windows-hop-failed`
  signal.
- `scripts/lib/nightly-health.ps1` `New-NightlyProductionWindmillApi.GetJobs`: after
  `jobs/list`, call `jobs_u/completed/get_result/{id}` for at most the five newest
  completed rows with a `schedule_path`, attach the result, and set `scheduledFor`
  for completed rows from `result.localDueDate` (00:30 London of that date) since
  the list row has no `scheduled_for`. `ConvertFrom-NightlyWindmillJob` is unchanged.
- `C544_ProductionJobAdapter` (CARD-0544's method, in the file this card owns) keeps
  its assertion names and gains rows for the second call; the stub in
  `Start-C544WindmillStub` must answer two requests.

### D-11: qualification runs against isolated targets with fault injection that only a qualification config can enable

A second watchdog **instance** (`--config /home/mc/antiphon-watchdog/qual.json`,
namespace `mc/qual`, own state dir, own snapshot port 17291) points at qualification
targets. `AllowFaultInjection=true` is honoured only when `namespace != "mc"`; the
production config cannot inject faults. Injection matrix:

| Case | Isolated target | Expected chain | What it proves |
|---|---|---|---|
| F-1 Windows hop | Qual script `u/lndcobra/antiphon_nightly_tests_qual_hop` (tag `desktop`) whose SSH uses `-i /tmp/windmill/no_such_key` → exit 255; one-off qual schedule a few minutes ahead | job failed → `windows-hop-failed` outage → failure message → readback → repoint qual schedule at `..._qual_ok` (echoes a valid result line) → success → recovery message → readback | Hop failure detected and delivered without touching the production schedule or Windows sshd |
| F-2 Desktop worker/queue | Qual script tagged `desktop-qual` (no worker owns the tag) | queued past grace → `start-overdue` → message → readback → retag the **script** to `desktop` → runs → recovery | Queue outage detected without editing the shared `worker__desktop` config |
| F-3 Windmill unavailable | `WindmillBaseUrl=http://127.0.0.1:17292` served by `Antiphon.NightlyWatchdog --stub-windmill` on server2 (modes `ok`, `refuse`, `503`, `timeout`) | ok → refuse (2 ticks) → `windmill-unreachable` → message → readback → ok (2 ticks) → recovery → readback | Detection and delivery need no Windmill; the real instance is untouched |
| F-4 Held reader | F-1 with the qual hold control file present for one tick | no resend while held; receipt imported on the next tick; exactly one message with attempt 1 | Busy consumer semantics (D-7) |
| F-5 Crash cuts | `CrashAfter=<intent|send-before-response|transport-accepted|reader-observation>` in the qual config; `systemctl restart` the qual unit | ledger resumes the same `nid`; one logical notification; receipt imported once | DL-5 persistence cuts on the real ledger, transport and reader |
| F-6 Missing heartbeat | Stop the **qual** instance; point a copy of `readiness-config.json` at port 17291 in a private state root | evaluator reports `watchdog-stale`/`watchdog-unreachable`; readiness reader returns unready | G-545-3 live |

Production live proof, once, after F-1..F-6: `Antiphon.NightlyWatchdog
--send-qualification-notice` on the production instance produces `kind=qualification`
through the same ledger/transport/reader; the operator acknowledges it in the chat and
the artifact records the `nid`, `message_id`, receipt row and acknowledgement time.
Machine readback proves receipt; the acknowledgement is recorded, not inferred as
comprehension.

Nothing disables the production schedule, the desktop worker, traefik, Windmill or
am-service. Nothing sends to a chat the operator has not authorized.

### D-12: no invented owner, destination or credential

The morning triage owner, the authorized destination, the watchdog host
authorization and every credential are recorded by the human in the qualification
artifact and the env file. This plan lists them as open questions with the safe
default "unset means refuse". TestDesign and Code proceed offline on fakes; S6 waits.

## Open questions for the human

| ID | Question | Default in this plan | Blocks |
|---|---|---|---|
| OQ-1 | Authorize server2's host OS (systemd system unit under `mc`, `/home/mc/antiphon-watchdog`) as the independent watchdog host? Native binary first, Docker fallback per D-2. | Yes (server2 is the only always-on host outside both domains). | S6 deploy |
| OQ-2 | Authorized destination: the operator's DM with `@antiphon_assistant_bot` (the chat id the telegram skill lists for that DM), a dedicated group, or a dedicated bot (`antiphon_test_bot` exists in the Bitwarden item)? | Unset → the watchdog refuses to send. No id is written into config by a delegate. | S6 deploy and any live message |
| OQ-3 | Reader identity: (A) the operator's own Telegram account session on server2, or (B) a dedicated reader account in a dedicated group destination, or (C) a different destination technology with native history read (Slack `conversations.history`, e-mail via Graph)? A requires an interactive login by the operator on server2 (phone code, 2FA); B needs a phone number. | Design is peer-agnostic; nothing provisioned. | S6 qualification; without it the card stays pending and CARD-0544 activation stays disabled |
| OQ-4 | Who is the named morning triage owner (the person who acknowledges the qualification notice and owns persistent-failure follow-up)? | None. Not inventable. | S6 acceptance criterion 4 |
| OQ-5 | Dead-man's switch for the watchdog itself outside server2: an external ping service (healthchecks.io-style, free tier), the laptop when online, or accept the residual (server2-wide loss is silent until the Windows readiness evaluator notices and only fails closed)? | Accept the residual and record it; readiness still fails closed within 20 minutes. | Nothing; a follow-up card if chosen |
| OQ-6 | Windmill token for the watchdog: a labelled, expiring, scoped token created by the operator (read scopes if this CE version enforces them; otherwise an ordinary user token) placed in the env file; and a token file on the desktop for the readiness job. | None minted by delegates. | S6 deploy |

## Component design

### Watchdog service (`src/Antiphon.NightlyWatchdog/`)

| File | Responsibility |
|---|---|
| `Program.cs` | Commands: `run` (service loop), `--self-check`, `--send-qualification-notice`, `--stub-windmill`, `--export-evidence <dir>` (writes receipts/outages/notifications JSON for the artifact). |
| `WatchdogOptions.cs` | Typed config from env/JSON: namespace, Windmill base URL, token, schedule path, expected script hash, destination chat id, Telegram bot token, reader api id/hash/session path, ports, tick, grace/deadline/budget minutes, `AllowFaultInjection`, `CrashAfter`, hold control path. `Validate()` refuses production namespace with fault injection. |
| `LondonClock.cs` | `DueDay(now)`, `DueUtc(day)`, `GraceEndUtc(day)`, `MorningDeadlineUtc(day)` via `TimeZoneInfo.FindSystemTimeZoneById("Europe/London")`; tested with the same dates as `Test-C544_LondonDates` (2026-03-29/30, 2026-10-25, 01:00 BST overdue). |
| `IWindmillApi.cs`, `WindmillHttpApi.cs` | Version, whoami, script get, schedule get, workers list, jobs list, completed result; 15 s timeouts; classifies transport failures. |
| `OutageEvaluator.cs` | Pure function `(probes, jobs, now, ledgerView) -> OutageTransitions` implementing D-3. |
| `Ledger.cs` | SQLite (Microsoft.Data.Sqlite) with the D-4 schema and transactional `OpenOutageWithIntent`, `CloseOutageWithRecovery`, `RecordAttempt`, `ImportReceipt`, `Heartbeat`. |
| `NotificationBody.cs` | Renders the D-5 body and marker; parses markers from recipient text. |
| `INotificationTransport.cs`, `TelegramBotTransport.cs` | `sendMessage`; destination authorization check; returns `TransportResult { Accepted, MessageId, Error }`. |
| `IRecipientReader.cs`, `TelegramUserReader.cs` | WTelegramClient history read; `Held` state; peer resolution. |
| `ReceiptImporter.cs` | D-6 rules 1-5; idempotent import. |
| `RetryPolicy.cs` | D-7 reader-first retry and backoff. |
| `SnapshotServer.cs` | `HttpListener` on the configured bind; serves `/snapshot.json` from the ledger; 64 KB cap. |
| `WatchdogLoop.cs` | Tick orchestration; heartbeat; fault-injection hooks (`CrashAfter`) that call `Environment.FailFast` only when allowed. |
| `WindmillStub.cs` | The F-3 stub server (modes `ok/refuse/503/timeout`), qualification only. |

Packages: `Microsoft.Data.Sqlite` (latest 9.x), `WTelegramClient` (4.4.8),
`Microsoft.Extensions.Logging.Console`. No Windmill SDK, no Kafka.

### Deployment assets (`scripts/nightly-watchdog/`)

`antiphon-nightly-watchdog.service` (D-1 unit; `ProtectSystem=strict`,
`ReadWritePaths=/home/mc/antiphon-watchdog/state`), `env.example` (names only),
`qual.example.json`, `README.md` (install, self-check, token/session placement steps
as operator instructions, never values). `scripts/deploy-nightly-watchdog.ps1`
mirrors `deploy-am-service.ps1`: default is a read-only preflight (publish, checksum,
remote `--self-check` dry run); `-Deploy` is the explicit opt-in that uploads the
binary and unit via `ssh mc@server2`, runs `sudo systemctl daemon-reload && enable
--now`, and verifies `GET /snapshot.json`. ASCII-only, PS 5.1-safe.

### Credential references (names and locations only)

| Reference | Where | Custodian | Used by |
|---|---|---|---|
| `ANTIPHON_WATCHDOG_WINDMILL_TOKEN` | `/home/mc/antiphon-watchdog/env` (0600, owner `mc`) | operator (OQ-6) | probes |
| `ANTIPHON_WATCHDOG_TELEGRAM_BOT_TOKEN` | same env file; source is the Bitwarden item "Telegram Bot Tokens (Antiphon / School Revision)" | operator | transport |
| `ANTIPHON_WATCHDOG_DESTINATION_CHAT_ID` | same env file | operator (OQ-2) | transport authorization |
| `ANTIPHON_WATCHDOG_TG_API_ID` / `_API_HASH` and `state/reader.session` | env file and state dir | operator (OQ-3) | reader |
| `WINDMILL_TOKEN_FILE` → `C:\Antiphon\nightly\secrets\windmill-token` | desktop user env for `lndco` | operator | readiness job |
| `C:\Antiphon\nightly\readiness-config.json` | desktop state root | written at qualification; contains hashes, watchdog URL and instance id, no secrets | readiness job |

`docs/agent-credentials.md` gains a short "Nightly watchdog" custody row set; no
value is ever logged, printed or committed.

## Implementation slices

Each slice commits and pushes on its own; every slice's tests run on local inherited
children only.

### S1: wrapper result line, native `localDueDate`, job-result fetch (PowerShell)

Files: `scripts/lib/nightly-run-impl.ps1`, `scripts/nightly-run.ps1`,
`scripts/lib/nightly-health.ps1` (`GetJobs`, `ConvertFrom-NightlyWindmillJob` unchanged,
production sink removal), `scripts/windmill/antiphon-nightly-tests.json`,
`scripts/windmill/README.md`, `docs/testing-and-build.md` (Nightly).

Tests: `scripts/test-nightly-run.ps1` new `Test-C545_ResultLine` (last stdout line is
the JSON on success, refusal and test-red paths; `localDueDate` present in both state
files); `scripts/test-nightly-health.ps1` `Test-C544_ProductionJobAdapter` amended
(two-request stub, `get_result` shape, due day from `localDueDate`) plus new
`Test-C545_JobResultFetch` (list row without result stays `unknown` until the result
call; result call bounded to five rows; result-call failure is `unknown`, never
`success`); C# wrappers in `NightlyVerificationContractTests` (`C545_ResultLine`,
`C545_JobResultFetch`). Existing `NightlyScriptsTests` full class as regression.

### S2: watchdog core: clock, evaluator, ledger, identities (C#)

Files: `src/Antiphon.NightlyWatchdog/{Antiphon.NightlyWatchdog.csproj, WatchdogOptions.cs,
LondonClock.cs, OutageEvaluator.cs, Ledger.cs, NotificationBody.cs}`, `Antiphon.sln`,
`tests/Antiphon.Tests/Antiphon.Tests.csproj` (project reference).

Tests: new `tests/Antiphon.Tests/Scripts/NightlyWatchdogCoreTests.cs`:
`C545_LondonDueDays`, `C545_OutageKinds` (one row per kind in D-3 with the exact
probe/job shapes), `C545_DeadlineMissedSuppressedByOpenOutage`,
`C545_ClosureNeverInventsRun`, `C545_OutageIdentityEpoch`,
`C545_IntentBeforeNetwork` (ledger transaction contains outage + nid + attempt
before any transport call), `C545_ReceiptIdempotent`, `C545_BodyHashRoundTrip`.

### S3: notification producer, transport, reader, importer, retry, recovery (C#)

Files: `src/Antiphon.NightlyWatchdog/{INotificationTransport.cs, TelegramBotTransport.cs,
IRecipientReader.cs, TelegramUserReader.cs, ReceiptImporter.cs, RetryPolicy.cs,
WatchdogLoop.cs, Program.cs}`; test fakes `tests/Antiphon.Tests/TestHelpers/C545World.cs`
(`FakeWindmillApi`, `FakeTransport` with lost-response and reject modes, `FakeRecipientReader`
with held/eligible modes, `FakeTimeProvider`, temp ledger, crash-at-boundary hooks).

Tests: the 13 transferred methods implemented in-process in
`NightlyVerificationContractTests` (ledger below) plus new
`C545_ReaderFirstRetry`, `C545_HeldReaderNoResend`, `C545_RecoveryLinksFailure`,
`C545_QualificationNoticePath`, `C545_ProductionRefusesFaultInjection`.
`TelegramBotTransport` and `TelegramUserReader` get thin contract tests against local
HTTP/fake clients for request shape only; live Telegram is S6.

### S4: snapshot server, Windows-side evaluator integration, readiness reader guard

Files: `src/Antiphon.NightlyWatchdog/SnapshotServer.cs`, `scripts/lib/nightly-health.ps1`
(`Get-NightlyWatchdogSnapshot`, `Test-NightlyWatchdogFreshness`, reasons, identity),
`scripts/nightly-health.ps1` (`-WatchdogSnapshotUrl`, `-ReadinessConfigPath`),
`server/Infrastructure/Files/InterimVerificationReadinessReader.cs`,
`server/Application/Interfaces/IInterimVerificationReadinessReader.cs` (snapshot record
gains `WatchdogInstanceId`), `tests/Antiphon.Tests/TestHelpers/C544World.cs` (receipt
fixture gains `watchdogInstanceId`), `scripts/windmill/antiphon-nightly-readiness*.json`,
`scripts/windmill/antiphon-nightly-health*.json` deleted.

Tests: `NightlyWatchdogCoreTests.C545_SnapshotShape`; `scripts/test-nightly-health.ps1`
`Test-C545_WatchdogFresh`, `Test-C545_WatchdogStale` (age 20:00 is fresh, 20:01 stale,
future invalid, unreachable, malformed, wrong namespace, open outage), `Test-C545_ReadinessRouting`
(readiness definition is `desktop`; monitor routing guard still throws for a
desktop-tagged monitor; the deleted health definition is absent); C# wrappers
`C545_WatchdogFresh`, `C545_WatchdogStale`, `C545_ReadinessRouting`;
`InterimVerificationReadinessTests.C545_WatchdogInstance` (missing receipt field,
missing monitor field, mismatch, match). Full `InterimVerificationReadinessTests` and
`NightlyVerificationContractTests` classes as regression.

### S5: deployment assets, docs, custody

Files: `scripts/nightly-watchdog/{antiphon-nightly-watchdog.service, env.example,
qual.example.json, README.md}`, `scripts/deploy-nightly-watchdog.ps1`,
`scripts/test-deploy-nightly-watchdog.ps1` (preflight-only: manifest, ASCII, refuses
without `-Deploy`, never prints env), new owner doc `docs/nightly-watchdog.md`,
`AGENTS.md` table row ("Nightly backstop, watchdog and qualification"),
`docs/testing-and-build.md` Nightly section rewrite, `docs/agent-credentials.md`
custody rows, `tests/test-execution-policy.json` script entry for the new test script.

### S6: explicitly authorized Deploy and qualification (blocked on OQ-1..OQ-4, OQ-6)

Operator-run, in this order, each step recorded in
`docs/investigations/<date>-card-0487-nightly-qualification.md`:

1. Self-check on server2 (D-2 gate); record runtime shape.
2. Register/read back `antiphon_nightly_tests` script and schedule (POST
   `scripts/create`, `schedules/create`; GET both back; compare content SHA-256 to the
   checked-in payload; record `hash`, `tag`, cron, timezone, args `{}`,
   worker/server versions from `/api/version` and `workers/list`).
3. Register/read back `antiphon_nightly_readiness`; place the desktop token file and
   `readiness-config.json`; observe one green readiness tick.
4. Deploy the production watchdog with destination **unset**; observe heartbeat and
   snapshot; confirm the evaluator folds it in.
5. Manual full unattended run: `POST /api/w/mc/jobs/run/p/u/lndcobra/antiphon_nightly_tests`
   (trigger manual); wait; read the job result and `C:\Antiphon\nightly\last-run.json`
   and the run's `summary.json`; record all seven suite inventories (antiphon,
   session-runner, pty-host, agents-pty, messaging, client, scripts: discovered,
   executed, passed, failed, skipped), explicit manual exclusions from
   `tests/test-execution-policy.json`, SHA/ref, `policyHash`, script hash, job id,
   `nativeRunId`, wall times. Zero failed required tests or the run is not green.
6. Real 00:30 scheduled run the following night: same evidence from the scheduled
   job (`schedule_path` set), `last-complete-green.json` advanced, readiness monitor
   green with matching identities.
7. Qualification instance: F-1..F-6 (D-11) with the operator-authorized destination;
   export ledger evidence; record `nid`s, `message_id`s, receipt rows, timings.
8. Set the production destination; `--send-qualification-notice`; operator
   acknowledges; record.
9. Publish `C:\Antiphon\nightly\interim-qualification-receipt.json` (below) only
   after 1-8; commit the artifact; link the SourceLanding companion for the code
   slices and its disposition; then CARD-0544 may commission S6.

## Carried-forward controls (exact IDs and methods; none renumbered, none passed)

All 13 remain **pending**. The guard and the method name are CARD-0544's; only the
production entrypoint and the mutation target are restated because the producer moved
from PowerShell into the watchdog. PC-71 stays on CARD-0544.

| ID | Guard (unchanged) | Method (unchanged, in `NightlyVerificationContractTests`) | CARD-0545 production entrypoint | Restated mutation | Expected red |
|---|---|---|---|---|---|
| G/PC-78 | Windows/SSH outage detected outside that failure domain | `C544_IndependentOutage` | `WatchdogLoop.TickAsync` + `OutageEvaluator` with `FakeWindmillApi` job `success=false`, logs `exit 255`, Windows facts unreadable | `OutageEvaluator`: classify a failed SSH-bridge job as `job-failed` only when native `last-run.json` is readable | `windows-hop-failed` outage and failure `nid` exist in the ledger and the fake recipient sees the body with no Windows state access |
| G/PC-79 | Failure intent persists before enqueue | `C544_NotificationIntent` | `Ledger.OpenOutageWithIntent` | `WatchdogLoop`: call transport before the intent transaction commits | crash after intent, before send: restart resumes the same `nid`; receipt matches it |
| G/PC-80 | Enqueue failure retryable with original identity | `C544_NotificationRetry` | `RetryPolicy` + `Ledger.RecordAttempt` | mark the attempt transport-accepted when `SendAsync` throws | first send throws; attempt 2 carries the same `nid`; recipient receives it |
| G/PC-81 | Transport/job acceptance is not receipt | `C544_RecipientEvidence` | `ReceiptImporter` / `Ledger.ImportReceipt` | set `state=received` when `TransportResult.Accepted` | accepted send with empty recipient view leaves `state=sent`, snapshot `received=false` |
| G/PC-82 | Receipt matches notification identity | `C544_ReceiptNotificationIdentity` | `ReceiptImporter` rule 2 | drop the `nid` equality | observation with another `nid` and the same run does not import |
| G/PC-83 | Receipt matches run identity | `C544_ReceiptRunIdentity` | `ReceiptImporter` rule 3 | drop the `run`/`oid` equality | same `nid`, wrong `run=` field does not import |
| G/PC-84 | Crash after enqueue/observation recovers without duplicate | `C544_NotificationCrash` | `WatchdogLoop` restart path + unique `(nid, messageId)` | allocate a new `nid` for an outage that already has one on restart | crash after acceptance and crash after observation each recover the original `nid`; exactly one logical notification; one receipt row |
| G/PC-85 | Recovery notification after outage clears | `C544_RecoveryNotification` | `Ledger.CloseOutageWithRecovery` | skip inserting the recovery notification on closure | a separate `kind=recovery` `nid` with `linkedNid` reaches the recipient after the failure |
| G/PC-86 | Unauthorized/missing destination not silently replaced | `C544_AuthorizedDestination` | `TelegramBotTransport` authorization check | substitute a default chat id when unset | unset destination: zero sends, `destination-unauthorized` recorded, snapshot unqualified |
| G/PC-95 | Recipient evidence from authorized destination readback | `C544_ReceiptDestination` | `ReceiptImporter` rule 1 | accept any `peerId` | same body read from a different peer does not import |
| G/PC-96 | Readback contains whole produced payload | `C544_ReceiptWholeBody` | `ReceiptImporter` rule 4 | compare marker fields only | marker-only or truncated body with matching ids does not import |
| G/PC-97 | Evidence predating the attempt cannot confirm | `C544_ReceiptAttemptFloor` | `ReceiptImporter` rule 5 | ignore `date >= attempt.startedAt - 120 s` | an observation older than the attempt floor does not import |
| G/PC-98 | Independent outage state survives Windows inaccessible | `C544_IndependentState` | `Ledger` on the watchdog host + `WatchdogLoop` restart | persist intent through a Windows-side file instead of the ledger | restart with the Windows facts unavailable finds the same intent and delivers it |

Also carried by ID, unchanged in wording: **DL-4's** Windows-host-loss/independence
rows and **DL-5** entirely (the "persist intent before enqueue outside the failed
host; recover enqueue failure, lost enqueue response, accepted-but-delayed delivery,
crash before receipt persistence, healthy transition; busy/eligible consumers; no
second logical notification" contract is D-4/D-7/D-8 here); **V-10's**
notification/outage methods (the 13 above) and **V-11** (S5 execution: manual full
unattended green, subsequent real 00:30 scheduled green, independent Windows-hop
and Windmill outage/recovery, busy/eligible recipients, every DL-5 cut) execute
under S6. The CARD-0487 harness cases G-118..G-126 stay green and unrenumbered as
guards of the local evaluator's ledger.

### New guard candidates for TestDesign (IDs prefixed to avoid collision with CARD-0544's G-1..G-120)

| ID | Guard | Candidate control |
|---|---|---|
| G-545-1 | The watchdog has no Windmill dependency in its timer, ledger or transport | unit file declares no docker/windmill dependency; `TelegramBotTransport` and `Ledger` have no `IWindmillApi` reference (compile-time); PC: route the send through `IWindmillApi` → structural test fails |
| G-545-2 | Windmill unreachable/5xx/timeout opens `windmill-unreachable` with due-day identity and no run id | `C545_OutageKinds` row; PC: treat connect failure as "no jobs" → row expects the kind |
| G-545-3 | Missing watchdog heartbeat fails readiness closed | `Test-C545_WatchdogStale`, `C545_WatchdogInstance`; PC: treat an unreachable snapshot as fresh |
| G-545-4 | Desktop worker missing opens an outage without inventing a run | `C545_OutageKinds`; PC: synthesize a queued job when no worker pings |
| G-545-5 | Reader-first retry never issues a second logical notification | `C545_ReaderFirstRetry`; PC: resend on lost response without reading back |
| G-545-6 | Watchdog instance identity binds readiness | `C545_WatchdogInstance`; PC: skip the equality |
| G-545-7 | Recovery is sent even when the failure was never received, and says so | `C545_RecoveryLinksFailure`; PC: gate recovery on failure receipt |
| G-545-8 | Qualification namespace cannot use production state or enable faults in production | `C545_ProductionRefusesFaultInjection`; PC: drop the namespace check in `Validate()` |
| G-545-9 | Wrapper emits the JSON result line on every exit path and native state carries `localDueDate` | `Test-C545_ResultLine`; PC: omit the line on the refusal path |
| G-545-10 | Job adapter fetches results per completed row and never upgrades a missing result to success | `Test-C545_JobResultFetch`; PC: mark `success=true` rows `success` without a result |
| G-545-11 | Hop-failure classification is by SSH evidence, other failures stay `job-failed` | `C545_OutageKinds`; PC: classify every failure as `windows-hop-failed` |
| G-545-12 | Deadline-missed does not duplicate an already open due-day outage | `C545_DeadlineMissedSuppressedByOpenOutage`; PC: always open it |

TestDesign finalizes bodies, row identities and the PC table; Mutation runs each
method-scoped on a local inherited SourceLanding child.

## Qualification evidence design (acceptance 3 and 4)

### Artifact: `docs/investigations/<date>-card-0487-nightly-qualification.md`

Sections, each with the evidence that must be pasted or linked (paths under
`docs/investigations/<date>-card-0487-nightly-qualification-evidence/`):

1. Identity: CARD-0545, CARD-0544 link, this plan, the code SHA deployed on the
   desktop checkout and on server2, watchdog `version`/`configHash`/`instanceId`,
   Windmill `/api/version`, desktop worker `wm_version`.
2. Registration readback: script `hash`/`tag`/`path`, schedule cron/timezone/`enabled`/
   `args`, content SHA-256 vs checked-in payload, for both definitions.
3. Manual full unattended green: job id, `nativeRunId`, SHA/ref, `policyHash`,
   `localDueDate`, seven-suite table (discovered/executed/passed/failed/skipped),
   explicit manual exclusions (e2e, Headed/Explicit/live branches by policy id),
   build/lint outcomes, queue/build/per-chunk/total wall times, `summary.json` path.
4. Real scheduled 00:30 green: same table for the scheduled job (`schedule_path`
   present, `started_at`), `last-complete-green.json` contents, readiness monitor
   `Identity` block and `ReadyForDeferral=true`.
5. Independence and delivery: F-1..F-6 with, per case, the outage id, failure/recovery
   `nid`s, attempt rows (`startedAt`, `message_id`), receipt rows (`peerId`,
   `messageId`, `date`, `bodySha256`), the exported ledger JSON, and the snapshot
   captured during the outage. Windows-hop and Windmill-unavailability are separate
   rows with separate ids.
6. Production live notice: `nid`, `message_id`, receipt row, operator acknowledgement
   text and time, destination id.
7. Morning triage owner (name), destination authorization statement, credential
   reference table (names/locations only), host/supervisor/store/transport actually
   deployed (native or Docker fallback).
8. Residuals and non-claims: server2-wide loss, reader account choice, anything not
   exercised.

### Receipt: `C:\Antiphon\nightly\interim-qualification-receipt.json`

```json
{ "schemaVersion": 1,
  "repositoryPath": "C:\\src\\Antiphon", "projectId": "<guid or null>",
  "qualificationArtifactPath": "docs/investigations/<date>-card-0487-nightly-qualification.md",
  "qualificationArtifactCommitSha": "<40 hex>",
  "policyHash": "<from tests/test-execution-policy.json>", "scriptHash": "<Windmill script hash>",
  "manualRunId": "<nativeRunId>", "scheduledRunId": "<nativeRunId>", "scheduledJobId": "<Windmill job id>",
  "recipientEvidenceIds": ["<receipt ids from F-1, F-3 and the production notice>"],
  "outageRecoveryEvidenceIds": ["<outage ids from F-1 and F-3 with their recovery nids>"],
  "watchdogInstanceId": "<instanceId>" }
```

The reader rejects anything missing; the monitor's `Identity.WatchdogInstanceId`
must equal the receipt's.

## Cost

Active minutes, excluding overnight boundaries and operator waiting; verification
floor plus authoring, reported as bands.

| Slice | Band | Notes |
|---|---|---|
| TestDesign (13 carried + 12 new guards, fixtures, rows) | 120-180 | in-process C# fakes reduce harness cost versus the PS seams |
| S1 wrapper/adapter repair | 90-150 | includes `NightlyScriptsTests` and the health harness reruns |
| S2 watchdog core | 150-240 | clock, evaluator, ledger, SQLite tests |
| S3 delivery, reader, retry, recovery | 240-360 | 13 transferred methods plus 5 new; WTelegramClient contract shims |
| S4 snapshot, evaluator, reader guard | 120-180 | PS and C# both touched; full readiness class rerun |
| S5 deploy assets and docs | 90-150 | preflight script and owner doc |
| Review (independent, other company) | 120-180 | |
| Mutation (25 method-scoped PCs at ~1.5 min plus setup) | 60-90 | local inherited SourceLanding child only |
| S6 Deploy + qualification | 300-600 active, 2 overnight boundaries | one night for the scheduled green, one for F-1/F-2 windows if the operator schedules them at night; plus interactive login for OQ-3 |

## Scope boundaries

- No change to `AgentTaskReplyService`, land notifications or session delivery
  (CARD-0544 S1-S4 and PC-71 stay there).
- No Antiphon-scheduled agent, new stage, card status or tick spend.
- No change to the desktop worker container, the SSH bridge key or
  `worker__desktop` tags; no Windows Scheduled Task.
- No new external SaaS unless OQ-5 chooses one (then a follow-up card).
- The Windows-side local evaluator keeps CARD-0544's clock/readiness rules; the
  watchdog deliberately implements only the outage kinds in D-3 and does not compute
  `ReadyForDeferral`.
- Live Telegram traffic happens only in S6 under operator authorization; every test
  before that uses fakes or the local stub.

## Plan-stage validation

Read-only. No build, test, registration, token or message was executed by this
dispatch. Facts marked "per ClaudeBot memory" were not re-verified because doing so
needs a Windmill token.

--- next stage ---
next: decide
handoff: Decide OQ-1..OQ-6 (server2 host authorization, authorized destination, reader account or alternative destination, morning triage owner, dead-man's switch, watchdog token); then TestDesign finalizes the 13 carried N.C544_* controls and G-545-1..12 against the D-1..D-12 component contract before any Code.
artifact: docs/superpowers/plans/2026-09-17-card-0545-independent-nightly-watchdog-plan.md
