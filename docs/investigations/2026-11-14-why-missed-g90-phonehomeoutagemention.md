# Why missed: PhoneHomeOutageMentionTests

`In_flight_mention_Enter_late_confirms_without_retyping` failed at the clean
master base `8cec1e74e75819ef20756c228add987ce4ae4170`: 8 executed, 7 passed,
1 failed, 0 skipped. It expected Pending and observed Sent. Its Input Enter
really was in flight; CARD-1892 correctly retains Sent with no verdict and one
attempt. Repair commit `6cbfc7e6f0ab332ab54fd600a4f5ab144e53fd1b` changes that
intermediate expectation and requires a null verdict. The original later
LateConfirmed/one-attempt/no-retyping assertions remain intact.

The recorded CARD-1892 ordinary Code/Review checkpoints selected
SessionMessageQueuePhoneHomeDropTests and PhoneHomeLaunchTransportTests, not
this mention-routing consumer. A passing drop harness did not execute its
distinct disconnect-during-Enter path. The base Merge impacted row watched
only InputDeliveryUncertainException.cs and omitted this class even when
matched. SessionMessageQueueService.cs, where the changed catch lives, was not
a watched path. No original Merge execution receipt was supplied.

The cheapest pre-land repair is the shared
[`input-delivery-uncertainty` row](2026-11-14-why-missed-g90-three-classes.md#cheapest-pre-land-selection):
watch the queue service and guard as well as the exception, and name this
whole class explicitly. Local evidence: `.antiphon/evidence/g90/red-phone-2/`.
