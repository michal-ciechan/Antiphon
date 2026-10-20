# CARD-1308 Code evidence

Implemented the bounded early review-handoff recheck and all four missing
temporary-block delivery cases. Ordinary verification for the explicitly
commissioned narrow Final scope is complete: 17 passed, zero failed/skipped.
Initial ordinary Review is next. Mutation remains paused and unqualified.

## Source and ownership

- Original Code task / landing owner: `3a56669b-8c0d-466f-a6ac-03f070761628`.
- Branch: `feat/card-task-3a56669b`.
- Worktree: `/work/worktrees/task-3a56669b`.
- Task base: `b11fe390863ea2c9dd9a683568859cc195ea4b13`.
- Plan: `docs/superpowers/plans/2026-10-20-card-1308-handoff-wait-plan.md`.
- First regression commit: `f7a3e043164c3f9104425002fa1793d631e93522`.
- Producer identity assertion correction, before any test run:
  `f8406dbefe753dde41c052b9f4e019bfa9e1df89`.
- Production change, bounded-retry guard and temporary-block tests:
  `ec3b9feb2ad1606bd2d26ab02748022cec98cf1b`.
- Corrected combined class selector / actual green tested source:
  `21c293a28802a75ae9ce27dfbd766f779b250c6f`.
- Every slice was committed and pushed to the assigned task branch before its
  checkpoint. This evidence-only follow-up does not alter tested code or selection.

Master only. The landed plan found the same coordinator blob on master and both
`rc/2026-10-15` and `rc/2026-10-18`; neither rc line was changed here.
Restart: **server**, when activation is separately authorized. No restart or
deployment was performed.

## Change and ordinary outcomes

The coordinator shares the existing newer-request predicate and tie ordering.
Coalescible boundary/status actions retain their old replacement behavior.
A deferred typed `review:` action instead executes its own normal queue phase
with `superseded: false`, retaining its identity and late-confirm phase.
An explicit Deferred disposition ends the drain after saving the ordinary retry,
so the still-present successor cannot cause 128 early retries in one page.
No queue gate, timeout, assertion ceiling, schema or ownership policy was relaxed.

| Invariant | Actual outcome |
|---|---|
| A-1 early progress | Three pre-fix assertion failures, then all three variants passed: `boundary-delivery`, `status-delivery`, `boundary-finished`. Original producer queue/thread identity is asserted; delivery requires a complete stored UserPrompt. |
| D-2/D-3 identity and bounded retry | One guard passed: no-newer hint does not retry; newer work is routed; a still-Working handoff increments attempts exactly once, retains both debts and returns no continuation. A generation change discards the stale actions without input. |
| A-2 existing finished behavior | Both existing boundary/status replacement cases passed. No product change to early finished publication for replaced boundaries. |
| Existing capped backoff | Existing 1/2/4/8/16/30/30-second test passed without charging input. |
| A-3 missing delivery paths | All four passed: `transport-unavailable`, `herdr-pending`, `herdr-send-unreachable`, `modal`. Each retains the original Deferred action, clears only its barrier, retries that action, confirms a complete UserPrompt, acknowledges it and sends no duplicate. |
| A-3 existing coverage / D-4 guards | All six existing routing variants passed: `working`, `herdr-blocked`, `rules-held`, `generation-changed`, `task-attempt-changed`, `action-during-flush`. |
| Delivery wire contract | Four new cases assert LF text in one bracketed paste plus a separate Enter, one accepted body and one delivery attempt. Modal recovery additionally sends its verified single Escape. |
| Backend refund | The test observes Sent/one charged attempt before the adapter rejects input, then verifies Pending/zero attempts and cleared attempt state. |
| Modal authority | A real detected menu episode is dismissed through recovery under the queue lock; persisted dismissal verification and resolution are required before delivery. |

The plan defines A-1/A-2/A-3 and decisions, not numbered V-n/R-n rows.
There are no omitted ordinary IDs within the commissioned selection.
The waiting-session checklist was reviewed against the patch: workers yield,
session ownership remains unchanged, and no automatic approval or seat-release
deadline was introduced. No live manual acceptance run was requested.

## Checkpoint scope, history and costs

The brief explicitly skipped TestDesign and prohibited Unit/whole-class runs.
Original CP-2, CP-3, CP-4 and CP-5 were merged into CP-2: one filtered invocation
covering the same 17 cases and three intended classes. CP-1 remains the separate
pre-fix witness. No full assembly, namespace, Unit lane or whole-class run occurred.

| Run | Source | Result | Build / host / reported wall |
|---|---|---|---|
| `20261020-175835-08ca`, CP-1 | `f8406dbefe753dde41c052b9f4e019bfa9e1df89` | 3 executed, 3 expected assertion failures; delivery/finished progress absent before DueAt. No fixture/build failure. | 670.51s / 64.27s / 752.23s |
| `20261020-181410-e858`, CP-2 attempt 1 | `ec3b9feb2ad1606bd2d26ab02748022cec98cf1b` | Build passed, zero tests. Missing trailing class wildcards caused a roster miss (exit 3). No verification credit. | 201.90s / about 4s row / 226.90s |
| `20261020-181936-6d20`, CP-2 corrected | `21c293a28802a75ae9ce27dfbd766f779b250c6f` | 17 executed, 17 passed, zero failed/skipped; exit 0. | 465.24s / 107.28s / 593.04s |

Every selected build and test row had `slot=granted`, `waited=0s`.
Each invocation used the checkpoint tool, committed HEAD as
`--expected-source-sha`, and `--row-timeout 40m`; every exit-75 wait was resumed
until the executor completed. Source stayed frozen throughout each run.
No repetitions or loaded runs were requested. The only green retry corrected
the zero-test selector; no assertions or production behavior changed for it.

One unlisted bootstrap build was necessary because this checkout had no compiled
checkpoint tool:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1308-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1308-tool/ --nologo
```

It passed with four existing nullable warnings, zero errors, `BUILD SLOT granted`,
`waited=0s`, and a seven-second lease. No other unlisted build/test driver ran.

The expected-red and roster-miss runs retained their output (`still-present`);
each executor completed before its owned output was manually removed. Their
historical receipts were not edited. The final green run deleted its output and
has no cleanup refusal. The bootstrap output directories were separately removed.
Generated TRX, JSON receipts and logs remain ignored in their original run roots.

Fresh TRX inspection confirmed these counts:

| Class | Cases | Passed |
|---|---:|---:|
| SessionRuntimeActionDeferralTests | 7 | 7 |
| SessionRuntimeActionRoutingTests | 6 | 6 |
| SessionRuntimeActionTemporaryBlockTests | 4 | 4 |

Receipt qualification completed with:

```text
CHECKPOINT SOURCE VALID source=21c293a28802a75ae9ce27dfbd766f779b250c6f rows=1
```

The validator read
`.antiphon/checkpoints/20261020-181936-6d20/report.json` with that exact expected
SHA and `-Rows CP-2`. The receipt records clean source and verified build
provenance. The new Slow class has a fully-qualified allowlist entry with the
required adjacent reason comment.

## Pending Mutation and handoff

No PC payload was bound or executed, and no PC is discharged by ordinary green.
No PC-n IDs were assigned in this plan. All planned barrier variants remain
pending for method-scoped SourceLanding Mutation: transport unavailable,
Herdr pending/unreachable, Herdr send-unreachable/refund, modal, existing
Herdr-blocked and rules-held. Handoff identity/progress variants and the
bounded-yield guard also remain unqualified by deliberate mutation.
Any eventual unbound candidate belongs under `docs/superpowers/mutations/`;
none was commissioned while Mutation is paused. Earlier CARD-1259 verification
debts remain pending.

At the tested tip, fetching `origin/master` at
`8c70d23802bf989e38ed353adb5116b35ed5158a` and running
`git merge-tree --write-tree origin/master HEAD` succeeded without conflicts
(tree `8fe8f922ec27d4da81da445033fa974ef5969e52`). Final publication checks and
the complete base..HEAD evidence guard are recorded in the caller report.

Next: ordinary Review of the original Code task. After Review, the caller owns
landing that original task and separately commissioning SourceLanding Mutation.

## Unedited checkpoint lines

CHECKPOINT CP-1 commit=f8406dbefe753dde41c052b9f4e019bfa9e1df89 build=ok filter=/*/Antiphon.Tests.Application/SessionRuntimeActionDeferralTests/C1308_new_wakeup_rechecks_review_handoff_before_due* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-3a56669b/.antiphon/checkpoints/20261020-175835-08ca/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=f8406dbefe753dde41c052b9f4e019bfa9e1df89 sourceState=clean buildSource=verified

CHECKPOINT CP-2 commit=ec3b9feb2ad1606bd2d26ab02748022cec98cf1b build=ok filter=/*/Antiphon.Tests.Application/(SessionRuntimeActionDeferralTests)|(SessionRuntimeActionTemporaryBlockTests)|(SessionRuntimeActionRoutingTests)/(C1308_*)|(C1259_new_working_or_blocked_state_defers_old_idle_action*)|(C1259_new_requests_replace_backed_off_queue_work*)|(C1259_transient_deferrals_back_off_without_charging_input*) executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-3a56669b/.antiphon/checkpoints/20261020-181410-e858/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=ec3b9feb2ad1606bd2d26ab02748022cec98cf1b sourceState=clean buildSource=verified

CHECKPOINT CP-2 commit=21c293a28802a75ae9ce27dfbd766f779b250c6f build=ok filter=/*/Antiphon.Tests.Application/(SessionRuntimeActionDeferralTests*)|(SessionRuntimeActionTemporaryBlockTests*)|(SessionRuntimeActionRoutingTests*)/(C1308_*)|(C1259_new_working_or_blocked_state_defers_old_idle_action*)|(C1259_new_requests_replace_backed_off_queue_work*)|(C1259_transient_deferrals_back_off_without_charging_input*) executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-3a56669b/.antiphon/checkpoints/20261020-181936-6d20/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=21c293a28802a75ae9ce27dfbd766f779b250c6f sourceState=clean buildSource=verified
