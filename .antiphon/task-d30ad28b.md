# CARD-1149/1150 S2 repair 3 (F6-F9 and the envelope corpus): Code report

Task `d30ad28b-b332-416f-8fa3-ccc421e067f6` (Code, Final). Branch `feat/card-task-d30ad28b`, runner mirror worktree `/work/worktrees/task-d30ad28b`.

- Branch base: `7495dd5d57b2030fcabc6263cc97e70fa02c619f`, the commit reviewed by b66d3382. The branch was not rebased.
- Landing owner: original Code task `ea5ef98c-75dd-4918-9513-f13a66b1680d`.
- origin/master after `git fetch`: `9f5f004f0f31a77c143642d0d7274d3bed42557c`. `git merge-tree --write-tree HEAD origin/master` exits 0 with tree `16ffc868b66c8cf4673c1008bf5b505d6200969b`.
- Plan: `docs/superpowers/plans/2026-10-07-card-1149-1150-dispatch-recovery-plan.md`, unchanged.
- Test design: `docs/superpowers/plans/2026-10-07-card-1149-1150-test-design.md`. New section "Repair 5 evidence (S2 F6-F9)" holds the producer-form history, the receipt rule, the corpus matrix, rows CP-73/CP-74 and the pending controls.

## Commits (all pushed)

| SHA | What |
|---|---|
| `2765bae9a46951fe673224f415df4d05d5374441` | F9: report corrections |
| `2b7f7f0466d02b2e233a023aea641a23f75b10ae` | F6 and F7: outer-envelope parser, plus 15 whitelist rows |
| `f6b19d5643e25bf027d7f496bdf775c7a9c6b98c` | F8: receipt floor and projection, 6 whitelist rows, and queue method CP-74 |
| `0f38808ef10b83bda5cb0a91be337110127b652f` | Corpus CP-73 |
| `e3c17c709e33f96686d119c15b810c94aad41a09` | Missing control found by mutant M5: joined pointer carrying another task's marker |
| `ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df` | Test-design note. **Tested SHA.** |
| (this report) | Report only |

Production files:

- `server/Application/Services/DispatchBriefEvidence.cs`
- `server/Application/Services/SessionMessageQueueService.DispatchBrief.cs`: projection only. Two existing queries now also select `TranscriptEntry.Sequence` and `LastDeliveryBaselineSequence`; no new statement.

`AgentTaskDispatcher.cs` has an empty diff for 7495dd5d..HEAD. There is no `AgentTaskDispatcher.BootStall.cs` on this branch. The boot-stall tail, lines 3211-3324, still has SHA-256 `59f5909b7e5b80ddad0bace8bea4279771072d541d909186e09940113c18b711`. No migration.

## Decision table

`DispatchBriefEvidence.Table` is unchanged: 11 rules, received before spill-unavailable before shape. Only two facts are now computed differently: `SpillPointer` (F6, F7) and `CompleteReceipt` (F8).

## F6: legacy pointers

**History.** `git log -S` and `git log -L` over `BuildBriefPointer` and `TypedBodySpill.BuildPointer` give these producer forms:

- eb24e568f: bare headline, whole reporting contract tail, no closing marker.
- 8c42ebd3e: short report section and closing marker.
- ad258cd41: joined one-line form with a quoted slot.
- dedd0ed97 / c4a3c371c / 76d45ed24: `scope=` / `areas=` / `verification=` header keys.
- ba72905e3, 597f43c77, d3bc892c6, f6856040a, c753cb8d7: role report sections. All open with `--- how to report back ---`.
- 563568e60: this task's marker added to the headline. Runner-relative path.
- 7fb7bc5cb and dc5e34dbd: compact form.
- TypedBodySpill 00ad9463c: bare `YOUR MESSAGE`. From 7851254ea it has an opening and closing marker.

**Change.** The full and joined forms accept the headline bare or after this task's marker. Another task's marker is still inline, so F4 stays.

**Red on 7495dd5d.**

- Whitelist rows: legacy-pointer-missing, -attempted-missing, -windows-, -unc-, -joined- and -corrupt.
- Queue case legacy-missing-payload.
- Corpus: full-legacy, full-legacy-contract, joined-legacy and readonly-legacy.
- Companions that pass there by design: legacy-pointer-intact, lookalike-other-marker-headline, and queue legacy-intact-payload.

## F7: retry handoffs

**Change.** The full form is read only from its outer envelope:

1. A single header line `{marker} role=…`.
2. A blank line, then one title paragraph.
3. A blank line, then the headline paragraph, with the length and slot.
4. After the slot close, the read-only line or `--- how to report back ---`.

Joined form: the first headline on the line, with no other task's marker immediately before it. Compact and `TypedBodySpill` keep their whole-body anchors. A pointer in a goal, a fenced, nested or quoted previous report, or a pointer without the report tail is inline.

**Red on 7495dd5d.**

- Whitelist rows: retry-handoff-own-pointer, -attempted, -nested-quotes, -fenced-headline, goal-quotes-own-pointer and lookalike-pointer-without-report-tail.
- Queue cases: retry-handoff-pending and retry-handoff-working. The Working case asserts Working, no FailureReason and no Blocked event.
- Corpus: every inline-goal-* and inline-retry-* form.
- Companion: retry-outer-pointer-missing (attempt 2, genuine pointer, missing payload) is held both before and after.

## F8: stale undated receipts

**Change.** A prompt counts as the current attempt's receipt only when all of these hold:

- It is on the same session.
- If the row has `LastDeliveryBaselineSequence`, the prompt's `Sequence` is above it. This is the queue's own late-confirm floor.
- If it is dated, the timestamp is inside the dispatch window.
- If it is undated, a known floor is the only proof. With no floor it is not a receipt (fail closed).

The absent rule's "possibly current" evidence is unchanged, so an undated marker prompt still prevents a second insert.

**Red on 7495dd5d.**

- Whitelist rows: stale-undated-receipt-retry (attempt 2 pending, attempt-1 prompt), stale-undated-receipt-under-floor and dated-receipt-under-floor.
- Queue case: stale-undated-receipt.
- Corpus: every undated-stale and undated-proven cell for Pending rows, and every dated-under-floor cell for attempted rows.
- Companions that pass there: undated-receipt-proven-sequence (counts), dated-receipt-before-dispatch (does not count), dated-receipt-above-floor, and queue proven-undated-receipt and stale-dated-receipt.

**Behaviour widening (stated).** The floor now also excludes a dated, in-window prompt at or below the row's last delivery baseline. Before, that was Received. It only matters when the payload is missing, and then the brief is held. This is the queue's own rule.

## F9: false safety statement

- `.antiphon/task-44fc0e5a.md:206` now says that S2 and its repairs add no stop, release, fail or relaunch path. It names the inherited CARD-1151 boot-stall tail (CP-37), which still stops and requeues an aged prompt-only Working session.
- `.antiphon/task-ea5ef98c.md:32` also qualifies "off the tick path": ensure is absent from the steady held/young tick and the absent scan, and runs on claim, boot-wedge, reuse/refocus and backfill.
- A grep of every file changed on the branch found no other unqualified "nothing stops" claim. The test-design lines that mention the boot-stall tail are already qualified. The boot-stall code is untouched.

## Corpus (CP-73)

The corpus test is `C1150_Brief_envelope_corpus_matches_the_expected_matrix`: 48 cases, 3,248 combinations. Every form above, plus 13 inline and lookalike forms, is built with the real producers. Each is crossed with attempt 1 and 2, Pending and attempted (floor 10), and seven receipts:

- none
- dated-current
- undated-proven
- undated-stale
- dated-old
- dated-under-floor
- clipped

Payloads are present, missing and corrupt. Locations are local POSIX, Windows and UNC, remote-bound, remote-staged and local-relative. Expected verdicts come from hand-written form, location, receipt and verdict tables, carried verbatim in the test and the note. The run reported 3,248 combinations with 0 mismatches. On 7495dd5d's logic, 1,166 of 3,220 combinations mismatched; that was before the 48th form was added. The mismatch list is in `/tmp/antiphon-code-d30ad28b/red1-mismatch.txt`.

These combinations keep 7495dd5d's verdict ("unknown keeps today's behaviour"):

- A markerless legacy `TypedBodySpill` with no retained payload: Uncertain, held.
- A remote-staged or local-relative pointer: Unavailable unless received, because the server cannot read the path.
- A title containing a blank line: read as inline.
- An inline goal whose second paragraph is a full pointer with length, slot and report tail: read as a spill, which is fail-closed. No producer writes it.
- A truncated payload that keeps the marker and goal: still accepted. This is separate debt.

## Mutation checks

Each mutant was built to `bin-c1150r5m/`, run on CP-70, CP-73 and CP-74, then reverted with `git checkout -- server/`. Batches combined only mutants whose red sets are disjoint.

| Batch | Mutants | Red (whitelist / queue) | Corpus red |
|---|---|---|---|
| B1 | M1 multi-line bare rejected; M4 no tail check; M7 undated-without-floor counts | legacy ×5 + queue legacy-missing; lookalike-pointer-without-report-tail; stale-undated-receipt-retry | 46/48 |
| B2 | M2 joined bare rejected; M3 headline anywhere after `\n` (the F7 defect); M6 floor ignored | legacy-joined; retry-handoff ×3, goal-quotes-own-pointer, legacy ×5, queue retry ×2 + legacy; stale-undated-under-floor, dated-under-floor, queue stale-undated | 46/48 |
| B3 | M5 joined foreign marker accepted; M8 window ignored; M9 undated never counts | lookalike-joined-other-marker-headline; dated-receipt-before-dispatch; undated-receipt-proven-sequence + queue proven-undated | 46/48 |
| B4 | M10a Sequence not projected | queue proven-undated | 0 (pure test) |
| B5 | M10b baseline not projected | queue proven-undated | 0 |
| B6 | T-spill: spill-unavailable rule removed | 20 whitelist rows, all F6/F8 Unavailable rows included; queue legacy-missing, stale-undated, stale-dated | 33/48 |
| B7 | T-order: received after spill-unavailable | dated-receipt-above-floor, undated-receipt-proven-sequence, local-spill-deleted-received, remote-released-received; queue proven-undated | 32/48 |
| B8 | M3+M4 together | adds retry-handoff-fenced-headline, which is guarded by both layers, to M3's and M4's sets | 13/48 |

Notes on the mutation runs:

- Every new case is red on 7495dd5d or under a named mutant, except two F4/F2 guard companions that no mutant of this repair can break: lookalike-other-marker-headline and retry-outer-pointer-missing.
- M5 initially had no detector. That control was added in `e3c17c70`.
- The first B4 attempt did not compile (CS0854: optional argument in an expression tree). Its run used the stale B3 binary and is **not evidence**. The mutant was fixed to `(long?)null` and rerun as B4 above.

The table rule mutants from repair 2 (PC-T/*) were not all rerun; the table is unchanged. T-spill and T-order were rerun against the new rows.

## Checkpoint run (ordinary scope)

The selection is the 41 rows of b66d3382's run, with filters copied unchanged, plus new rows CP-73 (48) and CP-74 (7). CP-70 goes from 51 to 73 and CP-68 from 114 to 191. After/Build are regrouped to `S2-R5` and one build, `bin-c1150-r5/`. Selection file: `/tmp/antiphon-code-d30ad28b/selection.md`.

`start --plan <selection> --after S2-R5 --serial --expected-source-sha ecdec7bd…`

- Run `20261008-092502-2d94`: **GREEN exit 0**. 43 green, 0 red, 0 skipped, **839 executed, 839 passed**. Wall 47m11s, sequential-equivalent 44m55s. One build, max concurrent builds 1, `unlisted: none`.
- Build lease `791f4e97-fc2f-4fa8-8ffb-7839046772f4`, waited 0s, build about 129s, 0 errors. Every row: slot=granted, dirty=0, sourceState=clean, buildSource=verified.
- `validate --evidence report.json --expected-source-sha ecdec7bd…` → `CHECKPOINT SOURCE VALID source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df rows=43`.
- Budgets in CP-67's TRX: held-dispatched-tick=18, working-live-tick=18, inside-grace-absent-scan=4.
- CP-37 ran all three boot-stall characterization cases, including the inherited stop/requeue.

The whole Unit lane was **not** run. It is not part of this brief's ordinary scope, and AGENTS.md forbids whole-Unit runs here. No whole-assembly run.

Evidence: `.antiphon/checkpoints/20261008-092502-2d94/` (gitignored), copied to `/tmp/antiphon-code-d30ad28b/20261008-092502-2d94/`.

Per-row executed counts, all passed, failed=0 skipped=0:

- CP-6 1, CP-7 3, CP-8 4, CP-9 1
- CP-60 1, CP-61 5, CP-62 12, CP-63 1
- CP-69 18, **CP-70 73**, CP-71 15, CP-72 2, **CP-73 48**, **CP-74 7**
- CP-1 1, CP-2 1, CP-3 5, CP-4 3, CP-5 3
- CP-37 3, CP-39 4, CP-40 1, CP-41 3
- CP-58 77, CP-67 49, **CP-68 191**
- CP-43 3, CP-44 25, CP-45 85, CP-46 8, CP-47 67, CP-48 7, CP-49 15, CP-50 25, CP-51 17
- CP-52 3, CP-53 1, CP-54 3, CP-55 3, CP-56 6
- CP-64 4, CP-65 2, CP-66 33

Slot waits were 0s except: CP-61 15s, CP-69 15s, CP-70 75s, CP-72 15s, CP-73 30s, CP-74 15s, CP-1 15s, CP-2 15s, CP-3 30s, CP-4 45s and CP-5 30s.

Unedited CHECKPOINT lines:

```
CHECKPOINT CP-6 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=ok filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Concurrent_producers_ensure_one_brief* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Existing_brief_and_spill_are_byte_identical* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Uncertain_evidence_cannot_create_a_replacement* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Old_attempt_evidence_does_not_suppress_current_brief* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-60 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Working_probe_failure_keeps_the_committed_brief* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-60/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-61 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Local_spill_must_be_present_and_intact* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-61/run.trx slot=granted waited=15s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-62 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Spill_pointer_forms_fail_closed* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-62/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-63 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Real_producers_race_to_one_brief_and_keep_the_followup* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-63/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-69 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Brief_decision_table_flips_one_condition* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-69/run.trx slot=granted waited=15s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-70 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Brief_evidence_whitelist_flips_one_condition* executed=73 passed=73 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-70/run.trx slot=granted waited=75s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-71 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Inline_brief_mentioning_spill_paths_is_not_held* executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-71/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-72 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Late_ensure_after_remote_receipt_and_payload_release* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-72/run.trx slot=granted waited=15s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-73 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Brief_envelope_corpus_matches_the_expected_matrix* executed=48 passed=48 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-73/run.trx slot=granted waited=30s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-74 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Outer_envelope_and_receipt_floor_through_the_queue* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-74/run.trx slot=granted waited=15s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Absent_launch_is_blocked_with_original_input* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Hold_is_once_and_automatic_relaunch_bound_is_zero* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-2/run.trx slot=granted waited=15s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Listed_or_unknown_runner_is_never_absence* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-3/run.trx slot=granted waited=30s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Changed_or_working_attempt_is_untouched* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-4/run.trx slot=granted waited=45s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Different_reason_or_attempted_brief_still_uses_failure_policy* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-5/run.trx slot=granted waited=30s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/BootStallWorkingTickCharacterizationTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-39 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Native_attempt_keeps_the_failure_path* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-39/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Failed_hold_does_not_persist_on_a_later_save* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-40/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-41 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Caller_note_has_one_complete_user_prompt* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-41/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-58 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/AbsentLaunchPolicyTests/* executed=77 passed=77 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-58/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-67 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_* executed=49 passed=49 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-67/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-68 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_* executed=191 passed=191 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-68/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-43 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DelegationBriefRecoveryTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-44 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/AgentTaskDeadSessionReconciliationTests/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-44/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-45 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/AgentTaskDeliveryWatchdogTests/* executed=85 passed=85 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-45/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-46 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/AgentSessionInterruptedLaunchResumeTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-46/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-47 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/SessionReconciliationServiceTests/* executed=67 passed=67 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-47/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-48 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/AgentSessionLaunchQueueOwnershipTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-48/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-49 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/AgentTaskDispatchFailureTests/* executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-49/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-50 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/AgentTaskConcurrencyLimitTests/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-50/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-51 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/AgentTaskDispatcherPredicateTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-51/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-52 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/PhoneHomeRollingRunnerTests/C1125_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-52/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-53 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/DispatcherSweepLifetimeRegistrationTests/Program_wires_both_sync_debt_sweeps_into_the_dispatcher* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-53/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-54 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-54/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-55 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/SessionTerminationSourcePersistenceTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-55/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-56 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/AgentTaskLivenessTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-56/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-64 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/RepairSourceDispatchTests/C1115_* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-64/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-65 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/BlockedTaskParkReplyAdmissionTests/C1144_* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-65/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
CHECKPOINT CP-66 commit=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df build=reused filter=/*/*/BlockedTaskParkReplyAdmissionTests/C1146_* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-d30ad28b/.antiphon/checkpoints/20261008-092502-2d94/rows/CP-66/run.trx slot=granted waited=0s dirty=0 source=ecdec7bd90f2ac78b3cdc2bef7ba275293daa6df sourceState=clean buildSource=verified
```

## Other builds and runs (unlisted, with reason; all through the slot gate)

| Run | Reason | Lease | Wait / held | Outcome |
|---|---|---|---|---|
| build `bin-c1150r5/` @ working tree (F6-F8 + corpus) | authoring | 0504504c | 0s / 127s | 0 errors |
| run corpus + whitelist + table + CP-62 methods | authoring | 85edb7ca | 0s / 3s | 149/149 |
| run `C1150_*` | authoring | 2fbe018c | 0s / 105s | 189/189 |
| build `bin-c1150r5m/` (7495dd5d logic + 2 optional record fields) | red proof | 8ee0dbdf | 0s / 295s | 0 errors; source restored |
| run corpus + whitelist + queue methods | red proof | 765528b8 | 15s / 76s | 126 executed: 64 failed as intended, 62 passed |
| build `bin-c1150r5/` @ M5 control | authoring | 4e1f3919 | 0s / 127s | 0 errors |
| run corpus + whitelist | authoring | 7980d0b1 | 0s / 3s | 121/121 |
| mutant builds B1..B8 + failed B4 | mutation checks | cae4b026, 19a95f72, 52fd93d9, f3e29a1d (CS0854, failed), 204371a4, 6edaf80c, b309f38e, c36ef774, 38f48437 | 0s / 53-128s | 0 errors except the noted failure |
| mutant runs B1..B8 + stale B4 | mutation checks | 8d099294, d45d9bdc, bbbe3be5, 2540b428 (stale binary, **not evidence**), 0a7b3d34, 00e9026a, 175f1da0, 86e0388c, 75e670b0 | 0s / 50-81s | 128 executed each: 54, 62, 50, —, 1, 1, 56, 37, 27 failed |
| build `tools/Antiphon.Checkpoints` → `bin-c1150r5drv/` | checkpoint tool bootstrap | 6e3d38f5 | 0s / 6s | 0 errors, 1 warning |

Cleanup:

- All `bin-c1150r5`, `bin-c1150r5m` and `bin-c1150r5drv` directories were deleted (56 + 1). The checkpoint tool deleted `bin-c1150-r5/`.
- Source was restored after every mutant. `git status` was clean before and after the checkpoint run.
- I accidentally copied three scratch copies into `/tmp` root and removed them at once.

## Invariants

- CARD-0079 remains the only automatic stop the session contract allows. **This repair and S2 add no stop, release, fail or relaunch path.** *Qualified by repair 4 (Review 818f247a F10): at this commit that statement was wrong. S2's interrupted-launch backfill typed the brief before the resume saved its "brief re-queued" event, so a failed save reached the launch-failure catch and killed and failed a Working recipient. Repair 4 (task 6da8a413) makes every resume step after input non-destructive; see the test design's "Repair 6 evidence (S2 F10)".* The inherited CARD-1151 boot-stall tail, which CP-37 characterizes, still stops an aged prompt-only Working session. It is untouched here and being replaced separately.
- The ensure is unchanged: it is absent from the steady-state tick and the absent scan.
- Statement budgets 18/18/4.
- S1 whitelist unchanged: CP-58 77/77, CP-67 49/49. F1-F5 rows green.
- No assertion was weakened or deleted. CP-70 and CP-68 minimum counts were raised, not lowered. No test inputs were changed this round.
- Platform: `GET /api/runner-defaults` and `/api/session-runners` returned 200. No runner or platform pin.
- Evidence guard: `scripts/check-evidence-diff.ps1 -BaseRef 31632adc03b78956c7dc2d056280d7309681a7ff -HeadRef HEAD` at ecdec7bd gave commits=16 entries=4 violations=0. It was rerun after this report commit; see the final message.

## Pending SourceLanding Mutation

None is discharged by Code.

- PC-F6/bare: M1, M2.
- PC-F6/foreign-marker: M5.
- PC-F7/position: M3, and M3+M4 for retry-handoff-fenced-headline.
- PC-F7/tail: M4.
- PC-F8/floor: M6.
- PC-F8/undated: M7, M9.
- PC-F8/window: M8.
- PC-F8/projection: M10a, M10b.
- PC-T/<rule> ×11, PC-T/<condition> ×4, PC-F5/order, PC-F4/claim, column and slot variants, PC-F1, PC-F2/*, PC-F3, PC-6..PC-9 and every earlier PC and variant.

## Restart

Server code changed, so the AppHost needs a restart after land. The landing caller owns it. Runner: none.
