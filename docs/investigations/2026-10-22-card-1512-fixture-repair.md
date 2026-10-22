# CARD-1512 fixture repair evidence

Owner and original landing task: `0b69108f-2a68-4948-80f2-1f411c960a45`.
Branch: `feat/card-task-0b69108f`.
Worktree: `/work/worktrees/task-0b69108f`.
Task base: `c5da1d2387c08adf86e301a3874a5cbcfd4d6a98`.
Plan: [publication fixture repair](../superpowers/plans/2026-10-22-card-1512-publication-fixture-repair.md).

## Result before repair

The unchanged method reproduced the Windows failure on Linux: 3 executed,
2 passed, 1 failed, 0 skipped. Only `.antiphon/inbox/photo.png` failed at
`op.LastReason`: expected `ignored_content_preserved`, actual
`evidence_retention_unavailable`. Fresh TRX class, method and argument identities
were inspected. The test source and product are unchanged from the task base in
this run; the only S1 change is the committed repair plan.

```text
CHECKPOINT RED-1512 commit=7cc17d89d8fe60e6ef820e1a3540641bf4620897 build=ok filter=/*/*/AgentTaskLandPublicationTests/C448_V04_IgnoredFilesSurviveConfirmedPublication executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-0b69108f/.antiphon/c1512-red-checkpoints/RED-1512-20261022-223007-7238/run.trx slot=granted waited=60s dirty=0 source=7cc17d89d8fe60e6ef820e1a3540641bf4620897 sourceState=clean buildSource=verified
```

Red source: `7cc17d89d8fe60e6ef820e1a3540641bf4620897`. Isolated build:
5m55.71s, zero errors; test host: 54.131s. Slot held 497s, waited 60s.
The explicit brief requires this diagnostic run outside the final checkpoint
table. An earlier invocation with a malformed SHA was rejected at admission:
zero builds/tests, `slot=skipped waited=0s`; it is not a red witness.

## Repair and final verification contract

CARD-1446 plan D-4/S4a/S4b is authoritative: protected names refuse cleanup;
unlisted `.antiphon` files are retained evidence. The two original protected
arguments and all their assertions stay. The image has separate real-retention
and unavailable-retention tests. The real implementation writes under a private
fixture root, with exact binary bytes and SHA-256 checked before unregistration
and after complete cleanup. The unavailable seam preserves bytes and refs and
does not enter removal. No product or shared harness changes, assertion
relaxations, timeout changes, or retries.

This report is committed before CP-1. Final V-1/R-1/R-2 checkpoint facts,
receipt validation, R-3 static checks, source SHA and push confirmation are
recorded afterward in `/work/worktrees/task-0b69108f/.antiphon/task-0b69108f.md`
and the final stored task report; this tracked file does not claim pending work
has passed. CP-1 uses the checkpoint tool already built by the red run, avoiding
an additional bootstrap build. No Unit/full-class/full-suite run per the brief.

PC-1, PC-2, PC-3 and both PC-4 protected variants remain pending for method-scoped
SourceLanding Mutation after ordinary Review and caller landing. Restart: none.

## CP-1 filter correction

The first final row at `5d404f6b6662e8bc4f819548925d01d2cd63e374`
(`20261022-224149-a344`) built successfully but selected zero cases, exit 3.
The OR operands lacked the wildcard suffixes required by the pinned TUnit hint
extractor (`docs/testing-and-build.md`, Combined class filters). This is missing
verification, not a passing test or a defect witness. CP-1 now uses the documented
method OR form with suffix wildcards; its exact four-case roster is unchanged.
A fresh isolated output is required for this committed manifest correction.
This is one failure-driven CP-1 rerun, with no test or product edits.

```text
CHECKPOINT CP-1 commit=5d404f6b6662e8bc4f819548925d01d2cd63e374 build=ok filter=/*/*/AgentTaskLandPublicationTests/(C448_V04_IgnoredFilesSurviveConfirmedPublication)|(C1512_ImageEvidenceIsRetainedBeforeCleanup)|(C1512_UnavailableImageRetentionPreservesSource) executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-0b69108f/.antiphon/checkpoints/20261022-224149-a344/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=5d404f6b6662e8bc4f819548925d01d2cd63e374 sourceState=clean buildSource=verified
```

Build slot wait was 60s; build driver 289.203s; run wall 6m25s. The earlier
launcher attempt `20261022-224027-76ff` refused before executor/build/test launch
because the red output root's ownership marker collided during tool copying.
The staged helper tool was used successfully instead. These outcomes remain
historical and are not relabeled green.
