# CARD-1014 ordinary spill incident repair

Original Code and landing owner: a8a78ab3 (task token identifies the full task GUID).
Branch: feat/card-task-a8a78ab3. Worktree: /work/worktrees/task-a8a78ab3.
Base: 29d7c3ebb440378abea791d2c4dcb5ac90b6192b. Round: Final.
Acceptance: CARD-1014; CARD-0888 origin and CARD-0965 item 5 incident visibility.

S1 extends Ordinary_spill_errors_keep_their_existing_delivery_behavior across
send-now, flush and enqueue-now at unchanged production. Require three assertion
failures for missing incidents. S2 adds recording only where fallback was refused,
using RecordTransportFailureAsync and RunnerSpillWriteException's fixed safe text.
The task-input fallback remains unchanged.

## Verification design

V-1: all three argument rows require exactly one persisted DeliveryTransportFailed
incident, Error severity, exact legacy recorder text and no private body tail.
R-1: all three rows retain Pending, one attempt, no typed input and no fallback
Warning; send-now and flush preserve row identity.
V-2: full affected SessionMessageQueue classes pin existing queue behavior.
V-3: AttentionServiceTests, IncidentPageNotifierTests, TaskInputReadFailureTests
and TaskInputAttentionCostTests cover incident projection and input privacy.
V-22 (inherited CARD-0965): Input_body_is_absent_from_task_summary_events_and_logs
and its whole AgentTaskInputFallbackTests class cover public events, logs and
whole-body authorized access; AgentTaskInputSpillTests, AgentTaskRefineTests and
AgentTaskReplyOverlayTests cover adjacent input behavior (R-2).
V-4: one whole Unit lane; report actual inherited OS skips.

PC-1/send-now, PC-1/flush, PC-1/enqueue-now: suppress recording on each ordinary
path; precise Ordinary_spill_errors_keep_their_existing_delivery_behavior method
must fail incident-count assertion. PC-2/kind: change recorder incident kind;
the same precise method must fail transport-kind assertion. All PCs stay pending
for post-land SourceLanding Mutation. The brief requests two scratch mutations;
Code does not execute deliberate mutants because the stage bundle assigns those
cycles to Mutation. No PC is discharged by ordinary red-first evidence.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1014-red/` | regression-red | `/*/*/AgentTaskInputFallbackTests/Ordinary_spill_errors_keep_their_existing_delivery_behavior` | V-1, R-1 baseline red | AgentTaskInputFallbackTests.Ordinary_spill_errors_keep_their_existing_delivery_behavior | 3 | 3 | true | `C804_ORPHAN_SWEEP_ROOT=c1014-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1014-final/` | regression-green | `/*/*/AgentTaskInputFallbackTests/Ordinary_spill_errors_keep_their_existing_delivery_behavior` | V-1, R-1 | AgentTaskInputFallbackTests.Ordinary_spill_errors_keep_their_existing_delivery_behavior | 3 | 3 | true | `C804_ORPHAN_SWEEP_ROOT=c1014-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | CP-2 (-NoBuild) | queue | `/*/*/SessionMessageQueue*/*` | V-2 | SessionMessageQueueServiceTests,SessionMessageQueueDeliveryVerificationTests,SessionMessageQueueSpillTests,SessionMessageQueueSupervisionTests,SessionMessageQueueInterruptedAttemptTests,SessionMessageQueueWedgedHeadTests,SessionMessageQueueBootWedgeTests,SessionMessageQueuePhoneHomeDropTests,SessionMessageQueuePtyIntegrationTests,SessionMessageQueueGrokPtyIntegrationTests | 10 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c1014-disabled` |
| CP-4 | S2 | CP-2 (-NoBuild) | attention | `/*/*/(AttentionServiceTests*)\|(IncidentPageNotifierTests*)\|(TaskInputReadFailureTests*)\|(TaskInputAttentionCostTests*)/*` | V-3 | AttentionServiceTests,IncidentPageNotifierTests,TaskInputReadFailureTests,TaskInputAttentionCostTests | 4 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c1014-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S2 | CP-2 (-NoBuild) | input | `/*/*/(AgentTaskInput*)\|(AgentTaskRefineTests*)\|(AgentTaskReplyOverlayTests*)/*` | V-22, R-2 | AgentTaskInputFallbackTests,AgentTaskInputSpillTests,AgentTaskRefineTests,AgentTaskReplyOverlayTests | 4 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c1014-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S2 | CP-2 (-NoBuild) | unit | `/*/*/*/*[Category=Unit]` | V-4 | nonzero Unit results | 1 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c1014-disabled` |

### Cost and scope

31 minutes estimated ordinary floor including baseline red, plus authoring.
Invariant: failed pre-input ordinary delivery is durably visible without changing
queue custody or leaking the body. Impact is bounded to the three outcome handlers;
no unbounded classes and no full-assembly run. Unit misses delivery, landing,
leases and persistence; named integration classes cover this repair's delivery and
persistence. No live restart/manual deployment is required during Code.
Checkpoint tool bootstrap is an explained setup build under the host slot gate.
