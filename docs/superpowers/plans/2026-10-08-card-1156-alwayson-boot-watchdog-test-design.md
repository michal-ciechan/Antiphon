# CARD-1156 test design

TestDesign for `docs/superpowers/plans/2026-10-08-card-1156-alwayson-boot-watchdog-plan.md`
(plan commit `877b9829461d8572ff4d1d7cb264f2ef7a463fe1` on `feat/card-task-82ca0355`; the plan's
source baseline `2a0f8407486f37ffb3588c221c42c77d7d1fbb70` is still `origin/master` on this
dispatch, and every production file the plan cites is byte-identical between the two). This file
is the checkpoint `--plan` input: the tool reads the first `### Checkpoints` table below.
Production code was not edited. Baseline for every citation: master `2a0f84074`.

**Operator decisions applied (orchestrator, consistent with AGENTS.md "CARD-0079 is the only
automatic stop of a Working session" and the CARD-1151 option B):** the plan's **option A**
(detection only: Warning at 8 minutes and operator attention at 20 minutes by default, no stop),
and **D-2 interpreted as "unknown keeps the session"**: when the watchdog cannot positively
prove a condition it must not stop. The brief wording "anything unknown keeps today's behaviour"
does not apply where today's behaviour is the unsafe stop. Every test below pins the fail-closed
rule the brief states: a stop never happens on this path for a Working session;
unknown/missing/null evidence yields no stop and an attention row at the normal threshold;
telemetry best-effort never changes an outcome; nothing else changes for task-bound sessions.

Committed beside this note are compiling skeletons that fix the class, method and argument
rosters named below (the `Card1151Pending` shape). Every skeleton body throws TUnit's
`SkipTestException` with its slice tag, so it is discovered, counted as skipped, never green,
and cannot be mistaken for proof. Code replaces each body in the slice that lands the behaviour.

| Skeleton | Slice |
|---|---|
| `tests/Antiphon.Tests/TestHelpers/Card1156Pending.cs` (helper) | all |
| `tests/Antiphon.Tests/Application/StandingBootWatchPolicyTests.cs` (V-3, V-4) | S2 |
| `tests/Antiphon.Tests/Application/StandingBootWatchdogTests.cs` (V-2, V-5, V-6, V-7, V-12) | S1-S3, S5 |
| `tests/Antiphon.Tests/Application/StandingBootAttentionTests.cs` (V-8, V-9, V-10) | S4 |
| `tests/Antiphon.Tests/Application/StandingBootStatementBudgetTests.cs` (V-11) | S5 |
| `tests/Antiphon.Tests/Application/StandingBootDocumentationTests.cs` (V-13) | S6 |

The four assertion reversals in existing tests (below) are Code's work in S1 and S6; they are
not touched here, so `BootReplyWatchdogTests` stays green at this commit and still reproduces
the defect (the measurement probe below reproduced the kill: `Stopper.Killed = [sessionId]`,
`ConsecutiveFailures = 1`, on a Working, Running, taskless AlwaysOn session).

## Audit findings against the plan

- **F-1. The CARD-1151 S4 branch pins the stop this card removes.** `origin/feat/card-task-cc45ec44`
  (S4 repair, four commits ending `2c70219bf`) adds the sentence "A taskless AlwaysOn session is
  the exception: the boot reply watchdog still raises its incident and stops the session for the
  existing standing-agent restart ladder (`BootReplyWatchdogService`, unchanged by CARD-1151;
  CARD-1156)." to `docs/session-runtime-invariants.md` and `docs/agent-kinds.md`, and pins it as
  `BootStallDocumentationTests.AlwaysOnExceptionSentence` (`:68-71`, asserted at `:142-143`). S6
  must replace that sentence in both owners and that constant and its two assertions; otherwise
  CP-29 is red the moment S6 lands. This is a fourth assertion reversal the plan does not list.
- **F-2. Both CARD-1151 branches edit three CARD-1156 files.** `cc45ec44` and
  `origin/feat/card-task-739f831c` (S5, shares the first two S4 commits, lacks the two S4-repair
  commits) both change `BootReplyWatchdogService.cs` (`:172-179` and `:294-298`, comments only),
  `AttentionService.cs` (`+98`: the 8b boot-row arm at `:1154-1166`, the `BootStallItem` method
  appended after `:3157`, `WithCheck`), `BootReplyWatchdogTests.cs` (`:205-207`, a comment),
  `DelegationSettings.cs` (comments `:411-480`), `AttentionDtos.cs`, `BootStallPolicy.cs` and the
  three owner documents. S1, S4 and S6 therefore start only after both land; the landed
  coordinates shift by +13 inside `AttentionService.cs` (the boot helper moves to `:2213-2297`).
- **F-3. The CARD-1149/1150 S2 repair and CARD-1121 do not overlap this card's files.**
  `origin/feat/card-task-31f67fe1` (twenty commits) edits `AgentSessionService.cs`,
  `AgentTaskDispatcher.cs`, `SessionMessageQueueService*.cs`, `DispatchBriefEvidence.cs` and
  `DelegationDispatchRecoveryBoundaryTests.Brief*.cs`; `C1149_C1150_Statement_budgets` still has
  three arguments (18/18/4) there, so CP-26 is unchanged by it. CARD-1121 S1
  (`origin/feat/card-task-56c39ac6`, `ed952d1c`) edits `AgentSessionRuntime.cs` (`+52`, a shared
  catch-up core) and adds `LandReceiptScanCache.cs`; S2 plans `AgentTaskLandNotificationService.cs`
  and a `Program.cs` registration. CARD-1156 adds no `Program.cs` registration (its policy, writer
  and observation are internal classes the singleton sweep constructs, the `BootStallWarningWriter`
  shape) and reads `CatchUpTranscriptAsync(Guid, CancellationToken)` without changing it, so the
  only dependency is behavioural: V-7 `reply-during-pull` and V-12 `pull-fault` rely on that
  method persisting the pulled entries and swallowing non-cancellation faults
  (`AgentSessionRuntime.cs:706-720`). If CARD-1121 lands first, Code re-reads those lines.
  `SessionReconciliationService.cs` is edited by no in-flight branch and by no CARD-1156 slice.
- **F-4. Measured cost of today's paths (probe, this mirror, isolated PostgreSQL).** Cheap paths
  are exactly 0/1/2/2 as the plan states. Today's cold raise on an armed AlwaysOn session is 12
  commands plus one runner pull (11 without a runtime); the repeat tick after a raise is 11 plus a
  pull (the raise cleared the watch, so every later tick re-arms: six commands of self-heal);
  an unarmed overdue session costs 18. The current boot attention helper inside `GetAsync` costs
  one fixed command plus two fixed and one per episode once any `bootSeq=` receipt exists
  (42 -> 45 -> 46 total for 0/1/2 receipts); the current prune is 2 commands, 3 with one over-cap
  agent. Rosters are in "Statement budgets".
- **F-5. The plan's one-flip table has no unknown rows for its nullable fields.** D-3 says an
  unknown required read declines emission; the plan's V-3 names twenty flips that are all
  "false" shapes except `generation-unknown`. Four `*-unknown` rows are added (provider, owner,
  task, reply) so a policy that reads a null as "not blocked" goes red. V-3 is 25.
- **F-6. Working is not an admission input, and nothing in the plan's controls mutates that.** A
  defect that adds `Working == false` as an admission condition would silently lose every
  Warning for the real-world shape (a prompt-only session is Working, measured
  `TranscriptWorkingStateQuery` true in the probe). PC-19 is added.
- **F-7. The watch must stay armed after a notification, and the plan's controls do not pin it.**
  Today's disarm-after-raise is what makes every later tick re-arm (F-4); the plan says "keep a
  valid unresolved standing watch armed". PC-18 is added; its red is the cost pin (V-11
  `same-recorded-episode`, 3 versus 9) and V-1's third witness.
- **F-8. Regression floors verified at this baseline.** `BootReplyWatchTests` 27 (1+5+7+14),
  `BootReplyWatchdogTests` 11, `BootLivenessProbeScopeTests` 8, `AgentSupervisionTests` names
  all three methods, `CheckCompactionContinuationTests` 2, `CheckCompactionRecoveryFlowTests` 1,
  `SessionHealthTests.No_probe_prompts_are_ever_sent_to_an_idle_session` 1, the three
  `AttentionServiceTests` boot methods at `:1333`, `:1355`, `:1375`;
  `BootStallAttentionTests.C1151_Attention_describes_detection_and_resolution` 5 and
  `BootStallDetectionTests.C1151_Detection_does_not_release_or_park` 2 on the S4/S5 branches
  (pending bodies at master; CP-16 and CP-25 require the landed bodies).

## Reconciliation with landing work

Observed on `origin` at 16:20 UTC. The rule: a slice may touch a file only after every branch
listed for it has landed, and it re-reads coordinates on its own baseline.

| In-flight work | Files it changes that CARD-1156 also changes | CARD-1156 rule |
|---|---|---|
| CARD-1151 S4 repair, `feat/card-task-cc45ec44` (`3cc5e04b8`, `c616578e3`, `6039b7c8b`, `2c70219bf`) | `BootReplyWatchdogService.cs` (`:172-179`, `:294-298` comments), `AttentionService.cs` (`:1154-1166` arm, `BootStallItem` after `:3157`, `WithCheck`), `BootReplyWatchdogTests.cs` (`:205-207`), `DelegationSettings.cs` (`:411-480`), `docs/session-runtime-invariants.md` (`:611-640`), `docs/orchestration-loop.md` (`:1090-1105`), `docs/agent-kinds.md` (`:469-475`), `BootStallDocumentationTests.cs` (whole, including `AlwaysOnExceptionSentence`) | S1, S4 and S6 start after it lands. S1 rewrites the two watchdog comments with the new policy; S6 replaces the AlwaysOn exception sentence in both owners and the test constant |
| CARD-1151 S5, `feat/card-task-739f831c` (`3cc5e04b8`, `c616578e3`, `bb847e020`, `662fed755`) | The same S4 production hunks (without the repair) plus `BootStallDetectionTests.cs`, `DelegationDispatchRecoveryBoundaryTests.BootStall.cs`, `BootStallWorld.cs` (`Interceptors`) | No CARD-1156 slice edits those test files; CP-16, CP-25 and CP-29 run only once the real bodies are on master. S1 waits for it as well because its two watchdog comment hunks are the same lines |
| CARD-1149/1150 S2 repair 5, `feat/card-task-31f67fe1` (twenty commits to `acab4c82e`) | None. `AgentSessionService.cs`, `AgentTaskDispatcher.cs`, `SessionMessageQueueService*.cs`, `DispatchBriefEvidence.cs`, `Brief*.cs` partials | Read-only dependencies. No wait. CP-26 runs whatever `C1149_C1150_Statement_budgets` arguments are on master at Code's baseline (three today, 18/18/4) |
| CARD-1121 S1 (`feat/card-task-56c39ac6`, `ed952d1c`) and S2 (planned) | None. `AgentSessionRuntime.cs` catch-up core, `LandReceiptScanCache.cs`, later `AgentTaskLandNotificationService.cs`, `Program.cs` | Behavioural dependency on `CatchUpTranscriptAsync` only (F-3). No wait; Code re-reads `:706-720` on its baseline |
| `SessionReconciliationService.cs`, `AgentTaskDispatcher*.cs` | Named in the brief as collision files | Edited by no CARD-1156 slice. The task exclusion stays a read (`AgentTasks` status set), never a dispatcher hook |

Slice order that avoids collisions: **S1-S3 as one Code task after CARD-1151 S4 and S5 are on
master**; then **S4** (after S1-S3 land); then **S5**; then **S6** (it edits the three owner
documents and `BootStallDocumentationTests.cs`, which are S4-repair files). S2's new files and
its additive `BootReplyWatch.cs` overload collide with nothing in flight. Files and hunks each
slice may touch, at the landed coordinates (master `2a0f84074` plus the `cc45ec44` deltas):

- S1: `server/Application/Services/BootReplyWatchdogService.cs` only: `EvaluateAsync`
  (`:103-191`; the S4 comment at `:172-179`), `RaiseAsync` (`:198-315`; the S4 comment at
  `:294-298`), delete `StopHungStandingSessionAsync` (`:323-339`) and `MaxProbeDrivenRestarts`
  (`:346`), keep `EpisodeKey` (`:348`; `AttentionServiceTests` calls it), keep
  `LoadFullnessAsync`, `ComposerHead` and `Describe` for the non-AlwaysOn legacy path. Tests:
  `tests/Antiphon.Tests/Application/BootReplyWatchdogTests.cs` (the three reversals), new
  `tests/Antiphon.Tests/TestHelpers/StandingBootWatchFixture.cs`, V-2 body.
- S2: new `StandingBootWatchPolicy.cs`, `StandingBootWatchObservation.cs`; one additive overload
  in `BootReplyWatch.cs` after `:218` (`LoadBootTurnAsync(AppDbContext, AgentSession, DateTime, CancellationToken)`:
  the same predicate over a loaded row, skipping the `GrokRulesState` round trip). `TryArmAsync`,
  `LoadBootTurnAsync(db, id, clock)` and `BootTurn` are not changed. V-3, V-4 bodies.
- S3: new `StandingBootWarningWriter.cs`; `BootReplyWatchdogService.cs` wiring (the same hunks as
  S1). `tests/Antiphon.Tests/TestHelpers/ListedInventoryRunner.cs` gains an optional
  `Func<Guid, SessionRunnerTranscriptDto>? Transcript` used by `GetTranscriptAsync` (V-7
  `reply-during-pull` through the real runtime). V-5, V-6, V-7 bodies.
- S4: `server/Application/Services/AttentionService.cs`: the `BuildBootReplyMissingItemsAsync`
  call at `:232` and the method (`:2200-2284` at master, `:2213-2297` after `cc45ec44`); new
  `StandingBootAttentionProjection.cs`. `server/Application/Services/AgentSupervisorService.cs`
  `PruneIncidentsAsync` (`:582-604`) only; no other supervisor hunk. V-8, V-9, V-10 bodies.
- S5: `StandingBootStatementBudgetTests.cs` and the V-12 body in `StandingBootWatchdogTests.cs`;
  fixture additions only.
- S6: `docs/session-runtime-invariants.md` (the CARD-0312 paragraph at `:611-618` and the
  CARD-1151 bullet's last sentence), `docs/orchestration-loop.md` (`:1090-1105`),
  `docs/agent-kinds.md` (`:469-475`), `tests/Antiphon.Tests/Application/BootStallDocumentationTests.cs`
  (`:67-71`, `:142-143`), watchdog comments if any remain, V-13 body. AGENTS.md's CARD-0079
  sentence is unchanged under option A.

## Assertion reversals

Every existing test that encodes today's stop, latch or disarm, with its flip. No assertion is
weakened or deleted silently: each old assertion is replaced by its explicit negation or by a
stronger positive, under a comment naming CARD-1156 and the operator's option-A decision.

1. **`BootReplyWatchdogTests.a_standing_agent_goes_through_the_existing_restart_ladder`
   (`:67-87`) is the existing test whose one-stop assertion must be reversed.** Its decisive line
   is `scenario.Stopper.Killed.ShouldBe([scenario.SessionId], "the hung Running session must be
   stopped so the supervisor's not-running branch fires")`, and its comment states the premise
   ("the raise must also STOP the hung session") that D-1 retires. Rename to
   `a_standing_boot_stall_is_detected_without_stopping_or_driving_the_restart_ladder`. Flips:
   `Killed` `[SessionId]` to empty; `state.ConsecutiveFailures.ShouldBe(1)` to "no
   `AgentSupervisionStates` row was created" (`SingleOrDefaultAsync` null); added: the session row
   is still Running with `TerminationSource` default and `EndedAt` null, one receipt whose
   `FailureReason` starts with `standingBoot:v1;`. This is a deliberate behaviour change, not a
   weakened assertion: the old line pinned the CARD-0312 S4 intervention contract, the operator
   replaced that contract with the AGENTS.md sole-exception rule, and the replacement asserts a
   stronger positive (custody unchanged and the receipt present) plus the explicit negation.
2. `the_third_consecutive_failure_latches_the_mechanism_off_instead_of_restarting` (`:89-111`)
   becomes `boot_silence_preserves_existing_failure_history_without_creating_a_latch`. Keeps the
   seed `consecutiveFailures: 2` and the 30-minute prompt. Flips: `LivenessLatchedAt.ShouldNotBeNull()`
   to `ShouldBeNull()`; `ConsecutiveFailures.ShouldBe(2)` is kept (unchanged history); `Severity`
   Error is kept but for the new reason (past the 20-minute operator due); `Message.ShouldContain("stopped restarting")`
   to `ShouldNotContain("stopped restarting")` plus `ShouldContain("Detection only")`; `Killed`
   empty is kept.
3. `a_delivered_prompt_the_model_never_answers_raises_one_incident_naming_what_was_seen`
   (`:37-64`) becomes `an_unanswered_standing_boot_raises_once_and_keeps_its_watch_for_escalation`.
   Keeps the 30-minute seed and the second-tick `ShouldBe(0)`. Flips:
   `FailureReason.ShouldStartWith("bootSeq=")` to `ShouldStartWith("standingBoot:v1;g=")`;
   `Severity.ShouldBe(Warning)` to `Error` (first observation after operator due is Error only);
   `session.BootReplyDueAt.ShouldBeNull()` to `ShouldNotBeNull()` and `BootPromptSequence`
   unchanged (the watch stays armed); `Message.ShouldContain("Boot prompt confirmed at sequence")`
   to `ShouldContain("Boot prompt at sequence")`; `ShouldContain("no assistant, thinking, tool or turn-end row")`
   is kept; added `ShouldNotContain("composer holds")`.
4. `BootStallDocumentationTests.AlwaysOnExceptionSentence` (`cc45ec44` `:68-71`) and its two
   `ShouldContain` assertions (`:142-143`) are replaced by `StandingBootDocumentationTests.DetectionSentence`
   (below) in both owners; the CARD-1151 test keeps asserting that the open-delegate promise is
   scoped (`DelegateScopedSentence`) and that the agent-kinds note no longer says the deadline
   ends a hung call. The 1151 design wrote the old sentence as a Review F1 correction that names
   CARD-1156 as its follow-up, so this is the follow-up, not a weakening.

The other eight `BootReplyWatchdogTests` contracts are retained unchanged: `a_zero_deadline_disables_the_sweep_entirely`
(0), `a_slow_but_alive_boot_that_answers_before_the_deadline_is_never_touched` (0, still armed),
`an_answered_boot_turn_disarms_cleanly_and_raises_nothing` (0, cleared),
`a_session_with_no_transcript_ground_truth_is_neither_armed_nor_judged` (0),
`a_session_that_has_already_answered_on_this_launch_is_never_armed_at_all` (0),
`a_redraw_with_no_model_row_is_still_overdue` (1), `a_session_owned_by_an_open_delegate_task_is_left_to_the_deadline_sweep`
(0; a Dispatched owner still excludes), `an_unarmed_live_session_is_re_derived_rather_than_left_unwatched` (1).

## Design details the tests pin (Code reads these as the contract)

- **Episode key.** `standingBoot:v1;g=<SessionGeneration.Normalize(StartedAt).Ticks>;l=<launchClock.Ticks>;p=<promptSequence>;stage=<detected|operator>`
  (`SessionGeneration.Normalize` is `src/Antiphon.SessionRunner.Contracts/SessionGeneration.cs:15`;
  `launchClock` is `BootReplyWatch.LaunchClock`). The dedup prefix is everything before
  `stage=`. The legacy `bootSeq=<n>` parser in `AttentionService` is preserved for old rows.
- **Receipt row.** `AgentIncident` with `Kind = LivenessProbeFailed`, `AgentId = owner`,
  `SessionId = session`, `Severity = Warning` for `stage=detected`, `Error` for `stage=operator`,
  `FailureReason = key`, `Message = "Boot prompt at sequence {p} ({UserPrompt|queued prompt record}); no assistant, thinking, tool or turn-end row in {age}; boot notice due {bootDue:u}; operator decision due {operatorDue:u}. Detection only: the session keeps its seat; nothing was stopped, restarted, typed or latched."`
  For a `QueuedUserPrompt` the first clause reads `queued prompt record at sequence {p}; no reply observed`. Never composer text, prompt text or a runner error.
- **Clocks.** `bootDue = promptAt + BootModelWaitDeadlineMinutes` (only when `> 0`);
  `operatorDue = promptAt + max(bootWait, ModelWaitDeadlineMinutes > 0 ? ModelWaitDeadlineMinutes : 20 minutes)`,
  the `BootStallPolicy.OperatorWait` rule. Equality is due. `DueStage` returns the highest due
  stage; a recorded `operator` receipt covers a later `detected` decision.
- **Observation (S2).** `StandingBootWatchObservation` record, every nullable field unknown when
  null: `BootWaitMinutes`, `ProviderVerified: bool?`, `GrokRules: GrokRulesState?`,
  `SessionLive: bool?` (null missing, false terminal status), `EndedAtNull: bool?`,
  `Generation: DateTime?`, `OwnerCount: int?`, `OwnerAlwaysOn: bool?`, `StandingAgentConflict: bool?`,
  `TaskOwner: StandingBootTaskOwner?` (`None`, `Queued`, `Dispatched`, `Working`, `Blocked`),
  `PromptSequence: long?`, `PromptAt: DateTime?`, `PromptKind`, `ReplyObserved: bool?`,
  `IdentityMatches: bool?`, `Now`. `Decide` admits only when every field is positively the admitted
  value; the result has exactly `None`, `Detected`, `NeedsOperator`; there is no stop, restart,
  latch or requeue member. Working is read by no one on this path.
- **Sweep (S1/S3), per live session, in order.** Unarmed: the existing cheap EXISTS and
  `TryArmAsync` self-heal (unchanged). `EvaluateSessionAsync` on stored rows; Answered disarms,
  Waiting returns. Overdue: (1) cheap recorded-key pre-check from the persisted columns (one
  `AgentIncidents` read by prefix); if the highest due stage is already recorded, return
  (no pull, no observation). (2) `CatchUpTranscriptAsync` when a runtime exists, then
  `EvaluateSessionAsync` again; Answered disarms and returns. (3) The observation: owner list
  (`Agents` by `PersistentSessionId`, selecting `Id`, `AlwaysOn`), task exclusion (`AgentTasks`
  by session in Queued/Dispatched/Working/Blocked), `LoadBootTurnAsync` over the loaded row
  (EXISTS plus prompt rows). A Dispatched/Working owner still stands down exactly as today; a
  Queued/Blocked owner now stands down too; a non-AlwaysOn owner takes the legacy diagnostic
  path (today's `RaiseAsync` minus the supervision writes and the stop, keeping `bootSeq=` and
  the disarm). (4) The writer, in its own context and transaction: session row
  `FOR UPDATE SKIP LOCKED` (missing or held declines this pass at Debug), owner and task re-read,
  dedup by prefix and stage, `LoadBootTurnAsync` over the locked row (same sequence, same
  `PromptAt`, no reply), insert, commit; then best-effort `PublishToAllAsync("AgentChanged")`.
  Any non-cancellation fault is logged at Warning and isolated; cancellation propagates. No RPC
  inside the transaction. The watch columns are not written on this path.
- **Projection (S4).** `StandingBootAttentionProjection` inside `GetAsync`: AlwaysOn agents
  with a persistent pointer (one read), their live sessions (one), open-task exclusion in bulk
  (one), current receipts in bulk by prefix (one), then per candidate the loaded-row
  `LoadBootTurnAsync` (two). One `LivenessProbeFailed` item per current unresolved episode:
  Warning from `bootDue`, Error from `operatorDue`, `OpenAgent` and `OpenDrawer` actions,
  evidence lines "Inspect the session or its transcript, then choose: keep waiting, reply
  through the session, or explicitly Stop and Start/resume the agent.", "Detection only: the
  session keeps running and keeps its seat; nothing is stopped, typed, restarted or latched
  automatically, and no deadline ends this episode.", prompt age, boot due, operator due. The
  legacy `bootSeq=` helper keeps running for sessions the projection does not cover, with the
  open-task exclusion applied to it too. No write, no runner call, no arm.
- **Prune (S4).** `PruneIncidentsAsync` computes the current unresolved episode prefixes (the
  same candidate reads, no receipts read) and excludes `FailureReason LIKE '<prefix>%'` rows
  from both the age delete and each cap delete. A fault while computing them excludes every
  `standingBoot:v1;` row from deletion this pass and logs at Warning.

## Statement budgets

Measured at this baseline by the diagnostic probe (below), `FullCommandCounter` on every
context including the runtime's, one isolated database per path, one AlwaysOn ClaudeCode
session, `BootModelWaitDeadlineMinutes` 8:

| Path (today) | Commands | Pulls | Roster |
|---|---:|---:|---|
| boot deadline disabled | 0 | 0 | none |
| no live sessions | 1 | 0 | live candidates |
| healthy, unarmed, already answered | 2 | 0 | candidates; model-reply EXISTS |
| armed, before deadline | 2 | 0 | candidates; rows past the prompt |
| cold raise, armed, AlwaysOn, runtime present | 12 | 1 | candidates; rows; rows (post-pull); task EXISTS; incident EXISTS; owner (Agents LIMIT 1); agent load; promptAt; fullness rows; kinds since; supervision state; one batched save (INSERT incident + UPDATE session + supervision) |
| the same without a runtime | 11 | 0 | as above without the post-pull rows |
| repeat tick after a raise, runtime present | 11 | 1 | candidates; EXISTS; session load; Grok-rules EXISTS; EXISTS; prompt rows; UPDATE (re-arm); rows; rows; task EXISTS; incident EXISTS |
| unarmed overdue (self-heal then raise), runtime present | 18 | 1 | the six re-arm commands then the twelve above |
| non-AlwaysOn owner, armed, runtime present | 11 | 1 | as the AlwaysOn raise without the supervision read |
| `AttentionService.GetAsync` with 0 / 1 / 2 `bootSeq=` receipts | 42 / 45 / 46 | 1 each (inherited list) | fixed incident read; then sessions, agents and one transcript read per episode |
| `PruneIncidentsAsync` with no over-cap agent / one | 2 / 3 | 0 | age DELETE; cap GROUP BY; one cap DELETE per over-cap agent |

Designed exact pins for the new paths (within the plan's caps; Code prints and asserts these
totals; a measured difference is a design revision to explain here, never a widened tolerance;
transaction begin/commit are not `DbCommand` executions and are not counted, matching the
CARD-1151 pins):

| V-11 path | Pin | Pulls | Enumerated roster |
|---|---:|---:|---|
| boot-deadline-disabled | 0 | 0 | unchanged early return |
| no-live-sessions | 1 | 0 | candidates |
| healthy-unarmed-answered | 2 | 0 | candidates; model-reply EXISTS (unchanged; PC-15 turns it into 3) |
| armed-before-deadline | 2 | 0 | candidates; rows (unchanged) |
| first-detected-stage (armed, runtime) | 15 | 1 | candidates; rows; recorded-key pre-check; [pull]; rows; owner list; task exclusion; EXISTS; prompt rows; writer: session FOR UPDATE SKIP LOCKED, owner, tasks, dedup, EXISTS, prompt rows, INSERT |
| same-recorded-episode | 3 | 0 | candidates; rows; recorded-key pre-check (today 11 plus a pull) |
| first-operator-stage | 15 | 1 | as first-detected; the pre-check finds only `stage=detected` |
| runtime-absent-detection | 14 | 0 | as first-detected without the pull and the second rows read |

Projection and prune (`C1156_Attention_and_pruning_statement_budgets`), measured as the
contiguous slice of `GetAsync`'s roster attributable to the projection by SQL shape, and as the
prune's delta over its inherited 2 commands: zero candidates 1 / 1 (the agents read, then early
return); one candidate 6 / 5 (agents, sessions, tasks, receipts, EXISTS, prompt rows; the prune
reads no receipts: agents, sessions, tasks, EXISTS, prompt rows); two candidates 8 / 7. All within
the plan's `2 + 4N` ceilings for N >= 1 (zero candidates is 1 <= 2). Zero writes and zero runner
calls beyond `GetAsync`'s inherited one list in every argument.

## Verification design

### Inspection

Bodies read for this design (baseline `2a0f84074` unless named):

- `BootReplyWatchdogService` whole (`:1-382`): `SweepAsync` (`:67-101`), `EvaluateAsync`
  (`:103-191`), `RaiseAsync` (`:198-315`), `StopHungStandingSessionAsync` (`:323-339`),
  `MaxProbeDrivenRestarts` (`:346`), `EpisodeKey` (`:348`), `LoadFullnessAsync`, `ComposerHead`;
  `BootReplyWatch` whole (`:1-330`): `IsModelReply`, `IsPromptRow`, `LaunchClock`,
  `HasModelReplySinceAsync` (`:86-99`), `Evaluate` (`:135-151`), `LoadBootTurnAsync` (`:171-218`),
  `TryArmAsync` (`:240-278`), `DisarmAsync`, `EvaluateSessionAsync` (`:299-311`), `BootTurn`.
- `AgentSupervisorHostedService` tick order (`:87-250`: supervisor `TickAsync`, prune every
  `PrunePeriod`, then the boot sweep every tick); `AgentSupervisorService` constructor (`:45-77`),
  `TickAsync` (`:80-120`), `SuperviseAsync` live-session branch (`:121-180`), `PruneIncidentsAsync`
  (`:582-604`); `AgentControlService.ClearSupervisionLatchAsync` by the R-3 test;
  `AgentSessionService.KillAsync`/`KillOnAsync` (`:1501-1640`: Stopping and `SessionTermination.Record`
  persisted before the runner kill, undelivered kill retained); `IDelegateSessionStopper`;
  `Program.cs:383` binding and `:734` singleton.
- `AttentionService` constructor (`:123-160`), `GetAsync` order (`:168-260`),
  `BuildBootReplyMissingItemsAsync` (`:2200-2284`); the `cc45ec44` diff of the same file.
- `SessionOwnerLookup.ResolveOwningAgentIdAsync` (`:33-51`); `SessionMessageQueueService.IsWorkingAsync`
  (`:5087-5092`); `TranscriptWorkingStateQuery` whole; `AgentSessionRuntime.CatchUpTranscriptAsync`
  (`:706-720`) and both constructors (`:56-100`); `SessionContextUsage.LoadFullnessAsync`
  (`:139-200`, one query); `AgentTaskLiveness.IsDeadSession` (`:60-64`); `ProviderContractCatalog`
  delivery verification (Supported for ClaudeCode `:43`, Grok `:110`, Codex `:175`; Unsupported
  for OpenCode `:242`, Raw `:299`); `GrokRulesState`; `AgentIncident`, `AgentSupervisionState`,
  `AgentSession` (`StandingAgentId :8`, `LaunchResumedAt :80`, watch columns `:91-100`), `Agent`
  fields; `AgentTaskStatus` values; `AttentionAction`; `SupervisionSettings` retention 30 / cap 500 /
  healthy reset 10; `DelegationSettings` boot 8 (`:452`), model wait 20 (`:415`).
- CARD-1151 precedents: `BootStallPolicy.cs` whole, `BootStallWarningWriter.cs` whole,
  `AgentTaskDispatcher.BootStall.cs` whole, `BootStallPolicyTests.cs` whole,
  `BootStallDetectionTests.cs` whole at master and the method/argument list on `739f831c`,
  `BootStallDocumentationTests.cs` on `cc45ec44` whole, the CARD-1151 plan and test design
  (Q-1 option B, R1-R3, F1-F2, checkpoint and PC format).
- Fixtures: `BootReplyWatchdogTests.Scenario` whole; `BootStallWorld.cs` whole (and its
  `739f831c` diff); `AttentionServiceTests.Scenario`, `ItemsForAsync`, `BuildService`;
  `AgentSupervisionTests.BuildHarness` (`:567-690`, takes `connectionString` and `configureDb`),
  `Harness.Supervisor()`, `CreateAlwaysOnAgentAsync`; `CheckCompactionContinuationTests` and
  `CheckCompactionRecoveryFlowTests` fixtures; `FullCommandCounter`, `RecordingSessionStopper`
  (`StopsSessionsIn`), `ListedInventoryRunner` (`TranscriptPulls`, `TranscriptFault`),
  `FakeSessionRunnerClient` (`KillCalls`, `ConditionalInputCalls`), `ScriptedSessionRunnerClient.SetTranscript`,
  `MockEventBus.ThrowOnceOnEvent`, `TestDbFixture.CreateIsolatedSchemaAsync` and
  `CreateDbContextOptions(cs, interceptors)`, `TestDbFixtureLifecycle` (Testcontainers
  `postgres:16-alpine`, template clone per isolated database), `Card1151Pending`.
- Owner documents: `docs/session-runtime-invariants.md:611-618`, `docs/orchestration-loop.md:1090-1105`,
  `docs/agent-kinds.md:469-475`, AGENTS.md CARD-0079 line (`:74`); `docs/testing-and-build.md`
  checkpoint manifest, runner tool, build slots, mutation execution, CARD-0222 clock rule.

Boundaries and where they are proved:

| Boundary | Where |
|---|---|
| A Working, Running, taskless AlwaysOn session keeps its seat, pointer and supervision state through sweeps at 9 and 21 minutes and real supervisor ticks; Warning then Error | V-2 |
| Every whitelist condition is individually required; unknown (null) is never admitted; refusal is never a recovery | V-3 |
| 8/20 defaults at exact boundaries, max rule, 20 fallback, highest stage only, no downgrade | V-4 |
| One receipt per key and stage across ticks, providers and concurrent sweeps; new prompt, generation and resume clock are new episodes; a legacy cleared watch self-heals once | V-5 |
| Telemetry faults never stop, never mutate supervision, never leak a row; cancellation propagates | V-6 |
| Evidence committed before the writer's final read prevents the insert; contention is quiet and bounded | V-7 |
| Attention is derived from current facts, survives missing, failed, old and pruned receipts, merges the legacy row | V-8 |
| Positive resolution (five model kinds, terminal, replacement) clears only the current episode | V-9 |
| Real pruning retains active receipts under age and cap pressure and under a read fault; releases resolved history | V-10 |
| Exact command rosters for every sweep, projection and prune path | V-11 |
| Unknown, absent, faulted, non-Working, queued, latched and terminal-history evidence: no recovery, labelled honestly | V-12 |
| Owner documents | V-13 |
| Inherited predicate, probe-scope, legacy attention, supervisor, CARD-0079, task-stall, dispatch-cost and no-periodic-probe invariants | R-1..R-7 |

Missing setup recorded for Code:

- `StandingBootWatchFixture` (`tests/Antiphon.Tests/TestHelpers/StandingBootWatchFixture.cs`):
  isolated database per test; `FakeTimeProvider` pinned at `Now0` (the CARD-0222 frozen-clock
  hazard is the queue's poll loops; this graph registers no queue); seeds one `Agent`
  (AlwaysOn true, `Kind` per option, `PersistentSessionId` = session), one `AgentSession`
  (Running, `AgentKind` per option, `StartedAt = SessionGeneration.Normalize(Now0 - 6h)`,
  `StandingAgentId` = agent, `GrokRulesState.Ready` for Grok), one `UserPrompt` whose text
  carries `PromptCanary` at `Now0 - PromptAge` (default 9 minutes), optional `TryArmAsync(8)`,
  optional `LegacyState` supervision row (`ConsecutiveFailures 2`, `LivenessLatchedAt Now0-1d`,
  `NextRestartAt Now0+1h`, `RestartBackoffFailures 3`, `LastEscalationTier 1`,
  `HerdrConsecutiveFailures 1`, `LastHealthyAt Now0-2h`); service graph with a capturing logger
  (`MinimumLogLevel`, structured entries), `AppDbContext` with `Interceptors`, `RecordingSessionStopper { StopsSessionsIn }`,
  `MockEventBus`, `ListedInventoryRunner` (listing the session Running at the matching
  `AcceptedStartedAt`), the real `AgentSessionRuntime`, and `BootReplyWatchdogService` built per
  sweep with or without the runtime; `SweepAsync()`, `ReceiptsAsync()` (by prefix),
  `SnapshotAsync()` (session row fields, agent pointer, every supervision column, queue rows and
  SHA-256 of their bodies, park rows, holds), `WorkingAsync()` through `TranscriptWorkingStateQuery`,
  `Recreate()` for a second provider, `WriterInterceptors`/`WriterContextFactory` seam on the
  writer (the `BootStallContextFactory` shape), `SupervisorHarness()` =
  `AgentSupervisionTests.BuildHarness(tempRoot, [], connectionString: cs)` for real ticks.
- `ListedInventoryRunner.Transcript` (S3) so the real runtime catch-up can land a reply.
- V-6 seeds a trailing armed-but-answered session B after the faulted session A so a leaked
  staged entity on the sweep's context would be committed by B's disarm save; the test asserts
  the sweep's enumeration order (A before B) as a precondition.
- V-10 uses `AgentSupervisionTests.BuildHarness` with `IncidentCapPerAgent 2` over the same
  isolated database, so the real `PruneIncidentsAsync` runs.
- New classes are `Unit` (`StandingBootWatchPolicyTests`, `StandingBootDocumentationTests`) or
  `Integration`; none is `Slow`; `slow-tests-allowlist.txt` is unchanged.

Excluded (no CARD-1156 test proves these, with the reason): the production `AgentSessionService.KillAsync`
body (unchanged; V-2 proves the watchdog requests no stop on either surface, and Review reads the
unchanged `Program.cs:383` binding); a database migration; parking enablement; a periodic probe;
option B/C/D protocols; `CatchUpTranscriptAsync`'s contract; the pool-release sweep; the hosted
service tick order (unchanged, read).

### Delivery inventory

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt (durable identity) |
|---|---|---|---|---|---|
| Standing boot receipt | The sweep's `StandingBootWarningWriter` after the observation admits a due stage | `AgentIncidents` row, `Kind = LivenessProbeFailed`, `FailureReason = standingBoot:v1;g=;l=;p=;stage=` | The writer's own context and transaction: session `FOR UPDATE SKIP LOCKED`, owner/task re-read, dedup by prefix and stage, loaded-row boot turn, insert, commit | The next tick re-derives from current facts and the recorded-key pre-check; a failed write leaves nothing and the next tick records once (V-6); a held row declines quietly and the next tick records once (V-7) | The row read back by key and stage from a fresh context after one, two and N ticks, from a second provider and from two concurrent sweeps (V-5), with the stage order Warning then Error (V-2), and its survival through the real prune (V-10) |
| Change notice | `PublishToAllAsync("AgentChanged")` after the commit | Event bus | After the commit | Logged only; a throwing bus keeps the committed receipt (V-6 `publish-fault`) | Not delivery evidence; excluded |
| Attention row | `StandingBootAttentionProjection` from current boot facts | HTTP read model | None (derived) | None needed | The item from `GetAsync` with kind, severity, actions and wording, present without any receipt and absent after resolution (V-8, V-9); never a persisted receipt |
| Supervisor custody | None: the sweep writes no supervision column | `AgentSupervisionStates`, `Agents.PersistentSessionId`, `AgentSessions` | n/a | n/a | After sweeps and a real `AgentSupervisorService.TickAsync`, the row is byte-equal to its seeded snapshot except `LastHealthyAt`/`UpdatedAt` written by the supervisor's live branch; no `RestartScheduled` incident; runner `KillCalls` 0 and no start (V-2, V-12 `legacy-latch`) |
| Session input | None. Detection sends nothing into the session | n/a | n/a | n/a | Runner `Inputs` 0, `ConditionalInputCalls` empty, `SessionQueuedMessages` count and body SHA-256 unchanged, transcript prompt count unchanged, in V-2, V-7 and V-12 (`queued-prompt` additionally asserts the queued record is never re-sent) |
| Legacy non-AlwaysOn diagnostic | The retained generic `RaiseAsync` path | `AgentIncidents` row with `bootSeq=` | The sweep's context (unchanged) | Unchanged | V-12 `non-alwayson-legacy-diagnostic` reads the row back; R-2 projects it |

Substitutes and what they cannot prove: `RecordingSessionStopper` and `ListedInventoryRunner`
are not the production stopper or transport, so they prove the watchdog issues no stop request
on either surface, not that no other service stops the session (the production binding
`IDelegateSessionStopper -> AgentSessionService` is unchanged and Review reads it); V-2's
`AgentSupervisionTests.BuildHarness` runs the real supervisor over a `FakeSessionRunnerClient`,
so it proves the supervisor's live-session branch leaves custody, not a native restart; a zero
direct runner kill count never stands alone (the stopper, the runner `KillCalls`, `CompactionStops`,
`Releases` and `Inputs` are asserted together). No session input is produced on this path, so no
UserPrompt transcript receipt is owed; no design here stops before recipient evidence, because
every receipt is read back from a fresh context and every custody claim is read from the row
after the real supervisor tick.

### Proves it works now

Nothing of CARD-1156 exists at the baseline; every V-n is a Code obligation and every skeleton
is skipped until its slice lands. The probe below is a diagnostic, not a checkpoint execution.

- V-1: the three legacy contracts are reversed and the other eight retained | shared Postgres, real sweep | `BootReplyWatchdogTests` (11) | first: `Killed` empty, no supervision row, session Running, `standingBoot:v1;` receipt; second: failures 2 unchanged, latch null, Error for the operator reason, no "stopped restarting"; third: one Error receipt with the versioned key, watch still armed, second tick 0; the other eight unchanged
- V-2: a Working boot keeps its session and supervisor custody | real sweep, real `AgentSupervisorService`, isolated Postgres, `FakeTimeProvider` | `StandingBootWatchdogTests.C1156_Working_boot_keeps_its_session_and_supervisor_custody` | 3: claude-code, grok (rules Ready), codex; `TranscriptWorkingStateQuery` true before and after; sweep at 9 min writes one Warning receipt, supervisor tick, sweep at 21 min writes one Error receipt, supervisor tick; `Killed` empty, `KillCalls` 0, `CompactionStops` 0, `Inputs` 0, `Releases` 0, `ConditionalInputCalls` empty, session Running with default `TerminationSource` and null `EndedAt`, pointer unchanged, supervision row absent or only `LastHealthyAt`/`UpdatedAt` changed, no `RestartScheduled` incident, no park, no hold, canary absent from receipts
- V-3: emission requires each positive condition | pure policy | `StandingBootWatchPolicyTests.C1156_Emission_requires_each_positive_condition` | 25: admitted -> Detected; every other flip -> None with the named reason; the decision type has no stop/restart/latch/requeue member (asserted by enum values)
- V-4: stages use the prompt clock | pure policy | `StandingBootWatchPolicyTests.C1156_Stages_use_the_prompt_clock` | 8: just-before-boot None; at-boot Detected and the `stage=detected` key; just-before-operator Detected; at-operator NeedsOperator and the `stage=operator` key; boot-longer-than-model operator due at 30; model-disabled operator due at 20; first-seen-after-operator exactly one NeedsOperator; clock-rollback a recorded operator receipt covers a later Detected decision
- V-5: episodes deduplicate and reopen only for a new identity | real sweep, isolated Postgres | `StandingBootWatchdogTests.C1156_Episodes_deduplicate_and_reopen_only_for_new_identity` | 7: repeated-tick (three sweeps, one Warning), fresh-provider (one), concurrent-sweeps (two providers meeting at the writer, one), new-prompt (a second accepted prompt: a new key with one Warning once due, nothing further on the old), new-generation (`StartedAt` moved: new key), new-resume-clock (`LaunchResumedAt` past the old prompt with a new prompt: new key), legacy-cleared-watch (null columns plus an old `bootSeq=` receipt: re-armed, one new-key receipt, no second `bootSeq=` row); custody assertions in every argument
- V-6: telemetry faults preserve custody and future writes | real sweep, writer interceptors, isolated Postgres | `StandingBootWatchdogTests.C1156_Telemetry_faults_preserve_custody_and_future_writes` | 6: read-fault, insert-fault, save-fault, commit-fault (zero receipts, the fault fired, no stop, supervision byte-equal, the trailing session's disarm committed nothing of A), publish-fault (one receipt, the bus threw once, the Warning log line), caller-cancel (`OperationCanceledException` propagates out of `SweepAsync`, zero receipts, no stop); after `Clear()` one fresh tick records exactly once
- V-7: fresh evidence revokes a stale emission | real runtime catch-up, second connection, isolated Postgres | `StandingBootWatchdogTests.C1156_Fresh_evidence_revokes_stale_emission` | 6: reply-during-pull (the runner transcript carries an AssistantText; one pull; the reply is stored; zero receipts; watch disarmed), queued-owner-before-insert, blocked-owner-before-insert, pointer-changed-before-insert, generation-changed-before-insert (each committed on a second connection as the writer's lock statement executes: zero receipts, no "Could not record" Warning), session-lock-held (an uncommitted UPDATE holds the row; a `FOR UPDATE NOWAIT` control proves it; the sweep returns inside 30 s; nothing above Debug except EF's executed-command records; the writer's Debug line; zero receipts; after rollback the next tick records once); custody assertions in every argument
- V-8: current boot attention survives optional history | `AttentionServiceTests.BuildService` with `FakeTimeProvider`, isolated Postgres | `StandingBootAttentionTests.C1156_Current_boot_attention_survives_optional_history` | 7: no-incident (one row, Warning), failed-save (sweep writer faulted: one row), warning-at-eight (Warning; evidence names prompt age, boot due, operator due; actions `[OpenAgent, OpenDrawer]`), error-at-twenty (Error; the operator sentence; no "restart", "retry", "kill" or "latch" text; no Retry/Cancel action), older-than-24-hours (prompt and receipt 25 h old: one row, Error), pruned-history (receipts deleted: one row), legacy-and-current (an old `bootSeq=` receipt on the same session: exactly one row, the new wording); zero writes on the context, runner `Lists` 1
- V-9: positive resolution clears only the current episode | same | `StandingBootAttentionTests.C1156_Positive_resolution_clears_only_the_current_episode` | 7: assistant, thinking, tool-call, tool-result, turn-end (no row; receipts remain), terminal-session (Stopped with `EndedAt`: no row), replaced-launch (new `LaunchResumedAt` and a new unanswered prompt: the old episode's row is gone, the new episode's row appears once due); receipts remain in every argument
- V-10: prune preserves active dedup and releases resolved history | real `PruneIncidentsAsync` through `AgentSupervisionTests.BuildHarness`, isolated Postgres | `StandingBootAttentionTests.C1156_Prune_preserves_active_dedup_and_releases_resolved_history` | 4: age-cutoff (31-day-old current receipts survive; an unrelated 31-day-old incident is deleted), agent-cap (cap 2 with six newer unrelated incidents: both current receipts survive; unrelated rows capped), unknown-current-read (a faulted candidate read: every `standingBoot:v1;` row survives, the rest prunes, a Warning is logged), positive-resolution (after an assistant row the same receipts are deleted by ordinary retention); after each prune a sweep cannot re-mint a retained receipt
- V-11: statement rosters | `FullCommandCounter` on every context, isolated Postgres | `StandingBootStatementBudgetTests.C1156_Boot_watch_statement_budgets` (8) and `C1156_Attention_and_pruning_statement_budgets` (3) | the exact pins and pull counts of the budget tables; every argument prints its roster; zero runner lists in the sweep paths; zero writes and one inherited list in the projection paths
- V-12: evidence variants never authorize recovery | real sweep, isolated Postgres | `StandingBootWatchdogTests.C1156_Evidence_variants_never_authorize_recovery` | 9: runtime-absent (one Warning from stored evidence, zero pulls), pull-fault (`TranscriptFault`: one Warning, one pull, no evaluation error), successful-unchanged-pull (one Warning, one pull), runner-absent (empty listing: one Warning), non-working (prompt then an interrupt marker, Working false: one Warning; still no stop), queued-prompt (`QueuedUserPrompt` only: one Warning whose message says "queued prompt record" and "no reply observed" and not "accepted" or "delivered"; `Inputs` 0; queue rows unchanged), legacy-latch (seeded `LegacyState`: byte-equal afterwards; one Warning), terminal-task-history (a Succeeded and a Failed task on the session: one Warning), non-alwayson-legacy-diagnostic (owner AlwaysOn false: one `bootSeq=` receipt, watch cleared, no supervision row); `Killed` empty and runner counters zero in every argument
- V-13: owner documents | file read | `StandingBootDocumentationTests.C1156_Docs_name_detection_clocks_custody_and_compaction_exception` | 1: `docs/session-runtime-invariants.md` carries `DetectionSentence` ("A taskless AlwaysOn boot stall is detection, never a stop (CARD-1156): the boot reply watchdog records a Warning at `Delegation:BootModelWaitDeadlineMinutes` (8) and an Error attention row at the operator threshold (`prompt + max(positive boot wait, operator wait)`, 20 minutes with defaults), keeps the watch armed, and never stops, restarts, latches or types into the session; CARD-0079 remains the only automatic stop of a Working session, and there is no automatic release deadline.") followed by pins `StandingBootWatchdogTests.C1156_Working_boot_keeps_its_session_and_supervisor_custody` and `StandingBootAttentionTests.C1156_Current_boot_attention_survives_optional_history`; `docs/orchestration-loop.md` and `docs/agent-kinds.md` carry it; the CARD-1151 `AlwaysOnExceptionSentence` text is absent from all three; "stops the session for the existing standing-agent restart ladder", "two consecutive probe-driven restarts", "A human StartAsync clears the latch" are absent from the changed sections

### Guards the regression

- R-1: `BootReplyWatchTests` (27) and `BootLivenessProbeScopeTests` (8): prompt/reply/provider/launch-clock semantics and the once-per-launch probe. CP-9, CP-19.
- R-2: `AttentionServiceTests.An_open_boot_reply_incident_on_a_live_session_is_liveness_probe_failed`, `A_boot_prompt_that_was_answered_after_the_incident_is_no_longer_listed`, `A_boot_reply_incident_on_a_dead_session_is_not_listed` (1 each; their agent is not AlwaysOn, so the legacy helper still owns the row). CP-13, CP-14, CP-15.
- R-3: `AgentSupervisionTests.AlwaysOn_agent_with_no_session_is_scheduled_then_started`, `StartAsync_clears_the_liveness_latch_on_an_already_running_agent`, `Healthy_uptime_resets_the_ladder` (1 each): real no-session recovery, human latch clear and healthy reset stay independent of the sweep. CP-20, CP-21, CP-22.
- R-4: `CheckCompactionContinuationTests` (2), `CheckCompactionRecoveryFlowTests` (1): CARD-0079's conditional stop and continuation are untouched. CP-23, CP-24.
- R-5: `BootStallAttentionTests.C1151_Attention_describes_detection_and_resolution` (5), `BootStallDetectionTests.C1151_Detection_does_not_release_or_park` (2), `BootStallDocumentationTests.C1151_Docs_describe_detection_and_only_compaction_exception` (1, with the S6 constant change): task-bound behaviour and shared documents. CP-16, CP-25, CP-29. Pending at master is not a pass.
- R-6: `DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets` (3 at this baseline and on `31f67fe1`; 18/18/4). CP-26.
- R-7: `SessionHealthTests.No_probe_prompts_are_ever_sent_to_an_idle_session` (1). CP-27.

### Guard inventory

- G-1: the sweep requests no stop on either surface for any observation (D-1) | PC-1
- G-2: the sweep writes no supervision column: no failure increment, no latch, no `NextRestartAt` change (D-1, D-7) | PC-2 (three independently bypassable lines)
- G-3: each whitelist clause group is individually required and unknown is never admitted: deadline, provider, rules, live/EndedAt, generation, owner (count, AlwaysOn, conflict), task owner, prompt/reply/identity (D-3) | PC-3 (eight groups)
- G-4: stage clocks derive from `promptAt`, the operator due uses `max`, and only the highest due stage is decided (D-4) | PC-4 (three lines)
- G-5: persisted dedup under the writer's lock, and a recorded operator stage covers a later Detected decision (D-5) | PC-5 (two lines)
- G-6: the episode key carries the generation and the launch clock (D-4) | PC-6 (two lines)
- G-7: the writer's session lock is `FOR UPDATE SKIP LOCKED`, so two sweeps cannot both insert (D-5) | PC-7
- G-8: the post-pull predicate re-read and the writer's owner/task revalidation precede the insert (D-3, D-5) | PC-8 (two lines)
- G-9: the writer uses its own context, so a failed telemetry row never rides a later save (D-5) | PC-9
- G-10: a telemetry fault never calls a stopper, and cancellation is never swallowed (D-3, D-5) | PC-10 (two lines)
- G-11: attention is derived from current facts, independent of a receipt's existence and age (D-6) | PC-11 (two lines)
- G-12: Error from the operator due, and the legacy row is suppressed for a covered session (D-6) | PC-12 (two lines)
- G-13: the projection revalidates reply, terminal and replacement before projecting (D-6) | PC-13
- G-14: the prune exempts active receipts from the age delete and from the cap delete (D-5) | PC-14 (two lines)
- G-15: nothing new runs ahead of the cheap answered-session gate (budget) | PC-15
- G-16: no probe, input or re-send on repeated detection, and queued evidence is labelled as queued (D-3, D-7) | PC-16 (two lines)
- G-17: terminal task history never excludes the standing detection (D-3 item 4) | PC-17
- G-18: a valid unresolved watch stays armed after a notification (D-5) | PC-18
- G-19: Working is not an admission input (D-3) | PC-19
- G-20: the three owner documents carry the detection sentence and no retired promise (S6) | PC-20 (three documents)

Guards = 20, mapped = 20 to distinct PC families, missing = 0, duplicate PC maps = 0, justified
exclusions = 0. The plan's sixteen families (31 cycles) are retained verbatim as PC-1..PC-16 at
their original variant counts plus the added variants named in F-5..F-7; PC-17..PC-20 are added
by this audit. Variants in one method or file run as separate cycles.

### Positive controls

Each control is a compiling production defect, run method-scoped after land on the
SourceLanding SHA with `/*/*/<Class>/<Method>*`, baseline green, red at the named assertion,
restore, green. Zero tests, build errors, fixture failures or a different assertion are not red.
Only variants in different files and methods may batch. `StandingBootWatchdogTests` and
`StandingBootAttentionTests` cycles are Postgres-backed (about 4 minutes each including the
isolated build); `StandingBootWatchPolicyTests` cycles are pure (about 2 minutes); documentation
cycles about 1 minute.

- PC-1 (1): in `BootReplyWatchdogService` (S3 wiring), after the writer returns Recorded, resolve `IDelegateSessionStopper` from the sweep scope and call `KillAsync(session.Id, ct)`. `C1156_Working_boot_keeps_its_session_and_supervisor_custody` red at `Stopper.Killed.ShouldBeEmpty()` on `claude-code`.
- PC-2 (3): in the same method, (a) load or create the owner's `AgentSupervisionState` and `ConsecutiveFailures++` then save; (b) separately `LivenessLatchedAt ??= now`; (c) separately `NextRestartAt = null`. `C1156_Evidence_variants_never_authorize_recovery` red on `legacy-latch` at the supervision snapshot equality for that field.
- PC-3 (8): in `StandingBootWatchPolicy.Decide`, replace one clause group with `true`: (a) `BootWaitMinutes > 0`; (b) `ProviderVerified == true`; (c) `GrokRules is None or Ready`; (d) `SessionLive == true && EndedAtNull == true`; (e) `Generation is not null`; (f) `OwnerCount == 1 && OwnerAlwaysOn == true && StandingAgentConflict == false`; (g) `TaskOwner == None`; (h) `PromptSequence is not null && ReplyObserved == false && IdentityMatches == true`. `C1156_Emission_requires_each_positive_condition` red at `decision.Stage.ShouldBe(None)` on that group's rows: (a) deadline-zero, deadline-negative; (b) provider-unsupported, provider-unknown; (c) rules-pending, rules-failed; (d) session-missing, session-terminal, ended-at-set; (e) generation-unknown; (f) owner-missing, owner-ambiguous, owner-unknown, owner-not-alwayson, owner-conflict; (g) task-queued, task-dispatched, task-working, task-blocked, task-unknown; (h) prompt-missing, reply-present, reply-unknown, identity-changed. The integration callers are guarded by PC-8 and PC-17.
- PC-4 (3): in `StandingBootWatchPolicy.Facts`/`DueStage`, (a) `bootDue = now + bootWait` instead of `promptAt + bootWait`: `C1156_Stages_use_the_prompt_clock` red on `at-boot` (None instead of Detected); (b) `min` instead of `max` for the operator due: red on `boot-longer-than-model`; (c) `DueStage` returns Detected whenever the boot due is reached, even past the operator due: red on `at-operator` and `first-seen-after-operator`.
- PC-5 (2): (a) in `StandingBootWarningWriter.RecordAsync`, skip the dedup read under the lock: `C1156_Episodes_deduplicate_and_reopen_only_for_new_identity` red on `repeated-tick` at the receipt count; (b) in `StandingBootWatchPolicy.IsRecorded`, a recorded `stage=operator` no longer covers a Detected decision: `C1156_Stages_use_the_prompt_clock` red on `clock-rollback`.
- PC-6 (2): in `StandingBootWatchPolicy.EpisodeKey`, (a) omit `g=`: V-5 red on `new-generation` (the new episode is suppressed as a duplicate); (b) omit `l=`: red on `new-resume-clock`.
- PC-7 (1): in `StandingBootWarningWriter.RecordAsync`, read the session row with a plain `SELECT` instead of `FOR UPDATE SKIP LOCKED`. V-5 `concurrent-sweeps` red at two same-stage receipts. Barrier: the fixture's writer interceptor holds sweep A after its dedup read until sweep B's whole sweep (second provider) has completed, bounded 20 s; with the lock B skips the held row and declines, A inserts (1); without it B inserts and A inserts (2). No sleeps and no barrier that needs both writers to hold the lock.
- PC-8 (2): (a) in `BootReplyWatchdogService.EvaluateAsync`, keep the pre-pull Overdue verdict instead of re-evaluating after the pull: `C1156_Fresh_evidence_revokes_stale_emission` red on `reply-during-pull` at the receipt count; (b) in `StandingBootWarningWriter.RecordAsync`, skip the owner and task re-read under the lock: red on `queued-owner-before-insert`, `blocked-owner-before-insert` and `pointer-changed-before-insert`.
- PC-9 (1): construct the writer over the sweep's scoped `AppDbContext` instead of its own factory. `C1156_Telemetry_faults_preserve_custody_and_future_writes` red on `insert-fault` at the receipt count after the trailing session's disarm save (the staged row rides it).
- PC-10 (2): in the writer's catch, (a) call the stopper on a non-cancellation fault: V-6 red on `commit-fault` at `Stopper.Killed.ShouldBeEmpty()`; (b) catch `OperationCanceledException` and return Faulted: red on `caller-cancel` (the sweep completes instead of throwing).
- PC-11 (2): in `StandingBootAttentionProjection`, (a) project only sessions with at least one `standingBoot:v1;` receipt: `C1156_Current_boot_attention_survives_optional_history` red on `no-incident` and `pruned-history`; (b) restore a `CreatedAt >= now - 24h` filter on the receipts the projection accepts: red on `older-than-24-hours`.
- PC-12 (2): (a) severity Warning at and past the operator due: V-8 red on `error-at-twenty`; (b) keep the legacy `bootSeq=` item for a session the projection covers: red on `legacy-and-current` at two items.
- PC-13 (1): in the projection, skip the current reply/terminal/replacement revalidation and project from the candidate's armed columns. `C1156_Positive_resolution_clears_only_the_current_episode` red on `assistant` and `terminal-session` at a stale row.
- PC-14 (2): in `AgentSupervisorService.PruneIncidentsAsync`, (a) drop the protected-prefix exclusion from the age delete: `C1156_Prune_preserves_active_dedup_and_releases_resolved_history` red on `age-cutoff` at the deleted receipt and the re-minted duplicate; (b) drop it from the cap delete: red on `agent-cap`.
- PC-15 (1): in `BootReplyWatchdogService.EvaluateAsync`, add `await db.AgentIncidents.AnyAsync(i => i.SessionId == session.Id, ct)` before the cheap `HasModelReplySinceAsync` return. `C1156_Boot_watch_statement_budgets` red on `healthy-unarmed-answered` at 3 versus 2.
- PC-16 (2): (a) on a repeated detection, add a one-line probe `SessionQueuedMessage` for the session: `C1156_Evidence_variants_never_authorize_recovery` red on `runtime-absent` at the queue-row count and body hash; (b) for a `QueuedUserPrompt` write "the model accepted the input" into the receipt message: red on `queued-prompt` at the wording assertion.
- PC-17 (1): in `StandingBootWatchObservation.ReadAsync`, include Succeeded, Failed and Canceled in the task-exclusion status set. V-12 red on `terminal-task-history` at zero receipts.
- PC-18 (1): in `BootReplyWatchdogService.EvaluateAsync`, clear `BootPromptSequence` and `BootReplyDueAt` after a recorded stage and save. `C1156_Boot_watch_statement_budgets` red on `same-recorded-episode` at 9 versus 3 (the self-heal re-arm), and V-1's third witness red at `BootReplyDueAt.ShouldNotBeNull()`.
- PC-19 (1): in `StandingBootWatchObservation.ReadAsync`, read `TranscriptWorkingStateQuery` and refuse emission when Working. `C1156_Working_boot_keeps_its_session_and_supervisor_custody` red on `claude-code` at zero Warning receipts; `C1156_Boot_watch_statement_budgets` red on `first-detected-stage` at 16 versus 15.
- PC-20 (3): change the detection sentence in one owner document, one per document. `C1156_Docs_name_detection_clocks_custody_and_compaction_exception` red; documentation control.
- PC-21 (4, added by repair d9a5492f): (a) in `StandingBootAttentionProjection.ProjectAsync`, rethrow from the optional receipt-read catch: `C1156_Optional_receipt_read_fault_keeps_current_attention` red on both arguments (the injected fault escapes `GetAsync`); (b) in `AttentionService.BuildBootReplyMissingItemsAsync`, drop `attachedIncidents.UnionWith(covered)`: `C1156_Legacy_error_history_is_suppressed_with_its_attention_row` red on `covered-due` and `covered-not-due` at `history.SessionId` (the covered session's legacy Error is the newest recent-incident row); (c) drop the task-owned live-session union: same method red on `task-owned`; (d) in the projection, restore `if (decision.Stage == Stage.None) continue;` ahead of the recorded-stage lookup: `C1156_Recorded_operator_stage_survives_clock_rollback` red on `below-boot-due` and `before-prompt` at the single-row assertion.
- PC-22 (1, added by repair 25d569de): in `StandingBootAttentionProjection.Item`, put a delivery verdict back on the UserPrompt line (append `, so delivery is not the problem`): `C1156_Standing_row_makes_no_delivery_claim` red on `partial-prefix` and `complete-match` at the no-delivery-claim assertion.
- PC-23 (1, added by repair e1fcd348, client): in `client/src/features/attention/attentionVisuals.ts`, restore the `LivenessProbeFailed` hint of `47d67de5` (delivery verdict, delegate retry, restart ladder, latch): the Vitest case `CARD-1156 F-5 the standing boot row tooltip claims no delivery and no automatic action` in `AttentionPanel.test.tsx` red at its first true-statement `toContain`. Run `pwsh -File scripts/test-client.ps1 src/features/attention/AttentionPanel.test.tsx -t "CARD-1156 F-5"` (about 10 s).

Cycle total: **47** (the plan's 31 retained plus 10 added: PC-4c, PC-5b, PC-10b, PC-16b,
PC-17, PC-18, PC-19 behavioural, PC-20 three documentation; plus PC-21's four from the S4 repair
and PC-22's one from S4 repair 2, and PC-23's client one from S4 repair 3).
Excluded from mutation with reason:
"no RPC while holding the lock" (no assertion observes statement order inside the writer's
transaction; Review reads `RecordAsync` for any runtime call, and V-11's pull counts bound the
number of pulls to one per cold path); log wording (Review reads the reason tokens).

### Out of scope

- V-1..V-13 bodies. Code writes them; the committed skeletons are skipped, never green.
- Option B/C/D stop protocols, a conditional runner operation, a Working classifier change, a
  `BootReplyWatch.TryArmAsync` rewrite, a runner-absence certificate, a release timer, parking
  enablement, a periodic probe, a channel notification, a migration, a client change, an
  AppHost restart from this worktree.
- The CARD-0079 coordinator's own guards (its unchanged regressions R-4 cover it) and the
  dead-session reconciler, dispatcher and pool-release policies (read-only dependencies).
- Whole-assembly or whole-Unit runs; no production runner, provider or live broker.

### Checkpoints

All rows are the portable .NET/PostgreSQL lane (no `-Runner`, no `-Platform`). One isolated
build per `After` group; every row is serial with `TUNIT_MAX_PARALLEL_TESTS=1` (shared-Postgres
classes in CP-1, CP-13..CP-15 and CP-20..CP-22; isolated-database classes elsewhere). Builds add
`UseAppHost=false` off Windows. Rows CP-16, CP-25 and CP-29 require the real CARD-1151 S4/S5
bodies on master; a pending skip is a red row, not a pass. Run one checkpoint-tool invocation per
committed group (`run --plan <this file> --after S1-S3`, then `--after S4`, then `--after S5-S6`)
and `wait` until the exit is not 75.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1156-watch/` | portable-watch-regression | `/*/*/BootReplyWatchdogTests/*` | V-1 | all 11 (three renamed), 0 failed/skipped | 11 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S3 | `CP-1` | portable-working-custody | `/*/*/StandingBootWatchdogTests/C1156_Working_boot_keeps_its_session_and_supervisor_custody*` | V-2 | all 3, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S3 | `CP-1` | portable-emission-whitelist | `/*/*/StandingBootWatchPolicyTests/C1156_Emission_requires_each_positive_condition*` | V-3 | all 25, 0 failed/skipped | 25 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S3 | `CP-1` | portable-stage-clocks | `/*/*/StandingBootWatchPolicyTests/C1156_Stages_use_the_prompt_clock*` | V-4 | all 8, 0 failed/skipped | 8 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1-S3 | `CP-1` | portable-episode-dedup | `/*/*/StandingBootWatchdogTests/C1156_Episodes_deduplicate_and_reopen_only_for_new_identity*` | V-5 | all 7, 0 failed/skipped | 7 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1-S3 | `CP-1` | portable-telemetry-faults | `/*/*/StandingBootWatchdogTests/C1156_Telemetry_faults_preserve_custody_and_future_writes*` | V-6 | all 6, 0 failed/skipped | 6 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S1-S3 | `CP-1` | portable-fresh-evidence | `/*/*/StandingBootWatchdogTests/C1156_Fresh_evidence_revokes_stale_emission*` | V-7 | all 6, 0 failed/skipped | 6 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S1-S3 | `CP-1` | portable-no-recovery | `/*/*/StandingBootWatchdogTests/C1156_Evidence_variants_never_authorize_recovery*` | V-12 | all 9, 0 failed/skipped | 9 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S1-S3 | `CP-1` | portable-boot-predicates | `/*/*/BootReplyWatchTests/*` | R-1 | all listed, 0 failed/skipped | 27 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S4 | `tests/Antiphon.Tests -> bin-c1156-attention/` | portable-current-attention | `/*/*/StandingBootAttentionTests/C1156_Current_boot_attention_survives_optional_history*` | V-8 | all 7, 0 failed/skipped | 7 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S4 | `CP-10` | portable-attention-resolution | `/*/*/StandingBootAttentionTests/C1156_Positive_resolution_clears_only_the_current_episode*` | V-9 | all 7, 0 failed/skipped | 7 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S4 | `CP-10` | portable-active-retention | `/*/*/StandingBootAttentionTests/C1156_Prune_preserves_active_dedup_and_releases_resolved_history*` | V-10 | all 4, 0 failed/skipped | 4 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | S4 | `CP-10` | portable-legacy-attention | `/*/*/AttentionServiceTests/An_open_boot_reply_incident_on_a_live_session_is_liveness_probe_failed` | R-2 | 1, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | S4 | `CP-10` | portable-legacy-answer | `/*/*/AttentionServiceTests/A_boot_prompt_that_was_answered_after_the_incident_is_no_longer_listed` | R-2 | 1, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | S4 | `CP-10` | portable-legacy-death | `/*/*/AttentionServiceTests/A_boot_reply_incident_on_a_dead_session_is_not_listed` | R-2 | 1, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | S4 | `CP-10` | portable-task-attention | `/*/*/BootStallAttentionTests/C1151_Attention_describes_detection_and_resolution*` | R-5 | all 5, no pending, 0 failed/skipped | 5 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-30 | S4 | `CP-10` | portable-receipt-read-fault | `/*/*/StandingBootAttentionTests/C1156_Optional_receipt_read_fault_keeps_current_attention*` | V-8 (repair F-1) | all 2, 0 failed/skipped | 2 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-31 | S4 | `CP-10` | portable-legacy-error-suppression | `/*/*/StandingBootAttentionTests/C1156_Legacy_error_history_is_suppressed_with_its_attention_row*` | V-8 (repair F-2) | all 3, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-32 | S4 | `CP-10` | portable-operator-rollback | `/*/*/StandingBootAttentionTests/C1156_Recorded_operator_stage_survives_clock_rollback*` | V-8 (repair F-3) | all 2, 0 failed/skipped | 2 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-33 | S4 | `CP-10` | portable-no-delivery-claim | `/*/*/StandingBootAttentionTests/C1156_Standing_row_makes_no_delivery_claim*` | V-8 (repair F-4) | all 2, 0 failed/skipped | 2 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-17 | S5-S6 | `tests/Antiphon.Tests -> bin-c1156-final/` | portable-watch-cost | `/*/*/StandingBootStatementBudgetTests/C1156_Boot_watch_statement_budgets*` | V-11 | all 8, exact pins, rosters printed, 0 failed/skipped | 8 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-18 | S5-S6 | `CP-17` | portable-projection-cost | `/*/*/StandingBootStatementBudgetTests/C1156_Attention_and_pruning_statement_budgets*` | V-11 | all 3, exact pins, rosters printed, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-19 | S5-S6 | `CP-17` | portable-delivery-scope | `/*/*/BootLivenessProbeScopeTests/*` | R-1 | all 8, 0 failed/skipped | 8 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-20 | S5-S6 | `CP-17` | portable-real-exit-recovery | `/*/*/AgentSupervisionTests/AlwaysOn_agent_with_no_session_is_scheduled_then_started` | R-3 | 1, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-21 | S5-S6 | `CP-17` | portable-human-latch-clear | `/*/*/AgentSupervisionTests/StartAsync_clears_the_liveness_latch_on_an_already_running_agent` | R-3 | 1, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-22 | S5-S6 | `CP-17` | portable-healthy-reset | `/*/*/AgentSupervisionTests/Healthy_uptime_resets_the_ladder` | R-3 | 1, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-23 | S5-S6 | `CP-17` | portable-compaction-evidence | `/*/*/CheckCompactionContinuationTests/*` | R-4 | all 2, 0 failed/skipped | 2 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-24 | S5-S6 | `CP-17` | portable-compaction-flow | `/*/*/CheckCompactionRecoveryFlowTests/*` | R-4 | 1, 0 failed/skipped | 1 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-25 | S5-S6 | `CP-17` | portable-task-custody | `/*/*/BootStallDetectionTests/C1151_Detection_does_not_release_or_park*` | R-5 | both, no pending, 0 failed/skipped | 2 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-26 | S5-S6 | `CP-17` | portable-dispatch-cost | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*` | R-6 | all landed arguments, 18/18/4 unchanged, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-27 | S5-S6 | `CP-17` | portable-no-periodic-probe | `/*/*/SessionHealthTests/No_probe_prompts_are_ever_sent_to_an_idle_session` | R-7 | 1, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-28 | S5-S6 | `CP-17` | portable-standing-docs | `/*/*/StandingBootDocumentationTests/C1156_Docs_name_detection_clocks_custody_and_compaction_exception` | V-13 | 1, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-29 | S5-S6 | `CP-17` | portable-task-docs | `/*/*/BootStallDocumentationTests/C1151_Docs_describe_detection_and_only_compaction_exception` | R-5 | 1, no pending, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Roster notes. Counts are TUnit executions from `[Test]` plus `[Arguments]`; no Skip, MethodData
or Matrix in any selected class at this baseline once the skeleton bodies are replaced, and no
platform skip on Linux in any row. CP-3 is 25 and CP-8 is 9 after F-5 and the added legacy
variant; CP-5 is 7 after the added legacy-cleared-watch row. CP-26 selects whatever arguments
are on master at Code's baseline (three today). Union of all rows = V-1..V-13 and R-1..R-7.

### Cost

- Ordinary V/R floor for Code (sum of EstimatedMinutes, CP-1..CP-29) = **78 minutes**,
  estimated: S1-S3 rows 26 (CP-1 includes its build), S4 rows 19 (CP-10 includes its build),
  S5-S6 rows 33 (CP-17 includes its build). Builds measured on this mirror: 124 s for the probe
  build (`Time Elapsed 00:02:04`), run 46 s; the three row builds are budgeted at 4 minutes each
  inside CP-1, CP-10 and CP-17. Slot waits are outside the floor (0 s on this mirror today).
- Build reuse avoids 26 rebuilds (every `CP-n` build cell), about 65 minutes not spent.
- PC floor (Mutation, separately commissioned after Review and land) = **131 minutes**,
  estimated: 26 Postgres-backed cycles (PC-1, PC-2 x3, PC-5a, PC-6 x2, PC-7, PC-8 x2, PC-9,
  PC-10 x2, PC-11 x2, PC-12 x2, PC-13, PC-14 x2, PC-15, PC-16 x2, PC-17, PC-18, PC-19) at 4
  minutes = 104; 12 pure-policy cycles (PC-3 x8, PC-4 x3, PC-5b) at 2 minutes = 24; 3
  documentation cycles (PC-20) at 1 minute = 3. The plan's 31-cycle subset is 108 minutes at
  these rates; the audit adds 23.
- Total = 285 authoring (plan) + 78 ordinary + 131 PC = **494 minutes**, estimated, plus slot
  waits and the separate Review.

Passed the bundle check: bodies read; guards 20, mapped 20, missing 0, duplicate PC maps 0;
every PC names a compiling defect and an exact method; Cost is numeric.

## Diagnostic runs

Run on this Linux runner mirror (nested Docker, Testcontainers PostgreSQL) at the plan commit
`877b98294` with the uncommitted probe and skeletons on disk, through the host build-slot gate
(`slot=granted waited=0s`). They are diagnostics, not checkpoint executions, and they prove
three things: the skeletons compile, the current cheap paths are exactly 0/1/2/2 and the current
cold path kills, and the checkpoint table imports.

- `CHECKPOINT TD-PROBE` (driver `run-checkpoint.ps1`, `bin-c1156td/`, filter
  `/*/*/C1156ProbeTests/*`): `DOTNET build EXIT CODE: 0` (124 s), `DOTNET run EXIT CODE: 0`,
  1 executed, 1 passed (46 s). The probe class is deleted after the run and never committed;
  its rosters are the "Statement budgets" measurements above. Decisive observations:
  `killed=<sessionId>` after the cold raise, `supervision failures=1 latch=`, `working-after=True
  status=Running` (the recording stopper leaves the row, so the production defect is a stop
  request on a Working session).
- Skeleton discovery on a rebuild after the probe was deleted and the skeleton pins edited:
  `CHECKPOINT TD-SKEL commit=877b9829461d8572ff4d1d7cb264f2ef7a463fe1 build=ok filter=/*/*/(StandingBootWatchPolicyTests*)|(StandingBootWatchdogTests*)|(StandingBootAttentionTests*)|(StandingBootStatementBudgetTests*)|(StandingBootDocumentationTests*)/C1156_* executed=0 passed=0 failed=0 skipped=94 trx=/work/worktrees/task-b0a9addd/.antiphon/c1156-td/skel/TD-SKEL-20261008-165117-045d/run.trx slot=granted waited=15s dirty=7 source=877b9829461d8572ff4d1d7cb264f2ef7a463fe1+dirty:eedd671318f96f0f6d9953618725af5727dfe19db8557b773b464381d21eee55 sourceState=dirty buildSource=verified`,
  `CHECKPOINT TD-SKEL EXIT CODE: 3` (fewer than `-MinExecuted` executed, the expected shape
  for an all-skipped selection; build held the slot 269 s). 94 discovered, 94 skipped with their
  `CARD-1156 S<n> pending` reason (25+8 policy, 3+7+6+6+9 watchdog, 7+7+4 attention, 8+3
  budgets, 1 documentation), 0 passed, 0 failed; `dirty=7` is the seven uncommitted files of
  this note.
- Checkpoint tool: `import --plan` on this file imported 29 rows with no warnings
  (`imported 29 rows -> .antiphon/checkpoints.yaml`, slot granted after 16 s, held 4 s).

Alternate outputs `bin-c1156td/` and `bin-c1156td-tool/` are deleted after these runs; results
stay under the ignored `.antiphon/c1156-td/`.

## As built: S1-S3 (Code task c7e48c17)

Branch `feat/card-task-c7e48c17` from `origin/master` `f8ecf3e6b4a5bd6895b3b0bc7e368d4d7c3cd0f6`
(CARD-1151 S4 repair and S5 landed). The plan commit `877b98294` and this design's commit
`6425df245` were cherry-picked with no conflict: both add new files only, and master's landed
`BootReplyWatchdogTests.cs`, owner documents and `BootStallDocumentationTests.cs` were not touched
by either. Production: `BootReplyWatchdogService.cs`, `BootReplyWatch.cs`, new
`StandingBootWatchPolicy.cs`, `StandingBootWatchObservation.cs`, `StandingBootWarningWriter.cs`.
Tests: `BootReplyWatchdogTests.cs` (three reversals), `StandingBootWatchPolicyTests.cs` (V-3, V-4),
`StandingBootWatchdogTests.cs` (V-2, V-5, V-6, V-7, V-12), new `StandingBootWatchFixture.cs`,
`ListedInventoryRunner.Transcript`. No migration, no `Program.cs` change (the sweep takes the
registered `IEventBus` through a new optional constructor parameter). S4, S5 and S6 bodies remain
skipped skeletons.

Decisions and deviations Review should check:

- **V-12 is implemented here, not in S5.** Its skeleton said S5, but the checkpoint table runs it
  as CP-8 in the S1-S3 group, and it is the fail-closed no-recovery witness for the behaviour S1-S3
  ships.
- **V-5 new-generation and new-resume-clock move one identity component each**: the accepted
  generation (with a seeded resume so the launch clock stays put) and the resume clock (moved, but
  kept before the prompt). A new prompt would also change `p=` and hide a missing `g=` or `l=`
  (PC-6); the refined prompt is the new-prompt argument.
- **`BootReplyWatch.cs`**: besides the additive loaded-row `LoadBootTurnAsync` overload, the prompt
  half of the predicate is extracted as `LoadPromptTurnAsync` (returning the latest real prompt's
  kind, so a queued record is labelled as queued). The original `LoadBootTurnAsync(db, id, clock)`
  calls it and keeps its semantics; R-1 (CP-9) guards that.
- **Routing after the pull**: one owner read and one open-task read. A Dispatched/Working task stands
  down for every population (unchanged). No AlwaysOn pointer owner: the generic diagnostic keeps
  its `bootSeq=` Warning and disarm, minus the agent/supervision reads, the latch and the stop.
  Otherwise the standing observation and policy decide; a Queued/Blocked owner, ambiguity, conflict
  or unknown emits nothing and keeps the session.
- **Pre-check and stale watch**: the recorded-key pre-check is skipped while a prompt row sits past
  the watched sequence (a refinement may be a new episode); an `identity-changed`, `prompt-missing`
  or `reply-observed` refusal re-derives the watch through `TryArmAsync` (watch columns only), so
  the next tick judges the current episode.
- **Cost**: a delegate-owned overdue session now costs the pre-check and the owner read on top of
  today's rows/pull/rows/task read (6 commands plus the pull). The standing paths follow the
  designed rosters; V-11 pins them in S5.

Author red (method-scoped, `bin-c1156dev/`, each mutation restored with `git checkout -- server/`
and the restored build re-run green). Batches touched different files and methods only.

| Test | Production mutation | Red at |
|---|---|---|
| `BootReplyWatchdogTests.a_standing_boot_stall_is_detected_without_stopping_or_driving_the_restart_ladder` | M1: `ObserveStandingAsync` calls `IDelegateSessionStopper.KillAsync` after `Recorded` (batch A) | `Stopper.Killed` should be empty |
| `StandingBootWatchdogTests.C1156_Working_boot_keeps_its_session_and_supervisor_custody` (3/3) | M1 | custody snapshot (session Stopped) |
| `StandingBootWatchPolicyTests.C1156_Stages_use_the_prompt_clock` (`boot-longer-than-model`) | M4: `Facts` drops the `max(boot, operator)` raise (batch A) | `before.Stage` |
| `StandingBootWatchdogTests.C1156_Fresh_evidence_revokes_stale_emission` (queued, blocked, pointer, generation) | M7: writer skips `SameOwnerAndSession` (batch A) | sweep returned 1 (a stale receipt) |
| `StandingBootWatchdogTests.C1156_Evidence_variants_never_authorize_recovery` (8/9; the generic diagnostic has no standing receipt) | M2: `ConsecutiveFailures++` on the owner's supervision row after `Recorded` (batch B) | custody snapshot (supervision) |
| `BootReplyWatchdogTests.boot_silence_preserves_existing_failure_history_without_creating_a_latch` | M2 | `state.ConsecutiveFailures` |
| `StandingBootWatchPolicyTests.C1156_Emission_requires_each_positive_condition` (task-queued, -dispatched, -working, -blocked) | M3: task-owner clause bypassed (batch B) | `decision.Reason` |
| `StandingBootWatchdogTests.C1156_Episodes_deduplicate_and_reopen_only_for_new_identity` (`concurrent-sweeps`) | PC-7: writer reads the session with a plain `SELECT` (batch B) | sweep B returned nonzero |
| same method (`new-generation`) | PC-6a: `EpisodePrefix` omits `g=` (batch C) | second sweep returned 0 (suppressed) |
| `BootReplyWatchdogTests.an_unanswered_standing_boot_raises_once_and_keeps_its_watch_for_escalation` | M8: clear the watch columns after `Recorded` (batch C2) | `session.BootReplyDueAt` should not be null |
| `StandingBootWatchdogTests.C1156_Telemetry_faults_preserve_custody_and_future_writes` (`caller-cancel`) | PC-10b: writer `catch (Exception)` swallows cancellation (batch C2) | warnings should not contain "Could not record" |

The PC-10b cycle was first green: the sweep's per-session cancellation check still threw for the
trailing session. The argument was strengthened (cancellation is never logged as a telemetry
fault) and then went red. Every PC in "Positive controls" stays pending for post-land
SourceLanding Mutation; these author-red cycles do not discharge any of them.

Slice order for the next tasks: **S4** (`AttentionService` boot projection, new
`StandingBootAttentionProjection.cs`, `AgentSupervisorService.PruneIncidentsAsync` only; CP-10..16),
then **S5** (V-11 statement budgets, CP-17/18 plus the S5-S6 regression rows), then **S6** (owner
documents, `BootStallDocumentationTests.AlwaysOnExceptionSentence` and its two assertions replaced,
V-13; CP-28/29). AppHost restart is needed after the reviewed land (server change); no runner
upgrade and no migration.

## As built: S4 (Code task 470638a8)

As first built at `fdd4d859` (the repair below changes three of these decisions). Branch `feat/card-task-470638a8` from `origin/master` `8a8cfda480288bbdd353dac063482c0aa61667bc`
(the landed S1-S3 tip). Production: new `StandingBootAttentionProjection.cs`;
`AttentionService.BuildBootReplyMissingItemsAsync` and its one call in `GetAsync`;
`AgentSupervisorService.PruneIncidentsAsync` only. Tests: the three V-8/V-9/V-10 bodies in
`StandingBootAttentionTests.cs`; `StandingBootWatchFixture` gains `StartedAge` (default 6 h,
unchanged for every S1-S3 test). No migration, no `Program.cs` change, no client change. No
existing assertion was weakened or deleted; the only edits outside the S4 skeleton are the
fixture option and the files above.

Decisions and deviations Review should check:

- **Projection admission is the sweep's own `StandingBootWatchPolicy.Decide`**, over
  `StandingBootWatchObservation.FromRow` plus the current boot predicate (model-reply EXISTS, then
  `LoadPromptTurnAsync`), with `IdentityMatches` derived from the CURRENT latest real prompt and
  never from the armed watch columns. Owners are read as every agent sharing a persistent pointer
  with an AlwaysOn agent, so count/AlwaysOn/conflict match the sweep. Error comes from the
  operator due, or from a recorded `stage=operator` receipt of the same episode (no downgrade;
  since the repair this also holds below the boot due, see F-3 below).
- **The call moved ahead of `BuildRecentIncidentItemsAsync`**: the current episode's receipts are
  added to `attachedIncidents`, so an Error receipt is not reported a second time as a
  `RecentCriticalIncident`. Item order is unchanged (the list is sorted at the end).
- **Legacy suppression**: a `bootSeq=` row is suppressed for every live session an AlwaysOn agent
  points at (covered, whether or not a stage is due) and for every session an open task
  (Queued/Dispatched/Working/Blocked) owns while the session is live; the legacy live-session read
  carries the task test inside the same command (first a `NOT EXISTS` filter, since the repair a
  projected `EXISTS` column), so its command count is unchanged.
- **Prune candidates deviate from the design roster** ("the same candidate reads, no receipts
  read"): `CurrentEpisodeKeysAsync` reads the live sessions that hold a `standingBoot:v1;`
  receipt (one command, the receipt test is an `EXISTS` subquery), then the boot predicate's two
  reads per candidate (EXISTS, prompt rows). Reason: plan D-5 says retention checks episode
  resolution, not the notification whitelist, so a disabled deadline, a flipped AlwaysOn, a moved
  pointer or a bound task must not release dedup evidence; candidates selected by AlwaysOn owner
  and task exclusion would release it. Protected keys are the exact two stage keys of each current
  episode (`FailureReason` equality, not `LIKE prefix%`), applied to the age delete and to every
  cap delete. A fault computing them retains every `standingBoot:v1;` row this pass (Warning
  logged); cancellation propagates. Expected V-11 prune deltas for S5: zero candidates 1, one
  candidate 3, two candidates 5 (design said 1 / 5 / 7). Projection roster as designed: 1 with no
  AlwaysOn pointer; agents, sessions, tasks, receipts, then EXISTS and prompt rows per candidate
  (6 for one, 8 for two), plus the unchanged legacy `bootSeq=` read.
- **V-8 wording**: the design's mandated detection sentence itself says "nothing is stopped,
  typed, restarted or latched automatically", so the "no restart/latch text" check asserts the
  retired phrases instead: `restart ladder`, `stopped restarting`, `kill`, `retry`,
  `Boot prompt confirmed`, `composer holds` (case-insensitive), and no Retry/Cancel action.
- **V-8 `warning-at-eight`** seeds a 7-minute-old prompt so the fake clock walks forward onto the
  boot due (one second before: no row); `error-at-twenty` checks one second before the operator
  due is still a Warning. `older-than-24-hours` seeds a 26-hour-old launch and a 25-hour-old prompt
  and ages the receipt by 25 hours. V-10 adds a receipt of an earlier resolved episode on the same
  session so a global `standingBoot:v1;` exemption goes red, and asserts exact removed counts.

Author red (method-scoped class runs from `bin-c1156s4dev/`, each batch restored with
`git checkout -- server/` before the next; mutants in one batch touch disjoint test cases).

| Case | Production mutation | Red at |
|---|---|---|
| V-8 no-incident, failed-save, pruned-history | M-A: `ProjectAsync` skips a candidate with no standing receipt (PC-11a) | `rows.Count` 0 |
| V-8 legacy-and-current | M-D: legacy rows not suppressed for covered sessions (PC-12b) | `rows.Count` 2 |
| V-10 age-cutoff (and unknown-current-read) | M-G: age delete over all incidents (PC-14a) | `removed` |
| V-8 error-at-twenty, older-than-24-hours | M-C: `Item` severity always Warning (PC-12a) | `row.Severity` |
| V-10 agent-cap | M-H: cap delete over all incidents (PC-14b) | `removed` |
| V-10 positive-resolution | M-J: `CurrentEpisodeKeysAsync` skips the reply check | `removed` |
| V-9 assistant, thinking, tool-call, tool-result, turn-end | M-E-reply: `ProjectAsync` drops the reply revalidation (PC-13) | rows not empty |
| V-8 error-at-twenty | M-F: current receipts not attached | `RecentCriticalIncident` row present |
| V-10 unknown-current-read | M-I: the fault path does not retain standing receipts | `removed` |
| V-8 older-than-24-hours | M-B: projection skips a prompt older than 24 h (PC-11b shape) | `rows.Count` 0 |
| V-9 terminal-session | M-E-live: no live-status filter and no live/ended gate | rows not empty |
| V-9 replaced-launch | M-E-armed: prompt identity from the armed columns (PC-13) | rows not empty |
| V-8 warning-at-eight (all seven) | M-K: boot-due evidence line prints the operator due | `row.Evidence` |

Every PC in "Positive controls" stays pending for post-land SourceLanding Mutation; these
author-red cycles discharge none of them. Next: **S5** (V-11 budgets, CP-17/18 and the S5-S6
regression rows CP-19..CP-27; use the prune deltas above), then **S6** (owner documents,
`BootStallDocumentationTests.AlwaysOnExceptionSentence` and its two assertions, V-13; CP-28/29).
AppHost restart is needed after the reviewed land (server change); no runner upgrade and no
migration.

### S4 repair (Code task d9a5492f, Final Review 3fe22492)

Branch `feat/card-task-d9a5492f` from `ced090d8`; landing owner remains 470638a8. Production:
`StandingBootAttentionProjection.ProjectAsync` (now takes the caller's `ILogger`) and
`AttentionService.BuildBootReplyMissingItemsAsync` only. Tests: three new methods in
`StandingBootAttentionTests` (CP-30..CP-32); `AttentionServiceTests.BuildService` gains an optional
`logger` (default `NullLogger`, unchanged for every existing caller); `StandingBootWatchFixture`
gains `Logger<T>()` over its captured log. The class helper `ProjectAsync` takes an optional fault
interceptor and an optional observation time (a separate `FakeTimeProvider`, because the fixture's
clock cannot move backwards). No existing assertion was weakened or deleted; no migration.

- **F-1, optional receipt read isolated.** A non-cancellation fault on the standing-receipt SELECT
  is caught, logged at Warning ("Could not read the standing boot receipts; ...") and every episode
  is projected from its clock alone, as if it had no receipt; the caller's cancellation still
  propagates. The rest of `GetAsync` is unaffected. Degraded mode: an unreadable operator receipt
  is not attached, so its Error can also appear as a `RecentCriticalIncident` for that pass, and a
  clock stepped back below the operator due shows the clock's Warning (pinned by
  `operator-on-record-unreadable`).
- **F-2, legacy Error suppressed in the recent-incident sweep.** Every legacy `bootSeq=` row inside
  the 24-hour window on a covered session (live, AlwaysOn pointer) or on a live session an open task
  owns is added to `attachedIncidents` before `BuildRecentIncidentItemsAsync`, at any severity.
  Legacy rows of any other session, including ended sessions of the same agent, stay ordinary
  recent-incident history (the test's dead-session row is the control).
- **F-3, monotonic operator stage.** Admission still requires the whitelist (`decision.Facts` is
  set only when every positive condition holds); `Stage.None` with facts is "valid, unresolved,
  nothing due on the clock". A recorded `stage=operator` receipt of the same episode now yields
  the Error row whatever the clock says, including below the boot due and before the prompt. A
  recorded `stage=detected` receipt alone does not show a row below the boot due (not requested;
  unchanged). Positive resolution (model reply, terminal session, new launch/prompt) still runs
  before and wins.
- **Statement counts** are unchanged: the receipt read and the legacy live-session read are still
  one command each.

Author red. Base: the seven new cases red with `server/` at `ced090d8` (18 existing green). Mutants
built into `bin-c1156rdev/`, each batch restored with `git checkout -- server/`; batches pair
mutants in different files whose cases are disjoint.

| Case | Production mutation | Red at |
|---|---|---|
| CP-30 `detected-on-record`, `operator-on-record-unreadable` | PC-21a: rethrow in the receipt-read catch | injected fault escapes `GetAsync` |
| CP-31 `covered-due`, `covered-not-due` | PC-21b: no `attachedIncidents.UnionWith(covered)` | `history.SessionId` (covered session's legacy Error) |
| CP-31 `task-owned` | PC-21c: no task-owned live-session union | `history.SessionId` |
| CP-32 `below-boot-due`, `before-prompt` | PC-21d: `Stage.None` skipped before the recorded-stage lookup | single-row assertion, 0 rows |

Review R2 disclosures, judged here (no production change):

- R2-1 (missing prompt facts release dedup receipts) is not a one-line change: retaining a
  candidate whose generation or prompt cannot be read needs a per-session retention set in
  `CurrentEpisodeKeysAsync` and `PruneIncidentsAsync`, plus new missing-prompt/missing-generation
  prune cases. Backlog.
- R2-2 (protected keys are `FailureReason` only, not `(SessionId, FailureReason)`) is conservative
  over-retention. Backlog.
- R2-3 (unknown-evidence wording versus the whitelist that declines unknowns) is a wording and
  test-design reconciliation; the no-stop guarantee is unaffected. Backlog.

PC-21 joins the pending list; every PC stays pending for post-land SourceLanding Mutation. AppHost
restart is needed after the reviewed land (server change); no runner upgrade and no migration.

Evidence correction (Review 67673f16 R3): repair d9a5492f's report and brief stated 289 test
executions for its final checkpoint run 20261008-205030-4a16; its 16 rows sum to **268**
(7+7+4+1+1+1+5+1+2+164+25+10+33+2+3+2). No row or argument was missing; the CP lines themselves
were right.

### S4 repair 2 (Code task 25d569de, Final Review 67673f16)

Branch `feat/card-task-25d569de` from `d833a35d`; landing owner remains 470638a8. Production:
one sentence in `StandingBootAttentionProjection.Item`. Tests: one new method in
`StandingBootAttentionTests` (CP-33). No existing assertion changed; no migration.

- **F-4, no delivery verdict on the standing row.** The UserPrompt evidence line ended "..., so
  delivery is not the problem." The projection reads only the latest prompt record's kind,
  sequence and time (`BootReplyWatch.LoadPromptTurnAsync`) and never matches it against the
  intended request, so a 12-character prefix with zero complete matching UserPrompt records
  produced the same sentence. The line now ends "no assistant, thinking, tool or turn-end row
  since." and states no delivery success or failure. The queued-record line, headline, the other
  evidence lines and the receipt message (`StandingBootWatchPolicy.Message`) already made no
  delivery claim and are unchanged. The plan's "retain the transcript-delivery distinction"
  instruction is corrected to match.
- **CP-33** `C1156_Standing_row_makes_no_delivery_claim`: `partial-prefix` (the only UserPrompt
  is cut to the first 12 characters of the still-Pending queued body; control: one record, zero
  complete matches) and `complete-match` (control: one complete match). Both assert the headline,
  the evidence and the sweep's receipt message contain none of `deliver`, `not the problem`,
  `received`, `accepted`, `reached the`, `confirmed`, then that the prompt line is exactly the
  stored facts and the silence.
- **Not changed (outside this card's footprint, disclosed for Backlog):** the same "delivery is
  not the problem" wording on the legacy `bootSeq=` attention row
  (`AttentionService.BuildBootReplyMissingItemsAsync`, CARD-0312, pinned by
  `AttentionServiceTests` CP-21), on the CARD-1151 task Overdue row (`AttentionService.BootStallItem`),
  in the client `LivenessProbeFailed` tooltip (`client/src/features/attention/attentionVisuals.ts`,
  which the standing row also shows) and in `docs/orchestration-loop.md` (CARD-0353 section).

Author red: `server/` checked out at `d833a35d` (the server diff to this repair is exactly the
restored sentence plus a two-line comment, so this is also the PC-22 mutant), tests at this
repair, class method run from an isolated build: both arguments red at the no-delivery-claim
assertion (`text should not contain "deliver"`); restored, rebuilt: both green. PC-22 joins the
pending list; every PC stays pending for post-land SourceLanding Mutation. AppHost restart is
needed after the reviewed land (server change); no runner upgrade and no migration.

| Case | Production mutation | Red at |
|---|---|---|
| CP-33 `partial-prefix`, `complete-match` | PC-22: the UserPrompt line ends ", so delivery is not the problem." (base `d833a35d`) | no-delivery-claim assertion, `deliver` |

### S4 repair 3 (Code task e1fcd348, Final Review 7c7df08d)

Branch `feat/card-task-e1fcd348` from `47d67de5`; landing owner remains 470638a8. Client only:
the `LivenessProbeFailed` badge tooltip (`client/src/features/attention/attentionVisuals.ts`) and
its type comment (`client/src/api/attention.ts`). Tests: one new Vitest case in
`AttentionPanel.test.tsx`. No server change, no existing assertion changed, no migration. The
"Out of scope: a client change" line above predates Review 7c7df08d F-5, which commissioned this one.

- **F-5, the standing row's tooltip.** `AttentionPanel` draws every `LivenessProbeFailed` row's
  badge tooltip from the kind's `hint`, so the standing row showed "The prompt reached the
  transcript, so delivery is not the problem ... A delegate task is retried once; a standing
  agent goes through the restart ladder and then latches off." After CARD-1156 the watchdog never
  stops, restarts or latches a standing agent, and the projection cannot verify complete delivery.
  The hint now says only: no assistant, thinking, tool or turn-end row has appeared since the boot
  prompt, past the boot-reply deadline; a standing agent's row is Warning from the boot notice due
  and Error from the operator decision due, and its evidence gives the prompt age and both due
  times; detection only, Antiphon only reports this and the session keeps running and keeps its
  seat; open the agent or the session, then reply through the session or keep waiting.
- **Shared text.** The same hint also serves the legacy `bootSeq=` rows (CARD-0312). The task
  card liveness badge never shows this kind (`LIVENESS_KINDS` excludes it) and the CARD-1151 task
  row is `Overdue`, not this kind. Every sentence is true for the legacy rows too: nothing acts
  on a boot stall automatically since CARD-1156, and the severity sentence is scoped to a standing
  agent. No other kind's hint changed. Action labels on the row (`Open agent`, `Read it first`) are unchanged and true.
- **Regression** `CARD-1156 F-5 the standing boot row tooltip claims no delivery and no automatic
  action` renders `AttentionPanel` over the shape `StandingBootAttentionProjection.Item` emits
  (Error, no task, session and agent, the five evidence lines, `OpenAgent`/`OpenDrawer`), hovers the
  badge and reads the rendered `role=tooltip` text. It asserts the true statements and that the
  tooltip contains none of `deliver`, `restart`, `ladder`, `latch`, `retry`, `retri`, `kill`,
  `stop`. The row's own evidence still says "nothing is stopped, typed, restarted or latched", so
  the negative check is scoped to the tooltip.
- **Still not changed (server, outside this repair):** the legacy `bootSeq=` row's evidence
  sentence and the CARD-1151 task Overdue row's wording, as disclosed in S4 repair 2.

| Case | Mutation | Red at |
|---|---|---|
| F-5 tooltip | PC-23: restore the 47d67de5 hint (`attentionVisuals.ts` at 47d67de5) | first true-statement `toContain` |
| F-5 tooltip | new hint plus " A delegate task is retried once." | negative loop, `retri` |
| F-5 tooltip | new hint plus " Nothing is stopped automatically." | negative loop, `stop` |
| F-5 tooltip | new hint plus " The prompt was delivered." | negative loop, `deliver` |

The first author mutant with "retried" passed against the first draft of the test, which checked
only `retry`; the stem `retri` was added and all three mutants then went red. PC-23 joins the
pending list. The client change needs a `client/dist` rebuild (Aspire's watcher on 17203, or the
AppHost restart already required by S4) before the browser shows it.

## As built: S5 (Code task d99733e0)

Branch `feat/card-task-d99733e0`, fast-forwarded from the S4 repair 3 tip `d5cb8e08` to
`origin/master` `51f175dbf738519e8e50842697499c6c6c12b2c1` (CARD-1105 commits only on top; no
CARD-1156 file differs). Tests only: the two V-11 bodies in `StandingBootStatementBudgetTests.cs`.
No production change, no fixture change, no migration, no `Program.cs` change, no existing
assertion changed. V-12 has no S5 body left: it was implemented with S1-S3 (see above). S5 itself
needs no AppHost restart (test-only); the restart owed by S1-S4 is unchanged.

Measured pins (FullCommandCounter on every context of the sweep's provider, which the writer and
the runtime inherit; seed and arm contexts are uncounted):

- **V-11a** equals the design table exactly: 0 / 1 / 2 / 2 / 15 (1 pull) / 3 / 15 (1 pull) / 14.
  The runtime's pull persisted nothing (0 SQL). The cold roster is the designed one: candidates,
  rows, recorded-key pre-check, rows (post-pull), owner list, task exclusion, model-reply EXISTS,
  prompt rows; writer session `FOR UPDATE SKIP LOCKED`, owner, tasks, dedup, EXISTS, prompt rows,
  INSERT. The test also asserts zero runner lists, no sweep or writer fault Warning, the exact
  receipt stages on record after the measured tick, and the custody counters.
- **V-11b projection** equals the design: 1 / 6 / 8. Measured by calling
  `StandingBootAttentionProjection.ProjectAsync` directly with its own counter; the test then
  requires that exact command sequence (same SQL text) to appear as one contiguous slice of the real
  `GetAsync` roster, starting at the AlwaysOn pointer read, which appears once. `GetAsync`'s own
  totals (52 / 50 / 52 here; the zero-candidate argument carries a legacy `bootSeq=` row, so its
  legacy helper reads more) are printed but not pinned: the rest of `GetAsync` belongs to other
  cards. Zero writes, nothing staged, exactly the one inherited runner list.
- **V-11b prune** is **1 / 3 / 5** over the inherited age DELETE and cap GROUP BY (the design said
  1 / 5 / 7). Reason: the S4 deviation recorded above. Prune candidates are live sessions holding a
  `standingBoot:v1;` receipt (one read with an `EXISTS` subquery, no receipts loaded), then the boot
  predicate's two reads per candidate; the design's roster re-read agents, sessions and tasks.
  The test pins the inherited pair by shape (one DELETE, one GROUP BY) and requires the delta
  commands to be reads.

Author red (method-scoped class runs from `bin-c1156s5mut/`, each batch restored with
`git checkout -- server/`; within a batch every case is moved by exactly one mutant):

| Case | Production mutation | Red at |
|---|---|---|
| V-11a boot-deadline-disabled | A1: `SweepAsync` checks the deadline after the live-session read | `total` 1 vs 0 |
| V-11a healthy-unarmed-answered | A2 (PC-15 shape): `EvaluateAsync` reads `AgentIncidents.AnyAsync` ahead of the cheap model-reply gate | `total` 3 vs 2 |
| V-11a armed-before-deadline, same-recorded-episode, first-detected-stage, first-operator-stage, runtime-absent-detection | A3: `EvaluateAsync` reads the owner list before the Overdue return | `total` 3 / 4 / 16 / 16 / 15 |
| V-11b zero-, one-, two-candidates | A4: `ProjectAsync` adds a standing-receipt `AnyAsync` ahead of the pointer read | `helperRoster.Count` 2 / 7 / 9 |
| V-11a no-live-sessions | B1: `SweepAsync` adds an `Agents.AnyAsync` ahead of the live-session read | `total` 2 vs 1 |
| V-11b zero-, one-, two-candidates | B2: `PruneIncidentsAsync` recomputes `CurrentEpisodeKeysAsync` before the cap loop | prune delta 2 / 6 / 10 |

Every PC stays pending for post-land SourceLanding Mutation; these author-red cycles discharge none
of them (PC-15, PC-18 and PC-19 name V-11 cases). A landed doc pin check: `BootStallDocumentationTests`
(CP-29's class, S6's row) was run once from the S5 development build and is green at this baseline,
so S1-S4 left no landed doc pin red. Next: **S6** (owner documents,
`BootStallDocumentationTests.AlwaysOnExceptionSentence` and its two assertions replaced, the V-13
body; CP-28/29).

## As built: S6 (Code task 92cc0d45)

Branch `feat/card-task-92cc0d45` from `origin/master` `f305e8aec721819f8757eb602a5a0d90c9f6b89b`
(the landed S5 tip). Documentation and test text only; no production behaviour changes, no
migration, no `Program.cs` change, no client change.

- **Owners.** `docs/session-runtime-invariants.md`: the CARD-1151 bullet's last sentence now points
  at a new bullet "A hung taskless AlwaysOn boot is reported, not recovered" with five sentences,
  each followed by its pins (`StandingBootDocumentationTests` constants): `DetectionSentence`
  (Warning receipt from the boot due, Error receipt from the operator threshold, at most one each per
  episode, own context and transaction, watch armed, never stops, kills, fails, restarts, latches,
  types or writes supervision state), `AttentionSentence` (row from current facts; Error also from a
  recorded operator receipt; resolution), `UnknownSentence` (unreadable policy input and an open
  Queued/Dispatched/Working/Blocked task record nothing; boot wait `<= 0` is off; runtime, pull and
  runner absence still record from the stored transcript), `LegacyDiagnosticSentence` (the generic
  `bootSeq=` path), `ReportedSentence` (CARD-0079 remains the only automatic stop of a Working
  session; no deadline releases the seat). `docs/orchestration-loop.md` gains a paragraph after the
  CARD-1151 provider-stall paragraph and `docs/agent-kinds.md` replaces the Grok note's exception
  sentence; both carry `DetectionSentence` and `ReportedSentence`. AGENTS.md is unchanged.
- **V-13** `C1156_Docs_name_detection_clocks_custody_and_compaction_exception`: the five sentences
  and their `nameof` pins in the owner; the two sentences in the loop and Grok note; the retired
  CARD-1151 sentence and the three CARD-0312 promises the design names absent from all three docs;
  restart-ladder wording absent from the three changed sections; the 8 and 20 minutes checked
  against `DelegationSettings` defaults and `StandingBootWatchPolicy.Facts`/`DueStage`.
- **Design reversal 4 (declared).** `BootStallDocumentationTests.AlwaysOnExceptionSentence` was the
  CARD-1151 F1 sentence "... still raises its incident and stops the session for the existing
  standing-agent restart ladder ...". CARD-1156 option A made it false. The constant now equals
  `StandingBootDocumentationTests.DetectionSentence`; its two `ShouldContain` assertions (runtime,
  agent-kinds) are kept against the new text, and each gains a `ShouldNotContain` of the retired
  sentence (`StandingBootDocumentationTests.RetiredAlwaysOnExceptionSentence`). No other assertion
  changed.
- **Other true-making edits** (repo grep for restart-ladder, latch, stop and "nothing ends it"
  claims about standing/AlwaysOn sessions): `AttentionDtos.cs` `LivenessProbeFailed` comment ("Error
  on the latching third, where the mechanism has stopped restarting"), `AgentSupervisionState.cs`
  `LivenessLatchedAt` comment (no code sets it since CARD-1156; a legacy value still holds automatic
  starts until a human start or attach clears it), `DelegationSettings.BootModelWaitDeadlineMinutes`
  comment (adds the standing receipts). Already true and unchanged: `BootReplyWatchdogService.cs:36`,
  `StandingBootAttentionProjection.cs:43`, `client/src/api/attention.ts:158`,
  `AgentIncidentKind.LaunchInterruptedByRestart` (the supervisor's not-running restart, not the boot
  watchdog), the negated wording in `BootReplyWatchdogTests` and `StandingBootAttentionTests`.
  Out of this card's footprint and left as found: CARD-1160's delivery-claim lines; the CARD-1151
  task-path residue `server/Bundles/check-interpreter.md:27` ("the harness kills and retries it once
  there") and the Windows-only test name
  `GrokDelegateEndToEndTests.a_provider_that_never_answers_the_boot_prompt_is_failed_killed_and_retried_once`.

Author red (doc mutants need no rebuild; the doc tests read the files at run time; each mutant
restored with `git checkout -- docs` and the clock mutant with `git checkout -- server`):

| Case | Mutation | Red at |
|---|---|---|
| V-13, CP-29 | runtime: the old exception sentence restored beside the new bullet | V-13 `runtime: CARD-1151 AlwaysOn exception`; CP-29 `runtime: retired AlwaysOn stop sentence` |
| V-13, CP-29 | agent-kinds: the detection sentence replaced by the old sentence | `agent-kinds: detection sentence`; `agent-kinds: AlwaysOn detection-only exception` |
| V-13 | loop: the detection sentence replaced by the old sentence | `loop: detection sentence` (CP-29 does not read the loop for it: green) |
| V-13, CP-29 | all three owners checked out at `f305e8aec` | `owner sentence missing`; `agent-kinds: AlwaysOn detection-only exception` |
| V-13 | runtime: one pin replaced by prose | `pin ... must follow` |
| V-13 | runtime: "two consecutive probe-driven restarts" appended | `runtime: retired promise` |
| V-13 | agent-kinds: "goes through the restart ladder" in the Grok note | `agent-kinds: retired wording 'restart ladder'` |
| V-13 | `StandingBootWatchPolicy.Facts` boot due from the operator wait (rebuild) | `boot due at the defaults` |

PC-20 stays pending for post-land SourceLanding Mutation with every other PC. No AppHost restart is
owed by S6 itself (comments and docs only); the restart owed by S1-S4 is unchanged.

**S6 repair (Review f5b1d580 F-1, Code f6ed79e3).** The `AttentionKind.LivenessProbeFailed`
summary claimed a delivered, transcript-confirmed boot prompt "projected from open incidents". It
now says neither projection is a delivery verdict, describes the standing row as projected from
the session's current facts (latest prompt record's age against the boot due and operator
threshold, no model row on the current launch), rendering with or without a saved
`standingBoot:v1;` incident and labelling a queued prompt record, and describes the legacy
`bootSeq=` incident projection separately. Pin:
`StandingBootDocumentationTests.C1156_LivenessProbeFailed_summary_claims_no_delivery_and_names_both_projections`
(reads the summary text at run time; no rebuild per mutant):

| Mutation | Red at |
|---|---|
| `AttentionDtos.cs` checked out at `b9ae1cd4f` (the old summary restored) | `LivenessProbeFailed summary: Neither projection below matches the prompt ...` |
| new summary plus the line "The boot prompt was transcript-confirmed." | `LivenessProbeFailed summary: retired 'transcript-confirmed'` |

Comment-only repair: no behaviour change, no migration, no restart beyond the one S1-S4 owe.

**S6 repair 2 (Review 5906dbf4 F-1, Code f405ce72).** The standing-row summary said the row
exists "with no assistant, thinking, tool or turn-end row on that launch". That is false for a Grok
session: `BootReplyWatch.HasModelReplySinceAsync` ignores rows inside a queued rules refresh's
(`RulesPromptSequence`, `RulesTurnEndSequence`] window (the launch rules turn and a
compaction-triggered refresh alike), so such rows can exist on the current launch, and, after a
compaction refresh, even after the boot prompt, while the row stands. The summary now says "no
qualifying boot-model reply on that launch (as decided by
`BootReplyWatch.HasModelReplySinceAsync`, which ignores Grok rules-turn responses)". The same
enumeration was emitted to users by the standing path, so it defers too: the receipt message
(`StandingBootWatchPolicy.Message`) now reads "no qualifying model reply in {age}", the row's
prompt line (`StandingBootAttentionProjection.Item`) "no qualifying model reply since.", the
`LivenessProbeFailed` tooltip "No qualifying model reply has appeared since the boot prompt", and
the projection's predicate comment and the client `AttentionKind` doc comment say the same. This
supersedes the message shapes quoted in the D-4 and S6 sections above. Left as found, because their
predicate really is every model row after the boot prompt (`BootReplyWatch.Evaluate`/
`EvaluateSessionAsync`, no rules exclusion): the generic `bootSeq=` diagnostic message in
`BootReplyWatchdogService` and its `AttentionServiceTests` fixture. Left as found and outside
CARD-1156's footprint: the CARD-0353 task-path wording (`DelegateCheckProbe.CheckSessionFacts.BootTurn`,
`AgentTaskFailureCode.ProviderUnresponsive`, `AgentIncidentKind.ProviderUnresponsive`, the
`AttentionService` CARD-0353 excerpt and `server/Bundles/check-interpreter.md`), reported to the
caller rather than changed here.

| Pin | Mutation | Red at |
|---|---|---|
| `StandingBootDocumentationTests.C1156_LivenessProbeFailed_summary_claims_no_delivery_and_names_both_projections` | `AttentionDtos.cs` checked out at `11847ca6f` (old clause restored) | `LivenessProbeFailed summary: For a taskless AlwaysOn session ...` |
| same | new summary plus "no assistant, thinking, tool or turn-end row" appended | `LivenessProbeFailed summary: retired 'assistant, thinking, tool or turn-end'` |
| `StandingBootAttentionTests.C1156_Standing_row_makes_no_delivery_claim` | `StandingBootAttentionProjection.cs` checked out at `11847ca6f` (rebuild) | the prompt-line `ShouldContain` |
| `BootReplyWatchdogTests.an_unanswered_standing_boot_raises_once_and_keeps_its_watch_for_escalation` | `StandingBootWatchPolicy.cs` checked out at `11847ca6f` (rebuild) | `ShouldContain("no qualifying model reply in")` |
| `AttentionPanel.test.tsx` LivenessProbeFailed tooltip case | `attentionVisuals.ts` checked out at `11847ca6f` | `toContain('No qualifying model reply ...')` |

Text-only repair: no behaviour change, no migration, no restart beyond the one S1-S4 owe.

**S6 repair 3 (Review 8216918f F-1, Code 5a07a450).** The `LivenessProbeFailed` tooltip
(`attentionVisuals.ts`) and the client `AttentionKind` doc comment said the row stood "past the
boot-reply deadline". `StandingBootAttentionProjection` keeps a standing row at Error once an
operator-stage receipt of the same episode is on record, even when the read clock is before the
boot due or before the prompt itself (S4 repair F-3), so that clause can be false while the row
shows. Both now describe the severities without asserting a clock reading: Warning is raised from
the boot notice threshold, Error from the operator decision threshold, an Error recorded for the
episode stays Error even if the clock later moves backwards, and the row's evidence carries the
prompt time, its age and both due times. The same omission was fixed in the `AttentionService`
standing-projection comment and the orchestration-loop owner paragraph (both said Error from the
operator threshold only). The DTO summary's "a saved Error receipt of the same episode only keeps
the Error stage when the clock steps back" was also too narrow (a raised operator threshold keeps
it too); it now reads "keeps the row at Error even if the clock later reads earlier than the
operator threshold". Server emitted strings (headline, evidence lines, receipt message) were
checked and left as found: none states a clock reading as present fact; the numbers are evidence.
Deliberate assertion changes: the DTO pin
(`C1156_LivenessProbeFailed_summary_claims_no_delivery_and_names_both_projections`) requires the
new sentence and rejects the old "only keeps ... when the clock steps back"; the existing F-5
Vitest case's three `toContain` strings follow the new tooltip text.

| Pin | Mutation | Red at |
|---|---|---|
| `AttentionPanel.test.tsx` `CARD-1156 Review 8216918f F-1 a recorded Error read under a clock rollback ...` | `attentionVisuals.ts` checked out at `7d3638977` (old tooltip) | `toContain('No qualifying model reply to the boot ...')` |
| same | new tooltip with ", past the boot-reply deadline" re-inserted | `past the: ... not to contain 'past the'` |

Text-only repair: no behaviour change, no migration, no restart beyond the one S1-S4 owe.

**S6 repair 4 (Review 4bbc024a F-1/F-2, Code c467dcb5; landing owner 5a07a450).** Repair 3's
promise that "an Error recorded for the episode stays Error" is false on a live row: CP-30
`operator-on-record-unreadable` records the operator stage, faults the optional receipt SELECT and
reads a Warning. Operator-facing and client text therefore no longer describes retention at all.
The tooltip (`attentionVisuals.ts`), the client `AttentionKind` comment (`attention.ts`), the DTO
summary (`AttentionDtos.cs`), the `AttentionService` projection comment, the orchestration-loop
paragraph and the session-runtime owner sentence (`AttentionSentence`, which said "Error from the
operator threshold or a recorded Error receipt of the same episode") now say only what is always
true: Warning from the boot notice threshold, Error from the operator decision threshold, both
computed from the session's current facts; the evidence carries the prompt time, its age and both
due times; the server decides what qualifies as a reply; detection only.

The retained-Error behaviour is recorded here only, as the code produces it
(`StandingBootAttentionProjection.ProjectAsync`):

- The displayed stage is NeedsOperator (Error) when the receipt read succeeds AND it returns a
  `standingBoot:v1;...;stage=operator` receipt whose prefix equals the current episode prefix
  (generation, launch clock, prompt sequence; due settings and the wall clock are not in the key).
  Otherwise it is the clock stage `DueStage(facts, now)`; `None` projects no row.
- Clock rollback: with the operator receipt readable, a read at prompt+9, prompt+7 (below the boot
  due) or prompt-1 (before the prompt) is Error (CP-32 `below-boot-due`, `before-prompt`).
- Unreadable receipt: the SELECT fault is logged as a Warning and every episode is projected with no
  receipts, so the same rollback at prompt+9 is a Warning (CP-30 `operator-on-record-unreadable`).
- `Delegation:ModelWaitDeadlineMinutes` raised after the operator stage was recorded (20 -> 60, read at
  prompt+21): the key is unchanged, so the receipt matches and the row is Error, with
  `Operator decision due <prompt+60>` in the evidence; the same raised setting before any operator
  receipt is a Warning (new `C1156_Recorded_operator_stage_survives_raised_operator_threshold`).
- Age clamp: a read before the prompt shows `no model reply 0s after the prompt` and
  `Prompt #1 (UserPrompt) at <real prompt time>, 0s ago; no qualifying model reply since.` on one
  line, with both dues (CP-32 `before-prompt`, new assertions).

F-3 (real-queue producer-to-recipient evidence) is not addressed here: the standing row reports and
never delivers, so the operator treats it as an R2 Backlog disclosure. No test in this slice
exercises delivery.

Deliberate assertion changes: `AttentionSentence` follows the owner sentence; the DTO pin requires
"It renders with or without a saved <c>standingBoot:v1;</c> incident. It makes no claim about
delivery: ..." (the repair 3 retention sentence is gone) and rejects `RetiredRetentionPromises`; the
new `C1156_Standing_row_texts_name_thresholds_and_no_retention_promise` requires the threshold
wording in all five surfaces and rejects the promise wording in each; the Vitest rollback case
replaces its `toContain('an Error recorded for the episode stays an Error ...')` with
`toContain('both computed from the current facts of the session')`, keeps every earlier forbidden
stem and adds `stays an error`, `stays error`, `recorded for the episode`, `moves backwards` and
`clock later`. `AttentionServiceTests.BuildService` gains an optional `delegation` argument (default
unchanged) so the projection can read a raised setting.

| Pin | Mutation | Red at |
|---|---|---|
| `C1156_Standing_row_texts_name_thresholds_and_no_retention_promise` | each of the five files checked out at `1b573a238` (five runs) | `<surface>: the thresholds, from current facts` (tooltip, client type, service, loop); `dto: retention promise 'keeps the row at Error'` |
| same | new text kept, promise sentence inserted after it, one surface per run (five runs) | `tooltip: ... 'stays an Error'`, `client type: ... 'stays Error'`, `dto: ... 'keeps the row at Error'`, `service: ... 'stays Error'`, `loop: ... 'keeps the row at Error'` |
| `C1156_Docs_name_detection_clocks_custody_and_compaction_exception` | `session-runtime-invariants.md` at `1b573a238` | `owner sentence missing` (`AttentionSentence`) |
| `C1156_LivenessProbeFailed_summary_claims_no_delivery_and_names_both_projections` | `AttentionDtos.cs` at `1b573a238` | `summary: It renders with or without ...` (required sentence) |
| `C1156_Recorded_operator_stage_survives_clock_rollback(before-prompt)` | age clamp removed in `Item` | `Headline` should be `... 0s ...` but was `... -60s after the prompt.` (`below-boot-due` stays green) |
| `C1156_Recorded_operator_stage_survives_raised_operator_threshold` | `stage = decision.Stage` (recorded operator stage ignored) | `the recorded operator stage is read and kept` (Warning) |
| `AttentionPanel.test.tsx` rollback case | promise sentence inserted into the new tooltip | `stays an error: expected ... not to contain 'stays an error'` |

These are author diagnostics; every PC stays pending for SourceLanding Mutation. Wording that remains
after this repair, outside the as-built notes: implementation comments that state the code's
behaviour precisely in the branch that runs only with receipts read
(`StandingBootAttentionProjection.cs` "Once the operator stage is on record for THIS episode it stays
the displayed stage", `StandingBootWatchPolicy.cs` "A recorded operator stage covers a later Detected
decision"), test names and comments, and the reject lists. No predicate change, no migration, no
restart beyond the one S1-S4 owe.
