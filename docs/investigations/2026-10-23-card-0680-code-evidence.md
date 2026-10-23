# CARD-0680 partial Code: classifier and send-guard witnesses

Original Code/landing owner: `52a5dc76-feb4-4349-acaa-e3ca459b918c`.
Branch: `feat/card-task-52a5dc76`.
Worktree: `/work/worktrees/task-52a5dc76`.
Base: `5a0cfd71324e2ff52c93e81b35004fc58e769f62` (clean assigned master start).
Plan: [shared Claude dialog observation](../superpowers/plans/2026-10-23-card-0680-claude-dialog-send-guard-plan.md).
Design/PC inventory: [Code verification design](../superpowers/plans/2026-10-23-card-0680-verification-design.md).

## Outcome and limitation

Partial, not landable. The bounded pure classifier and compiling runner refusal
witnesses are pushed. Runner enforcement, input-history capture, HTTP/phone-home
propagation/capability, server admission and writer migration remain unimplemented.
The new request/snapshot types are scaffolding, not an advertised working guard.
No production sender consumes the classifier yet. Server queue/direct-service
refusal tests have been authored but have not compiled or executed.

The checkpoint executor lost access to the task-owner API at 00:41:12 UTC on
2026-10-23. Repeated reads returned HTTP 502; it honored the three-minute ownership
uncertainty bound and ended `owner-unverified` (exit 7). CP-2/CP-3 did not execute.
That is infrastructure failure, not a qualifying red witness. The plan requires
those witnesses before implementation; no task-authority bypass was attempted.
The API was still returning 502 at 00:47 UTC. Required execution is incomplete.

`ClaudeDialogObservation` caps rendered frames at 65,536 characters/512 rows,
reuses the existing prompt detector, distinguishes Unknown from Clear and accepts
a live bottom composer separately from historical dialog text. It emits only
state/kind/cancel metadata, with no affirmative key or command text. Question
navigation hints were added to the existing detector; synthetic question fixtures
do not qualify real CLI layouts. Explicit question-kind refinement remains owed.

The two runner methods use the production runtime and a controlled child with
recorded input. They compile with optional protocol fields and a primitive rendered
screen seam, while the production conditional writer still lacks the new guards.
CP-4 tests real queue/expectation refusal before attempt custody and a full receipt
control. CP-5 currently covers runtime/channel/maintenance/configured-watchdog
entry points; boot, retry, interactive/startup and RC variants must still be added.
No new timeout, weakened assertion, retry or automatic control byte was introduced.

## Evidence and costs

The required platform reads succeeded before work and again around 00:40 UTC:
defaults revision 2, eligible Linux default and Windows runner, temporary runner
unavailable. No fleet address is embedded in source. Windows eligibility does not
release the commissioning hold on desktop supervisor PID 133964. No desktop,
release directory, live Claude session, restart or deployment was touched.

One unlisted tool bootstrap was required because this worktree had no checkpoint
binary. `scripts/build-slot.ps1 -Label c680-checkpoint-bootstrap -- dotnet build
tools/Antiphon.Checkpoints --property:OutputPath=bin-c680-tool/ --nologo` succeeded:
4 existing warnings, 0 errors; slot=granted, waited=0s, held=45s. Every selected
test/build thereafter used the checkpoint tool with committed HEAD as expected SHA.

Run `20261023-003552-baf4`, source
`fcb5f1275c880db42ef8723d195adbff423840e5`: CP-1 executed once and failed at the
intended assertion: old permission text above a live composer was Blocked instead
of Clear. Fresh TRX identifies the intended class/method and 1 failed, 0 skipped.
Build slot wait 15s; row wait 30s; build 45.086s; test 0.418s. Executor completed,
and its owned outputs/tool were removed through the checkpoint custody path.

```text
CHECKPOINT CP-1 commit=fcb5f1275c880db42ef8723d195adbff423840e5 build=ok filter=/*/*/ClaudeDialogObservationTests/C680_OnlyCurrentBoundedDialogsBlock executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-52a5dc76/.antiphon/checkpoints/20261023-003552-baf4/rows/CP-1/run.trx slot=granted waited=30s dirty=0 source=fcb5f1275c880db42ef8723d195adbff423840e5 sourceState=clean buildSource=verified
```

Run `20261023-003945-c249`, source
`5a2b4dcacbc7ac472a756fdb287269e82b34245e`: CP-1 passed, 1 executed/1 passed/
0 failed/0 skipped, fresh exact-method TRX. Classifier build 23.229s, test 0.029s,
slot=granted and waited=0s. Runner build also succeeded with a granted slot and
zero wait, proving the two new runner methods compile. Owner verification failed
before either runner test could launch. Overall exit 7 is not green.

```text
CHECKPOINT CP-1 commit=5a2b4dcacbc7ac472a756fdb287269e82b34245e build=ok filter=/*/*/ClaudeDialogObservationTests/C680_OnlyCurrentBoundedDialogsBlock executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-52a5dc76/.antiphon/checkpoints/20261023-003945-c249/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=5a2b4dcacbc7ac472a756fdb287269e82b34245e sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=5a2b4dcacbc7ac472a756fdb287269e82b34245e build=n/a filter=/*/*/ClaudeDialogInputGuardTests/C680_EveryWriteRechecksTheCurrentDialog executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-3 commit=5a2b4dcacbc7ac472a756fdb287269e82b34245e build=n/a filter=/*/*/ClaudeDialogInputGuardTests/C680_InputProofInvalidatesOnWriteFailureAndAdoption executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a2b4dcacbc7ac472a756fdb287269e82b34245e sourceState=clean buildSource=unknown
```

Receipt validation rejected that report with `cleanup_refused`; there is no
validated clean certificate. After executor death was confirmed, the two exact
task-owned output roots were inspected: no links or tracked paths, 1439 files /
227273718 bytes for observation and 576 files / 103239326 bytes for runner.
Files and then empty directories were removed individually. The historical
receipt was not edited. Generated evidence remains ignored.

This report is frozen before the last checkpoint attempt. Its outcome, exact
report-bearing SHA, receipt validation and final Git checks belong to the stored
task report `.antiphon/task-52a5dc76.md`; no successful final qualification is
predicted here.

## Acceptance disposition and continuation

| IDs | Actual disposition |
|---|---|
| V-1 / R-1 | CP-1 intended red then 1 green; synthetic question layouts, no native qualification; combined-run receipt rejected, final certificate owed |
| V-2 / R-2 | CP-2/CP-3 compiled, not executed; runner/input-proof implementation and transport coverage owed |
| V-3 / R-3 | CP-4/CP-5 initial tests authored, not compiled/executed; production S2 owed |
| V-4 / R-4 | CP-5 incomplete; CP-6/CP-7 not run; interactive and exact-negative exceptions owed |
| V-5 / R-5 | CP-8/CP-9 native watchdog tests and executions owed to native lanes per brief |
| V-6 / R-6 | CP-10/CP-11 belong to parent owner 38c4e51a, not executed here |

Whole Unit, whole suites and broader classes were explicitly deferred by the
commissioning brief. They are not passed. This remains an incomplete Final
request, never an Interim approval. No assembly-wide run was made.

PC-1 through PC-9 and every named variant in the companion design remain pending
for SourceLanding Mutation, including discovery of missing controls. No deliberate
mutant was run. Existing parent evidence is preserved: Claude 2.1.309 / UnixPty
permission Esc passed; Ctrl+C failed before input on unreadable Working composer.
Neither parent operation is enabled by this work.

Resume this original Code owner after the task API is healthy. Run CP-2/CP-3 red
before implementing S1, then CP-4/CP-5 red before S2. Complete all writer variants,
positive submitted-input proof and ordinary rows; retain native obligations.
The first ordinary Review is still required after implementation/ordinary V/R.
Next is code, not review or land. Restart now: none. Eventual activation requires
both server and runner guard/capability on the reviewed RC plus native witnesses.
