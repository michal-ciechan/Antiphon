# Fast-track lane docs review (round 1, CARD-1514 review process; subject card not yet bound)

Subject: `origin/feat/card-task-2be671fb` tip `d82155329b8c9498c18899c7be3948910372265c` (Grok author, Docs task 2be671fb). True diff is the branch's own two commits against its merge-base with master, `e54065fe59f798fb5dc395a222679545ebe1c9e0..d82155329`: `a9a6aa9bc` (add the interim fast-track lane rules) and `d82155329` (cite the fast-track plan by commit). 59 insertions across `docs/orchestration-loop.md` (+57), `AGENTS.md` (+1), `.claude/skills/antiphon-orchestrator/SKILL.md` (+1). Note: `git diff master..<tip>` is noisy because `origin/master` has diverged independently with unrelated docs commits since this branch's base; the merge-base diff above is the one actually reviewed.

Plan read: `docs/superpowers/plans/2026-11-20-fast-track-lane-plan.md` at `52b66df5ed0193104514d8a27fb5604ded06c96e` (fetched from `origin/feat/card-task-73d0252f`; not yet on master, hence the by-commit citation in the diff, which is itself correct per the plan note).

## Checks

1. **Faithful to the plan.** Lane names, worst-path-wins ordering (NORMAL > FAST-TEST > FAST-DOC-CONTRACT > FAST-DOC), the full NORMAL glob table, the FAST-TEST/FAST-DOC-CONTRACT/FAST-DOC patterns, the capped-doc list and 25-reader-class deferral (D-9/O-2), I-1 through I-8, the cap of 8 fast lands (O-5/I-7), the demotion escape hatch, and the card-revision audit requirement all match the plan verbatim or near-verbatim (section 2, section 6, D-2, D-9, O-5). No rule in the doc is looser than the plan; I-1's "`[fast]` in the title only when the lane is fast" is a tightening the plan's own table implies but doesn't state as crisply. No defect.
2. **Safety.** The NORMAL table explicitly lists `server/**`, `src/**`, `client/**`, `tools/**`, `scripts/**` (incl. `scripts/lib/**`), `docker/**`/`deploy/**`, every build-graph file type, migrations, shared/linked test infra, policy/roster/pin files, the classification guard files, E2E, non-`.cs` test fixtures, `.claude/**`, `server/Bundles/**`, `AGENTS.md`, `CLAUDE.md`, `docs/cards/**`, non-`.md` docs, repo/scope/runtime config, secret-named paths, and default-deny for anything unmatched. Nothing in the added text lets product/script/policy/`AGENTS.md`/`CLAUDE.md`/`.claude/**`/unmatched paths go fast. The marker is explicitly a request ("is a request... the classifier decides"), not a switch. FAST-DOC and FAST-DOC-CONTRACT keep an automated check (`check-evidence-diff.ps1`, merge-tree, `checkpoint import --plan` when applicable); FAST-TEST explicitly keeps the land build (D-6, I-4 "The land build stays"). No defect.
3. **Honesty.** The new subsection opens by stating FT-2 and FT-5 are not built and that until they land a FAST-DOC/FAST-DOC-CONTRACT land still builds and the index is regenerated per-wave rather than at land; it states `delegate.ps1 -Fast` does not exist until FT-6 and that today's request is the title bracket / card label only, decided by hand. No defect.
4. **Consistency with existing rules.** Rule 13 is appended correctly after rule 12 with no renumbering collision. It doesn't contradict "Merge verification is short," the three-round Review cap, standing operating rules 1–12, or the different-company rule — it adds an orchestrator-side classification step before dispatch and doesn't touch Review cardinality or company assignment. The new anchor `fast-track-lane-operator-2026-11-20` is unique in the file (one definition) and every reference to it (AGENTS.md, the SKILL.md pointer, and rule 13's own link) resolves to it. `git merge-tree --write-tree origin/master <tip>` returned a clean tree (`15303eac72fdd7e2c4f8b6de08a61d241ebff9be`, no conflict markers). No defect.
5. **AGENTS.md size/structure contract.** Read only, not run: `scripts/check-agent-context.ps1` enforces a 24,576-byte soft target / 30,720-byte hard ceiling on raw UTF-8 bytes; the test-suite equivalent is `AgentContextContractTests` (`tests/Antiphon.Tests/Application/AreaMapTests.cs:429-470`), which also asserts every `docs/*.md` link the file names (matched by regex ending `.md)`, i.e. no anchor suffix) exists on disk. `git show origin/feat/card-task-2be671fb:AGENTS.md | wc -c` is 21,317 bytes — comfortably under both thresholds. The one added line is a standing-rules pointer using the same `docs/orchestration-loop.md#anchor` shape already used by the adjacent land-graph-refresh and review-rounds pointers, so it falls outside the link-existence regex the same way those pre-existing lines do (no regression). No defect.

`check-evidence-diff.ps1 -BaseRef e54065fe59f798fb5dc395a222679545ebe1c9e0 -HeadRef d82155329` (the branch's own range): `commits=2 entries=0 violations=0`.

No code or test files are touched (docs-only), so per the verification profile's own condition the touched-class/guard-batch rerun (`TestClassificationPolicy*|TestClassificationGuard*|SlowTestRegistryMerge*|SlowTestTripwire*|NativeLinuxPolicyWitnessTests*`) does not apply and was not run.

## Verdict

CLEAN. No blockers.

--- review evidence ---
subjectTaskId: 2be671fb
reviewedSourceSha: d82155329b8c9498c18899c7be3948910372265c
reviewedSourceClean: false (no build ran for this docs-only review; no build-provenance receipt exists to validate against)
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: e54065fe59f798fb5dc395a222679545ebe1c9e0..d82155329b8c9498c18899c7be3948910372265c
