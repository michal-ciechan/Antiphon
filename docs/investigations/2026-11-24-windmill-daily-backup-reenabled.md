# Windmill daily Postgres backup re-enabled (2026-11-24)

Both Windmill schedules are enabled. One manual run through the desktop worker verified a new backup of the production Postgres container on server2 before the schedules were enabled.

## Manual verification

| Receipt | Value |
| --- | --- |
| Windmill workspace and job | `mc`, `01a20f0d-6728-e2ff-0f15-c8155bc6337e`, desktop worker, success in 370.293 seconds |
| Source | SSH Docker context `antiphon-server2`, container `antiphon-server2-postgres-postgres-1`, PostgreSQL 16.14 |
| Source checks | Running container, compose project `antiphon-server2-postgres`, volume `antiphon-server2-postgres_pgdata` at `/var/lib/postgresql/data`. The desktop container `antiphon-postgres` was absent. A live process check observed `pg_dump` in the server2 production container. |
| Local dump | `C:\Users\lndco\antiphon-backups\antiphon-20261124T193429Z.dump` |
| OneDrive copy | `C:\Users\lndco\OneDrive\Backups\antiphon-postgres\antiphon-20261124T193429Z.dump` |
| Size | 916,334,606 bytes in each location |
| SHA-256 | `62741e4620b57ea997374ec6acf0952b8ab49b6dc653515683277d33ffe7e091` on both copies |
| Restore listing | Independent `pg_restore --list` exited 0 and returned 153 TABLE DATA entries using the local PostgreSQL 16 image with networking disabled and a read-only file mount |
| Cloud status | Windmill logged `oneDrive=ok`. Windows reported `System.StorageProviderState=2` (in sync). |
| Telegram | `DB backup: OK 0.9 GB +cloud src=server2`. The job returned `telegram=0`. The gateway recorded Telegram message ID `1891` at 19:40:36 UTC. |

The manual run removed one OneDrive dump, `antiphon-20261113T075003Z.dump` (638,663,717 bytes), under the operator-approved seven-dump policy. Before starting the job, the existing local copy at `C:\Users\lndco\antiphon-backups\antiphon-20261113T075003Z.dump` matched the OneDrive SHA-256 `4a0fbecfec9f992dd79cf437e42249af4e51bc0037d71cbbec5969dc48c6a05`. Afterward, the local daily count grew from 12 to 13 and OneDrive still held 7. The earlier verified `antiphon-20261124T090703Z.dump` was preserved in both locations. Its prior restore proof is documented separately. [Earlier verification](2026-11-24-server2-db-backup-verification.md)

## Schedules

Both schedule states were read back through the Windmill API, and the next occurrences below were confirmed in the Windmill queue. Each temporary API credential was removed and its absence verified.

| Schedule | Cron | Timezone | Next run |
| --- | --- | --- | --- |
| `u/lndcobra/antiphon_postgres_snapshot` | `0 50 7 * * *` | Europe/London | 2026-11-25 07:50 UTC |
| `u/lndcobra/antiphon_postgres_snapshot_stale` | `0 0 8 * * *` | Europe/London | 2026-11-25 08:00 UTC |

The first scheduled backup and the stale-alert path await live execution. OneDrive retention and sync were verified on the manual Windmill run. Local retention is configured for 14 daily dumps and did not need to prune. Full scratch restore drills remain manual. This run verified `pg_restore --list`. The failure and stale Telegram paths were not exercised.

## Deployed runner

The runner at `C:\Users\lndco\antiphon-snapshot-runner` was refreshed from master commit `3e0cbab19324fb3ca2f50df1b74cb8b957fef06a`. The 64-byte ASCII `SOURCE-SHA` pins its `postgres-snapshot.ps1` bytes, and `Set-SnapshotTarget server2` wrote the verified target. The runner resolver selected a matching copy at `C:\src\Antiphon\scripts\postgres-snapshot.ps1`. The desktop worker read back the pin and target over its SSH bridge. The obsolete `SOURCE-SHA.txt` was removed.

| Deployed file | SHA-256 |
| --- | --- |
| `postgres-snapshot.ps1` (also the value in `SOURCE-SHA`) | `43c7967a8dd80847ff8ceb6156a2951aca360633677b75f5f1cebb184eb53d80` |
| `invoke-postgres-snapshot.ps1` | `f3e436b8cbe2189424029b0c3ab5cd38ad5b4976b08225dd88d228a2eedaa753` |
| `invoke-postgres-snapshot-stale.ps1` | `cc708dd8b6c1d64274717d9da5f36c2544d7347be590caac22fbeb0c7d79c380` |
| `job-status.ps1` | `70ed04247d68317fcd2f88d9db83b17495edde14cef725d6dd1108a641e743ca` |
| `telegram-sender\tg-send.exe` | `388c34d6bd2604475299a67fc184eb699c5ea8214950b6316c056182750726e9` |
| `telegram-sender\tg-send.dll` | `4fd7c66c098ad22c7ce8b5b3362bac82a3c8cb53c1a3db76ef35ec53cd890d5c` |

Three launchers have local deployment changes. The main launcher prepends `C:\Program Files\Git\usr\bin` to the server2 snapshot child's PATH. The Windows OpenSSH client stalled when Docker invoked it under the worker's non-PTY bridge. Git's SSH client passed the worker-side container check and the manual run. The snapshot script and source pin remain byte-for-byte master. `job-status.ps1` uses the prebuilt `telegram-sender\tg-send.exe`. The two job launchers report the Telegram result separately, so a sender failure does not change the backup or stale-check exit status. Preserve these changes on the next runner refresh.

The file-based publish attempt stopped during MSBuild project parsing, before compilation. The sender was then published once successfully from a temporary conventional project using the same source and Confluent.Kafka 2.5.3. The repository's PowerShell build-slot gate at `4877e5958cf5aa85acc61b18aef35cbb760efe42` avoided building the current C# gate on the desktop. The successful publish used lease `e9d4e294-533b-4a74-b1fa-1c382e4a10b2`, max CPU count 4, and released the lease after 23 seconds. No repository tests were run.
