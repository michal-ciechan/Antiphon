# CARD-1995 land refusal fails fast and keeps the build error, checkpoints

Code task `6770c2e5`, base master `81add8595d60295b4bf1d3bb76af2e927842421f`. Branch `feat/card-task-6770c2e5`. The red witnesses were committed and confirmed before this fix (`1a9afb4f498b52b5a0d9f395ffcc09ac3b849839`, filter `/*/*/*/*C1995*`, three failed and none passed). CARD-1994 warm-cache reuse, quarantine, and pruning stay as landed. No deploy and no restart.

## Design

1. **Keep the build error.** A real verification failure stores the failing step and the last 40 lines on the land operation. `-Status` prints that step and that output. The event detail keeps the same text, clipped at the existing event cap, with the end retained.
2. **Show the phase.** `-Status` names the current phase (`queue`, `admission`, `git`, `build`, `push`, `confirm`) and the UTC instant it started. A Completed request reports `noProgressSeconds` as 0 and does not print the no-progress counter.
3. **Fail fast on a known-broken target.** When master already has a recorded `build` failure for this target SHA, a land whose diff does not change that project is refused in-process with `target_build_broken` and the saved output. Changing the project includes its csproj, files under its directory, and Compile, Content, None, and EmbeddedResource includes, including links, read from the target SHA. A later passing build or a `tests` failure is not a broken target. A docs-only skip does not consult this record. An unreadable project, a glob include, or a diff that cannot be listed builds as usual.
4. **The server build stays the gate.** A plain `-Land` with no review evidence is valid. A review does not skip the build.

No timeout or assertion is loosened. `LandingVerifier` still marks a generation ready when the build child exits 0 or 1.

## Verification design

- V-1. The failing step and the last 40 lines are stored and `-Status` prints them (`C1995_LastLinesKeepsTheErrorAndDropsTheEarlyMarker`, `C1995_StatusPrintsPhaseAndKeepsTheBuildError`).
- V-2. `-Status` names the phase and its start, and a Completed request stops the no-progress counter (`C1995_CompletedStatusStopsNoProgress`, the status witness above).
- V-3. A second land of a target whose build already failed, and whose diff does not change that project, is refused with `target_build_broken` without another build. A linked compile item and a project that moved still build (`C1995_KnownBrokenTargetRefusesWithoutAnotherBuild`, `C1995_LinkedCompileStillBuilds`, `C1995_MovedTargetStillBuilds`, `C1995_ProjectListAndIncludesTreatALinkAsTheProject`).
- V-4. `docs/orchestration-loop.md` states that a plain `-Land` is gated by the server build and that a review does not skip that build.
- R-1. Every `LandWarmArtifactsTests` and `LandVerificationBuildSlotTests` case passes, including the CARD-1994 ready-after-failure assertions.
- R-2. Land failure diagnostics, delegate land status, the failure helper, and the migration metadata witness pass.
- R-3. `AgentTaskLandStageOutcomeTests` passes, including `land_verify_failure_writes_verify_found`. The five rows with a 180-second timeout are their own checkpoint so the earlier rows stay short.
- R-4. `TestClassificationPolicyTests` runs once, inside the guard union. `DerivedImpactIndexTests` runs alone and again inside that union. `C1729LayeringGuardTests` stays at 0 violations.
- R-5. `scripts/audit-land-verification-graph.ps1 -Check` approves. The Linux copy budget on this tree is 8 seconds; a clock hit is rerun once alone.
- R-6. `scripts/check-evidence-diff.ps1` over base..HEAD, and `git merge-tree --write-tree origin/master HEAD` after `git fetch origin master`, are clean.
- M-1. Migration `20261119045705_AddLandVerificationFailure` is additive, sorted after `20261114063858_AddSessionTranscriptSummaries`, sets `lock_timeout`, and its Down throws. The metadata witness covers that. `C1514HostLoadMigrationTests` and `C0824RefOnlySnapshotMigrationTests` pin the new tip and are Slow, so this short Final does not execute them.

No PC-n is named. Mutation execution is waived (operator 2026-11-12) and is not discharged. Nightly pins are recomputed and reported outside this table: the print command exits 0 whether or not the value matches the committed pin.

Red confirmation of the three witnesses is the unlisted run already recorded on `1a9afb4f498b52b5a0d9f395ffcc09ac3b849839`. It is not a row here because this plan closes the fix, where those witnesses pass.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1995-proof/ | c1995-witnesses | `/*/*/*/*C1995*` | V-1, V-2, V-3 | 8 cases, 0 failed | 8 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | S1 | CP-1 | touched-land | `/*/*/(LandWarmArtifactsTests*)\|(LandVerificationBuildSlotTests*)/*` | R-1 | 36 cases (22 + 14), 0 failed | 36 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | S1 | CP-1 | land-status-migration | `/*/*/(LandFailureDiagnosticTests*)\|(DelegateScriptLandStatusTests*)\|(LandVerificationFailureTests*)\|(LandVerificationFailureMigrationTests*)/*` | R-2, M-1 | 39 cases, 0 failed | 39 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | S1 | CP-1 | stage-outcome | `/*/*/AgentTaskLandStageOutcomeTests/(land_happy_path*)\|(C1470_*)\|(land_conflict*)\|(unreadable_push*)\|(land_verify_failure*)\|(an_unknown_graph*)\|(locked_source*)\|(reland_of_*)\|(land_warns_*)\|(land_is_silent*)\|(a_held_land*)` | R-3 | 11 cases, 0 failed | 11 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | S1 | CP-1 | stage-outcome-budget | `/*/*/AgentTaskLandStageOutcomeTests/(C508_*)\|(retained_merge*)` | R-3 | 5 cases, 0 failed | 5 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | S1 | CP-1 | derived-alone | `/*/*/DerivedImpactIndexTests/*` | R-4 | 27 cases, 0 failed | 27 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | S1 | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-4 | 114 executed; the Linux skip of C1210_Windows_linux_only_outcomes_are_exact is expected | 114 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | S1 | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/Antiphon.LandGraphAudit.dll` | R-5 | exit 0, approved; the evaluator is built before this row | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | S1 | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 81add8595d60295b4bf1d3bb76af2e927842421f -HeadRef HEAD` | R-6 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | S1 | n/a | merge-tree | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-6 | exit 0 and a tree id | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
