# Windows failure: post-land early warning, group 130

Run `6591e378` on `rc/2026-11-15` (`ac6d00022e940ee3b90584c7d313567b4491c498`), desktop, chunk `antiphon-all-group-130`. The qualification was still running when this was read. Nothing in the run directory was modified.

Sources, name-filtered only:

- `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378\antiphon-all-group-130.trx` (438 results)
- `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378\antiphon-all-group-130-tests.log`
- `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378\antiphon-all-group-130.timing.json`
- `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378\antiphon-all-group-130-execution\log_261110165814058.diag`

Stack paths are `C:\Antiphon\releases\checkout-4dd2a426\tests\Antiphon.Tests\Scripts\PostLandEarlyWarningTests.cs`. Script under test: `scripts/post-land-early-warning.ps1` and `scripts/lib/post-land-early-warning.ps1` in that checkout.

## Chunk

| Field | Value |
|---|---|
| Invocation | `6591e378c04d47bc898cf0927352b01b` |
| TRX start | `2026-11-10T16:58:14.3294544Z` |
| TRX finish | `2026-11-10T17:26:58.4979908Z` |
| tests.log summary | Failed, total 438, failed 14, succeeded 424, skipped 0, duration 28m 44s 284ms |
| TRX counters | total 438, executed 438, passed 424, failed 14, timeout 0, aborted 0, error 0 |
| timing.json | `runWallSeconds` 1725.997, `buildSeconds` 655.862, `testCount` 438, `buildSlot` not-used |

All 14 failures are `Antiphon.Tests.Scripts.PostLandEarlyWarningTests`. Stderr is empty on every one. Stdout is only the `ANTIPHON_TEST_PHASE_ID` line. No poll expiry and no `sharing violation` text.

The class ran one after another from `17:12:59Z` to `17:14:53Z`. Same-class overlap is zero on every failure, which matches `[ParallelLimiter<ProcessSpawnLimit>]`. The only cross-class overlap is on the first failure (below).

## Cause

Two deterministic Windows failures. Neither is a timeout or a load flake.

1. **Teardown (11 tests).** `GitRepo.Create()` runs `git init` and an identical init commit (`README.md` = `fixture\n`, author `fixture`, date `2026-10-01T00:00:00Z`). Git for Windows writes loose objects with the ReadOnly attribute. `Fixture.Dispose` line 953 calls `Directory.Delete(Repo, recursive: true)` and does not clear that attribute. .NET 9 `FileSystem.RemoveDirectoryRecursive` calls `DeleteFile` and, on `ERROR_ACCESS_DENIED`, throws `UnauthorizedAccessException` with the **leaf name only** (`FileSystem.Windows.cs` passes `fileName`, not the combined path). The leaf is always `892e7e5ca82d5b42ef2fc9fb4e1d8494d0a9a7` (38 hex chars): `.git\objects\15\892e7e5ca82d5b42ef2fc9fb4e1d8494d0a9a7`, the init commit's tree (`100644 blob ee8c1ee49b4799bbd170233915a897c19e3b55e1 README.md`). A temp repo built the same way reproduced the exact exception message, and the file's attributes were `ReadOnly, Archive`.

2. **Lock and hourly gates (3 tests).** `ConvertFrom-Json` turns `startedAt` into a `DateTime`. The script then does `[string]$doc.startedAt`. On this host (`en-GB`) that cast uses the invariant culture (`11/10/2026` for 10 November) while `[datetime]::Parse` uses `en-GB` and reads it as 11 October. A stamp 30 minutes old becomes about 30 days old. `Test-PostLandHourly` returns false (`coalesceMinutes` is 60). `Test-PostLandPidReused` returns true for a live pid, so `Get-PostLandLockKind` returns `stale` and `Test-PostLandLockBusy` returns false. Checked locally against the checkout lib: a `-30` minute `last-start.json` made `Test-PostLandHourly` false, and a fresh lock whose pid was a live child pwsh was `alive=True`, `reused=True`, `kind=stale`, `busy=False`.

## Group A — repo delete, 11 tests

Same message, same stack top, empty stderr:

```
UnauthorizedAccessException: Access to the path '892e7e5ca82d5b42ef2fc9fb4e1d8494d0a9a7' is denied.
   at System.IO.FileSystem.RemoveDirectoryRecursive(...)
   at System.IO.FileSystem.RemoveDirectory(...)
   at Antiphon.Tests.Scripts.PostLandEarlyWarningTests.Fixture.Dispose() line 953
```

There is no expected/actual pair. The throw is the delete itself.

Eight of these finished their in-method assertions and then threw from `Dispose`. The three `Real_slot_*` tests throw inside `RunRealSlotAsync` at the `return` (line 653), so the caller's `ShouldBe` lines never ran. Those three bodies are not shown green.

| Test | Duration | Start–end (UTC) | Throw site | Overlap |
|---|---|---|---|---|
| `Second_run_refuses_while_the_lane_holds_the_lock` | `00:00:08.5086712` | `17:12:59.1403704`–`17:13:07.6490416` | test line 478 | 2 (below) |
| `Real_slot_exit_zero_runs_shards_and_completes` | `00:00:05.8833924` | `17:13:07.6616490`–`17:13:13.5450414` | `RunRealSlotAsync` line 653, test line 485 | 0 |
| `Real_slot_exit_four_is_incomplete_and_skips_shards` | `00:00:05.4891405` | `17:13:13.5461194`–`17:13:19.0352599` | `RunRealSlotAsync` line 653, test line 498 | 0 |
| `Real_slot_shard_exit_records_an_int_and_the_failures` | `00:00:07.4624027` | `17:13:19.0371829`–`17:13:26.4995856` | `RunRealSlotAsync` line 653, test line 509 | 0 |
| `Zero_executed_shard_is_incomplete` | `00:00:04.4155998` | `17:13:28.7943421`–`17:13:33.2099419` | test line 586 | 0 |
| `Slow_shard_is_recorded_on_the_receipt` | `00:00:04.7018260` | `17:13:33.2108699`–`17:13:37.9126959` | test line 432 | 0 |
| `Stale_trx_and_nonzero_shard_exit_are_not_a_green` | `00:00:04.3466787` | `17:13:41.8408061`–`17:13:46.1874848` | test line 405 | 0 |
| `Build_exit_skips_shards_and_writes_an_incomplete_receipt` | `00:00:07.2440998` | `17:13:48.3285345`–`17:13:55.5726343` | test line 348 | 0 |
| `Shard_error_timeout_and_aborted_are_failures` | `00:00:05.4737846` | `17:13:55.5740928`–`17:14:01.0478774` | test line 378 | 0 |
| `New_reds_are_failures_absent_from_the_ancestor_receipt` | `00:00:09.1436920` | `17:14:06.4097127`–`17:14:15.5534047` | test line 164 | 0 |
| `Non_linux_run_refuses_before_a_slot` | `00:00:03.4280197` | `17:14:26.0163905`–`17:14:29.4444102` | test line 243 | 0 |

`Second_run_refuses_while_the_lane_holds_the_lock` overlapped two already-passing tests, both finished before it threw:

- Passed `00:00:05.9985015` `ProcessSpawnTimeoutPolicyTests.Spawning_classes_carry_a_class_timeout` `17:12:55.347`–`17:13:01.346`
- Passed `00:00:08.3292287` `ProcessSpawnTimeoutPolicyTests.Spawn_timeouts_do_not_exceed_ten_minutes` `17:12:55.782`–`17:13:04.112`

## Group B — gates, 3 tests

Assertion on returned state. Empty stderr. Overlap 0. Same date round-trip as above.

### `Start_inside_sixty_minutes_is_hourly`

- Duration `00:00:02.1390081`, `17:13:46.1889092`–`17:13:48.3279173`, test line 117.
- `ShouldAssertException: inside.State should be "skipped-hourly" but was "run"`.
- Payload in the message: `"ok": true`, `"state": "run"`, `"reason": "planned"`, tip `2222…`, base `1111…`, `"startedAt": null`. Exit was already 0 (line 116). The test had written `last-start.json` at `UtcNow.AddMinutes(-30)`.

### `Code_range_plans_namespace_shards_and_one_lane`

- Duration `00:00:05.3604435`, `17:14:01.0488160`–`17:14:06.4092595`, test line 102.
- The open plan's `State.ShouldBe("run")` and the two namespace filters (lines 89–97) had already passed. The failure is the second plan, after `WriteLock(Environment.ProcessId)`.
- `ShouldAssertException: busy.State should be "skipped-busy" but was "run"`.
- Payload: `"state": "run"`, `"reason": "planned"`, shards `unit-ns-Antiphon.Tests.Alpha` and `unit-ns-Antiphon.Tests.Beta`, `"startedAt": null`.

### `Stale_lock_reclaim_is_exclusive`

- Duration `00:00:03.9264813`, `17:13:37.9138171`–`17:13:41.8402984`, test line 539.
- `ShouldAssertException: acquired should be 1 but was 2`.
- Additional info: `acquired 261384 | acquired 78040`.
- Both reclaimers (`POST_LAND_WARNING_RECLAIM_DELAY_MS` 200 and 800) got `Enter-PostLandLock` true. A fresh winner lock is classified `stale` by the same reused-pid date parse, so the loser reclaims too. This is the exclusivity result, not a poll expiry.

## Passed siblings

Same class, all Passed. They bound the cause: they do not `git init`, or they do not ask for `skipped-hourly` / `skipped-busy` / a single acquirer.

| Test | Duration | Start–end (UTC) | Why it stayed green |
|---|---|---|---|
| `Doc_sentences_are_present_in_both_owner_files` | `00:00:00.0522472` | `17:13:26.5019660`–`17:13:26.5542132` | Reads two docs. No repo delete. |
| `Reused_pid_with_an_older_start_does_not_hold_the_lock` | `00:00:02.2392768` | `17:13:26.5546907`–`17:13:28.7939675` | `Fixture.Create()` has no `.git`. Expects `run`, which the stale classification also produces. |
| `Docs_only_range_skips` | `00:00:10.4614253` | `17:14:15.5545795`–`17:14:26.0160048` | No `.git`. Docs skip does not read `startedAt`. |
| `Manifest_uses_no_letter_or_and_partitions_unit_classes` | `00:00:19.4727969` | `17:14:29.4459095`–`17:14:48.9187064` | `deleteRepo: false` on the product root. |
| `Integration_list_drops_unit_classes_and_records_windows_as_owed` | `00:00:02.0625713` | `17:14:48.9190433`–`17:14:50.9816146` | No `.git`. No lock or hourly assert. |
| `Summary_line_names_tip_batch_and_new_reds` | `00:00:02.5161081` | `17:14:50.9819421`–`17:14:53.4980502` | No `.git`. Asserts the summary line only. |

## Debug handoff

- Clear ReadOnly (or delete another way) before `Directory.Delete` on a git fixture repo, and re-run the three `Real_slot_*` tests far enough to see their body assertions.
- Parse `startedAt` from the ISO text. Do not round-trip a `DateTime` through `[string]` and `[datetime]::Parse` under the current culture.
- `Reused_pid_with_an_older_start_does_not_hold_the_lock` passes on this host without proving that a fresh live lock is busy.
