# CARD-0418 round 32: V-13 storage boundary and V-14 route progress

This continues the [round-31 evidence](2026-09-29-card-0418-round-31-evidence.md)
from `d8e05c4ef232aa0cdc11d766b7d745b8c748c901`. The Code commits are
`89f922fff`, `847ed55e7`, `f2c0e0523`, `eaa26b749`, `0ee61d5e4`, and
`50e94c37b`. The final full checkpoint run used `50e94c37b5e8449bff7e685879e096ce732ea32f`.
No shared stack, actual destination, land, live provider, or SourceLanding Mutation
was used.

## V-13 and R-8 ordinary verdict

**V-13 and R-8 close for the ordinary isolated profile.** Round 31 already
exercised the manifest, path, zip, expanded-byte, serialization and output
refusal matrices. The remaining rows now exercise the following boundaries:

| Boundary | Executed oracle |
|---|---|
| Rename/link swap | `ChannelOutboundStorageTests.Output_rename_and_link_swap_after_inspection_seals_the_opened_file` renames the inspected worker output after the sealer has opened it, replaces its path with a fixture-owned symlink to unrelated sentinel bytes, and asserts that the sealed attachment still contains the inspected bytes. `ChannelOutboundFileStore` now reads through the retained handle and rechecks link status immediately after open. The sentinel remains unchanged. This exercises the deterministic inspection-to-copy race; it is not a claim of an atomic cross-platform no-follow open against every possible attacker interleaving. |
| I/O and admission retry | `Failed_staging_never_exposes_a_partial_snapshot` has separate partial write, complete temporary write, rename collision, disk-full and access-denied rows. Each leaves no accepted partial snapshot. `Staging_io_failure_leaves_correlation_owed_and_same_source_retry_admits_once` injects disk-full at the complete temporary barrier of the actual file store, sees zero durable intents and an unstamped, unlinked correlation from a fresh connection, then retries the identical source twice after recovery: one Pending intent, one correlation link, no producer send. |
| Accepted original lost | `Accepted_original_storage_failure_is_terminal_without_a_false_send` has corrupt and missing original rows. A Ready accepted intent with exact source bytes becomes Failed with a recorded storage reason, no Published stamp, zero publication attempts, no producer acceptance, and no second-tick retry. |

The file-store race oracle is Linux-executed with a fixture-owned symbolic link;
the plan's separate Windows reparse behavior remains dependent on its native
platform lane. No untrusted Source URL or unrelated local path is used as
worker input in these tests.

## V-14 increment and remaining work

`ChannelOutboundDeliveryTests.Deferred_intent_freezes_route_and_only_publication_stamps_correlation`
now obtains T1 and T2 from `SlackChannelAdapter.ReceiveAsync` against
`FakeSlackServer`. T1 is accepted before T2 arrives. The test changes the
catalog's latest handle to T2, then verifies A's frozen Channel,
ConversationId, ReplyHandle, ReplyToMessageId, Kind, RawOverrides, text and
original source metadata/bytes, as well as B's T2 handle. An unfinished conversion
keeps B and a later ready text-only reply behind it; a control notice bypasses
the queue. On worker success the producer gets A in T1, then B in T2, then the
plain follow-up, preserving the source and adding only the manifest PDF.
CP-6's existing independent-conversation dispatch test still sends Y while X
waits.

**V-14 remains open.** Its whole-ID acceptance still needs a process restart
after both same-channel replies are durable, plus explicit Held and
PublishUncertain head ordering/resolution. The current test uses a fresh DB
context but no process restart. V-15–V-18 and V-23 were not started in this
round; their whole-ID and dependent R-ID verdicts remain open. V-25 is a later
authorized live gate. PC-1–PC-30 remain pending method-scoped SourceLanding
Mutation; ordinary and nightly green runs do not discharge them.

## Checkpoint receipts

The complete CP-1–CP-13 closed list ran at
`.antiphon/checkpoints/20260929-200757-86d4/` on
`50e94c37b5e8449bff7e685879e096ce732ea32f`. Every build/test driver
received a build slot. Twelve rows passed; CP-8 had only the inherited
`PinnedAgentKindTests.T1/T2` `codex_desktop_unqualified` refusals already
recorded in rounds 29–31. There were no unlisted build/test commands.

| Row | Executed / passed / failed | Note |
|---|---:|---|
| CP-1 | 3491 / 3491 / 0 | Whole Unit lane; 33 platform skips |
| CP-2 | 373 / 373 / 0 | Four source-settlement classes |
| CP-3 | 32 / 32 / 0 | Four policy/schema classes |
| CP-4 | 64 / 64 / 0 | Storage and output manifest classes |
| CP-5 | 53 / 53 / 0 | Delivery, task, deadline, public-create, transition classes |
| CP-6 | 27 / 27 / 0 | Crash/dispatch/composed transport classes |
| CP-7 | 320 / 320 / 0 | Eight routing/attention classes |
| CP-8 | 71 / 69 / 2 | Inherited Codex desktop qualification refusals |
| CP-9 | 19 / 19 / 0 | Optional renderer classes |
| CP-10 | 1 / 1 / 0 | Headed real-browser PDF case |
| CP-11 | 125 / 125 / 0 | Gateway/adapter/monitor/broker classes |
| CP-12 | 26 / 26 / 0 | Two client files; wrapper exit 0 |
| CP-13 | build exit 0 | Production client bundle |

Selective slice receipts: `20260929-192803-081c` passed CP-1 3489/3489 and
CP-4 62/62 for the file-swap slice; `20260929-193655-f3d3` passed CP-1
3491/3491, CP-4 64/64 and CP-5 53/53 for the storage-failure slice;
`20260929-195543-0c73` passed CP-1 3491/3491 and CP-5 53/53 for the V-14
fixture correction. The first file-swap CP-1/CP-4 build at
`20260929-192635-e5b9` failed because `FileOptions.OpenReparsePoint` is not
available in this target framework; the supported open-handle implementation
passed the rerun. The first V-14 CP-5 at `20260929-194716-8d85` passed 52/53:
its plain fixture inherited a Markdown attachment and therefore correctly
matched MarkdownSources. The text-only fixture correction passed the rerun.

Whole ordinary IDs now closed: V-1–V-13, V-19–V-22, V-24, R-1, R-7, R-8 and
R-12. Still open: V-14–V-18, V-23, R-2–R-6, R-9–R-11 and R-13–R-14.
