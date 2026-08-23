# CARD-0150 — third-party messaging gateways via a public Kafka contract + NuGet client: plan

**Date:** 2026-08-23 · **Card:** CARD-0150 (`9f480f6f-54da-476a-8aee-f90ef4f4140f`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `18d21a2`. Every claim about the contract, the packages, the
workflow and the broker below is a direct read of the code or a live `rpk` query against
`am-redpanda` on server2 on 2026-08-23; nothing is taken from the card's own description, which
turned out to be stale in the ways §1 lists.

**Sibling:** CARD-0067 (channel-reply durability) is the server-side contract this exposes. It is
**not** redesigned here — §3.6 is the finding the card asked for on that point.

---

## Verdict up front

**This is "extract, harden and document something that basically exists", not "design a public
contract from scratch" — with one real gap and one real security decision.**

1. **The contract DTOs already exist, are already clean, and are already published.**
   `src/Antiphon.Messaging` holds `ChannelMessage`/`ChannelReply`/`ChannelCapabilities`/
   `IChannelAdapter` with **zero** references to the server, Postgres, or anything Antiphon-internal
   (its only dependency is the BCL). `.github/workflows/publish-nuget.yml` already packs it, plus
   `.Client`, `.Client.Testing`, `.FakeGateway` and `.Slack`, and pushes them to **GitHub Packages**
   at version `0.1.0` (`src/Messaging.Pack.props`). The card's open question #1 is answered: the
   contract surface is publishable today.

2. **The gap: the published client is the WRONG SIDE for a gateway.** `Antiphon.Messaging.Client`
   is the *application* side of the bus — `IAntiphonMessagingProducer.SendAsync(ChannelReply)` and
   `IAntiphonMessagingConsumer.ConsumeAsync() → ChannelMessage`. That is exactly what the Antiphon
   *server* uses (`ChannelBridgeService`, `ChannelReplyDispatcher`). A gateway does the mirror image
   — produce `ChannelMessage` to `channels.inbound`, consume `ChannelReply` from
   `channels.outbound` — and that code exists **three times as raw Confluent.Kafka loops** with no
   shared library: `Antiphon.Messaging.Service/ChannelIngressService.cs` +
   `OutboundConsumerService.cs`, and `Antiphon.Messaging.FakeGateway/Program.cs`. A third party
   today would reference the contracts package and then hand-copy ~150 lines of Kafka plumbing
   from a repo the card says they should not need to read. **The deliverable is a fourth package,
   `Antiphon.Messaging.Gateway`, that hosts an `IChannelAdapter` implementation against the bus**
   — and the existing `IChannelAdapter` is already the right seam for it (§3.1).

3. **The wire JSON is defined in three places and has one latent compatibility trap.** The
   camelCase + string-enum `JsonSerializerOptions` is constructed independently in
   `Antiphon.Messaging.Client/MessagingJson.cs`, `Antiphon.Messaging.Service/Program.cs` and
   `FakeGateway/Program.cs`. Because enums serialise as strings and `JsonStringEnumConverter`
   throws on an unknown name, **adding a member to `AttachmentKind` or `ChannelReplyKind` is a
   breaking change for every older reader** — each such message is logged and *skipped*
   (`KafkaAntiphonMessagingConsumer.TryDeserialize`, `OutboundConsumerService`), which on the
   outbound side is a silently lost reply. The contract package must own the wire definition and a
   tolerant enum reader before anyone outside this repo builds against it (§3.2).

4. **Security today is the Tailscale perimeter and nothing else — and a shared `channels.outbound`
   is not scopeable by ACL.** Measured on `am-redpanda`: `enable_sasl=false`,
   `kafka_enable_authorization=null`, `superusers=[]`, external listener advertised on the
   Tailscale address `100.93.77.126:19092`. Any process on the tailnet can read and write both
   topics. That is fine for the operator's own gateways; it is not a third-party boundary. Kafka
   ACLs are per-topic, and there is **one** outbound topic for every provider, so "a gateway may
   read only its own replies" cannot be expressed without a topic-per-provider layout — which
   touches the server's *producer* topic selection (a `Client`-layer concern, not CARD-0067's
   correlation logic). This is the one design choice that needs the operator (§3.4, Decision D2).

5. **Versioning: `0.1.0`, all four packages lock-stepped, `net9.0` only, no `schemaVersion` on the
   wire.** Adequate for "the operator's other repos" (the `school_revision` instance is the only
   consumer today), not for a third party. §3.3 sets the policy: additive-only wire changes, a
   tolerant reader, `1.0.0` at the end of this card, semver with a written deprecation rule.

6. **Non-.NET: in scope, cheaply.** .NET 9's `JsonSchemaExporter` can emit JSON Schema for the
   records from the same types the package ships, and a test pins the committed schema to the
   generated one so the two cannot drift. A Python gateway then builds against
   `docs/messaging/contract/v1/*.schema.json` (§3.5).

**Three operator decisions are needed before S3/S5 can be built** (§5): feed (GitHub Packages vs
NuGet.org — the repo is already **public, MIT**), the trust tier this card targets (operator-run
gateways vs genuinely third-party processes — it decides whether per-provider topics ship now),
and whether `am-service` moves from a source-tarball deploy to consuming the package. Everything
else is decided below.

---

## 1. What the investigation found

### 1.1 The three projects the card asked about

| Project | Contents | Server coupling | Packed today |
|---|---|---|---|
| `Antiphon.Messaging` | `ChannelMessage` (+ `Conversation`, `Participant`, `Mention`, `Attachment`, `ReplyReference`, enums), `ChannelReply` (+ `OutboundAttachment`, `ChannelReplyKind`), `ChannelCapabilities`, `IChannelAdapter` + `SendResult` | **None.** BCL only (`System.Text.Json.JsonElement` for `Raw`/`RawOverrides`) | yes |
| `Antiphon.Messaging.Client` | `AntiphonMessagingOptions` (bootstrap, two topic names, group, 20 MB cap), `IAntiphonMessagingProducer`/`Consumer`, Kafka impls, `MessagingJson`, `AddAntiphonMessaging()` | None — but it is the **app-side** API (sends replies, receives messages). `Confluent.Kafka 2.6.0`, `Microsoft.Extensions.*` 9.x | yes |
| `Antiphon.Messaging.Client.Testing` | `FakeAntiphonMessagingClient` (in-memory producer+consumer, `InjectTelegramText`) | None | yes |

The card's worry ("tightly coupled to server internals?") is unfounded. `Antiphon.Messaging` is the
kind of assembly a contract package should be. The server references `.Client` by
`ProjectReference` and uses it exactly as a third-party *application* would — which is itself
useful evidence that the app-side package is sufficient.

### 1.2 The two existing gateways, and what they have in common

**`Antiphon.Messaging.Service`** (= `am-service` on server2, compose project
`/home/mc/antiphon-messaging`, **built from a source tarball** copied by hand — no package, no
registry, no git: `reference_am_service_deploy`). It is a `Microsoft.NET.Sdk.Web` app that:

- registers one `IChannelAdapter` per configured provider (`TelegramChannelAdapter`,
  `SlackChannelAdapter`; both in one process since CARD-0107);
- `ChannelIngressService` — pumps `adapter.ReceiveAsync()` and produces each `ChannelMessage` to
  `Kafka:InboundTopic`, **keyed by `Conversation.Id`**, with a 5 s restart loop that logs every
  restart (the 2026-07-31 "deaf for 19 h" lesson);
- `OutboundConsumerService` — consumes `Kafka:OutboundTopic` in group `{ConsumerGroup}-outbound`,
  `AutoOffsetReset.Earliest`, auto-commit, routes by `reply.Channel` to the adapter with that key,
  logs (does not retry) a failed send;
- `InboxConsumerService` + Postgres inbox + a small REST API (`/api/channels/...`) — **not
  contract, not needed by a gateway**; it is a convenience UI for replying by hand.

**`Antiphon.Messaging.FakeGateway`** (port 17208 in the AppHost; packed as the dotnet tool
`antiphon-fake-gateway`) — the same two loops inlined into `Program.cs`, plus `POST /inbound`
(synthesises a Telegram-shaped `ChannelMessage`), `GET /deliveries`, `/pause`/`/resume`.

What both need, and what no package provides: bootstrap + two topic names + group + the 20 MB
`MessageMaxBytes` / `MaxPartitionFetchBytes` / `FetchMaxBytes` trio, the JSON options, a produce
keyed by conversation id, and a consume loop that routes on `Channel`. ~150 lines, copied twice.

### 1.3 The server-side contract a gateway is actually held to (CARD-0067 as shipped)

Read from `ChannelBridgeService.HandleInboundAsync`, `ChatChannelService.UpsertFromInboundAsync`
and `ChannelReplyDispatcher`. These are the fields a gateway must get right; everything else in
`ChannelMessage` is carried through or ignored:

| Field | Server use | Consequence of getting it wrong |
|---|---|---|
| `Channel` | The `ChatChannel.Provider` key and the first half of `ConversationKey` (`{provider}:{conversationId}`). Any string is accepted — `ChatChannel.cs:15` says so and nothing validates it. | Replies route back on `reply.Channel == Provider`; a gateway must consume with the same key it produced. |
| `Conversation.Id` | Second half of `ConversationKey`; catalog row identity (`Provider`+`ExternalId`); **the Kafka message key** (per-chat ordering) | Unstable id ⇒ a new catalog row per message and replies to the wrong place. |
| `ChannelMessageId` | Dedupe — but only **equality with the channel's `LastChannelMessageId`** (a Kafka redelivery of the most recent message). Not a set. | Non-unique ids ⇒ dropped messages; it is not a general idempotency key and must not be documented as one. |
| `Author.IsSelf` | `HandleInboundAsync` returns immediately when true | A gateway that does not mark its own bot's echoes loops the agent on itself. |
| `Text`, `Attachments[].Content` | Routed if either is non-empty; attachments saved to the agent inbox | Neither ⇒ logged, not routed. |
| `ReplyHandle` | Stored on the catalog row; copied verbatim into `ChannelReply.ReplyHandle` | Opaque to Antiphon. May equal the conversation id (Telegram does). |
| `Raw` | Persisted (in the gateway's own inbox) — the server ignores it | `required` on the record, so a gateway must send at least `{}`. |
| `Timestamp`, `Author.DisplayName/Username`, `Conversation.Title/Kind` | Envelope header in the prompt; catalog display | Cosmetic. |

Outbound, the server emits `ChannelReply { Channel, ReplyHandle, ConversationId, Kind, Text,
Attachments }` (`ChannelReplyDispatcher.cs:334`, `:660`). `ReplyToMessageId` and `RawOverrides`
are **never set** by the server today; `Text` is truncated at `ChannelBridge:MaxReplyChars`
(4000) before produce. `ConversationId` is always present ("a complete address for the gateway"
— `ChannelReplyDispatcher.cs:263`); `ReplyHandle` is null only if the catalog row is missing.

**Idempotency/correlation is entirely server-internal.** `ChannelReplySettledAt` lives on
`SessionQueuedMessages`; the `ChannelReplyLost` incident is raised against the agent. Nothing of
CARD-0067 crosses the wire — there is no correlation id on `ChannelReply` that ties it to the
inbound message. That is a *property* of the contract to document (a gateway cannot pair a reply
with a prompt; it gets "a reply for this conversation"), not a defect to fix here.

The two places the server is provider-aware are cosmetic and non-blocking: `ChannelPreamble.
PresetTemplateFor` returns null for unknown providers (the UI's "Use preset" buttons are
hard-coded `telegram`/`slack` in `AgentSettingsModal.tsx:129`), and `GET /preamble-preset`
defaults to `telegram`. A `discord` channel binds, routes and replies with no preset; the operator
writes the preamble by hand. Noted as a follow-up, not a blocker.

### 1.4 The broker, as it actually is

`am-redpanda` (Redpanda `v26.1.10`, `--mode dev-container`, single node):

- Topics `channels.inbound` and `channels.outbound`, **1 partition, 1 replica** each,
  `max.message.bytes=20971520` (`DYNAMIC_TOPIC_CONFIG` — confirms the memory note),
  `retention.ms=604800000` (7 days, default). The Kafka key is the conversation id, so with one
  partition ordering is global anyway.
- Listeners: `internal://redpanda:9092` (compose network) and `external://100.93.77.126:19092`,
  the **Tailscale** address, published via docker `-p 19092:19092` (so it is bound on all host
  interfaces at the Docker level; the iptables DOCKER chain accepts it; what keeps it off the
  public internet is that server2's public interface does not route 19092 — worth stating as an
  assumption to verify in S5, not a measurement).
- `enable_sasl=false`, `kafka_enable_authorization=null`, `superusers=[]`. **No identity, no
  ACLs.** The desktop dev server (`appsettings.Production.json`: `server2:19092`) and `am-service`
  both connect anonymously.
- The local dev broker (`docker-compose.dev.yml`, `antiphon-redpanda`, `localhost:19092`) is
  PLAINTEXT and single-listener by design (a second listener once broke group coordination).

### 1.5 Packaging, as it actually is

- `src/Messaging.Pack.props`: `Version 0.1.0`, MIT, `RepositoryUrl` github.com/michal-ciechan/
  Antiphon. **The repo is public** (`gh repo view` → `PUBLIC`).
- `publish-nuget.yml` runs on every master push touching the five packed projects, runs
  `tests/Antiphon.Messaging.Tests` first, packs **Messaging, Client, Client.Testing, FakeGateway,
  Slack** (Telegram is *not* packed — inconsistent, and a gateway author wanting to reuse the
  Telegram adapter cannot), pushes to GitHub Packages with `--skip-duplicate`.
- GitHub Packages requires a GitHub PAT with `read:packages` **even to restore from a public
  repo's feed**. A third party's first step today is "create a PAT and a `nuget.config`".
- No `PackageReadmeFile`, no `PackageTags`, no symbol package, no `<IsTrimmable>`/SourceLink
  beyond `PublishRepositoryUrl`. No API-surface baseline (`PublicAPI.Shipped.txt`), so a breaking
  change is not caught by any build.
- Consumers of the feed today: the `school_revision` instance (via the `ghcr` image, not the
  package) and the `antiphon-fake-gateway` tool in downstream repos. No external consumer of the
  contract package has ever existed.

---

## 2. Scope

**In:** a gateway-side library package; one wire-JSON definition owned by the contract package
with a tolerant reader; a written compatibility policy and `1.0.0`; JSON Schema for non-.NET;
Kafka SASL/SCRAM identity + ACL provisioning for a gateway principal; `am-service` and the fake
gateway rebuilt on the library; a getting-started doc with a worked sample.

**Out (explicit):** any change to `ChannelBridgeService`/`ChannelReplyDispatcher` correlation,
settlement or incident logic (§3.6); the gateway inbox/REST API of `Antiphon.Messaging.Service`
(it stays in the Service, not the library); a non-.NET reference gateway (schema only); a
preamble preset per arbitrary provider; multi-tenant Antiphon (one Antiphon server, one bus).

---

## 3. Design

### 3.1 The gateway library: `Antiphon.Messaging.Gateway`

A new project `src/Antiphon.Messaging.Gateway` (package of the same name), depending on
`Antiphon.Messaging` and `Confluent.Kafka` + `Microsoft.Extensions.Hosting.Abstractions`,
`Options`, `Logging.Abstractions`. Public surface, deliberately small:

```csharp
// What a gateway author implements: the seam that already exists.
//   Antiphon.Messaging.IChannelAdapter  (Channel, Capabilities, ReceiveAsync, SendAsync)

public sealed class AntiphonGatewayOptions
{
    public const string SectionName = "AntiphonGateway";
    public string BootstrapServers { get; set; } = "localhost:19092";
    public string InboundTopic  { get; set; } = "channels.inbound";
    public string OutboundTopic { get; set; } = "channels.outbound";
    public string ConsumerGroup { get; set; } = "antiphon-gateway";   // REQUIRED to be unique per gateway process; documented
    public int MaxMessageBytes  { get; set; } = 20 * 1024 * 1024;
    public KafkaSecurityOptions Security { get; set; } = new();       // §3.4: SaslMechanism, Username, Password, SecurityProtocol, CA path
    public TimeSpan IngressRestartBackoff { get; set; } = TimeSpan.FromSeconds(5);
}

public static class ServiceCollectionExtensions
{
    // Registers GatewayIngressService + GatewayOutboundService (hosted), the producer, and the
    // consumer factory. Adapters are whatever IChannelAdapter instances are in DI.
    public static IServiceCollection AddAntiphonGateway(this IServiceCollection, IConfiguration, string section = ...);
    public static IServiceCollection AddAntiphonGateway(this IServiceCollection, Action<AntiphonGatewayOptions>);
}
```

`GatewayIngressService` is `ChannelIngressService` moved verbatim (restart loop, logging and all —
it carries a live-miss lesson and must not be re-derived). `GatewayOutboundService` is
`OutboundConsumerService` moved verbatim. Both keep their `[ingress]`/`[outbound]` log prefixes.
The Kafka client configuration (the 20 MB trio, group naming `{group}-outbound`, `Earliest`)
moves with them so the three copies become one.

**Also in the package, for gateway tests:** `Antiphon.Messaging.Gateway.Testing` is *not* a new
package — `Antiphon.Messaging.Client.Testing`'s `FakeAntiphonMessagingClient` is the app side.
Instead the Gateway package ships an `InMemoryGatewayBus` (test double for the two hosted
services: push a `ChannelReply`, observe produced `ChannelMessage`s) in a `.Testing` sub-namespace
of the same package — one package, not five, because a gateway author's tests need it and the
Service's tests need it and nobody needs it without the Gateway.

**Rejected: ship the whole `Antiphon.Messaging.Service` as the "gateway host" package.** It drags
EF Core + Npgsql + an inbox schema + a REST API that no third-party gateway wants, and
`Microsoft.NET.Sdk.Web` apps do not pack as libraries. The Service becomes a *consumer* of the
library (§3.7) and keeps its inbox.

**Rejected: fold the gateway side into `Antiphon.Messaging.Client` as extra methods.** The two
sides have opposite topic roles and opposite ACLs (§3.4); one options class with both a
"consume inbound" and a "produce inbound" path is exactly the config mistake a wrong consumer
group already invites ("MUST be distinct from the bridge's own group" — `AntiphonMessagingOptions`
says this today because someone got it wrong).

### 3.2 One wire definition, owned by the contract, tolerant by construction

Move `MessagingJson` from `Antiphon.Messaging.Client` into **`Antiphon.Messaging`**
(`Antiphon.Messaging.MessagingJson`; the Client keeps a `[Obsolete]` forwarding type for one
minor version). The Service and FakeGateway delete their private copies and use it.

The options become:

```csharp
new JsonSerializerOptions(JsonSerializerDefaults.Web)          // camelCase, case-insensitive read, unknown props ignored
{
    Converters = { new TolerantStringEnumConverterFactory() },  // NEW
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,          // unchanged: nulls are written (explicit > implicit on a wire)
};
```

`TolerantStringEnumConverterFactory` writes the name exactly as today (wire-identical — pinned
by a golden-file test) and on **read** maps an unknown name to a per-enum sentinel:
`AttachmentKind.Other`, `ChannelReplyKind.Answer`, `ConversationKind.Group`. Each enum gets an
`[UnknownValue]`-style attribute naming its sentinel so the fallback is declared next to the
type, not buried in a converter. Result: adding an enum member is a **minor** change; an old
gateway renders a new attachment kind as a file and a new reply kind as an answer, instead of
dropping the message.

**Rejected: a `schemaVersion` field on every message.** Readers would have to do something with
it, and the only sane thing to do with a higher version is "read what you understand", which is
what the tolerant reader already does. A version field whose only consumer is a log line is
ceremony. If a genuinely incompatible v2 is ever needed, it goes on **new topics**
(`channels.v2.*`), which is the only way two wire formats coexist on a bus anyway.

**Rejected: switching the wire to a schema registry / Avro / Protobuf.** No registry exists, the
payloads are small JSON, Redpanda console already renders them, and the `Raw`/`RawOverrides`
`JsonElement` pass-throughs are the contract's whole "full native fidelity" story.

### 3.3 Versioning and compatibility policy (written into the package README and `CONTRACT.md`)

- **Wire contract is additive-only within a major.** New optional properties and new enum members
  are minor. Removing/renaming a property, changing a type, making an optional field required,
  changing a topic name or key, or changing the meaning of `Channel`/`Conversation.Id`/
  `ChannelMessageId`/`ReplyHandle` is major — and a major ships on new topic names.
- **`required` is part of the contract.** Today: `Id`, `Channel`, `ChannelMessageId`,
  `Conversation{Id,Kind}`, `Author.Id`, `Timestamp`, `ReplyHandle`, `Raw` on inbound;
  `Channel` on outbound. Adding a `required` is major. (The test in S2 asserts this list so it
  cannot grow by accident.)
- **.NET API surface** uses `Microsoft.CodeAnalysis.PublicApiAnalyzers` with
  `PublicAPI.Shipped.txt` in each packed project — a public-surface change fails the build until
  the file is updated, which makes the semver bump a reviewed decision instead of an accident.
- **Deprecation:** `[Obsolete]` for ≥ 1 minor before removal in the next major; the README lists
  every obsolete member with its replacement and the version it goes.
- **TFM:** `net9.0` now; add `net10.0` when the repo moves; drop a TFM only on a major.
- **Dependencies:** `Confluent.Kafka` floats within its major (`[2.6.0,3.0.0)`), because a gateway
  author will have their own copy and a hard pin causes NU1605 downgrades on their side.
- **Lock-step versioning stays** (one `<Version>` in `Messaging.Pack.props` for all packages):
  the Gateway and Client both `[=x.y,x+1)` the contract, so mixing versions within a major works
  and a major bump is one edit.
- **`1.0.0`** is cut at the end of S4 (after the Service and FakeGateway consume the library and
  the schema is pinned), not before. Everything up to then stays `0.x` so a third party who
  finds the feed early gets the "unstable" signal NuGet's own UI gives `0.x`.

### 3.4 Authentication and topic-level scoping

**Threat model, stated.** A gateway process holds a chat platform's bot token and a Kafka
credential. What Antiphon must bound: (a) a compromised or buggy gateway must not be able to
*read* another provider's replies (the Family chat's Telegram answers are private), (b) must not
be able to inject inbound messages *as* another provider (a forged `Channel="telegram"` message
would be routed into the Family agent and its reply published to the real Telegram chat), and (c)
its credential must be revocable without touching anyone else's. (b) is the serious one: today
the only thing stopping it is that nobody on the tailnet is hostile.

**Mechanism: SASL/SCRAM-SHA-256 per gateway principal + Redpanda ACLs.** Redpanda supports both
natively (`rpk acl user create`, `rpk acl create`), no extra component. Provisioning is a script
(`scripts/messaging/provision-gateway.ps1 -Provider discord`) that:

1. creates user `gw-discord` with a generated password (written to Bitwarden under a standard
   item name, never printed);
2. creates the ACLs for the tier in force (below);
3. prints the `AntiphonGateway` config block (sans password) for the integrator.

`-Revoke` deletes the user and its ACLs; that is the whole revocation story, and it is immediate
(librdkafka re-authenticates on reconnect and the broker closes the session on ACL removal).

The **Antiphon server** and **`am-service`** get principals too (`antiphon-server`,
`gw-telegram-slack`), so "anonymous" stops being a valid identity once `enable_sasl=true`. The
external listener moves to `SASL_PLAINTEXT`; TLS on the Tailscale listener is **deferred** (the
tailnet is already an encrypted transport, and WireGuard-inside-TLS buys nothing here — stated
as the reasoning, and reversed the day the listener is exposed off-tailnet). The **internal**
listener (`redpanda:9092`, compose network only) keeps no-auth so the console and `am-service`'s
local path are unaffected until S5 flips them deliberately.

**Scoping — two tiers, and the operator picks which this card ships (Decision D2):**

| | Tier 1 — operator-run gateways | Tier 2 — third-party gateway processes |
|---|---|---|
| Topics | shared `channels.inbound` / `channels.outbound` (as today) | per-provider `channels.{provider}.inbound` / `channels.{provider}.outbound` |
| Gateway ACL | WRITE `channels.inbound`; READ `channels.outbound`; READ/DESCRIBE group `gw-{provider}*` | WRITE `channels.{provider}.inbound`; READ `channels.{provider}.outbound`; group `gw-{provider}*`. **Nothing else.** |
| What it bounds | (c) only — identity and revocation. A gateway still sees every provider's replies and can still forge another provider's inbound. | (a), (b), (c). Provider isolation is a *broker* fact, not a convention. |
| Server change | none | `KafkaAntiphonMessagingProducer` picks the outbound topic from `reply.Channel` (`OutboundTopicPattern = "channels.{provider}.outbound"`); `KafkaAntiphonMessagingConsumer` subscribes by regex `^channels\.[a-z0-9-]+\.inbound$` (librdkafka supports `^`-prefixed regex subscriptions natively). Both are in `.Client`, not in the bridge/dispatcher. |
| Migration | none | dual-run: the server consumes both the legacy and the patterned inbound topics for one release; `am-service` moves to `channels.telegram.*` + `channels.slack.*` (one process, two adapters — the ingress pump already produces per adapter, so the topic becomes per adapter too). |
| Cost | one script, one config flip | the above + the first multi-topic change the bus has had |

**Recommendation:** ship **Tier 1 in this card** (S5) and **design Tier 2 into the options now**
— `AntiphonGatewayOptions` and `AntiphonMessagingOptions` both grow a `TopicLayout`
(`Shared` | `PerProvider`) so Tier 2 is a later slice that does not change the package's public
shape, and the getting-started doc states plainly which tier a given Antiphon deployment is on.
The reason not to ship Tier 2 now: there is no third-party integrator, the only real gateways are
the operator's, and Tier 2's dual-run migration is the riskiest change on this card against a
production chat with a family in it. The reason to design it now: retrofitting a topic layout
into a 1.0 options class is exactly the breaking change §3.3 forbids.

**Rejected: mTLS client certificates.** Works, and Redpanda supports principal-from-CN, but
certificate issuance/rotation is a second secrets system for an operator who already runs
Bitwarden for SCRAM passwords; SCRAM is revocable with one `rpk` command and fits the relay flow.

**Rejected: an Antiphon-side allowlist of `Channel` values instead of broker ACLs.** It addresses
(b) for *known* providers (the server refuses inbound with an unregistered `Channel`) but nothing
for (a), and it puts a security boundary in application code that a Kafka client bypasses by
definition. It is, however, cheap defence-in-depth and is added as an *optional*
`ChannelBridge:AllowedProviders` in S5 — empty means all, as the Telegram `AllowedChatIds` already
works.

**Rejected: an HTTP/gRPC ingress API instead of Kafka for third parties.** It would solve the ACL
question by moving it into a bearer-token API, but the card's premise is "Kafka is the stable
boundary", the durability story (CARD-0067) rests on the broker, and a second ingress path is a
second thing to keep correct.

### 3.5 Non-.NET integrators: JSON Schema, generated from the types, pinned by a test

`System.Text.Json.Schema.JsonSchemaExporter` (.NET 9) emits a draft 2020-12 schema from the DTOs
using the **same `MessagingJson` options** (so camelCase and string enums come out right). A
small tool target (`dotnet run --project tools/Antiphon.Messaging.SchemaGen`) writes
`docs/messaging/contract/v1/channel-message.schema.json` and `channel-reply.schema.json`;
`ContractSchemaTests.Committed_schema_matches_generated` fails when the two diverge, so a DTO
change that forgets the schema is red. `CONTRACT.md` beside them documents topics, keys, the
field table from §1.3, the size cap, the enum-tolerance rule, and the `required` list — the
document a Python author reads instead of the package.

`byte[]` properties (`Attachment.Content`, `OutboundAttachment.Content`) serialise as base64
strings under STJ; the exporter emits `"type":"string"` — the doc states `contentEncoding:
base64` explicitly and the test asserts that annotation is present.

**Rejected: a hand-written OpenAPI/AsyncAPI document.** It would drift. AsyncAPI is the "right"
format for a bus but nothing here consumes it; the JSON Schema files can be wrapped in an
AsyncAPI envelope later without changing them.

**Rejected: deferring non-.NET entirely.** The marginal cost is one test and one generator; the
cost of deferring is a .NET-only boundary that the card explicitly asks about.

### 3.6 The finding the card asked for: does CARD-0067 have to change?

**No.** Everything CARD-0067 added is behind the `IAntiphonMessagingProducer`/`Consumer` seam:
`ConversationKey` is derived server-side from two fields the gateway already sends; settlement,
the TTL sweep and `ChannelReplyLost` are rows and incidents the gateway never sees. The one
Tier-2 change touches `KafkaAntiphonMessagingProducer.SendAsync`'s choice of topic, a line the
dispatcher does not know exists.

Two contract *properties* that follow from CARD-0067 are documented rather than changed:

- **No per-message correlation on the wire.** A reply is addressed to a conversation, not to the
  inbound message that prompted it. A gateway that wants threading (Slack does) must use
  conversation-level state, as `SlackChannelAdapter` does. Adding a `InReplyToMessageId` would
  require the dispatcher to carry the inbound id through `SessionQueuedMessages` — a CARD-0067
  change, so out of scope; recorded as a candidate follow-up card, not done.
- **Dedupe is last-id-only** (§1.3). A gateway must not rely on Antiphon to de-duplicate
  redeliveries older than the most recent message. Documented; a real idempotency set on the
  server would be a (small) bridge change and is likewise a follow-up.

### 3.7 Reference implementation and the getting-started path

- **`Antiphon.Messaging.Service` rebuilt on the library** (S4): deletes `ChannelIngressService`,
  `OutboundConsumerService` and `KafkaSettings`, calls `AddAntiphonGateway(config,
  "Kafka")` (section name kept so the deployed env vars `Kafka__*` keep working), keeps the inbox
  and REST API. This is the real validation that the library is sufficient — the production
  Telegram+Slack gateway runs on nothing but the public surface.
- **`Antiphon.Messaging.FakeGateway` rebuilt on the library** (S4): its outbound loop becomes an
  `IChannelAdapter` ("fake" channel whose `SendAsync` records to `DeliveryStore` and whose
  `ReceiveAsync` yields what `POST /inbound` injects). That makes the fake gateway *itself* the
  smallest complete example of an adapter — which is what the getting-started doc points at.
- **`samples/EchoGateway`** (S6): a ~80-line console app with an `IChannelAdapter` that reads
  lines from stdin as inbound messages (`Channel="echo"`) and prints replies. Built in
  `publish-nuget.yml` **after** the push, restoring from the feed by `PackageReference`
  (`dotnet build samples/EchoGateway --source <feed>`), so the published artefacts are proven
  consumable every release — the one thing an in-repo `ProjectReference` can never prove.
- **`docs/messaging/build-your-own-gateway.md`** outline:
  1. What a gateway is (one process, one bot token, one Kafka principal; the two topics; the
     two hosted loops the library gives you).
  2. Prereqs: the feed + credentials (per D1), a provisioned Kafka principal (`provision-gateway.
     ps1` output block), the provider's bot/app credentials.
  3. Implement `IChannelAdapter` — `Channel` key rules, `Capabilities`, `ReceiveAsync` contract
     (loop forever, yield normalised messages, set `IsSelf`, stable `Conversation.Id`, unique
     `ChannelMessageId`, `Raw`), `SendAsync` contract (honour `ReplyHandle` then
     `ConversationId`, `Kind`, attachments from `Content`, return `SendResult`).
  4. Host it: `AddAntiphonGateway` + config block; consumer-group naming; the 20 MB cap and
     what to do above it (reject before produce).
  5. Bind it in Antiphon: the channel appears in the catalog on first message; bind to an
     agent; write a preamble (no preset for custom providers yet).
  6. Test it: `InMemoryGatewayBus` for unit tests; the 4-step E2E smoke against the fake broker
     (`project_channel_e2e_verification`).
  7. Compatibility: the §3.3 policy, the enum-tolerance rule, the `CONTRACT.md` pointer for
     non-.NET.
  8. Operations: what Antiphon raises when replies are lost (`ChannelReplyLost` is the *server's*
     incident; the gateway's own send failures are its logs), revocation.

### 3.8 Decision D3 — how `am-service` is deployed after this

Today: tarball of source → `docker compose build` on server2. After S4 the Service depends on
`Antiphon.Messaging.Gateway` by `ProjectReference`, and the tarball deploy keeps working if the
Gateway project is added to the tar list and the Dockerfile `COPY`s (the Dockerfile enumerates
projects explicitly — it will break on the first deploy otherwise; S4 updates both and the memory
note). Switching the Service to `PackageReference` + the published image is cleaner but couples
the production Telegram gateway's deploy to the feed's availability and to a credentials step on
server2. **Recommendation: keep source-build for `am-service`** (it is the operator's own
process; the *sample* is what proves the feed) — but this is a deploy-procedure change the
operator owns, hence D3.

---

## 4. Slices

Each slice lands on master independently, green, with the real outcome in its commit message.
Sizes are verification-floor + authoring bands.

**S1 — contract owns the wire, tolerant enums, API baselines.** Move `MessagingJson` into
`Antiphon.Messaging`; add `TolerantStringEnumConverterFactory` + sentinel attributes; Service
and FakeGateway use the shared options; `PublicApiAnalyzers` + `PublicAPI.Shipped.txt` in the
four packed projects; pack Telegram too. Tests: golden-file round trip of a full `ChannelMessage`
and `ChannelReply` (wire-identical to today's bytes — the fixture is captured from the live topic
via the `kafka` skill *before* the change); unknown enum name → sentinel; unknown property
ignored; the `required` list pinned. *No behaviour change on the bus.* (0.5–1 day)

**S2 — `Antiphon.Messaging.Gateway`.** New project; move the two hosted services and the Kafka
config in verbatim; `AntiphonGatewayOptions` with `Security` and `TopicLayout` (Shared only
implemented; PerProvider throws `NotSupportedException` naming the follow-up); `InMemoryGatewayBus`.
Tests: ingress pump restarts after a faulted `ReceiveAsync` and logs it; outbound routes on
`Channel` and logs an unknown channel; produce key = `Conversation.Id`; options binding from the
`Kafka` section name the Service uses. Add to `publish-nuget.yml` paths + pack list. (1 day)

**S3 — JSON Schema + `CONTRACT.md`.** `tools/Antiphon.Messaging.SchemaGen`; committed schemas;
`Committed_schema_matches_generated`; the contract document with the §1.3 field table. (0.5 day)

**S4 — Service and FakeGateway on the library; cut `1.0.0`.** Delete the duplicated loops;
FakeGateway becomes an adapter; Dockerfile/tar list/`reference_am_service_deploy` updated; run
the 4-step E2E smoke locally (fake broker + fake gateway + dev server) and, after the operator's
go, deploy `am-service` and smoke Telegram + Slack live. Bump `Messaging.Pack.props` to `1.0.0`
with the §3.3 policy in each package README (`PackageReadmeFile`). **Needs D3 for the deploy half.**
(1 day + deploy window)

**S5 — Kafka identity + ACLs (Tier 1).** `provision-gateway.ps1` (+ `-Revoke`); principals for
`antiphon-server`, `gw-telegram-slack`, `antiphon-fake-gateway` (dev broker stays PLAINTEXT —
stated in the doc); `enable_sasl=true` + `kafka_enable_authorization=true` on `am-redpanda` with
the external listener on `SASL_PLAINTEXT`; `KafkaSecurityOptions` honoured by **both** Client and
Gateway (shared `Antiphon.Messaging.Kafka` internal helper, or duplicated 20 lines — duplicated,
to keep the Client free of a Gateway dependency); optional `ChannelBridge:AllowedProviders`.
Verify the public-interface assumption from §1.4 (`nmap`/`ss` from off-tailnet). Tests: a
Testcontainers Redpanda with SASL on, asserting a principal with only the gateway ACLs can
produce inbound and consume outbound and **cannot** consume inbound or produce outbound. **Needs
D2 (tier) confirmed; touches production — operator-scheduled.** (1–1.5 days)

**S6 — sample + getting-started + feed proof.** `samples/EchoGateway`; the doc from §3.7; the
post-publish restore-from-feed build step in `publish-nuget.yml` (against whichever feed D1
picks). (0.5–1 day)

**Deferred follow-ups (candidate cards, not in this one):** Tier 2 per-provider topics + dual-run
migration; `InReplyToMessageId` on the wire (a CARD-0067 change); a real inbound idempotency set
on the bridge; preamble presets for custom providers (or a generic one); TLS on the external
listener if it ever leaves the tailnet.

---

## 5. Decisions needed from the operator before S4–S6

**D1 — Feed: GitHub Packages (status quo) or NuGet.org?** The repo is already public and MIT, so
"not a public release of the product" is not a reason to keep the *contract* private — the
source is public either way. GitHub Packages costs every integrator a PAT + `nuget.config` and
cannot be used from `dotnet tool install` without the same; NuGet.org is zero-friction and
irrevocable (a pushed version can be unlisted, never deleted; the `Antiphon.*` prefix should be
reserved at nuget.org to stop squatting). **Recommendation: NuGet.org at `1.0.0`, keep GitHub
Packages for `0.x` until then.** Needs: a NuGet.org API key in the repo secrets and the prefix
reservation — both the operator's accounts.

**D2 — Trust tier this card ships.** Tier 1 (operator-run gateways; identity + revocation; shared
topics) vs Tier 2 (true third parties; per-provider topics; provider isolation enforced by the
broker; a production dual-run migration). **Recommendation: Tier 1 now, Tier 2 designed-in and
deferred** (§3.4). If the actual integrator is someone other than the operator, the answer flips
and S5 grows by the migration.

**D3 — `am-service` deploy after the rebuild.** Keep the source-tarball build (recommended; tar
list + Dockerfile gain one project) or move it to the published image/package. Either way S4's
deploy is a production Telegram+Slack restart and needs a window.

Not decisions, but the operator should know: S5 flips the production broker to SASL and will
disconnect the desktop dev server until its `appsettings.Production.json` carries the
`antiphon-server` credential (Bitwarden → user-secrets or env, never the JSON file in git).

---

## 6. Test and validation coverage

| Layer | What pins it | Slice |
|---|---|---|
| Wire bytes unchanged by S1 | golden fixtures captured from the live topics before the change; byte-equal serialisation | S1 |
| Enum tolerance | unknown name → declared sentinel for all three enums; write path unchanged | S1 |
| `required` set | reflection test over `[RequiredMember]` equals the documented list | S1 |
| Public API surface | `PublicApiAnalyzers` — build fails on unreviewed change | S1 |
| Gateway loops | restart-after-fault logged; route-by-channel; unknown channel logged; key = conversation id | S2 |
| Schema ⇄ types | committed schema equals generated; base64 annotation present | S3 |
| Library sufficiency | Service + FakeGateway contain no `Confluent.Kafka` usage of their own (a grep-test, like the existing "no `Task.Delay` in X" style); 4-step E2E smoke | S4 |
| ACL scoping | Testcontainers Redpanda + SASL: gateway principal allowed/denied matrix | S5 |
| Feed consumability | post-publish restore + build of `samples/EchoGateway` from the feed in CI | S6 |
| Existing | `tests/Antiphon.Messaging.Tests` (adapters, conformance), `ChannelBridgeTests`, `ChannelReplyDurabilityTests` — all unchanged and must stay green, which is the proof §3.6 holds | every slice |
