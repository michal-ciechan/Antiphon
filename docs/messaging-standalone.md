# Messaging service — standalone, multi-instance

`Antiphon.Messaging.Service` (the Telegram↔Kafka bridge) runs **standalone**, **one instance
per bot**. Each instance is self-contained — **its own bot token, its own Kafka, its own
Postgres DB** — so you can run several side by side (e.g. an `antiphon` bot and a
`school_revision` bot) with no shared state.

- Published image: `ghcr.io/michal-ciechan/antiphon-messaging-telegram`.
- Override the image's pointers per instance via env (`Telegram__BotToken`,
  `Telegram__BotUsername`, `Kafka__BootstrapServers`, `Kafka__ConsumerGroup`,
  `ConnectionStrings__Messaging`, …).
- The service migrates its own DB on startup; create the Kafka topics once
  (`rpk topic create channels.inbound channels.outbound`).

Full table + a compose example: **[src/Antiphon.Messaging.Service/README.md](../src/Antiphon.Messaging.Service/README.md)**.

**Live:** the `school_revision` instance runs on server2 inside the school-revision compose
(`~/docker/schoolrevision`) — service `antiphon-messaging-telegram` + its own `messaging-redpanda`
+ DB `school_revision_messaging`, using `school_revision_bot`.
