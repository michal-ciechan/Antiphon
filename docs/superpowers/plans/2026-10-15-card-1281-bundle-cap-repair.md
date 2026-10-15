# CARD-1281 D-2 bundle-cap repair

Code owner: d0d71933-c2ec-42b2-8b9e-7bfbe4d8099e. Assigned branch: feat/card-task-d0d71933.
Base: 8906ea7c5f48a2cb74ebd97ecb097ef02dd1e6f3 (fetched origin/master).

## Scope and repair

The brief explicitly limits this Final follow-up to the existing size-cap classes,
CP-13's ten acceptance cases, the six restored guards once, and full classes reading
the edited stage bundles. No whole Unit lane or whole assembly is selected or credited.

Baseline checkpoint run 20261015-141136-74d6 executed eight existing cap cases: four
passed and four failed. The two parameter rows measured stage-code at 3,348 characters
and stage-review at 5,014, against the unchanged 2,500 cap. ScopedVerificationInstructionTests
and TaskPlatformGuidanceTests also failed the Code cap (2,500 and 2,480 respectively).
Fresh TRX: .antiphon/checkpoints/20261015-141136-74d6/rows/CP-1/run.trx.
The baseline has clean, SHA-bound source and verified build provenance; it is RED, not acceptance.

Preserve every stage guard verbatim. Remove only the D-2 additions from the stage
files and retain their complete text under the D-2 advisory policy section of the
existing owner docs/orchestration-loop.md. A short, mandatory Code/Review pointer
and acceptance summary in delegate-basics reaches both roles through the existing
composition. Other stages retain their stage contracts. This avoids adding another
fragment, changing role composition, weakening policy, or overflowing shared argv.
Expected stage sizes after LF normalization and trim: Code 2,398; Review 2,475.
Code is also 2,399 raw bytes, below the separate 2,410-byte repeat-policy guard.
No test source, assertion, timeout, cap, or production C# changes.

## Verification design

CP-1 covers all full classes found by stage filenames or their embedded StageCode/StageReview
consumers, plus the two other cap classes and the six guards' containing classes.
RunnerDefaultGuidanceTests shares TaskPlatformGuidanceTests.cs. CP-13 preserves the
original D-2 acceptance IDs V-1, V-4, V-5, V-6 and V-7. R-1 here means this brief's
bounded instruction regression selection; it does not discharge the upstream whole
Unit obligation or other CARD-1281 slices.

The six restored methods execute exactly once within CP-1's full classes, without a
duplicate CP-24 run:

- TaskPlatformGuidanceTests.Review_guidance_preserves_every_instruction
- TaskPlatformGuidanceTests.Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes
- VerificationRoundInstructionTests.C544_FinalReviewContract
- PostLandMutationContractTests.C478_V11_ComposedContractsKeepOrdinaryReviewBeforePublication
- CheckpointManifestDocumentationTests.the_review_bundle_names_each_checkpoint_defect
- InstructionBundleTests.C589_V6_BuildSlotGateIsAStandingRuleReviewEnforces

Inspect fresh TRX for every named class, parameterized cap case and guard; counts
must be nonzero and each restored guard must appear once. Validate the tool receipt
against the committed source SHA. The existing caps supply the real before/after
regression oracle; no deliberate mutant belongs in Code.

Manual acceptance: compare relocated D-2 text byte-for-byte to the base paragraphs,
check the mandatory shared pointer and all six ASCII stage lengths, inspect the full
diff and unchanged test sources, run check-evidence-diff.ps1 over base..HEAD, and
fetch origin/master then perform merge-tree without changing branches.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | repair | tests/Antiphon.Tests -> bin-c1281-cap-final/ | bundle-cap-and-consumers | `/*/*/(InstructionBundleTests*)\|(ScopedVerificationInstructionTests*)\|(TaskPlatformGuidanceTests*)\|(RunnerDefaultGuidanceTests*)\|(VerificationRoundInstructionTests*)\|(PostLandMutationContractTests*)\|(CheckpointManifestDocumentationTests*)\|(CheckpointRepeatDocumentationTests*)\|(RepairSourceDocumentationTests*)\|(AgentBundleAttachmentTests*)\|(DelegateBundleLaunchTests*)/*` | R-1 | all 11 named full classes, six restored guards once, all cap argument rows, 0 failed/skipped | 11 | 6 |
| CP-13 | repair | CP-1 | advisory-policy-acceptance | `/*/*/(AdvisoryChangePolicyTests*)\|(StageOutcomeSummaryTests*)/*` | V-1,V-4,V-5,V-6,V-7 | policy 5 + summary 5, all variants, 0 failed/skipped | 10 | 2 |

### Cost

Eight estimated minutes for the final selection, including one isolated build; slot
wait is additional. Baseline was one isolated build and one exact cap filter. One
unlisted gated checkpoint-tool bootstrap was necessary because this checkout had no
tool binary: BUILD SLOT granted, waited=0s, build succeeded in 5.36s. No repeated
green proof or loaded run is requested. Red rows are repaired and rerun.

### Mutation and handoff

All upstream PC-1..PC-64 and variants remain pending, including the D-2 candidate's
PC-1, PC-8, PC-9, PC-10, PC-11, PC-12 and PC-52. Candidate:
docs/superpowers/mutations/2026-10-15-card-1281-d2-policy-candidate.md. Mutation must
rebind the instruction variants to the shared summary and owner-document policy,
and discover missing controls; relocation does not discharge a PC. No new numbered
PC is claimed by this repair.

Next: ordinary Review of this Code owner, then caller landing and separately
commissioned SourceLanding Mutation. Restart: server after landing, because bundles
are embedded; warm delegates retain their launch-time instructions until relaunched.
