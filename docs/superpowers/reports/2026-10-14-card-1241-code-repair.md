# CARD-1241 Code repair report (task 5b418053, Review 06ff09f8 D1/D2)

Continues `f3197abc9edf77b30c6ea1984555d286abdc06fb` (Code 586e863b, the landing owner) on `feat/card-task-5b418053`.
Red commit `43bb94a827653d7d7f05e39148c453b96be79f28`; fix commit `a605ca05c8d27a0cd288574204b50ae538021292`.
Mutation not run; PC-1..PC-6 of the 586e863b report and PC-7..PC-10 below are pending.

## D1: ledger re-checked once the slot is granted

| Where | Change |
|---|---|
| `LedgerStat` (new) | Metadata stamp read without opening the file. Linux: `statx` type, device, inode, size, mtime, ctime (link count not included). Other platforms: type, size, last write time; no file identity. A failed read is `Unknown` and matches nothing. |
| `BuildConfinement.CaptureLedger` | Records each entry's `LedgerStat` before its hash, and `RacyFrom` = capture start minus `RacyWindow` (2 s). |
| `BuildConfinement.AdmitLedger` (new) | Re-hashes an entry when its stat stamp differs or its recorded mtime is not earlier than `RacyFrom`; any difference returns `refused: tampered` with the usual TAMPERED/manual-command lines. Returns the number of hashed entries. |
| `RunScheduler.BuildOneAsync` | After the lease is granted, the write-root preflight and `BeforeLaunch`, calls `AdmitLedger`; on a difference it appends the detail to `builds/<id>/build.log`, sets `refused: tampered` (exit 2) and returns without launching the build. `VerifyLedger` after release is unchanged. |

Not covered (stated in `docs/testing-and-build.md`): a change made between the admission check and the build's launch
(base had the same window between its in-slot stamp and launch), or after a path's after-release stamp, when the final
bytes equal the earlier ones.

## D2: scope statements

Tracked paths under the write roots are excluded from the ledger and stamped inside the slot; now named by
`tracked_file_under_a_write_root_is_stamped_inside_the_slot_not_in_the_ledger`. Corrected or removed in this slice:

- `docs/testing-and-build.md` table: row 1 "Tracked files; untracked and ignored files outside the write roots" became
  "Tracked, untracked and ignored files outside the write roots"; row 2 adds tracked paths under the write roots; each row
  names its test methods; the write-root preflight `tracked` refusal and the uncovered cases are stated with their tests.
- `BuildConfinement.Ledger` comment: "every tracked file and every untracked or ignored file outside the row's write roots" replaced.
- `BuildConfinement.Baseline`, `CaptureUntracked` ("everything else"), `IDriver.Ledger` and `IConfinedDriver` ("non-output
  files", "the slot covers the build itself") comments rewritten to the write-root scope.
- 586e863b report: "every PC", "all stamps", "every tracked file plus ...", "every untracked/ignored hash", PC-2 "every
  listed path", and the second change row (now names tracked/untracked/ignored paths under the write roots).
- Test comments: "every ledger entry", "every worktree file", "only the scheduler's ledger can see changes".

## Tests

- New: `file_changed_during_the_slot_wait_and_restored_by_the_build_is_refused_before_launch` (A -> B in the slot
  client's acquire hook, fake build writes A back; requires no build launch, B still on disk, `refused: tampered`,
  TAMPERED line, row not green), `unchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted` (aged fixture:
  0 hashes, then one changed entry: 1 hash and tampered), `entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted`,
  `tracked_file_under_a_write_root_is_stamped_inside_the_slot_not_in_the_ledger`.
- Changed: `scheduler_stamps_non_output_files_before_the_slot_and_verifies_after_release` now has the build write f0001
  (it was written in the acquire hook, which admission now refuses before launch). Its assertions are unchanged.
- `scripts/lib/checkpoint-usage.ps1` `Get-NamespaceCensus` selected 540 -> 549 (the 586e863b slice added 5 cases without
  moving it; this repair adds 4). origin/master `15a9071fa` adds one `CheckpointSourceExecutionTests` case and still says
  540, so the merged tree needs 550.

Red at `43bb94a8` (TRX `.antiphon/c1241-red/red.trx`, 7 executed, 5 passed, 2 failed): the D1 test failed
`driver.BuildLedgers should be empty but had 1 item [True]`; the census guard failed `should be 546 but was 540`.
The three other new tests use the new `AdmitLedger`/`Tracked` scope and have no compiling red at `f3197abc9`; their
reds are PC-8..PC-10.

Green at `a605ca05` (TRX `.antiphon/c1241-classes/classes.trx`, run `194babd8-b26c-4432-a8d3-f68580f85c1e`,
86 executed, 86 passed, 0 failed, 0 skipped, slot granted waited=0s held=720s):

| Class | Passed |
|---|---:|
| CheckpointConfinementLedgerTests | 9/9 |
| CheckpointNamespaceCensusUsageTests | 1/1 |
| CheckpointTrackedBaselineTests | 13/13 |
| CheckpointOutputConfinementTests | 5/5 |
| CheckpointReceiptSafetyTests | 8/8 |
| CheckpointSourceExecutionTests | 6/6 |
| OutputCleanupTests | 29/29 |
| RunSchedulerTests | 15/15 |

`RunSchedulerTests` loop, 5 separate host runs: 15/15 each (`.antiphon/c1241-loop1..5`; waited 0, 0, 0, 0, 15 s).

## Pending positive controls (Mutation)

| PC | Mutation | Expected red |
|---|---|---|
| PC-7 | `RunScheduler` skips the `AdmitLedger` call | `file_changed_during_the_slot_wait_and_restored_by_the_build_is_refused_before_launch` |
| PC-8 | `AdmitLedger` ignores the stat comparison (hashes each entry) | `unchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted` |
| PC-9 | `AdmitLedger` drops the `RacyFrom` condition | `entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted` |
| PC-10 | `CaptureLedger` stops skipping tracked paths under the write roots | `tracked_file_under_a_write_root_is_stamped_inside_the_slot_not_in_the_ledger` |

## Not changed

- Hard-link false tamper (task 47b93e01): a hard-linked ledger file stamps `unreadable` and can be reported `tampered`
  while unchanged. Not fixed.
- OutputCleanup, CARD-1236, CARD-1242: untouched.
