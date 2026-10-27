# E2E Linux baseline, shard 0 of 3

Server2 Linux baseline for `tests/Antiphon.E2E`. This is not the release gate.
The Windows rc profile remains the gate. Windows-only classes are recorded as
skip or fail with that cause. Product and test source were not changed.

## Base

- Ref: `origin/rc/2026-10-27`
- Tip: `9f00008facfc48e75555c08df59887019b3f8dfe` (fetched; worktree HEAD matches)
- Branch: `feat/card-task-cdc0a3b5`
- Host: server2 Linux (Debian 12 container, Docker 27.5.1, libglib-2.0 present)
- Shard rule: every class from `TUnit --list-tests` (no filter), ordinal sort,
  number from 0, keep `index % 3 == 0`
- Per-class cap: 25 minutes wall, then the process is stopped and the class is
  recorded as timeout
- Task budget: 120 minutes
- Isolated output: `bin-e2elinux0/` (forward slash). Removed at the end.
- Runner: the suite's own `IsolatedSessionRunner` on a random loopback port.
  Production runner 17204 and the live server are not used.

## Status

Client bundle and the E2E build have not finished. The class table below is
filled as each class returns.

## Classes

| # | class | executed | passed | failed | skipped | wall | notes |
|---|-------|----------|--------|--------|---------|------|-------|

## Failures

None recorded yet.

## Tests over 30 seconds

None recorded yet.
