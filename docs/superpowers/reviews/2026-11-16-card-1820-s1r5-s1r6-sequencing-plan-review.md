# CARD-1820 S1R5/S1R6 sequencing plan review, round 1

CLEAN. No blocker. Advisories are not required to land.

Reviewed source `03b7faa9791e3668e5ff89f18838c84f20ae3492` on `origin/feat/card-task-dc5b6c22`, plan owner `dc5b6c22-1144-4f18-a66e-285f6b0ae26f` (Codex). The range is `61259342c25c9960c12ac7171488e52b61dc03be..03b7faa9791e3668e5ff89f18838c84f20ae3492` (commits `c49ef4d3bac84e0d6e7b4f70538c92abe4fc1fc3` and `03b7faa9791e3668e5ff89f18838c84f20ae3492`). `origin/master` is `870a93d21f2b18c85a8b0905ab689b28e3099813`. The diff is only `docs/superpowers/plans/2026-11-16-card-1820-s1r5-s1r6-sequencing-plan.md`. Reviewer is Grok. Mutation stays waived (operator 2026-11-12). Positive controls stay pending. CARD-1820 was read at revision 51, including history through the Plan settlement and this Review dispatch.

## S1R5 is dispatch-ready

Named files are the new `scripts/lib/release-impact-scripts.ps1`, `tests/Antiphon.Tests/Scripts/ReleaseImpactScriptTests.cs`, fixture data under `scripts/fixtures/selector-v2/`, and owner-generated `tests/derived-impact-index.json`. None of the three new paths exist at this SHA. The must-not-touch list excludes the graph builder, project and runtime resolvers, the main selector, the envelope serializer and identity array, the graph-driver source and csproj, `tests/impacted-classes.json`, policy and roster files, rc admission and activation, stage bundles, and the production scripts being parsed. A needed extra production helper stops for an inventory amendment.

The four V-5 methods are the parent plan's methods, and each can go red against the current relation:

| Witness | Why the current tree fails it |
|---|---|
| `BasenameReachesBaselineHelper` | `ReleaseSelectionCliTests` passes the basename `test-release-selection-cli.ps1`. `ScriptHarness` resolves that basename under `scripts/`. The script's constant foreach `Join-Path` loads `lib/release-selection.ps1`, and `Get-AntiphonScriptsCli` compiles every `scripts/lib/Antiphon.Scripts/*.cs`, including `ReleaseSelection.cs`, which calls `Baseline.Read`. `ReleaseImpactGraphBuilder` records a path hit only when a whole string literal equals a tracked path, so the basename never reaches `Baseline.cs`. |
| `DotSourceClosureReachesAuthority` | `ReleaseGatePublicationAuthorityTests` and `ReleaseGateAuthorityContractTests` pass the basename `test-release-gate.ps1`. That script dot-sources `lib/release-authority.ps1` with a constant `Join-Path`. The same whole-literal limit misses the edge. `TestSelectionTests` likewise passes `test-test-selection.ps1`, which dot-sources `lib/test-selection.ps1`. |
| `EmbeddedPathReachesParityTest` | `NativeLinuxPolicyMatcherParityTests` appends `. (Join-Path $Repo 'scripts/lib/nightly-coverage.ps1')` as one command-shaped literal. The literal is not the tracked path, so whole-string equality drops the edge. |
| `DynamicScriptTargetIsIncomplete` | The builder always emits global `script-closure-unavailable` with `complete=false` and `fallback=Full`. It has no caller-path unresolved reason. The plan requires that exact reason, incomplete, and Full, and says the global fence alone cannot pass. There is no `Skip`. A thin red adapter may compile against the old relation; the finished tests call the new library, and four executions must fail four assertions. |

Checkpoint import of this plan, using the already-built checkpoint tool and writing ignored `.antiphon/c1820-plan-review-import.yaml`, accepted 4 rows, one build `tests/Antiphon.Tests -> bin-c1820-r5-green/`, and zero warnings. CP-106, CP-107, and CP-108 reuse that build under After `S1R5`. Filters, mins, and estimates are CP-6 min 4 / 10 minutes, CP-106 min 1 / 3, CP-107 min 81 / 3, CP-108 min 11 / 2. The sum is floor 97 and 18 estimated minutes. Imported row timeouts are the derived 30/15/15/15. The plan's later YAML overrides (`timeouts.rowMinutes: 18`, checkpoint `timeoutMinutes` 4/6/6/4) match the scheduler: the build deadline is `max(15, rowMinutes, row deadlines)` so it stays 18 minutes, and each test row keeps its own timeout. `--row-timeout` would replace both, and the plan forbids it. No explicit total is set, so a run derives `max(30, 2*18+10)=46` minutes. The serialized `totalMinutes: 90` is the unused manifest default. Budgets are provisional (n=0) and name the first-five-sample, near-2x-p95 follow-up. Output paths end in a forward slash. Red uses a separate `bin-c1820-r5-red/` manifest.

The nine CP-107 class operands each have one declaration and no prefix sibling, counting the linked `tests/Shared` guard and witness classes. At this SHA the leased nine-class run executed 81, failed 0, skipped 0.

`ReleaseImpactEnvelopeTests` is 11 results today: seven single tests plus four `AlgorithmSourceDriftIsRefused` arguments. That matches CP-108's floor before CARD-1882. `DerivedImpactIndexTests.committed_index_matches_the_builder` exists for CP-106. CP-6's class is not in the tree, so that row was not executed.

## Fail-closed

Unknown, ambiguous, dynamic, unresolved, external, missing, unparsed, and budget-exhausted closures keep a path-specific reason and Full. Bound exhaustion returns unknown rather than a truncated complete result. The plan forbids regex-only completeness, ambient checkout enumeration, schema-1 negative proof, and guessing an ambiguous working directory. S1R5, CARD-1883, and CARD-1882 retain incomplete/Full. S1R6 may admit a bounded synthetic complete graph only when every required graph, evaluation, target, and identity check passes; any real-tree gap stays explicit Full. Clearing an inherited reason because a module now exists is rejected. Nothing in the sequence turns an unknown or ambiguous closure into a skipped test.

## Order and the six collisions

CARD-1836 S5c tip `7154ce531a8784fa1d48ba3b778bae134fb3e7cc` exists and is not an ancestor of `origin/master`. The plan treats that as a dispatch dependency, not as publication, and refuses to merge that task branch here. After S5c is published, the order is 1820 S1R5, then 1883 S1..S3, then 1882 S1, then 1820 S1R6. Each arrow includes an independent Final/Full Review and a successful Merge.

All six pairs share `tests/derived-impact-index.json`. No pair that the plan treats as colliding is actually disjoint, and the plan does not treat any of the six as disjoint.

| Pair | Handwritten files, checked against the plans and the tree |
|---|---|
| S1R5 / 1883 | S1R5 does not edit `ReleaseImpactProjectTests.cs` or `ReleaseImpactProjectGraph.cs`. 1883's S1 and S2 edit only those two, then S3 regenerates the index. |
| S1R5 / 1882 | S1R5 must not edit `scripts/lib/release-impact.ps1`. 1882 edits that identity array and `ReleaseImpactEnvelopeTests.cs`. The new script module is the file-19 input 1882 does not yet bind. |
| S1R5 / S1R6 | S1R5 must not edit `scripts/impacted-classes.ps1`, `scripts/lib/release-impact.ps1`, or `scripts/regenerate-derived-impact-index/`. S1R6 owns those composition files. Both add fixtures and regenerate the index. |
| 1883 / 1882 | 1882 does not edit `ReleaseImpactProjectGraph.cs`. That file is roster entry 7, copied and hashed by 1882 CP-7. 1883 repairs it. Both write the index. |
| 1883 / S1R6 | S1R6 may edit project or runtime adapters only after its TestDesign names them. Those files are 1883's repair surface. Both write the index. |
| 1882 / S1R6 | Both edit `scripts/lib/release-impact.ps1`, the envelope fixtures, and the index. S1R6 also edits `scripts/impacted-classes.ps1`, which is 1882 roster entry 1 and a drift target 1882 does not itself edit. |

Exactly one of the four stages holds the index. After the source commit, and with no test in flight, each stage builds `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` with `UseAppHost=false` and no manual output path, then runs `scripts/regenerate-derived-impact-index.ps1 -NoBuild` twice under separate leases. The script implements `-NoBuild` as "run the already-built driver". The second run must be unchanged. Merge repeats on the assembled tree without `-NoBuild` on the first call, then one leased `-NoBuild`, which matches the script's Merge comment. The index is not hand-merged.

## File 19

The claim is true. CARD-1882's amended roster is 18 ordered paths, from `scripts/impacted-classes.ps1` through `tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj`. `scripts/lib/release-impact-scripts.ps1` is not among them, and the file is absent here. CARD-1882's collision row already says that if S1R5 lands first, that module joins as file 19 in a re-plan and is never added silently.

D-3 states the amendment: add that path as ordered file 19; add raw-byte B and W wrappers, the fixture copy, and an independent digest-oracle entry; append one checkpoint without renumbering CP-1..24; update floors and costs; import with zero warnings; plan-review the amendment before stage 3. The stated floors follow the TestDesign: 36 drift results, 43 envelope results, and 180 overall (43+1+78+57+1). One new deciding file adds two drift results, so the floors become at least 38/45/182. The plan says to recompute rather than treat those as executed counts, and to split the drift run instead of stretching the 25-minute estimate. The amendment edits docs only and may run beside stage 2; it must settle before stage 3.

S1R6 still needs its own integrated TestDesign. The plan names the files, the four V-6 outcome witnesses, the reflection and digest regressions, and the carried round-2 gaps (unbound driver SDK identity, and csproj globs that do not bind added `.cs` members or a ModuleInitializer). Nineteen path hashes are not treated as a complete execution closure.

## Gates and parallel work

Each stage states a different-company Review round 1 and a Merge before the next stage starts. Interim is not a landing gate. The three-round, diff-only policy is restated, including not restarting an exhausted subject's Review. Land uses the original Code owner, `git merge-tree --write-tree`, final-tree regeneration, and no local rebase, amend, or force-push.

The parallel-safe items are this plan review, the file-19 Plan/TestDesign amendment, and the S1R6 TestDesign, and only when each writes a distinct Markdown file. They do not share `ReleaseImpactProjectTests.cs`, `ReleaseImpactProjectGraph.cs`, `scripts/lib/release-impact.ps1`, `scripts/impacted-classes.ps1`, the driver, or the generated index with the stage that may be in Code. Investigations and standalone parent modules are allowed only when they do not edit the selector, driver, index, or owner docs. No second writer of the index is allowed.

## Checks at the reviewed SHA

| Check | Result |
|---|---|
| `git merge-tree --write-tree origin/master HEAD` | tree `20010fa41d6eb45b241a4cc3cac92aa096078029`, no conflicts |
| `scripts/check-evidence-diff.ps1` base..HEAD | commits=2, entries=0, violations=0 |
| `scripts/impacted-classes.ps1 -Range` | kinds, classes, files, filters, and windowsFilters empty |
| Downgrade selector | migration row did not match; the range is one Markdown file; downgrade count 0 |
| `scripts/check-os-witnesses.ps1` | OS-WITNESS complete; only the plan markdown changed; restart not required |
| Checkpoint import | 4 rows, one shared build, zero warnings |
| Leased nine-class guard run, slot `968bd4ea-dbf5-406a-a271-f147bef08276`, started at load 15.40 | total 81, failed 0, skipped 0, 57s 294ms, exit 0. The slot warned that `dotnet run`'s implicit build does not receive `-maxcpucount`; the lease was still held for the whole command (556s). |

`reviewedSourceClean` stays false. The guard run is a build-slot lease, not a checkpoint-tool receipt, and CP-6 cannot run until its class exists. Producer/recipient delivery and CARD-1083 input-wait release do not apply. No OS-selected production path changed.
