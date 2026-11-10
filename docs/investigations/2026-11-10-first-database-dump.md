# First desktop database dump (2026-11-10)

Read-only custom-format dump of the desktop `antiphon-postgres` database `antiphon` (Postgres 16.14). The file is unencrypted. It lives outside OneDrive, and the folder ACL is only `DESKTOP-KTLKPIF\lndco:(OI)(CI)(F)` with inheritance removed.

- Path: `C:\Users\lndco\antiphon-backups\antiphon-20261110T104159Z.dump`
- Size: 530,395,357 bytes
- SHA-256: `5903c66c8491715e6b16961ba67f6bc559526e9da0628906f5f46ac0d14bf589`
- Dump duration: 78.1 seconds. `pg_dump` stderr was empty.
- Database size at dump start: 2,099,780,631 bytes. The dump is 25.26% of that size (3.96:1).
- Format: `pg_dump -Fc -Z 6 --no-owner --no-acl`, streamed to the host. No dump file was written in the container. The production container and `antiphon_pgdata` were not changed.

`pg_restore --list` reports archive header `TOC Entries: 907` and prints 903 numbered rows (139 `TABLE`, 139 `TABLE DATA`, 324 `INDEX`). A throwaway `postgres:16` container on `127.0.0.1:38147` restored the archive in 93.5 seconds with exit 0 and no warnings, then was removed with `docker rm -f -v antiphon-dump-drill-52146b44`. Its anonymous volume was gone afterwards. The restored database had 139 public tables.

Largest relations by `pg_total_relation_size` on the live database after the dump (total includes indexes and TOAST):

| Relation | Total bytes | Heap bytes |
|---|---:|---:|
| public.TranscriptEntries | 1,339,023,360 | 748,781,568 |
| public.AgentTasks | 143,941,632 | 37,994,496 |
| public.SessionQueuedMessages | 138,248,192 | 52,281,344 |
| public.WorkspaceUseReservations | 104,775,680 | 83,165,184 |
| public.AgentTaskEvents | 99,819,520 | 66,306,048 |
| public.AgentTaskLandNotifications | 83,025,920 | 27,566,080 |
| public.Alerts | 39,886,848 | 9,306,112 |
| public.WorktreeResidueRunCandidates | 32,669,696 | 26,025,984 |
| public.HostOccupancySamples | 16,408,576 | 9,633,792 |
| public.CardRevisions | 9,273,344 | 4,669,440 |

Row counts below are the restored snapshot versus the live database a few minutes later. The live database stayed writable, and every difference is live ahead of the snapshot.

| Table | Restored | Live | Delta |
|---|---:|---:|---:|
| TranscriptEntries | 1,347,994 | 1,348,117 | +123 |
| WorkspaceUseReservations | 407,580 | 407,679 | +99 |
| AgentTaskEvents | 172,111 | 172,122 | +11 |
| WorktreeResidueRunCandidates | 104,840 | 104,840 | 0 |
| SessionQueuedMessages | 60,393 | 60,399 | +6 |
| HostOccupancySamples | 60,363 | 60,363 | 0 |
| AgentTasks | 26,532 | 26,533 | +1 |
| CardRevisions | 13,459 | 13,460 | +1 |
| Alerts | 10,168 | 10,170 | +2 |
| AgentTaskLandNotifications | 9,322 | 9,323 | +1 |

Live database size after those counts: 2,099,960,855 bytes. These ten relations account for 2,007,072,768 bytes of `pg_total_relation_size`.
