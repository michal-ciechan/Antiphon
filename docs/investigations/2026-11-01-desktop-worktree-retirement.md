# Desktop worktree retirement, 2026-11-01 (task 224cca6b)

Operator authority: "Clean up all old worktrees, anything older than 24 hours if anything lost that
is not an issue". Scope: registered worktrees of `C:\src\Antiphon` under `C:\Antiphon\worktrees\`.

## Outcome

- 137 old worktrees removed with `git -c core.longpaths=true -C C:\src\Antiphon worktree remove --force '<literal path>'`
  (135 in six guarded batches of up to 25, plus 2 stale `locked initializing` trees with `--force --force`).
  Every removal exited 0 and its directory is gone.
- One old tree is left as an unregistered orphan directory:
  `C:\Antiphon\worktrees\card-task-a7b2b64a-rc` (about 1.6 GB, task a7b2b64a Succeeded). The first
  attempt, without long-path support, failed with `Filename too long` after git had already dropped
  the registration. A recursive delete was not attempted (approval-hook policy). Operator command:
  `! cmd /c rd /s /q "\\?\C:\Antiphon\worktrees\card-task-a7b2b64a-rc"`
- `git worktree prune -v` ran clean (nothing left to prune).

## Counts

| | Before | After |
|---|---|---|
| Registered worktrees (all) | 237 | 99 |
| Registered under `C:\Antiphon\worktrees` | 225 | 87 |
| `git worktree list` | 576 ms | 214-232 ms |
| C: free | 390.45 GB | 418.92 GB (+28.5 GB; net of concurrent host activity) |

Probe after cleanup: `git worktree add --detach C:\Users\lndco\AppData\Local\Temp\antiphon-wt-probe-2 8edda9996`
took 10.2 s; its `worktree remove` took 4.4 s; the probe directory is gone.

Kept (87 under the root):

| Reason | Count |
|---|---|
| Newer than 24 h (newest of dir, `.git` file, HEAD reflog; index also checked) | 85 (includes all 7 open desktop tasks: 250a07fb Blocked, af497073, 9851e64c, c7fb4007, 01a41548, 9c944aab, 49f0e291 Dispatched) |
| Own task worktree (224cca6b) | 1 |
| `land\0f9784a67773` (`locked antiphon-land`, 5.6 h old) | 1 |

Outside the root, untouched: canonical checkout, `C:\src\Antiphon-card0417`, `C:\src\rc-bundle-c-final`,
`C:\src\rc-c1525`, `C:\temp\antiphon-add64715-base`, and the `C:\Antiphon\evidence` / `C:\Antiphon\scratch`
registrations. Nothing under `C:\Antiphon\releases` or `C:\Antiphon\nightly` was touched. No branch,
ref, config, object or `.git\antiphon` change.

## Method and guards

- Sanctioned path checked first: `POST /api/agent-tasks/{id}/worktree-retirement` only records a
  release, and the `antiphon:worktree-residue` job retires released clean trees only with
  `WorktreeResidue:Execute=true`. It does not fit an accept-loss sweep, so the per-path git removal was used.
- Task states: read-only `GET /api/agent-tasks?since=<20 days>` (7181 rows, excluded 0); the open set
  was identical (18) with a 1-day window. All 137 removed trees belonged to terminal tasks
  (129 Succeeded, 5 Canceled, 3 Failed); the youngest was 24.3 h old.
- Each batch script re-checked before removing: path inside the root and not this task's worktree,
  no open task by path or id prefix, still older than 24 h, live-run heartbeat
  (`idleMinutes` <= 2, `hangSuspected` false) and free memory >= 3 GB. A 20 s pause separated batches.
  Live run c8a7bcee heartbeat stayed at `idleMinutes` 0.0-0.5, `hangSuspected` false, throughout.
- No builds or tests were run.

## Old build-output folders (listed, not deleted)

| Path | Last write | Size |
|---|---|---|
| `C:\Users\lndco\AppData\Local\Temp\antiphon-f6654e98-build-output` | 2026-10-31 05:31 | 0.3 MB |

## Evidence (local, not committed)

`C:\Users\lndco\AppData\Local\Temp\antiphon-worktree-audit\`: `retirement2-inventory.csv`,
`retirement2-candidates.csv`, `retire2-batch-1.ps1` .. `retire2-batch-6.ps1`, `retirement2-removals.jsonl`.
