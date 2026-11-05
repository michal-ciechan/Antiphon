# Server2 Codex state cleanup

Date: 2026-11-05. Stage: Debug. Task `e36a0edf`, branch `feat/card-task-e36a0edf`.
Host: session-runner container `dfcc116be984`, uid 1654 (`app`), `HOME=/home/app`, `CODEX_HOME=/state/codex`.
Codex: `codex-cli 0.160.0` at `/usr/local/bin/codex` → `/opt/codex/0.160.0/package/vendor/x86_64-unknown-linux-musl/bin/codex`.
`auth.json` was not opened. No Antiphon setting, including `CodexReadyMaxWaitMs`, was changed.

## Verdict

Cleaned. The home shrank from 4,836,141,356 bytes to 1,008,112,509 bytes. A cold `gpt-6-astra` start on a 120×30 pty painted a gate-shaped model footer in 2.261 s, and the `xhigh` footer in 4.263 s. Both are under the 20 s target. Auth, config, and the installation id were not modified.

## Why this home

Debug `56b0f9aa` found two Astra Reviews killed at the ready gate on 2026-11-04 because Codex painted the greeting and never the model footer. At that time `logs_2.sqlite` was about 1.12 GB with a 29.8 MB WAL still being written at SIGKILL, and `thread_history_1.sqlite` was 826 MB. The last cold footer this home painted quickly was 3.545 s, immediately after the 2026-10-19 wipe.

CARD-1347 asks for a scheduled dry-run-first sweep and is still InProgress. No sweeper script exists on `origin/master` or on a `*1347*` branch. `scripts/cleanup-codex-test-residue.ps1` reads `state_5.sqlite` only, mutates only through `codex delete --force`, skips every cwd outside the machine temp directory, and refuses above 10 candidates. It does not select these pool sessions. The apply path used here is the CLI, one UUID at a time, as uid 1654.

## Gate

An fd walk found no open path under `/state/codex`. `ps` showed no `codex` process. `GET /api/agent-tasks?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f&status=Queued,Dispatched,Working,Blocked` returned no Codex row (7 open rows, all Claude or Grok, including this Debug). The same check was repeated every 40 deletes and stayed at zero Codex rows.

The three named Astra tasks were already finished:

| Task | Status | Completed (UTC) |
|---|---|---|
| `7c7132b3` | Failed | 2026-11-04T23:34:09Z |
| `adbd81fe` | Succeeded | 2026-11-04T23:50:30Z |
| `3fd6747e` | Failed | 2026-11-04T23:18:19Z |

Nothing was killed.

## Before

`du -sb /state/codex` = 4,836,141,356. Reads of the sqlite files used `node:sqlite` `readOnly: true`. No row text was printed.

| Path | Bytes |
|---|---:|
| `sessions/` (622 rollout files) | 2,511,526,644 |
| `logs_2.sqlite` | 1,000,501,248 |
| `logs_2.sqlite-wal` | 388,602,552 |
| `logs_2.sqlite-shm` | 786,432 |
| `thread_history_1.sqlite` | 828,149,760 |
| `thread_history_1.sqlite-wal` | 4,791,592 |
| `thread_history_1.sqlite-shm` | 32,768 |
| `state_5.sqlite` + wal | 3,375,104 + 4,845,152 |
| `cache/` | 62,265,582 |
| `plugins/` | 29,433,818 |
| `history.jsonl` | 589,645 |
| `session_index.jsonl` | 83,975 |
| `models_cache.json` | 465,154 |
| `skills/` | 401,774 |
| `shell_snapshots/` | 43,583 |

`logs_2` user table is `logs` only (`_sqlx_migrations` and `sqlite_sequence` beside it). There is no `threads` table. 410,145 rows, 237,000 with `thread_id` null, `ts` from 2026-10-25T23:28:46Z through 2026-11-04T23:50:30Z. Page size 4,096, `page_count` 244,263, `freelist_count` 105,367, `auto_vacuum` incremental. The 388 MB WAL had not been checkpointed back into the main file.

`thread_history_1` tables: `thread_items` 124,463, `thread_turns` 880, `thread_history_projection_state` 622, `thread_realtime_items` 0. `freelist_count` 0, so the 828 MB was live rows. `SUM(next_rollout_byte_offset)` was 2,511,403,764, the same byte count as the rollout files. The projection is a cache of those rollouts. Codex 0.160.0 does not age-delete it, and `background_reclamation` for this file is false, so a CLI delete frees rows and does not shrink the file.

`state_5.threads`: 622 rows, all `source=cli`, 620 with cwd under `/work/worktrees/`, 2 under `/tmp/c907-*` from 2026-10-19. Created 2026-10-19T10:52:11Z through 2026-11-04T23:28:58Z. Rollout days and file bytes:

| Day | Files | Bytes |
|---|---:|---:|
| 2026-10-19 | 51 | 186,083,445 |
| 2026-10-20 | 122 | 421,393,822 |
| 2026-10-22 | 36 | 150,919,309 |
| 2026-10-23 | 96 | 403,258,786 |
| 2026-10-24 | 51 | 198,467,130 |
| 2026-10-25 | 33 | 149,233,739 |
| 2026-10-26 | 48 | 161,694,442 |
| 2026-10-27 | 38 | 137,810,056 |
| 2026-10-28 | 2 | 9,437,936 |
| 2026-10-29 | 21 | 67,761,554 |
| 2026-10-30 | 75 | 399,381,852 |
| 2026-10-31 | 48 | 220,370,689 |
| 2026-11-04 | 1 | 5,591,004 |

## What was kept

Unchanged inode, size, and mtime: `auth.json` (3,880 bytes, mode 0600, inode 134064, mtime 2026-11-04T23:12:57Z), `config.toml` (438, inode 160935), `installation_id` (36, inode 151071).

`models_cache.json` stayed inode 160443 and 465,154 bytes. Codex itself moved the mtime to 2026-11-05T00:45:45Z while refreshing the catalog during the timing probe. The file was not edited by hand.

Also left in place: `skills/` (401,774), `plugins/` (29,433,818), `cache/` (62,265,582), `goals_1.sqlite` (32,768), `memories_1.sqlite` (45,056), `queue_1.sqlite` (40,960), `shell_snapshots/` (43,583), `tmp/`, and the 0-byte lock `thread-writer-locks/01a1a8e4-f26f-76f3-85bc-a669556f1726.lock`. That lock names the 2026-11-04 session, which is inside the keep window. No process held it.

A mode-0600 tar of the small keep-set (`auth.json`, `config.toml`, `installation_id`, `models_cache.json`, `skills/`, `.sandbox_migration`) is `/tmp/codex-keep-e36a0edf.tar`, 839,680 bytes. `cache/` and `plugins/` were not added: together the non-history tree is over the 50 MB backup cap. The tar is not in git.

## What was removed

Cutoff for "older than 7 days" was 2026-10-29T00:29:16Z, using `updated_at_ms` and `recency_at_ms`. 477 threads were on or before that cutoff, including the two `/tmp/c907-*` probes. 145 threads were newer and were kept. Their rollouts are the 2026-10-29, 10-30, 10-31, and 11-04 rows above (693,105,099 file bytes).

`logs_2.sqlite`, `logs_2.sqlite-wal`, and `logs_2.sqlite-shm` were unlinked before the deletes, after the schema check above and with no holder. The next `codex delete` recreated an empty `logs` database at 49,152 bytes.

Each old id was then removed with `codex delete --force <uuid>` as uid 1654. The first delete took 3.8 s while it recreated the log database. The other 476 took 379 s, 0 failures. That is 477 sessions. The CLI unlinked the 477 rollout files (1,818,298,665 file bytes) and the empty day directories `sessions/2026/10/{19,20,22,23,24,25,26,27,28}` were removed after they were listed and confirmed empty.

`thread_history_1` after those deletes: `thread_items` 39,695, `thread_turns` 161, `thread_history_projection_state` 145, file still 828,219,392 bytes, `freelist_count` 148,214 of 202,202 pages. `VACUUM` took 6.1 s and left 218,365,952 bytes, `freelist_count` 0, same row counts. The projection of the kept rollouts stays. Codex rebuilds a missing projection by reading rollout files; deleting the file would have forced that work onto the next sessions, so the compacted file was kept instead.

`history.jsonl` still had 897 lines after the CLI deletes, 736 of them for removed session ids. Those lines were dropped. 161 lines for kept ids remain, 122,522 bytes. Line text was not printed. `session_index.jsonl` was rewritten by the CLI to 144 lines, 19,761 bytes, 0 stale ids. One kept thread, `01a18d0e-c298-7cc1-9806-0498c5625a0f` (created 2026-10-30T13:45:17Z), is in `threads` and not in `session_index.jsonl`.

## After

`du -sb /state/codex` = 1,008,112,509 after the timing probe. Freed: 3,828,028,847 bytes.

| Path | Bytes |
|---|---:|
| `sessions/` (145 rollout files) | 693,146,059 |
| `thread_history_1.sqlite` | 218,365,952 |
| `cache/` | 62,265,582 |
| `plugins/` | 29,433,818 |
| `state_5.sqlite` | 3,375,104 |
| `logs_2.sqlite` | 135,168 |
| `history.jsonl` | 122,522 |
| `skills/` | 401,774 |
| `models_cache.json` | 465,154 |
| `session_index.jsonl` | 19,761 |

`threads` remaining: 145. Oldest `updated_at_ms` is 2026-10-29T09:33:36Z. Newest is 2026-11-04T23:50:30Z. No Codex process was left running.

## Cold start

Foreground pty, 120 columns by 30 rows, `TERM=xterm-256color`, cwd `/tmp/codex-cold-cwd-e36a0edf`, no prompt. `posix_fadvise(..., POSIX_FADV_DONTNEED)` was applied to `thread_history_1.sqlite`, `logs_2.sqlite` and its WAL, `state_5.sqlite`, and `models_cache.json` immediately before this start (each call returned 0). Argv:

`codex --no-alt-screen --dangerously-bypass-approvals-and-sandbox -m gpt-6-astra -c model_reasoning_effort=xhigh -c disable_paste_burst=true -c check_for_update_on_startup=false`

| Mark | Seconds |
|---|---:|
| `OpenAI Codex` banner | 0.291 |
| `Ask Codex to do anything` | 0.294 |
| `GPT-6-Astra default · /tmp/codex-cold-cwd-e36a0edf` | 2.261 |
| `GPT-6-Astra xhigh · /tmp/codex-cold-cwd-e36a0edf` | 4.263 |

Ctrl-C went out 2 s after the first footer. The child exited 0 at 4.726 s. The `xhigh` line also ends with `1 warning`. No rollout file was created. `auth.json` was still inode 134064, 3,880 bytes, mtime unchanged.

`IsModelEffortCwdFooter` accepts the 2.261 s line: a middle dot, two tokens on the left, and a cwd on the right. `default` is one of the effort words `TryReadFooterOnlyModel` allows, and `xhigh` replaced it by 4.263 s. Either line is inside the 20 s target.

An earlier capture in the same hour used `script` without a window size. Its frame was not a readable footer, so that 90 s run is not a timing result. A 120×30 start after that capture, without `fadvise`, painted the `default` footer in 0.862 s.

## Recommendations

Schedule the CARD-1347 sweep. The card is still the owner, and the script it describes is not on master. A useful sweep lists threads whose `updated_at_ms` is older than a cutoff (this pass used 7 days; the card's sketch says 14), prints counts and bytes, then deletes one UUID at a time with `codex delete --force` as uid 1654 and only while nothing has `/state/codex` open. `cleanup-codex-test-residue.ps1` is the wrong tool for this home.

Alarm when `logs_2.sqlite` plus its WAL, or `thread_history_1.sqlite`, passes about 256 MB. Codex 0.160.0 deletes log rows older than 10 days and leaves the file at its high-water size, and it never returns pages from `thread_history_1`. That pair is what held the ready footer past the launch gate on 2026-11-04. The CLI delete does not shrink `thread_history_1`; a follow-up `VACUUM` while the home is idle, or a recreate of `logs_2` when its only user table is `logs`, is the step that gives the space back.
