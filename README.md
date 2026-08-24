# Antiphon

Local-first agent orchestration: a board, durable sessions, and a Windows Aspire
dev stack. First-time setup is [docs/bootstrap.md](docs/bootstrap.md).

Prerequisites are Docker Desktop, the .NET SDK `global.json` pins, Node 20+, and
pwsh 7. Agent conventions live in [AGENTS.md](AGENTS.md).

Messaging gateway operations: [Telegram](docs/telegram-bot-ops.md) and
[Slack](docs/slack-bot-ops.md). Custom providers: [build your own gateway](docs/messaging/build-your-own-gateway.md)
([`samples/EchoGateway`](samples/EchoGateway)).

Running agents: [agent kinds, launch mechanics, and API keys](docs/agent-kinds.md),
[credential storage](docs/agent-credentials.md), and the optional
[herdr session backend](docs/herdr-sessions.md). Talking to Antiphon itself:
[the HTTP API](docs/antiphon-api.md).
