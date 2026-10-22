# CARD-1417 cutover script follow-ups

Closed list for the Code stage. CARD-1417 carries the four follow-ups from the CARD-1407
Final Review of `scripts/cutover-postgres-server2.ps1` (clean at 27a07737, verified source
161a7b6f). Master-only. No Unit lane. No production database, no real AppHost, no real
secret, no real maintenance marker: every case drives a PATH fake and a loopback listener.

## Items

- A-1. `Test-ExactNamedSwitch` scans every argv token, values included, and accepts a `/`
  prefix, so `-Execute -Confirm -HoldReason /ConfirmWindow` opens the window. Only `-` tokens
  count, and a token that is the value of the previous value-taking parameter is skipped.
- A-2. `keep-six.ps1 -ClearHold` failing at window exit is discarded with `| Out-Null` and no
  RECOVERY line. A non-zero clear prints a RECOVERY line that names the agent and the command.
- A-3. `-EnterIfAbsent` exit 2 (marker pre-existed) and any other non-zero exit (marker may
  have been written by this run) share one message that never names the marker. They are
  told apart and the refusal names `logs/apphost.down-on-purpose`. The script still clears
  nothing it did not record as entered.
- A-4. The harness bound 127.0.0.1:5432 with ReuseAddress and pointed `-Repository` at the
  real checkout. The script takes `-TailnetPort` (default 5432) and the harness drives an
  ephemeral port and a temporary repository for every window run.

## Verification design

- V-1. `-Execute -Confirm -HoldReason /ConfirmWindow` and `-Execute -Confirm -HoldR /ConfirmWindow`
  exit 2 with `REFUSED: -Execute requires -ConfirmWindow.`, never reach the snapshot check,
  and call no child. `-Execute -HoldReason:/ConfirmWindow -ConfirmWindow` still opens the
  window (it reaches the snapshot refusal). W-27.
- V-2. With StopOnly completed and `keep-six.ps1 -ClearHold` exiting 1, the window exit prints
  `RECOVERY: keep-six -ClearHold exited 1.` naming the hold agent and the clear command; the
  original refusal and exit 3 stand; the old-string restart and the maintenance clear still
  run. The StopOnly-failed path prints the same line. W-28.
- V-3. Enter exit 1 after the fake wrote the marker: exit 3, the refusal names
  `logs/apphost.down-on-purpose` as written by this run and left in place, `-Clear` is not
  called, the marker remains, StopOnly is not reached. Enter exit 2 with the marker written
  by the fake: exit 3, the refusal says the marker already existed and this run did not
  enter it, `-Clear` is not called, the marker remains. W-29.
- V-4. A window run reports `PHASE 0 tailnet <port> accepted a desktop connect.` with the
  ephemeral port the harness opened, never 5432; the `-Repository` the script hands to
  `recover-repository-children.ps1` is a temporary directory, not this checkout, and it is
  gone after the run; a closed `-TailnetPort` refuses with the port named. W-30.
- V-5. W-1 through W-26 stay green: the exact `-ConfirmWindow` token still opens the window,
  the connection secret stays off argv and stdout, and the foreign-marker cases still refuse
  without clearing.

No R-n and no PC-n are named. Mutation has nothing to discharge here.

Operator-only, not performed by tests: a real `-ConfirmWindow` against production, a real
keep-six POST, a real maintenance enter, and any Windows Scheduled Task change.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | n/a | cutover-followups | `pwsh -NoProfile -File scripts/test-cutover-postgres-server2.ps1` | V-1, V-2, V-3, V-4, V-5 | exit 0 | n/a | 4 |
| CP-2 | all | n/a | maintenance-exclusive | `pwsh -NoProfile -File scripts/test-set-apphost-maintenance.ps1` | exit 2 on a present marker (V-3 dependency) | exit 0 | n/a | 1 |

### Cost

Ordinary floor is 5 minutes.
