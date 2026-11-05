# CARD-1725 unbound Mutation candidate

Original Code/landing owner: 7626aee3. Plan: docs/superpowers/plans/2026-11-05-card-1725-ascii-scan.md.

Both PCs remain pending for method-scoped SourceLanding Mutation; Code ran no deliberate mutants.

- PC-1 / R3: in scripts/test-run-checkpoint.ps1 Test-C585_AsciiOnly, revert only the runner byte loop to the pre-fix `@((Read-CheckpointBytes $script:Runner) | Where-Object { $_ -gt 127 })` scan and its Count assertions. Test `CheckpointInheritedFileTests.C1725_LargeAsciiCallers` (precise method selector `/*/*/CheckpointInheritedFileTests/C1725_LargeAsciiCallers`, both native parameters). R3 must fail its unchanged eight-second watchdog assertion; build errors/zero cases do not count.
- PC-2 / R4: same reversion only for the harness byte loop. Same exact method selector; R4 must fail the watchdog assertion.

The witness also counts exactly three non-ASCII bytes spanning the first and last positions of a 2 MiB input, with zero/NUL ASCII bytes between. Returning at the first invalid byte or treating ASCII NUL as invalid must fail its real output/count assertions. Mutation owns additional missing-control discovery, binding exact diffs, source/output custody, red/restore/green and restoration records. No pack binding or mechanical eligibility is asserted here.

Ordinary red at 794bbf04a8de4f09c489722344791453fe09417e: checkpoint 20261105-184534-6c06, 6 executed / 4 passed / 2 failed / 0 skipped. Both C1725 R3/R4 failed Watchdog.ShouldBeFalse; R4 stdout recorded elapsedMs=8002 watchdog=True exit=137. Original four C1233 cases passed. This is ordinary defect evidence, not completed PC credit.
