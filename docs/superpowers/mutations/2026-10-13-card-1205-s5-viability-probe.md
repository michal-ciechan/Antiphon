# CARD-1205 S5 witness viability probe

All commissioned family controls produced assertion RED; no vacuous witness was found.

Nine independent mutant states: 31 executions, 15 intended assertion failures, 16 unaffected passes, zero skips. Baseline: 2/2 passed. Eleven isolated, slot-granted builds completed with zero build failures; restored witnesses passed 34/34.

Source: `715176d63e4486408345de2c86d9a30b78e26c5c`, task branch `feat/card-task-af6a833c`. This is an unsourced, scratch-only Test probe, not systematic Mutation. PC-84..100 and inherited controls remain pending. No production/test/fixture changes are delivered.

FC-7..9 cover manifest families absent from FC-1..6: lost volatile wakeup after transcript commit (T39), already-eligible producer wakeup (T42), and uncertain probes (T44). FC-5 also exercises busy authoritative tails (T41); FC-6 also exercises S5 invalid receipts (T45). Handoff variants and invalid-receipt variants unaffected by the single family defect are expected to remain green; this probe does not discharge their independent PCs. CP-6 metadata is a classification guard, not a production delivery family; it is not production-mutated. The compatibility controls use FC-6's commissioned whole-wire method.

| Family | Mutation | Exact witness method (class below) | Result (passed/failed/skipped) | Assertion message |
|---|---|---|---|---|
| FC-1 | Suppress Sync recovered-TurnEnd flush. | `C1205_Restart_delivers_five_gap_held_completions_once` | RED (0/2/0) | ShouldAssertException: Caller.SubmittedBodies.Count<br>    should be<br>5<br>    but was<br>0 |
| FC-2 | Admit the first watchdog probe only; suppress all later due admission. | `C1205_Watchdog_delivers_while_event_stream_remains_healthy` | RED (0/1/0) | ShouldAssertException: h.RecoveryOutcomes.Last()<br>    should be<br>"RecoveredIdle"<br>    but was<br>"CheckedWorking" |
| FC-3 | Retype the complete matched receipt before notifier confirmation, after the receipt-save fault boundary. | `C1205_Receipt_restart_confirms_without_retyping` | RED (0/1/0) | ShouldAssertException: Caller.SubmittedBodies.Count<br>    should be<br>1<br>    but was<br>2 |
| FC-4 | Return before assigning/saving the existing keyed QueueMessageId; abandon durable rejoin. | `C1205_Restart_recovers_each_delivery_handoff` | RED (14/2/0) | ShouldAssertException: note.State<br>    should be<br>LandNotificationState.Confirmed<br>    but was<br>LandNotificationState.RetryPending |
| FC-5 | Bypass the fresh Working condition in FlushIfIdleAsync. | `C1205_Busy_tail_keeps_completion_obligations_pending` | RED (0/1/0) | ShouldAssertException: rows<br>    should satisfy the condition<br>((r.Status == 0) && (r.DeliveryAttempts == 0))<br>    but<br>[Antiphon.Server.Domain.Entities.SessionQueuedMessage (11304450), Antiphon.Server.Domain.Entities.SessionQueuedMessage (41809607), Antiphon.Server.Domain.Entities.SessionQueuedMessage (66290627), Antiphon.Server.Domain.Entities.SessionQueuedMessage (10650535), Antiphon.Server.Domain.Entities.SessionQueuedMessage (63798331)]<br>    do not |
| FC-5 | Bypass the fresh Working condition in FlushIfIdleAsync. | `C1205_Flush_rechecks_a_new_turn_after_recovery` | RED (0/1/0) | ShouldAssertException: rows<br>    should satisfy the condition<br>((r.Status == 0) && (r.DeliveryAttempts == 0))<br>    but<br>[Antiphon.Server.Domain.Entities.SessionQueuedMessage (55630570)]<br>    do not |
| FC-6 | Accept an expected-wire prefix as receipt in LandNoteReceipt.IsReceipt. | `C544_CompletionReceiptWholeWire` | RED (0/1/0) | ShouldAssertException: refused.State<br>    should not be<br>LandNotificationState.Confirmed<br>    but was<br><br>Additional Info:<br>    header-only |
| FC-6 | Accept an expected-wire prefix as receipt in LandNoteReceipt.IsReceipt. | `C1205_Only_complete_destination_receipts_confirm` | RED (2/2/0) | ShouldAssertException: unconfirmed.State<br>    should not be<br>LandNotificationState.Confirmed<br>    but was |
| FC-7 | Omit boot-sweep ScheduleFlush for persisted pending completion rows. | `C1205_Restart_after_transcript_commit_recovers_lost_wakeup` | RED (0/1/0) | ShouldAssertException: Caller.SubmittedBodies.Count<br>    should be<br>5<br>    but was<br>0 |
| FC-8 | Drop notifier producer wakeups after keyed admission/rejoin. | `C1205_Already_eligible_caller_receives_completion` | RED (0/1/0) | ShouldAssertException: h.Flushes.Accepted<br>    should contain<br>615e676e-b784-4223-882c-0c956268c74f<br>    but was actually<br>[] (System.Collections.Concurrent.ConcurrentQueue`1[System.Guid])<br><br>Additional Info:<br>    the producer wakeup precedes any boot recovery of this obligation |
| FC-9 | Publish a scheduler wakeup for Unknown as well as RecoveredIdle. | `C1205_Unknown_probe_keeps_obligations_held` | RED (0/2/0) | ShouldAssertException: h.Flushes.Accepted<br>    should be empty but had<br>1<br>    item and was<br>[9fae8ebf-f2f1-4807-b8ef-6a8668a0b7ee] |

Filters are `/*/*/TurnEndGapRestartDeliveryTests/<exact method>*`, except `C544_CompletionReceiptWholeWire`, which uses `/*/*/VerificationRoundDeliveryTests/C544_CompletionReceiptWholeWire*`. Fresh TRX argument rows, not list-tests or exit codes alone, supply the counts. All failing messages in the TRX were inspected; fixture/build errors and timeouts are not credited as RED.

## Exact failing assertion messages

FC-1: `C1205_Restart_delivers_five_gap_held_completions_once(True)`, `C1205_Restart_delivers_five_gap_held_completions_once(False)`

```text
ShouldAssertException: Caller.SubmittedBodies.Count
    should be
5
    but was
0
```

FC-2: `C1205_Watchdog_delivers_while_event_stream_remains_healthy`

```text
ShouldAssertException: h.RecoveryOutcomes.Last()
    should be
"RecoveredIdle"
    but was
"CheckedWorking"
    difference
Difference     |  |    |    |    |    |         |    |    |    |    |    |    |    |   
               | \|/  \|/  \|/  \|/  \|/       \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  
Index          | 0    1    2    3    4    5    6    7    8    9    10   11   12   13   
Expected Value | R    e    c    o    v    e    r    e    d    I    d    l    e         
Actual Value   | C    h    e    c    k    e    d    W    o    r    k    i    n    g    
Expected Code  | 82   101  99   111  118  101  114  101  100  73   100  108  101       
Actual Code    | 67   104  101  99   107  101  100  87   111  114  107  105  110  103  
```

FC-3: `C1205_Receipt_restart_confirms_without_retyping`

```text
ShouldAssertException: Caller.SubmittedBodies.Count
    should be
1
    but was
2
```

FC-4: `C1205_Restart_recovers_each_delivery_handoff(note-committed, True)`, `C1205_Restart_recovers_each_delivery_handoff(note-committed, False)`

```text
ShouldAssertException: note.State
    should be
LandNotificationState.Confirmed
    but was
LandNotificationState.RetryPending
```

FC-5: `C1205_Busy_tail_keeps_completion_obligations_pending`

```text
ShouldAssertException: rows
    should satisfy the condition
((r.Status == 0) && (r.DeliveryAttempts == 0))
    but
[Antiphon.Server.Domain.Entities.SessionQueuedMessage (11304450), Antiphon.Server.Domain.Entities.SessionQueuedMessage (41809607), Antiphon.Server.Domain.Entities.SessionQueuedMessage (66290627), Antiphon.Server.Domain.Entities.SessionQueuedMessage (10650535), Antiphon.Server.Domain.Entities.SessionQueuedMessage (63798331)]
    do not
```

FC-5: `C1205_Flush_rechecks_a_new_turn_after_recovery`

```text
ShouldAssertException: rows
    should satisfy the condition
((r.Status == 0) && (r.DeliveryAttempts == 0))
    but
[Antiphon.Server.Domain.Entities.SessionQueuedMessage (55630570)]
    do not
```

FC-6: `C544_CompletionReceiptWholeWire`

```text
ShouldAssertException: refused.State
    should not be
LandNotificationState.Confirmed
    but was

Additional Info:
    header-only
```

FC-6: `C1205_Only_complete_destination_receipts_confirm(header-only)`, `C1205_Only_complete_destination_receipts_confirm(truncated)`

```text
ShouldAssertException: unconfirmed.State
    should not be
LandNotificationState.Confirmed
    but was
```

FC-7: `C1205_Restart_after_transcript_commit_recovers_lost_wakeup`

```text
ShouldAssertException: Caller.SubmittedBodies.Count
    should be
5
    but was
0
```

FC-8: `C1205_Already_eligible_caller_receives_completion`

```text
ShouldAssertException: h.Flushes.Accepted
    should contain
615e676e-b784-4223-882c-0c956268c74f
    but was actually
[] (System.Collections.Concurrent.ConcurrentQueue`1[System.Guid])

Additional Info:
    the producer wakeup precedes any boot recovery of this obligation
```

FC-9: `C1205_Unknown_probe_keeps_obligations_held(pull-fault)`

```text
ShouldAssertException: h.Flushes.Accepted
    should be empty but had
1
    item and was
[157bb595-e8e2-4990-8785-1b7fd7bec5e8]
```

FC-9: `C1205_Unknown_probe_keeps_obligations_held(wrong-generation)`

```text
ShouldAssertException: h.Flushes.Accepted
    should be empty but had
1
    item and was
[9fae8ebf-f2f1-4807-b8ef-6a8668a0b7ee]
```

The initial attempted Python edit failed before touching source. Its unfortunately named `FC-1-red` run is **baseline only**: 2 passed, 0 failed/skipped, dirty=0. FC-1-mutant is the actual two-case defect proof. All following mutations were applied with PowerShell.

After exact source restoration, the ten exact S5 methods passed 30/30 and named CP-6 compatibility/metadata passed 4/4, zero failures/skips. One restored isolated build served those two selections.

Execution used the documented `scripts/run-checkpoint.ps1` mutation driver: one isolated build per mutant state, `UseAppHost=false`, `OutputPath=bin-af6a-probe/`, then exact witness selections. FC-5/T41 and FC-6/T45 reuse their own mutant build with `-NoBuild`. This driver owns its build slot itself (no double wrapper). Dirty source fingerprints and verified build-source bindings are preserved. No broad Unit/native/live/deployment lane ran.
Restored runs use a documentation-only probe commit; production/test/tool bytes remain identical to the source above. Only producer-owned `bin-af6a-probe/` directories were removed at cleanup; all commands were awaited.

## Mutation diffs

### FC-1

```diff
diff --git a/server/Application/Services/AgentSessionRuntime.cs b/server/Application/Services/AgentSessionRuntime.cs
--- a/server/Application/Services/AgentSessionRuntime.cs
+++ b/server/Application/Services/AgentSessionRuntime.cs
@@ -984,7 +984,7 @@ public sealed class AgentSessionRuntime
-            await FlushQueueOnIdleAsync(sessionId, ct);
+            await Task.CompletedTask; // viability: suppress recovered-boundary flush
```

### FC-2

```diff
diff --git a/server/Application/Services/StaleWorkingTranscriptRecovery.cs b/server/Application/Services/StaleWorkingTranscriptRecovery.cs
--- a/server/Application/Services/StaleWorkingTranscriptRecovery.cs
+++ b/server/Application/Services/StaleWorkingTranscriptRecovery.cs
@@ -157,7 +157,7 @@ public sealed class StaleWorkingTranscriptRecovery(
-    private bool Due(Entry entry) => entry.LastSuccess is null || Age(entry) >= _settings.IntervalSeconds;
+    private bool Due(Entry entry) => entry.LastSuccess is null; // viability: never admit the next due probe
```

### FC-3

```diff
diff --git a/server/Application/Services/AgentTaskLandNotificationService.cs b/server/Application/Services/AgentTaskLandNotificationService.cs
--- a/server/Application/Services/AgentTaskLandNotificationService.cs
+++ b/server/Application/Services/AgentTaskLandNotificationService.cs
@@ -324,6 +324,8 @@ public sealed class AgentTaskLandNotificationService(AppDbContext db, SessionMes
+                    await runtime.SendInputAsync(session, PtyInputEncoding.WrapIfMultiline(expected), ct, trackManualTurn: false);
+                    await runtime.SendInputAsync(session, "\r", ct, trackManualTurn: false);
```

### FC-4

```diff
diff --git a/server/Application/Services/AgentTaskLandNotificationService.cs b/server/Application/Services/AgentTaskLandNotificationService.cs
--- a/server/Application/Services/AgentTaskLandNotificationService.cs
+++ b/server/Application/Services/AgentTaskLandNotificationService.cs
@@ -83,7 +83,7 @@ public sealed class AgentTaskLandNotificationService(AppDbContext db, SessionMes
-                    note.QueueMessageId = existing.Id;
+                    return; // viability: abandon existing-key rejoin
```

### FC-5

```diff
diff --git a/server/Application/Services/SessionMessageQueueService.cs b/server/Application/Services/SessionMessageQueueService.cs
--- a/server/Application/Services/SessionMessageQueueService.cs
+++ b/server/Application/Services/SessionMessageQueueService.cs
@@ -1,4 +1,4 @@
-﻿using System.Collections.Concurrent;
+using System.Collections.Concurrent;
@@ -1812,7 +1812,7 @@ public sealed partial class SessionMessageQueueService
-            if (!await ReadWorkingAsync(db, sessionId, ct))
+            if (true) // viability: bypass fresh Working gate
```

### FC-6

```diff
diff --git a/server/Application/Services/LandNoteReceipt.cs b/server/Application/Services/LandNoteReceipt.cs
--- a/server/Application/Services/LandNoteReceipt.cs
+++ b/server/Application/Services/LandNoteReceipt.cs
@@ -73,7 +73,7 @@ internal static class LandNoteReceipt
-        PromptSubmissionMatch.IsConfirmedBy(expected, promptText)
+        expected.StartsWith(promptText, StringComparison.Ordinal) || PromptSubmissionMatch.IsConfirmedBy(expected, promptText)
```

### FC-7

```diff
diff --git a/server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs b/server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs
--- a/server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs
+++ b/server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs
@@ -76,7 +76,7 @@ public sealed class CompletionNoteWorkHostedService(
-                flushes.Recovery.ScheduleFlush(row.AgentSessionId, row.HoldUntil ?? DateTime.MinValue);
+                // viability: suppress boot-sweep scheduling for persisted pending rows
```

### FC-8

```diff
diff --git a/server/Application/Services/AgentTaskLandNotificationService.cs b/server/Application/Services/AgentTaskLandNotificationService.cs
--- a/server/Application/Services/AgentTaskLandNotificationService.cs
+++ b/server/Application/Services/AgentTaskLandNotificationService.cs
@@ -95,7 +95,7 @@ public sealed class AgentTaskLandNotificationService(AppDbContext db, SessionMes
-                        flushes.TryEnqueue(session);
+                        _ = session; // viability: drop producer wakeup after keyed admission
@@ -159,7 +159,7 @@ public sealed class AgentTaskLandNotificationService(AppDbContext db, SessionMes
-                    flushes.TryEnqueue(session);
+                    _ = session; // viability: drop producer wakeup after keyed admission
```

### FC-9

```diff
diff --git a/server/Application/Services/StaleWorkingTranscriptRecovery.cs b/server/Application/Services/StaleWorkingTranscriptRecovery.cs
--- a/server/Application/Services/StaleWorkingTranscriptRecovery.cs
+++ b/server/Application/Services/StaleWorkingTranscriptRecovery.cs
@@ -219,7 +219,7 @@ public sealed class StaleWorkingTranscriptRecovery(
-                wake = result.Outcome == TranscriptRecoveryOutcome.RecoveredIdle;
+                wake = result.Outcome != TranscriptRecoveryOutcome.CheckedWorking; // viability: wake on Unknown
```

## Essential checkpoint provenance

```text
CHECKPOINT FC-1-red commit=715176d63e4486408345de2c86d9a30b78e26c5c build=ok filter=/*/*/TurnEndGapRestartDeliveryTests/C1205_Restart_delivers_five_gap_held_completions_once* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/FC-1-red-20261013-124712-ffcd/run.trx slot=granted waited=0s dirty=0 source=715176d63e4486408345de2c86d9a30b78e26c5c sourceState=clean buildSource=verified
CHECKPOINT FC-1-mutant commit=715176d63e4486408345de2c86d9a30b78e26c5c build=ok filter=/*/*/TurnEndGapRestartDeliveryTests/C1205_Restart_delivers_five_gap_held_completions_once* executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/FC-1-mutant-20261013-125109-423d/run.trx slot=granted waited=0s dirty=1 source=715176d63e4486408345de2c86d9a30b78e26c5c+dirty:df732a1aaee89609a8a901baf66230d26b9dc0761a309538d23e08581a4eb539 sourceState=dirty buildSource=verified
CHECKPOINT FC-2-mutant commit=715176d63e4486408345de2c86d9a30b78e26c5c build=ok filter=/*/*/TurnEndGapRestartDeliveryTests/C1205_Watchdog_delivers_while_event_stream_remains_healthy* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/FC-2-mutant-20261013-125755-2c46/run.trx slot=granted waited=0s dirty=1 source=715176d63e4486408345de2c86d9a30b78e26c5c+dirty:60ed2c3918ade563385a4d851499b8c3b206d37e91e7a93985e26e42846e1ce7 sourceState=dirty buildSource=verified
CHECKPOINT FC-3-mutant commit=715176d63e4486408345de2c86d9a30b78e26c5c build=ok filter=/*/*/TurnEndGapRestartDeliveryTests/C1205_Receipt_restart_confirms_without_retyping* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/FC-3-mutant-20261013-130230-f6fd/run.trx slot=granted waited=0s dirty=1 source=715176d63e4486408345de2c86d9a30b78e26c5c+dirty:c631b89521e1c16a77942f5daddb9217f9e46e7842a08896cdea9cc8323239f2 sourceState=dirty buildSource=verified
CHECKPOINT FC-4-mutant commit=715176d63e4486408345de2c86d9a30b78e26c5c build=ok filter=/*/*/TurnEndGapRestartDeliveryTests/C1205_Restart_recovers_each_delivery_handoff* executed=16 passed=14 failed=2 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/FC-4-mutant-20261013-130644-91fa/run.trx slot=granted waited=0s dirty=1 source=715176d63e4486408345de2c86d9a30b78e26c5c+dirty:04a64a28213e1387692a1b6c03539cf34717015305992222f1c4771f6ee4a37f sourceState=dirty buildSource=verified
CHECKPOINT FC-5-busy-mutant commit=715176d63e4486408345de2c86d9a30b78e26c5c build=reused filter=/*/*/TurnEndGapRestartDeliveryTests/C1205_Busy_tail_keeps_completion_obligations_pending* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/FC-5-busy-mutant-20261013-131353-a8d4/run.trx slot=granted waited=0s dirty=1 source=715176d63e4486408345de2c86d9a30b78e26c5c+dirty:77c793ac9b8ac887c7c0c7ac77f0001a2f94224f1eac797d13912c1c6fcb7cc4 sourceState=dirty buildSource=verified
CHECKPOINT FC-5-mutant commit=715176d63e4486408345de2c86d9a30b78e26c5c build=ok filter=/*/*/TurnEndGapRestartDeliveryTests/C1205_Flush_rechecks_a_new_turn_after_recovery* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/FC-5-mutant-20261013-130955-9917/run.trx slot=granted waited=0s dirty=1 source=715176d63e4486408345de2c86d9a30b78e26c5c+dirty:77c793ac9b8ac887c7c0c7ac77f0001a2f94224f1eac797d13912c1c6fcb7cc4 sourceState=dirty buildSource=verified
CHECKPOINT FC-6-mutant commit=715176d63e4486408345de2c86d9a30b78e26c5c build=ok filter=/*/*/VerificationRoundDeliveryTests/C544_CompletionReceiptWholeWire* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/FC-6-mutant-20261013-131546-1920/run.trx slot=granted waited=0s dirty=1 source=715176d63e4486408345de2c86d9a30b78e26c5c+dirty:2d60ac35c5a5e7f54cbbe52904f4dfe9825b802210459a1dae83ca32d0503472 sourceState=dirty buildSource=verified
CHECKPOINT FC-6-s5-mutant commit=715176d63e4486408345de2c86d9a30b78e26c5c build=reused filter=/*/*/TurnEndGapRestartDeliveryTests/C1205_Only_complete_destination_receipts_confirm* executed=4 passed=2 failed=2 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/FC-6-s5-mutant-20261013-131756-da36/run.trx slot=granted waited=0s dirty=1 source=715176d63e4486408345de2c86d9a30b78e26c5c+dirty:2d60ac35c5a5e7f54cbbe52904f4dfe9825b802210459a1dae83ca32d0503472 sourceState=dirty buildSource=verified
CHECKPOINT FC-7-mutant commit=715176d63e4486408345de2c86d9a30b78e26c5c build=ok filter=/*/*/TurnEndGapRestartDeliveryTests/C1205_Restart_after_transcript_commit_recovers_lost_wakeup* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/FC-7-mutant-20261013-131920-b7ba/run.trx slot=granted waited=0s dirty=1 source=715176d63e4486408345de2c86d9a30b78e26c5c+dirty:4143332e2d2dfb9aaccfe2a23416b9af55f45d95bcfa2f7bf179217fa46f47bc sourceState=dirty buildSource=verified
CHECKPOINT FC-8-mutant commit=715176d63e4486408345de2c86d9a30b78e26c5c build=ok filter=/*/*/TurnEndGapRestartDeliveryTests/C1205_Already_eligible_caller_receives_completion* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/FC-8-mutant-20261013-132342-dc2e/run.trx slot=granted waited=0s dirty=1 source=715176d63e4486408345de2c86d9a30b78e26c5c+dirty:5d8f41cbc426980247506c7cb9905034cca43a3b872430ec660d032cbbdba862 sourceState=dirty buildSource=verified
CHECKPOINT FC-9-mutant commit=715176d63e4486408345de2c86d9a30b78e26c5c build=ok filter=/*/*/TurnEndGapRestartDeliveryTests/C1205_Unknown_probe_keeps_obligations_held* executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/FC-9-mutant-20261013-132736-7590/run.trx slot=granted waited=0s dirty=1 source=715176d63e4486408345de2c86d9a30b78e26c5c+dirty:56b0df1902703a2064d27b79a345d9f5a63704d3bd675d82cdc4af6a9fe5893f sourceState=dirty buildSource=verified
CHECKPOINT restore-compat commit=6ab35d4c62978ffc02bc9e8bcfb8a7ffe4424c72 build=reused filter=/*/*/(VerificationRoundDeliveryTests*)|(TestClassificationGuardTests*)/(C544_CompletionReceipt*)|(C544_StampIsNotReceipt*)|(Registry_matches_compiled_metadata*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/restore-compat-20261013-133959-9b2b/run.trx slot=granted waited=0s dirty=0 source=6ab35d4c62978ffc02bc9e8bcfb8a7ffe4424c72 sourceState=clean buildSource=verified
CHECKPOINT restore-s5 commit=6ab35d4c62978ffc02bc9e8bcfb8a7ffe4424c72 build=ok filter=/*/*/TurnEndGapRestartDeliveryTests/(C1205_Restart_delivers_five_gap_held_completions_once*)|(C1205_Watchdog_delivers_while_event_stream_remains_healthy*)|(C1205_Restart_after_transcript_commit_recovers_lost_wakeup*)|(C1205_Receipt_restart_confirms_without_retyping*)|(C1205_Busy_tail_keeps_completion_obligations_pending*)|(C1205_Already_eligible_caller_receives_completion*)|(C1205_Restart_recovers_each_delivery_handoff*)|(C1205_Unknown_probe_keeps_obligations_held*)|(C1205_Only_complete_destination_receipts_confirm*)|(C1205_Flush_rechecks_a_new_turn_after_recovery*) executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-af6a833c/.antiphon/viability/restore-s5-20261013-133357-4f6e/run.trx slot=granted waited=0s dirty=0 source=6ab35d4c62978ffc02bc9e8bcfb8a7ffe4424c72 sourceState=clean buildSource=verified
```

Raw diffs, source bindings, TRX and full logs are retained in `/work/repos/antiphon/.git/antiphon/viability/af6a833c/`.
Reported source restoration is independent of delivery viability; this report grants no SourceLanding/landing verdict.

--- next stage ---
next: decide
handoff: Caller decides the S5 continuation using these family assertion proofs; retain PC-84..100 for the paused systematic Mutation stage.
artifact: docs/superpowers/mutations/2026-10-13-card-1205-s5-viability-probe.md
