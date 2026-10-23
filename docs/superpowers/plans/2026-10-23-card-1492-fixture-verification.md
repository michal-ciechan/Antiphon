# CARD-1492 fixture repair and verification

Code owner / landing owner: `d923b640` (assigned branch `feat/card-task-d923b640`).
Task base: `cbe0eda6a750dd934c1f3ed0bac4d1caee1f28bc`, current origin/master at start.
Brief: focused fixture-only repair; Plan/TestDesign bypassed; no Unit or full assembly.
Diagnosis: `docs/investigations/2026-10-23-rc-5a126f6fc-qualification-reds-debug.md`, cause B.
C544's independent product failure belongs to CARD-1529 and is outside this slice.

## Red witness

Before any repair, the four existing real-type C1205 cases ran at the task base:
watchdog, busy tail, restart five completions with cache true and false.
All four failed (0 passed, 4 failed, 0 skipped): CheckedWorking versus Unknown,
and five recipient submissions versus zero, with InvalidOperationException after
transcript saves. The existing assertions are the regression witnesses; no assertion,
retry, timeout or test classification changes are authorized.

This prerequisite used a direct slot-gated `dotnet run` rather than the final
checkpoint, to keep the single final checkpoint build separate from the red source.
Command: `timeout 720s pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1492-base-red -SlotWaitMinutes 5 -- dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c1492-red/ -- --treenode-filter '/*/*/TurnEndGapRestartDeliveryTests/(C1205_Watchdog_delivers_while_event_stream_remains_healthy*)|(C1205_Busy_tail_keeps_completion_obligations_pending*)|(C1205_Restart_delivers_five_gap_held_completions_once*)' --report-trx --results-directory <worktree>/.antiphon/c1492-evidence/red`.
Slot granted, waited=0s, held=438s; test execution 74.582s; exit 2, assertion reds.
Raw log and fresh TRX remain ignored under `.antiphon/c1492-evidence/`.

## Implementation boundaries

Register the real SessionRuntimeActionCoordinator in TurnEndGapDeliveryHarness.
The restart-gap witness must own the real action-worker loop alongside its pump,
include its pending hints/owners in quiescence, and join it after stopping the pump
and before service reconstruction/disposal. Other cases retain their explicitly
staged setup and delivery operations. The shared C544World is unchanged.

## Ordinary verification

V-1: all four original reds become green with unchanged outcomes/receipt assertions.
R-1: all remaining 27 cases in the affected delivery class retain their fault cuts,
restart identity, complete-recipient evidence and busy/unknown protection.
R-2: C487_G059 checks that the slow-test allowlist matches the compiled classifications.
No class/category changes; the existing registry entry is retained.
Manual M-1: exact committed repair diff applies on origin/rc/2026-10-20 in a detached
scratch worktree; compile that test project through the host slot if affordable.
This separately requested compile is the only additional build beyond red/final.
The source harness/world/test files are identical at master base and rc base
`5a126f6fc5ae4e16d803b628716b8f0485a37174`; Linux directly reproduces cause B.
No OS-selected behavior changes and no Windows-only witness is required for this DI cause.
No desktop, live qualification, deployment or restart work is authorized.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1492-final/` | fixture-delivery | `/*/*/(TurnEndGapRestartDeliveryTests*)\|(TestClassificationPolicyTests*)/(C1205_*)\|(C487_G059*)` | V-1, R-1, R-2 | 32 passed, 0 failed/skipped: 31 delivery cases and one registry guard | 32 | 8 | true |

Run once at committed HEAD with `--expected-source-sha <HEAD> --total-timeout 25m
--row-timeout 15m`; wait until exit is not 75. One build, one exact row, no repeats
required after green. Validate final report.json with validate-checkpoint-receipt.ps1.
Delete only inventoried task-owned bin-c1492-* outputs after their owners exit.

## Mutation handoff (pending)

PC-1492-1: omit coordinator registration; watchdog exact method must fail at
CheckedWorking versus Unknown. PC-1492-2: omit starting the gap action worker;
restart-five exact method must fail at five recipient submissions versus zero.
Both belong to method-scoped SourceLanding Mutation after ordinary Review and landing.
Existing CARD-1205 PC-84 through PC-100 remain pending; this repair does not discharge them.
