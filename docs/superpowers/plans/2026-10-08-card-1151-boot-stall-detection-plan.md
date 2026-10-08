# CARD-1151: boot-stall detection without stopping Working sessions

Plan date: 2026-10-08. Next stage: **test-design**, to audit the fixtures and
checkpoint roster before Code. Verification is designed below; this dispatch does
not implement tests or production changes. No policy decision is left to Code.

Planning baseline: fetched `origin/master`
**03c10be6d4467730ae2e26d8ed49cc8b3ddd3302**. Advanced the assigned branch by
fast-forward from `b5e78700ae9a76430c13d75cc03399055dd82e59`; no rebase or reset.
Read CARD-1151 with `scripts/card.ps1 get CARD-1151 -Board Antiphon`.

The required outcome is that an unanswered boot prompt is detection and operator
attention while its session is Working, listed, or uncertain. It cannot fail the
task, requeue it, stop it, release its seat, or hold its model alias. Positive proof
of a dead session retains the applicable failure/retry policy. CARD-0079 remains
the sole authorization for automatically stopping a Working session.

## Ground truth

Citations are verified against the baseline above. `Dispatcher` below means
`server/Application/Services/AgentTaskDispatcher.cs`; other shortened service names
also refer to `server/Application/Services/`. Test names refer to
`tests/Antiphon.Tests/Application/`. Line numbers are baseline coordinates.

| Card assumption or question | What master does | Design consequence |
|---|---|---|
| An aged Working boot can be killed/retried. | `Dispatcher:3155` selects `TryFailBootStallAsync`; `:3242` fails with `ProviderUnresponsive`; `:3291` calls `RetryAsync`. `BootStallWorkingTickCharacterizationTests.cs:29` pins Queued/attempt 2/Stopped/one delegate stop. | The premise is correct. Flip the test explicitly; changing only the runner's direct kill count misses the real stopper. |
| Workspace silence proves there is no work to protect. | `Dispatcher:3217` declines only when an **available** probe reports file/commit progress. Null/unavailable falls through despite the comment claiming available-and-quiet is required. | Workspace evidence can withhold failure, never establish non-Working or authorize a stop. |
| The boot deadline is independent of Working truth. | `TaskDeadlinePolicy.cs:168` requires `SessionMessageQueueService.IsWorkingAsync`; `:179` reads `BootReplyWatch`; `:182` selects BootModelWait. | The ordinary stable BootModelWait case is Working by construction. A dead negative control needs real false Working evidence, not merely SessionStatus.Failed. |
| Default detection starts at eight minutes. | `TaskDeadlinePolicy.cs:50` sets preview to 0.8; `:145` gates on the smallest armed limit; `DelegationSettings.cs:452` sets boot wait to 8. | Keep the 6.4-minute default preview and eight-minute detection. Do not disable boot wait to obtain green. |
| Any record answers the model watch. | `BootReplyWatch.cs:57` includes AssistantText, Thinking, ToolCall, ToolResult and TurnEnd; `:171` excludes housekeeping, uses the launch clock, and returns latest real prompt sequence/time. | Keep these predicates and resume/refinement semantics; queued input and titles are not replies. |
| Session status tells whether the session is Working. | `SessionMessageQueueService.cs:5087` delegates to `TranscriptWorkingStateQuery.ReadAsync`; `server/Infrastructure/Data/TranscriptWorkingStateQuery.cs:29` derives activity relative to turn boundaries, independent of the session status column. | Reuse this exact truth. False plus a missing/unreadable observation is not positive safety proof. |
| A transcript catch-up returning false proves a successful empty read. | `AgentSessionRuntime.cs:706` returns whether it persisted a new row; `:713` also returns false on failure. `Dispatcher:3430` discards the result and swallows faults. | Neither bool nor lack of exception from the dispatcher wrapper certifies freshness/completeness. |
| Retry is harmless after marking Failed. | `AgentTaskService.cs:2682` -> `RequeueAsync`; `:3463` calls `StopDelegateAsync` unless release is confirmed; `:3932` -> `IDelegateSessionStopper.KillAsync`. Boot tail also directly kills at `Dispatcher:3274` and `:3302`. | Guarding only FailAndNotify or one kill leaves bypasses. Automatic absent-session retry must not invoke a stopper. Human Retry keeps its existing semantics. |
| Withholding the eight-minute kill fixes the task outcome. | `Dispatcher:3161-3175` falls through to general failure after model-wait; `:3192` fails it. Policy picks the largest deadline fraction (`TaskDeadlinePolicy.cs:205`). | Carry boot identity separately from the winning clock; an unresolved boot episode cannot later fall into general/ceiling failure. |
| The current attention row is already suitable. | `AttentionService.cs:1228-1276` says the next sweep will fail, kill and retry. | Use the existing Overdue row with boot-specific detection/operator wording and severity. No new client enum or UI workflow. |
| Dead-session recovery automatically retries every failure. | `Dispatcher:2480-2653` has grace, runner, bind/report, commit-recovery and S1 gates, then FailAndNotify without a kill or automatic retry. | Preserve those policies. The automatic one-retry policy being retained is the narrowly proven absent **boot** branch, not a new retry for every dead session. |
| Runner inventory is already consulted in boot failure. | It is absent from `Dispatcher:3211-3312`. `ReadAbsenceAsync:2786` supports local/owning remote inventory and treats any listed status/generation as listed; missing session lookup currently falls into the local-null shape. | Reuse ownership-aware inventory logic, but require a positively loaded session binding first. A missing row is not local ownership. |
| Never-started sessions should all fail/retry. | S1 `DecideAbsentLaunchAsync:2679` and `AbsentLaunchPolicy.cs` reserve the exact runner-unknown, pristine-unattempted shape for a Blocked hold. CARD-1153's certificate integration is planned; `.AbsentEvidence.cs` still contains pending S4 tests. | S1 holds take priority. A 404/empty DTO cannot be promoted to a certificate; CARD-1151 adds no absence protocol. |
| Delivery watchdog is the same arm. | `Dispatcher:2155` withholds a Pending brief on a Working session; `:2295` withholds its kill; `:2305-2314` fails then conditionally stops for real delivery failure. | Preserve watchdog arbitration and S2's stronger fresh-safety requirements. A boot warning is neither a delivery failure nor cleanup authority. |
| CARD-0079 grants a general boot exception. | `CheckCompactionContinuationService.cs:121-145` confirms a specific continuation; `:248-335` checks scope/generation/budget and commits a stop intent before the conditional runner operation. `docs/session-runtime-invariants.md:770` limits it to an AlwaysOn Claude Check seat and explicit auto compact/continuation silence. | Do not call this coordinator or its conditional stop endpoint from boot detection. Keep its existing tests. |
| Session-scoped boot watch cannot interfere. | `BootReplyWatchdogService.cs:178` stands down for an open delegate. For taskless AlwaysOn sessions `:314` calls its stopper after raising the incident. | Keep the delegate open so this watch keeps standing down. Its separate taskless stopping behavior is an adjacent policy defect, not fixed or globally certified by CARD-1151. Caller should commission it separately. |
| Documentation consistently forbids this stop. | `docs/session-runtime-invariants.md:602-610` and `docs/orchestration-loop.md:1089-1098` still prescribe boot fail/kill/retry, contradicting the Working-stall rule at runtime invariants `:770`. | Replace the contradictory sentences and pin them to the new tests. |
| Recovery hot paths have spare query budget. | `DelegationDispatchRecoveryBoundaryTests.cs:25-44` asserts held tick 18, working-live tick 18, inside-grace absent scan 4 using all-command interception. | Keep these exact values and fixtures; all new IO is behind the existing age gate. |

## Decisions

### D-1. Detection takes precedence for an unresolved boot episode

Introduce a small internal boot disposition: `NotBoot`, `DetectOnly`,
`SafeAbsentFailure`. `DetectOnly` is the default for every incomplete observation.
Replace the nullable-bool boot tail with an explicit result; `DetectOnly` returns
from the overdue sweep before **any** failure, retry, alias hold or cleanup.

Extend the deadline evaluation result with optional boot facts (launch clock,
prompt sequence/time and boot due time), independent of which deadline fraction
wins. Load them only after the existing cheap age gate. Both Attention and the
dispatcher use those facts. Preserve the existing read-only policy/pure predicate
split. Boot classification must remain available when the ceiling wins, when the
workspace shows progress, and when the general model clock is shorter than boot.

After a reply/turn-end positively ends the boot predicate, ordinary model-wait,
local-execution and role-ceiling policy applies again. New genuine prompts and
same-ID resumed launches get the existing BootReplyWatch clock/identity rules;
telemetry timestamps never restart a deadline. A zero boot setting disables the
early boot notification, not the protection: when another deadline is evaluated,
an unresolved prompt-only boot still cannot become an automatic terminal failure.

Rejected: just removing KillAsync; returning null into the current 20-minute
fallback; marking Blocked/Failed while leaving the process alive; changing the
definition of Working; treating task.Status Working as the only signal; disabling
the deadline; allowing an earlier role ceiling to bypass protection.

### D-2. Positive safety whitelist; no automatic boot stop, even on the absent path

Before a boot-origin failure, require **all** of the following on the same attempt:

1. The task is still open, and its task ID, status, attempt, concurrency token,
   session ID and DispatchedAt match the observed attempt. Its bound session row
   exists with the same accepted generation and owning runner/store.
2. A successful read of the shared IsWorkingAsync verdict is explicitly false.
   A true verdict always selects DetectOnly, even if the DB says Failed or the
   inventory says absent. Exceptions and missing observations also select it.
3. A fresh, successfully obtained inventory from that **owning** runner contains
   no entry for the session ID. Any status/generation is a veto, including Exited,
   Pending and a generation mismatch. An unavailable directory, null collection,
   unsupported/read-failed inventory, or missing binding is unknown. A loaded
   legacy local binding may use the schema's null RunnerId convention; a missing
   session row may not masquerade as that binding.
4. Positive terminal/never-created evidence exists for the generation, and no
   launch/resume is pending or owned. For a previously created boot session use
   recorded terminal lifecycle evidence and complete readable transcript evidence
   for that generation. A stale DB snapshot or ordinary transcript 404 is not
   that evidence. For never-created work defer to the S1/CARD-1153 arbitration;
   never manufacture a certificate or prepare/certify an ID from this boot sweep.
5. No model reply, marked report awaiting settlement, unreported native work,
   unresolved API recovery or active commit-recovery obligation contradicts the
   failure. The workspace probe is positively available and quiet; null,
   unreadable or progress-bearing workspace evidence withholds this boot failure.
6. Recheck the attempt, generation, Working and absence immediately before the
   conditional failure/requeue transaction, using queue gate then task row lock.
   Any race, changed identity or failed read aborts without modifying the attempt.

Do not interpret CatchUpTranscriptAsync's bool as read success. The safe-absence
adapter must obtain a typed success/unknown observation using the existing runner
DTO identity/completeness fields and persistence result, or decline. Do not widen
the runtime's public bool contract or change general catch-up behavior. Successful
unchanged reads are success; 404, cancellation not requested by the caller, failed
persistence and incomplete snapshots are unknown. Caller cancellation propagates.

Keep the existing boot `ProviderUnresponsive` failure and one same-kind/tier retry
only for this whitelist. An exhausted attempt fails without a further retry.
Keep existing repeat-alias policy only for actual committed safe-absent failures;
detection events never enter BootStallLedgerKey counts. Other dead-session reasons
retain their own existing failure policy; S1's never-attempted hold still wins.

The automatic boot path calls **no stopper at all**. Add an internal,
attempt-bound absent-session retry entry to AgentTaskService, sharing ordinary
requeue logic but skipping StopDelegateAsync only after the proof/revalidation
above. This is not a public `skipStop` API or a bool supplied by HTTP callers.
Human Retry, Cancel and other explicitly authorized actions are unchanged. Remove
both boot-tail direct-kill fallbacks. If retry fails, retain Failed and warn;
never call KillAsync as compensation. Inventory cannot exclude every future
remote race, so even the proven-absent branch gains no authority to stop a process
that appears later.

The usual live BootModelWait candidate cannot satisfy this whitelist: it was
classified using true Working. Tests for safe absence must positively establish
false Working (e.g. a real terminal restart/interruption boundary), terminal
lifecycle evidence and complete inventory. Changing only SessionStatus is a bad
test. Preserve the failure/retry kernel through this narrow legitimate transition
rather than inventing a broad dead-session retry mechanism.

Rejected: null-as-false; any empty list regardless of owner; workspace silence as
authority; waiting a fixed time to infer absence; reusing human Retry's stop;
cleanup after a caught error; copying CARD-0079's exception into this path.

### D-3. Task-visible, restart-safe warning with bounded operator escalation

Use existing AgentTaskEventType.Warning and AttentionKind.Overdue. No new database
columns, enum, migration, endpoint, session status or client flow is needed.

The boot episode key is the task ID + attempt + session ID + normalized accepted
generation + launch clock + latest real boot-prompt sequence. Put a stable
`BootStallDetected` or `BootStallNeedsOperator` reason and this metadata in the
Warning event Detail; never include raw prompt text, secrets or composer text.
One event per episode **and severity stage**. At the default 6.4-minute preview
show the derived Attention row; at eight minutes write BootStallDetected once.
At `max(bootDueAt, promptAt + 20 minutes)` write BootStallNeedsOperator once and
raise the row to Error. If boot early notification is disabled, use the 20-minute
operator threshold when an ordinary deadline brings this boot into evaluation.
An already older episode first observed after escalation gets one operator event,
not a burst of historical warnings. No periodic repeats or auto-spawned agent.

The row explicitly says: inspect the session and choose to continue waiting,
reply, or explicitly cancel/retry. Use OpenDrawer, Reply and Cancel actions;
ordinary task controls supply explicit Retry. Do not advertise automatic model
escalation or imply a seat will be reclaimed. Show the original prompt age,
boot due time and any later general/ceiling breach as evidence. The operator
threshold bounds **visibility**, not task lifetime: absent an answer/progress or
explicit action the session can remain Working indefinitely, with Error visible.

Derive the Attention row from current boot facts, not successful telemetry writes.
Place it before generic overdue/progress-stall selection for this boot so an
unrelated warning cannot hide the operator escalation. Do not emit duplicate
task and session-watch rows; the latter already stands down for an open delegate.
A reply resolves the derived row even when historical Warning events remain.

Write optional events in a separate short-lived context/transaction: lock the
task, verify episode identity, test the exact key/stage, insert if absent, commit.
This serializes concurrent scoped dispatchers without a migration. Dispose or
rollback that context on failure so a later unrelated SaveChanges cannot leak
telemetry. Publish after commit, catch non-shutdown telemetry/SignalR faults,
and return the already-selected disposition. Telemetry failure cannot authorize
failure, retry, stop, release or an alias hold, or block an otherwise admitted
safe-absent result. There is no outcome transition in the telemetry writer.

Rejected: an in-memory dedup set lost on restart; using FailureReason on the task
as the warning store; a new incident kind/DB ledger; sending input to the delegate
or caller as the alert; making event delivery a prerequisite for safety.

### D-4. Preserve recovery, certificate and delivery ownership

CARD-1149 S1 remains the exact absent-launch hold with original input and zero
automatic relaunches. Its whitelist is not broadened by this plan. CARD-1150 S2
remains the owner of brief ensure, delivery identity, spill files and FIFO. A
boot detector neither calls ensure nor creates, resets, removes or rewrites a
queue row. Preserve original Body bytes, message ID, attempts, baseline and spill
bytes, including across warning/escalation/service recreation.

CARD-1153 S4 is a **file integration gate**, not a prerequisite for protecting
Working boot sessions. It changes AgentTaskDispatcher.cs and the absence-evidence
adapter. Its certificate supplies never-created evidence only, never non-Working
truth and never permission to kill. Unsupported peers remain unknown. Do not
add certificate calls, preparation or a fallback 404 interpretation to detection.
When S4 lands, rerun the affected boundary controls from its plan as its own
commissioned lane; do not count pending/skipped C1153 tests as this plan's evidence.

Delivery watchdog cleanup stays conditional on a real current failure, fresh
non-Working evidence and the established runner safety gate. Preserve S2/S4's
arbitration-before-failure ordering; warning/event failures must never route into
its stopper. CARD-0079 remains independent and its strict same-conversation
resume/receipt contract is unchanged.

### D-5. Scope, collisions, platform and release behavior

No AppHost restart is needed to write this plan, design tests or run isolated
checks. Production service changes in S1-S3 and attention changes in S4 require
**one server/AppHost activation after land**, by the orchestrator from the
canonical checkout under the runbook, followed by `/api/version` SHA verification.
No runner binary/protocol change is planned. Documentation/test-only slices need
no restart. Never restart from this assigned worktree.

Read `/api/runner-defaults` and `/api/session-runners` on 2026-10-08 at approximately
05:48 UTC: defaults revision 2, no kind overrides; catalogue had available Windows
and Linux lanes, one draining Linux entry, and the accepting Linux entry occupied
10/10. This is an observation, not a fleet placement pin. Dispatch omits `-Runner`
and `-Platform` for portable work; refresh both endpoints at dispatch. Only the
FakeGrok ConPTY witness requires `-Platform Windows`. No fleet address/location
belongs in commands or policy. Lane is recorded in each checkpoint's Group.

Serialize source ownership with CARD-1149/1150 S2 repair 2 (Code **44fc0e5a**,
`DispatchBriefEvidence.cs`, `SessionMessageQueueService.DispatchBrief.cs`,
`AgentSessionService.cs`): CARD-1151 reads those APIs and adds boundary tests, but
does not edit those three files. Coordinate shared boundary test partials before
Code. CARD-1153 S4 shares **AgentTaskDispatcher.cs**; do not run concurrent edits
there. Adopt its landed hunk or wait for the file lease; never overwrite either
card's evidence predicates. AgentTaskService.cs also requires a current scope
occupancy check before S3 because of the narrow internal requeue change.

This plan changes no waiting-for-input session state and creates no automatic
release timer. A boot-detected session remains Working and occupies its seat.
An explicit operator action or actual completion releases it under existing
ownership rules. If it later waits for input, **nothing automatically releases it
while parking is off**, and there is no elapsed release guarantee (CARD-1083).
BlockedTaskParking defaults off; neither its opt-in path nor elapsed boot time
may park a Working session.

## Implementation slices

Each slice is 30-60 minutes of authoring; verification is additional. Commit and
push each slice. S1-S3 are one verification group because the full-tick contract
needs the warning writer and safe-absence branch together; do not claim those
intermediate commits green. Run its checkpoints once after all three commits.
Re-read moved line numbers
on the eventual Code baseline and report any API drift rather than weakening tests.

| Slice | Files and concrete work | Tests / completion boundary | Activation / shared files |
|---|---|---|---|
| S1, 60 min | `TaskDeadlinePolicy.cs`, new `BootStallPolicy.cs`, `AgentTaskDispatcher.cs`: retain boot identity across winning clocks, explicit dispositions, short-circuit all live/unknown boot outcomes before failure. | New `BootStallPolicyTests`, `BootStallDetectionTests`; first characterization rename. No automatic boot stop remains reachable from DetectOnly. | Server activation later; Dispatcher shared with gated CARD-1153 S4. |
| S2, 45 min | New `BootStallWarningWriter.cs`, `AgentTaskDispatcher.cs`: Warning event dedup, independent context, stage/time rules, no leaked tracked entities. | `BootStallDetectionTests` telemetry faults, recreation, concurrency and exact event counts. | Server activation later; no queue producer edits. |
| S3, 60 min | `AgentTaskDispatcher.cs`, `AgentTaskService.cs`, boot policy observation adapter: whitelist/revalidation and internal absent-session requeue; remove exhausted/retry-error kill fallbacks. | Safe-dead one-retry/exhausted/fault controls; different-reason and S1 precedence; existing direct overdue assertions explicitly re-scoped. | Server activation later; acquire Dispatcher and AgentTaskService scopes. |
| S4, 45 min | `AttentionService.cs`, `DelegationSettings.cs` comments, boot service comments; `docs/session-runtime-invariants.md`, `docs/orchestration-loop.md`. | New `BootStallAttentionTests`, `BootStallDocumentationTests`; correct displayed outcome at preview/8/20/ceiling, resolve on reply. | Server activation for projection; docs alone need none. |
| S5, 45 min | `BootStallWorkingTickCharacterizationTests.cs`, new `DelegationDispatchRecoveryBoundaryTests.BootStall.cs`, `AgentTaskOverdueDeadlineTests.cs`; budget fixture additions without altering original three arguments. | Complete repeated full-tick, unchanged brief bytes and regression roster; exact SQL rosters for every new branch. | Tests only; coordinate boundary partial ownership with S2 repair. |
| S6, 30-45 min | `GrokDelegateEndToEndTests.cs`: flip the real FakeGrok boot-stall witness, retaining actual submit and post-warning recovery evidence. | Windows ConPTY method only; original brief submitted once, no automatic stop/retry, then the existing retry/success phase requires an explicit operator Retry call. | Tests only, isolated Windows lane; no production services. |

Required documentation sentences, added with concrete test pins in S4:

- “A transcript-confirmed boot prompt with no model reply is detection only while
  the session is Working, runner-listed, or safety evidence is unknown. At eight
  minutes it records BootStallDetected; at the bounded operator threshold (20
  minutes with defaults) it asks for an operator decision without failure, retry,
  input, stop or seat release.” Pin to the renamed full-tick test and
  `C1151_Operator_escalation_preserves_the_attempt`.
- “The general and role deadlines do not terminalize that unresolved boot
  episode; positive model progress returns it to ordinary deadline policy.” Pin
  to `C1151_Boot_protection_survives_all_deadlines`.
- “Only positively non-Working, absent and terminal evidence permits the narrow
  existing boot failure/retry outcome. That automatic retry performs no stop;
  S1's pristine absent-launch hold still takes precedence.” Pin to
  `C1151_Safe_absent_boot_keeps_failure_and_retry` and the S1 control.
- “CARD-0079 is the only automatic Working stop authorization; boot-stall
  detection does not call it. Parking is default-off and provides no release
  deadline for an input-waiting session.” Pin to the compaction regression and
  `C1151_Detection_does_not_release_or_park`.

Correct comments claiming “nothing is lost”/“license to kill” in deadline policy,
dispatcher, settings and the delegate ownership comment in BootReplyWatchdogService.
Do not change that service's separate taskless behavior in this card; record the
remaining discrepancy in the completion report rather than claiming fleet-wide
enforcement. No migrations or generated docs/cards files are edited.

## Verification design

TestDesign is separate. It must inspect the safe-dead fixture, exact runtime DTO
fields, and the new internal retry admission before commissioning Code, implement
or specify every named method, validate argument counts, and publish an amended
manifest if concurrent S2/S4 landings change the roster. This plan's new counts are
designed execution counts, **not measured passing results**. Keep every baseline
assertion unless its behavior change is listed explicitly below.

### Witnesses and explicit assertion changes

Use real PostgreSQL scopes and the existing DelegationTestServices graph. Inject a
FakeTimeProvider; never wait eight/twenty minutes or enlarge production deadlines.
Count both RecordingSessionStopper calls and runner kill/start/release/input/
compaction-stop calls. Also assert task status, attempt, concurrency token,
session binding/generation, agent ownership, failure/completion fields, queue bytes
and event types. A zero direct runner kill count alone is insufficient.

1. Rename `Aged_prompt_only_Working_tick_stops_the_session_and_requeues_once` to
   `Aged_prompt_only_Working_tick_detects_without_stopping_or_requeueing`. Add the
   comment: “CARD-1151 deliberately reverses the CARD-1149/1150 characterization:
   boot silence is detection; the original stop/requeue assertions are replaced
   by positive same-attempt/same-session assertions.” Keep its nine-minute prompt,
   quiet workspace, matching listed generation and default boot wait 8. Expected
   Working, attempt 1, same binding, Running session, zero stopper, no Failed or
   Retried event, one named Warning. Change the concurrency-skip assertion only
   because no Queued retry exists, with that explanation.
2. Rename `AgentTaskOverdueDeadlineTests.a_boot_stall_fails_with_the_code_kills_the_session_and_retries_once`
   to `a_working_boot_stall_warns_without_failure_stop_or_retry`. Keep the Working
   transcript and original input; explicitly replace failure/parent failure-note
   expectations with task Warning and unchanged attempt/parent queue assertions.
   Move its actual safe-absent failure/retry assertions into V-7, not into a fake
   “Failed status means idle” fixture. These are the two primary flipped tests.
3. Keep `Aged_Working_tick_with_an_assistant_row_is_not_stopped` and
   `Young_prompt_only_Working_tick_is_not_stopped`; strengthen each with no boot
   warning and unchanged input assertions. Do not silently delete them.
4. The old second-boot-failure and repeated-alias-hold tests also assume Working
   is killable. Rename/reseed them to explicitly proven absent cases, preserving
   exhaustion and alias-ledger assertions, and add live repeat/no-alias controls.
   Keep the workspace-progress companion and general thinking-row failure test.
5. Rename `GrokDelegateEndToEndTests.a_provider_that_never_answers_the_boot_prompt_is_failed_killed_and_retried_once`
   to `a_provider_that_never_answers_boot_is_detected_until_explicit_retry`.
   Retain real submit/confirmed prompt assertions and one full original brief.
   First prove no automatic stop/requeue across repeated sweeps. Then retain its
   existing second-session success phase, clearing the launch fault and calling
   the ordinary explicit Retry entry deliberately. Attribute its stop only to
   that request, after the detection assertions. No new fake-provider protocol.

### Behavior matrix

All `C1151_*` methods below are new unless the renamed method is stated.

| ID | Method / fixture | Required result and negative control |
|---|---|---|
| V-1 | Renamed full-tick witness, repeat two ticks and a new scoped dispatcher | Same Working attempt/session and one Warning; all destructive counters zero. |
| V-2 | `C1151_Whitelist_requires_positive_evidence`, 12 arguments | Individually remove Working-read success, false verdict, session row, generation match, runner ownership, available inventory, absence, terminal evidence, complete transcript, quiet workspace, no launch owner, or attempt match. Each is DetectOnly. Positive all-fields companion is V-7. |
| V-3 | `C1151_Listed_or_unknown_session_is_untouched`, 8 arguments | Working+listed, Working+empty list, false+listed Running, false+listed Exited, false+wrong-generation entry, unavailable remote, missing directory, null list. No outcome change/stop; named reason retained. |
| V-4 | `C1151_Boot_protection_survives_all_deadlines`, 6 arguments | At 20m, 240m, earlier custom ceiling, model-wait shorter than boot, workspace progress, boot notification disabled: unresolved boot remains open; model reply companion returns to ordinary policy. |
| V-5 | `C1151_Operator_escalation_preserves_the_attempt`, 4 arguments | Before/equal/after operator threshold and restart after it; Error at equality, fixed age, no new attempt/input, at most one event per stage. |
| V-6 | `C1151_Telemetry_failure_never_changes_disposition`, 5 arguments | Event read, insert, commit and publish faults plus a later unrelated SaveChanges; no leaked event/state and no stop. Include safe-absent result unaffected by publish fault in same fixture assertions. |
| V-7 | `C1151_Safe_absent_boot_keeps_failure_and_retry`, 3 arguments | Positively false Working + terminal generation + absent owner inventory + complete evidence: attempt 1 fails/requeues once at same kind/tier; exhausted attempt fails once; retry infrastructure error stays Failed. All three have zero stopper/release/compaction-stop. |
| V-8 | `C1151_Race_revokes_absent_failure`, 4 arguments | New Working row, inventory entry, generation or task attempt change at final barrier: no stale failure/requeue. |
| V-9 | `C1151_Warnings_deduplicate_per_episode`, 4 arguments | Repeated tick, service recreation, concurrent contexts, real later boot prompt: one per key/stage; later prompt can create exactly one new key. |
| V-10 | `C1151_Brief_and_spill_remain_byte_identical`, 3 arguments | Inline, spilled and Pending UI follow-up; hash/file-byte and queue ID/body/sequence/attempt/baseline comparisons through detection and escalation. No ensure/send. |
| V-11 | `C1151_Attention_describes_detection_and_resolution`, 5 arguments | 6.4m, 8m, 20m, past ceiling, model reply. Correct kind/severity/actions; no “will kill/fail/retry” text; row works without a persisted Warning and resolves on reply. |
| V-12 | `C1151_Detection_does_not_release_or_park` | Working seat retained with parking both off/on, no park/release row or calls; open-task session watch continues standing down. |
| V-13 | `C1151_Boot_branch_statement_counts`, 10 arguments | FullCommandCounter on every new path in the table below, every context registered, all reads/writes/faulted commands included. Print actual full tick and branch rosters. |
| V-14 | Existing `C1149_Different_reason_or_attempted_brief_still_uses_failure_policy` and `C1149_Absent_launch_is_blocked_with_original_input` | A different reason still fails; exact pristine S1 shape still holds with original input, never a boot retry. |
| V-15 | `C1151_Delivery_watchdog_stopper_requires_real_safe_failure`, 3 arguments | Real idle failure still uses existing cleanup; Working and unsuccessful/stale failure do not. No warning treated as failure. |
| V-16 | `C1151_Explicit_retry_retains_operator_semantics` | Explicit Retry still calls the established stopper/requeue path; internal absent-proof bypass is inaccessible from ordinary request input. |
| V-17 | `C1151_Docs_describe_detection_and_only_compaction_exception` | Owner sentences/pins match behavior; no boot kill/retry promise remains in the changed delegate sections. |

### Statement accounting

The original hot-path totals remain exactly **18 / 18 / 4**. Do not add evidence,
telemetry queries, a fleet list or a workspace probe ahead of the cheap age gate.
The following are **design caps for new branch work**, not invented baseline full
tick measurements. V-13 must print and pin actual full-path totals before Review;
TestDesign gives each full path its inherited prefix roster as well as this delta.
Do not count only Reader commands or exclude the separate telemetry context.

| New path (V-13 argument) | Additional SQL cap after existing deadline evaluation | Runner calls cap | Explanation |
|---|---:|---:|---|
| young/preview without emission | 0 | 0 | Existing evaluation supplies boot facts; projection is read-only. |
| Working first detection | 3 | 0 additional | Task lock/identity read, duplicate-key read, one event insert. No inventory is needed to veto on true Working. |
| Working repeated episode | 2 | 0 additional | Same lock and duplicate read, no write. |
| operator escalation | 3 | 0 additional | Same episode, second stage event. |
| identity changed before event | 1 | 0 | Lock/read refuses before dedup/insert. |
| event save/publish fault | 3 | 0 additional | Count attempted insert; dispose context. A publish fault adds no SQL. |
| non-Working listed/unknown | 5 | 1 inventory | One loaded session binding + one fresh Working query + at most 3 warning commands. |
| absent proof refused | 8 | 2 inventory, 1 transcript | Prior 5 plus at most 3 terminal/ownership/evidence reads; warning only. |
| safe-absent final revalidation | 10 | 2 inventory, 1 transcript | At most 8 evidence reads plus task-lock identity and final Working read; existing failure/requeue transaction statements reported separately, no telemetry writes on this path. |
| safe-absent race abort | 10 | 2 inventory, 1 transcript | Same bounded observations; no terminal writes. |

Boot facts must be passed out of existing queries rather than loaded twice. A
typed complete observation may require adapting these caps before Code: if a cap
is infeasible, TestDesign records the actual required roster and reason in the
artifact, never silently increases 18/18/4. No measurement is claimed by this Plan.

### Mutation controls

Run after ordinary green as a separate Mutation stage, one exact method filter per
cycle, including the trailing wildcard for argument-expanded methods. No whole
class or suite PC run. A red is the named outcome assertion, never compilation,
fixture failure, a skipped test or zero selection. Restore then run the same
method green; retain per-cycle source SHA and unedited receipts externally for
SourceLanding. Code does not run these additional mutations as ordinary rows.

| PC | Compiling defect | Exact test method and expected red |
|---|---|---|
| PC-1 | Restore old boot FailAndNotify/Retry route for true Working | `BootStallWorkingTickCharacterizationTests/Aged_prompt_only_Working_tick_detects_without_stopping_or_requeueing*`: status/attempt/stopper mismatch. |
| PC-2 | Treat one unknown whitelist field as admitted, one field per cycle | `BootStallPolicyTests/C1151_Whitelist_requires_positive_evidence*`: admitted disposition for the named missing field. Twelve cycles. |
| PC-3 | Trust desktop inventory for a remote owner or ignore listed Exited/generation mismatch | `BootStallDetectionTests/C1151_Listed_or_unknown_session_is_untouched*`: attempt/state mutation. Three cycles. |
| PC-4 | Restore model-wait fallback; separately discard boot when ceiling wins | `BootStallDetectionTests/C1151_Boot_protection_survives_all_deadlines*`: Failed replaces open. Two cycles. |
| PC-5 | Reset operator due time on each tick | `BootStallDetectionTests/C1151_Operator_escalation_preserves_the_attempt*`: missing Error at exact boundary. |
| PC-6 | Route telemetry exception into failure; separately share the outcome context | `BootStallDetectionTests/C1151_Telemetry_failure_never_changes_disposition*`: state change or leaked event after later save. Two cycles. |
| PC-7 | Return DetectOnly unconditionally for proven absence; separately bypass attempt cap | `BootStallDetectionTests/C1151_Safe_absent_boot_keeps_failure_and_retry*`: missing retry or excess attempt. Two cycles. |
| PC-8 | Omit final Working/identity recheck | `BootStallDetectionTests/C1151_Race_revokes_absent_failure*`: stale failure. |
| PC-9 | Omit duplicate-key read under lock | `BootStallDetectionTests/C1151_Warnings_deduplicate_per_episode*`: extra events. |
| PC-10 | Recompose the brief during warning | `DelegationDispatchRecoveryBoundaryTests/C1151_Brief_and_spill_remain_byte_identical*`: changed bytes/IDs or extra input. |
| PC-11 | Keep existing “will kill/retry” attention wording or require Warning persistence | `BootStallAttentionTests/C1151_Attention_describes_detection_and_resolution*`: wrong visible result. Two cycles. |
| PC-12 | Turn detection into a park/release request | `BootStallDetectionTests/C1151_Detection_does_not_release_or_park*`: nonzero release/park evidence. |
| PC-13 | Add SELECT 1 before hot age gate; separately add one before inside-grace return | `DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*`: 19 versus 18 or 5 versus 4. Two cycles. |
| PC-14 | Treat warning as successful delivery failure | `BootStallDetectionTests/C1151_Delivery_watchdog_stopper_requires_real_safe_failure*`: forbidden stopper call. |
| PC-15 | Re-enable stopper in absent retry or its catch fallback | `BootStallDetectionTests/C1151_Safe_absent_boot_keeps_failure_and_retry*`: nonempty stopper. |
| PC-16 | Apply automatic absent bypass to explicit human Retry | `BootStallDetectionTests/C1151_Explicit_retry_retains_operator_semantics*`: missing expected explicit stop. |

### Execution and cost

Portable rows use the available .NET/PostgreSQL lane with no runner/OS pin. All
Postgres rows are serial, including builds, with `TUNIT_MAX_PARALLEL_TESTS=1`.
The single native witness uses an isolated Windows ConPTY/FakeGrok lane and the
assembly's ProcessSpawnLimit. No production runner, provider or messaging broker.
Do not co-schedule Antiphon.Agents.Pty.Tests.

Run each committed slice group with the checkpoint tool (`run --plan <this path>
--after <After token>`), using its host build-slot gate. Bootstrap builds, if needed,
also use scripts/build-slot.ps1. Wait to a final result (repeat wait after exit 75)
within the owning turn. No source edits during a run. Slot timeout is not-run, not
permission to run unleased. Reused outputs must have the same After token. Delete
all generated `bin-c1151-*` directories when complete. Retain ignored receipts
and report per-CP executed/passed/failed/skipped counts and exact source/build SHA.

No whole-Unit or whole-assembly runs. Establish inherited red on the recorded
base using only the failing method, without weakening assertions or retries.
Regression classes below are selected for claim, launch, resume, overdue,
dead-session and delivery invariants; CARD-1153 pending methods are excluded by
positive method prefixes. TestDesign must count the final regression roster at its
baseline. Regression floors below are source-counted `[Test]`/`[Arguments]`
executions at the planning baseline, not passing test results. Preserve or grow
those rosters; a rename is not permission to lose a case.

Authoring estimate: 285-300 minutes. Ordinary checkpoint floor: **111 minutes**,
the sum of EstimatedMinutes (includes builds). Mutation: **34** method-scoped
cycles, roughly **102 minutes**; count variants, not 16 PC headings. Native Windows
availability is a separate lane obligation, never a skipped-pass substitute.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1151-core/` | portable-working | `/*/*/BootStallWorkingTickCharacterizationTests/Aged_prompt_only_Working_tick_detects_without_stopping_or_requeueing*` | V-1 | 1 executed, 0 failed/skipped | 1 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S3 | `CP-1` | portable-whitelist | `/*/*/BootStallPolicyTests/C1151_Whitelist_requires_positive_evidence*` | V-2 | 12 executed, 0 failed/skipped | 12 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S3 | `CP-1` | portable-listed | `/*/*/BootStallDetectionTests/C1151_Listed_or_unknown_session_is_untouched*` | V-3 | 8 executed, 0 failed/skipped | 8 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S3 | `CP-1` | portable-deadlines | `/*/*/BootStallDetectionTests/C1151_Boot_protection_survives_all_deadlines*` | V-4 | 6 executed, 0 failed/skipped | 6 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1-S3 | `CP-1` | portable-operator | `/*/*/BootStallDetectionTests/C1151_Operator_escalation_preserves_the_attempt*` | V-5 | 4 executed, 0 failed/skipped | 4 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1-S3 | `CP-1` | portable-telemetry | `/*/*/BootStallDetectionTests/C1151_Telemetry_failure_never_changes_disposition*` | V-6 | 5 executed, 0 failed/skipped | 5 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S1-S3 | `CP-1` | portable-dedup | `/*/*/BootStallDetectionTests/C1151_Warnings_deduplicate_per_episode*` | V-9 | 4 executed, 0 failed/skipped | 4 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S1-S3 | `CP-1` | portable-absent | `/*/*/BootStallDetectionTests/C1151_Safe_absent_boot_keeps_failure_and_retry*` | V-7 | 3 executed, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S1-S3 | `CP-1` | portable-race | `/*/*/BootStallDetectionTests/C1151_Race_revokes_absent_failure*` | V-8 | 4 executed, 0 failed/skipped | 4 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S1-S3 | `CP-1` | portable-explicit-retry | `/*/*/BootStallDetectionTests/C1151_Explicit_retry_retains_operator_semantics*` | V-16 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S4 | `tests/Antiphon.Tests -> bin-c1151-s4/` | portable-attention | `/*/*/BootStallAttentionTests/C1151_Attention_describes_detection_and_resolution*` | V-11 | 5 executed, 0 failed/skipped | 5 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S4 | `CP-11` | portable-docs | `/*/*/BootStallDocumentationTests/C1151_Docs_describe_detection_and_only_compaction_exception*` | V-17 | 1 executed, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | S5 | `tests/Antiphon.Tests -> bin-c1151-s5/` | portable-brief | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1151_Brief_and_spill_remain_byte_identical*` | V-10 | 3 executed, 0 failed/skipped | 3 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | S5 | `CP-13` | portable-no-park | `/*/*/BootStallDetectionTests/C1151_Detection_does_not_release_or_park*` | V-12 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | S5 | `CP-13` | portable-new-cost | `/*/*/BootStallDetectionTests/C1151_Boot_branch_statement_counts*` | V-13 | 10 executed, exact rosters, 0 failed/skipped | 10 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | S5 | `CP-13` | portable-hot-cost | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*` | V-13 | all listed; original 18/18/4 unchanged; 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-17 | S5 | `CP-13` | portable-other-reason | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Different_reason_or_attempted_brief_still_uses_failure_policy*` | V-14 | 3 executed, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-18 | S5 | `CP-13` | portable-s1-hold | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Absent_launch_is_blocked_with_original_input*` | V-14 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-19 | S5 | `CP-13` | portable-watchdog-safety | `/*/*/BootStallDetectionTests/C1151_Delivery_watchdog_stopper_requires_real_safe_failure*` | V-15 | 3 executed, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-20 | S5 | `CP-13` | portable-characterization | `/*/*/BootStallWorkingTickCharacterizationTests/*` | R-boot | all 3 named witnesses, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-21 | S5 | `CP-13` | portable-overdue | `/*/*/AgentTaskOverdueDeadlineTests/*` | R-deadlines, second flip, absent exhaustion/alias | all listed, 0 failed/skipped | 29 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-22 | S5 | `CP-13` | portable-policy | `/*/*/TaskDeadlinePolicyTests/*` | R-clocks | all listed, 0 failed/skipped | 26 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-23 | S5 | `CP-13` | portable-boot-predicate | `/*/*/BootReplyWatchTests/*` | R-replies | all listed, 0 failed/skipped | 27 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-24 | S5 | `CP-13` | portable-recovery-boundary | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_*` | R-S1 | all listed, 0 failed/skipped | 1 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-25 | S5 | `CP-13` | portable-dead-session | `/*/*/AgentTaskDeadSessionReconciliationTests/*` | R-dead | all listed, 0 failed/skipped | 25 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-26 | S5 | `CP-13` | portable-delivery | `/*/*/AgentTaskDeliveryWatchdogTests/*` | R-delivery | all listed, 0 failed/skipped | 76 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-27 | S5 | `CP-13` | portable-claim | `/*/*/AgentTaskConcurrencyLimitTests/*` | R-claim | all listed, 0 failed/skipped | 25 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-28 | S5 | `CP-13` | portable-claim-predicates | `/*/*/AgentTaskDispatcherPredicateTests/*` | R-predicates | all listed, 0 failed/skipped | 17 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-29 | S5 | `CP-13` | portable-launch-failure | `/*/*/AgentTaskDispatchFailureTests/*` | R-launch | all listed, 0 failed/skipped | 15 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-30 | S5 | `CP-13` | portable-launch-owner | `/*/*/AgentSessionLaunchQueueOwnershipTests/*` | R-launch-custody | all listed, 0 failed/skipped | 7 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-31 | S5 | `CP-13` | portable-resume | `/*/*/AgentSessionInterruptedLaunchResumeTests/*` | R-resume | all listed, 0 failed/skipped | 8 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-32 | S5 | `CP-13` | portable-compaction | `/*/*/CheckCompactionContinuationTests/*` | R-C0079 | all listed, 0 failed/skipped | 2 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-33 | S5 | `CP-13` | portable-compaction-flow | `/*/*/CheckCompactionRecoveryFlowTests/*` | R-C0079-conditional-stop | all listed, 0 failed/skipped | 1 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-34 | S6 | `tests/Antiphon.Tests -> bin-c1151-s6/` | windows-conpty-boot | `/*/*/GrokDelegateEndToEndTests/a_provider_that_never_answers_boot_is_detected_until_explicit_retry*` | R-native | 1 executed, 0 failed/skipped | 1 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

## Handoff

TestDesign audits the positive safe-absence proof and internal no-stop requeue,
builds the exact test/argument roster, preserves the listed assertion changes,
and reconciles the S2/S4 file gates. Do not dispatch Code from this Plan alone.
No tests/builds were run during planning; the source-ground-truth and HTTP reads
are planning evidence only. The separate taskless BootReplyWatchdog stop remains
an explicitly reported follow-up, not an implied completed fix.
