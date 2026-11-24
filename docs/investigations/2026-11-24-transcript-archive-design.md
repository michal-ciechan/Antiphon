# Monthly transcript archive rehearsal

**Decision evidence.** Three monthly gzip files preserved all 1,003,430 age-matched transcript rows in 215,771,196 bytes (215.8 MB). Each file passed a typed PostgreSQL restore and byte-for-byte re-export. Zero rows passed the retention guards, so the measured safe reclaim is 0 GB, both for database reuse and for filesystem recovery.

## Scope and provenance

- Snapshot `antiphon-20261124T090703Z.dump`, 905,456,635 bytes. Desktop and server2 independently verified SHA-256 `36ad51c138a100cf834d4663a3e86922f02520a8076508056381e3b55c41b89f`. The prior drill and production census supply the comparison, including its sampled 0.835 GB footprint. [Backup drill](2026-11-24-server2-db-backup-verification.md#4-restore-proof-scratch-on-server2) · [Prior census at fb29966671bef641661552866f3f0f5415e6b5fa](https://github.com/michal-ciechan/Antiphon/blob/fb29966671bef641661552866f3f0f5415e6b5fa/docs/investigations/2026-11-24-db-size-and-transcript-retention-plan.md)
- Code inspected at `4c72f7a2632456bffa47179b6849d089d728acfc`. Server2 reported 189,017,788,416 bytes free on the shared Docker and scratch filesystem before allocation. The scratch database `archive_probe` used the local `postgres:16` image (`f7ebf09eb01b02bb9904972705248217baeaed03e2a0a7ed24a77740b0aee98c`, PostgreSQL 16.13), network disabled and no published ports. `pg_restore --exit-on-error --no-owner --no-acl` completed in 253.231 seconds, 19:04:35–19:08:49 UTC.
- The archive window is strictly `CreatedAt < 2026-10-25 09:07:03Z` (backup filename time minus 30 days) and uses UTC calendar months. October is partial. Native `Timestamp` is retained with its original nullability, and its bounds are recorded separately. Only `public."TranscriptEntries"` was measured for archiving.
- The restored table held 3,748,263 rows and 2,943,696,896 allocated bytes (2,034,958,336 in main heap and 604,774,400 in base indexes). It held 1,003,876 rows before the prior production census's later cutoff of October 25 18:50 UTC, compared with that census's sampled estimate of approximately 987,000. The restored database size was 5,037,202,455 bytes. Sizes after logical restore are specific to that copy.
- The scratch catalog showed the primary key on `Id`, the foreign key to `AgentSessions.Id` with `ON DELETE CASCADE`, and the inbound foreign key from `SessionRuntimeActions.TranscriptEntryId` with `ON DELETE RESTRICT`. All SQL and archive operations for this investigation ran on scratch. Production database and backup files were untouched.

## COPY and restore evidence

On scratch, PostgreSQL streamed UTF-8 CSV with a header and its default null/quoting rules, using all columns in catalog order and sorting by `("CreatedAt", "Id")`. Gzip level 6 was used because zstd was absent on server2. The restore table was created with `LIKE public."TranscriptEntries" INCLUDING ALL` and emptied before each file. This exercises column types, nullability and indexes. The source's foreign-key relationships require a separate check on any real reinsertion.

For each month, the probe hashed raw COPY output as it compressed it, hashed the compressed file, re-opened that file, and hashed the decompressed bytes while streaming them into PostgreSQL. It checked the `COPY n` receipt and destination count and extrema against the source, re-exported the restored table using the original column order and sort, and compared the raw byte count and SHA-256 across all three streams. It also independently rehashed the compressed file. COPY counts records containing quoted line breaks as single rows. [PostgreSQL 16 COPY](https://www.postgresql.org/docs/16/sql-copy.html)

| Month UTC | Rows and COPY receipt | CSV bytes | Gzip bytes | Export + gzip, s | Re-read + COPY in, s | Verify restored and gzip, s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-08 | 28,885 | 29,416,758 | 9,336,586 | 1.907 | 1.394 | 1.043 |
| 2026-09 | 499,496 | 300,979,756 | 63,540,687 | 14.239 | 19.608 | 4.520 |
| 2026-10 | 475,049 | 489,775,043 | 142,893,923 | 29.959 | 36.759 | 8.950 |
| Total | 1,003,430 | 820,171,557 | 215,771,196 | 46.105 | 57.761 | 14.513 |

The end-to-end archive run took 125.786 seconds, including setup, source counts and hashes. The gzip total is 26.3% of the raw CSV and about 26% of the earlier 0.835 GB estimate for allocated database storage. The latter comparison includes PostgreSQL heap, TOAST and indexes and uses a different snapshot. File size measures off-host storage.

| Month | Min and max `Id` in UUID text order | Min and max ingestion `CreatedAt` UTC | Min and max native `Timestamp` UTC |
| --- | --- | --- | --- |
| 2026-08 | `00002214-a12d-4a34-b7d6-46a2b2f8411c`<br>`fffd04e9-3333-487a-8616-d1a30125b51d` | 08-13 19:45:05.141317<br>08-31 20:42:36.023381 | 08-13 19:44:31.326<br>08-31 20:42:34.646 |
| 2026-09 | `00000c39-4c01-4ee2-beaa-7ccbdf8d6adb`<br>`fffffd58-c372-45ad-bef3-ff4e8ab79b66` | 09-01 08:03:16.586159<br>09-30 23:59:59.101872 | 09-01 04:05:59.601<br>09-30 23:59:57.987 |
| 2026-10 | `00001d8a-37ac-4375-a6b2-65c40583ab0f`<br>`fffffe0e-f9a8-49a1-85bd-ebc3c2528c87` | 10-01 00:00:01.306549<br>10-25 07:50:08.045264 | 09-27 00:32:50.439<br>10-25 07:50:07.630 |

There were zero null native timestamps in the exported cohort. All dates in the extrema table are in 2026.

| File | Gzip SHA-256 | CSV SHA-256, matched by re-export |
| --- | --- | --- |
| `TranscriptEntries-2026-08.csv.gz` | `00738666e2679590419567a5f9bb4d111abc3c205677c0d7b3d99272290f0c29` | `a3bf24d1fb476a5393985df5f84526fcb14acf35d68cdb01041a10bf2ec3ce02` |
| `TranscriptEntries-2026-09.csv.gz` | `64a79690b2407ea3689ea911bd259fe32b8079573b3dfa566738b31c84982c18` | `8f56f34047a1291c3294b68251ab0d88ad92978b649c9197311ec1f04e79b623` |
| `TranscriptEntries-2026-10.csv.gz` | `9dca834e301e25098c41b866cb02c65f0436b578f539060f9d99a3ceb53e6d73` | `5d78a61a0198feb96d800047779fa1defb7702368978eed88f02005e8e5fb818` |

The local ignored evidence directory `.antiphon/task-bd6e2b45-probe/` contains the scratch-only driver, census SQL and aggregate receipts. The manifest includes all 26 ordered columns and their types. Manifest SHA-256 is `581405099264231cd93ee0a37c51c1c78763a48997ff74b19e8e5c00e64fe978`.

### Reproducing the monthly export on a verified restore

```sql
SET TIME ZONE 'UTC';
SET DateStyle TO 'ISO, YMD';
-- For each month, substitute its two UTC endpoints. Use catalog ordinal column order.
COPY (
  SELECT * FROM public."TranscriptEntries"
  WHERE "CreatedAt" >= timestamptz '2026-10-01 00:00:00Z'
    AND "CreatedAt" < timestamptz '2026-11-01 00:00:00Z'
    AND "CreatedAt" < timestamptz '2026-10-25 09:07:03Z'
  ORDER BY "CreatedAt", "Id"
) TO STDOUT WITH (FORMAT csv, HEADER true, ENCODING 'UTF8');
```

Stream those bytes through gzip level 6 with a deterministic header (no filename or timestamp) while hashing raw bytes. Restore the gzip stream with `COPY archive_probe."TranscriptEntries" FROM STDIN WITH (FORMAT csv, HEADER true, ENCODING 'UTF8')` using the same column order. Run the original sort on the restored rows before comparing hashes.

## Why the discovery guard persists

`DiscoverySources` selects `Sent` queue rows with null `ChannelReplySettledAt`, `ChannelOutboundDeliveryId` and `ChannelReplyDiscoveryClosedAt`. Eligible origins are Channel with a conversation key, plus Delegation, Check, System and Scheduled. It protects the entire session even when a single source qualifies. The two additional outbound guards cover an open direct-reply tail and an unfinished delivery, including a published delivery awaiting metadata or an unclosed captured root. [ChannelOutboundEvidence.cs:10–28](../../server/Application/Services/ChannelOutboundEvidence.cs#L10-L28)

The close marker is nullable and the migration did not backfill it. Discovery is opt-in and off by default in the inspected code, while retention applies the protection unconditionally. The historical scanner wraps and revisits unresolved sources. The dump establishes the stored states, with effective historical configuration and whether a particular source was scanned still open. [Migration:14–19](../../server/Migrations/20261004231302_ExtendChannelOutboundRecovery.cs#L14-L19) · [ChannelOutboundSettings.cs:6–13](../../server/Application/Settings/ChannelOutboundSettings.cs#L6-L13) · [ChannelOutboundDiscoveryService.cs:28–68](../../server/Application/Services/ChannelOutboundDiscoveryService.cs#L28-L68) · [DataRetentionService.cs:315–323](../../server/Application/Services/DataRetentionService.cs#L315-L323)

For a machine source, the normal close path first requires a prompt matching its persisted attempt and a `TurnEnd` owned by that prompt. It then requires a later turn-opening prompt or terminal proof for the same generation. Terminal proof comes from the runner and requires a complete matching snapshot, payload persisted and checked, and the generation still current. An old `EndedAt` alone gives no such proof. If the attempt has no baseline, the current machine matcher needs `LastDeliveryStartedAt` and native time. A `Sent` row can lack a delivery verdict because that status can be stamped before typing. [ChannelPromptCorrelation.cs:135–196](../../server/Application/Services/ChannelPromptCorrelation.cs#L135-L196) · [ChannelReplyDispatcher.cs:419–488](../../server/Application/Services/ChannelReplyDispatcher.cs#L419-L488) · [ChannelOutboundTerminalTranscript.cs:11–49](../../server/Application/Services/ChannelOutboundTerminalTranscript.cs#L11-L49) · [DeliveryVerdict.cs:3–7](../../server/Domain/Enums/DeliveryVerdict.cs#L3-L7)

A machine source with a provable complete turn and a missing earlier routed Channel message already meets the code's silence rule once the next opener or terminal proof is present. With an earlier route, response text, API error and attachment checks also matter. The stored earlier-route test requires a sent Channel message, a non-null conversation key, a lower queue sequence and an attempt/sent/creation time no later than the machine source. A missing route in the restored database leaves historical route custody uncertain. [ChannelReplyDispatcher.cs:456–482](../../server/Application/Services/ChannelReplyDispatcher.cs#L456-L482) · [ChannelReplyDispatcher.cs:1762–1773](../../server/Application/Services/ChannelReplyDispatcher.cs#L1762-L1773)

### Row census

Counts below cover the selected `CreatedAt` window across 4,483 distinct sessions. Guards overlap. Direct tails protected 1,498 rows, all of which discovery also protected. The dump held zero `ChannelOutboundDeliveries` rows. Of the full 1,003,430-row cohort, discovery guarded 963,080 rows (96.0%).

| Month UTC | Age-matched rows | Discovery or other outbound guard | Clear of all outbound guards | Open work link | Persistent session | Pass current retention checks | If all discovery sources cleared, with extra holds |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-08 | 28,885 | 18 | 28,867 | 28,545 | 322 | 0 | 0 |
| 2026-09 | 499,496 | 488,013 | 11,483 | 371,603 | 93,292 | 0 | 11,215 |
| 2026-10 | 475,049 | 475,049 | 0 | 181,208 | 218,100 | 0 | 101,725 |
| Total | 1,003,430 | 963,080 | 40,350 | 581,356 | 311,714 | 0 | 112,940 |

Of 6,819 terminal sessions with old `LastSeenAt`, 1,139 survived the shipped metadata guards and zero of them had any stored transcript rows. The rightmost column relaxes just discovery and applies the extra session holds in the next section. It gives an upper bound of 1,250 sessions and 112,940 rows under that scenario. It provides no clearance to remove those rows.

### Unresolved source census

The dump had 45,697 open discovery sources overall. The table below restricts them to old ended sessions (`Stopped` or `Failed`, `EndedAt < cutoff`) that have at least one age-matched transcript row. The unique session total allows overlap across origins. The prior route column applies the code's sequence-and-time test.

| Origin | Sources | Distinct sessions | Earlier stored route | Created before close-marker migration timestamp | Verdict recorded | Session has runner store ID |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| System | 1,801 | 969 | 0 | 278 | 1,590 | 995 |
| Delegation | 17,056 | 4,399 | 0 | 6,464 | 17,052 | 3,957 |
| Check | 8,420 | 9 | 0 | 3,738 | 8,420 | 0 |
| Scheduled | 7 | 6 | 0 | 4 | 7 | 0 |
| Total | 27,284 | 4,401 | 0 | 10,484 | 27,069 | 4,952 |

This old ended cohort had zero open Channel-origin sources. Across the whole dump, 318 Channel queue rows had `ChannelReplySettledAt` and zero queue rows had `ChannelReplyDiscoveryClosedAt` or `ChannelOutboundDeliveryId`. There were 111 old System sources with both zero delivery attempts and no attempt baseline/start, so those rows cannot match today under the machine matcher. The other sources include both older and newer metadata generations. A runner store ID identifies a store and supplies no transcript-completeness proof.

### What stored evidence can establish

The scratch probe selected the owning prompt for each stored `TurnEnd` using the code's preference for the latest `UserPrompt` in its window, falling back to the latest `QueuedUserPrompt`. For machine sources with no stored route, it looked for the full raw source body as a trimmed prompt prefix, checked attempt generation and baseline, or required native time at or after the attempt start when there was no baseline. It filtered out local commands, synthetic continuations and marked transport prompts. This deliberately strict subset omits normalized-only and batched matches, and omits the configured clock tolerance. A later opening prompt is sufficient for the structural branch without an EOF check. A source with only a matched final turn still needs exact runner-generation proof for that observed match. [TranscriptTurnWindow.cs:42–105](../../server/Application/Services/TranscriptTurnWindow.cs#L42-L105) · [ChannelPromptCorrelation.cs:135–196](../../server/Application/Services/ChannelPromptCorrelation.cs#L135-L196)

| Origin | Owning turn and later opener | Owning turn, no later opener found | Neither strict match found, attempt fields present | Missing attempt |
| --- | ---: | ---: | ---: | ---: |
| System | 1,206 | 170 | 314 | 111 |
| Delegation | 3,475 | 2,670 | 10,911 | 0 |
| Check | 4,521 | 1 | 3,898 | 0 |
| Scheduled | 3 | 0 | 4 | 0 |
| Total | 9,205 | 2,841 | 15,127 | 111 |

The 9,205 sources have stored structural evidence for the no-route silence branch and still lack its durable close marker. The 2,841 final-turn cases require runner or equivalent durable terminal evidence for the observed turn. The remaining 15,238 include sources whose provenance this matcher cannot establish and sources that cannot match with their current metadata. None of these classifications establishes permanent impossibility. The probe did not call the dispatcher or a runner and did not stamp closure.

For example, session `fdf1dd3d-da3e-4c68-8e3f-1215b07cceb9` contains both source `9f25cb3d-05ee-407e-8e79-a707df754f57` (baseline 12,586, owning turn and later opener) and source `3f271f99-d3fa-48c5-94f4-092770a60866` (baseline 1,581, no strict match). Source `8576d774-dd6e-4da7-9f41-b42202c77e66` in session `91e2a58b-1e50-4824-9662-6663b5f707e9` matched an owning final turn. Source `954fd480-9b1d-4327-9be4-613035e2f3d2` in session `39c6eb3a-5e96-4f67-9b09-253c106a1d64` lacked attempt evidence. These are `SessionQueuedMessages.Id` and `AgentSessionId` anchors in the restored dump.

Sessions for which **every** unresolved discovery source met the strict later-opener check accounted for 17,409 exported rows. If all those sources had durable closure, only 1,606 rows would pass the other conservative holds (77 in September, 1,529 in October). This is a measured candidate cohort for further review. Current eligible rows remain zero. The broader 112,940-row upper bound assumes clearance of discovery where evidence is still missing.

The successful query and receipt are `census-staged.sql` and `census-staged.txt` in the local evidence directory. Two earlier whole-query matching attempts hit the 180-second scratch statement timeout. Materializing the 69,559 prompt/boundary records and indexing the owned prompt prefixes let the final matching stage classify all 27,284 sources in 1.740 seconds, following setup and staging.

## Eligibility to remove an archived session

Apply the live-equivalent guards as a conjunction over each session, then check its full transcript. The rehearsal used `Stopped` or `Failed`, `LastSeenAt < cutoff`, no persistent binding, and no outbound protection from any of the three predicates above. It also excluded unresolved supervision notices on either side of the delivery, any runtime action, unresolved check-compaction source/resume references, captured legacy check publications or produced publications lacking a confirmed notice, unconfirmed land notifications except `NotRequired`, unmaterialized warning intents except `NotRequired`, and queued inbounds with a retained envelope. A nonempty transcript is eligible only if its maximum `CreatedAt < cutoff`. The mutation path checks freshness and selected guards again under the ingest gate. The extra rehearsal holds require `EndedAt < cutoff`, no pending queue row and no open-work link through a card owner, the session's card, or a queued, dispatched, working, blocked or open-card task's child, parent, specialist or ceiling-hold session. Open cards exclude Done and Canceled. [DataRetentionService.cs:301–372](../../server/Application/Services/DataRetentionService.cs#L301-L372) · [TaskSupervisionNoticeService.cs:13–19](../../server/Application/Services/TaskSupervisionNoticeService.cs#L13-L19)

An unresolved discovery row counts as cleared only after the existing source predicate becomes false from durable settlement, durable linkage whose delivery/tail is also clear, or a durable policy-silence close. This investigation established no class of permanently unresolvable sources that can bypass that predicate. Keep those sessions online. A session that spans two or more monthly files must pass verification in every affected file before it is eligible as a unit.

| Consumer | What the SQL readers require |
| --- | --- |
| Delivery and outbound | Prompt identity, attempt floors, turn ownership, assistant output and tail sequences. Durable `UserPrompt` receipt drives delivery confirmation. [SessionMessageQueueService.cs:4806](../../server/Application/Services/SessionMessageQueueService.cs#L4806) · [ChannelReplyDispatcher.cs:428](../../server/Application/Services/ChannelReplyDispatcher.cs#L428) |
| Reports and reviews | Report selection re-reads prompt spans and turn boundaries. Report tool receipts join source transcript rows. Review dispatch parses transcript boundaries and response text. [TaskReportTurnSelector.cs:64](../../server/Application/Services/TaskReportTurnSelector.cs#L64) · [AgentTaskReplyService.cs:3569](../../server/Application/Services/AgentTaskReplyService.cs#L3569) · [ReviewReplyDispatcher.cs:28](../../server/Application/Services/ReviewReplyDispatcher.cs#L28) |
| Resume and replay | A terminal card session can be resumed subject to provider and ownership checks. Ingest reconstructs sequence and UUID identity from stored rows. State loading probes transcript boundaries. [AgentSessionService.cs:2191](../../server/Application/Services/AgentSessionService.cs#L2191) · [TranscriptIngestPrelude.cs:27](../../server/Infrastructure/Data/TranscriptIngestPrelude.cs#L27) · [SessionStateLoader.cs:51](../../server/Infrastructure/Data/SessionStateLoader.cs#L51) |
| History and exports | The transcript API reads by session and sequence and returns the last observed sequence. Diagnostics select transcript rows from the same table. [AgentSessionService.cs:2350](../../server/Application/Services/AgentSessionService.cs#L2350) · [DiagnosticsBundleService.cs:231](../../server/Application/Services/DiagnosticsBundleService.cs#L231) |

## Operator decision and custody

1. The measured deletion batch is zero rows. A file-only copy can be considered independently of space reclamation. Use a separate archive directory under the existing OneDrive backup root, `C:\Users\lndco\OneDrive\Backups\antiphon-postgres\transcript-archive\<snapshot-UTC>\`, so the ordinary dump rotation does not own these files. Keep `TranscriptEntries-YYYY-MM.csv.gz` and a manifest together in the snapshot directory. The scope and cutoff in the manifest identify partial months and prevent overwriting an older generation. The test wrote no OneDrive files. [Existing backup folders](2026-11-24-server2-db-backup-verification.md#2-backup-mechanisms-found)
2. Before any separately approved removal, take a fresh server2 dump, verify its hash, and rehearse against the isolated restore. Stage the archive under restricted access because the files contain the original transcript and tool payloads. Verify the compressed and raw hashes and the typed restore, publish the verified files with the manifest last, and verify the synced copy by downloading it and matching the manifest. Keep the verified backup and exact session and row IDs for every committed batch.
3. For a later eligible cohort, the prior census's proposed starting limit is one complete session per transaction, one worker, oldest newest-entry first with session ID as tie-break, at most 1,000 rows in the initial canary and a one-second pause between batches. Hold oversized sessions for rehearsal. Recheck whole-session guards and archive membership inside the application's mutation/reseed boundary. Compare against a 15-minute baseline for database latency, locks and slow queries, and watch ingest/delivery errors, WAL, dead tuples, autovacuum, heap/TOAST/index bytes and host free space. Stop on a lock timeout, fresh runtime errors or sustained latency increase. A direct SQL delete would bypass the application state gate, and partial edits can invalidate prompt-span cache assumptions. [Prior canary proposal](https://github.com/michal-ciechan/Antiphon/blob/fb29966671bef641661552866f3f0f5415e6b5fa/docs/investigations/2026-11-24-db-size-and-transcript-retention-plan.md#not-done-noted) · [DataRetentionService.cs:386–403](../../server/Application/Services/DataRetentionService.cs#L386-L403) · [Prompt-span cache](../session-runtime-invariants.md#prompt-span-cache-card-1969-s2)
4. Ordinary vacuum gives reusable relation space after snapshots release dead tuples. Budget zero filesystem recovery from it. `VACUUM (TRUNCATE FALSE)` avoids the short exclusive lock for tail truncation. A separate operator-approved window is necessary for `VACUUM FULL` with its exclusive lock or for a full-table `pg_repack` with setup/swap locks and extension requirements. The earlier production census found `pg_repack` unavailable. Budget a rewrite of roughly one current relation for FULL or twice the target for repack, plus WAL and growth. Measure any prospective savings on scratch with before/after relation sizes. [PostgreSQL 16 routine vacuuming](https://www.postgresql.org/docs/16/routine-vacuuming.html#VACUUM-FOR-SPACE-RECOVERY) · [VACUUM options](https://www.postgresql.org/docs/16/sql-vacuum.html) · [pg_repack requirements](https://reorg.github.io/pg_repack/)
5. Roll back an in-flight batch as one transaction. For committed batches, restore the corresponding files or fresh dump in isolation, select the exact IDs in the deletion receipt, validate live parent sessions and conflicts on both ID and session/sequence, and reconcile those rows in an approved recovery window under the mutation/reseed contract. A whole-database replacement using the old snapshot would lose newer production writes.

### Not done, noted

To remove still-protected history, a separate design would need an archive-aware read path or verified rehydration with exact ID and session/sequence lookup, turn ordering, replay dedupe, cache reseed and explicit unavailable-archive handling across the readers above.

## Scratch cleanup

After checking task ownership labels, removed the exact container `antiphon-transcript-bd6e2b45` and volume `antiphon-transcript-bd6e2b45-pgdata` under context `antiphon-server2`. Fresh lists confirmed both were absent. Removed the exact transfer, gzip, manifest and probe files staged at `/tmp/antiphon-transcript-bd6e2b45` and confirmed that directory was absent at 19:20:20 UTC. The ignored local evidence directory retains the manifest and aggregate receipts.
