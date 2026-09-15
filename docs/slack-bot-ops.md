# Slack bot agents — ops

How to create, deploy, and operate a Slack-backed Antiphon agent. The Slack adapter uses **Socket
Mode**, so the messaging service makes outbound HTTPS/WebSocket connections only; it needs no
public webhook, signing-secret endpoint, or inbound TLS route. See [messaging-standalone.md](messaging-standalone.md)
for the gateway deployment shape and [telegram-bot-ops.md](telegram-bot-ops.md) for the sibling
Telegram procedure.

## Create the internal Slack app

**"Create New App → From an app manifest" is broken — do not use it.** Verified live 2026-08-21:
clicking Create on that wizard's review step sends no request to Slack's API at all (confirmed via
`Network.enable` — zero requests fired for any Slack endpoint around the click), and silently
returns to the empty "Your Apps" screen with nothing created. There is no error shown; it just does
nothing.

**The working path**: **Create New App → Blank app** (name it, pick the target workspace, Create —
this one genuinely calls `apps.manifest.create` and works), then open the newly-created app's own
**App Manifest** page (left sidebar) and paste the manifest below there instead. That editor runs
real client-side lint and shows actual errors, rather than the wizard's silent no-op.

It deliberately subscribes only to `message.*`: adding `app_mention` would double-deliver mentions,
because a channel mention is already a `message.channels` event.

```yaml
display_information:
  name: Antiphon
  description: Antiphon channel-backed agents
  background_color: "#2c2d30"
features:
  app_home:
    home_tab_enabled: false
    messages_tab_enabled: true
    messages_tab_read_only_enabled: false
  bot_user:
    display_name: Antiphon
    always_online: false
oauth_config:
  scopes:
    bot:
      - chat:write
      - channels:history
      - channels:read
      - groups:history
      - groups:read
      - im:history
      - im:read
      - mpim:history
      - mpim:read
      - users:read
      - files:read
      - files:write
settings:
  socket_mode_enabled: true
  token_rotation_enabled: false
  event_subscriptions:
    bot_events:
      - message.channels
      - message.groups
      - message.im
      - message.mpim
  org_deploy_enabled: false
```

`background_color` needs the `#` prefix — Slack's manifest validator rejects a bare hex triplet
with "The app card color has an invalid format" (this doc had it wrong until 2026-08-21; caught by
the App Manifest editor's own lint when the wizard-based creation above was replaced).

If automating this creation flow again (browser-harness or similar), read
`C:\src\claudebot\sites\api.slack.com.md` first — it has the exact working click sequence, the
cookie-consent-banner and viewport-size traps, and the `aria-disabled`/synthetic-vs-trusted-click
gotcha that made the broken wizard look like an input problem rather than Slack's own bug.

After creation:

1. Enable Socket Mode and generate an app-level token with `connections:write` (`xapp-…`).
2. Install (or reinstall after a scope change) the app to the workspace and copy its bot token
   (`xoxb-…`).
3. Store both values only in the Bitwarden item **“Antiphon Slack Bot”**. Never commit them,
   place them in an app manifest, or paste them into an agent prompt.
4. Invite the bot to every public or private channel it should hear. The history scopes only permit
   messages from conversations the bot has joined.

### DMs: the `app_home` block above is what enables them — and Save alone is enough

**Measured live 2026-08-21 (CARD-0107 found it, CARD-0119 fixed it).** Without
`features.app_home`, opening the bot's DM in Slack shows **"Sending messages to this app has been
turned off."** and renders *no composer at all*. Scopes are not the problem — `im:history` and
`message.im` are both present. The blocker is the App Home **Messages tab**, and specifically the
**`messages_tab_read_only_enabled: false`** line: a read-only Messages tab still renders no
composer. That is why channels round-tripped green while DMs stayed impossible — the two surfaces
fail independently.

Three facts worth having before you touch this again, all measured on app `A0BRR9DS9QV`:

- **No reinstall is required. Saving the manifest is the whole fix.** CARD-0119 pasted the
  `app_home` block into the **App Manifest** editor, clicked Save Changes ("Your changes have been
  successfully saved."), reloaded the Slack client, and the DM composer was there. No
  `oauth.v2.access`, no bot-token rotation, no `.env` edit, and no gateway deployment.
  This is an app-configuration fix, not evidence of today's gateway topology. If Slack and
  Telegram share an instance, restarting it affects both. `features.app_home` is not an OAuth
  scope change; reinstall was not needed in this observation. See the dated
  [Slack DM plan](superpowers/plans/2026-08-21-card-0119-slack-dm-plan.md).
- **A missing `app_home` block in the manifest is NOT evidence the tab is off.** Slack omits the
  block entirely when it has never been set, while the underlying toggles still have values. Read
  the real state on **App Home** instead — the checkboxes are `#message_tab_toggle` and
  `#message_tab_read_only_toggle`. On this app `message_tab_toggle` was **already on**; the one
  that was off was `message_tab_read_only_toggle`, whose visible label is the *inverse* of its id:
  "Allow users to send Slash commands and messages from the messages tab". Unticked = read-only =
  no composer.
- **The dashboard path is equivalent** — App Home → Show Tabs → Messages Tab, plus that "Allow
  users to send…" tick. Saving the manifest flips the same two checkboxes; either is fine.

A DM's Antiphon channel row has a **null `Title`** (`conversations.info` returns no `name` for an
IM), so the Channels UI shows the raw `D…` id. Identify a DM row by its `externalId` prefix, never
by title. Its `kind` is `Direct`.

The bot scopes are intentional: `chat:write` sends replies; `channels:history`, `groups:history`,
`im:history`, and `mpim:history` receive each conversation kind; `users:read` resolves author
names and mentions; `channels:read`, `groups:read`, `im:read`, and `mpim:read` resolve
conversation titles with `conversations.info`; and `files:read`/`files:write` handle attachments.
The sole app-level scope is `connections:write` for Socket Mode.

## Desktop Slack sidecar

CARD-0496 records the desktop setup on **2026-09-12**: `MikeysBotSlackGateway` uses local
Redpanda at `localhost:19092`, managed separately by the Scheduled Task
**`Antiphon MikeysBot Slack Gateway`**. This is an operator-recorded setup, not an installer
contract or a claim that the task is installed or running on every clone. The planning
environment did not have a matching task. This Slack sidecar does not establish Telegram support.

AppHost owns FakeGateway, not the real Slack/Telegram gateways. Its health or restart does
not prove sidecar health or restart the sidecar. `scripts/install-autostart.ps1` does not
provision this task. Begin with read-only discovery:

```powershell
Get-ScheduledTask -TaskName 'Antiphon MikeysBot Slack Gateway' |
    Select-Object TaskName, State
Get-ScheduledTaskInfo -TaskName 'Antiphon MikeysBot Slack Gateway' |
    Select-Object LastRunTime, LastTaskResult, NextRunTime, NumberOfMissedRuns
```

If the task is absent, this machine has not been shown to have that setup. Consult the
operator-provided sidecar provisioning details; do not fall through to SSH or invent a
launcher. If present, `Running` proves neither broker nor Slack connectivity. Check the
configured gateway's known health/log location and effective broker using its provisioning
details. Do not dump task arguments, tokens, Compose environments, or all user-secrets.
Diagnose broker connectivity and the Slack Socket Mode connection independently before
requesting a sidecar restart through its actual lifecycle owner.

The server also defaults to `localhost:19092`; this local path needs no remote override.
An intentional broker override belongs in AppHost user-secrets (`aspire-antiphon-apphost`)
or its gitignored development overlay and is forwarded only to the server. FakeGateway's
configuration is unchanged. Synthetic tests require a broker with no real gateway attached;
removing an override does not establish isolation.

## Configure and deploy an optional remote gateway

The same `Antiphon.Messaging.Service` image can register Telegram, Slack, or both. It registers an
adapter only when its bot token is configured, and logs a warning if neither adapter is present.
Add these environment values to the explicitly chosen Compose instance after the app exists:

```yaml
Slack__BotToken: "${SLACK_BOT_TOKEN}"       # xoxb-… from Bitwarden
Slack__AppToken: "${SLACK_APP_TOKEN}"       # xapp-… with connections:write
# Optional defence in depth; absent means conversations the bot joined are accepted.
Slack__AllowedConversationIds__0: "C0123456789"
```

The [August Slack deployment](superpowers/plans/2026-08-20-card-0107-slack-channel-plan.md)
records a historical shared instance. Confirm today's adapters and lifecycle for the selected
machine. The supported remote helper is restricted to `/home/mc/antiphon-messaging`,
`build/src`, service `messaging-service`, and container `am-service`; it is not the desktop
sidecar's deploy tool. Follow
[remote deployment operations](telegram-bot-ops.md#deploying-an-optional-remote-messaging-service).
Replace `<user>@<confirmed-host>` before this read-only preflight (there is no default target):

```powershell
pwsh -NoProfile -File scripts/deploy-am-service.ps1 -SshTarget '<user>@<confirmed-host>'
```

Compose interpolates
`${SLACK_BOT_TOKEN}` / `${SLACK_APP_TOKEN}` from the mode-600 `.env` beside `docker-compose.yml`.
Nothing is inline in the tracked Compose file. Use the helper's safe projections; never dump
the Compose environment or token values for diagnosis.

If both adapters share the chosen process, a Slack deploy also restarts its Telegram adapter.
With separate traffic authorization, verify each configured provider in both directions
afterwards using its designated test conversation.

Startup should log one `[ingress] starting channel …` line per adapter, then the Socket Mode
handshake. For that supported remote layout, `/api/channels` on the selected host's port 18090
lists registered adapters and capabilities; the helper checks it. This is not a sidecar health
port or proof of message delivery.

## Bind a Slack conversation to an agent

1. Send a message in the invited test channel or DM; the gateway creates the channel row.
2. On Antiphon’s Channels page, find the `slack` row and bind it to the target agent
   (`PATCH /api/channels/{id}`; binding is `ChatChannelService.UpdateAsync`).
3. Set that agent’s **System prompt (appended)** to the **Slack preset**. The preset is also
   available at `GET /api/agents/preamble-preset?provider=slack`.
4. Enable routing, then perform the live smoke: send a Slack message, confirm its row and
   transcript envelope, and confirm the reply appears in the originating thread.

Bind the channel before enabling an always-on agent so unbound inbound traffic cannot be mistaken
for a routing failure.

A DM is **one continuous history**, and it is worth knowing why rather than re-testing it each time.
`ChatChannelService.UpsertFromInboundAsync` looks a row up by `(Provider, ExternalId)` and inserts
only on a miss, and that pair carries a **unique index**, so a duplicate row is a database error
rather than silent fragmentation. Slack keeps a `D...` IM id for the life of the (user, bot-user)
relationship, and a reinstall preserves the bot user, so the id survives both. Measured end to end
on 2026-08-21 (CARD-0119): three DMs across eight minutes, with the conversation **closed and
reopened** in the Slack client in between, produced exactly one row (`D0BRT8UJCPQ`, kind `Direct`)
whose `Id` and `CreatedAt` never moved while `MessageCount` climbed 1 -> 2 -> 3, and the two routed
DMs landed in **one** transcript under a single `AgentSessionId` - the agent correctly recalled the
earlier DM when asked. Pinned by
`ChannelBridgeTests.A_second_distinct_message_on_the_same_conversation_reuses_the_row`. The one
thing that legitimately starts a new transcript is the **agent's session** restarting; the channel
row is untouched by that, so check `persistentSessionId` before calling it fragmentation.

## Threading limitation

A Slack thread stays in its parent channel’s Antiphon conversation. The adapter carries the
message’s `thread_ts` in the opaque reply handle, so a normal reply goes to that thread.

`ChatChannel.ReplyHandle` retains only the **latest** inbound handle for the channel. If messages
arrive in two threads at once and the response for thread A is dispatched after a newer message in
thread B, the response can land in B. This is accepted for v1. The named future fix is to persist
the inbound reply handle on each `SessionQueuedMessage` row (the CARD-0067 surface), rather than
resolve it from the mutable channel row.

## Socket Mode troubleshooting

- `invalid_auth` or a connection that never opens: check that `Slack__AppToken` is an `xapp-…`
  token carrying `connections:write`, and `Slack__BotToken` is the installed app’s `xoxb-…` token.
- Repeated `disconnect` envelopes are normal Slack connection rotation. The adapter acknowledges
  envelopes immediately and opens a fresh socket; investigate only if reconnects do not settle.
- **Never run two adapters on the same app token at once** — e.g. an old instance left running
  while you deploy a replacement. Slack accepts multiple Socket Mode connections per app and
  *load-balances* events across them, so roughly half the messages vanish into whichever process
  you weren't watching. There is no error and both sockets look healthy; the only symptom is
  intermittently missing inbound. Establish ownership of both instances and coordinate the
  old instance's shutdown with the authorized replacement; location alone does not identify
  which instance should stop.
- Duplicate events indicate a delayed envelope acknowledgement or Slack redelivery. The adapter
  acknowledges before normalization and the bridge deduplicates by channel message id; check logs
  for socket stalls before changing code.
- No messages from a channel: confirm the bot is invited, the matching `message.*` event is in the
  manifest, the app was reinstalled after changes, and `Slack__AllowedConversationIds` includes
  the actual `C…`, `G…`, or `D…` id when configured.
- A bot reply looping back into Antiphon is a severity-one configuration/code issue. Slack echoes
  `chat.postMessage` as an event; the adapter and bridge both suppress messages from the bot.
