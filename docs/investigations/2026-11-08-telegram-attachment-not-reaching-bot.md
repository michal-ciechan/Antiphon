# 2026-11-08 — Telegram file attachment (17.5 MB pptx) never reaches the bot

Investigate task aff9b6f2, base master `cad7766950ee96edc13e6ebf805ec5b4bbebbd6f`. Read-only.

## Verdict

**Confirmed. The cause is a fixed product limit, made worse by a vague note to the agent.**

The file is `Homework Religious Ideas of the Soul.pptx`, 18,430,415 bytes (17.58 MiB), MIME
`application/vnd.openxmlformats-officedocument.presentationml.presentation`. That is larger than
the gateway's built-in inbound inline cap, `Telegram:MaxInlineAttachmentBytes` = 14 MiB =
14,680,064 bytes (`src/Antiphon.Messaging.Telegram/TelegramSettings.cs:48`). The gateway checks
the size Telegram declares before it calls `getFile`, never downloads the file, and publishes the
message metadata-only
(`src/Antiphon.Messaging.Telegram/TelegramChannelAdapter.cs:171-177`). The server then gives the
agent a note with no reason in it, `[file attached — could not be imported (no content relayed)]`
(`server/Application/Services/ChannelBridgeService.cs:813-816`). The note leaves out the file
name, the size and the cause. The agent therefore told the sender to resend it as a photo or a
PDF, which is bad advice: a PDF over 14 MiB would be dropped the same way.

This is not configuration and not Telegram Group Privacy. Every message reached ingress and the
server journal (see below). Telegram's Bot API `getFile` limit (20 MB) would have allowed this
file. The 14 MiB cap exists because attachments travel inline as base64 inside one Kafka record
under the bus-wide 20 MiB cap (`src/Antiphon.Messaging.Client/AntiphonMessagingOptions.cs:29`,
`src/Antiphon.Messaging.Gateway/AntiphonGatewayOptions.cs:22`). This file's base64 is about
24.6 MB, so raising the setting cannot carry it. Inline-on-the-bus transport cannot deliver any
file between about 14.6 MiB and Telegram's 20 MB `getFile` limit.

## Evidence

### Timezone

The prompt headers say `18:30` and their transcript timestamps are `18:30:39Z`, so local time is
UTC+0 on 2026-11-08. The four messages are below.

### Server journal: `ChannelInbounds`, Family channel `caee9d25…`

Read-only query on the desktop dev Postgres. Only the metadata fields are shown here.

| Inbound id | Native msg | AcceptedAt (UTC) | Attachment | Declared size | Content inlined | Forwarded |
|---|---|---|---|---|---|---|
| 21367f64… | 1784 | 18:30:34.589 | File, the .pptx above | 18,430,415 | **no** | no |
| 8170a60f… | 1786 | 18:31:06.969 | same file, caption "Make it pdf printable into 2x A4" | 18,430,415 | **no** | no |
| f7145865… | 1788 | 18:39:41.832 | same file, same caption | 18,430,415 | **no** | **yes** |
| c498a8ac… | 1790 | 18:39:56.217 | none (text "Make it into 2x A4") | — | — | no |

The native `raw` Update in each envelope has the same `document.file_size` = 18430415 and
`mime_type`. Ingress, the Kafka hop and the bridge journal all worked. Only `Content` is missing.
The journal accepted all three records, so the bus did not reject a message for size. The bytes
were dropped before produce, in the gateway's hydrate step.

### Agent transcript: Family agent `a7647365…`, session `fb38e751…`

| Seq | Timestamp (UTC) | UserPrompt attachment line |
|---|---|---|
| 1778 | 18:30:39.207 | `[file attached — could not be imported (no content relayed)]` |
| 1781 | 18:31:09.180 | caption + the same line |
| 1784 | 18:39:44.395 | caption + the same line |

The agent's replies were at seq 1779, 1782 and 1787. At 1782 it said "plik znowu do mnie nie
dotarł" ("the file didn't reach me again"). At 1785-1787 it listed `.antiphon/inbox` and found
that its newest file was from 5 Nov. **"Znowu" ("again") refers to the 18:30 send a minute
earlier, not to an older incident.** This session has run since 2026-09-09 and has 30 earlier
UserPrompts with attachments: photos, plus `file` at seq 749, 881, 958 and 1434. Every one of
them was relayed with an inbox path. Seq 1778/1781/1784 are the only metadata-only attachments in
the session. What separates them from the successful sends is **size**. Group versus DM, caption
and the forwarded flag make no difference: the forwarded 1788 and the plain 1784 failed the same
way.

### Gateway log (not retrieved)

The am-service container on server2 is out of reach from this runner mirror: `ssh mc@server2`
fails with `Host key verification failed`, and the local docker socket shows only this runner's
test Postgres. The predicted line is Information
`[telegram] attachment <file_id> is 18430415 bytes — over the 14680064 inline cap; keeping
metadata only` (`TelegramChannelAdapter.cs:173-175`). Reading
`docker logs <am-service> --since 2026-11-08T18:25:00Z --until 18:45:00Z | grep '\[telegram\] attachment'`
from the desktop would confirm it. It would also show whether a deployed `Telegram__MaxInlineAttachmentBytes`
override exists. The repo sets none: no compose, script or doc override was found.

## Mechanism, end to end

1. `TelegramChannelAdapter.ExtractAttachments`/`AddFile` (`TelegramChannelAdapter.cs:352-391`)
   maps `message.document` to `Attachment{Kind=File, Name, Mime, Size=file_size, ChannelRef=file_id}`.
   Documents and any MIME type are accepted; there is no extension allow-list.
2. `HydrateAttachmentsAsync` (`:162-196`) skips the download when the declared `Size` is over
   `MaxInlineAttachmentBytes` (`:171`). It logs at Information and keeps the attachment
   metadata-only. A failed download also falls back to metadata-only (`:187-192`).
3. The message is published to `channels.inbound` with `attachments[].content` null. Only the
   gateway holds the bot token, so no consumer behind the bus can fetch the file later
   (`:146-147`).
4. `ChannelBridgeService` journals the envelope (CARD-0593), and `RenderMessageBody` (`:803-839`)
   writes the inlined bytes to `<workingDirectory>\.antiphon\inbox\<id>-<n>-<name>` and adds
   `[file attached: <path>]` to the prompt. When `Content` is empty it adds the generic
   "could not be imported" note instead (`:813-816`). Name, size, MIME and reason are all
   available in the envelope and none of them is passed on. Nothing is sent to the sender.

## Group privacy

Not a factor. Bot privacy was not checked with the token. All three document messages, which
carried no @-mention, arrived at ingress, so the bot does receive these group documents.

## Classification

**Product defect: a design limit plus poor diagnostics. No setting fixes it.**

- Telegram (`TelegramSettings.cs:48`) and Slack (`SlackSettings.cs:70`) default to 14 MiB. That
  cap comes from the 20 MiB bus record limit, so raising either one only moves the failure to
  produce or broker rejection.
- Owner: the Antiphon messaging gateway and the server channel bridge (this repo). The bus cap
  belongs to the gateway/server Kafka settings and the topic `max.message.bytes`.

## Card-ready description

**Telegram inbound documents over 14 MiB are dropped silently; the agent sees no name, size or reason**

A 17.58 MiB `.pptx` was sent three times to Family on 2026-11-08 (native ids 1784/1786/1788).
Each arrived metadata-only. The gateway skips any file whose declared size is over
`Telegram:MaxInlineAttachmentBytes` (14 MiB, `TelegramChannelAdapter.cs:171`), because
attachments travel base64 inside one 20 MiB Kafka record. The agent got
`[file attached — could not be imported (no content relayed)]`
(`ChannelBridgeService.cs:815`), without the file name, size or cause, and told the user to resend
it as a photo or PDF. A PDF of the same size would fail too. Files from about 14.6 MiB up to
Telegram's 20 MB `getFile` limit cannot be delivered today, and raising the setting does not help
because the base64 overflows the bus.

Acceptance:
1. The agent's note names the file, its size and the reason, and states the size limit.
2. Either files up to the provider limit (20 MB for Telegram) reach the agent inbox, or the
   sender gets a clear, automatic too-large notice.
3. Tests cover a declared-oversize document reaching the prompt with name, size and reason, and
   cover the chosen large-file path.

## Not done, noted

- Fix idea, one line: move large inbound bytes out of band (the gateway stores the file and the
  bus carries a reference the server fetches), and render name/size/reason in the
  metadata-only note.
- The gateway log line and any deployed override were not retrieved; the command is in the
  Gateway log section above.
- Earlier `file` attachments (seq 749/881/958/1434) could not be sized from the journal, because
  their envelopes were nulled after retention. The inbox listing at seq 1786 shows they are all
  much smaller than 14 MiB.
