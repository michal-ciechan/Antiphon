# Native Windows scope A

All five scope-A Windows skip rows now execute and pass natively. The four exact method selections passed 53/53, and all seven requested/adjacent census guards passed: 60 passed, 0 failed, 0 NotExecuted. No production fix was needed.

Host: native Windows 10.0.19045, .NET 9.0.22, SDK 10.0.300. Base: `ade54222543381e2e1b31ad79d8ae33a4035f70b`. Work stayed on `feat/card-task-00ae50cf` under the supplied standing authority, without a further approval request.

## Before

Each run used `scripts/build-slot.ps1`, `dotnet run --no-build`, `OutputPath=bin-scopea00/`, `UseAppHost=false`, and the exact `/*/*/<Class>/<Method>` filter. Parameterized methods retain all their argument rows. Two isolated baseline builds passed: Antiphon.Tests 0 errors / 1380 warnings / 12m08.91s; Antiphon.SessionRunner.Tests 0 errors / 43 warnings / 55.70s.

| Method | Passed | Failed | NotExecuted | Duration |
|---|---:|---:|---:|---:|
| OutputCleanupTests.parent_symlink_does_not_delete_a_tracked_stamp | 0 | 0 | 1 | 3.975s |
| CheckpointOwnedOutputTests.changed_inventory_is_refused | 3 | 0 | 1 (`link`) | 12.690s |
| CheckpointScriptReaderDeadlineTests.script_reader_re_arms_its_stall_window_and_the_stamp_reports_unreadable_apart_from_tampered | 4 | 0 | 2 (`unreadable`, `tampered-beside-unreadable`) | 23.520s |
| CompletedCardWorkspaceRemovalTests.C1032_InspectionHoldShapes | 41 | 0 | 1 (`case-twin`) | 106.595s |

The parent-only baseline exits 8 because its sole discovered row is NotExecuted. The other valid selections exit 0. These skips, not assertion failures, are the before witnesses for the requested fixture/skip conversion.

The initial `fsutil.exe file setCaseSensitiveInfo <owned-scratch> enable` probe inside this worktree exited 1 with `Error: Access is denied.` Query confirmed that directory was not case-sensitive. This was not a host-wide capability verdict: the actual test-owned temporary directory later accepted the flag and the full case-twin row passed. No elevation or host feature change was used. The helper preserves an explicit CARD-1032 capability skip for hosts/directories where fsutil is unavailable or refuses; once fsutil succeeds, a failed twin arrangement fails the test instead of skipping.

Three initial runner invocations selected zero cases because the local evidence script flattened a one-row list into strings. These are invalid execution evidence, retained without credit. The corrected exact method selection above supersedes them; no product failure occurred.

## Evidence

Absolute root: `C:\Antiphon\worktrees\card-task-00ae50cf\.antiphon\windows-scope-a-00ae50cf`.

Valid before receipts are `baseline-parent`, `baseline-inventory`, `baseline-ledger`, and `baseline-valid-case-twin`; each contains `run.trx`, `console.log`, and `provenance.json` with full source SHA, assembly SHA-256, exact filter, exit, and UTC bounds. Build logs and `case-capability.json` are beside them. Generated receipts remain gitignored. No production stack or runner was changed.

The selected methods have no covering exclusions; WindowsUnitSkipRoster and the C1584 E2E roster do not inventory these methods. These files and `tests/test-execution-policy.json` are unchanged. No definition update, exclusion, or `-Update` was used. The policy continues to require these rows.

## Changes and native results

| Scoped row | Before | After | Native case duration |
|---|---|---|---:|
| OutputCleanupTests.parent_symlink_does_not_delete_a_tracked_stamp | NotExecuted | Passed | 4.624s |
| CheckpointOwnedOutputTests.changed_inventory_is_refused(link) | NotExecuted | Passed | 5.090s |
| CheckpointScriptReaderDeadlineTests / unreadable | NotExecuted | Passed | 8.999s |
| CheckpointScriptReaderDeadlineTests / tampered-beside-unreadable | NotExecuted | Passed | 7.667s |
| CompletedCardWorkspaceRemovalTests.C1032_InspectionHoldShapes(case-twin, case_ambiguous_content) | NotExecuted | Passed | 3.713s |

- `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs`: the Windows branch uses the existing bounded `cmd.exe /d /c mklink /J` fixture and verifies a reparse point. The tracked stamp remains byte-identical and the script must report `REFUSED:reparse-point`. The method now carries the assembly-local process limiter.
- `tests/Antiphon.Tests/Checkpoints/CheckpointOwnedOutputTests.cs`: the existing `link` argument creates a real Windows junction, verifies its reparse attribute, and checks that cleanup refuses it while retaining the junction, outside sentinel, and compiler payload. Cleanup removes the junction itself. No extra argument/test identity was introduced.
- `tests/Antiphon.Tests/Checkpoints/CheckpointScriptReaderDeadlineTests.cs`: Windows now runs the existing script's `New-Item -ItemType HardLink` fixture. The second NTFS hard link deterministically changes the link count to two without changing file bytes; the reader must report the exact multiple-links unreadable diagnostic, distinguish adjacent tampering, and leave bytes/index intact. No ACL denial or exclusive handle is used. `scripts/test-checkpoint-reader-deadline.ps1` is byte-identical to base.
- `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceInspectionRepairTests.cs`: enables case sensitivity only on the fixture's empty `bin` directory through fsutil, with bounded/awaited process custody. The native pass proves two distinct `Twin`/`twin` entries, `case_ambiguous_content`, ineligibility, and unchanged inspection digest/index/refs/store. Unsupported capability diagnostics name CARD-1032, the command, exit and error; they do not create a policy exclusion.

Linux symlink creation/deletion, fixture operations and assertions are retained; Windows-only branches add the new arrangements. The hard-link script is entirely unchanged. Linux execution was unavailable here and is not claimed. Other runtime skips were left intact.

Full method selections: parent 1/1, changed inventory 4/4, script reader 6/6, inspection shapes 42/42; all 0 failed / 0 skipped. Native suite durations were 8.060s, 20.927s, 37.905s, and 150.668s respectively.

| Census method | Passed / failed / skipped | Suite duration |
|---|---|---:|
| C1514LandingSplitBudgetTests.C1404_Group10MeasuredBudget | 1 / 0 / 0 | 12.906s |
| C1514LandingSplitBudgetTests.C1577_Group71MeasuredBudget | 1 / 0 / 0 | 11.919s |
| C1514LandingSplitBudgetTests.C1584_E2EExpandedBudget | 1 / 0 / 0 | 13.889s |
| C1514LandingSplitBudgetTests.C1410_MeasuredCostGroups | 1 / 0 / 0 | 65.223s |
| C1514LandingSplitBudgetTests.G2865_Group28Group65MeasuredBudget | 1 / 0 / 0 | 18.748s |
| WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates | 1 / 0 / 0 | 26.907s |
| ReleaseGatePlatformTests.LinuxOnlyWindowsExclusions | 1 / 0 / 0 | 40.731s |

These are narrow native receipts, not a Full rc-profile qualification.

## Build outcomes and attribution

Checkpoint and census runs verified `87727bd2167b71620e9bf937c457efb06307d3b4`: Antiphon.Tests build 0 errors / 1380 warnings / 10m43.66s. Binary hashes were re-read after the runner rebuild and matched all ten run records (`binary-readback.json`).

The first runner build at that commit failed with one introduced compile error: CS0104, `TimeoutException` ambiguous between System and TUnit. Its log is retained (`native-build-runner.log`, 50.48s compilation, 106s admission wait). This was a fixture compile mistake, not a product behavior finding. Commit `745e4b6c1ef2abae9a9f49cc2ed0ca7892ce3cd9` qualifies `System.TimeoutException`; no other file changed. Only the affected project was rebuilt, as required after a fix: 0 errors / 55 warnings / 2m22.49s. The 42-row runner result verifies this corrected SHA; its binary hash also matched before cleanup. No failing runtime assertion was weakened or skipped.

No product defect was observed in the executed native rows. The initial directory-specific fsutil refusal is retained separately from the successful actual fixture qualification.

## Commits and handoff

- `a25bc3ba33fca4d0c07fe2a0c9623db865e846c4`: baseline NotExecuted witnesses.
- `87727bd2167b71620e9bf937c457efb06307d3b4`: native arrangements and capability gate.
- `745e4b6c1ef2abae9a9f49cc2ed0ca7892ce3cd9`: runner compile correction.

Each slice was pushed to the assigned branch. This final report is a subsequent report-only commit. Generated logs, TRX, hashes and scripts remain gitignored. All 35 exact producer-owned `bin-scopea00` directories were verified inside this worktree, removed, and checked absent. The initial empty capability-probe directory was also removed; evidence remains.

The implementation merge probe against fetched `origin/rc/2026-10-27` at `ade54222543381e2e1b31ad79d8ae33a4035f70b` was clean (tree `1cb7e72dd64a4938825af931a5fb370d6d2a4b8e` at `87727bd2167b71620e9bf937c457efb06307d3b4`). The required fetched-master probe exited 1: conflicts in `.antiphon/task-af2b78c8.md`, `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`, `server/Application/Services/CapacityRecoveryService.cs`, `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceInspectionRepairTests.cs`, and `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs`. No merge, rebase, tag, publication, or host activation was attempted. Review/landing should target the rc line; master is not merge-clean.

For reruns, build each project once through `scripts/build-slot.ps1` with `--property:OutputPath=bin-scopea00/ --property:UseAppHost=false`; run `dotnet run --project tests/<project> --no-build` with the same properties, `-- --treenode-filter "/*/*/<Class>/<Method>" --report-trx`, also through the gate. The exact method names are above. The retained local `run-rows.ps1 -Phase <fresh-name> -Group core|census|runner` supplies the individual filters, fresh evidence directories, hashes, and logs after rebuilding. No broad/full-suite run or `-Update` is needed.

## Performance

Selections are method-scoped. Existing fixture setup/teardown, read/process bounds, and the Linux script are retained. The new one-shot fsutil process has a provisional ten-second custody bound, no retries, and is awaited after termination on timeout; its phase is not separately timed. TRX preserves per-case durations; build and memory-floor admission waits are separate in console logs. Fixture/IO phase splits are unavailable and are not claimed as zero. No selection or build exceeded 30 minutes.

--- next stage ---
next: review
handoff: Review the test-only native Windows conversion and retained before/after evidence; all five scoped rows passed, with no exclusions or production changes.
artifact: C:\Antiphon\worktrees\card-task-00ae50cf\.antiphon\task-00ae50cf.md
