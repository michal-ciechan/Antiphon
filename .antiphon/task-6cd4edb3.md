# CARD-1490 Final Review (task 6cd4edb3) of Code task b180d6e2

Reviewed source: feat/card-task-b180d6e2 at 45e7e87750f1f33084e892dd05568be78c853117 (base 0ea49a7cc).
Verdict: FOUND. The EscalateAsync and auto-clock fix is correct and its tests can go red. But
check (1)'s trace misclassifies manual Retry: on a running task whose tier is held, Retry kills
the session and leaves the task Queued behind the hold. That is the same harm the fix removes
from escalation.

## Receipt (own run, server2, checkpoint tool)

Run 20261022-131236-22ab at 45e7e8775, one build (bin-c1490), wall 15m26s, verdict GREEN exit=0.

- CP-1 `/*/*/HeldModelEscalationTests/*` executed=4 passed=4 failed=0 skipped=0 (waited=330s)
- CP-2 `/*/*/AgentTaskServiceIntegrationTests/*` executed=120 passed=120 failed=0 skipped=0
- CP-3 `/*/*/AgentTaskStallEscalationTests/*` executed=8 passed=8 failed=0 skipped=0
- CP-4 `/*/*/ModelAvailabilityCreateTests/*` executed=11 passed=11 failed=0 skipped=0
- Every row: dirty=0, source=45e7e8775..., sourceState=clean, buildSource=verified.
- `scripts/validate-checkpoint-receipt.ps1 -Rows CP-1,CP-2,CP-3,CP-4`: `CHECKPOINT SOURCE VALID source=45e7e87750f1f33084e892dd05568be78c853117 rows=4`, exit 0.
- `check-evidence-diff.ps1 -BaseRef 0ea49a7cc -HeadRef HEAD`: commits=3 entries=1 violations=0.
- Unlisted runs, each with a reason:
  - The checkpoint tool build (bin-r6cd), under the slot gate.
  - One mutation build plus one run in a detached scratch worktree at 45e7e8775 (the brief's
    required mutation), under the slot gate.

## Mutation (brief-required)

I removed the `RequireAsync` in `EscalateAsync` (AgentTaskService.cs:3207-3208). The class filter
gave total 4, failed 2. Both V-1 cases (Failed, Working) went red at `C1490-escalate-held-refused`:
no ModelDisabledException was thrown. V-2 and V-3 stayed green. The tests fail for the stated
reason, and the Code task's seeding fix (Role Custom) is good. I discarded the mutation worktree.

## merge-tree / rc

- `git merge-tree --write-tree origin/master(9cf77dbad) HEAD`: clean.
- 8d407e723 then 520d2cd41 cherry-pick cleanly onto origin/rc/2026-10-20 (ceb6e3d6b) in a scratch
  worktree, which I discarded.
- 520d2cd41 also carries the plan doc and the HeldModelEscalationTests seed fix. Drop only the
  plan doc from the rc pick.

## Defects

### D-1 (finding) Manual Retry kills a running session and parks it behind a hold

- Where: `AgentTaskService.RetryAsync` (server/Application/Services/AgentTaskService.cs:2983-3030).
  It refuses only `Queued`, then `RequeueAsync` -> `RequeueCoreAsync`, which calls
  `StopDelegateAsync` when the seat release is unconfirmed (~:3774). It never consults the hold.
- Failure: take a Working/Dispatched explicit task on ClaudeCode High (fable) while the live Manual
  fable hold is active (until 2026-10-23T08:00Z). `POST /api/agent-tasks/{id}/retry` kills the
  working session, sets Queued at the same held tier, and the tick holds it (ModelHeld) until the
  hold clears.
  - The released-seat answer branch is not affected: `ContinueReleasedSeatAnswerAsync` does
    `RequireAsync` (:3688).
  - A chain-governed task re-walks instead of parking.
- Why: brief check (1) counts "any path that can kill ... a running session and then park behind a
  hold" as a finding. The plan's Step 1 row "Retry ... Not a launch on the hold" names this
  behaviour but does not fix it, and does not defer it out of scope. Escalation now refuses this
  exact pattern, so Retry is the remaining operator entry to it.
- Fix: in `RetryAsync`, before `RequeueAsync`, call `RequireAsync(task.AgentKind, <dispatch
  alias>)`. That gives a 409 `model_disabled` with no kill. At minimum refuse when there is a
  live, unreleased session. Add a HeldModelEscalationTests-style case: Retry of a Working task
  under a hold is 409, with no kill and the row unchanged.
- Owner: Code task b180d6e2 (land owner).

### D-2 (finding, secondary) The escalation hold check keys on the level alias, not the dispatch alias

- Where: `EscalateAsync` (:3208) checks `ModelLevelAliases.For(kind, target)`. The tick that will
  launch the requeued task checks `ResolveDispatchAliasAsync` (AgentTaskDispatcher.cs:7140). For a
  pinned (non-ephemeral) agent, that is `DispatchModelAlias.Resolve(kind, level, agent.ModelId)`,
  and `agent.ModelId` wins. `RequeueCoreAsync` keeps `AgentId` for a pinned agent. For a
  specialist, it is `task.SpecialistModelAlias`.
- Failure: a pinned agent with `ModelId=opus` runs a High task, and `ClaudeCode/opus` is held.
  `POST /escalate` checks `fable`, which is not held. It passes, kills the session, requeues, and
  the tick resolves `opus`, which is held, and parks. This is kill-then-park.
  - The reverse case is a spurious 409: fable held, the agent pinned to opus.
- Why: brief check (2) asks the check to key on the alias the escalation would actually use.
- Fix: resolve the alias the same way the tick does. Share or extract `ResolveDispatchAliasAsync`
  or `DispatchModelAlias.Resolve` with the agent's `ModelId`. Use it in `EscalateAsync` and in
  the D-1 Retry check.
- Owner: Code task b180d6e2.
- Reachability: narrow (pinned agents with a ModelId, specialists). Rate it below D-1.

## Checks that passed

- (2) No side effect before the refusal: before `RequireAsync`, `EscalateAsync` only loads the row
  and computes a pure target. `RequireAsync` may `SaveChanges` while normalising a legacy
  AutoDetected hold row, but that touches only the hold, never the task. There is no kill, tier,
  status or event change before the refusal.
  - A non-held escalation is unchanged apart from one hold read.
  - `FindActiveAsync` ORs in the kind-wide `*` hold.
  - `ResolveEscalationTarget` already applies the role's `EscalateTo`.
  - Chain-governed tasks are refused rather than re-walked. That is stricter than the tick, but it
    is not a launch (disclosure only).
- (3) Auto clock: the skip comes before the `FailureReason` write and the kill, with no side effect.
  The next sweep re-evaluates the task once the hold clears.
  - Without the skip, `EscalateAsync`'s `RequireAsync` would still refuse (caught). The only
    observable loss would be a `FailureReason` write left on the tracked row, which the next
    escalation's `SaveChanges` would persist.
  - The PC-2 gap is acceptable: the clock ships disarmed and V-1 covers the load-bearing guard.
  - A narrow test is practical. `AgentTaskStallEscalationTests.CreateHarness` builds a
    ServiceCollection, so register `ModelAvailability` over an isolated-schema context holding
    fable. Seed a stalled Debug High task under the opt-in settings, then assert 0 escalated, no
    kill and a null FailureReason. Backlog suggestion.
  - Residual race (advisory): a hold placed between `IsHeldAsync` and `EscalateAsync` leaves that
    tracked `FailureReason` dirty.
- (4) Reply / quota-resume: Reply on a quota-Blocked task enqueues into the existing
  `AgentSessionId`. The message queue has no relaunch path. It is not a launch on a held model:
  the provider re-refuses and no new process is started. A disclosed residual, not a finding.
- (1) The rest of the trace is right:
  - Create (explicit, override, chain), the tick for every Queued task, and warm reuse after the
    tick all consult the hold.
  - Reroute (:3090 `RequireAsync`), specialist (`IsHeldAsync` per candidate), and orchestrator or
    standing start (AgentControlService.cs:249 `RequireAsync`) consult it too.
- Trace inaccuracy (advisory): the plan row "Released-seat Continue ... no" is wrong. It consults
  the hold through `RequireAsync` at :3688, so the trace errs on the safe side.

## Documented non-changes (disclosure)

- `model_disabled` is kept rather than `model_held`.
- The refusal carries the available list, not the next chain candidate.
- Reply/quota-resume reads only its recorded hold.
- The Unit lane was not run, per the brief's minimal profile. The brief's REQUIRED RECEIPT (CP-1..CP-4)
  is the ordinary selection this Review ran in full.
