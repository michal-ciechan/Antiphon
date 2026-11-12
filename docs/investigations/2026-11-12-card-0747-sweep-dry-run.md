# CARD-0747: land-note expiry sweep dry run, and why it will not stop the orchestrator replay

Date: 2026-11-12, ~14:40Z. Task eeb60643 (read-only). Server `GET /api/version` =
`56e96eabf698e9c1fcd462836ad32ed1fb259e83` (rc/2026-11-18), capabilities `land-v2`, `operator-shutdown-v1`.
Evidence comes from one dry-run POST, two read-only GETs on session `b2f1a869-9bc5-4974-b8ae-53686448fd20`, its
transcript, and `BEGIN READ ONLY` SELECTs (with `default_transaction_read_only = on`) against the desktop
`antiphon` database in `antiphon-postgres`. There was no apply, no queue cancel, no SQL write and no restart. No
token value was read or printed.

Follows [2026-11-11-card-0747-receipt-replay.md](2026-11-11-card-0747-receipt-replay.md).

## Verdict

1. **The sweep has nothing to settle.** The dry run (`olderThanDays: 7`) scanned 17 aged open notes and found
   **0 eligible**. The hosted reconcile (`LandNoteReceiptExpiryDays` default 7) has already settled the aged
   land notes. An apply now would write nothing.
2. **The replay is a queue backlog, not open notes.** Session `b2f1a869` holds **1,937 `Pending` queue rows**.
   **855** are older than 7 days. Only **6** of the 855 link to an open land note. The other **849** have no
   open note: 767 have no land note at all, and 82 belong to tasks whose notes are already
   `LegacyUnverified` or are provider waits. The CARD-0747 sweep scans
   `AgentTaskLandNotifications` only, so it cannot cancel those rows.
3. **The replay is paused right now, not stopped.** Old notes were typed from 2026-11-11 18:58Z to
   2026-11-12 03:49Z at about one per minute, oldest first. The session restarted at **2026-11-12T03:49:05Z**
   and became an adopted session again. Every row is now withheld with `Claude dialog observation
   unavailable: adopted session`, and nothing has been typed since 03:49:48Z. The next restart under the server
   ends the adoption, and the queue will resume typing the 855 aged rows FIFO before any newer ones. At about
   1/min, that is roughly 14 hours of 2026-10-25..11-05 notes.

## Endpoint contract (as built)

`POST /api/agent-tasks/land-notifications/expire`, body `{ "apply"?: bool, "olderThanDays"?: int }`
(`server/Api/Endpoints/AgentTaskEndpoints.cs:147`, `server/Application/Services/LandNoteExpirySweepService.cs`).

- `apply` defaults to false, and `olderThanDays` defaults to 7, which is also the minimum (below 7 returns 422).
- The dry run needs no token. `apply: true` needs header `X-Antiphon-Operator-Token`, else 403
  `operator_token_required`. If `Supervision:DeliveryVerification:LandNoteReceiptExpiryDays <= 0` it returns
  409 `land_note_expiry_disabled`.
- It scans notes in `AwaitingReceipt`/`DestinationUnavailable` with `CreatedAt <= now - olderThanDays`.
  Notes newer than the cutoff are not scanned at all.
- Response: `{ apply, olderThanDays, cutoff, scanned, outcomes{NotAged|NotOpen|NonTerminal|TypedPending|
  ReceiptPresent|Changed|WouldSettle|Settled: n}, eligibleByKind{<LandNotificationKind>: n},
  eligibleByState{<state>: n}, failures }`.
- The response has no timestamps, no queue-cancel count and no newer-note count. The figures below come from SQL.

## Dry-run result (2026-11-12T14:40:38Z)

```json
{"apply":false,"olderThanDays":7,"cutoff":"2026-11-05T14:40:38.9241454Z","scanned":17,
 "outcomes":{"ReceiptPresent":11,"TypedPending":2,"NonTerminal":4},
 "eligibleByKind":{},"eligibleByState":{},"failures":0}
```

| Question | Answer |
|---|---|
| Would settle, per kind (land / task / check) | land 0 / task (TaskCompletion) 0 / check (LegacyCheckNote) 0 |
| Would settle, per state | AwaitingReceipt 0 / DestinationUnavailable 0 |
| Queue rows that would be canceled | 0 |
| Aged open notes scanned | 17 = 13 TaskCompletion (`AwaitingReceipt`, created 2026-09-26 03:07Z .. 2026-10-25 05:15Z) + 4 ProviderInputWait (`AwaitingReceipt`, 2026-10-27 19:52Z .. 2026-11-03 09:12Z) |
| Why each was left | 11 `ReceiptPresent`: the row was typed and Sent, and its receipt is stored, so the ordinary reconcile should confirm it but has not. That is a CARD-0747/CARD-1157 follow-up. 2 `TypedPending`: queue sequences 217/218 (2026-10-25), typed 2-3 times, parked. 4 `NonTerminal`: provider waits are never terminal by rule. |
| Oldest / newest scanned note | 2026-09-26T03:07:54Z / 2026-11-03T09:12:03Z |
| Open notes newer than 7 days (left alone) | **603** fleet-wide, all `AwaitingReceipt`: 519 TaskCompletion (2026-11-05 17:38Z .. 11-12 14:14Z) + 84 DispatchBase (2026-11-06 06:00Z .. 11-12 14:39Z) |
| Open land kinds (Held/Aged/Conflict/Outcome) at any age | 0. All are Confirmed, LegacyUnverified or Canceled. |

Fleet census of all notes by state, for context: Confirmed 7,574, LegacyUnverified 1,286, Canceled 13,
NotRequired 1, open (AwaitingReceipt) 620.

## Orchestrator queue (session b2f1a869)

`GET /api/sessions/b2f1a869-.../messages` returned 1,937 rows, all `Pending`, all `modalBlocked` with
`withholdReason = "Claude dialog observation unavailable: adopted session"`.
`GET .../pending-notes` reports the same withhold.
Lifetime row status for the session: Pending 1,937, Sent 621 (last 2026-11-12 03:49:31Z), Canceled 8,201.

| Bucket | Rows | Typed (attempts > 0) | Created |
|---|---:|---:|---|
| Aged (≥ 7 d), linked to an open TaskCompletion note | 2 | 2 | 2026-10-25 05:13Z .. 05:15Z |
| Aged, linked to an open ProviderInputWait note | 4 | 0 | 2026-10-27 .. 11-03 |
| Aged, no open note: Delegation `[task # done/blocked/failed]` | 785 | 0 | 2026-10-27 19:48Z .. 11-05 13:43Z |
| Aged, no open note: Check `SUPERSEDED — captured …` | 38 | 1 | 2026-10-27 .. 11-05 |
| Aged, no open note: System (system notes, runner unavailable/recovered, keep-six) | 26 | 0 | 2026-10-26 .. 11-05 |
| **Aged total** | **855** | 3 | 2026-10-25 05:13Z .. 2026-11-05 14:35Z |
| Newer than 7 d (linked TaskCompletion 519, DispatchBase 84, unlinked 479) | 1,082 | 0 | 2026-11-05 14:46Z .. 2026-11-12 14:39Z |

All source tasks of the aged Delegation/Check rows are terminal: Succeeded 732, Failed 22, Canceled 69.
Of the 849 aged rows without an open note, 767 have no land note for their task at all. These are plain
completion, block and failure notices and supervision notes, so they are outside the sweep by design. The
other 82 belong to tasks whose notes are already `LegacyUnverified`, or are provider waits.

### Replay timeline (transcript)

Typed Antiphon notes (`[land …]` / `[task …]`, bracketed paste) per hour, with how many carried pre-11-05 dates:

| Hour (Z) | Typed | land / task | dated before 11-05 |
|---|---:|---|---:|
| 11-11 18 | 2 | 2 / 0 | 2 |
| 11-11 19 | 49 | 38 / 11 | 45 |
| 11-11 20 | 46 | 29 / 17 | 42 |
| 11-11 21 | 52 | 38 / 14 | 14 |
| 11-11 22 | 35 | 20 / 15 | 17 |
| 11-11 23 | 23 | 7 / 16 | 15 |
| 11-12 00 | 30 | 24 / 6 | 6 |
| 11-12 01 | 40 | 30 / 10 | 15 |
| 11-12 02 | 38 | 1 / 37 | 32 |
| 11-12 03 | 33 | 4 / 29 | 31 |
| since 03:46Z | 0 | | |

Sent rows since 2026-11-11 by linked note: Held 99, Aged 73, Conflict 4, Outcome 17, TaskCompletion 40, and
147 unlinked. Their notes were created between 2026-10-25 and 2026-10-27, which matches the "replayed
10-25/26/27" report.

## What would actually stop it (decisions for the operator/caller)

The sweep apply is a no-op today and does not need to be run. The backlog lives in the queue. The options are:

1. **Cancel the 855 aged Pending rows on `b2f1a869` before the session's next server restart.** Each row
   goes through the existing `DELETE /api/sessions/{id}/messages/{messageId}`. That route takes no token,
   cancels only `Pending` rows under the queue lock, writes `Canceled` and deletes nothing. The selection is
   `CreatedAt <= 2026-11-05T14:40Z`, from the `GET .../messages` list. This is a mutation and needs operator scope
   confirmation. The 6 linked rows should be left out, or canceled knowing that their notes stay open
   until the hosted rule handles them.
2. **Code fix (Debug/Code card).** Extend the CARD-0747 rule to `SessionQueuedMessages` that have no open
   land note. Cancel a never-typed `Pending` Delegation/Check/System row older than the expiry window when its
   source task is terminal, or for System rows by age. Do this in the hosted reconcile and in the operator
   sweep, and add `queueRowsCanceled` and oldest/newest to the response. Without this, every adopted-session
   episode builds another backlog that replays after the next restart.
3. Separately, the 11 `ReceiptPresent` TaskCompletion notes (2026-09-26 .. 10-25) have a stored receipt but are
   never confirmed. That is the carried-row / generation gap described in the 2026-11-11 note, and it should be
   tracked on that card.

## Commands (operator; token from file, never inline)

Dry run (no token):

```powershell
Invoke-RestMethod -Method POST http://localhost:17202/api/agent-tasks/land-notifications/expire `
  -ContentType application/json -Body '{"apply":false,"olderThanDays":7}'
```

Apply (operator token from `%LOCALAPPDATA%\Antiphon\operator-token`, or the path in
`ANTIPHON_OPERATOR_TOKEN_FILE`, read by the existing helper and never printed). Today it is expected to return
`scanned: 17, outcomes: {ReceiptPresent: 11, TypedPending: 2, NonTerminal: 4}` with empty `eligibleBy*`,
so it settles nothing:

```powershell
. .\scripts\lib\runner-operator-token.ps1
$h = @{ 'X-Antiphon-Operator-Token' = (Get-RunnerOperatorToken) }
Invoke-RestMethod -Method POST http://localhost:17202/api/agent-tasks/land-notifications/expire `
  -Headers $h -ContentType application/json -Body '{"apply":true,"olderThanDays":7}'
```

The operator token file is present on this desktop at the default path. This task did not read it.
