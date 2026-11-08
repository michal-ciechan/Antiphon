# Why missed: Windows stamp-back witness written inside one file-clock tick

rc run a6410818 (rc/2026-11-12 = `5fef2c23b`), chunk `all-profile-1543`, failed 1 of 1:
`OutputCleanupTests.windows_set_clock_and_foreign_dacl_are_not_adopted`,
`ShouldAssertException: SafeWrite.MatchesCreation(stamped) should be False but was True`
at `OutputCleanupTests.cs:1390`. Windows read-out:
`docs/investigations/2026-11-08-run-a6410818-profile-1543-windows-failure.md` (task 92c33d61,
branch `feat/card-task-92c33d61`, commit `6ed72cf530`).

Classification: Windows-only test fixture timing race (flaky), not a product defect. The test takes
the observed exit as precise `DateTimeOffset.UtcNow`, then tampers with and stamps back `app.dll`
straight away. NTFS change time comes from the coarse kernel clock, which lags the precise clock by
up to one timer tick. A metadata change made inside that tick gets a change time at or before the
exit, so `OutputCustody.Refusal` correctly adopts it. `OutputCustody.cs:12-14` discloses that tick
as the custody boundary. The Linux custody witnesses already step past it with `PastKernelClock`
(CARD-1242 F1), but this Windows witness never called it. The same mechanism on server2 Linux: in
2,000 writes made right after reading the precise `UtcNow`, 1,998 got a file time at or before it.
On Windows the gap is usually covered by handle and Defender overhead, so the test passed 12 of 14
times before. The two earlier Windows reds (27a1b370, a779085c) were other failure modes at line 1339.

Fix (test only, master): `574371536f526aa9a1fc8ccc8831db615f94dd8d` calls
`PastKernelClock(clockExit)` before the tamper. No assertion changed, and the product is unchanged.

## Introducing commit/card

introducing commit/card: 433122a5b9e7b4fc21957ebf25dcb636c7174d60 (CARD-1318), "test(CARD-1318): red
Windows clock, foreign DACL, and sharing retry". It added the stamp-back half without the clock step.
The fix it witnessed is a6b7a67abab600572b37b0e047a0e6e9ef3114e6.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- cc798cdf (Review of Code f5c9c9db at `984e10fb6`), scope Full, on the Windows desktop
  (`C:\src\Antiphon`), selection `/*/*/OutputCleanupTests/windows_set_clock_and_foreign_dacl_are_not_adopted`
  (CP-1, 1/1 passed) plus the sharing, seal and `CheckpointShadowCustodyTests` rows. Outcome Found
  for another defect (copied-file change time).
- 4c9cd695 (Review of the repair at `cc022d912`), scope Full, Windows desktop, selection
  `/*/*/OutputCleanupTests/(windows_copied_old_change_time_is_adopted*)|(windows_set_clock_and_foreign_dacl_are_not_adopted*)|(windows_sharing_violation_retries_then_adopts*)|(windows_seal_docs_match_the_landed_adoption*)`
  (CP-1, 4/4 passed). Outcome Clean, and this review approved the land.

## Outside the selection

why the failing test was outside that selection (lane/category/path): it was not outside the
selection. The Windows lane, Unit category and path `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs`
were selected and run. Both reviews ran the failing method natively on Windows and it passed, and it
passed in 12 later Windows qualification runs. A single run cannot expose a race that loses on a few
percent of runs: it needs the tick boundary to fall between the exit stamp and the tamper. Linux
lanes skip the method by construction (4 Windows-only skips per class run), so no Linux review
could see it. The miss is a missing clock step in the fixture, not a selection gap. Neither review
checked the Windows witness against the tick boundary that the product comment discloses and the
Linux witnesses already honour.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`src/Antiphon.HostCleanup/Checkpoints/OutputCustody.cs`, `src/Antiphon.HostCleanup/Checkpoints/OwnedOutputLedger.cs`, `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs` -> `/*/*/OutputCleanupTests/*` (Windows lane required, 49 tests)

`scripts/impacted-classes.ps1 -Path src/Antiphon.HostCleanup/Checkpoints/OutputCustody.cs,tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs`
on master `cad7766950` prints `kinds=` and an empty `treenodeFilter=`. The table has no row for
the custody sources, and it has no lane field. The row decides what gets selected but does not
catch a rare race. The cheaper review rule: a Windows custody witness that writes after an observed
`UtcNow` exit and expects a refusal must first call `PastKernelClock`, as the Linux witnesses do.

## Exemption

same cause as an already written note: none. The other a6410818/4e0d1159 notes
(`2026-11-08-why-missed-server-repository-mirror-windows.md`, `2026-11-08-why-missed-server-git-windows.md`)
cover Windows-sensitive classes approved on Linux only. Here the class was approved on Windows,
and the cause is a timestamp-granularity race.
