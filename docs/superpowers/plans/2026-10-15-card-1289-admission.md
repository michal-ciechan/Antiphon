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
V-2: GitLockDuration probes the real row with NOWAIT during actual CommonDirectory
Git operations, measures lock/command/create durations, checks saved preview and
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
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1289-red/` | red-admission | `/*/*/PipelineSpeedIntegrationTests/C1289_*` | V-1,V-2,R-1 | all 11 cases; unrelated edits and ordinary Git arm fail before fix | 11 | 4 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1289-final/` | full-foundation-wiring | `/*/*/PipelineSpeedIntegrationTests/*` | V-1,V-2,R-1,R-2 | all methods, zero failed | 35 | 6 |
| CP-3 | S2 | CP-2 | existing-create-admission | `/*/*/(AgentTaskCardBindingTests*)\|(AgentTaskConcurrencyLimitTests*)\|(AgentTaskScopeOverlapCreateTests*)\|(DispatchConcurrencyAdmissionTests*)\|(AgentTaskWorktreeBaseCreateTests*)\|(AgentTaskWorktreeBaseResolverTests*)\|(PipelineHandoffPilotTests*)/*` | R-3 | every named full class, zero failed | 70 | 8 |
| CP-4 | S2 | CP-2 | unit | `/*/*/*/*[Category=Unit]` | R-4 | all Unit cases, zero failed | 1 | 5 |
