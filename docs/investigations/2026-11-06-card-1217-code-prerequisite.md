# CARD-1217 Code prerequisite finding

Task: `36bb3715-4e5e-4038-9a81-5ec024e60a99` (original Code landing owner).
Worktree: `/work/worktrees/task-36bb3715`.
Branch: `feat/card-task-36bb3715`.
Inspected source/base: `5abd946dd2c235b6e873b0cbb95c738f35216dcf`.

Implementation is blocked on the required Plan/TestDesign artifact. The brief
provides neither a plan path nor a plan commit. The card explicitly says
"Needed (Plan first; fail closed; this tool gates every land, so keep it small
and strictly additive)". The Code stage requires a landed plan and its closed
`### Checkpoints` list. This report is an investigation record, not that plan.

## Confirmed remaining behavior

At the inspected source, `ReportValidator.Validate` rejects non-command rows
with nonzero `Skipped`. `scripts/validate-checkpoint-receipt.ps1` does the same
for report rows and standalone receipts. `CheckpointSpec` has no conditional-skip
declaration; `ReportRow` has no capability-probe evidence. `TrxCaseResult` does
not retain a separate skip reason. No conditional-skip acceptance was found in
these paths; this is source inspection, not a newly executed red witness.

The card requires the two validators to agree: accept only the exact declared
skipped identities and exact reason when a named capability probe is unavailable,
require zero skips when available, and reject extra/missing skips, wrong reasons,
and probe/report mismatches. Every other test must pass. The declaration format,
probe execution and evidence binding need a plan before this landing gate changes.

## Discovery and existing ownership

- Read `card.ps1 get CARD-1217 -Board Antiphon` and its history. The history has
  one revision, a 2026-10-25 ContentEdit attaching the Linux incident; no plan or
  test-design link was present.
- `card.ps1 search 'checkpoint receipts' -Board Antiphon -All` and
  `card.ps1 search 'conditional skips' -Board Antiphon -All` retained CARD-1217
  as the existing owner; CARD-1210 is the related completed card.
- Searched available Git commit subjects for CARD-1217/C1217 and checkpoint
  conditional-skip terms. No matching implementation was identified. This does
  not prove absence from unavailable refs or differently named commits.
- Searched repository documentation, scripts, tool source, checkpoint tests and
  the supplied inbox for CARD-1217, C1217 and conditional-skip declarations.
  Existing references were the brief, CARD-1210 plans and the CARD-1514
  investigation; no CARD-1217 plan/TestDesign or closed checkpoint list was found.
- The existing CARD-1210 plans describe conditional skips but do not supply a
  CARD-1217 validator implementation plan. The prior CARD-1514 investigation
  explicitly left fix design with CARD-1217.
- Read the required orchestration D-2 policy, testing/build guidance, project
  context and performance standards. Read live runner-defaults and session-runners
  via authenticated environment-based HTTP; no placement/settings changes made.

## Verification status and handoff

No implementation, policy, schema or test selection changed. No build or test
driver ran; `slot=not-run`, `waited=not-applicable`. There are no CHECKPOINT lines,
fresh TRX counts, verified build provenance, red/fix/tested SHAs or green claims.
No bin/obj/test-result outputs were created by this task.

All required ordinary execution remains unrun: whole Unit lane, affected full
classes, classification/policy sweep, layering guards, mechanical contract,
TaskPlatformGuidanceTests and manual acceptance. No V-n/R-n/PC-n IDs were supplied
or found for CARD-1217, so none are credited. Every eventual PC/variant remains
pending method-scoped SourceLanding Mutation; Code does not own deliberate
mutant red/restore/green cycles. No timeout or assertion changed.

The caller needs to supply or commission the required landed Plan/TestDesign
with its exact source commit, declaration/probe trust contract, named affected
classes, ordinary V/R and manual acceptance, PC variants, performance budget and
closed checkpoint list. Resume Code on this original owner branch afterward.
Ordinary Review is required after implementation and ordinary verification;
this task grants no land or activation eligibility. Restart: none.
