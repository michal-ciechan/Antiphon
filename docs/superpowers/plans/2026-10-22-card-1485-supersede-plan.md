# CARD-1485: a replacement land operation binds the approval it was admitted with

Source: `docs/investigations/2026-10-22-s2-land-resume-approval-changed.md` (Debug `3505c834`).
Live witness: owner `26316601`, operations O1/O2 created with the new reviewed SHA B but the
superseded request's Review evidence E1, both refused `resume_approval_changed`.

## Root cause

`LandOperationFactory.CreateAsync` selects the two approval fields independently. For
`RecoveryMode.None` it keeps the predecessor's `ApprovalLandRequestId` whenever the predecessor's
`OriginalSourceSha` equals the new expected SHA, and it always prefers
`previous?.ReviewEvidenceId` over the request's evidence. A replacement admitted with a new Review
(after a Merge rewrite, or a re-review at the same SHA) is therefore persisted with the obsolete
evidence, and `AgentTaskLandingProtocol.RecheckApprovalAsync` correctly refuses the mismatch. The
protocol guard is not weakened.

## Slices

- **S1 (tests, committed first):** new `tests/Antiphon.Tests/Application/LandReplacementApprovalTests.cs`,
  five non-parameterized methods through `LandingSafetyHarness` (real git fixture + isolated schema:
  persisted admission -> source resolution -> factory -> protocol). Red witness: CP-1 at the S1
  commit, one narrow build-slot run, expected assertion `ReviewEvidenceId` should be E2 but was E1.
- **S2 (fix):** the factory treats the approval as one tuple. Only an identical ordinary approval
  (predecessor ordinary, same reviewed SHA, same Review evidence including none) keeps the
  predecessor's original approval request; every other replacement binds both
  `ReviewEvidenceId` (including explicit null) and `ApprovalLandRequestId` to the current request.

Out of scope (per the card): protocol equality checks, migrations, bulk retirement, a new
abandon route, automatic land, edits to historical Review/settlement evidence, lineage UX.

## Verification design

- **V-1 `ReplacementAfterSourceRewriteUsesNewReview`:** A/E1 refused `rebase_conflict`
  (NeedsResolution); the Merge settlement's persisted effect supersedes the request; the owner
  ref and remote are rewritten to B; Final/Full E2 for B is admitted. The first assertion on the
  new persisted operation is `ReviewEvidenceId == E2` (pre-fix: E1). Then B/E2/new request binding,
  distinct operation, predecessor inactive and otherwise unchanged, publication contains B.
- **V-2 `ReplacementAtSameShaUsesNewReview`:** A/E1 refused (verification) -> A/E2 binds E2 and the
  new request; the old operation keeps E1 and R0.
- **V-3 `ExplicitCallerReplacementDoesNotInheritReview`:** A/E1 refused -> a caller-only request at a
  stale SHA is refused `reviewed_source_mismatch` -> caller-only A binds null evidence and the new
  request (ExplicitCaller), and publishes.
- **V-4 `IdenticalApprovalRetryRetainsProvenance`:** A/E1 refused -> identical A/E1 retry keeps R0
  and E1 on a distinct operation, re-verifies (second verifier call) and publishes; predecessor kept.
- **V-5 `OwnerReviewedRecoveryBindsCurrentApproval`:** A/E1 refused -> owner pushes B, E2 for B,
  `-RecoverReviewedSource` binds the fresh request and E2 and publishes (the existing workaround).
- **R-1/R-2** `C488_PendingEvidenceImmutable`, `C488_PendingExpectedShaImmutable`;
  **R-3** `C488_OriginalRequestProvenanceRetained`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-landapproval/` | rewritten-source | `/*/*/LandReplacementApprovalTests/ReplacementAfterSourceRewriteUsesNewReview` | V-1 | 1 executed, 0 failed/skipped at S2; required expected-red invocation at S1 | 1 | 6 |
| CP-2 | S1-S2 | `CP-1` | approval-boundaries | `/*/*/(LandReplacementApprovalTests*)\|(AgentTaskLandApprovalRequestTests*)\|(AgentTaskLandApprovalRecoveryTests*)/(ReplacementAtSameShaUsesNewReview*)\|(ExplicitCallerReplacementDoesNotInheritReview*)\|(IdenticalApprovalRetryRetainsProvenance*)\|(OwnerReviewedRecoveryBindsCurrentApproval*)\|(C488_PendingEvidenceImmutable*)\|(C488_PendingExpectedShaImmutable*)\|(C488_OriginalRequestProvenanceRetained*)` | V-2,V-3,V-4,V-5,R-1,R-2,R-3 | all 7 listed methods, 0 failed/skipped | 7 | 2 |

CP-1 is the card's row unchanged. CP-2 is the card's row with the same three classes and seven
methods in the `(Name*)` group form: TUnit's tree-node grammar runs zero tests for a
`(FullName)|(FullName)` list. Every prefix is unique across the three classes, so the roster is
still exactly seven. No Unit lane, assembly or qualification run; the brief asks for minimal testing.

### Cost and mutation

Authoring 20-35 minutes, S1 red about 6 minutes, final floor 8 minutes. Post-land method-scoped
SourceLanding Mutation candidates, none executed here:

- **PC-1:** restore `previous?.ReviewEvidenceId ?? request.ReviewEvidenceId` for ordinary
  replacements; V-1, V-2 and V-3 must go red.
- **PC-2:** drop the evidence-equality term from the identical-approval tuple; V-2 must go red.
- **PC-3:** always bind the current request (no identical-approval retention); V-4 and R-3 must go red.
