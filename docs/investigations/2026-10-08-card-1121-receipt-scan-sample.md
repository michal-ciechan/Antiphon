# CARD-1121: post-CARD-1073 receipt-scan sample (2026-10-08)

Debug task `33731d74`, read-only. This doc answers the measurement gate in
`docs/superpowers/plans/2026-10-08-card-1121-receipt-scan-watermark-plan.md`
(plan branch `feat/card-task-4c27afbe` at `6cb36181`). Only SELECTs were run
against the production desktop database, through `docker exec antiphon-postgres psql`
with `PGOPTIONS=-c default_transaction_read_only=on`. Nothing was reset, written,
stopped or restarted. No prompt text, note body or SQL parameter value was read or
stored here. Every census column is metadata, and the census compares bodies only
with `=` inside SQL.

## Provenance

| Item | Value |
|---|---|
| `/api/version` at window start and end | `b5e78700ae9a76430c13d75cc03399055dd82e59` (unchanged, includes CARD-1073 `2af727db`) |
| Database | `antiphon` on container `antiphon-postgres`, PostgreSQL 16.14 |
| `pg_stat_statements_info.stats_reset` | `2026-09-30 16:39:44.16274+00` at both snapshots; `dealloc` 0 at both |
| Snapshot 1 / 2 (DB `now()`) | `2026-10-08 12:07:23.339783+00` / `12:08:35.952974+00` (**72.613 s**) |
| Evictions / counter decreases | 0 / 0 (all snapshot-1 query IDs present in snapshot 2) |

### Current projected receipt query IDs

I found these by their normalized SQL, as the plan specifies. I did not reuse
the old CARD-1073 full-entity IDs. The only production caller that emits this
`Sequence, Text ... ORDER BY Sequence` projection is
`LandNoteReceipt.FirstReceiptAsync`, which runs from
`AgentTaskLandNotificationService.ReconcileAsync`. The other users of
`LandNoteReceipt.Prompts` are the dispatcher's released-seat check and
`ExpectationSnapshotReader`. They project `Text` only, so their statements are
different.

| Shape | queryid | Normalized SQL |
|---|---|---|
| two-kind | `5747959679161616480` | `SELECT t."Sequence", t."Text" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = $1 AND t."Text" IS NOT NULL AND t."Kind" IN ($3, $4) AND t."Sequence" > $2 ORDER BY t."Sequence"` |
| one-kind | `-4975657953750802637` | `... AND t."Kind" = $3 AND t."Sequence" > $2 ORDER BY t."Sequence"` (same projection) |

## Counter delta (72.613 s window)

| Shape | Calls | Rows | Rows/call | Exec ms | ms/call | Exec ms/min |
|---|---:|---:|---:|---:|---:|---:|
| two-kind | 108 | 162,648 | 1,506.0 | 8,718.0 | 80.7 | 7,204 |
| one-kind | 72 | 61,317 | 851.6 | 3,245.0 | 45.1 | 2,681 |
| **combined** | **180** | **223,965** | 1,244.3 | **11,963.0** | 66.5 | **9,885** |

- Normalized comparison: `11,963 * 68 / 72.613 / 1000` = **11.2 s / 68 s**. The
  2026-10-06 old full-entity figure was 10.3 s / 68 s.
- Call counts and rows/call are effectively **identical to the CARD-1073 baseline**,
  which was 72 one-kind calls at 852 rows/call and 108 two-kind calls at 1,506
  rows/call. The CARD-1073 projection did not reduce the number of scans or the
  rows returned. Execution time per call did not fall: 3,245 ms and 8,718 ms now,
  against 3,034 ms and 7,601 ms before.
- Execution across all statements in the window was 16,484 ms. The two receipt
  SELECTs made up **72.6 %** of that time.
- Since the 09-30 reset, cumulative totals are 256,035 calls and 14,995 s of
  execution (10,838 s two-kind plus 4,157 s one-kind).
- Database container CPU from `docker stats --no-stream`, as instantaneous
  single-sample values: 26.5 % at start (12:07:22Z), 42.9 % mid-window (12:07:58Z)
  and 6.0 % at end (12:08:36Z). These are host CPU, reported separately from the
  database execution time above.

## Cohort census (metadata only)

The census covered every note in a state the hosted service still pages: not
`Confirmed`, `NotRequired` or `LegacyUnverified`. I ran it at window start and
end. The set of notes and every column were **identical** at both ends; only the
order of null-sorted rows differed. No note was confirmed or changed state during
the window. The last confirmation of any note was at 12:05:38Z, before the window
opened. Candidate counts use the scanner's own key: the note's `ParentSessionId`,
the accepted kinds for the note, and `Sequence > LastDeliveryBaselineSequence`.

| Group | Notes |
|---|---:|
| Open, unlinked (`DestinationUnavailable`, gated by NextAttemptAt, never scanned) | 85 |
| Open, linked, attempted, with a floor (eligible to reach the scan) | 44 |
| ... that actually reach the receipt SELECT each pass | **20** (12 two-kind and 8 one-kind calls per pass) |
| ... that return before the SELECT (profiled TaskCompletion generation or rendering exits, and one two-kind) | 24 |
| Notes in the scanned cohort that have **ever** matched | **0** (by definition: none are Confirmed) |

### Passes and rows

- There were **9 reconciliation passes in 72.6 s**, about one every 8.1 s. That is
  the hosted service's 5 s wait plus pass time.
- Two-kind rows per pass from the census are **18,072** (13 nonlegacy Aged and
  Outcome notes). Nine passes give 162,648, which matches the counter delta exactly.
- One-kind rows per pass from the census are **6,813**. That is 4 legacy Outcome
  notes (4,282 rows), 3 DispatchBase notes (2,530 + 1) and 1 profiled TaskCompletion
  note with 0 candidates. Nine passes give 61,317, which also matches exactly.
- **Eligible prompts rescanned per pass: 24,885 candidate prompt rows across 20
  notes.** All of them miss, and the same rows are re-read on every pass.

### Shape of the never-matching cohort

| Shape | Notes | Candidate rows/pass | Notes |
|---|---:|---:|---|
| two-kind, nonlegacy Aged(1)/Outcome(3), AwaitingReceipt, queue Sent, body equal, no spill, not profiled | 13 | 18,072 | **all 13 have queue destination ≠ note ParentSessionId**; the scan reads the parent, never the session the row was delivered to |
| one-kind legacy Outcome, AwaitingReceipt, Sent, body equal | 4 | 4,282 | one has DeliveryVerdict null |
| one-kind DispatchBase, AwaitingReceipt/Canceled | 3 | 2,531 | one is Canceled with 1 candidate |
| one-kind TaskCompletion, profiled | 1 reached the SELECT | 0 | excluded by the plan whitelist |

The parent sessions whose transcripts are scanned are `Stopped` or `Failed`. Their
`MAX(Sequence)` did not change during the window; the largest is 302,650 at both
ends. The repeated scans are therefore of frozen transcripts. These notes date
from 2026-09-01 to 2026-09-30.

## Gate verdict

- **The repetition is real and it is 100 % repeated misses.** All 180 receipt
  SELECTs in the window were the same 20 notes re-reading unchanged transcripts.
  The cost is about 11.0–12.0 s of database execution per 72.6 s, about 165 ms/s,
  and about 3,080 Text rows/s returned to the server.
- **Whitelist coverage.** Under the plan's exclusions, 19 of the 20 scanned notes
  look eligible, covering 171 of 180 calls and **100 % of returned rows**. The one
  excluded note is a profiled TaskCompletion with 0 rows. Every one is non-profiled
  with equal bodies, no spill and no pointer. Their destination state is quiet, so
  the committed revision should be stable and the cache would hit after one first
  scan per note per process. TestDesign must still confirm two things: the legacy
  Outcome notes with a null verdict, and the plan's oversized-text and
  pointer-headline exclusions.
- **Absolute savings opportunity.** Up to about 11.9 s per 72.6 s of database
  execution and about 224k Text rows per 72.6 s. Only the first scan of each note
  after a start or revision change remains.
- **Separate finding, not in this card's scope.** All 13 two-kind notes are
  scanned against `note.ParentSessionId`, while their queue rows were delivered to
  a different session. A receipt can therefore never appear in the scanned
  transcript. The watermark would make this cheap, but the notes would stay open
  forever. The orchestrator should decide whether that is a separate defect card
  (destination re-homing or terminal classification).
