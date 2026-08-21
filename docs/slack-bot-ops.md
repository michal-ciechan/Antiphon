# Slack bot agents — ops

How to create, deploy, and operate a Slack-backed Antiphon agent. The Slack adapter uses **Socket
Mode**, so the messaging service makes outbound HTTPS/WebSocket connections only; it needs no
public webhook, signing-secret endpoint, or inbound TLS route. See [messaging-standalone.md](messaging-standalone.md)
for the gateway deployment shape and [telegram-bot-ops.md](telegram-bot-ops.md) for the sibling
Telegram procedure.

## Create the internal Slack app

In <https://api.slack.com/apps>, choose **Create New App → From an app manifest**, select the
target workspace, and paste this manifest. It deliberately subscribes only to `message.*`: adding
`app_mention` would double-deliver mentions, because a channel mention is already a
`message.channels` event.

```yaml
display_information:
  name: Antiphon
  description: Antiphon channel-backed agents
  background_color: "2c2d30"
features:
  bot_user:
    display_name: Antiphon
    always_online: false
oauth_config:
  scopes:
    bot:
      - chat:write
      - channels:history
      - groups:history
      - im:history
      - mpim:history
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

After creation:

1. Enable Socket Mode and generate an app-level token with `connections:write` (`xapp-…`).
2. Install (or reinstall after a scope change) the app to the workspace and copy its bot token
   (`xoxb-…`).
3. Store both values only in the Bitwarden item **“Antiphon Slack Bot”**. Never commit them,
   place them in an app manifest, or paste them into an agent prompt.
4. Invite the bot to every public or private channel it should hear. The history scopes only permit
   messages from conversations the bot has joined; a user opens a DM by messaging the bot.

The bot scopes are intentional: `chat:write` sends replies; `channels:history`, `groups:history`,
`im:history`, and `mpim:history` receive each conversation kind; `users:read` resolves author
names and mentions; and `files:read`/`files:write` handle attachments. The sole app-level scope is
`connections:write` for Socket Mode.

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

For a source deployment, tar-sync `src/Antiphon.Messaging*` and `Messaging.Pack.props` to
`/home/mc/antiphon-messaging/build/src` on server2, then run:

```bash
docker compose build messaging-service && docker compose up -d messaging-service
```

Re-verify those paths and compose service name on server2 before executing them; they are captured
operator knowledge, not a guarantee about a future host. At deployment time, choose whether Slack
joins the existing family gateway (same persona and bindings) or has a separate gateway with its
own Kafka and Postgres, as described in [messaging-standalone.md](messaging-standalone.md).

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
- Duplicate events indicate a delayed envelope acknowledgement or Slack redelivery. The adapter
  acknowledges before normalization and the bridge deduplicates by channel message id; check logs
  for socket stalls before changing code.
- No messages from a channel: confirm the bot is invited, the matching `message.*` event is in the
  manifest, the app was reinstalled after changes, and `Slack__AllowedConversationIds` includes
  the actual `C…`, `G…`, or `D…` id when configured.
- A bot reply looping back into Antiphon is a severity-one configuration/code issue. Slack echoes
  `chat.postMessage` as an event; the adapter and bridge both suppress messages from the bot.
