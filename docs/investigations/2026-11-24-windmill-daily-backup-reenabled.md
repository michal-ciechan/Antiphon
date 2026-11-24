# Windmill daily Postgres backup re-enable blocked (2026-11-24)

Task 28dc44fe refreshed the desktop Windmill runner and selected the production Postgres on server2. Both schedules are disabled. The manual Windmill run is pending an operator decision about retention.

## Runner and source receipts

At 18:51 UTC the four files in `C:\Users\lndco\antiphon-snapshot-runner` were refreshed byte-for-byte from master commit `3e0cbab19324fb3ca2f50df1b74cb8b957fef06a`.

| Runner file | SHA-256 |
| --- | --- |
| `postgres-snapshot.ps1` | `43c7967a8dd80847ff8ceb6156a2951aca360633677b75f5f1cebb184eb53d80` |
| `invoke-postgres-snapshot.ps1` | `7c49b33d3afd86b4a24c88faf0bec6deac841f87c7e325e4a380393b120a9986` |
| `invoke-postgres-snapshot-stale.ps1` | `2577122f9b068819ab6687286e5dd25e4746b9f800135f0e93f33a15ebf0e036` |
| `job-status.ps1` | `728a9b72a96fdd22ee2cec372e6d9d3679fbc9c6b5fb80d264037e7ad092d8e1` |

`SOURCE-SHA` contains the 64-byte ASCII SHA-256 of `postgres-snapshot.ps1`. The obsolete `SOURCE-SHA.txt` was removed. `Set-SnapshotTarget server2` printed `SNAPSHOT TARGET server2`. The resolver selected `C:\src\Antiphon\scripts\postgres-snapshot.ps1`, whose bytes match the pin. The desktop Windmill worker read back the same target and pin through its Windows SSH bridge.

The SSH-backed Docker context `antiphon-server2` reached daemon 24.0.2. Container `antiphon-server2-postgres-postgres-1` was running with compose project `antiphon-server2-postgres` and volume `antiphon-server2-postgres_pgdata` mounted at `/var/lib/postgresql/data`. The desktop daemon returned no container named `antiphon-postgres`.

## Retention decision

The Windmill wrapper passes `-OneDriveCopy` and uses the default retention of seven OneDrive dumps and fourteen local daily dumps. Before the run, OneDrive already held seven matching files and the local folder held twelve. A successful Windmill run would remove `C:\Users\lndco\OneDrive\Backups\antiphon-postgres\antiphon-20261113T075003Z.dump` (638,663,717 bytes) after verifying the new copy. This task's authority requires preserving all existing backup files. The operator needs to authorize the documented pruning for the manual and scheduled runs, or commission a runner change that preserves the existing files.

The Telegram sender calls `dotnet run` for `tg-send.cs`, which can compile on the desktop. If the qualification's desktop build restriction is still in effect, the sender invocation needs an explicit exception or an approved prebuilt sender before the manual run.

## Windmill state and remaining verification

Read-only Windmill database queries confirmed zero queued or running jobs for the two paths. The rows below have no scheduled next run while disabled. The times in the last column are the next configured times if enabled before those occurrences.

| Schedule (workspace `mc`) | Cron | Timezone | Enabled | Next configured time |
| --- | --- | --- | --- | --- |
| `u/lndcobra/antiphon_postgres_snapshot` | `0 50 7 * * *` | Europe/London | false | 2026-11-25 07:50 UTC |
| `u/lndcobra/antiphon_postgres_snapshot_stale` | `0 0 8 * * *` | Europe/London | false | 2026-11-25 08:00 UTC |

No Windmill API token was issued. The manual job still needs to prove a new server2 dump, successful `pg_restore --list`, matching SHA-256 on the OneDrive copy, and a delivered Telegram status line naming server2. Schedule enablement and readback follow that proof.

The newest dump observed in both folders was the earlier `antiphon-20261124T090703Z.dump`, at 905,456,635 bytes. The [earlier verification](2026-11-24-server2-db-backup-verification.md) recorded matching SHA-256 `36ad51c138a100cf834d4663a3e86922f02520a8076508056381e3b55c41b89f`, OneDrive sync, and a successful scratch restore for that dump. The restore drill remains manual. Retention on the retargeted Windmill job, the scheduled off-host copy, and Telegram failure and stale alerts on the refreshed runner remain unverified.
