# CARD-0313 — reply-drop ordering-race fix plan

## Outcome

Ensure that a channel message late-confirmed by the session queue after a completed
agent turn gets a second, durable channel-reply dispatch pass. The real answer must
reach `channels.outbound` exactly once; it must never be converted to `Sent` and then
left undispatched.

## Scope and decisions

1. Keep the runtime's dispatcher-first ordering. It is intentional: dispatching before
   queue processing preserves the normal extraction/next-prompt boundary behaviour. The
   fix is a narrowly conditioned **second** dispatch after a channel row is
   late-confirmed, not an ordering reversal.
2. Make the queue boundary operation report the channel message IDs it promoted through
   the late-confirm path. The report is data only; `SessionMessageQueueService` must not
   reference `ChannelReplyDispatcher` or publish outbound messages itself.
3. In `AgentSessionRuntime.FlushQueueOnIdleAsync`, immediately re-run the existing
   channel dispatcher for the same boundary only when that report is non-empty. The
   existing durable settlement marker remains the exactly-once guard.
4. Do not broaden the dispatcher query to include `Pending` rows and do not "fix" only
   the TTL classifier. A Pending row has not yet met the prompt-confirmation invariant;
   changing the TTL wording would leave the actual answer undelivered.
5. Add an immediate structured warning for a newly late-confirmed, channel-originated
   completed turn which still has no outbound publication after the recovery dispatch.
   Include session ID, queue-message/correlation ID, conversation key, and dispatch
   outcome. Do not warn for intentional `NO_REPLY` or policy/API-withheld replies;
   producer errors keep their existing error handling. The normal successful path is the
   user-facing closure (the actual `ChannelReply`); a genuinely unresolved row remains
   eligible for the existing critical `ChannelReplyLost`/originating-conversation TTL
   closure. A new incident/event type is out of scope for this focused fix.

## Ground truth

| Fact | Evidence |
| --- | --- |
| A reply correlation is a channel queue row, not a session adoption flag or arbitrary broker reply handle. | `server/Application/Services/ChannelBridgeService.cs:187-203`; investigation evidence in `docs/investigations/2026-09-03-card-0313-lost-reply-after-unconsumed-ack-investigation.md`. |
| Dispatch only opens `Channel` rows already `Sent`, and returns when there are none. | `server/Application/Services/ChannelReplyDispatcher.cs:120-134`, `185-197`. |
| Runtime dispatches at the boundary before it lets the queue process that boundary; transcript-sync batches use that same flush path. | `server/Application/Services/AgentSessionRuntime.cs:460-489`, `531-561`. |
| Queue late-confirmation changes an attempted `Pending` row to `Sent` with a late `SentAt`, but does not dispatch. | `server/Application/Services/SessionMessageQueueService.cs:1435-1465`, `1474-1532`. |
| The durable dispatcher claim/settlement flow makes a second pass safe and sends the `ChannelReply` through the producer. | `server/Application/Services/ChannelReplyDispatcher.cs:326-385`, `518-525`. |
| The current TTL search ignores a matching prompt timestamped before the late `SentAt`, producing the misleading `StaleTtl` diagnosis. | `server/Application/Services/ChannelReplyDispatcher.cs:596-630`. |
| Existing tests cover late confirmation in isolation and a correlation seeded as `Sent`; neither exercises dispatcher-first followed by late confirmation in one runtime batch. | `tests/Antiphon.Tests/Application/SessionMessageQueueDeliveryVerificationTests.cs:870-895`; `tests/Antiphon.Tests/Application/ChannelReplyDurabilityTests.cs:62-110`. |

The inbound-unconsumed monitor is deliberately not part of the correlation mutation: it
only acknowledges/progress-notifies and emits its operational record
(`src/Antiphon.Messaging.Gateway/InboundUnconsumedMonitorService.cs:118-147`,
`153-180`). The deterministic regression therefore represents the stalled-consumer and
acknowledged state with the durable precondition that matters: an attempted,
still-`Pending` Channel row whose baseline precedes the transcript prompt. It then drives
the actual runtime batch path, rather than introducing a clock- and broker-timing-
dependent monitor test.

## Implementation slices

### Slice 1 — expose late-confirmation as a boundary result

Update `SessionMessageQueueService`'s turn-end processing to return a small result value
that records every row it late-confirmed, including a filtered collection of Channel row
IDs. Preserve all existing status, `SentAt`, delivery-verdict, and retry semantics.

Update each caller for the new result contract. A non-late-confirm path must yield an
empty collection and retain today's single dispatcher pass. Extend the existing
late-confirm regression at
`tests/Antiphon.Tests/Application/SessionMessageQueueDeliveryVerificationTests.cs:870-895`
to assert the reported Channel/non-Channel outcome as appropriate, alongside its current
"Sent without second terminal write" protection.

### Slice 2 — recover the dispatcher-first race and surface a real miss

In `AgentSessionRuntime.FlushQueueOnIdleAsync`:

1. Retain the present first `ChannelReplyDispatcher.OnTurnEndAsync` call.
2. Process the queue boundary and collect its late-confirmed Channel IDs.
3. If there are IDs, invoke the same dispatcher once more for this persisted boundary,
   after confirmation has made those rows eligible.
4. Have the dispatcher/runtime exchange enough structured outcome information to compare
   the newly-confirmed IDs with successful outbound publication and deliberate
   suppression/withholding. For an unsent, non-intentional completed answer, issue the
   structured warning described above and leave the correlation unsettled for the
   existing loss/TTL path.

Do not redispatch on every boundary and do not send directly from the queue. Retain the
dispatcher claim and settlement sequence so a repeated sync, retry, or restart cannot
produce a duplicate reply.

### Slice 3 — prove the combined runtime ordering path

Add the focused regression to
`tests/Antiphon.Tests/Application/ChannelReplyDurabilityTests.cs`, which already owns
dispatcher durability and serialises on `MessageQueue`
(`tests/Antiphon.Tests/Application/ChannelReplyDurabilityTests.cs:16-53`). Use
`BridgeQueueHarness` instead of manually calling queue and dispatcher methods:

- Seed its bound reply target and an attempted, `Pending`, Channel row with a baseline
  before the input prompt using its existing helpers
  (`tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs:381-421`, `469-485`).
- Feed one runner transcript snapshot/batch containing the matching `UserPrompt`, real
  `AssistantText`, and `TurnEnd` into `Runtime.SyncTranscriptAsync`. This must exercise
  persist -> first dispatch (empty) -> late confirm -> recovery dispatch in production
  order; if necessary, add only the small runner-fixture capability to the harness's
  current empty runner at `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs:539-575`.
- Assert exactly one fake `SentReplies` item for the expected conversation and answer;
  it is the test-double record of an outbound `ChannelReply`
  (`src/Antiphon.Messaging.Client.Testing/FakeAntiphonMessagingClient.cs:7-31`). Also
  assert `Sent`/`LateConfirmed`, a non-null `ChannelReplySettledAt`, and no critical
  lost-reply incident.

Add a warning-path control using the same late-confirm batch but a deterministic producer
failure (or equivalent non-intentional no-publication fixture). Assert the structured
warning fields and that the row remains unsettled; intentional `NO_REPLY` and
withheld-policy paths must not be classified as this warning. Keep the existing
restart/idempotency control at `ChannelReplyDurabilityTests.cs:93-110` as the no-duplicate
guard and the TTL `TurnUnmatched` coverage at `ChannelReplyDurabilityTests.cs:385-419` as
the unresolved-row closure guard.

## Verification design

This is the required medium-complexity verification design, deliberately folded into this
Plan dispatch under CARD-0146 D1/D4 rather than creating a separate TestDesign task.

| Element | Required evidence |
| --- | --- |
| **V — vehicle** | The new `ChannelReplyDurabilityTests` runtime-batch regression drives `SyncTranscriptAsync` through the real dispatcher-first and queue late-confirm sequence. A direct queue/dispatcher unit call is insufficient because it cannot prove the ordering race. |
| **R — regression assertion** | The simulated stalled-consumer/acknowledged precondition is an attempted Pending Channel row. After the complete transcript batch, `FakeAntiphonMessagingClient.SentReplies` contains exactly one expected `ChannelReply`; its row is late-confirmed and settled, with no loss incident. |
| **PC — positive control** | Temporarily remove or bypass only Slice 2's guarded second dispatcher call. The new regression must fail with zero `SentReplies` while the row is `LateConfirmed` and unsettled. Restore the call and confirm it is green. |
| **Failure signal** | Configure the fake producer (or a narrowly scoped substitute) to reject the recovery publication. Assert the structured warning, correlation/session/conversation fields, and retained unsettled row. Exclude intentional `NO_REPLY`/withheld cases. |
| **Cost** | Run the focused TUnit fixture first: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0313/ -- --treenode-filter "/*/*/ChannelReplyDurabilityTests/*"`. Use `dotnet run`, not `dotnet test`; retain the isolated output convention from `docs/testing-and-build.md:10`, `34`. Run the queue-delivery fixture too if Slice 1 changes its contract. |

No production test is run during this Plan task. Build must record the focused-test count
and any pre-existing failures, then expand only if the changed contract has callers outside
the two covered fixtures.

## Non-goals

- Do not change the inbound-unconsumed monitor, remote-control adoption, Telegram adapter,
  or generic reply-handle semantics: the investigation ruled them out.
- Do not make the late `SentAt` TTL diagnosis the primary repair. Once the answer is
  dispatched and settled, TTL is not reached; its classifier can be reconsidered separately
  only if a remaining user-visible misclassification is demonstrated.
- Do not add a blanket duplicate-suppression layer; use the dispatcher's existing durable
  claim/settlement protocol.
