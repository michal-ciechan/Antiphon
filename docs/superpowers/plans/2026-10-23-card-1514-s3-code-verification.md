# CARD-1514 S3 execution and verification

Code owner / landing owner: `2c5c38b8-548d-40ee-8948-6bb8557b1b5a`.
Task base: `9a9fee9ab34385765bea5e84bb7471fb12891865`.
This materializes only S3 of [the landed plan](2026-10-23-card-1514-slow-test-speedups-plan.md).
The caller explicitly skips separate TestDesign and prohibits broader Unit/assembly runs.
S1/S2 remain next Code work; S4/S5 are deferred, unchanged and unmeasured.

## Verification design

V-3: run all 43 original landing scenarios with unchanged assertions and arguments;
41 stay in Matrix and two move to Worker. All existing 20 limiter cases and the
two exact C1404/C1410 script cases also execute. One additional compiled-type guard
makes the new total 66. Its missing optional worker type is explicitly asserted before
reflection; real Type/attribute objects supply the observations. No timeout changes.
R-3: the new topology guard fails on the unchanged strict Matrix, before the fix.
The guard also requires a strict, non-Git worker and the moved method identity.
The new budget wrappers run the real offline packer, not a constant/model of it.
Keep C1410 and the old raw listing unchanged; the projection retains 158 identities.
No other R IDs are defined in the landed plan. V-1/V-2/V-4/V-5 and CP-2..5 remain
outside this S3 commission and are not passed.

Baseline CP-0 and candidate CP-1 use the same mapped selection, same server2 host
class and capture mode (deep landing capture off). Git width is one in the baseline
(the mixed Matrix is strict anyway), and the unchanged default on the candidate.
Report the 43 landing outcomes separately from the added guard's expected red.
Use fresh TRX timestamps for the landing interval union and wall span, and the tool's
row wall/build/slot timings for complete driver cost. No extra hidden test pass.
The baseline is source-bound to the test-first commit; the 43 bodies and limiter
remain byte-identical to the task base. No before/after claim uses the old profile.
Where cloning/startup/process counters are unavailable, say unknown; never infer
independent timings from overlapping spans. One pair is an observation, not an SLA.

PC-3a pending SourceLanding Mutation: put Worker on Git instead of strict; method
`ProcessSpawnLimitTests.C1514_Checkpoint_workers_are_separate_from_the_git_lane`
must fail its strict attribute check. PC-3b: lose worker projection mapping; method
`C1514LandingSplitBudgetTests.C1404_Group10MeasuredBudget` must fail its bijection.
PC-3c: remove the worker cost row; same budget method must fail its cost oracle.
PC-3d: move Matrix back to strict; topology guard must fail its Git attribute check.
Mutation owns deliberate edits/red/restore/green and missing-control discovery.
Candidate manifest: this file; no Mutation performed by Code.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-0 | S0 | `tests/Antiphon.Tests -> bin-c1514-before/` | baseline-and-red | `/*/*/(AgentTaskLandCheckpointMatrixTests*)\|(AgentTaskLandCheckpointWorkerTests*)\|(ProcessSpawnLimitTests*)\|(C1514LandingSplitBudgetTests*)/*` | V-3 baseline; R-3 topology red | 66 executed: 65 pass, exactly topology guard fails; all 43 landing cases pass | 66 | 12 | true | ANTIPHON_TEST_GIT_SPAWN_LIMIT=1; ANTIPHON_C448_EVIDENCE= |
| CP-1 | S3 | `tests/Antiphon.Tests -> bin-c1514-s3/` | checkpoint-lane-split | `/*/*/(AgentTaskLandCheckpointMatrixTests*)\|(AgentTaskLandCheckpointWorkerTests*)\|(ProcessSpawnLimitTests*)\|(C1514LandingSplitBudgetTests*)/*` | V-3 all 43 mapped witnesses, limiter guards and budget; R-3 green | 66 passed, 0 failed/skipped: 41 matrix, 2 worker, 21 limiter, 2 budget | 66 | 12 | true | ANTIPHON_TEST_GIT_SPAWN_LIMIT=; ANTIPHON_C448_EVIDENCE= |

Run one source-bound build per selected row, `--rows CP-0` then `--rows CP-1`,
with committed `--expected-source-sha`; no baseline auto-rerun. Preserve unedited
CHECKPOINT lines. Validate candidate receipts and inspect every TRX identity/count.
One unlisted build bootstraps the checkpoint tool under the build-slot gate into
an external task-owned output; the tool itself leases all test builds/rows.
The final source/report commit precedes final verification; append generated results
outside tracked source so the published final tip is the tested SHA.

Required manual checks: assertion/body bijection; Git-only helper/Windows audit;
unchanged worker lifecycle; raw historical fixture intact; RC scratch cherry-pick;
current-master merge-tree; full base..HEAD evidence-diff; cleanup task-owned outputs.
Restart: none (test and test-selection metadata only).
