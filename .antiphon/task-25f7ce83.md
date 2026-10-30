# CARD-1610 plan design review (task 25f7ce83)

Subject: `docs/superpowers/plans/2026-10-29-card-1610-failed-task-live-session-plan.md`
on `feat/card-task-c90aa6c9`, tip `7960ee532981c13d9d5f2d039acadeacfdcf84b8`
(base `f8bddb6efda7b0e8f50f3b45744087e52188bc66`). Author Codex Astra; reviewer Claude
Opus 5.5. Read-only design review on server2/Linux.

**Verdict: not sound as written. Send it back to Plan (10 defects).** The safety
invariants hold up. The chosen fix keeps the root defect in place and pays for that with
three large subsystems. It also leaves the Deploy incident class unfixed.

## Checks executed

- Card read (`card.ps1 get CARD-1610`, also CARD-1604/1606/1615).
- `dotnet run --project tools/Antiphon.Checkpoints -- import --plan <plan>` through
  `build-slot.ps1` (lease 6937b56d, waited 0 s): **imported 27 rows**, exit 0. Every
  EstimatedMinutes is a whole number. Pre-existing roster counts checked against source:
  - R-1 `an_overdue_pool_delegate_keeps_its_row_idle_while_the_session_lives` = 1
  - R-2 `TerminalSeatReleaseTests` two `[Test]` methods = 2. The class-scoped filter
    excludes the private same-name helper in `BlockedParkWireTests`.
  - R-3 `TaskDeadlinePolicyTests` two `[Test]` methods = 2
  - R-4 `C1161_Failed_caller_note_has_one_complete_user_prompt` has three `[Arguments]`
    (eligible/busy/crash) = 3
  - R-4 `C1259_admitted_task_attempt_cannot_settle_its_replacement` has two `[Arguments]`
    (false/true) = 2
  - The existing janitor test `the_janitor_retires_a_working_reading_session_silent_for_a_full_ttl`
    exists and asserts `stopper.Killed.ShouldContain(session)`
    (AgentTaskPoolTests.cs:916-925), so the plan's S1 conflict claim is true.
  - `BuildSlotScriptTests` and `BuildSlotClientTests` are both in `tests/Antiphon.Tests`,
    so CP-16/18 correctly reuse the CP-15/17 build.
- `check-evidence-diff.ps1 -Repository . -BaseRef f8bddb6ef… -HeadRef 7960ee532…`: 4 commits,
  0 entries, 0 violations, exit 0. The diff is the one plan file (+660).
  `git merge-tree --write-tree origin/master HEAD` is clean.
- No V-n tests exist yet; this is a plan. No Unit lane or integration class was required or
  run. PCs remain pending.

## Defects

**D-1 — Root cause: the chosen fix keeps "ceiling ⇒ terminal Failed while the session lives" (plan D-5/D-6/D-7; `AgentTaskDispatcher.cs:3953-3966`).**
`TryFailOverdueAsync` calls `FailAndNotifyAsync` on a ceiling breach whether or not the
session is Working. The plan keeps that and builds three compensations: an operator-bound
release (S2), a durable broker wait ledger (S3), and late-report recovery with lock-order
convergence (S4/D-9). It never evaluates the brief's "ceiling holds/asks instead of fails"
option, although the codebase already has the precedent: CARD-1151 made a BootModelWait
breach detection-only. The plan also already uses "hold the failure + attention" itself
(D-6, unknown accounting).
*Scenario:* Review 7ee3b07a at 240 min with a ToolCall 6 min old. Under hold semantics the
row stays Working. Attention raises one Error `CeilingExceeded` and the parent gets one note.
The orchestrator can use the existing `CancelAsync` on an open task, which stops the delegate
(AgentTaskService.cs:2947). It can also Reply, or leave the task running. The late report
settles through the ordinary Dispatched/Working path. The Failed+live split never arises, so
card Wanted #1, #4 and most of #2 come with no new release, ledger or recovery machinery.
Real hangs are still caught. The 20-min model-wait and 90-min local-tool phase deadlines keep
failing a stalled turn, and CARD-0153 loop detection still applies.
*Fix:* re-plan around this rule: a session that is Working at the ceiling with no breached
phase deadline gets a non-terminal escalation that is detection only. A human or the
orchestrator decides what happens next, optionally with a second, larger hard cap that does
fail. Keep S1 for the remaining Failed+live population (phase-deadline and unsafe-cleanup
failures). Justify S2-S4 only against that remainder.

**D-2 — Failure contract: the session is never told (plan D-4 "No notification sink, automatic input or stop action is added"; §Sessions waiting for input).**
The brief's contract is "a Failed task must not keep a live session OR the session must be
told". The plan does neither unless a human acts. After Failed, the session keeps working,
pushing and occupying its seat, and its report cannot settle until S4 exists.
*Scenario:* Deploy 2e4f7375 keeps executing rollout phases (drain/upgrade) for a task row
nobody owns any more. Meanwhile the orchestrator, told "Failed", plans its own recovery
against the same host.
*Fix:* for every non-destructive deadline failure, enqueue one bounded system note through
the existing session queue. It must be delivered only at TurnEnd: no mid-turn input, no
interrupt. It tells the session it is Failed, whether its report will settle, and to stop
its long-running work. Witness it with complete-UserPrompt receipt arms
(eligible/busy/crash), like C1161. If D-1 is adopted, this becomes the escalation note.

**D-3 — Ground-truth error: Retry already stops a Failed task's live session (plan ground-truth row 1, D-1, D-2; `AgentTaskService.cs:2983-3046`, `3846-3848`).**
`RetryAsync` → `RequeueAsync` → `RequeueCoreAsync` calls `StopDelegateAsync` unless the
attempt's seat release is Confirmed. Ordinary `CancelAsync` on an open Working task also
stops the delegate. The plan says no supported verb stops the session, and treats a
task-oriented stop as new "broader orchestrator kill authority" needing
`X-Antiphon-Operator-Token`. That authority analysis rests on a false premise. S2 is
stricter than two existing verbs that already stop the same session.
*Fix:* correct the table. Make the authority rule consistent with Cancel/Retry, or state
explicitly that Retry's implicit stop also becomes human-only and change it. D-7's
"has not been retried" fence should note that a retry kills the session.

**D-4 — Deploy incident not covered (plan D-5 "For Code/Review only"; `DelegationSettings.cs:372` Deploy inherits `TimeoutMinutes=240` at :1142; `scripts/deploy-server2.ps1:12` `WaitIdleMinutes = 480`).**
The rollout script's own default idle wait is twice the Deploy role ceiling. Any rollout
that uses its default wait is failed by construction. That is exactly how 2e4f7375 was
failed while alive inside a sanctioned drain wait. D-5 excludes Deploy and D-6 credits only
build-slot waits, so this incident class stays broken.
*Fix:* apply D-1 to all roles, or add a per-task declared long wait. The rollout wrapper
would announce the wait to the server, bounded and attempt-bound, and the ceiling would
honour it. Also reconcile 480 vs 240. Cross-reference CARD-1615: the Deploy task, a
Shared-workspace source writer, holds lands, and that circular wait is what stretches the
drain.

**D-5 — Late-report correction can double-run or double-land (plan D-7, §Failure-mode "Report accepted…").**
The fence is per task row (attempt/retry/replacement/cancel). But after a Failed note,
orchestrators routinely dispatch a *new* task for the same card and stage (standing rule:
failures get a Debug task).
*Scenario:* Code task A fails at the ceiling and the orchestrator dispatches Code task B for
the card. A's session finishes with `done`, `next=review`. D-7 corrects A to Succeeded and
the correction "retains the report's valid next= stage". The orchestrator dispatches from
`next=` (CARD-0146), so Review(A) and B both proceed toward land.
*Fix:* before auto-correcting, check for any newer task on the same card+role (or the same
branch lineage) created after FailedAt. If one exists, hold the correction for a decision.
A correction note should default to `next=decide`, or carry an explicit
superseded-or-not fact.

**D-6 — Blast radius and target line unstated; S3 is disproportionate (plan S3, §Rollout).**
S3 adds a durable per-attempt interval ledger to the shared build-slot broker that every
build on every host uses. It brings epochs, a 4096-interval cap, a runner proxy, server
persistence, migrations and changes to both slot clients, on top of dispatcher watchdog
changes. The plan states neither master-only nor rc+master, and gives no ordering against
in-flight CARD-1604 (Review, already on rc 8edda999) or CARD-1615 (InProgress,
Deploy/land admission). Product hosts track rc, so a master-only land fixes nothing live.
*Fix:* name the lines. Recommended: S1 visibility plus the janitor no-Working-stop guard go
to master and then get a reviewed rc port; everything else stays master-only until
qualified. Sequence after CARD-1604 lands on master. Defer or drop S3 behind the D-1
decision: with hold semantics, the existing `BuildSlotRequest` task/session IDs and metrics
are enough to *display* wait time in the escalation.

**D-7 — Missing cross-references (brief check 6).** The plan does not mention:
- CARD-1606: a progress signal that counts its own monitor masks a hang. This bears directly
  on any progress-aware ceiling, and on D-6's polling-derived waits.
- CARD-1615: Deploy as a source writer.
- CARD-1573: the sharded Unit lane, which the card cites as why the review ran long.
- CARD-1151: the detection-only precedent.
- CARD-0153: loop detection, which answers the plan's "chatter keeps a broken loop alive"
  objection.

CARD-1604 is cited but is not deployed-state-aware.

**D-8 — Classification/census obligations absent (plan §Slices "All new test classes have a … mapping").**
Seven new classes are planned: FailedTaskSeatRecoveryTests, FailedTaskSeatScriptTests,
OperatorBoundSeatReleaseTests, BuildSlotAttemptWaitTests, TaskDeadlineBudgetTests,
TaskBuildSlotWaitTests and FailedTaskLateReportTests. The plan declares none of their
`Unit` xor `Integration` tags (`TestLaneCategoryGuardTests`), Slow-allowlist status
(`TestClassificationGuardTests`), or limiter. The script class runs pwsh and the runner
class spawns an owned process, so both need `ProcessSpawnLimit`.
No CP row runs `TestLaneCategoryGuardTests`, `TestClassificationGuardTests` or
`ProcessSpawnLimitTests`, so a mis-tagged class is only caught at nightly.
*Fix:* add a per-class category/limiter table and one guard CP row per affected project.

**D-9 — Producer-to-recipient witness missing for the correction note (V-16, V-20).**
The late correction is a new parent-delivery producer. V-16 asserts "sends one correction"
and V-20 asserts "parent-ack barriers"; neither requires complete-UserPrompt transcript
evidence at the parent. Neither has busy/eligible recipient arms or an
enqueue-failure-after-commit (crash) arm. R-4 C1161 covers only the original Failed note,
not the correction. Queue, Sent or ack is not acceptance.
*Fix:* add a C1161-shaped method for the correction note (and for D-2's session note).

**D-10 — S1 removes the only reclaim for a hung warm Working session without adding detection (plan D-4 last paragraph, R-5).**
Removing TTL retirement of Working sessions matches the invariants and is correct. But a
pooled session stuck mid-turn with no task (no phase deadline applies) then holds its seat
forever with no signal. `FailedTaskSessionHeld` covers only sessions bound to a Failed
attempt.
*Fix:* an attention row (detection only) for a pooled session that has been Working past
its TTL, plus a witness.

## Advisories

- A-1: The imported manifest carries importer defaults (`slotWaitMinutes: 45`,
  `rowMinutes: 15`, `totalMinutes: 90`). The plan's budget says 15/29. The plan delegates
  this to TestDesign; make sure TestDesign writes it in.
- A-2: CP-25/26 client command rows have no count floor (Min n/a, executed=n/a). Pin "3
  cases" through the Vitest JSON receipt, not only expectText.
- A-3: The plan runs twelve isolated builds, eight of them `Antiphon.Tests` at 3-5 min each.
  Builds dominate the 80-minute estimate. The aggregate-Debug assessment the plan defers to
  the caller should cover build reuse across red/green where red-first allows it.
- A-4: Safety holds. No path auto-kills a Working session (D-2 operator-only, S1 removes the
  TTL kill, CARD-0079 is untouched), and the phase deadlines still fail real stalls. Parking
  disabled means no automatic release deadline (CARD-1083), and the plan says so correctly.

## Changed input waits (CARD-1083 question)

A recovered `blocked` report leaves its session waiting. With parking disabled, nothing
releases it automatically. A human-authorized explicit release, a normal exit or a
separately enabled parking path does, with no deadline. Under D-1, the session of a held
ceiling task keeps running and is never released automatically either.
