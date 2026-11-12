# Run ae84478d group 97: C0824RefOnlyCleanupTests red on Windows

Read-only extract of one failed chunk of the Windows rc qualification run `ae84478d` on `rc/2026-11-17` = `3db1a43b02246706dc28075fce7e7b5e84804ddb`. The run was still in progress. Nothing under `C:\Antiphon` was created, changed or restarted. Only the `antiphon-all-group-97*` files were read.

Log directory: `C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\logs\2026-11-11-1907-ae84478d\`

| File | Bytes |
|---|---|
| `antiphon-all-group-97-tests.log` | 3,647 |
| `antiphon-all-group-97.timing.json` | 2,549 |
| `antiphon-all-group-97.trx` | 381,994 |
| `antiphon-all-group-97-execution\log_261112000617785.diag` | 750,709 |

## Verdict

**Deterministic, not timing.** This is a fixture guard rejecting a deliberate test input, so the slow-Windows-git family behind group 56 does not apply. The test failed in 5.18 s against a 60 s class `[Timeout]`. The message is a named `InvalidOperationException` from the fixture constructor. No poll expired, no timeout fired and no sharing violation occurred. It fails the same way on every platform, Linux included, because `Path.GetTempPath()` there is `/tmp/` and the test builds its root under it.

## Chunk summary

| Field | Value |
|---|---|
| TRX counters | total 207, executed 206, passed 205, failed 1, notExecuted 1 |
| Started / completed | 2026-11-12T00:06:17.528Z / 2026-11-12T00:09:27.966Z |
| runWallSeconds | 190.309 (`buildSeconds` 475.008, `buildSlot` not-used) |
| p50 / p95 / max | 0.071 s / 7.031 s / 35.946 s |
| TUnit | v1.44.0.0, .NET 9.0.23, win-x64, Windows 10.0.19045 |

## The failed test

`Antiphon.Tests.Application.C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs`

- Outcome: Failed. Duration `00:00:05.1835113` (5 s 183 ms). Window: 00:06:42.153Z to 00:06:47.336Z.
- Assertion: none. The test failed before its first Shouldly assertion. The exception text:

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] InvalidOperationException:
c1017_checkout_under_temp: C:\Users\lndco\AppData\Local\Temp\c1017\9f7280f0193a48d5822c1de258bbd9ad
is under C:\Users\lndco\AppData\Local\Temp\; run from a checkout outside the temp directory.
```

- Expected vs actual: the R-5 arm expects `CompletedCardCleanupFixture.CreateAsync(sourceRoot: <temp>, reportRoot: <persistent>)` to build a fixture. It got a refusal from `CompletedCardCleanupFixture.TempCheckoutRefusal`.
- Stack top frames:

```
at Antiphon.Tests.TestHelpers.CompletedCardCleanupFixture..ctor(TimeProvider clock, String sourceRoot, String reportRoot) CompletedCardCleanupFixture.cs:51
at Antiphon.Tests.TestHelpers.CompletedCardCleanupFixture.CreateAsync(String leaf, TimeProvider clock, String sourceRoot, String reportRoot) CompletedCardCleanupFixture.cs:71
at Antiphon.Tests.Application.C0824RefOnlyCleanupTests.AssertPersistentReportLocationsAsync() C0824RefOnlyCleanupTests.cs:260
at Antiphon.Tests.Application.C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs() C0824RefOnlyCleanupTests.cs:30
```

- Standard output (all of it): the first loop arm (`temporarySource=false`, root under the checkout's `.antiphon\test-output\c1017\`) built and tore down normally. The second arm (`temporarySource=true`, root under `%TEMP%\c1017\`) threw before it printed any fixture stage.

```
ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.C0824RefOnlyCleanupTests.1.1.All_desktop_consumers_skip_ref_only_without_deleting_refs.1.1.0
C1017_FIXTURE stage=host-initialize task=a32c1d80-... root=C:\Antiphon\releases\checkout\.antiphon\test-output\c1017\46fa3e7b...
C1017_FIXTURE stage=worktree-create task=a32c1d80-... tree=...\c1017\46fa3e7b...\trees\card-task-a32c1d80
C1017_FIXTURE stage=worktree-created task=a32c1d80-... tree=...\trees\card-task-a32c1d80
C1017_FIXTURE stage=delete-root task=a32c1d80-... root=C:\Antiphon\releases\checkout\.antiphon\test-output\c1017\46fa3e7b...
```

- stderr: none recorded for the test.
- Ran at the same time (TRX windows overlapping 00:06:42.153Z to 00:06:47.336Z):
  - `C0824RefOnlyBoundaryTests.Legacy_release_keeps_the_classified_revision` Passed 14.11 s
  - `C0824RefOnlyBoundaryTests.Incomplete_preparation_refuses_initial_and_resumed_land` Passed 11.94 s
  - `Card1773TranscriptPromptStampMigrationTests.Retry_replaces_an_interrupted_invalid_index` Passed 30.81 s
  - `PoolClaudeEffortMigrationTests.C1491_nullable_stamp_roundtrips_and_legacy_rows_stay_unknown` Passed 35.95 s
  - `C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation` NotExecuted. It is the expected Windows skip: "S7 real phone-home recipient fixture requires Linux; Windows handle qualification is separate."

  Concurrency is irrelevant here, because the refusal is a pure path comparison done before any I/O.

## Siblings

The `C0824RefOnlyCleanupTests` class has three tests:

| Test | Outcome | Duration |
|---|---|---|
| `All_desktop_consumers_skip_ref_only_without_deleting_refs` | **Failed** | 5.18 s |
| `Legacy_missing_and_dirty_checkouts_keep_their_guards` | Passed | 5.78 s |
| `Remote_cleanup_still_uses_its_binding_and_generation` | Skipped (Linux-only) | 0.008 s |

The other C0824 ref-only classes in the chunk all passed:

- `C0824RefOnlyBoundaryTests`: `Legacy_release_keeps_the_classified_revision`, `Incomplete_preparation_refuses_initial_and_resumed_land` and `Local_merge_and_checkout_typed_removal_refuse_ref_only_owner`.
- `C0824RefOnlyConsumerTests`: `Same_repo_tasks_keep_distinct_ownership_and_display_mode`, `Decision_and_control_paths_do_not_grant_identity_file_access` and `Child_auth_uses_explicit_repo_ref_and_remote_execution`.
- `C0824RefOnlyDebtTests`: `Recovery_confirms_only_recorded_sha_and_attempt`, `Disabling_gate_keeps_existing_recovery_alive` and `Absent_identity_directory_does_not_supersede_debt`.
- `C0824RefOnlyReadTests`: `Reply_progress_counts_confirmed_range_never_main_head`, `Long_report_retains_text_without_materializing_identity` and `Check_distinguishes_creation_baseline_from_remote_observation`.

The bound: the only test that passes `sourceRoot:` to `CompletedCardCleanupFixture.CreateAsync` failed (`grep` across `tests/` finds exactly one such call site, `C0824RefOnlyCleanupTests.cs:260`). Every sibling that uses the default checkout-relative root passed.

## Comparison with run 6591e378 (rc/2026-11-15 = ac6d00022)

On the final-result note (`origin/feat/card-task-d6579463` `7a3c45c3f`, `docs/investigations/2026-11-11-run-6591e378-final-result.md`), the C0824 failure listed is only group 56 (`C0824RefOnlyReceiptTests` timeout). That run's `antiphon-all-group-96.trx` records this exact test as **Passed**, duration `00:00:33.8079355`, start 2026-11-10T14:25:34Z. It ran both loop arms in full, the temp-source arm included.

## What changed since ac6d00022

- `tests/Antiphon.Tests/Application/C0824RefOnlyCleanupTests.cs`: no change. The temp-source loop arm (`temporarySource`) has been present since `c8e61a69d` (CARD-0824 S5b-2, 2026-11-06), which is an ancestor of `ac6d00022`.
- `tests/Antiphon.Tests/TestHelpers/CompletedCardCleanupFixture.cs`: three CARD-1230 commits, all applied to master on 2026-11-11 03:32Z (committer time) and so present on `rc/2026-11-17`:
  - `29a9a58d9`
  - `bb5b2654b` (red)
  - `d912ed6a0` (fix): "C1017 fixture refuses a root under the OS temp directory by name". This added `TempCheckoutRefusal(root, Path.GetTempPath())` in the constructor and throws for any source root under temp, **whether or not an explicit `reportRoot` is passed**.

The stated cause of CARD-1230 is that `AgentReportStore` refuses a *report root* under temp, and that the fixture's report root is derived from its checkout. When the caller passes an explicit persistent `reportRoot`, that cause is absent. C0824 R-5 deliberately pairs a temporary source with a persistent report root to prove that reports land in the persistent location (`fixture-owned sibling of the Code checkout, outside TEMP`). The guard therefore over-refuses.

## Fix (this branch)

- `5d1868e39`, red: `CardDoneWorktreeCleanupTests.C1230_TempSourceWithPersistentReportRootIsAdmitted` asserts three cases:
  - a temp source with a persistent `reportRoot` is admitted;
  - an explicit temp `reportRoot` is refused as `c1017_report_root_under_temp`;
  - a temp source with no `reportRoot` is still refused as `c1017_checkout_under_temp`.

  `TempCheckoutRefusal` gained an ignored `reportRoot` parameter, so the first assertion is red.
- Fix commit: `TempCheckoutRefusal(root, temp, reportRoot)` judges the effective report root. An explicit `reportRoot` is judged on its own. Without one, the checkout is judged, which keeps CARD-1230's behaviour unchanged. The constructor passes `reportRoot`. The refusal message now names both the report root (or "derived from source root") and the source root, so a future refusal still says which path tripped it.

Assertions are not weakened. `C1230_TempCheckoutIsRefusedByName` is unchanged and keeps passing, because its 2-argument calls take the derived-root branch. No budget change: this is not a timing failure.

## Verification

No build or test was run in this task. The brief was read-only on the desktop with a live qualification, and a build would need a host build slot. Native Windows proof is owed:

- `/*/*/CardDoneWorktreeCleanupTests/(C1230_TempCheckoutIsRefusedByName*)|(C1230_TempSourceWithPersistentReportRootIsAdmitted*)` (red at `5d1868e39`, green at the fix)
- `/*/Antiphon.Tests.Application/C0824RefOnlyCleanupTests/*` (expect 2 passed, 1 Linux-only skip on Windows)
