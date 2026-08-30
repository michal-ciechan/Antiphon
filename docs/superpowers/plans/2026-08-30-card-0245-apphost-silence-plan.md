# CARD-0245 — plan: a disabled watchdog and a dead AppHost must not create silent hours (2026-08-30)

Plan only; nothing in this document is implemented. The 04:19 Slack message exposed two separate availability boundaries: the Windows AppHost watchdog had been disabled, while the gateway still durably produced inbound messages and the AppHost was unavailable. Neither the reply-loss TTL nor attention can see a message before the bridge consumes it.

## Verdict and order

Ship S1 and S2 first. S1 makes a disabled recovery mechanism visible. S2 is the only proposal that can tell a waiting chat user about an inbound message before SessionQueuedMessage and its SentAt exist.

| Idea | Verdict | Slice | Reason |
| --- | --- | --- | --- |
| 1. Watchdog-disabled visibility | **Build.** | S1 | Task Scheduler’s Disabled state is currently invisible. |
| 2. Inbound-unconsumed detector | **Build.** | S2 | It clocks from gateway/Kafka evidence, not an AppHost-created row. |
| 3. Bridge catch-up visibility | **Build after S1/S2.** | S3 | Useful operator evidence; reuses S2’s lag primitives. |
| 4. First-seen TTL | **Do not build separately.** | — | S2’s gateway receipt is the first-seen marker; a queue-row field recreates the blind spot. |
| 5. Block Disable-ScheduledTask | **Reject enforcement; document maintenance.** | S1 docs | A PowerShell guard cannot cover the UI, COM, schtasks.exe, or a -NoProfile host. |
| 6. Old dispatcher correlation | **No CARD-0245 code change.** | — | A reply to a newer prompt is not evidence that an older prompt was answered. |
| 7. Herdr LastSeenAt | **Defer as separate telemetry.** | — | It is a real eventual-consistency gap, but not this incident’s cause or CARD-0162’s remit. |

Use a two-minute watchdog-state observer and a configurable five-minute inbound-consumption budget, both bounded typed settings. Five minutes avoids an acknowledgement race during a normal AppHost restart but prevents another multi-hour silent wait.

## Evidence from the current implementation

ChannelBridgeService only upserts ChatChannel after IAntiphonMessagingConsumer yields. Its later FlushLaneAsync creates the durable channel-origin queue row. ChannelReplyDispatcher opens only channel rows that are Sent and have a conversation key, while its TTL clocks from SentAt. With the AppHost down none of those rows exist, so ChannelReplyLost is correct for a delivered prompt but structurally cannot report this outage.

The Kafka client exposes only ChannelMessage values and auto-commits the Antiphon consumer group (antiphon-consumer); it has no topic-offset or lag surface. The messaging service independently consumes inbound into its durable Inbox table, and GatewayIngressService first receives and produces the normalized message. That still-running gateway is the correct pre-consumption detector.

watchdog-apphost.ps1 observes HTTP health only while its own scheduled task runs. Nothing reads the state of Antiphon AppHost Watchdog itself. A server-hosted .NET service cannot know that Windows fact without shelling out, so an independently scheduled Windows script must produce the state sample.

## S1 — independently observe watchdog state and create attention

### Windows mechanism

Add ASCII-only scripts/apphost-watchdog-state-observer.ps1, run by a new per-user Scheduled Task named Antiphon AppHost Watchdog State Observer. scripts/install-autostart.ps1 must register it with the same logon-plus-repeating pattern as the watchdog, but separately: disabling Antiphon AppHost Watchdog must never disable this observer. NoWatchdog must not remove it; provide a separate NoWatchdogStateObserver opt-out. Skip it only with NoAppHost.

Each run uses Get-ScheduledTask for Antiphon AppHost Watchdog and writes an atomic logs/apphost-watchdog-state.json record containing:

- task absent, State Disabled, or an unreadable query (all unhealthy; query failure is Unknown, never falsely Enabled);
- whether logs/apphost.down-on-purpose exists; and
- observedAtUtc plus a stable disabledSinceUtc/episode id while the same condition persists.

Append a concise local observer log, but never write Antiphon’s database from the script. The state document is the handoff across an AppHost outage. Add scripts/set-apphost-maintenance.ps1 as the supported intentional-down helper: create the marker before disabling the watchdog; re-enable and remove it in reverse order. Update AGENTS.md, docs/bootstrap.md, and installer output to replace the current bare disable command. Direct Task Scheduler edits remain possible and are exactly what this observer detects.

### Server attention handoff

Add AppHostWatchdogStateAttentionService in the server supervision layer. On startup and a short period it reads the observer document. If state is Disabled/Missing/Unknown, maintenance is false, and an agent has AlwaysOn true plus an enabled bound ChatChannel, record one Critical AgentIncidentKind.AppHostWatchdogDisabled per affected agent through AgentSupervisorService.RecordIncidentAsync. Dedupe against disabledSinceUtc so a standing state creates one episode, while disable → enable → disable creates another. This reuses normal alerts and RecentCriticalIncident attention, not a parallel attention store.

The service cannot create an in-app row while AppHost is dead. The durable observer record creates it immediately after recovery; S2 is still required to contact a waiting human during the outage.

### S1 verification

- Deterministic server tests: Disabled creates one Critical incident for eligible agents; repeated reads do not duplicate; maintenance and no eligible agent create none; a new episode creates a new incident.
- Manual acceptance: disable the watchdog, run the observer once, verify JSON, then start AppHost and verify Critical attention. Repeat through the maintenance helper and verify suppression; re-enable before finishing.
- Verify install-autostart.ps1 -AppHostOnly refreshes only AppHost-side tasks, never the runner.

## S2 — gateway-owned inbound-unconsumed detector

### Durable evidence and lag test

Extend the messaging-service Inbox record, not SessionQueuedMessage. When InboxConsumerService receives a ConsumeResult, persist the original gateway/Slack timestamp, topic/partition/offset, and one-time acknowledgement/reporting watermarks. The offset identifies exactly the record whose consumption must be checked before an AppHost queue row exists.

Factor a testable consumer-group offset reader into Antiphon.Messaging.Gateway. Its real implementation uses Confluent’s admin/group-offset API against the configurable Antiphon group (default antiphon-consumer, distinct from the gateway’s outbound group). For each inbox row older than InboundUnconsumedMinutes:

1. query that group’s committed offset for the stored topic partition;
2. treat an absent group/partition offset or a committed offset not beyond this record’s offset as unconsumed; and
3. include an AppHost health probe only as diagnostics. Group offset, not HTTP health, is the deciding condition.

Run InboundUnconsumedMonitorService in the gateway once per minute. On the first proven overdue record, use the matching IChannelAdapter directly, not channels.outbound, to send one truthful acknowledgement to the original conversation/thread: “[Antiphon] I received this message, but the service is unavailable. It is queued and will be processed when the service returns.” Persist the successful-send watermark before a later monitor pass; a failed send remains retryable with bounded backoff. Preserve the original reply/thread handle and message id.

The detector observes accepted ingress for this gateway. A future per-conversation acknowledgement policy needs a durable server-to-gateway binding feed; it must not query Antiphon Postgres or guess from a transient AppHost HTTP call during an outage. That refinement is not required to truthfully acknowledge messages accepted by the deployed Antiphon bot.

### Critical evidence after recovery

After detecting lag, the gateway publishes a durable InboundUnconsumed operational event: provider, conversation id, original message id, first-seen/Slack timestamp, topic-partition-offset, detection time, and acknowledgement result. Add a dedicated operational topic and additive shared wire contract/schema; do not put a synthetic message on channels.inbound.

Add a server consumer for that topic. It persists a uniquely deduped, agent-independent ChannelIngressIncident, raises a Critical alert, and exposes it as a Critical attention item. The record intentionally has no required AgentId: the event can arrive before the restarted bridge has catalogued the channel, and evidence from an unbound channel must not vanish. The attention projection can join channel/agent details later.

Update AntiphonMessagingOptions, AntiphonGatewayOptions, shared messaging types, generated schemas/contract tests, fake gateway, and messaging deployment docs. Deploy server and server2’s am-service together because both need the operational-topic serializer and names; retain inbound/outbound v1 payload compatibility.

### S2 verification

- Unit-test committed-before, committed-at/past, and absent-group offsets with a fake clock and receipt store; no broker is needed.
- Pin exactly-once acknowledgement and event emission across repeated monitor passes/restarts; a failed adapter send retries but a successful send never duplicates.
- Pin negative cases: committed, under-five-minute, and no-lag records produce neither message nor operational event.
- In Antiphon.Tests, feed an event before a catalog row and assert one deduped Critical attention item; replay it; then let normal bridge catch-up route the actual answer once.
- Run schema/contract tests plus a real-broker fake-gateway scenario: stop only AppHost, inject Slack inbound, pass the budget, observe acknowledgement/event, restart AppHost, then observe catch-up and one final answer.

## S3 — catch-up evidence, not another notifier

After S2, add ChannelBridge:CatchUpWarningMinutes. At bridge startup take a lag snapshot and log a Warning if nonzero. For first post-start messages older than that age, log provider, conversation id, message id, age, and a startup catch-up episode; aggregate repeated messages by conversation. Do not send a second unsolicited chat message that can race the real answer. Pin a zero-lag clean start and a backlog warning that names the affected conversation.

## Explicit non-slices

### Idea 4 — first seen versus SentAt

Do not add FirstSeenAt to SessionQueuedMessage: it would be created only after the bridge is alive, so remains absent for this outage. S2’s inbox receipt is the durable first-seen marker and uses a gateway/Slack clock. ChannelReplyLost continues to own the later lifecycle after an agent receives the prompt; the two clocks are adjacent, not duplicate.

### Idea 5 — command interception

Do not install a PowerShell-profile/function refusal for Disable-ScheduledTask. The Task Scheduler UI, schtasks.exe, COM, another user, and -NoProfile all bypass it. The maintenance helper plus S1’s observable consequence is an operational contract; the independent detector is the real enforcement boundary.

### Idea 6 — CARD-0250 coordination

ChannelReplyDispatcher matches the latest completed turn’s owning prompt only to open rows whose stored bodies occur in that prompt, then settles only those rows. The seq-38 turn correctly settles its new correlation but cannot prove the older seq-33 request was answered. Silently settling the older row would turn a genuine miss into false success.

CARD-0250 changes this class for a distinct condition: an unmatched later turn containing attachments after the original correlation has settled. CARD-0245 must not concurrently edit ChannelReplyDispatcher.cs or add another unmatched-turn test. CARD-0250 owns post-settlement attachment follow-up and its unmatched-attachment incident. If an older completed turn needs recovery after CARD-0250 lands, create a separate brief for a transcript-proven replay; never infer it from a reply to a newer prompt.

### Idea 7 — Herdr staleness

This is real DB-observation staleness, but not work CARD-0162 already performs. The runner’s HerdrEventPumpService calls pane.get on baseline/reconnect and maintains live AgentStatus and AgentStatusSinceUtc. Server SessionRunnerEventPump catches up transcripts after restart, while AgentSessionRuntime.ObserveAgentStatusAsync only invalidates the UI and does the narrow blocked-exit queue nudge; it does not write AgentSessions.LastSeenAt. That field is advanced by output/transcript RecordActivityAsync.

The AppHost cannot update its database while down. Current health/corroboration reads runner sessions, output sequence, and AgentStatus, not LastSeenAt, so this staleness neither caused the Slack silence nor triggered a false health action. Defer a telemetry card to decide whether a successful post-recovery runner-list/pane-get should write a separately named LastRunnerObservedAt; do not overload LastSeenAt in CARD-0245.

## Delivery sequence

1. **S1a:** observer script, state document, independent Scheduled Task, maintenance helper, docs.
2. **S1b:** server state reader, incident kind, Critical attention, tests.
3. **S2a:** inbox metadata, lag-probe seam, monitor/direct acknowledgement, gateway tests.
4. **S2b:** operational event/topic, server incident/attention consumer, fake gateway, contract and real-broker outage tests.
5. **S3:** startup lag/catch-up warnings.

S1 may ship before S2, but this incident class is not closed until S2b runs on both AppHost and live am-service. S3 is operator context, not a replacement for either primary slice.

