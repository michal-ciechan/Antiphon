# CARD-1571 master land-graph record refresh

Original Code/landing owner: task 01b04ae0. Final verification round.
Base: 4497af9b48136a8003def54331f49ac66fd2dec9 (origin/master), including
CARD-1571 Windows path repairs e794522d7 and 7c42927d0. The assigned branch
was fast-forwarded from a2aedb57fc98422a1c1723f4e1f0d4cef40d70f3 without
merging or rebasing.

S1 commits this selection before the red witness. S2 uses only the real
`scripts/audit-land-verification-graph.ps1 -Update -ToolPath <staged evaluator>`
to regenerate the manifest and evaluation record. Never hand-edit the manifest,
raise the evaluator deadline, or approve changed human-pinned target bodies.
If the auditor refuses pins, retain the diagnostics and stop for the required
approval. The original CARD-1483 plan D-5 owns that approval boundary.
The desktop-only rc patch from Debug ae4df578 cannot be read on this Linux host;
inspect reachable rc records read-only and do not infer the patch contents.

## Performance budget

Hard task budget: 45 minutes. Evaluator solution phase remains 120 seconds;
its tool watchdog remains 300 seconds. Bootstrap the checkpoint tool once
through the host build-slot gate (unlisted infrastructure build needed to run
the checkpoint table). CP-1 builds the test project once, including its staged
real evaluator. A post-refresh rebuild is necessary because the manifest is an
embedded resource; reuse that build for subsequent green selections.
Estimates below are provisional (one bounded collection in this task, CARD-1571),
not measured p95. No repeated green proof runs. Retain per-test TRX durations,
checkpoint phase/slot timings and opt-in phase sidecars in ignored evidence.
Any unsupported phase is unknown, with unattributed time reported as remainder.
Existing fixture lifetimes remain unchanged. No fresh AppHost is required by
the targeted witnesses. Whole Unit and full affected classes are required by
the Final stage contract; their invariant is agreement between the actual Git
build-definition inventory, the embedded graph, and fail-closed pin evaluation.
The class selections are unbounded as classes grow; their cost is provisional.
If any selection is expected/observed over 30 minutes, stop and request Debug.

## Verification design

V-1: the existing C1380 target-contained-code witness fails on the stale base,
then passes after regeneration. This is a production-path witness, not a new test.
V-2: C1385 registry comparison and audit -Check agree with the actual Git tree.
V-3: exact target-pin enforcement passes its body/layout/malformed-pin matrix.
R-1: whole Unit lane remains green.
R-2: full affected LandNonCodeSkipTests and LandGraphEvaluationTests remain green.
Manual: real audit success, unchanged evaluation deadlines, and read-only rc
assessment. A pin refusal or evaluator timeout blocks S2 and all post-fix rows;
never report those rows passed. No deliberate mutants in Code. All inherited
CARD-1483 controls (pin comparison, differential, exit-2-as-approval, and S5
publication/record binding) remain pending SourceLanding Mutation; no new PCs.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1571-red/` | stale-witness | `/*/*/LandNonCodeSkipTests/C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff` | V-1 red witness | 1 executed; expected guarded-defect failure | 1 | 5 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1571-green/` | refreshed-witnesses | `/*/*/(LandNonCodeSkipTests\|LandGraphEvaluationTests)/(C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff\|C1385_RegistryMatchesNarrowGraph\|C1483_S3_PinnedTargetMatchesOnlyItsHash)` | V-1, V-2, V-3 | all 3 methods, 0 failed/skipped | 3 | 5 |
| CP-3 | S2 | CP-2 | unit | `/*/*/*/*[Category=Unit]` | R-1 | >= 1 executed, 0 failed | 1 | 8 |
| CP-4 | S2 | CP-2 | affected-classes | `/*/*/(LandNonCodeSkipTests\|LandGraphEvaluationTests)/*` | R-2 | both full classes, 0 failed/skipped | 2 | 10 |
