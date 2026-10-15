# CARD-1289 admission refinement

Original Code/landing owner: 5a9cd727-c32f-4f59-96ac-40b5a91c70b2.
Base: b6fdedc13fc46baddc22206c5c34003a93d3cd5b. Restart: server after landing.

Compare admission coordinates instead of the all-write concurrency token: status,
archive, board/identifier/workflow binding, platform and verification policies.
Title, description, owner and token-only edits do not invalidate admission. Keep
closed-card refusal roles unchanged (CARD-1288 owns that decision).

Prepare ordinary create's advisory worktree base outside the card transaction;
dispatch already recomputes it. Keep handoff preparation after its idempotency
return: a linked child must remain recoverable when Git is unavailable or changes.
Measure real Git and lock durations for both paths. Child plus handoff link remain
in one transaction; order stays card, action, host budget, concurrency gate.
No changes to persistence immutability (card item d is outside the Code brief).

## Verification design

V-1: ConcurrentCardEdit parameter rows force writes between pre-read and lock.
Four unrelated edits admit exactly one child; state/archive/platform/policy changes
retain their refusals and zero children. Existing admission fence retains Done,
unbound and post-Done Mutation scope. Red-first source has only tests.
V-2: GitLockDuration probes the real row with NOWAIT during actual Git subprocess
operations, measures lock/command/create durations, checks saved preview and
atomic child linkage, and proves retry bypasses Git. Ordinary arm must go red
before moving the preview. Handoff arm documents the retained critical section.
R-1: CardLockOrder observes production SQL for observation/refusal writers and
child admission. Mutation will remove/reorder locks to establish its controls.
R-2: Full PipelineSpeedIntegrationTests includes S0 and S6a atomicity, refusal,
HTTP, delivery recovery, correction and schema coverage.
R-3: Full existing create/binding/concurrency/base and pilot classes selected from
AgentTaskService references. Resolver tests cover the preparation being moved.
R-4: Whole Unit lane required by the higher-priority Final contract. The brief's
no-Unit instruction conflicts; retain real outcome rather than claim a waiver.
No full-assembly run: integration impact is bounded to the named classes. Unit
protects repository-wide structural contracts; estimated 15 minutes and may be
incomplete under the task's 60-minute budget. No timeout increases or assertion
weakening. No repetition after green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1289-repair/` | admission-regressions | `/*/*/PipelineSpeedIntegrationTests/C1289_*` | V-1,V-2,R-1 | all 11 cases, zero failed | 11 | 4 |
| CP-2 | S2 | CP-1 | full-foundation-wiring | `/*/*/PipelineSpeedIntegrationTests/*` | R-2 | all methods, zero failed | 35 | 6 |
| CP-3 | S2 | CP-1 | existing-create-admission | `/*/*/(AgentTaskCardBindingTests*)\|(AgentTaskConcurrencyLimitTests*)\|(AgentTaskScopeOverlapCreateTests*)\|(DispatchConcurrencyAdmissionTests*)\|(AgentTaskWorktreeBaseCreateTests*)\|(AgentTaskWorktreeBaseResolverTests*)\|(PipelineHandoffPilotTests*)/*` | R-3 | every named full class, zero failed | 70 | 8 |
| CP-4 | S2 | CP-1 | unit-application | `/*/Antiphon.Tests.Application/*/*[Category=Unit]` | R-4 | all Unit cases in namespace, zero failed | 1 | 5 |
| CP-5 | S2 | CP-1 | unit-checkpoints | `/*/Antiphon.Tests.Checkpoints/*/*[Category=Unit]` | R-4 | all Unit cases in namespace, zero failed | 1 | 5 |
| CP-6 | S2 | CP-1 | unit-scripts | `/*/Antiphon.Tests.Scripts/*/*[Category=Unit]` | R-4 | all Unit cases in namespace, zero failed | 1 | 5 |
| CP-7 | S2 | CP-1 | unit-remainder | `/*/(Antiphon.Tests)\|(Antiphon.Tests.AgentTui*)\|(Antiphon.Tests.Agents*)\|(Antiphon.Tests.ApiKeys*)\|(Antiphon.Tests.Domain*)\|(Antiphon.Tests.Infrastructure*)\|(Antiphon.Tests.Migrations*)\|(Antiphon.Tests.TestHelpers*)/*/*[Category=Unit]` | R-4 | all remaining Unit namespaces, zero failed | 1 | 5 |

The S1 version of CP-1 used bin-c1289-red and expected the guarded defects.
It now reruns green in S2, sharing one build with all remaining rows. Preserve
all historical outcomes. CP-4..7 partition the whole Unit lane using the source
category/namespace census, because S0's single Unit row exceeded 15 minutes.
Each row retains a 15-minute minimum deadline; no timeout is widened. Final
execution has a 22-minute total cap to respect the commissioning budget.

Repair after the first S2 selection: CP-3 exposed V17 concurrency-refusal priority.
Preview capacity without acquiring a lock before ordinary Git, then retain the
transactional capacity recheck before insertion. SourceLanding keeps its original
custody-before-capacity ordering. The Git probe now registers the real open gate.
The existing V17 test must continue to require zero Git calls on initial refusal.
Run CP-1 and CP-3 again, plus unfinished CP-5..7; CP-2 (35/35) and CP-4
(2938 passed, 27 skipped) were executed before this bounded repair and retain their
actual source identity. The stopped run has no terminal run receipt: do not claim
Final qualification from its partial state. Fresh output is bin-c1289-repair/.
The repair run has a 10-minute total cap; incomplete verification stays incomplete.
