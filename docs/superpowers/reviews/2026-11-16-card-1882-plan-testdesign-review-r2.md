# CARD-1882 plan and TestDesign review, round 2 (diff only)

Review 6292b6f7, round 2 of at most three (CARD-1635). Same reviewer context as round 1 (Review b65e738f, `docs/superpowers/reviews/2026-11-16-card-1882-plan-testdesign-review.md`, commit `0b502891d76a751be519cddac07173509f802fc9`). Documents only. Blockers only; everything else is advisory.

Subjects and ranges:

- Plan, task `dc76c20f-bead-47c8-a9c6-3b35c36c57c5`: `docs/superpowers/plans/2026-11-16-card-1882-algorithm-digest-plan.md`, diff `6de02276d3d963bcf617e10a42872bff927b4cd6..81e157eab125611c97c095fe785fa6da8cc69a6c` (one commit, fast-forward from the round-1 SHA).
- TestDesign, task `f8dd5dd4-9433-4bec-a779-b7d448639480`: `docs/superpowers/plans/2026-11-16-card-1882-testdesign.md`, diff `ccd3e470199b41c50277cce2554dc70f6e58a9c0..9e68ba04276bf046d7bfd900eb3ee33845d53dc2` (one commit, fast-forward).

Source checked: `origin/master` `18eabe4ce48a46adea27aa9cbb410415eae9ac91`, which is still the master tip.

## Verdict

**Clean, with no blockers.** Both round-1 blockers are fixed, the four advisories are carried, and the amendment adds no new blocker. Three new advisories follow; none needs another review round.

## D-1 (roster omits imported build files): fixed

| Check | Result | Evidence on `18eabe4ce` |
|---|---|---|
| Roster is 18 files; positions 15-18 are the four imported or referenced MSBuild files, appended without renumbering 1-14 | True | Plan "Identity order", TestDesign V-19..V-22 / CP-20..CP-23. |
| Each new file has a one-byte B and a CRLF W witness | True | Wrappers `_15_BuildProps`, `_16_BuildTargets`, `_17_CheckpointRepeatTargets` and `_18_EvaluatorCoreProject` are each `[Arguments(false)]`/`[Arguments(true)]`, with Min 2 per row. The fixture `.gitattributes` gains `*.props` and `*.targets text eol=lf`, so W still shows that filtered `hash-object` hides the drift. |
| Anchors are real, unique, inside a comment and start with an uppercase ASCII letter | True for all four | `Directory.Build.props:3` `Keep MSBuild's default item globs`; `Directory.Build.targets:3` `CARD-0222: cap the growth`; `tests/Shared/CheckpointRepeat.targets:2` `The exact selected csproj alone` (inside `<!-- -->`); `Antiphon.LandGraphAudit.Core.csproj:4` `Clock and approval orchestration` (inside `<!-- -->`). Each occurs once in its file. |
| The four files are LF with no BOM | True | No CR bytes; each starts `<Pr`. |
| Import and Compile claims | True | `Directory.Build.targets:40-41` imports `CheckpointRepeat.targets` (Exists-conditioned); `:46-47` is the `tests/`-parent-conditioned `PwshPrivateCache.cs` Compile; `Directory.Build.props` has no Import and sets `DefaultItemExcludes` at `:15`; the evaluator csproj `:21` holds the only ProjectReference (Core); the Core csproj has none. |
| Exclusion: `Directory.Build.rsp` | Correct | One switch, `-nodeReuse:false`. |
| Exclusion: `PwshPrivateCache.cs` | Correct | Compiled only when the project's parent directory is `<root>/tests`. The drivers' parents are `scripts` and `tools`. |
| Exclusion: `CheckpointRepeatEvidenceAttribute.cs` | Correct | Compiled only when `AntiphonCheckpointRepeatProject` equals the project path and the project references `TUnit`. The index driver references `TUnit.Core`, not `TUnit`, and the evaluator references neither. |
| Exclusion: nightly probe `Directory.Build.*` | Correct | The only tracked `Directory.Build.*` files are at the root and under `scripts/fixtures/nightly/{c1375,c487}-probe/`. Neither probe directory is an ancestor of either driver. `Directory.Packages.props` and `NuGet.config` are not tracked. |
| Exclusion: `EvaluationBudget.cs` and the other Core sources | Acceptable | `ReleaseInputCommand` and `ReleaseInputEvaluation` name only `EvaluationBudget` from Core. Neither project has a `ModuleInitializer` or an `[assembly:]` attribute. The Core csproj, which decides the glob, is bound. See advisory R2-A3. |
| Exclusion: `global.json` | Acceptable, but the rationale is mis-cited | See advisory R2-A1. |
| The digest fails closed on repo-wide MSBuild edits, and the plan says so | True | Plan D-2: "The digest will now change on any repo-wide MSBuild edit. That fails closed." |

## D-2 (S1 makes the committed index stale; S5c collision): fixed

| Check | Result | Evidence |
|---|---|---|
| D-8 puts owner regeneration into S1 | True | Plan S1 edit set and D-8; TestDesign execution protocol steps 1-3. Hand-editing is forbidden in both documents. |
| First run must report `changed`, `-NoBuild` second run must report `unchanged`, each in its own build-slot lease | True and feasible | `scripts/regenerate-derived-impact-index.ps1` has `-NoBuild` and builds with `ANTIPHON_BUILD_SLOT_MAXCPUCOUNT`. `Program.cs:101-102` prints `derived-impact-index changed`/`unchanged`. The two leases are labelled `c1882-index` and `c1882-index-2`. |
| D-8 mechanism claims | True | `CollectStringTargets` adds a tracked path of 8 or more characters (`DerivedImpactIndexBuilder.cs:304-313`). The allowlist counts in the committed index are `Directory.Build.props` 14 and `Directory.Build.targets` 11 (`reason: allowlist`). `tests/Shared/CheckpointRepeat.targets` has one class edge today. |
| V-23/CP-24 row for `DerivedImpactIndexTests.committed_index_matches_the_builder` | True | The method exists (`DerivedImpactIndexTests.cs:135`) and has no `[Arguments]`, so Min 1 is right. The row is in the guard run (S1b), runs only after the JSON commit, and is excluded from the red-first run. |
| Gated on S5c d2fff5d4 landing or releasing the index | True | Plan Collisions row 3 and S1 preamble; TestDesign Collisions and protocol. If S5c lands after the S1 commit, S1 regenerates again in a new commit. |
| S5c's real footprint matches the collisions table | True | `origin/feat/card-task-d2fff5d4` is still at `7154ce531` and is not on master. Its diff touches `tests/derived-impact-index.json` plus rc-ledger scripts, `RcLedgerStatusTests.cs` and its own docs. The index is the only overlap with S1. |
| Collisions name S1R5, S1R6, S5c and admission activation with the right waiting rows | True | S1R5 → CP-1..15 and CP-20..23, with the module joining only by re-plan. S1R6 → the whole S1 edit set and every row, serialized. S5c → the regeneration and CP-24, and therefore the S1 dispatch. Admission activation → CP-17. |

## Totals and import

- I imported the TestDesign at `9e68ba042` read-only with the repository's checkpoint tool (`Antiphon.Checkpoints import --plan <ignored copy> --out .antiphon/c1882-r2/manifest.yaml`). The result was "imported 24 rows" with exit 0 and no warnings. There is one build per After group: `S1` uses only `bin-c1882-s1` and `S1b` uses only `bin-c1882-s1b`.
- Floors: S1 sums to 37 (18×2 + 1) and S1b to 143 (6 + 1 + 78 + 57 + 1), 180 in all. Estimates: S1 sums to 25 and S1b to 17. No run is estimated over 25 minutes.
- There is no `explicitTotalMinutes`. The manifest's `timeouts.totalMinutes: 90` is the schema default and is not used: `CheckpointApp.cs:168-170` takes the request, then the explicit value, then `RowTimeout.DeriveTotalMinutes`. The derived allowances are therefore 60 and 44 minutes, as the TestDesign states.
- Counts are internally consistent: 18 files, 36 drift results, 43 envelope results, 24 checkpoints, and 27 guards with 27 distinct controls (PC-24..PC-27 each omit one path). The PC floor is 27 × 16 + 2 = 434.
- Round-1 advisories:
  - A-1 is fixed: the V-6 anchor is now the unique `PlatformTaxonomy.cs:6` sentence.
  - A-2 is fixed: the `totalTimeoutMinutes: 68` line is removed.
  - A-3 is fixed: the fixture copy is now raw-byte (`ReadAllBytes`/`WriteAllBytes`).
  - A-4 is fixed: the green run is split into two runs under a 25-minute cap. One point remains as R2-A2.

## Advisories (no re-review needed)

- **R2-A1, the `global.json` exclusion rationale is mis-cited.** The plan cites `ReleaseInputEvaluation.cs:39-41` (`sdk-drift`) as the receipt for the SDK. That check hashes the MSBuild that evaluates the audited repo, runs only on `release-inputs`, and is not in the envelope (D-4). It does not receipt the SDK that compiles either driver. The exclusion is still reasonable: `global.json` holds `rollForward: latestMinor` and an Aspire-only `msbuild-sdks` entry, so the compiling SDK can drift with no repo byte changing, and binding the file would not bind the SDK. Fix the citation when the plan is next touched. Carry "driver compile SDK identity is unbound" into the CARD-1836 activation gate next to the static-list reconciliation.
- **R2-A2, the drift-run estimate is packed to fit the cap.** The plan says "7 minutes for the build plus 18 minutes of rows", but the drift run has 19 rows. The TestDesign reaches 25 by folding the cold build and CP-1's two results into one 7-minute row, and it says so. The honest provisional sum is 26, which is over the plan's own 25-minute cap but under the 30-minute Debug threshold. The documents already state the right action: if the first measured build exceeds about 6 minutes, split the run and commission Debug.
- **R2-A3, glob-compiled sources stay unbound.** Binding the two evaluator csproj files binds the glob, not its members. A new or edited `.cs` file under `tools/Antiphon.LandGraphAudit/` or `tools/Antiphon.LandGraphAudit.Core/` (for example a `ModuleInitializer`, or a type that replaces one used on the `release-inputs` path) changes the compiled evaluator without changing any bound byte. The same gap existed in round 1 for the evaluator's other sources. It is outside this card's raw-byte witness and belongs with the closure work in S1R5/S1R6 or the activation gate. Note it there rather than here.

## Checks run

No builds and no tests. I ran:

- one read-only `Antiphon.Checkpoints import` into the ignored `.antiphon/c1882-r2/`;
- `git show`/`git grep` reads on `origin/master`;
- a task lookup through `GET /api/agent-tasks?projectId=…` to resolve the subject GUIDs.

`git merge-tree --write-tree` is clean for each subject branch against `origin/master` and for the two subjects against each other. Mutation controls stay pending under the operator's 2026-11-12 waiver.
