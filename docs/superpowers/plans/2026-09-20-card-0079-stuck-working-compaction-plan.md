
## Amendment 2026-09-22: disposition of the 127 missing PC methods

Plan task `022820cb`, inspected tip `abc7b90d1793ea2ae0e9e2b189a05678d73929b2`
(the tip Review `3986b2f9` rejected). Review D-2 found that 127 of the 217
`Alias.Method` selectors in the two PC tables have no test method. A mechanical
re-check here (every `Task`/`void` method name under `tests/`, 9,601 distinct
names at this tip) reproduces exactly those 127 rows: A 54, F 30, B 13, H 11,
D 9, N 7, W 3. No `[Arguments]` row hides any of them.

This amendment gives every missing row one of three dispositions:

- **keep+cover** — the control names something the code really has (or that
  D-16 adds), and it just lacks a test. That becomes Code work, listed in the
  slices and Checkpoints below. Nothing was implemented by this Plan task.
- **repoint** — the guard is real but its seam is not where the row put it: an
  existing method already proves it, a pure predicate is the honest layer, or
  the boundary the row names is one shared call already guarded by another row.
  The row keeps its number and maps to the named target.
- **retire** — the row named a sub-design that was never built and whose safety
  property is carried by other rows, or a case the code deliberately decides
  the other way. Each retirement is a numbered decision below.

Ten rows stay with the sibling Code task `b976e9ae` (D-1 vetoes, D-3 wire
tests, PC-13). They are listed here as keep+cover with that owner so the manifest
is closed; this amendment does not design them.

### Ground truth

| The parent plan and its verification design assume | What `abc7b90d` does |
|---|---|
| The D-2 scope is checked by compaction-specific code at three boundaries, each clause separately mutable there. | `CheckCompactionScope.Refusal` is one pure 27-clause predicate over `CompactionScopeSnapshot`; `BuildScopeAsync` populates it and is called at discovery and again in `BeginStopAsync`. Five hold clauses and `CurrentStandingOwnership` are unpopulated (Review D-1, sibling task). Discovery refuses silently (no episode row); pre-stop refusal is `NeedsDecision` with the clause name as `Reason`. |
| Resume has its own preflight with per-hold vetoes (PC-131…143 resume halves). | Resume is `AgentControlService.ResumeAsync` → the ordinary `StartAsync(automatic: true, ResumeSessionId: episode.SessionId, compactionRecoveryId)`. Holds come from the ordinary gates (`_quotaGate`, `_modelAvailability`, Herdr hold) and from `StandingSpecialistSeatPolicy.StartRefusalAsync(automatic: true)` (Suspended, liveness, Herdr, owner AlwaysOn/pool, routing). `RequireCurrentCheckLaunchAsync` repeats pointer, generation and that refusal at every launch boundary. Continuity hold is bypassed when `ResumeSessionId` is explicit. |
| Episode commit rechecks pointer and standing owner (PC-153/154). | The commit uses the scope built once at discovery. `BeginStopAsync` rebuilds the scope and supersedes on session/generation mismatch, and refuses on any clause, before any runner effect. |
| A durable restart fence precedes G2 admission, with a repair path (PC-33/157). | `WriteRestartBoundaryIfInterruptedAsync` swallows its own failure and returns false; `AdoptLaunchAsync` moves to `AwaitingCheck` on `Running` alone, so a failed fence leaves a resumed seat whose brief can never type. |
| `NeverAttempted` members are guarded by F integration flips (PC-144…150). | `StandingQueueSwitchPolicy.NeverAttempted` is a shared pure predicate used by three services; it has no unit class. |
| A missing brief never proves an untyped Check (G-96). | `ReleaseOccupantsAsync` treats zero linked rows as never-attempted and releases the occupant once both deadlines pass. That is the CARD-0079 zombie case. |
| Recovery needs whole new-interpreter-brief receipt (PC-112) and a non-whitespace reading (PC-114). | `TryReceiptAsync` requires a Succeeded Check with `Result != null && Result != ""` on the resumed session, its task marker in a G2 `UserPrompt`, a later `TurnEnd`, a `Sent` caller row with a baseline, and the whole `note.Body` contained in a parent `UserPrompt` above the baseline. Whitespace passes; the legacy path uses `IsNullOrWhiteSpace`. |
| Routed exhaustion could re-enter the blocked legacy primary (PC-100). | `SpecialistRequestService` skips every closed seat inside one candidate loop; there is no legacy fallback branch. |
| PC-20/78/125/126/166 have no method. | `Unsupported_stop_never_falls_back_or_resumes`, `Duplicate_episode_key_is_rejected` and `Unresolved_episode_survives_and_old_terminal_audit_prunes` exist under other names; the last is not red-capable at its current ages (open row at 0 d, terminal at 10 d). `Successful_unchanged_pull_confirms_but_failed_pull_does_not` already asserts `TerminationSource == CompactionContinuationRecovery`. |
| Health projection has a warm-ready flag (PC-111). | `StandingSpecialistHealthService` excludes `CheckCompactionAdmission.ClosedSeatIdsAsync` seats from `ready` via `NotStalled`. |
| Supervisor and capacity recovery consult the episode (PC-25/83/84). | Both `AgentSupervisorService` sites call `CheckCompactionAdmission.BlocksAutomaticRestartAsync` (true for every unresolved state including `NeedsDecision`). The healthy-uptime branch never touches the compaction fields. |

### Decisions

- **D-11 — Scope clauses are covered at the population layer.** Each clause row
  (PC-15/34–46/138/140/142/151) is one discovery-boundary flip: the eligible seat
  of `CheckCompactionContinuationTests.SeedAsync` with exactly one fact changed,
  asserting zero stops and zero episode rows, with the unflipped twin producing one
  stop inside the same method. The primary mutant is "hard-code the eligible value
  in `BuildScopeAsync`"; deleting the clause from `Refusal` is the equivalent
  secondary mutant and goes red on the same assertion. Rejected: pure-predicate
  tests only (they cannot see a hard-coded population, which is exactly how D-1
  reached review); repeating every clause at all three boundaries (one shared
  predicate; the pre-stop rebuild is guarded once by PC-32, the resume call once by
  PC-152).
- **D-12 — Resume-side hold rows repoint to the ordinary automatic-start gates.**
  There is no compaction-specific resume preflight to mutate. PC-131/133/141 map to
  the existing `StartAsync` gate tests named below; PC-143 and the automatic
  refusal set map to PC-152, re-specified as "`ResumeAsync` keeps `automatic: true`"
  (mutant: pass `automatic: false`, which drops the physical seat's Suspended,
  liveness and Herdr refusals). Rejected: seven real-graph resume tests that would
  re-prove gates their own suites already prove.
- **D-13 (default) — An explicit `ResumeSessionId` is the continuity decision.**
  `StartAsync` bypasses the continuity hold for an explicit resume, and the
  compaction resume names the captured conversation. PC-139 repoints to PC-138
  (stop side) plus PC-89 (a failed launch is `NeedsDecision`, never Fresh). Veto
  means Code adds a continuity check inside `ResumeAsync` and PC-139 returns as a
  real-graph A control.
- **D-14 (default) — PC-96 is retired.** An occupant with no linked queue row is
  untyped; releasing it after both deadlines is the fix for the original zombie
  (a brief that moved sessions). Rows naming another task are not this task's
  evidence, and PC-8 already guards attempted rows. Veto means production changes
  `ReleaseOccupantsAsync` to require at least one linked row and PC-96 returns.
- **D-15 (default) — PC-64 repoints to the observation contract.** Partial DB
  persistence is caught where the fresh tail is read: unavailable/unknown
  observations (PC-4, PC-17/60/61) and the runner's final revision rechecks
  (PC-18/66). No separate coordinator seam exists to mutate.
- **D-16 — Fence before admission is built, not retired.** `AdoptLaunchAsync` moves
  to `AwaitingCheck` only when the resumed transcript reads idle
  (`!SessionMessageQueueService.IsWorkingAsync`); if it still reads mid-turn on the
  reserved generation, adoption writes the restart boundary itself through the
  shared fence helper and retries on the next sweep, never launching again.
  PC-33 and PC-157 stay keep+cover against that change. Rejected: retiring both,
  which leaves `AwaitingCheck` reachable with a brief that can never type, i.e. a
  silent stall behind a "validation pending" badge.
- **D-17 (default) — PC-112 is retired.** The settled Check's own report (PC-114
  useful reading, PC-117 closing `TurnEnd`) is stronger evidence than byte-equality
  of the brief the interpreter was typed. Whole-body matching stays where no
  settlement exists: the caller note (PC-159).
- **D-18 — Shared methods are allowed for repointed rows with distinct mutants.**
  PC-127 and PC-166 run their own cycles against PC-4's method. The audit rule
  "duplicate PC mappings = 0" counts guards, not methods.
- **D-19 — Ownership split.** PC-13, PC-38/40/130/132/134/136 and PC-68/69/70 stay
  with Code task `b976e9ae`; its Review reconciles the alias table to the names it
  lands (rename-only). Every other keep+cover row is the follow-up Code work below.
- **D-20 — PC-100 repoints to PC-10.** There is no fallback branch to guard; both
  rows delete the same `ClosesSeatAsync` skip in one candidate loop.
- **D-21 — PC-151 drops its unknown-origin variant.** A never-attempted
  `System`-origin pending row is carried to G2 by design (D-6); the veto covers
  `Ui`/`Channel`/`Scheduled` origins only.

### Disposition table

Method names are exact selectors in the alias's class unless the target column
names another class. "flip" means the D-11 pattern. "seed Confirmed" means the
`CheckCompactionFixture.SeedConfirmedAsync` seat plus the named change. `Stops`
is `FakeSessionRunnerClient.CompactionStops.Count`, `Kills` is `KillCalls`,
`Resumes` is the counting `ICompactionContinuationResume`'s call count.

| PC | Disposition | Seam at `abc7b90d` | Method or target | Compiling defect | Red assertion |
|---|---|---|---|---|---|
| PC-5 | keep+cover | `DiscoverSeatAsync` second `Evaluate` after observation | `D.Catch_up_progress_saves_the_suspected_session`; add `ObserveOverride` to `FakeSessionRunnerClient` so the observation callback inserts an `AssistantText` row after C | reuse `verdict` instead of `confirmed` | `episodes.Count.ShouldBe(0); Stops.ShouldBe(0)` |
| PC-6 | keep+cover | `BuildScopeAsync` `AcceptedStartedAt = Normalize(session.StartedAt)` + policy ownership fence | `D.Old_generation_observation_cannot_hold_the_replacement`: same session id, new `StartedAt`, B/C event times before it, runner reports new generation | `AcceptedStartedAt = DateTime.MinValue` | `replacementEpisodes.Count.ShouldBe(0)` |
| PC-8 | keep+cover | `ReleaseOccupantsAsync` pre-stop `attempted`/`neverAttempted` | `F.Attempted_or_sent_brief_retains_its_existing_owner_and_delivery_evidence`: Pending row `DeliveryAttempts = 1` | `attempted` uses `DeliveryAttempts > 1` | `occupant.Status.ShouldBe(Dispatched); row.ShouldBe(snapshot)` |
| PC-9 | keep+cover | `SpecialistTaskRunner` `ClosesSeatAsync` → `Held` | `F.Legacy_check_refuses_the_stalled_primary_before_task_creation` using the `AgentTaskCheckInterpreterTests` runner harness with a Confirmed episode on the seat | delete the `ClosesSeatAsync` branch | `run.Outcome.ShouldBe(Held); newCheckTasks.Count.ShouldBe(0)` |
| PC-10 | keep+cover | `SpecialistRequestService` candidate loop `ClosesSeatAsync` skip | `F.Routed_check_excludes_only_the_stalled_physical_seat`: primary closed, alternate qualified | delete the skip | `selectedPhysicalAgentId.ShouldBe(healthyAlternateId)` |
| PC-12 | keep+cover | `CancelRetiredBriefsAsync` runs on every `AdvanceAsync` | `F.Terminal_untyped_briefs_are_pruned_while_working_after_a_commit_gap`: Failed `CompactionRecoveryRetiredGeneration` Check with a never-attempted Pending brief, session Working | gate cleanup on `!IsWorkingAsync` | `obsoleteRow.Status.ShouldBe(Canceled)` |
| PC-13 | keep+cover (owner `b976e9ae`) | end-to-end detect → stop → resume → new Check → caller receipt | `H.Automatic_restart_delivers_a_new_check_and_its_whole_caller_note` | as in the parent row | as in the parent row |
| PC-14 | repoint | ordinary D8/D9; the coordinator touches only seats with an episode | `AgentTaskDeliveryWatchdogTests.a_working_session_with_a_pending_brief_is_neither_failed_nor_killed` + PC-7 | — | — |
| PC-15 | keep+cover | `PhysicalAlwaysOn = agent.AlwaysOn` | `A.Physical_always_on_is_required` (flip `AlwaysOn = false`) | hard-code `true` | flip assertion |
| PC-20 | repoint (rename) | `FinishStopAsync` `!ConfirmsExit` | rename `Unsupported_stop_never_falls_back_or_resumes` → `A.Unsupported_or_ambiguous_stop_never_falls_back_or_resumes`; add the accepted-but-not-exited arm | treat `ConfirmsExit == false` as `Stopped` | `Resumes.ShouldBe(0); Kills.ShouldBe(0)` |
| PC-21 | keep+cover | `FinishStopAsync` Suspended recheck at `before-stop-rpc` | `A.Human_stop_between_detection_and_stop_revokes_recovery`: boundary hook sets `Suspended = true` in a second context | delete the recheck | `Stops.ShouldBe(0); state.ShouldBe(SupersededByOperator)` |
| PC-22 | keep+cover | `before-stop-commit` → `SaveChanges` → RPC | `A.Restart_intent_must_commit_before_any_runner_effect`: `SaveChanges` interceptor throws at that hook | issue the RPC before `SaveChanges` | `Stops.ShouldBe(0); episode.AttemptId.ShouldBeNull()` |
| PC-23 | keep+cover | `FinishStopAsync` catch → `stop-lost` | `A.Lost_stop_response_reconciles_only_the_captured_generation`: fake stop throws; twin with `Exited` result | treat the exception as a confirmed exit | `state.ShouldBe(NeedsDecision); Resumes.ShouldBe(0); session.Status.ShouldBe(Running)` |
| PC-25 | keep+cover | `AgentSupervisorService` first `BlocksAutomaticRestartAsync` | `A.Supervisor_cannot_bypass_an_active_or_failed_compaction_operation` using the `AgentSupervisionTests` harness, episode `StopRequested` | delete that check | `supervisorLaunches.Count.ShouldBe(0)` |
| PC-26 | keep+cover | `CheckCompactionBudget.Allows` over durable `LastAutomaticCompactionRestartAt` | `B.One_compaction_restart_per_day_survives_pruning_and_service_recreation`: last at 23:59:59.999, incidents deleted, new service | `Allows` returns true when no incidents exist | `Stops.ShouldBe(0); episode.Reason.ShouldBe("budget")` |
| PC-27 | keep+cover | same, receipt half | `B.A_second_restart_needs_useful_check_and_caller_receipt_even_after_a_day`: last at 25 h, `ReceiptEligible = false` | drop the receipt half | `Stops.ShouldBe(0)` |
| PC-28 | keep+cover | `StartAsync` `compactionRecoveryId` branch; `ResumeAsync` request | `A.Automatic_recovery_preserves_history_and_all_launch_holds` on `AgentControlServiceIntegrationTests.BuildHarness`: `Fresh: true` with a recovery id → 409; `ResumeAsync` resumes the same id | `Fresh: true` in `ResumeAsync` | `error.Code.ShouldBe("standing_recovery_operator_required"); acceptedResume.SessionId.ShouldBe(oldSessionId)` |
| PC-29 | keep+cover | `ReleaseOccupantsAsync` after-stop retirement | `F.Confirmed_stop_retires_exact_old_checks_without_replay`: Stopped episode, younger never-attempted Dispatched Check | apply the pre-stop age check after stop | `young.Status.ShouldBe(Failed); young.FailureCode.ShouldBe(CompactionRecoveryRetiredGeneration); brief.Status.ShouldBe(Canceled)` |
| PC-30 | keep+cover | `TryReceiptAsync` parent prompt above baseline | `N.Resumed_is_not_recovered_until_the_whole_parent_note_arrives`: Sent row with baseline, no matching parent prompt; then add it | treat `Sent` as receipt | `state.ShouldBe(AwaitingCheck)` before, `Recovered` after |
| PC-31 | keep+cover | `AdvanceAsync` disabled early return | `A.Disabling_recovery_revokes_unissued_stop_and_resume`: seed Confirmed, threshold 0 | delete the early return | `Stops.ShouldBe(0); state.ShouldBe(Confirmed)` |
| PC-32 | keep+cover | `BeginStopAsync` scope rebuild + `Refusal` | `A.Attempted_or_unrelated_input_vetoes_the_automatic_stop`: seed Confirmed plus Pending row `DeliveryAttempts = 1` | delete the `Refusal` call in `BeginStopAsync` | `Stops.ShouldBe(0); episode.Reason.ShouldBe("attempted-input")` |
| PC-33 | keep+cover (D-16) | new `AdoptLaunchAsync` idle gate | `A.Failed_restart_fence_write_keeps_new_generation_admission_closed`: ResumeReserved, G2 Running, mid-turn transcript, interceptor fails the boundary insert | drop the `!IsWorkingAsync` gate | `state.ShouldBe(ResumeReserved); ClosesSeatAsync.ShouldBeTrue(); newCheckTasks.Count.ShouldBe(0)` |
| PC-34 | keep+cover | `LogicalOwnerAlwaysOn = owner.AlwaysOn` | `A.Logical_owner_always_on_is_required` (separate owner, `AlwaysOn = false`) | hard-code `true` | flip assertion |
| PC-35 | keep+cover | `CheckSeat = IsCheck(agent)` | `A.Non_check_standing_specialist_never_restarts` (`StandingSpecialistRole = Diagnose`) | hard-code `true` (widen `Seat` in the same mutant if it already excludes non-Check seats; Code records which) | flip assertion |
| PC-36 | keep+cover | `EffectiveClaudeCode` | `A.Effective_non_claude_check_never_restarts` (`session.AgentKind = Codex`) | hard-code `true` | flip assertion |
| PC-37 | keep+cover | `PoolOwned = agent.IsPoolDelegate` | `A.Pool_owned_check_never_restarts` | hard-code `false` | flip assertion |
| PC-38 | keep+cover (owner `b976e9ae`) | `CurrentStandingOwnership` population | `A.Unproven_standing_owner_never_restarts` | as in the parent row | flip assertion |
| PC-39 | keep+cover | `LegacySlugOnly && !PositivelyCorrelatedOwningCheck` and `LoadFactsAsync` correlation | `A.Slug_lookalike_without_correlated_check_never_restarts`: slug-only seat whose current prompt is ordinary | treat every `UserPrompt` as a correlated Check (drop the token match and hard-code the clause) | flip assertion |
| PC-40 | keep+cover (owner `b976e9ae`) | `IsHumanOrigin` population | `A.Human_turn_vetoes_automatic_restart` | as in the parent row | flip assertion |
| PC-42 | keep+cover | `OpenNonCheckAssignment` | `A.Non_check_execution_vetoes_stop` (Dispatched non-Check task on the seat) | hard-code `false` | flip assertion |
| PC-43 | keep+cover | `PendingCardAssignment` | `A.Pending_card_work_vetoes_stop` (`CurrentCardId` set) | hard-code `false` | flip assertion |
| PC-44 | keep+cover | `PhysicalEnabled = Status == Running` | `A.Disabled_physical_seat_vetoes_stop` | hard-code `true` | flip assertion |
| PC-45 | keep+cover | `OwnerEnabled` from routing | `A.Disabled_logical_owner_vetoes_stop` (`routing.Enabled = false`) | hard-code `true` | flip assertion |
| PC-46 | keep+cover | `DiscoveryEnabled()` and `CheckInterpretationEnabled` | `A.Disabled_check_interpreter_vetoes_stop` | ignore `CheckInterpreterEnabled` in both places (one defect) | flip assertion |
| PC-63 | keep+cover | `DiscoverSeatAsync` `NativeBoundaryId` comparison | `D.Native_boundary_identity_must_match` (observation returns another id) | drop the comparison | `episodes.Count.ShouldBe(0)` |
| PC-64 | repoint (D-15) | observation contract | PC-4 + PC-17/60/61 + PC-18/66 | — | — |
| PC-68 | keep+cover (owner `b976e9ae`) | `SessionRunnerHttpClient` capability gate | `W.Missing_compaction_capability_sends_no_stop_request` | as in the parent row | as in the parent row |
| PC-69 | keep+cover (owner `b976e9ae`) | no `/kill` fallback | `W.Conditional_stop_never_falls_back` | as in the parent row | as in the parent row |
| PC-70 | keep+cover (owner `b976e9ae`) | response attempt/generation match | `W.Crossed_stop_response_cannot_confirm_exit` | as in the parent row | as in the parent row |
| PC-72 | repoint | `RequireCurrentCheckLaunchAsync` → `StartRefusalAsync(automatic)` Suspended | `SpecialistStartIntentTests.Card0415_V22_human_stop_wins_against_queued_and_inflight_Check_launch` + PC-21 | — | — |
| PC-73 | keep+cover | `FinishStopAsync` `!DiscoveryEnabled()` after `stopped-committed` | `A.Zero_after_exit_prevents_resume`: threshold set to 0 at that hook | delete that check | `Resumes.ShouldBe(0); state.ShouldBe(DisabledNeedsDecision)` |
| PC-74 | keep+cover | `DiscoveryEnabled()` `_settings.Enabled` | `A.Disabled_delegation_prevents_unissued_stop` | drop `Enabled` from `DiscoveryEnabled()` | `Stops.ShouldBe(0)` |
| PC-75 | retire | same predicate as PC-74; resume boundary guarded by PC-73 | → PC-73 + PC-74 | — | — |
| PC-76 | keep+cover | `AdvanceAsync` falls through for `StopRequested` when disabled | `A.Disabled_discovery_still_reconciles_issued_action`: seeded `StopRequested` with attempt id, disabled | return early for every state when disabled | `Stops.ShouldBe(1); state.ShouldBe(DisabledNeedsDecision)` |
| PC-78 | repoint (rename) | unique episode key | rename `Duplicate_episode_key_is_rejected` → `B.Duplicate_boundary_has_one_durable_episode` | as in the parent row | as in the parent row |
| PC-80 | keep+cover | `stop-lost` branch leaves the timestamp | `B.Unknown_stop_does_not_refund_allowance` | clear `LastAutomaticCompactionRestartAt` on stop-lost | `storedLastAttempt.ShouldBe(stopRequestedAt); secondStops.ShouldBe(0)` |
| PC-81 | keep+cover | `TryReceiptAsync` `Result` predicate | `B.Receipt_without_useful_check_does_not_unlock_restart`: empty `Result`, matching parent prompt | drop the `Result` predicate | `state.ShouldBe(AwaitingCheck); ReceiptEligible.ShouldBeFalse()` |
| PC-82 | keep+cover | supervisor healthy-uptime branch | `B.Healthy_uptime_does_not_reset_compaction_budget` using the `AgentSupervisionTests` harness | clear both compaction fields in that branch | `storedLastAttempt.ShouldBe(original); ReceiptEligible.ShouldBe(original)` |
| PC-83 | keep+cover | `BlocksAutomaticRestartAsync` for `NeedsDecision` | `A.Failed_resume_remains_outside_supervisor_ladder` | return false for `NeedsDecision`/`DisabledNeedsDecision` | `supervisorLaunches.Count.ShouldBe(0)` across ticks |
| PC-84 | keep+cover | supervisor capacity site | `A.Capacity_recovery_stands_down_for_action_owned_seat` | delete that check | `capacityLaunches.Count.ShouldBe(0)` |
| PC-85 | repoint | `RequireCurrentCheckLaunchAsync` generation check | `StandingSessionSwitchConcurrencyTests.Obsolete_queued_launch_cannot_overwrite_a_newer_outcome` + `Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation` | — | — |
| PC-86 | repoint | same, pointer check | `StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight` + `Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation` | — | — |
| PC-87 | retire | operator supersession sets Suspended (PC-72); `OperatorRequest` termination cannot occur between reservation and launch of a session the recovery itself stopped | → PC-72 | — | — |
| PC-88 | keep+cover | `ResumeAsync` request has no `Prompt`; check seats skip auto-continue | `A.Strict_resume_never_replays_the_old_check` on `BuildHarness` | pass the old brief as `Prompt` | the launch queued by `ResumeAsync` carries `initialPrompt == null` (observe `AgentSessionLaunchQueue.EnqueueInteractiveSession`) and `SessionQueuedMessages` gains no row naming the old Check |
| PC-89 | keep+cover | `AdoptLaunchAsync` Failed → `launch-failed` | `A.Missing_resume_history_never_falls_back_to_fresh`: ResumeReserved, resumed row Failed | transition back to `Stopped` on Failed | `state.ShouldBe(NeedsDecision); Resumes.ShouldBe(0); sessionRows.Count.ShouldBe(1)` (seeded `ResumeReserved`, so the stub is never called) |
| PC-90 | repoint | runtime stale-exit fences | `AgentSessionRuntimeTests.An_exit_event_with_a_stale_generation_is_a_no_op_disposition` + `A_stale_exit_never_backfills_an_already_closed_row` | — | — |
| PC-91 | keep+cover | `FinishStopAsync` refusal leaves the session | `A.Conditional_refusal_preserves_live_session_status` (`ConfirmsExit = false`) | set `Stopped` on refusal | `session.Status.ShouldBe(Running); TerminationSource unchanged` |
| PC-92 | repoint | generic watchdog | `AgentTaskDeliveryWatchdogTests.a_working_session_with_a_pending_brief_is_neither_failed_nor_killed` | — | — |
| PC-93 | keep+cover | pre-stop `DispatchedAt` age | `F.Young_untyped_occupant_waits_until_its_deadline` (9:59.999) | drop the age check | `occupant.Status.ShouldBe(Dispatched)` |
| PC-94 | keep+cover | pre-stop `DetectedAt` grace | `F.Old_occupant_does_not_bypass_episode_grace` | drop the grace check | `occupant.Status.ShouldBe(Dispatched)` |
| PC-95 | keep+cover | occupant query `AgentSessionId == episode.SessionId` | `F.Foreign_generation_occupant_is_untouched` (Check on the agent's other session) | drop the session predicate | `foreignTask.Status.ShouldBe(Dispatched)` |
| PC-96 | retire (D-14) | zero linked rows is untyped by design | — | — | — |
| PC-97 | keep+cover | `AgentTaskDispatcher.PlaceOnStandingAgentAsync` `ClosesSeatAsync` | `F.Episode_confirmed_after_routing_blocks_final_dispatch` on the `DelegationTestServices` dispatcher harness | delete that check | `queuedTask.Status.ShouldBe(Queued); briefs.Count.ShouldBe(0)` |
| PC-98 | repoint | same loop predicate as PC-10 | → PC-10 | — | — |
| PC-99 | keep+cover | `ClosesSeatAsync(seat.Id)` keyed on the physical seat | `F.Healthy_alternate_is_not_blocked_by_logical_owner_episode` | key on `ownerId` | `selectedPhysicalAgentId.ShouldBe(healthyAlternateId)` |
| PC-100 | repoint (D-20) | no fallback branch | → PC-10 | — | — |
| PC-101 | keep+cover | cleanup selects Failed retired tasks only | `F.Open_check_brief_is_not_pruned` | drop the `Status == Failed` filter | `openCheckBrief.Status.ShouldBe(Pending)` |
| PC-103 | repoint | `FailOccupantAsync` writes directly | → PC-7 (asserts `Kills == 0`) | — | — |
| PC-104 | keep+cover | owning branch leaves delivery rows | `F.Delivered_owning_check_keeps_delivery_evidence` (owning Check with a `Sent` brief) | cancel/reset the owning task's rows | `deliveredBrief.ShouldBe(snapshot); owning.FailureCode.ShouldBe(CompactionRecoveryRetiredGeneration)` |
| PC-105 | keep+cover | `FailOccupantAsync` terminal early return | `F.Terminal_owning_check_is_not_rewritten` | drop the early return | `terminalTask.ShouldBe(snapshot)` |
| PC-107 | keep+cover | incident and attention actor stamps | `N.Automatic_audit_is_distinct_from_operator_action` after a sweep-driven `StopRequested` | stamp `"operator"` | `incident.Message.ShouldContain(Actor).ShouldContain(Authorization); item.Evidence.ShouldContain(Actor)` |
| PC-108 | keep+cover | attention built from episode rows | `N.Legacy_episode_attention_survives_incident_pruning` (incidents and routing rows deleted) | build from incidents | `items.Count(i => i.ConditionKey == key).ShouldBe(1)` |
| PC-109 | keep+cover | `AdoptLaunchAsync` → `AwaitingCheck` | `N.Launch_acknowledgement_keeps_validation_pending` (ResumeReserved, G2 Running, idle transcript) | set `Recovered` there | `state.ShouldBe(AwaitingCheck); item.Headline.ShouldContain("validation pending")` |
| PC-110 | keep+cover | `BeginStopAsync` `progress` → `AbortedProgress` | `N.Late_progress_aborts_without_claiming_restart` | transition `Recovered` on progress | `state.ShouldBe(AbortedProgress); recoveredIncidents.Count.ShouldBe(0); ActiveCompactionRecoveryId.ShouldBeNull()` |
| PC-111 | keep+cover | `StandingSpecialistHealthService` `NotStalled` | `N.Running_stalled_seat_is_not_warm_ready` on the `SpecialistHealthAttentionTests` harness | drop `NotStalled` from `ready` | `health.Status.ShouldNotBe(Healthy)` for a Running closed seat |
| PC-112 | retire (D-17) | — | → PC-114 + PC-117 (+ PC-159 for the caller note) | — | — |
| PC-113 | keep+cover | `TryReceiptAsync` `CreatedAt >= since` | `H.Old_check_result_cannot_recover_new_generation` | drop the floor | `state.ShouldBe(AwaitingCheck)` |
| PC-114 | keep+cover (production: `IsNullOrWhiteSpace`) | `Result` predicate | `H.Unusable_new_check_result_keeps_recovery_pending` (whitespace `Result`) | revert to `!= ""` | `state.ShouldBe(AwaitingCheck)` |
| PC-115 | keep+cover | receipt query `AgentSessionId == parent` | `H.Foreign_session_receipt_cannot_recover_seat` | drop the session predicate | `state.ShouldBe(AwaitingCheck)` |
| PC-116 | keep+cover | `Sequence > LastDeliveryBaselineSequence` | `H.Old_identical_caller_prompt_cannot_recover_seat` | drop the floor | `state.ShouldBe(AwaitingCheck)` |
| PC-117 | keep+cover | `ended` `TurnEnd` after the marker prompt | `H.Unclosed_reading_cannot_recover_seat` | drop the `ended` check | `state.ShouldBe(AwaitingCheck)` |
| PC-118 | keep+cover | `FailOccupantAsync` single `SaveChanges` | `F.Failure_and_caller_obligation_commit_together` (interceptor fails the notification insert) | save before adding the notification | `task.Status.ShouldBe(Dispatched)` |
| PC-120 | repoint | keyed enqueue is the generic land-notification path | `AgentTaskLandNotificationRecoveryTests.C467_V09_KeyedQueueRacesAndDistinctEvents` | — | — |
| PC-121 | repoint | H-4 busy leg | `H.A_dispatched_interpretation_reaches_a_busy_recipient_whole_after_recovery` + PC-13 | — | — |
| PC-122 | repoint | H-4 eligible leg | `H.A_dispatched_interpretation_reaches_an_already_eligible_recipient_whole` + PC-13 | — | — |
| PC-125 | repoint (rename + age) | prune excludes unresolved | rename existing method → `B.Unresolved_episode_survives_retention`; open row at 91 d | remove the unresolved exclusion | `open.ShouldNotBeNull(); stale.ShouldBeNull()` |
| PC-126 | keep+cover | 90-day terminal audit | `B.Terminal_recovery_audit_survives_ninety_days` (terminal at 89 d and 91 d) | cutoff 30 d | `at89.ShouldNotBeNull(); at91.ShouldBeNull()` |
| PC-127 | repoint (D-18) | discovery enumerates `StandingSpecialistSeatPolicy.Seat` | PC-4's method (no pending rows are seeded there) | enumerate only seats with Pending queue rows | `episodes.Count.ShouldBe(1)` |
| PC-128 | keep+cover | `SweepAsync` per-seat catch | `D.One_unavailable_seat_does_not_abort_other_discovery` (boundary hook throws at `observed` for seat 1) | let the exception escape the loop | `healthySeatEpisodes.Count.ShouldBe(1)` |
| PC-129 | keep+cover | detection writes no transcript rows | `D.Detection_preserves_the_working_verdict` | write a `SessionRestartBoundary` at detection | `working.ShouldBeTrue(); addedRows.ShouldBe(0)` |
| PC-130 | keep+cover (owner `b976e9ae`) | `ModelHold` population | `A.Model_hold_vetoes_stop` | as in the parent row | flip assertion |
| PC-131 | repoint (D-12) | ordinary `_modelAvailability.RequireAsync` | `AgentControlServiceIntegrationTests.Start_returns_409_model_disabled_when_the_agent_alias_is_held` | — | — |
| PC-132 | keep+cover (owner `b976e9ae`) | `QuotaHold` population | `A.Quota_hold_vetoes_stop` | as in the parent row | flip assertion |
| PC-133 | repoint (D-12) | ordinary `_quotaGate.EnforceAsync` | `AgentControlServiceIntegrationTests.Start_returns_409_subscription_quota_low_on_a_fresh_low_Codex_reading` | — | — |
| PC-134 | keep+cover (owner `b976e9ae`) | `AuthenticationRefusal` population | `A.Authentication_refusal_vetoes_stop` | as in the parent row | flip assertion |
| PC-135 | repoint | a refused launch is a Failed session | → PC-89 | — | — |
| PC-136 | keep+cover (owner `b976e9ae`) | `CapacityHold` via `HasTerminalCapacityHoldAsync` | `A.Capacity_hold_vetoes_stop` | as in the parent row | flip assertion |
| PC-137 | repoint | no capacity check on the explicit-resume arm; stop side and mutual exclusion cover it | → PC-136 + PC-84 | — | — |
| PC-138 | keep+cover | `ContinuityHold` from `ContinuityHeldAt` (seat or owner) | `A.Continuity_hold_vetoes_stop` | hard-code `false` | flip assertion |
| PC-139 | repoint (D-13) | explicit `ResumeSessionId` bypasses the continuity hold | → PC-138 + PC-89 | — | — |
| PC-140 | keep+cover | `HerdrHold` from `HerdrFailureHeldAt` | `A.Herdr_hold_vetoes_stop` | hard-code `false` | flip assertion |
| PC-141 | repoint (D-12) | ordinary Herdr hold at start + `StartRefusalAsync(automatic)` | `HerdrAlwaysOnChannelParityTests.SupervisionHold.Named_AlwaysOn_herdr_agent_holds_after_three_failures_keeps_its_timeout_shell_and_resumes_only_on_explicit_retry` + PC-152 | — | — |
| PC-142 | keep+cover | `LivenessHold` from `LivenessLatchedAt` | `A.Liveness_hold_vetoes_stop` | hard-code `false` | flip assertion |
| PC-143 | repoint (D-12) | `StartRefusalAsync(automatic)` liveness refusal | → PC-152 | — | — |
| PC-144 | repoint (pure layer) | `StandingQueueSwitchPolicy.NeverAttempted` | new Unit class `StandingQueueSwitchPolicyTests` (alias V): `V.DeliveryAttempts_alone_is_attempted` | delete that clause | `NeverAttempted(row).ShouldBeFalse()` |
| PC-145 | repoint (pure layer) | same | `V.LastDeliveryStartedAt_alone_is_attempted` | delete that clause | same |
| PC-146 | repoint (pure layer) | same | `V.DeliveryVerdict_alone_is_attempted` | delete that clause | same |
| PC-147 | repoint (pure layer) | same | `V.DeliveryVerdictAt_alone_is_attempted` | delete that clause | same |
| PC-148 | repoint (pure layer) | same | `V.SentAt_alone_is_attempted` | delete that clause | same |
| PC-149 | repoint (pure layer) | same | `V.CanceledAt_alone_is_attempted` | delete that clause | same |
| PC-150 | repoint (pure layer) | same | `V.ChannelReplySettledAt_alone_is_attempted` | delete that clause | same |
| PC-151 | keep+cover (D-21) | `InteractiveHumanTurn` from `Ui`/`Channel`/`Scheduled` pending rows | `A.Unrelated_pending_input_vetoes_stop` (one argument per origin) | hard-code `false` | flip assertion |
| PC-152 | keep+cover (re-specified, D-12) | `ResumeAsync` passes `automatic: true` | `A.Scope_change_after_reservation_revokes_resume` on `BuildHarness`: physical seat `LivenessLatchedAt` set after the stop | pass `automatic: false` | `result.Accepted.ShouldBeFalse(); result.Outcome.ShouldBe("specialist_start_refused"); launches.ShouldBe(0)` |
| PC-153 | keep+cover (re-seamed) | `BeginStopAsync` session/generation supersede | `D.Pointer_change_before_stop_supersedes_the_episode`: seed Confirmed, repoint `PersistentSessionId` to a new session row | delete the `SessionId` half of the comparison | `Stops.ShouldBe(0); state.ShouldBe(SupersededByOperator); newSession untouched` |
| PC-154 | repoint | ownership is a scope clause rebuilt pre-stop | → PC-38 + PC-32 | — | — |
| PC-157 | keep+cover (D-16) | adoption retries the fence without a launch | `A.Fence_repair_never_launches_a_second_generation`: fence insert fails on sweep 1, succeeds on sweep 2 | transition `NeedsDecision` on fence failure | `state.ShouldBe(AwaitingCheck); fenceRows.ShouldBe(1); Resumes.ShouldBe(0)` (seeded `ResumeReserved`) |
| PC-158 | keep+cover | receipt gate lives on `AgentSupervisionState` | `B.Pruned_terminal_episode_does_not_reset_receipt_gate` (terminal row pruned at 91 d, `ReceiptEligible = false`) | treat absence of prior rows as eligible | `Stops.ShouldBe(0); episode.Reason.ShouldBe("budget")` |
| PC-159 | keep+cover | `Text.Contains(note.Body)` | `H.Truncated_caller_note_is_not_receipt` | replace with marker containment | `state.ShouldBe(AwaitingCheck)` |
| PC-160 | keep+cover | `note` must exist as `Sent` with a body | `H.Superseded_check_without_note_never_recovers_seat` | skip the note lookup when the Check succeeded | `state.ShouldBe(AwaitingCheck); CallerReceiptNotificationId.ShouldBeNull()` |
| PC-162 | keep+cover | `PruneSessionsAsync` referencer | `B.Unresolved_recovery_retains_session_evidence` | remove that referencer only | `protected.ShouldBe(original); unrelated.ShouldBeEmpty()` |
| PC-163 | keep+cover | `PruneTranscriptsAsync` referencer | `B.Unresolved_recovery_retains_transcript_evidence` | same | same |
| PC-164 | keep+cover | `PruneTasksAsync` referencer | `B.Unresolved_recovery_retains_task_tree_evidence` | same | same |
| PC-165 | keep+cover | `PruneQueuedMessagesAsync` referencer | `B.Unresolved_recovery_retains_queue_evidence` | same | same |
| PC-166 | repoint (D-18) | `FinishStopAsync` termination stamp | PC-4's method; add `supervision.Suspended.ShouldBeFalse()` | stamp `OperatorRequest` | existing `TerminationSource` assertion |
| PC-167 | keep+cover | `Evidence()` clip to `EvidenceMaxLength` | `N.Automatic_audit_contains_only_bounded_metadata` (5,000-char owning prompt) | store the prompt text in evidence | `EvidenceJson.Length.ShouldBeLessThanOrEqualTo(1000); ShouldNotContain(sentinel)` |
| PC-168 | keep+cover | cleanup filters by compaction failure codes | `F.Terminal_non_check_brief_survives_compaction_cleanup` | replace the code predicate with `Status == Failed` | `nonCheckRow.Status.ShouldBe(Pending)` |
| PC-169 | keep+cover | cleanup matches `Body.Contains(token)` | `F.Unrelated_input_survives_compaction_cleanup` | drop the token filter | `unrelatedRow.ShouldBe(snapshot)` |

Totals: keep+cover 90 (10 owned by `b976e9ae`, 80 in the follow-up), repoint 33
(7 of them into the new Unit class, 3 rename-only, 2 shared-method, 21 onto
existing methods or other PCs), retire 4 (PC-75, PC-87, PC-96, PC-112). Effective
guards 217, mapped 213 executable cycles + 4 retired with a decision each,
missing mappings 0.

### Amended alias table

Additions and re-bindings only; the parent alias table stands otherwise.

| Alias | Exact class | File |
|---|---|---|
| V | `StandingQueueSwitchPolicyTests` (Unit) | `tests/Antiphon.Tests/Application/StandingQueueSwitchPolicyTests.cs` (new) |
| A (partials) | `CheckCompactionAutomaticRestartTests` | `.Scope.cs` (D-11 flips), `.Stop.cs` (PC-21/22/23/31/32/73/74/76/91), `.Resume.cs` (PC-28/33/88/89/152/157 on `AgentControlServiceIntegrationTests.BuildHarness` or the coordinator with the counting resume as each row says), `.Supervisor.cs` (PC-25/83/84 on the `AgentSupervisionTests` harness) |
| F (partials) | `CheckCompactionRecoveryFlowTests` | `.Occupancy.cs` (coordinator rows), `.Admission.cs` (PC-9/10/97/99 on the runner/request/dispatcher harnesses) |
| H (partial) | `CheckNoteDeliveryHandoffTests` | `.Recovery.cs` (PC-113–117/159/160 drive `TryReceiptAsync` on a seeded AwaitingCheck episode) |

### Production changes in the follow-up

1. `CheckCompactionContinuationService.TryReceiptAsync`: `!string.IsNullOrWhiteSpace(t.Result)` (PC-114). The legacy path already does this.
2. `CheckCompactionContinuationService.AdoptLaunchAsync` (D-16): require `!IsWorkingAsync(resumed)` before `AwaitingCheck`; when Running on the reserved generation and still Working, write the restart boundary through a shared internal helper extracted from `AgentSessionService.WriteRestartBoundaryIfInterruptedAsync` (same row shape, same synthetic uuid rule), stay `ResumeReserved` on failure, retry on the next sweep, never launch again (PC-33/157).
3. `FakeSessionRunnerClient.ObserveOverride` (test helper, PC-5).
4. New Unit class `StandingQueueSwitchPolicyTests` (PC-144–150).
5. Renames: PC-20, PC-78, PC-125 (with the 91-day open row) and the PC-166 assertion on PC-4's method.

No change to `Refusal`, the policy, the runner, the wire client or the H-5 path.
Anything else needs its own stated reason in the Code report.

### Slices

Each slice is one commit set on distinct files; S-2 and S-6 both edit the
coordinator and run in sequence, the rest may run in parallel worktrees.

| Slice | Rows | Files | New methods |
|---|---|---|---|
| S-1 | PC-15/34/35/36/37/39/42/43/44/45/46/138/140/142/151 flips; PC-21/22/23/31/32/73/74/76/91 | `CheckCompactionAutomaticRestartTests.Scope.cs`, `.Stop.cs`; a shared `SeedEligibleAsync(Action<SeatOptions>)` helper in `CheckCompactionFixture` | 24 |
| S-2 | PC-28/88/152 (real harness), PC-33/157/89 (coordinator, D-16), PC-25/83/84 (supervisor) | `.Resume.cs`, `.Supervisor.cs`, `CheckCompactionContinuationService.cs`, `AgentSessionService.cs` (helper extraction only) | 9 |
| S-3 | PC-5/6/63/128/129/153; PC-30/107/108/109/110/111/167 | `CheckCompactionContinuationTests.cs`, `CheckCompactionAttentionTests.cs`, `FakeSessionRunnerClient.cs` | 13 |
| S-4 | PC-8/12/29/93/94/95/101/104/105/118/168/169; PC-9/10/97/99; PC-144–150 | `CheckCompactionRecoveryFlowTests.Occupancy.cs`, `.Admission.cs`, `StandingQueueSwitchPolicyTests.cs` | 23 |
| S-5 | PC-26/27/80/81/82/126/158/162/163/164/165; PC-78/125 renames | `CheckCompactionRecoveryPersistenceTests.cs` | 11 (+2 renames) |
| S-6 | PC-113/114/115/116/117/159/160; PC-20 rename; PC-166 assertion | `CheckNoteDeliveryHandoffTests.Recovery.cs`, `CheckCompactionAutomaticRestartTests.cs`, `CheckCompactionContinuationTests.cs`, `CheckCompactionContinuationService.cs` | 7 (+1 rename) |

Every new method must be shown red against its own row's defect before it is
committed green (a test that cannot go red is a stub, per the manifest rule);
the Code report lists the red run per method. Do not touch CP-5's
`Reservation_and_real_queue_flush_serialize_in_both_orders(True)` timeout; it is
inherited (reproduced at base `3c7a4057` by Review `3986b2f9`).

### Verification design

Verification for this amendment is folded into this dispatch: the rows above
carry the fixture, defect and red assertion for every keep+cover row, and the
repoint targets are named methods that exist at `abc7b90d`. The parent
Verification design and the H-5 amendment's V/R lanes stand; the follow-up Code
runs the Checkpoints below as its closed manifest, not the parent's CP-1–12
(which the sibling task runs for D-1/D-3/PC-13).

#### Checkpoints

One isolated build and one exact filter per row, reusing the task's own
`bin-c79/` output between rows, rebuilt at every checkpoint, application project
rows sequential. Selector text is literal; the Markdown backslashes before pipe
characters are not part of the filter. Record commit, filter, duration and
pass/fail/skip counts per row; check the fresh TRX names every new method.

| CP | Project under tests / build | Exact test selector | Coverage | V/R minutes, estimated |
|---|---|---|---|---:|
| CP-13 | `Antiphon.Tests`, dotnet build | `/*/*/*/*[Category=Unit]` | V (PC-144–150), P/C regression | 2.00 |
| CP-14 | `Antiphon.Tests`, dotnet build | `/*/*/(CheckCompactionContinuationTests*)\|(CheckCompactionAutomaticRestartTests*)\|(CheckCompactionRecoveryPersistenceTests*)/*` | S-1/S-2/S-3 D rows/S-5; PC-4 shared rows | 14.00 |
| CP-15 | `Antiphon.Tests`, dotnet build | `/*/*/(CheckCompactionRecoveryFlowTests*)\|(AgentTaskDeliveryWatchdogTests*)\|(AgentTaskStandingAgentDispatchTests*)\|(SessionMessageQueueWedgedHeadTests*)/*` | S-4; PC-14/92 targets | 13.00 |
| CP-16 | `Antiphon.Tests`, dotnet build | `/*/*/(CheckCompactionAttentionTests*)\|(SpecialistHealthAttentionTests*)\|(DataRetentionServiceTests*)/*` | S-3 N rows, PC-111 harness | 10.00 |
| CP-17 | `Antiphon.Tests`, dotnet build | `/*/*/(CheckNoteDeliveryHandoffTests*)\|(ReceiptFailureDeliveryTests*)/*` | S-6; PC-121/122 targets | 38.00 |
| CP-18 | `Antiphon.Tests`, dotnet build | `/*/*/(AgentSupervisionTests*)\|(SpecialistStartIntentTests*)\|(StandingSessionSwitchConcurrencyTests*)\|(StandingSessionQueueSwitchTests*)\|(StandingSessionRecoveryHttpTests*)\|(AgentControlServiceIntegrationTests*)/*` | S-2 harness regression; PC-72/85/86/131/133 targets; one inherited red expected | 11.00 |
| CP-19 | `Antiphon.Tests`, dotnet build | `/*/*/CheckCompactionCrashTests/*` | D-16 adoption change regression (PC-24/106/156) | 33.00 |
| CP-20 | `Antiphon.Tests`, dotnet build | `/*/*/(AgentTaskCheckInterpreterTests*)\|(SpecialistQualificationTests*)\|(AgentTaskLandNotificationRecoveryTests*)\|(HerdrAlwaysOnChannelParityTests*)/*` | PC-9/10 harness reuse; PC-120/141 targets | 12.00 |
| CP-21 | `Antiphon.Tests`, dotnet build | `/*/*/(AgentSessionRuntimeTests*)\|(CompactionContinuationWireTests*)/*` | PC-90 target; wire regression after the sibling's D-3 lands | 6.00 |

Excluded with reason: runner and Pty rows (CP-9/CP-10) and the client row
(CP-12) — no runner, fixture-binary or client file changes in any slice. If a
slice touches one, add that parent row back with the reason in the report.

#### Cost

| Component | Estimate |
|---|---:|
| Authoring, 80 integration + 7 unit methods, 3 renames, 2 production edits | 14–20 h across the six slices (band, not a promise) |
| One full manifest run, CP-13–21 | 139 min |
| Per-slice targeted reruns after fixes | the slice's rows only |
| Mutation cycles after land (unchanged per-cycle costs from the parent Cost) | 213 cycles; 4 retired |

### Sibling reconciliation

Code task `b976e9ae` is landing D-1 population, D-3 wire tests and PC-13 on the
same lineage. Its Review must: confirm the six D-1 rows and three W rows each
have a red-capable method; bind whatever names it used into the parent alias
table by rename only; and confirm PC-13's method drives the real
producer-to-recipient chain. If it lands first, S-1 rebases onto it and reuses
its seeding helper for the flips.

