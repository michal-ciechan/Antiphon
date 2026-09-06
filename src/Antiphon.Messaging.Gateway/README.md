# Antiphon.Messaging.Gateway

Gateway-side Kafka host for the Antiphon messaging bus: produce `ChannelMessage` to
`channels.inbound`, consume `ChannelReply` from `channels.outbound`, and dispatch each
reply to the matching `IChannelAdapter`.

```csharp
services.AddSingleton<IChannelAdapter>(new MyAdapter());
services.AddAntiphonGateway(configuration, "Kafka"); // or AntiphonGateway
```

`ConsumerGroup` must be unique per gateway process. `TopicLayout.PerProvider` is a
documented follow-up and throws `NotSupportedException`. Tests can use
`Antiphon.Messaging.Gateway.Testing.InMemoryGatewayBus` in this same package.

Worked example: [`samples/EchoGateway`](../../samples/EchoGateway). Getting
started: [`docs/messaging/build-your-own-gateway.md`](../../docs/messaging/build-your-own-gateway.md).

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

## Lag evidence and compatibility

Portable Gateway and Client defaults remain `antiphon-consumer`. Set
`AntiphonConsumerGroup` to the application's inbound consumer group, independently of
Gateway's outbound `ConsumerGroup`. `ExpectedAntiphonConsumerGroup` is optional for library
hosts, but if supplied must match ordinally. Standalone Service requires it in code.

DI uses `IConsumerGroupObservationReader`: Present/Absent/QueryFailed group observations,
with CommittedOffset/NoCommit/QueryFailed partition evidence. Only a nonnegative committed
next offset can authorize lag. `IConsumerGroupOffsetReader` remains available as a nullable
projection; null now explicitly means unknown. The old monitor constructor remains source
compatible but cannot prove group presence through a nullable reader and therefore suppresses
notices; hosts constructing monitors directly should migrate to the richer constructor.

Disabled monitors and hosts without an inbox store make no broker calls and report Disabled.
The observation budget defaults to ten seconds. Readiness expires after two polls plus that
budget. See [monitor semantics](../../docs/telegram.md#inbound-lag-notices-and-monitor-readiness-card-0410).
