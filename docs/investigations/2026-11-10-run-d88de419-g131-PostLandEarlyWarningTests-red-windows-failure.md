# Run d88de419 group 131: PostLandEarlyWarningTests red on Windows

Read-only extract from the desktop Windows Full qualification run `d88de419` on
`rc/2026-11-16` = `6f8741d872d63507827014f972ccf71196722d4a` (schedule slot of old group
130), chunk `antiphon-all-group-131`. Sources, read only:
`C:\Antiphon\releases\candidates\rc-2026-11-16-6f8741d872d6\logs\2026-11-11-1142-d88de419\`
`antiphon-all-group-131.trx`, `antiphon-all-group-131-tests.log`,
`antiphon-all-group-131.timing.json`. Why-missed note:
[2026-11-11-why-missed-post-land-census-raw-string-probe.md](2026-11-11-why-missed-post-land-census-raw-string-probe.md).

## Chunk summary

- 440 total, 439 passed, 1 failed, 0 skipped; run wall 1842 s (30m 40s), build 711 s,
  started 2026-11-11T12:27:12Z, completed 12:57:54Z.
- Only failure: `Antiphon.Tests.Scripts.PostLandEarlyWarningTests.Manifest_uses_no_letter_or_and_partitions_unit_classes`.

## The failure

- Start 2026-11-11T12:46:27.219Z, end 12:46:46.660Z, duration 19.44 s.
- Exception: `ShouldAssertException` at
  `PostLandEarlyWarningTests.cs:line 286` (`actualKeys.ShouldBe(expectedKeys)`).
- Message shape: `actualKeys should be [ ...578 keys... ] but was (case sensitive comparison)
  [ ...577 keys... ] difference [ ..., "Example\nUnlimitedTests", * ]`.
- Expected (test `Census` over `tests/Antiphon.Tests`) had 578 `namespace\nclass` keys;
  actual (unit shards planned by `scripts/post-land-early-warning.ps1 -Plan`) had 577.
  The only key in expected and not in actual: `N\nProbeTests`. Nothing extra in actual,
  no duplicates.
- Stdout: only `ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Scripts.PostLandEarlyWarningTests.1.1.Manifest_uses_no_letter_or_and_partitions_unit_classes.1.1.0`.
  Stderr empty. tests.log carries the same message and stack, no other lines near it.
- Stack top frames:
  `at Antiphon.Tests.Scripts.PostLandEarlyWarningTests.Manifest_uses_no_letter_or_and_partitions_unit_classes() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Scripts\PostLandEarlyWarningTests.cs:line 286`,
  then `ExceptionDispatchInfo.Throw` / `TaskAwaiter.HandleNonSuccessAndDebuggerNotification`
  (TUnit internals omitted).

## Running at the same time

The class carries `[ParallelLimiter<ProcessSpawnLimit>]`. Results whose window overlaps
12:46:27-12:46:47 are siblings only:
`Integration_list_drops_unit_classes_and_records_windows_as_owed` (Passed, 2.98 s) and
`Non_linux_run_refuses_before_a_slot` (Passed, 2.83 s).

## Passing siblings (19 of 20 in the class)

`Real_slot_shard_exit_records_an_int_and_the_failures`, `Real_slot_exit_zero_runs_shards_and_completes`,
`Real_slot_exit_four_is_incomplete_and_skips_shards`, `Stale_lock_reclaim_is_exclusive`,
`Second_run_refuses_while_the_lane_holds_the_lock`, `Readonly_git_object_disposes`,
`Zero_executed_shard_is_incomplete`, `Doc_sentences_are_present_in_both_owner_files`,
`Reused_pid_with_an_older_start_does_not_hold_the_lock`, `Slow_shard_is_recorded_on_the_receipt`,
`Code_range_plans_namespace_shards_and_one_lane`, `Shard_error_timeout_and_aborted_are_failures`,
`Build_exit_skips_shards_and_writes_an_incomplete_receipt`,
`Integration_list_drops_unit_classes_and_records_windows_as_owed`, `Non_linux_run_refuses_before_a_slot`,
`Summary_line_names_tip_batch_and_new_reds`, `New_reds_are_failures_absent_from_the_ancestor_receipt`,
`Start_inside_sixty_minutes_is_hourly`, `Docs_only_range_skips`, `Stale_trx_and_nonzero_shard_exit_are_not_a_green`.

These bound the cause. Every method that 01b93066a repaired after run 6591e378 group 130
(the read-only git object dispose, the en-GB `startedAt` parse, the hourly, busy and
exclusive-reclaim gates) passed here. The one red is a census comparison over the real
source tree, not fixture lifecycle or lock timing.

## Deterministic, not timing

Deterministic. The assertion compares two sets computed from file content: no poll, no
timeout, no sharing violation, no process exit code. The key differs because of text in
one file, `tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs`, which CARD-1820
(`af1c08c73`, `2d94f483f` on rc) filled with raw-string probes:

```
("tests/Antiphon.Tests/A.cs", """
    namespace N;
    public partial class ProbeTests {
        [Test] ...
...
[("tests/Antiphon.Tests/ProbeTests.cs", """
    [Category("Unit")]
    public sealed class ProbeTests {
        [Test] ...
```

Both scanners read these lines as source. The namespace stays `N` from the first probe to
the last. The script's `Get-PostLandClasses` dedups by key before it looks at categories,
so the first `N.ProbeTests` (a partial part with no `[Category]`) wins and is not unit.
The test's `Census` drops non-unit parts before its dedup, so the later
`[Category("Unit")]` part survives. Within one file the walk order is the line order, so
this is the same on every OS; it is not Windows-specific (inferred; no Linux rerun of the
rc tree was made here).

## Same failure as 01b93066a?

No. 01b93066a (CARD-1784, cherry-pick of `a86c257bd`) fixed the group-130 failures of run
6591e378 on rc/2026-11-15: `UnauthorizedAccessException` from fixture dispose on
read-only git objects and the en-GB `startedAt` parse (see
`2026-11-10-why-missed-post-land-early-warning-g130.md` and the run 6591e378 final-result
note on `origin/feat/card-task-d6579463` `7a3c45c3f`). That list does not include
`Manifest_uses_no_letter_or_and_partitions_unit_classes`, which passed in that run because
rc/2026-11-15 (`ac6d00022`) did not contain CARD-1820. `a86c257bd` itself is not based on
CARD-1820 (`a20ac7e8e`, `12ee34565` are not its ancestors), so its Linux class run could not
see this either.

## Fix (branch feat/card-task-0c8caad0)

- `333cbe507084d1c2cf809ae926b160d77a79e088`, `5c1e08f00e088289c423b16731996127ec2b322b`:
  red witness `Raw_string_classes_are_not_planned_and_partial_parts_merge` (a fixture with
  a non-unit partial part before a `[Category("Unit")]` part, and a raw-string
  `namespace N;` probe).
- `7efd8cb496512e47180df5c4026f23a1c62393b5`: `Get-PostLandClasses` and the test's
  `Census` skip multi-line raw-string literals (an odd count of 3+ quote runs with the
  last run at line end opens; a line starting with that many quotes closes) and merge
  partial parts (`[Category]` on any part counts). Owner sentence added to
  `docs/testing-and-build.md` under Post-land early warning.

Script-level red then green, same fixture through `scripts/post-land-early-warning.ps1 -Plan`:

- before: `shard filter=/*/Antiphon.Tests.Gamma/*/*[Category=Unit] classes=HostTests` and
  `shard filter=/*/N/*/*[Category=Unit] classes=N.ProbeTests` (GammaTests lost, fake shard planned).
- after: one shard `/*/Antiphon.Tests.Gamma/*/*[Category=Unit]` with `GammaTests,HostTests`.

Effect on the real tree at the cut (script census, 1688 rows, 577 unit before; 1667 rows,
562 unit after): every removed row is raw-string fixture text (for example `N.ProbeTests`,
`Example.UnlimitedTests`, `Antiphon.Tests.Checkpoints.HostTests`, `C487.Probe.*`,
`Antiphon.Tests.Scripts.{OverrideTargetTests,UndeclaredOsTokenTests,...}`). The fake
`N` and `Example` namespaces no longer become zero-test unit shards. 32 real partial
classes (for example `LandGraphEvaluationTests`, `DelegationDispatchRecoveryBoundaryTests`,
`AgentTaskReplyIntegrationTests`) whose `[Category("Integration")]` sits on a later part
now read as Integration instead of unclassified.

Native Windows proof: on the desktop, at `7efd8cb4`, through `scripts/build-slot.ps1`,
`dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter "/*/*/PostLandEarlyWarningTests/*"`:
total 22, failed 0, succeeded 22, skipped 0, duration 5m 31s, including
`Manifest_uses_no_letter_or_and_partitions_unit_classes` over the real tree and the new
witness. The C# witness was not run red under the harness (a queued red run starved on the
host memory floor and was stopped); its red is the script-level reproduction above with the
same fixture. A Linux run of the class is not yet recorded.
