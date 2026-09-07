# CARD-0432: distiller timeouts

Investigation, 2026-09-07. No production/configuration changes, launches, retries, or tests.

## Finding

Do not treat DegradedTimeout as Haiku inference latency. Half the observed timeouts were model-held tasks that never dispatched. Most remaining successful-but-late tasks produced a marked answer before the deadline and waited on server ingestion/settlement. A timeout-only change masks these causes.

The live mode is Shadow. All sampled ledger rows say Shadow, and none applied a distillation. Thus raw orchestrator notes currently arise by configuration even when the distiller finishes promptly. In Apply, a timeout likewise leaves the original note untouched and releases its hold.

## Evidence and method

Read CARD-0432 through card.ps1. Retrieved the live ledger and stats with since=2026-09-05T07:19:00Z and limit=200; the result contained 140 rows through 2026-09-07T08:22:35Z, so the list cap did not truncate this sample. Joined DistillTaskId to GET /api/agent-tasks?includeChecks=true&since=2026-09-05T00:00:00Z. Read selected task details and the distiller's bound transcript through GET /api/sessions/3c3809e8-2f6d-4b97-96c1-cf78bae46652/transcript?since=0. Matched each successful task's exact task/report markers to UserPrompt and AssistantText timestamps. These timestamps are producer timestamps, not server ingestion timestamps.

The agent is antiphon-output-distiller, id 1baf5f38-4551-4fcc-8ae4-5f173b5b6240, running ClaudeCode with effectiveModelId=haiku. The inspected task list contained only Distill work on that seat; no evidence of other roles sharing it.

| Outcome | Count |
|---|---:|
| DegradedTimeout | 38 |
| RejectedUnderCompressed | 70 |
| RejectedOverCompressed | 3 |
| SkippedShort | 29 |
| Total | 140 |

Timeout rate is 27.1% of ledger rows, but 34.2% of the 111 actual attempts after excluding short skips. All rows carry bundle stamp output-distiller vf65140b2. This extends the card's 135-row sample by five rows and one timeout. Daily slices independently show Sep 5: 16/55 timeouts (3 short skips); Sep 6: 19/52 (16 skips); Sep 7: 3/33 (10 skips).

### Decompose the 38 timeouts

- 19 Canceled, never Dispatched. Every one has a matching server log `Task <id> held: model haiku is unavailable`. Details sampled for 8434de68 and 26e6f30b contain Held events saying `haiku is held; dispatch paused for that model.` All waited approximately 45 seconds and were canceled by the specialist caller.
- 17 eventually Succeeded. Creation-to-settlement ranges 45.190 to 94.656 seconds. Twelve had their exact marked report in the producer transcript within 45 seconds of task creation. The other five had queue delays of 18.859 to 38.517 seconds, followed by prompt-to-report durations below 27 seconds.
- 2 eventually Failed after roughly ten minutes: c886af63 had a report that could not be attributed to its prompt; 17db75a1's brief remained Pending after a failed delivery attempt. These are delivery/correlation failures, not evidence that Haiku needs ten minutes.

Across all 90 eventual successes matched to transcript markers:

| Interval | Median | P90 (nearest lower rank) | Maximum |
|---|---:|---:|---:|
| Dispatch timestamp to UserPrompt | 1.754 s | 4.229 s | 14.733 s |
| UserPrompt to marked AssistantText | 16.752 s | 21.931 s | 30.634 s |
| Marked AssistantText to task CompletedAt | 2.357 s | 21.743 s | 80.084 s |
| Task creation to CompletedAt | 27.141 s | 55.454 s | 94.656 s |

CompletedAt is assigned at entry into settlement; it is not the final database-save timestamp. These measurements cannot separately quantify ingestion, scheduler, database, and settlement-persistence latency.

Concrete outlier bccc30ad (UTC, Sep 6): created 22:22:19.215; dispatched 22:22:21.517; UserPrompt 22:22:22.881; marked AssistantText/TurnEnd 22:22:33.787; CompletedAt 22:23:53.871. Model response: 10.906 seconds. Report-to-settlement: 80.084 seconds. The ledger reports waitMs=89616. Server logs show two extra Enter attempts, then at 22:22:52.906 a late transcript confirmation with redelivery skipped because ingestion lagged; at 22:23:53.842 the dispatcher re-invokes settlement for an already-present marked report. This is direct evidence against interpreting 89.6 seconds as model latency.

Latest additional timeout 47ba48cc: 26.966 seconds queued, 1.861 to prompt, 14.721 prompt-to-report, 13.682 report-to-settlement. It finished in 57.231 seconds; its marked answer existed after 43.549 seconds.

## Call path and mechanisms

1. AgentTaskReplyService.DeliverToParentAsync constructs the ordinary completion note, enqueues it WhenIdle, and posts a DistillRequest. In Apply, HoldUntil is now + OutputDistillerWaitSeconds (default 45); in Shadow there is no hold.
2. OutputDistillationQueue is an unbounded in-memory channel with a single reader. OutputDistillationHostedService awaits each RequestAsync serially. Time already spent in this channel is absent from ledger WaitMs.
3. OutputDistillationService applies eligibility checks, ensures the standing seat, and calls SpecialistTaskRunner.RunAsync. MaxBacklog=3 counts unfinished same-role AgentTasks, not channel backlog. RunAsync has no model-availability preflight.
4. SpecialistTaskRunner creates a pinned Queued task. The general dispatcher checks model availability and live-seat occupancy. It refuses a held model and serializes work on the seat. Its nominal poll interval is 5 seconds, but sweeps and dispatches are awaited sequentially, so that is not a dispatch-latency guarantee.
5. WaitForRunAsync polls durable task status every 2 seconds for 45 seconds, beginning after provisioning and row creation. It does not measure token generation or independently settle a transcript-confirmed result. On expiry it cancels only a still-Queued task, awaits incident/alert work, then records Timeout. Already-dispatched tasks continue and can occupy the seat after their caller abandons the result. No LLM retry loop exists in this distillation service.
6. Ledger WaitMs begins before provisioning and ends after timeout handling. Database calls and incident/alert work use the caller's cancellation token, not a dedicated deadline. Thus WaitMs can exceed 45 seconds; the exact contribution to the 89.616-second sample cannot be isolated retrospectively.

There is a concrete path for cross-session blocking: SessionRunnerEventPump consumes the fleet event stream serially and awaits AgentSessionRuntime.ObserveTranscriptAsync. That method awaits turn-end routing/settlement/queue flushing inline; FlushQueueOnIdleAsync can await delivery verification. A slow action can delay ingestion for other sessions. The general dispatcher likewise runs sweeps and dispatches serially. This code and the transcript/log evidence establish avoidable server delay, but existing logs do not identify every blocking operation responsible for each interval.

The provisioner prepares/reconciles the workspace on each request, but there is no timing evidence that provisioning is the dominant cause. No evidence supports adding model replicas or silently switching providers as the first fix.

## What reaches the orchestrator

On DegradedTimeout, OutputDistillationService writes the ledger, calls ReleaseHoldAsync, and returns without replacing queued.Body. It never applies a late successful task's result afterward. In Shadow this is indistinguishable from every other outcome at the note surface. In Apply it forfeits the intended compression.

The original body is the full trimmed report if it fits the configured/profile inline ceiling; larger reports use DelegationReportFormatter.FitReport's head/tail excerpt and full-report pointer. Existing already-polled suppression and queue delivery rules still apply. A timeout does not drop the authoritative stored report.

Apply has an additional timing defect: the hold clock starts before the serial channel wait, while the specialist polling clock starts afterward. A burst can consume the note's hold before distillation even starts. TryApplyAsync only rewrites Pending notes with zero DeliveryAttempts; otherwise it records AppliedLate. Raising both uses of the same timeout does not eliminate that clock mismatch.

Timeout ledger cost is zero because SpecialistTaskRunner.Finish returns zero cost/null result. The 17 late successes still incurred real task costs, absent from those ledger rows. The current stats therefore understate timeout-related spend.

## Recommendation for Plan

Keep the current timeout unchanged until addressing availability and observation; 45 seconds exceeds every observed prompt-to-report interval in this sample. Do not enable Apply as part of this fix. Compression-gate rejection is separate work (CARD-0430), and all 73 promptly completed attempts in this sample were rejected.

Plan these changes:

1. Fast-degrade known held/unavailable seats with a specific reason instead of creating a task and spending 45 seconds waiting for an impossible dispatch. Recheck availability while queued without bypassing model holds.
2. Separate transcript ingestion from slow turn-end side effects while preserving per-session ordering, correlation, and exactly-once delivery safeguards. Instrument producer/ingested/report-observed/settled timestamps and dispatcher wait so the shared-event-stream hypothesis can be verified per call.
3. Carry one absolute note deadline from enqueue through channel, dispatch and application; bound queue admission, avoid already-expired work, and preserve raw fallback. Record queue wait separately from execution and timeout-cleanup time. Preserve standing-seat ownership when the caller stops waiting.
4. Reconcile eventual outcome/cost for timed-out specialist runs. Do not silently deliver a late duplicate note.
5. Re-measure before deciding a permanent budget. If immediate mitigation is desired, trial 75-90 seconds in Shadow with separate phase metrics. Retrospectively 60 seconds covers 9/17 late successes, 75 covers 15/17, 90 covers 16/17; these are counterfactual bounds, not verified recovery rates or compression-gate passes. A longer serial wait can also worsen burst queueing.

Verification should cover held-model fast degradation, delayed/blocked ingestion with prompt evidence already present, burst queue deadlines, expired notes, timeout cleanup, late completion/cost accounting, and no duplicate/raw-report loss. Use isolated test sessions, never the production runner.

## Evidence files

Local read-only captures under C:\src\Antiphon\.antiphon: task-a87c5408-ledger.json, task-a87c5408-runs.json, task-a87c5408-timing.json, task-a87c5408-transcript.json, and selected task detail files. The timing file contains the per-run join used above. No benchmark tasks were launched.

Primary source locations: DelegationSettings.cs:758; AgentTaskReplyService.cs:1612; OutputDistillationService.cs:58, 114, 139, 348; SpecialistTaskRunner.cs:71, 118, 200; OutputDistillationHostedService.cs:45; OutputDistillationQueue.cs:13; AgentTaskDispatcher.cs:458, 4093; SessionRunnerEventPump.cs:46; AgentSessionRuntime.cs:267, 484; DelegationReportFormatter.cs:593.
