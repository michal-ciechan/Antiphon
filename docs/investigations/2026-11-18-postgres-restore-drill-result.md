# Postgres restore drill on server2: result (2026-11-18)

Task `49232218` (Debug/Ops). Plan: `docs/superpowers/plans/2026-11-18-postgres-restore-drill-plan.md` (review `576f6140`). Runbook: `docs/investigations/2026-11-18-postgres-move-runbook.md` A.2/A.3. Base master `997f9b8d9ffc4ba870d2fd56caf7c03781591624`.

**Outcome: PASS.** All six steps passed. The restore is clean: exit 0 and an empty restore log. The two independent probe restores have identical content fingerprints over all 142 tables. A one-microsecond change made the row probe refuse. Server version is 16.14. Cleanup is complete and the production cluster was not touched.

**Cutover: GO on the measurements.** Copy plus restore is 161 s, and the window gate is 600 s. The estimated window is about 25–35 min. Three deviations, listed under "Deviations", need the operator's acknowledgement.

Drill wall time is about 30 min (16:51Z–17:20Z). Server2 load average was 1.09 at the start and 2.93 at the end (1-minute figure; 24 logical CPUs).

## The dump

| Field | Value |
|---|---|
| File | `C:\Users\lndco\antiphon-backups\antiphon-20261118T075002Z.dump` (the newest; read only, left in place) |
| Size | 728,483,286 bytes |
| Taken | 2026-11-18 07:50:02 UTC (archive header); file mtime 07:51:41Z; nightly `duration` 214 s, `exit` 0 |
| SHA-256 | `8bad24e49a57630aff20c2d658469e5a0982682370a6f33695642fee3217937e`, identical on both ends; `snapshot-status.json` prefix `8bad24e49a57` |
| Format | CUSTOM, gzip, dump version 1.15-0, 934 TOC entries |
| Dumped from / by | 16.14 (Debian 16.14-1.pgdg13+1) / pg_dump 16.14 (Debian 16.14-1.pgdg13+1) |

No new dump was taken. Nothing ran against the desktop Postgres. The only desktop Docker call was a read-only `docker image inspect postgres:16`.

## Timings

| Step | Measured | Plan provisional / ceiling |
|---|---|---|
| M-1 `scp` desktop → server2 | **9.63 s**, 72.2 MiB/s (`copySec` 10) | — / 180 s |
| M-2 `docker cp` into the container | 1.41 s | — |
| M-2 `pg_restore` (drill compose, cpuset 0,12) | **150.97 s** (`dumpRestoreSec` 151), exit 0 | 125 s / 250 s |
| M-2 `ANALYZE` | **9.30 s**, exit 0 | — / 240 s |
| M-3 exact `count(*)` of the 10 tables | 3.23 s total | — / 600 s |
| M-4 probe restore c1974 (`--cpus=4`, no pin) | 141.83 s, exit 0 | — |
| M-4 probe restore c1975 (`--cpus=4`, no pin) | 140.59 s, exit 0 | — |
| M-4 `-VerificationProbe sequences` | 4.19 s, exit 0 | — / 120 s |
| M-4 `-VerificationProbe rollback-rows`, both sides | **294.72 s**, exit 0 | 70 s / **180 s: exceeded** |
| M-5 `rollback-rows` after the update | 202.54 s, exit 3 (refusal) | — / 180 s: exceeded |
| Extra: `-VerificationProbe keys` (CARD-1979 cast) | 9.75 s, exit 0, five key tables match | — |
| Extra: `TranscriptEntries` content fingerprint, one side | 48.2 s and 48.8 s | — |
| Extra: `ssh server2 true` from the desktop | 0.10–0.18 s | — |

The fingerprint probe exceeded its ceiling, but it ended with the correct result. It is a time-budget finding, not a correctness failure. See "Cutover estimate" for what it means for the rollback.

## Pass and fail per criterion

| Check | Result | Evidence |
|---|---|---|
| File | **PASS (deviation D-a)** | Newest file, as the brief asked. The plan named the 20261117 file. Length 728,483,286 is inside the 600M–900M band. |
| Copy | **PASS** | SHA-256 identical, `copySec` 10 |
| List | **PASS** | `TABLE DATA` count 142 |
| Restore | **PASS** | `pg_restore` exit 0 three times (drill, c1974, c1975); every restore log has 0 lines |
| Analyze | **PASS** | exit 0 |
| Counts | **PASS** | Integers below. No `Text` printed. |
| Sequences | **PASS** | exit 0; `PHASE 3 match: sequences`; reads 3 |
| Fingerprint | **PASS** | exit 0; `ROLLBACK ROWS clean`; compared 142 |
| Update | **PASS** | `UPDATE 1`, then exit 3 with `REFUSED: stop gate 6: the new database has rows the old desktop volume lacks.` |
| Volumes | **PASS** | Removed only `antiphon-server2-postgres-drill_pgdata` and the two anonymous volumes (`64da575b…`, `5261ca50…`) |
| Image | **PASS on version; deviation D-b on the digest** | Server 16.14 (Debian 16.14-1.pgdg13+1), the same package as the dump source. The digest differs from the desktop's RepoDigest; see D-b. |
| Desktop cluster | **PASS** | No command was run against `antiphon-postgres` or `antiphon_pgdata` |
| Production cluster on server2 | **PASS** | Read-only `docker inspect` only. Running and healthy. `StartedAt` is unchanged at 2026-11-18T16:49:09.54Z before and after the drill. Volume `antiphon-server2-postgres_pgdata` is present. |
| Server version | **16.14 confirmed** | `PostgreSQL 16.14 (Debian 16.14-1.pgdg13+1) on x86_64-pc-linux-gnu`, on all three clusters |

Fingerprint lines (no row text):

```text
PHASE 3 sequence fingerprint: 025665b869b48d1c275ab9ad86cb6af4dc23ab25f1f217627c405fe788793e25
PHASE 3 sequence reads: 3
ROLLBACK CONTENT fingerprint: f273dfea7d6343e3e46d5a145d6fbe55c835eb470d4a80f6ce617d32513cfaa6
ROLLBACK CONTENT compared: 142
```

## Exact counts (drill cluster, after ANALYZE)

Ten largest public tables by `pg_total_relation_size`. The five script key tables are all among them.

| Table | `count(*)` | total bytes | heap bytes |
|---|---:|---:|---:|
| TranscriptEntries | 1,864,866 | 1,616,470,016 | 986,906,624 |
| HostLoadSamples | 112,868 | 320,782,336 | 67,633,152 |
| AgentTaskEvents | 963,754 | 298,631,168 | 206,438,400 |
| AgentTasks | 29,874 | 151,093,248 | 34,603,008 |
| SessionQueuedMessages | 66,884 | 137,273,344 | 53,477,376 |
| WorkspaceUseReservations | 484,470 | 99,295,232 | 83,755,008 |
| AgentTaskLandNotifications | 9,841 | 66,134,016 | 7,471,104 |
| WorktreeResidueRunCandidates | 152,789 | 36,683,776 | 30,670,848 |
| HostLoadSampleIdentities | 112,868 | 26,460,160 | 17,170,432 |
| Alerts | 12,059 | 12,607,488 | 10,551,296 |

The dump listing carries no row counts and neither does the plan, so there is no recorded figure to compare these against. The desktop live database was out of bounds. The comparison that does exist is restore against restore: two independent restores of the same archive have the same count and md5 for all 142 tables (`ROLLBACK ROWS clean`).

Restored database size: 2,859,818,007 bytes (2,727 MB). The 2026-11-17 status note gives 3,018 MB for the desktop. The difference is consistent with a freshly loaded cluster that has no bloat or dead tuples. It is not evidence of missing data.

## Dump against restored schema

| Object | Dump TOC | Restored catalog (public) | Match |
|---|---:|---:|---|
| Tables | 142 TABLE, 142 TABLE DATA | 142 | yes |
| Indexes | 328 INDEX + 144 CONSTRAINT (PK/UNIQUE) | 472 | yes (328 + 144) |
| Foreign keys | 155 FK CONSTRAINT | 155 | yes |
| Sequences | 3 SEQUENCE, 3 SEQUENCE SET | 3 | yes; values match across restores |
| Triggers | 5 | 5 | yes |
| Functions | 6 FUNCTION | 9 | yes: 6 plus the 3 `pg_stat_statements` functions the extension creates in `public` |
| Other constraints | (inside CREATE TABLE) | 162 p/u/c/x = 144 + 18 | The 18 are not separate TOC entries. Probably inline CHECK constraints, not itemised. |
| Extension | 1 EXTENSION, 1 COMMENT | `pg_stat_statements 1.10`, `plpgsql 1.0` | yes |
| Invalid indexes / not-validated constraints | — | 0 / 0 | clean |
| EF migrations | — | 238 rows, `20260316224333_InitialCreate` .. `20261114063858_AddSessionTranscriptSummaries` | — |
| Owners | — | every public relation owned by `antiphon` (619) | — |

No schema difference was found.

## What the restore log warns about

Nothing. All three `pg_restore --no-owner --no-acl` logs were empty, with no warnings and no errors.

- **Extension `pg_stat_statements`.** `CREATE EXTENSION` restored cleanly as 1.10. `shared_preload_libraries=pg_stat_statements` is on the compose command line, so the view is usable after the restore. A target without that preload would still restore the extension, but its view would error on read. The production compose sets the preload.
- **Owners and roles.** There were no mismatch warnings. `--no-owner --no-acl` suppresses ownership and grants, and in any case the role is `antiphon` on both sides: the image's `POSTGRES_USER`, a superuser. Every restored object ended up owned by `antiphon`.

## CPU

Both the drill compose and the production cluster are pinned to `cpuset: "0,12"` with `cpu_shares: 2048`. Inspect on both shows `cpuset=0,12`. CPUs 0 and 12 are one physical core and its SMT sibling, on a host with 24 logical CPUs. The drill compose `mem_limit` is 8g; the plan text says 4g, and the compose was changed after the plan was written.

During the restore the drill shared those two logical CPUs with the empty production cluster. This was harmless: production stayed healthy and its `StartedAt` did not change.

Restore CPU use, taken from cgroup v1 `cpuacct.usage`: 197.3 CPU-seconds over 151.0 s of wall time. That is an average of **1.31 cores** out of the 2 pinned. `docker stats` readings were 107–138% for most of the restore, with a peak of **200%** during the index or constraint build near the end. Memory peaked at about 1.7 GiB. `ANALYZE` used 11.8 CPU-seconds over 9.3 s.

The unpinned probe restores (`--cpus=4`) took 141–142 s, only about 6% faster than the pinned run. A serial `pg_restore` is the bound, not the pin. The cutover restore lands on the production service, which has the same pin, so the 151 s figure is the representative one.

## Sizing measurements (M-6, read-only on the drill)

The live production GUCs were not read; that was out of bounds.

| Item | Value |
|---|---|
| Database | 2,859,818,007 bytes |
| `TranscriptEntries` | heap 986,906,624 + TOAST 280,813,568 + indexes 348,479,488 = 1,616,470,016 bytes; 1,864,866 rows, about 867 B/row all-in; 26 columns |
| `shared_buffers` | 128 MB (16384 × 8kB, image `postgresql.conf`) |
| `work_mem` / `maintenance_work_mem` | 4 MB / 64 MB (defaults) |
| `effective_cache_size` | 4 GB (default) |
| `max_connections` | 100 (image config) |
| `max_parallel_workers_per_gather` / `max_parallel_workers` / `max_worker_processes` | 2 / 8 / 8 (defaults) |
| `max_wal_size` / `checkpoint_timeout` / `wal_level` | 1 GB / 300 s / replica |
| `pg_stat_statements` | `track=top`, `max=5000` (command line), `track_utility=on` (default), extension 1.10, view readable |

The plan's later-settings recommendations stand: `shared_buffers` 1 GB; `max_parallel_workers_per_gather` 2, which is already the default. One addition: the restore's index builds ran at the default 64 MB `maintenance_work_mem`. Running the cutover `pg_restore` with `PGOPTIONS='-c maintenance_work_mem=512MB'` is a likely cheap speed-up, but it has not been measured. It is not needed to meet the gate.

## Cutover estimate (derived from measurements)

The cutover restores the **final** dump. The dump grew by about 20 MB/day over the last six nightlies (638.7 → 728.5 MB), so a window within a few days restores a file of roughly the same size.

| Window piece | Estimate | Basis |
|---|---|---|
| Phase 0–1 (graceful stop) | 3–5 min | runbook D |
| Phase 2 final dump + age | 3.5–4 min | nightly `duration` 214 s for this dump. Measured under load; a stopped server is likely not slower. |
| Phase 2 copy | ~0.2 min | 72 MiB/s measured (10 s) |
| Phase 2 restore on the production service (same pin) | **2.5–3 min** | 151 s measured, plus a margin for growth |
| Phase 3 verify | 3–5 min | Queries are cheap: the ten largest counts took 3.2 s, sequences 4.2 s, keys 9.8 s. The cost is 142 × 2 `count(*)` spawns, with desktop `docker exec` and `ssh` at about 0.1–0.2 s each plus `compose exec`. Spawn overhead is not measured in the window shape. |
| Phase 4 restart + SHA gate | 5–8 min | runbook D |
| Phase 5 + canary | 10 min | runbook D |
| **Window total** | **~27–35 min** | Inside the 60 min window. `dumpRestoreSec + copySec` = 161 < 600, so `-AcceptDrillTime` is not needed. |
| Rollback gate 6 (if used) | ~5 min of fingerprinting + restart ≈ 12–15 min | Both sides together took 294.7 s here. `TranscriptEntries` alone is about 48 s per side, well inside the 1200 s per-table `statement_timeout`. In a real rollback the source side runs on the desktop. |

## Deviations (for the operator to acknowledge)

- **D-a. Dump choice.** The brief said to take the newest file, `20261118T075002Z`. The plan's M-1 says to take the 20261117 file and not a 20261118 one. I followed the brief. The two files are the same shape with one day of growth, so this does not change the conclusion.
- **D-b. Image digest.** The drill used `postgres:16@sha256:95206741…` as the brief and compose specify: image id `8a4b0458…` on server2, `PG_VERSION=16.14-1.pgdg13+1`.
  - The desktop's `postgres:16` is image id `f40a5645…`, which the plan names, but its RepoDigest is **`postgres@sha256:4b7183ac05f8ef417db21fd72d71047a4238340c261d3cc3ddb6d579ab5071ae`**, not `95206741…`.
  - Both images carry the same PostgreSQL package, 16.14-1.pgdg13+1, which is also the version the dump records. They are probably two image builds of the same patch, for example a base-layer rebuild.
  - A logical dump and restore is unaffected by this: indexes are rebuilt on the target. The production compose already uses `95206741…`.
  - The plan's "Pinned RepoDigest of id `f40a5645…`" check therefore does not hold literally. The operator either accepts `95206741…` or re-pins to `4b7183ac…`.
- **D-c. Plan blob references are stale.** The plan names drill compose blob `cace91c5…` and script blob `32463bd3…`. Master `997f9b8d9` has drill compose blob `2d4fae51…`, which now carries the cpuset pin and `mem_limit` 8g, and script blob `bf479b68…`, which adds a `keys` probe and a 1200 s statement timeout. The staged copy under `/home/mc/antiphon-server2/postgres/` has the same git blob, `2d4fae51…`, with LF line endings. I used the master versions.
- **D-d. Fingerprint time.** The `rollback-rows` probe took 295 s, against the plan's provisional 180 s ceiling. The result was correct. The runbook's 10–15 min rollback estimate still holds.
- **Observation.** The production container on server2 was started at 16:49:09Z, two minutes before this drill began. That was not done by this task.

## Cleanup (exact names)

Removed:

- the drill project (container `antiphon-server2-postgres-drill-postgres-1`, network `antiphon-server2-postgres-drill`, volume `antiphon-server2-postgres-drill_pgdata`);
- the probe containers `c1974-65ba05ca0bbb9646` and `c1975-9c54c5d2d9e943a7`, with their anonymous volumes (`docker rm -v`, after a mount check that each had exactly one anonymous data volume);
- the staged dump and `/var/tmp/antiphon-server2-postgres-drill/`;
- the throwaway password file `/home/mc/antiphon-server2/drill/drill.env` (mode 0600, generated on server2 with `openssl rand`, never printed), together with the whole `drill/` directory, its logs and the copied scripts.

The two dangling volumes still on the host (`5d80e103…` and `schoolrevision-staging_school-revision-staging-pgdata`) existed before the drill and were left alone. `/home/mc/antiphon-server2/postgres.env` was not read.

## Drill record (draft, for operator sign-off)

The record is written outside the repo at `C:\Users\lndco\antiphon-drill\postgres-restore-drill-20261118.json`:

```json
{ "passed": true, "latencyMedianMs": 1.93, "dumpRestoreSec": 151, "copySec": 10 }
```

It also carries notes: dump name and hash, TABLE DATA 142, public heap 142, both fingerprints, the image digest, and deviations D-a and D-b.

The runbook says the operator signs the record off. `passed` is true because M-1 through M-6 passed. D-b is for the operator to accept before the window.
