# CARD-0418 round 30: ordinary outbound worker purpose

This continues the [round-29 evidence](2026-09-29-card-0418-round-29-evidence.md).
The Code commits are `4b1ddb880` through `15bf45778`. No shared stack,
actual destination, land, or SourceLanding Mutation was used.

## Whole V-10 verdict

**V-10 closes for the ordinary isolated profile.** The independent boundaries
below now exercise the normal creation, dispatch, and settlement paths. The
conversion is a tool-capable `Worker`/`Custom`/`Shared` task with a private
purpose link; it does not become a specialist role or inherit source authority.

| Boundary | Executed evidence |
|---|---|
| Create and claim | `OutboundConversionTaskTests.Ordinary_creation_links_one_pinned_internal_worker_without_source_inheritance` observes one atomically linked task, one attempt, pinned converter/project/model/workspace, frozen request, `AutoContinue=false`, `CommitOnSettle=Never`, and no source parent/card/environment inheritance. The worker-token child dispatch is refused. `ChannelOutboundDispatchIntegrationTests.Dispatcher_defers_only_the_bound_conversation_and_preserves_source_bytes` drives the actual pump. V-9's competing-pump takeover case protects one task and purpose link. |
| Public boundary | `OutboundConversionPublicCreateTests.Public_create_cannot_assign_an_outbound_conversion_purpose` uses the real Program HTTP host and submits a forged `outboundDeliveryId`; the resulting ordinary Custom task has no internal link. |
| Normal dispatch and credentials | `TaskPlatformDispatchTests.Outbound_conversion_uses_the_normal_worker_dispatch_without_source_privileges` runs `AgentTaskDispatcher`, reads its spilled brief, and verifies normal Worker execution, converter cwd/kind, instruction forbidding child delegation, absence of the source environment sentinel and no dispatch publication. The created worker has no channel binding. |
| Settlement side effects | `AgentTaskReplyIntegrationTests.Internal_conversion_settles_with_usage_but_without_sources_or_follow_up` records marked success with 19 input/7 output tokens, no bundle, follow-up, descendant, distillation record, or recursive delivery. A normal Custom companion produces its Markdown source byte for byte; existing Check/Distill/Diagnose companions remain in CP-2. |
| Policy and card companions | `OutputDistillationPolicyTests.Outbound_conversion_never_requests_distillation_even_with_a_session_reply_target` compares enabled normal Custom eligibility with linked conversion ineligibility. `CardWorkTransitionServiceTests.Unbound_conversion_worker_cannot_move_a_source_card_but_bound_work_can` proves the unbound Worker cannot move the source card while a bound Custom task can. |

This is an ordinary isolated verification verdict, not live V-25 or mutation
credit. R-7 as a whole remains open because V-11's deadlines/refusals remain.

## V-11 increment and remaining rows

`ChannelOutboundDeadlineTests.Deadline_crossing_the_final_creation_barrier_never_launches_a_worker`
holds the pump after its advisory lock, advances the test clock beyond the
deadline, and sees no worker, a Ready fallback, and one original publication.
Production `ChannelOutboundDeliveryPump.PrepareAsync` now checks the deadline
at that final pre-create boundary. The existing equality test proves queued
cancellation, preservation of a Working owner, one original send, and late
success ignored. `ChannelOutboundDeliveryTests` exercises `MaxPending=1`.

**V-11 remains open.** The whole row still needs distinct missing/busy/
unavailable/held converter and quota/auth/model refusals; blocked/failed task,
missing output/browser and text-only fallback; one-per-converter/two-global
capacity barriers across profiles; queued deadline consumption, selection and
pre-dispatch expiry; and a Queued-to-Working cancellation race. Each refusal
must retain original bytes and record an honest reason without provider reroute
or bypass. V-12–V-18 and V-23 gained no whole-ID closure in this round.

## Checkpoint receipts

The committed `dfe1f93d4` slice ran the full plan CP-1–CP-13 closed list at
`.antiphon/checkpoints/20260929-170834-5eae/`, each with a granted build slot:

| Row | Executed / passed / failed | Note |
|---|---:|---|
| CP-1 | 3485 / 3485 / 0 | 33 Linux platform skips |
| CP-2 | 373 / 373 / 0 | Green |
| CP-3 | 32 / 32 / 0 | Green |
| CP-4 | 57 / 57 / 0 | Green |
| CP-5 | 41 / 40 / 1 | Existing V-9 lease-takeover test raced its second claim; fixed in `a35f8dccd` |
| CP-6 | 27 / 27 / 0 | Green |
| CP-7 | 320 / 320 / 0 | Green |
| CP-8 | 71 / 69 / 2 | Inherited `PinnedAgentKindTests.T1/T2` `codex_desktop_unqualified` refusals |
| CP-9 | 19 / 19 / 0 | Green |
| CP-10 | 1 / 1 / 0 | Green |
| CP-11 | 125 / 125 / 0 | Green |
| CP-12 | 26 / 26 / 0 | Green |
| CP-13 | build exit 0 | Green |

The repaired CP-5 ran 41/41 at `.antiphon/checkpoints/20260929-173215-f1d0/`.
The latest CP-1 ran 3485/3485, 33 skipped, at
`.antiphon/checkpoints/20260929-173638-858f/` after the normal Custom
distillation companion. CP-2, CP-6 and all other unaffected full-run rows
were green. CP-8's two refusals match the inherited round-29 failures; no
introduced failure remains. Initial CP-6 brief inspection expected inline
text, but the real dispatcher spills it to a file; `097f42e96` corrected the
test and its same-row rerun passed 27/27.

Whole ordinary IDs now closed: V-1–V-10, V-19–V-22, V-24, R-1 and R-12.
Still open: V-11–V-18, V-23, R-2–R-11 and R-13–R-14. V-25 is the later live
gate; PC-1–PC-30 remain pending method-scoped SourceLanding Mutation.
