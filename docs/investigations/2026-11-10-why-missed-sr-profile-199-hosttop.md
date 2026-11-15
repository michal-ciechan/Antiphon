# Why missed: sr-profile-199 HostTopConsumerTests on Windows (rc b7812833)

Task f9e020bf (Debug). rc/2026-11-21 = `64e3d78c487d51570f708492ab67b5db6d0de9a9`, Windows
qualification run b7812833, chunk session-runner-all-profile-199.

## Failure

`HostTopConsumerTests.Production_factory_observes_this_process_on_windows` failed with
`InvalidOperationException: Sequence contains no matching element`. It reproduced 5 of 5
runs alone on the native desktop at the cut tip. It is a product defect, not test timing:
`WindowsHostProcessTable.Read` returned no rows on every call, so Windows top consumers were
always empty.

## Cause

`fa90c443c` (CARD-1884, "own the scan worker and fail closed when the cpu clock stalls") put
the Windows reader under the same 10 ms thread-CPU `ScanBudget` as the Linux `/proc` reader.
The budget started before `CreateToolhelp32Snapshot` and was checked on every
`Process32NextW`. If it ran out during the walk, `Read` returned `[]`. Native measurements on
this desktop (~500 processes, live run in progress), per scan:

| Phase | Thread CPU |
|---|---|
| `CreateToolhelp32Snapshot` capture | 7.6 to 8.2 ms |
| `Process32NextW` walk, 499 entries | 8.3 to 10.0 ms |
| `OpenProcess` + `GetProcessTimes`, 336 openable | 3.0 to 3.8 ms |

The capture plus the walk exceeds 10 ms by themselves, so every read stopped inside the walk.
Replacing the per-call struct marshalling with one native buffer did not help, because the
cost is inside `Process32NextW`.

## Fix

`d868e8b59` (red `f1b334ad4`): the snapshot is finite, and its cost scales with the host's
process count. The budget now starts after it and still stops the per-process handle reads,
retained pids first. An `IWindowsProcessSource` seam lets two deterministic tests run on every
host: `Windows_snapshot_capture_does_not_spend_the_scan_budget` (red before the fix) and
`Windows_reads_stop_at_the_scan_budget_after_the_retained_pid`. After the fix, a native read
returns about 318 rows with this process always present. Under the live run's load the reads
are sometimes truncated by the budget, which is the designed behaviour.

## Why the gate missed it

- Introducing card: CARD-1884 (host top consumers). The test was added in `004f8677e`; the
  defect arrived in `fa90c443c` and was carried by `f4f2c7706`. Both were authored and
  cherry-picked on the server2 runner (Linux).
- The approving CARD-1884 Reviews ran on server2 Linux. They ran the CARD-1884 class filters
  (`HostTopConsumerTests*` and the sampler classes). On Linux this test calls
  `Skip.Test(...)`, so the Reviews counted it as skipped, not run.
- The plan says so itself. `docs/superpowers/plans/2026-11-13-card-1884-host-top-consumers-plan.md`
  D-9: "Native Windows toolhelp proof is owed by the orchestrator". PC-4: "Windows native row.
  Not a source mutant ... This Linux host does not discharge it." No native Windows row was
  scheduled before the rc cut, so the first Windows execution was the rc qualification itself.
- The budget tests (`Scan_stops_when_the_cpu_budget_is_spent`) used a synthetic Linux clock
  and never measured the Windows snapshot cost.

## Cheapest pre-land check (impacted-classes row)

| Trigger | Row | Host |
|---|---|---|
| Diff touches `src/Antiphon.SessionRunner/HostProcessTable.cs` or `HostStatsSamplerService.cs` (`WindowsHostProcessTable`, `ScanBudget`, `WindowsThreadCycleClock`) | `/*/*/HostTopConsumerTests/*` (about 30 s after build) | native Windows (desktop runner) |

A plan that owes a native Windows row (like D-9 here) should name it as a land gate, not leave
it to the rc run.

## Side note (not this failure)

`Stop_while_scan_is_held_discards_the_late_result` and
`Dispose_twice_while_scan_is_held_drops_the_late_result` failed in 1 of 5 class runs on the
loaded desktop, both before and after this fix. The cause is `entered.Wait(2s)`: the scan worker
is slow to start under load. They use `ScriptedTable`, not the Windows reader. Follow-up
candidate: a p95-based bound for that entry wait.
