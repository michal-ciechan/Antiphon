# Why the Family Telegram message at 22:41 UTC went unanswered

Antiphon received the 22:41Z message from Ola and routed it to the bound Family agent. The runner had adopted that Claude session, so it could not attest to the session's input history. The desktop queue withheld the prompt before typing. At 07:58 UTC the next day the row was still pending with zero delivery attempts. The missing hop was **desktop queue → Claude prompt**, and the message remains in the queue.

## Timeline

All times are UTC on 2026-11-26 unless specified. Family's Telegram conversation is `-5052370282`, its server channel is `caee9d25-b751-4401-a295-3b7e242842aa`, and its bound agent is `a7647365-4803-4541-a457-ba9e7fcfa8f0`.

| Time | Evidence |
| --- | --- |
| 21:30–23:30 | One Family inbound record in the retained broker offsets. The gateway inbox's 100 newest Telegram rows span October 17 through this window and also show one Family row in the interval. |
| 22:41:56 | Telegram update `246942866`, native message `1904`, a photo with a caption. The gateway receipt is `82ae5e3a-a972-435f-803e-5208aff42b88`. Redpanda has the corresponding record at `channels.inbound/0:521`, keyed to Family. |
| 22:41:57.858594 | The desktop queued row `393b1a54-95f2-401e-bfcc-aa748230baf1` (sequence `206`) to session `fb38e751-dfbd-4b8d-9ce5-ef84d1828461`. Its prompt carries the queue marker and an imported-photo reference. |
| 22:41:57.995520 | The queue persisted its first and only withhold for row `206`, `Claude dialog observation unavailable: adopted session`. Its delivery-attempt count stayed at zero. |
| 22:52:07.585032 | Critical `ChannelReplyLost` incident `adcc0055-b52a-4c49-858c-9df40e2301c4` recorded `ChannelInputWithheld` for `telegram:-5052370282/1904` and the same reason. |
| 22:00–23:30 | Zero records across `channels.outbound`, including Family. The scan read all 87 records in the captured retained offset range `732:819` on the only partition. The first and last record timestamps bracket the window, November 6 at 08:00:04 and November 27 at 07:57:44. |
| Nov 27, 07:57–07:59 | The bridge was enabled, its last wake-worker pass completed, and the `antiphon-server-bridge` group had committed offset `522` with zero lag. Family was enabled and bound. Its current session had started Nov 12 at 22:04:02.702445 and was Running and idle. The queue still showed one withhold and zero attempts for `206`. The normalized transcript had zero matching `UserPrompt` entries and ended Nov 12 at 22:05:00.384. The channel's latest reply stamp was Nov 12 at 22:05:02.891488, matching the latest Family outbound record at offset `778` to the second. |

The timeline comes from filtered copies of the live gateway inbox detail, desktop channel/agent/queue/incident/transcript responses, and direct broker reads without a consumer group. The broker scan stopped at the captured high watermarks. The source logs and metadata are under `/work/evidence/telegram-20261126/`: `am-service.log`, `http-metadata.txt`, `pending-note-metadata.txt`, `broker-offsets.txt`, and `broker-{inbound,outbound}-metadata.json`. The topic scan syntax is documented by [Redpanda's rpk reference](https://docs.redpanda.com/streaming/24.3/reference/rpk/rpk-topic/rpk-topic-consume/).

## Why it stopped

- **Telegram and the gateway.** The recorded adapter update is a regular group message with a caption and photo. The adapter allows by chat ID and identifies its own bot by username. It accepts text/captions, photo, document, video, audio, voice and sticker fields. It accepts new/edited messages and channel posts, retaining the original native ID on an edit, so a replayed ID follows the bridge's existing deduplication rules. It preserves a reply-to ID and excerpt. A forward follows the same text/media path with the native metadata in the raw update. Other update shapes are skipped. A supported message update with only unhandled content such as a location, contact or poll can reach Kafka as an empty envelope and the bridge journals it without routing. Message `1904` passed both the chat check and the bridge's nonempty check. The inspected routing path has no per-sender allowlist, mention/command prerequisite or quiet-hours filter. [Telegram adapter](../../src/Antiphon.Messaging.Telegram/TelegramChannelAdapter.cs#L318), [attachments and reply reference](../../src/Antiphon.Messaging.Telegram/TelegramChannelAdapter.cs#L421), [bridge acceptance](../../server/Application/Services/ChannelBridgeService.cs#L166).
- **The 23505 belongs to the gateway receipt.** The supplied log has one Family publish line at line 2, one failed `Inbox` INSERT at lines 3–6, the same index violation in its EF exception at lines 7–78, and `InboxConsumerService`'s stored line for `1904` at offset `521` on lines 79–80. The two EF error blocks describe one failed insert. The gateway logs ingress after Kafka accepts publication. Both the ingress sink and gateway inbox consumer write the same Postgres receipt after publication. Their read-before-insert can race on the unique key `(Channel, ConversationId, ChannelMessageId)`. The retained receipt and single broker record in the window fit that race. The exception redacts the conflicting key, so the exact failed writer and whether it involved `1904` remain unverified. A successful receipt and subsequent desktop queue entry for `1904` establish the outcome of its first publication. [Ingress publish and receipt order](../../src/Antiphon.Messaging.Gateway/GatewayIngressService.cs#L60), [shared sink registration](../../src/Antiphon.Messaging.Service/Program.cs#L52), [receipt lookup and insert](../../src/Antiphon.Messaging.Service/EfInboxReceiptStore.cs#L16), [unique constraint](../../src/Antiphon.Messaging.Service/MessagingDbContext.cs#L17), [duplicate handling](../../src/Antiphon.Messaging.Service/InboxUniqueConstraint.cs#L23).
- **Desktop routing.** The desktop's durable table is `ChannelInbounds`, separate from the gateway's `Inbox`. The bridge accepts the broker record transactionally before acknowledging it, with a 15-second retry after a consume-loop exception. The incident is constructed by joining the inbound's native ID to its queue row and reading its persisted withhold reason. This corroborates the routing without using the mutable channel preview as delivery evidence. A bound, enabled channel selects its assigned agent, reuses a Running session, or starts one as needed even with `AlwaysOn=false`. Here it got as far as the queue for the existing session. [Consume and acknowledge](../../server/Application/Services/ChannelBridgeService.cs#L117), [Kafka commit](../../src/Antiphon.Messaging.Client/KafkaAntiphonMessagingConsumer.cs#L103), [session selection and holds](../../server/Application/Services/ChannelBridgeService.cs#L1005), [incident join](../../server/Application/Services/ChannelBridgeService.WithheldIncident.cs#L49).
- **Input admission.** Runner adoption sets `_adopted`. For that session the runner reports unknown input history even if it can classify the visible screen. The server requires known history for a guarded Claude write, records the adopted-session withhold, and returns before sending the body or Enter. The first withhold followed the queue insert by about 137 ms. The incident scanner found the same pending, never-attempted row on an idle Running session about ten minutes later. Both reported desktop pins `0cfc4ac1ffb41f5af159a0c1db423fe119950267` and `5c0883bf203d122fdc7d4556e79087af2a683669` contain the predicate. The latter was also confirmed by the live version endpoint. Existing tests cover captionless photo admission, the adopted-session no-write behavior, and paging on a durable channel withhold. [Runner adoption](../../src/Antiphon.SessionRunner/SessionRunnerRuntime.cs#L3755), [runner observation](../../src/Antiphon.SessionRunner/SessionRunnerRuntime.cs#L2865), [server refusal](../../server/Application/Services/ProviderDialogObservationService.cs#L23), [withhold classification](../../server/Application/Services/DialogDeliveryAdmission.cs#L26), [queue early return](../../server/Application/Services/SessionMessageQueueService.cs#L2564), [durable withhold accounting](../../server/Application/Services/SessionMessageQueueService.DialogAdmission.cs#L45), [photo routing test](../../tests/Antiphon.Tests/Application/ChannelBridgeTests.cs#L776), [adopted runner test](../../tests/Antiphon.SessionRunner.Tests/ClaudeDialogInputGuardTests.cs#L145), [withhold incident test](../../tests/Antiphon.Tests/Application/ChannelInputWithheldIncidentTests.cs#L472).
- **Outbound.** The queue and transcript give no agent turn for `1904` and the broker gives no reply for any chat in the window. A Telegram 403, rate limit, or wrong chat ID in gateway egress would follow a broker reply. The supplied gateway log also has zero outbound send or over-cap notice lines. The inbox's `Pending` flag is gateway state. The desktop's publication and Telegram receipt are separate evidence. [Reply publication](../../src/Antiphon.Messaging.Client/KafkaAntiphonMessagingProducer.cs#L26), [gateway dispatch results](../../src/Antiphon.Messaging.Gateway/GatewayOutboundService.cs#L87), [Telegram destination and retries](../../src/Antiphon.Messaging.Telegram/TelegramChannelAdapter.cs#L473).

## Remaining uncertainty

The supplied Redpanda diagnostics start at Nov 26 22:53:55.822, after the message, and contain no records from either message topic. The exact runner event that triggered adoption is still unknown. The reported desktop restart around Nov 27 00:20 and deadlock episodes from 01:46 to 03:25 follow the persisted withhold and incident. The desktop watchdog file is available on the desktop, and a watchdog event only records an outage that met its reporting threshold. [Watchdog record contract](../apphost-runbook.md#L378), [desktop and runner log sources](../logs.md).

The `channels.ops.inbound-unconsumed` topic had no retained records at collection (partition 0 start and high watermark both `28`). Both Attention reads timed out, so a separate historical `ChannelIngressIncidents` record and the Critical incident's `HumanNotifiedAt` remain unverified. The bridge's live queue/incident evidence establishes the delivery stop independently. [Ingress incident projection](../../server/Application/Services/AttentionService.cs#L2331), [aged withhold incident](../../server/Application/Services/ChannelBridgeService.WithheldIncident.cs#L212).

For the remaining host-only evidence, these commands output metadata. On the desktop, the watchdog window can be checked with PowerShell.

```powershell
$path = 'C:\src\Antiphon\logs\apphost-outage-events.jsonl'
$from = [datetimeoffset]'2026-11-26T21:30:00Z'
$to = [datetimeoffset]'2026-11-27T03:30:00Z'
Get-Content -LiteralPath $path | ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { $_.ObservedUtc -and ([datetimeoffset]$_.ObservedUtc) -ge $from -and
        ([datetimeoffset]$_.ObservedUtc) -le $to } |
    Select-Object Id, Kind, DownSinceUtc, ObservedUtc | ConvertTo-Json -AsArray
```

On server2, the incident ledger and the exact inbound row can be checked in a read-only transaction.

```sh
docker exec -i antiphon-server2-postgres-postgres-1 \
  psql -X -U antiphon -d antiphon -v ON_ERROR_STOP=1 <<'SQL'
BEGIN READ ONLY;
SET LOCAL statement_timeout = '5s';
SET LOCAL TIME ZONE 'UTC';
SELECT "Id", "NativeMessageId", "AcceptedAt", "TransferredAt", "QueueMessageId",
       "AgentId", "InputWithheldIncidentAt"
FROM "ChannelInbounds"
WHERE "Provider" = 'telegram' AND "ConversationId" = '-5052370282'
  AND "NativeMessageId" = '1904';
SELECT "Id", "FirstSeenAt", "DetectedAt", "Topic", "Partition", "Offset", "Acknowledged"
FROM "ChannelIngressIncidents"
WHERE "Provider" = 'telegram' AND "ConversationId" = '-5052370282'
  AND "OriginalMessageId" = '1904';
SELECT "Id", "CreatedAt", "HumanNotifiedAt"
FROM "AgentIncidents" WHERE "Id" = 'adcc0055-b52a-4c49-858c-9df40e2301c4';
ROLLBACK;
SQL
```

## Not done, noted

Carry forward the adopted-session recovery gap with one synthetic captioned-photo red test that requires one transcript-confirmed prompt and correlated reply while preserving the Claude unknown-dialog input guard and the aged-withhold alert.
