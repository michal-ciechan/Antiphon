# CARD-0747: cancel of the stale Pending queue rows on orchestrator session b2f1a869

Date: 2026-11-12, 14:52Z to 15:02Z. Task a006c4cc. Operator authority 2026-11-12: "Do both and send debug
agent" (settle the old land/task receipts) and "Can we stop the reposting?". Server `GET /api/version` =
`56e96eabf698e9c1fcd462836ad32ed1fb259e83` (rc/2026-11-18).

Follows [2026-11-12-card-0747-sweep-dry-run.md](https://github.com/michal-ciechan/Antiphon/blob/feat/card-task-eeb60643/docs/investigations/2026-11-12-card-0747-sweep-dry-run.md)
(task eeb60643, commit 89fef3ece), whose option 1 this is.

## Outcome

849 stale `Pending` rows on session `b2f1a869-9bc5-4974-b8ae-53686448fd20` are now `Canceled`. Every
DELETE returned 200. No row was deleted, and no row created at or after the cutoff changed. No land note
changed. The session has typed nothing since 2026-11-12 03:49:48Z, before and after the cancel.

| Measure | Before (14:49Z) | After (15:03Z) |
|---|---:|---:|
| Pending rows on the session | 1,939 | 1,093 |
| Pending rows older than the cutoff | 855 | 6 (the open-note rows, kept) |
| Canceled rows on the session | 8,201 | 9,050 |
| Sent rows on the session (last `SentAt`) | 621 (03:49:48Z) | 621 (03:49:48Z) |
| Total rows on the session (none deleted) | 10,761 | 10,764 |
| Open land notes, fleet (`AwaitingReceipt`) | 621 | 621, identical ids and fields |
| Aged open notes (≤ cutoff) | 17 | 17 (2026-09-26 03:07Z .. 11-03 09:12Z), unchanged |

The arithmetic is 1,939 − 849 + 3 = 1,093. Three new Pending rows arrived during the run, at 14:55:15Z,
15:00:07Z and 15:01:17Z (sequences 10762-10764). They were not touched.
`GET /api/sessions/b2f1a869-.../messages` after the run lists 1,093 rows, all `Pending`. They are still withheld
with `Claude dialog observation unavailable: adopted session`. The oldest is the kept 2026-10-25 05:13Z row.

## Selection (read-only SELECT, `begin read only` with `default_transaction_read_only = on`)

The selection takes rows on session `b2f1a869` that meet all of these conditions:

- `Status = Pending (0)`.
- `CreatedAt < 2026-11-05T14:40:38Z`, the dry-run cutoff.
- The row does not link to an open land note. No note in `Queued`, `RetryPending`, `AwaitingReceipt` or
  `DestinationUnavailable` has `Id = SourceLandNotificationId` or `QueueMessageId = row.Id`.
- The source task is terminal (`Succeeded 4`, `Failed 5` or `Canceled 6`), or the row is a System row with no
  source task.

That gives **849 rows**. The oldest was created 2026-10-26T02:18:07Z (seq 379) and the newest
2026-11-05T14:35:48Z.

| Kind (body header) | Rows | Source task status |
|---|---:|---|
| `[task … done]` (Delegation) | 716 | Succeeded 716 |
| `[task … blocked]` (Delegation) | 50 | Succeeded 16, Failed 3, Canceled 31 |
| `[task … failed]` (Delegation) | 19 | Failed 19 |
| `SUPERSEDED — captured …` (Check) | 38 | Canceled 38 |
| System (resume and compaction notes, runner unavailable/recovered, keep-six) | 26 | no source task |
| **Total** | **849** | 1 row had been typed before (a Check row, attempts > 0). The rest were never typed. |

The exact id list (id, sequence, created, kind, task status, source task, attempts) is
[2026-11-12-card-0747-queue-row-cancel-ids.md](2026-11-12-card-0747-queue-row-cancel-ids.md). It was committed
and pushed as `019e7fb55de754f3052b2b40652556bae03b00cb` before the first DELETE.

**Kept: the 6 rows linked to an open land note.** Two are TaskCompletion rows (`a16768f9`, `195b52d6`, created
2026-10-25 and typed 3 times). The other four are ProviderInputWait rows (`87e87f5f`, `e1495b00`,
`6f6decc9`, `5e8a5f65`).

**Caller note:** 10 of the canceled rows belong to tasks that still have an open ProviderInputWait note. Those
are 8 `SUPERSEDED` checks and 2 `[task … failed]` notes for cd3c92ab and b02344a7. The notes themselves are not
linked to those 10 rows. Each note's own queue row is one of the 6 kept rows, so the notes are untouched and stay
open.

## Method

1. Took a baseline snapshot before any DELETE. It holds every queue row on the session (id, status, seq,
   created, sent, canceled, attempts) and every open land note fleet-wide (id, state, kind, queue row,
   confirmed).
2. Ran a first batch of 5 (seq 379, 381, … then `96135dd2`) through
   `DELETE /api/sessions/b2f1a869-.../messages/{id}`, with no token. All returned 200. The re-snapshot showed
   the 5 rows still present as `Status = Canceled` with `CanceledAt` set. No other row and no land note had
   changed.
3. Ran the remaining 844 in two halves of 422. The script made sequential calls with at least 100 ms between
   starts, so the rate was ≤ 10/s. The observed rate was about 1.4/s because each response returns the full
   queue DTO, about 3.4 MB at the start. The script would stop on the first non-2xx. A re-snapshot between the
   halves showed only the expected changes.
4. Took a final snapshot and diffed it against the baseline. Changed rows were exactly the 849 targets, all
   `Canceled`, with `CanceledAt` from 14:52:33Z to 15:02:41Z. There were 0 unexpected changes, 0 deleted rows,
   0 changed rows created at or after the cutoff, and 0 changed or added open land notes.

Response codes: 849 × 200. There were no 404s and no other codes.

The `GET .../messages` route lists only Pending rows. "Still present as Canceled" was therefore verified with
the read-only SELECT, not with the GET.

## Evidence (gitignored, task worktree)

`C:\Antiphon\worktrees\card-task-a006c4cc\.antiphon\evidence\a006c4cc\` holds these files:

- `target.csv` is the selection.
- `all-ids.txt` and `batch1.txt` are the DELETE inputs.
- `cancel.log` has one line per DELETE: UTC time, id, HTTP code and latency.
- `before-snapshot.csv`, `after-batch1.csv`, `after-a.csv` and `after-final.csv` are the snapshots.
- `cancel.sh`, `verify.sql`, `cte.sql` and `diff.py` are the scripts.

## What this does not fix

This cancel only clears the current backlog. The code gap from the dry run remains. The CARD-0747 rule scans
`AgentTaskLandNotifications` only. Every adopted-session episode can therefore build a new backlog of
completion, check and System rows with no open land note, and replay it FIFO at about 1/min after the next
restart. Those 1,093 newer rows (2026-11-05 14:46Z onward) are still Pending and will type once the adoption
ends, unless the code fix lands or they age into a later cancel. The 11 `ReceiptPresent` TaskCompletion notes
also stay open, on the receipt-confirmation follow-up.

--- next stage ---
next: done
handoff: none
artifact: docs/investigations/2026-11-12-card-0747-queue-row-cancel.md
