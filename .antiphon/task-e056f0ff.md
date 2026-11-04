# Code e056f0ff: CARD-1709 CompletionNote recovery pass bounded lock wait

Branch feat/card-task-e056f0ff, base master 1ef88240b3c5ee2c759db958e5fb0b635b1248cc, runner mirror /work/worktrees/task-e056f0ff.

## Commits
- 83c33cbd05dd471714ce5ddc7db8dde634114ed6 test: red witnesses (seeding bug, failed at setup)
- 49ea6c3daf0779af28f3490a0ccca4d2370e623b test: seeding fix; RED at base: 4/4 C1709_* fail with TimeoutException (pass stalls on held lock)
- 7fdf784b8299dfa73c0d0f18f6eb8786037cd90c fix: tested SHA for all runs below

## Fix (mirrors CARD-1705)
- `SessionMessageQueueService.EnqueueSupervisionNoticeAsync` gains `lockWait` (passes to EnqueueCoreAsync's CARD-1705 bound).
- `TaskSupervisionNoticeService.RecoverAsync(taskId, ct, ISet<Guid>? busySessions = null)`: `ScanQueueLockWait` = 1 s; busy-set skip after CanSendAsync with ChangeTracker.Clear (notice left exactly as read: no NextAttemptAt backoff, no ReasonCode); `SessionQueueBusyException` caught before the general catch, Clear + add to set.
- `CompletionNoteWorkHostedService`: one busy set per pass, shared by the supervision and completion-note phases (as d4c88ddde shares across captured/land phases); completion enqueue `lockWait: ScanQueueLockWait` (1 s); `if (busySessions.Contains(parent)) continue;`; busy catch before the general catch, never sets `failed`.
- Addition beyond CARD-1705: this loop's backstop is 15 min (the land scan polls every 5 s), so a pass that skipped busy work sets `nextSweep = min(nextSweep, now + BusyRetryDelay)` (5 s). Busy is not a failure, so it takes no RequestSweep/failure path.

## Witnesses (TaskSupervisionReceiptTests.C1709.cs; class made partial; OpenAsync gains configure/services; RecoverAsync reflection helper fills optional params with Type.Missing)
- C1709_Held_recipient_lock_does_not_stall_the_recovery_pass: held parent lock, supervision notice + completion note for it; free session's completion note queued in the same pass; held items unchanged; next pass enqueues both.
- C1709_Busy_recipient_keeps_supervision_notice_order_when_its_lock_frees_mid_pass: N1 times out, lock freed when the pass reads N2; N2 not queued in that pass; next pass queues N1 then N2.
- C1709_Busy_caller_keeps_completion_note_order_when_its_lock_frees_mid_pass: A1 < B < A2, lock freed at B's completion-scan boundary; A2 not queued before A1.
- C1709_Busy_pass_retries_skipped_work_before_the_backstop: hosted worker, first due time 5 s; release + advance 5 s enqueues the note.

Mutant check (one build, restored with git checkout): delete supervision skip -> supervision-order RED; delete completion skip -> completion-order RED; drop retry -> retry RED (DueTimes 15 min); held-lock test green (expected). Not a Mutation-stage discharge.

## Runs (all at 7fdf784b8, build-slot, one lease per run)
| Run | Scope | total | passed | failed | skipped | slot waited |
|---|---|---|---|---|---|---|
| red2 (49ea6c3da) | C1709_* | 4 | 0 | 4 | 0 | 0s |
| green | C1709_* | 4 | 4 | 0 | 0 | 0s |
| mutant | C1709_* | 4 | 1 | 3 | 0 | 0s |
| G1 | TaskSupervisionReceipt, PostLandMutationDelivery, CompletionNoteWorkQueue, CompletionNoteDurableSweep, OutputDistillation{Delivery,Producer,Cleanup}, RunnerAlarmDelivery, StaleWorkingTranscriptWatchdog, DataRetentionService, CheckCompaction{Crash,WorkerLifetime,Fixture}Tests | 365 | 365 | 0 | 0 | 0s |
| G2 | CARD-1705 scanner set 1-10 (incl. AgentTaskLandNotificationRecoveryTests, C1705_* 3/3 passed) | 251 | 251 | 0 | 0 | 90s |
| G3 | CARD-1705 scanner set 11-19 | 157 | 157 | 0 | 0 | 105s |

Not run: whole Unit lane (brief scoped the run to these classes; 60-minute budget). Checkpoint tool not used: the brief carries no plan/### Checkpoints table and asked for build-slot runs per group.

check-evidence-diff base..HEAD(7fdf784b8): commits=3 entries=0 violations=0. merge-tree vs origin/master 5a95ae7a5: clean.

## Audit (written to CARD-1709)
Unbounded scan/background enqueues left unfixed: AgentTaskDispatcher FailNeverStartedAsync, RecoverLostDeadSessionFailureNoteAsync, FailAndNotifyAsync (sweep paths), RemindOneUnacknowledgedFailureAsync; ApiErrorRecoveryService.FireOneAsync; ContextCompactionService.ProcessSessionAsync; KeepSixReminderJob.ExecuteAsync; ScheduleService.FireCoreAsync; GrokRulesRefreshService.QueueLaunchBriefAsync; AgentTaskReplyService.NudgeForClosingLineAsync; ChannelBridgeService.FlushLaneAsync; OrchestratorInstructionsService.QueueNoticesAsync; SessionHealthActions.EnqueueWhenIdleAsync.

PCs pending for SourceLanding Mutation: the three mutants above (supervision skip, completion skip, retry delay) plus busy-catch-as-failure (catch removed -> general catch backoff) and shared-set (separate sets per phase).
