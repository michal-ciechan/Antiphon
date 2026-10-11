# CARD-1155 dispatch-warning E2E roster

Plan date: 2026-10-11. This is the later roster for the existing
`DispatchBaseWarningDeliveryE2ETests` methods. The closed batch plan
`docs/superpowers/plans/2026-10-08-test-hardening-batch-1137-1134-1130-1126-1152-plan.md`
stays the executed S2-S4 roster. This file does not add a method to that batch,
does not edit its checkpoint table, and does not edit production.

CARD-0782 still owns known red native delivery rows. CARD-0576 still owns the
unproven second-claim receipt. Neither is this roster gap.

## Premise

`tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs` already contains the
end-to-end methods. The batch plan's `### Checkpoints` table does not name that
class. Its `DispatchBaseNotificationTests` rows insert queue and transcript rows
directly. Those inserts are not acceptance for this roster.

## Discharge

Each CP-2..CP-13 row is discharged only by a complete UserPrompt transcript from
the real producer to the recipient. Queue insert, Sent, or ack alone does not
discharge that row. The existing methods reach that transcript through
`LandDeliveryFixture.AssertDispatchReceiptsAsync`, which calls `ReceiptAsync` and
requires `TranscriptKinds.UserPrompt` plus `PromptSubmissionMatch.IsCompleteIn`
for every committed intent. CP-1 does not discharge CP-2..CP-13.

## Platform

CP-2..CP-13 are a Windows witness. `LandDeliveryFixture.InitializeAsync` requires
Windows, a located ConPTY redistributable, and staged `fakegrok.exe`. A Linux run
cannot execute those rows. Do not mark them passed from a Linux CP-1 result.

## Cost basis

CARD-0550 CP-24 budgets one `DispatchBaseWarningDeliveryE2ETests` method at 4
minutes with the E2E build reused. CARD-0550 CP-22 budgets five of those methods
at 8 minutes. CP-2 uses 8 minutes because that row pays for the isolated E2E
build and one method. CP-3..CP-13 use 4 minutes, the CP-24 one-method budget.
CP-1 uses 2 minutes for the unit pin. These are published budgets, not a new
measurement on this host.

## Non-goals

No new E2E method. No production change, migration, or AppHost restart. No
positive control: this roster adds no production behavior to mutate.

## Verification design

V-1. `DispatchBaseWarningRosterTests.C1155_RosterNamesEveryDispatchWarningE2EMethod`
reads the current `[Test]` methods and the table below. It fails when any method
lacks exactly one E2E filter, when a filter names an unknown method, when the
discharge sentence, the Windows witness sentence, or the CARD-0550 CP-24 cost
citation is missing, or when the closed batch checkpoint section names this E2E
class.

V-2. On Windows, each named method stores one complete UserPrompt transcript per
committed dispatch intent from the real producer to the recipient. Queue insert,
Sent, or ack alone does not discharge V-2.

### Checkpoints

One isolated build for the unit pin and one isolated E2E build for the Windows
rows. Reuse the E2E build only inside S1. `Min` is TUnit results. CP-2..CP-13
run on Windows; CP-1 runs on Linux.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1155/` | roster-pin | `/*/*/DispatchBaseWarningRosterTests/C1155_RosterNamesEveryDispatchWarningE2EMethod*` | V-1 | exact 1 results, 0 failed/skipped | 1 | 2 | true |
| CP-2 | S1 | `tests/Antiphon.E2E -> bin-c1155e/` | e2e-idle | `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_CollapsedWarningsReachIdleCaller*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 8 | true |
| CP-3 | S1 | `CP-2` | e2e-mismatch-reach | `/*/*/DispatchBaseWarningDeliveryE2ETests/C508_MismatchWarningReachesCaller*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 4 | true |
| CP-4 | S1 | `CP-2` | e2e-mismatch-wait | `/*/*/DispatchBaseWarningDeliveryE2ETests/C508_MismatchWarningWaitsForCaller*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 4 | true |
| CP-5 | S1 | `CP-2` | e2e-busy | `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_CollapsedWarningsWaitForBusyCaller*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 4 | true |
| CP-6 | S1 | `CP-2` | e2e-claim-crash | `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_ClaimCrashRecoversCollapsedWarnings*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 4 | true |
| CP-7 | S1 | `CP-2` | e2e-projection-crash | `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_ProjectionCrashRecoversOriginalPairs*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 4 | true |
| CP-8 | S1 | `CP-2` | e2e-pre-enqueue | `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_PreEnqueueCrashRecoversWarnings*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 4 | true |
| CP-9 | S1 | `CP-2` | e2e-enqueue-fail | `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_EnqueueFailureRetriesWarnings*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 4 | true |
| CP-10 | S1 | `CP-2` | e2e-queue-insert | `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_QueueInsertCrashReusesRows*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 4 | true |
| CP-11 | S1 | `CP-2` | e2e-pre-typing | `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_PreTypingCrashRecoversWarnings*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 4 | true |
| CP-12 | S1 | `CP-2` | e2e-post-prompt | `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_PostPromptCrashDoesNotRetype*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 4 | true |
| CP-13 | S1 | `CP-2` | e2e-receipt-crash | `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_ReceiptCrashDoesNotRetype*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 4 | true |

### Cost

Ordinary Code floor is 54 minutes, the sum of `EstimatedMinutes`. A Windows run
of CP-2..CP-13 is that floor minus CP-1. A Linux roster-pin run is CP-1 only and
leaves V-2 pending.

## S1

Add this roster and `DispatchBaseWarningRosterTests`. AppHost restart: no.

PCs: none.
