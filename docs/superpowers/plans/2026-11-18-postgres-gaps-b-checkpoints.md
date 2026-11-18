# Postgres move to server2, part B: cutover-script blocking gaps

Script-only repair of `scripts/cutover-postgres-server2.ps1` for the blocking gaps G-1 and G-6 of the move runbook (`docs/investigations/2026-11-18-postgres-move-runbook.md`, commit 2e8907ee0 on `feat/card-task-3b15e001`), plus the rollback step from the backup retarget plan review (`docs/superpowers/plans/2026-11-18-postgres-backup-retarget-plan.md` on `feat/card-task-a1368228`, step W-5). Stacked on the CARD-1979/1980/1982 tip a95ebc443. Part A (`feat/card-task-801355c0`) owns the `ANTIPHON_SKIP_DEV_POSTGRES=1` opt-out in `dev-aspire.ps1` and `scripts/autostart-apphost.ps1`; this part only sets and clears it.

No production cutover, no desktop run, no ssh, and no named Docker volume. The new witnesses use the existing PATH sh fake of `scripts/test-cutover-postgres-server2.ps1`. The version rule stays: same Postgres 16.14 Debian pgdg13 as the desktop; the upgrade is CARD-1988.

## Behaviour

- G-1. The phase 4 restart runs with `ANTIPHON_SKIP_DEV_POSTGRES=1` in the process environment that `restart-apphost.ps1` inherits. Every other restart (StopOnly, the old-string restart, the rollback) clears it, so an opt-out inherited from the operator never keeps the desktop Postgres down when it is needed. After the phase 5 mechanical checks, the window reads `antiphon-postgres`. Anything but `exited`, or a failed read, ends the run with exit 2 and the manual command `docker compose -f "<repo>/docker-compose.dev.yml" stop postgres`. AppHost already runs on server2, so that failure is not a reason for phase 6, and the RECOVERY line says so. The success path prints one OPERATOR line: set the variable in the user environment so later restarts and the logon auto-start keep the desktop Postgres down.
- G-6. Gate 6 records which side failed to read. An unreadable server2 side (table list, identifier or fingerprint read) rolls back with `-AcceptDataLossRollback`, after two WARNING lines that name the server2 writes left behind. Without the switch it refuses and names the switch. An unreadable desktop side refuses with or without the switch. A readable server2 side with extra rows still needs the switch (W-13). A fingerprint statement timeout still refuses (CARD-1980), with or without the switch.
- W-5. Once the secret is removed, every rollback writes `desktop` (ASCII, no BOM, no newline) into the snapshot-target file, also after a passed server2 backup proof. A rollback refused at gate 6 changes nothing. A failed write never blocks the rollback restart; the run then exits 2 with the manual write. The path is one constant, `$script:CutoverSnapshotTargetDefaultPath` = `C:\Users\lndco\antiphon-snapshot-runner\snapshot-target`, and `-SnapshotTargetFile` for tests. The name is **not final**: the plan says `snapshot-target`, and part A's wrapper reads `SNAPSHOT-TARGET` beside its runner copy, which is the same file on NTFS. A relative path (the Windows default on Linux) is never written.

## Verification design

- V-1. W-31. Successful window with a fake restart that starts the desktop Postgres again unless the opt-out is 1. The `-ExpectedServerSha` restart sees `skip=1`, StopOnly sees it unset, a `docker inspect antiphon-postgres` follows the restart, and the run prints `PHASE 5 desktop antiphon-postgres is exited after the restart.` and the OPERATOR line. Red at a95ebc443 (`restart-env=sha skip=unset`, `no-inspect-after-restart`).
- V-2. W-32. A restart that starts it anyway (`running`), or an inspect that fails after the restart, exits 2 with the refusal and the manual stop command, after `PHASE 5 mechanical checks passed.`, and never prints the success line. Red at a95ebc443 (exit 0, `printed-success`).
- V-3. W-33. A rollback with `ANTIPHON_SKIP_DEV_POSTGRES=1` inherited proceeds, its restart sees the variable unset, and the run says it cleared it. Red at a95ebc443 (`restart-env=sha skip=1`).
- V-4. W-34. Server2 read fails (ssh exit 255) with `-AcceptDataLossRollback`: the rollback proceeds (secret removed, restart, maintenance cleared) with both WARNING lines. Red at a95ebc443 (exit 3).
- V-5. W-35. Server2 read fails without the switch: refused, names the switch, no dotnet, no restart, no maintenance. Red at a95ebc443 (old message).
- V-6. W-36. Guard, green before and after: a desktop read failure, alone or with server2 down, refuses even with the switch.
- V-7. W-37. Snapshot target starts as `server2`. After an equal rollback, an accepted extra-row rollback, a server2-down rollback and a rollback whose restart exits 5, it reads `desktop`. A refused rollback leaves `server2`. Red at 9f909ff25 (parameter `SnapshotTargetFile` not found).
- V-8. W-38. The snapshot-target directory is missing: the rollback still removes the secret and restarts, then exits 2 with `PHASE 6 failed: AppHost is back on the desktop volume, but the snapshot target was not reset.` and the manual write. Red at 9f909ff25.
- R-1. W-1 through W-30 of `scripts/test-cutover-postgres-server2.ps1` stay green. Every rollback case now passes its own temporary `-SnapshotTargetFile`.
- R-2. V-1 through V-13 of `scripts/test-cutover-postgres-verify.ps1` (parent plans `2026-11-17-card-1974-1975-checkpoints.md` and `2026-11-18-card-1979-1980-1982-checkpoints.md`) stay green. They run the real `Test-CutoverNewHasExtraRows` against throwaway tmpfs `postgres:16` containers; the probe text is unchanged.

Owed off-row: a Windows PowerShell 5.1 parse of the cutover script on the desktop, after the live run ends (nothing runs on the desktop during this task).

No PC-n is named. Mutation execution is waived (operator 2026-11-12) and is not discharged. A real cutover window is operator-only and is not performed here.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | n/a | cutover-window | `pwsh -NoProfile -File scripts/test-cutover-postgres-server2.ps1` | V-1, V-2, V-3, V-4, V-5, V-6, V-7, V-8, R-1 | 38 passed, 0 failed, exit 0 | n/a | 15 |
| CP-2 | all | n/a | cutover-verify | `pwsh -NoProfile -File scripts/test-cutover-postgres-verify.ps1` | R-2 | WITNESS passed=13 failed=0 | n/a | 8 |

### Cost

Ordinary floor is 23 minutes. CP-1 carries the first-row budget of 15 minutes; the script itself runs about 5 minutes. CP-2 is about 6 minutes.
