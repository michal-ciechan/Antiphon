# Run b7812833 session-runner all-profile-199: HostTopConsumerTests Windows witness failure

- Run: b7812833, Windows desktop qualification of `rc/2026-11-21` = `64e3d78c487d51570f708492ab67b5db6d0de9a9` (still running when read).
- Evidence folder: `C:\Antiphon\releases\candidates\rc-2026-11-21-64e3d78c487d\logs\2026-11-15-0434-b7812833\` (read with the name filter `session-runner-all-profile-199*` only).
- Read by task bffbe413, 2026-11-15. The reproduction ran on the same desktop, through the build slot, and selected only `HostTopConsumerTests` and a temporary diagnostic class that was not committed.

## Verdict

**Product defect, deterministic. It is not a race and not a ranking cut.** `WindowsHostProcessTable.Read` runs out of its own 10 ms thread-CPU scan budget (`HostTopConsumerLimits.ScanCpuBudget`) in `SnapshotNames`. That happens before any pid is read, and in that case `Read` returns `[]` (`if (LastReadTruncated) return [];`). On this desktop (~495-515 processes), one `CreateToolhelp32Snapshot` costs 5-9 ms of thread CPU. The `Process32NextW` walk costs another 6-10 ms. Every Windows read, cold or warm, is therefore truncated and empty, so the production Windows top-consumer list is always empty on this host. The test is a correct witness. It should not be relaxed or made to wait.

Neither hypothesis in the brief holds:
- *Cached off-tick scan race:* the test never touches the sampler or the scan worker. It calls `HostProcessTables.Create().Read(ScanCap, [self.Id])` directly and synchronously (`HostTopConsumerTests.cs:61-64`).
- *Ranking drops a low-CPU process:* `Read` does no ranking. The current pid is passed as a retained pid, and `HostProcessSelection.Choose` puts retained pids first. The list was empty, not missing just this process.

The test is **new in this cut**. `tests/Antiphon.SessionRunner.Tests/HostTopConsumerTests.cs` does not exist at `b559bfba2` (`git show b559bfba2:<path>` reports "exists on disk, but not in 'b559bfba2'"). The witness was added by `004f8677e` (test(host-stats): guard Linux proc tests and witness Windows self). The claim that it passed in run 34d41b7e on b559bfba2 cannot refer to this test.

## Failed test (chunk 199 ran only this test)

| Field | Value |
|---|---|
| Test | `Antiphon.SessionRunner.Tests.HostTopConsumerTests.Production_factory_observes_this_process_on_windows` |
| Outcome | Failed, 42 ms (`duration="00:00:00.0424061"`), start 2026-11-15T06:16:39.3287696Z |
| Message | `InvalidOperationException: Sequence contains no matching element` |
| Expected vs actual | expected exactly one row with `Pid == self.Id` from `table.Read(512, [self.Id])`; actual: no such row (the diagnostics below show the list is empty, `LastReadTruncated == true`) |
| Stack top | `System.Linq.ThrowHelper.ThrowNoMatchException()` <- `Enumerable.Single(source, predicate)` <- `HostTopConsumerTests.Production_factory_observes_this_process_on_windows()` `HostTopConsumerTests.cs:line 64` |
| Stdout | `[CARD-0206] no leaked pty-hosts` (no stderr, no other log lines) |
| Chunk | total 1, failed 1; runWall 1.37 s, 06:16:38.48Z-06:16:39.88Z; build 562.6 s (`buildSlot: not-used`) |
| Concurrent | Chunk 199 was its own single-test process. The neighbouring chunks ran sequentially: 198 (06:16:37-06:16:38, 1 test) and 200 (06:16:39-06:16:41, 1 test). No sharing violation, timeout or poll expiry appears in the evidence. |

Files read: `session-runner-all-profile-199.trx`, `-tests.log`, `.timing.json`, `-selection.discovery.json`, `-execution\` (diag log only).

## Passing siblings (same class, native class run on this desktop at the cut tip)

The class run had 31 tests: 28 passed, 2 skipped (both `NativeLinuxOnly`), and 1 failed, the same failure as in the run, twice out of 2. Passed:
Factory_selects_the_platform_reader, Shared_compiler_under_pid_1_is_ranked_compiler, Pid_reuse_does_not_inherit_the_previous_counter,
Vanished_pid_is_omitted_and_the_sibling_remains, Top_count_keeps_eight_highest, Command_line_and_environment_are_not_published,
Scan_keeps_a_retained_low_pid_inside_the_cap, Short_interval_does_not_rescan_or_move_the_baseline, Scan_fault_returns_the_previous_ranking,
Missing_table_publishes_no_top_consumers, Process_sampling_off_does_not_read_the_table, Blocking_scan_does_not_stretch_the_tick,
Unsafe_name_is_dropped, Roles_follow_the_allow_list, Old_history_without_top_consumers_deserializes_empty, Projection_keeps_eight_publishable_rows,
Eight_rows_add_a_bounded_wire_delta, Held_scan_keeps_ticks_moving_and_does_not_overlap, Stop_while_scan_is_held_discards_the_late_result,
Stop_after_the_epoch_check_discards_the_held_observe, Dispose_twice_while_scan_is_held_drops_the_late_result,
Scan_exception_is_logged_once_and_contained, Missing_constant_and_backwards_clocks_fail_closed, Stalled_and_regressing_clocks_fail_closed,
Enumeration_cost_exhausts_the_budget_before_any_process_read, Scan_stops_when_the_cpu_budget_is_spent, Cached_scan_is_stale_after_twice_the_cadence,
Source_does_not_open_environ_or_snapshot_every_process.
Skipped (Linux only): Production_factory_selects_the_linux_proc_table, Live_proc_scan_reads_proc_off_the_tick.

What the siblings show: every scan-worker, epoch, publish, ranking and budget-logic test passes, using fake clocks and Linux fixtures. In particular, `Enumeration_cost_exhausts_the_budget_before_any_process_read` passes. It asserts the designed behaviour this failure exercises: enumeration cost spends the budget and the read returns 0 rows, truncated. The budget logic is correct. The defect is that the real Windows enumeration cost on this host exceeds the 10 ms budget every time, and that truncation during enumeration also drops the retained pids. Only the one test that touches real toolhelp fails.

## Native reproduction (desktop, cut tip, fresh process per run, build slot)

The worktree was at `4ce55f367`, which descends from the cut. `HostProcessTable.cs`, `HostTopConsumerTests.cs` and the rest of `src/Antiphon.SessionRunner` and `tests/Antiphon.SessionRunner.Tests` are byte-identical to `64e3d78c4`.

- Method alone (`--treenode-filter "/*/*/HostTopConsumerTests/Production_factory_observes_this_process_on_windows"`): **5/5 failed**, 44-60 ms, same message and line.
- Whole class: **2/2 runs failed** only this test (31 total / 28 passed / 2 skipped).
- Temporary diagnostic (not committed; it called internal `WindowsHostProcessTable` and `HostStatsSamplerService.ThreadCpuMilliseconds` directly):

```
DIAG clock c1=159.732 c2-c1=5.601ms c3-c2=0.001ms          <- first clock read calibrates (3x2 ms spin) and charges ~6 ms to the next reading
DIAG read0 count=0 truncated=True self=False cpu=14.803ms procs=492
DIAG read1 count=0 truncated=True self=False cpu=10.096ms procs=492   <- warm clock, still truncated
DIAG read2 count=0 truncated=True self=False cpu=10.127ms procs=493
DIAG read3 count=0 truncated=True self=False cpu=10.064ms procs=493
(run 2: read0 cpu=11.825ms, reads1-3 ~10.05-10.08ms, all count=0 truncated=True)
DIAG nowarm read0..2 count=0 truncated=True self=False                 <- 2/2 runs
DIAG split0 snapshotCpu=9.239ms walkCpu=9.955ms entries=497
DIAG split3 snapshotCpu=8.786ms walkCpu=8.931ms entries=497
(run 2) split0 snapshotCpu=6.781ms walkCpu=7.782ms entries=514; split2 snapshotCpu=5.301ms walkCpu=6.024ms entries=513
```

Mechanism (`src/Antiphon.SessionRunner/HostProcessTable.cs` at the cut):
1. `Read` creates `ScanBudget(10 ms, ThreadCpuMilliseconds)`. The origin reading is taken before `CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS)` at line 543, so the snapshot's kernel CPU (5-9 ms) is charged to the budget.
2. `SnapshotNames` checks `budget.Exhausted()` for every entry. The per-entry `Process32NextW` uses a non-blittable struct (`ByValTStr` 260-char `Exe`), so a string is marshalled per entry, costing ~12-20 us each and 6-10 ms per walk. The budget is spent partway through the walk, and `LastReadTruncated = true`.
3. Line 511: `if (LastReadTruncated) return [];`. Retained pids, including the requested one, are never read.
4. Cold process only: the first `WindowsThreadCycleClock.Milliseconds()` call calibrates after reading cycles, so the next reading also includes the ~6 ms calibration spin. That makes the first read worse, but it is not needed for the failure, because warm reads fail too.

Production impact, inferred from code and not observed on the live runner: the off-tick scan worker (`HostStatsSamplerService` ~line 195) gets `rows = []` and `truncated = true` on every Windows scan. The Windows runner therefore never publishes top consumers. The Linux table uses the same 10 ms budget over `/proc`, and its native tests (Linux-only) were not exercised here.

## Why it was missed (rc standing rule)

The witness was authored on Linux, where it skips (`Skip.Test` on non-Windows), and every other test in the class uses fake clocks or synthetic `/proc` fixtures. CARD-1884's budget work (`6bc9f050d`, `fa90c443c`, `652c10417`, `f4f2c7706`) proved the budget logic against injected clocks and never measured the real Windows toolhelp cost against `ScanCpuBudget`. The skip reason on `Live_proc_scan_reads_proc_off_the_tick` says so: "Windows owes toolhelp and the overhead budget". No Windows-native run of this class happened before the rc cut. The first native execution was this qualification.

## Fix directions for Debug (red already exists: the failing witness)

The production fix should be red-first against the existing witness, which already fails 5/5 natively. Do not add a sleep, a wait-for-epoch or a looser assertion. Options, which can be combined:
- Never discard retained pids on an enumeration truncation. Read the retained pids (bounded) even when `SnapshotNames` was cut short, and still report `LastReadTruncated`.
- Make the Windows enumeration fit the budget. Use a blittable `PROCESSENTRY32W` (a `fixed char[260]` buffer, decoding the name only for chosen pids), or replace toolhelp plus a per-pid `OpenProcess` with one `NtQuerySystemInformation(SystemProcessInformation)` call, which returns pid, create time, CPU times and working set together.
- Treat the snapshot syscall as a fixed cost outside the per-entry budget, or give Windows a measured platform budget. Per the test-performance standards, budget about 2x the measured p95. Measured here: 11-19 ms total for a full snapshot plus walk at ~500 processes.
- Calibrate `WindowsThreadCycleClock` before taking the budget origin, at construction or at runner start, so the first scan is not charged the calibration spin.
- Add a Windows-native timing witness: a full read of the real table on a host with N processes finishes untruncated and includes the current process. The existing witness already does this. The rc roster must keep it on the Windows profile.
