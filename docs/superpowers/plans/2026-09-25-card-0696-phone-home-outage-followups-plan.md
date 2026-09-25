# CARD-0696: preserve mentions and input state across phone-home outages

Date: 2026-09-25. Plan task: `53529ab7`. Next stage: **Code** on server2, using Codex Astra/sol under the caller's standing authority.

Persist each accepted mention in the existing session message queue, return a typed retryable refusal for immediate input that cannot reach its runner, and bound the pre-inventory session-ID read with a five-second in-memory snapshot. Keep unknown sessions distinct from confirmed-gone sessions. This plan changes no implementation; the tests below are designs, not executed evidence.

## Evidence and scope

Read CARD-0696, CARD-0698 and CARD-0701 through `scripts/card.ps1 get`. Fetched and inspected `origin/master` at **`e0be7f5e62104165e39b63d66b222b81737ba25b`**, equal to this worktree's starting HEAD. A second fetch during planning returned the same SHA. CARD-0679 R6's published evidence commit `323872ed` is present. The caller reports it live; this Plan did not independently inspect the deployed version or interrupt the runner.

| Finding | Ground truth at that SHA | Design consequence |
|---|---|---|
| Outage drops mentions | `AgentChannelService.RouteMentionAsync` resolves a live-or-unknown target, then calls `AgentSessionRuntime.SendInputAsync` directly. `AgentMentionRouter.ProcessAsync` catches the resulting exception, logs `RouteFailed`, and consumes the command. No durable input row exists. | Persist before attempting transport; let queue recovery own subsequent delivery. |
| Mode=Now before first List says Herdr unreachable | `SessionMessageQueueService.EnqueueAsync` admits list membership, then reaches metadata/delivery guards. `DeliverAsync` maps a pre-body `phone_home_unavailable` into `BackendUnreachable`; Mode=Now renders that verdict as a Herdr 409. | Preserve the outage's typed cause and check runner availability before immediate-send mutations. |
| Send-now during reconnect says 409 | `SendNowAsync` has a 503 guard only for `RemoteInventoryPending`, which becomes false after the first successful List. A disconnected previously recovered runner reaches the general delivery-failure/409 path. | The guard must use current dispatch eligibility, socket and lease, not only first-List state. |
| Pending inventory reads scale with callers | `PhoneHomeRunnerDirectory.UnknownRemoteSessionIds` calls synchronous `BoundSessionIds` whenever `_lastRecovered` is null and the accepted runner is pending. Each call executes the existing RunnerId-filtered `AgentSessions` projection. `ListLiveOrUnknownSessions` invokes it on every call. | Cache that projection, with single-flight refresh and explicit failure behavior. Do not add another index for an already indexed lookup. |
| Schedule preview lacks coverage | `ScheduleService.BuildPreviewAsync` already uses `ListLiveOrUnknownSessions`, as does prompt fire. R6 tests fire, channel target resolution and pre-first-List send-now in `PhoneHomeStrandedQueueTests`; none calls preview in that window. | Add a real preview regression and demonstrate it red with a narrow intentional regression. Do not claim preview currently returns the wrong answer. |
| Adjacent work | CARD-0698 is InProgress; its indexed-query plan is on master (`d2236e9e`), but its implementation is not at the inspected SHA. CARD-0701 is Backlog and asks for an ingest-updated session-state cache. | Recheck their state before Code. Preserve CARD-0698's query/index changes when rebasing. This card caches only pending membership, never working/idle or transcript state. |

Owners: [project conventions](../../project-context.md), [runtime invariants](../../session-runtime-invariants.md), [Herdr delivery](../../herdr-sessions.md), [HTTP operations](../../ops-http.md), and [testing/checkpoints](../../testing-and-build.md). Adjacent plan: [CARD-0698](2026-09-25-card-0698-postgres-load-plan.md).

In scope: server mention acceptance, session queue admission/error classification, the directory's pending-ID projection, affected liveness gates and deterministic integration coverage. No runner protocol change, transcript working-state cache, database retention change, production outage exercise, native CLI qualification, deployment or board status change is required in Code. No schema migration is expected.

## Decisions

### D-1: the queue row is the durable mention route

Use the same path for a local target and a phone-home target:

1. Preserve current source eligibility, same-board target resolution, exact-name/ID-prefix matching, ambiguity and self-routing rules. Resolve the destination **once**. A runner outage does not make its still-active bound session an absent target.
2. Allocate an occurrence GUID when the router creates a `MentionRouteCommand`. Preserve it through that command's acceptance. Introduce a narrow internal queue entry point for mentions which uses this GUID as `SessionQueuedMessage.Id`; share ordinary queue insertion/sequence/validation code instead of cloning delivery code. Direct service calls without a supplied occurrence ID allocate one for that invocation.
3. Insert the fully formatted body and target session as a `WhenIdle` row with new additive enum value `QueuedMessageOrigin.Mention = 7`. Existing enum numbers stay fixed. Preserve `[channel from <definition>]` text; queue encoding owns line normalization, bracketed paste and the separate Enter. Store no raw keystroke payload as the route.
4. Commit with inline delivery disabled. Only after acceptance may a best-effort ordinary `FlushIfIdleAsync` run. Transport failure in that flush cannot undo acceptance or make the router create another row. A busy target waits until idle, on either backend; an idle available target is flushed promptly.
5. Add Mention to `QueueAttention.MachineOriginNeedsAttention`, so the existing stranded sweep finds a mention to a non-AlwaysOn card session after recovery even if no new turn-end arrives. Keep it one row per turn: do not add Mention to Channel/Delegation coalescing. The existing turn-end, startup and stranded recovery paths do the retrying.
6. Queue insertion recognizes the occurrence ID under the per-session lock and the database primary-key constraint. Same ID, destination and normalized body returns the existing row without changing its delivery state; a different destination/body/origin refuses the identity collision. Handle a concurrent unique violation by reading and validating the winner, not by generating a new ID. Two separate occurrences with identical text remain two messages.

No separate retry worker, route table or migration is needed. The queue row stores the frozen target, body and occurrence identity, and survives replacement of the router/runtime/service provider. On restart recovery reads that row; it does not rescan source text or re-resolve an agent name. If its target subsequently ends, retain existing dead-session/queue lifecycle behavior; never redirect it to a different session with the same name or launch a replacement because of the mention.

The durability boundary is the accepted database insert. This card does not make the router's pre-acceptance `ObserveDelta`/debounce buffer a durable transcript consumer or solve loss on a process crash before acceptance. Its existing duplicate suppression for a debounced partial line followed by its completed newline must continue to yield one occurrence. Recovery of an accepted occurrence must not depend on that memory.

Publish the existing `ChannelMessage` activity after queue acceptance and interpret `RouteMentionAsync == true` as accepted for delivery. Record a `queued` diagnostic with the queue ID instead of falsely recording `InputSent` at acceptance. A failed activity publication never re-enqueues. Queue state and transcript confirmation remain the delivery evidence. Keep the explicit `SendToSessionAsync` API's existing behavior in this card; its R6 outage test remains a regression control, separate from mention acceptance.

Rejected: retrying raw `SendInputAsync` after any exception (an in-flight write may already have submitted); an in-memory retry list (lost on server restart); text hashes as occurrence identity (collapse legitimate identical mentions); Channel origin (external reply correlation requires a ConversationKey and is a different contract); Delegation origin (different batching and completion semantics); changing every System-origin row's sweep eligibility just to recover mentions.

### D-2: typed immediate admission and exact state preservation

Apply one shared admission/error policy to Mode=Now, `SendNowAsync`, and the adjacent `EnqueueDeliveringNowAsync` path so another immediate entry point does not retain the same defect.

| Actual condition | Result | Durable effect |
|---|---|---|
| Accepted remote runner has not supplied its first List; is disconnected/recovering; has a closed socket; or its lease expired | `ServiceUnavailableException` with code `phone_home_unavailable`, HTTP 503 | Mode=Now creates no row. Send-now leaves the existing row and attempt evidence unchanged. EnqueueDeliveringNow refuses before insertion. |
| Live remote inventory merely aged stale, but the current connection can dispatch | Attempt ordinary delivery through that connection | Stale inventory alone is not a transport outage or evidence of death. |
| Local session or connected remote session is busy, Starting, modal-blocked, rules-held, or genuinely Herdr-unreachable | Preserve the applicable current terminal/readiness contract | Do not relabel a permission UI or local Herdr failure as phone-home failure. |
| Missing session, or confirmed-gone session after successful inventory/exit/kill | Preserve the applicable not-found/not-live refusal | Do not resurrect it from the pending cache. |
| A body or Enter might have reached the runner | Preserve typed transport uncertainty and existing attempt/baseline recovery | Never translate uncertainty into a safe-to-retry, unchanged-state refusal. |

Add a narrow runtime admission method backed by the existing `ISessionRunnerDirectory.GetBindingAsync` and `Resolve`: local binding is a no-op; missing binding is missing; remote binding must resolve the current eligible connection. Do not pin the returned client for a later write, and do not use `TryGetLiveMetadata`'s catch-all false as availability evidence. `Resolve` already checks recovery/socket and calls the lease-aware snapshot. Reuse session identity/status reads where possible instead of adding one database read per guard.

For a valid existing remote session, check transport admission before the legacy synthetic Herdr/readiness path, then recheck under the queue lock **before** rules recovery, late-confirm, spill preparation, attempt claim, timestamp/verdict changes or input. Preserve request validation and ownership checks. Do not return a successful queue response for a refused immediate request.

The socket can close after preflight. Retain a typed unavailability code in the private `DeliveryOutcome` when the **body write was definitely not sent**. Map phone-home unavailable/connection-closed-before-send at that boundary to 503 `phone_home_unavailable`; genuine Herdr unavailability retains its current response. Do not infer the cause from the English `Describe(BackendUnreachable)` text, and do not alter the wire-level error codes of `PhoneHomeLiveConnection`.

For send-now's race after a claim but before any body byte left, restore the pre-call row snapshot in the same locked operation: Status, Body/spill fields, SentAt, DeliveryAttempts, LastDeliveryStartedAt, LastDeliveryGeneration, LastDeliveryBaselineSequence, verdict/timestamp and any other field that path changed. A previously attempted Pending row must keep its **old** baseline/verdict, not have them nulled by a generic refund. No incident, park, kill, follow-up queued message or RunAttempt/card transition is caused by this pre-body refusal. If EnqueueDeliveringNow inserted a provisional row in that same no-body race, remove only its newly created, never-written row before returning 503; never remove another call's row. An operation with uncertain body transmission retains its durable evidence instead.

Keep CARD-0693's boundary: an overlay Esc before the body does not charge a body attempt, but a body followed by an unavailable Enter is **not** refundable. Caller cancellation and in-flight/time-out errors must not be normalized to pre-send unavailability. Automatic WhenIdle delivery still defers Pending and does not throw an immediate HTTP refusal. No native typing algorithm changes are planned.

### D-3: one five-second pending membership snapshot per accepted runner

Implement a small concrete `PendingRunnerSessionInventory` helper under `Infrastructure/Agents/SessionRunner`, owned by the DI singleton directory and using its `TimeProvider`/scope factory. Its only durable source is the current indexed `AgentSessions` projection of IDs bound to the configured runner in Starting/Running/Stopping. No tracked entities, transcript values, per-caller caches, global/static state, or local session IDs are stored.

* Cold pending read loads once; concurrent callers join one refresh. Cache empty results too. A successfully completed load expires five seconds after completion, measured with injected time; reads do not extend expiry. Sustained polling during an indefinite outage produces at most one projection read per five seconds after the cold load, per process/accepted runner, rather than one per caller.
* Keep the current synchronous directory interface for this bounded projection. Use a separate single-flight/cache lock; never hold the directory connection `_gate` during database I/O. Return an immutable snapshot/copy, not a mutable collection a caller can corrupt.
* Refresh failure cannot become an empty successful inventory. Keep the previous successful set as conservative unknown knowledge, throttle the next refresh attempt for five seconds, and log the failure once per attempted refresh. With no successful snapshot, propagate the unavailable read (including to joined callers) instead of declaring every session gone; cache the retry deadline so repeated reads cannot hammer a failing database.
* `MarkRecovered` discards/bypasses the pending snapshot immediately. After a load completes, recheck under the directory gate whether an authoritative first List won the race. If it did, return current authoritative live/unknown membership, not the just-loaded stale pending set. Reconnect gaps after that point use `_lastRecovered` exactly as R6 does, with no return to the database bootstrap query. Disabled and unaccepted runners never query.
* The timeout expires **knowledge freshness**, never a session's right to remain unknown. Do not expire unknown sessions into gone or turn the cache TTL into an outage deadline. Connection inventory exits, kills and generation tombstones retain priority.

**A cached negative is not a death verdict.** A new session row can be committed after an empty snapshot. Preserve correctness without building CARD-0701's write-invalidation graph here: gates that already know a session row use its active status plus `IsLiveOrUnknown(id, runnerId)`; that method's pending-runner arm does not depend on cached membership. Terminal DB rows remain terminal even though the accepted runner is pending.

Audit these exact list-negative sites as part of this slice:

| Caller | Required treatment of a pending cache miss |
|---|---|
| `ScheduleService` prompt fire and `BuildPreviewAsync` | Use the session row already loaded, including active status and RunnerId; no extra query per call. |
| `AgentChannelService.LoadLiveTargetSessionAsync` | Read the requested row first, then use the row-aware gate. |
| `AgentChannelService.FindMentionTargetsAsync` | Its existing same-board active-session query admits either an ID in the snapshot or a row bound to the accepted pending runner, then applies the unchanged name/ID and ambiguity rules. Do not miss a newly committed target. |
| Queue Now / EnqueueDeliveringNow / send-now | Shared D-2 session admission must not reject a remote active row solely because a TTL snapshot omits it. |
| Queue inline WhenIdle and stranded candidate filter | Preserve a newly accepted pending row. Reuse a bounded candidate/session projection for the sweep, not one binding query per candidate or a second full pending inventory scan per List call. |
| Runtime manual-turn wait's two list-negative checks | Carry the known session RunnerId into the wait and use the pending-aware predicate; a snapshot miss must not fail a StreamingTurn. Preserve the confirmed-gone outcome. |

Tests must cover insert/terminal transitions within a warm TTL, empty caches, concurrent readers, failed refreshes and recovery overtaking a blocked query. Set membership may lag a DB edit by five seconds, but admission cannot confuse this lag with confirmed absence. This narrow helper and its membership tests are the explicit absorption boundary for CARD-0701; that card may replace the TTL loader with committed lifecycle invalidation without changing these semantics.

Rejected: a permanent bootstrap snapshot (misses later DB changes), only caching nonempty results (outage still hammers an empty database), extending TTL on every hit (never refreshes under load), querying again for every cache miss (reintroduces amplification), retaining the pending union after recovery (resurrects gone sessions), and changing `IsWorkingBatchAsync`, ingest dedup or CARD-0698's indexes. A global SaveChanges interceptor/summary table would enlarge this card into CARD-0701, including transaction-commit and bulk-update semantics.

### D-4: preview is read-only and agrees with fire

For an active persistent session bound to the accepted runner before its first List, preview stays queueable: `Target.Live == true` retains this DTO's existing live-or-unknown meaning, effect says WhenIdle enqueue, and there is no down/SkippedNoSession warning. It spends nothing, starts nothing, and writes no schedules, fires or queued messages. Do not add a new UI status enum or claim the transport is reachable. After an authoritative List omits that session, preview reports down and the configured Skip/Queue warning as today. Missing/terminal and local cases remain distinct.

The corresponding current-code test should already pass. Its red proof is a one-line mutation of **the production preview predicate only** back to `ListLiveSessions().Contains(parsed)`. The before-first-List preview must then fail on its actual returned DTO; restore the production file before implementation. A self-comparison, test-fake-only mutation, compile failure, or test-list output is not red evidence.

## Rounds and implementation slices

One Code task completes the following slices in order; separate Code rounds are unnecessary unless a checkpoint finds a new issue.

1. **S1 — executable red probes.** Add the five `C696Red_` tests below using existing public entry points, and the first preview test. Test-only harness extensions may attach a DbCommandInterceptor to directory-created contexts and recreate providers over the same isolated schema. Commit test changes. Execute CP-1 and the narrowly restored preview mutation CP-2. Record assertion failures at the intended boundaries, not merely nonzero exit codes.
2. **S2 — durable mention acceptance.** Implement D-1 and its remaining tests. Extend the existing AgentChannel test DI harness with the real queue and transcript-producing fake adapter; retain all scanner/chunking and event assertions. Register shared queue dependencies through established test helpers. No circular construction of singleton runtime/router and scoped channel service.
3. **S3 — immediate refusal consistency.** Implement D-2 and its remaining tests, including exact old-attempt restoration, first-body/Enter boundaries and HTTP Problem Details. Read current CARD-0698 changes before editing `SessionMessageQueueService`; leave its working-state SQL intact.
4. **S4 — bounded inventory and preview.** Implement D-3/D-4 and remaining tests, including row-aware gates. Add no transcript cache or migration. Commit S2-S4 before the final green checkpoint build; the table deliberately batches their verification into one build.
5. **S5 — owner docs and final verification.** Update the phone-home bullets in `docs/session-runtime-invariants.md` and immediate-send guidance in `docs/ops-http.md` with mention acceptance, 503 state preservation and the pending-set freshness bound. Update DTO/enum comments. Run CP-3 through CP-5, report counts, commit evidence and push the task branch. Request Review next from Code; no deployment in this task.

Likely implementation footprint: `AgentMentionRouter`, `AgentChannelService`, `SessionMessageQueueService`, `QueueAttention`, `AgentSessionRuntime`, `ScheduleService`, `QueuedMessageOrigin`, `SessionQueueDtos`, `PhoneHomeRunnerDirectory`, the new infrastructure helper, applicable DI registration, four new test classes, the two existing phone-home/channel harnesses and the two owner docs. No edits under generated `docs/cards/`. The web queue DTO already uses string origin and needs no client change just to display the new ordinary queued row.

## Verification design

Use `TestDbFixture.CreateIsolatedSchemaAsync`, the real `PhoneHomeTestHost`/scripted WebSocket peer, production directory/recovery pump and queue. Local parity uses the established adapter harness with actual queue transcript matching. Provider recreation must dispose all old router/queue/runtime objects while retaining only database rows and the separately controlled runner peer. Bind assertions to seeded IDs, never global shared-Postgres counts. Fake queue clocks must advance timers or run as an offset over real time; a frozen deadline with real delays hangs this service.

New classes below are Integration. Specify the listed names as ordinary unparameterized `[Test]` methods: **30 executions total**. Matrix rows inside a method are separately described evidence, not extra TUnit executions. Use unique sessions per case. No real CLI, local production runner, browser, pty host or external channel/broker is involved. HTTP tests reuse `AntiphonWebAppFactory`'s production-runner guard and substitute the controlled graph; do not launch a standing interpreter.

### Red and green roster

`PhoneHomeOutageMentionTests` — V-1 through V-8:

| ID | Method | Required evidence / red target |
|---|---|---|
| V-1 | `C696Red_Mention_before_first_List_is_persisted` | Drive a real router delta naming one remote target before any List. Eventually exactly one target queue row exists Pending, zero attempts/input; recover, sweep, and assert one complete submitted prompt. Baseline logs/drops and has no row. |
| V-2 | `C696Red_Mention_in_reconnect_gap_survives_restart` | Recover A, disconnect, accept a mention, dispose/rebuild the desktop graph, recover B and sweep a non-AlwaysOn target. One persisted route and one submission. Baseline has no durable route. |
| V-3 | `Busy_local_and_remote_mentions_wait_for_idle` | Both targets persist while working, send nothing, then deliver once on turn-end with matching full UserPrompt. Pins the deliberate shared WhenIdle semantics. |
| V-4 | `Occurrence_replay_is_idempotent_but_identical_new_mentions_are_distinct` | Concurrent acceptance of the same occurrence produces one row; replay after graph recreation/settlement does not reset it. A second occurrence with identical text produces a second row; mismatched replay refuses. Mutation: remove keyed insertion validation. |
| V-5 | `Split_line_and_completed_newline_accept_one_occurrence` | Existing debounce/chunk path through the router, completed newline after debounce, exactly one queue row and one prompt. Mutation: bypass pending-key suppression. |
| V-6 | `Accepted_mention_keeps_original_target_after_source_ends` | Persist then end the source and introduce another similarly named target. Recovery uses the original row/session once. Missing, ambiguous, self and other-board controls still accept none. Mutation: re-resolve source/target on retry. |
| V-7 | `In_flight_mention_Enter_late_confirms_without_retyping` | Drop the actual WebSocket under Enter after the runner recorded a complete prompt. Restart/reconnect, sweep and assert no second body, retained attempt floor and LateConfirmed. Mutation: refund an in-flight attempt. |
| V-8 | `Queued_activity_failure_does_not_duplicate_or_block_recovery` | Fail the event bus after committed insertion; another mention can still be processed. No second row on recovery; no claim of InputSent merely from acceptance. Mutation: move insert after publish or retry raw input. |

`PhoneHomeImmediateSendTests` — V-9 through V-18:

| ID | Method | Required evidence / red target |
|---|---|---|
| V-9 | `C696Red_ModeNow_before_first_List_returns_503_without_mutation` | Real `/messages` Mode=Now request with active remote row: 503 and exact code, no new row, input, incident or RunAttempt/card change. Baseline 409. |
| V-10 | `C696Red_SendNow_in_reconnect_gap_returns_503_without_mutation` | Real send-now route after a successful A List then disconnect: 503/code and exact persisted row snapshot unchanged. Seed an older attempted Pending row to expose erased evidence. Baseline 409/mutated attempt fields. |
| V-11 | `Immediate_entry_points_share_all_outage_states` | Now, send-now and EnqueueDeliveringNow across no first List, recovering connection, closed socket and expired lease. Assert 503/code and unchanged ownership/queue state; include Starting remote row to pin transport precedence. |
| V-12 | `Recovered_remote_and_local_immediate_sends_deliver_once` | Both backends, both public HTTP operations plus durable immediate helper, complete prompt confirmation, expected queue/no-row contract. |
| V-13 | `Body_not_sent_after_preflight_restores_the_entire_old_attempt` | Barrier closes transport between locked admission and first body write. Snapshot comparison includes prior baseline/generation/verdict/spill state. 503, zero body writes; provisional durable-immediate row absent. Mutation: generic clear/refund instead of restore. |
| V-14 | `Body_sent_but_Enter_unavailable_keeps_uncertain_attempt` | Body actually recorded, Enter cannot leave; preserve attempt and floor, do not return an unchanged-state safe-retry result. Subsequent Enter-only/late-confirm never retypes. |
| V-15 | `In_flight_send_and_caller_cancellation_are_not_safe_refusals` | Controlled in-flight body/Enter drop and canceled operation separately preserve the applicable transport/cancellation result and evidence. Mutation: catch every transport exception as unavailable. |
| V-16 | `Terminal_and_missing_sessions_are_not_outages` | Missing, Stopped/Failed and authoritative omitted/exit cases keep their normal refusals; no cache resurrection. |
| V-17 | `Local_Herdr_and_modal_guards_keep_their_existing_contract` | Local unreachable/blocked/Starting and remote connected genuine Herdr failure, plus modal/rules hold. No phone-home code unless the connection itself is unavailable. |
| V-18 | `WhenIdle_outage_remains_accepted_and_preserves_attempts` | Both fresh and previously attempted Pending rows remain queued without immediate-send exception, parking or kill; recovery uses normal verification. |

`PhoneHomePendingInventoryTests` — V-19 through V-26:

| ID | Method | Required evidence / red target |
|---|---|---|
| V-19 | `C696Red_Pending_inventory_reads_are_bounded` | Intercept the actual bound-ID SELECT. 100 sequential plus 32 concurrent ListLiveOrUnknown calls at frozen inventory time execute exactly one such query and return the expected IDs; repeat with an empty projection. Baseline executes per call. Do not count unrelated local List/binding/queue SQL. |
| V-20 | `Expiry_refreshes_once_and_hits_do_not_slide_the_deadline` | At 4.999s no second query; at 5s exactly one refresh even with concurrent readers. After several intervals and a long outage, retained active IDs remain unknown. Mutation: bypass TTL or slide on hits. |
| V-21 | `Warm_negative_does_not_hide_a_newly_committed_target` | Warm empty set, insert active remote row within TTL, then mention resolution, schedule preview/fire, immediate refusal and WhenIdle admission all use row-aware unknown semantics. Include manual-turn/sweep negative gate regression in the harness. Mutation: use list absence as death. |
| V-22 | `Terminal_rows_and_unaccepted_bindings_do_not_gain_liveness` | Status/RunnerId changes during a warm TTL; row-aware named gates reject terminal/unaccepted targets immediately. Refreshed set drops them. |
| V-23 | `First_List_overtakes_blocked_pending_query` | Hold projection completion, recover with an empty/newer authoritative List, then release query. No old ID returned live or unknown and no later bootstrap reads. Mutation: publish the load without rechecking recovery. |
| V-24 | `Recovered_inventory_and_tombstones_bypass_pending_cache` | Nonempty recovery, launch/exit, later reconnect and late reply; previous cache cannot resurrect gone generation and database bootstrap counter remains unchanged. |
| V-25 | `Failed_refresh_is_throttled_without_inventing_absence` | Fail real intercepted SELECT on cold/warm paths, query burst charges one attempt per interval. Cold failures propagate; warm successful knowledge is preserved; recovery after retry deadline refreshes once. |
| V-26 | `Disabled_runner_and_local_only_lists_do_not_query_pending_inventory` | Disabled/unaccepted/local controls and fresh directory recreation: no cross-runner/cross-process leak, correct cold load, returned snapshot cannot mutate future results. |

`PhoneHomeSchedulePreviewTests` — V-27 through V-30:

| ID | Method | Required evidence / red target |
|---|---|---|
| V-27 | `Before_first_List_preview_remains_queueable_without_writes` | Real preview service or endpoint DTO for WhenTargetDown=Skip: Target.Live true, WhenIdle effect, no down/SkippedNoSession warning, no starts/spend/row writes. Baseline is expected green; CP-2 mutates only preview's predicate and must fail this assertion. |
| V-28 | `Pending_preview_and_fire_agree_after_warm_cache_miss` | Newly committed active target after empty-cache warm-up: preview remains queueable and subsequent explicit test fire creates one Pending message. Preview itself changes no rows. Mutation: omit row-aware pending fallback. |
| V-29 | `Authoritative_absence_changes_preview_to_down` | Recover empty List after a queueable preview: now Target.Live false and Skip/Queue warnings match each policy, with no preview writes. |
| V-30 | `Local_missing_and_terminal_preview_controls_remain_unchanged` | Local live, local down, no persistent session and terminal DB row; assert existing policy/DTO effects and read-only behavior. |

R-1: run all six existing `AgentChannelServiceIntegrationTests`, adapting only their queue-aware harness and delivery assertions. R-2: all 16 `PhoneHomeStrandedQueueTests`, seven `PhoneHomeDirectoryTests` and five `SessionMessageQueuePhoneHomeDropTests`. R-3: nine `ScheduleEndpointsTests` and 18 `ScheduleSweepTests`. These are **61 source `[Test]` methods at the inspected base**; actual expanded TRX counts remain authoritative. R-4: ordinary Unit lane for enum/predicate/layer and other fast regressions. No broad Integration or full-assembly run is in this profile.

### Execution and evidence

Every git command, including commands used by evidence scripts, needs a deadline. Run all shell git operations as `timeout 30s git ...` (network operations `timeout 60s git ...`). `run-checkpoint.ps1` invokes git internally: give it a temporary PATH shim that forwards to the previously resolved absolute git binary through `timeout 30s`, or ensure that helper's git calls are already bounded. This is test execution scaffolding, not a repository change. Do not dump task tokens or provider homes.

Run TUnit through `pwsh -NoProfile -File scripts/run-checkpoint.ps1`, never `dotnet test`. Off Windows it already selects `UseAppHost=false`. Use the exact table filter, output path, `-MinExecuted`, and `-Expect` class names. Use a fresh results directory for every row/rerun and inspect its printed executed roster, failures and TRX counters. The existing helper exits 1 for expected assertion-red CP-1/CP-2: explicitly grade the named failures; exit 2/3, no TRX, zero tests, fixture/compile errors or an unexpected failure is not a valid red result. Do not weaken the helper's normal green criterion globally.

For CP-2 save the exact production ScheduleService bytes, apply only the preview-predicate mutation, run the row, and restore in a `finally` path. Verify the mutation is absent afterward; touch restored source so incremental outputs cannot preserve mutant code. Never commit the mutant. CP-3 builds a separate fresh output from the complete fixed source and includes the same preview test green. A S1 test needs a new production API only if it belongs to S2-S4's extended roster; the five baseline defect probes and V-27 must compile against unchanged baseline APIs.

Example final green row:

```sh
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-3 -Project tests/Antiphon.Tests -OutputPath bin-c696-green/ -Filter '/*/*/(PhoneHomeOutageMentionTests*)|(PhoneHomeImmediateSendTests*)|(PhoneHomePendingInventoryTests*)|(PhoneHomeSchedulePreviewTests*)/*' -MinExecuted 30 -Expect PhoneHomeOutageMentionTests,PhoneHomeImmediateSendTests,PhoneHomePendingInventoryTests,PhoneHomeSchedulePreviewTests -ResultsRoot .antiphon/c696-checkpoints
```

CP-4/CP-5 reuse CP-3 with `-NoBuild`. Report each row as `CHECKPOINT CP-n commit=<sha> build=<ok|reused|failed> filter=<filter> executed=N passed=N failed=N skipped=N trx=<path>`, with rerun counts and expected-red explanation where applicable. Report any unlisted build/test with its reason before running it. Poll long commands inside the turn and retain progress updates; do not end a report while a checkpoint is still running.

### Cost

Ordinary Code verification floor: **24 minutes** (7 + 5 + 8 + 3 + 1). Authoring, harness adaptation and review preparation estimate: 90-130 minutes; budget Code's `ExpectAbout` at roughly **140 minutes**, adjusting from actual server2 timings. No production CPU claim follows from this card's tests: the measured acceptance here is bounded query counts during a controlled outage. CARD-0698/CARD-0701 own the broader live Postgres acceptance.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c696-red/` | baseline-defects | `/*/*/(PhoneHomeOutageMentionTests*)|(PhoneHomeImmediateSendTests*)|(PhoneHomePendingInventoryTests*)/C696Red_*` | V-1, V-2, V-9, V-10, V-19 red proof | Exactly 5 executed and 5 intended assertion failures; 0 skipped/errors. If baseline moved and a probe passes, record the fixing SHA and demonstrate a scoped production regression before claiming red. | 5 | 7 |
| CP-2 | S1 + temporary D-4 preview mutation | `tests/Antiphon.Tests -> bin-c696-preview-red/` | preview-positive-control | `/*/*/PhoneHomeSchedulePreviewTests/Before_first_List_preview_remains_queueable_without_writes` | V-27 red proof | Exactly 1 executed, intended DTO assertion fails; 0 skipped/errors. Restore mutation immediately. | 1 | 5 |
| CP-3 | S2-S5 | `tests/Antiphon.Tests -> bin-c696-green/` | outage-contracts | `/*/*/(PhoneHomeOutageMentionTests*)|(PhoneHomeImmediateSendTests*)|(PhoneHomePendingInventoryTests*)|(PhoneHomeSchedulePreviewTests*)/*` | V-1 through V-30 green | All 30 listed methods execute; 0 failed/skipped. | 30 | 8 |
| CP-4 | S2-S5 | CP-3 | affected-regressions | `/*/*/(AgentChannelServiceIntegrationTests*)|(PhoneHomeStrandedQueueTests*)|(PhoneHomeDirectoryTests*)|(SessionMessageQueuePhoneHomeDropTests*)|(ScheduleEndpointsTests*)|(ScheduleSweepTests*)/*` | R-1, R-2, R-3 | All six named classes; at least 61 executed, 0 failed/skipped. | 61 | 3 |
| CP-5 | S2-S5 | CP-3 | unit | `/*/*/*/*[Category=Unit]` | R-4 | Nonzero Unit lane with full printed counts; 0 failed. Inspect unexpected skips. | 1 | 1 |
