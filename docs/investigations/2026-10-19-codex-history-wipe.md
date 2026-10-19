# Server2 Codex history wipe

Date: 2026-10-19. Stage: Deploy. Task `e59b0f15`, branch `feat/card-task-e59b0f15`.
Host: session-runner container `a6457bc89508`, uid 1654 (`app`), `HOME=/home/app`, `CODEX_HOME=/state/codex`, runner id `server2`.
Codex: `codex-cli 0.160.0` at `/usr/local/bin/codex`.
This process was already that user inside that container, so the delete ran in place. Nothing ran as root. `auth.json` was not opened.

## Verdict

Deleted. The named history stores and the 1,368 rollout files are gone. A cold `gpt-6-astra` `xhigh` start painted the ready footer in 3.545s. Auth, config, and the installation id were not modified.

## Gate

An fd walk as uid 1654 found no open path under `/state/codex`. Two `codex` processes were already up, both 0.163.0 under `/tmp/codex-parser-probe-359b4ff7` with that directory as `CODEX_HOME`. They did not have this home open. `GET /api/agent-tasks?status=Working,Dispatched` returned no Codex row. The five open rows were Grok: two CARD-1311 Investigate, this Deploy, CARD-1340 Code, and CARD-0907 Custom. No session was killed.

## Removed

| Path | Bytes |
|---|---:|
| `logs_2.sqlite` | 1,500,565,504 |
| `logs_2.sqlite-wal` | 354,352 |
| `logs_2.sqlite-shm` | 32,768 |
| `thread_history_1.sqlite` | 1,795,739,648 |
| `thread_history_1.sqlite-wal` | 4,190,072 |
| `thread_history_1.sqlite-shm` | 32,768 |
| `state_5.sqlite` | 7,204,864 |
| `state_5.sqlite-wal` | 4,239,512 |
| `state_5.sqlite-shm` | 32,768 |
| `history.jsonl` | 1,216,786 |
| `session_index.jsonl` | 183,850 |
| `sessions/` `rollout-*.jsonl` | 5,369,756,086 (1,368 files) |

`state_5.sqlite` `threads` had 1,368 rows. `session_index.jsonl` had 1,367 lines. Rollout counts by day matched the 2026-10-18 inventory, last day 2026-10-16 (64, 38, 79, 63, 57, 60, 52, 73, 63, 122, 96, 15, 33, 57, 18, 75, 88, 152, 89, 48, 26). `history.jsonl` and `session_index.jsonl` were not in the named list. Both are session history and would have pointed at the deleted rollouts, so they were removed. No other file under `sessions/` existed. Empty day directories were removed. `sessions/` itself remains.

Unlinked file bytes: 8,683,548,978.
`du -sb /state/codex`: 8,800,311,739 before, 116,517,001 after the unlink (delta 8,683,794,738).

## Kept

Unchanged inode, size, and mtime: `auth.json` (3,880 bytes, mode 0600, inode 134064), `config.toml` (372), `installation_id` (36), `models_cache.json` (400,078).

Also left in place: `goals_1.sqlite` (32,768; `thread_goals` 0 rows), `memories_1.sqlite` (45,056; no stage-output rows), `queue_1.sqlite` (40,960; `queued_items` 0 rows), `shell_snapshots/` (30 files), `tmp/`, `plugins/`, `cache/`, `skills/`, `thread-writer-locks/`, `.sandbox_migration`, and `.sqlite-maintenance.lock`. Row text was not printed.

During the unlink, 0-byte `-wal` files and 32,768-byte `-shm` files appeared beside goals, memories, and queue. Nothing held them afterward. They were not deleted.

## Probe

Foreground PTY, 120x30, `TERM=xterm-256color`, scratch `/tmp/codex-wipe-probe-e59b0f15`, no prompt. Argv:

`codex --no-alt-screen --dangerously-bypass-approvals-and-sandbox -m gpt-6-astra -c model_reasoning_effort=xhigh -c disable_paste_burst=true -c check_for_update_on_startup=false`

| Mark | Seconds |
|---|---:|
| splash `OpenAI Codex (v0.160.0)` | 0.325 |
| `Ask Codex to do anything` | 0.330 |
| `GPT-6-Astra xhigh` footer | 3.545 |

Ctrl-C went out 1.5s after the footer. The child exited 0. The footer still ends with `1 warning`. f2 was not sent. No rollout file was created. Codex recreated `logs_2.sqlite` (49,152 plus a 115,392 wal) and `state_5.sqlite` (4,096 plus a 1,989,992 wal). `thread_history_1.sqlite` was not recreated in this short run. `du -sb /state/codex` after the probe was 118,666,002. `auth.json` was still inode 134064, 3,880 bytes, mtime unchanged.
