# CARD-0418 round 21: source read-path closure and gateway monitor contract

This continues the [round-20 ledger](2026-09-29-card-0418-round-20-evidence.md). The tested source tip is `fc013afd72918391d56048e70dd5bd8bce3024a9`. No shared-stack restart, live destination, land, or Mutation positive control was attempted.

## Exact ordinary ID accounting

**V-2 closed.** The branch-only Git source ZIP case was already present in the round-20 source and executed in that round's CP-2, but its ledger incorrectly left the case open. This sweep's CP-2 TRX explicitly records `DeliverableBundleServiceTests.Branch_only_git_sources_survive_zip_with_exact_bytes_and_manifest_hashes` as Passed. The test commits six UTF-16 BOM/CRLF/Unicode/trailing-space Markdown files on a branch, removes their disk copies, then checks all six extracted ZIP bytes against the originals and checks each manifest path, length, SHA-256, ZIP entry and complete/no-omission status. `Source_bytes_survive_each_disk_read_root` and `Source_bytes_from_all_disk_roots_survive_a_single_zip` cover the three disk roots in inline and ZIP forms. `SourceBundleManifestTests.Bytes_names_thresholds_and_budget_are_truthful` checks five inline, six zipped, exactly 1 MiB inline, +1 zipped, 64 selected paths, distinct duplicate-basename inline names, exact 64 MiB retention, +1 omission identification and incomplete warning. The 41-source case also asserts no `.Take(40)` loss. This closes the V-2 read-path and packaging cross-product without attributing a new test to this round.

**V-22 closed.** New `LibrarySufficiencyTests.Configured_inbound_monitor_and_outbound_consumer_keep_distinct_contracts` checks the deployed service and server JSON together: both share inbound/outbound topics, the gateway's watched and expected group equal the server's `antiphon-server-bridge`, and the gateway's outbound consumer group remains distinct. CP-11's 123/123 includes this named method, the eight existing gateway/adapter/monitor classes, and the real broker `KafkaOutboundPayloadTests` cases; CP-7's 320/320 includes `ChannelConsumerIdentityEndpointTests`. The existing monitor tests exercise absent group, query failure, missing partition and no commit as unknown without notice or watermark, then a committed old offset as a positive lag notice. Gateway, Slack and Telegram tests check the unchanged ChannelReply transport, inline bytes, thread uploads and formatting/fallback. The configuration assertion is a new test of the actual files, not a claim that a live gateway was changed or contacted.

Closed this round: **V-2, V-22**. Previously closed: **V-1, V-3, V-4, V-19–V-21, V-24, R-1, R-12**. Still open: **V-5–V-18, V-23, R-2–R-11, R-13–R-14**. The [round-17 inventory](2026-09-28-card-0418-round-17-evidence.md#remaining-ordinary-vr-assertions) gives each still-missing sub-assertion, excluding the IDs closed since then. R-2 still needs V-16's publication completeness matrix. R-13 still needs V-23's full serialized-budget and near-default broker matrix. R-14 still needs a per-sub-assertion actual-run index and a validator that rejects broad-ID tokens. V-25 remains for a separately authorized actual destination receipt. PC-1–PC-30 remain for method-scoped SourceLanding Mutation; no checkpoint here discharges one.

## CP-1 through CP-13 Final sweep

The checkpoint tool ran the plan's complete closed list on `fc013afd72918391d56048e70dd5bd8bce3024a9` in `.antiphon/checkpoints/20260929-022733-1554/` (Debian 12, 30m04s). Its native `report.md`, TRX, CP-8 failure detail, and host/slot records are there. Every row held a granted host build slot. The tool ran no unlisted row command. A separately leased bootstrap build of `tools/Antiphon.Checkpoints` succeeded before the sweep because this worktree had no built tool; it had the existing `TaskOwnerGuard.cs` CS8602 warning and was the only unlisted build.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3484 | 3484 | 0 | 33 | Green; Linux platform skips |
| CP-2 source settlement | 371 | 371 | 0 | 0 | Green; branch-only Git ZIP case observed |
| CP-3 policy/schema | 30 | 30 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 6 | 6 | 0 | 0 | Green |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Red: inherited T1/T2 `codex_desktop_unqualified` |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green on Linux; Windows descendant evidence remains round 15 |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green |
| CP-11 gateway wire | 123 | 123 | 0 | 0 | Green with `ANTIPHON_BROKER_TESTS=1`; new V-22 assertion observed |
| CP-12 client | 26 | 26 | 0 | n/a | Green; `CLIENT TESTS EXIT CODE: 0` |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

Overall tool exit **1**: twelve green rows and CP-8 red. Its two failures are the identical inherited `PinnedAgentKindTests.T1`/`T2` `codex_desktop_unqualified` pair from rounds 17–20; this slice changed only a gateway configuration test. The sweep verifies executed local assertions and does not close the remaining V/R IDs, V-25, or any PC. Do not land from this evidence.
