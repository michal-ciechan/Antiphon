# CARD-0824: bounded slot reuse canary 2

## Decisions

D-1: Record the already-authorized second Plan canary, without runtime changes, builds, tests or merge. Reject role widening and warm-output claims: this task only observes directory reuse.

D-2: Preserve the requested eight-line note and this canonical plan artifact required by the stage contract. Reject a chat-only deliverable; publish both on the assigned task branch only.

D-3: Leave release to normal settlement and reconciliation after session stop, confirmed sync and absence of live follow-up. Do not force-release the working lease. The caller must observe this task's later release; the one-minute polling cadence is not a release deadline.

## Ground truth

| Brief assumption | Observed runner state before any write |
|---|---|
| Reuse the first canary's ws-01 directory | ws-01 is Leased; directoryGeneration 1; leaseGeneration 2; lastTaskId 870a2044-4aa6-4611-9723-2bb745fd7755. |
| Current task owns the lease | taskId dc726c22-77ff-4bdb-af6e-bfb4cca12ced; preparationId 60a723a0-0384-4503-84ae-75259ac80e01; branch feat/card-task-dc726c22; base a71c45ab4d5703d480f4eee4c4e30adab45bc4f9. |
| Previous holder released with evidence retained | idleSince 2026-11-15T21:27:19.7514532+00:00; retained directory name 870a20444aa6461197232bb745fd7755. This is reuse evidence, not a post-settlement observation of this task. |

## Slice and affected tests

One documentation slice: `docs/superpowers/plans/2026-11-15-slot-canary-note-2.md` and this plan. Summarize admission, custody-safe release and roles-none rollback from `docs/docker-stack.md#runner-workspace-slots-on-the-volume-card-0824-s1` and the existing all-roles plan's first-task/release/reuse section.

Existing tests affected: none (CARD-1782); no migrations, public DTOs/routes, operation labels, remote script functions or statement budgets change.

### Checkpoints

None: no build/test lane is commissioned. Manual documentation checks verify the eight-line note, whitespace, exact changed-file roster and publication to the assigned branch. Both required placement GETs were read; no new dispatch or fleet placement is specified.

## Performance budget

Apply `docs/testing-and-build.md#performance-standards`: this documentation-only slice has no test selection, setup, isolation, test phases or external test spans. Build/test duration is zero; no measured p95 exists and no timeout is introduced or changed, so a roughly 2x-p95 timeout is inapplicable. Git publication and admission are separate from test time. Preserve the ledger excerpt and command identities in the final report. No session waiting behavior changes.

## Verification design

Read the ledger before writing; compare task/preparation, directory/lease generations, exact base SHA and branch. Report retained directory names, runner/store identity, cwd/pattern, hostname and CLI version without secrets. Check note length and diff, commit and push only the assigned branch, then obtain its advertised SHA. The caller checks persisted WorkspaceSlotDecision and observes settlement/release after this report; this task does not claim either future event complete.
