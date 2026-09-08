# CARD-0448 Review: land publication safety, state at `b719b3b4`

Review stage, 2026-09-09, against `feat/card-task-c86499fb` @ `b719b3b4` (the canonical branch
across all five Code rounds). Merge-base with master: `8ed4d68b`. Read-only pass; nothing fixed.

## Verdict

The landing protocol itself is sound and is a real improvement over the
`IsAlreadyLandedAsync`/`CleanupAlreadyLandedAsync` shortcut. **It is not safe to land as-is**
because of two defects that sit outside the protocol (D1, D4), plus two decisions the operator
should take deliberately (D2, D3). All four are small and bounded — a short Code round, not
another open-ended cycle.

## What I ran on `b719b3b4`

Built `tests/Antiphon.Tests` to `bin-rev8b/`: 0 errors, 133 warnings (output tree deleted after).

| Class | executed / passed / failed |
|---|---|
| LandingGitTests | 29 / 29 / 0 |
| LandSourceIdentityTests | 22 / 22 / 0 |
| DelegationWorktreeTests | 30 / 30 / 0 |
| LandingRemovalPolicyControlTests | 50 / 50 / 0 |
| AgentTaskLandingStateTests | 19 / 19 / 0 |
| AgentTaskLocalMergeSafetyTests | 9 / 9 / 0 |
| CardReviewServiceIntegrationTests | 8 / 8 / 0 |
| AgentTaskLandRequestTests | 7 / 7 / 0 |
| StageOutcomeBackfillTests | 7 / 7 / 0 |
| WorktreeResidueSweepTests | 6 / 6 / 0 |
| WorktreeRemovalAuthorityTests | 6 / 6 / 0 |
| WorktreeRemovalDefaultTests | 4 / 4 / 0 |
| AgentTaskLandCleanupSafetyTests/C448_V19 | 2 / 2 / 0 |
| **AgentTaskLandStageOutcomeTests** | **10 / 9 / 1** |

209 executed, 208 passed, 1 failed. Classes that exceeded a 10-minute foreground window and were
not measured: AgentTaskLandCleanupSafetyTests (full), AgentTaskLandPublicationTests,
AgentTaskLandRemovalMatrixTests, AgentTaskLandBoundaryTests, AgentTaskLandConcurrencyTests,
RepositoryMutationLeaseTests. Single-class cost is high (DelegationWorktreeTests 5m44s,
AgentTaskLandStageOutcomeTests 6m44s), which is itself a planning fact for the next round.

## Defects

### D1 (blocker) — permanent repository-wide lease deadlock after a crash mid-git

**Where:** `server/Infrastructure/Git/RepositoryChildJournal.cs:60-88`,
`server/Infrastructure/Git/RepositoryMutationLease.cs:17-21`,
`server/Application/Services/AgentTaskDispatcher.cs:2967-2982`,
`server/Application/Services/AgentTaskLandService.cs:133-143`,
`server/Application/Services/CardReviewService.cs:129-132`.

**Failure:** `LandingGit.ExecuteAsync` writes a journal file to
`<git-common-dir>/antiphon/children/<guid>.json` before every *mutating* git command — the
predicate matches `rebase merge push fetch update-ref add remove commit checkout checkout-index
restore reset`, so it also covers `git worktree add` during ordinary dispatch. The file is
deleted only by `Exited()` inside the same process. Kill the server while any such command runs
(`restart-apphost.ps1`, a crash, Ctrl-C) and the file survives. `HasUnfinishedAsync` then returns
`true` forever — deliberately, even when the recorded PID is confirmed dead ("A crash orphan
needs explicit recovery inspection, never automatic admission") — and no such recovery exists
anywhere in the server, in a script, or in a doc. From that point, for that repository:

- every non-ReadOnly, non-specialist dispatch records `Held: repository mutation lease is
  occupied or unavailable.` and never proceeds;
- every land is held;
- card PR creation throws `ConflictException("Repository is busy…")`.

`RepositoryMutationLeaseTests.C448_C24_StandingJournalFencesAdmissionByStartIdentity("exited")`
asserts exactly this fencing, and clears it only by the owning invocation calling
`journal.Exited(child)`.

**Why:** fail-closed admission was designed with a recovery inspection step that was never built.

**Fix:** a boot-time (or ops-command) recovery that clears journals whose recorded process is
confirmed dead, or whose record predates the current server start, plus a doc line naming the
directory. Minimum viable: an attention alert naming the stuck file so a human can clear it.

### D2 (high, decision) — automatic worktree cleanup now never completes in this repository

**Where:** `server/Infrastructure/Git/GuardedWorktreeRemoval.cs:35,39` (`HasProtectedIgnored` is
`snapshot.IgnoredPaths.Length != 0`), fed by `LandingGit.InspectAsync`'s
`git ls-files --others --ignored --exclude-standard`.

**Failure:** a real task worktree carries ~35k ignored paths (measured 35,446 in
`C:\Antiphon\worktrees\card-task-c86499fb`: `bin/`, `obj/`, `.antiphon/`, `node_modules/`). Both
`Publication` and `LocalMerge` removals therefore always refuse with `ignored_content_preserved`.
Every land will settle `LandedWithResidue`, `cleanup=Refused`. The residue sweeper is separately
hard-wired to keep everything (`WorktreeResidueSweepService.cs:178` — `Eligible` became
`Unknown`/keep) and `PruneStaleAsync` routes to the refusing raw remover. Net effect: **no
automatic worktree removal at all, ever**, and unbounded disk growth.

This is fail-closed and consistent with plan case A4 ("opaque ignored … no cleanup"), so it is a
correct-by-plan outcome rather than a coding error — but the operational consequence is total and
should be an explicit operator decision, not a side effect. The PC-7 evidence names
(`pc7-bin-red`, `pc7-claude-red`, `pc7-antiphon-red`) show an earlier round had a prefix
allowlist; the final code has none.

**Fix (follow-up card):** a narrow, evidence-backed ignored-prefix allowlist, or an explicit
operator-facing "reclaim residue" action.

### D3 (medium, scope) — `scripts/cleanup-build-junk.ps1` silently became a no-op

**Where:** `scripts/cleanup-build-junk.ps1` (whole file), changed in `563e4b18`, an orchestrator
WIP checkpoint. No round report mentions it; `…code-continuation-report.md:119` explicitly says
it was left unchanged and deferred to an F3 adjudication.

**Failure:** the weekly Windmill job `u/lndcobra/antiphon_build_junk_cleanup` now only prints
`retained (…)` and deletes nothing. Every `bin-*` alternate output tree (roughly a dozen per
isolated build) accumulates forever, and the schedule keeps running against a script that no
longer does its job.

**Fix:** drop this file from the landing set, or keep it and consciously retire/repoint the
Windmill schedule and update the docs that describe it.

### D4 (medium) — one red test in the branch's own acceptance suite

**Where:** `tests/Antiphon.Tests/Application/AgentTaskLandStageOutcomeTests.cs:271`
(`reland_of_an_already_landed_task_runs_cleanup_only`).

**Failure:** `landed.Detail should contain "mode=CleanupRetry" but was … "mode=Fresh"`.

**Why:** production is right. The second run genuinely runs as `CleanupRetry` — only one new
stage row appears (`rows.Count == 4`), whereas a Fresh run would add Rebase + Verify + Cleanup.
Its terminal event is remapped to `LandingCleanup` by the `alreadyReported` branch in
`AgentTaskLandService.SettleLandedAsync` (`AgentTaskLandService.cs:262-266`), so the test's
`Type == AgentTaskEventType.Landed` query returns the *first* event, which is `mode=Fresh`. The
test expectation is stale; the implementation is not wrong.

**Fix:** query `AgentTaskEventType.LandingCleanup` for the second outcome, and assert the first
`Landed` event still reads `mode=Fresh`.

## Coverage assessment

The ledger `2026-09-08-card-0448-code-continuation-matrix.md` is stale — last written at
`0dea285a`, and it marks every V/R/PC family "Pending". It is not a usable acceptance record for
the current HEAD; the continuation4 report and control ledger are.

Named-test census on HEAD (157 distinct `C448_*` test names):

- **V:** 33 of 36 families have at least one `C448_Vnn_` test. Missing: **V-07** (local-only
  containment / local target ahead), **V-21** (the CARD-0444 regression: pre-published source
  branch survives every refusal family), **V-29** (source reused by an active follow-up).
  V-21's intent is partly met by `LandingGitFixture.AssertRemoteSourceAsync()` asserted broadly
  across the boundary and admission fixtures, but not as the named per-refusal-family fixture the
  plan required — and that regression is the reason the card was filed.
- **R:** no test is named `C448_Rnn`. R-1..R-19 are only claimed by linkage to V evidence.
- **C:** only `C448_C09` and `C448_C24` of C01-C24 appear as names.
- **F:** F01, F03-F07 present; **F02 absent** by name (the S5 `StageOutcomeBackfillService`
  amendment — the service is in fact retired and `StageOutcomeBackfillTests` passes 7/7).
- **PC:** **no `C448_PCnn` test names exist at all.** The 50 positive-control families live only
  as mutation runs recorded in `…continuation4-controls.md` (84 current filter triples; all 70
  linked-policy controls with a RED oracle plus a restored GREEN, individually inspected in
  `c4-oracle-review.json`). That evidence is credible and unusually careful, but it is not
  re-runnable from the repository and cannot be spot-checked by a later reviewer.
- The A1 incident reproduction is `tests/Antiphon.Tests/Fixtures/Landing/C448IncidentBaselineTests.cs.txt`
  — a `.txt`, so not compiled. Its baseline RED was produced in a separate `f202fcc1` checkout.
  The green side is covered in-suite by `C448_V01_*` in `AgentTaskLandIdentityMatrixTests`,
  `LandSourceIdentityTests` and `AgentTaskLandPublicationTests`.
- Continuation 4 itself states the final unmutated combined regression is still owed. I confirmed
  it would not be green (D4).

Rough split: **implementation ~90-95% complete** (all six slices present, coherent, and no defect
found in the identity / publication / removal guards themselves); **acceptance evidence roughly
60-70%** of the plan's matrix with genuine red-then-green, concentrated on identity, removal and
policy decisions, and thin on PC re-runnability, R-family naming, and V-07 / V-21 / V-29.

## What is genuinely sound

I looked for defects in the core guards and did not find any:

- `AgentTaskLandingProtocol` checkpoints every mutation behind a durable phase, rechecks source
  identity and target state at each boundary, pins S/T0/P as `refs/antiphon/land/**`, and never
  substitutes a local `rev-parse` for publication — `ObserveAsync` reads the push endpoint with
  `ls-remote`, then an immutable fetched observation ref plus an ancestry check.
- The old boolean shortcut is gone: `IsAlreadyLandedAsync` / `CleanupAlreadyLandedAsync` survive
  only inside the non-compiled baseline fixture.
- `AgentTaskLandService` refuses (`landing_protocol_unavailable`) rather than falling back when
  the protocol is not injected — no silent legacy path.
- Every removal funnels through `GuardedWorktreeRemoval`; the untyped
  `TryRemoveAsync(repo, path, mergedInto)` returns `typed_removal_authority_required` and has no
  production caller, and `RemoveAsync` (which would throw) has none either.
- Branch deletion is an exact-old-SHA `update-ref --no-deref -d` with confirmation, after
  re-reading authority, re-checking absence of the directory, and re-checking that the source is
  not checked out anywhere.
- Migrations apply cleanly: every DB-backed class above ran against isolated schemas.

## Recommendation

1. **Short Code round on this branch** — D1 (journal recovery), D4 (test query). Both are small
   and specific. Re-run `AgentTaskLandStageOutcomeTests`, `RepositoryMutationLeaseTests` and one
   dispatcher admission class, then land.
2. **Operator decision** — D3: revert `scripts/cleanup-build-junk.ps1` out of this change set
   (recommended) or accept it and repoint the Windmill schedule.
3. **Follow-up card** — D2 (ignored-content allowlist or a residue-reclaim action) plus the
   remaining acceptance gaps: V-07, V-21, V-29, named R/C/F/PC fixtures, and a re-runnable form
   of the PC mutation evidence.
