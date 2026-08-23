# Antiphon.Messaging.Service

A channel-agnostic messaging bridge: it connects a chat platform (Telegram long-polling or Slack
Socket Mode) and
shuttles messages **to/from Kafka** — inbound messages land on `channels.inbound` (and a
Postgres inbox), replies are consumed from `channels.outbound` and delivered back to the
channel. It also exposes a small REST API (`/api/channels/...`) for listing/replying.

The Kafka ingress and outbound loops come from **`Antiphon.Messaging.Gateway`**
(`AddAntiphonGateway(config, "Kafka")` — the section name is kept so deployed `Kafka__*`
env vars keep working). The inbox and REST API stay in this service.

> **Telegram specifics** — outbound **Markdown → Telegram HTML formatting** (the full mapping,
> parse modes, fallback behaviour), inbound normalization, and every `Telegram__*` setting are
> documented in **[docs/telegram.md](../../docs/telegram.md)**.
> **Slack specifics** — app manifest, scopes, token custody, binding, threading limitation, and
> Socket Mode troubleshooting are in **[docs/slack-bot-ops.md](../../docs/slack-bot-ops.md)**.

## Run it standalone — one instance per bot

The service is designed to run **as many independent instances as you have bots**. Each
instance is fully self-contained: **its own bot token, its own Kafka, and its own Postgres
DB**. Two instances (e.g. an `antiphon` bot and a `school_revision` bot) run side by side
with zero shared state — just point each at different infra.

Published image: **`ghcr.io/michal-ciechan/antiphon-messaging-telegram`**.

The service runs `Database.MigrateAsync()` on startup, so it **creates its own DB + schema**
if the connecting user can create databases — no manual DB setup.

> librdkafka consumers don't auto-create topics. With a **dedicated** Kafka per instance you
> can let them be created on first produce, or create them up front:
> `rpk topic create channels.inbound channels.outbound`.

## Overriding the image's config pointers

All config comes from `appsettings.json` (defaults below) and is overridable via environment
variables using the standard .NET `__` (double-underscore) convention. These are the knobs you
change per instance:

| Env var (override)            | Section / default                     | Purpose |
| ----------------------------- | ------------------------------------- | ------- |
| `Telegram__BotToken`          | `Telegram:BotToken` (empty)           | **Required.** The bot's BotFather token — what makes it *that* bot. |
| `Telegram__BotUsername`       | `Telegram:BotUsername`                | The bot's @username (display/identity). |
| `Telegram__AllowedChatIds__0` | `Telegram:AllowedChatIds` (`[]` = all)| Optional allowlist of chat ids. |
| `Telegram__ApiBaseUrl`        | `https://api.telegram.org`            | Telegram API base. |
| `Slack__BotToken`             | `Slack:BotToken` (empty)              | **Required for Slack.** Installed bot token (`xoxb-…`). |
| `Slack__AppToken`             | `Slack:AppToken` (empty)              | **Required for Slack.** Socket Mode app token (`xapp-…`, `connections:write`). |
| `Slack__AllowedConversationIds__0` | `Slack:AllowedConversationIds` (`[]` = all) | Optional Slack `C…`/`G…`/`D…` allowlist. |
| `Slack__BotUserId`            | `Slack:BotUserId` (resolved)          | Optional bot user id; otherwise discovered with `auth.test`. |
| `Slack__ApiBaseUrl`           | `https://slack.com/api`               | Slack Web API base (fake/test override). |
| `Kafka__BootstrapServers`     | `Kafka:BootstrapServers` (`localhost:19092`) | Point at *this instance's* broker, e.g. `redpanda:9092`. |
| `Kafka__InboundTopic`         | `channels.inbound`                    | Inbound topic. |
| `Kafka__OutboundTopic`        | `channels.outbound`                   | Outbound topic. |
| `Kafka__ConsumerGroup`        | `antiphon-messaging-service`          | **Set a per-instance group** if instances ever share a broker, so they don't steal each other's messages. |
| `ConnectionStrings__Messaging`| `Host=localhost;...Database=antiphon_messaging;...` | Postgres for the inbox. Give each instance **its own database**. |

## Example: the `school_revision` instance (server2, in the school-revision compose)

```yaml
  messaging-redpanda:                                  # this instance's own Kafka
    image: docker.redpanda.com/redpandadata/redpanda:latest   # see note below
    command: [redpanda, start, "--kafka-addr=internal://0.0.0.0:9092",
              "--advertise-kafka-addr=internal://messaging-redpanda:9092",
              "--mode=dev-container", "--smp=1"]
    expose: ["9092"]

  antiphon-messaging-telegram:                         # the bridge, school_revision instance
    image: ghcr.io/michal-ciechan/antiphon-messaging-telegram:latest
    environment:
      Telegram__BotToken: "${TELEGRAM_BOT_TOKEN}"      # school_revision_bot
      Telegram__BotUsername: "school_revision_bot"
      # A Slack-capable instance additionally needs both tokens:
      # Slack__BotToken: "${SLACK_BOT_TOKEN}"
      # Slack__AppToken: "${SLACK_APP_TOKEN}"
      Kafka__BootstrapServers: "messaging-redpanda:9092"
      Kafka__ConsumerGroup: "school-revision-messaging-service"
      ConnectionStrings__Messaging: "Host=postgres;Port=5432;Database=school_revision_messaging;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
    depends_on: [postgres, messaging-redpanda]
```

Then `rpk topic create channels.inbound channels.outbound` once, and the consumers attach.
A second instance (e.g. `antiphon_messaging_telegram_bot`) is the same block with a different
token/username, its own Redpanda, and its own DB.

> **⚠️ Redpanda version:** use a **current** Redpanda. The `Confluent.Kafka` 2.6.0 client uses
> **Fetch API v12**; older brokers (e.g. `v24.2.7`) reject it — the *producer* still works but
> *consumers* loop with `Disconnected … after 1ms in state UP` / broker-side
> `Unsupported version 12 for fetch API`, so the inbox stays empty. `:latest` (v26.x) works.

## Build the image

```bash
# context = src/ (the Antiphon.Messaging* projects are siblings)
docker build -f src/Antiphon.Messaging.Service/Dockerfile -t ghcr.io/michal-ciechan/antiphon-messaging-telegram:latest src/
```
