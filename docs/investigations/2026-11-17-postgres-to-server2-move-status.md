# Postgres to server2: where the move stands and whether it can continue (2026-11-17)

Investigate task `a84df3ad` (CARD-1844 / CARD-1401), read-only. Observations 2026-11-17 09:11–09:20Z from the server2 runner container.
Master read: `7901e9ba537b757740c2913aa2211d513b0b31f2` (origin/master, 2026-11-17T04:06Z). Running desktop server: `GET /api/version` = `870a93d21f2b18c85a8b0905ab689b28e3099813` (rc/2026-11-24), `land-v2`.
No database login, no SQL, no Docker or volume operation, no restart. The only network probes were TCP connects plus one unauthenticated PostgreSQL `SSLRequest` (8 bytes; the server answers one byte) per sample. No secret or connection string appears here.

## Verdict

**Yes, the move can continue. The October latency gate that blocked a DB-only move no longer holds.**

- TCP connect from server2 to the desktop's published Postgres port (`100.79.51.37:17280`) now has a median of **1.6–1.9 ms**. On 2026-10-21 the same path measured **72–127 ms**.
- The move is still blocked, by operator-side prerequisites rather than by latency:
  - The server2 host Postgres (CARD-1406) is not deployed: port 5432 is refused on both server2 addresses.
  - No server2 restore drill exists.
  - The cutover script has two unfixed verification defects (F-1/F-2, 2026-11-12), and no card has been filed for either.
  - CARD-1844's S1/S2 code is unlanded. Its Review is Interim and its Test task was blocked.
- Postgres CPU will **not** fall much further from the fixes still in flight. Every landed chattiness fix is already in the running rc, because master..rc contains no `server/` or `src/` change. At their cumulative means, the seven top statements explain only about 0.2 core of the measured ~1 core.

## 1. State per stage

### Stage A: chattiness. All landed fixes are already running

`870a93d21` is an ancestor of master. `git diff --name-only 870a93d21 origin/master` touches only `docs/`, `tests/` and seven `scripts/` files, none of them server code. **No fix exists on master that is not yet running.** rc/2026-11-20 (`b559bfba2`) was the first rc that carried CARD-1886 S1/S2 and CARD-1887. rc/2026-11-21 (`64e3d78c4`) added CARD-1915 S1–S3. The time the desktop activated either rc is **UNOBSERVED**. The current server process started at 2026-11-17T03:35:48Z (`/api/diagnostics/session-state`).

| Card | Board | On master / in running rc | What it changes |
|---|---|---|---|
| CARD-1403 | Done | yes. S0 `7bb6355a4` (call-site counter), S1 `d9056c10f` (one boot-reply EXISTS per tick), S3 `a550bcbd2` (one-command ingest prelude) | Round trips per tick and per batch |
| CARD-1886 | Review | yes. S1 JIT off on application connections `12cd80492`; S2 claim query rewrite `7afdaefaf` + `e959f0b33` | Statement 1 (claim); JIT on statement 7 and on pins |
| CARD-1887 | Review | yes (`cf05ed8a0` and earlier) | Open-task membership in `session-state.seed`/`pins`. The running seed text starts `WITH open_tasks AS MATERIALIZED` (`server/Infrastructure/Data/SessionStateLoader.cs:15`), so statement 3 is already the post-1887 form |
| CARD-1915 | Review | S1–S3 only (schema, triggers, fenced activation `f60256fcf`) | **The reader, backfill and activation are not landed.** `DurableTranscriptSummaryEnabled` appears nowhere in `server/`. Statement 3's transcript laterals are unchanged |
| CARD-1933 | Backlog | no | Advisory: EF singleton appends invalidate summaries (matters only after 1915 activation) |
| CARD-1888 | InProgress | **no** (0 master commits) | New plan `2026-11-17-card-1888-transcript-ingest-probe-plan.md` (prefix memo), round-2 plan review; S1 witnesses at `8be0c8d8f` on `feat/card-task-6ea96bcb`. Targets statements 4 and 6. It explicitly excludes `TranscriptPromptSpan.LoadAsync` and the full-row read |
| CARD-1969 | Backlog | no | Statements 2 and 5 (bounded `TranscriptPromptSpan`, attribute the full-row read) |
| CARD-1914 | Review | yes `3adaf53cf` | Server allocation only (no Postgres effect) |
| CARD-1910 | Review | yes (`dc3c1b3e5` …) | Server CPU only (no Postgres effect) |

### Stage B: snapshots. The daily job exists, but recent success is UNOBSERVED

- CARD-1405 (Done, `5763320b3`): an encrypted snapshot script with a restore drill. The operator steps were never evidenced as run.
- CARD-1831 (Review, High/Now): a plain daily snapshot with a Windmill schedule and a Telegram line. Landed on master as `82feba880`, `ed1aee869`, `ef0431e17`, `a0e9f91a5`, `f0155042e`.
- The first dump was taken 2026-11-10 (`8e1811543` on `feat/card-task-52146b44`, `docs/.../2026-11-10-first-database-dump.md`). The database was 2,099,780,631 bytes. The dump took 78.1 s and produced 530,395,357 bytes (3.96:1). **A throwaway `postgres:16` restore drill on the desktop finished in 93.5 s, exit 0, 139 tables.** That drill ran on the desktop; none has run on server2.
- Task b28fd575 (`feat/card-task-b28fd575`, `2026-11-10-backup-runner-refresh.md`) recorded the Windmill schedules `u/lndcobra/antiphon_postgres_snapshot` (`0 50 7 * * *`, Europe/London, enabled, tag desktop) and `..._stale` (08:00, enabled). It also recorded three verified OneDrive dumps (1,594,125,176 bytes).
- **UNOBSERVED:** whether the 07:50 job has succeeded on each day since 2026-11-11, and today. `snapshot-status.json` and the Windmill run history live on the desktop and Windmill, and I could read neither.
- The wrapper still targets the container `antiphon-postgres` and the volume `antiphon_pgdata` (f8a51fd1: `scripts/windmill/invoke-postgres-snapshot.ps1:57,67`, `scripts/postgres-snapshot.ps1:54,69`). It does not follow a moved database.

### Stage C: the move itself (CARD-1401 → CARD-1406/1407/1844)

| Piece | State |
|---|---|
| CARD-1406 host Postgres compose + drill compose | Code landed (`2e43ac0f9`, `057d14128`, …). Card is in Review; the R3 Review `8195f9ce` was Clean but Interim. **Not deployed:** TCP to `100.93.77.126:5432` and `192.168.4.34:5432` is `ECONNREFUSED` (09:18Z). Only `:5433` (Windmill DB) is open |
| CARD-1407 cutover script (`scripts/cutover-postgres-server2.ps1`) | Done, landed. Has never been executed. **F-1** (`:586` selects `is_called` from `pg_sequences`, which PG16 does not have; failed reads compare equal) and **F-2** (the rollback "extra rows" gate is count-only) were reproduced by f8a51fd1 and are still on master. `card.ps1 search` for `is_called`/`pg_sequences` finds **no card** |
| CARD-1844 plan (17db3443) | `011a6349a` on `feat/card-task-17db3443`, **not on master**. Topology A2: AppHost stays, and only the connection string moves |
| CARD-1844 S1 (aec861c3) | AppHost owner sentence + `Database endpoint {Host}:{Port}` log. Unlanded |
| CARD-1844 S2 (8bbde9af) | `bootstrap-check.ps1 -DatabaseProbeOnly/-ConnectionString`, `scripts/lib/DatabaseEndpoint.ps1`. Unlanded |
| **Code 0303dd11** (2026-11-13) | Round-2 repair of Review 37deb941 F1/F2 at `b82f4eccd`: configuration precedence matches Aspire, an empty or malformed value fails closed, a bounded `psql` readiness and database-identity probe (`scripts/lib/RemoteDatabase.ps1`), libpq failure classification with no server text, and a launch-profile mirror. New witnesses: `test-bootstrap-remote-database.ps1`, `scripts/fixtures/Card1844PostgresStub.cs` (loopback wire stub). **It is a health check, not a runbook, a script that moves data, or a staging database** |
| **Review 85da972b** | `.antiphon/task-85da972b.md`: **CLEAN, round 2, Interim**. CP-2 81+34 passed, CP-1 18 passed, guards 81/81 + 88/88. Pending: `HangfirePostgreSqlStorageTests` not run, a Windows PowerShell 5.1 parse owed, operator cutover acceptance, PCs |
| **Test dd593fff** | `docs/investigations/2026-11-13-card-1844-hangfire-storage-run.md`: build green (0 errors, 5m39s). **The test was not run (0 executed).** The brief assumed an external test Postgres on 54329. In fact the class uses the Testcontainers fixture, whose orphan sweep and baked image the brief's limits forbade. Outcome: blocked, so it does not approve a land |
| CARD-1844 S3 (docs + contract witness) | Not started |
| Staging database on server2 | None was produced by CARD-1844. CARD-1742's staging server keeps its own scratch volume, and whether it is running is **UNOBSERVED** (loopback bind 17420) |

### Topology B (CARD-1744): the window has not been run

- Evidence: the public origin serves `870a93d21` from the desktop AppHost, and server2 has no Postgres listener.
- Ready on master:
  - CARD-1775 production server compose (Done)
  - CARD-1777 `ANTIPHON_API` required (Done)
  - CARD-1779 docs (Done)
  - CARD-1742 staging compose (Done)
  - CARD-1743 durable Hangfire (Done)
  - CARD-1745 SHA restart gate (Done)
  - CARD-1752 compose env (`c59ec9232`)
- Still blocking CARD-1744: CARD-1406, CARD-1776 and CARD-1778 (all Review). CARD-1752 additionally waits on CARD-1751 (InProgress).

## 2. What drives Postgres CPU

Window: the orchestrator's `pg_stat_statements` from 2026-11-11T17:45Z to 09:05Z today, 135.3 h (487,200 s). The "core-avg" column is total execution seconds ÷ wall seconds.

| # | Statement | Call site | calls/h | core-avg | Fix (state) |
|---|---|---|---:|---:|---|
| 1 | `SELECT s."Id", s."AcceptedGeneration"…` over `SessionRuntimeActions` | Old EF LINQ form of the runtime-action claim, `SessionRuntimeActionCoordinator.DrainCoreAsync` (`server/Application/Services/SessionRuntimeActionCoordinator.cs:165`) | 2,349 | 0.185 | CARD-1886 S1+S2, **already running**. The running claim is raw SQL starting `WITH candidates AS MATERIALIZED` (`server/Infrastructure/Data/SessionRuntimeActionClaimQuery.cs:14-38`), so the `SELECT s."Id"…` row is most likely pre-fix history. Its queryid and recent calls are **UNOBSERVED**. Another EF full-entity `SessionRuntimeActions` read is not excluded |
| 2 | `TranscriptEntries (Sequence, Text, Timestamp, Kind)` by session, `Kind IN`, `ORDER BY Sequence` | `TranscriptPromptSpan.LoadAsync` (`server/Application/Services/TranscriptPromptSpan.cs:88-94`, per the CARD-1888 plan) | 2,479 | 0.055 | CARD-1969 (Backlog). **Unfixed** |
| 3 | `-- session-state.seed WITH open_tasks AS MATERIALIZED` | `SessionStateLoader` seed (`server/Infrastructure/Data/SessionStateLoader.cs:15`) | 96 (now 181–239) | 0.033 | CARD-1887 running. The transcript laterals need CARD-1915 S4/S5 + activation. **Unfixed** |
| 4 | `-- session-state.identity SELECT t."Uuid", t."Kind"` | `AgentSessionRuntime.PersistTranscriptCoreAsync` 512-key chunk loop (`server/Application/Services/AgentSessionRuntime.cs:1449-1457`) | 22,653 (now 22,505) | 0.031 | CARD-1888 (in Code). **Unfixed** |
| 5 | `TranscriptEntries` full-row select | **Unattributed.** The CARD-1886 plan's S5 names `ChannelReplyDispatcher.ClassifyTtlLossAsync` as a candidate | 49 | 0.028 | CARD-1969 (attribute first). **Unfixed** |
| 6 | `-- session-state.identity SELECT $4 AS Part, s.StartedAt…` | `TranscriptIngestPrelude.ReadAsync` (`server/Infrastructure/Data/TranscriptIngestPrelude.cs:54-85`), tagged identity when UUIDs are present | 759 | 0.018 | CARD-1403 S3 made it one command (running). CARD-1888 cuts the replay re-probe. **Unfixed** |
| 7 | `EXISTS` over `SessionQueuedMessages` | **Unattributed.** About 15 `SessionQueuedMessages.AnyAsync` sites exist. Tagged boot-reply EXISTS runs only about 1,000/h (diagnostics), against 4,171/h for this statement | 4,171 | 0.016 | JIT off is running (CARD-1886 S1). No other card |
| | **Sum** | | 32,556 | **0.365** | |

Three conclusions:

- **Fixed on master but not running: nothing.** The cumulative window spans before and after rc/2026-11-20 activation, so these numbers mix eras and cannot price the current state.
- **The current ~0.99 core is not explained by the seven statements.** At their cumulative means, statements 2–7 cost about 0.18 core. Statement 1 should now be cheap (target <5 ms per the CARD-1886 plan). The remaining **~0.75 core is UNATTRIBUTED**. Candidates are other statements, autovacuum or I/O, CARD-1915 trigger cost, and contention on a desktop at 93% CPU. CARD-1889 is the existing CPU-attribution-gap card.
- **Expected after the next rc that carries CARD-1888 + CARD-1915 S4/S5 + CARD-1969:** at most about 0.15–0.2 core less, so roughly 0.8 core if nothing else changes. That is an estimate from cumulative means, not a measurement.

Server and runner, from my reads:

- **Antiphon.Server:** 0.57 core average since 03:35:48Z (11,675 CPU-s over 5.64 h), 0.44 core over a 199 s delta. Working set 1.12–1.30 GB, managed heap 0.60 GB, gen2 2,511 collections. That is within the range of the orchestrator's 0.3 core / 1.67 GB.
- **DB commands:** 143.6/s (delta) to 176.6/s (since start). 94.6% of them carry no call-site tag (`untagged` 135.8/s).
- **Antiphon.SessionRunner:** **UNOBSERVED** by me; only the orchestrator's 0.02 core / 287 MB exists.

## 3. Latency gate (measured now)

`node` TCP probe from the runner container on server2. `SSLRequest` = protocol round trip with no authentication, answered `N`.

| Target | When (Z) | n | TCP connect ms (min / p50 / p90 / max) | SSLRequest RTT ms (min / p50 / p90 / max) |
|---|---|---:|---|---|
| desktop `100.79.51.37:17280` (Postgres) | 09:15:45 | 30 | 0.85 / 1.87 / 5.63 / 6.52 | 2.52 / 4.26 / 9.04 / 23.73 |
| desktop `100.79.51.37:17280` | 09:15:52 | 30 | 0.86 / 1.93 / 6.21 / 10.27 | — |
| desktop `100.79.51.37:17280` | 09:19:10 | 60 | 0.80 / 1.60 / 9.25 / 21.36 | 3.19 / 7.98 / 24.73 / 65.67 |
| desktop `100.79.51.37:443` | 09:15:55 | 15 | 0.93 / 1.41 / 2.12 / 6.22 | — |
| desktop `100.79.51.37:17202` | 09:16 | 5 | timeout ×5 (no proxy answers every port) | — |
| server2 hairpin `100.93.77.126:22` | 09:16 | 10 | 0.53 / 1.18 / 4.48 / 4.48 | — |

How to read these:

- TCP connect is the network RTT proxy, at about 1.6–1.9 ms.
- The `SSLRequest` figure includes the postmaster fork on a desktop at 93% CPU, so it is an upper bound and not network time.
- The direction measured is server2 → desktop. Desktop → server2 was not measured here (**UNOBSERVED**). It is the same Tailscale pair, and October's desktop-side figure was 6.72 ms.
- **Why 72–127 ms became 1.9 ms: UNOBSERVED.** It is consistent with a relayed (DERP) path in October and a direct path now, but `tailscale status` is not available in the runner.

Added wall time if the server stayed on the desktop and the database moved to server2. Each command adds about one RTT (pooled connections). Seconds of extra waiting per hour, summed over all concurrent paths:

| RTT | Top-7 statements (32,556 calls/h) | All DB commands (143.6–176.6/s) |
|---|---:|---:|
| 2 ms (now) | 65 s/h | 1,034–1,272 s/h (≈0.3–0.35 extra commands in flight on average) |
| 6.72 ms (October desktop→server2) | 219 s/h | 3,474–4,272 s/h (≈1.0–1.2 in flight) |
| 20 ms (cutover script gate) | 651 s/h | 10,339–12,715 s/h (≈2.9–3.5 in flight) |
| 127 ms (October server2→desktop) | 4,135 s/h | 65,654–80,742 s/h (18–22 in flight: not viable) |

The heaviest per-statement contribution at 2 ms is statement 4: 45 s/h.

Per path:

- **Snapshot ingest:** each further 512-UUID identity chunk is a sequential command. The CARD-1888 plan infers about 30 chunks per snapshot, so a replayed snapshot gains about 60 ms at 2 ms, 200 ms at 6.72 ms, and 3.8 s at 127 ms. Since the 03:35Z start there were 32,299 ingest calls, 75% of them fully duplicate batches (24,265), averaging about 4.9 identity commands per ingest call.
- **Seed:** one command per load, so +2 ms on top of about 1.2 s.
- **Per page and per dispatch:** command counts are **UNOBSERVED**. No stored per-request census exists; the CARD-1403 `QueryBudget` harness can produce one.
- **Bandwidth:** statement 2 returns about 1,503 rows/call (11-13 figure). Its bytes are **UNOBSERVED**, so its transfer time on top of RTT is unknown. A rough guess: about 0.9 KB per row averaged over the table, about 1.3 MB per call, about 1 MB/s overall.

**A DB-only move (A2) is viable at the measured RTT.** It passes the CARD-1401 investigation's "well under 10 ms" bar and the script's 20 ms gate. The residual risk is a path flip back to a relayed route: at about 127 ms the server would saturate. Topology B removes that risk.

## 4. Capacity

### server2 (runner container view, 09:16–09:17Z)

- **CPU:** 24 cores, load average 21.09 / 21.13 / 19.25, then 12.13 / 17.72 / 18.30 at 09:19. A 35 s `/proc/stat` delta read 37% busy (8.9 cores), 0% iowait.
- **Memory:** 125 GiB total, 91 GiB available, no swap.
- **Disk:** `/dev/mapper/ubuntu--vg-ubuntu--lv` 705G, 573G used, **104G available, 85%**. On 2026-11-11 it was 91% with 66G free. The host daemon's data-root is **UNOBSERVED**: the runner sees the same LV but not the host `docker info`.
- **Postgres CPU isolation:** none beyond a ceiling. `docker-compose.server2-postgres.yml:21-22` sets `mem_limit: 4g` and `cpus: 4.0`. That is a CFS ceiling, not a reservation; there is no `cpuset` and no `cpu_shares`. The runner compose has no CPU limit (the orchestrator saw 738% of 24 cores). At build load of 20–50, Postgres competes unprotected.
- **Memory fit:** the DB is 3,018 MB and the desktop resident set is 2.83 GiB. A 4g limit fits today but is tight against cache and growth.
- **Disk sharing:** the volume would be on the same LV as the runner volumes. The 20 GiB free gate in the CARD-1844 plan is met on the LV (104G), but the host data-root itself is unverified.

### Desktop benefit

- About 1 core of 8 logical. The desktop is at 93% while the Windows Full run is live.
- About 2.83 GiB of the 7.757 GiB WSL VM (36%) (orchestrator figures).
- The Docker Desktop single point of failure for production data goes away. It stays for qualification Testcontainers.

### Database growth

- 2,099,575,831 bytes on 2026-11-10 grew to 3,018 MB on 2026-11-17, about +130 MB/day.
- The CARD-1844 plan's 30-minute downtime envelope requires a database of at most 2 GiB, so it **no longer applies**.

## 5. Risks and order

### Ordered slices

| # | Slice | Owner | During the live run? |
|---|---|---|---|
| 0 | Read `snapshot-status.json` and the Windmill run history. Confirm each day since 11-11 succeeded and that the stale check is quiet | orchestrator (desktop read) | yes (read-only) |
| 1 | Take a `pg_stat_statements` delta: two reads about 10 min apart, by queryid with query text. This attributes the current ~1 core and confirms that statement 1's `SELECT s."Id"` form no longer accrues calls | orchestrator (desktop, read-only) | yes |
| 2 | File cards and Code the fixes for F-1/F-2 in `scripts/cutover-postgres-server2.ps1` | orchestrator | yes (server2) |
| 3 | CARD-1844 land path: run `HangfirePostgreSqlStorageTests` with its real Testcontainers fixture, a final Review, then S3 docs. Or descope Hangfire, since its storage is InMemory on the desktop | orchestrator | yes (server2) |
| 4 | Server2 host preflight: `docker info` data-root, `df`, host inventory | **operator** | yes (server2 only) |
| 5 | Decide on CARD-1406's Interim Review. Create the mode-0600 env file (new password, `tailscale ip -4` bind, pinned PG16 digest). Run `deploy-server2-postgres.ps1 -WhatIf`, then `-Execute` to get an **empty** cluster. Decide on CPU isolation | **operator** | yes (server2 only; does not touch the desktop) |
| 6 | Restore drill on server2 from a fresh dump. Record dump, copy and restore seconds and exact counts | **operator** | **no.** The dump runs on the desktop (CPU/IO at 93%) |
| 7 | Cutover window: `cutover-postgres-server2.ps1 -Execute -ConfirmWindow` from the main desktop checkout | **operator** | **no** |
| 8 | Post-checks: CARD-1408 monitor, `GET /api/version`. Retarget the CARD-1831 job to the server2 source (the job must follow the database) | orchestrator + operator | — |
| 9 | Topology B window (CARD-1744), after CARD-1406/1776/1778 settle | **operator** | no |

Stage A (CARD-1888 → CARD-1915 S4/S5 + activation → CARD-1969) proceeds in parallel and is not a gate at 2 ms.

### Downtime estimate (not measured)

- Scaling the 2026-11-10 desktop timings by 3,018/2,100 gives a dump of about 112 s and a restore of about 134 s, plus a copy of about 760 MB. That is about 5 min of data movement.
- Adding quiesce (an empty land queue), the AppHost stop and start, and the phase-3 verification gives about 20–40 min. **Book 60 min.** The 30-min envelope is void above 2 GiB.

### Rollback

Phase 6 of the cutover script:

1. Start `antiphon-postgres`.
2. Remove the AppHost user secret.
3. Restart AppHost on the tracked localhost string.

Both volumes are kept. F-2 means count equality alone does not prove there were no writes after cutover.

### Must not happen during the live Windows qualification run (until about 13:30–14:00Z)

- No AppHost stop or restart (cutover phase 1 `-StopOnly`).
- No `docker compose stop postgres`.
- No Docker Desktop or WSL restart, and no `.wslconfig` change. Groups 115–118 failed falsely on 2026-11-10 when the engine died; their Testcontainers ride on Docker Desktop.
- No manual desktop `pg_dump` or restore drill.
- Server2-side preparation (slices 2–5) does not touch the desktop.

### Operator decisions (yes/no, with recommendation)

1. Take the DB-only move (A2) next, now that the RTT is about 2 ms, with the server following later (topology B)? **Recommend yes.** It matches the operator's 2026-10-21 direction that stage C may move the database first. It is reversible, and it is smaller than CARD-1744.
2. Accept the CARD-1406 compose already on master despite its Interim Review, and allow `-Execute` of an empty cluster on server2 once the data-root shows at least 20 GiB free? **Recommend yes.**
3. Require CPU isolation for server2 Postgres (a reservation or pinning, not only `cpus: 4.0`) before cutover? **Recommend yes.** Builds run server2 at load 20–50.
4. Require the F-1/F-2 cutover-script fixes before any window? **Recommend yes.**
5. Book a 60-minute window after the live run and after the server2 drill, rather than today? **Recommend yes**, which means not today unless the drill finishes after 14:00Z.
6. Retarget the daily backup to server2 in the same window? **Recommend yes.** The job must follow the database.

## 6. Recommendation

**Continue.** In order:

1. **First today, read-only, safe during the run:**
   - Confirm the daily backups succeeded since 11-11 (slice 0).
   - Take a 10-minute `pg_stat_statements` delta to attribute the current ~1 core (slice 1).
   - Dispatch the F-1/F-2 cards and the CARD-1844 Hangfire/S3 path on server2 (slices 2–3).
2. **Operator, any time today:** the server2 host preflight, then deploy the empty CARD-1406 cluster with a CPU-isolation decision (slices 4–5).
3. **After the live run ends (about 14:00Z):** take a fresh desktop dump and run the server2 restore drill. Then book the 60-minute window.
4. Carry CARD-1888 / CARD-1915 S4/S5 / CARD-1969 in the next rc and re-measure. They will not by themselves bring Postgres CPU far below about 0.8 core.

## Remaining uncertainties

- Which statement statement 1 is, and the current per-statement rates. These need a desktop delta by queryid.
- Desktop-origin latency, and why the path improved.
- Whether the daily backup ran 11-11 through 11-17.
- The server2 host data-root and its free space.
- SessionRunner CPU (orchestrator figure only).
- The callers of statements 5 and 7.
- Statement 2's bytes per call.
- When the desktop first activated rc/2026-11-20.

## Not done, noted

- Fix idea only: publish server2 Postgres on the LAN address or monitor the Tailscale path type, so that a relay fallback (about 127 ms) cannot silently stall a desktop server running against the server2 database.

--- next stage ---
next: decide
handoff: Operator: approve A2 now (RTT 1.6-1.9 ms), CARD-1406 empty-cluster -Execute after data-root df, Postgres CPU isolation, F-1/F-2 fixes before window, 60-min window after the live run + server2 drill; orchestrator meanwhile confirms backups, takes a pg_stat_statements delta, dispatches F-1/F-2 and CARD-1844 Hangfire/S3.
artifact: docs/investigations/2026-11-17-postgres-to-server2-move-status.md
