# CARD-1330 — restore the orchestrator argv reserve

Master-only. Implement the deletions in `docs/investigations/2026-10-17-card-1330-investigation.md` (`feat/card-task-ed28451e` at `d204ca43d99bf2daeb59f0a7c65a98e61cae2045`).

## Change

Delete 197 unpinned characters from `server/Bundles/orchestrator.md`: the scarce-context sentence, the duplicate "On every completion dispatch the named next stage." sentence, and the StoppedBeforeFirstPrompt meta clause. Keep the keep-six line. Re-pin `CheckpointRepeatDocumentationTests` to the raw SHA-256 of the edited file. Do not change `CommandLineBudgetChars`, the 500-character reserve, or the 30,000 ceiling. The embedded header version is the content hash; `C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders` has no hardcoded orchestrator version.

The keep-six documentation class pins `scripts/keep-six.ps1` and `The server does not dispatch`. Those bytes stay, so that class is not a checkpoint row.

## Verification design

The budget failure already exists on master. No new test is added. The brief names this closed list and forbids a whole-lane run.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1330/` | budget | `/*/*/InstructionBundleTests/the_worst_case_composition_measured_sits_far_under_the_budget` | V-1 | 1 executed, 0 failed | 1 | 15 |
| CP-2 | S1 | CP-1 | embedded | `/*/*/InstructionBundleTests/C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders` | V-2 | 1 executed, 0 failed | 1 | 4 |
| CP-3 | S1 | CP-1 | pin | `/*/*/CheckpointRepeatDocumentationTests/*` | V-3 | 1 executed, 0 failed | 1 | 4 |

### Cost

Ordinary floor: 23 minutes.
