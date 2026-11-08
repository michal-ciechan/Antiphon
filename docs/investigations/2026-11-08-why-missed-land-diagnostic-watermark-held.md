# Why missed: land diagnostic, receipt watermark, held notification

One committed note for rc/2026-11-10 run e737e9fe groups 62, 67, and 68. The Windows TRX and log at `C:\Antiphon\releases\candidates\rc-2026-11-10-74a8004f405b\logs\2026-11-08-0144-e737e9fe\` were not readable from this runner. Linux reproduced the four classes at rc tip `74a8004f405bc54666fea33889839f0ff4d62ec7` and at master `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`.

The sibling persistence note is `docs/investigations/2026-11-08-why-missed-land-notification-persistence.md`. The held failure below is that same `Down` refusal on a second class. No `CARD-1782` impacted-classes table exists under `docs/` yet; the path-glob entries below are the rows that table should carry.

## Held notification (group 67, 1 failure)

introducing commit/card: `c53a0fe802a147da9f2dc5ae1579e92438aea60d` (CARD-1514)

`HostLoadRetentionCatchup.Down` throws `NotSupportedException` ("Retention catch-up state cannot be discarded by a downgrade.") at `server/Migrations/20261107104114_HostLoadRetentionCatchup.cs`. The commit is on the rc tip and not on the previous cut `0e7aef0379a073c206e8398dcaed539bc33eaf9b`.

At the rc tip, `C641_Upgrade_preserves_existing_held_notification_debt` still migrates a current database down through that `Down`. Linux at the rc tip: the four-class filter was 92 total, 23 failed. This method's message was that `NotSupportedException`. Linux at master: the same method passed inside the four-class run (21 failed, and this method was not among them).

approving review(s): the same CARD-1514 reviews recorded in the sibling persistence note. `8da334c5` Interim (pre-catch-up; CP-M1 `C1514HostLoadMigrationTests`, CP-M2 `C0824RefOnlySnapshotMigrationTests`). `d56a0975` Interim, not clean. `a28cac94` Interim, clean for the D-1 diff only, which does not touch a migration. `c7a30141` master integration CLEAN, rerunning those migration rows. None named `AgentTaskLandHeldNotificationTests`.

why this test was outside that selection: Integration, path `tests/Antiphon.Tests/Application/AgentTaskLandHeldNotificationTests.cs`. The closed packet is the host-load migration classes. `C641_Upgrade_preserves_existing_held_notification_debt` is the other caller of the downgrade, and the introducing commit does not touch its file.

cheapest pre-land check: `server/Migrations/*.cs` -> `/*/*/AgentTaskLandHeldNotificationTests/C641_Upgrade_preserves_existing_held_notification_debt*`

Already on master. Repair `11cbc60027f2514145abfd673fcf49c11908c62d` ("test: rewind host-load clones without calling Down") calls `DetachHostLoadAsync` before the rewind. This task did not edit that file. No assertion was loosened.

## Receipt watermark and safety (groups 68 and 67, 10 + 5 failures)

introducing commit/card: `4d91773c122186930a3da485dd2a3221a1e960cd` (CARD-1759, cherry-pick of `bf6d0e263e019654471b84808eafe95e979e8f24`)

On the rc tip. `LoadNoteAsync` reloads an already-tracked note with one `ReloadAsync` and does not also run the old `Single`. The watermark harness keeps the note tracked on the service context, so every measured pass is one command under the old budget. Receipt-select counts did not change. Linux at master, before any edit here: the same 15 command-budget failures as at the rc tip, each exactly one command low per pass (`[6,5,5]` was `[5,4,4]`, and the same shape on the other arms). Not a product defect. The review judged the tracked path "minus the redundant first SELECT" and treated the command outcome as unchanged.

A second oracle, visible only after those command totals match, is the session-projection counter. `593ba300b711fca9708d967ecac5b0dc3eb66325` (CARD-1769, after the rc tip) leads the compiled `SELECT a."Status", a."StartedAt" FROM "AgentSessions"` with `-- land-reconcile-compiled`. The counter required the text to start at `SELECT`, so it counted 0 while the SELECT still ran. The planned projection fault used the same prefix and therefore never threw. That is why `ReceiptScanDestinationIntegrationTests` (not in these Windows groups) failed the same command budget, and why its fault arm then saw a receipt select: the fault never armed. The SELECT shape and the receipt-select assertions stay required.

approving review(s): Review `53194ebc`, round 1, Full, CLEAN. Subject `40cd61ba85cdafcad88f7a7406222b3ec0367b95`, range `8b75f315467f7b25c021ec1c0299c04e7398c0b8..40cd61ba85cdafcad88f7a7406222b3ec0367b95`. The report is commit `2b2d8cf819` (`.antiphon/task-53194ebc.md`). Selection actually run: CP-1 `/*/*/AgentTaskLandNotificationReconcileLoadTests/*` 4/4, CP-2 `/*/*/AgentTaskLandNotificationRecoveryTests/*` 49/49, CP-3 `/*/*/AgentTaskLandNotificationPersistenceTests/*` 12/12, CP-4 guard batch 75/75. It states the Unit lane was not run. No later review round of that subject was found. No plan file for CARD-1759 is on master.

why these tests were outside that selection: Integration. Paths `tests/Antiphon.Tests/Application/AgentTaskLandReceiptWatermarkTests.cs`, `AgentTaskLandReceiptWatermarkSafetyTests.cs`, and `ReceiptScanDestinationIntegrationTests.cs`. The diff did not touch those files, so a touched-class selection skips them. The guard batch does not execute them either.

cheapest pre-land check: `server/Application/Services/AgentTaskLandNotificationService.cs` and `server/Application/Services/LandNotificationReconcileQueries.cs` -> `/*/*/(AgentTaskLandReceiptWatermarkTests*)|(AgentTaskLandReceiptWatermarkSafetyTests*)|(ReceiptScanDestinationIntegrationTests*)/*`

## Failure diagnostic (group 62, 2 failures)

introducing commit/card: `ad30d2122f48988ec1e2407b62db70c6cf39203a` (CARD-0498)

`C498_TerminalTransactionFaultMatrix` and `C498_ReplacementBeforeFailureLock` slept a fixed 2 seconds and then read the log or the diagnostic id. The rest of the class already polls for 30 seconds (`WaitTerminalAsync`). `AgentTaskLandHostedService.cs` is unchanged from the rc tip to master. The land service diff in that range is only the worktree inspection timeout default. This is not a product change on this rc. It is a fixture race the loaded rc host tripped. Linux at master failed six of those timing cases (the log or `FailureDiagnosticId` was not visible at 2 seconds). Linux at the rc tip failed the same six, plus `C498_FailureAfterPublicationRetainsPublication(landed)` with `TimeoutException: terminal event` from the existing 30-second poll. That extra case passed at master and passed again on the green rerun (`already-present` timed out once on the first fix run and passed on the rerun). The 30-second cap was not widened.

approving review(s): no review on this rc re-ran these methods. The class file did not change between the rc tip and master, so a touched-class selection of later cards would not name it.

why this test was outside that selection: it was not outside a selection of a new diff. The sleep has been in the class since CARD-0498. A quiet host finishes inside 2 seconds, so the race stays green until the host is loaded.

cheapest pre-land check: `tests/Antiphon.Tests/Application/AgentTaskLandFailureDiagnosticTests.cs` -> `/*/*/AgentTaskLandFailureDiagnosticTests/*`. That filter passes on a quiet host. The check that matches the failure is to wait for the persist-failure log or `FailureDiagnosticId`, which is what the repair does, with the same 30-second cap `WaitTerminalAsync` already uses.

## Repairs on this branch

Test-only. Product code was not edited.

- `c6706894da719cc37c69f23fbeabb6a66e35da4b` retargets the watermark and safety command budgets by one per pass, and replaces the two 2-second sleeps with a 30-second condition wait. Receipt-select and transcript assertions were not changed.
- `776e3c800620718d1c19e9702d54919d0842c26f` counts a session projection when the compiled comment leads the same SELECT.
- `d9feb7fcefa4a7645a7a72ddfd0f0d8bf351ca85` retargets `ReceiptScanDestinationIntegrationTests` command budgets the same way.
- `b44a060339e15e2099b758e308170573577eb419` makes the planned projection fault recognize that tagged SELECT.

Linux at `776e3c800620718d1c19e9702d54919d0842c26f`: the four-class filter, 92 passed, 0 failed. Linux at `b44a060339e15e2099b758e308170573577eb419`: `ReceiptScanDestinationIntegrationTests` 23 passed, 0 failed; the CARD-1759 guard batch 75 passed, 0 failed.
