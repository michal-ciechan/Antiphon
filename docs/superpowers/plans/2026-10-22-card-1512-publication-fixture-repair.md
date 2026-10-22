# CARD-1512 publication fixture repair

Code owner: `0b69108f-2a68-4948-80f2-1f411c960a45`.
Base: `c5da1d2387c08adf86e301a3874a5cbcfd4d6a98`.

## Decision and scope

CARD-1446 [D-4, S4a/S4b](2026-10-22-card-1446-stale-landings-plan.md)
intentionally classifies `.antiphon/inbox/photo.png` as retained evidence. Real
retention copies and verifies it before cleanup. The harness's refusing seam
correctly returns `evidence_retention_unavailable`. `.antiphon/report.md` and
`.claude/settings.local.json` remain protected and return
`ignored_content_preserved`. Repair tests only; no product changes.

The explicit Code brief limits this Final round to the exact repaired methods:
no Unit lane, full class, or assembly run. Existing shared harness code stays
unchanged. The supplied group-10 Debug report and CARD-1512 support this decision.
The separately named `2026-10-22-rc-windows-qualification-interim-reds.md` is absent
from both the assigned tree and fetched master. It was retrieved and read at
`7d47ae6ca0d1e84d13ac5fa604065895f2543420` on the investigation's
`feat/card-task-77c15d47` branch. The group-10 report contains the completed image
failure and its diagnostic source references.

## Slices

S1 records the unchanged failing test and this verification contract before the
red witness. Run the existing three-argument method through
`scripts/run-checkpoint.ps1`, with expected committed SHA, isolated output
`bin-c1512-red/`, and filter
`/*/*/AgentTaskLandPublicationTests/C448_V04_IgnoredFilesSurviveConfirmedPublication`.
This is the one explicitly requested diagnostic run outside the final table;
expect two protected passes and one image assertion failure. Preserve its fresh
TRX and exact checkpoint line before editing the test.

S2 keeps the two protected arguments, adds a dedicated unavailable-retention
image refusal, and exercises real retention at a fixture-owned root with bytes
and SHA-256 observed before removal and after successful cleanup. Commit the
repair and report before the final checkpoint; final receipt facts stay in the
ignored task report so the tested SHA is the pushed tip.

## Verification design

| ID | Required observation |
|---|---|
| V-1 | Real image retention preserves exact binary bytes and SHA-256 before removal and after cleanup; publication confirmed and cleanup complete. |
| R-1 | Both protected paths retain their original bytes, refuse with `ignored_content_preserved`, and preserve local/remote source refs. |
| R-2 | Unavailable image retention refuses with `evidence_retention_unavailable`, preserves the source bytes and refs, and does not remove the worktree. |
| R-3 | Static test-only diff, evidence-history guard, merge-tree against fetched master, and patch applicability to fetched rc/2026-10-20. |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1512-final/` | publication-fixture | `/*/*/AgentTaskLandPublicationTests/(C448_V04_IgnoredFilesSurviveConfirmedPublication)\|(C1512_ImageEvidenceIsRetainedBeforeCleanup)\|(C1512_UnavailableImageRetentionPreservesSource)` | V-1, R-1, R-2 | all 4 cases, 0 failed/skipped | 4 | 7 |

### Mutation handoff

All PCs remain pending for method-scoped SourceLanding Mutation. Candidate:
PC-1 reclassify the image as protected (retention success must fail);
PC-2 skip/corrupt the retained copy (byte/hash and before-removal observations
must fail); PC-3 permit removal when retention is unavailable (refusal/source
assertions must fail); PC-4 remove protected-name precedence (both protected
variants must fail). Deliberate mutants and missing-control discovery belong to
Mutation. No mutation is claimed here.

### Cost

Final row: estimated 7 minutes including isolated build. One additional red
witness build/run is required by the brief. No repetitions after green.
Restart: none.
