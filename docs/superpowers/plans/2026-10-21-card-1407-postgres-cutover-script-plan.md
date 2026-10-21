# CARD-1407 cutover script checkpoints

Closed list for the Code stage. The design is CARD-1401 C2. This table is the one
row this round runs. Lane is unpinned. No production database and no AppHost restart.

## Verification design

- V-1. Default and `-WhatIf` exit 0, list phases 0 through 6 and each stop gate in order, and do not POST, call docker, or call `dotnet user-secrets set`.
- V-2. `-Execute` without `-ConfirmWindow` exits non-zero and does none of those calls.
- V-3. `-Execute -ConfirmWindow` without a snapshot reference, or without a passed drill record, refuses before any stop.
- V-4. The cutover script source has no `volume rm`, no `docker volume rm`, and no `-AllowWorktree`.
- V-5. `restart-apphost.ps1 -StopOnly -WhatIf` does not POST and does not reach the relaunch.
- V-6. `restart-apphost.ps1 -StopOnly` performs the graceful POST and exits before `dev-aspire.ps1`. `-WhatIf` alone refuses.
- V-7. A fixture `landPending` count other than zero refuses before StopOnly. A latency median above 20 ms, a drill that did not pass, and a drill over 600 seconds refuse before any external command.

No R-n and no PC-n are named for this slice. Mutation has nothing to discharge here.

Operator-only, not performed by tests: `-ConfirmWindow`, passwords, removing either data directory, tailnet-only confirmation, the keep-six hold, one real dispatch, one real land, the Hangfire dashboard, and the phase 6 choice when the new database has rows the desktop volume lacks.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | n/a | cutover-whatif | `pwsh -NoProfile -File scripts/test-cutover-postgres-server2.ps1` | V-1, V-2, V-3, V-4, V-5, V-6, V-7 | exit 0 | n/a | 3 |

### Cost

Ordinary floor is 3 minutes.
