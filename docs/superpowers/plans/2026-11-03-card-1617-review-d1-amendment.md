# CARD-1617 review D-1 amendment: witness the RootExitedForHangup signals

Date: 2026-11-03. Code task 333e1bd2, from Review 46541b3e round 1 D-1 (subject code tip
`fc709c93804c6922251f05f99272cbdfee1d22b8`). Parent plans (unchanged):
`2026-11-03-card-1617-custody-kill-bound-plan.md` and `2026-11-03-card-1617-s3-card-1681-amendment.md`.

## Defect

`PtyAgentRunner.RootExitedForHangup` decides "the root has exited" from two signals: the runner's
exit task, or `/proc/<pid>/stat` showing the root gone, a zombie, dead or PF_EXITING. A missing stat
file or directory is "gone"; an `IOException` or `UnauthorizedAccessException` on the read is "not
exited", so the drain fails closed. No test caught dropping either signal (review mutants M2, M3)
or an unreadable stat read as exited (M5, fails open).

## Change (S4)

- **S4a (red first).** `PtyAgentRunner.RootExited(bool exitObserved, Func<string?>? readStat)` and
  `ProcStatReader(string path)` exist as inert stubs (`false`, and a reader that returns null). New
  pure `Card1681PtyHangupTests` cases (`C1681_Root_*`):
  - exit observed with a live stat is exited, and the stat is not read;
  - stat alone: `Z`, `X`, `x`, PF_EXITING, and a comm with spaces and parentheses (parse after the
    last `)`) are exited;
  - a live stat, including a `Z)` inside comm, is not exited, and is read once;
  - a reader throwing `IOException` (EIO) or `UnauthorizedAccessException` is not exited;
  - a reader throwing `FileNotFoundException` or `DirectoryNotFoundException` is gone (exited);
  - no reader (no root pid) is not exited;
  - the production `ProcStatReader` over real files: live file, zombie file, a directory
    (`UnauthorizedAccessException`, not exited), a missing file and a missing directory (gone).
- **S4b (refactor).** `RootExited` holds the composition and exception mapping that
  `RootExitedForHangup` held; `RootExitedForHangup` passes `_exitTcs.Task.IsCompleted` and
  `ProcStatReader("/proc/<pid>/stat")` (null without a root pid). Behaviour is unchanged: the exit
  signal short-circuits before any read, no pid is "not exited", the read and its exception mapping
  are the same. The Windows job-object span stays byte-identical (sha256
  `2fd1ec493d80b8038c32994e9bcae1742057e369498ce7cc0a62db9d2c712e9a`).

A-3 (`UseCustodyClockForTests` internal) is not taken: its caller `HostSession` is in
`Antiphon.PtyHost`, which `Antiphon.Agents.Pty`'s `InternalsVisibleTo` does not reach, and csproj
edits are out of scope.

### Checkpoints

Lane: Linux (server2). CP-8 runs at the S4a commit and records RED. CP-3, CP-4 and CP-7 run at the
S4b commit with the parent filters unchanged; CP-7 now counts the new cases.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-8 | S4a | `tests/Antiphon.Agents.Pty.Tests -> bin-c1617d1-red/` | c1617d1-unit-red | `/*/*/Card1681PtyHangupTests/*` | D-1 witnesses red-first | 30 executed; 10 FAILED (observed x2, stat alone x5, missing x2, real files); record RED | 30 | 5 | false |
| CP-3 | S4b | `tests/Antiphon.PtyHost.Tests -> bin-c1617d1-host/` | c1617d1-host | `/*/*/LinuxCustodyReleaseHostTests/*` | parent CP-3: W1-W4, C1614 x3, C1681 x2 unchanged | 9 executed, 0 failed, 0 skipped | 9 | 4 | false |
| CP-4 | S4b | CP-3 | c1617d1-floor | `/*/*/(HostCustodyPlatformTests*)\|(CustodyReceiptBackendTests*)/*` | parent CP-4 floor | 6 executed, 0 failed, 0 skipped | 6 | 1 | false |
| CP-7 | S4b | `tests/Antiphon.Agents.Pty.Tests -> bin-c1617d1-unit/` | c1617d1-unit | `/*/*/Card1681PtyHangupTests/*` | classifier and D-1 composition green | 30 executed, 0 failed, 0 skipped | 30 | 5 | false |

Positive controls (pending, Mutation):

| PC | Mutant | Must turn red |
|---|---|---|
| PC-15 | `RootExited` drops the stat signal (`return ProcStatShowsExit(stat)` -> `return false`; review M2) | `C1681_Root_exited_from_stat_alone` x5, `C1681_Root_missing_stat_is_gone` x2, `C1681_Root_stat_reader_reads_real_files` |
| PC-16 | `RootExited` drops the exit signal (`exitObserved && false`; review M3) | `C1681_Root_exited_when_exit_observed_with_live_stat`, `C1681_Root_exited_when_exit_observed_without_reading_stat` |
| PC-17 | An unreadable stat is exited (IO/permission catch returns true; review M5) | `C1681_Root_unreadable_stat_is_not_exited` x2, `C1681_Root_stat_reader_reads_real_files` |
| PC-18 | Missing file/directory is not gone (FileNotFound/DirectoryNotFound catch returns false) | `C1681_Root_missing_stat_is_gone` x2, `C1681_Root_stat_reader_reads_real_files` |
| PC-19 | `RootExitedForHangup` passes no reader, or `RootExited` reads the stat when the exit is observed | CP-3 `C1681_Eio_after_root_exit_drains_to_receipt` (race-dependent), `C1681_Root_exited_when_exit_observed_without_reading_stat` |

CP-8 recorded (run 20261103-094631-f866 at 6b526a262, RED exit 1): 30 executed, 12 failed. The
expected 10 plus `C1681_Root_live_stat_is_not_exited` x2, whose read-count assertion is red under
the stub because the stub never reads.

Review M1 (S2 backoff formula) and M4 (root-exit condition always true) stay witnessed by CP-3.
