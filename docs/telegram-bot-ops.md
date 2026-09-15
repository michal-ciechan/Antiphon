# Telegram bot agents — ops

How to stand up (and verify) a Telegram-bot-backed agent. Spec:
[2026-07-21-telegram-bot-agents.md](superpowers/specs/2026-07-21-telegram-bot-agents.md) ·
plan: [2026-07-21-telegram-bot-agents-plan.md](superpowers/plans/2026-07-21-telegram-bot-agents-plan.md) ·
workspace convention: [agent-workspaces.md](agent-workspaces.md).
Slack uses the same gateway shape but its own app manifest and Socket Mode procedure; see
[slack-bot-ops.md](slack-bot-ops.md).

For human Telegram/Slack replies, choose `Phone` explicitly through Reply style; the
preamble preset changes append text only. Preserve existing append bytes and other
settings when changing a live style. Follow the preserving PATCH, idle refresh and
actual loaded-stamp procedure in [CARD-0417](superpowers/plans/2026-09-07-card-0417-channel-reply-conciseness-plan.md).
Notify-only refresh is not activation. If the canary's `PolicyRefreshMode` is `Notify` or
`Off`, POST `/refresh-policy` will not load Phone (Notify posts a message; Off is 409
`not_resumable` even with force). Temporarily switch that row to Auto or Relaunch with a
preserving body, refresh at an idle boundary, then restore the intended mode after the
`style-phone v…` stamp is loaded. Review one real canary before expanding to other
intended enabled bindings. V-10 reviewer of record is the operator (Mike); a Code or
Review delegate does not substitute that live wording verdict. Roll back by restoring
its recorded old style and verifying that style is loaded; reset all Phone rows before
downgrading to a binary without Phone.

## Per-bot deployment model

Use one `Antiphon.Messaging.Service` instance per bot token (bot name = persona), with
independent broker and database state as described in [messaging-standalone.md](messaging-standalone.md).
The server defaults to local Redpanda at `localhost:19092`. Live gateways are provisioned
per machine and remain outside AppHost. CARD-0496 records a
[desktop Slack sidecar](slack-bot-ops.md#desktop-slack-sidecar) using that local broker;
that sidecar is not evidence of Telegram support or a configured Telegram gateway.

For an intentional broker override, first confirm the chosen gateway's effective broker.
Replace both placeholders before using this optional example:

```powershell
dotnet user-secrets set "AntiphonMessaging:BootstrapServers" "<confirmed-broker-host>:<port>" --project Antiphon.AppHost
```

The AppHost user-secrets id is `aspire-antiphon-apphost`. A gitignored
`Antiphon.AppHost/appsettings.Development.json` with the same key is also accepted.
AppHost forwards a nonblank trimmed value only to the server as
`AntiphonMessaging__BootstrapServers`; FakeGateway configuration is unchanged.
The two are separated only when their effective brokers differ. Local traffic can be live;
removing an override does not establish test isolation. Synthetic tests require a broker
with no real gateway attached.

For an optional remote Compose instance, keep tokens in its protected environment source
(for example a mode-600 `.env`), never inline in tracked Compose or logs. Configure and verify
that instance's `Telegram__AllowedChatIds`; an empty list accepts every chat the bot is in.
The dated [Slack deployment](superpowers/plans/2026-08-20-card-0107-slack-channel-plan.md) and
[broker opt-in](superpowers/plans/2026-08-25-card-0185-apphost-broker-opt-in-plan.md) record
historical deployments, not this machine's current inventory.

**Known limitation (accepted):** `ChatChannel` is keyed `(Provider, ExternalId)` — two bots
joined to the SAME Telegram group would collide on one channel row and route to whichever
agent that row is bound to. Policy: one bot per group. Future fix: a `BotId` discriminator
column.

## Deploying an optional remote messaging service

Select and confirm an SSH destination explicitly. The helper supports only the existing
`/home/mc/antiphon-messaging` layout, source context `build/src`, Compose service
`messaging-service`, and container `am-service` (host port 18090). It is not a desktop
sidecar tool or a general Compose deployer. There is no default target or environment fallback.
Use one `user@host` (SSH alias, DNS hostname, or IPv4; no port suffix or custom arguments).
Replace `<user>@<confirmed-host>` in both examples before use:

```powershell
# Read-only preflight: validates the Dockerfile-derived archive manifest and the remote Compose build contract.
pwsh -NoProfile -File scripts/deploy-am-service.ps1 -SshTarget '<user>@<confirmed-host>'

# Production only after explicit authorization. -Confirm gives the final interactive confirmation.
pwsh -NoProfile -File scripts/deploy-am-service.ps1 -SshTarget '<user>@<confirmed-host>' -Deploy -Confirm
```

The script derives its archive entries from local
`src/Antiphon.Messaging.Service/Dockerfile` `COPY` sources. There is intentionally no manually
maintained project/tar list: a source dependency added to the Dockerfile enters the archive, while
unsupported `COPY` syntax refuses before upload. It validates the remote Compose build context and
Dockerfile before every write, replaces rather than overlays `build/src`, and retains the exact
`build/src.bak-*` path as rollback evidence. Read its final `REMOTE DEPLOY VERDICT` line; do not
reconstruct SSH, tar, or Compose commands by hand. It never prints Compose environment values,
tokens, or arbitrary logs.

For this supported remote layout, Kafka topics `channels.inbound` / `channels.outbound`
use `max.message.bytes=20971520`. Historical contracts:
[remote deploy helper](superpowers/plans/2026-08-31-card-0270-am-service-remote-deploy-plan.md) and
[consumer identity](superpowers/plans/2026-09-06-card-0410-gateway-consumer-group-plan.md).

The script technically verifies the running container, registered adapters, migration history, and
a bounded redacted startup-log scan on the selected instance. With separate traffic authorization,
confirm that instance serves the designated test group, then prove both directions. Send to the
**`Antiphon-Family` test group
(`-5370465377`)**, never the live `Family` group: `dotnet run scripts/tg-send.cs -- --to
-5370465377 --text "..."` from `C:\src\ClaudeBot` should log `[outbound] sent via telegram -> <id>`,
and a reply typed back into that group should log `[ingress] telegram -5370465377 -> channels.inbound`.

## Configure the agent (Channels + Agents pages, or API)

1. Agent row: `WorkingDirectory = C:\src\ClaudeBot\agents\<name>` (workspace must contain
   CLAUDE.md/SOUL.md — see agent-workspaces.md), **Always on** = on for a bot persona,
   **System prompt (appended)** = the Telegram preset ("Use Telegram preset" button;
   `GET /api/agents/preamble-preset?provider=telegram`). Clearing the preamble disables the
   bootstrap/restart/compaction notes for that agent — the per-agent kill-switch.
2. Send one message to the bot so the channel row appears, then on the Channels page bind it
   to the agent and enable routing (`PATCH /api/channels/{id}` — binding lives in
   `ChatChannelService.UpdateAsync`).
3. Optional: point an admin group's **Alerts** at Warning+ so supervision incidents land next
   to the conversation.

## Behaviour knobs (`ChannelBridge` section)

| Key | Default | Meaning / kill-switch |
|---|---|---|
| `Enabled` | false (dev AppHost forces true) | Bridge consumes `channels.inbound` at all |
| `DebounceWindowMs` | 500 | Same-sender merge window; **0 = passthrough (kill-switch)** |
| `DebounceMaxMs` | 2000 | Hard cap from first buffered message |
| `BatchingEnabled` | true | Coalesce same-conversation runs at turn end; **false = one-per-turn (kill-switch)** |

## Dev-stack smoke (fake gateway, no Telegram)

Prereqs: the canonical Aspire stack is up (server 17202, fake gateway 17208), both use
the same isolated test broker, and no real gateway is attached to that broker. A local broker
or an unset override alone does not prove isolation. Bind a test agent to a test channel
with a preamble set; re-run after changes touching the bridge/queue/dispatcher:

1. **Batching path** — three rapid inbounds from one sender:
   ```powershell
   1..3 | ForEach-Object {
     Invoke-RestMethod -Method Post http://localhost:17208/inbound -ContentType application/json `
       -Body (@{ chatId = '<boundChatId>'; text = "smoke msg $_"; username = 'mike' } | ConvertTo-Json)
   }
   ```
   Expect: ONE new turn in the session transcript (debounce merges within the window; if the
   sends straddle the window, the queue batches under the context/current markers instead) and
   exactly ONE reply in `GET http://localhost:17208/deliveries?since=<t>`.
2. **Compaction path** — queue `/compact` into the session
   (`POST /api/sessions/{id}/messages {"body":"/compact","mode":"WhenIdle"}`), then:
   - `GET /api/agents/{id}/incidents` gains a `ContextCompacted` (Info) row, NO alert;
   - the recovery note turn produces NO delivery (`NO_REPLY` honoured — check /deliveries
     did not grow).
3. **Restart note** — restart the agent (Stop → Start): the session resumes, the restart
   note turn sends nothing to the chat, and the launch args carry `--append-system-prompt`
   (visible in the session's pty-host manifest / audit).

## Transcript tailer discovers Claude's forked session id (2026-07-22, RESOLVED)

Reply routing (agent's turn → `ChannelReplyDispatcher` → `channels.outbound` → gateway) is driven
by the **transcript tailer**. Interactive Claude does **not** reliably honour `--session-id`: the
runner-spawned agent writes its conversation to a self-chosen `<uuid>.jsonl` instead of the id
Antiphon passed. Investigation (2026-07-22): the launch command line is correct (`--session-id`
present, read live off `claude.exe`); it is not `--append-system-prompt` (a headed diagnostic
honoured the id with the flag); and the live process env has **no** `CLAUDE_CODE_SESSION_ID` /
`CLAUDE_CODE_CHILD_SESSION` (read via PEB) — so it is neither an arg nor a nesting-marker bug, but a
Claude interactive-mode behaviour we don't control.

**Fix:** `TranscriptTailer.LocateAsync` now prefers `<session-id>.jsonl` but, after a 10 s grace,
falls back to discovering the real transcript by its `cwd` field — the newest transcript whose
recorded cwd matches this session's, preferring one that appeared after the tailer started (a fresh
fork) and, on re-adoption, the newest cwd match overall. **Verified live end-to-end** the same day:
inbound → agent → **PONG reply delivered to `channels.outbound` and recorded by the gateway**.

Limitation: two agents sharing one working directory could be ambiguous under the fallback (the
exact-id fast path is unaffected). The workspace model gives each agent its own cwd, so this is not
a concern in practice. `AgentRegistry` still scrubs the nesting markers (harmless/defensive).

## Migrations note (2026-07-22)

`AddAgentSystemPromptAppend`, `AddCompactionRecoveryWatermark`, and `AddQueuedMessageOrigin`
are hand-written (no Designer.cs) because the running dev server locked `bin/`. The snapshot
was updated by hand and must be verified with
`dotnet ef migrations has-pending-model-changes --project server` (expect "no changes")
whenever the server is stopped — done at the 2026-07-22 restart.

## Consumer-group deployment verification (CARD-0410)

Changing the server's `AntiphonMessaging:ConsumerGroup` (beside the default in
`server/appsettings.json`) requires a coordinated gateway redeployment. The server's
read-only `GET /api/channels/consumer` reports effective consumerGroup, inboundTopic,
enabled, and broker host/port pairs. `/api/version` provides separate build provenance.
Deploy the server endpoint first, with operator authorization; a missing endpoint refuses
rather than falling back to a guessed group.

The existing deploy script reads `ANTIPHON_API` (default `http://localhost:17202`) and
`ANTIPHON_TASK_TOKEN`, previews repository/server/gateway/proposed groups, and verifies
broker cluster identity through read-only metadata on both listeners. Internal/external
listener strings may differ. Metadata must identify the same cluster; an unavailable
listener or missing identity refuses. The rpk JSON field `cluster_name` contains Kafka's
cluster identity ([rpk implementation](https://github.com/redpanda-data/redpanda/blob/v25.3.4/src/go/rpk/pkg/cli/cluster/info.go)).

Only the existing `-Deploy` and ShouldProcess gate writes. It persists two environment keys
in the ordinary Compose override matching the base filename (`docker-compose.yml` becomes
`docker-compose.override.yml`; `compose.yaml` becomes `compose.override.yaml`). Ambiguous
base/override names and unmarked existing overrides refuse. Marked overrides and source
are backed up and their exact paths reported. Subsequent ordinary `docker compose build`
and `docker compose up` from the deployment directory load the same persisted settings.
No secret `.env` is edited. Never bypass refusal by hand-overlaying remote source.

Technical verification requires the new readiness route, HTTP 200/Ready, matching groups
and topic, ageSeconds at most 130, numeric commits on every sampled partition, matching
merged Compose settings, a changed running image ID, and independent read-only
`rpk group describe` offsets. An advancing offset need not equal an earlier observation.
The server identity is reread before mutation and after verification; concurrent renames
fail visibly. Adapter/migration checks and redacted startup-log checks remain in place.

Production deployment and real traffic are separately authorized. After rollout, use only
the Antiphon-Family test group (`-5370465377`), record one receipt's partition/offset and
reply, verify the commit moved past it, and observe for age threshold plus two polls
(normally seven minutes). No false notice/event should occur for that receipt. A technical
verdict without that traffic observation is not end-to-end acceptance.

Rollback uses retained backups only by operator decision. Keep the corrected group when
restoring old binaries, or explicitly disable only the lag monitor. Never restore the
obsolete group automatically, reset Kafka offsets, or reopen historical receipt watermarks.
