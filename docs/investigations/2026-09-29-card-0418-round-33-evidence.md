# CARD-0418 round 33: V-14 frozen routing through restart and head resolution

This continues the [round-32 evidence](2026-09-29-card-0418-round-32-evidence.md)
from `6e0b2195de02e7b2b32a3fa03c45269a8531c88d`. The source/test commits are
`85f5d1076`, `8aad180bf`, `7faa7d819`, `b67a1108e`, and `a7bdf646c`.
No shared stack, actual destination, land, live provider, or SourceLanding Mutation
was used.

## V-14 and R-10 ordinary verdict

**V-14 and R-10 close for the ordinary isolated profile.** The plan-named
`ChannelOutboundDeliveryTests.Later_prompt_thread_and_control_cannot_retarget_pending_reply`
receives T1 from `SlackChannelAdapter`/`FakeSlackServer`, durably admits A through
`ChannelOutboundService`, then receives T2 and durably admits B. A process started
from `Antiphon.ChannelOutbound.Probe.dll` after both admissions sees the queued
follower behind the leased heads and sends nothing. A fresh DB context subsequently
checks the frozen input, exact route fields, source metadata/bytes, correlation and
publication stamps. Control publication bypasses the queue. After A's conversion
settles, A publishes in T1, B in T2 and the text-only follower last. The two
inbound-derived replies now carry distinct T1/T2 `RawOverrides` markers. CP-6's
independent-conversation case sends Y while X waits.

`ChannelOutboundRecoveryTests.Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head`
uses separate processes and an isolated schema: after A and B are durable, the
first process dies at A's committed Publishing barrier. A new process records
PublishUncertain and leaves B Ready with zero producer records and no settled
stamp. Only an explicit possible-duplicate acknowledgement requeues A; a third
process publishes the sealed T1 then T2 payloads, including exact reply handles,
message ids, kinds, text, metadata and source bytes. It records two Published
intents and one matched correlation stamp.

`ChannelOutboundRecoveryTests.Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed`
disables the original channel and observes both heads Held with no producer send.
The new `POST /api/channels/outbound-deliveries/{id}/resume` operation refuses
while that binding remains disabled. After repair, resuming B alone leaves it
behind A; resuming A lets the pump publish A then B with their frozen handles.
The operation revalidates the original agent/project binding and the pump
revalidates immediately before publication. `docs/telegram.md` and the API map
record this explicit recovery path. Held and PublishUncertain never auto-replay.

## Checkpoint receipts and limits

The complete CP-1–CP-13 closed list ran on `7faa7d819e5600f2eefaca853e2f6ae728b03bd6`
at `.antiphon/checkpoints/20260929-210159-0d4b/`. CP-1 was rerun after a
one-second wall-clock assertion fired at 999.9675 ms. A 5 ms timer tolerance
and the plan-named V-14 fixture were committed at `b67a1108e`; CP-1 then hit
an unrelated 6-second timeout in the unchanged
`CheckpointTaskOwnershipTests.late_settlement_after_owner_unverified_cancels_the_running_row`
at `20260929-212645-0a68`, and its exact whole-lane rerun passed at
`20260929-213506-0643`. The final T1/T2 post-admission process restart was
committed at `a7bdf646c`; CP-5 passed again at `20260929-214108-1bed`.
The first slice build at `20260929-203850-5ffd` failed solely on an ambiguous
test serializer name; `8aad180bf` corrected it before the successful rerun.
All runs used the checkpoint tool and its build slot; no unlisted build/test
commands ran.

| Row | Executed / passed / failed | Final ordinary receipt |
|---|---:|---|
| CP-1 | 3491 / 3491 / 0 | Final exact rerun; 33 platform skips |
| CP-2 | 373 / 373 / 0 | Full-list run |
| CP-3 | 32 / 32 / 0 | Full-list run |
| CP-4 | 64 / 64 / 0 | Full-list run |
| CP-5 | 53 / 53 / 0 | Final affected-row rerun |
| CP-6 | 29 / 29 / 0 | Full-list run; two new recovery methods |
| CP-7 | 320 / 320 / 0 | Full-list run |
| CP-8 | 71 / 69 / 2 | Inherited `PinnedAgentKindTests.T1/T2` `codex_desktop_unqualified` refusals |
| CP-9 | 19 / 19 / 0 | Full-list run |
| CP-10 | 1 / 1 / 0 | Headed browser PDF case |
| CP-11 | 125 / 125 / 0 | Gateway/adapter/monitor/broker classes |
| CP-12 | 26 / 26 / 0 | Two client files; wrapper exit 0 |
| CP-13 | build exit 0 | Production client bundle |

V-15–V-18 and V-23 remain open at their whole-ID level. Existing C-1–C-8
crash cuts and partial publication, policy, TTL and wire-budget cases ran, but
the plan's full per-cut hash/ownership/attention oracles, multi-target source
completeness, final policy race, TTL matrix and serialized-bus boundary matrix
are still required. V-25 remains the later authorized live gate. PC-1–PC-30
remain pending method-scoped SourceLanding Mutation; ordinary and nightly
green runs do not discharge them.

Whole ordinary IDs now closed: V-1–V-14, V-19–V-22, V-24, R-1, R-7, R-8,
R-10 and R-12. Still open: V-15–V-18, V-23, R-2–R-6, R-9, R-11 and
R-13–R-14.
