# CARD-1151 test design

TestDesign for `docs/superpowers/plans/2026-10-08-card-1151-boot-stall-detection-plan.md`
(plan commit `65745cfa3aee43542fbeb6711996c769119028df`, which is also `origin/master` at
the time of this design; the plan's own citations were taken at `03c10be6d4467730ae2e26d8ed49cc8b3ddd3302`,
and every production file the plan cites is byte-identical between the two). The fix design
(D-1..D-5) is unchanged except for the additions A-1..A-11 and the decision Q-1 recorded
below. This file is the checkpoint `--plan` input: the tool reads the first `### Checkpoints`
table below. Production code was not edited. Baseline for every citation: master
`65745cfa3aee43542fbeb6711996c769119028df`.

Committed beside this note are compiling skeletons that fix the class, method and argument
rosters named below. Every skeleton body throws TUnit's `SkipTestException` with its slice
tag, so it is discovered, counted as skipped, never green, and cannot be mistaken for proof.
Code replaces each body in the slice that lands the behaviour.

| Skeleton | Project | Slice |
|---|---|---|
| `tests/Antiphon.Tests/TestHelpers/Card1151Pending.cs` (helper) | server | all |
| `tests/Antiphon.Tests/Application/BootStallPolicyTests.cs` | server | S1 |
| `tests/Antiphon.Tests/Application/BootStallDetectionTests.cs` | server | S1, S2, S3, S5 |
| `tests/Antiphon.Tests/Application/BootStallAttentionTests.cs` | server | S4 |
| `tests/Antiphon.Tests/Application/BootStallDocumentationTests.cs` | server | S4 |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.BootStall.cs` | server | S5 |

Rename-and-flip of the five existing witnesses (below) is Code's work in S1, S5 and S6; they
are not touched here, so the characterization class stays green at this commit and still
reproduces the defect.

## Safe-absence audit

This stage is safety-critical in both directions. A wrong "safe to fail" decision stops or
fails a live session; a wrong "keep" decision leaves a dead session waiting, which must stay
bounded and visible. The rule under audit is D-2's six-condition whitelist plus D-1's
"DetectOnly for every incomplete observation", and the internal no-stop retry of D-2.

### Findings

- **F-1. The safe-absent branch is unreachable against any real runner.** D-2 item 3
  requires the owning runner's fresh inventory to contain **no** entry for the session id
  (any status, including `Exited`, vetoes). D-2 item 4 requires, for a previously created
  boot session, "complete readable transcript evidence for that generation", which the plan's
  own ground truth and S1's adapter spell as the runner transcript DTO with
  `TerminalComplete: true` and `AcceptedStartedAt` equal to the session generation
  (`Dispatcher:2764-2767`). On the runner, `SessionRunnerRuntime.List()` (`:810`) enumerates
  `_sessions.Values`, and `GetTranscript(sessionId)` (`:880`) resolves `GetSession(sessionId)`
  from the same `_sessions` map; `TerminalComplete` is produced only by a live tailer's
  `Snapshot()` (`TranscriptTailer.cs:192`, `CodexTranscriptTailer.cs:222`,
  `GrokTranscriptTailer.cs:170`) through that session object. A session that is absent from
  the inventory therefore answers 404 on its transcript (unknown under D-2), and a session
  whose transcript is complete and readable is still listed (vetoed under D-2). The two
  positive conditions are mutually exclusive for every runner build on master and on every
  in-flight branch (`origin/feat/card-task-809c8b49`, `1a174347`, `a086fe80` add no
  producer). Consequence: with real runners the plan's retained "one automatic retry for a
  proven-absent boot" never fires; every boot stall is detection only, and a dead boot
  session is handled by the dead-session reconciler's existing policies. That is fail-closed
  and satisfies the card's ask, but it means S3's whitelist/revalidation/internal-requeue
  machinery, V-7, V-8 and their positive controls would exercise a fixture that fabricates a
  contradictory runner (absent from the list, yet answering a complete transcript). The
  existing S1 fixture (`DelegationDispatchRecoveryBoundaryTests.AbsentLaunch.cs:576-583`)
  already fabricates exactly that pair for CARD-1149's never-attempted hold, where CARD-1153
  is replacing it with a certificate; no such replacement exists for a created boot session.
  This is Q-1 below.
- **F-2. Reconciler precedence is unstated.** `TickAsync` runs the delivery watchdog, then
  the dead-session reconciler, then the overdue sweep (`Dispatcher:374-387`). A task whose
  session row is terminal (`AgentTaskLiveness.IsDeadSession`: Stopped, Failed, `EndedAt`
  set, or row missing) is the reconciler's: grace, runner gate, bind recovery, settle,
  commit-recovery hold, S1 hold, then `FailAndNotifyAsync` with no retry (`:2480-2653`).
  Today the boot tail can race it inside the three-minute grace and produce a different
  outcome (failure plus retry) for the same dead session. The plan keeps both policies and
  says "S1's never-attempted hold still wins" and "other dead-session reasons retain their
  own existing failure policy" without saying which sweep owns a terminal-row boot session.
  A-1 resolves it fail-closed.
- **F-3. Task status does not say whether the boot prompt was delivered.** A delegate task
  stays `Dispatched` until the reply service promotes it (`AgentTaskReplyService.cs:434`,
  `:1547`, `:1648`, `:1763`, all reply/report/API-error adoption paths); the Grok witness
  asserts `Dispatched` after its confirmed prompt (`GrokDelegateEndToEndTests.cs:402-406`).
  The real-world boot stall is therefore a `Dispatched` task with a `Sent` brief, and any
  rule keyed on `AgentTaskStatus.Working` would miss it. A-2 keys ownership on the brief
  row, as the delivery watchdog already does (`Dispatcher:2137-2165`).
- **F-4. Post-detection cost is unbounded in time.** Today a breached boot task costs two
  `TaskDeadlinePolicy.EvaluateAsync` passes (eight statements each), gates 1/1b (two), a
  runner transcript pull and gate 3 (two) on every five-second tick, and that ends when the
  task is failed at eight minutes. Under the plan the task stays open indefinitely, so that
  roughly twenty-statement, one-HTTP-call tick repeats until an operator acts. The ordinary
  young tick (18/18/4) is unaffected, but the brief's "no per-tick increase on the
  CPU-constrained desktop" is only half met without A-7.
- **F-5. The plan's regression floors are correct at this baseline.** Source-counted
  `[Test]`/`[Arguments]` executions: characterization 3, overdue 29, deadline policy 26, boot
  predicate 27, dead session 25, delivery watchdog 76 (a `Slow`-category class, already in
  `slow-tests-allowlist.txt`), concurrency 25, predicates 17, dispatch failure 15 (no platform
  skip; its `OperatingSystem.IsWindows()` branch only sets a file mode), launch ownership 7,
  resume 8, compaction continuation 2, recovery flow 1, `C1149_*` 49. The plan's CP-24 `Min`
  of 1 for `C1149_*` is replaced by 49.
- **F-6. Cross-card prose.** CARD-1153's test design (R-6, CP-29) and CARD-1149's (R-boot,
  CP-37/38) describe `BootStallWorkingTickCharacterizationTests` as "unchanged" and name the
  old method. Their filters are class-level, so their executed counts (3) survive the rename;
  the orchestrator should read those rows as "class green" after CARD-1151 lands, not as the
  old stop assertion. CARD-1149's uncommitted V-20
  `C1149_C1150_Working_full_tick_safety` is superseded by V-1 here and should not be written.

### Hazard table

One row per hazard. "Required outcome" is what production must do; "Witness" is the V/R/PC
that proves it. Rows marked with an A-n depend on an addition below.

| H | Hazard | Required outcome | Witness |
|---|---|---|---|
| H-1 | Working session, real prompt, no model reply yet (slow model), listed with the matching generation | DetectOnly. Task status, attempt, token, binding and failure fields unchanged; session Running; one `BootStallDetected` Warning at eight minutes; stopper, runner kill/start/release/compaction-stop/input all zero; no Failed/Retried event, no parent note, no incident, no alias hold | V-1, V-3 working-listed; PC-1, PC-20, PC-23 |
| H-2 | The runner still lists the session (Running, Pending, Exited; same or other generation) | Listed is a veto. DetectOnly | V-3 idle-listed-running, idle-listed-exited, idle-wrong-generation; PC-3 |
| H-3 | Owning runner unreachable, directory missing, null or unsupported inventory | Unknown is a veto. DetectOnly, no failure | V-3 unavailable-remote, missing-directory, null-list; V-2 inventory-unavailable; PC-3 |
| H-4 | Stale runner listing | A stale "present" listing vetoes (safe side). A stale "absent" list (the dead-session sweep's cached local list) is never an input to the boot decision: a fresh owning inventory is read, and re-read at the final barrier | V-2 inventory-listed; V-8 inventory-entry; PC-8 |
| H-5 | Different generation: resume or relaunch moves the launch clock; a listing with another `AcceptedStartedAt` | The old prompt is invisible to the boot predicate (`LaunchClockAsync`); a listing of any generation is listed | V-3 idle-wrong-generation; R-clocks; PC-3 |
| H-6 | Prompt only queued (`QueuedUserPrompt`, never accepted by the session) | No boot episode exists and no boot protection is given: the task keeps the ordinary role-ceiling failure exactly as at base, whether the session is idle, Working through an inherited mid-turn row, or terminal. A later queued row never opens or advances an accepted prompt's episode (R1, section "Final Review repairs") | V-18 (CP-39, CP-40, CP-41) and CP-2 `prompt-only-queued` |
| H-7 | Transcript tailer lag: the reply exists on the runner, not in the database | Gate 2 pulls before any decision; a reply that lands ends the episode; a failed pull leaves DetectOnly | V-21 `C1151_Reply_landing_in_the_pull_ends_the_episode` (CP-44): fresh-reply-lands-in-the-pull and stale-reply-lands-in-the-pull land the reply DURING Gate 2 through `CatchUpOverride` and assert the pull and the post-pull outcome; pull-times-out fails the production pull at the runner. V-4 model-reply-returns-to-ordinary-policy is only the stored-row control: its reply is pre-seeded before the first evaluation and it installs no pull hook (corrected by repair 2, Review 409623bd F1); PC-4, PC-32..PC-34 |
| H-8 | AlwaysOn Check seat (CARD-0079 territory) | Boot detection never calls `CheckCompactionContinuationService` or the runner's conditional compaction stop; `CompactionStops` is asserted zero in every witness; CARD-0079's own tests are unchanged | V-1; R-C0079 (CP-34, CP-35); PC-23 |
| H-9 | Parked or Blocked session | Blocked is outside the overdue population; detection creates no park row, no release row, no release call, with parking off and on | V-12; PC-12 |
| H-10 | CARD-1149 S1 absent-launch hold in progress (Failed row, runner-unknown reason, grace running) | The terminal row is the reconciler's (A-1): the boot sweep writes no boot event and fails nothing; the S1 hold still wins | V-3 terminal-row-reconciler-owned; V-14 (CP-19); PC-17 |
| H-11 | CARD-1150 S2 brief ensure in progress, or a Pending brief while the session answers another prompt | A task whose brief row is Pending is the delivery watchdog's (A-2): no boot event, no failure; the boot sweep never calls ensure or creates/resets/removes a queue row | V-3 dispatched-pending-brief; V-10; PC-17, PC-10 |
| H-12 | CARD-1153 certificate once it exists | Never requested, prepared or interpreted by the boot sweep; a never-created certificate contradicts a transcript-confirmed prompt and can never be boot evidence; a 404 is never promoted | G-23 (Review-read, justified in the guard inventory); R-S1 rows rerun when S4 lands |
| H-13 | Restart between detection and escalation | Events are durable rows; dedupe reads the row by episode key and stage; after restart at most one operator event, never a burst | V-5 restart-after-threshold; V-9 service-recreation; PC-5, PC-9 |
| H-14 | Repeated ticks | One event per episode key and stage; at most two rows per episode plus one per later real prompt; no periodic repeat | V-9 repeated-tick; PC-9 |
| H-15 | Clock steps | The episode key has no wall-clock component (A-3); a backwards step creates no key and no second Detected event; durations are non-negative; a forward step past the threshold writes the single operator event | V-5 clock-rewind-after-detection; PC-5 |
| H-16 | Delivery watchdog ("Boot prompt was never delivered") | Its stopper stays conditional on a committed real failure, non-Working and the runner safety gate; a boot Warning is never read as a delivery failure | V-15; PC-14 |
| H-17 | Telemetry write fails (read, insert, commit, publish) | Disposition unchanged; the separate context leaks nothing into a later unrelated save; the next tick records once | V-6; PC-6 |
| H-18 | General 20-minute or role ceiling breached on an unresolved boot | No terminal failure (D-1); a model reply returns the task to ordinary non-killing policy | V-4; PC-4 |
| H-19 | Boot notification disabled (`BootModelWaitDeadlineMinutes` 0) | No eight-minute event; the 20-minute operator event when another deadline brings the task into evaluation; still no failure | V-4 boot-notification-disabled; PC-4 |
| H-20 | Workspace shows progress, or the probe is null/unreadable | Progress withholds failure and changes nothing else (DetectOnly either way); null/unreadable never establishes non-Working | V-4 workspace-progress; V-2 workspace-not-quiet; PC-2 |
| H-21 | Terminal DB row (Stopped/Failed/EndedAt/missing) with a boot-shaped transcript | The reconciler owns it (A-1): no boot event, no boot failure, no race with its grace, S1 hold or no-retry failure | V-3 terminal-row-reconciler-owned; PC-17 |
| H-22 | A session reappears after a safe-absent requeue (Q-1 option A only) | The requeue calls no stopper and no release; later ownership is the pool-release sweep's existing rule, not this card's; V-7 drives `FailOverdueTasksAsync` alone so that sweep is not in the measurement | V-7; PC-15 |
| H-23 | Alias ledger and incidents | Detection writes no `ProviderUnresponsive` incident and no `BootStallLedgerKey` row; no AutoDetected hold follows detection | V-1, flipped `repeated_boot_detections_never_hold_the_model`; PC-20 |
| H-24 | Human Retry or Cancel on a detected task | Unchanged: the explicit path stops the delegate and requeues; the internal bypass is unreachable from HTTP-bound input | V-16; PC-16 |
| H-25 | Warning Detail content | Stable reason token plus key fields and due times; never prompt text, composer text or secrets (A-4) | V-5 canary; PC-19 |
| H-26 | Session-scoped `BootReplyWatchdogService` | Keeps standing down for an open delegate task; raises nothing and stops nothing in the detection fixture; its taskless AlwaysOn stop (`:314`) is the separate card the plan names | V-12; R-session-watch (CP-25) |
| H-27 | Attention projection | The boot row precedes generic Overdue/ProgressStalled for this task; NeverStarted and DeadSession keep their precedence for Pending-brief and terminal rows; wording never promises kill/fail/retry; actions are OpenDrawer, Reply, Cancel; the row exists without a persisted Warning and resolves on a reply | V-11; R-attention (CP-13); PC-11 |
| H-28 | Per-tick cost | Ordinary young and preview ticks unchanged (18/18/4, zero delta); a detected unresolved task's tick is bounded by A-7 | V-13 young-preview, working-repeated-episode; CP-17; PC-13, PC-18 |
| H-29 | Two scoped dispatchers tick concurrently, or Attention reads while the sweep writes | One event per key and stage under the task lock; no double failure; the projection never writes | V-9 concurrent-contexts; PC-9 |
| H-30 | A later genuine prompt (refinement) into the still-silent session | A new episode key with one new Detected event; the old key is closed; no failure | V-9 later-real-prompt; PC-9 |
| H-31 | Unresolved API-error recovery (CARD-0072) or commit-recovery obligation (CARD-0547) | Existing gates return before any boot work and before any boot event | V-2 api-recovery-unresolved, commit-recovery-pending; R-deadlines; PC-2 |
| H-32 | Attempt exhausted (attempt 2 of 2) on a Working boot | Still DetectOnly: the attempt count never authorizes failure | flipped `a_second_boot_episode_on_an_exhausted_attempt_still_only_detects`; PC-21 |
| H-33 | Grok rules Pending/Failed | `EvaluateAsync` returns null; no episode | R-clocks; excluded |
| H-34 | Check-role or `ReplyTo None` task | No parent note on detection; the parent queue is unchanged | V-1, flipped overdue witness (parent session kept); PC-1 |
| H-35 | Dispatched task whose brief is Sent and transcript-confirmed (the real-world shape, F-3) | DetectOnly with one Detected event, exactly as for a Working task; task status never gates detection | V-3 dispatched-sent-brief-detects; S6 native witness; PC-17 |

### Additions the plan leaves implicit

- A-1: a task whose session snapshot satisfies `AgentTaskLiveness.IsDeadSession` is owned by the dead-session reconciler. The overdue sweep's boot disposition for it is DetectOnly **without emission**: no boot Warning, no failure, no requeue. The reconciler's DeadSession attention row already covers it. This removes the pre-existing race between the boot tail and the reconciler's grace and makes "S1's hold still wins" true by construction.
- A-2: ownership of a not-yet-Working task is keyed on the brief row, not `AgentTaskStatus`. A task whose own delegation brief row is still `Pending` (the watchdog's `briefNeverTyped`) gets DetectOnly without emission; a task whose brief is `Sent` (or has no brief row and a real prompt since dispatch) is a boot episode whether it is `Dispatched` or `Working`. D-1's protection against general/ceiling terminalization applies to both.
- A-3: the episode key is `task id + attempt + session id + normalized accepted generation + launch clock + latest real boot-prompt sequence`, exactly as D-3, and contains no wall-clock reading; stage due times are derived from `promptAt` and settings on every tick; `TaskDeadlinePolicy.NonNegative` rules apply to every age.
- A-4: Warning Detail is a stable reason token (`BootStallDetected` or `BootStallNeedsOperator`) followed by the key fields, `promptAt`, `bootDueAt` and `operatorDueAt`; never the prompt text, composer text or any secret. V-5 seeds a canary in the prompt text and asserts its absence from every event.
- A-5: the pure policy's observation record carries, in addition to the plan's twelve fields, the API-error-recovery and commit-recovery flags and a model-reply flag, so V-2 is one closed table of fifteen flips: fourteen select DetectOnly, `model-reply-present` selects NotBoot.
- A-6: roster changes against the plan: V-3 gains `terminal-row-reconciler-owned`, `dispatched-pending-brief` and `dispatched-sent-brief-detects` (eleven arguments); V-4 gains `model-reply-returns-to-ordinary-policy` (seven); V-5 gains `clock-rewind-after-detection` (five); V-12 takes `parking-off`/`parking-on` as arguments (two); V-2 is fifteen.
- A-7: post-detection cost bound. For a task whose current episode key already has its current stage recorded, the overdue sweep skips Gate 2's runner pull and the second evaluation until the next stage boundary is due, then runs them once before writing the operator event; the safe-absent whitelist, if kept (Q-1), is evaluated at most once per 60 seconds per task through a process-local timestamp in the `DeadSessionFirstSeenState` shape. The first-pass evaluation on stored rows still runs every tick, so a streamed reply still ends the episode promptly. This is consistent with CARD-0055: the pull precedes every irreversible decision, and detection is not one.
- A-8: no incident and no alias hold on detection (G-20). Under Q-1 option A the `BootStallLedgerKey` ledger counts only committed safe-absent failures; under option B the ledger, the `ProviderUnresponsive` boot incident and the repeat-alias hold retire with the branch.
- A-9: attempt exhaustion never authorizes a boot failure (G-21).
- A-10: two regression rows the plan omits: `BootReplyWatchdogTests` (the stand-down and the taskless behaviour this card leaves alone) and the five `AttentionServiceTests` Overdue/ProgressStalled-precedence methods, because S4 edits the projection.
- A-11: S1 moves the characterization's `BootStallWorld` graph to `tests/Antiphon.Tests/TestHelpers/BootStallWorld.cs` with a `FakeTimeProvider`, an `ISessionRunnerDirectory` stub (owning inventory: available list, unavailable, missing, null), a `RecordingSessionStopper` and a `StubWorkspaceProgressProbe`, plus a `RunTickAsync()`/`RunOverdueSweepAsync()` pair, so `BootStallDetectionTests` and the characterization class share one fixture. The characterization tests keep their seeded shapes.

### Q-1: decision for the caller (why this stage ends with `decide`)

The plan says no policy decision is left to Code. F-1 shows that D-2's retained automatic
boot failure/retry is unreachable with real runners, and F-2 shows the ownership question it
raises. Two consistent designs exist; the rosters below are written for option A (the plan
as landed) and state exactly what option B removes.

- **Option A (plan as written).** Keep the three-valued disposition and the whitelist. S3
  implements the adapter, revalidation and the internal attempt-bound requeue; V-7 and V-8
  exercise them on the explicitly contradictory fixture (Running row, prompt then interrupt
  marker, owning inventory absent, transcript DTO `TerminalComplete` for the same generation,
  quiet workspace). Honest about reachability: the artifact records that no runner produces
  this pair. Cost: S3 at 60 minutes, CP-8/CP-9, PC-2 positive admissions, PC-7, PC-8,
  PC-15 (19 mutation cycles on a path no runner reaches).
- **Option B (recommended).** Remove the automatic boot failure/retry entirely. The
  disposition is `NotBoot`/`DetectOnly`; the dead-session reconciler owns terminal rows with
  its existing policies (S1 hold, no-retry failure); the delivery watchdog owns Pending-brief
  tasks; both boot-tail kill fallbacks, `TryHoldOnBootStallRepeatAsync`'s boot caller, the
  boot `ProviderUnresponsive` incident and the internal requeue entry are deleted; human
  Retry is the only retry. S3 becomes "remove the tail" (20 minutes). Roster deltas: drop
  CP-8 and CP-9 (V-7, V-8); V-2 shrinks to three arguments (prompt-only Working, the
  would-be-absent pristine observation, model reply) because every observation is DetectOnly
  or NotBoot, CP-2 `Min` 3; V-13 drops `safe-absent-final-revalidation` and
  `safe-absent-race-abort`, CP-16 `Min` 8; PC-2 becomes one cycle (admit the pristine
  observation as a failure), PC-7, PC-8 and PC-15 are removed; the flipped alias-hold test
  asserts "never holds" in both options, so nothing else moves. Ordinary floor 114 minutes,
  Mutation 29 cycles.

Either option keeps every safety guarantee the card asks for. The caller chooses; Code then
takes this artifact with the option named in its brief.

## Q-1 decided: option B (Code S1-S3)

The caller chose option B. Code task `4adef3e9` implemented S1-S3 on that basis; this section
supersedes every option-A statement above where they disagree.

- **Disposition.** `BootStallPolicy.Disposition` has exactly `NotBoot` and `DetectOnly`. A
  transcript-confirmed prompt with no model row since the launch clock is DetectOnly whatever
  the Working verdict, runner listing, workspace probe, attempt count or task status says.
  `NotBoot` needs the positive answer of the boot predicate (a model row, or no real prompt).
  Boot facts ride `TaskDeadlinePolicy.Verdict.Boot` whichever clock wins and are read only
  behind the existing cheap age gate, so the 18/18/4 hot paths are untouched.
- **Consequence recorded, not in the plan's text:** a session that is NOT Working with an
  unanswered boot prompt (prompt, then an interrupt marker) is also DetectOnly; the role ceiling
  no longer fails it, and the operator event is written instead (V-3 idle shapes pin it).
- **Removed (S3 = remove the tail):** `TryFailBootStallAsync` (ProviderUnresponsive failure,
  automatic `RetryAsync`, both `KillAsync` fallbacks), `BootStallLedgerKey` and
  `TryHoldOnBootStallRepeatAsync` (incident and repeat-alias hold), the raw `ProbeWorkspaceAsync`.
  No internal no-stop requeue was added; `AgentTaskService.cs` is untouched. A human Retry is
  the only retry; a terminal session row is the dead-session reconciler's (A-1); a Pending
  brief is the delivery watchdog's (A-2). `DelegationSettings.BootStallRepeatHoldMinutes` is now
  read by nothing; its comment is S4's (the setting stays, no configuration break).
- **Operator threshold:** `promptAt + max(boot wait, ModelWaitDeadlineMinutes)`, 20 minutes when
  the model wait is disarmed. With the shipped 8/20 it is `promptAt + 20`.
- **A-7 as built:** the post-detection tick costs the first evaluation, gates 1/1b, one
  ownership read (session status/EndedAt plus the brief row in one statement) and one lock-free
  key read on the telemetry context; no runner pull and no second evaluation until the next
  stage is due. The key is re-read under the task lock before any insert.
- **Struck rows and skeletons:** V-7 (`C1151_Safe_absent_boot_keeps_failure_and_retry`, 4
  arguments) and V-8 (`C1151_Race_revokes_absent_failure`, 4) are removed from
  `BootStallDetectionTests`; CP-8 and CP-9 are struck from the table; PC-7, PC-8 and PC-15
  and guards G-7, G-8, G-15 are removed.
- **V-2 re-derived (CP-2):** the option-A fifteen-flip table described inventory and
  terminal-evidence conditions that no longer exist. Option B's table flips the emission
  whitelist instead, one condition per argument, eleven arguments: `model-reply-present`
  (NotBoot), `session-row-missing`, `session-terminal`, `brief-pending`, `brief-state-unknown`,
  `api-recovery-unresolved`, `api-recovery-unknown`, `commit-recovery-pending`,
  `identity-changed`, `stage-not-due`, `would-be-absent-pristine` (option A's admitted shape,
  DetectOnly). One added method, `C1151_Stage_boundaries_come_from_the_prompt_clock`, pins the
  stage boundaries and the dedup rule. CP-2 selects the class: `Min` 12. PC-2 is one cycle
  (admit any failure disposition / treat one unknown as admitted) per the note.
- **V-13 (S5):** the two safe-absent arguments are removed from the skeleton (eight remain);
  CP-16 `Min` 8.
- **Regression rows moved to S1-S3:** the production change landed in S1-S3, so CP-13, CP-17,
  CP-18, CP-19 and CP-21..CP-35 now run after S1-S3 on the CP-1 build. Two rows are added:
  CP-37 `DelegateCheckProbeTests` (it calls `TaskDeadlinePolicy.EvaluateAsync` and the boot
  predicate) and CP-38 the registry guard (`TestClassificationGuardTests`,
  `SlowTestTripwireTests`). The S4-S6 Code task reruns these rows on its own build.
- **Assertion reversals 2-4 moved from S5 into S1-S3:** the three `AgentTaskOverdueDeadlineTests`
  boot witnesses and the alias-hold test would be red after S1 otherwise; they are flipped as
  listed in "Assertion reversals" (the overdue harness's model wait is 50 000, so its operator
  threshold is not reached at 44 000 minutes and the flipped witness asserts one Detected only).
  `TaskDeadlinePolicyTests.a_prompt_with_nothing_after_it_takes_the_tighter_boot_deadline` keeps
  its assertions, corrects its "licenses the kill" message and adds the boot-facts pin.
- **Open for S6:** `GrokDelegateEndToEndTests.a_provider_that_never_answers_the_boot_prompt_is_failed_killed_and_retried_once`
  still asserts the removed kill/retry and is red on Windows until S6 flips it; it is skipped on
  Linux.
- **Checkpoint facts (Code 4adef3e9):** CP-22's `Min` is 28, not 29 (the 29th `[Test]` in that file is
  `AgentTaskDispatcherWiringTests`). CP-37 carries one inherited red on the server2 mirror,
  `DelegateCheckProbeTests.a_status_probe_leaves_the_git_index_untouched`: its control (a bare
  `git status` rewrites the index) fails identically at task base `27e3e3f7f`; it touches no
  deadline code.
- **Mutation total under option B:** 31 method-scoped cycles, all pending for SourceLanding.

## Final Review repairs R1-R3 (Review 9b356254, Code e96821a4)

The Final Review of `5182f31a` found three defects. This section records the repairs and
supersedes any earlier statement here that disagrees.

- **R1, queued-only input earned boot protection.** `BootReplyWatch.LoadBootTurnAsync` counted
  a `QueuedUserPrompt` as a real prompt, and the S1 fallback attached boot facts to every
  surfacing verdict, so a task whose only input was queued (never accepted) stayed open past
  its role ceiling with a `BootStallNeedsOperator` warning; at base it failed by the ceiling.
  Repair: `BootTurn` now also carries the latest ACCEPTED prompt (a non-housekeeping
  `UserPrompt` that is not an interrupt marker), and `BootStallPolicy.Facts` returns null
  without one, so the task boot episode, its key, its due times and its protection exist only
  for an accepted prompt. A queued row neither opens nor advances an episode; a later accepted
  prompt does. The session-scoped `BootReplyWatchdogService` watch and `DelegateCheckProbe`
  keep reading every real prompt, deliberately unchanged (CP-24 `BootReplyWatchTests` still
  pins the queued-prompt watch argument). No new statement: the accepted prompt comes out of the
  same prompt-row query.
- **R2, the warning writer trusted an opaque key.** `BootStallWarningWriter.RecordAsync` checked
  only task status, attempt, session and `DispatchedAt` under the task lock. Repair: the
  episode carries its `BootStallFacts`; immediately before the insert, inside the writer's own
  transaction, the session row is share-locked with `SKIP LOCKED` (repair 2; it was `NOWAIT`, see below) and its accepted generation
  (`StartedAt`) and launch clock (`max(DispatchedAt, LaunchResumedAt)`, one shared
  `TaskDeadlinePolicy.LaunchClock` rule) must equal the decided episode's, and the boot
  predicate re-read on that clock must return the same accepted prompt sequence and timestamp
  with no model reply since. Any mismatch, a missing row, or a session row locked by a
  concurrent writer writes nothing and logs nothing above Debug: a write-locked row is skipped,
  so it reads as a missing row and is an ordinary empty result, never a database error (proved
  by V-22 under real two-connection contention; under `NOWAIT` EF Core logged the 55P03 twice at
  Error, Review 409623bd F2). Dedupe stays per episode and
  stage under the task lock; every other fault is still swallowed. Cost: a stage write adds
  four statements (session lock, Grok-rules check, model-reply EXISTS, prompt rows), at most
  twice per episode; the A-7 repeated-episode tick never reaches the writer and is unchanged.
  V-13 (S5) pins the totals.
- **R3, a false evidence claim.** The delivery-inventory row cited the CARD-1149 blocked-caller
  test as proof of the `FailAndNotifyAsync` caller note. It is a different producer. The row and
  the substitutes paragraph are corrected, and V-20 adds the missing producer-to-recipient proof
  for the failure path CARD-1151 keeps (role ceiling). The dead-session reconciler's note still
  has no recipient-level test; that is stated, not claimed. The other evidence citations in
  this note were re-read: H-6 claimed Working is always false for queued-only input and cited no
  existing witness (corrected); the substitutes paragraph cited "R-1 D3", which in this note is
  the characterization class (corrected). The remaining V/R/CP citations name methods that exist
  at this commit with the counts in the table.

Re-scan of the boot predicate and policy for unearned protection or a Working stop:

| Shape | Status |
|---|---|
| Queued-only prompt (idle, Working through an inherited row, terminal row) | Fixed (R1): ordinary policy, ceiling failure as at base |
| Interrupt marker as the only `UserPrompt` since the launch clock (e.g. after a resume) | Fixed (R1): an interrupt marker is not an accepted prompt |
| Accepted prompt, then interrupt, idle (option B consequence) | By design: DetectOnly with operator warning; the ceiling no longer resolves it |
| Task's own brief Pending while another accepted prompt is unanswered (A-2) | Unchanged, listed: DetectOnly without emission, so the overdue sweep never fails it; the delivery watchdog owns it and withholds while the session is Working. Whether A-2 should instead return such a task to the ordinary ceiling is a caller decision |
| Terminal or missing session row (A-1) | Unchanged, listed: DetectOnly without emission; the dead-session reconciler owns it (Review probe: fails below the delivery timeout; the runner-unknown Working shape stays open and visible as DeadSession) |
| Episode at NeedsOperator, reply only on the runner (tailer lag) | Unchanged, listed (A-7 design): no further pull from this sweep; the live stream or another sweep's pull ends the episode |
| A Working session failed or stopped by this path | None found. The boot branch returns before any failure; the NotBoot path is the pre-existing non-destructive failure (no stop, no release); the writer only reads and share-locks the session row |

New proofs (all `Integration` on isolated or scoped Postgres except the pure policy rows):

- V-18 (R1): `TaskDeadlinePolicyTests.C1151_Boot_facts_need_an_accepted_prompt` (6: accepted-only, queued-only-idle, queued-only-working, accepted-then-queued-refinement, queued-then-accepted, interrupt-only-after-resume); `AgentTaskOverdueDeadlineTests.a_queued_only_prompt_is_no_boot_and_keeps_the_ceiling_failure` (3: dispatched-idle, working-inherited-mid-turn, terminal-session; it uses only base symbols, so the same method runs unchanged at base `27e3e3f7f`); `BootStallDetectionTests.C1151_Only_an_accepted_prompt_is_protected` (3: accepted-no-reply-detects, accepted-then-queued-refinement, queued-only-past-ceiling); `BootStallPolicyTests` gains the `prompt-only-queued` flip and `C1151_Only_an_accepted_prompt_opens_or_advances_the_episode` (CP-2 `Min` 14).
- V-19 (R2): `BootStallDetectionTests.C1151_Warning_writer_revalidates_the_episode_identity` (5: generation-change, launch-clock-change, prompt-identity-change write nothing silently; same-episode writes once across two sweeps; concurrent-writers, meeting at the writer's lock statement, write one row).
- V-20 (R3): `DelegationDispatchRecoveryBoundaryTests.C1151_Ceiling_failure_note_has_one_complete_user_prompt` (3: eligible, busy, crash).

Class-row floors move with them: CP-2 `Min` 14, CP-22 `Min` 31, CP-23 `Min` 32. CP-26 selects
`C1149_*` only and is unchanged.

Pending positive controls for SourceLanding Mutation (method-scoped, one cycle each):

- PC-24: `LoadBootTurnAsync` treats every real prompt (queued rows and interrupt markers too) as accepted. `C1151_Boot_facts_need_an_accepted_prompt` red on `queued-only-idle`, `queued-only-working`, `accepted-then-queued-refinement`, `interrupt-only-after-resume`.
- PC-25: `BootStallPolicy.Facts` falls back to the latest real prompt when no accepted one exists. `C1151_Whitelist_requires_positive_evidence` red on `prompt-only-queued`.
- PC-26: drop the writer's generation comparison. `C1151_Warning_writer_revalidates_the_episode_identity` red on `generation-change`.
- PC-27: drop the writer's launch-clock comparison. Same method, red on `launch-clock-change`.
- PC-28: drop the writer's accepted-prompt comparison. Same method, red on `prompt-identity-change`.
- PC-29: make the revalidation always refuse. Same method, red on `same-episode`.
- PC-30: drop the dedupe read under the task lock. Same method, red on `concurrent-writers`.
- PC-31: skip the parent-note enqueue in `FailAndNotifyAsync`. `C1151_Ceiling_failure_note_has_one_complete_user_prompt` red on every argument.

Mutation total under option B with R1-R3: 39 cycles (31 + 8).

## Final Review repairs F1-F2 (Review 409623bd, Code a75e3df1)

The Final Review of `11b70758` ran all 34 S1-S3 rows (463/464; the one red, CP-37's git-index
control, fails identically at base `27e3e3f7f` and is inherited) and found two defects. This
section records the repairs and supersedes any earlier statement here that disagrees.

- **F1, a false H-7 citation.** H-7 cited V-4 `model-reply-returns-to-ordinary-policy` as a
  reply "landed through `CatchUpOverride`". That argument seeds the reply before the first
  evaluation (`AssistantAfterPrompt`) and installs no pull hook, so it never exercised a reply
  arriving during Gate 2's pull. Repair: H-7 now cites V-21, which does, and keeps the V-4
  argument as the stored-row control only. No production change: the behaviour was right, the
  evidence claim was not.
- **F2, expected contention logged at Error.** Under a held session row the writer's
  `FOR SHARE NOWAIT` raised PostgreSQL 55P03, and EF Core logged `Failed executing DbCommand`
  (20102) and `An exception occurred while iterating over the results of a query` (10100) at
  Error before the writer's Debug catch ran, contradicting R2's "logs nothing above Debug".
  Repair: the share lock is taken with `SKIP LOCKED`. A write-locked row is skipped, so the
  statement succeeds with no row, which the writer already treats as a changing identity
  (IdentityChanged, nothing written, one Debug line naming the session as missing or
  mid-update). The `55P03` catch is removed because nothing raises it any more. Lock
  semantics are unchanged: `FOR SHARE` still conflicts with exactly the locks `NOWAIT`
  refused on (an `UPDATE`'s `FOR NO KEY UPDATE`, `FOR UPDATE`) and not with `FOR KEY SHARE`
  (transcript inserts' foreign-key checks), it still never waits, and a granted lock is still
  held to the end of the writer's transaction. Genuine faults on the statement still reach
  the writer's Warning; EF logging is not filtered or lowered anywhere. Same statement count,
  so the R2 cost line and CP-17's 18/18/4 are unchanged. No migration, no setting, no new
  outcome: a held row was IdentityChanged before and is IdentityChanged now.

Fail-closed review of the change: an unknown (skipped) row writes nothing, as before;
telemetry still never feeds the disposition; nothing stops, fails or releases a Working
session; detection only.

New proofs (`Integration`, isolated Postgres):

- V-21 (F1): `BootStallDetectionTests.C1151_Reply_landing_in_the_pull_ends_the_episode` (3).
  Every argument starts from an accepted prompt 21 minutes old and no stored model row (a
  control asserts it), so the stored-row pass alone would write `BootStallNeedsOperator`.
  `fresh-reply-lands-in-the-pull`: the hook lands a reply stamped now during the pull; one
  pull, Gate 2's "the pull is what saved it" Information record, no warning, task Working with
  the same attempt, and a second sweep neither pulls nor writes. `stale-reply-lands-in-the-pull`:
  the hook lands the reply the tailer missed (prompt + 30 s); one pull, the ordinary general
  clock fails the task non-destructively (no failure code, "NOT killed"), no boot warning.
  `pull-times-out`: no hook; the production pull reaches `ListedInventoryRunner`, whose
  transcript request throws `TaskCanceledException` on an uncancelled sweep (the runtime does
  not catch an `OperationCanceledException`; the dispatcher's catch does); one runner pull, one
  `BootStallNeedsOperator`, task Working, no per-task evaluation error.
- V-22 (F2): `BootStallDetectionTests.C1151_Session_row_contention_is_quiet` (2), with the
  world's loggers captured from Debug up and the writer on its production default context (the
  dispatcher's own options, so EF Core logs through the same factory). `held-session-row`: a
  second connection holds an uncommitted `UPDATE` of the session row; a third connection's
  `FOR SHARE NOWAIT` control must raise 55P03 (the row really is held); the sweep must finish
  inside a 30-second bound, and in its window nothing is logged above Debug except EF's
  routine `CommandExecuted` (20101) records, no entry carries an exception, the writer's Debug
  line is present, no event is written and the task keeps its attempt; after rollback the next
  sweep writes exactly one `BootStallDetected`. `session-read-fault`: an interceptor throws on
  the session statement; the writer's `Could not record BootStallDetected` Warning is still
  logged, nothing is written, and once cleared the next sweep writes one event.
- Fixture: `BootStallWorld` gains `MinimumLogLevel` (default Warning, so every existing caller
  is unchanged) and structured `LogEntries()` (level, category, event id, exception);
  `ListedInventoryRunner` counts transcript pulls and can fault them.

Author red evidence (Code a75e3df1, build `bin-c1151r2/`, method filters
`C1151_Reply_landing_in_the_pull_ends_the_episode*` and `C1151_Session_row_contention_is_quiet*`,
every build and run through `build-slot.ps1`; local diagnostics, not PC discharges):

| Case | Mutation (worktree only, restored) | Result |
|---|---|---|
| held-session-row | pre-fix writer (`11b70758`, `NOWAIT`) | red: the 20102 and 10100 Error records in the window |
| held-session-row | PC-35 `SKIP LOCKED` -> `NOWAIT` | red: same two Error records |
| held-session-row | PC-36 `SKIP LOCKED` removed (blocking `FOR SHARE`) | red: `TimeoutException` at the 30-second bound |
| session-read-fault | PC-37 catch-all at Debug around the revalidation in `RecordAsync` | red: the writer's Warning is missing |
| fresh-reply-lands-in-the-pull | PC-32 Gate 2 reuses the stored-row verdict | red: no "the pull is what saved it" record (the writer's R2 revalidation alone also withholds the warning, which is why the witness pins Gate 2's record) |
| stale-reply-lands-in-the-pull | PC-32 | red: failed 0, expected 1 |
| all three V-21 arguments | PC-33 Gate 2's pull removed | red: pull count 0 |
| pull-times-out | PC-34 the dispatcher catch loses `\|\| !ct.IsCancellationRequested` | red: the per-task "Overdue-deadline evaluation" Error |

V-21 passes at the pre-fix writer (it witnesses unchanged behaviour); `session-read-fault`
passes there too (a control that the fix does not over-suppress).

Pending positive controls for SourceLanding Mutation (method-scoped, one cycle each):

- PC-32: in `TryFailOverdueAsync`, Gate 2 reuses `suspected` instead of re-evaluating after the pull. `C1151_Reply_landing_in_the_pull_ends_the_episode` red on `fresh-reply-lands-in-the-pull` and `stale-reply-lands-in-the-pull`.
- PC-33: in `TryFailOverdueAsync`, drop Gate 2's `CatchUpTranscriptAsync` call. Same method, red on every argument at the pull count.
- PC-34: in `AgentTaskDispatcher.CatchUpTranscriptAsync`, narrow the catch to `ex is not OperationCanceledException`. Same method, red on `pull-times-out`.
- PC-35: in `BootStallWarningWriter.SameEpisodeAsync`, `SKIP LOCKED` -> `NOWAIT`. `C1151_Session_row_contention_is_quiet` red on `held-session-row`.
- PC-36: in `SameEpisodeAsync`, drop `SKIP LOCKED` (a blocking share lock). Same method, red on `held-session-row` at the 30-second bound.
- PC-37: in `BootStallWarningWriter.RecordAsync`, swallow any revalidation exception at Debug as IdentityChanged. Same method, red on `session-read-fault`.

Mutation total under option B with R1-R3 and F1-F2: 45 cycles (39 + 6).

## S4 as built (Code 2a3aa29c)

- **Attention row.** `AttentionService` reads the deadline verdict once per task at a new arm
  8b, after UncorrelatedReport and before PastExpectedIdle, ProgressStalled and the generic
  Overdue arm (which reuses that verdict). A verdict with `Boot` facts yields the Overdue boot
  row: Warning before `OperatorDueAt`, Error from it (inclusive); actions OpenDrawer/Reply/Cancel;
  `SinceUtc` is the prompt time; the evidence lines are not excerpted (operator sentence, detection
  only, prompt sequence and age, boot and operator due times, any general/ceiling breach). The
  generic arm's BootModelWait wording is deleted: that kind needs boot facts, so it is unreachable
  there. Earlier arms (CommitRecoveryPending, DeadSession, NeverStarted, CardClosedWhileWorking,
  BriefUndelivered, ReportUnsettled, UnmarkedWaiting, UncorrelatedReport) still win. No enum,
  client, endpoint or migration change.
- **Sentence three is the option-B form.** The plan's option-A sentence ("Only positively
  non-Working, absent and terminal evidence permits the narrow existing boot failure/retry
  outcome ...") describes the outcome option B deleted, so V-17 pins this instead, in
  `docs/session-runtime-invariants.md`: "No boot evidence permits an automatic failure or retry:
  under option B the narrow automatic boot failure/retry is retired, a terminal or missing session
  row stays with the dead-session reconciler's existing policy, a Pending brief stays with the
  delivery watchdog, a human Retry is the only retry, and S1's pristine absent-launch hold still
  takes precedence." Pins: `C1151_Listed_or_unknown_session_is_untouched`,
  `C1151_Explicit_retry_retains_operator_semantics`, `C1149_Absent_launch_is_blocked_with_original_input`.
  Sentences one, two and four are verbatim; the loop doc carries one and two. Every pin is a
  `nameof`. `docs/agent-kinds.md`'s "is what ends this" is corrected too.
- **Author red evidence** (local diagnostics on `bin-c1151s4mut/`, not PC discharges; each
  restored): docs D1 option-A sentence three restored, D2 the old kill/retry promise re-added to
  the loop section, D3 a pin removed: V-17 red each time. M1 arm 8b removed (falls to the generic
  row): V-11 red 5/5. M2 the old kill/retry sentence added to the boot evidence: V-11 red 5/5 at
  `kill`. M3 Error only after `OperatorDueAt`: V-11 red on `operator-20m` only.
- **PC-11 and PC-22 stay pending** for SourceLanding Mutation as written above; PC-11's second
  variant (require a persisted Warning) has no author probe here.

## S4 repair (Code cc45ec44, Review 04808159 F1/F2)

- **F1.** `docs/agent-kinds.md`'s Grok note promised "nothing ends it automatically" for any
  session. It now scopes that to a session owned by an open delegate task and names the taskless
  AlwaysOn exception: `BootReplyWatchdogService` still raises and stops such a session for the
  standing-agent restart ladder (CARD-1156). The runtime owner states the same exception.
- **F2.** The operator threshold is what `BootStallPolicy.Facts` computes:
  `promptAt + max(bootWait, OperatorWait(modelWait))` with `bootWait = boot > 0 ? boot : 0` and
  `OperatorWait = modelWait > 0 ? modelWait : 20` (`BootStallPolicy.cs` `OperatorWait` and
  `Facts`, the `bootWait`/`operatorWait` lines). With model wait disabled it is
  `max(positive boot wait, 20 minutes)`, not 20. The `ModelWaitDeadlineMinutes` comment, the
  `DefaultOperatorMinutes`/`OperatorDueAt` comments and all three owner docs now say so; the
  plan's sentence one ("20 minutes with defaults") stays verbatim because it is true.
- **V-17 extended, still one execution (CP-12 unchanged).** It pins the delegate-scoped and
  AlwaysOn sentences, the threshold sentence in the runtime, loop and agent-kinds docs, the
  absence of the old Grok sentence and of the old settings sentence, the settings formula, and
  the formula itself against `BootStallPolicy.Facts` (8/20 -> 20, 8/0 -> 20, 30/0 -> 30,
  0/0 -> 20). Author red evidence (doc/comment text only, each restored): settings false sentence
  restored, old Grok sentence restored, AlwaysOn exception removed, loop formula removed, runtime
  formula replaced by "20 minutes", settings formula removed: V-17 red each time at the matching
  assertion. Production mutation of the formula belongs to SourceLanding Mutation.

## S5 as built (Code 739f831c)

Tests only: no production file, setting, migration or runner change; no restart. The four S5
skeletons are implemented on the shared `BootStallWorld`, which gains one option,
`Interceptors` (registered on the scoped context's options; the warning writer's default
context is built from the same options, so a counter there sees the telemetry statements too).
The original 18/18/4 `C1149_C1150_Statement_budgets` arguments are untouched.

- **V-10** `C1151_Brief_and_spill_remain_byte_identical` (3): the world's Sent brief row
  (`inline`), the same row as a spill pointer with `RemoteSpillBody` and the spill file under the
  session's `.antiphon/inbox/` (`spilled`), and a Pending Ui row behind it
  (`pending-ui-followup`). Four real `TickAsync` calls (detection, repeat, operator stage at
  `promptAt + 20`, a recreated provider); every queue row's identity fields and body/spill
  SHA-256 plus the file bytes are compared before and after; runner Inputs 0; one Detected and
  one NeedsOperator. It uses `BootStallWorld` rather than `BridgeQueueHarness` because the world
  already carries the real queue service and a fake clock that reaches the operator stage.
- **V-12** `C1151_Detection_does_not_release_or_park` (2): a real tick at 9 minutes and at
  `promptAt + 20`, each followed by a `BootReplyWatchdogService` sweep, parking off and on. No
  `AgentTaskParks` or `RunnerSeatReleases` row, Releases 0, stopper empty, agent still bound, the
  watch acts on nothing and raises no `LivenessProbeFailed`.
- **V-13** `C1151_Boot_branch_statement_counts` (8, signature `(path, expected, expectedPulls)`):
  the whole `FailOverdueTasksAsync` on one `FullCommandCounter`, measured and pinned:
  young-preview 9/0 pulls, working-first-detection 28/1, working-repeated-episode 13/0 (A-7),
  operator-escalation 28/1, identity-changed-before-event 22/1 (the writer stops at its task
  lock), event-save-fault 28/1 (the attempted insert counted), non-working-listed 28/1,
  absent-proof-refused 28/1; inventory reads 0 on every path. Decomposition: open-task list 1 +
  first evaluation 8 = 9; gates 1/1b 2, ownership 1, A-7 key read 1 = 13; pull (empty persist,
  0 SQL) and second evaluation 8 = 21; writer task lock 1, dedup read 1, session share lock 1,
  boot turn 3, insert 1 = 28. Against the design table's caps: the post-evaluation boot work is
  ownership 1 + key read 1 + writer 7 = 9 on a stage write, above the plan's option-A cap of 3
  because R2 (repair 1) added the four-statement revalidation, as R2's cost line already records;
  the repeated episode is 13 with no pull, which is the design's 10 + 2 plus the open-task list
  the design's prefix leaves out.
- **V-15** `C1151_Delivery_watchdog_stopper_requires_real_safe_failure` (3), on the real
  `FailNeverStartedAsync` with a seeded `BootStallDetected` Warning: idle + Pending brief fails
  and kills once; Working + Sent brief fails with the D9 withhold (a Pending brief on a Working
  session is deferred whole by D8 and never reaches the cleanup condition, so this argument uses
  a typed brief); a refused Failed write (injected `DbUpdateConcurrencyException`) throws, fails
  nothing and kills nothing. The skeleton's "Failed, no kill" for Working is kept; its
  "the same with a Working transcript" is corrected to the Sent brief for that reason.
- **CP-46** reruns the registry guard (CP-38) on the S5 build, so `--after S5` selects
  CP-14, CP-15, CP-16, CP-20 and CP-46.

## Assertion reversals

Every existing test that encodes today's stop, with its flip. No assertion is weakened or
deleted silently: each old assertion is replaced by its explicit negation or by a stronger
positive, under a comment that names CARD-1151 and the reason.

1. `BootStallWorkingTickCharacterizationTests.Aged_prompt_only_Working_tick_stops_the_session_and_requeues_once` (`:29`) becomes `Aged_prompt_only_Working_tick_detects_without_stopping_or_requeueing`. Comment: "CARD-1151 deliberately reverses the CARD-1149/1150 characterization: boot silence is detection; the original stop/requeue assertions are replaced by positive same-attempt/same-session assertions." Fixture unchanged (nine-minute prompt, quiet available workspace, listed matching generation, boot wait 8, `MaxConcurrentTasks` 0). Flips: `SkippedConcurrency` 1 to 0 (no Queued retry exists, so the claim loop sees no row; cap 0 keeps the shape); `Stopper.Killed` `[SessionId]` to empty; status Queued to Working; attempt 2 to 1; `AgentSessionId` null to `SessionId`; `FailureCode` ProviderUnresponsive to null; `FailureReason` contains "killed" to null; session Stopped to Running. Added: exactly one Warning event whose Detail starts with `BootStallDetected`; zero Failed/Retried events; zero `ProviderUnresponsive` incidents; zero `ModelAvailabilityHolds`; the Detail excludes the prompt canary; a second `TickAsync` and a tick from a freshly built provider add no event; the delegate transcript still has exactly one prompt; runner counters as before. The class comment drops "This is today's behaviour".
2. `AgentTaskOverdueDeadlineTests.a_boot_stall_fails_with_the_code_kills_the_session_and_retries_once` (`:481`) becomes `a_working_boot_stall_warns_without_failure_stop_or_retry`. Keeps the Working transcript, the parent session and the original input. Flips: `FailureCode` ProviderUnresponsive to null; reason "Provider never answered"/"is being retried once" to `FailureReason` null; status Queued to Working; attempt 2 to 1; `stopper.Killed` contains to empty; incident exists to none; the parent note containing "Provider never answered" to zero Delegation rows on the parent for this task. Added: one Warning event (Detected, and NeedsOperator because the fixture's 45,000-minute age is past the operator threshold too). The safe-absent failure/retry assertions move to V-7 under Q-1 option A, never into a "Failed status means idle" fixture.
3. `a_second_boot_stall_on_the_same_task_fails_without_retrying` (`:543`) becomes `a_second_boot_episode_on_an_exhausted_attempt_still_only_detects` (attempt 2, Working). Flips: Failed to Working; attempt stays 2; `FailureCode` to null; reason "is NOT being retried" plus alias to null; stopper contains to empty. Added: one Warning. Exhaustion of a proven-absent attempt is V-7 `exhausted-attempt-fails-once` (option A), because the `OverdueSweepHarness` has no owning-inventory seam.
4. `the_first_boot_stall_never_holds_the_model_but_the_second_does` (`:568`) becomes `repeated_boot_detections_never_hold_the_model`. The first-scenario "no hold" assertion is kept; the second scenario's `hold.ShouldNotBeNull`, `Source`, `Reason`, `DisabledUntil` block is replaced by "no `ModelAvailabilityHolds` row for the alias and no `ProviderUnresponsive` incident on either session"; the `finally` cleanup is kept. Under option A the hold-on-second-safe-absent-failure control is V-7's fourth argument `second-absent-failure-holds-alias` (CP-8 `Min` 4); under option B the alias hold on boot stalls retires.
5. `a_session_that_produced_one_thinking_row_takes_the_old_non_killing_failure` (`:521`) is kept verbatim (R-deadlines). `a_boot_stall_whose_workspace_shows_progress_is_neither_killed_nor_failed_early` (`:617`) is kept and strengthened with "zero Failed events" and "one Detected Warning".
6. `Aged_Working_tick_with_an_assistant_row_is_not_stopped` and `Young_prompt_only_Working_tick_is_not_stopped` are kept and strengthened: zero boot Warning events, delegate transcript prompt count unchanged, runner `Inputs` 0.
7. `GrokDelegateEndToEndTests.a_provider_that_never_answers_the_boot_prompt_is_failed_killed_and_retried_once` (`:357`) becomes `a_provider_that_never_answers_boot_is_detected_until_explicit_retry`. Keeps the real submit, the confirmed pointer prompt, the spill file, the check digest (`BOOT TURN`, `DEADLINE: PAST BootModelWait`) and the double-jeopardy control. After `FailOverdueTasksAsync` (run twice): status stays Dispatched (F-3) with the same session id; `FailureCode` null; attempt 1; no incident; the `RecordingSessionStopper` is empty; one Detected Warning. Then the test clears `ANTIPHON_FAKE_NO_TASK_REPLY`, calls `AgentTaskService.RetryAsync` explicitly, and only then asserts the stopper contains the first session, a fresh second session, and the existing Succeeded settle at attempt 2. The comment attributes the stop to that request.
8. `BootReplyWatchdogTests.a_session_owned_by_an_open_delegate_task_is_left_to_the_deadline_sweep` (`:203`) keeps its assertion; its comment's "fails the task, kills the session, retries once" is corrected to "detects and warns" (S4 comment pass), and the production comment at `BootReplyWatchdogService.cs:170-176` likewise.

## Restart order and serialization

Activation after Code, Review and Land: **AppHost only**. No runner binary or protocol
changes (D-5); the directory, inventory and transcript DTO fields the adapter reads exist on
every runner. Order: `git pull --rebase` in the main checkout, then
`pwsh -NoProfile -File scripts/restart-apphost.ps1` from the canonical checkout by the
orchestrator, never from this worktree; exit 0 requires `/api/version` SHA equal to
source-root HEAD; check `logs/apphost.restart.lock` and `logs/apphost.launch.lock` first
and inspect exit 3 before retrying. Documentation and test-only slices (S5, S6) need no
restart. Activation check, separate from health: `GET /api/version` SHA, then one aged
prompt-only delegate on a FakeClaude/FakeGrok lane shows the Overdue boot row with the
detection wording and no Failed event after eight minutes; a production delegate must not be
used as the probe.

Serialization of source ownership, observed 2026-10-08 (UTC) on `origin`:

| In-flight work | State | Rule for CARD-1151 |
|---|---|---|
| CARD-1149/1150 S2 repair 2, Code `44fc0e5a`, branch `feat/card-task-44fc0e5a` | Nine commits ahead of master (latest `c951feac9`), not landed. Edits `AgentTaskDispatcher.cs` at `:5655`, `:6060`, `:7739`, `:7768` (cold dispatch and brief ensure), `DispatchBriefEvidence.cs`, `SessionMessageQueueService.DispatchBrief.cs`, `AgentSessionService.cs`, and adds seven `DelegationDispatchRecoveryBoundaryTests.Brief*.cs` partials; `C1149_C1150_Statement_budgets` still has three arguments on that branch | CARD-1151 does not edit those three service files. Its dispatcher hunks (`:3140-3312` boot tail, `TryFailOverdueAsync`) are disjoint from the S2 hunks, so a rebase merges cleanly, but S1 and S3 must not start while a Code task holds the `AgentTaskDispatcher.cs` scope; Code re-reads line numbers on its baseline. The new partial `DelegationDispatchRecoveryBoundaryTests.BootStall.cs` uses `BootStall`-prefixed helpers only, so it cannot collide with the S2 partials' `CurrentBrief`, `SeedCurrentAsync`, `OpenQueueAsync`, `AssertRetainedAsync` and the rest. CP-17 (`C1149_C1150_Statement_budgets*`) runs whatever arguments are on master at Code's baseline. |
| CARD-1153 S4, gated | No branch carries an S4 dispatcher edit yet (S1-S3 and F1-F3 repairs on `1a174347`, `4a4aacaa`, `809c8b49`, `a086fe80` touch the runner, contracts and infrastructure only) | S4 shares `AgentTaskDispatcher.cs` (`DecideAbsentLaunchAsync`, `ReadAbsentLaunchEvidenceAsync`) with no overlap in the boot tail. Whichever lands second rebases; neither overwrites the other's evidence predicates. When S4 lands, rerun CP-18, CP-19 and CP-26 here as a commissioned lane. |
| `AgentTaskService.cs` | Not edited on any in-flight branch observed | S3's internal requeue (option A) needs a scope occupancy check at dispatch time; under option B `AgentTaskService.cs` is untouched. |
| `AttentionService.cs`, `TaskDeadlinePolicy.cs`, `BootReplyWatch.cs`, `BootReplyWatchdogService.cs`, `DelegationSettings.cs` | Not edited on any in-flight branch observed | Free for S1 and S4. |

## Statement budgets

`DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets` (`:24-46`) measures
every DbCommand through `FullCommandCounter` on a `BridgeQueueHarness` tick (held Dispatched
and young Working, 18 each) and on `SessionReconciliationServiceTests.BuildService` inside
the grace (4). It remains the decisive witness and stays measurable because:

- Both measured ticks seed a one-minute-old task. `TaskDeadlinePolicy.EvaluateAsync` returns at the cheap gate (`:145-146`) before any query, because one minute is under 6.4 minutes; D-1's boot facts are loaded after that gate, inside the same existing queries (`LoadBootTurnAsync` already returns `PromptSequence` and `PromptAt`; `LaunchClockAsync` already returns the clock; `bootDueAt` is `PromptAt + bootWait`), so nothing is loaded twice and nothing moves ahead of the gate.
- The warning writer, the inventory read, the transcript adapter and the workspace probe sit behind `suspected is { Breached: true }` (`:3097`) and the gates; a young task never reaches them.
- The inside-grace scan is `SessionReconciliationService`, which CARD-1151 does not edit.
- PC-13 adds one SELECT ahead of the gate and one before the inside-grace return and must turn 18 into 19 and 4 into 5.

Designed counts for the new paths (V-13 arguments). The inherited prefix is enumerated from
the baseline source for a breached prompt-only boot task inside `TryFailOverdueAsync`;
Code measures and pins the exact totals, printing each roster. The caps are per path after
the inherited prefix; the whole-tick totals include the rest of `TickAsync` (18 at the young
baseline, fewer sweeps active for an aged task is not assumed).

| Path | Inherited prefix inside the overdue sweep (baseline) | Cap on new SQL | Runner calls cap | Note |
|---|---|---|---|---|
| young-preview | 0 for a young task; for a preview-age task the first `EvaluateAsync` (GrokRules 1, last entry 2, Working 1, launch clock 1, boot turn 3 = 8) | 0 | 0 | Projection is read-only; facts travel out of the existing queries |
| working-first-detection | first evaluation 8, Gate 1 ApiErrorRecoveries 1, Gate 1b obligations 1, Gate 2 pull (empty persist 0 SQL), second evaluation 8, Gate 3 transcript AnyAsync 1 and CountAsync 1 = 20 | 3 | 0 beyond the inherited pull | task lock plus identity read (one `FOR UPDATE` select), duplicate-key read, one insert; no inventory on true Working |
| working-repeated-episode | first evaluation 8, Gate 1 and 1b 2 = 10 (A-7 skips the pull, the second evaluation and Gate 3 for a recorded stage) | 2 | 0 | lock and duplicate read, no write |
| operator-escalation | as first detection, 20 | 3 | 0 beyond one pull | second stage event |
| identity-changed-before-event | as first detection, 20 | 1 | 0 | the lock read refuses before dedup and insert |
| event-save-fault | as first detection, 20 | 3 | 0 | the attempted insert is counted; the context is disposed; a publish fault adds no SQL |
| non-working-listed | 20 | 5 | 1 inventory | one loaded session binding, one fresh Working query, at most three warning commands |
| absent-proof-refused (option A) | 20 | 8 | 2 inventory, 1 transcript | prior five plus at most three terminal/ownership/evidence reads; warning only |
| safe-absent-final-revalidation (option A) | 20 | 10 | 2 inventory, 1 transcript | at most eight evidence reads plus task-lock identity and final Working read; the existing failure/requeue transaction's statements reported separately; no telemetry writes |
| safe-absent-race-abort (option A) | 20 | 10 | 2 inventory, 1 transcript | same observations, no terminal writes |

Per-tick effect on the desktop: ordinary ticks unchanged; a detected unresolved task costs
10 plus 2 statements and no runner call per tick between stage boundaries (A-7), against
today's 20 plus one HTTP call per tick for a breached task that was about to be failed.
Without A-7 it would be 20 plus 2 and one HTTP call per tick indefinitely, which is the
reason A-7 is required rather than optional.

## Verification design

### Inspection

Bodies read for this design (baseline `65745cfa3`):

- `AgentTaskDispatcher.TickAsync` sweep order (`:330-440`), `FailOverdueTasksAsync` (`:3022-3060`), `TryFailOverdueAsync` gates 1-4 (`:3088-3193`), `TryFailBootStallAsync` (`:3211-3312`), `BootStallLedgerKey`/`TryHoldOnBootStallRepeatAsync` (`:3322-3372`), `CatchUpTranscriptAsync` and the seams (`:3378-3440`), `ProbeWorkspaceAsync` (`:3639-3647`), the delivery watchdog arbitration and conditional stopper (`:2137-2320`), `FailDeadSessionTasksAsync` (`:2480-2653`), `DecideAbsentLaunchAsync` (`:2679-2730`), `ReadAbsentLaunchEvidenceAsync` (`:2733-2785`), `ReadAbsenceAsync` (`:2792-2840`), `TryHoldAbsentLaunchAsync`/`HoldUnderLockAsync`/`DiscardUncommittedHoldAsync` (`:2857-2980`), `FailAndNotifyAsync` (`:3939-4000`), the constructor's optional collaborators (`:131-191`).
- `TaskDeadlinePolicy` whole (`:1-321`); `BootReplyWatch` whole (`:1-306`); `BootReplyWatchdogService` stand-down, `RaiseAsync` and `StopHungStandingSessionAsync` (`:150-340`); `AttentionService` constructor (`:132-162`) and the ProgressStalled/Overdue/ChecksSpent projection (`:1190-1300`); `CheckCompactionContinuationService.DiscoverSeatAsync` and `BeginStopAsync` (`:110-150`, `:245-335`).
- `AgentTaskService.RetryAsync` (`:2682-2735`), `RequeueAsync`/`RequeueCoreAsync` (`:3413-3480`), `StopDelegateAsync` (`:3932-3945`), `RemoveEphemeralAgentAsync` (`:3572-3590`), `AddEvent` (`:4687-4696`, Detail clamped to 4000). `AgentTaskReplyService` Working promotions (`:434`, `:1547`, `:1648`, `:1763`).
- `SessionMessageQueueService.IsWorkingAsync` (`:5087-5092`); `TranscriptWorkingStateQuery` whole (end and activity predicates); `AgentSessionRuntime.CatchUpTranscriptAsync` (`:706-720`) and the empty persist (`:836`); `AgentSessionService.WriteRestartBoundaryIfInterruptedAsync`; `AgentTaskLiveness.IsDeadSession`/`ClassifyFailure` (`:60-130`); `SessionReconciliationService.RunnerUnknownSessionReason` (`:41`); `CommitRecoveryObligations.LoadUnresolvedAsync` (`:53-60`); `DelegationSettings` boot 8 (`:452`), repeat hold 30 (`:462`), model wait 20 (`:415`), local 90 (`:475`), default ceiling 240 (`:401`), dead-session grace 3 (`:571`); `BlockedTaskParkingOptions.Enabled` default false; `AgentTaskEventType.Warning` (12); `AttentionAction`; `SessionRunnerSessionDto`/`SessionRunnerTranscriptDto` (`TerminalComplete`, `AcceptedStartedAt`); `ISessionRunnerDirectory`, `RunnerInventory`, `SessionRunnerBinding`; `SessionGeneration`.
- Runner: `SessionRunnerRuntime.List` (`:810`), `GetTranscript` (`:880`), the `_sessions` lookups (`:206-1045`); the three tailers' `Snapshot()`; `TranscriptKinds.InterruptedPromptPrefix` (`:552`), `TranscriptPromptSpan.IsHousekeepingPrompt` (`:151-156`).
- Fixtures: `BootStallWorkingTickCharacterizationTests` whole (its `BootStallWorld`, `ListLog`); `AgentTaskOverdueDeadlineTests` boot section (`:478-642`) and `Scenario` (`:895-1104`); `OverdueSweepHarness.Create`; `GrokDelegateEndToEndTests` boot witness (`:337-603`), `BuildHarness` (`:724-810`); `DelegationDispatchRecoveryBoundaryTests` partials (`.cs` budgets and `MeasureTickAsync`/`MeasureScanAsync`, `.AbsentLaunch.cs` `SeedAsync`/`OpenSweep`/`SweepHost`/`CountingRunner`/`UnavailableDirectory`/`BlockedSaveFault`/`AttemptBump`, `.AbsentLaunchWhitelist.cs`, `.AbsentLaunchRepair.cs`, `.AbsentEvidence.cs` skeleton style) and the seven `Brief*.cs` partials on `feat/card-task-44fc0e5a`; `BootReplyWatchdogTests.Scenario`; `AttentionServiceTests.Scenario`, `ItemsForAsync`, `BuildService`; `ListedInventoryRunner`, `RecordingSessionStopper`, `FullCommandCounter`, `StubWorkspaceProgressProbe`, `BridgeQueueHarness` head, `DelegationTestServices`, `TestDbFixture` API, `Card1153Pending`; `SessionRunnerAbsenceEvidenceDocumentationTests` and `RepairSourceDocumentationTests.FindRepoRoot`.
- Cross-card artifacts: the CARD-1149/1150 test design (guard inventory, D-7, PC-19) and the CARD-1153 test design (format, R-6/CP-29, serialization table); `docs/testing-and-build.md` checkpoint manifest, runner tool, build slots, combined filters and mutation execution sections; `docs/session-runtime-invariants.md:602-610`, `:770`; `docs/orchestration-loop.md:1089-1098`.

Boundaries and where they are proved:

| Boundary | Where |
|---|---|
| A Working, listed, same-generation boot stays Working with one Warning and zero destructive calls across repeated ticks | V-1 |
| Every whitelist condition is individually required; unknown is never admitted | V-2 |
| Listed, unknown, terminal-row, Pending-brief and Sent-brief Dispatched shapes | V-3 |
| No other clock terminalizes an unresolved boot; a reply returns to ordinary policy | V-4 |
| Operator threshold, restart and clock steps keep the attempt | V-5 |
| Telemetry faults never change the disposition or leak | V-6 |
| Proven absence keeps the kernel (option A) | V-7 |
| The final barrier revokes a stale failure (option A) | V-8 |
| One event per key and stage, across ticks, processes and contexts | V-9 |
| Brief and spill bytes and queue identity through detection and escalation | V-10 |
| The attention row says detection, never kill/fail/retry, and resolves | V-11 |
| No park, no release, session watch stands down | V-12 |
| Statement and runner-call rosters for every new path | V-13 |
| S1 hold and different-reason failure untouched | V-14 |
| Delivery watchdog stopper stays conditional | V-15 |
| Explicit Retry keeps its stop; bypass unreachable | V-16 |
| Owner documents | V-17 |
| Inherited claim, launch, resume, overdue, dead-session, delivery, compaction, session-watch and attention invariants | R-1..R-16 |

Missing setup recorded for Code:

- A-11's shared `BootStallWorld` helper with a `FakeTimeProvider` (the characterization uses `TimeProvider.System` and back-dated rows; V-5 and V-9 need an advancing clock), an owning-inventory directory stub (`ISessionRunnerDirectory` with `GetInventoryAsync` returning `Available`, `Unavailable`, throwing, or a null list; `GetBindingAsync` returning `Local`, `Remote(owner)` or `Missing`), and interceptor hooks (a `DbCommandInterceptor` seam for V-8's barrier and V-6's faults, in the `AttemptBump`/`BlockedSaveFault` shape).
- V-6 needs the telemetry writer to accept an injectable context factory or an interceptor on its own options so that read, insert and commit faults can be injected without touching the sweep's context; the publish fault uses a throwing `IEventBus`.
- V-13 registers `FullCommandCounter` on every `DbContextOptions` the graph builds, including the telemetry writer's; Code must not construct a context whose options bypass the counter.
- V-7 (option A) needs the directory stub plus `CountingRunner.ReadTranscript`-style transcript scripting on `ListedInventoryRunner`; a `ReadTranscript` func is added to that helper in S1.
- V-15 reuses the delivery watchdog fixtures of `AgentTaskDeliveryWatchdogTests` (Slow category); the three arguments live in `BootStallDetectionTests` with their own isolated schema so the class is not whole-run.
- V-11 reuses `AttentionServiceTests.BuildService` with a `FakeTimeProvider`; the five existing Overdue methods are a separate regression row (CP-13).
- V-17 reads both owner documents by `FindRepoRoot` and asserts the four plan sentences verbatim plus the absence of "kills the session (it produced nothing" and "retried once" in the two changed sections.
- New classes are `Unit` (`BootStallPolicyTests`, `BootStallDocumentationTests`) or `Integration`; none is `Slow`, so `slow-tests-allowlist.txt` is unchanged.

Excluded: a database migration, a new per-tick query ahead of the age gate, parking
enablement, any automatic stop of a Working session, the taskless AlwaysOn stop in
`BootReplyWatchdogService` (separate card), the pool-release sweep's own policy, a
certificate call from the boot sweep, and widening `CatchUpTranscriptAsync`'s bool contract.

### Delivery inventory

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt (durable identity) |
|---|---|---|---|---|---|
| Boot Warning event | Overdue sweep's telemetry writer, after the disposition is selected | `AgentTaskEvents` row, `Type = Warning`, Detail reason token plus key | Separate short-lived context: task lock, key/stage read, episode revalidation (generation, launch clock, accepted prompt; R2), insert, commit | Next tick re-reads the key; a failed write leaves nothing and is retried by the next tick (V-6) | The row read back by episode key and stage from a fresh context after one, two and N ticks and after a process restart (V-5, V-9); the publish is not evidence |
| Change notice | `PublishToAllAsync("AgentTaskChanged")` after the commit | Event bus | After the commit | Logged only | Not delivery evidence; excluded |
| Attention row | `AttentionService` projection from current boot facts | HTTP read model | None (derived) | None needed | The row from `GetAsync` with the expected kind, severity, actions and wording, and its absence after a reply (V-11); never a persisted Warning |
| Safe-absent requeue (option A) | Internal attempt-bound requeue after the final revalidation | `AgentTasks` row Queued, attempt+1, Retried event | The existing requeue transaction under the queue gate | A requeue fault retains Failed and warns; no stopper, no kill compensation (V-7) | Task row at attempt 2 with the Retried event and the same kind/tier (V-7); a Queued row is not a dispatched session, and no claim is asserted |
| Parent completion note (role-ceiling failure path; the option-A boot failure that this row first described is struck) | `FailOverdueTasksAsync` -> `FailAndNotifyAsync` through the real queue | Parent session | Queue row committed after the Failed status | Existing queue delivery and verification | One complete matching UserPrompt in the parent transcript with the note's SourceTaskId and a Delivered verdict, eligible, busy and crash (re-attached harness), plus no second submit and nothing on a decoy session: V-20 `C1151_Ceiling_failure_note_has_one_complete_user_prompt` (CP-43, added by R3). Corrected claim: `C1149_Caller_note_has_one_complete_user_prompt` proves the CARD-1149 blocked-caller note (`HoldUnderLockAsync` -> `EnqueueBlockedParentNoteAsync`), a different producer, and is not evidence for this one |
| Delegate session input | None. Detection sends nothing to the delegate | n/a | n/a | n/a | Runner `Inputs` 0 and the delegate transcript's prompt count unchanged in V-1, V-3, V-10 |

Substitutes and what they cannot prove: a queued parent note is not a received UserPrompt
(receipt is proved per producer: V-20 for the role-ceiling failure note; CARD-1149's
`C1149_Caller_note_has_one_complete_user_prompt` for the blocked-caller note only;
`ReceiptFailureDeliveryTests` for the delivery watchdog's `FailNeverStartedAsync` caller note
and its durable DeliveryFailure obligation; `AgentTaskDeadSessionReconciliationTests` asserts
the dead-session reconciler's terminal state and queued parent text only, and no test here
proves that reconciler's note reaches a recipient); a zero direct runner kill count does not
prove no stop (the stopper, `RetryAsync`'s `StopDelegateAsync` and the watchdog's kill are
counted separately);
a `ListedInventoryRunner` is not a transport (the native S6 witness runs the real runner on
ConPTY). No design stops before recipient evidence: every event is read back from a fresh
context, and the only session input in scope is the explicit Retry of S6, whose receipt is
the existing second-session Succeeded settle.

### Proves it works now

Nothing of CARD-1151 exists at the baseline; every V-n is a Code obligation and every
skeleton is skipped until its slice lands. Diagnostic rows run at this commit are recorded
under "Diagnostic runs" below; they are not checkpoint executions.

- V-1: an aged prompt-only Working tick detects and does nothing else | real `TickAsync`, isolated Postgres | `BootStallWorkingTickCharacterizationTests.Aged_prompt_only_Working_tick_detects_without_stopping_or_requeueing` | 1 executed; Working, attempt 1, same binding, Running, stopper empty, runner counters zero, one Detected Warning, no Failed/Retried event, no incident, no hold, canary absent, second tick and fresh provider add nothing
- V-2: every whitelist condition is required | pure policy | `BootStallPolicyTests.C1151_Whitelist_requires_positive_evidence` | 15: pristine admits SafeAbsentFailure, each flip selects DetectOnly with the named reason, `model-reply-present` is NotBoot
- V-3: listed, unknown and ownership shapes are untouched | overdue sweep over the shared world | `BootStallDetectionTests.C1151_Listed_or_unknown_session_is_untouched` | 11: working-listed, working-empty-list, idle-listed-running, idle-listed-exited, idle-wrong-generation, unavailable-remote, missing-directory, null-list, terminal-row-reconciler-owned, dispatched-pending-brief, dispatched-sent-brief-detects; no outcome change, stopper empty, inventory calls zero on true Working, no boot event on terminal and Pending-brief shapes, one Detected event on the Sent-brief shape
- V-4: no other clock terminalizes the boot | same | `BootStallDetectionTests.C1151_Boot_protection_survives_all_deadlines` | 7: general-20m, ceiling-240m, custom-ceiling-earlier, model-wait-shorter-than-boot, workspace-progress, boot-notification-disabled keep the task open; model-reply-returns-to-ordinary-policy fails non-destructively at the general clock with an empty stopper
- V-5: the operator stage preserves the attempt | same, `FakeTimeProvider` | `BootStallDetectionTests.C1151_Operator_escalation_preserves_the_attempt` | 5: before-threshold, at-threshold, after-threshold, restart-after-threshold, clock-rewind-after-detection; Error at equality, one event per stage, same attempt/token/binding, no input, canary absent
- V-6: telemetry faults never change the disposition | same, interceptors | `BootStallDetectionTests.C1151_Telemetry_failure_never_changes_disposition` | 5: event-read, insert, commit, publish, later-unrelated-save; disposition unchanged, no leaked row, sweep failures zero, next tick records once
- V-7 (option A): proven absence keeps the kernel | same, contradictory runner fixture | `BootStallDetectionTests.C1151_Safe_absent_boot_keeps_failure_and_retry` | 4: first-attempt-retries-once, exhausted-attempt-fails-once, retry-infrastructure-error-stays-failed, second-absent-failure-holds-alias; stopper, kills, releases, compaction stops zero in all
- V-8 (option A): the final barrier revokes a stale failure | same, `AttemptBump`-style interceptor | `BootStallDetectionTests.C1151_Race_revokes_absent_failure` | 4: new-working-row, inventory-entry, generation-change, attempt-change; no Failed status, no Retried event, stopper empty
- V-9: one event per key and stage | same | `BootStallDetectionTests.C1151_Warnings_deduplicate_per_episode` | 4: repeated-tick, service-recreation, concurrent-contexts, later-real-prompt; exactly one row per key and stage, the later prompt makes exactly one new key
- V-10: brief and spill bytes are untouched | `BridgeQueueHarness`, real tick through 8 and 20 minutes | `DelegationDispatchRecoveryBoundaryTests.C1151_Brief_and_spill_remain_byte_identical` | 3: inline, spilled, pending-ui-followup; SHA-256 and identity equal before and after, zero new queue rows, Inputs 0, no ensure/send
- V-11: the attention row describes detection and resolves | `AttentionServiceTests.BuildService` with `FakeTimeProvider` | `BootStallAttentionTests.C1151_Attention_describes_detection_and_resolution` | 5: preview-6m24s, detected-8m, operator-20m, past-ceiling, model-reply-resolves; kind Overdue, Warning then Error, actions exactly OpenDrawer/Reply/Cancel, no "kill"/"fail"/"retried" text, works without a persisted Warning, absent after a reply
- V-12: no park, no release, watch stands down | shared world plus `BootReplyWatchdogService` | `BootStallDetectionTests.C1151_Detection_does_not_release_or_park` | 2: parking-off, parking-on; no park row, no release row, Releases 0, stopper empty, no LivenessProbeFailed incident
- V-13: statement and runner-call rosters | `FullCommandCounter` on every context | `BootStallDetectionTests.C1151_Boot_branch_statement_counts` | 10 (8 under option B): exact pins at or under the caps in the budget table, roster printed per argument
- V-14: S1 hold and different-reason failure | existing | `C1149_Different_reason_or_attempted_brief_still_uses_failure_policy` (3), `C1149_Absent_launch_is_blocked_with_original_input` (1) | unchanged outcomes, never a boot retry
- V-15: the delivery watchdog's stopper stays conditional | isolated schema, watchdog fixtures | `BootStallDetectionTests.C1151_Delivery_watchdog_stopper_requires_real_safe_failure` | 3: real-idle-failure-cleans-up, working-withholds, stale-or-unsuccessful-failure-withholds; a boot Warning never routes to the stopper
- V-16: explicit Retry keeps its stop | shared world plus `AgentTaskService` | `BootStallDetectionTests.C1151_Explicit_retry_retains_operator_semantics` | 1: stopper contains the session, Queued at the same tier, no public parameter reaches the internal requeue
- V-17: owner sentences | file read | `BootStallDocumentationTests.C1151_Docs_describe_detection_and_only_compaction_exception` | 1: the four plan sentences verbatim with their pins; no kill/retry promise left in the changed sections

### Guards the regression

- R-1: `BootStallWorkingTickCharacterizationTests` (3 after the flip). CP-21.
- R-2: `AgentTaskOverdueDeadlineTests` (29 at this baseline; the four flips keep the count, "live repeat/no-alias controls" may grow it). CP-22.
- R-3: `TaskDeadlinePolicyTests` (26). CP-23.
- R-4: `BootReplyWatchTests` (27). CP-24.
- R-5: `BootReplyWatchdogTests` (11). CP-25.
- R-6: all `C1149_*` methods in `DelegationDispatchRecoveryBoundaryTests` (49 at this baseline; more if the S2 repair lands arguments). CP-26.
- R-7: `AgentTaskDeadSessionReconciliationTests` (25). CP-27.
- R-8: `AgentTaskDeliveryWatchdogTests` (76, Slow category, allow-listed). CP-28.
- R-9: `AgentTaskConcurrencyLimitTests` (25). CP-29.
- R-10: `AgentTaskDispatcherPredicateTests` (17). CP-30.
- R-11: `AgentTaskDispatchFailureTests` (15). CP-31.
- R-12: `AgentSessionLaunchQueueOwnershipTests` (7). CP-32.
- R-13: `AgentSessionInterruptedLaunchResumeTests` (8). CP-33.
- R-14: `CheckCompactionContinuationTests` (2) and `CheckCompactionRecoveryFlowTests` (1). CP-34, CP-35.
- R-15: `AttentionServiceTests` Overdue section and precedence (5 named methods). CP-13.
- R-16: `GrokDelegateEndToEndTests` renamed native witness (1, Windows ConPTY). CP-36.
- R-17: `C1149_C1150_Statement_budgets` (3 at this baseline: 18/18/4). CP-17.
- R-18: `DelegateCheckProbeTests` (43; it calls `TaskDeadlinePolicy.EvaluateAsync` and the boot predicate). CP-37 (added with Q-1 option B).
- R-19: registry guard, `TestClassificationGuardTests` and `SlowTestTripwireTests` (3). CP-38 (added with Q-1 option B).

### Guard inventory

- G-1: true Working selects DetectOnly before any failure, retry, alias hold or cleanup; the old route is unreachable | PC-1
- G-2: each whitelist condition is individually required; unknown is never admitted | PC-2
- G-3: listed (any status or generation) and unknown inventory veto; a remote owner is never judged from the local list | PC-3
- G-4: boot identity survives the winning clock; general and ceiling never terminalize an unresolved boot; a zero boot setting keeps the protection | PC-4
- G-5: the operator threshold is derived from facts, never reset per tick; the key carries no clock | PC-5
- G-6: telemetry faults never change the disposition; the separate context never leaks | PC-6
- G-7 (option A): proven absence keeps the kernel failure and one retry; the attempt cap is honoured | PC-7
- G-8 (option A): the final recheck under gate and lock revokes a stale failure | PC-8
- G-9: the duplicate-key read runs under the task lock | PC-9
- G-10: brief and spill bytes and queue identity are untouched by detection | PC-10
- G-11: attention wording, severity and actions; independence from a persisted Warning | PC-11
- G-12: detection requests no park and no release | PC-12
- G-13: nothing runs ahead of the age gate; the inside-grace scan is untouched | PC-13
- G-14: a boot Warning is never a delivery failure for the watchdog's stopper | PC-14
- G-15 (option A): the internal absent requeue and its error path call no stopper | PC-15
- G-16: explicit human Retry keeps its stop; the bypass is unreachable from request input | PC-16
- G-17: a terminal-row session and a Pending-brief task get no boot event and no failure (A-1, A-2) | PC-17
- G-18: no runner pull and no second evaluation on a repeated recorded episode (A-7) | PC-18
- G-19: Detail never carries prompt text | PC-19
- G-20: no incident and no alias hold on detection | PC-20
- G-21: attempt exhaustion never authorizes a boot failure | PC-21
- G-22: the owner documents carry the four sentences | PC-22
- G-23: the CARD-1153 certificate is never consulted by the boot sweep | none: no evidence-reader seam exists on master (S4 is gated), so no compiling defect can be written against it yet. Review reads `BootStallPolicy.cs`, the warning writer and the boot tail for any reference to the absence-evidence reader, prepare or certify; when S4 lands, the commissioned rerun of CP-18/CP-19/CP-26 adds a PC against the then-existing seam.
- G-24: the CARD-0079 coordinator and the runner's conditional compaction stop are never called from detection | PC-23

Guards = 24, mapped = 23 to distinct PCs, justified = 1 (G-23), missing = 0, duplicate PC
maps = 0. Under option B, G-7, G-8 and G-15 are removed with their PCs (guards 21, mapped
20, justified 1).

### Positive controls

Each control is a compiling production defect, run method-scoped after land on the
SourceLanding SHA with `/*/*/<Class>/<Method>*`, baseline green, red at the named
assertion, restore, green. Zero tests, build errors, fixture failures or a different
assertion are not red. Variants in one method or file run as separate cycles; only variants
in different files and methods may batch. The SourceLanding snapshot is single; no sharding.

- PC-1 (1): restore the `FailAndNotifyAsync`/`RetryAsync` route for a true-Working boot disposition. `Aged_prompt_only_Working_tick_detects_without_stopping_or_requeueing` red at status/attempt or `Stopper.Killed`.
- PC-2 (15): in `BootStallPolicy`, treat one unknown or false condition as admitted, one per argument (`working-read-failed`, `working-true`, `session-row-missing`, `generation-mismatch`, `runner-ownership-unknown`, `inventory-unavailable`, `inventory-listed`, `terminal-evidence-missing`, `transcript-incomplete`, `workspace-not-quiet`, `launch-owner-pending`, `attempt-mismatch`, `api-recovery-unresolved`, `commit-recovery-pending`), and for `model-reply-present` classify a replied session as a boot. `C1151_Whitelist_requires_positive_evidence` red on that argument. Under option B one cycle: admit the pristine observation as a failure.
- PC-3 (3): trust the local list for a remote owner; treat a listed `Exited` entry as absent; ignore the generation on a listed entry. `C1151_Listed_or_unknown_session_is_untouched` red at an attempt/state mutation on `unavailable-remote`, `idle-listed-exited`, `idle-wrong-generation`.
- PC-4 (2): restore the model-wait fallback after a declined boot; separately discard the boot identity when the ceiling wins. `C1151_Boot_protection_survives_all_deadlines` red at Failed replacing open on `general-20m`/`boot-notification-disabled` and on `ceiling-240m`.
- PC-5 (2): recompute the operator due time from `now` on each tick; separately include the wall clock in the episode key. `C1151_Operator_escalation_preserves_the_attempt` red at the missing Error at `at-threshold`, and at a second Detected event on `clock-rewind-after-detection`.
- PC-6 (2): route a telemetry exception into the failure path; separately write the Warning through the sweep's own context. `C1151_Telemetry_failure_never_changes_disposition` red at a state change on `commit`, and at the leaked row on `later-unrelated-save`.
- PC-7 (2, option A): return DetectOnly unconditionally for a proven-absent observation; separately bypass the attempt cap. `C1151_Safe_absent_boot_keeps_failure_and_retry` red at the missing retry on `first-attempt-retries-once`, and at an excess attempt on `exhausted-attempt-fails-once`.
- PC-8 (1, option A): omit the final Working/identity/inventory recheck under the lock. `C1151_Race_revokes_absent_failure` red at a stale failure on `new-working-row`.
- PC-9 (1): perform the duplicate-key read outside the task lock. `C1151_Warnings_deduplicate_per_episode` red at extra events on `concurrent-contexts`.
- PC-10 (1): recompose or re-ensure the brief during detection. `C1151_Brief_and_spill_remain_byte_identical` red at the byte or identity compare on `spilled`.
- PC-11 (2): keep the existing "kills the session ... retries the task once" evidence sentence for the boot row; separately require a persisted Warning before projecting the row. `C1151_Attention_describes_detection_and_resolution` red at the wording assertion on `detected-8m`, and at the missing row on `preview-6m24s`.
- PC-12 (1): turn DetectOnly into a park or release request. `C1151_Detection_does_not_release_or_park` red at the park/release evidence on `parking-on`.
- PC-13 (2): add one SELECT before the cheap age gate in `TaskDeadlinePolicy.EvaluateAsync`; separately one before the inside-grace return in `SessionReconciliationService`. `C1149_C1150_Statement_budgets` red at 19 versus 18 and at 5 versus 4.
- PC-14 (1): treat a `BootStallDetected` Warning as a successful delivery failure in the watchdog's cleanup condition. `C1151_Delivery_watchdog_stopper_requires_real_safe_failure` red at the forbidden stopper call on `working-withholds`.
- PC-15 (1, option A): re-enable `StopDelegateAsync` in the internal absent requeue or restore the catch-block `KillAsync`. `C1151_Safe_absent_boot_keeps_failure_and_retry` red at a nonempty stopper on `retry-infrastructure-error-stays-failed`.
- PC-16 (1): apply the internal no-stop requeue to the public `RetryAsync`. `C1151_Explicit_retry_retains_operator_semantics` red at the missing expected stop.
- PC-17 (2): let the boot sweep act on an `IsDeadSession` snapshot; separately on a Pending-brief task. `C1151_Listed_or_unknown_session_is_untouched` red at a boot event or failure on `terminal-row-reconciler-owned` and `dispatched-pending-brief`.
- PC-18 (1): run Gate 2's pull and the second evaluation on every tick of a recorded episode. `C1151_Boot_branch_statement_counts` red at the runner call count or the statement pin on `working-repeated-episode`.
- PC-19 (1): write the prompt text into the Warning Detail. `C1151_Operator_escalation_preserves_the_attempt` red at the canary on `before-threshold`.
- PC-20 (1): write the `ProviderUnresponsive` incident, or call `TryHoldOnBootStallRepeatAsync`, on DetectOnly. `Aged_prompt_only_Working_tick_detects_without_stopping_or_requeueing` red at the incident or hold count.
- PC-21 (1): treat `Attempt >= MaxAttempts` as an admitted failure. `a_second_boot_episode_on_an_exhausted_attempt_still_only_detects` red at Failed versus Working.
- PC-22 (4): change one pinned sentence in its owning document, one per sentence. `C1151_Docs_describe_detection_and_only_compaction_exception` red; documentation control.
- PC-23 (1): call the runner's `StopCompactionContinuationAsync` from DetectOnly. `Aged_prompt_only_Working_tick_detects_without_stopping_or_requeueing` red at `Runner.CompactionStops`.

Variant total: 49 under option A (31 under option B). Excluded from mutation with reason:
the log line wording of each withhold (no assertion observes it; Review reads the reason
tokens).

### Out of scope

- V-1..V-17 bodies. Code writes them; the committed skeletons are skipped, never green.
- The taskless AlwaysOn stop in `BootReplyWatchdogService.RaiseAsync` (`:314`): recorded, not fixed, its own card.
- The pool-release sweep's policy for a session that reappears after a requeue (H-22): asserted absent from the boot branch only.
- A database migration, a new attention kind, client changes, parking enablement, a release timer, any Working stop, any certificate call, any change to `CatchUpTranscriptAsync`'s contract.
- Whole-assembly or whole-Unit runs. No production runner, provider or live broker. The Windows row runs on an isolated ConPTY lane with FakeGrok only.

### Checkpoints

Group prefix names the lane: `portable-` or `windows-`. Builds add `UseAppHost=false` off
Windows; every row is serial with `TUNIT_MAX_PARALLEL_TESTS=1` (Postgres classes, and the
native witness holds `ProcessSpawnLimit`). One build per After group; CP-36 is selected
alone with `--rows CP-36` on a Windows host, and the Linux S6 group passes no rows. Q-1 is
decided (option B, section above): CP-8 and CP-9 are struck, CP-2 selects the class (`Min` 12),
CP-16 `Min` is 8, and the regression rows plus CP-37/CP-38 run after S1-S3 on the CP-1 build.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1151-core/` | portable-working | `/*/*/BootStallWorkingTickCharacterizationTests/Aged_prompt_only_Working_tick_detects_without_stopping_or_requeueing*` | V-1 | 1 executed, 0 failed/skipped | 1 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S3 | `CP-1` | portable-whitelist | `/*/*/BootStallPolicyTests/*` | V-2, V-18 | 14 executed (12 arguments plus the stage-boundary and accepted-prompt methods), 0 failed/skipped | 14 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S3 | `CP-1` | portable-listed | `/*/*/BootStallDetectionTests/C1151_Listed_or_unknown_session_is_untouched*` | V-3 | 11 executed, 0 failed/skipped | 11 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S3 | `CP-1` | portable-deadlines | `/*/*/BootStallDetectionTests/C1151_Boot_protection_survives_all_deadlines*` | V-4 | 7 executed, 0 failed/skipped | 7 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1-S3 | `CP-1` | portable-operator | `/*/*/BootStallDetectionTests/C1151_Operator_escalation_preserves_the_attempt*` | V-5 | 5 executed, 0 failed/skipped | 5 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1-S3 | `CP-1` | portable-telemetry | `/*/*/BootStallDetectionTests/C1151_Telemetry_failure_never_changes_disposition*` | V-6 | 5 executed, 0 failed/skipped | 5 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S1-S3 | `CP-1` | portable-dedup | `/*/*/BootStallDetectionTests/C1151_Warnings_deduplicate_per_episode*` | V-9 | 4 executed, 0 failed/skipped | 4 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S1-S3 | `CP-1` | portable-explicit-retry | `/*/*/BootStallDetectionTests/C1151_Explicit_retry_retains_operator_semantics*` | V-16 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | S1-S3 | `CP-1` | portable-attention-regression | `/*/*/AttentionServiceTests/(a_mid_turn_task_closing_on_its_ceiling_is_listed_before_the_sweep_fails_it*)\|(a_task_only_part_way_through_its_ceiling_is_not_listed_as_overdue*)\|(a_breached_deadline_says_the_sweep_is_about_to_fail_it*)\|(an_idle_task_keeps_the_more_explanatory_past_expected_row*)\|(ProgressStalled_beats_Overdue_and_loses_to_PastExpectedIdle_when_idle*)` | R-15 | 5 executed, 0 failed/skipped | 5 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-17 | S1-S3 | `CP-1` | portable-hot-cost | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*` | R-17 | all listed; 18/18/4 unchanged; 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-18 | S1-S3 | `CP-1` | portable-other-reason | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Different_reason_or_attempted_brief_still_uses_failure_policy*` | V-14 | 3 executed, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-19 | S1-S3 | `CP-1` | portable-s1-hold | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Absent_launch_is_blocked_with_original_input*` | V-14 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-21 | S1-S3 | `CP-1` | portable-characterization | `/*/*/BootStallWorkingTickCharacterizationTests/*` | R-1 | all 3 named witnesses, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-22 | S1-S3 | `CP-1` | portable-overdue | `/*/*/AgentTaskOverdueDeadlineTests/*` | R-2, V-18 | all listed, 0 failed/skipped (31: the file's `AgentTaskDispatcherWiringTests` `[Test]` is another class; R1 adds 3) | 31 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-23 | S1-S3 | `CP-1` | portable-policy | `/*/*/TaskDeadlinePolicyTests/*` | R-3, V-18 | all listed, 0 failed/skipped (R1 adds 6) | 32 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-24 | S1-S3 | `CP-1` | portable-boot-predicate | `/*/*/BootReplyWatchTests/*` | R-4 | all listed, 0 failed/skipped | 27 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-25 | S1-S3 | `CP-1` | portable-session-watch | `/*/*/BootReplyWatchdogTests/*` | R-5 | all listed, 0 failed/skipped | 11 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-26 | S1-S3 | `CP-1` | portable-recovery-boundary | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_*` | R-6 | all C1149 methods and arguments, 0 failed/skipped | 49 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-27 | S1-S3 | `CP-1` | portable-dead-session | `/*/*/AgentTaskDeadSessionReconciliationTests/*` | R-7 | all listed, 0 failed/skipped | 25 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-28 | S1-S3 | `CP-1` | portable-delivery | `/*/*/AgentTaskDeliveryWatchdogTests/*` | R-8 | all listed, 0 failed/skipped | 76 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-29 | S1-S3 | `CP-1` | portable-claim | `/*/*/AgentTaskConcurrencyLimitTests/*` | R-9 | all listed, 0 failed/skipped | 25 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-30 | S1-S3 | `CP-1` | portable-claim-predicates | `/*/*/AgentTaskDispatcherPredicateTests/*` | R-10 | all listed, 0 failed/skipped | 17 | 1 | true | n/a |
| CP-31 | S1-S3 | `CP-1` | portable-launch-failure | `/*/*/AgentTaskDispatchFailureTests/*` | R-11 | all listed, 0 failed/skipped | 15 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-32 | S1-S3 | `CP-1` | portable-launch-owner | `/*/*/AgentSessionLaunchQueueOwnershipTests/*` | R-12 | all listed, 0 failed/skipped | 7 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-33 | S1-S3 | `CP-1` | portable-resume | `/*/*/AgentSessionInterruptedLaunchResumeTests/*` | R-13 | all listed, 0 failed/skipped | 8 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-34 | S1-S3 | `CP-1` | portable-compaction | `/*/*/CheckCompactionContinuationTests/*` | R-14 | all listed, 0 failed/skipped | 2 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-35 | S1-S3 | `CP-1` | portable-compaction-flow | `/*/*/CheckCompactionRecoveryFlowTests/*` | R-14 | all listed, 0 failed/skipped | 1 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-37 | S1-S3 | `CP-1` | portable-check-probe | `/*/*/DelegateCheckProbeTests/*` | R-18 | all listed, 0 failed/skipped | 43 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-38 | S1-S3 | `CP-1` | portable-registry-guard | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | R-19 | all listed, 0 failed/skipped | 3 | 2 | true | n/a |
| CP-39 | S1-S3 | `CP-1` | portable-r1-policy | `/*/*/TaskDeadlinePolicyTests/C1151_Boot_facts_need_an_accepted_prompt*` | V-18 | 6 executed, 0 failed/skipped | 6 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-40 | S1-S3 | `CP-1` | portable-r1-overdue | `/*/*/AgentTaskOverdueDeadlineTests/a_queued_only_prompt_is_no_boot_and_keeps_the_ceiling_failure*` | V-18 | 3 executed, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-41 | S1-S3 | `CP-1` | portable-r1-dispatcher | `/*/*/BootStallDetectionTests/C1151_Only_an_accepted_prompt_is_protected*` | V-18 | 3 executed, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-42 | S1-S3 | `CP-1` | portable-r2-writer | `/*/*/BootStallDetectionTests/C1151_Warning_writer_revalidates_the_episode_identity*` | V-19 | 5 executed, 0 failed/skipped | 5 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-43 | S1-S3 | `CP-1` | portable-r3-ceiling-note | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1151_Ceiling_failure_note_has_one_complete_user_prompt*` | V-20 | 3 executed, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-44 | S1-S3 | `CP-1` | portable-f1-pull | `/*/*/BootStallDetectionTests/C1151_Reply_landing_in_the_pull_ends_the_episode*` | V-21 | 3 executed, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-45 | S1-S3 | `CP-1` | portable-f2-contention | `/*/*/BootStallDetectionTests/C1151_Session_row_contention_is_quiet*` | V-22 | 2 executed, 0 failed/skipped | 2 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S4 | `tests/Antiphon.Tests -> bin-c1151-s4/` | portable-attention | `/*/*/BootStallAttentionTests/C1151_Attention_describes_detection_and_resolution*` | V-11 | 5 executed, 0 failed/skipped | 5 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S4 | `CP-11` | portable-docs | `/*/*/BootStallDocumentationTests/C1151_Docs_describe_detection_and_only_compaction_exception*` | V-17 | 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-14 | S5 | `tests/Antiphon.Tests -> bin-c1151-s5/` | portable-brief | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1151_Brief_and_spill_remain_byte_identical*` | V-10 | 3 executed, 0 failed/skipped | 3 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | S5 | `CP-14` | portable-no-park | `/*/*/BootStallDetectionTests/C1151_Detection_does_not_release_or_park*` | V-12 | 2 executed, 0 failed/skipped | 2 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | S5 | `CP-14` | portable-new-cost | `/*/*/BootStallDetectionTests/C1151_Boot_branch_statement_counts*` | V-13 | 8 executed, exact rosters printed, 0 failed/skipped | 8 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-20 | S5 | `CP-14` | portable-watchdog-safety | `/*/*/BootStallDetectionTests/C1151_Delivery_watchdog_stopper_requires_real_safe_failure*` | V-15 | 3 executed, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-46 | S5 | `CP-14` | portable-registry-guard | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | R-19 | all listed, 0 failed/skipped (S5 rerun of CP-38 on its own build) | 3 | 2 | true | n/a |
| CP-36 | S6 | `tests/Antiphon.Tests -> bin-c1151-s6/` | windows-conpty-boot | `/*/*/GrokDelegateEndToEndTests/a_provider_that_never_answers_boot_is_detected_until_explicit_retry*` | R-16 | 1 executed, 0 failed/skipped | 1 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
Roster notes. Counts are TUnit executions from `[Test]` plus `[Arguments]`; no Skip,
MethodData or Matrix in any selected class at this baseline, and no platform skip on Linux
in any portable row (`AgentTaskDispatchFailureTests`' `OperatingSystem.IsWindows()` only
sets a Unix file mode). CP-26's 49 is 1+1+5+3+3 (`.AbsentLaunch`), 6+19
(`.AbsentLaunchWhitelist`), 4+1+3 (`.AbsentLaunchRepair`), 3 (budgets); the S2 repair's
`C1150_*` methods are not `C1149_*` and are not selected. CP-13 uses the CARD-0403
per-operand form `(A*)|(B*)` and selects five methods of a `Slow` class without running the
class. CP-28's class is `Slow` and allow-listed; eight minutes is its measured class time
on server2. CP-36 runs on a Windows host only (ConPTY, FakeGrok); on Linux the method
throws `SkipTestException`, so the S6 group must not be run on Linux. Union of all rows =
V-1..V-17 and R-1..R-17.

### Cost

- Ordinary V/R floor for Code (sum of EstimatedMinutes, CP-1..CP-36) = 120 minutes, estimated: S1-S3 rows 30, S4 rows 8, S5 rows 74, Windows lane 8. Under option B (CP-8 and CP-9 struck) 114. Slot waits are outside the floor; today's diagnostic waits were 0 to 1 second per lease on this mirror.
- Setup/build = 4 isolated builds (core, s4, s5, s6 on Windows) at about 2.5 minutes each, 10 minutes estimated, already inside the row estimates that build. Build reuse avoids 32 rebuilds (every `CP-n` build cell), about 80 minutes not spent.
- PC floor (Mutation) = 162 minutes, estimated: 49 method-scoped cycles. Postgres cycles PC-1, PC-3..PC-10, PC-12..PC-21, PC-23 (30 variants) at 4 minutes = 120; attention cycles PC-11 (2) at 4 minutes = 8; pure-policy cycles PC-2 (15) at 2 minutes = 30; documentation cycles PC-22 (4) at 1 minute = 4. The plan's 102-minute floor is revised upward for the fifteen-argument table and the eight added guards. Under option B: 31 cycles, 111 minutes (26 Postgres at 4 = 104, PC-2 one at 2, PC-22 four at 1, attention 2 at 4 = 8; rounded).
- Total = 300 authoring (plan's upper estimate) + 120 ordinary + 162 PC = 582 minutes, estimated, under option A; 260 + 114 + 111 = 485 minutes under option B.

Passed the bundle check: bodies read; guards 24, mapped 23 plus 1 justified, missing 0,
duplicate PC maps 0; every PC names a compiling defect and an exact method; Cost is numeric.

## Diagnostic runs

Run on this Linux runner mirror (nested Docker, Testcontainers PostgreSQL) at the skeleton
commit `c90e2e39f1928a8ea3481d2e0bf9ea6bb315e3f9` and the following commit, through the host
build-slot gate (`slot=granted waited=0s` on every lease). They are diagnostics, not
checkpoint executions, and they prove four things: the skeletons compile and are all skipped,
the characterization class is green here and still reproduces the kill, the statement budgets
are measured at 18/18/4 by the existing row, and the checkpoint table imports.

- Checkpoint tool: `import --plan` on this file imported 36 rows with no warnings; `coverage --plan` reported `obligations=0 missing=0 unmapped=0 pcIssues=0 result=clean` over 29 selected files (the lint binds a plan's V table; this note's bullet format yields no obligations, as it did for CARD-1153), 7 minutes on this mirror.
- `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1151-td/ --property:UseAppHost=false`: 0 errors (134 s, then 153 s and 95 s incremental after the two skeleton edits).
- Skeleton discovery on that build, filter `/*/*/(BootStallPolicyTests*)|(BootStallDetectionTests*)|(BootStallAttentionTests*)|(BootStallDocumentationTests*)|(DelegationDispatchRecoveryBoundaryTests*)/C1151_*`: 80 discovered, 80 skipped with their `CARD-1151 S<n> pending` reason (15 policy; 11+7+5+5+4+4+4+2+10+3+1 detection; 5 attention; 1 documentation; 3 boundary), 0 passed, 0 failed; TUnit exit 8 (zero tests ran), which is the expected shape for an all-skipped selection.
- `CHECKPOINT TD-1 commit=c90e2e39f1928a8ea3481d2e0bf9ea6bb315e3f9 build=reused filter=/*/*/BootStallWorkingTickCharacterizationTests/* executed=3 passed=3 failed=0 skipped=0 slot=granted waited=0s dirty=1 sourceState=dirty buildSource=unknown` (60 s; `dirty=1` is this untracked note being written under the run). The inherited kill is reproduced at this baseline by `Aged_prompt_only_Working_tick_stops_the_session_and_requeues_once` passing.
- `CHECKPOINT TD-2 commit=c90e2e39f1928a8ea3481d2e0bf9ea6bb315e3f9 build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets* executed=3 passed=3 failed=0 skipped=0 slot=granted waited=0s dirty=1 sourceState=changed buildSource=unknown reason=source_changed` with `C1149-BUDGET held-dispatched-tick total=18`, `working-live-tick total=18`, `inside-grace-absent-scan total=4` (69 s). The driver exit was 2 only because this note changed on disk between its two source captures; the three measured totals are the baseline the design relies on.

Alternate outputs `bin-c1151-td/` and `bin-c1151-td-tool/` are deleted after these runs;
results stay under the ignored `.antiphon/c1151-td/`.
