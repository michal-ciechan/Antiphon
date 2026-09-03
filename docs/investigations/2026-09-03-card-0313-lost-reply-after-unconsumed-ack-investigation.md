# CARD-0313 — lost reply after inbound-unconsumed acknowledgement

## Outcome

Confirmed: this was a late-confirmation ordering race. The channel row was still `Pending` when
the persisted transcript batch triggered channel-reply dispatch; the queue then late-confirmed
the very same row as `Sent`, after the dispatch attempt. Nothing re-triggered dispatch after that
promotion. The unconsumed monitor acknowledgement and the Family session's remote-control status
were not the cause.

## Evidence from the incident

All times below are UTC; the chat envelope's `20:22` is BST.

| Time | Evidence | Meaning |
|---|---|---|
| 19:22:59.564 | `SessionQueuedMessages` row `865c60e7-9808-41bf-a722-9f6bb39aa195` was created for session `9558d35f-bfd9-4d9b-bf9c-0caf1255f09b`, with `Origin=Channel`, `ConversationKey=telegram:-5052370282`, body `[Telegram "Family" — Ola Z 20:22] If 30 mins £23 , so 45 mins`, and attempt baseline `1589`. | The channel bridge did create the durable inbound/outbound correlation for the same Family session that answered. |
| 19:23:07.510–19:23:15.330 | Stored transcript rows 1590–1593: `UserPrompt`, `TurnEnd`, 175-character `AssistantText`, then `TurnEnd`. The answer begins `£34.50 pro rata...`. | The agent received and answered the channel prompt normally. The first turn end precedes the assistant text, a known Claude ordering. |
| 19:23:36.464 | All four transcript rows were persisted together, despite their original timestamps. | The confirm/tailer path was late by roughly 21 seconds after the answer. |
| 19:23:36.478 | The queue row became `Sent` (`DeliveryAttempts=1`, `SentAt=19:23:36.478259`). | The row was promoted only after the transcript batch was present. Its `SentAt` is later than both the prompt and answer timestamps. |
| 19:54:29.514 / 19:54:29.924 | The row was settled; `AgentIncidents` row `58f7b3fa-cd8c-40e0-bc96-a334ef177d78` recorded Critical `ChannelReplyLost`, `FailureReason=StaleTtl`. | No `ChannelReply` was produced before the 30-minute TTL; the eventual incident was real, but its wording incorrectly said no matching turn completed. |

The live transcript endpoint reproduces the stored content and ordering: Family session
`9558d35f-bfd9-4d9b-bf9c-0caf1255f09b`, sequences 1590–1593. Its later investigation turn also
records the gateway evidence: seq 1626 contains the inbound-unconsumed acknowledgement for
`channels.inbound/0:170` and Telegram message 558; seq 1637's topic dump lists outbound offsets
255–261 as unrelated and 262 as the manual `tg-send.cs` answer. The manual-send turn (seqs
1594–1602) records `channels.outbound` offset 262 and Telegram message 560. This establishes that
the missing answer was never produced to the outbound topic; it was not dropped by the Telegram
adapter.

## Mechanism

1. The monitor is detection and acknowledgement only. It reads the consumer-group offset, sends a
   progress reply directly through the adapter, and publishes an operational event; it neither
   changes a session nor creates/settles a channel reply correlation
   ([InboundUnconsumedMonitorService.cs:118-147](../../src/Antiphon.Messaging.Gateway/InboundUnconsumedMonitorService.cs#L118-L147),
   [InboundUnconsumedMonitorService.cs:153-180](../../src/Antiphon.Messaging.Gateway/InboundUnconsumedMonitorService.cs#L153-L180)).

2. The bridge first upserts the channel and selects the bound agent's running persistent session
   ([ChannelBridgeService.cs:101-119](../../server/Application/Services/ChannelBridgeService.cs#L101-L119),
   [ChannelBridgeService.cs:398-425](../../server/Application/Services/ChannelBridgeService.cs#L398-L425)).
   Its enqueue persists `Origin=Channel` and the `{provider}:{conversationId}` key; that row,
   not a session-level binding or a topic `replyHandle`, is the reply correlation
   ([ChannelBridgeService.cs:187-203](../../server/Application/Services/ChannelBridgeService.cs#L187-L203)).
   The incident row has precisely those values. The current catalog row for the chat is enabled,
   bound to Family, and has `ReplyHandle=-5052370282`.

3. Channel dispatch deliberately considers only rows already `Sent` with a conversation key and
   no settlement marker ([ChannelReplyDispatcher.cs:120-134](../../server/Application/Services/ChannelReplyDispatcher.cs#L120-L134)).
   It returns immediately when that set is empty
   ([ChannelReplyDispatcher.cs:185-197](../../server/Application/Services/ChannelReplyDispatcher.cs#L185-L197)).

4. When a persisted transcript batch has a turn boundary, runtime calls channel dispatch *before*
   it asks the queue to process the boundary
   ([AgentSessionRuntime.cs:460-489](../../server/Application/Services/AgentSessionRuntime.cs#L460-L489)).
   At the incident batch, the row had not yet been late-confirmed to `Sent`, so this dispatch saw
   no open correlation and returned. The batch also had AssistantText, but the runtime chooses the
   boundary branch rather than the AssistantText re-dispatch branch
   ([AgentSessionRuntime.cs:546-562](../../server/Application/Services/AgentSessionRuntime.cs#L546-L562)).

5. The subsequent queue step late-confirms a previously attempted Pending row from the now-stored
   matching `UserPrompt`, and writes `Status=Sent` and `SentAt` itself
   ([SessionMessageQueueService.cs:1435-1465](../../server/Application/Services/SessionMessageQueueService.cs#L1435-L1465),
   [SessionMessageQueueService.cs:1497-1532](../../server/Application/Services/SessionMessageQueueService.cs#L1497-L1532)).
   That matches the DB ordering exactly: transcript `CreatedAt=19:23:36.464824`, then
   `SentAt=19:23:36.478259`. The queue's late-confirm write does not invoke the dispatcher, so the
   already-completed answer has no later event to route it.

6. The TTL classifier then searches only prompt records whose original timestamp is at or after
   `SentAt` ([ChannelReplyDispatcher.cs:596-612](../../server/Application/Services/ChannelReplyDispatcher.cs#L596-L612)).
   Here prompt 1590 is timestamped 19:23:07.510, before its late `SentAt` of 19:23:36.478. It is
   excluded, producing `StaleTtl` instead of the more truthful `TurnUnmatched` despite the stored
   completed answer.

This also rules out the card's two initial hypotheses. A `replyHandle` on arbitrary pre-existing
outbound records is not how agent replies are bound: once a turn matches, the dispatcher resolves
the handle from `ChatChannels` and can fall back to `ConversationId`
([ChannelReplyDispatcher.cs:285-313](../../server/Application/Services/ChannelReplyDispatcher.cs#L285-L313)).
The Family agent was remote-control enabled, but its persistent session id equals the session on
both the queue row and transcript; there is no separate channel-session adoption/binding state in
this path.

## Coverage and verification status

No code or tests were changed for this investigation. The existing late-confirm regression test
only verifies that a queue row becomes `Sent` without a second terminal write
([SessionMessageQueueDeliveryVerificationTests.cs:870-895](../../tests/Antiphon.Tests/Application/SessionMessageQueueDeliveryVerificationTests.cs#L870-L895)).
The dispatcher durability tests seed a correlation already `Sent` before inserting the completed
turn ([ChannelReplyDurabilityTests.cs:62-86](../../tests/Antiphon.Tests/Application/ChannelReplyDurabilityTests.cs#L62-L86)).
Neither covers one transcript batch that causes the runtime's dispatcher-first ordering and then
late-confirms a Channel row. I did not run tests because this task was evidence-gathering only.

## Not done, noted

A fix should make the late-confirm transition re-run channel dispatch (or otherwise make the
completed turn visible after promotion) and add the combined ordering regression test.

--- next stage ---
next: plan
handoff: Confirmed late-confirm ordering race: dispatch runs while the Channel row is Pending, then late-confirm marks it Sent with no subsequent dispatch; TTL also misclassifies it because SentAt is late.
artifact: docs/investigations/2026-09-03-card-0313-lost-reply-after-unconsumed-ack-investigation.md
