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
  `oauth.v2.access`, no bot-token rotation, no `.env` edit, and **nothing on server2 touched** —
  which matters, because the Slack adapter shares one `am-service` process with the live Telegram
  gateway, so an unnecessary reinstall would have risked the Family and AZ Care conversations for
  nothing. `features.app_home` is not an OAuth scope change; treat reinstall as a fallback that was
  not needed, not as a step.
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

## Configure and deploy the gateway

The same `Antiphon.Messaging.Service` image can register Telegram, Slack, or both. It registers an
adapter only when its bot token is configured, and logs a warning if neither adapter is present.
Add these environment values to the chosen server2 compose instance after the app exists:

```yaml
Slack__BotToken: "${SLACK_BOT_TOKEN}"       # xoxb-… from Bitwarden
Slack__AppToken: "${SLACK_APP_TOKEN}"       # xapp-… with connections:write
# Optional defence in depth; absent means conversations the bot joined are accepted.
Slack__AllowedConversationIds__0: "C0123456789"
```

**Deployed 2026-08-21 (CARD-0107): Slack joins the existing `family` gateway** — the same
`am-service` container that carries the live Family and AZ Care Telegram conversations, in compose
project `antiphon-messaging` at `/home/mc/antiphon-messaging` on server2. It was *not* given a
separate gateway: one process now registers both adapters, sharing that instance's `am-redpanda`
and `am-postgres`. The full, re-verified tar-sync + build + rollback procedure lives in
[telegram-bot-ops.md](telegram-bot-ops.md#deploying-the-messaging-service-server2) — it is the same
gateway, so there is one copy of those steps, not two.

Both tokens are supplied the same way the Telegram one already was: compose interpolates
`${SLACK_BOT_TOKEN}` / `${SLACK_APP_TOKEN}` from the mode-600 `.env` beside `docker-compose.yml`.
Nothing is inline in the compose file, and no token value need ever be printed to add one:

```bash
# check interpolation WITHOUT revealing values — lengths only (xoxb- is 56, xapp- is 98)
ssh mc@server2 'cd /home/mc/antiphon-messaging && docker compose config' \
  | grep -E 'Slack__|Telegram__Bot' | awk -F': ' '{gsub(/"/,"",$2); print $1": len=" length($2)}'
```

Because the two adapters share one process, **a Slack deploy restarts the live Telegram gateway.**
Verify Telegram in both directions afterwards, per that doc's verify step — do not stop at "it
built".

Startup should log one `[ingress] starting channel …` line per adapter, then the Socket Mode
handshake. The authoritative registration check is `curl -s localhost:18090/api/channels`, which
lists each adapter's capabilities — prefer it over the startup log line.

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
- **Never run two adapters on the same app token at once** — e.g. a local instance left running
  while you deploy to server2. Slack accepts multiple Socket Mode connections per app and
  *load-balances* events across them, so roughly half the messages vanish into whichever process
  you weren't watching. There is no error and both sockets look healthy; the only symptom is
  intermittently missing inbound. Stop the local one before deploying (this is why the local
  pre-deploy check in CARD-0107 was torn down before the server2 build).
- Duplicate events indicate a delayed envelope acknowledgement or Slack redelivery. The adapter
  acknowledges before normalization and the bridge deduplicates by channel message id; check logs
  for socket stalls before changing code.
- No messages from a channel: confirm the bot is invited, the matching `message.*` event is in the
  manifest, the app was reinstalled after changes, and `Slack__AllowedConversationIds` includes
  the actual `C…`, `G…`, or `D…` id when configured.
- A bot reply looping back into Antiphon is a severity-one configuration/code issue. Slack echoes
  `chat.postMessage` as an event; the adapter and bridge both suppress messages from the bot.
