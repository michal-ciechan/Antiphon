# Native Windows scope A

Baseline witnesses captured at `ade54222543381e2e1b31ad79d8ae33a4035f70b` on native Windows 10.0.19045, .NET 9.0.22, SDK 10.0.300. Implementation and after-verification are pending.

## Before

Each run used `scripts/build-slot.ps1`, `dotnet run --no-build`, `OutputPath=bin-scopea00/`, `UseAppHost=false`, and the exact `/*/*/<Class>/<Method>` filter. Parameterized methods retain all their argument rows. Two isolated baseline builds passed: Antiphon.Tests 0 errors / 1380 warnings / 12m08.91s; Antiphon.SessionRunner.Tests 0 errors / 43 warnings / 55.70s.

| Method | Passed | Failed | NotExecuted | Duration |
|---|---:|---:|---:|---:|
| OutputCleanupTests.parent_symlink_does_not_delete_a_tracked_stamp | 0 | 0 | 1 | 3.975s |
| CheckpointOwnedOutputTests.changed_inventory_is_refused | 3 | 0 | 1 (`link`) | 12.690s |
| CheckpointScriptReaderDeadlineTests.script_reader_re_arms_its_stall_window_and_the_stamp_reports_unreadable_apart_from_tampered | 4 | 0 | 2 (`unreadable`, `tampered-beside-unreadable`) | 23.520s |
| CompletedCardWorkspaceRemovalTests.C1032_InspectionHoldShapes | 41 | 0 | 1 (`case-twin`) | 106.595s |

The parent-only baseline exits 8 because its sole discovered row is NotExecuted. The other valid selections exit 0. These skips, not assertion failures, are the before witnesses for the requested fixture/skip conversion.

`fsutil.exe file setCaseSensitiveInfo <owned-scratch> enable` exits 1 with `Error: Access is denied.` Query confirms case sensitivity is disabled. The brief explicitly permits a capability gate for this row; no elevation or host feature change is attempted.

Three initial runner invocations selected zero cases because the local evidence script flattened a one-row list into strings. These are invalid execution evidence, retained without credit. The corrected exact method selection above supersedes them; no product failure occurred.

## Evidence

Absolute root: `C:\Antiphon\worktrees\card-task-00ae50cf\.antiphon\windows-scope-a-00ae50cf`.

Valid before receipts are `baseline-parent`, `baseline-inventory`, `baseline-ledger`, and `baseline-valid-case-twin`; each contains `run.trx`, `console.log`, and `provenance.json` with full source SHA, assembly SHA-256, exact filter, exit, and UTC bounds. Build logs and `case-capability.json` are beside them. Generated receipts remain gitignored. No production stack or runner was changed.

The six requested census guards and `LinuxOnlyWindowsExclusions` remain to be executed after the fixture changes. The selected methods have no covering exclusions; WindowsUnitSkipRoster and the C1584 E2E roster do not inventory these methods. No policy/definition update is planned, and no `-Update` is permitted.

## Performance

Selections are method-scoped. The existing fixture setup/teardown, read/process bounds, and Linux script are retained. TRX preserves per-case duration; build and slot waits are separate in console logs. Fixture/IO phase splits are unavailable and are not claimed as zero. No selection or build exceeded 30 minutes. Linux execution is unavailable on this host.
