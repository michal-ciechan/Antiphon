# CARD-0545: independent nightly watchdog and recipient readback

Plan task `c9140e5d`, 2026-09-17, inspected checkout `f091e84d`. Revised in place by
plan task `117a4690` the same day (checkout `51c609a3`) per the operator's answers to
OQ-1..OQ-6; the revision's own observations are dated 2026-09-17 and marked "(rev)".
Card `b1c1ed0b-2608-40e7-99f5-f6587e9ed416` (Backlog). Explicit activation
prerequisite for CARD-0544 (TD-F2) and the deferred CARD-0487 S4 qualification.

This plan authorizes no deployment, notification, schedule change, agent launch,
credential creation or spend. Every observation below is read-only (Tailscale HTTP,
SSH inspection of the operator's chosen host, local Docker listing, checked-in
files). No Windmill token was minted, no message was sent, no schedule was touched.

**Host naming rule (OQ-1).** The operator authorized one specific always-on host for
this deployment and ruled that its name, addresses, user and paths must not be
hardcoded in tracked code, tracked configuration or tracked documentation. In this
document the "Ground truth" table is the historical record of what was inspected on
that host and keeps its identifiers; every design, component, slice and
qualification section below is written against "the watchdog host" and takes host
specifics only from the untracked deploy profile and the host's own env file (§ Deploy
profile). G-545-13 guards the tracked assets against regression.

## Disposition in five lines

1. **An independent host exists and is authorized for this deployment only (OQ-1).**
   The operator's chosen always-on host is separate hardware from the Windows desktop
   and offers systemd on its host OS outside Windmill's scheduler, workers, database
   and control plane. The watchdog is a systemd-supervised host process there (D-1);
   its identity lives only in the untracked deploy profile and the host env file, and
   the feature is documented generically so it can be deployed on any host that meets
   the D-1 requirements.
2. **No sender-side signal can stand in for the recipient's view, and the messaging
   bus adds none (OQ-3, rev).** The Kafka-to-Telegram gateway (`am-service`) publishes
   no acknowledgement topic; its only send-result sink is its container log; and what
   an acknowledgement would carry is the Bot API `sendMessage` `message_id`, which the
   watchdog already receives directly from its own send (D-5). That is Telegram's
   acceptance, not the recipient's receipt (D-13). Recommendation: keep the MTProto
   reader (D-6) as the qualification-grade receipt; OQ-2's DM destination fixes the
   reader identity to the operator's own account. Until the operator performs that
   one-time login, Deploy/qualification stays pending and CARD-0544 activation stays
   disabled; TestDesign and Code proceed offline against the reader contract.
3. **The checked-in "independent" health monitor is not independent and cannot even
   run where it is tagged**: from a Windmill worker on the chosen host it SSHes to
   `host.docker.internal`, which that Windmill compose does not define; every
   evaluation it would do runs on Windows, and its notification enqueue is itself a
   Windmill job. It is retired by this plan (D-9), never registered per the
   2026-09-16 census.
4. **Two latent defects block qualification regardless of the watchdog**: Windmill's
   `jobs/list` rows carry no `result`, so the production job adapter maps every real
   completed job to `unknown`; and the nightly wrapper emits no structured result
   line and `last-run.json` has no `localDueDate`. Both are repaired here (D-10).
5. **OQ-1, OQ-2, OQ-4 and OQ-5 are decided; OQ-3 is resolved by investigation and
   recommendation; OQ-6 and the reader login are deploy-time operator actions**
   (§ Decisions taken by the operator). No open design question remains.

## Ground truth

The rows dated 2026-09-17 without "(rev)" are the original inspection and keep the
inspected host's identifiers as the record of what was checked. Nothing in the
design below depends on those identifiers; where a row's consequence originally
stated a host-specific default, the consequence now names the profile or env value
that carries it.

| Card/plan assumption or question | Observed (2026-09-17, read-only) | Consequence |
|---|---|---|
| A host outside both the Windows execution host and Windmill's failure domain is available. | `server2` (Tailscale `100.93.77.126`, direct LAN `192.168.4.34`): Ubuntu 18.04.1, kernel 4.15, systemd 237, Docker 24.0.2, uptime 177 days, passwordless sudo for `mc`, `ufw` inactive, iptables INPUT ACCEPT, 71 GB free. Windmill is one compose project (`/home/mc/docker/windmill`: `windmill_server`, `windmill_worker` group `default`, `windmill_worker_native`, `windmill_db` published on `100.93.77.126:5433`). No user timers, no crontab, `Linger=no`. | A systemd **system** unit on that host is outside Windmill's scheduler/worker/control-plane and outside Docker. It shares the host (power, network, disk) with Windmill and with the Telegram gateway; that residual is named and accepted (D-1, OQ-5). D-1 states the host requirements generically; the identifiers go into the untracked deploy profile. |
| The chosen host might be coupled to the same failure domain as the desktop. | The desktop's only Windmill presence is the `windmill-desktop-worker` container (Docker Desktop, tag `desktop`, exclusive) connected to the host's Windmill DB over Tailscale. Nothing on that host depends on the desktop. | Windows-host loss is visible from the watchdog host as: desktop worker stops pinging, `desktop`-tagged jobs stay queued, SSH-bridge jobs fail with exit 255. |
| A third always-on host could host a dead-man's switch. | Tailscale peers: desktop (online), the chosen host (online), laptop `mc-dell-xps2023` offline 2 days, NAS offline 461 days, iPhone. | No third always-on host. The operator accepted the residual (OQ-5): no dead-man's switch; the Windows readiness evaluator fails closed within 20 minutes. |
| Windmill's health task detects Windmill loss independently. | `scripts/windmill/antiphon-nightly-health.json` is tag `default` (worker on the chosen host) but its content SSHes to `lndco@host.docker.internal`; that Windmill compose has no `extra_hosts`/`host-gateway`, so the name does not resolve there. All evaluation is `C:\src\Antiphon\scripts\nightly-health.ps1` on Windows. Its production sink enqueues `POST /api/w/mc/jobs/run/p/u/lndcobra/telegram_notify` (a Windmill job). Schedule `on_failure: null`. | Not independent and not runnable as tagged. Retire the definition; the Windows-side script survives only as the local readiness evaluator (D-9). G-116's routing guard is preserved literally. |
| The nightly definitions are registered. | Census 2026-09-16 08:41Z (`docs/investigations/2026-09-16-card-0544-evidence/windmill-census.txt`): no `antiphon_nightly_*` script or schedule; zero retained nightly jobs; live schedules are four unrelated `u/lndcobra/*` jobs. Windmill reports `CE v1.700.2-3-gd0f23cc523` at `GET /api/version` (unauthenticated, reachable from the desktop over Tailscale HTTP in 13 ms). | Registration and readback are Deploy-stage work under S6 of this plan (acceptance 3). |
| Windmill `jobs/list` returns the job result the adapter reads. | OpenAPI (`/api/openapi.yaml`, served unauthenticated) `CompletedJob` list rows carry `id, created_at, started_at, completed_at, duration_ms, success, script_path, script_hash, schedule_path, job_kind, ...` and **no `result`, no `scheduled_for`**. `ConvertFrom-NightlyWindmillJob` reads `$Job.result` and maps its absence to `unknown`. | Every real completed job is `unknown`; readiness could never turn green. Repair: fetch `GET /api/w/mc/jobs_u/completed/get_result/{id}` for candidate scheduled rows and derive the due day from `result.localDueDate` (D-10). `C544_ProductionJobAdapter` fixture must model the two-call shape. |
| The wrapper returns a structured completion record. | `antiphon-nightly-tests.json` content only runs the SSH command; `nightly-run.ps1` prints timestamped `Write-Host` lines and exits; no JSON last line; `grep localDueDate scripts/lib/*.ps1` finds it only in the health evaluator. `docs/testing-and-build.md` already records "production jobs map to unknown until the S4 qualification task adds it". | The wrapper's last stdout line must be the JSON record and native state must carry `localDueDate` (D-10). |
| The Telegram gateway can prove receipt. | `am-service` (`/home/mc/antiphon-messaging`, containers `am-service`, `am-redpanda`, `am-postgres`, `am-console`) relays `channels.outbound` to Bot API `sendMessage`; its log line `[outbound] sent via telegram -> <message_id>` is the Telegram server's acceptance, i.e. sender-side. Bot API has no `getHistory`; `getUpdates` never carries a bot's own or another bot's messages. | Recipient readback requires an MTProto user session reading the destination's history (D-6) or a non-Telegram destination with native history read (rejected in D-6). |
| The Kafka-to-Telegram sender publishes a send confirmation on another topic that the watchdog could consume as delivery evidence (the operator's OQ-3 question). **(rev)** | Source (`src/Antiphon.Messaging.Gateway/GatewayOutboundService.cs`, `DispatchAsync`): after `adapter.SendAsync` the `SendResult` is only logged (`[outbound] sent via {Channel} -> {MessageId}` or `[outbound] send failed via {Channel}: {Error}`); nothing is produced, stored or retried and the consumer auto-commits. `AntiphonGatewayOptions` and `Antiphon.Messaging.Service/appsettings.json` name three topics only (`channels.inbound`, `channels.outbound`, `channels.ops.inbound-unconsumed`); no `outbox`, `OutboundReceipt` or `DeliveryReceipt` type exists in `src/Antiphon.Messaging*`. Live broker `rpk topic list` 2026-09-17: exactly those three topics; consumer groups `antiphon-messaging-service-inbox`, `antiphon-messaging-service-outbound`, `antiphon-server-bridge`, `antiphon-server-bridge-ops-unconsumed`. The deployed gateway source file is byte-identical to the checkout (md5 `fa4e430b…`), image built 2026-09-12, container running. `SendResult.ChannelMessageId` is the Bot API `sendMessage` response `result.message_id` (`TelegramChannelAdapter`). | No acknowledgement topic exists. One could be added, but it would carry the same Bot API acceptance the watchdog already receives from its own direct `sendMessage` call (D-5), two hops later, inside the Docker/Redpanda/gateway domain D-5 rejected. Sender-side either way (D-13). |
| Existing Windmill notify plumbing can be the watchdog transport. | `u/lndcobra/telegram_notify` (python, Windmill variable `u/lndcobra/telegram_bot_token`) exists per ClaudeBot memory (2026-06, not re-verified here: no token). | It lives in Windmill's failure domain; the watchdog sends through the Bot API directly with its own credential reference (D-5). |
| The readiness reader already accepts independent evidence. | `InterimVerificationReadinessReader` requires `recipientEvidenceIds` and `outageRecoveryEvidenceIds` (non-empty) in `interim-qualification-receipt.json` plus matching `Identity` in `last-monitor.json`; it has no notion of which watchdog produced them and no freshness check on the watchdog. `MonitorFreshMinutes=60` applies to `last-monitor.json` only. | Add `watchdogInstanceId` to receipt and monitor identity and make the local evaluator fold watchdog heartbeat freshness into `Healthy` (D-9). |
| A .NET watchdog can run natively on the chosen host. | No `dotnet`, no `pwsh`, Python 3.6.9 on the host. glibc 2.27, OpenSSL 1.1.1, `libicuuc.so.60`, `/usr/share/zoneinfo/Europe/London` present. .NET 9 supported-OS notes list a glibc 2.23 floor and do **not** list Ubuntu 18.04. | Self-contained single-file `linux-x64` publish is expected to run; it is unsupported by Microsoft on that OS, so the first Deploy step is a smoke run with a stated fallback (D-2). The gate is generic: it applies to whatever host the profile names. |
| A Telegram user-session client exists for .NET. | NuGet `WTelegramClient` 4.4.8 (2026-08-18), netstandard2.0/.NET 5+, user login (`LoginUserIfNeeded`) and full client API including `Messages_GetHistory`. | Reader implementation choice for D-6. |
| Windmill tokens can be least-privilege. | OpenAPI `NewToken` has `label`, `expiration`, `scopes[]`, `workspace_id`. The superadmin password is only an argon2 hash; tokens are created by API with an existing token or by DB insert. | Operator creates a labelled, scoped watchdog token (OQ-6, deploy-time); the plan never mints one. |
| The PS notification ledger and its nine CARD-0487 harness cases (G-118..G-126) are the production notification path. | They are a JSON ledger plus seam-injected sink/recipient view; production sink is the Windmill job above. | Production notification moves to the watchdog. The PS ledger functions and G-118..G-126 stay as harness-level guards of the local evaluator; the production Windmill sink is removed (D-9). CARD-0487 IDs untouched. |
| Port for a private-network snapshot endpoint. | `ss -ltn` shows nothing on 17290 on the chosen host; no firewall filtering. | 17290 is the default **port**; the bind **address** is the profile's `snapshotBind` (D-9). Deploy re-verifies on the named host. |
| Tracked deploy precedent is host-agnostic. **(rev)** | `scripts/deploy-am-service.ps1` is a "fixed-target command" with the gateway host, user and remote root as literals (line 285); 47 tracked files name the host. `.gitignore` already ignores `.antiphon/` and local working dirs but has no `*.local.json` pattern. | The watchdog deploy script does **not** follow that precedent: its target comes only from the untracked profile and it refuses without one. Retrofitting the am-service script is out of scope (§ Scope boundaries). |

## Decisions

### D-1: the watchdog is a systemd-supervised host process on an operator-chosen host outside both failure domains; the host is named only in untracked configuration

`antiphon-nightly-watchdog.service` (system unit, `Restart=always`, `RestartSec=30`,
`After=network-online.target`, `ProtectSystem=strict`) runs a long-lived process with
an internal 10-minute tick. Long-lived rather than a `.timer` so the snapshot endpoint
(D-9) and reader session stay resident and systemd supervises crashes. The tracked
unit is a **template** (`antiphon-nightly-watchdog.service.template`) whose
`@@SERVICE_USER@@` and `@@REMOTE_ROOT@@` placeholders are rendered by the deploy script
from the profile at deploy time; `WorkingDirectory`, `EnvironmentFile`, `ExecStart` and
`ReadWritePaths` all derive from `@@REMOTE_ROOT@@`. Every path the watchdog uses at run
time is relative to its working directory (`./env`, `./state`, `./qual.json`) unless an
env value overrides it.

**Host requirements (what "any always-on host outside both failure domains" means):**

1. Not the Windows execution host, and not a process, container or job supervised by
   Windmill's scheduler, workers or control plane.
2. Linux with systemd for the native shape; alternatively any host with a container
   runtime for the D-2 fallback shape. A non-root service user with a writable state
   directory on persistent disk.
3. Network: outbound HTTPS to `api.telegram.org` (transport, D-5) and to Telegram's
   MTProto endpoints (reader, D-6); a route to Windmill's API (probes, D-3); and one
   private-network address (tailnet or LAN) on which the desktop can reach the
   snapshot port (D-9). No inbound exposure beyond that port on that address.
4. Time: a correct clock (NTP) and `Europe/London` zone data (D-3).

The operator's chosen host meets all four (ground truth). Sharing that host with
Windmill's containers and the Telegram gateway is a **residual, not a violation**: the
unit does not depend on `docker.service` or any container, so Windmill or gateway
failure leaves the watchdog running; only host-wide loss takes them down together.

**Why:** the card requires a timer/process, durable ledger and alert transport that
remain usable when Windmill or the Windows SSH hop is unavailable. systemd on a host
outside both domains satisfies that without a new machine or SaaS.

**Rejected:** (a) a Windmill job on any worker: inside the failure domain the card
forbids; (b) the desktop worker or a Windows Scheduled Task: inside the Windows host
domain and AGENTS.md forbids the task; (c) a Docker container beside Windmill's:
shares the Docker daemon with Windmill and the gateway; kept only as the fallback
runtime if the native .NET smoke run fails (D-2); (d) a GitHub Actions cron: Windmill
is private-network-only, so it would need new network exposure or an ephemeral tailnet
node, which is a spend and exposure decision the card does not authorize; (e) the
laptop: offline for two days at inspection, not always-on; (f) hardcoding the chosen
host in tracked assets, as `deploy-am-service.ps1` does: the operator ruled it out
(OQ-1), and a fixed target would make the feature non-portable and the tracked repo a
map of the operator's network.

**Residual (accepted, OQ-5):** loss of the watchdog host takes the watchdog and, in
the operator's deployment, the Telegram gateway down together and nobody is told
until the Windows readiness evaluator notices the missing heartbeat and fails closed
within 20 minutes (D-9). The operator accepted this on 2026-09-17; there is no
dead-man's switch and this plan does not pretend host-wide loss is detected.

### D-2: .NET 9 self-contained single-file binary; smoke-gated on the target host

New project `src/Antiphon.NightlyWatchdog/` (net9.0, matching the 25 existing
net9.0 projects), published with
`dotnet publish -c Release -r <rid> --self-contained -p:PublishSingleFile=true`
where `<rid>` is the profile's `rid` (default `linux-x64`). Tests live in
`tests/Antiphon.Tests` and reference the project directly, so the 13 transferred
controls and the new ones run in-process on local inherited children with fakes,
never on Windmill or an external executor.

**Why:** one language for probe, ledger, transport, reader and tests; method-scoped
PCs on C# methods; `WTelegramClient` gives the MTProto reader without a second runtime.

**Rejected:** Python on the host (the inspected host has 3.6.9: current Telethon
needs 3.7+; stdlib-only cannot do MTProto; PCs would cross a process boundary);
PowerShell on the host (not installed there; no MTProto client; the PS ledger's only
production sink is the Windmill job being retired); Node (no repo precedent for
server code).

**Gate:** Deploy step 1 is `./Antiphon.NightlyWatchdog --self-check` on the watchdog
host (prints runtime, OS release and glibc, tz conversion for two DST dates, opens a
temp SQLite ledger, binds the snapshot address from the env, and, when the bot token
is set, calls Bot API `getMe` to prove outbound HTTPS). The gate exists because the
chosen host may run an OS outside Microsoft's supported matrix (the inspected one
does). Fallback if it fails: run the same binary in
`mcr.microsoft.com/dotnet/runtime-deps:9.0` as a **separate** compose project
(`<remoteRoot>/docker-compose.yml`, `restart: always`, port mapping restricted to the
profile's `snapshotBind` address), which is still outside Windmill's compose/scheduler
but inside the Docker daemon; the profile's `runtime` records `native` or `container`
and the qualification artifact states which shape was deployed.

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

The Windmill base URL, workspace and paths are env values (`ANTIPHON_WATCHDOG_WINDMILL_BASE_URL`
and friends); the workspace `mc` and the `u/lndcobra/...` paths are Windmill
identifiers already present in tracked `scripts/windmill/*.json`, not host details.

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

### D-4: SQLite ledger in the watchdog's state directory with stable identities

`<stateDir>/ledger.db` (WAL) where `<stateDir>` is `ANTIPHON_WATCHDOG_STATE_DIR`
(default `./state` under the unit's working directory), tables `heartbeat`, `probes`,
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
the gateway's Postgres (Docker and a shared production database).

### D-5: transport is the Telegram Bot API called directly from the watchdog; its response is tier-1 acceptance

`TelegramBotTransport.SendAsync(nid, attempt)` posts `sendMessage` with
`disable_web_page_preview=true`, plain text (no parse mode), to the configured
`ANTIPHON_WATCHDOG_DESTINATION_CHAT_ID`. Unset, non-numeric or differing from the
qualified destination recorded in the ledger's `heartbeat.destination` refuses with
`destination-unauthorized` and sends nothing (PC-86). The destination decided under
OQ-2 (the operator's DM with `@antiphon_assistant_bot`) is placed by the operator in
the host env file only; no tracked file and no delegate ever writes the id, and the
ledger and artifact record it as `sha256(chatId)[..16]`, never raw.

**Evidence tiers (rev).** A notification attempt has three observable states:

| State | Set by | Meaning | Tier |
|---|---|---|---|
| `pending` | ledger intent (D-4) | intent persisted, nothing sent yet | none |
| `sent` | `TransportResult.Accepted` with `message_id`, `acceptedAt` | Telegram's servers accepted the message into the destination chat | 1, sender-side |
| `received` | `ReceiptImporter` (D-6) | the message is present in the recipient's own view of the chat | 2, recipient-side |

Tier 1 already catches the sender-side failure classes and records them on the
attempt: `401` (bad token), `400 chat not found` (wrong id), `403 bot was blocked by
the user`, `429` (retry-after honoured), `5xx`/timeouts/DNS (transport). What tier 1
cannot show: a message stored by Telegram that the operator's client never fetched or
that was deleted afterwards, or whether the operator read it. State never becomes
`received` from tier 1 (PC-81). The snapshot (D-9) and the qualification artifact
show both tiers separately so the operator sees "Telegram accepted (message_id) at
T" immediately, before and independently of the reader.

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
the gateway (Docker plus Redpanda plus gateway hops; an outage alert must not wait on
them; and per D-13 the bus would add no evidence the direct call lacks); e-mail (a
different operational channel from the one the operator uses).

### D-6: recipient readback is an MTProto user session, on the operator's own account, reading the DM history

`TelegramUserReader` (WTelegramClient, session file `<stateDir>/reader.session`,
`api_id`/`api_hash` from the env file) resolves the authorized peer and calls
`Messages_GetHistory` (limit 50, then paged back to the oldest pending attempt floor
minus 24 h at most). It returns `RecipientObservation { peerId, messageId, date,
text, readByRecipient }` rows. `ReceiptImporter` imports a receipt only when **all**
hold:

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

**Reader identity (rev, resolves the original OQ-3 options).** OQ-2 chose the
operator's DM with the bot. A bot DM's history is visible only to its two parties,
and the bot cannot read it through the Bot API, so the only account that can read
that destination's history is **the operator's own** (the original option A). Option
B (a dedicated reader account in a group) is incompatible with a DM destination and
is dropped; option C (a different destination technology) is rejected below. The
session is created once by the operator on the watchdog host with
`./Antiphon.NightlyWatchdog --reader-login` (interactive: phone number, code, 2FA
password; nothing is stored except WTelegramClient's session file, mode 0600, owner
the service user). The reader uses exactly one peer and one method
(`Messages_GetHistory`, plus `Messages_GetPeerDialogs` for the read marker); it never
sends. Because the session is as powerful as the account, the operator can revoke it
at any time from Telegram's active-sessions list, which fails the reader closed
(`Held`; notifications stay `sent`, never `received`), and the unit's
`ProtectSystem=strict` plus `ReadWritePaths=<stateDir>` confine it on disk.

**Optional read marker (rev).** With the operator's own account the dialog's
`read_inbox_max_id` is the recipient's own read position; the reader records
`readByRecipient = (read_inbox_max_id >= messageId)` on the observation and the
receipt row stores `readObservedAt` when it first turns true. This is recorded for
the artifact and snapshot only; it is **not** a condition of `received` and has no
PC. It is the one signal in this design that speaks to "the operator saw it".

**Why:** the card requires the authorized recipient's own message/transcript view
(CARD-0544 DL-5, PC-81/95/96/97). For Telegram that view exists only through a user
account's history.

**Rejected:** Bot API `getUpdates` (cannot see own or other bots' messages); the
`sendMessage` response, the gateway log or a gateway acknowledgement topic
(sender-side; D-13); an Antiphon session transcript as the outage destination (on
the Windows host that may be the thing that is down; allowed later as an additional
destination kind for non-outage notices, out of scope here); Slack/e-mail readers
(no configured workspace or Graph app is known; would change the operator's channel).

**Operator override path (stated so it is a decision, not a drift):** if the
operator prefers to run without the reader and treat tier 1 as sufficient for this
real-time-alerting use, that is a CARD-0544 contract change, not a CARD-0545 option:
PC-81, PC-95, PC-96 and PC-97 would retire, `recipientEvidenceIds` in the receipt
would be redefined as tier-1 attempt ids, and TD-F2's activation prerequisite would
be re-stated. This plan does not take that path; it proceeds under D-6 and D-13.

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

The watchdog serves `GET http://<snapshotBind>/snapshot.json` (read-only, no auth,
64 KB cap). `<snapshotBind>` is `ANTIPHON_WATCHDOG_SNAPSHOT_BIND` in the host env,
written by the deploy script from the profile: exactly one non-loopback, non-wildcard
address on a private network the desktop can reach (the tailnet in the operator's
deployment), default port 17290. A wildcard bind (`0.0.0.0`, `::`) is refused unless
the runtime is declared `container` (where the compose port mapping restricts the
published address). Snapshot shape:

```json
{ "schemaVersion": 1, "instanceId": "wd-…", "version": "…", "configHash": "…",
  "namespace": "mc", "heartbeatAt": "…Z", "tickSeconds": 600,
  "windmill": { "reachable": true, "lastOkAt": "…Z", "version": "CE v1.700.2-3-…" },
  "desktopWorker": { "seen": true, "lastPingAt": "…Z" },
  "schedule": { "present": true, "enabled": true, "scriptHash": "…" },
  "reader": { "available": true, "lastReadAt": "…Z", "held": false },
  "openOutages": [ { "outageId": "…", "kind": "…", "dueDay": "…", "openedAt": "…Z", "failureNid": "…", "failureState": "sent" } ],
  "recentNotifications": [ { "nid": "…", "kind": "failure", "outageId": "…", "state": "received", "attempts": 1, "acceptedAt": "…Z", "messageId": "…", "receivedAt": "…Z", "readObservedAt": null } ],
  "lastDueDay": { "dueDay": "…", "jobId": "…", "status": "success", "nativeRunId": "…", "sha": "…" } }
```

`scripts/nightly-health.ps1` (the Windows-side **local evaluator**, not the monitor)
gains `Get-NightlyWatchdogSnapshot` (bounded `Invoke-WebRequest`, 10 s, URL from
`readiness-config.json`'s `watchdogSnapshotUrl`) and `Test-NightlyWatchdogFreshness`
(heartbeat age 0..20 min, `instanceId` non-empty, `namespace` = `mc`). It adds
reasons `watchdog-unreachable`, `watchdog-stale`, `watchdog-malformed`,
`watchdog-outage-open` to `Health.Reasons` (each makes `Healthy=false`) and records
`Identity.WatchdogInstanceId`, `WatchdogHeartbeatAt` in `last-monitor.json`. The
server's `InterimVerificationReadinessReader` requires a new receipt field
`watchdogInstanceId` (schemaVersion stays 1: no production receipt exists yet) and
`Identity.WatchdogInstanceId` equality (`monitor_watchdog_mismatch`), so a replaced
or unqualified watchdog cannot inherit readiness. A stopped watchdog therefore fails
readiness closed within 20 minutes; the card's "missing heartbeat" guard is G-545-3
below. This is the whole of the OQ-5 residual's detection.

Windmill definitions after this card (all under `scripts/windmill/`, registration
remains an operator step):

| Definition | Change |
|---|---|
| `antiphon-nightly-tests.json` + schedule | Keep path/tag/cron. Content forwards the SSH exit code and relies on the ps1's final JSON line as the job result (D-10). |
| `antiphon-nightly-health.json` + schedule | **Deleted.** Never registered; not runnable from a worker on the watchdog host; superseded by the watchdog. README records why. |
| `antiphon-nightly-readiness.json` + schedule (new) | Tag `desktop`, `0 */30 * * * *`, Europe/London. SSH bridge runs `nightly-health.ps1 -RepositoryPath C:\src\Antiphon -ProjectId <guid>`; expected hashes, the watchdog snapshot URL and instance id come from `C:\Antiphon\nightly\readiness-config.json` written at qualification; `WINDMILL_TOKEN_FILE` points at an operator-placed token file. The tracked definition names no watchdog host. This job being on the desktop is correct: if Windows is down the Antiphon server that consumes readiness is down too, and if Windmill is down readiness goes stale and fails closed. |

`Test-NightlyMonitorRouting` (G-116) still throws for a desktop-tagged **monitor**;
the readiness job is not passed through it, so CARD-0487's guard keeps its meaning.
The production Windmill notification sink (`EnqueueNotification`, `NotifyPath`) is
removed from `New-NightlyProductionWindmillApi`; the PS ledger functions and the
seam-injected sink/recipient view remain so G-118..G-126 keep exercising the local
evaluator's ledger logic unchanged.

**Rejected for snapshot delivery:** SSH push from the watchdog host into the Windows
host (needs an administrator-authorized key on Windows held on that host: larger
blast radius than a read-only private listener); a Redpanda topic (Docker plus a
Kafka client under Windows PowerShell 5.1); Tailscale Serve (couples to tailscaled
version/config); a static-file container behind a reverse proxy (Docker again);
making the readiness reader itself fetch over the network (D-7 of CARD-0544 forbids
network in admission).

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

A second watchdog **instance** (`--config ./qual.json` under the same working
directory, namespace `mc/qual`, own state dir `./state-qual`, own snapshot bind from
the profile's `qualSnapshotBind`, default port 17291 on the same address) points at
qualification targets. `AllowFaultInjection=true` is honoured only when
`namespace != "mc"`; the production config cannot inject faults. The tracked
`qual.example.json` carries placeholders only; the deploy script renders the real
`qual.json` from the profile. Injection matrix:

| Case | Isolated target | Expected chain | What it proves |
|---|---|---|---|
| F-1 Windows hop | Qual script `u/lndcobra/antiphon_nightly_tests_qual_hop` (tag `desktop`) whose SSH uses `-i /tmp/windmill/no_such_key` → exit 255; one-off qual schedule a few minutes ahead | job failed → `windows-hop-failed` outage → failure message → readback → repoint qual schedule at `..._qual_ok` (echoes a valid result line) → success → recovery message → readback | Hop failure detected and delivered without touching the production schedule or Windows sshd |
| F-2 Desktop worker/queue | Qual script tagged `desktop-qual` (no worker owns the tag) | queued past grace → `start-overdue` → message → readback → retag the **script** to `desktop` → runs → recovery | Queue outage detected without editing the shared `worker__desktop` config |
| F-3 Windmill unavailable | `WindmillBaseUrl=http://<stubWindmillBind>` (profile value, default `127.0.0.1:17292`) served by `Antiphon.NightlyWatchdog --stub-windmill` on the watchdog host (modes `ok`, `refuse`, `503`, `timeout`) | ok → refuse (2 ticks) → `windmill-unreachable` → message → readback → ok (2 ticks) → recovery → readback | Detection and delivery need no Windmill; the real instance is untouched |
| F-4 Held reader | F-1 with the qual hold control file present for one tick | no resend while held; receipt imported on the next tick; exactly one message with attempt 1 | Busy consumer semantics (D-7) |
| F-5 Crash cuts | `CrashAfter=<intent|send-before-response|transport-accepted|reader-observation>` in the qual config; `systemctl restart` the qual unit | ledger resumes the same `nid`; one logical notification; receipt imported once | DL-5 persistence cuts on the real ledger, transport and reader |
| F-6 Missing heartbeat | Stop the **qual** instance; point a copy of `readiness-config.json` at the qual snapshot URL in a private state root | evaluator reports `watchdog-stale`/`watchdog-unreachable`; readiness reader returns unready | G-545-3 live |

Production live proof, once, after F-1..F-6: `Antiphon.NightlyWatchdog
--send-qualification-notice` on the production instance produces `kind=qualification`
through the same ledger/transport/reader; the operator acknowledges it in the chat and
the artifact records the `nid`, `message_id`, receipt row (with `readObservedAt` if
observed) and acknowledgement time. Machine readback proves receipt; the
acknowledgement is recorded, not inferred as comprehension.

Nothing disables the production schedule, the desktop worker, the reverse proxy,
Windmill or the gateway. Nothing sends to a chat the operator has not authorized.

### D-12: decided values live only in untracked locations; credentials are never invented

The morning triage owner (OQ-4: Mike, the operator), the authorized destination
(OQ-2), the watchdog host (OQ-1) and every credential are the operator's. This plan
records **who** and **what kind**; the values are placed by the operator in the deploy
profile, the host env file, the Windows state root and the qualification artifact
(which records the owner by name, the destination by kind and hash, and the host by
runtime shape and OS release, never by name or address). The safe default everywhere
is "unset means refuse": no destination → no send; no profile → no deploy; no
session → reader held. TestDesign and Code proceed offline on fakes; S6 waits for the
deploy-time actions.

### D-13: the messaging bus is not a delivery-evidence source and gains no acknowledgement topic in this card (rev)

The operator asked (OQ-3) whether the Kafka-to-Telegram sender reports success on
another topic. Investigated on 2026-09-17 (ground truth row "(rev)"): it does not.
`GatewayOutboundService.DispatchAsync` logs the `SendResult` and returns; the consumer
auto-commits; there is no ack topic, no outbound receipt table, no retry and no
dead-letter. The live broker has exactly three topics and the deployed gateway source
is byte-identical to the checkout.

Had such a topic existed, or if one is added later, its message would be
`{channel, conversationId, channelMessageId, ok|error, sentAt}` where
`channelMessageId` is the Bot API `sendMessage` response `message_id`. That is the
same tier-1 acceptance the watchdog records from its own direct call (D-5), reached
through two more hops (Redpanda, gateway) inside the Docker domain that D-5 rejected
for an outage alert. It cannot show that the operator's client fetched the message
or that the operator read it: the Bot API exposes no delivery or read receipts for
private chats, and blocked-bot, wrong-chat and bad-token refusals are already visible
to the direct call.

**Decision:** the watchdog does not consume `channels.*`, and this card adds no
acknowledgement topic to the gateway. The gap between "Telegram accepted" and "the
recipient's view contains it" stays closed by D-6, and the smaller gap to "the
operator read it" is observed, not gated, by the D-6 read marker.

**Optional follow-up, not this card:** an additive `channels.ops.outbound-sent`
event (mirroring `channels.ops.inbound-unconsumed`) would give the bus general
send-outcome observability for Antiphon sessions that reply through
`channels.outbound`. It would be a separate small card on the gateway; it changes
nothing here.

## Decisions taken by the operator (2026-09-17)

| ID | Operator's answer | Resolution in this plan |
|---|---|---|
| OQ-1 | The chosen host is authorized for this deployment only. Its name and details must not be hardcoded in tracked code; the feature must be generic and documented so a watchdog can run on another machine. | D-1, D-2, D-4, D-9, D-11 and D-12 rewritten host-agnostic; unit template plus untracked deploy profile (§ Deploy profile); `docs/nightly-watchdog.md` documents "deploy on any qualifying host"; G-545-13 guards the tracked assets. |
| OQ-2 | The operator's DM with `@antiphon_assistant_bot`. | D-5 unchanged in mechanism; the chat id is an env value placed by the operator; recorded as a hash in ledger and artifact. Forces the D-6 reader identity to the operator's own account. |
| OQ-3 | Asked whether the Kafka-to-Telegram sender confirms success on another topic. | Investigated: no such topic exists, and one would be sender-side (D-13). Recommendation adopted: keep the reader (D-6) on the operator's own account; tier-1 acceptance is recorded and shown separately (D-5). The reader login is a deploy-time action. |
| OQ-4 | Mike (the operator) is the named morning triage owner. | Recorded in the qualification artifact §7 and acceptance criterion 4; S6 step 8's acknowledgement is theirs. |
| OQ-5 | Accept the residual; no separate dead-man's switch. | D-1 residual marked accepted; D-9's 20-minute fail-closed is the only detection of host-wide loss. No follow-up card. |
| OQ-6 | Unchanged: a labelled, scoped Windmill token is a deploy-time operator action. | Listed under deploy-time actions; no design change. |

### Deploy-time actions (operator; S6 step 0)

None is a design question and none is performed by a delegate.

1. Write the deploy profile (§ Deploy profile) at the untracked path and keep a copy
   wherever the operator keeps machine secrets (a Bitwarden secure note is fine); the
   deploy script never reads a vault.
2. Create the labelled, expiring, scoped Windmill token (OQ-6) and place it in the
   host env file; place a second token file on the desktop for the readiness job.
3. Place the bot token (Bitwarden item "Telegram Bot Tokens (Antiphon / School
   Revision)") and the OQ-2 chat id in the host env file.
4. Obtain a Telegram `api_id`/`api_hash` for the reader, place them in the env file,
   and run the one-time interactive `--reader-login` on the watchdog host (D-6).
5. Provision the host per `docs/nightly-watchdog.md` (service user, state directory,
   private-network address for the snapshot bind).

### Open questions

None.

## Deploy profile (untracked) and tracked templates

The desktop-side deploy script finds its target only through a **deploy profile**:
JSON at `-Profile <path>`, else `ANTIPHON_WATCHDOG_DEPLOY_PROFILE`, else
`C:\Antiphon\nightly\watchdog-deploy.json` (the existing untracked nightly state root).
A repo-local `scripts/nightly-watchdog/*.local.json` is also accepted and is added to
`.gitignore` in S5. With no profile the script exits 3 with "no deploy profile" and
does nothing; it has no built-in fallback target.

Tracked `scripts/nightly-watchdog/deploy.example.json` (placeholders only; the
validator rejects any value still containing `<` or `>`):

```json
{
  "sshTarget": "<user>@<host>",
  "sshIdentityFile": null,
  "remoteRoot": "/home/<user>/antiphon-watchdog",
  "serviceUser": "<user>",
  "rid": "linux-x64",
  "runtime": "native",
  "snapshotBind": "<private-ip>:17290",
  "snapshotUrl": "http://<private-ip>:17290/snapshot.json",
  "qualSnapshotBind": "<private-ip>:17291",
  "stubWindmillBind": "127.0.0.1:17292"
}
```

Tracked `antiphon-nightly-watchdog.service.template`:

```
[Unit]
Description=Antiphon nightly watchdog
After=network-online.target
Wants=network-online.target

[Service]
User=@@SERVICE_USER@@
WorkingDirectory=@@REMOTE_ROOT@@
EnvironmentFile=@@REMOTE_ROOT@@/env
ExecStart=@@REMOTE_ROOT@@/Antiphon.NightlyWatchdog run
Restart=always
RestartSec=30
ProtectSystem=strict
ReadWritePaths=@@REMOTE_ROOT@@/state @@REMOTE_ROOT@@/state-qual

[Install]
WantedBy=multi-user.target
```

The deploy script renders the template and `qual.json` into the scratch directory,
uploads binary, unit and rendered files over `ssh <sshTarget>`, and writes only the
non-secret env keys it owns (`ANTIPHON_WATCHDOG_SNAPSHOT_BIND`,
`ANTIPHON_WATCHDOG_STATE_DIR`, `ANTIPHON_WATCHDOG_NAMESPACE`) into `<remoteRoot>/env`
if absent, never touching secret keys. `readiness-config.json` on the desktop gets
`watchdogSnapshotUrl` from the profile at qualification.

## Component design

### Watchdog service (`src/Antiphon.NightlyWatchdog/`)

| File | Responsibility |
|---|---|
| `Program.cs` | Commands: `run` (service loop), `--self-check`, `--reader-login` (interactive, operator only), `--send-qualification-notice`, `--stub-windmill`, `--export-evidence <dir>` (writes receipts/outages/notifications JSON for the artifact). |
| `WatchdogOptions.cs` | Typed config from env/JSON: namespace, Windmill base URL, token, workspace, schedule/script paths, expected script hash, destination chat id, Telegram bot token, reader api id/hash/session path, state dir, snapshot bind, runtime, tick, grace/deadline/budget minutes, `AllowFaultInjection`, `CrashAfter`, hold control path. `Validate()` refuses production namespace with fault injection and a wildcard snapshot bind outside the container runtime. |
| `LondonClock.cs` | `DueDay(now)`, `DueUtc(day)`, `GraceEndUtc(day)`, `MorningDeadlineUtc(day)` via `TimeZoneInfo.FindSystemTimeZoneById("Europe/London")`; tested with the same dates as `Test-C544_LondonDates` (2026-03-29/30, 2026-10-25, 01:00 BST overdue). |
| `IWindmillApi.cs`, `WindmillHttpApi.cs` | Version, whoami, script get, schedule get, workers list, jobs list, completed result; 15 s timeouts; classifies transport failures. |
| `OutageEvaluator.cs` | Pure function `(probes, jobs, now, ledgerView) -> OutageTransitions` implementing D-3. |
| `Ledger.cs` | SQLite (Microsoft.Data.Sqlite) with the D-4 schema and transactional `OpenOutageWithIntent`, `CloseOutageWithRecovery`, `RecordAttempt` (stores `acceptedAt`, `messageId`, error class), `ImportReceipt`, `Heartbeat`. |
| `NotificationBody.cs` | Renders the D-5 body and marker; parses markers from recipient text. |
| `INotificationTransport.cs`, `TelegramBotTransport.cs` | `sendMessage`; destination authorization check; returns `TransportResult { Accepted, MessageId, ErrorClass, Error }`. |
| `IRecipientReader.cs`, `TelegramUserReader.cs` | WTelegramClient history read and dialog read marker; `Held` state; peer resolution; `--reader-login` flow. |
| `ReceiptImporter.cs` | D-6 rules 1-5; idempotent import; records `readObservedAt`. |
| `RetryPolicy.cs` | D-7 reader-first retry and backoff. |
| `SnapshotServer.cs` | `HttpListener` on the configured bind; serves `/snapshot.json` from the ledger; 64 KB cap. |
| `WatchdogLoop.cs` | Tick orchestration; heartbeat; fault-injection hooks (`CrashAfter`) that call `Environment.FailFast` only when allowed. |
| `WindmillStub.cs` | The F-3 stub server (modes `ok/refuse/503/timeout`), qualification only. |

Packages: `Microsoft.Data.Sqlite` (latest 9.x), `WTelegramClient` (4.4.8),
`Microsoft.Extensions.Logging.Console`. No Windmill SDK, no Kafka.

### Deployment assets (`scripts/nightly-watchdog/`)

`antiphon-nightly-watchdog.service.template` (D-1), `deploy.example.json`,
`env.example` (names only, no values, no paths beyond `./state`),
`qual.example.json` (placeholders), `README.md` (install, self-check, profile,
token/session placement as operator instructions, never values, never a host name).
`scripts/deploy-nightly-watchdog.ps1` mirrors `deploy-am-service.ps1`'s
preflight/`-Deploy` shape but **not** its fixed target: default is a read-only
preflight (publish for the profile's `rid`, checksum, render template and qual
config, print the profile's non-secret values, remote `--self-check` dry run);
`-Deploy` is the explicit opt-in that uploads the binary, unit and rendered files via
`ssh <sshTarget>`, runs `sudo systemctl daemon-reload && enable --now`, and verifies
`GET <snapshotUrl>`. ASCII-only, PS 5.1-safe. Exit 3 without a profile or with
placeholder values.

`docs/nightly-watchdog.md` (new owner doc) is written for **any** host: the D-1
requirements checklist, the profile keys, the native and container shapes, the
self-check, the reader login, the qualification instance, the readiness config on
the desktop, and the custody table. It names no host, address or user.

### Credential and configuration references (names and locations only)

| Reference | Where | Custodian | Used by |
|---|---|---|---|
| Deploy profile (`sshTarget`, `remoteRoot`, `serviceUser`, binds, URLs) | `C:\Antiphon\nightly\watchdog-deploy.json` or `ANTIPHON_WATCHDOG_DEPLOY_PROFILE`; untracked | operator (OQ-1) | deploy script, readiness config |
| `ANTIPHON_WATCHDOG_WINDMILL_BASE_URL`, `_WINDMILL_WORKSPACE`, `_SCHEDULE_PATH`, `_SCRIPT_PATH`, `_EXPECTED_SCRIPT_HASH` | `<remoteRoot>/env` (0600, owner the service user) | operator | probes |
| `ANTIPHON_WATCHDOG_WINDMILL_TOKEN` | same env file | operator (OQ-6) | probes |
| `ANTIPHON_WATCHDOG_TELEGRAM_BOT_TOKEN` | same env file; source is the Bitwarden item "Telegram Bot Tokens (Antiphon / School Revision)" | operator | transport |
| `ANTIPHON_WATCHDOG_DESTINATION_CHAT_ID` | same env file | operator (OQ-2) | transport authorization |
| `ANTIPHON_WATCHDOG_TG_API_ID` / `_API_HASH` and `<stateDir>/reader.session` | env file and state dir | operator (D-6 login) | reader |
| `ANTIPHON_WATCHDOG_SNAPSHOT_BIND`, `_STATE_DIR`, `_NAMESPACE` | env file, written by the deploy script from the profile | deploy script | snapshot, ledger |
| `WINDMILL_TOKEN_FILE` → `C:\Antiphon\nightly\secrets\windmill-token` | desktop user env for `lndco` | operator | readiness job |
| `C:\Antiphon\nightly\readiness-config.json` | desktop state root | written at qualification; contains hashes, `watchdogSnapshotUrl` and instance id, no secrets | readiness job |

`docs/agent-credentials.md` gains a short "Nightly watchdog" custody row set; no
value is ever logged, printed or committed, and no tracked file names the host.

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
before any transport call), `C545_ReceiptIdempotent`, `C545_BodyHashRoundTrip`,
`C545_OptionsRefuseWildcardBind` (wildcard bind refused for `native`, allowed for
`container`).

### S3: notification producer, transport, reader, importer, retry, recovery (C#)

Files: `src/Antiphon.NightlyWatchdog/{INotificationTransport.cs, TelegramBotTransport.cs,
IRecipientReader.cs, TelegramUserReader.cs, ReceiptImporter.cs, RetryPolicy.cs,
WatchdogLoop.cs, Program.cs}`; test fakes `tests/Antiphon.Tests/TestHelpers/C545World.cs`
(`FakeWindmillApi`, `FakeTransport` with lost-response, reject and error-class modes,
`FakeRecipientReader` with held/eligible modes and a read-marker knob,
`FakeTimeProvider`, temp ledger, crash-at-boundary hooks).

Tests: the 13 transferred methods implemented in-process in
`NightlyVerificationContractTests` (ledger below) plus new
`C545_ReaderFirstRetry`, `C545_HeldReaderNoResend`, `C545_RecoveryLinksFailure`,
`C545_QualificationNoticePath`, `C545_ProductionRefusesFaultInjection`,
`C545_AcceptanceRecordedSeparately` (tier-1 fields present on the attempt and in
the snapshot while `state` stays `sent`; `readObservedAt` recorded without changing
`received`). `TelegramBotTransport` and `TelegramUserReader` get thin contract
tests against local HTTP/fake clients for request shape only; live Telegram is S6.

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

### S5: deployment assets, profile, docs, custody, host-agnostic guard

Files: `scripts/nightly-watchdog/{antiphon-nightly-watchdog.service.template,
deploy.example.json, env.example, qual.example.json, README.md}`,
`scripts/deploy-nightly-watchdog.ps1`, `scripts/test-deploy-nightly-watchdog.ps1`,
`.gitignore` (`scripts/nightly-watchdog/*.local.json`), new owner doc
`docs/nightly-watchdog.md`, `AGENTS.md` table row ("Nightly backstop, watchdog and
qualification"), `docs/testing-and-build.md` Nightly section rewrite,
`docs/agent-credentials.md` custody rows, `tests/test-execution-policy.json` script
entry for the new test script.

Tests (`scripts/test-deploy-nightly-watchdog.ps1`, preflight-only, seam-injected
runners): `Test-C545_DeployRefusesWithoutProfile` (no `-Profile`, env unset, default
path absent → exit 3, no ssh call; profile with a placeholder value → exit 3),
`Test-C545_DeployRendersFromProfile` (template placeholders fully substituted; env
keys written are exactly the three non-secret ones; secrets never read or printed),
`Test-C545_HostAgnosticAssets` (scans the tracked set `scripts/nightly-watchdog/**`,
`scripts/deploy-nightly-watchdog.ps1`, `docs/nightly-watchdog.md`,
`scripts/windmill/antiphon-nightly-readiness*.json` for IPv4 literals other than
`127.0.0.1`/`0.0.0.0`, for `name@host` SSH-target tokens, and for absolute `/home/`
paths other than the `<user>` placeholder; the scan's denylist is structural, so the
test itself names no host), manifest and ASCII checks; C# wrappers
`C545_DeployRefusesWithoutProfile`, `C545_HostAgnosticAssets` in
`NightlyVerificationContractTests`.

### S6: explicitly authorized Deploy and qualification (blocked on the deploy-time actions)

Operator-run, in this order, each step recorded in
`docs/investigations/<date>-card-0487-nightly-qualification.md`:

0. Deploy-time actions 1-5 (§ Decisions taken by the operator). Nothing committed.
1. Preflight then `-Deploy` of the production unit with destination **unset**;
   self-check on the watchdog host (D-2 gate); record runtime shape, OS release and
   glibc (not the host name).
2. Register/read back `antiphon_nightly_tests` script and schedule (POST
   `scripts/create`, `schedules/create`; GET both back; compare content SHA-256 to the
   checked-in payload; record `hash`, `tag`, cron, timezone, args `{}`,
   worker/server versions from `/api/version` and `workers/list`).
3. Register/read back `antiphon_nightly_readiness`; place the desktop token file and
   `readiness-config.json` (with `watchdogSnapshotUrl` from the profile); observe one
   green readiness tick.
4. Observe heartbeat and snapshot; confirm the evaluator folds it in.
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
7. Reader login (`--reader-login`, interactive, operator only); then the
   qualification instance: F-1..F-6 (D-11) with the operator-authorized destination;
   export ledger evidence; record `nid`s, `message_id`s, receipt rows, timings.
8. Set the production destination; `--send-qualification-notice`; the operator
   (OQ-4) acknowledges; record.
9. Publish `C:\Antiphon\nightly\interim-qualification-receipt.json` (below) only
   after 0-8; commit the artifact; link the SourceLanding companion for the code
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
| G-545-1 | The watchdog has no Windmill dependency in its timer, ledger or transport | unit template declares no docker/windmill dependency; `TelegramBotTransport` and `Ledger` have no `IWindmillApi` reference (compile-time); PC: route the send through `IWindmillApi` → structural test fails |
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
| G-545-13 | Tracked watchdog assets, deploy script and owner doc name no host, address, user or absolute home path, and the deploy script refuses without a profile | `Test-C545_HostAgnosticAssets`, `Test-C545_DeployRefusesWithoutProfile`; PC: give the deploy script a built-in fallback `sshTarget` → both fail |
| G-545-14 | Tier-1 acceptance is recorded and shown but never promotes state; the read marker is recorded but never gates `received` | `C545_AcceptanceRecordedSeparately`; PC: set `state=received` when `readByRecipient` is true without a matching body |

TestDesign finalizes bodies, row identities and the PC table; Mutation runs each
method-scoped on a local inherited SourceLanding child.

## Qualification evidence design (acceptance 3 and 4)

### Artifact: `docs/investigations/<date>-card-0487-nightly-qualification.md`

Sections, each with the evidence that must be pasted or linked (paths under
`docs/investigations/<date>-card-0487-nightly-qualification-evidence/`). The artifact
is tracked, so it records the watchdog host by runtime shape, OS release and glibc,
never by name, address or user (OQ-1), and the destination by kind and
`sha256(chatId)[..16]`, never raw.

1. Identity: CARD-0545, CARD-0544 link, this plan, the code SHA deployed on the
   desktop checkout and on the watchdog host, watchdog `version`/`configHash`/
   `instanceId`, Windmill `/api/version`, desktop worker `wm_version`.
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
   `nid`s, attempt rows (`startedAt`, `acceptedAt`, `message_id`, error class),
   receipt rows (`peerId` hash, `messageId`, `date`, `bodySha256`, `readObservedAt`),
   the exported ledger JSON, and the snapshot captured during the outage. Windows-hop
   and Windmill-unavailability are separate rows with separate ids.
6. Production live notice: `nid`, `message_id`, receipt row, operator acknowledgement
   text and time, destination kind and hash.
7. Morning triage owner: Mike (the operator), per OQ-4; destination authorization
   statement (OQ-2); credential reference table (names/locations only);
   host/supervisor/store/transport actually deployed (native or container; OS
   release) and the profile keys used, without their values.
8. Residuals and non-claims: host-wide loss (accepted, OQ-5), the reader session's
   power and revocation path, tier-1 versus tier-2 evidence per notification,
   anything not exercised.

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
must equal the receipt's. `recipientEvidenceIds` are tier-2 receipt ids (D-5 table);
tier-1 attempt ids never satisfy the field.

## Cost

Active minutes, excluding overnight boundaries and operator waiting; verification
floor plus authoring, reported as bands.

| Slice | Band | Notes |
|---|---|---|
| TestDesign (13 carried + 14 new guards, fixtures, rows) | 130-190 | in-process C# fakes reduce harness cost versus the PS seams |
| S1 wrapper/adapter repair | 90-150 | includes `NightlyScriptsTests` and the health harness reruns |
| S2 watchdog core | 150-240 | clock, evaluator, ledger, SQLite tests |
| S3 delivery, reader, retry, recovery | 240-360 | 13 transferred methods plus 6 new; WTelegramClient contract shims |
| S4 snapshot, evaluator, reader guard | 120-180 | PS and C# both touched; full readiness class rerun |
| S5 deploy assets, profile, docs | 105-165 | preflight script, template rendering, host-agnostic scan, owner doc |
| Review (independent, other company) | 120-180 | |
| Mutation (27 method-scoped PCs at ~1.5 min plus setup) | 60-100 | local inherited SourceLanding child only |
| S6 Deploy + qualification | 300-600 active, 2 overnight boundaries | one night for the scheduled green, one for F-1/F-2 windows if the operator schedules them at night; plus the interactive reader login |

## Scope boundaries

- No change to `AgentTaskReplyService`, land notifications or session delivery
  (CARD-0544 S1-S4 and PC-71 stay there).
- No Antiphon-scheduled agent, new stage, card status or tick spend.
- No change to the desktop worker container, the SSH bridge key or
  `worker__desktop` tags; no Windows Scheduled Task.
- No new external SaaS; OQ-5 accepted the residual.
- No change to the messaging gateway: no acknowledgement topic, no consumer of
  `channels.*` in the watchdog (D-13). The optional `channels.ops.outbound-sent`
  event is a separate card if ever wanted.
- `scripts/deploy-am-service.ps1` keeps its fixed target; retrofitting it to a
  profile is a separate card. The watchdog's deploy path is profile-only from day one.
- The Windows-side local evaluator keeps CARD-0544's clock/readiness rules; the
  watchdog deliberately implements only the outage kinds in D-3 and does not compute
  `ReadyForDeferral`.
- Live Telegram traffic happens only in S6 under operator authorization; every test
  before that uses fakes or the local stub.

## Plan-stage validation

Read-only, both passes. No build, test, registration, token or message was executed
by either dispatch. Facts marked "per ClaudeBot memory" were not re-verified because
doing so needs a Windmill token. The revision's additional observations (topic list,
consumer groups, deployed-source checksum, dispatcher behaviour, deploy-script
precedent, `.gitignore` patterns) came from the checkout and a read-only SSH
inspection of the running gateway; nothing on the host was changed.

--- next stage ---
next: test-design
handoff: TestDesign finalizes the 13 carried N.C544_* controls and G-545-1..14 against the D-1..D-13 component contract (host-agnostic deploy profile, tier-1/tier-2 evidence, reader on the operator's own account, no bus acknowledgement); S6 stays blocked only on the operator's deploy-time actions.
artifact: docs/superpowers/plans/2026-09-17-card-0545-independent-nightly-watchdog-plan.md

## Verification design (TestDesign, task 4a2497b2)

TestDesign dispatch 2026-09-17 on checkout `cb0fee37` (master; plan revision `a61d4e56`
is an ancestor). Nothing below changes the fix design (D-1..D-13); it finalizes the
controls Code implements. Ten stage rules that every row below follows:

1. The 13 carried controls keep their CARD-0544 IDs, guard text and method names
   verbatim (`NightlyVerificationContractTests.C544_*`). Only the body, the mutation and
   the decisive assertion are finalized here.
2. New guards are `G-545-n` with a 1:1 `PC-545-n`. The plan's candidates G-545-1..14
   keep their numbers; G-545-15..39 are splits of independently bypassable checks the
   candidates bundled, or D-3..D-9 invariants the candidate list left untested.
3. In-process C# tests run the real `Antiphon.NightlyWatchdog` components
   (`WatchdogLoop`, `OutageEvaluator`, `Ledger` on a real temp SQLite file,
   `NotificationBody`, `ReceiptImporter`, `RetryPolicy`, `SnapshotServer`) with fakes
   only at the four external boundaries: Windmill HTTP, Telegram Bot API, the MTProto
   reader, and the clock. No test fakes the ledger.
4. Positive receipt evidence is produced only by the fake transport delivering into the
   fake recipient chat; a test never writes an observation or a receipt row by hand.
   Negative observations (an adversarial message that must not import) are produced by
   the fake reader distorting exactly one field of a delivered message, or by a second
   ledger sharing the same chat; each negative names the importer rejection reason it
   expects, so a dropped rule is red even when a later rule would also reject.
5. `state=received` is set only by `ReceiptImporter` from a reader observation.
   `TransportResult.Accepted` (tier 1) and `readByRecipient` never set it.
6. PowerShell harness cases are `Test-C545_*` functions in the existing harness files,
   invoked by name through the C# wrappers (`-Case`), and every assertion row has a
   unique `PASS C545 ...` name that the wrapper requires.
7. Every time-dependent row states the exact London instant and its UTC value. The
   fixture due day is `2026-09-18` (BST): due `2026-09-17T23:30:00Z`, grace end
   `2026-09-18T00:00:00Z` (01:00 London), morning deadline `2026-09-18T07:00:00Z`.
8. Row names inside multi-row methods are quoted strings passed as the Shouldly custom
   message; a PC's expected red names the row.
9. Live Telegram, live Windmill, systemd and the real MTProto session are S6 only.
   Every substitute is declared with what it cannot prove.
10. Method-scoped filters: `--treenode-filter "/*/*/<Class>/<Method>"`; PS cases:
    `pwsh -NoProfile -File scripts/<harness>.ps1 -Case <Name> -ResultsDirectory <fresh>`.

### Inspection

Bodies read in full at `cb0fee37` (line counts as inspected):

- `tests/Antiphon.Tests/Scripts/NightlyVerificationContractTests.cs` (109): ten C544
  pwsh-wrapper methods; `RunCaseAsync` is hard-wired to `test-nightly-health.ps1` and
  the `PASS C544 ` prefix and requires `C487 HARNESS EXIT CODE: 0`, zero `FAIL ` lines,
  an exact `PASS` count and `C487: N passed, 0 failed, N rows`. Boundaries -> the class
  becomes `partial`; wrapper helper gains script/prefix parameters (V-545-15..22);
  `C544_ProductionJobAdapter` row count 21 -> 22 (R-545-2).
- `tests/Antiphon.Tests/TestHelpers/C544World.cs` (489): the nearest fixture for a new
  world (temp roots, `FakeTimeProvider`-style clock, `RestartAsync` rebuilding services
  over the same state, `ControlledInterimReadiness.ReadySnapshot` constructs
  `InterimReadinessSnapshot` positionally). Boundaries -> `C545World` shape below;
  `ReadySnapshot` gains the ninth positional `WatchdogInstanceId` (R-545-4).
- `tests/Antiphon.Tests/Application/InterimVerificationReadinessTests.cs` (328) and
  `server/Infrastructure/Files/InterimVerificationReadinessReader.cs` (231): `StateFixture`
  receipt/monitor JSON, exact age equalities (60m fresh, 60m+1 tick stale), reason codes.
  Boundaries -> `C545_WatchdogInstance` rows; fixture `Reset()` gains
  `watchdogInstanceId`/`Identity.WatchdogInstanceId` so the twelve C544 methods stay
  green (R-545-3); `tests/Antiphon.Tests/Application/VerificationRoundBriefTests.cs:128`
  constructs the snapshot positionally and must gain the ninth argument (R-545-4).
- `scripts/test-nightly-health.ps1` (609) and `scripts/lib/nightly-health.ps1` (831):
  `New-HealthFx` seams (`UtcNow`, `WindmillApi`, `NotificationSink`, `RecipientView`,
  `AllowOfflineNotify`), `Start-C544WindmillStub` (one request, one body),
  `Test-C544_ProductionJobAdapter` (8 list rows carrying inline `result`),
  `Test-NightlyMonitorHealth`, `Invoke-AntiphonNightlyHealth` (production sink
  `EnqueueNotification`/`NotifyPath`, `Identity` block), `$script:C544ExpectedRows = 83`,
  full-run `ExpectedRows 52 + 83`. Boundaries -> stub becomes a route table
  (`-Routes @{ '<url-suffix>' = <body|status> }`) serving 1 + N requests; adapter rows move
  `result` to `get_result` responses; `j-manual` (no `schedule_path`) is never fetched and
  becomes `unknown`; 20-minute heartbeat rows; `Identity.WatchdogInstanceId`.
- `scripts/test-nightly-run.ps1` (320) and `scripts/lib/nightly-run-impl.ps1` (464):
  `New-RunFx`/`Invoke-FxRun`; `Write-C487Seams` `StartProcess` seam receives the
  `nightly-tests.ps1` argument list including `-LogRoot <runDir>` and `-RunId`; the
  wrapper reads `<runDir>\summary.json` (`coverageComplete`, `testsPassed`,
  `reportDelivered`, `policyHash`) and runs `nightly-report.ps1` unless `-NoReport`;
  refusal returns exit 3 before any state write; the default case loop is prefix
  `C487_G0` with `ExpectedRows 56`. Boundaries -> `Test-C545_ResultLine` rows (green,
  refusal, test-red, no-report) and the `UtcNow` seam for DST dates; loop gains prefix
  `C545_`.
- `scripts/lib/c487-harness.ps1` (338): `Assert-C487`, `Write-C487Seams`,
  `Complete-C487Harness` (`-ExpectedRows 0` under `-Case`), `Get-C487CaseFunctions`.
  Reused unchanged by the new deploy harness.
- `scripts/test-deploy-am-service.ps1` (212) and `scripts/deploy-am-service.ps1`
  (389; dot-source guard at line 377: `if ($MyInvocation.InvocationName -eq '.') { return }`;
  fixed target literals at line 285; seam-injected `SshRunner`/`ScpRunner`/`HttpRunner`).
  Boundaries -> the watchdog deploy script keeps the dot-source guard and the injected
  runner shape but takes its target only from the profile.
- `scripts/windmill/*.json` (4 files) and `README.md` (15); `scripts/nightly-health.ps1`
  (34); `scripts/nightly-run.ps1` (98); `tests/Antiphon.Tests/Scripts/NightlyScriptsTests.cs`
  (96: ASCII list, `nightly-run.ps1` must still name `C:\Antiphon\nightly\checkout`).
  Boundaries -> `Test-C545_ReadinessRouting` census rows; ASCII list extension (R-545-6).
- `tests/test-execution-policy.json` (`scriptCensus`, `policyHash` verified by
  `Get-NightlyPolicyHash`); `tests/Antiphon.Tests/Antiphon.Tests.csproj` (project
  references, `Microsoft.Extensions.TimeProvider.Testing` 9.5.0 already present);
  `Directory.Build.props`; `.gitignore`; `tests/Shared/TestClassificationMetadata.cs`
  (categories `Unit`/`Integration`/`Slow`/`OptIn`); `tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs`.
- `docs/testing-and-build.md` "Mutation-stage positive-control execution" and "Nightly";
  CARD-0544 plan `## D-10 transfer ledger`, `### Delivery inventory`, PC-78..86/95..98
  rows and cost tables (for the carried wording and the moved-cost baseline).

**Boundaries covered (row -> control):** start grace 00:59:59 vs 01:00:00 London
(V-545-3 rows `start-overdue/before-grace`, `start-overdue/at-grace`; G-545-31);
morning deadline 07:59:59 vs 08:00:00 (`deadline-missed/*`; G-545-31); D-1 in scope
until 08:00 of D (`previous-day/*`); DST due instants 2026-03-28/29/30 and 10-24/25/26
plus London midnight instants (V-545-1; G-545-32); DST x grace (`start-overdue/dst-bst`
at 2026-03-30 01:00 BST = 2026-03-30T00:00:00Z); two-consecutive-tick thresholds
(1 vs 2 ticks; G-545-2); desktop-worker ping age 59m vs 60m (G-545-4);
`RunBudgetHours` 5:59:59 vs 6:00:00 (V-545-3 `run-stalled/*`, no PC); attempt floor
-120 s vs -121 s (PC-97); receipt grace 29:59 vs 30:00 (V-545-10; G-545-5 rows);
reader hold expiry 29:59 vs 30:00 (G-545-30 rows); retry backoff 2/5/10/30/60 (V-545-10,
no PC); heartbeat age 20:00 vs 20:01 and future (G-545-3); snapshot cap 64 KB
(V-545-13, no PC); body length < 4096 and ASCII (V-545-6, no PC); held reader x lost
response (G-545-30 row `held-then-eligible`); crash x each persistence cut (PC-84
rows); worker-missing x start-overdue (PC-545-4 row). **Excluded:** DST x morning
deadline for `deadline-missed` (the same `LondonClock.MorningDeadlineUtc` value is
asserted directly in V-545-1 for 2026-03-29 and 2026-10-25; the evaluator consumes it
opaquely, so a second evaluator row would repeat V-545-1); DST x D-1 scope window
(same reason); leap second/negative clock steps (`FakeTimeProvider` only advances; the
production clock is NTP-disciplined and D-9 fails closed on a future heartbeat).

**Missing setup Code must add (recorded here, not assumed):**

- New project `src/Antiphon.NightlyWatchdog/Antiphon.NightlyWatchdog.csproj` (net9.0,
  `OutputType=Exe`, `InternalsVisibleTo Include="Antiphon.Tests"`, packages
  `Microsoft.Data.Sqlite` latest 9.x with its bundled `SQLitePCLRaw.bundle_e_sqlite3`,
  `WTelegramClient` 4.4.8, `Microsoft.Extensions.Logging.Console` 9.*) added to
  `Antiphon.sln`; `tests/Antiphon.Tests/Antiphon.Tests.csproj` gains
  `<ProjectReference Include="..\..\src\Antiphon.NightlyWatchdog\Antiphon.NightlyWatchdog.csproj" />`.
  The three seams the tests need are constructor-injected interfaces: `IWindmillApi`,
  `INotificationTransport`, `IRecipientReader`, plus `TimeProvider` and an
  `Action<CrashPoint>? crashHook` on `WatchdogLoop` (production passes the `CrashAfter`
  fault hook only when `Validate()` allowed it).
- `Ledger : IDisposable`; `Dispose()` closes the connection and calls
  `SqliteConnection.ClearPool` so a restart test can move `ledger.db` alone (WAL is
  checkpointed and removed on last close). Ledger read API used by tests:
  `Outages()`, `Notifications()`, `Attempts(nid)`, `Receipts()`, `Heartbeat()` returning
  the row records named in the fixture contract.
- `WatchdogLoop.TickAsync(ct)` returns `TickReport { Transitions, Sends, Imports,
  ReaderState }` (records below). Tests never scrape logs.
- `ReceiptImporter.Import(observations)` returns one `ImportOutcome(Nid, Attempt,
  Reason, Imported)` per observation; `Reason` is one of `imported`, `duplicate`,
  `peer-unauthorized`, `unknown-notification`, `unknown-attempt`, `identity-mismatch`,
  `run-mismatch`, `body-mismatch`, `predates-attempt`, `marker-missing`; rules are
  evaluated in D-6 order 1..5 and the first failure is the reason.
- Attempt rows carry the rendered `body` and its `bodySha256` (each attempt renders its
  own text because the marker's `attempt=` changes); notification rows carry the
  logical identity (`nid`, `kind`, `outageId`, `linkedNid`, `runId`, `state`).
- Due-day assignment of job rows (the list row has no `scheduled_for`): a scheduled row
  (`schedule_path` set) belongs to due day D when its `created_at` lies in
  `[DueUtc(D) - 1 min, MorningDeadlineUtc(D))`; a completed row with a fetched result
  that carries `localDueDate` uses that date instead. Rows outside both are ignored.
  The fixture builders set `CreatedAtUtc = DueUtc(dueDay) + 1 min` unless told otherwise.
- Windows side: `Import-NightlySeams` default map gains `WatchdogSnapshot = $null`;
  `Get-NightlyWatchdogSnapshot -Url` uses the seam when set (raw JSON text, or throw),
  else `Invoke-WebRequest -UseBasicParsing -TimeoutSec 10`; freshness reasons are
  `watchdog-unreachable`, `watchdog-stale`, `watchdog-malformed`,
  `watchdog-identity-mismatch` (new: namespace or instance id), `watchdog-outage-open`.
- `tests/test-execution-policy.json` `scriptCensus` gains
  `{ "id": "test-deploy-nightly-watchdog", "path": "scripts/test-deploy-nightly-watchdog.ps1", "disposition": "unattended", "wildcard": false }`
  and `policyHash` is recomputed with `Get-NightlyPolicyHash` (`scripts/test-nightly-tests.ps1`
  verifies it).
- `NightlyScriptsTests.The_three_scripts_are_ascii_only` list gains
  `deploy-nightly-watchdog.ps1` and `test-deploy-nightly-watchdog.ps1`.

**Fixture contract: `tests/Antiphon.Tests/TestHelpers/C545World.cs`** (internal, one
file, nearest precedent `C544World`):

```
C545World : IAsyncDisposable
  static Task<C545World> CreateAsync(Action<WatchdogOptions>? configure = null, FakeRecipientChat? sharedChat = null)
  string StateDir                        // Directory.CreateTempSubdirectory("antiphon-c545")
  FakeTimeProvider Clock                 // Microsoft.Extensions.Time.Testing; starts 2026-09-18T00:40:00Z (01:40 London)
  WatchdogOptions Options                // Namespace "mc/test"; DestinationChatId "123456789"; ReaderPeer "peer-bot";
                                         // ExpectedScriptHash "h1"; WindmillWorkspace "mc"; SchedulePath/ScriptPath as D-3;
                                         // TickSeconds 600; ReceiptGraceMinutes 30; ReaderHoldExpiryMinutes 30;
                                         // RunBudgetHours 6; SnapshotBind "127.0.0.1:0" (port chosen at start); Runtime "native"
  FakeWindmillApi Windmill               // Reachable (bool), Auth (Ok|Unauthorized), Script (hash|null), Schedule (enabled|disabled|null),
                                         // WorkerLastPingUtc (DateTime?), Jobs (List<WindmillJob>), ThrowOnEverything (bool), Calls (List<string>)
  FakeRecipientChat Chat                 // Messages: List<ChatMessage(PeerId, MessageId, DateUtc, Text)>; appended ONLY by FakeTransport
  FakeTransport Transport                // Mode: Deliver | AcceptWithoutDelivery | LoseResponse | Throw | Reject(errorClass, retryAfterSeconds?)
                                         // Sends: List<SendRecord>; OnSend: Action<SendRecord>; IntentVisibleAtSend: bool? (set by OnSend default:
                                         // opens a second SqliteConnection on StateDir/ledger.db and checks a pending attempt row for the nid)
  FakeRecipientReader Reader             // Mode: Eligible | Held(reason); DateOffset: TimeSpan; PeerOverride: string?;
                                         // Transform: Func<string,string>?; ReadByRecipient: bool; Reads: int
  Ledger Ledger; WatchdogLoop Loop
  CrashPoint? CrashAt                    // Intent | SendBeforeResponse | TransportAccepted | ReaderObservation; the hook throws
                                         // WatchdogCrashException, which TickAsync lets propagate
  Task<TickReport> TickAsync()           // one WatchdogLoop.TickAsync at Clock.GetUtcNow()
  Task<TickReport> AdvanceAndTickAsync(TimeSpan by)
  Task RestartAsync(bool ledgerOnly = false)  // dispose Loop+Ledger; when ledgerOnly, move ONLY ledger.db to a fresh StateDir
                                              // (the old directory is deleted); rebuild Ledger+Loop over the same fakes and Chat
  Task<(int Status, string Body)> GetSnapshotAsync()   // real HttpClient GET http://<bind>/snapshot.json against the running SnapshotServer
  RecordingHttpHandler UseTelegramTransport(params ScriptedResponse[] responses)  // swaps FakeTransport for the real TelegramBotTransport
                                                                                   // over an HttpClient whose handler records requests
WindmillJob(Id, Kind: Queued|Running|Completed, Success: bool?, SchedulePath: string?, CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Logs: string, Result: JsonObject?)
TickReport(Transitions: IReadOnlyList<OutageTransition(Kind, OutageId, Change: Opened|Closed|Amended, Nid)>,
           Sends: IReadOnlyList<SendRecord(Nid, Attempt, Accepted, MessageId, ErrorClass, Note)>,
           Imports: IReadOnlyList<ImportOutcome(Nid, Attempt, Reason, Imported)>, ReaderState: Eligible|Held)
OutageRow(OutageId, Kind, DueDay, Epoch, OpenedAt, ClosedAt, JobId, RunId, FailureNid, RecoveryNid, EvidenceJson)
NotificationRow(Nid, Kind, OutageId, LinkedNid, RunId, State, CreatedAt, ReceivedAt)
AttemptRow(Nid, Attempt, StartedAt, Accepted, AcceptedAt, MessageId, ErrorClass, RetryAfterUtc, Body, BodySha256)
ReceiptRow(Nid, Attempt, MessageId, DateUtc, PeerHash, TextSha256, ReadObservedAt, ImportedAt)
```

Helper builders on the world: `Jobs.HopFailed(dueDay, id, logs)`, `Jobs.Failed(...)`,
`Jobs.Running(startedAt)`, `Jobs.Queued()`, `Jobs.Success(dueDay, runId, sha,
reportDelivered = true, testsPassed = true, coverageComplete = true, scheduled = true)`,
`Jobs.SuccessWithoutResult()`. `FakeTransport.Deliver` appends
`ChatMessage(Options.ReaderPeer, ++messageId, Clock.GetUtcNow(), body)` then returns
`Accepted`; `LoseResponse` appends the same message and then throws `HttpRequestException`
(the message reached Telegram, the response was lost); `AcceptWithoutDelivery` returns
`Accepted` and appends nothing; `Throw` appends nothing and throws; `Reject` returns
`Accepted=false` with the error class. `FakeRecipientReader.ReadAsync(floorUtc)` returns
`Held(reason)` in Held mode, else every chat message with `DateUtc >= floorUtc - 24h`
mapped to `RecipientObservation(PeerOverride ?? PeerId, MessageId, DateUtc + DateOffset,
Transform?.Invoke(Text) ?? Text, ReadByRecipient)`.

`RecordingHttpHandler : HttpMessageHandler` records `(Method, Uri, BodyJson)` and returns
the next `ScriptedResponse(status, bodyJson, delayMs)`; a `delayMs` above the client
timeout produces the timeout class.

### Delivery inventory

Acceptance is at the recipient. Tier-1 acceptance (`sent`, `message_id`, `acceptedAt`)
is recorded and shown but never satisfies delivery; `received` exists only when the
importer matched a reader observation against the ledger under D-6 rules 1-5. There is
no session destination in this card, so no UserPrompt evidence applies; the channel
destination requires the recipient-side readback of the whole produced body.

| ID / producer -> destination / durable identity | Persistence boundaries and recovery cuts | Observable receipt and tests |
|---|---|---|
| DL-545-A failure notification: `OutageEvaluator` transition -> `Ledger.OpenOutageWithIntent` (outage + nid + attempt 1 `pending`, one transaction) -> `INotificationTransport.SendAsync` -> `Ledger.RecordAttempt` (accepted, `message_id`) -> `IRecipientReader.ReadAsync` -> `ReceiptImporter` -> `Ledger.ImportReceipt` (`receipts` row, `state=received`). Identity `outageId = nw:{ns}:{dueDay}:{kind}:{epoch}` plus ULID `nid`, `attempt`, and the marker line in the body. | Cuts: (1) after intent commit before send; (2) after the message reached the chat before the response was recorded; (3) after `RecordAttempt` accepted before readback; (4) after the observation was returned before `ImportReceipt` committed. Enqueue failure: transport throws (nothing delivered) or rejects. Recovery: a restarted loop over the same ledger file resumes the same `nid`; reader-first (D-7) imports any attempt already in the chat before resending; never a second `nid` for the same open outage. | Busy recipient (held reader) and already-eligible recipient (answers in the same tick) both exercised. Tests: `C544_NotificationIntent` (intent durable before send; cut 1), `C544_NotificationRetry` (send throws; attempt 2 same nid), `C544_NotificationCrash` (cuts 1-4, one logical notification, one receipt), `C544_RecipientEvidence` (accepted, eligible-empty stays `sent`), `C545_ReaderFirstRetry` (cut 2 without crash), `C545_HeldReaderNoResend` (busy), `C544_IndependentOutage`/`C544_IndependentState` (Windows facts unreadable). Decisive evidence: `Chat.Messages` contains exactly the attempt body and `Receipts()` has one row `(nid, messageId)`. |
| DL-545-B recovery notification: `Ledger.CloseOutageWithRecovery` (closure + recovery nid + attempt 1, one transaction) -> same transport/reader/importer path. Identity: own `nid`, `linkedNid` = failure nid, same `outageId`. | Same cuts as A on the recovery `nid`; the failure notification's lifecycle is untouched. Sent even if the failure was never received. | `C544_RecoveryNotification` (failure received, then recovery received, `failureReceived=true`), `C545_RecoveryLinksFailure` (failure never received; recovery still delivered with `failureReceived=false`; failure stays `sent`/`pending`). |
| DL-545-C qualification notice: `Program --send-qualification-notice` -> `Ledger.OpenNoticeWithIntent` (`kind=qualification`, no outage row) -> same path. Identity: `nid`, marker `kind=qualification oid=none`. | Same cuts as A; the notice is the S6 production live proof and must be ledger-backed so the artifact can cite its `nid`, `message_id` and receipt row. | `C545_QualificationNoticePath` (one notification, one attempt, delivered, received; `Outages()` empty). |
| DL-545-D detection (the DL-4 independence rows carried from CARD-0544): `IWindmillApi` probes and job rows (list, logs, `get_result`) -> `OutageEvaluator` -> `Ledger` outage rows. Identity `outageId`; job facts carried as `jobId`/`runId` from Windmill only. | Windmill unreachable, auth failure, missing worker, hop failure and stalled/failed/missing-result jobs each open the D-3 kind; closure only from a later scheduled success with a matching result; no run is ever invented; Windows-side files are never read. Restart over the moved ledger recovers the open outage and its pending intent. | `C545_OutageKinds`, `C545_DeadlineMissedSuppressedByOpenOutage`, `C545_ClosureNeverInventsRun`, `C545_OutageIdentityEpoch`, `C544_IndependentOutage`, `C544_IndependentState`, `C545_NoWindmillDependency`. |
| DL-545-E readiness: `SnapshotServer` (`GET /snapshot.json` from the ledger) -> `Get-NightlyWatchdogSnapshot`/`Test-NightlyWatchdogFreshness` in `nightly-health.ps1` -> `last-monitor.json` (`Identity.WatchdogInstanceId`, `WatchdogHeartbeatAt`, `Health.Reasons`) -> `InterimVerificationReadinessReader` (`watchdogInstanceId` equality with the receipt). Identity `instanceId` + `heartbeatAt`. | Snapshot unreachable, stale (> 20 min), malformed, wrong namespace/instance, or with an open outage makes `Healthy=false`; the monitor file is written atomically; the reader fails closed on a missing or mismatching instance id. Recovery is the next 30-minute readiness tick. | `C545_SnapshotShape` (real HTTP GET), `Test-C545_WatchdogFresh`, `Test-C545_WatchdogStale`, `Test-C545_ReadinessRouting`, `InterimVerificationReadinessTests.C545_WatchdogInstance`. |

**Substitutes and what each cannot prove.** `FakeWindmillApi` cannot prove the real
`jobs/list`, `get_result`, `workers/list` shapes, timeouts or token scopes (the PS
adapter rows in `Test-C545_JobResultFetch` and `Test-C544_ProductionJobAdapter` prove
the production PowerShell adapter over real loopback HTTP; the watchdog's
`WindmillHttpApi` request shapes are proven by `C545_WindmillHttpApiRequestShape`
against `RecordingHttpHandler`; live registration and credentials are S6 steps 2-3).
`FakeTransport` cannot prove Telegram acceptance, `message_id` semantics or the 4096
limit (request/response mapping is proven by `C545_TelegramTransportRequestShape`; live
acceptance is S6 step 7/8). `FakeRecipientReader` and `FakeRecipientChat` cannot prove
MTProto history, peer resolution, the read marker or session revocation (mapping and
fail-closed are proven by `C545_ReaderHeldWithoutSession`; live readback is S6 step 7).
`FakeTimeProvider` cannot prove NTP or the host's zone data (`--self-check`, S6 step 1).
`RecordingHttpHandler` cannot prove TLS or DNS. The loopback `SnapshotServer` cannot
prove the tailnet bind (`-Deploy` verification, S6 step 1). The deploy harness's
injected `SshRunner`/`ScpRunner` cannot prove ssh, sudo or systemd (S6 step 1). None of
these substitutes is replaced by more mock assertions; S6 supplies the real evidence and
stays blocked on the deploy-time actions, and CARD-0544 activation stays disabled until
its receipt exists.

### Proves it works now

Layers: **W** = in-process C# against the real watchdog components (`NightlyWatchdogCoreTests`
unless stated), **N** = `NightlyVerificationContractTests` (in-process C# or pwsh wrapper),
**H** = `InterimVerificationReadinessTests`, **PS** = PowerShell harness case run directly.
Every test class below is `[Category("Integration")]`; `NightlyVerificationContractTests`
keeps `[ParallelLimiter<ProcessSpawnLimit>]`; `NightlyWatchdogCoreTests` spawns no process.

- V-545-1: London due-day arithmetic | W | `NightlyWatchdogCoreTests.C545_LondonDueDays` | `LondonClock.DueDay(utc)`: `2026-06-30T23:00:00Z -> 2026-07-01`, `2026-06-30T22:59:59Z -> 2026-06-30`, `2026-12-31T23:59:59Z -> 2026-12-31`, `2027-01-01T00:00:00Z -> 2027-01-01`; `DueUtc(day)`: `2026-03-28 -> 2026-03-28T00:30:00Z`, `2026-03-29 -> 2026-03-29T00:30:00Z`, `2026-03-30 -> 2026-03-29T23:30:00Z`, `2026-10-24 -> 2026-10-23T23:30:00Z`, `2026-10-25 -> 2026-10-24T23:30:00Z`, `2026-10-26 -> 2026-10-26T00:30:00Z`; `GraceEndUtc(day) == DueUtc(day) + 30 min` for the same six days; `MorningDeadlineUtc`: `2026-03-29 -> 07:00:00Z`, `2026-10-25 -> 08:00:00Z`, `2026-09-17 -> 07:00:00Z`; `PreviousDayInScope(utc)`: true at `2026-09-18T06:59:59Z`, false at `2026-09-18T07:00:00Z`. Zone lookup accepts `Europe/London` and falls back to `GMT Standard Time`.
- V-545-2: option validation | W | `NightlyWatchdogCoreTests.C545_OptionsRefuseWildcardBind` | rows: `0.0.0.0:17290`/native -> error `snapshot-bind-wildcard`; `[::]:17290`/native -> same; `0.0.0.0:17290`/container -> ok; `127.0.0.1:17290`/native -> ok; empty bind -> `snapshot-bind-missing`; empty namespace -> `namespace-missing`; unset `DestinationChatId` -> ok (send-time refusal, D-12); `Namespace=mc` with `AllowFaultInjection=true` -> `fault-injection-forbidden` (asserted again in V-545-11).
- V-545-3: every D-3 outage kind opens and closes from the exact probe/job shapes | W | `NightlyWatchdogCoreTests.C545_OutageKinds` (one fresh world per row; rows are named strings) | `windmill-unreachable/first-tick`: `Reachable=false`, tick 1 -> no transition; `windmill-unreachable/second-tick`: tick 2 -> `Opened nw:mc/test:2026-09-18:windmill-unreachable:1` with `JobId==null`, `RunId==null`, `Nid` non-empty; `windmill-unreachable/steady`: tick 3 -> no transition; `windmill-unreachable/close`: `Reachable=true`, tick 4 -> none, tick 5 -> `Closed` with `RecoveryNid` set. `windmill-auth-failed/*`: same four rows with `Auth=Unauthorized`. `script-missing/open`: `Script=null` -> opened on tick 1; `script-hash-drift/open`: hash `h2` vs expected `h1` -> `script-hash-drift`; `schedule-missing/open`, `schedule-disabled/open` (`enabled=false`). `desktop-worker-missing/59m`: `WorkerLastPingUtc = now - 59 min` -> none; `desktop-worker-missing/60m`: `now - 60 min` -> opened, `JobId==null`; `desktop-worker-missing/after-grace`: same at 01:40 London with no job rows -> transitions are exactly {`desktop-worker-missing`, `start-overdue`} and every outage row has `JobId==null`; `desktop-worker-missing/close`: ping present on two consecutive ticks -> closed on the second. `start-overdue/before-grace`: clock `2026-09-17T23:59:59Z`, no jobs -> none; `start-overdue/at-grace`: `2026-09-18T00:00:00Z` -> opened `nw:mc/test:2026-09-18:start-overdue:1`; `start-overdue/queued`: a `Queued` job -> still opened; `start-overdue/running`: `Running` started `2026-09-17T23:31:00Z` -> none; `start-overdue/dst-bst`: clock `2026-03-30T00:00:00Z` (01:00 BST) with no job for `2026-03-30` -> opened for due day `2026-03-30`. `run-stalled/5h59m59s`: running since `now - 5h59m59s` -> none; `run-stalled/6h`: `now - 6h` -> opened with `JobId` = the running job. `windows-hop-failed/exit-255`, `/connection-refused`, `/timed-out`, `/permission-denied`, `/no-such-identity`: completed `Success=false` with logs containing that token -> opened `windows-hop-failed`, `JobId` set, `RunId==null`; `job-failed/assertion`: logs `Assertion failed: expected 1` -> `job-failed`; `job-failed/empty-logs`: `Logs=""` -> `job-failed`. `result-missing/null-result`, `/no-run-id`, `/no-due-date`, `/wrong-due-date` (`localDueDate=2026-09-17`): completed `Success=true` -> opened `result-missing`; `result-matching/control`: result `{nativeRunId:"r18", localDueDate:"2026-09-18", sha:"s18", reportDelivered:true, testsPassed:true, coverageComplete:true}` -> no transition and `Heartbeat().LastDueDay == ("2026-09-18","j-ok","success","r18","s18")`. `report-undelivered/open`: matching result with `reportDelivered=false` -> opened `report-undelivered`. `not-outage/tests-red`, `not-outage/coverage-incomplete`: matching result with `testsPassed=false` / `coverageComplete=false` -> no transition; probe row records the flag. `deadline-missed/before`: clock `2026-09-18T06:59:59Z`, a job running since 00:31 London, no success -> none; `deadline-missed/at`: `2026-09-18T07:00:00Z` -> opened `deadline-missed`. `previous-day/closes-before-0800`: `job-failed` open for `2026-09-17`, clock `2026-09-18T00:40:00Z`, a completed scheduled success with result `localDueDate=2026-09-17` -> `Closed`; `previous-day/ignored-after-0800`: clock `2026-09-18T07:00:00Z`, a new hop failure for `2026-09-17` -> no transition for `2026-09-17`.
- V-545-4: an open due-day outage suppresses `deadline-missed` and is amended instead | W | `NightlyWatchdogCoreTests.C545_DeadlineMissedSuppressedByOpenOutage` | `windows-hop-failed` open for `2026-09-18`; clock to `2026-09-18T07:00:00Z`; tick -> `Transitions` contains `Amended` for that `outageId` and no `Opened` of kind `deadline-missed`; `Notifications().Count == 1`; `EvidenceJson` contains `"deadlineMissedAt":"2026-09-18T07:00:00Z"`; control world with no open outage -> `deadline-missed` opened with its own `nid`.
- V-545-5: closure and identity rules | W | `NightlyWatchdogCoreTests.C545_ClosureNeverInventsRun`, `NightlyWatchdogCoreTests.C545_OutageIdentityEpoch` | Closure rows on an open `job-failed` (`2026-09-18`, job `j1`): `manual-success` (no `schedule_path`, matching result) -> still open; `earlier-day-success` (scheduled, result `localDueDate=2026-09-17`) -> still open; `missing-due-date` -> still open; `failed-again` (`Success=false`) -> still open, `EvidenceJson` amended with `j2`; `scheduled-matching` (`j3`, `r18`) -> `ClosedAt` set, `EvidenceJson` names `closedByJobId=j3`, `closedByRunId=r18`, the failure notification's `RunId` unchanged, `Outages().Count == 1`, `Heartbeat().LastDueDay.NativeRunId == "r18"`; `later-day-success` (`localDueDate=2026-09-19`) -> closed. Epoch rows: open hop -> `:1`; tick again while open -> no new row, same `FailureNid`; close via `scheduled-matching`; hop again the same day -> `Opened nw:mc/test:2026-09-18:windows-hop-failed:2` with a different `nid`; `:1` remains closed; `Outages().Count == 2`.
- V-545-6: body render/parse round trip | W | `NightlyWatchdogCoreTests.C545_BodyHashRoundTrip` | `NotificationBody.RenderFailure` with fixed inputs (`kind=windows-hop-failed`, due `2026-09-18`, workspace `mc`, schedule `u/lndcobra/antiphon_nightly_tests`, job `01a0aaaa-0000-4000-8000-000000000001`, evidence `ssh exit 255 at 2026-09-18T00:31:07Z`, outage `nw:mc:2026-09-18:windows-hop-failed:1`, nid `01J8Z0000000000000000000A1`, attempt 1, sha `none`, run `none`, policy `3f9c0f2a`) equals the seven-line D-5 text exactly with `h=` = first 16 lowercase hex of SHA-256 over the six lines above joined by `\n`; `ParseMarker(text)` returns `nid/oid/kind/attempt/due/h` and the header fields `job/run/sha/policy`; `Sha256Hex(text)` equals the attempt's `BodySha256`; changing one character in line 3 changes both `BodySha256` and the recomputed `h` and `ParseMarker` reports `HashMismatch`; recovery body starts `Antiphon nightly watchdog: RECOVERED windows-hop-failed` and its marker carries `kind=recovery link=<failure nid> failureReceived=false`; qualification body marker carries `kind=qualification oid=none`; every body is ASCII, has no trailing whitespace or newline, and is under 4096 characters.
- V-545-7: intent transaction atomicity and durability | W | `NightlyWatchdogCoreTests.C545_IntentBeforeNetwork` | `Ledger.OpenOutageWithIntent` with an injected `nidFactory` that throws -> `Outages()`, `Notifications()`, `Attempts()` all empty (rolled back); normal call -> exactly one outage, one notification `state=pending`, one attempt `Accepted=false`; a second `Ledger` opened over the same file (first still open) sees the pending attempt; `Ledger.PendingNotifications()` returns it.
- V-545-8: receipt import idempotency | W | `NightlyWatchdogCoreTests.C545_ReceiptIdempotent` | `ImportReceipt(nid, 1, messageId 7, ...)` twice -> `Receipts().Count == 1`, second result `duplicate`, `ReceivedAt` unchanged; `ImportReceipt(nid, 2, messageId 9, ...)` -> second receipt row, state unchanged `received`.
- V-545-9: ledger namespace binding | W | `NightlyWatchdogCoreTests.C545_LedgerNamespaceBound` | first open with `mc/test` stores it in `heartbeat.namespace`; reopening the same file with `mc/qual` throws `LedgerNamespaceMismatchException`; `Validate()` with `Namespace=mc/qual` and `StateDir` resolving to `<workingDirectory>/state` -> `state-dir-shared-with-production`; `Namespace=mc/qual`, `StateDir=./state-qual` -> ok.
- V-545-10: reader-first retry, backoff, grace resend | N (in-process) | `NightlyVerificationContractTests.C545_ReaderFirstRetry` | row `lost-response/reader-sees-it`: `Transport.Mode=LoseResponse`, tick 1 -> attempt 1 `Accepted=false`, `Chat.Messages.Count == 1`; advance 2 min, `Reader.Mode=Eligible`, tick 2 -> `Imports` has `imported` for attempt 1, `Sends` empty, `Attempts(nid).Count == 1`, `Chat.Messages.Count == 1`, `State == received`. Row `lost-response/reader-empty`: `Transport.Mode=Throw`, tick 1; advance 1:59 -> tick: no send (backoff 2 min not reached); advance 0:01 -> tick: attempt 2 same `nid`, marker `attempt=2`, `Notifications().Count == 1`; `Transport.Mode=Deliver` before that tick so the recipient gets attempt 2 -> next tick imports attempt 2. Row `backoff-schedule`: `Transport.Mode=Throw` throughout; sends occur at +2, +7, +17, +47, +107 min and hourly after, never earlier. Row `retry-after`: `Reject("rate-limited", retryAfterSeconds 600)` -> next send not before +10 min. Row `accepted-unreceived/grace`: `AcceptWithoutDelivery`, tick 1 -> `sent`; advance 29:59 -> tick: no resend; advance 0:01 -> tick with eligible empty reader: attempt 2 sent, `ErrorClass` of attempt 1 unchanged, mismatch logged in `TickReport.Sends[0].Note == "accepted-but-unreceived"`.
- V-545-11: production namespace refuses faults; qualification allows | N (in-process) | `NightlyVerificationContractTests.C545_ProductionRefusesFaultInjection` | `Namespace=mc, AllowFaultInjection=true` -> `Validate()` error `fault-injection-forbidden`; `Namespace=mc, CrashAfter="intent"` -> same; `Namespace=mc/qual, AllowFaultInjection=true, CrashAfter="intent"` -> ok and a loop built with that config throws `WatchdogCrashException` at the intent cut; `Namespace=mc` loop built with no fault hook completes a tick that opens an outage.
- V-545-12: tier-1 fields recorded and shown separately; read marker recorded, never gating | N (in-process) | `NightlyVerificationContractTests.C545_AcceptanceRecordedSeparately` | row `accepted-shown`: `AcceptWithoutDelivery` -> attempt `Accepted=true`, `AcceptedAt == Clock.GetUtcNow()`, `MessageId == "1"`, snapshot `recentNotifications[0].state == "sent"`, `acceptedAt` and `messageId` present, `receivedAt == null`; row `received-then-read`: `Deliver`, `ReadByRecipient=false` -> received with `readObservedAt == null`; next tick `ReadByRecipient=true` -> `ReadObservedAt` set, state still `received`, `Receipts().Count == 1`; row `read-marker-without-body`: `Deliver` with `Reader.Transform = t => t[(t.LastIndexOf('\n') + 1)..]` (the marker line only) and `ReadByRecipient=true` -> `Imports[0].Reason == "body-mismatch"`, state `sent`, `Receipts()` empty.
- V-545-13: snapshot endpoint | W | `NightlyWatchdogCoreTests.C545_SnapshotShape` | real HTTP GET -> 200, `application/json`, top-level keys exactly {`schemaVersion`, `instanceId`, `version`, `configHash`, `namespace`, `heartbeatAt`, `tickSeconds`, `windmill`, `desktopWorker`, `schedule`, `reader`, `destination`, `openOutages`, `recentNotifications`, `lastDueDay`, `truncated`} (the D-9 shape plus `destination` and `truncated`, added here); `schemaVersion == 1`; `namespace == "mc/test"`; `destination == { "qualified": true, "hash": sha256("123456789")[..16] }`; after one hop outage and one accepted attempt: `openOutages[0].outageId`, `failureNid`, `failureState == "sent"`; `recentNotifications[0]` carries `nid`, `kind`, `state`, `attempts`, `acceptedAt`, `messageId`, `receivedAt`, `readObservedAt`; the body text contains none of the bot token, the Windmill token, the raw chat id `123456789`, or the reader api hash; with 500 notifications inserted through `Ledger.OpenNoticeWithIntent` the response is `<= 65536` bytes and `truncated == true`; `GET /other` -> 404; `POST /snapshot.json` -> 405.
- V-545-14: transport request shape and error classes | W | `NightlyWatchdogCoreTests.C545_TelegramTransportRequestShape` | `TelegramBotTransport` over `RecordingHttpHandler`: request `POST https://api.telegram.org/bot<token>/sendMessage`, JSON body exactly {`chat_id`: "123456789", `text`: body, `disable_web_page_preview`: true} (no `parse_mode`); responses: `200 {"ok":true,"result":{"message_id":42}}` -> `Accepted`, `MessageId=="42"`; `200 {"ok":false}` -> `api-error`; `401` -> `bad-token`; `400 {"description":"Bad Request: chat not found"}` -> `chat-not-found`; `403 {"description":"Forbidden: bot was blocked by the user"}` -> `blocked`; `429 {"parameters":{"retry_after":7}}` -> `rate-limited`, `RetryAfterSeconds==7`; `503` -> `transport`; handler delay beyond the 15 s client timeout (use a 200 ms timeout in the test) -> `transport`; for every error row `TransportResult.Error` and any thrown exception message do not contain the token.
- V-545-15: Windmill HTTP client request shapes | W | `NightlyWatchdogCoreTests.C545_WindmillHttpApiRequestShape` | `WindmillHttpApi` over `RecordingHttpHandler`: `GET /api/version` (no auth header), `GET /api/users/whoami`, `GET /api/w/mc/scripts/get/p/u/lndcobra/antiphon_nightly_tests`, `GET /api/w/mc/schedules/get/u/lndcobra/antiphon_nightly_tests`, `GET /api/workers/list?ping_since=900`, `GET /api/w/mc/jobs/list?script_path_exact=u%2Flndcobra%2Fantiphon_nightly_tests&per_page=20`, `GET /api/w/mc/jobs_u/completed/get_result/<id>`, `GET /api/w/mc/jobs_u/get_logs/<id>`; every authenticated call carries exactly one `Authorization: Bearer <token>`; a `CompletedJob` list row without `result` maps to `Result==null` until `get_result` is fetched; connect failure, 5xx and timeout classify as `unreachable`; 401/403 as `unauthorized`.
- V-545-16: reader fails closed without a session | W | `NightlyWatchdogCoreTests.C545_ReaderHeldWithoutSession` | `TelegramUserReader` with `SessionPath` absent -> `ReadAsync` returns `Held("session-missing")` without constructing a client; `ApiId=0` -> `Held("reader-unconfigured")`; `RecipientObservation.From(message)` mapping: `peer_id` -> `PeerId`, `id` -> `MessageId`, `date` -> UTC, `message` -> `Text`, `read_inbox_max_id >= id` -> `ReadByRecipient`.
- V-545-17: wrapper result line | PS via N wrapper | `NightlyVerificationContractTests.C545_ResultLine` -> `scripts/test-nightly-run.ps1 -Case C545_ResultLine` | rows (each runs `nightly-run.ps1` as a child `pwsh -File` with stdout captured): `green`: seams write `<runDir>\summary.json` `{coverageComplete:true,testsPassed:true,reportDelivered:true,policyHash:"p"}` and return exit 0 for tests and report, `Trigger=scheduled`, `Ref=master` -> last stdout line parses as JSON with `exitCode=0`, `nativeRunId`=the run id, `localDueDate="2026-09-18"` (seam `UtcNow=2026-09-17T23:35:00Z`), `testsPassed=true`, `coverageComplete=true`, `reportDelivered=true`, `trigger="scheduled"`, `summaryPath` set; `last-run.json` and `last-complete-green.json` both contain `localDueDate=2026-09-18` and the flags equal the line; `refusal`: `-CheckoutRoot C:\src\Antiphon` -> exit 3 and the last line is `{"nativeRunId":"","sha":"","ref":"","trigger":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":3,"summaryPath":""}` (key order fixed); `test-red`: tests seam exit 1 -> `exitCode=1`, `testsPassed=false`, line flags equal `last-run.json`; `no-report`: `-NoReport` -> `reportDelivered=false`, no green file; `dst-summer`: seam `UtcNow=2026-06-30T23:10:00Z` -> `localDueDate="2026-07-01"`; `dst-autumn`: `2026-10-24T23:40:00Z` -> `2026-10-25`; `dst-spring`: `2026-03-29T00:40:00Z` -> `2026-03-29`; in every row the line is the final stdout line and no line follows it. PASS row names the PCs reference: `C545 ResultLine refusal last line is the JSON record`, `C545 ResultLine dst-summer localDueDate 2026-07-01`.
- V-545-18: job-result fetch in the production adapter | PS via N wrapper | `NightlyVerificationContractTests.C545_JobResultFetch` -> `scripts/test-nightly-health.ps1 -Case C545_JobResultFetch` | route-table stub; seven `CompletedJob` rows `c1..c7` with `schedule_path`, listed in that order, none carrying `result`; routes `get_result/c1,c2,c4,c5` -> `{nativeRunId:"r18",sha:"s",localDueDate:"2026-09-18"}`, `get_result/c3` -> HTTP 500, `get_result/c6`,`c7` -> would return a valid result but must never be requested -> stub log has exactly 6 requests in order (list, c1..c5); statuses `c1,c2,c4,c5 == success`, `c3 == unknown`, `c6,c7 == unknown`; `c1.scheduledFor == 2026-09-17T23:30:00Z`; a row whose result is JSON `null` -> `unknown`; a row whose result lacks `localDueDate` -> `unknown`; `Test-NightlyMonitorHealth` with `c3` alone is unready; row `no-sink`: `(New-NightlyProductionWindmillApi ...).Keys` does not contain `EnqueueNotification` and `Get-NightlyWindmillConfig` output has no `NotifyPath` property. PASS row names the PCs reference: `C545 JobResultFetch c6 stays unknown beyond the cap`, `C545 JobResultFetch c3 unfetchable is unknown`, `C545 JobResultFetch no production Windmill notification sink`.
- V-545-19: watchdog freshness in the Windows evaluator | PS via N wrappers | `NightlyVerificationContractTests.C545_WatchdogFresh`, `C545_WatchdogStale` -> `scripts/test-nightly-health.ps1 -Case C545_WatchdogFresh` / `-Case C545_WatchdogStale` | Fresh rows (`Test-NightlyWatchdogFreshness -Snapshot -NowUtc -ExpectedNamespace mc -ExpectedInstanceId wd-1`): age 0, 19:59, 20:00 -> `Fresh=$true`, no reasons; real-HTTP row: `Get-NightlyWatchdogSnapshot -Url http://127.0.0.1:<stub>/snapshot.json` through `Start-C544WindmillStub` returns the parsed object and the stub log shows `GET /snapshot.json`; integration row: `Invoke-AntiphonNightlyHealth` with seam `WatchdogSnapshot` fresh and an otherwise-green fixture -> `Health.Healthy=$true`, `Identity.WatchdogInstanceId == 'wd-1'`, `Identity.WatchdogHeartbeatAt` equals the snapshot's `heartbeatAt`; `-ReadinessConfigPath` row: `readiness-config.json` `{expectedScriptHash,expectedPolicyHash,watchdogSnapshotUrl,watchdogInstanceId,repositoryPath,projectId}` supplies every value. Stale rows: age 20:01 -> reason `watchdog-stale`; age -1 s (future) -> `watchdog-malformed`; seam throws / closed port -> `watchdog-unreachable`; not JSON, missing `heartbeatAt`, `schemaVersion=2`, empty `instanceId` -> `watchdog-malformed`; `namespace=mc/qual` -> `watchdog-identity-mismatch`; `instanceId=wd-2` -> `watchdog-identity-mismatch`; `openOutages` non-empty -> `watchdog-outage-open`; no `watchdogSnapshotUrl` configured -> `watchdog-unreachable`; each stale row through `Invoke-AntiphonNightlyHealth` gives `Health.Healthy=$false` with that reason in `Health.Reasons` and the monitor file still written. PASS row names the PCs reference: `C545 WatchdogStale age 20:01 stale`, `C545 WatchdogStale unreachable`, `C545 WatchdogStale malformed not-json` (and `missing-heartbeat`, `schema-2`, `empty-instance`), `C545 WatchdogStale namespace mc/qual mismatch`, `C545 WatchdogStale instance wd-2 mismatch`, `C545 WatchdogStale open outage unhealthy`.
- V-545-20: readiness definition routing and the retired health definition | PS via N wrapper | `NightlyVerificationContractTests.C545_ReadinessRouting` -> `scripts/test-nightly-health.ps1 -Case C545_ReadinessRouting` | `scripts/windmill/antiphon-nightly-readiness.json` exists with `tag == 'desktop'`, `language == 'bash'`, content invoking `scripts\nightly-health.ps1` with `-ReadinessConfigPath C:\Antiphon\nightly\readiness-config.json` and no host literal other than `host.docker.internal`; its schedule file has `schedule == '0 */30 * * * *'`, `timezone == 'Europe/London'`, `enabled == $true`, `args == @{}`; `antiphon-nightly-health.json` and `.schedule.json` are absent; `Test-NightlyMonitorRouting -Definition @{ tag = 'desktop' }` still throws (G-116 preserved); `README.md` names the readiness definition and not the health definition.
- V-545-21: readiness reader binds the watchdog instance | H | `InterimVerificationReadinessTests.C545_WatchdogInstance` | rows on the fixture (receipt `watchdogInstanceId="wd-1"`, monitor `Identity.WatchdogInstanceId="wd-1"`): `match` -> `(true, "ready")` and `Snapshot.WatchdogInstanceId == "wd-1"`; `receipt-missing` (field removed) -> `qualification_watchdog_missing`; `receipt-blank` (`" "`) -> `qualification_watchdog_missing`; `monitor-missing` -> `monitor_watchdog_mismatch`; `monitor-blank` -> `monitor_watchdog_mismatch`; `mismatch` (`wd-2`) -> `monitor_watchdog_mismatch`; `case-differs` (`WD-1`) -> `monitor_watchdog_mismatch` (ordinal); `stale-and-mismatch` (`RecordedAt` 61 min old and `wd-2`) -> `monitor_stale` (age precedes identity, as for the existing identity checks).
- V-545-22: deploy script refusal, rendering and asset hygiene | PS via N wrappers | `NightlyVerificationContractTests.C545_DeployRefusesWithoutProfile`, `C545_DeployRendersFromProfile`, `C545_HostAgnosticAssets` -> `scripts/test-deploy-nightly-watchdog.ps1 -Case <name>` (new harness; dot-sources `scripts/lib/nightly-common.ps1`, `scripts/lib/c487-harness.ps1` and `scripts/deploy-nightly-watchdog.ps1`; the deploy script keeps the `InvocationName -eq '.'` guard and exposes `Invoke-NightlyWatchdogDeployment -Profile -Deploy -SshRunner -ScpRunner -PublishRunner -HttpRunner -Now`) | `Test-C545_DeployRefusesWithoutProfile` rows: `no-profile/no-arg` (no `-Profile`, `ANTIPHON_WATCHDOG_DEPLOY_PROFILE` cleared, default path redirected to a missing temp file via `-DefaultProfilePath`) -> exit 3, message `no deploy profile`, zero runner calls; `no-profile/env-missing-file` -> exit 3; `placeholder/sshTarget` (`"<user>@<host>"`) -> exit 3, message names `sshTarget`; `placeholder/remoteRoot` -> exit 3; `loopback-bind` (`snapshotBind=127.0.0.1:17290`) -> exit 3 `snapshotBind must be a private non-loopback address`; `wildcard-bind` native -> exit 3; `control` (valid fictitious profile `ops@watchdog-host.example`, `/home/ops/antiphon-watchdog`, `10.0.0.5:17290`) -> preflight exit 0. `Test-C545_DeployRendersFromProfile` rows: rendered unit has no `@@` token, `User=ops`, `WorkingDirectory=/home/ops/antiphon-watchdog`, `EnvironmentFile=/home/ops/antiphon-watchdog/env`, `ReadWritePaths=/home/ops/antiphon-watchdog/state /home/ops/antiphon-watchdog/state-qual`; rendered `qual.json` has no `<`/`>`; `preflight-no-upload`: without `-Deploy` the fake `SshRunner`/`ScpRunner` recorded no `scp`, no `systemctl`, no `sudo`, and the only ssh command is the `--self-check` dry run; `deploy-env-keys`: with `-Deploy` and a fake remote env file containing `ANTIPHON_WATCHDOG_TELEGRAM_BOT_TOKEN=never-print-this` and `ANTIPHON_WATCHDOG_SNAPSHOT_BIND=10.0.0.5:17290`, the remote env fragment the script writes contains exactly `ANTIPHON_WATCHDOG_STATE_DIR` and `ANTIPHON_WATCHDOG_NAMESPACE` (bind already present, untouched), the existing lines survive byte-for-byte, and the harness stdout does not contain `never-print-this`; `deploy-verifies-snapshot`: `-Deploy` ends with a `GET <snapshotUrl>` through `HttpRunner`. `Test-C545_HostAgnosticAssets`: scans `scripts/nightly-watchdog/**`, `scripts/deploy-nightly-watchdog.ps1`, `scripts/test-deploy-nightly-watchdog.ps1`, `docs/nightly-watchdog.md`, `scripts/windmill/antiphon-nightly-readiness*.json` for IPv4 literals other than `127.0.0.1`/`0.0.0.0`, for `[A-Za-z0-9_.-]+@[A-Za-z0-9_.-]+` SSH-target tokens other than the `<user>@<host>` placeholder and `noreply@anthropic.com`, and for `/home/` paths other than `/home/<user>`; a separate harness case `Test-C545_DeployManifest` (full-harness run only, no wrapper) checks the five tracked assets exist and are ASCII-only. PASS row names the PCs reference: `C545 DeployRefusesWithoutProfile no-profile/no-arg exit 3 no runner call`, `C545 DeployRefusesWithoutProfile placeholder/sshTarget exit 3`, `C545 DeployRendersFromProfile preflight-no-upload`, `C545 DeployRendersFromProfile env fragment has exactly the absent non-secret keys`, `C545 HostAgnosticAssets no ssh-target token`.
- V-545-23: the 13 carried controls (bodies in the guard inventory notes) | N (in-process) | `NightlyVerificationContractTests.C544_IndependentOutage`, `C544_NotificationIntent`, `C544_NotificationRetry`, `C544_RecipientEvidence`, `C544_ReceiptNotificationIdentity`, `C544_ReceiptRunIdentity`, `C544_NotificationCrash`, `C544_RecoveryNotification`, `C544_AuthorizedDestination`, `C544_ReceiptDestination`, `C544_ReceiptWholeBody`, `C544_ReceiptAttemptFloor`, `C544_IndependentState` | each green with its decisive assertion (PC table).
- V-545-24: delivery survives Windmill loss | N (in-process) | `NightlyVerificationContractTests.C545_NoWindmillDependency` | tick 1 opens a hop outage with `Transport.Mode=Throw` (attempt 1 failed); then `Windmill.ThrowOnEverything=true`, `Transport.Mode=Deliver`, advance 2 min, tick 2 -> `Sends[0].Accepted`, `Chat.Messages.Count == 1`; tick 3 -> imported, `received`; the unit template text contains none of `docker`, `windmill`, `Requires=`, `BindsTo=`, `After=docker`.
- V-545-25: held reader semantics | N (in-process) | `NightlyVerificationContractTests.C545_HeldReaderNoResend` | `Deliver` then `Reader.Mode=Held("session-locked")`: tick 1 -> `ReaderState==Held`, attempt 1 `sent`; advance 29:59, tick -> no resend, `Attempts(nid).Count == 1`, `TickReport.ReaderState==Held`; row `held-then-eligible`: `Reader.Mode=Eligible` -> next tick imports attempt 1, `Receipts().Count == 1`, `Chat.Messages.Count == 1`; row `hold-expiry`: reader held for 30:00 with `AcceptWithoutDelivery` -> the tick at 30:00 resends (rule 3 applies after expiry); row `held-lost-response`: `LoseResponse` + held -> no resend while held; eligible -> imported, one message.
- V-545-26: recovery when the failure was never received | N (in-process) | `NightlyVerificationContractTests.C545_RecoveryLinksFailure` | hop outage, `AcceptWithoutDelivery` (failure `sent`, never received); scheduled matching success -> `Closed`; `Notifications()` has a `kind=recovery` row with `LinkedNid` = failure nid and `OutageId` equal; `Transport.Mode=Deliver` -> recovery attempt delivered; body marker `kind=recovery link=<failure nid> failureReceived=false`; next tick -> recovery `received`, failure still `sent`, `Receipts().Count == 1`.
- V-545-27: qualification notice path | N (in-process) | `NightlyVerificationContractTests.C545_QualificationNoticePath` | `Loop.SendQualificationNoticeAsync()` -> `Notifications().Count == 1` with `kind=qualification`, `OutageId==null`, attempt 1 delivered, marker `kind=qualification oid=none nid=<nid>`; next tick imports -> `received`; `Outages()` empty; snapshot `recentNotifications[0].kind == "qualification"`.

### Guards the regression

- R-545-1: CARD-0544 harness cases stay green | `scripts/test-nightly-health.ps1` full run and the ten existing `NightlyVerificationContractTests.C544_*` wrappers; `C544_ProductionJobAdapter` expects 22 rows (new row `C544 ProductionJobAdapter get_result requests bounded to scheduled completed rows`: stub log = list + `j-success`, `j-failed`, `j-missing-result`, `j-string-bool`, nothing for `j-manual`/`j-unknown`) and `status j-manual` now expects `unknown`; `$script:C544ExpectedRows` and the full-run `ExpectedRows` recomputed and recorded in the Code report.
- R-545-2: CARD-0487 harness G-099..G-126 unchanged | `scripts/test-nightly-health.ps1` full run; `Test-C487_G126` still records `unauthorized-destination` with `$null` notification now that the production sink is gone.
- R-545-3: the twelve `InterimVerificationReadinessTests.C544_*` methods stay green after the reader gains the `watchdogInstanceId` requirement | full class run; decisive: `C544_QualificationReceipt` row `unsupported-schema` still `qualification_receipt_schema_unsupported` and `C544_DisabledByDefault` control still `ready`.
- R-545-4: positional `InterimReadinessSnapshot` constructions compile and behave | `VerificationRoundBriefTests` full class; `C544World.ControlledInterimReadiness.ReadySnapshot` used by the classes CARD-0544 lists (run them by explicit class names from `grep -l "C544World" tests/Antiphon.Tests/**/*.cs`, combined with the CARD-0403 OR syntax; a bare `*C544*` class wildcard is not assumed to be accepted).
- R-545-5: nightly wrapper harness stays green with the result line | `scripts/test-nightly-run.ps1` full run (`ExpectedRows` raised by the `C545_ResultLine` rows); decisive: `G001 refuse C:\src\Antiphon` still exit 3 and the sentinel untouched.
- R-545-6: ASCII and no-mutation pins | `NightlyScriptsTests` full class with the two new scripts in the ASCII list; `Nightly_run_script_names_the_isolated_clone_and_origin_master` unchanged.
- R-545-7: policy hash and census | `scripts/test-nightly-tests.ps1` full run after the census row is added and `policyHash` recomputed; decisive: the policy-hash mismatch case still refuses and the recomputed hash is accepted.
- R-545-8: monitor routing guard | `Test-C487_G116` unchanged, asserted again inside `Test-C545_ReadinessRouting`.
- R-545-9: readiness reader existing reason precedence | `C545_WatchdogInstance` row `stale-and-mismatch` (monitor stale and instance mismatch) -> `monitor_stale` (age checks precede identity checks, matching the existing order).

### Guard inventory

Carried (IDs, guard text and method names verbatim from CARD-0544; production entrypoint per the plan's carried table):

| ID | Guard | Test method | PC |
|---|---|---|---|
| G-78 | D-6: Windows/SSH outage must be detected outside that failure domain | `NightlyVerificationContractTests.C544_IndependentOutage` | PC-78 |
| G-79 | D-6/D-7: Nightly failure intent persists before notification enqueue | `NightlyVerificationContractTests.C544_NotificationIntent` | PC-79 |
| G-80 | D-6: Enqueue failure stays retryable with the original identity | `NightlyVerificationContractTests.C544_NotificationRetry` | PC-80 |
| G-81 | D-6: Transport/job acceptance is not recipient receipt | `NightlyVerificationContractTests.C544_RecipientEvidence` | PC-81 |
| G-82 | D-6: Receipt matches notification identity | `NightlyVerificationContractTests.C544_ReceiptNotificationIdentity` | PC-82 |
| G-83 | D-6: Receipt matches run identity | `NightlyVerificationContractTests.C544_ReceiptRunIdentity` | PC-83 |
| G-84 | D-6: Crash after accepted enqueue or recipient observation recovers without duplicate notification | `NightlyVerificationContractTests.C544_NotificationCrash` | PC-84 |
| G-85 | D-6: Recovery notification is produced and received after an outage clears | `NightlyVerificationContractTests.C544_RecoveryNotification` | PC-85 |
| G-86 | D-6: Unauthorized/missing destination cannot be silently replaced | `NightlyVerificationContractTests.C544_AuthorizedDestination` | PC-86 |
| G-95 | D-6: Recipient evidence must come from the authorized destination readback | `NightlyVerificationContractTests.C544_ReceiptDestination` | PC-95 |
| G-96 | D-6: Recipient readback must contain the whole produced payload | `NightlyVerificationContractTests.C544_ReceiptWholeBody` | PC-96 |
| G-97 | D-6: Recipient evidence predating the current notification attempt cannot confirm it | `NightlyVerificationContractTests.C544_ReceiptAttemptFloor` | PC-97 |
| G-98 | D-6: Independent outage recovery state must survive Windows being inaccessible | `NightlyVerificationContractTests.C544_IndependentState` | PC-98 |

Carried-method bodies (all in-process on `C545World`; each is one `[Test]`):

- `C544_IndependentOutage`: `Windmill.WorkerLastPingUtc = now - 61 min` (the Windows host is unreachable to Windmill), jobs = one completed scheduled `Success=false` for `2026-09-18` with logs `ssh: connect to host ... port 22: Connection timed out\r\nexit 255`; `Transport.Mode=Deliver`, `Reader.Mode=Eligible`. Tick 1 -> `Transitions` contains `Opened windows-hop-failed` (and `desktop-worker-missing`), `Sends` has the hop failure attempt accepted, `Chat.Messages` contains a message whose text equals `Attempts(hopNid)[0].Body`. Tick 2 -> `Imports` has `imported` for `hopNid`; `Notifications(hopNid).State == received`. `Windmill.Calls` contains only `IWindmillApi` members, and `StateDir` contains only `ledger.db` (plus WAL/SHM): the watchdog touched no Windows-side file. Decisive: `Outages().Single(o => o.Kind == "windows-hop-failed").FailureNid` is `received`.
- `C544_NotificationIntent`: `Transport.OnSend` (default) checks the ledger from a second connection at send time. Tick 1 with `Deliver` -> `Transport.IntentVisibleAtSend == true` (decisive). Row `crash-after-intent`: fresh world, `CrashAt=Intent`, tick 1 throws `WatchdogCrashException`; `Chat.Messages` empty; `Notifications().Single().State == pending`; `RestartAsync()`; tick -> the same `nid` is sent (`Sends[0].Nid == nid`), delivered, next tick received; `Notifications().Count == 1`.
- `C544_NotificationRetry`: `Transport.Mode=Throw`; tick 1 -> attempt 1 `Accepted=false`, `ErrorClass=="transport"`, state `pending`, `Chat.Messages` empty; `Transport.Mode=Deliver`; advance 2 min; tick 2 -> `Sends[0]` is `(nid, attempt 2)`, `Attempts(nid).Count == 2`, `Notifications().Count == 1`, `Chat.Messages.Count == 1` with marker `attempt=2`; tick 3 -> `received` via attempt 2; `Receipts().Single().Attempt == 2`. Decisive: `Attempts(nid).Count == 2` at tick 2.
- `C544_RecipientEvidence`: `Transport.Mode=AcceptWithoutDelivery`, `Reader.Mode=Eligible` (already eligible, view empty). Tick 1 -> attempt 1 `Accepted=true`, `MessageId=="1"`, `Notifications().Single().State == sent` (decisive), `Receipts()` empty; snapshot `recentNotifications[0].state == "sent"`, `receivedAt == null`; tick 2 (still empty view) -> still `sent`, `Imports` empty.
- `C544_ReceiptNotificationIdentity`: world A delivers failure `N1` for outage `O` (`Deliver`, eligible) and receives it. World B = `CreateAsync(sharedChat: A.Chat)` over a fresh state dir with `Transport.Mode=Throw`: tick 1 opens the same outage identity `O` (`nw:mc/test:2026-09-18:windows-hop-failed:1`) with a new nid `N2`; the reader (eligible) returns `N1`'s message. Decisive: `B.TickReport.Imports.Single(i => i.Nid == "N1").Reason == "unknown-notification"` and `B.Notifications(N2).State == pending`, `B.Receipts()` empty. Second row: attempt mismatch: `Reader.Transform` rewrites `attempt=1` to `attempt=2` in the marker of a delivered message -> `unknown-attempt`.
- `C544_ReceiptRunIdentity`: `report-undelivered` outage whose result names run `r18` (body line `run r18`); `Deliver`; `Reader.Transform = t => t.Replace("run r18", "run r99")` -> tick 2 `Imports[0].Reason == "run-mismatch"` (decisive), state `sent`; second row `Reader.Transform` rewriting `oid=` in the marker to the `:2` epoch -> `identity-mismatch`; third row rewriting `due=` -> `identity-mismatch`; control row without transform -> `imported`.
- `C544_NotificationCrash`: four rows, fresh world each, `Deliver`, eligible: `CrashAt=Intent`, `SendBeforeResponse` (the message is in the chat, `RecordAttempt` not reached), `TransportAccepted` (attempt accepted, reader not called), `ReaderObservation` (observation returned, `ImportReceipt` not committed). Each: tick 1 throws; `RestartAsync()`; tick(s) until `received` (at most two, advancing 2 min between). Decisive per row: `Notifications().Count == 1`, `Receipts().Count == 1`, `Chat.Messages.Count == 1`, `Attempts(nid).Count == 1` (Intent row: the single attempt is sent after restart; SendBeforeResponse row: attempt 1 is imported without a resend).
- `C544_RecoveryNotification`: failure delivered and received; then a scheduled matching success for `2026-09-18` -> `Closed`; `Notifications()` has `kind=recovery` with `LinkedNid == failureNid`, `OutageId` equal, own `nid`; recovery delivered (`Chat.Messages.Count == 2`), body starts `Antiphon nightly watchdog: RECOVERED windows-hop-failed` with `failureReceived=true`; next tick -> recovery `received`; failure's receipt row unchanged. Decisive: `Notifications().Single(n => n.Kind == "recovery").State == received`.
- `C544_AuthorizedDestination`: `world.UseTelegramTransport()` (real `TelegramBotTransport` over `RecordingHttpHandler`). Rows: `unset` (`DestinationChatId=null`): tick opens the outage, `Sends[0].ErrorClass == "destination-unauthorized"`, `handler.Requests.Count == 0` (decisive), attempt row `Accepted=false`, state `pending`, snapshot `destination.qualified == false`; `non-numeric` (`"abc"`): same; `mismatch` (ledger `heartbeat.destinationHash` written for `123456789` on a previous run, then options `987654321`): same with `Error` mentioning `mismatch`; `control` (`123456789`, handler scripted `200 ok`): one request whose body has `chat_id == "123456789"`.
- `C544_ReceiptDestination`: `Deliver`; `Reader.PeerOverride = "peer-other"` -> tick 2 `Imports[0].Reason == "peer-unauthorized"` (decisive), state `sent`, `Receipts()` empty; then `PeerOverride=null` -> imported.
- `C544_ReceiptWholeBody`: `Deliver`; rows: `marker-only` (`Transform` keeps only the marker line) -> `body-mismatch` (decisive); `truncated` (drops the last 10 characters of the marker line, so `h=` is short) -> `marker-missing` or `body-mismatch` (either is not imported; assert `Imported == false` and `Reason != "imported"`); `header-edited` (one character changed in line 2, marker intact) -> `body-mismatch`; control -> `imported`.
- `C544_ReceiptAttemptFloor`: `Deliver`; `Reader.DateOffset = -121 s` -> tick 2 `Imports[0].Reason == "predates-attempt"` (decisive); `DateOffset = -120 s` -> `imported` (boundary inclusive).
- `C544_IndependentState`: as `C544_IndependentOutage` but `Transport.Mode=Throw` on tick 1 (intent persisted, nothing delivered); `RestartAsync(ledgerOnly: true)` (only `ledger.db` survives in a fresh directory; the Windows facts are still unreachable: worker missing, hop job listed); `Transport.Mode=Deliver`; advance 2 min; tick -> `Sends[0].Nid` equals the original `nid` (decisive), delivered; next tick -> `received`; `Notifications().Count == 1`.

New guards:

| ID | Plan reference and guard | Test / decisive row | PC |
|---|---|---|---|
| G-545-1 | D-1/D-5: notification delivery and readback proceed while Windmill is unreachable; the unit declares no Windmill or Docker dependency | `C545_NoWindmillDependency` | PC-545-1 |
| G-545-2 | D-3: `windmill-unreachable`/`windmill-auth-failed` open only after the second consecutive failed tick, with London-date identity and no job/run | `C545_OutageKinds` rows `windmill-unreachable/second-tick`, `windmill-auth-failed/second-tick` | PC-545-2 |
| G-545-3 | D-9: heartbeat age 20:00 is fresh, 20:01 is stale | `Test-C545_WatchdogStale` row `age 20:01 stale` | PC-545-3 |
| G-545-4 | D-3: a missing desktop worker never changes due-day job facts (no job or run invented) | `C545_OutageKinds` row `desktop-worker-missing/after-grace` | PC-545-4 |
| G-545-5 | D-7 rule 1: reader-first retry never resends an attempt the recipient already has | `C545_ReaderFirstRetry` row `lost-response/reader-sees-it` | PC-545-5 |
| G-545-6 | D-9: monitor `Identity.WatchdogInstanceId` must equal the receipt's (ordinal) | `C545_WatchdogInstance` row `mismatch` | PC-545-6 |
| G-545-7 | D-8: recovery is sent even when the failure was never received and says `failureReceived=false` | `C545_RecoveryLinksFailure` | PC-545-7 |
| G-545-8 | D-11: namespace `mc` refuses `AllowFaultInjection`/`CrashAfter` | `C545_ProductionRefusesFaultInjection` | PC-545-8 |
| G-545-9 | D-10: the wrapper's last stdout line is the JSON record on every exit path, flags equal to `last-run.json`; refusal carries `exitCode=3` and empty identity | `Test-C545_ResultLine` row `refusal` | PC-545-9 |
| G-545-10 | D-10: a completed `success=true` row without a fetched result (missing, null, unfetchable, beyond the five-row cap) is never `success` | `Test-C545_JobResultFetch` rows `c6 unknown`, `c3 unknown` | PC-545-10 |
| G-545-11 | D-3: only SSH-classified logs give `windows-hop-failed`; other failures are `job-failed` | `C545_OutageKinds` rows `job-failed/assertion`, `job-failed/empty-logs` | PC-545-11 |
| G-545-12 | D-3: `deadline-missed` never duplicates an open due-day outage | `C545_DeadlineMissedSuppressedByOpenOutage` | PC-545-12 |
| G-545-13 | Deploy profile: no profile means exit 3 and no runner call | `Test-C545_DeployRefusesWithoutProfile` row `no-profile/no-arg` | PC-545-13 |
| G-545-14 | D-5/D-6: `readByRecipient` never gates or grants `received` | `C545_AcceptanceRecordedSeparately` row `read-marker-without-body` | PC-545-14 |
| G-545-15 | D-4/D-11: a ledger is bound to one namespace and the qualification instance cannot use the production state dir | `C545_LedgerNamespaceBound` | PC-545-15 |
| G-545-16 | D-9: an unreachable or malformed snapshot is unhealthy, never fresh | `Test-C545_WatchdogStale` rows `unreachable`, `malformed` | PC-545-16 |
| G-545-17 | D-9: the observed `instanceId` must equal the configured expected id | `Test-C545_WatchdogStale` row `instance wd-2 mismatch` | PC-545-17 |
| G-545-18 | D-9: an open outage in the snapshot makes readiness unhealthy | `Test-C545_WatchdogStale` row `open outage` | PC-545-18 |
| G-545-19 | D-9: the receipt's `watchdogInstanceId` is required non-blank | `C545_WatchdogInstance` rows `receipt-missing`, `receipt-blank` | PC-545-19 |
| G-545-20 | D-10: `localDueDate` is the Europe/London date of the run start | `Test-C545_ResultLine` row `dst-summer` | PC-545-20 |
| G-545-21 | OQ-1/D-12: tracked watchdog assets name no host, address, user or absolute home path | `Test-C545_HostAgnosticAssets` | PC-545-21 |
| G-545-22 | Deploy profile: placeholder values (`<`/`>`) refuse with exit 3 | `Test-C545_DeployRefusesWithoutProfile` row `placeholder/sshTarget` | PC-545-22 |
| G-545-23 | Deploy: the env writer touches only the three non-secret keys and only when absent; never prints a secret | `Test-C545_DeployRendersFromProfile` row `deploy-env-keys` | PC-545-23 |
| G-545-24 | D-4: receipt import is idempotent on `(nid, messageId)` | `C545_ReceiptIdempotent` | PC-545-24 |
| G-545-25 | D-4: the outage epoch increments only when the same kind re-opens after closure | `C545_OutageIdentityEpoch` | PC-545-25 |
| G-545-26 | D-3: a due-day outage closes only on a later scheduled `success=true` job with a matching result; never retroactively; no run invented | `C545_ClosureNeverInventsRun` rows `manual-success`, `earlier-day-success`, `missing-due-date` | PC-545-26 |
| G-545-27 | D-4: outage, notification and attempt 1 are one transaction | `C545_IntentBeforeNetwork` | PC-545-27 |
| G-545-28 | D-9: a wildcard snapshot bind is refused for the native runtime | `C545_OptionsRefuseWildcardBind` | PC-545-28 |
| G-545-29 | D-5/D-9: the snapshot carries no secret and no raw chat id | `C545_SnapshotShape` | PC-545-29 |
| G-545-30 | D-7 rule 4: a held reader never triggers a resend before the hold expiry | `C545_HeldReaderNoResend` | PC-545-30 |
| G-545-31 | D-3: start grace is overdue at exactly 01:00 London and the deadline is missed at exactly 08:00 | `C545_OutageKinds` rows `start-overdue/at-grace`, `deadline-missed/at` | PC-545-31 |
| G-545-32 | D-3: due day, due instant and deadline are London-local across DST | `C545_LondonDueDays` | PC-545-32 |
| G-545-33 | D-3: a result matches a due day only with `nativeRunId` and `localDueDate` equal to that day | `C545_OutageKinds` rows `result-missing/no-due-date`, `result-missing/wrong-due-date` | PC-545-33 |
| G-545-34 | D-11: the qualification notice is ledger-backed through the same transport/reader | `C545_QualificationNoticePath` | PC-545-34 |
| G-545-35 | D-9: readiness requires snapshot `namespace == mc` | `Test-C545_WatchdogStale` row `namespace mc/qual` | PC-545-35 |
| G-545-36 | D-9: no Windmill production notification sink remains in the local evaluator | `Test-C545_JobResultFetch` row `no-sink` | PC-545-36 |
| G-545-37 | Deploy: preflight performs no upload, `systemctl` or `sudo` without `-Deploy` | `Test-C545_DeployRendersFromProfile` row `preflight-no-upload` | PC-545-37 |
| G-545-38 | D-5: transport errors and exceptions never carry the bot token | `C545_TelegramTransportRequestShape` error rows | PC-545-38 |
| G-545-39 | D-6/D-12: a reader without a session or api credentials is `Held`, never eligible-empty | `C545_ReaderHeldWithoutSession` | PC-545-39 |

Inventory: guards = 52 (13 carried + 39 new), mapped = 52, missing = 0, duplicate PC
mappings = 0. Untested safety-critical guards: none. Assertions deliberately without a
PC (not safety-critical; ordinary V rows): `RunBudgetHours` timing (`run-stalled/*`),
backoff schedule and `retry-after` (V-545-10), snapshot 64 KB cap and 404/405
(V-545-13), body length/ASCII (V-545-6), `tests-red`/`coverage-incomplete` are not
outages (V-545-3), Windmill request-shape rows (V-545-15), readiness definition census
(V-545-20; its routing PC is CARD-0487's G-116), reader observation mapping (V-545-16),
`--self-check` (S6). Test-only guards are none: every G row protects production code or a
tracked asset.

### Positive controls

Mutation runs each PC method-scoped on a local inherited SourceLanding child:
`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-pc/ -- --treenode-filter "/*/*/<Class>/<Method>"`
for C# rows; `pwsh -NoProfile -File scripts/<harness>.ps1 -Case <Name> -ResultsDirectory <fresh>`
for PS rows (or the C# wrapper method). Each cycle: apply the mutation, run red at the
named assertion, restore, refresh the restored file's timestamp, run green. Zero tests,
build failures and fixture errors are not red.

| PC | Break (compiling defect) | Exact method | Expected red |
|---|---|---|---|
| PC-78 | `OutageEvaluator`: for a completed `success=false` job whose logs match the SSH classifier, return no transition (treat the hop failure as transient) | `NightlyVerificationContractTests.C544_IndependentOutage` | `Outages().Single(o => o.Kind == "windows-hop-failed")` throws (no such outage); nothing delivered |
| PC-79 | `WatchdogLoop`: invoke `transport.SendAsync` before `Ledger.OpenOutageWithIntent` commits (send inside the transaction, commit after) | `NightlyVerificationContractTests.C544_NotificationIntent` | `Transport.IntentVisibleAtSend.ShouldBe(true)` fails (second connection sees no pending attempt at send time) |
| PC-80 | `WatchdogLoop`: on a transport exception, call `Ledger.RecordAttempt(accepted: true, messageId: null)` | `NightlyVerificationContractTests.C544_NotificationRetry` | at tick 2 (+2 min) `Attempts(nid).Count.ShouldBe(2)` fails (stays 1: an "accepted" attempt waits 30 min) |
| PC-81 | `WatchdogLoop`/`Ledger.RecordAttempt`: set `notifications.state = received` when `TransportResult.Accepted` | `NightlyVerificationContractTests.C544_RecipientEvidence` | `State.ShouldBe("sent")` fails with `received`; snapshot `receivedAt` non-null |
| PC-82 | `ReceiptImporter` rule 2: match the notification by `oid` + `kind` instead of `nid` | `NightlyVerificationContractTests.C544_ReceiptNotificationIdentity` | `Reason.ShouldBe("unknown-notification")` fails (`body-mismatch` or `imported`) |
| PC-83 | `ReceiptImporter` rule 3: drop the `run` equality (keep `oid`/`due`/`kind`) | `NightlyVerificationContractTests.C544_ReceiptRunIdentity` | `Reason.ShouldBe("run-mismatch")` fails (`body-mismatch`) |
| PC-84 | `WatchdogLoop` start-up reconciliation: for an open outage whose failure notification is not `received`, allocate a new `nid` and attempt 1 instead of resuming the pending one | `NightlyVerificationContractTests.C544_NotificationCrash` | row `TransportAccepted`: `Notifications().Count.ShouldBe(1)` fails (2) and `Chat.Messages.Count.ShouldBe(1)` fails (2) |
| PC-85 | `Ledger.CloseOutageWithRecovery`: set `closedAt` without inserting the recovery notification/attempt | `NightlyVerificationContractTests.C544_RecoveryNotification` | `Notifications().Single(n => n.Kind == "recovery")` throws (none) |
| PC-86 | `TelegramBotTransport`: `chatId = string.IsNullOrWhiteSpace(options.DestinationChatId) ? "0" : options.DestinationChatId` before the authorization check (substitute a default) | `NightlyVerificationContractTests.C544_AuthorizedDestination` | row `unset`: `handler.Requests.Count.ShouldBe(0)` fails (1) and `ErrorClass.ShouldBe("destination-unauthorized")` fails |
| PC-95 | `ReceiptImporter` rule 1: accept any `peerId` | `NightlyVerificationContractTests.C544_ReceiptDestination` | `Reason.ShouldBe("peer-unauthorized")` fails (`imported`); `Receipts()` non-empty |
| PC-96 | `ReceiptImporter` rule 4: compare `sha256(marker line)` to a marker-only hash instead of `sha256(text)` to `bodySha256` | `NightlyVerificationContractTests.C544_ReceiptWholeBody` | row `marker-only`: `Reason.ShouldBe("body-mismatch")` fails (`imported`) |
| PC-97 | `ReceiptImporter` rule 5: remove the `date >= attempt.startedAt - 120 s` check | `NightlyVerificationContractTests.C544_ReceiptAttemptFloor` | row `-121 s`: `Reason.ShouldBe("predates-attempt")` fails (`imported`) |
| PC-98 | `Ledger.OpenOutageWithIntent` also writes `<stateDir>/pending-intents.json`, and `WatchdogLoop` start-up reads pending intents from that file instead of `Ledger.PendingNotifications()` | `NightlyVerificationContractTests.C544_IndependentState` | after `RestartAsync(ledgerOnly: true)` `Sends[0].Nid.ShouldBe(originalNid)` fails (no send; `Sends` empty) |
| PC-545-1 | `WatchdogLoop.TickAsync`: `return report;` after probing when the reachability probe failed (skip evaluation and delivery) | `NightlyVerificationContractTests.C545_NoWindmillDependency` | tick 2 `Sends[0].Accepted` fails (`Sends` empty); `Chat.Messages.Count.ShouldBe(1)` fails |
| PC-545-2 | `OutageEvaluator`: treat a failed reachability probe as `reachable=true` with an empty job list | `NightlyWatchdogCoreTests.C545_OutageKinds` | row `windmill-unreachable/second-tick`: `Transitions.ShouldContain(Opened windmill-unreachable)` fails |
| PC-545-3 | `Test-NightlyWatchdogFreshness`: stale threshold `-gt 30` minutes instead of `-gt 20` | `Test-C545_WatchdogStale` (`NightlyVerificationContractTests.C545_WatchdogStale`) | `PASS C545 WatchdogStale age 20:01 stale` becomes FAIL |
| PC-545-4 | `OutageEvaluator`: when no desktop ping is seen, synthesize a `Running` job for the due day before due-day evaluation | `NightlyWatchdogCoreTests.C545_OutageKinds` | row `desktop-worker-missing/after-grace`: transitions set lacks `start-overdue` and/or an outage row has a non-null `JobId` |
| PC-545-5 | `RetryPolicy`: skip the readback and resend whenever the last attempt has no acceptance | `NightlyVerificationContractTests.C545_ReaderFirstRetry` | row `lost-response/reader-sees-it`: `Sends.ShouldBeEmpty()` fails; `Attempts(nid).Count.ShouldBe(1)` fails (2) |
| PC-545-6 | `InterimVerificationReadinessReader`: remove the `Identity.WatchdogInstanceId` equality (keep the presence check) | `InterimVerificationReadinessTests.C545_WatchdogInstance` | row `mismatch`: `ShouldBe((false, "monitor_watchdog_mismatch"))` fails with `(true, "ready")` |
| PC-545-7 | `Ledger.CloseOutageWithRecovery`: insert the recovery notification only when the failure notification is `received` | `NightlyVerificationContractTests.C545_RecoveryLinksFailure` | `Notifications().Single(n => n.Kind == "recovery")` throws (none) |
| PC-545-8 | `WatchdogOptions.Validate()`: delete the `Namespace == "mc" && (AllowFaultInjection || CrashAfter != null)` refusal | `NightlyVerificationContractTests.C545_ProductionRefusesFaultInjection` | `Errors.ShouldContain("fault-injection-forbidden")` fails (no error) |
| PC-545-9 | `nightly-run-impl.ps1`: return from the shared-tree refusal before emitting the result line | `Test-C545_ResultLine` (`NightlyVerificationContractTests.C545_ResultLine`) | `PASS C545 ResultLine refusal last line is the JSON record` becomes FAIL (last line is `  CheckoutRoot: ...`) |
| PC-545-10 | `ConvertFrom-NightlyWindmillJob`: map `CompletedJob` with real `success=$true` to `success` even when `nativeRunId`/`localDueDate` are empty | `Test-C545_JobResultFetch` (`NightlyVerificationContractTests.C545_JobResultFetch`) | `PASS C545 JobResultFetch c6 stays unknown beyond the cap` and `... c3 unfetchable is unknown` become FAIL |
| PC-545-11 | `OutageEvaluator`: classify every completed `success=false` job as `windows-hop-failed` | `NightlyWatchdogCoreTests.C545_OutageKinds` | row `job-failed/assertion`: expected kind `job-failed`, got `windows-hop-failed` |
| PC-545-12 | `OutageEvaluator`: open `deadline-missed` at/after 08:00 regardless of open due-day outages | `NightlyWatchdogCoreTests.C545_DeadlineMissedSuppressedByOpenOutage` | `Transitions.ShouldNotContain(Opened deadline-missed)` fails; `Notifications().Count.ShouldBe(1)` fails (2) |
| PC-545-13 | `deploy-nightly-watchdog.ps1`: when no profile resolves, use a built-in `@{ sshTarget = 'ops@watchdog-host.example'; ... }` and continue | `Test-C545_DeployRefusesWithoutProfile` (`NightlyVerificationContractTests.C545_DeployRefusesWithoutProfile`) | `PASS C545 DeployRefusesWithoutProfile no-profile/no-arg exit 3 no runner call` becomes FAIL (exit 0, runner called) |
| PC-545-14 | `ReceiptImporter`: when `observation.ReadByRecipient` is true, mark `received` before rule 4 | `NightlyVerificationContractTests.C545_AcceptanceRecordedSeparately` | row `read-marker-without-body`: `Reason.ShouldBe("body-mismatch")` fails (`imported`); state `received` |
| PC-545-15 | `Ledger` open: skip the stored-namespace comparison (overwrite `heartbeat.namespace`) | `NightlyWatchdogCoreTests.C545_LedgerNamespaceBound` | `Should.Throw<LedgerNamespaceMismatchException>` fails (no exception) |
| PC-545-16 | `Get-NightlyWatchdogSnapshot`: on exception return a synthetic fresh snapshot object (`heartbeatAt = now`) | `Test-C545_WatchdogStale` | `PASS C545 WatchdogStale unreachable` becomes FAIL (Healthy) |
| PC-545-17 | `Test-NightlyWatchdogFreshness`: drop the `instanceId -eq ExpectedInstanceId` comparison (keep non-empty) | `Test-C545_WatchdogStale` | `PASS C545 WatchdogStale instance wd-2 mismatch` becomes FAIL |
| PC-545-18 | `Test-NightlyWatchdogFreshness`: ignore `openOutages` | `Test-C545_WatchdogStale` | `PASS C545 WatchdogStale open outage unhealthy` becomes FAIL |
| PC-545-19 | `InterimVerificationReadinessReader`: accept a missing or blank receipt `watchdogInstanceId` (treat as `""` and continue) | `InterimVerificationReadinessTests.C545_WatchdogInstance` | rows `receipt-missing`/`receipt-blank`: expected `qualification_watchdog_missing`, got `monitor_watchdog_mismatch` |
| PC-545-20 | `nightly-run-impl.ps1`: `localDueDate = (Get-NightlyUtcNow).ToString('yyyy-MM-dd')` (UTC date) | `Test-C545_ResultLine` | `PASS C545 ResultLine dst-summer localDueDate 2026-07-01` becomes FAIL (`2026-06-30`) |
| PC-545-21 | `docs/nightly-watchdog.md`: add the line `ssh ops@watchdog-host.example` (fictitious token; never a real host) | `Test-C545_HostAgnosticAssets` (`NightlyVerificationContractTests.C545_HostAgnosticAssets`) | `PASS C545 HostAgnosticAssets no ssh-target token` becomes FAIL naming the file and line |
| PC-545-22 | `deploy-nightly-watchdog.ps1` profile validator: remove the `<`/`>` placeholder check | `Test-C545_DeployRefusesWithoutProfile` | `PASS C545 DeployRefusesWithoutProfile placeholder/sshTarget exit 3` becomes FAIL |
| PC-545-23 | `deploy-nightly-watchdog.ps1` env writer: also append `ANTIPHON_WATCHDOG_TELEGRAM_BOT_TOKEN=` when absent and rewrite present keys | `Test-C545_DeployRendersFromProfile` (`NightlyVerificationContractTests.C545_DeployRendersFromProfile`) | `PASS C545 DeployRendersFromProfile env fragment has exactly the absent non-secret keys` becomes FAIL |
| PC-545-24 | `Ledger.ImportReceipt`: plain `INSERT` without the `(nid, messageId)` unique index / `ON CONFLICT DO NOTHING` | `NightlyWatchdogCoreTests.C545_ReceiptIdempotent` | `Receipts().Count.ShouldBe(1)` fails (2) or the second import throws instead of returning `duplicate` |
| PC-545-25 | `Ledger.OpenOutageWithIntent`: always use epoch 1 (`INSERT OR REPLACE`) | `NightlyWatchdogCoreTests.C545_OutageIdentityEpoch` | `OutageId.ShouldEndWith(":2")` fails; `Outages().Count.ShouldBe(2)` fails (1) |
| PC-545-26 | `OutageEvaluator` closure: close a due-day outage on any completed job for that day (ignore `success` and result matching) | `NightlyWatchdogCoreTests.C545_ClosureNeverInventsRun` | rows `manual-success`/`missing-due-date`: `ClosedAt.ShouldBeNull()` fails |
| PC-545-27 | `Ledger.OpenOutageWithIntent`: commit the outage row in its own transaction before inserting the notification and attempt | `NightlyWatchdogCoreTests.C545_IntentBeforeNetwork` | throwing-factory row: `Outages().ShouldBeEmpty()` fails (1 orphan outage) |
| PC-545-28 | `WatchdogOptions.Validate()`: allow `0.0.0.0`/`::` for every runtime | `NightlyWatchdogCoreTests.C545_OptionsRefuseWildcardBind` | row `0.0.0.0/native`: `Errors.ShouldContain("snapshot-bind-wildcard")` fails |
| PC-545-29 | `SnapshotServer`: emit `"destination": { "chatId": options.DestinationChatId, ... }` | `NightlyWatchdogCoreTests.C545_SnapshotShape` | `Body.ShouldNotContain("123456789")` fails |
| PC-545-30 | `RetryPolicy`: treat `Held` as eligible-empty (resend under rules 2/3 while held) | `NightlyVerificationContractTests.C545_HeldReaderNoResend` | at 29:59 held: `Attempts(nid).Count.ShouldBe(1)` fails (2) |
| PC-545-31 | `OutageEvaluator`: `now > graceEnd` instead of `now >= graceEnd` (and the same for the deadline) | `NightlyWatchdogCoreTests.C545_OutageKinds` | row `start-overdue/at-grace`: expected `Opened start-overdue`, got none (and `deadline-missed/at`) |
| PC-545-32 | `LondonClock.DueDay`: return `utc.Date` (UTC calendar date) | `NightlyWatchdogCoreTests.C545_LondonDueDays` | row `2026-06-30T23:00:00Z -> 2026-07-01` fails (`2026-06-30`) |
| PC-545-33 | `OutageEvaluator.ResultMatches`: accept a result whose `localDueDate` is missing when `nativeRunId` is present | `NightlyWatchdogCoreTests.C545_OutageKinds` | row `result-missing/no-due-date`: expected `Opened result-missing`, got none |
| PC-545-34 | `Program --send-qualification-notice` / `WatchdogLoop.SendQualificationNoticeAsync`: call `transport.SendAsync` directly with a fresh ULID and no ledger row | `NightlyVerificationContractTests.C545_QualificationNoticePath` | `Notifications().Count.ShouldBe(1)` fails (0); the next tick's import is `unknown-notification` |
| PC-545-35 | `Test-NightlyWatchdogFreshness`: drop the `namespace -eq 'mc'` check | `Test-C545_WatchdogStale` | `PASS C545 WatchdogStale namespace mc/qual mismatch` becomes FAIL |
| PC-545-36 | `New-NightlyProductionWindmillApi`: re-add the `EnqueueNotification` member posting to `jobs/run/p/<NotifyPath>` | `Test-C545_JobResultFetch` | `PASS C545 JobResultFetch no production Windmill notification sink` becomes FAIL |
| PC-545-37 | `deploy-nightly-watchdog.ps1`: run the upload and `systemctl enable --now` in preflight (ignore `-Deploy`) | `Test-C545_DeployRendersFromProfile` | `PASS C545 DeployRendersFromProfile preflight-no-upload` becomes FAIL (scp/systemctl recorded) |
| PC-545-38 | `TelegramBotTransport`: include `request.RequestUri` in `TransportResult.Error` | `NightlyWatchdogCoreTests.C545_TelegramTransportRequestShape` | error rows: `Error.ShouldNotContain(token)` fails |
| PC-545-39 | `TelegramUserReader.ReadAsync`: when the session file is missing return an empty eligible read | `NightlyWatchdogCoreTests.C545_ReaderHeldWithoutSession` | `Result.IsHeld.ShouldBeTrue()` fails |

PC batching allowed (different files and methods, no shared assertion): {PC-545-3, 16,
17, 18, 35} share `nightly-health.ps1` freshness functions and must run separately;
{PC-545-2, 4, 11, 12, 26, 31, 33} all mutate `OutageEvaluator` and must run separately;
{PC-82, 83, 95, 96, 97, 545-14} all mutate `ReceiptImporter` and must run separately.
Everything else may batch by distinct file.

### Out of scope

- Live Telegram acceptance, MTProto readback, the read marker, session revocation,
  systemd supervision, the tailnet bind, Windmill registration/readback and the real
  00:30 scheduled run: S6 (operator-run; blocked on the deploy-time actions OQ-1/OQ-6
  and the reader login). Neither this stage nor Code claims them. CARD-0544 activation
  stays disabled until the receipt exists.
- `--self-check`, `--reader-login`, `--stub-windmill` and `--export-evidence` command
  behaviour beyond argument parsing (V rows would need the host runtime; the stub server
  is exercised only in S6 F-3).
- The F-1..F-6 injection matrix: qualification instance on the host (S6 step 7).
- `scripts/deploy-am-service.ps1` retrofit (plan scope boundary).
- Any change to `AgentTaskReplyService`, land delivery, session delivery, the gateway
  (`channels.*`), the desktop worker container or Windows Scheduled Tasks.
- Concurrency of two watchdog instances on one ledger file (D-11 gives each instance its
  own state dir; the namespace binding in G-545-15 refuses the shared-dir case).
- WTelegramClient network behaviour (TLS, DC migration, flood waits) and Windmill
  token scopes (OQ-6).
- `Test-NightlyMonitorHealth` clock/readiness rules (CARD-0544 G-72..77/87..94 stay
  there; R-545-1 keeps them green).

### Cost

All figures are **estimated** (nothing was built or run in this dispatch); assumptions:
one foreground owner, isolated `bin-<name>/` output, warm NuGet cache, local pwsh 7,
no concurrent `Antiphon.Agents.Pty.Tests`. TestDesign's own active time is reported in
the stage report.

| Ordinary V/R floor, per Code or independent Review pass | Minutes |
|---|---:|
| Setup: restore + build `tests/Antiphon.Tests` graph with the new watchdog project into `bin-c545/` | 15 |
| `NightlyWatchdogCoreTests` full class (14 tests; SQLite temp files, one loopback listener) | 1 |
| `NightlyVerificationContractTests` full class (10 existing + 8 new pwsh wrappers at ~8 s, 20 in-process) | 4 |
| `InterimVerificationReadinessTests` + `VerificationRoundBriefTests` + C544 attached classes (`/*/*/*C544*/*`) | 4 |
| `NightlyScriptsTests` (4) | 1 |
| Unit lane `[Category=Unit]` (readiness record and policy touched) | 2 |
| PS harnesses direct: `test-nightly-health.ps1` full (1), `test-nightly-run.ps1` full (2), `test-nightly-tests.ps1` full (1), `test-deploy-nightly-watchdog.ps1` full (0.5) | 5 |
| **Per-pass setup + ordinary V/R** | **32** |

Code floor 32; independent ordinary Review floor another 32. Band per pass: 25-40.

| PC floor (Mutation), method-scoped red/restore/green | Controls | Min per cycle | Minutes |
|---|---:|---:|---:|
| C# controls (13 carried + PC-545-1,2,4,5,6,7,8,11,12,14,15,19,24,25,26,27,28,29,30,31,32,33,34,38,39): edit, incremental build (~45 s watchdog + ~40 s test relink), two runs | 38 | 3.5 | 133 |
| PS controls (PC-545-3,9,10,13,16,17,18,20,21,22,23,35,36,37): edit, two harness runs | 14 | 1.5 | 21 |
| Mutation setup: snapshot build of `bin-pc/`, `--list-tests` sanity of the two classes | - | - | 20 |
| Restoration inventory, timestamp refresh, evidence | - | - | 15 |
| **Mutation floor, all 52 controls, unbatched** | **52** | - | **189** |

Band 160-220. Batching the 24 batchable C# controls in groups of four saves about
40 minutes (each group shares one build pair); the three must-run-separately sets
(evaluator 7, importer 6, freshness 5) cannot batch. No concurrency saving is assumed
(one managed snapshot).

**Total verification floor** = setup/build 15 + ordinary V/R 17 + every PC red/restore/
green 154 + Mutation setup/evidence 35 = **221 minutes, estimated**, unbatched.

**Plan cost-band check.** The plan's TestDesign band (130-190) stands. S1 (90-150), S3
(240-360), S4 (120-180), S5 (105-165) and Review (120-180) are consistent with the
inventory above. Two bands change materially and the plan's Cost table should be read
with these corrections: **Mutation 60-100 -> 160-220** (the plan priced 27 PCs; the
finalized inventory has 52 because the bundled candidates split into independently
bypassable checks and D-3/D-4/D-9 invariants gained controls) and **S2 150-240 ->
180-270** (14 core tests and the fixture contract instead of 9). The moved-cost
baseline from CARD-0544 (13 x 1.5 = 19.5 min) is superseded by the C# cycle rate above.

--- next stage ---
next: code
handoff: Code implements S1-S5 against the finalized verification design (13 carried N.C544_* in-process on C545World, NightlyWatchdogCoreTests 14 methods, C545_WatchdogInstance, PS cases C545_ResultLine/JobResultFetch/WatchdogFresh/WatchdogStale/ReadinessRouting and the deploy harness) and runs the ordinary V/R floor; S6 stays blocked on the operator's deploy-time actions.
artifact: docs/superpowers/plans/2026-09-17-card-0545-independent-nightly-watchdog-plan.md
