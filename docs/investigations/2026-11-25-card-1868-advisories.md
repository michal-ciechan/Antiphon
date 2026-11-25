# CARD-1868 advisories (CARD-1820 S1R2 Review e6b4c624)

Debug task 99e597b8, base `bdca854ff5ecb92400f45c453b751211f6e192c4`, branch
`feat/card-task-99e597b8`, server2 runner mirror. The card names three advisories in its
title: type-edge precision, delegate gap and driver precondition (A-1 has two halves; A-2 is
the driver). A-3 (class cost re-measure after CARD-1862) is not one of the three and is not
claimed here.

| Advisory | Outcome | Commit |
|---|---|---|
| A-1 delegate gap | Repaired. Red witness, then fix. | red `3b9e5ea22e6f70d5e7c4afd8a828f9c24dbbad8c`, fix `6da82bb49e9b31a2f4290bdebbedb27a7d34988e` |
| A-2 driver precondition | Already resolved on master by `cda2783ee3245b1c9c09f2e253e3b17fcfaca005` (2026-11-24). Green proof on a cold checkpoint output with no driver preparation. | none needed |
| A-1 type-edge precision | Not implemented. It needs a plan decision (below). | none |

## A-1 delegate gap

`ReleaseImpactGraphBuilder` built its simple-name target map from
`BaseTypeDeclarationSyntax` only. `DelegateDeclarationSyntax` is not one, so a file that
declares only delegates (top-level, generic, or nested in a class no consumer names) got no
`type` edge at all.

Red witness `ReleaseImpactRelationTests.DelegateOnlyFileGetsTypeEdge` (commit `3b9e5ea22`):
three test classes use `ProbeCallback`, `ProbeFactory<int>` and the nested `ProbeNested`.
At `3b9e5ea22` the run (build-slot build of `tests/Antiphon.Tests` in its native output, then
`--treenode-filter "/*/*/ReleaseImpactRelationTests/DelegateOnlyFileGetsTypeEdge"`) was
`total: 1 failed: 1` with `ShouldAssertException: Members(graph, "shared/Callbacks.cs")
but was` empty, at `ReleaseImpactRelationTests.cs:96`.

Fix (commit `6da82bb49`): delegate identifiers join the simple-name target map. They are
positive targets only and never join the class universe. The change only adds edges, which
is what plan D-2 allows ("fine type/call edges add reasons; they cannot subtract").
`ReleaseImpactProjectGraph` already indexed delegates for its reflection lookup and is
unchanged.

The ordinary (capped, schema-1) `DerivedImpactIndexBuilder` has the same shape gap
(`TypeDeclarationSyntax` plus `IsClassLike`). That index is not release evidence; changing it
rewrites `tests/derived-impact-index.json`, so it is left alone. Regenerating the ordinary
index with the fixed driver printed `derived-impact-index unchanged`.

Classification: the builder lives under `tests/` but the `--release-graph` mode of
`scripts/regenerate-derived-impact-index` compiles it, and `RcAdmission` lists it in the
release algorithm inventory. It is therefore treated as release tooling, not test-only, so the
next stage is Review.

## A-2 driver precondition

The card: a plan that selects `DerivedImpactIndexTests` class-wide, or the regenerate test,
went red unless the `Antiphon.DerivedImpactIndex` driver had been built natively first.

`cda2783ee` (2026-11-24, "test: stage the derived impact driver with the test assembly") adds
a `StagedHelper` for `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`
(folder `derived-impact-index`) to `tests/Antiphon.Tests/Antiphon.Tests.csproj`.
`regenerate_command_reports_changed_then_unchanged` (renamed from
`regenerate_script_reports_changed_then_unchanged`) now runs that staged driver from
`AppContext.BaseDirectory` and asserts it exists. Every test build stages the driver, so
plans no longer need a native driver build in their preparation. No owner doc still requires one.

Green proof: CP-2 below runs that method on a fresh checkpoint build output with no driver
preparation declared or run.

## A-1 type-edge precision: needs a decision

The advisory asks to qualify ambiguous simple names (namespace, `using`, alias resolution) and
drop the false-positive `type` edges (one helper file reached 1,978 consumers). That removes
edges from the release relation. The governing plan
[2026-11-12 selector v2](../superpowers/plans/2026-11-12-card-1820-selector-v2-trustworthy-for-skipping-plan.md)
forbids that in this version:

- Edge table, "ambiguous symbols": "include ... all candidates for ambiguous source names.
  Do not infer a negative from failed simple-name lookup."
- D-2: "Fine type/call edges add reasons; they cannot subtract from project closure in this
  version."

The S1R3 plan also says this work "belongs to S1R6/V1, not this project-wide conservative
boundary". The precision also saves nothing today because project closure already selects
every class in the owning test project.

A syntactic resolver would also have to bound every way a simple name reaches a type without
a semantic model: global usings in any project file, `<Using Include>` and SDK implicit usings
(which need project evaluation, still `project-evaluation-unavailable`), `using static`,
aliases, nested types inherited from base classes in other namespaces, and `extern alias`.
Each one it misses becomes a false negative. Today the builder only reads names inside type
declarations, so `using X = N.Foo;` aliases are already unseen.

Two decisions are needed before anyone repairs this, and the plan owner or operator makes them:

1. Amend D-2 and the ambiguous-symbols row so a qualified `type` edge may narrow the
   relation. Today it may only add.
2. Choose the resolver. Either a Roslyn semantic model over evaluated compilations, which
   waits on the S1R3 source-bound evaluator, or a syntactic resolver. A syntactic resolver
   keeps every unresolvable case as all candidates and records an incompleteness reason.

Until both are made, all candidates stay positive, as the plan requires.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | final | `tests/Antiphon.Tests -> bin-c1868-green/` | release-relation | `/*/*/ReleaseImpactRelationTests/*` | A-1 delegate witness and relation regressions | five methods, zero failed or skipped | 5 | 15 | true |
| CP-2 | final | CP-1 | driver-staged | `/*/*/DerivedImpactIndexTests/regenerate_command_reports_changed_then_unchanged` | A-2 with no native driver preparation | one method, zero failed or skipped | 1 | 2 | true |
| CP-3 | final | CP-1 | ordinary-index | `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder` | committed ordinary index unchanged | one method, zero failed or skipped | 1 | 2 | true |

One cold build is shared by all three rows. No native driver build is declared or run; that
is the A-2 proof.

## Receipt

Checkpoint run `20261125-191731-1f24` at source `9e3af113b6034c1d8ddf12ae0bb93011cc2b88e7`
(server2, Debian 12, 24 cores). The source was clean and the build source was verified. The
run did one cold build of 275 s and no other build or test command.

```
CHECKPOINT CP-1 ... filter=/*/*/ReleaseImpactRelationTests/* executed=5 passed=5 failed=0 skipped=0 ... sourceState=clean buildSource=verified
CHECKPOINT CP-2 ... filter=/*/*/DerivedImpactIndexTests/regenerate_command_reports_changed_then_unchanged executed=1 passed=1 failed=0 skipped=0 ... sourceState=clean buildSource=verified
CHECKPOINT CP-3 ... filter=/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder executed=1 passed=1 failed=0 skipped=0 ... sourceState=clean buildSource=verified
wall: 11m28s  sequential-equivalent: 1m14s  builds: 1  rows: 3 green 0 red 0 skipped
verdict: GREEN exit=0
CHECKPOINT SOURCE VALID source=9e3af113b6034c1d8ddf12ae0bb93011cc2b88e7 rows=3
```

`scripts/antiphon evidence-diff --base bdca854ff5ec...` over commits=3 found entries=0 and
violations=0. `git merge-tree --write-tree origin/master HEAD` was clean after a fresh fetch.
This commit adds only the receipt text to this doc.
