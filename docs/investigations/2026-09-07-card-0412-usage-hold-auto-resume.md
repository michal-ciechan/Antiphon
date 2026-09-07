# CARD-0412 investigation: usage-hold expiry and recovery

Investigation complete: CARD-0401's sibling-text lookup is working, but hour-only reset times still fall back to six hours; its repair path can recreate cleared holds from old stubs. Hold clearing does not wake live wall-parked sessions, although queued and some routing-blocked tasks already recover through polling. No implementation or live recovery action was performed.

Evidence read on 2026-09-07 from the full CARD-0412, the running server's model-availability and transcript APIs, read-only SQL against the local antiphon database, and checkout ea049c9d. The relevant fix commit is dde6e057 (commit timestamp 2026-09-05 22:16:47Z; the card describes it as landed September 6). Commit time is the sampling boundary, not proof of deployment time. Persisted nonempty hold RawText paired with null TurnEnd.Text directly establishes that sibling text reached recovery in the observed incidents.

## 1. The flagged six-hour hold is a real parser gap

`UsageLimitWallParser.ResetRegex` requires a colon and two minute digits. Production supplied `You've hit your session limit · resets 9am (Europe/London)` and later the same sentence with `2pm`. Both fail that regex; `9:00am` and `2:00pm` match. `Parse` therefore returns ModelCap, and `ApplyWallAsync` writes its configured six-hour fallback and a misleading `no reset stated` reason.

Post-commit hold evidence (all times UTC on September 6; timestamps rounded to seconds):

| Alias | HitAt | DisabledUntil | Source / raw reset |
|---|---|---|---|
| haiku | 07:35:17 | 13:35:17 | AutoDetected / 9am |
| sonnet | 07:43:17 | 13:43:17 | AutoDetected / 9am |
| opus | 08:07:48 | 14:07:48 | AutoDetected / 9am; the flagged example |
| fable | 11:51:38 | 13:00:00 | Manual hold whose evidence was refreshed / 2pm |
| opus | 12:40:58 | 13:00:00 | Manual hold whose evidence was refreshed / 2pm |
| opus | 14:50:18 | 20:50:18 | AutoDetected / 2pm; later recurrence |

The four AutoDetected rows all have the six-hour duration, within a few milliseconds because the recovery and hold writer capture separate `now` values. The two Manual rows retain the operator's deadline by the intended Manual-outranks-AutoDetected rule; their reason still misclassifies the supplied reset. These are six evidence-bearing hold rows, not six independent provider incidents: an active hold is updated in place and loses earlier source evidence.

There are 18 post-commit Wall recoveries: 12 sibling sentences saying 9am and six saying 2pm, across six sessions. All have null TurnEnd.Text. Sixteen are WallParked, two WallModelPaused, and zero have NextAttemptAt. The latest new matching stub was stored at 12:40:25Z September 6. No newer hold rows than the September 6 records were present at inspection; the latest overall row is an unrelated manually cleared Codex kind-wide hold. The availability API currently reports all models available. This is a bounded observation of stored history, not a claim that no provider wall occurred outside captured evidence.

The flagged Opus source is Antiphon-Orchestrator, session `fdf1dd3d-da3e-4c68-8e3f-1215b07cceb9`. A fresh transcript API pull confirms the AssistantText/TurnEnd pairs, including sequences 12362/12363 (9am) and 12766/12767 (2pm). The live agent is AlwaysOn and Running; a running idle process is outside the supervisor's restart arm.

Existing parser fixtures cover minute-bearing examples such as 6:10pm and 5:20pm. Add the exact hour-only production sentences. Preserve zone conversion, noon/midnight handling, invalid-time rejection and the existing minute formats. Anchor reset calculation to the original stub's provider timestamp, with a defined fallback, rather than reinterpreting an old sentence relative to repair time: the current next-occurrence rule rolls a passed clock time to tomorrow.

## 2. Repair can undo a clear without a new wall

`ApiErrorRecoveryService.TryRepairEmptyWallAsync` looks for an uncleared hold by SourceSessionId. It skips only when that hold exists and has nonempty RawText. If no such hold exists, it still calls ApplyWallAsync with the old stub text and the current clock. It does not require an actual empty-text hold, an unresolved recovery, the latest stub, or absence of later user progress.

`AdoptAsync` revisits already adopted stubs within its default 180-minute window. Clearing a hold, or another session overwriting its SourceSessionId, can therefore make an old recovery eligible for this purported empty-text repair. This can rewrite/recreate holds and overwrite ResolvedAt repeatedly. Fixing only the regex does not fix that lifecycle problem.

Strong production evidence: Opus hold `d3138897-5d78-4365-8d00-3741e7990c0b` was written at 14:50:18.345031Z with the old 2pm text, after the previous manual hold cleared at 13:00:27.909282Z. Recovery sequence 12767, originally detected at 11:53:48.825138Z, has ResolvedAt 14:50:18.339930Z, exactly the fallback deadline's base clock. Sequence 190 on the Fable source was also rewritten at 14:50:18.321077Z. The fresh Opus transcript contains no later wall after sequence 12780, and does contain UserPrompts beginning at 13:08:31Z before this reapplication. The timestamp correlation and code support stale repair as the cause; historical service logs were not used to independently identify the caller.

Plan should make evidence enrichment idempotent and explicitly tied to the recovery/hold generation. A cleared hold must not be resurrected by rereading its old stub. Do not use mutable SourceSessionId on a shared alias row as the repair identity or as the complete list of blocked consumers. Do not automatically rewrite historical terminal tasks as a migration side effect.

## 3. Existing machinery and missing links

| Consumer / service | Already implemented | Gap relevant to CARD-0412 |
|---|---|---|
| ModelAvailability | Timed expiry on one-minute sweep and lazy reads; explicit ClearAsync; alias and kind-wide holds OR together; manual precedence | Clearing only stamps ClearedAt. No durable transition identity/reason, wake schedule, or consumer acknowledgement. A lazy GET/check may clear before the sweep, so using only the sweep's returned count loses transitions. |
| ApiErrorRecoveryService | Durable (session, stub sequence) adoption; parsed SessionLimit schedules reset+2 minutes; live Running check; transcript catch-up; later UserPrompt/newer stub suppression; WhenIdle Supervision message | ModelCap resolves WallModelPaused, wall cap resolves WallParked, both with no next attempt. FireOne does not restart dead sessions or consult current effective model holds. No shared pacing/dedup identity tied to a hold-clear generation. Enqueue occurs before schedule save. |
| SessionMessageQueueService | Normal queue owns delivery and transcript confirmation; Supervision/Ui can pass the terminal capacity gate | ApplyCapacityHoldAsync keeps Channel/Scheduled messages pending while a terminal recovery is newer than the last UserPrompt, independently of current ModelAvailability. Clearing the fleet hold alone never removes this barrier. NeedsHuman and UnknownExhausted share that gate and must remain excluded from capacity auto-resume. |
| AgentSupervisorService | AlwaysOn polling, runner-outage guard, suspension/Herdr-hold guards, durable NextRestartAt and failures, start through AgentControlService, incidents | Live Running sessions return without a continuation. Dead model-held agents already retry eventually, but model refusal grows the generic failure ladder and can force Fresh after two failures. Defaults are 5-second exponential base with a 30-day cap; capacity returning does not pull NextRestartAt forward. No fleet pacing. |
| AgentTaskDispatcher | Queued held aliases rechecked each tick. ResumeRoutingBlockedAsync re-walks routing-exhausted chains and requeues when a declared candidate is available. Capacity-return Rerouted event exists. Global MaxConcurrentTasks defaults to six, plus existing scope/specialist constraints | These are already auto-resume paths. They need coordination with shared release pacing, not a duplicate dispatcher. Routing/cascade guards intentionally exclude some blocks. Global concurrency is not per-provider pacing. |
| AgentTaskReplyService / AgentTaskService | Retryable unresolved recovery defers a task Working; declared routing can reroute/requeue or block on a wall | Terminal ModelCap/WallParked can mark ordinary tasks Failed and release the delegate. Such tasks are not merely queued/parked, and expiry must not blindly restart them. A narrowly typed capacity-wait state/reason is needed for future automatically resumable work. |

The AlwaysOn precedent is automatic recovery infrastructure, not a complete capacity policy. In particular, model refusals should not grow the process-crash counter or erase the resumable conversation through Fresh selection.

The wall-death counter also needs redesign: ApplyWallAsync counts all prior Wall recoveries in the session except Superseded, despite its consecutive-deaths terminology. Successful work does not reliably remove historical resolved Wall rows from that count. Long-lived orchestrators can remain WallParked across unrelated quota windows. Preserve a bounded retry budget within a capacity episode; do not reset every safety counter on each sweep or indiscriminately revive cascade-exhausted tasks.

## 4. Proposed Plan defaults

1. Repair hour-only parsing and stale adoption before enabling any new wake behavior. Existing nonempty unparseable rows will not be repaired by the current empty-only predicate; define a bounded compatibility policy, without replaying historical completed work.
2. Persist availability-clear cause (Expired versus OperatorCleared), generation and recoverable pending consumption in the same transaction as the state change, across explicit clear, timed sweep and lazy expiry. Consume on startup and periodic sweep. Recheck effective alias plus kind-wide availability immediately before action; a replacement hold cancels/defer the old release. Expiry is permission to attempt, not proof the provider has capacity.
3. Persist which actual work was blocked, its hold/episode identity, session/task/agent identity and version. One mutable SourceSessionId on the hold cannot identify the fleet. Capture standing delegate-launch refusals too: an orchestrator blocked by a 409 may have no API-error stub of its own.
4. Select only still-relevant work after transcript catch-up: an interrupted unfinished turn, pending eligible queue work, a recorded blocked orchestration dispatch, or a dead authorized standing owner. Honor manual stop/suspension, Herdr acknowledgement, rules barriers, card terminal/cancel state, newer user progress, task attempt changes and existing routing/pin/cascade guards. Do not wake every agent sharing an alias or restart arbitrary historical Failed tasks.
5. Route live-session continuation through one idempotent WhenIdle Supervision queue item, with a persistent episode/action key and delivered-prompt evidence. Prefer existing queued work when it can safely serve as the continuation. Coordinate capacity-barrier release so queued messages and the continuation do not create duplicate turns. Use existing AgentControlService/supervision for dead standing agents, and existing dispatcher for parked tasks. Never use raw input or an independent process launcher.
6. Suggested initial anti-stampede policy for Plan to refine: one recovery admission per provider kind per 60 seconds, deterministic per-consumer jitter up to 30 seconds, ordered oldest pending work first; retain normal global/scope limits. Share admission across resume prompts, supervised starts and held-task dispatches. Start with a conservative kind-wide bucket because the observed session limit spans Claude aliases; do not invent credential/profile equivalence. Persist reservations so restart does not release all consumers together. A new wall pauses the remaining release wave; bounded per-episode retries and visible escalation prevent infinite expiry/re-hit loops.
7. Natural expiry and deliberate clear should both resume eligible previously authorized work, with the same pacing by default. Persist the distinct cause for operator visibility. No extra approval knob is required for the normal card acceptance. Source=Manual describes how a hold was made, not why it cleared.
8. Record scheduled, queued, confirmed, skipped, re-held and exhausted outcomes with cause, hold/episode, consumer and next due time. Distinguish enqueued from delivered/resumed. Coalesce fleet events to avoid an incident flood. Preserve existing incidents and task events where sufficient.

## 5. Validation required in the next stages

No builds or test suite were run; no code, settings, holds, agents, messages or cards were explicitly mutated. The normal GET endpoints can perform their existing transcript synchronization/lazy expiry side effects. Four read-only regex probes reproduced rejection of 9am/2pm and acceptance of 9:00am/2:00pm. This file is the only authored change; pre-existing `.perfmon/` was left untouched.

TestDesign should cover exact production reset strings and delayed/backfilled parsing; manual precedence; clear followed by stale adoption; source-session overwrite; repeated clear; lazy expiry before sweep; restart between clear/admission/enqueue/save; wildcard overlap and re-hold races; many consumers paced across all three execution paths; later confirmed UserPrompt suppression; blocked Channel/Scheduled queue release; dead standing-agent resume without fresh-conversation escalation; manual suspension/Herdr hold/authentication exclusions; wall budget scoped to a new episode; and existing queued/routing-blocked recovery without duplication. Use TimeProvider and established database/queue tests; no real provider traffic or production runner launch is needed for this implementation validation.

Primary implementation locations: `server/Application/Services/UsageLimitWallParser.cs`, `ApiErrorStubText.cs`, `ApiErrorRecoveryService.cs`, `ModelAvailability.cs`, `AgentSupervisorService.cs`, `AgentTaskDispatcher.cs`, `AgentTaskReplyService.cs`, `SessionMessageQueueService.cs`; hosted clock `server/Infrastructure/Supervision/AgentSupervisorHostedService.cs`; settings `server/Application/Settings/SupervisionSettings.cs`; durable entities `server/Domain/Entities/ModelAvailabilityHold.cs` and `ApiErrorRecovery.cs`.

Next stage: Plan. No user decision blocks the investigation; the proposed pacing and episode policy are explicit defaults for Plan to specify and test.
