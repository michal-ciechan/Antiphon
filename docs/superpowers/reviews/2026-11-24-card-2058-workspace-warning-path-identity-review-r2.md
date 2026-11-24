# CARD-2058 Review round 2 (task c26ad247)

Verdict: **Clean**. Evidence-only round; no blockers, no regressions.

- Subject: `feat/card-task-9c157c8a` (Code task 9c157c8a-9271-44cd-83b4-75aaeb834a5d, Codex High), tip
  `8007ac89a8de451ab9f2ebad21cb9169255a90c7`, unchanged since round 1 (453f994d, note `...-review-r1.md` on
  `feat/card-task-453f994d`). The diff from the previous reviewed SHA is empty, so the code was not re-reviewed.
- Why this round: round 1 had no SHA-validated checkpoint receipt, so it reported `reviewedSourceClean: false`. The land
  refused with `review_evidence_source_not_clean`, because `LandApproval.LoadUsableEvidenceAsync` requires
  `ReviewedSourceClean == true`. On a first settlement that value is taken directly from the literal `true` in the
  block's single `reviewedSourceClean` line (`AbfLegacy`, `ReviewEvidenceBindingService.PrepareFirstSettlementAsync`).

## Evidence (server2 Linux, runner mirror ws-07-d2, checked out at exactly 8007ac89a, clean tree)

- Checkpoint run `20261124-120126-57f4` (one-row `.antiphon/c2058-r2-plan.md`, gitignored):
  `CHECKPOINT CP-1 commit=8007ac89a… filter=/*/*/OrchestratorWorkspaceWarningTests/* executed=10 passed=10 failed=0
  skipped=0 slot=granted dirty=0 sourceState=clean buildSource=verified`. The verdict was GREEN exit=0 and the tool
  reported `unlisted: none`. The cold build took 685 s and the run took 18m54s wall time.
- `validate --evidence …/report.json --expected-source-sha 8007ac89a8de451ab9f2ebad21cb9169255a90c7`:
  `CHECKPOINT SOURCE VALID source=8007ac89a8de451ab9f2ebad21cb9169255a90c7 rows=1`.
- Red-then-green: commit 5e0ee960d changes only the test file and precedes the fix commit 8007ac89a, which changes only
  the service. Round 1's build-slot red rerun (the service restored from 5e0ee960d) gave 6/10 with the 4 match cases
  failing on the Shouldly count assertion. It was not repeated here because of the 35-minute budget.
- `git merge-tree --write-tree origin/master(fb0538b76) 8007ac89a`: no conflicts.
- `scripts/antiphon evidence-diff --base 2f4cfa8df`: commits=2 entries=0 violations=0.
- Collateral: the range touches only the service and its test class.
- Not run: the batched guards and the Unit lane. The caller limited the round to the named class, and the card has no
  plan checkpoint table.
