# CARD-0650 continuation: TestDesign inventory

TestDesign task `d534fa92`, 2026-09-28. Source inspected at
`a8ccd73622a1ea94c7fe27c66ee7d71032df6139`. This is the verification supplement to the
[continuation plan](2026-09-28-card-0650-expectation-watchdog-continuation-plan.md),
not the September 24 implementation sequence. S1-S4 and D-1..D-6 remain authoritative.
No production code, tests or migrations are implemented here. No builds, tests or mutations
were executed; counts are inspected source rosters and required future executions.

## Live recovery boundary

`pwsh -NoProfile -File scripts/card.ps1 get CARD-0648 -Board Antiphon -Json` returned
Backlog, revisionCount 1, updatedAt `2026-09-24T15:06:03.50254Z`, card
`98a4e2fc-5422-4650-9ceb-1559681a78bc`, board `8988ca03-7414-47ad-b0b6-51556c701703`.
No card was changed. Its asks are boot/periodic dead-or-reused-PID recovery after proving
descendants are gone, restart integration, independent fence paging, and live-versus-dead
hold wording. Those are requested work, not implemented CARD-0648 capabilities.

The existing CARD-0726 `RepositoryChildJournalInspector.InspectCommonAsync` reads journal
files and asks `ILandingGit.IsProcessAliveAsync(pid, startTicks)`; `Dead` is only that
identity/liveness result. It does not prove descendants or locks clear and deletes nothing.
`InspectAsync` first resolves the common directory through git, so the watchdog must use
`InspectCommonAsync` with an already resolved directory. Missing directory information is
unknown journal health, not an open dispatch lane. D-3 is consistent with these facts:
observe, retain a safe read-only inspection hint, consume a future recovery result only as
evidence to re-observe, and never invoke recovery or clear a fence from PID death. CARD-0648
remains Backlog/out of scope. Existing CARD-0726 alarms remain a separate owner; this plan
does not create another recovery loop.

## Fixture inspection and required Code changes

All paths in this section are repository-relative. The existing test bodies below were
inspected, including their positive branches; a name or comment alone is not evidence.

| Fixture / source | Observed behavior and required use |
|---|---|
| `tests/Antiphon.Tests/Application/ExpectationTestWorld.cs` | Creates a cloned PostgreSQL database via `TestDbFixture`, board, audit card, standing ownership and two target lanes. Its fixed `Now` is September 24; OperatorChannelId is a random ID with **no ChatChannel row**, and the base agent has no current-session pointer. Make channel/current pointer explicit for production-reference tests; retain missing-reference cases intentionally. `localTarget=0` still produces target 1. Seed rated ready cards explicitly rather than inferring capacity from fixture arguments. |
| `ExpectationSnapshotTests.cs` | Has its own nested World, separate from ExpectationTestWorld; changing one will not fix the other. Six methods cover board/project/unbound caller scope, lane populations, backlog eligibility, structured hold classification, archive, and persisted land/cap evidence. Extend foreign-caller board-bound and unknown-runner cases with assertions on returned IDs, not only totals. |
| `ExpectationDirectiveTests.cs`, `ExpectationPipelineTests.cs` | Four and nine Unit methods respectively. Existing exact boundaries use fixed ten-minute windows. Add runtime custom-clock boundaries to the proposed configurable-window method; testing options constants alone cannot kill a hard-coded detector. Keep live land/full live cap controls beside stale/wrong-owner cases. |
| `ExpectationDebtTests.cs` | Six PostgreSQL methods. `C650_Note_age_includes_parked_and_retry_debt` intentionally includes **Canceled outbox** debt with a canceled queue row; do not confuse this with excluded canceled queue-only obligations. `C650_Receipt_catchup_and_concurrent_settlement_withhold_stale_nudge` inserts the receipt and settles the task during catch-up, then asserts zero committed nudge/audit. Preserve this fresh-read race. |
| `ExpectationSnapshotReader.ReadNotesAsync/IsReceivedAsync`, `LandNoteReceipt`, `SessionQueuedMessage` | Current read is first 100 land-outbox rows, no queue-only scan. Link by SourceLandNotificationId/QueueMessageId (including completion member queue IDs), never text equality. Held/Aged/Conflict/Outcome may accept QueuedUserPrompt per CARD-0641; profiled TaskCompletion requires its frozen wire rendering, member identity and valid spill hash. Queue-only Delegation completion/question provenance includes SourceTaskId and NoteHeader; Check publication uses SourceTaskId and `AgentTaskCheckService.ConversationKey`. Origin alone also matches briefs. Seed real producer shapes, owned older destinations, attempted floors and cancellation/supersession, then compare all watched rows before/after. |
| `ExpectationLedgerTests.cs`, `ExpectationEpisodeTests.cs` | Four and five PostgreSQL methods. Ledger rollback uses a missing audit-card FK; concurrency uses two DbContexts. Episode concurrency rendezvous is inside catch-up; it proves one committed aggregate, **not** a terminal-send or broker claim. `C650_Cooldown_recurrence_and_config_change_keep_correct_clocks` currently expects an unanswered repeat at t+40. In S2 update this branch to a correlated receipt/ACK and repeat from AnsweredAt; retain its urgent-bypass/config/reset assertions and independently assert no unanswered typing in SchedulingTests. CP-4 reselects all five episode methods after this change. |
| `ExpectationDeliveryFixture.cs`, `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs` | Real SessionMessageQueueService + AgentSessionRuntime + recording fake terminal + real DB. Each DeliverAsync creates a new DbContext/service, but hardcodes TimeProvider.System; nudge/transcript helpers also use wall time. Thread one supplied clock through fixture, world, service and timestamp helpers. HarnessOptions already supports TimeProvider, ConfigureServices and ConfigureDbContext; use the last for EF fault interception. NudgeAsync currently fabricates BodyDigest as 64 `a` characters and manually inserts audit/nudge; production-chain tests must create these through the real ledger/formatter, while seeded crash rows need valid digests/config identity/deadlines unless testing malformed legacy data. A stopped FakeTimeProvider stalls queue delays: advance registered timers at explicit barriers and await completion; never advance five hours during an in-flight send. Existing compressed real-time delivery tests may retain their clock. |
| Fake terminal submission | Attached harness OnSubmitted writes UserPrompt and TurnEnd even after MarkWorkingAsync. Busy coverage therefore needs **both** that delivered path and an override writing QueuedUserPrompt only, then a later complete UserPrompt/assistant turn. Read persisted text, sequence and generation evidence. Do not assert only SubmittedBodies or a sender invocation. |
| `ExpectationReceiptTests.cs` | Six methods; rename/update `C650_Complete_submitted_prompt_confirms_both_supported_kinds` to `C650_UserPrompt_confirms_but_queued_prompt_only_submits`. The new queued branch must leave ReceiptAt/ReceiptSequence/AnsweredAt null; a later UserPrompt confirms without any second body write. Retain no-floor, wrong-session, partial, housekeeping, restart/no-retype and frozen-old-session branches. |
| `ExpectationDirectDeliveryTests.cs` | Fourteen methods. Busy test blocks WhenIdle and checks untouched queue, but has **no lease fixture** despite its name. Add a recording/refusing repository-mutation-lease seam in the composed scenario and verify zero acquisition calls. Add attempt/audit-before-first-byte inspection at the runtime write boundary, not OnSubmitted (which is after Enter). Extend existing refusal matrix with closed rules, forbidden body, backend unavailable, Herdr blocked and over-ceiling input. Keep every composer repair: truncated/submitted echo, unreadable/tail-only Claude, ghost-empty then occupied, two settled empty frames, generation change and Released behavior. |
| `Api/ExpectationHoldReleaseEndpointTests.cs` | Four HTTP methods over production route + CurrentUserMiddleware/ExceptionMiddleware + real queue. Uses its own temporary operator token, never logged. Race blocks OnSubmitted while release waits; outcome remains inside session lock. For the PC which moves outcome outside the lock, use a deterministic outcome-write barrier so red is an assertion, not an intermittent scheduling race. Do not widen the existing 200ms probe. |
| `SessionMessageQueueServiceTests.cs` | Four selected public methods use the real queue and assert immediate Now, two writes, bracketed multiline paste and held WhenIdle. Their local harness uses shared test storage; retain MessageQueue serialization. They prove unchanged public input behavior, not watchdog receipt-kind policy or native TUI behavior. |
| Operator / HTTP fixtures to add | Seed an enabled ChatChannel with deliberately different `Provider` and `ExternalId` from a decoy channel. `ChannelReply.Channel` receives Provider and ConversationId receives ExternalId; ReplyHandle must not redirect it. Existing `FakeAntiphonMessagingClient.SendAsync` always succeeds: add a recording/failing/cancelable producer with before-accept, after-accept-before-stamp and claim barriers. Assert audits using a **separate context inside SendAsync**. No broker, channel agent or digest job. HTTP status tests must install real project/task-token authorization, unlike the release fixture's operator-only authorization. |
| `Infrastructure/HangfireStartupSafetyTests.cs`, `AntiphonWebAppFactory`, `ProductionRunnerGuard` | Twelve executions, including dashboard authorization. Shared Program factory disables Hangfire and replaces runner/census; it cannot prove an enabled dedicated worker. Keep that factory unchanged. Extract/reuse the production watchdog registration seam in a small isolated host with independent InMemoryStorage and explicit fake I/O. Gate truth table covers host/feature on/off; block the actual default worker and observe expectations worker completion. Verify DI registration through the same production composition, not a hand-built service list. Never clear assembly safety environment variables or enable Program's production worker. |
| `DispatchHeldAttentionTests.cs` | Ten methods, eleven executions (land-yield method has two Arguments). Uses real stored events, FakeTimeProvider and fake runner. It protects existing attention semantics, not the new nudge scheduler. CP-3 PCs therefore mutate the corresponding AttentionService/hold-ledger projection; pretending a watchdog-policy mutation kills this class would be false. |

New adapters/matcher/operator/job/status/scenario test classes named by V-1..V-5 do not yet
exist. Their required data and barriers below are authoring obligations, not inspected passing
tests. Every new class needs the repository's Unit xor Integration classification. PostgreSQL,
DI/HTTP, queue and scheduler classes belong to Integration; no native process or paid-agent
fixture is needed. None of these tests proves real gateway arrival or native terminal behavior.

## Guard-to-positive-control contract

Each PC is one independently applied production mutation and one exact method selection.
The assertion named after the arrow must fail with baseline green and restored green.
New implementation targets are specified by owning service and behavioral branch; Code must
keep that branch exercised by the method, and Mutation records the final file/line and compiling
patch against the landed SHA. This is **designed**, not demonstrated, mutation sensitivity.
Do not mutate assertions, fixture setup or a shared matcher merely to manufacture red.

Method notation `Alias.Suffix` means the exact `Class.C650_Suffix`, except HA/HF/Q which
use the method name verbatim. Execute `/*/*/Class/Method*` and assert that exact class/method
in fresh TRX. Only HA's land-yield method expands to two results; all others below execute one.
An internal scenario matrix is one execution. Each row's contrasting valid case is mandatory;
an always-refuse/always-unknown implementation must fail too.

| Alias | Class (under tests/Antiphon.Tests) |
|---|---|
| D / P / S | Application.ExpectationDirectiveTests / ExpectationPipelineTests / ExpectationSnapshotTests |
| O / N | Application.ExpectationObservationAdapterTests / ExpectationNoteDebtTests (new) |
| L / E / B | Application.ExpectationLedgerTests / ExpectationEpisodeTests / ExpectationDebtTests |
| R / A / T | Application.ExpectationReceiptTests / ExpectationResponseTests (new) / ExpectationSchedulingTests (new) |
| I / H / Q | Application.ExpectationDirectDeliveryTests / Api.ExpectationHoldReleaseEndpointTests / Application.SessionMessageQueueServiceTests |
| X / U | Application.ExpectationEscalationTests (new) / Api.ExpectationStatusTests (new) |
| J / Z / HF | Infrastructure.ExpectationWatchdogJobTests (new) / Application.ExpectationWatchdogScenarioTests (new) / Infrastructure.HangfireStartupSafetyTests |
| HA | Application.DispatchHeldAttentionTests |

### CP-1: observations and detection

| PC | Exact method | Production mutation -> discriminating fixture/assertion |
|---|---|---|
| PC-001 | D.Rejects_invalid_and_duplicate_directives | Validator omits duplicate enabled-board rejection -> distinct directive IDs on one board must fail validation; disabled twin stays valid. |
| PC-002 | D.Configurable_windows_validate_and_change_digest | Validator accepts 0 or 1441 for a window -> matrix independently varies each of seven options, accepts 1/1440 and rejects out-of-range; Repeat below Cooldown rejected. |
| PC-003 | D.Configurable_windows_validate_and_change_digest | Digest omits window settings -> changing each window independently must change digest; reordered candidates/trimmed IDs must not. |
| PC-004 | D.Configurable_windows_validate_and_change_digest | Digest omits destination identity -> edit only OperatorChannelId/resolved address semantics and require a different normalized digest; unchanged normalized destination is the control. SchedulingTests covers persisted retirement. |
| PC-005 | D.Validates_scope_and_recipient_references | Reference evaluator skips audit-card board comparison -> existing foreign-card case loses audit_card_wrong_board. |
| PC-006 | D.Validates_scope_and_recipient_references | Reference evaluator admits pool agent -> pool fault disappears despite a matching board. |
| PC-007 | D.Validates_scope_and_recipient_references | Reference evaluator skips agent board comparison -> foreign standing-agent fault disappears. |
| PC-008 | O.Composition_reads_actual_catalog_and_policy | Observation adapter substitutes an empty/default catalog -> a dynamically named runner and persisted agent/card/channel no longer produce expected lane/reference facts. Include missing and disabled references with a valid control. |
| PC-009 | O.Composition_reads_actual_catalog_and_policy | Admission observation treats a stale quota sample as Refused -> stale/missing sample must be Unknown while fresh low quota is Refused and fresh permitted quota is Open; record no Enforce/dispatch calls. |
| PC-010 | S.Scope_excludes_other_boards_and_ambiguous_callers | Snapshot scope ignores board match -> foreign-board task appears; own-board task from a different caller remains included. |
| PC-011 | S.Scope_excludes_other_boards_and_ambiguous_callers | Snapshot scope drops project check for unbound work -> wrong-project task with proven standing caller incorrectly appears. Add that row beside the existing wrong-project card-bound row. |
| PC-012 | S.Scope_excludes_other_boards_and_ambiguous_callers | Scope accepts ambiguous/unowned unbound caller -> excluded task IDs/diagnostic count change; proven owned older caller is included. |
| PC-013 | S.Counts_runner_lanes_and_excludes_specialists | Lane builder groups all work as local -> remote Working/local Dispatched IDs and counts differ. |
| PC-014 | S.Counts_runner_lanes_and_excludes_specialists | Running population includes Queued/Blocked/Check -> expected running IDs no longer exactly match the two productive tasks. |
| PC-015 | S.Backlog_candidates_exclude_owned_held_unrated_and_open_work | Backlog query admits cards with open work -> open-work ID enters the exact three-card set; closed-work card remains eligible. Keep separate owned/held/unrated/archive matrix branches. |
| PC-016 | S.Archived_board_is_inactive_for_detection | Snapshot ignores board ArchivedAt -> archived board creates a condition/nudge; same board before archive must be active. |
| PC-017 | P.Queue_age_and_dispatch_progress_use_exact_boundaries | Queue detector changes due >= threshold to > -> exactly due queue fails to produce one stalled condition; threshold-minus-one remains quiet. Extend to a non-default QueuedMinutes. |
| PC-018 | P.Queue_age_and_dispatch_progress_use_exact_boundaries | Progress lookup accepts foreign dispatch -> foreign dispatch alone wrongly suppresses due own queue; own recent dispatch suppresses it. Add Check/ACK/audit-only activity as non-dispatch controls. |
| PC-019 | P.Requeue_resets_age_but_Held_does_not | Queue-stint calculation treats HeldAged as restart -> original-aged task goes quiet; Retried/Rerouted still restart at their exact events. |
| PC-020 | P.All_paths_fenced_is_immediate | Fence policy applies QueuedMinutes delay -> newly fenced local and remote paths fail to nudge on first successful scan. |
| PC-021 | P.Partial_or_unknown_path_is_not_global_fence | Global fence quantifier changes all to any -> one open/unknown path incorrectly permits global fence; all-known fenced control succeeds. |
| PC-022 | O.Prepared_remote_path_defeats_repository_wide_fence | Adapter applies desktop lease fence to a prepared remote task -> global fence appears despite dispatchable mirrored remote work. Unprepared remote work remains subject to preparation fencing. |
| PC-023 | P.Quota_fences_new_admission_only | Policy includes already-admitted queued tasks among quota-fenced subjects -> task IDs appear in admission-only evidence; fresh quota refusal of all ready-backlog candidates still reports admission scope. |
| PC-024 | P.Capacity_deficit_requires_continuous_ready_work | Capacity clock survives target-met/backlog-empty observation -> next deficit pages before a full fresh window; run those two resets separately, then exact/minus-one custom CapacityMinutes. |
| PC-025 | P.Stale_land_or_cap_hold_beside_unrelated_running_work_opens_the_episode | Ordinary-wait suppression accepts any running work -> stale/wrong-owner land and ended/partial cap rows fail to produce a stalled episode; exact named moving land/full progressing cap remain quiet. |
| PC-026 | O.Unknown_runner_preserves_other_due_conditions | Adapter/service returns globally on one unknown runner -> independently aged note fails to commit while unknown runner's existing episode remains unresolved. No zero count substituted for unknown inventory. |
| PC-027 | O.Live_dead_and_unknown_journals_do_not_grant_recovery | Adapter marks dispatch unfenced when journal State=Dead -> due dead-journal fence disappears even though actual fence remains; Alive ordinary-wait and ambiguous health controls retain distinct diagnostics. |
| PC-028 | O.Live_dead_and_unknown_journals_do_not_grant_recovery | Adapter calls InspectAsync instead of InspectCommonAsync -> recording ILandingGit.CommonDirectoryAsync call count becomes nonzero. Supply a resolved scratch directory; missing-directory case must retain fence with unknown health. |
| PC-029 | O.Catchup_precedes_absence_judgment | Adapter omits actual runtime transcript catch-up -> receipt/activity injected only during catch-up fails to suppress stale absence; prove runner/session generation binding, no launch/recovery invocation. |
| PC-030 | O.Paged_observation_eventually_visits_all_subjects | Note cursor resets to first page each pass -> among 205 obligations, IDs 101..205 never receive observation/audit. Leave first-page debts open and reload between passes; incomplete page cannot clear unseen open episodes. |

### CP-2: note debt, silence and durable episodes

| PC | Exact method | Production mutation -> discriminating fixture/assertion |
|---|---|---|
| PC-031 | N.Queue_only_completion_and_check_notes_age | Reader removes queue-only Delegation branch -> aged legacy completion and blocked-question obligations lack subjects/audit; rows use actual SourceTaskId/NoteHeader shapes. |
| PC-032 | N.Queue_only_completion_and_check_notes_age | Reader removes queue-only Check branch -> persisted caller Check is absent despite age at custom NoteMinutes; young counterpart remains quiet. |
| PC-033 | N.Queue_only_completion_and_check_notes_age | Reader considers only Pending -> interrupted Sent with null/unconfirmed verdict disappears; parked-at-attempt-cap and held pending still count. |
| PC-034 | N.Linked_outbox_and_queue_form_one_obligation | Reader fails to deduplicate SourceLandNotificationId/member IDs -> linked outbox and queue produce two subjects instead of one notification-keyed obligation; unrelated identical-body queue IDs remain distinct. |
| PC-035 | N.Confirmed_superseded_and_noncaller_rows_are_excluded | Queue-only provenance accepts all Delegation-origin messages -> delegate execution brief is reported as caller debt. Include Ui/Channel/System/supervision decoys and valid caller completion/Check. |
| PC-036 | N.Confirmed_superseded_and_noncaller_rows_are_excluded | Queue-only scope uses current pointer only -> aged note to an older proven owned session disappears; foreign destination stays excluded. |
| PC-037 | N.Confirmed_superseded_and_noncaller_rows_are_excluded | Queue-only query ignores explicit cancellation/supersession -> canceled/superseded obligations reappear; a canceled **outbox** obligation retains the existing CARD-0641 debt semantics. |
| PC-038 | N.Catchup_uses_owning_receipt_and_preserves_original_age | Age uses LastDeliveryStartedAt/NextAttemptAt -> old retried obligation misses its due deadline; CreatedAt stays authoritative through reload. |
| PC-039 | N.Catchup_uses_owning_receipt_and_preserves_original_age | All note kinds accept QueuedUserPrompt -> profiled completion falsely clears, while supported nonlegacy Held queued receipt legitimately clears. Separate queue-only UserPrompt control. |
| PC-040 | N.Catchup_uses_owning_receipt_and_preserves_original_age | Profiled completion receipt skips frozen rendering/member/spill validation -> correct body with wrong member or changed scratch spill is wrongly clear; valid frozen rendering/hash is clear. No file outside scratch. |
| PC-041 | B.Quoted_note_is_not_receipt_only_a_complete_delivery_above_the_floor_is | Reader accepts full quoted text without keyed typed-attempt evidence -> untyped quote clears; typed complete prompt above floor still clears. |
| PC-042 | B.Quoted_note_is_not_receipt_only_a_complete_delivery_above_the_floor_is | Reader makes sequence comparison >= -> full row exactly at delivery floor clears wrongly; floor+1 valid control. |
| PC-043 | B.Dispatched_without_session_and_report_is_detected | Silent detector ignores LastActivityAt -> recent task-local activity is paged; last activity + custom MissingSessionMinutes-minus-one/exact cases distinguish timing. |
| PC-044 | B.Dispatched_without_session_and_report_is_detected | Snapshot drops report exclusion -> reported task becomes silent. Extend with intentionally Blocked task and recent watchdog Check-only activity; blocked stays excluded, Check must not reset another due task. |
| PC-045 | B.Unknown_runner_is_not_missing_session | SessionState maps null inventory/unreachable to Missing -> new false episode/nudge appears; authoritative missing control opens and authoritative live eventually resolves. |
| PC-046 | B.Fresh_stall_policy_verdict_is_rolled_up | Watchdog discards TaskProgressPolicy verdict -> looping live task no longer appears; recent workspace edit and disabled stall policy controls stay quiet. |
| PC-047 | B.Receipt_catchup_and_concurrent_settlement_withhold_stale_nudge | NudgeAsync commits its pre-catch-up snapshot -> receipt/settlement racing in catch-up still produces a nudge/check/comment; all must remain absent. |
| PC-048 | L.Audit_failure_rolls_back_nudge | Ledger saves episode/nudge outside the audit transaction -> injected audit persistence failure leaves a row in a fresh context. Add a command/save interceptor boundary if FK insertion order alone cannot expose this split; send count remains zero. |
| PC-049 | L.Persists_episode_and_nudge_with_audit | Ledger omits task Check creation -> nudge/comment exist but correlated task event IDs are missing. Include taskless capacity control with audit-card fallback. |
| PC-050 | L.Reload_keeps_clocks_and_attempt_identity | Ledger recomputes ordinal/body/floor on reload -> freshly loaded immutable identity differs; assert byte digest, destination/generation and clocks. |
| PC-051 | E.Concurrent_sweeps_commit_one_audit_and_send_claim | Aggregate commit skips the locked cooldown recheck -> rendezvoused separate-context sweeps create two nudges/comments or distinct ordinals. Do not count a unique-index exception as the intended red. |
| PC-052 | E.Repeated_sweeps_and_reason_flips_emit_one_nudge | Episode identity incorporates changing reason/elapsed text -> missing-to-terminal flip creates a second episode/nudge; a real new dispatch stint remains a new subject. |
| PC-053 | E.Repeated_sweeps_and_reason_flips_emit_one_nudge | Resolution accepts first clear observation -> ResolvedAt is set too early; add second clear at +59s (still open) then +60s (resolved). |
| PC-054 | E.Unknown_scan_preserves_episode_and_fair_cursor | Unknown scan updates last-success/clear streak -> two failed observations resolve a settled subject or advance LastSuccessfulScanAt; first real clear remains insufficient. |
| PC-055 | E.Fence_batches_queue_and_capacity_subjects | Audit uses the formatter's three-example subset -> fourth affected task loses its Check/audit mention while prompt stays bounded. |

### CP-3: existing dispatch-hold evidence

These are regression PCs in `AttentionService`/`DispatchHoldDetails`, not a claim that this
checkpoint executes ExpectationWatchdogService. They protect the evidence consumed by S1.

| PC | Exact method | Production mutation -> discriminating fixture/assertion |
|---|---|---|
| PC-056 | HA.a_queued_hold_past_warning_is_a_dispatch_held_warning_row | Attention projection omits aged Queued/Held candidates -> expected Warning row, task/card IDs and held age disappear. |
| PC-057 | HA.dispatched_task_is_absent | Attention projection drops Queued status predicate -> already Dispatched task retains a false held alert. |
| PC-058 | HA.requeue_stint_uses_the_new_held | Hold age takes oldest pre-dispatch Held -> newly queued 100s stint incorrectly alerts; 400s fresh stint still alerts with new SinceUtc. |
| PC-059 | HA.C672_dispatch_held_item_names_the_dominant_hold_class | Hold ledger uses newest class instead of accumulated duration -> expected lease class/200s and runner/150s become wrong. |
| PC-060 | HA.C672_land_yield_hold_is_not_an_attention_item_before_the_warning_age | LandHeld projection ignores warning age for lease-yield -> 20s argument incorrectly alerts; 301s argument still does. MinExecuted=2 for this PC. |
| PC-061 | HA.two_reads_share_the_condition_key | Projection includes read time in ConditionKey -> two reads lose stable task identity. Advance fixture clock between reads so the mutant is deterministically killed. |

### CP-4: attempt, receipt, answer and scheduling

| PC | Exact method | Production mutation -> discriminating fixture/assertion |
|---|---|---|
| PC-062 | R.UserPrompt_confirms_but_queued_prompt_only_submits | Expectation-only ResultOf/receipt reconciliation accepts QueuedUserPrompt as Confirmed -> queued branch incorrectly gets ReceiptAt/ReceiptSequence; later complete UserPrompt confirms with no retype. Do not mutate LandNoteReceipt. |
| PC-063 | R.Rejects_wrong_session_floor_partial_and_housekeeping_receipts | Nudge receipt search removes destination predicate -> complete foreign-session row confirms. Valid local complete row remains the control. |
| PC-064 | R.Rejects_wrong_session_floor_partial_and_housekeeping_receipts | Nudge receipt comparison changes > floor to >= -> at-floor body confirms; above-floor body confirms only in control. |
| PC-065 | R.Rejects_wrong_session_floor_partial_and_housekeeping_receipts | Receipt matcher omits completeness check -> head-only receipt confirms; QueueEnqueue/Dequeue and assistant body remain negative. |
| PC-066 | R.No_observable_baseline_never_invents_confirmation | Missing floor is replaced by zero -> no-baseline prompt invents ReceiptAt on initial send/reload. Submitted without receipt remains allowed. |
| PC-067 | R.Crash_after_attempt_commit_never_retypes | Attempting is reset to None after restart -> second pass writes body again instead of reconciling and creating immediate durable debt. |
| PC-068 | R.New_session_does_not_adopt_old_attempt | Reconcile resolves the current agent pointer instead of frozen session -> replacement echo confirms old nudge or receives a replay; fresh nudge still reaches replacement. |
| PC-069 | A.Only_complete_UserPrompt_opens_ack_window | Response matcher allows ACK before complete delivery -> partial prompt plus otherwise valid assistant ACK wrongly sets AnsweredAt; append full UserPrompt and later valid answer as control. |
| PC-070 | A.Assistant_ack_needs_matching_id_turn_and_action | ACK parser matches marker substring -> quoted/inline/fenced-code marker wrongly answers; only whole unquoted marker line with action/reason succeeds. |
| PC-071 | A.Assistant_ack_needs_matching_id_turn_and_action | ACK parser compares short GUID/prefix -> another full GUID sharing first eight digits answers wrong nudge. |
| PC-072 | A.Assistant_ack_needs_matching_id_turn_and_action | ACK parser drops action/reason requirement -> marker-only assistant message answers. Valid marker plus action in same answer succeeds. |
| PC-073 | A.Assistant_ack_needs_matching_id_turn_and_action | Response matcher accepts non-assistant entries -> user/tool/thinking text answers; same text as non-error AssistantText succeeds. PC-172 separately bypasses error exclusion. |
| PC-074 | A.Wrong_session_generation_and_sequence_do_not_answer | Response matcher drops accepted-generation check -> same session ID restarted at generation B accepts ACK for frozen A; same-generation control succeeds. |
| PC-075 | A.Wrong_session_generation_and_sequence_do_not_answer | Response matcher drops frozen destination check -> ACK in a different owned session answers the original. |
| PC-076 | A.Wrong_session_generation_and_sequence_do_not_answer | Matcher removes after-receipt sequence/turn correlation -> older ACK or ACK in unrelated later turn answers; genuine answer to delivered prompt succeeds. |
| PC-077 | A.Queued_submission_is_not_delivery_or_answer | Response service treats Submitted or Released as answered -> persisted queued-only/Released variants suppress due debt despite null ReceiptAt/AnsweredAt. Prove ordinary input can proceed independently. |
| PC-078 | A.Deadline_survives_restart_and_catchup_failure | Deadline is set when receipt arrives instead of attempt commit -> blocked-after-commit send and late receipt extend AnswerDueAt. Inspect timestamp before first byte; legacy attempt without timestamp conservatively uses CreatedAt. |
| PC-079 | A.Deadline_survives_restart_and_catchup_failure | Deadline is recomputed on catch-up failure/reload -> repeated failures extend expiry; an unattempted blocked execution is due no later than CreatedAt+AnswerMinutes. Use custom AnswerMinutes and fresh contexts. |
| PC-080 | A.Receipt_without_ack_still_becomes_due | Due predicate requires ReceiptAt null -> delivered but unanswered prompt never reaches Due at exact deadline; N-1 remains not due. |
| PC-081 | A.Late_ack_suppresses_unpublished_debt | Valid ACK transition leaves Due page eligible -> pre-publication ACK fails to suppress outbox with answer evidence and zero broker calls. |
| PC-082 | A.Ack_does_not_resolve_or_retype_uncertain_prompt | ACK handler resolves linked episodes -> valid answer with actual fence still present incorrectly sets ResolvedAt; reconcile an uncertain prompt by receipt/ACK without another write. |
| PC-083 | T.Unanswered_episode_pages_without_repeat_typing | Scheduling uses last nudge time without requiring AnsweredAt -> unresolved unanswered episode types again at RepeatMinutes; only operator reminders are eligible. |
| PC-084 | T.Acknowledged_unresolved_episode_repeats_after_answer_window | Repeat clock uses CreatedAt instead of AnsweredAt -> delayed ACK causes early second nudge; test answer+Repeat-1/exact and retain open condition. |
| PC-085 | T.Config_disable_change_and_expiry_cancel_stale_unsent_work | Eligibility ignores directive Enabled -> deactivated directive sends committed-but-unattempted prompt/page. History stays readable. |
| PC-086 | T.Config_disable_change_and_expiry_cancel_stale_unsent_work | Eligibility ignores expiry -> exact expiry still sends; just-before-expiry valid control. |
| PC-087 | T.Config_disable_change_and_expiry_cancel_stale_unsent_work | Eligibility ignores immutable config digest -> edited timing/destination allows old unsent nudge/page; barrier between claim and publication proves final recheck, not just scan-time check. |
| PC-088 | E.Cooldown_recurrence_and_config_change_keep_correct_clocks | Nudge scheduling removes LastCooldownBypassAt guard -> a second new urgent fence at t+15 bypasses again. Keep the t+13 first bypass and t+23 ordinary eligibility; update repeat branch to real ACK as specified above. |
| PC-089 | T.Prompt_byte_ceiling_keeps_identity_and_audit | Formatter counts UTF-16 characters instead of normalized UTF-8 bytes -> emoji/CJK evidence yields wire body over resolved ceiling; threshold-exact ASCII control still sends. Include paste framing where the runner ceiling applies to framed bytes. |
| PC-090 | T.Prompt_byte_ceiling_keeps_identity_and_audit | Formatter truncates final body after appending marker -> small ceiling drops full nudge GUID/ACK/status pointer or splits a rune. Minimum marked body too large must persist refusal/debt with zero input and no spill/reformatted second attempt. |

CP-4 also reruns all five E methods: the inherited repeat expectation is changed in S2 and
must be exercised after that change. This adds five executions, not five new methods.

### CP-5: direct input and composer repairs

| PC | Exact method | Production mutation -> discriminating fixture/assertion |
|---|---|---|
| PC-091 | I.Working_caller_receives_prompt_without_note_or_lease_progress | Direct sender routes via Enqueue(WhenIdle) -> Working caller gets no complete UserPrompt while held note remains pending. Assert actual transcript and unchanged note attempts. |
| PC-092 | I.Input_uses_normalized_paste_and_separate_enter | Expectation path omits CRLF-to-LF normalization -> recorded paste bytes contain CRLF; two writes and exact normalized persisted prompt are required. |
| PC-093 | Q.Delivery_sends_body_then_a_separate_CR_not_one_combined_write | DeliverAsync combines body and CR in one runtime write -> Inputs.Count/second-write assertion fails. |
| PC-094 | Q.Multiline_delivery_is_wrapped_in_bracketed_paste | DeliverAsync omits paste delimiters -> exact first write differs; Enter remains separate. |
| PC-095 | Q.When_idle_message_is_held_while_the_agent_is_working | Ordinary queue bypasses IsWorking -> ordinary note types and queue disappears; watchdog bypass must remain narrow. |
| PC-096 | Q.Send_now_delivers_immediately_and_does_not_queue | Public Now branch queues instead of direct send -> no immediate body/CR and nonempty queue. |
| PC-097 | I.Unsafe_composer_or_modal_sends_no_bytes | Sender drops held-back Pending-body guard -> parked same-generation body is overwritten. Existing clear-composer/unblocked control must still deliver. |
| PC-098 | I.Unsafe_composer_or_modal_sends_no_bytes | Sender drops unreconciled Sent guard -> interrupted Sent attempt allows new bytes. |
| PC-099 | I.Unsafe_composer_or_modal_sends_no_bytes | Sender drops current-generation modal guard -> unresolved RemoteControlModalEpisode receives bytes; resolved modal control succeeds. |
| PC-100 | I.Unsafe_composer_or_modal_sends_no_bytes | Sender skips rules-closed check -> closed Grok rules case commits/sends. Add open-rules control; inspect zero attempt commit and zero bytes. |
| PC-101 | I.Unsafe_composer_or_modal_sends_no_bytes | Sender skips byte-ceiling refusal -> over-ceiling direct request writes/commits. Also exercise forbidden control body and Herdr blocked/unreachable refusal paths with valid controls; formatter is not this last guard. |
| PC-102 | I.Delivery_failure_never_stops_or_restarts_session | Expectation send enables overlayRecovery -> swallowed/no-evidence branch escapes/retypes or reaches generic recovery. Assert no Escape, no duplicate body, no kill/start/generation-kill/incident, preserving Enter-only retries. |
| PC-103 | I.Destination_change_prevents_typing | Sender skips standing owner comparison -> foreign owner can commit/write. Keep matching owner/generation control. |
| PC-104 | I.Destination_change_prevents_typing | Sender skips current-session pointer comparison -> resolved old session receives body after pointer moves. |
| PC-105 | I.Destination_change_prevents_typing | Sender skips accepted generation comparison -> stale generation writes after same-ID restart. |
| PC-106 | I.Working_caller_receives_prompt_without_note_or_lease_progress | Sender moves commitAttempt after first runtime write -> first-byte observer sees no committed Attempting/floor/deadline/audit in a separate DbContext. Fault attempt/audit persistence there and require zero bytes. |
| PC-107 | I.Uncertain_watchdog_body_blocks_later_ordinary_input | Ordinary Now path skips EnsureNoUnconfirmedExpectationBodyAsync -> Now accepts/writes over stranded body instead of 409. Keep assertions for WhenIdle, SendNow, poll and second nudge, and clear-composer control. |
| PC-108 | I.No_baseline_screen_only_submit_holds_until_the_transcript_shows_it | Sender marks screen-only verdict Submitted -> screen redraw releases hold without a prompt row. Later real transcript releases input but does not invent no-floor receipt. |
| PC-109 | I.Submitted_prompt_left_on_screen_does_not_hold_ordinary_input | Composer query includes Submitted attempts as holding -> recorded/truncated submitted echo blocks otherwise safe ordinary input; ReceiptAt remains unconfirmed where appropriate. |
| PC-110 | I.Terminal_overlay_refuses_before_any_byte | Nonrecovering DeliverAsync skips ShowsTerminalOverlay refusal -> Grok usage overlay receives body/Enter; closed overlay succeeds. |
| PC-111 | I.Submitted_echo_above_a_readable_composer_does_not_hold | Claude hold uses whole screen instead of composer region -> conversation echo incorrectly blocks input despite two empty composer frames. |
| PC-112 | I.Swallowed_enter_keeps_holding_while_the_composer_shows_the_body | Hold tests full head visibility instead of nonempty composer -> tail-only composer wrongly releases; empty settled composer still releases. |
| PC-113 | I.Unreadable_Claude_composer_keeps_holding_until_it_is_proved_empty | Unreadable Claude composer returns false -> scroll/dialog/narrow/tail frames permit new bytes. |
| PC-114 | I.Ghost_empty_composer_on_one_snapshot_keeps_holding_while_the_next_shows_the_body | ComposerStaysEmptyAsync returns true after first snapshot -> ghost-empty then occupied pair releases; empty/empty control releases only after settle. |
| PC-115 | I.Operator_release_clears_a_whole_screen_hold_with_an_audit_and_no_input | Release clears OperatorOutboxState -> Released attempt loses due page; assert no ReceiptAt/AnsweredAt, one actor/reason comment and Check, zero release bytes, idempotent second call. |
| PC-116 | H.Release_without_the_operator_token_is_refused_and_releases_nothing | HTTP release trusts loopback/proxy instead of token -> missing/wrong/empty credential releases hold; authorized token succeeds without appearing in audit. |
| PC-117 | H.Release_of_another_session_releases_nothing | Release query omits destination session predicate -> request for other session releases original; add old-generation variant and retain original generation's hold. |
| PC-118 | H.Repeated_release_is_idempotent | Release updates/audits without conditional state ownership -> two concurrent authorized calls create duplicate release evidence. |
| PC-119 | H.Release_racing_a_watchdog_send_never_releases_the_send_in_flight | Sender records outcome after releasing session lock -> deterministic barrier exposes Attempting to release, incorrectly returning its nudge ID/overwriting Confirmed. Guard remains inside lock through outcome persistence. |

### CP-6: durable operator publication and status

Every producer barrier is cancelable and awaited in teardown. Restart means dispose/recreate
service scopes over the same persisted rows, not reseeding an equivalent in-memory object.

| PC | Exact method | Production mutation -> discriminating fixture/assertion |
|---|---|---|
| PC-120 | X.Unanswered_nudge_pages_exact_channel_at_deadline | Publisher uses an agent-associated/first channel instead of frozen configured channel -> deliberately different provider/conversation decoy receives page. Assert Channel, ConversationId, no redirecting ReplyHandle, immutable page ID/body and persisted publication. |
| PC-121 | X.Unanswered_nudge_pages_exact_channel_at_deadline | Publisher checks now > AnswerDueAt -> no acceptance at exact deadline; one tick before remains quiet. Receipt-only nudge still pages. |
| PC-122 | X.Unavailable_or_unsafe_recipient_pages_immediately | Refused/Uncertain outcome waits AnswerMinutes -> unsafe composer/no-current-session/transport-failed cases lack immediate Due publication; no direct queue fallback or session recovery. |
| PC-123 | X.Unanswered_nudge_pages_exact_channel_at_deadline | Operator publisher acquires session-input lock first -> due page cannot complete while independently held session lock blocks a direct sender. Bound/observe completion before releasing the lock in finally. |
| PC-124 | X.Unanswered_nudge_pages_exact_channel_at_deadline | Operator publisher awaits transcript catch-up before due work -> a gated failing catch-up prevents page at deadline. Release/cancel catch-up after page assertion; no detached task. |
| PC-125 | X.Broker_failure_retries_same_frozen_page_after_restart | Retry recomputes body/destination from current evidence -> post-failure/reload retry differs in bytes, digest, target or ordinal; a genuinely changed config retires the old unsent page instead. |
| PC-126 | X.Broker_failure_retries_same_frozen_page_after_restart | Retry schedule returns zero delay -> calls occur before 1, 5, 15, 15 minute delays; advance N-1/N for each attempt and inspect persisted attempts/NextAttemptAt through reload. |
| PC-127 | X.Accepted_but_unstamped_page_recovers_with_same_identity | Acceptance is treated as durable Published before completion stamp commits -> after injected post-accept stamp failure/restart no recoverable debt remains. After claim expiry permit exactly the documented duplicate with same page identity/ordinal. |
| PC-128 | X.Accepted_but_unstamped_page_recovers_with_same_identity | Recovery allocates a new publication ordinal -> accepted-but-unstamped retry loses its immutable identity. Compare both recorded broker payloads, not just a call count. |
| PC-129 | X.Concurrent_claims_and_ack_have_one_normal_publication | Claim update drops optimistic eligibility/ownership -> synchronized separate contexts both reach SendAsync in the normal case. No exception-only verdict; require one accepted call and one matching publish audit. |
| PC-130 | X.Concurrent_claims_and_ack_have_one_normal_publication | Completion update drops claim-token predicate -> stale worker stamps a replacement claimant's page. Pause before completion, expire/reclaim, then release old worker; stale stamp must affect zero rows. |
| PC-131 | X.Concurrent_claims_and_ack_have_one_normal_publication | Publisher omits final ACK eligibility recheck -> ACK committed between claim and first byte still publishes. Separate after-accept ACK variant must retain Published and record honest late race, never claim retraction. |
| PC-132 | X.Disabled_channel_retains_visible_unsent_debt | Missing/disabled channel marks work Published/Suppressed -> debt disappears; no send and a visible config fault/debt survive reload. Enabled exact-channel control publishes. |
| PC-133 | X.Reminders_are_bounded_and_audited | Reminder clock uses last attempt instead of last successful publication/required repeat boundary -> too-early page or no due reminder after failed retry; custom RepeatMinutes N-1/N and fresh contexts expose it. |
| PC-134 | X.Reminders_are_bounded_and_audited | Reminder reuses published ordinal -> second accepted reminder fails unique identity/audit ordinal assertion; same-attempt retry still keeps its ordinal. |
| PC-135 | X.Audit_failure_prevents_publish | Publisher sends before claim/audit transaction commits -> separate-context observer sees no committed page/audit at SendAsync; fail audit persistence and assert zero calls and recoverable debt. |
| PC-136 | X.Audit_failure_prevents_publish | Publisher keeps DB transaction open across broker I/O -> gated producer observes active transaction/blocks independent row access. Require committed claim readable from separate context and no ambient/current transaction at I/O. |
| PC-137 | U.Status_exposes_scan_and_delivery_evidence | Status derives fresh scan time from request time -> failed scan followed by GET falsely advances freshness. Persist distinct success/error/attempt/receipt/answer/page evidence and compare exact IDs/times; observe no outbound or state mutation. |
| PC-138 | U.Status_requires_authorized_board_and_paginates | Endpoint drops board/project task-token authorization -> valid token for board A reads sentinel episode/audit of B. Missing boardId rejected; authorized A control reads only A. |
| PC-139 | U.Status_requires_authorized_board_and_paginates | History ignores cursor or board predicate on later pages -> page 2 repeats/omits IDs or leaks B; exact union of small pages equals only A's bounded history. |
| PC-140 | U.Status_never_exposes_channel_address_or_transcripts | Status projects persisted raw page/transcript/address fields -> distinctive secrets/address/body sentinels appear in serialized response, including errors/history; safe IDs/status/audit links remain visible. |

### CP-7: composition, scheduling and complete scenarios

| PC | Exact method | Production mutation -> discriminating fixture/assertion |
|---|---|---|
| PC-141 | J.Registers_minute_job_on_dedicated_queue | Production registration uses default queue or different cron -> InMemoryStorage job is not `antiphon:expectation-watchdog`, UTC `* * * * *`, queue expectations. Resolve manager through DI without priming JobStorage.Current. |
| PC-142 | J.Registers_minute_job_on_dedicated_queue | Job restores automatic Hangfire retry -> effective job retry filter is nonzero; inject one job failure and verify durable next-sweep retry rather than framework reexecution. |
| PC-143 | J.Disabled_host_or_feature_starts_no_worker | Dedicated registration ignores Hangfire:ServerEnabled -> (host=false, feature=true) gets worker/job/startup catch-up. Test all four settings combinations without booting unsafe Program worker. |
| PC-144 | J.Disabled_host_or_feature_starts_no_worker | Dedicated registration ignores ExpectationWatchdog.Enabled -> (host=true, feature=false) gets watchdog worker/job/catch-up. Existing default worker configuration remains default-only. |
| PC-145 | J.Fresh_host_recovers_due_work_with_real_adapters | Production DI resolves NoExpectationCatchUp/default probe factory -> first host's committed due debt/late runtime transcript are not reconciled by a fresh host. Assert concrete resolved types and persisted outcome through production registration, not only type names. |
| PC-146 | J.Fresh_host_recovers_due_work_with_real_adapters | Startup hook omits initial catch-up -> already-due page is absent before any minute tick. Create new InMemoryStorage for second host while retaining PostgreSQL rows and virtual clock. |
| PC-147 | J.Default_worker_occupancy_does_not_block_watchdog | Dedicated server consumes default queue -> with sole default worker blocked, triggered watchdog job cannot finish. Assert actual job completion/page while blocker remains held, then release/await both. |
| PC-148 | J.Slow_directive_does_not_starve_operator_debt | Job awaits slow directive before independently due operator sweep -> second directive's due page is missing while first probe/send is blocked. Use separate DbContexts and sentinel second board. |
| PC-149 | J.Slow_directive_does_not_starve_operator_debt | Probe timeout budget is removed -> 5s virtual probe boundary no longer cancels/records observation fault or permits next directive. Timed-out subject remains unknown/open, not clear. |
| PC-150 | J.Slow_directive_does_not_starve_operator_debt | Direct-send timeout budget is removed -> locked/slow recipient prevents bounded 20s completion and durable page eligibility; no unsafe write after cancellation. |
| PC-151 | J.Slow_directive_does_not_starve_operator_debt | Operator-send timeout budget is removed -> cancelable broker call survives 5s and blocks remaining pages. Observe durable retry and zero owned I/O after timeout completion. |
| PC-152 | J.Overlapping_passes_and_budget_cursors_are_safe | Pass ignores 50s budget -> it begins additional subject work after budget. Resume persisted fair cursor with a new host; incomplete work never counts as clear. |
| PC-153 | J.Overlapping_passes_and_budget_cursors_are_safe | Job skips durable overlap claim -> two gated passes process same directive/page concurrently; assert one normal nudge/audit/publication and all directives eventually visited. |
| PC-154 | Z.Overnight_fence_nudges_and_pages_without_task_notes | Scenario's production job skips direct delivery after committing nudge -> virtual five-hour freeze has audits but no complete UserPrompt/no deadline page. Seed three Held tasks with **zero caller note rows**; require independent prompt and exact broker target. |
| PC-155 | Z.Idle_and_working_callers_receive_complete_prompt | Watchdog job waits for ordinary caller-note receipt before sending -> Working case stalls behind pending WhenIdle note. Both idle and working cases must deliver full immutable body/floor; queued-only then later UserPrompt variant enforces distinct states. |
| PC-156 | Z.Idle_and_working_callers_receive_complete_prompt | Watchdog routing takes repository mutation lease -> recording/refusing lease boundary is invoked or no prompt arrives while lease is fenced. Fixture must wire the actual production lease seam, not an unused mock. |
| PC-157 | Z.Recovery_and_late_note_receipt_resolve_without_mutation | Journal observation treats recovery-result success as fence clearance -> episode resolves while actual fence remains. Only changed fence plus two complete successful observations at least a minute apart may resolve; watched note rows remain untouched. |
| PC-158 | Z.All_five_conditions_have_durable_subject_audits | Service omits one condition family from aggregation (SilentInFlight) -> its subject lacks episode/nudge association/Check despite other four firing. Independently due fixture for **each** family asserts exact subject IDs and audit-card fallback where taskless. |
| PC-159 | HF.Assembly_guard_and_factory_override_disable_the_Hangfire_worker | Production composition registers watchdog hosted server outside the disabled-host gate -> shared guarded Program factory unexpectedly has Hangfire hosted worker. Keep dead runner URL and fake census; do not actually contact production. |

## Additional independent guard arms

These rows prevent a multi-case method from hiding independent bypasses. They use the same
checkpoint filters and do not add ordinary executions. Each remains its own PC cycle.

| PC | CP | Exact method | Production mutation -> decisive assertion |
|---|---|---|---|
| PC-160 | CP-1 | S.Backlog_candidates_exclude_owned_held_unrated_and_open_work | Remove ownership exclusion -> assigned/owned/live-session card enters exact eligible set. |
| PC-161 | CP-1 | S.Backlog_candidates_exclude_owned_held_unrated_and_open_work | Remove explicit hold exclusion -> AutoDispatchHeldAt card enters eligible set. |
| PC-162 | CP-1 | S.Backlog_candidates_exclude_owned_held_unrated_and_open_work | Remove imported-unrated exclusion -> unrated import enters eligible set; human-rated import stays eligible. |
| PC-163 | CP-1 | S.Backlog_candidates_exclude_owned_held_unrated_and_open_work | Remove card archive exclusion -> archived Backlog enters eligible set. |
| PC-164 | CP-1 | D.Disabled_or_expired_directive_has_no_effects | Ignore global feature Enabled -> disabled settings authorize effects despite enabled directive. |
| PC-165 | CP-1 | O.Composition_reads_actual_catalog_and_policy | Treat unavailable transport as authoritative stopped runner -> Unknown evidence incorrectly becomes a known lane fence. Authoritatively stopped control still fences. |
| PC-166 | CP-1 | O.Catchup_precedes_absence_judgment | Adapter accepts inventory from a different accepted generation/runner owner -> stale remote absence falsely becomes missing; matching current inventory legitimately detects missing. |
| PC-167 | CP-2 | B.Dispatched_without_session_and_report_is_detected | Include Blocked tasks in silent population -> intentionally blocked row gets an episode/Check. |
| PC-168 | CP-2 | B.Dispatched_without_session_and_report_is_detected | Count watchdog Check/ACK/self-prompt transcript as task progress -> otherwise due task goes quiet; unrelated real task activity still resets its clock. Seed task-local/self evidence separately. |
| PC-169 | CP-2 | N.Confirmed_superseded_and_noncaller_rows_are_excluded | Drop destination ownership requirement -> foreign caller note appears; earlier proven owned session remains included. |
| PC-170 | CP-2 | N.Catchup_uses_owning_receipt_and_preserves_original_age | Watchdog marks received watched row Confirmed/Sent -> before/after snapshot of outbox/queue changes. Read-only receipt can remove detection but cannot settle producer state. |
| PC-171 | CP-4 | A.Wrong_session_generation_and_sequence_do_not_answer | Receipt reconciliation drops generation evidence (independent of ACK generation) -> full UserPrompt from restarted same-ID session sets ReceiptAt for older attempt. |
| PC-172 | CP-4 | A.Assistant_ack_needs_matching_id_turn_and_action | Remove error exclusion while retaining AssistantText kind -> error-marked matching answer is accepted. Non-error answer control succeeds. |
| PC-173 | CP-5 | I.Unsafe_composer_or_modal_sends_no_bytes | Skip forbidden-body guard -> provider control command commits/writes rather than refusing; ordinary marked prompt succeeds. |
| PC-174 | CP-5 | I.Unsafe_composer_or_modal_sends_no_bytes | Skip Herdr blocked guard -> otherwise live blocked pane receives input. |
| PC-175 | CP-5 | I.Unsafe_composer_or_modal_sends_no_bytes | Skip backend-unreachable guard -> attempt/bytes appear for unreachable metadata. |
| PC-176 | CP-5 | I.Delivery_failure_never_stops_or_restarts_session | Route expectation failure through ordinary delivery-failure recovery -> recording launch/kill/incident sink becomes nonzero. OverlayRecovery remains false in this independent mutant. |
| PC-177 | CP-5 | I.Uncertain_watchdog_body_blocks_later_ordinary_input | FlushSession skips expectation composer check -> pending note types over body; Now guard remains intact. |
| PC-178 | CP-5 | I.Uncertain_watchdog_body_blocks_later_ordinary_input | SendNow skips expectation composer check -> forced queued note types over body. |
| PC-179 | CP-5 | I.Uncertain_watchdog_body_blocks_later_ordinary_input | Local-command poll skips expectation composer check -> /status or /usage is typed, instead of Skipped. |
| PC-180 | CP-5 | I.Uncertain_watchdog_body_blocks_later_ordinary_input | Second nudge skips expectation composer check -> second body writes over first. |
| PC-181 | CP-5 | H.Release_of_another_session_releases_nothing | Release query drops generation match -> current-session release changes old-generation hold; valid current-generation control releases only its own row. |
| PC-182 | CP-5 | I.Operator_release_clears_a_whole_screen_hold_with_an_audit_and_no_input | Release persists Released outside audit transaction -> injected audit failure leaves hold released with no actor/reason evidence. Keep empty-reason refusal. |
| PC-183 | CP-6 | X.Concurrent_claims_and_ack_have_one_normal_publication | Claim renewal does not check ownership -> expired old publisher extends/stamps new claimant's lease. Gate cancelable send at renewal and assert conditional row ownership plus eventual recovery. |
| PC-184 | CP-6 | X.Disabled_channel_retains_visible_unsent_debt | Missing-channel resolution falls back to agent/first enabled channel -> decoy producer target appears; missing configured channel must retain debt and send nothing. |
| PC-185 | CP-6 | X.Concurrent_claims_and_ack_have_one_normal_publication | Final publisher eligibility ignores positive episode resolution -> resolved-after-claim page still sends. Use two qualifying clear observations before releasing publication barrier. |
| PC-186 | CP-7 | J.Slow_directive_does_not_starve_operator_debt | Timeout returns without awaiting cancellation of owned probe/send -> completion observer sees in-flight I/O or a late side effect after pass returns. No real sleep establishes this barrier. |
| PC-187 | CP-7 | Z.Recovery_and_late_note_receipt_resolve_without_mutation | Watchdog takes recovery action for Dead journal -> recording recovery/lease/kill/spawn/card-move/tracker sinks show forbidden action; unchanged scratch journal/locks and queued task rows prove observation only. Mutate the observation-to-action branch only, never real recovery scripts. |

## Checkpoint roster validation

| CP | Existing executed roster | New methods | Required executions | PC count |
|---|---|---:|---:|---:|
| CP-1 | Directive 4 + Pipeline 9 + Snapshot 6 | Directive 1 + ObservationAdapter 6 = 7 | 26 | 37 |
| CP-2 | Ledger 4 + Debt 6 + Episode 5 | NoteDebt 4 | 19 | 29 |
| CP-3 | DispatchHeldAttention 9 single executions + 1 method with 2 arguments | 0 | 11 | 6 |
| CP-4 | Receipt 6 + Episode 5 (rerun after S2 change) | Response 8 + Scheduling 4 = 12 | 23 | 31 |
| CP-5 | DirectDelivery 14 + HoldReleaseEndpoint 4 + exact public queue methods 4 | 0 | 22 | 39 |
| CP-6 | none | Escalation 8 + Status 3 = 11 | 11 | 24 |
| CP-7 | HangfireStartupSafety 12 | Job 6 + Scenario 4 = 10 | 22 | 21 |

Total: **134 ordinary executions = 85 existing unique + 44 new unique + 5 repeated**.
There are **187 independently commissioned PCs**. The CP-5 class/method intersection admits
exactly the 18 C650 methods in I/H plus the four named public queue methods. CP-3's eleven
results must include both land-yield argument rows. New methods remain nonparameterized;
their matrices do not raise Min. Validate actual executed names in fresh TRX, not discovery.

The companion's tables are an inventory; the **only ordinary executable manifest** remains
the continuation plan's `### Checkpoints`. Its seven rows, S1-S4 grouping, serial ordering,
four isolated builds and three build reuses are preserved. CP-4 adds EpisodeTests and one
estimated minute because S2 changes that already-tested class's recurrence contract.

## Cost and execution handoff

Ordinary Code floor is **52 minutes** (8+6+1+9+6+10+12), plus the existing estimated two-minute
checkpoint-tool bootstrap. Retain the plan's 420-minute authoring estimate: **474 minutes**
overall. Fixture work described here is part of that authoring estimate, not free verification.
If the implementation cannot fit it, report a revised forecast; these are not measurements,
timeouts or authorization to omit coverage. CLI migration prerequisite builds remain separately
reported unlisted authoring runs under a build slot. No build/test was needed to edit this design.

Mutation is a **separate post-land SourceLanding commission**. Conservative serial cost, with
no unproved batching savings, assumes each PC gets three isolated builds plus three exact
method invocations (baseline, intended assertion-red, restored-green). At two minutes/build
and one minute/method phase (including fixture startup), this is **561 builds + 561 method
invocations = 1,683 minutes**, plus 187 minutes to apply/restore/check individual patches and
60 minutes for custody/evidence/triage setup: **1,930 minutes** (about 32h10m), before reruns.
This deliberately exposes the full cost of 187 independently bypassable guards. It is an
estimate for commissioning, not a claim that a single test consumes a minute. HA's one
parameterized PC adds one extra result per phase: **564 minimum test results** across the
561 method invocations. Ordinary counts, mutation results and assertion matrices are separate.

Different-file/different-method independent mutations may later batch under the testing owner;
same-file/method or interacting guards must remain separate. Record actual saved builds/time,
never silently collapse guards. New code may expose more independently bypassable predicates;
add a PC and cost before claiming complete Mutation coverage. A surviving control is a test
gap to repair; fixture/compile failure, timeout, zero tests or unrelated assertion is not red.

For each future PC use the unchanged copied `scripts/run-checkpoint.ps1` driver, a host build
slot, the exact method filter defined above, `-Expect Class.Method`, MinExecuted 1 (2 for
PC-060), and separate external evidence/output roots for all three phases. Baseline and restored
green need exit 0, no failures/skips and exact roster; mutant needs completed build exit 0 and
driver exit 1 with the specified assertion in fresh TRX. Refresh restored source timestamps
and rebuild to avoid stale mutant DLLs. Await every child and restore exact source/index bytes;
do not commit/push SourceLanding mutations. Follow
[the testing owner's mutation/custody contract](../../testing-and-build.md#mutation-stage-positive-control-execution-card-0451).

Next: Code against the amended continuation plan at this TestDesign commit. No product decision
blocks Code. Real configured IDs, deployment and gateway receipt remain caller-owned activation;
this design and fake broker acceptance do not establish that the five-hour incident is fixed.
