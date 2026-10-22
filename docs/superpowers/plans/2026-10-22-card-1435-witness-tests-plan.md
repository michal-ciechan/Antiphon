# CARD-1435: witness tests for the CARD-1422 channel-debt rules

Test-only. CARD-1422 (fix `f14acbc06`, landed on master as `4db9e7385`) added two rules in
`SessionRuntimeActionCoordinator` and `SessionMessageQueueService` with no witness test:

1. Next-message delivery does not wait for channel debt (a failed channel reply saved as its own
   `channel` debt row). Guarded at the coordinator's post-routing re-query and at
   `SessionMessageQueueService.OnTurnEndCoreAsync`'s deferral check.
2. A late-confirm publication (`Phase > QueuePhase`) waits while an earlier channel reply for the
   session is still owed.

## Slices

- S1: `tests/Antiphon.Tests/Application/SessionRuntimeActionRecoveryTests.cs`
  - `C1435_owed_channel_reply_does_not_hold_next_message_delivery` (rule 1): a failing producer
    leaves the first turn's reply owed as debt; a plain UI queued message (no late-confirm in play)
    is still typed by the turn end and marked Sent. It pins current plain-delivery behaviour only;
    the CARD-1433 caveat (a waiting late-confirm can hold later queue actions) is not encoded.
  - `C1435_late_confirm_publication_waits_for_an_earlier_owed_reply` (rule 2): with the first
    reply owed (backoff held open), a typed-once channel message is late-confirmed by the next
    turn end; nothing publishes, the TurnEnd action waits at `QueuePhase + 1` with
    `InvalidOperationException`; once due, both replies publish in transcript order and no action
    remains.

## Throwaway mutations (removed, not committed)

| Rule | Mutation | Test | Result |
|---|---|---|---|
| 1 | drop `&& a.TriggerKind != ChannelReplyDebt` from the `OnTurnEndCoreAsync` deferral check | `C1435_owed_channel_reply_does_not_hold_next_message_delivery` | red (`SubmittedBodies should be [next]`) |
| 2 | `if (false && action.LateConfirmedChannelMessageIds.Length > 0 ...)` in the late-confirm guard | `C1435_late_confirm_publication_waits_for_an_earlier_owed_reply` | red (`producer.Sent should be empty but had`) |

The coordinator's own re-query exclusion is a second site for rule 1; it is left for Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1435cp/` | witnesses | `/*/*/SessionRuntimeActionRecoveryTests/C1435*` | V-1, V-2 | all listed, 0 failed | 2 | 3 |
| CP-2 | S1 | CP-1 | class | `/*/*/SessionRuntimeActionRecoveryTests/*` | V-1, V-2, V-3 | all listed, 0 failed | 14 | 5 |

- V-1: rule 1 witness green. V-2: rule 2 witness green. V-3: the rest of the class unchanged.
- PC-1 (rule 1, both sites) and PC-2 (rule 2) stay pending for SourceLanding Mutation.

### Unlisted

One dev build (`bin-c1435/`) and three method-scoped runs from it before the checkpoint: both
witnesses green (2/2), then each throwaway mutation above red (1/1 failed each), source restored.
The output was deleted. Minimal testing per the brief: no Unit lane.
