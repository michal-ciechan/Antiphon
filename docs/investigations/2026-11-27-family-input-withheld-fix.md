# Family inbound 1904 and the adopted Claude input guard

Telegram inbound `1904` reached session `fb38e751-dfbd-4b8d-9ce5-ef84d1828461` as queue row `393b1a54-95f2-401e-bfcc-aa748230baf1`. The adopted runner had permanently excluded its session from known input history. The repeated observation of an unchanged reason does not increment the persisted withhold count. Incident `adcc0055-b52a-4c49-858c-9df40e2301c4` was recorded after ten minutes. The digest pager required an AlwaysOn agent and digest-enabled destinations.

## Recovery

An adopted PtyHost session can establish a new input boundary from its owned native transcript. The runner requires the current native reader to have parsed through EOF, a real prompt followed by an idle boundary in that binding, and two observations of a clear, empty composer at least 500 ms apart. The session generation, binding and input revision fence the proof. Every subsequent guarded write still checks the current dialog. An unavailable transcript, draft or modal keeps the input pending.

The channel wake worker revisits durable queue ownership. Its retry schedule for withheld rows starts after 10 seconds, backs off to 30 seconds after one minute, and reaches two minutes after five minutes. The original queue row owns the reply correlation. The persisted UserPrompt supplies delivery evidence.

If the session remains withheld, inspect `GET /api/sessions/fb38e751-dfbd-4b8d-9ce5-ef84d1828461/transcript`. For a verified idle agent that needs a relaunch, call `POST /api/agents/a7647365-4803-4541-a457-ba9e7fcfa8f0/stop`, then `POST /api/agents/a7647365-4803-4541-a457-ba9e7fcfa8f0/start` with `{"resumeSessionId":"fb38e751-dfbd-4b8d-9ce5-ef84d1828461"}`. The guarded manual retry for the pending row is `POST /api/sessions/fb38e751-dfbd-4b8d-9ce5-ef84d1828461/messages/393b1a54-95f2-401e-bfcc-aa748230baf1/send-now`. Check current custody and queue status before acting. The desktop recovery delegate owns any live action.

## Operator notification

A committed Critical withheld incident now triggers the operator's Critical `AlertRaised` toast and the attention client's `AgentChanged` push. A hidden tab can show the existing desktop notification when browser permission is granted. The monitor records `HumanNotifiedAt` after publication. This records a feed push, not human acknowledgement. Legacy incidents with a pending input are matched on their committed timestamp, native message identity, agent and session. The incident carries the recovery route. These alerts bypass digest-chat paging.

For enabled, agent-bound Telegram groups, `ChannelBridge:GroupReplyDeadlineMinutes` defaults to 10. The oldest accepted inbound after the last outbound starts the clock. Additional inbound messages do not postpone it. The same attention path reports a missed deadline and uses the inbound ID to associate both monitors with one incident.

## Gateway receipt

Gateway ingress and the Kafka inbox consumer both write the same native key. Both the successful receipt and the desktop queue row were observed for `1904`. The redacted 23505 log cannot establish which writer or key lost its race. The store now uses a single conflict-targeted insert, preserves original content and offsets, and can fill missing broker coordinates on an older receipt. A conflict on any other key remains an error.
