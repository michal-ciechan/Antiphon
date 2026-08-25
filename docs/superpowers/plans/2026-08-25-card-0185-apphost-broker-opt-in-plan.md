# CARD-0185 — The AppHost stops committing `server2:19092`; the live broker becomes a per-machine opt-in — plan

**Date:** 2026-08-25 · **Card:** CARD-0185 (`a440ffd5-65e1-430e-82ca-878bac0e2727`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `master` @ `a073f49`. Every file:line below was re-read out of the code on
that commit; every live fact (containers, env vars, user-secrets, channel rows) was measured on
this machine on 2026-08-25.

**Why this needs care, stated first.** `Antiphon.AppHost/Program.cs:68` —
`.WithEnvironment("AntiphonMessaging__BootstrapServers", "server2:19092")` — is the *only* thing
that puts this machine's server on the live Family broker. Nothing else on this machine carries
that value: no user env var (`User`/`Machine` scope both empty), no user-secret (`antiphon-server`
and `aspire-antiphon-apphost` both "No secrets configured"), no `server/appsettings.Development.json`
(does not exist). Deleting the line and restarting is a silent cut-over of the live "Family"
Telegram channel (`lastMessageAt` 2026-08-23 19:44, enabled) and of CARD-0171's GitHub-sync
notification (Windmill → `github-sync.ps1 -Notify` → `:17202` → `TrackerSyncNotifier` →
`ChatChannelService.SendAsync` → `IAntiphonMessagingProducer` → `channels.outbound` on **that**
broker) to a local Redpanda nobody on server2 is listening to. The replacement therefore has to be
configured on this machine **before** the code lands, and it has to be something every launch path
reads — the logon Scheduled Task, the watchdog and `restart-apphost.ps1` all launch without any
operator present to pass a flag.

---

## Established facts (Investigate, this pass)

- **Precedence today: the AppHost line beats everything the server could read.** The server is
  a stock `WebApplication.CreateBuilder` (`server/Program.cs:100-391`; `AddAntiphonMessaging(
  builder.Configuration)` at `:391` binds section `AntiphonMessaging`,
  `src/Antiphon.Messaging.Client/ServiceCollectionExtensions.cs:16-18`). Its config order is
  `appsettings.json` → `appsettings.{Environment}.json` → user-secrets (Development only) →
  **environment variables** → command line. `.WithEnvironment` lands in the last-but-one slot, so
  the tracked `localhost:19092` (`server/appsettings.json:147-152`), any gitignored
  `appsettings.Development.json`, and any user-secret are all overridden. Removing the line is
  what makes those files *mean* something again.

- **The AppHost pins `ASPNETCORE_ENVIRONMENT=Development` on the server** (`Program.cs:53`,
  comment: unset would default to Production). Simple mode does the same through
  `server/Properties/launchSettings.json` (single profile `Development`,
  `ASPNETCORE_ENVIRONMENT=Development`; `dev-start.ps1:62` runs `dotnet run` which picks it). So
  `server/appsettings.Development.json` is the file both launch modes would read.

- **An untracked `server/appsettings.Production.json` already exists on this machine** (mtime
  2026-07-05 21:14, 248 bytes): `ChannelBridge:Enabled=true` + the full `AntiphonMessaging` block
  with `server2:19092`. It is gitignored by `.gitignore:5` (`appsettings.*.json` — the same rule
  that would hide a `Development.json`; `git check-ignore -v` confirms both). It is **not loaded
  by any launch path in use** (both pin Development), so it is a fossil, not the live opt-in — but
  it is also proof that the operator already reached for exactly the mechanism the card proposes,
  in July, and it silently stopped applying when the AppHost pinned Development. That history is
  the argument against a server-side file: an opt-in whose effect depends on which environment
  name a launcher happens to pin is one that gets lost.

- **`docs/bootstrap.md:101-104` already sanctions two per-machine overlays**: `dotnet user-secrets
  set … --id antiphon-server` (preferred) and a gitignored `server/appsettings.Development.json`
  (accepted). There is no `*.local.json` convention anywhere in the repo (the only `.local.json`
  is Claude Code's `.claude/settings.local.json`, `DelegationWorktreeService.cs:130`). So the
  repo's established "per machine, never committed" store is **user-secrets, with a gitignored
  `appsettings.Development.json` as the file-shaped alternative** — this design reuses that pair
  and invents nothing.

- **A server-side `appsettings.Development.json` leaks into every test that hosts the real
  `Program`.** `tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs` and
  `tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs:557-598` are `WebApplicationFactory<Program>`
  hosts: environment `Development`, content root the `server/` project, and their
  `AddInMemoryCollection` overrides (`AntiphonAppFixture.cs:566-576`) name neither
  `AntiphonMessaging:*` nor `ChannelBridge:Enabled`. Neither factory replaces
  `IAntiphonMessagingProducer`/`IAntiphonMessagingConsumer` (only the direct-construction unit
  tests do — `ChannelBridgeTests.cs:732-735`, `TrackerSyncEndpointTests.cs:385-387`,
  `TrackerSyncNotifierTests.cs:284-287` — via `FakeAntiphonMessagingClient`). Consequences on this
  machine if the opt-in lived in `server/appsettings.Development.json`: the real
  `KafkaAntiphonMessagingProducer` (`KafkaAntiphonMessagingProducer.cs:13-24`, builds at
  construction) would point at the live broker in every E2E/smoke test run, and if the file also
  carried `ChannelBridge:Enabled=true` — which the July fossil does — the E2E server would **join
  the live consumer group `antiphon-server-bridge`** (`KafkaAntiphonMessagingConsumer.cs:24-37`,
  `Earliest`, auto-commit) and take Family messages away from the running server. The AppHost's
  own configuration has zero test exposure; that is the deciding fact for Decision 1.

- **Every launch path funnels through `dev-aspire.ps1` → `dotnet run` in `Antiphon.AppHost/`**:
  `scripts/autostart-apphost.ps1:42,104` (logon task), `scripts/restart-apphost.ps1:54,134`, and
  the watchdog via `restart-apphost.ps1`. `dev-aspire.ps1:116` runs `dotnet run` in
  `$appHostDir`, so the AppHost's `launchSettings.json` "http" profile applies
  (`DOTNET_ENVIRONMENT=Development`, `Antiphon.AppHost/Properties/launchSettings.json:11`). None
  of the scripts mention `server2`, `19092`, `BootstrapServers` or `AntiphonMessaging` (grep over
  `*.ps1`: zero hits outside Windmill-schedule comments). **No script relies on the line, and no
  script needs to change** — *provided* the opt-in is something the AppHost process reads from
  disk on its own. A per-invocation `dev-aspire.ps1 -LiveTelegram` flag (the card's option 3) is
  the one shape that *would* need script support, in all three callers plus the Scheduled Task
  definition, and forgetting any one of them would launch local at the next logon or watchdog
  restart. It is rejected below for exactly that reason.

- **The AppHost already has the store.** `Antiphon.AppHost/Antiphon.AppHost.csproj:10` declares
  `<UserSecretsId>aspire-antiphon-apphost</UserSecretsId>` (currently empty), and Aspire 9.3.0's
  `DistributedApplication.CreateBuilder` loads `appsettings.json`,
  `appsettings.{DOTNET_ENVIRONMENT}.json`, user-secrets (Development) and environment variables
  into `builder.Configuration` — the same host-builder order the server uses. `Program.cs` reads
  none of it today (`grep 'Configuration\['` over `Antiphon.AppHost/**/*.cs`: zero hits);
  `appsettings.json` there holds only logging levels and the Postgres connection string.

- **Fresh clone after the line is deleted = the documented local stack, with no further change.**
  `server/appsettings.json:148` says `localhost:19092`; `docker-compose.dev.yml:40-69` brings up
  `antiphon-redpanda` advertising `localhost:19092`, and `dev-aspire.ps1:62` runs that compose up
  on every launch; the FakeGateway's tracked `appsettings.json` targets the same address; the
  AppHost's `ChannelBridge__Enabled=true` (`Program.cs:54`) stays. If Redpanda is not up the bridge
  logs a Warning and retries (`ChannelBridgeService.cs:61-83`, `ConsumeRetryBackoff`) — degraded,
  never a crash. Acceptance criteria 1 and 3 are satisfied by the deletion alone.

- **Nothing today tells you which broker the server is on.** The consumer's start line
  (`KafkaAntiphonMessagingConsumer.cs:38`) logs topic and group, not `BootstrapServers`; the
  server log for today has no `19092` anywhere; no endpoint exposes the value. The 2026-08-23
  local smoke in the card was verified by an end-to-end ACK because there was no cheaper signal.
  The migration below needs one, so Decision 3 adds it.

- **The fake gateway must never be pointed at the live broker.** A `POST :17208/inbound` produced
  onto am-redpanda's `channels.inbound` would be consumed by the live bridge, answered by the
  Family agent, and the reply produced to `channels.outbound` where `am-service` sends it through
  the real bot. The card's "one broker or the other" is a safety property, not a limitation; the
  design keeps the fake gateway on `localhost:19092` unconditionally.

- **A bounded local window loses no Family messages.** The server consumes as group
  `antiphon-server-bridge` with `EnableAutoCommit = true` (`KafkaAntiphonMessagingConsumer.cs:28`);
  offsets live on am-redpanda. While the desktop is on the local broker, inbound Family messages
  accumulate behind the committed offset and are consumed on rejoin. Replies the agent would have
  sent during the window are simply not generated (no consumption). This is what makes the
  verification window in §6 safe to run.

---

## Verdict up front

1. **Delete the line. The AppHost forwards `AntiphonMessaging:BootstrapServers` from its *own*
   configuration to the server, only when set.** Store on this machine: `dotnet user-secrets`
   under `aspire-antiphon-apphost` (accepted alternative: gitignored
   `Antiphon.AppHost/appsettings.Development.json`). Not the server's files, not a script flag.
2. **The migration for this machine is step 1 of the build order, done and verified before the
   code change is pushed**, and it is inert under the current code — so there is no window, of
   any length, in which the server can come up on `localhost`.
3. **Both processes log the effective broker at startup** — the AppHost (value + which source),
   the server's consumer (value) — so the migration is checked from two log lines, not by
   sending a message into the family chat.
4. Docs describe the opt-in; a source-guard test makes re-committing a hostname a red build.

---

## 1. Decision 1 — the opt-in lives in the AppHost's configuration

### The rule

`Antiphon.AppHost/Program.cs` replaces lines 63-68 with:

```csharp
// ── Messaging broker (CARD-0185) ──────────────────────────────────────────────────────────
// Default: whatever server/appsettings.json says — localhost:19092, the docker-compose.dev.yml
// Redpanda that the fake gateway (:17208) also uses. A LIVE broker (this machine: am-redpanda on
// server2 over Tailscale, which the real Family Telegram gateway produces to) is a per-machine
// opt-in that never appears in source:
//   dotnet user-secrets set "AntiphonMessaging:BootstrapServers" "server2:19092" --project Antiphon.AppHost
// or the gitignored Antiphon.AppHost/appsettings.Development.json. Forwarded verbatim as the
// server's AntiphonMessaging__BootstrapServers; the fake gateway is deliberately NOT forwarded
// (a fake inbound on the live broker would be answered through the real bot), so while live,
// POST :17208/inbound does not reach the server. It is one broker or the other.
var liveBroker = builder.Configuration["AntiphonMessaging:BootstrapServers"];
```

and, after the `server` resource is declared:

```csharp
if (!string.IsNullOrWhiteSpace(liveBroker))
    server.WithEnvironment("AntiphonMessaging__BootstrapServers", liveBroker.Trim());
```

The key name is the *server's* section and key, on purpose: the operator learns one name, and the
value travels unchanged. Only `BootstrapServers` is forwarded — `InboundTopic`, `OutboundTopic`,
`ConsumerGroup` stay the server's own (same values on both brokers today; a machine that needs to
override them has `server/appsettings.Development.json` for that, and it is not this card).

### Why the AppHost and not the server's files

- **Zero test exposure.** Nothing under `tests/` hosts the AppHost; the two
  `WebApplicationFactory<Program>` fixtures read the server's files. A server-side
  `appsettings.Development.json` carrying the live broker would put every E2E run on this
  machine one `ChannelBridge:Enabled` away from consuming the family group (the July fossil
  shows that flag *does* get written alongside the broker).
- **Independent of which environment name the launcher pins.** The AppHost sets the env var on
  the child directly; it does not matter whether the server is Development or Production. The
  July `appsettings.Production.json` was lost precisely to that coupling.
- **Every launcher reads it.** Logon task, watchdog, `restart-apphost.ps1`, a manual
  `dev-aspire.ps1` — all run `dotnet run` in `Antiphon.AppHost/`, all get the same
  `builder.Configuration`. No flag to remember, no script to patch.
- **It is the existing convention.** `docs/bootstrap.md` already names user-secrets as the
  preferred per-machine overlay and a gitignored `appsettings.Development.json` as the accepted
  one; `.gitignore:5` already hides the latter; the AppHost csproj already carries a
  `UserSecretsId`. Nothing new is introduced — one more key goes into a store that exists.

### Rejected alternatives, with the reason each fails

- **`server/appsettings.Development.json` (or `antiphon-server` user-secrets) as the opt-in.**
  Works for the live stack; leaks into `AntiphonWebAppFactory`/`AntiphonAppFixture` (above).
  Also invisible from the Aspire dashboard, whereas a forwarded env var shows in the server
  resource's environment tab.
- **`dev-aspire.ps1 -LiveTelegram` (card option 3).** Three callers plus a registered Scheduled
  Task would need the flag; the first watchdog restart after anyone forgets one launches local,
  and the failure is silent. The opposite of the constraint this card is built around.
- **An Aspire `AddParameter("messaging-bootstrap", …)` resource.** Same `Parameters:*` user-secret
  store, more machinery (a parameter resource in the dashboard, a default that still has to
  say `localhost:19092` and then *always* sets the env var, so the server's tracked default is
  never what actually applies). A plain `Configuration[...]` read with a conditional
  `WithEnvironment` keeps the tracked server default as the real default.
- **Flip the default to `localhost:19092` in Program.cs and tell operators to edit it.** That is
  the card's failure mode with the sign reversed — the file is still the switch.
- **Keep the line but read it from `Antiphon.AppHost/appsettings.json` (tracked).** Still a
  hostname in source.

## 2. Decision 2 — the migration for this machine, stated as precisely as the code

This is build step **1**, not a follow-up. It is safe to do at any time before the code lands
because nothing on `master` reads the key, and its value equals the hardcoded one.

**M1 — configure the opt-in (before any code is pushed):**

```powershell
cd C:\src\Antiphon
dotnet user-secrets set "AntiphonMessaging:BootstrapServers" "server2:19092" --project Antiphon.AppHost
dotnet user-secrets list --project Antiphon.AppHost
#   expected:  AntiphonMessaging:BootstrapServers = server2:19092
```

(`--id aspire-antiphon-apphost` is equivalent; the file lands under
`%APPDATA%\Microsoft\UserSecrets\aspire-antiphon-apphost\secrets.json`, outside the repo, so a
`git clean -xdf` or a re-clone does not take it with it — the reason it is preferred over the
gitignored json for this machine.)

**M2 — land the code** (Decisions 1, 3, 4, 5) on `master`. The commit message names M1 as done.

**M3 — restart:** `pwsh -File scripts/restart-apphost.ps1`. This is the first moment the deleted
line could have mattered; M1 makes it a no-op change for the server.

**M4 — verify, without touching the family chat:**

1. `logs/apphost.log` contains the Decision-3 line naming `server2:19092` with source
   `AppHost configuration`.
2. `server/logs/antiphon-<today>.log` contains the consumer's start line naming
   `server2:19092`.
3. Aspire dashboard (`http://localhost:17205`) → `server` → Environment shows
   `AntiphonMessaging__BootstrapServers=server2:19092`.
4. Live proof from the broker's side: `ssh mc@server2 'docker exec am-redpanda rpk group describe
   antiphon-server-bridge'` lists one member for `channels.inbound` (the desktop's consumer).
   If SSH stalls on the Tailscale check, the `tailscale-ssh-auth` skill; if the group shows no
   member after 60 s, the server is not on the live broker — go to M5.

**M5 — rollback, either direction, no code:** to go local for a smoke, `dotnet user-secrets remove
"AntiphonMessaging:BootstrapServers" --project Antiphon.AppHost` + restart; to return, M1 + restart.
Family messages that arrive while local wait on am-redpanda behind the committed group offset
(Established facts) and are consumed on return. Code rollback is `git revert` of the M2 commit,
which puts the hardcoded line back — also safe, since it equals the secret.

**What must not happen:** M2 pushed before M1 is confirmed by `dotnet user-secrets list`. The
implementer runs M1 first and quotes the list output in the commit message.

## 3. Decision 3 — observability: both ends say which broker

- **AppHost**, once, after `builder.Build()`: log at Information through the host's logger
  (`app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Antiphon.AppHost")`, the same
  sink `DaemonProcessService` uses so it lands in `logs/apphost.log`):
  `Messaging broker for server: {Broker} ({Source})` where Source is `AppHost configuration
  (per-machine opt-in)` or `server/appsettings.json default`. When live, append the fake-gateway
  caveat: `fake-gateway stays on localhost:19092 and will not reach the server while the live
  broker is selected`. When the default applies, the logged value is the literal
  `localhost:19092` read from nowhere — say `default (server/appsettings.json)` rather than
  pretending to have read the server's file.
- **Server consumer**, `KafkaAntiphonMessagingConsumer.cs:38`: add `{BootstrapServers}` to the
  existing line — `[antiphon] consuming {Topic} as {Group} from {BootstrapServers}`. This fires
  only when the bridge is enabled, which the AppHost forces; that is the line M4.2 greps for.
- No new endpoint, no new incident kind. The value is not a secret; logging it is fine.

## 4. Decision 4 — the docs describe the opt-in, and the guard is a test

Docs to change (each currently says the AppHost forces `server2:19092` or implies commenting):

- `docs/telegram-bot-ops.md:27-31` — replace "the AppHost sets `AntiphonMessaging__BootstrapServers=
  server2:19092` (`Antiphon.AppHost/Program.cs`)" with the opt-in: the user-secret command, the
  gitignored-json alternative, the fact that the fake gateway is then unreachable, and M5's
  rollback.
- `docs/bootstrap.md:112-114` (§4 Postgres and Redpanda): state that a fresh machine is on the
  local Redpanda and needs nothing; add a row to the secrets table (§ "Secrets", `:206-210`):
  `AntiphonMessaging:BootstrapServers` — this machine: `server2:19092` via
  `aspire-antiphon-apphost` user-secrets; new machines: leave unset.
- `docs/messaging/build-your-own-gateway.md` §6 "Test it" (`:221-240`, the local-broker section): one
  sentence — the AppHost defaults to local; if this machine has opted into a live broker, remove
  the secret for the duration of a local smoke.
- `AGENTS.md`: one Gotchas bullet — the AppHost no longer names a broker; the live Family path on
  this machine is the `aspire-antiphon-apphost` user-secret; never put the hostname back in
  `Program.cs`, and never forward it to the fake gateway.
- The `Program.cs` comment in Decision 1 replaces the 2026-07-23 block.

Guard test (`tests/Antiphon.Tests/Infrastructure/AppHostBrokerSourceGuardTests.cs`, reads files
relative to the repo root the way `AntiphonAppFixture.EnsureClientBundleIsCurrent` does):

1. `Antiphon.AppHost/Program.cs` contains no string literal matching `\w+:\d{4,5}` inside a
   `WithEnvironment("AntiphonMessaging__BootstrapServers"` call — the only permitted second
   argument is an identifier.
2. `server/appsettings.json` `AntiphonMessaging:BootstrapServers` is `localhost:19092`, and
   `src/Antiphon.Messaging.FakeGateway/appsettings.json`'s bootstrap is `localhost:19092`.

Cheap, and it pins the card's actual failure mode (a flip-flop getting pushed) rather than the
Aspire wiring, which has no test host in this repo.

## 5. Out of scope, stated

- `ChannelBridge__Enabled=true` forced by the AppHost (`Program.cs:54`) — unchanged. A fresh
  clone gets the bridge on against the local Redpanda, which is the documented offline loop.
- The untracked `server/appsettings.Production.json` on this machine — left alone. It is inert
  under both launch modes; deleting it is a one-line operator choice, not part of this change.
  The plan records it so nobody later mistakes it for the live opt-in.
- Forwarding `ConsumerGroup`/topics, or a second opt-in for the fake gateway — no.
- `Antiphon.Messaging.Service`'s own `Kafka__*` env on server2 — a different process, untouched.

## 6. Verification / test design

- **Unit:** the guard test above (2 tests). `Antiphon.Messaging.Tests` gains nothing — the
  consumer log change is a format string.
- **Live, this machine (M4):** two log lines + dashboard env + `rpk group describe`. No message is
  sent to the family chat to prove the cut-over did not happen.
- **Fresh-clone / fake-gateway acceptance (criteria 1 and 3), verified in a bounded window:**
  M5 remove-secret + restart; confirm the two log lines now say `localhost:19092`; run the
  card's 2026-08-23 round trip (`POST :17208/inbound` → orchestrator → `/deliveries` ACK, per
  `docs/messaging/build-your-own-gateway.md` §6 "Test it"); then M1 + restart and re-run M4. State the
  window's start and end in the task report; Family inbound during it is queued on the broker,
  not lost. If the operator would rather not open the window, the local path is still covered
  by the unchanged tracked defaults plus the guard test, and the report says the round trip was
  not re-run.
- **Regression:** `dotnet run --project tests/Antiphon.Tests --treenode-filter
  "/*/Antiphon.Tests.Infrastructure/*/*" --property:OutputPath=bin-0185/` (delete the
  `bin-0185` directories afterwards).

## 7. Build order

1. **M1** on this machine; paste `dotnet user-secrets list --project Antiphon.AppHost` into the
   task notes. Nothing else starts until this shows the key.
2. `Antiphon.AppHost/Program.cs`: delete `:63-68`, add the Decision-1 read, the conditional
   `WithEnvironment`, and the Decision-3 log line.
3. `KafkaAntiphonMessagingConsumer.cs:38`: add the broker to the start line.
4. Guard test + docs (Decision 4) + `AGENTS.md` bullet.
5. Commit with a message that states M1 was done and quotes the list output; push.
6. **M3 + M4.** If M4.4 fails, M5 back to the hardcoded commit (`git revert`) and report.
7. Optionally the bounded local window of §6, then M1 + M4 again.
