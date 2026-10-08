# CARD-1149/1150 S2 repair 2 (F4, F5, decision table) — Code report

Task `44fc0e5a` (Code, Final). Branch `feat/card-task-44fc0e5a`, worktree `/work/worktrees/task-44fc0e5a` (server2 mirror).
Branch starts at `e158cfe53333f4498c860bb49092957554f0f061`, the reviewed commit of Code task `ea5ef98c-75dd-4918-9513-f13a66b1680d`, which is the landing owner of this line.
Master base: `31632adc03b78956c7dc2d056280d7309681a7ff`. The branch was not rebased.
Fetched origin/master `65745cfa3aee43542fbeb6711996c769119028df`, which is later than the brief's `ce338f69766fe6a100acb835f67593713fdf564c`. `git merge-tree --write-tree HEAD origin/master` exits 0 with tree `e5537387348f3666dff513ae5ba528df35f14778`; against ce338f69 it exits 0 with tree `6f2846d8…`.

- Plan: `docs/superpowers/plans/2026-10-07-card-1149-1150-dispatch-recovery-plan.md`.
- Test design, amended with "Repair 4 evidence" and CP-69..CP-72: `docs/superpowers/plans/2026-10-07-card-1149-1150-test-design.md`.

## Commits (all pushed)

| SHA | What |
|---|---|
| `6c887c4510f93c8ed78a0fa49a53b393506f51bb` | F5 and the decision table. Pointer detection was still unchanged in this commit. |
| `51528b5e762b6dc60f268376261f478e46d95481` | F4: a spill pointer must be in the producer's exact form. |
| `c951feac9269d318c7695d89bbdffca19bb27edb` | Test-design rows CP-69..72, decision table and pending PCs. **Tested SHA.** |
| (this report) | Report only. |

Production change: `server/Application/Services/DispatchBriefEvidence.cs` only. `SessionMessageQueueService.DispatchBrief.cs` and `AgentTaskDispatcher.cs` are untouched, and there is no migration.

## Decision table (`DispatchBriefEvidence.Table`, first match wins)

| # | Rule | When | Verdict |
|---|---|---|---|
| 1 | task-not-open | task not Dispatched/Working | Uncertain, no hold |
| 2 | superseded | attempt/session/dispatch/start generation differs | Superseded |
| 3 | several-briefs | >1 current brief row | Uncertain, hold |
| 4 | unrecognized-evidence | no brief row, but another current row or marker prompt | Uncertain, hold |
| 5 | absent | no brief row, nothing else | Absent (insert) |
| 6 | **received** | complete current UserPrompt of the typed body **or** its payload | Received |
| 7 | spill-unavailable | body **is a spill pointer** and payload missing/conflicting/lacks marker or goal | Unavailable, hold |
| 8 | canceled | row canceled | Uncertain, hold |
| 9 | unattempted | Pending, never attempted | Reuse |
| 10 | attempted | attempted | AttemptOwned |
| 11 | unknown | anything else | Uncertain, hold |

`Observe` computes the facts. `Decide` is `Table.First(...)`, so dropping a rule is observable. Evidence order is task, then identity, then receipt, then payload, then shape.

Behaviour changes, all of which remove holds:

- A receipt now beats a released payload (F5).
- Inline bodies are never a spill claim (F4).
- A canceled row with a complete receipt is now Received; it was Uncertain and held.
- A remote delivery-time spill of an inline body is no longer held after release, because its body is its payload. This was a latent F5-like hold found in the scan.

A screen-only Delivered verdict without a complete transcript prompt is still not a receipt. This matches `RemoteSpillCourier.Inspect`.

## F4: false inline hold

**What changed.** Two things claim a spill. Nothing else does.

- The queue's own claim: `RemoteSpillRelativePath` is set and the typed body contains that path. This is the row-id inbox path the queue writes.
- A producer pointer read from its slot:
  - `BuildBriefPointer` full form: the body opens with `{this marker} role=`, and the headline `{this marker} YOUR BRIEF IS NOT IN THIS MESSAGE. It is {N} characters` starts a line, or follows a space in the joined form.
  - `BuildBriefPointer` compact form: the header line is followed by `Read the complete task brief at … before doing anything. Follow its reporting contract.` and then the marker.
  - `TypedBodySpill` form: its `YOUR MESSAGE IS NOT IN THIS MESSAGE. It is N characters` headline opens the body.
  - In every form, the location is read between `Read it in full before you do anything else:` and `Everything you need is there.`, and it must name a `.antiphon` file. A rooted slot path is read from disk, with either platform's root or separator. A quoted slot works too.

Every other body is inline and is its own payload. F2 still holds: a genuine pointer whose payload is missing, corrupt or unreadable is held, for both separators.

**Red proof on e158cfe5's classifier.** Production file checked out from e158cfe5 and the facts-table file set aside: 30 targeted cases failed.

- CP-71: all 15 cases.
- CP-70: 13 cases, every inline-* and lookalike-* case plus delivery-time-spill-released, together with the F5 cases below.
- Companions passed there, as designed: genuine-relative, compact, joined and queue pointers missing, api-fallback, windows missing and intact, retried-intact-spill, and remote-spill-conflict with its new input.

**Mutants.**

- `f4-loose-claim` (any `.antiphon/` or `.antiphon\` in the body is a claim): 14 red. These are 9 whitelist inline/lookalike cases and the 5 CP-71 `pending` cases. Received and Working cases stay green under this mutant because rule 6 decides first.
- `f4-column-claim` (drop "body contains column path"): delivery-time-spill-released red.

## F5: false remote hold

**What changed.** The receipt (rule 6) now comes before the payload (rule 7). The receipt compares the typed wire body, which is the queue's own receipt rule, and also the payload. A complete current-attempt UserPrompt means delivered, whatever happened to the bytes. With no receipt and a missing payload, the brief is still held.

**Real remote producer race (CP-72).**

1. The session is runner-bound and `RemoteSpillCourier` is registered.
2. A late producer's ensure starts and is paused in `BeforeDispatchBriefRowLock`.
3. The real dispatch ensure fits the brief against the runner ceiling, stages the spill, binds it to `.antiphon/inbox/<row>.md`, commits and delivers.
4. The queue records one complete UserPrompt and releases `RemoteSpillBody`. The test asserts each of these.
5. The task is set Working, then the late ensure resumes.

Outcomes:

- `complete`: Received, the task stays Working with no FailureReason and no Blocked event, one row with the same bytes, one prompt, one submission, and the session stays Running.
- `clipped` (reverse: no complete receipt, payload released): Unavailable and Blocked with `dispatch_brief_input_unavailable`; the row and prompt are unchanged and the session is Running.

**Red on e158cfe5:** CP-72 `complete`, whitelist remote-released-received, local-spill-deleted-received and canceled-received. The clipped and unreceived companions passed there.

**Mutant `f5-order`** (received moved after spill-unavailable): 4 red. These are received-payload-released, remote-released-received, local-spill-deleted-received and CP-72 `complete`.

## Table mutation check (each built, run on the exact method, reverted)

Run exactly `C1150_Brief_decision_table_flips_one_condition`; each mutant turned only its named cases red:

- task-not-open: task-closed
- superseded: attempt-changed
- several-briefs: two-briefs
- unrecognized-evidence rule: no-brief-other-evidence
- the rule's `RecognizedRows==0` conjunct: brief-and-other-evidence
- its `OtherCurrentEvidence` conjunct: no-brief
- absent: no-brief
- received: received, received-payload-released and received-canceled
- spill-unavailable rule: spill-unavailable and spill-released-unreceived
- its `SpillPointer` conjunct: inline-without-goal and inline-attempted-without-payload
- its `!PayloadIntact` conjunct: spill-intact
- canceled: canceled-after-attempt
- unattempted: pristine-unattempted, brief-and-other-evidence, inline-without-goal and spill-intact
- attempted: attempted and inline-attempted-without-payload
- unknown: neither-attempted-nor-unattempted, through `Sequence contains no matching element`

F1 (commit before probe) and F3 (real-producer race) are stateful, so they are not table facts. CP-60 and CP-63 remain their detectors and both are green.

## Scan for other false holds

Fixed:

- An inline brief with path mentions, Windows paths, quoted paths, or a quoted pointer from another task. Covered by CP-70/71.
- A remote delivery-time spill of an inline body after release.
- A canceled row that has a complete receipt.

Unchanged, listed for Review:

1. Two recognized rows for one attempt, for example a rules-bootstrap `SourceTaskId` row plus an `ExecutionTaskId` brief, or a released-seat answer. These are held as several-briefs. A single ensure producer cannot create this.
2. No brief row, but a current refinement, custody or marker-bearing row or prompt. Held as unrecognized-evidence.
3. A canceled row with no receipt. Held.
4. A Delivered verdict with no complete transcript prompt and a missing payload. Held; the queue never releases bytes without the prompt.
5. A local `TypedBodySpill` pointer whose cwd-relative inbox path has no column. Held, because the pure classifier has no cwd. No brief producer writes this today.
6. A runner pointer that never staged (`.antiphon/task-x-brief.md` with no column). Held, correctly.
7. A truncated payload that still contains the marker and goal. Accepted. This is the Review's open Backlog item; the pointer's `It is N characters` header could check it.
8. A goal edited after dispatch on a spill row. Held, as today.
9. An inline goal that contains this task's own full pointer, including its own marker, the length header and the slot, reads as a spill. This is the residual risk.
10. A retried delivery with an intact spill is AttemptOwned (covered). A new task attempt is Superseded or windowed (CP-9).

## Tests changed (inputs only, assertions unchanged)

- The whitelist case `remote-spill-conflict` now types a producer pointer naming the conflicting file instead of a bare path line.
- The `local-spill` seed of `C1150_Existing_brief_and_spill_are_byte_identical` now uses `BuildBriefPointer`.

Under F4 a bare path line is inline text, and `lookalike-bare-path-lines` pins that case. No assertion was weakened or deleted.

## Checkpoint run (ordinary scope)

The selection is the 36 commissioned rows, CP-58 (the S1 whitelist `AbsentLaunchPolicyTests`, 77 executions; added to show S1 unchanged) and new rows CP-69..CP-72. The rows are copied unchanged from the committed test design; only After and Build were regrouped into one build.

The whole Unit lane was **not** run: the brief excludes it, and the predecessor's ordinary scope replaces it. CP-42 is again replaced by CP-67/CP-68.

The run started with `start --plan <scratch>/selection.md --after S2-R4 --serial --expected-source-sha c951feac…`.

- Run `20261008-060700-8f08`: **GREEN exit 0**, 41 green, 0 red, 0 skipped, **685 executions**, wall 36m54s.
- One build `bin-c1150-r4/`: lease `d1c97a58-6d66-4333-88d9-03565732a399`, waited 0s, about 118s.
- The tool reported `unlisted: none`.
- `validate --evidence report.json --expected-source-sha c951feac…` returned `CHECKPOINT SOURCE VALID source=c951feac9269d318c7695d89bbdffca19bb27edb rows=41`.
- Statement budgets from the CP-67 output: held-dispatched-tick 18, working-live-tick 18, inside-grace-absent-scan 4.

Per-row counts, all failed=0 skipped=0 slot=granted waited=0s:

- CP-6 1, CP-7 3, CP-8 4, CP-9 1
- CP-60 1, CP-61 5, CP-62 12, CP-63 1
- **CP-69 18, CP-70 51, CP-71 15, CP-72 2**
- CP-1 1, CP-2 1, CP-3 5, CP-4 3, CP-5 3
- CP-37 3, CP-39 4, CP-40 1, CP-41 3
- CP-58 77, CP-67 49, CP-68 114
- CP-43 3, CP-44 25, CP-45 85, CP-46 8, CP-47 67, CP-48 7, CP-49 15, CP-50 25, CP-51 17
- CP-52 3, CP-53 1, CP-54 3, CP-55 3, CP-56 6
- CP-64 4, CP-65 2, CP-66 33

Unedited CHECKPOINT lines are in `.antiphon/checkpoints/20261008-060700-8f08/report.md`, which is gitignored and held as evidence. Here are the new rows:

```
CHECKPOINT CP-69 commit=c951feac9269d318c7695d89bbdffca19bb27edb build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Brief_decision_table_flips_one_condition* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-44fc0e5a/.antiphon/checkpoints/20261008-060700-8f08/rows/CP-69/run.trx slot=granted waited=0s dirty=0 source=c951feac9269d318c7695d89bbdffca19bb27edb sourceState=clean buildSource=verified
CHECKPOINT CP-70 commit=c951feac9269d318c7695d89bbdffca19bb27edb build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Brief_evidence_whitelist_flips_one_condition* executed=51 passed=51 failed=0 skipped=0 trx=/work/worktrees/task-44fc0e5a/.antiphon/checkpoints/20261008-060700-8f08/rows/CP-70/run.trx slot=granted waited=0s dirty=0 source=c951feac9269d318c7695d89bbdffca19bb27edb sourceState=clean buildSource=verified
CHECKPOINT CP-71 commit=c951feac9269d318c7695d89bbdffca19bb27edb build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Inline_brief_mentioning_spill_paths_is_not_held* executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-44fc0e5a/.antiphon/checkpoints/20261008-060700-8f08/rows/CP-71/run.trx slot=granted waited=0s dirty=0 source=c951feac9269d318c7695d89bbdffca19bb27edb sourceState=clean buildSource=verified
CHECKPOINT CP-72 commit=c951feac9269d318c7695d89bbdffca19bb27edb build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Late_ensure_after_remote_receipt_and_payload_release* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-44fc0e5a/.antiphon/checkpoints/20261008-060700-8f08/rows/CP-72/run.trx slot=granted waited=0s dirty=0 source=c951feac9269d318c7695d89bbdffca19bb27edb sourceState=clean buildSource=verified
CHECKPOINT CP-58 commit=c951feac9269d318c7695d89bbdffca19bb27edb build=reused filter=/*/*/AbsentLaunchPolicyTests/* executed=77 passed=77 failed=0 skipped=0 trx=/work/worktrees/task-44fc0e5a/.antiphon/checkpoints/20261008-060700-8f08/rows/CP-58/run.trx slot=granted waited=0s dirty=0 source=c951feac9269d318c7695d89bbdffca19bb27edb sourceState=clean buildSource=verified
CHECKPOINT CP-68 commit=c951feac9269d318c7695d89bbdffca19bb27edb build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_* executed=114 passed=114 failed=0 skipped=0 trx=/work/worktrees/task-44fc0e5a/.antiphon/checkpoints/20261008-060700-8f08/rows/CP-68/run.trx slot=granted waited=0s dirty=0 source=c951feac9269d318c7695d89bbdffca19bb27edb sourceState=clean buildSource=verified
```

## Other builds and runs (unlisted, with reason; all through the slot gate, all waited 0s)

| Run | Reason | Lease | Held | Outcome |
|---|---|---|---|---|
| build `bin-c1150r2/` @6c887c45 | authoring | 96b68fbe | 126s | 0 errors |
| run 3 new methods | authoring | d4e98549 | 52s | 55/55 passed |
| build `bin-c1150r2/` @51528b5e | authoring | 9ec4f27d | 121s | 0 errors |
| run `C1150_*` | authoring | 8ef6aceb | 78s | 114/114 passed |
| build `bin-c1150r2m/` (e158cfe5 classifier + new tests, facts file aside; source restored after build) | red proof | 7539d549 | 171s | 0 errors |
| run whitelist + CP-71 + CP-72 + CP-7 methods | red proof | c68adf58 | 55s | 71 executed: 30 failed as intended, 41 passed |
| build attempt (mutant 1) | script argv bug (`-nodeReuse` split, MSB1041); nothing built | e5da1ead | 1s | failed, source restored |
| run after that (stale output) | discarded, **not evidence** | fae644f5 | 5s | 18 passed |
| 20 mutant builds `bin-c1150r2/`, one per mutant, reverted before each run | mutation checks above | f4d46ab5 a7fd3f12 45d740c0 3b232817 ac927ea5 a9aaa243 28a01af0 85505d8b fdfd0ea1 9526750a 67db7316 33da341e a1797589 dc33737e 67a2cf15 dd45227b 1058593b 67d1358d 8ce3de7c eed3de8f | 45–200s | all 0 errors |
| 20 mutant runs | mutation checks | 986f4038 70efd1f4 3836a4f9 74e0400f 8eceed01 872d150f 13e7e07c c8a9bef7 e2bc7b18 4e418ee4 9f353712 64a82363 1f7a9fd3 fc2dd6b4 218bf72f 39b55683 50f2c057 6751aacf bd3fd009 89eb4f51 | 4–55s | as listed above |
| build `tools/Antiphon.Checkpoints` → `bin-c1150r4drv/` | checkpoint tool bootstrap | 74347843 | 4s | 0 errors, 1 warning |

Two of the mutant runs need explaining:

- The first `f5-order` and `f4-loose-claim` runs (39b55683, 50f2c057) selected **0 tests**: a method-level OR without `*` matches nothing. They are not evidence. Both mutants were rebuilt and rerun with `(Name*)|(Name*)`, with the results above.
- Mutant run results: the first fourteen are the table mutants, 1–4 failed each as listed. Then 0 selected, 4F/67P (f5-order), 0 selected, 14F/52P (f4-loose-claim), and 1F/50P (f4-column-claim).

All `bin-c1150r2*`, `bin-c1150r2m` and `bin-c1150r4drv` directories were deleted: 56 + 1 directories. The checkpoint tool deleted `bin-c1150-r4/` itself. Source was restored after every mutant; git status was clean before the checkpoint run.

## Invariants

- This repair adds no stop, release, fail or relaunch path, and S2 itself adds none. That is narrower than "nothing stops a Working session": the inherited boot-stall tail (CARD-1151, `TryFailBootStallAsync`, characterized by CP-37 `Aged_prompt_only_Working_tick_stops_the_session_and_requeues_once`) still stops and requeues an aged prompt-only Working session today, and is being replaced separately. CARD-0079 remains the only automatic stop the session contract allows; the boot-stall tail is the known, tracked exception (D-7). CP-72 asserts only that its tested ensure path leaves the session Running. (Corrected by CARD-1150 S2 repair 3, F9.) *Qualified by repair 4 (Review 818f247a F10): "S2 itself adds none" was wrong until repair 4. A failed "brief re-queued" save after S2's backfill typed the brief killed and failed a Working recipient; repair 4 (task 6da8a413) makes every resume step after input non-destructive.*
- `AgentTaskDispatcher.cs` is untouched: an empty diff for e158cfe5..HEAD.
- The ensure path and its queries are unchanged. Budgets are 18/18/4.
- The boot-stall tail, lines 3211–3324, has SHA-256 `59f5909b7e5b80ddad0bace8bea4279771072d541d909186e09940113c18b711` at e2c51501, 31632adc, e158cfe5 and HEAD.
- The S1 whitelist is unchanged: CP-58 77/77 and CP-67 49/49.
- F1 (CP-60), F2 (CP-61/62 and the whitelist Windows cases) and F3 (CP-63) are all green. No migration.
- Platform: `GET $ANTIPHON_API/api/runner-defaults` and `/api/session-runners` returned 200 (globalRunnerId server2). No runner or platform was pinned.
- Evidence guard: `scripts/check-evidence-diff.ps1 -BaseRef 31632adc… -HeadRef HEAD` at c951feac gave commits=9 entries=1 violations=0, exit 0. The final message records the rerun after this report.

## Pending SourceLanding Mutation

None of these is discharged by Code. They are named in the test design, "Repair 4 evidence":

- PC-T/<rule>: 11 rules.
- PC-T/<condition>: 4 conjuncts.
- PC-F5/order.
- PC-F4/claim and PC-F4/column.
- PC-F4/slot variants: length header, this-task marker and line start, TypedBodySpill opening, file location, compact and joined parsing. Code did not run these.
- PC-F1, PC-F2/separator, rooted, quoted and reader, PC-F3, PC-6..PC-9 and every earlier PC and variant.

## Restart

Server code changed, so the AppHost needs a restart after land; the landing caller owns it. Runner: none.
