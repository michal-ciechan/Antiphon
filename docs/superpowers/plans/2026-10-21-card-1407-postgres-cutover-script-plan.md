# CARD-1407 cutover script checkpoints

Closed list for the Code stage. The design is CARD-1401 C2. This table is the three
rows this round runs. Lane is unpinned. No production database and no AppHost restart.

## Verification design

- V-1. Default and `-WhatIf` exit 0, list phases 0 through 6 and each stop gate in order, and do not POST, call docker, or call `dotnet user-secrets set`.
- V-2. `-Execute` without `-ConfirmWindow` exits non-zero and does none of those calls.
- V-3. `-Execute -ConfirmWindow` without a snapshot reference, or without a passed drill record, refuses before any stop.
- V-4. The cutover script source has no `volume rm`, no `docker volume rm`, and no `-AllowWorktree`.
- V-5. `restart-apphost.ps1 -StopOnly -WhatIf` does not POST and does not reach the relaunch.
- V-6. `restart-apphost.ps1 -StopOnly` performs the graceful POST and exits before `dev-aspire.ps1`. `-WhatIf` alone refuses.
- V-7. A fixture `landPending` count other than zero refuses before StopOnly. A latency median above 20 ms, a drill that did not pass, and a drill over 600 seconds refuse before any external command.
- V-8. The connection secret reaches `dotnet user-secrets set` on stdin. A sentinel password is absent from every child argv, from every other child's stdin, and from stdout.
- V-9. Maintenance is entered before StopOnly and lifted after the verified restart. A failed maintenance enter refuses before StopOnly.
- V-10. Rollback compares row counts and refuses when the new database has more rows unless `-AcceptDataLossRollback` is passed.
- V-11. The server2 copy is the `.age` file. Decrypt is `age -d -i -` with the identity bytes on stdin. The identity path is absent from child argv and from stdout.
- V-12. `-Confirm` and `-Con` do not open the window. Only the exact `-ConfirmWindow` token does.
- V-13. A live landPending count other than zero refuses after the hold is recorded, and that hold is cleared. A fixture count of zero reaches StopOnly.

No R-n and no PC-n are named for this slice. Mutation has nothing to discharge here.

Operator-only, not performed by tests: a real `-ConfirmWindow` against production, passwords, removing either data directory, tailnet-only confirmation, a real keep-six POST, one real dispatch, one real land, the Hangfire dashboard, and the phase 6 choice on a real database.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | n/a | cutover-whatif | `pwsh -NoProfile -File scripts/test-cutover-postgres-server2.ps1` | V-1, V-2, V-3, V-4, V-5, V-6, V-7, V-8, V-9, V-10, V-11, V-12, V-13 | exit 0 | n/a | 3 |
| CP-2 | all | n/a | apphost-stop | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1` | V-5, V-6 | exit 0 | n/a | 3 |
| CP-3 | all | n/a | maintenance-exclusive | `pwsh -NoProfile -File scripts/test-set-apphost-maintenance.ps1` | exclusive maintenance enter | exit 0 | n/a | 1 |

### Cost

Ordinary floor is 7 minutes.
