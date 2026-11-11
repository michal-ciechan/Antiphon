# Run d88de419 session-runner group 2: C1817 and C1818 still red on Windows

Read-only extract of the native Windows rerun. Qualification run `d88de419` was still running. Nothing in that run, AppHost, the production runner, or Docker was changed.

| Item | Value |
|---|---|
| Run | `d88de419` (`C487_INVOCATION=d88de419025b4d368fb75108e4de0c36`) |
| Host | desktop `DESKTOP-KTLKPIF`, Windows 10.0.19045, win-x64, .NET 9.0.22 |
| Cut | `rc/2026-11-16` = `6f8741d872d63507827014f972ccf71196722d4a` |
| Checkout under test | `C:\Antiphon\releases\checkout` |
| Chunk | `session-runner-all-group-2` |
| Wall | `2026-11-11T11:56:00.6070416Z` to `2026-11-11T12:02:38.7666629Z` (`runWallSeconds` 397.342) |
| Engine | TUnit 1.44.0.0, Microsoft Testing Platform 2.2.2 |
| Summary | total 463, failed 9, succeeded 454, skipped 0, duration 6m 35s 131ms |

Sources, name-filtered, under `C:\Antiphon\releases\candidates\rc-2026-11-16-6f8741d872d6\logs\2026-11-11-1142-d88de419\`:

- `session-runner-all-group-2.trx` (719654 bytes)
- `session-runner-all-group-2-tests.log` (the console log)
- `session-runner-all-group-2.timing.json`
- `session-runner-all-group-2-execution\log_261111115600894.diag`

No other `session-runner-all-group-2*` file is in that directory. The TRX `Output` for every failure contains `ErrorInfo` only. There is no `StdOut` and no `StdErr`. The console log and the diag repeat the same exception and add no nearby log line.

## Verdict

Deterministic cleanup failure. All nine tests throw `UnauthorizedAccessException` from `Directory.Delete` in a `finally`. None is a returned-state assertion, a poll expiry, a `TimeoutException`, or a sharing-violation message (`being used by another process` does not appear). Durations are 0.524s to 4.129s against method timeouts of 15s, 30s, or 60s.

`a4ea62dd3bfaa20b20ab8609c9352825b96c9974` (`fix(tests): make the runner gc fixtures run on Windows`, ancestor of this cut) fixed a real subset and left this subset. It is not a new git-config, line-ending, or ACL difference from run 6591e378. The seven `UnauthorizedAccessException` rows in that run's final-result note are the same failure, and a fifth C1817 method added by the fix fails the same way.

## Cause

Both helpers catch only `IOException`, then clear the read-only bit and retry:

- `C1817RunnerRepositoryMaintenanceTests.Delete`, lines 536-547, introduced by `d18bf2e00812a6854744722857176f1ff7eef274` (CARD-1817). The throw site on this cut is line 540.
- `C1818GcOwnerRobustnessTests.Delete`, lines 394-405, introduced by `58454a7a709431c8e974f17328dc75e68a41623f` (CARD-1818). The throw site on this cut is line 398.

`a4ea62dd3` does not edit either `Delete`. On Windows, `System.IO.FileSystem.RemoveDirectoryRecursive` throws `UnauthorizedAccessException` when it reaches a read-only file, and the message names the `WIN32_FIND_DATA` entry (`88846b6c...`, `commit-graph`), not the full path. The `catch (IOException)` never runs, so `File.SetAttributes(..., FileAttributes.Normal)` never runs.

Git for Windows marks loose objects and the post-gc `commit-graph` file read-only. The pass/fail split of this chunk is exactly that:

- Every method whose fixture commits at least once, or runs `git gc` and writes `commit-graph`, fails in `Delete`.
- Every sibling that calls the same `Delete` on a repo with zero commits, or that creates no repository, passes.

The four C1818 failures name one object id, `708421b4bdde8bf5a891d441600db79ecc9d39`. `C1818GcOwnerRobustnessTests.Repo` always commits the same bytes (`f.txt` = `body`, message `c`, author `c1818`). Each test uses its own `c1818-<name>-<guid>` temp directory. The shared name is content-addressed, not one locked file shared across tests.

The same test project already clears attributes before delete and catches both `IOException` and `UnauthorizedAccessException` (`RunnerInProcessGitTests.Root.Dispose`, `C0824SlotScratch.Dispose`). These two helpers do not.

A body assertion that threw inside `try` would be masked if `finally` also threw. The reported stack is only `Delete`. What bounds it to cleanup rather than a masked assertion:

- `C1817_gc_pass_reduces_loose_objects` names `commit-graph`. That entry is written by `git gc`. A failure before gc would name a loose object, as the one-commit tests do.
- The four tests whose 6591e378 messages were assertions or `TimeoutException` now pass on this cut. The seven whose 6591e378 message was already `UnauthorizedAccessException` are still that exception, including the identical `commit-graph` text.
- Zero-commit siblings exercise the same `Delete` and pass.

## Each failure

Stack top is the same three frames in every row: `FileSystem.RemoveDirectoryRecursive`, `FileSystem.RemoveDirectory`, then `Delete`. The next frame is the test method. No expected/actual pair exists. The test was expected to finish. The actual result is the exception below.

### C1817RunnerRepositoryMaintenanceTests (5 failed, 11 passed)

`C1817_skipped_while_lease_held`. Duration `00:00:00.6129155`. Start `2026-11-11T11:59:53.5483968+00:00`, end `2026-11-11T11:59:54.1613123+00:00`. Message: `UnauthorizedAccessException: Access to the path '88846b6c0be1529f8f57a5bf0ddfc8d26e99f1' is denied.` Test frame: line 160, the `finally` that calls `Delete(root)`. Fixture: `Repo(root, "antiphon", 1)`. Same message class as 6591e378 (`UnauthorizedAccessException` deleting a git object path, start `2026-11-10T14:58:07.8844656+00:00`). Overlapped 15 other tests, no C1817 or C1818 sibling. The overlapping tests all passed. They are rollout, sidecar, and child-exit tests (`A_rollout_that_predates_the_child_is_refused_even_when_the_prompt_matches` through `Child_exit_with_delivered_input_and_no_rollout_faults_and_without_input_stays_silent`).

`C1817_skipped_while_pool_repository_lock_held`. Duration `00:00:00.6047180`. Start `2026-11-11T11:59:54.1719069+00:00`, end `2026-11-11T11:59:54.7766249+00:00`. Message names the same loose object `88846b6c0be1529f8f57a5bf0ddfc8d26e99f1`. Test frame: line 187, `finally`. Fixture: `Repo(..., 1)`. Same as 6591e378 (start `2026-11-10T14:58:08.1630990+00:00`). Overlapped 6 passed tests, no sibling: `Stale_same_cwd_rollouts_with_no_input_delivered_stay_silent_indefinitely`, `First_input_starts_the_refusal_clock_from_the_input_not_the_child_start`, `A_lazily_created_rollout_is_picked_up_when_it_finally_appears`, `A_rollout_claimed_by_another_live_session_is_refused_even_when_it_qualifies`, and the two `Child_exit_*` tests.

`C1817_commit_fixture_pins_identity_and_disables_signing`. Duration `00:00:00.5673817`. Start `2026-11-11T11:59:54.7782680+00:00`, end `2026-11-11T11:59:55.3456497+00:00`. Message: `UnauthorizedAccessException: Access to the path '024848831573a45ea9522ca7f414ce908ec6ec' is denied.` Frames: line 434 (`finally`) and line 423 (method). Fixture: `Repo(..., 1)`, then reads `.git/config` for `name`, `email`, `gpgsign`, and `hooksPath = NUL`. This method was added by `a4ea62dd3`. It is not in the 6591e378 twelve. Overlapped 3 passed tests, no sibling: `A_rollout_claimed_by_another_live_session_is_refused_even_when_it_qualifies` and the two `Child_exit_*` tests.

`C1817_invalid_count_read_still_answers`. Duration `00:00:00.7347836`. Start `2026-11-11T11:59:56.8527726+00:00`, end `2026-11-11T11:59:57.5875562+00:00`. Message: `UnauthorizedAccessException: Access to the path 'c64420a6d6cdb6a77f175d80c410b7904efc9b' is denied.` Test frame: line 281, `finally`. Fixture: `Repo(..., 1)`. Same as 6591e378 (start `2026-11-10T14:58:19.5166529+00:00`). Overlapped 1 passed test, no sibling: `Child_exit_with_delivered_input_and_no_rollout_faults_and_without_input_stays_silent`.

`C1817_gc_pass_reduces_loose_objects`. Duration `00:00:04.1293960`. Start `2026-11-11T12:00:00.1061857+00:00`, end `2026-11-11T12:00:04.2355817+00:00`. Message: `UnauthorizedAccessException: Access to the path 'commit-graph' is denied.` Test frame: line 68, `Delete(root)` in `finally` after `Unlink`. Fixture: `Repo(..., 15)` plus a real maintenance pass whose arguments are `gc --prune=2.weeks.ago`. The 6591e378 one-line error is the same sentence, start `2026-11-10T14:58:04.5190923+00:00`. Overlapped 0 tests.

Passed siblings of this class:

- `C1817_skipped_while_worktree_or_land_in_flight` (0.622s). `Repo(..., 0)`. Was `TimeoutException` on 6591e378. Now passed.
- `C1817_forced_windows_fixture_uses_no_posix_tool` (0.002s). No repository. Added by the fix. Passed.
- `C1817_disabled_service_does_not_tick` (0.436s). `Repo(..., 0)`. Passed.
- `C1817_lease_skip_does_not_start_the_week` (0.354s). `Repo(..., 0)`. Passed.
- `C1817_first_pass_below_threshold_arms_the_week_without_gc` (0.338s). `Repo(..., 0)`. Passed.
- `C1817_due_when_loose_objects_exceed_threshold_or_the_week_elapsed` (0.000s). No repository. Passed.
- `C1817_invalid_count_spawn_is_named` (0.372s). `Repo(..., 0)`. Passed.
- `C1817_skipped_while_task_git_in_flight` (0.522s). `Repo(..., 0)`. Was `TimeoutException` on 6591e378. Now passed.
- `C1817_gc_child_strips_inherited_git_config` (0.446s). `Repo(..., 0)`. Was `Outcome` `ran` but was `failed` on 6591e378. Now passed.
- `C1817_status_omits_a_null_maintenance_result` (0.017s). No repository. Passed.
- `C1817_gc_budget_kills_the_child` (1.531s). `Repo(..., 0)`. Was `report.Reason` `budget` but was `git` on 6591e378. Now passed.

### C1818GcOwnerRobustnessTests (4 failed, 6 passed)

All four failures name `708421b4bdde8bf5a891d441600db79ecc9d39` and overlap zero other tests. Each was `UnauthorizedAccessException` deleting a git object path on 6591e378.

`C1818_one_repository_fault_does_not_abort_the_pass`. Duration `00:00:01.3893891`. Start `2026-11-11T12:00:04.2395596+00:00`, end `2026-11-11T12:00:05.6289487+00:00`. Test frame: line 44, `finally`. 6591e378 start `2026-11-10T14:58:30.0760941+00:00`.

`C1818_budget_kill_reports_a_lock_the_gc_child_wrote`. Duration `00:00:03.3103553`. Start `2026-11-11T12:00:05.6297774+00:00`, end `2026-11-11T12:00:08.9401327+00:00`. Test frame: line 141, `finally` after `Stop`. 6591e378 start `2026-11-10T14:58:30.6481677+00:00`.

`C1818_budget_kill_leaves_a_lock_held_by_an_outside_process`. Duration `00:00:01.6565998`. Start `2026-11-11T12:00:08.9409118+00:00`, end `2026-11-11T12:00:10.5975116+00:00`. Test frame: line 197, `finally`. 6591e378 start `2026-11-10T14:58:31.1343606+00:00`.

`C1818_symlinked_git_directory_is_not_a_repository`. Duration `00:00:00.5243443`. Start `2026-11-11T12:00:10.5986397+00:00`, end `2026-11-11T12:00:11.1229840+00:00`. Frames: line 219 (`finally`) and line 203 (method). The denied name is the loose object, not the `.git` link. 6591e378 start `2026-11-10T14:58:31.3807139+00:00`.

Passed siblings of this class. None of them commits a repository:

- `C1818_park_workspace_shares_repository_activity` (0.003s)
- `C1818_maintenance_owner_retries_on_the_next_tick` (0.069s). Empty temp directory, then the same `Delete`.
- `C1818_retention_workspace_binds_repository_activity` (0.001s)
- `C1818_dispatcher_workspace_shares_repository_activity` (0.001s)
- `C1818_runner_maps_repository_maintenance` (0.001s)
- `C1818_skip_docs_name_the_counted_git_calls` (0.006s)

## Comparison with run 6591e378

Final-result note: `origin/feat/card-task-d6579463` at `7a3c45c3f82e36c7b992d5b4b66316cd52127b47`, file `docs/investigations/2026-11-11-run-6591e378-final-result.md`, section "Already fixed on master `716cd8acf`". That note lists 8 C1817 and 4 C1818 failures and assigns every row to `a4ea62dd3`. The first why-missed note is `docs/investigations/2026-11-10-why-missed-c1817-c1818-windows.md` (`8f3656a4575eb76fdd7bb49a192d42b185182ec5`). It says the Windows TRX was not on the mirror and reconstructs POSIX tools (`/bin/sleep`, `/bin/true`, `/bin/sh`, `/proc`) plus a missing commit identity. It does not mention `Delete` or `UnauthorizedAccessException`.

| 6591e378 message | This rerun |
|---|---|
| `C1817_gc_budget_kills_the_child`: `Reason` should be `budget` but was `git` | Passed |
| `C1817_gc_child_strips_inherited_git_config`: `Outcome` should be `ran` but was `failed` | Passed |
| `C1817_skipped_while_task_git_in_flight`: `TimeoutException` | Passed |
| `C1817_skipped_while_worktree_or_land_in_flight`: `TimeoutException` | Passed |
| `C1817_gc_pass_reduces_loose_objects`: `UnauthorizedAccessException: Access to the path 'commit-graph' is denied.` | Same sentence |
| `C1817_skipped_while_lease_held`, `C1817_skipped_while_pool_repository_lock_held`, `C1817_invalid_count_read_still_answers`: `UnauthorizedAccessException` deleting a git object path | Same exception, loose-object entry name quoted above |
| Four C1818 methods: `UnauthorizedAccessException` deleting a git object path | Same exception, object `708421b4bdde8bf5a891d441600db79ecc9d39` |
| (not in the twelve) `C1817_commit_fixture_pins_identity_and_disables_signing` | New failure, same `Delete` exception |

`a4ea62dd3` changed `MaintenanceFixture` to `cmd.exe`, a powershell holder, a content hash, and a junction fallback, and it set `user.name`, `user.email`, `commit.gpgsign=false`, and `core.hooksPath=NUL` inside `Repo`. Those are why the four non-delete rows went green. The red witness for that fix, `15edc83cf014432236112949be34adadeb729a1f`, adds `C1817_forced_windows_fixture_uses_no_posix_tool` and the commit-fixture config assertions. It does not assert that `Delete` survives a read-only object. Linux `Directory.Delete` does not throw on the DOS read-only bit, so a Linux run of these classes stays green.

This is the second miss of the same chunk. The why-missed note for this cause is `docs/investigations/2026-11-11-why-missed-c1817-c1818-readonly-delete.md`.

## What Debug should change

Clear the read-only bit before `Directory.Delete`, and catch `UnauthorizedAccessException` as well as `IOException`, in both `Delete` helpers. Match `RunnerInProcessGitTests` and `C0824SlotScratch`. Do not widen timeouts, and do not treat this as a git identity or POSIX-tool defect. The pre-land check has to be a native Windows run of both classes, not only `C1817_forced_windows_fixture_uses_no_posix_tool`.
