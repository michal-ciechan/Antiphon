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

Each CP-2..CP-14 row is discharged only by a complete UserPrompt transcript from
the real producer to the recipient. Queue insert, Sent, or ack alone does not
discharge that row. The existing methods reach that transcript through
`LandDeliveryFixture.AssertDispatchReceiptsAsync`, which calls `ReceiptAsync` and
requires `TranscriptKinds.UserPrompt` plus `PromptSubmissionMatch.IsCompleteIn`
for every committed intent. CP-1 does not discharge CP-2..CP-14.

## Platform

CP-2..CP-14 are a Windows witness. `LandDeliveryFixture.InitializeAsync` requires
Windows, a located ConPTY redistributable, and staged `fakegrok.exe`. A Linux run
cannot execute those rows. Do not mark them passed from a Linux CP-1 result.

## Cost basis

CARD-0550 CP-24 budgets one `DispatchBaseWarningDeliveryE2ETests` method at 4
minutes with the E2E build reused. CARD-0550 CP-22 budgets five of those methods
at 8 minutes. CP-2 uses 8 minutes because that row pays for the isolated E2E
build and one method. CP-3..CP-14 use 4 minutes, the CP-24 one-method budget.
CP-1 uses 2 minutes for the unit pin. These are published budgets, not a new
measurement on this host.

CP-14 restores the later CARD-1359 slow-materialization witness. Its 4-minute
estimate provisionally reuses the CP-24 one-method budget; this Linux repair
does not execute it or establish p95. The Windows witness remains pending.

## Non-goals

No new E2E method. No production change, migration, or AppHost restart. No
positive control: this roster adds no production behavior to mutate.

## Verification design

V-1. `C1155_RosterNamesEveryDispatchWarningE2EMethod` reads this plan and
`tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs`. Discovery parses
that source with `CSharpSyntaxTree.ParseText` and fails when the parse has a
syntax error. Discovery reads that named E2E source file only, not a partial
class in another file and not generated code. It matches attribute simple names
`Test` and `TestAttribute`. A conditional-compilation directive makes the pin
fail closed. After parsing, `#if`, `#elif`, `#else`, or `#endif` trivia fails
the pin with a message that names the first such directive's line, and the pin
does not supply project preprocessor symbols. It records each public,
non-static method declared on class `DispatchBaseWarningDeliveryE2ETests` in
that file, once, when any attribute on that method has one of those simple
names, in any position and any list shape. A later attribute, a comma-combined
list such as `[Test, Timeout(...)]`, stacked `[Test][Category(...)]`, and
`[Arguments(...)]` before `[Test]` still record that method, once. A
declaration inside a line comment, a block comment, an ordinary string, a
verbatim string, a verbatim string that begins with an escaped quote, a `'"'`
character literal, a raw string literal, or an interpolated string is not
recorded, and a following public test method stays recorded. The same parser
is what `C1155_ExtraAttributeAfterTestIsDiscovered`,
`C1155_CommentedOutDeclarationIsNotCounted`,
`C1155_BlockCommentMethodIsNotCounted`,
`C1155_RemovedAndRenamedMethodDiffersFromTheRoster`,
`C1155_VerbatimEscapedQuoteDoesNotHideTheNextTest`,
`C1155_CharLiteralQuoteDoesNotHideTheNextTest`,
`C1155_RawStringDoesNotHideTheNextTest`,
`C1155_InterpolatedStringDoesNotHideTheNextTest`,
`C1155_ConditionalDirectiveFailsClosed`, and
`C1155_SourceWithoutConditionalDirectiveIsDiscovered` call. The pin fails when
the recorded names and the `DispatchBaseWarningDeliveryE2ETests` filter names
in the table below differ, when one of those filter names is repeated, when
this plan lacks the discharge sentence or the Windows witness sentence, when
the `## Cost basis` section lacks its four-minute one-method budget citation
(the same words in any other section do not count), when the closed batch
checkpoint section contains this E2E class name, or when that E2E source
contains a conditional-compilation directive. It does not compile the E2E
project, a non-public or static method is not a roster row, and a passing pin
does not pass V-2. CP-2..CP-14 stay unrun on Linux and V-2 stays pending.

V-2. On Windows, a named method stores one complete UserPrompt transcript per
committed dispatch intent from the real producer to the recipient. Queue insert,
Sent, or ack alone does not discharge V-2. V-2 stays pending: CP-2..CP-14 are
Windows rows, and this roster-pin repair does not run them or mark V-2 passed.

### Checkpoints

One isolated build for the unit pin and one isolated E2E build for the Windows
rows. Reuse the E2E build only inside S1. `Min` is TUnit results. CP-2..CP-14
run on Windows; CP-1 runs the roster class on Linux.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1155/` | roster-pin | `/*/*/DispatchBaseWarningRosterTests/*` | V-1 | exact 11 results, 0 failed/skipped | 11 | 2 | true |
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
| CP-14 | S1 | `CP-2` | e2e-slow-materialization | `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_PreEnqueueWaitSurvivesSlowMaterialization*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 4 | true |

### Cost

Ordinary Code floor is 58 minutes, the sum of `EstimatedMinutes`. A Windows run
of CP-2..CP-14 is that floor minus CP-1. A Linux roster-pin run is CP-1 only and
leaves V-2 pending. This repair does not run CP-2..CP-14, so V-2 stays pending.

## S1

Add this roster and `DispatchBaseWarningRosterTests`. AppHost restart: no.

PCs: none.
