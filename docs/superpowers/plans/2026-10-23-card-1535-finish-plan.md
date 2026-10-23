# CARD-1535 allowlist finish

Code and landing owner: task `c6e43a93-af54-41a9-8d18-b0626a9ca9f3`.
Assigned base: `3f3afb6f6742184cdf3342bb87b89b6108b6ba51`.

Restore the four registrations from `0114415576a5a258491ef8bce8064a74281c7569`
and add the immediately adjacent CARD-1514 S3 reason above
`AgentTaskLandCheckpointWorkerTests`. The worker row is present in fetched master;
S3 landed at `71e578108d6a9604f6f8d7d9f1aae5e45dbd0130`.
`TestClassificationMetadata.ValidateRegistryLayout` requires a single-# reason
on the immediately preceding physical line, with no intervening blank line.

The inherited guard is the regression witness; no new test or production change.
The caller supplies historical baseline RED/repaired GREEN for the four-row repair.
The supplied S3 Review measured the worker variants at 28.9139668 and 23.8012380
seconds at source prefix `2178c71f3`; these are historical timings, not this run.

V-1 / R-1: the exact registry guard must execute once and pass with clean,
SHA-bound verified-build receipts. No Unit lane, affected slow-class execution,
full assembly, repetition or desktop work is required by the explicit finish brief.
No PC IDs or variants are defined; Mutation retains missing-control discovery.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1535-finish/` | registry-finish | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata` | V-1 / R-1: compiled Slow metadata and adjacent reasons | 1 passed, 0 failed/skipped | 1 | 5 |

## Publication and rc handling

Commit this short report before qualification so the pushed final tip is the tested
source. Generated results stay ignored under `.antiphon/checkpoints/`; final facts
and unedited CHECKPOINT lines go in `.antiphon/task-c6e43a93.md` without a new commit.
One extra gated build may bootstrap the checkpoint tool because this clean checkout
has no compiled tool. It is infrastructure preparation, not another test selection.
Remove task-owned alternate outputs before and after qualification.

The allowlist uses `merge=union`. At inspected `origin/rc/2026-10-20`, only
`BlockedReplyReportRetentionTests`, `DeliveryUnverifiedDiagnosticTests` and
`DispatcherLeaseSpanTests` exist and need the corresponding reason/registration
pairs. `TestSchemaSnapshotTests` and `AgentTaskLandCheckpointWorkerTests` are absent;
defer their rows/reasons until their classes land on rc. A blind union pick would
leave unknown-class entries. No rc edit or activation is authorized here.

Review is required after ordinary verification, including with zero PC rows.
Restart: none.
