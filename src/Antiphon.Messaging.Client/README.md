# Antiphon.Messaging.Client

Application-side Kafka client for the Antiphon messaging bus: consume `ChannelMessage` from
`channels.inbound`, produce `ChannelReply` to `channels.outbound`. This is what an Antiphon
*server* uses. A gateway (the other side of the bus) should reference
`Antiphon.Messaging.Gateway` instead.

```csharp
services.AddAntiphonMessaging(configuration);
```

## Versioning and compatibility

Packages in the `Antiphon.Messaging*` family **version lock-step** (one `<Version>` in
`Messaging.Pack.props`). Mixing versions within a major is supported; a major bump is one edit.

- **Wire contract is additive-only within a major.** New optional properties and new enum
  members are minor. Removing or renaming a property, changing a type, making an optional
  field required, changing a topic name or key, or changing the meaning of `Channel` /
  `Conversation.Id` / `ChannelMessageId` / `ReplyHandle` is major — and a major ships on
  new topic names.
- **`required` is part of the contract.** Adding a `required` member is major.
- **.NET API surface** is pinned with `Microsoft.CodeAnalysis.PublicApiAnalyzers`
  (`PublicAPI.Shipped.txt`). An unreviewed public-surface change fails the build.
- **Deprecation:** `[Obsolete]` for at least one minor before removal in the next major.
  Obsolete in this release: `Antiphon.Messaging.Client.MessagingJson` (use
  `Antiphon.Messaging.MessagingJson`).
- **TFM:** `net9.0` now; add `net10.0` when the repo moves; drop a TFM only on a major.
- **Dependencies:** `Confluent.Kafka` floats within its major (`[2.6.0,3.0.0)`).
- **Enum tolerance:** unknown enum names on the wire map to a declared sentinel
  (`AttachmentKind.Other`, `ChannelReplyKind.Answer`, `ConversationKind.Group`) instead of
  dropping the message.
