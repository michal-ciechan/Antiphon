# Nightly backstop watchdog

Owner document for CARD-0545's independent nightly watchdog: what it watches, where it may run, how it is
deployed on **any** qualifying host, how it proves a notification reached the recipient, and how the Windows
side folds its heartbeat into readiness. It names no host, address or user; host details live only in the
untracked deploy profile and the watchdog host's own env file.

Plan and verification design: [CARD-0545 plan](superpowers/plans/2026-09-17-card-0545-independent-nightly-watchdog-plan.md).
Nightly bootstrap and readiness rules: [testing-and-build.md § Nightly](testing-and-build.md#nightly).

## What it does

`src/Antiphon.NightlyWatchdog` is a long-lived .NET process with a 10-minute tick. Each tick it:

1. Probes Windmill (`/api/version`, `whoami`, the nightly script and schedule, `workers/list`, `jobs/list` plus
   `get_result`/`get_logs` for the watched rows).
2. Evaluates outage kinds for the Europe/London due day: `windmill-unreachable`, `windmill-auth-failed`
   (two consecutive failed ticks), `script-missing`, `script-hash-drift`, `schedule-missing`, `schedule-disabled`,
   `desktop-worker-missing` (60 minutes without a `desktop` ping), `start-overdue` (01:00 London),
   `run-stalled` (6 h), `windows-hop-failed` (SSH evidence in the job log), `job-failed`, `result-missing`,
   `report-undelivered` and `deadline-missed` (08:00 London, suppressed while another outage for that day is
   open). `tests-red` and `coverage-incomplete` are not outages: the nightly board card owns them.
3. Persists each new outage, its notification and attempt 1 in one SQLite transaction
   (`<stateDir>/ledger.db`) **before** any network call. Identities: `nw:{namespace}:{dueDay}:{kind}:{epoch}`
   and a ULID `nid`.
4. Reads the recipient's view first (reader-first retry), imports receipts, then sends or retries through the
   Telegram Bot API directly. Closure sends a separate recovery notification linked to the failure.
5. Writes a heartbeat and serves `GET http://<snapshotBind>/snapshot.json` (read-only, no auth, 64 KB cap,
   no secret and no raw chat id).

It never reads Windows-side files, never consumes `channels.*`, and never changes Windmill, the desktop worker
or the messaging gateway.

## Evidence tiers

| State | Set by | Meaning |
|---|---|---|
| `pending` | ledger intent | persisted, not yet accepted |
| `sent` | Bot API `sendMessage` accepted (`message_id`, `acceptedAt`) | tier 1, sender-side |
| `received` | `ReceiptImporter` matched a reader observation | tier 2, recipient-side |

An observation imports only when it comes from the authorized peer, names a ledger notification and attempt,
matches the outage/due/kind (and run) identity, hashes to the whole produced body, and is not older than the
attempt start minus 120 s. The dialog read marker is recorded as `readObservedAt`; it never grants `received`.
The snapshot shows both tiers.

## Host requirements

Any always-on host qualifies when all of these hold:

1. It is not the Windows execution host and not a process, container or job supervised by Windmill's scheduler,
   workers or control plane.
2. Linux with systemd (native shape), or any host with a container runtime (container shape). A non-root
   service user with a writable state directory on persistent disk.
3. Outbound HTTPS to `api.telegram.org` and Telegram's MTProto endpoints; a route to Windmill's API; one
   private-network address (tailnet or LAN) the desktop can reach for the snapshot port. No other inbound
   exposure.
4. A correct clock (NTP) and `Europe/London` zone data.

Sharing the host with other services is a residual, not a violation: the unit depends on no container or
Windmill service. Loss of the whole watchdog host is detected only by the Windows evaluator's 20-minute
heartbeat check (accepted residual; there is no dead-man's switch).

## Deploy profile (untracked)

`scripts/deploy-nightly-watchdog.ps1` finds its target only through a JSON profile, in this order: `-Profile
<path>`, `ANTIPHON_WATCHDOG_DEPLOY_PROFILE`, `C:\Antiphon\nightly\watchdog-deploy.json`, then exactly one
gitignored `scripts/nightly-watchdog/*.local.json`. No profile, or any `<placeholder>` value, exits 3 and contacts
nothing. Shape (`scripts/nightly-watchdog/deploy.example.json`):

| Key | Meaning |
|---|---|
| `sshTarget` | `<user>@<host>` used by ssh/scp |
| `sshIdentityFile` | optional private key path on the desktop |
| `remoteRoot` | absolute install directory (unit `WorkingDirectory`) |
| `serviceUser` | the non-root user the unit runs as |
| `rid` | publish runtime identifier, default `linux-x64` |
| `runtime` | `native` or `container` |
| `snapshotBind` | `<private-ip>:17290`; loopback is refused, a wildcard only for `container` |
| `snapshotUrl` | the URL the desktop uses; written into `readiness-config.json` at qualification |
| `qualSnapshotBind` | qualification instance bind, default port 17291 |
| `stubWindmillBind` | F-3 stub bind, default `127.0.0.1:17292` |

## Deploy

1. **Preflight** (default, read-only): publishes `dotnet publish -c Release -r <rid> --self-contained
   -p:PublishSingleFile=true`, checksums the binary, renders `antiphon-nightly-watchdog.service.template`
   (`@@SERVICE_USER@@`, `@@REMOTE_ROOT@@`) and `qual.example.json`, prints the non-secret profile values and runs
   the remote `--self-check` when a binary is already installed.
2. **`-Deploy`** (explicit opt-in): creates `state` and `state-qual`, uploads the binary, unit and `qual.json`,
   appends only the absent owned keys (`ANTIPHON_WATCHDOG_SNAPSHOT_BIND`, `_STATE_DIR`, `_NAMESPACE`) to
   `<remoteRoot>/env` (secret keys are never read, written or printed), installs and enables the unit, and
   verifies `GET <snapshotUrl>`.
3. **Self-check gate** on the host: `./Antiphon.NightlyWatchdog --self-check` prints runtime, OS release, two DST
   due instants, opens a temp SQLite ledger, binds the snapshot address and, when the bot token is set, calls
   `getMe`. The binary is self-contained; an OS outside Microsoft's supported matrix must pass this gate.
4. **Container fallback** when the gate fails: run the same binary in `mcr.microsoft.com/dotnet/runtime-deps:9.0`
   as its own compose project in `<remoteRoot>` (`restart: always`, port mapping restricted to the private
   address, `runtime: container` in the profile). The qualification artifact records which shape was deployed.

## Operator-placed configuration (`<remoteRoot>/env`, mode 0600)

Key names are in `scripts/nightly-watchdog/env.example`: Windmill base URL, workspace, script/schedule paths,
expected script hash and a labelled, expiring, scoped Windmill token; the bot token; the authorized destination
chat id (the operator's DM with the bot); the reader peer id and username; the reader `api_id`/`api_hash`.

## Reader login (operator only, once)

`./Antiphon.NightlyWatchdog --reader-login` on the watchdog host, as the service user, prompts for the phone
number, code and 2FA password and stores only WTelegramClient's session file (`<stateDir>/reader.session`,
mode 0600). The session is as powerful as the operator's account; revoking it from Telegram's active-sessions
list fails the reader closed (`Held`; notifications stay `sent`). Without a session or credentials the reader is
`Held`, never an empty eligible view.

## Qualification instance

`./Antiphon.NightlyWatchdog run --config ./qual.json` runs a second instance: namespace `mc/qual`, state
directory `./state-qual`, its own snapshot bind. Fault injection (`allowFaultInjection`, `crashAfter`,
`holdControlPath`) is honoured only outside namespace `mc`; the production instance refuses it at start
(exit 3). `--stub-windmill <modeFile>` serves the F-3 loopback Windmill stub (`ok`, `refuse`, `503`, `timeout`).
`--send-qualification-notice` sends the ledger-backed production live notice; `--export-evidence <dir>` writes
outages, notifications, attempts and receipts as JSON for the qualification artifact.

## Windows side

`scripts/nightly-health.ps1` is the local readiness evaluator, registered as
`u/lndcobra/antiphon_nightly_readiness` (desktop tag, every 30 minutes). With
`-ReadinessConfigPath C:\Antiphon\nightly\readiness-config.json` (fields `expectedScriptHash`,
`expectedPolicyHash`, `watchdogSnapshotUrl`, `watchdogInstanceId`, `repositoryPath`, `projectId`) it reads the
snapshot (10 s bound) and adds `watchdog-unreachable`, `watchdog-stale` (older than 20:00),
`watchdog-malformed`, `watchdog-identity-mismatch` (namespace not `mc`, or a different instance id) or
`watchdog-outage-open` to `Health.Reasons`, each making `Healthy=false`. `last-monitor.json` records
`Identity.WatchdogInstanceId` and `WatchdogHeartbeatAt`. The server's `InterimVerificationReadinessReader`
requires the qualification receipt's `watchdogInstanceId` and its ordinal equality with the monitor identity,
so a replaced or unqualified watchdog cannot inherit readiness.

## Custody

| Reference | Where | Custodian |
|---|---|---|
| Deploy profile | untracked profile path (above) | operator |
| Windmill base URL, workspace, paths, expected hash | `<remoteRoot>/env` | operator |
| Windmill token (labelled, scoped, expiring) | `<remoteRoot>/env` | operator |
| Bot token | `<remoteRoot>/env` (source: the operator's password manager) | operator |
| Destination chat id | `<remoteRoot>/env`; ledger and artifact hold only `sha256(chatId)[..16]` | operator |
| Reader api id/hash and session | `<remoteRoot>/env`, `<stateDir>/reader.session` | operator |
| Snapshot bind, state dir, namespace | `<remoteRoot>/env`, written by the deploy script when absent | deploy script |
| Desktop readiness token file | `WINDMILL_TOKEN_FILE` on the desktop | operator |
| `readiness-config.json` | `C:\Antiphon\nightly\` (hashes, snapshot URL, instance id; no secrets) | written at qualification |

No value is ever logged, printed or committed. Deployment, registration, the reader login and every live
message are operator-run qualification steps (CARD-0545 S6), never a delegate's.
