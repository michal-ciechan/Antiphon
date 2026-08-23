# Messaging service — standalone, multi-instance

`Antiphon.Messaging.Service` (the channel↔Kafka bridge) runs **standalone**, **one instance
per bot**. Each instance is self-contained — **its own bot token, its own Kafka, its own
Postgres DB** — so you can run several side by side (e.g. an `antiphon` bot and a
`school_revision` bot) with no shared state.

- Published image: `ghcr.io/michal-ciechan/antiphon-messaging-telegram`.
- Override the image's pointers per instance via env (`Telegram__BotToken`,
  `Telegram__BotUsername`, `Slack__BotToken`, `Slack__AppToken`, `Kafka__BootstrapServers`, `Kafka__ConsumerGroup`,
  `ConnectionStrings__Messaging`, …).
- The service migrates its own DB on startup; create the Kafka topics once
  (`rpk topic create channels.inbound channels.outbound`).

Full table + a compose example: **[src/Antiphon.Messaging.Service/README.md](../src/Antiphon.Messaging.Service/README.md)**.
Slack setup, manifest, and Socket Mode diagnostics: **[slack-bot-ops.md](slack-bot-ops.md)**.

**Live (both verified on server2, 2026-08-21):**

- The `family` instance — the one Antiphon itself uses — runs in compose project
  `antiphon-messaging` (`/home/mc/antiphon-messaging`): service `messaging-service`, container
  **`am-service`** on host port 18090, **built from source** (`build: ./build/src`) rather than from
  the published image, with its own `am-redpanda` (external listener `100.93.77.126:19092`),
  `am-postgres` (db `antiphon_messaging`) and `am-console` (18080). Since CARD-0107 this single
  instance registers **both** the Telegram and Slack adapters — one process, two channels. Ops
  procedure: [telegram-bot-ops.md](telegram-bot-ops.md).
- The `school_revision` instance runs on server2 inside the school-revision compose
  (`~/docker/schoolrevision`) — service `antiphon-messaging-telegram` + its own `messaging-redpanda`
  + DB `school_revision_messaging`, using `school_revision_bot`.

The two share nothing — separate brokers, separate databases, separate images. "One instance per
bot" is about the *bot token*, not about the channel: a single instance happily serves several
providers at once, as `am-service` now does.

## Fake gateway (local dev / integration tests)

`src/Antiphon.Messaging.FakeGateway` (NuGet: `Antiphon.Messaging.FakeGateway`, dotnet tool
`antiphon-fake-gateway`) is a Kafka-connected stand-in for this service: real broker semantics,
no real Telegram. The AppHost runs it on **http://localhost:17208** in the dev stack; deployed
environments run only the real gateway.

- Hosts `FakeChannelAdapter` (telegram + slack) on `Antiphon.Messaging.Gateway`. Ingress
  produces `channels.inbound`; outbound consumes `channels.outbound` (group
  `antiphon-fake-gateway-outbound`, auto-offset `Latest`) and records every would-be
  delivery to `logs/fake-gateway/outbound.jsonl` and memory.
- `GET /deliveries?since=<seq>&channel=&conversationId=` — assert deliveries in tests;
  `DELETE /deliveries` resets between tests.
- `POST /inbound {"chatId":"123","text":"hi","username":"mike"}` — produces a synthetic
  Telegram-shaped `ChannelMessage` onto `channels.inbound`, driving the full bridge path
  (catalog upsert -> agent prompt -> reply -> outbound -> recorded delivery) with no external
  service.
- `POST /pause` / `POST /resume` — simulate a gateway outage (stops polling entirely).
- `GET /health`.

Downstream repos (e.g. school-revision) can `dotnet tool install antiphon-fake-gateway` from the
GitHub Packages feed and get the identical test tool.
