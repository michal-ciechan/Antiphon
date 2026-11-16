# CARD-1882 plan and TestDesign review, round 1

Review b65e738f (Claude). Blockers only.

Subjects:

- Plan e2dd6b0a (Grok), `docs/superpowers/plans/2026-11-16-card-1882-algorithm-digest-plan.md`, commit `6de02276d3d963bcf617e10a42872bff927b4cd6` on `feat/card-task-e2dd6b0a`.
- TestDesign 8b386cd5 (Codex), `docs/superpowers/plans/2026-11-16-card-1882-testdesign.md`, commit `ccd3e470199b41c50277cce2554dc70f6e58a9c0` on `feat/card-task-8b386cd5`.

Both were written against master `48a4615dcb4d67033886a3e31629dc7351ed0852`. I read the source at master `18eabe4ce48a46adea27aa9cbb410415eae9ac91`. It adds only CARD-1883 docs, so no cited source has changed.

**Verdict: NOT CLEAN.** There are two blockers, D-1 and D-2, and both artifacts need an amendment. Claims (2), (3) and (5) hold. Claim (4) holds in its row mechanics. Claims (1) and (6) fail as stated.

## Claims checked

| # | Claim | Result | Evidence on master |
|---|---|---|---|
| 1 | The executed deciding closure is exactly the 14 listed files. | **Incomplete (D-1).** The C#, PowerShell and leaf csproj inventory is right. The MSBuild files imported into both driver builds are missing. | The selector loads only `lib/release-impact.ps1` (`scripts/impacted-classes.ps1:131`) and `lib/SelectorCensus.cs` (`:599`). The index driver csproj compiles exactly the two builders plus `tests/Shared/PlatformTaxonomy.cs`. `release-inputs` dispatch is at `tools/Antiphon.LandGraphAudit/Program.cs:43`. `ReleaseInputCommand` and `ReleaseInputEvaluation` use only each other plus `EvaluationBudget`. They do not reference `BuildInputPolicy` or `EvaluationExpressionPolicy`. `ReflectionBindings` is a private nested class in `ReleaseImpactProjectGraph.cs:353`. `ReleaseImpactRuntimeInputs.cs:21` is the only caller of `ReleaseImpactProjectGraph.Build` outside tests. No canonicalizer exists. The graph builder records `script-closure-unavailable` (`ReleaseImpactGraphBuilder.cs:22`). |
| 1b | The admission `$required` list omits SelectorCensus, PlatformTaxonomy and RuntimeInputs, and adds carry scripts plus the two land-audit policies. | True. | `scripts/lib/rc-admission.ps1:13-27` |
| 2 | The identity hashes raw bytes of only the two scripts. | True. | `scripts/lib/release-impact.ps1:39-45` uses a two-path array, `hash-object --no-filters`, and builds a `path NUL blob LF` SHA-256. |
| 3 | `-ReleaseEvidence` exits before the census and publishes an empty incomplete/Full envelope. | True. | `exit 0` at `impacted-classes.ps1:135` comes before `Add-Type` at `:599`. The converter at `release-impact.ps1:101-108` refuses complete=true, any classes, a fallback other than Full, or verified=true. |
| 4 | 19 checkpoints with a 171-result floor, red witnesses, 23 guards with distinct controls, provisional budgets. | Mechanically sound. Gaps are under D-2. | The table imports cleanly: `Antiphon.Checkpoints import` gave "imported 19 rows", one build `bin-c1882-s1`, and minExecuted totals 28+1+6+1+78+57=171. All 14 comment anchors exist, are in comments, and start with an uppercase ASCII letter. All 14 targets are LF with no CR. The 12 `.cs` files carry a BOM. The B (one-byte) and W (CRLF with a raw-blob commit through `update-index --cacheinfo`) variants can go red before S1: CP-3..14 fail at exit 0 vs 2, and CP-1/2/15 fail on the 14-path oracle. `CommitAsync` is a bare `git commit` (`ReleaseImpactEnvelopeTests.cs:302`), so step 5 keeps the raw blob. The guard inventory has 23 guards, each mapped to its own PC. |
| 5 | Fail-closed. | True. | D-4 and D-6 keep the envelope incomplete/Full and leave `rc-admission.ps1` unedited. `Get-RcAdmissionAlgorithmCoverage` returns `complete=$false`. S5c's `rc-incremental-plan.ps1` (d2fff5d4 tip `7154ce531`) adds `baseline-unproven` whenever `admitted` is false. |
| 6 | The collision is named and rows wait. | **Partial (D-2).** | The TestDesign holds CP-1..19 behind S1R6 and S5c/d2fff5d4. The plan names only S1R6 and "CARD-1836 admission activation". Neither artifact names CARD-1820 S1R5. Neither sees that S1 itself forces a `tests/derived-impact-index.json` regeneration, and d2fff5d4 edits that same file. |

Merge-tree: each branch merges clean into `origin/master` 18eabe4ce, and plan+TestDesign merge clean together. The S5c tip `7154ce531` touches 8 files. None is either S1 production/test file. Its `tests/derived-impact-index.json` hunk is the overlap behind D-2.

## Defects

### D-1 (blocker): the roster omits the MSBuild files that decide what both drivers compile

- **Where:** Plan D-2 and the "Deciding-file roster" ("Fourteen files"). TestDesign V-1..V-14, R-1, CP-1..14, G-1..14 and PC-1..14, plus its "35 results" and "171" totals.
- **Failure:** Plan D-2 defines a deciding file to include "the project file that chooses what that command compiles". Both drivers are SDK projects under the repo root, so every build imports `Directory.Build.props` and `Directory.Build.targets`. `Directory.Build.targets` in turn imports `tests/Shared/CheckpointRepeat.targets`. These files already inject `<Compile Include>` items: lines 46-47 of `Directory.Build.targets`, conditioned on project location, and `CheckpointRepeat.targets`. A one-line edit to either could add or swap a compiled source in `Antiphon.DerivedImpactIndex` or `Antiphon.LandGraphAudit`. That would change which classes or items they select, while S1's digest stays identical and is still published. `tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj` is a `ProjectReference` of the evaluator, so it is in the same compile closure and is also unlisted. The repo already treats `Directory.Build.props`, `Directory.Build.targets` and `global.json` as global build inputs (`DerivedImpactIndexBuilder.Allowlist`, lines 32-38).
- **Why it blocks:** CARD-1882 is the activation prerequisite that a one-byte change in **every** deciding file makes the refusal fire. Without these files, S1 would close the card on a roster that its own D-2 definition says is incomplete.
- **Fix (Plan, then TestDesign):** Either:
  - add `Directory.Build.props`, `Directory.Build.targets`, `tests/Shared/CheckpointRepeat.targets` and `tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj` to the ordered identity, with B/W rows, guards and PCs, and recompute the result counts and Cost; or
  - amend D-2 to name each of these files as deliberately unbound, give the reason, and carry an explicit open prerequisite into the CARD-1836 activation gate, so that S1 cannot be reported as having satisfied "every deciding file".

  Either way, record why `global.json`, `Directory.Build.rsp` and `EvaluationBudget.cs` are excluded. The plan already covers `EvaluationBudget.cs`. The SDK identity for `global.json` is receipted by `ReleaseInputEvaluation`'s `sdk-drift` check.

### D-2 (blocker): S1's test edit makes the committed derived index stale, regenerating it is forbidden, and that file collides with S5c

- **Where:** Plan S1 ("Do not edit ... the derived index JSON") and the plan's collision paragraph. TestDesign collision hold ("Do not ... regenerate the index"), "Existing tests affected" ("Ordinary selector/index outputs are not edited") and the `### Checkpoints` table, which has no index row.
- **Failure:** `DerivedImpactIndexBuilder` adds an edge for every string literal in a test class that equals a tracked path of 8 or more characters (`CollectStringTargets`, around lines 309-313). Today the index has `ReleaseImpactEnvelopeTests` edges for exactly the three paths the class names as literals: the two scripts and `tests/derived-impact-index.json`. S1 must add 12 new literal paths: the fixture copy set, the wrapper paths and the independent oracle. Two of them, `scripts/lib/SelectorCensus.cs` and `scripts/regenerate-derived-impact-index/*`, have no edge today. The rest gain `ReleaseImpactEnvelopeTests` as a new class. After S1, `DerivedImpactIndexTests.committed_index_matches_the_builder` goes red and the advisory selector misses the new edges. No CP row runs that test, so S1 would land with all checkpoints green and leave the next Unit lane, nightly and rc red. The only fix is to regenerate the index, and d2fff5d4 (CARD-1836 S5c, in flight) edits that same file (ScriptHarness and ScriptHarnessProcess cap rows). The sibling plan already handles this: CARD-1820 S1R6 says "Any routine ordinary-index regeneration uses the existing owner script and is committed before tests" and runs `committed_index_matches_the_builder` as CP-9.
- **Why it blocks:** As written, the slice cannot both obey its own boundary and leave master green. It also hides the real file-level collision with S5c that claim (6) requires to be named.
- **Fix (Plan and TestDesign):**
  - Add the ordinary index regeneration to S1's edit set: the existing owner driver, committed before the green run.
  - Add a checkpoint row for `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder`, with its floor and budget.
  - State that this row and the regeneration wait until S5c d2fff5d4 has landed (or released `tests/derived-impact-index.json`), and are regenerated on that integrated base, never merged by hand.
  - In both artifacts, name the CARD-1820 S1R5 script-closure slice beside S1R6. S1R5 adds identity inventory files to `release-impact.ps1` and the path array.
  - Say which CP rows wait on which owner.

## Advisories (no re-review needed)

- **A-1:** The V-6 anchor `CARD-1832. Method declaration` appears twice in `tests/Shared/PlatformTaxonomy.cs` (lines 6 and 22). Bind the first occurrence explicitly so that the test asserts "exactly one differing offset".
- **A-2:** The table header `totalTimeoutMinutes: 68` imports as `explicitTotalMinutes: 68`. That contradicts "No total-timeout override is used". Drop the line or describe it as an explicit override equal to the derived value.
- **A-3:** The existing fixture copies with `ReadAllTextAsync`/`WriteAllTextAsync`, which strips the BOM and normalizes CRLF. The TestDesign requires raw-byte copies, so Code must switch to `ReadAllBytes`/`WriteAllBytes`. Twelve targets carry a BOM.
- **A-4:** The expected serial green run is 29 minutes, just under the 30-minute Debug threshold, and per-row host discovery is unmeasured. If the first run crosses 30 minutes, split it rather than widen it, as the measurement plan already says.

Mutation controls were waived by the operator on 2026-11-12, so all PCs stay pending. This review ran no builds and no tests. It ran one read-only `Antiphon.Checkpoints import` of the TestDesign table into ignored `.antiphon/`, plus `git merge-tree` checks.
