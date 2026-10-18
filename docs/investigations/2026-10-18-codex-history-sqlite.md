# Why server2 Codex history sqlite is never cleared

Date: 2026-10-18. Stage: Investigate. Task `41e2425f`, branch `feat/card-task-41e2425f`.
Host: session-runner container `a6457bc89508`, uid 1654 (`app`), `HOME=/home/app`, `CODEX_HOME=/state/codex`.
Codex: `codex-cli 0.160.0` at `/usr/local/bin/codex` → `/opt/codex/0.160.0/package/vendor/x86_64-unknown-linux-musl/bin/codex`.
Source compared with tag `rust-v0.160.0` (the installed binary contains the same SQL strings).
Reads were read-only (`node:sqlite` `readOnly: true`, uid 1654). `auth.json` was not opened. No row text, prompt, or credential was printed. Nothing was deleted.

Related measurement: CARD-0907, `docs/investigations/2026-10-18-codex-0160-ready.md` on `feat/card-task-13a42530` at `87fad6edb`. A cold 0.160.0 start spent ~51s reading these two files before the footer. That note is on the other branch; it is not in this commit.

## Verdict

Confirmed. The two files stay large for different reasons, and both reasons are the Codex 0.160.0 design plus this home's workload.

- `logs_2.sqlite` does delete log rows older than 10 days. The delete leaves the pages in the file. Giving them back requires a live Codex process and a free-page ratio Codex currently treats as optional work. On this host the process does not stay up long enough, so the file remains at its high-water size.
- `thread_history_1.sqlite` has no age retention and is excluded from that page-return worker. Every Antiphon session is materialised into it and kept.
- All 1,368 saved sessions are Antiphon card-task worktrees. There are no one-off sessions in this home.
- Antiphon's existing cleanup never selects them. Codex 0.160.0 has no prune or vacuum command, and no config key for either store.

## What the files contain

Inventory at the start of the read, names and sizes only:

| File | Bytes | mtime (UTC) |
|---|---:|---|
| `logs_2.sqlite` | 1,500,565,504 | 2026-10-18T21:44:17Z |
| `logs_2.sqlite-wal` | 354,352 | 2026-10-18T21:44:17Z |
| `logs_2.sqlite-shm` | 32,768 | 2026-10-18T21:44:17Z |
| `thread_history_1.sqlite` | 1,795,739,648 | 2026-10-16T14:07:35Z |
| `thread_history_1.sqlite-wal` | 4,190,072 | 2026-10-16T14:11:07Z |
| `thread_history_1.sqlite-shm` | 32,768 | 2026-10-18T21:44:20Z |
| `state_5.sqlite` | 7,204,864 | 2026-10-16T14:09:21Z |
| `sessions/` (1,368 files) | 5,369,756,086 | days 2026-09-25 through 2026-10-16, no 2026-10-10 |

`auth.json` is 3,880 bytes, mode `0600`, mtime `2026-10-15T06:54:18Z`. It was not read. `config.toml` is 372 bytes. Its keys are `check_for_update_on_startup`, `cli_auth_credentials_store`, `forced_login_method`, two `projects` trust sections (`/work/repos/antiphon`, `/work/repos/slides`), `tui` / `tui.model_availability_nux`, and `notice`. There is no retention, prune, vacuum, or history key.

Both databases are WAL, page size 4,096, `auto_vacuum=2` (incremental). `wal_autocheckpoint` is 1,000.

### logs_2.sqlite

Header: 366,349 pages, freelist 104,604 pages = 428,457,984 bytes (28.6% of the file). User table `logs`, 837,757 rows. Columns: `id`, `ts`, `ts_nanos`, `level`, `target`, `feedback_log_body`, `module_path`, `file`, `line`, `thread_id`, `process_uuid`, `estimated_bytes`. `_sqlx_migrations` has 2 rows, both installed `2026-09-25 07:12:41`.

`ts` is unix seconds. Range: `2026-10-08T21:44:26Z` through `2026-10-16T18:02:30Z`. At `2026-10-18T21:57:26Z` the 10-day cutoff was `2026-10-08T21:57:26Z`, and 307 rows were older than that cutoff (the minutes on Oct 8 just before it). `SUM(estimated_bytes)` is 615,502,148. Rows with `thread_id IS NULL`: 505,956. Distinct non-null `thread_id`: 1,006. Levels: TRACE 540,413, DEBUG 150,268, INFO 146,812, WARN 249, ERROR 15.

Rows by UTC day: Oct 8 11,615; Oct 9 32,165; Oct 11 110,665; Oct 12 132,786; Oct 13 264,499; Oct 14 155,293; Oct 15 80,181; Oct 16 50,553. No Oct 10 bucket. The file mtime is the Oct 18 cold open; the newest stored `ts` is still Oct 16.

### thread_history_1.sqlite

Header freelist 0. Pragma `page_count` 438,428 (WAL included), `freelist_count` 0. The bytes are live rows.

| Table | Rows | What was measured |
|---|---:|---|
| `thread_history_projection_state` | 1,368 | `SUM(next_rollout_byte_offset)` = 5,369,756,086, the same byte count as `sessions/` |
| `thread_items` | 256,938 | `created_at_ms` `2026-09-25T07:12:47.625Z` through `2026-10-16T14:11:01.477Z`, 1,368 distinct `thread_id` |
| `thread_turns` | 1,551 | `started_at` is unix seconds, `2026-09-25T07:12:47Z` through `2026-10-16T14:10:42Z`, 1,368 threads |
| `thread_realtime_items` | 0 | |
| `_sqlx_migrations` | 7 | first six installed `2026-09-25 07:12:47`, seventh `2026-10-02 11:06:49` |

`thread_items.item_type` counts: commandExecution 111,749, reasoning 107,993, agentMessage 25,439, fileChange 7,996, sleep 1,823, userMessage 1,575, contextCompaction 177, webSearch 154, imageView 30, mcpToolCall 2. Turn status: completed 1,475, failed 61, inProgress 15. Item JSON was not selected.

### Pool sessions versus one-offs

Classification is from `state_5.sqlite` `threads` (1,368 rows), counts only. Created `2026-09-25T07:12:44.583Z` through `2026-10-16T13:24:29.140Z`. Updated through `2026-10-16T14:11:07.518Z`. Archived 0, pinned 0.

| Bucket | Count |
|---|---:|
| `source=cli` | 1,368 |
| `thread_source=user` | 1,368 |
| `originator=codex-tui` | 1,368 |
| `model_provider=openai` | 1,368 |
| cwd under `/work/worktrees/` | 1,368 |
| `git_branch` under `feat/card-task-` | 1,368 |
| `rollout_path` under `sessions/` | 1,368 |
| any other cwd, branch, or path | 0 |

CLI version on the row: 0.160.0 × 923, 0.156.1 × 445. Model: gpt-6-astra 832, gpt-6-sol 322, gpt-6.1-sol 209, gpt-5.6-terra 4, gpt-6-luna 1.

This home is the runner pool. Every saved session is an Antiphon card-task worktree. One-off count is 0. Mean shape: 256,938 / 1,368 ≈ 188 items and 1,551 / 1,368 ≈ 1.13 turns per session, across 21 session-days (about 65 new rollout files per day that has any).

`sessions/` day counts (files, bytes): 09-25 64 / 231,388,528; 09-26 38 / 149,872,107; 09-27 79 / 340,186,240; 09-28 63 / 288,018,346; 09-29 57 / 199,614,438; 09-30 60 / 297,998,609; 10-01 52 / 266,148,891; 10-02 73 / 313,065,814; 10-03 63 / 289,181,223; 10-04 122 / 446,811,884; 10-05 96 / 350,372,610; 10-06 15 / 55,130,213; 10-07 33 / 125,962,356; 10-08 57 / 224,992,107; 10-09 18 / 65,507,864; 10-11 75 / 206,541,204; 10-12 88 / 290,439,492; 10-13 152 / 573,963,751; 10-14 89 / 330,726,419; 10-15 48 / 229,659,195; 10-16 26 / 94,174,795.

## Why they are never cleared

### logs_2: rows age out, the file does not

Tag `rust-v0.160.0`:

- `LOG_RETENTION_DAYS = 10` (`codex-rs/state/src/runtime/logs.rs:3`).
- Every state-runtime open calls `run_logs_startup_maintenance` (`codex-rs/state/src/runtime.rs:278`), which runs `DELETE FROM logs WHERE ts < ?` (`logs.rs:283-297`) and then `PRAGMA wal_checkpoint(PASSIVE)` (`logs.rs:301-303`). PASSIVE does not truncate the database file.
- After each insert, only the partitions touched by that batch are capped at 10 MiB and 1,000 rows (`runtime.rs:79-86`, `logs.rs:44-80`). A short process that exits under the cap keeps its rows until the 10-day delete.
- The measured minimum `ts` is the 10-day boundary, so the age delete has run. The 428,457,984 freelist bytes are the pages that delete freed and nobody gave back.
- Page return is `SqliteReclamationWorker` (`codex-rs/state/src/runtime/reclamation.rs`). It runs only inside a live Codex process, and only for databases with `background_reclamation: true`. That flag is true for `logs_2.sqlite` and false for `thread_history_1.sqlite` and `state_5.sqlite` (`codex-rs/state/src/sqlite.rs:62-112`).
- The worker starts a pass only when `auto_vacuum` is incremental and free space is at least 64 MiB and at least 25% of the file (`reclamation.rs:301-304`). It then keeps a reserve of `max(16 MiB, 10% of pages)` (`reclamation.rs:311-331`) and issues `PRAGMA incremental_vacuum` in batches of 64 pages, at most 1,024 pages per 100ms pass (`reclamation.rs:17-24`, `:334-336`). One elected process holds `.sqlite-maintenance.lock` (`reclamation.rs:115-124`).
- This file is eligible today: 104,604 free pages is 28.6%, above 25%, and above 64 MiB. A process that stayed up and won the lock would reclaim about 104,604 − 36,634 ≈ 68,000 pages (~265 MiB) and then stop at the 10% reserve. Arithmetic from those constants, not a timed run. The freelist is still 28.6%, so that pass has not finished since the hole reached this size. Pool sessions are about one turn long, and between them no Codex process has this home open (fd scan below).

There is no config knob. `codex --help` on 0.160.0 has `delete`, `archive`, `unarchive`, `resume`, and `migrate-rollouts`. It has no prune and no vacuum. `rollout_budget` in `codex features list` is a token-budget feature (`under development`, effective false), not a disk cap. `memories` is `stable` / false, and `config.toml` has no `memories` table, so `max_rollout_age_days` in the binary's `MemoriesToml` is inactive.

Startup deliberately refuses a full `VACUUM` of an existing database (`sqlite.rs:211-213`).

### thread_history_1: nothing deletes it

`delete_thread` removes rows for one `thread_id` from `thread_items`, `thread_realtime_items`, `thread_turns`, and `thread_history_projection_state` (`codex-rs/thread-store/src/local/thread_history.rs:244-286`). The binary has no age predicate for those tables. `background_reclamation` is false, and the freelist is already 0, so the worker would have nothing to return even if it were allowed to visit this file. The 1.67 GiB is the projection of all 1,368 rollouts: the summed projection offsets equal the `sessions/` byte count exactly. Sessions from 2026-09-25 are still in the projection. Their log rows are already gone.

### Antiphon does not clean this home

`scripts/cleanup-codex-test-residue.ps1` reads `state_5.sqlite` only. Its only mutation is `codex delete --force <id>`. A row is a candidate only under the machine temp directory (`outside-temp-NEVER`, around the script's C5 guards). These 1,368 cwds are `/work/worktrees/...`, so they are skipped. Default is dry-run, `MaxPerRun` 10. The header schedules Windmill `u/lndcobra/antiphon_codex_residue_cleanup` (Monday 09:30 Europe/London) as report-only until an operator changes that rollout. `docs/agent-kinds.md:734` forbids deleting a rollout file by hand. `docs/logs.md` names the rollout `jsonl` tree and sets no sqlite retention. `docs/agent-credentials.md` has no cleanup for these files.

### What was running during the read

Two `codex` processes were up, both 0.163.0 under `/tmp/codex-parser-probe-359b4ff7`, with `CODEX_HOME` pointing at that probe home. An fd walk found zero descriptors on `/state/codex`. The single file in `thread-writer-locks` is 0 bytes, mtime `2026-09-25T07:12:44Z`. The production home was idle for the read. A later dispatch can open it; recheck before any delete.

## Safe clear, for an operator to approve

Do not run this from the investigation. Do not `rm` a sqlite file, a `-wal`/`-shm`, or a `sessions/**/rollout-*.jsonl`. A hand-deleted rollout leaves the `threads` row (`docs/agent-kinds.md:734`). Do not run sqlite `VACUUM` yourself: 0.160.0 does not offer it, and a hand vacuum is outside the CLI.

Supported command, one session id per invocation, uid 1654, never root. `<uuid>` is `threads.id` and nothing else (no title, preview, or message column).

Inside the runner container, as uid 1654:

```text
# ids only
node --experimental-sqlite -e 'const {DatabaseSync}=require("node:sqlite"); const db=new DatabaseSync("file:/state/codex/state_5.sqlite?mode=ro",{readOnly:true}); for (const r of db.prepare("SELECT id FROM threads").all()) console.log(r.id)'

# one approved id, after a fresh check that no process has /state/codex open
codex delete --force <uuid>
```

`HOME=/home/app` and `CODEX_HOME=/state/codex` are already the container environment. From the host, the same user:

```text
ssh mc@server2 'c=$(docker ps -q --filter label=com.docker.compose.project=antiphon-runner --filter label=com.docker.compose.service=session-runner); test -n "$c" && docker exec -u 1654:1654 -e HOME=/home/app -e CODEX_HOME=/state/codex "$c" codex delete --force <uuid>'
```

`--force` requires a UUID (`codex delete --help`). There is no all-sessions, older-than, or vacuum flag. Skip any id named by a non-empty live `thread-writer-locks` entry, and skip the delete while a dispatch has this home open. The 15 `inProgress` turns are not a live process (newest turn `2026-10-16T14:10:42Z`, no fd on the home); deleting one still drops that unfinished session.

What `codex delete --force <uuid>` removes, from the 0.160.0 sources plus the CLI contract:

- the saved session, so `codex resume` of that id is gone, and the rollout file goes with the CLI delete (`docs/agent-kinds.md:734`)
- `threads` row, dynamic tools, and spawn edges (`codex-rs/state/src/runtime/threads.rs:1152-1206`)
- `logs` rows with that `thread_id` (`threads.rs:1170`)
- that thread's queue, goal, and memory rows (`threads.rs:1174-1176`)
- `thread_items`, `thread_realtime_items`, `thread_turns`, and `thread_history_projection_state` for that id (`thread_history.rs:262-277`)

What it leaves in place:

- `auth.json`, `config.toml`, `installation_id`, `models_cache.json`, and every other session
- the 505,956 threadless log rows (`thread_id IS NULL`). Those leave only through the 10-day age delete
- the on-disk size of `thread_history_1.sqlite`. Deletes punch a freelist, and the reclamation worker is not allowed to visit that file
- most of the on-disk size of `logs_2.sqlite`, until some later Codex process stays up long enough to incremental-vacuum the freelist down to the 10% reserve

`sessions/` bytes for a deleted id return when the rollout file is unlinked. That is the disk that the CLI actually gives back. The sqlite high-water marks stay.

## Not done, noted

Enabling background reclamation for `thread_history_1.sqlite`, or a CLI vacuum, would be a Codex change; this note does not run either.

## Uncertainties

- Reclamation during the CARD-0907 cold start was not traced. The remaining 28.6% freelist is the evidence a pass has not finished since the hole reached this size.
- `codex delete` was not executed. The rollout unlink is the repo CLI contract; the sqlite cascades are the `rust-v0.160.0` sources whose SQL is present in the installed binary.
- `codex --help` and `codex features list` ran before the aggregate read. `logs.max(ts)` was still `2026-10-16T18:02:30Z`, so those commands did not commit a newer log row before the scan.
