# CARD-0901: static alternative already shipped

Date: 2026-10-03. Plan task: `a2558b01`. Inspected source:
`bb5fa774cd56f85ee6f0b1122c198192427e5ddf`.

## Outcome

CARD-0901's explicitly permitted static alternative is already implemented by
landed CARD-0891. There is no additional implementation slice for that acceptance
route. This artifact records the evidence and the remaining scope decision rather
than commissioning a duplicate checker.

The card asks either for executed mutation reachability or, alternatively, that
assertions preceding the named assertion carry labels. The existing checker binds
the target to its method, reports unlabeled predecessors, and separately reports
earlier different labels. It deliberately does not prove dynamic reachability.

There is a conflicting board record: CARD-0891's terminal reason says reachability
proof remains open under CARD-0901. Its landed plan and the testing owner explicitly
say the static alternative satisfies CARD-0901. Default recommendation: reconcile
the card as satisfied by the static alternative. If the caller intended mandatory
executed proof, revise that acceptance explicitly and commission a separate plan.

## Evidence and ground truth

Read CARD-0901 with `scripts/card.ps1 get ... -Board Antiphon`, its complete revision
history, JSON detail and discussion. It has one revision (this Plan dispatch), no
terminal reason and an empty discussion. Read CARD-0891's full card and terminal
reason. No CARD-0901 revision revokes its static alternative.

CARD-0891's reported landing is `4358939ecd85d6e7ff0941f970879499cb930e3d`.
`git merge-base --is-ancestor <landing> HEAD` returned 0. Its PC test class and
`c835-expected.json` are unchanged between that landing and this task's base.
The checker was introduced in `0f989b8651`; historical binding corrections followed
in `ea843408a0`. These are source observations, not a fresh runtime receipt.

| Card assumption or requirement | Current code and evidence | Consequence |
|---|---|---|
| CARD-0891 only proposes checking label presence. | `tools/Antiphon.Checkpoints/Coverage/PlanCoverageAnalyzer.cs`, `AnalyzePc` (line 109), resolves the target inside the bound method and inspects earlier indexed assertions. | The presence-only premise is obsolete. |
| A label elsewhere can conceal a missing method-local target. | `AnalyzePc` emits `PC_LABEL_NOT_IN_METHOD` and `missing-target`; `PlanCoveragePcTests.rejects_label_in_other_method` guards this. | Global occurrence is insufficient. |
| Unlabeled Exit/ThrowAsync checks may fail before the target. | `TestAssertionIndex.cs` indexes supported Shouldly forms, including `Should.ThrowAsync`, and expands supported local helpers. `AnalyzePc` emits `PC_UNLABELED_PREDECESSOR` with source/plan locations and `unlabeled-predecessor`. `PlanCoveragePcTests.reports_unlabelled_predecessor_including_throw_async` guards the cited pattern. | This is the requested static alternative within the documented supported syntax. |
| A preceding assertion may carry another case's label. | `PC_EARLIER_OTHER_LABEL`, `EarlierOtherLabels` and `reachability=unproven` expose it. `PlanCoveragePcTests.reports_earlier_different_label_as_unproven` explicitly expects exit 0 for this advisory. | A labeled predecessor does not establish the intended mutant's first failure; the limitation remains visible. |
| Findings must be visible before Review. | `docs/testing-and-build.md`, “Static plan-to-test coverage (CARD-0891/0901)”, requires Code to paste complete coverage output before final checkpoints and Review to rerun it. `PlanCoverageReport.cs` gives non-static-labeled PC statuses exit 1. | The workflow integration already exists. This is a reporting requirement, not an automatic new land gate. |
| All eight CARD-0835 examples should be visible. | `tests/Antiphon.Tests/Checkpoints/PlanCoverageGoldenTests.cs`, `c835_eight_pc_risks_are_visible` (line 46), checks the exact eight IDs and statuses against `Fixtures/PlanCoverage/c835-expected.json`, plus the PC-23 advisory and universal unproven status. | Historical regression fixtures already cover the card's example set. |
| A clean static result proves execution reaches the intended label. | `PlanCoverageReport.cs` always reports unproven reachability; `PlanCoveragePcTests.accepts_labeled_sequence_without_claiming_dynamic_proof` guards that distinction. | No such execution proof is claimed or supplied by this dispatch. |

The frozen CARD-0835 expected statuses are:

| PC | Static status | Additional evidence |
|---|---|---|
| PC-5 | missing-target | Target belongs to another selected method. |
| PC-7A, PC-7B, PC-8, PC-9A, PC-21, PC-22 | unlabeled-predecessor | Earlier unlabeled assertion locations are reported. |
| PC-23 | static-labeled | Earlier other labels are reported as an advisory; reachability is unproven. |

The existing [CARD-0891 plan](2026-10-01-card-0891-plan-to-test-coverage-check-plan.md)
states that it ships the static alternative, describes these exact limitations in
D-4, and excludes executed reachability from acceptance in D-6. The bounded syntax
analyzer is not a control-flow proof: arbitrary helper dispatch, conditional
execution and general dataflow remain outside its claim.

## Decisions

- **D-1 — Default to satisfaction of the written static alternative.** Recommend
  closing CARD-0901 with the implementation and historical fixture evidence above.
  Reason: the card explicitly permits that route, and it is already landed.
  Reject a second implementation of predecessor lint.
- **D-2 — Retain the dynamic-proof distinction.** Do not relabel static-labeled as
  reachable or turn all different-label advisories into errors. Such an error
  would not prove mutation reachability and would change the accepted lint policy.
  Reject automatic execution of English mutation descriptions inside `coverage`.
- **D-3 — Reconcile the contradictory closure note before further dispatch.**
  Return `next: decide` with this completed artifact. The decision is whether to
  accept D-1 or explicitly replace the alternative with mandatory executed proof.
  This is not an implementation blocker: the default disposition is complete.
- **D-4 — Keep verification portable.** Both required GETs,
  `/api/runner-defaults` and `/api/session-runners`, succeeded on 2026-10-03.
  Defaults revision was 2; eligible Windows and Linux lanes were visible. Use the
  live default for any later work, with no fleet address or host pin. Omit
  `-Runner` and `-Platform`; `-Platform Any` is only needed to clear an inherited
  OS pin. This tooling requires no specific OS.

## Slices and files

| Slice | Files | Tests and disposition |
|---|---|---|
| S1 — Record the supersession evidence (this dispatch) | This plan file only. Commit and push on the assigned branch. | Documentation validation only; no test/source changes. |
| S2 — Caller reconciliation | CARD-0901's board verdict, through the normal card workflow; no generated `docs/cards/` edits. | Accept D-1, or revise the scope for a fresh Plan/TestDesign dispatch. Existing relevant tests are named above; no new tests are needed for D-1. |

## Verification design

This is a disposition artifact, not an implementation handoff to Code. Builds,
tests and mutation executions in this Plan dispatch: **0**. No daemon or production
settings were changed. CARD-0891's board verdict reports its historical CP-1 as
48 passed, 0 failed; that is inherited card evidence, not a run against this task's
base, and later additions mean it is not the current full roster.

The inspection established repository ancestry, exact predecessor diagnostics,
method binding, report exit semantics, eight fixture IDs, and the existing
Code/Review workflow requirement. Validate this documentation with
`git diff --check`, then commit/push and confirm the remote branch SHA.

If the caller requires fresh executable confirmation before disposition, the
single bounded checkpoint below is the complete confirmation scope. It is
contingent on that commissioning, not an outstanding test for this docs-only task.

| Coverage | Existing tests | Expected result |
|---|---|---|
| V-1 | All four methods in `PlanCoveragePcTests` | Unlabeled predecessor, wrong method, different-label advisory and honest static status contracts. |
| V-2 | `PlanCoverageGoldenTests.c835_eight_pc_risks_are_visible` | Exact eight historical PCs, expected statuses, PC-23 advisory, all reachability unproven. |
| R-1 | The other three methods in `PlanCoverageGoldenTests` | Existing CARD-0866 legacy/fixed obligations and CARD-0780 regression preserved. |

Run a commissioned CP-1 through the checkpoint tool after S1 is committed, with
the exact committed SHA in `--expected-source-sha`. Bootstrap the tool through
`scripts/build-slot.ps1` to a separate `bin-c901-tool/` output; the checkpoint
tool takes slots for its own drivers. Await terminal completion, including every
exit-75 continuation, report actual counts and source receipt, and clean owned
alternate outputs. Use the normal testing owner for the driver protocol. Do not
run a full suite, historical production mutants or a SourceLanding snapshot as
part of this optional static confirmation.

### Checkpoints

Contingent confirmation only. Lane: **Any / managed Unit**, no database, browser,
provider or native-session requirement. The expected eight executions are counted
from four `[Test]` methods in each class, with no argument expansion; they are not
eight mutation runs. Estimated ordinary floor: 8 minutes including one isolated
test-project build; mutation floor: 0.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c901-confirm/` | pc-static-confirm | `/*/*/(PlanCoveragePcTests*)\|(PlanCoverageGoldenTests*)/*` | V-1, V-2, R-1 | all 8 listed methods, 0 failed/skipped | 8 | 8 |

## Activation and handoff order

1. Publish S1 on the assigned task branch; caller may land this documentation.
2. Reconcile D-1/D-3 on CARD-0901. No binary activation, restart, deployment,
   migration, runner change or SourceLanding mutation is required for this
   disposition. CARD-0891's tooling already lives in the inspected ancestry.
3. If executed proof is required instead, revise the acceptance and commission a
   fresh plan followed by TestDesign. Measure each declared PC variant at the
   appropriate source: baseline green, intended mutant's first assertion failure
   and exact label/case, then restored green. Distinguish wrong-label failures,
   build/fixture errors, timeouts and zero tests from valid red. The eight frozen
   CARD-0835 examples are the seed; they are not executable historical snapshots.
   Reuse the existing SourceLanding custody and external-evidence contract rather
   than adding an executor to read-only `coverage`. No dynamic design is frozen
   by this disposition document.

Recommended next stage: **decide**. Recommended decision: accept the already
shipped static alternative and record that dynamic reachability remains a
separate capability, not an unsatisfied claim of the existing checker.
