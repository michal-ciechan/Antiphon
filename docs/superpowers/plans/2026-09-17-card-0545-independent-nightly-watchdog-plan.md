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
