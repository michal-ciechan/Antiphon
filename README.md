# Antiphon

Local-first agent orchestration: a board, durable sessions, and a Windows Aspire
dev stack. First-time setup is [docs/bootstrap.md](docs/bootstrap.md).

Prerequisites are Docker Desktop, the .NET SDK `global.json` pins, Node 20+, and
pwsh 7. Agent conventions live in [AGENTS.md](AGENTS.md).

Messaging gateway operations: [Telegram](docs/telegram-bot-ops.md) and
[Slack](docs/slack-bot-ops.md). Custom providers: [build your own gateway](docs/messaging/build-your-own-gateway.md)
([`samples/EchoGateway`](samples/EchoGateway)).
