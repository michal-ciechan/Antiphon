# CARD-1247 unbound SourceLanding Mutation candidate

All controls below are **pending**. Code caeeae9a runs ordinary regressions only. This candidate
has no SourceLanding operation/snapshot binding; the caller commissions it after Final Review
and landing through original Code owner 4c42a269-3d07-482f-ad01-bc90793f25ec.

Use method-scoped `/*/Antiphon.Tests.Application/ChannelReplyLateConfirmDispatchTests/ExactMethod`
filters on the bound pack. Argument variants listed below must appear in each relevant TRX.
Mutation owns deliberate red/restore/green cycles and missing-control discovery. No timeout
or assertion may be loosened. Ordinary green and the pre-repair failures discharge no PC.

| PC / variants | Deliberate guarded defect | Exact method | Status |
|---|---|---|---|
| PC-1a (false,true), 1b (false,false), 1c (true,true), 1d (true,false) | Replace bound source dispatch with newest-turn dispatch | Grace_dispatch_keeps_its_confirmed_turn_when_a_newer_turn_completes | Pending |
| PC-2a (false,true), 2b (true,false) | Bypass the direct source claim and let both publishers send | Grace_and_runtime_redispatch_share_one_atomic_publication_owner | Pending |
| PC-3a (false,true), 3b (true,false) | Await grace publication under the queue semaphore | A_held_grace_reply_publication_does_not_block_the_next_same_session_queue_operation | Pending |
| PC-4a (false,true), 4b (true,false) | Retain the failed direct claim | Failed_direct_publication_releases_only_its_completed_claim_for_retry | Pending |
| PC-5 | Omit grace dispatch | A_grace_late_confirmed_channel_row_publishes_its_reply_without_a_later_live_event | Pending |
| PC-6 | Dispatch on a grace that confirmed no Channel row | A_grace_late_confirm_with_no_channel_origin_row_does_not_dispatch | Pending |
| PC-7a (false,true), 7b (true,false) | Omit bound direct-turn watermark registration | Bound_grace_dispatch_retains_the_direct_turn_trailing_text_watermark | Pending |
| PC-8a (false,true), 8b (true,false) | Let a losing longer snapshot advance the watermark | A_losing_direct_dispatch_cannot_consume_text_it_did_not_publish | Pending |
| PC-9a (false,true), 9b (true,false) | Cancel the producer wait and release ownership while transport continues | Caller_cancellation_does_not_release_a_still_publishing_direct_reply | Pending |

There are 18 pending argument/control variants. The ordinary plan is
[the repair plan](../plans/2026-10-14-card-1247-bound-dispatch-repair.md).
