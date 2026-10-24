# CARD-1540 Aspire metadata precheck repair

Code continuation d18e3e78. Assigned base 627dc2ccea873c8f539eab217209edafad73ea80;
original Code/landing owner 6c903205-b7d5-4bfe-8cf1-e9486eacb490.
The prior re-cut plan and original CARD-1540 plan own V-1..V-8, R-1..R-6
and PC-1..PC-6. This continuation replaces their checkpoint selection with the
closed list below: run CP-RED before repair and CP-1 after committed repair and
real-tool record regeneration. The explicit brief's exact 80-result Final
selection supersedes generic Unit/full-class scope; four new precheck witnesses
extend it to 84. No full assembly or unbounded class selection is requested.

The root refusal is the PowerShell precheck metadata-name allowlist, before
BuildInputPolicy, SolutionAudit or TargetPins runs. Admit only the reviewed
ProjectReference name IsAspireProjectResource. It controls Aspire application
resource registration, not project closure or reference globals. Preserve all
unknown-name refusals and all target/OS/environment pins unchanged. No separate
human approval record is required for a metadata-name allowlist source change;
no pin or digest is manually changed. New witnesses exercise both attribute and
child metadata through the real precheck and verify manifest publication/refusal.

Manual work: gated Release evaluator build, real audit -Update and -Check on the
committed candidate graph, commit generated records, fresh checkpoint qualification,
receipt validation, full original task-base dfebf941d7d343009bb819cea4d65bb3dca8fbcf..HEAD
evidence guard, and merge-tree against fetched current master without merging.
This author-time audit regenerates records for this master-based candidate, not
an edit of master or an unrelated graph. rc/2026-10-24 is master-based; no separate
rc pick, tag, release script or restart. Restart: none.

## Performance budget

CP-RED is a necessary defect witness: four isolated tiny Git fixtures, one test
build, provisional 7-minute total. Existing per-call deadline is unchanged; new
method timeout is 60s, provisional pending the first sample (CARD-1540).
CP-1 shares one Antiphon.Tests build and selects the existing 80 results plus
four new results. Prior actual total 620s (build 220s, row 312s); provisional
22-minute estimate, finite 30-minute execution budget. No timeout or assertion
is widened. No repetition after green. One shared DB startup with existing schema
isolation for C1380; tiny Git fixture repositories are the subject of audit tests.
Category git operations/server Integration. No browser/provider/AppHost startup.
Record durations, exact identities, build/slot/startup/body/teardown in ignored
.antiphon/checkpoints and .antiphon/c1540-aspire. Internal filesystem/DB and CLI
spans not emitted by existing fixtures remain unknown; unattributed time is remainder.
Necessary non-test prerequisite builds: checkpoint launcher (absent) and Release
evaluator for -Update, through build-slot.ps1 and unique bin-d18-* outputs; remove
owned alternate outputs afterward. Portable runner defaults/catalog are read,
without embedding placement or runner/platform pins.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-RED | Witness | `tests/Antiphon.Tests -> bin-d18-red/` | precheck-witness | `/*/*/LandGraphEvaluationTests*/C1540_ProjectReferenceMetadataPrecheck*` | reviewed attribute/child and unknown attribute/child | 2 assertion failures, 2 passes | 4 | 7 |
| CP-GREEN | Repair | `tests/Antiphon.Tests -> bin-d18-final/` | precheck-green | `/*/*/LandGraphEvaluationTests*/C1540_ProjectReferenceMetadataPrecheck*` | reviewed and unknown metadata forms | 4 passed, 0 failed/skipped | 4 | 1 |
| CP-1 | Repair | CP-GREEN | final-repair | `/*/*/(LandGraphEvaluationTests*)\|(LandNonCodeSkipTests*)/(C1483_S1_*)\|(C1483_S2_*)\|(C1483_S3_*)\|(C1483_S4_*)\|(C1483_S5_*)\|(C1528_*)\|(C1540_EnvironmentPropertyRefuses*)\|(C1540_TimestampMetadataRefuses*)\|(C1540_KnobGateNeedsPin*)\|(C1540_AdmittedNamesApprove*)\|(C1540_AdmittedVerdictIgnoresEnvironment*)\|(C1540_ExpressionSyntaxFailsClosed*)\|(C1385_RegistryMatchesNarrowGraph*)\|(C1380_DocsOnlyRebasedLandRecordsNonCodeSkip*)` | V-1..V-8 R-1..R-6 C1380 | all passed, 0 failed/skipped | 80 | 22 |

Run each committed group with --after Witness or Repair, --expected-source-sha
<committed HEAD>, --total-timeout 30m. Wait to terminal exit; inspect every fresh
method/argument and nonzero counts. No required V/R deferred to Final.

All PC-1..PC-6 variants remain pending for method-scoped SourceLanding Mutation,
including new metadata allowlist missing-control discovery (zero new PC rows does
not discharge Mutation). CP-W is informational native Windows work from the
original plan, pending, unrun. Ordinary Review is the stage contract's next step
when implementation and ordinary verification complete; the brief requests no
Review, which is recorded for caller reconciliation rather than bypassing the gate.
