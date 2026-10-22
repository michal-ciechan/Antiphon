# CARD-1483 S3 unbound mutation candidates

Owner: original Code task 0ca57dc9-7d5c-459f-8bc2-bf201f51e528.
These are pending SourceLanding Mutation obligations, not executed controls.
Bind after ordinary Review and publication. Mutation owns deliberate defects,
red/restore/green, and discovery of missing controls. Do not widen timeouts or
weaken assertions. Use one exact method filter per cycle.

| ID | Candidate defect / variants | Detecting filter | Expected red |
|---|---|---|---|
| PC-S3-1 | Remove target scan/comparison; relative-dir, identity, task-properties, property-body, target-framework, imported-props | `/*/*/LandGraphEvaluationTests/C1483_S3_CardWitnessesRefuseAsUnpinnedTargets` | Each affected variant returns approval instead of missing-pin refusal |
| PC-S3-2 | Ignore changed hash, stale row, duplicate row, malformed row, duplicate target, or file identity (separate variants) | `/*/*/LandGraphEvaluationTests/C1483_S3_PinnedTargetMatchesOnlyItsHash` | Named invalid case approves |
| PC-S3-3 | Collapse significant task attribute spaces, property text spaces, or whitespace-only leaf text (separate variants) | `/*/*/LandGraphEvaluationTests/C1483_S3_PinnedTargetMatchesOnlyItsHash` | Exact whitespace-only value must approve; removed/changed bytes must refuse |
| PC-S3-4 | Omit OS detection for OS, IsOSPlatform, RuntimeInformation; omit element-path identity (separate variants) | `/*/*/LandGraphEvaluationTests/C1483_S3_OsConditionedElementNeedsPin` | Missing/changed/moved OS element approves |
| PC-S3-5 | Scan evaluated imports only, or skip unusually named XML definitions | `/*/*/LandGraphEvaluationTests/C1483_S3_PinnedTargetMatchesOnlyItsHash` | unused.props or Directory.Build.custom approves |
| PC-S3-6 | Trust an untracked pins file | `/*/*/LandGraphEvaluationTests/C1483_S3_PinnedTargetMatchesOnlyItsHash` | Untracked pin fixture approves |
| PC-S3-7 | Treat matching pins as sufficient approval and skip evaluated item checks | `/*/*/LandGraphEvaluationTests/C1483_S3_PinnedTargetMatchesOnlyItsHash` | Pinned target plus README item approves |

All inherited S1 controls and PC-S2-1a/1b/1c/2a/2b/3a/3b/3c remain pending.
The original plan's differential and exit-2-as-approval controls remain pending
with S4/S5. Discovery should examine XML namespaces, inherited xml:space,
linked/missing pin files, DTD rejection and less common definition extensions.
The pin-review trust boundary includes task execution and dynamic parameters;
this slice does not execute nested MSBuild tasks (accepted residual R-3).
