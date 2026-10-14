# CARD-1241 Code repair report 2 (task dd7920ee, Final Review 157c4013 D1/D2)

Continues `9e8980fa01ad7b108a05913fc36e1a723358cf40` (carrier Code 74be673f; landing owner 586e863b) on
`feat/card-task-dd7920ee`. Red commit `6856ddddfef146c02dcea3a36ac328dbb94e3bf2`; fix commit
`c783625f5ce913896b5aca88c439f57fd5aad607`. No master merge (merge-tree only). Mutation not run.

## D1: admission hashes stamps that cannot show an entry unchanged

| Where | Change |
|---|---|
| `LedgerStat.HasIdentityAndChangeTime` (new) | `Inode != 0 && Changed != 0`. The portable stamp (non-Linux, or the statx `DllNotFound`/`EntryPointNotFound` fallback) has neither. |
| `BuildConfinement.AdmitLedger` | Skips an entry's hash only when the recorded stamp `HasIdentityAndChangeTime`, its mtime **and ctime** are older than `RacyFrom` (capture start minus 2 s), and the stamp equals the current one. Off Linux admission hashes each entry. |
| `BuildConfinement.Ledger.Stat` / `CaptureLedger(..., stat)` | Injectable stat provider, used at capture and admission (default `LedgerStat.Of`); `LedgerStat.Portable` is public for the tests. |

Linux: ctime moves on content writes and on `utimes`/`touch -r`/`SetLastWriteTime`, so an A -> B rewrite with
restored size and mtime changes the stamp (asserted on the real file system). Same-tick risk: the 2 s window now
applies to ctime as well as mtime (`entry_with_a_change_time_inside_the_racy_window_is_hashed_at_admission`).
Cost on Linux unchanged: unchanged aged entries 0 hashes (`unchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted`,
now a declared Windows skip and asserting each stamp has inode and ctime). The fixture's `AgeWorktree` waits 2.25 s
after setting mtimes, because that set moves the ctime into the window.

## D2: refusal timing

Removed (not re-qualified):
- `docs/testing-and-build.md`: "is reported `tampered` after release even when unchanged" -> "can be reported `tampered`
  while unchanged"; "on Windows, a rewrite that keeps size and mtime" deleted from Not covered (now hashed). Table row 1
  states the new skip rule, "a hashed difference is refused `tampered` before launch", and names the new tests.
- `2026-10-14-card-1241-code.md`: "(since repair 5b418053 it is refused before the build starts)" deleted.
- `2026-10-14-card-1241-code-repair.md`: "on Windows a rewrite keeping size and mtime" deleted; hard-link bullet's
  "`AdmitLedger` skips it while ... and `VerifyLedger` still reports it after release" replaced by "can be reported
  `tampered` while unchanged".
- Code comments: `LedgerStat` header, `RacyWindow`, `AdmitLedger` rewritten to the new rule with test names. Other
  slice comments ("checked at admission and after release", "verified after release") state checks, not refusal
  timing, and were left.

Hard links: not changed. Off Linux an unchanged hard-linked ledger file is now hashed at admission, so it is refused
before launch instead of after release; no named test.

## Tests

New (4): `portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch`,
`rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch`,
`stat_without_a_change_time_hashes_each_entry_at_admission`,
`entry_with_a_change_time_inside_the_racy_window_is_hashed_at_admission`.
Census `Get-NamespaceCensus` selected 550 -> 554 (+4); `windowsSkips` adds the Linux-only cost test.

Red at `6856dddd` (TRX `.antiphon/c1241-d1-red/red.trx`, 14 executed, 10 passed, 4 failed): portable scheduler test
(`driver.BuildLedgers` not empty), no-ctime test (`unchanged.Hashed`), ctime-window test (`admitted.Hashed` 0, expected 1),
census (`should be 554 but was 550`). The real-file-system test passed at red on Linux (ctime already compared); its
red is PC-13.

Green at `c783625f` (TRX `.antiphon/c1241-d1-classes/classes.trx`, 91 executed, 91 passed, slot waited=15s held=900s):

| Class | Passed |
|---|---:|
| CheckpointConfinementLedgerTests | 13/13 |
| CheckpointNamespaceCensusUsageTests | 1/1 |
| CheckpointTrackedBaselineTests | 13/13 |
| CheckpointOutputConfinementTests | 5/5 |
| CheckpointReceiptSafetyTests | 8/8 |
| CheckpointSourceExecutionTests | 7/7 |
| OutputCleanupTests | 29/29 |
| RunSchedulerTests | 15/15 |

`RunSchedulerTests` loop, 5 `--no-build` host runs: 15/15 each (`.antiphon/c1241-d1-loop1..5`; waited 15, 90, 0, 0, 0 s).

## Pending positive controls (Mutation)

| PC | Mutation | Expected red |
|---|---|---|
| PC-11 | `AdmitLedger` drops the `HasIdentityAndChangeTime` condition | `portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch`, `stat_without_a_change_time_hashes_each_entry_at_admission` |
| PC-12 | `AdmitLedger` drops `recorded.Changed < RacyFrom` | `entry_with_a_change_time_inside_the_racy_window_is_hashed_at_admission` |
| PC-13 | `LedgerStat.Matches` ignores `Changed` | `rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch` (Linux) |
| PC-14 | `HasIdentityAndChangeTime` returns true | `stat_without_a_change_time_hashes_each_entry_at_admission`, `portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch` |
