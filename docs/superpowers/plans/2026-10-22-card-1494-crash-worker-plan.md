# CARD-1494: crash-worker harness fix (plan)

Code task 5db5df66. Source: Debug 50c720df,
[docs/investigations/2026-10-22-card-1494-crash-worker-flake.md](../../investigations/2026-10-22-card-1494-crash-worker-flake.md)
section 4. Test-only; no product change.

## Problem

- The crash-worker root is cwd-relative, so it lands in `tests/Antiphon.Tests/bin-<x>/.antiphon/acceptance/card-0079/`, inside the output a checkpoint run seals. Nothing deletes it.
- A failed wait skips `stop` and the drain. The orphaned child keeps writing into the sealed output after the host exits, and cleanup refuses the receipt (`cleanup_refused`).
- The Legacy cases poll for the prompt in a fixed 25 s window that starts at spawn, so it also has to cover the child host's 8-26 s start under load. Whichever case meets the slowest stretch fails.

## Slices

- **S1, red witnesses first.** A behaviour-preserving seam: `CreateRoot()` returns a `CrashRoot` and `Start()` returns a `CrashWorker`. Both are `IAsyncDisposable`, and today disposal does nothing. Call sites take them with `await using`.
  - `CheckCompactionFixtureTests.CheckCompactionFixture_root_is_outside_the_build_output` (Unit): the root is not under `AppContext.BaseDirectory` or the working directory, and it is gone after disposal.
  - `CheckCompactionWorkerLifetimeTests` (Integration, Slow, `ProcessSpawnLimit`): a running `scan` child (`Crash_worker_is_killed_when_the_wait_fails`) and a held `sweep` child that never reads `stop` (`Held_crash_worker_is_killed_when_the_wait_fails`). Each test throws inside `await using` and asserts the child is gone within 10 s.
  - The new Slow class gets a `slow-tests-allowlist.txt` row.
- **S2, the fix.**
  - `CreateRoot()` makes `Directory.CreateTempSubdirectory("antiphon-c79-crash-")`. The request carries `OwnedRoot`, the owned temp prefix. The child refuses unless that prefix is its own, the root is one directory directly under it, and `owner` exists. There is no cwd fallback. The child's MTP `--results-directory` is the root.
  - `CrashRoot.DisposeAsync` deletes the root. `ANTIPHON_C79_KEEP_CRASH_ROOT=1` keeps it and logs the path.
  - `CrashWorker` captures stdout and stderr as they arrive. `DisposeAsync`/`StopAsync` write `stop`, wait up to 10 s, then kill the process tree and drain it. `DrainAsync` is the committed-cut kill.
  - Condition waits replace the fixed windows. Readiness is the first `scans.txt` line, or `held-<pid>`, with a 90 s bound. The prompt has a 60 s bound. Every wait fails at once if the child exits, and its message carries the scan count and stderr. On a prompt timeout the H5 cases add the notification state, `NextAttemptAt`, enqueue attempts, `SessionQueuedMessages` status and attempts, and the Legacy publication state.
  - `WaitForHeldAsync` (per worker, and the static count form) throws on timeout and has a 90 s bound.
  - The child budget rises from 90 s to 180 s, above ready plus prompt (150 s). The exit wait is bounded at 270 s.

## Verification

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Repeat |
|---|---|---|---|---|---|---|---:|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1494/` | fixture-witnesses | `/*/*/(CheckCompactionFixtureTests*)\|(CheckCompactionWorkerLifetimeTests*)/*` | S1 witnesses, red at S1, green at S2 | all pass, 0 failed | 3 | 8 | |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1494-rep/` | legacy-crash-repeat | `/*/*/CheckNoteDeliveryHandoffTests/(Legacy_produced*)\|(Legacy_captured*)\|(Legacy_note_recovery*)` | the four Legacy crash-worker cases, run with `--repeat 5` and qualified with `--expected-repeat 5` | 20/20, 0 failed in every ordinal, receipt valid (no `cleanup_refused`) | 4 | 30 | 5 |
| CP-3 | S2 | CP-1 | crash-tests | `/*/*/CheckCompactionCrashTests/*` | the shared-fixture class | all pass, 0 failed | 9 | 10 | |

Run S2 as one checkpoint-tool run (`--after S2`) and wait until the exit is not 75. CP-2 has its own output because each repeat N needs one (testing guide, Repeat proof). Validate the run with `scripts/validate-checkpoint-receipt.ps1`. It must print `CHECKPOINT SOURCE VALID`, with the crash-worker cases included and not excluded.

S1 red evidence comes from one narrow build-slot run (`bin-c1494r/`, filter CP-1), not from the tool. A red row cannot produce a valid receipt.

Repeat budget: CP-2's five repetitions are the brief's named acceptance. That is 3 normal plus 2 loaded under `repeat-proof`, and the host load is recorded. Nothing else repeats.

### Cost

Two `Antiphon.Tests` builds, one fixture row, one repeated subset row and one class row: about 40 minutes on server2. Anything beyond this table needs a stated reason. There is no whole-suite run.

### rc

All four test files were byte-identical on `origin/rc/2026-10-20` at the investigation. Each commit is checked with a per-commit `git merge-tree --merge-base` onto rc.
