# Antiphon.Messaging.Tests

Tests for the Telegram messaging adapter, built around a **verified fake** of the Telegram Bot API.

Run (TUnit on the .NET 10 SDK is an executable — use `dotnet run`, not `dotnet test`):

```bash
dotnet run --project tests/Antiphon.Messaging.Tests/Antiphon.Messaging.Tests.csproj -c Debug
```

## Layout

| Area | What it does |
|---|---|
| `FakeTelegram/FakeTelegramServer.cs` | In-process Kestrel fake of the Bot API — only the endpoints the service uses: `getUpdates`, `sendMessage`, `deleteWebhook` (+ `getMe`). Binds a free loopback port; responses are hand-built `JsonObject`s so the wire shape (`ok`/`result` envelope, snake_case keys) matches real Telegram. |
| `TelegramChannelAdapterTests.cs` | Integration tests for `TelegramChannelAdapter` run entirely offline against the fake (receive→normalize, send→deliver, send-to-bad-chat→failed). |
| `Conformance/TelegramContractTests.cs` | The **verified-fake** contracts: each assertion runs against the fake and — when a real token is set — real Telegram. If the fake drifts from real on a faked endpoint, the test fails. Covers `getMe`, `getUpdates` (envelope), `deleteWebhook`, invalid-token (401), and `sendMessage` error. |
| `Conformance/TelegramLiveChatConformanceTests.cs` | The one contract needing a live chat: a *successful* `sendMessage`. Discovers a chat from `getUpdates` and delivers to it (fake always; real when a chat exists). |

## Verifying against real Telegram

The conformance real legs are gated on an env var so the offline suite stays green without credentials:

```bash
export ANTIPHON_TG_TEST_TOKEN=<token of a DEDICATED test bot>
dotnet run --project tests/Antiphon.Messaging.Tests/Antiphon.Messaging.Tests.csproj -c Debug
```

- Use a **dedicated test bot** (`@antiphon_test_bot`), never the production `school_revision_bot`: real `getUpdates` allows only one consumer, so polling a bot that's already running in prod would 409-conflict with it. The test bot's token lives in Bitwarden → *Telegram Bot Tokens (Antiphon / School Revision)* → field `antiphon_test_bot`, and on this machine in `~/.antiphon-test-bot-token`.
- For the **live-chat success** leg, the bot can only message a user who has started it: send `/start` to `@antiphon_test_bot` once from a Telegram client, then re-run. With no pending chat the real leg logs and skips (suite still passes).
