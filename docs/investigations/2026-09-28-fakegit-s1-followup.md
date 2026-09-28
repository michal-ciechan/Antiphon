# FakeGit S1 follow-up: Unit failures and pilot scope

Source implementation: `e50a3acc10019db324936fee90362943f72f116b` on `feat/card-task-3b559ad9`. This note records the requested follow-up to [the S1 plan](../superpowers/plans/2026-09-28-fakegit-implementation-plan.md). It does not change the G1 criteria or authorize S3. The full local Code report and TRX/log paths are in `.antiphon/task-3b559ad9.md` in the task worktree.

## Exact Unit outcomes

The two full Unit runs did **not** fail the same test. Each selected 3,515 cases, executed 3,482, skipped 33, and passed 3,481 of the executed cases.

| Run at S1 SHA | Failed test and assertion | Follow-up |
|---|---|---|
| `FINAL-UNIT-S1-NEGATIVE` | `Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.Disabling_the_probe_does_not_disable_effort_resolution(True)`: its 9-second `Task.WhenAny` watchdog won; test body took 10.937 seconds. | Both argument cases passed in the exact scoped rerun at S1 SHA. |
| `FINAL-UNIT-E50-REPEAT` | `Antiphon.Tests.Infrastructure.Resilience.ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget`: fake-clock `afterAttempt` was 12 seconds, violating `< 12`. | The exact scoped method failed twice at S1 SHA, at 12 and 13 fake seconds. |

The pre-S1 base is `5a1152a821235c62f9027e705b458e5bcfb920e6`. The exact Resilience method passed twice in a separately built, detached base worktree. The test, its `ResilienceTestHost.Pump`, and the Resilience implementation have no diff from base to S1. The test constructs its own service provider; it does not invoke `GitService` or FakeGit. `Pump` advances fake time in 250 ms steps, sleeping one real millisecond between advances, so a delayed cancellation continuation can produce the observed fake-clock overshoot. That is a plausible timing mechanism, **not a proven root cause**. Current evidence does not establish that FakeGit caused the failure, nor does it demonstrate that the same failure reproduces on base. The current S1 Unit lane is red and should not be reported as clean or merged as qualified infrastructure until this is resolved or explicitly accepted by the owner.

The RunnerClaude test and implementation also have no diff from base to S1. Its two argument cases passed in one scoped S1 rerun and one separately built, scoped base run (`BASE-EFFORT`, 2/2). The one full-lane timeout did not reproduce in either scoped run. A single base pass does not prove that the watchdog cannot time out there under full-lane load.

`BASE-EFFORT` was an extra, leased, isolated-output diagnostic build/filter outside CP-1–CP-8, run specifically to answer this follow-up. Its TRX and the two base Resilience TRXs are retained under `.antiphon/fakegit-baseline-evidence/` in the task worktree; the disposable base worktree was removed afterward.

## What the pilot measures

All S1 CP-1–CP-8 checkpoint rows passed (165 executed results, zero failures/skips). The 12 same-body `GitServiceTests` cases produced matching normalized command/graph/file observations in real and fake modes. Three real rows launched 218 Git commands each; three fake rows launched zero. The six operational cases' serial duration sum had medians 0.755 seconds real and 0.155 seconds fake. This is evidence that the fake avoids work in these cases, not a process-wall result.

The whole selection's process-wall medians were 3.976 seconds real and 3.384 seconds fake: a 0.592-second (14.9%) saving. One paired fake run was 0.062 seconds slower. Thus G1 fails all three speed predicates (at least 30%, at least one second, every pair faster). The six naming cases and roughly 2.5 seconds of runner/startup overhead dilute the process-level benefit. A selection this short can prove the limited Git semantics and zero-escape property, but cannot reliably establish the payoff for command-heavy landing tests. It also cannot justify waiving a predeclared gate after seeing the measurements.

The original investigation's five landing selections totaled roughly 122 minutes of **serialized case durations**, not one suite wall duration. A traced admission case launched 636–689 top-level Git commands, mostly read probes; that is materially different command density from the S1 selection. Those classes also exercise PostgreSQL, physical leases/journals, and native behaviors that a fake must retain or explicitly leave real. Their historical durations are potential, not a measured FakeGit saving.

## Recommendation for the decision owner

Keep S3+ paused under the current plan. A revised, bounded heavier pilot is a fairer test of the overall speed hypothesis **if** a real-only command trace first shows substantial Git subprocess time in one representative selection. Choose and preregister one named method or small matrix with high Git-command density, run the same body and assertions in both modes, retain physical lease/database semantics, and compare paired process-wall times with launch counts and semantic receipts. `AgentTaskLandAdmissionTests` has a traced high-command case, but its fake vocabulary and protocol risk are much larger than S1; a smaller worktree/dispatch selection may be a cheaper first probe if a trace shows enough Git cost. Revise the plan, TestDesign, and checkpoint table before implementing such a pilot. Do not infer a 122-minute saving, migrate all five matrices, or proceed to S3 from the present G1 miss.

For S1 merge readiness, triage the persistent current-SHA Resilience clock failure first. The unchanged code and independent fixture argue against a direct FakeGit behavior regression, but the present base/current comparison does not exonerate the branch. A controlled base/current repetition under comparable load, with the cancellation completion timing captured, can distinguish an ambient fake-clock scheduling race from a repeatable change-associated failure. Preserve the existing Unit result as red until then.
