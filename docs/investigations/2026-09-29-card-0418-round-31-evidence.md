# CARD-0418 round 31: conversion refusals and sealed output

This continues the [round-30 evidence](2026-09-29-card-0418-round-30-evidence.md)
from `bd9fa821039a9434b1d9c9d84cfc389b0d6d7cf2`. The Code commits are
`f31b3578c` through `53c777016`. No shared stack, actual destination, land,
live provider, or SourceLanding Mutation was used.

## Whole V-11 verdict

**V-11 closes for the ordinary isolated profile.** These tests exercise the
real pump and task service, and each asserted refusal or boundary can go red.
The missing-browser result is covered at two boundaries: the optional renderer
rejects a missing browser in CP-9, and a failed worker carrying that reason
falls back with its original bytes in CP-5. This is not a live provider or
V-25 browser invocation.

| Boundary | Executed evidence |
|---|---|
| Create refusal | `ChannelOutboundDeadlineTests.Missing_and_unavailable_converter_fall_back_without_creating_a_task` distinguishes an absent pinned agent from an unavailable workspace. `Real_create_refusals_keep_the_original_without_provider_reroute` is three data rows that seed an actual subscription sample, missing Grok sign-in, or a kind-wide model hold, then drive `AgentTaskService` through the pump. All three produce a reasoned Ready fallback, zero conversion tasks, one original publication, and no second send. No create override or provider reroute is supplied. |
| Capacity and queue | `One_converter_and_two_global_seats_hold_pending_work_until_its_original_deadline` binds three distinct profiles/channels: converter A occupies one seat, a second A request stays Pending, B occupies the second global seat, and C stays Pending. At its original deadline each waiting request becomes a no-task fallback while A/B remain owned. `ChannelOutboundDeliveryTests.Concurrent_admission_respects_max_pending_per_channel` admits with MaxPending=1 under concurrent sends, then publishes both overflow replies with exact original source bytes and no conversion task. |
| Worker outcome | `Blocked_failed_and_missing_output_keep_original_bytes_and_record_distinct_reasons` checks Blocked authentication, Failed quota/model/browser, and Succeeded with no output manifest. It compares original attachment bytes, distinct bounded reasons, and one publication each. The first row is text-only EveryAgentReply and checks the text-only annotation without invented source attachments. The optional renderer's explicit missing-browser and failure cases are in CP-9. |
| Expiry and owner | `Deadline_crossing_the_final_creation_barrier_never_launches_a_worker` catches pre-create expiry. `Deadline_equality_cancels_only_queued_worker_and_ignores_late_success` conditionally cancels Queued, preserves Working and its session, and ignores late success. `Linked_worker_expires_before_selection_or_at_the_final_dispatch_claim` uses the real dispatcher with a linked Custom worker, including a held `FOR UPDATE` claim: both rows cancel without session/input launch, and the pump alone sends the original. `Queued_to_working_race_preserves_the_owner_and_sends_once_at_deadline` changes Queued to Working behind the pump's observation barrier; the conditional cancel loses, the owner survives, and the original sends once. |

R-7 now closes with V-10's normal worker-purpose evidence and V-11's no-bypass,
deadline, owner, and fallback matrix. The test uses fixture-only credentials and
never attempts a real subscription or live sign-in.

## Whole V-12 verdict

**V-12 closes for the ordinary isolated profile.** The existing
`OutboundConversionManifestTests.Valid_results_add_files_and_seal_bytes`
validates `unchanged`, replacement text, original source retention, PDF/PNG/TXT
additions, manifest authority, and byte sealing. The new
`OutboundConversionManifestTestsPump.Sealed_pump_send_ignores_changed_worker_output_and_deleted_source_worktree`
drives two successful conversion tasks through `ObserveConversionAsync` and
`PublishReadyAsync`. At the committed Ready barrier it overwrites each worker
output, deletes each manifest, and deletes the source task's worktree. Each
published reply still matches its own sealed JSON bytes under
`MessagingJson.Options`, retains its own source bytes/routing, and has only its
own manifest-listed additions. A prose attach marker in `report.md` grants no
attachment. The second tick sends nothing.

## V-13 increment and remaining rows

`ChannelOutboundStorageTests.Source_zip_manifest_rejects_unsafe_or_ambiguous_members`
now has adjacent valid-control rows for forged entry length, forged SHA-256,
and a missing named entry. The valid control asserts the input directory has
only the attached zip, one selected Markdown source, and the source manifest;
unlisted traversal, absolute, and case-colliding zip members are not extracted.
The existing exact 64 MiB and +1 streaming expansion tests remain green.

**V-13 remains open.** Its remaining whole-row proof includes a rename/link
swap between inspection and sealed copy, the full I/O denial/full-disk and
admission retry boundary, and a terminal storage failure when accepted
originals corrupt or disappear. V-14–V-18 and V-23 were not started; their
whole-ID and dependent R-ID verdicts remain open. V-25 is a later live gate.
PC-1–PC-30 remain pending method-scoped SourceLanding Mutation; ordinary and
nightly green runs do not discharge them.

## Checkpoint receipts

The full CP-1–CP-13 closed-list run on `b1590110214bcc3812479af49bda3bd666b4ebef`
is `.antiphon/checkpoints/20260929-180608-4958/`. Each driver received a
build slot. CP-8's two failures are the inherited `PinnedAgentKindTests.T1/T2`
`codex_desktop_unqualified` refusals from rounds 29–30; no introduced failure
remains. Latest selective rows below ran after their committed slice groups.

| Row | Latest executed / passed / failed | Receipt and note |
|---|---:|---|
| CP-1 | 3488 / 3488 / 0 | `.antiphon/checkpoints/20260929-191219-b677/`; 33 platform skips |
| CP-2 | 373 / 373 / 0 | Full run |
| CP-3 | 32 / 32 / 0 | Full run |
| CP-4 | 61 / 61 / 0 | `.antiphon/checkpoints/20260929-191219-b677/` |
| CP-5 | 50 / 50 / 0 | `.antiphon/checkpoints/20260929-185503-d6bd/` |
| CP-6 | 27 / 27 / 0 | Full run |
| CP-7 | 320 / 320 / 0 | Full run |
| CP-8 | 71 / 69 / 2 | Full run; inherited Codex qualification refusals |
| CP-9 | 19 / 19 / 0 | Full run |
| CP-10 | 1 / 1 / 0 | Full run; headed real-browser row |
| CP-11 | 125 / 125 / 0 | Full run; isolated broker settings |
| CP-12 | 26 / 26 / 0 | Full run; client test log, exit 0 |
| CP-13 | build exit 0 | Full run |

The first CP-5 build failed on duplicate partial-class attributes; its rerun
found an object-versus-id fixture query mistake. Both were corrected before
the 45/45 receipt at `20260929-180130-15ac`. The first real-refusal CP-5 run
passed 47/48 because the test expected “held” while the actual model gate said
“disabled”; the corrected row passed 48/48 at `20260929-183558-5328`.
Subsequent CP-5 slice groups passed 50/50 at `20260929-184203-b99a`,
`20260929-184657-645f`, and `20260929-185503-d6bd`. CP-4 passed 58/58 for
V-12 at `20260929-190046-2d5e`. The first V-13 CP-1/CP-4 run had nine
failures in both rows because the fixture's expected input list omitted its
legitimate `source-manifest.json`; the corrected CP-1/CP-4 rerun passed
3488/3488 and 61/61 at `20260929-191219-b677`. All runs used the checkpoint
tool; no unlisted build or test command was run.

Whole ordinary IDs now closed: V-1–V-12, V-19–V-22, V-24, R-1, R-7 and
R-12. Still open: V-13–V-18, V-23, R-2–R-6, R-8–R-11 and R-13–R-14.
