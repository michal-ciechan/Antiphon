# Build your own Antiphon messaging gateway

A gateway is **one process** that owns one (or more) channel adapters and talks
to Antiphon over Kafka. It is the only thing that holds the provider's bot
token. Antiphon never calls Telegram, Slack, Discord, or anything else —
it produces and consumes the two contract topics.

This document is the getting-started path. The wire contract itself
(required fields, keys, enum tolerance, 20 MB cap) lives in
[`contract/v1/CONTRACT.md`](contract/v1/CONTRACT.md). The worked sample is
[`samples/EchoGateway`](../../samples/EchoGateway).

## 1. What a gateway is

```
provider (Telegram, Slack, Discord, stdin, …)
        │  IChannelAdapter.ReceiveAsync / SendAsync
        ▼
 your process  ──produce──►  channels.inbound   (ChannelMessage, key = Conversation.Id)
               ◄─consume──  channels.outbound  (ChannelReply,   key = conversation id)
               ──produce──►  channels.ops.inbound-unconsumed  (InboundUnconsumedEvent; CARD-0245)
        │
        ▼
     Antiphon server (catalog, agent, CARD-0067 reply dispatch)
```

`Antiphon.Messaging.Gateway` hosts the two Kafka loops for you:

- **ingress** — `adapter.ReceiveAsync()` → produce `ChannelMessage` to
  `channels.inbound`, keyed by `Conversation.Id`. If the receive stream ends
  or throws, the loop logs and restarts (do not let it die silently).
- **outbound** — consume `channels.outbound` in group `{ConsumerGroup}-outbound`,
  route each `ChannelReply` to the adapter whose `Channel` matches
  `reply.Channel`.

You implement `IChannelAdapter`. You do **not** write a `ProducerBuilder` or
a consume loop. `ConsumerGroup` must be unique per gateway process so two
gateways do not steal each other's outbound replies.

Topic layout today is **shared** (one inbound topic and one outbound topic
for every provider). Per-provider topics are a documented follow-up and
throw `NotSupportedException` if selected.

## 2. Prerequisites

### Packages

The contract and the host ship as NuGet packages, version lock-step at
**1.0.0**:

| Package | What it is |
|---|---|
| `Antiphon.Messaging` | `ChannelMessage` / `ChannelReply` / `IChannelAdapter` |
| `Antiphon.Messaging.Gateway` | `AddAntiphonGateway`, the two hosted loops, `InMemoryGatewayBus` |

`Antiphon.Messaging.Gateway` depends on `Antiphon.Messaging`; reference
Gateway and the contracts come with it.

The third-party feed is **nuget.org** (no PAT). GitHub Packages is the
first-party / internal mirror of the same bits, plus Slack/Telegram which
are not published publicly:

```
dotnet add package Antiphon.Messaging.Gateway --version 1.0.0
```

First-party consumers that still restore from GitHub Packages
(`https://nuget.pkg.github.com/michal-ciechan/index.json`) need a GitHub
PAT with `read:packages` and a `nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="github-antiphon"
         value="https://nuget.pkg.github.com/michal-ciechan/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github-antiphon>
      <add key="Username" value="YOUR_GITHUB_USERNAME" />
      <add key="ClearTextPassword" value="YOUR_PAT_WITH_READ_PACKAGES" />
    </github-antiphon>
  </packageSourceCredentials>
</configuration>
```

Inside this repo, `samples/EchoGateway` `ProjectReference`s the local
projects so it always compiles against the tree. The same csproj can restore
the published packages instead:

```
dotnet build samples/EchoGateway -p:UsePublishedPackages=true
```

Non-.NET gateways skip the package and build against the JSON Schema next to
`CONTRACT.md`.

### Kafka

The bus is currently **plaintext**, reachable on the operator's Tailscale
network. There is no SASL identity and no per-gateway ACL (CARD-0150 S5 was
dropped: high-trust environment). What you need:

- Bootstrap servers (local dev: `localhost:19092`; production: the
  advertised Redpanda listener).
- A **unique** `ConsumerGroup` for this process (the library appends
  `-outbound` for the reply consumer).
- Topics `channels.inbound` and `channels.outbound` already exist on the
  operator's broker. Maximum message size is **20 MB**.

A third-party process that is not on that tailnet cannot reach the broker
today. If that ever changes, identity/ACLs are the S5 follow-up, not a
gateway-author concern in this document.

### Provider credentials

Whatever the channel needs (bot token, app secret). Antiphon does not hold
them. The EchoGateway sample uses stdin/stdout and needs none.

## 3. Implement `IChannelAdapter`

```csharp
public interface IChannelAdapter
{
    string Channel { get; }
    ChannelCapabilities Capabilities { get; }
    IAsyncEnumerable<ChannelMessage> ReceiveAsync(CancellationToken cancellationToken);
    Task<SendResult> SendAsync(ChannelReply reply, CancellationToken cancellationToken);
}
```

Copy [`samples/EchoGateway/EchoChannelAdapter.cs`](../../samples/EchoGateway/EchoChannelAdapter.cs)
and replace stdin/stdout with the provider's API. The rules the server
actually enforces (full table in `CONTRACT.md`):

| Rule | Why |
|---|---|
| `Channel` is a stable string (`"echo"`, `"discord"`, …). Replies route on exact match with `reply.Channel`. | A typo here is a lost reply, not a compile error. |
| `ReceiveAsync` loops until cancelled. Do not return. | The library restarts a stream that ends; flapping is logged. After EOF the Echo sample waits on cancel rather than completing. |
| `Conversation.Id` is stable for the same chat. It is the Kafka key. | Unstable id ⇒ a new catalog row per message and replies to the wrong place. |
| `ChannelMessageId` is unique per message. Dedupe is last-id-only, not a set. | Reusing the most recent id drops the new message. |
| `Author.IsSelf` is true only for the bot's own echoes. | `true` ⇒ Antiphon ignores the inbound; `false` on your own send loops the agent on itself. |
| `Text` or `Attachments[].Content` is non-empty. | Neither ⇒ logged, not routed. |
| `ReplyHandle` is whatever you need to address a reply. Opaque to Antiphon; copied verbatim onto `ChannelReply.ReplyHandle`. | Telegram uses the chat id; Slack uses a conversation id. |
| `Raw` is required. Send at least `{}`. | The server ignores it; the gateway's own inbox may persist it. |
| Yield nothing over the 20 MB serialized cap. Reject in the adapter, before produce. | The broker will otherwise refuse the produce. |

`SendAsync` contract:

- Address the native conversation from `ReplyHandle`, falling back to
  `ConversationId`.
- Honour `Kind` (`Answer` / `Progress` / `Question`) if the channel can
  render them differently; otherwise print the text.
- Attachments arrive as `OutboundAttachment.Content` (`byte[]`, base64 on
  the wire). A filesystem path in `Source` is provenance, not something you
  can open.
- Return `SendResult.Sent(channelNativeId)` or `SendResult.Failed(error)`.
  A failed send is logged by the library and **not** retried.

There is **no per-message correlation on the wire**. A reply is addressed to
a conversation, not to the inbound message that prompted it. Threading
(Slack) is conversation-level state in the adapter.

A `ChannelReply` may also arrive for a conversation with **nothing pending**.
Antiphon has a proactive send (`POST /api/channels/{id}/send`) for scheduled
jobs and operator scripts, and the reply-durability model (CARD-0067) resolves
a reply's target from the stored prompt row at dispatch time rather than from
an in-memory correlation. `SendAsync` must therefore not assume it is
answering the last thing it received — address `ReplyHandle` /
`ConversationId` and send.

## 4. Host it

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<IChannelAdapter>(_ => new EchoChannelAdapter(Console.In, Console.Out));
builder.Services.AddAntiphonGateway(builder.Configuration);
await builder.Build().RunAsync();
```

Config section name defaults to `AntiphonGateway`. `Antiphon.Messaging.Service`
passes `"Kafka"` instead so its deployed `Kafka__*` env vars keep working.
Pick one and stick to it.

```json
{
  "AntiphonGateway": {
    "BootstrapServers": "localhost:19092",
    "InboundTopic": "channels.inbound",
    "OutboundTopic": "channels.outbound",
    "ConsumerGroup": "echo-gateway",
    "MaxMessageBytes": 20971520,
    "AutoOffsetReset": "Earliest"
  }
}
```

- `AutoOffsetReset`: production gateways keep `Earliest` so a new group does
  not skip pending replies. The fake gateway uses `Latest` so a restart does
  not replay `/deliveries`.
- `Security.SecurityProtocol` is `Plaintext`. SASL is not wired.
- `TopicLayout` is `Shared`. `PerProvider` throws.

The two hosted services start with the host. You do not call them.

## 5. Bind it in Antiphon

The channel appears in the catalog on the **first inbound message**
(`Provider` = your `Channel` key, `ExternalId` = `Conversation.Id`). Then:

1. In Antiphon, open the channel catalog, find the new row.
2. Bind it to an agent.
3. Write a preamble. There is no "Use preset" button for custom providers
   yet (telegram/slack only); paste one by hand. Unknown providers still
   route and reply.

Replies come back on `channels.outbound` with `Channel` equal to the key
you produced. If you produced `echo` and consume as `Echo`, the library
logs `no adapter registered for channel` and does not send — the match is
ordinal-ignore-case, but keep the key lowercase anyway.

## 6. Test it

**Unit / adapter tests.** `Antiphon.Messaging.Gateway.Testing.InMemoryGatewayBus`
is in the same Gateway package. Observe produced inbound (the Kafka key is
`Conversation.Id`) and `PushReply` as if a `ChannelReply` arrived. See
`samples/EchoGateway/SelfTest.cs` and
`dotnet run --project samples/EchoGateway -- --self-test`.

**Against a real broker, no real provider.**
`Antiphon.Messaging.FakeGateway` (`antiphon-fake-gateway`, AppHost port
17208) is itself a complete adapter: `POST /inbound` injects a message,
`GET /deliveries` asserts the reply. Use it as the 4-step smoke
(fake broker + fake gateway + Antiphon server) when you need the full
catalog → agent → outbound path. The AppHost defaults to the local
broker; if this machine has opted into a live broker, remove the
`AntiphonMessaging:BootstrapServers` AppHost user-secret for the
duration of a local smoke.

**EchoGateway against the local broker.** With Redpanda on `localhost:19092`
and Antiphon running, `dotnet run --project samples/EchoGateway`, type a
line, bind the `echo` / `echo-console` catalog row to an agent, and the
agent's reply prints on stdout.

## 7. Compatibility

Packages in the `Antiphon.Messaging*` family version lock-step. Mixing
versions within a major is supported; a major bump is one edit.

- **Wire contract is additive-only within a major.** New optional properties
  and new enum members are minor. Removing or renaming a property, changing
  a type, making an optional field required, changing a topic name or key,
  or changing the meaning of `Channel` / `Conversation.Id` /
  `ChannelMessageId` / `ReplyHandle` is major — and a major ships on new
  topic names.
- **`required` is part of the contract.** Adding a `required` member is major.
- **Enum tolerance:** unknown names map to a sentinel
  (`AttachmentKind.Other`, `ChannelReplyKind.Answer`,
  `ConversationKind.Group`) instead of dropping the message. Unknown
  **properties** are ignored.
- **Deprecation:** `[Obsolete]` for at least one minor before removal in
  the next major.
- **TFM:** `net9.0` now.

Non-.NET integrators: generate nothing. Read
[`contract/v1/CONTRACT.md`](contract/v1/CONTRACT.md) and the committed
JSON Schema (`channel-message.schema.json`, `channel-reply.schema.json`).
JSON is camelCase; enums are PascalCase names, not integers; attachment
bytes are base64.

## 8. Operations

- **A lost reply is a server incident, not a gateway incident.**
  `ChannelReplyLost` (Critical when the agent is channel-bound) is raised
  by Antiphon when a Channel-origin prompt ages out unanswered. Your
  `SendAsync` failures are your logs; the library logs them and does not
  retry. Do not swallow a send failure with a silent `return`.
- **Unknown outbound `Channel`:** logged, not dropped-on-the-floor without
  a line. Fix the key; the message is already consumed.
- **Ingress restart:** if `ReceiveAsync` throws or ends, the library waits
  `IngressRestartBackoff` (default 5 s), logs, and starts again. A flapping
  channel is visible. An `HttpClient` timeout is `OperationCanceledException`
  — check the token before treating it as shutdown (CARD-0031 class of bug).
- **Revocation:** there is no per-gateway Kafka principal today. Stopping
  the process is the off switch. Do not leave a disabled adapter registered
  in DI; it will still consume the shared outbound topic for its group.

## See also

- [`samples/EchoGateway`](../../samples/EchoGateway) — this document's
  worked example.
- [`src/Antiphon.Messaging.FakeGateway`](../../src/Antiphon.Messaging.FakeGateway) —
  the smallest complete *test* gateway (inject + recorded deliveries).
- [`src/Antiphon.Messaging.Telegram`](../../src/Antiphon.Messaging.Telegram) /
  [`Slack`](../../src/Antiphon.Messaging.Slack) — production adapters.
- [`src/Antiphon.Messaging.Gateway/README.md`](../../src/Antiphon.Messaging.Gateway/README.md) —
  package README (same versioning policy).
