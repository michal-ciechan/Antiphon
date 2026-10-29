# CARD-1610: Keep failed tasks' live sessions reachable

Date: 2026-10-29. Stage: Plan. Status: early draft; source investigation and
verification roster are still in progress. Inspected base:
`f8bddb6efda7b0e8f50f3b45744087e52188bc66`.

## Problem and evidence

CARD-1610 records Review `7ee3b07a` failing at its 240-minute role ceiling while
its session remained Working. A read during this Plan still reports Failed and
session `8aa54343-3835-4e5f-8f3b-4e77391f3f6f` Running, working. The second
example, `038c27ab`, now has an ended session; retain the original incident as
history rather than claiming both leaks are current.

## Ground truth

| Card assumption | Inspected behavior | Consequence |
|---|---|---|
| There is no supported stop surface | `RunnerSlotService.ReleaseAsync` already supports operator-authorized single-seat force release; terminal task cancel refuses | Build on the existing release surface, preserve task outcome |
| A Failed task must be an orphan | Live GET returned `openTaskId=null`, `occupiesCapacity=true`, **`orphan=false`** for the incident session. `FailAndNotifyAsync` calls `RemoveEphemeralAgentAsync`, which marks a surviving pool row Idle with PoolIdleSince; `SeatDesktopJoin` treats those fields as warm without checking the session's Working state | The warm-pool exemption hides this incident; absence of an open task is insufficient. Add terminal-live detection independent of that exemption |
| The orphan sweep should free it | `TerminalRunnerSeatRelease:AutomaticEnabled` ships false; enabled discovery still requires fresh conditional idle evidence | Working must remain protected; surface the reason for retention |
| Transcript progress extends the ceiling | `TaskDeadlinePolicy` measures ceiling from DispatchedAt; Codex heartbeat only adjusts model-wait | Make the ceiling budget explicit and account for queue time separately |

## Decisions

- D-1 (draft): retain terminal task status and provide task-oriented discovery of
  the existing seat-release path. No general automatic kill and no blanket
  enablement of terminal-seat discovery.
- D-2 (draft): preserve operator-token authority for destructive release. An
  ordinary task token is not consent to kill a Working session.
- D-3 (draft): reuse SlotOrphan/seat attention and add an explicit status line;
  unavailable runner evidence is Unknown, never released.
- D-4 (draft): design attempt-bound ceiling budgets and late-report recovery;
  do not reset a hard deadline on arbitrary transcript output.

## Performance budget

Plan runs no builds or tests. New witness timings are unmeasured; estimates and
roughly 2x measured p95 limits will be separated in the completed design. Tests
will reuse the existing shared PostgreSQL setup and virtual time. Slot admission
and execution have distinct finite budgets. Runs expected or observed above
30 minutes require Debug under docs/test-performance-standards.md.

## Pending investigation

Read the report settlement eligibility, release locks/receipts, task deadline
DTOs, build-slot identity and affected test rosters. Complete slices, failure-mode
analysis, witness map and checkpoints before reporting done. Runtime placement
reads succeeded: defaults revision 2, eligible Linux and Windows lanes. Planned
portable work omits runner and platform pins.

Further confirmed source facts: `AgentTaskReplyService` and durable runtime action
admission select only Dispatched/Working tasks, so a later report is not settled.
The dead-session reconciler's Failed branch repairs a missing failure notice,
not a late report. `BuildSlotRequest` has task/session identifiers, but public
waiter rows lack them and the metrics are aggregate, so transcript strings or the
current metrics cannot safely subtract per-attempt wait time.

Lock review found different SQL orders: runtime action saves take session then
task; terminal release takes task then release then session. The completed design
must explicitly resolve that intersection. The warm-pool janitor also permits a
mid-turn retirement after its transcript becomes older than the idle TTL; this
needs a no-Working-stop guard, not a shorter timeout.
