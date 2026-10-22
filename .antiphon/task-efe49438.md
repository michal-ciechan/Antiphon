# CARD-1521 Code report

Implementation is pushed, but Final verification could not finish within the
60-minute task budget. This is an incomplete Code handoff, not a green result.

- Original Code task / landing owner: `efe49438-7e31-4b87-8c99-39353812aa21`.
- Branch: `feat/card-task-efe49438`.
- Worktree: `/work/worktrees/task-efe49438`.
- Base: `1ccdd40501d135a7759d3adea3da9dc9e5392803`.
- Latest source: `85c518873e1cbe947392f1d1f1995fe540785ed8`.
- Plan: `/work/worktrees/task-efe49438/docs/superpowers/plans/2026-10-22-card-1521-complete-codex-pointer-plan.md`.
- Committed pre-checkpoint report: `/work/worktrees/task-efe49438/docs/investigations/2026-10-22-card-1521-code.md`.
- Unbound Mutation candidate: `/work/worktrees/task-efe49438/docs/superpowers/mutations/2026-10-22-card-1521-complete-pointer.md`.

## Behavior

The scoped queued Delegation Codex pointer path now requires the whole normalized
body in the active bottom composer, rechecks after settling, and sends a separate
Enter. A complete generation/floor-bound UserPrompt is the only delivery receipt;
the generic screen-only fallback is excluded. Recovery preserves the original
attempt and reconciles receipts before any input. Partial, empty, working or
unreadable evidence holds the row and later automation; canceled typed pointers
reuse the shared composer-hold helper. A complete idle composer permits Enter-only
recovery, and a positively changed generation permits the ordinary fresh write.
Complete consumption retains bounded cold NoSubmitOutput recovery with factual
incident wording. No timeout was widened and no assertion was loosened.

The fixture retains the actual 676-character pointer and ANSI captures of 237/193
consumed characters. Both old predicates accept those captures. The guarded
actions are emitted CR, duplicate body input, persistent attempts, and recovery.

## Required outcomes and deferred work

- CP-1 / V-1 / V-2 red: 27 executed, 9 controls passed, 18 failed, zero skipped,
  source `5e27e3593fd757fb0486ecff603728f0550af691`. Both captures fail the no-CR
  assertion with actual one CR; Pending fails on duplicate input. The complete
  per-argument inventory is retained below. Source fingerprint eligibility was
  checked with `Test-CheckpointSourceEvidence` and returned true.
- CP-2 / V-1 / V-2 and CP-3 / R-1: INCOMPLETE, zero test executions on the final
  source. Final run `20261022-233504-f5f7` was stopped through the checkpoint
  ownership guard at approximately 23:42:55 UTC as the budget expired. Source
  and Antiphon.Tests compiled, but the isolated driver was still building its
  nested channel-outbound probe output; no completed build receipt or fresh
  CP-2/CP-3 TRX exists. No green assertion or inherited-failure claim is made.
- CP-4 / V-3: NOT RUN / OWED to Windows. Desktop sample observed at
  `2026-10-22T23:30:24.0198057Z`: available memory 4,966,420,480 bytes, CPU 100%,
  sample state stale. Earlier 22:45:27 sample was 4,621,852,672 bytes / 100% CPU.
  Neither proves admission above the explicit 6 GB floor. The qualification
  supervisor PID 133964 was not touched; C:\Antiphon\releases was not read.
  Native measurement implementation is compiled by the portable build but only
  the Windows row can qualify its native timing/receipt/owned-process exit.
- Unit, whole assembly and full affected classes: deferred by the explicit
  minimal-testing brief, not passed. Only the five named R-1 methods are selected.
- PC-1 and PC-2: every variant pending method-scoped SourceLanding Mutation.
  No deliberate mutant was applied. Mutation owns red/restore/green and discovery
  of missing controls, including the scoped no-receipt deadline boundary.
- Restart: server after reviewed landing and RC publication; no restart performed.

## Integration checks

`scripts/check-evidence-diff.ps1` over full base..latest source: six commits, one
approved fixture entry, zero violations. All six commits were pushed separately.

Fetched master `b8f0ce088b1ab6d7b8aebfb16f85e95b81d3a904` and RC
`0714a9a89e4bff65aae7e0b4b94098694aeb931c`. `merge-tree --write-tree
origin/master HEAD` does NOT pass: add/add conflict in the plan document (only
the required pinned-TUnit wildcard suffixes differ). No merge/rebase was made.

Sequential per-commit three-way patch application to a temporary index seeded
from origin/rc/2026-10-20 does NOT apply cleanly: the RC lacks the plan, so the
checkpoint-filter commit fails. Repeating with the base plan blob seeded into
that index gets through the four test commits, then the implementation conflicts
in `SessionMessageQueueService.cs` and `SessionMessageQueueService.Expectations.cs`.
RC port therefore requires prerequisite reconciliation; no target was changed.

## Cost, provenance and tooling limitations

The checkpoint CLI was bootstrapped once through the build-slot gate; subsequent
calls used the existing DLL, whose build/test drivers each acquire slots. No
unlisted test selection or full assembly was run. During an early run, this task's
identified idle outer launcher lease was released while its driver stayed gated.
That wrapper later reported release 404; it did not authorize an unleased driver.

Earlier records are preserved: missing-import compilation failure, zero-test OR
filter discovery failure, and the first 27-case red run whose second capture
precondition incorrectly treated whitespace as a substantive missing byte. The
precondition was repaired and the final CP-1 reproduced all intended red actions.
The last two red builds cost 395.5 and 373.0 seconds; their test hosts cost 108.2
and 102.2 seconds. No repeat-proof repetition was requested after green.

S2 run `20261022-233125-ee2a` at c6ed50a027e08b50f5a70155496ffc14c44883ce
failed compilation on one missing Dtos import (43.34 seconds). The checkpoint
was explicitly stopped through its ownership guard before tests while its owner
poll was timing out; its slot had been released. After repair, run
`20261022-233423-5537` refused pre-existing output (zero tests, slot granted,
waited=0s). The task-owned output directories were checked for tracked files and
links, then removed before the next run. No source was edited during an active run.

Historical reports contain `cleanupRefusals: ["still-present"]`. Their unedited
CHECKPOINT lines report clean source/verified build fingerprints, but the strict
full-report validator rejects cleanup_refused. This limitation is not disguised
as a fully qualified green receipt. Generated reports and TRX remain ignored.

## Historical checkpoint lines and TRX inventory

Final run logs/state: `/work/worktrees/task-efe49438/.antiphon/checkpoints/20261022-233504-f5f7`.
It held slot `eeddc5ef-e826-4f61-818d-80c87131dfa9`, label `build:bin-c1521-green`,
slot=granted waited=0s, for roughly 7.5 minutes before the owned stop. Its owner
poll recorded three 12-second HTTP timeouts. Stop returned success, then the
foreground waiter exited 6 because no completed report existed. The executor,
build and its known MSBuild children were confirmed absent. There are no final
CHECKPOINT lines to quote for this interrupted run; the earlier refusal lines
below remain historical, unedited evidence.

Continue Code on the pushed source: run CP-2/CP-3 with exact expected SHA,
inspect all 27 arguments and the five R-1 method results, fix any actual red,
and rerun the red rows. CP-4 remains owed subject to Windows admission. Preserve
the source/cleanup and master/RC integration limitations above. Review follows
completed ordinary portable verification. No human choice is needed.


Run 20261022-224906-0cba

CHECKPOINT CP-1 commit=c331e1ae8a82968f768c9f60bc842d09cd0cf317 build=failed filter=/*/Antiphon.Tests.Application/SessionMessageQueueCodexPointerTests/(C1521_Initial_consumption_is_complete)|(C1521_Initial_transcript_is_authority)|(C1521_Recovery_never_stacks_a_pointer)|(C1521_Recovery_reconciles_before_input) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=c331e1ae8a82968f768c9f60bc842d09cd0cf317 sourceState=clean buildSource=unknown

Run 20261022-225509-d4f2

CHECKPOINT CP-1 commit=fba66341ed1e3df8923c1f6b4d35a3eff211a4f4 build=ok filter=/*/Antiphon.Tests.Application/SessionMessageQueueCodexPointerTests/(C1521_Initial_consumption_is_complete)|(C1521_Initial_transcript_is_authority)|(C1521_Recovery_never_stacks_a_pointer)|(C1521_Recovery_reconciles_before_input) executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-efe49438/.antiphon/checkpoints/20261022-225509-d4f2/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=fba66341ed1e3df8923c1f6b4d35a3eff211a4f4 sourceState=clean buildSource=verified

Run 20261022-230119-c892

CHECKPOINT CP-1 commit=b69d76eaf37d5648f217eb896e08b76a9edcc51e build=ok filter=/*/Antiphon.Tests.Application/SessionMessageQueueCodexPointerTests/(C1521_Initial_consumption_is_complete*)|(C1521_Initial_transcript_is_authority*)|(C1521_Recovery_never_stacks_a_pointer*)|(C1521_Recovery_reconciles_before_input*) executed=27 passed=9 failed=18 skipped=0 trx=/work/worktrees/task-efe49438/.antiphon/checkpoints/20261022-230119-c892/rows/CP-1/run.trx slot=granted waited=150s dirty=0 source=b69d76eaf37d5648f217eb896e08b76a9edcc51e sourceState=clean buildSource=verified

Run 20261022-231329-30c2

CHECKPOINT CP-1 commit=5e27e3593fd757fb0486ecff603728f0550af691 build=ok filter=/*/Antiphon.Tests.Application/SessionMessageQueueCodexPointerTests/(C1521_Initial_consumption_is_complete*)|(C1521_Initial_transcript_is_authority*)|(C1521_Recovery_never_stacks_a_pointer*)|(C1521_Recovery_reconciles_before_input*) executed=27 passed=9 failed=18 skipped=0 trx=/work/worktrees/task-efe49438/.antiphon/checkpoints/20261022-231329-30c2/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=5e27e3593fd757fb0486ecff603728f0550af691 sourceState=clean buildSource=verified

Run 20261022-233423-5537

CHECKPOINT CP-2 commit=85c518873e1cbe947392f1d1f1995fe540785ed8 build=failed filter=/*/Antiphon.Tests.Application/SessionMessageQueueCodexPointerTests/(C1521_Initial_consumption_is_complete*)|(C1521_Initial_transcript_is_authority*)|(C1521_Recovery_never_stacks_a_pointer*)|(C1521_Recovery_reconciles_before_input*) executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: pre-existing-output dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-3 commit=85c518873e1cbe947392f1d1f1995fe540785ed8 build=failed filter=/*/Antiphon.Tests.Application/(SessionMessageQueueBootWedgeTests*)|(SessionMessageQueueWedgedHeadTests*)|(ExpectationDirectDeliveryTests*)/(Codex_cold_first_delivery_NoSubmitOutput_relaunches_once*)|(a_non_codex_delivery_verified_kind_takes_the_same_recovery*)|(C0266_Cleared_composer_releases_after_a_canceled_typed_row*)|(C650_Ghost_empty_composer_on_one_snapshot_keeps_holding_while_the_next_shows_the_body*)|(C650_Input_uses_normalized_paste_and_separate_enter*) executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: pre-existing-output dirty=unknown source=unknown sourceState=unknown buildSource=unknown

Final CP-1 per-argument results:

- C1521_Initial_consumption_is_complete(capture-first): Failed
- C1521_Initial_consumption_is_complete(capture-second): Failed
- C1521_Initial_consumption_is_complete(prefix-10): Failed
- C1521_Initial_consumption_is_complete(prefix-14): Failed
- C1521_Initial_consumption_is_complete(missing-last-byte): Failed
- C1521_Initial_consumption_is_complete(complete-120): Passed
- C1521_Initial_consumption_is_complete(complete-80): Passed
- C1521_Initial_consumption_is_complete(partial-then-complete): Passed
- C1521_Initial_consumption_is_complete(history-only): Failed
- C1521_Initial_consumption_is_complete(complete-swallowed-enter): Passed
- C1521_Initial_transcript_is_authority(complete-current): Failed
- C1521_Initial_transcript_is_authority(clipped-current): Failed
- C1521_Initial_transcript_is_authority(complete-stale): Failed
- C1521_Recovery_never_stacks_a_pointer(pending): Failed
- C1521_Recovery_never_stacks_a_pointer(interrupted-sent): Failed
- C1521_Recovery_never_stacks_a_pointer(send-now): Failed
- C1521_Recovery_never_stacks_a_pointer(canceled-before-next): Failed
- C1521_Recovery_never_stacks_a_pointer(capped-before-next): Failed
- C1521_Recovery_never_stacks_a_pointer(expectation-direct): Failed
- C1521_Recovery_reconciles_before_input(complete-idle): Passed
- C1521_Recovery_reconciles_before_input(working): Passed
- C1521_Recovery_reconciles_before_input(unreadable): Passed
- C1521_Recovery_reconciles_before_input(two-empty-screens): Failed
- C1521_Recovery_reconciles_before_input(new-generation): Passed
- C1521_Recovery_reconciles_before_input(late-complete-receipt): Passed
- C1521_Recovery_reconciles_before_input(legacy-refunded-stamp): Failed
- C1521_Recovery_reconciles_before_input(changed-generation-stale-receipt): Failed

Latest integration recheck: master 6b09efc3a577b3065ce061cd9803437b2123fd2b, RC unchanged. At final source 85c518873e1cbe947392f1d1f1995fe540785ed8 merge-tree still reports the same plan-only add/add conflict. Full output is .antiphon/card-1521-master-merge.txt.

All pushed slices:

c331e1ae8a82968f768c9f60bc842d09cd0cf317 test(card-1521): capture partial Codex pointer and retry red witnesses
fba66341ed1e3df8923c1f6b4d35a3eff211a4f4 test(card-1521): fix queue witness namespace imports
b69d76eaf37d5648f217eb896e08b76a9edcc51e test(card-1521): make checkpoint OR filters discover on pinned TUnit
5e27e3593fd757fb0486ecff603728f0550af691 test(card-1521): distinguish next substantive byte in captured prefix
c6ed50a027e08b50f5a70155496ffc14c44883ce fix: gate queued Codex pointers on complete active composer consumption
85c518873e1cbe947392f1d1f1995fe540785ed8 fix: retain transcript authority through pointer confirmation deadlines

PC-1 pending variants: capture-first, capture-second, prefix-10, prefix-14, missing-last-byte, complete-120, complete-80, partial-then-complete, history-only, complete-swallowed-enter. PC-2 pending variants: pending, interrupted-sent, send-now, canceled-before-next, capped-before-next, expectation-direct.

The stopped task-owned alternate output was removed after checking for tracked files and links. Generated checkpoint evidence was retained. Git worktree is clean; the pushed branch tip was verified with git ls-remote.


Owned build slot eeddc5ef-e826-4f61-818d-80c87131dfa9 was explicitly released after all owned processes exited (HTTP 204). The release helper printed held=0s because the reconstructed cleanup lease had no stopwatch; actual grant-to-stop elapsed time is approximately 7.5 minutes in executor.log.

Resume using the existing checkpoint DLL run command with --plan docs/superpowers/plans/2026-10-22-card-1521-complete-codex-pointer-plan.md --rows CP-2,CP-3 --expected-source-sha 85c518873e1cbe947392f1d1f1995fe540785ed8. If the tool is rebuilt, put that bootstrap driver through scripts/build-slot.ps1. Keep source frozen and wait every run to completion.

