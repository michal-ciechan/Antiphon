# Why missed: C1584 E2E source roster

One committed note for the rc/2026-11-14 group-130 failure of `C1514LandingSplitBudgetTests.C1584_E2EExpandedBudget` (run dce78ee0). CARD-1781.
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

Run dce78ee0, chunk `antiphon-all-group-130.trx`, rc/2026-11-14 = `7bee43614495293afbae1b0f4add019d11dacb9c`. Windows read-out: `docs/investigations/2026-11-09-run-231f573c-landing-split-budget-windows-failure.md` on `feat/card-task-264d1050` at `8475954454dcd09cd49b972c6cd61054b3078ef2` (the file keeps the 231f573c name; the extract is dce78ee0).

## Failure

`C1514LandingSplitBudgetTests.C1584_E2EExpandedBudget` (Integration). Windows duration 3.09s. Linux at master `965ab080395310c957b04e3ea9088c5f04534670`: the class was 5 total, 1 failed, 4 passed, in 1m 04s. The failure is `ShouldAssertException: result.ExitCode should be 0 but was 1` at `tests/Antiphon.Tests/Scripts/ScriptHarness.cs:25`. The harness child threw at `scripts/test-nightly-tests.ps1:1981`:

`expanded-source-roster-changed: added=Antiphon.E2E.Fixtures.AntiphonAppFixtureHostPathTests|Every_windows_path_setting_resolves_inside_the_fixture_workspace|;Antiphon.E2E.Fixtures.AntiphonAppFixtureHostPathTests|Fixture_boot_creates_no_backslash_named_entry_and_teardown_removes_the_interpreter_directory|;Antiphon.E2E.Fixtures.AntiphonAppFixtureHostPathTests|Fixture_boot_leaves_the_user_claude_config_byte_identical_and_seeds_its_own|;Antiphon.E2E.LandDeliveryFixtureProbeTests|Notification_scan_reads_the_land_clock| removed=`

No timeout, no git call, no kill. The other four methods in the class passed. This is a stale source-roster oracle, not a launch-budget breach and not a product defect.

The same roster, census (188/181) and four methods are already on rc/2026-11-13 `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`. Nothing in this class, `scripts/test-nightly-tests.ps1`, `scripts/lib/nightly-class-costs.json` or the roster changed between that tip and `7bee43614`. Run 231f573c's dispatched failures were groups 2, 4, 71, 96 and 117. No group-130 failure was dispatched for that run, and the desktop extract read dce78ee0 rather than the 231f573c TRX. A run of this test on the 653f tree fails the same assertion.

## Introducing commit/card

introducing commit/card: `9f009f299fb524ab241d4be8d82b66ab1b39e505` (CARD-1795)

That commit adds `AntiphonAppFixtureHostPathTests.Every_windows_path_setting_resolves_inside_the_fixture_workspace` and `Fixture_boot_creates_no_backslash_named_entry_and_teardown_removes_the_interpreter_directory`. It does not touch the C1584 roster. Cherry-pick of `f22885514678e139998aa1797e88760915ea84ff`.

Further identities, same cause:

- `5aee4e8e813b21415601c49c4560cf016b5e383c` (CARD-1797) adds `Fixture_boot_leaves_the_user_claude_config_byte_identical_and_seeds_its_own`. Cherry-pick of `b36df72e44424de2fbdd480633fdc4fb3faa8e14`.
- `906533773a18b5b06d21fa43a3127dcb9839fdf7` adds `LandDeliveryFixtureProbeTests.Notification_scan_reads_the_land_clock`. Cherry-pick of Debug `61d7498e` red `026f497d053536bc42d943d7889a4302f75e0d75` (rc a6410818 e2e-split-5). No card of its own.

`3cc9e471cda848d1f9f8d2127a26ffecbc10d864` (CARD-1795) changes provisioning wait inside the class and does not add a roster identity. The roster's last edit before this fix is `4c12963577c8b9bca176d76609f942475237cfc5` (2026-11-04).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Review `b978e850-b8b3-4943-a0ac-6e4f185dd5aa` (CARD-1795, Final round 1, outcome Clean, `ordinaryScopeCompleted: Full`, reviewed `1984b39e284bb06f14a4565ec11c31632e502e89`, `reviewedSourceClean: true`). Checkpoint `20261108-194328-9497`: `ContractSnapshotTests` 5, `AntiphonAppFixtureHostPathTests` 2/2, E2E `TestClassificationGuardTests` 1, `MergeHighRadiusSubsetTests` 5, guard batch 52. 65 passed, 0 failed. The selection does not name `C1514LandingSplitBudgetTests`.
- Review `c69c6449-4878-4caa-8c1e-988d2b6c3287` (CARD-1797, Final round 1, outcome Clean, `ordinaryScopeCompleted: Full`, reviewed `a643f2b60b4e6867d7a05ed0ce4a671b7599f280`, `reviewedSourceClean: false` because the runs were direct build-slot runs). Provisioner classes 37/37, guard batch 43/52 with 9 inherited `WriteBehindFileSystemTests` failures, `AntiphonAppFixtureHostPathTests` plus `ContractSnapshotTests` 8/8. The selection does not name `C1514LandingSplitBudgetTests`.
- `906533773` had no Review. Debug `61d7498e` landed the probe by Merge direct. The review it cites for the underlying clock change is `53194ebc` (CARD-1759, round 1, Full, clean, at `40cd61ba8`): ReconcileLoad 4/4, Recovery 49/49, Persistence 12/12, guard batch 75/75. That review predates the probe and does not name `C1584_E2EExpandedBudget`. Recorded in [2026-11-09-why-missed-e2e-land-delivery](2026-11-09-why-missed-e2e-land-delivery.md).

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane, category `Integration`, path `tests/Antiphon.Tests/Scripts/C1514LandingSplitBudgetTests.cs`.

`C1584_E2EExpandedBudget` is the source census of `tests/Antiphon.E2E/**/*.cs`. The reviews selected the new E2E classes and the guard batch. They did not select the Antiphon.Tests harness that compares that tree to `scripts/fixtures/nightly/c1584-e2e-source-roster.txt`. A Unit filter does not match `[Category("Integration")]`. The new methods are `[Category("OptIn")]` in the E2E assembly, so an Antiphon.Tests touched-class filter on the E2E diff does not include them, and an E2E touched-class filter does not include `C1514LandingSplitBudgetTests`.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.E2E/**/*.cs` -> `/*/*/C1514LandingSplitBudgetTests/C1584_E2EExpandedBudget*`

Proposed CARD-1801 row (not applied; `ImpactedClassesSelectorTests` pins `tests/impacted-classes.json`):

```json
{
  "id": "e2e-source-roster",
  "globs": ["tests/Antiphon.E2E/**/*.cs"],
  "methods": ["C1514LandingSplitBudgetTests/C1584_E2EExpandedBudget"]
}
```

Also map `scripts/fixtures/nightly/c1584-e2e-source-roster.txt` and `scripts/lib/nightly-class-costs.json` to that same method. One method is enough: it throws `expanded-source-roster-changed` with the added and removed identities before the budget checks.

## Exemption

same cause as an already written note: none

The clock miss is a different cause: [2026-11-09-why-missed-e2e-land-delivery](2026-11-09-why-missed-e2e-land-delivery.md). That note's new probe is one of the four identities this roster did not record. The task-load window note `docs/investigations/2026-11-08-why-missed-c1514-task-load-window.md` is `C1514TaskLoadWindowTests`, a different class.
