# Windows session-runner all-group-2: C1817 and C1818 failures

Run `6591e378` on `rc/2026-11-15` (`ac6d00022e940ee3b90584c7d313567b4491c498`), desktop `DESKTOP-KTLKPIF`. Chunk `session-runner all-group-2` finished failed. This file is a read of that chunk only. The qualification was not touched.

## Sources

| Item | Value |
|---|---|
| TRX | `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378\session-runner-all-group-2.trx` |
| Console | `...\session-runner-all-group-2-tests.log` |
| Diag | `...\session-runner-all-group-2-execution\log_261110145447142.diag` |
| Timing | `...\session-runner-all-group-2.timing.json` |
| TestRun id | `8c24d1ab-e11a-4d0b-ae4b-7bf1dbd18d41` |
| TRX window | start `2026-11-10T14:54:47.3254501Z`, finish `2026-11-10T15:00:43.7587333Z` |
| Counters | total 438, executed 438, passed 426, failed 12, skipped 0 |
| Wall | 5m 56s 508ms (timing `runWallSeconds` 358.244) |
| Binary | `C:\Antiphon\releases\checkout-4dd2a426\tests\Antiphon.SessionRunner.Tests\bin\Debug\net9.0\Antiphon.SessionRunner.Tests.dll` |
| Host | TUnit 1.44.0, win-x64, .NET 9.0.22, Windows 10.0.19045 |
| Invocation | `C487_INVOCATION=6591e378c04d47bc898cf0927352b01b` |

Both classes ran as one sequence from `14:58:04.090Z` to `14:58:31.779Z`. No C1817 method overlapped another C1817 or C1818 method. Both classes carry `[ParallelLimiter<ProcessSpawnLimit>]`. The only in-flight siblings were `CodexTranscriptTailerTests`, and every one of those passed.

TRX `StdErr` is empty on all 12. `tests.log` and the diag file repeat the same exception text and contain no git or pwsh stderr for these tests. The fixture `Git()` helper throws `InvalidOperationException` with the child's stderr when `git` exits non-zero. That exception is not the recorded failure, so init and commit succeeded.

## Grouped causes

Two causes. Eight of the twelve record only the second, which is thrown from `finally` and replaces any exception from the test body.

### Cause 1. Linux process paths (4 tests, this exception is the body)

The fixtures start `/bin/sleep` and `/bin/true`. Those paths are absent on this Windows host. `Process.Start` throws, and `RunnerRepositoryMaintenance.GcAsync` maps `Win32Exception` to outcome `failed`, reason `git` (`RunnerRepositoryMaintenance.cs` lines 249-254 and 279-281 in checkout `4dd2a426`). `Hold` calls `entered.TrySetResult()` only after `/bin/sleep` starts (`C1817RunnerRepositoryMaintenanceTests.cs` lines 428-440), so a failed start leaves `WaitAsync(TimeSpan.FromSeconds(10))` to time out. These four use `Repo(..., 0)`, which creates no loose object, so `Delete` then succeeds and the body exception is the one in the TRX.

| Test | Duration | Recorded result |
|---|---|---|
| `C1817_gc_budget_kills_the_child` | 223 ms | `report.Reason` expected `budget`, actual `git` |
| `C1817_gc_child_strips_inherited_git_config` | 180 ms | `Outcome` expected `ran`, actual `failed` (the env-strip assertions above that line did not fail) |
| `C1817_skipped_while_worktree_or_land_in_flight` | 10.183 s | `TimeoutException` at the 10 s `WaitAsync` |
| `C1817_skipped_while_task_git_in_flight` | 10.169 s | `TimeoutException` at the 10 s `WaitAsync` |

### Cause 2. `Delete` does not handle Windows read-only git files (8 tests)

`Delete` calls `Directory.Delete(path, recursive: true)` and catches only `IOException`, then clears the read-only bit and retries (`C1817RunnerRepositoryMaintenanceTests.cs` lines 506-517, `C1818GcOwnerRobustnessTests.cs` lines 429-440). On Windows, `Directory.Delete` throws `UnauthorizedAccessException` for a read-only file. Git for Windows writes loose objects, and `git gc` writes `objects/info/commit-graph`, as read-only files. The attribute-clearing fallback never runs.

The denied path is the file name only: a 38-hex loose-object name, or `commit-graph`. The same 38-hex name on two tests is the same commit content (fixed author, message, and file body) landing in the same UTC second, in two different temp directories. It is not one shared file.

A `finally` exception replaces a body exception, so these eight TRX rows do not prove the body passed.

| Test | Duration | Denied path | Body, from the source |
|---|---|---|---|
| `C1817_gc_pass_reduces_loose_objects` | 3.364 s | `commit-graph` | Real `git gc` (the denied name is the gc output). Also calls `Directory.CreateSymbolicLink`. No assertion text was recorded. |
| `C1817_skipped_while_lease_held` | 278 ms | `4dde070c607382e53f749f4b220511c93de63b` | In-process lease skip after `Repo(..., 1)`. No `/bin` path. |
| `C1817_skipped_while_pool_repository_lock_held` | 302 ms | `4dde070c607382e53f749f4b220511c93de63b` | In-process pool lock after `Repo(..., 1)`. No `/bin` path. |
| `C1817_invalid_count_read_still_answers` | 375 ms | `c64420a6d6cdb6a77f175d80c410b7904efc9b` | Real `git rev-parse` after `Repo(..., 1)`. No `/bin` path. |
| `C1818_one_repository_fault_does_not_abort_the_pass` | 568 ms | `708421b4bdde8bf5a891d441600db79ecc9d39` | Also starts `/bin/true` for the good repo. A start failure would be hidden by this `Delete`. |
| `C1818_budget_kill_reports_a_lock_the_gc_child_wrote` | 486 ms | `18eab2c2c5f5e35a9c9a20e898fca04f70affc` | `StartShell` is `/bin/sh -c`. A start failure would be hidden by this `Delete`. |
| `C1818_budget_kill_leaves_a_lock_held_by_an_outside_process` | 246 ms | `18eab2c2c5f5e35a9c9a20e898fca04f70affc` | `/bin/sh` holder plus `/proc/<pid>/fd` and `stat -c`. A start failure would be hidden by this `Delete`. |
| `C1818_symlinked_git_directory_is_not_a_repository` | 298 ms | `18eab2c2c5f5e35a9c9a20e898fca04f70affc` | `File.CreateSymbolicLink` of `.git`. The recorded exception is the loose object, not the link. A privilege failure would be hidden by this `Delete`. |

Not this run: a packed-refs.lock sharing violation, a CRLF assertion, a case-fold collision, or a pwsh stderr line. The C1818 lock witnesses never reached their assertions in this log.

## Per-test extract

Stack paths are `C:\Antiphon\releases\checkout-4dd2a426\tests\Antiphon.SessionRunner.Tests\`. Times are UTC. "In flight" lists other tests whose TRX interval overlaps, all `Passed`.

### C1817_gc_budget_kills_the_child

- Class `C1817RunnerRepositoryMaintenanceTests`. Duration `00:00:00.2230437`. `14:58:04.0903348Z` to `14:58:04.3133785Z`.
- Message: `ShouldAssertException: report.Reason should be "budget" but was "git"`.
- Expected `budget` (codes 98, 117, 100, 103, 101, 116). Actual `git` (codes 103, 105, 116).
- Top frames: test line 97; then `ExceptionDispatchInfo.Throw` / `TaskAwaiter.ThrowForNonSuccess`. TUnit internals omitted.
- In flight: `CodexTranscriptTailerTests` `A_lazily_created_rollout_is_picked_up_when_it_finally_appears`, `A_rollout_claimed_by_another_live_session_is_refused_even_when_it_qualifies`, `Child_exit_with_input_delivered_before_restart_faults_from_the_sidecar`, `Input_delivered_before_a_restart_is_remembered_from_the_sidecar`, `First_input_starts_the_refusal_clock_from_the_input_not_the_child_start`, `Stale_same_cwd_rollouts_with_no_input_delivered_stay_silent_indefinitely`, `Refusal_detail_breaks_equal_write_times_by_path`, `Post_start_stranger_rollout_reports_AdoptionRefused_before_stale_C3_candidates`.

### C1817_gc_child_strips_inherited_git_config

- Duration `00:00:00.1797979`. `14:58:04.3389505Z` to `14:58:04.5187484Z`.
- Message: `ShouldAssertException: status.Repositories.Single().Outcome should be "ran" but was "failed"`.
- Expected `ran` (114, 97, 110). Actual `failed` (102, 97, 105, 108, 101, 100).
- Top frame: test line 140. The assertions at lines 134-139 (stripped `GIT_CONFIG_COUNT` and `GIT_CONFIG_KEY_0`, `GIT_TERMINAL_PROMPT=0`, working directory, argument list) are above the failing line.
- In flight: the eight named above, plus `A_rollout_that_predates_the_child_is_refused_even_when_the_prompt_matches`, `A_rollout_from_another_cwd_is_not_a_candidate_and_raises_the_empty_census_fault`, `A_strangers_rollout_in_the_same_cwd_is_never_adopted`.

### C1817_gc_pass_reduces_loose_objects

- Duration `00:00:03.3642130`. `14:58:04.5190923Z` to `14:58:07.8833053Z`.
- Message: `UnauthorizedAccessException: Access to the path 'commit-graph' is denied.`
- No expected/actual pair.
- Top frames: `System.IO.FileSystem.RemoveDirectoryRecursive`; `FileSystem.RemoveDirectory`; `Delete` line 510; test line 68 (`Delete(root)` in `finally`).
- In flight: the tailer tests still open from the previous window, plus `A_newline_joined_codex_prompt_binds_and_keeps_its_complete_receipt`, `Sidecar_path_is_retailed_directly_after_restart_with_no_discovery`, `A_rollout_held_open_by_the_writer_is_still_read`, `Child_exit_with_delivered_input_and_no_rollout_faults_and_without_input_stays_silent` (through `14:58:09.723Z`), `a_token_count_heartbeat_is_on_the_snapshot_and_not_a_transcript_entry`, `ResolveSessionsRoot_prefers_the_launch_environment_then_falls_back`. Sixteen overlaps, all passed.

### C1817_skipped_while_lease_held

- Duration `00:00:00.2779991`. `14:58:07.8844656Z` to `14:58:08.1624647Z`.
- Message: `UnauthorizedAccessException: Access to the path '4dde070c607382e53f749f4b220511c93de63b' is denied.`
- Top frames: `RemoveDirectoryRecursive`; `RemoveDirectory`; `Delete` line 510; test line 170.
- In flight: `Child_exit_with_delivered_input_and_no_rollout_faults_and_without_input_stays_silent`.

### C1817_skipped_while_pool_repository_lock_held

- Duration `00:00:00.3021466`. `14:58:08.1630990Z` to `14:58:08.4652456Z`.
- Message: `UnauthorizedAccessException: Access to the path '4dde070c607382e53f749f4b220511c93de63b' is denied.`
- Top frames: `RemoveDirectoryRecursive`; `RemoveDirectory`; `Delete` line 510; test line 197.
- In flight: the same tailer test.

### C1817_skipped_while_task_git_in_flight

- Duration `00:00:10.1688741`. `14:58:08.4657110Z` to `14:58:18.6345851Z`.
- Message: `TimeoutException: The operation has timed out.`
- The test would have expected outcome `skipped`, reason `git_call`. It failed at `WaitAsync` line 254, before that assertion.
- Top frame: test line 254.
- In flight: the same tailer test until `14:58:09.723Z`, then nothing.

### C1817_invalid_count_read_still_answers

- Duration `00:00:00.3745550`. `14:58:19.5166529Z` to `14:58:19.8912079Z`.
- Message: `UnauthorizedAccessException: Access to the path 'c64420a6d6cdb6a77f175d80c410b7904efc9b' is denied.`
- Top frames: `RemoveDirectoryRecursive`; `RemoveDirectory`; `Delete` line 510; test line 291.
- In flight: none.

### C1817_skipped_while_worktree_or_land_in_flight

- Duration `00:00:10.1826158`. `14:58:19.8922657Z` to `14:58:30.0748815Z`.
- Message: `TimeoutException: The operation has timed out.`
- The test would have expected outcome `skipped`, reason `worktree_or_land`. It failed at `WaitAsync` line 219.
- Top frame: test line 219.
- In flight: none.

### C1818_one_repository_fault_does_not_abort_the_pass

- Class `C1818GcOwnerRobustnessTests`. Duration `00:00:00.5681566`. `14:58:30.0760941Z` to `14:58:30.6442507Z`.
- Message: `UnauthorizedAccessException: Access to the path '708421b4bdde8bf5a891d441600db79ecc9d39' is denied.`
- Top frames: `RemoveDirectoryRecursive`; `RemoveDirectory`; `Delete` line 433; test line 49.
- In flight: none.

### C1818_budget_kill_reports_a_lock_the_gc_child_wrote

- Duration `00:00:00.4856952`. `14:58:30.6481677Z` to `14:58:31.1338629Z`.
- Message: `UnauthorizedAccessException: Access to the path '18eab2c2c5f5e35a9c9a20e898fca04f70affc' is denied.`
- Top frames: `RemoveDirectoryRecursive`; `RemoveDirectory`; `Delete` line 433; test line 145.
- In flight: none.

### C1818_budget_kill_leaves_a_lock_held_by_an_outside_process

- Duration `00:00:00.2456712`. `14:58:31.1343606Z` to `14:58:31.3800318Z`.
- Message: `UnauthorizedAccessException: Access to the path '18eab2c2c5f5e35a9c9a20e898fca04f70affc' is denied.`
- Top frames: `RemoveDirectoryRecursive`; `RemoveDirectory`; `Delete` line 433; test line 200.
- In flight: none.

### C1818_symlinked_git_directory_is_not_a_repository

- Duration `00:00:00.2983867`. `14:58:31.3807139Z` to `14:58:31.6791006Z`.
- Message: `UnauthorizedAccessException: Access to the path '18eab2c2c5f5e35a9c9a20e898fca04f70affc' is denied.`
- Top frames: `RemoveDirectoryRecursive`; `RemoveDirectory`; `Delete` line 433; test line 222.
- In flight: none.

## Passing siblings

These bound the cause. Every passing C1817 that touches a repo uses `Repo(..., 0)` (no commit, no read-only loose object) or no repo. Every passing C1818 avoids `Repo()` and `/bin`.

### C1817RunnerRepositoryMaintenanceTests (6 passed, 8 failed, 14 total)

| Passed | Duration | Why it stays green here |
|---|---|---|
| `C1817_status_omits_a_null_maintenance_result` | 18 ms | JSON only. No repo. |
| `C1817_disabled_service_does_not_tick` | 328 ms | `Repo(..., 0)`. No gc child. `Delete` of an empty repo succeeds. |
| `C1817_lease_skip_does_not_start_the_week` | 197 ms | Same empty repo. Lease skip itself answers `skipped` / `lease`. |
| `C1817_first_pass_below_threshold_arms_the_week_without_gc` | 177 ms | Same empty repo. Outcome `armed` / `week_started` with no gc. |
| `C1817_due_when_loose_objects_exceed_threshold_or_the_week_elapsed` | 0.4 ms | Pure `IsDue`. |
| `C1817_invalid_count_spawn_is_named` | 177 ms | `Repo(..., 0)`. The named `git_config_count_invalid` refusal passes. Its commit-creating sibling fails in `Delete`. |

### C1818GcOwnerRobustnessTests (6 passed, 4 failed, 10 total)

| Passed | Duration | Why it stays green here |
|---|---|---|
| `C1818_skip_docs_name_the_counted_git_calls` | 3 ms | Reads doc text. |
| `C1818_park_workspace_shares_repository_activity` | 4 ms | Runtime wiring. No `Repo()`. |
| `C1818_retention_workspace_binds_repository_activity` | 2 ms | Source text. |
| `C1818_dispatcher_workspace_shares_repository_activity` | 1 ms | In-memory dispatcher. No git. |
| `C1818_runner_maps_repository_maintenance` | 0.7 ms | Source text. |
| `C1818_maintenance_owner_retries_on_the_next_tick` | 89 ms | Owner retry on an empty directory. No `Repo()`, no `/bin`. The retry itself passed. |

## Linux reviews

The Windows run matches the advisories those reviews left open. Linux was green.

### CARD-1817 round 2, task `a38b6048`

Commit `b04f5070a82150b08119313a447698a0848b8df9`, report `.antiphon/task-a38b6048.md`. Subject tip `df669d16029a6a91e46ed07adcc0a4ea543f2311`. Verdict CLEAN.

- CP-1 at that tip: `C1817*` / `C1441*` 20/20.
- CP-M2 on the scratch merge: the same 20/20.
- The report does not name the host OS. That green set includes the four `/bin` cases that fail on this Windows run.
- Advisories A-1 through A-5 do not mention a Windows `Directory.Delete` failure. A-4 is the stale `packed-refs.lock` product gap, later CARD-1818.

### CARD-1818 round 1, task `211c38d3`

Commit `5fb7796f575a569170de7094b837ca207e17183d` on `origin/feat/card-task-211c38d3`, report `.antiphon/task-211c38d3.md`. Subject tip `a6cdc4101fcaec7db97f217342c038ecd12e127d`. One blocker, D-1 (lock deletion without ownership). Linux server2 evidence at that tip: `C1818GcOwnerRobustness` 9 passed, `C1817` 14 passed, filter 78/78, 0 skipped.

Advisory A-1, not a land hold: `C1818GcOwnerRobustnessTests` uses `/bin/true`, `/bin/sleep`, and `File.CreateSymbolicLink` with no OS gate. On Windows, `fault` and `budget_kill` fail at `Process.Start`, and the symlink case needs the symlink privilege. The same pattern already exists in `C1817RunnerRepositoryMaintenanceTests`. Neither class has an exclusion row. It needs a runtime skip and an rc exclusion.

### CARD-1818 round 2, task `5b3c6c26`

Commit `fe5961ca43f71147fe427a7abb487f5aaa46ae92` on `origin/feat/card-task-5b3c6c26`, report `.antiphon/task-5b3c6c26.md`. Subject tip `a8e4325b23c34b08f81ad10cb2b7172195a6d775`. Verdict CLEAN. D-1 closed: the pass reports `packed_refs_lock_left` and does not delete the lock. Linux server2: `C1818` 10 passed, `C1817` 14 passed, filter 34/34, 0 skipped. Red commit `3b6063908` failed exactly three C1818 tests for the old lock-deletion behavior, on Linux.

Advisory A-5: the new witnesses add `/bin/sh`, `stat -c`, and `/proc/<pid>/fd`. Still no runtime OS skip and no rc Windows exclusion row. Round 1 A-1 still stands.

The os-witness JSON on this rc (`docs/superpowers/evidence/2026-11-10-card-1818-os-witnesses.json` and `...-repair.json`) marks `C1818GcOwnerRobustnessTests.cs` as `test-only` with an empty candidate list. A search of the qualification checkout finds neither class name on a Windows exclusion row.

## Why this rc run is the first red

Linux server2 ran both classes green, so the landed behavior looked done. The reviews wrote the Windows `/bin` and `/proc` gap down as advisories A-1 and A-5 and did not add a skip or an exclusion. The os-witness checker accepts the file as test-only. The Windows read-only `Delete` gap was not in those advisories, because Linux `Directory.Delete` unlinks a read-only object and the `IOException` fallback is never required there.

Skipping the whole class on Windows would hide both causes. The four in-process C1817 failures (`gc_pass`, `lease_held`, `pool`, `invalid_count_read`) have no `/bin` call. They stay red on Windows until `Delete` handles `UnauthorizedAccessException`, even if the `/bin` tests are gated. The four C1818 failures need both: a Windows `Delete`, and a replacement for `/bin/sh`, `/bin/true`, `/proc/<pid>/fd`, and `stat -c`, or an explicit skip of those methods.
