# Why missed: e2e-split-7 land delivery enqueue counter

One committed note for the rc/2026-11-15 run 6591e378 chunk `e2e-all-split-7` failure of `AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix_Batch2(enqueue-exception)`. The Windows TRX extract is `docs/investigations/2026-11-10-run-6591e378-e2e-split-7-land-delivery-windows-failure.md` on `origin/feat/card-task-e0c86bd2`. The failure message was `TimeoutException: two persisted review enqueue failures` at `ProgressAwareWait.cs:36`, called from `AgentTaskLandDeliveryE2ETests.cs:430`. Duration `00:04:08.0607910`. The sibling argument `queue-before-commit` passed in the same chunk (`00:03:19.2410888`).

Classification: test fixture defect. The product retry path already records an `IOException` from `before-enqueue` and leaves `QueueMessageId` null (`AgentTaskLandNotificationRecoveryTests.C467_V08`). This run did not fail because Windows skipped the class, and it did not fail because the 180 second bound was short. Once another note has used both throws, the review note never sits at `EnqueueAttempts == 2` with a null queue id, so a longer wait cannot succeed.

## Introducing commit/card

introducing commit/card: `9792086f95db537da38285eb44bec3de4141211c` (CARD-0494)

That commit added `C488_ReviewDeliveryCrashMatrix(enqueue-exception)`. The arm maps to the `enqueue-errors` cut and waits until the review task's `TaskCompletion` note has `EnqueueAttempts == 2` and `QueueMessageId == null`. The cut itself counts the first two `before-enqueue` reaches in the process, whichever note they belong to. That counter was added earlier, without a card id in the subject, by `80ad0b1551d34cf2bfaec5096948260b5945b8bd`. CARD-0494 is where the review arm started requiring those two throws to land on one note.

The child host starts before the review task exists (`UseChildAsync`, then `StartReviewDelegateAsync`). It reconciles every due note. A caller note, a dispatch warning, or a retry that reaches `before-enqueue` first consumes a throw. The review note then enqueues successfully, and the predicate stays false for the whole 180 seconds. `queue-before-commit` does not use the counter, which is why that arm passed on the same process. About 68 seconds of the failed test were setup. The wait then used the full plain 180 seconds (`progress` was null, so the message has no quiet-window suffix).

The timing sidecar for run dce78ee0 on rc cut `7bee43614495` records this same method at 222.30 seconds and does not state pass or fail (`docs/investigations/2026-11-10-slow-chunk-trace-and-in-process-plan.md`). That is shorter than this failure's 248 seconds. No commit between `7bee43614495293afbae1b0f4add019d11dacb9c` and the failing rc tip `ac6d00022e940ee3b90584c7d313567b4491c498` changes this E2E project, `AgentTaskLandNotificationService`, or the hosted scan. The miss is the unscoped counter under load, not a new product change on this rc.

### Ruled out

- Docker outage 16:46Z–16:55Z. The chunk ran 19:05:30Z–19:12:59Z. It is not in `docs/investigations/2026-11-10-docker-outage-victim-chunks.txt` on `origin/feat/card-task-88c9a263` (that list is only `antiphon-all-group-115` through `118`). Testcontainers reached Docker Desktop and started Postgres `antiphon_e2e` in container `40299df7413c`. The land fixture still uses its own git checkout. The database side of this run was Docker.
- A product failure to increment `EnqueueAttempts`. The service catch records the `IOException`, sets `RetryPending`, and schedules the next attempt. The hosted scan does not have to do that itself. `C467_V08` already proves the seeded-note path.
- An expected Windows skip. The test ran on Windows and failed the wait. The class stays OptIn and Windows-gated.
- Raising the 180 second bound. The sibling arm settled, and a longer poll does not create two failures on the review note after another note has taken them.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Review `69a3408d-50e9-466a-a025-534dc9a486c9`, round Final, scope Full, clean. Subject `46c55ae6-22c5-4018-9154-478f8c49f4c1`, reviewed source `7e10fd9163b0f3c40659855cc0c906d3a54d9fb1`. Runs `20260928-181937-a527` and `20260928-204813-a968`, checkpoint tool, unlisted none. Selection: CP-1 `/*/*/AgentTaskLandApprovalRecoveryTests/*` 94/94, CP-2 `/*/*/AgentTaskLandPublicationTests/*` 63/63, CP-3 `/*/*/AgentTaskLandSourceFreshnessTests/*` 59/59, CP-4 `/*/*/AgentTaskLandDeliveryE2ETests/*` 49/52 (the third red, `C488_ApprovalReceiptSaveFailureNeverRetypes`, passed alone on rerun; the other two reds are the inherited CARD-0782 `C467_V28` pair), Unit `/*/*/*/*[Category=Unit]` 3431/3434 with three load flakes that passed alone. The report records all 24 V-3/R-3 review cases passing, including `C488_ReviewDeliveryCrashMatrix` 9/9.

## Outside the selection

why the failing test was outside that selection (lane/category/path): it was inside that CP-4 selection. The class is the E2E lane, category `OptIn`, path `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs`.

Review `69a3408d` ran the whole class on Windows and recorded the nine-arm matrix green. A green run does not show that the two throws belonged to the review note rather than happening to be the first two `before-enqueue` calls in the child. Later Linux pre-land selections leave the class out: the fixture asserts Windows, ConPTY, and a native `fakegrok.exe`, so server2 cannot run it, and `OptIn` keeps it off the ordinary Linux lanes. The existing `e2e-land-delivery` impacted-classes row is `windows: only` and its globs are the notification service files, not `tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs`. A fixture-only edit does not select that row. `e2e-land-delivery-probe` uses the same service globs.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs` -> `/*/*/LandDeliveryFixtureProbeTests/Enqueue_errors_count_only_the_named_task*`

The Windows class filter `/*/*/AgentTaskLandDeliveryE2ETests/*` is the real path and needs a Windows host. It cannot run on server2. The Linux probe fails while a foreign `before-enqueue` still throws once `enqueue-errors-task.txt` names another task. This note does not edit `tests/impacted-classes.json`. CARD-1801 owns adding the row, and that file's witness suite pins its contents.

## Exemption

same cause as an already written note: none

`docs/investigations/2026-11-09-why-missed-e2e-land-delivery.md` is a different failure (the hosted scan read the system clock, so `C467_V32(busy)` stayed `not yet enqueued`).

## Repair

Test fixture only, on `feat/card-task-7340be2f` from master `a69789c77865b2db502500ebd26d58f72a8013c0`. No product code changed. The 180 second wait is unchanged. `C467_V31` still uses the process-wide counter because it does not write the file.

- Red `b42f271840d6e69cf0aea68a8e6533436791e549`: `Enqueue_errors_count_only_the_named_task` 1 total, 1 failed. The foreign `before-enqueue` threw `IOException` `Owned notification insert failure`.
- Fix `bbe349ac4da9acb666f350b9f94d67f62a30730e`: when `enqueue-errors-task.txt` exists, only a `before-enqueue` whose task id matches the file (N or D form) counts toward the two throws. The review arm writes a placeholder id before the child starts, then replaces it with `ReviewTaskId` after the report is held and before it is released. Absent file, the first two reaches in the process still throw.
- Green at `bbe349ac4`, Linux: `/*/*/LandDeliveryFixtureProbeTests/(Enqueue_errors_count_only_the_named_task*)|(Enqueue_errors_still_count_the_first_two_when_unnamed*)` 2 total, 2 passed. The Windows E2E class was not run. This runner cannot host it.
