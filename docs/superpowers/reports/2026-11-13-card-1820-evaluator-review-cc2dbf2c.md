# CARD-1820 Final Review: S1R3E source-bound evaluator

Clean Final Review of S1R3E. No blocker and no selector fail-open. Release evidence stays incomplete/Full. Advisories are CARD-1880 and do not hold the land.

## Identity

- Reviewed source: `4266477a650bae1075e783dfc2f619b68a1f0d70` on `feat/card-task-da81e16e`.
- Code landing owner: `da81e16e-5d69-45be-ad31-c927d78d2794`.
- Review task: `cc2dbf2c`. Round 1, Final, ordinary scope Full.
- Own-commit range: `f3f1acd02814acd5d5dde4370e816c497eef09f0..4266477a650bae1075e783dfc2f619b68a1f0d70` (eight commits; the S1R3 commits are already on master).
- Named master: `ec5de7348fe327ed1fb7c67ffb2939098f548dbd`.
- Author qualifying receipt: `20261113-091323-aa21` at `07270c545bf8401539ee7162d6ea6d0e0023ddf7`. This review did not reuse it.

The range touches seven files (+752/-38): the checkpoints plan, the code report, `ReleaseImpactEvaluationTests.cs`, `tests/derived-impact-index.json`, a one-line dispatch in `Program.cs`, `ReleaseInputCommand.cs`, and `ReleaseInputEvaluation.cs`. `scripts/impacted-classes.ps1`, the release-impact scripts, the nightly scripts, and the derived-index driver are unchanged. `git grep` of `release-inputs` outside the tool, the new test, and docs is empty.

## Cherry-pick onto current master

`git cherry origin/master` marks the five S1R3 commits as already upstream and the eight own commits as new. Cherry-picking those eight onto `ec5de7348` applied clean, including the generated index. Scratch tip: `7b41baa5874b5b334aa0cb27896635c9a38142ee` at `/tmp/card-1820-s1r3e-review-master`.

The derived-index driver links `DerivedImpactIndexBuilder.cs`, `ReleaseImpactGraphBuilder.cs`, and `PlatformTaxonomy.cs`. The new evaluator is not one of those inputs; `Antiphon.LandGraphAudit` is an SDK-style project and compiles its own sources. The driver build had 0 warnings. Regeneration with `-NoBuild` printed `derived-impact-index unchanged` (windowsOnly 31, linuxOnly 1).

`--release-graph` on that scratch tip printed `incomplete/Full classes=2097 paths=4423 source=7b41baa58`, 495 reasons (445 `conditional-source-unresolved`, 46 `project-ownership-unresolved`, plus one each of platform, project, runtime, and script closure). That is the S1R2 relation graph. It is not the S1R3 project closure (5,501 paths, 868 reasons, 105.6 s). The graph artifact was deleted.

## Closed checkpoint list at the reviewed SHA

Tool run `20261113-095831-ffb3`, `--expected-source-sha 4266477a650bae1075e783dfc2f619b68a1f0d70`, row timeout 20m, total timeout 45m. Exit 0. Verdict GREEN. Wall 10m36s. One build (291.365 s), then reused. Every row `dirty=0`, `sourceState=clean`, `buildSource=verified`, slot granted, waited 0s. `unlisted: none`. Output `bin-c1820-eval-final/` was deleted by the tool.

| Row | Result |
|---|---|
| CP-106 ReleaseImpactEvaluationTests | 4/4 |
| CP-4 ReleaseImpactProjectTests | 4/4 |
| CP-101 nine-class guard union | 78/78 |
| CP-102 committed index | 1/1 |
| CP-103 LandGraphAuditProjectInstanceTests | 1/1 |
| CP-104 ProcessSpawnLimitTests | 57/57 |
| CP-105 two land-graph evaluation methods | 2/2 |
| CP-107 ReleaseImpactRelationTests | 4/4 |

Sum 151/151, 0 failed, 0 skipped. `validate-checkpoint-receipt.ps1` printed `CHECKPOINT SOURCE VALID source=4266477a650bae1075e783dfc2f619b68a1f0d70 rows=8`.

## Named classes on the cherry-picked tree

One leased `dotnet run --no-build` of the brief's combined filter (evaluator, project, relation, full DerivedImpactIndex, policy, guard, registry merge, tripwire, native Linux witness, envelope) on the scratch tip. Passed 96, failed 0, skipped 0, duration 5m 06s. Lease `1e24eedc-616c-4f3f-bed7-ad5148e6a376`, held 310s. An earlier invocation exited 5 before any test because `--nologo` was forwarded to the test host; it is not a product result.

## Hard checks

1. Source binding. `ReleaseInputCommand` refuses a dirty status, a HEAD mismatch, a non-blob mode, a symlink, a path escape, and a post-evaluation inventory change (`source-drift`). Every tracked regular file is hashed. `Bound` refuses an evaluated path that is not in that inventory or whose sha256 changed. The clean-tree receipt is `release-inputs-v1`, `complete=false`, `fallback=Full`, `evaluationMode=evaluation-only-without-restore`, source `7b41baa58`, 62 projects, 5803 item rows, 5312 distinct repository item paths, 132 SDK item rows, 7211 import rows, 166 distinct import paths. Directory.Build.props and Directory.Build.targets each appear 62 times. Directory.Packages.props appears 0 times; the file is absent. Incomplete reasons: 114 (62 `external-import-unbound`, 42 `package-provenance-unavailable`, 4 `reference-properties-uncomposed`, plus the six always-on reasons). SDK digest, manifest digest, and algorithm digest are 64 hex chars. Target is net9.0 / Debug / AnyCPU / empty RID. Slot hold on that clean run was 19s. Exit 0 is an incomplete inventory, not a land-audit approval. Invalid data returns exit 1. The workload-manifest imports outside `MSBuildPath` are hashed and still marked `external-import-unbound`, so the receipt stays incomplete.

2. Execution surface. The command calls `SolutionFile.Parse`, `ProjectCollection`, and `new Project` with `RecordDuplicateButNotCircularImports | RecordEvaluatedItemElements`. It does not call `Build`, restore, or a target. It does not apply `EvaluationExpressionPolicy` or `LandGraphPrecheck` before `new Project()`, so MSBuild property functions run. That cannot set `complete=true`. CARD-1880 A-1 records the side-effect gap. No nightly or qualification reader consumes the receipt.

3. Superset. The evaluator exports surviving Compile, ProjectReference, Content, None, EmbeddedResource, AdditionalFiles, and Analyzer items. That inventory (5312 repository paths) is not the S1R3 closure (5501 paths) and is not claimed as a path-set superset. The conservative closure still owns directory membership. Nothing in this range subtracts a closure edge. Ordinary selection code is byte-identical.

4. Ordinary index versus the S1R3 base: 2335 edges both sides, 0 paths lost, 0 paths gained, cap 25, capped paths 152, windowsOnly 31, linuxOnly 1. Twenty-six paths change class membership, all by adding `ReleaseImpactEvaluationTests`. The only selected-class loss on an edge is `CheckpointLargeStampTests` on `.gitignore`, which moves to the first owed name (owed 124 to 125) because the fixture literal `[".gitignore"]` takes a cap slot. The class remains in the global selected set through other edges. CARD-1880 A-4.

5. Mutants were not executed. Operator 2026-11-12 waived mutation checks, and PCs stay pending. From the fixtures: ignoring Condition goes red on the net9 versus net8 reference assertion; ignoring Update/Remove goes red because Compile is only `App/Code.cs` and the Content link is `settings.json`; skipping `shared.props` goes red because the imports must contain it and AdditionalFiles must be `schema.json`; treating a dirty or mismatched source as clean goes red because those runs exit 1 and must not print `release-inputs-v1`. The gaps in A-3 of CARD-1880 are the packages file, the dirty-status stand-in for a missing import, and a content-hash equality mutant that keeps git status and the digest.

6. Evidence diff for the own range: 8 commits, 0 entries, 0 violations. `git merge-tree --write-tree origin/master HEAD` exits 1 with the only conflict in `tests/derived-impact-index.json` (stages `57d0cd37`, `e2d052ee`, `3ec0816f`). The cherry-pick auto-merged that file and the builder then reported it unchanged, so landing regenerates the index rather than taking either side raw. OS-witness lint completed for the same range. It printed `CLASSIFY unrecognized` for the generated index and `OS-WITNESS complete syntax/inventory`. Native Windows W1 remains orchestrator-owned and is not credited here.

## CARD-1877

A-2 is folded in: the receipt stores project rows, not path/class pairs. A-3 is partial: Update/Remove and the conditional reference are real assertions; the old/new union still lives in `ReleaseImpactProjectTests`. A-1 is unchanged. Reflection still binds only `Type.GetType` and `Assembly.Load` on the conservative graph, which this slice does not replace.

## Still pending

Native Windows proof W1. SourceLanding mutation, including every PC. Neither is discharged by this Linux receipt or by the waived mutant judgment.
