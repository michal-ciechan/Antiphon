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

## Choose the machine's deployment

The server defaults to local Redpanda at `localhost:19092`. Live gateways are provisioned
per machine, outside AppHost. CARD-0496 records a separately managed
[desktop Slack sidecar](slack-bot-ops.md#desktop-slack-sidecar) on that local broker;
a fresh clone does not install it or establish real chat connectivity.

An optional remote instance needs an explicitly confirmed SSH destination and broker.
[Remote deployment operations](telegram-bot-ops.md#deploying-an-optional-remote-messaging-service)
describe `-SshTarget` and the helper's fixed `/home/mc/antiphon-messaging` Compose layout.
An intentional server broker override belongs in AppHost user-secrets
(`aspire-antiphon-apphost`) or its gitignored development overlay, and is forwarded only
to the server. The local desktop path needs no remote override.

Keep separate instances' broker/database state independent. "One instance per bot" is
about the bot token: an instance can register several providers when their tokens are
configured. Restart impact depends on which adapters actually share that instance.
The [August Slack deployment](superpowers/plans/2026-08-20-card-0107-slack-channel-plan.md)
and [broker opt-in plan](superpowers/plans/2026-08-25-card-0185-apphost-broker-opt-in-plan.md)
are dated evidence, not a current host census.

## Fake gateway (local dev / integration tests)

`src/Antiphon.Messaging.FakeGateway` (NuGet: `Antiphon.Messaging.FakeGateway`, dotnet tool
`antiphon-fake-gateway`) is a Kafka-connected stand-in for this service: real broker semantics,
no real Telegram or Slack egress of its own. AppHost runs it on **http://localhost:17208**.
Sharing its broker with a real gateway defeats test isolation: synthetic input can produce
real replies through that gateway. Synthetic tests require a broker with no real gateway
attached. The server and FakeGateway are separated only when their effective brokers differ;
changing a hostname or removing an override does not prove isolation.

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

Third-party / custom providers implement `IChannelAdapter` and host it with
`AddAntiphonGateway` — copy [`samples/EchoGateway`](../samples/EchoGateway) and
follow [`docs/messaging/build-your-own-gateway.md`](messaging/build-your-own-gateway.md).
The wire contract is [`docs/messaging/contract/v1/CONTRACT.md`](messaging/contract/v1/CONTRACT.md).

## Consumer identity and monitored deployments (CARD-0410)

The Service profile defaults both `Kafka__AntiphonConsumerGroup` and
`Kafka__ExpectedAntiphonConsumerGroup` to `antiphon-server-bridge`. Custom applications
must supply their own matching inbound group for both values, or set
`Kafka__InboundUnconsumedMonitorEnabled=false`. Do not rename the outbound
`Kafka__ConsumerGroup` or the inbox group. Service enforces the expected-group requirement
in code after configuration binding; an environment variable cannot turn that guard off.

Use `/health/inbound-unconsumed` for monitoring readiness, and `/health` for process
liveness. Unknown offsets suppress customer notices while ingress and outbound delivery
continue. See [Telegram monitor semantics](telegram.md#inbound-lag-notices-and-monitor-readiness-card-0410)
and [deployment operations](telegram-bot-ops.md#consumer-group-deployment-verification-card-0410).
