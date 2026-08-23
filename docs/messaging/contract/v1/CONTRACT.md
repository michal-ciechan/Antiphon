# Antiphon messaging contract v1

Package version **1.0.0**. This is the document a gateway author — including a non-.NET one — reads instead of the
package source. Getting started (packages, `IChannelAdapter`, `AddAntiphonGateway`, the EchoGateway
sample): [`docs/messaging/build-your-own-gateway.md`](../../build-your-own-gateway.md).
The JSON Schema files next to it (`channel-message.schema.json`,
`channel-reply.schema.json`) are generated from the same `ChannelMessage` / `ChannelReply`
types the NuGet packages ship, via `dotnet run --project tools/Antiphon.Messaging.SchemaGen`.
A test fails if the committed schema and a fresh generation diverge.

There is no `schemaVersion` field on the wire. Additive changes stay on these topics;
a genuinely incompatible v2 would ship on new topic names (`channels.v2.*`).

## Topics and keys

| Topic | Direction | Value | Kafka key |
|---|---|---|---|
| `channels.inbound` | gateway → Antiphon | `ChannelMessage` | `Conversation.Id` (per-chat ordering) |
| `channels.outbound` | Antiphon → gateway | `ChannelReply` | conversation id (`ConversationId`, falling back to `ReplyHandle`) |

Topic layout today is **shared** (one inbound topic and one outbound topic for every
provider). A per-provider layout is a documented follow-up, not implemented.

Maximum message size is **20 MB** (`max.message.bytes` on the topics, matched by producer
`MessageMaxBytes` and consumer `MaxPartitionFetchBytes` / `FetchMaxBytes`). Anything larger
must be rejected before produce.

## Required fields

These `required` members are part of the contract. Adding a `required` is a major change.

**Inbound (`ChannelMessage`):** `Id`, `Channel`, `ChannelMessageId`, `Conversation` (`Id`,
`Kind`), `Author.Id`, `Timestamp`, `ReplyHandle`, `Raw`. Nested: `Mention.Id`,
`Attachment.Kind`, `Attachment.ChannelRef`, `ReplyReference.ChannelMessageId`.

**Outbound (`ChannelReply`):** `Channel`. Nested: `OutboundAttachment.Kind`.

`Raw` is required so a gateway must send at least `{}`. The server ignores it.

## What the server actually uses

A gateway must get these fields right; everything else in `ChannelMessage` is carried
through or ignored.

| Field | Server use | Consequence of getting it wrong |
|---|---|---|
| `Channel` | The `ChatChannel.Provider` key and the first half of `ConversationKey` (`{provider}:{conversationId}`). Any string is accepted. | Replies route back on `reply.Channel == Provider`; a gateway must consume with the same key it produced. |
| `Conversation.Id` | Second half of `ConversationKey`; catalog row identity (`Provider`+`ExternalId`); **the Kafka message key** (per-chat ordering) | Unstable id ⇒ a new catalog row per message and replies to the wrong place. |
| `ChannelMessageId` | Dedupe — but only **equality with the channel's `LastChannelMessageId`** (a Kafka redelivery of the most recent message). Not a set. | Non-unique ids ⇒ dropped messages; it is not a general idempotency key and must not be treated as one. |
| `Author.IsSelf` | Inbound handler returns immediately when true | A gateway that does not mark its own bot's echoes loops the agent on itself. |
| `Text`, `Attachments[].Content` | Routed if either is non-empty; attachments saved to the agent inbox | Neither ⇒ logged, not routed. |
| `ReplyHandle` | Stored on the catalog row; copied verbatim into `ChannelReply.ReplyHandle` | Opaque to Antiphon. May equal the conversation id (Telegram does). |
| `Raw` | Persisted (in the gateway's own inbox) — the server ignores it | `required` on the record, so a gateway must send at least `{}`. |
| `Timestamp`, `Author.DisplayName/Username`, `Conversation.Title/Kind` | Envelope header in the prompt; catalog display | Cosmetic. |

Outbound, the server emits `ChannelReply { Channel, ReplyHandle, ConversationId, Kind, Text,
Attachments }`. `ReplyToMessageId` and `RawOverrides` are never set by the server today;
`Text` is truncated at 4000 characters before produce. `ConversationId` is always present;
`ReplyHandle` is null only if the catalog row is missing.

**No per-message correlation on the wire.** A reply is addressed to a conversation, not to
the inbound message that prompted it. A gateway that wants threading must keep
conversation-level state. Idempotency/settlement of replies is entirely server-internal.

## Enums

Serialized as PascalCase names (`"Group"`, `"Answer"`, `"Image"`), not integers.

Unknown names are **not** a hard failure. A tolerant reader maps them to a declared
sentinel so adding an enum member is a minor change:

| Enum | Sentinel |
|---|---|
| `AttachmentKind` | `Other` |
| `ChannelReplyKind` | `Answer` |
| `ConversationKind` | `Group` |

Unknown **properties** are ignored. Nulls are written (not omitted).

## Attachments

`Attachment.Content` and `OutboundAttachment.Content` are `byte[]` on the CLR and
**base64 strings** on the wire (`contentEncoding: base64` in the schema). The 20 MB bus
cap is the serialized size, so raw attachment bytes must stay under ~14 MB.

## Compatibility

Wire contract is additive-only within a major. New optional properties and new enum
members are minor. Removing/renaming a property, changing a type, making an optional
field required, changing a topic name or key, or changing the meaning of `Channel` /
`Conversation.Id` / `ChannelMessageId` / `ReplyHandle` is major — and a major ships on
new topic names.

JSON is camelCase (`JsonSerializerDefaults.Web`). The canonical .NET options live in
`Antiphon.Messaging.MessagingJson`.
