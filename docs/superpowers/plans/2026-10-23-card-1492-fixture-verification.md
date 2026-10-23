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

Run once at committed HEAD with `--expected-source-sha <HEAD> --total-timeout 45m
--row-timeout 15m`; wait until exit is not 75. One build, one exact row, no repeats
required after green. Validate final report.json with validate-checkpoint-receipt.ps1.
Delete only inventoried task-owned bin-c1492-* outputs after their owners exit.

## Mutation handoff (pending)

PC-1492-1: omit coordinator registration; watchdog exact method must fail at
CheckedWorking versus Unknown. PC-1492-2: omit starting the gap action worker;
restart-five exact method must fail at five recipient submissions versus zero.
Both belong to method-scoped SourceLanding Mutation after ordinary Review and landing.
Existing CARD-1205 PC-84 through PC-100 remain pending; this repair does not discharge them.

## Prequalification rc check

M-1 passed: repair commit `229ffecbf1598cfe9ec67ccee9b1fc8cca04385a`
applied with `git apply --check` and `git apply` in the detached rc scratch tree
at `5a126f6fc5ae4e16d803b628716b8f0485a37174`. The resulting harness bytes match
master's repaired harness. The rc test project compiled successfully (exit 0),
through `build-slot.ps1 -Label c1492-rc-compile`, bounded by `timeout 600s`, using
`dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1492-rc/
--property:UseAppHost=false --nologo`. Existing compiler/analyzer warnings remain.
This is compile/applicability evidence, not a claim of rc test execution.

Exact rc source pick list: `229ffecbf1598cfe9ec67ccee9b1fc8cca04385a` only.
The preceding verification-manifest commit and this documentation update are optional
rc documentation picks; neither is needed to compile the fixture repair.
Final checkpoint results remain pending at this committed narrative tip; the final
caller report supplies the fresh receipt, tested SHA and per-method counts.

## First checkpoint and additional fixture omission

The first checkpoint at `09c8e9f141f141176e944d6ca1689fe230b3fda9`
completed RED: 32 executed, 4 passed, 28 failed, 0 skipped. Build 5m08s,
tests 7m37s, total wall 13m56s; both build and row slots granted, waited=0s.
The original Unknown persistence outcome disappeared, but input stayed held:
EpochAdapter inherited null dialog observation and Unsupported conditional input,
and its FakeAgentProtocolAdapter never enabled the supported Claude composer.
C544DeliveryRig already has the required EnableClaudeDialogObservationAsync setup.
The repair now applies that same real generation-bound setup on each attachment
and forwards observation/conditional writes through the epoch wrapper. The input
guard remains active. The gap action worker remains scoped to its owned experiment.
All test assertions and timeouts remain unchanged.

Unedited first checkpoint receipt:

```text
CHECKPOINT CP-1 commit=09c8e9f141f141176e944d6ca1689fe230b3fda9 build=ok filter=/*/*/(TurnEndGapRestartDeliveryTests*)|(TestClassificationPolicyTests*)/(C1205_*)|(C487_G059*) executed=32 passed=4 failed=28 skipped=0 trx=/work/worktrees/task-d923b640/.antiphon/checkpoints/20261023-105551-fdda/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=09c8e9f141f141176e944d6ca1689fe230b3fda9 sourceState=clean buildSource=verified
```

PC-1492-3 (pending SourceLanding Mutation): omit forwarding dialog observation
through EpochAdapter; exact watchdog method must fail its recipient-submission
assertion despite successful transcript recovery. The first checkpoint is the
ordinary red witness for this fixture omission, not a Mutation execution.

A second CP-1 run is required after this substantive fixture repair, retaining all
32 selected cases because the shared attachment path changed. This is a changed
proof selection's repair run, not a repetition to seek a passing flake. Final
receipt validation remains outstanding. The rc source pick list must now include
both fixture repair commits; the earlier one-commit pick applied only to the first
candidate and is historical. Recheck the combined diff and compile on rc.

## Branch-specific rc pick correction

The combined master guard-forwarding change was checked in the rc scratch tree.
Its compile failed in 1m21s with CS0246: ProviderDialogObservation is absent from
that rc revision. Source inspection confirms that rc has neither that contract
nor the supported-dialog fixture extension. This is a later-master fixture
compatibility repair, not part of cause B on rc. A subsequent import-only commit
was unnecessary and is removed at this narrative tip; it cannot supply an API
that does not exist on rc.

Final exact rc pick list remains `229ffecbf1598cfe9ec67ccee9b1fc8cca04385a`
ONLY. Its clean apply and successful rc test-project compile are recorded above.
Do not pick d2e472d817a5e5ff0bc75a1b8676558d7ed66e10 or the import-only commit
to rc: those forward the master dialog API, which rc does not have. The master
candidate includes that separate fixture repair because its input guard is active.
The attempted combined compile is retained as failed applicability evidence,
not relabeled green. No product API or input guard was changed.

The next CP-1 run qualifies the committed master tip with the original 32-case
selection and unchanged deadlines. Its receipt and V-1/R-1/R-2 outcomes are pending.

## Targeted watchdog repair

At ca3d82b2838c40e10f0c7bead470c31b6a6c10e1 CP-1 executed 32: 31 passed,
1 failed, 0 skipped; full fresh TRX inspected. Both restart variants, busy-tail,
all 27 adjacent delivery cases and the registry guard passed. The only red was
watchdog, five submissions expected versus one. Build/source provenance verified,
both slots granted and waited=0s. Total wall 5m27s; host 105.57s, build phase205.90s.

The watchdog starts a healthy pump, so later recipient TurnEnds need action workers
as well as initial stale recovery. ProbeAsync now starts the same owned action loop
when a pump exists; the no-pump fault-cut setup remains explicit. StartGapAsync
uses the extracted identical starter. Only the watchdog method calls ProbeAsync
with a live pump. Run CP-2 only after this repair, retaining the prior 31 green
cases under the targeted-after-fix rule. No assertion or timeout changed.
CP-1492-4 remains pending Mutation: omit starting actions from the live-pump probe;
exact watchdog must fail five submissions versus one. The rc pick list is now
229ffecbf1598cfe9ec67ccee9b1fc8cca04385a followed by this final watchdog repair
commit (no dialog API dependency in that repair). Its source delta must apply to rc;
the initial rc fixture compile remains historical, not a new final-pick compile.

## Continuation Code 9e7c7dd0 (Final, focused brief)

Original Code / landing owner remains d923b640; continuation branch is
feat/card-task-9e7c7dd0, worktree /work/worktrees/task-9e7c7dd0. Start ref is
 e39114498ae4952779875f1a49ef297d60054689 (no history rewrite).
The explicit continuation brief overrides the default Final scope: one CP-1
shared build and all 32 original cases; no Unit, assembly, desktop, Windows
qualification, restart or repeated green proof. CP-2 above was a historical
repair selection; the current closed table intentionally contains CP-1 only.

Inherited red witness, no confirming repetition needed:

```text
CHECKPOINT CP-2 commit=e39114498ae4952779875f1a49ef297d60054689 build=ok filter=/*/*/TurnEndGapRestartDeliveryTests/C1205_Watchdog_delivers_while_event_stream_remains_healthy* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-d923b640/.antiphon/checkpoints/20261023-112035-729b/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=e39114498ae4952779875f1a49ef297d60054689 sourceState=clean buildSource=verified
```

That fresh historical TRX reports ends=2, sqlWorking=False, submissions=1;
UserPrompt and TurnEnd 3/4 were persisted and the first delivery was confirmed.
Source diagnosis: SessionRuntimeActionCoordinator.ExecuteAsync retains Deferred
queue phases with DueAt=RetryAt(attempts), releases its volatile owner, and its
DiscoverAsync waits on a one-second clock timer. The fixture's quiescence only
checked volatile owners/hints, and JoinDeliveryAsync drove only 20/50 ms timers.
It could therefore return with durable deferred queue work still awaiting the
frozen clock. This is fixture scheduling, not evidence for a product repair.

The fixture join now observes the discovery timer registration as well as owners
and hints, reads the recipient's durable action DueAt when idle, and advances to
the later of that due time and the existing discovery timer. The real coordinator
and queue execute the boundary. Working recipients retain their obligations.
No manual queue flush, re-arming of product state, new input retry, changed
assertion, classification, deadline or product source is introduced.

V-1, R-1 and R-2 are pending the final CP-1 at the committed continuation tip;
its fresh TRX must contain watchdog, both restart cache arguments, busy-tail,
27 adjacent delivery cases and C487_G059, 32/32 green with clean build provenance.
The final external report supplies actual outcomes and unedited CHECKPOINT lines;
this narrative is committed before that run so the final pushed tip is tested.
The run uses --total-timeout 45m --row-timeout 15m and no repetition. All task-owned
bin-c1492-* outputs must be absent before launch and removed after owner death.

M-1: prior rc applicability/compile evidence for fixture diffs 229ffecbf1598cfe9ec67ccee9b1fc8cca04385a
and e39114498ae4952779875f1a49ef297d60054689 remains historical. The continuation
fixture diff must also apply to rc; runtime execution on rc remains unclaimed
until a fresh rc receipt exists. Do not pick master's dialog API changes
 d2e472d817a5e5ff0bc75a1b8676558d7ed66e10 or
506892ab146fc3519df580bac71a81157442d188 to rc, which lacks that API.

PC-1492-5 pending SourceLanding Mutation: omit the durable idle-action join and
discovery-clock schedule; exact watchdog must fail five submissions versus one.
PC-1492-1..4 and PC-84..100, every variant, remain pending as before. Mutation owns
deliberate mutants, red/restore/green and missing-control discovery.
