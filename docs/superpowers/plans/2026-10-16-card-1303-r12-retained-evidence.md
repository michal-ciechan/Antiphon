# CARD-1303 R12 retained evidence refusal

Original Code/landing owner: `19ca257f-afe2-4d93-bdb8-fe467bb0a001`.
Base: `6ca859d006b0a3cac9a88dc6bd551c37e98b2217`; target: `rc/2026-10-15`.

S1 commits the regression before the fix. S2 changes only the retained-evidence
refusal detail and documents recovery. Keep HTTP 409 and
`review_evidence_correction_retained_evidence_unavailable`; never accept evidence
whose retained inventory or files cannot be verified. Keep the existing owner lock,
hash checks, and correction history.

## Verification design

The brief explicitly requests narrow verification and no whole Unit lane
(CARD-1287); this task-specific selection overrides its generic Final profile.
No assembly-wide run is needed: impact is the correction validator's refusal detail.

- V-1: `AdvisoryChangePolicyTests.C1303_R12_LandRefusesUnavailableCorrectedEvidence`
  imports authentic evidence, verifies the intact correction, removes one retained
  blob or the inventory, then requests land under the owner lock. Assert 409, exact
  code, bundle digest/path and actionable restoration/new-Review guidance.
- R-1: the same method's changed-blob variant proves integrity failure remains closed.
- R-2: all variants assert no persistent request, owner landing state or queue entry;
  exact-byte restoration allows a retry using the same correction.
- R-3: full `AdvisoryChangePolicyTests`, `PipelineSpeedIntegrationTests` and
  `PipelineHandoffRouteDocumentationTests` cover adjacent correction/import/link,
  authorization/transaction and documented-route behavior.
- Manual: inspect the documented refusal and recovery against the emitted message.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1303-r12-red/` | retained-red | `/*/*/AdvisoryChangePolicyTests/C1303_R12_LandRefusesUnavailableCorrectedEvidence` | V-1, R-1, R-2 | 3 executed; expected red at actionable message assertion before S2 | 3 | 3 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1303-r12-final/` | retained-final | `/*/*/(AdvisoryChangePolicyTests*)\|(PipelineSpeedIntegrationTests*)\|(PipelineHandoffRouteDocumentationTests*)/*` | V-1, R-1, R-2, R-3 | all listed, 0 failed/skipped | 35 | 5 |

CP-1 is the deliberate pre-fix baseline, not an outstanding final gate; CP-2
reruns all its cases after repair. Check fresh TRX identities and counts. The
checkpoint-tool bootstrap build is an explicitly listed infrastructure prerequisite,
through `scripts/build-slot.ps1`, with isolated `bin-c1303-r12-tool/` output.

### Cost

Eight minutes for checkpoints plus authoring and one tool bootstrap. No unchanged
green repetition is required. Source remains frozen throughout each run.

## Mutation handoff

PC-1 / missing-blob, missing-bundle, changed-blob: pending SourceLanding Mutation.
Precisely select `C1303_R12_LandRefusesUnavailableCorrectedEvidence`; omit retained
verification to prove admission/state assertions detect unsafe acceptance, and
restore the old generic detail to prove the actionable-refusal assertions detect it.
Code performs ordinary red-before-fix only; deliberate mutants belong to Mutation.
