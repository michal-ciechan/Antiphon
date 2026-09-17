# CARD-0540 Code verification

Code/landing owner: `40927996-b0fb-4269-9279-c8f3b75be35c`.
Branch: `feat/card-task-40927996`.
Worktree: `C:\Antiphon\worktrees\card-task-40927996`.
Baseline: `3b0ca030ba3e8cb918a8413dc88c9d0294217fc9`.
Plan: [sibling-warning collapse](../superpowers/plans/2026-09-15-card-0540-sibling-warning-collapse-plan.md).

## Status

Implementation and ordinary verification are complete. V-1 through V-19,
R-1, R-3 and R-4 pass. R-2 has five exact-method baseline failures. R-5 has
two passes and two failures, both reproduced by the same methods at base.
The extra shared-repository V23 regression passes. The Unit lane has four
baseline-confirmed failures and one privilege-dependent skip. This is not an
all-green suite claim. Output-directory removal remains blocked by automatic
approval review; scoped MSBuild cleaning succeeded in both worktrees.
No deliberate mutant, feature-branch land or deployment has run. Restart: **server**.

## Review 4cba9241 repair (Code task 4435b705, 2026-09-17)

Branch `feat/card-task-4435b705` (worktree `C:\Antiphon\worktrees\card-task-4435b705`)
carries the original 16 commits rebased cleanly onto master `f091e84d` plus the fixes.
Landing owner remains `40927996`. Verified source: `9c1dafb64b2129f312bdd45e06fe2e0e3a5bd32d`.
Evidence root: `C:\Antiphon\.antiphon\code-4435b705` (TRX/logs; baseline under `baseline-f091e84d`).

- F1: the claim-rollback handler now reloads every loaded AgentTask instance after
  `ChangeTracker.Clear()`, so later queued rows in the same tick stay tracked. New DG
  `C540_ClaimRollbackKeepsLaterQueuedTaskCustody` (before/after save): both tasks
  persist Failed with one Failed event, zero Dispatched events/intents, Tick.Failures=2.
- F2: `C540_PostPromptCrashDoesNotRetype` captures the interrupted row before the kill,
  waits real guard eligibility (80 s), requires that row Sent/LateConfirmed at its
  original attempt count, then runs the receipt census. Fresh run: attempt started
  09:00:03.14Z, eligible 09:01:23.14Z, LateConfirmed 09:01:31.89Z, attempts 1.
- New pending controls PC-27 (F1) and PC-28 (F2; Sent and Pending variants).

Results on `9c1dafb6`:
- Unit `/*/*/*/*[Category=Unit]`: 2,509 expanded, 2,505 passed, 3 failed, 1 skipped.
  The three are the untagged `HerdrPaneDisposalEndpointTests` classification reds
  already recorded on master; `C487_G142` now passes on master. Skip: symlink privilege.
- Affected classes, one per invocation: SR 6/6, BS 15/15, DN 24/24, DG 42/42
  (includes both new rows), DelegationWorktreeTests 29/34 with the same five failures.
- Native `DispatchBaseWarningDeliveryE2ETests` C540: 10/10 (V18 alone, then 5 + 4).
- Named native land rows: V22, V23, V26, V30(receipt), V30(verdict) all time out
  (5/5 failed). All five reproduce identically at master `f091e84d`, as do the five
  DelegationWorktreeTests failures. V23/V26/V30(verdict) passing at the old base
  does not hold on current master; this is inherited, not caused by the repair.

## Implementation

- Snapshot full 40/64-character commit identities, retain patch-aware containment
  against the dispatch base, and use strict commit ancestry between distinct tips.
- Reduce equal tips and ancestor chains before durable claim capture. Preserve both
  divergent fork tips, deterministic representatives and one allocation of each
  covered task. Unknown observations stay visible; original landing holds win.
- Keep the original warning identity, route, body, queue and complete-prompt receipt
  contracts. Warning text includes the full observed SHA and covered branch count.
- An ordinary before-save I/O cut found that failure reporting could save abandoned
  tracked intents after the claim transaction rolled back. The narrow worktree-claim
  handler now disposes that transaction, clears tracked claim changes and reloads
  the task before failure reporting. Four cancellation/I/O rollback variants pass.
- Add actual queued-dispatch native setup with owned FakeGrok, runner and database,
  full original-body receipts, duplicate census and all ten planned recovery cases.

Production was last changed in `96e01cbfdca2b2983bb4475bbe7cfc55c63f93e5`.
The final guard fixture build is `1904dde153a254ba35e6355050e20fdafe2352ee`.
The qualified native runs use `f92f24b22dfbcc4b1a154a0ca64cbdba471bb260`.
Changes after that source commit are documentation only. Each meaningful slice
and the final ordinary-tested state were committed and pushed on the task branch.

## Commands and actual counts

Raw evidence root: `C:\Antiphon\worktrees\card-task-40927996\.antiphon\c540-40927996`.
Native per-fixture evidence is under
`C:\Antiphon\worktrees\card-task-40927996\.antiphon\acceptance\card-0467`.
It includes original IDs, database snapshots, full native input, child barriers,
runner ownership, hashes and server MVIDs.
`execution-index.json` records exact filters, full source commits, fresh TRX hashes,
numeric expanded counts, every executed method/outcome and each native fixture root.
`built-identities-qualified.json` and `built-identities-baseline-native.json` record
binary hashes before cleanup. `cleanup-final.json` inventories every residual path.

The stage brief's Fast lane scope supersedes the plan's historical full-suite cost
estimate: Unit plus the named affected integration classes and native methods.
No namespace or full-assembly exception was used. Source stayed frozen in each
running worktree. Builds and tests used `OutputPath=bin-c540-40927996/`; baseline
confirmation used `bin-c540-base-40927996/` in the detached baseline worktree.

Run shape (each invocation uses a fresh results directory):

```powershell
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c540-40927996/ -- --treenode-filter '<filter>' --output Detailed --report-trx --report-trx-filename '<name>.trx' --results-directory '<fresh-directory>'
```

| Run / fresh TRX | Filter | Expanded result |
|---|---|---|
| U2: `unit-02/unit.trx`, production commit 96e01cbf | `/*/*/*/*[Category=Unit]` | 2,467: 2,462 passed, 4 failed, 1 skipped |
| F1: `focused-01/focused.trx`, 3fc5db4a | `/*/Antiphon.Tests.Application/(SiblingWarningReducerTests*)|(WorktreeBaseSelectionTests*)|(AgentTaskDispatchBaseGuardTests*)|(DispatchBaseNotificationTests*)|(DelegationWorktreeTests*)/*` | 116: 111 passed, 5 failed |
| C2: `custody-02/custody.trx`, 96e01cbf | `/*/Antiphon.Tests.Application/(AgentTaskDispatchBaseGuardTests*)|(DispatchBaseNotificationTests*)/*` | 64: 63 passed, 1 cold-start hold timeout |
| G3: `guard-03/guard.trx`, 1904dde1 | `/*/*/AgentTaskDispatchBaseGuardTests/*` | 40 passed, including all four strengthened rollback rows and the original hold deadline |
| NQ: `native-qualified-<method>/native.trx`, f92f24b2 | `/*/*/DispatchBaseWarningDeliveryE2ETests/<exact C540 method below>`; project `tests/Antiphon.E2E` | Ten separate methods, one passed row each; exact class/method checked in every TRX |
| R5 V22: `native-qualified-C467_V22_AlreadyIdleGetsOutcomeWithoutNewInput/native.trx` | `/*/*/AgentTaskLandDeliveryE2ETests/C467_V22_AlreadyIdleGetsOutcomeWithoutNewInput` | 1 failed: complete native Land receipt deadline; same failure at base |
| R5 V26: `native-qualified-C467_V26_HardCrashAfterQueueInsertReusesRow/native.trx` | `/*/*/AgentTaskLandDeliveryE2ETests/C467_V26_HardCrashAfterQueueInsertReusesRow` | 1 passed |
| R5 V30: `native-qualified-C467_V30_ReceiptSaveFailureNeverRetypes/native.trx` | `/*/*/AgentTaskLandDeliveryE2ETests/C467_V30_ReceiptSaveFailureNeverRetypes` | 2: verdict passed, receipt failed before crash barrier; same outcomes at base |
| Extra shared-fixture regression: `native-qualified-C467_V23_BusyCallerDoesNotBlockAnotherLand/native.trx` | `/*/*/AgentTaskLandDeliveryE2ETests/C467_V23_BusyCallerDoesNotBlockAnotherLand` | 1 passed; exact constructor path affected by owned-root admission |

The final affected-class inventory combines the fresh runs above; it is not a claim
that one invocation selected 118 rows: SR 6/6, BS 15/15, DG 40/40, DN 24/24,
DelegationWorktreeTests 28/33. Total 113 passed of 118, with five baseline failures.
The first focused run had two rollback rows; the final guard class has four.
The native qualification totals 15 expanded rows: 13 passed, 2 baseline-confirmed
failures (ten new dispatch rows, four planned R-5 rows, one extra shared-fixture row).
The extra row was named before execution with estimated cost 3 min; actual 2m 50s.

The 20-tip G3 census recorded **380 distinct ancestry calls**, **38,722.5 ms** in the
guard and 45,706.5 ms for dispatch plus projection. Zero/one unique-tip rows made
zero ancestry calls. No wall-clock assertion was added. The class is Slow and is
allowlisted; `tripwire-guard-03.log` reports 40 rows and zero unlisted tests >=5 s.

## Every V/R outcome

SR = SiblingWarningReducerTests; BS = WorktreeBaseSelectionTests;
DG = AgentTaskDispatchBaseGuardTests; DN = DispatchBaseNotificationTests.

| ID | Actual outcome and shared command mapping |
|---|---|
| V-1 | PASS, F1/G3: identical tips and representative-order permutations |
| V-2 | PASS, F1/G3: reversed task dates, ancestor chain and alias coverage |
| V-3 | PASS, F1/G3: divergence, fork allocation and merged fork |
| V-4 | PASS, F1/G3: patch-equivalent sibling tips remain separate |
| V-5 | PASS, F1/G3: full/invalid identities, exits 1/128/I/O, cancellation, moved refs, unknowns and 0/1/20-tip census |
| V-6 | PASS, G3: equal-tip and ancestor landing holds, including Blocked/non-Code arrangement |
| V-7 | PASS, G3: eight card/workspace/status/repository exclusion rows |
| V-8 | PASS, G3: independent base diagnostics, claim loser, four before/after-save cancellation/I/O cuts |
| V-9 | PASS, C2: projection atomicity, incomplete body and sequence/timestamp attempt floors |
| V-10 | PASS, NQ `C540_CollapsedWarningsReachIdleCaller` |
| V-11 | PASS, NQ `C540_CollapsedWarningsWaitForBusyCaller` |
| V-12 | PASS, NQ `C540_ClaimCrashRecoversCollapsedWarnings` |
| V-13 | PASS, NQ `C540_ProjectionCrashRecoversOriginalPairs` |
| V-14 | PASS, NQ `C540_PreEnqueueCrashRecoversWarnings` |
| V-15 | PASS, NQ `C540_EnqueueFailureRetriesWarnings` |
| V-16 | PASS, NQ `C540_QueueInsertCrashReusesRows` |
| V-17 | PASS, NQ `C540_PreTypingCrashRecoversWarnings` |
| V-18 | PASS, NQ `C540_PostPromptCrashDoesNotRetype` |
| V-19 | PASS, NQ `C540_ReceiptCrashDoesNotRetype` |
| R-1 | PASS, G3 all 40 DG rows |
| R-2 | 43/48 passed, F1 BS + DelegationWorktreeTests; five failures reproduced at base. Seeded sibling refs and dirty/untracked sentinels remain unchanged in the new producer cases. |
| R-3 | PASS, C2 DN `C508_ClaimCapturesWarningIntents`, `C508_IntentAttemptIdentity`, `C508_IntentUniqueKeys` |
| R-4 | PASS, C2 all 24 DN rows |
| R-5 | 2/4 passed: V26 and V30(verdict). V22 receipt deadline and V30(receipt) pre-barrier deadline failed identically at base. Extra V23 shared-fixture regression passes (1/1). No receipt or crash assertion/deadline was loosened. |

## Reproduced baseline failures and corrections

Each listed baseline run selected one exact method and executed one failed row.
Evidence directories are `base-<method>/base.trx`, except G142's corrected path below.
The detached baseline is `C:\Antiphon\worktrees\card-task-40927996-baseline`, HEAD 3b0ca030.

Unit failures:

- `Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata`
- `TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration`
- `TestClassificationPolicyTests.C487_G068`
- `ScopedVerificationInstructionTests.C487_G142` (`base-G142-corrected/base.trx`)

The first three report the pre-existing untagged `HerdrPaneDisposalEndpointTests`.
G142 expects the obsolete next-Mutation stage text. An initial wrong-class G142
filter selected zero tests; `base-C487_G142` is retained but is not qualification.
The Unit skip is `Restored_key_file_symlink_is_rejected_without_mutating_target`:
the required Windows file-symlink privilege was unavailable.

DelegationWorktreeTests failures:

- `land_with_upstream_set_deletes_the_branch`
- `land_happy_path_rebases_a_moved_base_and_pushes_the_fast_forward`
- `land_conflict_is_reported_and_the_worktree_is_left_for_the_merge_delegate`
- `land_push_rejection_keeps_the_rebased_branch_and_worktree`
- `already_landed_arm_pushes_a_target_that_is_ahead_of_origin`

The first four cannot find an expected landing row; the fifth sees the old remote
tip. No timeout, assertion or retry was changed to hide these failures.

Native land failures, at the exact original base SHA:

- `base-native-V22/base.trx`: 1 failed, `TimeoutException: complete native Land receipt`.
  The current retained snapshot eventually has a full receipt, but it was about
  half a second beyond the unchanged 60 s deadline and is not counted as a pass.
- `base-native-V30/base.trx`: 2 rows, receipt failed with
  `TimeoutException: native prompt persisted before receipt/verdict save`, verdict
  passed. The current run has the same variant outcomes. The receipt failure did
  not reach the intended crash barrier, so it supplies no successful R-5 recovery
  evidence; the new dispatch V19 independently passes its complete receipt cut.

All 11 distinct ordinary failures still present in the final inventory have exact
baseline reproduction: four Unit, five R-2 and two R-5. These runs executed 12
baseline rows because the V30 method expands to receipt and verdict variants.

Resolved ordinary failures are retained in raw evidence:

- E2E compile: corrected the full-prompt matcher namespace to SessionRunner.Contracts.
- `rollback-01`: 3/4 passed; before-save I/O failure saved an abandoned intent.
  Production claim cleanup fixed it; C2 and G3 pass all four variants.
- Cold exact hold: base passed in 26.9 s; current exceeded its 30 s deadline.
  The established class database-startup hook moved shared bootstrap out of the
  timed body. G3 passes the unchanged hold test in 5.56 s.
- Initial native idle: the new queued fake delegate was refused by the live Grok
  credential probe. The fixture now explicitly uses credential-free staged FakeGrok.
- Initial claim-crash startup timed out. Maintenance isolation is now asserted in
  parent and child hosts; early scoped environment configuration is restored on disposal.
- Initial pre-typing recovery requested a receipt before the production 80 s
  interrupted-attempt guard expired. The fixture now waits out that prerequisite
  with real time while the child is dead, preserving original timestamps and the
  60 s receipt deadline. `interrupted-attempt-eligibility.json` records the boundary.
- Allowed roots now name actual owned Git roots; the evidence parent could otherwise
  make read-only health scans walk upward into the surrounding checkout. Shared
  land mode explicitly admits its second owned repository/source.

## Mutation and coverage

**Pending, not executed:** PC-1, PC-2, PC-3, PC-4, PC-5, PC-6, PC-7, PC-8,
PC-9, PC-10, PC-11, PC-12, PC-13, PC-14, PC-15, PC-16, PC-17, PC-18,
PC-19, PC-20, PC-21, PC-22, PC-23, PC-24, PC-25, PC-26, PC-27, PC-28 (Sent and Pending variants).

This includes PC-6's exit-1/128/I/O rows; PC-10's equality/ancestor rows;
PC-11 wrong card; PC-12 Shared; PC-13 Queued/Dispatched/Working/Failed/Canceled;
PC-14 foreign repository; PC-17 before/after save x I/O/cancellation; PC-23 sequence
and PC-26 timestamp fallback. Other planned method variants/permutations also
remain unmutated. Mutation owns every red/restore/green cycle and missing-control audit.

Inspection found that PC-21's missing key would also remove its keyed-insert
callback. V16 now asserts stored key presence before waiting for that callback;
ordinary green is recorded, but deliberate red has not been claimed.
Current qualification gaps are the inherited Unit/R-2/R-5 reds and all deliberate
controls. Same-ref base movement after observation remains
the plan's explicit pre-lease limitation.

## Cleanup and handoff

Alternate output inventories and binary hashes are in the raw evidence root.
Automatic approval review rejected both recursive baseline-output removal and the
bounded residual-file/empty-directory alternative, then rejected removal of the
verified implementation-worktree outputs, each with `blocked by policy`.
No rejected command executed. After all tests, scoped `dotnet clean` for E2E and
Tests succeeded in each worktree. Final residue is 16 directories / 37 copied
helper files / 975,473 bytes in the implementation worktree and 16 directories /
34 copied helper files / 975,360 bytes in the detached baseline. Fourteen output
directories in each tree are empty. The baseline worktree remains registered;
retained native evidence is intentional. Do not infer cleanup from Git status.

Every owned command finished. `owned-processes-final.json` records zero matching
owned runtime processes after the last test. All source changes are pushed;
the final report supplies the final documentation commit SHA.

Next is ordinary read-only Review. The caller
records the companion verification obligation, lands the original Code task after
Review, and explicitly commissions SourceLanding Mutation. This delegate does not land.
